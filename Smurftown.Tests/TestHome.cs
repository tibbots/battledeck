using System.IO;
using System.Runtime.CompilerServices;
using Smurftown;
using Smurftown.Backend.Gateway;

namespace Smurftown.Tests
{
    /// <summary>
    ///     Points the whole test run at a throwaway data folder, before a single test runs.
    ///     <para>
    ///         <b>Why a module initializer and not a fixture</b>:
    ///         <see cref="Directories.UserPath" /> is a <c>static readonly</c> resolved on
    ///         first access and kept for the life of the process. Whichever code touches it
    ///         first decides the folder for everyone. A fixture runs after the test class is
    ///         constructed, which is already too late if a static somewhere got there first -
    ///         a module initializer runs when this assembly is loaded, and nothing of ours can
    ///         run before it.
    ///     </para>
    ///     <para>
    ///         <b>What it prevents</b> is not a tidiness problem. The real
    ///         <c>~/.smurftown/data.yaml</c> holds the credentials in plain text, and
    ///         <c>BattlenetAccountGateway</c> rewrites that file whole on every mutation. A
    ///         test that constructs a view is one line away from writing into it.
    ///     </para>
    /// </summary>
    internal static class TestHome
    {
        /// <summary>The folder every test in this assembly works in. Fresh per run.</summary>
        internal static string Path { get; private set; } = "";

        [ModuleInitializer]
        internal static void Redirect()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "smurftown-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Environment.SetEnvironmentVariable(Directories.OverrideVariable, Path);

            // A fresh stamp, so the update check finds itself not due and no test makes an
            // HTTP request as a side effect of constructing a view. Without this the folder
            // is empty, "never checked" counts as due, and MainViewModel asks GitHub -
            // fire-and-forget, so it would not even fail visibly on a machine that is offline.
            //
            // Written as app.yaml in the current layout, not as the update.yaml this used to
            // be: the migration would carry that one over just as well, but a test setup that
            // leans on a migration is one that starts failing for a reason that has nothing
            // to do with the test.
            var newline = Environment.NewLine;
            File.WriteAllText(
                System.IO.Path.Combine(Path, "app.yaml"),
                $"schemaVersion: {AppFile.CurrentSchema}{newline}" +
                $"update:{newline}" +
                $"  lastCheck: {DateTimeOffset.UtcNow:o}{newline}");
        }
    }
}
