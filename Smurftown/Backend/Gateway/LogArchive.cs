using System.IO;
using System.IO.Compression;
using System.Text;
using Serilog.Sinks.File;

namespace Smurftown.Backend.Gateway
{
    /// <summary>
    ///     Compresses a log file the moment it stops being the one written to, and keeps the
    ///     number of archives beside it at <see cref="Housekeeping.LogsKept" /> minus the
    ///     current one.
    ///     <para>
    ///         <b>Why a hook and not a step at startup</b>: the sink rolls whenever 10 MB are
    ///         full, which can happen several times inside one long session. A tidy-up that
    ///         only runs at startup would leave those files lying uncompressed for as long as
    ///         the window stays open - and the session that produces them is exactly the one
    ///         somebody is trying to debug.
    ///     </para>
    ///     <para>
    ///         <b>Serilog calls <see cref="OnFileOpened" /> after the previous file is
    ///         closed</b>, so the file being compressed here is never one that is still being
    ///         written. The one just opened is passed in and is skipped by name - it is the
    ///         only file in the folder that has a handle on it.
    ///     </para>
    ///     <para>
    ///         <b>Nothing in here logs.</b> It runs inside the sink while the logger is being
    ///         built or a roll is under way; a <c>Log.Warning</c> from here would re-enter the
    ///         very sink that is mid-open. A failure therefore stays silent and the file stays
    ///         uncompressed - the next roll finds it again, and the size limit that Serilog
    ///         enforces itself is untouched either way.
    ///     </para>
    /// </summary>
    public sealed class LogArchive : FileLifecycleHooks
    {
        /// <summary>The folder under the data folder that holds the log and its archives.</summary>
        internal const string FolderName = "logs";

        /// <summary>The name the sink writes to; everything else beside it is an old one.</summary>
        internal const string FileName = "smurftown.log";

        public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
        {
            try
            {
                Sweep(path);
            }
            catch
            {
                // See the class comment: no logging from in here, and a failed sweep costs a
                // few megabytes, not a log line.
            }

            return base.OnFileOpened(path, underlyingStream, encoding);
        }

        /// <summary>
        ///     Compresses every <c>*.log</c> beside <paramref name="current" /> and then caps
        ///     the archives. Also the recovery path after a crash: a file that was rolled but
        ///     never compressed is picked up at the next start, because that start opens a
        ///     file and thus lands here.
        /// </summary>
        internal static void Sweep(string current)
        {
            var folder = Path.GetDirectoryName(current);
            if (folder == null || !Directory.Exists(folder)) return;

            foreach (var rolled in Named(folder, ".log"))
            {
                if (string.Equals(rolled, current, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    CompressInto(rolled, rolled + ".zip");
                    File.Delete(rolled);
                }
                catch (IOException)
                {
                    // Held by something - another instance, a virus scanner, an editor.
                    // Leave it; the next roll comes past here again.
                }
            }

            Prune(folder);
        }

        /// <summary>
        ///     Writes <paramref name="source" /> into a ZIP at <paramref name="target" />,
        ///     under its own file name.
        ///     <para>
        ///         <b>Through a <c>.partial</c> and a move</b>, because the alternative fails
        ///         permanently: a process that dies mid-write leaves a truncated
        ///         <c>.zip</c>, and the next attempt would find the target already there and
        ///         refuse. A leftover <c>.partial</c> costs one delete instead.
        ///     </para>
        /// </summary>
        internal static void CompressInto(string source, string target)
        {
            var partial = target + ".partial";
            if (File.Exists(partial)) File.Delete(partial);

            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(source, Path.GetFileName(source), CompressionLevel.Optimal);
            }

            File.Move(partial, target, true);
        }

        /// <summary>
        ///     Keeps the newest archives and drops the rest. One fewer than
        ///     <see cref="Housekeeping.LogsKept" />, because the file currently being written
        ///     counts towards that number as well.
        /// </summary>
        private static void Prune(string folder)
        {
            var doomed = Named(folder, ".log.zip")
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Housekeeping.LogsKept - 1)
                .ToList();

            foreach (var file in doomed)
            {
                try
                {
                    file.Delete();
                }
                catch (IOException)
                {
                    // Same stance as above.
                }
            }
        }

        private static IEnumerable<string> Named(string folder, string suffix) =>
            Housekeeping.Named(folder, suffix);
    }
}
