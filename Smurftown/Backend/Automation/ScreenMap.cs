using System.IO;
using System.Reflection;
using Serilog;
using Smurftown.Backend.Entity;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     What a point of the interface is attached to when the window changes size.
    /// </summary>
    public enum ScreenAnchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        Center,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    /// <summary>
    ///     A point or area of the interface: an anchor and a distance from it, measured in
    ///     points of the reference size. <see cref="Layout" /> calculates the position in the
    ///     actual window from it.
    ///     <para>
    ///         <see cref="Width" /> equal to 0 means "to the window edge" - for strips that
    ///         always have the full width and whose reference width therefore says nothing.
    ///     </para>
    /// </summary>
    public sealed class Spot
    {
        /// <summary>
        ///     As a string and not an enum, because YamlDotNet distinguishes enum values by
        ///     capitalization - <c>topLeft</c> in the file would otherwise not match
        ///     <c>TopLeft</c>, and the file is meant to stay lowercase throughout.
        /// </summary>
        public string Anchor { get; set; } = "topLeft";

        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public ScreenAnchor Resolved =>
            Enum.TryParse<ScreenAnchor>(Anchor, true, out var anchor)
                ? anchor
                : throw new InvalidOperationException(
                    $"Calibration: '{Anchor}' is not an anchor. Allowed are " +
                    string.Join(", ", Enum.GetNames<ScreenAnchor>()) + ".");
    }

    /// <summary>
    ///     The actual window in relation to the reference size of the calibration.
    ///     <para>
    ///         The scaling depends solely on the HEIGHT. This is measured: at the same
    ///         height and different width (2560x1080 against 1920x1080), all distances of the
    ///         game interface are identical. Horizontally, the anchor moves instead - left
    ///         stays left, center stays center, right stays right.
    ///     </para>
    /// </summary>
    public readonly record struct Layout(int Width, int Height, int ReferenceWidth, int ReferenceHeight)
    {
        public double Scale => Height / (double)ReferenceHeight;

        /// <summary>A length from the calibration in points of this window.</summary>
        public int Length(double reference)
        {
            return (int)Math.Round(reference * Scale);
        }

        public (int X, int Y) Point(Spot spot)
        {
            var (baseX, baseY) = Origin(spot.Resolved);
            return ((int)Math.Round(baseX + spot.X * Scale), (int)Math.Round(baseY + spot.Y * Scale));
        }

        public (int X, int Y, int Width, int Height) Area(Spot spot)
        {
            var (x, y) = Point(spot);
            var width = spot.Width == 0 ? Width - x : Length(spot.Width);
            var height = spot.Height == 0 ? Height - y : Length(spot.Height);

            // Clip to the window: a crop that extends beyond the edge would be an access
            // into empty space when capturing. Better shorter than off to the side.
            x = Math.Clamp(x, 0, Math.Max(0, Width - 1));
            y = Math.Clamp(y, 0, Math.Max(0, Height - 1));
            return (x, y, Math.Clamp(width, 1, Width - x), Math.Clamp(height, 1, Height - y));
        }

        private (double X, double Y) Origin(ScreenAnchor anchor)
        {
            var x = anchor switch
            {
                ScreenAnchor.TopLeft or ScreenAnchor.BottomLeft => 0.0,
                ScreenAnchor.TopRight or ScreenAnchor.BottomRight => Width,
                _ => Width / 2.0
            };
            var y = anchor switch
            {
                ScreenAnchor.TopLeft or ScreenAnchor.TopCenter or ScreenAnchor.TopRight => 0.0,
                ScreenAnchor.Center => Height / 2.0,
                _ => Height
            };
            return (x, y);
        }
    }

    /// <summary>
    ///     The calibrated game interface: anchors, distances, and recognition thresholds from
    ///     <c>screen-map.yaml</c>.
    ///     <para>
    ///         Why a file and not constants in the code: with a game patch the interface
    ///         shifts, not the logic. A file can be swapped without rebuilding the app - and
    ///         the installation folder lives under <c>Program Files</c>, where you cannot
    ///         just drop something. Hence: the shipped version as an embedded resource, and a
    ///         file at <c>~/.smurftown/screen-map.yaml</c> supersedes it.
    ///     </para>
    /// </summary>
    public class ScreenMap
    {
        private const string ResourceName = "Smurftown.Backend.Automation.screen-map.yaml";

        // Two paths used to stand here, both gone since 21.08.2026: GamePath moved to the
        // settings (SettingsGateway, ~/.smurftown/app.yaml), BattlenetPath fell away for
        // good along with the "Open Battle.net" menu item. This calibration describes what
        // the game looks like, not where it is located. An old entry in a self-made
        // screen-map.yaml does not disturb anything - the deserializer runs with
        // IgnoreUnmatchedProperties.
        public int ReferenceWidth { get; set; } = 3440;
        public int ReferenceHeight { get; set; } = 1440;

        public DetectSection Detect { get; set; } = new();
        public LoginSection Login { get; set; } = new();
        public MenuSection Menu { get; set; } = new();
        public ProfileSection Profile { get; set; } = new();
        public LootSection Loot { get; set; } = new();
        public CollectionSection Collection { get; set; } = new();
        public PenaltySection Penalty { get; set; } = new();
        public PlaySection Play { get; set; } = new();

        public static ScreenMap Load()
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var local = Path.Combine(Directories.UserPath, "screen-map.yaml");
            if (File.Exists(local))
            {
                Log.Information("Calibration from {Path}", local);
                return deserializer.Deserialize<ScreenMap>(File.ReadAllText(local));
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                               ?? throw new InvalidOperationException(
                                   $"Embedded calibration {ResourceName} is missing - check the csproj entry.");
            using var reader = new StreamReader(stream);
            return deserializer.Deserialize<ScreenMap>(reader.ReadToEnd());
        }

        /// <summary>The relationship of this calibration to a concrete window.</summary>
        public Layout LayoutFor(int windowWidth, int windowHeight)
        {
            return new Layout(windowWidth, windowHeight, ReferenceWidth, ReferenceHeight);
        }

        public class DetectSection
        {
            public Spot Strip { get; set; } = new();
            public double MenuAbove { get; set; }
            public double HeroSelectBelow { get; set; }
            public double StableBelow { get; set; } = 3.0;
        }

        public class LoginSection
        {
            public int[] BorderColor { get; set; } = [70, 57, 148];
            public int BorderTolerance { get; set; } = 26;
            public double MinFieldWidth { get; set; } = 0.12;
            public double MinFieldHeight { get; set; } = 0.025;
            public double MaxFieldHeight { get; set; } = 0.060;
            public double SubmitPitchFactor { get; set; } = 2.19;
            public Spot RegionArea { get; set; } = new();

            /// <summary>
            ///     How far the three entries of the expanded list sit ABOVE the closed field.
            ///     Measured on 21.08.2026 at two resolutions (3440x1440 and 3251x1361); the
            ///     individual measurements gave 136 / 97.5 / 61, the entered set uses a
            ///     constant line spacing of 38 - a list has a fixed spacing, and the
            ///     individual measurement only estimates the text center.
            /// </summary>
            public int AmericasAbove { get; set; } = 137;

            public int EuropeAbove { get; set; } = 99;

            public int AsiaAbove { get; set; } = 61;

            /// <summary>
            ///     The distance for a region. The one place that maps region to calibration
            ///     value - a second one would drift apart.
            /// </summary>
            public int AboveFor(BattlenetRegion region)
            {
                return region switch
                {
                    BattlenetRegion.Americas => AmericasAbove,
                    BattlenetRegion.Asia => AsiaAbove,
                    _ => EuropeAbove
                };
            }
        }

        public class MenuSection
        {
            public Spot HeaderCurrencies { get; set; } = new();

            /// <summary>The gear icon bottom right - it opens the menu with "Ausloggen".</summary>
            public Spot Gear { get; set; } = new();

            /// <summary>The "Ausloggen" entry in the expanded gear menu.</summary>
            public Spot Logout { get; set; } = new();
        }

        /// <summary>
        ///     The leaver-penalty status in the menu: the warning symbol and its tooltip.
        ///     Both measured at two resolutions on 21.08.2026.
        /// </summary>
        public class PenaltySection
        {
            public Spot Icon { get; set; } = new();
            public Spot Tooltip { get; set; } = new();
        }

        /// <summary>
        ///     The PLAY screen. <see cref="ModeBar" /> is deliberately an AREA and not a
        ///     point: which tab sits where depends on the text widths and thus on the
        ///     language - the word is searched for, not a coordinate.
        /// </summary>
        public class PlaySection
        {
            public Spot Tab { get; set; } = new();
            public Spot ModeBar { get; set; } = new();
        }

        /// <summary>
        ///     The loot page. Only two points left: the tab and the strip with the counter.
        ///     The opening itself no longer needs a coordinate - it runs over the space bar,
        ///     see <see cref="LootOpener" />.
        /// </summary>
        public class LootSection
        {
            public Spot Tab { get; set; } = new();
            public Spot NavBar { get; set; } = new();
            public int BadgeReach { get; set; } = 120;
        }

        /// <summary>
        ///     The profile overlay: right-click on the profile picture top right, then
        ///     "Profil ansehen". Source for rank, account level, and the battletag.
        /// </summary>
        public class ProfileSection
        {
            public Spot Portrait { get; set; } = new();
            public Spot ViewProfile { get; set; } = new();
            public Spot Battletag { get; set; } = new();
            public Spot Progress { get; set; } = new();

            /// <summary>Where the pointer has to rest for the rank tooltip to come up.</summary>
            public Spot RankMedal { get; set; } = new();

            /// <summary>Where that tooltip then stands - it hangs on the medal, not on the pointer.</summary>
            public Spot RankTooltip { get; set; } = new();

            public Spot Close { get; set; } = new();
        }

        public class CollectionSection
        {
            /// <summary>
            ///     The row of sub-tabs in which <see cref="TabFinder" /> searches for the word
            ///     for "Helden". A fixed point stood here until 22.08.2026 and was measured
            ///     on German - see the comment in <c>screen-map.yaml</c>.
            /// </summary>
            public Spot HeroesBar { get; set; } = new();

            public Spot OwnedFilter { get; set; } = new();
            public Spot SortFilter { get; set; } = new();
            public Spot AllRoles { get; set; } = new();
            public Spot RoleTooltip { get; set; } = new();
            public int DropdownFirst { get; set; } = 313;
            public int DropdownPitch { get; set; } = 38;
            public int OwnedItem { get; set; } = 1;
            public int AlphabeticalItem { get; set; } = 3;
            public Spot CardName { get; set; } = new();
            public int ColumnPitch { get; set; } = 336;
            public int RowPitch { get; set; } = 450;
            public int Columns { get; set; } = 5;
            public int Rows { get; set; } = 2;
            public int ScrollNotches { get; set; } = 3;
        }
    }
}
