# Calibration: Anchors Instead of Coordinates

Where something sits in the game window depends on the window size. Fixed coordinates
would therefore be correct for exactly one resolution only — and clicking wrong costs
money here: 22 points next to "Accept" in the loot chest sits **"Try again: 250
gold"**.

## The Model

Every point of the calibration (`Battledeck/Backend/Automation/screen-map.yaml`) names

1. an **anchor** — `topLeft`, `topCenter`, `topRight`, `bottomLeft`, ... —, and
2. a **distance** from it, in points of the reference size **3440x1440**.

At runtime the distance is scaled by **`window height / 1440`**. The width does **not**
enter into the calculation; it only decides which edge an element sticks to.

This is measured, not assumed:

| | 3440x1440 | 2560x1080 | 1920x1080 |
|---|---|---|---|
| Button spacing, bottom right | 66 | 49 | 49 |
| Width of the region selector | 308 | 231 | 231 |
| Width of a login field | 556 | 360 | 360 |

**At the same height, nothing changes regardless of how wide the window is.**
Horizontally, every element sticks to an edge or to the centre, and which anchor
applies has to be measured per element: the top bar hangs left, the content of the
collection at the centre, the currencies right.

## The Rule: Two Resolutions

**An anchor that only holds at one resolution is not an anchor, it is a coordinate.**
Anyone entering a new point measures it again at a second resolution and checks
whether the measured value matches the prediction.

Prediction: `value(new) = value(3440x1440) x (height(new) / 1440)`, computed **from the
anchor** — for `topRight` that means the distance to the right edge, not the absolute x
coordinate.

## How to Get a Second Resolution

This is the part that costs time if you don't already know it. **Three approaches
look viable, two of them are not.**

### What Does Not Work

**Shrinking the window via `SetWindowPos`.** The game does not recompute its layout —
it keeps rendering at the old size, and the window merely **crops** it. At first
glance this looks like a smaller resolution, and it is actually a cropped large one.

**Choosing a different resolution in fullscreen.** On a machine whose desktop sits at
3440x1440, the game falls back to that: the client area stays 3440x1440, the field
width stays 554, nothing changes at all. The option sits right there in the settings
and still has no effect.

### What Works

**Windowed mode.** Options -> Graphics -> *Display Mode* -> `Windowed`.

- The game picks the window size **itself**; it does not even offer a resolution list
  there ("No valid resolutions found"). On a 3440x1440 screen this results in a client
  area of **3251x1361 at (8, 31)** — scale factor **0.9451**.
- The layout genuinely scales along with it: the field width of the login form drops
  from 554 to **524**, with 523.6 predicted.
- **Do not forget the confirmation** — otherwise the 13-second countdown reverts
  everything (see [driving-the-game.md](driving-the-game.md)).

**The limit of this approach**: the window keeps the aspect ratio of the screen. That
makes it possible to check **whether** and **by how much** an element scales — but not
whether it depends on height or on width. The table above carries that question; it
comes from captures with different aspect ratios.

**And a 5.5% difference is enough.** A spacing of 99 points becomes 93.6 — that
distinguishes "scales" from "constant" unambiguously, since measurement accuracy is in
the range of one to two points.

## How to Measure

1. Capture the client area (not the screen — in windowed mode there are 8/31 points in
   between).
2. View the crop around the element magnified and read off the centre.
3. Convert into the **anchor distance**: for `topRight` that is `width - x`, for a
   bottom anchor `height - y`.
4. Check against the prediction. Deviations of up to about two points are reading
   accuracy; anything beyond that is a different anchor model.

**For lists, measure the row spacing along with it** instead of tracking every entry
individually. The three region entries sit exactly 38 points apart; a set of
137 / 99 / 61 is more plausible than 136 / 97.5 / 61, because a list has a constant
spacing and the individual measurement only estimates the centre of the text.

## What Cannot Be Calibrated

**Elements whose position depends on the text.** The tab row of the PLAY screen lays
its tabs out side by side by text width — and that changes with the language. At the
spot where an English client shows `ARAM` (579), a German client shows
**`Heldenchaos`** instead (ARAM sits at 709 there). Both values scale cleanly with
height, and yet an anchor entered here is wrong the moment someone switches the
language.

**The correct approach there is to search for the word instead of setting a
coordinate** — the way `HeaderReader` searches for the `BEUTE` tab in the navigation
bar instead of calibrating its box.

**The login form** is the second exception, for a different reason: it additionally
shifts with the window width. It is located in the image, not calibrated.
