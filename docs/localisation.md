# Language

Which language Smurftown speaks, and which words the text recognition compares against. Two
questions, and the whole point of this file is that they are not one.

Which language the **client** runs in — how to switch it, what a missing language pack costs, and
the measured word lists per variant — is in [client-language.md](client-language.md).

**Three language questions that have nothing to do with each other.** They get confused
regularly, and each confusion costs something different:

| Question | Answered where | What breaks if you swap them |
|---|---|---|
| Which language does the **client** run in? | `settings.yaml` → `clientLanguage`, vocabulary in `GameVocabulary` | OCR stops reading anything |
| Which language does **Smurftown** speak? | `settings.yaml` → `appLanguage`, texts in `Backend/Texts/*.yaml` | the user reads a foreign language |
| Which language is the **log** in? | not at all — it is always English | one message has four wordings and is no longer searchable |

The first two are **independent of each other**, and that is not caution but the normal case:
somebody running a French client because their account lives there does not want a French UI
because of it. Hence two settings and not one.

## The UI speaks four languages

German, English, French, Spanish. **The code carries English texts and those are the source** —
`Backend/Texts/en.yaml` is not a translation state but the original; the other three are its
translations and fall back to it when a line is missing.

| What | Translated? |
|---|---|
| Labels, tooltips, toasts, progress messages | **yes** |
| `smurftown.log`, names of diagnostic captures, values of `GameScreen` | no, always English |
| Text of an **exception** (`e.Message`) | no — only the **frame** around it |
| **Reader notes** (`reading.Note`, `result.Note`) | no, for the same reason |
| Comments, XML doc, this file | no — English, like everything else in a public repo |
| OCR vocabularies | no — they **are** the text shown in the game |

**The boundary at exception texts is a deliberate compromise.** In French you then get
`Échec de l'ouverture des coffres : The game has exited.` The reason: the same string goes into
the log one line above, and a log in four wordings is no longer searchable. The alternative
would be an error-code system across 21 `throw` sites — its own task with its own cost/benefit,
not a drive-by step.

**Progress messages are translated even though they arise inside an automation run.** They are
the borderline case one can argue about, and the decision is: they go exclusively to the human,
never into a file — unlike an exception text they have no counterpart in the log that would need
to stay searchable.

**The mechanics** live in `Backend/Texts/Strings.cs` and are word for word those of
`GameVocabulary`: a static `Current` instance, set from outside (`SettingsGateway.Apply`), read
on every access. In **Backend** and not under `UI/`, because three backend sites write text for
humans — `HotsRegionData.RankName`, the progress messages in `GameSession`, and the note
`ProfileReader` puts into its reading. None of that needs WPF: `INotifyPropertyChanged` lives in
`System.ComponentModel`. What does need WPF sits separately in `UI/MVVM/StrExtension.cs`.

> **The namespace is called `Texts` and not `Language`.** `Language` is the name of a WinRT type
> (`Windows.Globalization.Language`) that `TextReader` needs for OCR — a namespace of the same
> name hides it in every file under `Backend/` that sees both. Measured: the build broke in
> `TextReader.cs` with `CS0118`, at a place that has nothing to do with translation.

**The switch takes effect immediately, without a restart**, and that is the reason for the shape
of the markup extension: `{loc:Str dialog.save}` yields **not a string but a binding** onto the
indexer of `Strings.Current`. If it handed back finished text, that text would be fixed from the
moment the view loaded. **Hence the instance is never replaced** — a new one per language would
leave every already-built binding hanging on the old one, silently.

Computed ViewModel properties do **not** follow along by themselves; they do not hang off the
indexer. `SettingsViewModel` therefore subscribes to `Strings.Changed` and re-announces all
properties at once with an empty name. An open dialog does not follow — harmless, because when
switching, the settings tab is open and no dialog is.

**Spanish exists only once here**, unlike for the game. The split into `SpanishSpain` and
`SpanishLatin` hangs on the **hero names** there — Blaze is `Vulcano` in Spain and `Blaze` in
Latin America, ten names diverge. A word like "Guardar" does not separate the two variants, so a
second file would be the same file twice.

**A missing key does not surface at build time.** XAML does not know the keys, C# sees only a
string, and `Strings` falls back to English silently. Against that stands
`python tools/check-texts.py`: it verifies that every used key exists, that no orphans lie
around, that all four files have the same keys — and that **each key has the same placeholders**
in every file. The last point is the important one: a `{2}` in a text that gets only two values
would otherwise be permanently broken in exactly one language. `Strings.Format` does catch the
`FormatException` and fall back to English, but nobody would have noticed.

> **And a `: ` in an unquoted value breaks the whole file.** YAML turns it into a nested mapping;
> measured on `settings.inputSpeedHint`, whose English text contains "…is not affected: a shorter
> wait…". Our own check script is more forgiving here than YamlDotNet — the cross-check therefore
> ran with a real YAML parser.

**Five labels sit in fixed widths** and get truncated when a translation grows longer, without
anything being reported anywhere. The limits are tabulated at the top of `en.yaml`. The most
expensive case is the main tab: 160 px at font size 22, "EINSTELLUNGEN" would need about 185,
which is why it says **"OPTIONEN"** there.

## The game's vocabulary

The last row of the table above is the dangerous one, and it has **one** place:
`Backend/Automation/GameVocabulary.cs`.

> **Two tables with the same words, and that is intentional.** The seven rank tiers appear twice
> in the code: in `GameVocabulary.Tiers` and as `rank.*` in the four language files. There they
> are **measurements** the OCR compares against — here it is **display**. They can be in
> different languages, and in the case the split was built for they are: French client, German
> UI. Merging them makes either the recognition blind or the display wrong.

**Every variant is its own vocabulary; none replaces another.** German stays word for word as
measured; translating there makes the recognition blind — the build stays green, the log stays
silent, and simply nothing gets read any more.

There are **five** — the same five the client offers: German, `English (US)`, `Français`,
`Español (ES)`, `Español (AL)`. The reference work for this is
[`client-language.md`](client-language.md); it holds the words per variant, what
happens when switching, and what OCR needs in terms of language packs.

**Measured against the running client, never from memory** — and that is not a formula of
caution: of six guessed French words, **four** were wrong. The loot tab is called `COFFRES` and
not `BUTIN`, the rank label `Ligue Storm` (the league name stays English) and not
`Ligue de tempête`, the tier label `Niveau du joueur` and not `de joueur`, and under an owned
card it says `DISPONIBLE` and not `obtenu`. None of the four would have announced itself when
translating or in the log.

**Calibration does not change with the language** — the same anchors hit in French as in German.
**What does change is everything that sits in a tab row**: see
[Tabs are searched, not calibrated](game-integration.md#tabs-are-searched-not-calibrated).

**Accents are stripped on both sides**, in the vocabulary as in the recognised text — the rule
lives once, in `TextNormalisation`. As long as only German and English were read, this went
unnoticed: no tier word and no tab name carries one there. With `maître`, `BOTÍN` and `héros` a
missed accent otherwise turns into a non-match, and that looks like a missing line.

**Two values remain unmeasured** and are marked as such in the code: the tiers `Master` and
`Grand Master` (no account on this machine is above Platinum) and the word for a freshly acquired
card. For the highest tier **both** spellings are therefore in the vocabulary. Two keys cost
nothing, a wrong one costs the whole reading.

> **And that nearly broke it**: `ProfileReader.RankPattern` read `^([a-z]+)(?:\s*([1-5]))?$` and
> would have let a two-part `Grand Master` fall through — silently, because an unrecognised line
> is the same case as a missing one. In German it never shows, because "Großmeister" is one word.
> The pattern now carries multi-part tier words, with a **lazy** word part: greedy, it swallowed
> the space in `platinum 5` and the division would go unread.

**The 90 English hero names needed no second data source**: `HotsHero.Name` is the English
display name and had long been in the catalogue. Ten of them cross-checked against the client —
ten out of ten. Verified in **both** languages: no name contains one of the `NotNames` words as a
sub-word, no two normalise onto the same key, and exactly **five** pairs lie below the matching
threshold of 0.34 — the closest being `Rehgar`↔`Rexxar` and `Thrall`↔`Tyrael` at 0.333.

**Every further language needs its own source — and there is none left.** Blizzard's hero page
now renders its names via JavaScript; checked for `de-de`, `fr-fr`, `es-es` and `es-mx`, not a
single name is in the delivered HTML. Measuring therefore happens **at the client**: set the
collection to "all heroes" and "alphabetical", page through, read the name strips and compare
against the catalogue. The result lives as a **deviation table** in `GameVocabulary.HeroNames` —
only the names that differ from the English one.

**Why not another column in the catalogue**: a full name list per language would be the same line
five times over for nine tenths of it, and in that noise a wrong entry catches nobody's eye.
German still stays in the catalogue — it came from the same generator run as the id, and that
provenance is worth more than uniformity. `GameVocabulary.HeroName()` is the only place that
knows the difference.

**Every new variant gets cross-checked**, three ways: no filler word may sit inside a name, no
two names may normalise onto the same key, and the number of pairs below the matching threshold
should not grow.

| | Filler word inside a name | Same key | Pairs below 0.34 |
|---|---|---|---|
| German | none | none | 5 |
| English | none | none | 5 |
| **French** | none | none | **7** |
| Spanish | none | none | 5 |

**French brings a new confusion with it**, and it is the price of translation: Blaze is called
`Kramer` there, and `kramer`↔`tracer` sits at exactly 0.333 — as close as `Rehgar`↔`Rexxar`.
That is no reason to change the name (it is measured), but it is the reason this calculation
belongs to every new variant. **Compute it directionally**: `HeroNameMatcher` divides the
distance by the length of the **candidate**, not by the longer of the two — with the wrong
formula you get three pairs instead of five and miss half of them.

**How many deviate depends on the language**: eight in German, **sixteen** in French, fourteen in
each of the two Spanish variants.

**The two Spanish variants are two translations, not two spellings.** Their labels are word for
word identical. In the **hero names** they diverge in ten places: `Blaze` is called `Vulcano` in
Spain, in Latin America as in the original; with `Orphea`/`Orfea` it is exactly the other way
round. Whoever merges the two loses half the card recognition in one of them — and does not
notice, because an unmatched card looks like an unread one. Four of them could only be settled
from the **picture**, because the name misleads: `Balafré` means "the scarred one", sounds like
Blaze and is **Stitches**; `Kramer` is the red mech (Blaze), `Lardeur` the gnoll (Hogger),
`EDN-OS` the Protoss probe (Probius).
