using System.Windows;
using System.Windows.Media;

namespace Battledeck.UI.MVVM.View
{
    /// <summary>What a step of a run funnel is doing right now.</summary>
    public enum RunStepState
    {
        Pending,
        Active,
        Done,
        Failed
    }

    /// <summary>
    ///     One step of a run funnel: what it is called, where it stands, and one line of detail
    ///     underneath.
    ///     <para>
    ///         Shared between <see cref="RunGuideViewModel" /> and <see cref="ReuseGuideViewModel" />
    ///         - both walk a human through a run behind a full-screen client and draw that as the
    ///         same funnel. Extracted on 24.08.2026 so the two do not carry two copies of the same
    ///         record and the same drawing rules, which is exactly the place they would drift apart.
    ///     </para>
    ///     <para>
    ///         An immutable record without notification, replaced in the collection rather than
    ///         mutated. Five records rebuilt a few times a second cost nothing, and a mutable step
    ///         would need its own <c>INotifyPropertyChanged</c> for four properties that are all
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
}
