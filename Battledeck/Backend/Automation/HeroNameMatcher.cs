using System.Globalization;
using System.Text;
using Serilog;
using Battledeck.Backend.Entity;

namespace Battledeck.Backend.Automation
{
    /// <summary>
    ///     Matches a read name strip to the hero(es) behind it.
    ///     <para>
    ///         Text recognition does not read error-free: measured results included "Arth"" instead of
    ///         "Arthas", "Funke/chen" instead of "Funkelchen", and - four times in a single run - a 'v' instead
    ///         of a 'y' ("Svlvanas", "Tvchus", "Ravnor"). That is immaterial as long as the answer
    ///         has to come from a known list of 90 names - the nearest neighbor is then
    ///         practically always the right one. Hit rate checked on a full page: 10 out of
    ///         10, including the two mangled ones.
    ///     </para>
    ///     <para>
    ///         Guessing does not happen anyway: if the distance exceeds <see cref="Threshold" /> of the
    ///         name length, the text counts as unreadable and falls away. Better one hero too few -
    ///         the expected count from the sidebar catches that - than one wrong one too many, which
    ///         would only surface weeks later.
    ///     </para>
    ///     <para>
    ///         <b>A card is not always one hero.</b> That is why <see cref="Match" /> returns a
    ///         list and not a single hero - see <see cref="Compound" />.
    ///     </para>
    ///     <para>
    ///         The comparison is against the <b>German</b> name: the game client runs on deDE.
    ///         On a client in a different language, the matching finds nothing - that is intentional and
    ///         is noticed immediately, because then not a single hero is recognized. A silent fallback
    ///         to the English name would instead yield a few random hits.
    ///     </para>
    /// </summary>
    public static class HeroNameMatcher
    {
        /// <summary>Share of the name length, beyond which matching no longer happens.</summary>
        private const double Threshold = 0.34;

        /// <summary>
        ///     Cards in the collection behind which <b>more than one</b> hero is hiding.
        ///     <para>
        ///         Cho'gall is the only known case: the game sells the two heads
        ///         together and shows ONE name strip for it, our catalog lists them
        ///         separately - own id, own portrait, own role (Cho is a tank, Gall
        ///         a ranged fighter). Without this entry, the card is fundamentally unreadable,
        ///         and that on every account that owns it: "chogall" has to "cho" a relative
        ///         distance of 1.33 and to "gall" 0.75, both well above the threshold of 0.34. In the log
        ///         this then shows as "'Cho'gall' matched no hero" - six times in a row,
        ///         even though the text was read cleanly each time.
        ///     </para>
        ///     <para>
        ///         <b>Consequence for the expected count</b>: the sidebar counts cards, our catalog
        ///         counts heroes. An account with all heroes therefore reports 89 acquired cards
        ///         at 90 catalog entries. <see cref="CollectionReader" /> therefore compares
        ///         cards with cards - not heroes with cards.
        ///     </para>
        /// </summary>
        private static readonly (string Name, string[] Ids)[] Compound =
        [
            ("Cho'gall", ["cho", "gall"])
        ];

        private static (string Key, IReadOnlyList<HotsHero> Heroes)[]? _candidates;

        /// <summary>
        ///     The vocabulary with which <see cref="_candidates" /> was built. Without this field
        ///     the once-built set would survive a language change and the matching would afterward
        ///     run silently against the wrong names - the most expensive error of this change,
        ///     because it would not be noticed either when translating or in the log.
        /// </summary>
        private static GameVocabulary? _builtFor;

        private static (string Key, IReadOnlyList<HotsHero> Heroes)[] Candidates
        {
            get
            {
                var vocabulary = GameVocabulary.Current;
                if (_candidates != null && ReferenceEquals(_builtFor, vocabulary)) return _candidates;

                _candidates = BuildCandidates(vocabulary);
                _builtFor = vocabulary;
                return _candidates;
            }
        }

        private static (string Key, IReadOnlyList<HotsHero> Heroes)[] BuildCandidates(
            GameVocabulary vocabulary)
        {
            var list = new List<(string Key, IReadOnlyList<HotsHero> Heroes)>();

            foreach (var hero in HotsHeroCatalog.All)
                list.Add((Normalise(vocabulary.HeroName(hero)), new[] { hero }));

            foreach (var (name, ids) in Compound)
            {
                var heroes = ids.Select(HotsHeroCatalog.Find).OfType<HotsHero>().ToArray();

                // Loud instead of silent: if someone renames one of the ids in the catalog, the
                // double card would otherwise silently drop out and the expected count would not add up again.
                if (heroes.Length != ids.Length)
                {
                    Log.Warning("Compound card '{Name}' cannot be resolved - the catalog is " +
                                "missing {Missing}", name,
                        string.Join(", ", ids.Where(id => HotsHeroCatalog.Find(id) == null)));
                    continue;
                }

                list.Add((Normalise(name), heroes));
            }

            return list.ToArray();
        }

        /// <summary>
        ///     The heroes for a read text - empty if none can be matched.
        ///     <paramref name="distance" /> is the distance to the best candidate, relative to its
        ///     length: 0 means word-identical.
        /// </summary>
        public static IReadOnlyList<HotsHero> Match(string? text, out double distance)
        {
            distance = 1.0;
            var key = Normalise(text);
            if (key.Length == 0) return [];

            IReadOnlyList<HotsHero> best = [];
            foreach (var (candidate, heroes) in Candidates)
            {
                var relative = Distance(key, candidate) / (double)Math.Max(candidate.Length, 1);
                if (relative >= distance) continue;
                distance = relative;
                best = heroes;
            }

            return distance <= Threshold ? best : [];
        }

        /// <summary>
        ///     Boiled down to lowercase letters and digits. Anything carrying an accent falls
        ///     back to its base letter - because text recognition occasionally misses it and
        ///     "Weissstraehne" and "Weißsträhne" should yield the same key.
        ///     <para>
        ///         <b>Decomposed instead of enumerated</b>, since 22.08.2026. There used to be a
        ///         list of ten characters here, grown from what German and English need -
        ///         and it would have silently missed French and Spanish:
        ///         <c>ñ</c>, <c>ç</c>, <c>í</c>, <c>ó</c>, <c>ô</c>, and <c>ï</c> were all
        ///         missing. The error would be the same as a wrong vocabulary: the name
        ///         is read cleanly and still not matched to any hero.
        ///     </para>
        ///     <para>
        ///         <c>ß</c> and the ligatures precede the decomposition, because they are
        ///         <b>not</b> accents: the normal form separates nothing from them, they have to be
        ///         replaced. For German nothing changes because of this - <c>ä</c> still yields
        ///         <c>a</c> and not <c>ae</c>, which is intentional, because the recognition makes exactly
        ///         this error.
        ///     </para>
        /// </summary>
        private static string Normalise(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // Whatever after that is not an ASCII letter or digit falls away - that
            // takes care of apostrophe, period, hyphen, and space in one go.
            var stripped = TextNormalisation.StripAccents(text).ToLowerInvariant();

            var result = new StringBuilder(stripped.Length);
            foreach (var character in stripped)
                if (char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character))
                    result.Append(character);

            return result.ToString();
        }

        /// <summary>Levenshtein distance, one row of memory instead of a matrix.</summary>
        private static int Distance(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) previous[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= b.Length; j++)
                    current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                        previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }
    }
}
