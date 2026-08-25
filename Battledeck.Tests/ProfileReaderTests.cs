using Battledeck.Backend.Automation;
using Battledeck.Backend.Entity;
using Xunit;

namespace Battledeck.Tests
{
    /// <summary>
    ///     Pins <c>ProfileReader</c>'s pure text-matching functions against the incident of
    ///     25.08.2026: a profile overlay read "Sturmliga" cleanly on all 18 attempts across two
    ///     runs, yet <c>ValueUnder</c> returned <c>null</c> every time and the reading failed
    ///     with a message claiming the label had not appeared. It had - <c>Windows.Media.Ocr</c>
    ///     had merged the rank medal's division digit into the same line as the rank text,
    ///     <c>"2 Bronze 2"</c>, whose line box starts at the medal rather than at the words. The
    ///     lines below are the six actually measured from the app's own failing captures (three
    ///     separate openings, byte-identical every time); the word boxes for the merged line are
    ///     reconstructed consistent with that measurement - the medal digit sits far left, the
    ///     value words sit under the label.
    ///     <para>
    ///         These are pure functions over <see cref="TextLine" />/<see cref="TextWord" /> -
    ///         no game, no OCR, no screen needed. That is deliberate: this exact defect could
    ///         have been caught here, cheaply, well before it ever needed a running client to
    ///         reproduce.
    ///     </para>
    /// </summary>
    public class ProfileReaderTests
    {
        private static TextLine Line(string text, int x, int y, int width, int height,
            params TextWord[] words)
        {
            return new TextLine(text, x, y, width, height) { Words = words };
        }

        [Fact]
        public void ValueUnder_reads_the_value_even_when_a_medal_digit_merges_onto_its_line()
        {
            var head = Line("Sturmliga", 152, 222, 116, 24);
            var merged = Line("2 Bronze 2", 63, 253, 285, 49,
                new TextWord("2", 63, 260, 20, 30),
                new TextWord("Bronze", 152, 253, 180, 40),
                new TextWord("2", 345, 253, 30, 40));

            var value = ProfileReader.ValueUnder([head, merged], "sturmliga");

            Assert.NotNull(value);
            Assert.Equal("Bronze 2", value!.Text);
        }

        [Fact]
        public void ValueUnder_falls_back_to_the_whole_line_when_it_carries_no_word_boxes()
        {
            // The degrade-instead-of-throw path: nothing today builds a TextLine without
            // Words, but ValueUnder must not assume every caller does either.
            var head = Line("Sturmliga", 152, 222, 116, 24);
            var clean = new TextLine("Bronze 2", 152, 253, 116, 40);

            var value = ProfileReader.ValueUnder([head, clean], "sturmliga");

            Assert.NotNull(value);
            Assert.Equal("Bronze 2", value!.Text);
        }

        [Fact]
        public void ValueUnder_returns_null_when_the_label_is_missing()
        {
            var onlyValue = Line("Bronze 2", 152, 253, 116, 40);

            Assert.Null(ProfileReader.ValueUnder([onlyValue], "sturmliga"));
        }

        [Fact]
        public void ValueUnder_returns_null_when_nothing_below_the_label_aligns()
        {
            var head = Line("Sturmliga", 152, 222, 116, 24);
            // Far enough right that no word falls within head.Height - exercises the "found
            // a candidate line, but no word in it aligns" branch, distinct from the
            // no-word-boxes-at-all fallback the test above covers.
            var unrelated = Line("Replays", 900, 253, 100, 24,
                new TextWord("Replays", 900, 253, 100, 24));

            Assert.Null(ProfileReader.ValueUnder([head, unrelated], "sturmliga"));
        }

        [Theory]
        [InlineData("bronze 2", HotsRankTier.Bronze, 2)]
        [InlineData("diamant 5", HotsRankTier.Diamond, 5)]
        [InlineData("master", HotsRankTier.Master, 0)]
        [InlineData("grossmeister", HotsRankTier.GrandMaster, 0)]
        public void TryRank_parses_the_measured_German_vocabulary(
            string normalised, HotsRankTier expectedTier, int expectedDivision)
        {
            GameVocabulary.Current = GameVocabulary.German;

            var ok = ProfileReader.TryRank(normalised, out var tier, out var division);

            Assert.True(ok);
            Assert.Equal(expectedTier, tier);
            Assert.Equal(expectedDivision, division);
        }

        [Fact]
        public void TryRank_rejects_a_leading_digit()
        {
            GameVocabulary.Current = GameVocabulary.German;

            // What ValueUnder used to hand back before the word-level fix, on exactly this
            // incident - a loud "not recognised" would have become a silent "matched no
            // tier" if only the alignment check had been loosened instead of fixed properly.
            var ok = ProfileReader.TryRank("2 bronze 2", out _, out _);

            Assert.False(ok);
        }

        [Fact]
        public void Normalise_lowercases_and_strips_accents()
        {
            Assert.Equal("grossmeister", ProfileReader.Normalise("Großmeister"));
            Assert.Equal("bronze 2", ProfileReader.Normalise("Bronze  2"));
        }
    }
}
