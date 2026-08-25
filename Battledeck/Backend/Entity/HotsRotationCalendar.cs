using System.Globalization;
using System.IO;
using System.Reflection;
using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Battledeck.Backend.Entity
{
    /// <summary>
    ///     Which heroes are free in which period - as a calendar without a year, from
    ///     <c>rotation-calendar.yaml</c>.
    ///     <para>
    ///         The rotation repeats annually: the same calendar day carries the same
    ///         14 heroes. That is measured and not assumed - each of the 48 periods holds
    ///         across at least two independent years; checked were 2023 through 2026.
    ///         That's why a key of month and day is enough, and that's why the file stays
    ///         valid without anyone maintaining it.
    ///     </para>
    ///     <para>
    ///         <b>It still cannot be calculated, though.</b> The first six slots do run in a
    ///         cycle of three, but that breaks twice a year: on 03-08 the Raynor group stands
    ///         for the second time in a row, on 04-15 Tychus sits in Dehaka's
    ///         spot. A table carries both outliers without a special case; a formula would
    ///         have to know them as an exception - and whoever overlooks an exception silently
    ///         marks the wrong heroes as free.
    ///     </para>
    ///     <para>
    ///         Shipped as an embedded resource, overridable by
    ///         <c>~/.smurftown/rotation-calendar.yaml</c> - the same pattern as with
    ///         <c>screen-map.yaml</c> and for the same reason: the installation folder sits
    ///         under <c>Program Files</c>, where you can't just drop something.
    ///     </para>
    /// </summary>
    public class HotsRotationCalendar
    {
        private const string ResourceName = "Battledeck.Backend.Entity.rotation-calendar.yaml";
        private const string FileName = "rotation-calendar.yaml";

        /// <summary>Key format of the file: month and day, without year.</summary>
        private const string KeyFormat = "MM-dd";

        private Dictionary<string, IReadOnlyList<string>>? _resolved;

        /// <summary>
        ///     Target of deserialization: <c>"MM-dd"</c> to hero ids. Public and
        ///     writable, because YamlDotNet otherwise has nothing to write into - it is
        ///     read exclusively via <see cref="For" />.
        ///     <para>
        ///         The value is allowed to be <c>null</c>, because a key without a list
        ///         (<c>"01-01":</c> with nothing behind it) deserializes to exactly that -
        ///         the same trap as with the <c>heroes:</c> key in <see cref="HotsRotation" />.
        ///     </para>
        /// </summary>
        public Dictionary<string, List<string>?> Periods { get; set; } = new();

        /// <summary>
        ///     The calendar, as this installation sees it. Never throws: the only caller
        ///     hangs on a static field initializer (<c>HotsRotationGateway.Instance</c>),
        ///     and an exception there would be a TypeInitializationException across half the
        ///     app. Written as <c>c</c> and not as <c>see cref</c>: <c>Entity</c> does not
        ///     know <c>Gateway</c>, not even in the comment.
        ///     An unreadable calendar means "no free rotation known", not "app broken".
        /// </summary>
        public static HotsRotationCalendar Load()
        {
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                var local = Path.Combine(Directories.UserPath, FileName);
                if (File.Exists(local))
                {
                    Log.Information("Rotation calendar from {Path}", local);
                    return deserializer.Deserialize<HotsRotationCalendar>(File.ReadAllText(local))
                           ?? new HotsRotationCalendar();
                }

                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                                   ?? throw new InvalidOperationException(
                                       $"Embedded rotation calendar {ResourceName} is missing - " +
                                       "check the csproj entry.");
                using var reader = new StreamReader(stream);
                return deserializer.Deserialize<HotsRotationCalendar>(reader.ReadToEnd())
                       ?? new HotsRotationCalendar();
            }
            catch (Exception e)
            {
                Log.Warning(e, "Rotation calendar unreadable - the free rotation stays empty");
                return new HotsRotationCalendar();
            }
        }

        /// <summary>
        ///     The free heroes of a period, in display order and without duplicates. Empty
        ///     if the calendar doesn't know the day - that is the case in which the UI shows
        ///     <c>FREE ?</c>.
        ///     <para>
        ///         A <b>period start</b> is expected, as
        ///         <see cref="HotsRotationPeriod.StartOf" /> delivers it. An arbitrary date
        ///         finds nothing here, and that is intentional: the file only knows the 1st,
        ///         8th, 15th and 22nd, and a silent fallback to "some nearby period" would be
        ///         a guess.
        ///     </para>
        /// </summary>
        public IReadOnlyList<string> For(DateOnly periodStart)
        {
            var key = periodStart.ToString(KeyFormat, CultureInfo.InvariantCulture);

            // Spelled out and not as a conditional operator - same reasoning as with
            // HotsRotationGateway.Free: with cond ? a : [] compilation depends on the
            // target type being passed through.
            if (Resolved.TryGetValue(key, out var heroes)) return heroes;
            return [];
        }

        /// <summary>
        ///     Resolved once and then held. Lazy and not in the constructor, because YamlDotNet
        ///     only fills <see cref="Periods" /> <i>after</i> the constructor.
        /// </summary>
        private Dictionary<string, IReadOnlyList<string>> Resolved => _resolved ??= Resolve();

        private Dictionary<string, IReadOnlyList<string>> Resolve()
        {
            var resolved = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, ids) in Periods)
            {
                if (ids == null) continue;

                // Unknown ids are REPORTED and not silently swallowed - unlike in
                // data.yaml, where they can be called "written by a newer app version".
                // Here they simply mean: the file is wrong.
                var unknown = ids.Where(id => HotsHeroCatalog.Find(id) == null).ToList();
                if (unknown.Count > 0)
                    Log.Warning("Rotation calendar {Key}: {Count} unknown id(s) " +
                                "skipped - {Unknown}", key, unknown.Count,
                        string.Join(", ", unknown));

                resolved[key] = HotsHeroCatalog.Resolve(ids).Select(hero => hero.Id).ToList();
            }

            return resolved;
        }
    }
}
