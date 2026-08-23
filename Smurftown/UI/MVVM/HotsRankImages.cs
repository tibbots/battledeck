using Smurftown.Backend.Entity;

namespace Smurftown.UI.MVVM;

/// <summary>
///     The one place that maps a rank tier to its pictures. Row, picker and dialog all
///     go through here - the derivation must not drift apart at two places the way the
///     battletag-to-Windows-user derivation once did.
///     <para>
///         <b>There is one picture per tier, not one per rank</b>, and that changed on
///         24.08.2026. Until then there were 25 files, <c>gold_3.png</c> and its
///         siblings, each with the division burnt into it. Two things wanted the same
///         cut: the digit is now drawn by <see cref="RankMedal" />, sharp at any size
///         and any scaling, and the ring carries the progress inside the division -
///         and what is burnt in cannot be lit in part.
///     </para>
/// </summary>
internal static class HotsRankImages
{
    private const string Folder = "pack://application:,,,/UI/Images/Ranks/";

    /// <summary>
    ///     Symbol for "no rank". Since 21.08.2026 that is the magenta circle the game
    ///     shows in the profile in place of the rank disc (drawn by
    ///     tools/build-placement-icon.py) - before that a desaturated, darkened bronze
    ///     medal without a digit, which on a dim screen looked like a real bronze medal.
    /// </summary>
    public const string NoRank = Folder + "norank.png";

    /// <summary>Like <see cref="PathFor" />, but falls back to <see cref="NoRank" /> instead of null.</summary>
    public static string Display(HotsRankTier tier)
    {
        return PathFor(tier) ?? NoRank;
    }

    /// <summary>Path to the medal, or null if no rank is set.</summary>
    public static string? PathFor(HotsRankTier tier)
    {
        return tier == HotsRankTier.None ? null : $"{Folder}{tier.ToString().ToLowerInvariant()}.png";
    }

    /// <summary>
    ///     The light that goes into the medal's channel - the dark groove between its two
    ///     metal rings - transparent everywhere else. <see cref="RankMedal" /> lays it over
    ///     the part the account HAS reached.
    ///     <para>
    ///         <b>Nothing is darkened anywhere.</b> The empty groove is the artwork itself,
    ///         so a medal at zero is the picture untouched, and this file adds only what is
    ///         lit. That is the second attempt: the first darkened the medal's ring and had
    ///         a full ring look like the original but an empty one like a switched-off
    ///         emblem - the wrong way round, since the game lights the groove and leaves
    ///         the metal alone.
    ///     </para>
    ///     <para>
    ///         Null wherever there is nothing to fill: Master and Grand Master have no next
    ///         division, and an unplaced account has no division at all. That question is
    ///         answered by <see cref="HotsRankTiers.HasDivisions" />, not by a second list
    ///         of tiers here.
    ///     </para>
    /// </summary>
    public static string? FillPathFor(HotsRankTier tier)
    {
        return tier.HasDivisions() ? $"{Folder}{tier.ToString().ToLowerInvariant()}_fill.png" : null;
    }
}
