using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MvvmDialogs;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Texts;

namespace Smurftown.UI.MVVM.View;

public class AddOrEditAccountViewModel : ObservableObject, IModalDialogViewModel
{
    /// <summary>Upper bound for penalty games - keeps the display two digits, in reality it never gets that high.</summary>
    private const int MaxPenaltyGames = 99;

    private const string TabAccount = "account";
    private const string TabHots = "hots";
    private const string TabOverwatch = "overwatch";
    private const string TabWow = "wow";
    private const string TabDiablo = "diablo";

    private static readonly BattlenetAccountGateway _battlenetAccountGateway = BattlenetAccountGateway.Instance;

    private bool? _dialogResult;
    private string? _email;
    private string? _password;
    private int _hotsDivision = HotsRankTiers.LowestDivision;
    private List<string> _hotsHeroes = [];
    private int _hotsPenaltyGames;
    private HotsRankTier _hotsTier;
    private string _notes;
    private bool _placementsPending;

    /// <summary>
    ///     The checked regions <b>per game</b>, since 22.08.2026 - the working copy of
    ///     <see cref="BattlenetAccount.RegionsByGame" />.
    ///     <para>
    ///         It replaced two separate sets of ticks: four games and three regions, which
    ///         together could say something the account could not hold. A game needs at
    ///         least one region and a region needs at least one game, and here that is one
    ///         statement rather than two that have to agree.
    ///     </para>
    ///     <para>
    ///         <b>A game without a region is removed, not stored empty.</b> The same
    ///         invariant as in <c>BattlenetAccount.SetRegions</c>, and everything asking
    ///         "is this game played" relies on it - see <see cref="PlaysGame" />.
    ///     </para>
    /// </summary>
    private readonly Dictionary<string, HashSet<BattlenetRegion>> _regionsByGame = new();

    /// <summary>
    ///     The game state per region, as a <b>working copy</b>. Without the copy, tapping a
    ///     medal would already write into the entity, and "Cancel" would still change something.
    ///     <para>
    ///         Also contains states of regions that are currently <b>not</b> checked: removing a
    ///         checkmark should not discard the rank that was read, only hide the row.
    ///         Whoever checks the region again finds everything intact.
    ///     </para>
    /// </summary>
    private readonly Dictionary<BattlenetRegion, HotsRegionData> _data = new();

    /// <summary>
    ///     Which region the HotS tab currently shows. With only one checked region the
    ///     switch bar is invisible and this is simply that one.
    /// </summary>
    private BattlenetRegion _editRegion = BattlenetRegion.Europe;

    private HeroPickerViewModel _heroPicker;
    private string _tab = TabAccount;
    private bool _saveButtonEnabled;
    private string _saveHint = "";

    /// <summary>
    ///     Battletag of the account, just passed through. Since 21.08.2026 it is
    ///     <b>read and not typed</b> - the dialog shows it as text and no longer has a field
    ///     for it. A newly created account carries two empty strings here until
    ///     the first reading fills them.
    /// </summary>
    private readonly string _name;

    private readonly string _discriminator;

    /// <summary>
    ///     Archive flag, also a silent pass-through. It is set solely via the
    ///     button in the row - if it were not carried through the constructor here, an archived
    ///     account would be active again after the next save in the dialog.
    /// </summary>
    private readonly bool _inactive;

    /// <summary>
    ///     <paramref name="region" /> and <paramref name="hotsTab" /> come from the account
    ///     row, which knows both: a row is exactly one account in exactly one region, and a
    ///     click on its hero strip means the HotS tab and nothing else. Without them the
    ///     dialog opens on the first HotS region of the account - which, coming from an
    ///     Americas row, is the wrong one.
    /// </summary>
    public AddOrEditAccountViewModel(BattlenetAccount? account, BattlenetRegion? region = null,
        bool hotsTab = false)
    {
        _inactive = account?.Inactive ?? false;

        // Regions and game states. Both as a copy: the checkmarks must not affect the
        // saved account as long as nothing has been saved. A NEW account
        // starts without any checkmark - the choice is enforced, just like in the game.
        foreach (var game in GameVisuals.InDisplayOrder)
        {
            var regions = account?.RegionsFor(game) ?? [];
            if (regions.Count > 0) _regionsByGame[game] = new HashSet<BattlenetRegion>(regions);
        }

        foreach (var entry in account?.HotsByRegion ?? []) _data[entry.Key] = entry.Value.Copy();

        // The region the caller names, as long as the account plays HotS there - otherwise
        // the first region of HEROES OF THE STORM, since it is the game the tab below belongs
        // to. Without a checkmark it falls back to Europe; the tab is not visible at all then.
        _editRegion = region is { } wanted && HotsRegions().Contains(wanted)
            ? wanted
            : FirstHotsRegion();
        // No more long.Parse: since the discriminator is no longer typed, it can be empty,
        // and long.Parse("") ended the dialog with a FormatException before it even opened.
        _name = account?.Name ?? "";
        _discriminator = account?.Discriminator ?? "";
        Email = account?.Email ?? "";
        Password = account?.Password ?? "";
        Notes = account?.Notes ?? "";
        // Rank, penalty games and heroes come from the game state of the edited region.
        // The call also sets HeroPicker, which is why it stands before the commands.
        LoadRegion(_editRegion);

        OkCommand = new RelayCommand(Ok);
        CancelCommand = new RelayCommand(Cancel);
        PickRankCommand = new RelayCommand<HotsRankChoice>(PickRank);
        MorePenaltyCommand = new RelayCommand(() => HotsPenaltyGames = _hotsPenaltyGames + 1);
        LessPenaltyCommand = new RelayCommand(() => HotsPenaltyGames = _hotsPenaltyGames - 1);
        SwitchRegionCommand = new RelayCommand<RegionTab>(SwitchRegion);
        // Set BEFORE RefreshDialog and not after: its guard bounces the tab back to the
        // account when the game is not played, and that is exactly the check this needs.
        if (hotsTab) _tab = TabHots;

        RefreshDialog();
        NotifyTabs();
    }

    public bool SaveButtonEnabled
    {
        get { return _saveButtonEnabled; }
        set
        {
            if (value == _saveButtonEnabled) return;
            _saveButtonEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     Why the save button is off, in plain text next to it. Empty as soon as everything is set.
    ///     Without this sentence you have to guess which of the fields is still missing - especially
    ///     with the game checkmark, because it does not look like a required field.
    /// </summary>
    public string SaveHint
    {
        get => _saveHint;
        private set
        {
            if (value == _saveHint) return;
            _saveHint = value;
            OnPropertyChanged();
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (value == _notes) return;
            _notes = value;
            OnPropertyChanged();
        }
    }

    public string? Email
    {
        get => _email;
        set
        {
            if (value == _email) return;
            _email = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     Needs a real setter with notification, even though nobody displays the password.
    ///     As an auto-property, typing in the <c>PasswordBox</c> did not trigger
    ///     <see cref="RefreshDialog" />: whoever filled in the password as the last field saw the
    ///     save button stay off until they touched some other field.
    /// </summary>
    public string? Password
    {
        get => _password;
        set
        {
            if (value == _password) return;
            _password = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     The battletag as text - the dialog shows it instead of asking for it. A newly
    ///     created account does not have one yet; it then stands there exactly like that, because an
    ///     empty field looks like an error and a bare <c>#</c> like a half-loaded value.
    /// </summary>
    /// <summary>
    ///     In which regions this account is played - <b>checkmarks instead of a selection list</b>,
    ///     since 21.08.2026. The reason is the game state: it hangs on the region, so
    ///     an account that is played in two of them has two ranks and two hero lists. A single
    ///     preselection could not represent that.
    ///     <para>
    ///         They stand in the ACCOUNT tab, because they belong to the account and not to a
    ///         single game - even though today only Heroes of the Storm does anything with them.
    ///     </para>
    /// </summary>
    public bool HotsEurope
    {
        get => Ticked(Games.Hots, BattlenetRegion.Europe);
        set => Tick(Games.Hots, BattlenetRegion.Europe, value);
    }

    public bool HotsAmericas
    {
        get => Ticked(Games.Hots, BattlenetRegion.Americas);
        set => Tick(Games.Hots, BattlenetRegion.Americas, value);
    }

    public bool HotsAsia
    {
        get => Ticked(Games.Hots, BattlenetRegion.Asia);
        set => Tick(Games.Hots, BattlenetRegion.Asia, value);
    }

    public bool OverwatchEurope
    {
        get => Ticked(Games.Overwatch, BattlenetRegion.Europe);
        set => Tick(Games.Overwatch, BattlenetRegion.Europe, value);
    }

    public bool OverwatchAmericas
    {
        get => Ticked(Games.Overwatch, BattlenetRegion.Americas);
        set => Tick(Games.Overwatch, BattlenetRegion.Americas, value);
    }

    public bool OverwatchAsia
    {
        get => Ticked(Games.Overwatch, BattlenetRegion.Asia);
        set => Tick(Games.Overwatch, BattlenetRegion.Asia, value);
    }

    public bool WowEurope
    {
        get => Ticked(Games.Wow, BattlenetRegion.Europe);
        set => Tick(Games.Wow, BattlenetRegion.Europe, value);
    }

    public bool WowAmericas
    {
        get => Ticked(Games.Wow, BattlenetRegion.Americas);
        set => Tick(Games.Wow, BattlenetRegion.Americas, value);
    }

    public bool WowAsia
    {
        get => Ticked(Games.Wow, BattlenetRegion.Asia);
        set => Tick(Games.Wow, BattlenetRegion.Asia, value);
    }

    public bool DiabloEurope
    {
        get => Ticked(Games.Diablo, BattlenetRegion.Europe);
        set => Tick(Games.Diablo, BattlenetRegion.Europe, value);
    }

    public bool DiabloAmericas
    {
        get => Ticked(Games.Diablo, BattlenetRegion.Americas);
        set => Tick(Games.Diablo, BattlenetRegion.Americas, value);
    }

    public bool DiabloAsia
    {
        get => Ticked(Games.Diablo, BattlenetRegion.Asia);
        set => Tick(Games.Diablo, BattlenetRegion.Asia, value);
    }

    /// <summary>The labels of the matrix - the region name once per column, not twelve times.</summary>
    public string EuropeLabel => BattlenetRegion.Europe.DisplayName();

    public string AmericasLabel => BattlenetRegion.Americas.DisplayName();
    public string AsiaLabel => BattlenetRegion.Asia.DisplayName();

    public string HotsRowLabel => GameVisuals.ShortLabelFor(GameVisuals.Hots);
    public string OverwatchRowLabel => GameVisuals.ShortLabelFor(GameVisuals.Overwatch);
    public string WowRowLabel => GameVisuals.ShortLabelFor(GameVisuals.Wow);
    public string DiabloRowLabel => GameVisuals.ShortLabelFor(GameVisuals.Diablo);

    /// <summary>
    ///     The switch bar of the HotS tab - one per checked region, in the order
    ///     of the selection list.
    /// </summary>
    public IReadOnlyList<RegionTab> RegionTabs =>
        HotsRegions()
            .Select(region => new RegionTab(region, region.DisplayName(),
                region == _editRegion, SwitchRegionCommand))
            .ToList();

    /// <summary>
    ///     The bar is only shown when there is something to switch between. With a single region
    ///     it would be a switch with exactly one position.
    /// </summary>
    public Visibility RegionTabsVisibility =>
        HotsRegions().Count > 1 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    ///     States in plain text which region the rank, penalty games and heroes below
    ///     refer to. Without this sentence, an account with two regions would not show
    ///     what is currently being edited.
    /// </summary>
    public string RegionScopeHint => HotsRegions().Count == 0
        ? Strings.Current["dialog.pickRegionFirst"]
        : Strings.Format("dialog.regionScope", _editRegion.DisplayName());

    /// <summary>Is this game ticked in this region?</summary>
    private bool Ticked(string game, BattlenetRegion region)
    {
        return _regionsByGame.TryGetValue(game, out var regions) && regions.Contains(region);
    }

    /// <summary>Is this game played at all - in any region?</summary>
    private bool PlaysGame(string game)
    {
        return _regionsByGame.ContainsKey(game);
    }

    /// <summary>The regions of Heroes of the Storm, in the order of the selection list.</summary>
    private IReadOnlyList<BattlenetRegion> HotsRegions()
    {
        return _regionsByGame.TryGetValue(Games.Hots, out var regions)
            ? BattlenetRegions.InDisplayOrder.Where(regions.Contains).ToList()
            : [];
    }

    /// <summary>
    ///     Sets or removes one checkmark of the matrix - one game in one region.
    ///     <para>
    ///         The deselected game state is <b>not</b> deleted - see <see cref="_data" />.
    ///         If the region being edited falls away, the first still-checked one takes over:
    ///         otherwise the HotS tab would show the rank of a region this account no longer
    ///         has.
    ///     </para>
    ///     <para>
    ///         <b>Only Heroes of the Storm stashes and reloads</b>, because it is the only
    ///         game with a tab of its own below. For the other three a checkmark is nothing
    ///         but a checkmark - and running the stash for them would write the currently
    ///         edited HotS state back for no reason.
    ///     </para>
    /// </summary>
    private void Tick(string game, BattlenetRegion region, bool selected)
    {
        if (selected == Ticked(game, region)) return;

        var hots = game == Games.Hots;

        // Stash first, then switch: the state currently being edited still sits in the
        // properties and would otherwise be gone as soon as LoadRegion overwrites them.
        if (hots) StashRegion();

        if (selected)
        {
            if (!_regionsByGame.TryGetValue(game, out var regions))
            {
                regions = new HashSet<BattlenetRegion>();
                _regionsByGame[game] = regions;
            }

            regions.Add(region);
        }
        else if (_regionsByGame.TryGetValue(game, out var regions))
        {
            regions.Remove(region);

            // A GAME WITHOUT A REGION IS NOT PLAYED, and it is not stored as an empty
            // entry either: that would be a second way of saying the same thing, and
            // PlaysGame would have to know about both.
            if (regions.Count == 0) _regionsByGame.Remove(game);
        }

        if (hots && !HotsRegions().Contains(_editRegion)) LoadRegion(FirstHotsRegion());

        NotifyRegionTicks();
        NotifyRegionTabs();

        // The tab of a game that just lost its last region disappears with it - and if it
        // was the open one, the dialog has to move off it.
        NotifyTabs();
    }

    /// <summary>Lets all twelve checkmarks of the matrix re-read their state.</summary>
    private void NotifyRegionTicks()
    {
        OnPropertyChanged(nameof(HotsEurope));
        OnPropertyChanged(nameof(HotsAmericas));
        OnPropertyChanged(nameof(HotsAsia));
        OnPropertyChanged(nameof(OverwatchEurope));
        OnPropertyChanged(nameof(OverwatchAmericas));
        OnPropertyChanged(nameof(OverwatchAsia));
        OnPropertyChanged(nameof(WowEurope));
        OnPropertyChanged(nameof(WowAmericas));
        OnPropertyChanged(nameof(WowAsia));
        OnPropertyChanged(nameof(DiabloEurope));
        OnPropertyChanged(nameof(DiabloAmericas));
        OnPropertyChanged(nameof(DiabloAsia));
    }

    /// <summary>Switches the region being edited in the HotS tab.</summary>
    private void SwitchRegion(RegionTab? tab)
    {
        if (tab == null || tab.Region == _editRegion) return;

        StashRegion();
        LoadRegion(tab.Region);
        NotifyRegionTabs();
    }

    /// <summary>
    ///     Writes rank, penalty games, placement and heroes into the working copy of the
    ///     region currently being edited. To be called <b>before</b> every switch and before
    ///     saving - the properties are the only place where the typed values live until then.
    /// </summary>
    private void StashRegion()
    {
        if (!HotsRegions().Contains(_editRegion)) return;

        var data = Data(_editRegion);
        data.Tier = EffectiveTier;
        data.Division = EffectiveDivision;
        data.PenaltyGames = EffectivePenaltyGames;
        data.PlacementsPending = EffectivePlacementsPending;
        data.Heroes = EffectiveHeroes;
    }

    /// <summary>
    ///     Loads the state of a region into the properties of the tab. In doing so it rebuilds
    ///     the hero selection: <see cref="HeroPickerViewModel" /> keeps its own selection, a
    ///     mere re-set of the list would not reach it.
    /// </summary>
    private void LoadRegion(BattlenetRegion region)
    {
        _editRegion = region;
        var data = _data.GetValueOrDefault(region);

        HotsTier = data?.Tier ?? HotsRankTier.None;
        var division = data?.Division ?? 0;
        HotsDivision = division is >= HotsRankTiers.HighestDivision and <= HotsRankTiers.LowestDivision
            ? division
            : HotsRankTiers.LowestDivision;
        HotsPenaltyGames = Math.Clamp(data?.PenaltyGames ?? 0, 0, MaxPenaltyGames);
        PlacementsPending = data?.PlacementsPending ?? false;

        // filtered through the catalog: unknown identifiers from a newer app version
        // would otherwise ride along invisibly and still fall out again when saving
        HotsHeroes = HotsHeroCatalog.Resolve(data?.Heroes).Select(hero => hero.Id).ToList();

        // The same area as in the hero filter, just embedded: HeroPickerView hangs in the
        // HotS tab. The ViewModel lives as long as the region being edited and IS the source
        // of the selection - it is only read when stashing, exactly like with the rank.
        HeroPicker = new HeroPickerViewModel(_hotsHeroes, HeroPickerMode.Owned, "", true);
    }

    /// <summary>The working state of a region, created as soon as someone needs it.</summary>
    private HotsRegionData Data(BattlenetRegion region)
    {
        if (_data.TryGetValue(region, out var existing)) return existing;

        var fresh = new HotsRegionData();
        _data[region] = fresh;
        return fresh;
    }

    /// <summary>
    ///     The first region Heroes of the Storm is played in - Europe as the fallback, for a
    ///     dialog in which the game is not ticked at all. Its tab is not visible then.
    /// </summary>
    private BattlenetRegion FirstHotsRegion()
    {
        return HotsRegions().FirstOrDefault(BattlenetRegion.Europe);
    }

    private void NotifyRegionTabs()
    {
        OnPropertyChanged(nameof(RegionTabs));
        OnPropertyChanged(nameof(RegionTabsVisibility));
        OnPropertyChanged(nameof(RegionScopeHint));
    }

    public string BattletagLabel =>
        _name.Length > 0 && _discriminator.Length > 0
            ? $"{_name}#{_discriminator}"
            : Strings.Current["dialog.battletagUnread"];

    /// <summary>Dims the read battletag, as long as none is there.</summary>
    public double BattletagOpacity => _name.Length > 0 && _discriminator.Length > 0 ? 1.0 : 0.5;

    /// <summary>
    ///     The embedded hero selection of the HotS tab. Until 21.08.2026 a
    ///     separate window opened for this; since the dialog has tabs and a fixed size, there is
    ///     no reason for that any more. It is literally the same area - <see cref="HeroPickerView" />
    ///     hangs here and in the window of the hero filter.
    /// </summary>
    public HeroPickerViewModel HeroPicker
    {
        get => _heroPicker;
        private set => SetProperty(ref _heroPicker, value);
    }

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    public ICommand PickRankCommand { get; }
    public ICommand SwitchRegionCommand { get; }
    public ICommand MorePenaltyCommand { get; }
    public ICommand LessPenaltyCommand { get; }

    /// <summary>
    ///     Which tab is open. ACCOUNT always exists, plus one per checked game.
    ///     <para>
    ///         Built like the tabs of the main window and the game filter: one
    ///         <c>ToggleButton</c> each with a pass-through property. Deliberately <b>no</b>
    ///         <c>TabControl</c> - its template requires parts with fixed names
    ///         (<c>TabPanel</c>, <c>PART_SelectedContentHost</c>), and a missing one drops out without
    ///         a compile error and without a binding warning. The same trap as with the
    ///         <c>ComboBox</c> in the settings.
    ///     </para>
    ///     <para>
    ///         There is no deselecting: a <c>ToggleButton</c> unchecks itself on click,
    ///         <i>before</i> the binding writes. <see cref="NotifyTabs" /> lets it re-read the source
    ///         and thereby snaps it back.
    ///     </para>
    /// </summary>
    public bool AccountTabActive
    {
        get => _tab == TabAccount;
        set => ChooseTab(TabAccount, value);
    }

    public bool HotsTabActive
    {
        get => _tab == TabHots;
        set => ChooseTab(TabHots, value);
    }

    public bool OverwatchTabActive
    {
        get => _tab == TabOverwatch;
        set => ChooseTab(TabOverwatch, value);
    }

    public bool WowTabActive
    {
        get => _tab == TabWow;
        set => ChooseTab(TabWow, value);
    }

    public bool DiabloTabActive
    {
        get => _tab == TabDiablo;
        set => ChooseTab(TabDiablo, value);
    }

    public Visibility AccountTabVisibility => Show(_tab == TabAccount);
    public Visibility HotsTabVisibility => Show(_tab == TabHots);
    public Visibility OverwatchTabVisibility => Show(_tab == TabOverwatch);
    public Visibility WowTabVisibility => Show(_tab == TabWow);
    public Visibility DiabloTabVisibility => Show(_tab == TabDiablo);

    /// <summary>
    ///     Whether the tab exists at all - it hangs on the game's checkmark. Whoever
    ///     unchecks a game loses its tab; the values remain in the <c>data.yaml</c> and
    ///     are simply no longer shown.
    /// </summary>
    public Visibility HotsTabHeaderVisibility => Show(PlaysGame(Games.Hots));

    public Visibility OverwatchTabHeaderVisibility => Show(PlaysGame(Games.Overwatch));
    public Visibility WowTabHeaderVisibility => Show(PlaysGame(Games.Wow));
    public Visibility DiabloTabHeaderVisibility => Show(PlaysGame(Games.Diablo));

    /// <summary>
    ///     Short names, not the full ones: five tabs side by side, and "HEROES OF THE STORM"
    ///     alone needs more space than the other four combined. The blue bar under the
    ///     tab measures the button width, so here it measures the short text.
    /// </summary>
    public string HotsTabLabel => GameVisuals.ShortLabelFor(GameVisuals.Hots);

    public string OverwatchTabLabel => GameVisuals.ShortLabelFor(GameVisuals.Overwatch);
    public string WowTabLabel => GameVisuals.ShortLabelFor(GameVisuals.Wow);
    public string DiabloTabLabel => GameVisuals.ShortLabelFor(GameVisuals.Diablo);

    private static Visibility Show(bool on)
    {
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChooseTab(string tab, bool on)
    {
        // A false only ever comes from the ToggleButton unchecking itself - the tab stays where it is.
        if (!on || _tab == tab)
        {
            NotifyTabs();
            return;
        }

        _tab = tab;
        NotifyTabs();
    }

    /// <summary>
    ///     Lets all tabs re-read their state. Also called when a game checkmark
    ///     falls: its tab then disappears, and if it was open, the content must move along -
    ///     otherwise the dialog would be standing on a tab that no longer exists.
    /// </summary>
    private void NotifyTabs()
    {
        if (_tab == TabHots && !PlaysGame(Games.Hots)) _tab = TabAccount;
        if (_tab == TabOverwatch && !PlaysGame(Games.Overwatch)) _tab = TabAccount;
        if (_tab == TabWow && !PlaysGame(Games.Wow)) _tab = TabAccount;
        if (_tab == TabDiablo && !PlaysGame(Games.Diablo)) _tab = TabAccount;

        OnPropertyChanged(nameof(AccountTabActive));
        OnPropertyChanged(nameof(HotsTabActive));
        OnPropertyChanged(nameof(OverwatchTabActive));
        OnPropertyChanged(nameof(WowTabActive));
        OnPropertyChanged(nameof(DiabloTabActive));
        OnPropertyChanged(nameof(AccountTabVisibility));
        OnPropertyChanged(nameof(HotsTabVisibility));
        OnPropertyChanged(nameof(OverwatchTabVisibility));
        OnPropertyChanged(nameof(WowTabVisibility));
        OnPropertyChanged(nameof(DiabloTabVisibility));
        OnPropertyChanged(nameof(HotsTabHeaderVisibility));
        OnPropertyChanged(nameof(OverwatchTabHeaderVisibility));
        OnPropertyChanged(nameof(WowTabHeaderVisibility));
        OnPropertyChanged(nameof(DiabloTabHeaderVisibility));
    }

    public HotsRankTier HotsTier
    {
        get => _hotsTier;
        set
        {
            if (value == _hotsTier) return;
            _hotsTier = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RankColumns));
        }
    }

    public int HotsDivision
    {
        get => _hotsDivision;
        set
        {
            if (value == _hotsDivision) return;
            _hotsDivision = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RankColumns));
        }
    }

    /// <summary>Rank and penalty games hang on the HotS toggle - without HotS there is nothing to maintain here.</summary>
    /// <summary>Open penalty games. Clamped to 0..<see cref="MaxPenaltyGames" /> when set.</summary>
    public int HotsPenaltyGames
    {
        get => _hotsPenaltyGames;
        set
        {
            var clamped = Math.Clamp(value, 0, MaxPenaltyGames);
            if (clamped == _hotsPenaltyGames) return;
            _hotsPenaltyGames = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPenalty));
            OnPropertyChanged(nameof(PenaltyHint));
        }
    }

    /// <summary>Controls both the number badge and the opacity of the penalty triangle.</summary>
    public bool HasPenalty => _hotsPenaltyGames > 0;

    /// <summary>Tooltip on the penalty triangle - names the state and explains the otherwise invisible operation.</summary>
    public string PenaltyHint => Strings.Format("dialog.penaltyHint", _hotsPenaltyGames);

    /// <summary>
    ///     Placement games are pending. The rank stays set - it is only displayed as not yet
    ///     valid, the same in the dialog as on the card (dimmed).
    /// </summary>
    public bool PlacementsPending
    {
        get => _placementsPending;
        set
        {
            if (value == _placementsPending) return;
            _placementsPending = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     The rank grid. Built in <see cref="HotsRankGrid" /> since 23.08.2026, because the
    ///     account row offers the same grid in a popup on its medal - one copy of the layout
    ///     is the only way the two cannot drift.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<HotsRankChoice>>> RankColumns =>
        HotsRankGrid.Columns(_hotsTier, _hotsDivision, PickRankCommand);

    /// <summary>
    ///     Starting value of the hero selection. Only used to populate <see cref="HeroPicker" /> in
    ///     the constructor - afterwards the picker is the source, no longer this field.
    /// </summary>
    private IReadOnlyList<string> HotsHeroes
    {
        get => _hotsHeroes;
        set => _hotsHeroes = value.ToList();
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set
        {
            _dialogResult = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    ///     Normalization when saving: without HotS there is no rank, and tiers without divisions
    ///     get the 0. Prevents states like "Master 3" in the data.yaml.
    /// </summary>
    private HotsRankTier EffectiveTier =>
        PlaysGame(Games.Hots) ? _hotsTier : HotsRankTier.None;

    private int EffectiveDivision => EffectiveTier.HasDivisions() ? _hotsDivision : 0;

    private int EffectivePenaltyGames => PlaysGame(Games.Hots) ? _hotsPenaltyGames : 0;

    private bool EffectivePlacementsPending => PlaysGame(Games.Hots) && _placementsPending;

    /// <summary>Copy, so that ViewModel and entity do not share a list after saving.</summary>
    private List<string> EffectiveHeroes =>
        PlaysGame(Games.Hots) ? [.. HeroPicker.SelectedIds] : [];

    private void PickRank(HotsRankChoice? choice)
    {
        if (choice == null) return;

        HotsTier = choice.Tier;
        // Only adopt the division where it has meaning - otherwise the last one is kept
        if (choice.Tier.HasDivisions()) HotsDivision = choice.Division;
    }

    private void Cancel()
    {
        DialogResult = false;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs propertyChangedEventArgs)
    {
        base.OnPropertyChanged(propertyChangedEventArgs);
        RefreshDialog();
    }

    /// <summary>
    ///     At least one game must be checked. Until 21.08.2026 this asked only about Heroes
    ///     of the Storm and Overwatch - an account that plays only World of Warcraft or only
    ///     Diablo could not be saved at all with that, and the button did not say why.
    ///     Without a checkmark an account would also be unreachable in the overview: the
    ///     game filter has been exclusive since 20.08.2026, so no symbol matches it.
    ///     <para>
    ///         Since 22.08.2026 this covers the region as well: an entry only exists once a
    ///         game has at least one region, so one condition where there used to be two.
    ///         What the account would otherwise be saved as is invisible - the row it is
    ///         edited in is exactly the pair of game and region.
    ///     </para>
    /// </summary>
    private bool AnyGameChecked => _regionsByGame.Count > 0;

    /// <summary>
    ///     Sets the button and the hint. Both go through setters with an equality guard, and that is
    ///     not a matter of style here: <see cref="OnPropertyChanged(PropertyChangedEventArgs)" /> calls
    ///     this method, so every notification from here calls back into here again. A
    ///     property without an equality check in the setter spins endlessly at this point.
    /// </summary>
    private void RefreshDialog()
    {
        SaveButtonEnabled = !string.IsNullOrEmpty(_password)
                            && !string.IsNullOrEmpty(_email)
                            && AnyGameChecked;

        // Name and discriminator are deliberately NO LONGER in the condition - they are read,
        // not typed, and a new account does not have them yet.
        SaveHint = MissingFieldHint();
    }

    private string MissingFieldHint()
    {
        if (string.IsNullOrEmpty(_email)) return Strings.Current["dialog.needEmail"];
        if (string.IsNullOrEmpty(_password)) return Strings.Current["dialog.needPassword"];
        if (!AnyGameChecked) return Strings.Current["dialog.needGame"];
        return "";
    }

    private void Ok()
    {
        DialogResult = true;
    }

    public void Execute(bool? success)
    {
        if (!success.HasValue || !success.Value)
        {
            return;
        }

        // The last edited state still sits in the properties - without this stashing
        // exactly the region that was just open would be lost.
        StashRegion();

        var account = new BattlenetAccount
        {
            // Passed through unchanged - the dialog only displays the battletag. It is written
            // exclusively when reading it out of the profile overlay.
            Name = _name,
            Discriminator = _discriminator,
            Email = Email!,
            Password = Password!,
            LatestInteractionAt = DateTime.Now,
            Notes = Notes ?? "",
            Inactive = _inactive,

            // The matrix, in the order of the selection list rather than that of the clicks -
            // data.yaml should not change just because someone ticked America before Europe.
            // Games without a region are not in the dictionary at all, see Tick.
            RegionsByGame = _regionsByGame.ToDictionary(
                entry => entry.Key,
                entry => BattlenetRegions.InDisplayOrder.Where(entry.Value.Contains).ToList()),

            // The game states along with the values written by reading (gold, shards,
            // gems, level, chests, read timestamp). They appear in no input mask - they
            // come from the game, not from the human - but must pass through here: what is saved
            // is a NEWLY built account, and whatever does not arrive here would be deleted after every
            // manual change.
            //
            // States of deselected regions also ride along. Removing a checkmark hides
            // the row and does not throw away what was once read there.
            HotsByRegion = _data.ToDictionary(entry => entry.Key, entry => entry.Value)
        };

        _battlenetAccountGateway.AddOrUpdate(account);
    }
}

/// <summary>
///     One entry of the region switch bar in the HotS tab.
///     <para>
///         The command sits in the record and is not looked up via <c>RelativeSource</c> -
///         the same construction as the row's start menu and for the same reason: a field in the
///         record cannot bind into thin air.
///     </para>
/// </summary>
public sealed record RegionTab(BattlenetRegion Region, string Label, bool IsActive, ICommand Command)
{
    /// <summary>Not chosen means dimmed - the same language as in the rank and hero grids.</summary>
    public double Opacity => IsActive ? 1.0 : 0.45;

    /// <summary>The blue underline sits only on the open entry, as with the tabs above.</summary>
    public Visibility UnderlineVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
///     A role bar under the hero stack. <paramref name="Opacity" /> dims roles that
///     the account owns nothing of - the bar still stays in place so that the
///     order of the six roles is the same across all accounts.
/// </summary>
public sealed record HeroRoleBar(double Width, SolidColorBrush Brush, double Opacity, string Tooltip);