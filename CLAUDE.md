# smurftown

WPF desktop app for managing several Battle.net accounts on one Windows machine. The feature
that matters beyond plain account management: **start Heroes of the Storm for an account, log
in, and read rank, heroes and currencies straight out of the running game** — see
[`docs/game-integration.md`](docs/game-integration.md).

The earlier core feature — one dedicated Windows user per account, Battle.net launched over it
via `psexec` — has been removed. It existed to run several Battle.net instances side by side;
the game brings its own login form when started directly, which made the whole detour
pointless. Three of the four security trade-offs and the administrator rights went with it.

Repo: `git@github.com:tibbots/smurftown.git` · default branch `main`.

Local app: no server, no account, no telemetry. All data lives in the user's home directory.

**It makes exactly one request, and it is worth knowing which.** Once an hour the app asks
`api.github.com` whether a newer release exists, anonymously and without sending anything about the
human or their accounts; if one does, it can download and install it itself. That is the whole of
the outbound traffic, and it is the reason this paragraph no longer reads "no network traffic" —
see [`docs/self-update.md`](docs/self-update.md) and
[`docs/security.md`](docs/security.md#the-update-check).

**There is no setting for it and that is deliberate**: the check runs, the human cannot switch it
off, and the honest move is to state the request rather than to offer a switch nobody would find. A
second request would be a change to what this application *is*, not a feature — name it here and
there before building it.

## Where things are documented

This file holds what somebody needs **before** touching anything: what the app is built with, how
it is built and delivered, and how work happens here. How the app is put together on the inside is
in `docs/`, one file per question — and that split is deliberate: a single file that answers every
question is one nobody reads to the end.

| Where | What belongs there |
|---|---|
| `CLAUDE.md` (this file) | stack, build and delivery, working practice |
| [`docs/`](docs/README.md) | how this app is built — and knowledge that stays true elsewhere too |
| [`.claude/skills/`](.claude/skills/) | procedures somebody executes: release, screenshots, driving the app or the game |
| [`CHANGELOG.md`](CHANGELOG.md) | what changed between two releases, in the words a user would use |
| `git log` | what an element used to look like before today |

That last row is a rule, not a joke. These documents state what **is** — not what it was. A past
state is mentioned only where it is the warning itself, i.e. where someone would otherwise
re-introduce a removed hazard.

**`CHANGELOG.md` is the one named exception**, and it is one on purpose: it holds the past because
somebody outside this repo needs it. An entry is written in the pull request that causes the change,
under `## [Upcoming]`; releasing renames that heading to `## [X.Y.Z] - DATE` and does not fill it. A
changelog written on the day of the tag is a `git log` with extra steps — and here there would not
even be that, the history is a single commit.

What belongs in it is what a **user** notices: a new capability, a changed behaviour, a fixed bug, a
security-relevant trade-off. A refactoring, a renamed class or a new document does not.

**How this application is built.** Start at the first row; it is the checklist and it points at the
rest.

| Document | What it covers |
|---|---|
| [`docs/changing-things.md`](docs/changing-things.md) | **the checklist to read before editing** — plus the known issues nobody fixes in passing |
| [`docs/architecture.md`](docs/architecture.md) | the annotated file tree, the layer rules, where the data lives, the backup before a migration |
| [`docs/data-model.md`](docs/data-model.md) | what `data.yaml` holds: regions, ranks, heroes, free rotation, penalty games, archive |
| [`docs/ui-conventions.md`](docs/ui-conventions.md) | MVVM as it is actually done here: themes, dialogs, the account dialog, the account row, settings |
| [`docs/ui-layout.md`](docs/ui-layout.md) | the pixel budgets: window, filter bar, account row, dialogs |
| [`docs/localisation.md`](docs/localisation.md) | the four languages the UI speaks and the five vocabularies the OCR compares against |
| [`docs/game-integration.md`](docs/game-integration.md) | starting the game, logging in, and reading rank, heroes and currencies back out |
| [`docs/self-update.md`](docs/self-update.md) | the hourly check, when the app may replace itself, and what the checksum does and does not prove |
| [`docs/security.md`](docs/security.md) | the deliberate trade-offs — and why no screenshot may show the real account list |

**Knowledge that stays true elsewhere too**, i.e. for the next application that has to drive a game
from outside.

| Document | What it covers |
|---|---|
| [`docs/driving-the-game.md`](docs/driving-the-game.md) | finding a window, clicking, typing, capturing — and the traps that cost time |
| [`docs/calibration.md`](docs/calibration.md) | anchors instead of coordinates, how the scale factor works, **how to get a second resolution** |
| [`docs/client-language.md`](docs/client-language.md) | what depends on the client language, how to switch it, what does **not** change |
| [`docs/game-reading.md`](docs/game-reading.md) | the read procedures: waiting, collection paging, loot chests, what is read from where |
| [`docs/assets.md`](docs/assets.md) | every bitmap is script-generated - which script, from what, and what was measured |
| [`docs/measurements.md`](docs/measurements.md) | measured values that are in no code yet |

| Skill | What it does |
|---|---|
| [`release-prep`](.claude/skills/release-prep/) | version bump across three files, tag, what CI does, installer state |
| [`readme-screenshots`](.claude/skills/readme-screenshots/) | swap in demo data, drive the app, retake the README images |
| [`readme-translations`](.claude/skills/readme-translations/) | keep `README.{de,fr,es}.md` in sync with the English original |
| [`drive-smurftown`](.claude/skills/drive-smurftown/) | operate the running app from outside — click, type, scroll, capture |
| [`drive-hots`](.claude/skills/drive-hots/) | start and operate the game for calibration and language measurement |

## Stack

| What | Value |
|---|---|
| Framework | .NET 8, `net8.0-windows10.0.19041.0`, WPF (`UseWPF`) |
| Language | C# 12, `Nullable` + `ImplicitUsings` enabled |
| MVVM | `CommunityToolkit.Mvvm` (`ObservableObject`, `RelayCommand`) |
| Dialogs | `MvvmDialogs` (`IDialogService`, `IModalDialogViewModel`) |
| Toasts | `ToastNotifications` + `.Messages` |
| Logging | Serilog (console + file) |
| Persistence | YamlDotNet, camelCase naming |
| Automation | `user32`/`gdi32` via P/Invoke — `SendInput`, `BitBlt`, no package |
| Text recognition | `Windows.Media.Ocr` — part of the OS, hence the SDK target |
| Installer | Visual Studio setup project `Setup/Setup.vdproj` → MSI |

Three projects in `Smurftown.sln`: `Smurftown` (app), `Smurftown.Tests` (xUnit) and `Setup`
(MSI). **No devcontainer, and none is possible** — see
[Build, release, delivery](#build-release-delivery). CI runs two workflows on `windows-latest`,
both of which run the tests before they build anything.

`Smurftown.Tests` targets the same `net8.0-windows10.0.19041.0` with `UseWPF`, because it loads
the application's compiled XAML. It points `SMURFTOWN_HOME` at a throwaway folder from a module
initializer, so **no test can reach the real `~/.smurftown`** — that file holds the passwords in
plain text and is rewritten whole on every mutation.

**A test that needs something the build server has not got carries a trait.** `[Trait("needs",
"ocr")]` marks those that compare against `Windows.Media.Ocr`, whose language packs for German,
French and Spanish a GitHub runner does not have. Both workflows therefore run
`./dev test --filter "needs!=ocr"`, while `./dev test` locally runs everything. A test that stands
red on the build server for a reason that is not a defect gets ignored within two weeks — and the
ones next to it with it.

**Why the SDK target** `net8.0-windows10.0.19041.0` and not `net8.0-windows`: only that reaches
`Windows.Media.Ocr`. The price is Windows 10 build 19041 (May 2020) as the minimum. Whoever
changes the target checks **three** other places that carry the name in a path:
`.run/Publish Smurftown x64.run.xml`, `Setup/Setup.vdproj` (`SourcePath` on `apphost.exe`), and
this section.

**Mind the preview pins**: `Microsoft.Extensions.Logging 9.0.0-preview.5.24306.7`,
`Serilog.Extensions.Logging 8.0.1-dev-10391`, `Serilog.Sinks.File 5.0.1-dev-00972`. That grew,
it was not chosen. Do not lift them to stable unasked — that is its own task with its own test
run, not a drive-by fix. `System.DirectoryServices[.AccountManagement]` and `System.Management`
went out with the Windows users.

## Build, release, delivery

Entry point is `./dev` (from PowerShell and cmd: `.\dev.cmd`).

**The script builds natively, and there is no container path here**: WPF needs Windows MSBuild,
and a Linux container cannot build `net8.0-windows10.0.19041.0`. Everything therefore goes through
exactly one door rather than through none. The comment at the top of `./dev` says the same thing at
the place somebody reads it.

| Command | What it does |
|---|---|
| `./dev build` | `dotnet build -c Debug` |
| `./dev test` | `dotnet test` — extra arguments go through, e.g. `--filter` |
| `./dev publish` | single-file release into `dist/publish/` — framework-dependent, `win-x64` |
| `./dev release` | `publish` + `dist/Smurftown_{version}_win-x64.zip` + `dist/checksums.txt` |
| `./dev version` | prints the version from the `csproj` |
| `./dev version 2.0.1` | sets it in `csproj`, `app.manifest` **and** `Setup.vdproj` |
| `./dev notes` | prints the `CHANGELOG.md` section of the current version — `./dev notes 2.0.1` for another |
| `./dev clean` | removes `dist/` and the dotnet outputs |

**`./dev version` is the reason the command exists**: the number sits in three places, and those
have drifted apart before. It generates a fresh `ProductCode` and `PackageCode` and leaves the
`UpgradeCode` alone. Each of the three replacements aborts if it does not match **exactly once**; a
silent "no match" would be more expensive here than an abort.

**There is deliberately no `./dev run`.** Starting the app stays the user's step — it collides
otherwise with their running IDE instance. Likewise the user's steps: branch, `commit`, `tag`,
`push`, PR.

**Delivery goes through GitHub Actions, not from here.** A tag push starts
`.github/workflows/release.yml`; the run calls the same `./dev release` on `windows-latest` and
attaches ZIP and checksum to the release. No token lies on a machine, and neither `gh` nor a forge
MCP is needed.

**The body of that release comes out of `CHANGELOG.md`**, cut out by `./dev notes <tag>` and handed
over as `body_path`. That is not cosmetic: an installed copy that may not replace its own file opens
exactly this page instead of updating, so an empty body is a release nobody can read. `./dev notes`
aborts on a missing or empty section, which fails the run rather than publishing one.

The workflow compares, as its **first** step, the tag against `<Version>` from the `csproj` and
aborts on deviation, before anything is built or uploaded. Without that check an `.exe` carrying the
predecessor's number could land in a release named differently.

`build.yml` builds every branch push (`branches: ['**']`, so `main` too) with `./dev publish` — not
with `build`: a debug build would let a broken single-file publish surface only at tag push. Tags do
not match the branch filter, so there is no double run.

**`windows-latest` instead of `ubuntu-latest` is a named exception** to the runner standard. Its
justification ("GitHub meters self-hosted runner minutes") does not apply to GitHub-hosted Windows,
and on a **public** repo — which `tibbots/smurftown` is — standard runners of all operating systems
are free.

The step-by-step release procedure is the [`release-prep`](.claude/skills/release-prep/) skill.

### Release and installer

**What ships is a ZIP with the `.exe`, not an MSI.** The MSI was last built for 1.0.0. The reason is
not a decision but a missing prerequisite: `.vdproj` needs the VS extension *Microsoft Visual Studio
Installer Projects*, and it is not installed on the work machine. Without it **nobody** can build the
MSI, the user included.

What fell away with the MSI and nobody replaces today: start menu entry, uninstall via the control
panel, and the prerequisite that installs the .NET 8 desktop runtime. The ZIP brings a
framework-dependent `.exe` — without the runtime it does not start. The README says so.

The setup project stays in the repo anyway and is maintained by `./dev version`. Throwing it away
would be the decision never to build an MSI again, and that decision has not been made.

Everything relevant is in the `"Product"` block of `Setup/Setup.vdproj`:

- `ProductName` = `Smurftown`, `Manufacturer` = `ZrdJ`
- `UpgradeCode` `{D4E02593-…}` stays **stable** across all releases — otherwise the installer does
  not recognise the predecessor. `ProductCode` and `PackageCode` belong regenerated on every version
  bump, or `RemovePreviousVersions` / `DetectNewerInstalledVersion` do not engage cleanly.
- `InstallAllUsers = TRUE`, `TargetPlatform = x64`
- Prerequisite: `Microsoft.NetCore.DesktopRuntime.8.0.x64`, `FrameworkVersion = 8.0.6`
- Install target: `[ProgramFiles64Folder][Manufacturer]\[ProductName]` →
  `C:\Program Files\ZrdJ\Smurftown`. The manufacturer still points at the old org; changing it moves
  the installation path and is therefore a breaking change for existing installations, not a
  drive-by rename.

**Nothing is signed — and that was never otherwise.** Both configurations of the `.vdproj` stand on
`"SignOutput" = "11:FALSE"` with an empty `"CertificateFile"`.

This file used to claim the opposite ("signed with `signcert.pfx`"), and the README instructed users
to put `Smurftown.cer` into "Trusted Root Certification Authorities". That was doubly wrong.
**Ineffective**, because a certificate in the root store does nothing for an *unsigned* MSI — there
is no signature it could vouch for. And **harmful**, because a root certification authority can
vouch for anything on the machine: the user takes a real risk and gets not a single benefit in
return. The instruction is therefore gone from the README; in its place stands the truth —
SmartScreen warns, "More info → Run anyway".

`Smurftown.cer` (770 bytes) still lies in the repo but is referenced by nothing. The
`signcert.pfx` entry in `.gitignore` refers to a file that never existed. Both may go once somebody
decides — deleting is the only irreversible line of this cleanup and therefore did not happen in
passing.

Whoever really wants signing changes `SignOutput`, provides a `.pfx` **and** a timestamp server
(`TimeStampServer` is empty too — without it the signature becomes invalid as soon as the
certificate expires). That is its own task, not a drive-by switch.

## Working practice

**Worktree + PR, no direct commit on `main`.** Creating a branch, `add`/`commit`/`push` and opening
a PR are the user's steps, not the AI's. The AI edits, builds, **tests** and reports.

**Plan first, then the edit** — even for one-liners.

### Testing is the AI's job — the go-ahead is the user's

**The AI starts, operates and closes both applications itself** — Smurftown and Heroes of the
Storm. What it does **not** do is start or drive either of them unasked. A window coming up and
taking clicks seizes the machine the user is sitting at, and interrupting them costs more than the
round trip saves.

So the older rule is only half reversed. The user no longer *runs* the test — that cost a round trip
per change, and it had the AI report on something it had not seen. But the user still decides
**when** it runs, because it runs on their screen.

- **Ask before starting, ask before driving — per run, not once per session.** One sentence naming
  the application and what is about to happen to the screen, then wait for the answer. A "go" for
  one run is not a "go" for the next. Waived only while the user is demonstrably away, and only for
  as long as they are.
- **Test against a test folder, not against the real list.** `.\tools\test-home.ps1` creates one
  under `%TEMP%\smurftown-test-home`, fills it with the invented accounts of `tools/demo-data.yaml`,
  points `SMURFTOWN_HOME` at it and starts the app against it. Nothing clicked there reaches the
  real `data.yaml` — and `BattlenetAccountGateway` rewrites that file whole on **every** mutation,
  without a lock.
- **One instance at a time.** `drive-smurftown.ps1` and `capture-window.ps1` take the **first**
  Smurftown process that owns a window; with two of them up, which window gets clicked is a coin
  flip — and one of the two is showing the real list. `test-home.ps1` aborts when one is already
  running. If that one is the user's, ask before closing it.
- **The game is the loud one.** It takes over the screen and logs an account in, so the question
  before it says exactly that. Smurftown is a window among windows — it needs the go-ahead too, just
  not the warning.
- **Close what you started.** A stranded client keeps the account logged in, and the next run has to
  sign it out first. `test-home.ps1` prints the PID for precisely this.
- **Never photograph the real list.** The repo is public, the UI shows battletags and e-mail
  addresses in plain text, and GitHub keeps every version of an image permanently. Test captures go
  to the scratchpad and nowhere near `docs/images/`; README captures run against the test folder
  — see [`docs/security.md`](docs/security.md#readme-and-screenshots).
- **Drove the real instance anyway? Put the data back.** A test that ticks a region or renames an
  account leaves the file changed; compare against `~/.smurftown/backups/` afterwards and restore
  values **verbatim** rather than re-formatting them. With a test folder this is the exception — it
  used to be every run.
- **Report what the test showed, then ask.** Naming the steps is not the report — the result is.

The two drivers are [`drive-smurftown`](.claude/skills/drive-smurftown/) and
[`drive-hots`](.claude/skills/drive-hots/). Neither of them starts its application; starting stays a
separate, deliberate step — for Smurftown it is `tools/test-home.ps1` that takes it.

### After every XAML change, `./dev test` — and then start the app once

`./dev build` is **no** substitute, and this is the incident that says why. An inserted comment once
lost its opening `<!--`; the text then stood as content in the `StackPanel` of the tab bar. The
build reported `0 errors`, an XML parser would have stayed silent too — `-->` without `<!--` is
valid XML in text content — and even the BAML compiled. Only on loading it did WPF throw a
`XamlParseException`, and the app no longer started at all.

**That half is now a test.** `XamlLoadsTests` loads every compiled XAML the assembly ships, inside
one `Application` on an STA thread, and reports every file that fails rather than the first. It is
the cheapest guard in this repository and it runs in half a second.

**Starting the app is still the other half.** A test that loads the markup says nothing about a
layout that comes out wrong or a binding that silently finds nothing — those need eyes. What
changed is that the expensive failure no longer waits for somebody to remember the rule.

**What appears in the log afterwards is only a consequence**: the toast notifier needs an
already-shown window and throws in turn as soon as the error handler calls it. Always read the
**first** exception of a run, not the last.
