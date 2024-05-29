using Smurftown.Backend.Texts;

namespace Smurftown.Backend.Entity
{
    /// <summary>
    ///     Hero roles as in the game. The order is at the same time the display order
    ///     in the hero picker - from front (Tank) to back (Support).
    /// </summary>
    public enum HotsHeroRole
    {
        Tank,
        Bruiser,
        MeleeAssassin,
        RangedAssassin,
        Healer,
        Support
    }

    public static class HotsHeroRoles
    {
        /// <summary>All roles in display order - the enum order, once as a list.</summary>
        public static readonly IReadOnlyList<HotsHeroRole> InDisplayOrder = Enum.GetValues<HotsHeroRole>();

        /// <summary>
        ///     Spelled-out name. The wiki template shortens to "Melee" and "Ranged",
        ///     in-game the roles are called "Melee Assassin" and "Ranged Assassin".
        /// </summary>
        /// <summary>
        ///     Spelled-out name in the language of the UI.
        ///     <para>
        ///         <b>These names are NOT measured against the client</b> - unlike everything in
        ///         <c>GameVocabulary</c>. They are never read, only displayed, so the
        ///         translation must be understandable and does not need to match Blizzard's
        ///         exact wording. Should someone check them against the client one day after
        ///         all, they get measured and it gets noted here.
        ///     </para>
        /// </summary>
        public static string DisplayName(this HotsHeroRole role)
        {
            return Strings.Current[$"role.{role.ToString().ToLowerInvariant()}"];
        }
    }
}
