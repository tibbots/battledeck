using Serilog;
using Smurftown.Backend.Automation;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Texts;
using Smurftown.UI.MVVM.ViewModel;

namespace Smurftown.UI.MVVM.View
{
    /// <summary>
    ///     The window that walks a human through switching a client that is already running to
    ///     a different account - <see cref="GameSession.StartAndLogin" />'s reuse branch, driven
    ///     from behind the same human gate as <see cref="RunGuideViewModel" />, and for the
    ///     identical measured reason: <b>the client has to hold the foreground, and only a human
    ///     can put it there</b>.
    ///     <para>
    ///         <b>Why this exists next to <see cref="RunGuideViewModel" /> instead of inside it.</b>
    ///         Until 24.08.2026 the account row's "Play"/"Play and read"/"Refresh"/"Open chests"
    ///         entries called <c>GameSession.StartAndLogin</c> straight from a background
    ///         <c>Task.Run</c>, with no window on screen. When the row reused a running client,
    ///         that meant <c>GameSession.TakeOver</c> fighting <c>BringToFront</c> against a
    ///         client that had lost the display - the exact bug the header chip already carries
    ///         the fix for, just not applied here: a client flickering up and down while nothing
    ///         happened. The frame - <see cref="RunGuideBase" />, <see cref="RunStep" />, the
    ///         funnel template - is shared with the chip's guide; what runs inside it differs
    ///         enough (a known account instead of an unread profile, no region question, a
    ///         session that is sometimes closed afterwards) that a second small funnel reads
    ///         clearer than a mode flag threaded through the first one.
    ///     </para>
    ///     <para>
    ///         <b>It closes itself on success, and that is the one difference to the chip's
    ///         guide.</b> See <see cref="AutoCloseOnSuccess" />: whoever switched an account
    ///         wanted to get back into the game, not read a report and click Close first. A
    ///         failed run still stays open - the message is the reason to keep the window, not a
    ///         reason to close it faster.
    ///     </para>
    /// </summary>
    public sealed class ReuseGuideViewModel : RunGuideBase
    {
        private const int StepFront = 0;
        private const int StepSwitch = 1;

        /// <summary>Only reached when the constants below are meaningful indices - see the two flags.</summary>
        private const int StepRead = 2;

        private const int StepDone = 3;

        private static readonly BattlenetAccountGateway _gateway = BattlenetAccountGateway.Instance;

        private readonly BattlenetAccount _account;
        private readonly BattlenetRegion _region;
        private readonly SessionPlan _plan;

        /// <summary>Whether <see cref="StepRead" /> exists at all - false for <see cref="SessionPlan.JustPlay" />.</summary>
        private readonly bool _hasRead;

        /// <summary>
        ///     Whether <see cref="StepDone" /> exists - only when the plan both reads and leaves
        ///     the game open afterwards. Reading and then closing leaves nobody at the machine to
        ///     see an ARAM screen, so <see cref="SessionPlan.RefreshOnly" /> and
        ///     <see cref="SessionPlan.Chests" /> never get this step.
        /// </summary>
        private readonly bool _hasDone;

        // INTERNAL, NOT PUBLIC: SessionPlan itself is internal to Smurftown.UI.MVVM.ViewModel,
        // and a public constructor may not expose a less visible type in its signature (CS0051).
        // The only caller is AccountCardViewModel, in the same assembly either way.
        internal ReuseGuideViewModel(BattlenetAccount account, BattlenetRegion region, SessionPlan plan)
            : base(BuildSteps(plan))
        {
            _account = account;
            _region = region;
            _plan = plan;
            _hasRead = plan.Read;
            _hasDone = plan.Read && !plan.CloseAfterwards;
        }

        /// <summary>
        ///     Two, three or four steps depending on the plan - the exact conditions
        ///     <see cref="ViewModel.AccountCardViewModel" /> used to branch on inline before this
        ///     window existed. <see cref="StepRead" /> and <see cref="StepDone" /> are fixed
        ///     positions rather than computed ones: when a step is missing, its constant is
        ///     simply never used as an index, guarded by <see cref="_hasRead" />/<see cref="_hasDone" />.
        /// </summary>
        private static List<RunStep> BuildSteps(SessionPlan plan)
        {
            var steps = new List<RunStep>
            {
                new(Strings.Current["run.stepFront"], RunStepState.Pending, ""),
                new(Strings.Current["reuse.stepSwitch"], RunStepState.Pending, "")
            };

            if (!plan.Read) return steps;
            steps.Add(new RunStep(Strings.Current["run.stepRead"], RunStepState.Pending, ""));

            if (plan.CloseAfterwards) return steps;
            steps.Add(new RunStep(Strings.Current["run.stepDone"], RunStepState.Pending, ""));

            return steps;
        }

        public override string Explain => Strings.Format("reuse.explain", _account.Battletag());
        public override string Title => Strings.Current["reuse.title"];
        protected override string LogContext => "Switching the running client's account";

        /// <summary>See the type doc - a successful switch has nothing left worth a click.</summary>
        protected override bool AutoCloseOnSuccess => true;

        protected override async Task RunAsync()
        {
            // The progress channel of the run. StartAndLogin reports both the sign-out of
            // whoever is there now and the sign-in of the wanted account through the same
            // stream - both belong to "switching the account", so both land on StepSwitch via
            // Current without a second channel to keep in step with the funnel.
            var progress = new Progress<string>(step =>
            {
                Log.Information("{Battletag}: {Step}", _account.Battletag(), step);
                Detail(Current, step);
            });

            if (!await WaitForClient(StepFront, "run.bringToFront")) return;
            Done(StepFront, "");

            // ------------------------------------------------------------------ the switch
            Begin(StepSwitch);
            var gamePath = SettingsGateway.Instance.HotsPath;
            var session = await Task.Run(
                () => GameSession.StartAndLogin(_account, gamePath, _region, progress, Cancel.Token),
                Cancel.Token);

            _gateway.UpdateInteraction(_account);
            Done(StepSwitch, Strings.Format("toast.signedIn", _account.Battletag()));

            // Pure playing ends here, same as RunSession without a step of its own for it: the
            // session is deliberately not disposed - that would end the game, and the human
            // wants to get into it right now.
            if (!_hasRead) return;

            // -------------------------------------------------------------------- the values
            Begin(StepRead);

            // HotsFor and not HotsIn: this is a write, so the record has to come into being if
            // it does not exist yet. It stays justified even if every single read step fails -
            // ReadAt below stamps the attempt.
            var data = _account.HotsFor(_region);
            var changes = new List<string>();
            var problems = new List<string>();

            try
            {
                // null for the profile: nobody read it yet on this path, unlike the chip's
                // guide, where the profile read is what found the account in the first place.
                await Task.Run(() => HotsReadout.ReadAll(session, _account, data, null,
                    _plan.OpenChests, progress, changes, problems), Cancel.Token);

                data.ReadAt = DateTime.Now;
                _gateway.AddOrUpdate(_account);

                var detail = changes.Count == 0
                    ? Strings.Format("toast.nothingChanged", _account.Battletag())
                    : string.Join(", ", changes);

                // No done marker on this plan (RefreshOnly, Chests) - problems join the same
                // line rather than a step nobody would ever see, the same reasoning
                // RunGuideViewModel uses for the chest count: one line doing double duty beats a
                // step that stands there greyed out on every run that never reaches it.
                if (!_hasDone)
                {
                    if (problems.Count > 0) detail += "  ·  " + string.Join(" · ", problems);
                    Done(StepRead, detail);
                    return;
                }

                Done(StepRead, detail);

                // ---------------------------------------------------------- the done marker
                Begin(StepDone);

                // Same as with "Refresh data": the client would otherwise stand on some
                // collection screen, and whoever comes back cannot tell whether the app has
                // finished or is still paging. On ARAM it has finished - and the human can hit
                // "Ready" right away.
                await Task.Run(() => PlayScreen.ShowAramAsync(session), Cancel.Token);

                Done(StepDone, problems.Count == 0 ? "" : string.Join(" · ", problems));
            }
            finally
            {
                // CLOSING IS SILENT, matching RunSession before this window existed: it took no
                // step of its own there either, and killing an already-signed-in client is
                // near-instant next to everything else in the funnel.
                if (_plan.CloseAfterwards) session.Dispose();
            }
        }
    }
}
