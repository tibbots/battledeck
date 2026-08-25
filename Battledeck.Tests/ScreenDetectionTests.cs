using Battledeck.Backend.Automation;
using Xunit;

namespace Battledeck.Tests
{
    /// <summary>
    ///     Pins <c>GameSession.ScreenOf</c> against the real, shipped calibration -
    ///     <c>screen-map.yaml</c>'s <c>menuAbove</c> (0.160) and <c>heroSelectBelow</c> (0.085),
    ///     and the three brightness values measured on the running client and recorded in that
    ///     file's comment (login 0.109, main menu 0.216, hero select 0.064).
    ///     <para>
    ///         <b>Written as the baseline before <c>SignOut</c> gets a retry loop</b> (24.08.2026):
    ///         the classifier itself was never the suspect - a single capture taken too early
    ///         was - and these tests are what proves that. They must stay green, unchanged,
    ///         through that change; if they do not, the retry touched more than timing.
    ///     </para>
    ///     <para>
    ///         Uses the <c>internal static</c> overload of <c>ScreenOf</c> that takes
    ///         <see cref="Layout" /> and <see cref="ScreenMap" /> explicitly - the instance
    ///         overload needs a live, playable <see cref="GameWindow" /> just to exist, which a
    ///         unit test has no business starting. A solid-color <see cref="Screenshot" /> is
    ///         enough: the classifier reads the brightest of the three channels per pixel and
    ///         cannot tell that fill apart from a real capture of the same brightness.
    ///     </para>
    ///     <para>
    ///         <b>The window in <see cref="Layout" /> is 400x100 with a matching reference
    ///         size</b> - scale 1.0, chosen only so the numbers stay easy to follow. It has
    ///         nothing to do with any real resolution: on a solid-color image every sampled
    ///         pixel is identical, so the strip's actual size cannot change the result.
    ///     </para>
    /// </summary>
    public class ScreenDetectionTests
    {
        private static readonly ScreenMap Map = ScreenMap.Load();
        private static readonly Layout Layout = new(400, 100, 400, 100);

        /// <summary>Builds a solid-color capture wide and tall enough to cover the strip.</summary>
        private static Screenshot Solid(byte r, byte g, byte b)
        {
            return Screenshot.Solid(400, 100, r, g, b);
        }

        [Fact]
        public void Main_menu_reference_brightness_reads_as_Menu()
        {
            // 56/255 = 0.220 - close to the 0.216 measured on the running client.
            var shot = Solid(56, 56, 56);
            Assert.Equal(GameScreen.Menu, GameSession.ScreenOf(shot, Layout, Map));
        }

        [Fact]
        public void Login_reference_brightness_reads_as_Login()
        {
            // 28/255 = 0.110 - close to the 0.109 measured on the running client.
            var shot = Solid(28, 28, 28);
            Assert.Equal(GameScreen.Login, GameSession.ScreenOf(shot, Layout, Map));
        }

        [Fact]
        public void HeroSelect_reference_brightness_reads_as_HeroSelect()
        {
            // 16/255 = 0.063 - close to the 0.064 measured on the running client.
            var shot = Solid(16, 16, 16);
            Assert.Equal(GameScreen.HeroSelect, GameSession.ScreenOf(shot, Layout, Map));
        }

        [Fact]
        public void Brightness_at_the_MenuAbove_threshold_reads_as_Menu()
        {
            // 41/255 = 0.1608 - the smallest byte value the ">=" comparison against
            // menuAbove (0.160) still accepts.
            var shot = Solid(41, 41, 41);
            Assert.Equal(GameScreen.Menu, GameSession.ScreenOf(shot, Layout, Map));
        }

        [Fact]
        public void Brightness_one_step_below_MenuAbove_reads_as_Login()
        {
            // 40/255 = 0.1569 - one byte step under the threshold above.
            var shot = Solid(40, 40, 40);
            Assert.Equal(GameScreen.Login, GameSession.ScreenOf(shot, Layout, Map));
        }

        [Fact]
        public void Brightness_at_the_HeroSelectBelow_threshold_reads_as_HeroSelect()
        {
            // 21/255 = 0.0824 - the largest byte value the "<=" comparison against
            // heroSelectBelow (0.085) still accepts.
            var shot = Solid(21, 21, 21);
            Assert.Equal(GameScreen.HeroSelect, GameSession.ScreenOf(shot, Layout, Map));
        }

        [Fact]
        public void Brightness_one_step_above_HeroSelectBelow_reads_as_Login()
        {
            // 22/255 = 0.0863 - one byte step over the threshold above.
            var shot = Solid(22, 22, 22);
            Assert.Equal(GameScreen.Login, GameSession.ScreenOf(shot, Layout, Map));
        }

        /// <summary>
        ///     Locks in today's behavior rather than the intent behind it, which the code does
        ///     not state: <c>ScreenOf</c> reads <c>Math.Max(r, g, b)</c> per pixel, not an
        ///     average. A pixel with a single strong channel and two dark ones proves the
        ///     difference - under the max it lands above <c>menuAbove</c>, under an average it
        ///     would not have.
        /// </summary>
        [Fact]
        public void The_brightest_channel_drives_the_reading_not_the_average()
        {
            // max = 45 -> 45/255 = 0.176, above menuAbove (0.160).
            // average = 15 -> 15/255 = 0.059, at or below heroSelectBelow (0.085) - the
            // opposite bucket, had the code averaged instead of taking the max.
            var shot = Solid(0, 0, 45);
            Assert.Equal(GameScreen.Menu, GameSession.ScreenOf(shot, Layout, Map));
        }
    }
}
