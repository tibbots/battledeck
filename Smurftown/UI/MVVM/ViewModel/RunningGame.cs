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

        private static readonly BattlenetAccountGateway _gateway = BattlenetAccountGateway.Instance;

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
        ///     The whole flow behind the chip: attach, find out who is signed in, settle the
        ///     region, read, write.
        ///     <para>
        ///         <b>The identification comes first and it costs nothing extra.</b> The profile
        ///         overlay carries the battletag and the rank in the same capture, so the reading
        ///         that answers "who is this" is the same one that supplies the values - and it
        ///         is handed on to <see cref="HotsReadout" /> instead of being taken a second
        ///         time.
        ///     </para>
        ///     <para>
        ///         <b>Three exits write nothing</b>, and all three are silent about the data
        ///         rather than clever: an unreadable tag, a tag no account carries, and a
        ///         cancelled region question. A run that cannot say whose numbers these are must
        ///         not put them anywhere.
        ///     </para>
        ///     <para>
        ///         <b>The session is never disposed here.</b> Not in the success case, not in the
        ///         error case, not in <c>finally</c>. Every one of those would kill the client
        ///         the human is playing in - which is precisely what this path promises not to
        ///         do.
        ///     </para>
        /// </summary>
        private async void Refresh()
        {
            if (!TryBegin())
            {
                Dialogs.Toast.ShowWarning(Strings.Current["problem.runBusy"]);
                return;
            }

            try
            {
                var progress = new Progress<string>(step =>
                    Log.Information("Running client: {Step}", step));

                Dialogs.Toast.ShowInformation(Strings.Current["toast.readingRunning"]);
                var session = await Task.Run(() => GameSession.AttachToRunning(progress));

                // expected: null - nobody said who should be standing there. This reading IS
                // the identification; see ProfileReader.ReadAsync.
                var reading = await Task.Run(() => ProfileReader.ReadAsync(session, null));
                Log.Information("Running client: {Note}", reading.Note);

                var seen = reading.SeenBattletag;
                if (!BattlenetAccount.TrySplitBattletag(seen, out _, out _))
                {
                    Dialogs.Toast.ShowWarning(Strings.Format("problem.runNotATag", seen ?? ""));
                    return;
                }

                var account = _gateway.FindByBattletag(seen);
                if (account == null)
                {
                    Log.Warning("Running client is signed in as {Battletag} - no account carries it", seen);
                    Dialogs.Toast.ShowWarning(Strings.Format("problem.runUnknown", seen));
                    return;
                }

                var changes = new List<string>();
                var problems = new List<string>();

                var region = ResolveRegion(account, changes);
                if (region == null)
                {
                    Log.Information("{Battletag}: region question cancelled - nothing written",
                        account.Battletag());
                    return;
                }

                // HotsFor and not HotsIn: this is a write, so the record has to come into
                // being if it does not exist yet.
                var data = account.HotsFor(region.Value);

                // Matches = true: the identity is settled - it is what found this account in
                // the first place. ApplyProfile refuses anything else, and that refusal is the
                // last floor before data.yaml.
                await Task.Run(() => HotsReadout.ReadAll(session, account, data,
                    reading with { Matches = true }, false, progress, changes, problems));

                data.ReadAt = DateTime.Now;

                // Saves the file and rebuilds the rows in one go, so the card picks the new
                // values up. That the account was read counts as an interaction with it -
                // the list sorts by that.
                _gateway.UpdateInteraction(account);

                foreach (var problem in problems) Dialogs.Toast.ShowWarning(problem);
                Dialogs.Toast.ShowInformation(changes.Count == 0
                    ? Strings.Format("toast.nothingChanged", account.Battletag())
                    : $"{account.Battletag()}: {string.Join(", ", changes)}");

                // Done marker, same as with "Play and refresh data": the client would otherwise
                // stand on some collection screen, and whoever looks over cannot tell whether
                // the app has finished or is still paging. On ARAM it has finished - and the
                // human can hit "Ready" right away. If it fails, nothing aborts.
                await Task.Run(() => PlayScreen.ShowAramAsync(session));
            }
            catch (Exception e)
            {
                // The messages from GameSession are written for humans (minimised window, wrong
                // screen, nobody signed in) - so show them directly instead of wrapping them in
                // a generic phrase.
                Log.Error(e, "Refreshing from the running client failed");
                Dialogs.Toast.ShowError(e.Message);
            }
            finally
            {
                End();
            }
        }

        /// <summary>
        ///     Which region the client is signed into.
        ///     <para>
        ///         <b>The game does not say.</b> Rank, heroes and currencies are stored per
        ///         region, the client is signed into exactly one - and on none of the calibrated
        ///         screens does it stand which one. Searched for on 22.08.2026: neither the main
        ///         menu nor the profile overlay shows it. So it is derived where it can be
        ///         derived, and asked where it cannot.
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <b>One region</b> on the account - that one, no question. This is the
        ///             normal case and the reason the dialog is not simply always shown.
        ///         </item>
        ///         <item>
        ///             <b>Several</b> - ask, offering exactly those. The human knows which one
        ///             they logged into; the app does not.
        ///         </item>
        ///         <item>
        ///             <b>None</b> - ask, offering all three, and <b>add</b> the pick to the
        ///             account's Heroes of the Storm regions. Without that the write would be
        ///             invisible: the overview builds one row per played region, so data in a
        ///             region nobody plays has no row to appear in.
        ///         </item>
        ///     </list>
        ///     <para>
        ///         <b>It is asked before the reading, not after.</b> The collection alone takes
        ///         over a minute, and a question at the end of it would be a minute spent before
        ///         finding out that nobody was there to answer.
        ///     </para>
        ///     <para>
        ///         <c>null</c> means cancelled, and cancelled writes nothing.
        ///     </para>
        /// </summary>
        private BattlenetRegion? ResolveRegion(BattlenetAccount account, List<string> changes)
        {
            var played = account.RegionsFor(Games.Hots);
            if (played.Count == 1) return played[0];

            var offered = played.Count > 0 ? played : BattlenetRegions.InDisplayOrder;
            var picker = new RegionPickerViewModel(account.Battletag(), offered);

            // THE GAME IS IN FRONT AT THIS POINT - the profile was just read off it - and a
            // dialog behind a full-screen client is a dialog nobody answers. So the main
            // window is brought up first.
            //
            // THE PRICE IS NAMED RATHER THAN HIDDEN: a client in EXCLUSIVE full screen
            // minimises itself when it loses the focus, and a client minimised out of full
            // screen does not come back from outside (measured with three methods, see
            // GameWindow.BringToFront). Then the reading afterwards fails - but it fails with
            // the sentence that says exactly that, because every capture checks the window
            // size. Borderless full screen and windowed mode, which is what this app is
            // calibrated against, are unaffected.
            //
            // This is also why the question is only asked when it has to be: an account with
            // one region never gets here, and that is the normal case.
            Application.Current.MainWindow!.Activate();

            // Dimmed like the account dialog, and put back afterwards even if the dialog
            // throws - a main window left at 0.4 opacity would look broken for the rest of
            // the session.
            Application.Current.MainWindow!.Opacity = 0.4;
            try
            {
                Dialogs.DialogService.ShowDialog(_dialogOwner!, picker);
            }
            finally
            {
                Application.Current.MainWindow!.Opacity = 100;
            }

            if (picker.DialogResult != true || picker.Picked is not { } picked) return null;

            if (played.Contains(picked)) return picked;

            account.SetRegions(Games.Hots, played.Append(picked));
            changes.Add(Strings.Format("change.regionAdded", picked.DisplayName()));
            Log.Information("{Battletag}: Heroes of the Storm region {Region} added - " +
                            "it was read there", account.Battletag(), picked);
            return picked;
        }
    }
}
