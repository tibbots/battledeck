# Architecture

How this application is put together: where the files sit, which layer may know which, where the
data lives, and the rule that decides whether two entries are the same account.

What is *in* the data is one level further along, in [data-model.md](data-model.md); how it is
displayed in [ui-conventions.md](ui-conventions.md).

## Project layout

```
Smurftown/
  App.xaml(.cs)          startup: create ~/.smurftown, configure Serilog, merge resources
  MainWindow.xaml(.cs)   chromeless window (DragMove, minimise/maximise/close by hand)
                         plus the tab bar ACCOUNTS | SETTINGS
  Directories.cs         the only place that knows the data path - and the SMURFTOWN_HOME
                       override that moves it for tests
  AppVersion.cs          the running version, three parts - for the backup and later updates
  app.manifest           requestedExecutionLevel = asInvoker -> no admin rights needed
  Backend/
    Texts/               the UI texts - ONE file per language
      Strings.cs           loads them, holds the current one, announces the switch
      en.yaml              the ORIGINAL; de/fr/es are its translations
      de.yaml fr.yaml es.yaml
    Entity/              BattlenetAccount, Games, HotsRankTier,
                         Settings + InputSpeed + GameLanguage + AppLanguage,
                         HotsHero + HotsHeroRole + HotsHeroCatalog,
                         HotsRotation + HotsRotationPeriod
      rotation-calendar.yaml  the free rotation as a calendar without a year, 48 periods,
                              embedded resource
      HotsRotationCalendar.cs loads it, looks up by month and day
    Gateway/             BattlenetAccountGateway, HotsRotationGateway, SettingsGateway
      DataBackup.cs        copies the YAML files to backups/{version}/ before a migration
    Update/              the hourly update check - see self-update.md
      GithubReleases.cs    asks api.github.com for the newest release; the app's ONLY request
      UpdateGateway.cs     owns update.yaml, decides whether an hour has passed
      UpdateInstaller.cs   downloads, verifies the checksum, swaps the .exe, restarts
    Automation/          reading from the running game
      GameInstallations.cs finds HeroesSwitcher_x64.exe
      screen-map.yaml      calibration: anchors and offsets, embedded resource
      ScreenMap.cs         loads it, maps anchors onto the window (Spot, Layout)
      GameWindow.cs        find window, bring to front, measure client area - and
                           IsRunning(), which only asks the process list, for the poll
                           TWO windows, not one: Handle takes the foreground and the input,
                           PictureWindow() is whichever of the process has the largest
                           client area - with a client restored from outside that is a
                           D3DProxyWindow and not the main window
      GameSession.cs       start, set region, log in, detect screens - and AttachToRunning,
                           which takes over a signed-in client and touches nothing
      LoginLocator.cs      find the login form in the image instead of calibrating it
      Screenshot.cs        BGRA buffer: crop, scale, distance metrics
      InputSender.cs       SendInput: click, text, mouse wheel
      NativeMethods.cs     every user32/gdi32 call in one place
      TextReader.cs        Windows.Media.Ocr, language from the settings
      TextNormalisation.cs the one rule for how accents leave recognised text
      TabFinder.cs         clicks tabs by their WORD - the counterpart to calibration
      GameVocabulary.cs    every word that must be recognised in-game - five variants
      HeroNameMatcher.cs   recognised text -> hero(es), over the 90 names of the client language
      CollectionReader.cs  owned heroes from the collection
      HeaderReader.cs      gold, shards, gems, chest counter
      ProfileReader.cs     rank, placement state, account level, battletag - all as text
      LootOpener.cs        opens all loot chests, counter as stop condition
      PenaltyReader.cs     deserter status: symbol by colour, count from the hint text
      PlayScreen.cs        switches to PLAY and there to ARAM
  UI/
    MVVM/
      StrExtension.cs    {loc:Str key} in XAML - yields a BINDING, not a string
      View/              XAML + code-behind + AddOrEditAccountViewModel and HeroPickerViewModel
                         (they live here, not in ViewModel/)
                         RunGuide walks a human through a run against a client that is
                         already up - it exists because only a human can put that client
                         in the foreground, and clicks land nowhere else. The region
                         question is a step inside it, not a second window
      ViewModel/         Main, Accounts, AccountCard, Settings, UpdateOffer, RunningGame
                         UpdateOffer is ONE object for two places - the version chip in
                         the header and the ABOUT & UPDATES card in the settings
                         RunningGame is the same shape: the chip beside that version, the
                         poll behind it, and the busy flag the account rows take too
      HotsReadout.cs     the read-out, shared by BOTH entrances - the account row and the
                         header chip. It is here and not in Backend/Automation/ because it
                         asks the gateway who owns a battletag
      Converter/         Entity -> CardViewModel
      Controls/          BindableRichTextBox (currently unused)
      Dialogs.cs         static DialogService + toast notifier
      DialogBounds.cs    clamps every modal to the main window minus a margin
      GameFocus.cs       which game the rows show - set by the game filter
      HotsRankImages.cs  the single tier+division -> image path mapping
      HotsRankGrid.cs    the 28 ranks laid out as the grid draws them, shared by BOTH
                         places that let one be picked - the HotS tab of the dialog and
                         the medal in the account row
      HotsHeroImages.cs  the single hero -> image path mapping, plus HotsRoleColors
      GameVisuals.cs     the single game -> icon/accent/name mapping
    Theme/               Battlenet*Theme.xaml, merged in App.xaml
    Images/              bound as <Resource>, addressed via pack:// URIs
      penalty.png        warning triangle for penalty games
      free.png           nexus mark for the free rotation
      Ranks/             27 rank medals plus norank.png, included by wildcard
      Heroes/            90 hero portraits as JPEG (160 px), included by wildcard
Setup/Setup.vdproj       MSI definition
Smurftown.cer            UNUSED - remnant of a signing that was never enabled
dev                      entry point for build and release (Bash, Windows-only)
dev.cmd                  the same entry point from PowerShell and cmd
.github/workflows/
  build.yml              every branch push -> ./dev publish on windows-latest
  release.yml            tag push -> ./dev release + assets onto the GitHub release
docs/                    reusable knowledge, not app-specific
  images/                the README screenshots
tools/                   not part of the solution - asset generators and drivers
  build-rank-assets.py     generates UI/Images/Ranks/
  build-penalty-icon.py    generates UI/Images/penalty.png
  build-placement-icon.py  generates UI/Images/Ranks/norank.png, verifies against the reference
  build-free-icon.py       generates UI/Images/free.png
  build-hero-assets.py     generates UI/Images/Heroes/ **and** HotsHeroCatalog.Generated.cs
  hero-names-de.json       German hero names, a data sheet rather than a fetch
  placement-referenz.png   cut-out placement logo from the game, reference for a generator
  gen-demo-data.py         generates tools/demo-data.yaml
  demo-data.yaml           ten invented accounts held in reserve - NEVER photograph the real
                           list, the repo is public
  check-texts.py           checks the four language files against the code and each other
  smurftown-home.ps1       resolves the data folder the way Directories.cs does - the one
                           place the other scripts ask
  test-home.ps1            demo data into a throwaway folder, SMURFTOWN_HOME set, app started
  capture-window.ps1       captures the Smurftown window to docs/images/<name>.png
  capture-run.ps1          walks a human through the captures, aborts on real data
  drive-smurftown.ps1      operates the running app - click, key, wheel, capture
  drive-hots.ps1           starts and operates the GAME - for calibration and language work
```

## Layer rules

- **`Backend/` does not know `UI/`.** No `System.Windows` reference in entities or gateways.
  (Today's exception: `BattlenetAccountGateway` uses `System.Windows.Data.CollectionViewSource`
  for the filtered view — deliberately tolerated, not a precedent.)
- **Gateways are hand-written singletons**: `public static readonly XGateway Instance = new();`
  with a private constructor. No DI container. ViewModels fetch the instance through
  `private static readonly` fields. If you need a new data source, follow the pattern — do not
  introduce a container without discussing it first.
- **`Backend/Automation/` does not know `UI/`** and, the other way round, the UI knows only
  `GameSession`, `CollectionReader`, `HeaderReader`, `ProfileReader` and `LootOpener`. Everything
  below that — P/Invoke, image buffers, calibration — stays internal. The exception is
  `Screenshot`, which uses WPF imaging classes for PNG output and scaling; taking on a second
  imaging library for that would be the worse trade.
- **Values flow inwards, never fetched outwards.** `Backend/Automation/` never reads a gateway:
  the game path, the input pace and the vocabulary are *set* from outside
  (`SettingsGateway.Apply`) or passed as parameters. Three static fields carry this —
  `InputSender.Pace`, `GameVocabulary.Current`, `TextReader.LanguageTag`.

## Persistence

By default everything under `%USERPROFILE%\.smurftown\` (defined in `Directories.UserPath`):

- `data.yaml` — the complete account list, serialised with camelCase naming
- `settings.yaml` — what the human configures: game path, input speed, client language, app
  language. Its own file and **not** part of `screen-map.yaml`, where the path used to sit:
  calibration describes how the game *looks*, not where it lives
- `rotation.yaml` — a **hand-set** rotation state; it beats the shipped calendar for its period
  and is the exception, not the normal case (see [Free rotation](data-model.md#free-rotation))
- `version.txt` — the version that wrote the files above. One line, nothing else
- `update.yaml` — when GitHub was last asked and what it offered. **Not** part of `settings.yaml`:
  those are what a human sets, these two values are what the app noted, and mixing them would have
  every check rewrite a file the human edits by hand ([self-update.md](self-update.md))
- `backups/{version}/` — copies of every YAML file, set aside once per version
- `smurftown.log` — Serilog file sink
- `shots/` — captures written when an automation run strands

`BattlenetAccountGateway` rewrites the whole list on **every** mutation (`SaveToConfigFile`). No
diff, no lock. Two parallel app instances overwrite each other.

### A different folder, for tests

**`SMURFTOWN_HOME` moves all of the above somewhere else.** Set it, and `Directories.UserPath`
resolves to that path instead — trimmed, environment variables expanded, made absolute, and read
**once** at first access: a variable changed while the app runs would otherwise split the data
across two folders.

It exists because of the sentence one paragraph up. Testing this app means clicking through it, and
every click that ticks a region or renames an account rewrites the whole `data.yaml` — the real one,
with the real credentials in it. The README captures used to move that file aside and put it back
afterwards, and "put it back afterwards" is a step that holds until the one run that strands
halfway.

**A path that cannot be resolved aborts the start**; there is deliberately no fallback to the
default. A typo in the variable would otherwise write into the real folder, which is the single
thing the mechanism exists to prevent. The exception flies before Serilog is configured, so its
message carries the variable and the value itself.

The scripts under `tools/` read the same files — `data.yaml` for a login, `settings.yaml` for the
game path — and resolve the folder through `tools/smurftown-home.ps1`, which must stay in step with
`Directories.Resolve()`. `tools/test-home.ps1` creates such a folder, fills it with the invented
accounts from `tools/demo-data.yaml` and starts the app against it.

### The backup before a migration

`DataBackup` copies every `*.yaml` of the data folder into `backups/{version}/` — **once per
version**, before the first gateway runs.

```
App.OnStartup
  │
  ├─ DataBackup.BeforeMigrations()      version.txt != running version?
  │        └─ yes ──► *.yaml  ──►  backups/{the version in the file, else "unknown"}/
  │                                (an existing folder is NOT overwritten)
  ├─ SettingsGateway.Apply()
  ├─ BattlenetAccountGateway.Instance    ← forced here, so the migration runs at the start
  │                                        of the app and not halfway into a window
  └─ DataBackup.MarkCurrent()            writes version.txt
```

- **Why per version and not per start.** A copy on every start would push the interesting state out
  of reach after two launches. What matters is the state *before* the update, so the marker is the
  version, not the date.
- **The marker is written last.** Everything above it may still throw, and then the next start has
  to find the same backup situation as this one. Written first, a failed migration would leave the
  second attempt without a copy.
- **The folder is named after the version that wrote the data**, not the one running now — that is
  what `version.txt` is for. Every installation from before 22.08.2026 lands under `unknown`, since
  none of them wrote a marker.
- **An existing folder is kept, not refreshed.** A second run of the same update would otherwise
  copy the state a failed migration left behind over the one from before it — precisely the state
  the backup exists to keep.
- **A failure does not abort the start.** A backup that cannot be written is almost always a full
  disk, and refusing to start over it would take the app away from the human as well as the backup.
  It goes into the log as a warning.
- **It replaced `data.yaml.pre-regions.bak`**, which one migration wrote for one file and which
  would have had to be invented anew for the next one.
- **`AppVersion.Current` is the single source** of the running version — three parts, no build
  suffix. It reads `AssemblyInformationalVersion` first, because that is the one carrying
  `<Version>` from the csproj literally.

**Schema evolution**: new fields need no migration — YamlDotNet serialises every public property
automatically, and missing keys in old files fall back to the property default. `required` neither
helps nor hurts: it is a compile-time feature only, the deserialiser ignores it. **Add new fields
without `required` and with a sensible default.** The deserialiser runs with
`IgnoreUnmatchedProperties()` so that a `data.yaml` written by a newer app version does not throw
a `YamlException` in an older one.

## Identity and normalisation

These rules sit scattered across property setters and comparison methods — think of them
together when changing one:

| Rule | Where |
|---|---|
| `BattlenetAccount.Name` is **upper-cased** on set | `BattlenetAccount.Name` setter |
| `BattlenetAccount.Email` is **lower-cased** on set | `BattlenetAccount.Email` setter |
| Account identity = **email alone** (`Equals`/`GetHashCode`) | `BattlenetAccount` |
| Sorting (`CompareTo`) by `Name` instead | `BattlenetAccount` |

Consequence of "identity = email": `AddOrUpdate` does `Remove` + `Add`. If the user changes the
email of an existing account, a **second** entry appears instead of an update. Known, see
[Known issues](changing-things.md#known-issues).

> **Trap on every new property on `BattlenetAccount`**: YamlDotNet serialises **every** public
> property. A computed value therefore lands in `data.yaml` as its own key — carried twice and
> ignored on the next read. That is exactly why `Battletag()`, `HotsRankName()` and `Plays()` are
> methods. `HasBattletag` and `DisplayName` have to be properties (a `SortDescription` needs a
> property name) and therefore carry `[YamlIgnore]`. Set the attribute when you add one.
