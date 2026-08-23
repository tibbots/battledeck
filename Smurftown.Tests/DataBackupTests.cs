using System.IO;
using System.IO.Compression;
using Smurftown;
using Smurftown.Backend.Gateway;
using Xunit;

namespace Smurftown.Tests
{
    /// <summary>
    ///     The backup that is taken before a migration touches anything.
    ///     <para>
    ///         <b>What is being guarded is a silent loss.</b> A migration that reads wrongly
    ///         does not throw - it writes an emptier file, and that looks exactly like an
    ///         account nobody ever entered. This archive is the only copy of the state before
    ///         it, so the cases below are all about the same question: is it there, does it
    ///         hold the files, and does a second start leave it alone.
    ///     </para>
    ///     <para>
    ///         Every case gets a folder of its own, because <see cref="DataBackup" /> works on
    ///         a whole data folder and two cases sharing one would see each other's archives.
    ///     </para>
    /// </summary>
    public class DataBackupTests
    {
        [Fact]
        public void The_data_of_an_older_version_is_kept_as_one_archive()
        {
            var folder = FreshFolder();
            Data(folder, "data.yaml", "accounts: []");
            Data(folder, "settings.yaml", "appLanguage: German");
            Marker(folder, "1.0.0");

            DataBackup.BeforeMigrations(folder);

            var archive = DataBackup.BackupFile(folder, "1.0.0");
            Assert.True(File.Exists(archive));
            Assert.Equal(["data.yaml", "settings.yaml"], EntryNames(archive));
        }

        [Fact]
        public void The_archive_carries_the_content_and_not_just_the_name()
        {
            var folder = FreshFolder();
            Data(folder, "data.yaml", "accounts: []");
            Marker(folder, "1.0.0");

            DataBackup.BeforeMigrations(folder);

            using var zip = ZipFile.OpenRead(DataBackup.BackupFile(folder, "1.0.0"));
            using var reader = new StreamReader(zip.GetEntry("data.yaml")!.Open());
            Assert.Equal("accounts: []", reader.ReadToEnd());
        }

        [Fact]
        public void Data_that_nobody_stamped_is_kept_under_the_unknown_version()
        {
            var folder = FreshFolder();
            Data(folder, "data.yaml", "accounts: []");

            DataBackup.BeforeMigrations(folder);

            // Every installation from before 22.08.2026 - none of them wrote a marker, and
            // the state of those is the one worth having most.
            Assert.True(File.Exists(DataBackup.BackupFile(folder, "unknown")));
        }

        [Fact]
        public void A_fresh_installation_leaves_no_archive_behind()
        {
            var folder = FreshFolder();

            DataBackup.BeforeMigrations(folder);

            // Not "an empty archive": one named after a version that never ran would be a
            // lie in the folder listing.
            Assert.False(Directory.Exists(DataBackup.BackupRoot(folder)));
        }

        [Fact]
        public void Data_already_on_the_running_version_is_not_backed_up_again()
        {
            var folder = FreshFolder();
            Data(folder, "data.yaml", "accounts: []");
            Marker(folder, AppVersion.Current);

            DataBackup.BeforeMigrations(folder);

            Assert.False(Directory.Exists(DataBackup.BackupRoot(folder)));
        }

        [Fact]
        public void An_archive_that_is_already_there_survives_a_second_start()
        {
            var folder = FreshFolder();
            Data(folder, "data.yaml", "accounts: []");
            Marker(folder, "1.0.0");
            DataBackup.BeforeMigrations(folder);

            // The state a failed migration left behind. A second run must not copy THIS
            // over the archive from before it - that one is the point of the exercise.
            Data(folder, "data.yaml", "accounts:");
            DataBackup.BeforeMigrations(folder);

            using var zip = ZipFile.OpenRead(DataBackup.BackupFile(folder, "1.0.0"));
            using var reader = new StreamReader(zip.GetEntry("data.yaml")!.Open());
            Assert.Equal("accounts: []", reader.ReadToEnd());
        }

        [Fact]
        public void A_backup_folder_of_the_older_layout_becomes_an_archive()
        {
            var folder = FreshFolder();
            var legacy = Path.Combine(DataBackup.BackupRoot(folder), "1.0.0");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "data.yaml"), "accounts: []");

            // Deliberately with the marker on the running version, so the backup itself is
            // NOT due: the conversion has to happen anyway, or a folder from an installation
            // that skips no version would never be converted at all.
            Marker(folder, AppVersion.Current);

            DataBackup.BeforeMigrations(folder);

            Assert.False(Directory.Exists(legacy));
            Assert.Equal(["data.yaml"], EntryNames(DataBackup.BackupFile(folder, "1.0.0")));
        }

        [Fact]
        public void An_empty_backup_folder_of_the_older_layout_just_goes()
        {
            var folder = FreshFolder();
            var legacy = Path.Combine(DataBackup.BackupRoot(folder), "1.0.0");
            Directory.CreateDirectory(legacy);
            Marker(folder, AppVersion.Current);

            DataBackup.BeforeMigrations(folder);

            Assert.False(Directory.Exists(legacy));
            Assert.False(File.Exists(DataBackup.BackupFile(folder, "1.0.0")));
        }

        [Fact]
        public void The_version_that_wrote_the_data_is_noted_in_the_app_file()
        {
            var folder = FreshFolder();
            var app = new AppFile(folder);

            DataBackup.MarkCurrent(app);

            // Read back through a SECOND instance, not off the one that just wrote: the
            // question is what stands in the file, not what stands in a field.
            Assert.Equal(AppVersion.Current, new AppFile(folder).State.AppVersion);
        }

        [Fact]
        public void The_version_is_taken_from_the_app_file_once_there_is_one()
        {
            var folder = FreshFolder();
            Data(folder, "data.yaml", "accounts: []");
            new AppFile(folder).SaveAppVersion("1.0.0");

            DataBackup.BeforeMigrations(folder);

            Assert.True(File.Exists(DataBackup.BackupFile(folder, "1.0.0")));
        }

        [Fact]
        public void The_marker_of_the_older_layout_still_answers_the_question()
        {
            var folder = FreshFolder();
            Data(folder, "data.yaml", "accounts: []");
            Marker(folder, "1.0.0");

            // The first start after the update: version.txt is all there is, app.yaml does
            // not exist yet, and the backup has to run BEFORE the migration that would
            // create it. Without the fallback this backup would be named "unknown".
            DataBackup.BeforeMigrations(folder);

            Assert.True(File.Exists(DataBackup.BackupFile(folder, "1.0.0")));
        }

        private static string[] EntryNames(string archive)
        {
            using var zip = ZipFile.OpenRead(archive);
            return zip.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToArray();
        }

        private static void Data(string folder, string name, string content) =>
            File.WriteAllText(Path.Combine(folder, name), content);

        private static void Marker(string folder, string version) =>
            File.WriteAllText(Path.Combine(folder, "version.txt"), version);

        private static string FreshFolder()
        {
            var folder = Path.Combine(TestHome.Path, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
