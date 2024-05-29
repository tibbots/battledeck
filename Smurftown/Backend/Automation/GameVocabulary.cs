using Smurftown.Backend.Entity;

namespace Smurftown.Backend.Automation
{
    /// <summary>
    ///     Every word that the text recognition needs to recognize in the game, in one place.
    ///     <para>
    ///         <b>Why this class exists</b>: the calibration (<c>screen-map.yaml</c>) says WHERE
    ///         something stands, and was never language-dependent - an English client does not shift a
    ///         single anchor. What is language-dependent is exclusively WHAT stands there. Until
    ///         21.08.2026, these words lay scattered as constants across four files
    ///         (<see cref="ProfileReader" />, <see cref="HeaderReader" />,
    ///         <see cref="CollectionReader" />, <see cref="HeroNameMatcher" />), each correct on its own
    ///         and not findable together.
    ///     </para>
    ///     <para>
    ///         <b>A second vocabulary, not a replaced one.</b> The German values are measured on the
    ///         running client and remain unchanged; whoever translates them makes the
    ///         recognition blind, without the build reporting anything.
    ///     </para>
    ///     <para>
    ///         <b>Both halves are measured on the running client</b>, on 21.08.2026. The
    ///         client can be switched under Options - "Sprache und Region" - "Sprache der Texte"
    ///         ; the English text version was not installed and was downloaded for
    ///         that. A restart is required, the calibration does NOT change in the process.
    ///     </para>
    ///     <para>
    ///         <b>One assumption was wrong</b>, and it shows why measuring was necessary: under
    ///         every collection card stands <c>OWNED</c>, not <c>Collected</c>. The latter was
    ///         guessed. The error would not have been noticed - the word only serves to save,
    ///         per card, one comparison against 90 names.
    ///     </para>
    ///     <para>
    ///         <b>Two values remain unmeasured</b> and are marked as such: the tiers
    ///         <c>Master</c> and <c>Grand Master</c> - no account on this machine carries
    ///         them - and the word for a newly acquired card (<c>new</c>).
    ///     </para>
    /// </summary>
    public sealed class GameVocabulary
    {
        /// <summary>
        ///     German. Every value measured on the running client - the origin stands at
        ///     the spot that uses the value, respectively.
        /// </summary>
        public static readonly GameVocabulary German = new(
            GameLanguage.German,
            "sturmliga",
            "spielerstufe",
            "platzierung",
            "BEUTE",
            ["erworben", "held", "neu"],
            new Dictionary<string, HotsRankTier>
            {
                ["bronze"] = HotsRankTier.Bronze,
                ["silber"] = HotsRankTier.Silver,
                ["gold"] = HotsRankTier.Gold,
                ["platin"] = HotsRankTier.Platinum,
                ["diamant"] = HotsRankTier.Diamond,
                ["master"] = HotsRankTier.Master,
                ["grossmeister"] = HotsRankTier.GrandMaster
            },
            collectionTab: "SAMMLUNG",
            heroesTab: "HELDEN");

        /// <summary>
        ///     English. Measured on the running client on 21.08.2026; what could not be
        ///     measured is marked below.
        /// </summary>
        public static readonly GameVocabulary English = new(
            GameLanguage.English,
            // Measured on GODOR#21291: "Storm League" / "Platinum 5". The space is
            // intentional - normalization happens before the comparison.
            "storm league",
            // Measured: "Player Level" / "241" over the same edge.
            "player level",
            // Measured on MUGGLE#21197, whose placements are open: under "Storm League"
            // stands "Placement" instead of a tier. Meant as a prefix (StartsWith), so
            // the value also covers a plural without a second entry.
            "placement",
            // Measured: the bar reads "PLAY | COLLECTION | LOOT | WATCH".
            "LOOT",
            // "owned" is MEASURED and was previously guessed as "collected" - under every card
            // stands "OWNED", and the sidebar reports "42/89 Owned". "hero" catches the
            // lines "LEGENDARY HERO" and "EPIC HERO" on not-owned cards, likewise
            // measured. "new" is NOT measured - a freshly acquired card would have had to
            // be on screen for that.
            //
            // All three are harmless: recalculated, not a single one of the 90
            // English hero names contains any of them as a substring.
            ["owned", "hero", "new"],
            new Dictionary<string, HotsRankTier>
            {
                ["bronze"] = HotsRankTier.Bronze,
                ["silver"] = HotsRankTier.Silver,
                ["gold"] = HotsRankTier.Gold,
                ["platinum"] = HotsRankTier.Platinum,
                ["diamond"] = HotsRankTier.Diamond,
                ["master"] = HotsRankTier.Master,
                // BOTH spellings, because neither one is measured: no account on
                // this machine is above Platinum. Blizzard's leaderboard heads the
                // page with "GRAND MASTER LEADERBOARDS", i.e. two words - whether the client writes
                // the same in the profile is thus NOT proven. Two keys cost
                // nothing, a wrong one costs the whole reading.
                ["grandmaster"] = HotsRankTier.GrandMaster,
                ["grand master"] = HotsRankTier.GrandMaster
            },
            // Measured: the bar reads "PLAY | COLLECTION | LOOT | WATCH", the
            // collection's sub-tab "HEROES".
            collectionTab: "COLLECTION",
            heroesTab: "HEROES");

        /// <summary>
        ///     French. Measured on the running client on 22.08.2026 (account JOKEY, region
        ///     Europa); what could not be measured is marked below.
        ///     <para>
        ///         <b>Four of six guessed values were wrong</b>, and none of them would
        ///         have been noticed when translating or in the log. The loot chest tab
        ///         is called <c>COFFRES</c> and not <c>BUTIN</c>; the label
        ///         above the rank is <c>Ligue Storm</c> - the proper name stays English - and
        ///         not <c>Ligue de tempête</c>; the tier above that is called <c>Niveau du
        ///         joueur</c> and not <c>de joueur</c>; and under an owned card stands
        ///         <c>DISPONIBLE</c> and not <c>obtenu</c>. This is exactly why the rule exists that
        ///         measurement happens on the running client.
        ///     </para>
        /// </summary>
        public static readonly GameVocabulary French = new(
            GameLanguage.French,
            // Measured on JOKEY#21643: in the PROGRESSION block stands "Ligue Storm" and below it
            // "Platine 5". "Storm" stays untranslated - the league name is a proper noun.
            "ligue storm",
            // Measured: "Niveau du joueur" / "90" over the same edge.
            "niveau du joueur",
            // NOT MEASURED - no account on this machine stood on French in
            // open placements. Meant as a prefix (StartsWith), so the value
            // also covers "Placements".
            "placement",
            // Measured: the bar reads "JOUER | COLLECTION | COFFRES | REGARDER".
            "COFFRES",
            // "disponible" is MEASURED and was guessed as "obtenu" - under every owned
            // card stands "DISPONIBLE", matching the filter entry "Héros disponibles".
            // "heros" catches the lines "HÉROS LÉGENDAIRE", "HÉROS ÉPIQUE" and "HÉROS RARE"
            // on not-owned cards, likewise measured. "nouveau" is NOT measured.
            //
            // Recalculated: none of the 89 French card names contains any of the three
            // as a substring.
            ["disponible", "heros", "nouveau"],
            new Dictionary<string, HotsRankTier>
            {
                // ONLY "platine" is measured (JOKEY is on Platinum 5). The remaining six
                // are the usual French rank ladder and unverified - if one is
                // off, that rank is not read and nothing is written.
                ["bronze"] = HotsRankTier.Bronze,
                ["argent"] = HotsRankTier.Silver,
                // Two letters, and that is fine: the comparison is against the WHOLE
                // line (RankPattern is anchored), not against an occurrence within it.
                ["or"] = HotsRankTier.Gold,
                ["platine"] = HotsRankTier.Platinum,
                ["diamant"] = HotsRankTier.Diamond,
                ["maitre"] = HotsRankTier.Master,
                ["grand maitre"] = HotsRankTier.GrandMaster
            },
            collectionTab: "COLLECTION",
            // "Héros" - and the accent does not matter, the comparison runs through TextNormalisation.
            heroesTab: "Héros",
            heroNames: FrenchHeroNames);

        /// <summary>
        ///     The 16 heroes whose French names differ from the English ones. Read off the
        ///     complete collection on 22.08.2026 (filter "Tout", sort
        ///     "Alphabétique", all 89 cards) and matched against the catalog; the four
        ///     cases where the name alone was not enough were resolved via the portrait.
        ///     <para>
        ///         <b>The other 73 are deliberately not listed here.</b> They are proper names and
        ///         the same in both versions - even where an accent is added: <c>Fénix</c>,
        ///         <c>Léoric</c>, <c>Méphisto</c>, <c>Orphéa</c>, <c>Tyraël</c>, and
        ///         <c>Malthaël</c> normalize to the same key as their English
        ///         name, and <c>Lt Morales</c> loses its period anyway.
        ///     </para>
        ///     <para>
        ///         <b>Four could only be resolved via the image</b>, because the name gives no clue:
        ///         <c>Kramer</c> is the red mech (Blaze), <c>Balafré</c> the bloated
        ///         undead (Stitches), <c>Lardeur</c> the gnoll (Hogger), and <c>EDN-OS</c> the
        ///         Protoss probe (Probius). Guessing here would have matched every one
        ///         to a different hero - "Balafré" (the scarred one) sounds like Blaze and is Stitches.
        ///     </para>
        ///     <para>
        ///         <c>Gazlow</c> and <c>Asmodan</c> differ by only one letter and
        ///         would still match within the matching threshold of 0.34. They are listed
        ///         here anyway: measured beats tolerated.
        ///     </para>
        ///     <para>
        ///         <c>Cho'gall</c> is deliberately missing - the double card lives in
        ///         <see cref="HeroNameMatcher" />, and it is named the same in French.
        ///     </para>
        /// </summary>
        private static Dictionary<string, string> FrenchHeroNames => new()
        {
            ["azmodan"] = "Asmodan",
            ["blaze"] = "Kramer",
            ["brightwing"] = "Luisaile",
            ["deathwing"] = "Aile de mort",
            ["gazlowe"] = "Gazlow",
            ["greymane"] = "Grisetête",
            ["hogger"] = "Lardeur",
            ["junkrat"] = "Chacal",
            ["murky"] = "Bourbie",
            ["nazeebo"] = "Nasibo",
            ["probius"] = "EDN-OS",
            ["sgt-hammer"] = "Sgt Marteau",
            ["stitches"] = "Balafré",
            ["the-butcher"] = "Le Boucher",
            ["the-lost-vikings"] = "Les Vikings perdus",
            ["whitemane"] = "Blanchetête"
        };

        /// <summary>
        ///     Spanish (Spain). Measured on the running client on 22.08.2026 (account JOKEY,
        ///     region Europa).
        ///     <para>
        ///         Unlike with <see cref="French" />, most of the guesswork here was right: only the
        ///         rank label was off (<c>Liga de la Tormenta</c> instead of
        ///         <c>Liga de tormentas</c>) and the word under an owned card
        ///         (<c>ARTÍCULO ADQUIRIDO</c> instead of <c>en propiedad</c>).
        ///     </para>
        /// </summary>
        public static readonly GameVocabulary SpanishSpain = new(
            GameLanguage.SpanishSpain,
            // Measured on JOKEY#21643: in the PROGRESIÓN block stands "Liga de la Tormenta" and
            // below it "Platino 5".
            "liga de la tormenta",
            // Measured: "Nivel de jugador" / "90".
            "nivel de jugador",
            // NOT MEASURED - no account stood on Spanish in open placements.
            "clasificacion",
            // Measured: the bar reads "JUGAR | COLECCIÓN | BOTÍN | REPETICIONES".
            "BOTÍN",
            // "adquirido" is MEASURED - under every owned card stands
            // "ARTÍCULO ADQUIRIDO". "heroe" catches "HÉROE LEGENDARIO", "HÉROE ÉPICO" and
            // "HÉROE POCO COMÚN", likewise measured. "nuevo" is NOT measured.
            //
            // Recalculated: none of the 89 Spanish card names contains any of the three.
            ["adquirido", "heroe", "nuevo"],
            SpanishTiers,
            collectionTab: "COLECCIÓN",
            heroesTab: "Héroes",
            heroNames: SpanishHeroNames);

        /// <summary>
        ///     The 14 heroes whose Spanish names differ from the English ones. Read off the
        ///     complete collection on 22.08.2026 and matched against the catalog.
        ///     <para>
        ///         Three could only be resolved via the meaning and none of them via the
        ///         sound: <c>Puntos</c> are the stitches (Stitches), <c>Vulcano</c> is the
        ///         firefighter (Blaze), and <c>Sondius</c> the Protoss probe (Probius).
        ///     </para>
        ///     <para>
        ///         <c>Cromi</c>, <c>Mefisto</c>, and <c>Orfea</c> deviate so little that the
        ///         matching would find them even without an entry - they are listed here anyway, for
        ///         the same reason as <c>Gazlow</c> in the French version.
        ///     </para>
        /// </summary>
        private static Dictionary<string, string> SpanishHeroNames => new()
        {
            ["blaze"] = "Vulcano",
            ["brightwing"] = "Alafeliz",
            ["chromie"] = "Cromi",
            ["deathwing"] = "Alamuerte",
            ["greymane"] = "Cringrís",
            ["lt-morales"] = "Tte. Morales",
            ["mephisto"] = "Mefisto",
            ["orphea"] = "Orfea",
            ["probius"] = "Sondius",
            ["sgt-hammer"] = "Sgto. Martillo",
            ["stitches"] = "Puntos",
            ["the-butcher"] = "El carnicero",
            ["the-lost-vikings"] = "Lost Vikings",
            ["whitemane"] = "Melenablanca"
        };

        /// <summary>
        ///     Spanish (Latin America), Blizzard's code <c>esMX</c>.
        ///     Measured on the running client on 22.08.2026 (account JOKEY, region Europa).
        ///     <para>
        ///         <b>And the dedicated instance paid off immediately.</b> The labels
        ///         are word for word the same as in <see cref="SpanishSpain" /> - but
        ///         <b>ten hero names</b> differ. Blaze is called <c>Blaze</c> here and in
        ///         Spain <c>Vulcano</c>, Orphea is called <c>Orphea</c> here and there
        ///         <c>Orfea</c>, <c>Sgto. Martillo</c> becomes <c>Sargento Maza</c>. Had
        ///         both versions been merged, half the cards in one of the two
        ///         would no longer match.
        ///     </para>
        ///     <para>
        ///         The difference is also visible at the margin: the navigation bar ends here
        ///         on <c>VER</c> instead of <c>REPETICIONES</c>, the profile block is called
        ///         <c>PROGRESO</c> instead of <c>PROGRESIÓN</c>, and the word under an owned
        ///         card is <c>ADQUIRIDO</c> instead of <c>ARTÍCULO ADQUIRIDO</c>. We need none
        ///         of these - but they show that these are two translations and not
        ///         two spellings.
        ///     </para>
        /// </summary>
        public static readonly GameVocabulary SpanishLatin = new(
            GameLanguage.SpanishLatin,
            // Measured: word-identical to the Spanish version.
            "liga de la tormenta",
            "nivel de jugador",
            // NOT MEASURED - no account stood in open placements.
            "clasificacion",
            "BOTÍN",
            // "adquirido" measured (here without the preceding "ARTÍCULO"), "heroe" catches
            // "HÉROE LEGENDARIO", "HÉROE ÉPICO" and "HÉROE RARO" - the last tier is called in
            // Spain "POCO COMÚN", which the word "heroe" covers both times.
            ["adquirido", "heroe", "nuevo"],
            SpanishTiers,
            collectionTab: "COLECCIÓN",
            heroesTab: "Héroes",
            heroNames: SpanishLatinHeroNames);

        /// <summary>
        ///     The 14 heroes whose Latin American Spanish names differ from the
        ///     English ones. Read off the complete collection on 22.08.2026.
        ///     <para>
        ///         Compared to <see cref="SpanishHeroNames" />, two entries have
        ///         <b>dropped out</b> - Blaze and Orphea are named here as in the original - and two
        ///         have <b>been added</b>: <c>Malthael</c> becomes <c>Maltael</c> and <c>Valla</c>
        ///         becomes <c>Vala</c>. Both differ by only one letter and would
        ///         be matched even without an entry; they are listed here because measured outweighs
        ///         tolerated.
        ///     </para>
        /// </summary>
        private static Dictionary<string, string> SpanishLatinHeroNames => new()
        {
            ["brightwing"] = "Alasol",
            ["chromie"] = "Cromi",
            ["deathwing"] = "Alamuerte",
            ["greymane"] = "Cringris",
            ["lt-morales"] = "Teniente Morales",
            ["malthael"] = "Maltael",
            ["mephisto"] = "Mefisto",
            ["probius"] = "Sondius",
            ["sgt-hammer"] = "Sargento Maza",
            ["stitches"] = "Puntos",
            ["the-butcher"] = "El Carnicero",
            ["the-lost-vikings"] = "Los Vikingos perdidos",
            ["valla"] = "Vala",
            ["whitemane"] = "Melenablanca"
        };

        /// <summary>
        ///     The tier words of both Spanish versions. UNVERIFIED.
        /// </summary>
        private static Dictionary<string, HotsRankTier> SpanishTiers => new()
        {
            ["bronce"] = HotsRankTier.Bronze,
            ["plata"] = HotsRankTier.Silver,
            ["oro"] = HotsRankTier.Gold,
            ["platino"] = HotsRankTier.Platinum,
            ["diamante"] = HotsRankTier.Diamond,
            ["maestro"] = HotsRankTier.Master,
            ["gran maestro"] = HotsRankTier.GrandMaster
        };

        private GameVocabulary(GameLanguage language, string rankLabel, string levelLabel,
            string placementWord, string lootTab, IReadOnlyList<string> notNames,
            IReadOnlyDictionary<string, HotsRankTier> tiers,
            string collectionTab, string heroesTab,
            IReadOnlyDictionary<string, string>? heroNames = null)
        {
            Language = language;
            RankLabel = rankLabel;
            LevelLabel = levelLabel;
            PlacementWord = placementWord;
            LootTab = lootTab;
            NotNames = notNames;
            Tiers = tiers;
            CollectionTab = collectionTab;
            HeroesTab = heroesTab;
            HeroNames = heroNames ?? new Dictionary<string, string>();
        }

        /// <summary>
        ///     The currently valid vocabulary. Publicly writable and set <b>from outside</b>
        ///     (<c>SettingsGateway.Apply</c>), exactly like <see cref="InputSender.Pace" />
        ///     and for the same reason: <c>Backend/Automation</c> does not know the gateways and
        ///     should not get to know them.
        /// </summary>
        public static GameVocabulary Current { get; set; } = German;

        public GameLanguage Language { get; }

        /// <summary>The label under which the rank stands in the profile overlay.</summary>
        public string RankLabel { get; }

        /// <summary>The label under which the account level stands in the profile overlay.</summary>
        public string LevelLabel { get; }

        /// <summary>
        ///     The word that stands instead of a rank, while placement matches are open.
        ///     The comparison is done via <c>StartsWith</c>.
        /// </summary>
        public string PlacementWord { get; }

        /// <summary>
        ///     The navigation bar tab whose badge carries the number of unopened
        ///     loot chests. The comparison is against the RAW text and without regard to
        ///     case - unlike the labels above, which run through normalization first.
        /// </summary>
        public string LootTab { get; }

        /// <summary>
        ///     Words that can appear in the name strip of a collection card but are not one.
        ///     They would have failed the matching distance threshold anyway - naming them here
        ///     saves one comparison against 90 names per card and keeps the log
        ///     readable. The comparison is via <c>Contains</c>, so a too-general word would take
        ///     real names down with it.
        /// </summary>
        public IReadOnlyList<string> NotNames { get; }

        /// <summary>The tier words, normalized (lowercase, without umlauts and eszett).</summary>
        public IReadOnlyDictionary<string, HotsRankTier> Tiers { get; }

        /// <summary>
        ///     The collection's tab in the topmost bar, and below it the heroes
        ///     sub-tab. Both are <b>searched for</b> and not clicked where the calibration
        ///     presumes them - see <see cref="TabFinder" />: a tab row lays its entries
        ///     side by side by text width, and a longer word further left shifts
        ///     all the following ones.
        ///     <para>
        ///         Until 22.08.2026, fixed points stood here in <c>screen-map.yaml</c>
        ///         (<c>collection.tab</c> at x=399, <c>collection.heroesTab</c> at x=497),
        ///         both measured on a German client. They matched every other
        ///         version at best by coincidence - and a missed click opens the
        ///         wrong screen instead of aborting.
        ///     </para>
        /// </summary>
        public string CollectionTab { get; }

        /// <inheritdoc cref="CollectionTab" />
        public string HeroesTab { get; }

        /// <summary>
        ///     The heroes whose name in this language <b>differs</b> from the English one -
        ///     id to display name. Whoever is missing here carries <see cref="HotsHero.Name" />.
        ///     <para>
        ///         <b>Only the deviations</b>, and that is the core of it: of 90 heroes, the
        ///         vast majority are proper names (Arthas, Diablo, Illidan) and stand the
        ///         same in every version. What gets translated is what means something - "The Butcher",
        ///         "Stitches", "Brightwing". A complete name list per language would be nine tenths
        ///         the same line five times over, and in that noise a wrong entry would
        ///         not stand out.
        ///     </para>
        ///     <para>
        ///         <b>German is not listed here</b>, but as <see cref="HotsHero.GermanName" />
        ///         in the catalog - it came from <c>tools/hero-names-de.json</c> and thus from
        ///         the same run as the id. For the other languages this
        ///         source does not exist: Blizzard's hero page now renders its names via
        ///         JavaScript, verified on 22.08.2026. They are therefore measured on the running
        ///         client, and what is measured belongs next to the other measured values.
        ///     </para>
        /// </summary>
        public IReadOnlyDictionary<string, string> HeroNames { get; }

        /// <summary>
        ///     The name under which this hero stands in the collection.
        /// </summary>
        public string HeroName(HotsHero hero)
        {
            if (Language == GameLanguage.German) return hero.GermanName;
            return HeroNames.TryGetValue(hero.Id, out var name) ? name : hero.Name;
        }

        public static GameVocabulary For(GameLanguage language)
        {
            return language switch
            {
                GameLanguage.English => English,
                GameLanguage.French => French,
                GameLanguage.SpanishSpain => SpanishSpain,
                GameLanguage.SpanishLatin => SpanishLatin,
                _ => German
            };
        }
    }
}
