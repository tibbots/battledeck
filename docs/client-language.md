# The Language of the Game Client

OCR compares against words that appear in the game — and those are translated. What
depends on that and what does not decides how much work another language takes.

> **Not to be confused with the language of the application.** Since 22.08.2026,
> Smurftown's own interface can be switched as well, and that is an entirely different
> matter: there it is about text **we** write, here about words that appear in the
> **game**. The former are translated, the latter are measured — whoever translates
> within one of this file's vocabularies makes the recognition blind. Both settings are
> independent of each other: a French client with a German interface is a valid state.
> What depends on the app language is covered in [localisation.md](localisation.md);
> this file covers only the client language.

## Switching it

**Optionen → Sprache und Region.** The setting is reachable from the login screen (the
"Optionen" button, bottom right) and, in the running game, via the gear menu.

There are **two** selection lists there, and only the second one matters to us:

| List | Affects |
|---|---|
| Language of dialogue and cutscenes | Voice-over — irrelevant to OCR |
| **Language of on-screen text** | every label on screen |

Five variants: `English (US)`, `Français`, `Deutsch`, `Español (ES)`, `Español (AL)`.
They are listed one below another in this order, with a row spacing of **38** — the
same as the region list on the login screen.

**The dialog's tab list is not stable** — and that is the trap that cost time here. On
the same night, two different lists appeared in the same spot:

| Run | Tabs | "Sprache und Region" |
|---|---|---|
| first start, login screen | …, Gameplay, Geselligkeit, **Sprache und Region**, Zuschauer-Interface | 6th, y = 669 |
| later, login screen as in-game | …, **Sprachchat**, Maus, Gameplay, Social, **Sprache und Region** | 7th, y = 752 |

The difference is a tab for **Sprachchat**, which was missing on the first run and
present later; in exchange, the one for Zuschauer-Interface disappeared. What this
depends on is not established — not on logging in, at any rate: both forms occurred
both before and after logging in.

**So don't calibrate it, look at it instead.** A fixed y-value hits the neighbor in half
the cases, and here the neighbor is "Social" — harmless, but then you're looking for
the selection list in a dialog that doesn't have it.

**A restart is required**, and a text variant that isn't installed is downloaded first.
Measured: French on 22.08.2026 took roughly **nine minutes** at 0.7–1.3 MB/s, English
took just under half an hour the first time — the rate varies widely, the volume is a
good gigabyte per variant.

**Progress is visible from the outside**, without looking at the screen: in
`HeroesData/data/`, the latest `data.NNN` file keeps growing. That is the only reliable
way, because the client shows nothing but its 404×143 loading screen the whole time, and
Battle.net still reports "Wird aktuell gespielt" unchanged.

**`.build.info` is not a reliable completion signal.** The `Tags` column already listed
`esES text?` before the Spanish variant had loaded — the entry is created by the
*request*, not by the data. Conversely, `frFR` was still not in there after switching,
even though the switch had taken effect.

**What actually applies is in `Variables.txt`** (`Dokumente\Heroes of the Storm\`):
`localeiddata` is the text variant, `localeidassets` the voice-over. The file is written
on exit; changing it **does not replace the path through the options** menu, because the
client only triggers the download when the switch was made through the menu.

**The game offers the restart but does not carry it out** — it quits and has to be
started again by hand.

## What does not change

**The calibration.** An English client does not shift a single anchor; verified against
the login screen, the collection, and the profile overlay. The language decides **what**
stands there, not **where**.

**The order of selection lists.** The collection filter reads, in both languages, as
`Alle Helden / Erworbene Helden / Nicht erworbene` and `All Heroes / Owned Heroes /
Unowned Heroes` respectively, and the sort order as `Veröffentlichung / Preis auf / Preis
ab / Alphabetisch`. Whoever clicks entries by their **index** survives the language
switch.

## What changes

**The vocabularies** — they live entirely in
`Smurftown/Backend/Automation/GameVocabulary.cs`, and only there:

| What | German | English | French | Spanish (ES) | Spanish (AL) |
|---|---|---|---|---|---|
| Label above the rank | `Sturmliga` | `Storm League` | `Ligue Storm` | `Liga de la Tormenta` | *same as ES* |
| Label above the account level | `Spielerstufe` | `Player Level` | `Niveau du joueur` | `Nivel de jugador` | *same as ES* |
| in place of a tier when placement is open | `Platzierung` | `Placement` | *not measured* | *not measured* | *not measured* |
| Tab of the collection | `SAMMLUNG` | `COLLECTION` | `COLLECTION` | `COLECCIÓN` | *same as ES* |
| Sub-tab of the heroes | `Helden` | `Heroes` | `Héros` | `Héroes` | *same as ES* |
| Tab of the loot chests | `BEUTE` | `LOOT` | `COFFRES` | `BOTÍN` | *same as ES* |
| Line under an owned card | `ERWORBEN` | `OWNED` | `DISPONIBLE` | `ARTÍCULO ADQUIRIDO` | `ADQUIRIDO` |
| Line under a card not owned | `… HELD` | `… HERO` | `HÉROS …` | `HÉROE …` | `HÉROE …` |
| Target count in the sidebar | `42/89 erworben` | `42/89 Owned` | `Vous en possédez 26/89` | `Artículos adquiridos: 26/89` | `Adquiridos: 26/89` |
| Tiers | Bronze, Silber, Gold, Platin, Diamant, Master, Großmeister | Bronze, Silver, Gold, Platinum, Diamond, Master, Grand Master | Bronze, Argent, Or, **Platine**, Diamant, Maître, Grand Maître | Bronce, Plata, Oro, **Platino**, Diamante, Maestro, Gran Maestro | *same as ES* |

**The two Spanish variants are identical in their labels** — and are nevertheless two
translations: the navigation bar ends in `REPETICIONES` (ES) versus `VER` (AL), the
profile block is called `PROGRESIÓN` versus `PROGRESO`, and **ten hero names** diverge.
Whoever merges the two loses half the card recognition in one of them.

The first bar reads `JOUER | COLLECTION | COFFRES | REGARDER` and
`JUGAR | COLECCIÓN | BOTÍN | REPETICIONES` respectively — the last tab is, in neither
variant, a literal translation of the English `WATCH`.

**Four of six guessed French words were wrong** (22.08.2026): the loot tab is called
`COFFRES` and not `BUTIN`, the rank label is `Ligue Storm` — the league name stays in
English — and not `Ligue de tempête`, the tier label is `Niveau **du** joueur` and not
`de joueur`, and under an owned card it reads `DISPONIBLE` and not `obtenu`. What is set
in bold is verified on the client; the remaining tier words are the common rank ladder
and unverified.

**The target count doesn't sit in the same spot everywhere**: in German the line starts
with the number, in French it ends with it. The expression `(\d+)\s*/\s*(\d+)` searches
only for the pair of numbers and therefore covers both forms — assuming a fixed position
would be the mistake.

**The hero names.** How many diverge depends on the language: in German it is **eight**,
in French **sixteen**.

| English | German | French | Spanish (ES) | Spanish (AL) |
|---|---|---|---|---|
| Brightwing | Funkelchen | Luisaile | Alafeliz | **Alasol** |
| Deathwing | Todesschwinge | Aile de mort | Alamuerte | Alamuerte |
| Greymane | Graumähne | Grisetête | Cringrís | Cringris |
| Sgt. Hammer | Sergeant Hammer | Sgt Marteau | Sgto. Martillo | **Sargento Maza** |
| Stitches | Kleiner | Balafré | Puntos | Puntos |
| The Butcher | Der Schlächter | Le Boucher | El carnicero | El Carnicero |
| The Lost Vikings | Lost Vikings | Les Vikings perdus | Lost Vikings | **Los Vikingos perdidos** |
| Whitemane | Weißsträhne | Blanchetête | Melenablanca | Melenablanca |
| Probius | — | EDN-OS | Sondius | Sondius |
| Blaze | — | Kramer | Vulcano | **Blaze** |
| Chromie | — | — | Cromi | Cromi |
| Lt. Morales | — | — | Tte. Morales | **Teniente Morales** |
| Mephisto | — | — | Mefisto | Mefisto |
| Orphea | — | — | Orfea | **Orphea** |
| Malthael | — | — | — | **Maltael** |
| Valla | — | — | — | **Vala** |
| Azmodan | — | Asmodan | — | — |
| Gazlowe | — | Gazlow | — | — |
| Hogger | — | Lardeur | — | — |
| Junkrat | — | Chacal | — | — |
| Murky | — | Bourbie | — | — |
| Nazeebo | — | Nasibo | — | — |

**Eight diverge in every translated variant** — the ones with a meaningful name, where
there was something to translate. Everything below that is a matter of taste for the
respective localization: French also translates `Junkrat` and `Murky`, Spanish leaves
those be and instead respells `Chromie` and `Mephisto` phonetically.

**Bold marks where the two Spanish variants diverge** — at ten of ninety names. That is
the proof that they need two instances: `Blaze` is called the same as the original in
Latin America and `Vulcano` in Spain, and `Orphea` exactly the other way around.

The counts: German **8**, French **16**, Spanish (ES) **14**, Spanish (AL) **14**.

**German sits in the catalog, everything else sits alongside it.** `HotsHero.GermanName`
came from `tools/hero-names-de.json` and thus from the same run as the identifier. For
further languages this source no longer exists: Blizzard's hero page now renders its
names via JavaScript (checked on 22.08.2026 for `de-de`, `fr-fr`, `es-es` and `es-mx` —
not a single name appears in the delivered HTML). They are therefore measured at the
client and kept as a **deviation table** in `GameVocabulary`; whoever is missing there
carries the English name.

**Only the deviations, not all ninety.** A complete list per language would, nine-tenths
of the time, be the same row five times over — and in that noise a wrong entry would go
unnoticed by anyone. Accents don't need an entry anyway: `Fénix`, `Léoric`, `Méphisto`,
`Orphéa`, `Tyraël` and `Malthaël` normalize to the same key as their English name.

**Four mappings could only be resolved from the image**, because the name gives no clue
and the guess leads astray: `Balafré` means "the scarred one" and sounds like Blaze — it
is **Stitches**. `Kramer` is the red mech (**Blaze**), `Lardeur` the gnoll (**Hogger**),
`EDN-OS` the protoss probe (**Probius**). Whoever browses the collection uses the search
bar for such cases and looks at the portrait.

**The position of text-width-dependent elements**, see [calibration.md](calibration.md).
Measured: `COLLECTION` sits at x≈379 in French, where German `SAMMLUNG` sits at 399 —
because `JOUER` is shorter than `SPIELEN` and drags everything behind it along. The same
20 points separate the sub-tabs (`Héros` at 476 versus `Helden` at 497). Both points
stood as fixed coordinates in `screen-map.yaml` until 22.08.2026 and are now word
searches.

> **And it isn't enough to just search for the word — you have to take the right one.**
> In the sub-tab row, `Packs de héros` sits **to the left** of `Héros`; likewise in
> German `Heldenpakete` sits before `Helden`, and in English `Hero Packs` before
> `Heroes`. Whoever takes the first match consistently opens the purchase screen instead
> of the hero list — silently, because something was clicked after all. `TabFinder`
> therefore takes the **shortest** match.

## What OCR needs

**A Windows language pack per language**, and that is a harder limit than any
vocabulary. `Windows.Media.Ocr` can only do what is installed:

```
[Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages
```

On the work machine, on 22.08.2026, exactly one entry appeared there: `de-DE`.

**For Latin script, that's no big deal.** A German recognizer reads French and Spanish
labels; the language model only helps with ambiguities, the letter shapes are the same.
It gets worse with **accents** — and those are common in both languages.

**That is why accents are stripped on both sides**, in the vocabulary as well as in the
recognized text (`TextNormalisation`). `maître` becomes `maitre`, `BOTÍN` becomes
`BOTIN`. The price would be that two words which differ only in their accent become
indistinguishable — that does not occur in any of the five variants.

**For Cyrillic or East Asian script, nothing would work at all.** No amount of stripping
helps there, only the language pack:

```
DISM /Online /Add-Capability /CapabilityName:Language.OCR~~~ru-RU~0.0.1.0
```

That requires administrator rights and is therefore a matter for the human, not the
application. Heroes of the Storm, in any case, only offers variants written in Latin
script, as long as you don't switch to the Chinese client.

**The app falls back, but it says so.** If the package for the configured language is
missing, `TextReader` takes the languages from the user profile and writes a warning
with both names to the log. A silent fallback would be the worst outcome here: something
would still be read, just worse, and nobody would know why.

## How to validate a word list

**On the running client, not from memory.** Of eight guessed English words, one was
wrong: under a collection card it reads `OWNED`, not `Collected`. The error would never
have surfaced — the word only serves to save, per card, a comparison against 90 names;
it doesn't abort anything and logs nothing.

**One account per state.** The tier words are shown only by an account with a valid
rank; whoever is stuck in placements shows the placement word instead of the tier. For
the highest tiers you accordingly need an account that carries them — otherwise they
remain unmeasured, and that has to be noted as such.

**Keep multi-word terms in mind.** `Großmeister` is one word, `Grand Master` is two. An
expression that reads a tier word as `[a-z]+` fails on this — silently, because an
unrecognized line is the same case as a missing one.

**Calculate what a new word does, to counter-check.** The filler words are checked
against every card line. Before adding one, check that none of the 90 names contains the
word as a substring — otherwise you filter out real cards. Two further calculations go
with this: that no two names normalize to the same key, and how many pairs fall below
the matching threshold of 0.34.

**Compute it directionally.** `HeroNameMatcher` divides the distance by the length of
the **candidate**, not by the longer of the two strings. With the wrong formula you get
three pairs instead of five and miss half of them.

Calculated on 22.08.2026 across all four variants: no filler word is embedded in a name,
no two names collide, and the number of close pairs stays at five — **except in French,
where it is seven**. The two additional ones are `kramer`↔`tracer`: Blaze is called
Kramer there and thus sits as close to Tracer as Rehgar does to Rexxar. No reason to
change the measured name — but the reason this calculation belongs with every variant.

## How to measure on the client

**The game may be remote-controlled for this**, and `tools/drive-hots.ps1` exists for
that — start page, clicks, keys, mouse wheel, captures, plus `user:`/`pw:`, which pull
the credentials from `~/.smurftown/data.yaml` so that no password goes over a command
line.

The approach that has proven itself, in this order:

1. **Log in** — don't forget the region, it resets to Americas after every start and
   every logout.
2. **Capture the navigation bar**: it holds three of the tab words at once.
3. **Profile overlay** (right-click on the portrait → "Profil ansehen"): rank label,
   tier label, one tier word.
4. **Collection → Heroes**, sidebar set to "alle Helden", hover over it: the target
   count in the tooltip.
5. **Open both selection lists** and cross-check the order — it has held in every
   variant so far, but it is the assumption that `ownedItem` and `alphabeticalItem`
   rest on.
6. **Sort alphabetically, all heroes, page through** and collect the name strips.

**Assemble the names into a single strip.** Nine pages with two name rows each make
eighteen images; stitched together they are one, and that reads in a single pass. The
strips must be **tall enough**: on an owned card the name sits lower than on a
purchasable one, because the price row is missing there.

**What the name doesn't reveal, the portrait does.** Four French mappings could only be
resolved this way, and in three cases the guess would have been wrong. The collection's
search bar accepts the recognized name and shows exactly one card.

## The cost of a wrong vocabulary

**It is low, and that is intentional.** A word that doesn't match results in nothing
being recognized — and where nothing is recognized, nothing is written either. The
readers treat that as "no idea", and a number from yesterday stays put. A wrong word
therefore doesn't corrupt any data, it only causes blindness.

The exception is the filler words: one that is too generic sweeps up real card names
with it, and you can't tell from the result.
