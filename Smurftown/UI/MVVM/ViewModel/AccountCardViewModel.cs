using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Smurftown.Backend.Automation;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.UI.MVVM.View;
using ToastNotifications.Messages;
using Smurftown.Backend.Texts;

namespace Smurftown.UI.MVVM.ViewModel
{
    /// <summary>
    ///     An entry in the card's start menu: one use case of a game.
    ///     <para>
    ///         The command sits in the entry itself and is not looked up via <c>RelativeSource</c>
    ///         from the menu. A popup lies outside the layout tree of the
    ///         card - exactly the fragility that is also why the rank picker is an overlay and
    ///         not a popup. A field in the record cannot bind into nothing.
    ///     </para>
    /// </summary>
    sealed record StartOption(
        string Icon, string Label, string Hint, string Mode, bool Enabled, ICommand Command);

    /// <summary>
    ///     What a start should do. Three switches, but only four valid combinations - they
    ///     stand as named cases below. The rest make no sense: not reading and
    ///     still closing would be a start that ends itself, and opening chests
    ///     without reading afterwards would mean throwing away the gain.
    /// </summary>
    sealed record SessionPlan(bool OpenChests, bool Read, bool CloseAfterwards)
    {
        /// <summary>Just start and sign in - the game belongs to the human immediately afterwards.</summary>
        public static readonly SessionPlan JustPlay = new(false, false, false);

        /// <summary>Start, sign in, read; the game stays open.</summary>
        public static readonly SessionPlan PlayAndRead = new(false, true, false);

        /// <summary>Start, sign in, read, close.</summary>
        public static readonly SessionPlan RefreshOnly = new(false, true, true);

        /// <summary>
        ///     Start, sign in, <b>open the chests first</b>, then read, then close.
        ///     The order is the point: a chest drops shards, gold and occasionally
        ///     a hero. Read beforehand, data.yaml would hold the state from before -
        ///     and that is wrong from the first opening onward.
        /// </summary>
        public static readonly SessionPlan Chests = new(true, true, true);
    }

    class AccountCardViewModel : ObservableObject
    {
        private static readonly BattlenetAccountGateway _battlenetAccountGateway = BattlenetAccountGateway.Instance;

        /// <summary>
        ///     How many hero portraits the strip shows before it switches to "+n".
        ///     Eleven circles of 47 points with 13 points of overlap take up 387 of the roughly 508
        ///     points that are free for it in the panel.
        ///     <para>
        ///         <b>The upper bound is the width, the number itself is chosen.</b>
        ///         Fourteen would fit (489) - eleven leave 121 points of air between
        ///         strip and currencies, and that is intentional: a row that is full from left to
        ///         right reads harder than one with a pause in it.
        ///     </para>
        ///     <para>
        ///         It jumped twice on 21.08.2026: from eleven to eight, when medal and
        ///         circles grew by 30 percent - and back to eleven, when on the same day the
        ///         penalty triangle disappeared from the panel (47 points), four buttons became one
        ///         menu (167), and the accent strip fell away (3). The budget stands in
        ///         <c>AccountCardView.xaml</c>.
        ///     </para>
        ///     <para>
        ///         Which eleven is deliberately not sorted: it is the order in which the
        ///         read-out found them, so alphabetical. The strip is a
        ///         <b>sample</b> - the actual statement stands as a number next to it. Sorted by role
        ///         you would never see a healer on an account with eleven tanks.
        ///     </para>
        /// </summary>
        private const int HeroChipLimit = 11;

        /// <summary>
        ///     The use cases the start menu offers. Not an enum, because the value runs as
        ///     CommandParameter through the XAML and is text there anyway.
        /// </summary>
        private const string ModeStart = "start";

        private const string ModePlay = "play";
        private const string ModeRefresh = "refresh";
        private const string ModeChests = "chests";

        private const string HotsIcon = "pack://application:,,,/UI/Images/hots.png";

        private AccountRegion? _row;
        private RelayCommand? _archiveCommand;
        private RelayCommand? _copyPasswordCommand;
        private RelayCommand? _copyUsernameCommand;
        private string _currencyHint = "";
        private Visibility _diablo;
        private string _gemsText = "";
        private string _goldText = "";
        private string _shardsText = "";
        private bool _hasStartOptions;
        private string _hotsHint = "";
        private Visibility _hots;

        private string _imageSource;


        private RelayCommand<string>? _runStartOptionCommand;
        private RelayCommand _openSettingsCommand;

        private bool _startMenuOpen;
        private bool _actionsMenuOpen;
        private bool _rankMenuOpen;
        private RelayCommand<HotsRankChoice>? _pickRankCommand;
        private RelayCommand? _editHeroesCommand;
        private IReadOnlyList<StartOption> _startOptions = [];

        private Visibility _overwatch;

        private string _penaltyName = "";
        private string _regionLabel = "";
        private string _regionHint = "";
        private Visibility _penaltyVisibility = Visibility.Collapsed;

        private HotsRankTier _rankTier = HotsRankTier.None;
        private int _rankDivision;
        private string _rankName = "";
        private string _rankHint = "";
        private double _rankOpacity = 1.0;
        private Visibility _rankVisibility = Visibility.Collapsed;
        private double _rankProgress;
        private bool _rankShowProgress;
        private Visibility _wow;


        private Visibility _hotsPanelVisibility = Visibility.Collapsed;
        private Visibility _noDataVisibility = Visibility.Collapsed;
        private string _noDataTitle = "";
        private string _noDataHint = "";
        private Brush _panelTint = GameVisuals.TintFor(null);
        private Brush _panelHoverBorder = GameVisuals.HoverBorderFor(null);
        private Brush _stripSeparator = GameVisuals.StripSeparatorFor(null);

        private IReadOnlyList<HeroChip> _heroChips = [];
        private Visibility _heroChipsVisibility = Visibility.Collapsed;
        private Visibility _heroEmptyVisibility = Visibility.Collapsed;
        private string _heroOverflow = "";
        private Visibility _heroOverflowVisibility = Visibility.Collapsed;
        private string _heroCountText = "";

        private string _chestsText = "";
        private string _readAtText = "";

        /// <summary>
        ///     Since 21.08.2026 the name column has had two states, because the battletag is no
        ///     longer typed but read: there is none until the first read, and until then
        ///     the email carries the row. Without this case a lone <c>#</c> would stand there.
        /// </summary>
        private Visibility _battletagVisibility = Visibility.Visible;

        private Visibility _nameFallbackVisibility = Visibility.Collapsed;
        private string _nameFallback = "";

        public AccountCardViewModel(AccountRegion row)
        {
            Row = row;
            var account = row.Account;

            // The symbols of THIS region, not of the account. Since 22.08.2026 the regions
            // hang on the game, so an account played in Europe and America can well be a
            // Heroes of the Storm one over there and a World of Warcraft one over here.
            Overwatch = Shown(account.PlaysIn(Games.Overwatch, row.Region));
            Hots = Shown(account.PlaysIn(Games.Hots, row.Region));
            Diablo = Shown(account.PlaysIn(Games.Diablo, row.Region));
            Wow = Shown(account.PlaysIn(Games.Wow, row.Region));
        }

        public AccountCardViewModel()
        {
        }

        private static Visibility Shown(bool yes)
        {
            return yes ? Visibility.Visible : Visibility.Collapsed;
        }

        public Visibility Overwatch
        {
            get { return _overwatch; }
            set { SetProperty(ref _overwatch, value); }
        }

        public Visibility Hots
        {
            get { return _hots; }
            set { SetProperty(ref _hots, value); }
        }

        public Visibility Wow
        {
            get { return _wow; }
            set { SetProperty(ref _wow, value); }
        }

        public Visibility Diablo
        {
            get { return _diablo; }
            set { SetProperty(ref _diablo, value); }
        }

        /// <summary>
        ///     The row: an account in ONE of its regions. Since 21.08.2026 this is the
        ///     unit of the overview - whoever plays in Europe and Americas has two rows with
        ///     two ranks, two hero lists and two gold balances.
        ///     <para>
        ///         Everything set here comes either from the account (email, password,
        ///         game checkboxes, archive) or from the <b>game state of this one region</b>. The
        ///         boundary between the two is the actual point of this setter.
        ///     </para>
        /// </summary>
        public AccountRegion? Row
        {
            get { return _row; }
            set
            {
                _row = value;
                var account = value!.Account;

                // The game state of THIS region - null if it was never read here. Every
                // number below then falls back to the dash, and that is the
                // difference to "has nothing": a 0 would be a statement we don't have.
                var data = value.Hots;

                // Is Heroes of the Storm played IN THIS REGION? Since 22.08.2026 that is the
                // question everything below hangs on, and it is a different one from "does
                // this account play HotS at all": the regions belong to the game. An American
                // row of a European-only HotS account has no rank, no heroes and no penalty
                // games - and must not show the ones from Europe.
                var hots = account.PlaysIn(Games.Hots, value.Region);
                var overwatch = account.PlaysIn(Games.Overwatch, value.Region);

                if (overwatch && hots)
                {
                    ImageSource = "pack://application:,,,/UI/Images/overwatchhots_full.png";
                }
                else
                {
                    ImageSource = overwatch
                        ? "pack://application:,,,/UI/Images/overwatch_full.png"
                        : "pack://application:,,,/UI/Images/hots_full.png";
                }

                // The region abbreviation names the row. It is ALWAYS there, even for an
                // account with only one region: a column that sometimes says something and sometimes
                // not leaves it open, when skimming, whether the value is missing or does not apply.
                RegionLabel = value.Region.ShortName();
                RegionHint = Strings.Format("row.regionHint", value.Region.DisplayName());

                // Only show rank if HotS is played at all and a tier is set.
                //
                // OPEN PLACEMENT MATCHES DIM THE MEDAL (0,4), they do not replace it.
                // On 21.08.2026 a separate icon briefly stood here - the same magenta-colored
                // circle that the game shows in the profile in place of the rank circle. It is
                // gone again: the dimmed medal shows the rank of the previous season TOO, the
                // separate icon threw it away and left only the tooltip. The circle is
                // not lost in the process - it is now the icon for "no rank", see
                // HotsRankImages.NoRank.
                //
                // WITHOUT A RANK AND WITH AN OPEN PLACEMENT the NoRank disc stands there, likewise
                // dimmed: otherwise this state would be invisible in the row. Without a rank and
                // without a placement, the spot stays empty - the rank is then simply nothing
                // for the row to say.
                var placements = hots && data is { PlacementsPending: true };
                RankTier = hots && data != null ? data.Tier : HotsRankTier.None;
                RankDivision = hots && data != null ? data.Division : 0;
                var hasRank = RankTier != HotsRankTier.None;
                RankVisibility = hasRank || placements ? Visibility.Visible : Visibility.Collapsed;
                RankName = RankLabel(hots, data, placements, hasRank);

                // THE RING SHOWS ONLY WHAT WAS READ. Without points the medal stands
                // untouched - which is also how it looks at zero percent, and that is the
                // one ambiguity worth naming: the picture cannot tell "at the start of the
                // division" from "never read". The tooltip can, and does, by saying the two
                // numbers or nothing at all.
                //
                // MASTER AND GRAND MASTER CARRY NO RING, and an unplaced account carries
                // none either: there is no next division to fill towards. That question is
                // answered by HasDivisions() - a second list of tiers written out here
                // would drift apart from it at the next change. Pending placements hide it
                // too: the medal is dimmed exactly because it does not count.
                var ranked = hasRank && RankTier.HasDivisions() && !placements;
                RankShowProgress = ranked && data?.RankProgress != null;
                RankProgress = RankShowProgress ? data!.RankProgress!.Value : 0;
                if (RankShowProgress)
                    RankName = Strings.Format("row.rankProgress",
                        RankName, data!.RankPoints, data.RankPointsMax);

                RankHint = Hinted(RankName, "row.rankClickHint");
                RankOpacity = placements ? 0.4 : 1.0;

                // Name column: the battletag, as soon as one has been read, otherwise the email.
                // It is the account's identity anyway and the only value that
                // exists for every one - an empty name plus "#" would look like an error by contrast.
                BattletagVisibility = account.HasBattletag ? Visibility.Visible : Visibility.Collapsed;
                NameFallbackVisibility = account.HasBattletag ? Visibility.Collapsed : Visibility.Visible;
                NameFallback = account.Email;

                // Penalty games: the row only shows the WARNING TRIANGLE, without a number, 18 points
                // large in the top left corner - and only when > 0. The count is in the tooltip
                // (PenaltyName); a dedicated property for it is deliberately gone, it
                // would have no consumer. The icon sits OUTSIDE the panel and thereby survives
                // a change of the game filter - the corner is free because the
                // name column sits vertically centered.
                var penalties = hots ? data?.PenaltyGames ?? 0 : 0;
                PenaltyVisibility = penalties > 0 ? Visibility.Visible : Visibility.Collapsed;
                PenaltyName = penalties == 1
                    ? Strings.Current["row.penaltyOne"]
                    : Strings.Format("row.penaltyMany", penalties);

                // Heroes as a portrait strip instead of a bare number. At 380 points of card width
                // there is room for this for the first time - in the old 280-wide card the game row
                // had 7 of 246 points free, only a badge on the symbol fit there.
                IReadOnlyList<HotsHero> heroes =
                    hots ? HotsHeroCatalog.Resolve(data?.Heroes) : [];
                HeroChips = BuildHeroChips(heroes);
                HeroChipsVisibility = heroes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                HeroEmptyVisibility = heroes.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                HeroOverflow = heroes.Count > HeroChipLimit ? $"+{heroes.Count - HeroChipLimit}" : "";
                HeroOverflowVisibility =
                    heroes.Count > HeroChipLimit ? Visibility.Visible : Visibility.Collapsed;
                // A dash and no 0, when it was never read AND nothing is entered:
                // "0 of 90" claims the account owns not a single hero - a
                // statement that does not exist without reading. Same rule as for the four
                // stats. If heroes are entered, the number is valid even without a read timestamp:
                // then they are hand-maintained.
                HeroCountText = heroes.Count == 0 && data?.ReadAt == null
                    ? $"– / {HotsHeroCatalog.Count}"
                    : $"{heroes.Count} / {HotsHeroCatalog.Count}";
                HeroStripHint = Hinted(HeroLabel(heroes), "row.heroesClickHint");

                // The stats that were read. They now sit IN the HotS panel and therefore need
                // no dedicated visibility anymore: the panel is only visible when HotS is chosen,
                // and choosable only if the account has the game at all.
                // Chests were added as a fourth column - at 380 points of width there is room,
                // and a number with a call to action does not belong in a tooltip.
                GoldText = Amount(data?.Gold);
                ShardsText = Amount(data?.Shards);
                GemsText = Amount(data?.Gems);
                ChestsText = Amount(data?.LootChests);
                CurrencyHint = CurrencyLabel(hots, data);
                ReadAtText = data?.ReadAt == null
                    ? Strings.Current["row.neverRead"]
                    : Strings.Format("row.readAt", $"{data.ReadAt:yyyy-MM-dd HH:mm}");

                // The strip, then the preselection. Heroes of the Storm first, because only there
                // is any data at all; if the account doesn't have it, the choice falls back to the
                // first game it does have.
                SelectGame(PreferredGame(AvailableGames(account, value.Region)));

                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     The account of this row. Shorthand for <c>Row.Account</c> - and deliberately a
        ///     computed property instead of a second field: two fields side by side would be
        ///     two truths, one of which could go stale.
        /// </summary>
        public BattlenetAccount? Account => _row?.Account;

        /// <summary>The account's gold, formatted. A dash means "never read yet".</summary>
        public string GoldText
        {
            get { return _goldText; }
            set { SetProperty(ref _goldText, value); }
        }

        public string ShardsText
        {
            get { return _shardsText; }
            set { SetProperty(ref _shardsText, value); }
        }

        public string GemsText
        {
            get { return _gemsText; }
            set { SetProperty(ref _gemsText, value); }
        }

        /// <summary>
        ///     Unopened loot chests as the fourth column. Until the rebuild they only stood in the
        ///     tooltip, and that was the wrong place: 24 chests mean "there is something to
        ///     get here", and a number with a call to action belongs on the card.
        /// </summary>
        public string ChestsText
        {
            get { return _chestsText; }
            set { SetProperty(ref _chestsText, value); }
        }

        /// <summary>
        ///     When it was last read, in plain text under the stats. Without the timestamp
        ///     none of the numbers can be placed in context - 1.800 gold from today and 1.800 gold from
        ///     three months ago look the same.
        /// </summary>
        public string ReadAtText
        {
            get { return _readAtText; }
            set { SetProperty(ref _readAtText, value); }
        }

        /// <summary>
        ///     The tooltip of the stats row. It carries what has no room on the card:
        ///     the account level and once more the read timestamp in full format.
        ///     <para>
        ///         Chests and read timestamp have stood <b>on</b> the card since the rebuild - the
        ///         tooltip was the wrong place for a number that means "there is something to
        ///         get here".
        ///     </para>
        ///     <para>
        ///         The row no longer needs its own visibility: it sits in the
        ///         HotS panel, and that is only visible when HotS is chosen - choosable
        ///         in turn only if the account has the game. A second condition with
        ///         the same meaning would be exactly the place where two truths
        ///         drift apart.
        ///     </para>
        /// </summary>
        public string CurrencyHint
        {
            get { return _currencyHint; }
            set { SetProperty(ref _currencyHint, value); }
        }

        /// <summary>
        ///     Which medal the row shows. <see cref="HotsRankTier.None" /> means the
        ///     "no rank" disc, which is what an account with open placements and no rank
        ///     of its own gets.
        ///     <para>
        ///         Tier and division instead of a finished picture path, since 24.08.2026:
        ///         <see cref="RankMedal" /> draws the digit itself, and it needs to know
        ///         which one rather than getting a bitmap with one baked in.
        ///     </para>
        /// </summary>
        public HotsRankTier RankTier
        {
            get { return _rankTier; }
            set { SetProperty(ref _rankTier, value); }
        }

        /// <summary>Division 5 to 1, or 0 where the tier has none.</summary>
        public int RankDivision
        {
            get { return _rankDivision; }
            set { SetProperty(ref _rankDivision, value); }
        }

        public Visibility RankVisibility
        {
            get { return _rankVisibility; }
            set { SetProperty(ref _rankVisibility, value); }
        }

        /// <summary>Name and discriminator of the name column - visible as soon as a battletag has been read.</summary>
        public Visibility BattletagVisibility
        {
            get { return _battletagVisibility; }
            set { SetProperty(ref _battletagVisibility, value); }
        }

        /// <summary>The opposite state: the email carries the row as long as there is no battletag yet.</summary>
        public Visibility NameFallbackVisibility
        {
            get { return _nameFallbackVisibility; }
            set { SetProperty(ref _nameFallbackVisibility, value); }
        }

        public string NameFallback
        {
            get { return _nameFallback; }
            set { SetProperty(ref _nameFallback, value); }
        }

        /// <summary>Plain text for the tooltip, e.g. "Gold 3" or "Gold 3 - placements pending".</summary>
        /// <summary>
        ///     The tooltip of the medal: the rank plus the sentence that it can be clicked.
        ///     Separate from <see cref="RankName" />, which stays a name - the call to action
        ///     belongs in a hint, the way <c>HeroStripHint</c> and <c>CurrencyHint</c> carry
        ///     theirs.
        /// </summary>
        public string RankHint
        {
            get { return _rankHint; }
            set { SetProperty(ref _rankHint, value); }
        }

        public string RankName
        {
            get { return _rankName; }
            set { SetProperty(ref _rankName, value); }
        }

        /// <summary>Dimmed as long as placement matches are outstanding - the rank does not count yet then.</summary>
        public double RankOpacity
        {
            get { return _rankOpacity; }
            set { SetProperty(ref _rankOpacity, value); }
        }

        /// <summary>
        ///     How full the ring around the medal stands, 0..1 - the progress inside the
        ///     current division, the way the game draws it.
        ///     <para>
        ///         A fraction and not a pair of numbers, because that is all the ring needs.
        ///         The pair the game shows on hover ("328 / 1000") belongs in the text, and
        ///         the text is <see cref="RankName" />.
        ///     </para>
        /// </summary>
        public double RankProgress
        {
            get { return _rankProgress; }
            set { SetProperty(ref _rankProgress, value); }
        }

        /// <summary>
        ///     Separate from <see cref="RankVisibility" />: the medal is there in cases the
        ///     ring is not - Master, Grand Master, and an account whose placement matches
        ///     are still open. False leaves the ring fully lit, which is the medal as it
        ///     stands; a ring at zero would claim a progress of nothing instead of saying
        ///     that the question does not apply.
        /// </summary>
        public bool RankShowProgress
        {
            get { return _rankShowProgress; }
            set { SetProperty(ref _rankShowProgress, value); }
        }


        /// <summary>
        ///     The rank in plain text - <b>only still</b> as a tooltip on the medal.
        ///     <para>
        ///         Until 20.08.2026 there was additionally a short form next to the medal
        ///         ("Gold 3", "Placements pending", "Unranked"). It is gone because it
        ///         cost 112 points and said little that the image next to it doesn't already show:
        ///         the 28 medals carry tier AND division, and with open
        ///         placement matches the medal is dimmed. What the image cannot say
        ///         stands here - which is why this text must cover all three cases. With an
        ///         open placement it is the only place that NAMES the state: the
        ///         medal shows the rank of the previous season, that it does not count yet is said only by the
        ///         opacity.
        ///     </para>
        /// </summary>
        private static string RankLabel(bool hots, HotsRegionData? data,
            bool placements, bool hasRank)
        {
            if (!hots) return "";

            var name = data?.RankName() ?? "";
            if (!placements) return name;
            return hasRank
                ? Strings.Format("row.rankPlacements", name)
                : Strings.Current["row.placementsPending"];
        }

        /// <summary>
        ///     Two letters for the region of this row - EU, AM or AS. No image: the
        ///     game has no region symbols, and three invented ones would be three symbols that
        ///     nobody knows.
        /// </summary>
        public string RegionLabel
        {
            get { return _regionLabel; }
            private set { SetProperty(ref _regionLabel, value); }
        }

        /// <summary>The full name plus the sentence saying what the values next to it refer to.</summary>
        public string RegionHint
        {
            get { return _regionHint; }
            private set { SetProperty(ref _regionHint, value); }
        }

        public Visibility PenaltyVisibility
        {
            get { return _penaltyVisibility; }
            set { SetProperty(ref _penaltyVisibility, value); }
        }

        /// <summary>Plain text for the tooltip, e.g. "3 penalty games".</summary>
        public string PenaltyName
        {
            get { return _penaltyName; }
            set { SetProperty(ref _penaltyName, value); }
        }

        /// <summary>
        ///     The first <see cref="HeroChipLimit" /> portraits of the strip. How many there
        ///     really are stands as a number next to it - the strip is the sample, not
        ///     the actual statement.
        /// </summary>
        public IReadOnlyList<HeroChip> HeroChips
        {
            get { return _heroChips; }
            private set { SetProperty(ref _heroChips, value); }
        }

        public Visibility HeroChipsVisibility
        {
            get { return _heroChipsVisibility; }
            private set { SetProperty(ref _heroChipsVisibility, value); }
        }

        /// <summary>
        ///     Visible when no hero is entered. An empty strip would be a hole in the
        ///     layout and would leave open whether it was never read or there is nothing there - the sentence
        ///     in its place says it.
        /// </summary>
        public Visibility HeroEmptyVisibility
        {
            get { return _heroEmptyVisibility; }
            private set { SetProperty(ref _heroEmptyVisibility, value); }
        }

        /// <summary>"+19", when there are more heroes than the strip shows.</summary>
        public string HeroOverflow
        {
            get { return _heroOverflow; }
            private set { SetProperty(ref _heroOverflow, value); }
        }

        public Visibility HeroOverflowVisibility
        {
            get { return _heroOverflowVisibility; }
            private set { SetProperty(ref _heroOverflowVisibility, value); }
        }

        /// <summary>"29 / 90" - the statement, for which the strip is only the sample.</summary>
        public string HeroCountText
        {
            get { return _heroCountText; }
            private set { SetProperty(ref _heroCountText, value); }
        }

        /// <summary>
        ///     Tooltip of the hero strip: count and breakdown by role. Now hangs on the
        ///     strip and no longer on the game symbol - that, since the strip, is no longer a
        ///     start button but a toggle.
        /// </summary>
        public string HeroStripHint
        {
            get { return _hotsHint; }
            private set { SetProperty(ref _hotsHint, value); }
        }

        /// <summary>
        ///     The entries of the start menu, one per use case.
        ///     <para>
        ///         They belong to the game the row is <b>showing</b>, not to the games the account
        ///         owns. The panel, the tint and this menu therefore always speak about the same
        ///         title - a row standing on Overwatch that offers "Start Heroes of the Storm"
        ///         would start something other than what it shows.
        ///     </para>
        /// </summary>
        public IReadOnlyList<StartOption> StartOptions
        {
            get { return _startOptions; }
            private set { SetProperty(ref _startOptions, value); }
        }

        /// <summary>
        ///     Whether the menu has anything to offer at all.
        ///     <para>
        ///         Since "Open Battle.net" is gone, <c>false</c> really occurs, and it occurs
        ///         often: for Overwatch, WoW and Diablo there is still no path stored, so every
        ///         row showing one of those three has not a single way to start - including the
        ///         rows of accounts that do own HotS. This is a transition and not a final
        ///         state; with the three missing EXE paths, one entry each will return.
        ///     </para>
        /// </summary>
        public bool HasStartOptions
        {
            get { return _hasStartOptions; }
            private set
            {
                if (!SetProperty(ref _hasStartOptions, value)) return;
                OnPropertyChanged(nameof(StartVisibility));
            }
        }

        /// <summary>
        ///     The start button is hidden instead of dimmed when there is nothing to start.
        ///     <para>
        ///         A permanently dead button is noise - it says "something could work here" and
        ///         never reveals what's missing. The column alignment does not suffer from this: the button
        ///         column is <c>Auto</c> wide and sits on the right, so the three small buttons
        ///         do not move, instead the panel on the left gets wider.
        ///     </para>
        /// </summary>
        public Visibility StartVisibility => _hasStartOptions ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        ///     Whether the start menu is expanded. Sits in the ViewModel and not in the XAML, because
        ///     the menu must close when something is selected: <c>StaysOpen="False"</c> reacts
        ///     only to clicks <b>outside</b> the popup. Every command therefore resets the value
        ///     first thing.
        /// </summary>
        public bool StartMenuOpen
        {
            get { return _startMenuOpen; }
            set { SetProperty(ref _startMenuOpen, value); }
        }

        /// <summary>
        ///     Whether the actions menu is expanded - copy email, copy password,
        ///     edit, archive.
        ///     <para>
        ///         Until 21.08.2026 the four stood as individual buttons in the row and
        ///         took up 148 points. They collapsed into one because three of them are rarely
        ///         needed and all four side by side looked like a toolbar;
        ///         the width gained now sits in the hero strip.
        ///     </para>
        ///     <para>
        ///         Same mechanism as <see cref="StartMenuOpen" />: <c>StaysOpen="False"</c>
        ///         reacts only to clicks <b>outside</b>, so each of the four
        ///         commands resets the value first thing.
        ///     </para>
        /// </summary>
        public bool ActionsMenuOpen
        {
            get { return _actionsMenuOpen; }
            set { SetProperty(ref _actionsMenuOpen, value); }
        }

        /// <summary>
        ///     Whether the rank grid on the medal is expanded. Since 23.08.2026 the medal is
        ///     not a picture but a button, and it carries the shortest way to correct a rank
        ///     the reading got wrong: two clicks instead of the five through the dialog.
        ///     <para>
        ///         Same mechanism as <see cref="StartMenuOpen" /> and for the same reason:
        ///         <c>StaysOpen="False"</c> reacts only to clicks <b>outside</b>, so
        ///         <see cref="PickRank" /> resets the value first thing.
        ///     </para>
        ///     <para>
        ///         <b>A <c>ToggleButton</c> is what makes the medal safe to double-click.</b>
        ///         The row opens the edit dialog on a double-click, and the medal is one of the
        ///         two spots that must not: <c>ButtonBase</c> marks <c>MouseLeftButtonDown</c>
        ///         as handled, so the binding on the row never sees the gesture - and the
        ///         second click of an accidental double closes the grid again instead of
        ///         picking whatever medal happens to lie under the pointer.
        ///     </para>
        /// </summary>
        public bool RankMenuOpen
        {
            get { return _rankMenuOpen; }
            set { SetProperty(ref _rankMenuOpen, value); }
        }

        /// <summary>
        ///     The 28 ranks as the grid draws them - the same layout the HotS tab of the edit
        ///     dialog shows, laid out once in <see cref="HotsRankGrid" />.
        ///     <para>
        ///         <b>Which region it writes to is not a question here</b>, unlike everywhere
        ///         the game is read out of a running client: a row IS an account in one
        ///         region, so <c>_row.Region</c> is the answer and there is nothing to ask.
        ///     </para>
        /// </summary>
        public IReadOnlyList<IReadOnlyList<IReadOnlyList<HotsRankChoice>>> RankColumns =>
            HotsRankGrid.Columns(_row?.Hots?.Tier ?? HotsRankTier.None,
                _row?.Hots?.Division ?? 0, PickRankCommand);

        /// <summary>
        ///     "42 of 90 heroes" plus one line per role that is represented. Roles without heroes
        ///     stay off - a list with six zeros says less than three real lines.
        /// </summary>
        /// <summary>
        ///     A read number for the card. <c>null</c> becomes a dash and
        ///     not a 0: "never read yet" and "has nothing" are two statements, and only
        ///     the second one should stand there as a digit.
        /// </summary>
        private static string Amount(int? value)
        {
            return value == null ? "–" : value.Value.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string CurrencyLabel(bool hots, HotsRegionData? data)
        {
            if (!hots) return "";

            var lines = new List<string>
            {
                $"{Strings.Current["currency.gold"]} {Amount(data?.Gold)}",
                $"{Strings.Current["currency.shards"]} {Amount(data?.Shards)}",
                $"{Strings.Current["currency.gems"]} {Amount(data?.Gems)}",
                $"{Strings.Current["currency.level"]} {Amount(data?.AccountLevel)}",
                $"{Strings.Current["currency.chests"]} {Amount(data?.LootChests)}"
            };

            lines.Add(data?.ReadAt == null
                ? Strings.Current["row.neverRead"]
                : Strings.Format("row.readAt", $"{data.ReadAt:yyyy-MM-dd HH:mm}"));

            return string.Join("\n", lines);
        }

        /// <summary>
        ///     Puts the call to action under a tooltip - two blank-line-separated blocks, the
        ///     statement first.
        ///     <para>
        ///         <b>It has to survive an empty text</b>, and that is not a corner case: the
        ///         hero strip says nothing at all when nothing has been read, and that is
        ///         exactly when the click matters most. A bare concatenation would leave a
        ///         leading blank line there.
        ///     </para>
        /// </summary>
        private static string Hinted(string text, string key)
        {
            var hint = Strings.Current[key];
            return text.Length == 0 ? hint : text + "\n\n" + hint;
        }

        private static string HeroLabel(IReadOnlyList<HotsHero> heroes)
        {
            if (heroes.Count == 0) return "";

            var lines = HotsHeroRoles.InDisplayOrder
                .Select(role => new { Role = role, Count = heroes.Count(hero => hero.Role == role) })
                .Where(entry => entry.Count > 0)
                .Select(entry => $"{entry.Role.DisplayName()}: {entry.Count}");

            return Strings.Format("row.heroCount", heroes.Count, HotsHeroCatalog.Count)
                   + "\n" + string.Join("\n", lines);
        }

        /// <summary>
        ///     Builds the entries of the start menu. All share the same command instance
        ///     and differ only in the parameter.
        ///     <para>
        ///         The parameter is "is the row showing Heroes of the Storm", and today that is
        ///         the only case that yields entries at all. The other three get an empty list,
        ///         which hides the button - see <see cref="StartVisibility" />.
        ///     </para>
        ///     <para>
        ///         All four HotS entries have been usable since 20.08.2026; "Open loot chests"
        ///         previously stood there dimmed, because the loot page was calibrated, but the
        ///         flow behind it was still missing.
        ///     </para>
        /// </summary>
        private IReadOnlyList<StartOption> BuildStartOptions(bool hots)
        {
            var command = RunStartOptionCommand;
            var options = new List<StartOption>();

            if (hots)
            {
                // The order is not just taste: a click on the game symbol takes
                // the FIRST entry. That's why the case you want most often stands on top.
                options.Add(new StartOption(HotsIcon, Strings.Current["start.play"],
                    Strings.Current["start.playHint"], ModeStart, true, command));

                options.Add(new StartOption(HotsIcon, Strings.Current["start.playRead"],
                    Strings.Current["start.playReadHint"], ModePlay, true, command));
                options.Add(new StartOption(HotsIcon, Strings.Current["start.refresh"],
                    Strings.Current["start.refreshHint"], ModeRefresh, true, command));
                options.Add(new StartOption(HotsIcon, Strings.Current["start.chests"],
                    Strings.Current["start.chestsHint"], ModeChests, true, command));
            }

            return options;
        }

        /// <summary>
        ///     The same accent once more, as a gradient across the whole row width. It
        ///     does not repeat the strip, it carries it further: the row gets the
        ///     mood of the game, without a second symbol standing anywhere.
        ///     <para>
        ///         It lies BEHIND the content and not on top of it - a layer on top would
        ///         also tint portraits and medal.
        ///     </para>
        /// </summary>
        public Brush PanelTint
        {
            get { return _panelTint; }
            private set { SetProperty(ref _panelTint, value); }
        }

        /// <summary>The border of the row under the pointer, in the game's accent.</summary>
        public Brush PanelHoverBorder
        {
            get { return _panelHoverBorder; }
            private set { SetProperty(ref _panelHoverBorder, value); }
        }

        /// <summary>
        ///     The separator ring between the overlapping hero portraits. It is a hole and
        ///     therefore carries the color that lies behind it at its spot - since the
        ///     tint this is no longer simply the row's base color, but a value
        ///     that <see cref="GameVisuals" /> derives from the same gradient.
        /// </summary>
        public Brush StripSeparator
        {
            get { return _stripSeparator; }
            private set { SetProperty(ref _stripSeparator, value); }
        }

        public Visibility HotsPanelVisibility
        {
            get { return _hotsPanelVisibility; }
            private set { SetProperty(ref _hotsPanelVisibility, value); }
        }

        /// <summary>
        ///     Visible for Overwatch, WoW and Diablo. For these three there is
        ///     one <c>bool</c> each in <see cref="BattlenetAccount" /> and nothing else - the panel
        ///     says exactly that, instead of showing an empty area that looks like an error.
        /// </summary>
        public Visibility NoDataVisibility
        {
            get { return _noDataVisibility; }
            private set { SetProperty(ref _noDataVisibility, value); }
        }

        public string NoDataTitle
        {
            get { return _noDataTitle; }
            private set { SetProperty(ref _noDataTitle, value); }
        }

        /// <summary>What will stand there one day - named, so that the gap is an intention.</summary>
        public string NoDataHint
        {
            get { return _noDataHint; }
            private set { SetProperty(ref _noDataHint, value); }
        }

        /// <summary>
        ///     Which game the row shows when built.
        ///     <para>
        ///         First choice is the game filter of the filter bar (<see cref="GameFocus.Current" />):
        ///         whoever filters on Overwatch wants to see Overwatch numbers and not switch again
        ///         in every row. If this account doesn't have the game, it falls back to HotS
        ///         and then to the first one it does have - exactly as before the exclusive
        ///         filter. With a filter set, the fallback doesn't occur at all, because then only
        ///         accounts with this game get through; without a filter it is the normal case.
        ///     </para>
        ///     <para>
        ///         It is the ONLY place that decides this. Until 20.08.2026 every
        ///         row carried four clickable tabs and was allowed to deviate from the filter; they
        ///         fell away because they offered a second way to the same choice and for that
        ///         cost 146 points of width. That now sits in the rank medal and the
        ///         hero circles.
        ///     </para>
        /// </summary>
        private static string? PreferredGame(IReadOnlyList<string> games)
        {
            if (GameFocus.Current != null && games.Contains(GameFocus.Current))
                return GameFocus.Current;

            return games.Contains(GameVisuals.Hots) ? GameVisuals.Hots : games.FirstOrDefault();
        }

        /// <summary>
        ///     Switches the panel to a game - or to none at all.
        ///     <para>
        ///         <b>The start menu switches with it</b>, and that is the whole reason it is built
        ///         here and not in the <see cref="Row" /> setter. There it hung on
        ///         "does this account play HotS in this region", which is a different question from
        ///         "which game is this row showing": an account with Overwatch <i>and</i> HotS
        ///         offered the four HotS entries while the row stood on Overwatch.
        ///     </para>
        /// </summary>
        private void SelectGame(string? game)
        {
            var hots = game == GameVisuals.Hots;

            // Not a computed property: otherwise every switch would have to raise the
            // notification by hand.
            StartOptions = BuildStartOptions(hots);
            HasStartOptions = StartOptions.Count > 0;

            HotsPanelVisibility = hots ? Visibility.Visible : Visibility.Collapsed;
            NoDataVisibility = hots || game == null ? Visibility.Collapsed : Visibility.Visible;
            PanelTint = GameVisuals.TintFor(game);
            PanelHoverBorder = GameVisuals.HoverBorderFor(game);
            StripSeparator = GameVisuals.StripSeparatorFor(game);
            NoDataTitle = game == null
                ? ""
                : Strings.Format("row.noData", GameVisuals.LabelFor(game));
            NoDataHint = game switch
            {
                GameVisuals.Overwatch => Strings.Current["row.noDataOverwatch"],
                GameVisuals.Wow => Strings.Current["row.noDataWow"],
                GameVisuals.Diablo => Strings.Current["row.noDataDiablo"],
                _ => ""
            };
        }

        /// <summary>
        ///     Which games this account has at all, in the order from
        ///     <see cref="GameVisuals.InDisplayOrder" />. The list is only still needed by
        ///     <see cref="PreferredGame" /> - exactly one of it is shown.
        /// </summary>
        private static IReadOnlyList<string> AvailableGames(BattlenetAccount account,
            BattlenetRegion region)
        {
            // The games of THIS region, not of the account. Otherwise the panel of a game
            // could be preselected that is not played here at all - the row would then show
            // an American rank the account has never had.
            return GameVisuals.InDisplayOrder.Where(game => account.PlaysIn(game, region)).ToList();
        }

        /// <summary>
        ///     The portraits of the strip. <see cref="HeroChip" /> comes from the hero picker
        ///     and is used the same way by the filter bar and the account dialog - ONE
        ///     record for all three stacks. A dedicated version for the card stood here
        ///     briefly and shadowed the existing one; the compiler reported it, otherwise it
        ///     would have become exactly the duplication that the derivation rules in this repo
        ///     warn against.
        /// </summary>
        private static IReadOnlyList<HeroChip> BuildHeroChips(IReadOnlyList<HotsHero> heroes)
        {
            return heroes.Take(HeroChipLimit).Select(HeroChip.For).ToList();
        }

        public string ImageSource
        {
            get { return _imageSource; }
            set
            {
                _imageSource = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     Hangs on every entry of the start menu; the parameter says which
        ///     use case is meant. Replaces the former right-click on the HotS symbol:
        ///     that did the same thing as "Refresh data", but was written down nowhere.
        /// </summary>
        public ICommand RunStartOptionCommand
        {
            get { return _runStartOptionCommand ??= new RelayCommand<string>(RunStartOption); }
        }

        /// <summary>Puts the email on the clipboard.</summary>
        public ICommand CopyUsernameCommand
        {
            get { return _copyUsernameCommand ??= new RelayCommand(CopyUsername); }
        }

        /// <summary>
        ///     Puts the password on the clipboard. The app does type it in itself for Heroes of the
        ///     Storm, but for every other game and for the Battle.net website
        ///     there is otherwise no way to get at the stored password.
        /// </summary>
        public ICommand CopyPasswordCommand
        {
            get { return _copyPasswordCommand ??= new RelayCommand(CopyPassword); }
        }

        /// <summary>
        ///     Archives this account or brings it back - the fourth button of the row.
        ///     <para>
        ///         It is deliberately not called "delete" and doesn't delete anything either. The credentials
        ///         are the actual value of this app; a mis-click in a list with 27
        ///         identical-looking rows must not be the last step. Whoever really
        ///         wants to delete finds the entry again in the archive and can clean up
        ///         <c>data.yaml</c> by hand.
        ///     </para>
        /// </summary>
        public ICommand ArchiveCommand
        {
            get { return _archiveCommand ??= new RelayCommand(ToggleArchive); }
        }

        /// <summary>
        ///     Label of the archive entry in the actions menu. Two words, because next to it
        ///     stand three other entries that also only have two; the reasoning is carried by
        ///     <see cref="ArchiveHint" /> as a tooltip.
        /// </summary>
        public string ArchiveLabel =>
            Strings.Current[Account is { Inactive: true } ? "row.restore" : "row.archive"];

        /// <summary>Tooltip of the archive entry - it alone carries the direction of the gesture.</summary>
        public string ArchiveHint => Strings.Current[Account is { Inactive: true }
            ? "row.restoreHint"
            : "row.archiveHint"];

        /// <summary>Arrow into the box: archive.</summary>
        public Visibility ArchiveDownVisibility =>
            Account is { Inactive: true } ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Arrow out of the box: bring back. Only visible in the archive.</summary>
        public Visibility ArchiveUpVisibility =>
            Account is { Inactive: true } ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>A medal in the grid was clicked - see <see cref="PickRank" />.</summary>
        public ICommand PickRankCommand
        {
            get { return _pickRankCommand ??= new RelayCommand<HotsRankChoice>(PickRank); }
        }

        /// <summary>The hero strip was clicked - see <see cref="EditHeroes" />.</summary>
        public ICommand EditHeroesCommand
        {
            get { return _editHeroesCommand ??= new RelayCommand(EditHeroes); }
        }

        public ICommand OpenSettingsCommand
        {
            get
            {
                if (_openSettingsCommand == null)
                {
                    _openSettingsCommand = new RelayCommand(
                        OpenSettings,
                        CanOpenSettings
                    );
                }

                return _openSettingsCommand;
            }
        }

        /// <summary>
        ///     An entry from the start menu. Closes the menu first thing - after that the
        ///     flow can run for minutes, and an expanded menu over the card would be in the
        ///     way the whole time.
        /// </summary>
        private async void RunStartOption(string? mode)
        {
            StartMenuOpen = false;

            var account = Account;
            if (account == null) return;

            switch (mode)
            {
                case ModeStart:
                    await StartHots(account, SessionPlan.JustPlay);
                    break;
                case ModePlay:
                    await StartHots(account, SessionPlan.PlayAndRead);
                    break;
                case ModeRefresh:
                    await StartHots(account, SessionPlan.RefreshOnly);
                    break;
                case ModeChests:
                    await StartHots(account, SessionPlan.Chests);
                    break;
            }
        }

        /// <summary>
        ///     Starts Heroes of the Storm and signs the account in - without Battle.net and without
        ///     a dedicated Windows user. What happens afterwards stands in <see cref="SessionPlan" />.
        ///     <para>
        ///         If the game stays open, the session is deliberately NOT disposed:
        ///         <see cref="GameSession.Dispose" /> ends the game.
        ///     </para>
        ///     <para>
        ///         The flow runs over <c>Task.Run</c>: it waits for windows and screens and
        ///         types with pauses between keystrokes. On the UI thread the
        ///         application would stand still for minutes in the meantime.
        ///     </para>
        /// </summary>
        private async Task StartHots(BattlenetAccount account, SessionPlan plan)
        {
            // ONE RUN AT A TIME, and the flag is the same one the header chip takes. Two runs
            // clicking into the same client take turns bringing the window to the front, and
            // every click then lands on whatever screen the other one just opened. Until the
            // chip existed this took two rows clicked within a minute; now it takes one row
            // and one chip, and the chip is visible exactly while a run is going on.
            if (!RunningGame.Instance.TryBegin())
            {
                Dialogs.Toast.ShowWarning(Strings.Current["problem.runBusy"]);
                return;
            }

            Dialogs.Toast.ShowInformation(plan switch
            {
                { OpenChests: true } => $"Opening chests for {account.Battletag()}",
                { Read: false } => $"Starting Heroes of the Storm for {account.Battletag()}",
                { CloseAfterwards: false } =>
                    $"Starting Heroes of the Storm for {account.Battletag()} and refreshing data",
                _ => $"Refreshing data for {account.Battletag()}"
            });

            try
            {
                await RunSession(account, plan);
            }
            catch (Exception e)
            {
                // The messages from GameSession are written for humans (wrong
                // window size, screen didn't come up, game not found) - therefore show them
                // directly instead of wrapping them in a generic phrase.
                Log.Error(e, "{Battletag}: start failed", account.Battletag());
                Dialogs.Toast.ShowError(e.Message);
            }
            finally
            {
                RunningGame.Instance.End();
            }
        }

        /// <summary>
        ///     Toggles the archive flag. The row disappears from the view afterwards -
        ///     that is the feedback, and that's why there is no toast for it.
        /// </summary>
        private void ToggleArchive()
        {
            ActionsMenuOpen = false;
            if (Account is not { } account) return;
            _battlenetAccountGateway.SetArchived(account, !account.Inactive);
        }

        private void CopyUsername()
        {
            ActionsMenuOpen = false;
            CopyToClipboard(Account?.Email, "E-mail");
        }

        private void CopyPassword()
        {
            ActionsMenuOpen = false;
            CopyToClipboard(Account?.Password, "Password");
        }

        /// <summary>
        ///     Puts a value on the clipboard.
        ///     <para>
        ///         Two cases that the earlier version was missing, and both are not an exception
        ///         but a piece of information: an empty value - <c>Clipboard.SetText</c> throws
        ///         then - and a clipboard currently held by another process. Without
        ///         catching, both end up as raw exception text in the error toast.
        ///     </para>
        /// </summary>
        private void CopyToClipboard(string? value, string label)
        {
            var account = Account;
            if (account == null) return;

            if (string.IsNullOrEmpty(value))
            {
                Dialogs.Toast.ShowWarning(Strings.Format("toast.copyEmpty", label));
                return;
            }

            try
            {
                Clipboard.SetText(value);
                _battlenetAccountGateway.UpdateInteraction(account);
                Dialogs.Toast.ShowInformation(Strings.Format("toast.copied", label));
            }
            catch (Exception e)
            {
                // Deliberately without the value in the log - this is also the password path.
                Log.Error(e, "{Battletag}: clipboard not reachable", account.Battletag());
                Dialogs.Toast.ShowError(Strings.Current["toast.clipboardBusy"]);
            }
        }

        private bool CanOpenSettings()
        {
            return true;
        }

        private void OpenSettings()
        {
            ActionsMenuOpen = false;
            ShowDialog();
        }

        /// <summary>
        ///     Writes a rank picked from the grid on the medal straight into the region of
        ///     this row.
        ///     <para>
        ///         <b><c>ReadAt</c> stays untouched</b>, and that is the point of the whole
        ///         method: a correction by hand is not a reading, and the timestamp under the
        ///         name would otherwise claim the game had been asked. <c>PlacementsPending</c>
        ///         stays too - carrying a rank from last season and still owing the placement
        ///         matches is a real state, and picking a medal does not end it.
        ///     </para>
        ///     <para>
        ///         The write goes the way the read-out goes: mutate the region record, then
        ///         <c>AddOrUpdate</c>, which saves and rebuilds the rows.
        ///     </para>
        /// </summary>
        private void PickRank(HotsRankChoice? choice)
        {
            RankMenuOpen = false;
            if (choice == null || _row == null) return;

            var wanted = choice.Tier;
            var division = wanted.HasDivisions() ? choice.Division : 0;
            var current = _row.Hots;

            // Nothing to write - either the region already says this, or there is no record
            // yet and "no rank" is exactly what an absent one already means. Without this
            // check HotsFor below would create an empty entry that data.yaml then carries
            // forever, just because somebody clicked the medal that was already lit.
            if ((current?.Tier ?? HotsRankTier.None) == wanted &&
                (current?.Division ?? 0) == division) return;

            var account = _row.Account;

            // HotsFor and not HotsIn: this is a write, so the record has to come into being
            // if this region has never been read.
            var data = account.HotsFor(_row.Region);
            data.Tier = wanted;
            data.Division = division;

            _battlenetAccountGateway.UpdateInteraction(account);

            // Medal, tooltip and opacity hang on the setter; the highlight in the grid does
            // not, it is a computed property without a notification of its own.
            Row = _row;
            OnPropertyChanged(nameof(RankColumns));
        }

        /// <summary>
        ///     The hero strip was clicked. Opens the edit dialog on the HotS tab, where the
        ///     picker already sits embedded.
        ///     <para>
        ///         <b>Deliberately not a quick pick of its own.</b> Ninety heroes are not a
        ///         popup, and the list is the value this application MEASURES - see
        ///         <see cref="HotsReadout" />. A comfortable way to type it by hand would
        ///         invite it to drift away from what the collection actually holds. The rank
        ///         is one value out of 28 and gets the short way; the heroes get the wide
        ///         surface.
        ///     </para>
        /// </summary>
        private void EditHeroes()
        {
            ShowDialog(true);
        }

        /// <summary>
        ///     Starts, signs in and reads - the shared flow behind all four gestures.
        ///     <para>
        ///         Without <see cref="SessionPlan.Read" /> it's over after signing in. Nothing is
        ///         saved either then and <c>HotsReadAt</c> is not set: it wasn't
        ///         read after all, and a timestamp without a measurement would be worse than none - it
        ///         would report an empty hero list as "read, owns nothing".
        ///     </para>
        ///     <para>
        ///         Signing in and reading are handled separately: if reading fails,
        ///         the start still succeeded and the game is running. A shared
        ///         error message would make you believe you couldn't play.
        ///     </para>
        ///     <para>
        ///         <b>What is read stands in <see cref="HotsReadout" /></b> and no longer here.
        ///         The header chip does the same read-out on a client that is already running, and
        ///         a second copy of it would be the place where the two drift apart.
        ///     </para>
        ///     <para>
        ///         All reading runs in the background. Capturing and clicking are blocking
        ///         calls, and the collection needs over a minute - on the UI thread
        ///         the app would stand still that long. That's why the read steps only collect
        ///         their messages; they are shown here, after returning.
        ///     </para>
        /// </summary>
        private async Task RunSession(BattlenetAccount account, SessionPlan plan)
        {
            var progress = new Progress<string>(step =>
                Log.Information("{Battletag}: {Step}", account.Battletag(), step));

            // The UI fetches the path AND the region and hands them in: Backend/Automation doesn't
            // know the gateways, and that direction is meant to stay that way.
            //
            // THE REGION IS THAT OF THE ROW, no longer a preselection on the account. This way
            // the same battletag signs in via the Europe row in Europe and via the
            // Americas row in Americas - and whatever is read afterwards ends up in the game state of
            // exactly this region.
            var region = _row!.Region;
            var gamePath = SettingsGateway.Instance.HotsPath;
            var session = await Task.Run(() =>
                GameSession.StartAndLogin(account, gamePath, region, progress));
            _battlenetAccountGateway.UpdateInteraction(account);
            Dialogs.Toast.ShowInformation(Strings.Format("toast.signedIn", account.Battletag()));

            // Pure playing ends here. The session is deliberately not disposed - that
            // would end the game, and the human wants to get into it right now.
            if (!plan.Read) return;

            var changes = new List<string>();
            var problems = new List<string>();

            // HotsFor and not HotsIn: this is a write, so the record must
            // come into being if it doesn't exist yet. It stays justified even if
            // every single read step fails - ReadAt below stamps the attempt.
            var data = account.HotsFor(region);

            try
            {
                // The read-out itself is shared with the header chip and therefore no longer
                // stands here - see HotsReadout. null for the profile means "read it here":
                // this way in the account is known, and its battletag is the cross-check.
                await Task.Run(() => HotsReadout.ReadAll(session, account, data, null,
                    plan.OpenChests, progress, changes, problems));

                data.ReadAt = DateTime.Now;
                _battlenetAccountGateway.AddOrUpdate(account);


                // Resets the row state - medal, tooltip, hero count and opacity
                // depend on it. Must happen on the UI thread, hence here.
                // Setting the same pair once more is enough: the setter recalculates everything,
                // and the values underneath have just changed.
                Row = _row;

                foreach (var problem in problems) Dialogs.Toast.ShowWarning(problem);
                Dialogs.Toast.ShowInformation(changes.Count == 0
                    ? Strings.Format("toast.nothingChanged", account.Battletag())
                    : $"{account.Battletag()}: {string.Join(", ", changes)}");

                // DONE MARKER: if the game stays open, the client would otherwise stand on some
                // screen of the collection, and whoever comes back to the machine can't tell whether the
                // app is done or still paging through. On ARAM it is done - and you can
                // press "Ready" right away.
                //
                // The condition is DERIVED and not a fifth switch on SessionPlan: what is meant
                // is exactly the case "read and left open", i.e. PlayAndRead. Where it is
                // closed afterwards, nobody would see the marker anyway.
                if (!plan.CloseAfterwards) await Task.Run(() => PlayScreen.ShowAramAsync(session));
            }
            finally
            {
                if (plan.CloseAfterwards) session.Dispose();
            }
        }

        /// <summary>
        ///     Opens the edit dialog for this row.
        ///     <para>
        ///         <b>It hands over the region of the row</b>, and that is not decoration:
        ///         without it the dialog opens on the first HotS region of the account, so
        ///         editing from an Americas row landed on the Europe tab and the ranks
        ///         standing there looked wrong.
        ///     </para>
        ///     <para>
        ///         The <c>Func</c> that used to be the parameter is gone. It had exactly one
        ///         caller and one implementation, and it stood in the way of the two arguments
        ///         that now matter.
        ///     </para>
        /// </summary>
        private void ShowDialog(bool hotsTab = false)
        {
            var dialogViewModel = new AddOrEditAccountViewModel(Account!, _row?.Region, hotsTab);

            bool? success;
            using (Dialogs.Backdrop())
            {
                success = Dialogs.DialogService.ShowDialog(this, dialogViewModel);
            }

            dialogViewModel.Execute(success);
        }
    }
}