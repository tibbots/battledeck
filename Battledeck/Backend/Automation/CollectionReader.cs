using System.Text.RegularExpressions;
using Serilog;
using Battledeck.Backend.Texts;

namespace Battledeck.Backend.Automation
{
    /// <summary>
    ///     What was in the collection. <paramref name="Expected" /> is the expected count from the
    ///     sidebar, <paramref name="Total" /> the total number of heroes in the game - both
    ///     0 if the hint could not be read.
    /// </summary>
    public sealed record CollectionReading(
        IReadOnlyList<string> HeroIds,
        int Expected,
        int Total,
        bool Complete,
        string Note);

    /// <summary>
    ///     Reads the acquired heroes from the collection.
    ///     <para>
    ///         <b>Why the collection and not the hero select:</b> the hero select shows all 90
    ///         heroes on a single screen, which is tempting - but only as a tile, and
    ///         "owned" there just means "brighter". Whoever reads it must know which tile
    ///         belongs to which hero, and this order could not be reliably derived:
    ///         the cross-check on an account with 24 recorded heroes hit 12. The collection
    ///         writes the name as text under each card and can be filtered to "Erworbene Helden"
    ///         - there is nothing to guess there. The price is paging.
    ///     </para>
    ///     <para>
    ///         <b>The expected count is the proof that nothing was lost.</b> Hovering over "Alle" in
    ///         the sidebar, a hint window names "32/89 erworben". Without this
    ///         number there would be no way to tell whether a page was skipped or the
    ///         account really has fewer heroes.
    ///     </para>
    ///     <para>
    ///         It thereby decides whether the result may <b>replace</b> or only
    ///         <b>merge</b> - see <c>AccountCardViewModel.ReadHeroes</c>. The reader itself
    ///         does not make this decision; it only reports whether it was complete
    ///         (<see cref="CollectionReading.Complete" />).
    ///     </para>
    /// </summary>
    public static class CollectionReader
    {
        /// <summary>How many times in a row a page may bring nothing new before it stops.</summary>
        private const int DryRounds = 2;

        /// <summary>
        ///     Upper limit for the paging loop. 90 heroes at 5 columns are 18 rows, and
        ///     paging happens row by row - so 17 steps, plus the two empty rounds at
        ///     the end and some reserve.
        /// </summary>
        private const int MaximumPages = 24;

        /// <summary>
        ///     How long to try reading the expected count from the hint. Kept short: the
        ///     hint appears either after a few seconds or not at all, and waiting longer just
        ///     pushes back the point at which work continues without proof.
        /// </summary>
        private static readonly TimeSpan CountTimeout = TimeSpan.FromSeconds(12);

        /// <summary>
        ///     How many times a page is read at most before paging on.
        ///     <para>
        ///         A <c>WaitForStableArea</c> over the card grid used to stand here, and that
        ///         was the wrong question. The measurement box necessarily spans
        ///         the area between the two name strips, and there the moving
        ///         background shows through between the cards: the area never went
        ///         quiet in a single run, every page burned the full 20 seconds of timeout, and
        ///         the result was not improved by a single hero because of it. The question is not
        ///         "is something still moving", but "is the page full" - and that is the shape of
        ///         <see cref="GameSession.Retry{T}" />: read, check, retry if needed.
        ///     </para>
        /// </summary>
        private const int PageAttempts = 3;

        /// <summary>
        ///     How long to wait after paging before reading for the first time -
        ///     and how long between two read attempts. Kept short: both occur per
        ///     page, and the cards fade in within fractions of a second.
        /// </summary>
        private static readonly TimeSpan PagePause = TimeSpan.FromMilliseconds(600);

        /// <summary>
        ///     Words that can appear in the name strip but are not one. They would have
        ///     failed the matching distance threshold anyway - naming them here just saves
        ///     one comparison against 90 names per card and keeps the log readable.
        ///     <para>
        ///         Language-dependent, hence from <see cref="GameVocabulary" /> and no longer a
        ///         fixed list.
        ///     </para>
        /// </summary>
        private static IReadOnlyList<string> NotNames => GameVocabulary.Current.NotNames;

        /// <summary>
        ///     How many crops of a run are saved as an image at most when a
        ///     cell yielded no text in any attempt.
        ///     <para>
        ///         The reason for these images is a measured case: Rehgar stayed empty in one run
        ///         over SIX captures - three times on page 12, three times on the
        ///         overlapping page 13 - while its neighbors Raynor and Rexxar were read
        ///         cleanly every time. Against an otherwise roughly 15 percent miss rate per capture, that is
        ///         no coincidence, but something about this card. What, the log does not say: it
        ///         only records that text recognition returned nothing. The saved
        ///         crop, on the other hand, shows immediately whether the box sat next to the name
        ///         or whether there really was nothing there.
        ///     </para>
        ///     <para>
        ///         Capped so that a fundamentally broken run does not leave behind 180 files.
        ///     </para>
        /// </summary>
        private const int MaximumDeadShots = 3;

        private static readonly Regex CountPattern = new(@"(\d+)\s*/\s*(\d+)", RegexOptions.Compiled);

        /// <summary>A cell of the visible page, counted from 1 - the way it appears in the log.</summary>
        private readonly record struct Cell(int Row, int Column);

        /// <summary>What a single look at a page yielded.</summary>
        private sealed record PageResult(int Recognised, IReadOnlyList<Cell> Empty, Screenshot Shot);

        public static async Task<CollectionReading> ReadAsync(GameSession session,
            IProgress<ProgressStep>? progress = null, CancellationToken token = default)
        {
            if (!TextReader.Available)
                return new CollectionReading([], 0, 0, false,
                    "Heroes cannot be read without text recognition - the Windows language pack is missing.");

            var map = session.Map.Collection;

            progress?.Report(ProgressStep.Of("progress.openCollection"));
            if (!await OpenHeroCollection(session, map, token))
                return new CollectionReading([], 0, 0, false,
                    "The collection could not be opened - the tab was not found. Check that the " +
                    "client language matches the one set in Battledeck.");

            progress?.Report(ProgressStep.Of("progress.setFilter"));
            await ChooseFromDropdown(session, map, map.OwnedFilter, map.OwnedItem, token);
            await ChooseFromDropdown(session, map, map.SortFilter, map.AlphabeticalItem, token);

            // Click "Alle" in the sidebar and leave the cursor there: the click
            // clears the role preselection, staying there makes the hint with the expected count
            // appear. Both in one go, because a second visit would only cost time.
            session.Click(map.AllRoles);
            await Task.Delay(500, token);
            var (expected, total) = await ReadExpectedCount(session, map, token);
            if (expected > 0) progress?.Report(ProgressStep.Of("progress.owned", expected, total));

            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Counted by CARDS and not by heroes, because the expected count from the sidebar
            // counts cards. Cho'gall is ONE card and TWO catalog entries - an account with
            // all heroes therefore reports 89 acquired cards out of 90 entries, and a
            // comparison of "heroes read against expected count" would never add up there. The key
            // is the card's id, joined with a plus sign for double cards.
            var cards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var dead = new List<string>();
            var deadShots = 0;
            var dry = 0;

            var slots = map.Rows * map.Columns;

            for (var page = 0; page < MaximumPages && dry < DryRounds; page++)
            {
                var before = cards.Count;
                HashSet<Cell>? empty = null;
                Screenshot? lastShot = null;

                // Every attempt takes a FRESH capture. Sending the same one through the
                // text recognition again would bring nothing - same pixels, same
                // answer. A new one, however, can show a card that just finished fading in.
                for (var attempt = 1; attempt <= PageAttempts; attempt++)
                {
                    await Task.Delay(PagePause, token);
                    var result = await ReadPage(session, map, cards, seen, found, page, attempt, token);
                    lastShot = result.Shot;

                    // A cell only counts as dead once it stayed empty in EVERY attempt. Hence
                    // intersection and not union: a single miss while
                    // fading in is not a finding, six zeros in a row are.
                    if (empty == null) empty = new HashSet<Cell>(result.Empty);
                    else empty.IntersectWith(result.Empty);

                    // Full is full. The stop condition is READABLE CELLS, not new
                    // heroes: since paging happens row by row, every page overlaps with the
                    // previous one by one row - a cleanly read page therefore
                    // brings only half as much new content from the start, and nothing can be read from that.
                    if (result.Recognised >= slots) break;
                    if (expected > 0 && cards.Count >= expected) break;
                }

                foreach (var cell in WithoutTrailing(empty, map))
                {
                    dead.Add($"p{page + 1} cell {cell.Row}/{cell.Column}");
                    if (deadShots >= MaximumDeadShots || lastShot == null) continue;

                    var (dx, dy, dw, dh) = session.Layout.Area(SpotFor(map, cell.Row - 1, cell.Column - 1));
                    GameSession.SaveDiagnostic(lastShot.Crop(dx, dy, dw, dh),
                        $"collection-empty-p{page + 1}-{cell.Row}-{cell.Column}");
                    deadShots++;
                }

                if (cards.Count == before) dry++;
                else dry = 0;

                progress?.Report(expected > 0
                    ? ProgressStep.Of("progress.cardsOf", cards.Count, expected)
                    : ProgressStep.Of("progress.cards", cards.Count));

                if (expected > 0 && cards.Count >= expected) break;

                session.ScrollAt(session.Layout.Width / 2, session.Layout.Height / 2, -map.ScrollNotches);
            }

            // Naming the missing ones would not work - whoever was not read is
            // unknown. Where it stood, however, can be said, and that is exactly what was missing:
            // the cell numbers only stood per attempt at Debug level and only became visible by
            // calculating across all pages afterward.
            if (dead.Count > 0)
                Log.Information("Collection: {Count} cells stayed empty in every attempt - {Cells}",
                    dead.Count, string.Join(", ", dead));

            var complete = expected > 0 && cards.Count == expected;
            var note = expected switch
            {
                0 => $"{found.Count} heroes read; the expected count was unreadable, so it is " +
                     "not certain that every page was covered.",
                _ when complete => $"{found.Count} heroes read, all {expected} cards covered.",
                _ => $"{found.Count} heroes read - {expected - cards.Count} of {expected} " +
                     "cards missing."
            };

            Log.Information("Collection: {Note}", note);
            return new CollectionReading(found, expected, total, complete, note);
        }

        /// <summary>
        ///     On SAMMLUNG and there on HELDEN. <b>Both tabs are searched, not
        ///     clicked where the calibration presumes them</b> - see <see cref="TabFinder" />.
        ///     <para>
        ///         Returns <c>false</c> if either one was not there. Unlike the ARAM tab, which
        ///         only sets a sign for the human, this is - here -
        ///         a stop condition: what would be read afterward would be the text of some
        ///         other screen, and the expected count would not catch that.
        ///     </para>
        /// </summary>
        private static async Task<bool> OpenHeroCollection(GameSession session,
            ScreenMap.CollectionSection map, CancellationToken token)
        {
            var vocabulary = GameVocabulary.Current;

            // The search strip is loot.navBar - the same top bar, just under the name
            // under which it was introduced for the loot chest counter.
            session.Window.BringToFront();
            if (!await TabFinder.ClickAsync(session, session.Map.Loot.NavBar,
                    vocabulary.CollectionTab, "collection", token))
                return false;

            await Task.Delay(500, token);

            session.Window.BringToFront();
            if (!await TabFinder.ClickAsync(session, map.HeroesBar,
                    vocabulary.HeroesTab, "heroes", token))
                return false;

            await Task.Delay(500, token);
            return true;
        }

        /// <summary>
        ///     Opens a dropdown and picks the nth entry. The entries stand at a fixed
        ///     distance below the field - measured 313 for the first, 38 spacing.
        /// </summary>
        private static async Task ChooseFromDropdown(GameSession session,
            ScreenMap.CollectionSection map, Spot field, int index, CancellationToken token)
        {
            session.Window.BringToFront();
            session.Click(field);
            await Task.Delay(500, token);

            var (x, _) = session.Layout.Point(field);
            var y = session.Layout.Length(map.DropdownFirst + index * map.DropdownPitch);
            session.ClickAt(x, y);
            await Task.Delay(500, token);
        }

        /// <summary>Expected count and total count, as they appear in the sidebar.</summary>
        private sealed record HeroCount(int Expected, int Total);

        /// <summary>
        ///     Reads the "32/89 erworben" hint from the sidebar. If that fails,
        ///     it continues with 0 - then only the proof is missing, not the result.
        ///     <para>
        ///         Read repeatedly, and not out of caution: this single number determines
        ///         whether the whole hero list is adopted. A single miss by the
        ///         text recognition would discard a minute of work. The cursor
        ///         stays still on "Alle" meanwhile, so the hint stays put - every round sees the same
        ///         picture, just with more time to build up.
        ///     </para>
        /// </summary>
        private static async Task<(int Expected, int Total)> ReadExpectedCount(GameSession session,
            ScreenMap.CollectionSection map, CancellationToken token)
        {
            var (x, y, width, height) = session.Layout.Area(map.RoleTooltip);
            var text = "";

            var (count, _) = await session.RetryAsync<HeroCount>(async shot =>
            {
                text = await TextReader.ReadTextAsync(shot, x, y, width, height, 3);
                var found = CountPattern.Match(text);
                if (found.Success &&
                    int.TryParse(found.Groups[1].Value, out var owned) &&
                    int.TryParse(found.Groups[2].Value, out var all) &&
                    all > 0 && owned <= all)
                    return new HeroCount(owned, all);

                return null;
            }, CountTimeout, "expected hero count", token);

            if (count != null) return (count.Expected, count.Total);

            Log.Warning("Expected count unreadable, last read '{Text}'", text);
            return (0, 0);
        }

        /// <summary>
        ///     The name strip of a cell. The calibration only names the one of the first card,
        ///     everything else is grid spacing - <paramref name="row" /> and
        ///     <paramref name="column" /> therefore count from 0.
        /// </summary>
        private static Spot SpotFor(ScreenMap.CollectionSection map, int row, int column)
        {
            return new Spot
            {
                Anchor = map.CardName.Anchor,
                X = map.CardName.X + column * map.ColumnPitch,
                Y = map.CardName.Y + row * map.RowPitch,
                Width = map.CardName.Width,
                Height = map.CardName.Height
            };
        }

        /// <summary>
        ///     Empty cells without the rest at the end of the list.
        ///     <para>
        ///         The last row is almost never full - 89 heroes at 5 columns fill
        ///         row 18 only four fifths of the way, and the last paging step leaves
        ///         half a page empty anyway. These cells are not missed, but empty. A
        ///         cell therefore only counts as missed once behind it - in reading order, i.e.
        ///         row by row from the left - there is still a read one.
        ///     </para>
        /// </summary>
        private static IEnumerable<Cell> WithoutTrailing(HashSet<Cell>? empty,
            ScreenMap.CollectionSection map)
        {
            if (empty == null || empty.Count == 0) return [];

            var ordered = empty.OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
            var last = new Cell(map.Rows, map.Columns);

            while (ordered.Count > 0 && ordered[^1] == last)
            {
                ordered.RemoveAt(ordered.Count - 1);
                last = last.Column > 1
                    ? new Cell(last.Row, last.Column - 1)
                    : new Cell(last.Row - 1, map.Columns);
            }

            return ordered;
        }

        /// <summary>
        ///     Reads the visible cards of a page. <paramref name="attempt" /> is only for the
        ///     log - so it can later be distinguished on which look a
        ///     card became readable.
        /// </summary>
        private static async Task<PageResult> ReadPage(GameSession session,
            ScreenMap.CollectionSection map, HashSet<string> cards, HashSet<string> seen,
            List<string> found, int page, int attempt, CancellationToken token)
        {
            var shot = session.Capture();
            var recognised = 0;
            var empty = new List<Cell>();

            for (var row = 0; row < map.Rows; row++)
            for (var column = 0; column < map.Columns; column++)
            {
                token.ThrowIfCancellationRequested();

                var (x, y, width, height) = session.Layout.Area(SpotFor(map, row, column));
                var where = $"p{page + 1}.{attempt} cell {row + 1}/{column + 1}";
                var lines = await TextReader.ReadAsync(shot, x, y, width, height, 2);

                // Second scale factor level on the SAME capture, before the cell is given up.
                // This costs no capture - and the capture is the expensive part: about 20 MB at
                // 3440x1440, and it brings the game window to the front. A different magnification
                // rasterizes the text differently and is therefore a genuine second attempt,
                // while the same magnification on the same pixels would inevitably
                // give the same answer.
                if (lines.Count == 0)
                {
                    lines = await TextReader.ReadAsync(shot, x, y, width, height, 3);
                    if (lines.Count > 0)
                        Log.Debug("Collection: {Where} only read at scale 3", where);
                }

                // One line per cell, even if nothing came back. Without this line, a
                // missing card cannot be classified afterward: text recognition returns nothing for
                // a card that is fading in, without reporting an error - and that
                // looks in the log exactly like an empty cell at the end of the list.
                if (lines.Count == 0)
                {
                    Log.Debug("Collection: {Where} - nothing read", where);
                    empty.Add(new Cell(row + 1, column + 1));
                    continue;
                }

                var hit = false;
                foreach (var line in lines)
                {
                    // Accent-insensitive: in French, a not-owned card reads
                    // "HEROS" with an acute accent, and a missed accent mark would let the word
                    // fall through - the line would then go into name matching instead of being discarded.
                    if (NotNames.Any(word => TextNormalisation.ContainsWord(line.Text, word)))
                        continue;

                    var heroes = HeroNameMatcher.Match(line.Text, out var distance);
                    if (heroes.Count == 0)
                    {
                        Log.Debug("Collection: {Where} '{Text}' matched no hero (distance {Distance:n2})",
                            where, line.Text, distance);
                        continue;
                    }

                    hit = true;

                    // One card, possibly several heroes - see HeroNameMatcher.Compound.
                    // The card's id is the key for comparing against the expected count, the
                    // heroes' ids go into the list.
                    var card = string.Join("+", heroes.Select(h => h.Id));
                    if (!cards.Add(card))
                    {
                        Log.Debug("Collection: {Where} '{Text}' -> {Card}, already known", where, line.Text, card);
                        continue;
                    }

                    foreach (var hero in heroes)
                        if (seen.Add(hero.Id))
                            found.Add(hero.Id);

                    Log.Debug("Collection: {Where} '{Text}' -> {Card} (distance {Distance:n2})",
                        where, line.Text, card, distance);
                    if (distance > 0)
                        Log.Information("Collection: '{Text}' -> {Card} (distance {Distance:n2})",
                            line.Text, card, distance);
                }

                if (hit) recognised++;
            }

            return new PageResult(recognised, empty, shot);
        }
    }
}
