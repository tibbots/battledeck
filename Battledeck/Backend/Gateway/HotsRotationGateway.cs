using Battledeck.Backend.Entity;

namespace Battledeck.Backend.Gateway
{
    /// <summary>Where the currently reported rotation comes from.</summary>
    public enum HotsRotationSource
    {
        /// <summary>The calendar doesn't know this period - the UI shows <c>FREE ?</c>.</summary>
        None,

        /// <summary>From <c>rotation-calendar.yaml</c>, the default case.</summary>
        Calendar,

        /// <summary>Set by hand for exactly this period and thereby overrides the calendar.</summary>
        Manual
    }

    /// <summary>
    ///     The free hero rotation. Two sources, and the order between them is the
    ///     whole logic of this class:
    ///     <list type="number">
    ///         <item>
    ///             the <b>calendar</b> (<see cref="HotsRotationCalendar" />) - it knows every
    ///             period and needs no maintenance, because the rotation repeats annually.
    ///         </item>
    ///         <item>
    ///             a <b>manual entry</b> in <c>~/.smurftown/rotation.yaml</c>, set via the
    ///             hero picker. It applies only to the period in which it was set, and
    ///             overrides the calendar within it.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         Until 20.08.2026 there was only the manual entry, because no maintained
    ///         public source was running along anymore. The calendar solves this differently:
    ///         it is not a live source, but a <b>measured table</b> - each of the 48 periods
    ///         checked against at least two independent years. The manual entry still
    ///         remains, as a way out in case Blizzard does change the table after all.
    ///     </para>
    ///     <para>
    ///         A manual entry from an older period is not deleted, but simply no longer
    ///         honored - the next entry overwrites it. Reporting it as "free this week"
    ///         would be wrong, discarding it would be unnecessary.
    ///     </para>
    /// </summary>
    public class HotsRotationGateway
    {
        public static HotsRotationGateway Instance => Singleton.Value;

        private static readonly Lazy<HotsRotationGateway> Singleton = new(() => new HotsRotationGateway(AppFile.Instance));

        private readonly AppFile _app;

        private readonly HotsRotationCalendar _calendar;

        /// <summary>
        ///     Reads the rotation out of <paramref name="app" /> - the <c>rotation</c> section
        ///     of <c>app.yaml</c>, which was <c>rotation.yaml</c> until 1.3.0. The file is
        ///     handed in and not fetched, for the reason given at
        ///     <see cref="BattlenetAccountGateway(string)" />.
        /// </summary>
        public HotsRotationGateway(AppFile app)
        {
            _app = app;
            _calendar = HotsRotationCalendar.Load();
        }

        /// <summary>
        ///     The stored entry. Asked of <see cref="AppFile" /> on every access rather than
        ///     copied into a field - see <see cref="SettingsGateway.Stored" /> for why.
        /// </summary>
        private HotsRotation Stored => _app.State.Rotation;

        /// <summary>The period running today - calculated, not stored.</summary>
        public DateOnly CurrentPeriod => HotsRotationPeriod.Current();

        /// <summary>
        ///     Where <see cref="Free" /> currently comes from. The UI hangs label,
        ///     opacity and tooltip on it.
        /// </summary>
        public HotsRotationSource Source
        {
            get
            {
                if (ManualIsCurrent) return HotsRotationSource.Manual;
                return _calendar.For(CurrentPeriod).Count > 0
                    ? HotsRotationSource.Calendar
                    : HotsRotationSource.None;
            }
        }

        /// <summary>
        ///     The currently free heroes. Everything that marks a hero as "free" hangs
        ///     on this one list - the filter of the bar, the badge in the picker, and
        ///     the match counter.
        ///     <para>
        ///         Spelled out and not as a conditional operator: with <c>cond ? a : b</c>
        ///         compilation depends on the target type being passed through. Two
        ///         separate return statements are unambiguous at the same place.
        ///     </para>
        /// </summary>
        public IReadOnlyList<string> Free
        {
            get
            {
                if (ManualIsCurrent) return Stored.Heroes;
                return _calendar.For(CurrentPeriod);
            }
        }

        /// <summary>
        ///     Does the manual entry apply to the running period? Only then does it
        ///     override the calendar. The empty list does not count as a state here -
        ///     otherwise an accidentally empty-closed picker could switch off the calendar
        ///     for an entire period.
        /// </summary>
        private bool ManualIsCurrent =>
            Stored.Heroes.Count > 0 &&
            HotsRotationPeriod.Parse(Stored.PeriodStart) == CurrentPeriod;

        /// <summary>
        ///     Sets the rotation by hand for the running period. Filtered through the
        ///     catalog, so the file stands in display order and without duplicates -
        ///     and without ids that don't exist.
        /// </summary>
        public void Save(IEnumerable<string> heroIds)
        {
            _app.SaveRotation(new HotsRotation
            {
                PeriodStart = HotsRotationPeriod.ToText(CurrentPeriod),
                Heroes = HotsHeroCatalog.Resolve(heroIds).Select(hero => hero.Id).ToList()
            });
        }

    }
}
