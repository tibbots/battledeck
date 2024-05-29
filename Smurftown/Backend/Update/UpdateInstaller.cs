using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Serilog;
using Smurftown.Backend.Texts;

namespace Smurftown.Backend.Update
{
    /// <summary>
    ///     What this build is allowed to do with an update it has found.
    /// </summary>
    public enum InstallRoute
    {
        /// <summary>Download it, check it, put it in place, restart.</summary>
        Replace,

        /// <summary>
        ///     A build out of the IDE, not a released single-file <c>.exe</c>. Replacing it
        ///     would overwrite a developer's build output with a release - and lose the very
        ///     change they are testing.
        /// </summary>
        DevBuild,

        /// <summary>
        ///     The folder does not take a write. That is the installation under
        ///     <c>C:\Program Files\ZrdJ\Smurftown</c> the old MSI produced - it needs
        ///     administrator rights, and this application deliberately runs as
        ///     <c>asInvoker</c> (<c>app.manifest</c>).
        /// </summary>
        NotWritable,

        /// <summary>No path to our own executable. Should not happen; not worth a crash.</summary>
        Unknown
    }

    /// <summary>
    ///     One step of an installation, as the human sees it: a line of text, and how far
    ///     along it is.
    ///     <para>
    ///         <b>The number exists because the text cannot be taken apart.</b> The display
    ///         fills a bar as the download runs, and reading the percentage back out of
    ///         "Downloading 42%" would mean parsing a translated string - a construction
    ///         that works in English and breaks in whatever language writes its numbers
    ///         differently.
    ///     </para>
    ///     <para>
    ///         <b><see cref="Fraction" /> is -1 where there is nothing to measure.</b>
    ///         Checking the hash and swapping the file take the time they take; a bar that
    ///         guesses at them would be a lie told in pixels. The display shows the text
    ///         alone for those.
    ///     </para>
    /// </summary>
    public readonly record struct UpdateProgress(string Text, double Fraction)
    {
        /// <summary>A step whose length is unknown - see the type.</summary>
        public static UpdateProgress Step(string text)
        {
            return new UpdateProgress(text, -1);
        }
    }


    /// <summary>
    ///     Fetches a release and puts it where the running one stands.
    ///     <para>
    ///         <b>This is one file move, and only because of how the release is built.</b>
    ///         <c>dev release</c> publishes single-file and framework-dependent: the whole
    ///         application is <c>Smurftown.exe</c>, there is no set of DLLs beside it that
    ///         would have to be swapped consistently. That removes the entire class of
    ///         half-updated installations - either the new <c>.exe</c> is in place or the old
    ///         one still is.
    ///     </para>
    ///     <para>
    ///         <b>Nothing here is signed</b>, and no amount of code makes it so - see
    ///         <c>Setup.vdproj</c>, which stands on <c>SignOutput = FALSE</c> with an empty
    ///         certificate. What is verified is that the ZIP arrived intact: its SHA-256 is
    ///         held against the <c>checksums.txt</c> of the same release. Both come over
    ///         HTTPS from github.com, so the trust anchor is that connection and the account
    ///         behind the repository - not a signature on the file. Whoever expects more from
    ///         this check than "the download is not corrupt and not swapped in flight" expects
    ///         the wrong thing.
    ///     </para>
    /// </summary>
    public static class UpdateInstaller
    {
        /// <summary>
        ///     The name the application carries in a release ZIP <b>and</b> on disk. Both
        ///     sides of the swap are this file and nothing else.
        /// </summary>
        private const string ExeName = "Smurftown.exe";

        /// <summary>
        ///     What the running <c>.exe</c> is renamed to before the new one takes its place.
        ///     <para>
        ///         Windows lets a running executable be <b>renamed</b> but not deleted, and
        ///         that single fact is what makes this whole procedure possible without a
        ///         helper process, a scheduled task or a batch file that outlives us.
        ///     </para>
        /// </summary>
        private const string PreviousSuffix = ".old";

        /// <summary>
        ///     Whether this build can replace itself, and if not, why not. Asked before the
        ///     button is drawn - the human should see what a click will do, rather than find
        ///     out once it has failed.
        /// </summary>
        public static InstallRoute Route()
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return InstallRoute.Unknown;

            var folder = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(folder)) return InstallRoute.Unknown;

            // A single-file publish carries the managed assembly INSIDE the .exe. A
            // Smurftown.dll lying beside it is therefore a build out of bin\Debug or
            // bin\Release - measured, the Debug folder holds exactly that plus nineteen
            // more DLLs. Replacing that .exe with a release would throw away the build
            // whoever is sitting there is currently testing.
            if (File.Exists(Path.Combine(folder, "Smurftown.dll"))) return InstallRoute.DevBuild;

            return Writable(folder) ? InstallRoute.Replace : InstallRoute.NotWritable;
        }

        /// <summary>
        ///     Downloads the release, verifies it, puts it in place. Returns the path the
        ///     caller has to start - the same path that was running, now carrying the new
        ///     build.
        ///     <para>
        ///         <b>It does not start anything and it does not close anything.</b> Both are
        ///         the caller's job, because both are WPF (<c>Application.Shutdown</c>), and
        ///         <c>Backend/</c> does not know <c>UI/</c>.
        ///     </para>
        ///     <para>
        ///         <b>Throws</b>, unlike <see cref="GithubReleases.Latest" /> next door. The
        ///         difference is who asked: a check nobody requested may fail in silence, a
        ///         button somebody pressed may not.
        ///     </para>
        /// </summary>
        public static async Task<string> Install(
            GithubRelease release, IProgress<UpdateProgress>? progress, CancellationToken cancel)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
                throw new InvalidOperationException("the path of the running executable is unknown");

            var package = release.Package
                          ?? throw new InvalidOperationException(
                              $"release {release.Version} does not carry exactly one ZIP asset");
            var checksums = release.Checksums
                            ?? throw new InvalidOperationException(
                                $"release {release.Version} carries no checksums.txt");

            // A fresh folder per run, and emptied rather than reused: a half-finished
            // download from an aborted attempt would otherwise be taken for a complete one
            // by the next - and it would fail the hash check, which is the right outcome by
            // accident rather than by design.
            var work = Path.Combine(Path.GetTempPath(), "smurftown-update");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);

            var zip = Path.Combine(work, package.Name);
            await Download(package, zip, progress, cancel);

            progress?.Report(UpdateProgress.Step(Strings.Current["update.verifying"]));
            await Verify(zip, checksums, package.Name, cancel);

            progress?.Report(UpdateProgress.Step(Strings.Current["update.installing"]));
            var staged = Extract(zip, work);
            Swap(exe, staged);

            Log.Information("Update {Version} installed over {Exe}", release.Version, exe);
            return exe;
        }

        /// <summary>
        ///     Removes what the last update left behind. Called once at startup.
        ///     <para>
        ///         <b>A failure here is expected, not exceptional.</b> The process this file
        ///         belonged to started us and is on its way out; until it is gone, Windows
        ///         holds the image and refuses the delete. The next start finds it free. That
        ///         is why this swallows instead of reporting - the alternative would be a
        ///         warning in the log of every single update, describing a state that repairs
        ///         itself.
        ///     </para>
        /// </summary>
        public static void CleanUpPrevious()
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            var previous = exe + PreviousSuffix;
            if (!File.Exists(previous)) return;

            try
            {
                File.Delete(previous);
                Log.Information("Removed the previous build {Path}", previous);
            }
            catch (Exception e)
            {
                Log.Debug(e, "The previous build is still held, leaving it for the next start: {Path}",
                    previous);
            }
        }

        private static async Task Download(
            GithubAsset asset, string target, IProgress<UpdateProgress>? progress, CancellationToken cancel)
        {
            Log.Information("Downloading {Name} ({Size} bytes)", asset.Name, asset.Size);

            using var response = await GithubReleases.Http.GetAsync(
                asset.Url, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();

            // The asset size out of the release, and the header only as a fallback: with
            // neither of the two there is no percentage, and the display then says so
            // rather than counting up to a made-up total.
            var total = asset.Size > 0 ? asset.Size : response.Content.Headers.ContentLength ?? 0;

            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var file = File.Create(target);

            var buffer = new byte[81920];
            long done = 0;
            var lastReported = -1;

            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;

                if (total <= 0) continue;

                // Only on a changed whole percent. Without that this reports on every 80 KB
                // block - around 430 times for a 34 MB package - and each one of those is a
                // property change that WPF has to render.
                var percent = (int)(done * 100 / total);
                if (percent == lastReported) continue;

                lastReported = percent;
                progress?.Report(new UpdateProgress(
                    Strings.Format("update.downloading", percent), percent / 100.0));
            }
        }

        /// <summary>
        ///     Holds the downloaded ZIP against the checksum list of the same release.
        /// </summary>
        private static async Task Verify(
            string zip, GithubAsset checksums, string name, CancellationToken cancel)
        {
            var list = await GithubReleases.Http.GetStringAsync(checksums.Url, cancel);
            var expected = HashFor(list, name)
                           ?? throw new InvalidOperationException(
                               $"checksums.txt has no line for {name}");

            var actual = await Sha256(zip, cancel);

            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"checksum mismatch for {name}: expected {expected}, got {actual}");

            Log.Information("Checksum verified for {Name}", name);
        }

        /// <summary>
        ///     Pulls one hash out of the output of <c>sha256sum</c>.
        ///     <para>
        ///         The format is <c>hash</c>, two spaces, file name - and <c>hash</c>, space,
        ///         <c>*</c>, file name when the tool ran in binary mode. Split on whitespace
        ///         and strip a leading star, and both forms read the same; pinning the exact
        ///         two spaces would break on a change nobody would connect to this.
        ///     </para>
        /// </summary>
        private static string? HashFor(string list, string name)
        {
            foreach (var line in list.Split('\n'))
            {
                var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                if (parts[1].TrimStart('*').Equals(name, StringComparison.OrdinalIgnoreCase))
                    return parts[0];
            }

            return null;
        }

        private static async Task<string> Sha256(string path, CancellationToken cancel)
        {
            await using var stream = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(stream, cancel);
            return Convert.ToHexString(hash);
        }

        /// <summary>
        ///     Takes the application out of the ZIP.
        ///     <para>
        ///         <b>Only <c>Smurftown.exe</c>.</b> The package also carries the
        ///         <c>README.md</c> that <c>dev</c> stages beside it; that is a copy of the
        ///         landing page and not part of the installation. Unpacking the archive
        ///         wholesale would put files into a folder the human chose, and would do it
        ///         from a decision made in <c>cmd_release</c>.
        ///     </para>
        /// </summary>
        private static string Extract(string zip, string work)
        {
            using var archive = ZipFile.OpenRead(zip);

            var entry = archive.Entries.FirstOrDefault(
                            e => e.Name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"the package holds no {ExeName}");

            var staged = Path.Combine(work, ExeName);
            entry.ExtractToFile(staged, true);
            return staged;
        }

        /// <summary>
        ///     Puts the new build where the running one stands.
        ///     <para>
        ///         <b>The order matters and the rollback is the point.</b> Between the two
        ///         moves there is a moment in which no <c>Smurftown.exe</c> exists; if the
        ///         second one fails - a virus scanner holding the fresh file, a full disk -
        ///         and nothing put the old one back, the human would be left with an
        ///         application that is simply gone. So the failure path moves it back and only
        ///         then lets the exception fly.
        ///     </para>
        /// </summary>
        private static void Swap(string exe, string staged)
        {
            var previous = exe + PreviousSuffix;

            // A leftover from an earlier update that the startup cleanup could not remove.
            // It has to go: File.Move onto an existing path throws.
            if (File.Exists(previous)) File.Delete(previous);

            File.Move(exe, previous);

            try
            {
                File.Move(staged, exe);
            }
            catch
            {
                File.Move(previous, exe);
                throw;
            }
        }

        /// <summary>
        ///     Does this folder take a write? Asked by writing, because there is no reliable
        ///     way to ask: an ACL check answers the question for the account, not for the
        ///     process - UAC virtualisation, a read-only attribute and a locked volume all sit
        ///     between the two.
        /// </summary>
        private static bool Writable(string folder)
        {
            var probe = Path.Combine(folder, $"smurftown-write-probe-{Guid.NewGuid():N}");

            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception e)
            {
                Log.Debug(e, "The installation folder does not take a write: {Folder}", folder);
                return false;
            }
        }
    }
}
