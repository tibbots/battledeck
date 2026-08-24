using CommunityToolkit.Mvvm.Input;
using Serilog;
using Smurftown.Backend.Automation;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Texts;
using System.Windows;
using System.Windows.Input;

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

    /// <summary>
    ///     The window that walks a human through refreshing from a client that is already
    ///     running - and that exists because of one measured fact: <b>the client has to hold the
    ///     foreground, and only the human can put it there</b>. The frame it runs in -
    ///     <see cref="Steps" />, <see cref="CloseCommand" />, <see cref="Start" />, the step
    ///     bookkeeping - is <see cref="RunGuideBase" />; this class is what is specific to
    ///     refreshing from an account that is not yet known.
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
    ///         screen where a window used to be. That is <see cref="AutoCloseOnSuccess" />
    ///         answering <c>false</c> - <see cref="ReuseGuideViewModel" /> is the funnel that
    ///         answers it the other way.
    ///     </para>
    /// </summary>
    public sealed class RunGuideViewModel : RunGuideBase
    {
        private const int StepFront = 0;
        private const int StepAccount = 1;
        private const int StepRegion = 2;
        private const int StepRead = 3;
        private const int StepDone = 4;

        private static readonly BattlenetAccountGateway _gateway = BattlenetAccountGateway.Instance;

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

        private string _regionQuestion = "";
        private Visibility _regionVisibility = Visibility.Collapsed;
        private IReadOnlyList<RegionChoice> _regionChoices = [];
        private TaskCompletionSource<BattlenetRegion?>? _regionAnswer;

        public RunGuideViewModel(bool openChests = false) : base(
        [
            new RunStep(Strings.Current["run.stepFront"], RunStepState.Pending, ""),
            new RunStep(Strings.Current["run.stepAccount"], RunStepState.Pending, ""),
            new RunStep(Strings.Current["run.stepRegion"], RunStepState.Pending, ""),
            new RunStep(Strings.Current["run.stepRead"], RunStepState.Pending, ""),
            new RunStep(Strings.Current["run.stepDone"], RunStepState.Pending, "")
        ])
        {
            _openChests = openChests;
        }

        public override string Explain => Strings.Current["run.explain"];
        public override string Title => Strings.Current["run.title"];
        protected override string LogContext => "Refreshing from the running client";

        /// <summary>See the type doc - the guide never closes on its own, success or not.</summary>
        protected override bool AutoCloseOnSuccess => false;

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

        protected override async Task RunAsync()
        {
            // The progress channel of the run, finally pointed at something that is not the
            // log. It writes into whichever step is active, so the detail line follows the
            // funnel without the backend knowing there is one.
            var progress = new Progress<string>(step =>
            {
                Log.Information("Running client: {Step}", step);
                Detail(Current, step);
            });

            if (!await WaitForClient(StepFront, "run.bringToFront")) return;
            Done(StepFront, "");

            // ------------------------------------------------------------------ the account
            Begin(StepAccount);
            var session = await Task.Run(() => GameSession.AttachToRunning(progress), Cancel.Token);

            // expected: null - nobody said who should be standing there. This reading IS the
            // identification, and the values come along in the same capture; see
            // ProfileReader.ReadAsync.
            var reading = await Task.Run(() => ProfileReader.ReadAsync(session, null, Cancel.Token),
                Cancel.Token);
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
                Cancel.Token);

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
            await Task.Run(() => PlayScreen.ShowAramAsync(session), Cancel.Token);

            Done(StepDone, problems.Count == 0 ? "" : string.Join(" · ", problems));
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

            await using var registration = Cancel.Token.Register(() => _regionAnswer.TrySetResult(null));

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
    }
}
