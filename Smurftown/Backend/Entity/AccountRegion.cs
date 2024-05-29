namespace Smurftown.Backend.Entity
{
    /// <summary>
    ///     An account in ONE of its regions - the unit the overview has consisted of since
    ///     21.08.2026. Whoever plays in Europe and America has two of them and consequently
    ///     appears twice in the list, with two different ranks and two hero lists.
    ///     <para>
    ///         <b>Pure view row, none of it is persisted.</b> In
    ///         <c>data.yaml</c> there is still one entry per account; the pairs are formed on
    ///         load from <see cref="BattlenetAccount.PlayedRegions" /> - the union over all
    ///         games - and rebuilt on every change.
    ///     </para>
    ///     <para>
    ///         <b>It deliberately carries no game.</b> Since 22.08.2026 the regions hang on
    ///         the game, so not every row plays every game: which one is shown is decided by
    ///         the exclusive game filter, and whether this row plays it at all is a question
    ///         for the filter predicate (<c>BattlenetAccount.PlaysIn</c>). A row per game and
    ///         region would be the same set once more, with a field nothing reads.
    ///     </para>
    ///     <para>
    ///         The three passed-through properties are not decoration: the list sorts via
    ///         <c>SortDescription</c>, and that requires a <b>property name on the element
    ///         itself</b> - a path via <c>Account.DisplayName</c> would be bindable, but is
    ///         not resolved by sorting.
    ///     </para>
    /// </summary>
    public sealed record AccountRegion(BattlenetAccount Account, BattlenetRegion Region)
    {
        /// <summary>
        ///     The game state of this pair, or <c>null</c> if this region has never been
        ///     read. The row then shows dashes instead of zeros - see
        ///     <see cref="BattlenetAccount.HotsIn" />.
        /// </summary>
        public HotsRegionData? Hots => Account.HotsIn(Region);

        public string DisplayName => Account.DisplayName;

        public DateTime LatestInteractionAt => Account.LatestInteractionAt;

        /// <summary>
        ///     Sort key of the region - the order of the selection list, not the alphabet.
        ///     This way the two rows of an account always stay in the same order relative
        ///     to each other (Europe, America, Asia) instead of one that changes with the
        ///     name.
        /// </summary>
        public int RegionOrder => Region.DisplayOrder();
    }
}
