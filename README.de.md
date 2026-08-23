# Smurftown

[English](README.md) · **Deutsch** · [Français](README.fr.md) · [Español](README.es.md)

Verwalte alle deine Battle.net-Konten an einem Ort — und lass die App Heroes of the Storm für
ein Konto starten, dich anmelden und Rang, Helden und Währungen direkt aus dem laufenden Spiel
auslesen.

Nur für Windows. Kein Konto, keine Telemetrie, keine Daten über dich irgendwo außer auf deinem
Rechner: Alles liegt in `C:\Users\YOUR_USER\.smurftown`. Die App stellt **genau eine** Anfrage,
einmal pro Stunde — sie fragt GitHub, ob es eine neuere Version gibt. [Was das ist und was es nicht
ist](#updates).

![Die Kontenliste](docs/images/overview.png)

Eine Zeile je Konto und Region. **Rang, Helden, Gold, Splitter, Edelsteine und Beutetruhen in
dieser Zeile wurden nicht eingetippt — die App hat sie aus dem laufenden Spiel gelesen.** Alles,
was folgt, erklärt wie.

> Jeder Screenshot auf dieser Seite entstand mit erfundenen Demo-Konten. Kein Battletag und keine
> Adresse hier gehört irgendjemandem.

# Funktionen im Überblick

## Konten
* Battle.net-Konten anlegen und bearbeiten — eine Zeile pro Konto, sortier- und filterbar
* Zugangsdaten speichern und E-Mail oder Passwort mit einem Klick kopieren
* Konten, die du nicht mehr nutzt, archivieren statt löschen — **einen Löschen-Knopf gibt es
  nicht**, mit Absicht: Ein Fehlklick in einer Liste aus lauter gleich aussehenden Zeilen soll
  nicht der letzte Schritt sein
* Filtern nach Name, Spiel oder Held

![Das Zeilenmenü](docs/images/actions-menu.png)

Ein archiviertes Konto verschwindet nicht, es geht nur aus dem Weg. Der Umschalter in der
Filterleiste zeigt stattdessen diese Hälfte der Liste, und derselbe Knopf in der Zeile holt ein
Konto zurück.

![Das Archiv](docs/images/archive.png)
* Markieren, welche Spiele ein Konto spielt: Heroes of the Storm, Overwatch, World of Warcraft,
  Diablo
* **Die Regionen wählen, in denen ein Konto spielt.** Der Fortschritt in Heroes of the Storm hängt
  an der Region: Ein Konto, das sowohl in Europa als auch in Amerika spielt, hat zwei Ränge, zwei
  Heldensammlungen und zwei Goldstände. Jede Region, die du ankreuzt, bekommt ihre eigene Zeile,
  und der Regionsfilter schaltet zwischen ihnen um.

**Der Spielfilter ist eine Ansichtswahl, kein reiner Filter.** Wähl Overwatch, und jede Zeile
zeigt, was über Overwatch bekannt ist — heute ist das nichts, und genau das steht auch da, statt
etwas vorzutäuschen.

![Gefiltert auf Overwatch](docs/images/filter-game.png)

**Der Regionsfilter schaltet zwischen den Zeilen eines Kontos um.** Unten stehen dieselben
Battletags wie weiter oben, aber ihre Amerika-Seite: anderer Rang, andere Helden, anderes Gold.
`HALFMOONBAY` hat Amerika angehakt und wurde dort noch nie gelesen, deshalb zeigt es
Gedankenstriche statt Nullen — eine Null würde behaupten, das Konto besäße nichts, und das wissen
wir schlicht nicht.

![Gefiltert auf Amerika](docs/images/filter-region.png)

**Alles zu einem Konto steht in einem Dialog.** Der Battletag wird angezeigt, nicht eingetippt:
Er kommt aus dem Spiel, sobald das Konto zum ersten Mal gelesen wird.

![Der Konto-Dialog](docs/images/edit-account.png)

## Heroes of the Storm
* **Starten und anmelden.** Wähl ein Konto aus dem Start-Menü der Zeile — die App startet das
  Spiel, wählt die Region dieser Zeile und tippt die Zugangsdaten für dich ein. Alle drei
  Regionen funktionieren; das Spiel vergisst die Einstellung bei jedem Start und nach jedem
  Abmelden, deshalb setzt die App sie jedes Mal neu.

![Das Start-Menü](docs/images/start-menu.png)

  Die vier Einträge sind vier Aufgaben, nicht vier Wege zu derselben. *Spielen* startet das Spiel
  und hört dort auf — wer sich hinsetzt, um zu spielen, will nicht, dass die App die nächste
  Minute lang durch Menüs klickt. Die anderen drei lesen das Konto danach aus und unterscheiden
  sich nur darin, was danach passiert.
* **Das Konto automatisch auslesen.** Sturmliga-Rang und Division, offene Platzierungsspiele,
  Accountstufe, besessene Helden, Gold, Splitter, Edelsteine und ungeöffnete Beutetruhen —
  **alles direkt vom Spielbildschirm durch die App gelesen** und in den Datensatz der Region
  geschrieben, mit der du dich angemeldet hast. Nichts zu bestätigen, nichts von Hand zu
  übertragen; ein Toast danach nennt jeden Wert, der sich geändert hat.

  Genau das füllt den Reiter darunter. Du kannst weiterhin alles selbst korrigieren — nötig ist
  das aber selten, und ein Feld, das die App nicht lesen konnte, bleibt unangetastet, statt mit
  einer Vermutung überschrieben zu werden.

![Rang, Strafspiele und Helden, je Region](docs/images/edit-hots.png)

  Alles auf diesem Reiter gehört zu **einer** Region; der Umschalter oben sagt, zu welcher.
  Spielst du in zweien, pflegst du auch zwei.
* **Beutetruhen öffnen.** Öffnet zunächst jede ungeöffnete Truhe, sodass die Zahlen danach die
  nach dem Öffnen sind, nicht die davor.
* **Freie Rotation.** Die Rotation wiederholt sich nach einem jährlichen Kalender, und der liegt
  der App bei — keine Pflege, keine externe Quelle, nichts zum Nachladen.

![Die freie Rotation der aktuellen Periode](docs/images/rotation.png)

* **Nach Helden filtern.** Wähl einen oder mehrere, und die Liste behält jedes Konto, das
  **einen davon** besitzt — oder ihn diese Periode kostenlos spielen kann. Der Ring um jedes
  Portrait zeigt die Rolle des Helden, und das kleine Nexus-Zeichen markiert die, die gerade
  frei sind.

![Helden für den Filter auswählen](docs/images/hero-filter.png)

  Zwei Helden gewählt, sieben von fünfundzwanzig Zeilen übrig:

![Die Liste unter diesem Filter](docs/images/hero-filter-result.png)

* **Zähler für Strafspiele** je Konto, Linksklick zählt hoch, Rechtsklick runter — und wird
  zusammen mit allem anderen aus dem Spiel gelesen.

Gelesen wird alles, indem die App auf das Spielfenster schaut und den Text darauf erkennt. Kein
Speicherzugriff, keine Injection, keine API-Schlüssel, nichts, das Blizzards Server über ein
normales Login hinaus berührt.

## Was das Lesen braucht

Zwei Dinge an deinem Spielclient entscheiden, ob die App ihn lesen kann: **die Sprache seines
Textes** und **die Größe seines Fensters**. Beides steht hier vollständig, weil ein falscher Wert
bei beidem leise scheitert — nichts stürzt ab, nichts wird protokolliert, es wird einfach nichts
gelesen.

### Clientsprache

Heroes of the Storm bietet fünf Textsprachen unter **Optionen → Sprache und Region →
Textsprache** (die zweite Liste; die erste ändert nur die Sprachausgabe und spielt hier keine
Rolle). Die App vergleicht das Gelesene mit dem Wortlaut, den diese Sprache auf den Bildschirm
bringt:

| Textsprache im Spiel | Unterstützt |
|---|---|
| `Deutsch` | ✅ **ja** — der Standard, an dem alles gemessen wurde |
| `English (US)` | ✅ **ja** — Wort für Wort an einem laufenden Client geprüft |
| `Français` | ✅ **ja** — an einem laufenden Client gemessen, samt aller 16 abweichenden Heldennamen |
| `Español (ES)` | ✅ **ja** — an einem laufenden Client gemessen |
| `Español (AL)` | ✅ **ja** — gemessen; zehn Heldennamen weichen von der spanischen Fassung ab |

**Sag der App, welche der fünf du spielst** — Optionen → Sprache des Spiels. Heldennamen,
Rangstufen und Bildschirmbeschriftungen werden gegen den Wortlaut abgeglichen, den der Client
zeigt, also bedeutet eine falsche Einstellung, dass überhaupt nichts gelesen wird. Wird nichts
erkannt, wird auch nichts geschrieben: Die App lässt die Zahlen von gestern lieber stehen, als
sie durch etwas Falsches zu ersetzen.

> **Zwei Lücken außerhalb von Deutsch und Englisch.** Das Wort, das das Spiel zeigt, während
> Platzierungsspiele noch offen sind, wurde auf Französisch und Spanisch nicht gemessen, und von
> den Rangstufen wurde nur die geprüft, die das Testkonto gerade innehatte — der Rest ist die
> übliche Ranglisten-Reihenfolge und könnte danebenliegen. Wird ein Rang oder eine offene
> Platzierung auf diesen Sprachen nicht erkannt, liegt es daran; alles andere liest sich normal.

Für das beste Ergebnis installiere das Windows-Sprachpaket, das zu deiner Clientsprache passt.
Die Texterkennung nutzt, was Windows mitbringt; ohne das passende Paket fällt sie auf eine
andere Sprache zurück, was bei lateinischer Schrift noch funktioniert, bei Akzenten aber
unzuverlässiger wird.

Umgestellt wird **im Spiel**, nicht hier — und das braucht einen Neustart, dazu einen Download,
wenn du eine Sprache wählst, die noch nie installiert war.

![Optionen](docs/images/settings.png)

Einstellungen werden gespeichert, sobald du sie änderst; einen Speichern-Knopf gibt es in dieser
App nirgends. Im selben Reiter findet die App auch deine Heroes-of-the-Storm-Installation — sie
sucht von selbst an den üblichen Orten, und *Laufwerke suchen* ist für den Fall da, dass deine
irgendwo Ungewöhnliches liegt.

### Bildschirmauflösung

Die App merkt sich keine Koordinaten, sondern **Anker** — eine Kante oder eine Mitte, plus einen
Abstand davon — und skaliert diese Abstände mit der **Höhe** des Fensters. Die Breite entscheidet
nur, an welcher Kante ein Element hängt, deshalb verhält sich *jede* Breite bei gleicher Höhe
identisch.

| Auflösung | Lesen aus dem Spiel |
|---|---|
| 3440 × 1440 | ✅ **ja** — die Referenz, an der alles gemessen wurde |
| 2560 × 1080 | ✅ **ja** — gemessen |
| 1920 × 1080 | ✅ **ja** — gemessen |
| jede andere Höhe | ungetestet — vermutlich in Ordnung, aber niemand hat es geprüft |
| jede andere Breite bei 1440, 1080 | ✅ dasselbe wie die Zeile darüber, die Breite geht nicht in die Rechnung ein |

Fenstermodus und randloser Vollbildmodus funktionieren beide; die App misst den Clientbereich,
nicht den Fensterrahmen. **Remote Desktop dagegen nicht** — die Sitzung übernimmt die Auflösung
der Maschine, an der du sitzt, nicht die, auf der das Spiel läuft, und jede Messung kommt dadurch
falsch heraus.

## Updates

Einmal pro Stunde, solange sie geöffnet ist, fragt Smurftown bei GitHub nach, ob es einen
neueren Release gibt. Die Anfrage ist anonym und enthält nichts über dich, deine Konten oder
das, was du damit gemacht hast — es ist dieselbe Frage, die jeder an ein öffentliches
Repository stellen kann. Gibt es etwas Neueres, sagt das der Versions-Chip oben rechts; ein
Klick öffnet dies:

![Das Update-Angebot](docs/images/update-offer.png)

**Installieren** lädt den Release herunter, prüft ihn gegen die veröffentlichte SHA-256-Prüfsumme
und setzt ihn ein; die App startet sich selbst neu. Wo sie ihre eigene Datei **nicht** ersetzen
darf — eine Installation unter `Program Files`, ein Ordner ohne Schreibrecht, ein Build direkt aus
der Entwicklungsumgebung — öffnet der Knopf stattdessen die Release-Seite und nennt den Grund. Was
davon für deine Installation gilt, steht unter **Optionen → Über & Updates**.

**Die Prüfsumme beweist weniger, als sie aussieht.** Hash und Datei stammen aus demselben
Release über dieselbe Verbindung; sie beantwortet also eine Frage — ist das die Datei, die der
Release nennt — und nicht die andere: wer sie gebaut hat. Signiert ist hier nichts, siehe unten.

**Es gibt keinen Schalter, um die Prüfung abzustellen, und das ist Absicht.** Eine Einstellung,
die niemand findet, ist keine Zustimmung; ehrlich ist es, die Anfrage klar zu benennen — genau das
tut dieser Abschnitt. Wer gar keinen ausgehenden Verkehr will, sperrt die Anwendung in seiner
Firewall — die Prüfung scheitert dann stillschweigend, alles andere läuft weiter.

# Installation

Lad dir `Smurftown_<version>_win-x64.zip` von
[Releases](https://github.com/tibbots/smurftown/releases) herunter, entpack sie irgendwohin und
starte `Smurftown.exe`. Es gibt nichts zu installieren: Die App hält alles in
`C:\Users\YOUR_USER\.smurftown` und lässt den Rest deiner Maschine in Ruhe.

**Du brauchst die .NET 8 Desktop Runtime.** Hol sie dir von
[dot.net/download](https://dotnet.microsoft.com/download/dotnet/8.0) — *Desktop Runtime*, x64.
Ohne sie sagt Windows, dass die App nicht starten kann.

**Windows wird dich warnen.** Der Download ist nicht mit einem Zertifikat signiert, dem Microsoft
vertraut, deshalb zeigt SmartScreen *„Windows hat den Computer geschützt"*. Wähl **Weitere
Informationen** → **Trotzdem ausführen**.

Jedes Release bringt außerdem eine `checksums.txt` mit. Um zu prüfen, was du heruntergeladen
hast, in PowerShell:

```powershell
Get-FileHash .\Smurftown_1.0.0_win-x64.zip -Algorithm SHA256
```

Voraussetzungen:

| | |
|---|---|
| Windows | 10, Build 19041 (Mai 2020) oder neuer — die App nutzt die in Windows eingebaute Texterkennung |
| Runtime | .NET 8 Desktop Runtime, x64 — **installierst du selbst**, siehe oben |
| Rechte | normaler Benutzer — **keine Administratorrechte** |

# Roadmap
* Mehrere Konten nacheinander durchlaufen, mit Pausen zwischen den Anmeldungen und Abbruch beim
  ersten Fehlschlag
* Eine Zwei-Faktor-Abfrage behandeln, statt in den Timeout zu laufen
* Kontodetails für Overwatch, World of Warcraft und Diablo — heute zeigen diese Zeilen nur, dass
  das Spiel angehakt ist

# FAQ

### Wo kann ich die App herunterladen?
Von [Releases](https://github.com/tibbots/smurftown/releases).

### Sendet oder empfängt diese App Daten von einem Server im Internet?
Einmal pro Stunde fragt sie bei `api.github.com` nach, ob es einen neueren Release gibt — anonym und
ohne irgendetwas über dich oder deine Konten in der Anfrage. Nimmst du das Angebot an, lädt sie
diesen Release ebenfalls von GitHub. Das ist der gesamte Verkehr, den diese App von sich aus
erzeugt; siehe [Updates](#updates). Alles andere passiert auf diesem Rechner, und das Einzige, was
ihn sonst verlässt, ist die eigene Anmeldung des Spiels, eingetippt in die eigene Anmeldemaske des
Spiels.

### Wo werden meine Daten gespeichert?
Ausschließlich in lokalen Dateien, im Ordner `.smurftown` in deinem Benutzerverzeichnis
(`C:\Users\YOUR_USER\.smurftown`). Deine Kontenliste liegt in `data.yaml`.

**Passwörter werden im Klartext gespeichert.** Das macht Kopieren und automatisches Eintippen
erst möglich, und es ist ein bewusster Kompromiss dieser App — behandle den Ordner wie den
Passwort-Speicher, der er ist.

### Warum steht ein Konto mehrfach in der Liste?
Das sind seine Regionen. Ein Konto bekommt eine Zeile je Region, in der es spielt, weil Rang,
Helden und Währungen sich zwischen ihnen unterscheiden — derselbe Battletag kann in Europa Platin
sein und in Amerika Bronze. Das Abzeichen `EU`, `AM` oder `AS` neben dem Battletag sagt, welche
Zeile welche ist, und der Regionsfilter in der Filterleiste zeigt jeweils eine Region.

### Woher weiß ich, dass hier nicht gelogen wird?
Das weißt du nicht. Lies den Quellcode und entscheide selbst.

### Warum warnt Windows mich, wenn ich sie starte?
Weil die ausführbare Datei nicht mit einem Code-Signing-Zertifikat signiert ist — eines, dem
Microsoft vertraut, kostet Geld, das dieses Projekt nicht hat. Die Warnung ist ehrlich: Windows
kann tatsächlich nicht sagen, wer die Datei gebaut hat. Wenn dich das stört, bau sie selbst aus
dem Quellcode — `.\dev.cmd release` erzeugt dieselbe ZIP wie das Release.

### Warum muss sie das Spielfenster sehen?
Weil das der einzige Ort ist, an dem die Daten existieren. Blizzard bietet keine öffentliche
Schnittstelle für Heldenbesitz, Rang oder Währungen, also öffnet die App die passenden Schirme,
macht ein Bild davon und liest den Text darauf — genau so, wie du es tun würdest, nur schneller
und ohne selbst zu tippen.

### Braucht sie Administratorrechte?
Nein, ein gewöhnliches Benutzerkonto reicht. Heroes of the Storm bringt seine eigene Anmeldemaske
mit, wenn man es direkt startet, deshalb muss die App nie etwas außerhalb deines
Benutzerverzeichnisses anfassen.
