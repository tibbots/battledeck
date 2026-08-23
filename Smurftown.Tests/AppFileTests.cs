using System.IO;
using Smurftown;
using Smurftown.Backend.Entity;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Update;
using Xunit;
using Settings = Smurftown.Backend.Entity.Settings;

namespace Smurftown.Tests
{
    /// <summary>
    ///     The one file the application keeps about itself, and the two things that make one
    ///     file safe where four used to be necessary: every write re-reads first, and a
    ///     schema nobody here understands is never written back.
    ///     <para>
    ///         <b>The interesting cases use two <see cref="AppFile" /> instances on the same
    ///         folder.</b> That is how a second writer is modelled - the hourly update check
    ///         and a human in the settings tab are, from the file's point of view, exactly
    ///         that: two holders of a picture that can go stale.
    ///     </para>
    /// </summary>
    public class AppFileTests
    {
        [Fact]
        public void A_write_keeps_what_another_writer_put_there_in_the_meantime()
        {
            var folder = FreshFolder();
            var app = new AppFile(folder);
            app.SaveSettings(new Settings { HotsPath = @"D:\hots.exe" });

            // Somebody else writes a different section - the hourly check, in the real
            // application. Our instance knows nothing about it.
            new AppFile(folder).SaveUpdate(new UpdateState { LatestVersion = "9.9.9" });

            // And now we write ours. Without the re-read this would carry our old picture of
            // the file back to disk and 9.9.9 would be gone - which is the entire reason
            // update.yaml used to be a file of its own.
            app.SaveRotation(new HotsRotation { PeriodStart = "2026-08-22", Heroes = ["tracer"] });

            var read = new AppFile(folder).State;
            Assert.Equal("9.9.9", read.Update.LatestVersion);
            Assert.Equal(@"D:\hots.exe", read.Settings.HotsPath);
            Assert.Equal(["tracer"], read.Rotation.Heroes);
        }

        [Fact]
        public void A_section_written_twice_keeps_the_later_value()
        {
            var folder = FreshFolder();
            var app = new AppFile(folder);

            app.SaveSettings(new Settings { HotsPath = @"D:\first.exe" });
            app.SaveSettings(new Settings { HotsPath = @"D:\second.exe" });

            Assert.Equal(@"D:\second.exe", new AppFile(folder).State.Settings.HotsPath);
        }

        [Fact]
        public void The_files_of_the_older_layout_become_one()
        {
            var folder = FreshFolder();
            Write(folder, "settings.yaml", "hotsPath: D:\\hots.exe\ninputSpeed: Fast");
            Write(folder, "rotation.yaml", "periodStart: 2026-08-22\nheroes:\n- tracer\n- muradin");
            Write(folder, "update.yaml", "lastCheck: 2026-08-22T18:38:03.0000000+00:00\nlatestVersion: 1.2.0");
            Write(folder, "version.txt", "1.2.0");

            var state = new AppFile(folder).State;

            Assert.Equal(@"D:\hots.exe", state.Settings.HotsPath);
            Assert.Equal(InputSpeed.Fast, state.Settings.InputSpeed);
            Assert.Equal(["tracer", "muradin"], state.Rotation.Heroes);
            Assert.Equal("1.2.0", state.Update.LatestVersion);
            Assert.Equal("1.2.0", state.AppVersion);
        }

        [Fact]
        public void The_files_of_the_older_layout_are_gone_afterwards()
        {
            var folder = FreshFolder();
            Write(folder, "settings.yaml", "hotsPath: D:\\hots.exe");
            Write(folder, "version.txt", "1.2.0");

            _ = new AppFile(folder);

            Assert.True(File.Exists(Path.Combine(folder, "app.yaml")));
            Assert.False(File.Exists(Path.Combine(folder, "settings.yaml")));
            Assert.False(File.Exists(Path.Combine(folder, "version.txt")));
        }

        [Fact]
        public void One_unreadable_file_of_the_older_layout_does_not_take_the_others_with_it()
        {
            var folder = FreshFolder();
            Write(folder, "settings.yaml", "hotsPath: D:\\hots.exe");
            Write(folder, "rotation.yaml", "heroes: [this: is, not: yaml");

            var state = new AppFile(folder).State;

            Assert.Equal(@"D:\hots.exe", state.Settings.HotsPath);
            Assert.Empty(state.Rotation.Heroes);
        }

        [Fact]
        public void A_fresh_installation_writes_nothing_until_something_is_saved()
        {
            var folder = FreshFolder();

            _ = new AppFile(folder);

            // An app.yaml carrying nothing but defaults would be a file that says the human
            // configured something. They did not.
            Assert.False(File.Exists(Path.Combine(folder, "app.yaml")));
        }

        [Fact]
        public void A_file_from_a_newer_schema_is_read_as_far_as_it_goes()
        {
            var folder = FreshFolder();
            NewerSchema(folder);

            var state = new AppFile(folder).State;

            // Best effort, so the human is not locked out of an application whose file a
            // later version touched.
            Assert.Equal(@"D:\hots.exe", state.Settings.HotsPath);
        }

        [Fact]
        public void A_file_from_a_newer_schema_is_never_written_back()
        {
            var folder = FreshFolder();
            NewerSchema(folder);
            var app = new AppFile(folder);

            // Deserialising drops every key this build does not know. Writing the file back
            // would therefore delete whatever the later version put in it - silently, which
            // is the failure mode this whole class is built against.
            Assert.Throws<InvalidOperationException>(() => app.SaveSettings(new Settings()));
            Assert.Contains("tomorrow", File.ReadAllText(Path.Combine(folder, "app.yaml")));
        }

        [Fact]
        public void A_broken_file_costs_the_defaults_and_not_the_start()
        {
            var folder = FreshFolder();
            Write(folder, "app.yaml", "settings: [this: is, not: yaml");

            var state = new AppFile(folder).State;

            Assert.Equal("", state.Settings.HotsPath);
        }

        [Fact]
        public void The_version_a_previous_release_noted_is_readable_without_building_the_file()
        {
            var folder = FreshFolder();
            new AppFile(folder).SaveAppVersion("1.2.0");

            Assert.Equal("1.2.0", AppFile.PeekAppVersion(folder));
        }

        [Fact]
        public void Peeking_falls_back_to_the_marker_of_the_older_layout()
        {
            var folder = FreshFolder();
            Write(folder, "version.txt", "1.1.0");

            // No app.yaml yet, and peeking may not create one: it runs before the backup,
            // and the migration deletes the files the backup is about to keep.
            Assert.Equal("1.1.0", AppFile.PeekAppVersion(folder));
            Assert.False(File.Exists(Path.Combine(folder, "app.yaml")));
        }

        [Fact]
        public void Peeking_a_folder_that_holds_nothing_answers_with_nothing()
        {
            Assert.Equal("", AppFile.PeekAppVersion(FreshFolder()));
        }

        private static void NewerSchema(string folder) =>
            Write(folder, "app.yaml",
                $"schemaVersion: {AppFile.CurrentSchema + 1}\n" +
                "settings:\n" +
                "  hotsPath: D:\\hots.exe\n" +
                "somethingFromTomorrow: tomorrow\n");

        private static void Write(string folder, string name, string content) =>
            File.WriteAllText(Path.Combine(folder, name), content);

        private static string FreshFolder()
        {
            var folder = Path.Combine(TestHome.Path, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
