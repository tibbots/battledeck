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

[Upcoming]: https://github.com/tibbots/smurftown/compare/1.1.0...HEAD
[1.1.0]: https://github.com/tibbots/smurftown/releases/tag/1.1.0
[1.0.0]: https://github.com/tibbots/smurftown/releases/tag/1.0.0
