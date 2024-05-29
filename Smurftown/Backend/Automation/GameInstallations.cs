using System.IO;
using Serilog;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     Finds installations of Heroes of the Storm on this machine.
    ///     <para>
    ///         Searched for is <c>Support64\HeroesSwitcher_x64.exe</c> and not the <c>.exe</c> in
    ///         the root: according to the manifest that one is a setup bootstrapper, and the path
    ///         <c>Versions\BaseNNNNN\</c> moves with every patch. Same reasoning as in
    ///         <see cref="GameWindow" />.
    ///     </para>
    /// </summary>
    public static class GameInstallations
    {
        private const string Relative = @"Support64\HeroesSwitcher_x64.exe";

        /// <summary>
        ///     Folder names under which there is nothing to find. Skipping them individually saves
        ///     more time during the full scan than any other measure - <c>C:\Windows</c> alone is
        ///     over a hundred thousand directories, and no game lies in any of them.
        /// </summary>
        private static readonly string[] SkipNames =
        [
            "windows", "$recycle.bin", "system volume information", "msocache",
            "recovery", "perflogs", "appdata", "node_modules", ".git"
        ];

        /// <summary>
        ///     The usual locations, checked in fractions of a second. Covers every installation
        ///     that accepted the installer's suggestion.
        /// </summary>
        public static IReadOnlyList<string> Likely()
        {
            var roots = new List<string>();

            foreach (var folder in new[]
                     {
                         Environment.SpecialFolder.ProgramFilesX86,
                         Environment.SpecialFolder.ProgramFiles
                     })
            {
                var basePath = Environment.GetFolderPath(folder);
                if (basePath.Length > 0) roots.Add(Path.Combine(basePath, "Heroes of the Storm"));
            }

            // On a second drive, the installer likes to place things directly in the root
            // or under "Games" - both common enough to pick up before the full scan.
            foreach (var drive in FixedDrives())
            {
                roots.Add(Path.Combine(drive, "Heroes of the Storm"));
                roots.Add(Path.Combine(drive, "Games", "Heroes of the Storm"));
                roots.Add(Path.Combine(drive, "Program Files (x86)", "Heroes of the Storm"));
            }

            return roots
                .Select(root => Path.Combine(root, Relative))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        ///     Search all fixed drives. <b>Expensive</b> - minutes depending on drive size,
        ///     and therefore only on explicit request and in the background.
        ///     <para>
        ///         <paramref name="progress" /> reports the folder currently being processed. Without
        ///         this feedback, a full scan looks like a frozen application.
        ///     </para>
        /// </summary>
        public static IReadOnlyList<string> ScanAll(IProgress<string>? progress, CancellationToken token)
        {
            var found = new List<string>();

            foreach (var drive in FixedDrives())
            {
                token.ThrowIfCancellationRequested();
                progress?.Report(drive);
                Walk(drive, found, progress, token, 0);
            }

            Log.Information("Full scan finished: {Count} installations found", found.Count);
            return found;
        }

        private static void Walk(string directory, List<string> found, IProgress<string>? progress,
            CancellationToken token, int depth)
        {
            token.ThrowIfCancellationRequested();

            // An installation is never twelve levels deep. The limit protects against
            // directory loops via junctions, which would otherwise run forever.
            if (depth > 12) return;

            var candidate = Path.Combine(directory, Relative);
            if (File.Exists(candidate))
            {
                Log.Information("Installation found: {Path}", candidate);
                found.Add(candidate);
                // Do not go in further: below an installation there is no second one.
                return;
            }

            string[] children;
            try
            {
                // IgnoreInaccessible swallows the folders this user is not allowed into -
                // without that, a full scan aborts at the first protected system folder.
                children = Directory.GetDirectories(directory, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child).ToLowerInvariant();
                if (SkipNames.Contains(name)) continue;

                if (depth <= 1) progress?.Report(child);
                Walk(child, found, progress, token, depth + 1);
            }
        }

        private static IEnumerable<string> FixedDrives()
        {
            return DriveInfo.GetDrives()
                .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                .Select(drive => drive.RootDirectory.FullName);
        }
    }
}
