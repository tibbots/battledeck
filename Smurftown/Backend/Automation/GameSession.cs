using System.Diagnostics;
using System.IO;
using Serilog;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Texts;

namespace Smurftown.Backend.Automation
{
    /// <summary>Which screen is currently open, measured instead of assumed.</summary>
    public enum GameScreen
    {
        Unknown,
        Login,
        Menu,
        HeroSelect
    }

    /// <summary>
    ///     A game run for exactly one account: start, set region, sign in - and if
    ///     desired, continue clicking afterward to read off rank, heroes, and stats.
    ///     <para>
    ///         The start button and the data refresh are the same process with two exits. The
    ///         start button leaves the game open afterward, the data refresh ends it. There is
    ///         therefore no second set of coordinates and no second sign-in path.
    ///     </para>
    ///     <para>
    ///         Battle.net is no longer needed. The game brings its own login form
    ///         when started directly - which removes the launcher, the account switcher, and the
    ///         limit of five remembered sign-ins.
    ///     </para>
    /// </summary>
    public sealed class GameSession : IDisposable
    {
        private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(180);

        /// <summary>
        ///     How long an ALREADY RUNNING client gets to bring its window back to a
        ///     usable size. Noticeably shorter than <see cref="WindowTimeout" />:
        ///     the window already exists, the only wait is for the
        ///     restoration to take effect.
        /// </summary>
        private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan LoginScreenTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan MenuTimeout = TimeSpan.FromSeconds(150);

        /// <summary>Minimum wait time after the region change, before any measurement happens at all.</summary>
        private static readonly TimeSpan FormSettleMinimum = TimeSpan.FromSeconds(2.5);

        private static readonly TimeSpan FormSettleTimeout = TimeSpan.FromSeconds(20);

        /// <summary>
        ///     This is how long the login form is searched for before giving up.
        ///     <para>
        ///         Generous, because the wait costs nothing here: the form is found in
        ///         the second it appears. The value is the upper limit for the case
        ///         that it never comes at all - for example because the game does not get a connection.
        ///     </para>
        /// </summary>
        private static readonly TimeSpan LoginFormTimeout = TimeSpan.FromSeconds(120);

        /// <summary>
        ///     Interval between two measurement attempts in <see cref="Retry{T}" />. Same cadence
        ///     as in <see cref="WaitForScreen" />: every capture costs about 20 MB at 3440x1440, and
        ///     each one brings the game window to the front. Measuring faster would bring nothing - the
        ///     login form does not appear any more precisely within a second.
        /// </summary>
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1.5);

        /// <summary>
        ///     Below this window height, no work is done. Not a calibration value, but a
        ///     plausibility limit: the game does not run below 720 lines, and whatever is
        ///     reported below that is a loading screen or an error - not a small window.
        /// </summary>
        private const int MinimumHeight = 700;

        private readonly ScreenMap _map;

        private GameSession(GameWindow window, ScreenMap map)
        {
            Window = window;
            _map = map;
            var bounds = window.Bounds();
            Layout = map.LayoutFor(bounds.Width, bounds.Height);
        }

        public GameWindow Window { get; }

        /// <summary>The calibration of this session - so readers do not have to load it again.</summary>
        public ScreenMap Map => _map;

        /// <summary>
        ///     The ratio of this window to the calibration's reference size. Determined once at
        ///     the start: the size does not change during a run, and a window measured
        ///     anew at every click would only be another source of error.
        /// </summary>
        public Layout Layout { get; }

        public void Dispose()
        {
            Window.Close();
        }

        /// <summary>
        ///     Starts the game and signs the account in. Returns once the main menu
        ///     stands.
        /// </summary>
        /// <param name="gamePath">
        ///     Full path to <c>HeroesSwitcher_x64.exe</c>. Since 21.08.2026 it comes from
        ///     outside and no longer from <c>screen-map.yaml</c> - the calibration describes how the
        ///     game looks, not where it lies. Who sets it stands in the settings;
        ///     it is passed in here because <c>Backend/Automation</c> does not know the gateways.
        /// </param>
        /// <param name="region">
        ///     Which region to sign in with. Comes from outside and NOT from
        ///     <c>account.DefaultRegion</c>: an account can play in several regions, and if
        ///     the start entries later get one per region, this is already the
        ///     right direction here. Same direction as with the game path.
        /// </param>
        public static async Task<GameSession> StartAndLogin(BattlenetAccount account, string gamePath,
            BattlenetRegion region, IProgress<string>? progress = null,
            CancellationToken token = default)
        {
            var map = ScreenMap.Load();
            GameSession session;

            // A RUNNING CLIENT IS REUSED, not rejected. Until 21.08.2026
            // this aborted here ("close it first"), and every account switch cost a
            // full restart - when reading out 27 accounts, this is the most expensive item of the
            // whole process. The game can sign out without exiting.
            var running = GameWindow.Find();
            if (running != null)
            {
                // MINIMIZED COUNTS AS RUNNING, but is not measurable. A minimized window
                // keeps WS_VISIBLE - IsWindowVisible therefore still says "yes", and Find() accepts
                // it -, but its GetClientRect returns 0x0. Whoever measures immediately here
                // fails with "Game window is only 0x0" on a client that is completely
                // fine. Exactly this happened on 21.08.2026.
                //
                // BringToFront() calls ShowWindow(SW_RESTORE) and brings it back; afterward
                // the window needs a few moments before the content area has its size again.
                // The wait therefore uses the same tool as in the restart branch below,
                // just with a short deadline - the window is already there after all.
                progress?.Report(Strings.Current["progress.reusing"]);
                running.BringToFront();

                GameWindow restored;
                try
                {
                    restored = await GameWindow.WaitForPlayableWindow(RestoreTimeout, token);
                }
                catch (TimeoutException)
                {
                    // "No usable window after 15s" would be a false statement here: the
                    // window is there, it is just unusable - and permanently so. A client minimized
                    // from fullscreen does not come back from outside, that is
                    // documented on 21.08.2026 with three methods (see GameWindow.BringToFront).
                    // Whoever reads this message should know what THEY need to do.
                    throw new InvalidOperationException(
                        "A Heroes of the Storm client is running but its window is minimised, and a " +
                        "minimised full-screen client cannot be restored from outside. Bring it up " +
                        "yourself or close it, then try again.");
                }

                session = new GameSession(restored, map);
                session.RequireUsableSize();
                session.SignOut(progress);
            }
            else
            {
                if (gamePath.Length == 0)
                    throw new FileNotFoundException(
                        "Heroes of the Storm was not found. Choose the path in the settings.");

                if (!File.Exists(gamePath))
                    throw new FileNotFoundException(
                        $"Game not found: {gamePath}. Adjust the path in the settings.", gamePath);

                progress?.Report(Strings.Current["progress.starting"]);
                Log.Information("{Battletag}: starting {Path}", account.Battletag(), gamePath);
                Process.Start(new ProcessStartInfo { FileName = gamePath, UseShellExecute = true });

                var window = await GameWindow.WaitForPlayableWindow(WindowTimeout, token);
                session = new GameSession(window, map);
                session.RequireUsableSize();
            }

            // Two steps with different tasks, not a double wait: the first
            // checks via the brightness of the header bar that the game has left the
            // loading screen at all. The second searches for the form itself - and that comes noticeably
            // later, because a loading spinner stands in its place first.
            progress?.Report(Strings.Current["progress.waitLogin"]);
            await session.WaitForScreen(GameScreen.Login, LoginScreenTimeout, token);

            var form = await session.LocateLogin("before the region change", token);
            progress?.Report(Strings.Format("progress.region", region.DisplayName()));
            session.SelectRegion(form, region);

            // The region change rebuilds the form. Without this wait, the first
            // keystrokes land in nothing - observed: the email stayed empty, the password arrived, because
            // enough time had passed by then.
            progress?.Report(Strings.Current["progress.waitSettle"]);
            await session.WaitForStableForm(form, token);

            // Search again afterward: the form is rebuilt after the region change, and whether it
            // ends up in the same place is an assumption that can be cheaply avoided.
            form = await session.LocateLogin("after the region change", token);

            progress?.Report(Strings.Format("progress.signingIn", account.Battletag()));
            session.FillCredentials(account, form);

            progress?.Report(Strings.Current["progress.waitMenu"]);
            await session.WaitForScreen(GameScreen.Menu, MenuTimeout, token);

            progress?.Report(Strings.Current["progress.signedIn"]);
            Log.Information("{Battletag}: signed in", account.Battletag());
            return session;
        }

        /// <summary>
        ///     Checks whether the window is usable at all.
        ///     <para>
        ///         This used to abort on any size other than the calibrated one. That
        ///         is no longer necessary: the interface scales with the height, and the calibration
        ///         accounts for it. Measured against captures at 3440x1440, 2560x1080, and 1920x1080 -
        ///         at the same height, all spacings are the same, no matter how wide the window is.
        ///     </para>
        /// </summary>
        private void RequireUsableSize()
        {
            if (Layout.Height >= MinimumHeight)
            {
                Log.Information("Game window {Width}x{Height}, calibration {RefWidth}x{RefHeight}, factor {Scale:n3}",
                    Layout.Width, Layout.Height, _map.ReferenceWidth, _map.ReferenceHeight, Layout.Scale);
                return;
            }

            throw new InvalidOperationException(
                $"Game window is only {Layout.Width}x{Layout.Height} - too small to work with.");
        }

        public Screenshot Capture()
        {
            return Window.Capture();
        }

        /// <summary>Clicks a calibration point, converted to this window.</summary>
        public void Click(Spot spot, bool rightButton = false)
        {
            var (x, y) = Layout.Point(spot);
            ClickAt(x, y, rightButton);
        }

        /// <summary>Clicks a point in image coordinates of the window content.</summary>
        public void ClickAt(int x, int y, bool rightButton = false)
        {
            var bounds = Window.Bounds();
            InputSender.Click(bounds.Left + x, bounds.Top + y, rightButton);
        }

        /// <summary>
        ///     The space bar - the control key of the loot page.
        ///     <para>
        ///         Brings the window to the front itself, unlike <see cref="ClickAt" />: a click
        ///         goes to the point the cursor is over, whereas a key press always goes to
        ///         the foreground window. If another window were in front, the space bar would land
        ///         there - in the worst case in a foreign text field.
        ///     </para>
        /// </summary>
        public void PressSpace()
        {
            Window.BringToFront();
            InputSender.Space();
        }

        /// <summary>
        ///     Points at a spot without clicking - for hint windows that only appear on
        ///     hover. Deliberately does NOT bring the window to the front: a
        ///     focus change closes open lists, and the cursor alone triggers the hint
        ///     even when the window is not in front.
        /// </summary>
        public void HoverAt(int x, int y)
        {
            var bounds = Window.Bounds();
            InputSender.MoveTo(bounds.Left + x, bounds.Top + y);
        }

        public void ScrollAt(int x, int y, int notches)
        {
            var bounds = Window.Bounds();
            InputSender.Scroll(bounds.Left + x, bounds.Top + y, notches);
        }

        /// <summary>
        ///     Detects the screen by the brightness of the topmost strip: the navigation bar
        ///     of the menu is noticeably brighter than the login's starry sky, and the
        ///     hero select has no bar at all. Measured values are in the calibration file.
        /// </summary>
        public GameScreen ScreenOf(Screenshot shot)
        {
            var (x, y, width, height) = Layout.Area(_map.Detect.Strip);
            double sum = 0;
            var count = 0;
            for (var sy = y; sy < y + height; sy += 2)
            for (var sx = x; sx < x + width; sx += 4)
            {
                var (r, g, b) = shot[sx, sy];
                sum += Math.Max(r, Math.Max(g, b)) / 255.0;
                count++;
            }

            var brightness = count == 0 ? 0 : sum / count;
            if (brightness >= _map.Detect.MenuAbove) return GameScreen.Menu;
            if (brightness <= _map.Detect.HeroSelectBelow) return GameScreen.HeroSelect;
            return GameScreen.Login;
        }

        private async Task WaitForScreen(GameScreen wanted, TimeSpan timeout, CancellationToken token)
        {
            var deadline = DateTime.UtcNow + timeout;
            Screenshot? last = null;
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                if (!Window.IsAlive) throw new InvalidOperationException("The game has exited.");

                last = Capture();
                if (ScreenOf(last) == wanted) return;
                await Task.Delay(1500, token);
            }

            var path = SaveDiagnostic(last, $"no-{wanted}".ToLowerInvariant());
            throw new TimeoutException(
                $"Screen '{wanted}' did not appear within {timeout.TotalSeconds:n0}s. Screenshot: {path}");
        }

        /// <summary>
        ///     Searches for the login form in the image - for as long as it takes until it is there.
        ///     <para>
        ///         There used to be a single capture here. That was a logical error: before it,
        ///         <see cref="WaitForScreen" /> does wait, but it only checks the brightness of the
        ///         topmost bar, and that is already correct while a loading spinner is still
        ///         turning at the form's location. Measured on 20.08.2026: 16 seconds after the
        ///         window appeared, the brightness reported "Login", the capture showed the
        ///         loading spinner, and the process aborted.
        ///     </para>
        ///     <para>
        ///         If it is not found by the time it runs out, it aborts and
        ///         saves a capture - there is deliberately no fallback to fixed coordinates,
        ///         which would type the password somewhere.
        ///     </para>
        /// </summary>
        private async Task<LoginForm> LocateLogin(string when, CancellationToken token)
        {
            var reason = "never even searched";
            var (form, last) = await Retry<LoginForm>(
                shot => LoginLocator.Find(shot, _map, Layout, out reason),
                LoginFormTimeout, $"login form {when}", token);

            if (form != null) return form;

            var path = SaveDiagnostic(last, $"login-form-not-found-{when.Replace(' ', '-')}");
            throw new TimeoutException(
                $"Login form {when} not recognised: {reason}. " +
                $"Searched for {LoginFormTimeout.TotalSeconds:n0}s. Screenshot: {path}");
        }

        /// <summary>
        ///     Repeats a measurement on the image until it yields something or time runs out.
        ///     What comes back is the find and the last capture - the latter so the caller can
        ///     save evidence in case of failure.
        ///     <para>
        ///         <b>Difference to <see cref="WaitForStableArea(int,int,int,int,string,System.Threading.CancellationToken)" />:</b>
        ///         there the process waits until nothing moves anymore, here until something specific
        ///         is visible. Both are needed and neither replaces the other - a
        ///         loading spinner turns calmly on its own, and a form under construction is there, but
        ///         not yet in its final place.
        ///     </para>
        ///     <para>
        ///         The measurement function must not log anything: it runs at a one-second cadence, and
        ///         a warning per attempt buries the actual message.
        ///     </para>
        /// </summary>
        public Task<(T? Value, Screenshot? Last)> Retry<T>(Func<Screenshot, T?> read,
            TimeSpan timeout, string what, CancellationToken token) where T : class
        {
            return RetryAsync(shot => Task.FromResult(read(shot)), timeout, what, token);
        }

        /// <summary>
        ///     Like <see cref="Retry{T}" />, just for measurements that have to wait themselves - text
        ///     recognition, for instance. The loop only stands here; the other version passes through.
        /// </summary>
        public async Task<(T? Value, Screenshot? Last)> RetryAsync<T>(Func<Screenshot, Task<T?>> read,
            TimeSpan timeout, string what, CancellationToken token) where T : class
        {
            var started = DateTime.UtcNow;
            var deadline = started + timeout;
            Screenshot? last = null;
            var attempts = 0;

            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (!Window.IsAlive) throw new InvalidOperationException("The game has exited.");

                last = Capture();
                attempts++;

                var value = await read(last);
                if (value != null)
                {
                    if (attempts > 1)
                        Log.Information("{What}: found after {Attempts} attempts ({Seconds:n1}s)",
                            what, attempts, (DateTime.UtcNow - started).TotalSeconds);
                    return (value, last);
                }

                if (DateTime.UtcNow >= deadline)
                {
                    Log.Warning("{What}: not found after {Attempts} attempts", what, attempts);
                    return (null, last);
                }

                await Task.Delay(RetryInterval, token);
            }
        }

        /// <summary>
        ///     Sets the region to Europa. The game does not remember it - after every start
        ///     it again shows Amerika there, regardless of which region was last
        ///     signed in with. Checked: neither the registry nor Variables.txt change because of it.
        ///     <para>
        ///         Both clicks run unconditionally, even if Europa is already set - opening
        ///         and choosing the same entry is inconsequential, a state check
        ///         would only be another spot that could be wrong.
        ///     </para>
        /// </summary>
        /// <summary>
        ///     Sets the region. Since 21.08.2026 all three entries are calibrated;
        ///     before that there was only Europe, and everything else aborted before the game started.
        ///     <para>
        ///         <b>The region must be set anew on EVERY start</b> - the game does not remember
        ///         it, neither in the registry nor in <c>Variables.txt</c>. And it also falls
        ///         back to <c>Amerika</c> after every sign-out.
        ///     </para>
        ///     <para>
        ///         <b>Between opening and selecting, the window is NOT brought back to the front
        ///         again.</b> <c>SetForegroundWindow</c> closes the open list, and the
        ///         second click then lands on the background - the account signs in silently
        ///         on the wrong region, where it is usually empty. That is why
        ///         <c>BringToFront</c> stands once before it and not in <c>ClickAt</c>.
        ///     </para>
        /// </summary>
        /// <summary>
        ///     Signs the running client out, so the next account can sign in.
        ///     <para>
        ///         <b>Only from the main menu.</b> If the client is in a hero select or in
        ///         a game, it aborts instead of clicking - signing out mid
        ///         match costs the human a game and the account a deserter status.
        ///         If it is already on the login form, there is nothing to do.
        ///     </para>
        ///     <para>
        ///         <b>The window is brought to the front once before, not between the two
        ///         clicks.</b> <c>SetForegroundWindow</c> closes the opened menu -
        ///         the same trap as with the region selection, and here it would be more costly: 66 points
        ///         below "Ausloggen" lies "Spiel verlassen".
        ///     </para>
        ///     <para>
        ///         After signing out, the region is back on <c>Amerika</c>. This is not a
        ///         special case - the caller sets it anew at every sign-in anyway.
        ///     </para>
        /// </summary>
        private void SignOut(IProgress<string>? progress)
        {
            var screen = ScreenOf(Capture());
            if (screen == GameScreen.Login)
            {
                Log.Information("Running client is already signed out");
                return;
            }

            if (screen != GameScreen.Menu)
                throw new InvalidOperationException(
                    $"A Heroes of the Storm instance is running, but it shows {screen} instead of " +
                    "the main menu. Sign out or close it yourself.");

            progress?.Report(Strings.Current["progress.signingOut"]);
            Log.Information("Reusing the running client - signing out");

            Window.BringToFront();
            Click(_map.Menu.Gear);
            Thread.Sleep(700);
            Click(_map.Menu.Logout);
            Thread.Sleep(1500);
        }

        private void SelectRegion(LoginForm form, BattlenetRegion region)
        {
            Window.BringToFront();
            ClickAt(form.Region.X, form.Region.Y);
            Thread.Sleep(500);
            ClickAt(form.Region.X, form.Region.Y - Layout.Length(Map.Login.AboveFor(region)));
            Thread.Sleep(500);
        }

        /// <summary>
        ///     Waits until the area around the input fields no longer changes.
        ///     <para>
        ///         Measured instead of guessed: two captures taken shortly after each other differ
        ///         noticeably during construction and hardly afterward. A fixed wait time would be
        ///         either too short (and the input is lost) or unnecessarily long.
        ///     </para>
        /// </summary>
        private async Task WaitForStableForm(LoginForm form, CancellationToken token)
        {
            var pitch = Math.Max(form.Password.Y - form.Email.Y, Layout.Length(60));
            var x = Math.Max(0, form.Email.X - Layout.Length(340));
            var y = Math.Max(0, form.Email.Y - pitch);
            var width = Math.Min(Layout.Length(680), Layout.Width - x);
            var height = Math.Min(pitch * 5, Layout.Height - y);
            await WaitForStableArea(x, y, width, height, "login form", token);
        }

        /// <summary>
        ///     Waits until an area of the screen is calm.
        ///     <para>
        ///         If time runs out, it continues anyway: calmness is a help, not
        ///         proof. Whether the sign-in really worked is decided only by the
        ///         wait for the main menu anyway - and that saves a capture on failure.
        ///     </para>
        /// </summary>
        /// <param name="minimum">
        ///     How long to wait before the first measurement. Without a value, the one for the
        ///     login form applies; when paging through the collection, a fraction of that is enough, and there
        ///     every superfluous second is incurred per page.
        /// </param>
        public async Task WaitForStableArea(int x, int y, int width, int height, string name,
            CancellationToken token, TimeSpan? minimum = null)
        {
            await Task.Delay(minimum ?? FormSettleMinimum, token);

            var deadline = DateTime.UtcNow + FormSettleTimeout;
            var previous = Capture();
            var calmRounds = 0;
            var difference = double.NaN;
            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(600, token);

                var current = Capture();
                difference = current.MeanDifferenceTo(previous, x, y, width, height);
                previous = current;

                if (difference > _map.Detect.StableBelow)
                {
                    calmRounds = 0;
                    continue;
                }

                // Two calm rounds instead of one: a single one can also occur mid-
                // construction, when nothing is being redrawn at that moment.
                if (++calmRounds < 2) continue;
                Log.Information("{Name} is stable (deviation {Difference:n2})", name, difference);
                return;
            }

            // With the last measured value, not without: only that lets you tell afterward
            // whether the area was just slightly off or whether something stands in the measurement box
            // that never goes calm - the moving background between the cards, for example.
            Log.Warning("Area {Name} did not settle (last {Difference:n2}, limit " +
                        "{Limit:n2}) - continuing anyway.",
                name, difference, _map.Detect.StableBelow);
        }

        /// <summary>Waits until a calibration area is calm.</summary>
        public async Task WaitForStableArea(Spot spot, string name, CancellationToken token,
            TimeSpan? minimum = null)
        {
            var (x, y, width, height) = Layout.Area(spot);
            await WaitForStableArea(x, y, width, height, name, token, minimum);
        }

        private void FillCredentials(BattlenetAccount account, LoginForm form)
        {
            Window.BringToFront();

            // Clear both fields first: the form pre-fills the email address with the last
            // used account, and without clearing, the new one would stand behind the old.
            // InputSender.Pause instead of Thread.Sleep: these three wait times belong to typing
            // and must therefore hang on the same pace as everything else in the form.
            ClickAt(form.Email.X, form.Email.Y);
            InputSender.Pause(250);
            InputSender.ClearField();
            InputSender.Type(account.Email);

            ClickAt(form.Password.X, form.Password.Y);
            InputSender.Pause(250);
            InputSender.ClearField();
            InputSender.Type(account.Password);

            InputSender.Pause(400);

            // The enter key submits the form. The button would be the second option,
            // but its position is the only one on the form that is not measured but
            // carried forward from the field spacing - the key does without this calculation.
            InputSender.Enter();
        }

        /// <summary>
        ///     Saves a capture under <c>~/.smurftown/shots/</c>. A stranded process should
        ///     leave behind an image and not a guess - without that, it cannot be decided afterward
        ///     whether the calibration is outdated or the game was just slow.
        /// </summary>
        public static string SaveDiagnostic(Screenshot? shot, string reason)
        {
            if (shot == null) return "(no screenshot)";
            var directory = Path.Combine(Directories.UserPath, "shots");
            var path = Path.Combine(directory, $"{DateTime.Now:yyyy-MM-dd HH.mm.ss} {reason}.png");
            shot.SaveTo(path);
            Log.Warning("Screenshot saved: {Path}", path);
            return path;
        }
    }
}
