using System.Reflection;

namespace Battledeck;

/// <summary>
///     The version of the running application - the one <c>./dev version</c> writes into
///     the <c>csproj</c>, and from there into three more places.
///     <para>
///         Its own class and not a constant somewhere, because two very different things
///         ask for it: the backup before a migration needs to know whether the data on
///         disk was written by an older build, and the update check needs to compare it
///         against what GitHub offers.
///     </para>
/// </summary>
public static class AppVersion
{
    /// <summary>
    ///     Three parts, without a build suffix - <c>2.1.0</c>, never <c>2.1.0.0</c> and
    ///     never <c>2.1.0+3a7f1c</c>.
    ///     <para>
    ///         <b>Read from the informational version first</b>: that is the one carrying
    ///         the <c>&lt;Version&gt;</c> of the csproj literally. <c>AssemblyVersion</c> is
    ///         the fallback and always four-part, which is why it gets cut - a folder named
    ///         <c>2.1.0.0</c> would not match any tag or any release.
    ///     </para>
    /// </summary>
    public static string Current { get; } = Read();

    /// <summary>
    ///     Is <paramref name="candidate" /> a released version newer than the one running?
    ///     <para>
    ///         <b>Compared as three numbers, never as text.</b> A string comparison answers
    ///         <c>1.0.10 &gt; 1.0.9</c> with "no", because <c>1</c> sorts before <c>9</c> - and
    ///         it does so silently, on the one release where it finally matters. That is the
    ///         entire reason this method exists instead of an <c>!=</c> at the call site.
    ///     </para>
    ///     <para>
    ///         <b>Exactly three parts, or false.</b> <see cref="System.Version" /> happily
    ///         parses <c>2.0</c> and then reports a build number of <c>-1</c>, which sorts
    ///         below <c>2.0.0</c> - a tag typed short would look like a downgrade. Anything
    ///         that is not <c>x.y.z</c> is not a tag this repository produces, so it is not
    ///         an update either.
    ///     </para>
    /// </summary>
    public static bool IsNewerThanCurrent(string? candidate)
    {
        return TryParse(candidate, out var offered)
               && TryParse(Current, out var running)
               && offered > running;
    }

    private static bool TryParse(string? version, out Version parsed)
    {
        parsed = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(version)) return false;

        var text = version.Trim();
        if (text.Split('.').Length != 3) return false;
        if (!Version.TryParse(text, out var value) || value == null) return false;

        parsed = value;
        return true;
    }

    private static string Read()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // "2.1.0+3a7f1c" - everything from the build metadata on is not the version.
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        var version = assembly.GetName().Version;
        return version == null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
