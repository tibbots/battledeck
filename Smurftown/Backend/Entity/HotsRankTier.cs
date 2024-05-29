using Smurftown.Backend.Texts;

namespace Smurftown.Backend.Entity
{
    /// <summary>
    ///     Storm League rank tiers. Order is ascending, <see cref="None" /> stands for "unranked".
    ///     Bronze through Diamond have divisions 5 (lowest) to 1 (highest); Master and GrandMaster have none.
    /// </summary>
    public enum HotsRankTier
    {
        None,
        Bronze,
        Silver,
        Gold,
        Platinum,
        Diamond,
        Master,
        GrandMaster
    }

    public static class HotsRankTiers
    {
        public const int LowestDivision = 5;
        public const int HighestDivision = 1;

        public static bool HasDivisions(this HotsRankTier tier)
        {
            return tier is >= HotsRankTier.Bronze and <= HotsRankTier.Diamond;
        }

        /// <summary>
        ///     The name of the tier in the language of the UI.
        ///     <para>
        ///         <b>These words appear a second time in the code</b>, namely in
        ///         <c>GameVocabulary.Tiers</c>, and that is intentional rather than
        ///         duplication: there they are <i>measured values</i> - the text that stands
        ///         on screen in the game client and that OCR compares against. Here it is
        ///         <i>display</i>. The two can have different languages, and in the case this
        ///         separation was built for, they do: a French client, a German UI. Whoever
        ///         merges them either makes recognition blind or the display wrong.
        ///     </para>
        ///     <para>
        ///         The key is built from the enum name and does not sit next to it as a
        ///         <c>switch</c> - a second list would drift apart with the next value. The
        ///         price: a rename in the enum silently changes the key, and the UI would
        ///         then show <c>!rank.xyz!</c>.
        ///     </para>
        /// </summary>
        public static string DisplayName(this HotsRankTier tier)
        {
            return Strings.Current[$"rank.{tier.ToString().ToLowerInvariant()}"];
        }
    }
}
