# Changelog

Everything a user of Smurftown would notice from one release to the next.

The shape is [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), the numbering is
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions carry **no `v` prefix** —
`1.0.0`, not `v1.0.0`. The tag, `<Version>` in `Smurftown.csproj` and the update check in every
installed copy compare that string literally, so a prefix here would be a prefix everywhere.

**An entry is written in the pull request that causes it**, under `## [Upcoming]`. Releasing
renames that heading to the version and the date; it does not fill it. A changelog written on the
day of the tag is a `git log` with extra steps.

**What belongs here is what a user sees**: a new capability, a changed behaviour, a fixed bug, a
security-relevant trade-off. Refactorings, renamed classes and new documentation do not — those are
in `git log`, which is where somebody looks for them.

`./dev notes X.Y.Z` cuts out exactly one section; `.github/workflows/release.yml` hands it to the
GitHub release as its body. The section headings are therefore load-bearing: `## [X.Y.Z] - DATE`,
nothing else on the line.

## [Upcoming]

### Changed

- **Four of the files in `~/.smurftown/` have become one.** `settings.yaml`, `rotation.yaml`,
  `update.yaml` and `version.txt` are now sections of a single `app.yaml`; the account list stays in
  `data.yaml`. Nothing is lost and nothing has to be re-entered — the first start after the update
  carries the old files over and removes them, and the backup taken just before it holds them as
  they were. Both files now also record which layout they are written in, so a future change to the
  format has something to hold on to instead of having to be guessed from the content.
- **A setting saved while the app is running can no longer be overwritten by the update check.**
  Both write to the same file now, so each write reads the whole file again first and replaces only
  its own part of it. Within one running Smurftown that is now guaranteed rather than likely. What
  it still cannot do is protect against a **second** Smurftown running at the same time — that
  remains what it always was: the last one to write wins.
- **A file written by a newer version of Smurftown is read but never written back.** An older build
  does not know every field a newer one stores, and writing the file back would quietly delete them.
  It now says so instead and asks for the newer build.
- **The log no longer grows without end, and it has moved.** It now lives in
  `~/.smurftown/logs/`, starts a new file every 10 megabytes and keeps five of them; all but the
  one being written are compressed, so the folder stays somewhere around fifteen megabytes instead
  of growing for as long as the application is installed. Until now everything went into a single
  `smurftown.log` next to the account list, and that file had a limit nobody would have guessed:
  at one gigabyte it stopped being written to, silently. The log of the previous layout is not
  thrown away — it is compressed into the new folder on the first start.
- **The backup taken before an update is now one archive per version** instead of a folder,
  `backups/1.2.0.zip` rather than `backups/1.2.0/`. Existing folders are converted on the first
  start. Ten of them are kept, and old ones now actually go — until now every version added one and
  nothing ever removed any. **What that does not change**: an archive still holds a complete copy of
  the account list, passwords in plain text and all. It is tidier, not safer.
- **Screenshots of a stranded game run are cleared out.** `shots/` keeps the newest twenty and
  nothing older than thirty days. One of those images is a full screen and around five megabytes,
  and nothing had ever deleted one.

## [1.2.0] - 2026-08-23

### Added

- **Behind a dialog the window is now blurred, not only dimmed.** What stands underneath is still
  recognisable as your list, but no longer readable — so the eye stays on the dialog that is asking
  you something. The window comes back the moment the dialog closes, and it does so even when the
  dialog closes because something went wrong: until now an error inside a dialog left the window
  dimmed for the rest of the session, with nothing to click that would restore it.
- **The chip for a running client now offers to open the loot chests as well.** Clicking it opens a
  small menu with two entries: *refresh data*, as before, or *open the loot chests and refresh*,
  which empties every unopened chest first and reads afterwards — so shards, gold and any hero out
  of a chest are already in the numbers that get stored. It was the one thing the chip could not do
  while an account row could; both now offer it, and neither signs the client out for it.

### Changed

- **The update check runs once an hour instead of once a day, and it keeps running while the
  application is open.** Until now it happened at start and nowhere else, so a window left open
  since the morning showed the state it had at the moment it opened. Nothing else about it changed:
  it is still one anonymous request to `api.github.com`, still carries nothing about you or your
  accounts, and still cannot be switched off. A version found in the middle of a session changes
  the chip in the header and says nothing else — no message, no dialog.

### Fixed

- **Unopened loot chests were counted as none, and opening them stopped early while reporting
  success.** The badge on the loot tab is small, and it was the one place read without
  magnification — which the text recognition does not manage at that size. Three digits it still
  managed, so the fault appeared only once the number had fallen below a hundred, and from then on
  it stayed: the count read 0, and opening therefore had nothing left to do.
- **A count between one and nine is now recognised as "some", even when the number itself cannot
  be read.** A single digit is something the text recognition does not return at all — not at any
  magnification, and not from any crop. Smurftown therefore no longer asks it whether the badge
  shows a number, but whether the badge is there: present means chests are waiting, absent means
  none are. The number is still read whenever it can be, and the account keeps its previous value
  instead of dropping to zero when it cannot. Opening runs to the end either way, because it now
  stops when the badge disappears rather than when the counter reads nothing.

  **Whoever ran the opener and saw it finish early should let it run once more** — the chests it
  skipped are still there, and the stored count corrects itself with them.
- **A chest run that loses track now stops and says so.** One round opens one chest, so a counter
  that drops by more is a misreading and not progress. Until now such a jump was booked as chests
  opened, which is how a run that opened 21 of 65 could report all 65.
- **The password in the account dialog came out backwards.** The cursor jumped back to the front
  after every keystroke, so a password typed as `secret` was stored as `terces` — and there is no
  way to read it back on screen, so it was noticed at the login that failed with it. Whoever has an
  account whose password was typed in that dialog should check it.

## [1.1.0] - 2026-08-23

### Added

- **Refresh from a client that is already running.** While Heroes of the Storm is up, a chip
  appears in the header of the window. It reads the account that is signed in — rank, placement
  status, account level, heroes, gold, shards, gems, loot chests and leaver penalty — and writes it
  into that account's record. **It signs nobody out and it closes nothing**: the client is left
  exactly as it was found, and afterwards it stands on the ARAM screen, ready to play. Until now
  refreshing meant starting the game from a row, which signed the running account out first.
- The app finds out **by itself** which account is signed in, by reading the battletag off the
  profile. If no account in Smurftown carries that battletag, nothing is written and the chip says
  so.
- **Which region the client is playing in is asked** when the answer is not already known. The game
  shows it on none of its screens, and rank and heroes are stored per region — so an account that
  plays in one region is refreshed without a question, and one that plays in several is asked
  before anything is read. Cancelling that question writes nothing.
- **A small window walks you through it.** Heroes of the Storm only takes clicks while it is in
  front, and no application can put it there from the outside — so the window asks you to, waits
  until it actually is, and then shows each step as it happens: which account was found, which
  region, how much of the collection has been read. The region question is asked in that same
  window rather than in one of its own. It stays open when the run is over, so there is something
  to read when you come back from the game.
- **A click on a row opens the edit dialog**, and the pointer turns into a hand to say so.
  Everywhere on the row except the rank medal, the hero strip and the two round buttons, which
  have gestures of their own.
- **A click on the rank medal opens the rank grid right there** — the same 28 medals the dialog
  shows, and the pick is written straight into that row's region. That is two clicks instead of the
  five it used to take to correct a rank the game was misread on. A rank set by hand does not
  pretend to have been read: the timestamp under the name stays where it was.
- **A click on the hero strip opens the edit dialog on the Heroes of the Storm tab**, where the
  hero picker is.

### Changed

- **The edit dialog opens on the region of the row it was opened from.** It used to always start on
  the account's first Heroes of the Storm region, so editing an Americas row showed the European
  rank and hero list.
- **Only one game run at a time.** Starting an account from a row while another run is in progress
  used to be possible and produced two flows clicking into the same client, each landing on
  whatever screen the other had just opened. It is now refused with a message.

## [1.0.0] - 2026-08-22

First public release.

### Added

**Accounts**

- One row per Battle.net account **and region**, because rank, heroes and currencies differ
  between them — the same battletag can be Platinum in Europe and Bronze in the Americas.
- Filters for name, game, hero and region; the region filter switches between the rows of one
  account rather than hiding them.
- Stored login credentials, with one click to copy e-mail or password.
- Archiving instead of deleting. An account moves out of the list and can come back; **there is no
  delete button**, so one misclick in a list of look-alike rows cannot be the last step.
- Per-account game flags for Heroes of the Storm, Overwatch, World of Warcraft and Diablo. A game
  with nothing known about it says so instead of showing zeroes.

**Heroes of the Storm**

- Start the game for an account: the app launches it, selects the region of that row and types the
  credentials. All three regions, and the setting is applied on every start because the game
  forgets it.
- Read the account off the game screen — Storm League rank and division, pending placement matches,
  account level, owned heroes, gold, shards, gems and unopened loot chests — and write it into the
  record of the region signed in to. A field that could not be read is left alone rather than
  overwritten with a guess.
- Open all unopened loot chests before reading, so the numbers that follow are the ones after
  opening.
- Free hero rotation from a yearly calendar shipped with the app. Nothing to fetch, nothing to
  maintain.
- Leaver penalty counter per account, adjustable by hand and read out of the game with everything
  else.

**Reading the game**

- Text recognition through `Windows.Media.Ocr`, matched against the vocabulary of five client text
  languages: `Deutsch`, `English (US)`, `Français`, `Español (ES)`, `Español (AL)`. Which one the
  client runs is a setting; a mismatch means nothing is read, and nothing is then written.
- Anchors instead of coordinates, scaled by the window height. Measured at 3440 × 1440,
  2560 × 1080 and 1920 × 1080; any width at those heights behaves identically. Windowed and
  borderless fullscreen both work, Remote Desktop does not.
- Automatic discovery of the Heroes of the Storm installation, plus a scan across all drives for
  the unusual case.

**The application itself**

- Four UI languages — German, English, French, Spanish — switchable without a restart, and
  independent of the client language.
- Everything in `%USERPROFILE%\.smurftown`, `data.yaml` as the account list. No account, no
  telemetry.
- A copy of every YAML file into `backups/{version}/` before the first run of a new version
  migrates anything.
- A daily check against `api.github.com` for a newer release — anonymous, carrying nothing about
  the human or their accounts, and the only request this application makes. It can install the
  release itself, verifying it against the published SHA-256 first; where it may not replace its
  own file, it opens the release page and says why.

### Security

- **Passwords are stored in plain text.** That is what makes copying and typing them possible, and
  it is the deliberate trade this application makes. The folder is a password store and wants
  treating like one.
- **Nothing is signed.** SmartScreen warns on the download, and the honest answer is *More info →
  Run anyway* — the checksum shipped with each release proves that the file matches the release,
  not who built it.

[Upcoming]: https://github.com/tibbots/smurftown/compare/1.2.0...HEAD
[1.2.0]: https://github.com/tibbots/smurftown/releases/tag/1.2.0
[1.1.0]: https://github.com/tibbots/smurftown/releases/tag/1.1.0
[1.0.0]: https://github.com/tibbots/smurftown/releases/tag/1.0.0
