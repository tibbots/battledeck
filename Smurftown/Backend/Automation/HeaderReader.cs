using Serilog;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     The stats of the header bar. Each value individually <c>null</c> if it was not
    ///     read reliably - a half-read set is better than a guessed one.
    /// </summary>
    public sealed record HeaderReading(int? Gold, int? Shards, int? Gems, int? LootChests, string Note);

    /// <summary>
    ///     Reads gold, shards, gems, and the number of unopened loot chests from the
    ///     main menu's header bar.
    ///     <para>
    ///         All four appear on every screen that the sign-in leaves behind anyway - one
    ///         capture is enough for all of them. The chest counter is therefore <b>free</b>: it hangs
    ///         as a badge on the BEUTE tab and needs neither a click nor its own
    ///         screen.
    ///     </para>
    ///     <para>
    ///         <b>The account level used to be included here and is out.</b> It stands as a
    ///         small number in the portrait frame at the far right, and the crop cut its
    ///         leading digit right in half: 145 became 45, and sometimes nothing came at all. The
    ///         error sat unnoticed in the data.yaml for months. It now comes from the
    ///         profile overlay, where it is written out in full - see <see cref="ProfileReader" />.
    ///     </para>
    ///     <para>
    ///         Read with magnification: the digits are small, and "30" instead of "305" would go
    ///         unnoticed by anyone. Hence additional plausibility limits - whatever lies
    ///         outside is discarded instead of saved.
    ///     </para>
    /// </summary>
    public static class HeaderReader
    {
        private const int CurrencyUpscale = 2;

        public static async Task<HeaderReading> ReadAsync(GameSession session,
            CancellationToken token = default)
        {
            if (!TextReader.Available)
                return new HeaderReading(null, null, null, null,
                    "Without text recognition gold, shards, gems and chests stay unread.");

            var shot = session.Capture();
            var numbers = await Currencies(session, shot, token);
            var chests = await CountLootChestsAsync(session, shot);

            int? Take(int index, int limit)
            {
                if (index >= numbers.Count) return null;
                return numbers[index] <= limit ? numbers[index] : null;
            }

            // Order in the bar, from left to right. It is fixed; making the assignment
            // via the symbols next to it would be three more comparison images for
            // information that the position already provides.
            var reading = new HeaderReading(
                Take(0, 10_000_000), Take(1, 1_000_000), Take(2, 1_000_000), chests,
                numbers.Count >= 3
                    ? "Header bar read."
                    : $"Only {numbers.Count} of 3 header numbers read.");

            Log.Information("Header: gold {Gold}, shards {Shards}, gems {Gems}, chests {Chests}",
                reading.Gold, reading.Shards, reading.Gems, reading.LootChests);
            return reading;
        }

        private static async Task<List<int>> Currencies(GameSession session, Screenshot shot,
            CancellationToken token)
        {
            var (x, y, width, height) = session.Layout.Area(session.Map.Menu.HeaderCurrencies);
            var lines = await TextReader.ReadAsync(shot, x, y, width, height, CurrencyUpscale);
            token.ThrowIfCancellationRequested();

            return lines.OrderBy(line => line.X)
                .Select(line => Number(line.Text))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();
        }

        /// <summary>
        ///     The number of unopened loot chests, from the badge on the BEUTE tab.
        ///     <para>
        ///         It is the <b>sum over all chest types</b> - the carousel on the
        ///         loot page shows them per type individually, the badge together. Cross-checked:
        ///         "Seltene Truhe 1" plus "Beutetruhe 28" at a badge of 29.
        ///     </para>
        ///     <para>
        ///         <b>The word is searched for, not a box.</b> The bar reflows: if
        ///         SAMMLUNG gets its own badge - which happens exactly when one has opened a chest
        ///         -, BEUTE shifts to the right, at 1920x1080 a measured 33 points
        ///         further than calculated from the height. A fixed crop would have pointed into
        ///         nothing there.
        ///     </para>
        ///     <para>
        ///         Three answers, and the distinction between the last two is the
        ///         point: number found - <c>0</c> when BEUTE stands there with no number next to it
        ///         (cross-checked on BUBU, where the badge is then missing entirely) - and <c>null</c> when
        ///         not even the word BEUTE was read. Then the screen is a different one,
        ///         and "no chests" would be a statement we do not have.
        ///     </para>
        /// </summary>
        public static async Task<int?> CountLootChestsAsync(GameSession session, Screenshot shot)
        {
            var map = session.Map.Loot;
            var (x, y, width, height) = session.Layout.Area(map.NavBar);
            var lines = await TextReader.ReadAsync(shot, x, y, width, height);

            // The word and not a box: the bar reflows, SAMMLUNG gets its
            // own badge, the tab shifts to the right. Which word stands there depends
            // on the client language and is therefore in GameVocabulary.
            // Accent-insensitive, since there are versions in which the tab carries one:
            // in Spanish it is called BOTIN with an accent on the I, and whether the recognition
            // reads that along is not reliable.
            var tab = lines.FirstOrDefault(line =>
                TextNormalisation.ContainsWord(line.Text, GameVocabulary.Current.LootTab));
            if (tab == null) return null;

            // Right of the word start and within reach - so that the badge
            // of SAMMLUNG, which stands to its left, falls away, and the divider behind it too.
            var reach = tab.X + tab.Width + session.Layout.Length(map.BadgeReach);
            var badge = lines.FirstOrDefault(line =>
                line.X > tab.X && line.X < reach &&
                line.Text.Length > 0 && line.Text.All(char.IsAsciiDigit));

            if (badge == null) return 0;
            return int.TryParse(badge.Text, out var value) && value is >= 0 and <= 999 ? value : 0;
        }

        /// <summary>
        ///     All digits of a line into one number. Separators are thrown out - the game
        ///     writes "3.835", and the recognition occasionally turns that into "17,4" for 174. A
        ///     period is never a comma here: HotS shows no decimal places.
        /// </summary>
        private static int? Number(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            return digits.Length is > 0 and <= 9 && int.TryParse(digits, out var value) ? value : null;
        }
    }
}
