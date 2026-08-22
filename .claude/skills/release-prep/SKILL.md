---
name: release-prep
version: 1
description: Prepare and ship a Smurftown release — bump the version across csproj, app.manifest and Setup.vdproj with ./dev version, verify, tag and let GitHub Actions build and upload. Triggers on release, version bump, ship a version, tag a release, "neue Version", publish Smurftown, installer state, MSI.
---

# Preparing a Smurftown release

Delivery runs through GitHub Actions, not from a workstation. A tag push starts
`.github/workflows/release.yml`, which calls `./dev release` on `windows-latest` and attaches the
ZIP and the checksum to the release. No token lies on a machine, and neither `gh` nor a forge MCP
is needed.

## Who does what

| Step | Who |
|---|---|
| rename `## [Upcoming]` in `CHANGELOG.md`, `./dev version X.Y.Z`, verify, build, test | **Claude** |
| `git add` / `commit` / `tag` / `push` | **the user** |
| watching the workflow, checking the release | either |

Claude does not commit, tag or push. Prepare the change, verify it, then report what is ready and
let the user push.

## The procedure

```
1. ./dev version              read the current number
2. CHANGELOG.md               rename '## [Upcoming]' to '## [X.Y.Z] - YYYY-MM-DD',
                              add a fresh empty '## [Upcoming]' above it,
                              point the link definitions at the new version
3. ./dev version X.Y.Z        writes THREE files + fresh ProductCode/PackageCode
4. verify (below)
5. ./dev release              build the ZIP locally and confirm it works
6. user commits, tags X.Y.Z, pushes the tag
7. workflow compares tag against <Version> and aborts on mismatch BEFORE building
```

**Step 2 does not write the entries.** They were written in the pull requests that caused them,
under `## [Upcoming]` — releasing only renames the heading. If that section is empty at release
time, the honest answer is that nobody wrote them down and the release is not ready; do not
reconstruct them from `git log` and pretend otherwise.

**`./dev version` exists because the number sits in three places** and they have drifted apart
before:

| File | What carries the version |
|---|---|
| `Smurftown/Smurftown.csproj` | `<Version>` |
| `Smurftown/app.manifest` | the assembly version, under the name `MyApplication.app` |
| `Setup/Setup.vdproj` | `ProductVersion` |

Each of the three replacements aborts if it does not match **exactly once**. A silent "no match"
would be more expensive here than an abort, so never work around a failure by editing by hand —
find out why the pattern missed.

**It also regenerates `ProductCode` and `PackageCode` and leaves `UpgradeCode` alone.** That split
is load-bearing: the `UpgradeCode` `{D4E02593-…}` has to stay stable across all releases or the
installer stops recognising the predecessor, while `ProductCode`/`PackageCode` have to change per
version or `RemovePreviousVersions` / `DetectNewerInstalledVersion` do not engage cleanly.

## Verify before tagging

```bash
./dev version                                   # prints what is now in the csproj
./dev notes                                     # prints what the release page will say
grep -n 'Version' Smurftown/Smurftown.csproj
grep -n 'version=' Smurftown/app.manifest
grep -n 'ProductVersion\|ProductCode\|PackageCode\|UpgradeCode' Setup/Setup.vdproj
```

All three numbers identical, `UpgradeCode` unchanged, `ProductCode` and `PackageCode` different
from the previous release. **The tag must match `<Version>` exactly** — the workflow checks this
first and aborts before building, but catching it here saves a failed run and a deleted tag.

## What CI does

| Workflow | Trigger | Runs |
|---|---|---|
| `build.yml` | every branch push (`branches: ['**']`, `main` included) | `./dev publish` |
| `release.yml` | tag push | version check, `./dev notes <tag>` into the body, then `./dev release`, then upload |

`build.yml` uses `publish` and not `build` on purpose: a debug build would let a broken
single-file publish surface only at tag push. Tags do not match the branch filter, so there is no
double run.

**`windows-latest` is a named exception** to the workspace runner standard. WPF needs Windows
MSBuild; a Linux container cannot build `net8.0-windows10.0.19041.0`. On a public repo — which
`tibbots/smurftown` is — standard runners of all operating systems are free.

## What ships, and what does not

**A ZIP with the `.exe`, not an MSI.** The MSI was last built for 1.0.0. The reason is a missing
prerequisite, not a decision: `.vdproj` needs the Visual Studio extension *Microsoft Visual Studio
Installer Projects*, and it is not installed on the work machine. Without it nobody can build the
MSI, the user included.

What fell away with it and nobody replaces today:

- start menu entry
- uninstall via the control panel
- the prerequisite that installs the .NET 8 desktop runtime

The ZIP brings a **framework-dependent** `.exe` — without the runtime it does not start. The README
says so, in all four languages. If that ever changes, all four have to change.

## What the shipped app expects of a release

Since the update check exists, installed copies **read** the release rather than a human doing it.
Three things are now a contract, and all three are what `./dev release` already produces:

| What | Consequence of breaking it |
|---|---|
| the tag is the version, no `v` prefix — `2.0.1` | compared three-part and numerically; anything else is not seen as an update |
| **exactly one** `.zip` asset | a second one stops every installed copy from updating |
| `checksums.txt` lists that ZIP by name | nothing to verify against, the install aborts |
| the changelog carries a section for that tag | `./dev notes` aborts and the whole run fails — deliberately, see below |

The last row is the one that behaves differently from the other three: it fails **loudly**, in CI,
before anything is uploaded. That is the point. The release page is where an installed copy sends
somebody when it may not replace its own file, so a release with an empty body is one nobody can
read — better a failed run than that.

**The first three do not surface in CI.** A release with two ZIPs builds and uploads perfectly; only the
already-installed copies go quiet. If a release ever has to carry a second archive, change
`UpdateInstaller` in the same commit — the mechanics are in
[`../../../docs/self-update.md`](../../../docs/self-update.md#what-the-release-has-to-look-like).

The ZIP's file name is *not* part of the contract: it is searched, not constructed, so changing the
RID or dropping the version from the name is safe.

**Nothing is signed.** Both configurations stand on `"SignOutput" = "11:FALSE"` with an empty
`"CertificateFile"`. Do not tell users to install `Smurftown.cer` into their root store: for an
unsigned MSI it vouches for nothing, and a root CA can vouch for anything on the machine — real
risk, zero benefit. The honest line is the one in the README: SmartScreen warns, "More info → Run
anyway".

## Checklist

- [ ] `CHANGELOG.md` has a `## [X.Y.Z] - DATE` section with real entries, and a fresh empty
      `## [Upcoming]` above it
- [ ] `./dev notes X.Y.Z` prints that section and nothing else — this is the release page
- [ ] `./dev version X.Y.Z` ran and reported three successful replacements
- [ ] the three files carry the same number
- [ ] `UpgradeCode` unchanged, `ProductCode` + `PackageCode` fresh
- [ ] `./dev release` produces `dist/Smurftown_X.Y.Z_win-x64.zip` and `dist/checksums.txt`
- [ ] exactly **one** ZIP in `dist/`, and `checksums.txt` names it — the update check in every
      installed copy depends on both
- [ ] the README's stated requirements still match what ships
- [ ] handed to the user: the tag to set (`X.Y.Z`, no `v` prefix — the workflow compares literally)
