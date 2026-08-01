# Akte Europa Reborn

Eine Neuimplementierung des Echtzeit-Strategiespiels **Akte Europa** (1997) in
Godot 4 und C#.

Das Projekt enthält **keinerlei Daten des Originalspiels**. Es liest die beiden
Original-CDs, die man selbst besitzen muss, und leitet daraus auf dem eigenen
Rechner alles ab, was zum Spielen nötig ist: Karten, Einheitengrafiken,
Schriften, Tabellen, Missionstexte. Die ausgelieferte Programmdatei trägt nichts
von 1997 in sich — die mitgelieferte `.pck` ist rund 200 KB groß.

---

## Stand

Spielbar sind Gefechte und die Kampagne mit 33 Missionen. Aus den zwei Discs
entstehen beim ersten Start:

| | |
|---|---|
| Karten | 44, gebacken bis 10160 × 5285 Pixel |
| Spielstände | 44 (Einheiten, Gebäude, Ziele, Vorkommen, Schienen …) |
| Einheitenbilder | 4329, aus ROBO.CWR zusammengesetzt |
| Tabellen | 11, aus Kartendateien und GAME.EXE |
| Oberfläche | Originalschrift, Seitenpanel, Effekte, Missionsbriefings |

Die Simulation läuft auf den Zahlen des Originals: Preise, Lebenspunkte, Angriff
und Verteidigung, Reichweite, Nachladezeit, Geschwindigkeit, Treibstoff und
Munition stammen aus den Datensätzen des Spiels und nicht aus geschätzten
Konstanten. Wo etwas nicht rekonstruiert werden konnte, steht das als solches im
Code — jede eigene Setzung ist dort ausdrücklich markiert.

## Was man braucht

* Die beiden Original-CDs von *Akte Europa* (im Laufwerk oder als Ordner)
* Windows; ein Build für andere Systeme ist bisher nicht eingerichtet

## Installation

Den Installer aus den [Releases](../../releases) ausführen. Beim ersten Start
fragt das Programm nach den Discs oder einem Installationsordner und leitet die
Inhalte daraus ab. Das dauert einige Minuten; die abgeleiteten Dateien landen im
Benutzerprofil, das Programmverzeichnis bleibt unberührt.

## Selbst bauen

Godot 4.7 (Mono/.NET) und das .NET-SDK werden gebraucht.

```bash
dotnet build "Akte Europa Reborn.csproj"
godot --path . --headless --export-release "Windows Desktop"
```

Inhalte ohne Oberfläche importieren:

```bash
godot --path . --headless -- --import-cd          # Discs im Laufwerk
godot --path . --headless -- --import=<Ordner>    # oder aus einer Installation
```

## Selbsttests

Die Leser für die Originalformate werden nicht nur gebaut, sondern gegen die
Daten geprüft, aus denen sie lesen:

```bash
godot --path . --headless -- --selftest-cwp=<Werkzeugordner>
godot --path . --headless -- --selftest-designs
godot --path . --headless -- --selftest-briefings
```

Geprüft wird unter anderem: 13 Sprite-Rahmen pixelgenau gegen eine zweite
Umsetzung, 26 Kartendateien restlos aufgebraucht, 4147 Einheitenbilder
deckungsgleich, 601 Entwurfssätze exakt nachgerechnet, 33 Missionstexte
zeichengleich.

## Wie das Projekt arbeitet

Jede Mechanik wird aus der Original-Binärdatei rekonstruiert, bevor sie
implementiert wird — auch wenn Erfinden schneller wäre. Eine Deutung gilt erst,
wenn eine Zahl dahintersteht: über alle Karten gezählt, gegen eine zweite
Umsetzung gehalten, in der laufenden Engine nachgemessen. Was aus den Daten
kommt und was von uns ist, wird getrennt und im Code benannt. Fehldeutungen
werden zurückgezogen statt überschrieben.

Die Dateiformate wurden dabei einzeln aufgeschlossen: CWP (Kacheln und Objekte),
CWM (Karten mit über 130 Abschnitten), CWR (Einheitensprites), CWD (Schriften),
CWA (Effekte), das InstallShield-Kabinett der CD sowie die Tabellen in GAME.EXE.

## Rechtliches

Der Code steht unter der **GPL-3.0**, siehe [`LICENSE`](LICENSE).

*Akte Europa* und alle zugehörigen Inhalte gehören ihren jeweiligen
Rechteinhabern. Dieses Projekt ist weder von ihnen unterstützt noch mit ihnen
verbunden und verbreitet keinerlei Originaldaten. Zum Spielen werden die
Original-CDs benötigt.
