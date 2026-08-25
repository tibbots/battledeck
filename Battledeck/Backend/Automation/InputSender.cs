using System.Runtime.InteropServices;

namespace Battledeck.Backend.Automation
{
    /// <summary>
    ///     Mouse and keyboard input into the game.
    ///     <para>
    ///         Positioning happens via <c>SetCursorPos</c> instead of via the normalized
    ///         absolute coordinates of <c>SendInput</c>. The normalization runs across the entire
    ///         virtual screen; with two monitors that is an additional source of error
    ///         with no benefit. The key events themselves have to go via <c>SendInput</c>,
    ///         because the game reads raw input.
    ///     </para>
    ///     <para>
    ///         Text goes out as a Unicode event, not as a key code. That way the
    ///         keyboard layout does not matter - on a German keyboard the <c>@</c> sits on
    ///         AltGr+Q, and via key codes every email address would be its own special case.
    ///     </para>
    /// </summary>
    public static class InputSender
    {
        private const ushort VK_BACK = 0x08;
        private const ushort VK_TAB = 0x09;
        private const ushort VK_RETURN = 0x0D;
        private const ushort VK_ESCAPE = 0x1B;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_SPACE = 0x20;
        private const ushort VK_END = 0x23;
        private const ushort VK_DELETE = 0x2E;
        private const ushort VK_A = 0x41;

        /// <summary>
        ///     This many backspaces are sent by <see cref="ClearField" />. An email address is
        ///     noticeably shorter; the reserve only costs time, whereas too short a run leaves
        ///     remnants standing and the sign-in fails with an address made up of two.
        /// </summary>
        private const int ClearStrokes = 64;

        private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();

        /// <summary>Pause between two keystrokes. Without it, the game swallows characters.</summary>
        private const double KeyDelayMs = 18;

        /// <summary>
        ///     Factor applied to every pause in this class. 1.0 is the measured baseline value; smaller
        ///     means faster.
        ///     <para>
        ///         It is set by <c>SettingsGateway.Apply</c>, not fetched here:
        ///         <c>Backend/Automation</c> does not know the gateways, and this direction should
        ///         stay that way. Hence a public field instead of a lookup upward.
        ///     </para>
        /// </summary>
        public static double Pace = 1.0;

        public static void Click(int x, int y, bool rightButton = false)
        {
            NativeMethods.SetCursorPos(x, y);
            Pause(120);
            Send(Mouse(rightButton ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_LEFTDOWN));
            Pause(60);
            Send(Mouse(rightButton ? NativeMethods.MOUSEEVENTF_RIGHTUP : NativeMethods.MOUSEEVENTF_LEFTUP));
        }

        public static void MoveTo(int x, int y)
        {
            NativeMethods.SetCursorPos(x, y);
        }

        /// <summary>
        ///     Turns the mouse wheel at the given spot. Positive notches page upward.
        ///     <para>
        ///         Individually and with a pause instead of all at once: the game fades the
        ///         scroll in softly, and it partially swallows several notches within the same
        ///         millisecond.
        ///     </para>
        /// </summary>
        public static void Scroll(int x, int y, int notches)
        {
            NativeMethods.SetCursorPos(x, y);
            Pause(200);
            for (var i = 0; i < Math.Abs(notches); i++)
            {
                Send(Wheel(notches > 0 ? NativeMethods.WHEEL_DELTA : -NativeMethods.WHEEL_DELTA));
                Pause(120);
            }
        }

        public static void Type(string text)
        {
            foreach (var character in text)
            {
                Send(Key(0, character, NativeMethods.KEYEVENTF_UNICODE));
                Pause(KeyDelayMs);
                Send(Key(0, character, NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP));
                Pause(KeyDelayMs);
            }
        }

        public static void Tab()
        {
            Tap(VK_TAB);
        }

        public static void Enter()
        {
            Tap(VK_RETURN);
        }

        public static void Escape()
        {
            Tap(VK_ESCAPE);
        }

        /// <summary>
        ///     Space bar - the control key of the loot page: open, reveal all four cards,
        ///     accept.
        ///     <para>
        ///         <b>With scancode and not just with the virtual code</b>, unlike
        ///         <see cref="Enter" /> and <see cref="Escape" />. Those two go to the
        ///         login form, i.e. to an ordinary input field; the space bar goes to
        ///         a game scene, and that evaluates the scancode. Measured, not assumed -
        ///         the measurement run on 20.08.2026 went via exactly this route.
        ///     </para>
        /// </summary>
        public static void Space()
        {
            var scan = (ushort)NativeMethods.MapVirtualKey(VK_SPACE, 0);
            Send(Key(VK_SPACE, scan, 0));
            Pause(60);
            Send(Key(VK_SPACE, scan, NativeMethods.KEYEVENTF_KEYUP));
        }

        /// <summary>Ctrl+A. Only still used as a preliminary step in <see cref="ClearField" />.</summary>
        public static void SelectAll()
        {
            Send(Key(VK_CONTROL, 0, 0));
            Pause(30);
            Send(Key(VK_A, 0, 0));
            Pause(40);
            Send(Key(VK_A, 0, NativeMethods.KEYEVENTF_KEYUP));
            Send(Key(VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP));
            Pause(60);
        }

        /// <summary>
        ///     Clears a text field in which something already stands - the login form pre-fills the
        ///     email address with the last used account.
        ///     <para>
        ///         Ctrl+A alone is not enough: whether the form evaluates it at all is not
        ///         guaranteed, and if not, the new address gets typed behind the old one. That is why it
        ///         is then cleared hard with End and a series of backspaces - that gets by without
        ///         any assumption about which keyboard shortcuts the field understands.
        ///     </para>
        /// </summary>
        public static void ClearField()
        {
            SelectAll();
            Tap(VK_DELETE);
            Pause(40);
            Tap(VK_END);

            // The 64 backspaces go out in ONE SendInput call instead of in 64 individual ones with
            // 40 ms hold time each. This was the most expensive item of the whole sign-in: this alone
            // took 2.56 s per field, so over five seconds for email and password combined.
            // An input field evaluates backspaces in order and needs no
            // pause in between - unlike a game scene, see Space().
            var strokes = new NativeMethods.INPUT[ClearStrokes * 2];
            for (var i = 0; i < ClearStrokes; i++)
            {
                strokes[i * 2] = Key(VK_BACK, 0, 0);
                strokes[i * 2 + 1] = Key(VK_BACK, 0, NativeMethods.KEYEVENTF_KEYUP);
            }

            SendMany(strokes);
            Pause(80);
        }

        private static void Tap(ushort virtualKey)
        {
            Send(Key(virtualKey, 0, 0));
            Pause(40);
            Send(Key(virtualKey, 0, NativeMethods.KEYEVENTF_KEYUP));
        }

        private static NativeMethods.INPUT Mouse(uint flags)
        {
            return new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dwFlags = flags } }
            };
        }

        private static NativeMethods.INPUT Wheel(int amount)
        {
            return new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT
                    {
                        dwFlags = NativeMethods.MOUSEEVENTF_WHEEL,
                        mouseData = unchecked((uint)amount)
                    }
                }
            };
        }

        /// <summary>
        ///     <paramref name="scan" /> is <c>ushort</c> and not <c>char</c>: that is what the field
        ///     is called in <c>KEYBDINPUT</c>, and otherwise the calls with <c>0</c> would not go through -
        ///     there is no implicit constant conversion from <c>int</c> to <c>char</c>, but from
        ///     <c>char</c> to <c>ushort</c> there is.
        /// </summary>
        private static NativeMethods.INPUT Key(ushort virtualKey, ushort scan, uint flags)
        {
            return new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT { wVk = virtualKey, wScan = scan, dwFlags = flags }
                }
            };
        }

        private static void Send(NativeMethods.INPUT input)
        {
            if (NativeMethods.SendInput(1, [input], InputSize) == 0)
                throw new InvalidOperationException(
                    $"SendInput sent nothing (error {Marshal.GetLastWin32Error()}).");
        }

        /// <summary>
        ///     Several events in one call. <c>SendInput</c> takes a whole array, and
        ///     the events within it arrive without a gap - no foreign event can
        ///     insert itself in between, and it costs one transition into the kernel instead of N.
        /// </summary>
        private static void SendMany(NativeMethods.INPUT[] inputs)
        {
            if (inputs.Length == 0) return;
            if (NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize) == 0)
                throw new InvalidOperationException(
                    $"SendInput sent nothing (error {Marshal.GetLastWin32Error()}).");
        }

        /// <summary>
        ///     A pause, stretched or compressed by <see cref="Pace" />. Every fixed number in
        ///     this class runs through here - a setting that misses half the pauses
        ///     would be worse than none.
        /// </summary>
        public static void Pause(double milliseconds)
        {
            var scaled = (int)Math.Round(milliseconds * Pace);
            if (scaled > 0) Thread.Sleep(scaled);
        }
    }
}
