---
name: drive-smurftown
version: 1
description: Operate the RUNNING Smurftown window from outside — clicks, keys, mouse wheel, captures — via tools/drive-smurftown.ps1. Use for README screenshots and for looking at a UI change in the real app. Triggers on drive Smurftown, click in the app, screenshot the app, operate the UI, "App fernsteuern", drive-smurftown.
---

# Driving the running Smurftown window

`tools/drive-smurftown.ps1` clicks, types, scrolls and captures in an **already running**
Smurftown. It exists because the UI has to be in a specific state for each README shot, and
clicking that by hand is the same eleven-step routine eleven times with eleven chances to forget
one.

## What it does not do: start the app

**The script never starts Smurftown.** If none is running it aborts instead of starting one, and
that stays as it is: driving and starting are two steps, and a script that did both would hide
which of them failed.

Starting is the AI's own step — **but never an unasked one**. The go-ahead comes first and it is
per run: a window that comes up and starts taking clicks seizes the machine the user is sitting at.
See `CLAUDE.md` → Working practice.

Two conditions survive from the older rule that reserved starting for the user entirely.

**Look for a running instance before starting one.** This script and `capture-window.ps1` take the
**first** Smurftown process that owns a window — with two of them up, which window gets clicked is a
coin flip, and one of the two is showing the real list. If one is already up, drive that one; close
only what you started yourself.

**Start it against a test folder.** `.\tools\test-home.ps1` puts the invented accounts into
`%TEMP%` and points `SMURFTOWN_HOME` there. Nothing clicked in that instance reaches the real
`data.yaml` — which the app rewrites whole on every mutation, without a lock.

## Usage

```powershell
.\tools\drive-smurftown.ps1 -Do 'front; click:139,140; wait:600; move:660,770; shot:filter-game'
```

One `-Do` string, steps separated by semicolons.

| Command | Does |
|---|---|
| `front` | bring the window to the front |
| `click:X,Y` | left click |
| `right:X,Y` | right click |
| `move:X,Y` | move the cursor only |
| `wheel:X,Y,N` | mouse wheel, N notches (negative = downward) |
| `key:NAME` | Escape, Enter, Tab, Home, End |
| `type:TEXT` | type text |
| `wait:MS` | wait |
| `shot:NAME` | capture to `docs/images/NAME.png` |

## The four rules that cost time when ignored

1. **Coordinates are window-relative** — exactly what you can read off a capture. The main window
   is borderless (`WindowStyle="None"`), so its window rectangle *is* its client area: 1340×800 in
   the image is 1340×800 on the window. No frame offset to subtract, unlike the game.

2. **No `front` between opening something and clicking in it.** `SetForegroundWindow` closes any
   open popup, and the click then lands on whatever is behind it. Bring the window forward *before*
   the first click of a sequence. This is the same trap as in the game, where it is more expensive.

3. **`move:` before every `shot:`.** The cursor left over a row photographs that row in its hover
   state — a different background and a different border colour than every other row in the image.
   Park it somewhere neutral first.

4. **Capture, then look.** After each step, take a shot and actually read it. The same discipline
   as when driving the game, for the same reason: a click that landed 40 points off does not report
   anything, it just produces a different screen.

## Computing positions instead of hunting for them

Where an element sits follows from the layout budget, which is written down — see
[`docs/ui-layout.md`](../../../docs/ui-layout.md). Grid positions in the hero picker follow from
`HotsHeroCatalog`: groups in role order, alphabetical within, 15 per row, grid pitch 74.

Guessing from an image works until it does not; deriving it from the budget is repeatable.

## Captures

Capture goes through `CopyFromScreen`, not `PrintWindow`. WPF popups — a row's start menu and its
action menu — are **separate windows** at OS level, and `PrintWindow` draws only the window it was
asked for, leaving them out. On the menu screenshots they are the entire point.

The consequence: **whatever overlaps the window ends up in the picture.** Clear the screen first.

What gets captured is the **main window's** rectangle, not the active window's. A modal therefore
sits in front of the dimmed list behind it — the way it is actually used, and it shows the
`DialogBounds` margin as a bonus. `-Foreground` switches that behaviour.

## Related

- Retaking the full README shot list, including the demo-data swap:
  the `readme-screenshots` skill.
- Driving the **game** rather than the app: the `drive-hots` skill — different script, different
  approval rule, and coordinates are client-relative there, not window-relative.
