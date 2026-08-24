using Smurftown.UI.MVVM.ViewModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Smurftown
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            StartRunningChipPulse();
        }

        /// <summary>
        ///     Starts the running-game chip's breathing glow and live dot - once, for the
        ///     lifetime of the window. Runs from code and not from an
        ///     <c>EventTrigger</c>/<c>Storyboard</c> in <c>RunningChipTheme</c>'s
        ///     <c>ControlTemplate</c>, because that was tried first and failed
        ///     <c>XamlLoadsTests</c>: <c>Application.LoadComponent</c> parses and validates a
        ///     template's triggers without ever instantiating the template on a live control, so
        ///     a <c>Storyboard.TargetName</c> pointed at a name that only exists once the
        ///     template is actually applied throws "Name not found in the namescope of
        ///     ControlTemplate" before the window is ever shown.
        ///     <para>
        ///         <b><c>ApplyTemplate</c> first, deliberately</b> - <c>Template.FindName</c>
        ///         only finds a named part once the template has actually built its visual tree,
        ///         and nothing before this call has forced that to happen yet.
        ///     </para>
        ///     <para>
        ///         <b>Runs regardless of <c>Running.Visibility</c></b>, the same reasoning the
        ///         abandoned XAML version already carried: an infinite animation on a
        ///         <c>Collapsed</c> element costs nothing to keep ticking, and starting it here
        ///         means the glow is already mid-pulse the instant a client is detected, instead
        ///         of restarting from a dead stop on every game that comes up.
        ///     </para>
        /// </summary>
        private void StartRunningChipPulse()
        {
            RunningChip.ApplyTemplate();

            if (RunningChip.Template.FindName("chip", RunningChip) is not Border chip) return;
            if (RunningChip.Template.FindName("dot", RunningChip) is not Ellipse dot) return;
            if (chip.Effect is not DropShadowEffect sharedGlow) return;

            // CLONED, not animated directly: a Freezable set inside a Style's ControlTemplate
            // is frozen once the Style is used, so every Button sharing RunningChipTheme can
            // share the one Effect instance cheaply. BeginAnimation on a frozen object throws
            // "sealed or frozen" - measured here, XamlLoadsTests is what caught it. The clone
            // is unfrozen and belongs to this one Border alone, which is exactly what a
            // per-instance animation needs.
            var glow = (DropShadowEffect)sharedGlow.Clone();
            chip.Effect = glow;

            var beat = TimeSpan.FromSeconds(1.3);

            var glowOpacity = new DoubleAnimation(0.3, 0.8, beat)
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            var glowBlur = new DoubleAnimation(6, 16, beat)
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            var dotOpacity = new DoubleAnimation(1.0, 0.45, beat)
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };

            glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowOpacity);
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowBlur);
            dot.BeginAnimation(OpacityProperty, dotOpacity);
        }

        private void DockPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}