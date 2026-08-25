using System.Threading;
using System.Windows.Threading;

namespace Battledeck.Tests
{
    /// <summary>
    ///     Runs an action on a thread that behaves like the application's UI thread - single
    ///     apartment, and with a dispatcher actually pumping - and hands back whatever
    ///     exception escaped it.
    ///     <para>
    ///         <b>The apartment alone is not enough, and that cost a crashed test host.</b>
    ///         Loading a view does not only build controls; it starts what those views set
    ///         going. <c>RunningGame</c> polls for the game client, and its poll continues
    ///         after an <c>await</c>. Without a <see cref="SynchronizationContext" /> there is
    ///         nothing for that continuation to return to, so it resumes on a thread-pool
    ///         thread - and the first line that touches a <c>Button</c> hits
    ///         <c>Dispatcher.VerifyAccess</c> and throws. As an <c>async void</c> path that
    ///         exception is rethrown on the pool, where nobody catches it, and the whole test
    ///         host dies with it.
    ///     </para>
    ///     <para>
    ///         So this is not a workaround: the objects under test expect a UI thread, and
    ///         giving them a real one is what makes the test resemble the application. The
    ///         shutdown is queued at <see cref="DispatcherPriority.ApplicationIdle" />, which
    ///         means "after everything already queued" - continuations posted by the loading
    ///         still get their turn on the thread that owns the objects.
    ///     </para>
    ///     <para>
    ///         There are packages that provide an <c>[StaFact]</c> attribute. None of them
    ///         would have solved the part above.
    ///     </para>
    /// </summary>
    internal static class Sta
    {
        /// <summary>How long the pump may run before the test is declared hung.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        internal static void Run(Action action)
        {
            Exception? escaped = null;

            var thread = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(dispatcher));

                // Anything that still blows up on this thread becomes a failing test rather
                // than a dead test host. A crash says nothing; a failure names the exception.
                dispatcher.UnhandledException += (_, e) =>
                {
                    escaped ??= e.Exception;
                    e.Handled = true;
                    dispatcher.InvokeShutdown();
                };

                dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        escaped ??= e;
                    }
                });

                dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                    new Action(dispatcher.InvokeShutdown));

                Dispatcher.Run();
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            if (!thread.Join(Patience))
            {
                throw new TimeoutException(
                    $"The STA thread was still pumping after {Patience.TotalSeconds:0} seconds. "
                    + "Something the loaded views started keeps the dispatcher busy.");
            }

            if (escaped != null) throw escaped;
        }
    }
}
