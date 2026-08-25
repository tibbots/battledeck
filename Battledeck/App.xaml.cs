using System.IO;
using System.Windows;
using Serilog;
using Serilog.Events;
using Battledeck.Backend.Gateway;
using Battledeck.Backend.Update;

namespace Battledeck
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (!Directory.Exists(Directories.UserPath)) Directory.CreateDirectory(Directories.UserPath);

            // The file carries more than the console, and that is intentional: reading
            // from the game can only be judged afterward if each card cell records what
            // OCR delivered. On the console that would be around 250 lines per run and
            // thus unreadable; in the file it is exactly the piece of evidence that is
            // missing in case of doubt.
            //
            // It rolls at 10 MB and keeps five files, the current one included; everything
            // that is no longer written to is compressed by LogArchive. Without those
            // arguments Serilog's defaults apply, and they are worse than they look: a
            // single file, a limit of 1 GB, and no rolling - so on reaching it the sink
            // stops writing, silently.
            var logs = Path.Combine(Directories.UserPath, LogArchive.FolderName);
            Directory.CreateDirectory(logs);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.File(Path.Combine(logs, LogArchive.FileName),
                    fileSizeLimitBytes: Housekeeping.LogSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: Housekeeping.LogsKept,
                    hooks: new LogArchive())
                .CreateLogger();

            Log.Information("starting battledeck {Version}", AppVersion.Current);

            // If the last start was one an update replaced, its .exe is still lying beside
            // ours under a different name - Windows lets a running executable be renamed but
            // not deleted, and that is what makes the swap in UpdateInstaller possible at
            // all. Here, one start later, nothing holds it any more.
            //
            // BEFORE everything below, because it may not depend on any of it: neither on a
            // data folder, nor on settings, nor on a migration having survived. It needs the
            // logger and nothing else.
            UpdateInstaller.CleanUpPrevious();

            // WARNING and not Debug, because the console only carries Information and up -
            // and this is the line that answers "why is my account list empty". A run
            // against a test folder looks exactly like a fresh installation otherwise.
            if (Directories.IsOverridden)
                Log.Warning("data folder overridden by {Variable}: {Path}",
                    Directories.OverrideVariable, Directories.UserPath);
            else
                Log.Debug("data folder {Path}", Directories.UserPath);

            // BEFORE the first gateway, and that is the whole point: every gateway migrates
            // on load, and a migration rewrites the file it just read. Once that has
            // happened, there is nothing left to set aside.
            DataBackup.BeforeMigrations(Directories.UserPath);

            // Directly after, and not at the end: the archive the line above just wrote is
            // the newest of the ten that survive, so counting them here counts the state
            // this start actually leaves behind. Captures and the leftover log of the
            // previous layout come along in the same pass - one place, one policy.
            Housekeeping.Run(Directories.UserPath);

            // Carries the saved settings to where they take effect: the input speed,
            // the game's vocabulary, the OCR language - and the UI language.
            //
            // Until 22.08.2026 the call stood in the constructor of MainViewModel, and that
            // worked fine as long as only values were affected that are only needed at game
            // start. With the translated texts, the first rendered line now depends on it:
            // if the language weren't set yet, the tab bar would briefly show !main.tabAccounts!.
            // The binding would correct itself as soon as Strings.Use reports the change - a
            // visible flicker would remain nonetheless, and the order would be luck instead
            // of intent.
            //
            // Here and not there, because settings are application state and not
            // ViewModel state. After the logger, so the messages arrive; before
            // base.OnStartup, which creates the main window.
            SettingsGateway.Instance.Apply();

            // Forces the account file to be read HERE, rather than whenever the first view
            // happens to ask for it. Two reasons: the marker below may only be written once
            // the read has survived, and a data.yaml that cannot be read should stop the
            // application at its start and not halfway into building a window.
            _ = BattlenetAccountGateway.Instance;

            // Only now - see DataBackup.MarkCurrent. Everything above may still throw, and
            // then the next start has to find the same backup situation as this one.
            DataBackup.MarkCurrent(AppFile.Instance);

            base.OnStartup(e);
        }
    }
}