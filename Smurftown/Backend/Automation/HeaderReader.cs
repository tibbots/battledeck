using Serilog;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     The stats of the header bar. Each value individually <c>null</c> if it was not
    ///     read reliably - a half-read set is better than a guessed one.
    /// </summary>
    public sealed record HeaderReading(int? Gold, int? Shards, int? Gems, int? LootChests, string Note);

    /// <summary>
    ///     What the loot tab says. <c>Number</c> is the count when it could be read, and
    ///     <c>null</c> when it could not - which is <b>not</b> the same as none, and that
    ///     distinction is the whole reason this is a record and not an <c>int?</c>.
    ///     <c>AnyLeft</c> answers the question the opener actually has, and it is answered
    ///     even in the range where the number is unreadable.
    /// </summary>
    public sealed record LootCount(int? Number, bool AnyLeft);

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

        /// <summary>
        ///     Magnification for the badge on the loot tab, and it is not a matter of taste.
        ///     <para>
        ///         <b>Measured on 23.08.2026</b>, German client at 3440x1440, an account with 44
        ///         unopened chests: the badge is 24x15 points, and text recognition does not see
        ///         it <b>at all</b> at scale 1 and 2 - the line is simply missing from the result,
        ///         not misread. From 3 on it comes back as "44" reliably.
        ///     </para>
        ///     <para>
        ///         <b>Why nobody noticed for so long</b>: the badge next to it on SAMMLUNG had
        ///         three digits (129, 33 points wide) and reads even at scale 1, and so did the
        ///         chest counter while it stood at 138 or 65. Two narrow digits are exactly on the
        ///         edge. Below this the count silently read 0 - and 0 is also the honest answer
        ///         for an account with nothing to open, which is what made it invisible.
        ///     </para>
        /// </summary>
        private const int BadgeUpscale = 3;

        /// <summary>
        ///     One step higher, on the <b>same</b> capture, before the badge is given up. A
        ///     different magnification rasterizes the digits differently and is therefore a
        ///     genuine second attempt; the same one on the same pixels would inevitably give the
        ///     same answer. Same trick and same reasoning as in <c>CollectionReader</c>, and it
        ///     costs no second capture - the capture is the expensive part.
        /// </summary>
        private const int BadgeUpscaleRetry = 4;

        /// <summary>
        ///     What counts as a pixel of the badge border: clearly blue, clearly bluer than it
        ///     is green, and not green-dominated. Measured against the violet of the tab
        ///     lettering next to it, which is lighter and less saturated - and against the
        ///     empty bar, which has nothing above these values at all.
        /// </summary>
        private const int BadgeBlue = 140;

        private const int BadgeBlueOverGreen = 40;

        /// <summary>
        ///     Share of such pixels in the window from which a badge counts as present.
        ///     Measured: 5.7% with a badge, 0.0% without. One percent sits between the two
        ///     with room on both sides.
        /// </summary>
        private const double BadgePixelShare = 0.01;

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
                Take(0, 10_000_000), Take(1, 1_000_000), Take(2, 1_000_000), chests?.Number,
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
        public static async Task<LootCount?> CountLootChestsAsync(GameSession session, Screenshot shot)
        {
            var map = session.Map.Loot;
            var area = session.Layout.Area(map.NavBar);
            var (x, y, width, height) = area;
            var reach = session.Layout.Length(map.BadgeReach);

            var lines = await TextReader.ReadAsync(shot, x, y, width, height, BadgeUpscale);
            var read = BadgeIn(lines, reach);
            if (read.Tab == null) return null;
            if (read.Badge is { } number) return new LootCount(number, number > 0);

            // The tab is there and no number beside it. The same capture once more at a
            // higher magnification - see BadgeUpscaleRetry.
            var again = BadgeIn(
                await TextReader.ReadAsync(shot, x, y, width, height, BadgeUpscaleRetry), reach);

            if (again.Badge is { } retried)
            {
                Log.Debug("Loot: the badge read only at scale {Scale}: {Count}",
                    BadgeUpscaleRetry, retried);
                return new LootCount(retried, retried > 0);
            }

            // STILL NO NUMBER, and now it is decided on pixels rather than on text. Whether
            // the badge is there at all is a question text recognition cannot answer for us:
            // it does not return a lone character, so a badge showing 1 to 9 is invisible to
            // it - see HasBadgeBox.
            var present = HasBadgeBox(shot, area, read.Tab, read.Next, reach);
            return present ? new LootCount(null, true) : new LootCount(0, false);
        }

        /// <summary>
        ///     Picks the loot tab, its badge and the next line to the right out of one reading.
        ///     <c>Tab</c> null means the word was not there at all - a different screen, and
        ///     then "no chests" would be a statement we do not have.
        /// </summary>
        private static (TextLine? Tab, int? Badge, TextLine? Next) BadgeIn(
            IReadOnlyList<TextLine> lines, int reach)
        {
            // The word and not a box: the bar reflows, SAMMLUNG gets its
            // own badge, the tab shifts to the right. Which word stands there depends
            // on the client language and is therefore in GameVocabulary.
            // Accent-insensitive, since there are versions in which the tab carries one:
            // in Spanish it is called BOTIN with an accent on the I, and whether the recognition
            // reads that along is not reliable.
            var tab = lines.FirstOrDefault(line =>
                TextNormalisation.ContainsWord(line.Text, GameVocabulary.Current.LootTab));
            if (tab == null) return (null, null, null);

            // Right of the word start and within reach - so that the badge
            // of SAMMLUNG, which stands to its left, falls away, and the divider behind it too.
            var limit = tab.X + tab.Width + reach;
            var badge = lines.FirstOrDefault(line =>
                line.X > tab.X && line.X < limit &&
                line.Text.Length > 0 && line.Text.All(char.IsAsciiDigit));

            var next = lines
                .Where(line => line.X >= tab.X + tab.Width)
                .OrderBy(line => line.X)
                .FirstOrDefault();

            var value = badge != null && int.TryParse(badge.Text, out var parsed)
                        && parsed is >= 0 and <= 999
                ? parsed
                : (int?)null;

            return (tab, value, next);
        }

        /// <summary>
        ///     Whether a badge stands beside the loot tab - decided on the pixels, because
        ///     text recognition cannot answer it.
        ///     <para>
        ///         <b>Why this exists at all.</b> <c>Windows.Media.Ocr</c> does not return a
        ///         character that stands on its own. Measured on 23.08.2026 at 3440x1440: a
        ///         badge reading <c>9</c> comes back from no magnification between 1 and 12,
        ///         from no crop - not even from one holding <c>BEUTE</c>, the badge and
        ///         <c>REPLAYS</c> at once, which returns the two words and skips the digit
        ///         between them. Two digits read from magnification 3 on. So the range 1 to 9
        ///         is unreachable by reading, and "no number" must not be allowed to mean "no
        ///         chests" - that is the false zero this whole detour exists to prevent.
        ///     </para>
        ///     <para>
        ///         <b>Where it looks</b>: from the end of the word to the start of the next
        ///         recognised line, at most <c>badgeReach</c> beyond it. The next line is the
        ///         following tab exactly when the badge was not read - which is the case this
        ///         is for - so the window closes before the neighbouring word, whose letters
        ///         are violet too. It also survives the bar reflowing, because both edges come
        ///         out of the same reading.
        ///     </para>
        ///     <para>
        ///         <b>What it counts</b>: the badge has a bright violet border, the empty bar
        ///         has nothing of the sort. Measured in that window: <b>379</b> such pixels of
        ///         6640 with a badge, and <b>0</b> of 3760 beside a tab without one - the
        ///         divider between the tabs included. The threshold sits at one percent, five
        ///         times below the measurement and infinitely above the counter-measurement.
        ///     </para>
        /// </summary>
        private static bool HasBadgeBox(Screenshot shot, (int X, int Y, int Width, int Height) area,
            TextLine tab, TextLine? next, int reach)
        {
            var from = tab.X + tab.Width;
            var to = Math.Min(Math.Min(next?.X ?? int.MaxValue, from + reach), area.Width);
            if (to <= from) return false;

            var violet = 0;
            var total = 0;

            for (var cx = from; cx < to; cx++)
            for (var cy = 0; cy < area.Height; cy++)
            {
                var px = area.X + cx;
                var py = area.Y + cy;
                if (px < 0 || py < 0 || px >= shot.Width || py >= shot.Height) continue;

                total++;
                var (r, g, b) = shot[px, py];
                if (b >= BadgeBlue && b > g + BadgeBlueOverGreen && r > g) violet++;
            }

            var share = total == 0 ? 0 : (double)violet / total;
            Log.Debug("Loot: badge box {Violet} of {Total} pixels = {Share:P1} in {From}..{To}",
                violet, total, share, from, to);

            return share >= BadgePixelShare;
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
