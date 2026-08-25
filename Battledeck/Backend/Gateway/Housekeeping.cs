using System.IO;
using Serilog;

namespace Battledeck.Backend.Gateway
{
    /// <summary>
    ///     What the data folder keeps, and for how long. Everything this application ever
    ///     deletes on its own is decided here.
    ///     <para>
    ///         <b>Why one class and not a number at each site</b>: three different things grow
    ///         without bound - the log, the diagnostic captures and the backups - and each of
    ///         them was written by somebody solving a different problem. A retention policy
    ///         spread over three files is one nobody can state, and the question asked about
    ///         it is always the same one: "how much of my disk does this take". The answer
    ///         stands at the top of this file, in four constants.
    ///     </para>
    ///     <para>
    ///         <b>Nothing here is a safety net for the data itself.</b> <c>data.yaml</c>,
    ///         <c>settings.yaml</c> and their neighbours are never touched - the only files
    ///         that go are copies, captures and logs. The backups are the exception that
    ///         proves it: they hold copies of <c>data.yaml</c>, so they are capped by count
    ///         and never by age, and the newest is never the one that goes.
    ///     </para>
    ///     <para>
    ///         <b>Compressed as ZIP and not as GZip</b>, although a log is a single file and
    ///         GZip is made for exactly that. Windows Explorer opens a <c>.zip</c> on a double
    ///         click and offers nothing at all for a <c>.gz</c> - and whoever goes looking for
    ///         an old log is a human on this machine, not a script.
    ///     </para>
    /// </summary>
    public static class Housekeeping
    {
        /// <summary>
        ///     How many log files survive, the one being written included. Handed to Serilog
        ///     as <c>retainedFileCountLimit</c> and to <see cref="LogArchive" /> as the number
        ///     of archives it keeps beside the current file.
        /// </summary>
        internal const int LogsKept = 5;

        /// <summary>
        ///     When the log rolls. 10 MB is a good many days of ordinary use and about eight
        ///     game reads, each of which writes some 250 lines of what OCR delivered.
        /// </summary>
        internal const long LogSizeLimitBytes = 10L * 1024 * 1024;

        /// <summary>
        ///     How many captures of a stranded automation run survive. A full-screen PNG is
        ///     around 5 MB, so this is the number that decides the size of the folder - not
        ///     <see cref="ShotsMaxAge" />, which only takes what the count already spared.
        /// </summary>
        internal const int ShotsKept = 20;

        /// <summary>
        ///     Above this age a capture goes even when the count would have kept it. A
        ///     screenshot answers "was the calibration outdated or was the game slow" - a
        ///     month later nobody asks that question about that run any more.
        /// </summary>
        internal static readonly TimeSpan ShotsMaxAge = TimeSpan.FromDays(30);

        /// <summary>
        ///     How many version backups survive. Higher than the other two on purpose: each
        ///     one is some 10 KB compressed, and the one nobody kept is the one that would
        ///     have been needed.
        /// </summary>
        internal const int BackupsKept = 10;

        /// <summary>
        ///     Tidies the data folder. To be called <b>after</b>
        ///     <see cref="DataBackup.BeforeMigrations" />, so that the backup this start
        ///     wrote is already among the ones being counted.
        ///     <para>
        ///         Every step carries its own <c>try</c>. A folder that cannot be read is no
        ///         reason to skip the other two, and none of the three is a reason to refuse
        ///         the start: what fails here is housekeeping, and the application works
        ///         without it - only fuller.
        ///     </para>
        /// </summary>
        public static void Run(string folder)
        {
            Step("captures", () => PruneShots(Path.Combine(folder, "shots")));
            Step("backups", () => PruneBackups(DataBackup.BackupRoot(folder)));
            Step("log of the previous layout", () => ArchiveLegacyLog(folder));
        }

        private static void Step(string what, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                Log.Warning(e, "Housekeeping could not tidy the {What} - continuing", what);
            }
        }

        /// <summary>
        ///     Count first, age second - both applied to the same list, so a capture goes if
        ///     either says so. Sorted newest first, which is what makes the count a "keep the
        ///     newest 20" and not an arbitrary twenty.
        /// </summary>
        private static void PruneShots(string shots)
        {
            if (!Directory.Exists(shots)) return;

            var files = Named(shots, ".png")
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            var cutoff = DateTime.UtcNow - ShotsMaxAge;
            var doomed = files
                .Where((file, index) => index >= ShotsKept || file.LastWriteTimeUtc < cutoff)
                .ToList();

            foreach (var file in doomed) Delete(file);
            if (doomed.Count > 0)
                Log.Debug("Removed {Count} capture(s), {Kept} left",
                    doomed.Count, files.Count - doomed.Count);
        }

        /// <summary>
        ///     By count alone, newest first. No age limit here, and that is the difference to
        ///     the captures: a backup holds the account list of a version, and the age of a
        ///     version says nothing about whether somebody still needs to get back to it.
        /// </summary>
        private static void PruneBackups(string backups)
        {
            if (!Directory.Exists(backups)) return;

            var doomed = Named(backups, ".zip")
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(BackupsKept)
                .ToList();

            foreach (var file in doomed) Delete(file);
            if (doomed.Count > 0)
                Log.Information("Removed {Count} old backup(s), {Kept} kept",
                    doomed.Count, BackupsKept);
        }

        /// <summary>
        ///     Every installation up to 1.2.0 wrote <c>smurftown.log</c> flat into the data
        ///     folder; from the next one the logs live in <c>logs/</c>. The leftover is
        ///     compressed into the new folder rather than deleted - it is the log of the run
        ///     that installed the update, which is the one worth having if the update went
        ///     wrong.
        ///     <para>
        ///         Safe at this point because the sink of the running process writes into
        ///         <c>logs/</c> and holds no handle on the old path.
        ///     </para>
        /// </summary>
        private static void ArchiveLegacyLog(string folder)
        {
            var legacy = Path.Combine(folder, "smurftown.log");
            if (!File.Exists(legacy)) return;

            var target = Path.Combine(folder, LogArchive.FolderName, "smurftown-previous-layout.log.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (File.Exists(target))
            {
                // A second start after a delete that did not go through. Compressing again
                // would only overwrite the same content with itself.
                Delete(new FileInfo(legacy));
                return;
            }

            LogArchive.CompressInto(legacy, target);
            Delete(new FileInfo(legacy));
            Log.Information("Kept the log of the previous layout as {Path}", target);
        }

        /// <summary>
        ///     Everything in <paramref name="folder" /> whose name ends in
        ///     <paramref name="suffix" /> - matched here and not handed to
        ///     <see cref="Directory.EnumerateFiles(string,string)" /> as a search pattern.
        ///     <para>
        ///         <b>Because that pattern lies on Windows.</b> A pattern whose extension is
        ///         exactly three characters also returns files whose extension merely
        ///         <i>starts</i> that way - documented behaviour, inherited from 8.3 names,
        ///         and the reason <c>*.xls</c> famously returns <c>book.xlsx</c>. Every
        ///         suffix in play here is three characters long, so <c>*.log</c> would be
        ///         able to return <c>smurftown.log.zip</c> - and
        ///         <see cref="LogArchive.Sweep" /> would then compress an archive into an
        ///         archive and delete the original.
        ///     </para>
        /// </summary>
        internal static IEnumerable<string> Named(string folder, string suffix) =>
            Directory
                .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        ///     A file held open by something else is skipped, not fought over. The next start
        ///     finds it again, and by then whatever held it is usually gone.
        /// </summary>
        private static void Delete(FileInfo file)
        {
            try
            {
                file.Delete();
            }
            catch (IOException e)
            {
                Log.Debug(e, "{Path} is in use, leaving it for the next start", file.FullName);
            }
        }
    }
}
