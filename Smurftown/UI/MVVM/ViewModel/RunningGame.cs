using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Smurftown.Backend.Automation;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Texts;
using Smurftown.UI.MVVM.View;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ToastNotifications.Messages;

namespace Smurftown.UI.MVVM.ViewModel
{
    /// <summary>
    ///     The chip in the header: a Heroes of the Storm client is running - read the account
    ///     that is signed into it, without signing it out and without closing it.
    ///     <para>
    ///         <b>This is the other direction of the same feature.</b> The account row starts
    ///         from the app: the human picks a row, the app starts the game and signs that
    ///         account in. Here the game came first, the human is already playing, and the app
    ///         only asks who that is. Whoever wants to read three accounts uses the rows; whoever
    ///         has just finished a match uses this.
    ///     </para>
    ///     <para>
    ///         <b>It signs nobody out and it closes nothing.</b> That is the whole promise of the
    ///         chip, and it is why <c>GameSession.AttachToRunning</c> exists next to
    ///         <c>StartAndLogin</c>. The session that comes back from it is deliberately never
    ///         disposed - <c>Dispose</c> kills the game process, and there is a human in it.
    ///     </para>
    ///     <para>
    ///         <b>One object, like <see cref="UpdateOffer" />, and for the same reason</b>: the
    ///         polling has to run once and not once per view, and the busy flag has to be the
    ///         same one the account rows take. Two of them would be two answers to "is a run in
    ///         progress".
    ///     </para>
    /// </summary>
    class RunningGame : ObservableObject
    {
        /// <summary>
        ///     How often the process list is asked. Three seconds, and the number is a
        ///     compromise rather than a measurement: the chip should be there by the time
        ///     somebody has alt-tabbed back to the app, and the question costs a process
        ///     enumeration - cheap, but not free, and it runs for as long as the app is open.
        ///     <para>
        ///         It asks <c>GameWindow.IsRunning</c> and deliberately nothing more. No capture
        ///         and no <c>BringToFront</c>: a poll that steals the focus every three seconds
        ///         would take the machine away from whoever is playing on it.
        ///     </para>
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

        public static readonly RunningGame Instance = new();

        private readonly DispatcherTimer _timer;
        private INotifyPropertyChanged? _dialogOwner;
        private RelayCommand? _openMenuCommand;
        private RelayCommand? _closeMenuCommand;
        private RelayCommand? _refreshCommand;
        private RelayCommand? _chestsCommand;
        private bool _busy;
        private bool _menuOpen;
        private bool _polling;
        private bool _running;

        private RunningGame()
        {
            _timer = new DispatcherTimer { Interval = PollInterval };
            _timer.Tick += (_, _) => Poll();

            // Same construction as in MainViewModel: after a language switch the chip has to
            // reread its words, and an empty name makes WPF reread everything at once.
            Strings.Changed += () => OnPropertyChanged(string.Empty);
        }

        /// <summary>
        ///     Whether a client is running right now. Nothing else is measured - not which
        ///     account, not which screen. Both of those cost a capture, and the chip must not
        ///     take a capture just to decide whether it is visible.
        /// </summary>
        public bool Running
        {
            get => _running;
            private set
            {
                if (!SetProperty(ref _running, value)) return;
                OnPropertyChanged(nameof(Visibility));
                NotifyCommands();

                // The client went away with the menu open - somebody closed the game while
                // deciding. The menu hangs under a chip that is no longer there, so it goes
                // with it.
                if (!value) MenuOpen = false;
            }
        }

        /// <summary>
        ///     Whether a game run is in progress - <b>any</b> run, this one or one an account
        ///     row started.
        ///     <para>
        ///         <b>It is one flag for both paths on purpose.</b> Two runs clicking into the
        ///         same client at the same time is the worst thing this app can do: they take
        ///         turns bringing the window to the front, and every click lands on whatever
        ///         screen the other one has just opened. Until now that hole was only theoretical
        ///         - it took two account rows clicked within a minute. The chip makes it easy,
        ///         because it is visible exactly while a row-started run is going on.
        ///     </para>
        ///     <para>
        ///         <b>Not thread-safe, and it does not need to be.</b> Both paths are started
        ///         from a command and continue on the UI thread after every <c>await</c>; there
        ///         is no second thread that could get between the check and the set.
        ///     </para>
        /// </summary>
        public bool Busy
        {
            get => _busy;
            private set
            {
                if (!SetProperty(ref _busy, value)) return;
                // No OnPropertyChanged(nameof(GameName)) here - unlike Label before it,
                // GameName never changes at runtime, so nothing needs to re-read it. The
                // busy/idle distinction now lives entirely in IsEnabled - see NotifyCommands.
                NotifyCommands();
            }
        }

        /// <summary>Hidden while no client is running - there is nothing to offer then.</summary>
        public Visibility Visibility => Running ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        ///     Whether the menu under the chip is open.
        ///     <para>
        ///         <b>The chip opens a menu since 23.08.2026 and no longer starts the run
        ///         itself.</b> It had exactly one thing to offer - read - and now has two, since
        ///         the loot chests can be opened from a client that is already running. The
        ///         second one could not have been a second chip: the header has room for one,
        ///         and two side by side would be two ways of saying the same sentence.
        ///     </para>
        ///     <para>
        ///         State in the ViewModel and not in the XAML, for the same reason as the two
        ///         menus of an account row: an entry that was picked has to close the menu, and
        ///         both commands below therefore reset this first thing.
        ///     </para>
        /// </summary>
        public bool MenuOpen
        {
            get => _menuOpen;
            set
            {
                if (!SetProperty(ref _menuOpen, value)) return;
                OnPropertyChanged(nameof(MenuVisibility));
            }
        }

        public Visibility MenuVisibility => MenuOpen ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        ///     The chip's own text since 25.08.2026: the game's name, not an instruction to
        ///     click it. "Heroes of the Storm" says what was detected - the more useful fact
        ///     than "Refresh", which the chip used to say regardless of what triggered it. The
        ///     caret in the template is what now says "click me, this opens something"; the two
        ///     questions used to share one word between them.
        ///     <para>
        ///         Hard-coded to Heroes of the Storm rather than derived from whichever game is
        ///         running: this chip exists in the first place because
        ///         <c>Backend/Automation/</c> only knows how to read that one game at all - there
        ///         is no "which game" question here yet, unlike the row's own game panel.
        ///     </para>
        ///     <para>
        ///         <b>Busy no longer changes the text</b> - it used to swap in "Reading...", and
        ///         that wording is gone along with <c>running.busy</c>/<c>running.chip</c>. The
        ///         chip already dims itself while busy: <see cref="OpenMenuCommand" />'s
        ///         <c>CanExecute</c> answers <c>false</c>, WPF sets <c>IsEnabled</c> from that
        ///         automatically, and <c>RunningChipTheme</c>'s own trigger on it is the signal
        ///         now - one fact, one place, instead of a second copy in words.
        ///     </para>
        /// </summary>
        public string GameName => GameVisuals.LabelFor(GameVisuals.Hots);

        public string Hint => Strings.Current["running.hint"];

        public string MenuRefresh => Strings.Current["running.menuRefresh"];

        public string MenuRefreshHint => Strings.Current["running.menuRefreshHint"];

        public string MenuChests => Strings.Current["running.menuChests"];

        public string MenuChestsHint => Strings.Current["running.menuChestsHint"];

        /// <summary>
        ///     The chip itself. It opens the menu and does nothing else - which is why it is
        ///     gated on <see cref="Busy" /> too: a menu whose two entries both refuse would
        ///     be a menu that opens to say no.
        /// </summary>
        public ICommand OpenMenuCommand =>
            _openMenuCommand ??= new RelayCommand(() => MenuOpen = true, () => Running && !Busy);

        /// <summary>The backdrop behind the menu - see the panel in <c>MainWindow.xaml</c>.</summary>
        public ICommand CloseMenuCommand =>
            _closeMenuCommand ??= new RelayCommand(() => MenuOpen = false);

        /// <summary>Read rank, heroes and currencies out of the running client.</summary>
        public ICommand RefreshCommand =>
            _refreshCommand ??= new RelayCommand(() => Refresh(false), () => Running && !Busy);

        /// <summary>
        ///     Open every unopened loot chest first, then read.
        ///     <para>
        ///         <b>The order is the point</b>, and it is the same one the account rows use
        ///         with <c>SessionPlan.Chests</c>: a chest drops shards, gold and occasionally a
        ///         hero. Read first, and what lands in <c>data.yaml</c> is the state from before
        ///         the opening - wrong from the first chest onwards.
        ///     </para>
        /// </summary>
        public ICommand RefreshWithChestsCommand =>
            _chestsCommand ??= new RelayCommand(() => Refresh(true), () => Running && !Busy);

        /// <summary>
        ///     Starts the polling, and takes the owner for the region dialog along with it.
        ///     <para>
        ///         <b>Both in one call, because both come from the same place.</b> MvvmDialogs
        ///         resolves the owner window from a ViewModel whose view carries
        ///         <c>md:DialogServiceViews.IsRegistered</c>, and this object is nobody's
        ///         DataContext - it hangs in the header of the main window. So the main window's
        ///         ViewModel hands itself in, and it is the one that starts the polling anyway.
        ///     </para>
        /// </summary>
        public void Watch(INotifyPropertyChanged dialogOwner)
        {
            _dialogOwner = dialogOwner;
            Poll();
            _timer.Start();
        }

        /// <summary>
        ///     Claims the client for one run. <c>false</c> means somebody else already has it.
        ///     <para>
        ///         Public because the account rows take it too - see <see cref="Busy" />.
        ///     </para>
        /// </summary>
        public bool TryBegin()
        {
            if (Busy) return false;
            Busy = true;
            return true;
        }

        public void End()
        {
            Busy = false;
        }

        /// <summary>
        ///     Asks the process list, off the UI thread.
        ///     <para>
        ///         <c>Process.GetProcessesByName</c> is cheap but not instantaneous, and it runs
        ///         every three seconds for as long as the app is open. The guard against
        ///         overlapping polls is not caution but the consequence: a tick that arrives
        ///         while the previous answer is still outstanding would ask twice.
        ///     </para>
        /// </summary>
        private async void Poll()
        {
            if (_polling) return;
            _polling = true;
            try
            {
                Running = await Task.Run(GameWindow.IsRunning);
            }
            catch (Exception e)
            {
                // A failing poll must not take the app with it. It says "no client" for this
                // round and asks again in three seconds.
                Log.Warning(e, "Could not check for a running game client");
                Running = false;
            }
            finally
            {
                _polling = false;
            }
        }

        /// <summary>
        ///     Opens the run guide, which does everything else.
        ///     <para>
        ///         <b>The whole flow used to stand here</b> and moved into
        ///         <see cref="View.RunGuideViewModel" /> on 23.08.2026, because it turned out to
        ///         need a human in the middle of it. A Heroes of the Storm client in exclusive
        ///         full screen takes clicks only while it holds the foreground, and nothing this
        ///         application can call puts it there - <c>SetForegroundWindow</c> restores the
        ///         window without restoring the display, measured with three methods. What works
        ///         is a person pressing Alt+Tab, and a flow that has to ask for that needs a
        ///         window to ask in.
        ///     </para>
        ///     <para>
        ///         <b>The busy gate stays here</b>, because it is shared with the account rows -
        ///         see <see cref="Busy" />. <c>ShowDialog</c> blocks until the guide is closed,
        ///         so the claim covers exactly the run and not a moment longer.
        ///     </para>
        /// </summary>
        /// <param name="openChests">
        ///     Whether every unopened loot chest is emptied before the reading - the difference
        ///     between the two entries of the menu, and the only one.
        /// </param>
        private void Refresh(bool openChests)
        {
            // FIRST, before anything can go wrong below: StaysOpen has no meaning for this
            // menu - it is an overlay and not a popup - so nothing closes it except the
            // command that was picked and the backdrop.
            MenuOpen = false;

            if (!TryBegin())
            {
                Dialogs.Toast.ShowWarning(Strings.Current["problem.runBusy"]);
                return;
            }

            try
            {
                using (Dialogs.Backdrop())
                {
                    Dialogs.DialogService.ShowDialog(_dialogOwner!, new RunGuideViewModel(openChests));
                }
            }
            finally
            {
                End();
            }
        }

        /// <summary>
        ///     All four commands hang on the same two flags, so they are told together. A list
        ///     of individual names is incomplete by the next addition - the same reasoning as
        ///     behind the empty name in <c>OnPropertyChanged</c>.
        /// </summary>
        private void NotifyCommands()
        {
            _openMenuCommand?.NotifyCanExecuteChanged();
            _refreshCommand?.NotifyCanExecuteChanged();
            _chestsCommand?.NotifyCanExecuteChanged();
        }
    }
}
