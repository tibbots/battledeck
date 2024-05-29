using System.Globalization;
using System.Text;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     The one rule for how accents disappear from read text.
    ///     <para>
    ///         <b>Why this class exists</b>: the project has four comparisons against text
    ///         from the game, and they normalize to different degrees because they need
    ///         different things. <see cref="ProfileReader" /> keeps spaces, so that
    ///         "grand master" stays two words; <see cref="HeroNameMatcher" /> throws away
    ///         everything that is not a letter, so that "Anub'arak" and "Anubarak" yield the
    ///         same key. They are allowed to differ in that - <b>not</b> in what they do with
    ///         an accent.
    ///     </para>
    ///     <para>
    ///         <b>And accents are the point where a text recognizer gives way.</b> As long as
    ///         only German and English were read, this did not show up: no tier word, no tab
    ///         name, and none of the filler words carries one there - <c>Grossmeister</c> is
    ///         the only special case and is covered via the eszett. With French and Spanish,
    ///         <c>maître</c>, <c>clasificación</c>, <c>BOTÍN</c>, and <c>héros</c> appear in
    ///         the game, and a missed accent mark turns a match into a miss - silently,
    ///         because an unrecognized line is the same case as a missing one.
    ///     </para>
    ///     <para>
    ///         <b>That is why the accent is cleared on both sides</b>, in the vocabulary as
    ///         in the read text. The price is that two words differing only in the accent
    ///         become indistinguishable - in none of the five versions does this occur, and
    ///         hitting a word at all is worth more than hitting it precisely.
    ///     </para>
    /// </summary>
    public static class TextNormalisation
    {
        /// <summary>
        ///     Accents off, base letters kept. <c>maître</c> becomes <c>maitre</c>,
        ///     <c>BOTÍN</c> becomes <c>BOTIN</c> - upper and lower case remain untouched, the
        ///     callers take care of that themselves.
        ///     <para>
        ///         The ligatures and the eszett are handled ahead of the decomposition,
        ///         because they are <b>not</b> accents: the normal form does not split
        ///         anything off them. They must be replaced, otherwise they fall away
        ///         without replacement in the last step.
        ///     </para>
        /// </summary>
        public static string StripAccents(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            var replaced = new StringBuilder(text.Length + 4);
            foreach (var character in text)
                switch (character)
                {
                    case 'ß': replaced.Append("ss"); break;
                    case 'æ': replaced.Append("ae"); break;
                    case 'Æ': replaced.Append("AE"); break;
                    case 'œ': replaced.Append("oe"); break;
                    case 'Œ': replaced.Append("OE"); break;
                    case 'ø': replaced.Append('o'); break;
                    case 'Ø': replaced.Append('O'); break;
                    case 'đ': replaced.Append('d'); break;
                    case 'ł': replaced.Append('l'); break;
                    case 'Ł': replaced.Append('L'); break;
                    default: replaced.Append(character); break;
                }

            // In the decomposed form, every accent stands as its own character behind its
            // base letter; once left out, the base letter remains.
            var decomposed = replaced.ToString().Normalize(NormalizationForm.FormD);

            var result = new StringBuilder(decomposed.Length);
            foreach (var character in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                    result.Append(character);

            return result.ToString();
        }

        /// <summary>
        ///     Does <paramref name="haystack" /> contain the word <paramref name="needle" />,
        ///     regardless of accents and upper/lower case?
        ///     <para>
        ///         For the two comparisons against the <b>raw</b> line text:
        ///         <see cref="HeaderReader" /> searches for the loot-chest tab, and
        ///         <see cref="CollectionReader" /> holds the filler words against every card
        ///         line. Both ran on a plain <c>Contains</c> until 22.08.2026 - correctly so,
        ///         as long as <c>BEUTE</c> and <c>LOOT</c> were the only values.
        ///     </para>
        /// </summary>
        public static bool ContainsWord(string? haystack, string? needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
            return StripAccents(haystack)
                .Contains(StripAccents(needle), StringComparison.OrdinalIgnoreCase);
        }
    }
}
