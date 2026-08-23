# The data model

What `data.yaml` holds, and at which of the three levels. The first section is the one everything
else follows from: the game state hangs off the region, and the region hangs off the game — the
same battletag has a different rank, different heroes and different gold in Europe than in the
Americas.

Which of these fields the read from the running game writes is in
[game-integration.md](game-integration.md); how they reach the screen in
[ui-conventions.md](ui-conventions.md) and [ui-layout.md](ui-layout.md).

## Regions

**The game state hangs off the region, not off the account.** The same battletag has a different
rank, different heroes and different gold in Europe than in the Americas — that is the one fact
everything below follows from.

**And the region hangs off the game, not off the account**, and that is the second fact. Heroes of
the Storm can be played in Europe and the Americas while everything else runs in Europe only.

```yaml
regionsByGame:            # key = game id, value = its regions; at least one entry mandatory
  hots: [Europe, Americas]
  wow: [Europe]
hotsByRegion:
  Europe:
    tier: Gold
    division: 3
    heroes: [muradin, li-ming]
    gold: 3835
    readAt: 2026-08-21T15:14:00
  Americas:
    tier: None
    heroes: []
```

**`regionsByGame` replaced the four booleans** `overwatch`, `hots`, `wow` and `diablo`. They said
the same thing twice over from the moment the regions moved down here, and the pair could
contradict itself: a game ticked without a single region was an account **nothing showed** — the
row it would be edited in did not exist. Whether a game is played is now one question with one
answer, namely whether it has a region (`BattlenetAccount.Plays`). A game without regions is
therefore **removed** from the map rather than stored empty — `SetRegions` is the only place that
enforces it.

**A row is created per region in which any game is played** — the union over `regionsByGame`
(`BattlenetAccount.PlayedRegions`). Visible is always the one of the filtered region, since the
region filter is exclusive and always set. The list consists not of accounts but of pairs —
`AccountRegion`, a record of account and region. It is **not persisted**: `data.yaml` still holds
one entry per account, the pairs are formed on load and rebuilt on every change
(`BattlenetAccountGateway.RebuildRows`).

**The row carries no game, and that is deliberate.** Which game it shows is decided by the
exclusive game filter; whether this row plays that game at all is asked by the filter predicate —
`PlaysIn(game, region)` and not `Plays(game)`. A row per game *and* region would be the same set
once more, with a field nothing reads. The price is that the row set alone is no longer the answer:
filter on World of Warcraft and the American row of a European-only WoW account has to be dropped
by the predicate.

| What | Level | Why |
|---|---|---|
| Email, password, notes, archive | Account | credentials, not game state |
| Battletag (`name` + `discriminator`) | Account | **global** at Blizzard, the same in every region |
| Which games are played, and where | **Game** | HotS in two regions, WoW in one — one map, not two lists |
| Rank, division, placement | **Region** | Storm League is rated per region |
| Heroes | **Region** | ownership is region-bound |
| Gold, shards, gems, level, chests | **Region** | they grow separately per region |
| Penalty games | **Region** | leaving in Europe still lets you play in the Americas |
| Read timestamp | **Region** | exactly one region is read at a time |

- **`RegionsByGame` and `HotsByRegion` are two different questions**: the first is "is this game
  played in this region", the second "is there anything stored here". An entry in `hotsByRegion`
  only appears once there is something to save — a freshly ticked region is not in there at all.
  Read via `HotsIn` (yields `null`), write via `HotsFor` (creates); **never directly**, or mere
  display creates entries.
- **`null` means "never read in this region"** — the same distinction as `readAt`, one level up.
  The row then shows dashes, not zeros.
- **An account without a single game/region pair would be invisible** and thereby unrepairable: the
  edit button sits in the row that would not exist. The dialog therefore enforces at least one
  tick, and the gateway sets HotS/Europe on load if necessary — with a warning in the log; nothing
  is repaired silently.
- **An unticked box deletes nothing.** The game state stays in `hotsByRegion`; whoever re-ticks the
  region finds rank and heroes there. The same stance as with the archive: the data is expensive,
  hiding it is cheap.
- **Everything a row shows is asked per region.** `PlaysIn` and not `Plays`, in `CreatePredicate`,
  in `CanPlayAnyHero`, in the row's game symbols, in `AvailableGames` and in the hero picker's
  match counter. Whoever asks `Plays` in one of those places shows the American row the rank the
  account has in Europe — and nothing reports it.
- **`GameSession.StartAndLogin` gets the region of the ROW.** That way the same battletag logs into
  Europe via the Europe row and into the Americas via the Americas row — and what is read afterwards
  lands in the game state of exactly that region.
- **Three regions, no China** — checked against the running game: the login form's dropdown has
  exactly three entries, on a German client `Amerika`, `Europa`, `Asien`.
- **The enum names are our persistence, not the game's.** They appear that way in `data.yaml` —
  also as **keys** of `hotsByRegion` — and need to match nothing on screen; only the click path
  needs the real wording, and that is language-dependent.
- **All three regions are selectable.** The entries sit as `americasAbove` / `europeAbove` /
  `asiaAbove` in `screen-map.yaml`, measured as an offset above the closed field: **137 / 99 / 61**,
  line spacing 38. `ScreenMap.AboveFor` is the only place that maps region to calibration value.
  Verified at **two** resolutions — see [`calibration.md`](calibration.md).
- **The second click must not bring the window to the front again.** `SetForegroundWindow` closes
  the open dropdown, and the click then lands on the background — the region stays on `Amerika`,
  the game logs in there, and the account is **empty** on that region: 0 gold, no heroes, welcome
  screen. That looks like a broken account and is not one. `SelectRegion` therefore calls
  `BringToFront` once beforehand.
- **After every sign-out the region falls back to `Amerika`** — not only after a start.

### Every account needs a game in a region

An account whose `regionsByGame` is empty produces **no row at all** — and can then no longer be
repaired either, because the edit button sits in the row that does not exist. The account dialog
rules that state out; a hand-edited file can still hold it.

`BattlenetAccountGateway` therefore repairs it while reading: such an account is set to Heroes of
the Storm in Europe, and the fallback goes into the log. **Nothing is written for it** — the repair
stands in memory until the next change to that account saves the file anyway. A read that rewrote
the file would be a start that changes data nobody asked it to change.

## HotS ranks

Storm League rank per account and region, two fields on `HotsRegionData`:

```yaml
tier: Gold        # HotsRankTier: None | Bronze | Silver | Gold | Platinum | Diamond | Master | GrandMaster
division: 3       # 5 (lowest) to 1 (highest); 0 for None, Master, GrandMaster
```

- **Only Bronze–Diamond have divisions** — `HotsRankTiers.HasDivisions()` is the only place that
  decides this. Master and Grand Master carry a score or a leaderboard position in-game; we
  deliberately model **neither**.
- **Normalisation happens on save**, not in the setter:
  `AddOrEditAccountViewModel.EffectiveTier`/`EffectiveDivision`. Without the HotS checkbox the
  rank falls to `None`, division-less tiers get 0. Setter validation would be wrong here — during
  deserialisation the order of property assignments is not guaranteed.
- **Image paths only via `HotsRankImages.PathFor()`.** Do not assemble them at a second place —
  that exact duplication already happened once with the battletag→Windows-user derivation.
- **Display**: only the medal in the HotS panel of the row — it carries tier and division in the
  image. The plain text lives in the tooltip (`RankName`) and nowhere else. Deliberately no rank
  filter and no rank sorting.
- **In the dialog the rank grid is always visible** — no overlay, no popup, no button, and no
  second medal beside it showing the current state. Both would be a second answer to the same
  question. The selected rank is highlighted: not selected means dimmed (0.35), plus a border on
  the selected one in `#1A73E8`. Selection sits in the `HotsRankChoice` record (`IsSelected`)
  and not as a comparison in XAML: two values have to match at once (tier **and** division),
  otherwise the whole row would light up.
- **The grid is rebuilt on every rank change** rather than created once and mutated.
  `HotsRankChoice` is an immutable record without notification, and a `static readonly` field
  would be shared between several open dialogs — one would move the other's selection. Rebuilding
  28 records costs nothing.

**The 27 medals and the "no rank" disc are generated**, not hand-drawn - by
`tools/build-rank-assets.py` and `tools/build-placement-icon.py`. `HotsRankImages.NoRank` points
at `Ranks/norank.png`, which `build-rank-assets.py` deliberately does not emit, or the next run
would overwrite it. Sizes, the measured geometry and the source-sheet trap that puts the division
digit visibly off-centre are in [`assets.md`](assets.md).

## Penalty games (leaver penalty)

Whoever leaves a HotS match by disconnect has to serve 1–n penalty games. One field on
`HotsRegionData`:

```yaml
penaltyGames: 3   # 0 = none; clamped to 0..99 on save
```

- **The field is read, not only typed.** `PenaltyReader` checks in the menu whether a red-bordered
  warning triangle sits below the profile picture on the right, and reads its hint text if needed.

  The first step costs no OCR: in the 38×37 box of the calibration there are **660** strongly red
  pixels with an active penalty and **0** without (at 3251×1361 it was 557). The threshold of 100
  carries both resolutions with wide margin.

  **The count is exclusively in the hint text** shown on hover — the symbol only says *that* a
  penalty is running. The text contains exactly **one** number in both languages, so a digit
  search suffices and no word for it sits in `GameVocabulary`. If the recognition finds none or
  several, nothing is written.

  **A 0 is written, a `null` is not.** If the symbol is absent on a menu screen, that is proof no
  penalty is running any more — otherwise an expired entry would sit there forever. If it could
  not be looked at at all (wrong screen, no OCR, unreadable hint), the stored value stays
  untouched. Same rule as for the four stats.

  A second trail lies on the RANKED screen, where "You must not have deserter status" appears
  under unmet requirements. It is **not** built: it would cost a screen change, be
  language-dependent, and not name the count.

- **Hangs off the same HotS checkbox as the rank** and is normalised on save by the same pattern:
  `AddOrEditAccountViewModel.EffectivePenaltyGames` yields 0 without HotS.
- **The icon *is* the control** — there are no ± buttons and no input field. Left click counts up,
  right click down, both via `MouseBinding` directly in XAML, no code-behind handler needed.
- **Both clicks may always fire** — no `CanExecute`, no `IsEnabled`. The clamp to 0..99 sits in the
  setter of `HotsPenaltyGames`; a click at the limit fizzles harmlessly.
- **The surface is a `Border` with `Background="Transparent"` from the style** — not as an
  attribute (that kills the hover trigger, see [The account row](ui-conventions.md#the-account-row)) and not `null`,
  because a surface without a background catches no mouse and only the image itself would be
  clickable.
- **Visible state without text**: triangle at opacity 0.25 for 0, fully opaque from 1 plus a round
  number badge. Both hang off `HasPenalty` — a second property with an identical condition would
  only be another place that can drift apart. The tooltip `PenaltyHint` names state **and**
  operation; without it the right-click gesture would not be discoverable.
- **In the row only the warning triangle appears**, without a number and **only when > 0** — see
  [`ui-layout.md`](ui-layout.md) for its size and why 18 px is the ceiling for the
  corner. The count lives only in the tooltip: the question "how many" comes up more rarely than
  "any at all".

**Asset**: `penalty.png` is drawn by `tools/build-penalty-icon.py`, not cut out - see
[`assets.md`](assets.md).

## Placement matches

After a season start an account usually needs 3 placement matches before the rank counts again.
A bool, not a counter — the exact number is of no interest, and the game does not name it either:
the profile simply says `Platzierung` under `Sturmliga`, without a number and without "x/3".

```yaml
placementsPending: true   # rank is set but not yet valid
```

- **The rank stays put.** `Tier`/`Division` are not cleared — it is last season's rank, not an
  invalid value. Only the presentation changes. That holds for the read too: if `ProfileReader`
  reads the word `Platzierung`, it sets **only** this field and leaves the tier alone. A rank in
  the profile conversely proves the placements are done — then the field falls back to `false`.
- **The medal is dimmed (0.4), not replaced.** It carries last season's rank, and that is the
  information at stake; that it does not count yet is what the opacity says. The plain text is in
  the tooltip (`Gold 3 - placements pending`); `RankLabel()` is the only place that **names** the
  state.
- **Without a rank and with placements pending the `norank.png` disc stands there**, likewise
  dimmed. Without that fallback the state would be invisible in the row, because `PathFor` yields
  `null` there. Without a rank **and** without placements the spot stays empty — the rank is then
  simply nothing the row has to say.

## Heroes

Which heroes an account has bought. A list of ids on `HotsRegionData`:

```yaml
heroes:              # empty in old files - the key is simply absent there
  - muradin
  - li-ming
  - lucio
```

- **The id is also the file name of the portrait** (`UI/Images/Heroes/{id}.jpg`). Hence without
  accents, apostrophes and dots: `lucio`, `anubarak`, `lt-morales`, `dva`, `etc`. One key for both
  instead of two that drift apart.
- **Catalogue and portraits come from one run.** `tools/build-hero-assets.py` generates both: the
  90 JPEGs *and* `Backend/Entity/HotsHeroCatalog.Generated.cs`. Do not edit the generated file by
  hand — adjust the script and re-run. The hand-written half (`HotsHeroCatalog.cs`, lookup and
  resolve) sits beside it as `partial`.
- **Why the indices in `HotsHeroCatalog` are lazy**: `All` is in the generated file, `ById` and
  `ByRoleIndex` in the hand-written one. Across file boundaries the order of static field
  initialisers is not defined — built as a field, `All` might still be `null`.
- **Unknown ids are not an error.** A `data.yaml` from a newer app version can contain heroes this
  version does not know. `HotsHeroCatalog.Resolve` skips them when displaying; they are not
  deleted. **`Ordered()` appends unknown ids again at the end** — `Resolve` drops them, and
  precisely that would be a deletion when merging a partial reading.
- **Roles** come from `Data:{Hero}` in the wiki (field `role`) and sit in the enum `HotsHeroRole`.
  The enum order *is* the display order: Tank, Bruiser, Melee Assassin, Ranged Assassin, Healer,
  Support. Distribution: 13 / 17 / 10 / 30 / 16 / 4.
- **Role colours are our addition** — no hero carries one in-game. They live in `HotsRoleColors`
  and are shown in the **ring** around the portrait, not in the image: the portrait stays
  untouched, the ring disappears with the selection.
- **Not owned means dimmed** (opacity 0.3), not desaturated. Same reasoning as for the rank with
  pending placements: greyscale would cost a second asset per hero, opacity costs nothing.

**The picker is two files**, and that is the core of its construction:

| File | What is in it |
|---|---|
| `HeroPickerView.xaml` (`UserControl`) | the surface: search box, role chips, grid, counter, footer |
| `HeroPicker.xaml` (`Window`) | only the frame: size, `WindowStyle="None"`, Esc binding |

There are **three** callers (hero filter, rotation, account dialog), and the surface may exist
only once. **Three traps when embedding**, all three measured and all three without a compiler
error:

1. **`RelativeSource AncestorType=Window` hits the wrong host.** The commands of the role chips
   and hero circles sit in `DataTemplate`s and fetch the ViewModel via the ancestor. Embedded, the
   next `Window` would be the account dialog, whose `DataContext` is an
   `AddOrEditAccountViewModel` — the binding would run into nothing and no click would do
   anything. It therefore says `AncestorType=UserControl`, which hits the right file in **both**
   hosts.
2. **A `UserControl` brings its own name scope.**
   `FocusManager.FocusedElement="{Binding ElementName=SearchBox}"` sat on the window and no longer
   found the search box after the move. The line belongs on the `UserControl` itself.
3. **The file name `HeroPicker.xaml` is mandatory.** MvvmDialogs finds the view by a naming
   convention: `HeroPickerViewModel` → `HeroPicker`. Renaming the window gets you a
   `TypeLoadException` at runtime and no hint at compile time. That the new surface is called
   `HeroPickerView` is harmless — what gets stripped is `ViewModel`, not `Model`.

**Embedded, title, close cross and footer fall away** (`ChromeVisibility`), and the surface does
not scroll itself either (`GridScrollBarVisibility`) — see
[`ui-layout.md`](ui-layout.md) for the scrolling mechanics and the mouse-wheel trap
that comes with them.

**`Embedded` is deliberately a second axis beside `HeroPickerMode`** and not a fourth enum value:
the mode says *what* is being chosen (ownership, filter, rotation), `Embedded` says *where* the
surface hangs. Merged they would be six values, none of which would still show which question it
answers.

Operation: click toggles, Esc or the × closes. No OK/Cancel — the selection travels straight into
the calling ViewModel and is saved with the dialog, exactly as with the rank. Search box and role
chips narrow down; the two bulk buttons act **only on what is currently visible**.

**Filtering by heroes**: `OR` — an account stays if it owns **at least one** of the chosen heroes
**or can play it for free** (see [Free rotation](#free-rotation)). Unlike the game filter it is
**not** exclusive. The question behind it is "who has any of these", not "who has all of them".

## Free rotation

Which heroes are free for everyone this period. Unlike everything else here this hangs **not off
the account** but off the game — hence its own files instead of a field on `BattlenetAccount`.
There are two, and their order is the whole logic:

| Rank | Source | File | Applies |
|---|---|---|---|
| 1 | manual entry | `~/.smurftown/app.yaml` → `rotation` | only in the period it was set in |
| 2 | calendar | `Backend/Entity/rotation-calendar.yaml` | always |

**The rotation repeats annually** — the same calendar day carries the same 14 heroes. That is
measured, not assumed: **every** one of the 48 periods holds across at least two independent
years; 2023, 2024, 2025 and 2026 were checked on `nexuscompendium.com/rotations`. The app
therefore needs no live source but a table — and that table does not need maintaining, because it
does not change.

```yaml
# rotation-calendar.yaml - 48 lines, key month and day, without a year
periods:
  "08-15": [li-li, etc, zagara, the-butcher, kaelthas, artanis, hanzo, azmodan, ...]
```

**It still cannot be computed.** The first six slots follow a three-way cycle that **breaks twice
a year**: on `03-08` the Raynor group appears twice in a row, and on `04-15` Tychus sits in
Dehaka's slot. A table carries both outliers without a special case; a formula would have to know
them as exceptions, and whoever misses one silently marks the wrong heroes as free.

- **The app computes the period itself.** It changes on the 1st, 8th, 15th and 22nd of each month
  — cross-checked against every recorded rotation from 2024 to 2026. `HotsRotationPeriod` is the
  only place that knows this; `EndOf` computes the first of the following month after the 22nd
  instead of adding seven days. The split is deliberate: the **timespan** follows a rule, the
  **line-up** does not.
- **The calendar ships embedded in the application**; a file at `~/.smurftown/rotation-calendar.yaml`
  beats it — the same pattern as `screen-map.yaml` and for the same reason: the installation
  folder lives under `Program Files`, where one does not simply drop a file. Copying on first
  start would be the alternative and would have the opposite fault: a corrected table would never
  arrive with an update.
- **`HotsRotationSource` says where the list came from** (`Manual`, `Calendar`, `None`). Label,
  opacity and tooltip hang off that. A manual entry from an older period is **not deleted**, just
  no longer considered; the next entry overwrites it.
- **An empty manual list does not count as a state.** Otherwise an accidentally emptied picker
  could switch off the calendar for a whole period — silently, because an empty rotation filter
  looks like "no rotation set".
- **Unknown ids in the calendar are reported**, not swallowed. Unlike in `data.yaml`: there an
  unknown id means "written by a newer app version" and is deliberately skipped, here it simply
  means the file is wrong.
- **The symbol *is* the control**, with **one** gesture: left click opens the entry. The right
  click does nothing.

  There used to be a toggle on the left click that set the hero filter to the free heroes.
  **It was removed because its question was not one**: `CanPlayAnyHero` lets through whoever owns
  the hero **or** can play it for free — and everyone can play it for free. Choosing fourteen free
  heroes therefore matched every HotS account, i.e. all of them, and a filter that removes nothing
  is not a filter.

  **`CanPlayAnyHero` itself stays.** It is a different matter from the removed toggle: whoever
  deliberately picks a hero in the hero filter should also see the accounts that can play it free
  this period.
- **`FREE ?` is the exception.** The label reads `FREE 14`, with opacity 0.4 and the question mark
  only when the calendar does not know the running period. Meant literally: the app then does not
  know, and showing a date that no longer applies would be the worse lie. If a manual entry is
  behind it, the tooltip says `(set by hand)`.
- **The entry starts from the calendar state** (`EditRotation` pre-fills with `Free`). Whoever
  changes nothing changes nothing.
- **Two oddities of the table, both not errors**: **Mei** appears in no period at all, and seven
  pairs are identically staffed (`01-01` ≡ `10-08`, `03-15` ≡ `12-15`, …).
- **In the picker, free heroes carry a badge** — top right, the same nexus mark as in-game.
  Deliberately a badge and not a second ring: the border already carries the role colour. It shows
  in the account dialog and in the filter, **not** in rotation mode itself: there the selection
  *is* the "free" state.
- **Free means playable, not bought.** In the account dialog the free heroes stay fully opaque even
  if the account does not own them — bright means "playable" there. The counter beside it still
  counts ownership (`73 / 90 owned`). In the filter bar the exception does **not** apply: there
  bright means "selected by me". The mode condition sits in `HeroChoiceViewModel.PortraitOpacity`.
- **No forced check for exactly 14.** The counter shows `n / 14`, no more. The level-bound slots
  11–14 do not interest everyone, and the game stays the truth — not our constant.
- **The filter matches every HotS account as soon as a free hero is chosen.** The rule lives once,
  in `BattlenetAccountGateway.CanPlayAnyHero` — ownership or free rotation. The hit counter in the
  picker calls the same method; two copies would drift apart like the battletag derivation. **It
  counts rows and is therefore called "n entries match"**: hero ownership hangs off the region, so
  an account can match in Europe and not in the Americas. Accounts **without** the HotS checkbox
  stay out: technically any Battle.net account could play the rotation, but pure Overwatch smurfs
  in the hit list would be noise.
- **The account row stays untouched.** The rotation hangs off the game and not off the account —
  in a row that shows an account it would have no business.

**Asset**: `free.png` is drawn by `tools/build-free-icon.py` from Bezier paths taken out of
Blizzard's own game icon, with the swirl size tuned against a measured bright-area fraction -
see [`assets.md`](assets.md).

## Archive

An account one no longer needs is **archived, not deleted**. One field on `BattlenetAccount`:

```yaml
inactive: true   # absent in old files, default false
```

- **There is no delete function in the UI**, and that is deliberate. The credentials are the actual
  value of this app; a misclick in a list of 27 similar-looking rows must not be the last step.
  `BattlenetAccountGateway.Remove` still exists but is called by no button — whoever really wants
  to delete tidies up `data.yaml` by hand.
- **It is set solely via the fourth entry of the row menu** (`ArchiveCommand`), not in the dialog.
  There it runs along as a silent pass-through — without it an archived account would be active
  again after the next save in the dialog, because `Execute` builds a **new** account.
- **The feedback is the row disappearing**, hence no toast. To undo, switch to the archive at the
  top and press the same entry; the arrow in the icon then points out instead of in.
- **`SetArchived` calls `BattlenetAccountsFiltered.Refresh()`** and that is not caution: the list
  itself does not change, only a field on an element. An `ICollectionView` notices nothing of that
  and would leave the row standing until some other filter is set anew.
- **In the predicate the condition sits first** and is the only one breaking the pattern "not set ⇒
  lets through": an archived account should not show up even when the search text or hero filter
  matches it.
- **The archive toggle is not a filter, even though it sits in the filter bar.** Every other entry
  there lets everything through when unset; this one switches between two halves of the same list —
  active accounts or archived ones, never both. A third "show all" deliberately does not exist: it
  would be the view in which one mistakes an archived account for an active one. The remaining
  filters still apply inside the archive.
