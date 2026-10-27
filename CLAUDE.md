# battledeck

- WPF-App (.NET 8, C#) fuer mehrere Battle.net-Accounts auf einem Windows-Rechner: startet Heroes of the Storm je Account, loggt ein, liest Rang, Helden und Waehrungen aus dem laufenden Spiel.
- Lokale App: kein Server, kein Konto, keine Telemetrie. Daten liegen im Home-Verzeichnis.
- Genau eine ausgehende Anfrage: stuendlicher Update-Check gegen `api.github.com`, ohne Schalter. Zweite Anfrage → vor dem Bauen hier und in `docs/security.md` benennen.

## Wissen

- Wie die App gebaut ist: `docs/`, Einstieg `docs/README.md`. Vor jeder Aenderung: `docs/changing-things.md`.
- Ablaeufe (Release, Screenshots, App oder Spiel bedienen, README-Uebersetzungen) → Skills unter `.claude/skills/`.
- `docs/` und diese Datei sagen, was ist, nicht was war. Vergangener Stand nur, wo er selbst die Warnung ist. Beispiel: `psexec` in `docs/security.md`.
- Aenderung, die ein User bemerkt → im selben PR Eintrag in `CHANGELOG.md` unter `## [Upcoming]`. Refactoring, Umbenennung, neues Dokument: kein Eintrag.

## Build

- Abweichend vom Provider: kein Devcontainer moeglich. `./dev` baut nativ, nur unter Windows (PowerShell/cmd: `.\dev.cmd`). Befehle: `./dev help`.
- Kein `./dev run`.
- Abweichend vom Provider: Branch, `commit`, `tag`, `push` und PR macht der User, nicht die KI. Die KI editiert, baut, testet und berichtet.
- Test, der etwas braucht, das der GitHub-Runner nicht hat → Trait, Beispiel `[Trait("needs", "ocr")]`. CI filtert `needs!=ocr`, lokal laeuft alles.
- Tests nie gegen das echte `~/.smurftown`. `SMURFTOWN_HOME` setzt der Modul-Initialisierer in `Battledeck.Tests` → nicht entfernen.
- Ziel `net8.0-windows10.0.19041.0` geaendert → auch `Battledeck.Tests/Battledeck.Tests.csproj`, `.run/Publish Battledeck x64.run.xml`, `Setup/Setup.vdproj` (`SourcePath` auf `apphost.exe`) und die Mindestversion in allen vier READMEs.
- Preview-Pins (`Microsoft.Extensions.Logging`, `Serilog.Extensions.Logging`, `Serilog.Sinks.File`) nicht ungefragt auf stabil heben. Eigene Aufgabe mit eigenem Testlauf.
- Nach jeder XAML-Aenderung: `./dev test` und die App einmal starten. `./dev build` ersetzt beides nicht.
- Log nach einem Absturz: die erste Exception des Laufs lesen, nicht die letzte.

## Release

- Release nur per Tag-Push `X.Y.Z` (ohne `v`) → `release.yml`. Ablauf, Versionsstellen, Installer, Signieren → Skill `release-prep`.
- Merge auf `main` baut nur (`build.yml`), veroeffentlicht nichts.
- `Setup/Setup.vdproj` bleibt, auch ohne MSI-Build. `Manufacturer` und `ProductName` nicht aendern: verschiebt den Installationspfad bestehender Installationen.
- `Smurftown.cer` und die Zeile `signcert.pfx` in `.gitignore` nicht nebenbei loeschen. Entscheidet der User.

## Testen

- Battledeck und Heroes of the Storm startet, bedient und schliesst die KI selbst, nie ungefragt.
- Vor jedem Start und jedem Bedienen fragen, je Lauf, nicht je Session: ein Satz mit Anwendung und was mit dem Bildschirm passiert, dann auf Antwort warten. Ausnahme nur, solange der User nachweislich weg ist.
- Beim Spiel sagt die Frage: es uebernimmt den Bildschirm und loggt einen Account ein.
- Battledeck nur ueber `.\tools\test-home.ps1` starten (Testordner mit Demo-Daten), nie gegen die echte Liste.
- Nur eine Battledeck-Instanz zur selben Zeit. Laeuft die des Users → vor dem Schliessen fragen.
- Gestartetes schliessen. `test-home.ps1` gibt die PID aus.
- Nie die echte Liste fotografieren. Test-Aufnahmen ins Scratchpad, nie nach `docs/images/` → `docs/security.md#readme-and-screenshots`.
- Doch die echte Instanz bedient → gegen `~/.smurftown/backups/{version}.zip` vergleichen, Werte woertlich zurueckschreiben. Archive nur ins Scratchpad entpacken, nie neben das Repo: sie enthalten die Passwoerter.
- Bericht nennt das Ergebnis des Tests, nicht die Schritte. Danach fragen.
