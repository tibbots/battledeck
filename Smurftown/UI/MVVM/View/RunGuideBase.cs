using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MvvmDialogs;
using Serilog;
using Smurftown.Backend.Automation;
using Smurftown.Backend.Texts;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Smurftown.UI.MVVM.View
{
    /// <summary>
    ///     What <see cref="RunGuideViewModel" /> and <see cref="ReuseGuideViewModel" /> share: a
    ///     funnel of steps behind a full-screen client, a human gate at the front of it - see
    ///     <see cref="GameWindow.WaitForForeground" /> for why that gate exists at all - and one
    ///     button that means Cancel while the run goes and Close once it is over.
    ///     <para>
    ///         Extracted on 24.08.2026 when the second funnel needed the identical bookkeeping.
    ///         <see cref="RunStep" /> and <c>RunStepsTemplate.xaml</c> are the other two pieces
    ///         that moved out of <see cref="RunGuideViewModel" /> at the same time - a second copy
    ///         of any of the three is exactly the place the two funnels would have drifted apart.
    ///     </para>
    ///     <para>
    ///         <b>What is deliberately NOT here</b>: reading a profile, asking a region, calling
    ///         <c>GameSession</c> at all. Those differ between the two funnels, which is why each
    ///         keeps its own <see cref="RunAsync" /> - only the frame around it is shared.
    ///     </para>
    /// </summary>
    public abstract class RunGuideBase : ObservableObject, IModalDialogViewModel
    {
        /// <summary>The run's own cancellation - shared with <see cref="WaitForClient" /> and every step.</summary>
        protected readonly CancellationTokenSource Cancel = new();

        private RelayCommand? _closeCommand;
        private bool? _dialogResult;
        private bool _failed;
        private bool _finished;
        private int _current;

        protected RunGuideBase(IEnumerable<RunStep> steps)
        {
            Steps = new ObservableCollection<RunStep>(steps);
        }

        public ObservableCollection<RunStep> Steps { get; }

        /// <summary>
        ///     The step a progress message written right now would land on - what
        ///     <see cref="RunGuideViewModel" /> forwards <c>IProgress&lt;string&gt;</c> callbacks
        ///     through via <c>Detail(Current, step)</c>, so the backend does not have to know the
        ///     funnel exists.
        /// </summary>
        protected int Current => _current;

        /// <summary>The one sentence that says why a human is needed at all.</summary>
        public abstract string Explain { get; }

        public abstract string Title { get; }

        /// <summary>
        ///     English, for the log only - see <c>Strings.ForLog</c>. <see cref="Title" /> is
        ///     translated and must never land in <c>smurftown.log</c>.
        /// </summary>
        protected abstract string LogContext { get; }

        /// <summary>
        ///     Whether a run that finishes without a failed step closes the window itself.
        ///     <para>
        ///         <see cref="RunGuideViewModel" /> answers <c>false</c>: "nothing closes itself,
        ///         whoever comes back wants to read what happened". <see cref="ReuseGuideViewModel" />
        ///         answers <c>true</c>: a run that only switched the account, and maybe read
        ///         afterwards, has nothing left worth a click to dismiss - the human wanted to get
        ///         back into the game, not close a window first.
        ///     </para>
        ///     <para>
        ///         A failed run never auto-closes, regardless of this flag - the message is the
        ///         reason to keep the window up, not a reason to close it faster.
        ///     </para>
        /// </summary>
        protected abstract bool AutoCloseOnSuccess { get; }

        public ICommand CloseCommand => _closeCommand ??= new RelayCommand(Close);

        /// <summary>
        ///     Cancel while it runs, close when it is over - one button, because there is never
        ///     a moment where both would make sense.
        /// </summary>
        public string CloseLabel => Strings.Current[_finished ? "run.close" : "run.cancel"];

        public bool? DialogResult
        {
            get => _dialogResult;
            protected set => SetProperty(ref _dialogResult, value);
        }

        /// <summary>The actual run. Each funnel is a different sequence of <c>Begin</c>/<c>Done</c>/<c>Fail</c>.</summary>
        protected abstract Task RunAsync();

        /// <summary>
        ///     Starts the run. Called from the view's <c>Loaded</c> and not from the constructor:
        ///     the first thing every funnel does is wait on the human, and there has to be a
        ///     window on screen telling them so.
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
                // The messages GameSession and friends throw are written for humans (wrong
                // screen, nobody signed in, window never usable) - so they are shown as they
                // are instead of being wrapped in a generic phrase.
                Log.Error(e, "{Context} failed", LogContext);
                Fail(_current, e.Message);
            }
            finally
            {
                _finished = true;
                OnPropertyChanged(nameof(CloseLabel));

                if (!_failed && AutoCloseOnSuccess) DialogResult = true;
            }
        }

        /// <summary>
        ///     Waits until the client holds the foreground and has its picture - see
        ///     <see cref="GameWindow.WaitForForeground" /> for why both halves are needed and why
        ///     this deliberately does not call <c>BringToFront</c>.
        ///     <para>
        ///         <c>false</c> means it never happened, and the step is already marked failed by
        ///         then. The caller only has to stop.
        ///     </para>
        /// </summary>
        protected async Task<bool> WaitForClient(int step, string hint)
        {
            Begin(step);
            Detail(step, Strings.Current[hint]);

            if (await GameWindow.WaitForForeground(Cancel.Token)) return true;

            if (Cancel.IsCancellationRequested)
            {
                Fail(step, Strings.Current["run.cancelled"]);
                return false;
            }

            Log.Warning("The client never came to the front within {Seconds:n0}s",
                GameWindow.FrontTimeout.TotalSeconds);
            Fail(step, Strings.Format("run.timeout", (int)GameWindow.FrontTimeout.TotalSeconds));
            return false;
        }

        // ------------------------------------------------------------------ step bookkeeping

        protected void Begin(int step)
        {
            _current = step;
            Set(step, RunStepState.Active, Steps[step].Detail);
        }

        protected void Done(int step, string detail)
        {
            Set(step, RunStepState.Done, detail);
        }

        protected void Fail(int step, string detail)
        {
            _failed = true;
            Set(step, RunStepState.Failed, detail);
        }

        protected void Detail(int step, string detail)
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
                Cancel.Cancel();
                return;
            }

            DialogResult = true;
        }
    }
}
