# Architecture

How this application is put together: where the files sit, which layer may know which, where the
data lives, and the rule that decides whether two entries are the same account.

What is *in* the data is one level further along, in [data-model.md](data-model.md); how it is
displayed in [ui-conventions.md](ui-conventions.md).

## Project layout

```
Battledeck/
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
      AppFile.cs           owns app.yaml; every write re-reads it and replaces one section
      DataBackup.cs        zips the YAML files to backups/{version}.zip before a migration
      Housekeeping.cs      what the folder keeps: 5 logs, 20 captures, 10 backups
      LogArchive.cs        compresses a log as soon as the sink stops writing to it
    Update/              the hourly update check - see self-update.md
      GithubReleases.cs    asks api.github.com for the newest release; the app's ONLY request
      UpdateGateway.cs     owns the update section of app.yaml, decides whether an hour has passed
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
Battledeck.Tests/         xUnit, same target framework and UseWPF - it loads compiled XAML
  TestHome.cs            module initializer: points SMURFTOWN_HOME at a throwaway folder
                         BEFORE any test runs, so none can reach the real ~/.smurftown
  Sta.cs                 runs an action on an STA thread and rethrows what escaped it
  XamlLoadsTests.cs      loads every compiled XAML once - the incident guard
  TextsTests.cs          the four language files against the code and each other
  BattlenetAccountGatewayTests.cs   write, read back, compare - and two folders at once
Setup/Setup.vdproj       MSI definition
Battledeck.cer            UNUSED - remnant of a signing that was never enabled
dev                      entry point for build and release (Bash, Windows-only)
dev.cmd                  the same entry point from PowerShell and cmd
.github/workflows/
  build.yml              every branch push -> ./dev test, then ./dev publish
  release.yml            tag push -> ./dev test, then ./dev release + assets onto the release
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
  demo-data.yaml           thirteen invented accounts, the shooting set for every README
                           screenshot - NEVER photograph the real list, the repo is public
  smurftown-home.ps1       resolves the data folder the way Directories.cs does - the one
                           place the other scripts ask
  test-home.ps1            demo data into a throwaway folder, SMURFTOWN_HOME set, app started
  capture-window.ps1       captures the Battledeck window to docs/images/<name>.png
  capture-run.ps1          walks a human through the captures, aborts on real data
  drive-battledeck.ps1      operates the running app - click, key, wheel, capture
  drive-hots.ps1           starts and operates the GAME - for calibration and language work
```

## Layer rules

- **`Backend/` does not know `UI/`.** No `System.Windows` reference in entities or gateways.
  (Today's exception: `BattlenetAccountGateway` uses `System.Windows.Data.CollectionViewSource`
  for the filtered view — deliberately tolerated, not a precedent.)
- **Gateways are hand-written singletons**:
  `public static readonly XGateway Instance = new(Directories.UserPath);` with a **public**
  constructor taking the data folder. No DI container. ViewModels fetch the instance through
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
- **The data folder is one of those values.** Every gateway and both `DataBackup` methods take it
  as an argument; `App.OnStartup` is the only place that reads `Directories.UserPath`. That static
  resolves **once** per process and keeps the answer — right for the application, because a
  variable changed mid-run would otherwise split the data across two folders, and unusable for a
  test, which needs a fresh folder per case.

## Persistence

By default everything under `%USERPROFILE%\.smurftown\` (defined in `Directories.UserPath`):

- `data.yaml` — the complete account list, serialised with camelCase naming
- `app.yaml` — everything the application knows about itself, in four sections:
  `schemaVersion` (the layout of the file), `appVersion` (which release last wrote it),
  `settings` (game path, input speed, client language, app language), `rotation` (a **hand-set**
  rotation, the exception rather than the normal case — see
  [Free rotation](data-model.md#free-rotation)) and `update` (when GitHub was last asked and what
  it offered). It was four files until 1.3.0 — `settings.yaml`, `rotation.yaml`, `update.yaml`
  and `version.txt`; see [One file for four kinds of state](#one-file-for-four-kinds-of-state)
- `backups/{version}.zip` — copies of every YAML file, set aside once per version
- `logs/` — `smurftown.log` plus up to four compressed predecessors
- `shots/` — captures written when an automation run strands

`BattlenetAccountGateway` rewrites the whole list on **every** mutation (`SaveToConfigFile`). No
diff. Inside one process the write is serialised and reads the file again first; two parallel app
instances still overwrite each other — see below.

### One file for four kinds of state

`app.yaml` holds what used to be `settings.yaml`, `rotation.yaml`, `update.yaml` and `version.txt`.
Three gateways read and write it, each owning one section, and `AppFile` owns the file.

```
SettingsGateway ──┐                        app.yaml
HotsRotationGateway ├──►  AppFile  ──►     schemaVersion: 1
UpdateGateway ────┘       │                appVersion: 1.3.0
DataBackup.MarkCurrent ───┘                settings:  { … }
                                           rotation:  { … }
                                           update:    { … }
```

**Every write re-reads the whole file and replaces only its own section**, and that is what makes
one file safe where four were needed. The update check runs once an hour; if it wrote its own
in-memory picture, that picture would carry a `settings` block as old as the moment the window
opened — and a setting changed in between would be gone. `AppFile.Write` therefore reads `app.yaml`
fresh, applies the caller's section to *that*, and writes the result.

**Read, change and write sit inside one lock** (`AppFile.FileLock`, and `BattlenetAccountGateway`
has its own for `data.yaml`). Reading immediately before writing is worth nothing if something can
slip in between. The lock is `static`, because what is protected is the file and not the object —
two instances with a lock each would guard nothing.

Today every writer happens to be on the UI thread anyway: the check hangs on a `DispatcherTimer`,
and the game flows do their reading in `Task.Run` but the gateway call after the `await`, back on
the caller's thread. That is a convention nobody enforces, and one `Task.Run` around a save would
turn the re-read from a guarantee into a race that shows up as a lost setting once a month.

**The lock does not reach across processes.** Two running copies of Battledeck still interleave
between their read and their write. Narrowing that window is all the re-read does; closing it would
take a named mutex, and that has its own questions — what the second instance does while it waits,
and what happens to a mutex a crashed instance abandoned. Not built.

**Both files carry a `schemaVersion`, and they carry their own.** `app.yaml` and `data.yaml` may
evolve independently, so one number each rather than one for the folder.

- **A file from a newer schema is read, never written.** Deserialising drops every key this build
  does not know, so writing it back would silently delete whatever a later version put there.
  Reading it best-effort keeps the application usable; the write throws, and the message says to run
  the newer build.
- **`data.yaml` was a bare sequence until 1.3.0** — it began with the first account and carried
  nothing else. It is recognised by its first line that is neither blank nor a comment: an item
  starts with `-`, the current layout starts with a key. Sniffed rather than found out by letting
  the deserialiser throw, because an exception used as a fork would swallow the one case that must
  reach the caller — a file broken in *both* layouts.
- **A change made by somebody else is named before it is overwritten.** The account gateway keeps
  the file content it last read and compares it against what is on disk before saving. It still
  writes — refusing would lock the human out of their own edit — but the log says the change was
  lost, and losing it silently is the one thing worse.

**The migration runs once, on the first start after the update**, and only when `app.yaml` is
missing. It is safe to lose: `DataBackup` archives every `*.yaml` moments earlier, which is exactly
why that call stands before the first gateway. `version.txt` is *not* a `*.yaml` and therefore not
in the archive — its one value moves into `appVersion` and is not lost either. Each of the four
files is carried over under its own `try`: a rotation nobody can read is no reason to lose the
settings as well. The old files are deleted only once the new one stands.

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

The scripts under `tools/` read the same files — `data.yaml` for a login, `app.yaml` for the
game path — and resolve the folder through `tools/smurftown-home.ps1`, which must stay in step with
`Directories.Resolve()`. `tools/test-home.ps1` creates such a folder, fills it with the invented
accounts from `tools/demo-data.yaml` and starts the app against it.

### The backup before a migration

`DataBackup` zips every `*.yaml` of the data folder into `backups/{version}.zip` — **once per
version**, before the first gateway runs.

```
App.OnStartup
  │
  ├─ DataBackup.BeforeMigrations()      app.yaml : appVersion != running version?
  │        └─ yes ──► *.yaml  ──►  backups/{the version in the file, else "unknown"}.zip
  │                                (an existing archive is NOT overwritten)
  ├─ Housekeeping.Run()                   caps captures, backups and the old flat log
  ├─ SettingsGateway.Apply()
  ├─ BattlenetAccountGateway.Instance    ← forced here, so an unreadable data.yaml stops
  │                                        the start and not the first window
  └─ DataBackup.MarkCurrent()            notes appVersion in app.yaml
```

- **Why per version and not per start.** A copy on every start would push the interesting state out
  of reach after two launches. What matters is the state *before* the update, so the marker is the
  version, not the date.
- **The marker is written last.** Everything above it may still throw, and then the next start has
  to find the same backup situation as this one. Written first, a failed migration would leave the
  second attempt without a copy.
- **The archive is named after the version that wrote the data**, not the one running now — that is
  what `appVersion` in `app.yaml` is for, and `version.txt` before it. Every installation from
  before 22.08.2026 lands under `unknown`, since none of them wrote a marker at all.
- **It asks `AppFile.PeekAppVersion` and not `AppFile.Instance`.** This runs before the first
  gateway exists, and building an `AppFile` here would run its migration — which deletes the very
  files the backup is about to set aside. The peek reads `app.yaml` if it is there, falls back to
  `version.txt`, and creates nothing.
- **An existing archive is kept, not refreshed.** A second run of the same update would otherwise
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

### What the folder keeps

Three things in here grow without an end of their own: the log is appended to on every run, a
stranded automation leaves a full-screen PNG of some 5 MB, and every version adds a backup.
`Housekeeping` is where all three bounds stand, and it is the only code in this application that
deletes anything by itself.

| What | Bound | Why that one |
|---|---|---|
| `logs/` | 5 files, roll at 10 MB, all but the current one compressed | Serilog's defaults are worse than they look: one file, 1 GB, no rolling — on reaching it the sink stops writing, silently |
| `shots/` | the newest 20, and nothing older than 30 days | the count decides the size of the folder, the age takes what the count spared |
| `backups/` | the newest 10, **no** age limit | the age of a version says nothing about whether somebody still needs to get back to it |

- **Compressed as ZIP and not GZip**, although a log is one file and GZip is made for exactly that.
  Windows Explorer opens a `.zip` on a double click and offers nothing for a `.gz` — and whoever
  goes looking for an old log is a human on this machine, not a script.
- **The log is compressed by a hook, not at startup.** `LogArchive` hangs in the sink's
  `OnFileOpened`, which fires on every roll. A tidy-up that only ran at startup would leave those
  files uncompressed for as long as the window stays open — and that session is the one somebody is
  trying to debug. The same hook is the recovery path after a crash: the next start opens a file and
  thus lands in it.
- **Nothing in `LogArchive` logs.** It runs inside the sink while the logger is being built or a
  roll is under way; a `Log.Warning` from there would re-enter the very sink that is mid-open.
- **A ZIP is not protection.** Every backup holds a complete `data.yaml`, and that file carries the
  passwords in plain text. Compressing makes the folder tidy and nothing else — see
  [security.md](security.md).
- **The suffixes are matched in code, not by a search pattern.** On Windows a pattern whose
  extension is exactly three characters also returns names whose extension merely *starts* that way
  — the reason `*.xls` famously returns `book.xlsx`. `*.log` could therefore return
  `smurftown.log.zip`, and the sweep would pack an archive into an archive and delete the original.

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
