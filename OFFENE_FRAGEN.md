# Offene Fragen an den Spieler

Alles, wo ich **im Original nichts gefunden habe** oder wo eine Deutung nicht
zu beweisen war. Kein Punkt hier ist geraten und eingebaut — entweder er ist
gar nicht gebaut, oder er ist gebaut und im Code ausdrücklich als *unsere
Setzung* markiert.

Der Spieler hat ein vollständiges Let's Play aller Missionen und kann
nachsehen oder Bildschirmfotos machen.

---

## Beantwortet (bleiben als Beleg stehen)

| Frage | Antwort | eingebaut |
|---|---|---|
| Beendet der Knopf **BEENDEN** im Nachfrist-Fenster die Mission sofort? | **Ja** — die verbleibende Zeit ist damit verloren, die Nebenmission lässt sich nicht mehr abschließen. | ja, `MissionScript.EndGraceNow` |
| **Mission 27**: warum 0 von 3 Untermissionen? | Die Basen müssen **eingenommen** werden, nicht zerstört. Bestätigt durch den Spieltext #370 (»Besetzung der Droiden-Basis innerhalb 20 Minuten«) **und** den Code (`objects(Typ1, P0) > 1`). | ja, alle drei Ziele lesbar |

---

## Offen

### ~~1. Untermissionen, die wir nicht erfuellen konnten~~ — ERLEDIGT

Alle acht sind gebaut. Der Abgleich Original gegen unsere Datei meldet fuer
**alle 33 Missionen keine einzige Abweichung** mehr: keine fehlt, keine ist zu
viel. Vier Formen haben gefehlt, alle vier jetzt gelesen:

* `find_unit_with_part(spieler, teil)` (M20, M24) — die Abfrage stand als
  »gelesen, nicht gebaut« da, weil zwei der drei Bauteilbytes bei uns fehlten.
* `time_after` mit einem anderen Operator als `>` (M14) — die Laufzeit rechnete
  `>` fest.
* `find_unit(...) == v[n]`, also gegen eine Variable statt gegen eine Zahl (M6).
* Untermissionen im ENDBLOCK (M7, M16) — der Leser uebersprang jeden
  Basisblock, der die Endfunktion ruft, samt allem anderen darin.

### 3. Die Nachfrist selbst

`byte[0x4F6FA4] = 0xA`, jede Spielminute eins herunter, bei 0 endet die Mission
als **Sieg**. Das ist gelesen. **Ungelesen** ist, ob ein Schritt wirklich eine
Spielminute ist — gemessen sind 250 Takte je Schritt, und dass 250 Takte eine
Spielminute sind, ist unsere Umrechnung.

**Was mir hülfe:** wie lange die »00:10« im Bild real dauert (Stoppuhr am
Video reicht: von 00:10 bis 00:00).

### 4. Zeigerarten

Von den 28 Mauszeigern im `ROBO.CWR`-Anhang ist die Bedeutung von vieren
gelesen (0 Pfeil, 1 eigenes Objekt, 2 Angriff, 5 eigene Infanterie). Die
übrigen 24 sind nur nach Augenschein benannt, weil ungelesen ist, **wer den
Modus `dword[0x502AD4]` setzt**.

**Was mir hülfe:** Bildschirmfotos, in denen ein ungewöhnlicher Zeiger zu sehen
ist, mit einem Wort dazu, was gerade passiert.

---

## Bekannte eigene Setzungen (nicht gelesen, aber gebaut)

Diese sind im Code an Ort und Stelle markiert; hier stehen sie gesammelt, damit
sie nicht in Vergessenheit geraten.

* **Takt der Zeigerbildfolge** (0,10 s) — das Original zählt die Phase in
  `byte[0x502AA0]`, nennt aber keine Geschwindigkeit.
* **Schwelle, ab wann ein Einheitenbalken gelb wird** (die Hälfte) — das
  Original hat zwei Farben, der Umschlagpunkt ist nicht abzulesen.
* **Wo der Einnahmebalken sitzt** — Breite, Höhe und Farben sind gelesen, der
  Bezugspunkt nicht (das Original rechnet ihn aus einer Zeichenliste, deren
  Felder wir nicht führen).
* **Wohin »Weiter« am Kampagnenende führt** — bei uns ins Menü.

---

## In Arbeit / Teilbefund

### Der weiche Nebel (18.08.2026)

Gemeldet: »unser fog of war ist schon gut aber deckt wie kacheln auf — im
original ist das aber wie weich«.

**Gelesen ist der Unterbau**, aber noch nicht die Weichheit:

* `0x678B58` — ein Byte je Zelle, 256×256, **jeden Nebeltakt neu gebaut**
  (`rep stosd` @0x4205BF) und dort auf 1 gesetzt, wo gerade jemand hinsieht
  (@0x420268, @0x42029A). Das ist unser *Watched*.
* `0x689710` — dieselbe Indizierung, aber **bleibend**: beim Aufdecken wird
  der Wert der Zelle aus `0x542E18` hinüberkopiert (@0x4202C5). Das ist unser
  *Seen*, nur dass es nicht »gesehen ja/nein« speichert, sondern **den
  Geländewert selbst**.
* Gezeichnet wird aus dem SCHATTENFELD: der Zeichner @0x432367 fragt
  `byte[0x689710 + zelle] == 0x63` **oder den rechten Nachbarn**
  `byte[0x689711 + zelle] == 0x63`. Eine Zelle wird also mitgezeichnet, wenn
  ihr NACHBAR den Wert trägt — daher kein harter Zellrand.

⚠ **Was ich NICHT belegt habe:** ob daraus die weiche Kante entsteht oder ob
es dafür noch eine eigene Stelle gibt. `0x542E18` ist als Geländefeld gedeutet
und nicht bewiesen, und was `0x63` bedeutet, ist offen. **Nichts davon ist
eingebaut** — unser Nebel bleibt, wie er ist, bis das gelesen ist.

**Was mir hülfe:** ein Bildschirmfoto aus einer Mission mit *unaufgedeckter*
Karte, am besten zwei kurz nacheinander, während eine Einheit in unbekanntes
Gebiet fährt. Dann sehe ich, ob der Rand wirklich weich ist oder ob nur die
Objekte fehlen und das Gelände durchgehend sichtbar bleibt.

### Die Schleifen des Regellesers (18.08.2026, Negativbefund)

**41 der 69 verbliebenen Verwürfe** heißen »Schleife«. Zwei Anläufe, beide
gemessen, beide **null Regeln**:

1. **Über die Rücksprungkante hinweg weiterlesen.** Strukturell richtig (was
   vor dem Schleifenkopf steht, gilt auch dahinter). Ergebnis: Schleifen­verwürfe
   25 → 23, Gesamtzahl unverändert 86, gelesene Wirkungen unverändert.
2. **Die zweite Zählerform** — der Zähler auf dem Stapel statt im Register
   (`mov al, byte[esp+K] / inc al / mov byte[esp+K], al / cmp al, N`). Erkannt
   samt Startwert und Laufindex, dazu die zwei Wirkungen im Rumpf. Ergebnis:
   684 Regeln vorher wie nachher, kein Durchlauf ausschreibbar.

Beide zurückgenommen. Der Grund steht als Vermerk im Leser, damit niemand
denselben Weg ein drittes Mal geht.

⚠ **Wer es weiterversucht**, fange bei Mission 28 an: Rumpf `0x4A30A8..0x4A3130`,
alle Filter springen auf `0x4A3124`, die zwei Wirkungen sind `0x4A3107`
(`v[51+k]` hochzählen über ein Register) und `0x4A311C`
(`v[61+k] = game_time()`). Dort sitzt noch mindestens ein Blocker, den ich
nicht gefunden habe.

### Die Klänge — was das Original spielt und wir nicht

Das Spiel ruft seine Klangroutine `0x4047E0` an **111 Stellen**, davon 89 mit
einer festen Nummer; **62 verschiedene Nummern**. Die vollständige Aufstellung
mit Modus, Aufrufstellen und der Protokollmarke, die das Spiel selbst dazu
druckt, liegt in `aekernel-tools/KLAENGE.md`.

Gebaut ist davon bisher nur ein Teil. Sicher erkannt und **heute gebaut** ist
der Befehlsklang (`0x429480`). Nicht gebaut sind unter anderem die
Warnungen des Bauwesens (»Ihre Basis wird besetzt« — Nummern 123..134,
Modus 0) und die Flugzeugmeldungen (Nummern 303/304/308/309, »air A:«/
»air B (no fuel):«).

⚠ Die Zahl »44 fehlen« aus einem Zwischenstand ist zu hoch gegriffen: sie kam
aus einem groben Abgleich, der unsere Konstanten per Muster suchte. Verlässlich
ist die Aufstellung, nicht die Differenz.


### Die Schriftfarben — die Tafel ist gelesen (19.08.2026)

Gefragt war: »haben wir überall die gleichen Schriftfarben«. Der Textzeichner
des Originals ist **`0x4BA420`** (über den Stummel `0x401041`) — **366
Aufrufstellen in 43 Funktionen**, darunter der Bedienblock. Das ist *der*
Zeichner der ganzen Oberfläche, es gibt keinen zweiten für gewöhnlichen Text.

**Er färbt je ZEICHEN, nicht je Wort.** Für jedes Zeichen rechnet er
`c − 0x24`, schlägt in einer Bytetafel bei `0x4BA504` nach und springt über
`0x4BA4E8` in einen von sieben Armen. Sechs Zeichen haben eine eigene Farbe,
alles andere fällt auf 0:

| Zeichen | Farbe | Palettenpaar | dunkel | hell |
|---|---|---|---|---|
| *(alles andere)* | 0 | — | die Schrift trägt ihre eigenen Farben |
| `]` | 1 | 156 / 153 | `#C1292F` | `#F05131` — rot-orange |
| `[` | 2 | 169 / 150 | `#AB871F` | `#F4B81C` — gold |
| `{` | 3 | 127 / 124 | `#63637F` | `#A3A3B7` — blaugrau |
| `$` | 4 | 87 / 84 | `#B79F73` | `#E3C793` — beige |
| `}` | 5 | 101 / 98 | `#9BB78B` | `#CBDBBF` — hellgrün |
| `^` | 6 | 55 / 53 | `#9B8F67` | `#B3AB83` — olivbeige |

Farbe 0 zeichnet mit `0x401852`, die sechs anderen mit `0x4020EA` und dem
Indexpaar als zusätzlichen Argumenten (zweite Sprungtafel `0x4BA560`).

**In `HELPG.TXT` kommen davon genau zwei vor:** `^` **738-mal** — immer am
Zeilenanfang und genau einmal je Zeile — und `$` **135-mal**, dort als echtes
Währungszeichen (»50$ für jeden versenkten Frachter«). `[`, `]`, `{` und `}`
kommen **nie** vor. Das `@` (577-mal) ist **kein** Farbzeichen; es fällt auf
Farbe 0 und ist der Absatzmarker, den wir schon behandeln.

**Was wir daraus schon richtig haben:** zwei unserer Farbwerte sind exakt
Farbe 2 (`#F4B81C` und `#AB871F`) — die hat jemand vor mir gelesen. Andere
sind daneben: unser Rot ist `#E86048`, das des Originals `#F05131`.

⚠ **Was NICHT geklärt ist, und darum ist nichts geändert:** im Tutorialfenster
des Originals sind **ganze Wörter** eingefärbt (»Zum **BEWEGEN** der
angewählten Einheit … mit der **Linken Maus Taste**«). Mit einer Farbe je
Zeichen und ohne Klammern in `HELPG.TXT` kann das aus dieser Tafel nicht
kommen. Entweder färbt das Nachrichtenfenster (`show_text` `0x443490` /
`show_text2` `0x4432E0`) selbst ein, oder es gibt eine Auszeichnung, die ich
nicht gefunden habe.

**Was mir hülfe:** ein Bildschirmfoto eines Hilfefensters, in dem ein
eingefärbtes Wort gut zu lesen ist, zusammen mit der Textnummer (steht oft im
Fenstertitel). Dann kann ich die Zeile in `HELPG.TXT` aufschlagen und sehen,
was dort wirklich um das Wort herum steht.


---

# Vier Recherchen im Original (19.08.2026)

Vier Untersuchungen am Programm von 1997, jede gegen **beide** GAME.EXE geprüft.
Was hier steht, ist der Ertrag, **nicht** die vollständigen Berichte — und
ausdrücklich getrennt nach »gelesen und gebaut«, »gelesen und offen« und
»behauptet und widerlegt«.

## Gelesen und schon gebaut

* **Der Sichtkreis war jede Zeile zwei Zellen zu schmal** (`9b1a005`). Das
  Original stempelt `Spalte−t … Spalte+t` einschliesslich, wir nahmen
  `−t+1 … +t−1`. Nachgerechnet: bei Sichtweite 10 sah jede Einheit 333 statt
  373 Zellen. ⚠ Die Mittelzeile stimmte zufällig, weil das Original dort mit
  dem Radius statt dem Tafelwert rechnet — und dieser Zufall hat den Fehler in
  allen anderen Zeilen verdeckt.

## A. Der weiche Nebel — die Ursache ist gefunden

Meine Vermutung »das Original dunkelt das Gelände gar nicht ab« ist
**widerlegt**: der Geländezeichner hat eine Dreiwegverzweigung genau auf dem
Sichtgitter. Die Weichheit hat drei Ursachen:

1. **Ein 50-%-Schachbrett statt Volldeckung** (`0x4AC990`): 40×20 Punkte, jeder
   zweite gesetzt, je Zeile um eins versetzt, Palettenindex 47. Halbtransparenz
   ohne Alphakanal.
2. **Ein dritter Sichtzustand, der SAUM** (`0x41FF50`) — eine Ein-Zellen-
   Ausweitung des Sichtbereichs.
3. **Marching Squares**: ein 257×257-Eckengitter (`0x5739D8`), aus dessen vier
   Ecken ein Vierziffern-Code wird, nachgeschlagen in einer 16-Einträge-Tafel
   (`0x4F89F8`, vollständig, kein Füllwert). Die Kante läuft dadurch **durch**
   die Kachel, nicht an ihr entlang. Ein von allen Seiten umschlossenes Loch
   bekommt gar keinen Nebel (`0xFFFF`).

Dazu: das Original führt **drei** Merkgitter, nicht eines — Bauwerksmarke,
Belegung und Kachelbild je Zelle. Die Übersichtskarte zeigt das Erinnerte, die
Kampfkarte nur das Gesehene.

⚠ **Warum trotzdem nichts gebaut ist:** im Zeichenpfad des Originals gibt es
gar keinen Zustand »gesehen, aber gerade nicht beobachtet« — `0x678B58` wird
jede Runde neu gebaut. Das widerspricht einer Ablesung in unserem Baum
(»auf tutorial15 liegt die ganze Insel in voller Helligkeit«). Eines von
beiden ist falsch, und das gehört entschieden, **bevor** jemand den Nebel
umbaut.

**Was mir hülfe:** ein Bildschirmfoto mit einer sichtbaren Nebelkante. Bei
genügend Zoom ist das Schachbrett unmittelbar als Punktraster zu sehen — und
dieselbe Aufnahme beantwortet auch die Frage nach dem Erinnerungszustand.

Nebenbefunde: `0x542E18` ist **kein** Geländefeld, sondern das Bauwerksgitter
(`0x63` = Türzelle); Radarmasten sind 200 Plätze zu 6 Byte mit Sichtweite 10
(`'Too many radars'`); und es gibt einen **Störsender** (Gerät 77), der
fremde Sicht wieder löscht — bei uns nicht vorhanden.

## B. Schuss und Einschlag — die Tafel, die vieles ersetzt

Die ganze Zuordnung sitzt in **einer** Tafel: `0x4F98E8` (C) / `0x4F88F0` (F),
Schrittweite 22, Index = Geschosstyp, in beiden Fassungen **Byte für Byte
gleich**. Sie trägt Geschwindigkeit, Flugfolge, Einschlagsfolge, Schussklang,
Mündungshöhe und Zwillingsversatz. Wir haben davon bisher **ein** Feld
importiert.

Damit fallen mehrere unserer Setzungen weg: `RocketSpeed`, `MuzzleReach`,
`RocketKind` und die geratene Einschlagsfolge stehen alle gelesen da.

Weitere Befunde:
* **Geschosse fliegen wirklich**, 1000 feste Plätze zu 32 Byte, Geschwindigkeit
  5…35 Bildpunkte je Takt **je Typ**, jeden Takt gegen die Restentfernung neu
  gerechnet — das Geschoss zielt nach.
* **Zwei Bahnarten**: gerade und eine echte **Wurfbahn** mit Schwerkraft. Die
  Probe, dass die Lesung stimmt: nach genau `n = d/v` Schritten ist die
  Steiggeschwindigkeit auf ihren negativen Startwert gefallen.
* **Vier Geschosstypen feuern zwei Geschosse nebeneinander** (Zwillingslafette).
* **Acht Einschlagszweige mit eigenen Höhenschwellen**: ein Gebäude (60) fängt
  Geschosse ab, die über einen Panzer (15) hinweggehen.
* **Einheiten werden mit jedem Treffer stärker.** `+0x28` ist der
  Erfahrungs-RANG, `+0x4C` die Punkte — das Spiel nennt es selbst:
  `printf("exp:", (byte[+0x28]<<8) | byte[+0x4C])` @0x4178F2, von mir
  nachgeprüft. Der Rang steht in **beiden** Vorfaktoren der Schadensformel: ein
  Rang mehr macht mehr Schaden **und** steckt mehr ein. Wir lassen ihn nie
  wachsen.
* **Waldbrand breitet sich mit dem WIND aus**: die Wahrscheinlichkeit hängt vom
  Winkel zwischen Ausbreitungsrichtung und Windrichtung ab — mit dem Wind
  4,6-mal so oft wie quer dazu. Der Wind (`0x4F8D68`/`0x4F8D6C`) ist bei uns
  ungenutzt.

⚠ **Negativbefund mit Beleg: es gibt kein Mündungsfeuer.** Die Schussroutine
legt in ihren 2816 Byte **keinen einzigen** Effekt an — alle Aufrufe wurden
ausgezählt. Der Blitz an der Mündung ist das erste Bild der Flugfolge.

## C. Schiffe

* **Zehn Schiffsarten**, Tafel `0x52EDA0`, 42 Byte je Satz, beide EXE gleich.
  Gegenprobe an den Karten: 97 von 97 Schiffen stimmen in Bauteil **und**
  Waffenplatz mit der Tafel überein, kein Gegenbeispiel.
* **16 Blickrichtungen**, dreifach belegt: die Drehroutine bricht bei 16 um,
  die Bauteiltafel von ROBO.CWR hat Abstand 16, und die Bilder selbst sind
  spiegelsymmetrisch 1↔15, 2↔14, 3↔13, 4↔12. ⚠ Unser Ausgeber nimmt **8** —
  die Schiffsbilder sind also halb exportiert.
* **`+0x0d` heisst `ZBRAN` = Waffe**, das Spiel druckt den Namen selbst. Unsere
  Deutung »Bildvariante des Rumpfes« ist falsch; das Rumpfbild kommt aus
  `+0x0b`.
* **Der Dock-Auslauf ist gelesen** und ersetzt unsere Setzung: Auftrag 52 wählt
  die Seite, Auftrag 49 lässt das Schiff geskriptet über zwei bzw. vier Zellen
  gleiten, und **erst danach** stempelt es sich ins Belegungsgitter.
* **Transport gibt es, und wir haben ihn nicht**: eine Tafel mit 100 Plätzen zu
  38 Byte, Gewichtssystem 5 je Fahrzeug / 1 je Infanterist, Deckel 15 — also
  **drei Fahrzeuge oder fünfzehn Mann**. Flugzeuge und Schiffe werden
  abgelehnt. Die »Rampe« ist keine Bauart, sondern eine Zellmarke (≥100 zum
  Beladen, ≥200 zum Entladen).
* **Ein Schiff ohne Sprit tut gar nichts** — der ganze Auftragsverteiler wird
  übersprungen.

### ⚠ Eine Behauptung der Recherche, die ich WIDERLEGT habe

Der Bericht führt »die Mittelpunktregel fehlt« als Erklärung für die gemeldete
Rakete, die »paar Felder entfernt im Wasser« abgeht. **Das stimmt nicht.** Das
Original setzt ein 2×2-Schiff auf `40·col + 40`, ein 4×4 auf `40·col + 80` —
also auf die Mitte des Grundrisses. Unser `BodyCenter` rechnet
`CellCenter + TileW/2` bzw. `+ 1,5·TileW`, und das ist **derselbe absolute
Punkt**. Nachgerechnet, nicht geglaubt. Die Ursache der gemeldeten Rakete ist
also weiter offen.

## D. Was sonst noch ungelesen ist — die drei grössten Posten

1. **Das Original hat einen eigenen Determinismus-Prüfstand.** Marken
   `RECORDING` / `REPLAY`, Dateien `replay.beg` (Anfangszustand), `replay.mes`
   (Befehlsstrom), `replay.txt` (Protokoll). Er schreibt je Takt **56 benannte
   Felder jeder Einheit** heraus und vergleicht sie beim Abspielen. Im ganzen
   Baum: null Treffer. Genau die Frage, an der »Multiplayer online« hängt — und
   nebenbei die vollständige, geordnete Feldliste des Einheitensatzes aus dem
   Mund des Spiels.
2. **Die Reihenfolge des Haupttakts steht im Klartext da**: 44 benannte
   Stationen mit 28 Zufallsprüfpunkten dazwischen. Unser Takt hat eine
   **andere** Reihenfolge, und mindestens 14 Stationen fehlen ganz — darunter
   `Check gas`, `Self-defenders`, `Mines and traps`, `craters`, `Check AA`.
   Die Reihenfolge *ist* der Determinismus.
3. **`game.007` ist ein echter Spielstand** (566 KB) und wir haben ihn nie
   angefasst. Er enthält die Bauteiltafel im Klartext — also eine **zweite
   Quelle**, gegen die sich jedes gelesene Satzformat byte-genau prüfen lässt,
   ohne unsere eigene Ableitung zu befragen.

Dazu: **Gas** ist eine eigene Objektart mit zwei Takten, **Minen, Fallen und
aufgestellte Radare** sind drei baugleiche Routinen mit je eigener Liste,
**Selbstverteidiger** sind eine eigene Liste im Haupttakt, und es gibt fünfzehn
Schummelcodes samt `ENABLEDEVEL`, das vermutlich die ganze Protokollausgabe
freischaltet.
