# UI layout budgets

This page is the measured layout of Smurftown's account list and its dialogs — every width
below is already spent. It exists so the next person who adds an element does it by subtracting
from a budget, not by guessing at a size that merely looks comfortable. Two constraints run
through all of it: the account row is horizontally budgeted (a pixel gained somewhere is a pixel
spent somewhere else), and it is vertically exhausted (the rank medal already fills all but
6.5 px of the row's height, so enlarging any emblem enlarges the row — and a taller row costs
screens).

## Window and modals

No dialog is larger than the window behind it, nor does it fill it completely. The rule lives in
one place, `UI/MVVM/DialogBounds.cs`; each of the three modals calls `FitToMainWindow(this)` in
its constructor, right after `InitializeComponent()`.

| Window | Size | Relation to the maximum |
|---|---|---|
| `MainWindow` | 1340 × 800 | — |
| Maximum for a modal | **1292 × 752** | 800 − 2 × 24 |
| `AddOrEditAccount` | 1000 × 752 | height maxed out |
| `HeroPicker` | 1200 × 752 | height maxed out, 92 px of width free |
| `ErrorBox` | 400 × 250 | well inside, the call changes nothing |

- **The padding is the point, not just the non-overhang.** While a dialog is open, the main
  window sits at opacity 0.4 (`AccountsViewModel.ShowDialog`) — a dialog that fills it exactly
  would cover the very thing that dimming is meant to reveal. `DialogBounds.Padding = 24` turns
  that into a visible frame.
- **24, not 40**: the value is lost to content twice over — top and bottom. A visible strip above
  a surface dimmed to 0.4 doesn't need 40 points; 24 is enough, and 32 extra points of dialog
  height are worth more than a wider frame.
- **Called in the constructor, and that is not arbitrary.** MvvmDialogs sets `Owner` only
  afterward, and `WindowStartupLocation="CenterOwner"` computes position from size — clamp too
  late and it centers on the old size, landing off-target. For the same reason `DialogBounds`
  reads `Application.Current.MainWindow`, not `dialog.Owner`: the main window already exists at
  this point, the owner is still `null`.
- **`MaxWidth`/`MaxHeight` alone are not enough.** They render the window smaller, but `Width`
  and `Height` still report the old value — and that is exactly what `CenterOwner` reads.
  `FitToMainWindow` therefore sets both.
- **The sizes still appear in XAML**, at the same values, so the designer shows the same thing as
  runtime. The call is the enforcer, not the common case — raising a XAML height wins nothing;
  the constructor pulls it back down.
- **Why a class and not three number pairs**: the rule is "no larger than the main window," not
  "no larger than 1292×752." As a number copied into three files it would survive only until
  someone touched the main window — and then be silently wrong, because an oversized window
  reports nothing. Same reasoning as `HotsRankImages` and `GameVisuals`.
- **A new modal must call `FitToMainWindow` too.** Without the call the rule simply does not
  apply there, and nobody notices until someone looks.

## The filter bar

At **1340** px window width, **1300** px remain after the `DockPanel` margin. The window is this
wide specifically because the region filter alone costs 127 points — more than a narrower bar
could absorb — so the window grew rather than the bar being squeezed further.

Worst case (four hero chips plus `+n · ANY OF m`):

| Element | Width |
|---|---|
| Label | 88 |
| Four game toggles | 300 |
| Separator | 7 |
| Region filter, including separator | 127 |
| Separator | 7 |
| Hero filter, including clear cross | 280 |
| Rotation | 114 |
| Archive toggle (36+8) + search field (224) + plus button incl. margin (90) | 358 |
| **Total** | **≈ 1281 of 1300 — 19 px free** |

The search field has given up width twice to make room for later additions: 300 → 260 for the
rotation control, then 260 → 224 for the archive toggle. Each trade was exactly balanced — the
new element costs 44 points, the search field gives up 36, the rest comes out of the frame. The
region filter could not be funded the same way: 127 points cannot be carved out of a 224-wide
search field without making it unusable, which is why the window grew instead of the bar
shrinking. Anyone adding an element here recomputes the row as a whole; otherwise the `DockPanel`
silently squeezes the right-hand block.

**The region filter is exclusive and always set**, exactly like the game filter two toggles to
its left — it defaults to Europe at startup. A second click on the same abbreviation does
nothing; the `ToggleButton` snaps back via `NotifyRegions`, the same mechanism as `NotifySymbols`.

Unlike the game filter, this is **not a technical necessity**: a row shows exactly one region
regardless, and several selected regions would simply produce more rows. It is a deliberate UX
choice: two adjacent filter blocks with different logic — one exclusive, one not — cost more
confusion than the rows you don't get to see side by side. The price is the same as with the
game filter: someone who only plays in Americas is invisible under EU and reachable only through
their own abbreviation.

**The archive toggle is not a filter**, even though it sits in the filter bar. Every other entry
there lets everything through when "not set"; this one switches between two halves of the same
list — active accounts or archived, never both. A third "show all" deliberately does not exist:
it would be the view in which an archived account looks active. The remaining filters still apply
inside the archive — searching and narrowing to a game both still work there.

## The account row

`AccountCardView` is a row spanning the full window width. The class keeps its old name because
`AccountsView.xaml` and the converter could stay unchanged; the name no longer describes the
thing itself.

**Why a row and not a card**: not density but comparability. Density is not actually the
advantage — at 91 px height, 27 accounts need roughly 3.7 screens. What stays is the real reason:
equal values line up in a column. To see which account has the most gold, you scan one column
instead of nine rows of cards. Because of that, **columns are fixed and not content-dependent**,
and currencies are docked right at a fixed width.

**The row aligns with the filter bar.** Its `DockPanel` sits on `Margin="20,10,20,10"`; the row
has to hit the same 20 px — which requires **asymmetric** margins, because the scrollbar sits
outside the content while the filter bar isn't inside the `ScrollViewer` at all.

| | |
|---|---|
| 1340 | window |
| − 20 | left margin — exactly the filter bar's |
| − 10 | vertical scrollbar (`BattlenetScrollViewerTheme`, Vertical → Width 10) |
| − 10 | right margin: 20 MINUS the scrollbar's 10 |
| **= 1300** | **outer** — without this deduction the row would sit 10 px left of the bar |
| − 2 | border |
| **= 1298** | **inner, across three columns** |

| Column | Width | Breakdown |
|---|---|---|
| 0 — Name | 240 | 12 margin + 228 |
| 1 — Panel | 957 | 14 margin + 943 content |
| 2 — Buttons | 101 | 14 margin + 77 + 10 margin |

**Row height is 91 plus 3 px margin top and bottom = 97 per row.** It appears at two places in
XAML (`Height` on the `Border`, `d:DesignHeight` on the `UserControl`). 91 is a forced value, not
a chosen one: the rank medal fills 78 of it, leaving the same 6.5 px of headroom top and bottom
it always had. **Touching the medal size always touches row height too — and that costs
screens.**

The old card's width-tier table is now moot — there are no columns anymore, only rows. What it
warned about survives as a general warning: a row is *not* denser than four narrow cards — at
91 px height, 27 accounts need 3.7 screens against 2.1 for the card layout — it is only more
legible.

### Tint and separator ring

The whole row carries the game's color, not just a narrow stripe: a gradient from the accent
color on the left to transparent on the right, spanning the full width. Filtering to Diablo makes
the whole list look different from HotS, without a second symbol anywhere.

Freeing the row of its old per-row game-tab column and icon column recovered width that went
into other emblems:

| Element | Was | Is |
|---|---|---|
| Rank medal | 34×31 | **78×71** |
| Hero circles | 6 × 26 | **11 × 47** |
| Penalty triangle | 26 | **18, moved into the corner** |

- **The tint sits beneath everything else.** A layer on top would be the simpler build and the
  worse one: it would tint hero portraits, the rank medal, and the numbers along with the
  background.
- **The transparent stop carries the same RGB values as the opaque one.** WPF gradients are not
  premultiplied — if the stop were plain `Transparent`, the color would pass through a muddy gray
  on its way out instead of simply fading.
- **The hero strip's separator ring is the bottleneck.** It is a *hole* in the overlap and must
  therefore carry exactly the color behind it — and since the gradient, that color is
  position-dependent, while the ring itself is one flat color. Both are derived in `GameVisuals`
  from the same two numbers: `TintAtLeft` (**0.18**) and `StripMidpoint` (**0.36** — where the
  strip sits, as a fraction of row width). Set by hand, the ring would drift from the gradient on
  the next tweak.
- **0.18 is therefore an upper bound, not a matter of taste.** The stronger the tint, the larger
  the remainder a single ring color misses. Computed across all four accents, the error over the
  strip's width is at most **five steps per channel**, worst for Overwatch's orange. Anyone
  strengthening the tint recomputes this.
- **The border under the pointer is now also the game color** (`PanelHoverBorder`, accent blended
  45% over the base color) instead of the neutral `#3A3D46`. It can afford to be strong because
  it affects only *one* row, only while the mouse sits on it. The hover **fill**, by contrast,
  stays weak (`#23252B`) — it shifts the ground under the separator ring, and the ring does not
  follow.
- **The tint surface's own `CornerRadius` is mandatory.** A `Border` does not clip its child to
  its own rounding; without it, four right-angle corners would stick out of the row. 7, not 8,
  because the outer frame is 1 px thick — the same arithmetic as the now-removed accent stripe.

### Panel

Two states stacked, exactly one visible at a time, both laid out **horizontally**:

1. **HotS** — medal left-aligned, the four currencies right-aligned, hero strip · `+n` · hero
   count centered between them. The penalty triangle now sits as an overlay in the row's corner,
   outside this budget.
2. **No data** — a dashed box with plain text for Overwatch, WoW, and Diablo. An empty box would
   look like an error; a labeled one reads as a decision. Dashed as a `Rectangle` with
   `StrokeDashArray`, not a `Border` — `Border` cannot dash.

**Budget of the 943 px** — currencies are docked right at a fixed width, the rest fills left:

| Side | Element | Width |
|---|---|---|
| right | Currencies, 4×59 | 236 |
| left | Medal, 71+10 | 81 |
| left | Hero strip, 11×47, overlap 13 | 387 |
| left | `+n` | 28 |
| left | Counter | 56 |
| | **Total** | **552 of 707 — 155 px free** |

The free space is deliberate: a row filled edge to edge reads harder than one with a pause before
the currencies. The `+n` and counter figures above are text-width **estimates, not
measurements** — both sit generously; what's actually used is closer to 25 and 44.

**Row height is the second constraint.** The medal is 78 tall inside a 91-tall row, the hero
strip's separator ring 51 — leaving 6.5 and 20 px of headroom respectively. Enlarging the emblems
further means a taller row, and that costs screens: 27 accounts need roughly **3.7** screens at
91 px versus **2.5** at 60 px.

**No text sits beside the medal anymore.** The 28 medal images carry stage and division in the
picture, and the medal dims when placement games are pending. The plain-text version survives
only in the tooltip (`RankName`), now the sole place a rank is ever spelled out.

**Three blocks, two of them docked**: the rank medal left (`DockPanel.Dock="Left"`), the four
currencies right (`DockPanel.Dock="Right"`, fixed 236) — and between them, **centered**, the hero
strip with `+n` and the counter. The currencies lining up column-wise across every row is the
reason this list exists at all; the medal has done the same since it moved into this position.

The center split works out to `542 − 471` (strip 387, `+n` 28, counter 56) = 71 px, roughly
**35 per side**.

> **The price**: the first hero circle no longer sits at the same spot in every row. An account
> with three heroes has a shorter strip, and a centered short strip starts further right. Medal
> and currencies still line up vertically — and those carry the numbers that matter for
> comparison; the strip is a sample, not the statement.

Color, image, and name per game live in exactly one place, `UI/MVVM/GameVisuals.cs` — the same
reasoning as `HotsRankImages` and `HotsHeroImages`: a derivation kept in more than one place
drifts apart.

### Name column

Two lines: name (13, bold), `#tag` (11, gray), plus a **region abbreviation** on top and a
**last-read timestamp** (9.5, gray) below. The column is **240 px** wide, 228 usable — enough for
the longest possible battletag plus the region abbreviation behind it, which takes 36 px
including its spacing.

> The two `MaxWidth` values depend on this number, not on each other: the name gets 146 (192
> minus about 46 for the hash and discriminator), the email as fallback display gets the full
> 192, because only the abbreviation sits beside it. Anyone changing the column recomputes both.

**The region abbreviation names the row** — `EU`, `AM`, or `AS` as a small badge behind the name.
Without it, an account's two rows look identical except for rank, heroes, and gold — exactly the
values one wants to compare. **It is always present, even with only one region**: a value that
sometimes appears and sometimes doesn't leaves it open, on a quick scan, whether it's missing or
simply doesn't apply. No icon, just two letters — the game has no region icons of its own, and
three invented ones would be three symbols nobody recognizes.

**The last-read timestamp sits here, not in the panel.** Without it none of the adjacent numbers
can be placed in time — 1,800 gold from today and 1,800 gold from three months ago look
identical. In the panel it cost 130 px; in the name column it costs nothing, because a second
line is free there anyway.

**Accounts without a battletag** — the top line has two states, exactly one visible
(`BattletagVisibility` / `NameFallbackVisibility` on `AccountCardViewModel`):

| State | What's shown |
|---|---|
| Battletag read | Name (13, bold, white) + `#tag` (11, gray) |
| Not yet read | The **email** (11.5, `#C6C8D0`), tooltip explains why |

An empty name plus a bare `#` would look like a half-loaded value. The email is the account's
identity anyway (`Equals`/`GetHashCode`) and the one field every account has.

**The same rule governs sorting**, and that is where the trap sat: `BattlenetAccount.CompareTo`
sorts over `DisplayName`, but the list does not sort through it at all —
`BattlenetAccountGateway` uses two `SortDescription`s, and an `ICollectionView` with sort
descriptions never calls `CompareTo`. That is why `DisplayName` is a **property**, not a method:
`SortDescription` needs a property name.

> **Trap for every new property on `BattlenetAccount`**: YamlDotNet serializes **every** public
> property. A computed value would land as its own key in `data.yaml` — duplicated and ignored on
> the next read. That is exactly why `Battletag()`, `HotsRankName()`, and `Plays()` are methods.
> `HasBattletag` and `DisplayName` must be properties and therefore carry `[YamlIgnore]`. Anyone
> adding here sets that attribute too.

### Hero strip

Up to **11** portraits at **47 px** (`HeroChipLimit`), each a circle built from `Ellipse` +
`ImageBrush` with a role ring, followed by `+n`, followed by the count.

- **Eleven at 47 occupy 387 px.** Width is the upper bound; the count itself is a choice.
  Thirteen would fit (455 of 458); eleven leaves **71 px of slack**, split across both sides of
  the strip, deliberately. **Bigger and more can never both be had at a fixed width** — every
  point of circle size costs count.
- **The overlap grew along with circle size** (10 → 13). That keeps 72% of each circle visible,
  the same ratio as at 36/10; an unchanged overlap would have spread the circles noticeably
  further apart.
- **The `ItemsControl`'s compensating margin is always exactly the overlap amount** (13 here) —
  otherwise the first portrait sticks out on the left.
- **The separator ring is 51, not 47** — the circle plus a 2 px border on each side. The row must
  be able to carry it, or it gets clipped top and bottom; at the current row height of 91 that
  leaves it (91 - 51) / 2 = 20 px of headroom.
- **The ring carries the tint's color at its own position**, not the row's flat color — derived
  in `GameVisuals`, see [Tint and separator ring](#tint-and-separator-ring). Row hover stays
  deliberately **weak** (`#23252B`, two shades): it shifts the ground under the ring, and the
  ring doesn't follow.
- **Which eleven show is not sorted** — it's read order, i.e. alphabetical. The strip is a
  **sample**; the statement is the `29 / 90` beside it. Sorted by role, an account with eleven
  tanks would never show a healer.
- **Without any heroes there's a sentence**, not an empty strip: an empty strip would be a hole
  in the layout, leaving it open whether nothing was ever read or nothing is owned.
- **The counter shows `– / 90` when nothing was ever read and nothing is entered.** `0 / 90`
  would claim the account owns not a single hero — a statement that doesn't exist without
  reading. Same rule as the four stat columns. If heroes are entered, the number stands even
  without a read timestamp — then they were entered by hand.

### Stats

Gold, shards, gems, **and chests** — four equal columns at **59 px**. About 52 of that is used:
a 13 px icon + 5 px gap + a five-digit number at `FontSize` 11.5.

A dash instead of a number means **"never read"** — not "has nothing"; a 0 would be a claim that
doesn't exist without reading.

**The chest needs four paths, not three.** Lid, box, and lock alone read as an orange blob at
13 px. The dark **band** between lid and box is what makes the shape legible.

**The row needs no visibility switch of its own.** It lives inside the HotS panel, which is only
visible when HotS is selected — selectable only when the account has that game. A second
condition carrying the same meaning would be exactly the place two truths drift apart.

**The four currency icons are XAML shapes, not PNGs.** A coin, a triangle, a hexagon, and a chest
at 13 px are geometry, not image material. Colors are measured from a screenshot of the in-game
header bar.

### Button column

Two round buttons at **36 px**, both open a menu: start — and everything else. Budget:
36 + 5 + 36 = **77**.

- **Round, like the plus button in the filter bar, for the same reason**: what opens a menu needs
  no word, just a mark. The start button carries a white triangle instead of the label "Start".
- **The column is `Auto`-wide and sits on the right.** Freed points therefore don't move the
  buttons — they widen the panel to the left, where they land in the hero strip. The same holds
  in reverse: if the start button disappears, the second button doesn't shift; the panel widens
  instead.
- **Both menus are right-aligned under their button**, and the offset is computed:
  `36 (button) − surface width − 10 (shadow margin)`. The action menu is **240** wide (`−214`),
  the start menu **360** (`−334`). The column sits at the right window edge; a menu opening
  rightward would run off-screen, and while WPF would push it back on-screen, it would land
  somewhere nobody chose. That's why it's `Width`, not `MinWidth` — a surface sized to its
  longest entry would make the offset a guess.
- **The start menu's 360 px is necessary.** After icon (22), gap (10), and margins (32), a
  subtitle's available width is exactly `surface − 64`. Measured with `FormattedText`, Segoe UI
  11: German **291.6**, French 279.9, Spanish 256.5, English only **224.1**. At a narrower width
  only the shortest language fit, and the others were **silently cut off** on screen — noticed
  only by looking at the running app, never during the build and never in the log. The right edge
  stays fixed; the menu grows **left**, where there is room.
- **The start button is hidden, not dimmed**, when an account has nothing to start
  (`StartVisibility`). Since Overwatch, WoW, and Diablo have no path configured yet, an account
  without the HotS checkbox genuinely has no start option. A permanently dead button would say
  "something could work here" and never reveal what's missing.
- **The last entry of the action menu archives, it does not delete** — see the archive rules
  elsewhere in this document. Its icon is drawn from `Path` geometry rather than a PNG, same
  reasoning as the four currency icons: lid, body, and arrow are geometry at 17 px. The arrow
  flips with the direction of the gesture — inward when archiving, outward when restoring — so
  the same entry never shows the opposite of what it does.
- **Both menu buttons share `BattlenetRoundMenuButtonTheme`**, a `ToggleButton` style. The fill
  color comes from outside: start is blue (`#1A73E8`) because it carries the one action used
  often; the three-dot menu stays gray. Both hover and open states lay a light **ring** over the
  top instead of relying on a fixed color, so it sits correctly on either background.
- **WPF trap this row depends on**: background, border, and shadow are set in `Border.Style`,
  **not** as attributes on the `Border`. A locally set attribute outranks any style trigger —
  writing `Background="#1E1F24"` directly makes the hover state inert, without anything
  reporting an error. (This does **not** apply inside a `ControlTemplate` — there, a value on the
  template element is a template property, and `ControlTemplate.Triggers` sit above it.)
- **Rounded corners are not inherited.** A `Border` does not clip its child to its own
  `CornerRadius`. The tint surface therefore carries its own rounding (**7**) — 7 and not 8,
  because the outer border is 1 px thick.
- **Spacing between rows comes from exactly one place**, the row's `Border.Margin`
  (`20,3,10,3` — left/right aligned with the filter bar, top/bottom half the row gap). The
  `ItemTemplate` in `AccountsView.xaml` deliberately adds no second margin.

## The account dialog

The dialog has tabs and a fixed size, **1000 × 752** — not chosen, but computed from the
[modal-fit rule](#window-and-modals), not from content.

| Tab | Content |
|---|---|
| ACCOUNT | Battletag (display only), email, password, the **game/region matrix**, notes |
| HOTS | Penalty games and placement status side by side, the rank grid below them — with the embedded hero picker underneath |
| OW2 · WOW · DIA | each a dashed box, "Nothing to configure yet." |

**Games carry short labels here** (`GameVisuals.ShortLabelFor`), and that is not a stylistic
choice: five tabs sit side by side, and "HEROES OF THE STORM" alone would need more room than the
other four combined. The labels live in `GameVisuals` next to icon, color, and full name — a
second derivation inside the dialog would drift from it. The same labels name the rows of the
matrix below.

#### The game/region matrix

Four game rows × three region columns, in the ACCOUNT tab. It replaced two separate rows of
toggles — four games under the notes and three regions beside the password — because since
22.08.2026 the regions hang on the **game** and no longer on the account.

```
                EUROPE   AMERICAS   ASIA        column heads: 88 each
 [icon] HOTS     [EU]      [AM]     [AS]        toggles 60 × 34, centred
 [icon] OW2      [EU]      [ ]      [ ]
 [icon] WOW      [EU]      [ ]      [ ]
 [icon] DIA      [ ]       [ ]      [ ]
```

| Element | Points |
|---|---|
| Label column (icon 28–32 + short label) | 86 |
| Region column, three of them | 88 each |
| **Total width** | **350** |
| Head row | 22 |
| Game row, four of them | 46 each |
| **Total height**, plus the hint line below | **206** + ~30 |

- **The column heads carry the region name once**, rather than a tooltip per cell saying the same
  thing twelve times over. The cells themselves carry the abbreviation `EU`/`AM`/`AS` — the same
  two letters as the filter bar and the account row.
- **Heroes of the Storm sits in the first row.** It is the only game with a tab of its own, and
  the regions ticked in that row are exactly what the region bar in the HOTS tab switches
  between. Unticking the last one closes that tab.
- **A row with no tick is a game that is not played.** There is no separate "plays this game"
  checkbox any more, and that is the point of the matrix: the pair of the two could contradict
  itself, and a game ticked without a region was an account that no row would show.
- **The three region names now have the same width budget** — 88 points at font size 11 rather
  than the old 76/86/60 on toggles. The limits are tabulated at the top of `en.yaml`.

**Where the height went**: the matrix costs about 236 points where the two toggle rows cost 100.
The ACCOUNT tab scrolls as a whole, so nothing had to be re-budgeted — but it is no longer free
of scrolling on a short window.

**Fixed tab width, 92** — same reasoning as the main window's tab bar: the blue underline
measures button width, not text width. 92 rather than the main window's 100, because five tabs
sit side by side here instead of two.

- **The fixed size was the actual lever.** Under `SizeToContent="Height"`, two workarounds were
  forced: the rank picker had to be an overlay, and the hero picker its own window — 90 circles
  would otherwise tear the dialog open on load. Both reasons are gone; whatever doesn't fit now
  scrolls inside its tab.
- **Every tab scrolls as a whole**, HotS included. The content budget is
  `752 − 60 (title) − 52 (tab bar) − 80 (footer) = 560` points; anything beyond that runs under a
  `ScrollViewer`. Height is therefore no longer a running arithmetic chain — adding something to
  the dialog only means checking whether the tab still looks sensible without scrolling, not
  weighing points against each other. How this came about, and what it costs, is below.
- **In the HotS tab, penalty games and placement stand above the rank, not beside it.** Both are
  one-line answers — a counter and a checkbox — while the rank is a block of 28 medals. Set
  next to that block, their two labels floated at the top edge of a surface four times their
  height, and the eye had to climb back up to learn what the widgets below them meant. Stacked,
  they read as one line and the rank grid gets the full tab width to itself. They stay side by
  side **with each other**: a second row would only cost height the hero picker underneath needs.

  ```
  before                                current
  +-------+---------+-----------+       +---------+-----------+
  | RANK  | PENALTY | PLACEMENT |       | PENALTY | PLACEMENT |
  | ##### |   /!\3  | [x] open  |       |   /!\3  | [x] open  |
  | ##### |         |           |       +---------+-----------+
  +-------+---------+-----------+       | RANK                |
                                        | #####   #####       |
                                        +---------------------+
  ```

### The HotS tab scrolls as a whole

The entire tab runs under one `ScrollViewer` — region bar, rank block, and hero picker together.

```
before                                    current
+- HotS tab (Grid) -----------+           +- ScrollViewer --------------+
|  Region bar          fixed  |           | +- Grid --------------------+ |
|  Rank block          fixed  |           | |  Region bar               | |
| +- HeroPickerView --------+ |           | |  Rank block               | |
| |  Search + chips   fixed | |           | | +- HeroPickerView ------+ | |
| | +- ScrollViewer -------+ | |           | | |  Search + chips      | | |
| | |  Grid      SCROLLS  | | |           | | |  Grid, full height   | | |
| | +----------------------+ | |           | | +----------------------+ | |
| +--------------------------+ |           | +--------------------------+ |
+-------------------------------+           +---------- SCROLLS ----------+
```

Two measured reasons:

1. **The grid used to be clamped to whatever height remained in the tab.** With the dialog
   clamped to 752, the content had 560 points; after the region bar (40), the rank block (266),
   and the hero picker's own header (96), exactly **158** points remained — two rows of hero
   circles. Now the grid takes its natural height, and the tab scrolls across it.
2. **There were two overlapping scroll regions.** The mouse wheel hit one or the other depending
   on pointer position — the most common misfire was turning the wheel over the rank block and
   finding that nothing happens.

- **The inner `ScrollViewer` is disabled, not removed.** In the standalone `HeroPicker.xaml`
  window it is still the right one: there the header with the search field and counter should
  stay in place while the grid scrolls. Which one applies is decided by
  `HeroPickerViewModel.GridScrollBarVisibility` — on the `Embedded` axis that `ChromeVisibility`
  already hangs on, **not** as a second switch.
- **`Disabled`, not `Hidden` — and that is not a matter of taste.** `Hidden` still lets the
  `ScrollViewer` scroll and only hides the bar — the grid would stay clamped to tab height with a
  second, invisible scroll region. `Disabled` passes the height constraint through to the child;
  under the outer `ScrollViewer` that constraint is unbounded, so the grid grows to its full
  natural height.
- **`Disabled` alone is not enough — a `ScrollViewer` still processes the mouse wheel even when
  it cannot scroll.** `OnMouseWheel` sets `Handled = true` regardless of whether an offset
  actually changed; `Disabled` turns off *scrolling*, not *handling*. The first attempt at this
  worked only halfway: the wheel scrolled over the region bar and rank block, and stayed inert
  over the hero grid.

  ```
  Wheel over the rank block            Wheel over the heroes (before the fix)
  --------------------------           ---------------------------------------
   Pointer                              Pointer
     | bubbles up                          | bubbles up
     v                                     v
   (nothing catches it)                  inner ScrollViewer
     |                                      | Handled = true, doesn't scroll
     v                                      x  STOPS HERE
   outer ScrollViewer --> scrolls        (outer never sees it)
  ```

  `HeroPickerView.GridScroller_PreviewMouseWheel` catches it in the **preview** pass instead —
  which tunnels top-down and thus arrives before the `ScrollViewer` does — and re-raises it as a
  bubbling `MouseWheelEvent` with `Source = this`. The inner `ScrollViewer` sits below the
  `UserControl` and is thereby out of the way.

  **The handler sits in the surface, not the host.** A `PreviewMouseWheel` on the dialog's outer
  `ScrollViewer` would have been shorter to write, but would have put the obligation on every
  future host — anyone embedding the surface would inherit the bug without knowing it existed.
- **Row 2 of the tab is set to `Auto`, no longer `*`.** Under a `ScrollViewer` a star row would be
  meaningless anyway — a `Grid` resolves star rows to their desired height under unconstrained
  measurement. `Auto` states that explicitly instead of relying on it. The star row **inside**
  `HeroPickerView` stays as-is, though: it still has to fill the remainder in the standalone
  window.
- **The two bulk-select buttons sit in the header, directly behind the search field**, and the
  role chips have their row to themselves. Both buttons act on what is currently **visible**, and
  what is visible is decided one element to their left; a row further down, next to the chips,
  that connection had to be read out of the label instead.

  **Declaration order in the header `DockPanel` is load-bearing.** It serves its children in the
  order they stand, so the left-docked group — title, search field, both buttons — claims its
  width before the counter on the right does, and the counter is what yields at a narrow window.
  That is deliberate: a clipped action is a broken button, a clipped counter is a missing number.
  Measured at the tighter of the two hosts, the account dialog at 1000 points: search field and
  buttons take about 530 of the roughly 960 available, the counter needs about 200.

  **What the move removed** is the trap the chip row used to carry. The buttons used to sit in
  that row, declared *before* the chips precisely because a `DockPanel` serves in declaration
  order — with the chips first, they took everything and clipped the buttons. On 22.08.2026 the
  account dialog showed "selec" instead of "select all", because "Melee Assassin" reads
  "Nahkampf-Assassine" in German. The chips can now take the full width and wrap via `WrapPanel`;
  there is nothing left beside them to squeeze.
- **The price: the search field and role chips scroll away with everything else.** They belong to
  the surface, and the surface scrolls. Someone who searches for a hero and then scrolls down no
  longer sees the search field. In the standalone `HeroPicker.xaml` window it stays fixed —
  there only the grid scrolls.
- **The ACCOUNT tab always worked this way.** The HotS tab was the exception, not the rule; after
  this change both are built alike.

### Main window tabs

The main window has two tabs, **ACCOUNTS** and **SETTINGS**, top left beside the logo.

- **One `DataTemplate` per tab is mandatory.** `CurrentView` carries a ViewModel, and the
  `ContentControl` looks up the matching view in `App.xaml`. Missing that line, the window shows
  the ViewModel's **class name** instead — no compiler error, no binding warning.
- **Deselecting does not exist**, same as with the game filter and for the same reason. A
  `ToggleButton` unchecks itself on click *before* the binding writes the new value;
  `MainViewModel.NotifyTabs` makes it re-read the source and snaps it back.
- **They carry no template of their own** — they inherit `BattlenetIconButtonTheme`, the same
  style that draws the four game icons in the filter bar: a blue underline `#1A73E8` across the
  full button width, shown on hover even when the tab isn't the active one. Only what an image
  doesn't need and text does is added on top: font color and size. **Without the color, WPF
  renders the text black** on dark gray, because the style is built for images and sets none.
- **Fixed width, 160, for both tabs**: the underline bar measures button width, not text width.
  Without it, the bar would run longer under `ACCOUNTS` than under `SETTINGS`.
- **Font size 22.** The width depends on it and is **measured, not estimated**: `ACCOUNTS`
  measures exactly **114.0** points in Segoe UI DemiBold at size 22
  (`System.Windows.Media.FormattedText`), `SETTINGS` measures **98.8**. With roughly 23 points of
  margin per side, that yields the 160. Vertically it fits without any rework: the text line is
  29.3 tall, the button 48, the row 70.
- **They sit vertically centered, on the logo's axis.** The row is 70 tall, the logo 50 and thus
  spans 10…60 (center 35); bottom-aligned at 48 px tall, the tabs would sit at 18…66 (center 42)
  — seven points too low. Centered, they sit at 11…59, and their bottom edge happens to line up
  with the logo's. Anyone changing tab height recomputes this.
- **The settings tab is built only on first visit** — its constructor scans the usual install
  locations, which is cheap but not a reason to do it on every start.
- **The right-hand end of the same row carries the version chip**, 20 from the right edge, where
  the filter bar below ends too (1340 window width, 1300 after its `DockPanel` margin, 20 per
  side). It is 26 tall with a corner radius of 13 at font size 12 — against the theme's 16, because
  this sits on the logo's axis and is not a main action; at 16 it would stand taller than
  everything around it and pull the eye off the tab bar. The 30-point title bar one row up was
  never an option — the three window buttons already sit there.
- **The chip changes its width, never its height.** `v1.0.0` in the quiet case, `v1.0.0 → 1.0.1`
  when something is offered, plus a trailer (`· 42 %`, `· Update failed`) while something is
  happening. Before August 2026 this was a button whose single label swapped between five wordings
  — so its left edge jumped at every step of an installation, which is the one place a display
  should stand still.
- **The panel under the chip is 330 wide and starts 74 from the top.** Title bar 30, plus the
  26-point chip centred in the 70-point row, puts the chip at 52…78 — the panel begins six points
  below it. It is an overlay inside the window and not a `Popup`: a popup opens a window of its own,
  which in a `WindowStyle="None"` window is placed against the screen rather than against the
  frame. Whoever changes the two row heights recalculates the 74.
- **Left of the version sits the chip for a running game client**, and its menu is the second
  overlay of this window — same 74 from the top, same 10 from the right, **360** wide. It hangs off
  the window's right edge and not off its own chip: the version chip beside it is as wide as its
  text, so any offset computed here would stop being true at a version with more digits. At 360 it
  covers the chip regardless — that one ends about 105 from the right edge and is 90 wide.
- **Its 360 is the start menu's 360, and for the same reason.** Two entries with an icon (22), a
  gap (10) and margins (32) leave the subtitle `surface − 64`. Measured with `FormattedText`,
  Segoe UI 11: German **280.7**, French 278.4, Spanish 278.1, English 238.9. At 300 wide the 236
  available carried none of them. The hints additionally wrap instead of truncating — a cut-off
  subtitle says nothing about being cut off.

### The settings tab

**700 points, and that is the whole budget**: the window is 800 and not resizable, the title bar
takes 30 and the header row 70.

| Element | Height |
|---|---|
| Card title row | 40 |
| A row | 52 (minimum) |
| Separator | 1 |
| Gap between cards | 14 |
| Outer margin | 10 top, 20 bottom |
| **Three cards, nine rows** | **≈ 620 of 700** |

- **Roughly 80 points spare, and they are the language reserve.** The numbers above are computed
  from the markup, not measured on screen; a state text is one line in German and can be two in
  French. The `ScrollViewer` stays for exactly that case — it costs nothing while nothing
  overflows.
- **Every row is a `DockPanel`**: the label docked left at a fixed **240**, the rest filling. 240 is
  measured against the longest label across the four languages plus the info sign and its gap — a
  label that wraps pushes its own row out of shape.
- **The path box stays 700 wide.** 240 + 700 + 10 + 30 for the scan button is 980, and a card has
  1264 inside the outer margin and its own padding.
- **The two state lines under the path take height only when they say something.** An empty
  `TextBlock` still reserves a whole line, so without the `DataTrigger` on `Value=""` they would
  cost 34 points permanently — in the state that is the normal one. `Visibility` is set in the
  style **alone**: an attribute on the element beats the trigger, silently.
- **What made the budget work is not the cards but what left the screen**: four explanation
  paragraphs at 700 wide, worth around 340 points. They now hang on the info signs — see
  [ui-conventions.md](ui-conventions.md#an-explanation-is-read-once-a-state-is-read-every-time).
