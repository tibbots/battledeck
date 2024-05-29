using System.IO;
using Serilog;
using Smurftown.Backend.Automation;
using Smurftown.Backend.Entity;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
// YamlDotNet.Serialization brings its own class named Settings. The alias keeps the
// entity's name short, without every line here having to carry the full namespace.
using Settings = Smurftown.Backend.Entity.Settings;
// And System.IO brings its own TextReader - the same name as our OCR reader. Same
// solution, same reason.
using TextReader = Smurftown.Backend.Automation.TextReader;
using Smurftown.Backend.Texts;

namespace Smurftown.Backend.Gateway
{
    /// <summary>
    ///     The settings set by the human, in <c>~/.smurftown/settings.yaml</c>.
    ///     Hand-written singleton like <see cref="BattlenetAccountGateway" /> - no
    ///     holder, no container, the same pattern as everywhere here.
    /// </summary>
    public sealed class SettingsGateway
    {
        public static readonly SettingsGateway Instance = new();

        private readonly string _configFile;

        private readonly IDeserializer _yamlIn = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        private readonly ISerializer _yamlOut = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        private Settings _settings = new();

        private SettingsGateway()
        {
            _configFile = Path.Combine(Directories.UserPath, "settings.yaml");
            Load();
        }

        public InputSpeed InputSpeed => _settings.InputSpeed;

        public GameLanguage ClientLanguage => _settings.ClientLanguage;

        /// <summary>
        ///     The language of the UI - a different question than
        ///     <see cref="ClientLanguage" /> above, see <see cref="Entity.AppLanguage" />.
        /// </summary>
        public AppLanguage AppLanguage => _settings.AppLanguage;

        /// <summary>
        ///     The chosen path to the game, or the first of the usual locations if
        ///     nothing has been chosen yet. Empty if neither yields anything - then
        ///     <c>GameSession</c> reports that at start, instead of throwing an exception here.
        ///     <para>
        ///         The stored path is checked on every access: an uninstalled or moved
        ///         installation should not cause the app to insist on a dead path while a
        ///         valid one sits right next to it.
        ///     </para>
        /// </summary>
        public string HotsPath
        {
            get
            {
                if (_settings.HotsPath.Length > 0 && File.Exists(_settings.HotsPath))
                {
                    return _settings.HotsPath;
                }

                return GameInstallations.Likely().FirstOrDefault() ?? "";
            }
        }

        /// <summary>Did the human set a path themselves - as opposed to "found"?</summary>
        public bool HotsPathIsExplicit => _settings.HotsPath.Length > 0 && File.Exists(_settings.HotsPath);

        public void Save(Settings settings)
        {
            _settings = settings;
            try
            {
                File.WriteAllText(_configFile, _yamlOut.Serialize(settings));
                Log.Information("Settings saved: {Path}", _configFile);
            }
            catch (Exception e)
            {
                Log.Error(e, "Settings could not be saved: {Path}", _configFile);
                throw;
            }

            Apply();
        }

        /// <summary>
        ///     A copy for editing - the dialog should not hang on the saved state.
        ///     <para>
        ///         <b>Field-by-field and therefore a trap</b>: whatever is missing here
        ///         falls back to the default on the next save, without anything reporting
        ///         it anywhere. Whoever adds a field to <see cref="Settings" /> adds it
        ///         here too.
        ///     </para>
        /// </summary>
        public Settings Current()
        {
            return new Settings
            {
                HotsPath = _settings.HotsPath,
                InputSpeed = _settings.InputSpeed,
                ClientLanguage = _settings.ClientLanguage,
                AppLanguage = _settings.AppLanguage
            };
        }

        /// <summary>
        ///     Carries the settings to where they take effect. Called at start and after
        ///     every save.
        ///     <para>
        ///         <see cref="InputSender" /> doesn't fetch the value itself: <c>Automation</c>
        ///         doesn't know the gateways, and this direction is meant to stay that way.
        ///         That's why the gateway pushes the factor in instead of letting it be
        ///         fetched. The same applies word for word to <see cref="GameVocabulary" />.
        ///     </para>
        /// </summary>
        public void Apply()
        {
            InputSender.Pace = _settings.InputSpeed.Factor();
            GameVocabulary.Current = GameVocabulary.For(_settings.ClientLanguage);

            // The language decides not only WHAT it's compared against, but also WITH WHAT
            // it is read. Until 22.08.2026 recognition was fixed on German, and that got
            // by for English - Latin script, and the language model only helps with
            // ambiguities. For French and Spanish many letters carry accents, and there
            // it matters.
            TextReader.LanguageTag = _settings.ClientLanguage.OcrTag();

            // The language of the UI, and that is a different question than the three
            // lines above: those all concern the game - how fast we type in it, what we
            // compare against, with what we read. This one concerns ourselves.
            Strings.Use(_settings.AppLanguage);
        }

        private void Load()
        {
            if (!File.Exists(_configFile))
            {
                Log.Information("No settings.yaml, using the defaults");
                return;
            }

            try
            {
                _settings = _yamlIn.Deserialize<Settings>(File.ReadAllText(_configFile)) ?? new Settings();
            }
            catch (Exception e)
            {
                // A broken file must not prevent startup - the defaults carry the app,
                // and the reason is in the log.
                Log.Error(e, "settings.yaml unreadable, using the defaults: {Path}", _configFile);
                _settings = new Settings();
            }
        }
    }
}
