using System.Windows;
using System.Windows.Media;
using Smurftown.Backend.Entity;

namespace Smurftown.UI.MVVM;

/// <summary>
///     The one place that maps a game identifier to image, accent color and display name.
///     The card's game rail, the panel's border and the message in the empty panel all access
///     it.
///     <para>
///         Same rationale as with <see cref="HotsRankImages" /> and
///         <see cref="HotsHeroImages" />: a derivation that stands in several places
///         drifts apart over time. That is exactly what happened with the battletag-to-Windows-user
///         derivation.
///     </para>
///     <para>
///         The identifiers are the same strings that stand in the XAML as <c>CommandParameter</c>.
///         Deliberately no enum: the value runs through the XAML, and there it is text
///         anyway - the same consideration as with the start menu's operating modes.
///     </para>
/// </summary>
static class GameVisuals
{
    // The values themselves stand in Backend/Entity/Games.cs: the gateway needs them
    // as well, and Backend does not know UI. Here they remain as names, so that
    // nothing had to be rewritten at the call sites.
    public const string Hots = Games.Hots;
    public const string Overwatch = Games.Overwatch;
    public const string Wow = Games.Wow;
    public const string Diablo = Games.Diablo;

    private const string Folder = "pack://application:,,,/UI/Images/";

    /// <summary>
    ///     Order of the rail, same as the former icon row. It is not taste:
    ///     whoever knows the card looks for the icons where they used to stand.
    /// </summary>
    public static readonly IReadOnlyList<string> InDisplayOrder = [Overwatch, Hots, Wow, Diablo];

    /// <summary>
    ///     Accent color per game. It is no longer shown directly - the 3-point-wide
    ///     strip at the left row margin fell on 21.08.2026. What emerges from it are
    ///     the three derived brushes below: tint, separator ring and border under the pointer.
    ///     That way the row says without a single word whose numbers are currently showing - exactly the
    ///     shortcoming of the former card, whose figures stood unlabeled next to four equal-ranked
    ///     game icons.
    ///     <para>The shades are measured from the game icons, not made up.</para>
    /// </summary>
    private static readonly Dictionary<string, SolidColorBrush> Accents = new()
    {
        [Hots] = Frozen("#4A9FD8"),
        [Overwatch] = Frozen("#F79E1B"),
        [Wow] = Frozen("#C8A44A"),
        [Diablo] = Frozen("#B3453F")
    };

    /// <summary>
    ///     Base color of the account row.
    ///     <para>
    ///         It stands here and not only in the XAML, because TWO values are derived from
    ///         it that must necessarily fit together: the row's tint and the separator ring
    ///         between the overlapping hero portraits. The ring is a hole in the
    ///         overlap - it must have exactly the color that, at its spot, lies BEHIND
    ///         it, and since the gradient that is no longer simply the base color.
    ///     </para>
    ///     <para>
    ///         Whoever changes it also changes the <c>Background</c> in the <c>Border.Style</c> of
    ///         <c>AccountCardView.xaml</c>.
    ///     </para>
    /// </summary>
    private static readonly Color RowBase = Rgb("#1E1F24");

    /// <summary>
    ///     Coverage of the tint at the left margin of the row; toward the right it fades to 0.
    ///     <para>
    ///         <b>The upper bound sits in the separator ring, not in taste.</b> It is a
    ///         single color, the gradient on the other hand location-dependent - the stronger the
    ///         tint, the bigger the remainder that a single color misses. At 0.18
    ///         the error over the width of the strip is at most five steps per channel
    ///         - computed across all four accents, biggest with Overwatch's orange -
    ///         and is thus invisible.
    ///     </para>
    /// </summary>
    private const double TintAtLeft = 0.18;

    /// <summary>
    ///     Where the hero strip sits, as a share of the row width - the spot where the
    ///     separator ring must match the tint.
    ///     <para>
    ///         From the column budget of <c>AccountCardView.xaml</c>: name column 146 plus
    ///         margins 17 plus medal 69 plus penalty triangle 47 are 279, the strip is 285
    ///         wide, so its center lies at around 420 of 1158.
    ///     </para>
    /// </summary>
    private const double StripMidpoint = 0.36;

    /// <summary>
    ///     Tint of the row: the accent fading out from left to right, across the whole
    ///     width. It says the same thing as the accent strip, only as mood instead of edge.
    ///     <para>
    ///         <b>The transparent stop carries the same RGB values as the opaque one.</b> WPF
    ///         computes the gradient non-premultiplied - if <c>Transparent</c> stood there,
    ///         the color would run through a dirty gray along the way instead of simply
    ///         fading out.
    ///     </para>
    /// </summary>
    private static readonly Dictionary<string, LinearGradientBrush> Tints =
        Accents.ToDictionary(e => e.Key, e => Tint(e.Value.Color));

    /// <summary>
    ///     The separator ring of the hero strip, in the color of the tint at its spot.
    ///     Derived from <see cref="TintAtLeft" /> and <see cref="StripMidpoint" /> and
    ///     therefore not as a number in the XAML: set by hand it would drift away from the
    ///     gradient at the next tweak.
    /// </summary>
    private static readonly Dictionary<string, SolidColorBrush> Separators =
        Accents.ToDictionary(e => e.Key,
            e => Frozen(Blend(RowBase, e.Value.Color, TintAtLeft * (1 - StripMidpoint))));

    /// <summary>
    ///     The row's border under the pointer, in the game's accent instead of the neutral gray.
    ///     Stronger than the tint, because it only affects one row and only as long as the
    ///     mouse is on it.
    /// </summary>
    private static readonly Dictionary<string, SolidColorBrush> HoverBorders =
        Accents.ToDictionary(e => e.Key, e => Frozen(Blend(RowBase, e.Value.Color, 0.45)));

    private static readonly Dictionary<string, string> Icons = new()
    {
        [Hots] = Folder + "hots.png",
        [Overwatch] = Folder + "overwatch.png",
        [Wow] = Folder + "wow.png",
        [Diablo] = Folder + "diablo4.png"
    };

    private static readonly Dictionary<string, string> Labels = new()
    {
        [Hots] = "Heroes of the Storm",
        [Overwatch] = "Overwatch",
        [Wow] = "World of Warcraft",
        [Diablo] = "Diablo IV"
    };

    /// <summary>
    ///     Short names for places where the full one does not fit - today the tabs of the
    ///     account dialog, where five labels must stand next to each other.
    ///     <para>
    ///         They stand here and not in the dialog, for the same reason as icon, color and
    ///         full name: a derivation in two places drifts apart over time.
    ///     </para>
    /// </summary>
    private static readonly Dictionary<string, string> ShortLabels = new()
    {
        [Hots] = "HOTS",
        [Overwatch] = "OW2",
        [Wow] = "WOW",
        [Diablo] = "DIA"
    };

    /// <summary>Without a game there is nothing to tint - and the ring stays the base color.</summary>
    private static readonly SolidColorBrush NeutralSeparator = Frozen(RowBase);

    private static readonly SolidColorBrush NeutralHoverBorder = Frozen("#3A3D46");

    private static readonly LinearGradientBrush NoTint = Tint(Color.FromArgb(0, 0, 0, 0));

    public static Brush TintFor(string? game)
    {
        return game != null && Tints.TryGetValue(game, out var brush) ? brush : NoTint;
    }

    public static Brush StripSeparatorFor(string? game)
    {
        return game != null && Separators.TryGetValue(game, out var brush) ? brush : NeutralSeparator;
    }

    public static Brush HoverBorderFor(string? game)
    {
        return game != null && HoverBorders.TryGetValue(game, out var brush) ? brush : NeutralHoverBorder;
    }

    public static string IconFor(string game)
    {
        return Icons.TryGetValue(game, out var path) ? path : "";
    }

    public static string LabelFor(string? game)
    {
        return game != null && Labels.TryGetValue(game, out var label) ? label : "";
    }

    public static string ShortLabelFor(string? game)
    {
        return game != null && ShortLabels.TryGetValue(game, out var label) ? label : "";
    }

    private static SolidColorBrush Frozen(string hex)
    {
        return Frozen(Rgb(hex));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Rgb(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex)!;
    }

    /// <summary>Two colors on top of each other, <paramref name="alpha" /> is the coverage of the upper one.</summary>
    private static Color Blend(Color under, Color over, double alpha)
    {
        return Color.FromRgb(
            (byte)Math.Round(under.R + (over.R - under.R) * alpha),
            (byte)Math.Round(under.G + (over.G - under.G) * alpha),
            (byte)Math.Round(under.B + (over.B - under.B) * alpha));
    }

    private static LinearGradientBrush Tint(Color accent)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            [
                new GradientStop(Color.FromArgb((byte)Math.Round(TintAtLeft * 255),
                    accent.R, accent.G, accent.B), 0.0),
                new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1.0)
            ]
        };
        brush.Freeze();
        return brush;
    }
}
