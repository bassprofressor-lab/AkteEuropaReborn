<div align="center">

# Akte Europa Reborn

**Das Echtzeit-Strategiespiel *Akte Europa* von 1997, neu gebaut in Godot 4 und C#.**

[![Release](https://img.shields.io/github/v/release/bassprofressor-lab/AkteEuropaReborn?label=herunterladen&style=for-the-badge&color=2b7)](../../releases/latest)
[![Discord](https://img.shields.io/badge/Discord-mitreden-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/MVMfRWrMKv)
[![Webseite](https://img.shields.io/badge/openreborn.com-besuchen-orange?style=for-the-badge)](https://openreborn.com/de/)
[![Lizenz](https://img.shields.io/badge/Lizenz-GPL--3.0-blue?style=for-the-badge)](LICENSE)

*[English version](README.md)* · *[Änderungen](CHANGELOG.de.md)* · *[Offene Fragen](OFFENE_FRAGEN.md)*

</div>

---

> **Es wird kein einziges Byte des Originals mitgeliefert.** Der Installer
> enthält die Engine und sonst nichts. Beim ersten Start liest er die beiden
> Original-CDs, die man selbst besitzen muss, und leitet auf dem eigenen Rechner
> alles ab: Karten, Einheitengrafiken, Schriften, Tabellen, Briefings, Klang.
> Die mitgelieferte `.pck` ist rund 500 KB groß.

![Eine Landung in Mission 5](docs/screenshots/04-mission.png)

---

## Herunterladen

**[→ Den Installer von der Releases-Seite holen](../../releases/latest)**

Ausführen, beim ersten Start auf die CDs zeigen und ein paar Minuten warten,
während der Inhalt entsteht. Die abgeleiteten Dateien landen im Benutzerprofil;
das Programmverzeichnis bleibt unberührt.

## Wie es aussieht

Die Missionsbriefings erscheinen auf dem Bildschirm des Originals — das Bild
steckt in `BRIEFG.DAT`, der Text in `BRIEFG.TXT`, die Schrift ist die des
Spiels. Davor laufen die Vorfilme, aus dem eigenen Videoformat des Originals
entschlüsselt.

![Missionsbriefing](docs/screenshots/01-briefing.png)

Die Karten werden in voller Größe gebacken, bis 10160 × 5285 Pixel. Hier „The
Dam", herausgezoomt, mit Bahnlinien, Strommasten und der Staumauer:

![Kartenübersicht](docs/screenshots/03-uebersicht.png)

Eine Basis mit Seitenpanel, Rohstoffleiste und Übersichtskarte:

![Basis](docs/screenshots/02-basis.png)

## Stand

Spielbar sind Gefechte und die **Kampagne mit 33 Missionen**. Aus den zwei
Discs entstehen beim ersten Start:

| | |
|---|---|
| Karten | 44, gebacken bis 10160 × 5285 Pixel |
| Spielstände | 44 (Einheiten, Gebäude, Ziele, Vorkommen, Schienen …) |
| Einheitenbilder | 4307, aus `ROBO.CWR` zusammengesetzt |
| Tabellen | 12, aus den Kartendateien und `GAME.EXE` |
| Filme | 35, aus dem eigenen Behälter des Originals entschlüsselt |
| Oberfläche | Originalschrift, Seitenpanel, Effekte, Missionsbriefings |

Die Simulation rechnet mit den Zahlen des Originals: Preise, Lebenspunkte,
Angriff und Verteidigung, Reichweite, Nachladezeit, Geschwindigkeit, Sprit und
Munition stammen aus den Sätzen des Spiels und nicht aus geratenen Konstanten.
Wo etwas nicht zu bergen war, sagt der Code das — jede eigene Setzung ist als
solche gekennzeichnet.

## Road to 0.7.0

**0.7.0 kommt heraus, wenn jede der 33 Kampagnenmissionen einzeln durchgespielt
und für sauber befunden ist.** Nicht früher.

Der Grund ist eine Erfahrung aus diesem Projekt, und sie war teuer: **eine
einzige aufmerksam gespielte Mission hat mehr Fehler gefunden als über hundert
automatische Prüfstände zusammen.** Mission 1 allein brachte an einem Abend
über zwanzig Befunde — von der Parteifarbe über fehlende Trümmer und Explosionen
bis zu Einheiten, die im Boden verschwanden. Prüfstände messen, was man sie zu
messen gebeten hat; ein Mensch am Bildschirm sieht auch das, wonach niemand
gefragt hat.

### Wie das abläuft

Für jede Mission, der Reihe nach:

1. **Spielen**, mit einem Erwartungsblatt aus dem ausgelesenen Missionsskript
   daneben — welche Hilfefenster kommen sollten, welche Untermissionen es gibt,
   was die Siegbedingung wirklich verlangt.
2. **Jede Unstimmigkeit melden**, auch die kleinste: ein Geräusch am falschen
   Ort, eine Kachel, die im Nebel nicht stimmt, ein Gegner, der nicht kommt.
3. **Im Original nachlesen**, nicht raten. Jede Behebung bekommt die Adresse in
   `GAME.EXE`, an der sie steht — und wo wir etwas selbst gesetzt haben, ist es
   im Code als *unsere Setzung* markiert.
4. **Messen statt behaupten.** Zu jeder Behebung gehört eine Zahl und ein
   Nullmodell, und zu jedem Schalter eine Gegenprobe, die den alten Zustand
   wiederherstellt.
5. **Erst dann die nächste Mission.**

### Woran man den Fortschritt sieht

Was dabei gefunden und behoben wird, steht vollständig in
**[CHANGELOG.de.md](CHANGELOG.de.md)**; was offen bleibt oder eine Zuarbeit
braucht, in **[OFFENE_FRAGEN.md](OFFENE_FRAGEN.md)** — samt der Fälle, in denen
sich eine frühere Erklärung von uns als falsch herausgestellt hat. Beides wird
mitgeführt, nicht nachträglich geschrieben.

⭐ **Stand heute:** Mission 1 und 2 sind mehrfach durchgespielt; Mission 2 ist
seit dem 30.08.2026 zum ersten Mal abschliessbar. Aus diesen zwei Missionen
allein stammen mehr als vierzig belegte Behebungen — darunter das
Brückengeländer im Nebel, die sieben Angreifer, die wegen eines Absturzes in
der Wegsuche nie kamen, und ein Gruppenbefehl, der eine Einheit ohne Ziel und
ohne zweiten Versuch stehenliess.

**Mitmachen ist ausdrücklich erwünscht.** Wer eine Mission spielt und etwas
bemerkt, das sich im Original anders anfühlt, sollte es auf
**[Discord](https://discord.gg/MVMfRWrMKv)** sagen — auch und gerade, wenn es
sich um eine Kleinigkeit handelt. Genau daran hängt der Fortschritt.

## Was als Nächstes kommt

- Mehrspieler über das Netz (im LAN läuft der Lockstep bereits)
- Der Karteneditor, fertig
- Ein Gefechtsmodus für den Wettkampf
- Umschalter Deutsch/Englisch
- Ein „Reborn"-Grafikmodus, umschaltbar gegen die Originalbilder
- Wiki

Darüber reden wir auf **[Discord](https://discord.gg/MVMfRWrMKv)**.

## Voraussetzungen

* Die beiden Original-CDs von *Akte Europa* (im Laufwerk oder als Ordner)
* Windows; Bauten für andere Systeme sind noch nicht eingerichtet

## Selber bauen

Gebraucht werden Godot 4.7 (Mono/.NET) und das .NET-SDK.

```bash
dotnet build "Akte Europa Reborn.csproj"
godot --path . --headless --export-release "Windows Desktop"
```

Inhalte ohne Oberfläche einlesen:

```bash
godot --path . --headless -- --import-cd          # CDs im Laufwerk
godot --path . --headless -- --import=<Ordner>    # oder aus einer Installation
```

## Selbsttests

Die Leser für die Originalformate sind nicht nur gebaut — sie werden gegen die
Daten geprüft, die sie lesen:

```bash
godot --path . --headless -- --selftest-cwp=<Werkzeugordner>
godot --path . --headless -- --selftest-designs
godot --path . --headless -- --selftest-briefings
```

Darunter: 13 Einzelbilder pixelgleich gegen eine zweite Umsetzung, 26
Kartendateien restlos aufgebraucht, 4147 Einheitenbilder identisch, 601
Entwurfssätze exakt reproduziert, 33 Missionstexte Zeichen für Zeichen.

Über die Leser hinaus werden die *Regeln* im laufenden Spiel geprüft — die
Kampagne gegen beide Originalfassungen (33 Missionen, keine Abweichung), der
Takt, die Wirtschaft, das Bahnnetz, die Transporter, Gruppen, Spielstände. Jeder
Prüfstand druckt Zahlen, und jeder kann rot werden.

## Wie dieses Projekt arbeitet

Jede Mechanik wird aus dem Originalprogramm gelesen, bevor sie gebaut wird —
auch dort, wo Erfinden schneller ginge. Eine Lesart gilt erst, wenn eine Zahl
dahintersteht: über alle Karten gezählt, gegen eine zweite Umsetzung gehalten,
im laufenden Spiel gemessen. Was aus den Daten kommt und was von uns, wird
getrennt gehalten und im Code benannt. Fehldeutungen werden **zurückgezogen**
statt überschrieben.

Die Dateiformate sind dabei eines nach dem anderen aufgegangen: CWP (Kacheln
und Objekte), CWM (Karten mit über 130 Abschnitten), CWR (Einheitenbilder), CWD
(Schriften), CWA (Effekte), RPL (Filme), das InstallShield-Kabinett der CD und
die Tabellen in `GAME.EXE`.

## Rechtliches

Copyright © 2026 **chr1zZo**. Der Code steht unter der **GPL-3.0**, siehe
[`LICENSE`](LICENSE).

*Akte Europa* und alle zugehörigen Inhalte gehören ihren jeweiligen
Rechteinhabern. Dieses Projekt ist weder von ihnen unterstützt noch mit ihnen
verbunden und verbreitet keinerlei Originaldaten. Zum Spielen werden die
Original-CDs gebraucht.
