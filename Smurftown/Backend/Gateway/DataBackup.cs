using System.IO;
using Serilog;

namespace Smurftown.Backend.Gateway
{
    /// <summary>
    ///     Sets the data files aside before a new version touches them - once per version,
    ///     into <c>~/.smurftown/backups/{the version that wrote them}/</c>.
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

        private static string MarkerFile(string folder) => Path.Combine(folder, "version.txt");

        private static string BackupRoot(string folder) => Path.Combine(folder, "backups");

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
            try
            {
                var previous = ReadMarker(folder);
                if (previous == AppVersion.Current) return;

                var files = DataFiles(folder);
                if (files.Length == 0)
                {
                    // A fresh installation. There is nothing to lose, and an empty folder
                    // named after a version that never ran would only be misleading.
                    Log.Information("No data files to back up before version {Version}", AppVersion.Current);
                    return;
                }

                var target = Path.Combine(BackupRoot(folder), previous ?? UnknownVersion);
                if (Directory.Exists(target))
                {
                    // ALREADY THERE, so leave it alone. A second run of the same update
                    // would otherwise copy the state a failed migration left behind over
                    // the one from before it - and that is precisely the state this exists
                    // to keep.
                    Log.Information("Backup {Path} already exists, keeping it", target);
                    return;
                }

                Directory.CreateDirectory(target);
                foreach (var file in files)
                {
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
                }

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
        /// </summary>
        public static void MarkCurrent(string folder)
        {
            try
            {
                if (ReadMarker(folder) == AppVersion.Current) return;

                Directory.CreateDirectory(folder);
                File.WriteAllText(MarkerFile(folder), AppVersion.Current);
                Log.Information("Data files are now on version {Version}", AppVersion.Current);
            }
            catch (Exception e)
            {
                // Same stance as above. The cost of a lost marker is one superfluous
                // backup on the next start, not a lost file.
                Log.Warning(e, "Could not write the version marker {Path}", MarkerFile(folder));
            }
        }

        /// <summary>The version that wrote the current data, or <c>null</c> if nobody noted one.</summary>
        private static string? ReadMarker(string folder)
        {
            if (!File.Exists(MarkerFile(folder))) return null;
            var text = File.ReadAllText(MarkerFile(folder)).Trim();
            return text.Length == 0 ? null : text;
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
