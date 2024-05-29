using Smurftown.Backend.Entity;

namespace Smurftown.UI.MVVM;

/// <summary>
///     The one place that maps rank tier + division to an image. Dialog and account card
///     both access it - the derivation must not drift apart at two places the way the
///     battletag-to-Windows-user derivation did.
/// </summary>
static class HotsRankImages
{
    private const string Folder = "pack://application:,,,/UI/Images/Ranks/";

    /// <summary>
    ///     Symbol for "no rank". Since 21.08.2026 that is the magenta-colored circle that
    ///     the game shows in the profile in place of the rank circle (drawn by
    ///     tools/build-placement-icon.py) - previously a desaturated, darkened
    ///     bronze medal without a digit, which on a dimly lit screen looked like a
    ///     real bronze medal.
    /// </summary>
    public const string NoRank = Folder + "norank.png";

    /// <summary>Like <see cref="PathFor" />, but falls back to <see cref="NoRank" /> instead of null.</summary>
    public static string Display(HotsRankTier tier, int division)
    {
        return PathFor(tier, division) ?? NoRank;
    }

    /// <summary>Path to the medal image, or null if no rank is set.</summary>
    public static string? PathFor(HotsRankTier tier, int division)
    {
        if (tier == HotsRankTier.None) return null;

        var name = tier.ToString().ToLowerInvariant();
        if (!tier.HasDivisions()) return $"{Folder}{name}.png";

        var clamped = Math.Clamp(division, HotsRankTiers.HighestDivision, HotsRankTiers.LowestDivision);
        return $"{Folder}{name}_{clamped}.png";
    }
}
