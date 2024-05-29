using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MvvmDialogs;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Texts;

namespace Smurftown.UI.MVVM.View;

/// <summary>
///     The hero picker - one surface, two callers: the account dialog picks the purchased
///     heroes, the filter bar the searched-for ones. The difference sits in
///     <see cref="HeroPickerMode" /> and affects only the label and the counter.
///     <para>
///         Why a separate window and not an overlay like the rank: the rank overlay sits in the
///         Grid of the account dialog, and that is <c>SizeToContent="Height"</c> - 90 circles
///         would tear it open on opening. Also, the filter bar calls it from an entirely
///         different window. A separate window solves both and lets the surface stay a single
///         one.
///     </para>
/// </summary>
public class HeroPickerViewModel : ObservableObject, IModalDialogViewModel
{
    private static readonly BattlenetAccountGateway _battlenetAccountGateway = BattlenetAccountGateway.Instance;
    private static readonly HotsRotationGateway _rotationGateway = HotsRotationGateway.Instance;

    private readonly IReadOnlyList<HeroChoiceViewModel> _all;
    private readonly HeroPickerMode _mode;
    private bool? _dialogResult;
    private IReadOnlyList<HeroGroupViewModel> _groups = [];
    private HotsHeroRole? _roleFilter;
    private string _searchQuery = "";

    public HeroPickerViewModel(IEnumerable<string>? selectedIds, HeroPickerMode mode, string subtitle = "",
        bool embedded = false)
    {
        _mode = mode;
        Subtitle = subtitle;
        Embedded = embedded;

        var selected = new HashSet<string>(selectedIds ?? [], StringComparer.OrdinalIgnoreCase);

        // In rotation mode the badge stays off: there the selection made IS the state "free".
        // A badge next to it would only show the previous state and would be wrong immediately
        // while editing.
        var free = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (mode != HeroPickerMode.Rotation) free.UnionWith(_rotationGateway.Free);

        _all = HotsHeroCatalog.All
            .Select(hero => new HeroChoiceViewModel(hero, mode, selected.Contains(hero.Id), free.Contains(hero.Id)))
            .ToList();

        Roles = HotsHeroRoles.InDisplayOrder.Select(role => new RoleChipViewModel(role)).ToList();

        ToggleHeroCommand = new RelayCommand<HeroChoiceViewModel>(ToggleHero);
        PickRoleCommand = new RelayCommand<RoleChipViewModel>(PickRole);
        SelectShownCommand = new RelayCommand(() => SetShown(true));
        ClearShownCommand = new RelayCommand(() => SetShown(false));
        CloseCommand = new RelayCommand(() => DialogResult = true);

        Rebuild();
    }

    /// <summary>
    ///     Does this picker hang in its own window, or embedded in another surface?
    ///     <para>
    ///         Deliberately a <b>second</b> axis next to <see cref="HeroPickerMode" /> and not a
    ///         fourth enum value: the mode says <i>what</i> is being picked (ownership, filter,
    ///         rotation), this here says <i>where</i> the surface hangs. Merged together they
    ///         would be six values, none of which would still let you tell which question it
    ///         answers.
    ///     </para>
    /// </summary>
    public bool Embedded { get; }

    /// <summary>
    ///     Title, close cross, and footer - everything only a window needs.
    ///     <para>
    ///         Embedded, the tab above already says what is being picked; a second title
    ///         would be a duplication. The cross would have nothing to close, and the hint "Esc
    ///         closes" would simply be wrong - there Esc belongs to the dialog. The search field
    ///         and the counter stay, they are needed in both hosts.
    ///     </para>
    /// </summary>
    public Visibility ChromeVisibility => Embedded ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    ///     Does the hero grid scroll itself, or does the host do it?
    ///     <para>
    ///         <b>In the window</b> (<c>Auto</c>) only the grid scrolls, and that is correct
    ///         there: the search field, role chips, and counter stay put while you page through.
    ///     </para>
    ///     <para>
    ///         <b>Embedded</b> (<c>Disabled</c>) the whole HotS tab of the account dialog
    ///         scrolls, so rank block and hero grid together. Two scroll areas nested inside one
    ///         another would be the worse operation there - the mouse wheel would hit one or the
    ///         other depending on cursor position.
    ///     </para>
    ///     <para>
    ///         <b>Why <c>Disabled</c> and not <c>Hidden</c>:</b> the two are not the same.
    ///         <c>Hidden</c> lets the <c>ScrollViewer</c> keep scrolling and only hides the bar -
    ///         the grid would stay clamped to the height of the tab and would have a second,
    ///         invisible scroll area. <c>Disabled</c> passes the height constraint through to
    ///         the child; under the outer <c>ScrollViewer</c>, which measures unbounded, the grid
    ///         then grows to its natural height and the outer one takes over the scrolling.
    ///     </para>
    /// </summary>
    public ScrollBarVisibility GridScrollBarVisibility =>
        Embedded ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

    /// <summary>Title - says what is being picked here.</summary>
    public string Title => Strings.Current[_mode switch
    {
        HeroPickerMode.Filter => "heroes.titleFilter",
        HeroPickerMode.Rotation => "heroes.titleRotation",
        _ => "heroes.titleOwned"
    }];

    /// <summary>
    ///     Battletag of the account being edited, for the rotation the time range of the
    ///     period; empty in filter mode.
    /// </summary>
    public string Subtitle { get; }

    public Visibility SubtitleVisibility =>
        string.IsNullOrEmpty(Subtitle) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Roles as chips - a click filters to one role, another click removes it again.</summary>
    public IReadOnlyList<RoleChipViewModel> Roles { get; }

    /// <summary>Visible heroes, grouped by role. Rebuilt on search and role change.</summary>
    public IReadOnlyList<HeroGroupViewModel> Groups
    {
        get => _groups;
        private set => SetProperty(ref _groups, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value ?? "")) return;
            Rebuild();
        }
    }

    public ICommand ToggleHeroCommand { get; }
    public ICommand PickRoleCommand { get; }
    public ICommand SelectShownCommand { get; }
    public ICommand ClearShownCommand { get; }
    public ICommand CloseCommand { get; }

    /// <summary>The selection made, in display order - the result for both callers.</summary>
    public IReadOnlyList<string> SelectedIds =>
        _all.Where(choice => choice.IsSelected).Select(choice => choice.Id).ToList();

    private int SelectedCount => _all.Count(choice => choice.IsSelected);

    /// <summary>"73 / 90 owned" in the dialog, "4 selected" in the filter, "14 / 14 free" for the rotation.</summary>
    public string CountLabel => _mode switch
    {
        HeroPickerMode.Filter => Strings.Format("heroes.countSelected", SelectedCount),
        HeroPickerMode.Rotation =>
            Strings.Format("heroes.countFree", SelectedCount, HotsRotationPeriod.HeroCount),
        _ => Strings.Format("heroes.countOwned", SelectedCount, HotsHeroCatalog.Count)
    };

    /// <summary>
    ///     How many accounts the current selection matches (OR: one of the chosen heroes is
    ///     enough). Makes searching short - you see immediately whether the selection is too
    ///     narrow.
    /// </summary>
    public string MatchLabel
    {
        get
        {
            var count = MatchingRows();
            return count == 1
                ? Strings.Current["heroes.matchOne"]
                : Strings.Format("heroes.matchMany", count);
        }
    }

    public Visibility MatchVisibility =>
        _mode == HeroPickerMode.Filter ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Label of the bulk buttons - they always only act on what is currently visible.</summary>
    public string SelectShownLabel =>
        Strings.Current[IsNarrowed ? "heroes.selectShown" : "heroes.selectAll"];

    public string ClearShownLabel =>
        Strings.Current[IsNarrowed ? "heroes.clearShown" : "heroes.clearAll"];

    private bool IsNarrowed => _roleFilter != null || _searchQuery.Trim().Length > 0;

    /// <summary>Only appears when the search finds nothing - otherwise the surface would be wordlessly empty.</summary>
    public Visibility EmptyHintVisibility =>
        Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
    ///     Counts by the same rule as the filter behind it (ownership or free rotation) -
    ///     otherwise the counter would contradict the count in the header of the list that
    ///     appears after closing.
    ///     <para>
    ///         <b>Rows are counted, not accounts</b>, and that is why it has been called
    ///         "entries" since 21.08.2026: hero ownership is tied to the region, so an account
    ///         can match in Europe and not in Americas. Counting over accounts here would keep
    ///         naming a smaller number than the list afterwards shows.
    ///     </para>
    /// </summary>
    private int MatchingRows()
    {
        var selected = SelectedIds;
        var free = _rotationGateway.Free;
        return _battlenetAccountGateway.AccountRegions
            .Count(row => row.Account.PlaysIn(Games.Hots, row.Region) &&
                          (selected.Count == 0 ||
                           BattlenetAccountGateway.CanPlayAnyHero(row, selected, free)));
    }

    private void ToggleHero(HeroChoiceViewModel? choice)
    {
        if (choice == null) return;
        choice.IsSelected = !choice.IsSelected;
        RefreshCounters();
    }

    private void PickRole(RoleChipViewModel? chip)
    {
        if (chip == null) return;
        _roleFilter = _roleFilter == chip.Role ? null : chip.Role;
        foreach (var role in Roles) role.IsActive = role.Role == _roleFilter;
        Rebuild();
    }

    private void SetShown(bool selected)
    {
        foreach (var group in Groups)
        foreach (var choice in group.Heroes)
            choice.IsSelected = selected;
        RefreshCounters();
    }

    /// <summary>
    ///     Reassemble the visible set. The choice objects remain the same ones - only that way
    ///     does the selection made survive a search or role change.
    /// </summary>
    private void Rebuild()
    {
        var query = _searchQuery.Trim();

        var visible = _all.Where(choice =>
            (query.Length == 0 || choice.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
            (_roleFilter == null || choice.Role == _roleFilter));

        Groups = visible
            .GroupBy(choice => choice.Role)
            .OrderBy(group => group.Key)
            .Select(group => new HeroGroupViewModel(group.Key, group.ToList()))
            .ToList();

        OnPropertyChanged(nameof(EmptyHintVisibility));
        OnPropertyChanged(nameof(SelectShownLabel));
        OnPropertyChanged(nameof(ClearShownLabel));
        RefreshCounters();
    }

    /// <summary>Everything that depends on the selection - header, chips, group counters.</summary>
    private void RefreshCounters()
    {
        OnPropertyChanged(nameof(CountLabel));
        OnPropertyChanged(nameof(MatchLabel));
        foreach (var chip in Roles) chip.Refresh(_all, _mode);
        foreach (var group in Groups) group.Refresh(_mode);
    }
}

/// <summary>What the picker was opened for. Changes only the label and the counter, not the operation.</summary>
public enum HeroPickerMode
{
    /// <summary>From the account dialog: which heroes this account owns.</summary>
    Owned,

    /// <summary>From the filter bar: which heroes are being searched for.</summary>
    Filter,

    /// <summary>
    ///     From the filter bar via right mouse button: which heroes are free in the current
    ///     period. The counter goes toward 14 instead of toward 90.
    /// </summary>
    Rotation
}

/// <summary>A hero in the grid. Ring and opacity depend on <see cref="IsSelected" />.</summary>
public class HeroChoiceViewModel : ObservableObject
{
    private readonly HeroPickerMode _mode;
    private bool _isSelected;

    public HeroChoiceViewModel(HotsHero hero, HeroPickerMode mode, bool isSelected, bool isFree)
    {
        Hero = hero;
        _mode = mode;
        _isSelected = isSelected;
        IsFree = isFree;
        ImageSource = HotsHeroImages.PathFor(hero);
        RoleBrush = HotsRoleColors.For(hero.Role);
    }

    public HotsHero Hero { get; }
    public string Id => Hero.Id;
    public string Name => Hero.Name;
    public HotsHeroRole Role => Hero.Role;
    public string ImageSource { get; }
    public SolidColorBrush RoleBrush { get; }

    /// <summary>
    ///     Free in the current period - depends solely on <c>HotsRotationGateway.Free</c>,
    ///     which is empty when the state is stale. A stale state therefore marks no one.
    /// </summary>
    public bool IsFree { get; }

    public Visibility FreeBadgeVisibility => IsFree ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The name only appears here - no hero is spelled out in the grid itself.</summary>
    public string Tooltip => IsFree
        ? $"{Hero.Name}\n{Hero.Role.DisplayName()}\n{Strings.Current["heroes.freeThisPeriod"]}"
        : $"{Hero.Name}\n{Hero.Role.DisplayName()}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            OnPropertyChanged(nameof(RingBrush));
            OnPropertyChanged(nameof(RingThickness));
            OnPropertyChanged(nameof(PortraitOpacity));
        }
    }

    /// <summary>Color is only carried by what is selected - the rest gets the neutral ring.</summary>
    public SolidColorBrush RingBrush => _isSelected ? RoleBrush : HotsRoleColors.Unselected;

    public double RingThickness => _isSelected ? 2.0 : 1.0;

    /// <summary>
    ///     What is not selected is dimmed instead of desaturated - the same decision as with the
    ///     rank with pending placement games: grayscale costs a second asset per hero, opacity
    ///     costs nothing. Reads like in-game: what you do not have is dark.
    ///     <para>
    ///         In the account dialog bright means <b>playable</b> and not <b>purchased</b>: the
    ///         free rotation is open to every account, so it stays bright even when not
    ///         purchased. In the filter bar, on the other hand, bright means "selected by me" -
    ///         there fourteen bright circles without a selection would be a false statement,
    ///         hence the mode condition. The counter remains unaffected by this: "73 / 90 owned"
    ///         keeps counting ownership.
    ///     </para>
    /// </summary>
    public double PortraitOpacity =>
        _isSelected || (IsFree && _mode == HeroPickerMode.Owned) ? 1.0 : 0.3;
}

/// <summary>A role group in the grid, with a header in the role color.</summary>
public class HeroGroupViewModel : ObservableObject
{
    private string _countLabel = "";

    public HeroGroupViewModel(HotsHeroRole role, IReadOnlyList<HeroChoiceViewModel> heroes)
    {
        Role = role;
        Heroes = heroes;
        RoleName = role.DisplayName().ToUpperInvariant();
        RoleBrush = HotsRoleColors.For(role);
    }

    public HotsHeroRole Role { get; }
    public IReadOnlyList<HeroChoiceViewModel> Heroes { get; }
    public string RoleName { get; }
    public SolidColorBrush RoleBrush { get; }

    /// <summary>"12 / 17" in the dialog, plain count in the filter - there no one owns anything.</summary>
    public string CountLabel
    {
        get => _countLabel;
        private set => SetProperty(ref _countLabel, value);
    }

    public void Refresh(HeroPickerMode mode)
    {
        CountLabel = mode == HeroPickerMode.Filter
            ? Heroes.Count.ToString()
            : $"{Heroes.Count(hero => hero.IsSelected)} / {Heroes.Count}";
    }
}

/// <summary>Role chip above the grid: filters to one role and shows its state.</summary>
public class RoleChipViewModel : ObservableObject
{
    private string _countLabel = "";
    private bool _isActive;

    public RoleChipViewModel(HotsHeroRole role)
    {
        Role = role;
        Name = role.DisplayName();
        RoleBrush = HotsRoleColors.For(role);
    }

    public HotsHeroRole Role { get; }
    public string Name { get; }
    public SolidColorBrush RoleBrush { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public string CountLabel
    {
        get => _countLabel;
        private set => SetProperty(ref _countLabel, value);
    }

    public void Refresh(IEnumerable<HeroChoiceViewModel> all, HeroPickerMode mode)
    {
        var ofRole = all.Where(choice => choice.Role == Role).ToList();
        CountLabel = mode == HeroPickerMode.Filter
            ? ofRole.Count.ToString()
            : $"{ofRole.Count(choice => choice.IsSelected)}/{ofRole.Count}";
    }
}

/// <summary>
///     A hero as pure display - for the stacks in the account dialog and in the filter bar,
///     where nothing is toggled.
/// </summary>
public sealed record HeroChip(string ImageSource, SolidColorBrush RoleBrush, string Tooltip)
{
    public static HeroChip For(HotsHero hero)
    {
        return new HeroChip(HotsHeroImages.PathFor(hero), HotsRoleColors.For(hero.Role),
            $"{hero.Name}\n{hero.Role.DisplayName()}");
    }
}
