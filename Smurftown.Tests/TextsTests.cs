using System.IO;
using System.Text.RegularExpressions;
using Smurftown.Backend.Texts;
using Xunit;
using YamlDotNet.Serialization;

namespace Smurftown.Tests;

/// <summary>
///     Ports <c>tools/check-texts.py</c> into facts that run with the rest of the suite.
///     <para>
///         <b>A missing or malformed key does not show up when building.</b> XAML does not
///         know the keys, C# only ever sees a string literal, and <see cref="Strings" />
///         quietly falls back to English at run time when a key or a placeholder does not
///         line up. So the bug reaches only the person using the affected language - and
///         only if they notice that a line reads in English, or the app logs a swallowed
///         <see cref="System.FormatException" /> and shows English instead of the sentence
///         that was meant to be there. These facts turn that silent, per-user failure into a
///         build-time one.
///     </para>
/// </summary>
public class TextsTests
{
    // XAML: {loc:Str key}
    private static readonly Regex XamlKeyPattern = new(@"\{loc:Str\s+([^}\s]+)\s*\}");

    // C#: EVERY literal that follows the key schema. Deliberately broad and not tied to
    // Strings.Current[...] - half of the calls sit in a conditional expression
    // (Strings.Current[x ? "row.restore" : "row.archive"]) or are spread across several
    // lines, and a pattern aimed at that would miss them. The price is false positives on
    // other dotted literals; they only ever hit the (deliberately untested, see below)
    // dead-entries list, never the "is a used key missing" question that matters here.
    private static readonly Regex CSharpKeyPattern = new(@"""([a-z][A-Za-z]*(?:\.[a-zA-Z][A-Za-z0-9]*)+)""");

    // Follows the key schema but is not one.
    private static readonly Regex NotAKeyPattern =
        new(@"\.(yaml|yml|exe|png|jpg|cs|xaml|log|txt|dll|json|bak)$");

    private static readonly Regex PlaceholderPattern = new(@"\{(\d+)\}");

    private static readonly Regex XmlCommentPattern = new(@"<!--.*?-->", RegexOptions.Singleline);
    private static readonly Regex AttrPattern = new(@"\b(Text|Content|ToolTip|Header)\s*=\s*""([^""{][^""]*)""");
    private static readonly Regex InnerPattern = new(@">\s*([A-Za-z][A-Za-z0-9 '\-.,+/]{2,})\s*<");
    private static readonly Regex ThreeLettersPattern = new(@"[A-Za-z]{3}");

    // No translatable text: plain characters (&#x2715; is the close cross), product
    // names, and the region abbreviations - those read the same in all four languages.
    private static readonly Regex FinePattern = new(
        @"^([#_x\[\]\s;&]|&#x[0-9A-Fa-f]+;|\d)*$" +
        @"|^(Smurftown|SMURFTOWN|HEROES OF THE STORM|Heroes of the Storm|Battle\.net" +
        @"|EU|AM|AS|OK|Height)$");

    // Keys the code assembles at run time from an enum name instead of spelling them out
    // literally - rank.*, role.*, region.*, speed.* and settings.speedHint.*. They cannot
    // be found by searching and are therefore hardcoded here, exactly as in the script.
    // Whoever adds an enum value there adds it here too.
    private static readonly HashSet<string> Dynamic = BuildDynamicKeys();

    private static HashSet<string> BuildDynamicKeys()
    {
        var keys = new HashSet<string>();

        foreach (var rank in new[]
                     { "none", "bronze", "silver", "gold", "platinum", "diamond", "master", "grandmaster" })
            keys.Add($"rank.{rank}");

        foreach (var role in new[]
                     { "tank", "bruiser", "meleeassassin", "rangedassassin", "healer", "support" })
            keys.Add($"role.{role}");

        foreach (var region in new[] { "europe", "americas", "asia" })
            keys.Add($"region.{region}");

        foreach (var speed in new[] { "slow", "normal", "fast" })
        {
            keys.Add($"speed.{speed}");
            keys.Add($"settings.speedHint.{speed}");
        }

        return keys;
    }

    /// <summary>
    ///     Reads one of the four embedded text files the way <see cref="Strings" /> itself
    ///     does - through the compiled assembly, not the file on disk - so this test catches
    ///     a csproj entry that stopped embedding a file just as reliably as a typo in a key.
    ///     Plain keys, not camelCase: the keys are literal dotted strings such as
    ///     <c>dialog.save</c>, not property names to be re-cased.
    /// </summary>
    private static Dictionary<string, string> LoadEmbedded(string tag)
    {
        var resourceName = $"Smurftown.Backend.Texts.{tag}.yaml";
        using var stream = typeof(Strings).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' was not found - check the csproj entry.");

        using var reader = new StreamReader(stream);
        var yaml = reader.ReadToEnd();

        return new DeserializerBuilder().Build().Deserialize<Dictionary<string, string>>(yaml)
               ?? new Dictionary<string, string>();
    }

    /// <summary>
    ///     Walks up from the running test binary until a directory containing
    ///     <c>Smurftown.sln</c> turns up, or gives up at the filesystem root. Checks 3 and 4
    ///     need the source tree, not the compiled assembly - there is no embedded copy of the
    ///     C# and XAML files to fall back to.
    /// </summary>
    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Smurftown.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsUnderObjOrBin(string root, string filePath)
    {
        var relative = Path.GetRelativePath(root, filePath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("obj") || parts.Contains("bin");
    }

    /// <summary>Every dotted key the code references, plus the run-time-assembled ones.</summary>
    private static HashSet<string> UsedKeys(string repoRoot)
    {
        var sourceRoot = Path.Combine(repoRoot, "Smurftown");
        var files = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories))
            .Where(f => !IsUnderObjOrBin(sourceRoot, f));

        var found = new HashSet<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match match in XamlKeyPattern.Matches(text)) found.Add(match.Groups[1].Value);
            foreach (Match match in CSharpKeyPattern.Matches(text)) found.Add(match.Groups[1].Value);
        }

        found.RemoveWhere(key => NotAKeyPattern.IsMatch(key));
        found.UnionWith(Dynamic);
        return found;
    }

    /// <summary>
    ///     Visible text in XAML that does not go through <c>loc:Str</c>. Two forms, both of
    ///     which actually slipped through once: an attribute (<c>Text=</c>, <c>Content=</c>,
    ///     <c>ToolTip=</c>, <c>Header=</c>) carrying a literal instead of a binding, and text
    ///     as element content (<c>&lt;TextBlock&gt;FILTER&lt;/TextBlock&gt;</c>) which a
    ///     pattern aimed only at attributes does not see at all.
    /// </summary>
    private static List<(string File, int Line, string Value)> StrayLiterals(string repoRoot)
    {
        var sourceRoot = Path.Combine(repoRoot, "Smurftown");

        var files = Directory.EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f) ?? string.Empty) == "View")
            .ToList();
        var mainWindow = Path.Combine(sourceRoot, "MainWindow.xaml");
        if (File.Exists(mainWindow)) files.Add(mainWindow);

        var problems = new List<(string, int, string)>();

        foreach (var file in files)
        {
            var raw = File.ReadAllText(file).Replace("\r\n", "\n").Replace("\r", "\n");
            var text = XmlCommentPattern.Replace(raw, string.Empty);
            var lines = text.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var lineNumber = i + 1;

                foreach (Match match in AttrPattern.Matches(line))
                {
                    var value = match.Groups[2].Value;
                    if (!FinePattern.IsMatch(value.Trim()))
                        problems.Add((Path.GetFileName(file), lineNumber, value));
                }

                foreach (Match match in InnerPattern.Matches(line))
                {
                    var value = match.Groups[1].Value.Trim();
                    if (!FinePattern.IsMatch(value) && ThreeLettersPattern.IsMatch(value))
                        problems.Add((Path.GetFileName(file), lineNumber, value));
                }
            }
        }

        return problems;
    }

    [Theory]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Every_translation_has_the_same_keys_as_english(string tag)
    {
        var english = LoadEmbedded("en");
        var translation = LoadEmbedded(tag);

        var missing = english.Keys.Except(translation.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var extra = translation.Keys.Except(english.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"{tag}.yaml - missing ({missing.Count}): {string.Join(", ", missing)}; " +
            $"extra ({extra.Count}): {string.Join(", ", extra)}");
    }

    [Theory]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Every_translation_has_the_same_placeholders(string tag)
    {
        var english = LoadEmbedded("en");
        var translation = LoadEmbedded(tag);

        var problems = new List<string>();
        foreach (var (key, value) in translation)
        {
            if (!english.TryGetValue(key, out var englishValue)) continue;

            var want = PlaceholderPattern.Matches(englishValue).Select(m => m.Groups[1].Value)
                .ToHashSet();
            var have = PlaceholderPattern.Matches(value).Select(m => m.Groups[1].Value)
                .ToHashSet();

            if (!want.SetEquals(have))
                problems.Add($"{key} - english has {{{string.Join(",", want.OrderBy(x => x))}}}, " +
                             $"{tag} has {{{string.Join(",", have.OrderBy(x => x))}}}");
        }

        Assert.True(problems.Count == 0,
            $"{tag}.yaml placeholder mismatches ({problems.Count}):\n{string.Join("\n", problems)}");
    }

    [Fact]
    public void Every_key_used_in_the_code_is_in_english()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            // Xunit.Assert has no Skip(string) in the v2 line this project references (it
            // was added in xunit v3 - see Xunit.Sdk.SkipException.ForSkip's own doc
            // comment). SkipException is the closest available equivalent: it still fails
            // loudly instead of letting the test pass silently, it just is not rendered
            // as a distinct "skipped" result by this runner.
            throw Xunit.Sdk.SkipException.ForSkip(
                "Could not locate the source tree (a directory containing Smurftown.sln) by " +
                "walking up from AppContext.BaseDirectory - " +
                "'Every_key_used_in_the_code_is_in_english' was not run.");
        }

        var english = LoadEmbedded("en");
        var used = UsedKeys(repoRoot);

        // GUARD AGAINST A VACUOUS PASS. If the folder layout ever moves and the scan walks
        // an empty tree, `used` is just the two dozen DYNAMIC keys and everything below is
        // green without having read a single source file. en.yaml holds 251 texts, so a
        // hundred is a floor nobody reaches by accident.
        Assert.True(used.Count > 100,
            $"Only {used.Count} keys were found in the source tree - that is too few to be "
            + "this application. The scan is looking in the wrong place.");

        var missing = used.Except(english.Keys).OrderBy(k => k, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            $"used in code but missing from en.yaml ({missing.Count}): {string.Join(", ", missing)}");

        // Deliberately NOT ported: the script's "in en.yaml, used nowhere" list. It is
        // informational there, not a problem - the broad C# literal regex above produces
        // false positives on unrelated dotted strings, so as a failing assertion here it
        // would be noise rather than a signal.
    }

    [Fact]
    public void No_visible_text_in_xaml_bypasses_the_translation()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            // See the matching branch in Every_key_used_in_the_code_is_in_english for why
            // this throws SkipException instead of calling the (xunit v3-only) Assert.Skip.
            throw Xunit.Sdk.SkipException.ForSkip(
                "Could not locate the source tree (a directory containing Smurftown.sln) by " +
                "walking up from AppContext.BaseDirectory - " +
                "'No_visible_text_in_xaml_bypasses_the_translation' was not run.");
        }

        // The same guard, counted independently of StrayLiterals: an empty file list would
        // otherwise be an empty problem list, which reads exactly like a clean result.
        var scanned = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "Smurftown"), "*.xaml", SearchOption.AllDirectories)
            .Count(f => Path.GetFileName(Path.GetDirectoryName(f) ?? string.Empty) == "View");
        Assert.True(scanned >= 5,
            $"Only {scanned} view(s) were scanned - the folder layout must have changed.");

        var stray = StrayLiterals(repoRoot);

        var problems = stray
            .Select(s => $"{s.File}:{s.Line}  {s.Value}")
            .ToList();

        Assert.True(problems.Count == 0,
            $"fixed text in XAML, not routed through loc:Str ({problems.Count}):\n{string.Join("\n", problems)}");
    }
}
