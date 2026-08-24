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
    /// <summary>
    ///     The shape of <c>data.yaml</c>: a version in front, the accounts behind it.
    ///     <para>
    ///         <b>Until 1.3.0 the file was a bare sequence</b> - it began with the first
    ///         account and carried nothing else. That worked as long as every change to the
    ///         format could be guessed from the content, and the guessing is what
    ///         <see cref="SchemaVersion" /> ends: a migration now hangs on a number rather
    ///         than on somebody noticing that a field looks different.
    ///     </para>
    /// </summary>
    public sealed class AccountFile
    {
        private List<BattlenetAccount> _accounts = [];

        /// <summary>
        ///     The layout of this file, not the version of the application. Zero means the
        ///     bare sequence of the older layout; nothing writes that any more.
        /// </summary>
        public int SchemaVersion { get; set; } = BattlenetAccountGateway.CurrentSchema;

        /// <summary>
        ///     The accounts. The setter catches null, because <c>accounts:</c> without a
        ///     value deserialises to null and not to an empty list - the same trap as in
        ///     <see cref="Entity.HotsRotation.Heroes" />.
        /// </summary>
        public List<BattlenetAccount> Accounts
        {
            get => _accounts;
            set => _accounts = value ?? [];
        }
    }

    public class BattlenetAccountGateway
    {
        /// <summary>The shape this build writes. See <see cref="AccountFile.SchemaVersion" />.</summary>
        internal const int CurrentSchema = 1;

        /// <summary>
        ///     Serialises read-modify-write against <c>data.yaml</c> for the whole process.
        ///     Same reasoning and same limits as <see cref="AppFile" />: it makes the re-read
        ///     below a guarantee inside this process, and it does nothing about a second
        ///     Smurftown running beside this one.
        /// </summary>
        private static readonly object FileLock = new();

        /// <summary>
        ///     The one gateway of the application.
        ///     <para>
        ///         <b>Lazy, and that is not a performance decision.</b> A plain
        ///         <c>static readonly … = new(…)</c> is built by the type initializer, which
        ///         runs the moment anything touches this type at all - including
        ///         <c>new BattlenetAccountGateway(folder)</c> in a test. The singleton was
        ///         therefore created on whichever thread happened to reach the type first, and
        ///         its <see cref="AccountRegionsFiltered" /> is an <c>ICollectionView</c>, which
        ///         is bound to the thread that created it. A test that built one for its own
        ///         folder handed the XAML test a singleton belonging to a worker thread, and
        ///         the first <see cref="Reload" /> from the STA thread threw
        ///         <c>NotSupportedException</c>. Which of the two ran first decided whether the
        ///         suite was green.
        ///     </para>
        ///     <para>
        ///         With <see cref="Lazy{T}" /> the singleton is built on first <b>use</b>. In
        ///         the application that is <c>App.OnStartup</c>, on the UI thread, deliberately
        ///         and by name.
        ///     </para>
        /// </summary>
        public static BattlenetAccountGateway Instance => Singleton.Value;

        private static readonly Lazy<BattlenetAccountGateway> Singleton =
            new(() => new BattlenetAccountGateway(Directories.UserPath));

        private readonly string _configFile;
        private readonly string _folder;

        /// <summary>
        ///     The file exactly as it was last read or written, so a change made by somebody
        ///     else can be recognised before the next save runs over it. Null until the first
        ///     read.
        /// </summary>
        private string? _lastKnownContent;

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

        /// <summary>
        ///     Reads <c>data.yaml</c> out of <paramref name="folder" />.
        ///     <para>
        ///         <b>The folder is handed in, not fetched.</b> <c>Directories.UserPath</c>
        ///         resolves once per process and keeps the answer - deliberately, so that a
        ///         variable changed while the app runs cannot split the data across two
        ///         folders. The same property makes it unusable from a test, which needs a
        ///         fresh folder per case. So the value flows inwards from
        ///         <c>App.OnStartup</c>, the way it already does for the automation layer.
        ///     </para>
        ///     <para>
        ///         <see cref="Instance" /> stays the one call site of the application.
        ///         Whoever constructs one of these directly is a test.
        ///     </para>
        /// </summary>
        public BattlenetAccountGateway(string folder)
        {
            _folder = folder;
            _configFile = Path.Combine(folder, "data.yaml");
            foreach (var account in ReadFromConfigFile())
            {
                BattlenetAccounts.Add(account);
            }

            RebuildRows();

            AccountRegionsFiltered = CollectionViewSource.GetDefaultView(AccountRegions);
            // Same default as always: last interaction first, see SortBy for the tie-breakers
            // this always appends.
            SortBy(AccountSortField.LastRead, ListSortDirection.Descending);
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
        ///     Re-sorts the row list by <paramref name="field" />, in <paramref name="direction" />.
        ///     <para>
        ///         <b>Two tie-breakers are always appended</b>, exactly as they stood
        ///         hard-coded before this method existed: <c>DisplayName</c> ascending (skipped
        ///         when that IS the primary field, or it would sort itself against itself),
        ///         then <c>RegionOrder</c> ascending, so the two rows of one account always
        ///         stand in the same order relative to each other regardless of what the list
        ///         is primarily sorted by.
        ///     </para>
        /// </summary>
        public void SortBy(AccountSortField field, ListSortDirection direction)
        {
            AccountRegionsFiltered.SortDescriptions.Clear();
            AccountRegionsFiltered.SortDescriptions.Add(
                new SortDescription(SortPropertyFor(field), direction));
            if (field != AccountSortField.Name)
            {
                AccountRegionsFiltered.SortDescriptions.Add(new SortDescription(
                    nameof(AccountRegion.DisplayName), ListSortDirection.Ascending));
            }

            AccountRegionsFiltered.SortDescriptions.Add(new SortDescription(
                nameof(AccountRegion.RegionOrder), ListSortDirection.Ascending));
        }

        private static string SortPropertyFor(AccountSortField field)
        {
            return field switch
            {
                AccountSortField.Name => nameof(AccountRegion.DisplayName),
                AccountSortField.Rank => nameof(AccountRegion.RankSortKey),
                AccountSortField.Gold => nameof(AccountRegion.GoldSortKey),
                AccountSortField.HeroesRead => nameof(AccountRegion.HeroesReadSortKey),
                _ => nameof(AccountRegion.LatestInteractionAt)
            };
        }

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
            IReadOnlyCollection<HotsRankTier> rankTiers,
            bool showArchived)
        {
            return row =>
                row.Account.Inactive == showArchived &&
                row.Region == region &&
                (string.IsNullOrEmpty(searchQuery) || Contains(row.Account, searchQuery)) &&
                (game == null || row.Account.PlaysIn(game, region)) &&
                (heroIds.Count == 0 || CanPlayAnyHero(row, heroIds, freeHeroIds)) &&
                (rankTiers.Count == 0 || rankTiers.Contains(row.EffectiveRankTier));
        }

        /// <summary>
        ///     How many rows are "in scope" - game, region and the archive half alone, none of
        ///     search, hero or rank narrowing it further. The denominator for the "N of M" count
        ///     in the filter bar; the numerator is <see cref="AccountRegionsFiltered" />'s own
        ///     count once every condition has run.
        ///     <para>
        ///         A smaller, separate predicate rather than a parameterised slice of
        ///         <see cref="CreatePredicate" />: it answers a genuinely different question
        ///         ("how many could match" vs. "how many do"), and threading which clauses to
        ///         skip through one method would obscure both.
        ///     </para>
        /// </summary>
        public int ScopeCount(string? game, BattlenetRegion region, bool showArchived)
        {
            return AccountRegions.Count(row =>
                row.Account.Inactive == showArchived &&
                row.Region == region &&
                (game == null || row.Account.PlaysIn(game, region)));
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
            IReadOnlyCollection<HotsRankTier> rankTiers,
            bool showArchived)
        {
            var filter = CreatePredicate(searchQuery, game, region, heroIds, freeHeroIds, rankTiers,
                showArchived);
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
            var accounts = ReadFile().Accounts;

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

        /// <summary>
        ///     The file as it stands on disk, in either layout.
        ///     <para>
        ///         <b>Deliberately without a catch.</b> Everything else in this application
        ///         falls back to a default when a file cannot be read; this one does not. An
        ///         empty account list looks exactly like a fresh installation, and the next
        ///         save would write that emptiness over the real file. A start that stops
        ///         with an exception is the cheaper outcome.
        ///     </para>
        /// </summary>
        private AccountFile ReadFile()
        {
            ensureConfigFileExists();
            var content = File.ReadAllText(_configFile);
            _lastKnownContent = content;

            if (content.Trim().Length == 0) return new AccountFile();

            if (IsOlderLayout(content))
            {
                // Deliberately silent. Reading happens on every start and again on every
                // Reload, and the layout of a file is a state and not an event - said three
                // times per start it is noise. The one line worth having is written when the
                // upgrade actually happens, in SaveToConfigFile.
                var accounts = _yamlIn.Deserialize<List<BattlenetAccount>>(new StringReader(content)) ?? [];
                return new AccountFile { SchemaVersion = 0, Accounts = accounts };
            }

            return _yamlIn.Deserialize<AccountFile>(new StringReader(content)) ?? new AccountFile();
        }

        /// <summary>
        ///     Is this the bare sequence of the older layout? Its first line that is neither
        ///     blank nor a comment begins an item; the current layout begins with a key.
        ///     <para>
        ///         Sniffed rather than found out by letting the deserialiser throw. An
        ///         exception used as a fork would also swallow the one case that has to reach
        ///         the caller - a file that is broken in both layouts.
        ///     </para>
        /// </summary>
        private static bool IsOlderLayout(string content)
        {
            var first = content
                .Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#'));

            return first != null && first.StartsWith('-');
        }

        /// <summary>
        ///     Writes the whole list.
        ///     <para>
        ///         <b>The file is read again first, completely.</b> Two things come out of
        ///         that. A file already written in a newer schema is not overwritten but
        ///         reported - deserialising drops every key this build does not know, so
        ///         writing it back would delete whatever a later version put in it. And a
        ///         file that somebody else changed since this window read it is named in the
        ///         log before it is overwritten: this application holds no lock and rewrites
        ///         the list whole, so the change is lost either way - and the one thing worse
        ///         than losing it is losing it silently.
        ///     </para>
        /// </summary>
        private void SaveToConfigFile()
        {
            // Read, check and write as ONE step - see AppFile.FileLock for the reasoning,
            // which holds word for word here. Static for the same reason: what is protected
            // is data.yaml, not this object.
            lock (FileLock) SaveUnderLock();
        }

        private void SaveUnderLock()
        {
            var known = _lastKnownContent;
            var onDisk = ReadFile();

            if (onDisk.SchemaVersion > CurrentSchema)
            {
                throw new InvalidOperationException(
                    $"data.yaml is written in schema {onDisk.SchemaVersion}, this build knows {CurrentSchema}. " +
                    "Refusing to write it back, because that would drop everything the newer version put in it. " +
                    "Run the newer Smurftown, or move the file aside.");
            }

            if (known != null && known != _lastKnownContent)
            {
                Log.Warning("data.yaml changed outside this window since it was last read - " +
                            "this save overwrites that change. Another Smurftown, or an editor.");
            }

            var content = _yamlOut.Serialize(new AccountFile
            {
                SchemaVersion = CurrentSchema,
                Accounts = BattlenetAccounts.ToList()
            });

            File.WriteAllText(_configFile, content);
            _lastKnownContent = content;

            // Here and not on reading: this is the moment the layout actually changes, and it
            // happens exactly once per data folder.
            if (onDisk.SchemaVersion < CurrentSchema && onDisk.Accounts.Count > 0)
            {
                Log.Information("data.yaml was written in the layout before 1.3.0 and is now on " +
                                "schema {Schema}", CurrentSchema);
            }
        }

        private void ensureConfigFileExists()
        {
            if (!File.Exists(_configFile))
            {
                Directory.CreateDirectory(_folder);
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
