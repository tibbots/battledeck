using System.Globalization;

namespace Smurftown.Backend.Entity
{
    /// <summary>
    ///     A <b>manually set</b> rotation state, as it stands in
    ///     <c>~/.smurftown/rotation.yaml</c>. It is the override for exactly one
    ///     period; the default case comes from <see cref="HotsRotationCalendar" />.
    ///     <para>
    ///         The rotation applies equally to all accounts - it hangs on the game, not the
    ///         account. That's why it lives in its own file and not in
    ///         <c>data.yaml</c>.
    ///     </para>
    ///     <para>
    ///         The period start is stored as a string in it and not as a <c>DateOnly</c>:
    ///         YamlDotNet does not map <c>DateOnly</c> reliably, and an ISO date is more
    ///         readable in the file anyway. It is parsed exclusively via
    ///         <see cref="HotsRotationPeriod.Parse" />.
    ///     </para>
    /// </summary>
    public class HotsRotation
    {
        private List<string> _heroes = [];

        /// <summary>Start of the period as an ISO date ("2026-08-15"), empty if never set.</summary>
        public string PeriodStart { get; set; } = "";

        /// <summary>
        ///     Ids of the free heroes. The setter catches null: a key without a value
        ///     (<c>heroes:</c>) deserializes to null, not to an empty list.
        /// </summary>
        public List<string> Heroes
        {
            get => _heroes;
            set => _heroes = value ?? [];
        }
    }

    /// <summary>
    ///     The periods of the free rotation. They change on the 1st, 8th, 15th and 22nd of
    ///     every month.
    ///     <para>
    ///         This class calculates exclusively the <b>time span</b>. Who is free within it
    ///         stands in <see cref="HotsRotationCalendar" /> - a measured table that looks up
    ///         by month and day. The separation is intentional: the time span follows a
    ///         rule, the lineup does not.
    ///     </para>
    /// </summary>
    public static class HotsRotationPeriod
    {
        /// <summary>Slots in the rotation - the reference size for the "n / 14" display.</summary>
        public const int HeroCount = 14;

        private const string IsoFormat = "yyyy-MM-dd";

        /// <summary>Start of the period that <paramref name="date" /> falls into.</summary>
        public static DateOnly StartOf(DateOnly date)
        {
            var day = date.Day switch
            {
                >= 22 => 22,
                >= 15 => 15,
                >= 8 => 8,
                _ => 1
            };
            return new DateOnly(date.Year, date.Month, day);
        }

        /// <summary>
        ///     End of the period, i.e. the start of the next one. After the 22nd that is the
        ///     first of the following month - that's why this is calculated here instead of
        ///     just blindly adding seven days.
        /// </summary>
        public static DateOnly EndOf(DateOnly start)
        {
            return start.Day == 22
                ? new DateOnly(start.Year, start.Month, 1).AddMonths(1)
                : new DateOnly(start.Year, start.Month, start.Day + 7);
        }

        /// <summary>Start of the period currently running today.</summary>
        public static DateOnly Current()
        {
            return StartOf(DateOnly.FromDateTime(DateTime.Today));
        }

        /// <summary>ISO date for the file.</summary>
        public static string ToText(DateOnly start)
        {
            return start.ToString(IsoFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        ///     ISO date from the file, or <c>null</c>. Null is not an error case: a rotation
        ///     that was never set has an empty string here.
        /// </summary>
        public static DateOnly? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return DateOnly.TryParseExact(text, IsoFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }

        /// <summary>Time span as "Aug 15 - Aug 22" - fixed English, like the rest of the UI.</summary>
        public static string Label(DateOnly start)
        {
            return $"{Day(start)} - {Day(EndOf(start))}";
        }

        private static string Day(DateOnly date)
        {
            return date.ToString("MMM d", CultureInfo.InvariantCulture);
        }
    }
}
