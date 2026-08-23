# The update check

Smurftown asks GitHub once an hour whether a newer release exists, and can put it in place itself.
This is the **only** thing the application ever sends anywhere — everything else it does happens on
this machine. What that costs and what it does not is in [security.md](security.md#the-update-check);
this file is about how it works.

## What happens, in order

```
App.OnStartup
  │
  ├─ UpdateInstaller.CleanUpPrevious()     delete Smurftown.exe.old, if it is free by now
  │
  └─ MainWindow  ──►  UpdateOffer.Watch()
                        │
                        ├─ Look()                          once, now
                        │    │
                        │    ├─ ShowOffer(update.yaml : latestVersion)  ← no network, first frame
                        │    │
                        │    └─ await UpdateGateway.CheckIfDue()   ← only if an hour has passed
                        │         │
                        │         └─ GithubReleases.Latest()   api.github.com, anonymous
                        │              │
                        │              └─ ShowOffer(...)  again, from the same place
                        │
                        └─ DispatcherTimer, every 10 min  ──►  Look()   for as long as the app is open
```

**Two places show this, and they are the same object.** The version chip sits in the top right
corner of the header and carries both numbers — `v1.0.0` on almost every start, `v1.0.0 → 1.0.1`
when something is offered. A click opens a panel under it with one sentence, one button and the
date of the last check. The second place is the `ABOUT & UPDATES` card at the foot of the settings
tab, which shows the same state as rows: installed version, latest version, when GitHub was last
asked, and whether this installation may replace itself at all.

`UpdateOffer` is that object. It is a singleton for the same reason the gateways are: the settings
ViewModel is built on the first visit to the tab, and a second copy of this state machine would let
an installation started in the card leave the chip in the header showing an offer that is already
being installed.

**What the button does, its label says** — `Install` where this build may replace itself, `Open the
release page` where it may not. The sentence above it carries the reason in that second case, so
the human reads *why* they have to do it by hand instead of finding out after a click.

| Actor | What it is | Its part here |
|---|---|---|
| `GithubReleases` | a static class in `Backend/Update/` | asks the releases API, returns the release or null |
| `UpdateGateway` | hand-written singleton, same pattern as the other gateways | owns `update.yaml`, decides whether an hour has passed |
| `UpdateInstaller` | a static class beside them | downloads, verifies, swaps the `.exe`, cleans up afterwards |
| `UpdateOffer` | one ViewModel object, used by two views | holds the four display states and runs the install |

## Why the clock lives in a file

**A timer alone would be wrong, and a file alone is no longer enough.** Both exist, and each covers
what the other cannot.

The file is the clock: `~/.smurftown/update.yaml` holds the time of the last request, and any look
that finds it more than an hour old does the check. That is what makes the interval survive a
restart — Smurftown is opened, used and closed again, and an interval that lived in a timer would
start over on every one of those, so an application opened five times in an afternoon would ask
five times.

The timer is what keeps a **long** session honest. `UpdateOffer.Watch()` looks once at start and
then ticks every ten minutes, and every tick does nothing but ask the file whether an hour is up.
Without it, a window that has been open since the morning would still show the answer from the
morning.

**Ten minutes, and the number is not the interval.** A tick every hour lands against an hourly
deadline it misses by whatever the start-up took: the check comes due seconds after the tick that
just decided against it, and the answer arrives an hour late. At ten minutes the worst case is ten
minutes late, and the five ticks in between cost one subtraction each — no request, no file access,
nothing that reaches the network.

**Nothing is said when something is found mid-session.** The chip in the header changes and that is
all: no toast, no dialog. A version that appeared at two in the afternoon can be installed at any
time, and interrupting somebody over it would buy nothing.

```yaml
lastCheck: 2026-08-22T09:14:03.0000000+00:00
latestVersion: 1.0.1
```

- **`lastCheck` is written even when the check fails.** Otherwise a machine that is offline for a
  week asks on every single start and writes a warning each time.
- **A stamp in the future counts as due.** A clock corrected backwards — a fresh installation, a
  dead CMOS battery — would otherwise push the next check as far out as the clock jumped, and no
  update would ever appear on that machine again.
- **`latestVersion` exists for the first frame.** It puts the notice on screen without waiting for
  the network, which is right on almost every start: a release does not appear between two of them.
  It is compared against the running version rather than trusted, so it corrects itself once
  somebody has installed by hand.

**It is not part of `settings.yaml`**, although it would fit in the same file. Settings are what a
human sets; these two values are what the application noted. Mixed together, every check would
rewrite a file the human edits by hand.

## When the app may replace itself, and when it may not

`UpdateInstaller.Route()` answers this before the button is drawn, so the human sees what a click
will do rather than finding out once it has failed.

| Route | Detected by | What the button does |
|---|---|---|
| `Replace` | none of the below | download, verify, swap, restart |
| `DevBuild` | a `Smurftown.dll` lies beside the `.exe` | open the release page |
| `NotWritable` | a write probe in the folder fails | open the release page |
| `Unknown` | no `Environment.ProcessPath` | open the release page |

**`Smurftown.dll` is the discriminator** because a single-file publish carries the managed assembly
*inside* the `.exe`. Beside it, that file only exists in `bin\Debug` or `bin\Release` — measured,
the Debug folder holds it plus nineteen more DLLs. Replacing that `.exe` with a release would throw
away the build whoever is sitting there is currently testing.

**`NotWritable` is the old MSI installation.** `C:\Program Files\ZrdJ\Smurftown` needs
administrator rights, and this application deliberately runs as `asInvoker` (`app.manifest`). The
permission is probed by writing, not by reading an ACL: an ACL check answers the question for the
account, not for the process — UAC virtualisation, a read-only attribute and a locked volume all
sit between the two.

## The swap

This is one file move, and only because of how the release is built: `./dev release` publishes
single-file and framework-dependent, so the whole application is `Smurftown.exe` and there is no
set of DLLs beside it that would have to be swapped consistently. That removes the entire class of
half-updated installations.

```
1. download   the release ZIP  ──►  %TEMP%\smurftown-update\
2. verify     SHA-256 against the checksums.txt of the SAME release
3. extract    Smurftown.exe out of the ZIP, nothing else
4. move       Smurftown.exe  ──►  Smurftown.exe.old       ← the running image
5. move       the fresh one  ──►  Smurftown.exe
              └─ fails?  move .old back, then throw
6. start      the new .exe, shut this one down
7. next start delete Smurftown.exe.old
```

**Step 4 works because Windows lets a running executable be renamed but not deleted.** That single
fact is what makes the whole procedure possible without a helper process, a scheduled task, or a
batch file that outlives the application.

**Step 5 has a rollback and needs one.** Between the two moves there is a moment in which no
`Smurftown.exe` exists. If the second move fails — a virus scanner holding the fresh file, a full
disk — and nothing put the old one back, the human would be left with an application that is simply
gone.

**Step 7 is expected to fail once.** The process the file belonged to started us and is on its way
out; until it is gone, Windows holds the image. The next start finds it free. That is why the
cleanup swallows instead of reporting — a warning on every single update, describing a state that
repairs itself, is worse than silence.

**Only `Smurftown.exe` comes out of the ZIP.** The package also carries the `README.md` that `dev`
stages beside it; that is a copy of the landing page, not part of the installation. Unpacking the
archive wholesale would put files into a folder the human chose, from a decision made in
`cmd_release`.

**The restart runs with `UseShellExecute = false`**, and that is not a detail: only then does the
new process inherit this one's environment — `SMURFTOWN_HOME` included. With it true, an update
triggered from a test run would restart against the real account list.

## What is verified, and what is not

The SHA-256 of the downloaded ZIP is held against the `checksums.txt` of the same release. Both
come over HTTPS from github.com, so **the trust anchor is that connection and the account behind
the repository — not a signature on the file.** Nothing Smurftown ships is signed and no amount of
code here makes it so; see `Setup.vdproj`, which stands on `SignOutput = FALSE` with an empty
certificate.

So the check answers exactly one question: *is this the file the release says it is*. Whoever
expects more from it than "the download is not corrupt and was not swapped in flight" expects the
wrong thing.

## What the release has to look like

The updater reads three things out of a release, and all three are what `./dev release` and
`release.yml` already produce. They are now a contract:

| What | Why the updater needs it |
|---|---|
| the tag is the version, no `v` prefix — `2.0.1` | it is compared against the running version, three parts, numerically |
| **exactly one** `.zip` asset | a release with two is one where somebody has to decide which, and guessing is the worse answer |
| a `checksums.txt` asset listing that ZIP by name | there is nothing to verify against otherwise |

**The ZIP's file name is searched, not constructed.** Building `Smurftown_{version}_win-x64.zip`
from the version would make a file name a contract between two places that cannot see each other,
and the day somebody changes the RID, the updater would stop finding anything with no error
anywhere near the change. The name that comes out of the search is also the one looked up in
`checksums.txt`, so the two can never disagree.

The version comparison is three numbers, never text: a string comparison answers `1.0.10 > 1.0.9`
with "no", and it does so silently, on the one release where it finally matters. Anything that is
not `x.y.z` is not a tag this repository produces and is therefore not an update either — see
`AppVersion.IsNewerThanCurrent`.

## There is no setting

The check runs every hour and the human cannot switch it off. That is a decision, and it was taken
after building the switch and looking at it:

- **The delivery has no other way of reaching anybody.** What ships is a ZIP without an installer
  and without a start menu entry, so nothing but this notice tells a human that a version exists. A
  setting would have been found by almost nobody, and would have cost the update to whoever found
  it and forgot.
- **It bought an inconsistency.** Switching it off left a notice that was already on screen sitting
  there — `ShowOffer` runs at startup, and the setting changing afterwards did not reach it. Fixing
  that needed an event from the gateway into the ViewModel: machinery for an option nobody asked
  for.
- **The honest alternative is to say so.** [security.md](security.md#the-update-check) states the
  request rather than offering to suppress it, which is the thing somebody actually needs to know.
  Blocking `api.github.com` for this executable stays possible and is a decision at the level where
  such decisions belong.

The consequence for the code is one less moving part: `UpdateGateway` has no `Enabled`,
`SettingsGateway.Apply` does not touch `Backend/Update`, and `settings.yaml` gained no field. An
installation that briefly carried a `checkForUpdates` key keeps it harmlessly — the deserialiser
runs with `IgnoreUnmatchedProperties()`.

**`Check now` in the settings is not that switch coming back.** It calls `UpdateGateway.Check()`,
which asks regardless of the clock and writes the same stamp the hourly check writes — so it brings
the next check *forward* and cannot postpone or suppress one. What it is really for is the answer
beside it: `Last checked` is the first thing that ever showed the value in `update.yaml`, and a
date without a way to refresh it invites the question whether the check still runs at all.

## Testing it

The check itself needs no release: point it at a version that does not exist yet by lowering
`<Version>` in the csproj, build, and the notice appears against whatever is currently published.
**The install path is the half that cannot be tested from a Debug build** — `Route()` returns
`DevBuild` there, deliberately. To exercise it, `./dev publish` into `dist/publish/`, run that
`.exe`, and let it replace itself.

Two things to know before doing that:

- **It is a real download** of the real release, roughly 34 MB, and it really replaces the `.exe`
  in `dist/publish/`. That folder is disposable, which is exactly why it is the right place.
- **`SMURFTOWN_HOME` survives the restart** (see above), so a run started through
  `tools/test-home.ps1` stays on the test folder across the update.

The rate limit is 60 requests per hour and IP address, unauthenticated. The check itself spends one
of those sixty in the hour it falls into, and only while the application is open; repeated manual
testing gets nowhere near it either, but a loop would.
