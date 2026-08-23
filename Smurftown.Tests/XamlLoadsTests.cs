using System.Collections;
using System.Resources;
using System.Windows;
using Xunit;

namespace Smurftown.Tests
{
    /// <summary>
    ///     Loads every compiled XAML of the application once.
    ///     <para>
    ///         <b>This test exists because of an incident, and the incident is the whole
    ///         argument.</b> A comment in <c>MainWindow.xaml</c> lost its opening
    ///         <c>&lt;!--</c>; the text then stood as content in the <c>StackPanel</c> of the
    ///         tab bar. The build reported <c>0 errors</c>, an XML parser would have said
    ///         nothing either - <c>--&gt;</c> without <c>&lt;!--</c> is valid text content -
    ///         and even the BAML compiled. Only loading it threw a
    ///         <c>XamlParseException</c>, and the application no longer started at all.
    ///     </para>
    ///     <para>
    ///         Which is why this loads the <b>compiled BAML</b> and not the <c>.xaml</c>
    ///         source. Parsing the markup with <c>XamlReader</c> would be easier - no
    ///         <c>Application</c>, no code-behind - but it would test something else than what
    ///         ships.
    ///     </para>
    ///     <para>
    ///         The rule it replaces reads "after every XAML change the app must be started
    ///         once". That rule depends on somebody remembering it.
    ///     </para>
    /// </summary>
    public class XamlLoadsTests
    {
        /// <summary>
        ///     One fact and not one per file, because an <see cref="Application" /> is a
        ///     singleton per AppDomain - a second one throws, so every component has to be
        ///     loaded inside the same run. Every failure is collected so that a run reports
        ///     all broken files, not the first.
        /// </summary>
        [Fact]
        public void Every_compiled_xaml_of_the_application_loads()
        {
            var failures = new List<string>();
            var loaded = new List<string>();

            Sta.Run(() =>
            {
                // Exactly what the generated entry point does, minus Run(): it loads app.xaml
                // and with it the ten theme dictionaries it merges. If one of those is broken
                // this line throws, and the test fails on it rather than on the first view
                // that happens to reference a style out of it.
                var app = new App();
                app.InitializeComponent();

                var merged = MergedSources();

                foreach (var component in ComponentPaths())
                {
                    // app.xaml is already loaded, the theme dictionaries with it. Loading one
                    // of those a second time on its own would fail for a reason that is not a
                    // defect: BattlenetComboBoxTheme references the ScrollViewer theme through
                    // StaticResource, and StaticResource only sees what was merged before it.
                    if (component == "app.xaml" || merged.Contains(component)) continue;

                    try
                    {
                        Application.LoadComponent(
                            new Uri($"/Smurftown;component/{component}", UriKind.Relative));
                        loaded.Add(component);
                    }
                    catch (Exception e)
                    {
                        failures.Add($"{component}: {e.GetBaseException().Message}");
                    }
                }
            });

            Assert.True(failures.Count == 0,
                $"{failures.Count} compiled XAML file(s) do not load:{Environment.NewLine}" +
                string.Join(Environment.NewLine, failures));

            // GUARD AGAINST A TEST THAT PASSES BY DOING NOTHING. If the resource container is
            // ever named differently, or every entry ends up filtered out as "already merged",
            // everything above is a loop over an empty list - and green. Naming the window the
            // incident happened in is the cheaper half of that: renaming it should make
            // somebody look here.
            Assert.True(loaded.Count >= 8,
                $"Only {loaded.Count} component(s) were loaded - that is too few to be the "
                + $"whole application: {string.Join(", ", loaded)}");
            Assert.Contains("mainwindow.xaml", loaded);
        }

        /// <summary>
        ///     Every XAML the application ships, as the lower-case component path the build
        ///     gave it - <c>ui/mvvm/view/errorbox.xaml</c>. Read out of the resource container
        ///     rather than off the file system: what is not in there does not ship, and what
        ///     is on disk but excluded from the project would be tested for nothing.
        /// </summary>
        private static IEnumerable<string> ComponentPaths()
        {
            var assembly = typeof(App).Assembly;
            using var stream = assembly.GetManifestResourceStream("Smurftown.g.resources")
                               ?? throw new InvalidOperationException(
                                   "Smurftown.g.resources is missing - the app assembly carries no compiled XAML at all.");

            using var reader = new ResourceReader(stream);
            var found = new List<string>();

            foreach (DictionaryEntry entry in reader)
            {
                var name = (string)entry.Key;
                if (name.EndsWith(".baml", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(name[..^".baml".Length] + ".xaml");
                }
            }

            Assert.True(found.Count > 0, "No .baml entries in Smurftown.g.resources.");
            return found;
        }

        /// <summary>
        ///     The dictionaries app.xaml merged, in the same lower-case component form as
        ///     <see cref="ComponentPaths" />. Taken from the loaded application and not from a
        ///     list in this file, so that a dictionary added to app.xaml is covered without
        ///     anybody editing the test.
        /// </summary>
        private static HashSet<string> MergedSources()
        {
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
            {
                if (dictionary.Source is not { } source) continue;

                // Two forms appear here: "UI/Theme/X.xaml" relative to the application, and
                // the absolute pack URI of the ToastNotifications theme. Only the tail is
                // comparable, and a foreign assembly's file never matches one of ours anyway.
                var path = source.OriginalString;
                var marker = path.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
                if (marker >= 0) path = path[(marker + ";component/".Length)..];

                sources.Add(path.TrimStart('/').ToLowerInvariant());
            }

            return sources;
        }
    }
}
