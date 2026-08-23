using MvvmDialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Effects;
using ToastNotifications;
using ToastNotifications.Lifetime;
using ToastNotifications.Position;

namespace Smurftown.UI.MVVM
{
    class Dialogs
    {
        /// <summary>
        ///     How far the main window is dimmed behind a modal. Together with the 24 points of
        ///     <c>DialogBounds.Padding</c> this makes the strip around the dialog a visible
        ///     frame rather than an invisible one.
        /// </summary>
        private const double DimmedOpacity = 0.4;

        /// <summary>
        ///     Blur radius behind a modal. Dimming alone left the window readable, and a reader
        ///     whose eye is caught by a row underneath is looking past the dialog that is asking
        ///     them something.
        /// </summary>
        private const double BlurRadius = 8;

        /// <summary>
        ///     Pushes the main window behind a modal - dimmed and blurred - for as long as the
        ///     returned object lives.
        ///     <para>
        ///         <b>A scope and not two lines per call site</b>, and that is the whole point:
        ///         the same pair of lines stood at four places, two of them restored with
        ///         <c>Opacity = 100</c> instead of <c>1.0</c>, one modal had no treatment at
        ///         all, and none of them was in a <c>finally</c>. An exception inside a dialog
        ///         therefore left the window dimmed for the rest of the session, with nothing
        ///         to click that would bring it back.
        ///     </para>
        ///     <para>
        ///         It restores what it found rather than a fixed 1.0, so a modal opened from a
        ///         modal hands back the state of the one underneath.
        ///     </para>
        /// </summary>
        public static IDisposable Backdrop()
        {
            return new BackdropScope();
        }

        private sealed class BackdropScope : IDisposable
        {
            private readonly Effect? _effect;
            private readonly Window? _main;
            private readonly double _opacity;

            internal BackdropScope()
            {
                _main = Application.Current?.MainWindow;

                // No main window is not an error case - it is a test, or a dialog that is
                // itself the first window. There is then nothing to push back.
                if (_main == null) return;

                _opacity = _main.Opacity;
                _effect = _main.Effect;

                _main.Opacity = DimmedOpacity;
                _main.Effect = new BlurEffect
                {
                    Radius = BlurRadius,
                    // Rendering is what the eye sees here, not a screenshot to be measured -
                    // and the cheaper box blur shows its square edges at this radius.
                    KernelType = KernelType.Gaussian
                };
            }

            public void Dispose()
            {
                if (_main == null) return;

                _main.Opacity = _opacity;
                _main.Effect = _effect;
            }
        }

        public static readonly IDialogService DialogService = new DialogService();
        public static readonly Notifier Toast = new Notifier(cfg =>
        {
            cfg.PositionProvider = new WindowPositionProvider(
                parentWindow: Application.Current.MainWindow,
                corner: Corner.BottomRight,
                offsetX: 10,
                offsetY: 10);

            cfg.LifetimeSupervisor = new TimeAndCountBasedLifetimeSupervisor(
                notificationLifetime: TimeSpan.FromSeconds(10),
                maximumNotificationCount: MaximumNotificationCount.FromCount(5));

            cfg.Dispatcher = Application.Current.Dispatcher;
        });
    }
}
