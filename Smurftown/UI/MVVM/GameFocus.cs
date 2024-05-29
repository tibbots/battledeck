namespace Smurftown.UI.MVVM
{
    /// <summary>
    ///     Which game the rows of the overview should show - <c>null</c> means "none
    ///     chosen, every row decides for itself".
    ///     <para>
    ///         Set exclusively by the game filter of the filter bar. Since that
    ///         became exclusive, it is no longer just a selection but also a view choice:
    ///         whoever filters on Overwatch wants to see Overwatch numbers and not have to switch
    ///         again in every row.
    ///     </para>
    ///     <para>
    ///         <b>Why a static value and not an event:</b> the row view models are created
    ///         via an <c>IValueConverter</c> in the <c>ItemTemplate</c>, so freshly for each row. An
    ///         event would have to be subscribed by each of them - and since they are
    ///         discarded and rebuilt on every re-filter, they would hang forever on the static
    ///         event list and could never be collected. Instead the filter sets
    ///         this value before it re-filters; <c>ICollectionView.Refresh</c> discards all
    ///         containers and has them recreated, and each fresh view model reads the
    ///         value in the constructor. This only holds as long as the <c>ItemsControl</c> does not
    ///         virtualize - it does not, its default panel is a plain
    ///         <c>StackPanel</c>. Whoever turns that into a <c>VirtualizingStackPanel</c> gets
    ///         an event plus unsubscription back here.
    ///     </para>
    /// </summary>
    internal static class GameFocus
    {
        public static string? Current { get; set; }
    }
}
