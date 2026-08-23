# MVVM conventions (as they are actually done here)

The code is not textbook MVVM. Stick to the existing pattern rather than "correcting" individual
places — partial migrations make the codebase less consistent than it is.

What the sections below add to that pattern are its traps, and they share one property: nearly
every one of them produces no compiler error, no binding warning and no log line.

The pixel budgets — window, filter bar, account row, dialogs — are in [ui-layout.md](ui-layout.md).

- **Views set their own DataContext in XAML**:
  `<UserControl.DataContext><viewModel:AccountsViewModel /></UserControl.DataContext>`. That is why
  every card ViewModel has a parameterless second constructor.
- **Entity → CardViewModel runs through an `IValueConverter` in the `ItemTemplate`**, not through a
  ViewModel collection. See `AccountsView.xaml` + `BattlenetAccountToCardViewModelConverter`.
- **Commands are lazily initialised `RelayCommand` properties** with a `CanX()` method that mostly
  says `return true;`. No `[RelayCommand]` source generator, although the toolkit has one.
- **Property changes trigger side effects via an `OnPropertyChanged` override**: `AccountsViewModel`
  re-filters on every property change, `AddOrEditAccountViewModel` recomputes `SaveButtonEnabled`.
  New properties in these classes therefore trip that automatically.

  > **Trap in `AddOrEditAccountViewModel`**: `OnPropertyChanged` is overridden and calls
  > `RefreshDialog`. Every notification from there thus arrives back there. A property without an
  > equality check in its setter spins **endlessly** at this point — `SaveButtonEnabled` and
  > `SaveHint` both have one.

- **Dialog flow**: the ViewModel builds an `AddOrEditAccountViewModel`, calls
  `Dialogs.DialogService.ShowDialog(...)`, then `dialogViewModel.Execute(success)` — persisting
  happens in `Execute`, not in the `Ok` command. Around it,
  `Application.Current.MainWindow.Opacity` is set to `0.4` and back.
- **Every view a dialog opens from needs `md:DialogServiceViews.IsRegistered="True"`** on its root
  element, plus the namespace `xmlns:md="https://github.com/fantasticfiasco/mvvm-dialogs"`.
  MvvmDialogs finds the owner window by looking among the **registered** views for one whose
  `DataContext` is the passed ViewModel; without the registration `ShowDialog` throws a
  `ViewNotRegisteredException`. Registered today: `MainWindow.xaml`, `AccountsView.xaml`,
  `AccountCardView.xaml`, `AddOrEditAccount.xaml` and `SettingsView.xaml`. **`HeroPicker.xaml` is
  deliberately not among them** — it is the dialog, not the caller.

  **This is the trap when rewriting a XAML file**: the attribute once fell out of
  `AccountCardView.xaml` during a layout rebuild, and the edit button was dead — without a compiler
  error, without a binding warning, without a log line. Whoever rewrites a view checks the root
  against the previous version.
- **Errors go out as a toast**, not as a dialog: `MainViewModel` hooks
  `DispatcherUnhandledException`, **logs the exception with its stack trace** and shows
  `Dialogs.Toast.ShowError`, then `e.Handled = true`. The app therefore practically never crashes —
  it swallows. **When debugging, look at `~/.smurftown/smurftown.log` first.** The order in the
  handler is deliberate: if the toast itself goes wrong, the reason is already in the file.
- **For `PasswordBox` and `RichTextBox`** (not bindable) there are code-behind handlers in
  `AddOrEditAccount.xaml.cs` that write into the ViewModel via `(dynamic)DataContext`.

## Themes

Styles live as `x:Key` styles in `UI/Theme/Battlenet*Theme.xaml` and are merged in `App.xaml`.
Styling a new control means adding a style there and referencing it via `{StaticResource ...}`.
Careful, the file names are not 1:1 the keys — `BattlenetPasswordBoxTheme` sits in
`BattlenetTextBoxTheme.xaml`.

Colour values are hard-coded scattered across the XAML files (Battle.net dark grey, among others
`#24262e`). There is no central palette.

**The merge order in `App.xaml` is load-bearing.** `BattlenetComboBoxTheme.xaml` references
`BattlenetScrollViewerTheme` via `StaticResource` — and `StaticResource` only sees what was merged
**before**. Put the line first and the app throws on startup.

**`DisplayMemberPath` does not take effect in our `ComboBox` template.** What gets displayed is
then the `ToString()` of the bound object — for a `record` that means
`SpeedChoice { Value = …, Label = … }` instead of "Fast". Both settings dropdowns stood like that,
and neither the build nor the log said a thing. An explicit `ItemTemplate` does take effect in
**both** halves: in the expanded entry via the `ContentPresenter` of the `ComboBoxItem`, in the
closed box via `SelectionBoxItemTemplate`. `SelectedValuePath` stays beside it — it is independent
of the display and worked the whole time.

**A `ComboBox` template needs more than a `Border`.** WPF looks inside for elements with fixed
names: a `Popup` whose `IsOpen` hangs off `IsDropDownOpen`, and with `IsEditable="True"`
additionally `PART_EditableTextBox`. If one is missing, the control stops working — without a
compiler error and without a binding warning. Our lists are not editable, so
`PART_EditableTextBox` is deliberately absent. The `ContentPresenter` for the selected entry needs
`IsHitTestVisible="False"`, or the text catches the clicks and the list never opens.

## Dialogs and layout

**No dialog is larger than the window behind it** — and it does not fill it either. The rule lives
once, in `UI/MVVM/DialogBounds.cs`; each of the three modals calls `FitToMainWindow(this)` in its
constructor, right after `InitializeComponent()`.

- **Called in the constructor, and that is not arbitrary.** MvvmDialogs sets `Owner` only
  afterwards, and `WindowStartupLocation="CenterOwner"` computes the position from the size —
  whoever clamps later has centred on the old one and sits off-place. For the same reason
  `DialogBounds` reads `Application.Current.MainWindow` and not `dialog.Owner`: the main window is
  long since there, the owner is still `null`.
- **`MaxWidth`/`MaxHeight` alone are not enough.** They draw the window smaller, but `Width` and
  `Height` keep reporting the old value — and that is exactly what `CenterOwner` reads.
  `FitToMainWindow` therefore sets both.
- **The sizes are still in the XAML**, on the same values, so the designer shows what the runtime
  does. The call is the guard, not the normal case.
- **Why a class and not three pairs of numbers**: the rule is "at most as large as the main window",
  not "at most 1292×752". As a number in three files it would have survived until somebody touched
  the main window — and would then be silently wrong, because a too-large window reports nothing.
- **A new modal calls `FitToMainWindow` too.** Without the call the rule does not apply there, and
  that would only surface when somebody looks.

**Behind a modal the main window steps back** — dimmed to 0.4 and blurred with a radius of 8. Both
come from one place, `Dialogs.Backdrop()`, and both are taken back when the returned scope is
disposed.

- **A scope and not two lines per call site.** The same pair of lines stood at four places, two of
  them restoring with `Opacity = 100` instead of `1.0`, one modal had no treatment at all, and not
  one of them sat in a `finally`. An exception inside a dialog therefore left the window dimmed for
  the rest of the session, with nothing left to click that would bring it back.
- **It restores what it found, not a fixed `1.0`.** A modal opened out of a modal hands back the
  state of the one underneath instead of resetting it.
- **The effect sits on the `Window` itself**, which works here because `WindowStyle="None"` leaves
  no chrome for it to render around.
- **A new modal opens the scope at its call site** — the same kind of obligation as the
  `FitToMainWindow` call in its constructor. Two rules, two places, neither of them automatic.

The concrete sizes, the padding reasoning and the scrolling mechanics of the account dialog are in
[`ui-layout.md`](ui-layout.md).

## The account dialog

The dialog has tabs and a fixed size. It used to be a flat vertical list with
`SizeToContent="Height"`; the rebuild came not from taste but because further games are to get
fields — and a list you hang four game blocks onto grows arbitrarily long.

| Tab | Content |
|---|---|
| ACCOUNT | battletag (display), email, password, the **game/region matrix**, notes |
| HOTS | penalty games and placement side by side, the rank grid below them — under that the embedded hero picker |
| OW2 · WOW · DIA | one dashed box each, "Nothing to configure yet." |

- **The fixed size was the actual lever.** Two workarounds hung off `SizeToContent="Height"`: the
  rank picker had to be an overlay, and the hero picker its own window — 90 circles would otherwise
  blow the dialog open. Both reasons are gone; what does not fit scrolls inside the tab.
- **The games carry short names here** (`GameVisuals.ShortLabelFor`), and that is not taste: five
  tabs sit side by side, and "HEROES OF THE STORM" alone would need more room than the other four
  together. They live in `GameVisuals` next to icon, colour and full name — a second derivation in
  the dialog would drift apart.
- **The tabs are `ToggleButton`s plus visibilities, not a `TabControl`.** Its template demands parts
  with fixed names (`TabPanel`, `PART_SelectedContentHost`), and a missing one fails without a
  compile error and without a binding warning — the same trap as with the `ComboBox`. It is built
  like the main window tabs and like the game filter: one pass-through property each, `NotifyTabs`
  snaps back the button that unchecks itself on click.
- **The tabs of the other three games are not a `ContentControl` with templates** but lie on top of
  each other in the same `Grid`. They share the same `DataContext`; a detour via `DataTemplate`s
  would only add another place where a forgotten line shows the class name.
- **Unticking a game loses its tab.** If it was open, the dialog jumps back to ACCOUNT
  (`NotifyTabs`) — otherwise it would sit on a tab that no longer exists. The values stay in
  `data.yaml` and are merely no longer shown.
- **Everything in the HotS tab belongs to ONE region** — rank, penalty games, placement and the
  hero picker. Whoever plays in two regions maintains them one after the other; above sits a
  switcher bar, below a sentence naming the region being edited. **The switchers appear only from
  two regions on** — with one they would be a switch with exactly one position. The sentence stays
  anyway: it says what the values refer to.
- **Switching runs through `StashRegion` and `LoadRegion`**, and the order is the whole logic:
  first save the typed state into the working copy, then load the other. The properties of the tab
  are, until saving, the **only** place the entries exist — forgetting the stash loses exactly the
  region that was open. `Execute` therefore calls it again before building the account.
- **The dialog works on copies** (`HotsRegionData.Copy`). Without them merely tapping a medal would
  write into the entity, and "Cancel" would still have changed something.
- **The hero picker is rebuilt on a region switch**, not refilled: `HeroPickerViewModel` holds its
  selection itself, and merely re-setting the list would not reach it. Hence `HeroPicker` is a
  settable property with notification.
- **The hero picker is literally the same surface as in the filter** — `HeroPickerView` with
  `Embedded = true`. The `HeroPickerViewModel` lives as long as the dialog and **is** the source of
  the selection; `EffectiveHeroes` reads from it.

Three validation rules, all in the same `RefreshDialog`:

- **At least one game is mandatory.** Every one of the four counts. The requirement also closes the
  gap through which an account without any tick could arise: no filter symbol would match it, and
  it would be unreachable in the overview.
- **The battletag is display, not a field.** It is read, not typed; a new account shows
  `not read yet`. A greyed-out input box would also stand there but would invite clicking and then
  do nothing.
- **The save button switches while typing**, not only on focus loss. Beside it stands, in plain
  words, which mandatory field is still missing — especially needed for the game tick, which does
  not look like a mandatory field.

**The save button had two causes, not one.** The visible one: the `TextBox` bindings sat on the WPF
default `LostFocus`, now on `UpdateSourceTrigger=PropertyChanged`. The invisible one: `Password`
was an auto-property **without** `OnPropertyChanged` — whoever filled in the password last saw the
button stay off until they touched some other field.

## Main window tabs

The window has two tabs: **ACCOUNTS** and **SETTINGS**, top left beside the logo.

**The rebuild was small because the switching was already there.** `MainWindow.xaml` has long bound
a `ContentControl` to `MainViewModel.CurrentView` — only nobody set the value more than once. Added
were: two `ToggleButton`s, one `DataTemplate` each in `App.xaml`, and `SettingsViewModel`.

- **One `DataTemplate` per tab is mandatory.** `CurrentView` carries a ViewModel, and the
  `ContentControl` looks up the matching view in `App.xaml`. Without the line the window shows the
  **class name** of the ViewModel — no compiler error, no binding warning.
- **There is no unselecting**, same as with the game filter and for the same reason. A
  `ToggleButton` unchecks itself on click *before* the binding writes; `MainViewModel.NotifyTabs`
  makes it re-read the source and snaps it back.
- **They carry no template of their own** but inherit `BattlenetIconButtonTheme` — the same style
  the four game symbols of the filter bar are drawn with. Only what an image does not need and text
  does is added: font colour and size. **Without the colour WPF draws the text black** on dark grey,
  because the style is made for images and sets none.

  An early draft had its own template here with rounded corners, a hover surface and a second blue.
  That looked like a different application from the toggles two lines below — and would have been
  the place where two appearances drift apart.
- **The settings tab is only built on first visit.** Its constructor scans the usual installation
  locations — cheap, but no reason to do it on every start.

## The account row

`AccountCardView` is a **row across the full window width**. The class name stayed so that
`AccountsView.xaml` and the converter could stay unchanged; it no longer describes the thing.

**Why a row**: not denser, but **comparable**. Equal values stand below one another. Whoever wants
to know which account has the most gold scans a column instead of nine rows of cards. That is why
the columns are fixed and not content-dependent, and why the currencies are docked right at a fixed
width.

The complete pixel budget — column widths, row height, panel breakdown, hero strip, stats, button
column, tint derivation — is in [`ui-layout.md`](ui-layout.md). What matters here is the
mechanism:

### The view follows the filter

**The game filter of the filter bar is exclusive and always set** — exactly one game, where four
independent ticks used to be. That makes it not merely a selection but the **view choice**: filter
on Overwatch and every row shows the Overwatch panel.

**On startup it stands on Heroes of the Storm.** That is not cosmetic: since the rows no longer have
tabs of their own, the filter is the only way to switch the panel. Without a set filter every row
would show its own game — the columns would line up but compare apples with pears. HotS, because
only there is any data at all.

| Place | Role |
|---|---|
| `AccountsViewModel.GameFilter` | the one chosen value, `null` = none |
| `AccountsViewModel.{Overwatch,Hots,Wow,Diablo}Filtered` | four bools as pure pass-through onto `Choose` |
| `AccountsViewModel.Choose` | chooses; a `false` is ignored and the button snapped back |
| `GameFocus.Current` | static, set by the filter, read by every row on construction |
| `AccountCardViewModel.PreferredGame` | filter first, else HotS, else the row's first game |
| `BattlenetAccount.PlaysIn(id, region)` | **the question every row asks** — is this game played here |
| `BattlenetAccount.Plays(id)` | is it played anywhere at all; the wrong question inside a row |
| `Games` (`Backend/Entity`) | the four ids — `GameVisuals` refers here, because `Backend/` does not know `UI/` |

- **The price is the combination.** "Overwatch **and** HotS" can no longer be asked. Two chosen
  games would have no answer for the row.
- **The second price: "all accounts" no longer exists.** Whoever has only ticked Overwatch is
  invisible while HotS is chosen — reachable via their own symbol.
- **Why a static value and not an event**: the row ViewModels arise through the `IValueConverter` in
  the `ItemTemplate`, i.e. anew per row and again on every re-filter. An event would have to be
  subscribed by every one of them — and since they get thrown away, they would hang on the static
  event list forever. Instead the filter sets the value *before* re-filtering; `Refresh()` throws
  away all containers, and every fresh ViewModel reads it in its constructor.
- **This only holds without virtualisation.** The default panel of an `ItemsControl` is a plain
  `StackPanel`, and `AccountsView.xaml` sets exactly that. Turning it into a
  `VirtualizingStackPanel` brings back an event plus its unsubscription.
- **Filtering on Overwatch, WoW or Diablo shows 27 dashed boxes.** That is the consequence and not
  an error: for those three there is one `bool` each on `BattlenetAccount` and nothing else. A
  dashed `Rectangle` with `StrokeDashArray` and not a `Border`, because `Border` cannot dash.

**The extra filters belong to the game and disappear with it.** Hero filter and free rotation are
Heroes of the Storm; filter on another game and they are gone along with the separator before them.
The condition lives once, as `AccountsViewModel.HotsFiltersVisibility`.

- **Hidden means ineffective, not deleted.** A filter one cannot see but which still removes rows is
  the worse half of both: the list would be shorter than it should be, and nothing on screen would
  say why. The selection itself stays and applies again as soon as HotS returns.
- **The dropping happens at exactly one place**, in the `OnPropertyChanged` override: there an empty
  hero list goes into the predicate for another game. `CreatePredicate` itself stays the pure AND
  chain and knows nothing of the rule — otherwise the game dependence would sit in two places.
- **Search box and archive toggle stay** — they belong to no game.

**WPF trap the row hangs on**: background, border and shadow are set in the `Border.Style` and
**not** as attributes on the `Border`. A locally set attribute has higher precedence than any style
trigger — write `Background="#1E1F24"` directly and the hover state is ineffective, without anything
reporting an error. (Inside a `ControlTemplate` this does *not* apply — there a value on the
template element is a template property, and `ControlTemplate.Triggers` sit above it.)

**Rounded corners are not inherited.** A `Border` does not clip its child to its own `CornerRadius`.
The tint surface therefore carries its own rounding.

### The three click zones

Since 23.08.2026 the row answers to the mouse, and it does so in three places that must not
overlap:

```
+- row -----------------------------------------------------------------+
| PUPSI#22733 |  medal   | OOOOOOOO  32/90 | gold  shards |   >     ... |
| EU . 22.08. |          |                 | gems  chests |             |
+-------------+----------+-----------------+--------------+-------------+
       ^            ^             ^                              ^
  click        click         click                          untouched
  -> edit      -> rank grid  -> edit dialog,                (the two round
     dialog       in a popup     HotS tab                    buttons)
```

Every one of them is a **single** click, and the whole row carries `Cursor="Hand"` so that says
itself. It was a double-click on the row until 23.08.2026 — the convention for a list row, and
therefore invisible: nothing on screen suggested trying it.

| Zone | What happens |
|---|---|
| the row | `OpenSettingsCommand` — the edit dialog, on **this row's region** |
| the rank medal | a popup with the same 28 medals the HotS tab shows |
| the hero strip | the edit dialog on the HotS tab, where the picker sits |
| the two round buttons | their menus, as before |

- **The exceptions need no code, and that is what lets the row be this greedy.** The medal, the
  strip and the two round buttons are all `ButtonBase`, and `ButtonBase` marks
  `MouseLeftButtonDown` as handled — so the row's binding never sees the gesture from any of them.
  A `Popup`, being its own window, never bubbles into the row at all.
- **`LeftClick` matches on the click count**, so a double-click on the row fires the binding once
  on its first click and sends the second into the modal that just opened. Harmless, and the reason
  there is no second binding for it.
- **The medal is a `ToggleButton`**, and that is what makes an accidental double-click harmless: the
  second click closes the grid again instead of picking whichever medal happens to lie under the
  pointer. The same construction as the start menu — `StaysOpen="False"` only reacts to clicks
  *outside*, so `PickRank` resets the toggle as its first instruction.
- **The command sits in `HotsRankChoice`**, not in a `RelativeSource` walk. A popup lies outside the
  layout tree of the row, and a walk out of three nested `ItemsControl` would have to count levels.
  Since the dialog passes the same field, there is one mechanism and not two — see
  `UI/MVVM/HotsRankGrid.cs`, which lays out the grid for both.
- **Which region a click writes to is not a question.** A row *is* an account in exactly one region,
  so `_row.Region` is the answer. That is the one place where this is easy: reading out of a running
  client has to ask, because the game shows the region on no screen.
- **A rank picked by hand does not set `ReadAt`,** and does not clear `PlacementsPending`. A
  correction is not a reading, and owing the placement matches is a state the medal does not end.
- **The strip must not paint a hover surface.** The separator rings between the portraits are
  *holes* that show the ground behind them; a surface appearing under them would make every ring sit
  visibly wrong. The medal has no such illusion and may have one. Both say "clickable" through the
  pointer.
- **The hero strip is deliberately not a quick pick of its own.** Ninety heroes are not a popup, and
  the list is the value this application *measures* out of the collection — a comfortable way to
  type it by hand would invite it to drift away from what the game holds. The rank is one value out
  of 28 and gets the short way.
- **Without a rank and without pending placements there is no medal**, so there is nothing to click.
  That follows from `RankVisibility` and is not a gap to fill: the way in is the dialog, which is one
  double-click away.

## Settings

Four things, all in `~/.smurftown/settings.yaml`. **Saving is immediate, there is no save button** —
the same pattern as with the rank and the hero picker.

```yaml
hotsPath: 'D:\Games\Heroes of the Storm\Support64\HeroesSwitcher_x64.exe'
inputSpeed: Fast          # Slow | Normal | Fast
clientLanguage: German    # German | English | French | SpanishSpain | SpanishLatin
appLanguage: German       # English | German | French | Spanish
```

**The update check is deliberately not among them.** It runs hourly and has no switch; the reasoning
is in [self-update.md](self-update.md#there-is-no-setting). A `checkForUpdates` key in an existing
file is a leftover and is ignored. The `ABOUT & UPDATES` card at the foot of the tab *shows* the
check — version, date of the last one, whether this build may replace itself — and its `Check now`
brings the next one forward without switching anything off.

### An explanation is read once, a state is read every time

That sentence is the layout of this tab. Until August 2026 every setting stood under a paragraph
explaining it — 209 words for four controls, around 340 points of height, which is why a tab with
four dropdowns scrolled.

- **The explanations are unchanged and hang on an info sign** beside the label (`ContentControl`
  with `BattlenetInfoIconTheme`, the text as its `ToolTip`). They are the same `settings.*Hint`
  keys as before, so nothing was rewritten and `TextsTests` stays green.
- **What stays on screen is what changes with the choice**: `Measured against the running client`
  beside the client language, `Half the pauses` beside the speed, the warning about a path that no
  longer exists, the progress of a running scan. Those belong next to the control, not at the end
  of a paragraph.
- **What said the same thing twice went**: `settings.interfaceLanguageNote` ("Applies at once")
  stood beside a hint that already said so, and it is deleted rather than moved.
- **Two cards, because one setting belongs to Smurftown and three describe the game.** That used
  to stand in the running text and now stands in the structure — which is what allowed the labels
  to get shorter: `INTERFACE LANGUAGE` became `Language`, because the card above it says
  `SMURFTOWN`.
- **The tooltip needed its own theme first.** The WPF default is a system window — light ground,
  black text — and it disappears after five seconds, which is about half of a sixty-word text. See
  `BattlenetToolTipTheme.xaml`: dark surface like the menu, wrapping at 400, `ShowDuration` two
  minutes, `InitialShowDelay` 150. `ToolTipService.*` belongs on the **owner** of the tooltip, not
  in the tooltip's own style — WPF reads it from the element that was pointed at.

**`clientLanguage` and `appLanguage` are two questions**, see [Language](localisation.md). The app-language
dropdown therefore sits **at the top** of the tab and not next to the client language: it is the only
setting there that has nothing to do with Heroes of the Storm.

**The default for `appLanguage` is the system language** (`AppLanguages.FromSystem`), not a fixed
value — unlike `clientLanguage`, where German had to stay the default because it was previously
hard-wired. Here there is no "previously": the app spoke only English, so every existing
`settings.yaml` without this key belongs to somebody who never had a choice. What is read is
`CurrentUICulture` and not `CurrentCulture` — the second says how numbers are formatted, the first
in which language Windows talks to the human.

The five client values are the five variants the client offers. In the dropdown they appear **in
their own language** (`Deutsch`, `English (US)`, `Français`, `Español (ES)`, `Español (AL)`) — the
one justified deviation from "everything a human sees is English": the value is only usable if it
can be held word for word against what the game shows. The list is generated from the enum and not
maintained by hand, so a future variant is not forgotten in exactly one place.

> **Trap with every new field here, and it is set twice**: `SettingsGateway.Current()` builds the
> copy for editing **field by field**, and `SettingsViewModel.Store()` builds the object to save
> the same way. What is missing from either falls back to the default on the next save — silently,
> without a compiler error and without a log line. The same trap as in the account dialog, and for
> the same reason: what gets saved is a **newly built** object, not the edited one.

### Where the game lives

The path used to be in `screen-map.yaml`. That was the wrong place: calibration describes how the
game **looks** — anchors, offsets, thresholds — not where it lives.

Searching happens in three stages, so the expensive one usually falls away:

| Stage | What | Cost |
|---|---|---|
| 1 | the stored path, if it still exists | nothing |
| 2 | `GameInstallations.Likely()` — program folders, plus `{drive}\Heroes of the Storm`, `\Games\…` and `\Program Files (x86)\…` on every fixed disk | fractions of a second |
| 3 | `GameInstallations.ScanAll()` — all fixed drives, recursive | **minutes**, only on demand |

- **The stored path is checked on every access.** An uninstalled or moved installation should not
  make the app insist on a dead path while a valid one lies beside it.
- **The full scan runs in the background, reports where it is, and can be cancelled.** A mute UI
  looks like a crashed one during that time.
- **It skips folders where nothing can be** (`windows`, `$recycle.bin`, `node_modules`, …) and goes
  at most twelve levels deep — the limit guards against directory loops via junctions. On finding an
  installation it does not descend further.
- **A path pointing nowhere is stated plainly in the tab.** Without that it only surfaces at the next
  game start, and then in the middle of a flow already running.
- **`GameSession.StartAndLogin` gets the path as a parameter**, it does not fetch it itself.

### Which language the game runs in

`clientLanguage` decides **what** the OCR compares against — and nothing else. Calibration is
untouched by it: an English client moves no anchor, it merely labels everything differently.

- **Switching happens in the game, not with us.** Our setting only says what we compare against — it
  changes nothing on the client. Whoever switches the client and forgets us reads nothing; whoever
  switches us and forgets the client, likewise. Both cases are silent, which is why the hint beside
  the dropdown names the language in plain words.
- **`GameVocabulary.Current` is a static field set from outside** (`SettingsGateway.Apply`, called at
  startup and after every save) — word for word the same construction as `InputSender.Pace`.
- **`HeroNameMatcher` remembers which vocabulary it was built with.** Its candidate set is cached;
  without that memory it would survive a language switch and then run silently against the wrong
  names — the most expensive error of this changeover, because it shows neither at compile time nor
  in the log.
- **`TextReader` has the same cache and the same trap.** The setting decides not only what is
  compared against but also **what reads**: recognition used to be fixed on `de`. The setter of
  `TextReader.LanguageTag` therefore discards the built engine.
- **If the Windows language pack is missing, it falls back — but loudly.** On this machine only
  `de-DE` is installed; for French and Spanish the German recogniser reads, which works for Latin
  script and gets worse with accents. The warning in the log names both languages, so a missing word
  is not hunted in the wrong place. Details and the DISM command:
  [`client-language.md`](client-language.md).
- **A language switch needs no restart.** The readers read `Current` on every access; the constants
  in `ProfileReader` therefore became properties — a `const` would be compiled into every caller.

### How fast typing and clicking happen

**First the waste was removed, then the slider came.** `InputSender.ClearField` emptied every field
with 64 backspaces, each as its own `SendInput` call with 40 ms hold time:

```
per field:  Ctrl+A 130 ms · Del 80 ms · End 40 ms · 64x backspace 2560 ms · 80 ms
          = 2.9 s          x 2 fields = 5.8 s, only to empty two fields
```

The 64 keystrokes now go out in **one** call — `SendInput` takes a whole array, and an input field
evaluates backspaces in order. A **game scene** does not, which is why `Space()` stays on individual
events with a pause between them; the same distinction as with the scancode.

The slider is a **factor on every pause**, not 25 individual values:

| Level | Factor |
|---|---|
| Fast | 0.5 |
| Normal | 1.0 — the measured baseline |
| Slow | 1.75 |

- **Every fixed number in `InputSender` runs through `Pause()`.** A setting that misses half the
  pauses would be worse than none. The three waits in `GameSession.FillCredentials` belong to it and
  call the same method.
- **The timeouts deliberately do not hang off it** (`WindowTimeout`, `LoginScreenTimeout`,
  `MenuTimeout`, …). They wait for the game, not for us — a tighter value speeds nothing up there, it
  only gives up earlier.
- **`InputSender.Pace` is a public field set from outside** (`SettingsGateway.Apply`).
  `Backend/Automation/` does not fetch the value itself — the same layer rule as with the game path.
