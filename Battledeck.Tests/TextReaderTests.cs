using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Battledeck.Backend.Automation;
using Xunit;
using TextReader = Battledeck.Backend.Automation.TextReader;

namespace Battledeck.Tests
{
    public class TextReaderTests
    {
        private const int StripWidth = 400;

        // A word standing alone in a wide strip is read erratically at any size; a row of them is not.
        private const string TabBar = "SAMMLUNG     HELDEN     BEUTE     REPLAYS";

        [Theory]
        [InlineData(400, 45, 1, 1)]
        [InlineData(400, 40, 1, 1)]
        [InlineData(400, 39, 1, 2)]
        [InlineData(400, 34, 1, 2)]
        [InlineData(400, 30, 1, 2)]
        [InlineData(400, 20, 1, 2)]
        [InlineData(400, 19, 1, 3)]
        [InlineData(400, 10, 1, 4)]
        [InlineData(30, 400, 1, 2)]
        [InlineData(400, 15, 3, 3)]
        [InlineData(400, 12, 3, 4)]
        [InlineData(400, 80, 3, 3)]
        [InlineData(400, 0, 2, 2)]
        public void EffectiveUpscale_is_the_smallest_factor_that_reaches_the_minimum(
            int width,
            int height,
            int requested,
            int expected
        )
        {
            Assert.Equal(expected, TextReader.EffectiveUpscale(width, height, requested));
        }

        [Theory]
        [Trait("needs", "ocr")]
        [InlineData(20)]
        [InlineData(30)]
        [InlineData(34)]
        public async Task A_strip_lower_than_the_minimum_is_read_and_its_box_lies_in_the_crop(int stripHeight)
        {
            Assert.True(TextReader.Available, "No text recognition on this machine.");
            var shot = StripWith(TabBar, stripHeight);

            var lines = await TextReader.ReadAsync(shot, 0, 0, StripWidth, stripHeight);

            Assert.Equal("SAMMLUNG HELDEN BEUTE REPLAYS", string.Join(" ", lines.Select(l => l.Text)));
            foreach (var line in lines)
            {
                Assert.True(line.X >= 0 && line.Y >= 0, $"Box starts outside the crop: {line}");
                Assert.True(line.X + line.Width <= StripWidth, $"Box is wider than the crop: {line}");
                Assert.True(line.Y + line.Height <= stripHeight, $"Box is taller than the crop: {line}");
            }
        }

        private static Screenshot StripWith(string text, int height)
        {
            var pixels = new byte[StripWidth * height * 4];

            Sta.Run(() =>
            {
                var formatted = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    12,
                    Brushes.White,
                    1.0
                );

                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, StripWidth, height));
                    context.DrawText(formatted, new Point(20, Math.Round((height - formatted.Height) / 2)));
                }

                var target = new RenderTargetBitmap(
                    StripWidth,
                    height,
                    96,
                    96,
                    PixelFormats.Pbgra32
                );
                target.Render(visual);
                target.CopyPixels(pixels, StripWidth * 4, 0);
            });

            return Screenshot.FromBgra(pixels, StripWidth, height);
        }
    }
}
