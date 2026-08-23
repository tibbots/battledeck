using System.IO;
using System.IO.Compression;
using Smurftown.Backend.Gateway;
using Xunit;

namespace Smurftown.Tests
{
    /// <summary>
    ///     What the data folder keeps and what it drops.
    ///     <para>
    ///         <b>Every case reads its bound from the constant</b> rather than restating it.
    ///         A test that writes 20 as its own literal keeps passing after somebody lowers
    ///         <see cref="Housekeeping.ShotsKept" /> to five - it would then be testing a
    ///         number that exists nowhere else.
    ///     </para>
    ///     <para>
    ///         Age is set through <c>LastWriteTimeUtc</c> and not by waiting. The policy is
    ///         written against that stamp, so a test that sets it is testing the same thing
    ///         the application reads.
    ///     </para>
    /// </summary>
    public class HousekeepingTests
    {
        [Fact]
        public void The_newest_captures_survive_and_the_rest_go()
        {
            var folder = FreshFolder();
            var extra = 5;
            for (var i = 0; i < Housekeeping.ShotsKept + extra; i++)
                Shot(folder, $"shot-{i:00}.png", TimeSpan.FromMinutes(i));

            Housekeeping.Run(folder);

            var left = Shots(folder);
            Assert.Equal(Housekeeping.ShotsKept, left.Length);

            // Written oldest first, so the highest indices are the newest and the ones that
            // have to be there.
            Assert.Contains($"shot-{Housekeeping.ShotsKept + extra - 1:00}.png", left);
            Assert.DoesNotContain("shot-00.png", left);
        }

        [Fact]
        public void A_capture_past_its_age_goes_even_when_there_is_room()
        {
            var folder = FreshFolder();
            Shot(folder, "yesterday.png", -TimeSpan.FromDays(1));
            Shot(folder, "ancient.png", -(Housekeeping.ShotsMaxAge + TimeSpan.FromDays(1)));

            Housekeeping.Run(folder);

            Assert.Equal(["yesterday.png"], Shots(folder));
        }

        [Fact]
        public void Captures_are_left_alone_while_they_fit()
        {
            var folder = FreshFolder();
            Shot(folder, "one.png", TimeSpan.Zero);
            Shot(folder, "two.png", TimeSpan.FromMinutes(1));

            Housekeeping.Run(folder);

            Assert.Equal(2, Shots(folder).Length);
        }

        [Fact]
        public void Only_the_newest_backups_survive()
        {
            var folder = FreshFolder();
            var root = DataBackup.BackupRoot(folder);
            Directory.CreateDirectory(root);
            for (var i = 0; i < Housekeeping.BackupsKept + 2; i++)
                Stamp(Path.Combine(root, $"1.0.{i}.zip"), TimeSpan.FromMinutes(i));

            Housekeeping.Run(folder);

            var left = Directory.GetFiles(root).Select(Path.GetFileName).ToArray();
            Assert.Equal(Housekeeping.BackupsKept, left.Length);
            Assert.DoesNotContain("1.0.0.zip", left);
            Assert.Contains($"1.0.{Housekeeping.BackupsKept + 1}.zip", left);
        }

        [Fact]
        public void A_backup_is_never_dropped_for_being_old()
        {
            var folder = FreshFolder();
            var root = DataBackup.BackupRoot(folder);
            Directory.CreateDirectory(root);

            // Older than any capture would be allowed to get. A backup holds the account
            // list of a version, and the age of a version says nothing about whether
            // somebody still needs to get back to it.
            Stamp(Path.Combine(root, "1.0.0.zip"), -(Housekeeping.ShotsMaxAge + TimeSpan.FromDays(400)));

            Housekeeping.Run(folder);

            Assert.True(File.Exists(Path.Combine(root, "1.0.0.zip")));
        }

        [Fact]
        public void The_log_of_the_previous_layout_is_kept_as_an_archive()
        {
            var folder = FreshFolder();
            File.WriteAllText(Path.Combine(folder, "smurftown.log"), "the run that installed the update");

            Housekeeping.Run(folder);

            var archive = Path.Combine(folder, LogArchive.FolderName, "smurftown-previous-layout.log.zip");
            Assert.False(File.Exists(Path.Combine(folder, "smurftown.log")));

            using var zip = ZipFile.OpenRead(archive);
            using var reader = new StreamReader(zip.GetEntry("smurftown.log")!.Open());
            Assert.Equal("the run that installed the update", reader.ReadToEnd());
        }

        [Fact]
        public void A_second_start_does_not_archive_the_previous_log_over_itself()
        {
            var folder = FreshFolder();
            File.WriteAllText(Path.Combine(folder, "smurftown.log"), "the first one");
            Housekeeping.Run(folder);

            File.WriteAllText(Path.Combine(folder, "smurftown.log"), "a leftover after a failed delete");
            Housekeeping.Run(folder);

            var archive = Path.Combine(folder, LogArchive.FolderName, "smurftown-previous-layout.log.zip");
            using var zip = ZipFile.OpenRead(archive);
            using var reader = new StreamReader(zip.GetEntry("smurftown.log")!.Open());
            Assert.Equal("the first one", reader.ReadToEnd());
        }

        [Fact]
        public void A_folder_with_nothing_in_it_is_not_a_failure()
        {
            // The first start of a fresh installation lands here before anything has been
            // written. None of the three steps may throw over a folder that is not there.
            Housekeeping.Run(FreshFolder());
        }

        private static string[] Shots(string folder) =>
            Directory
                .GetFiles(Path.Combine(folder, "shots"))
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToArray();

        private static void Shot(string folder, string name, TimeSpan age)
        {
            var shots = Path.Combine(folder, "shots");
            Directory.CreateDirectory(shots);
            Stamp(Path.Combine(shots, name), age);
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
