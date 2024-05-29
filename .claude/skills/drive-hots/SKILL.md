---
name: drive-hots
version: 1
description: Start and operate Heroes of the Storm from outside via tools/drive-hots.ps1 — for calibrating screen-map.yaml and for measuring the game vocabulary of a client language. Covers the announcement rule, the three process traps and the client-relative coordinates. Triggers on drive HotS, calibrate, screen-map, measure the game, client language measurement, "Spiel fernsteuern", drive-hots, Eichung.
---

# Driving Heroes of the Storm

`tools/drive-hots.ps1` starts the game, clicks, types, captures and quits. It exists because every
calibration in this repo is measured against the running game, and a second-hand screenshot is half
a measurement.

## The approval rule — read this first

**Claude may start, operate and close the game, logging in included** — every calibration here is
measured against the running game, and a second-hand screenshot is half a measurement.

**The condition: ask, and wait for the answer.** Not "announce, then go". The game takes over the
screen and logs an account in, and that interrupts whatever the user was doing. The go-ahead is for
this run; the next one gets asked again. Waived only while the user is demonstrably away, and only
for as long as they are.

**What is specific to the game is what the question has to say.** Smurftown is a window among
windows and needs the go-ahead too — just not the warning about the screen. See `CLAUDE.md` →
Working practice for the rule both applications share, and the `drive-smurftown` skill for the app.

## Usage

```powershell
.\tools\drive-hots.ps1 -Do 'start; wait-window:180; info; shot:login'
.\tools\drive-hots.ps1 -Do 'click:1720,900; wait:800; crop:region,1500,700,500,300' -OutDir .\shots
```

| Command | Does |
|---|---|
| `start` | start the game (path from `settings.yaml` in the data folder), wait for a usable window |
| `wait-window:SEC` | wait for a window ≥ 1000×600, at most SEC seconds |
| `info` | print the client area — size and position |
| `front` | bring the window to the front |
| `click:X,Y` / `right:X,Y` | click, **client-relative** |
| `move:X,Y` | move the cursor only |
| `wheel:X,Y,N` | mouse wheel, N notches (negative = downward) |
| `key:NAME` | escape, enter, tab, space, up, down, left, right |
| `type:TEXT` | type text (Unicode) |
| `clear` | End, then backspace until the field is empty |
| `user:BATTLETAG` | type that account's e-mail, read from `data.yaml` in the data folder |
| `pw:BATTLETAG` | type that account's password, read from the same file |
| `wait:MS` | wait |
| `shot:NAME` | capture the client area |
| `crop:NAME,X,Y,W,H` | capture only that area — small crops read better |
| `quit` | quit the game |

`-OutDir` decides where captures land (default `%LOCALAPPDATA%\Temp\hots-shots`).

**`user:` and `pw:` take a battletag, not a password.** A password passed on a command line lands
in the shell history, the process tree and every log that records it. The application itself left
exactly that path behind when `psexec` went; do not reintroduce it here.

**"The data folder" is `%USERPROFILE%\.smurftown` — unless `SMURFTOWN_HOME` says
otherwise.** The script resolves it through `tools/smurftown-home.ps1`, exactly as the app does in
`Directories.UserPath`. That matters here in a way it does not elsewhere: a test folder holds the
**demo** accounts, and `user:GHOSTLANE` then types `ghostlane@example.com` into the login form of a
real game client. Whoever wants to log in for real starts from a shell without the variable set.

## The three process traps

1. **The game process is called `HeroesOfTheStorm_x64`, not `HeroesSwitcher_x64.exe`** — the latter
   is what gets started. Waiting for the start process means waiting for the wrong one.
2. **The first window is a loading screen of roughly 400×180.** A minimum size of 1000×600
   separates it from the game.
3. **`front` between opening a list and choosing from it closes the list** —
   `SetForegroundWindow` does that. Bring it to the front *before* the first click of a sequence.
   In the gear menu this is expensive: 66 points below "Log out" sits **"Exit game"**.

## Coordinates are client-relative

Not window-relative — this is the difference from `drive-smurftown`. In windowed mode there are 8
points horizontally and 31 vertically between window frame and client area, and those are exactly
what you click past otherwise. A capture from `shot` shows the **client area**, so what sits at
(x,y) in the image is hit by `click:x,y`.

## Measuring discipline

- **Capture after every step and actually look.** A click 40 points off reports nothing; it just
  opens a different screen, and everything read afterwards is that screen's text.
- **An anchor is measured at two resolutions.** One resolution gives you a number and a
  coincidence. How to get to a second resolution — and which two obvious routes do **not** lead
  there — is in [`docs/calibration.md`](../../../docs/calibration.md).
- **A minimised client cannot be recovered from outside.** It keeps `WS_VISIBLE` but reports 0×0,
  and `SW_RESTORE` leaves it at the 160×28 placeholder size. The real image lives in an invisible
  `D3DProxyWindow`. If it is minimised, ask the user to bring it up.
- **Remote Desktop falsifies every measurement.** The session has the client's resolution, not the
  monitor's — a window then reports something like 2752×1152 instead of 3440×1440. Calibration runs
  belong on the machine itself.

## Measuring a client language

Setting up a new language variant means measuring words, never guessing them: of six guessed French
words, four were wrong, and none of the four would have announced itself in the build or the log.

The procedure, the menu path for switching the client, what the OCR needs in terms of Windows
language packs, and what does **not** change with the language are in
[`docs/client-language.md`](../../../docs/client-language.md).

Where the measured words go: `Smurftown/Backend/Automation/GameVocabulary.cs`, in all five
variants, with every unmeasured line marked `NOT MEASURED` until somebody has checked it.

## Related

- What is read from where, and how the read procedures wait:
  [`docs/game-reading.md`](../../../docs/game-reading.md)
- Window handling, clicking and capturing in general:
  [`docs/driving-the-game.md`](../../../docs/driving-the-game.md)
