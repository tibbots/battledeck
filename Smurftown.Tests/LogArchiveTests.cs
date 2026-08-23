using System.IO;
using System.IO.Compression;
using Smurftown.Backend.Gateway;
using Xunit;

namespace Smurftown.Tests
{
    /// <summary>
    ///     The sweep that runs whenever the file sink opens a log file.
    ///     <para>
    ///         <b>Called directly and not through Serilog.</b> Driving a real roll would mean
    ///         writing ten megabytes per case; the hook is a method taking the path of the
    ///         file just opened, and that is what is handed to it here. What the test cannot
    ///         show is that Serilog closes the previous file before it opens the next - that
    ///         one is read out of the sink's own documentation and stated in
    ///         <see cref="LogArchive" />.
    ///     </para>
    /// </summary>
    public class LogArchiveTests
    {
        [Fact]
        public void A_log_that_is_no_longer_written_to_becomes_an_archive()
        {
            var folder = FreshFolder();
            var current = Log(folder, "smurftown.log", "the one being written");
            Log(folder, "smurftown_001.log", "the one that rolled");

            LogArchive.Sweep(current);

            Assert.False(File.Exists(Path.Combine(folder, "smurftown_001.log")));

            using var zip = ZipFile.OpenRead(Path.Combine(folder, "smurftown_001.log.zip"));
            using var reader = new StreamReader(zip.GetEntry("smurftown_001.log")!.Open());
            Assert.Equal("the one that rolled", reader.ReadToEnd());
        }

        [Fact]
        public void The_file_being_written_is_left_alone()
        {
            var folder = FreshFolder();
            var current = Log(folder, "smurftown.log", "still open");

            LogArchive.Sweep(current);

            // It is the one file in the folder with a handle on it. Compressing it would
            // either fail or produce an archive of half a log.
            Assert.True(File.Exists(current));
            Assert.False(File.Exists(current + ".zip"));
        }

        [Fact]
        public void An_archive_is_never_swept_into_another_archive()
        {
            var folder = FreshFolder();
            var current = Log(folder, "smurftown.log", "current");
            var archive = Path.Combine(folder, "smurftown_001.log.zip");
            LogArchive.CompressInto(Log(folder, "smurftown_001.log", "rolled"), archive);
            File.Delete(Path.Combine(folder, "smurftown_001.log"));

            LogArchive.Sweep(current);

            // The trap this guards is a Windows one: a search pattern of "*.log" also
            // returns names whose extension merely starts that way, so an archive could be
            // picked up as a log, packed again and its original deleted.
            Assert.False(File.Exists(archive + ".zip"));
            Assert.Equal(["smurftown_001.log"], EntryNames(archive));
        }

        [Fact]
        public void Only_as_many_archives_survive_as_fit_beside_the_current_file()
        {
            var folder = FreshFolder();
            var current = Log(folder, "smurftown.log", "current");
            var written = Housekeeping.LogsKept + 2;
            for (var i = 0; i < written; i++)
                Stamp(Path.Combine(folder, $"smurftown_{i:000}.log.zip"), TimeSpan.FromMinutes(i));

            LogArchive.Sweep(current);

            // One fewer than the limit, because the file being written counts towards it.
            var left = Archives(folder);
            Assert.Equal(Housekeeping.LogsKept - 1, left.Length);
            Assert.Contains($"smurftown_{written - 1:000}.log.zip", left);
            Assert.DoesNotContain("smurftown_000.log.zip", left);
        }

        [Fact]
        public void A_half_written_archive_does_not_block_the_next_attempt()
        {
            var folder = FreshFolder();
            var current = Log(folder, "smurftown.log", "current");
            Log(folder, "smurftown_001.log", "rolled");

            // What a process killed mid-write leaves behind. Under the final name this
            // would be a truncated ZIP that every later start walks around; under
            // ".partial" it is one delete.
            File.WriteAllText(Path.Combine(folder, "smurftown_001.log.zip.partial"), "half a zip");

            LogArchive.Sweep(current);

            Assert.False(File.Exists(Path.Combine(folder, "smurftown_001.log.zip.partial")));
            Assert.Equal(["smurftown_001.log"], EntryNames(Path.Combine(folder, "smurftown_001.log.zip")));
        }

        [Fact]
        public void A_log_left_over_from_a_crash_is_picked_up_at_the_next_start()
        {
            var folder = FreshFolder();
            Log(folder, "smurftown_001.log", "written before the process died");

            // The next start opens a fresh file and thus lands in the sweep - this is the
            // whole recovery path, and it needs no separate step anywhere.
            var current = Log(folder, "smurftown.log", "the new run");
            LogArchive.Sweep(current);

            Assert.True(File.Exists(Path.Combine(folder, "smurftown_001.log.zip")));
        }

        private static string[] Archives(string folder) =>
            Directory
                .GetFiles(folder)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => name.EndsWith(".log.zip"))
                .ToArray();

        private static string[] EntryNames(string archive)
        {
            using var zip = ZipFile.OpenRead(archive);
            return zip.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToArray();
        }

        private static string Log(string folder, string name, string content)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, content);
            return path;
        }

        private static void Stamp(string path, TimeSpan offset)
        {
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow + offset);
        }

        private static string FreshFolder()
        {
            var folder = Path.Combine(TestHome.Path, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
