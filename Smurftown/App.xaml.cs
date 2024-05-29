using System.IO;
using System.Windows;
using Serilog;
using Serilog.Events;
using Smurftown.Backend.Gateway;
using Smurftown.Backend.Update;

namespace Smurftown
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
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.File(Path.Combine(Directories.UserPath, "smurftown.log"))
                .CreateLogger();

            Log.Information("starting smurftown {Version}", AppVersion.Current);

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
            DataBackup.BeforeMigrations();

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

            // Forces the account file to be read and migrated HERE, rather than whenever the
            // first view happens to ask for it. Two reasons: the marker below may only be
            // written once the migration has survived, and a migration that aborts should do
            // so at the start of the application and not halfway into building a window.
            _ = BattlenetAccountGateway.Instance;

            // Only now - see DataBackup.MarkCurrent. Everything above may still throw, and
            // then the next start has to find the same backup situation as this one.
            DataBackup.MarkCurrent();

            base.OnStartup(e);
        }
    }
}