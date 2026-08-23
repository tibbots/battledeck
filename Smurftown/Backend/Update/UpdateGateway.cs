using System.Globalization;
using Serilog;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using Smurftown.Backend.Gateway;

namespace Smurftown.Backend.Update
{
    /// <summary>
    ///     Reads and writes a <see cref="DateTimeOffset" /> as ONE scalar in round-trip
    ///     format (<c>2026-08-22T18:38:03.0000000+00:00</c>).
    ///     <para>
    ///         <b>Without it the hourly check is not hourly.</b> YamlDotNet has no built-in
    ///         emitter for this type and falls back to walking its properties, so
    ///         <c>lastCheck</c> came out as a mapping of twenty - <c>dateTime</c>, <c>day</c>,
    ///         <c>dayOfWeek</c>, <c>ticks</c> and the rest. Reading that back produced no
    ///         exception and no warning in the log: the value simply arrived as
    ///         <c>default</c>, which <see cref="UpdateGateway.Due" /> reads as "never asked".
    ///         Every start therefore asked GitHub again - the one thing the interval exists to
    ///         prevent, and the one thing the README promises does not happen.
    ///     </para>
    ///     <para>
    ///         The deserializer already handled the scalar on its own; the converter is
    ///         registered on both sides anyway, so the two directions cannot drift apart
    ///         again.
    ///     </para>
    /// </summary>
    public sealed class DateTimeOffsetYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type)
        {
            return type == typeof(DateTimeOffset);
        }

        public object ReadYaml(IParser parser, Type type)
        {
            var value = parser.Consume<Scalar>().Value;

            // An empty or unparseable stamp is not worth an exception: the caller's catch
            // would land on the same default, only after writing a warning about a file the
            // human never touched.
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : default(DateTimeOffset);
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type)
        {
            var stamp = value is DateTimeOffset offset ? offset : default;
            emitter.Emit(new Scalar(stamp.ToString("O", CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>
    ///     What the update check remembers between two starts, in
    ///     <c>~/.smurftown/update.yaml</c>.
    ///     <para>
    ///         <b>Not part of <c>Settings</c></b>, even though it would fit in the same file.
    ///         The settings are what a human sets; these two values are what the application
    ///         noted. Mixing them would mean every check writes <c>settings.yaml</c> - a file
    ///         the human edits by hand - and a save that collides with such an edit would take
    ///         the edit with it.
    ///     </para>
    ///     <para>
    ///         As everywhere here: new fields without <c>required</c> and with a sensible
    ///         default, then an older file needs no migration.
    ///     </para>
    /// </summary>
    public sealed class UpdateState
    {
        /// <summary>
        ///     When GitHub was last asked - <b>successfully or not</b>. A failed check counts
        ///     too, otherwise a machine that is offline for a week asks again on every start of
        ///     the application and writes a warning into the log each time.
        /// </summary>
        public DateTimeOffset LastCheck { get; set; }

        /// <summary>
        ///     The version the last successful check found. It exists so that the notice is on
        ///     screen at the next start <b>immediately</b>, instead of appearing a second or
        ///     two in - the check runs on the network, the window does not wait for it.
        /// </summary>
        public string LatestVersion { get; set; } = "";
    }

    /// <summary>
    ///     Asks once an hour whether there is a newer release, and remembers the answer.
    ///     <para>
    ///         Hand-written singleton like <see cref="Gateway.SettingsGateway" /> and
    ///         <see cref="Gateway.BattlenetAccountGateway" /> - no holder, no container, the
    ///         same pattern as everywhere here.
    ///     </para>
    ///     <para>
    ///         <b>The clock lives in the file and not in a timer</b>, and that stays true even
    ///         though a timer now exists: <see cref="ViewModel.UpdateOffer" /> asks this
    ///         gateway every few minutes whether something is due, and the answer comes out of
    ///         <c>lastCheck</c> in <c>update.yaml</c>. So a session that is closed and reopened
    ///         does not start the hour over, and one that stays open all afternoon still asks
    ///         once per hour rather than once at nine in the morning.
    ///     </para>
    ///     <para>
    ///         That split is the reason nothing here knows about a timer. Whoever ticks calls
    ///         <see cref="CheckIfDue" />; whether that costs a request is decided in
    ///         <see cref="Due" /> and nowhere else.
    ///     </para>
    /// </summary>
    public sealed class UpdateGateway
    {
        /// <summary>
        ///     Once an hour, as asked for.
        ///     <para>
        ///         It was a day until 23.08.2026, on the argument that a release does not appear
        ///         more often than that. True, and beside the point: the cost of the longer
        ///         interval is not paid by the release, it is paid by the human sitting in front
        ///         of an application that has been open since morning and shows a version that
        ///         went stale at noon.
        ///     </para>
        ///     <para>
        ///         An hour is still nothing against the rate limit - see
        ///         <see cref="GithubReleases" />: 60 requests per hour and IP address,
        ///         unauthenticated, and this spends one of them.
        ///     </para>
        /// </summary>
        public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        public static UpdateGateway Instance => Singleton.Value;

        private static readonly Lazy<UpdateGateway> Singleton = new(() => new UpdateGateway(AppFile.Instance));

        private readonly AppFile _app;

        /// <summary>
        ///     Reads the state out of <paramref name="app" /> - the <c>update</c> section of
        ///     <c>app.yaml</c>, which was <c>update.yaml</c> until 1.3.0. The file is handed
        ///     in and not fetched, for the reason given at
        ///     <see cref="Smurftown.Backend.Gateway.BattlenetAccountGateway(string)" />.
        /// </summary>
        public UpdateGateway(AppFile app)
        {
            _app = app;
        }

        /// <summary>
        ///     The stored state. Asked of <see cref="AppFile" /> on every access rather than
        ///     copied into a field: the file is re-read before every write, and a copy here
        ///     would be exactly the stale picture that arrangement exists to avoid.
        /// </summary>
        private UpdateState _state => _app.State.Update;

        /// <summary>
        ///     The release found in this session, once one has been found. The install command
        ///     needs it for the download URLs, which <see cref="LatestVersion" /> out of the
        ///     file cannot carry.
        /// </summary>
        public GithubRelease? Pending { get; private set; }

        /// <summary>
        ///     The newest version known from the last run, or an empty string. Read at start,
        ///     before anything touches the network.
        /// </summary>
        public string LatestVersion => _state.LatestVersion;

        /// <summary>
        ///     When GitHub was last asked, successfully or not - <c>default</c> when it never
        ///     was. It is read by the settings tab, which is the first place that ever shows
        ///     this: the value has been written since the check existed and was visible to
        ///     nobody.
        /// </summary>
        public DateTimeOffset LastCheck => _state.LastCheck;

        /// <summary>
        ///     Is a check due?
        ///     <para>
        ///         <b>There is no switch in front of this.</b> The check runs, every hour, and
        ///         the human cannot turn it off - a decision taken deliberately, because the
        ///         delivery has no other way of reaching anybody: what ships is a ZIP without an
        ///         installer and without a start menu entry, so nothing else would tell them
        ///         that a version exists. A setting existed here for half a day and went again;
        ///         what it bought was an inconsistency (a notice already on screen stayed after
        ///         switching off) in exchange for an option nobody would find.
        ///         <c>docs/security.md</c> states the request instead of offering to suppress it.
        ///     </para>
        ///     <para>
        ///         <b>A time stamp in the future counts as due</b>, and that is not
        ///         hair-splitting: a clock corrected backwards - a fresh installation, a dead
        ///         CMOS battery, a trip across time zones with a badly set clock - would
        ///         otherwise put the next check as far into the future as the clock jumped, and
        ///         no update would ever appear again on that machine.
        ///     </para>
        /// </summary>
        public bool Due
        {
            get
            {
                if (_state.LastCheck == default) return true;

                var since = DateTimeOffset.UtcNow - _state.LastCheck;
                return since >= Interval || since < TimeSpan.Zero;
            }
        }

        /// <summary>
        ///     Asks GitHub if it is time, and returns the release <b>only when it is newer</b>
        ///     than what is running. Null covers everything else: not due, offline, already up
        ///     to date.
        /// </summary>
        public async Task<GithubRelease?> CheckIfDue(CancellationToken cancel = default)
        {
            return Due ? await Check(cancel) : null;
        }

        /// <summary>
        ///     Asks, regardless of the clock. Separate from <see cref="CheckIfDue" /> so that
        ///     pressing the button can fetch the URLs the cached version does not carry.
        /// </summary>
        public async Task<GithubRelease?> Check(CancellationToken cancel = default)
        {
            var release = await GithubReleases.Latest(cancel);

            // A NEW state and not a mutation of the stored one: what is in AppFile belongs
            // to the file, and the file decides what reaches disk. The stamp goes down even
            // on a failure - see LastCheck - and before the comparison, so an unparsable tag
            // does not turn into a check on every start.
            var noted = new UpdateState
            {
                LastCheck = DateTimeOffset.UtcNow,
                LatestVersion = release?.Version ?? _state.LatestVersion
            };

            if (release != null)
            {
                Log.Information("Update check: {Latest} offered, {Current} running",
                    release.Version, AppVersion.Current);
            }

            Save(noted);

            if (release == null || !AppVersion.IsNewerThanCurrent(release.Version)) return null;

            Pending = release;
            return release;
        }

        private void Save(UpdateState noted)
        {
            try
            {
                _app.SaveUpdate(noted);
            }
            catch (Exception e)
            {
                // Deliberately only a warning and no exception upwards: the consequence of a
                // state that cannot be written is one check per start instead of one per
                // hour. That is not worth an error on a path the human never asked for - and
                // it is the right stance for the one case that throws here on purpose, a
                // file written by a newer schema.
                Log.Warning(e, "The update state could not be written");
            }
        }
    }
}
