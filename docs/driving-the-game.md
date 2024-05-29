# Driving Heroes of the Storm from the Outside

What an application needs that starts the game, logs in, and reads data out of it. The
code for this lives in `Smurftown/Backend/Automation/`; what follows is the knowledge
**behind** the code — in particular, what cannot be read off it, because it consists of
the mistakes that were made before it existed.

## The Window

**The game process is not named after the launched program.** What gets started is
`Support64\HeroesSwitcher_x64.exe`; it selects the right build and then exits. The
window afterwards belongs to **`HeroesOfTheStorm_x64`**. Anyone waiting on the launch
process is waiting on the wrong one.

**Do not launch the `.exe` in the root** — per its manifest that is a setup
bootstrapper. And do not use the path `Versions\BaseNNNNN\`: it changes with every
patch.

**The first window is not the game.** After a few seconds a loading screen of roughly
400x180 points appears, with the actual window appearing considerably later. A
minimum size (1000x600) reliably distinguishes the two.

**Client area, not window frame.** `GetClientRect` plus `ClientToScreen`. In borderless
fullscreen these are the same; in windowed mode there are **8 points horizontally and 31
vertically** between them — and missing exactly that offset is what makes clicks land
in the wrong place.

## The Mouse

**Positioning uses `SetCursorPos` with real screen coordinates**, not normalised
absolute values.

> **Pitfall that cost 1400 points**: `SendInput` with `MOUSEEVENTF_ABSOLUTE`, without
> the additional `MOUSEEVENTF_VIRTUALDESK`, maps onto the **primary monitor**, not onto
> the virtual screen. On a machine with two monitors (virtual 6000x1440, primary
> 3440x1440), this made the target 3264 land at **1871** instead. The error is
> proportional — with small coordinates it barely shows, and with large ones it is
> huge.

**Do not bring the window to the foreground between opening and selecting.**
`SetForegroundWindow` closes an open dropdown, and the following click lands on the
background behind it. This happened twice with the region selector: the list opened,
the click on "Europe" hit nothing, and the account silently logged in on **Americas** —
where it is empty. Anyone who needs to bring the window forward does it **before** the
first click of the sequence.

## The Keyboard

**Text is sent as a Unicode event** (`KEYEVENTF_UNICODE`) to an input field.

**An in-game scene does not evaluate the virtual key code, but the scancode.**
`Enter` and `Escape` go to the login form, i.e. to an ordinary input field — the space
bar, on the other hand, goes to a scene, and that needs `MapVirtualKey`. Measured, not
assumed.

**An input field processes a whole batch of backspaces in a single `SendInput` call,
an in-game scene does not.** That is why clearing a field can go out batched, while a
keypress to the scene cannot.

## The Login Form

**It is located in the image, not calibrated.** It does scale with window height like
everything else, but it additionally shifts with **width**: at the same height it sits
73 points lower at 2560x1080 than at 1920x1080. An anchor does not capture that, and a
fixed set of coordinates even less so.

**It is recognised by the border colour `70,57,148`** — two boxes of equal width,
starting at the same distance from the left, directly one above the other. The colour
does not occur in the starfield background behind it and does not change with
resolution.

Two things that go wrong when rebuilding this:

- **Search each image row for the longest contiguous run**, not the first-to-last
  match. Other elements with a similar colour sit in the same row; they stretched the
  measured span from 554 to 1241 points.
- **A field is a pair of edges**, not one edge. Anyone who counts every edge found as
  a box gets six stripes instead of two fields, and the result hits nothing.

**The button underneath is computed, not located.** At some resolutions a stray edge
sits between the fields and the button that any width-based rule would also pick up.
The distance from the password field is **2.21 times** the field spacing — verified at
two resolutions.

**There is deliberately no fallback to fixed coordinates.** That would type the
password somewhere at random instead of saying that it does not recognise the form.

## What Must Be Redone on Every Start

**The region.** The game does not remember it — neither the registry nor
`Variables.txt` change as a result. It stands at `Americas` after every start, **and
again after every logout**. Anyone who forgets it logs in on a region where the same
Battle.net account has an empty game account: 0 gold, no heroes, welcome screen. That
looks like a broken account and is not one.

## Two Ways to Wait

| Tool | Waits for | Used for |
|---|---|---|
| Stability in a region | until **nothing changes** there anymore | after a click that redraws something |
| Repeated measurement | until a measurement **finds** something | when the target still has to appear |

**Stability is not proof.** A loading spinner turns quietly on its own, and a form
being built is already there but not yet in its final position. Both have each led to
an abort once, even though the only problem was looking too early.

**Where stability cannot be measured at all, waiting for stability is not the
answer.** Over a card grid the moving background shows through between the cards — the
region never settles in any pass. The right question there was not "is something still
moving", but "is the page full".

**The measurement function must not log anything.** It runs once a second; a warning
per attempt buries the actual message. The reason comes back as a return value and is
reported once, after the time runs out.

## Captures

**Every capture has a cost.** At 3440x1440 that is around 20 MB, and bringing the
window to the foreground for it takes the screen away from the human. A cadence of
1.5 s is the lower bound at which working alongside it is still possible.

**Small crops read better than large ones.** OCR occasionally returns nothing at all
on large areas, without an error — on small ones, by contrast, error-free. That is not
fine-tuning, it is the difference between "reads" and "does not read".

**The same crop at a different scale factor is a genuine second attempt**, because the
text gets rasterised differently. The same magnification on the same pixels would
inevitably give the same answer — and costs no new capture.

**Remote Desktop distorts every measurement.** The session has the resolution of the
client, not of the monitor; a window then reports something like 2752x1152 instead of
3440x1440. Calibration runs belong on the machine itself.

## Dialogs That Disappear on Their Own

**After a display change, the game asks for confirmation** — "Keep these display
settings? **Reverting in 13 seconds**". Anyone who does not confirm in time finds
everything reverted and looks for the bug in the wrong place. The confirmation belongs
in the same step as applying the change, not in the next one.

**After a language change, the game offers a restart but does not carry it out.** It
exits and does **not** come back on its own. Anyone waiting for that waits forever.
