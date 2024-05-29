using Smurftown.Backend.Texts;

namespace Smurftown.Backend.Entity
{
    /// <summary>
    ///     A region of the login screen.
    ///     <para>
    ///         <b>An account is NOT fixed to one region</b> - the same account can be played
    ///         in several, the choice is made anew at every login. That is exactly why
    ///         <c>BattlenetAccount</c> has stored a <b>list</b> and no longer a single value
    ///         since 21.08.2026: the game state is region-bound, so an account has its own
    ///         one per region (see <c>HotsRegionData</c>). Since 22.08.2026 that list hangs
    ///         on the <b>game</b> and no longer on the account
    ///         (<c>BattlenetAccount.RegionsByGame</c>) - Heroes of the Storm can be played in
    ///         Europe and America while everything else runs in Europe only.
    ///     </para>
    ///     <para>
    ///         <b>These names are our own data model, not the game's.</b> They land as-is in
    ///         <c>data.yaml</c> and don't need to match anything shown on the screen. Only the
    ///         click path in the login screen needs the real wording of the selection list -
    ///         that gets measured there, not guessed here.
    ///     </para>
    ///     <para>
    ///         Three and not four: checked against the running game on 21.08.2026, the
    ///         login screen's selection list has exactly these entries. No China.
    ///     </para>
    /// </summary>
    public enum BattlenetRegion
    {
        Europe,
        Americas,
        Asia
    }

    public static class BattlenetRegions
    {
        /// <summary>
        ///     Order of the selection list. Europe first, because it's the default case.
        ///     <para>
        ///         <b>This one order applies everywhere</b>: selection in the dialog, the
        ///         toggle in the filter bar, and the rows of an account relative to each
        ///         other. That's why there's <see cref="DisplayOrder" /> next to it and not a
        ///         second comparison that sorts by the enum value - that happens to match
        ///         today and would drift apart with the next entry.
        ///     </para>
        /// </summary>
        private static readonly BattlenetRegion[] Ordered =
        [
            BattlenetRegion.Europe,
            BattlenetRegion.Americas,
            BattlenetRegion.Asia
        ];

        public static readonly IReadOnlyList<BattlenetRegion> InDisplayOrder = Ordered;

        public static string DisplayName(this BattlenetRegion region)
        {
            return Strings.Current[$"region.{region.ToString().ToLowerInvariant()}"];
        }

        /// <summary>
        ///     Two letters for the places where a whole word doesn't fit - the toggle in the
        ///     filter bar and the badge on the account row. Deliberately no image: there are
        ///     no region symbols in the game, and three invented ones would be three symbols
        ///     nobody recognizes.
        ///     <para>
        ///         <b>Not translated, unlike <see cref="DisplayName" /></b>: the abbreviations
        ///         are the same in all four languages - Europa/Europe/Europe/Europa,
        ///         Amerika/Americas/Ameriques/America, Asien/Asia/Asie/Asia. A key per
        ///         abbreviation would be twelve lines saying the same thing four times.
        ///     </para>
        /// </summary>
        public static string ShortName(this BattlenetRegion region)
        {
            return region switch
            {
                BattlenetRegion.Americas => "AM",
                BattlenetRegion.Asia => "AS",
                _ => "EU"
            };
        }

        /// <summary>Position in <see cref="InDisplayOrder" /> - the sort key of the list.</summary>
        public static int DisplayOrder(this BattlenetRegion region)
        {
            return Array.IndexOf(Ordered, region);
        }
    }
}
