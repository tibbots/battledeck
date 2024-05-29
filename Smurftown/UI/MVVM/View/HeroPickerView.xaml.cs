using System.Windows.Controls;
using System.Windows.Input;

namespace Smurftown.UI.MVVM.View;

/// <summary>
///     The selection surface of the hero picker, without window chrome. All behavior sits in
///     <see cref="HeroPickerViewModel" /> - there is only the one thing here that the binding
///     cannot do: pass a mouse wheel event past its own scroll area upward.
///     <para>
///         It has two hosts: <see cref="HeroPicker" /> (a window) and the HotS tab of the
///         account dialog. What to watch out for there stands at the head of the XAML file.
///     </para>
/// </summary>
public partial class HeroPickerView : UserControl
{
    public HeroPickerView()
    {
        InitializeComponent();
    }

    /// <summary>
    ///     Passes the mouse wheel through to the host's scroll area, as long as this surface is
    ///     embedded.
    ///     <para>
    ///         <b>Why it's needed:</b> a <c>ScrollViewer</c> processes the mouse wheel even
    ///         when it cannot scroll at all - <c>OnMouseWheel</c> sets
    ///         <c>Handled = true</c> regardless of whether an offset actually changed.
    ///         <c>VerticalScrollBarVisibility="Disabled"</c> turns off the scrolling, not the
    ///         processing. Embedded, the wheel stayed ineffective over the hero grid,
    ///         while it worked over the region bar and rank block - the bug thus sat exactly at
    ///         the edge between the two and looked like a layout bug.
    ///     </para>
    ///     <para>
    ///         <b>Why the event is re-raised instead of just passed through:</b>
    ///         leaving <c>Handled = false</c> did not help - the <c>ScrollViewer</c> sets it
    ///         itself, as soon as the event reaches it. It is intercepted instead in the
    ///         <b>Preview</b> pass, which tunnels top-down and thus arrives before it;
    ///         the new event starts at <c>this</c> and bubbles upward. The
    ///         inner <c>ScrollViewer</c> lies below that and is thereby out of the way.
    ///     </para>
    ///     <para>
    ///         <b>Why here and not in the host:</b> a <c>PreviewMouseWheel</c> on the outer
    ///         <c>ScrollViewer</c> of the account dialog would have been shorter, but would have
    ///         put the burden on every future host. The surface brings the problem with it, so
    ///         it also brings the solution with it.
    ///     </para>
    ///     <para>
    ///         <b>None of this happens in the window</b> - there the grid is meant to scroll
    ///         itself, and that is exactly what it does. The condition is
    ///         <see cref="HeroPickerViewModel.Embedded" /> and not, say, "cannot scroll": it is
    ///         the same axis that <c>ChromeVisibility</c> and <c>GridScrollBarVisibility</c>
    ///         already hang on.
    ///     </para>
    /// </summary>
    private void GridScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        if (DataContext is not HeroPickerViewModel { Embedded: true }) return;

        e.Handled = true;

        RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = this
        });
    }
}
