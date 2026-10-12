using System.IO;
using System.IO.Compression;
using Battledeck.Backend.Gateway;
using Xunit;

namespace Battledeck.Tests
{
    public class SupportBundleTests
    {
        [Fact]
        public void Recent_logs_and_captures_land_under_their_own_entry_folders()
        {
            var folder = FreshFolder();
            Stamp(folder, "logs/smurftown.log", -TimeSpan.FromHours(1));
            Stamp(folder, "logs/smurftown_001.log.zip", -TimeSpan.FromDays(2));
            Stamp(folder, "shots/tab-not-found.png", -TimeSpan.FromDays(6));

            var bundle = SupportBundle.Create(folder);

            Assert.Equal(
                ["logs/smurftown.log", "logs/smurftown_001.log.zip", "shots/tab-not-found.png"],
                EntryNames(bundle));
        }

        [Fact]
        public void The_bundle_is_written_into_the_support_folder_under_its_dated_name()
        {
            var folder = FreshFolder();

            var bundle = SupportBundle.Create(folder);

            Assert.Equal(Path.Combine(folder, "support"), Path.GetDirectoryName(bundle));
            Assert.Matches(
                @"^Battledeck-support-\d{4}-\d{2}-\d{2} \d{2}\.\d{2}\.\d{2}\.zip$",
                Path.GetFileName(bundle));
            Assert.Equal([bundle], Directory.GetFiles(Path.Combine(folder, "support")));
        }

        [Fact]
        public void Files_past_the_age_stay_out()
        {
            var folder = FreshFolder();
            var past = -(Housekeeping.ShotsMaxAge + TimeSpan.FromHours(1));
            var inside = -(Housekeeping.ShotsMaxAge - TimeSpan.FromHours(1));
            Stamp(folder, "logs/old.log.zip", past);
            Stamp(folder, "logs/kept.log.zip", inside);
            Stamp(folder, "shots/old.png", past);
            Stamp(folder, "shots/kept.png", inside);

            var bundle = SupportBundle.Create(folder);

            Assert.Equal(["logs/kept.log.zip", "shots/kept.png"], EntryNames(bundle));
        }

        [Fact]
        public void Nothing_but_logs_and_captures_goes_in()
        {
            var folder = FreshFolder();
            Stamp(folder, "data.yaml", TimeSpan.Zero);
            Stamp(folder, "settings.yaml", TimeSpan.Zero);
            Stamp(folder, "app.yaml", TimeSpan.Zero);
            Stamp(folder, "backups/x.zip", TimeSpan.Zero);
            Stamp(folder, "support/Battledeck-support-2026-01-01 10.00.00.zip", TimeSpan.Zero);
            Stamp(folder, "logs/smurftown.log", TimeSpan.Zero);

            var bundle = SupportBundle.Create(folder);

            Assert.Equal(["logs/smurftown.log"], EntryNames(bundle));
        }

        [Fact]
        public void The_log_the_sink_is_still_writing_is_bundled()
        {
            var folder = FreshFolder();
            var path = Path.Combine(folder, "logs", "smurftown.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Mode, access and sharing as Serilog's FileSink opens its file.
            using var sink = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(sink);
            writer.Write("still being written");
            writer.Flush();

            var bundle = SupportBundle.Create(folder);

            using var zip = ZipFile.OpenRead(bundle);
            using var reader = new StreamReader(zip.GetEntry("logs/smurftown.log")!.Open());
            Assert.Equal("still being written", reader.ReadToEnd());
        }

        [Fact]
        public void A_file_that_cannot_be_read_fails_the_bundle_and_leaves_nothing_behind()
        {
            var folder = FreshFolder();
            Stamp(folder, "logs/smurftown.log", TimeSpan.Zero);
            using var locked = File.Open(
                Path.Combine(folder, "logs", "smurftown.log"),
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None
            );

            Assert.Throws<IOException>(() => SupportBundle.Create(folder));

            Assert.Empty(Directory.GetFiles(SupportBundle.Root(folder)));
        }

        [Fact]
        public void Without_logs_and_captures_the_bundle_is_a_valid_empty_archive()
        {
            var bundle = SupportBundle.Create(FreshFolder());

            Assert.Empty(EntryNames(bundle));
        }

        private static string[] EntryNames(string archive)
        {
            using var zip = ZipFile.OpenRead(archive);
            return zip.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        private static void Stamp(string folder, string relative, TimeSpan offset)
        {
            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
