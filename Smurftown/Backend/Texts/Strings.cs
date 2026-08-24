using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Reflection;
using Serilog;
using Smurftown.Backend.Entity;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Smurftown.Backend.Texts
{
    /// <summary>
    ///     Every text a human gets to see in Smurftown - in the language they have set.
    ///     <para>
    ///         <b>Not to be confused with <see cref="Automation.GameVocabulary" />.</b> The
    ///         two look similar and are the opposite of each other: the game vocabulary
    ///         contains words that <i>stand in the game</i> - measured values that OCR
    ///         compares against, and whoever translates there makes it blind. Here stand
    ///         words that <i>we write</i>, and translating is exactly their purpose.
    ///     </para>
    ///     <para>
    ///         <b>What does NOT run through here: the log.</b> Every line in
    ///         <c>smurftown.log</c> stays English, as do the names of the diagnostic
    ///         captures and the values of <c>GameScreen</c>. A log is not text for the
    ///         user, but for whoever is looking for a bug - and they look for it in the
    ///         same language the code is written in. A translated log would also no
    ///         longer be searchable: the same message would have four different wordings.
    ///     </para>
    ///     <para>
    ///         <b>The design is that of <see cref="Automation.GameVocabulary" />:</b> a
    ///         static <see cref="Current" /> instance, set from outside
    ///         (<c>SettingsGateway.Apply</c>), read on every access. The backend doesn't
    ///         know the gateways, and this direction is meant to stay that way.
    ///     </para>
    ///     <para>
    ///         <b>Why in the backend and not under <c>UI/</c>:</b> three places in the
    ///         backend write text for humans - <c>HotsRegionData.RankName</c>, the
    ///         progress messages in <c>GameSession</c>, and the note that
    ///         <c>ProfileReader</c> puts into its reading and that comes out as a toast.
    ///         They cannot point to <c>UI/</c>. Nothing about this class needs WPF:
    ///         <see cref="INotifyPropertyChanged" /> lives in <c>System.ComponentModel</c>.
    ///         What WPF needs stands separately in <c>UI/MVVM/StrExtension.cs</c>.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     <b>The namespace is called <c>Texts</c> and not <c>Language</c></b>, even
    ///     though that would seem more obvious. <c>Language</c> is the name of a WinRT type
    ///     (<c>Windows.Globalization.Language</c>) that <c>TextReader</c> needs to set up
    ///     OCR - a namespace of the same name would shadow it in every file under
    ///     <c>Backend/</c> that sees both. Measured: the build broke in
    ///     <c>TextReader.cs</c> with <c>CS0118</c>, and the error was at a spot that
    ///     has nothing to do with translation.
    /// </remarks>
    public sealed class Strings : INotifyPropertyChanged
    {
        /// <summary>
        ///     The name under which WPF reports a change to an indexer. Spelled out
        ///     instead of fetched via <c>Binding.IndexerName</c> - that sits in
        ///     <c>System.Windows.Data</c>, and this class stays free of WPF.
        /// </summary>
        private const string IndexerName = "Item[]";

        /// <summary>
        ///     <b>The instance is never swapped, only its content.</b> The entire runtime
        ///     language switch depends on this: every XAML binding points to this one
        ///     object, and a swap would leave them all pointing at nothing - silently,
        ///     because a dead binding in WPF just yields empty text.
        /// </summary>
        public static Strings Current { get; } = new();

        private Dictionary<string, string> _texts = new(StringComparer.Ordinal);

        /// <summary>
        ///     English, always loaded. The floor a missing key falls onto - an
        ///     untranslated line is better than an empty one, and incomparably better
        ///     than a crash.
        /// </summary>
        private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

        /// <summary>
        ///     Keys that have already been complained about. Without this list the
        ///     warning would be in the log on every access, and a binding asks often -
        ///     with a list of 27 rows that would be hundreds of identical lines burying
        ///     everything else.
        /// </summary>
        private readonly HashSet<string> _complained = new(StringComparer.Ordinal);

        private Strings()
        {
        }

        /// <summary>Which language is currently loaded. Only for checking.</summary>
        public AppLanguage Language { get; private set; } = AppLanguage.English;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        ///     Fires after every language change. XAML bindings don't need this - they
        ///     hang on the indexer - but ViewModels with <b>computed</b> properties do
        ///     (<c>RankName</c>, <c>PenaltyHint</c>, the hero filter's label). Those have
        ///     to trigger their own notification, otherwise their text stays in the old
        ///     language until the next rebuild.
        /// </summary>
        public static event Action? Changed;

        /// <summary>
        ///     The text for a key. If it's missing, the English version comes; if
        ///     that's missing too, the key itself in exclamation marks.
        ///     <para>
        ///         <b>Null and empty are never returned</b>, and that is the point: a
        ///         missing key is meant to <i>stand out</i> on screen.
        ///         <c>!accounts.tab!</c> looks wrong and can be searched for - an empty
        ///         label looks like intent. The build reports nothing about it, XAML
        ///         doesn't know the keys.
        ///     </para>
        /// </summary>
        public string this[string key]
        {
            get
            {
                if (_texts.TryGetValue(key, out var text)) return text;

                if (_fallback.TryGetValue(key, out var english))
                {
                    Complain("Text '{Key}' is missing in {Language}, using English", key);
                    return english;
                }

                Complain("Text '{Key}' is missing in {Language} and in English", key);
                return $"!{key}!";
            }
        }

        /// <summary>
        ///     A text with placeholders - <c>{0}</c>, <c>{1}</c> in the order the
        ///     caller passes them.
        ///     <para>
        ///         <b>Numbered and not named</b>, so that a translation can rearrange the
        ///         order: in German the rank comes before the division, in another
        ///         language possibly not.
        ///     </para>
        ///     <para>
        ///         <b>A format error doesn't crash.</b> If the Spanish file has a
        ///         <c>{2}</c> where only two values are passed,
        ///         <see cref="string.Format(string,object?[])" /> throws a
        ///         <see cref="FormatException" /> - and that would otherwise only be
        ///         noticed by the Spanish user, in the middle of a flow that's already
        ///         running. Instead it falls back to English and names the cause.
        ///     </para>
        /// </summary>
        public static string Format(string key, params object?[] values)
        {
            var pattern = Current[key];

            try
            {
                return string.Format(CultureInfo.CurrentCulture, pattern, values);
            }
            catch (FormatException e)
            {
                Log.Warning(e, "Text '{Key}' is malformed in {Language} ('{Pattern}') - " +
                               "falling back to English", key, Current.Language, pattern);

                if (!Current._fallback.TryGetValue(key, out var english)) return pattern;

                try
                {
                    return string.Format(CultureInfo.CurrentCulture, english, values);
                }
                catch (FormatException)
                {
                    // The English version doesn't match the passed values either - then
                    // the error is in the caller and not in the translation. The raw
                    // text is still better than an exception.
                    return english;
                }
            }
        }

        /// <summary>
        ///     The English text for a key, whatever language is set - for the lines that end
        ///     up in <c>smurftown.log</c>.
        ///     <para>
        ///         <b>Why the log needs its own rendering.</b> The progress steps of a game run
        ///         (<c>GameSession</c>, <c>CollectionReader</c>, <c>LootOpener</c>) travel as a
        ///         <see cref="ProgressStep" /> - a key and its arguments, not a finished string -
        ///         because two readers want opposite things from the same step:
        ///         <c>RunGuideViewModel</c> and <c>ReuseGuideViewModel</c> show it to the human
        ///         in their language via <see cref="Format" />, and <c>smurftown.log</c> wants
        ///         it in English regardless, per the rule two paragraphs up in this class. This
        ///         method is the log's half of that split.
        ///     </para>
        ///     <para>
        ///         <b>Until 24.08.2026 there was no display</b>, and this method rendered the
        ///         only copy that existed - which is why lines like
        ///         <c>25 von 31 Karten gelesen</c> used to stand in a German installation's log
        ///         before the funnel was written. The key/argument split above is what let the
        ///         funnel start reading the same steps without pulling them back into the log's
        ///         language.
        ///     </para>
        /// </summary>
        public static string ForLog(string key)
        {
            return Current._fallback.TryGetValue(key, out var english) ? english : Current[key];
        }

        /// <summary>
        ///     <see cref="ForLog" /> with placeholders. Deliberately without the
        ///     <see cref="FormatException" /> safety net of <see cref="Format" />: the English
        ///     text is the original, so if its placeholders do not match the values, the fault
        ///     is in the caller and there is nothing left to fall back to.
        /// </summary>
        public static string FormatForLog(string key, params object?[] values)
        {
            return string.Format(CultureInfo.InvariantCulture, ForLog(key), values);
        }

        /// <summary>
        ///     Loads a language and notifies everything that depends on it. Called at
        ///     start and after every save of the settings - the same design as
        ///     <c>InputSender.Pace</c> and <c>GameVocabulary.Current</c>.
        /// </summary>
        public static void Use(AppLanguage language)
        {
            Current.Load(language);
        }

        private void Load(AppLanguage language)
        {
            _fallback = Read(AppLanguage.English);
            _texts = language == AppLanguage.English ? _fallback : Read(language);
            Language = language;
            _complained.Clear();

            Log.Information("Interface language {Language} ({Count} texts, {Fallback} in English)",
                language, _texts.Count, _fallback.Count);

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
            Changed?.Invoke();
        }

        /// <summary>
        ///     Reads a language file. A custom one under <c>~/.smurftown/language/</c>
        ///     overrides the embedded one - the same pattern as with <c>screen-map.yaml</c>
        ///     and <c>rotation-calendar.yaml</c> and for the same reason: the installation
        ///     folder sits under <c>Program Files</c>, where you can't just drop
        ///     something. Whoever finds a typo should be able to fix it without
        ///     rebuilding the application.
        /// </summary>
        private static Dictionary<string, string> Read(AppLanguage language)
        {
            var tag = language.Tag();

            try
            {
                var local = Path.Combine(Directories.UserPath, "language", $"{tag}.yaml");
                if (File.Exists(local))
                {
                    Log.Information("Texts from {Path}", local);
                    return Parse(File.ReadAllText(local));
                }

                var resource = $"Smurftown.Backend.Texts.{tag}.yaml";
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);

                if (stream == null)
                {
                    Log.Error("Embedded texts {Resource} are missing - check the csproj entry",
                        resource);
                    return new Dictionary<string, string>(StringComparer.Ordinal);
                }

                using var reader = new StreamReader(stream);
                return Parse(reader.ReadToEnd());
            }
            catch (Exception e)
            {
                // A broken language file must not prevent startup. Without texts, the
                // key stands everywhere in exclamation marks - ugly, but usable, and
                // the settings remain reachable to switch back.
                Log.Error(e, "Texts for {Language} could not be read", language);
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static Dictionary<string, string> Parse(string yaml)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(NullNamingConvention.Instance)
                .Build();

            return deserializer.Deserialize<Dictionary<string, string>>(yaml)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private void Complain(string template, string key)
        {
            if (!_complained.Add(key)) return;
            Log.Warning(template, key, Language);
        }
    }
}
