using System.IO;
using Serilog;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Update;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Settings = Smurftown.Backend.Entity.Settings;

namespace Smurftown.Backend.Gateway
{
    /// <summary>
    ///     Everything the application knows about itself, in one file: what the human set,
    ///     what they picked by hand for this rotation period, and what the update check
    ///     noted. <c>~/.smurftown/app.yaml</c>.
    ///     <para>
    ///         <b>The three sections used to be three files</b> — <c>settings.yaml</c>,
    ///         <c>rotation.yaml</c>, <c>update.yaml</c> — plus <c>version.txt</c> for the
    ///         version that wrote them. Four files for four handfuls of values, and each of
    ///         them its own reader, its own writer and its own "what if it is missing".
    ///     </para>
    /// </summary>
    public sealed class AppState
    {
        /// <summary>
        ///     The shape of this file, so a later change has something to hang a migration
        ///     on. It is <b>not</b> the version of the application - that one is
        ///     <see cref="AppVersion" /> and moves with every release, while this number only
        ///     moves when the layout does.
        /// </summary>
        public int SchemaVersion { get; set; } = AppFile.CurrentSchema;

        /// <summary>
        ///     Which release last wrote these files. Read by <see cref="DataBackup" /> before
        ///     anything else runs, to decide whether a backup is due - it was
        ///     <c>version.txt</c> until 1.3.0.
        /// </summary>
        public string AppVersion { get; set; } = "";

        /// <summary>What the human sets. The settings tab writes here, nothing else does.</summary>
        public Settings Settings { get; set; } = new();

        /// <summary>A rotation picked by hand, for one period. Usually empty.</summary>
        public HotsRotation Rotation { get; set; } = new();

        /// <summary>What the update check noted. Written every hour, read by nobody else.</summary>
        public UpdateState Update { get; set; } = new();
    }

    /// <summary>
    ///     Reads and writes <c>app.yaml</c>. Not named <c>…Gateway</c> on purpose: it offers
    ///     no domain API, it owns a file. The three gateways above it
    ///     (<see cref="SettingsGateway" />, <see cref="HotsRotationGateway" />,
    ///     <see cref="UpdateGateway" />) each own one section of it and keep their own
    ///     questions.
    ///     <para>
    ///         <b>Every write re-reads the whole file first and replaces only its own
    ///         section.</b> That is the property the whole class exists for, and it is what
    ///         makes one shared file safe where three separate ones used to be necessary: the
    ///         update check runs once an hour and writes <c>update</c>; if it wrote its own
    ///         in-memory picture of the file, it would carry a <c>settings</c> block that is
    ///         as old as the moment the window opened - and take an edit made in the meantime
    ///         with it. Reading immediately before writing costs one file read of a few
    ///         hundred bytes and removes the entire class of problem.
    ///     </para>
    ///     <para>
    ///         <b>It does not make two running instances safe</b>, and nothing here pretends
    ///         otherwise. Two windows can still interleave between the read and the write.
    ///         What it does remove is the far more likely case: two writers <i>inside one
    ///         process</i> that hold their own copy of a file they share.
    ///     </para>
    ///     <para>
    ///         <b>A file from a newer schema is read but never written.</b> Deserialising into
    ///         these classes drops every key they do not know, so writing such a file back
    ///         would silently delete whatever a later version put there. Reading it
    ///         best-effort keeps the application usable; refusing the write keeps the file
    ///         intact.
    ///     </para>
    /// </summary>
    public sealed class AppFile
    {
        /// <summary>The shape this build writes. Raise it when the layout changes, not the content.</summary>
        internal const int CurrentSchema = 1;

        internal const string FileName = "app.yaml";

        /// <summary>
        ///     Serialises read-modify-write against this file for the whole process.
        ///     <para>
        ///         <b>Static and not per instance</b>, because the thing being protected is the
        ///         file and not the object: two <see cref="AppFile" /> instances on one folder
        ///         with a lock each would guard nothing. A process works in exactly one data
        ///         folder (<see cref="Directories.UserPath" /> resolves once and keeps the
        ///         answer), so one lock is one file.
        ///     </para>
        ///     <para>
        ///         <b>Why at all, when every writer is on the UI thread today.</b> They are -
        ///         the update check hangs on a <c>DispatcherTimer</c>, and the game flows do
        ///         their reading in <c>Task.Run</c> but the gateway call after the await, back
        ///         on the caller's thread. That is a convention nobody enforces, and the day
        ///         somebody wraps one save in a <c>Task.Run</c>, the re-read below stops being
        ///         a guarantee and becomes a race that shows up as a lost setting once a month.
        ///     </para>
        ///     <para>
        ///         <b>It does not reach across processes.</b> Two running copies of Smurftown
        ///         still interleave; see <c>docs/architecture.md</c>.
        ///     </para>
        /// </summary>
        private static readonly object FileLock = new();

        /// <summary>
        ///     The one app file of the application. Lazy for the same reason as
        ///     <see cref="BattlenetAccountGateway.Instance" />: building this one has a side
        ///     effect - it migrates the files of the older layout - and a test that merely
        ///     mentions the type should not trigger that against the shared test folder.
        /// </summary>
        public static AppFile Instance => Singleton.Value;

        private static readonly Lazy<AppFile> Singleton = new(() => new AppFile(Directories.UserPath));

        private readonly string _folder;
        private readonly string _path;

        // IgnoreUnmatchedProperties for the same reason as everywhere here: a file written
        // by a newer version must not throw in an older one. The converter is what keeps
        // update.lastCheck a single scalar - see DateTimeOffsetYamlConverter.
        private readonly IDeserializer _yamlIn = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new DateTimeOffsetYamlConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        private readonly ISerializer _yamlOut = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithTypeConverter(new DateTimeOffsetYamlConverter())
            .Build();

        private AppState _state;

        /// <summary>
        ///     Reads <c>app.yaml</c> out of <paramref name="folder" />, and builds it from the
        ///     files of the older layout if it is not there yet. The folder is a parameter for
        ///     the reason given at <see cref="BattlenetAccountGateway(string)" />.
        /// </summary>
        public AppFile(string folder)
        {
            _folder = folder;
            _path = Path.Combine(folder, FileName);
            _state = Load();
        }

        /// <summary>
        ///     The state as it was last read or written. Read freely - but never write
        ///     through it: a section reaches the file only through the <c>Save…</c> methods,
        ///     which re-read first.
        /// </summary>
        public AppState State => _state;

        public void SaveSettings(Settings settings) => Write(fresh => fresh.Settings = settings);

        public void SaveRotation(HotsRotation rotation) => Write(fresh => fresh.Rotation = rotation);

        public void SaveUpdate(UpdateState update) => Write(fresh => fresh.Update = update);

        /// <summary>
        ///     Notes the running release as the one that wrote the data - the successor of
        ///     <c>version.txt</c>. Called from <see cref="DataBackup.MarkCurrent" />, once the
        ///     start has survived.
        /// </summary>
        public void SaveAppVersion(string version) => Write(fresh => fresh.AppVersion = version);

        /// <summary>
        ///     The release that wrote the data folder, without building an
        ///     <see cref="AppFile" /> and without migrating anything.
        ///     <para>
        ///         <b>It has to be static, and that is not a style question.</b>
        ///         <see cref="DataBackup.BeforeMigrations" /> asks this before any gateway
        ///         exists - that is the whole point of it. Constructing an
        ///         <see cref="AppFile" /> there would run the migration below, which deletes
        ///         the very files the backup is about to set aside.
        ///     </para>
        ///     <para>
        ///         Empty when nothing is noted anywhere: a fresh installation, or one from
        ///         before 22.08.2026, which wrote no marker at all.
        ///     </para>
        /// </summary>
        internal static string PeekAppVersion(string folder)
        {
            try
            {
                var path = Path.Combine(folder, FileName);
                if (File.Exists(path))
                {
                    var peeked = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .WithTypeConverter(new DateTimeOffsetYamlConverter())
                        .IgnoreUnmatchedProperties()
                        .Build()
                        .Deserialize<AppState>(File.ReadAllText(path));

                    if (!string.IsNullOrWhiteSpace(peeked?.AppVersion)) return peeked.AppVersion;
                }

                // The older layout. Still read here and not only in the migration, because
                // the very first start after the update asks this question before app.yaml
                // exists - and answering it "unknown" would name the backup after nothing.
                var marker = Path.Combine(folder, LegacyMarkerFile);
                return File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
            }
            catch (Exception e)
            {
                Log.Warning(e, "Could not read the version that wrote {Folder}", folder);
                return "";
            }
        }

        private void Write(Action<AppState> change)
        {
            // The lock spans read, change and write as ONE step. Reading immediately before
            // writing is only worth something if nothing can slip in between the two.
            lock (FileLock)
            {
                // FRESH FROM DISK, never from _state: see the class comment. Whatever another
                // writer put into the other sections since this one last looked is in here,
                // and stays in here.
                var fresh = ReadOrEmpty();

                if (fresh.SchemaVersion > CurrentSchema)
                {
                    throw new InvalidOperationException(
                        $"{FileName} is written in schema {fresh.SchemaVersion}, this build knows {CurrentSchema}. " +
                        "Refusing to write it back, because that would drop everything the newer version put in it. " +
                        "Run the newer Smurftown, or move the file aside.");
                }

                change(fresh);
                fresh.SchemaVersion = CurrentSchema;

                Directory.CreateDirectory(_folder);
                File.WriteAllText(_path, _yamlOut.Serialize(fresh));
                _state = fresh;
            }
        }

        /// <summary>
        ///     The file as it stands, or an empty state. Used before every write, so it must
        ///     <b>not</b> migrate: at that point the migration has long since run, and a
        ///     missing file means somebody deleted it while the app was open.
        /// </summary>
        private AppState ReadOrEmpty()
        {
            if (!File.Exists(_path)) return new AppState();

            try
            {
                return _yamlIn.Deserialize<AppState>(File.ReadAllText(_path)) ?? new AppState();
            }
            catch (Exception e)
            {
                // Same stance as the gateways this replaces: a broken file costs the defaults
                // and a line in the log, never the start. The next write repairs it, which is
                // acceptable precisely because none of these values is irreplaceable - unlike
                // data.yaml, which does stop the start.
                Log.Error(e, "{File} unreadable, continuing with the defaults: {Path}", FileName, _path);
                return new AppState();
            }
        }

        private AppState Load()
        {
            if (File.Exists(_path))
            {
                var state = ReadOrEmpty();
                if (state.SchemaVersion > CurrentSchema)
                {
                    Log.Warning("{File} is written in schema {Found}, this build knows {Known}. " +
                                "Reading what is recognised; nothing will be written back to it.",
                        FileName, state.SchemaVersion, CurrentSchema);
                }

                return state;
            }

            return Migrate();
        }

        // ---- the older layout ------------------------------------------------------------

        private const string LegacySettingsFile = "settings.yaml";
        private const string LegacyRotationFile = "rotation.yaml";
        private const string LegacyUpdateFile = "update.yaml";
        private const string LegacyMarkerFile = "version.txt";

        /// <summary>
        ///     Builds <c>app.yaml</c> out of the four files of the older layout and removes
        ///     them. Runs once, on the first start after the update.
        ///     <para>
        ///         <b>What makes deleting them safe is the order, not the backup.</b> The old
        ///         files go only once <c>app.yaml</c> stands on disk, and every value they held
        ///         is in it - <c>version.txt</c> included, as
        ///         <see cref="AppState.AppVersion" />.
        ///     </para>
        ///     <para>
        ///         The archive <see cref="DataBackup" /> writes moments earlier is a second net
        ///         and not the first one, and it is worth knowing when it is <i>not</i> there:
        ///         it is written only when the running version differs from the one that wrote
        ///         the data. For the upgrade this migration exists for that is always the case.
        ///         For a build carrying the same version number as the files it finds - a
        ///         development build, a reinstall of the same release - it is not, and then
        ///         this migration is the only thing standing between those four files and
        ///         nothing.
        ///     </para>
        ///     <para>
        ///         <b>Each file on its own <c>try</c>.</b> A rotation nobody can read is not a
        ///         reason to lose the settings as well - and none of the four is a reason to
        ///         refuse the start. What cannot be read stays at its default, which is what
        ///         the old gateways did with the same file.
        ///     </para>
        ///     <para>
        ///         <b>The old files go only once the new one stands.</b> If writing fails, they
        ///         stay where they are and the next start tries again.
        ///     </para>
        /// </summary>
        private AppState Migrate()
        {
            // Under the same lock as every other write: two instances built at the same
            // moment would otherwise both find the four files and both delete them.
            lock (FileLock) return MigrateUnderLock();
        }

        private AppState MigrateUnderLock()
        {
            var state = new AppState
            {
                Settings = ReadLegacy<Settings>(LegacySettingsFile) ?? new Settings(),
                Rotation = ReadLegacy<HotsRotation>(LegacyRotationFile) ?? new HotsRotation(),
                Update = ReadLegacy<UpdateState>(LegacyUpdateFile) ?? new UpdateState(),
                AppVersion = ReadLegacyMarker()
            };

            var legacy = new[] { LegacySettingsFile, LegacyRotationFile, LegacyUpdateFile, LegacyMarkerFile }
                .Select(name => Path.Combine(_folder, name))
                .Where(File.Exists)
                .ToArray();

            if (legacy.Length == 0)
            {
                // A fresh installation. Nothing to carry over, and nothing to write yet
                // either - the first save creates the file.
                return state;
            }

            try
            {
                Directory.CreateDirectory(_folder);
                File.WriteAllText(_path, _yamlOut.Serialize(state));
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not write {Path} - keeping the files of the older layout", _path);
                return state;
            }

            foreach (var file in legacy)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Log.Warning(e, "{Path} is carried over but could not be removed", file);
                }
            }

            Log.Information("Carried {Count} file(s) of the older layout into {File}", legacy.Length, FileName);
            return state;
        }

        private T? ReadLegacy<T>(string name) where T : class
        {
            var path = Path.Combine(_folder, name);
            if (!File.Exists(path)) return null;

            try
            {
                return _yamlIn.Deserialize<T>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Log.Warning(e, "{Path} could not be carried over, its section stays at the defaults", path);
                return null;
            }
        }

        private string ReadLegacyMarker()
        {
            var path = Path.Combine(_folder, LegacyMarkerFile);
            if (!File.Exists(path)) return "";

            try
            {
                return File.ReadAllText(path).Trim();
            }
            catch (Exception e)
            {
                Log.Warning(e, "{Path} could not be read - the next start backs up under 'unknown'", path);
                return "";
            }
        }
    }
}
