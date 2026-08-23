# Changing something

The checklist to read before editing. Each entry names the places that have to move together —
and the failure mode, because most of them report nothing: no compiler error, no binding warning,
no log line.

Its counterpart stands at the bottom: [Known issues](#known-issues), the things deliberately left
alone.

Two rules that used to open this list are not change steps but working practice, and stand in
[`../CLAUDE.md`](../CLAUDE.md): plan before the edit, and start the app once after every XAML
change — the build is no substitute there.

- **New text for humans: a key in all four language files** — `Backend/Texts/*.yaml`, English wording
  into `en.yaml`, then `python tools/check-texts.py`. In XAML `{loc:Str key}` fetches it, in C#
  `Strings.Current["key"]` or `Strings.Format(...)` for placeholders. A missing line does **not**
  surface at build time.
- **New output to the log: English, and without the detour via `Strings`** — see
  [Language](localisation.md). Likewise the text of an exception and the reader notes: there only the
  **frame** they appear in is translated.
- **A new word that must be recognised in-game belongs in `GameVocabulary`** — in **all five**
  variants, and every unmeasured line gets the note `NOT MEASURED` until somebody has checked it at
  the client. Putting a constant beside it is exactly the state this class arose from.
- **Another language variant**: `GameLanguage` (value + `DisplayName` + `LocaleTag` + `OcrTag`) → one
  instance in `GameVocabulary` plus the hero-name deviation table → nothing else. The settings
  dropdown generates itself from the enum, `For()` is a `switch` over all values, and the calibration
  stays untouched. **Measured at the running client** — the way there is in
  [`client-language.md`](client-language.md); `tools/drive-hots.ps1` operates the game for
  it, see the [`drive-hots`](../.claude/skills/drive-hots/) skill.
- **New field: decide the level first** — account, game or region? The question is not "where does
  it fit" but "**what is there more than one of**". Everything that is the game state of a session
  (rank, heroes, currencies, penalty games, read timestamp) belongs in `HotsRegionData`; which games
  are played and where is `RegionsByGame`; everything that describes the account (credentials,
  battletag, archive) sits directly on `BattlenetAccount`. The table is under [Regions](data-model.md#regions).
- **Anything a row shows is asked per region.** `PlaysIn(game, region)`, never `Plays(game)` — the
  latter only answers "anywhere at all". Getting this wrong shows the American row what the account
  has in Europe, and nothing reports it: no compiler error, no binding warning, no log line.
- **New field on the account**: `BattlenetAccount` → `AddOrEditAccountViewModel` (property +
  constructor + the `new BattlenetAccount { ... }` block in `Execute`) → `AddOrEditAccount.xaml` →
  if needed `AccountCardViewModel`/`AccountCardView`.
  **Fields nobody edits by hand must go through the dialog too** — what gets saved is a newly built
  account, and what does not arrive there is deleted after every manual change. That applies to the
  whole `HotsByRegion` collection: it runs along as a silent pass-through, and the values written by
  the read sit inside it. Directly on the account, `Name` and `Discriminator` are in that role.
- **New field on `HotsRegionData`**: add the property (without `required`, with a sensible default) →
  extend `Copy()`, or it is lost on editing → if needed `StashRegion`/`LoadRegion` in the dialog →
  `AccountCardViewModel.Row` setter. **`Copy()` is the one people forget**, and the failure is
  silent: the value is in the file but disappears on the first save through the dialog.
- **New field in the account dialog**: no height arithmetic needed — every tab scrolls as a whole.
  Only check whether the tab still looks sensible without scrolling. **Whoever adds their own
  `ScrollViewer` there rebuilds the second scroll region the rebuild just removed.**
- **New modal**: `DialogBounds.FitToMainWindow(this)` in the constructor, right after
  `InitializeComponent()`. Set the declared size in XAML to the same value so the designer does not
  lie.
- **New image**: generate it via a script in `tools/` first, then enter it in `Smurftown.csproj` as
  `<Resource Include>` **plus** `<None Remove>`. Without the entry the `pack://` URI loads into
  nothing at runtime — the build reports nothing.
- **New filter in the bar**: `BattlenetAccountGateway.CreatePredicate` is a pure AND chain of
  conditions of the form "not set ⇒ lets through". Append a new condition by the same pattern and
  "nothing filtered = show everything" stays right by itself. **If the filter belongs to a game, it
  gets a visibility in the pattern of `HotsFiltersVisibility`** — and the value is then passed
  through empty in the `OnPropertyChanged` override, not queried in the predicate: a filter one
  cannot see must not filter. **Three entries do not follow this** — they are view choices and not
  filters: the **game filter** and the **region filter** (both exclusive and always set) and the
  **archive** (switches between two halves). Check the width budget in
  [`ui-layout.md`](ui-layout.md) before building — the bar is close to exhausted, and the
  window has already grown once for it.
- **New element in the account row**: first recompute the column budget, then the budget **within**
  the column — the numbers are in [`ui-layout.md`](ui-layout.md). The free space there is
  deliberately left free so the row does not stand edge to edge. **Vertically the row is exhausted.**
  A taller row changes the height at two places in XAML (`Height` on the `Border`, `d:DesignHeight` on
  the `UserControl`) and costs about half a screen per 27 accounts. **A new use case belongs in one of
  the two menus**, not as a button in the row — there it costs no width.
- **New game in the row**: `Games` (the id), `GameVisuals` (icon, accent colour, name **and** order),
  a row of three ticks in the dialog's game/region matrix (three properties plus three cells in
  `AddOrEditAccount.xaml`, and the three notifications in `NotifyRegionTicks`), a toggle in the
  filter bar plus a pass-through property in `AccountsViewModel`, and a panel plus its visibility in
  the ViewModel. **No `bool` on `BattlenetAccount` any more** — a game is played when it has a
  region, and `Plays`/`PlaysIn` need no branch per game. Without its own panel it lands in the
  "no data" box — a valid intermediate state, not an error.
- **New region** (should Blizzard ever open a fourth): `BattlenetRegion` and
  `BattlenetRegions.Ordered` → an anchor `xyzAbove` in `screen-map.yaml`, measured at **two**
  resolutions → `ScreenMap.AboveFor` → a **column** in the dialog's matrix (four properties, four
  cells, four lines in `NotifyRegionTicks`), a toggle in the filter bar. Do not forget the
  abbreviation in `ShortName`: it names the row.
- **A script that reads the data folder goes through `tools/smurftown-home.ps1`** — never
  `$env:USERPROFILE` plus `.smurftown` on its own. `SMURFTOWN_HOME` moves the folder
  ([architecture](architecture.md#a-different-folder-for-tests)), and a script that resolves it its
  own way looks into one folder while the app works in another. For `capture-run.ps1` that is not an
  inconvenience but a hole: its safeguard would then be vouching for a file nobody is photographing.
  Nothing reports it — the script runs, and it reports success.
- **New data file under `~/.smurftown/`**: nothing to do for the backup as long as it ends in
  `.yaml` — `DataBackup` takes every one of them. Anything else is deliberately not copied; see
  [The backup before a migration](architecture.md#the-backup-before-a-migration).
- **New setting: four places, and two of them fail silently.** `Settings` (the property, without
  `required`, with a default) → `SettingsGateway.Current()` → `SettingsViewModel.Store()` →
  `SettingsView.xaml` plus a property on the ViewModel. **`Current()` and `Store()` both rebuild
  the object field by field**, so a field missing from either falls back to its default on the next
  save — and nothing reports it: no compiler error, no binding warning, no log line. The human
  sets the value, sees it take effect, and finds it gone after changing something else. If the
  setting has to reach somewhere at run time, it is pushed there by `SettingsGateway.Apply` rather
  than fetched from the gateway — see [Layer rules](architecture.md#layer-rules).
  In the tab it becomes **one row in one of the cards** — label docked left at 240, control beside
  it — and its explanation goes on the info sign, not on the screen; the tab has around 80 points
  of height left, see [ui-layout.md](ui-layout.md#the-settings-tab). What stays visible is only
  what changes with the chosen value.
- **Changing what a release ships breaks the updater, and only at run time.** `UpdateInstaller`
  reads three things out of a GitHub release: the tag as the version (`2.0.1`, no `v`), **exactly
  one** `.zip` asset, and a `checksums.txt` listing that ZIP by name. Adding a second ZIP to
  `cmd_release` is enough to stop every installed copy from updating, and nothing about it surfaces
  in CI — the release builds and uploads fine. The full contract is in
  [self-update.md](self-update.md#what-the-release-has-to-look-like). The ZIP's *name* is
  deliberately not part of it: it is searched, not constructed.
- **New migration**: there is no migration in the code today, so a new one brings its own
  mechanism with it. The backup is already there, per version. What a migration owes is a marker
  (a state that cannot occur after it ran), a read-back check before the previous file is let go,
  and a fallback for the account it cannot place — an account without a row cannot be repaired,
  because the edit button sits in the row.
- **New point in `screen-map.yaml`**: measure at **two** resolutions, otherwise the anchor is guessed.
  How to get to a second resolution — and which two obvious routes do **not** lead there — is in
  [`calibration.md`](calibration.md).
- **Not everything is calibratable.** Elements whose position depends on a text width move with the
  language. Such elements are searched by their **word** — `TabFinder` does that, `HeaderReader`
  always did. Whoever needs a new tab enters its word in `GameVocabulary` and **no coordinate** in
  `screen-map.yaml`. See [Tabs are searched, not calibrated](game-integration.md#tabs-are-searched-not-calibrated).
- **No `BringToFront` between opening and selecting.** `SetForegroundWindow` closes every open
  dropdown, and the second click lands on the background. In the gear menu, 66 points beside it would
  be "Exit game". `GameSession.ClickAt` therefore deliberately does not bring the window to the front
  — whoever needs it calls `BringToFront` once beforehand.
- **Correcting the free rotation**: edit `Backend/Entity/rotation-calendar.yaml`, **not** the code —
  the calendar is data, not logic. To try something without a rebuild, put a copy at
  `~/.smurftown/rotation-calendar.yaml`; it beats the embedded version. Whoever changes a period
  verifies it against **two years** — the same rule as measuring at two resolutions and for the same
  reason.
- **No new framework, no DI container, no test setup without asking.** The app is deliberately kept
  small.
- **Changes to layout/paths in the `Setup` project cannot be verified here** — the user has to build
  those.

## Known issues

Known, not yet fixed. Do not touch these as drive-by cleanup — each is its own task:

| Location | Problem |
|---|---|
| `BattlenetAccount.Equals` | identity only via email ⇒ changing the email creates a duplicate entry instead of an update. |
| `Smurftown.csproj` | two `<Page Update>` entries on paths that do not exist (`UI\MVVM\View\Dialog\`, `UI\MVVM\Views\`). Dead configuration. |
| `Backend/ObservableHashSet.cs` | ~490 lines copied from EF Core, **completely unused**. |
| `UI/MVVM/Controls/BindableRichTextBox.cs`, `View/RichTextBoxHelper.cs` | unused — the notes are written into the ViewModel via a code-behind `TextChanged` instead. |
| `ErrorBox` / `ErrorBoxViewModel` | reachable only via the commented-out `ShowErrorDialog` route in `MainViewModel`. Effectively dead, errors go out as toasts. |
| `AccountsViewModel.ShowDialog` and others | `MainWindow.Opacity = 100` instead of `1.0`. WPF clamps it to 1.0, so it works — but it is wrong. |
| `AddOrEditAccount.xaml` | the `PasswordBox` has both the `PasswordBoxHelper.BoundPassword` attached property **and** a `PasswordChanged` code-behind handler. Two mechanisms for the same thing. |
| `AccountCardView`, `AccountCardViewModel`, `BattlenetAccountToCardViewModelConverter` | named after a card that no longer exists — it is a row. Deliberately not renamed along: the three names hang off `AccountsView.xaml`, the `x:Class`, the code-behind file and the converter key; a rename is its own task with its own build run. |
| `AccountCardViewModel.ImageSource` | sets a path to `overwatchhots_full.png` / `overwatch_full.png` / `hots_full.png` that **nobody binds**. Also falls back to the HotS image for accounts with neither Overwatch nor HotS. With it, `overwatchhots_full.png` (3 MB) hangs unused in the build. |
| `UI/Images/Ranks/` | 28 PNGs (27 medals plus `norank.png`), ~1.4 MB in the repo. Could be roughly halved by palette quantisation, not done so far. |
| `GameSession.StartAndLogin` | if a step fails **after** the window start, the game keeps running — nobody calls `Dispose`. Not a dead end since the next start signs the running client out and reuses it, but a stranded window stays until somebody touches it. |
