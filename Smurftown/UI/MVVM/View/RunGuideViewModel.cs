using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MvvmDialogs;
using Serilog;
using Smurftown.Backend.Automation;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Texts;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Smurftown.UI.MVVM.View
{
    /// <summary>
    ///     One offered region: the word, the two letters, and the pick.
    ///     <para>
    ///         The command sits in the record and is not looked up via <c>RelativeSource</c> -
    ///         the same construction as the start menu of an account row, and for the same
    ///         reason: a field in the record cannot bind into nothing.
    ///     </para>
    /// </summary>
    public sealed record RegionChoice(string Label, string ShortName, BattlenetRegion Region, ICommand Command);

    /// <summary>What a step of the run guide is doing right now.</summary>
    public enum RunStepState
    {
        Pending,
        Active,
        Done,
        Failed
    }

    /// <summary>
    ///     One step of the funnel: what it is called, where it stands, and one line of detail
    ///     underneath.
    ///     <para>
    ///         An immutable record without notification, replaced in the collection rather than
    ///         mutated - the same construction as <c>HotsRankChoice</c> and for the same reason.
    ///         Five records rebuilt a few times a second cost nothing, and a mutable step would
    ///         need its own <c>INotifyPropertyChanged</c> for four properties that are all
    ///         derived from one.
    ///     </para>
    /// </summary>
    public sealed record RunStep(string Label, RunStepState State, string Detail)
    {
        private static readonly Brush PendingBrush = Frozen(0x5A, 0x5E, 0x6C);
        private static readonly Brush FailedBrush = Frozen(0xD9, 0x53, 0x4F);

        /// <summary>A step nobody has reached yet is dimmed - the same language as everywhere else.</summary>
        public double Opacity => State == RunStepState.Pending ? 0.45 : 1.0;

        /// <summary>
        ///     <b>Three different shapes, not one shape in three colours.</b> A ring while
        ///     nothing has happened, a turning arc while it is happening, a check when it is
        ///     done - and only the failure keeps a filled disc, in red. Colour alone would have
        ///     to be read; a shape is recognised across the room, which matters for a window
        ///     that spends most of its life behind a full-screen game.
        ///     <para>
        ///         Blue for both the arc and the check, and that is deliberate: they are the
        ///         same accent the whole application uses for "this is the app talking"
        ///         (<c>#1A73E8</c>, the tabs, the rank highlight, the start button). A separate
        ///         success colour would be a fourth meaning nobody asked for.
        ///     </para>
        ///     <para>
        ///         The shapes are drawn and not typed. The repo learned that with the three dots
        ///         of the actions menu, whose spacing depended on the font that happened to be
        ///         installed.
        ///     </para>
        /// </summary>
        public Visibility RingVisibility =>
            State is RunStepState.Pending or RunStepState.Failed ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>The turning arc - exactly one step wears it, the one being worked on.</summary>
        public Visibility SpinnerVisibility =>
            State == RunStepState.Active ? Visibility.Visible : Visibility.Collapsed;

        public Visibility CheckVisibility =>
            State == RunStepState.Done ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Stroke of the ring: grey while pending, red when it failed.</summary>
        public Brush MarkerBrush => State == RunStepState.Failed ? FailedBrush : PendingBrush;

        /// <summary>Only the failure is filled. A pending step is an outline and nothing more.</summary>
        public Brush MarkerFill => State == RunStepState.Failed ? FailedBrush : Brushes.Transparent;

        public Visibility DetailVisibility =>
            Detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>A failed step says so in red; everything else stays grey.</summary>
        public Brush DetailBrush => State == RunStepState.Failed ? FailedBrush : PendingBrush;

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    ///     The window that walks a human through refreshing from a client that is already
    ///     running - and that exists because of one measured fact: <b>the client has to hold the
    ///     foreground, and only the human can put it there</b>.
    ///     <para>
    ///         <b>Why the app cannot do it itself.</b> Measured over three evenings ending
    ///         23.08.2026: a Heroes of the Storm client in exclusive full screen that loses the
    ///         front keeps rendering through an invisible <c>D3DProxyWindow</c> at full size
    ///         while its INPUT window shrinks to a 160x28 placeholder. Captures still work -
    ///         they only need a rectangle off the screen - but every click at picture
    ///         coordinates goes to where that point sits on the DESKTOP, which is this
    ///         application, which takes the front, which collapses the client again.
    ///         <c>SetForegroundWindow</c> from outside restores the window without restoring the
    ///         display; three methods were tried and all three failed. What worked, first time,
    ///         was a human pressing Alt+Tab.
    ///     </para>
    ///     <para>
    ///         <b>So the wait is the feature, and it waits on the measurement rather than on a
    ///         clock.</b> <c>GameWindow.IsForemostAndPlayable</c> answers "clicks will land"; the
    ///         guide asks it three times a second. An earlier version simply slept six seconds,
    ///         which was a race with the human on one side and no way to tell whether it had been
    ///         won.
    ///     </para>
    ///     <para>
    ///         <b>It shows the progress it already had.</b> The run reports its steps through an
    ///         <c>IProgress&lt;string&gt;</c> whose only subscriber used to be the log - which is
    ///         why those texts are translated and why <c>Strings.ForLog</c> had to exist to keep
    ///         them out of it. Here they finally have the reader they were written for.
    ///     </para>
    ///     <para>
    ///         <b>The region question is a step and not a second dialog.</b> It used to open
    ///         a window of its own in the middle of the run, which meant a second window taking
    ///         the front at the worst possible moment. Now it is asked here, and the step after
    ///         it waits for the client to come back - because answering means alt-tabbing away
    ///         from the game. That window is gone; what survived of it is
    ///         <see cref="RegionChoice" /> above.
    ///     </para>
    ///     <para>
    ///         <b>Nothing closes itself.</b> The guide sits behind a full-screen client for the
    ///         whole run; whoever comes back wants to read what happened, not to find an empty
    ///         screen where a window used to be.
    ///     </para>
    /// </summary>
    public class RunGuideViewModel : ObservableObject, IModalDialogViewModel
    {
        private const int StepFront = 0;
        private const int StepAccount = 1;
        private const int StepRegion = 2;
        private const int StepRead = 3;
        private const int StepDone = 4;

        /// <summary>
        ///     How long the human has to bring the client to the front. Generous on purpose:
        ///     this window sits behind a full-screen game, so somebody who alt-tabbed to the
        ///     wrong place first should not be punished with a restart.
        /// </summary>
        private static readonly TimeSpan FrontTimeout = TimeSpan.FromSeconds(60);

        /// <summary>Three times a second - the answer costs a process enumeration.</summary>
        private static readonly TimeSpan FrontPoll = TimeSpan.FromMilliseconds(300);

        private static readonly BattlenetAccountGateway _gateway = BattlenetAccountGateway.Instance;

        private readonly CancellationTokenSource _cancel = new();

        /// <summary>
        ///     Whether the loot chests are emptied before the reading. Picked in the menu under
        ///     the header chip and carried through unchanged - see
        ///     <see cref="ViewModel.RunningGame.RefreshWithChestsCommand" />.
        ///     <para>
        ///         <b>No step of its own in the funnel</b>, and that is a decision rather than an
        ///         omission: the opening reports through the same <c>IProgress&lt;string&gt;</c>
        ///         as everything else, so it writes into the detail line of "read the values" -
        ///         chest by chest, with the counter it stops on. A sixth step would stand there
        ///         greyed out on every run that opens nothing.
        ///     </para>
        /// </summary>
        private readonly bool _openChests;

        private bool? _dialogResult;
        private bool _finished;
        private int _current = StepFront;
        private string _regionQuestion = "";
        private Visibility _regionVisibility = Visibility.Collapsed;
        private IReadOnlyList<RegionChoice> _regionChoices = [];
        private TaskCompletionSource<BattlenetRegion?>? _regionAnswer;

        public RunGuideViewModel(bool openChests = false)
        {
            _openChests = openChests;

            Steps =
            [
                new RunStep(Strings.Current["run.stepFront"], RunStepState.Pending, ""),
                new RunStep(Strings.Current["run.stepAccount"], RunStepState.Pending, ""),
                new RunStep(Strings.Current["run.stepRegion"], RunStepState.Pending, ""),
                new RunStep(Strings.Current["run.stepRead"], RunStepState.Pending, ""),
                new RunStep(Strings.Current["run.stepDone"], RunStepState.Pending, "")
            ];

            CloseCommand = new RelayCommand(Close);
        }

        public ObservableCollection<RunStep> Steps { get; }

        /// <summary>The one sentence that says why a human is needed at all.</summary>
        public string Explain => Strings.Current["run.explain"];

        public string Title => Strings.Current["run.title"];

        public ICommand CloseCommand { get; }

        /// <summary>
        ///     Cancel while it runs, close when it is over - one button, because there is never
        ///     a moment where both would make sense.
        /// </summary>
        public string CloseLabel => Strings.Current[_finished ? "run.close" : "run.cancel"];

        public string RegionQuestion
        {
            get => _regionQuestion;
            private set => SetProperty(ref _regionQuestion, value);
        }

        public Visibility RegionVisibility
        {
            get => _regionVisibility;
            private set => SetProperty(ref _regionVisibility, value);
        }

        public IReadOnlyList<RegionChoice> RegionChoices
        {
            get => _regionChoices;
            private set => SetProperty(ref _regionChoices, value);
        }

        public bool? DialogResult
        {
            get => _dialogResult;
            private set => SetProperty(ref _dialogResult, value);
        }

        /// <summary>
        ///     Starts the run. Called from the view's <c>Loaded</c> and not from the constructor:
        ///     the first thing it does is wait on the human, and there has to be a window on
        ///     screen telling them so.
        /// </summary>
        public async void Start()
        {
            try
            {
                await RunAsync();
            }
            catch (OperationCanceledException)
            {
                Fail(_current, Strings.Current["run.cancelled"]);
            }
            catch (Exception e)
            {
                // The messages from GameSession are written for humans (wrong screen, nobody
                // signed in, window never usable) - so they are shown as they are instead of
                // being wrapped in a generic phrase.
                Log.Error(e, "Refreshing from the running client failed");
                Fail(_current, e.Message);
            }
            finally
            {
                _finished = true;
                OnPropertyChanged(nameof(CloseLabel));
            }
        }

        private async Task RunAsync()
        {
            // The progress channel of the run, finally pointed at something that is not the
            // log. It writes into whichever step is active, so the detail line follows the
            // funnel without the backend knowing there is one.
            var progress = new Progress<string>(step =>
            {
                Log.Information("Running client: {Step}", step);
                Detail(_current, step);
            });

            if (!await WaitForClient(StepFront, "run.bringToFront")) return;
            Done(StepFront, "");

            // ------------------------------------------------------------------ the account
            Begin(StepAccount);
            var session = await Task.Run(() => GameSession.AttachToRunning(progress), _cancel.Token);

            // expected: null - nobody said who should be standing there. This reading IS the
            // identification, and the values come along in the same capture; see
            // ProfileReader.ReadAsync.
            var reading = await Task.Run(() => ProfileReader.ReadAsync(session, null, _cancel.Token),
                _cancel.Token);
            Log.Information("Running client: {Note}", reading.Note);

            var seen = reading.SeenBattletag;
            if (!BattlenetAccount.TrySplitBattletag(seen, out _, out _))
            {
                Fail(StepAccount, Strings.Format("problem.runNotATag", seen ?? ""));
                return;
            }

            var account = _gateway.FindByBattletag(seen);
            if (account == null)
            {
                Log.Warning("Running client is signed in as {Battletag} - no account carries it", seen);
                Fail(StepAccount, Strings.Format("problem.runUnknown", seen));
                return;
            }

            Done(StepAccount, account.Battletag());

            // ------------------------------------------------------------------- the region
            Begin(StepRegion);
            var changes = new List<string>();
            var region = await ResolveRegion(account, changes);
            if (region == null)
            {
                Log.Information("{Battletag}: region question cancelled - nothing written",
                    account.Battletag());
                Fail(StepRegion, Strings.Current["run.cancelled"]);
                return;
            }

            Done(StepRegion, region.Value.DisplayName());

            // -------------------------------------------------------------------- the values
            Begin(StepRead);

            // ANSWERING MEANT COMING HERE, so the client is behind us again. Without this
            // second wait the first click of the reading would go to the desktop - which is
            // exactly the failure this whole window exists to prevent. For an account with one
            // region nothing was asked and this returns at once.
            if (!await WaitForClient(StepRead, "run.backToFront")) return;

            // HotsFor and not HotsIn: this is a write, so the record has to come into being if
            // it does not exist yet.
            var data = account.HotsFor(region.Value);
            var problems = new List<string>();

            // Matches = true: the identity is settled - it is what found this account in the
            // first place. ApplyProfile refuses anything else, and that refusal is the last
            // floor before data.yaml.
            await Task.Run(() => HotsReadout.ReadAll(session, account, data,
                reading with { Matches = true }, _openChests, progress, changes, problems),
                _cancel.Token);

            data.ReadAt = DateTime.Now;

            // Saves the file and rebuilds the rows in one go, so the row picks the new values
            // up. That the account was read counts as an interaction with it - the list sorts
            // by that.
            _gateway.UpdateInteraction(account);

            Done(StepRead, changes.Count == 0
                ? Strings.Format("toast.nothingChanged", account.Battletag())
                : string.Join(", ", changes));

            // ---------------------------------------------------------------- the done marker
            Begin(StepDone);

            // Same as with "Play and refresh data": the client would otherwise stand on some
            // collection screen, and whoever looks over cannot tell whether the app has
            // finished or is still paging. On ARAM it has finished - and the human can hit
            // "Ready" right away.
            await Task.Run(() => PlayScreen.ShowAramAsync(session), _cancel.Token);

            Done(StepDone, problems.Count == 0 ? "" : string.Join(" · ", problems));
        }

        /// <summary>
        ///     Waits until the client holds the foreground and has its picture - see
        ///     <see cref="GameWindow.IsForemostAndPlayable" /> for why both halves are needed.
        ///     <para>
        ///         <c>false</c> means it never happened, and the step is already marked failed by
        ///         then. The caller only has to stop.
        ///     </para>
        /// </summary>
        private async Task<bool> WaitForClient(int step, string hint)
        {
            Begin(step);
            Detail(step, Strings.Current[hint]);

            var deadline = DateTime.UtcNow + FrontTimeout;

            while (DateTime.UtcNow < deadline)
            {
                if (_cancel.IsCancellationRequested)
                {
                    Fail(step, Strings.Current["run.cancelled"]);
                    return false;
                }

                if (await Task.Run(GameWindow.IsForemostAndPlayable)) return true;

                await Task.Delay(FrontPoll);
            }

            Log.Warning("The client never came to the front within {Seconds:n0}s",
                FrontTimeout.TotalSeconds);
            Fail(step, Strings.Format("run.timeout", (int)FrontTimeout.TotalSeconds));
            return false;
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
        ///             normal case and the reason nothing is asked most of the time.
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
        ///         <b>Asked here and not in a window of its own.</b> A second dialog would take
        ///         the front in the middle of the run, and taking the front is the one thing this
        ///         flow cannot survive.
        ///     </para>
        ///     <para>
        ///         <c>null</c> means cancelled, and cancelled writes nothing.
        ///     </para>
        /// </summary>
        private async Task<BattlenetRegion?> ResolveRegion(BattlenetAccount account, List<string> changes)
        {
            var played = account.RegionsFor(Games.Hots);
            if (played.Count == 1) return played[0];

            var offered = played.Count > 0 ? played : BattlenetRegions.InDisplayOrder;
            var picked = await AskRegion(account, offered);
            if (picked == null) return null;

            if (played.Contains(picked.Value)) return picked;

            account.SetRegions(Games.Hots, played.Append(picked.Value));
            changes.Add(Strings.Format("change.regionAdded", picked.Value.DisplayName()));
            Log.Information("{Battletag}: Heroes of the Storm region {Region} added - " +
                            "it was read there", account.Battletag(), picked.Value);
            return picked;
        }

        /// <summary>
        ///     Shows the region buttons and waits for a click on one of them.
        ///     <para>
        ///         A <see cref="TaskCompletionSource{TResult}" /> and not a nested dialog: the
        ///         run is one <c>async</c> method from the first wait to the done marker, and a
        ///         blocking <c>ShowDialog</c> in the middle of it would be a second message loop
        ///         inside the one this window already runs in.
        ///     </para>
        ///     <para>
        ///         Cancelling the whole guide answers the question with <c>null</c> - otherwise
        ///         the button would do nothing while this is open, which is the one moment
        ///         somebody is most likely to press it.
        ///     </para>
        /// </summary>
        private async Task<BattlenetRegion?> AskRegion(BattlenetAccount account,
            IReadOnlyList<BattlenetRegion> offered)
        {
            RegionQuestion = Strings.Format("region.pickQuestion", account.Battletag());

            var pick = new RelayCommand<BattlenetRegion>(region => _regionAnswer?.TrySetResult(region));
            RegionChoices = offered
                .Select(region => new RegionChoice(
                    region.DisplayName(), region.ShortName(), region, pick))
                .ToList();

            _regionAnswer = new TaskCompletionSource<BattlenetRegion?>();
            RegionVisibility = Visibility.Visible;
            Detail(StepRegion, Strings.Current["run.regionWaiting"]);

            await using var registration = _cancel.Token.Register(() => _regionAnswer.TrySetResult(null));

            try
            {
                return await _regionAnswer.Task;
            }
            finally
            {
                RegionVisibility = Visibility.Collapsed;
                _regionAnswer = null;
            }
        }

        // ------------------------------------------------------------------ step bookkeeping

        private void Begin(int step)
        {
            _current = step;
            Set(step, RunStepState.Active, Steps[step].Detail);
        }

        private void Done(int step, string detail)
        {
            Set(step, RunStepState.Done, detail);
        }

        private void Fail(int step, string detail)
        {
            Set(step, RunStepState.Failed, detail);
        }

        private void Detail(int step, string detail)
        {
            Set(step, Steps[step].State, detail);
        }

        /// <summary>
        ///     Replaces a step instead of mutating it - that is what makes the record work
        ///     without notification of its own. Assigning into an
        ///     <see cref="ObservableCollection{T}" /> raises <c>Replace</c>, and the
        ///     <c>ItemsControl</c> rebuilds exactly that row.
        /// </summary>
        private void Set(int step, RunStepState state, string detail)
        {
            Steps[step] = Steps[step] with { State = state, Detail = detail };
        }

        private void Close()
        {
            // Cancel first, close second. While the run is going, the button is "Cancel" and
            // the window has to stay until the flow has noticed - a window torn out from under
            // a run in progress would leave the client on whatever screen it was paging
            // through, with nothing on screen saying so.
            if (!_finished)
            {
                _cancel.Cancel();
                return;
            }

            DialogResult = true;
        }
    }
}
