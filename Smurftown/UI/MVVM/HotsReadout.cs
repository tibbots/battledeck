using Serilog;
using Smurftown.Backend.Automation;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Texts;

namespace Smurftown.UI.MVVM
{
    /// <summary>
    ///     What is read out of a client that is signed in, and what of it goes into
    ///     <c>data.yaml</c>. Four steps, individually guarded, in a fixed order.
    ///     <para>
    ///         <b>It exists because there are two entrances and only one read-out.</b> The account
    ///         row starts the game and signs an account in; the header chip attaches to a client
    ///         that is already running and signed in. What happens afterwards is the same thing in
    ///         both cases, and a second copy of it would be the place where the two drift apart -
    ///         the collection paging, the merge rule for heroes, the "0 is written, null is not"
    ///         of the penalty.
    ///     </para>
    ///     <para>
    ///         <b>It lives in <c>UI/</c> and not next to the readers in
    ///         <c>Backend/Automation/</c></b>, although it looks like it belongs there: it asks
    ///         the gateway who owns a battletag, and <c>Backend/Automation</c> deliberately does
    ///         not know the gateways. Whoever moves it down has to take that question with it.
    ///     </para>
    ///     <para>
    ///         <b>It writes into <see cref="HotsRegionData" /> and saves nothing.</b> The
    ///         <c>ReadAt</c> stamp and the <c>AddOrUpdate</c> stay with the caller - only there is
    ///         it known whether the run got far enough to be worth a timestamp.
    ///     </para>
    ///     <para>
    ///         <b>Nothing here touches the UI thread.</b> Capturing and clicking block, and the
    ///         collection takes over a minute; the steps therefore collect their words in
    ///         <c>changes</c> and <c>problems</c> instead of showing a toast, and the caller shows
    ///         them after the return.
    ///     </para>
    /// </summary>
    static class HotsReadout
    {
        private static readonly BattlenetAccountGateway _gateway = BattlenetAccountGateway.Instance;

        /// <summary>
        ///     The whole read-out, in the one order that is not a matter of taste.
        /// </summary>
        /// <param name="resolved">
        ///     A profile reading whose identity is already settled - or <c>null</c> to read the
        ///     profile here.
        ///     <para>
        ///         The two cases are the two entrances again. Coming from an account row, the
        ///         account is known and the profile is read here, with its battletag as a
        ///         cross-check. Coming from a running client, the profile has <b>already</b> been
        ///         read - that is how the account was found in the first place - and reading it a
        ///         second time would cost another eight seconds to learn what is already known.
        ///     </para>
        ///     <para>
        ///         Whoever hands one in hands in a reading with <c>Matches</c> set to <c>true</c>:
        ///         <see cref="ApplyProfile" /> refuses anything else, and that refusal is the last
        ///         floor before <c>data.yaml</c>.
        ///     </para>
        /// </param>
        public static async Task ReadAll(GameSession session, BattlenetAccount account,
            HotsRegionData data, ProfileReading? resolved, bool openChests,
            IProgress<string> progress, List<string> changes, List<string> problems)
        {
            // Chests FIRST: they change shards, gold and occasionally the hero list.
            // Read afterwards the stored state is correct, read before it would be stale immediately.
            if (openChests) await OpenChests(session, account, progress, changes, problems);

            if (resolved == null) await ReadProfile(session, account, data, changes, problems);
            else ApplyProfile(resolved, data, changes, problems);

            await ReadPenalty(session, account, data, changes, problems);
            await ReadHeader(session, account, data, changes, problems);
            await ReadHeroes(session, account, data, progress, changes, problems);
        }

        /// <summary>
        ///     Opens all unopened loot chests. Runs before the read-out, not after.
        ///     <para>
        ///         Own error branch just like the read steps: if the opening gets stranded, rank,
        ///         stats and heroes should still arrive. The counter in the header is
        ///         re-read right afterwards anyway and then carries the real state.
        ///     </para>
        /// </summary>
        private static async Task OpenChests(GameSession session, BattlenetAccount account,
            IProgress<string> progress, List<string> changes, List<string> problems)
        {
            try
            {
                var result = await LootOpener.OpenAllAsync(session, progress);
                Log.Information("{Battletag}: {Note}", account.Battletag(), result.Note);

                if (result.Opened > 0)
                    changes.Add(result.Opened == 1
                        ? Strings.Current["change.chestOne"]
                        : Strings.Format("change.chestMany", result.Opened));

                // Everything except "none left at the end" is a problem - including the case that the
                // counter was no longer readable (null). That there was no chest at all is not one.
                if (result.Remaining != 0) problems.Add(result.Note);
            }
            catch (Exception e)
            {
                Log.Error(e, "{Battletag}: opening chests failed", account.Battletag());
                problems.Add(Strings.Format("problem.chestsFailed", e.Message));
            }
        }

        /// <summary>
        ///     Reads rank, placement status and account level from the profile overlay and
        ///     adopts what was really there.
        ///     <para>
        ///         Without confirmation, and that is a deliberate reversal: as long as reading
        ///         was its own button, the edit dialog opened afterwards. Now it hangs
        ///         on the start, and whoever wants to start in order to play shouldn't first switch
        ///         back to the app. This is only bearable because every value is secured
        ///         individually - "no idea" is a valid answer and writes nothing - and
        ///         because the toast names every change in plain text.
        ///     </para>
        ///     <para>
        ///         <b>With open placement matches the rank stays as is.</b> The overlay then
        ///         shows the word "Placement" instead of a tier; the stored rank is
        ///         that of the previous season and not an invalid value. It is not cleared, only the
        ///         display changes.
        ///     </para>
        ///     <para>
        ///         <b>Identity comes before everything else.</b> If the overlay shows a different
        ///         battletag than the stored one, nothing is written here - then
        ///         <see cref="AdoptRenamedBattletag" /> decides whether that was a rename
        ///         or a foreign screen. Only what comes back from there goes on to
        ///         <see cref="ApplyProfile" />.
        ///     </para>
        /// </summary>
        private static async Task ReadProfile(GameSession session, BattlenetAccount account,
            HotsRegionData data, List<string> changes, List<string> problems)
        {
            try
            {
                var reading = await ProfileReader.ReadAsync(session, account.Battletag());
                Log.Information("{Battletag}: {Note}", account.Battletag(), reading.Note);

                // If the battletag doesn't match, the identity is open and the values might
                // belong to a foreign account. Clarify first, then write - and if it
                // can't be clarified, don't write at all.
                if (!reading.Matches)
                {
                    var confirmed = await AdoptRenamedBattletag(session, account, reading, changes, problems);
                    if (confirmed == null) return;
                    reading = confirmed;
                }

                ApplyProfile(reading, data, changes, problems);
            }
            catch (Exception e)
            {
                Log.Error(e, "{Battletag}: reading the profile failed", account.Battletag());
                problems.Add(Strings.Format("problem.profileFailed", e.Message));
            }
        }

        /// <summary>
        ///     The profile shows a different battletag than the stored one. Two explanations
        ///     are possible, and they lead to opposite behavior:
        ///     <list type="bullet">
        ///         <item>
        ///             The human has <b>renamed</b> the account at Blizzard. Then the
        ///             read tag is the truth and the stored one is stale.
        ///         </item>
        ///         <item>
        ///             We are photographing the screen of a <b>foreign</b> account. Then everything
        ///             about this reading is worthless and must not touch anything.
        ///         </item>
        ///     </list>
        ///     <para>
        ///         Three conditions must come together, otherwise nothing is adopted:
        ///     </para>
        ///     <list type="number">
        ///         <item>
        ///             The read text must have the <b>form</b> of a battletag at all
        ///             (<see cref="BattlenetAccount.TrySplitBattletag" />). Catches gross
        ///             reading errors that don't even look like a battletag in the first place.
        ///         </item>
        ///         <item>
        ///             The tag must not belong to <b>any other account</b>
        ///             (<see cref="BattlenetAccountGateway.OwnerOf" />). This is the safeguard
        ///             against the dangerous case: on a machine with many accounts,
        ///             a foreign screen is the more likely explanation, and then the
        ///             read tag stands in our own list.
        ///         </item>
        ///         <item>
        ///             A <b>second capture</b> must show the same tag. Without it a
        ///             reading error would rename the account: <c>PITAPAN#2523</c> becomes
        ///             <c>PlTAPAN#2523</c>, form valid, no collision - and the name would be
        ///             broken. A real rename reads the same thing twice, a reading error
        ///             almost never.
        ///         </item>
        ///     </list>
        ///     <para>
        ///         The second capture costs around eight seconds and only occurs when something
        ///         really diverges - the normal case stays at one.
        ///     </para>
        ///     <para>
        ///         What comes back is the <b>second</b> reading, with <c>Matches</c> set: from here
        ///         on the identity is clarified, and <see cref="ApplyProfile" /> is allowed to write. On
        ///         every abort <c>null</c> - then the account stays untouched.
        ///     </para>
        /// </summary>
        private static async Task<ProfileReading?> AdoptRenamedBattletag(GameSession session,
            BattlenetAccount account, ProfileReading first, List<string> changes,
            List<string> problems)
        {
            // Two occasions lead here, and they deserve different words. Since
            // 21.08.2026 the battletag is read instead of typed, so a freshly created account
            // has none at all - that is the normal case and not a suspicion. The three safeguards
            // below still apply unchanged though: a reading error would otherwise set a permanently
            // wrong name, and a foreign screen would name the wrong account.
            var firstRead = !account.HasBattletag;
            var label = account.DisplayName;
            var seen = first.SeenBattletag;

            if (!BattlenetAccount.TrySplitBattletag(seen, out var name, out var discriminator))
            {
                problems.Add(firstRead
                    ? Strings.Format("problem.tagNotATagFirst", seen, label)
                    : Strings.Format("problem.tagNotATag", seen, label));
                return null;
            }

            var owner = _gateway.OwnerOf(seen!, account);
            if (owner != null)
            {
                Log.Warning("Foreign profile: {Seen} belongs to {Email}, read for {Expected}",
                    seen, owner.Email, label);
                problems.Add(Strings.Format("problem.tagForeign", seen, owner.Email, label));
                return null;
            }

            Log.Information(firstRead
                    ? "{Expected}: no battletag stored yet, profile shows '{Seen}' - verifying with a second capture"
                    : "{Expected}: profile shows '{Seen}' - verifying with a second capture",
                label, seen);
            var second = await ProfileReader.ReadAsync(session, account.Battletag());

            // second.Matches stays false on a first read, because the stored tag is still
            // empty - the condition therefore doesn't wrongly kick in there.
            if (second.Matches || !string.Equals(second.SeenBattletag, seen, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(Strings.Format("problem.tagAmbiguous", seen,
                    second.SeenBattletag ?? label, label));
                return null;
            }

            account.Name = name;
            account.Discriminator = discriminator;

            if (firstRead)
            {
                changes.Add(Strings.Format("change.battletagFirst", account.Battletag()));
                Log.Information("Battletag read for the first time: {Email} is {After}",
                    account.Email, account.Battletag());
            }
            else
            {
                changes.Add(Strings.Format("change.battletagRenamed", label, account.Battletag()));
                Log.Warning("Rename adopted: {Before} -> {After}", label, account.Battletag());
            }

            return second with { Matches = true };
        }

        /// <summary>
        ///     Adopts what stood in the profile. Every value individually - what was not read
        ///     stays as it was.
        /// </summary>
        private static void ApplyProfile(ProfileReading reading, HotsRegionData data,
            List<string> changes, List<string> problems)
        {
            // Double safety net. Only what has cleared identity may come here; whoever
            // calls this method from elsewhere in the future would rather fall through here than into data.yaml.
            if (!reading.Matches) return;

            if (reading.AccountLevel != null && reading.AccountLevel != data.AccountLevel)
            {
                var before = data.AccountLevel;
                data.AccountLevel = reading.AccountLevel;
                changes.Add(before == null
                    ? Strings.Format("change.levelFirst", reading.AccountLevel)
                    : Strings.Format("change.level", before, reading.AccountLevel));
            }

            if (reading.PlacementsPending is { } pending && pending != data.PlacementsPending)
            {
                data.PlacementsPending = pending;
                changes.Add(Strings.Current[pending
                    ? "change.placementsPending"
                    : "change.placementsDone"]);
            }

            if (reading.Tier is { } tier)
            {
                if (data.Tier == tier && data.Division == reading.Division) return;

                var before = data.RankName();
                data.Tier = tier;
                data.Division = reading.Division;
                var now = data.RankName();
                changes.Add(before.Length == 0
                    ? Strings.Format("change.rankFirst", now)
                    : Strings.Format("change.rank", before, now));
            }
            else if (reading.PlacementsPending != true)
            {
                problems.Add(reading.Note);
            }
        }

        /// <summary>
        ///     Reads gold, shards and gems. Every value individually: what was not read
        ///     stays as it was instead of falling to null - a number that existed yesterday is better
        ///     than a gap because of a blurred capture. The account level comes from the
        ///     profile overlay, see <see cref="ReadProfile" />.
        /// </summary>
        /// <summary>
        ///     Reads the leaver-penalty status and writes <see cref="HotsRegionData.PenaltyGames" />
        ///     of the region currently signed in to.
        ///     <para>
        ///         <b>Until 21.08.2026 the field was pure hand maintenance</b>, and the
        ///         hand-entered value was off: for MUGGLE#21197 it said 1 there, the game said 3.
        ///     </para>
        ///     <para>
        ///         <b>A 0 is written, a <c>null</c> is not.</b> If the warning icon is missing on
        ///         a menu screen, that is proof that no penalty is running anymore - otherwise
        ///         an expired entry would stand forever. If on the other hand it wasn't possible to look at all
        ///         (wrong screen, no OCR, unreadable hint), the
        ///         stored value stays untouched. Same rule as for the four stats.
        ///     </para>
        /// </summary>
        private static async Task ReadPenalty(GameSession session, BattlenetAccount account,
            HotsRegionData data, List<string> changes, List<string> problems)
        {
            try
            {
                var reading = await PenaltyReader.ReadAsync(session);
                Log.Information("{Battletag}: {Note}", account.Battletag(), reading.Note);

                if (reading.Games == null)
                {
                    problems.Add(reading.Note);
                    return;
                }

                var before = data.PenaltyGames;
                if (reading.Games == before) return;

                data.PenaltyGames = reading.Games.Value;
                changes.Add(Strings.Format("change.penalty", before, reading.Games));
            }
            catch (Exception e)
            {
                Log.Error(e, "{Battletag}: reading the leaver penalty failed", account.Battletag());
                problems.Add(Strings.Format("problem.penaltyFailed", e.Message));
            }
        }

        private static async Task ReadHeader(GameSession session, BattlenetAccount account,
            HotsRegionData data, List<string> changes, List<string> problems)
        {
            try
            {
                var reading = await HeaderReader.ReadAsync(session);
                Note(Strings.Current["currency.gold"], data.Gold, reading.Gold,
                    v => data.Gold = v);
                Note(Strings.Current["currency.shards"], data.Shards, reading.Shards,
                    v => data.Shards = v);
                Note(Strings.Current["currency.gems"], data.Gems, reading.Gems,
                    v => data.Gems = v);
                Note(Strings.Current["currency.chests"], data.LootChests, reading.LootChests,
                    v => data.LootChests = v);
            }
            catch (Exception e)
            {
                Log.Error(e, "{Battletag}: reading the header failed", account.Battletag());
                problems.Add(Strings.Format("problem.statsFailed", e.Message));
            }

            void Note(string label, int? before, int? now, Action<int?> assign)
            {
                if (now == null || now == before) return;
                assign(now);
                changes.Add(before == null
                    ? Strings.Format("change.valueFirst", label, now)
                    : Strings.Format("change.value", label, before, now));
            }
        }

        /// <summary>
        ///     Reads the acquired heroes from the collection.
        ///     <para>
        ///         <b>The target count from the sidebar no longer decides whether, but how</b>
        ///         it is written:
        ///     </para>
        ///     <list type="table">
        ///         <item>
        ///             <term>complete</term>
        ///             <description>
        ///                 replace. Only a complete reading may take something away - this way
        ///                 it also corrects a wrong hand entry.
        ///             </description>
        ///         </item>
        ///         <item>
        ///             <term>incomplete</term>
        ///             <description>
        ///                 merge. What was read is added; nothing is deleted.
        ///             </description>
        ///         </item>
        ///     </list>
        ///     <para>
        ///         Until 21.08.2026, in this case <b>nothing at all</b> was adopted, because
        ///         writing replaces the list and a half-read list would thereby unnoticed have
        ///         deleted entries. The rule was stricter than necessary: in Heroes of the
        ///         Storm heroes <b>cannot be lost</b>, ownership only grows.
        ///         Merging can therefore never cost data - and throwing away 31 of 32 read cards
        ///         because one tile wasn't readable cost some.
        ///     </para>
        ///     <para>
        ///         The price stands nonetheless: a wrong hand entry survives an incomplete
        ///         reading and only disappears on the next complete run. That's why
        ///         the incomplete reading is still worth a <c>problems</c> message, even if it
        ///         contributed something.
        ///     </para>
        /// </summary>
        private static async Task ReadHeroes(GameSession session, BattlenetAccount account,
            HotsRegionData data, IProgress<string> progress, List<string> changes,
            List<string> problems)
        {
            try
            {
                var reading = await CollectionReader.ReadAsync(session, progress);
                Log.Information("{Battletag}: {Note}", account.Battletag(), reading.Note);

                var before = new HashSet<string>(data.Heroes, StringComparer.OrdinalIgnoreCase);
                var read = new HashSet<string>(reading.HeroIds, StringComparer.OrdinalIgnoreCase);

                // Complete replaces, incomplete merges. The difference depends solely
                // on the target count from the sidebar.
                var now = reading.Complete
                    ? read
                    : new HashSet<string>(before.Concat(read), StringComparer.OrdinalIgnoreCase);

                if (!reading.Complete)
                    problems.Add(Strings.Format("problem.heroesMerged", reading.Note));

                if (before.SetEquals(now)) return;

                data.Heroes = Ordered(now);
                var added = now.Except(before).Count();
                var removed = before.Except(now).Count();
                changes.Add(removed == 0
                    ? Strings.Format("change.heroesAdded", added, now.Count)
                    : Strings.Format("change.heroes", before.Count, now.Count, added, removed));
            }
            catch (Exception e)
            {
                Log.Error(e, "{Battletag}: reading heroes failed", account.Battletag());
                problems.Add(Strings.Format("problem.heroesFailed", e.Message));
            }
        }

        /// <summary>
        ///     Identifiers in display order and without duplicates - the same form in which
        ///     the edit dialog and the rotation also save.
        ///     <para>
        ///         <b>Unknown identifiers survive.</b> A <c>data.yaml</c> from a newer
        ///         app version can contain heroes that this version doesn't know;
        ///         <see cref="HotsHeroCatalog.Resolve" /> leaves them out, and that would be exactly
        ///         a deletion here. They are therefore appended again at the end. Without this, the
        ///         merge would have broken its promise before it ran for the first time.
        ///     </para>
        /// </summary>
        private static List<string> Ordered(IReadOnlyCollection<string> heroIds)
        {
            var known = HotsHeroCatalog.Resolve(heroIds).Select(hero => hero.Id).ToList();
            known.AddRange(heroIds.Where(id => HotsHeroCatalog.Find(id) == null));
            return known;
        }
    }
}
