# Security model and the public repo

Two things that look unrelated and are the same question: what may leave this machine.

The first half is the price of the functionality — the app is a password manager, and it types the
password into a game. The second half is a fact about the repo: `tibbots/smurftown` is **public**,
and the UI shows battletags and e-mail addresses in plain text.

## Deliberate trade-offs

This is not an oversight but the price of the functionality. Do not rebuild it "for security
reasons" without asking, but do not make it worse either:

- **Battle.net passwords are stored in plain text** in `data.yaml`. No DPAPI, no encryption. The app
  is explicitly a password manager.
- **The password is typed into the game** (`SendInput`, Unicode events). It is therefore briefly in
  the system input stream. What it does **not** do: land in a process command line or in the Serilog
  log. Both were the case with the old `psexec` path and fell away with it — do not reintroduce.
- **The password goes to the clipboard** (the "Copy password" entry in the row menu). It stays there
  until something else overwrites it, and every running application can read it — Windows cleans up
  nothing. The price for getting at the stored password at all: the app can only type it itself for
  Heroes of the Storm. The error path deliberately logs only the battletag, never the value.
- **Captures on failure** (`~/.smurftown/shots/`) show the game screen. On the login form the
  password is masked, but the email address is legible. The newest 20 are kept and nothing older
  than 30 days — a bound on how long that address lies around, not a reason it may be shared.
- **Every backup holds the passwords too.** `backups/{version}.zip` contains a complete
  `data.yaml`, so up to ten copies of the credential file live beside the current one. The ZIP
  carries no password and is not meant to: it makes the folder countable, it protects nothing.
  Whoever treats `~/.smurftown/` as a password store has to treat `backups/` as one as well.

Fell away with the removal of the Windows users: the Windows password equal to the user name, the
administrator rights (`app.manifest` stands on `asInvoker`), and `psexec` with the password in the
command.

If you work at one of these places: do not silently "harden", but name the effect and let it be
decided.

## The update check

**This application makes exactly one request, and it used to make none.** Once an hour it asks
`api.github.com` for the newest release of the public repository; if one is newer than what is
running, it can download and install it. How that works is in
[self-update.md](self-update.md) — what it costs is here.

**What leaves the machine**: a URL and a user agent of the form `Smurftown/2.0.1`. The request is
anonymous — no token, no account, no identifier, and nothing at all about the account list. The
version in the user agent is the only thing about the installation it reveals, and it reveals it to
GitHub, who serve the download anyway.

**What comes back and gets executed**: a `.exe` out of a release ZIP. That is the part worth being
precise about.

- **The SHA-256 is verified** against the `checksums.txt` of the same release. That answers one
  question — *is this the file the release says it is* — and no other.
- **Nothing is signed.** `Setup.vdproj` stands on `SignOutput = FALSE` with an empty certificate,
  and it always has. **So the trust anchor is HTTPS to github.com and the account behind
  `tibbots/smurftown`, not a signature on the file.** Whoever gets write access to that repository
  can ship an `.exe` that this application will install without asking again. The checksum does not
  change that and cannot: it is published by the same party as the file.
- **The install is a click, never automatic.** The check finds; the human decides. An update that
  installed itself would take that last decision away, and it is the only one left.

**Where it cannot install, it does not try.** A build out of the IDE and a write-protected
installation folder both fall back to opening the release page in a browser — see the route table
in [self-update.md](self-update.md#when-the-app-may-replace-itself-and-when-it-may-not). The app
never asks for elevation to get around a folder it may not write.

**There is no switch for it**, and that is a decision rather than an omission. One existed for half
a day and went again: the delivery has no other way of reaching anybody — a ZIP without an
installer and without a start menu entry tells nobody that a version exists — so the setting would
have been found by almost nobody and would have cost the update to whoever did find it and forgot.
What replaces it is this paragraph: the request is **stated** instead of suppressible, precisely so
that nobody has to discover it. Whoever does not want it blocks `api.github.com` for this
executable, which is a decision at the level where such decisions belong.

**A second outbound request would be a change to what this application is.** There is exactly one
today, and the sentence in [`../CLAUDE.md`](../CLAUDE.md) says so. Whoever adds another names it in
both places before building it — not afterwards.

## README and screenshots

The README is the public landing page and exists in four languages: `README.md` (English, the
source) plus `README.de.md`, `README.fr.md`, `README.es.md`. A language bar at the top of each links
the others. **English is the source** — a change goes there first, the three translations follow.
Procedure: [`readme-translations`](../.claude/skills/readme-translations/).

**The screenshots are taken against demo data and never against the real list.** The reason is not
caution but a fact about the repo: `tibbots/smurftown` is **public**, and the UI shows battletag and
email address in plain text. A picture of the real list publishes 27 battletags and 27 addresses,
and does so **permanently**: GitHub keeps every version of an image in the history, including one
replaced later.

`tools/capture-run.ps1` enforces this itself and aborts as soon as `data.yaml` holds a single
address not ending in `@example.com` — the `data.yaml` of the folder the app is **actually** running
against, which with `SMURFTOWN_HOME` set is not the one under `%USERPROFILE%`. Vouching for a file
nobody is photographing would be worse than not checking. That is the only safeguard which does not
depend on somebody remembering.

**The AI starts and operates the app — once the user has said go.** Starting a window and sending
it clicks seizes the machine the user is sitting at, so the go-ahead comes first, and it is a
go-ahead for this run rather than a standing one.

What gets started is a **test instance**: `tools/test-home.ps1` puts the demo accounts into a folder
under `%TEMP%` and points `SMURFTOWN_HOME` there, so the real list is not moved aside for the shot
— it is simply never opened. `drive-smurftown.ps1` still starts nothing itself; it aborts if no
instance is running, so that driving and starting stay two steps which fail separately.

The full procedure, the shot list and what cannot be captured with demo data are in the
[`readme-screenshots`](../.claude/skills/readme-screenshots/) skill.
