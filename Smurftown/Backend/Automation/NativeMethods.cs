using System.Runtime.InteropServices;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     All Windows calls of the automation in one place.
    ///     <para>
    ///         Deliberately no additional package: screen capture and input need only
    ///         <c>user32</c> and <c>gdi32</c>, and WPF already brings everything needed to save
    ///         a capture with <c>PngBitmapEncoder</c>. <c>System.Drawing.Common</c> would be a
    ///         dependency for functions we need ourselves anyway.
    ///     </para>
    ///     <para>
    ///         Input runs over <c>SendInput</c> and not over <c>PostMessage</c>: the game reads
    ///         raw input, posted window messages do not arrive there.
    ///     </para>
    /// </summary>
    internal static class NativeMethods
    {
        // ---------------------------------------------------------------- Window

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
        }

        internal const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        /// <summary>
        ///     The content area without frame and title bar, with origin 0,0. Together with
        ///     <see cref="ClientToScreen" /> this is the only correct reference area: in
        ///     borderless fullscreen, frame and content are the same; in windowed mode there
        ///     are measured 8 points horizontally and 31 vertically in between. Whoever
        ///     calculates with <see cref="GetWindowRect" /> clicks off by exactly this amount
        ///     there.
        /// </summary>
        [DllImport("user32.dll")]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        internal static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int cmd);

        /// <summary>
        ///     Whether the window is minimized. Important because a minimized window still
        ///     affirms <see cref="IsWindowVisible" /> (WS_VISIBLE stays set), but its client
        ///     area measures 0x0.
        /// </summary>
        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        internal static extern bool AttachThreadInput(uint attachTo, uint attachFrom, bool attach);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        // ---------------------------------------------------------------- Input

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        /// <summary>
        ///     The union of <c>INPUT</c>. It must run through <see cref="LayoutKind.Explicit" />,
        ///     otherwise the size is wrong - and <c>SendInput</c> silently discards a struct
        ///     with the wrong size, without error and without effect. On x64 it is 40 bytes.
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        internal const uint INPUT_MOUSE = 0;
        internal const uint INPUT_KEYBOARD = 1;

        internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
        internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        internal const uint MOUSEEVENTF_WHEEL = 0x0800;

        /// <summary>One notch of the mouse wheel. Windows counts wheel rotation in multiples of this.</summary>
        internal const int WHEEL_DELTA = 120;

        internal const uint KEYEVENTF_KEYUP = 0x0002;
        internal const uint KEYEVENTF_UNICODE = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

        /// <summary>
        ///     Virtual key code to scan code (<c>MAPVK_VK_TO_VSC</c> = 0). Needed for keys that
        ///     must arrive in a GAME SCENE and not just in an input field: Blizzard's interface
        ///     evaluates the scan code there, an event with a bare virtual code does not even
        ///     arrive.
        /// </summary>
        [DllImport("user32.dll")]
        internal static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        internal static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT point);

        // ---------------------------------------------------------------- Capture

        [StructLayout(LayoutKind.Sequential)]
        internal struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        internal const uint BI_RGB = 0;
        internal const uint DIB_RGB_COLORS = 0;
        internal const uint SRCCOPY = 0x00CC0020;
        internal const uint CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateDIBSection(IntPtr hDC, ref BITMAPINFOHEADER header, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr hDC, IntPtr obj);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        internal static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height,
            IntPtr src, int srcX, int srcY, uint rop);
    }
}
