using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using Serilog;
using Smurftown.Backend.Entity;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Smurftown.Backend.Gateway
{
    public class BattlenetAccountGateway
    {
        public static readonly BattlenetAccountGateway Instance = new();
        private readonly string _configFile;

        // IgnoreUnmatchedProperties: without this, a data.yaml containing fields from a
        // newer app version throws a YamlException when read by an older version. It is
        // also the prerequisite for reading the same file a second time as LegacyAccount -
        // there almost no key matches.
        private readonly IDeserializer _yamlIn = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        private readonly ISerializer _yamlOut = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        private BattlenetAccountGateway()
        {
            _configFile = Path.Combine(Directories.UserPath, "data.yaml");
            foreach (var account in ReadFromConfigFile())
            {
                BattlenetAccounts.Add(account);
            }

            RebuildRows();

            AccountRegionsFiltered = CollectionViewSource.GetDefaultView(AccountRegions);
            AccountRegionsFiltered.SortDescriptions.Add(
                new SortDescription(nameof(AccountRegion.LatestInteractionAt), ListSortDirection.Descending));
            // DisplayName and not Name: since the battletag is read instead of typed,
            // fresh accounts have no name, and they would otherwise all stand together
            // under the empty key. DisplayName falls back to the email in that case.
            AccountRegionsFiltered.SortDescriptions.Add(new SortDescription(
                nameof(AccountRegion.DisplayName), ListSortDirection.Ascending));
            // Region last, so that the two rows of ONE account always stand in the same
            // order relative to each other - Europe, America, Asia.
            AccountRegionsFiltered.SortDescriptions.Add(new SortDescription(
                nameof(AccountRegion.RegionOrder), ListSortDirection.Ascending));
        }

        /// <summary>The accounts, as they stand in <c>data.yaml</c> - one per account.</summary>
        public ObservableCollection<BattlenetAccount> BattlenetAccounts { get; } = [];

        /// <summary>
        ///     The rows of the overview: one per account <b>per region it plays any game
        ///     in</b>. Not stored, but rebuilt from <see cref="BattlenetAccounts" /> on
        ///     every change.
        ///     <para>
        ///         The union over the games and not one row per game: the game filter is
        ///         exclusive, so a row shows exactly one game anyway, and which one is a
        ///         question the filter answers rather than the list.
        ///     </para>
        /// </summary>
        public ObservableCollection<AccountRegion> AccountRegions { get; } = [];

        public ICollectionView AccountRegionsFiltered { get; }

        /// <summary>
        ///     Rebuilds the row list. Call after <b>every</b> change to an account -
        ///     even after one that only affects a single field: the regions of a game may
        ///     have changed along with it, and then the number of rows changes.
        ///     <para>
        ///         Rebuilding from scratch instead of tracking individually is the cheaper
        ///         truth at 28 accounts - a diff between the old and new row set would be a
        ///         second place where the list can go wrong.
        ///     </para>
        /// </summary>
        private void RebuildRows()
        {
            AccountRegions.Clear();
            foreach (var account in BattlenetAccounts)
            foreach (var region in BattlenetRegions.InDisplayOrder)
            {
                if (account.Covers(region)) AccountRegions.Add(new AccountRegion(account, region));
            }
        }

        public static Predicate<T> Or<T>(Predicate<T> p1, Predicate<T> p2)
        {
            return x => p1(x) || p2(x);
        }

        /// <summary>
        ///     Every condition lets through as long as it is not set - if nothing at all is
        ///     set, the whole chain is true and everything is shown.
        ///     <para>
        ///         The game filter is <b>exclusive</b>: one game or none, where four
        ///         independent checkboxes used to stand. That drops the combination -
        ///         "Overwatch AND HotS" can no longer be asked. The reason is that the same
        ///         filter now also determines the view of the rows (see <c>GameFocus</c>),
        ///         and two chosen games would have no answer there.
        ///     </para>
        ///     <para>
        ///         <b>The region filter is exactly the same</b> - exclusive and always set.
        ///         Unlike with the game, this is not a technical necessity (a row shows
        ///         exactly one region anyway), but a UX decision: two adjacent filter
        ///         blocks with different logic would be the more expensive surprise. The
        ///         price is the same as with the game - whoever only plays in America is
        ///         invisible under Europe and reachable only via their own abbreviation.
        ///     </para>
        ///     <para>
        ///         <b>The two of them belong together and are asked as one question</b>:
        ///         <c>PlaysIn(game, region)</c> and not <c>Plays(game)</c>. Since 22.08.2026
        ///         the regions hang on the game (see <c>BattlenetAccount.RegionsByGame</c>),
        ///         so an account that plays Heroes of the Storm in Europe and America but
        ///         World of Warcraft only in Europe has a row under America - and that row
        ///         must not appear when the filter stands on World of Warcraft. The row list
        ///         itself cannot decide this: it is built from the union over all games, or
        ///         switching the game filter would have to rebuild it.
        ///     </para>
        ///     <para>
        ///         <b>The archive is the exception in this chain.</b> Every other condition
        ///         has the form "not set, so it lets through"; <c>showArchived</c> never lets
        ///         everyone through, but toggles between two halves - active or archived,
        ///         never both. It is a view choice like the game filter and not a filter,
        ///         and that is exactly why it stands first: an archived account should not
        ///         show up even when the search text or hero filter matches it.
        ///     </para>
        /// </summary>
        private Predicate<AccountRegion> CreatePredicate(string searchQuery, string? game,
            BattlenetRegion region,
            IReadOnlyCollection<string> heroIds, IReadOnlyCollection<string> freeHeroIds,
            bool showArchived)
        {
            return row =>
                row.Account.Inactive == showArchived &&
                row.Region == region &&
                (string.IsNullOrEmpty(searchQuery) || Contains(row.Account, searchQuery)) &&
                (game == null || row.Account.PlaysIn(game, region)) &&
                (heroIds.Count == 0 || CanPlayAnyHero(row, heroIds, freeHeroIds));
        }

        /// <summary>
        ///     Can this account play one of the chosen heroes - <b>in this region</b>?
        ///     Ownership OR free rotation, one match is enough. The question behind it is
        ///     "who can play any of these", not "who has all of them".
        ///     <para>
        ///         Ownership depends on the region: the same battletag can have 40 heroes
        ///         in Europe and none in America. The <b>free rotation</b> does not depend
        ///         on that - it applies to everyone everywhere, that's why it stands
        ///         independent of the game state.
        ///     </para>
        ///     <para>
        ///         The rotation only counts for accounts that play HotS <b>in this
        ///         region</b>. Without this condition, pure Overwatch accounts that the
        ///         filter has never matched before would also show up in the list for a free
        ///         hero - and since 22.08.2026 so would the American row of an account that
        ///         only plays Heroes of the Storm in Europe.
        ///     </para>
        ///     <para>
        ///         Public and static, because the match counter in the picker ("n accounts
        ///         match") needs the same rule. Written twice it would drift apart sooner
        ///         or later - like the battletag-to-Windows-user derivation.
        ///     </para>
        /// </summary>
        public static bool CanPlayAnyHero(AccountRegion row, IReadOnlyCollection<string> heroIds,
            IReadOnlyCollection<string> freeHeroIds)
        {
            var owned = row.Hots?.Heroes;
            if (owned != null && heroIds.Any(id => owned.Contains(id, StringComparer.OrdinalIgnoreCase))) return true;

            return row.Account.PlaysIn(Games.Hots, row.Region)
                   && heroIds.Any(id => freeHeroIds.Contains(id, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        ///     Which <b>other</b> account carries this battletag? <c>null</c> if none -
        ///     then the tag is free.
        ///     <para>
        ///         This is the second safeguard of the rename path when reading from the
        ///         game. If the profile overlay finds a different battletag than the stored
        ///         one, there are two explanations: the human renamed the account at
        ///         Blizzard, or we are photographing the screen of a <b>foreign</b>
        ///         account. The second case is the dangerous one, and on a machine with
        ///         many accounts it is also the more likely one - then the read tag is,
        ///         with high probability, already in this list. A match therefore means:
        ///         adopt nothing.
        ///     </para>
        ///     <para>
        ///         The battletag is <b>global</b> at Blizzard and the same in every region -
        ///         that's why this method asks for the account and not for a pair of
        ///         account and region.
        ///     </para>
        ///     <para>
        ///         <paramref name="except" /> is the account in question - it must not
        ///         block itself. Comparison is via the email, because that is the
        ///         identity (<c>BattlenetAccount.Equals</c> and <c>GetHashCode</c> depend on
        ///         it alone); the name is precisely not that, otherwise this method wouldn't
        ///         exist. That's why a rename also survives <c>AddOrUpdate</c>: the
        ///         Remove+Add finds the old entry via the unchanged email.
        ///     </para>
        /// </summary>
        public BattlenetAccount? OwnerOf(string battletag, BattlenetAccount except)
        {
            return BattlenetAccounts.FirstOrDefault(other =>
                !string.Equals(other.Email, except.Email, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(other.Battletag(), battletag, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        ///     Which account carries this battletag - the counterpart to <see cref="OwnerOf" />,
        ///     and the only place that answers the question at all.
        ///     <para>
        ///         <b>Two hits give <c>null</c>, not the first one.</b> A duplicate battletag in
        ///         the list is a broken state (the identity of an entry is its email, so nothing
        ///         stops two of them from carrying the same tag), and picking one of the two would
        ///         write a whole reading into an account chosen by list order. Ambiguous means
        ///         unanswered here.
        ///     </para>
        ///     <para>
        ///         <b>The archive counts.</b> An archived account still owns its battletag, and if
        ///         it is the one signed into the client, then that is whose numbers these are.
        ///     </para>
        ///     <para>
        ///         <b>This is what secures the running-client path.</b> There the read tag alone
        ///         decides whose data gets written, and the safeguard is exactly that it has to
        ///         match a stored account character for character: the realistic reading errors
        ///         (I/l, 0/O, 5/S) turn a battletag into a string that matches nothing, and then
        ///         nothing is written. See <c>ProfileReader.ReadAsync</c>.
        ///     </para>
        /// </summary>
        public BattlenetAccount? FindByBattletag(string? battletag)
        {
            if (string.IsNullOrWhiteSpace(battletag)) return null;

            var hits = BattlenetAccounts
                .Where(account => account.HasBattletag &&
                                  string.Equals(account.Battletag(), battletag,
                                      StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();

            if (hits.Count == 1) return hits[0];

            if (hits.Count > 1)
                Log.Warning("Battletag {Battletag} is carried by more than one account - " +
                            "nothing is resolved", battletag);

            return null;
        }

        private bool Contains(BattlenetAccount account, string searchQuery)
        {
            var parts = new List<string> { account.Name, account.Discriminator, account.Email };
            return searchQuery.Split(" ")
                .All(word => parts.Any(part => part.Contains(word, StringComparison.OrdinalIgnoreCase)));
        }

        public void FilterBy(string searchQuery, string? game,
            BattlenetRegion region,
            IReadOnlyCollection<string> heroIds, IReadOnlyCollection<string> freeHeroIds,
            bool showArchived)
        {
            var filter = CreatePredicate(searchQuery, game, region, heroIds, freeHeroIds, showArchived);
            AccountRegionsFiltered.Filter = obj =>
            {
                if (obj is AccountRegion row)
                {
                    return filter?.Invoke(row) ?? true;
                }

                return false;
            };
        }

        public void AddOrUpdate(BattlenetAccount account)
        {
            BattlenetAccounts.Remove(account);
            BattlenetAccounts.Add(account);
            SaveToConfigFile();
            RebuildRows();
        }

        /// <summary>
        ///     Archives an account or brings it back.
        ///     <para>
        ///         The <c>Refresh</c> is necessary and not caution: the list itself doesn't
        ///         change, only a field on one element - an <c>ICollectionView</c> doesn't
        ///         notice that and would leave the row standing until some other filter is
        ///         set anew.
        ///     </para>
        /// </summary>
        public void SetArchived(BattlenetAccount account, bool archived)
        {
            account.Inactive = archived;
            SaveToConfigFile();
            AccountRegionsFiltered.Refresh();
        }

        public void Remove(BattlenetAccount account)
        {
            BattlenetAccounts.Remove(account);
            SaveToConfigFile();
            RebuildRows();
        }

        private List<BattlenetAccount> ReadFromConfigFile()
        {
            ensureConfigFileExists();
            var content = File.ReadAllText(_configFile);
            var accounts = _yamlIn.Deserialize<List<BattlenetAccount>>(new StringReader(content)) ?? [];

            // The file is set aside by DataBackup at startup, before any gateway runs -
            // per version and for every YAML file, rather than one .bak per migration.
            if (MigrateToPerGameRegions(accounts, content))
            {
                WriteAccounts(accounts);
                VerifyMigration(accounts, content);
            }

            // SECOND SAFEGUARD against the invisible row, and it deliberately stands AFTER
            // the migration: its marker is exactly the empty game map, placed before it
            // it would never run again. What remains here is only what the migration
            // couldn't fill either - a manually edited file, for example. Repair happens
            // quietly, but not silently; nothing is written here, the next change takes
            // care of that.
            foreach (var account in accounts.Where(account => account.RegionsByGame.Count == 0))
            {
                account.SetRegions(Games.Hots, [BattlenetRegion.Europe]);
                Log.Warning("{Email} played no game in any region and would not have been " +
                            "visible - set to Heroes of the Storm in Europe", account.Email);
            }

            return accounts;
        }

        /// <summary>
        ///     Brings a <c>data.yaml</c> from before 22.08.2026 onto the regions-per-game
        ///     model. <c>true</c> if there was something to do.
        ///     <para>
        ///         <b>Two past states, one pass.</b> Before 21.08.2026 an account carried a
        ///         single <c>defaultRegion</c> plus eleven flat HotS fields; between then and
        ///         22.08.2026 a <c>regions</c> list plus <c>hotsByRegion</c>. Both carried
        ///         the four game booleans. What is built from that is the cross product:
        ///         every ticked game gets the regions of the account.
        ///     </para>
        ///     <para>
        ///         <b>That cross product is the honest translation and not the true one</b> -
        ///         the old file simply did not know which game was played where. An account
        ///         with Europe and America and both HotS and World of Warcraft therefore ends
        ///         up with an American WoW entry it may never have had. That is the direction
        ///         to err in: too much is visible and can be unticked in the dialog, whereas
        ///         too little would be a row nobody can reach.
        ///     </para>
        ///     <para>
        ///         <b>Why a second read pass</b>: the old keys no longer exist on
        ///         <see cref="BattlenetAccount" />, and <c>IgnoreUnmatchedProperties</c>
        ///         discards them without comment on the first read. Without this pass,
        ///         rank, heroes and currencies of all 28 accounts would have vanished on
        ///         the next save - silently, because an empty list looks exactly like one
        ///         never read.
        ///     </para>
        ///     <para>
        ///         The marker is the <b>empty game map</b>: a migrated or freshly written
        ///         file always has at least one game with at least one region, because the
        ///         dialog enforces it. That's why the migration runs exactly once.
        ///     </para>
        /// </summary>
        private bool MigrateToPerGameRegions(List<BattlenetAccount> accounts, string content)
        {
            if (accounts.All(account => account.RegionsByGame.Count > 0)) return false;

            var legacy = _yamlIn.Deserialize<List<LegacyAccount>>(new StringReader(content)) ?? [];
            var byEmail = legacy
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Email))
                .ToDictionary(entry => entry.Email.ToLowerInvariant(), entry => entry);

            var migrated = 0;
            foreach (var account in accounts)
            {
                if (account.RegionsByGame.Count > 0) continue;

                // NO LEGACY ENTRY. This case shouldn't occur - both lists come from the
                // same file - but an account without a region would have no row, would thus
                // be invisible and could no longer be repaired either: the edit button sits
                // in the row, which then doesn't exist.
                byEmail.TryGetValue(account.Email.ToLowerInvariant(), out var old);

                // The regions of the account: the list, if the file already had one -
                // otherwise the single defaultRegion of the state before that.
                var regions = old?.Regions is { Count: > 0 } listed
                    ? listed
                    : [old?.DefaultRegion ?? BattlenetRegion.Europe];

                foreach (var game in old?.TickedGames() ?? [Games.Hots])
                {
                    account.SetRegions(game, regions);
                }

                // NOT A SINGLE GAME TICKED, which the dialog has ruled out for a while but
                // an older file or a hand edit can still hold. Without a fallback the
                // account would lose its last row here, so it gets the one game this app
                // has any data for.
                if (account.RegionsByGame.Count == 0)
                {
                    account.SetRegions(Games.Hots, regions);
                    Log.Warning("{Email} had no game ticked - migrated as Heroes of the Storm",
                        account.Email);
                }

                migrated++;

                // The state before 21.08.2026 on top: the flat HotS fields into the game
                // state of the region that stood there. An account without a single read
                // value gets NO game state - "played" and "has data" are two different
                // things, and an empty record in the file would claim the latter.
                if (old == null || !old.HasAnyValue()) continue;

                var data = account.HotsFor(old.DefaultRegion);
                data.Tier = old.HotsTier;
                data.Division = old.HotsDivision;
                data.PenaltyGames = old.HotsPenaltyGames;
                data.PlacementsPending = old.HotsPlacementsPending;
                data.Heroes = old.HotsHeroes ?? [];
                data.Gold = old.HotsGold;
                data.Shards = old.HotsShards;
                data.Gems = old.HotsGems;
                data.AccountLevel = old.HotsAccountLevel;
                data.LootChests = old.HotsLootChests;
                data.ReadAt = old.HotsReadAt;
            }

            if (migrated == 0) return false;

            Log.Information("Migrated {Count} account(s) to per-game regions", migrated);
            return true;
        }

        /// <summary>
        ///     Reads the just-written file back in and compares it with what should have
        ///     been written. If it doesn't match, the old state is written back and
        ///     startup aborts.
        ///     <para>
        ///         <b>Why this effort</b>: the dangerous error is not the exception -
        ///         that gets noticed - but the silent round trip. If YamlDotNet wrote the
        ///         game state but didn't recognize it on reading, rank, heroes and
        ///         currencies of all accounts would be gone, and on the next start the
        ///         migration wouldn't run again: the game map is already there by then.
        ///         The damage would be final and would go unnoticed by anyone until
        ///         someone looks at a row.
        ///     </para>
        ///     <para>
        ///         Comparison is deliberately coarse - number of games and their regions,
        ///         plus tier and hero count per region. It's about "did it arrive at all",
        ///         not a field-by-field comparison.
        ///     </para>
        /// </summary>
        private void VerifyMigration(List<BattlenetAccount> expected, string original)
        {
            var written = _yamlIn.Deserialize<List<BattlenetAccount>>(
                new StringReader(File.ReadAllText(_configFile))) ?? [];

            var back = written.ToDictionary(account => account.Email, account => account);
            foreach (var account in expected)
            {
                if (!back.TryGetValue(account.Email, out var other)
                    || other.RegionsByGame.Count != account.RegionsByGame.Count
                    || account.RegionsByGame.Any(entry =>
                        other.RegionsFor(entry.Key).Count != entry.Value.Count)
                    || other.HotsByRegion.Count != account.HotsByRegion.Count
                    || account.HotsByRegion.Any(entry =>
                        other.HotsIn(entry.Key) is not { } mirror
                        || mirror.Tier != entry.Value.Tier
                        || mirror.Heroes.Count != entry.Value.Heroes.Count))
                {
                    File.WriteAllText(_configFile, original);
                    Log.Error("Region migration did not survive a write and read of {Path} - " +
                              "the previous file has been restored", _configFile);
                    throw new InvalidOperationException(
                        "Migrating the accounts to per-game regions failed the read-back check. " +
                        "Your data.yaml has been restored unchanged - nothing was lost.");
                }
            }

            Log.Information("Region migration verified for {Count} account(s)", expected.Count);
        }

        private void SaveToConfigFile()
        {
            WriteAccounts(BattlenetAccounts.AsEnumerable());
        }

        private void WriteAccounts(IEnumerable<BattlenetAccount> accounts)
        {
            ensureConfigFileExists();
            var content = _yamlOut.Serialize(accounts);
            File.WriteAllText(_configFile, content);
        }

        private void ensureConfigFileExists()
        {
            if (!File.Exists(_configFile))
            {
                Directory.CreateDirectory(Directories.UserPath);
                using (File.Create(_configFile))
                {
                }
            }
        }

        public void Reload()
        {
            BattlenetAccounts.Clear();
            foreach (var battlenetAccount in ReadFromConfigFile()) BattlenetAccounts.Add(battlenetAccount);
            RebuildRows();
        }

        public void UpdateInteraction(BattlenetAccount account)
        {
            account.LatestInteractionAt = DateTime.Now;
            AddOrUpdate(account);
        }

        /// <summary>
        ///     The <c>data.yaml</c>, as it looked before 22.08.2026 - only the fields
        ///     that no longer exist today, plus the email as the key. Exclusively for
        ///     <see cref="MigrateToPerGameRegions" />; whoever adds something here adds
        ///     the past.
        ///     <para>
        ///         It spans <b>two</b> past states at once, and which one a file is in shows
        ///         in <see cref="Regions" />: filled means the state from 21.08.2026, empty
        ///         means the one before it, where <see cref="DefaultRegion" /> and the flat
        ///         HotS fields applied.
        ///     </para>
        /// </summary>
        private sealed class LegacyAccount
        {
            public string Email { get; set; } = "";

            /// <summary>The account-wide region list, 21.08.2026 to 22.08.2026.</summary>
            public List<BattlenetRegion>? Regions { get; set; }

            /// <summary>The single region of the state before that.</summary>
            public BattlenetRegion DefaultRegion { get; set; } = BattlenetRegion.Europe;

            /// <summary>
            ///     The four game ticks. They were on the account until 22.08.2026 and said
            ///     nothing about a region - which is exactly why the migration has to spread
            ///     them across the regions of the account.
            /// </summary>
            public bool Overwatch { get; set; }

            public bool Hots { get; set; }
            public bool Wow { get; set; }
            public bool Diablo { get; set; }

            public HotsRankTier HotsTier { get; set; } = HotsRankTier.None;
            public int HotsDivision { get; set; }
            public int HotsPenaltyGames { get; set; }
            public bool HotsPlacementsPending { get; set; }
            public List<string>? HotsHeroes { get; set; }
            public int? HotsGold { get; set; }
            public int? HotsShards { get; set; }
            public int? HotsGems { get; set; }
            public int? HotsAccountLevel { get; set; }
            public int? HotsLootChests { get; set; }
            public DateTime? HotsReadAt { get; set; }

            /// <summary>The ids of the games that were ticked - in the display order.</summary>
            public IEnumerable<string> TickedGames()
            {
                if (Hots) yield return Games.Hots;
                if (Overwatch) yield return Games.Overwatch;
                if (Wow) yield return Games.Wow;
                if (Diablo) yield return Games.Diablo;
            }

            /// <summary>Is there anything in here at all that justifies a game state?</summary>
            public bool HasAnyValue()
            {
                return HotsTier != HotsRankTier.None
                       || HotsDivision != 0
                       || HotsPenaltyGames != 0
                       || HotsPlacementsPending
                       || HotsHeroes is { Count: > 0 }
                       || HotsGold != null
                       || HotsShards != null
                       || HotsGems != null
                       || HotsAccountLevel != null
                       || HotsLootChests != null
                       || HotsReadAt != null;
            }
        }
    }
}
