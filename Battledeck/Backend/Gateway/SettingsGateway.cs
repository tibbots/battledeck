using System.IO;
using Serilog;
using Battledeck.Backend.Automation;
using Battledeck.Backend.Entity;
// YamlDotNet.Serialization brings its own class named Settings. The alias keeps the
// entity's name short, without every line here having to carry the full namespace.
using Settings = Battledeck.Backend.Entity.Settings;
// And System.IO brings its own TextReader - the same name as our OCR reader. Same
// solution, same reason.
using TextReader = Battledeck.Backend.Automation.TextReader;
using Battledeck.Backend.Texts;

namespace Battledeck.Backend.Gateway
{
    /// <summary>
    ///     The settings set by the human - the <c>settings</c> section of
    ///     <c>~/.smurftown/app.yaml</c>. Hand-written singleton like
    ///     <see cref="BattlenetAccountGateway" /> - no holder, no container, the same pattern
    ///     as everywhere here.
    ///     <para>
    ///         It owned <c>settings.yaml</c> until 1.3.0. Reading and writing now go through
    ///         <see cref="AppFile" />, which re-reads the whole file before every write and
    ///         replaces only this section - so the hourly update check can no longer carry a
    ///         stale copy of these values back onto disk.
    ///     </para>
    /// </summary>
    public sealed class SettingsGateway
    {
        public static SettingsGateway Instance => Singleton.Value;

        private static readonly Lazy<SettingsGateway> Singleton = new(() => new SettingsGateway(AppFile.Instance));

        private readonly AppFile _app;

        /// <summary>
        ///     Reads the settings out of <paramref name="app" />. The file is handed in and
        ///     not fetched, for the reason given at
        ///     <see cref="BattlenetAccountGateway(string)" /> - and because two
        ///     <see cref="AppFile" /> instances on one path would each cache their own picture
        ///     of it.
        /// </summary>
        public SettingsGateway(AppFile app)
        {
            _app = app;
        }

        /// <summary>
        ///     The stored values. Always asked of <see cref="AppFile" /> and never kept in a
        ///     field of this class: the file is re-read on every write, so a copy here would
        ///     be the stale picture the whole arrangement exists to avoid.
        /// </summary>
        private Settings Stored => _app.State.Settings;

        public InputSpeed InputSpeed => Stored.InputSpeed;

        public GameLanguage ClientLanguage => Stored.ClientLanguage;

        /// <summary>
        ///     The language of the UI - a different question than
        ///     <see cref="ClientLanguage" /> above, see <see cref="Entity.AppLanguage" />.
        /// </summary>
        public AppLanguage AppLanguage => Stored.AppLanguage;

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
                if (Stored.HotsPath.Length > 0 && File.Exists(Stored.HotsPath))
                {
                    return Stored.HotsPath;
                }

                return GameInstallations.Likely().FirstOrDefault() ?? "";
            }
        }

        /// <summary>Did the human set a path themselves - as opposed to "found"?</summary>
        public bool HotsPathIsExplicit => Stored.HotsPath.Length > 0 && File.Exists(Stored.HotsPath);

        public void Save(Settings settings)
        {
            try
            {
                _app.SaveSettings(settings);
                Log.Information("Settings saved");
            }
            catch (Exception e)
            {
                Log.Error(e, "Settings could not be saved");
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
                HotsPath = Stored.HotsPath,
                InputSpeed = Stored.InputSpeed,
                ClientLanguage = Stored.ClientLanguage,
                AppLanguage = Stored.AppLanguage
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
            InputSender.Pace = Stored.InputSpeed.Factor();
            GameVocabulary.Current = GameVocabulary.For(Stored.ClientLanguage);

            // The language decides not only WHAT it's compared against, but also WITH WHAT
            // it is read. Until 22.08.2026 recognition was fixed on German, and that got
            // by for English - Latin script, and the language model only helps with
            // ambiguities. For French and Spanish many letters carry accents, and there
            // it matters.
            TextReader.LanguageTag = Stored.ClientLanguage.OcrTag();

            // The language of the UI, and that is a different question than the three
            // lines above: those all concern the game - how fast we type in it, what we
            // compare against, with what we read. This one concerns ourselves.
            Strings.Use(Stored.AppLanguage);
        }
    }
}
