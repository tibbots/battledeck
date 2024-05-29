namespace Smurftown.Backend.Entity
{
    /// <summary>
    ///     The ids of the four games.
    ///     <para>
    ///         They stand here and not in <c>GameVisuals</c>, because they are needed in
    ///         <b>both</b> layers: the UI builds symbol, color and name from them, the gateway
    ///         uses them to decide which account passes the filter. <c>Backend/</c> does not
    ///         know <c>UI/</c> - so they must sit at the bottom, not the top. <c>GameVisuals</c>
    ///         refers back here with its own constants, so nothing had to be rewritten at the
    ///         call sites.
    ///     </para>
    /// </summary>
    public static class Games
    {
        public const string Hots = "hots";
        public const string Overwatch = "overwatch";
        public const string Wow = "wow";
        public const string Diablo = "diablo";
    }
}
