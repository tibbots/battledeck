using System.IO;
using System.IO.Compression;
using Serilog;

namespace Battledeck.Backend.Gateway
{
    // An allow-list of two folders, never an exclusion from the data folder: data.yaml and
    // backups/ hold the passwords, and a file added beside them must not slip in by default.
    public static class SupportBundle
    {
        internal const string FolderName = "support";
        internal const string ShotsFolder = "shots";

        internal static string Root(string folder) => Path.Combine(folder, FolderName);

        // A file that cannot be read fails the whole bundle: one that silently lacks the
        // current log is worse than none.
        public static string Create(string folder)
        {
            var cutoff = DateTime.UtcNow - Housekeeping.ShotsMaxAge;
            var target = Path.Combine(Root(folder), $"Battledeck-support-{DateTime.Now:yyyy-MM-dd HH.mm.ss}.zip");
            Directory.CreateDirectory(Root(folder));

            var partial = target + ".partial";
            if (File.Exists(partial)) File.Delete(partial);

            var count = 0;
            try
            {
                using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
                {
                    foreach (var name in new[] { LogArchive.FolderName, ShotsFolder })
                    foreach (var file in Recent(Path.Combine(folder, name), cutoff))
                    {
                        Add(zip, file, name);
                        count++;
                    }
                }

                File.Move(partial, target, true);
            }
            catch
            {
                // The name carries the time, so no later attempt would come past this file again.
                try
                {
                    File.Delete(partial);
                }
                catch (IOException)
                {
                }

                throw;
            }

            Log.Information("Wrote support bundle {Path} with {Count} file(s)", target, count);
            return target;
        }

        private static List<FileInfo> Recent(string folder, DateTime cutoff)
        {
            if (!Directory.Exists(folder)) return [];

            // A fresh FileInfo per path: the stamp a directory listing carries lags behind for a
            // file that is still open, and the log being written is exactly that file.
            return Directory
                .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .Where(file => file.LastWriteTimeUtc >= cutoff)
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void Add(ZipArchive zip, FileInfo file, string entryFolder)
        {
            // ReadWrite, because the file sink holds the current log open for writing and a
            // reader that does not grant that is refused - CreateEntryFromFile is such a reader.
            using var source = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            var entry = zip.CreateEntry($"{entryFolder}/{file.Name}", CompressionLevel.Optimal);
            entry.LastWriteTime = file.LastWriteTime;

            using var destination = entry.Open();
            source.CopyTo(destination);
        }
    }
}
