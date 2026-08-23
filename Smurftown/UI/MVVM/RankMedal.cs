using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Smurftown.Backend.Entity;

namespace Smurftown.UI.MVVM;

/// <summary>
///     One rank medal, drawn from three parts: the tier's picture, the lit part of its
///     progress channel, and the division digit.
///     <para>
///         <b>Why an element that draws, instead of three things in the XAML.</b> All
///         three sit on the same circle, and the numbers that place them are fractions of
///         the same canvas. In the XAML they would stand once per site - the account row
///         and the rank picker - and that pair would drift apart the first time a medal
///         changes size. Here they stand once, and every site passes only a box.
///     </para>
///     <para>
///         <b>The progress lies IN the picture.</b> Every medal carries two metal rings
///         with a dark groove between them, and that groove is what fills up - so progress
///         is light added, never metal taken away. <c>{tier}_fill.png</c> holds that light,
///         and what gets painted is the sector the account HAS reached; at zero the medal
///         stands untouched, exactly as it was drawn.
///     </para>
///     <para>
///         <b>Two attempts came before it</b>, and both are worth not repeating. A ring
///         drawn AROUND the emblem cost the row six points of height and read as a progress
///         bar parked behind a medal. Darkening the medal's own metal read the wrong way
///         round: full looked like the original, empty like a switched-off emblem.
///     </para>
///     <para>
///         <b>The bottom of the channel is missing on purpose.</b> The crest sits in front
///         of it at six o'clock, so the light leaves a wedge out there, and the fill runs
///         from the crest's left edge clockwise back to its right one -
///         <see cref="StartDegrees" /> and <see cref="SweepDegrees" />. A fill starting at
///         twelve would have to run through the crest, where nothing shows anyway.
///     </para>
///     <para>
///         Every measured number below comes from <c>tools/build-rank-assets.py</c>, which
///         cuts the pictures from the same values. Whoever changes one changes both, and
///         re-runs the script.
///     </para>
/// </summary>
public sealed class RankMedal : FrameworkElement
{
    /// <summary>Which medal. <see cref="HotsRankTier.None" /> draws the "no rank" disc.</summary>
    public static readonly DependencyProperty TierProperty = DependencyProperty.Register(
        nameof(Tier), typeof(HotsRankTier), typeof(RankMedal),
        new FrameworkPropertyMetadata(HotsRankTier.None, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Division 5 to 1. Anything outside draws no digit - which is what Master and Grand Master need.</summary>
    public static readonly DependencyProperty DivisionProperty = DependencyProperty.Register(
        nameof(Division), typeof(int), typeof(RankMedal),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How far through the division, 0…1. Values outside are clamped, not rejected.</summary>
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(RankMedal),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    ///     Whether the medal shows progress at all. <b>False draws the medal as it was
    ///     drawn</b> - which is the answer for Master, Grand Master, an account whose
    ///     placement matches are still open, and the rank picker, where the question is
    ///     which rank and not how far along.
    /// </summary>
    public static readonly DependencyProperty ShowProgressProperty = DependencyProperty.Register(
        nameof(ShowProgress), typeof(bool), typeof(RankMedal),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public HotsRankTier Tier
    {
        get => (HotsRankTier)GetValue(TierProperty);
        set => SetValue(TierProperty, value);
    }

    public int Division
    {
        get => (int)GetValue(DivisionProperty);
        set => SetValue(DivisionProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool ShowProgress
    {
        get => (bool)GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    // ---- the canvas the pictures were measured on, and the places on it ---------------

    /// <summary>Every medal is 160x176. Everything below is a fraction of that, so any box works.</summary>
    private const double CanvasWidth = 160, CanvasHeight = 176;

    /// <summary>
    ///     Centre of the channel, as a fraction of the box - the mean over the five fitted
    ///     tiers. They differ by up to two points on the canvas, under one on screen, and
    ///     this centre only decides where the CUT sits; the channel's own shape comes out
    ///     of the picture. A point of error therefore moves an edge, not a ring.
    /// </summary>
    private const double RingCentreX = 80.578 / CanvasWidth, RingCentreY = 80.072 / CanvasHeight;

    /// <summary>Half the wedge the crest covers at six o'clock, in degrees. Same value as in the script.</summary>
    private const double CrestWedge = 38;

    /// <summary>Left edge of the crest, in screen degrees - zero points right, ninety down.</summary>
    private const double StartDegrees = 90 + CrestWedge;

    /// <summary>Once round, minus the wedge at both ends of it.</summary>
    private const double SweepDegrees = 360 - 2 * CrestWedge;

    /// <summary>Where the division digit sits, as a fraction of the box.</summary>
    private const double DigitCentreX = 80.0 / CanvasWidth, DigitCentreY = 82.0 / CanvasHeight;

    /// <summary>Cap height of that digit, measured on the original sheet.</summary>
    private const double DigitCapHeight = 30.0 / CanvasHeight;

    /// <summary>
    ///     The typeface the pictures were lettered in until 24.08.2026 - <b>not</b>
    ///     Blizzard's own, which is not ours to ship. Keeping it is why the digit did not
    ///     visibly change when it moved out of the pictures.
    /// </summary>
    private static readonly Typeface DigitFace = new(
        new FontFamily("Segoe UI Black"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal);

    private static readonly Brush DigitFill = Frozen(Color.FromRgb(252, 250, 248));

    /// <summary>
    ///     The glow. Warm white rather than the tier's own colour, and that is the point:
    ///     the discs behind the digit are gold, blue, violet and white by turn, so a glow
    ///     in the tier's colour would sink into its own disc every time. Brighter than
    ///     everything beneath it always reads.
    /// </summary>
    private static readonly Brush DigitGlow = Frozen(Color.FromRgb(255, 246, 216));

    /// <summary>
    ///     Under the glow, not instead of it. Diamond's gem is nearly white, and a bright
    ///     digit on a bright ground needs the dark edge to stand at all - the glow alone
    ///     would let it melt into the background.
    /// </summary>
    private static readonly Brush DigitShade = Frozen(Color.FromArgb(190, 0, 0, 0));

    /// <summary>The glow, as three strokes: wide and faint on the outside, narrow and stronger within.</summary>
    private static readonly (double Width, double Opacity)[] GlowStrokes =
        [(7.0, 0.16), (4.5, 0.24), (2.5, 0.32)];

    /// <summary>
    ///     Loaded once per file. A row rebuilds its medal on every filter change, and
    ///     without this each one would decode a PNG again.
    /// </summary>
    private static readonly Dictionary<string, BitmapImage> Cache = new();

    protected override void OnRender(DrawingContext dc)
    {
        var box = Fit();
        if (box.IsEmpty) return;

        var medal = Load(HotsRankImages.Display(Tier));
        if (medal != null) dc.DrawImage(medal, box);

        DrawChannelFill(dc, box);
        DrawDivision(dc, box);
    }

    /// <summary>
    ///     The largest 160x176 box that fits, centred. The two call sites pass 71x78 and
    ///     58x64, both already that shape - this only keeps a third one from silently
    ///     squashing the medal.
    /// </summary>
    private Rect Fit()
    {
        var scale = Math.Min(ActualWidth / CanvasWidth, ActualHeight / CanvasHeight);
        if (scale <= 0) return Rect.Empty;

        var width = CanvasWidth * scale;
        var height = CanvasHeight * scale;
        return new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
    }

    /// <summary>
    ///     Paints the part of the channel already reached. The clip is a plain pie from the
    ///     channel's centre - the groove's own shape lives in the picture's alpha, so
    ///     nothing here has to know how wide or how far out it runs.
    /// </summary>
    private void DrawChannelFill(DrawingContext dc, Rect box)
    {
        if (!ShowProgress) return;

        var progress = Math.Clamp(Progress, 0.0, 1.0);
        if (progress <= 0) return;

        var fill = Load(HotsRankImages.FillPathFor(Tier));
        if (fill == null) return;

        var centre = new Point(box.X + box.Width * RingCentreX, box.Y + box.Height * RingCentreY);
        var reach = box.Width + box.Height;
        var from = StartDegrees;
        var span = SweepDegrees * progress;

        var figure = new PathFigure { StartPoint = centre, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(OnCircle(centre, reach, from), false));
        figure.Segments.Add(new ArcSegment(OnCircle(centre, reach, from + span),
            new Size(reach, reach), 0, span > 180, SweepDirection.Clockwise, false));

        var wedge = new PathGeometry();
        wedge.Figures.Add(figure);
        wedge.Freeze();

        dc.PushClip(wedge);
        dc.DrawImage(fill, box);
        dc.Pop();
    }

    /// <summary>
    ///     The division, as an outline rather than as text: an outline can be stroked, and
    ///     three strokes of falling width are a glow that needs no <c>Effect</c> and
    ///     therefore no second layer in the visual tree.
    ///     <para>
    ///         Sized from its own bounds, not from the font size. A font size is a promise
    ///         about the line, not about the capitals, and this digit has to hit the height
    ///         measured on the original.
    ///     </para>
    /// </summary>
    private void DrawDivision(DrawingContext dc, Rect box)
    {
        if (!Tier.HasDivisions()) return;
        if (Division < HotsRankTiers.HighestDivision || Division > HotsRankTiers.LowestDivision) return;

        var text = new FormattedText(
            Division.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, DigitFace, 100, DigitFill,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var glyph = text.BuildGeometry(new Point(0, 0));
        var bounds = glyph.Bounds;
        if (bounds.Height <= 0) return;

        var scale = box.Height * DigitCapHeight / bounds.Height;
        var place = new TransformGroup();
        place.Children.Add(new TranslateTransform(-bounds.X - bounds.Width / 2, -bounds.Y - bounds.Height / 2));
        place.Children.Add(new ScaleTransform(scale, scale));
        place.Children.Add(new TranslateTransform(
            box.X + box.Width * DigitCentreX, box.Y + box.Height * DigitCentreY));
        glyph.Transform = place;

        // The shade first, offset the way it used to be baked into the pictures.
        var shade = glyph.Clone();
        var shifted = new TransformGroup();
        shifted.Children.Add(place);
        shifted.Children.Add(new TranslateTransform(scale, 2 * scale));
        shade.Transform = shifted;
        dc.DrawGeometry(DigitShade, null, shade);

        foreach (var (width, opacity) in GlowStrokes)
        {
            var pen = new Pen(Faded(DigitGlow, opacity), width * scale) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, glyph);
        }

        dc.DrawGeometry(DigitFill, null, glyph);
    }

    private static Point OnCircle(Point centre, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(centre.X + radius * Math.Cos(radians), centre.Y + radius * Math.Sin(radians));
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    private static Brush Faded(Brush source, double opacity)
    {
        var brush = source.Clone();
        brush.Opacity = opacity;
        brush.Freeze();
        return brush;
    }

    private static BitmapImage? Load(string? path)
    {
        if (path == null) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();

        Cache[path] = image;
        return image;
    }
}
