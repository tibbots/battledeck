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

        private static readonly string[] ProcessNames = ["HeroesOfTheStorm_x64", "HeroesOfTheStorm"];

        private GameWindow(Process process, IntPtr handle)
        {
            Process = process;
            Handle = handle;
        }

        public Process Process { get; }
        public IntPtr Handle { get; }

        public bool IsAlive => !Process.HasExited;

        /// <summary>
        ///     Whether the window is minimized. <b>Not the same as invisible</b>: a
        ///     minimized window keeps WS_VISIBLE, <see cref="Find" /> therefore accepts it -
        ///     but its <c>GetClientRect</c> returns 0x0.
        /// </summary>
        public bool IsMinimised => NativeMethods.IsIconic(Handle);

        /// <summary>
        ///     Brings a minimized window back. <b>With a fullscreen client this may
        ///     not help at all under some circumstances</b> - see <see cref="BringToFront" />.
        /// </summary>
        public void Restore()
        {
            if (IsMinimised) NativeMethods.ShowWindow(Handle, NativeMethods.SW_RESTORE);
        }

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
                    // Restore in EVERY round, not just once beforehand: a client can
                    // minimize itself again while waiting, and a single attempt
                    // right at the start would let the rest of the deadline run against a 0x0 window.
                    window.Restore();

                    var bounds = window.Bounds();

                    // Read AFTER Restore and after measuring: whether it is minimised NOW is
                    // the interesting half. A window that keeps saying "minimised" through
                    // every round did not come back, and one that says "no" while still
                    // measuring 160x28 is the invisible-proxy case.
                    seen = $"window 0x{window.Handle:X} is {bounds.Width}x{bounds.Height} " +
                           $"at {bounds.Left},{bounds.Top}, minimised={window.IsMinimised}";

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
        ///         As a tuple and not as a <c>RECT</c>: the struct lives in
        ///         <see cref="NativeMethods" /> and is <c>internal</c> - it must not appear in any
        ///         public signature.
        ///     </para>
        /// </summary>
        public (int Left, int Top, int Width, int Height) Bounds()
        {
            NativeMethods.GetClientRect(Handle, out var client);
            var origin = new NativeMethods.POINT { X = 0, Y = 0 };
            NativeMethods.ClientToScreen(Handle, ref origin);
            return (origin.X, origin.Y, client.Width, client.Height);
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

        /// <summary>Captures the entire window content, after the window has been brought to the front.</summary>
        public Screenshot Capture()
        {
            if (!BringToFront())
                throw new InvalidOperationException(
                    "Could not bring the game window to the front - a screenshot would be worthless.");

            var bounds = Bounds();

            // The size check belongs here and not in Screenshot.Capture: there it would be an
            // area check ("Capture area is empty"), here it is a statement about the
            // client. A client minimized from fullscreen reports 160x28 or 0x0, while
            // still being the foreground window - BringToFront above therefore harmlessly says "yes".
            if (bounds.Width < MinimumPlayableWidth || bounds.Height < MinimumPlayableHeight)
                throw new InvalidOperationException(
                    $"The Heroes of the Storm window collapsed to {bounds.Width}x{bounds.Height}. " +
                    "It was minimised and does not come back on its own - close the client and try again.");

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
