namespace Smurftown.Backend.Entity
{
    /// <summary>
    ///     How fast the app types and clicks in the game.
    ///     <para>
    ///         A factor on the pauses and not twenty-five individual values: the timings in
    ///         <c>InputSender</c> stand in a fixed ratio to each other that someone once
    ///         felt out by hand. Individually adjustable they would be an invitation to
    ///         break apart this ratio without noticing which of the values was just the
    ///         deciding one.
    ///     </para>
    ///     <para>
    ///         The <b>time limits</b> (<c>WindowTimeout</c> and its siblings) deliberately do
    ///         NOT depend on this. They wait for the game, not for us - a tighter value
    ///         speeds up nothing there, it only gives up sooner.
    ///     </para>
    /// </summary>
    public enum InputSpeed
    {
        Slow,
        Normal,
        Fast
    }

    public static class InputSpeeds
    {
        /// <summary>
        ///     The factor on every pause. <c>Fast</c> halves it, <c>Slow</c> adds three
        ///     quarters on top - none of this is measured, they are levels to try out. Whoever
        ///     shifts one of them does it in this one place.
        /// </summary>
        public static double Factor(this InputSpeed speed)
        {
            return speed switch
            {
                InputSpeed.Fast => 0.5,
                InputSpeed.Slow => 1.75,
                _ => 1.0
            };
        }

        public static string DisplayName(this InputSpeed speed)
        {
            return Texts.Strings.Current[$"speed.{speed.ToString().ToLowerInvariant()}"];
        }
    }

    /// <summary>
    ///     The language in which the game client runs.
    ///     <para>
    ///         It stands here and not in the calibration: <c>screen-map.yaml</c> describes
    ///         WHERE something stands - the language decides WHAT stands there. Both change
    ///         independently of each other, an English client doesn't shift a single anchor.
    ///     </para>
    ///     <para>
    ///         What it is needed for is described at <see cref="Automation.GameVocabulary" />:
    ///         OCR compares against words that stand in the game, and those are
    ///         translated. Without the right language, nothing is recognized and - that is
    ///         the benefit of this design - nothing wrong is written either.
    ///     </para>
    /// </summary>
    public enum GameLanguage
    {
        German,
        English,
        French,
        SpanishSpain,
        SpanishLatin
    }

    public static class GameLanguages
    {
        /// <summary>
        ///     How the game client names the version - <b>in its own language</b>, because
        ///     that is exactly how the selection list under "Optionen - Sprache und Region -
        ///     Sprache der Texte" is labeled.
        ///     <para>
        ///         This is the one justified deviation from "everything a human sees is
        ///         English" (CLAUDE.md): the value of this setting is only useful if it can
        ///         be held word for word against what stands in the game. A translated
        ///         "Spanish (Spain)" would force the human to map it themselves -
        ///         and that is exactly where the error this setting is meant to prevent arises.
        ///     </para>
        /// </summary>
        public static string DisplayName(this GameLanguage language)
        {
            return language switch
            {
                GameLanguage.English => "English (US)",
                GameLanguage.French => "Français",
                GameLanguage.SpanishSpain => "Español (ES)",
                GameLanguage.SpanishLatin => "Español (AL)",
                _ => "Deutsch"
            };
        }

        /// <summary>
        ///     Blizzard's id for the version, as it stands in <c>Variables.txt</c> under
        ///     <c>localeiddata</c> and in <c>.build.info</c> in the <c>Tags</c> column
        ///     (<c>frFR text?</c>). It is not needed for switching - the human does that
        ///     in the game - but for <b>checking</b>: it is the only place from which you
        ///     can verify from the outside which version the client is actually
        ///     running.
        ///     <para>
        ///         Read off the running client on 22.08.2026; <c>esMX</c> is the id of
        ///         "Español (AL)" - AL stands for América Latina, not for a country.
        ///     </para>
        /// </summary>
        public static string LocaleTag(this GameLanguage language)
        {
            return language switch
            {
                GameLanguage.English => "enUS",
                GameLanguage.French => "frFR",
                GameLanguage.SpanishSpain => "esES",
                GameLanguage.SpanishLatin => "esMX",
                _ => "deDE"
            };
        }

        /// <summary>
        ///     The language with which <see cref="Automation.TextReader" /> sets up OCR
        ///     - a BCP-47 tag for <c>Windows.Media.Ocr</c>.
        ///     <para>
        ///         <b>Both Spanish versions share <c>es</c></b>, and that is not an
        ///         oversight: the difference between Spain and Latin America lies in word
        ///         choice, not in the script. A dedicated recognizer would bring nothing and
        ///         would be a second language pack that would have to be installed.
        ///     </para>
        /// </summary>
        public static string OcrTag(this GameLanguage language)
        {
            return language switch
            {
                GameLanguage.English => "en",
                GameLanguage.French => "fr",
                GameLanguage.SpanishSpain => "es",
                GameLanguage.SpanishLatin => "es",
                _ => "de"
            };
        }
    }

    /// <summary>
    ///     The language in which <b>Smurftown itself</b> speaks - labels, hints,
    ///     toasts, tooltips.
    ///     <para>
    ///         <b>Not to be confused with <see cref="GameLanguage" />.</b> One says in
    ///         which language the game client runs; this one says in which the human wants
    ///         to read. Those are two questions, and they have different answers: whoever
    ///         runs a French client because their account is there does not necessarily
    ///         want a French UI because of that.
    ///     </para>
    ///     <para>
    ///         <b>Spanish exists only once here</b>, unlike in the game. The split into
    ///         <c>SpanishSpain</c> and <c>SpanishLatin</c> hangs there on the <i>hero names</i>
    ///         - Blaze is called <c>Vulcano</c> in Spain and <c>Blaze</c> in Latin America. A
    ///         word like "Speichern" or "Abbrechen" does not separate the two versions, so
    ///         a second Spanish file would be the same file twice.
    ///     </para>
    ///     <para>
    ///         <b>English is the first value and therefore the enum default.</b> That is
    ///         intentional: the texts in the code ARE English, <c>en.yaml</c> is the source
    ///         and every other version its translation. If a language fails, English is the
    ///         floor.
    ///     </para>
    /// </summary>
    public enum AppLanguage
    {
        English,
        German,
        French,
        Spanish
    }

    public static class AppLanguages
    {
        /// <summary>
        ///     How the version names itself - <b>in its own language</b>, for the
        ///     same reason as with <see cref="GameLanguages.DisplayName" />: a selection
        ///     list in which one's own language appears translated is useless for whoever
        ///     is looking for it. Whoever only reads French won't find "French".
        /// </summary>
        public static string DisplayName(this AppLanguage language)
        {
            return language switch
            {
                AppLanguage.German => "Deutsch",
                AppLanguage.French => "Français",
                AppLanguage.Spanish => "Español",
                _ => "English"
            };
        }

        /// <summary>
        ///     The file name of the vocabulary without extension - <c>UI/Language/{Tag}.yaml</c>,
        ///     embedded in the application.
        /// </summary>
        public static string Tag(this AppLanguage language)
        {
            return language switch
            {
                AppLanguage.German => "de",
                AppLanguage.French => "fr",
                AppLanguage.Spanish => "es",
                _ => "en"
            };
        }

        /// <summary>
        ///     The default for a <c>settings.yaml</c> that doesn't have the key yet:
        ///     the language of the Windows UI, as long as we speak it.
        ///     <para>
        ///         <b>Not German as the default</b>, even though the author is German - that
        ///         would be the wrong language for everyone else and would first have to be
        ///         found and changed. And not the <i>client</i> language: at first launch
        ///         that still sits on its own default and knows nothing about the human.
        ///     </para>
        ///     <para>
        ///         <c>CurrentUICulture</c> is read and not <c>CurrentCulture</c>: the
        ///         second says how numbers and dates are formatted, the first, in
        ///         which language Windows talks to the human. A German with
        ///         English Windows wants the second answer.
        ///     </para>
        /// </summary>
        public static AppLanguage FromSystem()
        {
            return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                switch
                {
                    "de" => AppLanguage.German,
                    "fr" => AppLanguage.French,
                    "es" => AppLanguage.Spanish,
                    _ => AppLanguage.English
                };
        }
    }

    /// <summary>
    ///     What the human sets, as opposed to what the app measures or reads.
    ///     Lives in <c>~/.smurftown/settings.yaml</c>.
    ///     <para>
    ///         Its own file and <b>not</b> in <c>screen-map.yaml</c>: the calibration
    ///         describes what the game looks like - anchors, distances, thresholds. Where
    ///         it's installed doesn't belong there. Until 21.08.2026 the path still stood
    ///         there anyway, and whoever wanted to change it had to touch a file full of
    ///         image coordinates.
    ///     </para>
    ///     <para>
    ///         As everywhere here: new fields without <c>required</c> and with a
    ///         sensible default, then an older file needs no migration.
    ///     </para>
    /// </summary>
    public sealed class Settings
    {
        /// <summary>
        ///     Full path to <c>Support64\HeroesSwitcher_x64.exe</c>. Empty means
        ///     "not chosen yet" - then <see cref="Gateway.SettingsGateway" /> scans the
        ///     usual locations on access.
        /// </summary>
        public string HotsPath { get; set; } = "";

        public InputSpeed InputSpeed { get; set; } = InputSpeed.Normal;

        /// <summary>
        ///     The language of the game client. Default <c>German</c> and not the system
        ///     language: until 21.08.2026 German was hard-wired, and an old
        ///     <c>settings.yaml</c> without this key is meant to keep behaving exactly
        ///     as before.
        /// </summary>
        public GameLanguage ClientLanguage { get; set; } = GameLanguage.German;

        /// <summary>
        ///     The language of the UI. Default is the <b>system language</b>
        ///     (<see cref="AppLanguages.FromSystem" />) and not a fixed value - unlike
        ///     <see cref="ClientLanguage" />, where German had to stay the default because
        ///     it was hard-wired before and an old file shouldn't change.
        ///     <para>
        ///         There is no "before" here: until 22.08.2026 the app only spoke English,
        ///         so any existing <c>settings.yaml</c> without this key is one whose
        ///         owner never had a choice.
        ///     </para>
        /// </summary>
        public AppLanguage AppLanguage { get; set; } = AppLanguages.FromSystem();
    }
}
