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
        // newer app version throws a YamlException when read by an older version - the
        // case that arises whenever an older release is run against data a newer one has
        // already written.
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

            // An account without a single game in a single region would have no row at
            // all - and could then not be repaired either, because the edit button sits
            // in that row. A hand-edited file is how it happens. Repair is quiet but not
            // silent; nothing is written here, the next change takes care of that.
            foreach (var account in accounts.Where(account => account.RegionsByGame.Count == 0))
            {
                account.SetRegions(Games.Hots, [BattlenetRegion.Europe]);
                Log.Warning("{Email} played no game in any region and would not have been " +
                            "visible - set to Heroes of the Storm in Europe", account.Email);
            }

            return accounts;
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
    }
}
