using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using Smurftown.Backend.Entity;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     What stood in the profile overlay. Each value individually <c>null</c> if it was
    ///     not read with certainty - a half-read line is better than a guessed one.
    ///     <para>
    ///         <paramref name="Tier" /> is also <c>null</c> when placement games are open:
    ///         then there is no rank there, but the word "Platzierung" instead. The entered
    ///         rank of the previous season stays as is in that case and must not be cleared.
    ///     </para>
    /// </summary>
    /// <param name="SeenBattletag">
    ///     The battletag that actually stood in the header of the overlay - set only when it
    ///     deviates from the expected one. It is the basis for the decision on whether a
    ///     rename has taken place; that decision is made by the caller, not the reader.
    /// </param>
    /// <param name="Matches">
    ///     <b>The most important value in this record.</b> <c>false</c> means: the values
    ///     come from an overlay that carried a different battletag than the account being
    ///     read for. They may then belong to a <b>foreign</b> account and must not be written
    ///     as long as the identity is not clarified.
    ///     <para>
    ///         Previously the reader gave out no values at all in this case. It does so now
    ///         so that the caller can handle a rename in <b>two</b> readings instead of three -
    ///         and carries the warning for that in this field. Whoever ignores
    ///         <c>Matches</c> reproduces the bug from 20.08.2026: back then a hero selection
    ///         was compared against a medal and foreign values were written.
    ///     </para>
    /// </param>
    /// <param name="RankPoints">
    ///     Progress inside the division, as the tooltip on the rank shows it - the left of
    ///     the two numbers in <c>328 / 1000</c>, with <paramref name="RankPointsMax" /> the
    ///     right one.
    ///     <para>
    ///         <b>Both or neither.</b> A tooltip read as half a line would put a share on
    ///         screen that nothing supports, and it is a value nobody would look at twice.
    ///     </para>
    ///     <para>
    ///         They stay <c>null</c> where there is nothing to fill towards: Master, Grand
    ///         Master and an open placement. And they stay null where the tooltip was not
    ///         reached at all - the rank itself is read without it, so a missing tooltip
    ///         costs the points and not the reading.
    ///     </para>
    /// </param>
    public sealed record ProfileReading(
        int? AccountLevel,
        HotsRankTier? Tier,
        int Division,
        bool? PlacementsPending,
        string Note,
        int? RankPoints = null,
        int? RankPointsMax = null,
        string? SeenBattletag = null,
        bool Matches = true);

    /// <summary>
    ///     Reads rank and account level from the profile overlay - right-click on the profile
    ///     picture top right, then "Profil ansehen".
    ///     <para>
    ///         <b>Why no longer from the rank screen:</b> there, the rank stands as a medal,
    ///         and the division is a decorative glyph on the disc that no character
    ///         recognizer reads. That is why this used to run over a self-learning set of
    ///         comparison images - with three drawbacks that all surfaced at once on
    ///         20.08.2026: the disc carries a <b>moving facet pattern</b>, which made the
    ///         distance to the same rank swing between 0.019 and over 0.3; a rank that was
    ///         not yet in the <c>data.yaml</c> was fundamentally unreadable, because there
    ///         was nothing to learn from; and nobody checked whether the photographed screen
    ///         was even the rank screen - once a hero selection was compared against a medal.
    ///     </para>
    ///     <para>
    ///         Instead, the profile shows <c>Sturmliga</c> and below it <c>Silber 3</c> as
    ///         running text - tier and division in one line. Text recognition reads this
    ///         without upscaling error-free, verified at 3440x1440 as at 1920x1080. Added to
    ///         that are two things that are otherwise not available cleanly anywhere else:
    ///         the <b>account level</b> as a spelled-out number (in the header bar the crop
    ///         cut off the leading digit - 145 became 45) and the <b>battletag</b> as a
    ///         cross-check.
    ///     </para>
    ///     <para>
    ///         <b>Since 20.08.2026 the battletag is two things at once:</b> cross-check and
    ///         source. It still decides whether the values belong to this account at all
    ///         (<see cref="ProfileReading.Matches" />) - and if it deviates, it is at the
    ///         same time the only sign that the account was renamed at Blizzard. What
    ///         follows from that is decided by the caller; here it is only read and reported.
    ///     </para>
    ///     <para>
    ///         <b>Since 22.08.2026 there is a third role</b>, and it is the one where the tag
    ///         carries the most weight: with <c>expected</c> set to <c>null</c> nobody said who
    ///         should be standing there, and the read tag alone decides which of the stored
    ///         accounts this reading belongs to. That is the path behind the running client -
    ///         it is signed in already, and the app has to find out into whose row the numbers
    ///         go.
    ///     </para>
    ///     <para>
    ///         It stays language-dependent, but differently: instead of an image replacement
    ///         per rank, there is now a table of seven words below. On a client with a
    ///         different language, nothing is recognized and nothing is written.
    ///     </para>
    /// </summary>
    public static class ProfileReader
    {
        /// <summary>
        ///     The label under which the rank stands. Comes from the vocabulary of the set
        ///     client language and is therefore a property and not a constant - the language
        ///     can change at runtime, a <c>const</c> would be compiled into every caller.
        /// </summary>
        private static string RankLabel => GameVocabulary.Current.RankLabel;

        /// <summary>The label under which the account level stands.</summary>
        private static string LevelLabel => GameVocabulary.Current.LevelLabel;

        /// <summary>
        ///     The word that stands in place of a rank as long as placement games are open.
        ///     Measured on 20.08.2026 at BUBU: <c>Sturmliga</c> / <c>Platzierung</c>, plus a
        ///     purple placeholder instead of the medal. No number, no "x/3".
        /// </summary>
        private static string PlacementWord => GameVocabulary.Current.PlacementWord;

        /// <summary>The word for "rank points" in the tooltip on the medal.</summary>
        private static string RankPointsWord => GameVocabulary.Current.RankPointsWord;

        /// <summary>
        ///     How long to wait for the overlay, and how often it is opened. Two attempts,
        ///     because the game occasionally swallows a click - then the overlay simply never
        ///     appears, and waiting longer does not help.
        /// </summary>
        private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(8);

        private const int OpenAttempts = 2;

        /// <summary>
        ///     How long to wait for the tooltip on the rank medal. Shorter than
        ///     <see cref="ReadTimeout" /> on purpose: the overlay is already standing at this
        ///     point, the pointer is already on the medal, and if nothing has appeared after
        ///     four seconds it is not going to. What is lost then is the points, not the
        ///     reading - the rank itself was read without the tooltip.
        /// </summary>
        private static readonly TimeSpan TooltipTimeout = TimeSpan.FromSeconds(4);

        /// <summary>
        ///     Both lines of the tooltip start with their number: "497 Rangpunkte" and
        ///     "503 Rangpunkte fuer Aufstieg erforderlich". Built per read rather than held
        ///     as a field, because the client language can change at runtime.
        /// </summary>
        private static Regex PointsPattern =>
            new($@"^(\d+)\s+{Regex.Escape(RankPointsWord)}", RegexOptions.CultureInvariant);

        /// <summary>
        ///     The tier words of the set client language. Until 21.08.2026 they stood here as
        ///     a German table with the note "the only language-dependent spot" - that was not
        ///     even true back then, there were four spread across three files. Now they all
        ///     live in <see cref="GameVocabulary" />.
        /// </summary>
        private static IReadOnlyDictionary<string, HotsRankTier> Tiers => GameVocabulary.Current.Tiers;

        /// <summary>
        ///     The rank line: a tier word, followed by the division as a single digit.
        ///     <para>
        ///         The tier word may be MULTI-PART. Until 21.08.2026 <c>^([a-z]+)</c> stood
        ///         here, and an English "Grand Master" would have fallen through it -
        ///         silently, because an unrecognized line is the same case as a missing one.
        ///         On German this does not show up, because "Grossmeister" is one word.
        ///     </para>
        ///     <para>
        ///         The word part is LAZY (<c>*?</c>): greedy, it swallowed the space in
        ///         "platinum 5" and the division would stay unread.
        ///     </para>
        /// </summary>
        private static readonly Regex RankPattern =
            new(@"^([a-z]+(?: [a-z]+)*?)(?:\s+([1-5]))?$", RegexOptions.Compiled);

        /// <param name="expected">
        ///     The battletag that <b>should</b> be standing there - or <c>null</c> for
        ///     "tell me who is signed in".
        ///     <para>
        ///         The two cases are the two entrances of this app. With a value it is a
        ///         <b>cross-check</b>: an account was chosen and signed in, and a deviation means
        ///         either a rename or a foreign screen. With <c>null</c> the reading <b>is</b> the
        ///         identification - a client was already running and nobody said whose it is.
        ///     </para>
        ///     <para>
        ///         Both cases come back the same way, and that is deliberate: <c>Matches</c> false
        ///         plus a <c>SeenBattletag</c>. "Identity not settled here" is the honest answer in
        ///         both, and it keeps <see cref="ProfileReading" /> from growing a third state that
        ///         two callers would have to agree on.
        ///     </para>
        ///     <para>
        ///         <b>A string and not a <c>BattlenetAccount</c></b>, and not because it is shorter:
        ///         with the account in hand it is one careless line to look up the list from here,
        ///         and <c>Backend/Automation</c> deliberately does not know the gateway.
        ///     </para>
        /// </param>
        public static async Task<ProfileReading> ReadAsync(GameSession session,
            string? expected, CancellationToken token = default)
        {
            if (!TextReader.Available)
                return Nothing("Without text recognition rank and account level stay unread.");

            var map = session.Map.Profile;

            for (var attempt = 1; attempt <= OpenAttempts; attempt++)
            {
                var menu = Open(session, map);

                var (block, last) = await session.RetryAsync<IReadOnlyList<TextLine>>(async shot =>
                {
                    var (x, y, width, height) = session.Layout.Area(map.Progress);
                    var lines = await TextReader.ReadAsync(shot, x, y, width, height, 1);
                    return ValueUnder(lines, RankLabel) == null ? null : lines;
                }, ReadTimeout, $"profile overlay (attempt {attempt})", token);

                if (block == null)
                {
                    Close(session, map);
                    if (attempt < OpenAttempts) continue;

                    var path = GameSession.SaveDiagnostic(last, "profile-not-recognised");

                    // The second picture is the one that explains the first: it shows the
                    // moment after the right click, i.e. whether a context menu came up and
                    // where its entries sat.
                    var menuPath = GameSession.SaveDiagnostic(menu, "profile-menu");

                    // TWO DIFFERENT FAILURES have always collapsed into this one message, and
                    // saying "the line did not appear" was flatly wrong for one of them: the
                    // 25.08.2026 incident had "Sturmliga" reading perfectly on every one of 18
                    // attempts, while ValueUnder still returned null because nothing aligned
                    // underneath it. Read once more here, purely to name which case this was -
                    // cheap, because it only runs on the rare failing path.
                    var (x, y, width, height) = session.Layout.Area(map.Progress);
                    var lastLines = last == null
                        ? []
                        : await TextReader.ReadAsync(last, x, y, width, height, 1);
                    var reason = lastLines.Any(l => Normalise(l.Text).StartsWith(RankLabel))
                        ? $"the line '{RankLabel}' appeared, but nothing lined up under it"
                        : $"the line '{RankLabel}' did not appear";

                    return Nothing($"Profile overlay not recognised - {reason} in {OpenAttempts} " +
                                   $"attempts. Screenshots: {path} and {menuPath}");
                }

                try
                {
                    // The cross-check BEFORE evaluating: if a different battletag stands
                    // there, the overlay may not belong to this account. This exact check
                    // was missing from the old path via the rank screen.
                    //
                    // The reader does NOT decide the case. It delivers the values anyway,
                    // marks them as unresolved (Matches = false), and names the battletag it
                    // read. Whether this was a rename or a foreign screen can only be
                    // answered by the caller - that needs the list of all accounts, and
                    // Backend/Automation deliberately does not know it.
                    var seen = await ReadBattletag(session, map, last!, token);

                    // NOBODY SAID WHO SHOULD BE STANDING THERE - so this reading is the
                    // identification, and the values come along with it. Exactly ONE capture is
                    // taken for both, and that is the point: a second one would show a different
                    // frame, and the tag would then no longer belong to the numbers it identifies.
                    //
                    // There is deliberately no confirming second reading here, unlike the rename
                    // case. Two captures of the same static overlay are the same pixels and give
                    // the same misreading - the guard would be theatre. What actually guards this
                    // path is on the other side: the tag has to match exactly one stored account,
                    // and the realistic slips (I/l, 0/O, 5/S) produce a string that matches none.
                    var reading = expected == null || !string.Equals(seen, expected, StringComparison.OrdinalIgnoreCase)
                        ? Evaluate(block, seen) with { SeenBattletag = seen, Matches = false }
                        : Evaluate(block, expected);

                    // THE POINTS COME LAST, and only where there is a division to fill. They
                    // cost a second capture and a wait, and they are the one value here that
                    // is not on the overlay at all - see ReadRankPoints. Whatever they cost
                    // is spent after the rank is already in hand, so a tooltip that never
                    // comes up costs the points and not the reading.
                    if (reading.Tier is { } tier && tier.HasDivisions())
                    {
                        if (await ReadRankPoints(session, map, token) is { } points)
                            reading = reading with
                            {
                                RankPoints = points.Points, RankPointsMax = points.Max
                            };
                    }

                    return reading;
                }
                finally
                {
                    Close(session, map);
                }
            }

            return Nothing("Profile overlay not recognised.");
        }

        /// <summary>
        ///     Right-click on the profile picture, then "Profil ansehen". Both are measured
        ///     points of the calibration; the context menu is attached to the picture and
        ///     moves with it.
        ///     <para>
        ///         <b>It hands back the capture taken BETWEEN the two clicks</b>, and that is
        ///         the only picture that can say why an overlay did not open: whether the right
        ///         click landed at all, and where the context menu put its entries. Without it,
        ///         a failure leaves nothing but a clean menu screen - which looks the same
        ///         whether the first click missed or the second one did. It is saved only when
        ///         the reading fails; in the normal case it is 20 MB that is dropped.
        ///     </para>
        ///     <para>
        ///         The <c>BringToFront</c> calls that used to stand before each click are gone.
        ///         They were the ad-hoc version of a guard that now sits in one place, inside
        ///         <c>GameWindow.RequirePlayableBounds</c>, which every click and every capture
        ///         passes through.
        ///     </para>
        /// </summary>
        private static Screenshot Open(GameSession session, ScreenMap.ProfileSection map)
        {
            session.Click(map.Portrait, true);
            Thread.Sleep(900);

            var menu = session.Capture();

            session.Click(map.ViewProfile);
            Thread.Sleep(1200);

            return menu;
        }

        /// <summary>
        ///     Closes the overlay via its cross. Not with the Esc key: whether that works
        ///     here is not measured - the cross is.
        /// </summary>
        private static void Close(GameSession session, ScreenMap.ProfileSection map)
        {
            try
            {
                session.Click(map.Close);
                Thread.Sleep(600);
            }
            catch (Exception e)
            {
                // An open overlay is a cosmetic flaw, not a reason to fail the whole run -
                // the collection clears it away with the next click.
                Log.Warning(e, "Could not close the profile overlay");
            }
        }

        /// <summary>
        ///     The battletag from the header of the overlay - from the SAME capture that the
        ///     block was also read from. A second one costs 20 MB and could theoretically
        ///     show a different state, which would render the cross-check worthless right
        ///     there.
        /// </summary>
        private static async Task<string> ReadBattletag(GameSession session,
            ScreenMap.ProfileSection map, Screenshot shot, CancellationToken token)
        {
            var (x, y, width, height) = session.Layout.Area(map.Battletag);
            var lines = await TextReader.ReadAsync(shot, x, y, width, height, 1);
            token.ThrowIfCancellationRequested();

            // Name and number come as two lines ("Questqueen", "#2790") and stand side by
            // side - therefore assembled sorted by X, not by Y.
            var joined = string.Concat(lines.OrderBy(line => line.X).Select(line => line.Text));
            return new string(joined.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        }

        private static ProfileReading Evaluate(IReadOnlyList<TextLine> block, string battletag)
        {
            var notes = new List<string>();

            int? level = null;
            var levelText = ValueUnder(block, LevelLabel)?.Text;
            var digits = new string((levelText ?? "").Where(char.IsAsciiDigit).ToArray());
            if (digits.Length is > 0 and <= 4 && int.TryParse(digits, out var value) && value is >= 1 and <= 5000)
                level = value;
            else if (levelText != null)
                notes.Add($"account level '{levelText}' not usable");

            var rankText = ValueUnder(block, RankLabel)!.Text;
            if (Normalise(rankText).StartsWith(PlacementWord))
            {
                Log.Information("{Battletag}: placements pending, level {Level}", battletag, level);
                return new ProfileReading(level, null, 0, true,
                    Join("placements pending", notes));
            }

            if (!TryRank(rankText, out var tier, out var division))
            {
                notes.Add($"rank '{rankText}' matched no tier");
                return new ProfileReading(level, null, 0, null, Join("", notes));
            }

            Log.Information("{Battletag}: rank {Tier} {Division}, level {Level}",
                battletag, tier, division, level);
            var text = tier.HasDivisions() ? $"{tier.DisplayName()} {division}" : tier.DisplayName();
            return new ProfileReading(level, tier, division, false, Join(text, notes));
        }

        /// <summary>
        ///     The two numbers behind the rank, out of the tooltip that only comes up while
        ///     the pointer rests on the medal.
        ///     <para>
        ///         <b>The game does not name the bound, it names what is missing.</b> The
        ///         tooltip reads "497 Rangpunkte" and below it "503 Rangpunkte fuer Aufstieg
        ///         erforderlich" - so the size of the division is the SUM of the two, and
        ///         that is better than a constant: whoever assumed 1000 would be entering a
        ///         number the game never said, and would be wrong the day Blizzard changes it.
        ///     </para>
        ///     <para>
        ///         <b>Which line is which is decided by their order, not by a second word.</b>
        ///         Both start with a number and carry the same word; the current standing
        ///         stands above, what is still missing below. Sorted by Y, therefore, and not
        ///         by how they came out of text recognition.
        ///     </para>
        ///     <para>
        ///         Returns <c>null</c> on anything unclear - a tooltip that did not appear, a
        ///         line count other than two, a bound that would not be a bound. The caller
        ///         then writes nothing, and nothing is worth more here than a plausible
        ///         number: this one ends up as a ring somebody reads at a glance.
        ///     </para>
        /// </summary>
        private static async Task<(int Points, int Max)?> ReadRankPoints(GameSession session,
            ScreenMap.ProfileSection map, CancellationToken token)
        {
            var (x, y) = session.Layout.Point(map.RankMedal);
            session.HoverAt(x, y);

            // Magnified twofold, unlike the block above: the tooltip's lines are visibly
            // smaller than the overlay's, and at 1 the three-digit numbers came back with
            // a missing digit often enough to matter.
            var (lines, last) = await session.RetryAsync<IReadOnlyList<TextLine>>(async shot =>
            {
                var (ax, ay, width, height) = session.Layout.Area(map.RankTooltip);
                var read = await TextReader.ReadAsync(shot, ax, ay, width, height, 2);
                var numbered = read.Where(line => PointsPattern.IsMatch(Normalise(line.Text)))
                    .OrderBy(line => line.Y).ToList();
                return numbered.Count == 2 ? numbered : null;
            }, TooltipTimeout, "rank tooltip", token);

            if (lines == null)
            {
                var path = GameSession.SaveDiagnostic(last, "rank-tooltip-not-recognised");
                Log.Information("Rank tooltip not read - the two lines with '{Word}' did not " +
                                "appear. Screenshot: {Path}", RankPointsWord, path);
                return null;
            }

            var current = int.Parse(PointsPattern.Match(Normalise(lines[0].Text)).Groups[1].Value);
            var missing = int.Parse(PointsPattern.Match(Normalise(lines[1].Text)).Groups[1].Value);
            var bound = current + missing;

            // A division of zero points is no division, and a standing above its own bound
            // cannot come out of an addition - both would mean the two lines were not the
            // two lines. Better unread than a ring at a value nobody can explain.
            if (bound <= 0 || current > bound)
            {
                Log.Information("Rank tooltip implausible: {Current} + {Missing}", current, missing);
                return null;
            }

            Log.Information("Rank points {Current} of {Bound}", current, bound);
            return (current, bound);
        }

        /// <summary>
        ///     The value under a label. The next line below it that begins at the same edge
        ///     is searched for.
        ///     <para>
        ///         The edge condition is not decoration: to the left of the label sits a
        ///         circle with the same number in it once more (the account level stands in
        ///         the medal and next to it, the rank stands in the medal and next to it). The
        ///         tolerance depends on the line height and thus scales with the window size
        ///         by itself.
        ///     </para>
        ///     <para>
        ///         <b>Matched by WORD, not by line</b> - since 25.08.2026, and that changed
        ///         after a consistent failure on 25.08.2026 turned out to have nothing to do
        ///         with recognition at all: <c>Windows.Media.Ocr</c> had merged the medal's
        ///         digit into the same line as "Bronze 2", giving <c>"2 Bronze 2"</c> whose
        ///         line box starts at the medal - 89 points left of the text, well outside
        ///         tolerance. The account level survived the same hazard only because its two
        ///         numbers happened to stay on separate lines at the resolutions this was
        ///         checked against; nothing guaranteed that, and the rank shows it does not
        ///         always hold. Looking at where the aligned WORD starts, rather than where
        ///         the whole (possibly merged) line starts, finds the value regardless of how
        ///         the recognizer chose to group that particular capture - and the medal digit,
        ///         sitting to its left, is simply not part of what gets read out.
        ///     </para>
        ///     <para>
        ///         A line with no word boxes at all (nothing currently produces one, but
        ///         nothing built to require it either - see <see cref="TextWord" />) falls back
        ///         to the old whole-line edge check, so this degrades rather than throws.
        ///     </para>
        /// </summary>
        internal static TextLine? ValueUnder(IReadOnlyList<TextLine> lines, string label)
        {
            var head = lines.FirstOrDefault(line => Normalise(line.Text).StartsWith(label));
            if (head == null) return null;

            foreach (var line in lines.Where(line => line.Y > head.Y).OrderBy(line => line.Y))
            {
                var aligned = AlignedValue(line, head);
                if (aligned != null) return aligned;
            }

            return null;
        }

        /// <summary>
        ///     Cuts a candidate value line down to the words that actually align with the
        ///     label, dropping anything the recognizer glued on further left (a medal digit,
        ///     for instance). <c>null</c> when nothing in this line aligns - the caller then
        ///     tries the next line down, exactly as the old whole-line check did.
        /// </summary>
        private static TextLine? AlignedValue(TextLine line, TextLine head)
        {
            if (line.Words.Count == 0)
                return Math.Abs(line.X - head.X) <= head.Height ? line : null;

            var ordered = line.Words.OrderBy(word => word.X).ToList();
            var index = ordered.FindIndex(word => Math.Abs(word.X - head.X) <= head.Height);
            if (index < 0) return null;

            var kept = ordered.Skip(index).ToList();
            var left = kept.Min(word => word.X);
            var top = kept.Min(word => word.Y);
            var right = kept.Max(word => word.X + word.Width);
            var bottom = kept.Max(word => word.Y + word.Height);

            return new TextLine(string.Join(" ", kept.Select(word => word.Text)),
                left, top, right - left, bottom - top);
        }

        internal static bool TryRank(string text, out HotsRankTier tier, out int division)
        {
            tier = HotsRankTier.None;
            division = 0;

            var found = RankPattern.Match(Normalise(text));
            if (!found.Success || !Tiers.TryGetValue(found.Groups[1].Value, out tier))
            {
                tier = HotsRankTier.None;
                return false;
            }

            // Master and Grand Master carry a score or leaderboard rank in the game instead
            // of a division - there, the absence of the digit is the normal case, otherwise
            // an error.
            if (!tier.HasDivisions()) return true;
            if (!found.Groups[2].Success) return false;

            division = int.Parse(found.Groups[2].Value);
            return true;
        }

        /// <summary>
        ///     Lowercase, letters and digits only, umlauts and eszett resolved. This makes
        ///     "Großmeister" the same as "Grossmeister" - and text recognition delivers
        ///     either, depending on how the eszett is hit.
        /// </summary>
        internal static string Normalise(string text)
        {
            // Strip accents BEFORE filtering: otherwise "maître" would stay with its accent,
            // and a circumflex missed by the recognition would turn a match into a miss. The
            // rule lives in TextNormalisation and applies to all four comparisons against
            // game text - the eszett is handled there too.
            var stripped = TextNormalisation.StripAccents(text).ToLowerInvariant();

            var builder = new StringBuilder();
            foreach (var character in stripped)
                if (char.IsLetterOrDigit(character)) builder.Append(character);
                else if (builder.Length > 0 && builder[^1] != ' ') builder.Append(' ');

            return builder.ToString().Trim();
        }

        private static string Join(string head, List<string> notes)
        {
            var parts = new List<string>();
            if (head.Length > 0) parts.Add(head);
            parts.AddRange(notes);
            return parts.Count == 0 ? "Nothing read." : string.Join("; ", parts);
        }

        private static ProfileReading Nothing(string note)
        {
            return new ProfileReading(null, null, 0, null, note);
        }
    }
}
