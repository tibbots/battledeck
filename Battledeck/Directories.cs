using System.IO;

namespace Battledeck;

/// <summary>
///     Where everything this application persists lives - the account list, the settings,
///     the log, the backups. One place, so that moving it moves all of it.
/// </summary>
public abstract class Directories
{
    /// <summary>
    ///     The environment variable that points the data folder somewhere else.
    ///     <para>
    ///         <b>Why it exists</b>: testing the app means clicking through it, and every
    ///         click that ticks a region or renames an account rewrites <c>data.yaml</c> -
    ///         the real one, with the real credentials in it. The README captures had to
    ///         move the real file aside and put it back afterwards, and "put it back
    ///         afterwards" is a step that works until the one run that strands halfway.
    ///     </para>
    ///     <para>
    ///         An environment variable and not a command line argument, because the
    ///         PowerShell scripts under <c>tools/</c> read the same files as the app
    ///         (<c>data.yaml</c> for a login, <c>app.yaml</c> for the game path). A
    ///         variable set once in a shell reaches the app <b>and</b> them; an argument
    ///         would reach only the app, and the scripts would keep pointing at the real
    ///         folder.
    ///     </para>
    /// </summary>
    public const string OverrideVariable = "SMURFTOWN_HOME";

    /// <summary>
    ///     <c>%USERPROFILE%\.smurftown</c> - what a normal installation uses, and what
    ///     <see cref="UserPath" /> is unless <see cref="OverrideVariable" /> says otherwise.
    /// </summary>
    public static readonly string DefaultPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".smurftown");

    /// <summary>
    ///     The folder actually in use. Read once, at first access - a variable changed
    ///     while the app runs would otherwise split the data across two folders.
    /// </summary>
    public static readonly string UserPath = Resolve();

    /// <summary>
    ///     Whether the app is running against something other than the real data folder.
    ///     Logged at startup, and it is the only signal that a test run is a test run.
    /// </summary>
    public static bool IsOverridden =>
        !string.Equals(UserPath, DefaultPath, StringComparison.OrdinalIgnoreCase);

    private static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (string.IsNullOrWhiteSpace(configured)) return DefaultPath;

        try
        {
            // Expanded, because %USERPROFILE%\battledeck-test is the obvious thing to write
            // into such a variable. Absolute, because a relative path would land wherever
            // the app happened to be started from - and that is a different folder for the
            // IDE than for a script.
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim()));
        }
        catch (Exception e)
        {
            // NO fallback to the default. A typo in the variable would then silently write
            // to the real folder, which is the one thing this whole mechanism exists to
            // prevent. The exception flies before the logger is configured, so the message
            // has to carry everything on its own.
            throw new InvalidOperationException(
                $"{OverrideVariable} is set to '{configured}', which is not a usable path: {e.Message}", e);
        }
    }
}
