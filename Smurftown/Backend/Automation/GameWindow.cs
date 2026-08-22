using System.Diagnostics;
using Serilog;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     The window of the running game: find, bring to the front, measure.
    ///     <para>
    ///         The game process is not named like the started program. What is started is
    ///         <c>HeroesSwitcher_x64.exe</c>, which only picks the matching version and
    ///         exits; the window afterward belongs to <c>HeroesOfTheStorm_x64</c>. Whoever
    ///         waits on the start process is waiting on the wrong one.
    ///     </para>
    /// </summary>
    public sealed class GameWindow
    {
        /// <summary>The first window is a small loading screen - only afterward does the game come.</summary>
        private const int MinimumPlayableWidth = 1000;

        private const int MinimumPlayableHeight = 600;

        /// <summary>
        ///     How long a single click or capture waits for the client to have a picture
        ///     again. Short, because this is not the wait for a start - the client is up, it
        ///     just lost the display for a moment.
        /// </summary>
        private static readonly TimeSpan PictureTimeout = TimeSpan.FromSeconds(5);

        private static readonly string[] ProcessNames = ["HeroesOfTheStorm_x64", "HeroesOfTheStorm"];

        private GameWindow(Process process, IntPtr handle)
        {
            Process = process;
            Handle = handle;
        }

        public Process Process { get; }

        /// <summary>
        ///     The window that takes the <b>foreground, the focus and the input</b> - the one
        ///     with class <c>Heroes of the Storm</c>.
        ///     <para>
        ///         <b>It is not necessarily the one carrying the picture.</b> That is a second
        ///         question with a second answer, and <see cref="Bounds" /> asks it fresh every
        ///         time.
        ///     </para>
        /// </summary>
        public IntPtr Handle { get; }

        public bool IsAlive => !Process.HasExited;

        /// <summary>
        ///     Whether the window is minimized. <b>Not the same as invisible</b>: a
        ///     minimized window keeps WS_VISIBLE, <see cref="Find" /> therefore accepts it -
        ///     but its <c>GetClientRect</c> returns 0x0.
        /// </summary>
        public bool IsMinimised => NativeMethods.IsIconic(Handle);

        /// <summary>
        ///     Whether a client is running at all - <b>without</b> building a
        ///     <see cref="GameWindow" /> for it.
        ///     <para>
        ///         That is the whole difference to <see cref="Find" />, and it is the reason this
        ///         exists: the header chip asks this question every few seconds, and every
        ///         <see cref="Process" /> that <c>Find</c> hands out keeps an OS handle alive.
        ///         One leaked handle every three seconds is a handle leak measured in hours.
        ///     </para>
        ///     <para>
        ///         <b>It touches the window itself with nothing.</b> No capture, no
        ///         <c>BringToFront</c> - a poll that steals the focus every three seconds would take
        ///         the machine away from whoever is playing on it.
        ///     </para>
        /// </summary>
        public static bool IsRunning()
        {
            foreach (var name in ProcessNames)
            {
                var processes = Process.GetProcessesByName(name);
                try
                {
                    foreach (var process in processes)
                    {
                        process.Refresh();
                        if (process.MainWindowHandle != IntPtr.Zero &&
                            NativeMethods.IsWindowVisible(process.MainWindowHandle))
                            return true;
                    }
                }
                finally
                {
                    foreach (var process in processes) process.Dispose();
                }
            }

            return false;
        }

        /// <summary>
        ///     Whether a client is running, <b>holds the foreground</b> and has its picture -
        ///     the one state in which it can be driven from outside.
        ///     <para>
        ///         <b>This is the question the whole feature turned out to hang on.</b> Measured
        ///         on 23.08.2026: a client in exclusive full screen that does not hold the front
        ///         renders through an invisible <c>D3DProxyWindow</c> while its INPUT window
        ///         stays a 160x28 placeholder. Captures still work - they only need a rectangle
        ///         off the screen - but a click at picture coordinates goes to wherever that
        ///         point sits on the DESKTOP, which is not the game. So reading appears to work
        ///         while nothing can be clicked, and that is the shape the bug had.
        ///     </para>
        ///     <para>
        ///         <b>Both halves, and neither alone.</b> Foreground without a picture is the
        ///         collapsed client above; a picture without the foreground is a client about to
        ///         lose it. Only together do they mean "clicks will land".
        ///     </para>
        ///     <para>
        ///         It disposes the process it enumerated, like <see cref="IsRunning" /> and
        ///         unlike <see cref="Find" />: the run guide asks this three times a second while
        ///         it waits, and one leaked handle per ask adds up fast.
        ///     </para>
        /// </summary>
        public static bool IsForemostAndPlayable()
        {
            var window = Find();
            if (window == null) return false;

            try
            {
                // The foreground window is not necessarily the one Find handed out - in full
                // screen it can be a sibling of the same process. The PROCESS is the question,
                // not the handle.
                NativeMethods.GetWindowProcessId(NativeMethods.GetForegroundWindow(), out var front);
                if (front != (uint)window.Process.Id) return false;

                var bounds = window.Bounds();
                return bounds.Width >= MinimumPlayableWidth && bounds.Height >= MinimumPlayableHeight;
            }
            finally
            {
                window.Process.Dispose();
            }
        }

        public static GameWindow? Find()
        {
            foreach (var name in ProcessNames)
            foreach (var process in Process.GetProcessesByName(name))
            {
                process.Refresh();
                if (process.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindowVisible(process.MainWindowHandle))
                    return new GameWindow(process, process.MainWindowHandle);
            }

            return null;
        }

        /// <summary>
        ///     Waits until the game window has its full size. The loading screen appears after
        ///     a few seconds, the actual window noticeably later - therefore it is not enough
        ///     to wait for just any window.
        ///     <para>
        ///         <b>On failure it says what it saw, and that is not decoration.</b> This used
        ///         to time out with "No usable game window after 15s" and nothing else, so
        ///         every caller that wanted to explain the failure had to <i>assert</i> a cause
        ///         it had never measured - "the window is minimised" was a guess that happened
        ///         to be right often. The last observation now travels in the message and into
        ///         the log: handle, minimised or not, and the size that was measured. A client
        ///         reporting 160x28 is a different fault from one reporting nothing at all,
        ///         and the two want different sentences.
        ///     </para>
        /// </summary>
        public static async Task<GameWindow> WaitForPlayableWindow(TimeSpan timeout, CancellationToken token = default)
        {
            var deadline = DateTime.UtcNow + timeout;
            var seen = "no window found at all";

            while (DateTime.UtcNow < deadline)
            {
                token.ThrowIfCancellationRequested();
                var window = Find();
                if (window == null)
                {
                    seen = "no window found at all";
                }
                else
                {
                    // THE FOREGROUND IN EVERY ROUND, and that is the point of this loop.
                    //
                    // Until 23.08.2026 this called Restore(), which only fires SW_RESTORE
                    // while the window is iconic. Measured against a client taken over from
                    // outside: it leaves the minimised state in the FIRST round and then sits
                    // at 160x28 - so Restore did nothing for the remaining twenty-nine rounds
                    // while the loop measured the same collapsed window over and over and
                    // reported "minimised=False" at the end.
                    //
                    // A client in exclusive full screen rebuilds its picture only while it
                    // holds the FOREGROUND, and one attempt before the loop is not enough:
                    // whoever clicked has just made Smurftown the foreground application, and
                    // anything that takes the front back collapses the client again. Measured
                    // the same day: the very same handle reports 3440x1440 at 0,0 while the
                    // game is up, and 160x28 at 1640,706 while it is not.
                    //
                    // It costs nothing in the start flow: BringToFront returns straight away
                    // when the window is already in front and not minimised, without its
                    // 400 ms settle.
                    var front = window.BringToFront();

                    var bounds = window.Bounds();

                    // Measured AFTER the attempt, because whether it is in front and
                    // un-minimised NOW is the interesting half. A window that says
                    // "foreground=False" every round never got the handover; one that says
                    // "True" and still measures 160x28 got it and did not use it.
                    seen = $"window 0x{window.Handle:X} is {bounds.Width}x{bounds.Height} " +
                           $"at {bounds.Left},{bounds.Top}, minimised={window.IsMinimised}, " +
                           $"foreground={front}";

                    if (bounds.Width >= MinimumPlayableWidth && bounds.Height >= MinimumPlayableHeight)
                    {
                        Log.Information("Game window ready: {Width}x{Height} at {Left},{Top}",
                            bounds.Width, bounds.Height, bounds.Left, bounds.Top);
                        return window;
                    }
                }

                // Debug and not Information: this runs twice a second, and at Information a
                // single failed wait would bury the run it belongs to under thirty lines.
                Log.Debug("Waiting for a usable game window - {Seen}", seen);
                await Task.Delay(500, token);
            }

            Log.Warning("No usable game window after {Seconds:n0}s - last seen: {Seen}",
                timeout.TotalSeconds, seen);
            throw new TimeoutException(
                $"No usable game window after {timeout.TotalSeconds:n0}s - last seen: {seen}.");
        }

        /// <summary>
        ///     Position and size of the <b>content area</b> in screen points - not of the
        ///     window frame.
        ///     <para>
        ///         The difference is zero in borderless fullscreen and, in windowed mode, a measured
        ///         8 points horizontally and 31 vertically. Since all calibration coordinates are relative
        ///         to the game's image area, the frame would be the wrong reference area:
        ///         every click would be off by the title bar, and every capture would have it in the
        ///         picture.
        ///     </para>
        ///     <para>
        ///         <b>It measures the window with the picture, which is not always
        ///         <see cref="Handle" />.</b> See <see cref="PictureWindow" /> - that is the whole
        ///         reason this is not two lines any more.
        ///     </para>
        ///     <para>
        ///         As a tuple and not as a <c>RECT</c>: the struct lives in
        ///         <see cref="NativeMethods" /> and is <c>internal</c> - it must not appear in any
        ///         public signature.
        ///     </para>
        /// </summary>
        public (int Left, int Top, int Width, int Height) Bounds()
        {
            return ClientBounds(PictureWindow());
        }

        /// <summary>
        ///     The window of this process with the <b>largest client area</b> - the one the
        ///     picture is on.
        ///     <para>
        ///         <b>Measured on 23.08.2026, and it cost three evenings.</b> A client taken over
        ///         from outside comes back out of the minimised state, takes the foreground and
        ///         still reports 160x28 on <c>Process.MainWindowHandle</c>, centred on the screen.
        ///         Next to it, in the same process, sits a <c>D3DProxyWindow</c> at 3440x1440 at
        ///         0,0 - and that is where the picture is. The old reading, that the proxy was an
        ///         unreachable hiding place, had it backwards: it is not hiding anything, it is
        ///         the window that has it.
        ///     </para>
        ///     <para>
        ///         <b>And it is reachable</b>, despite <c>IsWindowVisible</c> saying no:
        ///         <see cref="Screenshot.Capture" /> blits from the SCREEN device context with
        ///         absolute coordinates. It needs a rectangle, not a window - whoever owns the
        ///         pixels is beside the point as long as they are on the display.
        ///     </para>
        ///     <para>
        ///         <b>Resolved fresh on every call, never cached.</b> The proxy is created anew
        ///         with every acquisition of the display - measured 0x1050BCC and 0x9A0AAE within
        ///         the same hour, and gone entirely while the client is collapsed. A field
        ///         holding it would be a stale handle within a minute.
        ///     </para>
        ///     <para>
        ///         In every ordinary state the largest window IS <see cref="Handle" /> - during
        ///         startup, in windowed mode, in borderless full screen. The rule therefore
        ///         changes nothing about the flow that always worked; it only stops the one that
        ///         never did from measuring a placeholder.
        ///     </para>
        /// </summary>
        private IntPtr PictureWindow()
        {
            var best = Handle;
            var bestArea = Area(ClientBounds(Handle));

            foreach (var candidate in TopLevelWindowsOf(Process.Id))
            {
                if (candidate == Handle) continue;

                var area = Area(ClientBounds(candidate));
                if (area <= bestArea) continue;

                best = candidate;
                bestArea = area;
            }

            // Only when it is NOT the main window: in the normal case this would be a line per
            // capture, and there are hundreds of those in a collection run.
            if (best != Handle)
                Log.Debug("The picture is on 0x{Picture:X}, not on the main window 0x{Main:X}",
                    best, Handle);

            return best;
        }

        private static long Area((int Left, int Top, int Width, int Height) bounds)
        {
            return (long)bounds.Width * bounds.Height;
        }

        private static (int Left, int Top, int Width, int Height) ClientBounds(IntPtr handle)
        {
            NativeMethods.GetClientRect(handle, out var client);
            var origin = new NativeMethods.POINT { X = 0, Y = 0 };
            NativeMethods.ClientToScreen(handle, ref origin);
            return (origin.X, origin.Y, client.Width, client.Height);
        }

        /// <summary>
        ///     Every top-level window belonging to a process.
        ///     <para>
        ///         <c>EnumWindows</c> and not <c>Process.MainWindowHandle</c>, because that hands
        ///         out exactly one and picks it by a rule of its own - which window a process
        ///         calls its main one is not the same question as which one has the picture.
        ///     </para>
        /// </summary>
        private static List<IntPtr> TopLevelWindowsOf(int processId)
        {
            var windows = new List<IntPtr>();

            NativeMethods.EnumWindows((handle, _) =>
            {
                NativeMethods.GetWindowProcessId(handle, out var owner);
                if (owner == (uint)processId) windows.Add(handle);
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        /// <summary>
        ///     Brings the window to the front. Without this, <see cref="Screenshot.Capture" /> captures whatever
        ///     is currently in front - in doubt an editor, and the recognition then interprets window content
        ///     as a game screen.
        ///     <para>
        ///         <c>SetForegroundWindow</c> fails silently if the calling thread is not attached to
        ///         the foreground. Hence the detour via <c>AttachThreadInput</c>.
        ///     </para>
        /// </summary>
        /// <remarks>
        ///     <b>"Front" does not mean "usable".</b> Measured on 21.08.2026: a client minimized
        ///     from fullscreen can be the foreground window and still report a
        ///     client area of 160x28 - the placeholder size of a minimized
        ///     window. Three methods did not bring it back: <c>SW_RESTORE</c> alone,
        ///     additionally <c>BringWindowToTop</c> + <c>SetForegroundWindow</c>, and both
        ///     plus a tapped ALT against the foreground lock. The real image sat in
        ///     an invisible <c>D3DProxyWindow</c> (3440x1440), which cannot be
        ///     reached. Whoever reads the return value as "now I can capture" is
        ///     mistaken - <see cref="Capture" /> therefore additionally checks the size.
        ///     <para>
        ///         <b>That proxy window is not a way in, and it is not the cause either</b> -
        ///         measured on 23.08.2026 by enumerating every top-level window of the process
        ///         in both states. While the client is collapsed there is <i>exactly one</i>
        ///         window, class <c>Heroes of the Storm</c>, and no proxy at all. The proxy
        ///         appears only while the client holds the display - i.e. in the state in which
        ///         the ordinary window already reports its full 3440x1440 and nothing is wrong.
        ///         Whoever goes looking for a second window to capture is chasing a thing that
        ///         is only there when it is not needed.
        ///     </para>
        /// </remarks>
        public bool BringToFront()
        {
            var foreground = NativeMethods.GetForegroundWindow();

            // Do NOT exit prematurely with a minimized window: it can very well already
            // be the foreground window, and then SW_RESTORE further below would remain uncalled.
            if (foreground == Handle && !IsMinimised) return true;

            var foreignThread = NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
            var ownThread = NativeMethods.GetCurrentThreadId();

            var attached = foreignThread != ownThread && NativeMethods.AttachThreadInput(ownThread, foreignThread, true);
            try
            {
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_RESTORE);
                NativeMethods.BringWindowToTop(Handle);
                NativeMethods.SetForegroundWindow(Handle);
            }
            finally
            {
                if (attached) NativeMethods.AttachThreadInput(ownThread, foreignThread, false);
            }

            Thread.Sleep(400);
            return NativeMethods.GetForegroundWindow() == Handle;
        }

        /// <summary>
        ///     The bounds of the picture - but only once there IS one. Brings the client to the
        ///     front and keeps at it until it measures playable again.
        ///     <para>
        ///         <b>Every click and every capture goes through here</b>, and that is the point.
        ///         Until 23.08.2026 only the capture checked the size; <c>GameSession.ClickAt</c>
        ///         took whatever <see cref="Bounds" /> happened to say and added the calibrated
        ///         offset to it. With a collapsed client that reads 160x28 at 1640,706, so a click
        ///         meant for 3371,74 was sent to 5011,780 - off the screen, into nothing. The
        ///         client then flickers up and down and nothing else happens, which is exactly
        ///         what it looked like.
        ///     </para>
        ///     <para>
        ///         <b>Waiting and not throwing straight away</b>, because a client in exclusive
        ///         full screen needs a moment after every handover: it has to reacquire the
        ///         display, and until it has, its window is a placeholder. One
        ///         <c>BringToFront</c> plus a glance was too little.
        ///     </para>
        ///     <para>
        ///         The size check belongs here and not in <see cref="Screenshot.Capture" />:
        ///         there it would be an area check ("Capture area is empty"), here it is a
        ///         statement about the client.
        ///     </para>
        /// </summary>
        public (int Left, int Top, int Width, int Height) RequirePlayableBounds()
        {
            var deadline = DateTime.UtcNow + PictureTimeout;

            while (true)
            {
                var front = BringToFront();
                var bounds = Bounds();

                if (bounds.Width >= MinimumPlayableWidth && bounds.Height >= MinimumPlayableHeight)
                    return bounds;

                if (DateTime.UtcNow >= deadline)
                    throw new InvalidOperationException(
                        $"The Heroes of the Storm window is {bounds.Width}x{bounds.Height} at " +
                        $"{bounds.Left},{bounds.Top} (foreground={front}) and does not come back " +
                        $"within {PictureTimeout.TotalSeconds:n0}s. A client in exclusive full " +
                        "screen drops its picture whenever it loses the front - switch it to " +
                        "borderless full screen (displaymode=1 in Variables.txt), or start the " +
                        "account from its row instead.");

                Thread.Sleep(250);
            }
        }

        /// <summary>Captures the entire window content, once the window has a picture again.</summary>
        public Screenshot Capture()
        {
            var bounds = RequirePlayableBounds();
            return Screenshot.Capture(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }

        public void Close()
        {
            if (Process.HasExited) return;
            Log.Information("Killing game process {Pid}", Process.Id);
            Process.Kill();
            Process.WaitForExit(10_000);
        }
    }
}
