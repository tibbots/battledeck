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
        private RelayCommand? _refreshCommand;
        private bool _busy;
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
                _refreshCommand?.NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(Label));
                _refreshCommand?.NotifyCanExecuteChanged();
            }
        }

        /// <summary>Hidden while no client is running - there is nothing to offer then.</summary>
        public Visibility Visibility => Running ? Visibility.Visible : Visibility.Collapsed;

        public string Label => Strings.Current[Busy ? "running.busy" : "running.chip"];

        public string Hint => Strings.Current["running.hint"];

        public ICommand RefreshCommand =>
            _refreshCommand ??= new RelayCommand(Refresh, () => Running && !Busy);

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
        private void Refresh()
        {
            if (!TryBegin())
            {
                Dialogs.Toast.ShowWarning(Strings.Current["problem.runBusy"]);
                return;
            }

            try
            {
                Dialogs.DialogService.ShowDialog(_dialogOwner!, new RunGuideViewModel());
            }
            finally
            {
                End();
            }
        }
    }
}
