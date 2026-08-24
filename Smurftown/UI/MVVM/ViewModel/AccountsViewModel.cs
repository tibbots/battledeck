using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.UI.MVVM.View;
using Smurftown.Backend.Texts;

namespace Smurftown.UI.MVVM.ViewModel
{
    internal class AccountsViewModel : ObservableObject
    {
        /// <summary>Portraits in the filter button. Beyond that, only the label counts.</summary>
        private const int HeroFilterChipCount = 4;

        private static readonly BattlenetAccountGateway _battlenetAccountGateway = BattlenetAccountGateway.Instance;
        private static readonly HotsRotationGateway _rotationGateway = HotsRotationGateway.Instance;
        private ICollectionView _accountRows;
        private BattlenetRegion _regionFilter = BattlenetRegion.Europe;
        private RelayCommand? _clearHeroFilterCommand;
        private RelayCommand? _createAccountCommand;
        private RelayCommand? _editRotationCommand;
        private string? _gameFilter;
        private List<string> _heroFilter = [];
        private RelayCommand? _openHeroFilterCommand;
        private string _searchQuery = "";
        private bool _showArchived;
        private List<HotsRankTier> _rankFilter = [];
        private RelayCommand<HotsRankTier>? _toggleRankCommand;
        private AccountSortField _sortField = AccountSortField.LastRead;
        private ListSortDirection _sortDirection = ListSortDirection.Descending;
        private RelayCommand? _toggleSortDirectionCommand;

        private static readonly AccountSortField[] HotsOnlySortFields =
        [
            AccountSortField.Rank, AccountSortField.Gold, AccountSortField.HeroesRead
        ];

        public AccountsViewModel()
        {
            _battlenetAccountGateway.Reload();
            _accountRows = _battlenetAccountGateway.AccountRegionsFiltered;

            // The count in the filter bar answers "how many rows does the CURRENT filter
            // leave", so it has to follow every Refresh() of the view - not just the
            // properties this ViewModel itself changes. A Filter or a SortDescription change
            // both raise this, and so does an account being added, edited or archived.
            if (_accountRows is INotifyCollectionChanged notifying)
            {
                notifying.CollectionChanged += (_, _) =>
                {
                    OnPropertyChanged(nameof(FilteredCount));
                    OnPropertyChanged(nameof(ScopeCount));
                    OnPropertyChanged(nameof(FilterCountLabel));
                    OnPropertyChanged(nameof(AccountRowsVisibility));
                    OnPropertyChanged(nameof(NoAccountsVisibility));
                    OnPropertyChanged(nameof(NoMatchesVisibility));
                    // Both now read ScopeCount, not just _showArchived - a change to either
                    // needs to reach them, and ScopeCount changes on every re-filter, not only
                    // when the archive toggle itself is flipped.
                    OnPropertyChanged(nameof(NoMatchesTitle));
                    OnPropertyChanged(nameof(NoMatchesHint));
                };
            }

            // The same applies to the region: always exactly one, at start Europe - the
            // normal case, and until 21.08.2026 the only one that existed.
            //
            // A game is ALWAYS selected, and at start it is Heroes of the Storm.
            // Since the rows no longer have their own tabs, the filter is the only
            // way to switch the panel - without a set filter every row showed its
            // own game, and the columns would line up but compare
            // apples to pears. HotS, because only there is there any data at all.
            GameFilter = GameVisuals.Hots;
        }

        /// <summary>
        ///     Shows the archive instead of the active accounts.
        ///     <para>
        ///         Deliberately a toggle between two halves and no third "show all":
        ///         "archive" means looking inside, and whoever is inside sees the way back
        ///         immediately from the set switch. A view that mixes active and archived would be
        ///         exactly the one in which an archived account is taken for active.
        ///     </para>
        ///     <para>
        ///         All other filters still apply - so you can also search for a
        ///         battletag or narrow it down to one game while in the archive.
        ///     </para>
        /// </summary>
        public bool ShowArchived
        {
            get => _showArchived;
            set
            {
                if (!SetProperty(ref _showArchived, value)) return;
                OnPropertyChanged(nameof(ArchiveHint));
                // The wording of the "nothing to show" panel depends on which half of the
                // list is open - see NoMatchesTitle/NoMatchesHint. The visibility itself
                // follows the re-filter this same change already triggers (through the base
                // OnPropertyChanged override), which is why only the TEXT is notified here.
                OnPropertyChanged(nameof(NoMatchesTitle));
                OnPropertyChanged(nameof(NoMatchesHint));
            }
        }

        public string ArchiveHint => Strings.Current[_showArchived
            ? "accounts.archiveShowing"
            : "accounts.archiveShow"];

        public ICommand CreateAccountCommand
        {
            get
            {
                if (_createAccountCommand == null)
                {
                    _createAccountCommand = new RelayCommand(
                        this.CreateAccount,
                        this.CanCreateAccount
                    );
                }

                return _createAccountCommand;
            }
        }

        /// <summary>Opens the same selection surface as the account dialog, just in filter mode.</summary>
        public ICommand OpenHeroFilterCommand
        {
            get
            {
                if (_openHeroFilterCommand == null)
                {
                    _openHeroFilterCommand = new RelayCommand(OpenHeroFilter);
                }

                return _openHeroFilterCommand;
            }
        }

        public ICommand ClearHeroFilterCommand
        {
            get
            {
                if (_clearHeroFilterCommand == null)
                {
                    _clearHeroFilterCommand = new RelayCommand(() => HeroFilter = []);
                }

                return _clearHeroFilterCommand;
            }
        }

        /// <summary>
        ///     Click on the rotation symbol: enter the free heroes of the current period.
        ///     <para>
        ///         Until 21.08.2026 there was a toggle here that set the hero filter to the free
        ///         heroes, and the input hung off the right mouse button. The filter has
        ///         fallen with no replacement, because its question is not one: <c>CanPlayAnyHero</c> lets
        ///         through whoever owns the hero <b>or</b> can play it for free - and everyone
        ///         can play it for free. Choosing fourteen free heroes thus hit every
        ///         HotS account, i.e. all of them, and a filter that removes nothing is not one.
        ///     </para>
        /// </summary>
        public ICommand EditRotationCommand
        {
            get
            {
                if (_editRotationCommand == null)
                {
                    _editRotationCommand = new RelayCommand(EditRotation);
                }

                return _editRotationCommand;
            }
        }

        /// <summary>
        ///     The one selected game, or <c>null</c>.
        ///     <para>
        ///         <b>Exclusive</b>, where four independent checkmarks used to stand. The filter is
        ///         thus no longer just a selection, but also a view choice: it sets
        ///         <see cref="GameFocus.Current" />, and every row shows the panel of this game,
        ///         provided the account has it. The price is the combination - "Overwatch AND HotS"
        ///         can no longer be asked. Two selected games would have
        ///         no answer for the row.
        ///     </para>
        ///     <para>
        ///         The value is set via the four bool properties below, because the
        ///         filter bar has four <c>ToggleButton</c>. A second click on the same
        ///         symbol takes the selection back - that happens on its own with the
        ///         <c>ToggleButton</c> and is the reason why there are no <c>RadioButton</c> here.
        ///     </para>
        /// </summary>
        public string? GameFilter
        {
            get => _gameFilter;
            set
            {
                if (_gameFilter == value) return;
                _gameFilter = value;

                // BEFORE the notifications: OnPropertyChanged re-filters, the re-filtering builds the
                // rows anew, and those read the value in the constructor.
                GameFocus.Current = value;

                // The three other symbols must be deselected along with it. Each of these notifications
                // re-filters once more via the overrider below - the same result,
                // just computed multiple times. That is the price of the pattern and with 27 accounts
                // not measurable; HeroFilter has always done it the same way.
                NotifySymbols();
                OnPropertyChanged(nameof(HotsFiltersVisibility));
                // The sort field list narrows to Name/LastRead outside HotS, and the
                // currently chosen field has to be re-read along with it: SortField's own
                // getter falls back to LastRead once its real value drops out of the
                // narrowed list, the same snap-back NotifySymbols already does for the game
                // buttons themselves.
                OnPropertyChanged(nameof(SortFieldOptions));
                OnPropertyChanged(nameof(SortField));
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     The additional filters belong to a game and only stand there when it is filtered
        ///     on. Hero filter and free rotation are Heroes of the Storm; for Overwatch,
        ///     WoW and Diablo there are none yet.
        ///     <para>
        ///         <b>Hidden means without effect, not deleted.</b> A filter that you
        ///         don't see, but that still takes rows away, is the worse half
        ///         of both: the list would be shorter than it should be, and nothing on the
        ///         screen would say why. The selection itself nevertheless remains standing and returns
        ///         when switching back - whoever briefly looks at Overwatch does not want to re-select
        ///         their three heroes afterwards. The omission therefore happens at a
        ///         single place, in the override of <c>OnPropertyChanged</c>.
        ///     </para>
        ///     <para>
        ///         The archive toggle and the search field are deliberately NOT included: they
        ///         belong to no game. The search field matches name and email, the archive
        ///         switches between two halves of the same list.
        ///     </para>
        /// </summary>
        public Visibility HotsFiltersVisibility =>
            _gameFilter == GameVisuals.Hots ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        ///     The selected region. <b>Exclusive and always set</b>, exactly like the
        ///     game filter next to it - one region or nothing else, a second click on
        ///     the same abbreviation has no effect.
        ///     <para>
        ///         Unlike with the game, this is <b>no technical necessity</b>: a
        ///         row shows exactly one region anyway, several selected ones would have
        ///         simply resulted in more rows. It is a usability decision, and its reason
        ///         stands two toggles further to the left: two adjacent filter blocks with
        ///         different logic - one exclusive, the other not - are the
        ///         more expensive surprise than the rows that you thereby do not see side by side.
        ///     </para>
        ///     <para>
        ///         <b>The price is the same as with the game filter</b>: whoever only
        ///         plays in America is invisible under EU. They remain reachable via their own
        ///         abbreviation - the same solution as there, and the same sentence in the CLAUDE.md.
        ///     </para>
        /// </summary>
        public bool EuropeFiltered
        {
            get => _regionFilter == BattlenetRegion.Europe;
            set => ChooseRegion(BattlenetRegion.Europe, value);
        }

        public bool AmericasFiltered
        {
            get => _regionFilter == BattlenetRegion.Americas;
            set => ChooseRegion(BattlenetRegion.Americas, value);
        }

        public bool AsiaFiltered
        {
            get => _regionFilter == BattlenetRegion.Asia;
            set => ChooseRegion(BattlenetRegion.Asia, value);
        }

        /// <summary>States the status in plain text, along with what is thereby not visible.</summary>
        public string RegionFilterHint =>
            Strings.Format("accounts.regionFilterHint", _regionFilter.DisplayName());

        /// <summary>
        ///     Selects a region.
        ///     <para>
        ///         <b>There is no deselecting</b>, word for word as with <see cref="Choose" />: a
        ///         <c>ToggleButton</c> un-checks itself on click, before the binding
        ///         writes the value here - <see cref="NotifyRegions" /> lets it re-read
        ///         the source and thereby snaps it back.
        ///     </para>
        /// </summary>
        private void ChooseRegion(BattlenetRegion region, bool selected)
        {
            if (!selected)
            {
                NotifyRegions();
                return;
            }

            if (_regionFilter == region) return;
            _regionFilter = region;

            OnPropertyChanged(nameof(RegionFilterHint));
            // also triggers the re-filtering via the overrider below
            NotifyRegions();
        }

        /// <summary>Lets the three abbreviations of the filter bar re-read their state.</summary>
        private void NotifyRegions()
        {
            OnPropertyChanged(nameof(EuropeFiltered));
            OnPropertyChanged(nameof(AmericasFiltered));
            OnPropertyChanged(nameof(AsiaFiltered));
        }

        public bool OverwatchFiltered
        {
            get => _gameFilter == GameVisuals.Overwatch;
            set => Choose(GameVisuals.Overwatch, value);
        }

        public bool HotsFiltered
        {
            get => _gameFilter == GameVisuals.Hots;
            set => Choose(GameVisuals.Hots, value);
        }

        public bool DiabloFiltered
        {
            get => _gameFilter == GameVisuals.Diablo;
            set => Choose(GameVisuals.Diablo, value);
        }

        public bool WowFiltered
        {
            get => _gameFilter == GameVisuals.Wow;
            set => Choose(GameVisuals.Wow, value);
        }

        /// <summary>
        ///     Select a symbol of the filter bar.
        ///     <para>
        ///         <b>There is no deselecting.</b> A game is always set - since the rows
        ///         no longer have their own tabs, the filter is the only way to switch the panel,
        ///         and "no game selected" would mean "every row shows a different one".
        ///         A <c>ToggleButton</c> un-checks itself on click, though, before the
        ///         binding writes the value here; <see cref="NotifySymbols" /> lets it
        ///         re-read the source and thereby snaps it back.
        ///     </para>
        ///     <para>
        ///         The same approach catches a second case: if you select Overwatch while HotS
        ///         is set, <c>GameFilter</c> also reports <c>HotsFiltered</c> as changed, and the
        ///         HotS button then reads <c>false</c>. If it wrote that back, the
        ///         selection just made would be gone again immediately. WPF does not write back
        ///         during a two-way binding while a target update is in progress - but relying
        ///         on that would be a bet on a subtlety of the
        ///         binding engine, and here the safeguard costs nothing extra.
        ///     </para>
        /// </summary>
        private void Choose(string game, bool selected)
        {
            if (selected) GameFilter = game;
            else NotifySymbols();
        }

        /// <summary>Lets the four symbols of the filter bar re-read their state.</summary>
        private void NotifySymbols()
        {
            OnPropertyChanged(nameof(OverwatchFiltered));
            OnPropertyChanged(nameof(HotsFiltered));
            OnPropertyChanged(nameof(WowFiltered));
            OnPropertyChanged(nameof(DiabloFiltered));
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        /// <summary>
        ///     Identifiers of the sought heroes. Every account that owns at least one
        ///     of them is shown - the selection is an OR, not an AND.
        /// </summary>
        public IReadOnlyList<string> HeroFilter
        {
            get => _heroFilter;
            private set
            {
                _heroFilter = value.ToList();
                // also triggers the re-filtering via OnPropertyChanged
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeroFilterChips));
                OnPropertyChanged(nameof(HeroFilterLabel));
                OnPropertyChanged(nameof(HeroFilterActive));
                OnPropertyChanged(nameof(HeroFilterChipsVisibility));
                OnPropertyChanged(nameof(HeroFilterEmptyVisibility));
            }
        }

        /// <summary>The first portraits as an overlapping stack in the filter button.</summary>
        public IReadOnlyList<HeroChip> HeroFilterChips =>
            HotsHeroCatalog.Resolve(_heroFilter).Take(HeroFilterChipCount).Select(HeroChip.For).ToList();

        /// <summary>
        ///     With exactly one hero, their name stands there - for a single circle it is the
        ///     faster information than the image. From two on, only the label counts.
        /// </summary>
        public string HeroFilterLabel
        {
            get
            {
                var heroes = HotsHeroCatalog.Resolve(_heroFilter);
                if (heroes.Count == 0) return Strings.Current["accounts.heroFilterEmpty"];
                if (heroes.Count == 1) return heroes[0].Name.ToUpperInvariant();

                var hidden = heroes.Count - HeroFilterChipCount;
                return hidden > 0
                    ? Strings.Format("accounts.heroFilterOverflow", hidden, heroes.Count)
                    : Strings.Format("accounts.heroFilterAny", heroes.Count);
            }
        }

        public bool HeroFilterActive => _heroFilter.Count > 0;

        public Visibility HeroFilterChipsVisibility =>
            _heroFilter.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility HeroFilterEmptyVisibility =>
            _heroFilter.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        ///     "FREE 14", or "FREE ?" if the calendar does not know the current period. The
        ///     question mark is meant literally: which heroes are then free, the app
        ///     simply does not know, and showing a date that no longer applies would be the
        ///     worse lie.
        ///     <para>
        ///         Since the calendar it is the exception and no longer the weekly rhythm:
        ///         before this, a question mark stood here after every period change, until someone
        ///         entered the list by hand.
        ///     </para>
        /// </summary>
        public string RotationLabel => _rotationGateway.Source == HotsRotationSource.None
            ? Strings.Current["accounts.rotationUnknown"]
            : Strings.Format("accounts.rotationCount", _rotationGateway.Free.Count);

        /// <summary>Dimmed as long as the period is unresolved.</summary>
        public double RotationOpacity =>
            _rotationGateway.Source == HotsRotationSource.None ? 0.4 : 1.0;

        /// <summary>States the status and origin of the list. A gesture, so one sentence about it.</summary>
        public string RotationHint
        {
            get
            {
                var current = HotsRotationPeriod.Label(_rotationGateway.CurrentPeriod);
                if (_rotationGateway.Source == HotsRotationSource.None)
                {
                    return Strings.Format("accounts.rotationHintUnknown", current);
                }

                // "set by hand" only stands there when it applies: the calendar case is the
                // normal case and needs no origin note, the hand-set status does - otherwise
                // a deviating list does not show that you set it yourself.
                var origin = _rotationGateway.Source == HotsRotationSource.Manual
                    ? Strings.Current["accounts.rotationByHand"]
                    : "";
                return Strings.Format("accounts.rotationHint", current,
                    _rotationGateway.Free.Count, origin);
            }
        }

        /// <summary>
        ///     Tiers the rank filter is narrowed to. Every account whose <c>EffectiveRankTier</c>
        ///     is one of these is shown - an OR, exactly like <see cref="HeroFilter" />.
        ///     "Unranked" is <see cref="HotsRankTier.None" /> like any other tier here; it covers
        ///     both "never read" and "read, but no rank set" - see
        ///     <see cref="AccountRegion.EffectiveRankTier" /> for why those two collapse.
        /// </summary>
        public IReadOnlyList<HotsRankTier> RankFilter
        {
            get => _rankFilter;
            private set
            {
                _rankFilter = value.ToList();
                // also triggers the re-filtering via OnPropertyChanged
                OnPropertyChanged();
                OnPropertyChanged(nameof(RankFilterOptions));
            }
        }

        /// <summary>The eight tiers, in ascending order, each with its own selection state and medal.</summary>
        public IReadOnlyList<RankFilterOption> RankFilterOptions =>
            Enum.GetValues<HotsRankTier>()
                .Select(tier => new RankFilterOption(tier, HotsRankImages.Display(tier), _rankFilter.Contains(tier)))
                .ToList();

        /// <summary>One chip toggled. Mirrors the hero filter's toggle, just per tier instead of per hero.</summary>
        public ICommand ToggleRankCommand
        {
            get { return _toggleRankCommand ??= new RelayCommand<HotsRankTier>(ToggleRank); }
        }

        private void ToggleRank(HotsRankTier tier)
        {
            RankFilter = _rankFilter.Contains(tier)
                ? _rankFilter.Where(t => t != tier).ToList()
                : [.. _rankFilter, tier];
        }

        /// <summary>
        ///     The field the list is sorted by. <b>Always writable</b>, even for a field
        ///     <see cref="SortFieldOptions" /> is currently hiding - the value survives a trip
        ///     through Overwatch and back exactly like <see cref="HeroFilter" /> does, it is only
        ///     the GETTER that falls back to <see cref="AccountSortField.LastRead" /> while the
        ///     real choice isn't offered, so the ComboBox never shows a selection that isn't in
        ///     its own list.
        /// </summary>
        public AccountSortField SortField
        {
            get => SortFieldOptions.Any(o => o.Field == _sortField) ? _sortField : AccountSortField.LastRead;
            set => SetProperty(ref _sortField, value);
        }

        public ListSortDirection SortDirection
        {
            get => _sortDirection;
            set
            {
                if (!SetProperty(ref _sortDirection, value)) return;
                OnPropertyChanged(nameof(SortDescending));
            }
        }

        /// <summary>Plain bool for the direction arrow's rotation trigger - a DataTrigger
        /// comparing against a ListSortDirection value cannot be relied on to convert the
        /// XAML string to the enum, so the ViewModel does that comparison instead.</summary>
        public bool SortDescending => _sortDirection == ListSortDirection.Descending;

        public ICommand ToggleSortDirectionCommand
        {
            get
            {
                return _toggleSortDirectionCommand ??= new RelayCommand(() =>
                    SortDirection = _sortDirection == ListSortDirection.Ascending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending);
            }
        }

        /// <summary>
        ///     Rank, Gold and Heroes read only exist for Heroes of the Storm - hidden for every
        ///     other game rather than offered without effect, the same principle as the hero and
        ///     rank filters themselves.
        /// </summary>
        public IReadOnlyList<SortFieldOption> SortFieldOptions
        {
            get
            {
                var fields = _gameFilter == GameVisuals.Hots
                    ? Enum.GetValues<AccountSortField>()
                    : Enum.GetValues<AccountSortField>().Where(f => !HotsOnlySortFields.Contains(f)).ToArray();
                return fields.Select(f => new SortFieldOption(f, Strings.Current[LabelKeyFor(f)])).ToList();
            }
        }

        private static string LabelKeyFor(AccountSortField field) => field switch
        {
            AccountSortField.Name => "accounts.sortByName",
            AccountSortField.Rank => "accounts.sortByRank",
            AccountSortField.Gold => "accounts.sortByGold",
            AccountSortField.HeroesRead => "accounts.sortByHeroesRead",
            _ => "accounts.sortByLastRead"
        };

        /// <summary>How many rows the current filters leave, out of how many are in scope for
        /// the chosen game and region - see <see cref="BattlenetAccountGateway.ScopeCount" />.</summary>
        public int FilteredCount => _battlenetAccountGateway.AccountRegionsFiltered.Cast<object>().Count();

        public int ScopeCount => _battlenetAccountGateway.ScopeCount(_gameFilter, _regionFilter, _showArchived);

        public string FilterCountLabel => Strings.Format("accounts.filterCount", FilteredCount, ScopeCount);

        /// <summary>
        ///     The rows of the overview - one per account per selected region. Until
        ///     21.08.2026 these were the accounts themselves.
        /// </summary>
        public ICollectionView AccountRows
        {
            get { return _accountRows; }
            set => SetProperty(ref _accountRows, value);
        }

        /// <summary>
        ///     Nothing is active for the current game and region - either because every account
        ///     is archived, or because none was ever ticked for this exact game/region pair. The
        ///     archive toggle itself is excluded on purpose: standing IN the archive with zero
        ///     entries is answered by <see cref="NoMatchesVisibility" /> instead, one panel down -
        ///     "the archive is empty" is not the same sentence as "add your first account".
        /// </summary>
        private bool NothingActiveHere => !_showArchived && ScopeCount == 0;

        /// <summary>
        ///     "Nothing in <c>data.yaml</c> at all" and "something is in there, the current view
        ///     just doesn't show it" are two different facts and get two different panels below -
        ///     the same distinction this application draws everywhere between "never read" and
        ///     "read, found nothing". Conflating them here would tell somebody who has archived
        ///     every account, or searched for a typo, that they have never added one at all.
        /// </summary>
        public Visibility AccountRowsVisibility => FilteredCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        ///     The onboarding panel: either not a single account exists yet, or every account
        ///     that would otherwise show here is archived - both leave the active list equally
        ///     empty, and both have the same two answers, so both get the same panel. This is the
        ///     one place that explains the two ways an account gets into this list at all, since
        ///     24.08.2026 one of them - starting Heroes of the Storm yourself and reading it via
        ///     the header chip - never touches this dialog or a password.
        ///     <para>
        ///         <b>Archiving somebody's only account is what exposed the gap</b> this second
        ///         condition closes: until 24.08.2026 that state showed "no active accounts" here
        ///         instead - true, but the wrong panel, since the two ways to fill this list are
        ///         exactly as relevant with one archived account as with none.
        ///     </para>
        /// </summary>
        public Visibility NoAccountsVisibility =>
            _battlenetAccountGateway.BattlenetAccounts.Count == 0 || NothingActiveHere
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>
        ///     Accounts exist, the current view just doesn't show any of them - a search, hero or
        ///     rank filter hiding rows that ARE there, or the archive toggle itself standing on an
        ///     empty half. Deliberately NOT the case <see cref="NothingActiveHere" /> already
        ///     covers: "add your first account" is the more useful answer there than a sentence
        ///     about filters that, in that state, are not even the reason.
        /// </summary>
        public Visibility NoMatchesVisibility =>
            _battlenetAccountGateway.BattlenetAccounts.Count > 0 && !NothingActiveHere && FilteredCount == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>
        ///     Only two cases reach this panel now that <see cref="NothingActiveHere" /> has its
        ///     own: the archive itself stands empty, or a search/hero/rank filter hides rows that
        ///     exist. "No active accounts" is gone from here - it used to stand in for both, and
        ///     for the first of them it named a symptom rather than the thing to do about it.
        /// </summary>
        public string NoMatchesTitle => (_showArchived && ScopeCount == 0)
            ? Strings.Current["accounts.emptyArchiveTitle"]
            : Strings.Current["accounts.noMatchesTitle"];

        public string NoMatchesHint => (_showArchived && ScopeCount == 0)
            ? Strings.Current["accounts.emptyArchiveHint"]
            : Strings.Current["accounts.noMatchesHint"];

        private bool CanCreateAccount()
        {
            return true;
        }

        private void CreateAccount()
        {
            ShowDialog(viewModel => Dialogs.DialogService.ShowDialog(this, viewModel));
        }

        private void OpenHeroFilter()
        {
            var picker = new HeroPickerViewModel(_heroFilter, HeroPickerMode.Filter);

            using (Dialogs.Backdrop())
            {
                Dialogs.DialogService.ShowDialog(this, picker);
            }

            HeroFilter = picker.SelectedIds;
        }

        /// <summary>
        ///     Enter the free heroes. Saved on close; there is as little a Cancel
        ///     here as with the rank or the hero filter.
        ///     <para>
        ///         Pre-filled with <c>Free</c>, i.e. with the calendar status of the current
        ///         period. Previously this was an empty list after every period change, and
        ///         pre-filling with the <i>saved</i> status was expressly undesired:
        ///         it would have stamped the old list with the new period on closing, without
        ///         doing anything. With the calendar this is reversed - what stands here already applies,
        ///         and whoever changes nothing, changes nothing.
        ///     </para>
        /// </summary>
        private void EditRotation()
        {
            var picker = new HeroPickerViewModel(_rotationGateway.Free, HeroPickerMode.Rotation,
                HotsRotationPeriod.Label(_rotationGateway.CurrentPeriod));
            using (Dialogs.Backdrop())
            {
                Dialogs.DialogService.ShowDialog(this, picker);
            }

            _rotationGateway.Save(picker.SelectedIds);
            RefreshRotation();
        }

        /// <summary>Everything that hangs off the rotation status - label, opacity, tooltip.</summary>
        private void RefreshRotation()
        {
            OnPropertyChanged(nameof(RotationLabel));
            OnPropertyChanged(nameof(RotationOpacity));
            OnPropertyChanged(nameof(RotationHint));
        }

        private void ShowDialog(Func<AddOrEditAccountViewModel, bool?> showDialog)
        {
            var dialogViewModel = new AddOrEditAccountViewModel(null);

            bool? success;
            using (Dialogs.Backdrop())
            {
                success = showDialog(dialogViewModel);
            }

            dialogViewModel.Execute(success);
        }


        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            // Trap: the count properties are notified from the CollectionChanged handler in
            // the constructor, and that notification runs through THIS SAME override (the
            // string overload of OnPropertyChanged calls the PropertyChangedEventArgs one).
            // Without this guard, re-filtering here fires Refresh(), Refresh() raises
            // CollectionChanged again, and the handler calls back in - the same shape as the
            // OnPropertyChanged/RefreshDialog loop in AddOrEditAccountViewModel, just with the
            // collection view standing in for the missing equality check.
            if (e.PropertyName is nameof(FilteredCount) or nameof(ScopeCount) or nameof(FilterCountLabel)
                or nameof(AccountRowsVisibility) or nameof(NoAccountsVisibility) or nameof(NoMatchesVisibility)
                or nameof(NoMatchesTitle) or nameof(NoMatchesHint))
            {
                return;
            }

            // A hidden filter must not filter - see HotsFiltersVisibility.
            // An empty list is therefore passed through; _heroFilter/_rankFilter themselves
            // remain untouched and apply again as soon as HotS returns.
            IReadOnlyCollection<string> heroes =
                _gameFilter == GameVisuals.Hots ? _heroFilter : Array.Empty<string>();
            IReadOnlyCollection<HotsRankTier> ranks =
                _gameFilter == GameVisuals.Hots ? _rankFilter : Array.Empty<HotsRankTier>();

            _battlenetAccountGateway.FilterBy(SearchQuery, _gameFilter, _regionFilter, heroes,
                _rotationGateway.Free, ranks, _showArchived);

            // SortField's own getter already falls back to LastRead while the stored choice
            // isn't offered (see its property), so applying that same getter here keeps what
            // gets sorted in lockstep with what the dropdown shows - no second fallback needed.
            _battlenetAccountGateway.SortBy(SortField, _sortDirection);
        }
    }

    /// <summary>One rank chip: which tier, its medal, and whether it is currently selected.</summary>
    public sealed record RankFilterOption(HotsRankTier Tier, string ImagePath, bool IsSelected);

    /// <summary>One entry of the sort dropdown: the field it selects, and its localized label.</summary>
    public sealed record SortFieldOption(AccountSortField Field, string Label);
}
