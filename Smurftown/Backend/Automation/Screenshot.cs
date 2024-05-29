using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     A screen capture as a raw BGRA buffer.
    ///     <para>
    ///         Raw and not as <see cref="BitmapSource" />, because the recognition reads
    ///         point by point anyway - a WPF bitmap in between would only be one more copy.
    ///         For display and storage there are <see cref="ToBitmap" /> and
    ///         <see cref="SaveTo" />.
    ///     </para>
    ///     <para>
    ///         What is captured is the visible screen, not the window content. That means:
    ///         the game window must be in front, otherwise you photograph whatever stands in
    ///         front of it. <see cref="GameWindow.BringToFront" /> is therefore not an
    ///         accessory, but mandatory before every capture.
    ///     </para>
    /// </summary>
    public sealed class Screenshot
    {
        private const int BytesPerPixel = 4;

        private readonly byte[] _pixels;

        private Screenshot(byte[] pixels, int width, int height)
        {
            _pixels = pixels;
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>
        ///     Captures a section of the screen. The coordinates are screen points, not
        ///     window-relative.
        /// </summary>
        public static Screenshot Capture(int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Capture area is empty.");

            var screenDc = NativeMethods.GetDC(IntPtr.Zero);
            var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            var header = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,
                // Negative = rows top to bottom. Without this the image would stand on its
                // head, and every coordinate from the calibration would be mirrored.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB
            };

            var bitmap = IntPtr.Zero;
            var previous = IntPtr.Zero;
            try
            {
                bitmap = NativeMethods.CreateDIBSection(screenDc, ref header, NativeMethods.DIB_RGB_COLORS,
                    out var bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed.");

                previous = NativeMethods.SelectObject(memoryDc, bitmap);
                if (!NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y,
                        NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                    throw new InvalidOperationException("BitBlt failed.");

                var buffer = new byte[width * height * BytesPerPixel];
                System.Runtime.InteropServices.Marshal.Copy(bits, buffer, 0, buffer.Length);
                return new Screenshot(buffer, width, height);
            }
            finally
            {
                if (previous != IntPtr.Zero) NativeMethods.SelectObject(memoryDc, previous);
                if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
                NativeMethods.DeleteDC(memoryDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>Color of a point. Points outside deliver black.</summary>
        public (byte R, byte G, byte B) this[int x, int y]
        {
            get
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) return (0, 0, 0);
                var i = (y * Width + x) * BytesPerPixel;
                return (_pixels[i + 2], _pixels[i + 1], _pixels[i]);
            }
        }

        /// <summary>
        ///     Average color and average saturation of a circle. Saturation is the value
        ///     that distinguishes owned from unowned heroes in the game - desaturated
        ///     portraits sit close to zero.
        /// </summary>
        public (double Saturation, double Value) DiscAverage(int centerX, int centerY, int radius)
        {
            double sumSaturation = 0, sumValue = 0;
            var count = 0;
            var squared = radius * radius;
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > squared) continue;
                var (r, g, b) = this[centerX + dx, centerY + dy];
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                sumValue += max / 255.0;
                sumSaturation += max == 0 ? 0 : (max - min) / (double)max;
                count++;
            }

            return count == 0 ? (0, 0) : (sumSaturation / count, sumValue / count);
        }

        /// <summary>Cuts out an area. Points outside are made black.</summary>
        public Screenshot Crop(int x, int y, int width, int height)
        {
            var buffer = new byte[width * height * BytesPerPixel];
            for (var dy = 0; dy < height; dy++)
            for (var dx = 0; dx < width; dx++)
            {
                var (r, g, b) = this[x + dx, y + dy];
                var i = (dy * width + dx) * BytesPerPixel;
                buffer[i] = b;
                buffer[i + 1] = g;
                buffer[i + 2] = r;
                buffer[i + 3] = 255;
            }

            return new Screenshot(buffer, width, height);
        }

        /// <summary>
        ///     Mean deviation from a second capture within an area, in color steps (0..255).
        ///     This makes it possible to tell whether a screen has finished building without
        ///     needing to know what it looks like - two captures taken shortly after each
        ///     other differ clearly while it is still building and hardly at all afterward.
        /// </summary>
        public double MeanDifferenceTo(Screenshot other, int x, int y, int width, int height, int step = 3)
        {
            double sum = 0;
            var count = 0;
            for (var sy = y; sy < y + height; sy += step)
            for (var sx = x; sx < x + width; sx += step)
            {
                var (ar, ag, ab) = this[sx, sy];
                var (br, bg, bb) = other[sx, sy];
                sum += (Math.Abs(ar - br) + Math.Abs(ag - bg) + Math.Abs(ab - bb)) / 3.0;
                count++;
            }

            return count == 0 ? 0 : sum / count;
        }

        public BitmapSource ToBitmap()
        {
            var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null,
                _pixels, Width * BytesPerPixel);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        ///     Saves the capture as a PNG. Meant for the case that the flow strands on an
        ///     unknown screen: then the image is available instead of just a message in the
        ///     log.
        /// </summary>
        public void SaveTo(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(ToBitmap()));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
