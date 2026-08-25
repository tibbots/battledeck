using System.Windows;

namespace Battledeck.UI.MVVM
{
    /// <summary>
    ///     Clamps a modal to the size of the main window, minus a margin per side.
    ///     No dialog after that sticks out beyond the window it stands behind.
    ///     <para>
    ///         <b>Why at all:</b> the main window measures 1340x800, the account dialog stood
    ///         at 1000x920 - so 120 points taller than what it covers. A modal that
    ///         sticks out above and below looks like a second program and not like a
    ///         dialog to the one underneath.
    ///     </para>
    ///     <para>
    ///         <b>Why the margin:</b> a dialog that fills the window exactly covers it
    ///         completely - the connection between the two is lost. While a dialog is
    ///         open, the main window is dimmed and blurred anyway (see
    ///         <c>Dialogs.Backdrop</c>); the 24 points make it into a visible
    ///         frame instead of an invisible one.
    ///     </para>
    ///     <para>
    ///         <b>Why a class and not three number pairs in the XAML:</b> the rule reads
    ///         "at most as big as the main window" and not "at most 1292x752". As a
    ///         number in three XAML files it would have survived until someone touches
    ///         the main window - and would then be silently wrong afterwards, because a
    ///         too-large window reports nothing. The declared sizes in the three XAML
    ///         files still stand at the clamped values, so the designer shows the same
    ///         as runtime; this call is the guard, not the normal case.
    ///     </para>
    ///     <para>
    ///         <b>Called in the constructor</b> of the modal, right after
    ///         <c>InitializeComponent()</c>. That is the only safe point in time: MvvmDialogs
    ///         sets <c>Owner</c> only afterwards, and <c>WindowStartupLocation="CenterOwner"</c>
    ///         computes the position from the size - whoever clamps later centers on the old
    ///         one. That is why <see cref="Application.MainWindow" /> is used here and not
    ///         <c>dialog.Owner</c>: the main window is already there at this point, the owner
    ///         is not.
    ///     </para>
    /// </summary>
    internal static class DialogBounds
    {
        /// <summary>
        ///     Margin per side, in points. That leaves 1292x752 of the main window for a modal.
        ///     <para>
        ///         24 and not 40: the value is lost to the content, and doubled at that - top and
        ///         bottom. A visible strip over an area dimmed to 0.4 does not need
        ///         40 points; 24 is enough, and 32 more points of dialog height are worth more than
        ///         a wider frame.
        ///     </para>
        ///     <para>
        ///         Until 22.08.2026 a sharper justification stood here: 40 would not have left the
        ///         hero grid in the HotS tab of the account dialog a single complete
        ///         row of circles. It no longer applies - since the tab scrolls as a whole,
        ///         no height in it is tight any more.
        ///     </para>
        /// </summary>
        public const double Padding = 24;

        /// <summary>
        ///     Sets <c>MaxWidth</c>/<c>MaxHeight</c> of the dialog to the main window minus
        ///     <see cref="Padding" /> per side and pulls an already-set size down with it.
        ///     If the dialog fits in anyway, the call changes nothing.
        /// </summary>
        public static void FitToMainWindow(Window dialog)
        {
            var main = Application.Current?.MainWindow;

            // Without a main window there is nothing to clamp against - and a dialog that
            // finds itself as the main window would shrink to its own size minus
            // margin. Neither is an error case, just nothing to do.
            if (main == null || ReferenceEquals(main, dialog)) return;

            var available = new Size(
                Measure(main.ActualWidth, main.Width) - 2 * Padding,
                Measure(main.ActualHeight, main.Height) - 2 * Padding);

            if (double.IsNaN(available.Width) || available.Width <= 0) return;
            if (double.IsNaN(available.Height) || available.Height <= 0) return;

            dialog.MaxWidth = available.Width;
            dialog.MaxHeight = available.Height;

            // MaxWidth/MaxHeight alone would already draw the window smaller, but Width
            // and Height would keep reporting the old value - and that is exactly what CenterOwner reads.
            if (dialog.Width > available.Width) dialog.Width = available.Width;
            if (dialog.Height > available.Height) dialog.Height = available.Height;
        }

        /// <summary>
        ///     The measured dimension, otherwise the set one. <c>ActualWidth</c> is 0 as long as a
        ///     window has not yet been shown - that case does not occur here, because the
        ///     main window has long since been standing, but a 0 would be a negative remainder and thus an
        ///     invisible dialog.
        /// </summary>
        private static double Measure(double actual, double declared)
        {
            return actual > 0 ? actual : declared;
        }
    }
}
