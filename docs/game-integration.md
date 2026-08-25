# Reading from the running game

What this application does with Heroes of the Storm: start it, log an account in, and read rank,
heroes and currencies straight out of the running client.

The reusable half of that knowledge sits in three other files — [driving-the-game.md](driving-the-game.md)
for finding a window, clicking and capturing, [calibration.md](calibration.md) for anchors instead
of coordinates, and [game-reading.md](game-reading.md) for the read procedures themselves. Here
stands what is specific to this app: the flow with its four exits, what is calibrated and what has
to be searched, and what ends up in `data.yaml`.

**There are two entrances and one read-out.** The account row starts from the app: a row is
picked, the game is started, that account is signed in. The header chip starts from the game: a
client is already running with somebody signed into it, and the app only asks who. What happens
afterwards is the same thing in both cases, and it lives in one place — `UI/MVVM/HotsReadout.cs`.
A second copy of it would be where the two drift apart, in the collection paging or in the merge
rule for heroes.

```
account row ──► GameSession.StartAndLogin ──┐
                (starts, signs out, signs in)│
                                             ├──► HotsReadout.ReadAll ──► data.yaml
header chip ──► GameSession.AttachToRunning ─┘    (chests, profile, penalty,
                (takes over, touches nothing)      header, heroes)
```

One flow, four exits: start → log in → **stop here**, or read and then leave the game
**open**, **close** it, or first **open the loot chests**, then read and close. Which one applies
is chosen in the start menu of the row (see the button column in [`ui-layout.md`](ui-layout.md)).

**The chip offers two of the four**, and it has to offer a choice at all for the same reason the
row does: opening the chests is the expensive half. Reading takes seconds, emptying a stack of
chests takes minutes of space bar, and a chip that always did both would make the cheap case
unusable. So it opens a menu — *refresh data*, or *open the loot chests and refresh* — and hands
the answer through `RunGuideViewModel` to the `openChests` flag of `HotsReadout.ReadAll`. The two
exits that **start** the game are not among them: the client is already running, and the two that
**close** it would sign out a human who is sitting in front of it.

**Only one run at a time**, whichever entrance it came through. Two flows clicking into the same
client take turns bringing the window to the front, and every click then lands on whatever screen
the other one just opened. `RunningGame.TryBegin` is the one flag both take; a second run is
refused with a message instead of queued.

**The first exit reads nothing at all** and is the default behind the game icon. Somebody
clicking in order to play does not want the app switching screens, paging and clicking for over
a minute afterwards. Without reading, **nothing is written** either and `readAt` stays put — a
timestamp without a measurement would be worse than none, because it would report an empty hero
list as "read, owns nothing".

**A running client is reused, not rejected.** `StartAndLogin` signs a running client out and the
wanted account in, instead of aborting with "close it first". When reading several accounts, the
restart is the most expensive item of the whole flow — this removes it.

**The client has to be standing there visibly.** That is a limit of the method and not a bug
somebody will still fix — measured with three approaches.

A minimised window keeps `WS_VISIBLE`, so `IsWindowVisible` still says yes and
`GameWindow.Find()` accepts it; its `GetClientRect` however yields **0×0**. A client once
minimised out of fullscreen does **not** come back from outside:

| Attempt | Result |
|---|---|
| `ShowWindow(SW_RESTORE)` | `IsIconic` goes false, size stays **160×28** |
| plus `BringWindowToTop` + `SetForegroundWindow` | no change, window does not come to front |
| plus a tapped ALT against the foreground lock | **foreground yes**, size stays 160×28 |

The 160×28 is the placeholder size of a minimised window (window rect at −32000,−32000). Next to
it the process list showed an **invisible `D3DProxyWindow`** at 3440×1440.

**That proxy window was read backwards, and the misreading cost three evenings.** It was taken for
an unreachable hiding place; it is in fact the window that *has* the picture. Enumerating every
top-level window of the process in all three states on 23.08.2026 — polling every 400 ms while the
take-over ran, so the failing state was caught live:

| State of the client | What the process owns |
|---|---|
| minimised, no foreground | one window, class `Heroes of the Storm`, client `0×0` at `−32000,−32000`. **No proxy.** |
| restored from outside, **holds** the foreground — the failing state | that window at `160×28`, centred on screen, **plus** `D3DProxyWindow` at `3440×1440` at `0,0` |
| brought up normally and played in | that window at `3440×1440` at `0,0`, plus the proxy, likewise `3440×1440` |

The middle row is the whole bug. The client has the foreground (`foreground=True` in the log), the
picture is on screen and the human watches it come up — and `Process.MainWindowHandle` points at a
`160×28` placeholder centred at `1640,706`, which on a 3440×1440 screen is exactly
`centre − (80,14)`.

**And the proxy is reachable**, `IsWindowVisible` saying no notwithstanding: `Screenshot.Capture`
blits from the **screen** device context with absolute coordinates. It needs a rectangle, not a
window; who owns the pixels does not matter as long as they are on the display. The three attempts
of 21.08 above did not fail because the picture was unreachable — they were all measured on the
wrong window.

**The proxy must never be cached.** It is created anew with every acquisition of the display:
`0x1050BCC` and `0x9A0AAE` within the same hour, and absent entirely while the client is collapsed.

Five rules follow from all this, and they are in the code:

- **`Bounds()` measures the largest window of the process, not `Handle`.** `GameWindow` therefore
  carries two roles that used to be one handle: `Handle` takes the foreground, the focus and the
  input; `PictureWindow()` is resolved fresh on every call and is whatever has the largest client
  area. In every ordinary state — startup, windowed, borderless — that *is* `Handle`, so the flow
  that always worked is untouched.
- **"In front" does not mean "usable".** `BringToFront()` can return `true` while the window
  measures 160×28. `Capture()` therefore also checks the size and fails with it in the message,
  rather than attempting a capture 0 points wide (`Capture area is empty` said nothing).
- **No early `return true` in `BringToFront` for a minimised window** — otherwise `SW_RESTORE`
  further down never runs, precisely when the client already is the foreground window.
- **`WaitForPlayableWindow` calls `BringToFront()` on every pass**, not `Restore()`. That was the
  actual bug behind "found the window, never got a picture": `Restore()` only fires `SW_RESTORE`
  while the window is iconic, and a client taken over from outside leaves that state in the *first*
  pass. The remaining twenty-nine passes then did nothing at all while the loop measured the same
  collapsed window over and over — and reported `minimised=False` at the end, which is what finally
  gave it away. `BringToFront()` costs nothing when the window is already in front and
  un-minimised: it returns before its 400 ms settle.
- **The pass logs `foreground=`**, and that flag is what to read first in a failure. `False` in
  every pass means the handover never happened; `True` with `160×28` means it happened and the
  client did not use it — two different faults that want two different answers.
- **Nothing of ours may appear while the handover runs.** A toast is a window too. One stood in
  `RunningGame.Refresh` before `AttachToRunning` until 23.08.2026, announcing the read for ten
  seconds while `BringToFront` was trying to hand the front to the game. It is gone; the chip
  itself already says `Reading…` for the whole run.

If it stays unusable, the reuse branch says so plainly ("bring it up yourself or close it")
instead of `No usable game window after 15s` — the window is there, it is just not usable.

- **Only from the main menu.** If the client sits in a hero select or in a match, the run aborts
  instead of clicking: signing out mid-match costs a match and earns the account a deserter
  status. If it is already at the login form, there is nothing to do.
- **The screen is measured up to three times before either conclusion is drawn**, one second
  apart, and Menu or Login is trusted the moment either appears. Measured on 24.08.2026: the
  identical, unchanged screen came back as hero select twice and main menu once across three
  sign-outs in a row — `WaitForForeground`, which runs right before this, checks only window
  ownership and size, nothing about the picture inside it, and a single capture taken in that
  same instant could still catch the client's own interface mid-redraw. Once the attempts run
  out, the *last* reading counts, not the first — a transitional frame settles toward the truth.
- **Two clicks, no focus change in between.** `SetForegroundWindow` closes the opened gear menu —
  the same trap as with the region picker, and more expensive here: 66 points below "Log out"
  sits **"Exit game"**.
- **The region falls back to `Americas` on sign-out.** Not a special case, because it is set anew
  on every login anyway — but the reason it has to stay that way.

**Battle.net is not needed.** What gets started is `Support64\HeroesSwitcher_x64.exe`; the game
brings its own login form. The switcher and not the `.exe` in the root: that one is a setup
bootstrapper per its manifest, and the path `Versions\BaseNNNNN\` moves with every patch.

**The region must be set to Europe anew on every start.** The game does not remember it —
checked: neither the registry nor `Variables.txt` changes with it. Two clicks are cheaper than
any registry trick.

## The running client

The other entrance. A chip appears in the header of the window while a Heroes of the Storm client
is up, and it reads the account that is **already** signed in.

### The client has to be in front, and only a human can put it there

This is the fact the whole entrance is built around, and it cost three evenings to find. It is
worth stating before anything else, because every earlier design here was an attempt to work around
it without knowing it.

A client in *exclusive* full screen that does not hold the foreground keeps **rendering** at full
size through an invisible `D3DProxyWindow`, while its **input** window shrinks to a `160×28`
placeholder centred on the screen. The two halves come apart:

| What | Which window | Works without the foreground? |
|---|---|---|
| capture | any — `BitBlt` takes a rectangle off the **screen** DC | **yes** |
| click | the input window, wherever it currently is | **no** |

So reading appears to work while nothing can be clicked. A click at picture coordinates goes to
where that point sits on the *desktop* — which is Battledeck, which takes the foreground, which
collapses the client again. That loop is what a failing run looked like: a client flickering up and
down for half a minute while nothing happened.

`SetForegroundWindow` does not fix it. It restores the *window* without restoring the *display*;
three methods were tried on 21.08 and three more on 23.08, and the log line that finally said so
was `foreground=True` next to `160×28`. What works, first time, every time, is a person pressing
Alt+Tab.

**Hence `RunGuide`** — a small modal that says what it needs, waits for it, and shows the run while
it happens. It waits on the measurement and not on a clock: `GameWindow.IsForemostAndPlayable`
answers "clicks will land" (the foreground window belongs to the client's process **and** the
picture measures playable), asked three times a second for up to a minute. An earlier version slept
six seconds instead, which was a race with the human on one side and no way to tell whether it had
been won.

The five steps are the flow itself, and the detail line under the active one is the run's own
`IProgress<string>` — the channel whose only subscriber used to be the log, which is why
`Strings.ForLog` had to exist to keep German out of `smurftown.log`. Here it finally has the reader
it was written for.

```
  Bring the client to the front   waits for IsForemostAndPlayable
  Identify the account            AttachToRunning, profile overlay, FindByBattletag
  Settle the region               derived, or asked right here
  Read the values                 waits AGAIN if the region was asked, then HotsReadout
  Done                            ARAM marker
```

**Nothing closes itself.** The guide sits behind a full-screen client for the whole run; whoever
comes back wants to read what happened, not to find an empty screen where a window used to be.

**And nothing of ours may take the front while it runs.** Toasts are windows too. One announced the
read before `AttachToRunning` and was removed for exactly this reason, and the ARAM marker moved
*before* the closing toasts rather than after them.

**It signs nobody out and it closes nothing.** That is the whole promise, and it is why
`AttachToRunning` stands next to `StartAndLogin` instead of being a fifth `SessionPlan`: the plan
says what to do after signing in, the entrance says how the session comes to be. `AttachToRunning`
brings the window up, measures it, checks that the main menu is standing — and stops. No sign-out,
no region click.

**A session that came from there is never disposed.** `GameSession.Dispose` kills the game process,
and there is a human in it. Not on success, not in the error branch, not in a `finally`.

**Only from the main menu**, for the same reason the sign-out has that rule. At the login form
there is no account to read; in a hero select or a match the profile overlay is not reachable, and
clicking there anyway costs the human the match. Both cases abort with a sentence that says which
one it was — and, since 24.08.2026, with a screenshot: it shares the sign-out's re-measured check
(`GameSession.SettledScreen`, up to three tries a second apart) instead of trusting a single
capture, for the identical reason — `WaitForForeground` checks only window ownership and size, not
the picture inside it.

**The poll costs a process list and nothing else.** `GameWindow.IsRunning` every three seconds — no
capture, no `BringToFront`. A poll that steals the focus every three seconds would take the machine
away from whoever is playing on it. It is a separate method from `Find()` and not a convenience:
`Find()` hands out a `Process` per candidate, and one leaked handle every three seconds is a handle
leak measured in hours.

### Who is signed in

Nobody said, so the profile says. `ProfileReader.ReadAsync(session, expected: null)` opens the
overlay once and comes back with the battletag **and** the values, out of the same capture — the
reading that answers "who is this" is the one that supplies the numbers, and it is handed on rather
than taken again.

```
battletag read ──► shape valid (NAME#DIGITS)? ──no──► nothing, warn
        │
       yes
        ▼
   carried by exactly one account? ──yes──► that account
        │
        no
        ▼
   carried by more than one? ──yes──► nothing, warn (ambiguous)
        │
        no
        ▼
   placeholder e-mail already taken? ──yes──► nothing, warn (address collision)
        │
        no
        ▼
   create it: name + discriminator + battletag from the profile,
   password empty, e-mail is the placeholder — nothing asked
        │
        ▼
   region settled ──cancelled──► nothing
        │
        ▼
   read and write
```

**There is deliberately no confirming second reading here**, unlike the rename case. Two captures of
the same static overlay are the same pixels and yield the same misreading — the guard would be
theatre. What actually guards this path sits on the other side: the tag has to match a stored
account **character for character**, and the realistic slips (`I`/`l`, `0`/`O`, `5`/`S`) turn a
battletag into a string that matches nothing, and then nothing is written or created — the same
safe outcome an unmatched tag always had.

**Two accounts carrying the same battletag count as no answer, and it stays that way.** Identity
here is the e-mail, so nothing stops two entries from carrying the same tag; picking one of the
two would write a whole reading into an account chosen by list order. `FindByBattletag` answers a
missing tag and an ambiguous one identically, with `null` — and since a missing tag now creates an
account instead of refusing (next paragraph), the ambiguous case needs a check of its own so it
does not fall into the same branch: `BattlenetAccountGateway.IsAmbiguousBattletag` asks the one
question `FindByBattletag` cannot, and `RunGuideViewModel` fails the run on it before
`CreateAccount` is ever reached.

**A battletag nobody carries creates the account instead of refusing it — since 25.08.2026.**
Whoever wants an account without ever typing a password starts Heroes of the Storm themselves,
signs in, and this is what creates it: name, discriminator and battletag come from the profile
overlay, the password stays empty on purpose, and the e-mail is a placeholder built from the same
battletag (`BattlenetAccount.PlaceholderEmail`) rather than asked for — nothing is left this step
could still need. The one way this still refuses is a placeholder address that already belongs to
a real, hand-typed account under that exact e-mail; `RunGuideViewModel.CreateAccount` checks for
it and warns rather than silently colliding. `AccountCardViewModel` hides such an account's start
menu — there is no password to type into the login form with — reading still works from here.

### Which region

**The game does not say.** Rank, heroes and currencies are stored per region, the client is signed
into exactly one — and on none of the calibrated screens does it stand which. Searched for on
22.08.2026: neither the main menu nor the profile overlay shows it. So it is derived where it can
be and asked where it cannot.

| The account plays HotS in | What happens |
|---|---|
| exactly one region | that one, no question — the normal case |
| several | asked, offering exactly those |
| none yet | asked, offering all three; the pick is **added** to the account's HotS regions |

The last row is not politeness. The overview builds one row per played region, so a reading written
into a region nobody plays would have no row to appear in — it would be invisible, which is worse
than an extra tick.

**It is asked before the reading, not after.** The collection alone takes over a minute, and a
question at the end of that is a minute spent before finding out nobody was there to answer.

**It is asked inside the guide, not in a window of its own.** Until 23.08.2026 it opened
`RegionPicker`, a second modal — and a second window taking the foreground is the one thing this
flow cannot survive. That window is gone; what survived of it is the `RegionChoice` record, now in
`RunGuideViewModel`.

**Answering means alt-tabbing away from the game**, so the step *after* the question waits for the
client to come back before it clicks anything. For an account with one region nothing is asked and
that second wait returns immediately — which is the normal case, and the reason the question is
only asked when it has to be.

### What it ends on

The same done marker as "Play and refresh data": the PLAY screen, on ARAM. Without it the client
sits on some collection screen and whoever looks over cannot tell whether the app has finished or is
still paging. Here it matters more than there — the human is at the machine and wants to keep
playing.

## Anchors instead of fixed coordinates

`screen-map.yaml` is the only place with coordinates. Every entry names an **anchor**
(`topLeft`, `topCenter`, `topRight`, `bottomLeft`, …) and an offset from it in points of the
reference size 3440×1440. At runtime the offset is scaled by `window height / 1440`.

At equal **height** nothing changes, however wide the window is. Horizontally every element
clings to an edge or to the centre, and which anchor applies is measured per element. Whoever
enters a new point measures it at **two** resolutions — an anchor that only holds at one is not
an anchor.

The full reasoning, the measurement tables, the client-area-versus-window-frame trap and **how to
get to a second resolution** are in [`calibration.md`](calibration.md).

**The login form is the exception** and therefore not in the calibration. It scales with the
height too, but additionally shifts with the width. `LoginLocator` finds it in the image instead
— two equally wide boxes with the border `70,57,148`, a colour that does not occur in the starry
sky behind it. There is deliberately **no** fallback to fixed coordinates: that would type the
password somewhere rather than say it cannot recognise the form.

## Tabs are searched, not calibrated

**A tab row cannot be calibrated**, and that is the one exception to the anchor model that has
nothing to do with resolution: its entries sit next to each other in order, each as wide as its
word. A single longer word further left shifts everything after it.

| Element | German | French |
|---|---|---|
| Collection tab | `SAMMLUNG` at 399 | `COLLECTION` at **379** |
| Hero sub-tab | `Helden` at 497 | `Héros` at **476** |
| Tabs in the PLAY screen | `Heldenchaos` at 579, ARAM at 709 | — |

This is the most expensive mistake this app knows: it aborts nothing, it **opens the wrong
thing**, and whatever is read afterwards is the text of some other screen.

Searching goes through `TabFinder` + `GameVocabulary`, at four sites: collection
(`CollectionReader`), hero sub-tab (ditto), loot chests (`LootOpener`) and ARAM (`PlayScreen`).
`HeaderReader` always did it that way.

- **The shortest match wins, not the first.** In the sub-tab row `Packs de héros` sits **left** of
  `Héros`, in German `Heldenpakete` before `Helden`, in English `Hero Packs` before `Heroes`.
  Taking the first lands you in the purchase screen every time.
- **No fallback to a coordinate.** A calibrated point would be right for exactly one language;
  the same reasoning by which `LoginLocator` searches for the login form instead of typing
  somewhere on failure. Found or aborted.
- **`play.tab` stays calibrated** and is the exception within the exception: the tab is the first
  of the row, clings to the left edge, and is clicked near its **start**. That start is in the
  same place in every language, however long the word after it.

## The read procedures

Waiting, what is read from where, loot chests and the collection paging are in
[`game-reading.md`](game-reading.md). Four things from there are load-bearing enough to
name here:

- **Stillness is no proof.** A loading spinner spins quietly, and a form under construction is
  already there but not yet at its final place. Hence two tools that do not replace each other —
  `WaitForStableArea` waits for nothing to change, `Retry` waits for a measurement to find
  something.
- **The rank is read as text, not compared as an image.** In the profile overlay `Sturmliga` and
  below it `Silber 3` stand as running text — tier and division in one line. The former image
  comparison failed on a shimmering medal disc, could not read a rank that was not already in
  `data.yaml`, and never checked whether the screen it photographed was the rank screen at all.
- **Heroes come from the collection, not from hero select.** Hero select shows all 90 on one
  screen, which looks like the shorter path and is not: there a hero is only a tile, and "owned"
  means "brighter". The collection writes the name as **text** under every card.
- **Loot chests are opened with the space bar**, three presses per chest. The five calibrated
  click points this replaced are gone on purpose: 22 points right of "Accept" sits
  **"Retry: 250 gold"**. Whoever puts coordinates back in re-introduces a way to burn the
  account's gold.

**If a run strands, a capture lands in `~/.smurftown/shots/`.** Without it there is no deciding
afterwards whether the calibration is stale or the game was merely slow.

## What gets written

Without asking, straight into `data.yaml` — a deliberate reversal of the earlier intermediate
state in which the edit dialog opened afterwards. Somebody starting the game in order to play
should not have to switch back to the app first. That is only bearable because every step has a
strict stop condition ("no idea" is a valid answer and writes nothing) and because the toast
names every change in plain words.

The three read steps are individually guarded and do not depend on each other. They run entirely
in the background: capturing and clicking block, and the collection takes over a minute — on the
UI thread the app would stand still that long. Messages are therefore collected and shown after
the return.

The fields in `data.yaml`, all written by the read and therefore **not** visible in the edit
dialog. They sit under `hotsByRegion` **per region** — exactly one is read, namely that of the
row the start came from (see [Regions](data-model.md#regions)):

```yaml
hotsByRegion:
  Europe:
    gold: 3835
    shards: 760
    gems: 305
    accountLevel: 174             # from the profile overlay, not from the header bar
    lootChests: 27                # sum across all chest types, from the navigation bar
    readAt: 2026-08-20T15:14:00   # null means "never read", not "found nothing"
```

`hotsTier`, `hotsDivision`, `hotsPlacementsPending` and `hotsPenaltyGames` are **also** written by
the read but appear in the edit dialog anyway: they used to be hand-maintained fields and stay
that way for accounts one does not start in the first place.

`readAt` carries the distinction everything hangs on: an empty hero list can mean "owns none" or
"was never read", and only in the first case may it overwrite a result. The four numbers stay
individually put when a run did not read them — a number from yesterday is better than a gap
caused by one blurred capture.

**`name` and `discriminator` belong to the written set too** — but only under three conditions at
once, and this is the most delicate write in the whole app. This is also the *only* path by which
a battletag reaches `data.yaml`; the dialog has no field for it. A freshly created account carries
two empty strings until it has been started and read once.

```
Reading 1 ──► battletag matches? ──yes──► write as usual (the normal case, one capture)
                    │
                   no
                    │
                    ├─ not battletag-shaped (NAME#DIGITS)  ──► nothing, warn
                    ├─ belongs to another account          ──► nothing, warn
                    │
                    ▼
              Reading 2 ──► same tag?  ──no──► nothing, warn
                                │
                               yes
                                ▼
                    adopt name + discriminator,
                    write the values of the SECOND reading
```

- **The collision check is the more important of the two guards.** The dangerous case is not the
  rename but the foreign screen — and on a machine with 27 accounts the read tag is then likely
  one of them. `BattlenetAccountGateway.OwnerOf` is the only place that decides this; the
  comparison runs over the **email**, because that is the identity — the name is precisely not.
- **The second capture catches read errors.** Without it a slipped letter would turn
  `PITAPAN#2523` into `PlTAPAN#2523`: shape valid, no collision, account renamed. A real rename
  reads the same thing twice, a read error almost never. It costs about eight seconds and is
  incurred **only** in the deviation case.
- **The shape check lives in `BattlenetAccount.TrySplitBattletag`**, the counterpart to
  `Battletag()`. Deliberately strict (name 3–16, starts with a letter, then letters and digits
  only; discriminator 3–6 digits): a wrongly rejected battletag is cheap, a wrongly accepted one
  renames an account. Cross-checked against all 27 real battletags.
- **`ProfileReader` does not decide the case.** It supplies the values, marks them via
  `ProfileReading.Matches` as unresolved and names the tag it read. Resolving the identity needs
  the list of all accounts — and `Backend/Automation/` deliberately does not know the gateway.
  The decision therefore falls in `AccountCardViewModel.AdoptRenamedBattletag`.
- **`ApplyProfile` checks `Matches` a second time**, although its only caller already did. Double
  floor: whoever calls the method from elsewhere in future fails there rather than in `data.yaml`.
- **The toast names the rename in plain words** (`Battletag OLD -> NEW`) and the log writes it as
  a `Warning` — unlike every other change, because it touches the identity of the entry and not
  just a value on it.

**If the game stays open, the flow ends on ARAM.** A done-signal and nothing else: without it the
client sits on some collection screen, and whoever comes back to the machine cannot see whether
the app has finished or is still paging. On ARAM it has finished — and you can hit "Ready" right
away. If that step fails, nothing aborts: it is a sign for the human, not a step data depends on.
