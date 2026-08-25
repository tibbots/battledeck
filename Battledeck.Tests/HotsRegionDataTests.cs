using Battledeck.Backend.Entity;
using Xunit;

namespace Battledeck.Tests
{
    /// <summary>
    ///     The share the rank medal fills its channel with. It is computed and not stored,
    ///     so the two numbers stay the truth - which means this is the one place a wrong
    ///     ring could come from.
    /// </summary>
    public class HotsRegionDataTests
    {
        [Fact]
        public void The_share_is_points_over_the_bound()
        {
            var data = new HotsRegionData { RankPoints = 250, RankPointsMax = 1000 };

            Assert.Equal(0.25, data.RankProgress);
        }

        /// <summary>
        ///     Never read is not the same as at zero. Both draw an untouched medal, and only
        ///     the tooltip tells them apart - so the value behind them has to.
        /// </summary>
        [Theory]
        [InlineData(null, null)]
        [InlineData(328, null)]
        [InlineData(null, 1000)]
        public void Half_a_reading_is_no_reading(int? points, int? max)
        {
            var data = new HotsRegionData { RankPoints = points, RankPointsMax = max };

            Assert.Null(data.RankProgress);
        }

        /// <summary>
        ///     A bound of zero would divide by it. It should never arrive, which is exactly
        ///     why it is worth a line here - the reading comes out of text recognition, and
        ///     "1000" misread as "0" costs nothing else.
        /// </summary>
        [Fact]
        public void A_bound_of_zero_yields_no_share()
        {
            var data = new HotsRegionData { RankPoints = 0, RankPointsMax = 0 };

            Assert.Null(data.RankProgress);
        }

        /// <summary>
        ///     More points than the division needs is a state the ring has to survive rather
        ///     than break on - the game hands them out between the game ending and the
        ///     promotion showing.
        /// </summary>
        [Fact]
        public void More_points_than_the_bound_stay_at_full()
        {
            var data = new HotsRegionData { RankPoints = 1400, RankPointsMax = 1000 };

            Assert.Equal(1.0, data.RankProgress);
        }

        /// <summary>The copy the edit dialog works on has to carry them, or Cancel would keep them.</summary>
        [Fact]
        public void A_copy_carries_the_points()
        {
            var copy = new HotsRegionData
            {
                Tier = HotsRankTier.Gold, Division = 3, RankPoints = 328, RankPointsMax = 1000
            }.Copy();

            Assert.Equal(328, copy.RankPoints);
            Assert.Equal(1000, copy.RankPointsMax);
        }
    }
}
