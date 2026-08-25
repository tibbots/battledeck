# Reading values out of the running game

These are the procedures behind `GameSession`'s read steps — how rank, heroes, currencies, and
loot-chest counts actually get pulled out of a running Heroes of the Storm client. Each of them
has its own stop condition, and "no idea" is a valid answer that writes nothing. For which values
get persisted where, and under what guard against a bad reading, see
[game-integration.md](game-integration.md) — it covers the write guards separately from the
extraction described here, and [architecture.md](architecture.md) holds the persistence itself.

## Two ways to wait — and neither replaces the other

| Tool | Waits for | Used for |
|---|---|---|
| `WaitForStableArea` | until nothing changes anymore in an area | after a click that redraws something |
| `Retry` / `RetryAsync` | until a measurement finds something | when the target has yet to appear |

**Stillness is no proof.** A loading spinner turns quietly in place, and a mask still being built
is already there but not yet at its final position. That's why both tools exist.

This was measured directly, twice on the same day, in two different places. Before login, only
`WaitForScreen(Login)` used to stand guard — a brightness measurement on the topmost strip. It
reported "Login" 16 seconds after the window appeared, because `ScreenOf` throws everything that
is neither `Menu` nor `HeroSelect` into that bucket. `LocateLogin` then took a single capture —
and the loading spinner was still spinning in it. Result: abort with "Login form … not
recognised", even though the problem was only that it looked too early.

The same mistake showed up a second time: `rank.area is stable (deviation 2.83)` against a
threshold of 3.0 — the medallion was still being drawn, and the crop taken right after sat at a
distance of 0.093 instead of the 0.019 from a run half an hour earlier. Nothing was recognized.
This is one of the reasons the rank screen is no longer read by image comparison at all — see
[What is read from where](#what-is-read-from-where) below for how rank is read instead.

- **The measuring function must not log anything.** It runs on a one-second cadence; a warning
  per attempt would bury the actual message. `LoginLocator.Find` hands its reason back as an
  `out reason` parameter instead of logging it — reported once, after the time budget runs out,
  together with the capture.
- **Every capture costs** roughly 20 MB at 3440×1440 **and brings the game window to the front**
  (`GameWindow.Capture` calls `BringToFront`). The cadence is therefore 1.5 s, the same as in
  `WaitForScreen`. Anyone doing something else on the machine during a wait loop will notice.
- **Retried where a miss actually costs something** — six sites, four different mechanisms: the
  **login mask** (nothing proceeds without it), the **expected hero count** (the entire list's
  fate as replace-vs-merge hangs on that one number), **rank** (`ProfileReader`, until the line
  `Sturmliga` appears, at most 8 s per opening), a **card page** (at most three times, aborting as
  soon as all ten slots are readable), and the **screen check** — one mechanism used at two sites,
  `GameSession.SignOut` and `GameSession.AttachToRunning` (`SettledScreen`, up to three times a
  second apart — added 24.08.2026 after the identical screen read as hero select twice and main
  menu once in a row). Deliberately **not** retried: `HeaderReader` — there, an unread
  value simply stays as it was. Blindly retrying everywhere would only report real failures later
  and slower.
- **Where stillness isn't measurable at all, don't wait for stillness.** A `WaitForStableArea`
  used to sit over the collection's card grid. Its measurement box necessarily spans the area
  between the two name strips, and the moving background shows through there between the cards:
  the area never went still in a single run, every page burned the full 20 seconds, and as a
  result no hero got read at all. The right question wasn't "is something still moving" but "do
  I have the page full". So that this is recognizable in the log, `WaitForStableArea`'s warning
  now names both the last measured value **and** the threshold.

## What is read from where

| Value | Screen | Method |
|---|---|---|
| Rank (tier + division) | Profile overlay, PROGRESS block | OCR, `ProfileReader` |
| Rank points inside the division | Tooltip on the rank medal | hover, then OCR, `ProfileReader` |
| Placement games pending | same block | the word `Platzierung` stands there instead of a tier |
| Account level | same block | OCR, `ProfileReader` |
| Battletag | Header of the overlay | OCR — cross-check **and** source on rename |
| Heroes | `SAMMLUNG` → `Helden` (Collection → Heroes sub-tab) | OCR per card, `CollectionReader` |
| Gold, shards, gems | Header bar (everywhere) | OCR, `HeaderReader` |
| Unopened loot chests | Badge on the `BEUTE` tab (Loot) | OCR across the whole bar, `HeaderReader` |

**The points inside the division stand nowhere on the overlay.** They come up as a tooltip while
the pointer rests on the medal, and on nothing else — measured on 24.08.2026 at BUBU#23555, where a
point 113 further right, on the words `Gold 3` themselves, brought up nothing at all. The tooltip
hangs on the **medal**, not on the pointer: hovering 27 points further left and 22 lower put the box
in exactly the same place.

It reads three lines — the rank, then `497 Rangpunkte`, then `503 Rangpunkte für Aufstieg
erforderlich`. **The game names what is missing, not the size of the division**, so the bound is the
sum of the two; assuming 1000 would be entering a number the game never said. Which line is which
follows from their order and not from a second word — both carry the same one, the current standing
above, what is still missing below. Sorted by Y, therefore, and not by how they came out of
recognition.

The read costs a second capture and happens **after** the rank is already in hand, so a tooltip that
never appears costs the points and not the reading. Anything unclear — a line count other than two,
a bound that would not be a bound — writes nothing: this value ends up as a ring somebody reads at a
glance, and a plausible wrong number is worse there than a gap.

**Only German is measured.** `GameVocabulary.RankPointsWord` carries the word for all five
variants, four of them marked `NOT MEASURED`; on those clients the tooltip is not read and the
points stay unwritten. That is the intended failure — a wrong word costs nothing, a guessed number
would be believed.

**Rank is read via OCR now, not image comparison.** The reason image comparison existed at all
was the division: on the rank screen it sits as a decorative glyph on the medallion disc, not as
a character. In the profile overlay, `Sturmliga` stands instead, with `Silber 3` underneath as
plain text — tier and division on one line. That removes the reason for image comparison.

Three measured reasons why the old approach had to go, all encountered together:

1. **The medallion disc carries an animated facet pattern.** The distance to the *same* rank
   fluctuated across three runs between 0.019 and over 0.3 — against a threshold of 0.075.
   Raising the threshold didn't help: `silver-2` and `silver-3` differ only in the digit, and
   that digit is small against the area that's flickering.
2. **A rank that wasn't already in `data.yaml` was fundamentally unreadable.** The learned set
   trained on whatever the human had entered by hand — for a fresh account there was nothing to
   learn from, so reading produced nothing there at all.
3. **Nothing checked whether the captured screen was actually the rank screen.** In one run, a
   hero-select screen got compared against a medallion. The Battletag now sits in the same
   overlay and is checked **before** evaluating; if it doesn't match, nothing is adopted.

**The path there is a gesture**, not a menu path: right-click on the profile picture top right,
then "View Profile". Both are calibration points, both measured at two resolutions. Closed via
the X — not Esc; whether that key works there hasn't been checked.

**The whole block is read, not two narrow crops.** The lines carry their own labels, so
`ValueUnder` finds the value above each label instead of via a fixed coordinate: the aligned word
below that starts at the same edge. The edge condition is necessary — a circle sits to the left
with the same number repeated inside it. The block shifts down by a good dozen points depending
on content; a narrow crop wouldn't follow that.

**Matched by word, not by whole line — since 25.08.2026.** A profile read consistently failed
with "the line 'Sturmliga' did not appear", even though it read cleanly on every attempt: the OCR
had merged the rank medal's division digit into the same line as the rank text, `2 Bronze 2`,
whose line box starts at the medal rather than the text — well outside the edge tolerance. Looking
at the aligned *word* instead of the aligned *line* finds the value regardless of how the
recognizer happened to group that capture, and drops the medal digit rather than tripping over it.

**It stays language-dependent, but cheaper.** Instead of an image asset per rank there is now a
table of seven words (Bronze, Silver, Gold, Platinum, Diamond, Master, Grand Master) that lives
in `GameVocabulary` — together with the labels `Sturmliga` and `Spielerstufe` and the word
`Platzierung`. If the client is set to a language the dictionary doesn't cover, nothing is
recognized and nothing written. `~/.smurftown/references/` is no longer read or written — the
folder may be removed, the app won't recreate it.

## Opening loot chests

Mode 4 of the start menu. **The whole flow hangs on one key** — three presses of the space bar
per chest:

```
LOOT ──► carousel, center = next stocked chest type
   │
   │  Space ──► chest opens, four hidden hexagons
   │  Space ──► all four revealed at once
   │  Space ──► "Accept", back, counter one less
   ▼
next chest — the carousel advances on its own
```

- **This used to be six clicks on five calibrated points** (Open, four hexagons individually,
  Accept). They're gone without replacement, and that is the actual win: 22 px right of "Accept"
  sits **"Retry: 250 gold"**. As long as clicks landed there, a drifted anchor was a way to burn
  the account's gold. The space bar selects "Accept" on its own — cross-checked across two runs
  with an unchanged gold balance. Whoever puts coordinates back here reclaims that danger.
- **The space bar needs a scancode**, not just the virtual key code. `Enter` and `Escape` go to
  the login mask, i.e. an ordinary input field; the space bar goes to a game scene, and that
  evaluates the scancode. `InputSender.Space` is therefore the only key that uses
  `MapVirtualKey`. Measured, not assumed.
- **The counter remains the stop condition.** After every chest the badge is re-read; if it
  hasn't dropped, stop instead of pressing on. Since the clicks fell away, the cost is only time
  — a loop that achieves nothing should still end.
- **Two attempts per chest, then abort.** The game occasionally swallows a keypress; a second
  pass catches up and brings a shifted flow back into step. How many chests actually got opened
  is answered afterward by the counter, not by counting keypresses.
- **A round opens one chest, at most two — a bigger drop is a misread, not progress.** The
  per-chest retry above means one round can lower the counter by at most `Attempts` (2).
  `LootOpener` checks that after every round and aborts with a diagnostic capture rather than
  crediting a larger drop as chests opened. Measured 23.08.2026: an unmagnified badge read came
  back as 0, the 44 chests still standing were booked as opened in that single round, and the
  run reported "65 chests opened" while only 21 had actually been.
- **While a chest is opening, the navigation bar disappears** along with the badge. Reading at
  that moment returns "no BEUTE found" and thus `null` — which here would mean "abort", although
  it was only looked at too early. The counter is therefore read up to three times at 800 ms
  intervals. Same trap as with the login mask.
- **The number is the sum across all chest types.** The carousel shows it per type
  individually, the badge combined: "Rare Chest 1" plus "Loot Chest 28" at a badge reading 29.
- **The word is searched for, not a fixed box.** The bar reflows — if `SAMMLUNG` gets its own
  badge, and that's exactly what happens after opening a chest, `BEUTE` shifts right. Observed
  twice in the same run: from 584 to 595, while `SAMMLUNG`'s badge grew from 9 to 14.
  `HeaderReader.CountLootChestsAsync` therefore looks for the line `BEUTE` and, next to it, the
  next pure-digit line within 120 points. `SAMMLUNG`'s badge drops out of consideration because
  it sits to the **left** of `BEUTE`.
- **The badge is read magnified — at magnification 1 it was silently absent, not misread.**
  `CountLootChestsAsync` reads the capture at `BadgeUpscale` (3), escalating to
  `BadgeUpscaleRetry` (4) on the same capture if that still finds nothing — the same
  retry-at-a-different-magnification trick used for cards (see
  [Per-cell OCR and the second attempt](#per-cell-ocr-and-the-second-attempt)). Measured
  23.08.2026, German client, 3440×1440, an account with 44 unopened chests, nav-bar crop
  `{x:0, y:0, width:1300, height:80}`:

  | Magnification | Result |
  |---|---|
  | 1 | absent from the result — not misread, simply not returned |
  | 2 | absent |
  | 3 | `X=737 W=24 H=15` → `44` |
  | 4 | `X=736 W=24 H=14` → `44` |

  The `SAMMLUNG` badge in the same reading (`129`, three digits, 33 points wide) is found
  already at magnification 1; the loot badge (two digits, 24 points wide) is not — two narrow
  digits sit right on the edge of what unmagnified OCR resolves, three do not. This was the
  only reading site in `HeaderReader` that skipped magnification.
- **From one to nine the number cannot be read at all, and no magnification changes that.**
  `Windows.Media.Ocr` does not return a character standing on its own. Measured 23.08.2026 on a
  badge reading `9`: absent at magnifications 1, 2, 3, 4, 5, 6 and 12, absent from a crop of the
  box alone, absent from a crop of the box with its border — and absent from a crop holding
  `BEUTE`, the badge and `REPLAYS` at once, which returns **the two words and skips the digit
  between them**. The carousel on the loot page shows the same behaviour from the other side:
  `Beutetruhe 8` reads as label plus number, while the `8` cropped on its own reads as nothing.
  What isolates the badge digit is its bright box.

  Ten readings in a row on that badge returned the number three times. So the three attempts in
  `LootOpener.CountWithRetry` usually get it — and when they do not, the answer below is what
  keeps that from becoming a zero.
- **Whether a badge is there at all is decided on pixels, not on text.** `HasBadgeBox` counts the
  bright violet of the badge border in the window between the end of the word and the start of
  the next recognised line, at most `badgeReach` beyond it. Measured: **379** such pixels of 6640
  with a badge showing `9`, and **0** of 3680 with none — the latter with `BEUTE` as the active
  tab, glow included, and the divider inside the window. The threshold sits at one percent.

  **The window closes at the next line, and that is what makes it survive the reflow.** With the
  badge gone, `REPLAYS` moved from 799 to 762 in the same measurement. Both edges come out of the
  same reading, so the window follows.
- **Four answers, and the two in the middle are the point**: a **number**, when it was read —
  **"some, but unread"**, when no digits came back and the badge box is there — **0**, when
  neither is there — and **null**, when not even the word `BEUTE` was read. In that last case the
  screen is a different one, and "no chests" would be a claim we don't actually have.

  **"Some, but unread" used to vanish between here and `data.yaml`.** `HeaderReader.ReadAsync`
  collapsed the answer down to `LootCount.Number` alone before 25.08.2026, and that is `null` for
  "some, but unread" exactly as it is for "not read at all" — indistinguishable once it reached
  `HotsReadout`, which correctly refuses to overwrite on `null`. A stored `0` from an earlier
  read, honest at the time, then kept asserting itself for as long as the count stayed in the
  1-9 range: single-digit chests read as present via the pixel check, and were never reflected
  anywhere. `HeaderReading` now carries `ChestsPresent` alongside the number, and `HotsReadout`
  clears a stored `0` back to `null` the moment it is known to be wrong - not to a guessed digit,
  which would trade one wrong number for another, but to the honest "not currently known".
- **Opened before reading.** A chest drops shards, gold, and occasionally a hero. Reading first
  would leave the previous state in `data.yaml` — and that state is wrong from the very first
  chest opened onward.
- **The counter costs nothing.** It sits in the navigation bar, which is present on every screen;
  `HeaderReader` reads it from the same capture as gold, shards, and gems.

## Heroes via the collection, not via hero select

Hero select shows all 90 heroes on **one** screen — looks like the shorter path and isn't. There
a hero is just a tile, and "owned" means "brighter". Reading that requires knowing which tile
belongs to whom, and that ordering couldn't be reliably derived: a cross-check against an account
with 24 entered heroes hit 12. Four further tile-recognition approaches failed too (row/column
projection, contrast filter with connected components, ring score, ring score with
re-centering).

The collection writes the name as **text** under every card, can be filtered to "Owned Heroes",
and can be sorted alphabetically. There is nothing to guess there. The price is paging — 5 cards
per row, 2 rows per screen, mouse wheel.

### Paging

**One notch is one row, and paging advances by exactly one** (`scrollNotches: 1`). It used to be
3, on the reasoning that three notches were roughly one page. But only two rows are visible —
every page therefore silently skipped exactly one row. On an account with 29 heroes (6 rows),
row 3 went missing entirely as a result: of the 29, 23 arrived, and the six missing were the five
from row 3 plus one single unread slot. It only surfaced because the expected count stands right
next to it. With one notch, **two consecutive pages overlap by one row** — a slot the OCR misses
once gets a second chance on the neighboring page.

### Expected count: replace or merge

**The expected count no longer decides *whether* to write, but *how*.** Hovering "All" in the
sidebar, a tooltip names `32/89 owned`.

| Reading | What happens | Why |
|---|---|---|
| complete | **replace** | only a full reading is allowed to take something away — this way it also corrects a wrong manual entry |
| incomplete | **merge** | whatever was read gets added; nothing gets deleted |

An incomplete reading merges rather than being discarded, and that is deliberate: **in Heroes of
the Storm heroes cannot be lost, ownership only grows.** Merging can therefore never cost data,
whereas discarding 31 of 32 successfully read cards because a single tile was unreadable does
cost data.

The price stands next to it: a wrong manual entry survives an incomplete reading and only
disappears on the next complete run. That's still worth a warning even though the merge
contributed something — the toast then reads `Heroes merged instead of replaced`.

- **`Ordered()` appends unknown IDs back at the end.** `HotsHeroCatalog.Resolve` drops them, and
  that would be a deletion here — a `data.yaml` written by a newer app version can contain heroes
  this build doesn't know about. Without this line, the merge would break its own promise before
  it ran for the first time.

### Compound cards (Cho'gall)

- **What's compared are CARDS, not heroes.** The expected count counts cards, and `Cho'gall` is
  **one** card with **two** catalog entries (`cho` and `gall` — separate ID, separate portrait,
  separate role: Cho is a tank, Gall a ranged assassin). An account owning every hero therefore
  reports **`89`** acquired cards against **`90`** catalog entries. Comparing heroes against the
  expected count never reaches "complete" that way — measured across three consecutive runs.
- **Compound cards are listed in `HeroNameMatcher.Compound`**, and `Match` therefore returns a
  *list*. Without that entry, Cho'gall is fundamentally unreadable: `chogall` has a relative
  distance of **1.33** to `cho` and **0.75** to `gall`, both far above the **0.34** matching
  threshold. In the log this showed up as `'Cho'gall' matched no hero` — six times in a row,
  even though the text was read cleanly every time. If either ID is missing from the catalog,
  the build **warns** instead of letting the compound card silently drop out.

### Per-cell OCR and the second attempt

- **Read cell by cell, not the whole card field at once.** Measured: on large crops, OCR
  occasionally returns nothing at all, without an error; on small crops it's error-free. This is
  not fine-tuning, it's the difference between "reads" and "doesn't read".
- **If a slot returns nothing, the same crop of the same capture is read again at scale 3.** A
  different magnification rasterizes the font differently and is thus a genuine second attempt;
  the same magnification on the same pixels would necessarily give the same answer. It costs
  **no** additional capture, and the capture is the expensive part.
- **Slots that stayed empty on every attempt are named at the end** (`INF`), and the first three
  land as crops under `~/.smurftown/shots/`. The reason is a measured case: **Rehgar** stayed
  empty across six captures — three times on page 12, three times on the overlapping page 13 —
  while its neighbors Raynor and Rexxar were read cleanly every time. Against an otherwise
  roughly **15 %** miss rate per capture, that is not chance, it's something about this specific
  card; the log doesn't say what. The crop shows it: if text was there, it's the recognition; if
  background was there, the box has drifted.

### The Li-Ming finding

Four saved crops from two runs and two accounts showed **the same card**, at four different grid
positions:

| Crop | Page / Row / Column | Content |
|---|---|---|
| `p3-2-1` | 3 / 2 / 1 | Li-Ming |
| `p4-1-1` | 4 / 1 / 1 | Li-Ming |
| `p4-2-3` | 4 / 2 / 3 | Li-Ming |
| `p5-1-3` | 5 / 1 / 3 | Li-Ming |

That rules out two things. **Calibration** — the box sits correctly, the name is complete and
sharp inside it. And **the mouse cursor**: it's set to the exact window center on every scroll
(`session.ScrollAt(Width / 2, Height / 2, …)`) and stays there during the capture, which at
3440×1440 lands in the name strip of column 3 row 1 — an obvious suspicion that the table
disproves: the failure follows the **content**, not the position. If it sat with the cursor, it
would always hit the same grid position regardless of the changing content.

What remains is OCR failing specifically on this one card. Visible in the crops: the name strip
is semi-transparent, and Li-Ming's bright robe shows through exactly where white text sits. That
is a hypothesis with image evidence, not proof — Rehgar failed the same way and isn't bright.

### Missed cells

- **The empty tail at the end of the list doesn't count as missed.** 89 heroes at 5 columns fill
  row 18 only four-fifths full, and the last paging step leaves half a page empty regardless. A
  slot only counts as missed once a read slot follows it further along in reading order.
- **Overlap catches most, but not all.** In one measured run, **12** slots were missed, **10** of
  them recovered on the neighboring page.
- **Read errors are expected.** Recognition returns `Arth"` instead of `Arthas` and
  `Funke/chen` instead of `Funkelchen`. Against 90 known candidates, the nearest neighbor still
  matches (`HeroNameMatcher`, Levenshtein, threshold one-third of the name length). Checked on
  one complete page: **10 of 10**.
- **Compared against the German name.** The client runs on `deDE`. On a client set to any other
  language the matching finds nothing — intentional, and it's immediately obvious because then
  not a single hero gets recognized. A silent fallback to the English name would instead deliver
  a handful of random hits. The same holds for the tier words in `ProfileReader` — except there
  it's a seven-entry dictionary affected, not a learned image asset that would have to be rebuilt
  from scratch.
