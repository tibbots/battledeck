using System.IO;
using System.IO.Compression;
using Serilog;

namespace Battledeck.Backend.Gateway
{
    /// <summary>
    ///     Sets the data files aside before a new version touches them - once per version,
    ///     into <c>~/.smurftown/backups/{the version that wrote them}.zip</c>.
    ///     <para>
    ///         <b>One archive and not a folder</b>, since 1.3.0. It is not a size argument -
    ///         the files are some 30 KB - but a countable one: <see cref="Housekeeping" />
    ///         caps the backups at ten, and counting archives is honest in a way that counting
    ///         folders somebody may have half-emptied is not. Existing folders are converted
    ///         on the next start.
    ///     </para>
    ///     <para>
    ///         <b>A ZIP is not protection.</b> Every one of these archives holds a complete
    ///         <c>data.yaml</c>, and that file carries the passwords in plain text. The
    ///         archive makes the folder tidy, nothing more - whoever treats
    ///         <c>~/.smurftown/</c> as a password store has to treat <c>backups/</c> as one
    ///         too.
    ///     </para>
    ///     <para>
    ///         <b>Why at all</b>: <c>data.yaml</c> holds the credentials in plain text and is
    ///         the actual value of this app, and every schema change so far has been a
    ///         migration that rewrites the whole file. A migration that reads wrongly does not
    ///         throw - it writes an emptier file, and that looks exactly like an account that
    ///         has never been read. The read-back check in
    ///         <see cref="BattlenetAccountGateway" /> catches the case it knows about; this
    ///         backup catches the ones nobody thought of.
    ///     </para>
    ///     <para>
    ///         <b>Why per version and not per start</b>: a copy on every start would push the
    ///         interesting state out of reach after two launches. What matters is the state
    ///         <i>before</i> the update, so the marker is the version - not the date.
    ///     </para>
    ///     <para>
    ///         It replaced the single <c>data.yaml.pre-regions.bak</c>, which was written by
    ///         exactly one migration and would have had to be invented anew for the next one.
    ///     </para>
    /// </summary>
    public static class DataBackup
    {
        /// <summary>
        ///     The name of the folder for data whose writing version is unknown - every
        ///     installation from before 22.08.2026, because none of them wrote a marker.
        /// </summary>
        private const string UnknownVersion = "unknown";

        /// <summary>
        ///     The version that wrote the data, or <c>null</c> if nobody noted one.
        ///     <para>
        ///         <b>Asked of <see cref="AppFile.PeekAppVersion" /> and not of
        ///         <see cref="AppFile.Instance" />.</b> This runs before the first gateway
        ///         exists - that is the entire point of it - and building an
        ///         <see cref="AppFile" /> here would run its migration, which deletes the very
        ///         files that are about to be set aside.
        ///     </para>
        /// </summary>
        private static string? ReadMarker(string folder)
        {
            var version = AppFile.PeekAppVersion(folder);
            return version.Length == 0 ? null : version;
        }

        /// <summary>
        ///     Where the backups live. <see cref="Housekeeping" /> caps their number and needs
        ///     to know the same folder.
        /// </summary>
        internal static string BackupRoot(string folder) => Path.Combine(folder, "backups");

        /// <summary>
        ///     One archive per version, not a folder per version. Named after the version that
        ///     <i>wrote</i> the files, which is the state somebody would want back.
        /// </summary>
        internal static string BackupFile(string folder, string version) =>
            Path.Combine(BackupRoot(folder), version + ".zip");

        /// <summary>
        ///     Copies the data files aside if they were written by a different version than
        ///     the running one. To be called <b>before the first gateway</b> - afterwards a
        ///     migration may already have overwritten them.
        ///     <para>
        ///         <paramref name="folder" /> is the data folder, handed in rather than read
        ///         from <c>Directories.UserPath</c>: that one resolves once per process, which
        ///         is right for the application and unusable for a test that needs its own
        ///         folder per case.
        ///     </para>
        ///     <para>
        ///         Deliberately without an abort on failure: a backup that cannot be written
        ///         is almost always a full disk, and refusing to start over it would take the
        ///         app away from the human as well as the backup. It is logged as a warning,
        ///         which is the one thing that stays true either way.
        ///     </para>
        /// </summary>
        public static void BeforeMigrations(string folder)
        {
            CompressLegacyFolders(folder);

            try
            {
                var previous = ReadMarker(folder);
                if (previous == AppVersion.Current) return;

                var files = DataFiles(folder);
                if (files.Length == 0)
                {
                    // A fresh installation. There is nothing to lose, and an archive named
                    // after a version that never ran would only be misleading.
                    Log.Information("No data files to back up before version {Version}", AppVersion.Current);
                    return;
                }

                var target = BackupFile(folder, previous ?? UnknownVersion);
                if (File.Exists(target))
                {
                    // ALREADY THERE, so leave it alone. A second run of the same update
                    // would otherwise copy the state a failed migration left behind over
                    // the one from before it - and that is precisely the state this exists
                    // to keep.
                    Log.Information("Backup {Path} already exists, keeping it", target);
                    return;
                }

                WriteArchive(target, files);

                Log.Information("Backed up {Count} file(s) written by {Previous} to {Path} " +
                                "before running {Current}",
                    files.Length, previous ?? UnknownVersion, target, AppVersion.Current);
            }
            catch (Exception e)
            {
                Log.Warning(e, "Could not write the backup before the migrations - continuing");
            }
        }

        /// <summary>
        ///     Notes the running version as the one that wrote the data. To be called
        ///     <b>after</b> the gateways are up, not before: whoever writes the marker first
        ///     and then fails the migration has no backup left for the second attempt.
        ///     <para>
        ///         <b>It takes the file and not a folder</b>, unlike
        ///         <see cref="BeforeMigrations" /> right above it, and the asymmetry is the
        ///         point: that one runs before anything exists and may not build an
        ///         <see cref="AppFile" />, this one runs when everything is up and must use
        ///         the one that is already there. A second instance on the same path would
        ///         keep its own picture of the file.
        ///     </para>
        /// </summary>
        public static void MarkCurrent(AppFile app)
        {
            try
            {
                if (app.State.AppVersion == AppVersion.Current) return;

                app.SaveAppVersion(AppVersion.Current);
                Log.Information("Data files are now on version {Version}", AppVersion.Current);
            }
            catch (Exception e)
            {
                // Same stance as above. The cost of a lost marker is one superfluous
                // backup on the next start, not a lost file.
                Log.Warning(e, "Could not note the version that wrote the data");
            }
        }

        /// <summary>
        ///     Writes the given files into one ZIP, each under its own name and without any
        ///     folder inside the archive.
        ///     <para>
        ///         <b>Through a <c>.partial</c> and a move</b>, for the same reason as in
        ///         <see cref="LogArchive.CompressInto" />: a process that dies mid-write would
        ///         otherwise leave a truncated archive under the final name, and the
        ///         "already there, keep it" check above would then protect a broken file
        ///         forever.
        ///     </para>
        /// </summary>
        private static void WriteArchive(string target, IEnumerable<string> files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var partial = target + ".partial";
            if (File.Exists(partial)) File.Delete(partial);

            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                }
            }

            File.Move(partial, target, true);
        }

        /// <summary>
        ///     Turns the <c>backups/{version}/</c> folders of every installation up to 1.2.0
        ///     into <c>backups/{version}.zip</c>. Runs on every start and costs a directory
        ///     listing once there is nothing left to convert.
        ///     <para>
        ///         <b>Its own try per folder</b>, and the folder goes only once the archive
        ///         stands: a conversion that fails halfway must leave the copies where they
        ///         are. The whole point of these files is to survive the case where something
        ///         else went wrong.
        ///     </para>
        /// </summary>
        private static void CompressLegacyFolders(string folder)
        {
            var root = BackupRoot(folder);
            if (!Directory.Exists(root)) return;

            foreach (var legacy in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var target = legacy + ".zip";
                    var files = Directory.EnumerateFiles(legacy, "*.*", SearchOption.TopDirectoryOnly).ToArray();

                    if (files.Length > 0 && !File.Exists(target)) WriteArchive(target, files);

                    Directory.Delete(legacy, true);
                    Log.Information("Backup {Name} is now an archive", Path.GetFileName(target));
                }
                catch (Exception e)
                {
                    Log.Warning(e, "Could not turn the backup folder {Path} into an archive", legacy);
                }
            }
        }

        /// <summary>
        ///     Everything worth keeping: the YAML files directly in the data folder.
        ///     <para>
        ///         Not the log - it is written continuously and says nothing about the state
        ///         of the data. Not <c>shots/</c> either: screenshots of a stranded run are
        ///         evidence, not data, and copying them would double megabytes per update.
        ///     </para>
        /// </summary>
        private static string[] DataFiles(string folder)
        {
            if (!Directory.Exists(folder)) return [];

            return Directory
                .EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                               || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }
}
