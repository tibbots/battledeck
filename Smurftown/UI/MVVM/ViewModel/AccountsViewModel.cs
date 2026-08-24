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

        public AccountsViewModel()
        {
            _battlenetAccountGateway.Reload();
            _accountRows = _battlenetAccountGateway.AccountRegionsFiltered;

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
        ///     The rows of the overview - one per account per selected region. Until
        ///     21.08.2026 these were the accounts themselves.
        /// </summary>
        public ICollectionView AccountRows
        {
            get { return _accountRows; }
            set => SetProperty(ref _accountRows, value);
        }

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

            // A hidden filter must not filter - see HotsFiltersVisibility.
            // An empty list is therefore passed through; _heroFilter itself remains
            // untouched and applies again as soon as HotS returns.
            IReadOnlyCollection<string> heroes =
                _gameFilter == GameVisuals.Hots ? _heroFilter : Array.Empty<string>();

            _battlenetAccountGateway.FilterBy(SearchQuery, _gameFilter, _regionFilter, heroes,
                _rotationGateway.Free, _showArchived);
        }
    }
}
