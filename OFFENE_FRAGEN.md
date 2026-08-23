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
| Wie lange steht **eine Zahl** im Nachfrist-Fenster? | **5 bis 6 reale Sekunden** (Stoppuhr am Let's Play, 21.08.2026). Damit ist `TicksPerSecond = 50` gemessen, nicht mehr gesetzt — siehe Abschnitt 3. | ja, keine Änderung nötig |
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

### ~~3. Die Nachfrist~~ — ⭐ ERLEDIGT am 21.08.2026, **am Video nachgemessen**

**Die Messung des Spielers:** »1 Sekunde im Spiel sind wohl so 5–6 Sekunden im
echten Leben« — also steht **eine einzelne Zahl des Zählers 5 bis 6 reale
Sekunden**, gestoppt am Let's Play.

**Damit geht die Rechnung des Programms glatt auf.** Der Zähler geht alle
**250 Takte** um eins herunter (@0x4160FC), der Taktzähler steigt innerhalb der
Geschwindigkeitsschleife (@0x416097), und die Schleife läuft bei der Vorgabe
**einmal** je Zeitgeberschlag von 20 ms:

    250 Takte / 50 Takte je Sekunde = 5,00 s je Zahl        ✔ gemessen 5–6 s

| Hypothese | ergäbe je Zahl | Urteil |
|---|---|---|
| **250 Takte @ 50 Hz, Geschwindigkeit 1** | **5,00 s** | ⭐ **das ist es** |
| Geschwindigkeit 2 | 2,50 s | tot |
| Geschwindigkeit 3 | 1,67 s | tot |
| der Takt wäre in Wahrheit 250 Hz | 1,00 s | tot |

⭐ **Der Befund reicht weit über dieses Fenster hinaus.** `TicksPerSecond = 50`
war bis heute als **unsere Setzung** ausgeschildert und stand unter Verdacht,
weil eine frühere Aussage des Spielers (»10 Sekunden sind 10 Sekunden«) einen
Faktor 5 dagegen behauptete. Die Konstante ist jetzt **gelesen und gemessen**,
und mit ihr alles, was an ihr hängt: Markttick, Bahn, Produktion, Fahrzeiten
und sämtliche Skriptzeiten. Die frühere Aussage meinte nicht den Zähler.

**Am Code war nichts zu ändern** — er rechnet seit dem 20.08. genau so
(`GameSpeed`, Tasten `+` und `−`, belegt mit `--takt-check`: *»Takte je
Sekunde: Pause 0, Geschw. 1 = 50, 2 = 100, 3 = 150«*). Zu ändern waren nur die
Stellen, die die 50 noch als unsichere Setzung auswiesen; drei sprachen sogar
noch von `SimHz = 60`, das es seit dem 20.08. nicht mehr gibt.

**Die Anzeige bleibt der Rohwert** hinter »00:« (@0x487485) — das ist die des
Originals, sie meint Spielminuten und nicht Sekunden.

⚠ **Was das ausdrücklich NICHT sagt:** dass das Let's Play durchgehend auf
Geschwindigkeit 1 lief. Für diese Stelle ist es belegt (5 s je Zahl); wer eine
andere Zeit aus demselben Video misst, muss die Geschwindigkeit mitbedenken.

### ~~4. Zeigerarten~~ — ERLEDIGT am 20.08.2026, **ohne** Bildschirmfoto

Hier stand: »von den 28 Mauszeigern ist die Bedeutung von vieren gelesen, die
übrigen 24 nur nach Augenschein benannt, weil ungelesen ist, wer den Modus
`dword[0x502AD4]` setzt«. Gelesen, und die Frage an den Spieler entfällt.

**`dword[0x502AD4]` ist kein Zeigerindex, sondern ein ZUSTAND.** 59 feste
Schreibstellen, 30 davon allein in der 5,5-kB-Trefferprüfung C `0x4315D0`. Die
Umsetzung Zustand → Zeiger macht C `0x4A9AB0` / F `0x4A93E0` über eine
26er-Sprungtafel `0x4A9BEC` (plus die Sonderwerte 100, 1000, 1001, 1002); das
Ergebnis landet in `byte[0xA182D0]`. Unter anderem: 14 »Ziel gültig«,
15 »ungültig«, 23 »zu weit«, 20 Gummiband, 0xFF aus.

Die 28 Zeiger selbst liegen in einer bis dahin unbekannten Tafel im
**`ROBO.CWR`-Kopf** (`0xA31AA0`, 1760 B = 40 Sätze zu 44 B = Bildzahl plus zehn
Versätze; belegt sind 0…28, Satz 9 ist leer). Der Lader `0x429020` geht auf das
Byte auf.

⚠⚠ ~~**Vier Zeiger (6, 7, 8, 25) sind gefüllt, aber tot** — kein Code wählt sie.~~ **WIDERLEGT am 21.08.2026, siehe Abschnitt AK.3:** 6, 7 und 8 leben — sie werden nicht über den Zustandsautomaten gewählt, sondern direkt aus der Satztafel gezeichnet (Klickmarkierung und die zwei Auswahlmarken). **Nur 25 ist tot.**
Wer sie im Original je gesehen hat, hätte einen Befund; erwarten würde ich es
nicht.

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

### ~~Der weiche Nebel~~ — ERLEDIGT am 19.08.2026

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

**Gebaut und angenommen.** Die Ursache war Marching Squares über ein
257×257-Eckengitter plus ein 50-%-Schachbrett in Palettenfarbe 47 (`#13130F`).
Beides gelesen, beides gebaut; die Deckkraft ist damit **gemessen (0,50)**
statt geschätzt (0,30).

Urteil des Spielers am fertigen Bau: »nicht ganz der originale, aber sieht top
aus, schön weich. kann man so lassen, besser als alle bisherigen versionen.«

⚠ **Was daran bewusst abweicht:** das Original hat je Eckenmuster ein festes
Übergangsbild (Tafel `0xBAC72C`), wir haben eine stetige Rampe zwischen den
vier Ecken. Die Tafel liegt im BSS, wird zur Laufzeit gefüllt und ist aus der
EXE nicht zu lesen — sie käme nur aus einem laufenden Spiel oder aus dem
Kachelsatz. Solange es dafür keinen Anlass gibt, bleibt es, wie es ist.

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

⚠⚠ **21.08.2026 — NACHGEZÄHLT, und zwei Angaben hier waren falsch.**

Hier stand, nicht gebaut seien »die Warnungen des Bauwesens — Nummern 123..134«
und »die Flugzeugmeldungen 303/304/308/309«. Beides stimmt so nicht:

* Von 123..134 sind **123, 124, 125, 127, 128, 129, 130, 131 und 132 gebaut**;
  es fehlen nur **133, 134, 135**.
* **309 gibt es im Klangvorrat gar nicht** — der Aufruf ist im Original selbst
  stumm, wie 40, 307 und 399 auch. Es als »fehlend« zu führen, wäre ein
  Fehler, den man nie bemerkte: man baute einen Klang, den das Original nie
  spielt.

**Die gemessene Zahl** — 62 Nummern im Verzeichnis, 4 davon im Original leer,
**36 weder gebaut noch leer**:

```
2, 8, 36, 41, 42, 43, 44, 45, 47, 49, 50, 51, 52, 53, 55, 56, 57, 58,
60, 72, 74, 75, 120, 122, 133, 134, 135, 139, 143, 300, 303, 304, 308,
310, 600, 601
```

⚠ **600 und 601 stehen bewusst darin und bleiben ungebaut** — die Deutung
»das ist der Bedienklick« ist am 19.08.2026 zurückgenommen worden (Begründung im
Kopf von `GameSounds.cs`).

⚠ Und die ältere Zahl »44 fehlen« war zu hoch gegriffen: sie kam aus einem
groben Abgleich, der unsere Konstanten per Muster suchte. **36** ist gegen die
Aufstellung UND gegen `GameSounds.cs` gerechnet, einschliesslich der Bänder
150..253 und 400..410.

Die 36 sind **nicht einfach nachzutragen**: jeder braucht die Stelle in
UNSEREM Code, an der das Ereignis wirklich eintritt, und mehrere hängen an
Dingen, die es bei uns noch nicht gibt (Werftfehler 133, Rampenfehler 42/43,
Suchkopf 37). Ein Klang an der falschen Stelle ist schlimmer als keiner.


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

⚠⚠ **ERLEDIGT am 20.08.2026, und die Fährte war falsch.** Hier stand: »im
Tutorialfenster sind ganze Wörter eingefärbt, das kann aus dieser Tafel nicht
kommen — was mir hülfe, ist ein Bildschirmfoto«. Es brauchte keins.

**Die Auszeichnung ist `@`** — ein Zeichen ohne Breite vor einem Wort, **577
mal** in `HELPG.TXT` und in **keiner** anderen `.TXT`. Wir hielten es für den
Absatzmarker; das ist `^`. Der Hilfetext wird **wortweise** gemalt
(C `0x47CF10` / F `0x47B800`): die Breitenmessung `0x45A560` überspringt das
`@`, und ab Wort+1 wird mit dem Farbpaar **(0x97, 0x9A)** — orange-rot —
gezeichnet statt mit (0x94, 0x96) — gelb.

⚠ Warum die alte Überlegung danebenlag: `@` steht in der Zeichentafel
`0x4BA504` auf »ohne Farbe«, und `[ ] { }` kommen in `HELPG.TXT` gar nicht vor.
Beides stimmte — nur war die Färbung nie eine Sache der Zeichentafel.

**Noch offen dazu:** die zweite Hilfefenster-Fassung `0x47D6D0` benutzt
dasselbe Hervorhebungs-Farbpaar **ohne** die `@`-Prüfung. Ungelesen.


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

## B. Schuss und Einschlag — ERLEDIGT am 19.08.2026

**Die Tafel ist importiert und angeschlossen.** Was unten steht, ist der Befund
von damals; hier zuerst, was daraus geworden ist und was sich dabei als falsch
herausgestellt hat.

### Gebaut

* `ExeTables.Projectiles()` liest alle **91 Zeilen zu 22 Byte**. Der Ausgeber
  schreibt sie in dieselbe `weapon_sounds.json` wie den Klang — der Index ist
  derselbe, denn die »Klangklasse« aus dem Statssatz `+0x1C` **ist** die
  Geschossart.
* **Drei geratene Konstanten sind weg**: `RocketSpeed` (eine Zahl für alles),
  `RocketKind` (eine Liste von drei Bauteilen) und der immer gleiche
  Einschlag `"explosion"`.
* **Aus 3 mach 13.** Welche Waffe ein fliegendes Geschoss hat, sagt Feld `+0x02`
  (30000 = keine). Danach haben **dreizehn** der Bauteile eines — bisher hatten
  drei eines und alle anderen zogen eine Leuchtspur.
* Der Ausgeber schreibt jetzt **30 Flugfolgen und 13 Einschlagfolgen** aus
  ANIM.CWA (vorher: zwei fest verdrahtete). Zwei weitere Einschlagfolgen (87 und
  309) sind in ANIM.CWA **leer** — diese Waffen zeigen im Original also gar
  keinen Einschlag, und das wird jetzt nachgebildet.
* **Die Erfahrung wächst.** `Erfahren()` in `MapEntityLayer` rechnet
  `((Nachladezeit+1) · Schaden · (Verteidigung+2·Höhe_Opfer)) / (Angriff+2·Höhe_Schütze) / 8`,
  Punkte in `+0x4C` als Byte mit Überlauf, und beim Überlauf steigt der Rang
  `+0x28`. **Gebäude geben keine Erfahrung** (@0x40CEEA). Damit ergibt auch der
  Veteranen-Tonfall der Stimmroutine (Schwelle 50) erstmals Sinn.

### ⚠ Zwei eigene Behauptungen, die dabei gefallen sind

1. **»+0x14 ist die Mündungshöhe über Grund.«** Zu viel behauptet. Belegt ist
   nur: der Wert wird auf das zurückaddiert, was die *Lafettensuche* (0x435BD0)
   liefert, und landet auf Byte +3 des Schusssatzes — nicht auf einer der beiden
   Zellzahlen (+4/+5) und nicht auf dem Bildpunkt (+0). Was jener Grundwert
   bedeutet, ist **nicht gelesen**. Das Feld heisst deshalb jetzt `MountBias`.
2. **»Eine Rohrlänge gibt es im Original nicht.«** Auch das war zu grob.
   SHOOT.CWT gibt es und wird gelesen — aber vom **Zeichner** 0x42A188, nicht von
   der Schussroutine. Unser `MuzzleReach = 14` bleibt damit unsere Setzung, und
   der richtige Weg dorthin ist SHOOT.CWT, nicht Feld +0x14.

### Was daraus NEU offen ist

* **Zeigen wir jetzt das Mündungsfeuer doppelt?** Der Negativbefund unten sagt:
  der Blitz an der Mündung **ist** das erste Bild der Flugfolge, das Original
  legt keinen eigenen Effekt an. Wir legen weiter einen an (`Kind = "muzzle"`),
  und seit heute haben dreizehn statt drei Waffen zusätzlich eine Flugfolge.
  ⚠ **Das ist eine Sache für den Blick, nicht für den Zähler** — bitte einmal
  ansehen, ob es zu hell wirkt. Bis dahin bleibt es, wie es ist.
* **Zählt `Satz[+0x26]` in Achterschritten?** Das Original rechnet
  `Bild = Folgenanfang + Satz[+0x26] + Richtung` (@0x42B198). Wir zeichnen
  weiter `Phase*8 + Richtung`, was bei den Folgen 64/65 sichtbar stimmt. Bei
  Folgen mit **einem einzigen Bild** (60, 61, 71, 250, 251, 271, 273, 276, 279)
  hätte die Formel des Originals in die **nächste** Folge gegriffen — die Bilder
  liegen in ANIM.CWA fortlaufend. Ob das im Original so aussieht oder ob diese
  Arten nie mit Richtung feuern, ist nicht gelesen; wir zeigen dort dasselbe
  Bild aus jeder Richtung.
* Der **Zwillingsversatz** (+0x15, vier Arten) ist gelesen und importiert, aber
  noch **nicht gebaut**: diese Waffen feuern bei uns ein Geschoss statt zwei.

---

### Der Befund von damals (bleibt als Beleg stehen)

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
* **16 Blickrichtungen** — ⭐ **ERLEDIGT am 19.08.2026**, aber die Begründung
  musste ausgetauscht werden.

  ⚠ **Die Spiegelsymmetrie 1↔15 stimmt nicht.** Nachgemessen an den
  Abmessungen der Einzelbilder (die bei einem gespiegelten Paar zwangsläufig
  gleich sind) treffen bei den Schiffsteilen **weder** die Paare zu 8 **noch**
  die zu 16 — während Kettenteile 5 bis 7 von 7 Paaren zu 8 treffen. Schiffe
  sind schlicht nicht spiegelsymmetrisch gezeichnet. Der Punkt war trotzdem
  richtig, nur aus anderen Gründen:

  1. Jedes Schiffsteil besitzt **genau 16 Bilder**, und die Teiletafel nennt
     dafür **eine** Gruppe. Bodenteile haben 48, 96 oder 144 — sechs
     Neigungsblöcke zu acht. Ein Schiff fährt auf Wasser und hat keine Neigung.
  2. **Angesehen**: die 16 Bilder sind eine durchgehende Volldrehung, der Bug
     wandert Bild für Bild weiter und wiederholt sich bei 8 nicht. Beim
     Kettenteil wiederholen sich die Richtungen im zweiten Achterblock sehr wohl.
  3. **Die ausgelieferten Karten sagen es selbst**: von 213 Schiffen tragen
     **21 eine Richtung über 7** (bis 15) — von 4592 Landeinheiten **eine
     einzige**. Das ist der stärkste der drei Belege, weil er weder am Code noch
     an meinem Auge hängt.

  Gebaut: `CwrFile.ShipFacings = 16` mit `IsShipPart`, der Ausgeber schreibt für
  die neun Schiffstypen 150…158 jetzt `f0…f15` und vermerkt `n_facings` in
  `units_index.json`; die Laufzeit liest die Stufenzahl von dort und dreht in
  22,5-Grad-Schritten statt in 45ern.

  ⚠ **Ein Widerspruch, der sich beim Nachlesen aufgelöst hat.** Im Quelltext
  stand »@0x405100: nur Gattung 3 bekommt 16 Richtungen«, während `Can_go`
  `+0x0A == 4 oder 5` als Schiff behandelt. Beides stimmt — es sind **zwei
  verschiedene Felder**:

  * Der 16er-Zweig bei 0x405132 (`cmp al,3`) dreht **`+0x03` = OT_HLAV**, den
    *Aufbau*: @0x405148 vergleicht `byte[+0x03]` gegen `wunsch*2`, rechnet also
    in Sechzehnteln, während der Aufrufer Achtel liefert. Gattung 3 hat auf
    allen Karten zehn Einheiten, alle vom Rumpftyp 138.
  * Der *Rumpf* eines Schiffes steht in **`+0x02` = OT_PODV**, und dass der
    sechzehn Stufen hat, sagen die Kartendaten (siehe Punkt 3 oben).

  Die Namen stammen aus dem Aufzeichner des Originals, siehe `ENTITY_FELDER.md`.
  Unser Importeur hatte beides schon richtig getrennt (`Facing = raw[0x02]`,
  `Aim = raw[0x03]`).
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

1. ⚠ **ERLEDIGT und WIDERLEGT am 21.08.2026 — siehe Abschnitt U.** Es gibt
   keinen benutzbaren Determinismus-Prüfstand: die Feldnamen kommen aus einer
   Bildschirm-Einblendung für EINE Einheit, und das Abspielen ist in beiden
   ausgelieferten Bauten unfertig (null Aufrufer, kein `fread`). Der Absatz
   bleibt als Beleg stehen:

   **Das Original hat einen eigenen Determinismus-Prüfstand.** Marken
   `RECORDING` / `REPLAY`, Dateien `replay.beg` (Anfangszustand), `replay.mes`
   (Befehlsstrom), `replay.txt` (Protokoll). Er schreibt je Takt **56 benannte
   Felder jeder Einheit** heraus und vergleicht sie beim Abspielen. Im ganzen
   Baum: null Treffer. Genau die Frage, an der »Multiplayer online« hängt — und
   nebenbei die vollständige, geordnete Feldliste des Einheitensatzes aus dem
   Mund des Spiels.
2. **Die Reihenfolge des Haupttakts steht im Klartext da** — ⚠ **die Zahl
   hier war falsch, berichtigt am 20.08.2026.** Es sind nicht 44 Stationen mit
   28 Würfelpunkten, sondern **rund 85 Protokollpunkte**, davon 32 Würfelmarken
   (`rnd A`, `rnd b`, `rnd c` … `rnd p`). Die 44 stammten aus einer alphabetisch
   sortierten Etikettenspalte, nicht aus der Aufruffolge. Die echte Folge steht
   zwischen C `0x415CF0` und `0x4168A7` und lässt sich so auslesen:
   jeder `push <Zeichenkette>` vor dem Protokollaufruf benennt eine Station.

   **Die Ankerpunkte:** `CPU` 14 (0x41618C) · `Power` 16 · `Trains` 21 ·
   `Transported` 22 · `Self-defenders` **63** · `Buildings` **64** (0x416683) ·
   `Movement` **65** · `Airplanes` 69 · `marks` 79. ⚠ **Ab `Self-defenders` war
   unsere Zählung um eins zu niedrig** — berichtigt am 21.08.2026, siehe
   Abschnitt T.

   ⭐ **Was davon gebaut ist (20.08.2026):** die Geschwindigkeitsschleife
   (`SimHz` 50 statt 60, 1…3 Takte je Zeitgeberschlag), die **zwei getrennten
   Durchgänge** (erst alle Gebäude `0x43CA50`, dann alle Einheiten `0x406CD0`
   — vorher wechselte eine Schleife beides nach Listenstelle ab), `UpdateAi`
   auf Station 14, und die vier verschobenen Stationen `Trains`,
   `Transported`, `Airplanes`, `unexplored`, `marks`. Prüfstand
   `--takt-check`.

   ⚠ **Es sind 29 Würfelzüge, nicht 32** (21.08.2026 in beiden Bauten
   nachgezählt), und die `rnd`-Marken sind **Kontrollpunkte, keine
   Verbraucher**. ⭐ **Die fünf fehlenden Stationen sind gelesen** — Abschnitt T.
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

---

## E. Kartenbau (19.08.2026) — der Baumbefund und was daneben abfiel

Die Verdeckung ist **gebaut und gemessen** (siehe CHANGELOG). Was aus derselben
Recherche offen blieb oder berichtigt gehört:

### ⚠ BERICHTIGT: »in Kampagne 1 brennen von Haus aus ein paar Bäume«

Lässt sich an den Daten **nicht** belegen. Über alle 23 mitgelieferten `.CWM`
hat **jeder einzelne der 37.231 Waldeinträge den Zustand 1 (steht)** — kein
einziger im Brandbereich 2…254. Wenn im Original etwas brennt, setzt es das
**Missionsskript**, nicht die Kartendatei.

**Was mir hülfe:** ein Standbild aus dem Let's Play von Mission 1 direkt nach
dem Start. Brennt dort etwas, ohne dass geschossen wurde? Dann suche ich die
Regel; wenn nicht, ist der Punkt erledigt.

### Sektion 20 der `.CWM` ist eine ZEICHENLAGE je Zelle — und wir lesen sie nicht

`0x542E18` (F `0x541E78`), 256×256 Byte. Aus den beiden Toren @`0x4B4274` und
@`0x4B4485` ergibt sich die **Wirkung**:

| Wert | Wirkung |
|---|---|
| `0` | Vorgabe — die Belegungskarte entscheidet |
| `1…98` | Zelle wird in den **flachen** Durchgang gezwungen (liegt unter allem) |
| `99` | Zelle wird von **beiden** Durchgängen übersprungen (unsichtbar) |
| `≥ 100` | Zelle kommt in den **verzahnten** Durchgang (verdeckt) |

Geschrieben wird sie von den Funktionen zu **Brücke, Rampe und Gebäudebau**
(`0x43CB12` schreibt 99, `0x43DE88` schreibt 98, `0x4CC4F0`/`0x4CC889` einen
laufenden Wert, »Erase bridge« @`0x4CB0A0` löscht drei Spalten auf 0).

**Was uns das kostet, gemessen:** **578 Zellen** über alle Karten haben
`imap == 0xFFFF` und ein Byte von 0 oder ≥ 100 — die zeichnet das Original im
verzahnten Durchgang, unsere `MapForest.ImZeilenfach()` lässt sie durchfallen.
Auf **map_01 sind das 10 Zellen** (558 statt 568). Die Kachelcodes (10001…10071,
40×38 bis 40×48 px) und die Bytegruppen 100…108 sprechen für **Brücken und
Rampen**.

⚠ **Die Bedeutung der Zahlen ist nicht gelesen**, nur ihre Wirkung. Ob 1…98 eine
Bauwerksnummer ist oder eine Lage, ist offen.

**Was mir hülfe:** ein Blick ins Original auf eine Karte mit **Brücke**
(04.CWM hat 40 solcher Zellen, NET05 hat 77). Läuft eine Einheit über die
Brücke *darüber* oder *darunter durch*?

### Der Schatten ist im Original eine EIGENE LAGE

Zweiter Durchgang @`0x4B4342`, über **alle** Zeilen, eigener Zeichner
`0x42C8C0` — also zwischen Boden und allem Beweglichen. Ein Schatten liegt im
Original **nie** auf einer Einheit und nie auf einem Baum. Bei uns laufen die
Gleisschatten **im** verzahnten Durchgang mit. Sichtbar wird der Unterschied,
sobald zwei Einheiten dicht beieinanderstehen. **Nicht gebaut.**

### Unsere Grundierung ist unsere Zutat

Der Zeichner des Originals malt **je Zelle genau ein Sprite**. Einen zweiten
»Grundierungsblit« wie unseren Durchgang A in `MapBaker.cs` gibt es nicht.
Ob er schadet, ist **nicht gemessen** — er kann Übergangs- und Küstenkacheln
anders aussehen lassen als im Original.

**Was mir hülfe:** ein Standbild des Originals mit einem einzelnen Baum auf
einer Übergangskachel.

### Die Auswahlmarkierung — ANKER ERLEDIGT, Aussehen offen

⭐ **Der Anker ist berichtigt (19.08.2026).** Gemeldet: »sitzt noch nicht ganz
mittig«. Am Bildschirmfoto nachgemessen (Massstab 1,625, gewonnen am Rahmen des
Lebensbalkens: 26 Bildpunkte fuer die gezeichneten 16): die vier Winkel standen
mittig auf **283,0**, der Balkenrahmen auf **283,5** — der Kranz sass also genau
auf `Entity.Pos`, dem Bodenpunkt. Zwischen dem und der sichtbaren Bildflaeche
liegen laut `--stempel-check` bei einzelligen Einheiten im Mittel **6,7** und bis
zu **11** Bildpunkte.

Er haengt jetzt an der schattenfreien Mitte des Bildes. `--auswahl-check`
(ebenfalls neu) hat dabei die **erste** Fassung verworfen — mit der geometrischen
Leinwandmitte wurde es auf Kampagne 5 schlechter statt besser:

| | Kampagne 1 | Kampagne 5 | Kampagne 20 |
|---|---|---|---|
| vorher, mittlerer Abstand | 6,2 / 9,0 | 8,4 / 9,0 | 4,4 / 3,8 |
| jetzt | **1,6 / 0,3** | **1,5 / 0,6** | **2,7 / 0,3** |
| groesster Abstand | 20,7 → **4,6** | 20,7 → **4,6** | 20,8 → **4,6** |

**Was weiter offen ist: das Aussehen.**

Gemeldet: »diese originale Markierung um die Einheiten sitzt noch nicht ganz
genau.« Zu Recht — unsere vier Winkel sind **am abgefilmten Original
ausgemessen**, nicht gelesen. Gelesen ist jetzt:

Bei gesetztem `OZNACEN` (Satz `+0x1B`, der Name stammt aus `ENTITY_FELDER.md`)
zeichnet das Original @`0x42AA88` **ein Sprite**:

```
sub ebx, 7            ; x -= 7
sub ebp, 0x12         ; y -= 18
push ebx / push ebp
eax = [0xA31C04] + 0xA183E8      ; der Bildzeiger
call 0x401B1D                    ; derselbe Blitter wie fuer Geschosse
```

Zwei Fundstellen je EXE, **beide mit denselben Zahlen 7 und 18**. `0xA183E8` ist
derselbe Bildblock, aus dem auch die **Mauszeiger** kommen (@`0x4A9F14`) — also
der Anhang von `ROBO.CWR`. Dessen Eintrag **7** ist genau das gesuchte Zeichen:
vier Winkel mit den Spitzen nach außen, wie im Tutorialfenster.

⚠ **Was NICHT zusammenpasst und deshalb nicht gebaut ist:** dieser Eintrag misst
**58×58** Bildpunkte. Ein 58×58-Bild mit dem Anker (−7, −18) läge völlig
daneben. Entweder ist es ein anderes Bild, oder der Anker ist nicht die linke
obere Ecke. **Solange das nicht geklärt ist, bleibt unsere gezeichnete Fassung
stehen** — eine falsche Zahl einzusetzen wäre schlechter als die gemessene.

**Was mir hülfe:** ein Bildschirmfoto aus dem Original, in dem **eine einzelne
Einheit angewählt** ist, möglichst formatfüllend. Daran messe ich, wie weit die
Winkel wirklich auseinanderstehen und wie hoch die Markierung über dem
Bodenpunkt sitzt.

---

## F. Schiffe und Waffen, zweite Tiefenlesung (19.08.2026)

### ⭐ ERLEDIGT: Feld +0x14 ist eine HÖHE

Vormittags stand hier »Aufschlag auf die Lafettensuche, Bedeutung ungeklärt«.
Die Kette endet zwei Sprünge weiter: `0x435BD0 → 0x4B5CE0 → 0x401AAF`, und der
Schwanz `mov cl,0x0F / imul cl / add al,bl` (C `0x4B6129`, F `0x4B5A5B`, das
Muster gibt es in beiden EXE genau 29-mal) liefert **Zellhöhe · 15 + Anteil in
der Kachel**. Eine Geländestufe = **15 Einheiten**. Die Werte 10…30 sind also
zwei Drittel bis zwei Stufen. Gegenprobe: derselbe Maßstab beim Einschlag
(Einheit +15, Gebäude +60). Umbenannt in `MuzzleHeight`.

⚠ Und `0x435BD0` ist **keine** »Lafettensuche«: sie bekommt
`(KOLIK, POHYB, &Satz[0], &Satz[1])` und rechnet den **Bildpunkt der Einheit in
ihrer Zelle** aus dem Fahrfortschritt.

### ⭐ GEBAUT: Zwillingslafette und Zielstreuung

Beide Rohre feuern **im selben Takt** (0x40C35E und 0x40C449), Versatz senkrecht
zur Rohrrichtung, Rohr A `+v`, Rohr B `−v`. Vor **jedem** Schuss wird der
Zielpunkt verwürfelt: `x −(zufall mod 20)+9`, `y −(zufall mod 10)+4` — beide
Rohre streuen unabhängig.

⚠ **Berichtigt:** in Abschnitt B stand »**vier** Geschosstypen feuern zwei
Geschosse«. Vier sind die *Werte* (0/6/8/20), die *Arten* sind **21**. Baubare
Waffenbauteile sind davon aber nur **drei**: 26, 27 und 31 — nachgezählt über
`stats[zeile][+0x1C] → Geschossart → +0x15`.

**Was mir hülfe:** halbiert das Original den Schaden je Rohr? Ich habe es nicht
gelesen. Falls Dir diese drei Waffen im Spiel zu stark vorkommen, sag es — dann
suche ich gezielt danach.

### ✅ GEBAUT 19.08.2026: die Wurfbahn  (Scheitelteiler/BogenHoehe)

Die Bahnart hängt **allein an der Geschossart**, an einer fest verdrahteten
Tafel (`byte[0x4530DC + art]` → `[0x453064 + 4·klasse]`), nicht an Reichweite
oder Waffengattung. Drei Rümpfe: gerade, **Wurfbahn** (22 Arten) und ein
Steig-/Sinkprofil (nur Art 7, der Marschflugkörper).

Die Rechnung beim Anlegen (`0x451DF4`): `h = d/(2v)`, `g = d / (h(1+h)/2) / K`,
`vz = h·g`; je Takt `Bogen += vz`, `Höhe = Grundhöhe + round(Bogen)`, `vz −= g`.
**Der Scheitel ist exakt `d/K`** — die Summe kürzt sich weg. `K = 11` (flach)
für 14 Arten, `K = 2` (steil) für 8. Probe, dass die Lesung stimmt: die
Anlegeroutine hat eine **eigene** Tafel, und deren Wurfbahn-Arten sind genau
dieselben 22.

Art 7 steigt in Siebenerschritten bis Höhe 150 (zehn Stufen), marschiert und
stürzt, sobald `(Zielhöhe−Höhe)/7 + 1 ≥ Restschritte`.

### ✅ GEBAUT 19.08.2026: die acht Einschlagszweige  (SchwelleAn)

Vor der Trefferprüfung holt das Spiel die Geländehöhe am Geschosspunkt und
vergleicht `Gelände + Schwelle` gegen die Geschosshöhe:

| Belegung | was | Schwelle |
|---|---|---|
| < 8000 | lebende Einheit | **+15** |
| ≥ 8000 mit Lagenbyte 1…59 | aufragendes Hindernis | **+40** |
| 10000…13999 | Infanteriezelle | +15 |
| 50000…55999 | Wald | +40 |
| 60000…60299 | **Gebäude** | **+60** |
| 61000…63999 | zerstörbares Objekt | +30 |
| sonst | Boden | +0 |

Dazu: **kein Eigenbeschuss** (`byte[0x87B155 + 40·schütze + ziel]`), ein Ziel mit
`UKOL ≥ 100` wird durchflogen, und die Arten 5…20 ziehen eine **Rauchspur**
(Effekt 42 + zufall mod 3, mit 1/3 bzw. 1/2 Wahrscheinlichkeit je Takt).

### ⭐ NICHT GEBAUT, aber wertvoll: SHOOT.CWT ist die Mündungstafel

Waffenindex = **`ZBRAN − 1`** (`0x429B8E`), Bild = dasselbe wie beim Turm,
Mündung = Einheitenanker + **`(x − 25, y − 68)`** (`0x42A188`). Gemessen an der
Datei: 9600 Sätze, davon **413 mit Flag 0** (werden gezeichnet), **756 mit
Flag 1** (Punkt vorhanden, aber hinter dem Turm), 8431 leer. Belegt sind genau
die Plätze 0…17 — und das Spiel hat 18 Waffenbauteile. Waffe 17 hat als einzige
**vier** Mündungen in einer Reihe (ein Vierlingsgestell).

Damit liesse sich unsere geratene `MuzzleReach = 14` ersetzen.

⚠ **Und der Negativbefund »es gibt kein Mündungsfeuer« ist zu präzisieren:** er
stimmt für die *Schussroutine*, die legt keinen Effekt an. Der **Zeichner** malt
aber sehr wohl eines aus SHOOT.CWT, solange `byte[Einheit+0x42]` läuft. Unser
`Kind = "muzzle"` ist im Grundsatz richtig — es sitzt nur am falschen Ort.

### ✅ GEBAUT 19.08.2026: Transport — die Karte liefert ihn beladen aus (sec37, Abschnitt J)

Tafel `0xBBFEF8`, 100 Plätze zu 38 Byte: `+0x00` belegt, `+0x02` Schiff,
`+0x04` Anzahl, `+0x06…+0x23` fünfzehn Einheitenindizes, `+0x24` Gewicht.
Fahrzeug +5 (abgelehnt über 10), Infanterie +1 (abgelehnt über 14) — also
**drei Fahrzeuge oder fünfzehn Mann**, gemischt erlaubt. Beladen nur aus einer
der vier orthogonalen Nachbarzellen und nur, wenn die Einheit steht.
Rampenmarken in der zweiten Rasterebene: **≥ 100** beladen, **≥ 200** entladen.

⚠ **Beim Versenken wird die Ladung gelöscht, nicht abgesetzt** (`0x410E9B` →
`0x4CEE00` → je Einheit `0x410E60`, dort `byte[+0x09] = 0xFF`).

### Was ein versenktes Schiff hinterlässt: nichts

Nur **Landfahrzeuge** legen ein Wrack an (`0x4A97C0`, 1000 Plätze zu 10 Byte).
Ein 2×2-Schiff setzt seine Zellen auf `0xFFFC` (Wasser), ein 4×4 alle vier.
Kein Wrack, kein Ölfleck.

### Wie ein Schiff dreht

`0x404E80`: Gattung 3/4/5 rechnen in Sechzehnteln (`Ziel = wunsch·2`), und das
Feld `OTACIM` (+0x16) bremst: **eine Stufe alle drei Takte**. Gattung 5 (4×4)
tut ausserdem nur an geraden Takten etwas — also **alle sechs**. Volle Drehung:
48 bzw. 96 Takte. Der **Turm** (+0x03) dreht dagegen ohne Bremse, eine Stufe je
Takt.

⚠ Wir drehen den Rumpf derzeit **jeden** Takt. Das ist zu schnell.

### Kleinere Berichtigungen

* **`+0x2A` ist eine MINDESTreichweite**: `0x40BF7F` bricht ab, wenn
  `+0x2A · 40 > Entfernung`. In `ENTITY_FELDER.md` steht nur `+0x2B = range`.
* **Die Mittelpunktregel für 4×4** ist `40·spalte + 78`, nicht +80 — zwei
  Bildpunkte, aber gemessen (`0x40BE41`).
* **`Satz+0x1E` zählt NICHT in Achterschritten**, sondern wählt den
  Neigungsblock: steigend 0x10, fallend 8, sonst 0. Damit ist die offene Frage
  aus Abschnitt B beantwortet — **drei Blöcke zu acht**.

### Was offen blieb

* **Wer `NABYTO` (+0x32) herunterzählt.** Ausgeschlossen ist jede Form mit der
  Absolutadresse (in beiden EXE null Treffer für `dec word` / `sub …,1`); der
  Zähler läuft über einen Registersockel. Es bräuchte einen Durchgang durch den
  Einheitentakt `0x406D20`.
* **Das EINlaufen ins Dock** — nur das Auslaufen ist gelesen. Ansatz:
  `'Seedock'` (0x4FDD8C) und `'Error in shipyard'` (0x4FAE74).
* **Die y-Halbierung beim Einschlag**: `0x4528EF` übergibt `py/2`, `0x435BD0`
  dagegen `dy+10` — einer der beiden rechnet mit halber senkrechter Auflösung.
  **Was mir hülfe:** ein Blick ins Original auf ein Geschoss über einer Steigung.

### SHOOT.CWT — nachgemessen, aber der ANKER fehlt (19.08.2026)

Selbst nachgemessen, und der Bericht stimmt auf den Eintrag: **28.800 Byte =
9.600 Sätze zu drei Byte**, Flags **413 / 756 / 8.431** (gezeichnet / vorhanden
aber verdeckt / leer). Belegt sind die Waffen **0…17** sowie 40 und 41 — und das
Spiel hat 18 Waffenbauteile. Waffe 17 hat als einzige **vier** Mündungen je Bild,
die Waffen 4, 5, 6, 10, 13, 15, 40, 41 haben zwei.

**Was fehlt, um es zu bauen:** die Zuordnung des Ankers. Der Zeichner setzt die
Mündung auf `Zeichenpunkt + (x − 25, y − 68)`. Die 25/68 sind der Ankerpunkt
*des Originals* in seinem Waffenbild; unser zusammengesetztes Bild hängt an
`ComposedAnchor = (30, 55)`. Welcher Punkt dem anderen entspricht, ist **nicht
gelesen** — und ohne das wäre jede Umrechnung geraten. `MuzzleReach = 14` bleibt
deshalb vorerst stehen und ist weiter als unsere Setzung gekennzeichnet.

**Was mir hülfe:** ein Bildschirmfoto aus dem Original, auf dem ein Panzer
gerade schiesst, möglichst formatfüllend — daran messe ich, wo die Mündung
relativ zum Fahrzeugbild sitzt, und habe den Anker.

---

## G. Zwei Messungen an den Originalbildern (19.08.2026 abends)

> ⚠⚠ **BERICHTIGT NOCH AM SELBEN ABEND — DER MASSSTAB WAR FALSCH.**
> Ich habe unten mit **2,156** gerechnet, in der Annahme, die Bildfläche von
> 1380 Bildpunkten seien 640 Spielpunkte. Das Spiel hat aber eine
> **Auflösungstafel** (`0x538858`, fünf Einträge, in beiden GAME.EXE über die
> Form gefunden und byte-gleich): **640×480, 800×600, 1024×768, 1280×1024,
> 1600×1200** — und sie wird direkt an `SetDisplayMode` weitergereicht. Das
> Video läuft auf **800×600**, der Maßstab ist **1,725**.
>
> Drei unabhängige Messungen belegen das: die Zeichenvorschübe von
> »Heavy Tank« aus FONT.CWD (alle acht Abstände 1,71…1,72), die Grundperiode
> der Bodentextur (3,45 px = das 2-Punkt-Raster) und die Größe des Panzers
> (bei 2,156 wäre er nicht höher als seine bloßen Ketten).
>
> **Alle Zahlen unten sind damit um 25 % zu klein und mit 1,25 zu
> multiplizieren.** Insbesondere:
>
> * **Die Flamme ist RICHTIG.** Original 34×64 (stehend) und 38×43 (seitlich)
>   gegen ANIM.CWA 550 = 41…46 × 65…73 und 552 = 34…40 × 45…58. Meine „1,7-mal
>   zu groß" verglich außerdem die **Leinwand** (60×79 = `max(w)` über alle
>   sieben Bilder) mit der *Farbfläche* des Originals — zwei verschiedene Dinge.
>   **Nichts zu ändern.**
> * Das Mündungsfeuer wird aus 5,1×4,2 zu **6,4×5,2**, seine Lage aus
>   (+6,7/−2,1) zu **(+8,4/−2,6)**.
>
> Die Lehre steht in der Arbeitsweise: **ein Maßstab ist eine Messung und keine
> Annahme.** Ich hatte „640" gesetzt, weil das Spiel von 1997 ist.



Der Spieler hat `mündungsfeuer.png` (aus dem Let's Play) und die sechzehn
`kampagne1 original tutorial*.png` bereitgestellt. Maßstab in beiden: die
Kartenfläche ist **1380 Bildpunkte für 640** → **2,156**, an den schwarzen
Balken gemessen und in beiden Bildern gleich.

### Das Mündungsfeuer — gemessen, aber nicht baubar

| | Original | wir |
|---|---|---|
| Größe | **5,1 × 4,2** Spielpunkte | Folge 232: **30 × 27** |
| Lage | **(+6,7 / −2,1)** von der Rumpfmitte | `ShotOrigin + Richtung · 14` |

**Unser Mündungsfeuer ist rund sechsmal zu groß.** Das ist die Antwort auf die
Frage von heute mittag, ob es „zu hell wirkt" — es ist nicht zu hell, es ist zu
groß.

⚠ **Warum ich es trotzdem nicht geändert habe.** Beides fehlt noch:
* Die **Folge** ist im Original eine Variable, keine Konstante:
  `0x42A1C1` liest `word[edx*4 + 0x7A404A]` mit `edx = word[esp+0x22]` — die
  Nummer kommt vom Aufrufer. Welche es ist, ist nicht verfolgt.
* Die **Lage** hängt an der Blickrichtung: SHOOT.CWT führt je Waffe und je Bild
  einen eigenen Punkt (`Zeichenpunkt + (x−25, y−68)`, `0x42A188`). Die gemessenen
  (+6,7 / −2,1) gelten nur für die eine Richtung auf diesem Bild. Eine feste Zahl
  daraus zu machen wäre schlechter als die jetzige Rechnung.

**Was mir hülfe:** zwei bis drei weitere Standbilder desselben Panzers beim
Schuss in **verschiedene Richtungen**. Damit habe ich die Punkte je Richtung und
kann die Tafel anhängen, ohne den Anker zu erraten.

### Das Feuer — die Folge stimmt, die GRÖSSE nicht

⭐ **Bestätigt:** `kampagne1 original tutorial7.png` zeigt zwei brennende Bäume
**nebeneinander mit zwei verschiedenen Flammen** — links die seitliche mit
weißglühender Spitze, rechts der aufrechte Feuerball. Unsere Wechselfolge
550/552 ist also richtig. Und die Sprungtafel bestätigt den Weg unabhängig:
Art 12 → Bytetafel `0x42BC10` Index 9 → `0x42BBD0` → **`0x42B422`**, dort
`edi = (index&1)·2 + 0x226` und `word[edi·4 + 0x7A404A]` als Anfangsbild.

⭐ **Ebenfalls beantwortet:** in `kampagne1 original tutorial.png` brennt ein Baum
**mitten im dichten Wald**, und die Kronen davor verdecken seinen unteren Teil.
Der Eindruck „strange" ist also **originalgetreu** — nichts zu ändern.

⚠ **ABER die Größe passt nicht, und das ist ungeklärt.** Gemessen an drei Feuern
in `mündungsfeuer.png` und zweien in `tutorial7.png`:

| | Original | unser Sprite (heller Kern) |
|---|---|---|
| Flamme | **23…29 breit, 31…45 hoch** | `blast` 44 × 70, `blast2` 35 × 52 |

Rund **1,7-mal zu groß**, und das bei derselben gelesenen Folge — die Sprites
kommen ja aus ANIM.CWA 550/552. Entweder exportieren wir die falschen Bilder
dieser Folge, oder der Maßstab 2,156 gilt für die Flammen nicht.

**Was mir hülfe:** ein Standbild, auf dem ein brennender Baum und eine Einheit
dicht beieinander stehen. An der Einheit habe ich einen zweiten Maßstab und
kann entscheiden, ob es an unserem Bild oder an meiner Rechnung liegt.

---

## H. Abgearbeitet am 19.08.2026 spätabends

### ⭐ Der Zielkasten im Vorschaufenster — GEBAUT

Er fehlte ganz: `objectives.json` war exportiert und wurde auf dem Schirm nirgends
gezeichnet. Jetzt an der gelesenen Stelle (`0x45C2A3`…`0x45C31A`): **x 355,
y 390, Zeilenabstand 11, Umbruch bei x > 490**, und in **FONT2.CWD** — dem
einzigen Ort, für den das Original die zweite Schrift überhaupt einwechselt.

⚠ Der Umbruch des Originals ist ein **Zeichen-**, kein Wortumbruch (er schaut
erst beim nächsten Leerzeichen). Godots `WordSmart` bricht früher; das ist
unsere Abweichung und fällt nur bei langen Wortketten auf.

### ✅ Sektion 20, die Zeichenlage — GEBAUT UND WIRKSAM (19.08.2026)

⚠ Der Vorbehalt unten (»wirkt erst nach einem neuen Einlesen«) ist **erledigt**:
die CDs waren gemountet, alle 54 Karten sind neu gebacken. Gemessen ueber alle
Karten: **125 971 aufragende Objekte, davon 980 nur ueber die Zeichenlage**.
`map_01` meldet die erwarteten 568.


Die Aufnahmeregel des verzahnten Durchgangs ist jetzt die des Originals
(selbst nachgelesen, `0x4B446C`…`0x4B4491`):

```
imap in [50000, 64000)  -> Lagenbyte pruefen
imap == 0xFFFF          -> Lagenbyte pruefen
sonst                   -> ueberspringen
Lagenbyte: 0 -> zeichnen · 1..99 -> ueberspringen · >=100 -> zeichnen
```

Vorher standen dort zwei Bereiche und kein Lagenbyte. Damit kommen die **578
Zellen** herein, die das Original aufragen lässt — Brücken und Rampen.

⚠⚠ **Die Änderung wirkt erst nach einem neuen Einlesen.** Die gebackenen Karten
liegen fertig im Nutzerordner, und die `.CWM`-Quellen liegen **nicht** in
`F:\Akte Europa` — sie kommen von den CDs. Ein Neubacken war hier deshalb nicht
möglich. Damit es beim nächsten Mal auffällt, zählt der Backofen die Zellen und
sagt sie: *„… davon N nur über die Zeichenlage (Sektion 20, Brücken und
Rampen)"*. Steht dort **0**, hat die Regel nicht gegriffen.

**Was mir hülfe:** einmal `--reexport-maps=<CD-Pfad>` laufen lassen und mir die
Zeile zeigen. Erwartet sind rund 578.

### ✅ Der Transport — GEBAUT (19.08.2026), Einzelheiten in Abschnitt J

Die Zahlen sind nachgeprüft, nicht übernommen. Ich habe die Laderoutine
`0x4CEE80` selbst gelesen:

```
al = byte[einheit*78 + 0x6E2708]     ; Einheit +0x40 = ihr Transportplatz
cl = byte[schiff*78  + 0x6E26D2]     ; +0x0A = Gattung
  == 0 -> Fahrzeugzweig · == 1 -> Infanterie
  sonst -> 'Wrong type of unit tries to go in transport ship'
Fahrzeugzweig: al = byte[19*platz*2 + 0xBBFF1C] ; cmp al,0x0A ; jbe weiter
```

`19·2 = 38` ist die Schrittweite, und `0xBBFF1C − 0xBBFEF8 = 0x24` das
Gewichtsfeld — beides deckt sich mit der Tiefenlesung. Grenze 10 für Fahrzeuge
(+5 je Stück → drei), 14 für Infanterie (+1 → fünfzehn).

⚠ **Warum er trotzdem nicht gebaut ist:** das Entladen hängt an den
**Rampenmarken**, und die stehen in Sektion 20 — der Zeichenlage, die die
Laufzeit **gar nicht kennt**. Ohne sie könnte ein Schiff nirgends absetzen.

**Deshalb ist der erste Schritt jetzt gemacht:** der Kartenausgeber schreibt die
Rampenzellen in die Meta (`ramps`, je Zelle mit ihrem Lagenbyte). Damit hat die
Laufzeit die Daten, sobald neu eingelesen wurde — und dann ist der Transport
eine überschaubare Sache.

⚠⚠ Wie die Zeichenlage selbst: **wirkt erst nach `--reexport-maps` von den CDs.**


---

## I. Was die zweite CD gebracht hat (19.08.2026, Nacht)

⭐⭐⭐ **Die Kampagne ist zum ersten Mal vollständig.** Bis heute hatten wir
**15 von 33** Kampagnenkarten — die Missionen **16 bis 33 lagen auf CD 2** und
waren im Projekt gar nicht vorhanden (18 `.CWM`, 57 Geländesatzdateien, 16
KI-Dateien; zusammen 67 MB). Die *Regeln* aller 33 standen längst in
`mission_scripts.json` und alle 33 Einsatzbesprechungen in `briefings.json` —
es fehlte nur das **Gelände**.

Nach dem Backen von beiden Datenträgern:

| | vorher | jetzt |
|---|---|---|
| Kampagnenkarten | 15 | **33** |
| Vorführungen (`.DM`) | 3 | **13** |
| Netzkarten | 8 | 8 |

Stichprobe: Mission 20 (118 Einheiten, 35 Gebäude), 25 (136/57), 33 (184/80) —
alle laden mit Gelände, Regeln und Skriptplätzen und treffen keine sofortige
Entscheidung.

⚠ **Die Installation `F:\Akte Europa` enthält KEINE einzige `.CWP/.PAL/.CWG/.CWS`** —
das Gelände liest das Original zur Laufzeit von der CD. Wer neu einliest, braucht
also **beide** Datenträger.

### Was auf den CDs weiterhin ungenutzt liegt

| | Umfang | Stand |
|---|---|---|
| **Filme (`.RPL`)** | 35 Stück, 853 MB, **70,7 min** | keiner wird abgespielt |
| **KI-Dateien `AI*.CWI`** | 43 × 2968 B, alle verschieden | ungelesen |
| **`HELPG.PIC` / `ENCYCLOG.PIC`** | 36 + 96 Bilder à 60×60 | ungelesen |
| **`SPR.DAT`** | 31 KB, 57 Einträge | Format halb gelesen |
| **`MARK.CWK`** | 640 B, 320 Bytepaare | Bedeutung offen |
| **`game.007`** | echter Spielstand des Originals | ungenutzt |

**Die Filme** laufen im Original nach *jeder gewonnenen Mission* (Zustand `0xC8`
→ `0x4CF930`), dazu Vorspann und Abspann nach Mission 33. Format ist ARMovie/RPL
mit Escape 124 — FFmpeg kann beides. Die CD-1-Hälfte liegt schon im Baum.

⭐ **`game.007` ist ein Prüfstand, den keine Rechnung ersetzt:** ein echter
Endzustand von Mission 1 im `.CWM`-Format, den unser eigener Zerleger vollständig
liest (131 Abschnitte, 0 Byte Rest). Damit liesse sich unsere Simulation gegen
den Ausgang des Originalmotors prüfen statt gegen unsere eigene Deutung.

⚠ **Berichtigung an `GAMESTATE_RE.md` §3.85:** dort steht, drei `.DM`-Missionen
hätten gar keine `.CWM` (Sätze 21, 25, 26). Das war nur wahr, solange CD 2
fehlte — `21.CWM`, `25.CWM` und `26.CWM` liegen auf `E:\LEVELS`.

⚠ **Nicht alle `.DM` haben 131 Abschnitte:** gemessen 1× 131, 8× 122, 4× 120.
Die 120er/122er sind vom 08.07.1997, `1.DM` vom 04.08.1997 — ein älterer
Formatstand. **`1.DM` ist damit die einzige Datei auf beiden CDs mit allen 131
Abschnitten** und der beste Prüfgegenstand für alles jenseits von `sec120`.

---

## J. Die Abschnitte der Kartendatei — was wirklich fehlt (19.08.2026)

Ein Prüflauf über alle 130 Abschnitte hat die Tafel in `CwmSections.Sizes`
Zeile für Zeile bestätigt (130 von 130, 0 Abweichungen) und den Schnitt
zwischen `.CWM` und `.DM` an einer Adresse festgemacht: **Kopfbyte 3**
(`@0x41E6A5`, `cmp cl,2 / jne`) — `.CWM` = 1 → sec1..sec38, `.DM` = 2 →
sec1..sec131. Es gibt im ganzen Lader nur diesen einen Zweig.

### ⚠ Zwei Berichtigungen an einem Bericht, den ich fast übernommen hätte

**1. »Wir lesen sec22 (die Gleise) nicht« — falsch.** Wir lesen sie bis in den
Zeichner (`CwmExtra.RailCells` → `rail_cells` → `MapEntityLayer`). Gemessen
nach dem Neuschreiben: **27 Karten, 19 977 Gleiszellen**, `map_32` allein 1986
— genau die Zahl, die in der Datei steht. Was gefehlt hat, war nicht der
Leser, sondern die **Dateien**: die `.entities.json` der Missionen 16–33 gab es
nicht, weil es die Karten nicht gab.

⚠ **Mein eigener Messfehler dabei, und er ist die Lehre:** mein erster Zähllauf
bekam einen MSYS-Pfad (`/c/Users/...`), fand **null Dateien** und meldete
seelenruhig »0 Karten, 0 Zellen«. Ich hätte daraufhin einen funktionierenden
Leser neu gebaut. **Ein Zähler, der nichts findet, muss sagen können, wieviel
er angesehen hat** — sonst ist »0 Treffer« nicht von »0 Versuche« zu
unterscheiden.

**2. Die Satzform von sec37 stimmte nicht.** Gemeldet war »+0x04, 16 Griffe,
Deckel immer 15«. Gemessen ist es **+0x06, 15 Griffe** (2+2+2+15·2+2 = 38 geht
auf) und der Deckel ist **0, 12, 13 oder 15** — er hängt am Rumpf.

### Was wir nach dieser Prüfung wirklich nicht lesen

| Abschnitt | Inhalt | Menge über die `.CWM` |
|---|---|---|
| **sec17** | Brücken und Molen, 100 × 24 | 92 |
| **sec21** | Rampentafel, 50 × 4 | 78 |
| **sec25 / sec29** | Zustand von Depot und Seedock | 3 / 27 |
| **sec38** | Terranium-Vorkommen der Karte, 50 × 14 | 9 |
| **sec16** | Infanteriezellen, 4000 × 22 | auf 26 von 49 Karten |

⚠ Zu **sec38** eine Klarstellung an unseren eigenen Notizen: `sec28` ist
**nicht** die Vorkommenstafel, sondern der Zustand der Minen-*Gebäude*. Die
freien Vorkommen, auf die man eine Mine setzt, sind sec38.

**Sauber ausgeschlossen** (kein Kartendatum, sondern erst Spielstand):
Minen (sec84), Fallen (sec85), Radare (sec86), Gas (sec82, **0 belegte Sätze
in allen 62 Dateien**). Die **Selbstverteidiger** stehen in *gar keinem*
Abschnitt: ihre Tafel `0x53d8d8` liegt genau am Ende von sec126, und der
Nullfüllblock des Laders hört bei `cmp eax,0x53d8d8` auf. Sie werden nicht
gespeichert. Ebenso gibt es **keinen Abschnitt mit Programmform** — die
Missionslogik liegt in der EXE, nicht in der Karte.

### sec37 — der Transport, jetzt eingebaut

Die Karten liefern **beladene** Transporter aus. Der Zuteiler `@0x4CED60`
(F: `@0x4CE910`) meldet »Too many transport ships«: 100 Plätze zu 38 Byte.

```
+0x00 u16 belegt   +0x02 u16 Einheitenplatz des Transporters
+0x04 u16 Anzahl   +0x06..+0x22  15 × u16 Griffe   +0x24 u16 Deckel
```

⚠ **Man darf die Liste nicht durchlaufen.** Sie enthält Karteileichen; gültig
ist ein Satz nur, wenn die **Einheit** in `+0x40` auf ihn zeigt. Gemessen über
beide Datenträger: **30 gültige Sätze auf 7 Karten mit 65 geladenen
Einheiten, 0 Zeiger auf einen fremden Satz.** Wer stattdessen die Liste
durchläuft, bekommt allein auf `05.CWM` 27 Einheiten zu viel.

Fracht ist an **UKOL 57** (+0x14) kenntlich — alle 65 tragen sie, keine freie
Einheit tut es, 0 Ausnahmen. Transporterrümpfe sind **2, 70, 72, 73**; die 2
heißt: es gibt auch einen **Landtransport**, nicht nur Schiffe.

**Eingebaut:** die Fracht wird nicht mehr aufgestellt. Im Original steht sie
auch nicht auf der Karte — auf `05.CWM` teilen sich fünfzehn geladene
Einheiten die Zelle (5,11), was für aufgestellte Einheiten unmöglich ist.
Prüfstand `--transport-check`, Messlatte: map_05 **24**, map_08 **38**,
map_01 **0**.

### ✅ ERLEDIGT 19.08.2026: welcher Satz gilt bei einer Kollision?

Auf `08.CWM` beanspruchen **zwei** Sätze dieselben drei Einheiten (23, 24, 28),
und beide bestehen die Zeigerprobe:

* Satz 7, Träger 1 (Rumpf 73, Zelle 19,51): der **lückenlose** Lauf 15…29 —
  genau seine 15 Plätze voll.
* Satz 3, Träger 3 (Rumpf 72, Zelle 16,50): 23, 24, 28 — drei verstreute
  Stücke **daraus**.

**Antwort aus dem laufenden Spiel:** auf dieser Karte stehen »4 Frachter und
2 kleine Kampfschiffe«, zwei Frachter laden je 3 Einheiten aus, einer trägt
15 Infanteristen.

Beides trifft, und zusammen entscheidet es die Frage. Die Schiffstafel
(`ships.json`, GAME.EXE 0x52eda0) benennt die Rümpfe — Typ = Rumpf + 80:

| Rumpf | Typ | Schiff | Angriff |
|---|---|---|---|
| 70 | 150 | Patrol-Boot | 5 |
| 72 | 152 | Küstenwache | 7 |
| **73** | **153** | **Frachter** | **0** |

Träger 1 ist ein **Frachter** und trägt die vollen 15 (alles Laser Trooper —
die Frachtarten sind über `infantry.json`, Regel `spodek = (waffe·2−124)&0xFF`,
allesamt **Infanterie**). Träger 3 ist eine **Küstenwache**: ein Kriegsschiff,
das drei Infanteristen will, die schon im Frachter sitzen. Der erste Anspruch
gilt — und es war keine Setzung, sondern der Befund.

⭐ **Die Probe, die nichts kostet und mitläuft:** nach dem Aufräumen tragen
**genau die fünf Frachter** Ladung und **beide Kriegsschiffe nichts** — obwohl
die Regel den Rumpf gar nicht ansieht. Der `--transport-check` schreibt den
Schiffstyp jetzt mit, damit das sichtbar bleibt; fällt es je auseinander,
stimmt die Regel nicht mehr.

⚠ Offen bleibt eine Kleinigkeit: die Daten führen **fünf** Frachter, gezählt
wurden vier. Die zwei weiteren (Träger 36 und 37, mit 8 und 9 Infanteristen)
liegen bei (10,61) und (3,57) — weit von der Gruppe. Vermutlich schlicht nicht
mitgezählt; ein Blick dorthin würde es abschliessen.

---

## K. Die restlichen Kartenabschnitte — vier davon brauchen wir NICHT (19.08.2026)

Der Reihe nach abgearbeitet. Bei jedem stand zuerst die Frage, ob wir die
Sache nicht längst haben — bei den Gleisen hätte mich das Auslassen dieser
Frage fast einen zweiten Leser gekostet.

### sec17 (Brücken/Molen) und sec21 (Rampen) — schon im Gelände

Satzform gelesen. sec17: 100 Plätze zu 24 Byte, **110 Bauwerke auf 21 Karten**;
`+0x00/+0x01` Zelle, `+0x02` Richtung, `+0x03..+0x11` ein **3×5-Kachelfeld**
(die Länge 1…3 nutzt drei bis fünf Spalten, der Rest steht auf Null),
`+0x13` Länge, `+0x16` u16 Trefferpunkte = 500 in **110 von 110** Sätzen.
sec21: **85 Rampen**, `+0/+1` Zelle, `+2` Marke, `+3` Zähler 200.

⭐ **Beide muss man nicht zeichnen.** Das Kartenraster trägt an genau diesen
Zellen `10000 + Kachelnummer` — die Brücke bei (35,49) auf `01.CWM` steht dort
als 10063/10012/10069, die Rampen auf `05.CWM` als 10727. Wir zeichnen sie
also längst. Die Abschnitte werden erst gebraucht, wenn man ein Bauwerk
**zerstören** können will (»Destroy ramp«, 0x539754).

### sec23–sec31 (Gebäudezustände) — leer, bis auf einen Zeiger

GEMESSEN über beide Datenträger, Zustand hinter dem Kopf, verschiedene Werte:

| Abschnitt | | Sätze | verschiedene Zustände |
|---|---|---|---|
| sec23 | Basis | 202 | **1** |
| sec24 | Fabriken | 737 | **1** |
| sec25 | Depot | 5 | **1** |
| sec26 | Generator | 14 | **1** |
| sec30 | Bahnstation | 220 | **1** |
| sec31 | Kraftwerk | 252 | **1** |
| sec29 | Seedock | 36 | 16 |

Sechs von sieben tragen **nur die Vorgabe** — sie zu lesen brächte nichts. Und
die 16 des Seedocks sind kein Zustand: `+0x02` ist fast immer **Gebäudeplatz +
1** (13→14, 21→22, 9→10, 29→30), ein Zeiger auf das Nachbargebäude.

**Damit sind sec25 und sec29 von der Fehlerliste gestrichen** — nicht gebaut,
sondern als gegenstandslos erwiesen.

### sec16 (Infanteriezellen) — vollständig gelesen und trotzdem entbehrlich

4000 Blöcke zu 22 Byte: `+0x00` u16 Anzahl, `+0x02` Spalte, `+0x03` Zeile,
ab `+0x04` **neun** u16-Griffe mit `0xFFFF` als leer.

⚠ Meine erste Lesung war falsch (Griffe ab `+0x00`, und `0xFFFF` als Griff
mitgezählt) und ergab 26 % Treffer. Das ist die nützliche Zahl gewesen: **eine
Struktur, die zu einem Viertel aufgeht, ist keine.** Richtig gelesen:

**579 Griffe, 579 echte Einheiten, alle 579 auf der Zelle ihres Blocks, die
Anzahl trifft in 445 von 445 Fällen — 0 Ausnahmen.**

Und genau deshalb brauchen wir ihn nicht: jede dieser Einheiten steht bereits
in sec5 an derselben Stelle. sec16 ist ein **Sucheintrag**, kein Datum. Nötig
wird er erst, wenn die Reihenfolge im Stapel eine Rolle spielt.

### `game.007` — der Prüfstand des Originals

Liest sich vollständig: **131 Abschnitte, 0 Byte Rest**, »Mission 1«, 42×72,
dieselbe Karte wie `01.CWM`. Der Vergleich zeigt, was der Originalmotor während
einer Mission anfasst: **21 Abschnitte unverändert, 17 verändert, 93 neu**.

Am wertvollsten ist der Feldvergleich in sec5 — **47 Einheitensätze, 829 Byte**.
Er bestätigt unsere Feldtafel und legt die Lücken offen. Die vermeintlich
unbekannten Versätze `+0x35`, `+0x37`, `+0x41` sind nur die **oberen Bytes** der
u16-Felder darüber (STRILI_NA, UTOK_NA, trans). Echt ungelesen bleiben:
**+0x08** (35×), **+0x24/+0x25** (21×), **+0x29**, **+0x2C**, **+0x2E** (20×),
**+0x30**, **+0x39** (17×), **+0x3E**, **+0x47**.

**Teilbefund zu sec11/sec12**, die in jeder Karte leer sind und hier gefüllt:
608 Einträge, sec13 = sec14 = 608 als Zähler. sec11 ist ein Einheitenplatz
(608 von 608 gültig), sec12 ein kleiner Code (0,1,2,3,6,255). Nur **35
verschiedene** Plätze in **341 Blöcken** — also **verschachtelt, nicht je
Einheit gruppiert**: eine zeitliche Liste, kein Wegplan. Was der Code bedeutet,
ist offen.

---

## L. Die Sachbilder — 132 Stück, und das Hilfefenster zeigt sie jetzt (19.08.2026)

`HELPG.PIC` (36 Bilder) und `ENCYCLOG.PIC` (96) sind rohe Punktdaten **ohne
Kopf**: 60×60 zu einem Byte, Palette `DATA/01.PAL`, Punktwert 255 durchsichtig.
Beide gehen ohne Rest auf (129 600 = 36 · 3600, 345 600 = 96 · 3600).

⭐ **Gegenprobe, die nichts kostet:** `MissionTechExporter` schreibt die
Enzyklopädiebilder schon lange auf einem eigenen Weg. **8 von 8 Stichproben
sind byteweise gleich** — zwei unabhängig geschriebene Ausgeber kommen auf
dasselbe Bild, also stimmen Palette, Größe *und* Nummerierung.

⚠ **Einsbasiert.** Das Spiel rechnet `3600·(Bild−1)` (@0x486B7C für die
Enzyklopädie, @0x45A608 für die Hilfe). Meine erste Fassung schrieb
`enc00…enc95` — das hätte zu jedem Text das **falsche** Bild geliefert, ein
Versatz um eins, der nirgends auffällt. Jetzt `enc01…enc96`, `help01…help36`.

### Das Bild zum Text — der Weg ist vollständig gelesen

```
mov ecx, [eax*4 + 0x8b62b0]   ; Tafel[textnummer] = Bildnummer
test ecx, ecx / je ...        ; 0 heisst: kein Bild
lea edx, [ecx + ecx*4 - 5]    ;   5·(n−1)
lea eax, [edx + edx*4]        ;  25·(n−1)
lea eax, [eax + eax*8]        ; 225·(n−1)
shl eax, 4                    ; 3600·(n−1)
fread(0x8b7258, 1, 0xe10)     ; 3600 Byte
```

Die Tafel bei `0x8b62b0` liegt im uninitialisierten Speicher — gefüllt wird sie
aus **`HELPG.DAT`**, 4000 Byte = 1000 dword, Werte 0…36.

**MESSLATTE, und sie ist lückenlos: genau 36 Texte tragen ein Bild, alle 36
Bilder kommen vor, jedes genau einmal, keines fehlt.** Prüfstand
`--hilfebild-check`; er lädt sie auch wirklich (36 von 36).

### Wo es sitzt — auch das gelesen, nicht gesetzt

C `@0x47CFF2`…`@0x47D032`: je Zeile 60 Byte nach `x = 0x1E = 30`,
`y = zeile + 0x1E = 30` — oben links. Und die Zeile direkt hinter der
Kopierschleife ist der Grund, warum es den Text nicht überdeckt:

```
sub word ptr [ebp + 0x8b903e], 0x50
```

Die **Umbruchbreite schrumpft um 0x50 = 80 Punkte**, sobald ein Bild da ist.
Der Text fließt daneben, nicht darunter. Beides ist so eingebaut.

⚠ Beide GAME.EXE tragen dieselbe Anordnung: `HELPG.PIC` hat in C und F je
**einen** Verweis, `ENCYCLOG.PIC` je **drei**, und der Abstand zwischen dem
ersten und zweiten ist in beiden Fassungen `0x7D8`.

---

## ~~M. Die sechs ungebauten Gebäudeklänge~~ — ERLEDIGT am 20.08.2026

Hier stand eine Tabelle mit sechs Nummern (123, 124, 125, 127, 133, 134) und
dem Satz »nicht gebaut, und mit Absicht: der Auslöser ist gelesen, die
*Bedeutung* nicht«. Beides ist jetzt geklärt — und **zwei der sechs waren schon
gebaut**, als die Liste geschrieben wurde.

**Die Voraussetzung, an der es hing:** der ganze Block 120…143 ist **gemessen**
eine einzige Aufnahmereihe **gesprochener Ansagen** — 24 Stücke, 1,04…2,31 s,
eine männliche Stimme, Grundton 155…212 Hz. Die Trennung Sprache/Geräusch ist
an bekannten Gruppen geeicht und überlappt nirgends: Stimmhaftigkeit 0,42…0,77
bei allen fünf Sprachbänken gegen 0,00…0,08 bei allen Effektbänken.

| Nr | Stand |
|---|---|
| **124** | **gebaut** — Stromdeckung fällt unter 100 %, in `PowerMessages` (@0x440460) |
| **125** | **gebaut** — Deckung wieder ≥ 100 %, reine Aufwärtsflanke (@0x440491) |
| **127** | **gebaut** — ein Teilelager trifft seinen Lagerplatz (@0x43E04F), `jne` nicht `jge` |
| **133** | **war schon gebaut** — `Capture.cs`, geht an den NEUEN Besitzer (@0x43D068) |
| **134** | **war schon gebaut** — `Capture.cs`, geht an den ALTEN Besitzer (@0x43CED6) |
| **123** | **kann im Original nie erklingen** — siehe unten |

### ⭐ 123 ist ein echter Fehler des Originals

Der Wächter des Blocks steigt bei 0 ganz aus (C `@0x43DEC2` / F `@0x43CED2`:
`cmp word[bld+0x32], 0` / `je` aus dem Block). Weiter unten wird abgezogen — und
der Klangtest liest den Wert **vor** dem Abzug:

    ax = word[bld+0x32]
    test ax, ax          ; setzt ZF am ALTEN Wert
    lea edx, [eax-1]     ; lea aendert keine Flaggen
    word[bld+0x32] = dx
    jne <weiter>         ; also IMMER genommen
    Klang 123

Gewollt war »abziehen, und wenn er dabei auf 0 fällt, ansagen«. Gebaut ist
»ansagen, wenn er schon 0 war« — und genau das hatte der Wächter ausgeschlossen.
Der `test` steht **einen Befehl zu früh**. In **beiden** Auslieferungen gleich,
geprüft über die Fundstellen des Feldes statt über geratene Adressen.

⚠ **Nicht nachgebaut, und das ist die Antwort, kein offener Punkt.** Eine Ansage
nachzubauen, die das Original nie spielt, wäre keine Originaltreue.

### ⚠ Was diese Liste über sich selbst lehrt

Sie führte **133 und 134 als ungebaut**, während `Simulation/Capture.cs` sie
längst spielte — mit derselben Begründung, die ich am 20.08. noch einmal
hergeleitet habe (der Anker ist der Besitzerwechsel: 134 steht davor, 133
danach). **Auch die Begründung eines offenen Punktes gehört geprüft, nicht nur
der Punkt.**


## N. ⭐ Sektion 20 ist vollständig erklärt (19.08.2026)

Das Lagenbyte über 100 war bisher »Brücken und Rampen« — ohne zu wissen,
welche. Es ist mehr als das: **es ist die Platznummer.**

| Lagenbyte | bedeutet |
|---|---|
| 0 | zeichnen (gewöhnlicher Boden) |
| 1…99 | im verzahnten Durchgang überspringen |
| **100 + n** | **Brücke/Mole Nr. n aus sec17** |
| **200 + n** | **Rampe Nr. n aus sec21** |

**GEMESSEN über beide Datenträger, ohne eine einzige Ausnahme:**

* `sec20[spalte·256 + zeile] == 100 + Platznummer` — **110 von 110** Brücken
  auf 21 Karten.
* dasselbe mit 200 für die Rampen — **85 von 85** auf 12 Karten.

Damit ist die Zuordnung *Zelle → Bauwerk* geschenkt: wer auf einer Zelle steht,
weiß ohne Suche, welche Brücke oder Rampe das ist — und über sec17/sec21 auch
deren Trefferpunkte (500 bzw. 200) und Länge.

⚠ **Mein eigener Fehlschlag auf dem Weg dorthin, und er ist lehrreich.** Der
erste Vergleich ergab **null** Überschneidung zwischen sec21 und den Zellen mit
Lagenbyte ≥ 100 — ich hätte daraus »das sind verschiedene Dinge« geschlossen.
Der Grund war, dass mein Prüfscript `zeile·256 + spalte` rechnete, während der
Index `spalte·256 + zeile` ist. Der Code war die ganze Zeit richtig; **falsch
war der Prüfstand**. Ein Nullbefund aus einem selbstgeschriebenen Vergleich
gehört an einem bekannten Fall geeicht, bevor man ihm glaubt — hier hätte ein
Blick auf die eine Zelle (43,37) gereicht, die der Laufzeit-Prüfstand schon
gemeldet hatte.

---

## O. Der 20.08.2026 — was gebaut wurde und was dabei auffiel

### Gebaut und mit einem Prüfstand belegt

| Sache | Prüfstand | Beleg im Original |
|---|---|---|
| **Recycle** im Depot | `--recycle-check` | `0x4B28E0`, Befehl 506 |
| **Transportieren** über die Bahn | `--transport-netz-check` | Flut `0x4CE710`, Befehl 0x206 |
| **Einheitenmitnahme** zwischen Missionen | `--mitnahme-check` | Fensterart 38 `0x482290`, Liste `word[0x9937B8]` |
| **Reparatur als Knopf** | `--repair-check` | `0x44A122`, 521 gegen 525 |
| **Geschwindigkeitsschleife**, SimHz 50 | `--takt-check` | `0x416068`…`0x4168AE` |
| **Zehn Gruppen, vier Merkpunkte** | `--gruppen-check` | `0x833A00` / `0x799FA8` |
| **Missionsbezahlung gegen sec74** | `--selftest-pay` | zwei unabhängige Quellen |

### ⚠ Die Fehlerklasse des Tages: der Prüfstand, der lügt

Sechsmal in einer Sitzung, und jedes Mal anders:

1. **Er misst den Ort statt der Menge.** `PartsOf` zählte nur in den Fabriken;
   der Nachschub fährt die Teile aber zur Basis. Der Lauf meldete »0 Teile« und
   »Gegenprobe FEHLGESCHLAGEN« — nach der Berichtigung 582 gegen 6.
2. **Er räumt auf und sieht danach noch einmal hin.** `--gruppen-check` fragte
   `GroupOf()` nach dem `_groups.Clear()` ab und meldete DURCHGEFALLEN, obwohl
   alle sechs Messzeilen richtig waren.
3. **Er lässt seinen Zustand liegen.** `--mitnahme-check` schrieb drei
   Prüfeinheiten in `campaign.cfg`, die danach bei jedem Start von Mission 26
   auf der Karte standen.
4. **Sein Urteil umfasst nur einen Teil der Messung.** `--transport-netz-check`
   meldete BESTANDEN, während zwei Zeilen darüber »STIMMT NICHT« stand.
5. **Er geht nur den Hinweg.** `--hangar-check` endete beim Aussenden — und
   deshalb blieb jahrelang unbemerkt, dass ein gelandetes Flugzeug in KEINE
   Liste zurückkommt.
6. **Er endet nie.** `--power-check` lief vollständig durch, druckte seinen
   Bericht und kehrte dann in den Spielbetrieb zurück; nur `--quit-after`
   beendet einen kopflosen Lauf. Gemeldet wurde »hängt«.

**Die Lehre in einem Satz:** ein Prüfstand ohne Gegenprobe misst, dass er
läuft — nicht, dass die Sache stimmt.

### ⚠ Die zweite Fehlerklasse: zwei Zählungen in einem Zahlenraum

* `Bud1` = −1 gegen `BuildingAt` = −1 (Bahnlinien, 11.08.)
* `--bau-check=6` fiel still auf den Depot-Lauf zurück
* **Platznummer gegen Listenstelle** — »kein verbundenes Gebäudepaar« zwei
  Zeilen unter »128 verbundene Knotenpaare«
* **`StExpand` = `FaRepair` = 2** — der Reparaturabbruch hätte einer Basis den
  bezahlten Lagerausbau abgeräumt
* Ein Zähler, der **je Einheit** statt **je Takt** hochläuft: die Drehbremse
  der grossen Schiffe hing damit an Reihenfolge und Anzahl der Schiffe statt
  am Taktzähler (gemeldet als »Schiffe fahren komisch«)

### Berichtigungen an unserem eigenen Baum

* `CommandOp.cs`: **alle vier** Sprungtafeln standen um 0x478 zu niedrig.
* `CommandRecord.cs`: die Behauptung, dem C-Bau fehlten die Wiederholungsdateien
  (»Bytesuche: keine«), ist widerlegt — er enthält alle vier.
* `CwmExtra.RailNodes`: `+0x02` ist der **Gebäudetyp**, kein fünfter Anschluss;
  es sind **vier** Anschlüsse (`+0x03..+0x06`).
* `MapOpen.cs` überschrieb beim Speichern genau diesen Typ mit `0xFF`.
* `CwmExtra.TransportLoads`: `sec5 +0x40` ist ein **Wort**, kein Byte
  (17 Wortzugriffe gegen 7 Bytezugriffe, in beiden Bauten gleich).
* `ImportSelfTest.RunCwm` sah **23 von 41** Karten und 3 von 13 Spielständen.
* `GameHud.cs` schloss aus »keine Knopfaufrufe im Zeichner«, der Bedienblock
  habe keine Bedienung — die Knöpfe liegen im Trefferarm `0x45E541`.
* `SoundBankPlayer.cs` deutet `0x833A16` als Klangkanaltafel; es ist die
  Mitgliederliste der zehn Gruppen.

### Noch offen

* **32 Würfelzüge je Takt** — wir ziehen sie nicht (Lockstep).
* ~~**Fünfzehn Gebäudebefehle** (501…537) laufen in `CommandBridge` nicht~~ —
  ⭐ **GEBAUT am 21.08.2026**, und die Empfehlung des Fensterberichts war
  richtig: es ist **eine Tafel je Gebäudeart**. Siehe den neuen Abschnitt
  »P. Die fünfzehn Gebäudebefehle« weiter unten. Prüfstand `--gebaeude-check`.
* ~~**Fensterart 24 und 25** (Lokator, Gruppieren) sind gelesen, aber nicht
  gebaut~~ — **gebaut am 20.08.2026** (`LocatorWindow.cs`, `GroupWindow.cs`),
  belegt mit `--gruppen-check`.
* **Drei Missionen** haben keine Tore bekommen, weil die zwei GAME.EXE dort
  verschiedene Torfolgen liefern.
* ~~Die Fahrt der verlegten Einheit **mit den Güterzügen** ist nicht
  nachgebaut~~ — ⭐ **GEBAUT am 21.08.2026**, siehe Abschnitt R. Es gab kein
  Tempo zu lesen: sie fährt mit dem Zug.
* Zuarbeit des Spielers: ~~Nachfrist-Video~~ (**erledigt 21.08.2026**),
  **Diagonalkosten** im Gefecht, **Mündungsanker**, die **y-Halbierung** beim
  Einschlag, die zwei **Frachter in Mission 8**.

---

## P. Die fünfzehn Gebäudebefehle — vier Tafeln, vier Kartenabschnitte (21.08.2026)

Alle fünfzehn sind derselbe Dreizeiler: Satzindex aus P1, Eigentümer gegen
`byte[Gebäude+0x05]` aus P2, dann

    byte[Tafel + Satz·Länge + 0x02] = ZUSTAND

Die zwei bezahlten setzen zusätzlich `+0x06 = 0` (den Fortschritt) und ziehen
den Preis von `dword[0xA9C600 + Spieler·4]` ab — dem Kontostand, also sec73.

| Gebäudeart | Abschnitt | Tafel (C) | Satz | Befehle → Zustand |
|---|---|---|---|---|
| Fabrik 2,3,4 | sec24 | `0x87A2C0` | 50×14 | 509→3 510→4 519→2 511→0 |
| Mine 10,15 | sec28 | `0x878AD0` | 50×18 | 515→3 516→4 522→2 517→0 |
| Flughafen | sec27 | `0x879438` | 50×52 | 536→2 520→1 524→0 |
| Basis | sec23 | `0x878E58` | 50×16 | 521→1 525→0 |

⭐ **Die Zuordnung Tafel→Abschnitt musste nicht geraten werden** — sie stand
schon im Baum: `Import/BuildingPatterns.cs` nennt sec24→`0x87A2C0` (50×14) und
sec28→`0x878AD0` (50×18), `GAMESTATE_RE.md` nennt sec27→`0x879438` (50×52) und
sec23→Basis (16 Byte). Neu ist nur, welcher **Befehl** welche Zahl schreibt.

⭐ **Und die Zustandszahlen sind unsere eigenen.** 3/4 sind `FaExpand`/
`FaProdUp`, 2 ist `FaRepair`, 1 ist `StRepair`, 0 ist `StAktiv` — alle vier
standen seit Wochen so im Baum, gelesen aus den vier Fensterzeichnern. Zwei
unabhängige Lesungen, dasselbe Ergebnis.

**Welcher Ausbau welcher ist**, entscheidet der Preis: 509 nimmt
`word[Satz+0x0A]`, 510 nimmt `word[Satz+0x0C]` (@0x44AD8A gegen @0x44AE9F) —
und bei der Mine sind es `+0x0E`/`+0x10` (@0x44BBAB). Beide Paare hiessen bei
uns schon `CostStore`/`CostProd`.

### ⚠ 523 und 526 sind TOT

Beide sind ein zweiter »zurück auf 0« für Fabrik bzw. Mine, beide haben einen
vollständigen Behandler — und im ganzen Programm **keinen einzigen Absender**.
Bytesuche nach `mov word[0xB8A3D8], imm16`: 35 der 37 Nummern aus Bereich B
haben einen, diese zwei nicht. Dieselbe Sorte Fund wie die vier toten
Mauszeiger.

### ⚠ Die Basis kennt keinen Ausbaubefehl

In ihre Tafel schreibt **kein** Befehl eine 2 oder 3, obwohl ihr Fenster
»Status : vergrössern« und »forschen« anzeigt. Das ist gelesen, nicht
übersehen — wie sie in diese Zustände kommt, ist **offen**.

---

## Q. Die Mine ist eine Fabrik zweiter Art (21.08.2026)

Der ganze Baum prüfte `BType is 2 or 3 or 4` und liess die Mine damit die
Basiszahlen tragen. Das Original sagt etwas anderes, und der Minentakt
@0x43E5D4..0x43E6A7 ist jetzt Befehl für Befehl gelesen:

    al  = byte[sec28 + 0x05]                 ; die Ausbaustufe
    ecx = word[0x4FACB8 + al*2]              ; die PERIODE
    if (Taktzaehler % ecx != 0) return       ; nur jeden n-ten Takt
    ... der Foerderwurf (rand%100 gegen +0x03) ...
    word[sec28 + 0x0C]--                     ; EINS aus dem Vorkommen
    word[Gebaeude + 0x32]++                  ; EINS ins Lager
    if (Lager == word[sec28 + 0x08]) Klang 0x80   ; voll

Vier Befunde, und jeder änderte etwas:

1. **Ein Stück je Periode**, nicht fünf je Takt. Hier stand bis heute »die
   Menge (5 je Takt) bleibt unsere Setzung; gelesen ist der Wurf, nicht die
   Schaufel«. Jetzt ist auch die Schaufel gelesen.
2. Die **Periode hängt an der Ausbaustufe** — 85 Takte auf Stufe 0, zwei auf
   Stufe 9 (Tafel `0x4FACB8`, zehn Einträge). Damit tut die
   Produktionserweiterung der Mine überhaupt erst etwas; vorher war sie ein
   **bezahlter Knopf ohne Wirkung**.
3. Der Deckel ist der **eigene Lagerplatz** (sec28 `+0x08`), keine Konstante —
   und der Lagerausbau hebt ihn um **30** (@0x43E7A3 `add word[+0x08], 0x1e`),
   während die Fabrik nur **10** bekommt (@0x43E0F1). Zwei verschiedene Zahlen,
   beide gelesen.
4. Der **Wurf sitzt innerhalb** der Periodenprüfung, nicht davor: ein
   misslungener Wurf verliert genau eine Gelegenheit, nicht einen ganzen
   Gebäudetakt.

⚠ **Das ist ein spürbarer Eingriff in die Wirtschaft.** Auf Stufe 0 fördert
die Mine jetzt 16/85 statt 5 Stück je Gebäudetakt, also rund ein
Sechsundzwanzigstel; über die Stufen holt sie es wieder ein (Stufe 8 liegt bei
16/3). `--mine-check` druckt beide Zahlen.

⚠ **Der Umzug musste am Stück geschehen.** Wer die Mine nur auf Zustand 2 hebt,
ohne `StateName`, `PercentDone` und den Takthandler mitzuziehen, nimmt ihr die
Reparatur ganz: sie stünde auf 2 und niemand führte sie aus. Beim Bau gemessen.

---

## R. ⭐ Die verlegte Einheit fährt — GEBAUT am 21.08.2026

Bei uns war »Transportieren« ein Sprung: `e.Depot.RemoveAt(k); ziel.Depot.Add(nr)`.
Das Original tut etwas anderes, und es ist jetzt ganz gelesen **und gebaut**.

**Befehl 518** (`0x206`), Behandler `CreateConvoy` **C `0x4CEA90`**:

```
  Schleife ueber 200 Saetze zu 48 Byte bei 0xBC0DD0, gesucht wird +0x00 == 0
    keiner frei -> Fehlerzeile ueber 0x539824 und RAUS
  rep movsd ecx=0xA von 0xBC3350 nach +0x04     ; die ROUTE, 40 Byte Knoten
  byte[+0x00] = 1 ; word[+0x02] = Einheit
  byte[+0x2C] = 0 ; byte[+0x2D] = 0             ; Wegindex, faehrt-gerade
  byte[+0x2E] = Zielknoten
```

### ⭐ Die Frage nach dem TEMPO war falsch gestellt

Hier stand: »solange das Tempo nicht gelesen ist, bleibt der Sprung stehen«.
Es gibt **kein Tempo**. Die Einheit hat keine eigene Geschwindigkeit — sie
fährt mit dem Güterzug, und ihr Wegindex rückt um eins, wenn der Zug einen
Knoten erreicht. Dass die Tafel zum Zug gehört, sagt das Spiel selbst: die
Funktion, die sie weiterschaltet, ist dieselbe, die »**Wrong index of slope for
train**« meldet (`0x4C69C0`).

**Zusteigen** — `spoj_launch` @0x4C64C5..0x4C651B, und es ist eine Bedingung,
keine Wahl:

```
  ueber alle Saetze:  +0x00 != 0
    eax = byte[+0x2C]                     ; der laufende Wegindex
    if (route[eax]   != Abfahrtsknoten) weiter
    if (route[eax+1] != Ankunftsknoten) weiter
    -> zusteigen, +0x2D = 1
```

**Aussteigen** — @0x4C6CA5..0x4C6CC9: `+0x2D = 0`, `+0x2C` um eins hoch, und
wenn `route[+0x2C] == +0x2E` ist, ist die Einheit da. **Drei Plätze je Wagen**
(`cmp bl, 3` @0x4C6CD6).

### Was daran unser ist

* Unser Depot hält **Entwurfsnummern**, keine Einheitensätze — der Satz trägt
  darum die Entwurfsnummer. ⚠ Genau das stand hier als Grund, warum nichts
  gebaut sei (»es gibt kein Stück, das unterwegs sein könnte«). Der Einwand war
  zu eng: **der Satz IST dieses Stück**, auch im Original bewegt er nicht die
  Einheit, sondern belegt einen Platz in einer eigenen Tafel.
* ⚠ Steht das **Zielgebäude** bei der Ankunft nicht mehr, ist die Einheit weg.
  Was das Original bei dessen Verlust tut, ist **nicht gelesen**; sie still ins
  nächstbeste Depot zu legen wäre die schlechtere Erfindung. Sie wird gezählt
  (`RailTransfersLost`) und gemeldet.

### Gemessen

`--transport-netz-check` prüft jetzt die **Fahrt** statt des Sprungs — vorher
war seine Messlatte »Ziel += 1 sofort«, er hätte den Umbau also als Fehler
gemeldet:

```
  map_21  Verlegen 67 -> 68: Quelle 0, Ziel noch 0, unterwegs 1
          ... nach 44,1 s Bahnfahrt: Ziel 1, angekommen 1
  map_20  Verlegen 94 -> 100, nach 35,3 s angekommen
  Gegenprobe (Ziel ohne Anschluss): abgelehnt
```

Und `--save-check` trägt die Fahrten mit: ohne das wäre eine verlegte Einheit
beim Speichern **weg** — aus dem Quelldepot heraus, im Zieldepot noch nicht.
Dieselbe Fehlerklasse wie bei den Merkpunkten, nur andersherum.

---

## S. Die Lagentafel im Spiel, und wo die Rampe wirklich zaehlt (21.08.2026)

Auf dem Weg zum Be- und Entladen von Hand sind drei Adressen gefallen, die
das Thema erst baubar machen.

### ⭐ `0x542E18` ist sec20 im Arbeitsspeicher

```
  0x4CF100  (spalte*, zeile*, wert)
    cx  = word[esi]                                  ; die Spalte
    edx = cx << 8                                    ; spalte * 256
    ax  = word[edi]                                  ; die Zeile
    cmp byte[edx + ebx + 0x542E18], 0xC8             ; die LAGENTAFEL
    jb  raus
```

Das ist **derselbe Index, den Abschnitt N gemessen hat** — `spalte·256 + zeile`,
nicht andersherum. Dort war es aus den Dateien erschlossen; hier steht es im
Code. Zwei unabhängige Seiten.

### ⚠⚠ Eine Berichtigung MEINER Berichtigung, am selben Tag

Hier stand seit einer Stunde: »die Zellmarke fürs Absetzen ist ≥ 200; die alte
Notiz *≥100 zum Beladen, ≥200 zum Entladen* ist in ihrer ersten Hälfte falsch«.
**Das war mein Fehler, und er ist zurückgezogen.**

Ich hatte EINE Stelle gelesen (`0x4CF100`, `cmp …, 0xC8; jb raus`) und daraus
geschlossen, es gebe keine 100er-Schranke. Es gibt sie — im **Einheiten­durch­gang
`0x406CD0`** (»move kolik:«, »on square«, »no fuel«), und zwar viermal gegen
dieselbe Tafel:

| Stelle | Vergleich | heisst |
|---|---|---|
| `0x409510` | `cmp dl, 0x64; jb raus` | **≥ 100** |
| `0x409767` | `cmp cl, 0x64; jb raus` | **≥ 100** |
| `0x409387` | `cmp cl, 0xC8; jae …` | **≥ 200** |
| `0x4097BC` | `cmp cl, 0xC8; jae …` | **≥ 200** |

Damit stimmt die alte Notiz: die **Einheit** steigt beim Betreten einer Zelle
mit ≥ 100 ein und bei ≥ 200 aus. `0x4CF100` fragt nur nach ≥ 200, weil es der
**Entladebefehl** ist — dort ist die 200 richtig, aber sie ist nicht die ganze
Regel.

⭐ **Die Lehre, und sie ist teuer bezahlt:** ich habe aus dem Fehlen einer
Schranke *an einer Stelle* auf ihr Fehlen *im Programm* geschlossen. Das ist
derselbe Fehlschluss wie »null Aufrufer, also toter Code« ohne
Thunk-Auflösung — ein Negativbefund aus einer einzigen Fundstelle ist keiner.
Aufgefallen ist es nur, weil unser eigener Code die zwei Adressen
`0x40950C`/`0x409763` schon in einem Kommentar führte.

### Und was ≥ 100 und ≥ 200 nach Abschnitt N sind

`100 + n` ist eine **Brücke/Mole** (sec17), `200 + n` eine **Rampe** (sec21) —
das bleibt gemessen (110 von 110 und 85 von 85). Beides zugleich zu halten ist
kein Widerspruch: eine **Mole** ist genau der Ort, an dem man ein Schiff
besteigt, eine **Rampe** der, über den man herunterfährt.

### Der Rampenschritt

Steht die Zelle auf ≥ 200, rechnet die Funktion eine Richtung aus
(`0x4018C0`, dann `(winkel − 0x29E3) & 7`, halbiert, durch die Auswahltafel
**`0x539790`** = `3, 0, 2, 1`) und holt sich daraus ein Zellenpaar aus
**`0x539798`** (je 4 Byte: `word` Spaltenversatz, `word` Zeilenversatz):

| Auswahl | (dSpalte, dZeile) |
|---|---|
| 0 | (−1, −2) |
| 1 | (−1, 1) |
| 2 | (1, 0) |
| 3 | (−2, 0) |

⚠ **Die vier Paare stehen hier als ROHWERTE.** Sie sind ausgelesen, aber ihre
Bedeutung ist *nicht* nachgemessen — dass eine Zeilenversetzung von −2 zu einer
Rampe passt, ist plausibel (Rampen überbrücken Höhen), belegt ist es nicht. Wer
sie einbaut, misst sie vorher an einer Karte nach.

### ⭐ `0xBDEA80` ist das Belegungsgitter

`word[(spalte·256 + zeile)·2]`, und **`0xFFFC` heisst frei** (@0x4CF183).
Danach fragt die Funktion, ob auf der Nachbarzelle abgesetzt werden kann.

### ⭐ Das ENTLADEN ist gebaut (21.08.2026)

**Befehl 18**, und die ganze Kette benennt sich selbst:

| Stelle | was sie ist |
|---|---|
| `0x438870` | der Absender — »**Very unique error before unloading units**« |
| `0x4CF100` | die Rampenprüfung, die die Zielzelle **zurückschreibt** |
| `0x4C30C8` | der Behandler: Auftrag `0x10`, Zelle nach `+0x36`, Stückzahl nach `+0x38` |
| `0x4CF240` | der Entlader — »Not unloaded unit found«, »Wrong square to unload infantry/robot« |

Der Satz trägt **beide** Zellen: P2/P3 die gerechnete, P4 die angeklickte Rampe.
Wir rechnen im Behandler neu und verwerfen, wenn es nicht übereinstimmt — über
das Netz käme sonst eine beliebige Zelle herein.

⭐ **Und die Stückzahl im Satz sagt, dass NACHEINANDER abgesetzt wird.** Der
erste Anlauf setzte alle fünfzehn auf einmal ab; gemessen auf `map_05` fanden
**5 von 15** Platz. Einen Zähler braucht nur, wer eines je Takt absetzt — und
das löst das Platzproblem von selbst. Jetzt: **15 von 15 in 16 Takten.**

**Was daran unser ist:** die Ladung kommt auf die gerechnete Zelle, und ist die
besetzt, auf die nächste freie im Umkreis **6**. Die Zahl ist gemessen, nicht
geraten (mit 3 blieben zehn Stück an Bord — eine Rampe liegt am Ufer). Das
Original hat eigene Zweige für Infanterie und Fahrzeug; die sind als vorhanden
gelesen, nicht in ihrer Regel.

⚠ **Die Ladung wurde bis heute beim Laden weggeworfen** (`continue`). Sie läuft
jetzt durch dieselbe Bauzeile wie jede andere Einheit und wird danach aus der
Liste genommen — und sie steht im **Spielstand**, sonst wäre sie beim Speichern
weg. Das wäre heute der dritte Fall derselben Art gewesen (Merkpunkte,
Bahnfahrten, Ladung).

⚠ **Eine Falle beim Prüfen, in neuem Gewand:** mit `--skirmish` dünnt
`SkirmishAi` die Karte aus und setzte auf `map_05` genau die vier beladenen
Frachter auf tot. Der Prüflauf hielt daraufhin ein Gebäude mit demselben Platz
für einen Träger. Über `--campaign=N` steht die Karte, wie sie ist.

### ⭐ Das BELADEN ist gebaut (21.08.2026)

Die Einheit steht auf einer Ladezelle (**Lagenbyte ≥ 100**, gelesen im
Einheitendurchgang `0x406CD0` @0x409510/@0x409767) und geht an Bord. Die
Laderoutine `0x4CEE80` ist selbst nachgelesen, nicht aus der Notiz übernommen:

```
  al = byte[TRAEGER*78 + 0x6E2708]      ; +0x40 = sein Transportsatz
  cl = byte[STUECK*78  + 0x6E26D2]      ; +0x0A = die GATTUNG des Stuecks
     == 0 -> Fahrzeug     cmp al,0x0A ; jbe   dann  add al, 5
     == 1 -> Infanterist  cmp al,0x0E ; jbe   dann  inc al
     sonst -> "Wrong type of unit tries to go in transport ship"
```

⚠ **Die zwei Schranken sind verschieden, und das ist gelesen, nicht
vereinheitlicht:** ein Fahrzeug steigt auf, solange das Gewicht **≤ 10** ist —
also **drei** —, ein Infanterist, solange es **≤ 14** ist — also **fünfzehn**.
Gemischt ist erlaubt. Und dass Schiffe und Flugzeuge draussen bleiben, braucht
keine eigene Liste: ihre Gattung ist weder 0 noch 1.

**Gemessen** (`--beladen-check`, map_05): 3 von 5 Fahrzeugen, 15 von 17
Infanteristen, ein Schiff abgewiesen mit dem Wortlaut des Originals, und eine
Einheit abseits der Ladezelle steigt gar nicht erst ein.

**Was UNSER ist:** welchen Träger die Einheit nimmt (den nächsten eigenen mit
Platz, Umkreis 6). Das Original entscheidet das im Bewegungsschritt, und dieser
Teil ist nicht gelesen.

⚠ **Die heikle Stelle ist das Herausnehmen aus der Einheitenliste.** Im
laufenden Spiel wird bei uns sonst NIE eine Einheit entfernt — wer stirbt,
bekommt `Dead = true` und bleibt stehen. Auf Listenstellen zeigen vier Dinge,
und alle vier werden nachgezogen: Belegungsgitter, Auswahl, die zehn Gruppen
und jedes `Target`. ⚠ Nicht über `InitEntityMovement` neu stempeln — das setzt
Wege und Belegungen zurück und liesse jede laufende Fahrt vergessen.

### Der Auslöser

Im Original geschieht das Einsteigen im **Bewegungsschritt** selbst
(`0x406CD0` prüft die Lagentafel beim Betreten einer Zelle). Wir fragen es
einmal je Takt statt im heissen Pfad — dasselbe Ergebnis (»steht auf der
Ladezelle → steigt ein«), und der Bewegungscode bleibt unangetastet.
⚠ **Eine je Takt**, denn das Einsteigen verschiebt die Einheitenliste; und
⚠ **nur wer nichts mehr vorhat**, sonst verschluckt eine Mole jeden, der
daran vorbeifährt. Die Laderoutine `0x4CEE80` ist längst gelesen
(Gewichte 5 je Fahrzeug / 1 je Infanterist, Deckel 15, Flugzeuge und Schiffe
abgelehnt), die Rampendaten liegen auf **33 Karten** in der Meta (`ramps`, je
Zelle mit Lagenbyte) — der alte Blocker »die Laufzeit kennt sec20 gar nicht«
ist damit **weg**. Es fehlt die Bedienung: ein Auftrag »absetzen« und ein
Auftrag »einsteigen«.
---

## T. Die fünf fehlenden Takt-Stationen — gelesen am 21.08.2026

`Check gas`, `Self-defenders`, `Mines and traps`, `craters` und `Check AA`
fehlten in unserem Nachbau **ganz**. Alle fünf sind jetzt gelesen, in beiden
Bauständen befehlsweise verglichen (15 der 17 beteiligten Funktionen identisch,
2 nur mit anderer Registerwahl), und alle Tafeln liegen im `.bss` exakt auf
C = F + 0xFA0.

### ⚠ Zwei Berichtigungen an unseren eigenen Zahlen

**Es sind 29 Würfelzüge je Takt, nicht 32.** Selbst nachgezählt in beiden
Bauten: genau 29 Aufrufe des Würfels zwischen C `0x415CF0` und `0x4168A7` und
genau 29 `rnd`-Marken. Und die Marken sind **Kontrollpunkte, keine
Verbraucher**: jede zieht eine Zahl und protokolliert sie, damit ein
Auseinanderlaufen auf eine Gruppe eingegrenzt werden kann.

**`Buildings` ist Station 64, nicht 63.** Ab `Self-defenders` war unsere
Zählung um eins zu niedrig; die Adresse `0x416683` stimmte, die Nummer nicht.
Nachgezählt: 85 Protokollpunkte, Punkt 63 = `Self-defenders`, 64 = `Buildings`.

### ⭐ Es gibt ZWEI Zufallsquellen, und nur eine zählt

| | Adresse | Art |
|---|---|---|
| **der deterministische** | C `0x4C5B30`, F `0x4C56E0` | `seed = seed + 25 + (int)(sin(seed·K1)·K2)` über `word[0x539240]`, wenn `dword[0x539234] != 0`; sonst Rückfall auf `rand()` |
| `rand()` | `0x4D6C70` | MSVC-LCG (214013 / 0x269EC3 auf `0x53A338`) |

**Nur der erste ist determinismusrelevant.** Der zweite wird ausschliesslich
beim *Aufstellen* (Minen, Fallen, Radare) und beim *Anlegen* von Kratern
benutzt — und genau deshalb sind die Krater für einen Lockstep harmlos.

### Die fünf Stationen

| Station | Nr. | Handler (C) | Tafel (C) | Satz | Plätze | Abschnitt |
|---|---|---|---|---|---|---|
| `Check AA` | 26 | `0x428600` | `0x6E1498` | 8 | 200 | **nicht gespeichert** |
| `Gas 2` | 46 | `0x439C50` | `0x833870` | 8 | 50 | sec83 |
| `Check gas` | 47 | `0x4396C0` | `0x77CAE8` | 8 | 4000 | sec82 |
| `Mines and traps` | 49 | `0x4216F0` | `0x552E18` / `0x688B58` / `0x677F30` | 6 | 500 / 500 / 200 | sec84 / 85 / 86 |
| `Self-defenders` | 63 | `0x411820` | `0x53D8D8` | 6 | 200 | **nicht gespeichert** |
| `craters` | 73 | `0x4A9A70` | `0x9C9948` | 6 | 1000 | sec45 |

### Was daran für den Nachbau zählt

**`Self-defenders`** bestätigt die alte Vermutung mit einer Zahl: die
Füllschleife des Laders (`0x41EE75`) schreibt `0xFFFF` bis `cmp eax,0x53D8D8` —
sec126 endet auf dem **ersten Byte der Tafel**. Eingetragen wird aus der
**Trefferroutine** `0x40C9A0`, mit `+0x04 = 0x14` = **20 Takte Wartezeit**;
läuft sie ab und hat das Opfer immer noch keinen Befehl (`+0x14 == 0`,
`+0x34 == 0xFFFF`), setzt `0x40FC90` den Angriffsbefehl. Ein Platz wird nie
freigegeben — `+0x04 == 0` *ist* frei.

**`craters`** beeinflussen die Simulation **nicht**: ausser Anlegen, Altern,
Speichern und zwei Zeichnern gibt es keinen Leser. Alter beginnt bei
`rand()%80 + 5`, steigt bei `Takt%10 == 4`, Schluss beim Überlauf von 255 —
Lebensdauer **1720…2510 Takte**, genau **zwei** Bildstufen (`(Alter − 2)/130`).

**`Mines and traps`** sind dreimal dieselben 60 Befehle. Eine Mine ruft die
Trefferroutine; eine **Falle setzt `Einheit +0x20 = 0`** — sie hält an, sie
verletzt nicht. Radare zählen bei `Takt%25 == 13` herunter: 255 · 25 =
**6375 Takte**, und `unexplored` deckt für jedes lebende Radius 10 auf.
⭐ **Ein toter Zufall:** `+0x02`/`+0x03` werden beim Aufstellen mit
`rand()%20+10` bzw. `rand()%10+5` gefüllt und in **beiden** EXE von niemandem
gelesen.

**Das Gas** ist zweistufig: 50 Quellen stossen je **7 × 10 Wolken** aus; die
Wolken driften mit Wind (Tafel `0x4FA560`, acht Einheitsvektoren ×100, in C und
F byteidentisch), Geländegefälle (`0x4FA580`), Zerfall `v = v·19/20` und
Dichteabstossung über eine 256×256-Karte, die jeder Takt neu aufbaut. Ein
Treffer setzt `Einheit +0x2e = (rnd&3)+1`. ⚠ **Der grösste Zufallsverbrauch des
ganzen Takts** — bei voller Tafel bis rund 28 000 Züge je Takt.

**`Check AA`** wird aus dem Schiessen (`0x40DDB0` ← `Movement`) eingetragen,
wenn `Einheit +0x0c == 0x26` (Flak). Vier Rohre, `+0x07 = 4` Schüsse, je Schuss
ein 50-%-Tor und Streuung `30 − rnd%60`. Schaden
`= ((rnd&3 + Schütze[+0x26]) − (rnd&3 + Flugzeug[+0x23])) / 2`.

⚠ **Was auch dieser Befund NICHT belegt:** `Einheit +0x26` wird hier als
Angriffswert benutzt, steht in `GAMESTATE_RE.md` aber als »category/role«.
**Eine** Fundstelle ist nach unserer eigenen Regel kein Befund.

---

## U. Der »Wiederholungs-Prüfstand« — die Erwartung war falsch (21.08.2026)

Abschnitt D führte als grössten ungelesenen Posten: »Das Original hat einen
eigenen Determinismus-Prüfstand … Er schreibt je Takt **56 benannte Felder
jeder Einheit** heraus und vergleicht sie beim Abspielen.« Daraus sollte die
vollständige Feldliste kommen **und** ein Weg, unsere Simulation gegen den
Originalmotor zu halten.

**Beides trifft nicht zu, und das ist der Befund.**

### Die 56 war ein Zufallstreffer

Die Feldnamen stammen nicht aus einem Aufzeichner, sondern aus einer
**Bildschirm-Einblendung**: `draw_text(x, y, "NAME:", Wert, Schrift)` zeigt die
Felder **einer** Einheit — Index in `word[0x4FA0C8]`, Grenze `cmp ax,0x1F40`
(8000). Sie schreibt nichts und vergleicht nichts.

Die Zahl 56 kommt vermutlich aus einem **anderen** Zeichenkettennest: die
Ablaufspur-Senke hat ihr dichtestes Vorkommen mit n = 56 in beiden Bauten bei
C `0x416191..0x416BCD` — das sind aber Hauptschleifen-Marken (`Start
Main_funct`, `CPU`, `Power`, `Search`), nicht Feldnamen.

Der echte Feldausgeber liegt bei **C `0x416E4E..0x417CF2`** (F `0x416C8A`,
Abstand durchgehend `0x1C4`): **64 Pushes** = 4 Kopfmarken + **60 Feld-Pushes**
= **59 verschiedene Namen**, davon **37 Versätze in den Einheitensatz**.

### Was das für `ENTITY_FELDER.md` heisst

⭐ **Keine einzige Widersprüchlichkeit bei den Versätzen** — alle 37 stimmen,
darunter `+0x02`/`+0x03` als Fahrwerk/Rohr, `+0x3D = RELOAD`, `+0x0F =
l_engine` und die exp-Formel `(byte[+0x28]<<8) | byte[+0x4C]`.

Falsch sind die **Zahlen und drei Zuschreibungen**:

* »56 Namen« → **59** verschiedene, 60 Pushes · »46 Versätze« → **37**
* `ANIM` ist **bindbar**: `byte[+0x11] & 7`, dasselbe Byte wie `ANIM_SPODEK`.
  Die Doku führt es unter »nicht bindbar«.
* `pin` existiert in diesem Zeichenkettenvorrat **gar nicht**.
* **Neu: Vorzeichen.** `SMX`/`SMY` sind vorzeichenbehaftete `i8`,
  `KOLIK`/`NABYTO`/`speed` sind `i16` (`movsx`).
* Die sechs namenlosen Zeilen der Doku (`+0x08 Hp`, `+0x0A Gattung`,
  `+0x26 Attack`, `+0x27 Defence`, `+0x29 HpMax`, `+0x2E Sprit`) fasst der
  Ausgeber **nicht an** — sie bleiben durch diese Quelle unbelegt.

### ⚠ Zu `trans` (+0x40): kein Widerspruch, sondern zwei Tafeln

Der Bericht las: `if (byte[+0x0C] == 0x2E) idx = word[+0x40]`, Index in einen
**18-Byte-Nebensatz** bei C `0x77AC50` (`zdroj0-3`, `cil`, `weap`, `chas`,
`spec`, `anga`, `sklad`, `max_sklad`, `robot`, `activ`, `jedu`, `dalsi`) — und
schloss daraus, `+0x40` sei »kein Transport«.

**Das gilt so nicht.** Der Zugriff hängt an `+0x0C == 0x2E`; für diese eine
Einheitenart zeigt `+0x40` dorthin. Unsere Transportdeutung steht unabhängig
davon: die Laderoutine `0x4CEE80` liest `byte[Träger·78 + 0x6E2708]` = `+0x40`
als Satznummer in die Transporttafel, und das ist über **beide Datenträger**
gemessen (30 gültige Sätze, 65 geladene Einheiten, 0 Zeiger auf einen fremden
Satz). **`+0x40` ist ein Verweis, dessen Tafel von der Einheitenart abhängt** —
beides ist wahr.

### ⭐ Aufzeichnen geht, Abspielen gibt es nicht

**Einschalten** ist gelesen: im Spiel **`ENABLEDEVEL`** tippen (Schummeltafel
C `0x4FA100`, Schrittweite 21, Index 12 → `0x43AF28`, meldet »Developers'
cheats enabled«). Danach schaltet **`K`** die Feldeinblendung um
(`byte[0x4F6FB4]`), und **`Z`** sendet Opcode **976**.

Das Aufzeichnen selbst hängt an **Opcode 975** → C `0x4C3F52` → `0x4C2090`
(`Modus = 1`, `replay.beg` speichern, `.mes`/`.txt` leeren). ⚠ **975 wird
nirgends erzeugt** — kein Schreiber in beiden Bauten. Erreichbar nur über einen
Zweibyte-Eingriff `976 → 975` (Dateiversatz `0x12DFC` in C, `0x12BB1` in F,
je `d0 03` → `cf 03`).

⚠⚠ **ABSPIELEN IST IN BEIDEN AUSGELIEFERTEN BAUTEN UNFERTIG.**
`mov byte[Modus],2` (C `0x4C2100`) hat **null Aufrufer** und wird auch als
dword nirgends referenziert. Der von ihr geöffnete `FILE*` hat 12 Fundstellen,
und **keine einzige liest** — ein `fread` von `replay.mes` existiert nicht.
Der Modus-2-Zweig führt statt dessen in den **Netzwerk**-Empfänger.

**Damit ist die Hoffnung aus Abschnitt D erledigt:** es gibt keinen Weg, unsere
Simulation gegen den Ausgang des Originalmotors laufen zu lassen. Der
Prüfstand, den wir uns davon versprochen haben, ist im Original selbst nie
fertig geworden. `game.007` bleibt die einzige zweite Quelle.

### Der Befehlssatz, nebenbei bestätigt

Die 236 Byte: `+0x00` Opcode (i16), `+0x02` Merker, `+0x04` Fälligkeit (i32),
**`+0x08..+0x18` neun Parameterwörter**, `+0x22` Takt, `+0x24` ein Feld mit
16 Fundstellen, **alle Zeichenkettenkopien**. Unser `CommandRecord` führt genau
diese Aufteilung samt der Lücke von P11 bis P15 — ⭐ neu ist der Hinweis, dass
`+0x24` (»P15«) ein **Textfeld** sein dürfte. Das passt zu den Nummern, die es
benutzen: 505, 533 und 30 — Gruppen- und Lokatorfenster nehmen einen **Namen**
entgegen. Nachgelesen ist die Deutung noch nicht.

⚠ `replay.mes` bekommt nur Opcodes **unter 800** (`cmp word[esi],0x320` bei
C `0x4C25E0` — die in Abschnitt D genannte Adresse `0x4C20A4` war eine
F-Adresse).
---

## V. `SPR.DAT` und `MARK.CWK` — beide gelesen (21.08.2026)

Zwei Dateien, die im Baum **null Codestellen** hatten. Eine ist fast ganz tot,
die andere quicklebendig — und beide berichtigen `CAB_ASSETS_RE.md`.

### `SPR.DAT` — die Ersatzkachel-Bank, und 34 von 35 Bildern sind tot

`F:\Akte Europa\SPR.DAT`, **31 048 B**; im Kabinett `D:\DATA1.CAB` als Eintrag
32 von 38 mit derselben Sollgrösse. ⚠ Das CAB ist ein **InstallShield-5**-Archiv
(`ISc(`), kein MS-CAB — `expand.exe` kann es nicht.

**Behälterformat**, belegt an Grössen- (C `0x4ABEA0`) und Ladefunktion
(C `0x4ABC00`):

| Versatz | Breite | Bedeutung |
|---|---|---|
| `+0` | 3 B | Magie `"MSF"` (`strcmp` gegen `0x504068`) |
| `+3` | u16 | **Anzahl Slots = 57** |
| `+5` | n×u32 | Versatztabelle, blobrelativ; `0xFFFFFFFF` = leer |
| `+233` | — | Blobanfang |

Satzgrösse ist `tab[i+1] − tab[i]`. Der Inhalt ist ein CWR-Bild: `+0` Zeilen,
`+1` yoff, dann je Zeile `[count][leftoff][mode]` und `count` Palettenbytes,
Zeilenschritt `count+3`, Schluss `58 58 58`.

**Gemessen über alle Einträge:** 57 Slots, **35 belegt**, 22 leer. 33 der 35
sind messbar — **33 von 33** decodieren sauber, **33 von 33** enden auf
`58 58 58`, **33 von 33** sind **40 px breit**.

⭐ **Gelesen wird davon genau EIN Eintrag: Nummer 19.** Ein Abtast aller Dwords
in `.text`, die ins Zeigerfeld `0xB0E188` fallen, findet dort nur den
schreibenden Zugriff des Laders — und als einzige Leser zwei feste Zugriffe auf
`0xB0E1D4` = `0xB0E188 + 19·4`. Beide sind der Zweig *»die Kachelnummer ist
0xFFFF«* im Geländezeichner (C `0x4B42AB` und `0x4B44C0`). Die Umgebung
bestätigt die Masse: Kachelschritt `add eax,0x28` = **40** = die gemessene
Bildbreite, `sub ebp,0x32` = **50** = das gemessene yoff.

**Also: `SPR.DAT` ist die Ersatzkachel des Geländezeichners.** Von 35
vorhandenen Bildern sind **34 tot** — 26 werden geladen und nie gelesen, 8
werden nicht einmal geladen, denn der Lader läuft nur bis Index 37
(`cmp esi,0x26`, in beiden EXE).

⭐ **Und ein echter Fehler des Originals:** für `i = 26` (und 45) ist der
Folgeversatz `0xFFFFFFFF`, die Grösse wird damit zu `0xFFFFA4C9` statt 865. Die
Grössenfunktion prüft nur `tab[i]` auf −1, nie `tab[i+1]` — in **beiden** EXE
gleich. Das ist der zweite belegte Originalfehler überhaupt (nach M5s `jge`).

Nebenbei: die Fehlermeldungen des Behälters sind **tschechisch** —
`"soubor nenalezen"`, `"Neni to MSF"`, `"Chybny index sprajtu"`. ⚠ Das ist
**kein** Hinweis auf einen tschechischen Entwickler; dieser Fehlschluss ist im
Projekt schon einmal gezogen und zurückgenommen worden.

### `MARK.CWK` — die Punktschablone für Bodenspuren, und sie lebt

640 B, Lader C `0x4A9740`: `fread(0x9C96C8, 1, 0x280, f)` — ganze Datei, kein
Kopf, **genau ein Aufrufer** in beiden EXE.

⚠ **Die naheliegende Deutung »320 Bytepaare« trifft die Byteanzahl, verfehlt
aber die Gliederung.** Belegt am Leser (C `0x42D320…0x42D3B3`, in F Befehl für
Befehl gleich):

```
640 B = 5 Bodenarten × 8 Richtungen × 8 Punkte × (u8 dx, u8 dy)
      = 40 Gruppen zu 16 Byte
```

* `k` = 0…7 (`inc di; cmp di,8; jl`)
* `G = Bodenbasis + Richtung`, `G·8` (`shl eax,3`)
* **Bodenbasis** = `byte[0x4FA4D8 + 2·t]`, `t` = Bodenbyte der Karte. Für
  `t = 0…4` sind die Werte **0, 16, 32, 8, 24** — genau die fünf Achtergrenzen.
* **Richtung** = `Markensatz[+2] & 0x7F`

Gemessen: dx 9…41, dy 10…38 — beides innerhalb einer 40-px-Kachel, was zur
SPR-Kachelbreite passt.

**Wozu:** Objektart 19 des Anzeigelisten-Zeichners setzt je Marke **acht
Bildpunkte**. Der *vorhandene* Bildpunkt wird gelesen, über die 256-Byte-Tafel
`0xB135B0` abgedunkelt und zurückgeschrieben — **die Datei enthält keine Farben
und kein Bild**, sie verdunkelt den Boden darunter. Dazu ein **Zittern** von ±2
aus der EXE-Tafel `0x5029B0` (in beiden EXE byteidentisch).

Der Markenspeicher: `0x9CB0B8`, **13 Byte je Satz, 500 × 40 = 20 000 Sätze**.
Anlegen: Platz = `rand()%40`, `+4` = **180** Takte Lebensdauer; das Abklingen
senkt `+4` je Durchlauf um eins.

**Also: vergängliche Bodenspuren** — Fahr- und Kettenspuren, Brandflecke, deren
Punktmuster von **Bodenart und Fahrtrichtung** abhängt.

### ⚠ Zwei Berichtigungen an `CAB_ASSETS_RE.md`

1. **`SPR.DAT`:** dort steht »`MSF9` tagged container«, Tabelle bei `+9`, 38
   Einträge. Richtig: die Magie ist **3 Byte** `"MSF"` — das `'9'` ist
   `0x39` = **57**, das niedrige Byte des Zählers. Die Tabelle steht bei
   **`+5`** (`tab[0] = 0` wurde übersehen) und hat **57** Einträge; 38 ist die
   Schleifengrenze des *Laders*. Die dort vermutete »per-sub metadata region
   0x161..0x361« gibt es nicht — das ist der Rumpf von Teilbild 0. Damit ist
   die dortige offene Frage 1 (»per-sub raster dimensions unresolved«)
   **erledigt**: alle Bilder sind 40 px breit.
2. **`MARK.CWK`:** dort steht »colour from an in-exe table `0x5029B0`«. Die
   **Farbaussage ist falsch** — `0x5029B0` ist die ±2-Zittertafel; die Farbe
   kommt aus `0xB135B0`, angewandt auf den bereits vorhandenen Bildpunkt.

### Was auch hier offen bleibt

* **Welches SPR-Bild welche Kachel ist** — nicht auflösbar, solange 34 von 35
  nie gelesen werden; das ginge nur durchs Ansehen.
* Was die Bodenarten `t = 0…4` **heissen** (die Zuordnung zu den Blöcken ist
  gelesen, die Namen nicht). Ab `t = 5` liefert `0x4FA4D8` unter anderem
  **100** — `G = 100 + Richtung` läge weit hinter dem Dateiende; die Tafel wird
  offenbar noch für etwas anderes mitbenutzt.
* Vom 13-Byte-Markensatz sind nur `+0…+5` verfolgt.

---

## W. ⭐ Die KI-Dateien `AI*.CWI` sind gelesen (21.08.2026)

Der grösste ungelesene Block des Projekts. 43 Dateien à 2968 B — und sie sind
**keine** Bau- oder Wellensteuerung, sondern die **strategische Wegekarte der
KI**: ein 11×11-Sektorennetz über der Karte, dessen Kanten von **Brücken**
abhängen.

### Der Teiler geht auf: 2000 + 484 + 484 = 2968

Am Lader abgelesen (C `0x41CB00`, `push 0x7d0 / push 0x1e4 / push 0x1e4`),
Ziele C `0x542330 / 0x541F60 / 0x542148` — **C = F + 0xFA0 in allen dreien**.

Der Dateiname entsteht als `"ai" + (Mission < 10 ? "0") + Missionsnummer +
".cwi"`; das Laufwerk hängt an der Mission (< 16 → CD1, 16…39 → CD2).

| Block | Datei | Form | Inhalt |
|---|---|---|---|
| **A** | `0x0000–0x07CF` | 400 × 5 | Brückenregeln |
| **B** | `0x07D0–0x09B3` | 2 × 121 × 2 | Ankerpunkt je Sektor |
| **C** | `0x09B4–0x0B97` | 2 × 121 × 2 | Kanten offen/zu |

**Block A**, je Satz: `+0x00` X der Brückenzelle (0 = frei), `+0x01` Y,
`+0x02` Sektor-X = X/24, `+0x03` Sektor-Y = Y/24, `+0x04` Richtung
(0 = Kante zu Sektor+11, 1 = zu Sektor+1). Die Regel feuert nur, wenn
`byte[0x542E18 + (X<<8) + Y]` in **100…199** liegt — also **wenn dort eine
Brücke steht**.

**Block B**: `+0` Ankerpunkt X, `+1` Y, `0xFF` = Sektor unbenutzt. Knotenindex
= SektorX·11 + SektorY; Sektorkante **24** Felder (`div cl` mit `cl = 0x18`).

**Block C**: je Sektor zwei Kantenbytes. **Ebene 1 ist die unversehrte
Fassung** — `restore` kopiert sie nach Ebene 0 zurück, dann läuft `apply`.

### Was die Datei bewirkt

Der einzige Laufzeitleser von Block C ist ein **Dijkstra über die 121
Sektoren** (C `0x4BEAE0`, Kostenfeld `0xB45FB0`). Der einzige Leser von Block B
schickt die Angriffsgruppe der KI mit **Busbefehl 3 (BEWEGEN)** auf den
Ankerpunkt ± `(rand&7 − 4)`. Die Protokollstationen daneben heissen
`get target in sector`, `Attack group not available`, `Take all`,
`Enough units`.

⭐ **Und die Bedingung ist die Brücke.** `restore+apply` wird ausserhalb des
Laders von genau zwei Stellen gerufen: die eine steht unmittelbar vor der
Protokollzeile **»end erase bridge«**, die andere am Ende der Gegenroutine, die
das Liniengitter stempelt. **Wird eine Brücke zerschossen, fällt die
Sektorkante weg und die KI marschiert anders.**

### Die Zahlen

* 17 200 Plätze angesehen, **475 belegt**. `+0x02`/`+0x03` decken alle 11
  Werte ab, `+0x04` ∈ {0,1} (254/221).
* **475/475** zeigen auf einen benutzten Gitterknoten. **475/475** zielen auf
  eine Kante, die in Block C **0** ist — die Sätze **öffnen ausschliesslich**.
* 2162 von 5203 Knotenplätzen belegt, **nie halb** (0 Mischfälle).
  **2162/2162** erfüllen X/24 = Index/11 und Y/24 = Index%11.
* **Ebene 1 von Block B ist in 43/43 Dateien vollständig 0.**
* Gegenprobe an den Karten (Sektion 20, 41 Karten, 457 Regelzellen): der Wert
  ist **entweder 0 oder 100…120 — nie etwas anderes**, obwohl dasselbe Gitter
  sonst 1…60 (Bahnlinien) führt. ⭐ Das bestätigt **Abschnitt N** von einer
  ganz anderen Seite: `Code = 100 + Brückennummer`.

### Die Zuordnung

**AI01…AI33 ↔ Missionen 1…33**, **AI51…AI58 ↔ NET01…NET08**; `AI00` und `AI34`
haben auf keiner CD eine Karte. 33 + 8 + 2 = 43 ✔

Härteste Zahl: für alle 41 zuordenbaren Dateien liegt **jeder** benutzte
Knoten im Sektorrechteck der zugehörigen Karte (0 Ausreisser), und in 18 von 41
ist die Knotenzahl exakt ⌈W/24⌉·⌈H/24⌉. Die 16 auf beiden CDs doppelt
liegenden Dateien sind byteweise identisch und entsprechen genau den
Missionsnummern der `.DM`-Karten.

⚠ **`0x41C850` ist kein Lader, sondern der ERZEUGER**: fehlt die CD-Datei,
scannt das Spiel die Karte selbst und **schreibt die Datei mit `"wb"`**.

### Was auch hier offen bleibt

* **Block B, Ebene 1** ist in allen 43 Dateien null; das Format hat Platz für
  einen zweiten Ankersatz, ein Leser dafür ist nicht aufgetaucht.
* Woher das Byte kommt, das der Brückenbauer ins Liniengitter stempelt —
  »Code = 100 + Brückennummer« ist aus den **Kartendaten** erschlossen, nicht
  am Code abgelesen.
* Warum 288 der 457 Regelzellen in der ausgelieferten Karte 0 lesen.
  Verträglich mit »die Brücke muss erst gebaut werden«, aber unbeobachtet.

---

## X. `game.007` als zweite Quelle — vierzehn Widersprüche (21.08.2026)

Der Spielstand liest sich restlos: **131 Abschnitte, 0 Byte Rest**, Mission 1,
42×72 — dieselbe Karte wie `01.CWM`. Von 76 geprüften Kartendateien schaffen
das nur `1.DM` und `game.007`.

⚠ **Der Wert dieser Prüfung liegt in dem, was sie WIDERLEGT.** Alle folgenden
Punkte sind Befunde gegen unsere **eigenen** Dokumente. Sie sind aus den Daten
gemessen und **nicht** gegen die zwei GAME.EXE gegengelesen — wer danach baut,
liest erst nach.

### Die zwei, die am meisten wehtun

**W1 — die Tafel »unit_type → hp_max« in `GAMESTATE_RE.md` §2 ist die
`fuel_max`-Tafel.** Über 192 belegte Einheitensätze: gegen `+0x29`
(energie_max) **0 Treffer, 172 Widersprüche**, und die Zuordnung ist nicht
einmal eindeutig. Gegen `+0x30` (fuel_max) sind **14 von 14** Schlüsseln
eindeutig und 12 von 12 gemeinsamen Typen exakt getroffen.
⭐ **Folge:** »148, 149 = non-combat (scenery/waypoint)« ist falsch — es sind
**Infanteristen** (`+0x0d` = 190 = die Infanteriewaffe). ✔ Unser *Spielcode*
führt sie längst richtig (`d.Propulsion is 148 or 149 → infOwn++`); der Fehler
steckt allein im RE-Dokument.

**W8 — `GAMESTATE_RE.md` §3.8 trägt oben noch die ZURÜCKGEZOGENE Satzform**
(»4-Byte-Kopf + 255 Sätze bei `4 + 76·k`«), während §3.86 sie längst zugunsten
von `76·k` und 300 Sätzen widerruft. Gegenprobe: mit `76·k` liegen **0 von
3545** Gebäuden ausserhalb der Karte. **Wer §3.8 von oben liest, baut den
Fehler nach.**

### Die übrigen zwölf, knapp

| | Behauptung | Messung |
|---|---|---|
| W2 | `+0x08 == +0x29` als Blickrichtungs-Kandidat | 24 von 28 `<`, 0 `>` — es ist Leben/Höchstleben, Kandidat erledigt |
| W3 | `+0x0f`: »Rumpftypen 160…175« | **66 von 192** ausserhalb (148, 149, 153, 158) |
| W4 | `+0x14`: »kleiner Index < 0x1e« | **14 von 192** darüber, UKOL 100 und 54 |
| W5 | `+0x26` = »category/role« gegen »Attack« | **18 von 192** ausserhalb der genannten Menge; **keine** der 58 Bauteilspalten reproduziert es. **Ungeklärt** |
| W6 | `+0x06 KOLIK`: »Zähler bis 80« | vorzeichenbehaftet, **−112…+119** |
| W7 | Gebäudebesitzer `sec3 +0x05` 0…7/0xFF | **1006 von 3545** tragen **11** |
| W9 | `+0x40` = sec37-Zeiger | `1.DM`: sec37 **restlos null**, aber 19 Einheiten mit `+0x40` ∈ 1…18. Ein Vorfilter »`+0x40 != 0`« ist an beiden Enden falsch |
| W10 | sec16 `+0x00` u16 Anzahl, »445 von 445« | u8 trifft 93,9 %, u16 85,9 % — **keine** Lesart ist ausnahmefrei |
| W11 | §3.80 »belegte Plätze == `cis_typ`-Menge« | `1.DM`: 16 gegen 15 |
| W12 | §3.87 »kein y ausserhalb 0..2H« | **3** Waggons mit y = 146/148 gegen 2H = 144 |
| W13 | sec119/120 Namen | **sprachabhängig** — `game.007` deutsch, `1.DM` englisch |
| W14 | `+0x08/+0x29/+0x2E/+0x30` »echt ungelesen« | alle vier sind in `GAMESTATE_RE` benannt |

### ⭐ Ein neuer Befund nebenbei: sec100 ist die ENGLISCHE Bauteiltafel

sec46 und sec100 sind beide 92 800 B = 1600 × 58 und zu **84,5 %** byteweise
gleich. In `game.007` ist sec46 deutsch (Kanone, S.Kanone, SchallKmp.) und
sec100 englisch (Cannon, H-Cannon, Radiator); in `1.DM` sind **beide**
englisch. **sec46 ist die lokalisierte Kopie, sec100 die englische** — und
sec100 wird bei uns gar nicht gelesen. Praktische Folge: **jede namensbasierte
Gegenprobe gegen sec46 ist über Dateien hinweg nicht stabil.**

### Was die Prüfung BESTÄTIGT hat

* `ZBRAN = VRSEK − 20`: **110 von 132**, die 22 Ausnahmen sind genau die
  ausgenommenen Bereiche — unabhängige Bestätigung von der Datenseite.
* `exp = (byte[+0x28]<<8) | byte[+0x4C]`: frische Karte 0 von 37 ungleich null,
  gespielter Stand **16 von 28**, Höchstwert **466** — nur erreichbar, wenn
  `+0x28` das obere Byte ist.
* `+0x09 == 0xFF` als leerer Platz: 0 Gegenbeispiele. ⚠ Der höchste Wert bei
  einer lebenden Einheit ist **194** — der Abstand zu 0xFF ist nur 61.
* `+0x34/+0x36` sind **u16**-Griffe, nicht Byte.
* sec2 3024 Zellen alle in {0,1,2,3}; sec6 25 Griffe, alle lebend; sec20
  **15 von 15** Lagenbytes 100 gegen genau eine Brücke in sec17; sec53
  Bündnismatrix symmetrisch 64 von 64; sec47 **alle 592** Entwürfe mit
  lesbarem cp437-Namen.

### Die lohnendsten ungedeuteten Abschnitte

Gemessen als »Byte, die vom häufigsten Füllbyte abweichen«: **sec51** (131 072,
davon 65 617 aussagend, 256×256 u16), **sec39** (260 000 / 41 506, 32 500 × 8),
**sec42** (20 000 / 7 709) und **sec52** — ein **zweites Griffgitter**
256×256 u16 mit Füllung `0xFFFF` statt `0xFFFC`, das in 24 von 25 Zellen mit
sec6 übereinstimmt. Insgesamt **42 belegte Abschnitte ohne volle Deutung**.
---

## Y. ⭐ Die vollständige LADERTAFEL, und was sie aufschliesst (21.08.2026)

Das Werkzeug, das lange gefehlt hat. Der Lader (C `0x41E070`, F `0x41D230`) und
der **Speicherer** (C `0x41D210`, F `0x41C3D0`) tragen beide dieselbe Folge von
131 `(Ziel, Grösse)`-Paaren. **Alle vier Tafeln getrennt gezogen — 0
Abweichungen.** Die Grössen sind in C und F byteweise gleich.

**Wozu das gut ist:** wer weiss, wohin ein Abschnitt kopiert wird, findet über
einen Adressabtast des `.text` alle Funktionen, die ihn anfassen — und das Spiel
benennt seine Funktionen selbst. Damit sind in einem Durchgang **rund zwanzig**
Abschnitte eingeordnet worden, statt jeden einzeln zu erraten.

### ⚠⚠ BERICHTIGUNG AN EINER REGEL, DIE WIR ÜBERALL ZITIEREN

Wir schreiben seit Wochen »im `.bss` gilt C = F + `0xFA0`«. Das stimmt — **aber
im `.data` gilt es nicht**, und dort ist der Versatz auch nicht einheitlich:

| Versatz | Abschnitte |
|---|---|
| `0xF98` | sec13, sec14 |
| `0xFA0` | alles im `.bss` |
| `0xFC0` | sec7–10, **sec46, sec47**, sec105, sec109, **sec119, sec120**, sec131 |
| `0xFC8` | sec61 |
| `0xFF8` | sec54, sec99, sec111, sec127 |
| `0x1000` | sec125 |
| `0x1004` | sec129, sec130 |

**Selbst nachgeprüft**, und zwar hart: an den vier `.data`-Paaren sec46, sec47,
sec119 und sec120 sind die ersten 64 Byte in beiden Bauten **byteweise
identisch** — die Paarung mit `0xFC0` ist damit bewiesen, nicht geschlossen.
(`.bss`-Abschnitte liefern beim Vergleich nichts, weil sie gar keinen
Dateiinhalt haben.)

⚠ **Wer eine `.data`-Adresse umrechnet, muss den Einzelfall nehmen.**

### Ein zweiter Fund aus der Tafel selbst

**sec89 und sec114 laden in DENSELBEN Puffer** `0x6786A8`, beide 1200 B —
belegt in allen vier Tafeln. sec114 überschreibt also sec89. Entweder ein
Fehler im Spiel oder zwei Namen für dieselbe Tabelle; die 1200 Byte stehen in
der Datei aber **zweimal**. Welche der beiden das Spiel danach benutzt, ist
offen.

### Die Tafel

Grössen in Byte. `.CWM` lädt sec1…38, `.DM` alle 131 (Kopfbyte 3, `@0x41E6A5`).


| Abschnitt | Grösse | Ziel C | Ziel F | C−F |
|---:|---:|---|---|---|
| 1 | W*H*4 | *(Zeiger)* | *(Zeiger)* | — |
| 2 | 66049 | `0xA3AEB0` | `0xA39F10` | `0xFA0` |
| 3 | 22800 | `0xC06910` | `0xC05970` | `0xFA0` |
| 4 | 12000 | `0xC03A30` | `0xC02A90` | `0xFA0` |
| 5 | 624000 | `0x6E26C8` | `0x6E1728` | `0xFA0` |
| 6 | 131072 | `0xBDEA80` | `0xBDDAE0` | `0xFA0` |
| 7 | 4 | `0x5387AC` | `0x5377EC` | `0xFC0` |
| 8 | 4 | `0x5387B0` | `0x5377F0` | `0xFC0` |
| 9 | 4 | `0x5387B8` | `0x5377F8` | `0xFC0` |
| 10 | 4 | `0x5387BC` | `0x5377FC` | `0xFC0` |
| 11 | 2000 | `0xBDA0E8` | `0xBD9148` | `0xFA0` |
| 12 | 1000 | `0xBDA8C0` | `0xBD9920` | `0xFA0` |
| 13 | 2 | `0x539B10` | `0x538B78` | `0xF98` |
| 14 | 2 | `0x539B14` | `0x538B7C` | `0xF98` |
| 15 | 400000 | `0x7AEC38` | `0x7ADC98` | `0xFA0` |
| 16 | 88000 | `0x7847E8` | `0x783848` | `0xFA0` |
| 17 | 2400 | `0xBFEA80` | `0xBFDAE0` | `0xFA0` |
| 18 | 18000 | `0xBFF3E0` | `0xBFE440` | `0xFA0` |
| 19 | 13600 | `0x6DDF70` | `0x6DCFD0` | `0xFA0` |
| 20 | 65536 | `0x542E18` | `0x541E78` | `0xFA0` |
| 21 | 200 | `0xC2FCB8` | `0xC2ED18` | `0xFA0` |
| 22 | 15000 | `0xC2C220` | `0xC2B280` | `0xFA0` |
| 23 | 800 | `0x878E58` | `0x877EB8` | `0xFA0` |
| 24 | 700 | `0x87A2C0` | `0x879320` | `0xFA0` |
| 25 | 700 | `0x879F38` | `0x878F98` | `0xFA0` |
| 26 | 200 | `0x87A5A8` | `0x879608` | `0xFA0` |
| 27 | 2600 | `0x879438` | `0x878498` | `0xFA0` |
| 28 | 900 | `0x878AD0` | `0x877B30` | `0xFA0` |
| 29 | 200 | `0x87A1F8` | `0x879258` | `0xFA0` |
| 30 | 700 | `0x879178` | `0x8781D8` | `0xFA0` |
| 31 | 200 | `0x879E70` | `0x878ED0` | `0xFA0` |
| 32 | 169 | `0xA68E68` | `0xA67EC8` | `0xFA0` |
| 33 | 960 | `0xA8D508` | `0xA8C568` | `0xFA0` |
| 34 | 17120 | `0xA89220` | `0xA88280` | `0xFA0` |
| 35 | 16481 | `0xA8D8C8` | `0xA8C928` | `0xFA0` |
| 36 | 10500 | `0x830790` | `0x82F7F0` | `0xFA0` |
| 37 | 3800 | `0xBBFEF8` | `0xBBEF58` | `0xFA0` |
| 38 | 700 | `0x6783E8` | `0x677448` | `0xFA0` |
| 39 | 260000 | `0x9CB0B8` | `0x9CA118` | `0xFA0` |
| 40 | 500 | `0xA0A858` | `0xA098B8` | `0xFA0` |
| 41 | 10000 | `0x9C6FB8` | `0x9C6018` | `0xFA0` |
| 42 | 20000 | `0x8106C0` | `0x80F720` | `0xFA0` |
| 43 | 32032 | `0x884730` | `0x883790` | `0xFA0` |
| 44 | 5760 | `0xB95F48` | `0xB94FA8` | `0xFA0` |
| 45 | 6000 | `0x9C9948` | `0x9C89A8` | `0xFA0` |
| 46 | 92800 | `0x5045A0` | `0x5035E0` | `0xFC0` |
| 47 | 73600 | `0x51CE20` | `0x51BE60` | `0xFC0` |
| 48 | 7200 | `0x77AC50` | `0x779CB0` | `0xFA0` |
| 49 | 9600 | `0xBC0DD0` | `0xBBFE30` | `0xFA0` |
| 50 | 65536 | `0x678B58` | `0x677BB8` | `0xFA0` |
| 51 | 131072 | `0x5539D0` | `0x552A30` | `0xFA0` |
| 52 | 131072 | `0xC0C220` | `0xC0B280` | `0xFA0` |
| 53 | 320 | `0x87B140` | `0x87A1A0` | `0xFA0` |
| 54 | 4 | `0x4FA240` | `0x4F9248` | `0xFF8` |
| 55 | 1936 | `0xB461C0` | `0xB45220` | `0xFA0` |
| 56 | 11616 | `0xB3D390` | `0xB3C3F0` | `0xFA0` |
| 57 | 32 | `0xB461A0` | `0xB45200` | `0xFA0` |
| 58 | 16 | `0xB38D40` | `0xB37DA0` | `0xFA0` |
| 59 | 8 | `0xB46950` | `0xB459B0` | `0xFA0` |
| 60 | 24000 | `0xB400F0` | `0xB3F150` | `0xFA0` |
| 61 | 8 | `0x538BD8` | `0x537C10` | `0xFC8` |
| 62 | 4080 | `0xBC41E0` | `0xBC3240` | `0xFA0` |
| 63 | 1200 | `0xBC51D0` | `0xBC4230` | `0xFA0` |
| 64 | 4 | `0xBC5680` | `0xBC46E0` | `0xFA0` |
| 65 | 4 | `0xBC5684` | `0xBC46E4` | `0xFA0` |
| 66 | 4 | `0xBC5688` | `0xBC46E8` | `0xFA0` |
| 67 | 8 | `0xB38528` | `0xB37588` | `0xFA0` |
| 68 | 6464 | `0xB36BE0` | `0xB35C40` | `0xFA0` |
| 69 | 4800 | `0xBC5A78` | `0xBC4AD8` | `0xFA0` |
| 70 | 8 | `0xBC6D38` | `0xBC5D98` | `0xFA0` |
| 71 | 500 | `0x87AE00` | `0x879E60` | `0xFA0` |
| 72 | 1000 | `0xBC5690` | `0xBC46F0` | `0xFA0` |
| 73 | 32 | `0xA9C600` | `0xA9B660` | `0xFA0` |
| 74 | 4 | `0xA9A1D8` | `0xA99238` | `0xFA0` |
| 75 | 8 | `0xA9A200` | `0xA99260` | `0xFA0` |
| 76 | 8 | `0xA9A208` | `0xA99268` | `0xFA0` |
| 77 | 8 | `0xB46198` | `0xB451F8` | `0xFA0` |
| 78 | 300 | `0xBC6D40` | `0xBC5DA0` | `0xFA0` |
| 79 | 800 | `0x834C38` | `0x833C98` | `0xFA0` |
| 80 | 92 | `0x799FA8` | `0x799008` | `0xFA0` |
| 81 | 4220 | `0x833A00` | `0x832A60` | `0xFA0` |
| 82 | 32000 | `0x77CAE8` | `0x77BB48` | `0xFA0` |
| 83 | 400 | `0x833870` | `0x8328D0` | `0xFA0` |
| 84 | 3000 | `0x552E18` | `0x551E78` | `0xFA0` |
| 85 | 3000 | `0x688B58` | `0x687BB8` | `0xFA0` |
| 86 | 1200 | `0x677F30` | `0x676F90` | `0xFA0` |
| 87 | 200 | `0x834B70` | `0x833BD0` | `0xFA0` |
| 88 | 200 | `0x88E390` | `0x88D3F0` | `0xFA0` |
| 89 | 1200 | `0x6786A8` | `0x677708` | `0xFA0` |
| 90 | 640 | `0xB49E50` | `0xB48EB0` | `0xFA0` |
| 91 | 6000 | `0xB4A0D0` | `0xB49130` | `0xFA0` |
| 92 | 50 | `0xB49C50` | `0xB48CB0` | `0xFA0` |
| 93 | 50 | `0xB49C88` | `0xB48CE8` | `0xFA0` |
| 94 | 3900 | `0x82AA30` | `0x829A90` | `0xFA0` |
| 95 | 100 | `0x81A3A8` | `0x819408` | `0xFA0` |
| 96 | 160 | `0xA3A9D0` | `0xA39A30` | `0xFA0` |
| 97 | 2 | `0xA3A9C8` | `0xA39A28` | `0xFA0` |
| 98 | 1560 | `0x81A410` | `0x819470` | `0xFA0` |
| 99 | 1 | `0x4FA27C` | `0x4F9284` | `0xFF8` |
| 100 | 92800 | `0xA9C620` | `0xA9B680` | `0xFA0` |
| 101 | 9200 | `0xA9A210` | `0xA99270` | `0xFA0` |
| 102 | 500 | `0x87AC08` | `0x879C68` | `0xFA0` |
| 103 | 1000 | `0xBC3DF8` | `0xBC2E58` | `0xFA0` |
| 104 | 32 | `0xA9A1E0` | `0xA99240` | `0xFA0` |
| 105 | 1 | `0x504598` | `0x5035D8` | `0xFC0` |
| 106 | 8 | `0xB38D38` | `0xB37D98` | `0xFA0` |
| 107 | 160 | `0xB36B20` | `0xB35B80` | `0xFA0` |
| 108 | 1984 | `0xB3CBD0` | `0xB3BC30` | `0xFA0` |
| 109 | 8 | `0x538BB0` | `0x537BF0` | `0xFC0` |
| 110 | 32 | `0xB36BC0` | `0xB35C20` | `0xFA0` |
| 111 | 1 | `0x4FA284` | `0x4F928C` | `0xFF8` |
| 112 | 72000 | `0xA51110` | `0xA50170` | `0xFA0` |
| 113 | 2400 | `0xA62A50` | `0xA61AB0` | `0xFA0` |
| 114 | 1200 | `0x6786A8` | `0x677708` | `0xFA0` |
| 115 | 1 | `0x81AA2C` | `0x819A8C` | `0xFA0` |
| 116 | 1 | `0x8154E4` | `0x814544` | `0xFA0` |
| 117 | 4 | `0x833868` | `0x8328C8` | `0xFA0` |
| 118 | 1 | `0x81AA28` | `0x819A88` | `0xFA0` |
| 119 | 3360 | `0x52EDA0` | `0x52DDE0` | `0xFC0` |
| 120 | 7680 | `0x51B020` | `0x51A060` | `0xFC0` |
| 121 | 480 | `0xB95D60` | `0xB94DC0` | `0xFA0` |
| 122 | 320 | `0xA66DD8` | `0xA65E38` | `0xFA0` |
| 123 | 255 | `0xB45EB0` | `0xB44F10` | `0xFA0` |
| 124 | 256 | `0x991718` | `0x990778` | `0xFA0` |
| 125 | 4 | `0x4F5AEC` | `0x4F4AEC` | `0x1000` |
| 126 | 4000 | `0x53C938` | `0x53B998` | `0xFA0` |
| 127 | 4 | `0x4FAD14` | `0x4F9D1C` | `0xFF8` |
| 128 | 2 | `0x9937E0` | `0x992840` | `0xFA0` |
| 129 | 1 | `0x4F6FA0` | `0x4F5F9C` | `0x1004` |
| 130 | 1 | `0x4F6FA4` | `0x4F5FA0` | `0x1004` |
| 131 | 8 | `0x538BB8` | `0x537BF8` | `0xFC0` |


### Was der Adressabtast aufgeschlossen hat

Der Abtast war streng: ein Dword zählt nur, wenn es an einer echten x86-Form
steht **und beide EXE denselben relativen Versatz** in den Puffer zeigen. Das
ist der entscheidende Filter — bei sec15 fiel die Rohzahl von 66 auf 15, bei
sec52 von 54 auf 11; der Rest waren Nachbarglobale, die im selben Adressfenster
liegen, aber in den zwei Bauten verschieden.

**Am Code gelesen** (Schrittweite aus den `lea`-Ketten, in beiden EXE):

| Abschnitt | Grösse | Form | was es ist |
|---|---:|---|---|
| **sec15** | 400 000 | **8000 × 50 B** | **der WEG einer Einheit** — Richtungscodes, `0xFF` = Ende. Schreiber ist die Wegsuche (C `0x4D2CEF`), Leser die Bewegung (C `0x407C57`, setzt `+0x04 = 0xFF` = »steht«) |
| **sec48** | 7 200 | **400 × 18 B** | `rob_trans` — der Index steht im Einheitensatz bei **`+0x40`**. Die Funktion sagt es selbst: »**Transport check : wrong index of robot in 'rob_trans'**« |
| **sec94** | 3 900 | **50 × 78 B** | Einheitensätze im **Marktlager** (dieselbe Satzgrösse wie sec5). »**Cannot add new unit to market-store**« |
| **sec81** | 4 220 | **10 × 422 B** | die **benannten Gruppen** — 22 B Name, dann 200 Wortplätze. ✔ Deckt sich mit unserer Lesung in `MapEntityLayer` |
| **sec63** | 1 200 | **8 × 50 × 3 B** | die **Bauschlange** je Spieler. »**Cannot add new 'vyroba'**« |
| **sec91** | 6 000 | **1000 × 6 B** | die **Verkaufsschlange**. »**Robot already sold.**« |
| **sec98** | 1 560 | **20 × 78 B** | wieder Einheitensätze — **Lagerbestand eines Gebäudes** (Depot/Hangar), 20 fertige Stück |
| **sec18** | 18 000 | **6000 × 3 B** | die **brennenden Waldfelder**. »**hori forest / dohorel forest — sjizdnej / nesjizdnej**« (brennt / abgebrannt — befahrbar / nicht) |
| **sec80** | 92 | **4 × 23 B** | die vier **Merkpunkte** (21 B Name, Spalte, Zeile, `0xFF` = leer). ✔ Deckt sich mit Abschnitt N unserer Lesung |
| **sec55 / sec56** | 1 936 / 11 616 | ⚠ sec55 **zellendur** `8·Zelle + Spieler` (in Abschnitt AC berichtigt) / sec56 **8 × 121 × 12** | das **KI-Wichtigkeitsraster** (»imp«), je Spieler 11 × 11 Zellen — **dasselbe Raster wie die `AI*.CWI`** aus Abschnitt W |
| **sec11 / 12 / 13** | 2 000 / 1 000 / 2 | 1000 × u16 / 1000 × u8 / Zähler | **eine Suchliste**. ⚠ Die u16 sind **Einheitenplätze, keine Zellen** — in Abschnitt AD an den Daten berichtigt (968/968 lebende Plätze) |
| **sec32** | 169 | **13 × 13** | ⚠ **nicht** die Verbindungsmatrix dieser Karte — in allen 14 Dateien byteidentisch (Abschnitt AD). Eine statische Typentafel |
| **sec40** | 500 | 500 Belegt-Bytes | die Belegungsmarken der `ROB_PROD`-Tafel. »**WRONG ROB_PROD in PLACE!!!!**« |
| **sec95** | 100 | 50 × u16 | parallel zu sec94 — Preis **oder** Restzeit, das ist noch offen |
| **sec54** | 4 | Dword | der **laufende Taktwert**, gesetzt in derselben Uhrroutine wie der 250er-Vorzähler sec118 |
| **sec64/65/66** | je 4 | Dword | die drei **Zeitzähler der Missionsuhr**, angestossen von sec118 |
| **sec72** | 1 000 | 500 × u16 | die **Missionsvariablen v[0…499]** — ✔ das kennen wir längst (`VAR_BASE = 0xBC5690`), es stand nur nicht in `GAMESTATE_RE.md` |

**Erklärbar, aber nur angelesen:** sec60 (24 000, KI-Tabelle, 11 benannte
Leser), ⚠ sec68, NICHT sec108 (KI-**Angriffsgruppen**, »Attack group not available«), sec110 /
sec57 (KI-Kopfwerte je Spieler), sec71 (500 B am Hilfetextfenster —
*vermutet*: welcher `HELPG`-Text schon gezeigt wurde), sec62 (1020 Dwords im
Kampagnenzustand), sec131 (Zustand des Einheiten-Auswahlfensters).

### ⚠⚠ ZURÜCKGEZOGEN: sec101 ist NICHT tot — siehe Abschnitt AD

Der folgende Befund hat der Messung an echten Daten **nicht standgehalten** und
bleibt nur als Beleg stehen. sec101 ist die **Sicherungskopie der ersten 200
Entwürfe**: 1010 von 9200 Byte sind in allen 14 Dateien belegt, und eine
`rep movsd`-Blockkopie (`ecx = 0x8FC`) schaufelt sec47 ↔ sec101. Der
Adressabtast fand sie nicht, weil eine Blockkopie ihre Adressen in `esi`/`edi`
lädt statt als Konstante. **»Kein Leser gefunden« heisst nicht »kein Leser«.**

### ~~⭐ Und eine tote Tafel: sec101~~

**9 200 Byte bei C `0xA9A210` — KEIN LESER.** Roh über das ganze `.text`: C = 9
Treffer, F = 5. Die fünf in F sind Init, Lader, Speicherer und zweimal der
Missions-Reset; die vier zusätzlichen in C haben in F **keinen** Gegenpart mit
gleichem Versatz — Zufallstreffer. Der einzige verbleibende »Leser« ist ein
Massen-Nullsetzer.

→ **sec101 wird geschrieben, gespeichert, genullt — und nie gelesen.** Das ist
nach den vier toten Mauszeigern, den zwei toten Befehlsnummern (523/526), den
34 toten `SPR.DAT`-Bildern und dem toten Zufall in den Minensätzen der fünfte
Fund dieser Art.

### Was als Nächstes lohnt

1. **sec60** (24 000 B, 11 benannte KI-Leser) — die grösste offene Tafel mit
   Lesern, die einen Namen tragen.
2. **sec62** (4 080 B) — liegt mitten im schon gelesenen Missionszustand.
3. **sec108 + sec55/56/110/57** als Paket »KI-Zustand«; sec56 ist gelesen, die
   Nachbarn teilen den Zellenindex.
4. **sec95** — nur noch Preis oder Restzeit.
5. **sec18** — Schrittweite und Name stehen, offen ist die Feldbelegung.
6. **Nicht mehr anfassen:** sec101 (tot), sec72 und sec64–66 (anderswo gedeutet).

### ⚠ Was diese Lesung NICHT ist

Alle Schrittweiten oben sind **am Code beider EXE gelesen, aber nicht an
Prüfdaten gemessen** — `game.007` und `1.DM` wurden dabei nicht geöffnet. Der
nächste Schritt wäre, jede Form gegen die 131 Abschnitte in `game.007` zu
halten. Bei sec81 und sec80 haben wir das indirekt schon: beide decken sich mit
dem, was wir aus den Dateien gelesen hatten.

Offen blieben ausserdem: **sec1** (Ziel steht in einem Register, nicht als
Konstante), **sec9/sec10** (als Paar erkennbar, ohne Protokollmarke nicht
benennbar), und die Frage, welche der beiden Tafeln **sec89/sec114** das Spiel
benutzt.


---

## Z. ⭐ sec50 + sec51 + sec52 sind das GEDÄCHTNIS des Nebels (21.08.2026)

Die zwei 256×256-Gitter gehören zusammen — und mit sec50 zu dritt. Der Anstoss
steht im Haupttakt unter der Marke **`unexplored`**.

| | Ziel C | Ziel F | Form | was es merkt |
|---|---|---|---|---|
| **sec50** | `0x678B58` | `0x677BB8` | 65536 × u8 | **jetzt sichtbar** (0/1/2) |
| **sec51** | `0x5539D0` | `0x552A30` | 65536 × u16 | das **Bodenbild**, das der Spieler sieht |
| **sec52** | `0xC0C220` | `0xC0B280` | 65536 × u16 | der zuletzt gesehene **sec6-Griff** |

Index überall `spalte·256 + zeile` — **kein Tausch**, am Holer `0x41D0C0`
belegt (`shl ecx,8; add ecx,zeile; mov ax, word[ecx*2+0x5539D0]`).

### Der Motor

`0x4205B0` läuft bei **`tick % 5 == 1`**: sec50 komplett nullen, dann für jedes
**verbündete** Gebäude und jede Einheit einen Kreis vom Sichtradius stempeln
(Sehnentafel `0x4F8A48`, Radius ≤ 19). Je gestempelter Zelle:
`sec50 = 1`, `sec52 = sec6`, `sec51 = das rohe Wort des Kachelgitters`.

### ⭐ Der Beweis steht im Gegenteil

In `1.DM` stehen **22 Objekte in sec6, die nie in sec52 auftauchen — 21 davon
Einheiten, ALLE Besitzer 1.** Der Spieler ist 0 und nur mit 3 verbündet;
gesehen wurden Besitzer 3 (37×), 0 (21×), 2 (10×), 1 (4×). **Das ist Nebel des
Krieges, in Zahlen.**

Und die Abweichungen zwischen sec52 und sec6 sind **Geister**: game.007 hat
genau einen (eine Einheit ist eine Kachel weitergefahren), `1.DM` dreizehn —
**13 von 14 Geisterzellen liegen ≤ 2 Kacheln neben der heutigen Position**. Die
Gemeinsamkeit ist nicht Art oder Grundriss, sondern **Bewegung seit dem letzten
Blick**.

### Die `0xFFFF`-Frage ist beantwortet

sec6 füllt mit `0xFFFC`, sec52 mit `0xFFFF` — **anderer Schreiber, anderer
Leerwert**: die Missionsanfangs-Rüstung `0x437F10` setzt `0xFFFF` (und nullt
dabei zugleich die Uhrbytes). Übereinstimmung sec52 ↔ sec6 im Übrigen:
**98,8 %** (game.007) und **98,5 %** (1.DM).

### Was in sec51 steht

Je Zelle der Sprite-Code, den der Spieler sieht: `< 10000` → Bank `0xBB4588`,
`≥ 10000` → Bank `0xBA5100`, `0xFFFF` → das Ersatzbild aus **`SPR.DAT` Nr. 19**
(siehe Abschnitt V — dort war es die einzige je gelesene Kachel; hier steht,
wofür).

Zwei Quellen füllen es: eine **nie gesehene** Bodenzelle bekommt
`Basis + rnd()%Anzahl` aus einer Tafel, deren Schlüssel aus den **vier Eckwerten
von sec2** (Höhe und Neigung) und der Geländeart gebildet wird; eine **gesehene**
bekommt das rohe Wort des Kachelgitters.

**Die Zahlen:** wo sec50 ≠ 0 ist, gilt `sec51 == Kachelwort` in **235/235**
(game.007) und **7085/7085 = 100 %** (1.DM). Die nie gesehenen Zellen zerfallen
nach dem Schlüssel in 125 bzw. 70 Gruppen, deren Wertebereiche sich in **keinem
Fall überlappen**.

⚠ **Ausserhalb `W×H` ist sec51 Müll** — dort wird nie geschrieben. Nicht deuten.

⚠ **Zwei Mitspieler gehen beim Speichern verloren:** `0x689710` (die gemerkte
Zeichenlage je Zelle) und `0x5739D8` (die 257²-Eckmaske) sind **keine
Abschnitte**. Nach dem Laden ist die Lagen-Erinnerung weg.

---

## AA. ⭐ sec39 und sec42 — und eine Bestätigung über Kreuz (21.08.2026)

### sec39 sind die FAHRSPUREN — und »32500 × 8« war falsch

**500 Gruppen × 40 Sätze × 13 Byte** = 260 000. Belegt an **neun voneinander
unabhängigen Fundstellen, alle in beiden EXE** — Neustart-Init, Alterungstakt,
Zeichenliste, Zuteiler, Zeichner und vier Gruppen-Zuteiler.

| Versatz | Breite | Bedeutung |
|---|---|---|
| +0x00 / +0x01 | u8 | Kartenspalte / Kartenzeile |
| +0x02 | u8 | Richtung 0…7, Bit 7 = Halbfeld |
| +0x03 | u8 | Anzahl belegter Marken (1…8) |
| +0x04 | u8 | **Verfallszähler, Start 180, 0 = FREI** |
| +0x05…+0x0C | u8[8] | Spurvariante je Einzelmarke |

Der Gruppenindex steht in **Einheit `+0x24`** (`0x2710` = keine), die
Belegungsflaggen in **sec40**.

⭐ **NUR RADFAHRZEUGE HINTERLASSEN SPUREN.** Beim Erzeugen prüft der Spawn das
Fahrwerk gegen die Flaggentafel `0x4FA24F`: Flagge 1 haben Reifen, Panzerreifen,
6×6, Ketten, Schwere Ketten, Luftkissen … — **Flagge 0 haben genau die drei
beinigen: Spinne (1), Stahlsucher (16), Läufer (17).**

### ⭐⭐ Zwei Agenten, dieselbe Mechanik von zwei Seiten

Der Agent für `MARK.CWK` (Abschnitt V) fand: eine Punktschablone,
5 Bodenarten × 8 Richtungen × 8 Punkte, dazu ein Markenspeicher bei
`0x9CB0B8` mit **13 Byte je Satz, 500 × 40**, Lebensdauer **180** Takte,
gezeichnet als acht abgedunkelte Bildpunkte.

Der Agent für sec39 fand — ohne davon zu wissen — **dieselbe Tafel unter ihrer
Abschnittsnummer**, mit denselben Zahlen. `0x9CB0B8` **ist** sec39.

**`MARK.CWK` ist die Schablone, sec39 der Speicher.** Zwei unabhängige Wege,
dasselbe Ergebnis — das ist die stärkste Bestätigung, die dieses Projekt kennt.

### Die Gegenprobe zur alten Form

Unter Schrittweite 8 liegen die Koordinaten in **keinem** Fall zu 100 % auf der
Karte (bestenfalls 88,6 % / 96,9 %); unter 13 sind es **100,0 %** in beiden
Dateien bei drei unabhängigen Prüfgrössen. Und die »7470 belegten Sätze« der
alten Zählung waren Müll: beim Zuteilen wird nur `+0x04` genullt, der Rest
bleibt stehen.

Belegt sind wirklich **31** (game.007) und **698** (1.DM) Sätze — und **6/6
bzw. 52/52** der benutzten Gruppen gehören einer **lebenden** Einheit.

### sec42 sind die laufenden ANIMATIONEN — 2000 × 10 stimmt

Jetzt am Leser belegt statt aus der Grösse geraten. `+0x00`/`+0x01` Zelle,
`+0x02`/`+0x03` Teilfeld 0…39, `+0x04` i16 Höhe über Boden, **`+0x06` u16
Kennung (`0xFFFF` = frei)**, `+0x08` laufendes Einzelbild, **`+0x09`
ungenutzt** (0 in allen 2000 Sätzen beider Dateien).

Angelegt an **37 Stellen** je EXE — Einschläge, Explosionen, Laser und der
Fahrstaub aus »jedu:«. Kennungen **42…44** rücken nur jeden zweiten Takt vor.

⭐ Eine hübsche Rückkopplung: die einzigen Kennungen in `1.DM` sind 42 (9×),
44 (9×), 43 (3×) und 312 (2×) — **genau die drei, die der Takt gesondert
behandelt.**

### Berichtigung an `GAMESTATE_RE.md`

§3.83 führt »sec39 32500 × 8«. Richtig ist **500 × 40 × 13**. Nebenbei am
Neustart-Init mitgeprüft und bestätigt: sec41 1000×10, sec42 2000×10,
sec44 240×24, sec45 1000×6; neu ablesbar sec43 Schritt 0x20, sec48 400×18,
sec49 200×48.

### Was offen bleibt

* Die **zweite Spurensorte** (Marken 10…17, für Fahrwerk 7 und 9) kommt in
  keiner Prüfdatei vor — 0 von 1613 Marken; ihr Zeichenzweig ist unbelegt.
* In sec40 sind **281 bzw. 213** Flaggen gesetzt, für die keine Einheit mehr
  existiert — die Freigabe wird offenbar nicht auf jedem Todesweg erreicht.
  Umgekehrt gilt lückenlos: **keine** Einheit hält eine Gruppe ohne Flagge.
* `game.007` hat **0** belegte sec42-Sätze; alle Wertebereiche dort stützen
  sich auf die 23 Sätze aus `1.DM`.
---

## AB. Der Kampagnenzustand — und wie eine Mission in die nächste übergeht (21.08.2026)

### sec62 ist die `imp`-Tafel der Computerspieler — 8 × 255 × 2 B

Nicht 1020 Dwords, wie die Grösse nahelegt: die Einheit sind **zwei Byte**,
Satzindex `255·Spieler + Gebäude`. Belegt an der Füllschleife C
`0x488347…0x4883B6` (`add eax, 0x1FE` = 510 = 255 × 2 je Spieler, `add eax, 2`
je Gebäude) und zweifach gegengerechnet am Besitzerwechsel und am Leser.

| Feld | Inhalt | gelesen? |
|---|---|---|
| **+0x00** | 6 = gehört mir · 4 = Besitzer 11 · 3 = Feind · 0 = leer/verbündet | ⚠ **NEIN — 0 Lesestellen in beiden EXE** |
| **+0x01** | **`imp`** — die Wichtigkeit des Gebäudes | ja, genau **eine** Stelle |

Der einzige Leser (C `0x4BBB80`) addiert `imp` in ein **11 × 11-Sektorenraster**
und in die Spielersumme. **Das Spiel benennt das Feld selbst**: die zwei
Protokollmarken dort sind »**Set imp:**« und »**Set imp cpu:**«.

Geschrieben wird an **63 Stellen**: 39 im Missions-Aufbaublock, **24 in den
Missionslogikblöcken** (M1, M3, M6, M7, M12, M13, M21, M24, M29, M30), Werte
1…9 — fast durchweg unmittelbar neben `add_target`. **Die Mission trägt ein
Ziel ein und hebt zugleich dessen Wichtigkeit.**

⭐ **Die eine Zahl, die alles zusammenhält:** In `game.007` ist genau **ein**
sec62-Byte gesetzt — Spieler 1, Gebäude 2, `imp = 9`. Und im Missionsblock 1
steht genau ein Befehl dieser Art: `mov byte [0xBC43E3], 9`, Versatz `0x203` =
Satz 257 = 255·1 + 2. **Die Datei bestätigt Adressrechnung und Deutung an einer
einzigen Stelle.**

### ⭐⭐ Der Missionswechsel: was übergeht und was nicht

`0x4CFD80` (»**saving global variables**«, am Ende »**Mission reset**«) baut aus
`Missionsnummer + 1` den Namen der nächsten Mission, **nullt v[0…299]**
(Schranke `0x12C` bei `0x4D00D1`) und kopiert dann live → Schatten:

| live | Schatten | Inhalt |
|---|---|---|
| `0x5045A0` | `0xA9C620` | die Bauteiltafel (sec46 → sec100) |
| `0x51CE20` | `0xA9A210` | die Entwürfe (sec47 → **sec101**) |
| `0x87AE00` | `0x87AC08` | die Hilfetextmerker (sec71 → sec102) |
| `0xBC5690` | `0xBC3DF8` | **die Missionsvariablen (sec72 → sec103)** |
| `0xA9C600` | `0xA9A1E0` | **das Geld** (→ sec104) |

⚠ **Damit ist auch sec101 erklärt** — es ist die Schattenkopie der Entwürfe. Der
Befund »tot, kein Leser« aus Abschnitt Y bleibt richtig für den *laufenden*
Betrieb; gelesen wird sie nur von der Gegenrichtung `0x4D0290`.

**Zwei Folgerungen mit Zahl:**

1. ⭐ **v[300…499] gehen in die nächste Mission über, v[0…299] nicht.** Und
   genau dort sitzt der gemeinsame **Vorspann** der Kampagne
   (`0x497540…0x49814D`): **36 Einmal-Tore v[346]…v[381]**, je in der Form
   `cmp word[v],0 / … show_text2 / inc word[v]` — die kampagnenweiten
   Hinweisfenster.
2. ⭐ **Das Geld wird mitgenommen** — die offene Frage aus `CAMPAIGN_RE` §4.
   ✔ Das hatten wir am 11.08. schon gelesen und gebaut; neu ist der *Weg*
   (dieselbe Schattenkopie).

### ⚠ Was das für UNS heisst

Unser `MissionScript._var` ist **je Mission neu**. Heute schadet das nicht:
unsere Regeln benutzen aus dem oberen Bereich nur v[301…305], und die jeweils
innerhalb *einer* Mission (21, 24, 25). **Aber die 36 Vorspann-Regeln stehen in
keiner unserer Missionen** — der gemeinsame Vorspann ist nie eingelesen worden.
Wer ihn einbaut, muss den Übertrag mitbauen, sonst käme jedes Hinweisfenster in
**jeder** Mission wieder. Die Regel steht jetzt im Kopf von `_var`.

### sec71 — die Vermutung stimmt, mit zwei Einschränkungen

**500 Byte, ein Byte je Textnummer, 1 = schon gezeigt** (`show_text2`
@`0x4432E0`, Merker gesetzt bei `0x443340`). Aber: **nur `show_text2` (105
Aufrufstellen) fragt die Tafel** — `show_text` (**264** Aufrufstellen) rührt sie
nicht an. Und der Nullsetzer läuft am Anfang des Missions-Aufbaublocks; die
Merker gelten also **je Mission** und werden nur für den Übertrag im Schatten
gehalten.

In `game.007` stehen die Texte 1,2,3,4,5,6,10,11,12,13,18,20 auf 1 — **12 von
16** `show_text2`-Fenstern der Mission.

### Und zwei kleinere

**sec78 = `terra_places`**, 50 × 6 B (»Cannot add more terra_places«):
`+0` belegt, `+1` Spalte, `+2` Zeile, `+3` **nie beschrieben**, `+4` u16 Menge.

**sec70 (8 B) ist TOT** — 5 Fundstellen in jeder Fassung, restlos: Speichern,
Laden, zweimal Nullen, einmal Nullen im Aufbaublock. **Keine Lesestelle.** Die
sechste tote Tafel.

**Halbtot: sec62 Feld +0x00** — 1020 Byte, von genau einer Stelle geschrieben,
von keiner gelesen. Die Basisadresse kommt in `.data`/`.rdata` **nullmal** vor,
es gibt also auch keine Zeigertafel darauf.

### Nebenbefund

`ai_tick` prüft `byte[0x538BD8 + Spieler]`; **Wert 10 schaltet den `imp`-Zweig
ab**. Das ist die bislang offene Byte-Tafel aus `0x4D1050`.

---

## AC. ⭐ Der KI-Zustand ist gelesen (21.08.2026)

Damit ist die zweite Hälfte dessen erklärt, was Abschnitt W begonnen hat: die
`AI*.CWI` liefern das **Sektornetz**, diese Abschnitte den **laufenden Zustand**
darauf.

### ⚠ Zuerst eine Berichtigung an meiner eigenen Vorgabe

Ich hatte den Agenten mitgegeben, `game.007` und `1.DM` seien »die einzigen
zwei Dateien mit allen 131 Abschnitten«. Für **131** stimmt das — aber
**vierzehn** Dateien tragen alles bis sec110: `game.007` plus `1..13.DM`
(121–131 Abschnitte). Alle Zahlen unten sind über diese **14** gezählt.

⚠ Und: **keine der beiden GAME.EXE enthält einen Schreiber** für diese
Abschnitte — nur die zwei Lader und die KI. Die Dateien stammen vom **Editor**.

### sec60 — der KI-Zustand JE EINHEIT. 8000 × 3 B

Nicht 8 × 3000, wie die Grösse nahelegt. Die Schrittweite steht am Leser
(`mov cl, byte [eax+eax*2+0xB400F0]`) und ist **3**; der Index ist die
**Einheitennummer**, dieselbe wie in sec5 (Schranke `cmp ax, 0x1F40` = 8000).
Die 3000 sind **1000 Einheiten × 3 B** je Spielerblock (`imul ax, ax, 0x3E8`).

⭐ **Die Felder benennen sich selbst** — die Debug-Überlagerung druckt
»**CPU0:**« und »**CPU1:**«.

| CPU0 | Bedeutung |
|---|---|
| 0 | frei — kommt in die Kandidatenliste |
| 1 | Marsch in Sektor CPU1 befohlen |
| 2 | im Sektor angekommen |
| 3 | greift Ziel an; bleibt nur, solange \|Δsx\| ≤ 1 und \|Δsy\| ≤ 1 |
| 5 | auf Wachposten CPU1 (0…9, Index in sec107) |
| 10 | in Angriffsgruppe CPU1 (0…3) |
| 20 | frisch produziert |

**`+2` ist tot:** 0 Fundstellen in beiden EXE, und in allen 14 Dateien
**8000/8000** Sätze mit `+2 = 0`. Es gibt auch kein »CPU2:« in der Anzeige.

⭐ **CPU1 ist bei Zustand 1/2/3 der Sektor als NIBBLE-PAAR** — low = X, high = Y.
Bewiesen doppelt: im Code (`dl = cl & 0xF` wird mit 11 multipliziert) **und in
den Daten** — über 706 belegte Sätze **706 richtig / 0 verletzt**, die
vertauschte Lesart **535 / 171**.

### sec108 — der SEKTOR-WEG der Angriffsgruppe. 32 × 62 B

8 Spieler × 4 Gruppen. `+0` laufender Wegpunkt, `+1` letzter Index (**immer
0x1D = 29**; wird 0, wenn der Weg abgelaufen ist), `+2…+61` **30 Wegpunkte als
(SektorX, SektorY)**, rückwärts gefüllt.

Er entsteht aus der **Dijkstra-Vorgängertafel** — also genau aus dem Lauf, den
Abschnitt W beschrieben hat. Der Leser rechnet `24·s − 6 … 24·s + 30` als
Kasten; sind mehr als zwei Drittel der Gruppe darin, rückt der Wegpunkt vor.

**16 belegte Sätze über 14 Dateien, und 16/16 sind lückenlose
4er-Nachbarketten** von Sektorkoordinaten, alle endend auf Platz 29.

### sec110 und sec57 — je 8 × 4 B

**sec110 = die Summe der Gebäude-Wichtigkeit** je Spieler, aufsummiert aus
sec62. **Nachgerechnet: für die Spieler 1…7 in allen 14 Dateien exakt gleich
(98/98).** Spieler 0 weicht ab — erwartbar, der Zweig läuft nur für KI-Spieler.
Benutzt als Torwächter »hat der Spieler noch eine Basis«.

**sec57 = der Zeitstempel, ab wann der Spieler wieder bauen darf.** Gegen die
Uhr (sec54); bei Erfolg `Uhr + 200`. ⭐ **Alle 18 belegten Werte über 14 Dateien
sind ≡ 2 (mod 50)** — genau der Tickplatz, an dem der Bauzweig läuft.

### ⚠ Eine Berichtigung an Abschnitt Y

Dort steht sec55 als »8 × 121 × 2 B«. **Das ist verkehrt herum.** Der Schreiber
rechnet `Spieler + 8·Zelle` — die Tafel ist **zellendur**, nicht spielerdur.
In den Daten entschieden: zellendur **0–10** Abweichungen von 968, spielerdur
**8–66**; in drei Dateien zellendur exakt 0.

### sec56 — der Satz, nachgerechnet

12 B je Sektor und Spieler: `+0` eigene Stärke, `+2` verbündete, `+4`
feindliche, `+6` Gebäude-Wichtigkeit, `+7` Kopie (»DEF:«), `+8`
»DEF_robots:«, `+0xA` Einheitenzahl. **`+9` ist tot** (0 Fundstellen, in allen
14 Dateien durchgehend 0).

⭐ **Die Formel `+8 = min(100, 100·(+7) / skala[pro_style])`** mit
`skala = {1,30,50,100,400,255,0,0}` — **0 Abweichungen von 13 552 Zellen** über
14 Dateien.

### Wer welchen Index benutzt

| Tafel | Index |
|---|---|
| sec56 | `121·Spieler + 11·sx + sy` — **spielerdur** |
| sec55 | `2·(8·(11·sx+sy) + Spieler)` — ⚠ **zellendur** |
| sec108 | `4·Spieler + Gruppe`, Wegpunkte als Sektorpaare |
| sec60 `+1` | Sektor als **Nibble-Paar** (nur Zustand 1/2/3) |
| sec68 | `4·Spieler + Gruppe` — **kein** Sektorindex |
| sec107 | 8 × 10 Wachposten in **Feldkoordinaten**, kein Sektor |

### Was offen bleibt

* **sec58** (16 B) — zwei Fundstellen im Produktionszweig, Bedeutung offen.
* **Zustand 20 → CPU1** ist undefiniert (beim Setzen nicht beschrieben).
* Der Wert **0x63** im Lagenraster sec20, nach dem Zustand 1 in seiner
  5×5-Umgebung sucht — nicht gedeutet.
* ⚠ **Die zwei EXE sind nicht in allem gleich:** sec59 hat in C fünf
  Fundstellen, in F vier. Alle *berichteten* Abschnitte stimmen 1:1 überein.
---

## AD. ⭐ Der Messlauf — drei Deutungen widerlegt, darunter eine von heute (21.08.2026)

Abschnitt Y hat rund zwanzig Abschnittsformen **am Code** abgeleitet, ohne eine
einzige Prüfdatei zu öffnen. Dieser Lauf holt das nach — über **14 Dateien**
(`game.007` plus `1…13.DM`), und er ist genau deshalb wertvoll: **drei
Deutungen halten der Messung nicht stand.**

### Die Eichung zuerst

`sec81` (die zehn Gruppen) und `sec80` (die vier Merkpunkte) haben wir
unabhängig gelesen — sie sind der Prüfstein für die Methode. Ergebnis: `sec81`
**100 %** (32 belegte Wortplätze, 32/32 zeigen auf eine lebende Einheit oder ein
lebendes Flugzeug; 1968/1968 freie exakt `0xFFFF`), `sec80` Form bestätigt (die
`0xFF`-Paare liegen exakt bei 0x15, 0x2C, 0x43, 0x5A). **Die Methode trägt.**

### Bestätigt

| Abschnitt | Ergebnis |
|---|---|
| **sec15** (8000 × 50, Weg) | 1.DM **111/111** belegte Blöcke in lebenden Plätzen, **5550/5550** Bytes im Wertebereich {0…7, 0xFF} |
| **sec48** (400 × 18) | Vorwärtszeiger **100 %**; ⭐ **der Rückzeiger gefunden: Satz `+0x0C` (u16) = der sec5-Platz der Einheit** — über *alle* 78 Versätze gesucht, `+0x40` gewinnt mit 19/20, der nächstbeste hat 3/20 |
| **sec94** (50 × 78) | 18 belegt, Position/Besitzer/Typ je 18/18 — bei Schrittweite 80 fällt das auf 3 % |
| **sec63** (8 × 50 × 3) | **100 %** der `0xFF` sitzen auf Tripel-Versatz 0; alle acht Schlangen sind ein lückenloser Präfix |
| **sec18** (6000 × 3) | **564/564** und **1409/1409** Zellen auf der Karte; Schrittweite 2 oder 4 gibt nur 85 %. Die Zellen sind zu **99,6 %** Terrainklasse 2 (Karte: 56 %) — der »brennender Wald«-Deutung sehr zuträglich |
| **sec98** (20 × 78) | `0xFF` genau bei 9 + 78k, 20 Stück, **alle Abstände exakt 78** |
| **sec13/14** | tragen denselben Wert und treffen den Füllstand exakt (608 = 608, 968 = 968) |

### ⚠ Drei Deutungen, die fallen

**1. `sec11` ist KEINE Zellenliste — es ist eine Liste von Einheitenplätzen.**
Der Kopf spricht für sich: `[1000, 3000, 3001, 3002, 1001, …]`. In `1.DM` sind
**968 von 968** Einträgen ein *lebender* sec5-Platz (Zufallserwartung **1,59 %**).
Die Koordinatendeutung scheitert. ⚠ **Und eine Warnung zur Methode:**
`row·W + col` gibt 100 % — aber nur, weil bei 254 × 100 jeder u16 durchfällt.
Das misst nichts.

**2. `sec32` ist KEINE kartenabhängige Verbindungsmatrix.** Die Form 13 × 13
steht (am Code belegt), aber die 169 Byte sind in **allen 14 Dateien
byteidentisch** — und nur zu 62 % symmetrisch. Eine Matrix der bahnfähigen
Gebäude *dieser Karte* kann das nicht sein. Es ist eine **statische
Typentafel**; was sie bedeutet, sagen die Daten nicht, weil sie überall gleich
ist.

**3. ⚠⚠ `sec101` ist NICHT TOT — und diese Berichtigung trifft mich selbst.**
In Abschnitt Y steht sie als »fünfte tote Tafel«, weil der Adressabtast keinen
Leser fand. Die Daten sagen etwas anderes: **1010 von 9200 Byte ≠ 0 in allen 14
Dateien**, und der Inhalt sind **200 Entwurfssätze zu 46 Byte** — dieselbe Form
wie sec47, 73 davon mit Namen (»Chaingunner«, »Radar Scout«, »Pioneer«).

Und es gibt sehr wohl Code: **`rep movsd` mit `ecx = 0x8FC`** (= 2300 Dwords =
exakt 9200 B) kopiert **sec47 → sec101** (@`0x4189E3`, @`0x4D0104`) und
**sec101 → sec47** (@`0x4D03E4`). ⭐ Das deckt sich mit Abschnitt AB, der
denselben Befund von der anderen Seite fand: sec101 ist die **Sicherungskopie
der ersten 200 Entwürfe** im Missionsübertrag.

**Die Lehre:** ein Adressabtast findet `mov`-Formen mit Konstante — eine
`rep movsd`-Blockkopie lädt die Adresse aber in `esi`/`edi`, oft aus einem
Register. **»Kein Leser gefunden« heisst nicht »kein Leser«**, solange nur nach
Zugriffen mit unmittelbarer Adresse gesucht wurde. Die anderen fünf toten
Tafeln sind davon nicht berührt (bei ihnen wurde roh über das ganze `.text`
gesucht), aber die Methode gehört ab jetzt mit dieser Einschränkung zitiert.

### Die vier offenen Fragen

**1. `sec89` und `sec114` sind byteidentisch** — 1200 Byte gegeneinander
gehalten, in allen 14 Dateien **0 abweichende Byte**. Der Lader liest zweimal in
denselben Puffer, und weil der Speicherer beide Male denselben Puffer schreibt,
überschreibt sec114 sec89 mit **identischem Inhalt**. **Kein Datenverlust, ein
reiner Doppeleintrag — die Datei trägt 1200 Byte umsonst.**

**2. `sec95` ist ein PREIS, keine Restzeit.** Der Beweis ist die
Reproduzierbarkeit: gruppiert man die 36 Marktposten beider Dateien nach dem
Entwurfsschlüssel, sind **6 von 12 mehrfach besetzten Gruppen über beide
Dateien hinweg aufs Byte gleich** — (4,41) → 262 viermal, (4,35) → 750,
(9,37) → 487. **Ein Countdown kann in zwei unabhängigen Speicherständen nicht
denselben Wert tragen.** Alle 36 Werte sind Vielfache von 7,5.
⚠ Nicht gelungen: die Verbindung zu sec47 — eine erschöpfende Suche über 3510
Feldpaare findet bestenfalls 2 von 34 Treffern. **Der Grundpreis steht nicht in
sec47**, jedenfalls nicht so.

**3. `sec9`/`sec10` sind kein zweites Koordinatenpaar, sondern die
FEINSTELLUNG desselben.** sec9 bleibt in [0, 36] und ist in **14 von 14**
Dateien durch 4 teilbar, sec10 in [0, 16] und durchweg gerade — unabhängig von
der Kartengrösse. Der Code sagt warum: bei Unterlauf wird mit Divisor **40**
(waagerecht) bzw. **20** (senkrecht) nach sec7/sec8 übergetragen. Also:
**sec7/sec8 = Kameraecke in Kacheln, sec9/sec10 = der Pixelrest darin**,
Scrollschritt **4 px waagerecht, 2 px senkrecht**. Nebenbei fällt die
Fenstergrösse ab: **21 × 37 Kacheln** sichtbar.

**4. `sec101`** — siehe oben.

### ⚠ Eine Werkzeugfalle, gefunden und behoben

Der Agent musste die Ladertafel gegen 87 Aufrufstellen nachprüfen, weil drei
Adressen um einen Platz verschoben schienen. Ursache: **`SECT_DESTS` beginnt
bei sec2**, richtig ist also `[i-2]` — so machen es `zz_load`, `zz_map`,
`zz_scale` und `zz_show`. **`cwm_inventory.py` benutzte `[i-1]`** und lieferte
für sec32 die Adresse von sec33. Behoben; unsere Tafel in Abschnitt Y war von
Anfang an richtig.

### ⚠ Und eine Warnung zur Messmethode selbst

Bei `sec15` liefert der naheliegende Test »passt der Weg irgendwo auf die
Karte« **100 % — aber das Zufalls-Nullmodell auch**. Er misst nichts. Was
trägt, ist die **Glattheit**: Wendungen ≤ 45° zu **84,4 % / 98,4 %** gegen 37 %
im Zufallsmodell. **Zu jeder Trefferquote gehört das Nullmodell**, sonst ist
eine 100 % wertlos.

### Was auch dieser Lauf nicht messen konnte

* **sec80, sec91, sec98 inhaltlich** — 0 von 56 bzw. 0 von 14 000 bzw. 0 von
  280 Plätzen sind in irgendeiner der 14 Dateien belegt. Die Formen stehen, die
  Inhalte sagt keine Datei.
* **sec32 inhaltlich** (überall gleich), der **Grundpreis zu sec95**, und der
  genaue Partner von **sec40** (sec39 ist mit 87,6 % der beste von sechs
  Kandidaten, aber keine exakte Belegt-Maske).
* ⭐ **NACHGETRAGEN:** dieser Vorbehalt ist eingelöst — alle fünf Punkte sind in
Abschnitt AF gegen die F-Fassung gegengelesen und **bestätigt**. Dabei fiel der
erste belegte **Verhaltensunterschied** zwischen den zwei Auslieferungen auf.

⚠ **Alle Code-Belege dieses Laufs stammen aus der C-Fassung.** F wurde nur
  für die PE-Struktur geöffnet. Nach Regel 1 ist das **kein abgeschlossener
  Befund** — die Datenmessungen tragen, die Code-Zitate gehören gegengelesen.
---

## AE. ⭐ Der Vorspann der Kampagne ist ein KONTEXTHILFE-System (21.08.2026)

Abschnitt AB hatte den Block `0x497540…0x49814D` als »36 Einmal-Tore v[346]…v[381]
für kampagnenweite Hinweisfenster« benannt. Jetzt ist er gelesen — es sind
**34 Tore**, und sie tun etwas Bestimmteres, als es klang.

### Was ein Tor ist

Jedes der 34 ist derselbe Zwölfzeiler:

```
  if (v[n] != 0)                       raus      ; schon gezeigt
  if (word[0x4FA0C8] >= 0x1F40)        raus      ; keine Einheit angewaehlt
  eax = word[0x4FA0C8]                           ; die ANGEWAEHLTE Einheit
  al  = byte[eax*78 + 0x6E26C8 + FELD]           ; eines ihrer Bauteilbytes
  if (al != WERT)                      raus
  v[n]++
  show_text2(100, 200, TEXT, 0)
```

**Also: wer eine Einheit anwählt, die ein bestimmtes Bauteil trägt, bekommt
einmal in der ganzen Kampagne den Hilfetext dazu.** Kein Skript, kein Auslöser
in der Mission — es hängt allein daran, was man anklickt.

⭐ **Die Selbstprobe:** `Text = v − 300` gilt für **32 von 34** Toren. Nur
v[346]→67 und v[347]→19 fallen heraus. Das bestätigt die Auslesung.

### Die 34 Tore

| v[] | Feld | Wert | Text |
|---:|---|---|---:|
| 348…355 | **`+0x0D` `ZBRAN` (Waffe)** | 6/7, 8, 9, 14, 15, 16, 18, 19 | 48…55 |
| 347, 356…367 | **`+0x0E` `top_spec` (Aufbauteil)** | 69, 65, 66, 68, 70, 72…79 | 19, 56…67 |
| 370, 371 | **`+0x0F` `l_engine` (Antrieb)** | 171, 19/172 | 70, 71 |
| 368, 369 | **`+0x10`** | 83, 84 | 68, 69 |
| 346, 372…378, 381 | *anderes* | 55, 2, 5, 18, … | 67, 72…78, 81 |

⚠ **NEUN der 34 Tore sind nicht auswertbar** — berichtigt am 21.08.2026 beim
Bauen: hier stand »fünf«, und das zählte nur die ohne *Wert*. Ohne **Feld**
sind es v[346], 372…378 und 381, also neun; vier davon (346→55, 373→2, 376→5,
378→18) tragen sogar einen Wert, aber keinen Satzversatz. Sie prüfen etwas
anderes als ein Bauteilbyte der angewählten Einheit, und was, ist offen.
**Die übrigen 25 sind gebaut** (`--hinweis-check`).

### Warum es bei uns nie erscheinen konnte

Zwei Gründe, und beide sind belegt:

1. **Die 34 Regeln stehen in keiner unserer Missionen.** Der Vorspann ist ein
   *gemeinsamer* Block vor den 33 Missionsblöcken; unser Regelleser nimmt sich
   je Mission ihren eigenen Block und hat den Vorspann nie gesehen.
2. **Und selbst mit den Regeln käme jedes Fenster in JEDER Mission wieder** —
   denn v[346…381] liegen über 300, und genau die gehen im Original **in die
   nächste Mission über** (Abschnitt AB). Unser `MissionScript._var` ist je
   Mission neu.

### ⭐ Gebaut am 21.08.2026

`Campaign.CampaignHints` lädt die Tore, `MapEntityLayer.KontexthilfePruefen`
hängt an `SetPrimary` — der einen Stelle, die die Hauptauswahl setzt, also dem
Gegenstück zu `word[0x4FA0C8]`. Gezeigt wird mit `HelpWindow.Show(this, id,
100, 200)`, den vier Zahlen des Originals.

**Der Übertrag ist UNSER Weg:** statt v[300…499] zu verschleppen, führen wir
die gefeuerten Tore als eigene kampagnenweite Menge, und die geht in den
Spielstand. Gleichwertig, solange niemand sonst diese Variablen liest — und im
Original tut das niemand: die 34 Tore sind ihre einzigen Leser und Schreiber.

⚠ **Nur in der Kampagne**, und nur für **eigene** Einheiten. Beides folgt aus
dem Original: der Block liegt vor den Missionsblöcken, und `0x4FA0C8` trägt nur,
was der Spieler angewählt hat.

Gemessen (`--hinweis-check`): erste Anwahl **ein** Fenster, zweite Anwahl
derselben Einheit **schweigt**, ein Bauteil ohne Tor löst **nichts** aus, und
im Gefecht bleibt alles still.

### Was ein Nachbau bräuchte (erledigt, bleibt als Beleg)

* Die 34 Regeln als Daten (Feld, Wert, Textnummer) — die Auslesung steht oben,
  fünf Bedingungen fehlen noch.
* Den Übertrag von v[300…499] über den Missionswechsel, **oder** einen
  gleichwertigen kampagnenweiten Merker. ⚠ Das Original hält beides getrennt:
  die Variablen (sec72) *und* eine eigene Tafel »welcher Hilfetext wurde schon
  gezeigt« (sec71, 500 Byte, 1 Byte je Textnummer).
* Den Anschluss an unser Hilfefenster — das haben wir, samt der 132 Sachbilder
  und dem gelesenen Weg vom Text zum Bild.

⭐ **Die Felder haben wir alle schon**, und unsere Benennung ist dabei
ausdrücklich richtig: `Entity.Weapon` hält den **Aufsatz `+0x0C` (`VRSEK`)**,
nicht die Waffe — das steht so am Feld, und `TurretOf` bildet die Beziehung
`ZBRAN = VRSEK − 20` bereits ab. Wer die Tore einbaut, muss also `Weapon − 20`
gegen die `ZBRAN`-Werte halten, nicht `Weapon` selbst.

### Nebenbefund

`word[0x4FA0C8]` ist die **angewählte Einheit**, Schranke 8000. Dieselbe Zelle
benutzt die Entwickler-Einblendung aus Abschnitt U, um die 37 Satzfelder
anzuzeigen — sie ist also der allgemeine »worauf schaut der Spieler«-Zeiger.
---

## AF. ⭐⭐ Die zwei Auslieferungen sind NICHT dasselbe Programm (21.08.2026)

Das Gegenlesen der C-Befunde in der F-Fassung hat alle fünf bestätigt — und
dabei etwas gefunden, das eine **Grundannahme des Projekts berührt**.

### Der Unterschied

Beide Fassungen haben in der Transport-KI denselben Zweig »kein Ziel gefunden«.
Aber:

```
C (22.01.1998)                          F (16.09.1997)
  cmp al, 0xFF ; jne …                    cmp al, 0xFF ; jne …
  push "AI: transport - no target found"  push "AI: transport - no target found"
  call <Protokoll>                        call <Protokoll>
  mov byte[ebx + 0xB46950], 1   ← nur C   pop esi
  ret                                     ret
```

**Selbst nachgezählt und nachgelesen, beide Fassungen nebeneinander.** Die
Stelle ist C `0x4BB7FC`; in F folgt auf denselben Protokollaufruf unmittelbar
`pop esi`.

### Was es bewirkt

`sec59[Spieler]` (8 B, C `0xB46950`) ist die **Sperre für den
Produktionsschritt** der KI: `if (sec59[p] == 0) → "AI: no production - no
transport"` und Rückkehr. Geschrieben wird sie an vier Stellen, zwei davon auf 0.

Findet die Transport-KI in einem Durchlauf **kein Ziel**, dann gilt:

* **F (1997):** sec59 bleibt 0 → der nächste Schritt bricht ab. **Die Produktion
  dieses Spielers steht in diesem Durchgang still.**
* **C (1998):** sec59 wird trotzdem auf 1 gesetzt → **die Produktion läuft
  weiter.**

Das sieht nach einer **bewussten Nachbesserung** aus — eine Verklemmung der
KI-Produktion, die in der späteren Auslieferung beseitigt wurde. Dafür spricht
auch ein zweiter, kleinerer Unterschied an derselben Stelle: F löscht `ebx` vor
der Zielsuche, C übergibt es mit undefinierten oberen Bytes.

### ⚠⚠ Was das für unsere Regel 1 heisst

Wir schreiben überall: **»Nur was BEIDE GAME.EXE liefern, gilt als gelesen.«**
Die Regel bleibt richtig — aber ihre stillschweigende Begründung war, die zwei
Bauten seien **dasselbe Programm** in zwei Übersetzungen. **Das stimmt nicht
mehr.**

Ab jetzt gilt die Regel in dieser Fassung: *eine Abweichung zwischen C und F ist
zuerst ein Verdacht auf einen eigenen Lesefehler — aber sie kann auch ein
**Befund** sein.* Wer eine findet, muss beide Stellen nebeneinander lesen und
entscheiden, welches von beidem vorliegt. Es genügt nicht mehr, sie als
Lesefehler abzutun.

⭐ **Und für den Nachbau ist die Sache eindeutig:** unser Bestand ist die
**C-Fassung**, und C ist die **spätere** (22.01.1998 gegen 16.09.1997). Wir
folgen also der nachgebesserten Fassung — das ist die richtige Wahl, und sie
ist jetzt begründet statt zufällig.

### Was das Gegenlesen sonst bestätigt hat

| Befund | Urteil | F-Adresse |
|---|---|---|
| sec48 `+0x0C` = der sec5-Platz | **bestätigt** | Schreiber F `0x43536B`, Leser F `0x4353E5` |
| sec9/sec10 = Feinstellung (Divisor 40 / 20) | **bestätigt** | F `0x4B48AF`, `0x4B48F0` |
| … der Abzug im Zeichencode (~30 Stellen) | **bestätigt** | F `0x42BDD0` |
| … Fenstergrösse 21 × 37 Kacheln | **bestätigt** | F `0x4B9309`/`0x4B9318` |
| sec101 = Sicherungskopie (`rep movsd`, `ecx = 0x8FC`) | **bestätigt**, dreimal in beiden | F `0x418823`, `0x4CFCB4`, `0x4CFF94` |
| sec89/sec114 in denselben Puffer, auch beim SPEICHERN | **bestätigt** | F `0x41CBEB`/`0x41CDD7`, `0x41DC6F`/`0x41DE5B` |
| sec11 = Einheitenplätze | **bestätigt** | F `0x4D2EAA`, Verbraucher F `0x4D33DF` |

⭐ **Zwei Verschärfungen gegenüber dem C-Lauf:**

1. **sec48 heisst `rob_trans`, und der Rückzeiger ist nicht erschlossen,
   sondern vom Spiel selbst als Prüfbedingung ausgeschrieben** — der Leser
   vergleicht `sec48[Einheit.+0x40].+0x0C` gegen die Einheitennummer und meldet
   sonst »Transport check : wrong index of robot in 'rob_trans'«. Das ist
   stärker als die Korrelation 19/20, mit der er gefunden wurde.
2. **sec101 sichert nur EIN ACHTEL von sec47.** sec47 ist 73 600 B, kopiert
   werden 9 200 — genau `1600 / 8 = 200` Entwürfe, also **der Block eines
   Spielers**. Die Sicherung deckt nicht die ganze Entwurfstafel ab.

### Und eine Berichtigung an der Fehlersuche selbst

Der vorige Lauf hatte gemeldet, C `0x4BB861` sei die C-eigene Stelle. **Die
Zählung stimmte (5 gegen 4), die Zuordnung nicht:** `0x4BB861` hat sehr wohl
eine F-Entsprechung (`0x4BB32F`) — sie war nur über die Adresse nicht zu finden,
weil C den Spielerwert über eine Stapelzelle führt und F in `ebx`. Gefunden
wurde die richtige Stelle über den **Kontrollfluss**, nicht über die Adresse.

⚠ **Die Lehre:** eine unterschiedliche *Anzahl* von Fundstellen sagt noch nicht,
**welche** fehlt. Wer die Differenz über Adressen zuordnet, greift daneben,
sobald die Übersetzer verschieden registriert haben.

### Was auch dieser Lauf nicht geprüft hat

Er hat **keine Spielstandsdaten geöffnet** — alles oben ist reine EXE-Lesung.
Die Datenaussage zu sec11 (968/968 lebende Plätze) stammt aus dem Messlauf
(Abschnitt AD) und steht unabhängig davon. Und sec58/sec59 sind nur bis zur
Funktionsebene gedeutet: was in sec63 genau steht, ist offen.
---

## AG. Der Preis, die Bahnandock-Tafel und der Waldbrand (21.08.2026)

### ⭐ Der PREIS — gefunden, und auf 434 Marktposten aufs Byte nachgerechnet

Der Vorgänger hatte erschöpfend gesucht und nichts gefunden. Der Grund: **die
Baustoffkosten stehen in sec47 als BYTES an `+26/+27/+28`, nicht als u16** — ein
Abtast über u16-Felder läuft daran vorbei.

Die Kette (C `0x4C1194` → `0x451010`, F `0x4C0C55` → `0x44FCC0`):

```
wert(posten):
  D = m[0x43]                        ; die Entwurfsnummer im Marktsatz
  s = d[26] + d[27] + d[28]          ; die DREI Baustoffkosten des Entwurfs
  v = (30 · s · m[0x08]) / d[30]     ; Zustand / Höchstzustand
  p = TAFEL[erfahrungsband(m[0x28])] ; Faktor · 100
  return (v · p) / 100
preis = (wert · 25) / 10             ; ×2,5
```

Alle Teilungen sind `idiv`, also zur Null hin abgeschnitten.

**Die Erfahrungstafel** (`0x4FA0E0` Bänder, `0x4FA0F0` Faktoren; ausgewertet in
`0x43AAC0`, Fehlertext »Wrong experience level«):

| Erfahrung | 0–5 | 6–20 | 21–40 | 41–75 | 76–110 | 111–170 | 171–254 | 255 |
|---|---|---|---|---|---|---|---|---|
| Faktor ×100 | 10 | 20 | 50 | 100 | 200 | 400 | 700 | 1000 |

Bei einem frischen Posten ist `m[8] = d[30]` (beim Einstellen gesetzt), das
Verhältnis also 1, und mit Erfahrungsband 0 bleibt **Preis = 7,5 · s** — genau
die beobachteten »Vielfachen von 7,5«.

**Gemessen: 27 Dateien, 434 Posten, jeder einzelne aufs Byte getroffen** — mit
einer Abweichung, und die löst sich am Datum:

| Dateien | Schlussfaktor | Treffer | Dateidatum |
|---|---|---|---|
| `game.007`, `1.DM` | **×2,5** | 34/34 | 04.08.1997 |
| `2…13.DM` | **×1,5** | 400/400 | 08.07.1997 |

⭐ **Der Aufschlag wurde zwischen dem 8. Juli und dem 4. August 1997 von 1,5 auf
2,5 angehoben.** Beide ausgelieferten EXE tragen 2,5 — **für den Nachbau gilt
2,5**. ⚠ Die ×1,5-Fassung steht in keiner der beiden EXE; der Beleg ist rein
chronologisch.

### ⭐ sec32 ist die BAHNANDOCK-Tafel — und echte Nutzdaten

13 Zeilen à 13 Byte, **Zeile = Gebäudetyp**: `[0]` = Zahl der Andockpunkte
(0…4), dann je Punkt `(dx, dy, Seite)`.

| Zeile | Typ | Andockpunkte |
|---|---|---|
| 1 | Basis | 1 × (5,1,1) |
| 2,3,4 | die drei Fabriken | 1 × (6,2,1) |
| 6 | **Bahnstation** | 3 × (0,1,0) (0,2,0) (3,1,1) |
| 9,10 | Flughafen, Mine | 1 × (6,2,1) |
| 12 | **Feldbahnhof** | 4 × (0,1,0) (0,2,0) (2,1,1) (2,2,1) |
| 0,5,7,8,11 | Depot, Generator, Radar, Seedock | **0 → keine Bahn** |

Die Seite entscheidet über Richtungscode und Lage (`x−2` / Code 2 / Lage 1
gegen `x+1` / Code 3 / Lage 0, @`0x4AFF03`). Zeilenbyte 0 = 0 heisst »dieser
Typ kann keine Bahn« (`rail_register` @`0x4B00A0`).

⭐ **Und die Kernfrage ist beantwortet: die Tafel wird beim Laden NICHT neu
aufgebaut.** `0x4AFB20` ist kein Aufbau, sondern ein Konstanten-Initialisierer —
und **toter Code**: ein Thunk existiert, aber **nichts im ganzen Bild ruft ihn
auf**, in beiden Fassungen. Härter noch: die Zeilen **9 und 10** (Flughafen,
Mine) stehen nicht einmal in diesem toten Initialisierer, und **kein einziger
Befehl beschreibt sie**. Der Puffer liegt im `.bss`, ist also ohne Datei null.

→ **Ohne die 169 Byte aus dem Spielstand funktioniert der Bahnbau für Flughafen
und Mine überhaupt nicht.** Die »Verbindungsmatrix«-Deutung ist endgültig
erledigt.

### sec18 — (X, Y, Zustand), und es IST der Waldbrand

Schrittweite 3 am Code belegt (`add esi,3` bis `0xC03A31` ab `0xBFF3E1` = exakt
6000 Sätze). `+0` Spalte, `+1` Zeile, `+2` Zustand: **0 = frei, 1 = steht,
2…255 = brennt**.

⭐ **Die Gegenprobe MIT Nullmodell** — genau das, woran der vorige Lauf fast
gescheitert wäre: die Weltkarte trägt für Waldfelder `50000 + Platznummer`.

| Zustand | Treffer | |
|---|---|---|
| brennt (2…255) | **108/108 = 100 %** | |
| steht (1) | 30 630/31 635 = 96,8 % | |
| **Nullmodell (x/y vertauscht)** | **0,42 %** | ← der Vergleich, der es trägt |

**Die Brandmechanik, vollständig:**

* Takt nur bei `Takt % 4 == 0`; Zustand > 1 → +1; bei **255** ausgebrannt.
* `zapal` setzt `rand()%150 + 2` → **Branddauer 416…1012 Takte**.
* **Anzünden aus Schaden**, fünf Bänder (`0x40D638…0x40D727`): **≥ 70** → Wald
  wird *ohne* Feuer gelöscht · **46…69** → immer Feuer · **23…45** → mit ¼ ·
  **13…22** → mit ⅛ · **≤ 12** → nichts. Sonderfall: Einheitenart `+0x0D == 12`
  setzt den Wert fest auf 60.
* **Übergreifen** (`0x4CA7E0`, aus dem Brandtakt): **ein** zufälliger der acht
  Nachbarn, Wahrscheinlichkeit
  `1 / (2·(5·((9 − Windstärke) · Winkelabweichung) + 50))`.
* **Was danach steht:** `rand()%20` — 19/20 »sjizdnej« (befahrbar), 1/20
  »nesjizdnej« (dauerhaft blockiert).

⭐ **Das bestätigt unseren Nachbau von aussen:** `rand()%150 + 2`, jeder vierte
Schritt, Schluss bei 255 und die 19-zu-1-Regel stehen bei uns schon genau so.

### ⚠ Zwei Lücken bei uns, jetzt benennbar

1. **Wir zünden Wald bei jedem Treffer an.** Das Original hat die **fünf
   Schadensbänder** oben — insbesondere löscht starker Schaden (≥ 70) den Wald
   *ohne* Feuer, und schwacher (≤ 12) tut gar nichts.
2. ~~**Das Übergreifen fehlt ganz.**~~ — ⭐ **GEBAUT am 21.08.2026**
   (`--brand-check`: mit dem Wind 234, dagegen 63; Gegenprobe ohne Wind flach).
   ⚠ Dabei mussten **Brand und Wind aus dem Bildlauf in den Simulationstakt**
   umziehen — der Brand hing im Zeichenweg und wäre kopflos nie gelaufen. Den
   Wind hat ein Kommentar gerettet, den wir selbst an `TickWind` geschrieben
   hatten: »Wer das Feuer an den Wind hängt, MUSS den Takt vorher auf die
   Simulation umstellen.«

### Was offen bleibt

* Warum `2…13.DM` mit ×1,5 gerechnet sind — der Faktor steht in keiner EXE.
* Wer die sec32-Zeilen 9 und 10 je geschrieben hat: **kein Code in C oder F tut
  es**, es kann nur der Editor gewesen sein.
* `0xFFFD` in der Weltkarte (1070 abgebrannte Felder tragen es statt `0xFFFE`),
  und 32 der 108 brennenden Felder tragen noch eine lebende Waldkachel.


---

## AE-2. ⭐⭐ Der Vorspann ist VOLLSTÄNDIG gelesen — und ein Drittel unseres Baus war falsch (21.08.2026)

Abschnitt AE hatte 25 der 34 Tore als »Bauteil der angewählten Einheit« gelesen
und die übrigen neun als »ohne Bedingung« abgelegt. Beides war zu kurz gegriffen.
Der Block ist jetzt **Befehl für Befehl** gelesen, in beiden Auslieferungen:
C `0x497540…0x49814D`, **F `0x496E50…0x497A5E`**. Strukturvergleich **72 Zeilen,
0 Unterschiede** — dieser Block ist in C und F derselbe.

Versätze F→C an dieser Stelle: `.bss` +0xFA0, `.data` **+0xFF8** für `0x4F90D0` /
`0x4F928C`, **+0xF98** für `0x538998` / `0x53899C`.

### ⭐ Die Missionsschranke ist eine Zahl, keine Auslegung

`cmp word[C 0x539934], 0x32; jge Ende` @C `0x497643` — der ganze Block läuft nur
bei **Missionsnummer < 50**. `word[0x539934]` ist belegt als die Missionsnummer:
es ist genau die Variable, aus der der Missionsverteiler unmittelbar hinter dem
Block liest (`movsx eax,word[…]; cmp eax,0x22`).

### Der Block prüft VIER Dinge

| Art | Tore | woran |
|---|---:|---|
| `einheit_feld` | 26 | ein Satzfeld der ANGEWÄHLTEN Einheit |
| `gebaeude_vorhanden` | 4 | `zaehle_gebaeude(typ, 0) != 0`, dazu eine Missionsschranke |
| `fenster_geoeffnet` | 3 | `byte[C 0x539930]` == Fensterart, danach auf 0 zurück |
| `flughafen_platz_belegt` | 1 | ein Flugzeug auf **Stellplatz 0** eines eigenen Flughafens |

Neu belegte Sockel: `byte[C 0x539930 / F 0x538998]` = **Ereignisbyte**, die Art
des zuletzt geöffneten Fensters (Schreiber C `0x441270`: `al = byte[44324·Fenster
+ 0x8B9038]`, ausser Art 3, der Karte). `C 0x401893 → 0x4CFA70` =
`zaehle_gebaeude(typ, besitzer)` über die Gebäudetafel `0xC06914`, Schrittweite
76, 255 Sätze.

### Die neun

| v[] | C | was wirklich geprüft wird | Text |
|---:|---|---|---:|
| 346 | `0x497D57` | `+0x43 == 55` — **rob_prod**, der Entwurfsplatz | 67 |
| 372 | `0x497F02` | Mission > 16 **und** Typ 6 **oder** 12 → Bahnstation/Feldbahnhof | 72 |
| 373 | `0x497F52` | Ereignisbyte == 2 → Fenster »Bahnhof«; setzt es danach auf 0 | 73 |
| 374 | `0x497F86` | Mission > 12 **und** Typ 7 → Generator | 74 |
| 375 | `0x497FC5` | Mission > 16 **und** Typ 9 → Flughafen | 75 |
| 376 | `0x498004` | Ereignisbyte == 5 → Fenster »Hangar« | 76 |
| 377 | `0x498038` | Mission > 12 **und** Typ 10 → Mine | 77 |
| 378 | `0x498077` | Ereignisbyte == 18 → Fenster »Terranium-Mine« | 78 |
| 381 | `0x4980AB` | v[375] muss gefeuert haben, dann: eigener Flughafen mit belegtem Platz 0 | 81 |

⭐ **Die Gegenprobe an `HELPG.TXT` trägt jede einzelne Deutung — und sie war nicht
Teil der Ableitung:** #72 »**Bahnstationen** bilden Kreuzungen von Bahnstrecken…«
↔ Typ 6/12 · #74 »**Generatoren** fügen Ihrer Wirtschaft weitere Energie hinzu…«
↔ Typ 7 · #75 »Im **Flughafen** können Sie Flugzeuge bauen…« ↔ Typ 9 · #76 »Im
**Hangar** werden Flugzeuge automatisch repariert…« ↔ Fensterart 5 · #78 »Im
**Mineninfofenster** finden sich Informationen über Lagerhaltung und Ausstoß…«
↔ Fensterart 18.

Die Form der drei neuen Arten, je einmal:

```
; v[374] — GEBAEUDE  (C 0x497F86)
  cmp word[0xBC597C], 0        ; v[374], schon gezeigt?
  cmp word[0x539934], 0x0C     ; Missionsnummer, > 12 verlangt
  push 0 ; push 7 ; call 0x401893   ; zaehle_gebaeude(typ=7, besitzer=0)
  test ax, ax ; je raus
  inc word[0xBC597C] ; show_text2(100, 200, 74, 0)

; v[378] — FENSTER  (C 0x498077)
  mov al, byte[0x539930]       ; Art des zuletzt geoeffneten Fensters
  cmp al, 0x12                 ; 18 = Terranium-Mine
  show_text2(70, 150, 78, 0)   ; ANDERE Koordinaten
  mov byte[0x539930], 0        ; Ereignis verbraucht

; v[381] — FLUGHAFEN BELEGT  (C 0x4980AB)
  cmp word[0xBC597E], 0        ; v[375] MUSS schon gefeuert haben
  schleife (cl = 0..254):
     ebx = 76*cl
     byte[ebx + 0xC06914] == 9              ; typ 9 = Flughafen
     byte[ebx + 0xC06915] == byte[0x4FA284] ; eigener
     b = byte[ebx + 0xC06929]               ; +0x15 cis_typ
     byte[52*b + 0x879443] != 0xFF          ; STELLPLATZ 0 belegt
```

`0x879443` ist **sec27, die Flughafentafel** (50 × 52, Sockel `0x879438`);
`+0x0B` ist der **erste Stellplatz**, `0xFF` = frei. ⚠ Gelesen wird **nur Platz
0**, nicht »irgendein Platz«.

### ⚠⚠ Drei Fehler in den 25 schon gebauten Toren

| Betrifft | stand da | ist |
|---|---|---|
| v[347], v[356…369] — **15 Tore** | nur `top_spec == Wert` | zusätzlich **`ZBRAN == 0`** davor (`test cl,cl; jne raus`) — **nur unbewaffnete Einheiten** |
| **v[371]** | `feld 15, werte [19, 172]` | **`+0x0D != 19`** (Ausschluss) **und** `+0x0F == 172` — kein Oder |
| Koordinaten | überall 100/200 | **v[373], 376, 378, 381** zeigen bei **70/150** |

⚠ **Die Blockreihenfolge ist nicht die Variablenreihenfolge:** v[347] steht
zwischen v[358] und v[359], v[346] zwischen v[367] und v[368]. Beide sind
nachträglich eingeschoben — und genau sie sind die zwei Ausreisser der Probe
`Text = v − 300`.

### ⭐ Beide Ausreisser sind aufgelöst

**v[346] → Text 67:** `Text = v − 300` scheitert nicht an der Auslesung, sondern
daran, dass **eine Textnummer doppelt vergeben ist**. v[367] zeigt denselben Text
67 über `top_spec == 79` (Komponentenzeile »Zielfokus«). Es gibt zwei Wege zum
Fokussierer: die fertige Einheit »Target« (Entwurfsplatz 55, ab Zustand 30
freigeschaltet) und die Ausrüstung »Zielfokus« an einer selbst entworfenen.
HELPG #67: »@**Fokussierer** können in Feindgebiet eingeschleust und von
Mittelstreckenraketen direkt angezielt werden.«

**v[347] → Text 19:** richtig ausgelesen (`ZBRAN == 0` und `top_spec == 69` =
»Fallenräumer«, `push 0x13`). ⚠ **HELPG #019 ist eine LEERE Marke** — die Zeile
`#019` steht da, der Rumpf fehlt. Das Tor zeigt im Original ein leeres Fenster.

⚠ **HELPG #081 gibt es gar nicht** — die erste Textgruppe endet bei #080.
Gemessen nur an `F:\Akte Europa\HELPG.TXT`; die C-Auslieferung hier hat keine
Datendateien, also ist das **ein halber Befund**.

### ⚠ Ein Widerspruch im ORIGINAL, nicht bei uns

v[372]/374/375/377 zählen Gebäude von **Besitzer 0** (feste Zahl), v[381]
vergleicht gegen **`byte[0x4FA284]`** (den Betrachter). In der Kampagne ist beides
dasselbe; im Gefecht wäre es das nicht — dort läuft der Block ohnehin nicht.

### Was offen bleibt

* **Warum v[377] (Mine, Typ 10) den FABRIK-Text #77 zeigt.** Der Code ist
  eindeutig (`push 0xa`), die Absicht nicht.
* Ob `rob_prod == 55` in einer Kampagnenkarte wirklich »Target« heisst. Der
  Entwurfsplatz ist bewiesen; der *Name* stammt aus sec47 von `3.DM`. Eine
  Kampagnen-`.CWM` endet nach Sektion 38 und trägt sec47 gar nicht.
* Die drei **Fenster** (Bahnhof, Hangar, Terranium-Mine) gibt es im Nachbau
  nicht; ihre Tore sind übergangen und **gezählt**.

---

## AE-3. ⭐⭐ `+0x43` ist der ENTWURFSPLATZ — und unsere Untermissionen sind richtig gebaut (21.08.2026)

Unser Nachbau las `+0x43` als **Missionsmarke**, die Neulesung als **`rob_prod`**.
Beides kann nicht stimmen. Entschieden am Code, in beiden EXE:

⭐ **Es ist EIN Byte mit EINER Bedeutung, und das Programm benennt sie selbst.**
`create_unit` (C `0x4B34E0`, F `0x4B2E10`) trägt in beiden Auslieferungen die
Zeichenkette **»WRONG ROB_PROD in PLACE!!!!«** (C `0x538568`). Sie wird gemeldet,
wenn `byte[46·(arg1 + 200·Spieler) + 0x51CE38]` null ist — wenn `arg1` also auf
einen **leeren Entwurfsplatz** zeigt. Elf Befehle später:

```
004B3556  lea ecx, [ebp + eax*8]            ; arg1 + 200*Spieler
004B3562  mov al, byte[ecx+esi + 0x51CE38]  ; = sec47-Zeile, Feld +0x18
...
004B366E  mov byte[esi + 0x6E270B], bl      ; DASSELBE arg1  ->  +0x43
```

**`create_unit` schreibt in `+0x43` genau die Zahl, mit der es vorher die
Entwurfszeile aufgeschlagen hat.** F ist Zeile für Zeile dasselbe.

⭐ **Und die gute Nachricht:** `find_unit` (C `0x4D0F20`, F `0x4D0AD0`) vergleicht
**denselben Versatz** (`cmp byte[esi + 0x6E270B], cl`; `0x6E270B − 0x6E26C8 =
0x43`). Unser `find_unit_with_mark` ist damit **richtig gebaut**, und die darauf
gestützten Untermissionen stimmen. Falsch war nur der **Name** im Kommentar — eine
Berichtigung vom 10.08.2026 war nie in den C#-Kommentar durchgereicht worden.

**26 Stellen in C, 26 in F**, eins zu eins dieselben Funktionen: drei Schreiber,
dreiundzwanzig Leser (darunter die Fenster Basis, Patrol-Boot, Depot und
»Mitnehmbare Einheiten«, die Produktion, `change_owner` und das Tor v[346]).
**Kein Schreiber im Kartenladeweg** — und das ist kein Loch: eine Karteneinheit
kommt als ganzer 78-Byte-Satz herein, `+0x43` steht schon in der Datei.

⚠ **NEU, und für den Nachbau wichtig: die Entwurfstafel hängt an der GATTUNG.**

| Gattung | Tafel | Schrittweite | Plätze je Spieler | Index | belegt @ |
|---|---|---:|---:|---|---|
| Land | sec47 `0x51CE20` | 46 | **200** | `n + 200·Spieler` | `0x4B3556`, `0x4B18BB` |
| Schiff | `0x52EDA0` | 42 | **10** | `n + 10·Spieler` | `0x4B2BB2` |

Wer `Mark` gattungsblind gegen sec47 auflöst, liest bei Schiffen die falsche
Zeile. Für v[346] folgenlos: 55 ist als Schiffsplatz nicht darstellbar.

### ⭐ `VRSEK = ZBRAN + 20` — für die 19 Waffen richtig, als REGEL falsch

Es ist kein Rechenweg, sondern ein **Nachschlag** in Spalte `+0x0D` der
Bauteiltafel `0x5045A0` (@`0x4B21B6`):

| Bauteilzeilen | `+0x0D` | Differenz |
|---|---|---|
| **1…19** (Kanone … M-Bombe, die echten Waffen) | 21…39 | **genau +20**, 19 von 19 |
| **65…79** (Teleporter … Zielfokus, die Ausrüstungen) | 40…54, **nicht der Reihe nach** | −21 bis −27 |
| 80…88 (Schild, Kamikaze, Spiegel …) | 0 | — |

Unsere Tafel `EquipMountOrder` ist **exakt** diese Spalte — sie war richtig, sie
ist jetzt auch **hergeleitet** statt abgeschrieben.

⭐⭐ **Und der Punkt, der unseren eigenen Fehler am Code bestätigt: `ZBRAN` lässt
sich aus `VRSEK` grundsätzlich nicht zurückrechnen.**

```
FAHRZEUG (Antrieb >= 150)              INFANTERIE (Antrieb < 150)
  +0x0B SPODEK = Entwurf +0x2C           +0x0B SPODEK = 2*Waffe - 124
  +0x0C VRSEK  = Entwurf +0x2D           +0x0C VRSEK  = 0        <-- fest null
  Waffe >= 50 ? +0x0D=0, +0x0E=Waffe     Waffe > 192 ? +0x0D=0, +0x0E=Waffe
              : +0x0D=Waffe, +0x0E=0                 : +0x0D=Waffe, +0x0E=0
```

(C `0x4B36E2…0x4B373C`, F `0x4B3012…0x4B306C`, Befehl für Befehl gleich.)
**Es gibt keine Umkehrung, die 0 liefert** — und genau auf 0 prüfen fünfzehn Tore.
Unsere Kontexthilfe rechnete `Weapon − 20` und machte damit die häufigste
Bedingung des ganzen Blocks unerfüllbar. Sie liest jetzt die Rohbytes.

### ⚠ Ein Nebenbefund, nicht verfolgt

`MapEntityLayer.cs` begründete den Schiffs-Aufsatz mit »der Kampftakt @0x40DE1E
prüft `test al,al` auf **+0x0C**«. Am Code steht dort **`+0x0D`**:

```
0040DDF0  mov al, byte[edi + 0x6E26D5]   ; +0x0D ZBRAN
0040DDFE  mov cl, byte[edi + 0x6E26D4]   ; +0x0C -- nur gegen 0x26 (Flak) geprueft
0040DE1E  test al, al                     ; al ist +0x0D, NICHT +0x0C
```

**Der Unbewaffnet-Schalter ist ZBRAN, nicht VRSEK** — und das passt zusammen:
Infanterie hat `+0x0C == 0` und kämpft trotzdem. Der Schluss der Notiz (Schiffe
brauchen einen Aufsatz) kann aus anderem Grund richtig bleiben; die zitierte
Begründung zeigt aufs falsche Feld.

---

## AH. ⭐⭐ sec58 gelöst, sec32 hart belegt — und ein Werkzeugfehler, der unsere Negativbefunde betrifft (21.08.2026)

### ⚠⚠ ZUERST DIE WARNUNG, denn sie betrifft alles davor

Beide EXE tragen eine vollständige **Relokationstafel** — C 31 848, F 31 776
Einträge, davon **31 650 / 31 578 im `.text`**. Jede absolute Adresse, die
irgendwo im Bild als Konstante steht, ist damit **aufzählbar statt suchbar**.
Das ersetzt den Adressabtast durch eine Vollerhebung.

⚠⚠ **Und dabei ist herausgekommen: ein naiver Linearabtast des `.text` mit
capstone bricht STILL AB** — C nach 78 845 Befehlen bei `0x42BC1D`, F schon nach
16 622 bei `0x409FAE`. Das sind **18 % bzw. 4 %** des Abschnitts (⚠ hier stand die Zuordnung vertauscht; am 21.08.2026 nachgerechnet). Mit
Resynchronisation (bei Fehlschlag ein Byte weiter) sind es **443 471 / 442 952**
Befehle.

**Wer so gesucht und nichts gefunden hat, hat nichts gesucht.** Jeder frühere
»kein Treffer«-Befund, der auf einem Linearabtast beruht, steht auf Sand und
gehört nachgelaufen. Das ist die zweite Falle dieser Art nach `sec101` (dort war
es ein `rep movsd`, das die Adresse ins Register lud).

### ⭐ sec58 (16 B, C `0xB38D40` / F `0xB37DA0`) ist der UMLAUFZEIGER der KI-Bauschlange

**8 Spieler × 2 Byte.** Byte 0 = Lesezeiger 0…49 in die 50 Einträge lange
Bauschlange dieses Spielers (**sec63**, 8 × 50 × 3 B, C `0xBC51D0`). Byte 1 wird
von keinem Befehl angefasst.

Sechs Fundstellen, in beiden EXE gleich (44/44 Befehle im Fenster ±16 B stimmen
in Versatz, Mnemonik und Operandenform überein): Speicherer C `0x41D7D0`, Lader
C `0x41E84A`, Neustart C `0x41EFC5`/`0x41EFCA`, **Lesen** C `0x4BB9DE`
(`mov al,[ebx*2 + sec58]`), **Schreiben** C `0x4BBA79`.

**Die Funktion, vom Spiel selbst benannt** — `ai_production`, C `0x4BB9A0` /
F `0x4BB460`, in F befehlsgleich:

```
ai_production(byte spieler):
    log("AI: production ", spieler)
    wenn sec59[spieler] == 0:  log("AI: no production - no transport");  ret
    z = sec58[2*spieler]
    wenn z == 0xFF:            log("AI: no production - nothing to do "); ret
    satz = &sec63[(50*spieler + z)*3]
    wenn satz[0] == 0:  0x4BB1E0(satz[1], ...)   // "AI: production in base "
    wenn satz[0] == 1:  0x4BB3D0(satz[1], ...)   // "Build in airp"
    z = z + 1
    wenn z > 49 ODER sec63[(50*spieler+z)*3] == 0xFF:  z = 0
    sec58[2*spieler] = z
```

**Getaktet:** einziger Aufrufer ist der KI-Takt (C `0x4BFB80`, Protokollname
»AI«), der über `Takt % 50` 20 Aufgaben auf 49 Plätze verteilt; die Produktion
ist **Fall 4 = `Takt % 50 == 5`**. Also **ein Schlangeneintrag je Spieler alle
50 Takte**.

### ⭐ Die Gegenprobe MIT Nullmodell

Die tragende Vorhersage aus dem Code: der Zeiger wird nur auf 0 oder auf einen
Platz gesetzt, dessen Art-Byte ≠ `0xFF` ist — also `zeiger < Schlangenlänge`.

| Prüfung | Treffer |
|---|---|
| **`zeiger < Schlangenlänge`, 14 × 8 Plätze** | **112/112 = 100 %** |
| ungerade Bytes (hohes Byte) alle 0 | 112/112 = 100 % |
| sec63-Art-Byte ∈ {0, 1, 0xFF} | 5 600/5 600 = 100 % |
| Schlangen lückenlos (`0xFF` am Stück am Ende) | 112/112 = 100 % |

**Die Nullmodelle** — Zeiger von Spieler p gegen die Schlange von Spieler p+k:

| k | **0 (die Deutung)** | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| Treffer | **100 %** | 41,2 % | 0 % | 29,4 % | 0 % | 17,6 % | 23,5 % | 5,9 % |

Dazu: Schrittweite 1 statt 2 → **50 %**. Gleichverteilter Zufallszeiger → **21,5 %**.

**Was in den Schlangen steht** (327 belegte Sätze): Art 0 → »AI: production in
base« (Argumente 50…124, 282 Stück), Art 1 → »Build in airp« (Argumente 0/1/4,
45 Stück), `0xFF` → Endmarke (5 273). Bodeneinheiten gegen Flugzeuge, sauber
getrennt.

### ⚠ Zwei Grenzen bei sec58 — und eine Falle für den Nachbau

1. **Der `0xFF`-Zweig ist praktisch unerreichbar.** Der einzige Schreiber legt
   nur 0…49 ab, und die Neustart-Nullung setzt **0**, nicht `0xFF`. In allen 14
   Dateien: 0/112.
2. ⚠⚠ **Frischstart aus `.CWM`:** `.CWM` lädt nur sec1…38, also weder sec58 noch
   sec63. Beide werden genullt (sec63 per `rep stosd` über 1 200 B). Die
   **richtige** Füllung mit der `0xFF`-Endmarke macht erst eine zweite Stelle:
   8 × 50 mit Schrittweite 3, C `0x488305` (`eax`) / F `0x4869C5` (`ecx`).
   **Wer im Nachbau nur nullt, bekommt 50 Sätze »Art 0, Argument 0« statt einer
   leeren Schlange** — die KI baute dann endlos Einheit 0.

**Nachbarn nebenbei:** sec59 (8 B, C `0xB46950`) = Transport vorhanden, je
Spieler 1 Byte, sperrt die Produktion komplett. sec63 wird von »Cannot add new
'vyroba'« (C `0x4CF640`) gefüllt, das den ersten `0xFF`-Platz von 50 sucht.

### ⭐⭐ sec32 Zeile 9 und 10: der Negativbefund HÄLT — jetzt belegt statt vermutet

Fünf voneinander unabhängige Wege, alle in **beiden** EXE:

| Weg | C | F |
|---|---|---|
| Relokationen mit Wert im Fenster [Basis−64, Basis+233) | 75 | 75 |
| davon im Puffer selbst [0, 169) | 52 | 52 |
| **davon Schreibstellen** | **39** | **39** |
| **betroffene Zeilen** | **{1,2,3,4,6,12}** | **{1,2,3,4,6,12}** |
| **Schreibstellen in Zeile 9/10 (Versatz 117…142)** | **0** | **0** |
| `rep stos`/`rep movs`, die den Puffer überlappen | 0 von 210 | 0 von 214 |
| Zeigerschleifen mit bekanntem Ende, die überlappen | 0 von 169 | 0 von 173 |
| **Relokationen, die die Initialisiereradresse tragen** | **0** | **0** |

Der letzte Punkt schliesst die Hintertür, die bei totem Code immer offenbleibt:
**die Adresse `0x4AFB20` / `0x4AF450` steht nirgends als Datum.** Ein indirekter
Aufruf ist damit ausgeschlossen, nicht nur unwahrscheinlich.

### ⭐ Der Beleg, der trägt: der tote Initialisierer trifft die Datei zu 100 % — ausser in Zeile 9 und 10

Beide Initialisierer symbolisch ausgewertet (39 Schreibstellen, 0 unaufgelöst,
**C und F erzeugen bitgleiches Ergebnis**) und gegen den Dateiinhalt gehalten:

| Messung | Wert |
|---|---|
| **ausserhalb Zeile 9/10** | **143/143 Byte = 100 %** |
| innerhalb Zeile 9/10 | 18/26 Byte = 69,2 % |
| gesamt | 161/169 = 95,3 % |

Der Initialisierer ist **kein Näherungswert, sondern eine exakte, aber
unvollständige Fassung** derselben Tafel. Ihm fehlen genau zwei Zeilen.

⭐ **Und Zeile 9 und 10 sind byteidentisch mit Zeile 2, 3 und 4** (`01 06 02 01`,
die Fabrikzeile) — genau das, was jemand tut, der zwei Gebäudearten nachträgt: er
kopiert die Fabrikzeile. Der Initialisierer ist schlicht der **ältere Stand**,
11 Gebäudearten statt 13, und wurde nach dem Nachtragen nie wieder angefasst,
weil er da längst tot war.

### ⭐ Der Bestand ist 37 Dateien, nicht 14

| Bestand | Dateien | sec32 byteidentisch |
|---|---:|---|
| `game.007` | 1 | (Bezug) |
| `1.DM … 13.DM` | 13 | 13/13 |
| **alle `.CWM` in `Assets/Legacy/LEVELS`** | **23** | **23/23** |
| **gesamt** | **37** | **37/37 = 100 %** |

`.CWM` lädt sec1…38, sec32 liegt also **im Kartenformat**. Damit ist die Frage
beantwortet, soweit sie beantwortbar ist: **der Karteneditor**, und für jede
ausgelieferte Karte gleich. Die Tafel ist keine Karteneigenschaft, sondern eine
Konstante, die das Kartenformat mitschleppt.

**Und die Zeilen 9/10 sind kein Randfall:** über 14 Dateien tragen **17 Flughäfen
und 45 Minen** = **62 Gebäude** diese Zeilen. Ohne die 169 Byte aus der Datei
bekämen sie alle null Andockpunkte — der Bahnbau fiele dort lautlos aus.

### ⭐ Nebenbefund: 13 Zeilen sind exakt richtig

`rail_register` (C `0x4B00A0`, »Cannon build more 'rail-possible' buildings«)
prüft **`cmp dl, 0xC; ja → 0xFF`**. Arten > 12 — und davon gibt es bis Art 73 —
fallen vorher heraus. Die Zeilenwahl ist `[ecx + ebx*4 + Basis]` mit `ebx = 3·Art`,
also **13·Art**. Dazu aufgeschlossen: **sec33** (960 B, C `0xA8D508`) ist das
Verzeichnis der bahnfähigen Gebäude, Sätze à 8 B, und die Suchschleife läuft nur
bis `0xA8D64A` — **höchstens 40 Einträge**.

### ⚠ C gegen F bei sec32 — eine ehrliche Abweichung

Die 52 Verweise im Puffer sind in C und F **nicht in derselben Reihenfolge und
nicht mit denselben Registern** codiert (vier Stellen, alle im Leser). **Die
Rechnung ist dieselbe**; es ist Registerwahl und Befehlsanordnung des Übersetzers.
Die 39 Schreibstellen und ihre Zeilen sind deckungsgleich.

### Was offen bleibt

* **Warum** der Initialisierer tot ist — dazu bräuchte es eine dritte, frühere
  EXE. Aus C und F allein nicht zu holen.
* Das **dritte Byte** jedes sec63-Satzes: wird geschrieben, ist aber in allen
  327 belegten Sätzen **0**.
* Ob die 22 Zeigerschleifen je EXE mit laufzeitabhängiger Schranke wirklich nie
  so weit laufen — das ist ein **Reichweitenargument, kein Beweis**. Die drei
  nächstgelegenen sind einzeln nachgeprüft und begrenzt.
* sec106 (8 B) und sec61 (8 B) sperren die KI-Produktion je Spieler; beide nur
  angelesen.

---

## AI. ⭐⭐ Der Nebel, die Lagentafel und `0xFFFD` — drei Rätsel, drei Zahlen (21.08.2026)

Alles hier ist **in beiden ausgelieferten EXE gelesen**.

### AI.1 ⭐⭐ `0x63` ist die TÜRZELLE eines Gebäudes — 322 von 322

Im **Gebäudetakt** (C `0x43CA54`, F `0x43BAF4`; die Protokollmarke mitten in der
Schleife ist `"Bg: "`) läuft eine Schleife über **sec3** = 300 Sätze zu 76 Byte:

```
  C 0x43CAA9   cmp cl, 0x63          ; cl = Satz[0x0A], nur wenn > 99
  ...          jeder zweite Takt, cl++
  C 0x43CACD   cmp cl, 0xFA          ; bei 250:
               Satz[0x0A] = 1, und wenn Satz[0x34] != 0:
  C 0x43CB08   mov word[Zelle*2 + sec6],  0xFFFE
  C 0x43CB12   mov byte[Zelle   + sec20], 0x63
      Zelle = (Satz[0x00] + Satz[0x35])*256 + (Satz[0x02] + Satz[0x36])
```

`Satz[0x34]` ist das **Türkennzeichen**, `Satz[0x35]/[0x36]` der **Türversatz**.

| | Treffer |
|---|---|
| **`sec20[(sp+Satz[0x35])·256 + (ze+Satz[0x36])] == 99`** | **322 / 322 = 100 %** |
| Nullmodell x/y vertauscht | 3 / 322 = 0,9 % |
| Nullmodell Nachbarzelle (+1 Zeile) | 0 / 322 = 0,0 % |
| Nullmodell Zufallszelle | 1 / 322 = 0,3 % |
| Nullmodell **ohne** Türversatz (Ankerzelle) | 0 / 322 = 0,0 % |

Und die **Anzahl** stimmt in **14 von 14** Dateien aufs Stück.

⭐ **Wozu die Tür da ist: der Gebäudegriff steht EINE Zelle davor.**
`sec6[Türzelle − 1] == 60000 + eigener sec3-Index` → **322/322 = 100 %**, alle
drei Nullmodelle 0/322. Die Griffsäule ist 2 bis 4 Zellen tief — **darum ist der
scheinbare Fehler um eins in den Lesern keiner**: wer bei `K` oder `K+1` eine Tür
findet und dann `sec6[K−1]` holt, trifft beide Male dasselbe Gebäude.

**Wer `0x63` liest**, beide EXE: der Nebelspeicher-Zeiger (C `0x432367`), dasselbe
auf sec20 (C `0x432462`), `infantry` (C `0x406D57`), `move units end`
(C `0x40BB2E`: 99 → `xor al,al; ret` — **die Tür ist unbetretbar**), `shoot end
false` (C `0x4116DA`), `Unit missing` (C `0x433E21`) und `Destroy ramp`
(C `0x4CE6EF`: `cmp …,0x63; seta al; ret` — ein Prädikat »liegt hier ein Bauwerk
(>99)?«).

### ⚠ Berichtigung: es gibt KEINE 5×5-Umgebung

Die Notiz »Zustand 1 sucht `0x63` in seiner 5×5-Umgebung« ist **falsch**.
C `0x4116DA` liest **einen** festen Versatz `+0x1FE` = 510 = `2·256 − 2`, also die
Zelle **(Spalte + 2, Zeile − 2)**. Kein Schleifenkopf. Die Funktion (C `0x411670`)
ist der **Einstieg in die Basis** — und damit ein **zweiter, unabhängiger Beleg**,
dass 99 die Tür ist:

```
  Einheitensatz[0x0B] == 0x49  und  [0x0E] == 0x47
  sec20[Zelle + 510] == 0x63              ; die Tuer liegt bei (Sp+2, Ze-2)
  cx = sec6[Zelle + 509] ; cl -= 0x60     ; 60000+n -> Gebaeudeindex n
  sec48[Satz[0x40]][0x0F] == n            ; ist das MEIN Transportziel?
  sec3[n][0x04] == 1                      ; und ist es eine Basis?
  -> Satz[0x14] = 0x19
```

### AI.2 ⭐⭐ `0x542E18` ist NICHT das Geländefeld — es ist sec20, die Lagentafel

Drei unabhängige Belege: die Ladertafel (sec20, 65536 B, C `0x542E18` /
F `0x541E78`; `push 0x542E18` @C `0x41E514` Lader und `0x41D4BA` Speicherer), der
Entladebefehl C `0x4CF100`, und die Werte selbst.

**Die vollständige Wertetafel von sec20** — 14 Dateien:

| Wert | Stück | was es ist |
|---:|---:|---|
| **0** | 317 428 | nichts, gewöhnlicher Boden |
| **1 … 80** | 3 574 | ⭐ **GLEIS DER VERKEHRSLINIE `n − 1`** — geklaert am 21.08.2026, siehe AW.3: jede markierte Zelle steht in sec22 unter genau dieser Linie, **7 872 / 7 872 = 100,00 %** (Nullmodelle: Linie `n` 0,88 %, `n−2` 0 %, zufaellig 1,96 %). sec34 hat 80 Plaetze -> 80 Werte, geht auf. Hoechster je beobachteter Wert: 43. |
| **98** | 1 102 | ⭐ **von einem Betriebsgebaeude (Typ 2/3/4) geraeumte und DAUERHAFT GESPERRTE Zelle** — geschrieben an genau einer Stelle, C `0x43DE88`, nur wenn dort ein Landschaftsobjekt (> 8000) stand; setzt zugleich `sec6 = 0xFFFF`. Gleiszellen mit 98: **1 600 / 1 600 = 100 %** gesperrt (Nullmodell aller Gleiszellen: 16,6 %). Siehe AW.4. |
| **99** | 322 | ⭐ **Tuerzelle eines Gebaeudes** — jetzt am Code belegt (C `0x43CB12`) und gemessen: **813 / 813 = 100,0 %** liegen auf `(x + [+0x35], y + [+0x36])` eines Gebaeudes, Nullmodell 0,12 %. ⭐ Damit heisst »aus Gelaendeklasse 99 darf nicht geschossen werden« woertlich: **aus einer Tueroeffnung heraus wird nicht geschossen.** Siehe AW.4. |
| **100 + n** | 264 | **Brücke/Mole Nr. n aus sec17** |
| **200 + n** | 12 | **Rampe Nr. n aus sec21** |

**Brücke am Code:** C `0x41C5BB` liest sec20, rechnet `sub ax, 0x64` und ruft
damit **`Erase bridge`** (C `0x4CB0A0`). Die 100 ist die Basis, kein Schwellwert.
Gefundene Brückennummern mit belegtem sec17-Satz: **19/19 = 100 %**, Nullmodell
(Index + 7) **0/19**.

⚠ **Ein Muster, das nicht aufgelöst ist.** Das Niederband 1…34 und das Brückenband
100+n schliessen sich in allen 14 Dateien **gegenseitig aus**. Die Zellen eines
Niederband-Werts bilden eine durchgehende **diagonale Kette** quer über die Karte.
Eine erschöpfende Suche über alle Abschnitte 2…122 mit jeder Schrittweite 1…79
findet **keine** Tafel, deren Belegung dazu passt. **Ungeklärt.** Der Code kennt
zwei Unterbänder: `1…59` und `60…98`.

### ⭐ Wo das Gelände WIRKLICH steht

* **sec1** — `W·H·4`, Zeiger `dword[0x677E20]`, **zeilenweise** mit Schrittweite
  `dword[0x542DC4]` = W. `word[+0]` = Kachelcode, `byte[+3]` = Klassenbyte.
  Holer C `0x41D090` (Kachel), C `0x41D110` (Klassenbyte), Setzer C `0x41D140`.
* **sec6** — die *imap*, `spalte·256 + zeile`, u16:

| Bereich | Stück | Bedeutung |
|---|---:|---|
| `0xFFFE` | 142 828 | frei |
| `0xFFFC` | 81 655 | Wasser |
| `0xFFFD` | 50 967 | rau |
| `0xFFFF` | 3 934 | gesperrt |
| `50000 + n` | 30 738 | Waldplatz n aus sec18 (96,8 % exakt) |
| `60000 + n` | 9 924 | Gebäudeplatz n aus sec3 (300 Plätze) |
| `< 8000` | 2 198 | Einheitenplatz |
| `10000…13999` | 290 | Infanteriezelle |
| `61000 + n` | 842 | ⭐ **sec4-Objekt n** — benannt am 21.08.2026, siehe Abschnitt AL |

⚠⚠ **Berichtigung an einer Zahl, die wir zitieren:** `0xEA60` ist **60000**, nicht
50000. Der Bereichstest im Aufdecker (C `0x420253`) prüft also **60000…60299 =
die 300 Gebäudeplätze**, nicht den Wald.

### ⭐ Der Nebelspeicher merkt sich den LAGENWERT, nicht das Gelände

**Alle** Schreiber von `0x689710`, beide EXE — jeder ist entweder eine Nullung
oder eine wortwörtliche Kopie aus sec20: C `0x437F55` (Missionsanfang, 256×256 auf
0), C `0x41FEE5` / `0x420035` / `0x4202C5` (`fog[Z] := sec20[Z]`), C `0x4D57B6`
(Nullung über Register).

⚠ **Die indirekten Wege sind geprüft** — »kein Leser gefunden« heisst nicht »kein
Leser«: C `0x4B7FC0`, `0x4B8937` und `0x4D5796` laden die Adresse in ein Register;
es sind die zwei Kartenzeichner und ein Löschlauf, keine weiteren Schreiber.

**Damit ist »ist es derselbe 0x63?« beantwortet: ja, zwangsläufig** — der
Nebelspeicher enthält nur Werte, die aus sec20 stammen. ⭐ Und die Übersichtskarte
(C `0x4B822A`) benutzt ihn so: `0` → nichts, `1…59` → eine Farbe, `≥ 60` → eine
andere.

**`0x678B58` = sec50, der Takt-Nebel: BESTÄTIGT.** `rep stosd` C `0x4205BF`,
`= 1` bei C `0x420268`/`0x42029A` (jetzt gesehen), `= 2` bei C `0x41FFE7`/`0x420013`
— das ist der **SAUM**.

⚠ **Ein Fehler im Original, in beiden Fassungen gleich:** `0x3FFF·4 + 2 + 1 =
65535` — die Nullung ist **ein Byte zu kurz**. Das letzte Byte von sec50 wird nie
geräumt. Folgenlos (die Karte ist höchstens 254 breit), aber es steht so da.

### AI.3 ⭐⭐ `0xFFFD` ist »rau« — und die 1070 sind ein Trugbild des Prüfstands

`Can_go` (C `0x4053B6`/`0x4053BD`) nimmt für Infanterie **`0xFFFE` und `0xFFFD`**
als begehbar; alles andere fällt in den Objektzweig (»Infantry go on wrong
square«). Das deckt sich mit unserer schon gebauten Lesung.

**Alle `0xFFFD`-Schreiber** liegen unter genau zwei Marken: **`Erase bridge`**
(C `0x4CB471`, `0x4CB47B`, `0x4CB7D5`, `0x4CB7DE`) und **`Destroy ramp`**
(C `0x4CBB4A`). `Erase bridge` räumt einen 3×2-Abdruck: sec20 auf 0, das Deck auf
`0xFFFD` (rau = Trümmer), die Reihe darüber/darunter auf `0xFFFC` (Wasser ist
wieder da).

⭐ **Der Waldbrand schreibt NIE `0xFFFD`:**

| Ausgang | sec6 wird | Protokollmarke | C |
|---|---|---|---|
| ausgebrannt, 19/20 | **`0xFFFE`** | `"dohorel forest - sjizdnej"` | `0x4CA43E` |
| ausgebrannt, 1/20 | **`0xFFFF`** | `"dohorel forest - nesjizdnej"` | `0x4CA47F` |
| durch Schaden entfernt (`zrus`) | **`0xFFFE`** | — | `0x4CADDA` |

⚠ **Und hier die Falle, vor der die Regel warnt:** der Adressabtast findet nur
**zwei** Schreiber des sec18-Zustandsbytes (C `0x4CACAE`, `0x4CADD3`). Der dritte
— C `0x4CA3A2` / F `0x4C9F52`, das Ausbrennen — schreibt über `byte[esi+1]` und
taucht in **keinem** Adressabtast auf.

**Die Messung: die 1070 sind reproduziert — und tragen nichts.**

| Stichprobe | `0xFFFD` | `0xFFFC` |
|---|---|---|
| Zellen freier sec18-Sätze (n = 6097) | **17,5 %** | 0,1 % |
| **Zufallszelle derselben Karten** | **16,7 %** | 25,3 % |
| Nachbarzelle eines lebenden Waldes | 10,7 % | 0,0 % |

17,5 % gegen 16,7 % — **kein Unterschied**. Dass die Stichprobe trotzdem nicht
zufällig ist, zeigt dieselbe Tafel: Wasser ist bei Zufallszellen 25,3 %, an diesen
Zellen 0,1 % (Wald wächst nicht auf Wasser). Der Vergleich trägt also — und er
sagt: **`0xFFFD` enthält keinerlei Aussage über Brand.**

⭐⭐ **Der harte Beleg: 49 nie gespielte `.CWM`** — Karten, auf denen kein
Spieltakt gelaufen ist — zeigen dasselbe Bild (1220 × `0xFFFD`). **Die
»abgebrannten Felder« sind gar keine.** Ein freigegebener sec18-Satz behält seine
alten Koordinaten (`zrus` setzt nur `Zustand = 0`), und der Karteneditor hat beim
Bauen Bäume gesetzt und wieder gelöscht.

### Und die »32 von 108 brennenden Feldern mit lebender Waldkachel«

**Alle 108 tragen eine Waldkachel** — und das ist richtig so. Die Kachel wird erst
beim **Ende** getauscht (`10381 + ((alt−10381) mod 57 / 19)·19 + Klassenbyte`, in
`zrus` und im Ausbrennen identisch), nie währenddessen. In sec6 tragen **108/108**
brennende Felder ihren Waldgriff `50000+n` (Nullmodell x/y vertauscht: 27,8 %).

### ⚠ Zwei Berichtigungen an unseren eigenen Notizen

1. **»14 Spielstände lesen sich restlos in alle 131 Abschnitte« stimmt nicht.**
   Alle 14 gehen restlos auf (Rest = 0 B), aber nur `game.007` und `1.DM` reichen
   bis **sec131**. `3…10.DM` enden nach **sec122**, `2.DM`/`11…13.DM` nach
   **sec120**. Es ist das ältere Dateiformat (die 08.07.1997-Fassung aus AG).
2. Der Nebelzeichner prüft nicht den »rechten Nachbarn«, sondern `Index + 1` —
   bei `spalte·256 + zeile` ist das die **nächste ZEILE**, nicht die nächste
   Spalte.

### Was ungeklärt bleibt

* **Das Niederband 1…34 in sec20** — Form und Geometrie stehen, die zugehörige
  Tafel nicht.
* **`sec20 == 98`**: 1102 Einzelzellen, 90,5 % davon `0xFFFF` in sec6. Ein
  sperrender Aufbau — welcher, sagt keine Datei.
* ~~**`sec6 ≥ 60300`**: 842 Zellen, keiner Tafel zugeordnet.~~ ⭐ **ERLEDIGT am 21.08.2026:** es ist `61000 + n` = sec4-Objekt n, belegt am Loeschtrupp C `0x4CA610` und mit 1913/1913 Rueckschluss gemessen. Siehe Abschnitt AL.
* **Die weiche Nebelkante selbst.** Der Saum (`sec50 == 2`) ist noch nicht gegen
  Bilddaten gemessen — das Bildschirmfoto aus Abschnitt A wird weiterhin
  gebraucht.
* Warum die Tür erst bei Zähler 250 gesetzt wird.

---

## AJ. ⭐⭐ DER NACHLAUF — alle 130 Abschnitte über die Relokationstafel nachgezählt (21.08.2026)

Abschnitt AH hat gezeigt, dass unser Suchverfahren löchrig war. Dieser Abschnitt
zieht die Folgerung: **jeder Abschnittspuffer beider EXE ist neu erhoben**, nicht
abgetastet. Werkzeug: `aekernel-tools/reloc_refs.py` und `nachlauf.py`.
⚠ Beide liegen in `aekernel-tools/` und sind damit **nicht versioniert** — wer
sie braucht, findet die Herleitung hier.

### ⚠ Zuerst zwei Berichtigungen an eigenen Angaben

**1. Die Prozente waren vertauscht.** In AH stand »C nach 4 %, F nach 18 %«. Es
ist umgekehrt: **C bricht nach 78 845 von 443 471 Befehlen ab (18 %), F nach
16 622 von 442 952 (4 %)**. Nachgerechnet mit `reloc_refs.py --stat`, das die
Zahl jetzt selbst ausrechnet, statt sie abzuschreiben.

**2. Das erste Werkzeug hatte denselben Fehler wie das Verfahren, das es
ersetzen sollte.** Es suchte von der Relokationsstelle **rückwärts** nach einem
Befehl, der sie überdeckt. Der Selbsttest an sec58 — einem unabhängig gelesenen
Fall — lieferte:

```
  0041EFC4  liest     adc eax, 0xb38d40          <-- FALSCH
  0041EFC3  SCHREIBT  mov dword ptr [0xb38d40], edx   <-- richtig
```

`89 15 40 8D B3 00` gegen `15 40 8D B3 00`: **dieselben vier Adressbytes, ein
Byte Versatz — und aus einem Schreiber wird ein Leser.** Genau die Art Fehler,
die einen Negativbefund kippt, und zwar in die bequeme Richtung. Das Werkzeug
baut jetzt **einen Befehlsindex von vorn durch das ganze `.text`** (mit
Resynchronisation) und meldet **UNKLAR**, wenn der Befehl an einer Stelle die
Adresse nicht wirklich trägt.

⭐ **Der Selbsttest ist der Punkt.** Ein Werkzeug, das Negativbefunde prüfen soll,
muss zuerst an einem bekannten Fall zeigen, dass es die richtige Antwort gibt.

### ⭐ Und der Rohabtast lag in die andere Richtung falsch

Der alte Befund zu sec101 meldete »roh über das ganze `.text`: **C = 9 Treffer,
F = 5**«. Die Vollerhebung findet **5 in beiden**. Die vier zusätzlichen in C
waren **Falschtreffer** des Bytemusters — der Rohabtast hatte sie schon damals
als »Zufallstreffer« abgetan, aber geraten, nicht gemessen.

**Damit sind beide alten Verfahren belegt fehlerhaft, und zwar gegenläufig:**
der Linearabtast findet zu **wenig** (er bricht ab), der Rohabtast zu **viel**
(er trifft Datenbytes). Ein Befund, der nur auf einem von beiden steht, ist
wertlos.

### Das Ergebnis: 130 Abschnitte, ~~ein einziger~~ ⚠ **ZWEI** wirklich tote

⚠⚠ **BERICHTIGT am 21.08.2026, siehe AW.1.** Dieser Lauf suchte nach »2
Fundstellen« und hat **sec67** darum uebersehen: es hat vier, aber zwei davon
sind die **Nullung des Laders selbst**. Das richtige Kriterium lautet **»keine
Fundstelle ausserhalb von Lader `0x41E070` und Speicherer `0x41D210`«** — und
danach sind es **sec36 und sec67**.

| | |
|---|---|
| Abschnitte erhoben (beide EXE) | **130** |
| **ohne jeden Benutzer ausser Lader und Speicherer** | ⚠ **2 — sec36 UND sec67** (berichtigt, AW.1) |
| mit Blockregister (`mov esi/edi, <Puffer>`) | 46 |
| deren Verweiszahl in C und F abweicht | 21 |

### ⭐⭐ sec36 ist tot — 10 500 Byte, C `0x830790` / F `0x82F7F0`

```
  C:  0041D60A  push 0x830790      (Speicherer)
      0041E664  push 0x830790      (Lader)
  F:  0041C7CA  push 0x82f7f0
      0041D823  push 0x82f7f0
```

**Zwei Verweise je Fassung, sonst nichts.** Kein Schreiber, kein Leser, **kein
`mov esi/edi`** — also auch kein Blockbefehl, auf den eine Konstante zeigt.

⭐ **Und die Daten stimmen zu:** über **13 von 13** `.DM`-Dateien ist sec36
**0 von 10 500 Byte ungleich null**. Ein Puffer, der geladen und gespeichert
wird, nie angefasst und immer leer ist.

Das ist der **sechste** Fund dieser Art — nach den vier toten Mauszeigern, den
zwei toten Befehlsnummern (523/526), den 34 toten `SPR.DAT`-Bildern, dem toten
Zufall in den Minensätzen und dem toten sec32-Initialisierer — und der erste,
den der systematische Lauf gefunden hat statt eines Zufallsblicks.

⚠ **Was das NICHT heisst:** 3 963 Blockbefehle stehen im `.text` von C. Keiner
davon wird von einer Konstanten auf sec36 gerichtet, aber eine Blockoperation
mit vollständig berechneter Adresse bliebe unsichtbar. Der Befund ist so hart,
wie ein Negativbefund werden kann — bewiesen ist er nicht.

### ⚠ sec101 und seine vier Nachbarn tragen denselben Fingerabdruck

| Abschnitt | Grösse | Verweise / Schreiber / Leser / Blockregister |
|---|---:|---|
| sec100 | 92 800 | 5 / 0 / 2 / **3** |
| **sec101** | 9 200 | 5 / 0 / 2 / **3** |
| sec102 | 500 | 5 / 0 / 2 / **3** |
| sec103 | 1 000 | 5 / 0 / 2 / **3** |
| sec104 | 32 | 5 / 0 / 2 / **3** |

Fünf Abschnitte, **identisches Muster in beiden EXE**: null Schreiber, dafür je
drei Stellen, die die Adresse nach `esi`/`edi` laden. Das ist die Handschrift
einer **Blockkopie** — genau das, woran sec101 einmal falsch für tot erklärt
wurde. ⭐ **Der Fingerabdruck ist damit ein Suchmuster**, kein Einzelfall: wo
»0 Schreiber, aber Blockregister« steht, ist die Frage nicht *ob*, sondern *wo*
kopiert wird.

### ⚠ 21 Abschnitte zählen in C und F verschieden viele Verweise

Die grössten Abstände: sec111 (361 zu 369), sec54 (75 zu 81), sec48 (148 zu
141), sec7 (117 zu 122), sec5 (2 370 zu 2 360), sec3 (1 094 zu 1 086), sec52
(13 zu 15), **sec59 (10 zu 9)**.

⭐ sec59 ist die **schon bekannte** Verhaltensdifferenz (C setzt `sec59 = 1`
@`0x4BB7FC`, F nicht). Dass der Lauf sie unabhängig wiederfindet, ist die
Eichung dieser Spalte.

⚠⚠ **Aber die anderen zwanzig sind KEIN Befund**, und das ist wichtig: eine
abweichende Verweiszahl entsteht auch dann, wenn beide Fassungen dasselbe
rechnen. Der sec32-Lauf hat es vorgeführt — dort waren vier Stellen anders
codiert (`[ecx + edi*4 + ADR]` gegen `[edx + ADR]`), **die Rechnung war
dieselbe**. Registerwahl, Einbettung und Befehlsanordnung des Übersetzers
verschieben die Zahl, ohne dass sich etwas ändert.

→ Die zwanzig sind **Kandidaten für eine Verhaltensdifferenz**, mehr nicht. Wer
einen daraus machen will, muss die Stellen paarweise gegenlesen, so wie es bei
sec59 und sec32 geschehen ist. **Als Liste sind sie trotzdem wertvoll**: sie
sagt, wo zu suchen wäre, und sie sagt, wo NICHT — die 109 übrigen Abschnitte
stimmen in der Zahl überein.

### Was der Nachlauf NICHT geprüft hat

Die Vollerhebung greift nur bei **Puffern mit einer Adresse**. Diese
Negativbefunde stehen weiterhin auf dem alten Verfahren und wären einzeln
nachzulaufen:

* **Feldweise Befunde** — »sec62 `+0x00` hat 0 Lesestellen«, »`+2` ist tot«,
  »`+9` ist tot«. Der Puffer wird benutzt; die Frage ist, welches Byte darin.
  Das entscheidet nur Codelesen.
* **Konstanten statt Adressen** — »Opcode 975 wird nirgends erzeugt«. Eine
  Zahl im Befehlsstrom ist keine Relokation.
* **Die vier toten Mauszeiger (6, 7, 8, 25)** und die **34 toten
  `SPR.DAT`-Bilder**: beides hängt an Sprungtafeln und Ladeschleifen, nicht an
  Adresskonstanten.

⚠ Alle drei Gruppen sind damit **ungeprüft**, nicht bestätigt.

---

## AK. ⭐⭐ DER NACHLAUF, zweiter Teil — was die Vollerhebung nicht konnte (21.08.2026)

Abschnitt AJ hat alle 130 Abschnittspuffer neu erhoben und dabei drei Gruppen
ausdrücklich als **ungeprüft** liegenlassen: feldweise Befunde, Konstanten statt
Adressen, und Sprungtafel-Fälle. Sie sind jetzt nachgelaufen. **Fünf von sechs
Befunden halten — einer ist widerlegt, und zwei Zahlen waren zu klein.**

⚠ Alle Läufe nennen ihre **Eichung**: naiv gegen resynchronisiert. Ohne diese
zwei Zahlen weiss niemand, ob das ganze Bild gesehen wurde.

| | naiv | Abbruch bei | resynchronisiert | Anteil |
|---|---|---|---|---|
| C | 78 845 | `0x42BC1F` | **443 471** | **17,8 %** |
| F | 16 622 | `0x409FB0` | **442 952** | **3,8 %** |

⚠ Zwei Läufe haben unabhängig gemeldet, dass diese Prozente im Kopf von
`reloc_refs.py` **vertauscht** standen. Berichtigt; `--stat` rechnet sie jetzt
selbst aus, statt sie abzuschreiben.

### AK.1 ⭐ Die drei feldweisen Befunde halten — alle drei

Verfahren: Vollerhebung, dann **jede** Fundstelle einzeln zerlegt und der
Feldversatz **aus dem Indexregister** nachgerechnet — plus die
**Operandenbreite** je Zugriff (ein Wortzugriff auf `+8` läse `+9` mit). Alle
sechs Läufe (C und F × sec62/60/56): **0 UNKLAR**.

| Frage | Verweise C / F | auf das Feld | Datei-Sätze | Abweichungen |
|---|---|---:|---:|---:|
| **sec62 `+0x00`** liest jemand? | 74 / 74 | 6 — **kein Speicherzugriff** | 28 560 | Werte `{0,3,4,6}` wie vorhergesagt |
| **sec60 `+2`** | 38 / 38 | **0** | 112 000 | **0** |
| **sec56 `+9`** | 46 / 46 | **0** | 13 552 | **0** |

**sec62 `+0x00`:** Die 68 Zugriffe auf `+1` sind **ausnahmslos `mov byte`** — in
ganz sec62 gibt es keinen Wort- oder Dwordzugriff, die `mov ax, word[…]`-Falle
ist damit ausgeschlossen. Die 6 auf `+0` haben **überhaupt keinen
Speicheroperanden**: zwei `push` in die `fread`/`fwrite`-Hüllen, zwei
`rep stosd`-Nullungen, ein Zeiger für den Initialisierer und eine
Schleifenschranke (`cmp eax, Basis+510`, 510 = 2 × 255).

⭐ Der Initialisierer (C `0x488347…0x4883B6`) belegt die Deutung aufs Wort:
`cmp edi,edx` → **6 = gehört mir** · `cmp cl,0xB` → **4 = Besitzer 11** ·
`byte[…+sec53+0x15]` (Diplomatie) → **3 = Feind** · sonst **0**.

**sec60 `+2`:** ⭐ Beide Verfahren treffen sich — der **rohe Byteabtast** auf
`Basis+2` findet im gesamten 1,4-MB-Bild **null Vorkommen**. Wo der rohe Abtast
überzählt, kann er nicht null melden. Alle 34 Bytezugriffe benutzen einen Index
aus `lea reg,[u+u*2]` = **3·u**, einzeln nachgerechnet. Die einzige Zeigerform,
die ein `[reg+2]` erlaubte (C `0x4BC691`), ist verfolgt: zwei Benutzungen, beide
`+0`. Und im Zeichenbestand **beider** EXE gibt es `CPU0:` und `CPU1:` —
**kein `CPU2:`**.

**sec56 `+9`:** ⭐⭐ **Es ist ein AUSRICHTUNGSLOCH, kein vergessenes Feld.** Der
Satzlöscher (C `0x4BBBDF…0x4BBBF1`) räumt einen Satz mit genau vier Befehlen —
`+7`, `+6`, `+8` byteweise, dann `mov word [ebp+Basis+0xA], ax`. **`+9` wird
übersprungen**, weil es das Füllbyte ist, das das Wort auf `+0xA` gerade
ausrichtet. Kein einziger `+8`-Zugriff ist ein Wortzugriff; ein
`mov ax, word[+8]` hätte `+9` mitgelesen.

⭐ **Nebenbefund, hart:** `+6 == +7` in **13 552 von 13 552** Sätzen — die
»Kopie« ist keine Vermutung mehr.

### AK.2 ⭐ Opcode 975 hält — und »zwei tote Nummern« waren sieben

**Verfahren, und es ist besser als das alte:** ein Sofortwert 975 steht auf x86
immer als zusammenhängende Bytefolge `cf 03` im Befehl. Also **alle `cf 03` roh
aufzählen** (7 je Datei) und an **jedem** Startversatz davor decodieren. Damit
ist die Ausrichtung des Abtasts für den **Fund** irrelevant; sie sortiert nur
noch aus. Kontrollprobe mit 976: genau ein Befehl je Datei, auf der
Befehlsgrenze — der bekannte `Z`-Sender.

* **Kein Befehl** trägt 975 als Sofortwert. Die 7 rohen `cf 03` je Datei liegen
  alle **innerhalb** anderer Befehle (`jmp`-Versatz, `imul ecx,edi`,
  `add ecx,edi`, `movsx ecx,di`) — genau der Falschtreffer des Byteabtasts.
* **Keine Tafel:** das Wort `0x03CF` kommt in **keiner** Sektion ausserhalb
  `.text` vor, in beiden Dateien. `.text` ist die einzige ausführbare Sektion.
* **Keine Rechnung:** auf dem Opcode-Feld steht kein `inc`/`add`/`xor`, nur `mov`.
* **Dateiversätze bestätigt**, mitsamt Befehl: C `mov word[0xB8A3D8], 0x3D0`
  @`0x4139F5`, `d0 03` bei **`0x12DFC`**; F `mov word[0xB89438], 0x3D0`
  @`0x4137AA`, bei **`0x12BB1`**.

⭐ **Die alte Bytesuche `66 c7 05` sah nur 140 von 148 Schreibern.** Es gibt je
Bau **8 Schreiber über ein Register**, alle mit einer Konstanten unmittelbar
davor — und **1100** (C `0x4C2303`) und **1004** (C `0x4C49C0`) haben **keinen
anderen Erzeuger**. Jede Liste, die nur `66 c7 05` gezählt hat, führt sie zu
Unrecht als tot.

⭐ **Nicht zwei tote Nummern, sondern sieben** — eigener Behandler, kein
Erzeuger, in C und F identisch: **10, 523, 526, 975, 992, 997, 1200**. Weitere
195 Nummern ohne Erzeuger zeigen nur auf den **Vorgabe-Behandler**
(C `0x4C4CCD`) — das sind Lücken, keine toten Befehle.

⭐ **Zwei Adressen, die Regel 1 noch fehlten:** Rohpuffer F **`0xB89438`**,
Programmpuffer F **`0xB4FA38`**, Verteiler F `0x4C2262`.

⚠ **Der Netzweg bleibt offen.** C `0x404480` / F `0x404460` (DirectPlay-Empfang,
`call [esi+0x54]`) schreibt 236 rohe Paketbytes **direkt** in einen
Programmsatz. Ein Gegenspieler könnte jeden Opcode liefern, 975 eingeschlossen.
»Kein Schreiber in beiden Bauten« bleibt richtig — »975 ist unerreichbar« wäre
es nicht. Über Datei geht es dagegen nicht: es gibt **kein `fread`** von 236 B.

### AK.3 ⭐⭐ WIDERLEGT: die Mauszeiger 6, 7 und 8 leben

Der Befund lautete: »vier Zeiger (6, 7, 8, 25) sind gefüllt, aber tot — kein
Code wählt sie«. **Drei davon leben.** Sie werden nur nicht über den
Zustandsautomaten gewählt, sondern **direkt aus der Satztafel gezeichnet** —
deshalb hat eine Analyse der Sprungtafel sie übersehen.

| Satz | C-Stellen | was es ist |
|---|---|---|
| **6** (+268) | `4B43A6` | **Klickmarkierung auf der Karte**, 5-Bild-Einmalanimation |
| **7** (+312) | `42A7B6`, `42AC14`, `42AE8C`, `42B0C3`, `42BB8D` | **Auswahlmarke am Objekt** |
| **8** (+356) | `42AA8F`, `42CB9F` | zweite Auswahlmarke |

Satz 6 wird über `byte[0x5387E4]` durchgezählt: `0x4B6C40` setzt 0 und merkt die
Klickstelle (gerufen aus der Befehlsvergabe), `0x4B6C70` zählt hoch bis 4 und
setzt dann `0xFF` (Stopp). Sätze 7/8 hängen am Objektflag
`byte[0x6E26E3 + 78·id]`, das 10 Schreiber im Auswahlcode hat.

⚠ **Satz 25 bleibt tot** (5 Bilder, `0x1336F`…`0x147DB`, kein Leser). Satz 9 ist
in `ROBO.CWR` leer.

### ⭐ Die vollständige Tafel Zustand → Zeiger (C `0x4A9BEC` / F `0x4A951C`)

Beide Tafeln byteweise deckungsgleich, Versatz konstant `0x6D0`.

| Zust. | Zeiger | Zust. | Zeiger | Zust. | Zeiger |
|---|---|---|---|---|---|
| 0 | 0 | 9 | 13 | 18 | 22 |
| 1 | **1 oder 5 (berechnet)** | 10 | 2 | 19 | 23 |
| 2 | 2 | 11 | 11 | 20 | 14 |
| 3 | **27 oder 4 (bedingt)** | 12 | 16 | 21 | 15 |
| 4 | 0 | 13 | 19 | 22 | 17 |
| 5 | 11 | 14 | 17 | 23 | unverändert |
| 6 | 10 | 15 | 18 | 24 | unverändert |
| 7 | 2 | 16 | 20 | 25 | 24 |
| 8 | 12 | 17 | 21 | | |

Sonderwerte: **100** → 26 · **1000** → 3 · **1001** → 2 · **1002** → `0xFF`
(Zeiger aus). 26…99, > 1002 und negative Werte lassen den Zeiger unverändert.

⭐⭐ **Zeiger 5 steht nirgends als Konstante im Code** — er entsteht nur in
Zustand 1 aus `byte[0x6E26D2 + 78·id]` (`dec/cmp/sbb/and 4/inc` @`0x4A9B4B`).
**Ein Konstantenabtast hätte auch ihn für tot erklärt.** Dasselbe gilt für
Zeiger 27 (`0x4A9B7D`, nur bei `word[0x4FA0C8] == 10000`).

Sechs **Direktschreiber** auf `byte[0xA182D0]`, in beiden EXE gleich:
`0x415236`/`0x4152EB`/`0x4315EE` → `0xFF`; **`0x4152B8` → 28, der EINZIGE Weg zu
Satz 28**; `0x441C25` → 0; `0x4A9BCF` → die Funktion selbst.

### AK.4 `SPR.DAT`: 34 von 35 tot — bestätigt, Begründung berichtigt

⭐ **`SPR.DAT` hat 57 Plätze, nicht 35:** 0–26 belegt, 27–37 leer, **38–45
belegt**, 46–56 leer → 35 vorhandene Bilder. Der Lader (C `0x4B4100`) läuft
`esi = 0…37` (`cmp esi,0x26`) — die 8 nicht geladenen sind also genau
**38…45**. Der Grund war richtig benannt, die **Zahl der Plätze** nicht.

* **1 lebendig**: Index **19**, die Ersatzkachel für die Kennung `0xFFFF`.
  Gelesen wird **nur** `0xB0E188 + 4·19`, immer als Konstante, **nie
  berechnet** — C `0x4B42AB`, `0x4B44C0`.
* **26 geladen, nie gelesen**: 0–18 und 20–26.
* ⚠ **Zusatz:** Index 26 bekommt Grösse `0xFFFFA609` (der Folgeeintrag ist der
  Leer-Marker) → `malloc` scheitert, faktisch auch nicht geladen.
* ⚠ **F hat einen Leser mehr:** `0x4B8F00` ist ein zweiter, fast identischer
  Geländezeichner (Schleifenschranke `cmp eax,0x320`), der **in C nicht
  existiert**. Auch er benutzt nur Index 19.

⭐ **Nebenbefund:** die Bilder 19–26 und 38–45 sind je **865 B** — zwei Sätze zu
acht gleich grossen Kacheln, von denen nur die erste verdrahtet ist.
Kachelvarianten, die nie angeschlossen wurden.

### ⚠ Was auch dieser Nachlauf nicht kann

* **Er führt nichts aus.** »Kann erzeugt werden« ist nicht »tritt auf«.
* **Blockbefehle mit gerechneter Adresse** (3 963 in C, 3 957 in F) sind nicht
  einzeln aufgelöst. Für *feldweise* Fragen ist das die falsche Bauform — ein
  `rep` arbeitet dwordweise über ganze Sätze —, für Puffer bleibt es ein
  **Reichweitenargument, kein Beweis**.
* **Nachbarüberlauf:** sec56 endet exakt bei sec60, sec60 exakt bei sec123,
  sec103 exakt bei sec62. Die Schranken der Nachbarschleifen sind konstant und
  liegen im Nachbarn — wieder Reichweite, nicht Beweis.
* **Zeigerweitergabe über mehrere Register oder Strukturen** ist nicht verfolgt.
* Der **`ROBO.CWR`-Kopf** wurde aus der F-Fassung gelesen; zu C könnte eine
  andere Datei gehören.

---

## AL. ⭐⭐ DIE UNBESEHENEN ABSCHNITTE — vier Läufe, und fast alles ist gelesen (21.08.2026)

Vier Agenten haben die Abschnitte gelesen, die bis dahin nur mit Adresse und
Grösse in der Ladertafel standen. **Von 27 sind jetzt 25 gedeutet, 2 bleiben
ausdrücklich offen.**

⚠ **Zuerst eine Berichtigung an meiner eigenen Zählung.** Ich hatte »27
unbesehene Abschnitte, 5,3 % der Datei« gemeldet. Die Zahl war zu pessimistisch:
sie zählte Erwähnungen in **diesem Dokument**, nicht das, was der Nachbau schon
weiss. `CwmExtra.cs` führt sec34 seit langem als *»sec34 — the SPOJ lines,
80 × 214«*. Über Dokument **und** Code gezählt waren es **20 Abschnitte, 3,5 %**.

⚠ Und eine zweite: ich hatte einem Agenten die C-Adresse von `sec116` als
`0x815484` in den Auftrag geschrieben. Richtig ist `0x8154E4`, und so steht es
auch hier — **der Tippfehler war meiner, nicht der des Dokuments**. Der Agent hat
ihn über die Probe `.bss` C = F + `0xFA0` selbst gefunden (21 von 22 gingen auf,
nur dieser ergab `0xF40`).

### AL.1 ⭐⭐ `sec112` = `fly_part` — das Partikelsystem der Explosionen

**C `0xA51110` / F `0xA50170`, 72 000 B = 2000 Sätze × 36.** Der Name steht im
Bild: der Streuer bricht mit **`"Wrong size of fly_part"`** ab (C `0x504108`).

Die Schrittweite 36 kommt aus drei unabhängigen Stellen in **beiden** EXE
(`lea eax,[ebx*4]` + `[eax+eax*8+Basis]`), dazu die Schleifenschranke
`Basis + 6 + 36000`.

| Versatz | Feld | Versatz | Feld |
|---|---|---|---|
| +0x00/+0x01 | Kachel X / Y | +0x14 | f32 Steiggeschwindigkeit |
| +0x02/+0x03 | Feinlage X (0…39) / Y (0…19) | +0x18 | f32 Schwerkraft je Takt |
| +0x04 | i16 Höhe Z | +0x1C | f32 Bogenhöhe |
| +0x06 | **Art: 0 frei, 1 Trümmer, 2 Rauch** | +0x20 | i16 Grundhöhe, überblendet |
| +0x08 | u16 Sprite/Anim | +0x22 | Bildnummer = `(Takt + Satz) mod Bildzahl` |
| +0x0A…+0x0E | Ziel (Kachel, Feinlage, Höhe) | +0x23 | Rauchfahne 0/1/2 |
| +0x10 | Tempo | +0x0F | Richtung — **geschrieben, NIE gelesen** |

**Art 1** fliegt auf einer echten Wurfparabel (`+1C += +14`, `+14 -= +18`),
**Art 2** treibt mit dem **Wind** — über dieselben `byte[0x4F8D68]` /
`byte[0x4F8D6C]`, die auch den Waldbrand tragen, mit einer 8-Richtungstafel bei
`0x5040E0`.

⭐ **Vergabe:** erster freier Platz von 0 aufwärts; ist alles voll, würfelt der
Erzeuger `rnd%1000` und **gibt auf, wenn das > 300 ist** — Partikel werden unter
Last **verworfen**, nicht überschrieben.

**Die zwei tragenden Messungen**, nur auf lebenden Art-1-Sätzen:

| Aussage | Treffer | Nullmodelle |
|---|---|---|
| `+04 == +20 + trunc(+1C)` (die Wurfbahn) | **832/832 = 100 %** | `+14` statt `+1C`: **3,97 %** · Nachbarsatz: **3,25 %** · ohne `+20`: **17,07 %** |
| Stil `+23` folgt dem Sprite | **832/832 = 100 %** | Nachbarsatz: **83,17 %** |

⚠ Das zweite Nullmodell mit **83 %** ist der Grund, warum es dazugehört: ohne die
Gegenzahl sähe die Regel nach einer Entdeckung aus, obwohl fast jede Zuordnung
sie erfüllt hätte.

⭐ **Nebenertrag:** weil `+22` gleichverteilt ist, verrät sec112 die
**Bildzahlen der Effekt-Sprites**, die sonst nur in einer Laufzeittabelle stehen:
Anim 19…24 → 1 Bild, 29…38 → 6, 200…204 → 8, 210…212 → 12, 240…242 → 12.

#### ⭐⭐ Die ZWEITE Verhaltensdifferenz zwischen den Auslieferungen

| | C `0x42EAB0` | F `0x42DC70` |
|---|---|---|
| Kappe je Bildschirmzeile | `cmp bp,0x1F3` | `cmp ax,0x1F3` |
| **zusätzliche Zeilenschranke** | **`cmp al,0x46 / jae überspringen`** | **fehlt** |

⚠⚠ **BERICHTIGT am 21.08.2026 (BD.4): DAS IST KEIN EINZELUNTERSCHIED.**
Eine rohe Bytesuche ueber das ganze `.text`, unabhaengig von jedem Zerleger und
ueber drei Kodierungsformen, findet **22 Treffer in C und 0 in F** — alle
zwischen `0x42E04A` und `0x43083C`, also **genau die 22 Einsortierfaelle der
Zeichenliste**. C hat die Korbindex-Schranke an ALLEN 22 Stellen nachgetragen,
F hat sie nirgends: eine **systematische Fehlerbehebung**, kein Einzelfall.
⭐ Und die **70** ist nicht »Bildschirmzeilen«, sondern **70 Zeilenkoerbe der
Zeichenliste** — ein Korb ist eine Kachelzeile (70 × 20 = 1400 ≥ 1200).
⭐ Zwei unabhaengige Laeufe haben denselben Befund erhoben (BD.4 und BE.8).

C weigert sich, ein Partikel für Bildschirmzeile ≥ 70 einzutragen; **F trägt es
ein und schreibt über das Feldende hinaus.** C ist die gehärtete Fassung. Sonst
ist die Kette befehlsgleich (Aktualisierer 524/524, Streuer 195/195). Das ist
nach `sec59` der **zweite** belegte Unterschied.

⚠ **Offen:** der Puffer fasst 2000 Sätze, alle drei Schleifen enden bei 1000 —
aber **drei der 13 Dateien haben Sätze bis 1999 belegt**, und die bestehen alle
Formprüfungen. Aus den ausgelieferten EXE ist die obere Hälfte unerreichbar. Für
den Nachbau: **1000 aktive Plätze, 2000 Plätze Speicher.**

### AL.2 ⭐ `sec34` + `sec35` — die Verkehrslinien und ihre Belegungskarte

⭐ **Die krumme Zahl ist geknackt:** `16481 = ⌈257 · 513 / 8⌉` — 131 841 Bit, 7
Füllbits. **`sec35` ist ein Bitfeld**, ein Bit je Zelle, Index `257·yh + x`, wobei
`yh` in **halben Zeilen** zählt. Vier unabhängige Belege, darunter ein
Zwillings-Bytefeld, das auf **7 Byte genau** vor sec34 endet.

**`sec34` = 80 Linien à 214 B**, Satzweite dreifach aus dem Code. ⚠ **Zwei
Schranken im selben Bild:** die Aufräumschleife läuft über alle **80**, der
Zustandsautomat nur über die ersten **60**.

| Prüfung | Modell | Nullmodell |
|---|---|---|
| Strecke ab `(+2,+3)` endet auf `(+4,+5)` | **215/215 = 100 %** | vertauscht 0/215 · `(+2,+3)` 0/215 · `(+6,+7)` 0/215 |

⚠⚠ **BERICHTIGT am 21.08.2026 (AW.5): `+0x03` und `+0x05` sind nur die
NIEDERWERTIGEN BYTES der y-Halbzeile.** Die vollen 16 Bit stehen in
**sec122** (C `0xA66DD8`, 80 × 2 × u16). Halbzeilen laufen bis `2·H`; bei
H > 127 ueberlaeuft das Byte. ⚠ Die 23 `.CWM` tragen sec122 gar nicht.

| Satzweite 214 gültig | **780/780 = 100 %** | 212: 2,6 % · 213: 4,6 % · 215: 14,6 % |
| Streckenzelle → Bit gesetzt | **3577/4961 = 72,1 %** | Index vertauscht 1,69 % · **Zufallszelle 0,73 %** |
| Fehlstellen je Linie | genau vorn 2 + hinten 3, **215/215** | — |

⚠ **Das war für uns keine Neuentdeckung, sondern eine Bestätigung über Kreuz:**
unsere `RailLine` führt seit langem `Bud1`, `Bud2`, `Steps` und `Faze (+0xd5)` —
genau diesen Satz. Neu sind die **Endpunkte** `+0x02…+0x05`, die **Schritttafel**
`0x5043C0` und `sec35` vollständig.

⭐ **ERLEDIGT am 21.08.2026 (AW.5): mit sec122 sind es 0 von 215**, und das
Fehlstellenmuster ist dann exakt (2 vorn, 3 hinten) in 215/215. Die 40 %
schrumpfen auf 11–19 % je Datei; der Rest ist die **Spur einer Linie aus
einer groesseren Karte** — zu sec35 gibt es Test und Setzen, aber keinen
Loescher, das Bitfeld sammelt an.

~~⚠ Offen:~~ warum 42 von 215 Linien **kein** Bit tragen, und dass 40 % der gesetzten
Bits zu keiner heutigen Linie gehören (in `1.DM` liegen 66 von 70 davon
**ausserhalb der Karte**). Zu sec35 gibt es nur Test und Setzen, keinen Löscher —
das Feld sammelt offenbar an.

### AL.3 ⭐ `sec19` = die Flugzeuge · `sec4` = die brennbaren Einzelobjekte

**`sec19` — 200 × 68 B, bestätigt.** Die Zahlen stehen wörtlich im Code
(`cmp dx, 0xC8`, `i·68`). Gegen die acht Flugzeug-Vorlagen der EXE gehalten,
**sieben Felder auf einmal**:

| | Treffer | |
|---|---|---|
| **Schrittweite 68** | **189/190 = 99,5 %** | der eine Ausreisser ist ein Editorrest in `NET05.CWM` |
| Satzanfang +4 | 0/169 = 0 % | |
| Schrittweite 67 / 69 / 70 | 13,5 % / 8,9 % / 8,8 % | |

⭐ **`(0,0)` ist kein Ort, sondern der Merker »im Hangarbestand«:** 19 Sätze
stehen darauf, **alle 19** stehen in einer Hangarliste, von den 161 nicht
gelisteten **keiner**. ⚠ Die Umkehrung gilt nicht — 11 der 30 gelisteten sind
unterwegs. Die Liste ist der **Bestand**, nicht »steht gerade da«.

Arten: 1 `Shark` (Jagd), 2 `Whale` (Bomber), 10 `Fight` (Kampfheli), 13 `Fuel`,
14 `Ammo`. Besitzer `+0x09` bei 189/189 im Bereich 0…7.

**`sec4` — der Verdacht war FALSCH, und die Widerlegung hat ihn aufgeschlossen.**
Die Vermutung lautete: sec4 beginnt am Ende der Waldtafel sec18, ist also
vielleicht eine zweite Spalte dazu. ⚠ **Die Adressnachbarschaft ist sogar eine
Falle:** drei der 41 Fundstellen im sec4-Fenster sind gar keine sec4-Zugriffe,
sondern die **Schleifenenden von sec18** — genau weil sec18 dort endet.
Binderarithmetik, keine Bedeutung.

sec4 ist **2000 × 6** mit eigenem Indexraum, und das Spiel benennt es:
**`hori strom`** / **`dohorel strom`** (C `0x53968C` / `0x53967C`). ⭐ **sec18 sind
die Wald_felder_, sec4 die einzelnen Objekte** — bedient von derselben Routine:

```
v = imap[X·256+Y]
if 50000 <= v < 56000:  sec18[v-50000].Zustand = 1    ; brennender Wald aus
if 61000 <= v < 64000:  sec4[v-61000].Zustand = 0     ; brennendes Objekt aus
```

⭐ **Damit ist das imap-Band `61000 + n` benannt** — es stand hier als
»`≥ 60300` · 842 · ⚠ noch unbenannt«.

| Prüfung | Treffer | Nullmodelle |
|---|---|---|
| Kachel der Karte im Band `10000+T … +9` | 1931/1961 = 98,5 % | Zufallszelle **0,15 %** · Nachbarzelle 0,10 % |
| **Rückschluss imap → sec4** | **1913/1913 = 100 %** | vertauscht **0** · Index ±1 **0** · Zufallszelle **0** |

⚠ **Andere Konstanten als beim Wald, und das ist wichtig:** Klasse 0 zündet bei
Schaden **> 80 / 41…80 / 21…40 (⅓) / 11…20 (⅙) / ≤ 10**, setzt
`Zustand = rand()%106 + 150` und zählt **abwärts**. Der Wald hat 70/46/23/13 und
zählt **aufwärts**. Die zwei Tafeln teilen die Mechanik, **nicht die Zahlen** —
wer die Einzelobjekte baut, darf die Waldwerte nicht wiederverwenden.

⚠ **Berichtigt:** `GAMESTATE_RE.md` §3 führte sec4 als »Map Markers, Start-/
Zielmarken je Spieler«. Die »fünf erwarteten« auf `map_01` waren eine Koinzidenz;
`4.DM` führt elf.

### AL.4 ⭐ Die 22 kleinen — 14 gedeutet, 4 teilweise, 2 offen

Fundstellenzahl in **C und F bei allen 22 gleich**.

#### ⭐⭐ Die Missionsuhr ist vollständig

```
Takt++ → sec118 ++, Umbruch bei 250
   → sec64/65/66 ++
   → sec115 ++, Umbruch bei 60
      → sec116 ++, Umbruch bei 24
         → sec117 ++
```

`0x4CF570` (C) / `0x4CF110` (F), befehlsgleich: **`sec115 + 60·sec116 +
1440·sec117`** — das ist `game_time()` mit seinen 288 Aufrufen. Also
**Minuten / Stunden / Tage**. Das Spiel schreibt es selbst:
`'Missionszeit : '` + Tage + `' Tag, '` / `' Tage, '` + Stunden + Minuten.

⭐ **Eine Spielminute = 250 Takte = 5,00 reale Sekunden** — dieselbe Zahl, die am
Nachfrist-Fenster mit der Stoppuhr gemessen wurde. Ein Spieltag = 2 reale Stunden.

**sec65/sec66** sind **derselbe Minutenzähler, dreifach geführt**, seit
Programmstart und nie zurückgesetzt: je vier Fundstellen (ein `inc`, Speicherer,
Lader), **kein Leser**. `sec64 == sec65 == sec66` in 13/13.

#### ⭐ sec124 — die Statistiktafel, und die Rechnung geht auf

8 Spieler × 4 Klassen × (zerstört / verloren), Überschriften **Bewaffnete ·
Unbewaffnete · Schiffe · Flugzeuge**. Probe in `1.DM`: die Klassensummen
reproduzieren `sec53 +0x20` (Kills) und `+0x24` (Verluste) — **8 von 8 Spielern,
kein Ausreisser**.

⚠ **Die Schiffsspalte hat in beiden EXE keinen Schreiber** — untergegangene
Schiffe erscheinen in der Statistik nie.

#### Die übrigen

| Abschnitt | Deutung | tragende Zahl |
|---|---|---|
| **sec128** | »Gebaute Einheiten« des lokalen Spielers | drei `inc`, je nur wenn der Bauende der lokale ist |
| **sec75/76** | je Spieler: Entwurf und Klasse der zuletzt gebauten Einheit, **einmal abholbar** (`take_flag`) | Klasse gegen `sec47+0x18 < 150`: **32/34** |
| **sec79** | 100 × 8 — **RAWMAT**-Sätze (`'Cannot add new rawmat'`) | ⭐ benennt **Einheitenfeld +0x40 = Rawmat-Platz** |
| **sec96/97** | 10 × 16 laufende **Forschungen** (`'Too many researches'`) + Losnummer | sec97 ≠ 0 **genau** in den 5 Dateien mit sec96-Satz: 13/13 |
| **sec87** | 50 × 4 — »gr_ins to fix«-Warteschlange | 3 Reste, alle mit `Einheit+0x04 == 0xFF`: **3/3** |
| **sec90** | 20 × 32 — die **Handelsschiffe** (`'More mer_ships needed'`) | `8.DM`: x = 183 bei W = 180 — knapp ausserhalb, wie einlaufend |
| **sec92/93** | zahlender Spieler und Gebäude je Nachschubplatz | in 13/13 vollständig 0 — reiner Laufzeitzustand |
| **sec113** | 400 × 6 — Rauch/Staub; `+0x02` = **15 × Geländehöhe** | 85/85 mit x < W und y < H; `+0x02` nur 0/15/30/45 |
| **sec88** | 50 × 4 — Flächenwirkung mit Radius 6, jeder dritte Takt | ⭐ Nebenertrag: **`0x4222C0`** — bisher als offen geführt — ist der sec114-Erzeuger |
| **sec69** | 8 × 100 × 6 Ziellisten — **bestätigt** | Typbyte in allen belegten Sätzen = 1 |
| **sec121** | 240 × u16, y der Waggons — **bestätigt** | `Waggon+0x01 == sec121[i]/2` bei **424/424** |

#### ⚠ Zwei bleiben offen

**`sec77`** (8 B): fünf Fundstellen, darunter die Missionsvokabel `0x4D0970`
(`byte[Basis + arg1] = arg0`), die hier ohnehin als offen steht. **Kein Leser.**
⚠⚠ **BERICHTIGT am 21.08.2026 (AW.2): DIESE ZAHL TRAEGT FAST NICHTS.** Von den
76 Plaetzen mit `sec77[p] == 0` erfuellen **63** dieselbe Bedingung — **das
Nullmodell ist 83 %, nicht 0 %.** 28/28 darunter hat eine
Zufallswahrscheinlichkeit von 2,3 %: ein Streifschuss, keine Entdeckung.
⭐ Was sec77 wirklich ist, steht in AW.2 — die dritte Skriptsperre, **ohne
einen einzigen Leser**.

~~⭐ `sec77[p] == 1 ⇒ sec53[p].Zustand == 1` (aktive KI) in **28 von 28**~~ — geprüft
und **verworfen** wurden »hat Gebäude« (widerlegt), »hat Einheiten«, »hat Ziele«.
Der Leser läuft mit Sicherheit über ein Register: sec57 liegt lückenlos dahinter,
und `esi−8` sieht die Relokationstafel nicht.

**`sec67`** (8 B): vier Fundstellen (Speicherer, Lader, zwei Nullungen), **0 in
13/13 Dateien**. Eines von sechs 8-Byte-Feldern, die der Lader im selben Muster
paarweise nullt. Für den Nachbau: acht Nullbytes lesen und schreiben.

### ⚠⚠ Ein Fehler in UNSEREM Werkzeug, von einem der Läufe gefunden

`reloc_refs.py` zählte `movsx reg, word ptr […]` als **SCHREIBT**. Die Ursache
war `mnemonic.startswith("movs")` — `movsx` und `movsd` fangen so an, sind aber
keine Zeichenkettenbefehle und **lesen nur**. Bei `sec121` waren von »6
Schreibstellen« nur **2** echte.

Berichtigt: `startswith("rep")` bleibt, dazu eine **Aufzählung** der echten
Zeichenkettenbefehle. Gegenprobe: sec121 meldet jetzt 2 statt 6, und der
Selbsttest an sec58 bleibt bei 2 Schreibern / 4 Lesern.

⚠ Gefunden hat es der Lauf, der die Zahlen **nachgerechnet** hat, statt sie zu
übernehmen. Genau dafür steht in jedem Auftrag, jede Fundstelle einzeln zu
zerlegen — und es ist der zweite Werkzeugfehler an einem Tag, den erst ein
Selbsttest sichtbar gemacht hat.

### ⚠ Wodurch alle vier Läufe blind sind

* **Berechnete Adressen.** `mov esi, Nachbar; mov al, [esi−8]` erscheint unter
  dem **Nachbarn**. Bei `sec77` und `sec67` ist genau das die wahrscheinlichste
  Erklärung für »kein Leser«.
* **Zeiger über den Stapel.** Bei `sec88` im Fund belegt: die Basis erscheint
  **einmal**, danach läuft die ganze Schleife registerrelativ und kein einziger
  Feldzugriff ist sichtbar.
* **Blockbefehle.** Alle 22 kleinen werden vom Kurzweg des Laders per
  `rep stosd` genullt; Einzelfelder sind darin unsichtbar.
* **Nur 13 Dateien**, und `3/5/6/7/10.DM` sind offensichtlich Abkömmlinge
  derselben Karte — »13 von 13« wiegt dort weniger, als es aussieht.
  `sec124` und `sec128` hängen an **einer** Datei.

---

## AM. ⭐⭐ SIEBZEHN UNGELESENE MECHANIKEN — die »Zuteiler« sind gefunden (21.08.2026)

Die 118 Funktionen, die das Spiel selbst benennt und die bei uns nirgends
standen, enthielten eine ganze Klasse: **Zuteilerfunktionen** (`add_*`). Jede
sucht einen freien Platz in einer Tafel, und **die Fehlermeldung nennt die
Schranke**. Alle 17 Marken in C und F gefunden, alle 17 Funktionspaare zerlegt.

⚠ **»Test shooting unit« hatte zuerst keinen Treffer, weil die Zeichenkette ein
Leerzeichen am Ende trägt** (`"Test shooting unit "`). Wer ohne das sucht, findet
nichts und hält es für einen Negativbefund.

⚠ **Und eine Falle, in die der Lauf fast gefallen wäre:** bei »There is no free
place for the ramp« stimmen nur 29 von 64 Befehlen zwischen C und F überein — das
sah nach einem dritten Verhaltensunterschied aus. Zeile für Zeile verglichen hält
F nur einen Wert in `esi` statt auf dem Stapel; **alle Konstanten sind gleich**.
**Ein Befehlszähler-Unterschied ist kein Verhaltensunterschied.**

### AM.1 Minen und Fallen — je 500 Plätze

| | Tafel C | Abschnitt | Satz | Plätze |
|---|---|---|---|---|
| Minen | `0x552E18` | **sec84** | 6 B | **500** |
| Fallen | `0x688B58` | **sec85** | 6 B | **500** |

`+0x02 = rand()%20 + 10`, `+0x03 = rand()%10 + 5`, `+0x04` belegt, `+0x05` Spieler.

| Prüfung (877 Minensätze) | Treffer | Nullmodell |
|---|---|---|
| `+0x02` in 10…29 | **877/877** | dieselbe Stelle in **freien** Sätzen: **3,5 %** |
| `+0x03` in 5…14 | **877/877** | freie Sätze: **3,5 %** |
| imap-Tor erfüllt | **877/877** | Zufallszelle: **44,0 %** |

⭐ **Die Kampagnenmissionen verminen ihre Karten per Skript:** 17 der 18
Minen-Aufrufstellen stehen in `mission_init`. Der eine übrige steht in
`move units` — eine Einheit legt selbst eine Mine.

⚠ Die 877 stehen in nur **fünf** Dateien derselben Kartenfamilie. »877 von 877«
wiegt hier weniger, als es aussieht; die Bandbreiten sind aber Byte für Byte der
Würfel im Code und davon unabhängig.

### AM.2 ⭐⭐ »terra« — zwei Tafeln, und wir lesen eine davon falsch

| | Tafel C | Abschnitt | Satz | Plätze |
|---|---|---|---|---|
| **terra_place** (Erz im Boden) | `0xBC6D40` | **sec78** | 6 B | 50 |
| **terra** (aufgeschlossenes Vorkommen) | `0x6783E8` | **sec38** | 14 B | 50 |

⭐ **Die Mission legt Erz in den Boden, eine EINHEIT schliesst es auf.**
`add_terra` hat über die ganze Vollerhebung **genau einen** Aufrufer:
`move units` `0x408717`.

Beim Aufschliessen wird ein 3×3-Feld gesetzt: Kacheln `10240 + 3·ze + sp`,
imap `0xFFFF`/`0xFFFE` nach festem Muster. **27 von 27 Zellen, 3 von 3 Sätzen.**

⚠⚠ **Und damit ist ein Feld bei uns falsch gedeutet.** `CwmExtra.cs` führt
sec38 `+0x06`/`+0x07` als *»Artmarke (genau eine der beiden steht auf 1)«*. Es
sind **acht Byte, eines je Spieler**: der Zeichner liest
`byte[+0x06 + byte[0x4FA284]]`, und `0x4FA284` ist der lokale Spieler.
**`+0x06+p` heisst »Spieler p sieht dieses Vorkommen«**, und der Zuteiler setzt
nur das Byte des Aufschliessers. Dass in den Daten »eines von zweien« auf 1
steht, liegt allein daran, dass nur Spieler 0 vorkommt.

### AM.3 Die übrigen fünfzehn, gerafft

| Mechanik | Tafel C | Satz × Plätze | Kern |
|---|---|---|---|
| **Selbstverteidiger** | `0x53D8D8` | 6 × 200 | ein Eintrag je Opfer, `+0x04 = 20` Takte; nur wenn das Opfer untätig, ohne Ziel und bewaffnet ist |
| **Gaswerfer** | `0x833870` | 8 × 50 (**sec83**) | Waffenart **9**; `NABYTO = 120` Takte Sperre; `+0x00 = 7` Wolken, Richtung aus der Windtafel |
| **Teleport** | — | — | ⭐ **Befehlsnummer 23**; bei einer Infanteriezelle werden **alle 9 Mann** einzeln umgesetzt |
| **Rampe** | `0xC2FCB8` | 4 × 50 (**sec21**) | ⭐ macht **rauhes Gelände befahrbar**: Vorbedingung imap `0xFFFD`, danach `0xFFFE` |
| **Bahnstation / Depot** | `0x879178` / `0x879F38` | 14 × 50 (**sec30** / **sec25**) | je eine Warteschlange von **sechs** Robotern, nicht »Kopf + Zustand« |
| **Auswahlliste** | `0x833098` | 2 × 1000 | ⚠ **schreibt zuerst und prüft danach** — siehe unten |
| **Infanteriezelle** | `0x7847E8` | 22 × 4000 (**sec16**) | höchstens **9** Mann |

⭐ **Die Rampe beantwortet eine offene Frage aus Abschnitt S:** wozu sie zählt.
Sie ist das Bauwerk, mit dem eine Einheit **grobes Gelände befahrbar macht**.
57 belegte Sätze, `+0x03 == 200` in 57/57; Kachel `== 10723 + art` in 36/57, und
⚠ **die 21 Fehlschläge sind Editorabfall**: alle in `08/09/NET07.CWM`, wobei `09`
dasselbe wie `08` ist, nur mit weggeputzten Kacheln bei stehengebliebener Tafel.
Ohne diese drei: **36 von 36 = 100 %**.

⚠⚠ **Die Auswahlliste ist der einzige der 17 Zuteiler, der NICHT schützt.** Der
Eintrag landet auf Index `anzahl`, die Anzahl steigt, und **erst dann** meldet
`cmp ax,0x3e8` »Too many units in this group«. Ab 1001 schreibt sie in **sec117**
hinein und meldet gar nichts mehr. ⭐ Die 1000 ist doppelt belegt: im Code **und**
weil `0x833098 + 2·1000 = 0x833868` **auf das Byte genau** der Anfang von sec117
ist.

⭐ **Nebenertrag — der Griffraum ist vollständig:** ein Listenwert < 8000 ist ein
Einheitenplatz, ein Wert **≥ 20000 ein FLUGZEUG**. Belegt über
`0x591EF0 + 68·20000 = 0x6DDF70` = sec19, auf die Adresse genau.
⚠ **Warnung:** `0x591EF0` sieht wie eine eigene Tafel aus, ist aber nur der
**vorgespannte Versatz** einer Tafel 1,3 MB weiter. Wer sie als Tafelanfang
deutet, deutet einen Übersetzertrick.

### AM.4 ⭐ »Test shooting unit« — die Zielsuche folgt einer TAFEL, nicht dem Abstand

| Tafel | C | Inhalt |
|---|---|---|
| Zellenzahl je Reichweite | `0x4F5B28` | `0, 8, 20, 36, 56, 88, 136, 0` |
| Abtastfolge | `0x4F5B38` | `(0,0) (−1,0) (0,−1) (1,0) (0,1) (−1,−1) …` |

Die Folge ist nach Entfernung sortiert — **es gewinnt der erste Treffer in dieser
Reihenfolge**, nicht der nächste.

⚠ **Bei uns ist das anders gebaut:** `AutoAcquire` geht alle Einheiten durch und
nimmt die **kürzeste Entfernung**; die beiden Tafeln kommen in `Scripts/` nirgends
vor. Bei gleichweit entfernten Zielen entscheidet im Original die Tafelreihenfolge,
bei uns die Listenreihenfolge — **genau die Art Naht, an der ein Gleichlauf reisst.**

### ⚠ Der Kern für den Nachbau

**Von diesen siebzehn Mechaniken hat unser Bau keine einzige vollständig.** Fünf
Tafeln werden importiert (sec16, sec21, sec37, sec38 — eine davon mit falsch
gedeutetem Feld), **sieben sind gar nicht angeschlossen** (sec25, sec30, sec48,
sec78, sec83, sec84, sec85), und die Zuteiler gibt es nirgends.

---

## AN. ⭐⭐ DIE FORSCHUNG IST VOLLSTÄNDIG GELESEN — und unsere beruht auf einer falschen Prämisse (21.08.2026)

### AN.1 Der Fahrplan in einem Satz

⭐ **Die Forschung schaltet nichts frei.** Sie tut zwei Dinge: sie **verbessert
die Zahlen eines Bauteils, das man schon besitzt** (Stufe 0…9), oder sie
**erfindet eine neue Waffe** aus 40 Rezepten. Und **der Preis IST die Dauer**:
`+0x02` des Laufsatzes ist der Preis in $, `+0x04` steigt **um 1 je Takt**.

| | 500 $ | 2000 $ | 5000 $ |
|---|---|---|---|
| | Kleine | Mittlere | Große Forschung |
| bei 50 Takten/s | **10,0 s** | **40,0 s** | **100,0 s** |

### AN.2 Der Preis, ausgerechnet

```
Grundpreis = LEITER[Mission]                       ; 100…1500
Wert       = sec46[ Bauteil ≥ 0xA0 ? +0x21 : +0x20 ]
Preis      = pow(3.2, sec46[+0x24] − Grundpreis/100 + 1) · pow(2.3, Stufe) · (Wert/2) · 10
Preis      = min(Preis, 30000)
```

**`+0x24` ist die Techstufe und schliesst das Tor** (`3.2^Stufe`), **die
Missionsleiter öffnet es wieder**, und **jede Stufe kostet ×2,3**. Damit kostet
die Kanone in Mission 0 224 $, der Laser 30 000 $ — und in Mission 33 alles 1 $.

⚠ **Das Vorzeichen hängt an einem Befehl** (`DE E1 = FSUBRP`). Kippt es, wird aus
dem Missionsrabatt ein Aufschlag und fortgeschrittene Bauteile wären billiger als
die Kanone — als Entwurf unsinnig. Belegt ist die Kodierung; die Spielsicht
stützt sie nur.

### AN.3 ⭐ »Too many researches« steht dreimal — und es sind KEINE drei Schlangen

Zwei der drei Wächter bewachen dieselbe 10er-Tafel. Und beide sind **tot**, weil
davor eine echte Spielregel steht: beide Funktionen laufen **zuerst** über alle
10 Laufsätze und **löschen jeden, der demselben Spieler gehört**.

> ⭐ **Ein Spieler kann genau EINE Forschung laufen haben. Eine neue bricht die
> alte ab — ohne Rückzahlung.**

### AN.4 ⭐ `for_vyv` und `next_rand` sind dieselbe Sache

`for_vyv` (tschech. *pro vývoj*, »für die Entwicklung«) ist 40 Byte lang und tut
eines: `AUFWERTUNG[i].+0x02 = rand() & 3`. Gerufen **genau einmal**, unmittelbar
bevor die neue Stufe eingetragen wird.

⭐ **`next_rand` ist die vorgewürfelte Nebenwirkung der NÄCHSTEN Stufe.** Jede
Stufe verbessert immer den Hauptwert und **einen von vier** Nebenwerten — welchen,
steht schon fest, bevor der Spieler kauft. Das Angebot zeigt darum eine ehrliche
Vorschau.

### AN.5 ⭐⭐ Die Erfindung — und der stärkste Beleg des ganzen Tages

40 Rezepte à 70 B bei `0x502B00`; die Tafel endet **auf das Byte genau** dort, wo
die Aufwertungstafel beginnt. Vier Namensvarianten je Rezept = **160 Namen**.

Der Ablauf würfelt aus der gemeinsamen **Losnummer** ein Budget und drei Rezepte,
mischt deren Werte und hängt bei einem Namen auf `-` zwei Ziffern an.

⭐ **Prüfstand »Hiff-64«:** drei `.DM` tragen in Zeile 20 eine erfundene Waffe.
Zurückgerechnet stimmen **11 von 11 Feldern** — darunter zwei nur dann, wenn man
den Code *einschliesslich seiner Schrulligkeiten* liest: die
Reichweiten-Nachkorrektur, und dass Rezept 3 im Wert **doppelt** zählt.
Nullmodell: vier Rezepte aus 40⁴ = 2,56 Mio. Möglichkeiten, und alle elf Felder
treffen.

### AN.6 ⭐ Die KI forscht NICHT — sie erbt

`enemy_upgrade`, gerufen aus `mission_init`:

> ⭐ **Die KI-Spieler besitzen immer exakt das, was der Mensch besitzt** (Feld
> `+0x00` wird jede Mission neu von Scheibe 0 kopiert).
> ⭐ **Jede Mission setzt alle KI-Aufwertungen auf 0 zurück** (`PARTS.CWD` wird
> neu geladen).
> ⭐ Dann bekäme jeder KI-Spieler je Bauteil `n` bis `2n−1` Stufen.

⚠⚠ **Aber die Schraube steht auf Null.** `n = (byte + 10) / 15` mit einem Byte,
das in **allen drei verfügbaren Beständen 0** ist. **Die KI wird im
ausgelieferten Spiel niemals aufgewertet.** Der Mechanismus ist vollständig
gelesen; **belegt ist er nur als schlafend.**
⚠ Wir haben genau **eine** `PARTS.CWD` — »immer 0« ist damit nicht gezeigt.

### AN.7 ⚠⚠ Was unser Nachbau falsch macht

| Original | Nachbau heute |
|---|---|
| Forschung **schaltet nichts frei**, sie verbessert Zahlen | `_researchedStatic.Add(tech)` — sie **schaltet frei** |
| Ziele sind die **35 Bauteile** der Aufwertungstafel (`0x01`…`0x13`, `0xA0`…`0xAF`) | Ziele sind **Zeilen 65…88** — ⚠ die kommen in der Aufwertungstafel **überhaupt nicht vor** |
| **Stufe 0…9** je Bauteil je Spieler | fehlt |
| Preis aus der Formel oben | `ResearchCost = 2000`, fest, selbst erfunden |
| Gesamtaufwand **= Preis**, `+1` je Takt | `ResearchTotal = 5000` fest, `ResearchRate = 60` |
| **eine Forschung je Spieler**, neue bricht alte ab | hängt am **Gebäude**, beliebig viele parallel |
| Erfindung aus 40 Rezepten | fehlt |

⭐ **Immerhin eine Zahl stimmt zufällig:** `GameSounds.ResearchDone = 136`, und
der Klang des Originals ist `0x88` = 136.

⚠ **Nicht gebaut, nur aufgeschrieben.** Die Zeilen 65…79 bleiben als
**Ausrüstungen** im Entwurfsschirm richtig — sie sind es, und `EquipMountOrder`
ist heute unabhängig belegt worden. Falsch ist allein, sie als **Forschungsziele**
zu führen.

### ⚠ Zur Sicherheit: eine »Berichtigung«, die keine war

Der Lauf meldete, `UNIT_STATS_RE.md` rechne mit der falschen sec46-Basis
`0x5045BA`. **Dort steht die Berichtigung seit dem 10.08.2026.** Neu ist der
zusätzliche Beleg: `PARTS.CWD` ist **exakt 92 800 B = 1600 × 58**, und ihre
Scheibe 0 stimmt mit dem `.data`-Block ab `0x5045A0` in **11 600 von 11 600 Byte**
überein.

---

## AO. ⭐⭐ GESCHOSSE, LASER UND ZWEI NEUE UNTERSCHIEDE DER AUSLIEFERUNGEN (21.08.2026)

### AO.1 Die Geschosstafel

`0x884730`, **32 B × 1000 Plätze**, frei = `+0x06 == 0xFF`. Ist keiner frei, kehrt
der Anleger **wortlos** zurück — das Geschoss fällt aus.
⭐ Der Satz bei `0x88C430` (der 1001.) ist kein Geschoss, sondern eine Kladde —
darum ist **sec43 32 032 B** und nicht 32 000.

⭐ **Drei Bahnarten, nicht zwei:** gerade Bahn · Wurfbahn (**22 Arten**) · und
**nur für Art 7** (Mittelstreckenrakete) eine eigene: steigt, deckelt bei Höhe
150, fällt dann 7 je Takt. Die 22 stimmen exakt mit der unabhängigen Tafel
überein, die den Scheitelpunkt auf 1/11 oder 1/2 setzt.

⚠ **Es gibt keinen »fliegt aus der Karte«-Tod** — ein Geschoss verschwindet nur
durch einen Einschlag. Negativbefund aus der Vollerhebung, nicht aus einer Suche.

### AO.2 ⭐ Die Waffentafel benennt sich selbst — und `Art = ZBRAN − 1`

19 Waffen mit Namen aus der EXE. ⭐ **Die vier mit ganz leerer Arttafelzeile**
(Gaswerfer, Minenleger, Fallenleger, Membranbombe) sind **genau die vier**, die
der Takt als »Wrong type of missile« abweist. Ein Nullmodell, das in beide
Richtungen aufgeht.

### AO.3 ⭐⭐ Der LASER — zwei Dinge, und eines fehlt uns ganz

**Der Fahrzeuglaser** (ZBRAN 10/11) ist ein gewöhnliches Geschoss und braucht
**keinen neuen Code**, nur die richtigen Tafelwerte.

**Der Roboterlaser ist ein STRAHL**, und den haben wir nicht: Tafel `0x87B448`,
**44 B × 200 Plätze**, mit `Place laser` als Anleger und `kresli_laser2` als
Takt- und Zeichenschritt.

⚠ **`kresli_laser1` und `kresli_laser2` sind KEINE zwei Zeichner.**
`kresli_laser1` ist 23 Befehle lang und tut nichts als
`for i in 0..199: if aktiv: kresli_laser2(i)`. Die Vermutung »zwei Zeichner ⇒
zwei Waffenarten« trägt nicht.

Der Strahl wächst je Takt um 20 oder 40 Bildpunkte (40 beim Flugzeug), Farbe 73
oder 74, und schlägt am Ende ein.

| Waffe | Einheiten auf den 36 Karten |
|---|---:|
| 10 »Laser« | **230** |
| 11 »Zwillingslaser« | **131** |
| 191 »Laser« (S-Infanterie) | **328** |
| 194 »LaserXXL« | ⚠ **0** — Col. Hullman kommt auf keiner Karte vor |

**689 Lasereinheiten auf 17 der 36 Karten.** Das ist keine Randerscheinung.

⭐ Nebenertrag: **`+0x43` ist der Musterindex** in sec47 — nachgemessen an
**2025 von 2025** bewaffneten Landeinheiten, kein Gegenbeispiel. Das bestätigt
Abschnitt AE-3 aus einer zweiten Richtung.

### AO.4 »Place laser:« ist keine Anlage

*Place* heisst **eintragen**, nicht *aufstellen*. Es gibt kein Gebäude dieses
Namens; der **»Laserturm«** ist Muster 103 und schiesst gewöhnliche Geschosse.

### AO.5 ⭐ Zwei Fenster, zwei Zwecke

| Marke | Fensterart | was es ist |
|---|---|---|
| **Market window not found** | **33** | »Geschäftszentrum« — gebrauchte **Einheiten** |
| **Store window not found** | **31** | »Angebot des Nachschubpostens« — **Treibstoff- und Munitions-Helikopter** |

Beide Funktionen **schliessen** ein Fenster; die Zeichenkette ist nur die Meldung
des Fehlschlags. Ausgelöst, wenn rund um die Tür keine eigene Einheit mehr steht.

### AO.6 ⚠⚠ ZWEI NEUE Verhaltensunterschiede zwischen C und F

Damit sind es **vier** insgesamt (nach sec59 und der Partikel-Zeilenschranke).
Beide neuen stecken im **Schuss**:

**(a) C verbietet den Schuss von Geländeklasse 99.**
```
C 0x40BB17 :  cmp bl, 0x63 ; je -> return 0      ; KEIN SCHUSS
F 0x40BA07 :  fehlt vollständig
```
Die 99 ist die **Türzelle**, die heute früh gemessen wurde. In C kann eine
Einheit, die dort steht, nicht feuern; in F kann sie es.

**(b) C putzt Geisterbelegungen aus der imap.** Ist das Opfer längst tot,
ruft C eine eigene 73-Byte-Funktion, die **alle 65 536 imap-Zellen** durchgeht
und jede zurücksetzt, die noch diese Platznummer trägt. ⚠ **In F gibt es diese
Funktion überhaupt nicht** — gesucht mit und ohne angepasste Basis, kein Treffer.
Eine nachträgliche Fehlerbehebung in C gegen stehengebliebene Blockaden.

---

## AP. ⭐⭐ DAS KARTENFENSTER — sechs Betriebsarten, und wir haben nichts davon (21.08.2026)

Die fünf »Planungsschirme« sind **keine fünf Fenster**, sondern **sechs
Betriebsarten EINES Fensters** — der Fensterart 3 »Karte«. Die sechste heisst
**»Einheiten-Transport Planung«** und stand nicht auf der Liste.

| Art | Marke | was sie tut |
|---:|---|---|
| 0 | **Einsatzkarte** | »Hier klicken, um die Hauptansicht zu verschieben« |
| 1 | **Verknüpfungskarte** | Bahnstrecke anklicken → Einstellungsfenster |
| 2 | **Luft-Einsatzplanung** | »Ziel anwählen« für ein Flugzeug |
| 3 | **Raketen-Einsatzplanung** | Mindestreichweite, 13×13-Zielsuche, Befehle 2 und 9 |
| 4 | **Materialtransport Planung** | Start- und Zielgebäude, **Befehl 512** |
| 5 | **Einheiten-Transport Planung** | Verbindungsprüfung, bis zu 6× **Befehl 518** |

**Drei Zoomstufen** (1, 2, 3 Bildpunkte je Zelle), Aufruf über **Taste M** und aus
sechs anderen Fenstern heraus. ⭐ **C und F sind hier verhaltensgleich** — kein
dritter Unterschied.

⭐⭐ **Der Kartenmaler liest ausschliesslich die GEMERKTEN Felder** (sec50, die
gemerkte Lage, sec52) — die Karte zeigt Erinnerung, nicht Wahrheit. Hilfszeile
und Klick lesen dagegen die **lebenden** Felder. Eine saubere Trennung, die so
noch nicht aufgeschrieben war.

⚠ **Ein Befund, der codeseitig sicher und datenseitig unbestätigt ist:** trägt
eine Zelle in sec20 einen Wert **1…59**, so ist das nach dem Befehlsstrom ein
einsbasierter Platz in sec34 (die Rechnung `214·v` ist eindeutig). Der Vergleich
mit den Endzellen scheitert aber: **0 von 752**. Entweder sind `+0x02…+0x05`
anders kodiert, oder die 1…59 bezeichnen in den Dateien noch etwas anderes.
**Der Code ist eindeutig, die Datei bestätigt ihn nicht** — das gehört so
stehengelassen.

Die Streckendeutung selbst trägt dagegen: `sec34[+0x00]` → sec33 → sec3 ist eine
**Bahnstation oder ein Feldbahnhof** in **224 von 371 = 60,4 %**, gegen ein Mittel
über **alle 214 Versätze** von **1,1 %**.

⭐ **Zwei Tafeln, die wir noch nicht führen:** die 16 **Gebäudenamen des Spiels**
(Basis, Waffen-Fabrik, …, Werft-Station) und die **Verträglichkeitstafel des
Materialtransports** (welche Quelle zu welchem Ziel darf). Beide liegen fertig in
beiden EXE.

⚠ **Unser Nachbau hat davon nichts.** `Minimap.cs` baut eine **stehende** Minimap
und begründet das damit, das Original habe keine. Das stimmt — es hat stattdessen
**dieses Fenster**.

---

## AQ. Adressverzeichnis zu den Abschnitten AM…AP (21.08.2026)

⚠ **Warum dieses Verzeichnis nötig ist.** Die Abschnitte AM…AP fassen vier Läufe
zusammen, und beim Zusammenfassen sind die meisten **Funktionsadressen**
weggefallen — der Text las sich besser und war schlechter zu benutzen. Aufgefallen
ist es an `funktionen.py`: die Quote »bei uns erwähnt« stand nach dem Anhängen
unverändert bei 361 von 1107. **Eine Zusammenfassung ohne Adressen ist eine
Erzählung, kein Nachschlagewerk.**

Alle Paare C / F, beide Auslieferungen geprüft.

### Zuteiler und Mechaniken (AM)

| Was | C | F |
|---|---|---|
| Mine legen | `0x421940` | `0x420B00` |
| Falle legen | `0x421A40` | `0x420C00` |
| terra aufschliessen | `0x420E20` | `0x41FFE0` |
| terra_place (Missionsvokabel) | `0x4D0A10` | `0x4D05C0` |
| Selbstverteidiger | `0x411770` | `0x411540` |
| Teleport (Befehl 23) | `0x43A420` | `0x439590` |
| Gaswerfer | `0x439B30` | `0x438C90` |
| Gaswolkentakt | `0x439C50` | — |
| Was unter Infanterie liegt | `0x4125F0` | `0x4123C0` |
| Infanterieschritt | `0x4052D0` | `0x4052B0` |
| Zielsuche »Test shooting unit « | `0x40F0A0` | `0x40EED0` |
| STOP TRANS (Befehl 12/29) | `0x4103B0` | `0x4101E0` |
| ST TRANS (Befehl 514) | `0x4108D0` | `0x410700` |
| Roboter in Bahnstation | `0x43C370` | `0x43B500` |
| Roboter in Depot | `0x43C630` | `0x43B7C0` |
| rob_trans-Gegenprobe | `0x436260` | `0x4353C0` |
| Auswahlliste, Eintrag | `0x4344D0` | `0x433610` |
| Auswahlliste, Austrag | `0x4345D0` | — |
| in Infanteriezelle aufnehmen | `0x433B90` | `0x432CE0` |
| Rampe anlegen | `0x4CBEE0` | `0x4CBAA0` |
| Minentakt (liest `+0x05`) | `0x4216F0` | — |

**Tafeln:** Minen `0x552E18` (sec84) · Fallen `0x688B58` (sec85) ·
terra `0x6783E8` (sec38) · terra_place `0xBC6D40` (sec78) ·
Selbstverteidiger `0x53D8D8` · Gaswolken `0x833870` (sec83) ·
Rampen `0xC2FCB8` (sec21) · Bahnstation `0x879178` (sec30) ·
Depot `0x879F38` (sec25) · Auswahlliste `0x833098` ·
Zielsuche-Tafeln `0x4F5B28` und `0x4F5B38`.

### Forschung und Aufwertung (AN)

| Was | C | F |
|---|---|---|
| Angebot: Aufwertung | `0x4AA360` | `0x4A9C90` |
| Angebot: neue Forschung | `0x4AA890` | `0x4AA1C0` |
| Angebotstafel neu bauen | `0x4AA950` | `0x4AA280` |
| `for_vyv` | `0x4AAA20` | `0x4AA350` |
| Aufwertung anwenden (Befehl 531) | `0x4AAA80` | `0x4AA3B0` |
| Aufwertung beginnen | `0x4AABD0` | `0x4AA500` |
| freie Bauteilzeile suchen | `0x4AAE00` | `0x4AA730` |
| Erfindung abschliessen | `0x4AAE70` | `0x4AA7A0` |
| Waffe erfinden | `0x4AAF00` | `0x4AA830` |
| Forschungstakt | `0x4AB580` | `0x4AAEB0` |
| Forschung beginnen | `0x4AB830` | `0x4AB160` |
| läuft eine Forschung? | `0x4AB910` | `0x4AB240` |
| alle abfeuern (Missionsende) | `0x4AB950` | `0x4AB280` |
| KI-Aufwertung, einzeln | `0x4ABA80` | `0x4AB3B0` |
| KI-Aufwertung, Paket | `0x437CD0` | `0x436E30` |
| Bezahlknopf | `0x44A87D` | `0x449870` |
| freies Gebäude suchen | `0x4D5700` | `0x4D5290` |
| Markt: Einheit einstellen | `0x4C0D20` | `0x4C07E0` |
| gemeinsamer Zufall (Netz) | `0x4C5B30` | `0x4C56E0` |

**Tafeln:** Angebote `0xA39640` (100 × 50) · Rezepte `0x502B00` (40 × 70) ·
Aufwertungen `0x5035F0` (50 × 24) · Bauteile `0x5045A0` (sec46, 1600 × 58) ·
Grundpreisleiter `0x503AA8` · Techstufenleiter `0x503AF0` ·
Preise 500/2000/5000 `0x503B38` · Erfindungsbudget `0x503B80`.

### Geschosse, Laser, Handel (AO)

| Was | C | F |
|---|---|---|
| Geschoss anlegen (`add strela`) | `0x451B40` | `0x4507F0` |
| Geschosstakt / Einschlag | `0x452190` | `0x450E40` |
| Geschosse löschen | `0x451780` | — |
| Strahl anlegen (`Place laser:`) | `0x455320` | `0x453FC0` |
| Strahlenschleife (`kresli_laser1`) | `0x4554A0` | `0x454140` |
| ein Strahl (`kresli_laser2`) | `0x454CF0` | `0x4539A0` |
| Schussverteiler (Waffe → Zweig) | `0x40C8C0` | `0x40C780` |
| Schussroutine | `0x40BB00` | `0x40B9F0` |
| Trefferroutine `Zasah` | `0x40C9A0` | `0x40C800` |
| ⭐ imap-Putzfunktion | `0x40C940` | **fehlt in F** |
| Marktfenster schliessen | `0x4511D0` | `0x44FE80` |
| Nachschubfenster schliessen | `0x451270` | `0x44FF20` |
| Untermissionsliste (`SUB:`) | `0x451530` | — |

**Tafeln:** Geschosse `0x884730` (32 × 1000, sec43) · Geschossarten `0x4F98E8` ·
Bahnart-Index `0x4530DC`, Sprungtafel `0x453064` · Strahlen `0x87B448` (44 × 200) ·
Rauch/Teilchen `0x77CAE8` · Marktlager `0x82AA30` (sec94) · Preise `0x81A3A8` (sec95).

**Die zwei neuen Unterschiede:** Schussverbot auf Geländeklasse 99 @C `0x40BB17`
(fehlt in F @`0x40BA07`) · imap-Putzen @C `0x40C9A7` (fehlt in F).

### Das Kartenfenster (AP)

| Was | C | F |
|---|---|---|
| Fenster anlegen (Art 3) | `0x457730` | `0x4563D0` |
| Zeichner Art 3 | `0x464A20` | `0x463310` |
| ⭐ Kartenmaler | `0x4B7ED0` | `0x4B7810` |
| auf den Schirm bringen | `0x4409E0` | `0x43F9F0` |
| Befehlsbehandler | `0x4485D0` | `0x4475D0` |
| Hilfszeile | `0x447920` | `0x446910` |
| Hilfszeile setzen | `0x4501C0` | `0x44EE70` |
| Fenster schliessen | `0x4471A0` | `0x446170` |
| nach vorn holen | `0x44FC20` | `0x44E8D0` |
| **Einsatzkarte** | `0x444740` | `0x443720` |
| **Verknüpfungskarte** | `0x444A30` | `0x443A00` |
| **Luft-Einsatzplanung** | `0x444D90` | `0x443D50` |
| **Raketen-Einsatzplanung** | `0x4450F0` | `0x4440A0` |
| **Materialtransport Planung** | `0x445650` | `0x444600` |
| **Einheiten-Transport Planung** | `0x4459F0` | `0x444990` |
| Verbindungsprüfung | `0x4CE710` | `0x4CE2C0` |
| Streckenfenster öffnen | `0x445D70` | `0x444D00` |
| »There is no airplane selected« | `0x450310` | `0x44EFC0` |

**Tafeln:** Fenstersätze `0x8B9038` (44324 je Fenster) · Betriebsart `0x8C3CD8` ·
Zoomstufe `0x8C3D5B` · Zoomwerte `0x4FD610` · Gebäudenamen `0x4FDCB0` (16 × 20) ·
Verträglichkeitstafel `0x4FDC00` (10 je Zielart) · Befehlsnamen `0x4FD660` (30 B) ·
hervorgehobene Strecke `0x4FD63C` · Flugzeug in Planung `0x4FD640`.

### AQ.2 Nachtrag — schon gelesen, aber ohne Adresse geführt

⚠ Beim Durchsehen der »70 benannten, nie erwähnten« fiel auf: **neun davon sind
längst gelesen**, sie standen nur ohne Adresse im Text. Derselbe Fehler wie in
AQ, eine Ebene tiefer.

| Marke | C | wo es steht |
|---|---|---|
| Cannot add new rawmat | `0x438980` | AL.4, sec79 — benennt Einheitenfeld `+0x40` |
| Cannot remove rawmat | `0x438A10` | dieselbe Kette, Gegenweg |
| Too many gr_ins to fix | `0x43A8D0` | AL.4, sec87 |
| Wrong size of fly_part | `0x4AD520` | AL.1, sec112 — der Streuer |
| hori strom | `0x4C9FC0` | AL.3, sec4 — der Takt der Einzelobjekte |
| check_forest | `0x4CAB10` | Abschnitt AG, der Waldbrand |
| AI: transport - no target found | `0x4BB7D0` | Abschnitt AF — **die erste** C/F-Differenz (sec59) |
| Attack group not available | `0x4BC920` | AU.3, sec68 (⚠ nicht sec108) |
| Wrong type of tr.unit | `0x4B92E0` | Abschnitt R, die verlegte Einheit |

---

## AR. Die Frage-Dialoge — ein Fenster, eine Unterart (21.08.2026, selbst gelesen)

Aus den 61 verbliebenen benannten Funktionen das Bündel »Dialoge«. Sie sind
**alle derselbe Bau**, und das ist der ganze Befund.

### Der Bau

```
0x446270 (u. a.):
  1. die Liste der offenen Fenster durchgehen (0x87AFF8, Anzahl 0x4FD64C),
     je Eintrag 44324 Byte, Art bei +0x00 -> schon offen? dann nur nach vorn
  2. sonst: Fenster anlegen  ->  0x401B40 -> 0x458150
  3. UNTERART setzen:  word[Fenster + 0x8C3CD8] = n
  4. registrieren      ->  0x401AF0 -> 0x441270
```

⭐ **`0x458150` legt einen Kasten fester Grösse an: 0x12C × 0x64 = 300 × 100
Bildpunkte** (`mov word[…+0x8B903E], 0x12C` / `[…+0x8B9040], 0x64`).

### ⭐ `0x8C3CD8` ist die UNTERART eines Fensters, nicht »die Kartenbetriebsart«

Abschnitt AP hat das Feld als Betriebsart des **Kartenfensters** (0…5) gelesen.
Es ist allgemeiner: **jede Fensterart deutet es für sich**. Bei der Karte ist es
die Betriebsart, beim Frage-Dialog **welche Frage**:

| Funktion C | Text bei | Unterart | Frage |
|---|---|---:|---|
| `0x446780` | `0x4FC988` | **0** | Spiel beenden? |
| `0x446880` | `0x4FC9EC` | **1** | Einheitentyp löschen? |
| `0x446370` | `0x4FC7F8` | **3** | Möchten Sie die Mission erneut starten? |
| `0x446270` | `0x4FD0F4` | **5** | Möchten Sie die Mission beended? |
| `0x4424A0` | `0x4FC4D8` | **0** | Geben Sie Ihren Namen ein |
| `0x442590` | `0x4FC4D8` | **1** | derselbe Text, andere Unterart |

⭐ **Die zwei Namensdialoge tragen denselben Text und verschiedene Unterarten** —
die Unterart sagt also, **wofür** der Name ist. Das ist der Beleg dafür, dass das
Feld die Absicht trägt und nicht bloss das Aussehen.

⚠ Unterarten bis mindestens **10** kommen vor (`mov word[…+0x8C3CD8], 0xa`
@`0x442318`). Die vollständige Zuordnung **Unterart → Wirkung beim »Ja«** ist
**nicht** gelesen; sie sitzt im Befehlsbehandler und ist eine Sprungtafel weiter.

### ⭐⭐ Und damit schliesst sich ein Kreis zur Kontexthilfe

`0x401AF0 → 0x441270` ist die Funktion, die **jedes neu geöffnete Fenster
registriert** — und genau sie ist der **Schreiber des Ereignisbytes**
`byte[0x539930]`, an dem drei Tore der Kontexthilfe hängen (Abschnitt AE-2).

Das erklärt, warum das Ereignis bei **jedem** Fenster fällt und nicht nur bei den
drei, die ein Tor haben: es ist keine Sonderbehandlung, sondern die allgemeine
Registrierung. ⭐ **Unser Nachbau setzt `CampaignHints.Ereignis` beim Öffnen des
Gebäudefensters — das ist genau die richtige Stelle**, und jetzt ist belegt,
warum.

⚠ `0x41CDB0` (»Do you want to quit the game?«) folgt diesem Bau **nicht** — dort
steht ein anderer Weg. Ungelesen.

⚠ **Was mir dabei nicht gelungen ist:** die Wirkung der Unterarten beim »Ja«. Ich
habe den Verbraucher gesucht (20 Fundstellen auf `0x8C3CD8`), aber die
Lesestellen, die ich angesehen habe, sind alle die **Doppelöffnungs-Prüfung**
(»ist ein Fenster dieser Art UND Unterart schon offen?«). Der eigentliche
Verbraucher sitzt im Befehlsbehandler `0x4485D0` hinter einer Sprungtafel.

---

## AS. ⭐⭐ `options.cfg` — die zwölf echten Einstellungen, und zwei Berichtigungen an uns (21.08.2026)

Schreiber C `0x446F00` / F `0x445ED0` (`"wb"`), Leser C `0x447040` / F `0x446010`
(`"rb"`): **zwölf `fwrite`/`fread` in fester Folge**. Die Summe der Breiten ist
`4+4+4 + 8·1 + 4 = 24` — ⭐ **genau die Dateigrösse**. Keine Kennung, keine
Version, keine Textform.

| Vsz | Weite | C | Bedeutung |
|---:|---:|---|---|
| 0 | 4 | `0x500E10` | Lautstärke **Spiel** (0…255) |
| 4 | 4 | `0x892128` | **Geschwindigkeit** |
| 8 | 4 | `0x991818` | **Bildschirmmodus** |
| 12 | 1 | `0x991708` | **Meldungen** EIN/AUS |
| 13 | 1 | `0x9927C0` | **Autosichern**, Minuten (0 = AUS) |
| 14 | 1 | `0x500E18` | **Hilfe-Fenster** EIN/AUS |
| 15 | 1 | `0x8934C4` | **Hilfe-Sprache** EIN/AUS |
| 16 | 1 | `0x8B8068` | ⭐ **Formation / Gruppe** |
| 17 | 1 | `0x8B62A8` | **Pause bei Hilfe** EIN/AUS |
| 18 | 1 | `0x8934B8` | **MIDI-Musik** EIN/AUS |
| 19 | 1 | `0x8B7250` | **Scrollen** aktiv/passiv |
| 20 | 4 | `0x500E14` | Lautstärke **Sprache** (0…255) |

Die Beschriftungen stehen als Block beieinander (Dateiversatz `0x100140` ff.) und
benennen die Schalter selbst: `Gruppe Standard` · `Formation Standard` ·
`Hilfe-Sprache AUS/EIN` · `Hilfe-Fenster AUS/EIN` · `Autosichern ` + ` Min.` ·
`Meldungen AUS/EIN` · **`Auflösung`** · **`Geschwindigkeit`** · `Scrollen AUS/EIN`.

### ⭐⭐ BERICHTIGUNG 1 — das Original HAT eine Auflösungswahl

Ich hatte am selben Tag beim Bau der Auflösungseinstellung geschrieben:
*»das Original von 1997 lief in einer festen Auflösung (640×480) und kennt keine
Wahl«*. **Das ist falsch.** Es gibt eine Modustafel bei C `0x538858` (je zwei
u16), gesetzt von `Set 1` (C `0x4B68A0` / F `0x4B61D0`), gespeichert als Feld 8 —
und im Einstellungsschirm steht wörtlich **»Auflösung«**:

| Nr | 0 | 1 | 2 | 3 | 4 |
|---|---|---|---|---|---|
| | 640×480 | 800×600 | 1024×768 | **1280×1024** | 1600×1200 |

Immer 8 bit. `Setmode:` (C `0x4B69E0`) setzt sie über DirectDraw und **fällt bei
Fehlschlag auf 640×480×8 zurück**.

→ **Unsere fünf 4:3-Stufen sind keine Erfindung, sondern die des Originals** —
bis auf eine: wir hatten **1280×960**, das Original hat **1280×1024**.
Berichtigt. ⚠ Unsere Zutat bleiben die 16:9-Stufen, und dass wir eine
FENSTERgrösse setzen statt eines Vollbildmodus.

⭐ Nebenbei fällt aus `Setmode:` das **Kachelmass** heraus: `0x5387C0 =
Breite/40 + 1`, `0x5387C4 = Höhe/20 + 1` — **40 × 20**, wie überall sonst auch.

### ⭐⭐ BERICHTIGUNG 2 — die Formationsfrage ist beantwortet, und zwar vom Original

In `CommandBridge` steht seit Tagen »GELESEN, ABER NICHT GEBAUT« zur
**Formationsverschiebung** beim Gruppenbefehl (das Original rechnet
`Einheit.x − Mittelwert.x + Klick.x`, wir suchen freie Zellen im Ring), und die
Frage »bauen wir das?« lag als **Entscheidung beim Spieler**.

⭐ **Sie muss gar nicht entschieden werden: das Original stellt sie dem Spieler.**
Feld 16 von `options.cfg` ist genau dieser Schalter, mit den zwei Beschriftungen

> **`Gruppe Standard`** ↔ **`Formation Standard`**

und dem Hilfetext **»Formation bei Gruppenbewegungen einstellen«** (Dateiversatz
`0xF0BEE`).

→ Zu bauen sind **beide** Verhalten plus der Schalter — nicht eines von beiden.
⚠ **Welche Stellung welches Verhalten meint und was die Vorgabe ist, ist noch
nicht gelesen.** Die Namen legen nahe: »Formation« behält die Aufstellung,
»Gruppe« sammelt am Klickpunkt. Das ist eine **Vermutung aus dem Wortlaut**, kein
Befund.

### ⚠ Was uns fehlt und was wir zuviel haben

**Fehlt:** Geschwindigkeit als *Einstellung* (bei uns nur Tasten), Autosichern,
Hilfe-Fenster-Schalter, der Formation/Gruppe-Schalter, und die **zweite
Lautstärke** — das Original trennt **Spiel** und **Sprache** (Klangnummern
< 120 und 300…499 gegen 120…299 und ≥ 500).

**Zuviel (unsere Zutaten):** `fps_limit`, `vsync`, `ui_scale`, `cursor_hints`,
`pan_speed`, `skirmish_*`. Die dürfen bleiben — sie sind als Zutat gekennzeichnet.

⚠ **Nicht in `options.cfg`, obwohl das Fenster sie anbietet:** »Online-Hilfe
EIN/AUS« und »Karten-Scrollen EIN/AUS«. Die überleben den Programmlauf nicht.

### ⭐ Und drei weitere Formate, die dabei abfielen

* **`HELPG.PIC` = 36 × 3600 B, jedes Bild 60 × 60**, nicht 360 × 360 — die
  Blitgeometrie sagt 60 Zeilen à 60 Byte. `HELPG.DAT` ist 4000 B = **1000 × u32**
  (Bildnummer je Hilfetext, 0 = keins), und die 36 belegten Werte sind genau
  1…36, jeder einmal.
* **`ENCYCLOG.DAT` = 12 000 B = 3 × (1000 × u32)**: Textversatz, Bildnummer,
  Textlänge. ⭐ Die Datei geht **restlos** auf: für alle 106 Einträge zeigt der
  Versatz exakt hinter die Marke `#p<id>,`, und die Zwischenräume sind genau
  diese Marken. **Keine unerklärten Bytes.**
* **`PANEL.DTA` = 34 680 B = 204 × 170**, rohes 8-bit-Bild ohne Kopf, `0xFF` =
  durchsichtig; beide Kantenlängen stehen als Sofortwerte im Code. Nullmodell:
  203×170, 205×170, 204×169 und 204×171 gehen alle **nicht** auf.
* ⚠ **`CW.TMP` (177 440 B) ist die Mitnahme der Konstruktionen:** vier Blöcke —
  Fahrzeug-, Schiffs- und Flugzeugentwürfe plus die Bauteiltafel. Summe stimmt
  aufs Byte, und Block 4 ist mit `PARTS.CWD` **byteweise identisch**.

⚠ **Eine Kodierungsfalle:** `HELPG.TXT`, `BRIEFG.TXT` und `OBJECTG.TXT` sind
**CP437**, `ENCYCLOG.TXT` dagegen **CP1252**. Ein gemeinsamer Decoder ist falsch.

---

## AT. ⭐⭐ SPIELSTART, KARTENLADEN UND DIE GRENZEN DES GEFECHTS (21.08.2026)

16 benannte Startfunktionen gelesen, **alle Zahlen in beiden EXE geprüft**.
Ergebnis vorweg: **keine einzige Grenze unterscheidet sich** — die Unterschiede
sind Adressversätze. (Zur einen echten Ausnahme siehe AT.6.)

### AT.1 ⭐⭐ Die wichtigste Einsicht: alle sieben Grenzmeldungen sind STUMM

`meldung(text, zusatz)` — C `0x41CDB0` / F `0x41BF70`, **121 Aufrufer**:

```
mov  al, byte[0x4FA0C0]      ; der ENTWICKLERSCHALTER
test al, al
je   -> sofort zurück
... MessageBoxA(hwnd, "Do you want to quit the game?", text + ": " + zusatz)
```

Die **einzige** Schreibstelle von `0x4FA0C0` im ganzen Bild ist der Umschalter
hinter »Developers' cheats enabled/disabled« (C `0x43AF5A`).

⭐ **Folge für den Nachbau:** »Too many players for this map«, »There is no place
to appear«, »Cannot add more probr structures«, »Self check…«, »Out of map!«,
»Selected level not found!« sind **Entwicklermeldungen**. Im Auslieferungszustand
**sieht der Spieler nichts** — die Grenze wird trotzdem gezogen, und der Code
läuft mit der abgeschnittenen Menge weiter. **Wer sie als Fehlerabbruch baut,
weicht vom Original ab.**

### AT.2 ⭐ Acht Spieler, fest verdrahtet — und die Karte sagt es im Kopf

`cmp bl, 8` steht an **neun** Stellen in `spieler_verteilen` (C `0x41B310`);
die Acht kommt aus **keiner** Tafel.
⭐ Zweiter, unabhängiger Beleg: das Leeren eines Platzes räumt die Nummern
`platz·1000 … +999` — **8 × 1000 = 8000**, genau die Grenze des imap-Bandes
»< 8000 = Einheit«. Spielerzahl und imap-Bänder sind **dieselbe Entscheidung**.

⚠ **Es gibt kein Startplatz-Objekt.** Ein Platz existiert genau dann, wenn dem
Index `p` irgendeine Einheit **oder** ein Gebäude gehört.

⭐⭐ **Und der 53-Byte-Kopf jeder Karte sagt die Spielerzahl vorweg**
(Versatz 2). Nachgerechnet mit der Regel aus `0x41B250` über alle Karten:

> **Kopfbyte 2 == Zahl der belegten Plätze: 36 von 36. Null Abweichungen.**

| Karte | Plätze |
|---|---|
| NET01 · NET03 · NET08 | 4 |
| NET02 | 6 |
| **NET04 · NET05 · NET06 · NET07** | **8** |

⚠ **Die Plätze müssen nicht lückenlos sein** — NET08 belegt `{0,1,3,4}`. Der
Verteiler springt Lücken ausdrücklich über.

⚠⚠ **Zweig B der Platzvergabe (Auslosung) hat KEINE Prüfung** — dort steht weder
`cmp bl,8` noch die Meldung. Bei null freien Plätzen greift der Code auf
Altbestand zu. **Für den Gefechtsmodus die gefährliche Stelle**; ob die
Oberfläche vorher deckelt, ist ungelesen.

### AT.3 ⭐ Die drei Gefechts-Einstellungen sind gefunden

| Wert | C | Bereich |
|---|---|---|
| **Startgeld** | `word[0x5407A0]` | `0, 1000, … 10000`, dann zurück auf 0 |
| **Technikstufe** | `byte[0x540EB8]` | **1…8**, dann zurück auf 1 |
| **Platzvergabe** | `byte[0x540798]` | 0 = ausgelost · 1 = feste Reihenfolge |

⭐ Das ist genau der Stoff für den späteren Punkt »Gefechtsmodus anpassen«.

### AT.4 ⚠⚠ Ein belegter Fehler des Originals: drei Karten ohne Namen

Die deutsche Namenstafel (`0x4F805B`, 21 B je Eintrag) führt
`51 Sumpfschlacht · 52 Sandfalle · 53 Waldesrauschen · 54 Umkämpfte Inseln ·
55 Flußgefechte` — und **56, 57, 58 sind leer**. Das sind **NET06, NET07 und
NET08**. Die Gefechtskartenliste zeigt für sie einen leeren Namen, **in beiden
Auslieferungen** — und ausgerechnet NET06/NET07 sind 8-Spieler-Karten.

⚠ Zweiter Fund: `level_waehlen` durchsucht nur die **ersten 8** Plätze
(`cmp cl, 8`), obwohl die Kartentafel **20** fasst.

### AT.5 ⭐ Die Startfolge, und was Einzel- von Mehrspieler trennt

| | `doInit` (Einzelspieler) | `netzstart` (Mehrspieler) |
|---|---|---|
| Spieler 0 | Art **0** (Mensch) | Art 0 (Gastgeber) |
| Spieler 1…7 | Art **1** (Rechner) | Art **0xFF** (**leer**, warten auf Beitritt) |
| Stelle (C) | `0x415BA5` | `0x4195A4` |
| Bündnisdiagonale | — | `0x41952E` setzt `Matrix[i][i] = 1` |

⭐ Aus `doInit` fällt nebenbei der Takt heraus: **`SetTimer(hwnd, 1, 0x14, 0)` =
20 ms = 50 Hz** — zum dritten Mal unabhängig belegt.

### AT.6 ⚠⚠ DER FÜNFTE UNTERSCHIED DER AUSLIEFERUNGEN — und er ist gross

`gefecht_starten`: **C `0x41A150` ist 3171 Byte, F `0x419F90` nur 614.**

Der Zuwachs ist ein **Sprungverteiler über die Technikstufe** (C `0x41A22B`,
8 Fälle), der jedem der 8 Spieler eine **vorgefüllte Bauschlange** gibt
(sec63). **F hat das nicht — F leert die Schlange stattdessen.**

→ Im Gefecht startet man in C mit einer laufenden Produktion je nach
Technikstufe, in F mit gar keiner. Das ist der **grösste** der bisher fünf
belegten Unterschiede.

### AT.7 ⭐ Die Selbstprüfung repariert

`imap_selbstpruefung(id)` — C `0x404AC0`: geht die ganze Karte durch und
setzt **jede Zelle, die noch diese Nummer trägt, auf `0xFFFE`** (frei). Auch mit
ausgeschaltetem Entwicklerschalter — dann eben still.

⚠ **Preis:** ein voller Durchlauf über bis zu 254 × 254 Zellen **pro entfernter
Einheit**. Für den Nachbau ist die *Bedeutung* wertvoll, die *Umsetzung* nicht —
ein Rückverweis Einheit → Zellen wäre dieselbe Prüfung in O(1).

⭐ Nebenbei belegt: Breite `dword[0x542DC4]`, Höhe `dword[0x542DF8]`, imap-Index
**`spalte·256 + zeile`** — und weil sec6 65 536 Wörter fasst, kann **keine Karte
breiter oder höher als 254** sein (grösster gemessener Wert: 254 × 254).

### AT.8 ⭐ »Freund oder Feind«, genau — und zwei Ecken

```
co <  8000   -> Matrix[cis/1000][co/1000]        ; Einheit
co < 14000   -> pratelska_infa(cis, co - 10000)  ; Infanteriezelle
sonst        -> "nothing", 0
```

⭐ Eine **Infanteriezelle gilt als befreundet, solange KEINER ihrer bis zu neun
Insassen feindlich ist** — auch wenn sie leer ist.

⚠⚠ **Alles ≥ 14000 ist NIE befreundet.** Wald, Gebäude und Einzelobjekte laufen
in den `nothing`-Zweig. **Wer Gebäude über diese Funktion auf Bündnis prüft,
bekommt im Original immer »Feind«.**

### AT.9 »Out of map!« bricht nichts ab

Die Zelle wird **übersprungen**, die Schleife läuft weiter. Kein Abbruch, kein
Rückgabewert. ⚠ Der Zähler der Sichtzellen hat **keine obere Schranke**.

⭐ Das dritte Byte je Sichtzelle ist eine fertige »hier darf ich hin«-Auskunft:
frei = imap ist `0xFFFE` **oder** trägt eine Einheitennummer.

### AT.10 ⚠ Ein neuer Datenpunkt zum `.data`-Versatz

Der Zeichenkettenblock liegt **weiter auseinander als notiert**: `Out of map!`
`0xFF8` · `Self check…` `0x1000` · `Too many players…` `0x1020` · `doInit 1`
`0x1024`. Der Bereich ist also **`0xFF8…0x1024`**, nicht `0xF98…0x1004`.
Die Zustandstafeln bleiben bei `0xFA0`.

---

## AU. ⭐⭐ DIE KI DES ORIGINALS, VOLLSTÄNDIG (21.08.2026)

Neun benannte KI-Funktionen gelesen, aus **beiden** EXE, gemessen an **13 `.DM`**.
⚠ Vorweg: **vier der neun sind gar keine KI** (AU.11).

### AU.1 Der Takt: 20 Aufgaben auf 50 Bilder

`ai_tick` (C `0x4BFB80` / F `0x4BF630`) läuft je Bild. `ph = sec54 % 50`.
**Je Bild genau EINE Aufgabe — aber für alle acht Spieler.**

Vorsperren in dieser Reihenfolge:
1. `sec53[40p+0]`: `1` = Rechner · `0` = Mensch (nur wenn `byte[0x538BA8] != 0`) ·
   sonst (`0xFF`) übersprungen
2. `sec106[p] != 0` → **der ganze Zug fällt aus**
3. `ph > 48` → nichts. **Takt 49 ist tot.**

| Takt | C | Aufgabe |
|---:|---|---|
| 0 | `0x4BAB40` | **AI: test of life** |
| 1 | `0x4BA710` + `0x4BA7D0` | Stärkekarte sec55 → Sektorwerte sec56 |
| 2 | `0x4BB7D0` | **AI: transport** (Roboterlogistik) |
| 4 | `0x4BBAC0` | Basis schickt ihre 6 angedockten Einheiten los (sec23) |
| 5 | `0x4BB9A0` | **AI: production** (Bauschlange sec63) |
| 7 | `0x4BC900` + `0x4BC540` | **Set imp cpu:** und der Angriffsdurchlauf |
| 8 | `0x4BE2E0` | **target:** → Gruppen bilden und fahren |
| 10 | `0x4BE330` | Infanteriedurchlauf |
| 12 | `0x4BF150` | **Wachposten besetzen** (sec107) |
| 14 | `0x4BFA30` | 1 von 10: Basis suchen (schreibt sec3) |
| 16,20,24,28,32,36,40,44 | `0x4BF4E0` | `ai_units`, `k = 0…7`: jede untätige Einheit sucht sich selbst ein Ziel |
| 46 | `0x4BF760` | schreibt sec24/28/123 — **ungelesen** |
| 48 | `0x4BE5C0` | Gebäudepflege je Typ (Sprungtafel `0x4BE6E0`) |
| die übrigen 29 | — | Leerlauf |

⭐ **Die KI denkt nur in 21 von 50 Bildern.** In Mission 14 kommen die Takte 7 und
8 sogar nur mit 1/5 Wahrscheinlichkeit — also **einmal pro 250 Bilder je Spieler**.

### AU.2 ⭐ `target:` — die Auftragswahl ist eine DIVISION

`target:` (C `0x4BECF0` / F `0x4BE7A0`) protokolliert je Kandidat
`cx:` `cy:` `imp:` `pway:` **`po:`** `min:` — und

> **`po = Wegkosten / Wichtigkeit`. Gewählt wird das KLEINSTE `po`.**

Losgeschickt wird nur bei `r_best != 0xFF` **und** (`r_num > r_min` **oder**
`sec61[p] == 5`). Die Gruppengrösse ist **`(3 · r_min) / 2`**, geklemmt auf 3…99.

⚠ **Unser `SkirmishAi.cs` nimmt stattdessen das Maximum von `Priority` und kennt
gar keinen Wegewert.** Das ist der grösste einzelne Abstand zum Original.

**sec69** (8 × 100 × 6 B) ist die Auftragstafel:
`+0` Art (0 leer · 1 Gebäude · 2 Einheit · **3 sec17-Objekt** · 4 rohe Zelle) ·
`+1` `imp`, **nie 0** (sonst Abbruchfenster »IMP is 0!!!«) · `+2` Index/Spalte ·
`+4` Zeile. **483 von 483** Einträgen zeigen auf ein bestehendes Gebäude
(Nullmodell bei zufälligem Index: 80/483 = 17 %).

### AU.3 ⭐ Die Gruppentafel ist sec68 — und eine Berichtigung

**sec68 = 6464 B = 8 Spieler × 4 Gruppen × 202 B**, Index `202·(4p+g)`:
`+0` Anzahl · `+1` Auftragsnummer · `+2…+0xC9` **100 Einheitennummern als Wörter**.

⭐ **Höchstens 4 Gruppen à 100 Einheiten je Spieler.** Alle vier belegt →
»Attack group not available«, Rückkehr ohne Wirkung.

⚠ **Berichtigung zu Abschnitt Y:** dort stand »sec108 = Angriffsgruppen«. Falsch.
**sec108** (1984 B = 32 × 62) trägt die **Wegpunkte** der Gruppen (`0x4BCF30`).

Aufnahme: `faze == 0`, `CPU0` ist 1 oder 2, Antrieb ≠ `0xAB`, und im Sektor aus
`CPU1` muss `sec56[+7] < sec56[+0x0A]` gelten. Dann `CPU0 = 10`,
**`CPU1 = Gruppennummer`**, und `sec56[+0x0A]--`.
Bei `sec110[p] == 0` gilt »**Take all**«: ohne Sektorprüfung, jede Einheit mit
`faze==0`, `UKOL < 45`, `CPU0 < 5`.

### AU.4 ⭐ Woher »freie Angreifer« kommt

`Set imp cpu:` (C `0x4BBB80` / F `0x4BB640`) füllt je Sektor:
`+6` Summe der `imp` der eigenen Gebäude (aus **sec62**) · `+7 = +6` (»DEF:«) ·
**`+8 = min(100, 100·(+7) / pro_style[sec61[p]])`** (»DEF_robots:«) ·
`+0x0A` = zugeordnete Einheiten.

> **freie Angreifer = Summe über alle Sektoren mit `+8 < +0x0A` von `(+0x0A − +8)`.**

Ist die Summe 0 → »**Not free attacker:**« (C `0x4BE790`), und `target:` bricht ab.

**sec62 gemessen:** 324 gesetzte `imp`-Einträge, **322 (99,4 %)** auf ein Gebäude
mit Typ ≠ 0, **316 (97,5 %)** auf ein **eigenes**. Werte 6 (311×), 4 (8×), 9 (3×),
2 (2×). → sec62 ist die **Verteidigungswichtigkeit der eigenen Gebäude**.

**`CPU1` als Sektor-Halbbytepaar, gemessen:** 655 Einheiten mit `CPU1 != 0`,
davon **655 (100 %)** mit beiden Halbbytes ≤ 10.
⭐ Nullmodell (beliebiges Byte): 47 %, erwartet 309. **Damit ist es belegt.**
Bei `CPU0 == 2` (angekommen) nennt `CPU1` in **67 %** den eigenen Sektor;
Nullmodell einer Zufallseinheit: **0,9 %**.

⚠ **Berichtigung:** `CPU0 == 20` trifft **133 der 136 Roboter** und nur 3 andere
Einheiten — der Wert ist der **Transportroboter-Zustand**, nicht »frisch produziert«.

### AU.5 ⭐ `make robot z` / `make robot do` — »von« und »nach«

Keine zwei Erzeuger. Beide schreiben einem **vorhandenen** freien Roboter eine
Fahrstrecke in seinen **sec48**-Satz (400 × 18 B, Index = `sec5+0x40`).
Tschechisch **`z`** = *von*, **`do`** = *nach*:

| | `make robot z` (C `0x4BB570`) | `make robot do` (C `0x4BB6A0`) |
|---|---|---|
| sec48 `+0` **Quelle** | das übergebene Gebäude | zufällige eigene **Mine** (Typ 10/15) |
| sec48 `+4` **Ziel** | zufällige eigene **Basis** (Typ 1/9) | das übergebene Gebäude |
| gerufen bei | »one wheel less in **factory**« | »one wheel less in **mine**« |

**Der Satz ist also: `+0…+3` = bis zu vier Abholorte, `+4` = der Ablieferort.**

**Gemessen an 136 belegten Sätzen mit 123 Paaren:**

| Lesart | Treffer |
|---|---|
| `+4` ist das **Ziel** (Verträglichkeitstafel `0x4FDC00` erfüllt) | **123 / 123 = 100 %** |
| ⭐ **Nullmodell**: `+4` ist die Quelle (umgekehrt) | **0 / 123 = 0 %** |

Die Paare sind ausschliesslich **Mine → Fabrik** (10→3, 10→2, 10→4, 15→2, 15→3)
und **Fabrik → Basis/Flughafen** (2→1, 3→1, 3→9, 2→9, 4→1, 4→9) — die
Rohstoffkette, genau wie `z`/`do` es sagen. Die Tafel `0x4FDC00` / F `0x4FCC38`
(19 × 10 B, indiziert mit dem **Zieltyp**) sagt: zu Basis/Flughafen dürfen
1,2,3,4,6,9,12,16; zu den Fabriken nur 10,6,12,15; **zu einer Mine gar nichts —
eine Mine ist nie Ziel.**

Zwei Nebenbelege, beide 100 %: **136/136** Rückzeiger `sec48+0x0C` = Einheitennummer;
**136/136** Einheiten mit `top_spec == 0x47` haben einen belegten Satz
(Nullmodell bei zufälliger Zuordnung: rund 9).

Findet `0x4BAEB0` keinen freien Roboter: **`sec59[p] = 0`** — die Produktion geht aus.

### AU.6 ⭐ `get target in sector` — die Ordnung ist die Einheitennummer

`0x4BC3D0` / F `0x4BBE90`: läuft `si` von 0 bis 8000, **überspringt ganze
Spielerblöcke** (`si += 1000`), für die `sec53[40p + 0x15 + q] != 0` (Freund oder
man selbst), und nimmt den **ersten** Treffer mit `faze != 0xFF`, `RX/24 == sx`,
`RY/24 == sy`, `+0x0A < 4` (Landeinheit, kein Schiff) und `UKOL < 45`.

⭐ **Nicht der nächste, nicht der schwächste, nicht der wertvollste — der mit der
kleinsten Einheitennummer.** Da die Nummer der Reihenfolge der Kartenanlage
entspricht, greift die KI die Einheit an, die der **Kartenbauer zuerst gesetzt hat**.

Der Aufrufer `0x4BC540` klappert je Sektor mit `sec56[+0x0A] > 0` die **9 Nachbarn**
aus der Tafel `0x538C10` ab, in fester Reihenfolge:
(0,0), (0,+1), (+1,0), (−1,0), (0,−1), (+1,+1), (−1,+1), (+1,−1), (−1,−1).
Beim ersten Treffer läuft der Alarmruf (Klang `0x7A`) mit Zufalls-Sperrzeit
4000…7000 — **ausser in Mission 14**.

### AU.7 ⭐ `AI: test of life` — und wie ein Spieler stirbt

`0x4BAB40` / F `0x4BA640`, 114/114 Befehle **gleich**. Drei Wege zum Weiterleben,
jeder reicht: ein Gebäude mit Typ **1, 9 oder 11** · eine Einheit mit
`faze != 0xFF` · ein Flugzeug (sec19) mit Typ ausser 0, 13, 14.

**Fällt alles durch, sterben heisst genau drei Handgriffe:**
`sec53[40p] = 0xFF` · **jedes** seiner 255 Gebäude bekommt bei `+5` *und* `+0x41`
den Besitzer **11** · sein ganzes 11 × 11-Raster sec56 wird auf `+0/+2/+4` genullt.

⭐ Gemessen: `sec53[40p]` hat nur drei Werte — `1` (50×), `0xFF` (41×), `0` (13×),
und **die 13 Nullen liegen ausnahmslos auf Platz 0**. In jeder Kampagnenkarte ist
Platz 0 der Mensch.

### AU.8 ⭐⭐ DER SECHSTE AUSLIEFERUNGSUNTERSCHIED: `pro_style`

In der Zeile, die `DEF_robots` ausrechnet:

```
C 0x4BBD11   movsx ecx, word ptr [ecx*2 + 0x538BC8]   ; ⭐ WORT
F 0x4BB7D6   mov   cl,  byte ptr [edx   + 0x537C08]   ; ⭐ BYTE
```

| Betriebsart | 0 | 1 | 2 | 3 | **4** | 5 | 6 | 7 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| **C** (8 Wörter) | 1 | 30 | 50 | 100 | **400** | 255 | 0 | 0 |
| **F** (8 Bytes) | 1 | 30 | 50 | 100 | **200** | 255 | 0 | 0 |

`DEF_robots = min(100, 100·DEF / pro_style)` → in Betriebsart 4 hält **F doppelt
so viele Verteidiger zurück wie C. C greift dort spürbar aggressiver an.**
Der Grund für den Formatwechsel liegt offen: **400 passt nicht in ein Byte.**

⭐ **Und der Fund erklärt sich selbst:** vor der Tafel beträgt der C−F-Abstand
`0xFC0` (`0x538BC0`→`0x537C00`, `0x538BC8`→`0x537C08`), unmittelbar dahinter
`0xFC8` (`sec61` `0x538BD8`→`0x537C10`). Die **8 Byte** Sprung sind exakt die
8 Byte, um die die Tafel breiter geworden ist.

⚠ **Eine Mine:** `pro_style[6]` und `[7]` sind **0**. Wäre `sec61[p]` 6 oder 7,
teilt `Set imp cpu:` durch Null. Es geht nur gut, weil das Skript sie nie setzt.

⭐ Gegenprobe zum bekannten sec59-Unterschied: nachgeprüft und bestätigt.
`AI: transport` C 120 gegen F 109 Befehle; C setzt im Zweig »no target found«
`sec59[p] = 1` @`0x4BB7FC`, F nicht. Da `make robot z/do` `sec59[p] = 0` schreiben,
wenn kein freier Roboter da ist, bleibt **in F ein basisloser Spieler dauerhaft
ohne Produktion**.

`make robot z`, `make robot do` und `AI: test of life` sind Befehl für Befehl
gleich (78/78, 77/77, 114/114). Der einzige weitere Fund im ganzen KI-Bereich ist
in `0x4BCF30` ein `cmp dl,al; ja` gegen `cmp al,dl; jb` — **gleichbedeutend**.

### AU.9 sec61 und sec106 — wer was sperrt

**sec61 = die Betriebsart**, geschrieben an **genau einer** Stelle (C `0x4D1050`),
gerufen 72×, davon 71× aus dem Kampagnenskript `0x487C40` »LEVEL0 A«. Reine
Missionsvorgabe. Skriptwerte: 2, 3, 4, 5, 10. In den 13 `.DM`: 2 (92×), 3 (11×),
5 (1×) — nie 10, nie 4.

| sec61 | Wirkung |
|---:|---|
| `== 10` | ⭐ **Takte 2, 4, 5, 7, 8, 10, 12 fallen ganz aus** — kein Transport, keine Produktion, keine Wichtigkeitsrechnung, kein Angriff, keine Gruppen, keine Wachposten |
| `== 5` | Takt 8 nimmt `0x4BDCC0` statt `target:`; und es wird trotz zu weniger freier Angreifer angegriffen, ohne die Gruppe auf 99 hochzusetzen |
| sonst | **der Index in `pro_style`** — der einzige Zahlenhebel der Schwierigkeit |

**sec106 = die Skriptsperre**, ebenfalls ein einziger Schreiber (C `0x4D09F0`),
14×, nur aus `0x487C40`, fast immer Platz 7. In den Prüfdateien 101× `0`, **3× `1`**
(1.DM, 2.DM, 11.DM, jeweils Platz 7). `!= 0` heisst: der ganze KI-Zug fällt aus
(`0x4BFBFA`), seine Einheiten können nicht übernommen werden (`0x411351`,
`0x4113C2`), Verbündete übergehen ihn (`0x42072C`, `0x420B60`).

⭐ Das heisst **»dieser Spieler wird vom Missionsskript geführt«** — nicht
»ausgeschieden«. Ausgeschieden ist `sec53[40p] == 0xFF`.

### AU.10 ⚠ Ein Fehler im Original, in BEIDEN Fassungen

`0x4BAFE0` / F `0x4BAAE0` (Minenwahl für `make robot do`) teilt die
Gebäudekoordinaten **selbst durch 24** und übergibt sie dann an `0x4BAD20`,
**das noch einmal durch 24 teilt**. `AI: transport` übergibt dieselben Werte roh.

Die Daten entscheiden: über 13 `.DM` liegen **540 von 540** Gebäuden nach **einer**
Division im 11 × 11-Raster; nach zweien ist der Sektor **immer (0,0)**.

⭐ **Folge: `make robot do` prüft für jede Mine die Lohnendheit von Sektor (0,0)
statt der ihren.** Der Fehler steht in C und in F.

### AU.11 ⚠ Vier der neun sind keine KI

| C / F | was es wirklich ist |
|---|---|
| **`sejmi 1`** `0x4B95D0` / `0x4B90D0` | ⭐ **Ein Bildschirmfoto.** Tschechisch *sejmi* = »nimm auf«. Nullt 64 KB, schreibt `D:\screen.bin`, baut auf dem Stapel einen BMP-Kopf (`0x436` = 54 + 1024 Palettenbytes) und schreibt `D:\mapa.bmp`. **Null Aufrufer, null Relokationen** — unerreichbarer Entwicklerrest. |
| **`Unit missing`** `0x433C20` / `0x432D70` | Die **Truppverwaltung des Menschen**. sec16 = 4000 Trupps × 22 B (`+0` Anzahl, `+1…+3` Zelle, `+4…+0x15` neun Nummern). Fehlt die Einheit, wird sie aus **allen** 4000 gestrichen; leert sich ein Trupp, wird sein Kartenzeichen in sec6 gelöscht (`0xFFFE − n`). |
| **`go in 1`** `0x4380F0` / `0x437250` | Der Knopf **»einsteigen«**; unterscheidet »inf in« von »robot in« über `sec5+0x0A == 1`. |
| **`guard:`** `0x428940` / `0x427B30` | Die **Abfangjägerwache eines Flughafens**, gerufen aus dem Gebäudetakt »Bg:« `0x43CA50`, **nicht** aus `ai_tick`. Über `sec3+0x19` in die Flughafentafel **sec27** (50 × 52), Wachliste ab `+0x0B`. Sucht unter 200 Flugzeugen den nächsten Feind **in der Luft** und startet bei Quadratabstand < 3600 (= 60²) einen Jäger. |

### AU.12 Was unser `SkirmishAi.cs` anders macht

| | Original | `SkirmishAi.cs` (2413 Zeilen) |
|---|---|---|
| Takt | 50-Bilder-Rundlauf, 1 Aufgabe je Bild für alle 8 | Sekundentakt je Spieler |
| **Auftragswahl** | **Minimum von `pway / imp`** | **Maximum von `Priority`**, kein Wegewert |
| Angriffsfreigabe | nur wenn `r_num > r_min`, ausser `sec61 == 5` | nur `army.Count > guard` |
| Gruppengrösse | `(3·r_min)/2`, geklemmt 3…99; bei `sec110 != 0 && sec61 != 5` auf **99** | fester Wellenwert je Schwierigkeit |
| Gruppen | **4 × 100** je Spieler, sec68, Zustand in `CPU0`/`CPU1`, Wegpunkte in sec108 | **eine** unbegrenzte `Wave` |
| **Sektorenraster** | sec55 → sec56, freie Angreifer als Überschuss **je Sektor** | **fehlt vollständig**, Einheiten sind eine globale Liste |
| Schwierigkeit | **ein** Hebel: `pro_style[sec61]` aus dem Missionsskript | drei fest verdrahtete Stufen |
| Wachposten | sec107, 10 je Spieler, Feldkoordinaten, Takt 12, `CPU0 = 5` | eigene Wachzahl, keine Posten |
| **Logistik** | `rob_trans`: **Mine → Fabrik → Basis**; ohne Roboter steht die Produktion (sec59) | **fehlt**; `AiEmptyDepots` ist Erfindung |
| Auftragsart 3 | **sec17-Objekt** (100 × 24 B) | dort die rohe Zelle — das ist Art **4** des Originals |
| Lebendprüfung | drei Wege; beim Tod fallen alle Gebäude an Besitzer **11** | `AliveAsPlayer` |
| Skriptsperren | `sec61==10`, `sec106!=0` | keine Entsprechung |

Bereits übernommen sind `find_base`, `build_in_base`, `ai_production`, `ai_units`
(`0x4BF4E0`) und `AddMissionTarget` samt der 100er-Grenze von sec69.

### AU.13 ⚠ Wachposten und Gruppen sind reiner Laufzeitzustand

**sec107 = 8 × 10 × 2 B** (Spalte, Zeile) in Feldkoordinaten. Ein Posten zählt,
wenn er ≠ 0 ist und `sec20[Zeile·256 + Spalte] == 0`.

⭐ **Nullmodell: in allen 13 `.DM` sind alle 1040 Posten-Bytepaare 0**, und alle
416 Gruppenzähler (13 × 32) ebenfalls. Passend dazu kommt `CPU0 == 10` (in Gruppe)
und `CPU0 == 5` (Wachposten) in **keiner** Prüfdatei vor. **Der Kartenbauer setzt
beides nicht — es entsteht erst im Spiel.**

### AU.14 Was offen bleibt — und wodurch das Verfahren blind ist

**Ungelesen:** `0x4BF760` (Takt 46, schreibt sec24/28/123, Obergrenze aus
`byte[0x503AF0 + 2·Missionsnummer]`) · `0x4BE5C0` (Takt 48, Sprungtafel `0x4BE6E0`
über 12 Gebäudetypen) · `0x4BDCC0` (Sonderweg Betriebsart 5) · `0x4BCF30`
(671 Befehle, Gruppenfahrt und sec108-Wegpunkte) · die Bedeutung von `sec56 +0x02`
(unser Code sagt »zweiter Stärkeeimer«, die alte Deutung »verbündete« —
**ungegengeprüft**) · `dword[0x539234]`, `byte[0x540EB8]`, `word[0xBCA0E0]` ·
der **Gebäudetyp 11** (zählt als Leben und ist zugleich der Erbe der Toten).

⚠ **Blindstellen des Verfahrens, ausdrücklich benannt:**
* **Berechnete Adressen.** Alles hier stammt aus `[reg + Sockel]`; wer die Basis in
  ein Register lädt und dann `[esi+4]` schreibt, taucht bei `reloc_refs` nur mit der
  Basis auf. Die Negativbefunde »sec61 hat genau einen Schreiber« und »sec106 hat
  genau einen« stehen unter diesem Vorbehalt.
* **Indirekte Aufrufe.** `sejmi 1` hat null direkte Aufrufer *und* null
  Relokationen — das schliesst eine Tabelle aus, aber keinen berechneten Sprung.
* **Versetzter Strom.** Wo eine Sprungtafel direkt hinter dem `ret` liegt
  (`0x4BECF0`, `0x4BE5C0`, `0x4BDCC0`, `0x4BF760`), wurden Daten als Code gesehen.
  **Alle scheinbaren C/F-Unterschiede in diesen Nachläufen sind verworfen**, nicht
  gemeldet — richtig so.
* **Die Prüfdateien sind Anfangszustände.** Leere Tafeln (sec68, sec107) belegen
  »reiner Laufzeitzustand«, sagen aber nichts über die Werte im Spiel. Alle Zahlen
  zu `CPU0`, `sec56 +6…+0x0A` und `sec110` sind Momentaufnahmen des Kartenbauers.
* **Die 23 `.CWM` tragen die KI-Abschnitte gar nicht** (sie laden nur sec1…38).
  Jede Zahl oben stammt aus den 13 `.DM`; **für Gefechtskarten gibt es keinen
  KI-Anfangszustand.**

### AU.15 Adressverzeichnis

| was | C | F |
|---|---|---|
| `ai_tick` »AI« / »AI end« | `0x4BFB80` | `0x4BF630` |
| `AI: test of life` | `0x4BAB40` | `0x4BA640` |
| `make robot z` | `0x4BB570` | `0x4BB070` |
| `make robot do` | `0x4BB6A0` | `0x4BB1A0` |
| `get target in sector` | `0x4BC3D0` | `0x4BBE90` |
| `Not free attacker:` | `0x4BE790` | `0x4BE250` |
| `guard:` (Luftwaffe) | `0x428940` | `0x427B30` |
| `sejmi 1` (Bildschirmfoto) | `0x4B95D0` | `0x4B90D0` |
| `Unit missing` (Trupps) | `0x433C20` | `0x432D70` |
| `go in 1` (Knopf) | `0x4380F0` | `0x437250` |
| `AI: transport` | `0x4BB7D0` | `0x4BB2D0` |
| `AI: production` | `0x4BB9A0` | `0x4BB460` |
| `AI: production in base` | `0x4BB1E0` | `0x4BACE0` |
| `Set imp cpu:` / `Set imp:` | `0x4BBB80` | `0x4BB640` |
| Angriffsdurchlauf je Sektor | `0x4BC540` | `0x4BC000` |
| Hülle Takt 7 | `0x4BC900` | `0x4BC3C0` |
| `Create group cpu:` / `Take all` | `0x4BC920` | `0x4BC3E0` |
| `target:` / `po:` / `r_best:` | `0x4BECF0` | `0x4BE7A0` |
| Sektor-Wegesuche (Kostenkarte) | `0x4BEA30` | `0x4BE4E0` |
| Auftragswahl Betriebsart 5 | `0x4BDCC0` | `0x4BD780` |
| Gruppen fahren / Wegpunkte | `0x4BCF30` | `0x4BC9F0` |
| Wachposten besetzen (Takt 12) | `0x4BF150` | `0x4BEC00` |
| Hülle Takt 8 | `0x4BE2E0` | `0x4BDDA0` |
| Infanteriedurchlauf (Takt 10) | `0x4BE330` | `0x4BDDF0` |
| Basis suchen (Takt 14) | `0x4BFA30` | `0x4BF4E0` |
| `ai_units` (Takte 16…44) | `0x4BF4E0` | `0x4BEF90` |
| Takt 46 (ungelesen) | `0x4BF760` | `0x4BF210` |
| Gebäudepflege (Takt 48) | `0x4BE5C0` | `0x4BE080` |
| Gebäudepflege bei `sec61==10` | `0x4BF3C0` | `0x4BEE70` |
| Stärkekarte sec55 | `0x4BA710` | `0x4BA210` |
| Sektorwerte sec56 | `0x4BA7D0` | `0x4BA2D0` |
| Basis schickt Angedockte los | `0x4BBAC0` | `0x4BB580` |
| freien Roboter suchen | `0x4BAEB0` | `0x4BA9B0` |
| zufällige eigene **Basis** | `0x4BAF50` | `0x4BAA50` |
| zufällige eigene **Mine** ⚠ Fehler | `0x4BAFE0` | `0x4BAAE0` |
| »lohnt der Sektor« | `0x4BAD20` | `0x4BA820` |
| `rob_trans`-Feld setzen | `0x4362E0` | `0x435440` |
| Auftrag scharfschalten | `0x410870` | `0x4106A0` |
| sec61-Setzer (Betriebsart) | `0x4D1050` | — |
| sec106-Setzer (Skriptsperre) | `0x4D09F0` | — |
| Kampagnenskript »LEVEL0 A« | `0x487C40` | — |

**Tafeln:** Phasentafel `0x4BFEA4` · Sprungtafel `0x4BFE50` ·
Verträglichkeit 19 × 10 `0x4FDC00` / F `0x4FCC38` · **`pro_style` `0x538BC8`
(Wörter) / F `0x537C08` (Bytes)** · Nachbartafel 9 × (dx,dy) `0x538C10` ·
Nachbargewicht `0x538BE4` · »KI übernimmt leere Plätze« `0x538BA8` ·
Alarmruf-Sperrzeit `0x538BAC` · 150er-Zähler `0x538BC0` (⚠ **nicht** sec131) ·
sec61 `0x538BD8` · Missionsnummer `0x539934` · `CPU0`-Sprungtafel `0x4BC214` ·
Gebäudepflege-Sprungtafel `0x4BE6E0`.

**Laufzeitpuffer ohne Abschnittsnummer** (`.bss`, C = F + `0xFA0`):
Sektor-Kostenkarte `0xB45FB0` (121 × 4 B) · Vorgängerkarte `0xB36AA0` (121 B) ·
Warteschlange `0xB38D50` (Sätze zu 16 B) · Freiliste `0xB38530` (Wörter) ·
Zufallsliste `0xB38D00` (Bytes).

---

## AV. ⭐⭐ DIE HAUPTSCHLEIFE — UND WARUM »50 Hz« SO NICHT STIMMT (21.08.2026)

Elf selbstbenannte Funktionen gelesen, **alle in beiden EXE befehlsweise
verglichen**. Die Hauptschleife ist in C und F dieselbe — und sie sieht anders
aus, als wir dreimal angenommen haben.

### AV.1 ⭐⭐⭐ Der Aufbau, vollständig

```
doInit  (C 0x4158F0 / F 0x415730)   letzte Handlung:
        SetTimer(hwnd, 1, 0x14, NULL)            C 0x415BC5 / F 0x415A05

WndProc, Fall 0x113 = WM_TIMER      C 0x414010 / F 0x413E25:
        byte[0x53920C]++ ; fertig.               ← MEHR NICHT

WinMain (C 0x414E20 / F 0x414C60):
  0x41518A  PeekMessage(PM_REMOVE) — Nachricht? -> Dispatch, zurück zum Kopf
  0x415470  wenn byte[0x4F6FB0] != 0 :  byte[0x53920C] = 1
  0x415480  wenn byte[0x53920C] == 0 :  zurück zum Kopf
  0x415570  call Main_funct                      C 0x415CF0 / F 0x415B30
```

**Je Durchlauf wird genau EINE Fensternachricht verarbeitet ODER genau EIN
`Main_funct` gefahren.** Es gibt **kein `GetMessage` und kein `WaitMessage`** im
ganzen Bild (`PeekMessageA` 4 Stellen, `GetMessage` **0**) — die Schleife dreht
frei.

### AV.2 ⚠⚠ DER ZEITGEBER IST IM AUSLIEFERUNGSZUSTAND WIRKUNGSLOS

`byte[0x4F6FB0]` (F `0x4F5FAC`) hat im ganzen Bild **genau einen** Verweis: den
Lesebefehl `0x415471`. Sein Anfangswert in `.data` ist **`0x01`**, in **beiden**
EXE. Also wird `byte[0x53920C] = 1` **vor jeder Prüfung** gesetzt, und der vom
`WM_TIMER` hochgezählte Rückstand wird **überschrieben, bevor er gelesen wird**.

> **Zahl:** 1 Verweis unter 31 650 `.text`-Verweisen, und er ist ein Leser.
> **Nullmodell:** die Relokationstafel ist eine Vollerhebung — ein Schreiber mit
> fester Adresse **müsste** auftauchen. Alle zehn Nachbarn (`0x4F6FA0`…`0x4F6FC8`)
> haben eigene, einzelne Verweise; ein Blockschreiber über eine Zeigerbasis ist
> damit sehr unwahrscheinlich, aber (AV.10) nicht ausgeschlossen.

Damit ist auch die **Rückstandskappung tot**, die sonst schön wäre
(`cmp al,4; ja -> byte[0x53920C]=1` @C `0x415F01`): sie kann nur über den zweiten
Aufrufer von `Main_funct` (`Get run begin` `0x4198D6`) je greifen.

### AV.3 ⭐⭐ Was den Takt WIRKLICH begrenzt: der Strahlrücklauf

`flip_ddraw` (C `0x415CB0` / F `0x415AF0`, 51 B, gleich):

```
push 0                 ; dwFlags = 0
call [vtbl+0x2C]       ; IDirectDrawSurface::Flip(NULL, 0)
cmp eax, 0x887601C2    ; DDERR_SURFACELOST  -> Restore
cmp eax, 0x8876021C    ; DDERR_WASSTILLDRAWING -> nochmal, Warteschleife
```

`Flip` mit `dwFlags == 0` **wartet auf den vertikalen Rücklauf**. Das ist der
einzige Bremsklotz im Bildweg. ⭐ **Der Bildtakt des Originals ist die
Bildwiederholrate des Monitors, nicht 20 ms.** Dazu passt, dass das Programm
sich überhaupt eine Bildrate misst (AV.5) — bei festen 50 Hz wäre das sinnlos.

### AV.4 ⭐⭐ Simulationstakt und Zeichentakt sind getrennt — durch einen Faktor

In `Main_funct` steckt eine **innere Wiederholung**:

```
0x41605A  cmp word[0x4FA280],0 ; jne Ende      Pause/Ende -> gar keine Simulation
0x41606A  al = byte[0x4FA23C]  ; == 0 -> nichts
0x416077: ▼ dword[0x4FA240]++ (Takt) ; rnd, CPU, Power, Search, Movement, …
0x4168A7  cmp bl,al ; jb 0x416077              ▲ WIEDERHOLEN
0x4168B4: »move window« · »Draw windows« · »Palette anim« · »Flip pages« -> Flip
```

> **`byte[0x4FA23C]` (F `0x4F9244`) ist die SPIELGESCHWINDIGKEIT — die Zahl der
> Simulationsdurchläufe je gezeichnetem Bild.**

6 Leser, 6 Schreiber. Mit **1** geschrieben in `Get run begin` (`0x4193C8`) und
im Wiedergabezweig (`0x416D67`); im Befehlsbus-Verteiler `0x4C2280` auf
**`0x14` = 20 gedeckelt** (`0x4C4420`) und aus dem Befehlssatz gesetzt
(`0x4C443E`, `0x4C444E`). ⭐ **Die Geschwindigkeit läuft über den Befehlsbus —
sie ist im Netzspiel mitsynchronisiert** und dort hart auf 1 gezwungen.

| | |
|---|---|
| **Zeichentakt** | 1 Bild je `Main_funct`, begrenzt vom `Flip` auf den Strahlrücklauf |
| **Simulationstakt** | `byte[0x4FA23C]` Durchläufe je Bild = **Bildrate × Geschwindigkeit** |
| **Zeitgeber (20 ms)** | läuft und zählt — und wird vor jeder Prüfung überschrieben. **Er begrenzt nichts.** |
| **Netzspiel** | `byte[0x4F7F60]` wird bei `dword[0x539234] != 0` nicht gesetzt (`0x4153CB`); der Takt läuft nur nach einem abgeglichenen Befehl → **Gleichschritt über den Bus** |

### AV.5 ⚠⚠ DER WIDERSPRUCH ZU UNSERER EIGENEN ZAHL — offen, nicht entschieden

Wir führen **`TicksPerSecond = 50`** als *gemessen*, dreifach: der `SetTimer` mit
20 ms, die Ableitung »eine Spielminute = 250 Takte = 5,00 s«, und der Takt aus
`doInit` (AT.5).

**Dieser Befund entwertet den ersten der drei Belege** und deutet den dritten
um: der `SetTimer` ist da, aber sein Ergebnis wird überschrieben.

⚠⚠ **Das ist NICHT als erledigt zu verbuchen.** Der Lauf war eine reine
Codelesung; das Original lief dabei nicht. Zwei Lesarten stehen nebeneinander:

1. **Der Befund stimmt** → die Taktrate des Originals hing an Monitor und
   Geschwindigkeitsregler, war also **nie fest**. Dann ist unsere 50 eine
   willkürlich herausgegriffene Zahl, die zufällig in der Nähe lag.
2. **Der Befund ist unvollständig** → irgendetwas setzt `byte[0x4F6FB0]` doch
   auf 0 (über eine Zeigerbasis, die die Relokationstafel nicht sieht), und der
   Zeitgeber greift.

⭐ **Der Prüfstand, der das entscheidet, ist klein und muss gebaut werden:** das
Original laufen lassen und `dword[0x4FA240]` (Takte seit Missionsbeginn) je
Sekunde gegen die Bildwiederholrate halten, bei Geschwindigkeit 1.
**Bis dahin bleibt `TicksPerSecond = 50` stehen** — eine ungeprüfte Umstellung
wäre schlimmer als eine bekannte Unsicherheit.

### AV.6 ⭐ Das Original misst seine eigene Bildrate — und wirft die Zahl weg

C `0x417DD1` / F `0x417C0D`, am Ende jedes `Main_funct`, einmal je Sekunde:
`dword[0x54072C] = Bilder · 1000 / verstrichene ms`. Die `·1000` ist keine
Deutung: `lea ·5, lea ·5, lea ·5, shl 3` = 5·5·5·8.

> ⭐ **Negativbefund mit Zahl:** `dword[0x54072C]` (F `0x53F78C`) hat **genau
> einen** Verweis — den Schreibbefehl. **Die gemessene Bildrate wird nirgends
> gelesen und nirgends angezeigt.** In C und F gleich.

Denselben Bau hat der Wegsuchzähler `dword[0xBCA010]`.

### AV.7 ⭐⭐ DER SIEBTE AUSLIEFERUNGSUNTERSCHIED: NUM0…NUM7

Die Tastenindextafel ist in beiden 137 B (VK 9…0x91). Von 101 abweichenden
Einträgen sind 100 nur die andere Nummer des Vorgabearms (C 39, F 47). Die
**inhaltliche** Abweichung sind acht:

| VK | C | F |
|---|---|---|
| `0x60`…`0x67` (**NUM0…NUM7**) | Vorgabe → `DefWindowProc` | **Arme 25…32** |

Der F-Arm ist achtmal derselbe 12-Byte-Stummel:
`mov byte[0x4F928C], k` (k = 0…7) — und `byte[0x4F928C]` ist F's **eigene
Spielernummer** (in C `byte[0x4FA284]`, belegt über den befehlsgleichen Griff
`mov byte[eax + 0xB8A3B8], 1` @C `0x4151E4` gegen `0xB89418` @F `0x415024`).

> ⭐⭐ **In der frühen Auslieferung (F, 16.09.1997) schaltet man mit dem
> Nummernblock 0…7 auf jeden der acht Spieler um. In der späteren (C,
> 22.01.1998) ist dieser Entwicklergriff ENTFERNT.**
> 8 Tafeleinträge, 8 Sprungtafelarme (32 B), 96 B Stummelcode — und die
> restliche Tastentafel ist Arm für Arm dieselbe.

### AV.8 ⭐ Der achte und neunte Unterschied

**Achter — C hält beim Beenden die CD an.** Am Schluss von `WinMain` ist bis auf
**einen** Befehl alles gleich: C ruft zusätzlich `cd_stop()`
(`0x4D50C0` = `mciSendCommandA(…, 0x808 = MCI_STOP, …)`) vor `cd_close()`.
Beide EXE *besitzen* die Funktion (F `0x4D4C50`, dort von 3 Stellen im
CD-Fenster gerufen) — **F ruft sie beim Beenden nur nicht.**
Befehlszahl `WinMain`: C 583, F 582 — **genau ein Befehl.**

**Neunter — das Entwicklerprotokoll `c:\cw_log.txt` (Strg+R):**

| | F (09/1997) | C (01/1998) |
|---|---|---|
| Öffnungsart | **`"r+t"`** — Datei muss vorher da sein | **`"wt"`** — wird angelegt |
| Formatzeile | 8 `%d` (`nr typ faze ukol prod energie reload spodek`) | dieselbe **+ `akce x y`** — 11 `%d` |
| Stapelabbau | `add esp, 0x28` = 10 Doppelworte ✔ | `add esp, 0x34` = 13 ✔ |

⭐ **Beide Fassungen sind in sich stimmig** — kein Fehler, sondern eine
Erweiterung.

⭐ Nebenbei belegt: die Einheitentafel `0x6E26C8`, **78 B je Satz**, 8000 Sätze —
`(0x77AC51−0x6E26D1)/78 = 8000`, und der Spielstandschreiber sichert genau
`0x98580 = 624 000 = 8000 × 78`.

### AV.9 ⭐ Das Bildschirmfoto IST erreichbar — über ROLLEN

`0x418B00` (C) / `0x418940` (F), 137 B, befehlsgleich:
`fopen("d:\screen.bmp","r+b")` → `fseek(0x436)` → `Lock(Rückpuffer)` →
`fwrite(pixel, 1, 0x75300)`.

> ⭐⭐ **Beide Zahlen gehen auf:**
> `0x436 = 1078 = 14 + 40 + 1024` — genau der Kopf einer 8-Bit-BMP.
> `0x75300 = 480 000 = **800 × 600**`, rohes 8 Bit.
> **Nullmodell:** 480 000 zerfällt auch in 640×750, 960×500, 1200×400 … — von
> allen Teilerpaaren ist **800 × 600 das einzige Bildschirmmass**, und es ist
> Stufe 1 der Modustafel `0x538858` (Abschnitt AS).

**Weg:** `VK_SCROLL (0x91)` → C `0x413E2E` / F `0x413C43`; prüft
`byte[0x4FA0C0]` (Entwicklerschalter) und ruft sonst nichts.
→ **Praktisch verwertbar: »Developers' cheats enabled« (C `0x43AF5A`), dann
ROLLEN.** ⚠ Zwei Vorbehalte: `d:\screen.bmp` muss **vorher existieren** samt
gültigem 1078-Byte-Kopf (`"r+b"` legt nichts an), und es wird **fest 800 × 600**
geschrieben.

⚠ Die zweite `d:\screen.bmp`-Funktion `0x4226C0` / F `0x421880` ist **tot**
(0 Aufrufer, 0 Relokationen, auch über ihren Stummel) — sie schreibt `save.sav`
und ist ein Entwicklerwerkzeug, kein Bildschirmfoto.

⭐ **Das Tastenzustandsfeld ist `0xA182E8`** (F `0xA17348`), 256 B. Es wird für
**genau 16** Tasten abgefragt, in beiden EXE dieselben: `0x00`, Umschalt, Strg,
die vier Pfeile, und A E H Q R S T W Z. **Strg an 24 Stellen, Umschalt an 13, in
C und F gleich viele** — damit sind alle »Strg+X«-Griffe an einer Stelle lesbar.

### AV.10 ⭐ `CWorms Player` ist der DirectPlay-Spielername

`0x403B60` / F `0x403B40`, 404 B, befehlsgleich. Ein Aufrufer: `Get run begin`.
Vier benutzte Tafelplätze, alle auf `IDirectPlay` (Fassung 1):
`+0x08 Release`(1 Arg) · `+0x10 Close`(1) · `+0x14 CreatePlayer`(5) ·
`+0x50 Open`(2).

> **Zahl:** 4 von 4 treffen die semantisch richtige Methode **und** die
> Argumentzahl. **Nullmodell:** `IDirectPlay` hat rund 30 Methoden — vier
> geratene Indizes, die alle passen *und* die Stellenzahl treffen, liegen bei
> etwa 1 : 10⁶.

Und die Sitzungsstruktur geht **restlos auf**: `dwSize = 0x7C = 124`, GUID nach
`+0x04`, **`+0x18 = 8`**, `+0x20 = 2`, Name ab `+0x24` — genau
`DPSESSIONDESC` der Fassung 1 (124 B).
⭐⭐ **`dwMaxPlayers = 8` — ein dritter, ganz unabhängiger Beleg für die acht
Spieler aus AT.2.**

**Warum »CWorms«?** Es ist der **Name der Spielmaschine selbst.** Im `.data`
stehen zwölf Dateiendungen, die alle mit `CW` beginnen (`.cwa .cwd .cwg .cwi
.cwk .cwm .cwn .cwp .cwr .cws .cwt .cww`) plus der Dialogfilter
`CW Map File (*.CWM)`; und `CWorms` kommt im ganzen Bild **genau einmal** vor.
→ **Kein Rest eines fremden Programms, sondern der Arbeitsname unseres eigenen.**

⭐ Die DirectPlay-GUID ist in C und F **byteweise gleich**
(`67F21240-51C1-11D0-B7C5-008048A81FDF`) — eine F- und eine C-Auslieferung
könnten einander im Netz finden.

⚠ Ein echter Schnitzer in beiden: `push 0x7F00; call SetCursor` — dort wird die
**Ressourcennummer** `IDC_ARROW` als Zeigerkennung übergeben (`0x403B93`, `0x4194A4`).

### AV.11 ⭐ Der Befehlsbus — und eine Grösse, die aufs Byte aufgeht

`Message-buffer is full!!!!` (C `0x4C5F50` / F `0x4C5B00`) ist die
**Gleichschritt-Sperre** des Mehrspielerbetriebs. Ein Aufrufer: der
Befehlsbus-Verteiler C `0x4C253E`.

⭐ **Opcodes ≤ 1000 laufen über die Sperre, Opcodes > 1000 örtlich.**

Der Ring: Basis C `0xB509D8` / F `0xB4FA38`, Satzweite **236 B** (`lea`-Kette
`i→7i→29i→59i`, dann `·4`), Umlauf **1000** (`ring_weiter` C `0x4C2070`).

> ⭐⭐ **Die Grösse geht aufs Byte auf, zweimal unabhängig:**
> `1000 × 236 = 236 000 = 0x399E0`.
> C: `0xB509D8 + 0x399E0 = 0xB8A3B8` — genau die 8-Byte-Spielerflaggentafel, die
> `WinMain` @`0x4151E9` beschreibt.
> F: `0xB4FA38 + 0x399E0 = 0xB89418` — genau die Stelle in F `0x415029`.
> **Nullmodell:** dass der ausgerechnete Ringschluss in beiden Bauten zufällig
> auf eine unabhängig bekannte Nachbarveränderliche fällt, ist ausgeschlossen.

⚠ **Und danach wird nicht abgebrochen — der Spieler sieht aber doch etwas:** auf
die stumme Protokollsenke folgt ein **echtes `MessageBoxA`** (»Message buffer is
full(2) «), das **nicht** über `meldung()` läuft und darum **nicht** vom
Entwicklerschalter abhängt. Danach läuft die Schleife weiter.

Die Sperre wartet höchstens **2000 ms** (`timeGetTime + 0x7D0`, C `0x4C5FC8`),
führt für jeden der acht Plätze eine Anwesenheitsflagge, und **`0x44C = 1100`**
ist der Takt-Bestätigungsbefehl.

### AV.12 ⭐ Der Netz-Vollabgleich `0.tmp` — und er wird nie gerufen

`0x4C5C30` / F `0x4C57E0`, 629 B: schreibt den **kompletten Spielstand** nach
`0.tmp` und schickt ihn in Blöcken zu `0x7D0 = 2000` B über den Bus, in zehn
Häppchen zu `0x32·4 = 200` B, mit Befehl **`0x2BC = 700`** — genau dem
Sonderzweig, den `opcodes.py` seit dem 08.08. führt. Dazu ein Fortschrittsbalken
(`0x444180`), passend zu Fensterart 47 »Synchronisieren…«.

⚠⚠ **Und er ist tot.** 0 direkte Aufrufer, 0 Relokationsverweise, auch über
seinen Stummel — in **beiden** EXE. Dieselbe Signatur wie `sejmi 1` und `JOJO`.

⭐ Ein Nebenfund von grossem Wert: **`0x41D210` ist der Spielstandschreiber**
(2934 B, Kennung `"CWM"`). Er wird auch mit `"replay.tst"` gerufen (C `0x416D75`)
bei Takt **`0x1388 = 5000`**, danach `PostQuitMessage` — **der
Determinismus-Prüfstand des Originals schreibt bei Takt 5000 einen vollen
Spielstand und beendet das Programm.**

### AV.13 ⭐ `JOJO` — 21 Byte, unerreichbar

```
push 0 ; push "JOJO" ; push "Chyba" ; push 0 ; call MessageBoxA ; ret
```
0 direkte Aufrufer, 0 Relokationsverweise, und `"Chyba"` (tschechisch »Fehler«)
wird im ganzen Bild **nur hier** benutzt. Ein Entwicklerscherz (»jojo« ≈ »jaja«)
unter dem Titel »Fehler«, unerreichbar wie `sejmi 1`.

### AV.14 `Multi:` — und der Mechanismus hinter dem Fehler von AT.4

`0x418DE0` / F `0x418C20`, 905 B: baut `net01.cwm` … `net08.cwm`
(`cmp al,9; jb`), sucht sonst auf der CD unter `<Laufwerk>:\levels\`.

⭐ **Und der 53-Byte-Kopf aus AT.2 geht restlos auf** — die Lesefolge verbraucht
ihn aufs Byte:

| Kopfversatz | Bytes | wohin |
|---|---|---|
| `+0x00` | 2 | verworfen |
| `+0x02` | 1 | ⭐ **Zahl der belegten Plätze** (AT.2) |
| `+0x03`, `+0x04` | 1 + 1 | örtlich |
| `+0x05` | 21 | örtlich |
| `+0x1A` | 2 | ⭐ **Index in die deutsche Namenstafel `0x4F805B`** |
| `+0x1C` | 21 | `0x77CAD0` |
| `+0x31`, `+0x33` | 2 + 2 | Kartentafel `+0x2A` / `+0x2C` |
| | **= 53** | |

→ **Das ist der Mechanismus hinter AT.4:** NET06/07/08 tragen im Kopf die
Indizes 56/57/58, und dort steht in der Namenstafel nichts.

Die Gefechtskartentafel ist `0x5407A8` / F `0x53F808`, **20 × 50 B = 1000 B** —
unmittelbar über den Gefechtseinstellungen aus AT.3.
`»Too many multiplayer levels«` feuert bei 20 (`cmp al,0x14`), läuft über
`meldung()` (also **stumm**), **bricht nicht ab** — und kann bei nur 8 gesuchten
Dateien ohnehin nie auslösen.

### AV.15 `MEMORY INFO` — sieben Felder, und unerreichbar

`0x418BB0` / F `0x4189F0` blendet Überschrift plus **sieben** Zahlen aus einer
`MEMORYSTATUS` bei C `0xB97AB0` / F `0xB96B10` ein: `MemoryLoad` `+0x04` …
`AvailVirtual` `+0x1C`.

> **Zahl:** 7 von 7 Wertfeldern, in exakter Reihenfolge, auf lückenlos
> aufeinanderfolgenden Doppelwortversätzen ab `+4`.

**Aber** der Aufruf hängt an `byte[0x4F6FB8]`, und das hat **genau einen
Verweis — den Lesebefehl** — bei Anfangswert 0. Keine Taste, kein Befehl setzt
es. ⭐ **Im ausgelieferten Programm nicht erreichbar**, beide EXE.
(Der grosse Einheiten-Auszug daneben hängt dagegen an `dword[0x4F6FD0]`, und das
schaltet die Taste **I** um.)

### AV.16 `Search:` — die Wegsuche, ein Auftrag je Takt

`0x4D3810` / F `0x4D33A0`, 3656 B in beiden, gerufen **genau einmal je
Simulationsdurchlauf**. Auftragsring: Nummern (u16) `0xBDA0E8`, Art (u8, `0xFF` =
leer) `0xBDA8C0`, Zeiger `word[0x539B10]` / `[0x539B14]`.

> ⭐ **Die Ringlänge 1000 steht dreifach:** im Code (`cmp …,0x3E8`), und im
> Spielstandschreiber, der `0x7D0 = 2000` B ab `0xBDA0E8` (= 1000 × u16) und
> `0x3E8 = 1000` B ab `0xBDA8C0` (= 1000 × u8) sichert.

Je Takt wird **genau ein** Auftrag abgearbeitet — der erste ab dem Lesezeiger,
dessen Besitzer nicht ausgeschieden ist und dessen Einheit lebt.

### AV.17 ⚠ Was offen blieb, und wodurch das Verfahren blind ist

* **Berechnete Adressen.** Alle »genau ein Verweis«-Aussagen (`0x4F6FB0`,
  `0x4F6FB8`, `0x54072C`) stützen sich auf die Relokationstafel, die nur
  **literale** absolute Adressen aufzählt.
* **Das Programm lief nicht.** Die zentrale Aussage von AV.2/AV.3 ist eine reine
  Codelesung — siehe den Prüfstand in AV.5.
* ⭐ **Der Vorgabewert von `byte[0x4FA23C]` (Geschwindigkeit) ist ungelesen.**
  Das ist **die eine Zahl, die für den Nachbau des Takts noch fehlt**; sie steht
  vermutlich in `options.cfg` oder in `0x447040`.
* `0x4C2280` (11 601 B) wurde nur an den Rändern gelesen — wer den Gleichschritt
  nachbauen will, muss dort hinein.
* Die Felder `+0x2A`/`+0x2C` der Gefechtskartentafel sind als »zwei Worte«
  gelesen, ihre Bedeutung nicht. »Breite/Höhe« ist **Wortlaut, kein Befund**.
* Warum die Sprungtafelfläche in `WndProc` bei F 24 B grösser ist, obwohl F acht
  Arme **mehr** hat, wurde nicht aufgerechnet — es sind Daten hinter dem `ret`,
  und die Regel verlangt, solche »Unterschiede« zu verwerfen.

⚠⚠ **Und eine Selbstkorrektur des Laufs, die für alle künftigen Läufe gilt:**
das erste Werkzeug erkannte Funktionsgrenzen an **einem** `0xCC`. Das ist falsch
— `0xCC` kommt massenhaft als Adressbyte vor (z. B. `push 0x4F79CC` @`0x41684D`).
Dadurch zerfiel `Main_funct` scheinbar in **neun** Funktionen mit »0 Aufrufern«,
die in Wahrheit **eine** 8713-Byte-Funktion sind. **Die Polsterung ist erst ab
DREI aufeinanderfolgenden `0xCC` verlässlich.** Wer in älteren Notizen »auf = 0«
zu einer Adresse zwischen `0x415CF0` und `0x417EF9` findet, muss dort nachrechnen.

### AV.18 ⚠ Eine Lücke, die beim Bauen des Sektorenrasters auffiel

Unser Ausleser exportiert **sec62 gar nicht.** In `map_09.entities.json` gibt es
weder ein `imp`-Feld am Gebäude noch eine eigene Tafel; `targets` ist eine leere
Liste. Damit ist beim Bau von `AiSetImpCpu` (Abschnitt AU.4) `sec110[p] == 0`
für **jeden** Spieler in **jeder** Kampagnenmission — und `sec110 == 0` ist
genau die Bedingung, unter der das Original in den **»Take all«**-Zweig geht und
gar keine Verteidiger zurückhält.

⭐ Es ist also nicht bloss ein fehlendes Feld: **die fehlende Tafel kippt den
Gegner in eine andere Betriebsart.** Zu tun ist zweierlei:
1. sec62 aus den 13 `.DM` mit ausexportieren (8 × 255 × 2 B, gemessene Werte
   6/4/9/2 — siehe AU.4),
2. bis dahin steht in `AiImpVon` eine benannte Ersatzregel: im Gefecht **6** je
   eigenem Gebäude (der Wert, der im Original 311 von 324 Fällen ausmacht), in
   der Kampagne 0. **Die Ersatzregel ist unsere, nicht die des Originals.**


### AV.19 Adressverzeichnis zur Hauptschleife

⚠ **Warum es hier steht.** Beim Verdichten des Laufs sind die meisten
Funktionsadressen weggefallen — der Text las sich besser und war schlechter zu
benutzen. `funktionen.py` hat es sofort gezeigt: `Flip pages`, `Get run begin`,
`JOJO`, `Achtung` und `pratelsky, cis:` galten weiter als »nirgends erwähnt«,
obwohl sie oben beschrieben sind. **Dieselbe Lektion wie bei AQ, und sie gilt
weiter: eine Zusammenfassung ohne Adressen ist eine Erzählung, kein
Nachschlagewerk.**

| Was | C | F | Grösse |
|---|---|---|---|
| **`WinMain`** (Nachrichtenschleife) | `0x414E20…0x4156C2` | `0x414C60…0x4154FE` | 2210 / 2206 B |
| **`WndProc`** (Fensterverfahren) | `0x412E30…0x414774` | `0x412C00…0x41459C` | ⚠ 6468 / 6556 B (Sprungtafel hinter dem `ret`) |
| **`Main_funct`** (ein Bild) | `0x415CF0…0x417EF9` | `0x415B30…0x417D35` | 8713 / 8709 B |
| `WM_TIMER`-Arm | `0x414010` | `0x413E25` | 4 Befehle |
| **`flip_ddraw`** | `0x415CB0…0x415CE3` | `0x415AF0…0x415B23` | 51 B |
| **`bildpuffer_leeren`** | `0x419250…0x4192CB` | `0x419090…0x41910B` | 123 B |
| **`Flip pages`** (eigenständiges Bild) | `0x4192F0…0x419374` | `0x419130…0x4191B4` | 132 B |
| **`Get run begin`** (Netzstart) | `0x4193A0…0x41992B` | `0x4191E0…0x41976B` | 1419 B |
| `doInit 1` | `0x4158F0…0x415BE8` | `0x415730…0x415A28` | 760 B |
| `Multi:` (Gefechtskarten einlesen) | `0x418DE0…0x419169` | `0x418C20…0x418FA9` | 905 B |
| `MEMORY INFO` | `0x418BB0…0x418CAC` | `0x4189F0…0x418AEC` | 252 B |
| ⭐ `CWorms Player` (DirectPlay) | `0x403B60…0x403CF4` | `0x403B40…0x403CD4` | 404 B |
| ⭐ `JOJO` (unerreichbar) | `0x428EC0…0x428ED5` | `0x4280B0…0x4280C5` | 21 B |
| `Search:` (Wegsuche je Takt) | `0x4D3810…0x4D4658` | `0x4D33A0…0x4D41E8` | 3656 B |
| `Message-buffer is full!!!!` | `0x4C5F50…0x4C631D` | `0x4C5B00…0x4C5ECD` | 973 B |
| `0.tmp` (Vollabgleich, **tot**) | `0x4C5C30…0x4C5EA5` | `0x4C57E0…0x4C5A55` | 629 B |
| `d:\screen.bmp` **lebend** | `0x418B00…0x418B89` | `0x418940…0x4189C9` | 137 B |
| `d:\screen.bmp` **tot** | `0x4226C0…0x422AC8` | `0x421880…0x421C8C` | 1032 / 1036 B |
| **`spielstand_schreiben(name)`** | `0x41D210` | `0x41C3D0` | 2934 B |
| `ring_weiter(&idx)` (Umlauf 1000) | `0x4C2070…0x4C2089` | `0x4C1B30…0x4C1B49` | 25 B |
| `cd_stop()` (MCI_STOP) | `0x4D50C0` | `0x4D4C50` | 41 B |
| `cd_close()` (MCI_CLOSE) | `0x4D4F70` | `0x4D4B00` | 31 B |
| `palette_animieren(primär)` | `0x4B5BF0…0x4B5C44` | `0x4B5520…` | 84 B |
| Taste **I** (Einheiten-Auszug) | `0x413622` | `0x4133F2` | — |
| Taste **F11** | `0x413DDC` | `0x413BF1` | — |
| Taste **Strg+R** (`c:\cw_log.txt`) | `0x413708` | `0x4134D8` | — |
| ⭐ Taste **ROLLEN** (Bildschirmfoto) | `0x413E2E` | `0x413C43` | — |
| `WM_KEYDOWN`-Kopf / `WM_KEYUP` | `0x412F97` / `0x413E45` | `0x412D67` | — |
| Befehlsbus-Verteiler | `0x4C2280` | `0x4C1D40` | 11 601 B |
| » Geschwindigkeitsdeckel `0x14` | `0x4C4420` | — | — |
| » Geschwindigkeit setzen | `0x4C443E`, `0x4C444E` | — | — |
| Vollabgleich absenden | `0x4C2160` | — | — |
| »Error reading network message« | `0x404480` | — | — |
| Fortschrittsbalken | `0x444180` | — | — |
| `pratelsky, cis:` (Aufrufer der Freund/Feind-Probe) | `0x4054D0` | `0x4054B0` | — |
| `Achtung` (Meldungsfenster) | `0x418CF0` | `0x418B30` | — |
| `Selected level not found!` | `0x419A90` | — | — |
| `There is no place to appear` | `0x419B20` | — | — |
| `Cannot add more probr structures` | `0x41C180` | — | — |
| `Internal error: No group selected` | `0x436910` | — | — |
| `Out of map!` | `0x43A1B0` | — | — |
| `Developers' cheats disabled` | `0x43AE50`, Umschalter `0x43AF5A` | — | — |
| `Drücken Sie Alt-F4 zum Beenden` (CD-Laufwerksbuchstabe) | `0x43B580` | — | — |
| `error 4` | `0x43BF00` | — | — |
| `Place encyc` | `0x442B30` | — | — |
| `Post quit message` | `0x4AA1E0` | — | — |
| `Error!!!!` | `0x4ABDB0` | — | — |
| `handle is null` / `createcompatible dc failed` | `0x4C8400` / `0x4C84B0` | — | — |
| `status playerSnd position` / `open sequencer!%s alias playerSnd` | `0x4D5600` / `0x4D5660` | — | — |

**Veränderliche**

| Was | C | F |
|---|---|---|
| Zeitgeberzähler (WM_TIMER) | `byte[0x53920C]` | `byte[0x538244]` |
| ⭐ **Zwangstakt-Schalter** (Anfangswert **1**) | `byte[0x4F6FB0]` | `byte[0x4F5FAC]` |
| Taktfreigabe | `byte[0x4F7F60]` | `byte[0x4F6F40]` |
| ⭐ **Spielgeschwindigkeit** (Durchläufe je Bild) | `byte[0x4FA23C]` | `byte[0x4F9244]` |
| Takte seit Missionsbeginn | `dword[0x4FA240]` | `dword[0x4F9248]` |
| Pause-/Endewort | `word[0x4FA280]` | `word[0x4F9288]` |
| Netzspiel/Determinismus | `dword[0x539234]` | — |
| Bildzähler / letzte Messsekunde | `dword[0x54074C]` / `dword[0x540724]` | `dword[0x53F7AC]` / `dword[0x53F784]` |
| ⭐ **gemessene Bilder/s** (nirgends gelesen) | `dword[0x54072C]` | `dword[0x53F78C]` |
| primäre DD-Oberfläche / Rückpuffer | `dword[0x540770]` / `dword[0x540744]` | `dword[0x53F7D0]` / `dword[0x53F7A4]` |
| `IDirectDraw`-Objekt / Fensterkennung | `dword[0x540730]` / `dword[0x540748]` | `dword[0x53F790]` / `dword[0x53F7A8]` |
| Breite / Höhe des Puffers | `dword[0x5387C8]` / `dword[0x5387CC]` | — |
| Tastenzustandsfeld, 256 B | `0xA182E8` | `0xA17348` |
| Tastenindextafel (137 B) / Sprungtafel | `0x414644` / `0x4145A4` (40 Arme) | `0x414474` / `0x4143B4` (**48** Arme) |
| Befehlsring (1000 × 236 B) | `0xB509D8` | `0xB4FA38` |
| Ring-Zeiger schreiben / füllen / ausführen | `word[0x539254]` / `[0x539258]` / `[0x53925C]` | — / `[0x538290]` / `[0x538294]` |
| Kratzbefehl (236 B) / Spielerflaggen (8 B) | `0xB8A3D8` / `0xB8A3B8` | `0xB89438` / `0xB89418` |
| Wegsuch-Auftragsring (u16 / u8) | `0xBDA0E8` / `0xBDA8C0` | `0xBD9148` / `0xBD9920` |
| Wegsuche schreib / lese | `word[0x539B10]` / `[0x539B14]` | `word[0x538B78]` / `[0x538B7C]` |
| Wegsuchzähler / Sekundenkopie | `dword[0xBCA010]` / `dword[0x539B18]` | — |
| Gefechtskartentafel (20 × 50 B) | `0x5407A8` | `0x53F808` |
| deutsche Kartennamen (21 B je Eintrag) | `0x4F805B` | `0x4F703B` |
| `MEMORYSTATUS` | `0xB97AB0` | `0xB96B10` |
| DirectPlay-GUID | `0x4F01C0` | `0x4EF1C0` |
| Entwicklerschalter | `byte[0x4FA0C0]` | `byte[0x4F90C8]` |
| eigene Spielernummer | `byte[0x4FA284]` | `byte[0x4F928C]` |
| ⭐ `MEMORY INFO`-Schalter (**nie gesetzt**) | `byte[0x4F6FB8]` | `byte[0x4F5FB4]` |
| Schalter des Einheiten-Auszugs (Taste I) | `dword[0x4F6FD0]` | `dword[0x4F5FCC]` |
| Schalter der Taste F11 | `dword[0x4F6FCC]` | `dword[0x4F5FC8]` |
| Einheitentafel (**8000 × 78 B**) | `0x6E26C8…0x77AC51` | — |

---

## AW. ⭐⭐ DIE FÜNF LETZTEN RÄTSEL DES DATEIFORMATS (21.08.2026)

Alle Zahlen aus 23 `.CWM` + 13 `.DM` und aus der **Vollerhebung über die
Relokationstafel** beider EXE. Das Werkzeug wurde vorher an sec58 geeicht
(C `0xB38D40` → 2 Schreiber / 4 Leser, F genauso).

### AW.0 Adresstafel

| Sache | C | F |
|---|---|---|
| sec20 Lagentafel | `0x542E18` | `0x541E78` |
| sec22 Gleiszellen (15 000 B, 5-B-Sätze) | `0xC2C220` | `0xC2B280` |
| sec34 Linien (80 × 214) | `0xA89220` | `0xA88280` |
| sec35 Belegungsbitfeld (16 481 B) | `0xA8D8C8` | `0xA8C928` |
| ⭐ **sec122 Linien-y (80 × 2 × u16)** | `0xA66DD8` | `0xA65E38` |
| sec3 Gebäude (**300 × 76**) | `0xC06910` | `0xC05970` |
| sec6 Handhabungsgitter | `0xBDEA80` | `0xBDDAE0` |
| **sec67** (8 B) | `0xB38528` | `0xB37588` |
| **sec77** (8 B) | `0xB46198` | `0xB451F8` |
| **sec112** `fly_part` (72 000 B) | `0xA51110` | `0xA50170` |
| Speicherer / Lader | `0x41D210` / `0x41E070` | `0x41C3D0` / `0x41D230` |
| Gebäudetakt (`doors`·`mines`·`mining`) | `0x43CA50` | `0x43BAF0` |
| » Schreiber **99** | `0x43CB12` | `0x43BBB2` |
| » Schreiber **98** | `0x43DE88` | `0x43CE94` |
| » Typumschalter / Tafel / Indexkarte | `0x43DC4E` / `0x43ECAC` / `0x43ECD8` | `0x43CC53` / `0x43DCC4` / `0x43DCF0` |
| Prüfer »sec20 > 99« (Thunk C `0x4023C9`) | `0x4CE6E0` | `0x4CE290` |
| Prüfer »sec20 ≥ 200« | `0x4CF100` | `0x4CECA0` |
| sec77-Setzer (Thunk C `0x40220C`) | `0x4D0970` | `0x4D0520` |
| sec109- / sec131-Setzer (Thunks `0x40123F` / `0x4020A9`) | `0x4D0F00` / `0x4D0EE0` | — |
| sec112 Nullung `rep stosd` 72 000 B | `0x41EFA9` | `0x41E164` |
| sec112 Schranke **Satz 1000** | `0xA59DB6` | `0xA58E16` |
| » im Aktualisierer / im Zuteiler | `0x42EBE8` / `0x4AD8C7` | `0x42DDB0` / `0x4AD1F7` |
| » dritte Schranke `cmp si, 0x3E8` | `0x4AE4A2` | `0x4ADDD2` |
| Schritttafel der Linien (12 × i8,i8) | `0x5043C0` | — |

### AW.1 ⭐⭐ sec67 ist der ZWEITE tote Abschnitt

**Genau vier Fundstellen, und alle vier liegen im Speicherer oder im Lader** —
zwei davon sind die **Nullung des Laders selbst**:

```
C  0x41D87A  push 0xB38528              (Speicherer)
   0x41E8F5  push 0xB38528              (Lader)
   0x41F02D  mov dword[0xB38528], edx   ┐ die Nullung, zwei Doppelworte
   0x41F033  mov eax, 0xB38528          ┘ 0x41F042: mov dword[eax+4], edx
F  0x41CA3A / 0x41DAB4 / 0x41E1EC / 0x41E1F2 — befehlsgleich
```

| Aussage | Zahl | Nullmodell |
|---|---|---|
| Fundstellen **ausserhalb** von Lader/Speicherer | **0 von 4** | über alle 130 Abschnitte trifft »0 ausserhalb« auf **2 zu = 1,5 %** |
| belegte Bytes in den `.DM` | **0 von 8, in 13 von 13** | — |
| 4 Fundstellen ist Bodenniveau | nur 6 von 130 haben ≤ 4 | **Median 12** |

⚠ Und die Gegenprobe, dass 4 Fundstellen sonst *nicht* »tot« heisst: sec64/65/66
haben ebenfalls je 4 — aber zwei davon sind echt (`mov ecx,[0xBC5684]` @`0x4160CC`
und zurück @`0x4160E5`, beide im Hauptdurchgang). Bei sec67 gibt es kein
solches Paar.

⚠⚠ **BERICHTIGUNG AN ABSCHNITT AJ.** Dort steht »130 Abschnitte, **ein einziger**
wirklich toter — sec36«. **Falsch, es sind zwei.** Der damalige Lauf suchte nach
»2 Fundstellen«; sec67 hat 4. Das richtige Kriterium lautet **»keine Fundstelle
ausserhalb von Lader `0x41E070` und Speicherer `0x41D210`«** — und danach sind es
sec36 (2/2) und **sec67 (4/4)**.

⭐ **Die Nachbarschaft ist ein Indiz:** sec67 sitzt zwischen `0xB38520` (dem
u16-Kopf der Warteschlange des Sektor-Wegsuchers, `0x4BEA30`) und `0xB38530`
(der 1000er-Kandidatenliste der KI) — **mitten im KI-Kladdenspeicher**, als
einziges davon in der Ladertafel. Das riecht nach einem Feld, das beim Umbau
der KI herausfiel und dessen Zeile in Lader und Speicherer stehenblieb.

⚠ Blockbefehle geprüft: von 3 963 `rep` in C sind 181 mit auflösbarem Ziel;
**keiner** deckt `0xB38528`. Der `rep stosd` nebenan (`0x41F047`, `edi=0xB36BE0`,
`ecx=0x650`) trifft exakt sec68 (6 464 B) und hört bei `0xB38520` auf.

**Für den Nachbau: acht Nullbytes lesen, acht schreiben. Mehr ist nicht drin.**

### AW.2 ⭐⭐ sec77 — 119 Schreibstellen, KEIN Leser

```
C 0x4D0970 (Thunk 0x40220C):  xor ecx,ecx ; al=[esp+4] ; cl=[esp+8]
                              mov byte[ecx + 0xB46198], al ; ret
```
Also `sec77[Spieler] = Wert`. ⭐ **Die Rechnung geht auf:** die Rücksetzschleife
@C `0x488406` endet auf `cmp esi, 8` — **8 B / 1 B je Spieler = 8, restlos.** In
den 13 `.DM` kommen nur **0 und 1** vor.

⭐ **Die Familie ist gefunden** — gleichgebaute Setzer, die nur das
Missionsskript ruft:

| Abschnitt | Setzer C | Thunk | Rufstellen | Zustand |
|---|---|---|---:|---|
| sec61 | `0x4D1050` | `0x402022` | 72 | Betriebsart der KI — **wird gelesen** |
| sec106 | `0x4D09F0` | `0x40237E` | 14 | Skriptsperre — **wird gelesen** |
| **sec77** | `0x4D0970` | `0x40220C` | **119** | ⚠ **kein Leser in beiden EXE** |
| sec109 | `0x4D0F00` | `0x40123F` | — | — |
| sec131 | `0x4D0EE0` | `0x4020A9` | — | — |

| Aussage | Treffer | Nullmodell |
|---|---|---|
| `sec61[p] ≠ 2` ⇒ `sec77[p] == 1` | **12/12 = 100 %** | dieselben Spieler mit `sec77[p]==0`: **0/76 = 0 %** |
| sec61-Setzer innerhalb 48 B einer sec77-Rufstelle mit **demselben** Index | **38/47 = 81 %** | zufällige Indexpaarung: **16 %** |

**Ergebnis: sec77 ist die dritte Skriptsperre derselben Familie — »Spieler p ist
eine vom Skript eingeschaltete KI«. Wirkung hat sie in keiner der beiden
Auslieferungen, weil kein Befehl sie liest.** Für den Nachbau: mitschreiben,
sonst ignorieren.

⚠⚠ **BERICHTIGUNG AN ABSCHNITT AL.4.** Dort steht »`sec77[p]==1 ⇒
sec53[p].Zustand == 1` in **28 von 28**«. Die Zahl stimmt, **aber sie trägt fast
nichts**: von den 76 Plätzen mit `sec77[p]==0` erfüllen **63** dieselbe
Bedingung. **Das Nullmodell ist 83 %, nicht 0 %.** 28/28 darunter hat eine
Zufallswahrscheinlichkeit von 2,3 % — das ist keine Entdeckung, das ist ein
Streifschuss. Die Aussage gehört mit ihrer Gegenzahl ins Dokument oder gar nicht.

⚠ **Ein Verdacht, ausdrücklich NICHT als Auslieferungsunterschied gemeldet:**
der Setzer hat in C **119**, in F **123** Rufer. Da sec77 keinen Leser hat, wäre
der Unterschied ohnehin wirkungslos — und das Funktionsanfangsraten des Laufs
liefert im Skriptbereich 13 Anfänge in C gegen 7 in F, ist dort also unsicher.
**Verdacht, nicht Befund.**

### AW.3 ⭐⭐ Das untere Band von sec20 ist die LINIENNUMMER

> **`sec20[Spalte·256 + Zeile] == n` mit 1 ≤ n ≤ 97 heisst: auf dieser Zelle
> liegt das Gleis der Verkehrslinie mit dem Index `n − 1`.**

Damit ist die Wertetafel von sec20 **lückenlos**:

| Wert | bedeutet |
|---|---|
| 0 | gewöhnlicher Boden |
| **1 … 80** | **Gleis der Linie `n−1`** (sec34 hat 80 Plätze → 80 Werte, geht auf) |
| **98** | gesperrte Zelle (AW.4) |
| **99** | **Türzelle eines Gebäudes** (AW.4) |
| 100 + n | Brücke/Mole n (sec17) |
| 200 + n | Rampe n (sec21) |

Der Prüfer C `0x4CE6E0` (`cmp byte[…+0x542E18], 0x63; seta al`) trennt genau bei
99: **alles ≤ 99 ist Boden/Gleis/Tür, alles > 99 ist Bauwerk.**

**Der Beleg läuft über sec22** (die Gleiszellenliste, 5-B-Sätze: x, y, ·, ·, Linie):

| Aussage | Treffer | Nullmodelle |
|---|---|---|
| Jede sec20-markierte Zelle steht in sec22 unter genau der Linie `Wert−1` | **7 872 / 7 872 = 100,00 %** | Linie `Wert`: **0,88 %** · `Wert−2`: **0 %** · zufällig: **1,96 %** |
| Zahl der echten Linien je Datei == höchster Bandwert | NET03 **10/10**, NET02 **33/33**, NET08 **43/43** | — |
| Höchster je beobachteter Wert | **43** — alle Plätze 60…79 haben `delka == 0` | — |

⭐ **Die Enden bleiben frei, und zwar exakt:** über die 215 echten Linien tragen
die **ersten zwei und die letzten zwei** Zellen jeder Route `sec20 == 0` —
**430 von 430 = 100 %, keine Ausnahme.** Das ist die Andockstrecke vor dem
Gebäude, und dieselbe Trimmung findet sich in sec35 (AW.5): beide Felder werden
von derselben Routine geschrieben.

⚠ **Wodurch der Fund blind ist:** die Marke *lesende* Seite wurde nicht
gefunden. sec20 hat 96 Fundstellen in C; die vier benannten Prüfer testen auf
`> 99`, `≥ 200`, `≥ 100` und `!= 0`. **Der Fund steht auf den Daten, nicht auf
dem Kontrollfluss.**

### AW.4 ⭐⭐ `sec20 == 99` ist die TÜRZELLE — 813/813

Beide Sonderwerte werden an **je genau einer Stelle** geschrieben, beide im
Gebäudetakt (C `0x43CA50`, Protokollmarken `doors`, `mines`, `mining`).

**99 (`0x63`) — C `0x43CB12`:**
```
cl = Gebäude[+0x0A]                      ; ein Zustandszähler
cmp cl, 0x63 ; jbe raus                  ; nur oberhalb 99
... jeden zweiten Takt: cl++ ...
cmp cl, 0xFA ; jne raus                  ; bei 250:
Gebäude[+0x0A] = 1
Zelle = ((Gebäude.x + [+0x35]) << 8) + Gebäude.y + [+0x36]
sec6[Zelle]  = 0xFFFE                    ; Zelle FREIGEBEN
sec20[Zelle] = 99
```

| Aussage | Treffer | Nullmodell |
|---|---|---|
| Jede `sec20 == 99`-Zelle liegt auf `(x + [+0x35], y + [+0x36])` eines Gebäudes | **813 / 813 = 100,0 %** | zufällige Zellen gleicher Anzahl: **0,12 %** |

⭐ **Und die alte Notiz fügt sich:** »aus Geländeklasse 99 darf nicht geschossen
werden« (C `0x40BB17`, einer der Auslieferungsunterschiede) heisst also
wörtlich: **aus einer Toröffnung heraus wird nicht geschossen.**

⚠⚠ **BERICHTIGUNG: sec3 ist `300 × 76 B OHNE KOPF`**, nicht »4 B Kopf +
255 × 76«. `22 800 = 300 · 76`, geht auf. Basis `0xC06910 + i·76`:
**x bei +0x00, y bei +0x02, Typ bei +0x04** (je als Byte gelesen),
Zustandszähler **+0x0A**, Türversatz **+0x35/+0x36**, ein zweiter Versatz
**+0x38/+0x39**. Die falsche Notiz kostet einen Satzversatz von 4 Byte **und**
eine falsche Satzzahl.

**98 (`0x62`) — C `0x43DE88`:**
```
Zelle = ((Gebäude[+0x38] + …) << 8) + Gebäude[+0x39] + …
cmp word[Zelle*2 + sec6], 0x1F40 ; jbe raus   ; NUR wenn dort ein Landschaftsobjekt (>8000) steht
sec20[Zelle] = 98
sec6[Zelle]  = 0xFFFF                          ; Zelle SPERREN
```
Erreicht über die Sprungtafel C `0x43ECAC` mit Indexkarte `0x43ECD8`
(`edx = Gebäudetyp − 1`) — und zwar **nur für Gebäudetyp 2, 3 und 4**.

| Aussage | Treffer | Nullmodell |
|---|---|---|
| 98-Zellen, die zugleich Gleiszellen sind, haben `sec6 == 0xFFFF` | **1 600 / 1 600 = 100,0 %** | Anteil aller Gleiszellen mit `0xFFFF`: **16,6 %** |
| Zellen mit Liniennummer haben `sec6 == 0xFFFE` (frei) | 7 870 / 7 872 = 99,97 % | — |

→ **98 ist die Marke einer Zelle, die ein Betriebsgebäude (Typ 2/3/4) beim
Ausbau von einem Landschaftsobjekt geräumt und dauerhaft gesperrt hat.** Wo die
Zelle sonst die Liniennummer trüge, ersetzt 98 sie — »Gleis, aber gesperrt«,
und liegt darum genau unter der reservierten 99 und über der grössten je
vorkommenden Liniennummer (43).

⚠ **Als Fehlschlag gemeldet, nicht als schwaches Ergebnis:** die Zuordnung
98-Zelle → *welches* Gebäude gelang nicht. Die naheliegende Probe trifft zu
77 %, aber das Nullmodell mit zufälligen Zellen trifft schon zu **24,5 %** —
**kein Signal.**

### AW.5 ⭐⭐ »42 von 215 Linien tragen kein Bit« — AUFGELÖST

> **Die y-Halbzeile einer Linie steht NICHT in `sec34 +0x03` / `+0x05`. Dort
> steht nur ihr NIEDERWERTIGES BYTE. Die vollen 16 Bit stehen in `sec122`**
> (C `0xA66DD8`, 320 B = 80 Linien × 2 × u16; `y1` bei `4·Linie`, `y2` bei
> `4·Linie+2`).

Halbzeilen laufen bis `2·H`; bei H > 127 **überläuft das Byte**. Genau dafür
gibt es sec122.

**Beide Verfahren nebeneinander, gleicher Datensatz (13 `.DM`, 215 Linien):**

| y-Quelle | Linien ohne ein einziges Bit | Laufpunkte mit gesetztem Bit |
|---|---|---|
| `sec34 +0x03` (ein Byte) | **34 von 215** | 3 616 / 6 036 = **59,9 %** |
| **`sec122` (u16)** | **0 von 215** | 4 949 / 6 036 = **82,0 %** |

⭐ **Und das Fehlstellenmuster ist perfekt, sobald y stimmt:**

| Aussage | Treffer | Nullmodell |
|---|---|---|
| Fehlstellen je Linie == genau **(2 vorn, 3 hinten)** | **215 / 215 = 100 %** | ein anderes Paar: **0/215** |
| Linien **ohne jede Lücke im Inneren** | 205 / 215 = 95,3 % | — |
| Endpunkt der Route trifft `(x2, y2)` | 457 / 469; die 12 Fehlschläge sind **ausnahmslos `.CWM`**, die kein sec122 tragen | — |

**Der zweite Teil des Widerspruchs schrumpft ebenfalls:** »40 % der gesetzten
Bits gehören zu keiner Linie« wird mit sec122 zu **11 – 19 % je Datei**. Die 66
übrigen Bits in `1.DM` (254 × 100, Halbzeilen 0…199) liegen bei Halbzeile
**242 … 402** und bilden in ihren Abständen (7, 13, 7, 13 …) eine saubere
Diagonale — **die Spur einer Linie aus einer grösseren Karte.**

⚠ Das passt zu dem, was schon dasteht: **zu sec35 gibt es Test und Setzen, aber
keinen Löscher.** Das Bitfeld sammelt an. **sec35 ist kein verlässlicher
Zellenindex, sondern eine wachsende Spur; verbindlich sind sec22 und sec20.**

⚠⚠ **BERICHTIGUNG AN AL.2:** »Strecke ab (+2,+3) endet auf (+4,+5)« ist
unvollständig. Und »42 von 215 Linien tragen kein Bit« ist **erledigt: 0 von 215.**

### AW.6 ⭐ sec112: die obere Hälfte ist ECHT — und diese EXE können sie nicht erzeugen

**Alle Schleifen enden bei Satz 1000, in beiden Fassungen, ohne Ausnahme:**

| Schleife | Schranke | C | F |
|---|---|---|---|
| Zuteiler | `cmp eax, Basis+6+36000` | `0x4AD8C7` | `0x4AD1F7` |
| Aktualisierer | `cmp edi/esi, Basis+6+36000` | `0x42EBE8` | `0x42DDB0` |
| dritte Schleife | `cmp si, 0x3E8` | `0x4AE4A2` | `0x4ADDD2` |

Und die **Relokations-Vollerhebung über `0xA51110 … 0xA62A50`** findet oberhalb
von Satz 0 **kein einziges weiteres Symbol** ausser der Schranke selbst.
→ **Kein Befehlspfad der beiden ausgelieferten EXE kann Satz 1000…1999
beschreiben.**

**Was dort trotzdem steht:**

| Datei | belegt unten | belegt oben |
|---|---:|---:|
| 13.DM | **1000 / 1000** | 419 |
| 2.DM | **1000 / 1000** | 547 |
| 6.DM | **1000 / 1000** | 1000 |
| die übrigen 10 `.DM` | 20 … 868 | **0** |

| Aussage | Treffer | Nullmodell |
|---|---|---|
| Die obere Hälfte ist auf **demselben 36-B-Raster** wohlgeformt (neun Feldproben) | **1 966 / 1 966 = 100,0 %** | dieselbe Probe **um 18 B phasenverschoben**: **14 / 1 965 = 0,7 %** |
| Obere Hälfte belegt **nur**, wenn die untere randvoll ist | **3/3** und **10/10** | drei beliebige der 13 Dateien: **0,35 %** |

→ Keine Trümmer eines Nachbarpuffers und keine Phasenverschiebung, sondern
**richtige `fly_part`-Sätze**, fast alle mit Art 0 (»Platz frei«) — also
**abgelaufene Teilchen**. Und sie treten genau dann auf, wenn der Zuteiler bei
Platz 1000 hätte aufgeben müssen.

⭐ **Schluss: diese drei Dateien wurden von einem Programm geschrieben, dessen
Teilchenschranke bei 2000 lag** — dem Kartenwerkzeug oder einem
Entwicklungsstand. Die 1000er-Schranke der Auslieferungen ist gegen die
Felddeklaration (72 000 B, die »Wrong size of fly_part« ja prüft) **um die
Hälfte zu klein.** Ein Deckel im Code, nicht im Datenformat.
⚠ Auf einen dritten Verhaltensunterschied geprüft: **nein**, F hat dieselbe
Schranke an denselben Stellen.

**Für den Nachbau: 2 000 Plätze Speicher, 1 000 aktive Plätze, obere Hälfte beim
Laden lesen und verwerfen.**

⚠⚠ **UND EIN VORBEHALT, DER FÜR JEDE MESSUNG AN `.DM`-LAUFZEITABSCHNITTEN GILT:**
die `.DM` sind offensichtlich **Momentaufnahmen aus laufenden Partien**, nicht
saubere Anfangszustände. **1 000 fliegende Trümmer hat kein Missionsanfang.**

### AW.7 ⚠ Was offen blieb, und wodurch das Verfahren blind ist

* **sec67 bleibt ohne Zweck.** Belegt ist, dass niemand es anfasst — nicht,
  wofür es gedacht war. Ein Negativbefund über Relokationen ist so hart, wie ein
  Negativbefund werden kann; bewiesen ist er nicht (eine vollständig berechnete
  Adresse bliebe unsichtbar).
* **»sec77 hat keinen Leser« heisst »keinen über eine absolute Adresse«.**
  sec57 (`0xB461A0`) schliesst lückenlos an; ein `mov edi, 0xB461A0` …
  `byte[edi−8+p]` wäre unsichtbar. Alle sieben Fundstellen im Fenster ±64 B
  wurden zerlegt: **alle greifen indiziert zu**, keine lädt eine Basis in ein
  Register.
* **Der Leser des sec20-Bandes fehlt** (siehe AW.3).
* **98 ist mechanisch, nicht semantisch geklärt** (siehe AW.4).
* ⚠ **`.CWM` haben kein sec122.** Für 23 der 36 Dateien ist die volle
  y-Halbzeile im Format schlicht nicht da. **Alle Linienmessungen aus AW.5
  stehen deshalb nur auf den 13 `.DM`**; AW.3 steht auf allen 36, weil sec22
  keine Halbzeilen benutzt.
* ⚠ **Die 13 `.DM` sind kein unabhängiger Datensatz.** `3/5/6/7/10.DM` sind
  Abkömmlinge derselben Karte — »13 von 13« wiegt weniger, als es aussieht.
  Die Kernaussagen stützen sich darum bewusst auf **zellweise** Zahlen
  (7 872, 813, 430, 215) statt auf Dateizahlen.
* ⚠ **Die Blockbefehlsprüfung löst nur 181 von 3 963 `rep` auf.** Für sec67,
  sec77, sec34 und sec35 ergab sie »nicht abgedeckt«; für sec112 fand sie den
  Nullsteller korrekt.

### AW.8 Nebenbefund, nicht verfolgt

`cwm_sections.py`: **sec89 und sec114 tragen beide `0x6786A8`** als Ziel — eines
der beiden ist falsch.

---

## AX. ⭐⭐ DIE SECHS LÜCKEN VON AU.14, GESCHLOSSEN (21.08.2026)

Alles unten steht in **beiden** EXE. Die Sprungtafeln wurden **aufgezählt**, nicht
abgetastet — die Blindstelle aus AU.14 wurde also beachtet.

### AX.0 Adressverzeichnis

| was | C | F |
|---|---|---|
| **Takt 46 »KI baut ihre Fabriken und Minen aus«** | `0x4BF760` | `0x4BF210` |
| dessen Sprungtafel (Ziele / Byteindex) | `0x4BF978` / `0x4BF988` | `0x4BF428` / `0x4BF438` |
| **Techstufe am Missionsanfang setzen** | `0x4407C0` | `0x43F7D0` |
| **Takt 48 »Teilespende«** | `0x4BE5C0` | `0x4BE080` |
| Tafel 1 (Zählen) / Tafel 2 (Verteilen) | `0x4BE6E0` / `0x4BE708` | `0x4BE1A0` / `0x4BE1C8` |
| 150er-Zähler (Tor für Takt 48) | `0x538BC0` | `0x537C00` |
| **Auftragswahl Betriebsart 5** | `0x4BDCC0` | `0x4BD780` |
| Gültigkeitstafel / Ausführungstafel (Art 1…4) | `0x4BE184` / `0x4BE194` | `0x4BDC44` / `0x4BDC54` |
| ⭐ **Luftangriff der KI (Takt 8, Schritt 2)** — neu | `0x4BDB40` | `0x4BD600` |
| Feindwahl dazu / Luftbefehl | `0x4BDA10` / `0x426020` | `0x4BD4D0` / `0x425200` |
| `angreife(…)` → UKOL 4 · `fahre(…)` → UKOL 2 | `0x410220` · `0x40B070` | `0x410050` · `0x40AF60` |
| **Gruppenfahrt / sec108** | `0x4BCF30` | `0x4BC9F0` |
| Tafel »Ziel noch gültig« / »Endanflug« | `0x4BD7BC` / `0x4BD7CC` | `0x4BD27C` / `0x4BD28C` |
| **Gruppe auflösen** | `0x4BCEA0` | `0x4BC960` |
| Richtungstafel des Rückwärtslaufs | `0x4BCD6C` | `0x4BC82C` |
| **sec55-Füller** / **sec56-Füller** | `0x4BA710` / `0x4BA7D0` | `0x4BA210` / `0x4BA2D0` |
| Nachbartafel 8 × (dx, dy, **Teiler**) | `0x538BE0` | `0x537C18` |
| ⭐ **Gebäudetyp-Namen, 20 B je Typ** | `0x4FDCB0` | `0x4FCCE8` |
| Besitzer 11 setzen (Skript/Netz) / (Tod) | `0x41B194` / `0x4BAC59` | `0x41A290` / `0x4BA640` |
| `obj_owner` (leerer Platz → **12**) | `0x4D0780` | `0x4D0330` |
| ⭐ **Sektor-Ankerzelle (121 × x,y)** | `0x541F60` | `0x540FC0` |
| Roboter **andocken** Basis / `Robot not found` | `0x43BFC0` / `0x43C120` | `0x43B130` / `0x43B2B0` |
| Bahnstation andocken / `Robot not found 2` | `0x43C370` / `0x43C430` | `0x43B500` / `0x43B5C0` |
| Depot andocken / `Robot not found 3` | `0x43C630` / `0x43C6F0` | `0x43B7C0` / `0x43B880` |

**Puffer** (C = F + `0xFA0`): sec23 `0x878E58` · sec24 `0x87A2C0` · sec25
`0x879F38` · sec28 `0x878AD0` · sec30 `0x879178` · sec53 `0x87B140`
(Bündniszeile `+0x15` → `0x87B155`) · sec55 `0xB461C0` · sec56 `0xB3D390` ·
sec60 `0xB400F0` · sec68 `0xB36BE0` · sec69 `0xBC5A78` · sec108 `0xB3CBD0` ·
sec123 `0xB45EB0`.

### AX.1 ⭐⭐ Takt 46 IST DER AUSBAU — und er trägt einen fehlenden `break`

`0x4BF760`, **145/145 Befehle gleich**.

```
Obergrenze L = min(7, Techstufe >> 1) + 2      ; Techstufe = byte[0x503AF0 + 2·Mission]
                                               ; bzw. byte[0x540EB8] im Gefecht
für i = 0 … 254 über die Gebäudeplätze:
   FABRIK (Typ 2,3,4): wenn L + (i mod 3) > sec24[cis].Stufe: Zähler(sec123[i])
   MINE   (Typ 10,15): wenn L + (i mod 3) > sec28[cis].Stufe: Zähler(sec123[i])
Zähler:  0 → 5 · 1 → AUSBAUEN, dann 0 · sonst → −1
AUSBAUEN Fabrik: Stufe++ · Lager += 10 · zwei Felder je ×3/2
AUSBAUEN Mine  : Stufe++ · Lager += 30 · zwei Felder je ×3/2
```

⭐ **sec123 (255 B) ist damit gelesen: ein Rückwärtszähler je Gebäudeplatz.**
Sechs Besuche je Ausbaustufe → **300 Bilder je Stufe**.

⭐ **Der Ausbau kostet nichts.** In der ganzen Funktion wird der Kontostand
`0xA9C600` nie berührt. Der Mensch zahlt (`0x44AD8A`, `0x44AE9F`, `0x44BBAB`),
**die KI nicht.**

⭐ **Das Gegenstück:** `0x4407C0` (66/66 gleich), gerufen am Missionsanfang, setzt
für **alle** Gebäude ohne Besitzerprüfung `Stufe = Techstufe>>1`,
`Lager = (Techstufe+4)·10`. **Die KI darf also 2 bis 4 Stufen über den
Missionsanfang hinaus ausbauen.**

#### ⚠⚠ Der fehlende `break` — in BEIDEN Fassungen und in BEIDEN Funktionen

Der Fabrikzweig endet **nicht** mit einem Sprung ans Schleifenende, sondern
**fällt in den Minenzweig durch** (`0x4BF8A2`→`0x4BF8A9`). Der Minenzweig
benutzt denselben `cis`-Index und arbeitet damit auf einem **fremden**
sec28-Satz. Dieselbe Gestalt in `0x4407C0` (`0x440869`→`0x440870`) und in F.
**Zwei Funktionen, zwei Fassungen, dieselbe Form: das ist ein fehlendes `break`
im Quelltext, kein Lesefehler.**

**Und die Wirkung ist nicht harmlos** — beide Zweige bedienen denselben Zähler:

| Besuch | sec123 vorher | Fabrikzweig | Minenzweig | nachher |
|---|---|---|---|---|
| 1 | 0 | setzt 5 | 5 → 4 | 4 |
| 2 | 4 | → 3 | 3 → 2 | 2 |
| 3 | 2 | → 1 | **1 → Mine ausgebaut**, 0 | 0 |

⭐ **Die Fabrik sieht die 1 nie.** Solange der gleichindizierte sec28-Satz noch
Luft hat, wird **nur die Mine** ausgebaut; die Fabrik kommt erst dran, wenn der
Minenzweig am Deckel abkürzt.

| Aussage (13 `.DM`) | Treffer | Nullmodell |
|---|---|---|
| in 1.DM: `Stufe = min(7,t/2)+2 + (Platz mod 3)` für KI-Fabriken | **10/10** | ohne den `mod 3`-Term: **2/10** |
| dasselbe für KI-Minen | **3/3** | — |
| sec24-Satz **gleich** dem sec28-Satz an den 15 Fabrik-`cis` | **13/15** | die 2 Ausnahmen sind exakt die 2 `cis` mit einer **echten** Mine |
| `sec28[+5] != 0` an genau den 15 Plätzen mit Mine **oder** Fabrik | **15/15** | ohne den Durchfall wären es nur die 5 Minen |
| Fabriklager +**10** je Stufe | 15/15 | Faktor 20: 5/15 · Faktor 30: 5/15 |
| Minenlager +**30** je Stufe | 15/15 | Faktor 10: 5/15 · Faktor 20: 5/15 |

⭐ Und der Zähler belegt sich selbst: in 1.DM ist **genau ein** sec123-Byte ≠ 0 —
`sec123[14] = 3`. Gebäude 14 ist eine **Mine**, die längst am Deckel steht. Sie
kam dorthin **nicht durch ihren eigenen Zähler**, sondern durch den Durchfall
der Fabrik auf Platz 17 (gleicher `cis`) — darum blieb ihr Zähler auf 3 stehen.

### AX.2 ⭐⭐ Takt 48 IST EINE ROHSTOFFSPENDE

`0x4BE5C0`, **79/79 gleich**.

```
wenn byte[0x538BC0] != 0: sofort zurück       ; das Tor
Schleife 1 über die 255 Gebäude von p:
   Typ 2 → a += 0x50 · Typ 3 → b += 0x50 · Typ 4 → c += 0x50 · Typ 10/15 → d += 0x50
Schleife 2 über die 255 Gebäude von p:
   Typ 1, 9, 16 → +0x2C += a ; +0x2E += b ; +0x30 += c     (die drei Teilelager)
   Typ 2, 3, 4  → +0x32 += 2·d                             (Terranium)
```

⭐ **Jede Basis, jeder Flughafen und jede Werft-Station bekommt 80 Teile je
Fabrik der passenden Art geschenkt; jede Fabrik 160 Terranium je Mine.** Keine
Produktion, kein Transport, kein Preis. Die Feldzuordnung ist unabhängig belegt:
`fill_resources` `0x419FE0` beschreibt dieselben Felder für dieselben Typen.

**Das Tor:** `byte[0x538BC0]` hat genau zwei Schreiber, beide in `ai_tick`
(`0x4BFBB2` `++`, `0x4BFBBB` bei ≥ 150 auf 0).
⭐ **Die Spende fällt einmal je 150 × 50 = 7500 Bilder.**

⚠⚠ **Der Sammler ist ein BYTE mit `add al, 0x50`.** Vier Fabriken einer Art
ergeben 320 → **64**. In den Prüfdateien gibt es genau einen Fall: **4.DM,
Spieler 1** hat 4 Waffen- und 4 Fahrwerkfabriken und bekommt **64 statt 320**,
und mit 5 Minen **288 statt 800** Terranium.
⭐ **Die vierte Fabrik macht den Spieler ärmer als die erste.**

### AX.3 ⭐ Betriebsart 5: das Maximum statt der Division

`0x4BDCC0`, **365/365 gleich** (die 11 scheinbaren Abweichungen sind
Thunk-Adressen; alle elf lösen paarweise auf dieselbe Funktion auf).

| | `target:` `0x4BECF0` | Betriebsart 5 `0x4BDCC0` |
|---|---|---|
| Auswahl | **kleinstes `po = pway / imp`** | ⭐ **grösstes `imp`** — kein Wegewert, keine Division |
| Sektorraster | ja | **gar keins** |
| Gruppe | bildet eine in sec68 | **keine** |
| Wer marschiert | die Gruppenmitglieder | ⭐ **alle 1000 Einheitenplätze**, `sec60[u] = 10` für alle |
| Protokoll | `po:` `imp:` `pway:` `r_best:` | **keine Marke** |

⭐⭐ **Damit war unsere alte Regel nicht falsch, sondern an der falschen Stelle.**
`AiMissionAttack` nahm das Maximum von `Priority` und schrieb `@0x4BDCC0`
daneben — das ist **genau richtig für Betriebsart 5** und falsch für alle
anderen. Der Umbau vom 21.08. hat den Normalfall ergänzt, nicht einen Irrtum
ersetzt.

| Art | Gültigkeitsprüfung (`0x4BE184`) | Marschbefehl (`0x4BE194`) |
|---:|---|---|
| 1 | `sec3[w].typ != 0` | `c == 0` → `fahre` an die Tür · `c != 0` → `angreife`, Ziel **`60000 + w`** |
| 2 | `sec5[w]+0x09 != 0xFF` | `angreife`, Ziel **`w`**, Streuung `± rnd%10 − 4` |
| **3** | `sec17[w]+0x12 != 0` | `angreife`, Ziel **`40100 + w`** |
| **4** | Belegungskarte `sec6[(w<<8)+c] / 1000 != p` | `fahre` nach `(w ± rnd%3 − 1, c ± rnd%3 − 1)` |

⚠⚠ **BERICHTIGUNG AN `CAMPAIGN_RE.md` §6 UND AN UNSEREM CODE:** dort stehen
**Art 3 und Art 4 vertauscht** (»art 3 = Kartenzelle, art 4 ungelesen«). Die
Sprungtafel sagt: **3 = sec17-Objekt, 4 = rohe Zelle** — genau wie AU.2. Und
die Packung ist **`Spalte·256 + Zeile`**, nicht `Zeile·256 + Spalte`.

⭐ **`40100 + n` ist der Griff auf ein sec17-Objekt**, unabhängig belegt im
Einschlagcode: `0x4537F8` prüft `0x9CA4 ≤ x ≤ 0x9D06` (40100…40198) und rechnet
mit `add bx, 0x635C` in den sec17-Index zurück — exakt die Umkehrung des
`sub 0x635C` der KI. Damit gehört die Zeile in die Griff-Tafel neben `< 8000`
(Einheit), `20000+` (Flugzeug), `50000+` (Wald), `60000+` (Gebäude), `61000+`
(sec4-Objekt).

⚠ **Ein zweiter fehlender `break`:** bei `0x4BDD7D` springt die Art-2-Prüfung
mit `jne 0x4BDDB8` — und `0x4BDDB8` ist **der Rumpf von Art 3**. Eine *lebende*
Einheit als Ziel wird also zusätzlich durch `byte[0xBFEA92 + 24·w] != 0`
geschickt. sec17 hat 100 Sätze zu 24 B; ein Einheitenindex geht bis 7999 — die
Sonde liest weit dahinter. Ist das Fremdbyte 0, wird das Ziel **gelöscht,
obwohl die Einheit lebt**.
⚠ **An den Daten nicht prüfbar:** alle **483** sec69-Einträge der 13 `.DM` haben
Art 1; die einzige Art-2-Stelle des Feldzugs ist Mission 9 (`w = 6000`).

#### ⭐ Nebenbefund: Takt 8 hat DREI Schritte, nicht einen

`0x4BE2E0` ruft `0x4BDCC0`/`0x4BECF0`, dann **`0x4BDB40`**, dann `0x4BCF30`.
`0x4BDB40` (95/95 gleich) war nirgends verzeichnet:

> Für jeden **Flughafen** (Typ 9) des Spielers: zähle die belegten Plätze der
> Wachliste sec27. **Bei mehr als 3** wähle über `0x4BDA10` einen zufälligen
> **feindlichen** Spieler und darin ein Ziel, und starte **bis zu 3** Flugzeuge
> mit Befehlsart **7** auf dessen Zelle (`0x426020`).

**Das ist der Luftangriff der KI** — das Gegenstück zu `guard:` (AU.11), das nur
abfängt.

### AX.4 ⭐⭐ Die Gruppenfahrt und die sec108-Wegpunkte

`0x4BCF30`, **656/656 Befehle**; die einzigen Abweichungen sind der schon in
AU.8 gemeldete gleichbedeutende Vergleich und die Adresse des lokalen Spielers.

**Wie ein Wegpunktsatz entsteht** (am Ende von `0x4BC920`):
```
i := 29 ; (cx,cy) := ZIEL-Sektor
solange (cx,cy) != START-Sektor:
    richtung := Vorgängerkarte[0xB36AA0 + 11·cx + cy]
    sec108[+2 + 2i] := cx ; sec108[+3 + 2i] := cy
    i-- ; wenn i == −1: ABBRUCH (Kopf bleibt ungeschrieben)
    (cx,cy) := Nachbar in richtung          ; Tafel 0x4BCD6C, 4 Einträge
sec108[+0] := i+1 ; sec108[+1] := 0x1D
```
⭐ **Der Satz wird RÜCKWÄRTS gefüllt: Platz 29 ist das Ziel, `+0` der erste
Schritt.** ⚠ Ein Weg über 30 Sektoren bricht ab und lässt `+0`/`+1` unbeschrieben.

**Wie eine Gruppe fährt:**
1. **Aufräumen** — ein Mitglied fliegt aus sec68, wenn `faze != 0`, `UKOL > 45`
   oder `sec60[u] != 10`; die Liste wird lückenlos zusammengeschoben.
2. **Ziel noch da?** (Tafel `0x4BD7BC`) Art 1: Gebäude weg **oder schon uns** →
   **Gruppe auflösen** (`0x4BCEA0`: `sec60[u] = 0`, `sec68[+0] = 0`).
3. `sec108[+1] != 0` → **Wegpunktfahrt**, sonst **Endanflug**.

```
k := sec108[+0] ; (wx,wy) := sec108[+2+2k], sec108[+3+2k]
Kasten:  lo_x … 24·wx + 30      ⚠ siehe unten
         24·wy − 6 … 24·wy + 30
drin := Zahl der Mitglieder im Kasten; die übrigen untätigen fahren zum Anker
wenn (3·drin)/2 > Anzahl:                     ; also drin > 2/3 der Gruppe
    sec108[+0]++ ; wenn == sec108[+1]: sec108[+1] := 0
```

⭐ **`0x541F60` / F `0x540FC0` ist die Sektor-Ankerzelle:** 121 × (Spalte, Zeile),
Vorbelegung `0xFF` (`0x41BFD7`), gefüllt beim Kartenladen (`0x41C050`). Sie ist
der Punkt, auf den ein Sektor-Wegpunkt tatsächlich zeigt.

#### ⚠⚠ Der Kasten ist auf der X-Achse kaputt — in beiden Fassungen

```
C 0x4BD62B  … C6 44 24 15 06   ; lo_x := 6  (KONSTANTE)
F 0x4BD0EB  … C6 44 24 15 06   ; byteweise gleich
C 0x4BD644  … 8D 48 FA …       ; lo_y := 24·wy − 6
```
Die Y-Seite rechnet, die X-Seite schreibt die **Konstante 6**. Für jeden
Wegpunkt mit `wx ≥ 1` lautet die X-Bedingung nur noch `RX > 6` — **der Kasten
hat keine linke Kante**, die Gruppe rückt viel zu früh vor.

⚠ Dazu ein Byteüberlauf: `24·10 + 30 = 270 → 14`. Ein Wegpunkt in Sektorspalte
oder -zeile **10** ist nie erfüllbar, die Gruppe bleibt stehen. Erreichbar ist
das: **5 der 36 Karten sind 254 Felder breit** (254/24 = 10,58).

| Aussage (13 `.DM`) | Treffer | Nullmodell |
|---|---|---|
| belegte sec108-Sätze | 15 von 416 | — |
| `+1 == 0x1D` | **15/15** | — |
| lückenlose 4er-Nachbarkette vom laufenden Index bis 29 | **15/15** | für die 9-Punkt-Kette in 10.DM ≈ **1,4·10⁻¹²** |
| Wegpunkte in Sektor 10 | **0 von 450** | der Überlauf bleibt in diesen Ständen folgenlos |
| sec68-Gruppenzähler ≠ 0 | **0 von 416** | bestätigt AU.13 |

### AX.5 ⭐⭐ `sec56 +0x02` ist die VERBÜNDETE Stärke — die alte Deutung gewinnt

`0x4BA7D0`, **183/183 gleich**; davor `0x4BA710` (**54/54**):

> **sec55** = Summe der **Trefferpunkte** (`+0x08`) aller Einheiten mit
> `faze != 0xFF` **und Waffe `ZBRAN` (+0x0D) != 0**, abgelegt zellendur als
> `sec55[8·(11sx+sy) + Spieler]`.

```
für jeden Sektor, für jeden Spieler q:
    q == p                            → +0x00 += sec55
    sonst byte[0x87B155 + 40p + q]!=0 → +0x02 += sec55
    sonst                             → +0x04 += sec55
und danach DASSELBE über die 8 Nachbarn, jeweils GETEILT durch den Teiler:
    Tafel 0x538BE0:  (−1,0,3) (1,0,3) (0,−1,3) (0,1,3) (1,1,5) (1,−1,5) (−1,1,5) (−1,−1,5)
```

⭐ **`+0x02` ist entschieden.** Drei unabhängige Belege im selben Bild: AU.6
(`get target in sector` **überspringt** genau solche Spieler), `0x4BDA10` (die
Luft-Feindwahl sammelt genau die mit `== 0`), und die Daten
(`sec53[40p+0x15+p] == 1` in **104/104** — man selbst ist immer Freund).

⭐ **Und es ist mehr als eine Summe: sec56 `+0/+2/+4` ist eine gewichtete
Nachbarschaftsglättung** — eigener Sektor voll, orthogonale Nachbarn ⅓,
diagonale ⅕.

| Feld (7623 Sektor-Spieler-Paare) | Formel trifft | trifft nicht |
|---|---:|---:|
| `+0x00` eigene Stärke | **7442** | 181 |
| `+0x02` **verbündete** | **7400** | 223 |
| `+0x04` feindliche | **6892** | 731 |

**Nullmodell (die beiden vertauscht), nur auf den 2684 Zellen, wo sich die
Deutungen unterscheiden:**

| | richtig herum | vertauscht |
|---|---:|---:|
| `+0x02` | **2461 (91,7 %)** | 185 (6,9 %) |
| `+0x04` | **1953 (72,8 %)** | 693 (25,8 %) |

⭐ **Und der Rest erklärt sich:** **alle 1135 Abweichungen sind einseitig** — der
gespeicherte Wert ist **immer kleiner** als die Vorhersage, nie grösser.
Nullmodell bei Rauschen: 50/50, erwartet 568. Das ist genau das Bild eines
Zeitversatzes: sec55 steht auf dem letzten Bild, sec56 auf dem letzten Takt 1
(50 Bilder früher), und dazwischen sind Einheiten gestorben.

### AX.6 ⭐⭐ Typ 11 ist das SEEDOCK, Besitzer 11 ist NIEMAND

**Der Zufall ist wirklich einer** — die beiden Elfen stehen in getrennten
Zahlenräumen.

⭐ **Das Spiel nennt die Gebäudetypen selbst** (Tafel `0x4FDCB0`, 20 B je
Eintrag, in beiden EXE wortgleich):

| 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|
| Basis | Waffen-Fabrik | Fahrwerk-Fabrik | Spezial-Fabrik | Depot | Bahnstation | Generator | Radarstellung |

| 9 | 10 | **11** | 12 | 13 | 14 | 15 | 16 |
|---|---|---|---|---|---|---|---|
| Flughafen | Mine | **Seedock** | Feldbahnhof | Kraftwerk | Nachschub-Posten | Feld-Rohstoffmine | Werft-Station |

⭐ **`AI: test of life` prüft damit die drei Zugänge zur Karte: Basis (Land),
Flughafen (Luft), Seedock (See).** Kein Kuriosum, sondern eine vollständige
Aufzählung. Passend dazu: sec29 (50 × 4) ist die Paarung **Seedock ↔
Werft-Station**, und die Verträglichkeitstafel `0x4FDC00` hat für Zieltyp 11
eine **leere** Zeile — zu einem Seedock liefert kein Roboter.

**Besitzer 11:** `byte[sec3 + 76·i + 5]` hat sieben Schreibstellen; genau **zwei**
schreiben eine 11, und beide schreiben zugleich `+0x41` — `0x41B194` (Spieler
scheidet aus / verlässt das Netzspiel) und `0x4BAC59` (der Tod nach AU.7).

| Besitzer | 0 | 1 | 2 | 3 | 4 | 5 | 6 | **11** | 255 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 13 `.DM` | 43 | 101 | 89 | 8 | 75 | 1 | — | **48** | 175 |
| 23 `.CWM` | 20 | 60 | 29 | 3 | 5 | 1 | 1 | **428** | 278 |

⭐ **In den Feldzugskarten ist 11 der HÄUFIGSTE Besitzer: 428 von 826.** Das sind
Anfangszustände — dort ist noch niemand gestorben. **Besitzer 11 heisst also
»herrenlos«**, und der Sterbefall benutzt dieselbe Zahl, um ein Erbe an
niemanden zu geben. Passend: 8…10 kommt **nie** vor, und `obj_owner` gibt für
einen **leeren Platz** die **12** zurück — die nie in einer Datei steht.
Besitzer **255** trägt ausschliesslich Deko (**0** Fälle bei Typ 1…12).

⚠ Und weil sec53 nur 8 × 40 B ist, kann Besitzer 11 **weder Freund noch Feind**
von irgendwem sein.

**Typ 11 gezählt:** 8 Seedocks in den 13 `.DM`, 13 in den 23 `.CWM` — davon
**9 der 13 in den NET-Karten, und dort 9 von 9 mit Besitzer 11**, also zum
Einnehmen ausgelegt.

### AX.7 ⭐ `Robot not found` 1 / 2 / 3 — und ein Fehler, der in den Daten steht

Zur Frage aus dem Auftrag: `meldung()` ist stumm, **kehrt aber zurück** und
bricht nichts ab. ⭐ Das ändert hier nichts — an allen drei Stellen folgt
unmittelbar der Funktionsabschluss. **Die Funktion tut in diesem Fall gar
nichts, nicht wegen der Meldung, sondern weil der Rückgabepfad derselbe ist.**

Alle drei sind **dieselbe Funktion in drei Ausführungen**: »nimm Roboter `r` aus
der Andockliste des Gebäudes `c`«.

| Marke | C | Liste | Satz | Gegenstück »andocken« |
|---|---|---|---|---|
| `Robot not found` | `0x43C120` | **sec23** (Basis) `+0x04` | 16 B | `0x43BFC0` |
| `Robot not found 2` | `0x43C430` | **sec30** (Bahnstation) `+0x02` | 14 B | `0x43C370` |
| `Robot not found 3` | `0x43C6F0` | **sec25** (Depot) `+0x02` | 14 B | `0x43C630` |

⚠⚠ **Und hier steht die eigentliche Antwort auf »wann schlägt welche zu«:**

```
andocken   0x43BFC0: cmp esi, 6 · 0x43C370: cmp esi, 6 · 0x43C630: cmp esi, 6
ablegen    0x43C120: cmp edi, 5 · 0x43C430: cmp esi, 5 · 0x43C6F0: cmp ecx, 5
```

⭐ **Der sechste angedockte Roboter kann nie wieder abgelegt werden** — jeder
Versuch endet in `Robot not found` und **ohne Wirkung**. Der sec23-Satz ist 16 B
(`+0x04…+0x0F` = **sechs** Wörter), also sind sechs Plätze richtig und die Fünf
ist der Fehler. Unabhängige Gegenprobe: **Takt 4** (`0x4BBAC0`, AU.1) liest
dieselbe Liste mit `cmp bp, 6`.

**Und der Fall steht in den ausgelieferten Daten.** Von 36 Dateien sind nur drei
Andocklisten überhaupt belegt:

| Datei | Satz | Liste |
|---|---|---|
| 12.DM | 0 | 1086, 1087, FFFF … |
| 13.DM | 1 | 1061 … 1064, FFFF, FFFF |
| **2.DM** | **1** | **1091, 1005, 1007, 1119, 1138, 1006** ← **voll** |

⭐ In 2.DM gehört Satz 1 zu Gebäudeplatz 4 (**Typ 1 Basis**, Besitzer 1,
`cis` 1) — damit ist die Zuordnung `sec23-Index = cis` direkt belegt. Alle sechs
Einheiten stehen auf **derselben Zelle (77,31)** mit `UKOL = 51` (im Gebäude):
**6/6**; Nullmodell einer Zufallseinheit auf genau dieser Zelle: 6/8000. Und die
sechste (Einheit **1006**) sitzt auf Platz 5 — dem Platz, den `Robot not found`
nie ansieht.

### AX.8 ⚠ Was offen blieb, und wodurch das Verfahren blind ist

1. ⚠⚠ **sec123 gibt es nur in EINER Prüfdatei.** Der Lader kommt bei **12 von 13
   `.DM`** nicht bis sec123 (3.–10.DM enden nach sec122, 2./11./12./13.DM nach
   sec120) — und zwar mit `tail == 0`, die Datei ist **exakt zu Ende**. Nur
   **1.DM** trägt alle 131 Abschnitte. **Alles über sec123 und über den
   gespielten Zustand von sec24/sec28 hängt an einer Datei.** Ob die
   Grössenliste für den Schwanz stimmt oder ob diese `.DM` schlicht kürzer sind,
   ist **nicht** entschieden.
2. ⚠ Die Deckel-Formel ist an 10 Fabriken und 3 Minen gemessen — **alle aus
   1.DM**. Die zwölf übrigen sind unbespielte Anfangszustände, in denen Takt 46
   nie lief.
3. ⚠ **Die Art-2-Sonde von `0x4BDCC0` ist unmessbar** (483/483 sec69-Einträge
   haben Art 1).
4. ⚠ **Der X-Kasten-Fehler ist folgenlos in allen 450 gemessenen Wegpunkten.**
   Gemeldet, weil er byteweise in beiden EXE steht und die Y-Seite unmittelbar
   daneben es anders macht — **aber kein Datensatz zeigt ihn.**
5. ⚠ »Kein Schreiber« bleibt unter dem Vorbehalt aus AU.14; `reloc_refs --block`
   lief für diese Adressen **nicht**.
6. ⚠ Die Füllung der Sektor-Ankertafel `0x541F60` ist ungelesen. Der Setzer
   benutzt einen dreistufigen Index, der Leser nur `2·(11·wx+wy)`.
7. ⚠ `0x426020` (Luftbefehl, Art 7) und `0x4BDA10` ab `0x4BDA8E` nur an der
   Kante gelesen.
8. ⚠ Der C/F-Vergleicher normiert Adressen über eine Liste erlaubter Abstände
   und **kann eine echte Abweichung verschlucken**, wenn sie zufällig einen
   dieser Abstände hat. Für die fünf Kernfunktionen wurden Sprungtafeln und
   Thunkziele **einzeln aufgezählt**; für die Roboterroutinen **nicht**.
9. ⚠ **Vier fehlende `break` auf einmal zu melden ist selbst verdächtig.** Jeder
   einzelne ist nur deshalb belastbar, **weil er in C und F byteweise gleich ist
   und weil die Nachbarzeile im selben Rumpf es anders macht.** Wo dieser zweite
   Halt fehlte, wurde nichts gemeldet — ein fünfter Durchfall (in `0x4407C0`)
   ist wirkungslos, weil beide Zweige dieselben Zahlen schreiben.

---

## AY. ⭐ DIE MESSLATTE, EHRLICHER: 47 % DER FUNKTIONEN, ABER 80 % DER BYTES

`funktionen.py` zählt seit dem 21.08.2026 **zweierlei**, und die zwei Zahlen
sagen Verschiedenes:

```
  Funktionen (ohne Thunks)        1107
  davon mit eigener Marke          257   (23,2 %)
  davon bei uns erwaehnt           521   (47,1 %)
  ⭐ nach BYTES statt Stueck    689 264 / 861 488   (80,0 %)
     ⭐ HOEHER als die Stueckquote: die Ungelesenen sind die KLEINEREN
        (293 gegen 1322 Byte im Schnitt).
```

⭐ **Der grosse, tragende Code ist gelesen.** Was bleibt, sind **586 Funktionen
mit 172 224 Byte** — im Schnitt 293 Byte, gegen 1322 Byte bei den gelesenen.
⚠ **Klein heisst nicht unwichtig:** die Setzer des Missionsskripts (`sec61`,
`sec77`, `sec106`) sind rund 20 Byte gross und tragen trotzdem ganze
Verhaltensweisen.

⚠ **Was auch die Bytequote nicht sagt:** eine *erwähnte* Adresse ist nicht
dasselbe wie eine *verstandene* Funktion. Beide Zahlen sind **Obergrenzen**.
Umgekehrt sind sie zu klein, wo etwas verstanden, die Adresse aber nie
hingeschrieben wurde — genau dafür gibt es die Adressverzeichnisse AQ und AV.19.

### ⚠ Und eine Lektion, die sich zum zweiten Mal gemeldet hat

Nach dem Anhängen von Abschnitt AV stand die Quote bei 496 — obwohl AV die
Hauptschleife, `Flip pages`, `Get run begin`, `JOJO` und `Achtung` ausführlich
beschreibt. Der Grund war derselbe wie bei AQ: **beim Verdichten des Laufberichts
sind die Adresstafeln weggefallen.** Das Nachtragen als AV.19 hat die Quote in
einem Schritt von **496 auf 521** gehoben und die Zahl der »benannt, aber nie
erwähnt« von **27 auf 7** gesenkt.

> ⭐ **Regel: Ein Laufbericht darf verdichtet werden, seine Adresstafel nicht.**

---

## AZ. ⚠⚠ GEMELDET AM 21.08.2026: IN DER KAMPAGNE GEHT EINIGES NICHT MEHR

Wörtlich: »*seit wir mehr und mehr hier analysieren geht nämlich einiges nicht
mehr in der kampagne. aber das ist nicht so schlimm. daher erst alles auslesen,
einarbeiten und dann werde ich sauber testen*«.

⭐ **Die Reihenfolge steht damit fest und ist nicht zu verhandeln:** erst das
Lesen zu Ende, dann alles einarbeiten, **dann** prüft er. Ein Fehler, den man
jetzt jagt, wird von der nächsten Erkenntnis ohnehin wieder verschoben.

⚠ **Was ausdrücklich NICHT gilt:** dass hier nichts zu tun sei. Der Befund ist
festgehalten, damit er beim Prüflauf nicht als neu behandelt wird — und damit
niemand später denkt, die Kampagne sei die ganze Zeit heil gewesen.

### AZ.1 Zwei Fehler desselben Tages, selbst gefunden und behoben

Beim Nachsehen, ob der Umbau des Gegners vom 21.08. daran beteiligt ist, fielen
**zwei eigene Fehler** auf. Beide stammen aus genau diesem Umbau:

**1. ⚠⚠ Die Gruppen gaben ihre Einheiten nie wieder frei.**
`AiGruppeBilden` setzt jedem Mitglied `CPU0 = 10`; gezählt werden in
`Set imp cpu:` aber **nur** die Einheiten mit `CPU0` 1 oder 2. Wer nie
zurückgesetzt wird, fällt **dauerhaft** aus der Rechnung — die Zahl der freien
Angreifer sank mit jeder Welle, und irgendwann fuhr **gar nichts mehr los**.

Das Original hat dafür `0x4BCEA0` / F `0x4BC960`, und es löst an **drei**
Stellen auf: wenn das Ziel weg ist, wenn es **uns gehört**, und wenn die Gruppe
leer läuft. Nachgebaut als `AiGruppeAufloesen`.

⚠ Genau so sieht »die Kampagne tut nichts mehr« von aussen aus — und **keine der
sechs Messlatten des `--sektor-check` hätte es bemerkt**, weil alle sechs
Augenblicksaufnahmen sind und keine den Verlauf misst.

**2. ⚠ Die Gruppe merkte sich einen Listenplatz statt des Auftrags.**
Im Original ist sec69 eine **feste Tafel mit 100 Plätzen**; ein erledigter
Auftrag wird **an Ort und Stelle geleert**, die übrigen rücken nicht nach — ein
gespeicherter Index bleibt gültig. Unsere Zielliste ist eine `List<>`, aus der
erledigte Einträge **herausgenommen** werden; dabei rutscht alles dahinter eine
Stelle vor, und der gemerkte Platz zeigte auf einen **fremden** Auftrag.

⭐ **Die Lehre ist allgemein:** eine feste Tafel mit Leerplätzen und eine
mitwachsende Liste sind **nicht** dasselbe. Wo das Original einen Index
speichert, darf unser Nachbau das nur, wenn er die Plätze auch stehenlässt.

### AZ.2 ⭐ Die siebte Messlatte, die daraus entstanden ist

`--sektor-check` misst jetzt zusätzlich **den Verlauf statt des Augenblicks**:

```
  7. Gruppe bilden und aufloesen (P0): 3 Einheiten, 3 auf CPU0=10, danach 0  ok
     freie Angreifer 48 -> 48 (muss gleich sein)
     Nullmodell: OHNE Aufloesung stuenden hier 3 auf CPU0=10
     und die freien Angreifer waeren um 3 gefallen.
```

⭐ **Die Regel dahinter, und sie gilt über diesen Fall hinaus:** ein Prüfstand,
der nur einen Zustand misst, findet keinen, der **versickert**. Wer einen
Zähler hochsetzt, muss auch prüfen, dass ihn jemand wieder herunterholt — und
zwar mit derselben Zahl vorher und nachher.

### AZ.3 Was beim Prüflauf des Spielers gezielt anzusehen ist

Ohne seine Beobachtungen ist das eine Liste von Verdachtsmomenten, keine
Fehlerliste. In der Reihenfolge, in der die Änderungen die Kampagne berühren:

| Was | seit | warum verdächtig |
|---|---|---|
| Missionsziele der Art **3** | 21.08. | galten bis dahin als Kartenzelle, sind aber **sec17-Objekte**; sie lösen jetzt bewusst zu **nichts** auf, statt eine falsche Zelle anzugreifen |
| Zellziele (jetzt Art **4**) | 21.08. | Spalte und Zeile waren **vertauscht** |
| Der Gegner in der Kampagne | 21.08. | ganz neue Auftragswahl (`po = Weg / Wichtigkeit`) und Gruppen mit harter Grenze 4 × 100 |
| `sec62` fehlt im Ausleser | offen | dadurch `sec110 == 0`, und das kippt den Gegner in den **»Take all«**-Zweig (AV.18) |
| Kontexthilfe | 20.08. | 34 Tore, Schranke `Missionsnummer < 50` |
| Die Auflösungswahl | 21.08. | `1280×960` war unsere Erfindung, das Original hat `1280×1024` |

### AZ.4 ⚠⚠ Die Liste ist am 22.08.2026 viel länger geworden

An diesem Tag sind **19 Bauaufgaben** eingegangen (15 Commits, alles lokal).
Sechs davon ändern etwas, das der Spieler **unmittelbar sieht** — sie gehören
darum in dieselbe Verdachtsliste. Wieder gilt: **keine Fehlerliste.** Jede
dieser Änderungen ist am Original gelesen; verdächtig sind sie nur, weil sie
das gewohnte Bild verschieben.

| Was | warum es auffällt | Beleg |
|---|---|---|
| **Die Wegsuche ist eine andere** | reine 8-Nachbar-Breitensuche mit Wellenmarken statt A*; **Chebyshev**, also kostet eine Diagonale **nicht mehr** als eine Gerade. Einheiten fahren sichtbar andere Wege, Ecken werden nicht mehr geschnitten | BB, `--wegsuche-check`, Rückfall `--alter-astern` |
| **Der Gesundheitsbalken** | drei Farbbänder statt zwei (Schwellen ½ und ¼) und die Länge wächst mit `HpMax` | Bauaufgabe 1–5 |
| **Reparieren kostet jetzt Trefferpunkte** | **1/30** je Schritt — vorher war Reparieren umsonst | Bauaufgabe 6er-Block |
| **Der Ausbau ohne Geld sagt gar nichts** | kein Ton, keine Meldung — originalgetreu (BL.4.1, dreimal wortgleich im Code). ⚠ **Das liest sich wie ein Fehler und ist eine offene Frage an ihn** | BL.4.1 |
| **Fenster blenden auf und zu** | 4 Bilder auf, 6 zu; dasselbe Fenster lässt sich **nicht mehr doppelt** öffnen; feste Lagen; Platz 0 ist oben | BA, `--fenster-check` (18 Messungen) |
| **Der Wind dreht sich zwanzigmal langsamer** | 2000 **Takte** statt 2 Sekunden, und am Takt statt an der Bildrate — damit läuft auch der **Waldbrand** anders | Bauaufgabe 12, `--brand-check` |

Dazu vier Änderungen, die weniger sichtbar sind, aber dieselbe Wirkung haben
können, wenn etwas daneben liegt:

* **Der Angriff ist Busbefehl 11** statt unserer Nummer 2001 — er geht damit
  erstmals in die **Wiederholung** und über die **Leitung** (Netzbetrieb).
* **Die Reichweite misst nicht rund** (x·40, y·20) — das Schussfeld ist eine
  **Ellipse**; Schiffe (Gattung 4) haben feste 16 Zellen. Wer bisher aus einer
  bestimmten Entfernung traf, trifft je nach **Richtung** nicht mehr.
* **Forschung ist Spielersache** — ein Satz je Spieler statt einem für alle
  acht. In der Kampagne heisst das: der Gegner erbt nichts mehr von uns.
* **Die verlegte Einheit stirbt mit dem Zug**, und der Zug nimmt seine Ladung
  mit.

⭐ **Beim Prüfen hilft die Reihenfolge dieser Tabelle**: was oben steht, sieht
man in der ersten Minute einer Mission; was unten steht, erst im Gefecht.

---

## BA. ⭐⭐ DIE FENSTER- UND ZEICHENMASCHINE (21.08.2026)

Gelesen in **beiden** Auslieferungen. Verglichen wurde nie eine geratene
Adresse, sondern die **normierte Befehlsfolge** (Mnemonik + Operandenform, alle
Absolutadressen ausgeblendet) und die **Ruferzahl**.

⭐ Nebenbefund, der Regel 1 selbst stützt: alle hier genannten `.bss`-Globalen
haben Abstand **C − F = genau `0xFA0`**, alle `.data`-Zeichenketten `0xFC0` bzw.
`0x1020` — gemessen an 27 Adressen aus fünf Funktionen, **kein Ausreisser**.

### BA.1 ⭐⭐ `0x455E50` ist der Fensterrahmenzeichner — und seine 44 Rufer sind die 44 Fensterarten

| | C | F |
|---|---|---|
| Rahmenzeichner | `0x455E50` (1168 B) | `0x454AF0` (1168 B) |
| Rufer | **44** | **43** |
| Gleichheit der normierten Folge | **1.000 — kein einziger Unterschied** | |

Die 44 Rufer sind Adresse für Adresse die Fensterart-Funktionen, **jede genau
einmal**: `0x463D60 0x463FB0 0x464A20 0x464BE0 0x465050 0x467C60 0x46C490
0x46EDC0 0x4716C0 0x471800 0x472D40 0x4732A0 0x4733B0 0x473750 0x473BF0
0x473EC0 0x474220 0x474FE0 0x476410 0x476880 0x476D00 0x4790A0 0x47A740
0x47AAE0 0x47AE80 0x47BB10 0x47C800 0x47CA30 0x47CD60 0x47D340 0x47D6D0
0x47DF70 0x47F150 0x480390 0x480650 0x480870 0x482290 0x484C00 0x485F10
0x486020 0x4861A0 0x487060 0x487180 0x4872A0`.

⭐ Die **vier**, die ihn *nicht* rufen: `0x46FE10` (Art 9), `0x47CF10` (Art 30,
Hilfe), `0x486480` (Art 43, Vollbild), `0x486F20` (Art 44). Für Art 30 und 43
gibt es **eigene** Zeichner (BA.3) — das ist die Erklärung am Kontrollfluss.

```
rahmen_zeichnen(x, y, wZellen, hZellen, puffer, zeilenschritt, titelleiste, sondereck)
   arg3/arg4 sind ZELLEN, nicht Punkte.
   Vorbedingung: arg3 > 2 UND arg4 > 2, sonst tut die Funktion NICHTS.
```

#### ⭐ Der Zufall ist keiner: `srand` mit der Fensterbreite

| C | Handlung |
|---|---|
| `0x455E61` | `srand(wZellen)` |
| `0x455F21` | `srand(wZellen + 5)` |
| `0x456192` | `srand(wZellen + 10)` |
| `0x4561D6`/`DF` | `srand(time(NULL))` — **Rückgabe des Generators an das Spiel** |

(CRT: `0x4D6C50` = `srand`, `0x4D6C70` = `rand`, `0x4D7690` = `time`.)

⭐ **Die Rahmensprenkelung ist damit eine reine Funktion der Fensterbreite** —
sie flimmert nicht, wenn ein Fenster mehrfach neu gemalt wird. Für den Nachbau:
**dieselben drei Startwerte, dieselbe Reihenfolge der `rand()`-Aufrufe**, sonst
ist das Bild ein anderes.

#### Die Kacheln, die er setzt (Elementnummern aus `WINDOWS.CWW`)

| C | Element | Ort |
|---|---|---|
| `0x455EDA` | `0 + rand%3` | linke Senkrechte |
| `0x455EFC` | `6 + rand%3` | rechte Senkrechte |
| `0x455F84` | `46 + rand%3` | obere Kante **mit** Titelleiste |
| `0x455FB1` | `43 + rand%3` | obere linke Ecke mit Titelleiste |
| `0x455FE7` | `49 + rand%3` | obere rechte Ecke der Titelleiste |
| `0x456003` | `12` fest | obere linke Ecke **ohne** Titelleiste |
| `0x45603F` | `3 + rand%3` | obere Kante ohne Titelleiste |
| `0x456097` | `9 + rand%3` | untere Kante |
| `0x456108` | `14`, bzw. **`297`** wenn arg8≠0 | untere rechte Ecke |
| `0x456165` | **`16 + rand%9`** | **Innenfläche**, Doppelschleife |
| `0x4561AF` | `13` fest | obere rechte Ecke |
| `0x4561CC` | `15` fest | untere linke Ecke |

⭐ **Nullmodell für die Eckenzuordnung, unabhängig vom Code gewonnen.** Aus
`WINDOWS.CWW` selbst: die Zeilenköpfe `[leftoff][count]` sagen die Form. Genau
die Elemente, die der Code an die **linke** Fensterkante setzt, sind um 3 Punkte
eingerückt — alle anderen nicht:

| Element | Zeilenprofil | Code setzt es an |
|---|---|---|
| 0, 1, 2 · 12 · 43 | `leftoff 3, count 17` | linke Kante bzw. linke obere Ecke |
| 15 | `3 : 17`, **letzte zwei Zeilen `9 : 11`** | linke untere Ecke — die Abrundung ist da |
| 13 | `0 : 17` in den **ersten fünf** Zeilen | rechte obere Ecke — die Kerbe ist da |
| 3, 6, 9, 14, 46, 49 | `0 : 20` | überall sonst |

**6 von 6 linken Kacheln eingerückt, 0 von 12 übrigen.**

### BA.2 ⭐ Die Grundwerkzeuge — die praktisch wertvollsten Adressen des Reviers

| Aufgabe | C | F | Byte | Rufer |
|---|---|---|---:|---|
| Kachel zeichnen | `0x455DB0` | `0x454A50` | 160 | 58 |
| **Rechteck füllen** | `0x455C50` | `0x4548F0` | 160 | **88** |
| ⭐ **Maus im Rechteck?** | `0x455CF0` | `0x454990` | 96 | **363** |
| **Knopf zeichnen** | `0x456670` | `0x455310` | 528 | **123 / F 117** |
| Knopf, Variante | `0x456880` | `0x455520` | 464 | 3 |
| Rollbalken zeichnen | `0x456FF0` | `0x455C90` | 336 | 5 |
| Rollbalken auswerten | `0x457140` | `0x455DE0` | 448 | 18 |

⚠ Wo die Ähnlichkeit unter 1.000 liegt, ist es **reine Registerwahl** des
Übersetzers (`esi`↔`ebx`, `lea` statt `add`, `jg` statt `jl` mit vertauschten
Operanden), Zeile für Zeile nachgesehen. **Solche Zahlen dürfen nicht als
»Unterschied« gemeldet werden.**

**`rechteck_fuellen(x, y, breite, hoehe, farbe, puffer, zeilenschritt)`** — x/y/b/h
und Schritt als **Worte**, Farbe als **Byte**, auf ein Dword gespreizt,
`rep stosd` + `rep stosb`. **Keine Beschneidung.**

**`maus_in_rechteck(x, y, breite, hoehe)`** — `MausX = dword[0x8B62A4]`
(F `0x8B5304`), `MausY = dword[0x8B62A0]` (F `0x8B5300`).
⚠ **363 Rufstellen — der meistgerufene Code des Reviers.** Er wird über den
Stummel `0x401CF8` gerufen; **wer die Stummel nicht auflöst, sieht drei.**
⭐ Unabhängig bestätigt: **`word[0x502AC8]` (F `0x501B08`) ist der
Maustastenzustand**.

**`knopf_zeichnen`** — Startwert `srand(x · y)`: **ein Knopf sieht an derselben
Stelle immer gleich aus.** Elemente `25 + art + 2·rand%3` (linke Kappe),
`31 + …` (Mitte je Zelle), `37 + …` (rechte Kappe); `art` = 0 normal, 1 gedrückt.
Beschriftung mittig über `0x4BA160` (Textbreite) und `0x4BA5E0` (Ausgabe),
Versatz `(+0,+3)` normal / `(+1,+4)` gedrückt.
⚠⚠ **Ein hässlicher, aber wichtiger Kniff:** die Funktion legt den
**Zeilenschritt des Zielpuffers vorübergehend in `dword[0x5387C8]`** (die
»Bildbreite«) ab, weil der Textzeichner seinen Schritt von dort holt, und stellt
sie danach wieder her (`0x456673`, `0x45676F`, `0x4567F1`).

**Der Rollbalken:** drei Treffflächen von **20 × 10** Punkten — Pfeil hinauf bei
`(x, y+20)` → 0; Pfeil hinunter bei `(x, y+n·20−30)` → n−1; Schieber ab
`(x, y+30)`. Gemalt mit Element **60** (oben), **63** (unten), **61/62** (Mitte,
`rand%2`).

### BA.3 ⭐ Es gibt nicht einen Rahmen, sondern DREI

| Zeichner C | F | Byte | Rufer | Elementsatz | Wofür |
|---|---|---:|---|---|---|
| `0x455E50` | `0x454AF0` | 1168 | 44 | **0…24, 43…51, 297** | Normalfenster |
| `0x4562E0` | `0x454F80` | 912 | 1 (`0x47CF10`) | **229…247** | **Art 30, Hilfefenster** |
| `0x456CC0` | `0x455960` | 816 | 1 (`0x47CF10`) | **298…309** + Füllung 16…24 | der olivfarbene Satz |

Die Farbwerte bestätigen es unabhängig: Satz 1 benutzt Palettenindizes
`0x24…0x2F`, Satz 229 zusätzlich `0x7F…0x83`, Satz 298 zusätzlich `0xA5…0xAF` —
**drei getrennte Farbbänder**.
⭐ **Nullmodell:** höchste je benutzte Elementnummer = **309**, die Datei hat
**314** (`138160 / 440 = 314`, Rest 0). Es geht auf und fällt nicht durch.

### BA.4 ⭐⭐ 45 Fensterarten mit ihren Punktmassen

Der Bereich `0x4573C0 … 0x45CA30` ist **kein Zeichencode**, sondern **eine
Anlegefunktion je Fensterart**. Jede tut dasselbe:

```
1. freien Platz suchen:  byte[SOCKEL + 44324·k] == 0,  k = 0 … 19
   voll  ->  Rueckgabe 0xFFFF
2. die 44324 Byte des Satzes nullen (rep stosd, ecx = 0x2B49 = 11081 Dwords)
3. byte[+0x00] = Art ; word[+0x02] = x ; word[+0x04] = y
   word[+0x06] = Breite in PUNKTEN ; word[+0x08] = Hoehe in PUNKTEN
4. malloc(Breite·Hoehe)  ->  dword[+0xAC9C]
5. den Puffer mit 0xFF fuellen  =  vollstaendig DURCHSICHTIG
```

**Fenstersatz-Sockel: C `0x8B9038` · F `0x8B8098`.**

⭐⭐ **Nullmodell für »20 Plätze à 44 324 Byte«, zweifach unabhängig:**
`0x8B9038 + 20·44324 = 0x991708`, nächste belegte Globale `0x991820` — **Lücke
280 Byte**. `0x8B8098 + 20·44324 = 0x990768`, nächste `0x990880` — **Lücke 280
Byte**. Dieselbe Lücke, zweimal. **Ein 21. Platz passte nicht hinein.**

| Art | C | F | px | Art | C | F | px |
|---:|---|---|---|---:|---|---|---|
| 1 | `0x4573C0` | `0x456060` | 80×80 | 25 | `0x45A070` | `0x458D10` | 280×220 |
| 2 | `0x4575F0` | `0x456290` | 300×280 | 26 | `0x459B70` | `0x458810` | 560×300 |
| 4 | `0x4578C0` | `0x456560` | 300×100 | 27 | `0x459CB0` | `0x458950` | 620×300 |
| 5 | `0x457A00` | `0x4566A0` | 360×340 | 28 | `0x45A1B0` | `0x458E50` | 220×60 |
| 6 | `0x457BB0` | `0x456850` | 360×340 | 29 | `0x45A2E0` | `0x458F80` | 220×220 |
| 7 | `0x457D50` | `0x4569F0` | 600×340 | **30** | `0x45A5F0` | `0x459290` | **360 × wächst** |
| 8 | `0x457EC0` | `0x456B60` | 260×240 | 31 | `0x45ABE0` | `0x459880` | 260×100 |
| **9** | `0x458000` | `0x456CA0` | **204×170** | **32** | `0x45AD10` | `0x4599B0` | 500×340 |
| 10 | `0x458150` | `0x456DF0` | 300×100 | 33 | `0x45AFD0` | `0x459C70` | 360×260 |
| 11 | `0x4582C0` | `0x456F60` | 360×300 | 34 | `0x45A420` | `0x4590C0` | 360×300 |
| 12 | `0x458410` | `0x4570B0` | 180×100 | 35 | `0x45B100` | `0x459DA0` | 200×240 |
| 13 | `0x4586D0` | `0x457370` | 60×80 | 36 | `0x45B230` | `0x459ED0` | 300×60 |
| 14 | `0x4589B0` | `0x457650` | 300×400 | 37 | `0x45B3A0` | `0x45A040` | 600×420 |
| 15 | `0x458CB0` | `0x457950` | 300×360 | 38 | `0x45B4E0` | `0x45A180` | 600×420 |
| 16 | `0x458FD0` | `0x457C70` | 280×120 | 39 | `0x45B620` | `0x45A2C0` | 560×300 |
| 17 | `0x4593F0` | `0x458090` | 200×300 | 40 | `0x45B770` | `0x45A410` | 180×60 |
| 18 | `0x459530` | `0x4581D0` | 260×240 | 41 | `0x45B8A0` | `0x45A540` | 280×60 |
| 19 | `0x459670` | `0x458310` | 140×200 | 42 | `0x45B9D0` | `0x45A670` | 300×160 |
| 20 | `0x4597B0` | `0x458450` | 220×100 | **43** | `0x45BC10` | `0x45A8B0` | **640×480** |
| 21 | `0x4598F0` | `0x458590` | 220×100 | 44 | `0x45C540` | `0x45B1E0` | 420×20 |
| 22 | `0x459A30` | `0x4586D0` | 640×320 | 45 | `0x45C670` | `0x45B310` | 220×60 |
| 23 | `0x459DF0` | `0x458A90` | 360×260 | 46 | `0x45C8F0` | `0x45B590` | 120×100 |
| 24 | `0x459F30` | `0x458BD0` | 280×140 | 47 | `0x45C7B0` | `0x45B450` | 220×60 |
| | | | | **48** | `0x45CA30` | **fehlt** | 200×180 |

⚠ **Art 3 (Kartenfenster) hat keinen solchen Anleger** — seine Grösse ist nicht
fest.

⭐⭐ **Das Nullmodell, das die ganze Zeichenmaschine erklärt:** von **44 festen
Massen sind 43 restlos durch 20 teilbar.** Die einzige Ausnahme ist
**Art 9 = 204 × 170 = 34 680 = genau `PANEL.DTA`.** Und das 20er-Raster ist kein
Zufall, sondern **erzwungen** — siehe BA.5.
Art 43 = 640 × 480 = 307 200 — dasselbe `0x12C00`-Dword-Mass, mit dem `0x4409E0`
dieses eine Fenster mit **einem einzigen `rep movsd`** auf den Schirm wirft.

### BA.5 ⭐⭐ Wie durchsichtig kopiert wird — und warum es KEINE Beschneidung gibt

**Die Kachel:** `0x455DB0` / F `0x454A50` —
`kachel_zeichnen(puffer, x, y, elementnr, zeilenschritt)`. Satz = 440 B ab
C `0x8938D8` / F `0x892938`, **20 Zeilen à 22 Byte**, Zeile =
`[u8 leftoff][u8 count][20 Byte Punkte]`, Quelle = Satz + 2 + leftoff.
**20 Zeilen fest** (`0x455DC3: mov [esp+0xc], 0x14`).
⭐ **Die Durchsichtigkeit läuft hier NICHT über `0xFF`, sondern über
`leftoff`/`count`** — je Zeile wird nur ein Lauf kopiert.

**Der durchsichtige Kopierer:** `0x4409B0` / F `0x43F9C0`, 48 Byte, Gleichheit
1.000:
```
zeile_kopieren_durchsichtig(quelle, ziel, laenge):
    fuer laenge Byte:  al = [quelle];  wenn al != 0xFF:  [ziel] = al
```
Gerufen ausschliesslich von `0x4409E0` / F `0x43F9F0` (*fenster_auf_schirm*),
einmal je Bildzeile:
`Ziel = dword[0x87B044] + y·dword[0x5387C8] + x`, `Quelle = Fenstersatz + 0xAC9C`.

#### ⭐⭐ Und die Antwort auf »wie wird geschnitten?« — **gar nicht, und das ist Absicht**

Weder `0x455DB0` noch `0x455C50` noch `0x4409B0` prüft eine Grenze. Sie
**brauchen keine**:

> Jedes Fenster hat seinen **eigenen 8-Bit-Puffer** von genau `Breite × Höhe`.
> Alles Zeichnen geht mit **Zeilenschritt = Fensterbreite** in diesen Puffer.
> Weil alle Fenstermasse (bis auf Art 9) **Vielfache von 20** sind und die
> Kachel **20 × 20** misst, **kann eine Kachel den Puffer nicht überlaufen.**
> Erst beim Aufsetzen auf den Bildschirm wird `0xFF` übersprungen.

⭐ **Das 20er-Raster der Fenstermasse und die fehlende Beschneidung sind
dieselbe Entscheidung.** Für unseren Nachbau: wer Fenster in beliebiger
Punktgrösse zulässt, muss eine Beschneidung **erfinden**, die das Original nicht
hat — und bekommt ein anderes Bild.

#### Der Fenstersatz, soweit gelesen

| Versatz | Breite | Inhalt |
|---|---|---|
| `+0x00` | Byte | **Fensterart** (1…48), 0 = Platz frei |
| `+0x02` / `+0x04` | Wort | x / y |
| `+0x06` / `+0x08` | Wort | Breite / Höhe in Punkten |
| `+0x0A` | Byte | (Art 30 nullt es) |
| `+0x0C` | Text | **Fenstertext / Titel** |
| `+0x1394` | Wort | Bildnummer (Art 30) |
| `+0xAC9C` | Dword | **Zeiger auf den Punktpuffer** |
| `+0xACA0` | Wort | Betriebsart (Art 3) |
| `+0xAD23` | Byte | Zoomstufe (Art 3), Index in `0x4FD610` |

### BA.6 Die Lader

**`0x45A5F0` / F `0x459290` — Art 30, das Hilfefenster** (1520 B, 2 Rufer
`0x4432E0`, `0x443490`):
* ⭐ **`HELPG.DAT` ist belegt, nicht mehr vermutet:** `dword[0x8B62B0 + 4·textnr]`
  (F `0x8B5310`) liefert die **Bildnummer**. 4000 B = 1000 Dwords, geht auf.
* Bild: `fseek((nr−1)·3600)`, `fread(0x8B7258, 1, 0xE10)`.
  **`0xE10 = 3600 = 60 × 60`; `129600 / 3600 = 36` Bilder, Rest 0.**
* ⭐ **Wie `HELPG.TXT` zerlegt wird** (`0x45A695 … 0x45A762`): Byte für Byte über
  `fread(buf,1,1,f)` in einen **5036-Byte-Stapelpuffer** (`mov eax, 0x13AC`):
  `0x0D` → verwerfen · `0x0A` → **wird zum Leerzeichen**, vorher werden
  nachlaufende Leerzeichen rückwärts abgeschnitten (**Absätze werden zu einer
  Zeile verklebt**) · `0x23` (`#`) → **Satzende**, das `#` wird zur Null · danach
  `buf[3] = 0` und `atoi` → **die Satznummer hat genau DREI Ziffern**. Nicht
  gefunden → `" : Text is not in the file."` (C `0x5015E8`).
* Das Fenster ist **360 Punkte breit**, startet mit **Höhe 10** und **wächst
  zeilenweise**; Umbruchgrenze ist `Breite − 40 = 320` (`0x45A967`).
  ⭐ **Deshalb hat es keine feste Grösse und fehlt in der Tafel von BA.4.**

**`0x45AD10` / F `0x4599B0` — Art 32, die Enzyklopädie**, 500 × 340:
⭐ **`ENCYCLOG.DAT` ist jetzt gedeutet, nicht nur »geht auf«:**

| Block | C | F | Bedeutung, belegt durch |
|---|---|---|---|
| A | `0x8B8070` | `0x8B70D0` | **Byteversatz in `ENCYCLOG.TXT`** — Argument von `fseek` (`0x45AD6D`) |
| B | `0x9927C8` | `0x991828` | **Anzahl Bytes** — Schleifenschranke (`0x45ADC1`) |
| C | `0x991820` | `0x990880` | **Bildnummer in `ENCYCLOG.PIC`** — `fseek((b−1)·3600)` (`0x45AE1A`) |

Textzerlegung viel einfacher als bei HELPG: `count` Bytes einzeln lesen,
**alles ab `0x20` übernehmen**, `0x0A` als Leerzeichen anhängen, in Puffer
C `0x892130`. **Kein `#`, keine Nummern, keine Umbruchlogik** — der Satz wird
über Versatz + Länge geschnitten, nicht gesucht.
⚠ `cmp al,0xFF; ja` bei `0x45AD9C` ist **toter Code** (ein Byte ist nie > 0xFF).
⭐ **`ENCYCLOG.PIC`: `345600 / 3600 = 96` Bilder à 60 × 60, Rest 0** — dasselbe
Bildmass wie `HELPG.PIC`. Puffer C `0x8B5490`.

**⚠⚠ BERICHTIGUNG: `0x458000` lädt `PANEL.DTA` NICHT.** Sie ist der Anleger des
204 × 170-Fensters (Art 9); der Name taucht dort nur als Argument auf. **Der
Lader bleibt `0x4B9F70`.**
⭐ Sie ist aber der **unabhängige Beleg für das Panelmass**: `word[+0x06] = 204`,
`word[+0x08] = 170`, und die `malloc`-Grösse wird als **34 680** ausgerechnet
(`0x4580AA…0x4580BE`) — **genau die Dateigrösse. Das Mass steht zweimal getrennt
im Programm.**

### BA.7 ⚠⚠ EIN SAUBERER NEGATIVBEFUND: die zwei grössten Funktionen des Reviers zeichnen NICHTS

Das Revier `0x450000 … 0x45B000` zerfällt in **zwei** Bausteine. Alles
**unterhalb `0x455B00`** gehört zum **Waffen- und Wirkungsmodul** (die
tschechischen Marken `kresli_laser1` @`0x4554A0`, `kresli_laser2` @`0x454CF0`,
`Add missile` @`0x451B40` stehen mittendrin). **Erst ab `0x455C50` beginnt die
Oberfläche.**

* **`0x453AA0`** (C 1888 B / F `0x452750`, Rufer `0x454300`, `0x4543C0`)
  berechnet den **Mündungspunkt und erzeugt ein Geschoss**: Einheitensatz
  `0x6E26C8` (Schrittweite 78), **Feinraster 40 Unterschritte je Zelle**
  (`imul ax, ax, 0x28`), `fild/fsqrt` für die Entfernung, eine **8-Wege-
  Sprungtafel bei `0x454060`** für den Rohrversatz, dann `0x4517A0`
  (Geschoss anlegen) und eine Warteschleife auf `0x452190`. Waffentafel
  `0x4F98FC`, Schrittweite 22.
* **`0x454600`** (C 1248 B / F `0x4532B0`, **1 Rufer: `0x415CF0` = `Main_funct`**)
  ist der **wachsende Explosionsring**: läuft nur jeden dritten Takt
  (`dword[0x4FA240] % 3 == 0`), geht die Liste ab `0x88E390` durch und sucht alle
  Zellen, deren **`sqrt(dx²+dy²)` genau dem gespeicherten Radius gleicht**. Für
  jede: Belegungskarte `0xBDEA80` befragen, `0x40C9A0` (`Zasah`) rufen, und über
  `0x435950` Wirkungsbilder streuen (`rand()%9 + 0x1FE`, `rand()%6 + 0x136`,
  `rand()%0x3C + 10`).
* Ebenso **kein Zeichencode**: `0x4536C0` (Einheitensatz, `fild/fsqrt` →
  Entfernung, 4 Rufer) und `0x4554F0` (Geschossmodul, Daten `0x884730 ff.`,
  2 Rufer).

### BA.8 ⭐⭐ DER ZEHNTE AUSLIEFERUNGSUNTERSCHIED: C hat 48 Fensterarten, F nur 47

Nicht abgetastet — die **Sprungtafel aufgezählt**:

| | C | F |
|---|---|---|
| Verteiler | `0x487630`, 992 B | `0x485D00`, 976 B |
| Schranke | `cmp eax, 0x2F` (47) | `cmp eax, 0x2E` (46) |
| Tafel | `0x487888` | `0x485F4C` |
| Arme | **48** | **47** |

Die Arme 1…47 entsprechen einander eins zu eins (C-Arm 47 `0x487180` ↔ F-Arm 47
`0x485850`, beide »Synchronisieren…«). **Es fehlt in F genau Art 48 =
C `0x480650`** — das zweite Hauptmenü mit dem Eintrag **»Enzyklopädie«**
(Art 35 `0x480390` ist das Menü mit »Netzwerkspiel«).

⭐ **Vier voneinander unabhängige Zählungen, dieselbe Erklärung:** F hat keinen
Anleger für Art 48 · der Rahmenzeichner hat in F **43** statt 44 Rufer · die
Knopfroutine **117** statt 123 · und die Sprungtafel einen Arm weniger.

⚠ Das ist der **einzige** Unterschied dieses Laufs, der behauptet wird. Alle
anderen Abweichungen der Ähnlichkeitszahlen sind Registerwahl und **werden
verworfen**.

### BA.9 Weitere eingeordnete Funktionen

| C | F | Byte | Rufer | Was es ist |
|---|---|---:|---|---|
| `0x4589B0` | `0x457650` | 768 | 1 | Anleger Art 14, 300 × 400 (»Spielstand laden«) |
| `0x458CB0` | `0x457950` | 800 | 1 | Anleger Art 15, 300 × 360 (»Spiel speichern«) |
| `0x458150` | `0x456DF0` | 368 | 6 | Anleger Art 10 — ruft danach den Fensterverteiler `0x487630` |
| `0x459110` | `0x457DB0` | 736 | 5 | **`gebaeudename(platz)`** — »Bahnhof «, »Rohstoff-Mine «, »Hafen «, »Basis «, »Waffenfabrik «, »Fahrwerkfabrik «, »Spezialfabrik «, »Flughafen « nach `byte[0xC06914]` |
| `0x4501C0` | `0x44EE70` | 224 | 18 | **`fenstertitel_setzen_wenn_anders`** — vergleicht mit `+0x0C` und kopiert nur bei Unterschied |
| `0x457300` | `0x455FA0` | 192 | 8 | vier Kacheln, nur von Art 1 (Einheiten-Menü) |
| `0x4588E0` | `0x457580` | 208 | 5 | schreibt nach `+0xAD20`, `sprintf`-artig |
| `0x45A560` | `0x459200` | 144 | 4 | Texthilfe, ruft `0x4BA0A0` |
| `0x455D50` | `0x4549F0` | 96 | — | `WINDOWS.CWW` laden |
| `0x456A50` | `0x4556F0` | 624 | — | vertiefte Mulde |
| `0x457730` | `0x4563D0` | 400 | — | Fenster anlegen |
| `0x4409E0` | `0x43F9F0` | 1008 | — | Fenster auf den Schirm |

**Aufgelöste Stummel:** `0x402275→0x455DB0` · `0x401CF8→0x455CF0` ·
`0x4015AF→0x4409B0` · `0x402306→0x4517A0` · `0x4014B0→0x452190` ·
`0x401C08→0x4BA160` (Textbreite) · `0x4020A4→0x4BA5E0` (Text zeichnen) ·
`0x40155F→0x435BD0` · `0x401AAF→0x41D0E0` (`terrain_at`) ·
`0x401217→0x40C9A0` (`Zasah`) · `0x401172→0x435950` · `0x4010BE→0x43B750`.

### BA.10 ⚠ Was offen blieb, und wodurch das Verfahren blind ist

1. ⚠ **`0x4517A0` gegen F `0x450450`: Ähnlichkeit 0.758 bei gleicher Grösse und
   gleicher Ruferzahl.** Der Unterschied wurde **nicht** am Kontrollfluss erklärt
   und darum **nicht** als Befund gemeldet. Die einzige offene Stelle der
   C/F-Zuordnung im Revier.
2. ⚠ **Art 3 (Kartenfenster) hat keinen gefundenen Anleger.** Der Sucher verlangt
   die Platzsuche `cmp si, 0x14` im selben Rumpf; wo sie ausgelagert ist, findet
   er nichts. **Bekannte Blindheit, kein Beleg für Nichtexistenz.**
3. ⚠ Die Elemente **17…24** (Innenflächen-Sprenkel) wurden nicht angesehen —
   Element 16 hat genau eine Farbe (`0x2C`), ob 17…24 Schmutz, Nieten oder
   Schatten sind, sagt nur das Bild.
4. ⚠ **`0x455E50` ist nur statisch gelesen.** Der Rahmen wurde **nicht**
   nachgezeichnet und gegen ein Bildschirmfoto gehalten. Die Eckenzuordnung ruht
   auf zwei unabhängigen Stützen (Stapelrechnung + Kacheleinrückung), aber **auf
   keinem Bild**. ⭐ Das wäre der nächste, billige Prüfstand: 314 Elemente
   auspacken, `rahmen_zeichnen(0,0,15,17,…)` nachrechnen, mit einem echten
   Fenster vergleichen.
5. ⚠ Die Bedeutung von `arg7` (Titelleiste) und `arg8` (Sonderecke 297) ist **aus
   der Form erschlossen, nicht aus den Rufern**.
6. ⚠ **Die Ähnlichkeitszahl ist kein Beweis.** `difflib` über normierte Befehle
   bestraft Registerwahl genauso hart wie echte Logikänderungen. Jede Zahl unter
   1.000 wurde von Hand nachgesehen; **wer die Tafel später weiterverwendet, darf
   die Zahlen nicht als »Unterschied« lesen.**
7. ⚠ **Die Ruferzählung geht über `E8`-Aufrufe mit aufgelösten Stummeln.**
   Aufrufe über Zeiger oder Sprungtafeln zählt sie **nicht**. Für `0x455CF0` mit
   363 Stellen folgenlos — für `0x459B70`, `0x45B8A0` und `0x45CA30` mit
   »0 Rufern« wäre es genau die falsche Schlussfolgerung: **diese drei werden
   über die Verteilertafel erreicht.**

---

## BB. ⭐⭐⭐ DIE WEGSUCHE DES ORIGINALS (21.08.2026)

**Die wichtigste offene Frage des Projekts ist beantwortet.** Unsere Navigation
war von Anfang an **komplett eigene Erfindung**; sie kann jetzt durch das ersetzt
werden, was hier steht.

> ⭐⭐ **`0x4D1170` ist NICHT die Wegsuche — es ist der Bauer der
> Passierbarkeitskarte mit 14 Bewegungsarten. Die eigentliche Wegsuche sind
> `0x4D2580` / `0x4D2A10` / `0x4D2E40`, und sie ist eine reine
> 8-Nachbar-BREITENSUCHE mit Wellenmarken — ohne Kostenfunktion, ohne
> Heuristik, ohne Prioritätswarteschlange.**

### BB.0 Adresstafel

Alle F-Adressen wurden über **Bytefolgen mit maskierten Absolutadressen und
rel32 gemessen**, nicht gerechnet. Prüfstein: das Werkzeug liefert für `Search:`
von selbst C `0x4D3810` → F `0x4D33A0` — genau den Wert, der schon feststand.

| Was | C | F |
|---|---|---|
| ⭐ **Passierbarkeitskarte bauen** (14 Arten) | `0x4D1170` | `0x4D0D20` |
| dessen Sprungtafel, 14 Einträge | `0x4D1E7C` | `0x4D1A14` |
| ⭐ **BFS für 1×1-Rümpfe** | `0x4D2580` | `0x4D2110` |
| ⭐ **BFS für 2×2-Rümpfe** | `0x4D2A10` | `0x4D25A0` |
| ⭐ **BFS für 4×4-Rümpfe** | `0x4D2E40` | `0x4D29D0` |
| Nachbarprüfung 2×2 | `0x4D2370` | `0x4D1F00` |
| Nachbarprüfung 4×4 | `0x4D23D0` | `0x4D1F60` |
| Einreihen (2×2-Pfad) / (4×4-Pfad) | `0x4D2210` / `0x4D2280` | `0x4D1DA0` / `0x4D1E10` |
| ⭐ **Vorgängersuche beim Rückverfolgen** | `0x4D2470` | `0x4D2000` |
| `Search:` (Auftragsabarbeiter) | `0x4D3810` | `0x4D33A0` |
| dessen Sprungtafel, 7 Einträge (`Art>>1`) | `0x4D463C` | `0x4D41CC` |
| Auftrag ablegen (`Search buffer is full!`) | `0x4D32C0` | `0x4D2E50` |
| ⭐ **Auftrag erteilen** | `0x40B070` | `0x40AF60` |
| Unterklassentafel (gerade / ungerade Arten) | `0x40B1EC` / `0x40A208` | `0x40B0DC` / `0x40A148` |
| ⭐ **Weg verbrauchen** (eine von vier Stellen) | `0x407C39` | `0x407B63` |
| `Can_go(Einheit, Richtung)` | `0x404E80` | `0x404E60` |
| Schritt ausführen | `0x4052D0` | `0x4052B0` |
| Geländebyte lesen | `0x41D110` | `0x41C2D0` |
| Kartengrenzen-Test | `0x41D1D0` | `0x41C390` |
| ⭐ **Ringtafel nach Abstand erzeugen** | `0x438790` | `0x4378F0` |
| Brückenplatz vergeben (100 × 24) | `0x4CC280` | `0x4CBE30` |
| Brückenvorschau / als Probelauf | `0x4CCCB0` / `0x4CD900` | `0x4CC850` / `0x4CD4A0` |
| ⚠ Filmabspieler (**kein Spielcode**) | `0x4D49F0` | `0x4D4580` |

**Daten** — ⚠ die drei verschiedenen Abstände (`0xF98`, `0xFA0`, `0x1000`) sind
genau der Grund, warum F-Adressen **gemessen** und nicht gerechnet werden dürfen:

| Was | C | F | Δ |
|---|---|---|---|
| ⭐ **Passierbarkeitskarte** (256 × 256 B, Kladde) | `0xBCA0E8` | `0xBC9148` | `0xFA0` |
| ⭐ **Wellen-Warteschlange** (≥ 5000 × 2 B) | `0xBC7830` | `0xBC6890` | `0xFA0` |
| Schreibzeiger / Lesezeiger (u16) | `0xBC7828` / `0xBDA8B8` | `0xBC6888` / `0xBD9918` | `0xFA0` |
| Wellengrenze (u16) / Wellenmarke (u8) | `0xBC7820` / `0xBDACA8` | `0xBC6880` / `0xBD9D08` | `0xFA0` |
| Schrittzahl des letzten Weges (u16) | `0xBCA0E0` | `0xBC9140` | `0xFA0` |
| eigene Einheitennummer während der Suche | `0xBC9F40` | `0xBC8FA0` | `0xFA0` |
| ⭐ **Wegpuffer = sec14**, 8000 × 50 B | `0x7AEC38` | `0x7ADC98` | `0xFA0` |
| ⭐ **Richtungstafel**, 8 × (i16, i16) | `0x4F5AF0` | `0x4F4AF0` | **`0x1000`** |
| ⭐ **Umkehrtafel**, 8 Byte | `0x539B20` | `0x538B88` | **`0xF98`** |
| Ringtafel nach Abstand (≤ 20000 × 2 B) | `0x79A008` | `0x799068` | `0xFA0` |
| Radiusindex (127 × u16) / Radius-50-Grenze | `0x834A80` / `0x834AE4` | `0x833AE0` / `0x833B44` | `0xFA0` |
| Rundungskonstante 0.5 (double) | `0x4F0268` | `0x4EF268` | `0x1000` |
| Brückentafel (100 × 24) | `0xBFEA80` | `0xBFDAE0` | `0xFA0` |
| Infanterie-Mehrfachzellen (4000 × 22) | `0x7847E8` | — | — |

### BB.1 ⭐ Der Bauer der Passierbarkeitskarte — 14 Bewegungsarten

`karte_bauen(u8 art, u8 spalte, u8 zeile)` — die Koordinaten sind die **eigene
Position** der Einheit. Alle drei Rufer sind die drei BFS-Rümpfe.
`cmp eax,0xD / ja ende / jmp [eax*4 + 0x4D1E7C]` — **eine Sprungtafel mit genau
14 Einträgen**, in beiden Fassungen mit `[12] == [6]` und `[13] == [7]`.

| Art | C-Rumpf | Regel je Zelle (sec6 = `word[0xBDEA80 + 2·(Sp·256+Ze)]`) |
|---|---|---|
| 0 | `0x4D118D` | **0** wenn `0xFFFE`, oder `<8000` ∧ Unterklasse 0, oder `10000…13999` ∧ `byte[Zellensatz+1]==0`; **1** wenn `0xFFFD`; sonst **2** |
| 1 | `0x4D127C` | wie 0, danach 5×5-Kasten: `<14000` → **2** |
| 2 | `0x4D1417` | **0** wenn `0xFFFE` ∨ `≥14000` ∨ `0xFFFD`; sonst **2** |
| 3 | `0x4D1482` | wie 2, danach 5×5-Kasten: `8000…13999` ∧ ¬`0x433DF0(sec6−10000)` → **2** |
| 4 | `0x4D15D5` | **1** = frei: Geländebyte 0 ∧ (`0xFFFE` ∨ eigene ∨ Unterklasse 0 ∨ Infanteriezelle mit `+1==0`); sonst **0** |
| 5 | `0x4D16C0` | wie 4, danach 5×5-Kasten: `<14000` ∧ ≠ eigene → **0** |
| 6 | `0x4D1886` | **1** = frei: Geländebyte 0 ∧ (**`0xFFFC`** ∨ (`<8000` ∧ Unterklasse ∈ {4,5})); sonst **0** |
| 7 | `0x4D193C` | wie 6, danach Kasten [Sp−2…Sp+5] × [Ze−2…Ze+5]: `<8000` ∧ ≠ eigene → **0** |
| 8 | `0x4D1ACC` | **0** wenn `0xFFFC` ∨ `0xFFFE` ∨ `<8000` ∨ (`0xFFFD` ∧ Geländebyte 0) ∨ Infanteriezelle mit `+1==0`; sonst **2** |
| 9 | `0x4D1B94` | wie 8, danach 5×5-Kasten: `<14000` → **2** |
| 10 | `0x4D1D07` | wie 2 |
| 11 | `0x4D1D6C` | wie 10, danach 5×5-Kasten: `<14000` → **2** |
| 12 | = `[6]` | dieselbe Karte wie 6, aber **4×4**-Suche |
| 13 | = `[7]` | dieselbe Karte wie 7, aber **4×4**-Suche |

**Drei Messlatten mit Nullmodell:**

| Aussage | Treffer | Nullmodell |
|---|---|---|
| Gerade Art = blanke Karte, ungerade = blanke Karte **+ Nachbearbeitung im Kasten** | **6/6 Paare** | Vorzeichen zufällig: 1/2⁶ = **1,6 %** |
| Alle 8 Arten der 1×1-Suche schreiben **0** für *frei*, alle 6 der 2×2/4×4-Suche **1** | **14/14** | 1/2¹⁴ = **0,006 %** |
| Nur die Arten 4,5,6,7 (und 12,13) lesen `0xBC9F40`, die anderen acht nie — und `Search:` setzt es genau dann | **6 von 14** | zufällige Auswahl: 1/C(14,6) = **0,033 %** |

⭐ **Und die Karte ist reine Kladde:** 44 Relokationsstellen auf `0xBCA0E8`
(13 Schreiber, 31 Leser), **in F auf `0xBC9148` exakt dieselben 13/31**, alle in
einem Fenster von rund 8 KB. Nullmodell (44 Stellen zufällig über 872 KB
`.text`, alle im selben 8-KB-Fenster): **≈ 10⁻⁹¹**.
Sie steht in **keinem** der 130 Abschnitte der Kartendatei — ebensowenig die
Warteschlange und ihre vier Zeiger. **Jede Suche baut sie neu.** Das ist der
wichtigste Freiheitsgrad für den Nachbau.

⚠ **Ein neuer sec6-Wert:** Art 6/7 behandeln **`0xFFFC`** als befahrbar, und
zwar zusammen mit Zellen, in denen eine Einheit der Unterklasse 4 oder 5 steht —
das sind genau die Schiffsrümpfe. `GAMESTATE_RE.md:1994` führt `0xFFFC` bisher
als »empty«. **Die Lesung »`0xFFFC` = Wasser« ist danach die weitaus
wahrscheinlichste — aber sie ist NICHT gemessen** (siehe BB.7).

### BB.2 ⭐⭐ Das Verfahren: Breitensuche mit Wellenmarken

`suche(u16 einheit, u8 zielSp, u8 zielZe, u32 art) → 0/1`. Alle drei Rümpfe
beginnen gleich:
1. `karte_bauen(art, eigeneSpalte, eigeneZeile)`,
2. **Kartenrand sperren** (Zeile 0, Zeile H−1, Spalte 0, Spalte B−1 auf **2**),
3. Startzelle markieren: **8** bei `0x4D2580`, **7** bei den anderen.

**Es gibt keine Kostenkarte, keine Heuristik, keine Prioritätswarteschlange.**
Jeder Schritt kostet gleich viel, eine Diagonale genauso viel wie eine Gerade —
die entstehende Metrik ist die **Chebyshev-Distanz**.

Die Entfernung wird **nicht gespeichert, sondern in die Karte geschrieben**:
jede erreichte Zelle bekommt die laufende **Wellenmarke**, die bei 8 (bzw. 9)
beginnt, bei jedem Wellenwechsel um 1 steigt und nach 255 auf 8 zurückspringt —
**248 unterscheidbare Wellen**. Der Wellenwechsel wird daran erkannt, dass der
Lesezeiger die gemerkte **Wellengrenze** erreicht.

**Erweiterungsregel 1×1 (`0x4D2580`), in genau dieser Reihenfolge:**
```
Diagonalen ZUERST — jede mit ZWEI Zusatzbedingungen:
  (Sp−1,Ze+1)  wenn karte==0 ∧ karte[Sp−1,Ze]≤1 ∧ karte[Sp,Ze+1]≤1
  (Sp−1,Ze−1)  wenn karte==0 ∧ karte[Sp,Ze−1]≤1 ∧ karte[Sp−1,Ze]≤1
  (Sp+1,Ze−1)  wenn karte==0 ∧ karte[Sp+1,Ze]≤1 ∧ karte[Sp,Ze−1]≤1
  (Sp+1,Ze+1)  wenn karte==0 ∧ karte[Sp,Ze+1]≤1 ∧ karte[Sp+1,Ze]≤1
dann die Geraden — OHNE Zusatzbedingung:
  (Sp,Ze+1) (Sp,Ze−1) (Sp+1,Ze) (Sp−1,Ze)   wenn karte==0
```
⭐ **Ecken werden nicht geschnitten:** jede Diagonale prüft genau die beiden
anliegenden geraden Nachbarn. **8/8 Bedingungen haben diese Form**, keine
Ausnahme. Nullmodell (jede Bedingung eine von 8 Nachbarzellen): 8⁻⁸ ≈ **6·10⁻⁸**.

⚠ **Der Wert 1 ist »weich gesperrt«**: man darf nicht hinein, aber eine Diagonale
daran vorbei ist erlaubt. **Nur 2 ist hart.**

**2×2 und 4×4** (`0x4D2A10`, `0x4D2E40`) sind **byteweise identisch bis auf ein
einziges Sprungziel** — den Aufruf der Nachbarprüfung. Sie nehmen eine Zelle,
wenn `karte[c] == 1` **und** die ganze 2×2- bzw. 4×4-Fläche `≠ 0` ist.
⚠ Diese beiden prüfen die Ecken **nicht** — die Rumpfbedingung ersetzt sie.

**Die Grenzen, genau so, wie man sie bauen muss:**

| Grösse | Wert | Fundstelle C |
|---|---|---|
| Ringlänge, 2×2/4×4-Pfad | **5000 Einträge** (`0x1388`) | `0x4D2B88`, `0x4D2247`, `0x4D22BB` |
| Ringlänge, 1×1-Pfad | **4096 Einträge** (Bytemaske `and si,0x1FFF`) | `0x4D26BC`, `0x4D26EB` |
| Weglänge im Puffer | **50 Schritte** (`0x32`) | `0x4D2840`, `0x4D2905`, `0x4D2CDC`, `0x4D2D34` |
| Wellenmarken | **8 … 255** | `0x4D268B` |

⚠⚠ **Zwei verschiedene Ringlängen auf demselben Puffer.** Der 1×1-Pfad maskiert
Byteversätze mit `0x1FFF` (8192 Byte = 4096 Einträge), die anderen zählen
Einträge und schlagen bei 5000 um. Der Puffer muss also **mindestens 10 000 Byte**
gross sein; der 1×1-Pfad nutzt nur die ersten 8192. **In sich ist jede Variante
widerspruchsfrei — wer nachbaut, darf das nicht vereinheitlichen.**

**Abbruch:** Ziel erreicht → `karte[Ziel] = 5` · Warteschlange leer → raus, und
wenn `karte[Ziel] != 5` → **Rückgabe 0 = kein Weg**. ⚠ Läuft der Ring über, holt
der Schreibzeiger den Lesezeiger ein und die Suche meldet **fälschlich** »kein
Weg«. Bei ≤ 254×254 ist die Front nie grösser als rund 1000 — praktisch
unerreichbar, aber die Grenze darf nicht unterschritten werden.
**Es gibt keinen Zeit- oder Knotenzähler.** Die Suche läuft in einem Zug durch.

### BB.3 ⭐ Das Rückverfolgen und das Wegformat

Von der Zielzelle rückwärts: gesucht wird der Nachbar mit der Wellenmarke
`aktuell − 1`. Der 1×1-Pfad macht das inline, die anderen über `0x4D2470`:
**erst die geraden Richtungen (0,2,4,6), dann die diagonalen (1,3,5,7).**

⭐ **Richtungstafel `0x4F5AF0` / F `0x4F4AF0`** — in beiden Fassungen bitgleich:

| i | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| dSpalte | 0 | −1 | −1 | −1 | 0 | +1 | +1 | +1 |
| dZeile | +1 | +1 | 0 | −1 | −1 | −1 | 0 | +1 |

⭐ **Umkehrtafel `0x539B20` / F `0x538B88`** — `{4,5,6,7,0,1,2,3}`, also genau
`i XOR 4`. Sie wandelt »Richtung zum Vorgänger« in »Fahrtrichtung«.

⭐⭐ **Die Messlatte:** der 1×1-Pfad kennt die Umkehrtafel nicht, er schreibt die
Ziffern direkt hin. Sind diese acht Inline-Konstanten genau `Index XOR 4`?

| Versatz zum Vorgänger | (0,+1) | (0,−1) | (−1,0) | (+1,0) | (+1,+1) | (+1,−1) | (−1,−1) | (−1,+1) |
|---|---|---|---|---|---|---|---|---|
| Index in `0x4F5AF0` | 0 | 4 | 2 | 6 | 7 | 5 | 3 | 1 |
| erwartet (`XOR 4`) | 4 | 0 | 6 | 2 | 3 | 1 | 7 | 5 |
| **im Code (`0x4D28B8…`)** | **4** | **0** | **6** | **2** | **3** | **1** | **7** | **5** |

**8/8 = 100 %.** Nullmodell (zufällige Permutation von 0…7): 1/8! = **0,0025 %**.

**Wegpuffer:** `0x7AEC38 + 50·EinheitsNr`, ein Byte je Schritt, Index 0 = erster
Schritt. Schrittzahl < 50 → an Index `schrittzahl` die **Endemarke `0xFF`**;
länger → nur 0…49 geschrieben, **keine Endemarke**.

⭐ **Unabhängige Bestätigung der 50:** `0x7AEC38` ist **sec14** mit Grösse
`0x61A80 = 400 000 = 8000 × 50`. Die Zahl aus dem Code (`cmp …,0x32`) und die
aus dem Dateiformat stimmen — **zwei völlig getrennte Quellen**.

⚠ **Ein Schönheitsfehler, in BEIDEN Fassungen.** Vor der Rückverfolgungsschleife
prüft der Umlauf richtig auf 7 (`cmp cl,7` @C `0x4D289E`), **in** der Schleife
aber auf **3** (`cmp cl,3` @C `0x4D28AD`) — byteweise gleich in F. Fällt die
Marke von 8 auf 7, greift der Umlauf nicht; vier Durchläufe lang findet die
Nachbarsuche nichts. Feuert erst bei Wegen von mehr als 247 Wellen.
**Für den Nachbau: den Umlauf bei 7 machen.** Der Unterschied ist in keinem
Spielstand messbar. (Kein Fassungsunterschied — nach Regel 1 sauber.)

### BB.4 ⭐⭐ Unterklasse → Auftragsart → Karte → Rumpfgrösse

`0x40B070` schreibt in den Einheitensatz: `+0x18`/`+0x19` **Zielspalte/Zielzeile**,
`+0x1A` **Schrittzeiger** (`0xFF` = kein Weg / fertig), `+0x0D` Suchmarke.
Dann springt sie über `byte[Einheit+0x0A]` (die **Unterklasse**) in eine
6-Eintrag-Tafel:

| Unterklasse | Art (gerade) | Art (ungerade) |
|---|---|---|
| 0 | **0** | **1** |
| 1 | **2** | **3** |
| 2 | ⚠ **kein Auftrag** | ⚠ **kein Auftrag** |
| 3 | **4** | **5** |
| 4 | **6** | **7** |
| 5 | **12** | **13** |

⚠ **Beide Tafeln haben in F dasselbe Loch bei Unterklasse 2.**

`Search:` liest je Takt einen Auftrag, verschiebt die Art (`byte[+0x0B] == 7` →
`+8`; `== 0x11` → `+0xA`), und wählt dann den Rumpf:
`Art < 4` → 1×1 · `Art < 8` → **2×2** · `Art < 0xC` → 1×1 · sonst → **4×4**.
Bei 2×2 und 4×4 wird zusätzlich `word[0xBC9F40]` gesetzt.

⭐⭐ **Die Messlatte, die alles zusammenbindet** — die Unterklasse bestimmt an
**drei völlig getrennten Stellen** dieselbe Rumpfgrösse:

| Unterklasse | Stempelcode `0x4110E3` | Auftragsart | Rumpfwahl in `Search:` | stimmt? |
|---|---|---|---|---|
| 0 | 1 Zelle | 0/1 | 1×1 | ✓ |
| 1 | 1 Zelle (Mehrfachbelegung) | 2/3 | 1×1 | ✓ |
| 2 | Fehlerzweig | **kein Auftrag** | — | ✓ |
| 3 | 2×2 | 4/5 | 2×2 | ✓ |
| 4 | 2×2 | 6/7 | 2×2 | ✓ |
| 5 | 4×4 | 12/13 | 4×4 | ✓ |

**6/6 = 100 %.** Nullmodell: 6!/(2!·2!) = 180 gleichwahrscheinliche Anordnungen
→ **0,56 %**.

⭐ Und die Karte passt dazu: Art 6/7 und 12/13 benutzen **dieselbe** Kartenregel
(`[12]=[6]`, `[13]=[7]`), nämlich die, die genau Zellen mit Unterklasse 4 oder 5
freigibt — **die Schiffe**. Art 2/3 (Infanterie) gibt alles ab 14000 frei: Wald,
Gebäude, Objekte — **Infanterie darf hindurch.**

### BB.5 ⭐ Wie der Weg abgefahren wird — und was bei mehr als 50 Schritten passiert

Vier Stellen lesen den Wegpuffer (`0x407C50`, `0x407DF6`, `0x408B3D`,
`0x409106`, Protokollmarken `move M` / `N` / `O` / `P`). Gelesen wurde
`0x407C39` / F `0x407B63`:

```
bl = byte[Einheit+0x1A]                       Schrittzeiger
if (bl == 0x32) {                             ⭐ 50 Schritte abgefahren
      byte[Einheit+4] = 0xFF ; word[Einheit+6] = 0
      0x40B070(Einheit, byte[+0x18], byte[+0x19], 0)   NEUEN AUFTRAG ERTEILEN
      raus }
al = byte[0x7AEC38 + 50·EinheitsNr + bl]
if (al == 0xFF) {                             Weg zu Ende
      byte[Einheit+4] = 0xFF ; word[Einheit+6] = 0
      byte[Einheit+0x14] = 0 ; byte[Einheit+0x1A] = 0xFF
      0x411670(Einheit)                       Ankunft melden
      raus }
if (Can_go(Einheit, al) && 0x4055D0(Einheit, al) == 2) {
      byte[Einheit+4] = al                    ⭐ Fahrtrichtung 0..7
      byte[Einheit+0x1A]++
      0x4052D0(Einheit, al) }                 Schritt ausführen
```

⭐ **Damit ist beantwortet, was bei einem Weg über 50 Schritte passiert: das
Original plant alle 50 Schritte NEU**, mit demselben Endziel aus `+0x18/+0x19`.
Es gibt keinen längeren Weg im Speicher, und **es gibt keine Wegglättung**.

**Wenn kein Weg gefunden wird:** es wird **nichts** in den Wegpuffer geschrieben,
`+0x1A` bleibt `0xFF`, der Lesezeiger rückt vor, der Auftrag ist weg. **Es gibt
keinen Wiederholungsmechanismus in `Search:`.**

⭐ Ergänzung zum Auftragsring: `0x4D32C0` prüft vor dem Anhängen den **ganzen
belegten Ring** auf die Einheitennummer; steht sie schon drin, wird nur die Art
überschrieben — **eine Einheit kann nie zweimal im Ring stehen.**

### BB.6 ⭐ Die Ersatzziel-Suche: eine Ringtafel nach EUKLIDISCHEM Abstand

`0x438790` / F `0x4378F0` baut beim Start:
```
für r = 0 … 126:
    Radiusindex[r] = laufender Zähler                (word[0x834A80 + 2r])
    für dSpalte = −r … r, dZeile = −r … r:
        wenn (int)(sqrt(dSp² + dZe²) + 0.5) == r:
            Tafel[zähler++] = (i8 dZeile, i8 dSpalte)    (0x79A008)
    Abbruch bei zähler == 20000
```
Die Rundungskonstante ist der `double 0.5` bei `0x4F0268` (gemessen).

`Search:` liest `word[0x834AE4]` = `Radiusindex[50]` und läuft die Tafel von
vorn durch, um zum belegten Ziel die **nächstgelegene freie Ersatzzelle** zu
finden. ⭐ `0x834AE4 − 0x834A80 = 0x64 = 2·50` — **die 50 ist doppelt belegt.**

⭐ **Für den Nachbau: ist das Ziel belegt, weicht das Original auf die nächste
freie Zelle im Umkreis 50 aus, in aufsteigender EUKLIDISCHER Entfernung** —
nicht in Chebyshev-Ringen. Das ist ein sichtbarer Verhaltensunterschied zu einer
naiven Spiralsuche.

### BB.7 ⚠⚠ Ein sauberer Negativbefund: drei der vier »grössten« gehören gar nicht zur Bewegung

* **`0x4CC280`** (2076 B, 5 Rufer) — **Brückenplatz vergeben.** 100 Plätze zu
  24 Byte bei `0xBFEA80` (= sec16, `0x960` = 2400 = 100·24), erster mit
  `+0x12 == 0`. Kachelnummern mit ausgewürfelter Variante
  (`Grundzahl·120 + (rand&1)·54 + …`). Deckt sich Wort für Wort mit der schon
  dokumentierten Lesung — **unabhängig bestätigt, nichts Neues.**
* **`0x4CCCB0`** (2512 B) — **Brückenvorschau.** Prüft ein **2×2-Fenster in
  sec2** (`0xA3AEB0`, 257 × 257, Index `a·257+b` — ⚠ die krumme 257er-Rechnung
  ist **kein Lesefehler**, sie steht so im Format), verlangt alle vier Werte
  `≤ 1` und bildet einen 4-Bit-Code. ⭐ Nur die vier Codes **{3, 12, 10, 5}**
  werden angenommen — genau die Muster, bei denen das 2×2-Fenster durch eine
  **gerade Naht** zerfällt; der Index geht in eine 4-Wege-Sprungtafel
  `0x4CD670` = die vier **Brückenrichtungen**.
* **`0x4CD900`** (2840 B) — **dieselbe Funktion als zerstörungsfreier Probelauf**:
  sichert die komplette Vorschauliste (`rep movsd`, 7200 Byte) auf den Stapel und
  stellt sie bei jedem Misserfolg wieder her. Die Diff enthält ausser
  Sprungzielen und Sicherungsblock **nur Registerumbenennungen**.
* ⚠⚠ **`0x4D49F0` ist KEIN Spielcode**, sondern ein **Filmabspieler**: acht
  numerierte Fehlerrückgaben, öffnet einen Datenstrom, fragt Breite/Höhe ab und
  pumpt in einer Nachrichtenschleife (`PeekMessage`/`TranslateMessage`/
  `DispatchMessage`) Bilder, bis `WM_QUIT` kommt. **Sie berührt weder die
  Passierbarkeitskarte noch den Wegpuffer noch den Auftragsring.**
  ⚠ Der Eindruck »die zweitwichtigste Funktion des Reviers« kam **allein aus der
  Ruferzahl** — ein Beleg dafür, dass `--gross` einen Arbeitsplan liefert und
  keine Bedeutung.

### BB.8 ⚠ Was offen blieb, und wodurch das Verfahren blind ist

1. ⚠⚠ **`0xFFFC` ist erschlossen, nicht gemessen.** Der Schluss »`0xFFFC` =
   Wasser« kommt aus Art 6/7. **`GAMESTATE_RE.md:1994` nennt es »empty« — einer
   der beiden Sätze ist falsch, und die Zahl fehlt.** Die richtige Messung wäre:
   `sec6 == 0xFFFC` gegen `sec2 == 1` kreuzen, Trefferquote gegen das Nullmodell
   »gleiche Randverteilung, zufällig gemischt«.
2. ⚠ **Die Felder `+0`/`+1` im 22-Byte-Satz der Infanteriezellen (`0x7847E8`).**
   Die Karte prüft `byte[Satz+1] == 0` als »begehbar«; `zz_deutung.py` deutet den
   Satz als »col, row, 9 × u16«. **Wäre `+1` die Zeile, ergäbe der Test keinen
   Sinn.** Eine der beiden Lesungen ist falsch — der Vergeber `0x433A50` wurde
   nicht aufgemacht. **Der Nachbau darf sich auf diesen Test noch nicht stützen.**
3. ⚠⚠ **Das Geländebyte `[0x677E20] + 4·(Zeile·Breite + Spalte) + 3`** (via
   `0x41D110`), das die Arten 4…7 als hartes Sperrkriterium benutzen, ist nicht
   identifiziert. **Beachte: dieses Feld ist zeilenweise mit Schrittweite =
   Kartenbreite indiziert, NICHT mit dem imap-Index `Spalte·256+Zeile`. Wer die
   beiden verwechselt, baut eine still verdrehte Karte.**
4. ⚠ **Fünf der sieben Vorbereitungsstücke in `Search:`** (`0x4D3B88`,
   `0x4D3C99`, `0x4D3D26`, `0x4D3DB3`, `0x4D4140` — rund 1600 Byte) sind
   ungelesen. **Dort sitzt die Logik »wohin darf diese Einheit überhaupt gehen
   wollen«, und ohne sie ist die Navigation nachbaubar, aber nicht deckungsgleich.**
5. ⚠ **Ein möglicher Pufferüberlauf, nicht aufgelöst.** Bei `0x407C00` steht
   `cmp bl, 0x32; jne`. Ist `bl` gleich `0xFF` — der Zustand nach einer
   fehlgeschlagenen Suche —, greift der Vergleich nicht und der Code liest
   `weg[0xFF]`, also **205 Byte über den 50-Byte-Satz hinaus**. Entweder gibt es
   weiter oben eine Wache, oder das Original liest hier fremde Wegdaten.
   Als **offen** gemeldet, nicht als Fund.
6. ⚠ Die Passierbarkeitskarte wurde **ausschliesslich über die Relokationstafel**
   gefunden; `--block` lief **nicht**. Der Satz »alle 44 Zugriffe liegen im
   Wegkern« gilt nur für **relozierte** Zugriffe.
7. ⚠ **Alle Zahlen stammen aus dem Code**, keine einzige aus einer laufenden
   Partie. Die einzige unabhängige Bestätigung ist sec14 = 8000·50. **Es wurde
   kein Weg gegen einen echten Spielstand nachgerechnet.**
8. ⚠ Die F-Zuordnung von `0x4D2A10` und `0x4D2E40` war über Bytefolgen
   **mehrdeutig** (die beiden sind Klone) und wurde über die aufgelösten
   Thunkziele der Nachbarprüfung getrennt — belastbar, aber ein zweiter Schritt.
9. ⚠⚠ **Der Kladdenspeicher steht in keinem Spielstand.** Umgekehrt heisst das:
   **an dieser Maschine lässt sich über `.DM`/`.CWM` gar nichts nachmessen.** Wer
   sie prüfen will, muss sie nachbauen und gegen den laufenden Prozess laufen
   lassen.

### BB.9 ⭐ Die Kurzanleitung zum Bauen

```
weg_suchen(einheit, zielSp, zielZe):
    art = artTafel[unterklasse(einheit)]           # 0/2/4/6/12 (+1 = Nahbereich sperren)
    if typ(einheit)==7:    art += 8
    if typ(einheit)==0x11: art += 10
    karte = baue_karte(art, spalte(einheit), zeile(einheit))   # 256x256 Byte, BB.1
    karte[Rand] = 2
    frei = 0 wenn art in {0,1,2,3,8,9,10,11} sonst 1
    karte[start] = 8 wenn 1x1 sonst 7
    warteschlange = [start]; marke = 9 wenn 1x1 sonst 8; schritte = 0
    solange warteschlange nicht leer (hoechstens 5000 Eintraege):
        wenn Wellengrenze erreicht: marke = 8 wenn marke==255 sonst marke+1; schritte++
        c = pop()
        wenn c == ziel: karte[c] = 5; break
        fuer n in [4 Diagonalen, dann 4 Gerade]:            # die Reihenfolge zaehlt
            1x1: nimm n, wenn karte[n]==0 und (bei Diagonalen) beide
                 anliegenden Geraden <= 1
            2x2: nimm n, wenn karte[n]==1 und die 2x2-Flaeche != 0
            4x4: nimm n, wenn karte[n]==1 und die 4x4-Flaeche != 0
            karte[n] = marke; push(n)
    wenn karte[ziel] != 5: return KEIN_WEG          # nichts schreiben, Auftrag verfaellt
    c = ziel; m = marke - 1 (Umlauf 7 -> 255)
    weg[schritte] = 0xFF   falls schritte < 50
    fuer k = schritte-1 abwaerts bis 0:
        i = erster Index aus [0,2,4,6, dann 1,3,5,7] mit karte[c + versatz[i]] == m
        wenn k < 50: weg[k] = i XOR 4
        c = c + versatz[i];  m = m - 1 (Umlauf 7 -> 255)
    return OK
```
Abfahren: Zeiger bei 0 beginnen, je Takt eine Ziffer, `0xFF` = angekommen, bei
Zeiger == 50 **neu planen mit demselben Ziel**.

---

## BC. ⭐⭐ GRAFIK, PALETTE UND SPRITES (21.08.2026)

### BC.0 Adresstafel

| Name (neu vergeben) | C | F | Byte | Rufer |
|---|---|---|---:|---:|
| ⭐ `Check_pal` | `0x4B5310` | `0x4B4C40` | 2096 (Rumpf **1674**) | 3 |
| `Schwarzpalette_erzeugen` | `0x4B5B40` | `0x4B5470` | 111 | 1 |
| `Schwarzpalette_setzen` | `0x4B5BD0` | `0x4B5500` | 19 | 5 |
| `palette_animieren` | `0x4B5BF0` | `0x4B5520` | — | 1 |
| `Load_ppp` | `0x4B3F50` | `0x4B3880` | 180 | 1 |
| `Save_ppp` | `0x4B4040` | `0x4B3970` | 153 | 2 |
| ⭐ `Teilchen_bewegen` | `0x4ADB80` | `0x4AD4B0` | 2304 | 1 |
| `Teilchen_Schleife` (0…999) | `0x4AE480` | `0x4ADDB0` | 64 | 1 |
| `Teilchen_zuteilen` | `0x4AD8B0` | `0x4AD1E0` | 720 | 3 |
| ⭐ `Bodenmarke_zuteilen` | `0x4AE4C0` | `0x4ADDF0` | 672 | **10** |
| ⭐ `Hoehe_an_Unterposition` | `0x4B62B0` | `0x4B5BE0` | 1520 | 1 |
| ⚠ dessen Sprungtafel (19 Einträge) | `0x4B6724` | `0x4B6054` | 76 | — |
| `Flugzeug_Kamerafahrt?` | `0x4B4B90` | `0x4B44C0` | 976 | 1 |
| `Kartenfenster_Rahmen` | `0x4B7ED0` | `0x4B7810` | 2560 | 9 |
| ⭐ `Kartenfenster_Inhalt` | `0x4B88D0` | `0x4B8210` | 2048 | 1 |
| ⭐ `Eckhoehe_max` | `0x4B7E70` | `0x4B77B0` | 96 | 2 |
| `Kartenfenster_Hilfsmaler` | `0x4B7AC0` | `0x4B7400` | 816 | 1 |
| ⭐⭐ `Bildschirmfoto_BMP` (**Taste F9**) | `0x4B9BB0` | `0x4B96B0` | 800 | 1 |
| ⭐ `Naechster_freier_Fotoname` | `0x4B9A80` | `0x4B9580` | 242 | 1 |
| `gelaende_typ(col,row)` = Byte `+3` | `0x41D110` | `0x41C2D0` | 28 | viele |
| `gelaende_lage(col,row)` = Byte `+2` | `0x41D0E0` | `0x41C2A0` | 28 | viele |
| `gelaende_wort_setzen` = Wort `+0` | `0x41D140` | `0x41C300` | 33 | viele |
| `Laufwerksbuchstabe(cd)` | `0x43B580` | `0x43A6F0` | 370 | — |
| Geländemaler (liest Schatten-Tafel) | `0x42C8C0` | `0x42BAA0` | 5280 / **5296** | 1 |
| Hülle um `Kartenfenster_Inhalt` | `0x446CD0` | `0x445C90` | 272 / 288 | 1 |

**Daten**

| Bedeutung | C | F | Δ |
|---|---|---|---|
| `.pal`-Rohpalette 768 B | `0xB12FB0` | `0xB12010` | `0xFA0` |
| ⭐ `.cws` **Schatten-Tafel** 256 B | `0xB135B0` | `0xB12610` | `0xFA0` |
| ⚠ `.cwg` Aufhell-Tafel 256 B (**tot**) | `0xB136B8` | `0xB12718` | `0xFA0` |
| `PALETTEENTRY`-Block (`0x404` B) | `0x538800` | `0x537840` | `0xFC0` |
| ⭐ **6 Palettenobjekte** | `0x540750…0x540767` | `0x53F7B0…0x53F7C7` | `0xFA0` |
| Schwarzpalette | `0x4F6F88` | `0x4F5F88` | `0x1000` |
| Missionsnummer (Byte) | `0x4F6FC4` | `0x4F5FC0` | `0x1004` |
| »Ohne CD«-Schalter (Byte) | `0x4F6F9C` | `0x4F5F98` | `0x1004` |
| ⭐ Zoomtafel `{1,2,3}` | `0x538870` | `0x5378B0` | `0xFC0` |
| ⭐ Geländetyp→Gruppe (19 B) | `0x5385F0` | `0x537630` | `0xFC0` |
| ⭐ **Kartenfarbgruppen, Schritt 7** | `0x538608` | `0x537648` | `0xFC0` |
| Landschaftsbyte | `0x6783E0` | `0x677440` | `0xFA0` |
| ⭐ Zeiger auf Geländeraster (**4 B je Zelle**) | `0x677E20` | `0x676E80` | `0xFA0` |
| ⭐ **Eckhöhengitter 257 × 257, 1 B** | `0xA3AEB0` | `0xA39F10` | `0xFA0` |
| Bodenmarkenfeld, 6 B je Satz | `0xA62A50` | `0xA61AB0` | `0xFA0` |
| Kartenmarkenzähler (Wort) | `0xB0D824` | `0xB0C884` | `0xFA0` |

### BC.1 ⭐⭐ `Check_pal` — die Palettenlogik

Baut aus dem Basisnamen + `itoa(Missionsnummer)` **drei** Dateinamen mit den
Endungen `.pal`, `.cws`, `.cwg`. Liegt die erste nicht im Arbeitsverzeichnis,
liefert `Laufwerksbuchstabe()` einen Buchstaben für `"X:\data\"`. Protokolliert
je Datei `f OK`/`f wrong`, `g OK`/`g wrong`, `h OK`/`h wrong`.

| Puffer | Datei | `fread` | Dateigrösse |
|---|---|---|---|
| `0xB12FB0` | `NN.PAL` | **8 B Kopf + `0x300` B** | **776** |
| `0xB135B0` | `NN.CWS` | `0x100` B | **256** |
| `0xB136B8` | `NN.CWG` | `0x100` B | **256** |

> ⭐ **Nullmodell:** `8 + 0x300 = 776` — die `.PAL` geht **restlos** auf, und
> `0x300 = 768 = 256 × 3` ist die einzige Zerlegung, die eine Palette ergibt.
> **Kein Byte bleibt in irgendeiner der drei Dateien übrig.**

Der Kopf der `.PAL` ist über alle 27 Dateien **konstant**
(`08 03 00 00 23 B1 00 00` = Grösse 776 + Kennung `0xB123`) — also **keine
Prüfsumme**, und er wird **nie ausgewertet**.

⭐ **Volle 8 Bit, kein 6-Bit-VGA:** in `01.PAL` sind **496 von 768** Byte > 63.
Und **Byte 0 = Rot**, zweifach belegt: `Check_pal` kopiert 0/1/2 nach
`peRed/peGreen/peBlue`, der BMP-Schreiber gibt `pal[2],pal[1],pal[0]` in die
`RGBQUAD`-Felder Blau/Grün/Rot.

#### ⭐⭐ Wie die sechs Paletten entstehen — und was flimmert

```
if (dword[0x538800] == 0) dword[0x538800] = malloc(0x404);   // 256*4 + 4
für n = 0..255:  tab[4n+0..2] = rgb[3n+0..2]                 // peFlags bleibt ungesetzt
für jeden der 6 Steckplaetze 0x540750 … 0x540764:
    if (*edi) Release(*edi);
    IDirectDraw::CreatePalette(vtbl+0x14, DDPCAPS_8BIT=4, tab, edi, 0);
    // danach Ringtausch ueber 0x3E0, 0x3E4, 0x3E8, 0x3EC, 0x3F0, 0x3F4
```

Der Ringtausch verschiebt **genau sechs** Einträge: **Palettenindex 248…253**.

> ⭐⭐ **Nullmodell:** `(0x3F4−0x3E0)/4 + 1 = 6` und `(0x540768−0x540750)/4 = 6`
> — **die Ringlänge ist gleich der Zahl der Palettenobjekte.** Palette *k* trägt
> den um *k* Stellen gedrehten Ring; nach 6 Drehungen ist man wieder am Anfang.
> Mit `palette_animieren` (Wechsel alle 4 Bilder, Zähler 0…23) ergibt das genau
> **24 Bilder Umlauf. Die Kette schliesst sich ohne Rest.**

**Gegenprobe an den Dateien:** die sechs Farben sind in `01.PAL`
`(39,71,115) (43,79,119) (51,91,123) (43,83,119) (39,75,115) (35,67,111)` — eine
**Dreieckswelle**: drei Stufen heller, drei dunkler, geschlossen. Genau die Form,
die eine Ringdrehung zu einem **sprungfreien Flimmern** macht. In `01`, `13` und
`90` byteweise identisch (blaues Wasser), in `40` dieselbe Form in Braun (Wüste).
In **24 von 27** Dateien sind die sechs Farben paarweise verschieden.

⚠ **Bei Misserfolg passiert NICHTS.** Ein `fopen`- oder `fread`-Fehlschlag wird
nur protokolliert (`f wrong`, bzw. `posranej 1/2/5/6` — tschechisch, derb), und
die Funktion läuft mit dem **alten Pufferinhalt** weiter und erzeugt trotzdem
sechs Paletten. **Es gibt keinen Notausgang und keine Vorgabepalette.**

⚠ **Eine Feinheit:** am Ende ruft `Check_pal` `Schwarzpalette_erzeugen`, und das
**nullt denselben Puffer `0x538800`** — beim ersten Aufruf. **Wer den Puffer nach
dem Laden ausliest, liest beim ersten Mal Schwarz.**

> ⭐ **Nullmodell für die Missionsnummern:** `Check_pal` verzweigt bei `< 0x10`,
> `< 0x28` und sonst. Vorhanden sind `01…15` (alle < 16), `21, 25, 26` (alle in
> [16,40)) und `40…47, 90` (alle ≥ 40). **Keine einzige Datei fällt aus dem
> Raster.** Die drei Zweige sind **CD 1 / CD 2 / Festplatte**.

### BC.2 ⭐ `.CWS` ist der Schatten — `.CWG` ist TOT

Beide sind Nachschlagetafeln Palettenindex → Palettenindex.

**`.CWS` dunkelt ab.** Über `01`, `13`, `40`: 214–217 von 256 Einträgen werden
dunkler, 0–3 heller, mittleres Helligkeitsverhältnis 0,82–0,85.
**Nullmodell:** für jeden Faktor *k* von 0,40 bis 2,00 wurde die Tafel »nächster
Index zu Farbe × k« gerechnet und gegen die echte gezählt. **Bestes k = 0,90 in
allen drei Dateien**, 98–114 exakte Treffer von 256.
⚠ **Der Faktor erklärt nur rund 40 %** — wer die Tafel nachbaut, **nehme sie aus
den Dateien, statt sie zu rechnen.**

**8 Lesestellen, in beiden EXE deckungsgleich:** C `0x42D39C`, `0x42D447`
(Geländemaler), `0x4B82AD`, `0x4B82B3` (Kartenrahmen), `0x4B8C09`, `0x4B8C0F`,
`0x4B8C30` (Kartenmaler).
⭐ **Im Kartenmaler wird sie ZWEIMAL hintereinander angewandt**
(`dl = cws[cws[dl]]`) — das ist der unerkundete Nebel; einmal für »bekannt, aber
nicht gesehen«.

⭐ **Sauberer Negativbefund zu `.CWG`:** die Vollerhebung über die
Relokationstafel findet in **C wie in F genau eine Lesestelle — das `fread`
selbst** — und einen Schreiber in einem Ladehelfer. **Kein Maler greift darauf
zu.** Passend dazu **fehlt `90.CWG` ganz**, und niemand merkt es: `Check_pal`
schreibt `h wrong` und läuft weiter.

### BC.3 ⭐⭐ `Load_ppp` / `Save_ppp` — es gibt KEINE `.ppp`-Datei

`ppp` ist **kein Dateiname und keine Endung**. Die Zeichenkette `".ppp"` kommt in
**keiner** der beiden EXE vor (Vollerhebung, 0 Treffer); die einzigen
`ppp`-Vorkommen sind die vier Protokollmarken. **Es ist ein Funktionsname, sonst
nichts.**

Die Datei heisst **`cw.tmp`** (`0x53859C` / F `0x5375DC`). Vier Blöcke am Stück:

| Block | C | F | Länge |
|---|---|---|---:|
| 1 | `0x51CE20` | `0x51BE60` | `0x11F80` = 73 600 |
| 2 | `0x52EDA0` | `0x52DDE0` | `0xD20` = 3 360 |
| 3 | `0x51B020` | `0x51A060` | `0x1E00` = 7 680 |
| 4 | `0x5045A0` | `0x5035E0` | `0x16A80` = **92 800** |

> ⭐⭐ **Nullmodell:** 73 600 + 3 360 + 7 680 + 92 800 = **177 440** — exakt die
> Grösse von `CW.TMP`. **Kein Kopf, kein Rest.** Und Block 4 hat exakt die Grösse
> von `PARTS.CWD` (92 800 B), das im selben Verzeichnis mit derselben Zeitmarke
> liegt.

`Save_ppp` setzt `byte[0x4F8A38] = 1`; `Load_ppp` lädt **nur**, wenn das gesetzt
ist **und** `word[0x539934] == 1`, und löscht es danach. Also ein **einmaliger
Übergabespeicher** zwischen zwei Spielabschnitten — **kein Anwenderformat**.

### BC.4 ⭐⭐ Das brauchbare Bildschirmfoto: Taste **F9**

`0x4B9BB0` / F `0x4B96B0`. **Kein Entwicklerschalter, kein `D:\`, richtige
Auflösung** — dem bekannten `0x418B00` in jeder Hinsicht überlegen.

⭐ In **C** ist es Fall 35 der 40-Einträge-Tastentafel, in **F** Fall 43 der 48 —
aber in **beiden** trägt die Bytetafel den Wert an Stelle **VK `0x78` = `VK_F9`**.
**Die Tastenzuordnung ist versionsübergreifend dieselbe, nur die Fallnummerierung
wurde umgestellt.**

* `rep stosd` × `0x10D` + `stosw` = **1078 Byte** genullt.
  > ⭐ `1078 = 0x436 = 14 + 40 + 1024` — Dateikopf + `BITMAPINFOHEADER` + 256 ×
  > `RGBQUAD`.
* Kopf: `'B','M'`, `bfSize = W·H + 0x436`, `bfOffBits = 0x436`, `biSize = 0x28`,
  `biWidth = dword[0x5387C8]`, `biHeight = dword[0x5387CC]`, `biPlanes = 1`,
  `biBitCount = 8`.
* Palette: 256 Einträge aus `0xB12FB2` in Dreierschritten als
  `RGBQUAD{Blau, Grün, Rot, 0}`. `(0xB132B2 − 0xB12FB2)/3 = 256`.
* `Lock` (vtbl `+0x64`) auf der **Primärfläche**, `DDSURFACEDESC.dwSize = 0x6C =
  108`, Wiederholung bei `DDERR_SURFACELOST`.
* Bildzeilen **von unten nach oben** — die richtige BMP-Reihenfolge.

**Der Dateiname** kommt aus `0x4B9A80`: probiert `screen1.bmp` … `screen99.bmp`
(`cmp ebx,0x64`), öffnet jeden mit `"rb"`, und **der erste, der sich nicht öffnen
lässt, wird angelegt.** Kein Pfad — Arbeitsverzeichnis.

### BC.5 ⭐ Das Kartenfenster: die Farbwahl, mit dem schönsten Nullmodell des Laufs

**Zoom:** Tafel `0x538870` = **`{1, 2, 3}`**, byteweise gleich in C und F; die
Dreifachverzweigung schreibt 1×1, 2×2 bzw. 3×3 Bildpunkte je Zelle.

```
g = byte[gelaende_typ(col,row) + 0x5385F0]     // 0..2
h = Eckhoehe_max(col,row)                       // 0..3
if (h == 0) g = 0
k = 3*(h + 4*byte[0x6783E0]) + g
n = byte[0x538608 + 7*k]
farbe = byte[0x538609 + 7*k + (rand() % n)]
```

Die Tafel ab `0x538608` ist **byteweise identisch in C und F**, Satzlänge **7**
(1 Anzahl + 6 Farben):

| k | h,g | n | Farben |
|---:|---|---:|---|
| 0 | 0,0 | 2 | 242 243 |
| **1, 2** | 0,1 / 0,2 | **0** | — |
| 3,4,5 | 1,* | 2 | 212 213 · 38 211 · 40 42 |
| 6,7,8 | 2,* | 2 | 195 196 · 192 193 · 198 199 |
| 9,10,11 | 3,* | 2 | 39 40 · 37 38 · 41 42 |
| 12 | nächste Landschaft, h=0 | 2 | 242 243 |
| **13, 14** | h=0, g≠0 | **0** | — |

> ⭐⭐ **Das schönste Nullmodell des Laufs:** die Formel erzwingt `g = 0`, sobald
> `h = 0`. Die Sätze `k = 1, 2` und `k = 13, 14` sind damit **unerreichbar** — und
> **genau diese vier, und nur diese, stehen leer in der Datei.** Die Satzlänge 7
> ist damit unabhängig belegt: bei jeder anderen Länge fielen die Nullsätze
> woandershin.

⭐ Und der Farbinhalt passt: `242…245` sind in `01.PAL` `(31,79,115) (23,71,107)
(15,67,99) (7,59,87)` — tiefes Blau, **dieselbe Familie wie der Flimmerring
248…253. Der Ring ist das Wasser.**

⚠ `n` ist überall **2**, also werden je Satz nur die ersten zwei Farben gewürfelt;
die dritte bis fünfte stehen **ungenutzt** in der Datei. Eine
Zwei-Farben-Rasterung.

**`Eckhoehe_max`** (`0x4B7E70`):
`i = col·257 + row; max(g[i], g[i+1], g[i+257], g[i+258])` mit `g = 0xA3AEB0`.
> ⭐ **Nullmodell:** `257 = 256 + 1`, und die vier Versätze `0, 1, 257, 258` sind
> genau die **vier Ecken einer Kachel** in einem Eckgitter zu einer
> 256×256-Kachelkarte. Damit ist `0xA3AEB0` das **Eckhöhengitter 257 × 257 zu je
> einem Byte** — dieselbe 257er-Kantenlänge, die aus sec2 schon bekannt ist.

### BC.6 ⭐ Die Höhe an einer Unterposition — 19 Geländeformen

`0x4B62B0`, ein Rufer im Geländemaler. `typ = gelaende_typ(col,row)`, `> 0x12`
→ 0, sonst Sprungtafel `0x4B6724` mit **19 Einträgen**.
⚠ **Die Tafel liegt unmittelbar hinter einem `ret` und wurde AUFGEZÄHLT**, nicht
abgetastet: `0x4B62DD, 62E5, 6301, 6321, 633E, 635A, 63A1, 63E6, 642E, 6472,
64B9, 64FE, 6543, 6587, 65E6, 6652, 6687, 66BB, 66EF`.

Die Fälle rechnen lineare Rampen: Fall 1 `h = unterX · 15 / 40`,
Fall 2 `h = 15 − unterY · 15 / 20`.
> ⭐ **Nullmodell:** 40 und 20 sind Kachelbreite und -höhe. Bei `unterX = 0`
> liefert Fall 1 exakt 0, bei 40 exakt 15; Fall 2 spiegelbildlich über 20. **Die
> Rampen schliessen an den Kachelrändern ohne Rest an — eine Höhenstufe ist
> 15 Bildpunkte.**

⭐ **Das Geländeraster:** alle drei Zugriffsfunktionen rechnen
`i = row·dword[0x542DC4] + col` und greifen auf `dword[0x677E20] + i·4` zu.
**4 Byte je Zelle**: Wort `+0` (geschrieben), Byte `+2` = Lage/Höhe, Byte `+3` =
Geländetyp (19 Werte).

### BC.7 Teilchen und Bodenmarken

**`0x4ADB80`** ist der **Schritt eines Teilchens**, gerufen aus der Schleife
`0x4AE480` (0…999), die wiederum aus der Marke `step:` in `Main_funct` kommt.
Satzlänge 36, Basis `0xA51110`:

| Versatz | Bedeutung |
|---|---|
| `+0x00` / `+0x01` | Spalte / Zeile |
| `+0x02` / `+0x03` | Unterposition X (0…39) / Y (0…19), **vorzeichenbehaftet** |
| `+0x06` | Zustand: 1 = fliegt, 2 = zweiter Zweig, 0 = frei |
| `+0x0A` / `+0x0B` | Zielspalte / Zielzeile |
| `+0x0C` / `+0x0D` | Ziel-Unterposition X / Y |
| `+0x08` (Wort) | laufende Grösse, gegen `+0x0E` interpoliert |
| `+0x10` | Teiler (Geschwindigkeit); 0 wird auf 1 gehoben |
| `+0x14` / `+0x1C` | Gleitkomma: Zuwachs und Summierer |
| `+0x20` (Wort) / `+0x22` | Winkel / Bildnummer |

> ⭐ **Nullmodell:** `0x28 = 40` und `0x14 = 20` als Überlaufschwellen, mit
> `jle 0x27` / `jle 0x13` — **die Kachel ist 40 × 20** und die Unterposition läuft
> 0…39 / 0…19. Deckungsgleich mit den Kamera-Versätzen aus dem Spielstand und mit
> den Rampenteilern in `0x4B62B0`.

**`0x4AE4C0`** ist ein zweites, kleineres Feld: **Bodenmarken.** Basis
`0xA62A50`, **Satzlänge 6**, **400 Sätze** (`cmp dx,0x190`), Notfall
`rand()%400`. `+0` Spalte, `+1` Zeile, `+2` Wort = `gelaende_lage(col,row) · 15`,
`+4` Belegtflagge, `+5` Lebensdauer (Vorgabe `0x32 = 50`).
⭐ **Die 15 ist dieselbe Zahl wie die Höhenstufe** in `0x4B62B0` — zwei
unabhängige Funktionen, ein Wert.
Gegenprobe: **54 Relokationsverweise ins Fenster `+0…+0x20` in C, 54 in F**,
Stelle für Stelle deckungsgleich (Abstand durchgehend `0x6D0`).

### BC.8 ⚠⚠ ZWEI WERKZEUGFUNDE, DIE ÜBER DIESES REVIER HINAUSGEHEN

1. ⚠⚠ **Die Weichentafel hat 1109 Einträge in C und 1107 in F — NICHT 1047.**
   Ununterbrochene `E9 rel32`-Kette ab `0x401000`: C bis `0x4025A9`, F bis
   `0x40259F`. Aufgefallen ist es, weil `0x4AE4C0` mit einem Fenster von 1047
   Einträgen in F **0 Rufer** ergab und mit voller Kette **10** — genau der
   Fehlermodus, vor dem die Regel warnt, **nur mit der falschen Zahl.**
   **Wer die 1047 übernimmt, verliert in C 62 und in F 60 Weichen.**
2. ⚠ **Die richtige Vollerhebung für Rufer ist die BYTESUCHE, nicht die
   Zerlegung.** Ein Ruferzähler über einen zerlegten `.text`-Index verliert
   Stellen. Der Zähler, der jedes `0xE8` im Rohbild prüft und das Ziel gegen
   `{Funktion} ∪ {alle E9-Weichen darauf}` hält, liefert in C und F **dieselben
   Zahlen für jede** der elf Revierfunktionen.

### BC.9 ⚠ Was offen blieb, und wodurch das Verfahren blind ist

* ⚠ **»Wie kommt ein Sprite mit Durchsichtigkeit auf den Schirm« wurde NICHT
  bearbeitet** — der Weg führte über den Geländemaler `0x42C8C0` weiter ins
  Revier des Fensterlaufs. **Dort wurde aufgehört.** Gesichert ist nur: der
  Geländemaler dunkelt über `.CWS` ab und holt Höhen über `0x4B62B0`.
* ⚠ **Der `.PAL`-Kopf `0xB123`** ist konstant, also keine Prüfsumme — aber *was*
  er ist, sagt der Code nicht: er liest die 8 Byte und überschreibt sie sofort.
* ⚠ **`peFlags`** wird von `Check_pal` **nie gesetzt** — es bleibt, was `malloc`
  hinterlässt. Ob DirectDraw das stört, ist statisch nicht zu sagen.
* ⚠ **Das Landschaftsbyte `0x6783E0`** hat 3 Lesestellen und **keinen** Schreiber
  in der Relokationstafel. Ob es 0…1 oder weiter läuft, ist offen — und daran
  hängt, ob die Kartenfarbtafel `h = 0…3` mit **zwei** Landschaften oder
  `h = 0…4` mit **einer** beschreibt. **Beide Lesarten erklären die vier leeren
  Sätze gleich gut**, sie liessen sich nicht auseinanderhalten.
* ⚠ `.CWG` als »unbenutzt« stützt sich auf die Relokationstafel; **berechnete
  Basen sieht sie nicht.** Das Fenster `+0…+0x100` wurde abgesucht, ein
  Blockbefehl mit dieser Basis kommt nicht vor. Das fehlende `90.CWG` stützt den
  Befund unabhängig.
* ⚠ `0x4B7AC0` (Kartenfenster-Hilfsmaler) und `0x4B4B90` (vermutlich
  Kamerazentrierung auf ein gewähltes Flugzeug, Satzlänge 68 = sec19) sind
  **eingeordnet, nicht ausgedeutet.**
* ⚠ **Grössenunterschied C 5280 / F 5296 beim Geländemaler** und **40 gegen 48
  Fälle in der Tastentafel**: als **Beobachtung** gemeldet, **nicht als Befund** —
  am Kontrollfluss nur teilweise erklärt.

---

## BD. ⭐⭐ DIE ZEICHENLISTE — und die Berichtigung eines unserer Unterschiede

> **Die zwölf sind keine Zeichnerfamilie, sondern EINSORTIERER in eine
> y-sortierte Zeichenliste** — je einer pro Objektgattung. Sie zeichnen nichts.
> Sie gehen ihre Quelltafel ab, verwerfen alles Unsichtbare, rechnen Kachel- in
> Bildpunktkoordinaten um und hängen einen **10-Byte-Eintrag** in den Korb der
> Kachelzeile. Erst zwei andere Funktionen gehen die Körbe ab und zeichnen.
> **Die Familie hat nicht zwölf, sondern 22 Fälle in 18 Funktionen**, und alle
> hängen an einem einzigen Taktgeber.

### BD.0 Der Rahmen

| Was | C | F |
|---|---|---|
| **Taktgeber** `zeichenliste_aufbauen` | `0x430DC0` | `0x42FF00` |
| Rahmenfunktion (äussere Ausgabeschleife) | `0x4B4150` | `0x4B3A80` |
| **Eintragstafel** | `0xAB8068` | `0xAB70C8` |
| **Zählertafel** (Korbfüllstände) | `0xAB93F0` | `0xAB8450` |
| Ende der Zählertafel | `0xB0EBAC` | `0xB0DC0C` |
| Sichtbarkeitskarte (**sec50**) | `0x678B58` | `0x677BB8` |   ⚠ hier stand »sec49«, berichtigt 22.08.2026 (BG.5)
| eigener Spieler | `0x4FA284` | `0x4F928C` |
| Kamera-Kachel X / Y | `0x5387AC` / `0x5387B0` | `0x5377EC` / `0x5377F0` |
| Feinversatz X / Y | `0x5387B8` / `0x5387BC` | `0x5377F8` / `0x5377FC` |
| Sichtbreite / Sichthöhe in Kacheln | `0x5387C0` / `0x5387C4` | `0x537800` / `0x537804` |

### BD.1 Die 18 Erzeuger — die Reihenfolge IST der Zeichenvorrang

⭐ Wer früher einsortiert, wird früher gezeichnet und liegt **hinten**.

| # | Art | C | F | Quelltafel C | Abschnitt | Satz | Sätze |
|---:|---:|---|---|---|---|---:|---:|
| 1 | 0,1,3,4,5 | `0x4300C0` | `0x42F230` | `0x6E26DC` | **sec5 Einheiten** | 78 | 8000 |
| 2 | *(Sortierer Art 1)* | `0x430AE0` | `0x42FC20` | — | — | — | — |
| 3 | 0x0E | `0x42FAA0` | `0x42EC20` | `0x77CAEA` | sec82 | 8 | 4000 |
| 4 | 0x12 | `0x42F0C0` | `0x42E280` | `0x9C994D` | sec45 | 2 | 3000 |
| 5 | 0x11 | `0x42EE10` | `0x42DFE0` | `0x9C6FBF` | sec41 | 10 | 1000 |
| 6 | 0x1E | `0x42E340` | `0x42D510` | `0x6DDF78` | **sec19 Flugzeuge** | 68 | 200 |
| 7 | 0x08 | `0x42E8D0` | `0x42DA80` | `0x8106C6` | sec42 | 10 | 2000 |
| 8 | 0x09 | `0x42EC50` | `0x42DE20` | `0x884736` | **sec43 Geschosse** | 32 | 1000 |
| 9 | 0x15 | `0x42EAB0` | `0x42DC70` | `0xA51116` | **sec112 Teilchen** | 36 | 1000 |
| 10 | 0x0A | `0x42FCD0` | `0x42EE50` | `0xC06944` | **sec3 Gebäude** | 76 | 255 |
| 11 | 0x14 | `0x42DF40` | `0x42D130` | `0xC2C222` | **sec22 Gleise** | 5 | 3000 |
| 12 | 0x0D | `0x42E100` | `0x42D2E0` | `0xB975B0` | **sec44 Waggons** | ⚠ **−24** | 239 |
| 13 | *(Sortierer Art 0x0D)* | `0x430C50` | `0x42FD90` | — | — | — | — |
| 14 | 0x13 | `0x42FF00` | `0x42F080` | `0x9CB0BC` | sec39 | 13 | 20000 |
| 15 | 0x19 | `0x42F690` | `0x42E830` | `0x677F34` | **sec86 Radare** | 6 | 200 |
| 16 | 0x0F | `0x42F2B0` | `0x42E470` | `0x552E1C` | sec84 | 2 | 1500 |
| 17 | 0x10 | `0x42F4A0` | `0x42E650` | `0x688B5C` | sec85 | 2 | 1500 |
| 18 | 0x0B | `0x42E4D0` | `0x42D6A0` | `0xC03A32` | sec4 | 6 | ≤2000 |
| 19 | 0x0C | `0x42E6D0` | `0x42D890` | `0xBFF3E0` | **sec18 Wald** | 3 | 6000 |
| 20 | 0x28 | `0x42F830` | `0x42E9C0` | `0xB49E57` | sec89 | 32 | 20 |

⭐ **Alle 18 Quelltafeln unterscheiden sich C−F um exakt `0xFA0`** — unabhängig
aus der F-Zerlegung abgelesen, nicht gerechnet. Das bestätigt die Zuordnung.
⚠ Art 0x0D läuft **rückwärts** durch die Tafel.

### BD.2 Der Aufbau der Liste

```
Korb r  (r = 0 … 69):
   0xAB8068 + r·5002 + n·10     Eintrag n   (n = 0 … 499)
   0xAB93F0 + r·5002            u16 Fuellstand des Korbs
```

| Versatz | Breite | Inhalt | Belegt durch |
|---|---|---|---|
| `+0` | u8 | **Art** | 22 Schreibstellen, alle mit fester Zahl |
| `+1` | — | ungenutzt | 0 Relokationstreffer |
| `+2` | u16 | **Satznummer** in der Quelltafel | 53 Treffer |
| `+4` | u8 | Zusatzbyte — **nur Art 0x13** | genau 2 Treffer |
| `+5` | — | ungenutzt | 0 Treffer |
| `+6` | u16 | **Bildpunkt-X** | 47 Treffer |
| `+8` | u16 | **Bildpunkt-Y** | 51 Treffer |

> ⭐ **Nullmodell für 5002:** `0xAB93F0 − 0xAB8068 = 0x1388 = 5000` legt den
> Zähler ans Ende; der Eintragsschritt 10 folgt aus
> `lea eax,[ebp+ebp*4]; lea eax,[ebx+eax*2]`; die Schranke `cmp …, 0x1F3` = 499
> nennt die Kapazität. `500 × 10 + 2 = 5002`. **Von allen Teilerpaaren von 5002
> (`2·2501`, `41·122`, `61·82`) ist keines mit einem 10-Byte-Eintrag verträglich.**
>
> ⭐ **Nullmodell für 70:** der Räumlauf läuft
> `(0xB0EBAC − 0xAB93F0)/0x138A = 350140/5002 = 70` mal, **in F ebenso 70**.
> Gesamtgrösse der Liste: **350 140 Byte**.

**Der Rahmen ist in allen 22 Fällen wörtlich derselbe:**
```
1  Quelltafel laden, Laufindex = 0
2  Aktivpruefung (byte==0 / byte==0xFF / word==0xFFFF — Fall fuer Fall verschieden)
3  Sichtpruefung  byte[ x·256 + y + 0x678B58 ] == 0  ->  verwerfen
4  Kachelkulling gegen KamKachelX/Y und Sichtbreite/-hoehe
5  x_bp = (x − KamKachelX)·0x28 − FeinX          <- Kachel 40 breit
   y_bp = (y − KamKachelY)·0x14 − FeinY          <- Kachel 20 hoch
   feste Bildpunktversaetze abziehen (Fall fuer Fall)
6  Korb r = (y − KamKachelY) + 2                 <- Art 0x1E: +3
7  wenn Fuellstand[r] >= 499  ->  verwerfen
8  ⭐ NUR IN C:  wenn r >= 70  ->  verwerfen
9  Fuellstand[r]++, Eintrag schreiben
```
⭐ `imul …, 0x28` und `imul …, 0x14` kommen in **jedem** der 22 Fälle genau
einmal vor. Damit ist das Kachelmass **40 × 20** unabhängig bestätigt — und zwar
als **einzige** Umrechnung im ganzen Revier.
⭐ **Nullmodell für die Sichtkarte:** `shl ebx, 8` (Spaltenschritt 256) und
sec50-Grösse `0x10000 = 256²` — das einzige quadratische Mass, das aufgeht.
⚠ Hier stand »sec49«; das ist `0xBC0DD0` (9 600 B, die Verlegungsfahrten). Die
**Zahl** 65 536 war richtig, die **Nummer** nicht — berichtigt 22.08.2026, BG.5.

### BD.3 ⭐⭐ Worin sich die 22 Fälle unterscheiden — und worin NICHT

**Drei Achsen tragen:**
1. **Nebelprüfung:** **0 Proben** bei Gebäuden (0x0A) und Gleisen (0x14) — die
   sind dauerhaft sichtbar · **1 Probe** bei allen anderen · **4 Proben** bei 0x28.
2. **Koordinatenbreite:** u8/u8 für alles bis 255 Kacheln; **u16/u16** nur bei
   Gebäuden und Art 0x28.
3. **Fester Bildpunktversatz** — der **Ankerpunkt des Sprites** relativ zur
   Kachel, von 0 bis −0xF0. **Das ist die eigentliche Fallunterscheidung.**

⚠⚠ **Was sich NICHT unterscheidet: Durchsichtigkeit, Spiegelung, Bildrandschnitt,
Farbtiefe, Schattierung, Zeilenschritt — nichts davon kommt in diesen Funktionen
vor.** Die Ausgangsvermutung war falsch: **das alles steckt im Zeichner, nicht im
Einsortierer.**

**Bemerkenswerte Einzelfälle:**
* **0,1,3,4,5** — *eine* Quelle, fünf Arten: verzweigt nach `ukol`
  (`cmp 0x32` / `cmp 0x64`).
* **0x1E** — ⭐ **der einzige Fall mit Besitzerausnahme:** ist `byte[+1]` der
  eigene Spieler, wird die Nebelprüfung übersprungen — **eigene Flugzeuge sieht
  man immer.**
* **0x09** und **0x15** — Flughöhe gedeckelt bei `cmp bx, 0x96` (150).
* **0x28** — vier Nebelproben.
  > ⭐ **Nullmodell:** `0x678F58 − 0x678B58 = 0x400 = 4 × 256` → **x + 4 Spalten**;
  > `0x678B58 − 0x678B4E = 10` → **y − 10 Zeilen**. Beides deckt sich Byte für
  > Byte mit `lea eax,[ebx+4]` und `lea eax,[ebp−0xA]` im selben Code. Das Objekt
  > belegt **4 × 10 Kacheln = 160 × 200 Bildpunkte**, und `sub di, 0xF0` = −240 =
  > −12 × 20 setzt den Anker zwölf Kachelzeilen darüber.

### BD.4 ⚠⚠ DIE BERICHTIGUNG: `cmp al,0x46` ist KEIN Einzelunterschied

Geprüft mit einer **rohen Bytesuche über das ganze `.text`**, unabhängig von
jedem Zerleger, über drei Kodierungsformen:

| | C | F |
|---|---:|---:|
| `cmp al, 0x46` + `jb/jae` | 4 | **0** |
| `cmp r8, 0x46` + `jb/jae` | 17 | **0** |
| `cmp byte[esp+n], 0x46` + `jb/jae` | 1 | **0** |
| **Summe im ganzen Bild** | **22** | **0** |

Alle 22 C-Treffer liegen zwischen `0x42E04A` und `0x43083C` — **genau die 22
Einsortierfälle**: `0x42E04A · 0x42E27B · 0x42E432 · 0x42E604 · 0x42E7D9 ·
0x42E9F1 · 0x42EBB0 · 0x42ED68 · 0x42EFDA · 0x42F1F3 · 0x42F3DF · 0x42F5CF ·
0x42F785 · 0x42F9BF · 0x42FC01 · 0x42FE35 · 0x42FFEB · 0x43023D · 0x43041D ·
0x430578 · 0x4306DA · 0x43083C`.

> ⭐⭐ **Unsere bisherige Notiz »Teilchen-Bildschirmzeilen-Schranke `cmp al,0x46`
> @C `0x42EBB0`« ist zu berichtigen.** Es ist **kein Einzelunterschied** und
> **nichts Teilchen-Spezifisches: die spätere Auslieferung C hat die
> Korbindex-Schranke an ALLEN 22 Stellen der Familie nachgetragen; F hat sie
> nirgends.** Eine systematische Fehlerbehebung zwischen 16.09.1997 und
> 22.01.1998 — **der eine belegte Unterschied im Revier, aber 22-fach.**

Am Kontrollfluss erklärt: in F folgt auf `cmp ax, 0x1F3 / jge` unmittelbar der
Schreibblock; in C stehen zwei Befehle dazwischen. **In F würde ein Korbindex
≥ 70 hinter das Ende der Zählertafel schreiben.**

⭐ **Und die 70 ist damit auch gedeutet:** nicht Bildschirmzeilen, sondern
**70 Zeilenkörbe — ein Korb ist eine Kachelzeile.**
> **Nullmodell:** 70 Körbe × 20 Bildpunkte = **1400 ≥ 1200**, der höchsten Zeile
> der Auflösungstafel. Als *Bildschirm*zeilen gelesen wären 70 bei jeder
> Auflösung sinnlos zu klein — **die Kachelzeilen-Lesart ist die einzige, die
> aufgeht.**

⚠ **Verworfene Scheinunterschiede (Regel 1 in Aktion):** die Besitzerprüfung bei
Art 0x1E sah zuerst nach »C ja, F nein« aus — F prüft dieselbe Sache gegen
`0x4F928C` statt `0x4FA284`, also `.data`-Verschiebung `0xFF8`. **Verworfen.**
Ebenso alle Sichtkarten-, Kamera- und Tafeladressunterschiede (`0xFA0`).
Die Funktionsgrössen weichen um −3 bis +11 Byte ab — **Registerneuzuteilung plus
die 5 Byte der Schranke, keine belastbare Messlatte.**

### BD.5 ⚠ `0x42F830` ist NICHT tot — und warum das Werkzeug es so sah

| Prüfung | C | F |
|---|---|---|
| direkte `call` | **0** | 0 |
| Relokationseinträge auf die Funktion | **0** | 0 |
| Relokationseinträge auf ihren Thunk | **0** | 0 |
| direkte `jmp` | **1** — aus dem Thunk | 1 |
| `jmp` auf den Thunk | **1** — `0x430E35` | 1 — `0x42FF75` |

⭐ **Der Taktgeber endet mit einem Schwanzruf** (`jmp`) — die Funktion ist der
**zwanzigste und letzte Erzeuger**, in beiden Auslieferungen.
⚠⚠ **Die »0 Rufer« waren ein Werkzeugartefakt: `funktionen.py --gross` zählt nur
`call`, nicht `jmp`.**

Und ihr Erzeugnis wird verbraucht: der Hauptzeichner verwirft Art 0x28
(`cmp eax, 0x1E; ja`), aber **`0x42C7E0` / F `0x42B9C0` ist ein eigener Zeichner
nur für Art 0x28**. Kein toter Rest.

### BD.6 ⚠ Über den Einsortierern steht KEINE Sprungtafel

Es ist eine **flache, fest verdrahtete Folge von 20 Aufrufen** im Taktgeber —
daher genau ein Rufer je Funktion. **Die Sprungtafeln liegen darunter, auf der
Verbraucherseite**, und es sind zwei:

| Was | C | F | Grösse |
|---|---|---|---:|
| **Vorlaufzeichner** (Durchgang 1) | `0x42C8C0` | `0x42BAA0` | 5280 / 5296 |
| ↳ Indextafel (30 B) / Zieltafel (13 Zeiger) | `0x42D7FC` / `0x42D7C8` | `0x42C9E0` / `0x42C9AC` | |
| **Hauptzeichner** (Durchgang 2) | `0x429900` | `0x428AF0` | 12000 / 11984 |
| ↳ Indextafel (31 B) / Zieltafel (16 Zeiger) | `0x42BC10` / `0x42BBD0` | `0x42ADFC` / `0x42ADBC` | |
| **Sonderzeichner nur Art 0x28** | `0x42C7E0` | `0x42B9C0` | 224 |

⭐ **Beide Indextafeln sind in C und F byteidentisch** (aufgezählt, nicht
abgetastet):
Haupt `[0,1,1,2,3,4,15,15,5,6,7,8,9,10,11,15,15,15,15,15,12,13,15,15,15,15,15,15,15,15,14]`
Vorlauf `[0,0,12,12,12,12,12,12,1,12,12,12,2,12,3,4,5,6,7,8,9,12,12,12,10,12,12,12,12,11]`

⭐ **Die Deckung ist lückenlos:** jede der 22 erzeugten Arten wird von genau
einem der drei Zeichner behandelt. Die sieben Arten, die der Hauptzeichner
verwirft, sind **exakt** die, die nur der Vorlauf hat.

**Die äussere Schleife** (`0x4B4150`): Durchgang 1 ab Korb **2**
(Zeiger `0xABBB04 = 0xAB93F0 + 2·5002` ✓), Durchgang 2 ab Korb **0**, beide bis
Korb `Sichthöhe + 8`.

⭐ **Zwei Sortierer** hängen zwischen den Erzeugern: `0x430AE0` sammelt in jedem
Korb die Einträge mit **Art 1** und **blasensortiert sie nach Bildpunkt-Y**
(ganze 10-Byte-Einträge werden getauscht); `0x430C50` dasselbe für **Art 0x0D**
(Waggons). **Sonst gilt: Einfügereihenfolge = Zeichenreihenfolge.** Nur diese
zwei Gattungen bekommen eine echte Tiefensortierung innerhalb der Zeile.

### BD.7 Der Weg eines Sprites, bis zur Reviergrenze

```
Rahmen 0x4B4150
  ├─ 0x430DC0  Koerbe raeumen (70x), dann 20 Erzeuger, letzter per jmp
  ├─ 0x4B435E → 0x42C8C0   Durchgang 1, Koerbe 2 … Sichthoehe+8
  ├─ 0x4B43F9 → 0x429900   Durchgang 2, Koerbe 0 … Sichthoehe+8
  └─ 0x4B46EA → 0x42C7E0   Art 0x28
```
Der Zweig für Art 0x00 löst die Satznummer auf: `idiv 1000` trennt Spieler von
Platz, `n·78 + 0x6E26C8` ist der Einheitensatz, und bei `+0x10 == 0x53/0x54`
verzweigt er — ⭐ **das ist genau die bekannte Sonderbehandlung Spiegel/Illusion
bei `0x4299B4`, jetzt in ihrem Zusammenhang.**

| Kopierer (C) | Aufrufe Haupt | Aufrufe Vorlauf | Rolle |
|---|---:|---:|---|
| `0x4AC0A0` | 20 | 1 | **Sprite-Verteiler**: schaltet über `byte[0x504034]` und `byte[0x504038]` (0…3) auf vier Kopierer und addiert `word[0xA3AE88]`/`word[0xA3AE8C]` als Weltversatz |
| `0x4ACCD0` | 20 | 6 | Kopierer |
| `0x4AC5C0` | 14 | 2 | Kopierer (Grundfall) |
| `0x4AC6D0` | 1 | 10 | Kopierer (Vorlauf-Hauptweg) |
| `0x4AC450`, `0x4ACB90`, `0x4AC830`, `0x4AC040`, `0x4AC070` | | | weitere Kopierer |
| `0x4B71F0`, `0x4B5CE0`, `0x4B62B0` | 8 | 3 | Hilfszeichner |

⚠ **Hier endet das Revier.** Alle Aufrufe haben die Form `(bild, x, y)` bzw.
`(bild, x, y, 0)`; der Einstieg ist `0x4AC0A0`, sein Modusschalter
`byte[0x504034]`/`byte[0x504038]`.

⭐ **Ein Hinweis zum Randschnitt:** die Erzeuger schneiden **nur grob in Kacheln**
(±2 bzw. +5…+7 Kacheln Rand) und **lassen negative Bildpunktkoordinaten stehen** —
`x_bp`/`y_bp` werden als **vorzeichenbehaftete** u16 eingetragen (`movsx` beim
Lesen). **Der Feinschnitt muss also im Kopierer sitzen.**

### BD.8 ⚠ Was offen blieb, und wodurch das Verfahren blind ist

1. ⚠ **Sieben Arten haben keinen Namen** (0x08, 0x0B, 0x0E, 0x0F, 0x10, 0x11,
   0x12, 0x13): Tafel, Satzgrösse, Anzahl und Zeichenzweig sind bekannt — **was
   sie darstellen, nicht.**
2. ⚠⚠ **Widerspruch bei `0xB49E50` (sec89).** Die Unterlagen führen die Tafel als
   **Fähraufträge** (20 × 32, »More mer_ships needed«). Die Messung sagt: sie wird
   als **sichtbares Objekt von 4 × 10 Kacheln mit u16-Koordinaten** gezeichnet,
   mit eigenem Zeichner und 15 Bildpunkten Höhenstufung. **Beides kann stimmen
   (ein Auftrag mit Position), muss aber nicht — hier wird sich nicht
   festgelegt.**
3. ⚠ **`byte[0x504034]` / `byte[0x504038]`** — der Modusschalter des
   Sprite-Verteilers, zwei Bytes über vier Kopierwege. **Nicht gelesen.**
4. ⚠ `word[0x7AEC30]` / `word[0x7AEC34]`, die der Einheiten-Erzeuger nebenbei
   schreibt — unklar wozu. (⚠ Beachte: `0x7AEC38` ist der **Wegpuffer sec14** aus
   BB — diese zwei Worte liegen **unmittelbar davor.**)
5. ⚠⚠ **Wie `Sichthöhe` berechnet wird, ist ungelesen** — und daran hängt, ob die
   F-Fassung tatsächlich überlaufen **kann** oder ob die Schranke reine Vorsorge
   war. **Ohne diese Zahl ist »22-fache Fehlerbehebung« eine Beschreibung des
   Codeunterschieds, kein Beweis eines behobenen Absturzes.**
6. ⚠ **Berechnete Sprünge.** `call` und direkte `jmp` wurden aufgezählt und die
   Relokationstafel abgefragt. Ein `jmp eax` taucht in keinem der drei auf.
   »Jede Art hat genau einen Zeichner« gilt für die aufgezählten Wege.
7. ⚠ **Die Feldaussagen »`+1` und `+5` ungenutzt« stützen sich auf
   Relokationstreffer und sind eine UNTERGRENZE der Nutzung.** Im Hauptzeichner
   wurden die Versätze von Hand nachgesehen; für die ungelesenen Zweige lässt es
   sich nicht ausschliessen.
8. ⚠ **Die Grössenvergleiche C/F sind Abstände bis zum nächsten Funktionsanfang,
   nicht Codelängen** — sie enthalten `0xCC`-Polsterung und wurden nur berichtet,
   nicht ausgewertet.
9. ⚠ **Keine Laufzeitmessung.** Ob je ein Korb 499 Einträge erreicht, ob Art 0x28
   im Spiel auftaucht, ob F je überläuft — dazu wird nichts gesagt.

---

## BE. ⭐⭐ DIE EINHEITEN- UND GEBÄUDESIMULATION (21.08.2026)

### BE.0 Adresstafel

| C | F | Byte | Rufer | Was sie ist |
|---|---|---:|---:|---|
| ⭐⭐ `0x40EC70` | `0x40EAA0` | 854 | 7 | **Infanterie-Schuss auslösen** (Sofortwirkung, kein Geschoss) |
| ⭐ `0x434700` | `0x433840` | 2040 | 1 | **Auswahlrahmen einsammeln** |
| `0x43BA30` | `0x43ABA0` | 832 | 1 | **Gebäude-Nebensatz anlegen** (Typ → sec23…sec31) |
| `0x410940` | `0x410770` | 852 / **772** | 2 | **Roboter be-/entladen** (sec48) |
| `0x441810` | `0x440810` | 742 | 1 | ⚠ 8 Bildbausteine fürs Fenster — **UI, keine Simulation** |
| `0x4223A0` | `0x421560` | 633 | 1 | ⚠ alle Karten der Reihe nach laden — **Entwicklerprüflauf** |
| `0x404170` | `0x404150` | 618 | ⚠ siehe BE.5 | **Dialogprozedur** der Mehrspieler-Sitzung |
| `0x439D20` | `0x438E80` | 611 / 616 | 1 | **Gasabsauger** |
| `0x421400` | `0x4205C0` | 595 | 2 | **Lagerstätten-Bauplatzprüfung** |
| `0x433460` | `0x4325A0` | 598 | 1 | **Direktsteuerung** (Befehle 1/2/8) |
| `0x42EE10` | `0x42DFE0` | 547 / 538 | 1 | ⚠ sec41 in die Zeichenliste — **Renderer** (siehe BD) |

⚠ **Drei der elf gehören gar nicht ins Revier.** »Gross und wenig gerufen« ist
kein Hinweis auf Simulation.

**Hilfsfunktionen, unterwegs benannt:**

| C | F | Byte | Rufer | Was |
|---|---|---:|---:|---|
| ⭐ `0x40F0A0` | `0x40EED0` | 1380 | 2 | **`Test shooting unit` / `ready to shoot` / `shoot end`** |
| `0x40EAA0` | `0x40E8D0` | 368 | 3 | Weltkoordinaten einer Einheit, Verteiler über Gattung 0…5 |
| `0x40F9B0` | `0x40F7E0` | 361 | 15 | Richtung von Einheit zu (x,y) |
| `0x4338F0` | `0x432A30` | 274 | 2 | 8er-Richtung aus (dx,dy) |
| `0x40FB80` | `0x40F9B0` | 210 | 1 | `atan2` → Richtungswinkel (x87) |
| ⭐ `0x455320` | `0x453FC0` | 307 | 4 | **`Place laser:`** |
| ⭐ `0x439410` | `0x438570` | 188 | **49** | **`Add gas` / `Error: too many gas`** |
| `0x4396C0` | `0x438820` | 904 | 1 | Gaswolken-Takt |
| `0x4344D0` | `0x433610` | 131 | 7 | Auswahlliste ergänzen |
| `0x421200` | `0x4203C0` | 407 | 4 | Bauplatzprüfung nach Grundriss |
| `0x4C91B0` | `0x4C8D60` | 856 | 3 | **Gebäude erschaffen** (ruft `0x43BA30`) |
| ⭐ `0x406CD0` | `0x406CB0` | **13688 / 13504** | 1 | **`move units`** — der Einheitentakt |
| `0x40C9A0` | `0x40C800` | 4100 / 4063 | **57** | `Zasah` (Treffer) |
| `0x4047E0` | `0x4047C0` | 584 | **111** | Klang/Effekt auslösen |
| `0x4B5CE0` | `0x4B5610` | 1184 | 21 | Höhenversatz für die Bildschirmlage |
| `0x430E60` | `0x42FFA0` | 440 | 10 | Feinversatz innerhalb der Zelle |
| `0x435BD0` | `0x434D10` | 448 | 13 | Zwischenposition aus `kolik` + Richtung |
| `0x4C2190` | `0x4C1C50` | 111 | **116** | **Befehl absenden** |
| `0x403DB0` | `0x403D90` | 29 | 1 | `DialogBoxParamA(…, 130, …, Thunk)` |

**Daten** — ⚠ der `.data`-Abstand ist **nicht** konstant; jede F-Adresse wurde
aus F-Code **gelesen**, nicht gerechnet:

| Was | C | F | Δ | Form |
|---|---|---|---:|---|
| ⭐ sec48 **Umschlagsätze** | `0x77AC50` | `0x779CB0` | `0xFA0` | **400 × 18** |
| ⭐ **Gaswolken** | `0x77CAE8` | `0x77BB48` | `0xFA0` | **4000 × 8** |
| Gaswolkenzähler | `0x8106B8` | `0x80F718` | `0xFA0` | Wort |
| sec38 Lagerstätten | `0x6783E8` | `0x677448` | `0xFA0` | 50 × 14 |
| sec41 | `0x9C6FB8` | `0x9C6018` | `0xFA0` | 1000 × 10 |
| Bauvorschau-Zellen / Anzahl | `0xA32188` / `0x502AD0` | `0xA311E8` / `0x501B10` | `0xFA0` / `0xFC0` | 3 B je Zelle |
| ⭐ angewählte Einheit | `0x4FA0C8` | `0x4F90D0` | `0xFF8` | Wort; `<8000` Einheit, `10000` Gruppe |
| Gruppenliste / -zahl | `0x833098` / `0x4FA278` | `0x8320F8` / `0x4F9280` | | |
| ⭐ **Rahmen-Anker** | `0xA182E0/E4` | `0xA17340/44` | `0xFA0` | genau **2** Schreiber |
| Mausposition | `0x502AA8/AC` | `0x501AE8/EC` | `0xFC0` | 40 Schreiber |
| Warenannahme je Gebäudetyp | `0x4FACD0` | `0x4F9CD8` | | 4 B je Typ |
| ⚠ ungeklärte Tafel | `0x591F00` | `0x590F60` | | Schritt 68 |

### BE.1 ⭐⭐ Die Schussauslösung der Infanterie

`f(word schuetze, word ziel_a, word ziel_b)`:
`ziel_b == 0x7530` (30000) → `ziel_a` ist eine **Einheitennummer**;
`ziel_b < 0x100` → `(ziel_a, ziel_b)` ist eine **Zelle**, das Opfer kommt aus der
imap; sonst nichts.

```
0x40EC90  word[+0x32] != 0  ->  sofort raus          +0x32 = NACHLADEZAEHLER
0x40ECAD  word[+0x32] := byte[+0x3D] + Zufall%3      +0x3D = NACHLADEZEIT
0x40ECC4  byte[+0x47] := 0x0B   (Schussbild) ; byte[+0x11] := 0
0x40ECD2  Verteiler ueber byte[+0x0D] (Waffe) im Bereich 0xBE..0xC7
          Sprungtafel 0x40EFA4, Indexbyte 0x40EFBC = [0,1,2,5,3,5,5,5,5,4]
          Klang: 0xBE->6, 0xBF->0x12, 0xC0->0x50, 0xC2->0x12, 0xC7->8
          byte[+0x02] := Richtung zum Ziel
0xBF oder 0xC2  ->  LASERSTRAHL (0x455320 »Place laser:«), Streuung
                    Zufall%20 / Zufall%15
sonst           ->  0x40C9A0 (Zasah), der gewoehnliche Treffer
```

⭐ Die Sprungtafel und **alle fünf Klangnummern sind in C und F Byte für Byte
gleich**.

**Rufer:** genau 7, alle in `0x40F0A0` (`Test shooting unit`), jeder unmittelbar
vor einer `shoot end`-Marke. Das Tor dort ist `0x40F0CF`: `cmp word[+0x32], 0`.
⭐ **Damit ist die Kette geschlossen:** Tor `+0x32 == 0` → Schuss →
`+0x32 := +0x3D + Zufall%3` → Abzählen in `move units` bei `0x4074F4`.

**Gemessen an 3154 lebenden Einheiten aus 36 Dateien:**

| Behauptung | Messung | Nullmodell |
|---|---|---|
| Waffen `0xBE…0xC7` sind **Infanteriewaffen** | alle mit `+0x0D >= 0xBE` haben Gattung 1: **879/879 = 100 %** | Gattung 1 ist **29,2 %** aller Einheiten → 257 erwartet |
| `+0x0B` ist der Waffenuntertyp | `+0x0D = 0xBE + (+0x0B)/2`: **879/879 = 100 %** | jede andere affine Zuordnung: **0/879** |
| `+0x32` ist ein kurzer Zähler | `== 0` bei **2882/3154 = 91,4 %** | ein beliebiges Wortfeld wäre selten 0 |

⚠ **`+0x3D` ist NICHT aus der Waffe ableitbar:** bei `+0x0B = 0` kommen {4, 5, 20}
vor, bei `+0x0B = 2` die Werte {15, 20}. **Woher die Nachladezeit stammt, ist
offen.**

⭐ **Praktische Folge: das ist der Hebel fürs Infanteriegefecht.** Schaden sitzt
in `Zasah`, **Feuerrate und Klang sitzen hier** — in zwei Bytes je Einheit und
einer 10-Einträge-Tafel.

### BE.2 ⭐ Der Auswahlrahmen

Liest den Anker aus **`0xA182E0/E4`** (wo die Taste gedrückt wurde — genau **2
Schreibstellen**, beide in der Fensterprozedur) und `0x502AA8/AC` (aktuelle
Maus). Rufer `0x41415F`, beim Loslassen, wenn `dword[0x502AD4] == 4` und
`dword[0x502ACC] == 0` (kein Bau-Modus).
⭐ Dass es der **Rahmen** und nicht der Einzelklick ist, ist belegt: bei einem
Klick wäre x1 == x2, und die Vergleiche sind **strikt**.

Schleife über die **1000 Sätze des eigenen Spielers**. Filter: `+0x09 < 2`,
`UKOL < 45`, `+0x0A <= 5`. Sprungtafel `0x434EE0`, 6 Einträge —
⚠ **Gattung 2 lässt sich per Rahmen nie anwählen.**

⭐ **`byte[+0x0E] == 0x47` (Fahrgestell 71) wird zurückgestellt:** solche
Einheiten wandern in eine Nebenliste und werden **nur dann** angewählt, wenn im
Rahmen **keine andere** Einheit lag. **Ein Rahmen bevorzugt Kampfeinheiten vor
Transportrobotern.**

> **Zahl:** Fahrgestell 0x47 kommt 136 mal vor; **125 (91,9 %) tragen `+0x40 != 0`**,
> also einen sec48-Umschlagsatz. Umgekehrt sind von 157 Einheiten mit
> `+0x40 != 0` **125 (79,6 %)** Fahrgestell 0x47.
> **Nullmodell:** 0x47 ist 4,3 % aller Einheiten → bei Unabhängigkeit wären ~7
> der 157 erwartet. ⭐ **Fahrgestell 71 = der Transport-/Umschlagroboter**, und
> die Auswahlregel ist genau deshalb da.

Zweite Schleife über **sec19** (200 × 68): Auswahlnummer **`20000 + i`**.
Mehr als eine Auswahl → `word[0x4FA0C8] := 0x2710` (10000).

### BE.3 ⭐ Der Gebäude-Nebensatz — neun Tafeln, alle mit 50 Plätzen

`f(byte typ, word gebaeudenummer)` → Platznummer oder `0xFF`. Einziger Rufer:
`0x4C93A5` in »Gebäude erschaffen«. Sprungtafel `0x43BD34`, 15 Einträge.

| Typ | Tafel C | Abschnitt | Satz | Plätze |
|---:|---|---|---:|---:|
| 1 Basis | `0x878E58` | sec23 (800 B) | 16 | 50 |
| 2/3/4 Fabriken | `0x87A2C0` | sec24 (700 B) | 14 | 50 |
| 5 Depot | `0x879F38` | sec25 (700 B) | 14 | 50 |
| 6/12 Bahnstation, Feldbahnhof | `0x879178` | sec30 (700 B) | 14 | 50 |
| 7 Generator | `0x87A5A8` | sec26 (200 B) | 4 | 50 |
| **8 Radar** | — | **keine** | — | — |
| 9 Flughafen | `0x879438` | sec27 (2600 B) | 52 | 50 |
| 10/15 Minen | `0x878AD0` | sec28 (900 B) | 18 | 50 |
| 11 Seedock | `0x87A1F8` | sec29 (200 B) | 4 | 50 |
| 13 Kraftwerk | `0x879E70` | sec31 (200 B) | 4 | 50 |
| **14 Nachschub** | — | **keine** | — | — |

> ⭐ **Nullmodell:** Satzlänge × 50 trifft die Abschnittsgrösse bei **9 von 9**
> Tafeln exakt. Eine geratene Satzlänge zwischen 2 und 64 teilt 700 nur in etwa
> 11 % der Fälle; **neunmal hintereinander ≈ 10⁻⁹**.

⭐⭐ **Die Andocklisten sind damit aufgeklärt:** sec23 (Basis, 16 B) hat
`word[+0x00]` Gebäudenummer, `byte[+0x02] = 0`, dann **`+0x04…+0x0F` = 12 Byte
`0xFF` = sechs Plätze**; sec25 und sec30 (14 B) ebenso sechs Plätze.
**Das ist die Datenstruktur zum bekannten Fehler »andocken zählt bis 6, ablegen
bis 5« (AX.7).**

⭐ Die Mine **nullt beim Anlegen auch das Teilelager des Gebäudes selbst**
(`word[0xC0693C/3E/40/42 + 76·i] = 0`) — das bestätigt die Felder
`+0x2C/+0x2E/+0x30/+0x32` aus AX.2 **unabhängig**.

### BE.4 Die übrigen fünf

**`0x410940` — Roboter be- und entladen.** Marke am ersten Rufer: **`trans A`**.
`word[+0x40]` = `trans`, Satz `0x77AC50 + 18·trans` → **sec48 = 400 × 18**
(`0x1C20` = 7200 = 400·18 ✓). Satz: `+0x00…+0x03` vier Warenarten (`0xFF` =
keine), `+0x04` Gebäudenummer, `+0x05…+0x08` Stückzahlen, `+0x09` Ladung,
`+0x0A` Fassungsvermögen, `+0x0C` Einheitennummer, `+0x0E` Merker, `+0x10`
Umlaufzeiger 0…3. `+0x04 == gebaeude` → **abladen** ins Gebäudelager, sonst
**aufladen** bis `+0x09 == +0x0A`.

**`0x439D20` — der Gasabsauger.** Gerufen aus `move units`, **nur wenn
`byte[+0x0E] == 0x42`** (Fahrgestell 66). Tor: es gibt Gaswolken **und**
`(Takt + Einheit) % 4 == 0` → **die Einheit rechnet nur jeden 4. Takt**. Wolken
näher als ±60 Welteinheiten werden **gelöscht**; die übrigen bekommen
`byte[+0x06] := 12000/dx`, `byte[+0x07] := 12000/dy` — **die Wolke wird
hingezogen**. Weltmassstab: **120 Welteinheiten je Zelle**.

⚠ **`+0x40` wird hier als 0/1-Merker benutzt — dasselbe Feld, das in `0x410940`
der sec48-Index ist und in `0x441810` als Bildnummer gelesen wird.** Zahl: von
den 8 Einheiten mit Fahrgestell 0x42 hat **keine** `+0x40 != 0` (0/8) — der
Konflikt tritt in den ausgelieferten Karten nicht auf, **ist aber im Code
angelegt**.

**`0x421400` — Lagerstätten-Bauplatzprüfung.** Rückgabe = **Lagerstättennummer +
1**. Sucht in sec38 (50 × 14) einen Eintrag im 3×3-Fenster, stempelt die imap an
der eigenen 2×2-Fläche vorübergehend frei, prüft **5 Spalten × 6 Zeilen**.
⭐ Damit ist geklärt, **woher der Aufrufer die Lagerstättennummer erfährt**:
aus dem Rückgabewert.
⚠ **Negativbefund:** die Lagerstättentafel ist in **35 von 36** Prüfdateien
vollständig leer; nur `8.DM` trägt einen Eintrag. **Lagerstätten entstehen zur
Laufzeit, nicht beim Laden.**

**`0x433460` — Direktsteuerung.** Sendet **Befehl 2** bei Richtungsänderung per
Maus, **Befehl 1** bei Pfeiltaste, **Befehl 8** für sec19-Objekte.
> **Zahl: 3/3.** Alle drei Absendestellen stehen wortgleich in der vorhandenen
> `COMMAND_SENDERS.txt`. **Nullmodell:** die Datei nennt 94 Befehlsnummern an 140
> Stellen — dass drei geratene Stellen exakt treffen, ist ausgeschlossen.

**`0x4223A0` — Entwicklerprüflauf.** Baut `1.cwm` … `99.cwm`, dann
`net1.cwm` … `net99.cwm`, prüft jeden auf Vorhandensein und lädt ihn.
⭐ **Damit ist der vollständige Namensraum der Karten festgeschrieben: 1…99 und
net1…net99.**

### BE.5 ⚠⚠ »0 Rufer« war ein Werkzeugfehler — und er hätte die Fensterprozedur für tot erklärt

| Prüfung | C `0x404170` | F `0x404150` |
|---|---|---|
| direkte `call`-Ziele | 0 | 0 |
| Relokationseinträge mit der **Funktionsadresse** | 0 | 0 |
| die vier Adressbytes irgendwo in der Datei | **0** | **0** |

Nach dieser Tafel wäre sie unerreichbar. **Sie ist es nicht.** Der Weg führt über
den **Thunk**: C `0x401541` → `0x404170`, und ein Relokationseintrag bei
`0x403DB9` trägt **die Thunk-Adresse**. Die Stelle ist
`DialogBoxParamA(hInst=0, Vorlage=130, Eltern, Prozedur=Thunk, 0)`, gerufen aus
**`CWorms Player`**. `0x404170` ist eine `stdcall`-Prozedur mit `ret 0x10`, die
auf `WM_INITDIALOG` und `WM_COMMAND` verzweigt — **die Dialogprozedur der
Mehrspieler-Sitzungsauswahl.**

⚠⚠ **Das ist die Thunk-Regel in neuer Gestalt.** Sie sagt »Thunks aufdröseln,
sonst misst man 0 Rufer«. Sie gilt genauso für die **Adressübergabe**: der
Thunkblock ist ein lückenloses Feld aus `E9`-Sprüngen **ohne `0xCC`-Polsterung**,
also findet ihn keine Polsterungssuche; und wer nach der Adresse der *Funktion*
sucht statt nach der des *Thunks*, findet nichts.

**Die Volkszählung, korrekt gerechnet** (Thunkfeld vollständig aufgezählt:
**C 1109, F 1107**; unerreichbar = weder gerufen, noch Thunk gerufen, noch
Funktion oder Thunk in der Relokationstafel):

| | Spielfunktionen (< `0x4D6000`, ohne Thunks) | unerreichbar |
|---|---:|---:|
| C | 1107 | **59 = 5,3 %** |
| F | 1107 | **59 = 5,3 %** |

⚠ Vorher waren es 66 in C. Die sieben Differenzfunktionen sind genau die, deren
Adresse über einen Thunk genommen wird — **darunter `0x412E30`, die
Fensterprozedur**, die ganz sicher läuft. **Wer die Thunks nicht aufzählt,
erklärt die Fensterprozedur für tot.**

### BE.6 ⭐ Der Einheitentakt, in seiner Reihenfolge

`move units` = `0x406CD0`, **13688 Byte, ein Rufer, 111 eigene Protokollmarken**.

```
je Satz  edi = 0x6E26C8 + 78·i,  i = 0 … 7999
  byte[+0x09] == 0xFF  ->  naechster Satz
  "move unit: <i>" · "rnd X1" · "step: <Takt>"
  UKOL entscheidet:
     == 0x32 (50)   Tuerzelle: sec20 muss 0x63 sein, imap-Nachbar < 8000,
                    Spieler aktiv — sonst STIRBT die Einheit
     == 0x33 (51)   im Gebaeude: byte[+0x15] ist die GEBAEUDENUMMER;
                    Besitzer und Typ muessen stimmen — sonst STIRBT die Einheit
     == 0x64 (100)  eigener Zweig
  "likvid typ:"  Verschrottung
  Nachladezaehler: Gattung != 1 && +0x39==0 && +0x0D!=0 -> word[+0x32] := 10
                   sonst  word[+0x32] > 0 -> dec      <-- hier laeuft die Infanterie ab
  byte[+0x0E] == 0x42  ->  Gasabsauger
  "move kolik:" · "on square" · "no fuel" · "move L".."move Q"  (Bewegung)
  ->  "Test shooting unit"  ->  SCHUSS
  "attack A".."attack G" · "stay A|B" · "check transporter"
  "MINE A|B|C"  ->  Lagerstaettenpruefung
  "trans A"  ->  BE-/ENTLADEN     ·  "wait A|B|C"  ->  BE-/ENTLADEN (zweiter Weg)
"move units end"
```

⭐ **Die Reihenfolge:** Lebendprüfung → UKOL-Sonderfälle (mit **Todesfolge**) →
Nachladezähler → Gas → Bewegung → **Schuss** → Standverhalten → Bau/Mine →
Umschlag. **Der Schuss kommt NACH der Bewegung, das Nachladen davor — eine
Einheit, die in diesem Takt fährt, kann im selben Takt schiessen.**

⭐ Nebenbefund: **`byte[+0x15]` ist die Gebäudenummer, in der die Einheit
steckt.** ⚠ Da es ein Byte ist, **können nur die Gebäude 0…255 von 300 Einheiten
aufnehmen.**

### BE.7 ⚠⚠ BERICHTIGUNG AN `GAMESTATE_RE.md` §2: sec5 hat KEIN Besitzerfeld

Dort steht `+0x02 u8 owner (player 0–7)` und `+0x03 team/owner-dup`. **Das ist
falsch.** Der Besitzer steckt **allein in der Satznummer** (`nummer / 1000`).

**Gemessen über 3154 lebende Einheiten — alle 78 Feldversätze geprüft, ob sie
`nummer/1000` sind:**

| Feld | Treffer |
|---|---|
| `+0x15` (bester) | 1303/3154 = **41,3 %** |
| `+0x2F` | 1247/3154 = 39,5 % |
| **`+0x02`** | **638/3154 = 20,2 %** |
| **`+0x03`** | **660/3154 = 20,9 %** |

> **Nullmodell: ein echtes Besitzerfeld ergäbe 3154/3154 = 100 %.** Kein einziges
> Feld kommt in die Nähe. Die 41 % bei `+0x15` erklären sich, weil
> Blickrichtungen mit der Startseite korrelieren — Zufall wären 12,5 %.

⭐ **Was `+0x02` und `+0x03` stattdessen sind: Richtungsfelder.**
24 Schreibstellen für `+0x02`; geschriebene Sofortwerte sind `0`, `7`, `0x0F` —
**nie ein Spielerindex**. Sechs davon schreiben unmittelbar den Rückgabewert
einer Richtungsroutine. `+0x03` wird gegen die 8er-Richtung aus einem Mausdelta
verglichen — es ist die **Sollrichtung**.
Werteverteilung `+0x02`: **0…7 tragen 3132 von 3154 (99,3 %)**, 8…15 die
restlichen 22 — eine 16er-Richtung, von der Bodeneinheiten fast nur die geraden
acht benutzen.

**Vier Felder bekommen einen Namen:**
`+0x32` (Wort) = **Nachladezähler**, 0 = feuerbereit ·
`+0x3D` (Byte) = **Nachladezeit** in Takten ·
`+0x47` = **Bildphase** (beim Schuss `0x0B`) ·
`+0x0B` = **Waffenuntertyp der Infanterie** (`+0x0D = 0xBE + (+0x0B)/2`, 879/879).

### BE.8 ⭐ Ein ELFTER Auslieferungsunterschied — und eine Bestätigung des zehnten

Alle elf Paare wurden **befehlsweise** verglichen (Mnemonik + Operanden, Adressen
entfernt; zusätzlich **registerblind**).

⚠ **Verworfen: 7 Fundstellen**, alle **Sprungtafeln hinter einem `ret`**
(`0x40EFA4`, `0x43BD34`, `0x441A60`, `0x441AAC`, `0x434EE0`, `0x410C84`,
`0x40EBF8`). Aufgezählt statt abgetastet stimmen sie **restlos** überein.
⚠ Ebenfalls verworfen: `0x434700` (53 Blöcke) und `0x439D20` (7 Blöcke) — nach
registerblindem Vergleich bleibt nur Registerzuteilung und umgedrehte
Vergleiche. Vier weitere sind **byteweise identisch**.

**⭐ Unterschied A — bestätigt den 22-fachen aus BD.4.** In `0x42EE10` hat C
`cmp cl, 0x46 / jae`, F nicht — genau eine der 22 Stellen. **Zwei unabhängige
Läufe, derselbe Befund.**
> **Nullmodell:** die Zeichenliste hat in **beiden** Auslieferungen genau **70**
> Zeilen, unabhängig aus der Nullungsschleife hergeleitet (`0x557BC / 0x138A = 70`
> glatt, in C und F). Dass ein beliebiger Vergleichswert ausgerechnet diese Zahl
> trifft, hat 1/256.

**⭐⭐ Unterschied B — NEU, der elfte: `0x410940` hat in C drei Wächter, in F
keinen** (+80 Byte):
```
C 0x41096E  cmp word[sec48+0x0C], dx   ; traegt der Umschlagsatz WIRKLICH diese Einheit?
C 0x410994  cmp byte[sec48+0x0E], 0    ; Satz gueltig?
C 0x4109A2  +0x00..+0x03 alle 0xFF ?   ; ueberhaupt eine Ware bestellt?
```
In F fehlen **alle drei**; die Funktion geht direkt in den Umschlag. Zusätzlich
zieht C das Kopieren `word[+0x30] → word[+0x2E]` **hinter** die Wächter, während
F es unbedingt macht.
⭐ **Gegenprobe:** die beiden Rufstellen sind in C und F **befehlsgleich** — kein
ausgleichender Test auf der Aufruferseite. **Der Unterschied ist echt und nicht
verlagert.** Beides sind Härtungen in Richtung der **späteren** Fassung.

### BE.9 ⚠ Was offen blieb, und wodurch das Verfahren blind ist

1. ⚠ **Woher `+0x3D` (die Nachladezeit) kommt** — nicht aus Waffe oder Untertyp
   ableitbar. Vermutlich eine Entwurfs- oder Forschungstafel, nicht gesucht.
2. ⚠ **Die Tafel `0x591F00`** (Schritt 68), die `0x433460` für Befehl 8 abfragt:
   **12 Lesestellen, 0 Relokations-Schreiber** — sie wird über einen Zeiger
   gefüllt, und genau das findet die Relokationstafel nicht. `--block` lief nicht.
3. ⚠ `byte[0x4FACD0]` (Warenannahme je Gebäudetyp) wurde **benutzt, nicht
   ausgelesen**.
4. ⚠ **Was sec41 (1000 × 10 B) enthält** — bekannt ist nur, dass es als
   Elementart `0x11` gezeichnet wird und an der Sichtkarte hängt.
5. ⚠ **`+0x40` ist DREIFACH belegt** (sec48-Index, Gas-Merker, Bildnummer).
   Welche Regel auswählt, ist nicht gefunden; in den Prüfdaten tritt der Konflikt
   nicht auf (0/8) — **das beweist aber nichts über den Code.**
6. ⚠ **Berechnete Sprungziele** bleiben unsichtbar. Die 59 unerreichbaren
   Funktionen sind »unerreichbar, **soweit aufzählbar**«.
7. ⚠⚠ **Eine byteweise C/F-Maske allein taugt nicht für Regel 1.** Die erste
   Maske meldete für `0x434700` **712** abweichende Bytes; der befehlsweise,
   registerblinde Vergleich zeigte, dass **keiner davon Bedeutung trägt**.
8. ⚠ **Ein einziger Rufer heisst nicht »unwichtig«.** `move units` (13 688 B) und
   der Zeichenlisten-Taktgeber (122 B) haben je **einen** Rufer und sind das Herz
   des Spiels. **Die Rufer-Spalte hat bei `0x434700` und `0x43BA30` fast in die
   Irre geführt.**
9. ⚠ **Keine Prüfstände.** Alle Zahlen stammen aus dem Abbild und den
   Kartendateien, keine aus einem laufenden Spiel. Für die Taktreihenfolge wäre
   das der nächste ehrliche Schritt.

---

## BF. ⭐⭐ DIE ENZYKLOPÄDIE IST FREISCHALTBAR — und wir haben davon nichts (22.08.2026)

Angesetzt an der **einen** Funktion, die das Spiel selbst benennt und die
nirgends bei uns stand (`funktionen.py --liste`): `0x4557A0`, Marke
**`HELPG.DAT`**. Sie hat einen zweiten Teil mitgebracht, der grösser ist als sie.

### BF.0 Adresstafel

| C | F | Byte | Rufer | Was sie ist |
|---|---|---:|---:|---|
| ⭐ `0x4557A0` | ⭐ `0x454440` | 161 | 1 (`0x415A21` über Thunk `0x401983`) | **HELPG.DAT + ENCYCLOG.DAT laden**, beim Programmstart |
| ⭐⭐ `0x455870` | ⭐ `0x454510` | 787 | 3 (`0x44BAFB`, `0x44CD89`, `0x44DBC4`, alle über Thunk `0x40173A`) | **die Freischalttafel der Enzyklopädie füllen** |
| `0x44C51B` | — | — | — | Enzyklopädie **eine Seite zurück** |
| `0x44C5A0` | — | — | — | Enzyklopädie **eine Seite vor** |

⭐ **Nachgetragen am 22.08.2026: die zwei F-Adressen stehen, und zwar BELEGT
statt gerechnet.** Beide meldet `aekernel-tools/cfind.py` als **eindeutig** —
der Rumpf ist Befehl für Befehl gleich, und es gibt in F genau **eine** Funktion
dieser Form. Damit ist der Vorbehalt für diesen Abschnitt **aufgehoben**: die
Enzyklopädie schaltet in beiden Auslieferungen gleich frei.
⚠ Der `.text`-Abstand ist hier **`−0x1360`** und hat mit dem `.data`-Abstand
`0xFA0` nichts zu tun. **Wer F-Adressen rechnet, rechnet falsch.**

### BF.1 Der Lader `0x4557A0`

Drei Bibliotheksfunktionen, aus der Ruf-Form eindeutig: `0x4D7040` = `fopen`,
`0x4D73C0` = `fread`, `0x4D6D90` = `fclose`.

```
fopen("HELPG.DAT", "rb+")        -> fread(0x8B62B0, 1, 0xFA0)   ; 4000 B
                                    fclose
fopen("ENCYCLOG.DAT", "rb+")     -> fread(0x8B8070, 1, 0xFA0)   ; A Textversatz
                                    fread(0x991820, 1, 0xFA0)   ; C Bildnummer
                                    fread(0x9927C8, 1, 0xFA0)   ; B Anzahl Bytes
                                    fclose
memset(0x8934F0, 0, 250 dword)   ; 1000 Byte
```

⭐ **Neu daran ist die REIHENFOLGE in der Datei.** Wir haben die drei Tafeln in
AT/AS nach ihrer Adresse als A/B/C benannt; in `ENCYCLOG.DAT` stehen sie als
**A (Textversatz), C (Bildnummer), B (Anzahl Bytes)**. Wer die Datei nach
unserer Buchstabenfolge liest, vertauscht Bildnummer und Länge.

⚠ **Der Modus ist `"rb+"`, geschrieben wird hier aber nichts.** Entweder gibt es
einen Schreiber anderswo, oder das `+` ist folgenlos. Nicht nachgesehen.

### BF.2 ⭐⭐ `0x8934F0` ist die Freischalttafel der Enzyklopädie

**1000 Byte, ein Byte je Enzyklopädieseite** — genau die 1000 Einträge der drei
`ENCYCLOG.DAT`-Tafeln. `0 = gesperrt`, `≠0 = sichtbar`.

Belegt durch die Blätterfunktionen, die als einzige beide ein Muster zeigen:

```
0x44C51B   zurueck:  bx-- ; wenn bx == -1  -> bx = 999
0x44C5A0   vor:      bx++ ; wenn bx == 1000 -> bx = 1
           beide:    solange byte[0x8934F0 + bx] == 0, weiter blaettern
```

⭐ Die `999`/`1000` sind der Beleg für die Länge — sie stehen als Rohzahlen
(`0x3E7`, `0x3E8`) im Code, unabhängig von der Dateigrösse.
⚠ **Und eine Unsymmetrie des Originals:** rückwärts wird auf **999**
umgebrochen, vorwärts auf **1**, nicht auf 0. **Seite 0 ist vorwärts nicht
erreichbar.** Das ist kein Lesefehler, es sind zwei verschiedene Rohzahlen.

Dritter Leser: `0x47DAF6`, in der Aufbereitung einer Seitenliste — dieselbe
Prüfung `== 0 → überspringen`.

### BF.3 ⭐⭐ Woher die Freischaltung kommt: aus den vier Entwurfstafeln

`0x455870(int mit_technik)` baut zuerst eine **83 Byte lange Kennungstafel auf
dem Stapel** auf und schreibt sie dann durch fünf Schleifen nach `0x8934F0`.
Der Spieler ist `byte[0x4FA284]` (der eigene).

| Seiten | Quelltafel | Abschnitt | Schritt | Block je Spieler | Kennungen |
|---:|---|---|---:|---:|---|
| **20…77** | `0x5045A0` | sec46 **Bauteile** | **58** | 200 | `0xA0…0xAF`, `1…0x13`, `0x41…0x4F`, `0x51…0x58` |
| **78…80** | `0x51B020` | sec120 **Flugzeuge** | **48** | 20 | 0, 1, 4 |
| **82…84** | `0x51B020` | sec120 **Flugzeuge** | **48** | 20 | 5, 6, 2 |
| **85…94** | `0x52EDA0` | sec119 **Schiffe** | **42** | 10 | 0,1,2,3,4,5,6,7,9,8 |
| **96…102** | `0x51CE20` | sec47 **Entwürfe** | **46** | 200 | 0x32…0x38 (50…56) |

Genommen wird jeweils **Byte 0 des Satzes** und unverändert nach
`byte[0x8934F0 + seite]` gelegt.

⭐⭐ **Der Beleg, und er ist vierfach.** Die vier Schrittweiten stehen nirgends
als Zahl im Code — sie sind aus den `lea`-Ketten gerechnet
(`29·x·2 = 58`, `16·x·3 = 48`, `21·x·2 = 42`, `(45+1)·x = 46`), **bevor** die
Ladertafel aufgeschlagen wurde. Alle vier treffen den dort verzeichneten
Abschnitt genau: sec46 = 58, sec120 = 48, sec119 = 42, sec47 = 46.
**Nullmodell:** vier freie Schrittweiten träfen zufällig mit rund `4/256` je
Tafel, zusammen etwa `6·10⁻⁸`. Dazu kommen die Blockgrössen (200/20/10/200), die
`92800/58/8 = 200`, `7680/48/8 = 20`, `3360/42/8 = 10`, `73600/46/8 = 200`
ebenfalls alle vier bestätigen.

⭐ **Die zweite Gegenprobe steckt in den Lücken.** Die Kennungstafel trägt an
genau zwei Stellen `0xFF` — und genau diese zwei Seiten, **81 und 95**, lassen
die Schleifen aus. Ein Trennzeichen, das dort steht, wo aufgehört wird, ist
kein Zufall.
⭐ Und die **zehn** Schiffe der Tafel `0x52EDA0` (AC.?, »Zehn Schiffsarten«)
sind hier zehn Seiten — die Zahl kommt aus zwei unabhängigen Richtungen.

**Fest verdrahtet, ohne Quelltafel:**

```
0x8934F1 .. 0x8934FC  := 1     Seiten  1…12 immer sichtbar
0x8934F3, 0x8934F7    := 0     ⚠ Seiten 3 und 7 davon wieder AUSGENOMMEN
0x893586 .. 0x893592  := 1     Seiten 150…162 immer sichtbar
```

**Und der Schalter:** ist das Argument 0, werden die Seiten **20…80 und 82…102**
stumpf auf 0 gesetzt — die ganze Technik bleibt gesperrt.

⭐ **Die drei Rufer sind nachgesehen, und sie stehen 2:1:**

| Rufstelle | Argument | daneben |
|---|---:|---|
| `0x44BAF9` | **0** | nichts — ein blosses `push 0; call` |
| `0x44CD6F` | **1** | `dword[eax + 0x8C3CF0] := 1` |
| `0x44DBAA` | **1** | `dword[eax + 0x8C3CF0] := 1` — **befehlsgleich mit dem vorigen** |

Damit ist der Schalter belegt: **es gibt beide Betriebsarten**, und die zwei
freischaltenden Stellen sind bis aufs Byte dieselbe Vorbereitung. ⚠ **Welcher
Schirm welcher ist, ist damit noch nicht gesagt** — nur, dass die Unterscheidung
existiert und nicht von mir hineingedeutet ist. Ein Anhaltspunkt: `0x44BBAB`
liegt zwischen den Rufern und ist bei uns schon als »der Mensch zahlt« gelesen
(AW), das Revier `0x44B…0x44D` ist also der **Bau- und Kaufschirm**.

### BF.4 Was das für uns heisst

`Scripts/UI/EncyclopediaScreen.cs` zeigt **alle 106 Seiten immer**, blättert
nicht seitenweise, und kennt keine Freischaltung. Das ist kein Fehler, den
jemand meldet — aber es ist ein **Stück Spiel, das wir nicht haben**: im
Original wächst die Enzyklopädie mit der Forschung mit.

⚠ **Nicht sofort baubar.** Unsere Seiten sind aus `ENCYCLOG.TXT` durchnummeriert
(1…106), die Tafel hier ist über 1000 Plätze gestreut. Die Brücke ist Tafel A
(`0x8B8070`, Textversatz in `ENCYCLOG.TXT`): Seite *n* des Originals ist unsere
Seite mit demselben Byteversatz. **Das ist der erste Schritt, wenn es gebaut
wird.**

⚠ **Nebenbefund, eine Berichtigung an uns:** der Klassenkommentar in
`EncyclopediaScreen.cs` sagt »`ENCYCLOG.PIC` ist ungelesen«. Das stimmt seit dem
21.08. nicht mehr (96 Bilder à 60×60, AT); `ContentBuilder` benutzt die Datei
längst für den Technikkasten. **Wieder ein Kommentar, der das Gegenteil des
Standes behauptet** — dritter Fall dieser Art.

### BF.5 Was offen bleibt

1. ⚠ **Welcher der drei Rufer welcher Schirm ist.** Dass es zwei Betriebsarten
   gibt, ist belegt (BF.3); die Zuordnung Schirm ↔ Betriebsart nicht.
2. ⚠ **Byte 0 eines Bauteil-/Entwurfssatzes** wird hier als »verfügbar« benutzt.
   Was es sonst noch bedeutet, ist nicht nachgeschlagen.
3. ⚠ **Die F-Fassung** ist an keiner der vier Adressen gegengelesen.
4. ⚠ **Wer `HELPG.DAT`/`ENCYCLOG.DAT` schreibt** (der Modus `"rb+"` legt nahe,
   dass es jemanden gibt).
5. ⚠ **Seiten 103…149 und 163…999** kommen in keiner Schleife vor. Entweder
   leer, oder es gibt einen sechsten Füller, den ich nicht gesehen habe.

---

## BG. ⭐⭐ DIE ZUGEXPLOSION — und sec44 hat vier Waggonblöcke (22.08.2026)

Angesetzt an der **grössten ungelesenen Funktion** des Programms (`0x4C7990`,
1456 Byte, 4 Rufer). Sie ist `zug_vernichten(byte zug, byte staerke)`.

### BG.0 Adresstafel

| C | F | Byte | Was |
|---|---|---:|---|
| ⭐⭐ `0x4C7990` | ⚠ `0x4C7540` | 1456 | **den Zug vernichten** — Animationen, Trümmer, Ladung, Räumen |
| `0x435A40` | ⭐ `0x434B80` | 171 | **einen sec42-Satz anlegen** (laufende Animation) |
| `0x4AD520` | ⭐ `0x4ACE50` | — | `fly_part` — der Trümmerstreuer (sec112, AL.1) |
| `0x41D0E0` | ⚠ strittig | 28 | `terrain_at` = Höhenbyte der Zelle (schon gelesen, BC) |
| `0x410E60` | ⭐ `0x410C30` | — | **die Einheit entfernen** (schon gelesen) |
| `0x4D6C70` | — | — | MSVC-`rand()` (schon gelesen, über Thunk `0x4010BE`→`0x43B750`) |

⭐ **F nachgetragen am 22.08.2026** mit `aekernel-tools/cfind.py`. Die drei
Helfer sind **eindeutig** — Befehl für Befehl gleich.
⚠⚠ **`0x4C7990` selbst ist es NICHT.** Der beste F-Kandidat `0x4C7540` trifft zu
**99 %**, nicht zu 100. Das ist zu wenig für »gleich« und zu viel für »eine
andere Funktion«. **Der Vorbehalt für diesen Abschnitt bleibt stehen** — er hat
jetzt aber eine Adresse: die zwei Fassungen der Zugvernichtung gehören
nebeneinander gelesen. ⚠ Gegenrede, die zuerst auszuräumen ist: bricht die
Zerlegung im Rest der Funktion ab, weicht der Schwanz zufällig ab, und die 99 %
wären ein Werkzeugartefakt.
⚠ **`0x41D0E0` (`terrain_at`) ist ein eigener Streitfall.** `cfind` meldet drei
gleich geformte F-Kandidaten und wählt `0x41C250`; in Abschnitt **BC** steht
dagegen `0x41C2A0`. **Einer von beiden ist falsch**, und bei einer der
meistgerufenen Funktionen des Spiels ist das keine Kleinigkeit. Offen.

**Rufer** (alle über Thunk `0x401D34`): `0x4B0F6C`, `0x4C73A9`, `0x4C7FB7`.

### BG.1 ⭐⭐ sec44 ist 4 Waggonblöcke × 60 Züge × 24 Byte

Bisher stand bei uns nur »240 Sätze zu 24«. Die Funktion greift **vier** feste
Basen mit demselben Index an:

| Block | Adresse | Abstand |
|---:|---|---:|
| 0 | `0xB95F48` | — |
| 1 | `0xB964E8` | `0x5A0` = **60 × 24** |
| 2 | `0xB96A88` | `0xB40` = **120 × 24** |
| 3 | `0xB97028` | `0x10E0` = **180 × 24** |

⭐ **Also 60 Züge zu je 4 Waggons**, nicht 240 gleichrangige Sätze.
⭐ **Gegenprobe aus einem Rufer:** `0x4C739C` rechnet `byte[esp+0x1C] % 60`,
bevor es hier hereingeht. Die **60** steht dort als Rohzahl (`mov cl, 0x3C`) —
zwei unabhängige Stellen, dieselbe Zahl.

**Felder, die hier gebraucht werden:**

| Versatz | Was |
|---:|---|
| `+0x00` | **Spalte** — und zugleich der Belegtmerker: `== 0` heisst »gibt es nicht« |
| `+0x01` | **Zeile** |
| `+0x12`, `+0x14`, `+0x16` | **drei Ladeplätze**, je ein Wort, `0xFFFF` = leer |

### BG.2 Der Ablauf

```
zug_vernichten(zug, staerke):
  wenn byte[Block0 + 24·zug] == 0 -> raus            (kein Zug)

  1) fuer jeden der 4 Bloecke:
       art  = 510 + rand()%9
       hoehe = terrain_at(spalte, zeile)
       y     = hoehe·15 + 20
       sec42_anlegen(spalte, zeile, 0, y, art)       (0x435A40)

  2) staerke mal:
       fuer jeden der 4 Bloecke:  fly_part(spalte, zeile, 0, y, art=1, mass=8)
       fuer jeden der 4 Bloecke:  fly_part(spalte, zeile, 0, y, art=0, mass=6)

  3) byte[Block0..3 + 24·zug] := 0                   (der Zug ist weg)

  4) fuer i = 0..2:
       n = word[Block0 + 24·zug + 0x12 + 2·i]
       wenn n != 0xFFFF:
         einheit_entfernen(word[0xBC0DD0 + 48·n + 2])   (0x410E60)
         byte[0xBC0DD0 + 48·n] := 0                     (Platz frei)
```

⭐ **Punkt 4 schliesst den Kreis zu Abschnitt R.** `0xBC0DD0` ist **sec49**, 200
Sätze zu 48, `+0x00 == 0` = freier Platz — genau die Tafel der **verlegten
Einheiten**, die wir am 21.08. gebaut haben. Hier steht, was das Original tut,
wenn der Zug unter ihnen zerschossen wird: **die verlegte Einheit stirbt mit.**
⚠ **Das haben wir nicht gebaut.** Unsere Verlegungsfahrt überlebt den Zug — sie
hängt dann an einem Zug, den es nicht mehr gibt. Erste Bauaufgabe aus diesem
Abschnitt.

### BG.3 `0x435A40` — wie eine Animation entsteht

`sec42_anlegen(spalte, zeile, a2, y, art)` schreibt einen 10-Byte-Satz nach
`0x8106C0` (sec42, 2000 × 10 — die laufenden Animationen, AA):

```
+0 Spalte   +1 Zeile   +2 a2   +3 0   +4..5 y   +6..7 art (0xFFFF = frei)   +8 0
```

Drei Eigenheiten des Originals, alle drei mit Rohzahl belegt:

* ⚠ **`art > 999` → die Funktion tut gar nichts** und kehrt zurück (`cmp si,
  0x3E7; jg`). Kein Fehler, keine Meldung.
* ⭐ **`art == 309` wird zu `310 + rand()%6`** — eine Animation, die sich selbst
  auswürfelt. Die einzige Art mit dieser Sonderbehandlung.
* ⭐⭐ **Ist kein Platz frei, nimmt das Original einen ZUFÄLLIGEN** (`rand()%2000`)
  und überschreibt ihn. Nicht den ältesten, nicht den ersten — einen gewürfelten.
  ⚠ Das ist ein Verhalten, das man nie nachbaut, wenn man es nicht liest.

### BG.4 Der Höhenversatz `hoehe·15 + 20`

Dieselbe Formel wie in `0x4528EF`/`0x435BD0` (offene Frage 4 im Abschnitt der
y-Halbierung), hier aber **unverkürzt**: `imul cl` mit `cl = 15`, dann `+20`.
⚠ Das ist ein **dritter** Belegort für die senkrechte Umrechnung und gehört
gegen die beiden anderen gehalten, wenn der Spieler das Bildschirmfoto vom
Geschoss über der Steigung liefert.

### BG.5 ⚠ Eine Berichtigung an uns: sec49 ≠ Sichtbarkeitskarte

Abschnitt **BD** führt `0x678B58` zweimal als »Sichtbarkeitskarte (sec49)«.
Nach der Ladertafel (Abschnitt Y, aus Lader UND Speicherer beider EXE, 0
Abweichungen) ist `0x678B58` = **sec50**, 65 536 Byte. **sec49 ist
`0xBC0DD0`**, 9 600 Byte — die Verlegungsfahrten dieses Abschnitts.
⭐ BDs eigene Begründung verrät den Dreher: sie rechnet mit `0x10000 = 256²`,
und 65 536 ist die Grösse von sec50. **Die Zahl war richtig, die Nummer nicht.**
Unten berichtigt.

### BG.6 Was offen bleibt

1. ⚠ **Die drei Rufer** sind nur an ihrer Rufstelle angesehen, nicht gelesen.
   `0x4C7FB7` setzt vorher `byte[0xA89220 + 214·n + 213] := 0xFF` — nebenbei
   fällt damit ab, dass **sec34 = 80 Sätze zu 214** ist (17 120 / 214 = 80).
   Nicht weiterverfolgt.
2. ⚠ **Was `staerke` steuert** ausser der Zahl der Trümmerrunden — und woher die
   Rufer sie nehmen.
3. ⚠ **Die Bedeutung von `a2 = 0`** im sec42-Satz (Feld `+2`).
4. ⚠ **Kein Klang.** Ein Zug explodiert lautlos, soweit diese Funktion reicht.
   Der Klang müsste beim Rufer sitzen — nicht nachgesehen.
5. ⚠ **F ist nicht gegengelesen.** Wie BF ein Befund unter Vorbehalt.

---

## BH. ⭐⭐ DIE BALKEN ÜBER DEN EINHEITEN — und die Zeichenfläche (22.08.2026)

Angesetzt an `0x4B6F60` (656 Byte, **17 Rufer** — die meistgerufene ungelesene
Funktion). Sie ist der **Balkenzeichner**, und der Weg dorthin hat die
Zeichenfläche des Originals mit aufgedeckt.

### BH.0 Adresstafel

| C | F | Byte | Rufer | Was |
|---|---|---:|---:|---|
| ⭐ `0x4B6F60` | ⚠⚠ `0x4B6890` | 656 | 17 | **einen Balken zeichnen** (Rahmen + Füllung) |
| ⭐ `0x4B71F0` | ⚠ `0x4B6B20` | — | — | **alle Balken einer Einheit** — Verteiler über die Gattung |
| ⭐ `0x4AC000` | ⭐ `0x4AB930` | 42 | — | **die Zeichenfläche setzen** (3 Schreibstellen, sonst nur Leser) |
| `0x4B9400`± | — | — | — | der **Ladebalken** eines Umschlagsatzes (sec48) |

⭐ **F nachgetragen am 22.08.2026** mit `aekernel-tools/cfind.py`. `0x4AC000` ist
**eindeutig** — die Zeichenfläche wird in beiden Bauten gleich gesetzt, und
damit steht der Kniff mit der vorverschobenen Basis für beide.

~~⚠⚠ Die zwei Balkenfunktionen weichen ab — Verdacht auf einen zwölften
Auslieferungsunterschied.~~ **ZURÜCKGEZOGEN am 22.08.2026, noch am selben Tag.**

⭐ **Es gibt ihn nicht.** Abschnitt **BO** hat alle drei Anker von Hand
nebeneinandergelegt. Die Abweichung ist restlos Übersetzerrauschen:
Registerzuteilung, `jle` gegen `jge` mit vertauschten Operanden, `test/jl`
gegen `dec/js` — und der »abweichende Schwanz« ist die **Sprungtafel hinter dem
`ret`**, die gar kein Code ist. Keine andere Konstante, kein zusätzlicher
Wächter. **Der Vorbehalt für diesen Abschnitt fällt.**

⚠⚠ **Und das ist eine Lehre über die Zahl, nicht über die Balken.** Ich habe aus
»89 % bei 127 Befehlen sind rund vierzehn abweichende Befehle« einen Verdacht
gebaut. Die Rechnung stimmte; die **Voraussetzung** stimmte nicht — die
Prozentzahl misst gar keine bedeutungstragenden Unterschiede. Als Gegenbeispiel
notiert: `cfind` meldet für `0x4AF1C0` **74 %**, obwohl C und F **dieselben 23
Befehle** enthalten und nur zwei unabhängige Hälften umsortiert sind.
⭐ **Regel daraus:** unter rund 40 Befehlen ist die Ähnlichkeitszahl aussagelos,
und über 40 sagt sie nur, dass sich etwas lohnt anzusehen — **nie, was es ist.**
Das entscheidet `--diff` und ein Augenpaar.

⭐ **Nebenbefund aus derselben Erhebung: der `.text`-Abstand ist REGIONAL.**
An zwölf Paaren gemessen: `−0x1D0` bei `0x40Exxx`, `−0x230` bei `0x410xxx`,
`−0xEC0` bei `0x43xxxx`, `−0x1360` bei `0x455xxx`, `−0x6D0` bei
`0x4Axxxx…0x4Bxxxx`, `−0x540` bei `0x4C1xxx`, `−0x450` bei `0x4C7xxx…0x4C9xxx`.
**Es gibt keinen einen Abstand.** Damit ist auch begründet, warum die alte
Handarbeit »Basis + 0xFA0« zweimal danebenlag: `0xFA0` ist der `.data`-Abstand
und im `.text` schlicht falsch.

**Zeichenfläche** — die drei Globalen, bisher nirgends bei uns:

| Adresse | Was | Leser |
|---|---|---:|
| `0xA3AE98` | die **echte** Basis | |
| `0xA3AE7C` | **Zeilenschritt** (Breite in Byte) | 105 im Fenster gesamt |
| `0xA3AE80` | **Höhe** in Zeilen | |
| ⭐ `0xA3AE84` | **`Basis − 256·Zeilenschritt`** | |

⭐⭐ **`0xA3AE84` ist eine vorverschobene Basis, und das erklärt einen Kniff.**
Der Zeichner rechnet `(y + 256)·Zeilenschritt + 0xA3AE84` — dadurch darf `y` bis
**−256** laufen, ohne dass irgendwo ein Vorzeichen geprüft wird. Belegt durch
beide Stellen zugleich: `0x4AC01E` legt die Verschiebung an (`shl edx, 8`),
`0x4B6F86` nimmt sie mit `lea ecx, [esi + 0x100]` wieder heraus.
⚠ **Nur drei Schreibstellen, alle in `0x4AC000`** — wer die Fläche wechselt,
geht durch diese eine Tür.

### BH.1 `0x4B6F60(x, y, breite, hoehe, fuellung, wert)`

```
x -= breite/2                        ⭐ der Balken ist auf x ZENTRIERT
rahmenfarbe = (wert / 1000)·4 + 4    (16-Bit-Division, vorzeichenlos)
Rechteck-UMRISS in rahmenfarbe:      obere und untere Zeile, dann linke und
                                     rechte Spalte, jede Zelle einzeln geklippt
breite -= 2
fuellfarbe nach byte[0xA31A88] (1…4, Sprungtafel 0x4B715C)
```

**Die Klippung ist zellenweise, nicht als Rechteckschnitt** — jedes einzelne
Byte prüft `0 ≤ x < Zeilenschritt` und `0 ≤ y < Höhe`. Langsam, aber es kann
nicht danebenschreiben.

⭐ **Betriebsart 1 — die Schwellen des Gesundheitsbalkens:**

| Bedingung | Farbe |
|---|---:|
| `2·fuellung ≥ breite` (≥ 50 %) | **5** |
| `4·fuellung < breite` (< 25 %) | **9** |
| sonst (25…50 %) | **13** |

⭐⭐ **Die Farben sind aufgelöst** (22.08.2026, Abschnitt **BO**). Die richtige
Palette ist `DATA/01.PAL`, geeicht an zwei Vorbefunden (Platz 47 = `#13130F` wie
in Abschnitt A, und 248…253 sind genau das Blau-Band, das `Check_pal` im Ring
vertauscht). Sie ist in **Viererblöcken ab Platz 1** aufgebaut:

| Platz | Farbe | wann |
|---:|---|---|
| **5** | `#67D75F` hellgrün | ≥ 50 % |
| **13** | `#F7FF0F` gelb | 25…50 % |
| **9** | `#FF2B27` rot | < 25 % |

**Grün, gelb, rot in absteigender Gesundheit** — die Reihenfolge, die man
erwartet, aber die Platznummern sagen sie nicht von selbst (13 liegt zwischen 9
und 5, die Bänder nicht).
⭐ Und **BH.2 fällt damit mit**: `4 + 4·spieler` trifft genau den **dunkelsten
Platz jedes Viererblocks**. Spieler 0 blau, 1 grün, 2 rot, 3 gelb, 4 orange,
5 grau, 6 magenta, 7 cyan.

Die **Schwellen** ½ und ¼ sind hart: `shl eax,1` und `shl eax,2` gegen dieselbe
Breite.
⚠ Der `setl/dec/and 4/add 9`-Griff ist eine sprungfreie Auswahl zwischen 9 und
13 — leicht falsch herum zu lesen. Ich habe ihn zweimal gerechnet.

### BH.2 ⭐⭐ Die Rahmenfarbe ist die SPIELERFARBE

`0x4B71F0(word einheit)` ist der Rufer und übergibt als `wert` **die
Einheitennummer selbst**. Und:

* `einheit ≥ 8000` → die Funktion tut nichts (`cmp si, 0x1F40`)
* der Satz liegt bei `0x6E26C8 + 78·einheit` (26·3 = **78**, aus der `lea`-Kette)

⭐ **8000 = 8 Spieler × 1000 Einheiten**, also ist `einheit / 1000` **die
Spielernummer**, und `farbe = 4 + 4·spieler` gibt acht Plätze im Abstand 4.
**Nullmodell:** wäre `wert` irgendetwas anderes, stünde dort eine Division durch
1000 ohne Sinn — und die Schranke 8000 und der Teiler 1000 stehen als zwei
unabhängige Rohzahlen im Code.
⭐ **Gegenprobe von aussen:** die Satzgrösse **78** ist an keiner Stelle dieses
Reviers hingeschrieben; sec94 (50 × 78) und sec98 (20 × 78) nennen sie
unabhängig.

Der Verteiler geht über `byte[+0x0A]` (die Gattung, 0…5) auf die Sprungtafel
`0x4B78BC`; darüber `ja 0x4B78B5` für alles ≥ 6.

**Was der Arm für Gattung 0 übergibt** (`0x4B722F`):

| Argument | Wert |
|---|---|
| x, y | Bildschirmlage **+ 20** |
| breite | `(byte[+0x29] >> 2) + 2` — **HpMax** |
| hoehe | **5** |
| fuellung | `byte[+0x08] >> 2` — **Hp** |
| wert | die Einheitennummer |

⭐ **Das bestätigt `ENTITY_FELDER.md` von einer zweiten Seite.** Dort stehen
`+0x08 = Hp` und `+0x29 = HpMax` aus dem **Aufzeichner** des Originals; hier
kommen sie aus dem **Zeichner**. Zwei unabhängige Quellen, dieselben zwei
Versätze.
⭐ Und die Balkenbreite ist **nicht fest**: sie wächst mit HpMax
(`HpMax/4 + 2` Punkte). Eine zähe Einheit trägt einen längeren Balken.

Der ganze Arm hängt an `byte[0xA31A88] == 1`.

### BH.3 ⭐ `0xA31A88` ist eine Betriebsart, kein blosser Schalter

18 Leser, **4 Schreiber**: `0x412FFC` (aus `al`) und `0x413009` (auf 0) — das ist
die Bedienung; und dann das Paar `0x4B941A` / `0x4B9455`:

```
bl := byte[0xA31A88]          ; alten Wert merken
byte[0xA31A88] := 4           ; Betriebsart 4 erzwingen
balken_zeichnen(...)
byte[0xA31A88] := bl          ; zuruecksetzen
```

⭐ Das Original benutzt die Globale hier als **Parameter**, nicht als
Einstellung. Der Balken daneben zeichnet einen **Umschlagsatz** (sec48,
`0x77AC50`, 400 × 18): Einheitenfeld `+0x40` ist der Satzindex, Balkenlänge aus
Feld `+9`, Breite aus `+10 + 1`, Höhe 5.
⚠ **Wer Betriebsart 1…4 als reine Nutzereinstellung nachbaut, bekommt den
Ladebalken in der falschen Farbe.**

### BH.4 Was offen bleibt

1. ⚠ **Die Betriebsarten 2, 3 und 4** sind nicht gelesen — nur, dass es sie gibt
   und wo die Sprungtafel steht (`0x4B715C`).
2. ⚠ **Die Farbplätze 5 / 9 / 13** sind Zahlen, keine Farben. Gegen die Palette
   halten (BC), dann steht es fest.
3. ⚠ **Die fünf anderen Gattungsarme** (`0x4B78BC`) — nur der für Gattung 0 ist
   gelesen. 17 Rufstellen heisst: mehrere Balken je Einheit.
4. ~~⚠ Wo `byte[0xA31A88]` in der Bedienung sitzt und ob es in `options.cfg`
   steht.~~ ⭐ **BEANTWORTET (BO): es ist die TAB-TASTE.** Der Schreiber
   `0x412FF5` sitzt im Arm der `WM_KEYDOWN`-Sprungtafel für **`VK_TAB` (0x09)**
   und zykelt `0 → 1 → 2 → 3 → 0`. In F identisch (`0x412DC5`, 22 Fundstellen
   deckungsgleich). **Keine `options.cfg`-Einstellung** — die Vermutung war
   falsch.
   ⚠ Zwei Folgen, die vorher niemand sehen konnte: **Betriebsart 4** (der
   Ladebalken) ist über die Tastatur **unerreichbar**, und **Stellung 0** landet
   im Vorgabearm, dessen »Farbe« das niederwertige Byte von
   `Zeilenschritt·(Höhe−1)` ist — bei Zeilenschritt 640 also 0 oder 128.
5. ⚠ **F ist nicht gegengelesen** — wie BF und BG ein Befund unter Vorbehalt.
6. ⚠ **Bei uns gibt es davon nichts Nachprüfbares.** Ob unsere Balken zentriert
   sind, mit HpMax wachsen und bei ½/¼ umschlagen, ist nicht nachgesehen —
   das ist die Bauaufgabe aus diesem Abschnitt.

## BI. Revier 1: 0x403D60 … 0x41BD60

46 Funktionen, 10 848 Byte. Alle 46 gelesen; 13 Mechaniken belegt.

⚠ **Alle 46 werden ausschliesslich über die Thunk-Tafel `0x401000…0x402xxx`
gerufen.** `rufer.py` auf die Funktion selbst liefert genau einen Treffer (den
`jmp`); erst `rufer.py` auf den Thunk zeigt die echten Rufer. Die Thunk-Adressen
stehen in der Tafel unten, damit das nicht noch einmal von Hand gesucht werden
muss.

---

### Adresstafel

F-Adressen mit `cfind.py` gemessen. ⚠ Der Abstand C→F ist **blockweise
verschieden** (`−0x20`, `−0x110`, `−0x1D0`, `−0x230`, `−0x1C0`, `−0xE40`) —
innerhalb eines Blocks aber für **jede** Funktion derselbe. Genau diese
Blockkonstanz ist der Beleg: sechs verschiedene Abstände, und keine Funktion
fällt aus ihrem Block.

| C | F | Byte | Thunk | Rufer (echt) | Was sie ist |
|---|---|---:|---|---|---|
| `0x403D60` | `0x403D40` | 48 | `0x4012E4` | `0x403B60` (»CWorms Player«) | `DialogBoxParamA(0, 129, hwnd[0x540748], 0x403DE0, 0)` |
| `0x403DE0` | `0x403DC0` | 544 | `0x401CFD` | — (als Zeiger übergeben) | **DlgProc der Vorlage 129** — Spielerliste (Steuerelement 1021) |
| `0x404000` | `0x403FE0` | 128 | `0x4019CE` | — (Rückruf) | **`DPENUMSESSIONSCALLBACK`** — Sitzung in die Liste |
| `0x404080` | `0x404060` | 96 | `0x40190B` | — (Rückruf) | **`DPENUMPLAYERSCALLBACK`** — Spieler in die Liste, DPID als ItemData |
| `0x4040E0` | `0x4040C0` | 144 | `0x402342` | — (Rückruf) | zweiter **DlgProc**: `WM_COMMAND` 2/1005/1006 → `EndDialog(−1/1/2)` |
| `0x404780` | `0x404760` | 96 | `0x40119A` | `0x414E20`, `0x497540` ×2 | COM-Objekt `[0x4F5A38]`+`[0x4F5A3C]` freigeben (`vtbl+0x20`, `+0x10`, `Release`) |
| `0x404D10` | — | 1 | `0x401A5F` | — | **reines `ret`** — leerer Haken |
| `0x404D20` | `0x404D00` | 352 | `0x40109B` | `Can_go` (`0x4055D0`) **21×**, `0x4342A0` | ⭐ **»geh mir aus dem Weg«** |
| `0x405460` | `0x405440` | 112 | `0x40147E` | — | `imap_in_richtung(einheit, richtung)` |
| `0x406C20` | `0x406C00` | 80 | `0x40217B` | `move units` **9×** | ⭐ **2×2-Fussabdruck in sec6 setzen** |
| `0x406C70` | `0x406C50` | 96 | `0x401E9C` | `move units` **6×** | ⭐ **4×4-Fussabdruck in sec6 setzen** |
| `0x40AFB0` | `0x40AEA0` | 48 | `0x4024E6` | `0x4C2280` (Hand control) | `einheit[+0x15] (AKCE) := wert` |
| `0x40AFE0` | `0x40AED0` | 144 | `0x401B9A` | `move units` ×3, `0x43CA50` ×2 | ⭐ **Streufahrbefehl in die Nähe** |
| `0x40B270` | `0x40B160` | 112 | `0x40175D` | `Zasah`, Teleport, Tod u. a., 9× | ⭐ **Einheit aus allen Infanteriezellen austragen** |
| `0x40B9A0` | `0x40B890` | 352 | `0x4015FF` | `0x437060` ×2 | ⭐ **Angriffsbefehl (Nr. 9) mit Reichweitenkürzung** |
| `0x40F760` | `0x40F590` | 592 | `0x401EA6` | `0x40DDB0` ×5, `Test shooting unit` ×2 | ⭐ **8-Wege-Richtung Einheit→Einheit, Feinraster 40** |
| `0x410420` | `0x410250` | 240 | `0x4010E1` | `0x4B1840`, `0x4BBAC0`, Hand control | ⭐ **Einheit verlässt das Gebäude** (Erstbelegung) |
| `0x410610` | `0x410440` | 112 | `0x40148D` | `0x4106D0` | grösster Wert über die 4 Ladeplätze eines Umschlagsatzes |
| `0x410680` | `0x4104B0` | 80 | `0x402121` | — | »mehr als **einer** geladen?« |
| `0x4106D0` | `0x410500` | 416 | `0x401D75` | `move units` | ⭐ **Reihum-Wähler des nächsten Ladeplatzes** |
| `0x410D70` | `0x410B40` | 128 | `0x401E2E` | `move units` ×2 | `sqrt((RX−CX)² + (RY−CY)²)` — Luftlinie zum Zielfeld |
| `0x410DF0` | `0x410BC0` | 112 | `0x40219E` | `0x4C69C0` (Zug) ×2, `0x4CECC0` | Zugsatz `0xBC0DD0` (48 B) → `UKOL := 0x38`, `AKCE` aus `0xA8D508` |
| `0x4118C0` | `0x411690` | 272 | `0x40257C` | `move units` ×2 | ⭐ **freie Zelle im Rechteck suchen** (Abladen) |
| `0x411C80` | `0x411A50` | 432 | `0x401861` | `move units` | ⭐ **Umschlag abbrechen, wenn ein Feind in Reichweite ist** |
| `0x4120C0` | `0x411E90` | 288 | `0x40225C` | `Zasah` (`0x40C9A0`) | ⭐⭐ **beide halb belegten Zellen freigeben** — der `POD`-Keller |
| `0x4121E0` | `0x411FB0` | 160 | `0x401794` | `0x4123D0` | »steht auf (x,y) eine **feindliche** Einheit?« |
| `0x412280` | `0x412050` | 336 | `0x4013A7` | `0x40B3C0`, `0x4123D0`, Hand control | ⭐ **Selbstsprengung**, Quellkennung 40020/40250 |
| `0x4123D0` | `0x4121A0` | 176 | `0x40108C` | `move units` | ⭐ **Zünder**: Feind auf einer der 8 Nachbarzellen → sprengen |
| `0x412480` | `0x412250` | 368 | `0x401D57` | `0x40B3C0`, `0x4125F0` | ⭐ **grosse Sprengung**, Kennungen 40010/40030/40100 + Wirkungsbilder |
| `0x412720` | `0x4124F0` | 304 | `0x40160E` | `move units` | ⭐ **Nachschub `+0x39`** an eigene Einheiten im 3×3 |
| `0x412850` | `0x412620` | 304 | `0x401B09` | `move units` | ⭐ **Betankung `+0x2E`** an eigene Einheiten im 3×3 |
| `0x412B20` | `0x4128F0` | 496 | `0x401CA8` | `Main_funct` (`0x415CF0`) | ⭐⭐ **Selbstprüfung der Infanteriezellen** |
| `0x412D10` | `0x412AE0` | 288 | `0x402360` | `0x412F4C` | GDI-`BitBlt` in 16×16-Kacheln (**kein Spielcode**) |
| `0x414DD0` | `0x414C10` | 80 | — (`call`) | `0x412F1E` | **DirectDraw freigeben** (`[0x540770]`, `[0x540730]`) |
| `0x419E10` | `0x419C50` | 128 | `0x401B8B` | `gefecht_starten` (`0x41A150`) | ⭐ **Fahrzeugbaupläne 50…56 nach Technikstufe freischalten** |
| `0x41B0D0` | `0x41A290` | 384 | `0x401B04` | `0x41B310`, Hand control ×3 | ⭐⭐ **Spieler ausschalten** |
| `0x41B6A0` | `0x41A860` | 400 | `0x401E51` | `0x41B920` ×2 | ⭐⭐ **RLE-Packer** |
| `0x41B830` | `0x41A9F0` | 224 | `0x401A5A` | `0x41BA60` | ⭐⭐ **RLE-Entpacker** |
| `0x41B910` | `0x41AAD0` | 16 | `0x402059` | `0x41B920` ×4, `0x41BA60` | `*(u32*)p` lesen |
| `0x41B920` | `0x41AAE0` | 320 | `0x402392` | `0x41BB10` | ⭐⭐ **gepackt schreiben**, Blockgrösse 1000 |
| `0x41BA60` | `0x41AC20` | 176 | `0x40118B` | `0x41BB70` | ⭐⭐ **gepackt lesen** |
| `0x41BB10` | `0x41ACD0` | 96 | `0x40125D` | **Speicherer `0x41D210` 40×** | ⭐⭐ **`fwrite`-Ersatz mit Packung** |
| `0x41BB70` | `0x41AD30` | 96 | `0x4023A6` | **Lader `0x41E070` 40×** | ⭐⭐ **`fread`-Ersatz mit Entpackung** |
| `0x41BBD0` | `0x41AD90` | 144 | `0x40202C` | `0x41BC60`, `0x41BD60` ×2 | ⭐ **Zelle gültig und im selben Sektor?** |
| `0x41BC60` | `0x41AE20` | 256 | `0x401550` | `0x41BD60` ×3 | ⭐ **freie Zelle im Umkreis 10 über die Ringtafel** |
| `0x41BD60` | `0x41AF20` | 1056 | `0x40151E` | `0x41C6C0` | ⭐⭐ **Sektor-Ankertafel `0x541F60` füllen** |

⚠ `cfind.py` meldete für `0x406C70`, `0x410420`, `0x410610`, `0x41BBD0` und
`0x41BD60` »ungenau« (85…98 %). **Alle fünf habe ich C gegen F von Hand
verglichen: die Unterschiede sind ausschliesslich Registerwahl,
Operandenreihenfolge und der `.data`-Versatz. Kein Verhaltensunterschied.**
Siehe Abschnitt 11.

---

### 1. ⭐⭐ Die Packung des Spielstands — vollständig

Sechs Funktionen dieses Reviers sind **ein** Mechanismus.

#### 1.1 Der Packer `0x41B6A0(quelle, ziel, laenge)`

```
zaehle[256] := 0
für i in 0..laenge-1:  zaehle[quelle[i]]++
marke := der Bytewert mit der KLEINSTEN Häufigkeit      ; 0x41B6EB..0x41B701
ziel[4] := marke ;  aus := 5 ;  lauf := 0
für i in 0..laenge-1:
    a := quelle[i]
    wenn a == marke:                       ziel[aus++]:=marke ; ziel[aus++]:=0
    sonst wenn quelle[i+1]==a ∧ lauf<0xFD ∧ i<laenge-4:
        wenn lauf==0:
            wenn quelle[i+2]==a ∧ quelle[i+3]==a: lauf:=1
            sonst:                                ziel[aus++]:=a
        sonst: lauf++
    sonst wenn lauf != 0:
        ziel[aus++]:=marke ; ziel[aus++]:=lauf+1 ; ziel[aus++]:=a ; lauf:=0
    sonst: ziel[aus++]:=a
*(u32*)ziel := aus
```

⭐ **Die Fluchtmarke ist das *seltenste* Byte des Blocks.** Im Code steht
`cmp [zaehle[marke]], [zaehle[i]] / jbe weiter / marke:=i` — striktes Minimum.
Ein Nullmodell »häufigstes Byte« hätte das umgekehrte Sprungzeichen.

⭐ Weitere gemessene Konstanten: **Mindestlauflänge 4** (`quelle[i+1..i+3]`
müssen gleich sein, ehe ein Lauf beginnt), **Höchstlauflänge 254**
(`lauf < 0xFD`, dann `lauf+1`), **Blockkopf 5 Byte** (`u32` Blocklänge +
Markenbyte).

#### 1.2 Der Entpacker `0x41B830(block, ziel) → ausgepackte Länge`

Spiegelbildlich; `marke,0` → ein wörtliches Markenbyte, `marke,n,v` → `n`× `v`.
Die Füllung geht über `rep stosd`/`rep stosb`, hinterlässt also **keine
Relokation** — für Suchen nach Schreibern dieses Puffers ist
`reloc_refs --block` zwingend.

#### 1.3 Der Rahmen

```
0x41B920(quelle, DATEI, gesamt)          ; schreiben
    für abschnitt in 1000er-Schritten:
        n := 0x41B6A0(quelle+getan, puffer, min(1000, gesamt-getan))
        fwrite(puffer, 1, n, DATEI)      ; muss n zurückgeben, sonst 0
0x41BA60(DATEI, ziel, gesamt)            ; lesen
    solange getan < gesamt:
        fread(&L, 4, 1, DATEI)           ; L = Blocklänge inkl. der 4 Byte
        fread(puffer+4, L-4, 1, DATEI)
        getan += 0x41B830(puffer, ziel+getan)
```

⭐ **Blockgrösse 1000 (`0x3E8`)**, Puffer `0x4EC` bzw. `0x4E8` Byte
(= 1000 + Kopf + Reserve für den ungünstigsten Fall).

#### 1.4 Die zwei Torfunktionen — und das eine Schaltbyte

```
0x41BB10(p, groesse, anzahl, DATEI)      ; = fwrite
    wenn byte[0x4F8114]==1 ∧ groesse*anzahl > 100:  0x41B920(p, DATEI, groesse*anzahl)
    sonst                                           fwrite(p, groesse, anzahl, DATEI)
0x41BB70(p, groesse, anzahl, DATEI)      ; = fread   (spiegelbildlich, 0x41BA60)
```

`0x41BB10` hat **40 Rufer, alle im Speicherer `0x41D210`**; `0x41BB70` **40, alle
im Lader `0x41E070`**.

⭐⭐ **`byte[0x4F8114]` hat nach `reloc_refs` GENAU EINEN SCHREIBER:**
`mov byte[0x4F8114], 1` bei `0x41D230`, in den ersten Befehlen des Speicherers.
Es wird nirgends auf 0 gesetzt. Der Lader stellt es aus der Datei wieder her
(`fread(&byte[0x4F8114],1,1,DATEI)` bei `0x41E1B9`, unmittelbar nach einem
Versionsbyte). **Das Original schreibt also immer gepackt; die 0-Seite des
Tores existiert nur für Fremddateien.**

#### 1.5 ⭐⭐⭐ Am Datensatz gemessen

Behauptung: die ausgelieferten `.CWM`/`.DM` sind mit **genau diesem** Verfahren
gepackt, ab Dateiversatz **75**.

| Datei | Blöcke gelesen | grösster ausgepackter Block | ausgepackt gesamt |
|---|---:|---:|---:|
| `01.CWM` | 871 | **1000** | 868 017 |
| `NET02.CWM` | 1071 | **1000** | 1 067 664 |
| `1.DM` | 961 | **1000** | 957 558 |

**Nullmodell:** damit die Kette rein zufällig durchläuft, müsste jedes
Längen-`u32` in `[5, 0x4EC]` liegen — Wahrscheinlichkeit ≈ 3·10⁻⁷ je Block.
Bei 871 Blöcken hintereinander ist das ausgeschlossen. Und der grösste
ausgepackte Block ist **exakt 1000** — genau die Konstante aus `0x41B920`, die
ich *vor* dem Messen aus dem Code gelesen habe (Regel 2).

⚠ `aekernel-tools/cwm_sections.py` **hat den Entpacker bereits**
(`_decode_chunk`, Tor `>0x64`) und stimmt Zeichen für Zeichen mit meiner Lesung
überein. Das ist eine unabhängige Bestätigung — aber **in keinem `.md` des
Projekts steht das Verfahren.** `grep -i "komprim|compress|RLE"` über
`OFFENE_FRAGEN.md`, `GAMESTATE_RE.md` und `CAMPAIGN_RE.md` findet **null**
Treffer. Und den **Packer** hat das Projekt nirgends.

---

### 2. ⭐⭐ Die Sektor-Ankertafel — die offene Frage AU-6 ist beantwortet

`OFFENE_FRAGEN.md` (Abschnitt AU, Punkt 6) sagt:

> ⚠ Die Füllung der Sektor-Ankertafel `0x541F60` ist ungelesen. Der Setzer
> benutzt einen dreistufigen Index, der Leser nur `2·(11·wx+wy)`.

**Der Setzer ist `0x41BD60`, und er ist der einzige.** `reloc_refs --range
0x541F60 484` findet **3 Schreibstellen, alle in `0x41BD60`** (`0x41BFD7`,
`0x41C050`, `0x41C056`) und 19 Lesestellen.

#### 2.1 Der dreistufige Index

```
platz := 2 · (121·klasse + 11·wy + wx)         ; klasse = arg1, wx/wy = 0..10
byte[0x541F60 + platz + 0] := Anker-Spalte
byte[0x541F60 + platz + 1] := Anker-Zeile      ; 0xFF, wenn kein Anker gefunden
```

⭐ **Nullmodell:** der grösste Index ist `klasse=1, wy=10, wx=10` →
`2·241 = 482`, also die Byte 482/483 — **exakt die letzten zwei der 484 Byte des
Abschnitts.** Eine andere Indexformel liesse die Tafel entweder überlaufen oder
Reste frei. Die Tafel ist also **zwei Ebenen zu 121 Sektoren**, gewählt über ein
Klassenbyte; der bekannte Leser bei `0x4BD71E`/`0x4BD730` liest nur `+0` und
`+1`, also **nur Ebene 0**.

⭐ Die Zeilenbreite ist unabhängig bestätigt: `0x41C6C0` liest `+0x16` und
`+0x17` als »ein Sektor weiter unten« — `11 Sektoren × 2 Byte = 22 = 0x16`.

#### 2.2 Wie der Anker gefunden wird

```
0x41BD60(klasse):
  für wy in 0..10:  für wx in 0..10:
      "Y:"+wx auf die Primäroberfläche zeichnen     ; Ladefortschritt, 0x4C7FE0
      stimmen[0..8] := 0                            ; 0x542B00
      für a in 0..8:                                 ; 9 Nachbarversätze 0x4F8118
          (px,py) := 0x41BC60(24·wx + dx[a], 24·wy + dy[a], wx, wy)
          wenn NICHT 0x41BBD0(px, py, wx, wy): weiter mit a
          für b in a+1..8:
              wenn NICHT 0x41BC60(24·wx+dx[b], 24·wy+dy[b], wx, wy) → (qx,qy): weiter
              wenn (px,py) == (qx,qy): stimmen[a]++ ; stimmen[b]++ ; weiter
              wenn NICHT 0x41BBD0(qx, qy, wx, wy): weiter
              wenn 0x4D2580(0x3E7, (qx,qy), (px,py), 0) ∧ word[0xBCA0E0] < 50:
                  stimmen[a]++ ; stimmen[b]++
      Sieger := a mit den meisten Stimmen (a = 0..3, dann 8..4)
      wenn keiner: byte[Tafel] := 0xFF
      sonst: Anker := 0x41BC60(Sieger-Punkt) und (x,y) eintragen
```

⭐ **Ein Sektor ist 24 × 24 Zellen** — `0x41BBD0` teilt die Zellkoordinate durch
`0x18` und vergleicht mit der Sektornummer. Bei höchstens 254 × 254 Zellen
ergibt das genau **11 × 11 = 121 Sektoren** — und 121 ist dieselbe Zahl, die aus
der Tafelgrösse fällt. Zwei unabhängige Wege, dasselbe Ergebnis.

⭐ `word[0xBCA0E0]` (»Schrittzahl des letzten Weges«) `< 0x32` ist die Schranke:
zwei Punkte gelten nur dann als verbunden, wenn die Breitensuche `0x4D2580`
(BFS für 1×1-Rümpfe) sie in **unter 50 Schritten** verbindet. Der Anker ist
damit **der Punkt eines Sektors, von dem aus die meisten Sektoreingänge
kurzwegig erreichbar sind.**

#### 2.3 Die Nachbarn: `0x41BBD0` und `0x41BC60`

```
0x41BBD0(x, y, sx, sy) → bool          ; x,y in ZELLEN
    x > 2  ∧  y > 2  ∧  x < Breite[0x542DC4]−2  ∧  y < Höhe[0x542DF8]−2
    ∧  x/24 == sx  ∧  y/24 == sy
0x41BC60(x, y, &ax, &ay, sx, sy) → bool
    für i in 0 .. word[0x834A94]−1:                  ; Radiusindex[10] = Umkreis 10
        c := (x + ringdx[i], y + ringdy[i])          ; Ringtafel 0x79A008, 2 B je Eintrag
        wenn 0x41BBD0(c, sx, sy) ∧ sec6[c] == 0xFFFE: *ax,*ay := c ; return true
    *ax := 0xFF ; *ay := 0xFF ; return false
```

⭐ Das belegt nebenbei das **Satzformat der Ringtafel `0x79A008`: zwei
vorzeichenbehaftete Byte (dx, dy) je Eintrag** (`lea ecx,[eax*2]`, dann
`movsx …[ecx+0x79A008]` und `…[ecx+0x79A009]`) — und dass `0x834A80 + 2·r` die
**Anzahl der Ringeinträge bis Radius r** ist (`0x834A94` = `r = 10`).

⚠ In F liest `0x41AD90` die Kartengrenzen aus `dword[0x541E24]` /
`dword[0x541E58]` — das sind C `0x542DC4` / `0x542DF8` minus `0xFA0`. Damit ist
auch die F-Seite dieser beiden Globalen belegt.

---

### 3. ⭐⭐ Die Selbstprüfung der Infanteriezellen — und das Satzformat

`0x412B20`, ein Rufer: `Main_funct` (`0x415CF0`).

```
kladde[0..7999] := 0                          ; 0x53DD88, 8000 Byte
für zelle in 0..3999:                         ; 0x7847E8, Schritt 22
    für platz in 0..8:
        id := u16[zelle*22 + 4 + 2*platz]
        wenn id >= 8000: weiter
        wenn einheit[id].faze(+0x09) != 0: weiter
        wenn einheit[id].UKOL(+0x14) >= 45: weiter
        wenn byte[zelle*22 + 0] == 0 ∧ id == 0: weiter
        kladde[id] := 1
        wenn |RX(id) − byte[zelle*22+2]| > 1  ODER  |RY(id) − byte[zelle*22+3]| > 1:
            0x40B270(id)                      ; aus ALLEN Zellen austragen
            einheit[id].faze := 0xFF          ; Platz freigeben = Einheit weg
            kladde[id] := 0
für id in 0..7999:
    f := einheit[id].faze
    wenn 0 < f < 0xFF ∧ Gattung==1 ∧ UKOL != 100:  0x40B270(id) ; faze := 0xFF
    sonst wenn f == 0 ∧ Gattung==1 ∧ UKOL < 45 ∧ kladde[id]==0: faze := 0xFF
```

⭐⭐ **Das Original löscht still jede Infanterie, die nicht in einer
Infanteriezelle in ±1 ihrer eigenen Position eingetragen ist.** Das ist keine
Zusatzprüfung — es ist Teil der Hauptschleife.

⭐ `reloc_refs` auf `0x53DD88` (8000 Byte): **genau 4 Verweise, alle in
`0x412B20`.** Eine bislang unbekannte, rein private Kladde.

#### 3.1 ⭐⭐ Berichtigung: das 22-Byte-Format der Infanteriezelle

`OFFENE_FRAGEN.md` BB.8, Punkt 2, nennt einen ungelösten Widerspruch:

> ⚠ Die Felder `+0`/`+1` im 22-Byte-Satz … `zz_deutung.py` deutet den Satz als
> »col, row, 9 × u16«. **Wäre `+1` die Zeile, ergäbe der Test keinen Sinn.**

**`zz_deutung.py` liegt um zwei Byte daneben.** Das richtige Format:

| Versatz | Länge | Was |
|---|---|---|
| `+0x00` | 1 | Belegtmarke (0 = leer) — `0x412BA9` prüft `!= 0` |
| `+0x01` | 1 | ⚠ eigenes Feld; die Passierbarkeitskarte prüft `== 0` |
| `+0x02` | 1 | **Spalte** — gegen `RX` verglichen, `0x412BB6` |
| `+0x03` | 1 | **Zeile** — gegen `RY` verglichen, `0x412BD9` |
| `+0x04` … `+0x15` | 18 | **9 × u16 Einheitennummer** |

**Beleg, dreifach unabhängig:** `0x412B20` liest die Plätze bei `0x7847EC`
(= Sockel + 4); `0x40B270` liest sie bei `0x7847EC` mit `+2·j`, `j = 0..8`;
`0x433C20` (»Unit missing«) ebenso. **Nullmodell:** 4 + 9·2 = **22** = die
verzeichnete Satzgrösse, punktgenau. Bei der alten Lesung (Koordinaten bei
`+0`/`+1`, Plätze ab `+2`) blieben zwei Byte unerklärt **und** der Test
`byte[+1] == 0` bliebe sinnlos. Mit der neuen Lesung stimmt beides.

→ **`OFFENE_FRAGEN.md` BB.8 Punkt 2 kann gestrichen werden.** Der Test
`byte[Satz+1] == 0` ist gültig; der Nachbau darf sich darauf stützen.

---

### 4. ⭐⭐ Der `POD`-Kellerspeicher und die Zellwerte `0xFFFE − n`

`0x4120C0(einheit)`, ein Rufer: `Zasah` bei `0x40CB28` (früh, vor der
Todesbehandlung).

```
wenn POHYB(+0x04) != 0xFF  ∧  KOLIK(+0x06) > 0:
    sec6[RX, RY] := 0xFFFE − (POD(+0x13) & 3)     ; die verlassene Zelle
    POD >>= 2                                     ; auskellern
    RX += byte[0x4F5AF0 + 4·POHYB]                ; Richtungstafel, dx
    RY += byte[0x4F5AF2 + 4·POHYB]                ;                dy
sec6[RX, RY] := 0xFFFE − (POD & 3)                ; die betretene Zelle
0x410E60(einheit)                                 ; endgültig entfernen
```

⭐⭐ **`POD` (+0x13, im Aufzeichner »pod« = *darunter*) ist ein Zwei-Bit-Keller.**
Jedes Mal, wenn eine Einheit eine Zelle betritt, merkt sie sich zwei Bit
darüber, **was vorher in der Zelle stand**; beim Verlassen schreibt sie
`0xFFFE − diese zwei Bit` zurück. Damit sind die Sonderwerte der
Passierbarkeitskarte als **eine** Grösse erklärt:

| sec6-Wert | Kellerbits | bisherige Deutung |
|---|---|---|
| `0xFFFE` | 0 | frei |
| `0xFFFD` | 1 | von Art 0 der Passierbarkeitskarte als »1« gelesen |
| `0xFFFC` | 2 | in BB.8 Punkt 1 als »erschlossen, nicht gemessen« markiert |
| `0xFFFB` | 3 | — |

⚠ Das ist **kein** Beweis für »`0xFFFC` = Wasser«; es beweist nur, dass die vier
Werte eine **zusammenhängende Zwei-Bit-Grösse** sind, die eine fahrende Einheit
unter sich mitträgt und wieder freigibt. Der offene Punkt BB.8-1 bleibt offen —
aber die Frage ist anders zu stellen: nicht »was heisst `0xFFFC`«, sondern
»welche zwei Bit trägt der Untergrund«.

⭐ Zugleich belegt: **`0x4F5AF0` ist die Richtungstafel mit 8 × (i16, i16)**
(`+0` = dx, `+2` = dy, Schrittweite 4), hier bei `0x412139`/`0x412154`
mit `byte[POHYB·4 + 0x4F5AF0]` bzw. `+0x4F5AF2` gelesen.

---

### 5. ⭐⭐ Der Wertebereich von sec6 — vollständig

Aus `0x412280` und `0x412480`, die beide dieselbe Fallunterscheidung über den
Zellwert machen:

| sec6-Wert | Bedeutung | Beleg |
|---|---|---|
| `0 … 7999` | **Einheit**, Spieler = Wert/1000 | `cmp ax, 0x1F40` |
| `8000 … 9999` | (nie gedeutet) | fällt in `< 0x36B0` |
| `10000 … 13999` | **Infanteriezelle**, Index = Wert − 10000 | `cmp ax, 0x36B0` |
| `60000 … 60299` | **Gebäude**, Platz = Wert − 60000 | `cmp ax,0xEA60` / `cmp ax,0xEB8C` |
| `0xFFFB … 0xFFFE` | frei, mit Untergrund-Zweibit | Abschnitt 4 |

⭐ **Nullmodell:** die Gebäudeschranke `0xEB8C − 0xEA60 = 0x12C = 300` ist
**genau** die Platzzahl von sec3 (»300 × 76«) aus `OFFENE_FRAGEN.md`. Das ist
keine Deutung, sondern eine Übereinstimmung auf die Einheit genau.

⚠⚠ **Möglicher Fehlgriff im Original.** In `0x412280` wird für jeden Zellwert
`< 14000` gelesen:

```
0x412309  cmp ax, 0x36B0 ; jae ende            ; >= 14000 wird übersprungen
0x41230F  ecx := 26·ax
0x412321  cmp byte[ecx + ecx*2 + 0x6E26D2], 3  ; = einheit[ax].Gattung
```

Für `ax` zwischen 10000 und 13999 — also für **Infanteriezellen** — greift das
auf `78·ax + 0x6E26C8`, bis `0x7EC03A`, **weit hinter das Ende des
Einheitensatzes** (8000 × 78 endet bei `0x77A748`). Der Vergleich entscheidet
dann über zufälligen Speicher, ob eine benachbarte Infanteriezelle Schaden
bekommt. ⚠ **Nur gemeldet, nicht gemessen** — ich habe keinen Datensatz, der
zeigt, was dort tatsächlich steht. Die Schwesterfunktion `0x412480` macht es
richtig: sie unterscheidet `< 8000` und `< 14000` getrennt.

---

### 6. ⭐ »Geh mir aus dem Weg« — `0x404D20`

21 der 22 Rufer stehen in `Can_go` (`0x4055D0`). Die Funktion fragt die Einheit,
die im Weg steht, ob sie ausweicht.

```
0x404D20(id, akce) → bool
  wenn id >= 8000:                          ; Infanteriezelle
      wenn zufall()%50 == 13: return 0
      wenn 0x434200(id−10000): return 1
      return 0x4342A0(id−10000, akce)
  e := einheit[id]
  wenn e.l_engine(+0x0F) == 0xAB:  return 0     ; dieser Antrieb weicht nie aus
  wenn zufall()%50 == 13:          return 0     ; ⭐ 2 % blosse Verweigerung
  wenn e.Gattung(+0x0A) > 2:       return 0
  wenn e.POHYB(+0x04) != 0xFF:     return 1     ; fährt schon
  wenn e.OTACIM(+0x16) != 0:       return 1     ; dreht schon
  u := e.UKOL(+0x14)
  wenn u == 4:                     return 0     ; greift an — bleibt stehen
  wenn u == 2 ∧ e.AKCE(+0x15)==0:  return 1
  wenn u != 0:  { wenn u==3: e.UKOL := 0 ; return 0 }
  e.UKOL := 3 ;  e.AKCE := akce ;  return 1     ; ⭐ UKOL 3 = AUSWEICHEN
```

⭐ **`UKOL 3` ist der Ausweichauftrag**, `AKCE` trägt die Richtung. Der
Zufallsschritt `zufall()%50 == 13` (`0x4C5B30`, der deterministische Würfel)
sitzt **vor** allem anderen: in 2 % der Anfragen weicht auch eine
ausweichbereite Einheit nicht aus.

⚠⚠ **Toter Zweig, in C und F byteweise gleich.** Bei `0x404DA0` wird
`UKOL == 4` mit `return 0` beantwortet; vier Befehle später prüft `0x404DAE`
noch einmal `cmp al,4 / jne` und öffnet für den Fall `al == 4` einen Zweig, der
`e.DALSI_SMER(+0x1A) != 0xFF` prüft. **Dieser Zweig ist unerreichbar.**
Vermutlich war `wenn u==4 ∧ +0x1A == 0xFF: return 0` gemeint und die Reihenfolge
ist vertauscht. Die F-Fassung `0x404D00` hat denselben Zweig Byte für Byte (nur
`.data`-Sockel verschoben) — es ist also kein Zerlegerfehler, sondern der
Zustand beider Auslieferungen.

---

### 7. ⭐ Fussabdruck, Nachschub und Sprengung — die `move units`-Helfer

#### 7.1 `0x406C20` / `0x406C70` — 2×2 und 4×4

```
0x406C20(sp, ze, wert):  sec6[sp,ze] = sec6[sp+1,ze] = sec6[sp,ze+1] = sec6[sp+1,ze+1] := wert
0x406C70(sp, ze, wert):  für i,j in 0..3: sec6[sp+i, ze+j] := wert
```

⭐ Der Index ist überall `Spalte·256 + Zeile`; `0xBDEC80 − 0xBDEA80 = 0x200 =
2·256` ist genau eine Spalte weiter. Zusammen mit den drei Breitensuchen aus
`OFFENE_FRAGEN.md` BB (1×1, 2×2, 4×4) ist belegt: **eine Einheit belegt 1, 4
oder 16 Zellen** — und `move units` ruft die 2×2-Fassung 9×, die 4×4-Fassung 6×.

#### 7.2 `0x412720` / `0x412850` — Nachschub im 3×3

Beide durchlaufen die 3×3-Nachbarschaft der Einheit, prüfen `Zellwert/1000 ==
eigener Spieler` und erhöhen dann um **eins je Takt**:

| Funktion | Vorrat | Höchstwert |
|---|---|---|
| `0x412850` | `word[+0x2E]` (**Sprit**) | `word[+0x30]` |
| `0x412720` | `byte[+0x39]` | `byte[+0x3A]` |

⭐ Beide Paare werden bei der Erstbelegung durch `0x410420` **auf den
Höchstwert gesetzt** (`+0x2E := +0x30`, `+0x39 := +0x3A`). Das ist der Beleg,
dass es zwei gleichgebaute Vorräte sind. `+0x39` fehlt in `ENTITY_FELDER.md`.

#### 7.3 `0x4123D0` → `0x4121E0` → `0x412280` — der Selbstmörder

```
0x4123D0(id):  für r in 0..7:
                   wenn 0x4121E0(id, RX+dir[r].x, RY+dir[r].y): 0x412280(id) ; ende
0x4121E0(id, x, y): Karte gültig ∧ sec6[x,y] < 8000 ∧ Bündnis[eigen][fremd] == 0
0x412280(id):  für r in 0..7 über die Tafel 0x4F6310 (8 × (i16,i16)):
                   Gebäude in der Zelle          → Zasah(40020, zelle)
                   Wert < 14000 ∧ Gattung != 3   → Zasah(40250, zelle)
               0x4047E0(Klang 36, Art 1, id, 0)
               Zasah(40250, sec6[RX,RY])        ; sich selbst
```

⭐ `0x412480` ist die grössere Fassung derselben Sache (Quellkennungen
40010 Gebäude / 40030 Einheit / 40100 Infanterie, dazu `0x435950` mit
Wirkungsradius `0x1F4 = 500`). Die Kennungen ≥ 40000 sind **Pseudo-Angreifer**:
`Zasah(quelle, opfer)` prüft `quelle < 8000` genauso wie `opfer < 8000` —
alles darüber ist »die Umwelt«.

⚠ **Zwei verschiedene Richtungstafeln.** `0x412280`/`0x412480`/`0x4123D0`
benutzen `0x4F6310`, `0x4120C0` dagegen `0x4F5AF0`. Wer die verwechselt, dreht
die Sprengung gegen die Fahrtrichtung.

#### 7.4 `0x411C80` — Umschlag abbrechen

Läuft je Einheit nur, wenn `(Bildzähler[0x4FA240] + id) % 20 == 9` — also
**einmal je 20 Takte, über die Einheiten gestaffelt** (bei 50 Hz alle 0,4 s).
Dann 760 Ringeinträge (`0x79A008`) absuchen; für jede Einheit mit
`l_engine == 0xAD` und `UKOL == 2`, die **nicht verbündet** ist
(`byte[0x87B155 + 40·eigen + fremd] == 0`), wird `UKOL := 0` und
`trans(+0x40) := 0xFFFF` gesetzt. Zum Schluss Klang 47 über `0x4047E0`.

⭐ Belegt nebenbei die **Zeilenbreite 40 der Bündnismatrix sec53**
(`0x87B140`, Bündniszeile `+0x15`) — unabhängig auch aus `0x41B0D0`
(`byte[0x87B140 + 40·p] := 0xFF`) und aus dem Lader `0x41E070`
(`add edx, 0x28` über `0x87B140 … 0x87B280` = 8 × 40).

#### 7.5 `0x4118C0` — freie Zelle im Rechteck

`0x4118C0(&sp, &ze, ze0, sp0, nZeilen, nSpalten) → bool`: Spalte aussen, Zeile
innen; nimmt die erste Zelle mit `sec6 == 0xFFFE`, für die zusätzlich
`0x421D40(sp, ze)` **falsch** ist. Zwei Rufer in `move units`, beide im Umkreis
der Zeichenkette »Error while unloading… no ramp found«. → **Absetzplatz beim
Entladen.**

#### 7.6 `0x40AFE0` — Streufahrbefehl

```
0x40AFE0(id):  0x40B070(id, RX − zufall()%5 + 2, RY − zufall()%3 + 5, 0)
```
also ein `fahre`-Auftrag (UKOL 2) auf eine Zelle **2 Spalten um die eigene
herum und 3…5 Zeilen darunter**. ⚠ Die Y-Seite ist asymmetrisch (`+3 … +5`),
die X-Seite symmetrisch (`−2 … +2`). Rufer: `move units` ×3 und der Gebäudetakt
`0x43CA50` ×2 (dort bei »mining«).

---

### 8. ⭐ Der Umschlagsatz sec48 — `0x410610` / `0x410680` / `0x4106D0`

Alle drei greifen über `word[einheit + 0x40]` (`trans`) auf
`0x77AC50 + 18·trans` zu (Schrittweite **18**, aus `lea eax,[eax+eax*8]` mit
`eax = 2·trans` gerechnet — trifft die verzeichneten »400 × 18«).

| Versatz | Was |
|---|---|
| `+0x00 … +0x03` | **4 Ladeplätze**, `0xFF` = leer |
| `+0x04` | ein Ziel-/Warenwert |
| `+0x09`, `+0x0A` | zwei Werte, die auf Gleichheit geprüft werden |
| `+0x10` | ⭐ **Reihum-Zeiger** über die 4 Plätze |

```
0x410680(id) → (Zahl der belegten Plätze > 1)
0x410610(id) → max über die 4 Plätze von 0x410510(id, platzwert)
0x4106D0(id) → wenn satz[+0x0A] == satz[+0x09]: return satz[+0x04]
               sonst: Reihum-Zeiger satz[+0x10] auf den nächsten belegten
                      Platz drehen (mod 4), dessen Wert über 0x410510 bewerten,
                      gegen 0x410610 abwägen und den besten Platz melden
```

⭐ Das ist der **Reihum-Wähler**, mit dem ein Transporter seine vier Fächer der
Reihe nach abarbeitet — nicht immer das erste. Rufer: `move units` bei
`0x407FA0`.

---

### 9. ⭐⭐ Spieler ausschalten — `0x41B0D0`

Rufer: `0x41B310` (»PLAYERS / Too many players for this map«) und dreimal
`Hand control` (`0x4C2280`).

```
0x41B0D0(spieler p):
  für id in 1000·p .. 1000·p+999:
      wenn faze != 0xFF ∧ UKOL < 99:  0x40B3C0(id)          ; Einheit auflösen
  für i in 0..199:                                          ; sec19 Flugzeuge, 68 B
      wenn byte[0x6DDF78 + 68·i] != 0 ∧ byte[+1] == p:  byte[+0] := 0
  für i in 0..254:                                          ; sec3 Gebäude, 76 B
      wenn byte[0xC06910 + 76·i + 4] != 0 ∧ byte[+5] == p:
          byte[+5] := 11 ;  byte[+0x41] := 11               ; ⭐ Besitzer 11
          wenn byte[+4] == 9:                               ; Flughafen
              s := byte[+0x19]
              byte[0x87943C + 52·s]     := 0                ; Platzzahl
              byte[0x87943C + 52·s + 5] := 1
              10 × dword 0xFFFFFFFF ab 0x879443 + 52·s      ; Plätze leeren
  byte[0x87B140 + 40·p] := 0xFF                             ; sec53-Zeilenkopf
```

⭐ **Schrittweiten aus den `lea`-Ketten gerechnet, bevor nachgeschlagen wurde:**
68 (`shl ecx,4 / add ecx,eax` = 17c, dann `[ecx*4]`), 76 (`lea ebp,[eax+eax*8]`
= 9i, `lea eax,[eax+ebp*2]` = 19i, dann `[eax*4]`), 52 (`lea ebp,[eax+eax*2]`
= 3a, `lea eax,[eax+ebp*4]` = 13a, dann `shl eax,2`). Alle drei treffen die
verzeichneten Werte (sec19 = 200 × 68, sec3 = 300 × 76, Flughafen-Platztafel aus
`AIR_RE.md`).

⚠ **Der Gebäudelauf geht nur bis 254**, nicht bis 299 — `cmp si, 0xFF / jl`.
Gebäude auf den Plätzen 255…299 behalten beim Ausschalten ihren Besitzer.
In F (`0x41A290`) steht dieselbe Schranke.

⭐ `0x41B194` (»Besitzer 11 setzen (Skript/Netz)«) steht schon in
`OFFENE_FRAGEN.md` — hier ist der **Rumpf**, in dem diese Stelle sitzt.

---

### 10. ⭐ Weitere einzeln belegte Stücke

**`0x410420(id, ?, gebaeude)` — die Einheit verlässt das Gebäude.**
`UKOL := 0x33 (51)`, `RX := b[+0x00] + b[+0x35]`, `RY := b[+0x02] + b[+0x36]`,
dann `+0x02 = +0x03 = 0`, `+0x04 = 0xFF`, `word[+0x06] = 0`, `+0x16 = 0`,
`+0x17 = +0x1A = 0xFF`, `word[+0x32] = 0`, `word[+0x34] = 0xFFFF`,
`+0x2E := +0x30`, `+0x39 := +0x3A`. ⭐ Das ist die **vollständige
Anfangsbelegung eines neu gebauten Fahrzeugs** und für den Nachbau direkt
verwertbar. Die Schrittweite 76 des Gebäudesatzes fällt hier zum zweiten Mal aus
der `lea`-Kette (`9c → 19c → ×4`).

**`0x40B9A0(id, x, y)` — Angriffsbefehl mit Reichweitenkürzung.**
Wenn `y == 0x7530 (30000)`, ist `x` eine Einheitennummer (die Sonderregel aus
`OFFENE_FRAGEN.md` BE.1) und es wird nichts gerechnet. Sonst:
`d = √((40·(RX−x))² + (40·(RY−y))²)`, `r = 0x454200(id)` (Reichweite über die
Gattung, 6-Arm-Sprungtafel `0x45424C`); ist `r < d`, wird der Zielpunkt auf
`r` Zellen entlang der Geraden gekürzt. Danach der Kratzbefehl:
`word[0xB8A3D8] := 9`, `+8 := id`, `+0x0A := x`, `+0x0C := y`, `+0x0E := 0xFFFF`,
dann `0x4C2190` (»Befehl absenden«). ⭐ **Befehlsnummer 9 = »greife an«**, mit
den Feldversätzen des Kratzbefehls.

**`0x40F760(a, b)` — Richtung von Einheit a zu Einheit b, 0…7.**
Beide Feinpositionen über `0x435BD0(KOLIK, POHYB, …)`; Feinraster
**40 Unterschritte je Zelle** (`(RXa−RXb)·40 − suba + subb`), dann über
`0x4D6D62` (x87), Skalierung `[0x4F5AE8]`, Quadrantenkorrektur über
`[0x4F0064…0x4F0078]`, zuletzt `wenn r < 2: r += 8 ; r −= 2`.
Ergänzt `0x40F9B0` (»Richtung von Einheit zu (x,y)«) um die
Einheit→Einheit-Fassung mit Unterzellgenauigkeit. **Rufer: die
Schussvorbereitung.**

**`0x410D70(id)`** — `int(√((RX−CX)² + (RY−CY)²))` über `fild/fsqrt/__ftol`,
also die **Luftlinie zum eigenen Zielfeld** (`+0x18 CX`, `+0x19 CY`).

**`0x419E10(techstufe)` — Baupläne freischalten.**
Eine Tafel auf dem Stapel, Baupläne **50…56**, Schwellen `{1, 2, 1, 1, 3, 8, 6}`:
`wenn schwelle[d] <= techstufe: 0x4D04D0(d, 1)`. Zum Schluss unbedingt
`0x4D04D0(59, 0)`. Rufer: `gefecht_starten`. ⭐ Gehört unmittelbar zum fünften
Auslieferungsunterschied (AT.6): C füllt im Gefecht Bauschlangen nach
Technikstufe — und schaltet hier zusätzlich Baupläne frei.

**`0x412D10`** — GDI: `CreateCompatibleDC`, `SelectObject`, `GetObjectA(hbm,
0x18, &bm)`, `SetMapMode`/`DPtoLP`, `GetSystemMetrics(0)/(1)`, dann `BitBlt` in
**16×16-Kacheln** mit `SRCCOPY`, `DeleteDC`. **Kein Spielcode.**
**`0x414DD0`** — `Release` auf `[0x540770]` (Primäroberfläche) und `[0x540730]`
(`IDirectDraw`), beide auf 0 gesetzt. **DirectDraw-Abbau.**

**`0x403D60` / `0x403DE0` / `0x404000` / `0x404080` / `0x4040E0`** — der
Netzwerk-Vorschaltdialog. `0x403D60` öffnet Vorlage **129** mit `0x403DE0` als
`DlgProc`; die füllt bei `WM_INITDIALOG` das Listenfeld **1021**. `0x404000` und
`0x404080` sind `stdcall`-Rückrufe mit 4 bzw. 5 Argumenten (`ret 0x10` /
`ret 0x14`) und passen auf `DPENUMSESSIONSCALLBACK` bzw.
`DPENUMPLAYERSCALLBACK`: `0x404000` bricht bei `flags & 1` (`DPESC_TIMEDOUT`)
ab, hängt den Namen aus `lpSD + 0x24` an und speichert `lpSD + 0x14` als
ItemData; `0x404080` hängt `lpFriendlyName` an und speichert die `DPID`.
⭐ **Nullmodell:** Argumentzahl **und** beide Feldversätze treffen die
DirectPlay-1-Sätze; ein beliebiger Rückruf legt keinen Namen auf `+0x24` und
keine Kennung auf `+0x14`. Die benutzten Einfuhren sind
`SendMessageA`/`SetFocus`/`EndDialog`/`DialogBoxParamA` (aus der
Einfuhrtafel gelesen, nicht geraten).

**`0x405460(id, richtung)`** — `sec6[RX + dir.x, RY + dir.y]` über `0x4F5AF0`.
Sauber, klein, **ohne Rufer** (auch nicht über den Thunk `0x40147E`).
**`0x404D10`** — ein einzelnes `ret`, ohne Rufer. Beide sind **toter Code**.

---

### 11. ⚠⚠ Kein zwölfter Auslieferungsunterschied — die drei Adressen geprüft

Die Vollerhebung meldete `0x409FF6`, `0x414538` und `0x41AA21` als »in F nicht
vorhanden«. **Alle drei sind keine Funktionsanfänge.** `aere.py fs` findet für
alle drei keinen Anfang; `adis.py` zeigt jeweils ein **Sprungziel mitten in
einer grösseren Funktion**:

| Adresse | liegt in | Beleg |
|---|---|---|
| `0x409FF6` | `move units` `0x406CD0` (+13 094) | `jmp 0x409FF6` bei `0x409FDB`, dazu `jne` bei `0x409FE2` und `0x409FE9` — eine Sammelmarke |
| `0x414538` | `0x412D10`-Block (+6 184) | `jmp 0x414538` bei `0x41451A` |
| `0x41AA21` | `gefecht_starten` `0x41A150` (+2 257) | Schleifenausgang von `cmp bl,8 / jb 0x41A836` bei `0x41AA1B` |

Die umschliessenden Funktionen **gibt es in F sehr wohl**: `cfind.py` liefert
`0x406CD0 → 0x406CB0` und `0x412D10 → 0x412AE0` (eindeutig). Bei `0x41A150` ist
der bekannte F-Partner `0x419F90`, und dass eine Marke bei +2 257 dort fehlt,
ist die **unmittelbare Folge des schon belegten fünften Unterschieds**
(C 3 171 B gegen F 614 B) — kein neuer Befund.

⚠⚠ **Warnung zu `cfind.py`:** für `0x41A150` schlägt es `0x48631F` mit »60 %«
vor. Das ist **falsch** — der wahre Partner ist `0x419F90`. Die Prozentzahl
unter etwa 90 % ist damit an einem Gegenbeispiel als unbrauchbar belegt.
Oberhalb davon hat das Werkzeug in meinem Revier 46 von 46 richtig getroffen
(geprüft an der Blockkonstanz der sechs Abstände).

#### Die fünf »ungenauen« Fälle — von Hand nachgesehen

| C | F | gemeldet | tatsächlich |
|---|---|---|---|
| `0x406C70` | `0x406C50` | 85 % | andere Registerwahl, andere Reihenfolge der Argumentladung; gleiche 4×4-Schleife, Ziel `0xBDDAE0`. **Gleich.** |
| `0x410420` | `0x410250` | 97 % | zwei Befehle vertauscht (`mov cx,word[+2]` / `mov cl,byte[+0x36]`). **Gleich.** |
| `0x410610` | `0x410440` | 94 % | `edi` gegen `ebx` getauscht. **Gleich.** |
| `0x41BBD0` | `0x41AD90` | 98 % | `cmp ecx,edx / jle` gegen `cmp edx,ecx / jge` — dieselbe Bedingung. **Gleich.** |
| `0x41BD60` | `0x41AF20` | 94 % | `ecx`/`edx` getauscht, ein `push` verschoben. **Gleich.** |

Auch `0x404D20` gegen `0x404D00`: **Befehl für Befehl gleich**, einschliesslich
des toten Zweigs aus Abschnitt 6.

**Ergebnis: in Revier 1 gibt es keinen einzigen Verhaltensunterschied zwischen
C und F.**

---

### Berichtigungen an bestehenden Dokumenten

1. ⭐⭐ **`OFFENE_FRAGEN.md` BB.8, Punkt 2 ist erledigt.** Das 22-Byte-Format der
   Infanteriezelle ist `[Marke][Feld][Spalte][Zeile][9 × u16]`, nicht
   »col, row, 9 × u16«. Drei unabhängige Leser, und 4 + 18 = 22 geht punktgenau
   auf. `zz_deutung.py` liegt um zwei Byte daneben. Der Test `byte[+1] == 0` der
   Passierbarkeitskarte ist damit gültig.
2. ⭐⭐ **`OFFENE_FRAGEN.md` AU, Punkt 6 ist erledigt.** Der dreistufige Index der
   Sektor-Ankertafel ist `2·(121·klasse + 11·wy + wx)`; die Tafel hat zwei
   Ebenen zu 121 Sektoren, x und y liegen **nebeneinander** bei `+0`/`+1`.
   Der Füller ist `0x41BD60`, und `reloc_refs` bestätigt: es gibt keinen anderen.
3. ⭐ **`OFFENE_FRAGEN.md` BB.8, Punkt 1 ist neu zu stellen.** `0xFFFB…0xFFFE`
   sind keine vier unabhängigen Sonderwerte, sondern `0xFFFE − n` mit `n` aus
   dem Zwei-Bit-Keller `POD` (+0x13). Die Frage lautet jetzt: welche zwei Bit
   trägt der Untergrund.
4. ⭐⭐ **Die Packung des Spielstands fehlt in allen `.md`.**
   `grep -i "komprim|compress|RLE"` über `OFFENE_FRAGEN.md`, `GAMESTATE_RE.md`
   und `CAMPAIGN_RE.md` findet nichts, obwohl `cwm_sections.py` den Entpacker
   längst implementiert. Abschnitt 1 dieses Berichts ist die Spezifikation —
   samt Packer, den das Projekt nirgends hat.
5. ⚠ **`ENTITY_FELDER.md`:** `+0x39`/`+0x3A` fehlen. Sie sind ein Vorrat mit
   eigenem Höchstwert, genau wie `+0x2E`/`+0x30` (Sprit) — bei der Fertigung
   vollgesetzt (`0x4104CE`), von benachbarten eigenen Einheiten je Takt um 1
   erhöht (`0x412720`), beim Besitzerwechsel mitgenommen (`0x4D0E5D`).
6. ⚠ `OFFENE_FRAGEN.md` Abschnitt BE nennt `0x4047E0` »Klang/Effekt auslösen«.
   Die Argumentform ist `0x4047E0(klang, art, einheit, 0)` — belegt an
   `0x41234F` (36, 1), `0x412546` (36, 1) und `0x411DC1` (47, 6). Die
   Klangnummern stehen schon in `KLAENGE.md`, die **Art** als zweites Argument
   nicht.
7. ⚠ `OFFENE_FRAGEN.md` AU.6 vermerkt den Leser der Ankertafel als
   `2·(11·wx+wy)`. Im gelesenen Code (`0x4BD71E`/`0x4BD730` und `0x41C6C0`) ist
   die Reihenfolge `11·wy + wx`; ob das nur eine Namensfrage ist oder ein echter
   Achsentausch, hängt daran, wie der Rufer seine beiden Zähler nennt — und der
   liegt ausserhalb meines Reviers. **Beim Nachbau nachprüfen.**

---

### Bauaufgaben, die daraus folgen

1. ⭐⭐ **Die Selbstprüfung der Infanteriezellen nachbauen** (`0x412B20`).
   Sie läuft in der Hauptschleife und **löscht** Infanterie, die nicht in einer
   Zelle in ±1 ihrer Position eingetragen ist. Wer sie weglässt, bekommt eine
   andere Kampagne — genauso wie einer, der sie mit anderer Toleranz baut.
2. ⭐⭐ **`POD` (+0x13) als Zwei-Bit-Keller umsetzen.** Ohne ihn hinterlässt jede
   fahrende Einheit, die getroffen wird, den falschen Zellzustand — und die
   Passierbarkeitskarte wird schleichend falsch. Der Ablauf steht in
   Abschnitt 4.
3. ⭐ **Ausweichen mit `UKOL 3` und der 2-%-Verweigerung** (`0x404D20`).
   Der Zufallsschritt ist kein Schmuck, er ist die Stauauflösung. Bei
   `l_engine == 0xAB` weicht nie jemand aus, bei `UKOL == 4` (Angriff) auch
   nicht.
4. ⭐ **Fussabdruck 1/4/16 Zellen** (`0x406C20`/`0x406C70`) statt einer Zelle je
   Einheit. Hängt unmittelbar an den drei Breitensuchen aus BB.
5. ⭐ **Anfangsbelegung eines gebauten Fahrzeugs** wörtlich aus `0x410420`
   übernehmen (Abschnitt 10) — einschliesslich `UKOL 51` und der beiden Vorräte.
6. ⭐ **Angriffsbefehl auf die Waffenreichweite kürzen** (`0x40B9A0`).
   Wir schicken die Einheit vermutlich bis auf die Zielzelle; das Original
   schickt sie nur bis auf Reichweite.
7. ⭐ **Nachschub im 3×3** (`0x412720`/`0x412850`): eine eigene Einheit in der
   Nachbarschaft füllt Sprit und den zweiten Vorrat um **1 je Takt** auf.
8. **Reihum-Wähler der vier Umschlagfächer** (`0x4106D0`) statt »immer das
   erste«.
9. **Spieler ausschalten** (`0x41B0D0`) vollständig: Einheiten auflösen,
   Flugzeuge löschen, Gebäude auf Besitzer 11, Flughafenplätze leeren,
   sec53-Zeilenkopf auf `0xFF`. ⚠ Und: nur die Gebäudeplätze **0…254**.
10. **Packer nachbauen**, wenn wir je originalformatige Dateien schreiben wollen
    (`0x41B6A0`). Der Entpacker ist schon da; der Packer nirgends.
11. **Sektor-Anker beim Kartenladen erzeugen** (`0x41BD60`) — die Gruppenfahrt
    über sec108-Wegpunkte (`0x4BCF30`) hängt daran, und die Tafel steht **nicht**
    in der Datei, sie wird beim Laden gerechnet.

---

### Was ungedeutet bleibt

1. ⚠ **`byte[+0x39]` / `byte[+0x3A]` des Einheitensatzes.** Sicher ist: ein
   Vorrat mit eigenem Höchstwert, bei der Fertigung vollgesetzt, von
   benachbarten eigenen Einheiten um 1 je Takt aufgefüllt, beim Besitzerwechsel
   kopiert. **Munition ist die naheliegende Vermutung — ich habe sie nicht
   belegt.** Der Schussauslöser `0x40EC70` liest das Feld nicht; die Leser
   sitzen in `0x40B4B5`, `0x40BB44`, `0x40C587`, `0x424B4E`, `0x427C7B`,
   `0x4321EF`, `0x43684A` und in vier gleichgebauten Stellen ab `0x4B72E9`.
2. ⚠ **Das Klassenbyte der Sektor-Ankertafel.** Die Tafel hat zwei Ebenen; der
   mir bekannte Leser (`0x4BD71E`) liest nur Ebene 0. Was Ebene 1 unterscheidet,
   steht im Rufer `0x41C6C0`, den ich nur an der Kante gelesen habe. ⚠ Zusätzlich
   lesen `0x4B850C`, `0x4B8579` und `0x4BEBCC` die Tafel bei `+246`, `+247` und
   `+466` mit `[reg*2 + Sockel]` — das passt zu Ebene 1, aber ich habe diese drei
   Rümpfe nicht aufgemacht.
3. ⚠ **`0x421D40`** (aus `0x4118C0`): Tafel `0x552E18`, Schrittweite 6, Felder
   `+0`, `+1`, `+4`. Ich weiss, dass sie den Absetzplatz sperrt — nicht, was sie
   führt.
4. ⚠ **`0x410DF0`**: Zugsatz `0xBC0DD0`, Schrittweite 48, Felder `+0` (auf 0
   gesetzt), `+2` (u16 Einheitennummer), `+4+i`, `+0x2C` (Index). Setzt
   `UKOL := 0x38 (56)` und `AKCE := byte[0xA8D508 + 8·b]`. Die Tafel `0xA8D508`
   liegt unmittelbar vor sec35 (`0xA8D8C8`) und ist ungedeutet.
5. ⚠ **Die Aussenlesung in `0x412280`** (Abschnitt 5). Gemeldet, weil sie
   byteweise auch in F steht und die Schwesterfunktion `0x412480` es anders
   macht — **aber kein Datensatz zeigt sie.**
6. ⚠ **Die drei Anfangsbyte des Speicherers** (`byte[0x4F8D74] = 2`,
   `byte[0x4F8114] = 1`, `byte[0x87B3C0]`) passen **nicht** auf den Kopf der
   ausgelieferten `.CWM`/`.DM` (`67, 1, Spielerzahl, Art`). Entweder schreibt
   `0x41D210` eine andere Dateiart (Spielstand), oder der Lader öffnet an der
   von mir gelesenen Stelle eine Begleitdatei. **Nicht entschieden** — beide
   Rümpfe liegen ausserhalb meines Reviers. Der gepackte Strom der `.CWM`
   beginnt bei Dateiversatz **75** und ist gemessen (Abschnitt 1.5). Die
   Blockkette bricht vor Dateiende ab (bei `01.CWM` 29 530 Byte Rest) — dort
   steht vermutlich ein ungepackter Abschnitt (≤ 100 Byte) oder ein Abschnitt,
   den ein anderer Weg schreibt. **Ungeprüft.**
7. ⚠ **`0x403DE0` ab `0x403E64`** (die `WM_COMMAND`-Zweige der Spielerliste) und
   `0x404780` (welches COM-Objekt bei `0x4F5A38` liegt) habe ich nur an der
   Kante gelesen. Für die Spielsimulation ohne Belang.
8. ⚠ **`0x405460` und `0x404D10` haben keinen Rufer** — geprüft mit `rufer.py`
   auf die Funktion **und** auf ihren Thunk (`0x40147E`, `0x401A5F`). Nach der
   Regel aus der Einweisung ist ein Negativbefund ohne `reloc_refs` verdächtig;
   hier hilft `reloc_refs` aber nicht, weil ein Thunkaufruf ein `rel32` ist und
   keine Relokation hinterlässt. **Der Befund steht damit nur so fest, wie
   `rufer.py` relative Sprünge findet.**
9. ⚠ **Die Stimmenauswertung in `0x41BD60`** (der »Sieger« aus `0x542B00`) läuft
   erst über `bx = 0..3` und dann über `bx = 8..4`, mit `>=` als Vergleich.
   Warum in dieser Reihenfolge und warum Index 4…8 rückwärts, habe ich nicht
   erklärt. Der Ablauf ist gelesen, die Absicht nicht.

---

## BJ. Revier 2: 0x41C230 … 0x42DD60

53 Funktionen, 10 912 Byte. Alle 53 waren vorher **nirgends erwähnt**
(`grep` über `OFFENE_FRAGEN.md` und alle `aekernel-tools/*.md`: 0 Treffer für
jede der 53 Adressen; Gegenprobe mit `0x41D0E0` → 6 Treffer, mit `0x42C8C0` →
5 Treffer, der Griff misst also).

Das Revier zerfällt in **acht** Mechanikgruppen, nicht in 53 Einzelbefunde:
die KI-Wegekarte samt Brücken, der Zellensatz der Karte, das Bodenbild und der
Nebel, die Lagerstätten, Minen/Fallen/Radare, das Wetter, der Luftkrieg
(Flugzeuge + Flugabwehr) und die Zeichenliste.

---

### Adresstafel

F-Adressen aus `cfind.py`, **zwei davon von Hand berichtigt** (siehe
»Berichtigungen«, fett gesetzt). Der Versatz C→F ist in diesem Revier
**nicht konstant**: −0xE40 bis `0x4224xx`, −0xE20 ab `0x425CB0`,
−0xE10 ab `0x426D30`.

| C | F | Byte | Rufer | Was sie ist |
|---|---|---:|---:|---|
| `0x41C230` | `0x41B3F0` | 608 | 1 | ⭐ Brückenregel erzeugen: 3×3-Sektorumfeld neu prüfen |
| `0x41C490` | `0x41B650` | 496 | 1 | ⭐⭐ ganze Karte nach Brückenplätzen absuchen |
| `0x41C680` | `0x41B840` | 64 | 1 | alle 100 Brückenplätze löschen |
| `0x41C6C0` | `0x41B880` | 400 | 1 | ⭐⭐ Sektor-Kantentafel bauen (10×10 BFS-Paare) |
| `0x41CA40` | `0x41BC00` | 144 | 1 | ⭐⭐ `apply`: 400 Brückenregeln auf die Kantentafel |
| `0x41CAD0` | `0x41BC90` | 48 | 3 | ⭐ `restore` (242 B) + `apply` |
| `0x41CED0` | `0x41C090` | 272 | 1 | ⭐ Zellensatz der Karte neu anlegen (`W·H·4`) |
| `0x41CFE0` | `0x41C1A0` | 96 | 1 | `set_cell(x,y,u16 kachel,u8 hoehe,u8 bruecke)` |
| `0x41D040` | `0x41C200` | 80 | 0¹ | `get_cell(x,y,&hoehe,&bruecke) → u16 kachel` |
| `0x41D170` | **`0x41C330`** | 80 | 10 | `set_hoehe(x,y,v)` → Zellensatz `+2` |
| `0x41D1A0` | **`0x41C360`** | 80 | 32 | ⭐⭐ `set_bruecke(x,y,v)` → Zellensatz `+3` |
| `0x41F9E0` | — | 16 | 2 | ⚠ **leer** (`ret`) |
| `0x41F9F0` | — | 16 | 413 | ⚠ **leer** (`ret`) |
| `0x41FA00` | — | 16 | 157 | ⚠ **leer** (`ret`) |
| `0x41FA10` | `0x41EBD0` | 208 | 1 | ⭐ Bodenkachel würfeln (Tafel `0xBAA800`) |
| `0x41FAE0` | `0x41ECA0` | 176 | 3 | ⭐ sec51 (`0x5539D0`) für die ganze Karte bauen |
| `0x41FC60` | `0x41EE20` | 160 | 1 | ⭐ Nebelkachel einer Zelle |
| `0x41FD00` | `0x41EEC0` | 96 | 1 | Nebelkachel »ganz zu« |
| `0x41FE20` | `0x41EFE0` | 304 | 2 | ⭐ ein GEBÄUDE ganz aufdecken (Grundriss) |
| `0x4203A0` | `0x41F560` | 528 | 1 | ⭐ Sicht (sec50) im Kreis r≤19 **löschen** |
| `0x420FC0` | `0x420180` | 480 | 1 | ⭐ Lagerstätten: wer steht daneben (sec38 `+6+p`) |
| `0x421C40` | `0x420E00` | 256 | 1 | Mine/Falle eines Verbündeten auf (x,y)? |
| `0x421D40` | `0x420F00` | 208 | 3 | Mine/Falle/Radar auf (x,y)? (Besitzer egal) |
| `0x421E10` | `0x420FD0` | 192 | 3 | ⭐ Mine entfernen + **Klang 40** |
| `0x421ED0` | `0x421090` | 192 | 3 | ⭐ Falle entfernen + **Klang 41** |
| `0x421F90` | `0x421150` | 496 | 2 | ⭐⭐ Brückengeländer in Zellensatz `+3` stempeln |
| `0x422210` | `0x4213D0` | 176 | 1 | ⭐⭐ **der Wind** (Richtung + Stärke), alle 2000 Takte |
| `0x422340` | `0x421500` | 96 | 1 | Ablaufzähler über 200 × 6 B (sec89/sec114) |
| `0x422BD0` | `0x421D90` | 432 | 1 | ⭐ Kartenrand nach Geländehöhe sperren |
| `0x422D80` | — | 16 | 195 | ⚠ **leer** (`ret`) |
| `0x422D90` | `0x421F50` | 16 | 12 | ⚠ `dword[0x542DF4] = 0` — **niemand liest das** |
| `0x422DA0` | — | 16 | 1 | ⚠ **leer** (`ret`) |
| `0x422DB0` | `0x421F70` | 64 | 0¹ | Flugzeug dreht **+6°** (`dir`, sec19 `+0x0A`) |
| `0x422DF0` | `0x421FB0` | 48 | 0¹ | Flugzeug dreht **−6°** |
| `0x425CB0` | `0x424E90` | 352 | 2 | ⭐ Flugzeug schlägt auf? — acht Höhenschwellen |
| `0x425FE0` | `0x4251C0` | 64 | 2 | Flugzeug steigt eine Stufe (bis `Stufe oben`) |
| `0x426D30` | `0x425F20` | 272 | 1 | ⭐ Zellenwert für den Bombenanflug: 1 Einheit, 2 Gebäude |
| `0x426E40` | `0x426030` | 592 | 2 | ⭐⭐ **Bombenziel**: Aufschlagpunkt + Ringsuche |
| `0x427930` | `0x426B20` | 96 | 5 | ⭐ **Netzbefehl 6** absenden (4 Wörter) |
| `0x427E30` | `0x427020` | 176 | 1 | ⭐ Flugzeug greift Einheit an, `uk = m_uk = 10`, **Klang 75** |
| `0x427EE0` | `0x4270D0` | 176 | 1 | ⭐ dito mit `uk = m_uk = 11` |
| `0x427F90` | `0x427180` | 288 | 1 | ⭐ nächstes Feindflugzeug → Befehl 6 |
| `0x4281B0` | `0x4273A0` | 416 | 1 | ⭐ Luft gegen Luft: Feindflugzeug prüfen und beschiessen |
| `0x428350` | `0x427540` | 560 | 1 | ⭐⭐ **Flugabwehr vom Boden** (Kegel + Nachladen) |
| `0x428580` | `0x427770` | 128 | 1 | ⭐ `Check AA`-Auftrag anlegen (`0x6E1498`, 200 × 8) |
| `0x428D60` | `0x427F50` | 352 | 2 | Flughafen: Liste der baubaren Muster (sec27) |
| `0x428F30` | **`0x428120`** | 80 | 0¹ | `datei_schreiben(pfad, puffer, n)` — Modus `"w"` |
| `0x428F80` | `0x428170` | 80 | 0¹ | `dateigroesse(pfad)` |
| `0x428FD0` | `0x4281C0` | 80 | 14 | ⭐ `datei_da(pfad)` → 0/1 |
| `0x429170` | `0x428360` | 48 | 1 | ⭐ `faze = 0xFF` für **8000** Einheiten + 2000 × 10 B |
| `0x4291A0` | `0x428390` | 128 | 1 | ⭐ **Wichtigkeit** einer Einheit (Auswahlvorrang) |
| `0x429220` | `0x428410` | 112 | 2 | ⭐ wichtigste Einheit der Auswahlliste |
| `0x42DD60` | `0x42CF50` | 400 | 1 | ⭐⭐ Verdeckungsprüfung im fertigen Bild |

¹ »0 Rufer« heisst hier: `rufer.py` findet über den Thunk keine `E8`-Stelle.
Diese Funktionen werden über Zeiger oder Tafeln erreicht (die Ruferzahl der
Revierliste kommt aus einer anderen Zählung).

---

### 1. ⭐⭐ Die KI-Wegekarte `aiNN.cwi` wird notfalls SELBST ERZEUGT

Abschnitt W von `OFFENE_FRAGEN.md` liest die Datei als KI-Wegekarte und nennt
in AX.8 Punkt 6 die Füllung der Sektor-Ankertafel `0x541F60` **ungelesen**.
Sie ist jetzt gelesen — und die eigentliche Nachricht ist eine andere:

> ⭐⭐ **Wenn `aiNN.cwi` fehlt, baut das Spiel sie beim Laden aus der Karte.**
> Die 43 Dateien sind ein **Zwischenspeicher, keine Autorendaten.**

Der Lader `0x41CB00` (der aus W bekannte, mit `push 0x7d0 / 0x1e4 / 0x1e4`):

```
ai_laden(mission):
    Name = "ai" + (mission < 10 ? "0" : "") + mission + ".cwi"
    f = fopen(Name, "rb")                            @0x41CCBA
    wenn f == NULL:
        ai_erzeugen()                                @0x41CCC8 -> 0x41C850
    sonst:
        fread(0x542330, 1, 2000, f)   ; Block A: 400 × 5 Brückenregeln
        fread(0x541F60, 1,  484, f)   ; Block B: 2 × 121 × 2 Sektor-Anker
        fread(0x542148, 1,  484, f)   ; Block C: 2 × 121 × 2 Kantenbytes
        fclose(f)
    restore_und_apply()                              @0x41CD17 -> 0x41CAD0
```

`ai_erzeugen()` (`0x41C850`, selbst nicht in meinem Revier, aber der Rahmen
für vier Funktionen daraus):

```
0x41C85A  bruecken_alle_loeschen()            -> 0x41C680
0x41C866  Block C (484 B) auf 0 setzen        rep stosd 0x79 dwords ab 0x542148
0x41C872  Block A (2000 B) auf 0 setzen       rep stosd 0x1F4 dwords ab 0x542330
0x41C87F  kantentafel_bauen(0)                -> 0x41C6C0
0x41C88C  242 B kopieren 0x542148 -> 0x54223A ; Ebene 0 wird die »unversehrte« Ebene 1
0x41C895  bruecken_absuchen()                 -> 0x41C490
danach     Name bauen und mit "wb" schreiben
```

⭐ Damit ist die Reihenfolge belegt, die AX beschreibt, aber nicht herleiten
konnte: **Ebene 1 ist die Kantentafel OHNE Brücken.** Sie entsteht, bevor
irgendeine Brücke gesetzt wird. Und `0x54223A = 0x542148 + 242` ist die
Adresse der zweiten Ebene, auf das Byte genau.

#### 1.1 `0x41C6C0` — wie eine Sektorkante entsteht

```
kantentafel_bauen(art):
    karte_vorbereiten(art)                    ; 0x41BD60, ausserhalb
    für sx = 0…9:  für sy = 0…9:              ; ⚠ 10×10, nicht 11×11
        i = 11·sx + sy
        Anker = 0x541F60[2i]                  ; (Spalte, Zeile), 0xFF = Sektor leer
        wenn Anker.x == 0xFF: weiter
        byte[0x6F572A] = Anker.x ; byte[0x6F572B] = Anker.y   ; Startzelle der Suche
        für Nachbar in { i+11 (Sektor +X), i+1 (Sektor +Y) }:
            wenn Nachbar-Anker.x == 0xFF: weiter
            ok = BFS_1x1(999, NachbarAnker.x, NachbarAnker.y, 0)     ; 0x4D2580
            byte[0x542148 + 2i + richtung] = (ok && word[0xBCA0E0] < 0x50) ? 1 : 0
```

⭐ **Eine Sektorkante gilt als offen, wenn die 1×1-Breitensuche von Anker zu
Anker einen Weg von weniger als 80 Schritten findet.** `0xBCA0E0` ist die
schon aus BB bekannte »Schrittzahl des letzten Weges«, `0x4D2580` die dort
gelesene 1×1-BFS, `999` die Platzhalter-Einheitennummer.
Die 10×10 statt 11×11 ist richtig: Kanten laufen nur nach +X und +Y, Spalte
bzw. Zeile 10 hat keine.

#### 1.2 `0x41C490` — die ganze Karte nach Brücken absuchen

```
für X = 0 … Breite−1:  für Y = 0 … Höhe−1:
    v = brueckenvorschau(X, Y, 0)             ; 0x4CCCB0, Abschnitt BB.7
    wenn v == 0: weiter
    r = v % 8 ; k = v / 8
    wenn r in 1…4:  bruecke_setzen(...)       ; 0x4CC280, je nach r vier Fälle
    Fortschritt: "." bei (5·n+20, 400)        ; 0x4C81B0
    brueckenregel_erzeugen(X, Y)              ; 0x41C230
    erase_bridge(sec20[X,Y] − 100)            ; 0x4CB0A0 — Brücke wieder weg
```

⭐⭐ **Der Rückgabewert von `0x4CCCB0` ist damit entschlüsselt** — und zwar
ohne Rateanteil, denn beide Enden rechnen. Bei `0x4CD020` steht

```
lea eax, [eax + ecx*8] ; sub al, 7      ; eax = Musterindex 0…3, ecx = Länge L
```

also `v = 8·L + idx − 7 = 8·(L−1) + (idx+1)`. Der Leser in `0x41C490` bildet
`r = v % 8`, `k = v / 8` und schaltet über eine **Sprungtafel mit genau vier
Einträgen** (`0x41C608`), Index `r−1`. Das trifft die vier Einträge der
Richtungstafel `0x4CD670` in `0x4CCCB0` Zahl für Zahl:
**`r = Richtung + 1` (1…4), `k = Länge − 1`.**

Die vier Fälle setzen die Brücke (Aufruf `0x4CC280(x0, y0, achse, k, 1)`):

| r | Achse | Startzelle |
|---:|:---:|---|
| 1 | 1 (senkrecht) | `(X−1, Y−k−1)` |
| 2 | 1 (senkrecht) | `(X−1, Y)` |
| 3 | 0 (waagerecht) | `(X, Y−1)` |
| 4 | 0 (waagerecht) | `(X−k−1, Y−1)` |

#### 1.3 `0x41C230` — wann eine Brückenregel entsteht

```
brueckenregel_erzeugen(X, Y):
    sx0 = X/24 − 1 ; sy0 = Y/24 − 1           ; div cl mit cl = 0x18: Sektorkante 24
    für sx = sx0 … sx0+2:  für sy = sy0 … sy0+2:      ; 3×3-Sektorumfeld
        wenn sx oder sy ausserhalb 0…10: weiter
        i = 11·sx + sy
        Startzelle = 0x541F60[2i]  (0xFF -> weiter)
        für richtung in {0 (Nachbar +11), 1 (Nachbar +1)}:
            offen_jetzt = BFS(999, NachbarAnker) && word[0xBCA0E0] < 0x50
            wenn byte[0x542148 + 2i + richtung] != offen_jetzt UND offen_jetzt == 1:
                probr_anlegen(X, Y, sx, sy, richtung)     ; 0x41C180
```

Also: **eine Regel entsteht nur für die Kanten, die es OHNE die Brücke nicht
gäbe.** Das ist genau die Semantik, die AX für Block A beschreibt — hier steht
sie im Erzeuger.

`0x41C180` (`probr_anlegen`) ist der Zuteiler dazu: erster Platz mit
`byte[0x542330 + 5i] == 0`, 400 Plätze, sonst
`meldung("Cannot add more probr structures")`. **Der Name »probr« kommt aus dem
Spiel selbst** (`0x4F8148`).

#### 1.4 `0x41CAD0` / `0x41CA40` — `restore` und `apply`, Zeile für Zeile

```
restore():                                    ; 0x41CAD0, 11 Befehle
    rep movsd 0x3C dwords + movsw   0x54223A -> 0x542148   ; 60·4+2 = 242 = 121·2
    apply()

apply():                                      ; 0x41CA40
    für i = 0 … 399:
        r = 0x542330 + 5i
        wenn r[0] == 0: weiter
        wenn byte[0x542E18 + (r[0]<<8) + r[1]] in 100…199:
            byte[0x542148 + 2·(11·r[2] + r[3]) + r[4]] = 1
```

⭐ Der geschriebene Wert ist **1** = offen; `restore` setzt vorher alles auf
die brückenfreie Fassung zurück. Und die drei Rufer sagen, wann das läuft:
`0x41CD17` (Kartenladen), `0x4CB889` (**Brücke zerstören**), `0x4CCA8F`
(**Brücke bauen**). ⭐ **Eine gesprengte Brücke schliesst die Sektorkante der
KI-Wegekarte sofort wieder** — das ist keine Vermutung mehr, es sind drei
Aufrufstellen.

---

### 2. ⭐⭐ Der Zellensatz der Karte — vollständig, mit allen zwölf Zugriffen

`dword[0x677E20]` zeigt auf `Breite · Höhe · 4` Byte. Der Satz:

| Versatz | Breite | Inhalt | Leser | Schreiber |
|---|---|---|---|---|
| `+0` | u16 | **Kachelnummer** | `0x41D090` | `0x41D140` |
| `+2` | u8 | **Höhe** (`terrain_at`) | `0x41D0E0` | `0x41D170` |
| `+3` | u8 | **Brückenaufbau** (siehe 3.) | `0x41D110` | `0x41D1A0` |

Index überall `4·(Zeile·dword[0x542DC4] + Spalte)` — **zeilenweise mit
Schrittweite Kartenbreite**, nicht der imap-Index. (BB.8 Punkt 3 warnt davor;
die Warnung ist richtig.)

⭐ **Nullmodell für »das sind alle Zugriffe«:** `reloc_refs --addr 0x677E20`
liefert **12 Relokationen, 1 Schreiber, 11 Leser**, und alle zwölf liegen
zwischen `0x41CED1` und `0x41E37F`. Es gibt keinen Weg in das Feld hinein, der
nicht durch diese Handvoll Funktionen geht:

* `0x41CED0` — **anlegen**: altes Feld freigeben (`0x4D8390`), `W·H·4` holen
  (`0x4D7820`), `[0x542DC4] = W`, `[0x542DF8] = H`, dann drei volle
  Kartendurchläufe (`set_cell(x,y,0,arg3,0)`, `0x4ACDA0`, `0x4ACEB0`).
* `0x41CFE0` `set_cell(x,y,u16,u8,u8)` und `0x41D040`
  `get_cell(x,y,&h,&b) → u16` — beide merken den Zellzeiger in
  `dword[0x542E04]` zwischen.
* `0x41D090` (u16 lesen), `0x41D0C0` (imap-Wort aus `0x5539D0`),
  `0x41D0E0`, `0x41D110`, `0x41D140`, `0x41D170`, `0x41D1A0`.
* `0x41D339` — **Spielstand schreiben** (`fwrite(ptr, 1, 4·W·H, f)`).
* `0x41E37F` — **Spielstand lesen** (`fread` derselben Grösse).

⚠ Das heisst auch: `+2` und `+3` kommen beim Laden **als Block** aus der
Spielstandsdatei. Der Satz »alle Schreiber sind Brückencode« gilt für die
**Laufzeit**, nicht für den Dateiinhalt.

---

### 3. ⭐⭐ Das Byte `+3` ist der BRÜCKENAUFBAU — Antwort auf BB.8 Punkt 3

> BB.8.3: »Das Geländebyte `[0x677E20] + 4·(Zeile·Breite + Spalte) + 3`
> (via `0x41D110`), das die Arten 4…7 als hartes Sperrkriterium benutzen, ist
> **nicht identifiziert**.«

Es ist identifiziert. **Alle 32 Schreibstellen von `0x41D1A0` liegen im
Brückenmodul:**

| Wo | Stellen |
|---|---:|
| `0x421F90` (Geländer stempeln, **in diesem Revier**) | 12 |
| `0x4CB0A0` `Erase bridge` (`0x4CB236`…`0x4CB7C8`) | 16 |
| `0x4CC280` Brückenplatz vergeben (`0x4CC6DC`, `0x4CCA5C`, `0x4CCA6E`) | 3 |
| `0x4D639F` (Einzelfall im C-Laufzeitbereich) | 1 |

Dazu genau **ein** weiterer Schreiber überhaupt: `0x41CFE0` mit dem Wert `0`,
gerufen nur aus dem Kartenanleger `0x41CED0`.

> ⭐ **Nullmodell:** wäre `+3` ein allgemeines Geländemerkmal, lägen seine
> Schreiber verstreut (Lader, Editor, Terraforming, Missionsskript). 31 von 32
> liegen in **zwei benachbarten Rümpfen**, die zusammen rund 5 kB von 861 kB
> ausmachen — 0,6 % des Bildes.

#### `0x421F90` — was genau gestempelt wird

```
für i = 0 … 99:
    Platz = 0xBFEA80 + 24i
    wenn Platz[+0x12] == 0: weiter          ; Platz frei
    x = Platz[+0] ; y = Platz[+1] ; L = Platz[+0x13] ; achse = Platz[+2]

    achse == 0  (WAAGERECHTE Brücke, Zeilen y und y+2, Spalten x … x+L+1):
        (x,      y  ) = 9      (x,      y+2) = 12
        (x+L+1,  y  ) = 10     (x+L+1,  y+2) = 11
        (x+1 … x+L, y) = 2     (x+1 … x+L, y+2) = 4

    achse != 0  (SENKRECHTE Brücke, Spalten x und x+2, Zeilen y … y+L+1):
        (x,   y    ) = 9       (x+2, y    ) = 10
        (x,   y+L+1) = 12      (x+2, y+L+1) = 11
        (x,   y+1 … y+L) = 1   (x+2, y+1 … y+L) = 3
```

⭐ **Die Zahlenordnung ist selbsterklärend:** 1…4 sind die vier geraden
Geländerstücke (1 links senkrecht, 2 oben waagerecht, 3 rechts senkrecht,
4 unten waagerecht), 9…12 die vier Ecken. **Die mittlere Zeile bzw. Spalte —
die Fahrbahn — bleibt auf 0.**

Damit passt die Sperrregel aus BB.1 zusammen: die Arten 4, 5, 6, 7 (und 12, 13)
verlangen »Geländebyte 0« als *frei*. **Sie können nicht auf das Geländer,
nur auf die Fahrbahn.** Das ist die Bedeutung des Kriteriums.

`+3` hat eine **zweite** Aufgabe: es ist der letzte Index (`C`, 0…18) in die
Bodenkacheltafel — siehe 4. Ein Geländerstück bekommt also automatisch sein
eigenes Bild. Dieselbe Zahl trägt Bild **und** Sperre.

Nebenbefund: `0x41C680` ist der Gegenspieler — läuft die 100 Brückenplätze ab
und ruft für jeden belegten `Erase bridge` (`0x4CB0A0`) mit dem **Platzindex**.

---

### 4. ⭐ Das Bodenbild sec51 und die Kacheltafel `0xBAA800`

`0x41FAE0` baut sec51 (`0x5539D0`, 65 536 × u16, »das Bodenbild, das der
Spieler sieht«) für die ganze Karte:

```
für x = 0 … W−1:  für y = 0 … H−1:
    c = byte[0x542E18 + (x<<8) + y]                  ; sec20, die Lagentafel
    kachel = (c < 100) ? bodenkachel(x, y)           ; 0x41FA10
                       : word_der_zelle(x, y)        ; 0x41D090, Zellensatz +0
    word[0x5539D0 + 2·((x<<8)+y)] = kachel
```

⭐ **Trägt die Zelle ein Bauwerk (sec20 ≥ 100 — nach W ist 100…199 eine
Brücke), wird das im Zellensatz gespeicherte Kachelwort genommen; sonst wird
die Bodenkachel gewürfelt.**

#### `0x41FA10` — die Kachel würfeln

```
bodenkachel(x, y):
    code = ecken_lesen(x, y, &A)               ; 0x4ACDE0 (Nachbar von set_corner 0x4ACDA0)
    B = Index von code in der 16er-Tafel 0x4F89F8      ; Marching Squares
    wenn B != 0: A += 4
    C = zelle(x,y).+3                          ; 0x41D110
    satz = 0xBAA800 + 4·(19·(15·A + B) + C)
    wenn satz.zahl == 0: return 0xFFFF
    return satz.erste + rand() % satz.zahl     ; rand = 0x4D6C70, MSVC
```

Satz: `u16 erste Kachel; u8 Zahl der Spielarten; u8 unbenutzt`.

⭐ **Drei unabhängige Zahlen gehen auf:**

1. Die Tafel `0x4F89F8` enthält als 16 Dwords genau
   `0, 101, 11, 1010, 1100, 1, 10, 1000, 100, 111, 1011, 1110, 1101, 110, 1001, 1111`
   — **alle 16 Vierbitmuster als Dezimalziffern**. Das ist der »Vierziffern-
   Code« aus dem Nebelabschnitt, unabhängig aus der EXE gelesen.
2. Der Lader `0x4C8DAD` macht `fread(0xBAA800, 1, 0x23A0, f)`.
   **0x23A0 = 9120 = 4 · 19 · 15 · 8.** Die Tafel ist also `u32 [8][15][19]`,
   und die Indexformel oben passt auf das Byte.
3. `0xBAC72C − 0xBAA800 = 7980 = 4 · 19 · 15 · 7`. ⭐⭐ **Die »Nebeltafel«
   `0xBAC72C` ist keine eigene Tafel — sie ist dieselbe Tafel bei `A = 7`.**

Das erklärt `0x41FC60` und `0x41FD00` in einem Satz:

```
nebelkachel(x, y):                                   ; 0x41FC60
    code = ecken_lesen_nebel(x, y, &_)               ; 0x41FB90
    B = Index von code in 0x4F89F8
    wenn B == 0 und byte[0x678B58 + (x<<8)+y] == 2: return 0xFFFF   ; ganz frei
    return word[0xBAC72C + 4·(19·B + zelle.+3)]

nebelkachel_voll(x, y):                              ; 0x41FD00
    B = erster Nulleintrag in 0x4F89F8  ( = 0 )
    return word[0xBAC72C + 4·(19·B + zelle.+3)]
```

⚠ `B` läuft 0…15, die Schrittweite ist aber **15**. Für `B = 15` (Muster 1111)
greift die Rechnung in den ersten Satz von `A+1`; bei `A = 7` (Nebel) liegt sie
hinter dem Feldende. Ob das je vorkommt, ist **nicht gemessen** — gemeldet als
Rechnung, nicht als Fehler.

---

### 5. ⭐ Ein Gebäude wird als GANZES aufgedeckt (`0x41FE20`)

Zwei Rufer, beide im Nebelmodul: `0x41FF50` (der »Saum«) und `0x4200C0`.

```
gebaeude_aufdecken(g):                     ; g = Platz in sec3 (0xC06910, 76 B)
    byte[0x677E30 + g] = 0
    x0 = sec3[g].+0 ; y0 = sec3[g].+2 ; typ = sec3[g].+4
    v = word[0xBB41A2 + 10·typ]                       ; Grundriss-Kennung
    für j = 0…5:  für k = 0…9:
        wenn word[0xB97B38 + 2·(j + 6·(k + 15·v))] == 0: weiter
        zelle = ((x0+k) << 8) + (y0+j)
        word[0xC0C220 + 2·zelle] = word[0xBDEA80 + 2·zelle]   ; sec52 <- sec6
        word[0x5539D0 + 2·zelle] = word_der_zelle(x0+k, y0+j) ; sec51 <- Kachel
        byte[0x689710 + zelle]   = byte[0x542E18 + zelle]     ; Erinnerung <- sec20
```

⭐ Die Grundrissrechnung `j + 6·(k + 15·v)` ist **Zeichen für Zeichen** die,
die `GAMESTATE_RE.md:2282` unabhängig notiert hat
(`word[+ 2·(6·(15·shape + col) + row)]`). Zwei Lesungen aus zwei Richtungen,
dasselbe Ergebnis — das ist das Nullmodell für diesen Punkt.

**Wozu:** wird eine einzige Zelle eines Gebäudes sichtbar, kommt der ganze
Grundriss in die drei Erinnerungsgitter. Ein Gebäude blinkt nicht kachelweise
auf.

---

### 6. ⭐ Sicht im Kreis LÖSCHEN (`0x4203A0`)

Das Gegenstück zum bekannten Stempler `0x4205B0`, mit derselben Sehnentafel:

```
sicht_loeschen(spalte, zeile, r):
    r = min(r, 19)
    oben = max(zeile − r, 0) ; unten = min(zeile + r, H−1)
    für z = oben … unten:
        w = word[0x4F8A48 + 2·(20·r + k)]             ; k = Zeilenlaufindex
        li = max(spalte − w, 0) ; re = min(spalte + w, W−1)
        für s = li … re:
            wenn byte[0x678B58 + (s<<8) + z] != 0:
                byte[0x678B58 + (s<<8) + z] = 0
                byte[0x677E28] = 1                    ; »Bild ist schmutzig«
```

⭐ **Nullmodell für die Grösse der Sehnentafel:** die Rechnung `20·r + k`
erlaubt r ≤ 19 (harte Klemme `cmp …,0x13`), also 20 × 20 × 2 = **800 Byte** —
und `0x4F8A48 + 800 = 0x4F8D68`, was unabhängig davon der **Wind** ist
(Abschnitt 8). Die Tafel endet auf das Byte, wo die nächste beginnt.

`byte[0x677E28]` hat im ganzen Bild genau **drei** Zugriffe: gesetzt hier,
gelöscht `0x420A61`, gelesen `0x420AED` — eine Einwegmarke im Nebelmodul.

---

### 7. ⭐ Lagerstätten: der zweite Setzer von sec38 `+0x06+p` (`0x420FC0`)

Abschnitt AM.2 sagt: »`+0x06+p` heisst ›Spieler p sieht dieses Vorkommen‹, und
**der Zuteiler setzt nur das Byte des Aufschliessers**.« Das ist unvollständig.
Es gibt einen zweiten, **laufenden** Setzer:

```
wenn dword[0x4FA240] % 30 == 17:                      ; Takt seit Missionsbeginn
    für i = 0 … 49:                                   ; sec38, 14 B je Satz
        Satz = 0x6783E8 + 14i
        wenn Satz[+0] == 0: weiter
        für sp = Satz[+1]−4 … +8:  für ze = Satz[+2]−4 … +8:   ; 9×9-Kasten
            h = word[0xBDEA80 + 2·((sp<<8)+ze)]                ; sec6
            wenn h < 8000:
                byte[Satz + 6 + h/1000] = 1
            sonst wenn h < 14000:
                für n = 0…8:
                    e = word[0x7847E8 + 22·(h−10000) + 4 + 2n]
                    wenn e != 0xFFFF: byte[Satz + 6 + e/1000] = 1
```

⭐ **Also: wer mit einer Einheit in den 9×9-Kasten um ein aufgeschlossenes
Vorkommen fährt, sieht es fortan — nicht nur der, der es aufgeschlossen hat.**
Und der Setzer läuft nur alle 30 Takte, auf Phase 17.

⭐⭐ **Nebenbei ist der Griffraum von sec6 hier ein weiteres Mal unabhängig
bestätigt:** `h/1000` ist die Spielernummer (8 Spieler × 1000 Stück),
`h − 10000` der Index in die Infanteriezellen `0x7847E8`. Und dort liegen die
**neun** Einträge ab Versatz **+4**, nicht +2 — siehe Berichtigung B4.

---

### 8. ⭐⭐ Der Wind (`0x422210`) — und eine Klammer, die die falsche Grösse fasst

`0x4F8D68` (Richtung) und `0x4F8D6C` (Stärke) sind in `OFFENE_FRAGEN.md`
bekannt (Waldbrandausbreitung, Rauchdrift, Kompassnadel), aber **ihr Schrittwerk
war es nicht.** Es ist diese eine 176-Byte-Funktion:

```
wenn dword[0x4FA240] % 2000 == 1111:
    Richtung += (zufall_det() % 3) − 1                 ; 0x4C5B30, DETERMINISTISCH
    wenn Richtung == 0xFF: Richtung = 7
    wenn Richtung == 8:    Richtung = 0                ; sauberer Ringschluss 0…7

    Staerke  += (zufall_det() % 3) − 1
    wenn Staerke == 0xFF:  ⚠ RICHTUNG = 0              ; @0x422277
    Staerke wird geschrieben
    wenn Staerke == 10:    ⚠ RICHTUNG = 9              ; @0x422289
```

⚠⚠ **Die zwei Klemmen des zweiten Blocks schreiben die ERSTE Grösse.**
`0x4F8D6C` bekommt dadurch **überhaupt keine Grenzen** und läuft als Byte frei
0…255 um, während `0x4F8D68` auf 0 bzw. auf **9** gezwungen wird — und 9 liegt
ausserhalb seines eigenen Wertebereichs 0…7.

**Warum das ein Befund und keine Fehldeutung ist — die drei Halte:**

1. ⭐ **In F byteweise dasselbe.** C `0x422277` `C6 05 68 8D 4F 00 00` /
   F `0x421437` `C6 05 48 7D 4F 00 00`; C `0x422289` `… 09` /
   F `0x421449` `… 09`. Beide Fassungen fassen dieselbe falsche Grösse.
   (F-Windgrössen: `0x4F7D48` / `0x4F7D4C`.)
2. ⭐ **Die Nachbarzeile macht es anders.** Der erste Block klemmt korrekt auf
   `0x4F8D68`. Es ist derselbe Rumpf, sechs Zeilen weiter oben.
3. ⭐ **Der Anfangswert widerspricht.** Der Setzer beim Missionsstart
   (`0x41A17A` / `0x41A189`) schreibt `Staerke = zufall%? + 2` und
   `Richtung = zufall() & 7`. **Die Richtung ist ausdrücklich 0…7.**

**Was daraus folgt** — `reloc_refs` findet je 9 Leser, drei davon ungemaskt:

* `0x4ADE35`: `movsx eax, word[eax*4 + 0x5040E0]` mit `eax` = Richtung. Die
  Windtafel hat **acht** Einträge. Mit Richtung 9 liest sie 4 Dwords dahinter.
* `0x4ADE3D`: `imul eax, ecx` mit `ecx` = Stärke — die **ungeklemmte** Stärke
  skaliert die Rauchdrift unmittelbar.
* `0x4CA873`: Richtung geht ungemaskt in den Winkelvergleich der
  Waldbrandausbreitung.
* `0x43983E`: `wenn Staerke <= 1: keine Drift` — die einzige Untergrenze, die
  es überhaupt gibt.

Die Kompassnadel (`CAB_ASSETS_RE.md:118`) maskiert mit `&7` und bleibt heil.

⚠ **Wie oft:** ein Schritt je 2000 Takte (bei SimHz 50 also rund 40 s). Von 2
aus braucht die Irrfahrt zwanglos zehn Minuten bis zur 10. Ich habe das
**nicht** gemessen — die Aussage ist eine Rechnung über den Schrittzähler,
kein Prüfstandsergebnis.

---

### 9. ⭐ Minen, Fallen und Radare — die vier Zugreifer

`0x552E18` = sec84 Minen (500 × 6), `0x688B58` = sec85 Fallen (500 × 6),
`0x677F30` = sec86 Radare (200 × 6). Satz: `+0` X, `+1` Y, `+4` belegt,
`+5` Besitzer.

| Funktion | Was |
|---|---|
| `0x421C40(x,y,p)` | 1, wenn auf (x,y) eine **Mine oder Falle** liegt, deren Besitzer mit `p` **verbündet** ist (`byte[0x87B155 + 40p + q] != 0`) |
| `0x421D40(x,y)` | 1, wenn auf (x,y) **irgendetwas** aus einer der **drei** Tafeln liegt, Besitzer egal |
| `0x421E10(x,y,p)` | die erste verbündete **Mine** auf (x,y) freigeben und **Klang 40** an der Kartenstelle auslösen |
| `0x421ED0(x,y,p)` | dasselbe für **Fallen**, **Klang 41** |

⭐ Damit tragen zwei bisher namenlose Zeilen in `KLAENGE.md` einen Namen:
**Nr. 40 = Mine wird geräumt** (einzige Aufrufstelle `0x421E95`),
**Nr. 41 = Falle wird geräumt** (`0x421F55`). Beide Modus 2 (Kartenstelle).

⚠ Bemerkenswert: `0x421C40`/`0x421E10` prüfen auf **Bündnis**, nicht auf
Feindschaft. Das Räumen betrifft also die *eigenen* Minen.

---

### 10. ⭐ Der Kartenrand wird nach Geländehöhe gesperrt (`0x422BD0`)

```
; 1. die unteren 18 Zeilen
für x = 0 … W−1:  für y = H−18 … H−1:
    g = terrain_at(x, y) · 15                         ; 15 Bildpunkte je Höhenstufe
    w = g + 20·(H − y)
    wenn x < 5: w −= word[0x4F8E80 + 2x]              ; 170, 170, 170, 135, 64
    wenn w < 140: sec6[(x<<8)+y] = 0xFFFF

; 2. die Sperrspalte
für y = 0 … H−1:  sec6[(W<<8)+y] = 0xFFFF             ; Spalte W, ausserhalb der Karte

; 3. die obersten 10 Zeilen
für x = 0 … W−1:  für k = 0 … 9:
    wenn terrain_at(x,k)·15 − 20k > 0: sec6[(x<<8)+k] = 0xFFFF
```

⭐ Der Faktor **15** ist derselbe, den `AIR_RE.md` für die Flughöhe nennt
(`Gelände·15 + Zuschlag`) und den `0x425CB0`, `0x426E40` und `0x428350` in
diesem Revier ebenfalls benutzen. Er kommt hier zum fünften Mal unabhängig vor.

Zweck: der Kartenrand wird nicht pauschal gesperrt, sondern **wo das Gelände
zu tief liegt**, und die erste Sperrspalte liegt bewusst **ausserhalb** der
Karte, als Wache.

---

### 11. ⚠ Fünf leere Funktionen mit 768 Aufrufstellen — und eine sechste ohne Leser

| Adresse | Byte | Aufrufstellen | Inhalt |
|---|---:|---:|---|
| `0x41F9E0` | 16 | 2 | `C3` |
| `0x41F9F0` | 16 | **413** | `C3` |
| `0x41FA00` | 16 | **157** | `C3` |
| `0x422D80` | 16 | **195** | `C3` |
| `0x422DA0` | 16 | 1 | `C3` |

Roh nachgesehen: an jeder der fünf Adressen steht `C3`, davor und danach
`CC`-Füllung. Das sind keine abgebrochenen Zerlegungen, sondern wirklich leere
Rümpfe.

Die Rufer liegen fast alle in `0x405xxx`…`0x40Cxxx` — **Einheitenbewegung und
-simulation**. ⚠ Als Vermutung markiert: das sind mit einiger
Wahrscheinlichkeit wegkompilierte Protokoll- oder Prüfhaken der
Entwicklungsfassung; was sie taten, steht nirgends im Bild.

Dazu `0x422D90` (12 Rufer, alle im UI-Bereich `0x415D36`…`0x417ECA`): sie
schreibt `dword[0x542DF4] = 0`. ⭐ `reloc_refs --addr 0x542DF4` findet im
ganzen Bild **genau eine** Relokation — eben diesen Schreiber. **Kein Leser.**
Zwölf Aufrufstellen für eine Grösse, die niemand ausliest.

---

### 12. ⭐ Der Luftkrieg — sieben Funktionen, eine Kette

Alle rechnen auf sec19 (`0x6DDF70`, 68 B je Flugzeug, 200 Plätze) und benutzen
die Felder aus `AIR_RE.md`. **Neu:** `+0x04`/`+0x06` = Feinlage X/Y
(in `0x426E40`).

#### 12.1 Drehen und Steigen

* `0x422DB0` / `0x422DF0`: `dir += 6` bzw. `dir −= 6`, Ringschluss bei **360**.
  ⭐ Also **60 Blickrichtungen zu 6°**. Dass `dir` Grad trägt, ist unabhängig
  belegt: `0x426E40` teilt es durch die Gleitkommakonstante bei `0x4F9208`,
  und die ist **57,2958f = 180/π**.
* `0x425FE0`: `wenn Stufe_oben (+0x0D) > Stufe (+0x0C): Stufe++`.

#### 12.2 `0x425CB0` — schlägt das Flugzeug auf? Acht Zweige

```
g = terrain_at(X, Y) · 15 ; h = alt (+0x0E) ; s = sec20[X,Y] ; d = sec6[X,Y]
s in 1…59                            -> Treffer wenn g + 50 >= h
d in 0xFFFC…0xFFFE                   -> Treffer wenn g +  2 >= h
d <  8000   (Einheit)                -> Treffer wenn g + 10 >  h
d < 14000   (Infanteriezelle)        -> Treffer wenn g +  5 >  h
s == 98 und d in 60000…60299 (Geb.)  -> Treffer wenn g +  2 >= h
s != 98                              -> Treffer wenn g + 50 >  h
sonst                                -> kein Treffer
```

⭐ Dieselbe Bauform wie die »acht Einschlagszweige mit eigenen Höhenschwellen«
der Geschosse (Abschnitt B von `OFFENE_FRAGEN.md`) — hier für Flugzeuge, mit
eigenen Zahlen.

#### 12.3 `0x426E40` / `0x426D30` — das Bombenziel

```
bombenziel(f, &zx, &zy):
    wenn Munition (+0x16) == 0: return 0
    h = alt − 15·terrain_at(X, Y)                       ; Höhe über Grund
    r = 2·h
    n = r / 120                                         ; Zahl der Ringe
    tx = (40·X + Feinlage_X + round(r·cos(dir/57,2958))) / 40
    ty = (40·Y + Feinlage_Y + round(r·sin(dir/57,2958))) / 40
    für ring = 0 … n:
        für j = 0 … 11:
            (dx,dy) = 0x4F9210[48·ring + 4j] ; wenn dx == 10: Ring zu Ende
            t = zellenwert(f, tx+dx, ty+dy)             ; 0x426D30
            t == 1 -> SOFORT: *zx = 40·(tx+dx), *zy = 40·(ty+dy), return 1
            t == 2 -> merken, falls noch nichts gemerkt
    gemerktes zurückgeben, sonst 0
```

```
zellenwert(f, x, y):                                    ; 0x426D30
    wenn uk (+0x10) != 3: return (Flugziel_X == x && Flugziel_Y == y)
    h = sec6[x,y]
    h < 8000        : return byte[0x87B155 + 40·f.Besitzer + h/1000] == 0   ; Feindeinheit
    h in 60000…60299: B = byte[0x7AD495 + 76·h]
                      return (byte[0x87B155 + 40·f.Besitzer + B] == 0) ? 2 : 0
    sonst           : 0
```

⭐ **Eine feindliche EINHEIT gewinnt sofort, ein feindliches GEBÄUDE ist nur
Rückfallposition.**

⭐⭐ **Nullmodell für die Ringtafel:** aus der EXE gelesen enthält `0x4F9210`
Ring 0 = `{(0,0)}` (Ende `(10,10)`), Ring 1 = die acht Nachbarn (Ende `(10,0)`),
Ring 2 = zwölf Einträge ohne Ende — **danach kommt Text.** Die Tafel hat also
genau **drei** Ringe. Und `0x6DDF83` (Sollhöhe) wird in diesem Revier zweimal
auf **0x87 = 135** gesetzt (`0x427EAA`, `0x427F5A`), was `n = 2·135/120 = 2`
ergibt — **den letzten vorhandenen Ring, auf den Eintrag genau.** ⚠ Fliegt ein
Flugzeug jemals mehr als 180 Punkte über Grund, liest die Schleife hinter die
Tafel; die Werte werden aber als Byte genommen, es stürzt nichts ab. Gemeldet
als Kante, nicht als Fehler.

#### 12.4 `0x427E30` / `0x427EE0` — Angriffsbefehl an ein Flugzeug

```
angriff(f, ziel):                     ; 0x427E30 -> uk 10 ; 0x427EE0 -> uk 11
    klang(75, 1, 20000 + f, 0)                        ; 0x4047E0
    Flugziel_X (+0x14) = Einheit[ziel].RX
    Flugziel_Y (+0x15) = Einheit[ziel].RY
    uk (+0x10) = m_uk (+0x11) = 10 bzw. 11
    Sollhoehe (+0x13) = 0x87
    Ziel (+0x2E) = ziel
```

⭐ **Klang 75** ist damit benannt: er hat in `KLAENGE.md` genau die zwei
Aufrufstellen `0x427E50` und `0x427F00` — beide hier. **»Flugzeug nimmt ein
Ziel auf.«**

#### 12.5 `0x427F90`, `0x4281B0`, `0x427930` — Luft gegen Luft

Beide Sucher benutzen dieselbe Ausschlussliste: Platz belegt, **nicht**
derselbe Besitzer, und `uk` **nicht** in {0, 2, 4, 100}. Bei `0x4281B0` steht
diese Liste als Bytetafel `0x428290` (`uk` → 0…4) über einer Dword-Sprungtafel
`0x42827C` (fünf Einträge, vier davon »überspringen«). **Beide Tafeln sind in
C und F Byte für Byte gleich** (einzeln nachgeschlagen, nicht gerechnet).

`0x4281B0(f, a)`: nur wenn das eigene Flugzeug Munition (`+0x16`) hat und die
Abklingzeit (`+0x2B`) 0 ist; findet es ein zulässiges Feindflugzeug, für das
`0x426580(a, b)` wahr ist, ruft es `0x427090(a, b)` und hört auf.

`0x427F90(f, a)`: sucht das Flugzeug mit dem kleinsten Wert von
**`(eigenX + eigenY) − (fremdX + fremdY)`**, und wenn dieser Wert < 20 ist,
sendet es **Netzbefehl 6** über `0x427930`.

⚠⚠ **Dieses Abstandsmass ist keines.** Es ist vorzeichenbehaftet, der
Startwert ist 10000, und jeder negative Wert gewinnt. Gewählt wird also nicht
das nächste, sondern das in +X+Y-Richtung am weitesten voraus liegende
Flugzeug. **In F byteweise dasselbe** (`mov bx, si; sub bx, [+2]; sub bx, [+0]`
bei `0x4271F7`). Ich habe **kein** Nullmodell dafür, ob das gewollt ist —
gemeldet als auffällig, nicht als Fehler.

`0x427930(a, b, c, d)` schreibt in den Kratzbefehl `0xB8A3D8`:
`+0 = 6` (die Befehlsnummer), `+8 = a`, `+0xA = b`, `+0xC = c`, `+0xE = d`,
und springt in »Befehl absenden« (`0x4C2190`). ⭐ **Netzbefehl 6 mit vier
Wörtern**; die fünf Rufer sind `0x428062` (hier), `0x4355A0`, `0x436520`,
`0x4375A0`, `0x43778E`.

---

### 13. ⭐⭐ Die Flugabwehr vom Boden (`0x428350` + `0x428580`)

```
flak(u):                                    ; u = Einheitengriff (1000·Spieler + Nummer)
    p  = u / 1000
    g  = terrain_at(RX, RY) · 15
    für b = 0 … 199 (Flugzeuge):
        Platz frei / verbündet / uk in {0,2,4,100}  -> weiter
        dz = |alt(b) − g| / 13
        d  = round( sqrt( (RX − X(b))² + (RY − Y(b))² ) )       ; fild/fsqrt/ftol
        wenn d > dz:      weiter                                 ; zu weit
        wenn dz/3 > d:    weiter                                 ; zu nah
        ; Treffer:
        OTOC_HLAVEN (+0x17) = richtung_zu(u, X(b), Y(b))         ; 0x401BE0
        wenn NABYTO (+0x32) == 0:
            check_aa_anlegen(u, b)                               ; 0x428580
            NABYTO = zufall_det() % 6 + 12                       ; 12…17 Takte
        aufhören
```

⭐ **Die Bedingung `dz/3 ≤ d ≤ dz` ist ein Kegel:** eine Bodeneinheit erwischt
ein Flugzeug nur in einem Ring um sich herum, dessen Radius zwischen einem
Drittel und dem Vollen der (durch 13 geteilten) Höhendifferenz liegt.
Senkrecht über der Flak ist tote Zone.

`0x428580(u, b)` legt den Auftrag in `0x6E1498` an: erster Platz mit
`byte[+7] == 0`, dann `word[+0] = u`, `byte[+2] = b`, `+3…+6 = 0`,
`byte[+7] = 4`. ⭐ Das ist die Tafel, die `OFFENE_FRAGEN.md:1839` als
**Station 26 »`Check AA`«, `0x6E1498`, 8 × 200, nicht gespeichert** führt —
und `0x428580` ist ihr Zuteiler. Der Wert `+7 = 4` ist die Lebensdauer.

Der Nachladewert **12…17** ist neu und kommt aus dem **deterministischen**
Würfel `0x4C5B30`, nicht aus `rand()`.

---

### 14. ⭐ Auswahl und Einheitentafel

`0x429170` — Vorbelegung nach dem Laden:

```
byte[0x6E26D1 + 78·k] = 0xFF   für k = 0 … 7999       ; faze (+0x09), leerer Platz
word[0x8106C6 + 10·n] = 0xFFFF für n = 0 … 1999       ; sec42
```

⭐ `(0x77AC51 − 0x6E26D1)/78 = 8000` auf das Byte — **8 Spieler × 1000
Einheiten**, unabhängig bestätigt. `0x8106C6` ist der Erzeuger von
Zeichenlistenart 0x08 (BD.1 Zeile 7), 2000 Sätze zu 10 Byte, und
`(0x8154E6 − 0x8106C6)/10 = 2000` stimmt.

`0x4291A0(u)` — **die Wichtigkeit einer Einheit**:

```
wenn u >= 8000: return 1
w = word[0x4FA4E8 + 2·Gattung(+0x0A)]
wenn Gattung == 1 und (SPODEK(+0x0B) >> 1) in {1, 3}: w += 8
return w + Rang(+0x28)
```

Die Tafel `0x4FA4E8` steht in der EXE: **20, 2, 0, 100, 10, 10, 0, 0** für
Gattung 0…7. ⭐ Gattung **3** schlägt mit 100 alles. Die Schiffe (Gattung 4/5
nach `ENTITY_FELDER.md`) liegen bei 10.

`0x429220` — läuft die Auswahlliste `word[0x833098 + 2i]`,
`i < word[0x4FA278]`, und gibt den Griff mit der grössten Wichtigkeit zurück
(`0xFFFF`, wenn nichts). ⭐ **Damit ist `word[0x4FA278]` als Füllstand der
Auswahlliste benannt** und die Auswahlliste selbst (Abschnitt AM.3,
`0x833098`, 2 × 1000) hat ihren Leser.

⚠ Weil der **Erfahrungsrang** in die Wichtigkeit eingeht, wechselt bei uns das
gezeigte Bild einer gemischten Auswahl nie so wie im Original — wir lassen den
Rang nicht wachsen (Abschnitt B von `OFFENE_FRAGEN.md`).

---

### 15. ⭐⭐ Die Verdeckungsprüfung im fertigen Bild (`0x42DD60`)

Sie läuft **nach** der Ausgabe über die ganze Zeichenliste — 70 Körbe, je 500
Einträge zu 10 Byte (die Zahlen sind in Abschnitt BD.2 belegt):

```
verdeckungspruefung(bildpuffer):
    für r = 0 … 69:
        n = word[0xAB93F0 + 5002·r]
        für e = 0 … n−1:
            E = 0xAB8068 + 5002·r + 10·e
            wenn E[+0] != 1: weiter                    ; nur Art 1
            u = word[E+2] ; x = i16 E[+6] ; y = i16 E[+8]
            Randprüfung: x+14 > 0, x+16 < Breite, y+35 > 0, y+37 < Höhe
            p = bildpuffer + (y+36)·Zeilenschritt + x + 15
            wenn p[0], p[+1], p[−1], p[+Zeilenschritt], p[−Zeilenschritt]
                 ALLE FÜNF in 0xF0 … 0xFD:
                    E[+0] = 2
                    Einheit[u].+0x22 = 1
            sonst:
                    Einheit[u].+0x22 = 0
```

⭐ Fünf Bildpunkte um `(x+15, y+36)` — die Mitte der Kachel, zwölf Punkte
unter dem Sprite-Anker. Die Palettenspanne **0xF0…0xFD** ist der
Farbwechselbereich. Die Funktion fragt also: **»ist von dieser Einheit im
fertigen Bild noch etwas zu sehen?«**
(`dword[0x5387C8]` = Zeilenschritt, `dword[0x5387CC]` = Höhe,
`dword[0xB136B0]` = Breite des Zielpuffers.)

⚠⚠ **Der fünfte fehlende `break` — und er ist wirkungslos.**

```
C 0x42DE42  test edx, edx / jl ende          ; edx wurde vorher genullt: TOTER ZWEIG
C 0x42DE46  cmp edx, 1 / jle 0x42DE52
C 0x42DE4B  cmp edx, 2 / je  0x42DE55
C 0x42DE50  jmp ende
C 0x42DE52  C6 07 02      mov byte[edi], 2
C 0x42DE55  C6 07 01      mov byte[edi], 1   ; <- KEIN Sprung dazwischen
```

Der Zweig für 0 und 1 schreibt eine **2**, die im nächsten Befehl von einer
**1** überschrieben wird. In **F** steht an `0x42D042`/`0x42D045` dieselbe
Folge `C6 07 02 / C6 07 01`, byteweise gleich.

⭐ **Der zweite Halt** (die Regel aus AX.8.9): `reloc_refs --addr 0x6E26EA`
zeigt fünf Schreiber. Vier davon (`0x4B1CA7`, `0x4B1CB0`, `0x4B3818`,
`0x4B3821`) schreiben **nur 0 und 1**. Das Feld ist anderswo eine reine
Ja/Nein-Marke; die **2** kommt im ganzen Bild nur an dieser einen, sofort
überschriebenen Stelle vor.

**Wirkung: keine.** Beide Zweige enden bei 1. Gemeldet, weil er dieselbe Form
hat wie die vier schon dokumentierten und damit belegt, dass die Fehlerklasse
in diesem Bau systematisch ist.

---

### 16. Der Rest, gerafft

* `0x422340` — läuft 200 Sätze zu 6 Byte bei `0x6786A8` (sec89/sec114) ab:
  `wenn word[+4] > 0: word[+4]−−; wenn byte[+2] > word[+4]: byte[+2]−−`.
  Ein Ablaufzähler mit nachlaufender zweiter Grösse. ⚠ Bedeutung offen.
* `0x428D60(flughafen, ausgabe)` — sec27 (`0x879438`, 50 × 52): geht die
  `byte[+4]` belegten Plätze ab `byte[+11]` durch, holt zu jedem Flugzeug
  `byte[sec19 + 0x26]` und setzt eine Marke; sammelt die gesetzten Marken 0…9
  in die Ausgabeliste. Danach sucht sie den **höchsten** der zehn Gruppensätze
  bei `0x833A00` (sec81, 4220 B = **10 × 422**, »Zehn Gruppen«), dessen
  Namenskette leer ist, und hängt ihn an. Rufer `0x445F27`, `0x449B8F`.
* `0x428F30` `datei_schreiben(pfad, puffer, n)` — `fopen(pfad,"w")`
  (Textmodus!), `fwrite(puffer,1,n,f)`, `fclose`.
  `0x428F80` `dateigroesse(pfad)` — `fopen/"rb"`, `fseek(0,SEEK_END)`, `ftell`.
  `0x428FD0` `datei_da(pfad)` → 1/0, **14 Aufrufstellen** quer durchs Bild.

---

### Berichtigungen an bestehenden Dokumenten

#### B1. ⚠⚠ `cfind.py` liegt bei `0x41D0E0` (`terrain_at`) falsch — Abschnitt BC hat recht

`cfind.py` meldet `0x41D0E0 → 0x41C250` (»mehrdeutig, 3 Kandidaten«).
**Richtig ist `0x41C2A0`**, wie in BC. Belegt durch Nachlesen im F-Code:

| F | erste Befehle | ist |
|---|---|---|
| `0x41C250` | `… mov ax, word ptr [edx + ecx*4]` | Kachelwort `+0` |
| `0x41C280` | `… mov ax, word ptr [ecx*2 + 0x552A30]` | imap-Wort (sec51) |
| **`0x41C2A0`** | `… mov al, byte ptr [edx + ecx*4 + 2]` | ⭐ **`terrain_at`** |
| `0x41C2D0` | `… mov al, byte ptr [edx + ecx*4 + 3]` | Brückenbyte (BB.0 nennt es schon so) |

⭐ **Nullmodell:** die ganze Familie hat in F einen **einheitlichen** Versatz
von −0xE40, und dieser ist an den *eindeutigen* Zuordnungen
`0x41D0C0 → 0x41C280`, `0x41D140 → 0x41C300` und `0x41D1D0 → 0x41C390`
verankert:

| C | F | Δ | Was |
|---|---|---:|---|
| `0x41D090` | `0x41C250` | −0xE40 | Kachelwort lesen |
| `0x41D0C0` | `0x41C280` | −0xE40 | imap-Wort lesen (cfind: eindeutig) |
| **`0x41D0E0`** | **`0x41C2A0`** | −0xE40 | `terrain_at` |
| `0x41D110` | `0x41C2D0` | −0xE40 | Brückenbyte lesen |
| `0x41D140` | `0x41C300` | −0xE40 | Kachelwort schreiben (cfind: eindeutig) |
| `0x41D170` | `0x41C330` | −0xE40 | Höhe schreiben |
| **`0x41D1A0`** | **`0x41C360`** | −0xE40 | Brückenbyte schreiben |
| `0x41D1D0` | `0x41C390` | −0xE40 | Kartengrenzen (cfind: eindeutig) |

`cfind`s Wahl ergäbe stattdessen −0xE90, −0xEC0, −0xE40, −0xE40, −0xE70, −0xE40
— **eine unstete Folge**, während die drei eindeutigen Zuordnungen alle −0xE40
tragen.

**Die Fehlerursache ist benennbar:** `cfind` bildet auch
`0x41D110 → 0x41C250` und `0x41D1A0 → 0x41C330` ab, also **zwei C-Funktionen
auf dieselbe F-Funktion**. Sein Fingerabdruck lässt das
**Verschiebungsbyte** (`+2` gegen `+3`) und die **Operandenbreite**
(`ax` gegen `al`) fallen. Wo eine Familie sich nur darin unterscheidet, würfelt
er.

**Zweiter Fall im selben Revier:** `0x428F30 → 0x4280D0` ist falsch, richtig
ist **`0x428120`**. Unterscheidungsmerkmal ist die Modus-Zeichenkette:
C `0x428F30` schiebt `0x4F98E0 = "w"`, F `0x4280D0` schiebt `0x4F6FB0 = "rb"`,
F `0x428120` schiebt `0x4F88EC = "w"`. `cfind` streicht Adressen — und damit
genau das, was hier trennt. (Der Versatz stützt es: die Nachbarn `0x428F80`
und `0x428FD0` liegen bei −0xE10, `0x428120` auch, `0x4280D0` bei −0xE60.)

⭐ **Empfehlung für das Werkzeug:** Verschiebungsbyte, Operandenbreite und
Sofortwerte gehören in den Abdruck; und wenn zwei C-Adressen auf dieselbe
F-Adresse fallen, sollte es das melden.

#### B2. ⚠⚠ Die zwei »in F fehlenden« Funktionen sind beide **keine Funktionsanfänge**

* **`0x429884`** — `aere.py fs` findet den Anfang bei **`0x4297F0`**.
  `0x429884` ist das gemeinsame Sprungziel der Nachspann-Zweige innerhalb
  dieser Funktion (mehrere `jmp 0x429884` im Rumpf, z. B. `0x42982D`,
  `0x42983B`, `0x42985A`). `cfind 0x4297F0` liefert **`0x4289E0`,
  eindeutig**, 48 Befehle, Δ −0xE10.
* **`0x42D78E`** — `aere.py fs` findet **gar keinen** Anfang. Die Adresse
  beginnt mitten in einem Ausdruck (`movsx eax, di` mit schon geladenem `di`)
  und endet 0x33 Byte später in `pop ebp/edi/esi/ebx; ret`. `rufer.py` findet
  null Aufrufe. Ein Abtast über die `CC CC`-Grenzen zeigt: die umschliessende
  Funktion beginnt bei **`0x42C8C0`** und reicht bis `0x42DD60`.
  `cfind 0x42C8C0` → **`0x42BAA0`** (96 %, Δ −0xE20) — sie ist in F sehr wohl
  vorhanden.

⭐ **Damit ist keiner der beiden ein Auslieferungsunterschied.** Beide sind
Zerlegungsartefakte. Die Warnung des Auftrags war richtig — und sie gilt für
beide, nicht nur für die krumme Adresse.

#### B3. ⭐⭐ `BB.8` Punkt 3 ist beantwortet

Siehe Abschnitt 3: das Byte `+3` ist der **Brückenaufbaucode** (0 = nichts,
1…4 Geländergeraden, 9…12 Ecken). Punkt 3 kann von »nicht identifiziert« auf
»identifiziert« gehen, mit dem Zusatz, dass **die Fahrbahn 0 trägt** und
deshalb passierbar bleibt.

#### B4. ⭐ `BB.8` Punkt 2 — der 22-Byte-Satz der Infanteriezelle hat einen VIER-Byte-Kopf

`zz_deutung.py` deutet `0x7847E8` als »col, row, 9 × u16« — das wären 20 Byte.
`0x420FC0` liest bei `0x4210BD`:

```
mov ax, word ptr [eax*2 + 0x7847EC]      ; eax = 11·k + n , n = 0…8
```

also `0x7847E8 + 22·k + 4 + 2n`. ⭐ **Die neun Wörter beginnen bei +4, nicht
bei +2.** Der Kopf ist vier Byte breit. Damit bleibt Platz für `byte[+1]`
(das Begehbarkeitstor der Passierbarkeitskarte) **und** für zwei weitere
Bytes — der Widerspruch aus BB.8.2 löst sich auf, ohne dass eine der beiden
Lesungen ganz falsch sein muss.

⚠ Was die vier Kopfbytes einzeln bedeuten, ist damit **nicht** entschieden.

#### B5. ⭐ `AM.2` — sec38 `+0x06+p` hat einen zweiten, laufenden Setzer

»der Zuteiler setzt nur das Byte des Aufschliessers« ist zu eng: `0x420FC0`
setzt es alle 30 Takte für **jeden** Spieler mit einer Einheit im 9×9-Kasten.
Siehe Abschnitt 7.

#### B6. ⭐ `KLAENGE.md` — drei Nummern bekommen einen Namen

| Nr | Aufrufstelle | Bedeutung (neu) |
|---:|---|---|
| 40 | `0x421E95` | **Mine geräumt** (`0x421E10`) |
| 41 | `0x421F55` | **Falle geräumt** (`0x421ED0`) |
| 75 | `0x427E50`, `0x427F00` | **Flugzeug nimmt ein Ziel auf** (`0x427E30`/`0x427EE0`) |

#### B7. ⚠ Zur Prozentzahl von `cfind`

Sieben Funktionen des Reviers meldet `cfind` als »UNGENAU« (89 … 99 %).
Ich habe sechs davon Befehl für Befehl gegen F gelegt — `0x4203A0` (89 %),
`0x41CED0` (93 %), `0x4281B0` (93 %), `0x428580` (96 %), `0x425CB0` (97 %),
`0x427F90` (99 %). **In keiner steckt ein Verhaltensunterschied.** Der Abstand
kommt aus Registerbelegung (`al` gegen `bl`, `and eax,0xff` gegen
`and eax,0xffff00ff`, `lea ebx,[eax+esi]` gegen `lea ebx,[esi+eax]`) und
daraus, dass angehängte Sprungtafeln als Befehle mitgezählt werden. Für
`0x4281B0` habe ich die beiden Tafeln **einzeln** verglichen: byteweise gleich.

---

### Bauaufgaben, die daraus folgen

1. ⭐⭐ **`aiNN.cwi` ist ableitbar, nicht Autorendaten.** Der Nachbau kann die
   Sektor-Wegekarte aus der Karte selbst erzeugen (1.1–1.3). Wichtiger: **wenn
   wir die Kantentafel rechnen, muss die Regel »BFS unter 80 Schritte«
   gelten** — nicht Luftlinie, nicht »begehbar ja/nein«.
2. ⭐⭐ **Brücken schalten die KI-Wegekarte um.** `restore + apply` nach jedem
   Brückenbau und jeder Brückenzerstörung (`0x4CB889`, `0x4CCA8F`). Fehlt das,
   fährt die KI durch gesprengte Brücken hindurch — oder benutzt eine neu
   gebaute nie.
3. ⭐⭐ **Das Zellenbyte `+3` fehlt uns.** Es trägt gleichzeitig das Bild des
   Geländerstücks und die Sperre für die Rumpfarten 4…7/12/13. Ohne es sind
   Brücken entweder unsichtbar oder ganz unpassierbar.
4. ⭐ **Der Wind.** `OFFENE_FRAGEN.md` notiert »bei uns ungenutzt«. Jetzt liegt
   das Schrittwerk vor: Anfangswert `zufall&7` / `zufall%?+2`, Schritt ±1 alle
   2000 Takte auf Phase 1111 aus dem **deterministischen** Würfel `0x4C5B30`.
   ⚠ **Die zwei kaputten Klemmen mitzubauen ist eine Entscheidung, keine
   Selbstverständlichkeit.** Für die Kampagne (originalgetreu) mitbauen; für
   den Gefechtsmodus ein guter Kandidat für eine bewusste Abweichung.
5. ⭐ **Die Flugabwehr.** Der Kegel `dz/3 ≤ d ≤ dz` mit
   `dz = |alt − 15·Höhe|/13` und der Nachladewert `12…17` sind Zahlen, die ein
   Nachbau eins zu eins übernehmen kann. Ebenso die acht Höhenschwellen von
   `0x425CB0`.
6. ⭐ **Bombenzielsuche:** Aufschlagpunkt aus Höhe und Richtung, dann Ringsuche
   mit der Tafel `0x4F9210`; Einheit schlägt Gebäude. Unsere Fassung wirft
   vermutlich direkt auf das Ziel.
7. ⭐ **Auswahlvorrang.** `0x4291A0` bestimmt, welches Bild bei einer gemischten
   Auswahl gezeigt wird: `Grundwert[Gattung] + Sonderfall + Rang`, Tafel
   `{20, 2, 0, 100, 10, 10, 0, 0}`.
8. ⭐ **Ein Gebäude wird als Ganzes aufgedeckt**, nicht kachelweise
   (Abschnitt 5) — ein sichtbarer Unterschied am Nebelrand.
9. ⚠ **Sicht wird gelöscht, nicht nur gestempelt** (`0x4203A0`). Zusammen mit
   dem schon bekannten »sec50 wird jede Runde neu gebaut« stützt das, dass das
   Original im Kampfbild **keinen** Zustand »gesehen, aber gerade unbeobachtet«
   führt.
10. ⚠ **768 leere Aufrufe.** Wer die Rufergrafik als Wichtigkeitsmass benutzt,
    muss `0x41F9F0` (413), `0x422D80` (195) und `0x41FA00` (157)
    ausschliessen — sie tun nichts.

---

### Was ungedeutet bleibt

1. ⚠ **`0x422340` / sec89 / sec114.** Die Rechnung ist klar (200 Sätze zu
   6 Byte bei `0x6786A8`, `word[+4]` läuft ab, `byte[+2]` folgt nach), die
   Bedeutung nicht. `OFFENE_FRAGEN.md:6589` merkt an, dass sec89 und sec114
   **beide** auf `0x6786A8` laden — eines von beiden ist falsch, und das ist
   hier nicht zu entscheiden.
2. ⚠ **Die Achse `A` der Kacheltafel.** Die Grösse (8 × 15 × 19 Dwords) ist
   über die `fread`-Länge `0x23A0` gemessen, und `A = 7` ist als Nebelebene
   belegt. Was `0x4ACDE0` als `A` liefert und warum bei gemischten Ecken
   `A += 4` gerechnet wird, habe ich **nicht** aufgemacht.
3. ⚠ **Das Abstandsmass in `0x427F90`** (12.5). Byteweise in beiden Fassungen,
   aber ohne Nullmodell dafür, ob es gewollt ist.
4. ⚠ **Was die fünf leeren Funktionen einmal taten.** Nur die Rufermuster sagen
   etwas: `0x41F9F0`/`0x41FA00` fast ausschliesslich aus `0x405xxx`/`0x406xxx`
   (Bewegung), `0x422D80` aus `0x406xxx`…`0x40Cxxx`.
5. ⚠ **`0x428D60`s zweite Hälfte.** Dass sie in sec81 (`0x833A00`, zehn Gruppen
   zu 422 Byte) den höchsten Satz mit leerer Namenskette sucht, ist gelesen;
   **warum** eine Flughafen-Bauliste das tut, nicht.
6. ⚠ **`0x41CED0`s Argumente 3 und 4** und die zwei dort gerufenen Füllschleifen
   (`0x4ACDA0`, `0x4ACEB0`) sind nicht verfolgt; `0x4ACDA0` ist nach
   `CAMPAIGN_RE.md:706` `set_corner`.
7. ⚠ **Negativbefunde unter Vorbehalt.** Für `0x542DF4` (»kein Leser«) und für
   `0x677E20` (»nur zwölf Zugriffe«) lief `reloc_refs --block` **nicht**.
   Beides sind Dword-Globale, für die Blockbefehle unwahrscheinlich sind — aber
   die Regel aus der Einweisung ist damit nicht erfüllt.
8. ⚠ **Sechs C/F-Zuordnungen bleiben `cfind`-Vorschläge**, von mir nicht
   nachgeschlagen: `0x421E10`, `0x421ED0`, `0x427E30`, `0x427EE0`, `0x422D90`,
   `0x428FD0`. Bei allen sechs stimmt der Versatz mit dem der Nachbarn überein,
   was ein Hinweis, aber kein Beleg ist.
9. ⚠ **`0x41C230`s Fortschrittsanzeige** zeichnet »X« bei `(10n+20, 450)` und
   `0x41C490` »·« bei `(5n+20, 400)` über `0x4C81B0`. Dass das eine
   Fortschrittsausgabe ist, folgt aus Form und Zahlen; **nachgesehen habe ich
   `0x4C81B0` nicht.**

---

## BK. Revier 3: 0x42DEF0 … 0x43AC60

50 Funktionen, 10 752 Byte. Alle 50 sind zerlegt, 46 gedeutet, 4 als tot
belegt. Das Revier zerfällt in **zehn Mechaniken**: Auswahl und Abwahl,
die Truppzelle (sec16), die Gruppenbefehle, der Angriffsbefehl,
die Wracks (sec41), Animation und Geschossrichtung, die Gruppenanzeige,
das Gas, die Abwehrstellung und drei Entwicklertasten.

Die zwei tragendsten Funde:

* ⭐⭐ **`0x4353F0` ist der Angriffsbefehl des Originals — Busbefehl 11.**
  Damit fällt `CommandOp.OursAttack = 2001` (unsere eigene Nummer) weg.
* ⭐⭐ **sec41 sind die WRACKS.** Das war Punkt 4 der offenen Liste BE.9.

---

### Adresstafel

F-Spalte durchgehend mit `cfind.py` nachgeschlagen, nicht gerechnet.
»~%« = `cfind` meldet »ungenau« mit dieser Formgleichheit; die Funktion ist in
F vorhanden, weicht aber ab (meist Registerumbenennung — nicht nachgeprüft).

| C | F | Byte | Rufer | Was sie ist |
|---|---|---:|---:|---|
| `0x42DEF0` | `0x42D0E0` ~91 % | 80 | 2 | **freien Einheitenplatz des Spielers suchen** (`faze == 0xFF`) |
| `0x431090` | `0x4301D0` | 544 | 1 | ⭐ **Wrackversatz in der Zelle** aus sec41-Satz (Richtung + `KOLIK`) |
| `0x4312B0` | `0x4303F0` ~95 % | 624 | 1 | dasselbe mit ausgeschriebenen Argumenten (Geländemaler) |
| `0x431520` | `0x430660` ~98 % | 176 | 1 | »ist etwas Ausgewähltes von Gattung < 2« |
| `0x433010` | `0x432150` | 464 | 9 | ⭐ **die Auswahl aufheben** (Einheit / Flugzeug / Gruppe) |
| `0x433750` | `0x432890` | 416 | 1 | die zwei **aufgeschobenen Knopfaktionen** (`0xA1833B`, `0xA18330`) |
| `0x433B50` | `0x432CA0` | 64 | 5 | steht Einheit *e* in Trupp *t*? |
| `0x433F90` | `0x4330E0` | 80 | 3 | ist der Trupp **voll** (≥ 9) oder gar kein Truppgriff? |
| `0x434070` | `0x4331C0` | 176 | 1 | steht in Trupp *t* ein **Feind** von Spieler(*e*)? |
| `0x434120` | `0x433270` | 80 | **0** | ⚠ Zwilling von `0x433B50`, **tot** |
| `0x434170` | `0x4332C0` | 144 | 2 | den **ersten Feind** im Trupp liefern (`0xFFFF` = keiner) |
| `0x434200` | `0x433350` | 160 | 1 | Truppzustand: ist **keiner** der neun untätig/schussbereit |
| `0x4342A0` | `0x4333F0` | 96 | 1 | Prädikat `0x404D20` über alle neun Mann |
| `0x434300` | `0x433450` ~84 % | 128 | 1 | ⭐ **Richtung von der Einheit zur Zelle** (Seitenverhältnis 40 : 20) |
| `0x434380` | `0x4334D0` ~91 % | 336 | **0** | ⚠ Einheit vom Belegungsgitter nehmen und entfernen, **tot** |
| `0x434580` | `0x4336C0` | 80 | 1 | steht dieser Griff in der Auswahlliste? |
| `0x435100` | `0x434240` | 480 | 1 | ⭐ **Gruppenbewegung mit Formation** (Befehl 3 / 6) |
| `0x4352E0` | `0x434420` | 176 | 3 | Gruppenbewegung **ohne** Formation (Befehl 3 / 6) |
| `0x435390` | `0x4344D0` | 96 | 1 | Befehl **12** an jede ausgewählte Einheit |
| `0x4353F0` | `0x434530` | 592 | 1 | ⭐⭐ **der ANGRIFFSBEFEHL — Busbefehl 11**, mit dem ganzen Griffraum |
| `0x435640` | `0x434780` | 208 | 1 | ⭐ **Spurgruppen-Zuteiler** (sec39/sec40) |
| `0x4357F0` | `0x434930` | 352 | 10 | ⭐ **sec42-Satz anlegen, mit Feinversatz** (Geschwister von `0x435A40`) |
| `0x435B20` | `0x434C60` ~92 % | 176 | 1 | ⭐ **der Animationstakt** (sec42 weiterschalten und freigeben) |
| `0x435E00` | `0x434F40` ~96 % | 448 | 2 | ⭐ **8er-Richtung eines Geschosses**, über `asin` |
| `0x436420` | `0x435580` | 48 | 1 | `byte[Einheit + 0x42] = 1` (ungedeutet) |
| `0x4364A0` | `0x435600` | 208 | 1 | ⭐ alle ausgewählten **Bomber** auf das Ziel unter der Maus |
| `0x436570` | `0x4356D0` | 240 | 1 | ist ein **Bomber mit Munition** ausgewählt? (Zeigerzustand) |
| `0x436660` | `0x4357C0` | 112 | 1 | Gruppe: eine Einheit mit Gattung < 2 dabei? |
| `0x4366D0` | `0x435830` | 112 | 1 | Gruppe: eine Einheit mit Gattung == 0 dabei? |
| `0x4369F0` | `0x435B50` | 256 | 1 | ⭐ **mittlere Gesundheit** der Auswahl in Prozent |
| `0x436C30` | `0x435D90` | 288 | 2 | ⭐ **mittlere Munition** der Auswahl in Prozent (101 = keine) |
| `0x436E40` | `0x435FA0` | 272 | 1 | ⭐ **wieviele der Auswahl unter 25 % Sprit** (»Niedrig«) |
| `0x437F90` | `0x4370F0` | 352 | 2 | ⭐ **Bauplatzklick**: Rumpf 72/74/198 → Befehl 20 / 21 / 16 |
| `0x438920` | `0x437A80` | 96 | 1 | Befehl **19** senden (Einheit, Spalte, Zeile) |
| `0x438A70` | `0x437BD0` | 112 | **0** | ⚠ »alle Ausgewählten Gattung < 4«, **tot** |
| `0x438D10` | — | 16 | **0** | ⚠ **leere Funktion** (`ret`), **tot** |
| `0x439170` | `0x4382D0` | 336 | 1 | ⭐ **Flugzeugliste eines Flughafens** neu aufbauen (`0x833A00`) |
| `0x439500` | `0x438660` | 208 | 1 | ⭐ **Gasdichte einer Zelle plus ihrer vier Nachbarn** |
| `0x4395D0` | `0x438730` | 240 | 1 | ⭐ **die Gaswirkung auf eine Zelle** |
| `0x43A020` | `0x439190` ~95 % | 240 | 2 | Auswahlrechteck nach min/max sortieren |
| `0x43A110` | `0x439280` | 160 | 1 | ⭐ Befehl **22** senden — ein **Rechteckbefehl** |
| `0x43A2F0` | `0x439460` | 304 | 3 | ⭐ **freie Zelle in der Schablonenspirale** `0x79A008` |
| `0x43A880` | `0x4399F0` | 32 | 3 | Blinkmarke setzen (6 Takte) |
| `0x43A8A0` | `0x439A10` | 48 | 1 | Blinkmarke ablaufen lassen |
| `0x43A940` | `0x439AB0` | 192 | 1 | ⭐ **Abwehrstellung aufbauen** — Warteschlange `0x834B70` |
| `0x43AA00` | `0x439B70` | 96 | 2 | ⭐ Stellungswerte **abziehen** (−8/−8/−6/−4) |
| `0x43AA60` | `0x439BD0` | 96 | 1 | ⭐ Stellungswerte **zugeben** (+8/+8/+6/+4) |
| `0x43AB00` | `0x439C70` | 112 | 1 | ⚠ **Entwicklertaste**: 50 Gaswolken an der Zeigerzelle |
| `0x43AB70` | `0x439CE0` | 240 | 1 | ⚠ **Entwicklertaste**: `Zasah` über das ganze Sichtfeld |
| `0x43AC60` | `0x439DD0` | 160 | 1 | ⚠ **Entwicklertaste**: `+0x28` aller eigenen Einheiten würfeln |

⚠ Die C−F-Abstände sind in diesem Revier **nicht konstant**: `−0xE10`, `−0xEC0`,
`−0xEB0`, `−0xEA0`, `−0xE90`, blockweise in dieser Reihenfolge. Wer den
Datenversatz `0xFA0` auf Code überträgt, liegt bei jeder einzelnen falsch.

**Der Ruferknoten des Reviers** ist `0x437060` (2 517 B, ungelesen): der
Klick- und Befehlsmenüverteiler. Er ruft `0x434580`, `0x435100`, `0x4352E0`
(3×), `0x4353F0`, `0x4364A0`, `0x437F90` (2×), `0x438920`, `0x43A880` (2×) und
sendet selbst die Befehle 3, 11, 14, 15, 23, 29. Der zweite Knoten ist
`0x4315D0` (5 376 B, die »5,5-kB-Trefferprüfung« aus Abschnitt 4 von
`OFFENE_FRAGEN.md`): er ruft `0x431520`, `0x436570`, `0x436660`, `0x4366D0`,
`0x43A110` — alles Prädikate, die entscheiden, **welcher Mauszeiger** gilt.

---

### 1. ⭐⭐ sec41 sind die WRACKS

`OFFENE_FRAGEN.md` BE.9 Punkt 4: *»⚠ Was sec41 (1000 × 10 B) enthält — bekannt
ist nur, dass es als Elementart 0x11 gezeichnet wird und an der Sichtkarte
hängt.«* Das ist jetzt gelesen, von drei Seiten, die sich schliessen.

#### 1.1 Der Satz

Der Anleger ist C `0x4A97C0` (**genau ein Verweis im ganzen Programm**, der
Thunk `0x4011A9`), sieben Argumente:

```
sec41_anlegen(a1, a2, a3, a4, a5, a6, a7)                   0x4A97C0
    i = erster Satz mit byte[+7] == 0        (Suchschleife 0x4A97C8..0x4A97D8)
    +0x00 = a1     +0x01 = a2      +0x02 = a3     +0x04 = a4 (Wort)
    +0x06 = a7     +0x07 = 1       +0x08 = a5     +0x09 = a6
```

Sockel `0x9C6FB8`, Schritt 10, 1000 Plätze (`cmp ecx, 0x9C96CF`, das ist
`0x9C6FBF + 1000·10 − 1`). `+0x07 == 0` heisst **frei**.

#### 1.2 Die zwei Anleger — und was sie einsetzen

**A. Der Tod einer Bodeneinheit**, in `move units` @`0x406F3D…0x406F63`. Direkt
davor stehen die Fehlersuchzeilen `'likvid typ:'` (`0x4F6C9C`) und `'rnd X4'`,
und der Sprung geht über eine Sechs-Wege-Tafel auf die **Gattung**
(`byte[+0x0A]`). Der Zweig schiebt:

```
a1 = byte[Einheit + 0x00]   RX      -> +0x00
a2 = byte[Einheit + 0x01]   RY      -> +0x01
a3 = byte[Einheit + 0x04]   POHYB   -> +0x02   (Richtung 0..7, 0xFF = stand)
a4 = word[Einheit + 0x06]   KOLIK   -> +0x04   (der Fortschritt in der Zelle)
a5 = 0, a6 = 0
a7 = rand() % 16 mit Vorzeichen     -> +0x06
```

**B. Der Absturz eines Flugzeugs**, @`0x423A2F…0x423A6D`, sechsmal in Folge
(`mov bl, 6`), direkt hinter der Zeile `'air naraz OK'` (`0x4F9338`) und dem
`byte[Flugzeug + 0x08] = 0` (»der Platz ist leer«, `AIR_RE.md` Zeile 531):

```
a1 = word[Flugzeug + 0x00]  X       a2 = low(word[+0x02]) + 1
a3 = 0     a4 = 0
a5 = rand() % 40      a6 = rand() % 40      a7 = rand() & 0x0F
```

**Ein abstürzendes Flugzeug hinterlässt sechs Trümmerstücke**, gestreut über
0…39 Bildpunkte in beiden Achsen.

#### 1.3 `0x431090` — warum `+0x02` und `+0x04` genau das sind

Der Zeichenlisten-Erzeuger `0x42EE10` ruft `0x431090(satz, &x, &y)`; die
Funktion verzweigt über eine **Achtwegetafel** bei `0x431220` auf `byte[+0x02]`
und rechnet mit `word[+0x04]` (im Folgenden `d`):

| Fall | X | Y |
|---:|---|---|
| 0 | 0 | `−d/8` |
| 1 | `−d/6` | `−d/12` |
| 2 | `−d/4` | 0 |
| 3 | `−d/6` | `+d/12` |
| 4 | 0 | `+d/8` |
| 5 | `+d/6` | `+d/12` |
| 6 | `+d/4` | 0 |
| 7 | `+d/6` | `−d/12` |
| `0xFF` | **20** | **10** (gesetzt, nicht addiert) |
| sonst | (unverändert) | |

Danach addieren **alle** Fälle noch `(+20, +10)` — die Zellmitte.

⭐ **Das ist eine Ellipse mit den Halbachsen `d/4` und `d/8`, also genau dem
Seitenverhältnis 2 : 1 der 40 × 20-Kachel.** Die Gegenprobe: auf einer solchen
Ellipse liegt der 45°-Punkt bei `(d/4)/√2 = d/5,657` und `(d/8)/√2 = d/11,31`.
Der Code nimmt `d/6` und `d/12` — **6 % daneben**, die billigste ganzzahlige
Näherung, die es gibt. Ein Nullmodell, in dem die acht Fälle unabhängig
gewürfelt wären, müsste dieses Vorzeichenmuster (X: 0,−,−,−,0,+,+,+ und
Y: −,−,0,+,+,+,0,−) **und** diese zwei Beträge zufällig treffen.

⭐ **Und `d` ist `KOLIK`.** `KOLIK` läuft laut `ENTITY_FELDER.md` bis 80.
Bei `d = 80` gibt Fall 6 genau `+20` — eine **halbe Kachelbreite** — und Fall 4
genau `+10` — eine **halbe Kachelhöhe**. Die Zahl 80 ist also nicht nur der
Zählbereich, sondern der Massstab: **`KOLIK` = 80 heisst »eine halbe Zelle
weit«**, und das Wrack liegt exakt dort, wo die Einheit im Augenblick des Todes
zwischen zwei Zellen stand.

`0x4312B0` ist dieselbe Rechnung mit ausgeschriebenen Argumenten
(`f(klasse, d, &x, &y)`); sie wird vom Geländemaler `0x42C8C0` @`0x42D29D`
gerufen — das Wrack wird also **in den Boden gemalt**, nicht in die
Beweglichen-Liste. Das passt zu `OFFENE_FRAGEN.md` Zeile 603 (»`0x42C8C0` —
also zwischen Boden und allem Beweglichen«).

#### 1.4 Der Takt und die Lebensdauer

C `0x4A9860` (nicht in meinem Revier, aber unmittelbar hinter dem Anleger):

```
if (dword[0x4FA240] % 10 != 0) return             ; jeder zehnte Bildtakt
for (jeder Satz)  if (byte[+7] != 0) byte[+7]++ ;
```

`+0x07` ist also **zugleich Belegtmarke und Alterszähler**. Bei `255 → 0`
läuft er über und der Platz ist frei: **ein Wrack lebt 255 × 10 = 2 550
Bildtakte.** Der Geländemaler liest `+0x07` @`0x42D0CA` und `+0x06`
@`0x42D0E2`/`0x42D102` — Alter und die 16 Trümmervarianten.

⭐ Damit ist auch die Marke **`Wrecks`** aus der Hauptschleife `0x415CF0`
belegt zugeordnet.

**Satztafel sec41 (`0x9C6FB8`, F `0x9C6018`, 10 × 1000):**

| Versatz | Breite | Inhalt |
|---|---|---|
| `+0x00` | u8 | Spalte |
| `+0x01` | u8 | Zeile |
| `+0x02` | u8 | **Richtung 0…7**, `0xFF` = mittig |
| `+0x03` | — | ungenutzt |
| `+0x04` | u16 | **`KOLIK`** — die Entfernung aus der Zellmitte |
| `+0x06` | u8 | **Trümmervariante** (Einheit: `rand()%16` vorzeichenbehaftet; Flugzeug: `rand()&15`) |
| `+0x07` | u8 | **0 = frei, sonst Alter**, alle 10 Takte +1 |
| `+0x08` | i8 | Bildpunktversatz X (nur Flugzeugtrümmer, 0…39) |
| `+0x09` | i8 | Bildpunktversatz Y (dito) |

---

### 2. ⭐⭐ Der Angriffsbefehl ist Busbefehl 11 — `0x4353F0`

`Scripts/Simulation/Commands/CommandOp.cs` sagt heute:

> *»unser Angriffsbefehl trägt eine Einheitennummer als ZIEL … und für einen
> solchen Befehl haben wir im Original KEINEN Behandler nachgewiesen.«*
> (`OursAttack = 2001`, unsere Setzung)

und `CAMPAIGN_RE.md` §10.2:

> *»⚠ Für Busbefehl 11 gilt das nicht automatisch: das ist eine andere Routine,
> und ihre Argumente sind nicht gelesen.«*

**Beides ist damit erledigt.**

#### 2.1 Der Behandler ruft `order()`

Die Sprungtafel des Bereichs A steht bei `0x4C4D54` (30 Dwords, aus der Datei
gelesen). Eintrag 11 → `0x4C2DDD`. Der Rumpf:

```
0x4C2DDD  ecx = P1 = word[Ring + 0xE0]                 ; die Einheit
0x4C2DF5  if (byte[Einheit + 0x14] == 0x16) return     ; UKOL 22 -> nichts
0x4C2E04  if (byte[Einheit + 0x14] == 0x17) return     ; UKOL 23 -> nichts
0x4C2E2D  0x410220(P1, P2, P3, P4, P5)                 ; ueber Thunk 0x4020DB
0x4C2E5F  wenn byte[letzteEinheit + 0x0D] in {15,16}:  byte[+0x48] = 0
```

`0x410220` ist in `CAMPAIGN_RE.md` §10.2 vollständig gelesen:
**`order(einheit, cx, cy, utok_na, extra)` → `UKOL = 4` (Angriff)**.
Busbefehl 11 ist also kein anderer Weg, sondern **derselbe** — nur über den
Bus statt direkt.

#### 2.2 Der Absender baut den ganzen Griffraum von `UTOK_NA`

`0x4353F0(ziel)`, gerufen aus `0x437060` @`0x43743B`:

```
ziel = arg1

; --- Teil 1: das ZIEL in P4/P5 uebersetzen ---
if (ziel < 8000)                                  ; eine EINHEIT
        merke  zx = byte[ziel + 0x00], zy = byte[ziel + 0x01]
        P4 = ziel
else
        z = word[0xBDEA80 + 2*(Spalte<<8 | Zeile)]      ; imap unter dem Zeiger
        if (60000 <= z < 60300)         P4 = z                      ; GEBAEUDE
        else {
            b = byte[0x542E18 + (Spalte<<8 | Zeile)]    ; sec20, die Lagentafel
            if (100 <= b < 250)         P4 = b + 40000              ; BRUECKE/RAMPE
            else                      { P4 = Spalte + 30000; P5 = Zeile }  ; BODEN
        }

; --- Teil 2: Schwerpunkt der Auswahl ---
mx = Summe(Spalte) / Anzahl ;  my = Summe(Zeile) / Anzahl

; --- Teil 3: je Ausgewaehltem ---
fuer jeden Eintrag der Auswahlliste 0x833098:
    Einheit (<8000):  Befehl 11, P1 = Einheit,
                      P2 = Spalte + zx − mx,  P3 = Zeile + zy − my
    Flugzeug:         0x427930(idx, …)  ->  Befehl 6
```

⭐⭐ **Die Gegenprobe schliesst auf die Zahl genau.** `CAMPAIGN_RE.md` §10.2
liest im *Behandler* `order()`: »`arg4 → +0x38`, **aber NUR wenn arg3 in
[30000, 30256)** liegt«. Mein Absender schreibt für ein reines Bodenziel
`P4 = Spalte + 30000` mit Spalte ≤ 255 — das ist **exakt** das Fenster
`[30000, 30256)`, und `P5` ist die **Zeile**. Zwei Seiten, die nichts
voneinander wissen, und eine 256 breite Schranke, die auf den Anfangswert
passt. **Damit ist auch `+0x38` gelesen: es ist die Zeile des Bodenziels.**

Und `CAMPAIGN_RE.md` schreibt: *»⚠ Ungelesen bleibt `UTOK_NA`… Griffräume:
< 8000 Einheit, 10000..13999 Infanteriezelle, ab 50000 Kulissenobjekt. Kein
Gegenbeispiel, also keine Deutung.«* Der Absender nennt sie jetzt vollständig:

| `UTOK_NA` | was | belegt an |
|---|---|---|
| `0 … 7999` | eine **Einheit** | `0x435403` |
| `30000 + Spalte` (dazu `+0x38` = Zeile) | eine **blosse Bodenzelle** | `0x435495` |
| `40100 … 40249` | **Brücke/Mole (sec17) oder Rampe (sec21)** — der sec20-Wert plus 40 000 | `0x435487` |
| `60000 … 60299` | ein **Gebäude** | `0x435465` |

Die Rechnung bei den Brücken ist ein Übersetzertrick und sieht schlimmer aus,
als sie ist: `sub bx, 0x63C0` mit `bx ∈ [100, 250)` ist modulo 65536 dasselbe
wie `+ 40000`. Die Bänder 100…199 (Brücke/Mole) und 200…249 (Rampe) sind die
aus `OFFENE_FRAGEN.md` AI.2.

⚠ **Eine Stelle bleibt unsauber.** Bei einem Nicht-Einheiten-Ziel springt
`0x435401` an dem Block vorbei, der `zx`/`zy` füllt — die beiden Stapelbytes
`[esp+0x13]`/`[esp+0x12]` bleiben **uninitialisiert** und gehen trotzdem in
P2/P3 ein. Das ist kein Lesefehler von mir: der Sprung steht vor dem Schreiber,
und `sub esp, 8` ist die einzige Vorbereitung. Für den Nachbau heisst das:
bei einem Bodenangriff sind P2/P3 des Originals Müll — was nichts schadet,
weil der Behandler `order()` sie nur nach `CX`/`CY` durchreicht und der
Angriff über `UTOK_NA` läuft.

---

### 3. Die Auswahl: `0x433010`, `0x433750`, `0x434580`

#### 3.1 `word[0x4FA0C8]` — der Griffraum, jetzt mit dem Flugzeugzweig

Abschnitt AM.3 hat schon: `< 8000` Einheitenplatz, `≥ 20000` Flugzeug über
`0x591EF0 + 68·20000 = 0x6DDF70`. `0x433010` (»Auswahl aufheben«) benutzt
denselben vorgespannten Sockel und **schliesst damit BE.9 Punkt 2**:

```
abwaehlen()                                                     0x433010
    0x44F7C0()                            ; Fensterarbeit
    dword[0x502ACC] = 0                   ; Bau-/Platzierungsmodus aus
    byte[0xA182F9] = 0 ; byte[0xA31A9C] = 0
    h = word[0x4FA0C8]
    if (h == 0xFFFF) return                                     ; nichts gewaehlt

    if (h < 8000):                                              ; EINHEIT
        if (byte[h + 0x14] == 1)  Befehl 5 (P1 = h)             ; UKOL 1
        word[0x4FA0C8] = 0xFFFF ;  byte[h + 0x1B] = 0           ; OZNACEN
    elif (h >= 20000):                                          ; FLUGZEUG
        byte[0x591EFF + 68·h] = 0                               ; = Flugzeug +0x0F
        if (byte[0x591F00 + 68·h] == 3)  Befehl 30 (P1 = h−20000)   ; uk == 3
        if (byte[0x591EF9 + 68·h] == byte[0x4FA284]) dword[0x502AD4] = 0
        word[0x4FA0C8] = 0xFFFF
    else:                                                       ; h == 10000, GRUPPE
        fuer jeden Eintrag der Liste 0x833098 (Anzahl 0x4FA278):
            < 8000  ->  byte[e + 0x1B] = 0
            sonst   ->  byte[0x591EFF + 68·e] = 0
        word[0x4FA0C8] = 0xFFFF
    0x4500B0()                            ; Fensterarbeit
```

⭐ Umgerechnet auf den echten Flugzeugsockel `0x6DDF70`:

* `0x591EF9` → **`+0x09`** — und `0x433010` vergleicht es mit `byte[0x4FA284]`,
  dem eigenen Spieler. Das **bestätigt `AIR_RE.md` Zeile 209** (»`+0x05 !=
  byte[0x6DDF79]` → fremder Besitzer«): `+0x09` ist der **Besitzer**.
  ⚠ `AIR_RE.md` nennt `+0x09` an drei anderen Stellen (144, 560, 716) `faze`.
  Eine der beiden Lesungen ist falsch; der Vergleich mit dem eigenen Spieler
  ist die härtere.
* `0x591EFF` → **`+0x0F`**. `AIR_RE.md` Zeile 36 führt dieses Byte als
  »⚠ ungelesen«. ⭐ **Es ist die Auswahlmarke des Flugzeugs** — das `OZNACEN`
  der Luft: `0x433010` nullt es beim Abwählen, genau parallel zu
  `byte[Einheit + 0x1B] = 0` im Einheitenzweig.
* `0x591F00` → **`+0x10` = `uk`**, `0x591F01` → **`+0x11` = `m_uk`**.

#### 3.2 `0x433750` — die zwei aufgeschobenen Knopfaktionen

Gerufen aus der Hauptschleife `0x415CF0`. Zwei Merkerbytes, die anderswo
gesetzt und hier eingelöst werden:

```
if (byte[0xA1833B]):                         ; Merker A
    h < 8000     ->  Befehl 12 (P1 = h)
    h == 10000   ->  0x435390()              ; Befehl 12 an jeden der Gruppe
    byte[0xA1833B] = 0

if (byte[0xA18330]):                         ; Merker B
    h < 8000 und Gattung(h) nicht in {0xFF, 1}:
        byte[0xA18330] = 0
        UKOL(h) == 1  ->  Befehl 5
        sonst         ->  0x429760(h) ; Befehl 4
    20000 <= h < 25000:
        byte[0xA18330] = 0
        m_uk in [3,5]  ->  nichts
        uk == 3        ->  Befehl 5
        sonst          ->  Befehl 4, dann 0x44FE10(0)
```

⚠ **Markierte Vermutung, keine Zahl dahinter:** `UKOL == 1` scheint
»handgesteuert« zu heissen. Beleg dafür ist nur, dass `0x433460` (die
Direktsteuerung) Befehl 1 für die Pfeiltasten sendet und dass sowohl `0x433010`
als auch `0x433750` beim Loslassen einer Einheit mit `UKOL == 1` Befehl 5
schicken — also »Steuerung aufgeben«. Der Behandler 5 (`0x4C29C3`) ist nicht
zu Ende gelesen.

#### 3.3 Die kleinen Prädikate

Alle lesen dieselbe Liste (`0x833098`, Anzahl `0x4FA278`) und dienen dem
Zeigerzustandsautomaten `0x4315D0`:

| C | fragt |
|---|---|
| `0x431520` | Einzelauswahl **oder** Gruppe: Gattung `< 2` dabei? |
| `0x436660` | nur Gruppe: Gattung `< 2` dabei? |
| `0x4366D0` | nur Gruppe: Gattung `== 0` dabei? |
| `0x436570` | Bomber (`typ == 1`) mit Munition `> 0` dabei? |
| `0x434580` | steht Griff *h* in der Liste? |
| `0x438A70` | ⚠ tot: sind **alle** Ausgewählten Gattung `< 4`? |

---

### 4. Die Truppzelle sec16 — sechs Funktionen, und die offene Frage BB.8/2

`0x7847E8`, 22 Byte × 4000, Griffraum **10 000 … 13 999**. Der Übersetzer
spannt die 10 000 in den Sockel vor: `0x74EC88 = 0x7847E8 − 10000·22` und
`0x7847EC = 0x7847E8 + 4`. Wer `0x74EC88` für eine eigene Tafel hält, deutet
einen Übersetzertrick — dieselbe Falle wie bei `0x591EF0`.

**Der Satz, aus meinen sechs Funktionen gegengerechnet:**

| Versatz | Inhalt | belegt an |
|---|---|---|
| `+0x00` | **Anzahl Mann (0…9)** | `0x433FB8`: `cmp al, 9; jb` → »nicht voll« |
| `+0x01 … +0x03` | Zelle (aus Abschnitt AU.11) | — |
| `+0x04 … +0x15` | **neun `u16`-Einheitennummern** | alle sechs Funktionen laufen `i = 0…8` mit Schritt 2 ab `0x7847EC` |

⭐ **Das entschärft `BB.8` Punkt 2** (»Die Felder `+0`/`+1` im 22-Byte-Satz…
Eine der beiden Lesungen ist falsch«): `+0x00` ist die **Anzahl**, nicht die
Spalte, und `zz_deutung.py`s Lesung »col, row, 9 × u16« ist um ein Feld
verschoben. Der Kartenprüfer, der `byte[Satz+1] == 0` als »begehbar« liest,
prüft also nicht die Zeile. ⚠ `0x433A50` (den Vergeber) habe ich nicht
aufgemacht; die Deutung von `+0x01…+0x03` bleibt die aus Abschnitt AU.11.

**Die sechs Funktionen:**

```
0x433B50(einheit, trupp)   -> 1, wenn einheit in einem der neun Plaetze steht
0x434120(...)              -> derselbe Rumpf, TOT (kein Rufer)
0x433F90(griff)            -> 1, wenn griff kein Truppgriff ist ODER der Trupp voll (>= 9)
0x434070(einheit, griff)   -> 1, wenn im Trupp ein Mann eines FEINDLICHEN Spielers steht
0x434170(einheit, trupp)   -> die Nummer des ersten Feindes, sonst 0xFFFF
0x434200(trupp)            -> 0, sobald ein Mann untaetig oder schussbereit ist; sonst 1
0x4342A0(trupp, x)         -> 0, sobald 0x404D20(mann, x) fuer einen Mann 0 liefert
```

Die Feindprüfung in `0x434070`/`0x434170` benutzt
`byte[0x87B155 + 40·eigenerSpieler + fremderSpieler]`, wobei der Spieler aus
`einheit / 1000` fällt. `== 0` heisst **kein Bündnis**, also angreifbar — das
deckt sich mit `GAMESTATE_RE.md` Zeile 566 (»is q an ally of p?«) und mit
`OFFENE_FRAGEN.md` Zeile 737 (»kein Eigenbeschuss«). Rufer sind `0x40DDB0`
(*Shooting of robot which cannot shoot*) und `0x40F0A0` (*ready to shoot*) —
die Zielsuche der Infanterie.

---

### 5. Die Gruppenbefehle — und was die Formation wirklich rechnet

Der Kratzblock ist `0xB8A3D8` (Befehlsnummer), `0xB8A3E0…0xB8A3E8` (P1…P5).
Drei meiner Funktionen schicken einen Befehl an **jeden** Ausgewählten:

| C | Befehl Einheit | Befehl Flugzeug | Formation? |
|---|---|---|---|
| `0x435100` | 3 | 6 (P4 = `0xFFFF`) | **ja** |
| `0x4352E0` | 3 | 6 (P4 = `0xFFFF`) | **nein** — alle auf denselben Punkt |
| `0x4353F0` | 11 | über `0x427930` | **ja**, um das Ziel herum |
| `0x435390` | 12 | — (übersprungen) | — |

Die Formation ist in allen dreien dieselbe Rechnung:

```
mx = (Summe aller Spalten) / Anzahl          ; ganzzahlige Division
my = (Summe aller Zeilen)  / Anzahl
je Einheit:  P2 = eigeneSpalte − mx + Zielspalte
             P3 = eigeneZeile  − my + Zielzeile
```

⚠ Für Flugzeuge geht in dieselbe Summe **`word[Flugzeug+0x00]` / `+0x02`** ein
— das sind Bildpunkt-, keine Kachelkoordinaten. Eine gemischte Auswahl aus
Bodeneinheiten und Flugzeugen verschiebt den Schwerpunkt darum um ein
Vielfaches. Das steht so im Original, nicht bei mir verlesen: `0x435162`
(`movsx ebp, word[…+0x6DDF70]`) addiert in dieselbe Summe wie `0x43513A`
(`mov dl, byte[…+0x6E26C8]`).

⚠ `CommandOp.cs` beschreibt zu Befehl 3 den Absender `0x4342E9…0x43433E`
(F-Adressen). Im C-Bestand gibt es **beide** Absender, mit und ohne Formation,
und `0x437060` ruft die formationslose Fassung **dreimal**.

**Befehl 22 ist ein Rechteckbefehl.** `0x43A110`:

```
0x43A020(&x0, &y0, &x1, &y1)     ; Klickpunkt und Zeigerzelle nach min/max sortiert
Befehl 22:  P1 = word[0x4FA0C8]           ; die gewaehlte Einheit
            P2 = x0        P3 = y0
            P4 = x1 − x0 + 1              ; BREITE
            P5 = y1 − y0 + 1              ; HOEHE
dword[0x502ACC] = 0                       ; Platzierungsmodus aus
```

Der Behandler `0x4C33D0` packt daraus `word[Einheit + 0x40]`. Rufer ist
`0x4315D0` @`0x4319A8`, also der Zeigerzustand »Gummiband«. ⚠ **Welche
Tätigkeit** das ist (Gelände einebnen, Minenfeld, Rodung), ist **nicht**
gelesen — belegt ist nur die Form.

**`0x437F90` — der Bauplatzklick.** Er ist der Absender, den `CommandOp.cs`
für 20 und 21 schon beschreibt; ich lese ihn ganz und ergänze **Befehl 16**:

```
h = word[0x4FA0C8]
switch (byte[h + 0x0E])                    ; top_spec, im Baum »Rumpf«
  case 0x48 (72):  Befehl 20, P2/P3 = Zeigerzelle + 1, P4 = dword[0x502ACC],
                              P5 = byte[0x81A3A4] − 1
  case 0x4A (74):  Befehl 21, P2/P3 = Zeigerzelle + 1
  case 0xC6 (198): Befehl 16, P2/P3 = Zeigerzelle  (OHNE +1), P4 = dword[0x502ACC]
danach jeweils dword[0x502ACC] = 0
```

⭐ **Befehl 16 zählt die Zelle nicht hoch, 20 und 21 schon.** Die Behandler von
16 (`0x4C2F97`) und 19 (`0x4C3191`) rufen beide `0x40B070` =
`fahre(einheit, cx, cy, 0)` → `UKOL 2`; die Bauabsicht steckt nur in dem, was
danach in den Satz geschrieben wird. `0x438920` sendet Befehl 19 mit
`(Einheit, Spalte, Zeile)` und löscht ebenfalls `0x502ACC`.

---

### 6. Fahrspuren, Animation, Geschossrichtung

#### 6.1 `0x435640` — der Spurgruppen-Zuteiler, und die zweite Spurensorte

Gerufen **einmal** aus dem Kartenlader `0x41E070` @`0x41F435`. Er geht alle
8 000 Einheitenplätze ab:

```
faze == 0xFF                            -> ueberspringen (leerer Platz)
Gattung != 0                            -> word[+0x24] = 10000 ; ueberspringen
byte[0x4FA24F + SPODEK] == 0            -> ueberspringen        ; beinige Fahrwerke
word[+0x24] != 10000                    -> ueberspringen        ; hat schon eine Gruppe
g = erster Index i < 500 mit byte[0xA0A858 + i] == 0
word[+0x24] = g ;  byte[0xA0A858 + g] = 1
40 mal:  byte[0x9CB0BC + 520·g + 13·k] = 0        ; nur +0x04 jedes Satzes
byte[+0x22] = (SPODEK == 7 || SPODEK == 9) ? 1 : 0
```

Das bestätigt Abschnitt AA dreifach und unabhängig: `0x9CB0BC = 0x9CB0B8 + 4`
und Schritt 13 × 40 = **520** (»beim Zuteilen wird nur `+0x04` genullt«);
`0xA0A858` ist **sec40** mit 500 Flaggen; und `0x4FA24F` entscheidet über das
Fahrwerk.

⭐⭐ **Und es beantwortet die offene Frage aus AA:** *»Die zweite Spurensorte
(Marken 10…17, für Fahrwerk 7 und 9) kommt in keiner Prüfdatei vor.«*
Der Schalter ist **`byte[Einheit + 0x22]`**, gesetzt auf 1 genau für
`SPODEK == 7` und `SPODEK == 9`, sonst 0 — hier im Zuteiler, und nirgends
sonst in dieser Funktion.

#### 6.2 `0x4357F0` — `sec42_anlegen` mit Feinversatz

Das reichere Geschwister von `0x435A40`. Zehn Rufer, davon neun in
`0x4AEBA0…0x4AF0F9`.

```
sec42_anlegen2(spalte, zeile, dx, dy, art)                    0x4357F0
    if (art > 999) return
    i = erster Satz mit word[0x8106C6 + 10·i] == 0xFFFF   (i < 2000)
    h = 0x435BD0(spalte, zeile, &tx, &ty)     ; Zwischenposition aus KOLIK+Richtung
    word[+0x04] = h − 50                      ; Hoehe ueber Boden
    x = dx + tx ;  y = dy + ty
    solange x >= 40: x -= 40, spalte++    solange x <  0: x += 40, spalte--
    solange y >= 40: y -= 40, zeile++     solange y <  0: y += 40, zeile--
    +0x00 = spalte  +0x01 = zeile  +0x02 = x  +0x03 = y
    +0x06 = art     +0x08 = 0
```

Das deckt sich Feld für Feld mit Abschnitt AA (`+0x02/+0x03` Teilfeld 0…39,
`+0x04` i16 Höhe, `+0x06` Kennung mit `0xFFFF` = frei, `+0x08` Einzelbild,
`+0x09` ungenutzt) — und liefert die Konstante **50** für den Höhenabzug.

#### 6.3 `0x435B20` — der Animationstakt

Gerufen aus der Hauptschleife (`Anims`, `0x415CF0` @`0x4164A7`):

```
fuer i = 0 … 1999:
    k = word[0x8106C6 + 10·i] ;  if (k == 0xFFFF) weiter
    if (42 <= k <= 44 && (dword[0x4FA240] & 1) == 0)  weiter    ; nur ungerade Takte
    b = ++byte[0x8106C8 + 10·i]
    if (byte[0x7A4048 + 4·k] <= b)  word[0x8106C6 + 10·i] = 0xFFFF
```

⭐ Abschnitt AA hat »Kennungen 42…44 rücken nur jeden zweiten Takt vor«.
Jetzt steht auch die Uhr da: **`dword[0x4FA240]`, und zwar auf den ungeraden
Takten**. ⭐ Und `0x7A4048` (Schritt 4, Index = Kennung) trägt die
**Bildzahl je Animationsart** — die Abbruchbedingung. ⚠ Die Tafel liegt im
`.bss`; sie wird zur Laufzeit gefüllt, vermutlich aus der Bilddatei.
**Ausgelesen habe ich sie nicht.**

#### 6.4 ⭐⭐ `0x435E00` — die Geschossrichtung, und ein 8/8-Beleg

Zwei Rufer: `Add missile` (`0x451B40` @`0x451F40`) und der Teilchenzuteiler
(`0x4AD8B0` @`0x4ADAB8`). Sie bekommt einen sec43-Satzindex (Schritt 32):

```
dx = (byte[+0x0F]·40 + byte[+0x11]) − (byte[+0x08]·40 + (i8)byte[+0x0A])
dy = (byte[+0x10]·40 + byte[+0x12]) − (byte[+0x09]·40 + (i8)byte[+0x0B])
w  = asin( dy / sqrt(dx² + dy²) ) · 57.29578          ; 0x4FA244 = 180/pi
if (w < 0)          w += 360.0                        ; 0x4F0254 = 360
if (dx < 0)         w  = (dy < 0 ? 540.0 : 180.0) − w ; 0x4F025C / 0x4F0258
w += 22.0                                             ; 0x4F0260 = 22
if (w >= 360.0)     w -= 360.0
r = (int)(w · 0.0222222)                              ; 0x4F0264 = 1/45
if (r < 2) r += 8
return r − 2
```

Die 22 ist die halbe Sektorbreite — es wird auf das nächste 45°-Fach gerundet.
Die Vorzeichenprüfungen stehen im Code als `cmp <Rohbits>, 0x80000000`, ein
unsignierter Vergleich der IEEE-Bits: »grösser« heisst »negativ«.

⭐⭐ **Die Messlatte.** Setze die acht Nachbarversätze aus der Richtungstafel
`0x4F5AF0` (Abschnitt BB.3) in die Formel ein:

| (dSpalte, dZeile) | (0,+1) | (−1,+1) | (−1,0) | (−1,−1) | (0,−1) | (+1,−1) | (+1,0) | (+1,+1) |
|---|---|---|---|---|---|---|---|---|
| Index in `0x4F5AF0` | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
| **aus der Formel** | **0** | **1** | **2** | **3** | **4** | **5** | **6** | **7** |

**8 von 8 = 100 %.** Nullmodell (zufällige Permutation von 0…7): 1/8! =
**0,0025 %**. Damit ist zweierlei zugleich belegt: dass `0x4D6D62` **`asin`**
ist und nicht `acos` (mit `acos` bricht die Tafel schon im ersten Feld), und
dass diese Fliesskommaroutine dasselbe Richtungsschema liefert wie die
ganzzahlige `0x4338F0`. Wer sie im Nachbau ersetzt, muss die Rundung `+22`
und den `−2`-Versatz mitnehmen.

⭐ Nebenbefund: Geschosse rechnen in **1/40 Zelle in BEIDEN Achsen** — im
Unterschied zu den Zeichenroutinen, die X mit 40 und Y mit 20 skalieren.

#### 6.5 `0x434300` — Richtung von der Einheit zu einer Zelle

```
dx = (zielSpalte − byte[e+0x00]) · 40
dy = (zielZeile  − byte[e+0x01]) · 20
if (dx == 0 && dy == 0)  return byte[e + 0x03]        ; OT_HLAV, Rohrdrehung
return 0x4338F0(dx, dy)
```

⭐ Die **40 : 20** ist hier ausgeschrieben. Sie stimmt mit dem
Zeichenlisten-Erzeuger `0x42EE10` überein, der `imul di, di, 0x28` auf die
Spalte und `imul bp, bp, 0x14` auf die Zeile legt: **eine Kachel ist 40 × 20
Bildpunkte**, X hängt nur an der Spalte, Y nur an der Zeile.

---

### 7. Die Gruppenanzeige — drei Balken, zwei neue Feldpaare

Alle drei laufen die Auswahlliste ab und werden von `0x46FE10` gerufen, dessen
Zeichenketten `'Sprit gesamt '` (`0x501CE0`), `'Kontostand '`, `'Wenig '`
(`0x501D00`) und `'Niedrig '` (`0x501D08`) sind.

| C | rechnet | Einheit | Flugzeug |
|---|---|---|---|
| `0x4369F0` | Mittel von `100·wert/max` | `byte[+0x08]` / `byte[+0x29]` | `byte[+0x19]` / `byte[+0x1A]` |
| `0x436C30` | dito, `101` wenn niemand zählt | `byte[+0x39]` / `byte[+0x3A]` | `byte[+0x16]` / `byte[+0x17]` |
| `0x436E40` | **Anzahl** mit `100·wert/max < 25` | `word[+0x2E]` / `word[+0x30]` | `word[+0x1C]` / `word[+0x1E]` |

⭐ Die erste Zeile ist die **Eichung des Verfahrens**: `+0x08` (`Hp`) und
`+0x29` (`HpMax`) stehen so schon in `ENTITY_FELDER.md`. Die Funktion rechnet
also nachweislich »Ist durch Soll«, und die anderen zwei Zeilen sind damit
Feldpaare derselben Art.

⭐ Die dritte Zeile ist die **Bestätigung über Kreuz**: `word[Flugzeug + 0x1C]`
heisst in `AIR_RE.md` mit dem Namen des Spiels **`fuel`**, und daneben liegt
`+0x1E` als Höchstwert. Der Einheitenzweig nimmt an derselben Stelle
`word[+0x2E]`, das `ENTITY_FELDER.md` als **Sprit** führt, und die Zeichenkette
daneben heisst »Sprit gesamt«. Damit ist **`+0x30` = Sprit-Höchstwert** neu
belegt, und die Schranke ist **25 %**.

⭐ Neu sind ferner **`+0x39` = Munition** und **`+0x3A` = Munitions-Höchstwert**
der Bodeneinheit — über den Flugzeugzweig derselben Funktion, der dort `+0x16`
nimmt, und das nennt `AIR_RE.md` **Munition**.

⚠ `0x436C30` überspringt Infanterie (`Gattung == 1`) und alles mit Höchstwert 0;
sind es alle, gibt sie **101** zurück — die »kein Balken«-Marke. `0x436E40`
nimmt die Flugzeugtypen 13 und 14 aus.

---

### 8. Das Gas: `0x439500` und `0x4395D0`

Beide hängen am Gaswolkentakt `0x4396C0` (sec82, `0x77CAE8`, 8 × 4000).

#### 8.1 Die Ausbreitung — `0x439500`

```
gasdichte(spalte, zeile)                                        0x439500
    i = (spalte << 8) + zeile
    s = byte[0x81AA30 + i]
    if (in_map(spalte−1, zeile))  s += byte[0x81A930 + i]     ; = 0x81AA30 − 0x100
    if (in_map(spalte+1, zeile))  s += byte[0x81AB30 + i]     ; = 0x81AA30 + 0x100
    if (in_map(spalte, zeile−1))  s += byte[0x81AA2F + i]     ; = 0x81AA30 − 1
    if (in_map(spalte, zeile+1))  s += byte[0x81AA31 + i]     ; = 0x81AA30 + 1
    return s          (u16)
```

⭐ **`0x81AA30` ist die Gaskarte**, 256 × 256 Byte, Index `Spalte·256 + Zeile`
wie die *imap*. Die vier Nachbarsummanden sind derselbe Sockel mit `±0x100`
(Spalte) und `±1` (Zeile) — der Übersetzer hat sie in die Adresse gefaltet, es
ist **eine** Tafel und kein Vierertrupp. Die Randprüfung ist `0x41D1D0`
(`in_map`, gegen `dword[0x542DC4]` und `dword[0x542DF8]`).

#### 8.2 Die Wirkung — `0x4395D0`

Gerufen aus dem Wolkentakt, und zwar nur, wenn
`dword[0x4FA240] % 25 == 23`. Die Wolke merkt sich ihren Ort in **1/120 Zelle**
(`word[+0x02] / 120`, `word[+0x04] / 120`).

```
gas_wirkt(spalte, zeile)                                        0x4395D0
    dword[0x53C930] = spalte ;  dword[0x53C934] = zeile
    z = word[0xBDEA82 + 2·((spalte << 8) + zeile)]
    if (z < 8000):                                       ; eine EINHEIT
        Gattung == 1                        -> 0         ; Infanterie: nichts
        byte[+0x0C] (VRSEK) in {0x1D, 0x28} -> 0
        word[+0x2E] <= 4                    -> 0         ; schon leer
        byte[+0x10] == 0x57                 -> 0
        Klang/Wirkung 0x136 (310) an der Einheit
        word[+0x2E] = (0x4C5B30() & 3) + 1               ; Sprit auf 1..4
        return 1
    if (60000 <= z < 60300)  return 1                    ; ein GEBAEUDE
    return 0
```

Liefert sie 1, löscht der Takt die Wolke (`byte[+0x00] = 0`) und zählt
`word[0x8106B8]` herunter — **eine Wolke wirkt genau einmal**.

⭐ Der Zufall ist `0x4C5B30`, die **deterministische** Quelle, nicht
`0x4D6C70` (`rand()`). Für den Gleichlauf ist das die richtige.

⚠ **Zwei Vorbehalte, die ich nicht auflösen kann.**
1. Die Zeile liest `0xBDEA82`, nicht `0xBDEA80` — also die Zelle
   `(Spalte, Zeile+1)`. Das ist **kein Einzelfall**: `reloc_refs --addr
   0xBDEA82` findet **30 Verweise** im Programm, Schreiber wie Leser. Es gibt
   also im Original zwei Konventionen für den *imap*-Index, und `0x437F90`
   (Befehl 20/21 mit `Zelle + 1`, Befehl 16 ohne) zeigt dieselbe Verschiebung.
   **Wer den Nachbau auf eine der beiden festlegt, muss die andere mitziehen.**
2. Dass Gas ausgerechnet den **Sprit** auf 1…4 setzt, ist ungewöhnlich; die
   Feldnummer `+0x2E` ist aber dreifach gehalten (`ENTITY_FELDER.md`, der
   »Niedrig«-Balken neben »Sprit gesamt«, und der Flugzeug-`fuel`-Zweig
   derselben Funktion). Ich schreibe die Feldnummer hin, nicht meine Erwartung.

#### 8.3 `0x43AB00` — die Gastaste

```
sx = Zeigerspalte · 120 ;  sy = Zeigerzeile · 120
50 mal:  Add gas(sx, sy, rand()%60 − 30, rand()%60 − 30, 250)
```

⭐ Das bindet die Einheit der Gaskoordinaten: **1/120 Zelle**, dieselbe, in der
der Wolkentakt seine Zellnummer zurückrechnet. Die Streuung ist ±30, also ein
Viertel einer Zelle. ⚠ Der Zufall ist hier `0x43B750 → 0x4D6C70`, die
**C-Bibliotheks-`rand()`** — nicht gleichlauffähig. Das passt zu einem
Entwicklerwerkzeug und nicht zu Spielmechanik.

---

### 9. ⭐ Die Abwehrstellung — vier Funktionen und ein geschlossener Kreis

Antrieb **171** heisst laut `UNIT_STATS_RE.md` Zeile 201
»**Abwehrstellung** (Defense emplacement)«. Vier meiner Funktionen bilden ihren
ganzen Lebenslauf ab.

```
0x43AA00(e)     byte[e+0x26] −= 8   ; Angriff
                byte[e+0x27] −= 8   ; Verteidigung
                byte[e+0x2C] −= 6   ; (ungedeutet, ein Entwurfswert)
                byte[e+0x2B] −= 4   ; Reichweite

0x43AA60(e)     dieselben vier, mit +8 / +8 / +6 / +4
```

Wer sie ruft:

| Stelle | was dort steht | Wirkung |
|---|---|---|
| `0x4B1DF2` | `if (byte[e + 0x0F] == 0xAB) 0x43AA00(e)` | beim **Erschaffen** einer 171er-Einheit: Malus |
| `0x4C3619`, im Behandler **Befehl 26** | `UKOL = 0x17`, `AKCE = 7`, Klang 60, dann `0x43AA00(e)` | **abbauen** |
| `0x409888`, `move units`, Zweig `'ground A'` | `AKCE` zählt bis 8, `OT_PODV = 7`, `ANIM_SPODEK = AKCE`, dann `faze = 1`, `UKOL = 0`, dann `0x43AA60(e)` | **fertig aufgebaut**: Bonus |
| `0x43A940` | Warteschlange `0x834B70`, 50 Plätze zu 4 Byte | **Aufbau anstossen** |

```
0x43A940()          ; jeden Takt aus der Hauptschleife 0x415CF0 @0x41633C
  fuer k = 0 … 49:
     if (byte[0x834B70 + 4k] == 0) weiter
     e = word[0x834B72 + 4k]
     if (byte[e + 0x04] != 0xFF) weiter          ; erst wenn die Einheit STEHT
     byte[e + 0x14] = 0x16    ; UKOL 22 = aufbauen
     byte[e + 0x15] = 0       ; AKCE
     byte[e + 0x09] = 1       ; faze
     if (word[e + 0x2E] == 0) word[e + 0x2E] = 1
     byte[0x834B70 + 4k] = 0
     Klang 59 an der Einheit
```

⭐ Der Kreis schliesst sich an einer Stelle, die ich nicht gesucht habe: die
Behandler von **Befehl 11 (Angriff)** und **Befehl 12** brechen beide sofort ab,
wenn `UKOL` gleich `0x16` oder `0x17` ist. **Eine Stellung, die gerade auf- oder
abgebaut wird, nimmt keine Befehle an** — und die zwei Auftragsnummern, die
`0x43A940` und Befehl 26 setzen, sind genau diese zwei.

Für den Nachbau heisst das: **die Entwurfswerte in der Tafel sind die Werte der
AUFGEBAUTEN Stellung.** Eine frisch erschaffene 171er steht mit −8/−8/−6/−4 auf
der Karte und holt es erst nach acht Aufbautakten ein.

⚠ `+0x2C` bleibt ungedeutet. Belegt ist nur: es wird an fünf Stellen beim
Erschaffen gesetzt (`0x4B1BF2`, `0x4B2E58`, `0x4B3790`, `0x4B3BE1`, `0x4B3E5B`
— dieselben Stellen wie Angriff/Verteidigung/Reichweite), von der Schussroutine
`0x40B46E` gelesen und in vier Fenstern angezeigt. Es ist also ein
**Kampf-Entwurfswert** neben Angriff, Verteidigung und Reichweite.

---

### 10. Die drei Entwicklertasten (`0x412D10`)

`0x412D10` (6 760 B, Zeichenketten `Group | SHIP_PROD | Station:%d |
c:\vmodes.txt`) ruft drei Funktionen, die alle mit `rand()` arbeiten und darum
**nicht gleichlauffähig** sind:

* `0x43AB00` — 50 Gaswolken an der Zeigerzelle (siehe 8.3).
* `0x43AB70` — geht das **sichtbare Kachelfeld** ab (`0x5387AC`/`0x5387B0`
  plus `0x5387C0`/`0x5387C4`) und ruft für jede Zelle, deren *imap*-Wert in
  `[50000, 56000)` oder `[61000, 64000)` liegt, `Zasah` (`0x40C9A0`) mit dem
  Wert 40 050. ⚠ Was in diesen zwei *imap*-Bändern steht, habe ich **nicht**
  nachgeschlagen; die Funktion setzt vorher `dword[0x53C930]`/`0x53C934` auf
  Spalte und Zeile, dieselben zwei Zellen wie `0x4395D0`.
* `0x43AC60` — geht die 1 000 Plätze des **eigenen** Spielers durch
  (`byte[0x4FA284] · 1000`) und schreibt in `byte[+0x28]`: bei jedem fünften
  Wurf `0xFF`, sonst `rand() % 150 + 104`. ⚠ `ENTITY_FELDER.md` führt `+0x28`
  als obere Hälfte von `exp`. Ein Wertebereich 104…253 plus `0xFF` **passt
  nicht zu Erfahrung**. **Ungedeutet.**

---

### 11. Der Rest, kurz

* **`0x42DEF0(spieler)`** — geht `spieler·1000 … +999` ab und liefert den
  ersten Platz mit `faze == 0xFF`. ⚠ **Findet er keinen, gibt er `0` zurück**:
  `xor ax, ax` löscht nur die unteren 16 Bit, und der Zähler steht bei
  `(spieler+1)·1000 < 65536`, also bleibt 0 übrig. Beide Rufer — `0x43B1FD` und
  `0x4C14D0` — nehmen das Ergebnis **ungeprüft**. Ist ein Spieler voll,
  überschreibt die neue Einheit also **Platz 0 von Spieler 0**. ⚠ Die
  Fehlerzeile »Incredible error …no free place for new robot« in `0x4C1480`
  gehört zu einer *anderen* Prüfung, davor.
* **`0x43A2F0(einheit, flagge, &spalte, &zeile)`** — sucht in der Schablone
  `0x79A008` (je Schritt zwei vorzeichenbehaftete Byte, bis zu **20 000**
  Schritte) die erste Zelle um die Einheit herum, deren *imap* `0xFFFE` ist
  (`flagge == 0`) bzw. `0xFFFE`, `0xFFFD` oder ein noch nicht voller Trupp
  (`flagge != 0`, geprüft über `0x433DF0`). Drei Rufer, alle in **`0x43A420`
  (Teleport, Befehl 23)**.
* **`0x43A880(h)` / `0x43A8A0()`** — `byte[0x77AC48] = 6`, `word[0x4FA0D8] = h`;
  der Takt zählt herunter und setzt bei 0 wieder `0xFFFF`. Eine **Blinkmarke
  über sechs Bildtakte**, gesetzt aus der Schussroutine `0x40BB00` und zweimal
  aus `0x437060`.
* **`0x439170(gruppe, flughafen)`** — sammelt alle Flugzeuge mit
  `typ != 0`, `word[+0x28] == flughafen` und `byte[+0x26] == gruppe` als Griffe
  `index + 20000` in eine Liste bei `0x833A16 + 422·gruppe`, füllt auf 200
  Einträge mit `0xFFFF` auf und kopiert die Zeichenkette `'Airplanes'`
  (`0x4F7A94`) nach `0x833A00 + 422·gruppe`. Der Satz ist also **422 Byte:
  22 Byte Name, dann 200 `u16`-Griffe**. Rufer: `0x4485D0` (Einsatzbesprechung).
* **`0x4364A0()`** — schickt jeden ausgewählten **Bomber** (`typ == 1`,
  `Munition > 0`) über `0x427930` (Befehl 6) auf das Ziel `word[0x502AD8]`.
  ⚠ Die Zielkoordinate holt sie aus `byte[0x591EF0 + 68·Ziel]` und
  `0x591EF2` — das ist der vorgespannte Flugzeugsockel, also `+0x00`/`+0x02`
  eines **Flugzeugs**. Der Zweig ist damit nur für Luftziele richtig; eine
  Absicherung auf `Ziel >= 20000` steht **nicht** im Code (`0x437741` prüft
  nur, ob überhaupt eine Gruppe gewählt ist).
* **`0x436420(e)`** — setzt `byte[e + 0x42] = 1`. Ein Rufer, in der
  Schussroutine `0x40BB00`. **Ungedeutet.**
* **`0x434380`** (tot) ist trotzdem lehrreich: sie zeigt, wie eine Einheit vom
  Belegungsgitter genommen wird — `word[0xBDEA80 + 2·(Spalte·256 + Zeile)] =
  0xFFFE`, bei Gattung 0 mit `POHYB != 0xFF` **zusätzlich die Zelle in
  Fahrtrichtung** (über `0x4F5AF0`), bei Gattung 1 über `0x433C20`
  (»Unit missing«) mit `imap − 10000` als Truppnummer.
* **`0x438D10`** ist ein reines `ret` mit `int3`-Füllung, und der einzige
  Verweis darauf ist der Thunk `0x4018F7`, den niemand ruft.

---

### Berichtigungen an bestehenden Dokumenten

1. ⭐⭐ **`OFFENE_FRAGEN.md`, BE.9 Punkt 4** (»⚠ Was sec41 (1000 × 10 B)
   enthält«) — **erledigt**: sec41 sind die Wracks. Siehe Abschnitt 1.
2. ⭐⭐ **`OFFENE_FRAGEN.md`, BE.9 Punkt 2** — *»Die Tafel `0x591F00`
   (Schritt 68), die `0x433460` für Befehl 8 abfragt: 12 Lesestellen, 0
   Relokations-Schreiber — sie wird über einen Zeiger gefüllt, und genau das
   findet die Relokationstafel nicht.«*
   **Die Begründung ist falsch, und die Tafel ist keine.** `0x591F00` ist der
   **vorgespannte Sockel** der Flugzeugtafel: `0x591EF0 + 68·20000 =
   0x6DDF70`, also `0x591F00 = 0x6DDF70 + 0x10` = das Feld **`uk`**. Es gibt
   keine Schreiber, weil über den echten Sockel `0x6DDF70` geschrieben wird.
   Abschnitt AM.3 hat diesen Trick schon (»⚠ Warnung: `0x591EF0` sieht wie eine
   eigene Tafel aus, ist aber nur der vorgespannte Versatz«) — BE.9 hat ihn
   nicht mitbekommen. `0x433010` benutzt gleich vier dieser Adressen
   (`0x591EF9`, `0x591EFF`, `0x591F00`, `0x591F01` = `+0x09`, `+0x0F`, `+0x10`,
   `+0x11`). Auch die Zeile `OFFENE_FRAGEN.md:8643` (»⚠ ungeklärte Tafel
   `0x591F00` / `0x590F60`, Schritt 68«) ist damit aufzulösen.
3. ⭐⭐ **`Scripts/Simulation/Commands/CommandOp.cs`, `OursAttack = 2001`** —
   die Begründung (»für einen solchen Befehl haben wir im Original KEINEN
   Behandler nachgewiesen«) ist überholt. **Busbefehl 11 ist der
   Angriffsbefehl**, Absender `0x4353F0`, Behandler `0x4C2DDD` → `order()`
   `0x410220`. Siehe Abschnitt 2.
4. ⭐ **`aekernel-tools/CAMPAIGN_RE.md`, §10.2**, der Satz *»⚠ Für Busbefehl 11
   gilt das nicht automatisch: das ist eine andere Routine, und ihre Argumente
   sind nicht gelesen«* — sie ist **keine andere Routine**, der Behandler reicht
   `0x410220` unverändert durch. Und *»⚠ Ungelesen bleibt `UTOK_NA`«* ist
   erledigt: vier Bereiche, alle am Absender belegt (Abschnitt 2.2), dazu
   `+0x38` = Zeile des Bodenziels.
5. ⭐ **`aekernel-tools/AIR_RE.md`, Zeile 36** — `0x6DDF7F` (`+0x0F`) steht dort
   als »⚠ ungelesen«. Es ist die **Auswahlmarke** des Flugzeugs; `0x433010`
   nullt sie beim Abwählen an derselben Stelle, an der der Einheitenzweig
   `OZNACEN` nullt.
6. ⚠ **`aekernel-tools/AIR_RE.md`, Widerspruch um `+0x09`** — Zeile 209 nennt
   `0x6DDF79` den **Besitzer**, die Zeilen 144/560/716 nennen `+0x09` `faze`.
   `0x433010` vergleicht `byte[0x591EF9 + 68·h]` mit `byte[0x4FA284]` (dem
   eigenen Spieler); das stützt »Besitzer«. Eine der beiden Lesungen ist falsch
   und sollte aufgelöst werden, bevor sich der Nachbau darauf stützt.
7. ⭐ **`OFFENE_FRAGEN.md`, BB.8 Punkt 2** (»Die Felder `+0`/`+1` im 22-Byte-Satz
   der Infanteriezellen«) — `+0x00` ist die **Anzahl der Mann (0…9)**, belegt
   an `0x433FB8` (`cmp al, 9`). Die Lesung »col, row, 9 × u16« aus
   `zz_deutung.py` ist um ein Feld verschoben. Abschnitt AU.11 hatte es schon
   richtig (»`+0` Anzahl, `+1…+3` Zelle«) — BB.8 nicht.
8. ⭐ **`OFFENE_FRAGEN.md`, Abschnitt AA, »Was offen bleibt«** — *»Die zweite
   Spurensorte (Marken 10…17, für Fahrwerk 7 und 9)«*: der Schalter ist
   `byte[Einheit + 0x22]`, gesetzt im Zuteiler `0x435640` @`0x4356C9`.
9. **Ergänzungen zu `ENTITY_FELDER.md`** (Abschnitte 2.2, 6.1 und 7):
   `+0x22` Spurvariante · `+0x30` Sprit-Höchstwert · `+0x38` Zeile des
   Bodenziels · `+0x39` Munition · `+0x3A` Munitions-Höchstwert.

---

### Die drei »fehlenden« C-Funktionen — Fehlalarm, alle drei

Auftrag des Koordinators: `0x432A00`, `0x433397` und `0x437980` haben laut
Vollerhebung **keine Entsprechung in F** und wären damit der zwölfte belegte
Auslieferungsunterschied. `cfind.py` bestätigt das auch für alle drei
(»bester Rest nur 40 % / 52 % / 42 %«).

**Es ist keiner.** Alle drei sind **Sprungziele innerhalb einer grösseren
Funktion**, kein Funktionsanfang:

| Adresse | steckt in | wird erreicht durch |
|---|---|---|
| `0x432A00` | `0x4315D0` (5 376 B) | `jmp 0x432A00` @`0x4329D9`; dazu vier weitere `jmp` @`0x43282B`, `0x43289C`, `0x4328E8`, `0x432934` |
| `0x433397` | `0x4331E0` (`aere.py fs` sagt es selbst) | `jmp 0x433397` @`0x433379` |
| `0x437980` | `0x437060` (2 517 B) | `jmp 0x437980` @`0x43795E` |

`rufer.py` liefert für `0x432A00` ausschliesslich `jmp`-Einträge und für
`0x437980` **gar nichts**; an keiner der drei Adressen steht ein `call`.
Die krumme Adresse `0x433397` war, wie vermutet, das leichteste Opfer — aber
die zwei »sauber liegenden« sind es genauso.

**Die Gegenprobe, die den Befund erst schliesst** — die drei umschliessenden
Funktionen gibt es in F, und zwar eindeutig:

```
0x4315D0  ->  0x430710   eindeutig   (Abstand −0xEC0)
0x4331E0  ->  0x432320   eindeutig   (Abstand −0xEC0)
0x437060  ->  0x4361C0   eindeutig   (Abstand −0xEA0)
```

**Warum `cfind` sie trotzdem meldet:** ein Abdruck, der mitten in einer Funktion
beginnt, kann in F nur passen, wenn dort ein Sprungziel auf demselben Befehl
liegt — und Sprungziele wandern zwischen den Fassungen. Die Prozentzahl misst
dann Rauschen, genau wie die Einweisung für »ungenau« warnt.
**Für die Vollerhebung heisst das: die 25 Meldungen sind erst Befunde, wenn
geprüft ist, dass die Adresse ein Funktionsanfang ist.** In meinem Revier
waren es 0 von 3.

---

### Bauaufgaben, die daraus folgen

1. ⭐⭐ **Den Angriffsbefehl auf die echte Nummer umstellen.**
   `CommandOp.OursAttack = 2001` → **11**, mit dem Satz
   `(P1 Einheit, P2 Spalte, P3 Zeile, P4 UTOK_NA, P5 Zeile-bei-Bodenziel)`
   und dem Griffraum aus 2.2. Der Behandler ist `order()`, den wir als
   `MissionOrder` schon haben. Damit wird der Angriff **gleichlauffähig** und
   geht in Wiederholung und Netz (`< 800` bzw. `<= 1000`), was er unter 2001
   nicht tut — `CommandOp.cs` sagt das selbst.
2. ⭐⭐ **Die Wracks bauen.** Sie fehlen uns ganz. 1000 Plätze, Alterung alle
   10 Takte, Lebensdauer 2 550 Takte, 16 Varianten, Zeichnung im Bodendurchgang
   (Elementart `0x11`). Der Ort ist nicht die Zellmitte, sondern die
   Ellipsenformel aus 1.3 mit `KOLIK` und `POHYB` der sterbenden Einheit.
   Ein abstürzendes Flugzeug macht **sechs** Stück.
3. ⭐ **Der Angriff formiert sich um das Ziel.** Wir setzen alle Einheiten auf
   dieselbe Zelle; das Original rechnet `eigenePosition + Zielposition −
   Schwerpunkt`. Das ist derselbe Ausdruck wie beim Bewegungsbefehl, nur mit
   der Zielzelle statt der Klickzelle.
4. ⭐ **Die Abwehrstellung (Antrieb 171)** braucht ihre zwei Zustände: Malus
   beim Erschaffen, acht Aufbautakte (`UKOL 22`, `AKCE` 0…7, `OT_PODV = 7`),
   dann +8 Angriff / +8 Verteidigung / +6 (`+0x2C`) / +4 Reichweite; Befehl 26
   baut ab und nimmt den Bonus. Während `UKOL` 22/23 nimmt sie **keine**
   Angriffs- und Stoppbefehle an.
5. **Das Gas wirkt nur alle 25 Takte** (Rest 23) und **jede Wolke nur einmal**;
   Ausbreitung über die Fünf-Zellen-Summe `0x439500`; Wirkung auf Bodeneinheiten
   ist `Sprit := rand()%4 + 1` mit dem **deterministischen** Zufall `0x4C5B30`.
   Infanterie ist immun, Gebäude gelten immer als Treffer.
6. **Die Gruppenanzeige** hat drei Balken mit belegten Feldern (Abschnitt 7),
   darunter »Niedrig« = Anzahl unter **25 %** Sprit, und »kein Balken« = 101.
7. **Der Spurgruppen-Zuteiler läuft einmal beim Kartenladen**, nicht laufend —
   und er setzt `byte[+0x22]` für die Fahrwerke 7 und 9 auf 1 (zweite
   Spurensorte).
8. **Die Geschossrichtung** ist die `asin`-Formel aus 6.4, nicht die
   ganzzahlige Tafel. Bei kurzen Wegen unterscheiden sich die beiden.
9. ⚠ **Die Zellverschiebung um eins** (`0xBDEA82`, `Zelle + 1` bei Befehl 20/21,
   `Zelle` bei Befehl 16) ist im Original inkonsistent. Vor dem Nachbau
   entscheiden, welche Konvention gilt, und **beide** Stellen gleich ziehen.

---

### Was ungedeutet bleibt

1. ⚠ **`byte[Einheit + 0x2C]`** — der vierte Wert, den die Abwehrstellung um 6
   ändert. Belegt: fünf Setzer beim Erschaffen (dieselben wie Angriff /
   Verteidigung / Reichweite), gelesen in der Schussroutine `0x40B46E` und in
   vier Fenstern. Es ist ein Kampf-Entwurfswert; **welcher**, ist offen.
2. ⚠ **`byte[Einheit + 0x42]`** — `0x436420` setzt es auf 1, gerufen einmal aus
   der Schussroutine. Sonst nichts gesehen.
3. ⚠ **Befehl 22** (`0x43A110`): die Form ist gelesen (Einheit + Rechteck aus
   Ecke und Breite/Höhe), die **Tätigkeit** nicht. Der Behandler `0x4C33D0`
   packt sie in `word[Einheit + 0x40]` — und `+0x40` ist laut BE.9 Punkt 5
   ohnehin dreifach belegt.
4. ⚠ **Die zwei *imap*-Bänder `[50000, 56000)` und `[61000, 64000)`**, die
   `0x43AB70` mit `Zasah` beharkt, und die Bedeutung des Wertes **40 050**, den
   sie als ersten Parameter mitgibt.
5. ⚠ **`byte[Einheit + 0x28]`** — `0x43AC60` würfelt dort 104…253 bzw. `0xFF`
   hinein. Zu `exp` (obere Hälfte, `ENTITY_FELDER.md`) passt der Wertebereich
   nicht.
6. ⚠ **`0x7A4048`** (Bildzahl je Animationsart, Schritt 4) liegt im `.bss` und
   ist in der Datei leer. Woher sie gefüllt wird, habe ich nicht verfolgt.
7. ⚠ **`UKOL 1`** — meine Deutung »handgesteuert« ist eine **markierte
   Vermutung ohne Zahl**. Der Behandler von Befehl 5 (`0x4C29C3`) ist nicht zu
   Ende gelesen; ebensowenig die Behandler 4, 12, 19, 22, 26 und 30, die ich
   nur so weit angesehen habe, wie es meine Absender erklärt.
8. ⚠ **Die Merker `byte[0xA1833B]` und `byte[0xA18330]`**, die `0x433750`
   einlöst: **wer sie setzt**, habe ich nicht gesucht. Es sind zwei
   Knopf-/Tastenaktionen, die einen Bildtakt lang liegen bleiben.
9. ⚠ **`word[0x502AD8]`** — 32 Schreiber, 20 Leser, fast alle in `0x437060`.
   Ich lese es als »der Griff unter der Maus«, weil `0x4364A0` und die
   Befehle 14/15 es als Zielgriff verwenden und der Zeigerzeichner `0x4A9B1C`
   es liest. Eine Zahl dahinter habe ich nicht.
10. ⚠ **Die vier Fensterrufe** in `0x433010` (`0x44F7C0` am Anfang, `0x4500B0`
    am Ende) und `0x433750` (`0x429760`, `0x44FE10`) sind nicht aufgemacht.
11. ⚠ **`0x437060` selbst** (2 517 B) ist der Verteiler, an dem die Hälfte
    meines Reviers hängt, und er ist **ungelesen**. Er sendet unter anderem die
    Befehle 14, 15, 23 und 29 direkt. Wer ihn liest, bekommt vermutlich die
    Namen der Befehle 4, 5, 12, 14, 15, 19, 22 und 26 mit.

---

## BL. Revier 4: 0x43AD00 … 0x442ED0

62 Funktionen, 10 944 Byte. Gelesen am 22.08.2026.

**Kurzfassung.** Das Revier ist zu zwei Dritteln die **Fenster-Bedienmaschine**
(Öffnen, Suchen, Neuzeichnen, In-den-Schirm-Zwingen, Aufsetzen auf die
DirectDraw-Oberfläche), zu einem Viertel sind es die **Ausführer der
Gebäudebefehle** aus dem Befehlsbus, dazu drei kleinere Bündel: **das
Cheat-System**, die **CD-Prüfung** und ein **PCX-Lader**.

⭐⭐ **Zwei Funde, die über das Revier hinausgehen:**

1. **`0x43C960` gibt es in F nicht** — vierfach belegt (Abschnitt 12). Damit
   fehlt F ein ganzer Schritt der Gebäudeeroberung.
2. Im selben Rufer (`0x43CA50`) sitzt ein **zweiter C-eigener Block**
   (`0x43CFF5…0x43D04B`, 87 Byte), den `GAMESTATE_RE.md` als »both conditions
   unidentified« führt — **beide Bedingungen sind jetzt benannt**, und der Block
   fehlt in F ebenfalls.

⭐ **Fünfte, unabhängige Stütze für den zehnten Unterschied (BA.8):**
`0x441190` (Fenster in den Schirm zwingen) nimmt in C **drei** Fensterarten aus
(9, 35, **48**), in F nur **zwei** (9, 35). Der fehlende Wert ist genau
`0x30 = 48`.

---

### Adresstafel

F durchweg mit `cfind.py` bestimmt und bei jedem strittigen Fall von Hand
nachgelesen. ⚠ Die vier mit **(korr.)** markierten hat `cfind` falsch gepaart —
die eingetragene F-Adresse ist von Hand belegt (Abschnitt 13.9).
Die Ruferzahlen sind die **aufgelösten** (Stummel überspringend).

| C | F | Byte | Rufer | Was sie ist |
|---|---|---:|---:|---|
| `0x43AD00` | `0x439E70` | 112 | 1 | ⭐ **Cheat »Waffen«**: +1000 auf `+0x28` aller eigenen Lagergebäude |
| `0x43AD70` | `0x439EE0` | 112 | 1 | ⭐ **Cheat »Fahrwerke«**: +1000 auf `+0x2A` |
| `0x43ADE0` | `0x439F50` | 112 | 1 | ⭐ **Cheat »Specials«**: +1000 auf `+0x2C` |
| `0x43B080` | `0x43A1F0` | 240 | 1 | ⭐ **»Check Hopla«** — den Bildschirm um 180° drehen |
| `0x43B170` | `0x43A2E0` | 32 | **0** | ⚠ ausgehöhlter Rumpf, gibt **immer 0** zurück; **kein Rufer** |
| `0x43B350` | `0x43A4C0` | 160 | 1 | Mitnahmeliste `0x9937B8` → 20 Einheitensätze nach sec98 `0x81A410` |
| `0x43B3F0` | `0x43A560` | 80 | 10 | `waffenfeld(entwurf)` — Entwurfstafel `0x5045BC` (58 B) → Waffentafel `0x4F98FC` (22 B) |
| `0x43B440` | `0x43A5B0` | 320 | 11 | ⭐ **CD-Laufwerk suchen**: `d:`…`z:` nach `X:\cw.id`, Fund → `byte[0x7A4FE8]` |
| `0x43B760` | `0x43A8D0` | 80 | 1 | `datei_vorhanden(name)` **(korr.)** |
| `0x43B7B0` | `0x43A920` | 80 | 1 | `dateigroesse(name)` **(korr.)** |
| `0x43B800` | `0x43A970` | 320 | **0** | ⭐ **PCX-Lader** (Kopf 128 B, RLE `0xC0`); **kein Rufer** |
| `0x43B940` | `0x43AAB0` | 128 | 1 | `datei_ganz_lesen(name, puffer, laenge)` |
| `0x43B9C0` | `0x43AB30` | 112 | **0** | Gebäude-Nebensätze (sec23/24/25/26/28/30/31) auf `0xFFFF`; **kein Rufer** |
| `0x43C960` | ⚠ **fehlt** | 240 | 1 | ⭐⭐ **Eroberung: Ankerzelle räumen** — Abschnitt 12 |
| `0x43F5F0` | `0x43E5F0` | 160 | 2 | `ist_2x2_wasser(spalte, zeile)` — imap `0xBDEA80`, Wert `0xFFFC` |
| `0x43F690` | `0x43E690` | 160 | 2 | `ist_4x4_wasser(spalte, zeile)` |
| `0x43F820` | `0x43E820` | 144 | 1 | **Befehl 509** Fabrik-Lagerausbau (Zustand 3, Preis sec24 `+0x0A`) |
| `0x43F8B0` | `0x43E8B0` | 144 | 1 | **Befehl 510** Fabrik-Produktionsausbau (Zustand 4, Preis `+0x0C`) |
| `0x43F940` | `0x43E940` | 112 | 1 | **Befehl 511** Fabrik-Reparatur **umschalten** 1↔0 |
| `0x43F9B0` | `0x43E9B0` | 144 | 1 | **Befehl 536** Flughafen-Ausbau (Zustand 2, Preis sec27 `+0x06`) |
| `0x43FA40` | `0x43EA40` | 128 | 1 | **Befehl 515** Minen-Lagerausbau (Zustand 3, Preis sec28 `+0x0E`) |
| `0x43FAC0` | `0x43EAC0` | 128 | 1 | **Befehl 516** Minen-Produktionsausbau (Zustand 4, Preis `+0x10`) |
| `0x43FB40` | `0x43EB40` | 96 | 1 | **Befehl 517** Minen-Reparatur **umschalten** 1↔0 |
| `0x43FBA0` | `0x43EBA0` | 176 | 1 | **Befehl 519** Fabrik reparieren (Zustand 2, **−1/30 TP**) |
| `0x43FC50` | `0x43EC50` | 192 | 1 | **Befehl 520** Flughafen reparieren (Zustand 1, **−1/30 TP**) |
| `0x43FDC0` | `0x43EDC0` | 176 | 1 | **Befehl 522** Mine reparieren (Zustand 2, **−1/30 TP**) |
| `0x43FE70` | `0x43EE70` | 80 | 1 | **Befehl 523** Fabrik → 0 — ⚠ **ohne Absender (tot)** |
| `0x43FEC0` | `0x43EEC0` | 80 | 1 | **Befehl 524** Flughafen → 0 |
| `0x43FF50` | `0x43EF50` | 64 | 1 | **Befehl 526** Mine → 0 — ⚠ **ohne Absender (tot)** |
| `0x43FF90` | `0x43EF90` | 272 | 1 | ⭐ `freie_andockplaetze(gebaeude)` — 6er-Schlange in sec23/25/30 |
| `0x4400A0` | `0x43F0B0` | 240 | 1 | `entfernung_tuer_zu_einheit(gebaeude, einheit)`, `(int)sqrt` |
| `0x440190` | `0x43F1A0` | 224 | 2 | Gebäude aus allen 400 sec48-Umschlagsätzen streichen |
| `0x440930` | `0x43F940` | 128 | 1 | Gebäudenamen `+0x17` = `"1 "` … `"256 "` (Missionsstart) |
| `0x440DD0` | `0x43FDE0` | 192 | 2 | ⭐ **alle Fenster auf den Rückpuffer** |
| `0x440E90` | `0x43FEA0` | 176 | 4 | ⭐ **alle Fenster auf die primäre Oberfläche** |
| `0x440F40` | `0x43FF50` | 240 | 1 | ⭐ nur **Art 45 »Laden…«** direkt auf die primäre Oberfläche |
| `0x441030` | `0x440040` | 240 | 1 | ⭐ nur **Art 47 »Synchronisieren…«** direkt auf die primäre Oberfläche |
| `0x441120` | `0x440130` | 112 | **82** | ⭐ `bedienelement_greifen(fenster, element)` |
| `0x441190` | `0x4401A0` | 224 | **52** | ⭐⭐ `fenster_in_den_schirm_zwingen(nr)` — **hier sitzt der C/F-Unterschied** |
| `0x4412E0` | `0x4402E0` | 48 | **32** | ⭐ `fenster_neu_zeichnen(nr)` = Verteiler + Schirmzwang |
| `0x441310` | `0x440310` | 128 | 1 | »gibt es Art 3 (Karte) mit Unterart n?« → 0/1 |
| `0x441390` | `0x440390` | 144 | 1 | Art 3 mit Unterart n **schliessen** |
| `0x441420` | `0x440420` | 144 | 4 | Geschwindigkeit → `dword[0x892128]`, Art 34 neu zeichnen |
| `0x4414B0` | `0x4404B0` | 128 | 1 | Art 12 (CD-Spieler) neu zeichnen |
| `0x441530` | `0x440530` | 192 | 1 | Art 46, `byte[+0x0C] = arg`, neu zeichnen |
| `0x4415F0` | `0x4405F0` | 240 | 2 | ⭐ Art 9 nur bei **geänderter Auswahl** neu zeichnen (Merker `0x4FAEF0`) |
| `0x4416E0` | `0x4406E0` | 128 | 1 | Art 16 neu zeichnen |
| `0x441760` | `0x440760` | 176 | 1 | Art 12: `+0xACA8/+0xACAC/+0xACB0 = 0` |
| `0x441BB0` | `0x440BB0` | 192 | 1 | **Art 43 öffnen** (Vollbild 640×480) + Bildschirmmodus 3 |
| `0x441C70` | `0x440C60` | 144 | 2 | **Art 46 öffnen** (»Mission beendet«) |
| `0x441D00` | `0x440CF0` | 128 | 1 | **Art 42 schliessen** (»Warten auf…«) |
| `0x441D80` | `0x440D70` | 320 | 2 | **Art 42 öffnen**, mittig, y = Höhe/5 |
| `0x441EC0` | `0x440EA0` | 272 | 1 | **Art 40 öffnen** (»Pause«), mittig, y = Höhe/5 |
| `0x4421A0` | `0x441190` | 208 | 1 | **Art 38 öffnen** (Mitnahme) + Liste `0x9937B8` leeren |
| `0x442270` | `0x441260` | 272 | 1 | **Art 36 / Unterart 10 öffnen** (Namenseingabe, 40 Zeichen) **(korr.)** |
| `0x442380` | `0x441370` | 288 | 1 | **Art 36 / Unterart 2 öffnen**, »Wählen Sie einen Namen für dieses Teil«, 19 Zeichen |
| `0x442670` | `0x441650` | 368 | 2 | **Art 37 öffnen** (Gefechtsvorbereitung) + Palette + Takt 0 |
| `0x4427E0` | `0x4417C0` | 256 | 6 | **Art 35 öffnen** (Hauptmenü) — fest **x = 25**, y = Höhe − h − 20 |
| `0x4428E0` | `0x4418C0` | 288 | 2 | ⭐ **die fünf Auflösungen prüfen** → `byte[0x9937E8 … +4]` |
| `0x442A00` | `0x4419E0` | 304 | 3 | **Art 34 öffnen** (Einstellungen) |
| `0x442E10` | `0x441E00` | 192 | 1 | **Art 27 öffnen** (Gebäudeliste) **(korr.)** |
| `0x442ED0` | `0x441EB0` | 224 | 1 | ⚠ sucht **Art 23**, legt aber **Art 21** an — in beiden Bauten |

---

### 1. ⭐⭐ Das Cheat-System — fünfzehn Wörter, vier lebende Arme, drei Tastengriffe

`0x43AD00`, `0x43AD70`, `0x43ADE0` sind dreimal derselbe Rumpf:

```
cheat_lager_fuellen(versatz):                  ; 0x28 / 0x2A / 0x2C
    ich = byte[0x4FA284]
    fuer k = 0 … 254:
        satz = 0xC06914 + 76·k                 ; Schrittweite aus der lea-Kette:
                                               ; ebx=k, ebp=9k, ebx=19k, eax=76k
        wenn byte[satz+0x01] != ich: weiter     ; nicht meins
        typ = byte[satz+0x00]
        wenn typ nicht in {1, 9, 16}: weiter    ; Basis, Flughafen, Werft
        word[satz + versatz] += 1000
```

⭐⭐ **Das Nullmodell liefert das Spiel selbst.** Die drei Rufer stehen im
Tastenbehandler und geben je eine Meldung aus:

| C | Feld | Meldung (C-Adresse) |
|---|---|---|
| `0x43AD00` | `+0x28` (`0xC0693C`) | »**Cheat: Waffen** hinzugefügt« `0x4FB2E0` |
| `0x43AD70` | `+0x2A` (`0xC0693E`) | »**Cheat: Fahrwerke** hinzugefügt« `0x4FB344` |
| `0x43ADE0` | `+0x2C` (`0xC06940`) | »**Cheat: Specials** hinzugefügt« `0x4FB3A8` |

`GAMESTATE_RE.md` führt genau diese Zuordnung (`0x28→W, 0x2a→F, 0x2c→S`,
gemessen ab `0xC06914`) seit Wochen als *erschlossen*. **Jetzt ist sie aus den
Zeichenketten des Programms selbst belegt: 3 von 3.** Ein geratenes Tripel aus
den vier Lagerfeldern (W/F/S/Terranium) trifft mit Wahrscheinlichkeit 1/24.

#### 1.1 Wie man drankommt

Der Erkenner ist `0x43AE50` (C) / `0x439FC0` (F), 67 Befehle, **normiert
befehlsgleich**. Er hängt am Tastenbehandler und arbeitet eine Tafel ab:

```
wenn dword[0x539234] != 0 (Netzspiel):  sofort raus   ; ⭐ Cheats sind im Netz gesperrt
taste nach Grossbuchstabe; nur A…Z und Leerzeichen zaehlen
Tafel C 0x4FA100 / F 0x4F9108, Schrittweite 21:
     +0x00…+0x13  das Wort   ·   +0x14  der Fortschrittszaehler
fuer k = 0 … 14:
    wenn Wort[k][zaehler[k]] == taste:  zaehler[k]++
         wenn danach Wortende:  zaehler[k]=0 ; k-3 in 0…11 -> Sprungtafel 0x43AFD4
    sonst zaehler[k] = 0   (bzw. 1, wenn die Taste der Wortanfang ist)
```

**Die fünfzehn Wörter, im Klartext aus `.data`** (⚠ Dateiversatz für `.data` ist
`VA − 0x402200`, **nicht** `VA − 0x400C00` wie in `.text`):

`IDKFA` · `GOBGAS` · `FIREBUG` · `HLAVOUDOLU` · `PROFIS` · `EIDOS` · `FANFAR` ·
`TWISTER` · `GUNS` · `WHEELS` · `SPECIALS` · `COLUMBUS` · `ENABLEDEVEL` ·
`ARMLEUCHTER` · `SETLOWRES`

⭐ **Nur vier davon haben einen Arm.** Die Sprungtafel `0x43AFD4` deckt die
Wörter 3…14; acht ihrer zwölf Einträge zeigen auf `0x43AFB9`, also auf »nichts«,
und die Wörter 0…2 haben gar keinen Arm (`sub ecx,3` läuft unter null).

| Wort | Arm | Wirkung |
|---|---|---|
| **`HLAVOUDOLU`** (tschech. »Kopf nach unten«) | `0x43AEE5` | `byte[0x4FA0D0] ^= 1`; beim Einschalten `malloc(B·H)` → `dword[0x4FA0D4]`, beim Ausschalten `free` |
| **`ENABLEDEVEL`** | `0x43AF28` | `byte[0x4FA0C0]` umschalten, Meldung »Developers' cheats **enabled/disabled**« |
| **`ARMLEUCHTER`** | `0x43AF61` | Meldung »**Pfuschmodus aktiviert**« (`0x4FD34C`), **`byte[0x4FA0C4] = 1`** |
| **`SETLOWRES`** | `0x43AF84` | Bildschirmmodus 0 (640×480), `dword[0x991818]=0`, `byte[0x5385E4]=0` |

⭐ `byte[0x4FA0C4]` hat **genau einen Schreiber im ganzen Programm**
(`reloc_refs --addr`: 1 Schreiber `0x43AF78`, 8 Leser, alle im Tastenbehandler
`0x412E30`). Es ist der Freigabeschalter der Tastencheats.

#### 1.2 Welche Tasten

Die Tastenverteilung ist `eax = VK − 9`, Schranke `0x88`, Indextafel `0x414644`
(137 B) → Sprungtafel `0x4145A4` (40 Arme) (`0x412FD8…0x412FEE`).

> **Nullmodell für »VK = Position + 9«, dreifach.** Arm 38 sitzt auf Position 136
> → VK 145 = ROLLEN, und `OFFENE_FRAGEN` nennt `0x413E2E` bereits »Taste ROLLEN
> (Bildschirmfoto)«. Arm 36 → Position 113 → VK 122 = **F11**, dort steht
> `0x413DDC` = »Taste F11«. Arm 17 → Position 73 → VK 82 = **R**, dort steht
> `0x413708` = »Taste Strg+R«. **3 von 3 vorhandenen Marken treffen.**

Damit:

| Arm | Taste | Rufstelle | Cheat |
|---:|---|---|---|
| 5 | **A** | `0x4131CC` | Specials |
| 11 | **H** | `0x4135FE` | Fahrwerke |
| 19 | **U** | `0x41386E` | Waffen |

Alle drei prüfen davor `byte[0xA182F9]` **und** `byte[0xA182F8]`. Das
Tastenzustandsfeld beginnt bei `0xA182E8`, also sind das **VK 17 = Strg** und
**VK 16 = Umschalt**.

⭐ **Der vollständige Griff: `ARMLEUCHTER` tippen, dann Strg+Umschalt+U /
Strg+Umschalt+H / Strg+Umschalt+A.**

#### 1.3 »Check Hopla« — der Bildschirm auf dem Kopf

`0x43B080` heisst so, weil der einzige Rufer (`0x417E7F`, in der Zeichenschleife)
unmittelbar davor die Protokollmarke `"Check Hopla"` (`0x4F7670`) setzt und
`byte[0x4FA0D0]` prüft.

```
Lock(Rueckpuffer 0x540744)               ; Neuversuch bei DDERR_WASSTILLDRAWING (0x8876021C)
n = dword[0x5387CC] · dword[0x5387C8]    ; Hoehe · Breite
kopiere n/4 Dwords  Oberflaeche -> 0x4FA0D4
fuer i = 0 … n-1:  Oberflaeche[i] = Puffer[n-1-i]
Unlock
```

Punktspiegelung des ganzen Bildes. Der Puffer `dword[0x4FA0D4]` wird von
`HLAVOUDOLU` angelegt und wieder freigegeben — beide Enden passen zusammen.

⚠ Nachgerechnet: die zweite Schleife schreibt **in dieselbe Oberfläche zurück**,
liest aber aus der Kopie. Das ist eine saubere 180°-Drehung, kein Halbbildfehler.

---

### 2. ⭐ Die CD-Prüfung: `X:\cw.id`

`0x43B440` / F `0x43A5B0`, 96 Befehle, **normiert befehlsgleich**.

```
fuer laufwerk = 'd' … 'z':
    pfad = "X:\"  mit  pfad[0] = laufwerk    ; Vorlage 0x4FAC94 = "X:\"
    name = pfad + "cw.id"                     ; 0x4FAC8C = "cw.id"
    wenn !datei_vorhanden(name): weiter
    f = fopen(name, "rb"); fread(&c,1,1,f); fclose(f)
    wenn c == arg1:  byte[0x7A4FE8] = laufwerk ; return 1
return 0
```

⭐ Die Datei ist **ein Byte** lang und muss dem übergebenen Kennbyte gleichen —
eine Datenträgerkennung, keine Prüfsumme. Elf Rufer, davon sechs in
`0x418D05…0x418D79` (die Reihe der Kennbytes) und fünf in `0x43B580`, der
Funktion mit »Drücken Sie Alt-F4 zum Beenden«.

Die drei Helfer sind gewöhnliche CRT-Hüllen, in beiden Bauten gleich:
`0x43B760` = `datei_vorhanden`, `0x43B7B0` = `dateigroesse`
(fopen/fseek END/ftell), `0x43B940` = `datei_ganz_lesen(name, puffer, laenge)`.

---

### 3. ⭐ Der PCX-Lader — und drei tote Funktionen

`0x43B800(dateiname, ziel, zeilenschritt)`:

```
wenn !datei_vorhanden: return 0
n = dateigroesse ; roh = malloc(n) ; datei_ganz_lesen(name, roh, n)
breite = word[roh+8] - word[roh+4] + 1
hoehe  = word[roh+10] - word[roh+6] + 1
gesamt = hoehe · zeilenschritt ;  i = 0x80 ;  v = 0
solange v < gesamt:
    a = roh[i++] ; anz = 1
    wenn (a & 0xC0) == 0xC0:  anz = a & 0x3F ; a = roh[i++]
    memset(ziel + v, a, anz) ; v += anz
    wenn v % zeilenschritt == breite:      ; Zeile voll
         v += zeilenschritt - breite
free(roh) ; return 1
```

Kopf bei 128 Byte, Lauflängenmarke `0xC0` in den oberen zwei Bit, Bildgrenzen bei
`+4/+6/+8/+10` — **das ist das PCX-Format, Merkmal für Merkmal.**
⚠ Er ignoriert `bytesPerLine` (PCX `+0x42`) und nimmt den **Zielschritt** als
Zeilenmass; bei ungerader Breite würde er verrutschen.

#### 3.1 ⚠⚠ Negativbefund: drei Funktionen sind im ausgelieferten C-Bau tot

Nach der Regel geprüft — nicht über den Linearabtast, sondern über `rufer.py`
(relative `E8`/`E9`) **und** `reloc_refs.py --addr` (Zeiger und Sprungtafeln):

| Funktion | `E8`/`E9`-Rufer | Relokationen darauf |
|---|---:|---:|
| `0x43B170` (Rumpf, gibt immer 0) | 0 | 0 |
| `0x43B800` (PCX-Lader) | 0 | 0 |
| `0x43B9C0` (Nebensätze auf `0xFFFF`) | 0 | 0 |
| ihre Stummel `0x401488`, `0x40249B`, `0x4014F6` | 0 | 0 |

⚠ **Die Stummel beweisen nichts** — MSVC legt bei `/INCREMENTAL` je Funktion
einen an, auch für ungerufene. Gerufen wird keiner der drei.

`0x43B170` ist zusätzlich innen leer: `if (arg > 0x41) return 0; else return 0;` —
beide Zweige geben 0 zurück. Ein ausgehöhlter Rumpf, kein Zufallsfund.

⭐ `0x43B9C0` ist trotzdem lehrreich: sie setzt in **sieben** Nebensatztafeln das
erste Wort jedes der 50 Plätze auf `0xFFFF` —
sec23 `0x878E58` (16 B) · sec24 `0x87A2C0` (14) · sec25 `0x879F38` (14) ·
sec26 `0x87A5A8` (4) · sec28 `0x878AD0` (18) · sec30 `0x879178` (14) ·
sec31 `0x879E70` (4). **Die Schleifenschranke `eax < 0x879178` mit Schritt 16
ergibt genau 50 Durchgänge** (`0x320 / 0x10`), und alle sieben Schrittweiten
treffen die Satzlängen der Tafel aus BE.3.
⚠ **sec27 (Flughafen, 52 B) und sec29 (Seedock, 4 B) fehlen** — 7 von 9.

---

### 4. ⭐⭐ Die Ausführer der Gebäudebefehle — dreizehn Arme des Befehlsbusses

`0x43F820 … 0x43FF50` sind dreizehn Funktionen desselben Baus, alle gerufen aus
dem Verteiler bei `0x4C382D…0x4C3A52`. Die Befehlsnummer steht nicht im Code —
sie steht in der **Sprungtafel bei `0x4C45F8`**:

> ⭐⭐ **Nullmodell, unabhängig gewonnen.** Die Tafeleinträge, die auf die
> dreizehn Arme zeigen, liegen bei `0x4C4DEC … 0x4C4E58`. Setzt man
> `Befehl = (Eintragsadresse − 0x4C45F8)/4`, ergeben sich für die **neun**
> Befehle, die Abschnitt P bereits verzeichnet, **9 von 9** dieselben Nummern
> (509, 510, 511, 515, 516, 517, 519, 520, 522, 524, 536 — und 523/526 genau
> dort, wo P »tot« vermerkt). Ein falscher Sockel würde alle neun gleichzeitig
> verfehlen.

#### 4.1 Der gemeinsame Bau

```
befehl(satznr, spieler):
    geb = word[Tafel + Satzlaenge·satznr + 0x00]
    wenn byte[0xC06914 + 76·geb + 0x01] != spieler: return    ; Eigentum
    <Wirkung>
```

Die **bezahlten** fünf (509, 510, 515, 516, 536) hängen zusätzlich das Konto ein:

```
    konto = dword[0xA9C600 + 4·spieler]                       ; sec73
    preis = (short) word[Satz + Preisversatz]
    wenn preis > konto: return                                ; ⭐ still, ohne Meldung
    byte[Satz+0x02] = zustand ; byte[Satz+0x06] = 0           ; Fortschritt zurueck
    dword[0xA9C600 + 4·spieler] = konto − preis
```

| Befehl | C | Tafel | Zustand | Preis |
|---:|---|---|---:|---|
| 509 | `0x43F820` | sec24 (Fabrik) | 3 | `+0x0A` Lagerausbaukosten |
| 510 | `0x43F8B0` | sec24 | 4 | `+0x0C` Produktionserweiterung |
| 515 | `0x43FA40` | sec28 (Mine) | 3 | `+0x0E` |
| 516 | `0x43FAC0` | sec28 | 4 | `+0x10` |
| 536 | `0x43F9B0` | sec27 (Flughafen) | 2 | `+0x06`, Fortschritt bei `+0x08` |

#### 4.2 ⚠⚠ Berichtigung: 511 und 517 sind **Umschalter**, nicht »→ 0«

```
0x43F940 (511) / 0x43FB40 (517):
    wenn byte[Satz+0x02] == 1:  byte[Satz+0x02] = 0
    sonst                        byte[Satz+0x02] = 1
```

Abschnitt P führt beide als »→ 0«. Das ist die halbe Wahrheit: aus **jedem
anderen** Zustand springen sie auf **1** (`StRepair`). Nur 523/524/526
(`0x43FE70`, `0x43FEC0`, `0x43FF50`) setzen bedingungslos 0.

#### 4.3 ⭐⭐ Neu: der Reparaturbefehl **kostet Trefferpunkte**

`0x43FBA0` (519, Fabrik), `0x43FC50` (520, Flughafen), `0x43FDC0` (522, Mine)
sind dreimal derselbe Rumpf und zahlen **nicht** mit Geld:

```
hp = word[geb+0x02] ; hpmax = word[geb+0x12]
wenn hp == hpmax: return                    ; nichts kaputt
byte[Satz+0x02] = 2 (beim Flughafen 1)
wenn hp > 50:
    word[geb+0x02] = (29·hp) / 30           ; ⭐ 1/30 der TP GEHEN VERLOREN
    0x4CBBF0(geb, 0)                        ; Schadensbild sofort neu
```

Der Faktor 29/30 ist aus der `lea`-Kette gerechnet, bevor irgendwo nachgeschlagen
wurde: `eax = hp·8`, `eax −= hp` → `7·hp`, `lea eax,[hp + 7hp·4]` → `29·hp`,
`idiv 30`. Die Felder `+0x02`/`+0x12` sind in `GAMESTATE_RE.md` §3.80 als
hp/hp_max belegt.

`0x4CBBF0` ist der Schadensstufenrechner:
`stufe = (hpmax − hp) / (hpmax / byte[0xBB41A0 + 10·typ])`, und bei Änderung
`byte[geb+0x06] = stufe` plus ein Ereignis über `0x4017EE`.

⚠ **Deutung:** »Reparieren« reisst das Gebäude erst weiter auf und flickt es dann
langsam (der Gebäudetakt `0x43E05C` gibt jeden 4. Takt einen Punkt zurück).
Dass es so ist, steht dreimal wortgleich im Code. **Warum** das Spiel es tut,
bleibt ungedeutet.

---

### 5. ⭐ `0x43FF90` — die Andockschlange hat sechs Plätze, und Typ 12 teilt sie mit Typ 6

```
freie_andockplaetze(gebaeude):
    idx = byte[geb+0x15]                     ; cis_typ, die Nummer INNERHALB des Typs
    typ = byte[geb+0x00]
    wenn typ-1 > 11: return 0xFF
    arm = byte[0x440058 + typ-1] ; jmp dword[0x440044 + 4·arm]
    Arm 0 (Typ 1  Basis):        p = 0x878E5C + 16·idx      ; sec23 +0x04
    Arm 1 (Typ 5  Depot):        p = 0x879F3A + 14·idx      ; sec25 +0x02
    Arm 2 (Typ 6  Bahnstation):  p = 0x87917A + 14·idx      ; sec30 +0x02
    Arm 3 (Typ 12 Feldbahnhof):  ⭐ DIESELBE Sprungadresse wie Arm 2
    Arm 4 (alle uebrigen):       return 0xFF
    zaehle n = 0…5, solange word[p + 2n] != 0xFFFF ;  return 6 − n
```

Die Indextafel `0x440058` lautet `00 04 04 04 01 02 04 04 04 04 04 03` — Typ 1→0,
Typ 5→1, Typ 6→2, Typ 12→3, alles andere→4.

⭐ Das bestätigt zwei Aussagen der bestehenden Doku unabhängig: BE.3 ordnet
»6/12 Bahnstation, Feldbahnhof« derselben Tafel sec30 zu (**hier steht es im
Kontrollfluss: Arm 2 und Arm 3 sind wörtlich dieselbe Adresse `0x440018`**), und
Abschnitt AM nennt die Schlange »sechs Roboter« (**hier: `cmp ecx, 6`**).
Neu ist, dass die **Basis** dieselbe Schlange hat, nur bei `+0x04`.

⭐ **Ein kleiner Auslieferungsunterschied ohne Wirkung:** F gibt in Arm 4 vorher
noch die Protokollzeile `"Wrong depo..."` (F `0x4F9F0C`) aus, C nicht. Nur
Entwicklerprotokoll, kein Verhalten.

---

### 6. ⭐ `0x4400A0` — die Türzelle, und die Sonderbehandlung der Basis

```
entfernung(gebaeude, einheit):
    wenn byte[geb+0x00] == 1:                      ; ⭐ Basis
         zx = x + 2 ; zy = y + 4                   ; FEST
    sonst
         zx = x + byte[geb+0x31] ; zy = y + byte[geb+0x32]   ; Tuerversatz
    dx = Einheit.RX − zx ; dy = Einheit.RY − zy
    return (int) sqrt(dx² + dy²)
```

Abschnitt AI.1 hat den Türversatz als `Satz[0x35]/[0x36]` (Sockel `0xC06910`,
also `+0x31/+0x32` ab `0xC06914`) belegt. **Hier steht er ein zweites Mal — und
mit der Ausnahme, dass die Basis ihn nicht benutzt, sondern (2,4) fest.**

---

### 7. ⭐ `0x440190` — ein Gebäude aus allen Umschlagsätzen streichen

```
gebaeude_streichen(gebaeude):
   fuer g = 0 … 399:                          ; sec48, 400 × 18 B, 0x77AC50
       wenn byte[satz+0x0A] == 0: weiter       ; Fassungsvermoegen 0 = Satz unbenutzt
       geaendert = 0
       fuer j = 0…3:  wenn byte[satz+j] == gebaeude: byte[satz+j] = 0xFF ; geaendert = 1
       wenn byte[satz+0x04] == gebaeude:
            byte[satz+0x04] = 0xFF ; byte[satz+0x0E] = 0 ; geaendert = 1
       wenn geaendert und byte[satz+0]+…+byte[satz+3] == 0x3FC:   ; alle vier 0xFF
            byte[satz+0x0E] = 0                                    ; Satz ungueltig
```

Die Schrittweite 18 ist aus der `lea`-Kette gerechnet (`eax = 2·i`,
`edi = eax + eax·8 = 18·i`) und trifft sec48 = 7200 = 400 × 18.
`+0x00…+0x03` sind die vier **Quellen** (`zdroj0…3` aus dem Aufzeichner),
`+0x04` das **Ziel**, `+0x0E` der Gültigkeitsmerker.

Zwei Rufer: `0x43CD75` (bei der **Eroberung**) und `0x4C9A6C`.

---

### 8. ⭐⭐ Die Fenster-Bedienmaschine

#### 8.1 Der Zugriffsweg auf einen Fenstersatz

Alle Funktionen dieses Bündels beginnen mit derselben `lea`-Kette. Ausgerechnet:
`ecx = k`, `ebx = 9k`, `eax = 81k`, `+k = 82k`, `·3 = 246k`, `k + 4·246k = 985k`,
`·5 = 4925k`, `·9 = 44325k`, `−k` = **44324·k**.
Das trifft die Satzlänge aus BA.4 genau. Sockel C `0x8B9038`, F `0x8B8098`.

#### 8.2 ⭐⭐ `0x441190` — Fenster in den Schirm zwingen, **und der C/F-Unterschied**

```
in_den_schirm(nr):
    art = byte[0x8B9038 + 44324·nr]
    C:  wenn art in {9, 0x23, 0x30}:  return
    F:  wenn art in {9, 0x23}:        return          ; ⚠ ohne 0x30
    x = word[+0x02] ; y = word[+0x04] ; b = word[+0x06] ; h = word[+0x08]
    wenn x < 0:                     word[+0x02] = 0
    wenn x + b >= dword[0xB136B0]:  word[+0x02] = dword[0xB136B0] − b − 1
    wenn y < 0:                     word[+0x04] = 0
    wenn y + h >= dword[0x5387CC]:  word[+0x04] = dword[0x5387CC] − h − 1
```

C `0x4411B6/BA/BE`: `cmp al,9` · `cmp al,0x23` · `cmp al,0x30`.
F `0x4401C6/CA`: `cmp al,9` · `cmp al,0x23` — **die dritte Prüfung fehlt.**

⭐⭐ `0x30 = 48` ist **genau die Fensterart, die es in F nicht gibt** (BA.8: das
zweite Hauptmenü mit »Enzyklopädie«). Das ist die **fünfte** unabhängige Zählung
für den zehnten belegten Unterschied — neben Anlegerzahl, Rahmenzeichner-Rufern,
Knopfroutine-Rufern und der Sprungtafel.

⚠ **Methodische Warnung.** `cfind.py --diff` meldet hier »delete `cmp al,9`« —
weil `difflib` bei drei gleichgeformten Paaren das **erste** wegwirft. Die
Konstanten sagen etwas anderes: `9` und `0x23` stehen in beiden, `0x30` nur in C.
**Die Blockstelle des Werkzeugs ist keine semantische Aussage.**

⭐ Nebenbei: `dword[0xB136B0]` ist eine **zweite Kopie der Bildbreite**. Ihr
einziger Schreiber ist `0x4B6B1C`, unmittelbar hinter
`SetDisplayMode(breite, 480, 8)`, im selben Atemzug wie
`dword[0x5387C8] = breite` (`0x4B6B16`). Die Fensterschicht liest `0xB136B0`,
die Zeichenschicht `0x5387C8`; es ist derselbe Wert.

#### 8.3 ⭐ Die vier Wege, Fenster auf den Schirm zu bringen

Alle vier haben denselben Kopf: sofort raus, wenn `byte[0x4FD64C] == 0` **und**
`byte[0x4FD644] == 0xFF`; dann `Lock` (Wiederholung bei `DDERR_WASSTILLDRAWING`),
`dword[0x87B044] = desc.lpSurface`, zeichnen, `Unlock`.

| C | Ziel | Umfang | Rufer |
|---|---|---|---|
| `0x440DD0` | **Rückpuffer** `0x540744` | alle Fenster **plus** `byte[0x4FD644]` | `0x416935`, `0x419307` |
| `0x440E90` | **primäre Oberfläche** `0x540770` | alle Fenster (ohne `0x4FD644`) | `0x415240`, `0x4152F5`, `0x43B6A8`, `0x4C61F8` |
| `0x440F40` | primäre Oberfläche | **nur Art 0x2D = 45** (»Laden…«) | `0x444128` |
| `0x441030` | primäre Oberfläche | **nur Art 0x2F = 47** (»Synchronisieren…«) | `0x4442A8` |

⭐⭐ **Damit ist erklärt, wozu Art 45 und Art 47 überhaupt da sind:** sie werden
**unter Umgehung der Hauptschleife direkt auf den sichtbaren Bildschirm** gemalt,
während das Spiel lädt bzw. auf den Netzabgleich wartet und gar nicht taktet.

⭐ **Und `dword[0x87B044]` ist jetzt belegt.** BA.5 nennt es als Zielsockel von
`0x4409E0` (`Ziel = dword[0x87B044] + y·dword[0x5387C8] + x`), ohne Schreiber.
Die Schreiber sind genau diese vier Funktionen, und der Wert ist
`DDSURFACEDESC.lpSurface` der gerade gesperrten Oberfläche.

⭐ **Die Zeichenreihenfolge steht fest:**
`for (i = anzahl; i > 0; i--) fenster_auf_schirm(byte[0x87AFF7 + i])` — also die
Liste `0x87AFF8` **von hinten nach vorn**. `0x44FC20` (»nach vorn holen«) schiebt
ein Fenster auf **Index 0**. **Index 0 ist das oberste Fenster.**

#### 8.4 Die Fensterliste

| Grösse | Bedeutung |
|---|---|
| `byte[0x87AFF8 + i]`, i = 0…n−1 | Liste der offenen Fenster, **0 = vorn** |
| `byte[0x4FD64C]` | Anzahl |
| `byte[0x4FD644]` | ⭐ das **Immer-oben-Fenster** (`0xFF` = keines) — nur `0x440DD0` malt es |
| `word[0x4FD648]` / `word[0x87B050]` | gegriffenes Fenster / gegriffenes Bedienelement |
| `word[0x87B054]`, `word[0x87AC00]` | zwei Zähler des Ziehvorgangs |

`0x441120(fenster, element)` (82 Rufer, alle im Befehlsbehandler
`0x448C…0x44DC`):

```
wenn word[0x4FD648] != 0xFFFF:
     0x44FC90(altes_fenster, word[0x87B050])      ; alten Griff abschliessen
     fensterverteiler(altes_fenster)              ; und neu malen
word[0x87B050] = element ; word[0x4FD648] = fenster
word[0x87B054] = 0 ; word[0x87AC00] = 0
```

`0x4412E0(nr)` (32 Rufer) ist der Standardgriff »neu zeichnen«:
Fensterverteiler `0x487630` **und danach** `0x441190`.

#### 8.5 ⭐ `0x441270` schliesst das Kontexthilfe-Ereignis — mit einer Ausnahme

Abschnitt AR nennt `0x441270` als Registrierer und als Schreiber des
Ereignisbytes `byte[0x539930]`. Vollständig gelesen:

```
registrieren(nr):
    wenn nr == 0xFFFF: return
    byte[0x87AFF8 + anzahl] = nr ; anzahl++      ; ⚠ ans ENDE, also nach HINTEN
    byte[44324·nr + 0x8C3D5A] = 0                ; Fenstersatz +0xAD22
    art = byte[44324·nr + 0x8B9038]
    wenn art != 3:  byte[0x539930] = art         ; ⭐ Art 3 (Karte) loest NICHTS aus
```

⭐ **Zwei Berichtigungen zu AR:** das Kartenfenster ist ausgenommen, und die
Registrierung legt das Fenster **hinten** ab — nach vorn kommt es erst durch
`0x44FC20`, das die Öffner separat rufen (sonst die Meldung »Window up not
found.«, `0x4FE8FC`).

#### 8.6 Die Sucher und Auffrischer

Alle nach demselben Muster: Liste `0x87AFF8` durchgehen, `byte[+0x00]` gegen eine
feste Art prüfen.

| C | Art | Wirkung |
|---|---:|---|
| `0x441310` | 3 | + Unterart `word[+0xACA0]`; gibt 1/0 zurück |
| `0x441390` | 3 | + Unterart; **schliesst** (`0x4471A0`) |
| `0x4414B0` | 12 | neu zeichnen |
| `0x4416E0` | 16 | neu zeichnen |
| `0x441420` | 34 | vorher `dword[0x892128] = byte[0x4FA23C] − 1` (Geschwindigkeitsregler) |
| `0x441530` | 46 | `byte[+0x0C] = arg`, neu zeichnen, 1 zurück |
| `0x441760` | 12 | `dword[+0xACA8] = +0xACAC = +0xACB0 = 0` |
| `0x441D00` | 42 | **schliessen** |

⭐ `0x4415F0` ist feiner gebaut — es zeichnet nur bei **wirklicher Änderung**:

```
s = word[0x4FA0C8]                              ; die Auswahl
wenn s == 10000:                                ; keine/mehrere
    wenn dword[0x502AD4] == 1:                  ; genau ein Objekt markiert
         s = word[0x502AD8]
         wenn s/1000 != byte[0x4FA284]: s = 10000    ; fremd -> nichts
    sonst s = 10000
wenn word[0x4FAEF0] != s:  Fenster Art 9 neu zeichnen
word[0x4FAEF0] = s
```

`word[0x4FAEF0]` ist der **Gedächtniswert der zuletzt gezeigten Auswahl** — er
hat sonst keinen Leser.

#### 8.7 ⭐ Die Öffner — elf von zwölf stimmen mit BA.4 überein

Jeder Öffner: Liste nach der Art durchsuchen, sonst Anleger rufen, `0x441270`
registrieren, `0x44FC20` nach vorn.

| C | Art gesucht | Anleger (BA.4) | Lage / Besonderheit |
|---|---:|---|---|
| `0x441BB0` | `0x2B` = 43 | `0x45BC10` (43, 640×480) | + `byte[0xA182D0]=0`, `byte[0x502AA0]=0`, Bildschirmmodus 3 |
| `0x441C70` | `0x2E` = 46 | `0x45C8F0` (46, 120×100) | (0,0) |
| `0x441D80` | `0x2A` = 42 | `0x45B9D0` (42, 300×160) | x mittig, **y = Höhe/5** |
| `0x441EC0` | `0x28` = 40 | `0x45B770` (40, 180×60) | x mittig, **y = Höhe/5** |
| `0x4421A0` | `0x26` = 38 | `0x45B4E0` (38, 600×420) | vorher Mitnahmeliste leeren |
| `0x442270` | `0x24` = 36, Unterart 10 | `0x45B230` (36, 300×60) | Textfeld, 40 Zeichen |
| `0x442380` | `0x24` = 36, Unterart 2 | `0x45B230` | »Wählen Sie einen Namen für dieses Teil«, 19 Zeichen |
| `0x442670` | `0x25` = 37 | `0x45B3A0` (37, 600×420) | x und y mittig |
| `0x4427E0` | `0x23` = 35 | `0x45B100` (35, 200×240) | **x = 25 fest**, y = Höhe − h − 20 |
| `0x442A00` | `0x22` = 34 | `0x45A420` (34, 360×300) | x mittig, y = Höhe/5 |
| `0x442E10` | `0x1B` = 27 | `0x459CB0` (27, 620×300) | — |
| `0x442ED0` | `0x17` = 23 | `0x4598F0` (**21**, 220×100) | ⚠ passt nicht |

> **Nullmodell:** gesuchte Art und die Art, die der Anleger einträgt, stimmen bei
> **11 von 12** überein. Ein zufälliges Paaren aus 48 Arten träfe im Mittel
> 0,25 mal.

⚠ **Der eine Ausreisser, in beiden Bauten gleich:** `0x442ED0` sucht Art
**`0x17` = 23** (Depot), ruft dann aber `0x4598F0` — und die schreibt
`byte[+0x00] = 0x15 = 21` und 220 × 100. In F genauso (`0x441EED: cmp al,0x17`,
Anleger `0x458590` schreibt ebenfalls `0x15`). Wirkung: **die
Doppelöffnungsprüfung greift nie**, das Fenster entsteht bei jedem Klick neu.
Ich melde das als **wahrscheinlichen Fehler des Originals**, nicht als Deutung.
Der Rufer ist `0x437305` und übergibt eine Objektnummer aus `word[0x502AD8]`.

#### 8.8 ⭐⭐ `0x4428E0` — fünf Auflösungen, und nur 8 Bit

```
liste = { 640×480, 800×600, 1024×768, 1280×1024, 1600×1200 }   ; auf dem Stapel
dword[0x9937E8] = 0 ; byte[0x9937EC] = 0                       ; fuenf Merker loeschen
0x4AD4F0(dword[0x540730])                                      ; EnumDisplayModes
fuer m = 0 … dword[0xA50FD8]−1:                                ; Saetze zu 24 B ab 0xA509D8
    fuer k = 0 … 4:
        wenn breite[k] == dword[satz+0] und hoehe[k] == dword[satz+4]
             und dword[satz+8] == 8:                           ; ⭐ 8 Bit je Punkt
             byte[0x9937E8 + k] = 1
```

Die Stapelbelegung ist von Hand nachgerechnet (`sub esp,0x18` + vier `push`), die
Schrittweite 24 aus `ebp = 3·i`, `edx = ebp·8`. Der Bereich `0xA509D8 … 0xA50FD8`
fasst genau **64** Sätze. Gerufen aus dem Einstellungsfenster-Öffner `0x442A00`
und aus `0x4B68A1`.

---

### 9. ⭐ `0x43B350` — die Mitnahme zwischen Missionen

```
fuer k = 0…19:  byte[0x81A410 + 78·k + 0x09] = 0xFF          ; alle 20 Plaetze leeren
fuer k = 0…19:
    n = word[0x9937B8 + 2·k]
    wenn n == 0xFFFF: weiter
    kopiere 78 Byte von 0x6E26C8 + 78·n  nach  0x81A410 + 78·k
```

Beide Schrittweiten (78) sind aus den `lea`-Ketten gerechnet, die Kopierlänge ist
`rep movsd ×19 + movsw` = **78 Byte** — der Einheitensatz. Ziel ist **sec98**
(1560 = 20 × 78, `0x81A410`), Quelle die Einheitentafel. `+0x09` = `faze`,
`0xFF` = leerer Platz.

Einziger Rufer: `0x4472E6`. Die Liste `word[0x9937B8]` ist dieselbe, die
`0x4421A0` beim Öffnen des Mitnahmefensters (Art 38) leert — und die
`OFFENE_FRAGEN` bereits als »Einheitenmitnahme, Liste `word[0x9937B8]`« führt.

---

### 10. `0x43B3F0` — Entwurf → Waffe

```
p = byte[0x4FA284]
entwurf = 58·(arg + 200·p) + 0x5045BC       ; aus der lea-Kette: 29·(…)·2
w = byte[entwurf + 0x00]
return byte[0x4F98FC + 22·w + 0x01]
```

Schrittweite 58 für die Entwurfstafel, 200 Entwürfe je Spieler, Sockel
`0x5045BC` (sec46 liegt bei `0x5045A0`, also `+0x1C`). Die Waffentafel
`0x4F98FC` mit Schrittweite 22 ist aus BA.7 bekannt — **die gerechnete
Schrittweite trifft die verzeichnete.**

Zehn Rufer, alle in Fensterzeichnern (`0x468A30` Basis, `0x46D9B6` Erstellung,
`0x483CEE` Mitnahme …) — die Funktion füllt die Zeilen »Nachladen / A/V /
Geschw. / Sicht«.

---

### 11. `0x43F5F0` / `0x43F690` — Wasserprüfung 2×2 und 4×4

```
ist_wasser(spalte, zeile, kante):               ; kante = 2 bzw. 4
    fuer z = zeile … zeile+kante−1:
        fuer s = spalte … spalte+kante−1:
            wenn word[0xBDEA80 + 2·(s·256 + z)] != 0xFFFC: return 0
    return 1
```

`0xFFFC` = Wasser (Abschnitt AI, 81 655 Zellen). Beide werden nur von `0x43F730`
gerufen, das an einem Gebäude nach einem **Liegeplatz** sucht: bei Art 4 ein
2×2-Feld bei `(x−2, y+2)`, sonst ein 4×4-Feld bei `(x−4, y+1)`, und danach die
anderen Seiten.

---

### 12. ⭐⭐ DER BEFUND: `0x43C960` gibt es in F nicht

#### 12.1 Was sie tut

```
zelle_raeumen(gebaeudeplatz):                     ; C 0x43C960, 240 B
    satz  = 0xC06910 + 76·platz
    x     = word[satz+0x00] ; y = word[satz+0x02]      ; die ANKERZELLE
    b     = byte[satz+0x05]                             ; der Besitzer
    fuer k = 1000·b … 1000·b+999:
        e = 0x6E26C8 + 78·k
        wenn byte[e+0x09] == 0xFF: weiter               ; faze: leerer Platz
        wenn byte[e+0x00] != x:    weiter               ; RX
        wenn byte[e+0x01] != y:    weiter               ; RY
        wenn byte[e+0x14] >= 0x2D: weiter               ; UKOL (Auftrag) < 45
        0x410E60(k)                                     ; EINHEIT LOESCHEN
        word[0xBDEA80 + 2·(x·256+y)] = 0xFFFE           ; Zelle FREI
```

`0x410E60` ist der Einheitenlöscher: Auswahlliste `0xA0A858` leeren,
`word[e+0x24] = 10000`, Truppeintrag in `0xB4A0D0` streichen,
`byte[e+0x09] = 0xFF`.

**Gerufen genau einmal**, aus dem Eroberungsbehandler `0x43CA50`:

```
C 0x43CD02  al = byte[esp+0x18]                  ; der EINDRINGENDE Spieler
C 0x43CD06  push ebx                             ; der Gebaeudeplatz
C 0x43CD07  byte[satz+0x3C] = al
C 0x43CD0D  call 0x401E24  ->  0x43C960          ; <<<< NUR IN C
C 0x43CD12  movsx eax, word[satz+0x3A]
```

#### 12.2 Vier voneinander unabhängige Zählungen

**(a) Fingerabdruck.** `cfind.py 0x43C960` → *KEINE ENTSPRECHUNG IN F, bester
Rest 46 %*.

**(b) Volkszählung der `0xFFFE`-Schreiber.** Rohe Bytesuche über beide `.text`
(`66 c7 04 …` + Sockel + `fe ff`), Ziel C `0xBDEA80` / F `0xBDDAE0`:
**C hat 27 Stellen, F hat 26.** Die 26 lassen sich der Reihe nach paaren; die
eine übrige ist **`0x43CA02`, mitten in `0x43C960`**.

**(c) Volkszählung der Schwelle `0x2D` auf dem Auftragsfeld.** Bytesuche nach
»`al = byte[Einheit+0x14]` gefolgt von `cmp al,0x2D`«:
C `0x412B8D, 0x412C7E, 0x431CEA, 0x431D5B, 0x43C9EB, 0x4BC49A, 0x4BF41B, 0x4D39A9`
(**8**) gegen F `0x41295D, 0x412A4E, 0x430E2C, 0x430E9D, 0x4BBF5A, 0x4BEECB,
0x4D3539` (**7**). **Sieben Paare, ein Rest — wieder `0x43C9EB` in `0x43C960`.**

**(d) Die Rufstelle im F-Code.** Der Eroberungsbehandler ist C `0x43CA50` ↔
F `0x43BAF0` (Kopf byteweise gleich, Versatz −0xF60). Die Stelle des Aufrufs ist
über die eindeutige Bytefolge `mov byte[esi+…+0x3C], al` gefunden
(C `0x43CD07`, F `0x43BDA6` — je **genau eine** Fundstelle je EXE). In F folgt
darauf direkt `movsx ecx, word[esi+0xC059AE]`. **Kein Aufruf, keine Lücke.**

⚠ Der Rest der beiden Funktionen ist an dieser Stelle nicht wortgleich (C hält
den Eindringling im Feld `+0x3C` und liest ihn von dort zurück, F in einem
Stapelplatz `[esp+0x2C]`). Das ist Registerzuteilung, kein Verhalten — es zeigt
aber, dass die Stelle **neu übersetzt** wurde, nicht nachträglich gepatcht.

#### 12.3 ⭐⭐ Der zweite C-eigene Block im selben Rufer

Ein befehlsweiser, registerblinder Vergleich von C `0x43CB0C…0x43D290` gegen
F `0x43BBAC…0x43C2B0` (137 gegen 130 Befehle) findet **genau zwei** Blöcke, die
etwas tun. Der eine ist der Aufruf oben, der andere ist `0x43CFF5 … 0x43D04B`
(87 Byte):

```
ebp = dword[0x539234]
byte[satz+0x05] = eindringling                   ; der Eigentuemerwechsel
byte[0xBC41E1 + 2·(255·alt + platz)] = 0
wenn ebp != 0                                    ; ⭐ Netzspiel/Determinismus
und byte[0x87B140 + 40·eindringling] == 1:       ; ⭐ sec53[40p] == 1 = RECHNER (KI)
    fuer p = 0 … 254:
        wenn byte[0xC06914 + 76·p + 0x00] == 1                ; Typ 1 = Basis
        und  byte[0xC06914 + 76·p + 0x01] == eindringling:
             word[… + 0x2A] += 100                            ; Fahrwerke
             word[… + 0x2C] += 100                            ; Specials
```

F springt an dieser Stelle direkt weiter (`cmp dl,al; jne`).
Bytesuche bestätigt: `+= 100` auf `+0x2A` bzw. `+0x2C` gibt es in C **je genau
einmal** (`0x43D036`, `0x43D03E`) und in F **null mal**.

⭐ `GAMESTATE_RE.md` §3.87 führt diesen Block bereits, mit dem Vermerk
»**Both conditions unidentified** — documented, not implemented in the remake«.
**Beide sind jetzt benannt:** `dword[0x539234]` ist der verzeichnete
Netzspiel-/Determinismusschalter (Abschnitt AV), und `sec53[40·p + 0x00] == 1`
heisst laut Abschnitt AT »**Rechner**« (0 = Mensch, 0xFF = ausgeschieden;
gemessen 50×1, 41×0xFF, 13×0).

⭐ Also: **erobert ein KI-Spieler im Netz-/Determinismusbetrieb ein Gebäude,
bekommt jede seiner Basen +100 Fahrwerke und +100 Specials.** In F nicht.

#### 12.4 ⚠ Die zwei krummen Adressen sind keine Funktionen

Auftragsgemäss geprüft: `0x43D38C` und `0x43D57E` liefern bei `aere.py fs`
**keinen Funktionsanfang** (das Werkzeug bricht mit `NoneType` ab), haben **null**
`E8`/`E9`-Rufer, und ein Linearlauf ab `0x43CA50` trifft beide als
**Befehlsanfänge innerhalb** des Eroberungsbehandlers:

```
0x43D387  jne 0x43d38c
0x43D389  mov byte[edx], 3
0x43D38C  add edx, 3                    <-- reines Sprungziel

0x43D57A  jne 0x43d57e
0x43D57C  mov byte[ecx], bl
0x43D57E  cmp word[esp+0x18], 0x1F40    <-- reines Sprungziel
```

**Beide sind Sprungziele, kein Auslieferungsunterschied.** Der Verdacht der
Vollerhebung war richtig begründet und ist damit erledigt. Dass `cfind` für sie
»keine Entsprechung« meldet, liegt daran, dass ein Rumpfstück ab einem
willkürlichen Sprungziel keinen stabilen Fingerabdruck hat.

#### 12.5 Der Rest des Reviers ist in beiden Bauten gleich

| Befund | Anzahl |
|---|---:|
| F-Partner **eindeutig**, normierte Rumpfform identisch | 35 |
| F-Partner **mehrdeutig** (mehrere gleichgeformte Geschwister), von Hand geprüft | 16 |
| F-Partner **ungenau**, von Hand nebeneinander gelesen | 10 |
| **keine Entsprechung** | 1 (`0x43C960`) |

Von den zehn »ungenauen« sind **acht reine Übersetzerwahl** —
`cmp byte[ebx+SOCKEL], dl` gegen `mov al, byte[ebx+SOCKEL]; cmp al, K` (gleiche
Konstanten 0x28/0x2A/0x2B/0x1B), `jb`/`ja` mit vertauschten Operanden, `lea`
statt `add`, `push ebp` an anderer Stelle. Nach der Regel aus BA.2 werden sie
**verworfen**. Die zwei mit Inhalt sind `0x441190` (Abschnitt 8.2, der zehnte
Unterschied) und `0x43FF90` (F hat die Protokollzeile `"Wrong depo..."`, C nicht).

---

### 13. Berichtigungen an bestehenden Dokumenten

1. ⚠⚠ **`OFFENE_FRAGEN.md`, Abschnitt P** — »511→0« und »517→0«.
   Beide Behandler (`0x43F940`, `0x43FB40`) sind **Umschalter**: aus 1 wird 0,
   aus **allem anderen** wird 1. Nur 523/524/526 setzen bedingungslos 0.
2. ⚠⚠ **Abschnitt P** nennt für die Reparaturbefehle keinen Preis. Es gibt keinen
   Geldpreis, sondern einen **Trefferpunktpreis: `hp := 29·hp/30`**, nur wenn
   `hp > 50` (`0x43FBFE`, `0x43FCB6`, `0x43FE1F` — dreimal wortgleich), plus
   sofortige Neuberechnung der Schadensstufe über `0x4CBBF0`.
3. ⭐ **Abschnitt P** nennt die Behandler nicht bei Adresse. Sie stehen jetzt in
   der Adresstafel oben, samt Befehlsnummer aus der Sprungtafel `0x4C45F8`.
4. ⚠ **`GAMESTATE_RE.md` §3.87**, der Ablauf der Eroberung, lässt zwischen
   `@0x43CD07 byte[+0x3c] = intruder` und `@0x43CD3F` einen Schritt aus:
   den Aufruf **`0x43CD0D → 0x43C960`**. Nachzutragen.
5. ⭐ **`GAMESTATE_RE.md` §3.87, `@0x43CFF5`:** »Both conditions unidentified« —
   erledigt, siehe 12.3. Nachzutragen ist auch, dass **F diesen Block nicht hat**.
6. ⚠ **`OFFENE_FRAGEN.md`, Abschnitt AR:** »`0x401AF0 → 0x441270` … Schreiber des
   Ereignisbytes `byte[0x539930]`, das Ereignis fällt bei **jedem** Fenster«.
   Es fällt bei **jedem ausser Art 3** (`0x4412BE: cmp al,3; je`).
7. ⚠ **Abschnitt BA.5**, Fenstersatz: `+0x0C` ist dort »Fenstertext / Titel«.
   Bei den Arten 21/23/46 steht dort ein **Wort mit einer Objektnummer**
   (`0x459991` schreibt es aus dem dritten Argument, `0x442ED0` und `0x441530`
   lesen bzw. schreiben es als Zahl). Das Feld ist überladen.
8. ⭐ **Abschnitt BA.9**, `0x4409E0` »Fenster auf den Schirm«: der Zielsockel
   `dword[0x87B044]` hat vier Schreiber — `0x440DD0`, `0x440E90`, `0x440F40`,
   `0x441030` — und ist die gesperrte DirectDraw-Oberfläche.
9. ⚠ **`cfind.py`** hat in diesem Revier **vier Paare falsch** gebildet, alle nach
   demselben Muster: eine Familie fast gleichgeformter Geschwister.
   Belegt korrigiert:

   | C | cfind | richtig | Beleg |
   |---|---|---|---|
   | `0x43B760` | `0x4281C0` | **`0x43A8D0`** | wird von F `0x43A970` gerufen (Versatz 0xE90 wie die Nachbarn) |
   | `0x43B7B0` | `0x428170` | **`0x43A920`** | dito |
   | `0x442270` | `0x445300` | **`0x441260`** | enthält `word[+0xACA0] = 10`, F `0x441308` |
   | `0x442E10` | `0x445AD0` | **`0x441E00`** | enthält `cmp al,0x1B`, F `0x441E37` |

   ⚠ Und: `cfind --diff` legt bei mehreren gleichgeformten Prüfungen den
   Unterschiedsblock auf die **erste**, nicht auf die inhaltlich richtige (8.2).
10. ⚠ Der Dateiversatz für `.data` ist **`VA − 0x402200`** (C) bzw.
    **`VA − 0x401E00`** (F), nicht der `.text`-Versatz `0x400C00`. Eine Bytesuche
    in `.data` mit dem `.text`-Versatz liefert stillen Unsinn — mir ist genau das
    beim ersten Griff auf die Cheat-Tafel passiert.
11. ⭐ **Kein Unterschied, obwohl es so aussah:** Die Cheat-Worttafel hat in
    **beiden** Bauten 15 Einträge, dieselben Wörter, dieselbe Schranke
    (`cmp al,0x0F`) und dieselbe Armtafel mit denselben acht Leerarmen
    (C `0x43AFD4`, F `0x43A144`). Auch die zehn Setzer der Fenster-Unterart
    (`word[+0xACA0]`) stimmen 10 zu 10 in Wert und Reihenfolge.

---

### 14. Bauaufgaben, die daraus folgen

1. ⭐ **Das Fenster wird in den Bildschirm gezwungen** (8.2) — mit den drei
   Ausnahmen 9, 35, 48. Wer das nicht nachbaut, hat Fenster, die halb aus dem
   Bild ragen; wer es für Art 9/35/48 auch tut, verschiebt Panel und Hauptmenü.
2. ⭐ **Zeichenreihenfolge:** Liste von hinten nach vorn, Index 0 zuoberst;
   »nach vorn holen« schiebt auf Index 0.
3. ⭐ **»Laden…« (45) und »Synchronisieren…« (47) gehen direkt auf die primäre
   Oberfläche**, nicht über den Rückpuffer. Ohne das bleibt der Bildschirm beim
   Laden schwarz — genau die Klasse Fehler, die in AZ gemeldet ist.
4. ⭐ **Das Immer-oben-Fenster `byte[0x4FD644]`** wird nur beim Rückpufferlauf
   mitgemalt.
5. ⭐ **Feste Lagen:** Hauptmenü (35) links unten bei x = 25, y = Höhe − h − 20.
   »Warten auf…« (42), »Pause« (40), Einstellungen (34): x mittig,
   **y = Höhe/5**. Gefechtsvorbereitung (37): beides mittig.
6. ⭐ **Reparatur kostet 1/30 der Trefferpunkte** (4.3) — bei uns kostet sie nichts.
7. ⭐ **Fabrik- und Minenreparatur sind Umschalter**, keine Ausschalter (4.2).
8. ⭐ **Ausbau ohne Geld scheitert still** — kein Ton, keine Meldung, der Zustand
   bleibt einfach stehen.
9. ⭐ **Eroberung räumt die Ankerzelle** (12.1): alle Einheiten des alten
   Besitzers, die auf der Ankerzelle stehen und `UKOL < 45` haben, werden
   **gelöscht** und die imap-Zelle auf `0xFFFE` gesetzt. ⚠ Nur im C-Bau — für die
   Kampagne (originalgetreu) ist C massgeblich.
10. ⭐ **Der KI-Basisbonus** (12.3) — nur unter beiden Bedingungen, und nur in C.
11. ⭐ **Das Cheat-System** (Abschnitt 1): 15 Wörter, davon vier wirksam, plus
    Strg+Umschalt+U/H/A nach `ARMLEUCHTER`. **Im Netzspiel gesperrt.**
    Für einen Wettkampfmodus (Abschnitt »Gefecht«) ist das die Vorlage.
12. ⭐ **Auflösungen:** genau fünf werden angeboten, und **nur 8-Bit-Modi zählen**.
13. ⭐ **Gebäudenamen:** `+0x17` trägt bei Missionsstart `"1 "` … `"256 "`
    (`0x440930`, `_itoa` + angehängtes Leerzeichen aus `0x4FAEE0`), zusammen mit
    `gebaeudename()` aus BA.9.
14. ⭐ **Die Andockschlange hat sechs Plätze**, auch bei der Basis (sec23 `+0x04`),
    und der Feldbahnhof (12) teilt sich die Tafel mit der Bahnstation (6).
15. ⭐ **`0x440190`:** wird ein Gebäude erobert oder zerstört, muss es aus allen
    400 sec48-Umschlagsätzen gestrichen werden.
16. ⚠ **CD-Prüfung** `X:\cw.id` — für den Nachbau nur zu wissen, nicht zu bauen.

---

### 15. Was ungedeutet bleibt

1. ⚠ **Warum »Reparieren« Trefferpunkte kostet.** Der Faktor (29/30) und die
   Schwelle (50) sind sicher, dreimal wortgleich. Der Sinn ist geraten und wird
   darum nicht behauptet.
2. ⚠ **`0x442ED0`: Art 23 gesucht, Art 21 angelegt.** In beiden Bauten. Ich halte
   es für einen Fehler des Originals, habe aber **kein Bild** dazu. Ungeprüft
   bleibt, ob das kleine 220×100-Fenster beim wiederholten Klick sichtbar
   mehrfach entsteht.
3. ⚠ **`0x441530`** schreibt ein einzelnes **Byte** nach `+0x0C` des Fensters der
   Art 46 (»Mission beendet«). Was der Zeichner daraus macht, ist nicht gelesen.
4. ⚠ **`0x441760`** nullt drei Dwords bei `+0xACA8/+0xACAC/+0xACB0` im
   CD-Spieler-Fenster (Art 12). Bedeutung unbekannt.
5. ⚠ **`0x442670`** setzt bei `dword[0x539238] != 0` fünf Netzwerkschalter
   (`word[0x5407A0]=0`, `byte[0x54079C]=0`, `byte[0x540798]=0`,
   `byte[0x540B94]=0`, `byte[0x540EB8]=1`) und **`dword[0x4FA240] = 0`**
   (Takte seit Missionsbeginn). Die fünf sind nicht weiterverfolgt.
6. ⚠ **`0x4421A0`** ruft vor dem Anlegen `0x4C1720` und legt das Ergebnis in
   `word[0x99170C]`. Nicht gelesen.
7. ⚠ **`0x43B440`, das Kennbyte.** Sechs der elf Rufer (`0x418D05…0x418D79`)
   übergeben je einen anderen Wert; welche Datenträger damit gemeint sind, ist
   nicht nachgesehen.
8. ⚠ **Warum drei Funktionen tot sind** (3.1). Dass sie es sind, ist sauber
   belegt; ob **F** sie ruft, habe ich **nicht** geprüft — der Negativbefund gilt
   nur für C.
9. ⚠ **`0x441120`s Gegenstück `0x44FC90`** ist nur angelesen: es prüft
   `word[0x87AC00] != 0` **und** `word[0x87B054] >= 4`, gibt dann Klang 0x133
   aus und nullt `dword[+0xACA8]`. Die Bedeutung der zwei Zähler
   (Ziehschwelle? Doppelklick?) ist **nicht** geklärt.
10. ⚠ **`0x43C960` benutzt die Ankerzelle** (`+0x00/+0x02`), während die
    Nachbarstelle im selben Behandler mit der **Türzelle** (Anker + `+0x31/+0x32`)
    arbeitet. Ob das Absicht ist, sagt der Code nicht.
11. ⚠ **Die drei toten Cheat-Wortgruppen.** `IDKFA`, `GOBGAS`, `FIREBUG`,
    `PROFIS`, `EIDOS`, `FANFAR`, `TWISTER`, `GUNS`, `WHEELS`, `SPECIALS`,
    `COLUMBUS` haben keinen Arm mehr. Dass `GUNS`/`WHEELS`/`SPECIALS` genau die
    drei Tastencheats benennen, legt nahe, dass die Wirkung von der Worttafel auf
    die Tasten umgezogen ist — **belegt ist nur, dass die Arme leer sind.**

---

## BM. Revier 5: 0x442FB0 … 0x4505F0

52 Funktionen, 10 944 Byte. **Das Revier ist die Fensterverwaltung des Spiels** —
nicht das Zeichnen (das liegt in `0x455C50…0x45CB60`, Abschnitt BA) und nicht die
Auswertung der Klicks (die liegt in `ui_action` `0x4485D0`), sondern die Schicht
dazwischen: **Fenster öffnen, schliessen, nach vorn holen, altern lassen**.

⭐ **Keine einzige Zeichenkette im ganzen Revier.** `funktionen.py` findet hier
nichts zu benennen — genau darum stand von diesen 52 Funktionen bis heute keine
in irgendeiner Unterlage (`grep` über alle `*.md` des Baums: **0 von 52
Treffer**). Benannt wurden sie über ihre *Rufer* und über die *Fensterarten*, die
sie anlegen.

Gelesen an Bestand **C**; **alle 52 in F gegengelesen** (Abschnitt 12).

---

### 1. Adresstafel

⚠ Die F-Spalte ist **nicht gerechnet**. Wo »Anleger« steht, ist sie über den
eindeutigen Rufer des F-Anlegers aus BA.4 gewonnen; wo »cfind« steht, über
`cfind.py` mit dem Vermerk »eindeutig«. Die sechs Zeilen, bei denen `cfind` etwas
**anderes** vorschlug, sind mit ⚠ markiert — siehe Abschnitt 12.4.

#### 1a. Die Fensteröffner (»wenn nicht schon offen, dann öffnen«)

| C | F | Byte | Ruf | Art | Beleg F | Was es öffnet |
|---|---|---:|---:|---:|---|---|
| `0x444490` | `0x443480` | 320 | 3 | **1** | cfind | Einheiten-/Befehlsmenü (80×80) |
| `0x4445D0` | `0x4435C0` | 176 | 2 | **2** | Anleger | Bahnhof |
| `0x444680` | `0x443670` | 192 | 2 | **2** | Anleger | Bahnhof (zweiter Weg, Feldbahnhof) |
| `0x445E70` | `0x444E00` | 400 | 2 | **5** | cfind | Flughafen / Hangar |
| `0x446000` | `0x444F90` | 224 | 2 | **6** | Anleger | Basis |
| `0x4460E0` | `0x445070` ⚠ | 176 | 2 | **7** | Anleger | Art 7, 600×340 (ungedeutet) |
| `0x443E00` | `0x442DF0` | 224 | 2 | **8** | Anleger | Fabrik (Waffen / Fahrwerk / Spezial) |
| `0x444440` | `0x443430` | 80 | 8 | **9** | cfind | **Bedienfeld** (`PANEL.DTA`, 204×170) |
| `0x446680` | `0x445610` | 256 | 2 | **10** | cfind | Ja/Nein-Frage |
| `0x446190` | `0x445120` | 224 | 2 | **11** | Anleger | Hafen / Werft |
| `0x446B30` | `0x445AD0` ⚠ | 192 | 2 | **12** | Anleger | CD-Spieler |
| `0x4469A0` | `0x445940` | 400 | **44** | **13** | cfind | **Meldungsfenster** |
| `0x443B90` | `0x442B80` | 176 | 5 | **14** | cfind | Spielstand laden |
| `0x443AA0` | `0x442A90` | 240 | 3 | **15** | cfind | Spiel speichern |
| `0x443980` | `0x442970` | 288 | 3 | **16** | cfind | Materialtransport (Von/Nach/Start) |
| `0x443C40` | `0x442C30` ⚠ | 176 | 2 | **17** | Anleger | Hauptmenü im Spiel |
| `0x4438A0` | `0x442890` ⚠ | 224 | 2 | **18** | Anleger | Terranium-Mine |
| `0x4436E0` | `0x4426D0` | 224 | 2 | **19** | cfind | Einheiten-Info |
| `0x442FB0` | `0x441F90` ⚠ | 224 | 2 | **20** | Anleger | **Generator** ⚠ (Abschnitt 5) |
| `0x443620` | `0x442620` ⚠ | 192 | 2 | **22** | Anleger | Einheitenliste |
| `0x4437C0` | `0x4427B0` ⚠ | 224 | 2 | **23** | Anleger | Depot |
| `0x443230` | `0x442220` | 176 | **0** | **28** | Anleger | Stromversorgung — **tot** (Abschnitt 6) |
| `0x443180` | `0x442170` | 176 | 2 | **29** | Anleger | Forschungsergebnisse |
| `0x443090` | `0x442070` | 240 | 2 | **31** | Anleger | Nachschubposten |
| `0x444300` | `0x4432F0` | 176 | 3 | **44** | cfind | Statuszeile |
| `0x444000` | `0x442FF0` | 384 | **28** | **45** | cfind | **Ladebalken »Laden…«** |

#### 1b. Die Schliesser

⚠ Die F-Spalten von 1b und 1c stammen aus `cfind.py`. Wo es »eindeutig« meldete,
ist der Wert belegt; wo »mehrdeutig« oder »ungenau«, ist er ein **Vorschlag** —
er passt aber durchweg zum jeweils konstanten Blockabstand (`0x447xxx`: −0x1010,
`0x44F7C0…0x4505F0`: −0x1350) und trifft in F einen echten Funktionsanfang aus
dem `int3`-Abtast.

| C | F | Byte | Ruf | Was es schliesst |
|---|---|---:|---:|---|
| `0x443EE0` | `0x442ED0` | 144 | 2 | **alle** Fenster der Art 13 |
| `0x443F70` | `0x442F60` | 144 | 2 | **alle** Fenster der Art 45 (Ladebalken) |
| `0x4443B0` | `0x4433A0` | 144 | 2 | **alle** Fenster der Art 44 (Statuszeile) |
| `0x447480` | `0x446470` | 64 | 3 | das erste Fenster der Art 16 |
| `0x4474C0` | `0x4464B0` | 64 | 2 | das erste Fenster der Art 40 (Pause) |
| `0x447500` | `0x4464F0` | 96 | 2 | Hilfefenster (Art 30) mit Betriebsart *n* |
| `0x447600` | `0x4465F0` | 80 | 3 | Karte (Art 3), **Betriebsart 4** = Materialtransport |
| `0x447650` | `0x446640` | 80 | 4 | Karte (Art 3), **Betriebsart 2** = Luft-Einsatzplanung |
| `0x4476A0` | `0x446690` | 80 | 3 | Karte (Art 3), **Betriebsart 5** = Einheiten-Transport |
| `0x44F7C0` | `0x44E470` | 240 | 3 | Sammelschliesser über eine Arttafel (Abschnitt 8) |
| `0x44FE10` | `0x44EAC0` | 160 | **24** | **alles ausser Art 9, 35, 46, 48** — »Schirm freiräumen« |
| `0x4502A0` | `0x44EF50` | 112 | 3 | ⭐ das Infofenster **eines bestimmten Gebäudes** (Abschnitt 7) |

#### 1c. Der Rest

| C | F | Byte | Ruf | Was es ist |
|---|---|---:|---:|---|
| `0x445450` | `0x4443F0` | 512 | 7 | ⭐ **Markiertafel des Materialtransports** (Abschnitt 9) |
| `0x446BF0` | `0x445BA0` | 224 | 4 | Karte (Art 3) einer Kennung **neu malen** |
| `0x446DE0` | `0x445DB0` | 288 | 4 | ⭐ **die Maus über den Fenstern** (Abschnitt 4) |
| `0x4476F0` | `0x4466E0` | 192 | 2 | Fenster **verschieben** (Ziehen mit der Maus) |
| `0x4477B0` | `0x4467A0` | 368 | 3 | Mausklick auf der Karte → Zelle |
| `0x44F8B0` | `0x44E560` | 416 | 2 | ⭐ **ein Bild der Schliessblende** (Abschnitt 10) |
| `0x44FA50` | `0x44E700` | 192 | 2 | Folgefenster nach dem Schliessen (Abschnitt 8) |
| `0x44FB10` | `0x44E7C0` | 272 | 2 | ⭐ **der Schliessvorgang, 6 Bilder** (Abschnitt 10) |
| `0x44FC90` | `0x44E940` | 224 | 4 | der Öffnungsvorgang, Abschluss (Abschnitt 10) |
| `0x44FD70` | `0x44EA20` | 160 | 2 | Fenster Art 4 mit Kennung *n* neu malen + nach vorn |
| `0x4500B0` | `0x44ED60` | 64 | 5 | Bedienfeld (Art 9) neu malen |
| `0x4500F0` | `0x44EDA0` | 208 | 2 | »wartet gerade ein Fenster auf eine Eingabe?« |
| `0x4505E0` | — | 16 | 0 | ⚠ **leer** (`ret`), kein Rufer, kein Zeiger — tot |
| `0x4505F0` | `0x44F2A0` | 416 | 5 | ⭐⭐ **der Fenstertakt** (Abschnitt 11) |

#### 1d. Sockel und Bausteine, die hier tragen

| Adresse | Bedeutung | woher |
|---|---|---|
| `0x8B9038` | Fenstersätze, 20 × 44 324 B | BA.4 |
| **`0x87AFF8`** | ⭐ **die Reihenfolgeliste der offenen Fenster** (1 Byte je Platz) | hier |
| **`0x4FD64C`** | ⭐ **Anzahl offener Fenster** | hier |
| `0x441270` | Fenster in die Liste eintragen (**hinten**) + Ereignisbyte `0x539930` | AE-2 / hier |
| `0x441190` | Fenster auf den Schirm zwängen | hier |
| `0x44FC20` | ⭐ Fenster **nach vorn** (auf Platz 0) — Marke `"Window up not found."` | hier |
| `0x4471A0` | ⭐ **Fenster schliessen** — Marke `"Window not found"` | hier |
| `0x4412E0` | Fenster neu malen (`0x487630` + `0x441190`) | hier |
| `0x487630` | Fensterverteiler, 48 Arme | BA.8 |
| `0x4FD648` / `0x4FD644` | das sich **öffnende** / **schliessende** Fenster | hier |
| `0x4FD650` / `0x4FD654` | Fenster unter der Maus / dessen Trefferkennung | hier |
| `0x4FA248` | der Takt (Wort) | hier |
| `0xC06914` | Gebäudetafel, Schrittweite **76** | AE-2 |
| **`0x4FDE08`** | ⭐ **Gebäudeart → Fensterart** (18 Dwords) | hier |
| **`0x4379F0`** | ⭐ **Sprungtafel: Klick auf Gebäudeart → Öffner** (17 Arme) | hier |
| `0x502AD8` | angewähltes Objekt; **≥ 60000 heisst Gebäude Nr. (Wert − 60000)** | hier |

---

### 2. ⭐⭐ Die Schablone: wie ein Fenster im Original geöffnet wird

**26 der 52 Funktionen sind dieselbe Funktion, 26-mal ausgeprägt.**

```
oeffne_art_N(kennung, x, y):
    fuer i = 0 .. byte[0x4FD64C]-1:                 ; alle offenen Fenster
        w = byte[0x87AFF8 + i]
        wenn byte[0x8B9038 + 44324*w] == N          ; gleiche Art?
             [ und word[+0x0C] == kennung ]:        ; gleiches Objekt?
                 RAUS — es ist schon offen
    idx = anleger_ArtN(x, y, kennung)               ; BA.4
    0x441270(idx)          ; in die Liste, HINTEN; Ereignisbyte 0x539930 = Art
    [ word[+0xACA0] = Betriebsart ]                 ; nur wo es eine gibt
    0x441190(idx)          ; auf den Schirm zwaengen
    0x44FC20(idx)          ; nach VORN holen (Platz 0)
```

⭐ **Das Nullmodell steckt in der Wache selbst.** Von den 31 Funktionen des
Reviers, die eine Artwache tragen, prüfen **30 genau die Art, die sie danach
anlegen**. Eine einzige tut es nicht (`0x442FB0`, Abschnitt 5). Wäre die Wachart
frei gewählt, träfe sie bei 48 Arten mit ~1/48; erwartet wären 0,6 Treffer,
beobachtet sind 30. Die Deutung »die Wache ist die Doppelöffnungssperre« ist
damit belegt und nicht bloss plausibel.

⭐ **Zwei Sorten Fenster, am Code unterscheidbar.** 15 Öffner vergleichen
zusätzlich `word[+0x0C]` — das ist die **Kennung des Objekts** (bei den
Gebäudefenstern der Platz in `0xC06914`, vom Anleger als drittes Argument nach
`+0x0C` geschrieben; nachgesehen an `0x459851` für Art 20 und `0x459E91` für
Art 23). Diese Fenster gibt es **je Objekt einmal**. Die übrigen sind
Einzelstücke: von Art 29 (Forschungsergebnisse) kann es nur eines geben.

#### Wer nicht nach vorn geholt wird

`0x444490` (Art 1, Befehlsmenü), `0x4445D0`/`0x444680` (Art 2) und `0x444300`
(Art 44, Statuszeile) rufen `0x44FC20` **nicht**. Sie bleiben, wo `0x441270` sie
hingelegt hat — hinten. Für die Statuszeile ist das offensichtlich richtig.

---

### 3. ⭐⭐ Der Klick auf ein Gebäude — die vollständige Zuordnung

`0x43710B` liest `word[0x502AD8]` (das angewählte Objekt). Ist der Wert
`>= 0xEA60` (60 000), so ist es ein **Gebäude**; `bx += 0x15A0` lässt den Wert
über 65 536 überlaufen und liefert damit `bx − 60000` = den Platz in `0xC06914`.
Aus dessen Art (`byte[0xC06914 + 76·platz]`) wird über die Sprungtafel
`0x4379F0` (17 Arme, `dec eax; cmp eax, 0x10`) der Öffner gewählt und mit
`(platz, MausX−3, MausY−3)` gerufen.

| Geb.art | Name (`gebaeudename` `0x459110`) | Arm | Öffner | Fensterart | Marke im Zeichner |
|---:|---|---|---|---:|---|
| 1 | Basis | `0x437151` | `0x446000` | **6** | »Basis «, »Forschung«, »Produktion« |
| 2 | Waffenfabrik | `0x43717D` | `0x443E00` | **8** | » Fabrik «, »Waffen gelagert : ]« |
| 3 | Fahrwerkfabrik | `0x43717D` | `0x443E00` | **8** | dieselbe, »Fahrwerke gelagert : [« |
| 4 | Spezialfabrik | `0x43717D` | `0x443E00` | **8** | dieselbe, »Spezialteile gelagert : {« |
| 5 | *(ohne Namen)* | `0x4371A9` | `0x4437C0` | **23** | »Depot «, »Aussenden« |
| 6 | Bahnhof | `0x4371D5` | `0x4445D0` | **2** | » Bahnhof«, »Transportsystem« |
| 7 | *(ohne Namen)* | `0x437207` | `0x442FB0` | **20** | »Generator«, »Stromerzeugung : « |
| 8 | — | `0x43798A` | *nichts* | — | |
| 9 | Flughafen | `0x437233` | `0x445E70` | **5** | »Flughafen «, »Hangar«, »Jagdflieger« |
| 10 | Rohstoff-Mine | `0x43725F` | `0x4438A0` | **18** | »Terranium-Mine «, »Rohstoffvorkommen: « |
| 11 | Hafen | `0x43728B` | `0x446190` | **11** | »Werft «, »Patrol-Boot« |
| 12 | Bahnhof (Feld) | `0x4372B7` | `0x444680` | **2** | wie 6 |
| 13 | *(ohne Namen)* | `0x4372E9` | `0x442ED0` | **21** | (ausserhalb des Reviers) |
| 14 | *(ohne Namen)* | `0x437315` | `0x443090` | **31** | »Angebot des Nachschubpostens« |
| 15 | Rohstoff-Mine | `0x43725F` | `0x4438A0` | **18** | wie 10 |
| 16 | Hafen | `0x43798A` | *nichts* | — | |
| 17 | *(ohne Namen)* | `0x437341` | `0x443CF0` | — | (ausserhalb des Reviers) |

#### ⭐⭐ Das Nullmodell — es kommt aus einem ganz anderen Teil des Programms

Abschnitt **AE-2** hat aus dem **Vorspann der Kampagne** drei Aussagen über das
Ereignisbyte `byte[0x539930]` gewonnen, ohne diesen Code je gesehen zu haben:

| AE-2 sagt | diese Tafel sagt | HELPG-Text |
|---|---|---|
| Ereignisbyte **2** → Fenster »Bahnhof« | Gebäudeart 6/12 → **Fensterart 2** | #72 »**Bahnstationen** bilden Kreuzungen…« |
| Ereignisbyte **5** → Fenster »Hangar« | Gebäudeart 9 (Flughafen) → **Fensterart 5** | #76 »Im **Hangar** werden Flugzeuge repariert…« |
| Ereignisbyte **18** → Fenster »Terranium-Mine« | Gebäudeart 10/15 (Mine) → **Fensterart 18** | #78 »Im **Mineninfofenster**…« |

**Drei von drei.** Bei 48 Fensterarten wäre die Wahrscheinlichkeit, dass drei
freie Zuordnungen zufällig zusammenfallen, `(1/48)³ ≈ 9·10⁻⁶`. Dazu kommt, dass
das Ereignisbyte in `0x441270` **genau die Art des zuletzt eingetragenen
Fensters** ist (`al = byte[44324·w + 0x8B9038]`, ausser Art 3) — die
Verbindungskette ist damit geschlossen und nicht erschlossen.

⭐ **Nebenertrag: drei Gebäudearten bekommen einen Namen.** `gebaeudename`
`0x459110` lässt die Arten 5, 7, 13, 14 namenlos (Sprungtafel `0x45931C`,
Standardarm `0x4592E3`). Über ihre Fenster heissen sie: **5 = Depot**,
**7 = Generator** (deckt sich mit AE-2, wo Art 7 aus HELPG #74 »Generatoren«
gelesen wurde), **14 = Nachschubposten**.

#### ⭐ Was die Zeichner sonst noch benennen

Nebenbei fällt eine Tafel ab, die BA.8 nur der Anzahl nach hatte: der
Fensterverteiler `0x487630` springt über `0x487888` auf **48** Zeichner. Die
hier gebrauchten, mit ihren Marken:

| Art | Zeichner | Marke | Art | Zeichner | Marke |
|---:|---|---|---:|---|---|
| 2 | `0x463FB0` | Bahnhof | 18 | `0x474220` | Terranium-Mine |
| 5 | `0x465050` | Flughafen / Hangar | 19 | `0x474FE0` | Einheiten-Info |
| 6 | `0x467C60` | Basis | 20 | `0x476410` | **Generator** |
| 8 | `0x46EDC0` | Fabrik | 22 | `0x476D00` | Einheitenliste |
| 9 | `0x46FE10` | Bedienfeld | 23 | `0x4790A0` | **Depot** |
| 11 | `0x471800` | Werft | 28 | `0x47C800` | Stromversorgung |
| 12 | `0x472D40` | CD-Spieler | 29 | `0x47CA30` | Forschungsergebnisse |
| 14 | `0x4733B0` | Spielstand laden | 31 | `0x47D340` | Nachschubposten |
| 15 | `0x473750` | Spiel speichern | 40 | `0x485F10` | Pause |
| 16 | `0x473BF0` | Materialtransport | 45 | `0x487060` | **»Laden…«** |
| 17 | `0x473EC0` | Hauptmenü im Spiel | | | |

---

### 4. ⭐⭐ Die Reihenfolgeliste `0x87AFF8` — Platz 0 ist oben

Die Liste ist ein Byte-Feld mit `byte[0x4FD64C]` Einträgen; jeder Eintrag ist ein
Fensterplatz (0…19). Vier Funktionen fassen sie an:

```
0x441270   eintragen:    byte[0x87AFF8 + n++] = idx           ; ANS ENDE
0x44FC20   nach vorn:    alles 0..k-1 um eins nach hinten,
                         byte[0x87AFF8 + 0] = idx             ; AUF PLATZ 0
0x4471A0   schliessen:   alles nach k um eins nach vorn, n--
0x446DE0   Maustreffer:  das getroffene Fenster auf Platz 0
```

⚠ `0x4471A0` schiebt mit einer **Byteschleife** (`0x447370`), die keine
Relokation hinterlässt: `reloc_refs --addr 0x87AFF8` sieht im ganzen Programm nur
**5 Schreibstellen**. Die Warnung der Einweisung trifft also auch ohne
Blockbefehl zu.

**Dass Platz 0 oben ist, steht nicht da, es folgt aus zwei Suchläufen:**

1. `0x446DE0` (Maus) geht die Liste **von 0 aufwärts** und bricht beim ersten
   Treffer ab — dann muss 0 das oberste sein, sonst träfe man durch ein
   verdecktes Fenster hindurch.
2. Die Tastaturschleife `0x413EC4` tut dasselbe: von 0 aufwärts, Abbruch beim
   ersten Fenster, dessen Behandler (`0x487A10`) 1 oder 2 liefert.

⭐ Daraus erklärt sich die **Reihenfolge der Öffner-Schablone**: `0x441270` legt
das neue Fenster ans **Ende** (= ganz hinten), erst `0x44FC20` holt es nach
**vorn**. Wer einen der beiden Aufrufe weglässt, bekommt ein Fenster, das unter
allen anderen liegt — genau das tun die vier Öffner aus Abschnitt 2.

#### `0x446DE0` im einzelnen

```
wenn word[0x502AC0] != 0 und byte[0x8B7250] == 0:  word[0x4FD650] = 0xFFFF; RAUS
dword[0x8B62A4] = MausX (dword[0x502AA8])      ; die Sockel aus BA.2
dword[0x8B62A0] = MausY (dword[0x502AAC])
fuer i = 0 .. n-1:
    r = 0x45CB60( byte[0x87AFF8+i] )           ; Trefferpruefung des Fensters
    wenn r == -5 -> r = 0 ;  wenn r == 0 -> r = -5      ; die zwei tauschen
    wenn r != 0xFFFF:
        word[0x4FD654] = r                     ; Trefferkennung
        Fenster auf Platz 0
        word[0x4FD650] = Fensterplatz
        wenn r == 0xFFFA und word[0x502AC8] != 0:  0x487630(fenster)  ; neu malen
        Rueckgabe 1
Rueckgabe 0, word[0x4FD650] = 0xFFFF
```

⭐ Das bestätigt BA.2 unabhängig: `0x8B62A4`/`0x8B62A0` sind Maus-X/Y (hier
**geschrieben**), und `word[0x502AC8]` ist der Tastenzustand.

---

### 5. ⚠⭐ Ein Fehler des Originals: die Doppelöffnungssperre des Generatorfensters greift nie

`0x442FB0` ist der Öffner für **Gebäudeart 7 = Generator** und legt
**Fensterart 20** an (Anleger `0x4597B0`: `0x459829 mov byte[edx], 0x14` = Art 20,
`0x45983F word[+0x06] = 0xDC = 220`, `0x459848 word[+0x08] = 0x64 = 100` — die
Masse aus BA.4). Seine Wache prüft aber:

```
0x442FF0   cmp byte ptr [ebx + 0x8B9038], 0x17     ; 0x17 = 23, nicht 20
0x442FF9   movsx esi, word ptr [ebx + 0x8B9044]    ; +0x0C = Gebaeudeplatz
0x443006   je  Ende
```

**Art 23 ist das Depot** (`0x4437C0`, Zeichner `0x4790A0`, Marke »Depot «). Die
Wache verlangt also ein offenes **Depot**fenster mit demselben *Gebäudeplatz*.
Ein Platz in `0xC06914` trägt genau ein Gebäude, und das ist entweder ein Depot
(Art 5) oder ein Generator (Art 7) — **die Bedingung kann nie erfüllt sein.**

**Folge, und sie ist prüfbar:** zweimal auf denselben Generator klicken erzeugt
**zwei** Generatorfenster; *n*-mal klicken erzeugt *n*, bis die 20 Plätze voll
sind und der Anleger `0xFFFF` liefert. Bei allen 30 anderen Öffnern des Reviers
öffnet der zweite Klick nichts.

⭐⭐ **Der Fehler steckt in BEIDEN Auslieferungen.** F `0x441F90` (gefunden über
den einzigen Rufer des F-Anlegers `0x458450`, BA.4 Art 20):

```
F 0x441FCD   cmp al, 0x17                          ; ebenfalls 23
F 0x441FFE   call 0x402351 -> 0x458450             ; ebenfalls der Art-20-Anleger
```

Es ist also **kein Auslieferungsunterschied**, sondern ein Tippfehler von 1997,
der beide Bauten überlebt hat. Der Nachbar `0x4437C0` (Depot, Wache 23, legt 23
an) ist bis auf den Anleger befehlsgleich — die Herkunft ist offensichtlich eine
Kopie.

⚠ **Zweiter Fundort desselben Fehlers:** die Tafel `0x4FDE08` (Abschnitt 7) trägt
für Gebäudeart 7 ebenfalls **23** statt 20. Ich melde es trotzdem als Fehler und
nicht als Absicht, weil der Klickverteiler, der Zeichner und HELPG #74 zu dritt
sagen, dass Art 7 der Generator ist und Art 20 sein Fenster.

---

### 6. ⭐ Das Fenster »Stromversorgung« (Art 28) ist im fertigen Spiel nicht erreichbar

`0x443230` legt Fensterart 28 an (220×60, Anleger `0x45A1B0`). Der Zeichner
`0x47C800` malt **»Stromversorgung«** und **»Stromverbrauch : «** — das Fenster
ist fertig.

* `0x443230` hat **keinen Rufer** (`rufer.py`: leer) und steht **nirgends als
  Dword** im Bild (Vollabtast über alle Sektionen: kein Treffer) — also auch in
  keiner Sprungtafel.
* Der Anleger `0x45A1B0` hat genau **einen** Rufer, nämlich `0x443283` in
  `0x443230`.
* Dasselbe in F: `0x458E50` ← `0x442273`, und `0x442220` hat keinen Rufer.

Damit ist die Kette geschlossen: **es gibt keinen Weg, Art 28 zu öffnen.**
⚠ Der Negativbefund ruht auf `rufer.py` (relative Sprünge) **plus** einem
Dword-Abtast; ein Aufruf über einen zur Laufzeit gerechneten Zeiger wäre nicht
erfasst.

Ebenso tot: **`0x4505E0`** — 16 Byte, Inhalt `ret` + `int3`-Polster, kein Rufer,
kein Zeiger, und `cfind` findet dazu nicht einmal einen Funktionsanfang.

---

### 7. ⭐⭐ `0x4FDE08` — die Tafel »Gebäudeart → Fensterart«, und drei kaputte Einträge

`0x4502A0(gebäudeplatz)` schliesst das Infofenster **eines bestimmten Gebäudes**:

```
typ  = byte[0xC06914 + 76*platz]
art  = dword[0x4FDE08 + 4*typ]          ; <- die Tafel
fuer k = 0 .. 19:                       ; 0x8B9038, Schritt 0xAD24, Ende 0x991708
    wenn byte[+0x00] == art  und  word[+0x0C] == platz:   0x4471A0(k)
```

⭐ **Die Tafel ist ein von der Sprungtafel `0x4379F0` völlig unabhängiger Beleg**
— Daten gegen Code. Ausgelesen (18 Dwords ab `0x4FDE08`, danach Nullen):

| Geb.art | Tafel `0x4FDE08` | Klickverteiler `0x4379F0` | |
|---:|---:|---:|---|
| 1 | 6 | 6 | ✓ |
| 2 | 8 | 8 | ✓ |
| 3 | 8 | 8 | ✓ |
| 4 | 8 | 8 | ✓ |
| 5 | 23 | 23 | ✓ |
| 6 | 2 | 2 | ✓ |
| **7** | **23** | **20** | ⚠ (Abschnitt 5) |
| 8 | 0 | *nichts* | ✓ |
| 9 | 5 | 5 | ✓ |
| 10 | 18 | 18 | ✓ |
| 11 | 11 | 11 | ✓ |
| 12 | 2 | 2 | ✓ |
| 13 | 21 | 21 | ✓ |
| **14** | **18** | **31** | ⚠ |
| **15** | **11** | **18** | ⚠ |
| 16 | 0 | *nichts* | ✓ |

**13 von 16 gleich.** Nullmodell: bei 48 möglichen Fensterarten wären bei freier
Wahl 0,3 Übereinstimmungen zu erwarten. Dass die Tafel dasselbe meint wie der
Verteiler, ist damit belegt.

⚠ **Die drei Abweichungen sind kein Rauschen, sie haben eine Form.** Art 14 trägt
den Wert, der zu 15 gehört (Mine = 18), und Art 15 den, der zu 16 gehört
(Hafen = 11). **Ab Eintrag 14 ist die Tafel um eins verrutscht** — ein Eintrag
fehlt. Das trifft sich mit `gebaeudename` `0x459110`, das »Rohstoff-Mine« bei
Art **15** und »Hafen« bei Art **16** führt, während die Tafel sie bei 14 und 15
hat. **Zwei unabhängige Quellen gegen die Tafel.**

⚠ **Was das im Spiel tut, habe ich nicht gemessen** — die Deutung des *Fehlers*
ist belegt, seine *Wirkung* ist gefolgert: verschwindet ein Nachschubposten
(Art 14), so wird nicht sein Fenster (Art 31) geschlossen, sondern ein
Minenfenster gesucht; das Fenster des Postens bleibt stehen. Ebenso beim
Generator. Die zwei Rufer sitzen bei `0x43CF52` und `0x43CFBC`.

---

### 8. Die zwei Aufräumtafeln beim Schliessen

**`0x44FA50(fenster)`** ist der **letzte** Aufruf von `0x4471A0`. Sie ruft
zuerst `0x463CE0(fenster)` und schliesst dann ein **Folgefenster**, über
`art − 2` → `byte[0x44FAD4]` → `dword[0x44FABC]`:

| geschlossen wird Art | dann schliesst sich auch |
|---:|---|
| 2 (Bahnhof) | Karte, Betriebsart **5** (Einheiten-Transport) |
| 3 (Karte) | Art **16** (Materialtransport-Fenster) |
| 5 (Flughafen) | Karte, Betriebsart **2** (Luft-Einsatzplanung) |
| 6 (Basis) | Karte, Betriebsart **5** |
| 16 | Karte, Betriebsart **4** (Materialtransport) |
| alle übrigen | nichts |

⭐ Ein sauberer Kreuzbeleg für AP: die Zuordnung *Betriebsart 2 = Luft*,
*4 = Materialtransport*, *5 = Einheiten-Transport* fällt genau mit den Fenstern
zusammen, aus denen man diese Planungen anstösst (Flughafen → Luft,
Bahnhof/Basis → Einheiten-Transport). `0x44FB10` trägt dieselbe Tafel ein
zweites Mal (`0x44FBD8` / `0x44FBC0`), Byte für Byte gleich.

**`0x44F7C0()`** ist der Sammelschliesser, über `art − 1` → `byte[0x44F870]` →
`dword[0x44F85C]`: Art 1 und 16 immer schliessen; **Art 3 nur, wenn die
Betriebsart 3 oder 4 ist** (Raketen- bzw. Materialtransport-Planung); Art 10 nur
bei Betriebsart 2; alles andere stehen lassen. ⭐ Die Schleife macht `dec esi`
vor dem Schliessen und `inc esi` danach — weil `0x4471A0` die Liste
zusammenschiebt, wäre sonst ein Fenster übersprungen worden. Das Original hat
diesen Fallstrick gesehen.

---

### 9. ⭐ `0x445450` — welche Gebäude beim Materialtransport anklickbar sind

```
markiere(kennung, modus):
    t = byte[0x6E26C8 + 78*kennung + 0x40]      ; Einheitenfeld +0x40 = "trans"
    memset(0xB133A0, 0, 256)                    ; 0x40 Dwords
    wenn modus == 0:                            ; QUELLE waehlen
        fuer g = 0 .. 254:
            byte[0xC06914 + 76g + 0x01] == byte[0x4FA284]   ; eigenes Gebaeude
            byte[+0x14] != 0
            typ = byte[+0x00]                   ; 1..16
            ueber die Sprungtafel 0x4455C0 (Index 0x4455D8):
                Typen 1..5 -> byte[0xB133A0 + g] = 1 ;  Typ 6 -> ueberspringen
    sonst:                                      ; ZIEL waehlen
        z    = byte[0x77AC54 + 18t]             ; das eingetragene Ziel
        ztyp = byte[0xC06914 + 76z]
        fuer g = 0 .. 254 (eigenes, +0x14 != 0):
            wenn byte[+0x00] in 0x4FDC00[10*ztyp .. +9]:  byte[0xB133A0 + g] = 1
    byte[0xB133A0 + byte[0x77AC54 + 18t]] = 2              ; das ZIEL
    fuer i = 0..3: byte[0xB133A0 + byte[0x77AC50 + 18t + i]] = 3   ; die QUELLEN
```

Alles daran ist mit vorhandenen Befunden verzahlt und **nicht** neu geraten:
`0x77AC50` ist sec48, die **Umschlagsätze, 400 × 18** (Abschnitte Y und BE);
`Einheitenfeld +0x40` ist dort schon als `trans` = Satzindex belegt; `+0x00…+0x03`
sind `zdroj0-3` (die vier Quellen) und `+0x04` ist `cil` (das Ziel) — **genau die
Felder, die diese Funktion an genau diesen Versätzen liest.** `0x4FDC00` ist die
**Verträglichkeitstafel 19 × 10** aus AT/AG.

⭐ **Die Schrittweite 18 ist gerechnet, nicht nachgeschlagen:** `edx = 2t`,
`ebp = 9·edx`, `ebx = edx + 2·ebp` → `18t`. Sie trifft die in Abschnitt Y
verzeichnete Satzgrösse von sec48 (`7200 / 400 = 18`) — Regel 2 erfüllt.

**`0xB133A0` ist damit die Markiertafel: 256 Byte, ein Byte je Gebäude**,
`0 = nicht anklickbar`, `1 = mögliche Quelle bzw. mögliches Ziel`, `2 = das
eingetragene Ziel`, `3 = eine der vier eingetragenen Quellen`. Die Rufer
(`0x445866` im Öffner der Materialtransport-Karte, dazu fünf Stellen in
`ui_action`) rufen sie stets zusammen mit `0x446BF0` (Karte neu malen), und die
Argumente kommen aus dem Fenstersatz: `word[+0xACA2]` (die Einheit) für
`0x445450`, `word[+0xACA6]` (die Kennung) für `0x446BF0`.

⚠ **Ungedeutet bleibt der Unterschied der zwei Betriebsarten.** Bei `modus == 0`
werden **alle** eigenen Gebäude der Arten 1…5 markiert, ohne
Verträglichkeitsprüfung; nur bei `modus != 0` wird `0x4FDC00` befragt. Warum die
Quellenwahl keine Prüfung hat, sagt der Code nicht.

---

### 10. ⭐⭐ Fenster öffnen und schliessen sind ANIMIERT

Das war so nirgends aufgeschrieben.

**Schliessen — sechs Bilder.** `0x44FB10` läuft je Takt einmal:

```
w = byte[0x4FD644]                       ; das schliessende Fenster, 0xFF = keins
wenn byte[0x87ADFC] < 6:
    0x44F8B0(w) ; byte[0x87ADFC]++       ; EIN Bild der Blende
sonst:
    0x463CE0(w) ; byte[0x4FD644] = 0xFF  ; endgueltig weg
    Folgefenster ueber die Tafel 0x44FBC0 (Abschnitt 8)
```

`0x44F8B0(fenster)` arbeitet **im eigenen Punktpuffer des Fensters**
(`dword[+0xAC9C]`, BA.5): von der Mittelzeile aus wird jede zweite Zeile
paarweise nach innen kopiert (`rep movsd` + `rep movsb`, Zeilenlänge = Breite),
und die frei werdende oberste und unterste Zeile wird mit **`0xFF`** gefüllt —
also **durchsichtig**, genau der Wert, den `0x4409B0` beim Aufsetzen überspringt.
⭐ **Das Fenster klappt zur Mittellinie zusammen und verschwindet.**

**Öffnen — vier Bilder.** `0x4505F0` (der Takt) beginnt mit:

```
wenn word[0x4FD648] != 0xFFFF:                  ; ein Fenster oeffnet gerade
    wenn word[0x87B054] < 4:  word[0x87B054]++
    0x44FC90( word[0x4FD648], word[0x87B050] )
```

und `0x44FC90` malt erst, **wenn `word[0x87B054] >= 4`** — dann Klang `0x133`
über `0x4047E0`, danach `0x4412E0` (bei Art 43 der Zeichner direkt), und
`word[0x4FD648] = 0xFFFF`.

| Sockel | Bedeutung |
|---|---|
| `word[0x4FD648]` | Fenster, das gerade **aufgeht** (`0xFFFF` = keins) |
| `word[0x87B054]` | Bildzähler des Aufgehens, 0…4 |
| `byte[0x4FD644]` | Fenster, das gerade **zugeht** (`0xFF` = keins) |
| `byte[0x87ADFC]` | Bildzähler des Zugehens, 0…6 |
| Klang | `0x133` = 307 beim Aufgehen |

⚠ **Nicht gemessen:** was `word[0x87B050]` (das zweite Argument von `0x44FC90`)
bedeutet. `0x44FC90` benutzt es nur, um `dword[0x8C3CE0 + …]` zu nullen.

---

### 11. ⭐⭐ Der Fenstertakt `0x4505F0` — und `+0xAD22` ist eine Lebensdauer

```
schleife ueber alle 20 PLAETZE (nicht ueber die Liste!):
    edi laeuft von 0x8C3D5A in Schritten 0xAD24 bis 0x99C42A
    art = byte[edi - 0xAD22]          ; = +0x00
    wenn art == 0 oder Platz == byte[0x4FD644]:  weiter
    Verteiler ueber art-2 (0..0x29) -> Tafel 0x4506D8 / Index 0x45070C:
        - die meisten Arten: nichts
        - eine Gruppe: alle 30 Takte neu malen  (word[0x4FA248] % 30 == 0)
        - je eine Art: 0x446CD0 bzw. 0x4AA950
    alle 20 Takte (word[0x4FA248] % 20 == 0):
        wenn byte[edi] != 0:                    ; +0xAD22
            byte[edi]-- ; wenn 0 -> 0x4471A0(Platz)     ; SELBST SCHLIESSEN
```

⭐⭐ **`byte[+0xAD22]` ist ein Selbstschliesszähler**: ein Fenster mit dem Wert
*n* schliesst sich nach **20·n Takten** von selbst. `0x441270` setzt ihn beim
Eintragen jedes Fensters auf **0** (= niemals), `0x4469A0` (Art 13, das
Meldungsfenster, 44 Rufer) setzt ihn aus einem **Argument** — die Meldungen im
Spiel verschwinden also von allein, und der Rufer bestimmt, wie lange sie stehen
bleiben.

⭐ **Zwei unabhängige Belege für »20 Plätze à 44 324 Byte« stehen hier als
ROHZAHLEN im Code**, was BA.4 nur aus der Lücke zur nächsten Globalen erschlossen
hatte:

* `0x4505F0`: Schrittweite `0xAD24 = 44324`, Endadresse **`0x99C42A`**
  = `0x8C3D5A + 20·44324`, auf das Byte genau.
* `0x447480`, `0x4474C0`, `0x447500`, `0x447600`, `0x447650`, `0x4476A0`,
  `0x4500B0`, `0x4502A0`: Anfang **`0x8B9038`**, Schrittweite `0xAD24`, Ende
  **`0x991708`** = `0x8B9038 + 20·44324`.
* `0x4500F0`: Anfang `0x8B9044`, Ende **`0x991714`** — dieselbe Rechnung, um
  `+0x0C` versetzt.

**Zehn Fundstellen, dieselben zwei Zahlen.**

---

### 12. Die elf »in F fehlenden« Funktionen — **Werkzeugartefakt, kein Unterschied**

Die Vollerhebung mit `cfind.py` meldete elf C-Funktionen in diesem Bereich ohne
F-Entsprechung, mit dem Verdacht, hier könne der zehnte Auslieferungsunterschied
(C 48 Fensterarten, F 47) im Code liegen. **Der Verdacht ist ausgeräumt.**

#### 12.1 Drei der elf sind gar keine Funktionsanfänge

Ein Abtast der `int3`-Polster im Bereich `0x444300…0x446000` liefert die echten
Grenzen:

```
00444740   00444A30   00444D90   004450F0
00445450   00445650   004459F0   00445D70
```

`0x444822`, `0x445214` und `0x445784` sind **keine** Anfänge — sie liegen mitten
in `0x444740`, `0x4450F0` und `0x445650`. `0x444822` etwa ist das Ziel von
`jge 0x444822` (bei `0x4447DF`) und `jmp 0x444822` (bei `0x444806`) im selben
Rumpf. `rufer.py` findet für alle drei **null** Rufer.

⚠ **Das ist die Fehlerursache:** `cfind` nimmt jeden erkannten »Anfang« zugleich
als **Ende der vorigen Funktion**. Es hat `0x444740` bei `0x444822`
abgeschnitten und **226 von 593 Byte** gegen ganze F-Funktionen gehalten
(»76 Befehle«). Kein Wunder, dass nichts passt.

#### 12.2 Die echten Funktionen haben alle eine F-Entsprechung

| C | Byte | F | Byte | Befehle C/F | Titel, in **beiden** Fassungen |
|---|---:|---|---:|---|---|
| `0x444740` | 593 | `0x443720` | 588 | 185 / 184 | **»Einsatzkarte«** |
| `0x444A30` | 682 | `0x443A00` | 672 | 205 / 204 | (Verknüpfungskarte, ohne Titel) |
| `0x444D90` | 688 | `0x443D50` | 679 | 206 / 205 | **»Luft-Einsatzplanung«** |
| `0x4450F0` | 680 | `0x4440A0` | 671 | 205 / 204 | **»Raketen-Einsatzplanung«** |
| `0x445450` | 408 | `0x4443F0` | 420 | 134 / 129 | (Markiertafel, Abschnitt 9) |
| `0x445650` | 736 | `0x444600` | 720 | 218 / 216 | **»Materialtransport Planung«** |
| `0x4459F0` | 708 | `0x444990` | 698 | 212 / 211 | **»Einheiten-Transport Planung«** |
| `0x445D70` | 194 | `0x444D00` | 201 | 70 / 74 | — |

⭐⭐ **Fünf der acht Paare tragen dieselbe, im Bild jeweils genau einmal
vorkommende Zeichenkette.** Nachgezählt mit `reloc_refs --addr`: »Einsatzkarte«
liegt in C bei `0x4FC5A0` mit **einer** Lesestelle (`0x44482A`), in F bei
`0x4FB5D8` mit **einer** (`0x4437EB`). Ein Nullmodell braucht es dafür nicht —
eine einmalige Zeichenkette kann nicht zufällig in der falschen Funktion stehen.

Dazu passen die **Befehlszahlen auf ±2 genau** und die gleiche Reihenfolge im
Bild. `cfind --diff` auf den richtigen Grenzen gibt für `0x445450` (die einzige
ohne eingestreuten Falschanfang) **92,5 %**, und die Abweichung ist reine
Registerwahl plus die bekannte `.data`-Verschiebung (C `0x4FA284` / F `0x4F928C`
für den eigenen Spieler, `+0xFF8`, genau wie in AE-2 verzeichnet; Gebäudetafel
C `0xC06915` / F `0xC05975`, `+0xFA0`).

⚠ Bei `0x444740` gegen `0x443720` sieht `--diff` einen »replace«-Block. Von Hand
nachgelesen: C prüft bei `0x4447DD` die Breite und bei `0x444802` die Höhe gegen
den Schirm — **F tut bei `0x4437BE` und `0x4437DF` genau dasselbe**, nur mit
umgekehrter Sprungbedingung und anderen Registern. **Kein Unterschied.**

#### ⭐ Nebenertrag: BA.10 Punkt 2 ist beantwortet

»Art 3 (Kartenfenster) hat keinen gefundenen Anleger« — es gibt keinen, **weil es
keinen geben kann**. `0x444740` rechnet die Fenstergrösse aus Kartengrösse und
Zoomstufe und ruft den **allgemeinen** Anleger `0x457730`:

```
zellen_breit = (zoom·dword[0x542DC4] + 19) / 20 + 2
zellen_hoch  = (zoom·dword[0x542DF8] + 19) / 20 + 2
idx = 0x457730(x, y, zellen_breit, zellen_hoch, "Einsatzkarte", flagge)
0x441270(idx)
byte[idx + 0xAD23] = zoom                 ; die Zoomstufe — bestaetigt BA.6
word[idx + 0xACA0] = 0                    ; Betriebsart 0
0x4B7ED0(puffermitte, breite, 0, zoom)    ; die Karte hineinmalen
0x441190(idx)
wenn word[+0x44] > 0 bzw. word[+0x40] > 0:  Mauszeiger dorthin setzen
                                            (dword[0xC657FC], ueber 0x502AA8/AC)
```

⭐ `zoom` ist Index in `0x4FD610 = {1, 2, 3, 0, 0, 0}` — die drei Zoomstufen aus
AP, als Zahlen. Und die Aufrundung auf **Vielfache von 20** ist derselbe Zwang,
den BA.5 als Grund für die fehlende Beschneidung nennt: **auch das
grössenveränderliche Fenster hält das 20er-Raster ein.** Die sechs Betriebsarten
aus AP haben damit ihre Öffner:

| Betriebsart | C | F |
|---:|---|---|
| 0 Einsatzkarte | `0x444740` | `0x443720` |
| 1 Verknüpfungskarte | `0x444A30` | `0x443A00` |
| 2 Luft-Einsatzplanung | `0x444D90` | `0x443D50` |
| 3 Raketen-Einsatzplanung | `0x4450F0` | `0x4440A0` |
| 4 Materialtransport Planung | `0x445650` | `0x444600` |
| 5 Einheiten-Transport Planung | `0x4459F0` | `0x444990` |

⭐⭐ **Die Zuordnung ist nicht aus der Reihenfolge geraten, sie steht als Zahl da:**
jede der sechs Funktionen schreibt ihre Betriebsart nach `word[+0xACA0]`, und die
Werte sind **0, 1, 2, 3, 4, 5 in genau dieser Adressreihenfolge** — und drei
davon (`0`, `2`, `4`, `5`) tragen zusätzlich die passende Titelzeichenkette.
Die drei Schliesser `0x447600`/`0x447650`/`0x4476A0` prüfen dasselbe Feld auf
4/2/5 und die Aufräumtafel aus Abschnitt 8 kommt unabhängig auf dieselben drei
Zahlen.

#### 12.3 `0x44D974` und `0x44DC6C` liegen in der grössten Funktion des Programms

Zwischen `0x4485D0` und `0x44E0EC` steht **kein einziges `int3`-Polster**, und im
ganzen Block gibt es **genau ein** `call`-Ziel: `0x4485D0`. Also ist
`0x4485D0 … 0x44E0EC` **eine Funktion von 23 324 Byte** — `ui_action`, in
`.wolf/memory.md` schon als »40-KB-Verteiler« geführt, F `0x4475D0`, dort
`0x4475D0 … 0x44CE4C` = 22 652 Byte, ebenfalls mit genau einem Rufziel.

`0x44DC6C` ist Ziel von fünf `jmp` (`0x44DAA1`, `0x44DAF3`, `0x44DB3D`,
`0x44DB87`, `0x44DBDD`) im selben Rumpf; `0x44D974` hat überhaupt keinen Rufer.
**Beide sind keine Funktionen.**

⚠ Damit steht auch fest, wo die schon gelesenen Anker aus AW und BF liegen:
`0x44AD8A`, `0x44AE9F`, `0x44BAF9`, `0x44BBAB`, `0x44C51B`, `0x44C5A0`,
`0x44CD6F`, `0x44DBAA` sind **allesamt Marken innerhalb dieser einen Funktion**,
keine eigenen Funktionen.

#### 12.4 ⚠ Eine Warnung zu `cfind` für dieses Revier

Alle 52 Funktionen sind in F vorhanden. Aber `cfind` **verwechselt die Öffner
untereinander**, weil 26 von ihnen dieselbe Rumpfform haben: es bildet
`0x442FB0`, `0x4437C0`, `0x443E00`, `0x446000` und `0x446190` **alle fünf** auf
F `0x442890` ab, jeweils mit »90…98 % der Rumpfform gleich«.

⭐ **Der verlässliche Weg ist der Anleger.** Jeder Öffner ruft genau einen
Anleger, und die C/F-Paare der Anleger stehen in BA.4. `rufer.py` auf den
F-Anleger liefert genau eine Rufstelle, der `int3`-Abtast in F den zugehörigen
Funktionsanfang. So sind alle 26 Zeilen von Tafel 1a gewonnen. **Sechs davon
widersprechen `cfind`**; die richtige Zuordnung ist zusätzlich durch die Wachart
in F bestätigt — F `0x442890` trägt bei `0x4428D1`
`cmp byte[ebx + 0x8B8098], 0x12` = Art 18 und gehört damit zu C `0x4438A0`, nicht
zu den fünf anderen.

---

### 13. Der Ladebalken (`0x444000`, 28 Rufer)

```
fortschritt(prozent):
    ... zwei Systemaufrufe (Ereignisschleife durchlassen) ...
    wenn prozent == 0:
        idx = anleger_Art45(0, 0)                  ; 220 x 60, Titel "Laden..."
        0x441270(idx) ; 0x4B9480()
        byte[+0x0C] = 0
        word[+0x02] = (dword[0xB136B0] - Fensterbreite) / 2      ; waagerecht mittig
        word[+0x04] = dword[0x5387CC] - 200
        0x4412E0(idx)
    sonst:
        das offene Art-45-Fenster suchen
        byte[+0x0C] = prozent ; 0x4412E0 ; 0x440F40
```

Die 28 Rufstellen geben die Stufen: **5, 7, 8, 9, 10, 11, 12** (`0x4C8CD8` ff.),
**40, 41, 42, 43, 44, 45** (`0x4C8E70` ff.), **50** (`0x4C9029`), **55**
(`0x41E366`), **85** (`0x41EF0E`), **90** (`0x41F0AD`), **100** (`0x41F42D`) —
und unmittelbar hinter der 100 steht bei `0x41F47D` der Aufruf von `0x443F70`,
»alle Art-45-Fenster schliessen«. ⭐ Die Stufen sind monoton und die
Abschlusszahl 100 wird sichtbar »verbraucht« — das stützt die Deutung
»Prozentwert« gegen »Bildnummer« ohne weitere Annahme.

---

### Berichtigungen an bestehenden Dokumenten

1. ⚠ **BA.6, Fenstersatz `+0x0C`:** dort steht »Fenstertext / Titel«. Für die
   Objektfenster (Gebäude, Einheiten) liegt an `+0x0C` ein **Wort mit der
   Kennung des Objekts**, geschrieben von den Anlegern (`0x459851` Art 20,
   `0x459E91` Art 23) und geprüft von 15 Öffnern dieses Reviers. Beides kann
   nicht gleichzeitig stimmen; die Kennung ist die belegte Lesart.
2. ⚠ **BA.10 Punkt 2** (»Art 3 hat keinen gefundenen Anleger — bekannte
   Blindheit«) kann gestrichen werden: Art 3 hat **keinen eigenen Anleger**,
   sondern benutzt den allgemeinen `0x457730` mit gerechneter Grösse
   (Abschnitt 12.2). Kein Werkzeugfehler, sondern der Bauplan.
3. ⚠ **BA.4, »20 Plätze à 44 324 Byte«** war über die 280-Byte-Lücke begründet.
   Die Zahlen `0x991708` und `0x99C42A` stehen in **zehn** Funktionen dieses
   Reviers wörtlich im Befehlsstrom. Die Begründung darf ersetzt werden.
4. ⚠ **AP** führt sechs Betriebsarten des Kartenfensters ohne Adressen. Ihre
   Öffner stehen jetzt in 12.2, mit F-Adressen.
5. ⚠ **`cfind.py`** darf für Funktionsfamilien gleicher Form nicht als Beleg
   genommen werden (12.4), und seine Funktionsgrenzen sind falsch, sobald ein
   Sprungziel als »Anfang« erkannt wurde (12.1). Beides gehört in die
   Werkzeugbeschreibung. Ein zweiter Anker (Anleger, Zeichenkette, Tafel) ist
   für dieses Revier **Pflicht**, keine Kür.
6. ⚠ Die Warnung der Einweisung zum Linearabtast bestätigt sich in neuer Form:
   `0x4471A0` verschiebt die Fensterliste mit einer **Byteschleife**, und
   `reloc_refs` sieht davon nichts. Auch ohne Blockbefehl kann ein Schreiber
   unsichtbar sein.

---

### Bauaufgaben, die daraus folgen

1. ⭐ **Fensterreihenfolge.** Ein neues Fenster kommt **hinten** in die Liste und
   wird erst durch einen zweiten Schritt nach vorn geholt; Art 1, 2 und 44 werden
   **nicht** nach vorn geholt. Wer alles nach vorn holt, bekommt eine Statuszeile
   über dem Bauschirm.
2. ⭐ **Doppelöffnungssperre.** Objektfenster gibt es je Objekt einmal
   (`Art + Kennung`), Einzelfenster einmal überhaupt. Ein zweiter Klick tut
   nichts — ausser beim Generator (Abschnitt 5).
3. ⭐⭐ **Auf- und Zublende.** Fenster gehen über **4 Bilder** auf (mit Klang 307)
   und über **6 Bilder** zu, indem der eigene Punktpuffer zur Mittelzeile
   zusammengeklappt und der Rand auf `0xFF` gesetzt wird. Ohne das erscheinen und
   verschwinden unsere Fenster hart.
4. ⭐⭐ **Meldungsfenster verschwinden von selbst.** `byte[+0xAD22]` zählt alle
   **20 Takte** herunter; bei 0 schliesst sich das Fenster. Die Standzeit ist ein
   Argument des Rufers (`0x4469A0`, 44 Rufstellen).
5. **Die Kartenfenstergrösse** ist `(zoom·Kartenmass + 19)/20 + 2` **Zellen**,
   also stets ein Vielfaches von 20 Punkten. Wer sie frei rechnet, verletzt das
   Raster aus BA.5.
6. **Die Gebäudeart bestimmt das Fenster** über zwei Tafeln (`0x4379F0` zum
   Öffnen, `0x4FDE08` zum Schliessen). Beide gehören übernommen — samt der
   Entscheidung, ob wir die drei kaputten Einträge von `0x4FDE08` nachbauen oder
   berichtigen (Gefecht darf abweichen, die Kampagne nicht).
7. **Der Ladebalken** (Art 45, »Laden…«, 220×60, waagerecht mittig,
   `Bildhöhe − 200`) mit den 19 belegten Stufen fehlt bei uns.
8. **Die Markiertafel `0xB133A0`** (Abschnitt 9) mit ihren vier Werten steuert,
   welche Gebäude beim Materialtransport überhaupt anklickbar sind.
9. ⚠ **Art 28 »Stromversorgung« nicht nachbauen** — im Original unerreichbar.

---

### Was ungedeutet bleibt

1. ⚠ **Fensterart 7** (600×340, Öffner `0x4460E0`, Zeichner `0x46C490`) — nicht
   nachgesehen, welches Gebäude sie zeigt; ihr Rufer `0x44A7B8` liegt in
   `ui_action`. Ebenso Art 21 (`0x442ED0`, Gebäudeart 13) und der Öffner für
   Gebäudeart 17 (`0x443CF0`), beide knapp ausserhalb meines Reviers.
2. ⚠ **Die Ruferzahlen der Revierliste** (meist »2«) zählen den Stummel mit;
   `rufer.py` mit aufgelösten Stummeln findet oft nur einen. Die Differenz habe
   ich nicht Zeile für Zeile geprüft.
3. ⚠ **`word[0x87B050]`**, das zweite Argument von `0x44FC90` — ungedeutet.
4. ⚠ **Die zwei Sonderarme des Fenstertakts** (`0x446CD0` und `0x4AA950` in
   `0x4505F0`): welche Fensterarten sie betreffen, habe ich aus der Indextafel
   `0x45070C` nicht ausgelesen.
5. ⚠ **`0x4500F0`** (»wartet ein Fenster auf Eingabe?«) hat eine eigene Arttafel
   bei `0x450174` / `0x45015C` (Arten 7…37), die ich nicht aufgelöst habe.
6. ⚠ **Warum `0x445450` bei `modus == 0` keine Verträglichkeit prüft** — der Code
   ist eindeutig, die Absicht nicht (Abschnitt 9).
7. ⚠ **`0x4477B0`** ist als »Mausklick → Kartenzelle« gelesen (Umkehrrechnung des
   Kartenmalers, Suchfenster von `−4` bis `+4` in beiden Richtungen), aber wonach
   die 81 Zellen durchsucht werden, habe ich nicht zu Ende verfolgt.
8. ⚠ **Die Wirkung der drei kaputten `0x4FDE08`-Einträge ist gefolgert, nicht
   gemessen.** Für einen Beleg müsste man im Original ein Nachschubposten- oder
   Generatorfenster offen lassen und das Gebäude zerstören.
9. ⚠ **Der eigentliche Zeichendurchlauf** über die Fensterliste liegt nicht in
   diesem Revier. Ausserhalb von `0x441000…0x450700` liest nur `0x413ED4` /
   `0x413EDA` / `0x413FDA` die Liste `0x87AFF8`, und das ist die
   **Tastaturschleife**. Wo die Fenster der Reihe nach auf den Schirm kommen,
   habe ich nicht gefunden.

---

## BN. Revier 6: 0x450790 … 0x4AF000

52 Funktionen, 10 928 Byte. Das Revier ist **weit gestreut** und zerfällt in
**dreizehn** getrennte Mechaniken. Alle 52 F-Entsprechungen sind mit
`cfind.py` gefunden und, wo das Werkzeug »mehrdeutig« meldete, **von Hand an
einer unterscheidenden Konstanten nachgeprüft**.

⚠ **Eine F-Zuordnung von `cfind.py` ist falsch** und hier berichtigt:
`0x454200` → **`0x452EB0`** (nicht `0x452F30`, das ist die Entsprechung von
`0x454280`). Siehe *Berichtigungen*.

---

### Adresstafel

| C | F | Byte | Rufer | Was sie ist |
|---|---|---:|---:|---|
| `0x450790` | `0x44F440` | 64 | 2 | **Zufalls-Fahrwerk** für den Zufallsentwurf (10 Möglichkeiten) |
| `0x4507D0` | `0x44F480` | 128 | 2 | **Zufalls-Waffe/Ausrüstung** für den Zufallsentwurf (34 Möglichkeiten) |
| `0x450850` | `0x44F500` | 80 | 4 | Sonderzustand `byte[0x4F6F9C]` verlassen, eigener Spieler := 0, Leiste neu (⚠ Zustand ungedeutet) |
| `0x4510F0` | `0x44FDA0` | 112 | 2 | ist ein Fenster **Art 0x21 (Geschäftszentrum)** mit Unterart `n` offen? |
| `0x451160` | `0x44FE10` | 112 | 2 | ist ein Fenster **Art 0x1F (Nachschubposten)** mit Unterart `n` offen? |
| `0x451310` | `0x44FFC0` | 96 | 14 | Fenster **Art 0x25 (Gefechts-Einrichtung)** neu durchlaufen lassen |
| `0x451370` | `0x450020` | 128 | 2 | Fenster **Art 0x21** mit Unterart `n` neu durchlaufen lassen |
| `0x4513F0` | `0x4500A0` | 320 | 2 | Flugzeug-Feld `+0x2C` weiterschalten — einzeln oder für den ganzen Verband am Flughafen |
| `0x451600` | `0x4502B0` | 80 | 2 | **Anzahl offener Fenster** zählen (0…20) |
| `0x451650` | `0x450300` | 304 | 9 | ⭐ **» Cheater« an den Spielernamen anhängen** |
| `0x453690` | `0x452340` | 48 | 2 | **Geschosse takten** — alle 1000 Sätze von sec43 |
| `0x453990` | `0x452640` | 272 | 4 | **Entfernung Einheit → Feld** (Feinraster 40/Zelle, `fsqrt`) |
| `0x454200` | **`0x452EB0`** | 128 | 7 | **Höchstreichweite** einer Einheit (`+0x2B`·40; Schiffe fest 640) |
| `0x454280` | `0x452F30` | 128 | 7 | **Mindestreichweite** einer Einheit (`+0x2A`·40) |
| `0x454370` | `0x453020` | 80 | 4 | ist Ziel-**Einheit** in Reichweite? (ohne Sichtprüfung) |
| `0x454490` | `0x453140` | 96 | 6 | ist Ziel-**Feld** in Reichweite? |
| `0x454560` | `0x453210` | 160 | 3 | ⭐ **Druckwellenring anlegen** (sec88, 50 Plätze) + Klang 510…518 |
| `0x454AE0` | `0x453790` | 528 | 2 | ⭐ **Leuchtspur zeichnen** — sec42-Teilchen entlang der Schusslinie streuen |
| `0x4551A0` | `0x453E40` | 272 | 2 | ⭐ **Leuchtspur anmelden** (200 Sätze zu 40 B ab `0x88C450`, Lebensdauer 2) |
| `0x4552B0` | `0x453F50` | 112 | 2 | Leuchtspurenliste je Bild abarbeiten und altern |
| `0x4585A0` | `0x457240` | 160 | 2 | ⭐ **CD-Spieler: Stück zu Ende** (`MM_MCINOTIFY`) → nächstes/zufälliges Stück |
| `0x458640` | `0x4572E0` | 144 | 3 | ⭐ **CD-Spieler: Wiedergabe starten** |
| `0x45BB00` | `0x45A7A0` | 272 | 8 | **Bildblock aus einem Fenstersatz in die Zeichenfläche kopieren** |
| `0x463CE0` | `0x462590` | 128 | 3 | ⭐ **Fensterplatz freigeben** (`free` + 44 324 B nullen) |
| `0x4649F0` | `0x4632E0` | 48 | 10 | ⭐ **Rangzeichen aus der Erfahrung** (`0xB4`…`0xBB`) |
| `0x479070` | `0x477960` | 48 | 26 | ⭐ **Prozent rechnen**: `100·a/b`, `b == 0 → 0` |
| `0x484BB0` | `0x483280` | 80 | 11 | Zeichenkette auf ein Restbudget kürzen (Endefenster) |
| `0x487A10` | `0x4860D0` | 352 | 1 | ⭐ **Tastenzeichen an das Eingabefeld des Fensters** (6 Fensterarten) |
| `0x487B70` | `0x486230` | 80 | 7 | **Fenster verschieben** (`+0x02` = x, `+0x04` = y) |
| `0x487BC0` | `0x486280` | 64 | 1 | Zahl dezimal wandeln und an eine Ausgabe hängen |
| `0x4A9590` | `0x4A8EC0` | 240 | 3 | ⭐ **Bodenspur anlegen** (sec39, Gruppe × 40 Plätze) |
| `0x4A9680` | `0x4A8FB0` | 192 | 2 | ⭐ **Punkt an eine bestehende Bodenspur anhängen** |
| `0x4A9790` | `0x4A90C0` | 48 | 2 | ⭐ **Bodenspuren altern** — 20 000 Sätze, `+4` je Takt −1 |
| `0x4A98A0` | `0x4A91D0` | 464 | 9 | ⭐ **Krater anlegen** (sec45) — der bisher fehlende Erzeuger |
| `0x4A9CC0` | `0x4A95F0` | 304 | 6 | ⭐ **Mauszeiger löschen** (64×64-Hintergrund zurückschreiben) |
| `0x4A9F90` | `0x4A98C0` | 592 | 2 | ⭐ **Mauszeiger zeichnen** (Hintergrund sichern, Bildlauf, Blit) |
| `0x4AA340` | `0x4A9C70` | 32 | 3 | **Tastenzustandsfeld nullen** (256 B ab `0xA182E8`) |
| `0x4AC2F0` | `0x4ABC20` | 352 | 3 | **Sprite-Blitter, 8-stufige Spielerfarbe** |
| `0x4ACA00` | `0x4AC330` | 400 | 2 | **Sprite-Blitter, einfarbig** (Silhouette) |
| `0x4ACDE0` | `0x4AC710` | 208 | 4 | ⭐⭐ **Neigungsschlüssel einer Zelle** — vierstellige Dezimalzahl der Eckhöhen |
| `0x4ACEB0` | `0x4AC7E0` | 272 | 6 | ⭐ **Geländekachel für eine Zelle wählen und setzen** |
| `0x4ACFC0` | `0x4AC8F0` | 48 | 6 | Ecke in die Glättungs-Warteschlange legen |
| `0x4ACFF0` | `0x4AC920` | 112 | 2 | nach der Glättung die vier Nachbarzellen jeder Ecke neu bekacheln |
| `0x4AD060` | `0x4AC990` | 320 | 2 | ⭐ **Höhenglättung** — Höhenunterschied benachbarter Ecken auf ≤ 1 zwingen |
| `0x4AD1A0` | `0x4ACAD0` | 160 | 2 | **Gelände verändern**: setzen → Ecken einreihen → glätten → neu bekacheln |
| `0x4AD240` | `0x4ACB70` | 336 | 1 | wie `0x4ACEB0`, gibt die Kachelnummer **zurück** statt sie zu setzen (kein Rufer auffindbar) |
| `0x4AD390` | `0x4ACCC0` | 352 | 1 | ⭐ **`EnumDisplayModes`-Rückruf** — bis 64 Bildschirmarten ab `0xA509D8` |
| `0x4AD4F0` | `0x4ACE20` | 48 | 3 | `IDirectDraw::EnumDisplayModes` anstossen |
| `0x4AD7B0` | `0x4AD0E0` | 256 | 10 | ⭐ **Trümmerstück streuen** — Art 1 = `fly_part`, Art 2 = sec42-Bild 240…242 |
| `0x4AEBA0` | `0x4AE4D0` | 480 | 2 | **kleine Einheit zerlegen** (2 Durchgänge) |
| `0x4AED80` | `0x4AE6B0` | 640 | 2 | **grosse Einheit zerlegen** (10 Durchgänge) |
| `0x4AF000` | `0x4AE930` | 448 | 2 | **Flugzeug zerlegen** (sec19-Satz `0x6DDF70`) |

---

### 1. ⭐⭐ Die Bauteiltafel gibt es **acht Mal — einmal je Spieler**

Das ist der tragendste Fund des Reviers, und er fällt beim Lesen von zwei
64-Byte-Funktionen ab.

`0x450790` und `0x4507D0` lesen beide nach demselben Muster:

```
al = byte[ 58·(rand()%N + 200·spieler) + SOCKEL ]
```

Die **200** kommt aus der `lea`-Kette (`5·p → 25·p → rem + 8·25p`), die **58**
aus `edx·2` mit `edx = 29·x` — beides gerechnet, bevor irgendwo nachgeschlagen
wurde. `spieler` ist `byte[0x4FA284]`, der eigene Spieler; die Rufer
(`0x448B44`, `0x448B5E`) schieben ihn wörtlich hinein.

58 ist die bekannte Schrittweite der Bauteiltafel (`UNIT_STATS_RE.md`,
Index-0-Sockel `0x5045BA`). **200 Sätze × 58 Byte = 11 600 Byte je Spieler.**

**Die Probe (Nullmodell mitgeliefert):**

| Prüfung | Ergebnis |
|---|---|
| Sockel + 8·11 600 | `0x5045A0 + 0x16A40 = 0x51AFE0` — die nächste bekannte Tafel (Flugzeugvorlagen) fängt bei `0x51B021` an, 65 Byte später. Es ist also **kein** Platz für einen neunten Block |
| Spieler 0 gegen 1…6 | je **2613 von 11 600** Byte verschieden, **dieselbe Zahl für alle sechs** |
| Spieler 1…6 untereinander | **0 verschieden** |
| Spieler 7 gegen 1…6 | **16** verschieden |
| Die abweichenden Sätze | genau **0…19, 64…88, 99…107, 119…123, 139** — das sind exakt die **benannten** Zeilen (Waffen, Ausrüstung, Robotertechnik), nie eine leere |
| C gegen F, ganze 92 800 Byte | **0 verschieden** (F-Sockel `0x5035E0`, aus dem F-Code abgelesen: `0x505A67`/`0x5044A7`/`0x5032C1`, Δ durchgehend `0xFC0`) |

**Nullmodell:** wären die 8 Blöcke Zufall, müsste die Zahl der Unterschiede
zwischen p0 und p1…p6 schwanken. Sie ist sechs Mal dieselbe, und die
abweichenden Sätze liegen ausnahmslos auf benannten Zeilen. Für eine
zufällige Blockstruktur wäre das nicht zu erwarten.

⭐ **Damit ist das Rätsel aus `UNIT_STATS_RE.md` §1 (»kein
`[base + unit_type*58]`-Zugriff im `.text` auffindbar«) gelöst:** der Zugriff
existiert, er ist nur **zweidimensional** — `[base + 58·(200·spieler + zeile)]`.
Genau deshalb hat die Suche nach `imul …,58` nichts gefunden: der Übersetzer
hat 58 und 200 zu einer einzigen `lea`-Kette verschmolzen.

Das erklärt auch die Zeichenketten, die unmittelbar vor dieser Tafel stehen:
`Wrong next_rand in upgrade chassis`, `upgrade vyv:`, `upgrade enemy:`,
`Too many researches` — **die Forschung ändert die Bauteilwerte je Spieler**,
und dafür braucht jeder Spieler seine eigene Kopie.

#### 1.1 Was die zwei Funktionen tun

Mit der bekannten Regel »`+0x2D` eines Satzes ist die Bauteilnummer des
**nächsten**« (`UNIT_STATS_RE.md` §2.1) lesen sich beide sofort:

```
zufalls_fahrwerk(spieler):                       # 0x450790
    r = rand() % 10
    return statstafel[spieler][160 + r].feld2D   # = Bauteil von Zeile 161+r

zufalls_waffe_oder_ausruestung(spieler):         # 0x4507D0
    r = rand() % 34
    wenn r < 15:  return statstafel[spieler][64 + r].feld2D   # Zeile 65…79
    sonst:        return statstafel[spieler][r - 15].feld2D   # Zeile  1…19
```

Nachgeschlagen (das ist die Bestätigung, nicht der Beleg):

* Zeilen 161…170 = **Reifen, Panzerreifen, 6×6 Reifen, Ketten, Schwere Ketten,
  Luftkissen, Schneegleiter, Kugelroller, Wüstenwiesel, Schw. Wüstenwiesel** —
  genau die zehn normalen Fahrwerke. **Nicht** dabei: Spinne, Abwehrstellung,
  Schwerer Blocker, Stahlsucher, Läufer, Schweber.
* Zeilen 65…79 = **Teleporter … Zielfokus**, 15 Ausrüstungen.
* Zeilen 1…19 = **Leichte Bordkanone … Membranbombe**, 19 Waffen.

15 + 19 = 34 = der Divisor. Die Aufteilung geht **lückenlos** auf.

**Der Rufer** (`0x448B32`/`0x448B8B`, Befehl **25**): das Fenster schickt
Befehl 25 mit `+0` = angewählte Einheit `word[0x4FA0C8]`, `+2` = 1,
`+4` = Zufallsfahrwerk, `+6` = Zufallswaffe/-ausrüstung. Eine zweite Stelle
schickt Befehl 25 mit `+2` = 0 und **ohne** Bauteile.

⚠ Ungedeutet bleibt, welcher Knopf Befehl 25 auslöst; das Fenster
(`0x4487xx`…`0x448Cxx`, Sprungtafel `0x44DD64`) liegt ausserhalb des Reviers.

---

### 2. ⭐ Der Cheat-Pranger: » Cheater« am Namen

`0x451650` (F `0x450300`), **9 Rufer**, alle in `0x413xxx` — dem Tastenfeld.

```
spieler_als_cheater_markieren():
    kopiere 9 Byte von 0x4FE9D0 (" Cheater") in einen Zwischenspeicher
    wenn strlen(0x87B141) > 12: raus                  # kein Platz
    n = Stelle des ersten Nullbytes in 0x87B141 (max 20)
    wenn n > 7 und die 7 Zeichen davor "Cheater" sind: raus   # schon dran
    strcat(0x87B141, " Cheater")
```

Die sieben Einzelvergleiche stehen ausgeschrieben im Code:
`0x43 0x68 0x65 0x61 0x74 0x65 0x72` = `C h e a t e r`. Die angehängte Kette
bei `0x4FE9D0` ist wörtlich `' Cheater'` (mit führendem Leerzeichen).
`0x87B141` ist der 20 Zeichen lange Spielername (dorthin kopiert `0x44D096`).

**Die neun Aufrufstellen sind alle gleich gebaut:**

```
wenn byte[0xA182F9] == 0 raus        # Taste W nicht gedrückt (an 6 von 9 Stellen)
wenn byte[0xA182F8] == 0 raus        # Taste Q nicht gedrückt
wenn byte[0x4FA0C4] == 0 raus        # »Developers' cheats enabled«
spieler_als_cheater_markieren()
<der Cheat>
```

⭐ **`0xA182E8` ist das Tastenzustandsfeld** (schon belegt, Abschnitt AV).
`0xA182F8 − 0xA182E8 = 0x10`, `0xA182F9 = 0x11` — Abtastcodes **Q** und **W**.
Der bestehende Bericht nennt als abgefragte Tasten »A E H Q R S T W Z«;
**Q und W stehen also unabhängig schon in der Liste.** Die Cheats laufen
folglich über **Q + W + Taste**.

`0x4FA0C4` hat genau **einen** Schreiber, `0x43AF78` (`:= 1`) — und
`0x43AF5A` ist die im Bericht bereits vermerkte Stelle
»Developers' cheats enabled«. Damit ist die Kette geschlossen.

Die neun Cheat-Rümpfe (ausserhalb des Reviers, der Vollständigkeit halber):
`0x43ADE0`, `0x43AB70`, `0x43AC60`, `0x4CFC10`, `0x43AB00`, `0x43AD70`,
`0x43AD00`, und einer inline ab `0x4139BD`.

---

### 3. Die Fensterverwaltung: sechs kleine Werkzeuge

Alle rechnen mit der bekannten Schrittweite **44 324** je Fenstersatz
(Sockel `0x8B9038`, 20 Plätze) — jedes Mal aus der `lea`-Kette gerechnet
(`9c → 81c → 82c → 246c → 985c → 4925c → 44325c → −c`), nicht nachgeschlagen.

| Funktion | Ablauf |
|---|---|
| `0x4510F0` | `for k in 0..19: wenn Art[k] == 0x21 und Unterart[k] == n: return 1` → **1/0** |
| `0x451160` | dasselbe für Art `0x1F` |
| `0x451310` | `for k: wenn Art[k] == 0x25: fensterverteiler(k)` |
| `0x451370` | `for k: wenn Art[k] == 0x21 und Unterart[k] == n: fensterverteiler(k)` |
| `0x451600` | `zaehle k mit Art[k] != 0` → **Anzahl offener Fenster** |
| `0x463CE0` | `wenn Art[f] == 0: return 0xFFFF; free(dword[f+0xAC9C]); 44324 B nullen; Art := 0; return f` |

Die Arten stammen aus `FENSTER_RE.md`: **0x21 = Geschäftszentrum**,
**0x1F = Angebot des Nachschubpostens**, **0x25 = Akte Europa /
Techstandard / Konto / Rohstoffe** (die Gefechts-Einrichtung).
Dass `0x451310` **14 Rufer** hat und dreizehn davon in `0x4C4xxx` liegen —
dem Netzwerk-/Lobbymodul — passt genau: jede Änderung in der Lobby lässt das
Einrichtungsfenster neu durchlaufen.

`0x463CE0` ist das **Gegenstück** zum schon gelesenen Öffner (Abschnitt AL,
»freien Platz suchen, 44 324 Byte nullen mit `rep stosd`, `ecx = 0x2B49`«):
hier steht dieselbe Zahl `0x2B49` = 11 081 Dwords = 44 324 Byte.
Neu dabei: **jeder Fenstersatz hat bei `+0xAC9C` (`0x8C3CD4`) einen
`malloc`-Zeiger, der beim Schliessen freigegeben wird.**

#### 3.1 ⭐ `0x487A10` — die Tasteneingabe der Fenster

Ein Rufer: `0x413F36`, das Tastenfeld. Der Ablauf:

```
art = Art[fenster];  i = art - 7
wenn i > 30: return 0
zweig = byte[0x487B08 + i]                # 31-Byte-Umsetzer, 7 Zweige
```

Der Umsetzer verteilt auf sieben Zweige, und nur **sechs Fensterarten** haben
überhaupt ein Eingabefeld:

| Art | Fenster | Regel für das Zeichen `byte[fenster + 0xAD20]` |
|---:|---|---|
| 7 | Erstellung (Entwurfsname) | jedes Zeichen; `0x0D` (Eingabetaste) → **2** |
| 15 | Spiel speichern | wie 7, aber nur wenn `word[fenster+0x0C] != 0` |
| 24 | Lokator | wie 15 |
| 25 | Gruppieren | wie 15 |
| 36 | `0x47CD60` (unbenannt) | wie 7 |
| 37 | Gefechts-Einrichtung (**Spielername**) | ⭐ nur `0x20 … 0x7A`, und **`[`, `]`, `^` sind verboten** |
| sonst | — | 0 |

Rückgabe **0** = Zeichen verworfen, **1** = angenommen, **2** = Eingabetaste.
Bei 1 und 2 wird das Fenster sofort neu durchlaufen (`0x487630`).

⭐ Das Feld `+0xAD20` ist damit **das letzte Feld des 44 324-Byte-Satzes**
(`0xAD24` gross) und hält das eben getippte Zeichen.

`0x487B70` setzt `+0x02`/`+0x04` eines Fenstersatzes — zusammen mit dem
Verteiler, der `+0x06`/`+0x08` als Puffermasse liest (`FENSTER_RE.md`), ist
der Kopf des Satzes damit: **`+0x00` Art, `+0x02` x, `+0x04` y, `+0x06`
Breite, `+0x08` Höhe, `+0x0C` ein Wort mit der Bedeutung »Feld belegt«**.

#### 3.2 `0x45BB00` — Bildblock aus dem Fenstersatz zeichnen

Jeder Fenstersatz hat bei **`+0xA3CC` (`0x8C3404`)** eine Reihe von
8-Byte-Beschreibern `{Breite:Wort, Höhe:Wort, Zeiger:Dword}`.
`0x45BB00(fenster, zeilenschritt, x, y0, basis, k)` kopiert `Höhe` Zeilen zu
`Breite` Byte aus dem Zeiger in die Zeichenfläche.
Alle **8 Rufer** liegen in `0x4864E4 … 0x486CE9`, also **innerhalb von
Fensterart 43** (`0x486480`, Beschriftungen `SYMBOL.DAT`, `BRIEFG.TXT`,
`MAP.DAT`, `ENCYCLOG.PIC`) — dem Fenster, das geladene Rohbilder anzeigt.

---

### 4. ⭐ Der CD-Spieler

Zwei Funktionen, und die MCI-Aufrufe darunter benennen sich selbst.
`dword[0xBDEA6C]` ist die MCI-Gerätenummer, `dword[0xC65880]` ist
`mciSendCommandA`.

| Hilfsroutine | MCI |
|---|---|
| `0x4D51A0` | `MCI_STATUS` (`0x814`), Posten **3** = `NUMBER_OF_TRACKS` |
| `0x4D5100` | `MCI_STATUS` (`0x814`), Posten **7** = `MEDIA_PRESENT` |
| `0x4D5000` | `MCI_PLAY` (`0x80D`) mit `MCI_FROM` (`0x400`) |

```
cd_starten(geraet, stueck):                       # 0x458640, F 0x4572E0
    dword[0x8934C0] = geraet
    wenn stueck != 0: byte[0x500E08] = stueck
    n = anzahl_stuecke()
    wenn n >= byte[0x500E08] und byte[0x500E08] != 0: spielen(geraet, byte[0x500E08])
    sonst wenn medium_da(): byte[0x500E08] = 1; spielen(geraet, 1)

cd_stueck_zuende(geraet, code):                   # 0x4585A0, F 0x457240
    wenn code != 1 oder geraet != dword[0xBDEA6C]: raus
    betriebsart = byte[0x500E0C]
      == 1: n = anzahl_stuecke(); wenn n > jetzt: jetzt+1  sonst 1     # ALLE
      == 2: jetzt = rand() % anzahl_stuecke() + 1                       # ZUFALL
      sonst: jetzt bleibt                                               # EINEN
    byte[0x500E08] = jetzt;  spielen(geraet, jetzt)
```

**Beleg für die Betriebsarten:** `reloc_refs.py --addr 0x500E0C` findet genau
fünf Fundstellen — **zwei Leser** (beide hier) und **drei Schreiber**:
`0x472DA8 := 0`, `0x472DB9 := 1`, `0x472DCA := 2`. Alle drei liegen in
`0x472D40` = **Fensterart 12, dem CD-Spieler-Fenster**
(»CD-Spieler – ANHALTEN – SPIELEN – EINEN – ALLE«). Die Knöpfe schreiben
die drei Werte. ⚠ Wert 2 (Zufall) hat im Fenster keine eigene Beschriftung —
es gibt ihn aber.

`byte[0x500E08]` ist das laufende Stück, Anfangswert im `.data` = **1**.
`0x4585A0` ist der Rumpf hinter der `MM_MCINOTIFY`-Nachricht; sein Rufer ist
`0x41452B` in der Fensternachrichtenschleife.

---

### 5. Waffenwirkung: Reichweite, Druckwelle, Leuchtspur

Das Revierstück `0x453690 … 0x4552B0` gehört ganz in das schon bekannte
**Waffen- und Wirkungsmodul** (Abschnitt BA, unterhalb `0x455B00`).

#### 5.1 Die Reichweitenprüfung — fünf Funktionen, eine Mechanik

```
hoechstreichweite(u):            # 0x454200 / F 0x452EB0
    wenn u >= 8000: return 0
    nach Gattung byte[u+0x0A]:
        0,1,5 -> byte[u+0x2B] · 40
        4     -> 640                       # Schiffe, fest = 16 Zellen
        2,3   -> 0

mindestreichweite(u):            # 0x454280 / F 0x452F30
    wenn u >= 8000: return 30000
    nach Gattung:
        0,1,4,5 -> byte[u+0x2A] · 40
        2       -> 30000
        3       -> 0

in_reichweite_einheit(u, ziel):  # 0x454370
    d = entfernung(u, ziel)
    return hoechstreichweite(u) >= d  und  mindestreichweite(u) <= d

in_reichweite_feld(u, x, y):     # 0x454490   (dieselbe Prüfung, Feldentfernung)
```

Die Werte `0` bzw. `30000` für einen ungültigen Index sind **komplementär**:
Höchstreichweite 0 **und** Mindestreichweite 30 000 heisst »kann nie
schiessen«. Das ist die Probe darauf, dass die Zuordnung richtig herum ist.

⭐ **`+0x2A` ist die Mindestreichweite** — ein Feld, das in `ENTITY_FELDER.md`
noch fehlt (dort steht nur `+0x2B range`). Es wird ausschliesslich von
`0x454280` gelesen und ausschliesslich gegen die Entfernung geprüft.

`0x453990` liefert die Entfernung Einheit → Feld: sie holt über `0x435BD0`
die **Feinstellung innerhalb der Zelle** aus `KOLIK` (`+0x06`) und `POHYB`
(`+0x04`), rechnet `dx = (zielX − RX)·40 − feinX`, `dy` entsprechend
(`feinY = 20 − wert`), und gibt `sqrt(dx²+dy²)` zurück. **40 Unterschritte
je Zelle in x, 20 in y** — das ist die isometrische Halbierung und deckt
sich mit dem, was Abschnitt BA für `0x453AA0` schon festhält.

#### 5.2 ⭐ Der Druckwellenring (`0x454560`)

```
druckwelle_anlegen(x, y):
    suche k in 0..49 mit byte[0x88E390 + 4k] == 0        # 50 Plätze, sec88
    wenn keiner frei: raus
    0x4222C0(x, y, 8, 150)
    klang = 510 + rand() % 9
    0x435950(x, y, 0, 0, klang)                          # Wirkungsbilder streuen
    byte[0x88E391 + 4k] = x
    byte[0x88E392 + 4k] = y
    byte[0x88E390 + 4k] = 1                              # Radius := 1
```

Der Verbraucher ist `0x454600` (schon in Abschnitt BA gelesen): er läuft nur
jeden dritten Takt, geht dieselbe Liste durch, sucht alle Zellen im Fenster
±6, deren `sqrt(dx²+dy²)` genau dem gespeicherten Wert gleicht, und erhöht
ihn. Damit ist **`+0` gleichzeitig Belegtmerker und Radius** (`dec dl` in
`0x454632`), **`+1`/`+2`** sind Spalte/Zeile und **`+3`** wird nach
`byte[0x87D6A8]` kopiert.

⚠ **`+3` wird von niemandem geschrieben.** `reloc_refs.py --range 0x88E390
0xC8` findet 11 Zeiger ins Fenster, davon **4 Schreiber** — `+0`, `+1`, `+2`
(alle hier) und der Schleifenanfang in `0x454600`. Kein einziger schreibt
`+3`. Der Wert kann also nur aus dem Spielstand kommen (sec88 wird
gespeichert) und ist in einem frischen Spiel **0**. Das sieht nach einem
Fehler des Originals aus, wird hier aber nur festgestellt, nicht gedeutet.

Rufer: `0x40B77B` (Einheitentod, Fall 8) und `0x454510`.

#### 5.3 ⭐ Die Leuchtspur (`0x4551A0` / `0x4552B0` / `0x454AE0`)

Eine bisher nicht beschriebene Tafel: **`0x88C450`, 200 Sätze zu 40 Byte**.
Die Grenze fällt exakt mit dem Anfang der Druckwellenliste zusammen:
`0x88C450 + 200·40 = 0x88E390`. Belegtmerker ist `+0x25`.

```
leuchtspur_anmelden(x1,y1,fx1,fy1,z1, einheit, x2,y2,fx2,fy2, z2):   # 0x4551A0
    suche k mit byte[0x88C475 + 40k] == 0
    schreibe die zehn Werte nach +0x00 … +0x24
    byte[+0x25] = 2                                    # zwei Bilder lang
    byte[+0x26] = 100 + [0,2,1,3][ byte[einheit + 0x03] & 3 ]   # OT_HLAV = Rohrdrehung

leuchtspuren_zeichnen():                                            # 0x4552B0
    for k in 0..199:  wenn byte[+0x25] != 0:
        leuchtspur_zeichnen(die 11 Felder);  byte[+0x25] -= 1
```

`0x454AE0` ist der Zeichner. Er rechnet die beiden Endpunkte in das
Feinraster um — **`x·40 + fx`, `y·20 + fy`** — nimmt die Länge über
`fild/fsqrt`, bricht bei Länge ≤ 6 ab und streut dann Länge−6 Mal ein
sec42-Teilchen (`0x435A40`) entlang der Linie, jedes mit `rand()&3`
Zufallsversatz an drei Stellen. Die Bildnummer ist das oben gesetzte
`100 … 103` — **vier Spurbilder, gewählt nach der Rohrdrehung**.

Rufer von `0x4551A0`: genau einer, `0x451CCD` — **innerhalb von `0x451B40`,
das im Bericht schon `Add missile` heisst**. Rufer von `0x4552B0`: genau
einer, `0x416621` in der Hauptschleife. Damit ist die Kette vollständig:
Schuss anlegen → Spur anmelden → zwei Bilder lang zeichnen → verfallen.

#### 5.4 `0x453690` — der Geschosstakt

```
geschosse_takten():
    for i in 0..999:
        wenn byte[0x884736 + 32i] != 0xFF: 0x452190(i)
```

`0x884730` ist sec43 (Geschosse, 32 × 1000), `+0x06 == 0xFF` ist der freie
Platz — beides schon belegt. Die Schleifengrenze `0x88C436` ergibt
`(0x88C436 − 0x884736)/32 = 1000`, gerechnet, nicht nachgeschlagen.
Einziger Rufer: `0x416591`, die Hauptschleife.

---

### 6. ⭐ Die Krater (sec45) — der fehlende Erzeuger

Abschnitt AV hält fest: »`craters` beeinflussen die Simulation nicht: ausser
**Anlegen**, Altern, Speichern und zwei Zeichnern gibt es keinen Leser« —
aber nur das *Altern* hat dort eine Adresse (`0x4A9A70`).
**Der Erzeuger ist `0x4A98A0` (F `0x4A91D0`), 9 Rufer.**

```
krater_anlegen(spalte, zeile, a2, a3, art):
    zelle = 256·spalte + zeile
    wenn word[0xBDEA80 + 2·zelle] != 0xFFFE: raus       # Zelle nicht frei
    wenn byte[0x542E18 + zelle]   != 0:      raus
    wenn gelaende(spalte, zeile) > word[0xB97B38] + 10000: raus
    suche k mit byte[0x9C994D + 6k] == 0                 # Alter == 0 heisst frei
    wenn keiner frei: raus
    g = gelaende(spalte, zeile)
    wenn g < 10000:
        h = eckhoehe(spalte, zeile)
        wenn h == 0 oder eine der vier Ecken != h: raus  # nur auf EBENEM Boden
        bild = 2·(h + 4·(art + 2·(rand()%8))) − 2
    sonst:
        bild = 2·(byte[0xBAB2A8 + g] + 4·(art + 2·(rand()%8)))
    Satz: +0 = spalte, +1 = zeile, +2 = a2, +3 = a3, +4 = bild
          +5 = rand() % 80 + 5                           # das ALTER
```

**Die Probe:** Abschnitt AV sagt unabhängig »Alter beginnt bei `rand()%80 + 5`«.
Genau diese Rechnung steht bei `0x4A99F7` (`idiv 0x50`, `add dl, 5`). Und die
Schrittweite: `0x9CB0BD − 0x9C994D = 6000`, `/6 = 1000` Plätze — die Tafel
endet auf dem ersten Byte von sec39 (`0x9CB0B8`). Beides gerechnet.

Neu belegt sind damit zwei Dinge:

* **Ein Krater entsteht nur auf einer völlig ebenen, freien Zelle.** Alle vier
  Eckhöhen müssen gleich und ungleich 0 sein.
* **`+5` ist Alter *und* Belegtmerker**, `+0…+4` bleiben beim Freigeben stehen
  — das erklärt die im Bericht erwähnten »7470 belegten Sätze« der alten
  Zählung als Müll.

Die 9 Rufer: `0x4548D9`, `0x454928` (im Druckwellenring `0x454600`),
`0x45554E`, `0x45559C`, `0x4555D5`, `0x455613`, `0x45564D` (in
`kresli_laser1` `0x4554A0`) und `0x4AE21A`. **Krater kommen also von
Explosionen und von Lasereinschlägen.**

---

### 7. ⭐ Die Bodenspuren (sec39) — die drei Schreiber

Der Markenspeicher `0x9CB0B8` (13 Byte, 500 × 40) ist bekannt (Abschnitte V
und Y). Hier stehen seine Schreiber.

```
spur_anlegen(gruppe, RX, RY, richtung, art):        # 0x4A9590
    for b in 0..39:                                  # ⚠ ERST einen FREIEN suchen
        wenn byte[0x9CB0BC + 13·(40·gruppe + b)] == 0: gefunden
    wenn keiner frei: b = rand() % 40                # nur DANN würfeln
    satz = 13·(40·gruppe + b)
    +0 = RX;  +1 = RY;  +2 = richtung;  +3 = 1 (Anzahl Punkte);  +4 = 180 (Leben)
    +5 = (art == 0) ? rand()%4 : rand()%8 + 10

spur_punkt_anhaengen(gruppe, platz, art):           # 0x4A9680
    n = byte[satz + 3];  byte[satz + 5 + n] = (art==0)? rand()%4 : rand()%8+10
    byte[satz + 3] = n + 1                           # bis zu 8 Punkte

spuren_altern():                                     # 0x4A9790
    for 500 Reihen, 40 Plätze: wenn +4 != 0: +4 -= 1
```

`0x4A9790` läuft aus der Hauptschleife (`0x416804`); die Schleifenzahlen
`0x1F4 = 500` und `0x28 = 40` und die Schrittweite `0xD = 13` stehen als
Rohzahlen im Code. Die Lebensdauer 180 (`0xB4`) deckt sich mit Abschnitt V.

Rufer von `0x4A9590`: `0x40538F` und `0x4079DD`. Beide schieben aus dem
Einheitensatz: `word[+0x24]`, `RX`, `RY`, einmal `POHYB + 0x80`, dazu
`byte[+0x22]`. **`cmp ax, 0x2710` (10 000) vorher** heisst »keine Gruppe«.

⭐ **`+0x24` des Einheitensatzes ist die Nummer der Spurgruppe** (0…499,
10 000 = keine). Das Feld fehlt in `ENTITY_FELDER.md`.

---

### 8. ⭐ Der Mauszeiger

| Funktion | Was |
|---|---|
| `0x4A9AB0` (F `0x4A93E0`, **nicht** im Revier) | wählt die Zeigerart nach dem anstehenden Befehl `dword[0x502AD4]` → `byte[0xA182D0]` |
| `0x4A9F90` (F `0x4A98C0`) | **zeichnen** |
| `0x4A9CC0` (F `0x4A95F0`) | **löschen** |

```
zeiger_zeichnen():                                  # 0x4A9F90
    Oberfläche dword[0x540744] sperren (vtbl +0x64 = Lock,
        Wiederholung solange Ergebnis == 0x8876021C)
    64×64 Bildpunkte um (x,y) nach 0xA30A88 SICHERN
    bild = byte[0x502AA0] + 1
    wenn bild == byte[0xA31AA0 + 44·art]: bild = 0
    zeichne dword[0xA31AA4 + 44·art + 4·bild] + 0xA183E8  bei (x−32, y−32)
    entsperren (vtbl +0x80 = Unlock)

zeiger_loeschen():                                  # 0x4A9CC0
    wenn word[0x502AA4] == 0xFFFF: raus              # nichts gesichert
    sperren; die 64×64 aus 0xA30A88 ZURÜCKSCHREIBEN
    word[0x502AA4] = 0xFFFF; entsperren
```

Der Zeigervorrat: **je Zeigerart ein 44-Byte-Satz ab `0xA31AA0`** —
`+0` Bildanzahl (Byte), `+4 … +0x2B` **zehn** Dword-Versätze in den
Bildvorrat `0xA183E8`. Die 44 kommt aus `lea edx,[ecx+ecx*4]; lea ecx,
[edx+edx*8]; sub ecx,eax` = 45c − c; die 11 aus `lea ecx,[ecx+edx*2]`
= c + 10c. Beides gerechnet.

Der Zeiger wird **von Hand in die gesperrte Oberfläche geschrieben** — es ist
kein DirectDraw-Zeiger. Deshalb braucht es das Sichern und Zurückschreiben.

`0x4AA340` nullt die **256 Byte** ab `0xA182E8` (`rep stosd`, `ecx = 0x40`) —
das ist das Tastenzustandsfeld. Rufer: `0x41DD7C` und `0x41F30E`, also
**Spielstand speichern und laden**: nach dem Laden gilt keine Taste mehr als
gedrückt. Die F-Fassung `0x4A9C70` nullt `0xA17348` — genau die im Bericht
verzeichnete F-Adresse des Feldes. (Das ist zugleich die Probe darauf, dass
die als »mehrdeutig« gemeldete cfind-Zuordnung stimmt.)

---

### 9. ⭐⭐ Das Gelände: Neigungsschlüssel, Kachelwahl, Glättung

Das ist die zweite tragende Gruppe des Reviers.

#### 9.1 `0x4ACDE0` — der Neigungsschlüssel

```
neigung(spalte, zeile, &tiefste):
    i = 257·spalte + zeile                     # Eckhöhengitter 0xA3AEB0, 257×257
    A = gitter[i]        B = gitter[i+257]
    C = gitter[i+1]      D = gitter[i+258]
    m = min(A,B,C,D);   *tiefste = m
    return 1000·A + 100·B + 10·C + D − 1111·m
```

Das ist eine **vierstellige Dezimalzahl, deren Ziffern die vier Eckhöhen
über der tiefsten Ecke sind**.

**Der Beleg mit Nullmodell.** `0x4ACEB0` sucht das Ergebnis in einer Tafel
von 16 Dwords bei `0x504090`:

```
0, 101, 11, 1010, 1100, 1, 10, 1000, 100, 111, 1011, 1110, 1101, 110, 1001, 1111
```

Das sind **exakt die 16 Zahlen 0000…1111, dezimal geschrieben** — 16 von 16
vorhanden, keine einzige darüber hinaus. Hätte die Funktion irgendetwas
anderes gerechnet, träfe **kein** Eintrag. **Nullmodell:** 16 willkürliche
32-Bit-Zahlen, die genau diese Menge bilden — praktisch ausgeschlossen.

⭐ **Und es gibt eine zweite, unabhängige Bestätigung.** Der Index in
`0x4ACEB0` lautet `15·H + S`, wobei `S` die Fundstelle in dieser 16er-Tafel
ist. Bei Schrittweite **15** dürfen nur **15** Werte vorkommen — und genau
einer der sechzehn ist **unmöglich**: `1111` hiesse, alle vier Ecken lägen
über dem Minimum, was dem Minimum widerspricht. Die Schrittweite 15 sagt also
den einen unerreichbaren Eintrag voraus, und es ist genau der. `1111` steht
nur als Fangeintrag am Ende, damit die Suche immer endet.

#### 9.2 `0x4ACEB0` — Kachel wählen und setzen

```
kachel_setzen(spalte, zeile):
    wenn nicht auf der Karte: raus
    0x4C9D50(spalte, zeile, 1000)
    schluessel = neigung(spalte, zeile, &h)
    S = Fundstelle von schluessel in der 16er-Tafel 0x504090
    wenn S != 0: h += 4                          # geneigt: anderer Vorrat
    T = gelaendeart(spalte, zeile)               # 0x41D110
    i = 19·(15·h + S) + T                        # 19 Geländearten
    n = byte[0xBAA802 + 4i]
    wenn n == 0:  gelaende_setzen(spalte, zeile, −1)
    sonst:        gelaende_setzen(spalte, zeile, word[0xBAA800 + 4i] + rand()%n)
```

Die **19** trifft die schon verzeichnete Tafel »Geländetyp → Gruppe (19 B)«
bei `0x5385F0`. Die Kachelvorratstafel `0xBAA800` liegt im uninitialisierten
Bereich, wird also zur Laufzeit aus dem Kachelsatz gefüllt:
`{Erste Kachelnummer: Wort, ?, Anzahl Spielarten: Byte}`.

`0x4AD240` (F `0x4ACB70`) ist dieselbe Rechnung, gibt die Kachelnummer aber
**zurück**, statt sie zu setzen. ⚠ Für sie ist über den Stummel `0x4015C8`
**kein Rufer** auffindbar (Funktionszeiger oder tot).

#### 9.3 `0x4AD060` — die Höhenglättung

Warteschlange: `dword[0xA3AEA8]` = gelesen, `dword[0xA3AEAC]` = geschrieben,
Feld `0xA4B0B0`, 8 Byte je Eintrag. `0x4ACFC0` legt ab, `0x4AD060` arbeitet ab:

```
glaetten():
    solange schreibzeiger > lesezeiger:
        (x, y) = naechster Eintrag
        h = gitter[257·x + y]
        for (dx,dy) in den 8 Nachbarn aus 0x53A2B0:
            wenn (x+dx, y+dy) auf der Karte:
                g = gitter[…]
                wenn |h − g| > 1:
                    gitter[…] = (g < h) ? h−1 : h+1
                    (x+dx, y+dy) einreihen
```

Die Tafel `0x53A2B0` enthält wörtlich die acht Wortpaare
`(1,0) (1,1) (0,1) (−1,1) (−1,0) (−1,−1) (0,−1) (1,−1)` —
alle acht Nachbarn, genau einmal. **Der Höhenunterschied benachbarter Ecken
ist damit hart auf 1 begrenzt.**

`0x4ACFF0` läuft danach dieselbe Warteschlange durch und ruft für jede Ecke
`kachel_setzen` auf **(x,y), (x−1,y), (x,y−1), (x−1,y−1)** — die vier
Zellen, die diese Ecke berühren.

`0x4AD1A0` klammert alles zusammen:

```
gelaende_aendern(spalte, zeile, art):
    0x4ACDA0(spalte, zeile, art)                 # die Höhe/Art tatsächlich setzen
    Warteschlange leeren
    die vier Ecken (x,y) (x,y+1) (x+1,y) (x+1,y+1) einreihen
    glaetten()
    neu_bekacheln()
```

⚠ Die zwei umschliessenden Schleifen laufen **genau einmal** durch
(`cmp esi,esi; jg` bzw. `cmp edi,edi; jg` sind nie erfüllt, und die
Rücksprungbedingungen scheitern sofort). Es ist also ein Einzelzellenaufruf
mit einem vom Übersetzer stehengelassenen Schleifengerüst. Einziger Rufer:
`0x4C9E60` mit `push 2`.

---

### 10. Die Zeichenfläche: zwei Sprite-Blitter

Beide benutzen die bekannten Sockel `0xA3AE7C` (Zeilenschritt), `0xA3AE80`
(Höhe), `0xA3AE84` (Basis − 256·Zeilenschritt) und dasselbe Bildformat:
Kopf `{Höhe:Byte, y:Byte}`, dann je Zeile `{Länge:Byte, x:Byte, Merker:Byte}`
und die Bildpunkte. `Merker == 1` heisst »kann `0xFF` = durchsichtig
enthalten«, sonst ist der Lauf voll deckend.

⭐ **Der Unterschied ist die Spielerfarbe:**

| | Regel je Bildpunkt `p` |
|---|---|
| `0x4AC2F0` | `wenn (p & 0xF8) == 0: p += 8·spieler` — **acht** Farbstufen, Plätze `8s … 8s+7` |
| `0x4AC450` (nicht im Revier, F `0x4ABD80`) | `q = p−1; wenn (q & 0xF8) == 0: q = q/2 + 8·spieler; p = q+1` — **vier** Stufen, Plätze `8s+1 … 8s+4` |

Gewählt wird über `byte[0x504034]`; im `.data` steht **1**, also läuft
normalerweise `0x4AC450`. Geschrieben wird der Schalter an zwei Stellen
(`0x415B11`, `0x419440`). Der Verteiler ist `0x4AC5C0` (F `0x4ABEF0`,
18 Rufer, alle im Zeichenmodul `0x42xxxx`); danach schaltet `byte[0x504038]`
∈ {1,2,3} noch ein bis zwei Zusatzdurchgänge um `word[0xA3AE88]`/`[0xA3AE8C]`
versetzt (Schatten/Umriss) — geschrieben ausschliesslich in
`0x4299E0 … 0x42B0AB`.

`0x4ACA00` (F `0x4AC330`) ist der **einfarbige** Blitter: jeder nicht
durchsichtige Punkt wird als `byte[0xA3AE94]` geschrieben — die Farbe kommt
als Argument. Einziger Rufer: `0x4B4670`.

⚠ In `0x4ACA00` färbt der Zweig für **deckende** Läufe die Punkte **nicht**
ein: er kopiert sie roh in 4-, 2- und 1-Byte-Schritten und schreibt nur das
**letzte** Byte in der Farbe. Das sieht nach einem Fehler des Originals aus,
greift aber nur, wenn ein Lauf den Merker ≠ 1 trägt.

Beide Blitter beschneiden links über den Vergleich
`cmp eax, 0x75BCD15` (123 456 789): ein negativer x-Wert erscheint als
riesige vorzeichenlose Zahl und wird darüber erkannt.

---

### 11. Bildschirmarten aufzählen

```
bildschirmarten_holen(pDD):                       # 0x4AD4F0
    wenn pDD == 0: return 0x80070057               # E_INVALIDARG
    return pDD->vtbl[0x20](pDD, 0, 0, 0, rueckruf) # EnumDisplayModes
```

`vtbl + 0x20` ist bei `IDirectDraw` **EnumDisplayModes** (Eintrag 8), und die
Argumentzahl (4 + `this`) stimmt. Rufer: `0x4138B2` und `0x442935`.

Der Rückruf ist **`0x4AD390`** (F `0x4ACCC0`) — `rufer.py` findet für ihn
keinen Rufer, weil er nur als Zeiger übergeben wird; über den Stummel
`0x40166D` ist er eindeutig.

```
rueckruf(ddsd, ctx):                               # stdcall, ret 8
    wenn dword[0xA50FD8] >= 64: return 0            # Aufzählung abbrechen
    satz = 0xA509D8 + 24·dword[0xA50FD8]
    satz+0x00 = ddsd->dwWidth   (+0x0C)
    satz+0x04 = ddsd->dwHeight  (+0x08)
    satz+0x08…+0x14 = 0
    pf = ddsd + 0x48                                # DDPIXELFORMAT
    wenn pf.dwFlags & 0x40   (DDPF_RGB):            +0x0C/+0x10/+0x14 = R/G/B-Masken
    wenn pf.dwFlags & 0x800  (PALETTEINDEXED1):     +0x08 = 1
    wenn pf.dwFlags & 0x1000 (PALETTEINDEXED2):     +0x08 = 2
    wenn pf.dwFlags & 0x8    (PALETTEINDEXED4):     +0x08 = 4
    wenn pf.dwFlags & 0x20   (PALETTEINDEXED8):     +0x08 = 8
    dword[0xA50FD8] += 1;  return 1
```

**Zahl:** fünf `DDPF_`-Bits, jedes an der richtigen Stelle, plus die zwei
`DDSURFACEDESC`-Versätze für Breite/Höhe (`0x0C`/`0x08`, in dieser
Reihenfolge und nicht vertauscht) — 7 von 7 Treffern gegen eine Struktur mit
über 20 Feldern. **Nullmodell:** geratene Versätze und Bitwerte, die alle
sieben zugleich treffen, sind bei dieser Strukturgrösse nicht zu erwarten.

⚠ `0x4AD3FC` (`cmp dword[ecx+4],1; sbb; neg; test ah,0x10`) ist toter Code:
`ah` ist an der Stelle immer 0.

---

### 12. Die Trümmer

```
truemmerstueck(x, y, KOLIK, POHYB, …, art):        # 0x4AD7B0
    (fx, fy) = feinstellung(KOLIK, POHYB)          # 0x435BD0
    fy = 20 − fy
    art == 1 -> fly_part(…)                        # 0x4AD520, sec112
    art == 2 -> 0x4AD8B0(…, bild = 240 + rand()%3, 2)
```

Drei Rümpfe darüber, alle nach demselben Schnittmuster (Zufallsversatz
`rand()%20` / `rand()%40` / `rand()%60`, Klang `510 + rand()%9`,
Bild `310 + rand()%…`, dann `truemmerstueck`):

| | Quelle | Durchgänge | Rufer |
|---|---|---:|---|
| `0x4AEBA0` | Einheitensatz `0x6E26C8` | 2 | `0x40B6B2` |
| `0x4AED80` | Einheitensatz `0x6E26C8` | 10 | `0x40B6BA` |
| `0x4AF000` | **Flugzeugsatz `0x6DDF70`** (Schrittweite 68) | 2 | `0x423A8C` |

`0x40B6B2` und `0x40B6BA` liegen unmittelbar nebeneinander in derselben
Verzweigung wie `0x40B77B` (Druckwellenring) — es ist **eine**
Fallunterscheidung beim Einheitentod: kleine Einheit, grosse Einheit,
Druckwelle. `0x4AF000` rechnet `68·index` (`shl 4; add; shl 2`) und liest
`+0x00`/`+0x02` — die Position des Flugzeugs; der Rufer sitzt im
Flugzeugtakt.

---

### 13. Kleinkram, der überall gebraucht wird

**`0x479070` — Prozent.** 26 Rufer, das meistgerufene Stück des Reviers.
`prozent(a, b) = (b == 0) ? 0 : 100·a/b`, 16-Bit-vorzeichenbehaftet.
Die 100 steht als `shl eax,2` plus zwei `lea`-Fünferschritte im Code.

**`0x4649F0` — das Rangzeichen.** Der Rufer `0x4645C0` liest
`byte[einheit + 0x28]` = `exp` (Rang) und legt das Ergebnis als
**einzeichige Zeichenkette** ab (`byte[esp+0x24] = al; byte[esp+0x25] = 0`).

```
rangzeichen(exp):
    for i in 0..7: wenn byte[0x500E21 + 2i] >= exp: return 0xB4 + i
    return 0
```

Die Schranken bei `0x500E21` (Schrittweite 2): **5, 20, 40, 75, 110, 171,
255, 255**; die geraden Nachbarbytes halten die Untergrenzen 0, 6, 21, 41,
76, 110, 170, 254. Ergebnis ist ein **Zeichencode `0xB4 … 0xBB`** für die
Spielschrift (das `sub al, 0x4C` ist genau der Sprung von 0 auf 180).
⚠ Weil `exp` ein Byte ist, ist Eimer 7 (`0xBB`) **unerreichbar** — es gibt
praktisch **sieben** Ränge.
Die 10 Rufer verteilen sich auf die Fensterarten 2, 6, 9, 19, 22, 23, 33, 38.

**`0x484BB0` — Zeichenkette auf Restbreite kürzen.**
`kuerzen(word* rest, char* text)`: läuft vor, bis `*rest` verbraucht ist oder
ein Nullbyte kommt, zieht das Verbrauchte von `*rest` ab und schneidet ab.
Alle 11 Rufer liegen in Fensterart 39 (`0x484C00`, »Mission erfolgreich
beendet«).

**`0x487BC0`** wandelt eine Zahl mit `itoa(…, 10)` und hängt sie über
`0x41CDB0` an eine Ausgabe.

**`0x4513F0` — Flugzeug-Feld `+0x2C` weiterschalten.**

```
schalten(flugzeug, flughafen):
    wenn byte[flugzeug + 0x26] == 0xFF:            # keine Verbandskennung
        +0x2C: 0x2D -> 0x2E -> 0x2F -> 0x2D ; fertig
    sonst: for j in 0..byte[0x87943C + 52·flughafen]-1:
        p = byte[0x879443 + 52·flughafen + j]      # sec27, Stellplatz j
        wenn p == 0xFF: Schluss
        wenn typ[p] == 2 und byte[p + 0x26] == byte[flugzeug + 0x26]:
            +0x2C desselben Rings weiterschalten
```

Die Tafeln stimmen mit dem schon Belegten: sec19 `0x6DDF70`, 68 Byte;
sec27-Flughafentafel `0x879438`, 50 × 52, `+4` Platzanzahl, ab `+11` die
Stellplätze. ⚠ **Was `+0x26` und `+0x2C` bedeuten, ist ungedeutet** — sicher
ist nur: `+0x26 == 0xFF` heisst »gehört zu keinem Verband«, `+0x2C` läuft im
Dreierring `45 → 46 → 47`, und die Umschaltung gilt für **alle Flugzeuge
vom Typ 2 mit derselben `+0x26` auf demselben Flughafen**. Einziger Rufer:
`0x4C3D42`.

**`0x450850`** — läuft direkt nach dem Spielstandlader (`0x4C47E8` →
`0x41E070`):

```
wenn byte[0x4F6F9C] != 0:
    byte[0x4F6F9C] = 0
    byte[0x4FA284] = 0                 # eigener Spieler := 0
    0x44FE10(1)
    0x444440(0)                        # Leiste bei Pufferhöhe − 0xAA neu setzen
```

⚠ Siehe *Berichtigungen* B8 zur Bedeutung von `0x4F6F9C`.

---

### Berichtigungen an bestehenden Dokumenten

#### B1. ⭐⭐ `0x48786F` ist **keine** fehlende Funktion — aber der Unterschied ist echt, und jetzt **anfassbar**

Die Zuarbeit meldete `0x48786F` als »keine Entsprechung in F, Verdacht auf
Auslieferungsunterschied«. **`aere.py fs 0x48786F` gibt `0x487630` zurück** —
es ist der **gemeinsame Ausgang** des Fensterverteilers (`add esp,4` nach dem
`push ecx` jedes Arms, dann `movsx eax, di`, Puffermasse zurücksetzen,
`pop`). Kein Funktionsanfang, also auch kein eigener Unterschied.

**Aber `cfind.py --diff 0x487630 0x485D00` liefert genau das, was gesucht war:**

```
Aehnlichkeit 99.1 %  --  1 abweichender Block
--- delete: C[167..170]   F[167..167]
  C 00487867  jmp     0x48786f
  C 00487869  push    ecx
  C 0048786A  call    0x40205e
```

`0x40205E` → **`0x480650`** — das zweite Hauptmenü mit »Enzyklopädie«, genau
die Fensterart, die Abschnitt BA.8 als in F fehlend führt.

⚠ Das Werkzeug warnt hier: »der einzige Block liegt im letzten Sechstel — das
ist das Muster eines Werkzeugartefakts«. **Diese Warnung greift hier nicht**,
und zwar aus einem prüfbaren Grund: die 48 Arme stehen in der Reihenfolge
ihrer Nummer, der 48. Arm **muss** der letzte sein. Drei unabhängige
Gegenproben:

1. C: `cmp eax, 0x2F` (47) — F: `cmp eax, 0x2E` (46). Beide nachgelesen,
   nicht gerechnet.
2. Die Sprungtafel: C `0x487888` hat 48 Einträge, F `0x485F4C` hat 47 und
   danach `CCCCCCCC`-Füllung ab `0x486008`.
3. Der fehlende Block ist ein **vollständiger Arm** (`push ecx; call; jmp`),
   nicht ein abgeschnittener Rest.

⭐ **Damit ist der zehnte Auslieferungsunterschied jetzt zum fünften Mal und
erstmals als konkreter Maschinencode belegt: die drei Befehle bei `0x487869`.**

#### B2. `0x4A9BCF` ist ebenfalls kein Funktionsanfang

`aere.py fs 0x4A9BCF` → **`0x4A9AB0`** (die Wahl der Mauszeigerart, siehe
Abschnitt 8). `0x4A9BCF` ist deren gemeinsamer Ausgang. `cfind.py 0x4A9AB0`
findet die F-Entsprechung `0x4A93E0` **eindeutig**.
**Kein Auslieferungsunterschied.**

→ Die Liste der »25 C-Funktionen ohne Entsprechung in F« sollte um beide
Einträge gekürzt werden; beide sind entgleiste Zerlegungsgrenzen. ⚠ Die
übrigen 23 lohnen dieselbe Prüfung mit `aere.py fs`, bevor sie gezählt werden.

#### B3. ⚠ `cfind.py` ordnet `0x454200` falsch zu

`cfind.py` meldet `0x454200 → 0x452F30` mit »UNGENAU: 81 %« **und**
`0x454280 → 0x452F30` mit »87 %«. Zwei C-Funktionen auf dieselbe F-Adresse ist
schon für sich ein Warnzeichen. Nachgeschlagen im F-Code:

* F `0x452F30` liest `byte[…+0x6E1752]` = **`+0x2A`** und gibt bei
  ungültigem Index `0x7530` zurück → das ist **`0x454280`**.
* F `0x452EB0` liest `byte[…+0x6E1753]` = **`+0x2B`** und hat die
  Schiffskonstante `0x280` → das ist **`0x454200`**.
* Gegenprobe aus den F-Stummeln: F `0x4013F2 → 0x452EB0`,
  F `0x401F55 → 0x452F30`; F `0x452FB0` (= C `0x454300`) ruft beide in der
  Reihenfolge Höchst-, dann Mindestreichweite — wie C.

**Richtig ist `0x454200 → 0x452EB0`.**

#### B4. Die Belegung eines Bodenspurplatzes ist **nicht** `rand()%40`

`OFFENE_FRAGEN.md`, Abschnitt V: »Anlegen: Platz = `rand()%40`«.
`0x4A9590` sucht **zuerst** von 0 an einen Platz mit abgelaufener Lebensdauer
(`+4 == 0`) und würfelt **nur dann**, wenn alle 40 belegt sind
(`0x4A95A6`…`0x4A95CE`). Für den Nachbau ist das der Unterschied zwischen
»Spuren überschreiben sich sofort« und »erst wenn die Gruppe voll ist«.

#### B5. `UNIT_STATS_RE.md` §1, »Accessor note (honest)«

Dort steht: »The 58-stride table has **no inline `[base + unit_type*58]`
accessor** in `.text` … Conclusion: the engine copies these records into
runtime design-structs at startup (base is a malloc/relocated pointer).«

Beides ist zu berichtigen. Der Zugriff existiert (Abschnitt 1 oben), er ist
nur zweidimensional; und die Tafel wird **nicht** umkopiert, sondern liegt
achtfach im `.data` und wird an Ort und Stelle beschrieben.
`0x5045A0 + 8·200·58 = 0x51AFE0`, unmittelbar vor den Flugzeugvorlagen
`0x51B021`.

#### B6. `ENTITY_FELDER.md` — drei Felder fehlen

| Versatz | Was | Beleg |
|---|---|---|
| `+0x0A` | **Gattung** (0,1 = Land · 2,3 = ? · 4,5 = Schiffe) | Sprungtafeln in `0x454200`/`0x454280`; Gattung 4 bekommt feste Reichweite 640 |
| `+0x2A` | **Mindestreichweite** (×40 = Feinschritte) | `0x454280`, einziger Leser |
| `+0x24` | **Nummer der Bodenspurgruppe** (0…499, 10 000 = keine) | `0x40538F`, `0x4079DD` prüfen `cmp ax, 0x2710` und schieben den Wert als Gruppenindex in `0x4A9590` |

#### B7. sec45 — Schrittweite 6, nicht 2

`BD.1` führt sec45 als »Quelltafel `0x9C994D`, Satz 2, Sätze 3000«.
Die Byte-Zahl (6000) stimmt, die Aufteilung nicht: der Belegungssucher in
`0x4A98FD` schreitet in **6er**-Schritten von `0x9C994D` bis `0x9CB0BD`
(= 1000 Schritte), und `0x4A98A0` beschreibt sechs Felder `+0 … +5`.
Es sind **1000 Sätze zu 6 Byte**. (Abschnitt Y führt sec45 bereits korrekt
mit 6/1000 — die abweichende Zeile steht in BD.1.)

#### B8. ⚠ `0x4F6F9C` — die Begründung »Ohne CD-Schalter« ist zu prüfen

Die Datentafel in Abschnitt AT führt `0x4F6F9C` als »Ohne CD«-Schalter.
`reloc_refs.py --addr 0x4F6F9C` findet **21 Fundstellen, 8 Schreiber**:
`0x41512C := 1`, `0x41ACBE := 0`, `0x44729E := 1`, `0x44B209`, `0x44D9DD`,
`0x450850 := 0`, `0x4C47C0 := 1`, `0x4D009B := 1`.
**Keine einzige liegt in der CD-Erkennung** (`0x43B580`
`Laufwerksbuchstabe(cd)` ist nicht darunter); die Leser häufen sich dagegen
in `0x4B4F61`, `0x4B554A`, `0x4B6948`, `0x4C8C37`, `0x4C9986`.
⚠ **Markierte Vermutung, kein Beleg:** das sieht nach einem Editor- oder
Sonderbetriebsschalter aus, nicht nach der CD. `0x450850` passt dazu — es
setzt beim Verlassen den eigenen Spieler auf 0 zurück und baut die Leiste
neu. Ich habe die Stelle **nicht** zu Ende gelesen und ändere die Tafel
nicht; sie gehört auf die Nachprüfliste.

---

### Bauaufgaben, die daraus folgen

1. ⭐⭐ **Bauteilwerte je Spieler.** Unser Nachbau liest die 58-Byte-Sätze
   einmal (`ExeTables.cs`, `component_stats.json`). Das Original hält
   **acht Kopien** und lässt die Forschung nur die eigene ändern. Solange
   wir eine Kopie haben, wirkt jede Forschung auf alle Spieler.
   → `component_stats` achtfach anlegen, Sockel `0x5045A0`, Satz
   `[spieler][zeile]`, und `DesignMath` mit dem Spieler indizieren.
2. ⭐ **Mindestreichweite `+0x2A`.** Wir prüfen nur die Höchstreichweite.
   Mörser, Mittelstreckenrakete usw. können im Original **nicht in den
   Nahbereich** schiessen. Regel: `min ≤ d ≤ max`, beide × 40 Feinschritte,
   Gattung 4 fest `max = 640`.
3. ⭐ **Leuchtspur.** Bei jedem Schuss einen Eintrag in eine 200er-Liste,
   zwei Bilder lang, Teilchen entlang der Linie mit ±3 Streuung, Bildnummer
   `100 + [0,2,1,3][Rohrdrehung & 3]`.
4. ⭐ **Höhenglättung mit harter Grenze 1.** Wer Gelände verändert, muss die
   acht Nachbarecken nachziehen, bis kein Sprung > 1 bleibt, und danach die
   vier berührten Zellen neu bekacheln.
5. ⭐ **Neigungsschlüssel.** Die Kachelwahl ist
   `19·(15·(h + (geneigt ? 4 : 0)) + S) + Geländeart` mit `S` aus der
   16er-Tafel `0x504090`. Das ist eine fertige Formel — wir raten
   Kachelübergänge bisher.
6. **Rangzeichen.** Erfahrung → Zeichen `0xB4 + i` über die Schranken
   `5/20/40/75/110/171/255`. Sieben erreichbare Ränge.
7. **Krater nur auf ebenem, freiem Boden**, Alter `rand()%80 + 5`, Bildnummer
   aus Eckhöhe, Art und `rand()%8`.
8. **Bodenspuren: freien Platz suchen, erst dann würfeln** (siehe B4).
9. **Spielerfarbe.** Der laufende Blitter ist `0x4AC450`: Quellstufen 1…8
   werden **halbiert** und auf `8·spieler + 1 … 8·spieler + 4` abgebildet.
   Wer acht Stufen abbildet, malt die falschen Farbplätze.
10. **» Cheater«.** Falls wir je Entwicklerkniffe einbauen: das Original hängt
    den Namen an — höchstens einmal, und nur wenn der Name ≤ 12 Zeichen hat.
11. **Zufallsentwurf (Befehl 25).** Fahrwerk aus 10, Waffe/Ausrüstung aus 34
    (15 Ausrüstungen : 19 Waffen). Wir haben keinen Zufallsentwurf.
12. **Namensfilter der Gefechts-Einrichtung**: nur `0x20 … 0x7A`, ohne
    `[`, `]`, `^`.
13. **CD-Musik**: drei Betriebsarten (Wiederholung, Reihe, **Zufall**) —
    die dritte hat im Originalfenster nicht einmal eine Beschriftung.

---

### Was ungedeutet bleibt

* **`0x4513F0`**: `+0x26` (Verbandskennung?) und `+0x2C` (Ring 45/46/47) des
  Flugzeugsatzes. Sicher ist nur der Ablauf, nicht die Bedeutung. Beide
  Versätze fehlen auch in `AIR_RE.md`.
* **`0x450850` / `0x4F6F9C`**: siehe B8. Der Zustand ist nicht gelesen.
* **`0x454560`, Feld `+3`** der Druckwellenliste (die Wucht, nach
  `byte[0x87D6A8]`): **kein Schreiber im ganzen Programm**. Ob das ein Fehler
  des Originals ist oder ob ich einen Schreiber übersehe, der ohne Relokation
  auskommt, ist offen — `reloc_refs.py --block` habe ich dort **nicht**
  laufen lassen.
* **`0x4AD240`** (336 B): funktionsgleich mit `0x4ACEB0`, gibt die
  Kachelnummer zurück. **Kein Rufer auffindbar**, auch nicht über den
  Stummel `0x4015C8`.
* **`0x4ACA00`**, deckender Zweig: färbt nur das letzte Byte ein (siehe
  Abschnitt 10). Ob je ein Bild einen deckenden Lauf hat, habe ich nicht
  geprüft.
* **`0x487BC0`**: über `rufer.py` und den Stummel `0x4018CF` ist kein Rufer
  auffindbar (die Revierliste nennt 1).
* **`0x4AD1A0`**: der Aufruf `0x4C9E60` mit `push 2` — welche Geländeart »2«
  ist und wer `0x4C9E60` seinerseits ruft, habe ich nicht verfolgt.
* **`0x4A98A0`**, Argumente `a2`/`a3` (Kratersatz `+2`/`+3`) und die Tafel
  `0xBAB2A8` für den Wasser-/Sonderfall: nur die Rechnung ist gelesen, nicht
  die Bedeutung.
* **Fensterart 36** (`0x47CD60`, im Bericht ohne Beschriftung) hat nach
  `0x487A10` ein **Texteingabefeld** — welches, ist offen.
* **`0x4A9F90` / `0x4A9CC0`**: `word[0x502AC0]` und `byte[0x8B7250]` schalten
  beide Funktionen ganz oder teilweise ab (Fenster-/Vollbildbetrieb?).
  Ungedeutet.
* **`0x4AC2F0` gegen `0x4AC450`**: warum es zwei Farbregeln gibt und wann
  `byte[0x504034]` auf 0 gesetzt wird (`0x415B11`, `0x419440`), habe ich
  nicht gelesen.

---

## BO. Revier 7: 0x4AF1C0 … 0x4C7FE0

61 Funktionen, 10 912 Byte. Gelesen am 22.08.2026.

Das Revier zerfällt in **elf Mechaniken**, nicht in 61 Einzelbefunde. Vier davon
hängen an der **Bahn** (die Streckensuche, die Zellenliste sec22, die
Linienverwaltung sec33/sec34, der Zugtakt), drei an der **Anzeige** (die
Zeichengrundformen, die Kamera, der Zeichensatz), zwei an den **Entwürfen**
(sec46/sec47/sec119), eine am **Klang** und eine an der **KI**.

Zusätzlich sind die vier Aufträge des Auftraggebers erledigt:
die **Palettenplätze 5/9/13** (Abschnitt 9), die Herkunft von
**`byte[0xA31A88]`** (Abschnitt 10), die **F-Gegenlesung der drei Anker**
(Abschnitt 11) und die krumme Adresse **`0x4BFE2D`** (Abschnitt 12).

---

### Adresstafel

F-Adressen aus `cfind.py`. ⚠ = Fund nur mit Vorbehalt (siehe Fussnote).

| C | F | Byte | Rufer | Was sie ist |
|---|---|---:|---:|---|
| `0x4AF1C0` | `0x4AEAF0` | 80 | 2 | Streckensuche: **Bit prüfen** in sec35, Index `257·yh + x` |
| `0x4AF210` | `0x4AEB40` | 80 | 2 | Streckensuche: **Bit setzen** in sec35 |
| `0x4AF260` | `0x4AEB90` | 128 | 1 | Prüfbild: 8×8-Block in Farbe `a3` punktweise auf die Oberfläche |
| `0x4AF2E0` | `0x4AEC10` | 256 | 2 | Streckensuche: **einen Schritt rückwärts** über die 6 Nachbarn; liefert die Gleisachse, merkt den Schrittcode |
| `0x4AF3E0` | `0x4AED10` | 192 | 2 | Streckensuche: **Zelle in die Warteschlange legen**, wenn frei und unbesucht |
| `0x4AF4A0` | `0x4AEDD0` | 32 | 2 | Warteschlangenzeiger `+1`, Umlauf bei **2000** |
| `0x4B0130` | `0x4AFA70` | 80 | 1 | Knoten räumen: alle vier Linien des sec33-Knotens zerstören |
| `0x4B0180` | `0x4AFAB0` | 160 | 1 | Linie ablaufen (Schritttafel `0x5043C0`) — ⚠ **Ergebnis wird verworfen** |
| `0x4B0220` | `0x4AFB50` | 224 | 2 | `sec34[linie] + 0x08 + feld = wert` für `feld` 0…3, dann `0x44FD70(linie)` |
| `0x4B07C0` | `0x4B00F0` | 96 | 4 | sec22: Satz an `(sp,ze)` mit `+2 != 0xFF` suchen → `0x4B0460(idx,1)` |
| `0x4B0820` | `0x4B0150` | 480 | 3 | Umkreis eines sec3-Gebäudes absuchen (10 Spalten × 6 Zeilen) |
| `0x4B0A60` | `0x4B0390` | 96 | 2 | sec22: „liegt an `(sp,ze)` ein Satz mit `+2 < 100`?" (Boden/Gleis/Tür) |
| `0x4B0AC0` | `0x4B03F0` | 96 | 3 | sec22: Satzindex an `(sp,ze)` mit `100 ≤ +2 < 255` (Brücke/Rampe), sonst `0xFFFF` |
| `0x4B0B20` | `0x4B0450` | 176 | 9 | wie oben, zusätzlich `+4 == a3` verlangt |
| `0x4B0BD0` | `0x4B0500` | 304 | 3 | **eine Brücke/Rampe bis ans Ende ablaufen**, Endzelle über zwei Zeiger |
| `0x4B0D00` | `0x4B0630` | 240 | 2 | die vier Nachbarn W→N→O→S auf dieselbe Bauwerksnummer prüfen |
| `0x4B0ED0` | `0x4B0800` | 272 | 2 | über alle 80 Linien: die mit `+0x00 != 0xFF` und `Faze != 3` behandeln |
| `0x4B1110` | `0x4B0A40` | 96 | 1 | sec22: den Wert `+2` an `(sp,ze)` holen, `0xFF` wenn keiner |
| `0x4B2290` | `0x4B1BD0` | 80 | 4 | **sec46 (200 × 58) von Spieler 0 auf die Spieler 1…7 vervielfältigen** |
| `0x4B22E0` | `0x4B1C20` | 80 | 2 | **sec47 (200 × 46) von Spieler 0 auf die Spieler 1…7 vervielfältigen** |
| `0x4B24B0` | `0x4B1DF0` | 48 | 3 | 8 Spieler × Entwürfe 1…199 löschen (`0x4B1FB0`) |
| `0x4B24E0` | `0x4B1E20` | 48 | 2 | 8 Spieler × Schiffsentwürfe 1…9 löschen (`0x4B25E0`) |
| `0x4B2510` | `0x4B1E50` | 208 | 2 | **neuen Entwurf in sec47 anlegen**, Name 20 B bei `+0x02` |
| `0x4B27D0` | `0x4B2110` | 272 | 2 | sec19 (200 × 68): `+0x19 · 100 / +0x1A` — eine Prozentrechnung an der Luftwaffe |
| `0x4B2AE0` | `0x4B2420` | 64 | 2 | sec47-Satz `(entwurf, spieler)` löschen: `+0x00` und `+0x18` auf 0 |
| `0x4B3AF0` | `0x4B3420` | 480 | 2 | **Land-/Infanterieeinheit aus ihrem sec47-Entwurf neu setzen** |
| `0x4B3CD0` | `0x4B3600` | 160 | 4 | über alle 8000 Einheiten: Gattung 0/1 → `0x4B3AF0`, 4/5 → `0x4B3D70`, 2/3 → nichts |
| `0x4B3D70` | `0x4B36A0` | 480 | 2 | **Schiff aus seinem sec119-Entwurf neu setzen** (80 Sätze à 42 B) |
| `0x4B4A60` | `0x4B4390` | 304 | 2 | **Randscrollen** der Karte am Bildschirmrand |
| `0x4B4F60` | `0x4B4890` | 496 | 2 | **Kamera normalisieren**: Feinversatz zurück in `[0,40)` / `[0,20)`, Kachel nachziehen |
| `0x4B5150` | `0x4B4A80` | 304 | 2 | ⭐ **„liegt der Mauszeiger auf Feld (sp,ze)?"** — die Iso-Trefferprüfung |
| `0x4B5280` | `0x4B4BB0` | 144 | 3 | ⭐ **Bildschirmpunkt → Kartenfeld**, mit Höhensuche über 8 Zeilen |
| `0x4B5C60` | `0x4B5590` | 128 | 3 | **einen Bildpunkt setzen** (Sperren/Entsperren der Primäroberfläche je Punkt) |
| `0x4B6CA0` | `0x4B65D0` | 192 | 3 | **waagerechte Linie** im Rückpuffer |
| `0x4B6D60` | `0x4B6690` | 192 | 3 | **senkrechte Linie** im Rückpuffer |
| `0x4B6E20` | `0x4B6750` | 224 | 2 | ⭐ **das Auswahlrechteck (Gummiband)** in Farbe 255 |
| `0x4B6F00` | `0x4B6830` | 96 | 1 | Oberfläche sperren, `0x4BA640(lpSurface)` rufen, entsperren |
| `0x4B93F0` | `0x4B8D20` | 144 | 2 | **Ladebalken eines Umschlagsatzes** (sec48), erzwingt Betriebsart 4 |
| `0x4B9480` | `0x4B8DB0` | 304 | 11 | **beide Oberflächen mit 0 füllen** (Primär + Rückpuffer), setzt `dword[0x5387C8]` |
| `0x4B95B0` | ⚠ | 32 | 2 | `IDirectDrawSurface::SetPalette(0x540770, [0x540750])` |
| `0x4B9ED0` | ⚠ | 128 | 3 | **`datei_lesen(name, puffer, laenge)`** — `fopen "rb"` / `fread` / `fclose` |
| `0x4BA1C0` | `0x4B9CC0` | 240 | 3 | **ein Zeichen zeichnen**, Zeichensatz `0xB26440`, **131 B je Zeichen** |
| `0x4BADB0` | `0x4BA8B0` | 144 | 2 | sec48: Umschlagsatz des Spielers mit Ladungsart `a1` suchen |
| `0x4BAE40` | `0x4BA940` | 112 | 2 | dasselbe, nur als Ja/Nein |
| `0x4BE9A0` | `0x4BE450` | 144 | 3 | ⭐ **KI-Bedrohungszahl eines Sektors** aus sec56 |
| `0x4C00C0` | `0x4BFB80` | 272 | 2 | sec90/sec91: ein 15-Zeilen-Fenster absuchen |
| `0x4C0A30` | `0x4C04F0` | 64 | 2 | Summe von `0x4C0890(0…49)` |
| `0x4C0E00` | `0x4C08C0` | 64 | 2 | zweimal `0x4C0860` würfeln; wenn `wurf+2 ≤ byte[0x53904C]` → `0x4C0D20(a1)` |
| `0x4C1290` | `0x4C0D50` | 208 | 2 | ⭐ **Marktlagereinheit auf einen freien ROB_PROD-Platz setzen** (sec94/40/39) |
| `0x4C16E0` | `0x4C11A0` | 64 | 2 | **„steht die Einheit NICHT in der Mitnahmeliste?"** (20 Wörter ab `0x9937B8`) |
| `0x4C1960` | `0x4C1420` | 48 | 2 | `Release` auf `0xB4BE0C` und `0xB4BE10` |
| `0x4C1C10` | `0x4C16D0` | 80 | 5 | ⭐ **alle Klänge anhalten** — `Stop` auf 20 + 5 Puffer |
| `0x4C1C60` | `0x4C1720` | 48 | 2 | einen bestimmten Klang anhalten, wenn `a1 == dword[0x539200]` |
| `0x4C1C90` | `0x4C1750` | 864 | 2 | ⭐ **einen Klang abspielen** — Ringkanal, `DuplicateSoundBuffer` |
| `0x4C1FF0` | `0x4C1AB0` | 128 | 2 | **alle Klangpuffer freigeben** (`Release`) |
| `0x4C2220` | `0x4C1CE0` | 96 | 1 | ⭐ **`ist_dateiende(FILE*)`** über `ftell`/`fseek END`/`ftell`/`fseek zurück` |
| `0x4C5BB0` | `0x4C5760` | 128 | 8 | **Netzbefehl 0x3D3 füllen und absenden** |
| `0x4C7680` | `0x4C7230` | 64 | 2 | ⭐ **Takt aller 240 Waggons** (sec44, 24 B je Satz) |
| `0x4C7F40` | `0x4C7AF0` | 48 | 1 | `sec34[linie] + 0xD5 (Faze) = 3` |
| `0x4C7F70` | `0x4C7B20` | 112 | 3 | ⭐ **eine Verkehrslinie zerstören** |
| `0x4C7FE0` | `0x4C7B90` | 288 | **79** | ⭐⭐ **`text_und_zahl_ausgeben(x, y, text, zahl, oberflaeche)`** |

⚠ `0x4B95B0` und `0x4B9ED0`: `cfind` meldet »mehrdeutig« mit einem `.text`-Abstand
von `-0x40B0` bzw. `-0x7F420`, während alle Nachbarn in diesem Fenster `-0x6D0`
haben. **Diese zwei F-Adressen sind falsch** — es sind kurze, formgleiche
Allerweltsrümpfe (ein `Release`-Aufruf; `fopen/fread/fclose`), von denen es in F
mehrere gibt. Nicht übernehmen.

---

### 1. ⭐⭐ Die Streckensuche der Verkehrslinien — `0x4AF1C0 … 0x4AF4A0`

Die sechs kleinen Funktionen am Reviersanfang sind zusammen **die Wegsuche, mit
der das Spiel ein Gleis von A nach B legt**. Die Klammer darum ist `0x4AF4C0`
(nicht in meinem Revier; ein einziger Rufer über den Thunk `0x401A2D`, gerufen
aus `0x4AFEE8`). Ich habe sie mitgelesen, weil die sechs sonst nicht deutbar sind.

#### 1.1 Das Arbeitsgitter — und warum sec35 genau 16 481 Byte hat

Zwei Felder tragen die Suche:

| Feld | Adresse | Index | Grösse |
|---|---|---|---|
| Arbeitsgitter (Bytes) | `0xA68F18` | `513·Spalte + Halbzeile` | 131 841 B |
| Besuchtbitfeld = **sec35** | `0xA8D8C8` | Bit `257·Halbzeile + Spalte` | **16 481 B** |

⭐ **Der Beleg, den ich nicht gesucht, sondern gerechnet habe:** die zwei
Indexformeln stehen als `lea`-Ketten im Code (`shl 9; add` bzw. `shl 8; add`),
also **vor** jedem Nachschlagen. Setzt man die Schranken aus `0x4AF3E0` ein
(`cmp esi,0xFF` → Spalte ≤ 256, `cmp edi,0x201` → Halbzeile ≤ 512), ergibt sich
für das Bitfeld

```
⌈(512·257 + 256 + 1) / 8⌉ = ⌈131841/8⌉ = 16481 Byte
```

— **auf das Byte genau die verzeichnete Grösse von sec35.** Nullmodell: von den
Nachbargrössen in der Ladertafel (sec33 = 960, sec34 = 17 120, sec36 = 10 500)
trifft keine; eine falsch geratene Schrittweite verfehlt die Zahl um Hunderte
Byte. Das bestätigt AL.2 aus einer dritten, unabhängigen Richtung.

⚠ Die Byte- und die Bitablage sind **gegeneinander transponiert**: das Bytefeld
läuft spaltenweise (`513·Spalte`), das Bitfeld zeilenweise (`257·Halbzeile`).
Wer eines von beiden nachbaut und die Formel des anderen benutzt, bekommt
Zufallstreffer.

#### 1.2 Der Nachbarschaftsbegriff: sechs Richtungen mit Zeilenparität

`0x4AF2E0` liest eine Tafel `0x504370`, **12 Paare `i16 dSpalte, dHalbzeile`**,
und wählt den Block über `Halbzeile % 2`:

| Halbzeile **gerade** | | Halbzeile **ungerade** | |
|---|---|---|---|
| 0 | `(0, −1)` | 6 | `(−1, −1)` |
| 1 | `(+1, −1)` | 7 | `(0, −1)` |
| 2 | `(0, +1)` | 8 | `(−1, 0)` |
| 3 | `(+1, +1)` | 9 | `(+1, 0)` |
| 4 | `(0, −2)` | 10 | `(−1, +1)` |
| 5 | `(0, +2)` | 11 | `(0, +1)` |

Daneben stehen zwei Übersetzungstafeln, beide 12 Byte:

* `0x5043A0 = {4,2,5,3,1,1, 3,5,0,0,2,4}` — **die Gleisachse** (Rückgabewert)
* `0x5043B0 = {4,6,7,5,0,1, 8,9,2,3,10,11}` — der **Schrittcode** für die Waggons

⭐⭐ **Beide sind exakt geprüft, nicht geraten.**

`0x5043B0` bildet jeden Schritt auf **genau seine Umkehrung** in der schon
bekannten Waggon-Schritttafel `0x5043C0` ab (GAMESTATE_RE §3.88). Ich habe alle
zwölf einzeln nachgerechnet: **12/12.** Nullmodell: eine zufällige Permutation
trifft je Eintrag mit 1/12, alle zwölf mit `12⁻¹²`. Das ist auch sachlich
zwingend — die Suche läuft **rückwärts** vom Ziel zum Anfang, also muss der
gespeicherte Fahrcode der Gegenschritt sein.

`0x5043A0` gibt jedem Richtungspaar dieselbe Zahl: `{8,9}`→0, `{4,5}`→1,
`{1,10}`→2, `{3,6}`→3, `{0,11}`→4, `{2,7}`→5. **Alle sechs Paare sind exakt
antipodisch** (`d + d' = (0,0)`) — 6/6. Nullmodell: eine zufällige Zuordnung von
12 Richtungen auf 6 Werte à 2 trifft die antipodische Aufteilung mit
`1/10395 ≈ 0,01 %`. Der Rückgabewert ist also **die ungerichtete Gleisachse**,
und das ist genau, was ein Gleisstück braucht: ein Gleis kennt keine Fahrtrichtung.

#### 1.3 Der Ablauf

```
0x4AF4C0(sp1, hz1, sp2, hz2, linie):        # alle fünf sind BYTES
    gitter[..] = 0                          # 257 Zeilen à 513, rep stosd
    für jede Kartenzelle (sp, ze):
        halbzeile = 2·ze          -> word[0xA66DD4]
        code = word[0xBDEA80 + 2·(sp·256 + ze)]      # sec6
        gesperrt, wenn code == 0xFFFE
                  oder (code < 8000 und byte[einheit(code)+0x0A] != 0)
                  oder (8000 <= code < 14000 und byte[gebäude…] != 0)
        sonst nach Geländeklasse 0x41D110 verzweigen (5 Arme):
             gitter[sp][2ze .. 2ze+2] = 1   # drei Halbzeilen je Kachel
    gitter[ziel] = 0 ; gitter[start] = 8
    warteschlange 0xA66F20 = start ; wellenfarbe byte[0xA8D500] = 9

    # ---- Welle vorwärts (Breitensuche) ----
    solange nicht am Ziel:
        0xA66DD0/D4 = warteschlange[kopf]
        wenn Wellenende erreicht: wellenfarbe++ (Umlauf 255 -> 8), wellenzahl++
        für alle 6 Nachbarn:  0x4AF3E0(dSpalte, dHalbzeile)
              -> Schranken 0<=sp<=255, 0<=hz<=513
              -> nur wenn gitter[..] == 0 und Bit in sec35 NICHT gesetzt
              -> gitter[..] = wellenfarbe ; Bit setzen (0x4AF210)
              -> anhängen, Zähler 0x4AF4A0 (Umlauf 2000)

    # ---- rückwärts, die Strecke schreiben ----
    byte[sec34 + 214·linie + 0x0C] = wellenzahl + 4      # delka
    solange wellenzahl > 0:
        wellenfarbe--  (Umlauf 8 -> 255)
        achse = 0x4AF2E0(&spalte, &halbzeile, wellenfarbe)
        byte[0xA68E64] = Schrittcode aus 0x5043B0
        klasse = 0x41D110(spalte, halbzeile/2)
        klasse 1 -> 7 ; 2 -> 9 ; 3 -> 6 ; 4 -> 8      # überschreibt die Achse
        0x4AFA90(spalte, halbzeile/2, stück, linie)   # Gleis in die Karte
```

⭐ Damit sind **`delka` (sec34 `+0x0C`) und die Strecke `+0x0D…` erzeugt** — die
Felder, die unsere `RailLine` seit langem trägt, ohne dass bekannt war, wer sie
schreibt.

⚠⚠ **Ein belegter Engpass:** `0x4AF4C0` nimmt seine vier Koordinaten als
**Bytes** entgegen. Die Halbzeile läuft aber bis `2·H`. Auf einer Karte mit
`H > 127` kann diese Funktion Start und Ziel gar nicht mehr aufnehmen. Das ist
dieselbe Bytefalle wie bei `sec34 +0x03`/`+0x05`, für die es sec122 gibt (AW.5) —
nur ist hier **kein** Ausweg im Code. Für den Nachbau heisst das: auf Karten mit
H > 127 konnte das Original keine neue Linie mehr verlegen.

⚠ `0x4AF260` (der 8×8-Block, ein Rufer) ruft `0x4B5C60` und zeichnet punktweise
mit Sperren/Entsperren **je Punkt** — 64 Lock/Unlock-Paare für eine Kachel. Das
ist kein Spielcode, das ist ein **Prüfbild der Wegsuche**.

---

### 2. ⭐ Die Zellenliste sec22 und die Brücken — `0x4B07C0 … 0x4B1110`

Sieben Funktionen bedienen **sec22** (`0xC2C220`, 15 000 B). Die Satzweite steht
in jeder von ihnen als `lea eax,[eax+eax*4]`, also **5 Byte**; `0xBB8 = 3000`
Sätze ⇒ `3000 · 5 = 15000`. Geht auf.

**Satzbau, aus den Zugriffen zusammengesetzt:**

| Versatz | Was |
|---|---|
| `+0` | Spalte |
| `+1` | Zeile |
| `+2` | **derselbe Code wie sec20** — `0xFF` = freier Platz |
| `+3` | (nicht angefasst) |
| `+4` | die Bauwerks-/Liniennummer |

⚠⚠ **Berichtigung an AW.3/AW.4:** dort steht sec22 als
»5-B-Sätze: x, y, ·, ·, Linie«. **`+2` ist kein Platzhalter.** Vier Funktionen
lesen ihn und zerlegen ihn nach genau der Wertetafel, die für sec20 aufgestellt
wurde:

* `0x4B0A60`: `+2 < 100` → wahr (Boden / Gleis / Tür)
* `0x4B0AC0`: `100 ≤ +2 < 255` → Bauwerk, gibt den Satzindex
* `0x4B1110`: gibt `+2` roh zurück, `0xFF` wenn kein Satz
* `0x4B0B20`: dieselbe Dreiteilung, zusätzlich `+4 == a3`

Die Schranke **100** steht dreimal roh im Code (`cmp cl,0x64`) und trennt genau
dort, wo `0x4CE6E0` mit `cmp …,0x63; seta` trennt. Zwei unabhängige Stellen,
dieselbe Zahl. `0xFF` als »Platz frei« ist der vierte Wert und in sec20 nicht
möglich (dort ist 0 der Boden) — sec22 ist eine **Liste**, keine Karte, und
braucht darum eine Leermarke.

**`0x4B0D00(sp, ze, nr, &sp_aus, &ze_aus)`** prüft die vier Nachbarn in fester
Reihenfolge **W → N → O → S** und liefert den ersten, der zu Bauwerk `nr`
gehört. **`0x4B0BD0`** hängt diesen Schritt in eine Schleife und **läuft eine
Brücke oder Rampe bis ans Ende ab**; die Bauwerksnummer holt es sich vorher
selbst über `0x4B0AC0`. Das ist die Funktion, die »geh über die Brücke« beantwortet.

---

### 3. ⭐ Linien, Knoten und Züge — `0x4B0130`, `0x4B0180`, `0x4B0220`, `0x4B0ED0`, `0x4C7680`, `0x4C7F40`, `0x4C7F70`

Alle Satzweiten stammen aus den `lea`-Ketten, nicht aus der Ladertafel:

| Tafel | Rechnung im Code | Ergebnis | Grösse laut Ladertafel |
|---|---|---|---|
| sec34 | `x·8 → ·9 → −x → ·3 → +x` | **214** | 17 120 = 80 × 214 ✓ |
| sec33 | `x·8` | **8** | 960 = 120 × 8 ✓ |
| sec44 | `esi += 0x18`, Ende `0xB975C8` | **24**, 240 Sätze | 5 760 ✓ |
| sec3 | `x·9 → ·2+x → ·4` | **76** | 22 800 = 300 × 76 ✓ |

**Was hier neu belegt ist:**

* **`0x4C7F70(linie)` = eine Verkehrslinie zerstören.** Sie räumt die
  Liniennummer aus allen Knoten (`0xA8D50B + 8·k + 0…3`, **nur die ersten 40**
  von 120 Knoten — `cmp ecx,0x140`), setzt `sec34[linie]+0x00 = 0xFF` und ruft
  `0x4C7990`, die schon gelesene **Zugexplosion** (Abschnitt BG). Damit ist auch
  BG angeschlossen: die Explosion ist kein Kampfereignis, sie ist die Folge
  davon, dass die Linie unter dem Zug verschwindet.
* **`0x4B0130(knoten)` = einen Bahnhof räumen**: `byte[sec33+8k+2] = 0`, dann
  `0x4C7F70` für alle vier Einträge `+3…+6`. ⭐ Damit steht der sec33-Satzbau:
  `+0 u16` Gebäudeindex, **`+2` Zahl der Linien, `+3…+6` bis zu vier
  Liniennummern**, `+7` unbenutzt.
* **`0x4C7F40(linie)` setzt `Faze` (`+0xD5`) auf 3.** `0x4B0ED0` überspringt
  genau die Linien mit `Faze == 3` und die mit `+0x00 == 0xFF`. **3 ist also der
  Zustand »ausser Betrieb«**, und `0xFF` in `+0x00` heisst »Platz frei«.
* **`0x4C7680` = der Waggontakt über alle 240 Sätze** — `0xB95F48` bis
  `0xB975C8` in Schritten von 24, jeder mit `byte[+0] != 0` geht an `0x4C69C0`.
  Das ist der Rufer über dem schon gelesenen `0x4C64AE`. **240 = 60 Züge × 4
  Waggons** deckt sich mit dem `div 60` aus GAMESTATE_RE §3.88.
* **`0x4B0220(linie, feld, wert)`** schreibt in `sec34 +0x08 … +0x0B`. Vier
  Felder, über eine Sprungtafel `0x4B02C0` einzeln erreichbar, danach immer
  `0x44FD70(linie)`. **Welche vier Werte das sind, bleibt ungedeutet** — sicher
  ist nur, dass sie einzeln von aussen gesetzt werden, also Bedienelemente sind.

⚠ **`0x4B0180(linie)` ist toter Rechenaufwand.** Sie läuft die Strecke der Linie
Schritt für Schritt mit der Tafel `0x5043C0` ab, führt Spalte und Halbzeile mit
— und **kehrt zurück, ohne das Ergebnis irgendwohin zu schreiben**. Beide
Zwischenwerte liegen in Stapelplätzen (`[esp+0xa]`, `[esp+0xb]`), die nach dem
`ret` verfallen. Ein Rufer. Vermutlich ein Rest einer Debugausgabe.
**Nicht nachbauen.**

---

### 4. ⭐ Die Entwurfstafeln und ihre Rückwirkung — `0x4B2290 … 0x4B3D70`

**Drei Tafeln, drei Satzweiten, alle aus den `lea`-Ketten:**

| Tafel | Adresse | Satz | Sätze | Aufteilung |
|---|---|---:|---:|---|
| sec46 (Bauteile) | `0x5045A0` | **58** | 1600 | 8 Spieler × 200 |
| sec47 (Entwürfe) | `0x51CE20` | **46** | 1600 | 8 Spieler × 200 |
| sec119 (Schiffe) | `0x52EDA0` | **42** | 80 | 8 Spieler × **10** |

⭐ **Der Beleg für »8 × …«, ohne dass die Zahl 8 im Index steht:** `0x4B2290`
und `0x4B22E0` kopieren einen Block der Länge `0x2D50` (= 200 × 58) bzw.
`0x23F0` (= 200 × 46) **siebenmal** hintereinander weiter — `mov eax,7`. Ein
Block plus sieben Kopien = acht, und `8 · 11600 = 92800` bzw. `8 · 9200 = 73600`
sind auf das Byte die Grössen aus der Ladertafel. **Das ist die Vorbelegung: der
Spieler 0 bringt die Entwürfe mit, die anderen sieben bekommen sie geschenkt.**

⭐ Die **10** bei den Schiffen steht zweimal unabhängig da: `0x4B24E0` läuft
`i = 1…9` (`cmp esi,0xA`) über 8 Spieler, und `0x4B3D70` rechnet
`entwurf + 10·spieler` (`lea ebp,[edx+edx*4]; lea edx,[ebx+ebp*2]`). Dagegen
`0x4B24B0` mit `cmp esi,0xC8` = 200 und `0x4B3AF0` mit `entwurf + 200·spieler`.
**Land 200, Schiff 10** — und in beiden Fällen ist **Platz 0 unbenutzt**
(beide Löschschleifen fangen bei 1 an).

**`0x4B3CD0` ist das Bindeglied.** Sie geht alle 8000 Einheitenplätze durch und
verteilt über eine zweistufige Tafel:

```
byte[0x4B3D48 + gattung] = {0,0,2,2,1,1}      Sprungziele bei 0x4B3D3C
   Gattung 0,1 -> 0x4B3AF0   (Land / Infanterie, sec47)
   Gattung 2,3 -> nichts
   Gattung 4,5 -> 0x4B3D70   (Schiffe, sec119)
```

⭐ **Das ordnet die sechs Gattungen zum ersten Mal ihren Entwurfstafeln zu** —
und zwar unabhängig von der Balkensprungtafel `0x4B78BC`, die dieselbe Gattung
liest. Zwei Verteiler, dieselbe Aufteilung: 0/1 zusammen, 4/5 zusammen, 2/3
gesondert.

Beide Nachzieher tun dasselbe:

```
spieler = einheit / 1000
entwurf = byte[einheit + 0x43]
satz    = tafel + weite·(entwurf + je_spieler·spieler)
wenn Hp(+0x08) == HpMax(+0x29):  Hp    = satz[+0x1E bzw. +0x1D]
                                 HpMax = satz[+0x1E bzw. +0x1D]
(0x4B3D70 klemmt danach Hp auf HpMax)
```

⭐ Der Wächter `Hp == HpMax` ist der Kern: **eine unbeschädigte Einheit
übernimmt den neuen Entwurfswert voll, eine beschädigte behält ihren Schaden.**
Das ist die Regel für »Entwurf verbessert — was passiert mit den bestehenden
Einheiten«.

**`0x4B2510` legt einen neuen Entwurf an**: sie sucht von Platz **40 abwärts**
den obersten mit `+0x00 == 0`, verlangt Index ≥ 2, setzt `+0x00 = +0x01 = 1`,
kopiert den Namen mit `strncpy(satz + 0x02, name, 0x14)` — **20 Byte Name bei
`+0x02`** — und setzt `+0x17`, `+0x18` (Spieler) und `+0x19`.
⚠ Die Suche von oben heisst: **neue Entwürfe füllen die Plätze von 40 abwärts**,
nicht von 1 aufwärts. Wer sie aufsteigend vergibt, bekommt eine andere
Reihenfolge in der Liste als das Original.

---

### 5. ⭐ Die Iso-Trefferprüfung — `0x4B5150` / `0x4B5280`

Das ist die Antwort auf »welches Feld liegt unter dem Mauszeiger«, und sie ist
sauber belegbar, weil alle beteiligten Globalen schon benannt sind
(Kamera-Kachel `0x5387AC/B0`, Feinversatz `0x5387B8/BC`, Maus `0x502AA8/AC`).

```
0x4B5150(spalte, zeile) -> 0/1 :
    wenn zeile >= Kartenhöhe: 0
    y0 = 20·(zeile − kamera_zeile) + 20 − 15·terrain_at(spalte, zeile) − feinversatz_y
    x0 = maus_x − (feinversatz_x + 40·(kamera_spalte − spalte))
    form = gelände_typ(spalte, zeile)                    # 0x41D110
    oben  = (h[form][0] − h[form][2]) · x0 / 40 − h[form][0] + y0 − 1
    unten = (h[form][1] − h[form][3]) · x0 / 40 − h[form][1] + y0 + 1
    liefert 1, wenn maus_y > oben UND maus_y >= unten
```

⭐ **Die Kachel ist 40 × 20 Bildpunkte.** Beide Zahlen stehen roh im Code
(`mov ebx,0x28`, `mov ecx,0x14`), und die Höhe schlägt mit **15 Punkten je
Geländestufe** durch (`lea eax,[ecx+ecx*2]; lea edx,[eax+eax*4]` = 3·5). Dieselbe
40/20-Teilung findet sich unabhängig in `0x4B5280` (`idiv 0x28`, `idiv 0x14`)
und in `0x4B4F60` (Klemmung des Feinversatzes auf `[0,40)` / `[0,20)`).
**Drei unabhängige Stellen, dieselben zwei Zahlen.**

⭐ **`0x538808` ist eine Tafel mit 4 Byte je Geländeform** — die vier Eckhöhen
einer Kachel. Sie ist bei uns nirgends verzeichnet. Der Index kommt aus
`0x41D110`; in BC.6 sind **19 Geländeformen** gezählt, `19 · 4 = 76` Byte ab
`0x538808` wären der Umfang.

**`0x4B5280(&spalte, &zeile)`** rechnet zuerst grob
`spalte = kamera_spalte + (maus_x + feinversatz_x)/40`,
`zeile  = kamera_zeile  + (maus_y + feinversatz_y)/20`
und probiert dann `0x4B5150` für `zeile+7, zeile+6, … zeile+0`. ⭐ **Von hinten
nach vorn**, damit bei überlappenden Kachelsäulen die **vordere** gewinnt —
genau das, was ein Höhenfeld in Isometrie braucht. Die **8** ist die maximale
Höhendifferenz, die eine Kachel verdecken kann.

---

### 6. ⭐ Die Zeichengrundformen — `0x4B5C60`, `0x4B6CA0`, `0x4B6D60`, `0x4B6E20`, `0x4B9480`, `0x4BA1C0`, `0x4C7FE0`

Alle benutzen dasselbe Muster: `IDirectDrawSurface::Lock` (Vtable `+0x64`) mit
Wiederholung bei `DDERR_SURFACELOST = 0x8876021C`, schreiben, `Unlock`
(Vtable `+0x80`).

| Funktion | Oberfläche | Was |
|---|---|---|
| `0x4B5C60(x, y, farbe)` | **Primär** `0x540770` | ein Punkt |
| `0x4B6CA0(x, y, länge, farbe)` | Rückpuffer `0x540744` | waagerechte Linie (`rep stosd`) |
| `0x4B6D60(x, y, länge, farbe)` | Rückpuffer | senkrechte Linie |
| `0x4B9480()` | **beide** | mit 0 füllen |
| `0x4C7FE0(x,y,text,zahl,fl)` | die übergebene | Text + Zahl |

⭐ **`dword[0x5387C8]` ist der Zeilenschritt der gerade gesperrten Oberfläche**,
und `0x4B9480` ist die Stelle, die ihn setzt (`mov [0x5387C8], eax` aus
`DDSURFACEDESC.lPitch` bei `+0x10`). Das bestätigt die Bemerkung in AS
(»Zeilenschritt vorübergehend in `dword[0x5387C8]`«) und benennt den Schreiber.
⚠ Die zweite Füllung im selben Aufruf benutzt den eben gesetzten Wert **auch für
den Rückpuffer** — beide Oberflächen müssen denselben Zeilenschritt haben.

⭐ **`0x4B6E20` ist das Auswahlrechteck.** Zwei waagerechte und zwei senkrechte
Linien in **Farbe 255** (`#FFFFFF`, weiss) zwischen `(0xA182E0, 0xA182E4)` und
der Mausposition. Sichtbar nur, wenn `dword[0x502AD4] == 4` **oder**
(`dword[0x502ACC] == 8` **und** `dword[0xA32180] == 2`). Damit sind `0xA182E0/E4`
als **Anfasspunkt des Ziehens** belegt — sie liegen unmittelbar vor der
Tastenzustandstafel `0xA182E8`, die die Fensterprozedur bei `0x412FD2` füllt.

⭐⭐ **`0x4C7FE0` ist mit 79 Rufern die meistgerufene Funktion des Reviers:**

```
0x4C7FE0(x, y, text, zahl, oberfläche):
    puffer = strcpy(text)
    _itoa(zahl, tmp, 10)             # 0x4D9CD0, Basis 10 steht roh da
    strcat(puffer, tmp)
    Lock(oberfläche)
    0x4BA420(x, y, puffer, lpSurface)
    Unlock
```

**Das ist der Zahlenbeschrifter des ganzen Spiels** — jede Anzeige der Form
»Text 123« geht hier durch. Für den Nachbau: es gibt **keine**
Formatzeichenkette, die Zahl wird immer **hinten angehängt**, immer dezimal,
immer vorzeichenbehaftet (`_itoa`, Basis 10).

**`0x4BA1C0` zeichnet ein einzelnes Zeichen**: `0x4BA0A0(c)` bildet ab, dann
`c − 0x20`, und der Zeichensatz liegt bei `0xB26440` mit **131 Byte je Zeichen**
(`c'·64 + c' = c'·65`, dann `c' + 2·65c' = 131c'` — zwei `lea`, eindeutig).

---

### 7. ⭐ Kamera und Randscrollen — `0x4B4A60`, `0x4B4F60`

`0x4B4A60` läuft nur, wenn `byte[0x8B7250]` gesetzt ist — die Globale, die in AS
schon als **»Scrollen aktiv/passiv«** (Einstellung 19) verzeichnet ist. Damit ist
sie hier zum ersten Mal an ihrer Wirkung belegt.

```
maus_x < 2                   -> feinversatz_x -= 0x20
maus_x + 2 > dword[0xB136B0] -> feinversatz_x += 0x20     # Bildschirmbreite
maus_y < 2                   -> feinversatz_y -= 0x10
maus_y + 2 > dword[0x5387CC] -> feinversatz_y += 0x20     # Pufferhöhe
```

⚠ **Der Schritt ist nicht symmetrisch:** nach oben **16**, nach unten **32**.
Waagerecht sind es beide Male 32. Ich habe die vier Stellen einzeln nachgesehen
(`0x4B4A7C`, `0x4B4AA5`, `0x4B4A90`, `0x4B4AC4`) — es steht so da. Fehler oder
Absicht ist am Code nicht entscheidbar.
⚠ Auch die zwei Schranken sind verschiedener Art: waagerecht gegen `0xB136B0`
(Bildschirmbreite), senkrecht gegen `0x5387CC` (Puffer**höhe**).

`0x4B4F60` normalisiert danach: fällt der Feinversatz aus `[0,40)` bzw. `[0,20)`,
wird er durch 40 bzw. 20 geteilt und die Kamera-Kachel um den Quotienten
verschoben. Das ist die Stelle, an der aus »Bildpunkte scrollen« ein Kachelsprung
wird.

---

### 8. ⭐ Klang, Datei, KI, Markt — die Einzelbefunde

**Klang (`0x4C1960`, `0x4C1C10`, `0x4C1C60`, `0x4C1C90`, `0x4C1FF0`).**
Alle greifen über COM-Vtables auf `0xB4BE0C … 0xB4FD14` zu. Die benutzten
Versätze bestimmen die Schnittstelle: **`+0x08` = `Release`**,
**`+0x48` = `IDirectSoundBuffer::Stop`**, **`+0x14` =
`IDirectSound::DuplicateSoundBuffer`**. Nullmodell: in `IDirectDrawSurface` liegt
an `+0x48` `GetOverlayPosition`, was auf einem Feld von rund tausend Zeigern
keinen Sinn ergibt; in `IDirectSoundBuffer` ist `Stop` genau der 18. Eintrag.

* `0xB4BE40 … 0xB4BE8F` = **20 Ausgabekanäle**, Ringzeiger `dword[0x5391F8]`
* `0xB4BE94 … 0xB4FD13` = die **Vorlagepuffer**, je 8 Byte
* `0xB4BE1C … 0xB4BE43` = **5 gesonderte Kanäle**

⭐ **`0x4C1C90(klang, …)` ist der Abspieler:** alten Kanal `Release`,
`DuplicateSoundBuffer(vorlage[klang])` in den Kanal, Ringzeiger weiterzählen.
Damit ist die Kanalzahl **20** belegt — mehr als 20 gleichzeitige Klänge kann das
Original nicht, der 21. wirft den ältesten weg.
⭐ **`0x4C1C10` = alle Klänge anhalten** (5 Rufer) — der Griff bei Pause,
Missionsende und Fensterwechsel.

**`0x4B9ED0(name, puffer, länge) = Datei einlesen.** `fopen(name, "rb")` — die
Betriebsart steht als Zeichenkette bei `0x4F7FD0` und ist nachgelesen —,
`fread(puffer, 1, länge, f)`, Vergleich mit `länge`, `fclose`. Rückgabe 1 nur,
wenn **alle drei** Schritte gelingen.

**`0x4C2220(FILE*) = ist_dateiende`**: `pos = ftell; fseek(f,0,SEEK_END);
ende = ftell; ergebnis = (ende == pos); fseek(f,pos,SEEK_SET)`. ⭐ Das Original
benutzt also **nicht** `feof`, sondern diese Konstruktion — sie meldet das Ende
schon **vor** dem fehlgeschlagenen Lesen, `feof` erst danach. Wer sie durch
`feof` ersetzt, liest einen Satz zu viel.

**`0x4BE9A0(spieler, sx, sy)` = die KI-Bedrohungszahl.** Der Index rechnet sich
im Code zu `((spieler·11 + sx)·11 + sy)·12` — **das ist Zeichen für Zeichen der
in Abschnitt AC verzeichnete sec56-Index `121·Spieler + 11·sx + sy`, und ich habe
ihn aus der `lea`-Kette gewonnen, bevor ich nachgeschlagen habe.** Mit den dort
benannten Feldern (`+0` eigene, `+2` verbündete, `+4` feindliche Stärke):

```
bedrohung = max(0, feindlich − (eigen + verbündet/2)/2) / 50
```

⭐ Zwei Halbierungen: **ein verbündeter Punkt zählt halb so viel wie ein eigener,
und die Summe zählt nochmals halb gegen den Feind.** Die 50 am Ende ist die
Körnung — die KI rechnet in Stufen zu 50 Stärkepunkten. Zwei Rufer, `0x4BEB15`
und `0x4BEC2D`.

**`0x4C1290(marktplatz)` = eine Marktlagereinheit aufstellen.** sec94
(`0x82AA30`, 50 × 78) → Fahrwerk `+0x0B` gegen die Flaggentafel `0x4FA24F`
(in AA schon als »Fahrwerksflaggen« benannt). Ist die Flagge 0, wird
`word[satz + 0x24] = 10000` gesetzt — der »geht nicht«-Wert. Sonst sucht sie den
ersten freien der **500 sec40-Plätze**, trägt ihn bei `+0x24` ein, setzt
`byte[+0x12] = 0` und **nullt 40 Sätze à 13 Byte in sec39** — die Rechnung
`(i + 40·platz)·13` ist die in AA belegte Aufteilung **500 × 40 × 13**.
Unabhängige Bestätigung derselben Zahlen aus einer anderen Funktion.

**`0x4C16E0(einheit)`** durchsucht die 20 Wörter ab `0x9937B8` — die
**Mitnahmeliste zwischen Missionen** (Abschnitt O) — und liefert **1, wenn die
Einheit NICHT darin steht**. Umgekehrte Logik; leicht falsch herum zu bauen.

**`0x4C5BB0` = Netzbefehl `0x3D3` (979).** Sie füllt `0xB8A3D8` mit
`word = 0x3D3` und vier Werten aus `0x5407A0`, `0x540EB8`, `0x54079C`,
`0x540798`, `0x540B94` und springt dann in den Absender. **8 Rufer.** Der Befehl
gehört in `COMMAND_SENDERS.txt` nachgetragen.

**`0x4BADB0` / `0x4BAE40` (sec48, `rob_trans`, 400 × 18).** Beide gehen alle 400
Sätze durch und verlangen: `+0x0A != 0`, `word[+0x0C] / 1000 == spieler`,
`+0x0E != 0`, und eines der vier Bytes `+0x00…+0x03` gleich `a1`.
⭐ Das benennt vier Felder auf einmal: **`+0x00…+0x03` sind vier Ladungsarten**
(passend zu `zdroj0-3` aus der Zeichenkette bei `0x77AC50`), **`+0x0A` ist
zugleich Belegtmarke und Fassungsvermögen** (0 = Satz frei), und **`+0x0C` ist
die Einheitennummer**, aus der sich der Spieler durch `/1000` ergibt — dieselbe
Teilung wie überall sonst.

---

### 9. ⭐⭐ ERLEDIGT: die Palettenplätze 5 / 9 / 13 des Gesundheitsbalkens

Offene Frage **BH.4.2**. Die Palette liegt als `DATA/01.PAL` vor
(776 Byte = 8 Byte Kopf + 768 = 256 × RGB, **volle 8 Bit je Kanal**, nicht 6).

⭐ **Erst geeicht, dann abgelesen** — sonst wäre es Bestätigungssuche: In
Abschnitt A steht »Palettenfarbe 47 = `#13130F`«, aus einer ganz anderen
Untersuchung. Die Datei liefert an Platz 47 genau **`#13130F`**. Und die sechs
Plätze **248…253**, die `Check_pal` im Ring vertauscht (BC.1), sind in der Datei
ein geschlossenes Blau-Band `#274773 … #23436F` — genau die Wasseranimation, die
BC.1 vermutet. Zwei unabhängige Proben treffen. **Die Datei ist die richtige.**

**Die Palette ist in Viererblöcken ab Platz 1 aufgebaut:**

| Block | Plätze | Farbe (hell → dunkel) |
|---:|---|---|
| 0 | 1…4 | **blau** `#536FFF … #1B47B3` |
| 1 | 5…8 | **grün** `#67D75F … #2B8F0B` |
| 2 | 9…12 | **rot** `#FF2B27 … #BF0000` |
| 3 | 13…16 | **gelb** `#F7FF0F … #DF9F0F` |
| 4 | 17…20 | orange |
| 5 | 21…24 | weissgrau |
| 6 | 25…28 | magenta |
| 7 | 29…32 | cyan |

⭐⭐ **Das schliesst BH.2 mit ein:** die Rahmenfarbe `4 + 4·spieler` trifft
`4, 8, 12, 16, 20, 24, 28, 32` — **genau den dunkelsten Platz jedes Blocks.**
Acht Spieler, acht Blöcke, geht auf. Damit ist die Spielerreihenfolge
mitbelegt: **0 blau, 1 grün, 2 rot, 3 gelb, 4 orange, 5 grau, 6 magenta,
7 cyan.**

⭐⭐ **Und damit die Antwort:**

| Bedingung | Platz | Farbe |
|---|---:|---|
| `2·füllung ≥ breite` (≥ 50 %) | **5** | **`#67D75F` — hellgrün** |
| 25 … 50 % | **13** | **`#F7FF0F` — gelb** |
| `4·füllung < breite` (< 25 %) | **9** | **`#FF2B27` — rot** |

**Grün → gelb → rot, in genau der Reihenfolge, in der die Gesundheit fällt.**

Nullmodell: drei aus 256 Plätzen gegriffene Farben ergeben die geordnete Folge
Grün/Gelb/Rot mit weniger als `10⁻⁵`. Zusätzlich ist es kein Zufall, **welche**
Grüne, Gelbe und Rote: es sind je die **hellsten** ihres Blocks (`4k+1`) — drei
Farben derselben Bauart, nicht drei beliebige.

⚠ Ein Vorbehalt, der bleiben muss: das Spiel führt **sechs** Palettenobjekte
(BC.1), die sich in den Plätzen 248…253 unterscheiden. Die Plätze 1…32 sind vom
Ringtausch **nicht** betroffen, der Befund gilt also für alle sechs.

**Und die drei anderen Betriebsarten dazu** (siehe Abschnitt 10):

| Betriebsart | Farbe | Palette |
|---:|---:|---|
| 2 | 3 | `#2B53CB` — mittleres Blau |
| 3 | `0x54` = 84 | `#E3C793` — helles Sandbeige |
| 4 | `0x56` = 86 | `#BBAB87` — dunkleres Sandbeige |

Betriebsart 4 ist die, die `0x4B93F0` für den **Ladebalken** erzwingt — Sand für
Fracht. Passt.

---

### 10. ⭐⭐ ERLEDIGT: `byte[0xA31A88]` steht NICHT in `options.cfg` — es ist die TAB-Taste

Offene Frage **BH.4.4**. Der Schreiber `0x412FF5…0x413010` sitzt **innerhalb der
Tastenverarbeitung**:

```
0x412F97   WM_KEYDOWN-Kopf                      (in AS schon benannt)
0x412FD8   eax = vk − 9 ; wenn > 0x88: verwerfen
0x412FE8   cl  = byte[0x414644 + eax]           Tastenindextafel, 137 Byte
0x412FEE   jmp dword[0x4145A4 + cl·4]           Sprungtafel, 40 Arme
```

Ich habe beide Tafeln ausgelesen: **`0x4145A4[0] == 0x412FF5`**, und der einzige
Eintrag der Bytetafel mit dem Wert 0 steht an Stelle `vk = 0x09`.

⭐⭐ **`VK_TAB` (0x09).** Der Arm tut genau das:

```
byte[0xA31A88] += 1
wenn byte[0xA31A88] == 4:  byte[0xA31A88] = 0
```

**Ein Ringschalter über vier Zustände 0 → 1 → 2 → 3 → 0.**

Gegenprobe in F: `reloc_refs.py` auf `0xA30AE8` (= C `0xA31A88` − `0xFA0`) meldet
**dieselben 22 Fundstellen, dieselben 4 Schreiber, dieselben 18 Leser**, und der
Tastenarm sitzt bei `0x412DC5`. **Beide Bauten gleich.**

⚠⚠ **Drei Folgerungen, die den Nachbau betreffen:**

1. **Betriebsart 4 ist über die Tastatur nicht erreichbar.** Tab zählt nur bis 3.
   Die 4 setzt ausschliesslich `0x4B941A` für den Ladebalken (BH.3, bestätigt).
   Wer Tab bis 4 zählen lässt, färbt die Einheitenbalken sandfarben.
2. **Betriebsart 0 heisst »aus«** — die Gattungsarme in `0x4B71F0` fragen alle
   `cmp bl,1` und für die eigenen Einheiten `cmp bl,2`; bei 0 fällt der Arm in
   den Zweig für 3/4, der Balken wird also **doch** gezeichnet.
3. …und dabei greift der **Vorgabearm** von `0x4B6F60` bei `0x4B70EC`:
   `mov dl, byte ptr [esp+0x10]`. Dieser Stapelplatz hält zu dem Zeitpunkt
   **`Zeilenschritt · (Höhe − 1)`**. ⚠ Die Füllfarbe ist also das niederwertige
   Byte einer Multiplikation. Bei Zeilenschritt 640 wird daraus: Höhe 5 → Farbe
   **0** (`#000000`, schwarz), Höhe 4 und 6 → Farbe **128** (`#4F4F6B`, ein
   dunkles Graublau). **Kein Absturz, aber auch keine gewollte Farbe** — der
   Balken sieht bei Tab-Stellung 0 einfach dunkel aus. Das erklärt, warum es
   nie jemandem aufgefallen ist.

**Nebenbei erledigt: die Betriebsarten 2, 3 und 4** (offene Frage BH.4.1). Die
Sprungtafel `0x4B715C` hat vier Arme, angesprungen mit `byte[0xA31A88] − 1`:

| Wert | Arm | Füllfarbe |
|---:|---|---|
| 1 | `0x4B70B5` | 5 / 13 / 9 nach dem Verhältnis (Abschnitt 9) |
| 2 | `0x4B70E0` | fest **3** |
| 3 | `0x4B70E4` | fest **0x54** |
| 4 | `0x4B70E8` | fest **0x56** |
| 0 oder > 4 | `0x4B70EC` | ⚠ `Zeilenschritt·(Höhe−1) & 0xFF` |

**Und die fünf anderen Gattungsarme** (offene Frage BH.4.3) — alle nach demselben
Muster `0x4B6F60(x, y, breite, höhe, füllung, einheitennummer)`:

| Gattung | Bedingung | x | y | Breite | Höhe | Füllung |
|---:|---|---|---|---|---:|---|
| 0 | `art == 1` | `+0x14` | `+0x14` | `HpMax/4 + 2` | 5 | `Hp/4` |
| 1 | `art == 1` | `+0x0C` | `+0x0A` | `HpMax/4 + 2` | 4 | `Hp/4` |
| 2 | — | — | — | — | — | zeichnet **nichts** |
| 3 | `art == 1` | `+0x23` | `+0x05` | `HpMax/4 + 2` | 6 | `Hp/4` |
| 4 | `art == 1` | `+0x23` | `+0x0F` | `HpMax/4 + 2` | 6 | `Hp/4` |
| 5 | `art == 1` | `+0x23` | `+0x5C` | `HpMax/4 + 2` | 6 | `Hp/4` |

⭐ Die Gattungen 3, 4 und 5 haben zusätzlich einen **zweiten Balken, den nur der
eigene Spieler sieht** (`einheit/1000 == byte[0x4FA284]`): bei `art == 2` aus
`word[+0x2E] / 20` und `word[+0x30] / 20 + 2`, sonst aus `byte[+0x39] >> 2`.
**Was diese zwei Grössen sind, bleibt ungedeutet** — dass sie nur für eigene
Einheiten gezeigt werden, ist der Beleg dafür, dass es Bestands- und nicht
Kampfwerte sind.

---

### 11. ⭐⭐ ERLEDIGT: der vermutete zwölfte Auslieferungsunterschied ist KEINER

Offene Frage **BH.4.5** und der ⚠⚠-Vorbehalt in BH. `cfind.py --diff`, dazu beide
Zerlegungen von Hand nebeneinander:

| C | F | cfind | Befund |
|---|---|---:|---|
| `0x4B6F60` Balkenzeichner | `0x4B6890` | 88,6 % | **Rauschen** |
| `0x4B71F0` alle Balken | `0x4B6B20` | 96,1 % | **Rauschen** |
| `0x4C7990` Zugexplosion | `0x4C7540` | 99,1 % | **Rauschen** |

**Was in den 14 abweichenden Blöcken von `0x4B6F60` tatsächlich steht:**

* **Registerzuteilung.** C hält den Zeilenschritt in `[esp+0x14]` und lädt ihn
  jedes Mal neu; F hält ihn in `edi`. Dieselben Werte, andere Ablage. Das erzeugt
  allein sieben `insert`/`delete`-Blöcke von je 1–2 Befehlen.
* **Vergleichsrichtung.** C: `cmp edx, ebx / jle`; F: `cmp eax, ecx / jge` mit
  vertauschten Operanden. **Dieselbe Aussage.** Zweimal.
* **Befehlsauswahl.** C: `lea ebx,[ebp-1]; test ebx,ebx; jl`;
  F: `dec ecx; js`. Dieselbe Aussage in zwei statt drei Befehlen.
* **Der Schwanz ist die Sprungtafel.** Beide Funktionen enden mit `ret` an
  derselben Stelle im Rumpf; danach zerlegt capstone Daten. In C steht dort die
  Balken-Betriebsartentafel `0x4B715C`, in F `0x4B6A88`.

**Was NICHT drinsteht:** kein zusätzlicher Wächter, keine andere Konstante
(`0x100`, `0x3E8`, `0x28` stehen in beiden), kein fehlender Aufruf, keine andere
Schranke. **Alle Globalen sind dieselben, um genau `0xFA0` verschoben.**

⭐⭐ **Der Vorbehalt in Abschnitt BH kann gestrichen werden: es gibt keinen
zwölften Unterschied an den Balken.**

**Zwei Nebenfunde daraus, beide neu:**

* Die **Sprungtafel der vier Balken-Betriebsarten** liegt in F bei **`0x4B6A88`**
  (C `0x4B715C`), mit denselben vier Armen.
* Die **Sprungtafel der sechs Gattungen** liegt in F bei **`0x4B71F4`**
  (C `0x4B78BC`). Beide ausgelesen, beide sechs Einträge, **beide mit demselben
  Muster**: Gattung 2 zeigt in beiden Bauten auf den Rücksprung.

| Gattung | C-Arm | F-Arm |
|---:|---|---|
| 0 | `0x4B722F` | `0x4B6B5F` |
| 1 | `0x4B732D` | `0x4B6C5D` |
| 2 | `0x4B78B5` *(nichts)* | `0x4B71EF` *(nichts)* |
| 3 | `0x4B7381` | `0x4B6CB1` |
| 4 | `0x4B7481` | `0x4B6DB1` |
| 5 | `0x4B7581` | `0x4B6EB1` |

⚠⚠ **Eine Warnung zum Werkzeug, die teuer war:** `cfind` meldet für `0x4AF1C0`
**74 %** bei 23 Befehlen. Der `--diff` zeigt: C und F enthalten **dieselben 23
Befehle mit denselben Konstanten**, nur sind die zwei unabhängigen Hälften
(Byteindex `sar 3` und Bitmaske `and 7`) **in der anderen Reihenfolge** gerechnet.
Ein reines Umsortieren erzeugt bei einem kurzen Rumpf zwei Blöcke à 6 Befehlen
und damit einen dramatisch aussehenden Prozentwert. **Bei Funktionen unter etwa
40 Befehlen ist die Prozentzahl praktisch aussagelos.** Dasselbe bei `0x4B0130`
(88 %, 17 Befehle): C schreibt `push esi; and eax,0xff`, F `push ebx; xor eax,eax`
— dieselben 17 Befehle, andere Reihenfolge.

---

### 12. ⭐ ERLEDIGT: `0x4BFE2D` ist kein Funktionsanfang

`aere.py fs 0x4BFE2D` liefert **`0x4BFB80`**, und `rufer.py 0x4BFE2D` findet genau
einen Eintrag: **`0x4BFDA6 jmp`** — einen funktionsinternen Sprung. `0x4BFE2D`
ist ein Sprungziel im Rumpf von `0x4BFB80`. Die Vermutung des Auftraggebers
stimmt. **Der Eintrag gehört aus der Liste der 25 »in F fehlenden« Funktionen
gestrichen**; wahrscheinlich gilt dasselbe für weitere der 25 mit krummen
Adressen — die Probe ist billig (`aere.py fs`) und sollte für alle gelaufen sein,
bevor jemand aus der Zahl 25 etwas folgert.

---

### Berichtigungen an bestehenden Dokumenten

1. ⚠⚠ **Abschnitt BH, der ⚠⚠-Vorbehalt zu den zwei Balkenfunktionen:** aufgelöst
   als Übersetzerrauschen (Abschnitt 11). Kein zwölfter Unterschied.
2. ⚠⚠ **BH.4.4:** `byte[0xA31A88]` steht **nicht** in `options.cfg` — es ist der
   TAB-Ringschalter (Abschnitt 10). Die Frage kann geschlossen werden.
3. ⚠⚠ **BH.4.2:** die Farbplätze sind bestimmt — 5 grün, 13 gelb, 9 rot
   (Abschnitt 9).
4. **BH.4.1 und BH.4.3:** die vier Betriebsarten und die fünf übrigen
   Gattungsarme sind gelesen (Abschnitt 10, zweite Hälfte).
5. ⚠⚠ **AW.3/AW.4, sec22-Satzbau »x, y, ·, ·, Linie«:** `+2` ist kein
   Platzhalter, sondern trägt **denselben Code wie sec20**, mit `0xFF` als
   Leermarke. Vier Leser im Revier, alle mit der Schranke 100.
6. ⚠ **Die Liste der 25 in F fehlenden Funktionen:** mindestens `0x4BFE2D` ist
   ein Artefakt (Abschnitt 12).
7. ⚠ **`cfind`-Prozentwerte unter etwa 40 Befehlen** sind kein Mass; blosses
   Umsortieren unabhängiger Befehlsgruppen drückt sie auf 74 % (Abschnitt 11).
8. **Neu zu ergänzen:** `dword[0x5387C8]` wird von `0x4B9480` gesetzt (bisher war
   nur die Verwendung bekannt); `0x538808` ist eine Tafel mit 4 Byte Eckhöhen je
   Geländeform; `byte[0x8B7250]` (»Scrollen aktiv«) wirkt in `0x4B4A60`;
   Netzbefehl `0x3D3` wird von `0x4C5BB0` abgesetzt; `0xA182E0/E4` ist der
   Anfasspunkt des Auswahlrechtecks.

---

### Bauaufgaben, die daraus folgen

1. ⭐⭐ **Die Streckensuche nachbauen** (Abschnitt 1). Wir haben `RailLine` mit
   `delka` und `Steps`, aber **niemand erzeugt sie**. Ohne `0x4AF4C0` und seine
   sechs Helfer kann der Spieler keine neue Linie legen. Erforderlich: das
   Halbzeilengitter (257 × 513), die 6-Nachbar-Tafel mit Zeilenparität, die
   Wellenfarben 8…255 mit Umlauf, die Warteschlange zu 2000 Plätzen.
2. ⭐ **Die Trefferprüfung nachbauen** (Abschnitt 5). 40 × 20 Kachel, 15 Punkte
   je Höhenstufe, Suche über 8 Zeilen **von hinten nach vorn**, Eckhöhentafel
   `0x538808`. Ohne die »von hinten nach vorn«-Reihenfolge klickt man bei Hügeln
   auf die falsche Kachel.
3. ⭐ **Die TAB-Betriebsart** (Abschnitt 10). Vier Zustände, Umlauf 0→1→2→3→0,
   Balkenfarben 5/13/9 bzw. 3, 84, 86. **Nicht** bis 4 zählen.
4. ⭐ **Der Entwurfsnachzieher** (Abschnitt 4): wird ein Entwurf geändert,
   übernehmen **nur unbeschädigte** Einheiten (`Hp == HpMax`) den neuen Wert.
   Und: neue Entwürfe füllen die Plätze **von 40 abwärts**.
5. ⭐ **Die Vorbelegung der Entwurfstafeln**: Spieler 0 füllen, dann siebenmal
   kopieren — nicht acht Spieler getrennt laden.
6. **Der Klangring hat 20 Kanäle**; der 21. gleichzeitige Klang wirft den
   ältesten weg. Unbegrenztes Mischen weicht hörbar ab.
7. **Randscrollen**: 32 Punkte waagerecht, **16 nach oben, 32 nach unten**
   (Abschnitt 7) — die Asymmetrie ist im Original; zunächst übernehmen und erst
   bei einem Prüfstandsbefund ändern.
8. **`0x4C2220` statt `feof`** beim satzweisen Einlesen (Abschnitt 8).
9. **Nicht nachbauen:** `0x4B0180` (Ergebnis verworfen) und `0x4AF260`
   (Prüfbild).
10. **Nachtragen:** Netzbefehl `0x3D3` in `COMMAND_SENDERS.txt`.

---

### Was ungedeutet bleibt

1. ⚠ **`sec34 +0x08 … +0x0B`** — vier Bytes, einzeln über `0x4B0220` von aussen
   setzbar, danach immer `0x44FD70(linie)`. Dass es Bedienelemente sind, ist
   sicher (eine Sprungtafel mit vier Armen für vier Feldnummern); **was** sie
   bedeuten, nicht. Der Rufer liegt ausserhalb des Reviers.
2. ⚠ **Die zwei Werte der Betriebsart 2** (`word[+0x2E]`, `word[+0x30]`, beide
   durch 20 geteilt) und `byte[+0x39]` der Arten 3/4 — nur für eigene Einheiten
   gezeigt, Bedeutung offen.
3. ⚠ **`0x4B0820`** (480 B, 3 Rufer): sucht ein Rechteck von 10 Spalten × 6
   Zeilen um ein sec3-Gebäude ab. Der Wert `index − 0x15A0` in einem lokalen Wort
   ist ungedeutet; `0x15A0 = 5536` passt zu keiner bekannten Basis.
4. ⚠ **`0x4C00C0`** (272 B): läuft ein Fenster von 15 Zeilen über sec90/sec91
   (`0xB49E50` / `0xB4A0D0`) und klemmt gegen die Kartenhöhe. Zu sec89/sec90
   steht in BH.8 ein ungeklärter Widerspruch — die Funktion gehört dorthin, ich
   kann ihn nicht auflösen.
5. ⚠ **`0x4C0A30`** summiert `0x4C0890(0…49)`, **`0x4C0E00`** würfelt zweimal mit
   `0x4C0860` und ruft bei `wurf + 2 ≤ byte[0x53904C]` die Funktion `0x4C0D20`.
   Beide gerufenen Funktionen liegen ausserhalb; ohne sie ist weder die Zahl 50
   noch die Schwelle `0x53904C` deutbar.
6. ⚠ **`0x4B27D0`**: `sec19[+0x19] · 100 / sec19[+0x1A]` — eine Prozentrechnung
   an der Luftwaffentafel. Welche zwei Grössen, ist offen.
7. ⚠ **Die fünf gesonderten Klangkanäle** `0xB4BE1C … 0xB4BE43` — sie werden
   getrennt gestoppt und freigegeben. Sprache? Musik? Nicht entschieden.
8. ⚠ **`0x4BA1C0`, 131 Byte je Zeichen** — die Aufteilung dieser 131 Byte
   (Breite + Bitmuster?) habe ich nicht zerlegt; `font_export.py` weiss
   vermutlich mehr.
9. ⚠ **`byte[0xA68E64]`** (der zuletzt gegangene Schrittcode) und
   **`word[0xA68E60]`** (Warteschlangenkopf) sind belegt, aber wer `0xA68E64`
   liest, habe ich nicht verfolgt.
10. ⚠ **Das asymmetrische Randscrollen** (16 gegen 32) — Fehler oder Absicht, am
    Code nicht entscheidbar.

---

## BP. Revier 8: 0x4C8100 … 0x4D5FD0

66 Funktionen, 9 632 Byte. Gelesen am 22.08.2026.
Bestand **C** = `…/opencode/aekernel/GAME.EXE` (22.01.1998),
Bestand **F** = `F:\Akte Europa\GAME.EXE` (16.09.1997).

**Kurzfassung der drei tragenden Funde:**

1. ⭐⭐ **Die Grenze zur C-Laufzeitbibliothek liegt nicht bei `0x4D6000`, sondern
   bei `0x4D6A00`.** Dazwischen stehen **sechs Spielfunktionen mit 2 416 Byte**,
   die `funktionen.py` bisher abgezogen hat. Beleg in Abschnitt 1 — mit Zahlen,
   Nullmodell und dem Gegenbeleg, dass die vom Auftrag vorgeschlagene
   Abstandsmethode hier **nicht** trägt.
2. ⭐⭐ **Die offene Frage BB.8.2 ist beantwortet.** Die Felder `+0`/`+1` im
   22-Byte-Satz der Infanteriezellen (`0x7847E8`) sind **Anzahl** und
   **`0xFE − alter sec6-Wert`**. Damit ist der Test `byte[Satz+1] == 0` in der
   Passierbarkeitskarte gedeutet: »die Zelle war vorher **freier Boden**«.
3. ⭐ **Drei der »Vorbereitungsstücke in `Search:`« (BB.8.4) sind gelesen:**
   `0x4D3390` = 2×2-Platz für Landfahrzeuge, `0x4D35C0` = 2×2-Platz für Schiffe,
   `0x4D3700` = 4×4-Platz für grosse Schiffe.

---

### Adresstafel

Alle F-Adressen mit `cfind.py` nachgeschlagen (nicht gerechnet). Spalte »Ruf« =
Zahl der **tatsächlichen** Rufstellen (relative `call`/`jmp` auf die Funktion
**oder ihren Thunk**, plus relozierte Zeiger) — nicht die Spalte aus
`revier8.txt`. ⚠ **17 Funktionen haben null Rufstellen**; siehe Abschnitt 11.

| C | F | Byte | Ruf | Was sie ist |
|---|---|---:|---:|---|
| `0x4C8100` | `0x4C7CB0` | 176 | 11 | DirectDraw: **Zahl** auf eine Oberfläche schreiben (`itoa` → `Lock` → Textausgabe → `Unlock`) |
| `0x4C81B0` | `0x4C7D60` | 144 | 7 | dasselbe für eine **fertige Zeichenkette** |
| `0x4C8240` | `0x4C7DF0` | 96 | 2 | **Dateinamen aus einem Pfad lösen** (letzter `\`, in sich verschoben) |
| `0x4C82A0` | `0x4C7E50` | 32 | 1 | `GlobalMemoryStatus(&…)`, Satz bei `0xB97AB0` |
| `0x4C82C0` | `0x4C7E70` | 320 | **0** | `LoadImageA` (Ressource, sonst Datei) + `GetObjectA` — Bitmapladen |
| `0x4C8840` | `0x4C83F0` | 288 | 1 | ⭐ **Rohfarbwert einer Farbe im Oberflächenformat bestimmen** (Pixel setzen, roh zurücklesen, Pixel wiederherstellen) |
| `0x4C8960` | `0x4C8510` | 64 | **0** | `IDirectDrawSurface::SetColorKey(DDCKEY_SRCBLT, …)` mit diesem Rohwert |
| `0x4C89A0` | `0x4C8550` | 176 | 1 | ⭐ `OPENFILENAME` **grundstellen** — Filter »**CW Map File (\*.CWM)**«, Vorgabeendung `cwm` |
| `0x4C8A50` | `0x4C8600` | 64 | 1 | ⭐ **`GetOpenFileNameA`, Titel »Load Map«** |
| `0x4C8A90` | `0x4C8640` | 64 | 1 | ⭐ **`GetSaveFileNameA`, Titel »Save Map«** |
| `0x4C9D50` | `0x4C9900` | 464 | 4 | ⭐ **Gebäude an einer Zelle abreissen** (10×6-Fussabdruck, Zellen auf `0xFFFE`) |
| `0x4C9F20` | `0x4C9AD0` | 128 | **0** | ⭐ **sec4-Objekt setzen/anzünden** (Marker anlegen, Kachel stempeln) |
| `0x4C9FA0` | `0x4C9B50` | 32 | **0** | `MessageBeep(-1)` **300-mal** |
| `0x4CA570` | `0x4CA120` | 144 | 4 | ⭐ sec4-Objekt **Stufe 1**: Kachel +1, Zustand := `rand()%106 + 150`, `+4 := 1` |
| `0x4CA600` | `0x4CA1B0` | 336 | 1 | ⭐ **Löschtrupp**, 3×3-Umkreis (schon als »`0x4CA610`« in AL.3 notiert) |
| `0x4CA750` | `0x4CA300` | 144 | 1 | ⭐ sec4-Objekt **Stufe 2 / Ende**: Kachel +2, Zustand := 0, Zelle wird `0xFFFE` |
| `0x4CAAC0` | `0x4CA670` | 80 | 2 | »**brennt auf dieser Zelle Wald?**« (sec6 ∈ [50000, 56000) ∧ sec18-Zustand == 1) |
| `0x4CAC20` | `0x4CA7D0` | 48 | **0** | freien **sec18**-Platz suchen (6000 Sätze, `+2 == 0`) |
| `0x4CAE30` | `0x4CA9E0` | 624 | 2 | ⭐ **Brücke zeichnen** — Bauphase = `(500 − word[Brücke+0x16]) / 167` |
| `0x4CBB80` | `0x4CB750` | 112 | 2 | ⭐ **Rampe zeichnen** — Bauphase = `(200 − byte[Rampe+3]) / 67`, Kachel `10723 + Richtung + 8·Phase` |
| `0x4CBC90` | `0x4CB860` | 256 | 1 | ⭐ **Rampenrichtung** (Vorbedingung: Zelle ist `0xFFFD`) |
| `0x4CBD90` | `0x4CB950` | 336 | 2 | ⭐ **Rampenrichtung**, weichere Vorbedingung (eigene Infanteriezelle **oder** `0xFFFD`) |
| `0x4CC000` | `0x4CBBB0` | 160 | 40 | ⭐ **2×2-Eckcode aus sec2** (die vier Eckhöhen einer Kachel → 4 Bit, `0xFF` wenn eine > 1) |
| `0x4CC0A0` | `0x4CBC50` | 336 | 16 | ⭐ **Brückenkopf prüfen**: Zelle frei/eigene infy/`0xFFFD` **und** Eckcode ∈ {3,12,10,5} **und** Richtung stimmt |
| `0x4CC1F0` | `0x4CBDA0` | 64 | **0** | **Brücke an (Spalte, Zeile) suchen** — 100 Plätze, `+0x12 != 0` = belegt |
| `0x4CC230` | `0x4CBDE0` | 80 | 18 | ⭐ **Zelle räumen**: `Zasah(40200, sec6[x,y])`, vorher `(x,y)` nach `0x53C930`/`0x53C934` |
| `0x4CEC20` | `0x4CE7D0` | 160 | 1 | ⭐ **alle Verlegungsfahrten abbrechen, die zu diesem Gebäude wollen** |
| `0x4CECC0` | `0x4CE870` | 160 | 1 | ⭐ **Takt der Verlegungsfahrten** — jeden **zweiten** Takt genau **eine** von 200 |
| `0x4CF020` | `0x4CEBC0` | 224 | 2 | ⭐ **Hangcode, 8 Formen**: {3,12,10,5} (Kanten) + {4,8,2,1} (Ecken) |
| `0x4CF5A0` | `0x4CF140` | 32 | 1 | **Kameramitte, Spalte** = `Sichtbreite/2 + Kamera-X` |
| `0x4CF5C0` | `0x4CF160` | 32 | 1 | **Kameramitte, Zeile** = `Sichthöhe/2 + Kamera-Y` |
| `0x4CF600` | `0x4CF1A0` | 16 | **0** | `byte[0x4F8D68]` — **Wind X** |
| `0x4CF610` | `0x4CF1B0` | 16 | **0** | `byte[0x4F8D6C]` — **Wind Y** |
| `0x4CF620` | `0x4CF1C0` | 32 | **0** | ⭐ `verbuendet(p, q)` = `byte[0x87B155 + 40·p + q]` |
| `0x4D05E0` | `0x4D0190` | 32 | **0** | Weiterleitung auf `0x447500` (Flugzeugtafel) |
| `0x4D0720` | `0x4D02B0` | 32 | **0** | `sec75[n] := 0xFF`, gibt den alten Wert zurück |
| `0x4D0740` | `0x4D02F0` | 32 | **0** | **Einheitenfeld `+0`** lesen (`0x6E26C8 + 78·n`) |
| `0x4D0760` | `0x4D0310` | 32 | **0** | **Einheitenfeld `+1`** lesen |
| `0x4D0800` | `0x4D03B0` | 16 | 1 | `dword[0xA9A1D8] += n` (sec74) |
| `0x4D0AA0` | `0x4D0650` | 48 | 1 | ⭐ **`terra_place` leeren** — 50 Sätze zu 6 B ab `0xBC6D40`, `+0 := 0` |
| `0x4D10F0` | `0x4D0CA0` | 112 | **0** | 2×2-Feinraster: ruft `0x4B5C60(2·Zeile+j, 2·Spalte+i, Wert)` |
| `0x4D1160` | `0x4D0D10` | 16 | **0** | ⚠ **leere Funktion** (`ret`) — in beiden Bauten |
| `0x4D22F0` | `0x4D1E80` | 128 | **0** | ⭐ **Diagonalfreigabe der Passierbarkeitskarte** (`0xBCA0E8`) |
| `0x4D3270` | `0x4D2E00` | 80 | 2 | ⭐ **Wegsuchauftrag stornieren** — Ring `0xBDA0E8`, Art-Byte `0xBDA8C0` := `0xFF` |
| `0x4D3390` | `0x4D2F20` | 560 | 2 | ⭐ **2×2-Platz frei für ein Landfahrzeug** |
| `0x4D35C0` | `0x4D3150` | 320 | 2 | ⭐ **2×2-Platz frei für ein Schiff** |
| `0x4D3700` | `0x4D3290` | 272 | 5 | ⭐ **4×4-Platz frei für ein grosses Schiff** |
| `0x4D4EA0` | `0x4D4A30` | 112 | 10 | ⭐ **Filmabspieler herunterfahren** (`winplay.dll!Player_ShutDown*`) |
| `0x4D4F10` | `0x4D4AA0` | 96 | 1 | ⭐ **CD-Gerät öffnen** (`MCI_OPEN`, Gerätetyp »cdaudio«) |
| `0x4D4FA0` | `0x4D4B30` | 96 | 1 | **Länge eines CD-Titels** (`MCI_STATUS`, `MCI_STATUS_LENGTH`, `MCI_TRACK`) |
| `0x4D5000` | `0x4D4B90` | 192 | 6 | ⭐ **CD-Titel abspielen** (`MCI_SET` TMSF, dann `MCI_PLAY` von Titelanfang bis Titelende) |
| `0x4D5100` | `0x4D4C90` | 80 | 1 | `MCI_STATUS_READY` |
| `0x4D5150` | `0x4D4CE0` | 80 | 3 | ⭐ **»spielt die CD gerade?«** (`MCI_STATUS_MODE == 526 = MCI_MODE_PLAY`) |
| `0x4D51A0` | `0x4D4D30` | 80 | 4 | `MCI_STATUS_NUMBER_OF_TRACKS` |
| `0x4D51F0` | `0x4D4D80` | 80 | 1 | `MCI_STATUS_CURRENT_TRACK` |
| `0x4D5240` | `0x4D4DD0` | 208 | 1 | ⭐ **MIDI-Stücke zählen**: `0.mid`, `1.mid`, … bis zur ersten fehlenden, höchstens 200 |
| `0x4D5310` | `0x4D4EA0` | 384 | 4 | ⭐ **MIDI-Stück abspielen**: `<Kurzpfad>\<n>.mid`, `n == 0xFF` → Zufallsstück |
| `0x4D55C0` | `0x4D5150` | 64 | 1 | ⭐ **`MM_MCINOTIFY` (0x3B9) + `MCI_NOTIFY_SUCCESSFUL`** → zufälliges Folgestück |
| `0x4D5750` | `0x4D52E0` | 48 | 1 | freien **sec4**-Platz suchen (2000 Sätze, `+2 == 0xFF`), sonst 1999 |
| `0x4D5780` | `0x4D5310` | 176 | 1 | ⭐ **Karte leeren** (sec6, Aufdeckung, sec52, sec4-Marker, 255 Gebäudeplätze) |
| `0x4D5D60` | `0x4D58F0` | 80 | 1 | **Gebäude auf Anfangszustand** — `Gebäude+6 := byte[0xBB41A0 + 10·Typ]` |
| `0x4D5DB0` | `0x4D5940` | 16 | 14 | ⚠ **gibt immer 0 zurück** (`xor eax,eax; ret`) — in beiden Bauten |
| `0x4D5DC0` | `0x4D5950` | 368 | 1 | ⭐ **Hangausgleich**: 4 gerade Nachbarn max. 1 Stufe, 4 schräge max. 2 Stufen |
| `0x4D5F30` | `0x4D5AC0` | 48 | **0** | ⭐ **Planieren anstossen** (Liste leeren, Zelle anheben, nachzeichnen) |
| `0x4D5F60` | `0x4D5AF0` | 112 | 3 | ⭐ **Zelle um eine Stufe anheben** (Stopp bei Höhe 7), Zelle in die Liste |
| `0x4D5FD0` | `0x4D5B60` | 192 | 2 | ⭐ **die geänderten Zellen samt 8 Nachbarn nachzeichnen** |

⚠ **Der C→F-Abstand ist in diesem Revier NICHT konstant**: `−0x450` von
`0x4C8100` bis `0x4CF020`, `−0x430` bei `0x4CBB80`, `−0x440` bei `0x4CBD90`,
`−0x460`/`−0x480` bei `0x4CF5A0`/`0x4CF5C0`, `−0x450`…`−0x470` im `0x4D0…`-Bereich,
ab `0x4D22F0` durchgehend `−0x470`. **Wer die F-Adresse rechnet, rechnet falsch.**

---

### 1. ⭐⭐ Die Grenze zur C-Laufzeitbibliothek gehört auf `0x4D6A00`

`funktionen.py` zieht ab `0x4D6000` alles als MSVC-Bibliothek ab (341
Funktionen). **Das ist um 2 416 Byte zu früh.**

#### 1.1 Der Befund

Der Rumpf der letzten Funktion meines Reviers, `0x4D5FD0`, endet erst bei
**`0x4D6067`** — sie läuft also schon über die angenommene Grenze. Danach
folgen, im `int3`-Raster sauber getrennt:

| Anfang | Byte | was sie ist |
|---|---:|---|
| `0x4D6090` | 112 | Randprüfung gegen Kartenbreite/-höhe, Helfer von `0x4D6100` und `0x4D67C0` |
| `0x4D6100` | 880 | Kachel neu bestimmen (aus `0x4D5FD0` gerufen, dreimal) |
| `0x4D6470` | 160 | schreibt die Zellenliste `0xC40160` |
| `0x4D6510` | 112 | Einstiegspunkt: ruft `0x4D6580`, `0x4D6470` **und `0x4D5FD0`** |
| `0x4D6580` | 576 | benutzt dieselben Nachbartafeln `0x53A290`/`0x53A2A0`/`0x53A2B0` |
| `0x4D67C0` | 576 | Kartenmass-Schleife über `0x542DF8`/`0x542DC4` |
| **`0x4D6A00`** | **557** | ⭐ **die Einfuhrsprungtafel** — 90 × `jmp dword ptr [0xC65xxx]` |
| `0x4D6C2D` … | | ab hier Laufzeitbibliothek (`0x4D6C70` = `rand`, `0x4D6D90` = `fclose`, `0x4D7040` = `fopen`, `0x4D73C0` = `fread` — alle vier schon bekannt) |

#### 1.2 Der Beleg mit Zahl und Nullmodell

Ich habe für **jede** relozierte Stelle im `.text` gezählt, wohin ihr Dword
zeigt: in den `.data`-Bereich (Spielstand) oder in die `.idata` (Einfuhrtafel).

| Bereich | Funktionen | `.data`-Verweise | `.idata`-Verweise |
|---|---:|---:|---:|
| `0x4D6090` … `0x4D69FF` (strittig) | 6 | **37** | **0** |
| `0x4D6A00` … `0x4D6C2C` | 2 | 0 | **90** |
| `0x4D6C2D` … `0x4D7B02` (sichere Bibliothek) | 34 | 24 | 3 |

Und die 37 Verweise der strittigen sechs zeigen **ausnahmslos** auf
Spielstandsglobale, die in der Ladertafel (Abschnitt Y) stehen oder schon
benannt sind:

* `0x542DC4` / `0x542DF8` — Kartenbreite / Kartenhöhe (10 Verweise)
* `0x53A290` / `0x53A2A0` / `0x53A2B0` / `0x53A2D0` — die Nachbartafeln, die
  auch `0x4D5DC0` und `0x4D5FD0` benutzen (8 Verweise)
* `0xC40150` / `0xC40154` / `0xC40158` / `0xC40160` … — die Arbeitsliste der
  Geländeänderung, die `0x4D5F60` füllt (17 Verweise)
* `0xC5D620` (2 Verweise)

**Nullmodell:** eine aus einer MSVC-Bibliothek gelinkte Funktion kann keine
Adresse aus der Ladertafel dieses Spiels kennen. Von den 34 sicheren
Bibliotheksfunktionen dahinter benutzt **keine einzige** eine dieser Globalen.
Die Wahrscheinlichkeit, dass sechs Bibliotheksfunktionen zufällig genau die
Nachbartafel und die Arbeitsliste der Planierung ansprechen, ist praktisch null.

Dazu kommt die **Rufkante**: `0x4D6510` ruft `0x4D5FD0` (Spielcode), und
`0x4D5FD0` ruft `0x4D6100` (dreimal). Die Grenze läuft also **quer durch eine
zusammenhängende Mechanik**.

#### 1.3 ⚠ Die vorgeschlagene Abstandsmethode trägt hier NICHT

Der Auftrag schlug vor, die Grenze am **gleichmässigen C→F-Abstand** zu
erkennen. Ich habe das mit `cfind.py` (Vorabgrenze auf `0x4F0000` hochgesetzt)
über den ganzen Schwanz gemessen — **die Methode trennt hier nichts:**

* Der Abstand ist **`−0x470` durchgehend von `0x4D411E` bis zum Ende des
  `.text` bei `0x4E9C6C`** — also für den letzten Spielcode **genauso** wie für
  die ganze Bibliothek.
* 154 kleine Bibliotheksfunktionen sind ausserdem **mehrdeutig** (alle bilden
  auf denselben F-Kandidaten `0x4D683C` ab), also gerade dort, wo die Methode
  greifen sollte, liefert sie Rauschen.

Der Grund ist einfach: der Binder legt Spielcode und Bibliothek **hintereinander
in dasselbe `.text`**, und der Versatz der beiden Bauten ändert sich nur da, wo
sich die Grösse eines vorangehenden Stücks geändert hat. Das ist im ganzen
letzten Zehntel nicht mehr der Fall. **Der Verweisinhalt trennt, der Abstand
nicht.**

#### 1.4 Was das für die Messlatte heisst

`funktionen.py` meldet heute: 1 107 Spielfunktionen, **861 488 Byte**, davon
775 616 gelesen = 90,0 %.

Mit der berichtigten Grenze kommen **2 416 Byte in 6 Funktionen** dazu
(`0x4D6090` … `0x4D69FF`), die alle **ungelesen** sind:

* Nenner: 861 488 → **863 904** Byte
* Zähler bleibt 775 616 → **89,8 %** statt 90,0 %
* Stückzahl: 1 107 → **1 113**

**Bauaufgabe am Werkzeug:** in `funktionen.py` Zeile 170 und in `cfind.py`
`CRT_C` den Wert `0x4D6000` durch **`0x4D6A00`** ersetzen. ⚠ Die Einfuhrtafel
`0x4D6A00`…`0x4D6C2C` (557 B, 90 Einträge) ist Binderglibber und sollte **auch
dann nicht** als Spielcode zählen — sie liegt aber genau auf der neuen Grenze,
also ist `0x4D6A00` der richtige Schnitt.

#### 1.5 ⭐ Nebenertrag: die vollständige Einfuhrtafel

Aus der Tafel bei `0x4D6A00` fällt heraus, womit das Spiel wirklich arbeitet —
darunter **zwei bisher nicht benannte Fremdbibliotheken**:

* `winstr.dll` — `Movie_GetXSize`, `Movie_GetYSize`, `Movie_GetSoundRate`,
  `Movie_GetSoundChannels`, `Movie_GetSoundPrecision`, `Movie_SetSyncAdjust`,
  `Movie_GetTotalFrames`, `Movie_GetCurrentFrame`
* `winplay.dll` — `Player_InitMovie`, `Player_InitSoundSystem`,
  `Player_InitSound`, `Player_InitVideoSystem`, `Player_InitVideo`,
  `Player_InitPlaybackMode`, `Player_InitMoviePlayback`, `Player_MapVideo`,
  `Player_StartTimer`, `Player_PlayFrame`, `Player_StopTimer`,
  `Player_ReturnPlaybackMode`, `Player_ShutDownSound`, `Player_ShutDownVideo`,
  `Player_ShutDownMovie`, `Player_ShutDownVideoSystem`,
  `Player_ShutDownSoundSystem`
* dazu `DPLAYX.dll!#1` / `#2` (DirectPlay, nur die zwei Ordinale),
  `DSOUND.dll!DirectSoundCreate`, `DDRAW.dll!DirectDrawCreate`,
  `WINMM.dll!mciSendCommandA` / `mciSendStringA` / `timeGetTime`,
  `comdlg32.dll!GetOpenFileNameA` / `GetSaveFileNameA`.

Damit ist die in **BB.7** notierte Beobachtung »`0x4D49F0` ist ein
Filmabspieler« **namentlich belegt**: `0x4D4EA0` (mein Revier, 10 Rufer aus
`0x4D49F0`) ist genau die Abbruchkette
`Player_StopTimer` → `Player_ReturnPlaybackMode` → `Player_ShutDownSound` →
`Player_ShutDownVideo` → `Player_ShutDownMovie` → `Player_ShutDownVideoSystem`
→ `Player_ShutDownSoundSystem`, davor `SetCursor(0)` und `dword[0xBDEA64] := 0`.

---

### 2. ⭐⭐ BB.8.2 ist beantwortet: die Felder `+0`/`+1` der Infanteriezellen

**Die offene Frage** (OFFENE_FRAGEN, BB.8.2): der 22-Byte-Satz bei `0x7847E8`
wird in der Passierbarkeitskarte mit `byte[Satz+1] == 0` auf »begehbar«
geprüft; `zz_deutung.py` deutete den Satz als »col, row, 9 × u16«. Eine der
beiden Lesungen musste falsch sein, »der Vergeber `0x433A50` wurde nicht
aufgemacht«.

Meine Funktion `0x4CC0A0` (Brückenkopf) und `0x4D3390` (Landplatz) lesen beide
diesen Satz, also habe ich den Vergeber aufgemacht.

#### `0x433A50(einheit, spalte, zeile)` — »**infy** anlegen«

```
if sec6[spalte·256 + zeile] < 14000:        Fehler "Wrong place to create new 'infy'"
i = erster Satz ab 0x7847E8 (Schritt 22) mit byte[+0] == 0
if i > 3998:                                Fehler "Out of INFY space"
alt = byte[ &sec6[…] ]                      ; das NIEDERE Byte des alten Zellenwerts
byte[0x7847E8 + 22i + 2] = spalte
byte[0x7847E8 + 22i + 3] = zeile
byte[0x7847E8 + 22i + 1] = 0xFE − alt       ; <<< HIER
byte[0x7847E8 + 22i + 0] = 1                ; Anzahl Mann
word[0x7847E8 + 22i + 4] = einheit          ; erster Mann
dword[+6] = dword[+10] = dword[+14] = dword[+18] = 0xFFFFFFFF
sec6[spalte·256 + zeile] = 10000 + i
```

**Damit steht die Satzform fest:**

| Versatz | Grösse | Bedeutung |
|---|---|---|
| `+0` | u8 | **Anzahl Mann** (1…9) |
| `+1` | u8 | ⭐ **`0xFE − (alter sec6-Wert & 0xFF)`** — der gemerkte Untergrund |
| `+2` | u8 | Spalte |
| `+3` | u8 | Zeile |
| `+4` … `+21` | 9 × u16 | Einheitennummern, `0xFFFF` = leer |

**Die Zahl, die es belegt:** der alte Zellenwert kann nur `0xFFFE` (frei),
`0xFFFD` (rau) oder `0xFFFC` (Wasser) sein — anders käme man nicht durch die
Eingangswache `< 14000`. `0xFE − 0xFE = 0`, `0xFE − 0xFD = 1`, `0xFE − 0xFC = 2`.
Also:

| `byte[Satz+1]` | Untergrund vor der Zelle |
|---:|---|
| **0** | **frei** (`0xFFFE`) |
| 1 | rau (`0xFFFD`) |
| 2 | Wasser (`0xFFFC`) |

⭐ Damit ist der Test `byte[Satz+1] == 0` in den Passierbarkeitsarten 0, 4, 8
gedeutet: **»die Infanteriezelle steht auf freiem Boden, also darf ein
Fahrzeug/Fussgänger da durch«**. `zz_deutung.py`s Deutung »col, row« stimmt —
sie sitzt nur **zwei Byte weiter rechts**, bei `+2`/`+3`.

**Nullmodell:** die Zuordnung ist keine Statistik, sondern ein arithmetischer
Beweis aus drei möglichen Eingangswerten. Der Nachbau darf sich jetzt darauf
stützen.

#### Nebenbei: `0x433B50(einheit, zellenindex)`

Läuft die neun Wortplätze `+4`…`+21` durch und gibt 1 zurück, wenn `einheit`
darunter ist. **Das ist NICHT »befreundet«**, sondern »**ist genau diese
Einheit in dieser Zelle**«. Meine beiden Rufer (`0x4CC0A0`, `0x4CBD90`) prüfen
davor `byte[Satz+0] == 1` — also »**in der Zelle steht genau ein Mann, und der
bin ich**«. Für Brücken- und Rampenbau heisst das: der Pionier darf auf seiner
eigenen Uferkachel stehen, sonst niemand.

⚠ **Berichtigung:** OFFENE_FRAGEN Zeile 5388 nennt `0x433B50` »`pratelska_infa`«
und Zeile 5392 deutet »eine Infanteriezelle gilt als befreundet, solange KEINER
ihrer bis zu neun …«. Der Rumpf von `0x433B50` enthält **keinen einzigen
Besitzervergleich** — er vergleicht neunmal `word[+4+2i]` gegen das erste
Argument. Wenn es einen Bündnistest gibt, sitzt der in `0x433DF0`, nicht hier.

---

### 3. ⭐ Die Geländeplanierung (`0x4D5F30` … `0x4D5FD0` und weiter bis `0x4D6A00`)

Vier Funktionen meines Reviers und die sechs jenseits der falschen Grenze
bilden **eine** Mechanik: das Anheben von Gelände mit Steigungsbegrenzung.

#### 3.1 Die Nachbartafeln bei `0x53A290`

Aus dem Rohbild gelesen (`.data`, 12 Einträge zu je zwei `i16`):

| Adresse | Inhalt | benutzt von |
|---|---|---|
| `0x53A290` | **(+1,0) (−1,0) (0,+1) (0,−1)** | `0x4D5DC0` Schleife 1, `0x4D5FD0` Schleife 1, `0x4D6580` |
| `0x53A2A0` | **(−1,−1) (−1,+1) (+1,−1) (+1,+1)** | `0x4D5DC0` Schleife 2, `0x4D5FD0` Schleife 2, `0x4D6580` |
| `0x53A2B0` | (1,0) (1,1) (0,1) (−1,1) | `0x4AD0C4`, `0x4D6624` |

**Nullmodell:** dass vier aufeinanderfolgende Wortpaare zufällig genau die vier
Einheitsvektoren der Achsen sind und die nächsten vier genau die vier
Diagonalen, ist bei 12 möglichen Einträgen aus dem ganzen Wertebereich nicht
sinnvoll zu bepreisen — es ist eine Identität, kein Treffer.

#### 3.2 Der Ablauf

```
0x4D5F30(spalte, zeile):                      ; »planieren«
    word[0xC40158] = 0                        ; Zellenliste leeren
    byte[0xC5D620] = 0
    zelle_anheben(spalte, zeile)              ; 0x4D5F60
    nachzeichnen()                            ; 0x4D5FD0

0x4D5F60(spalte, zeile):                      ; »Zelle um 1 anheben«
    h = terrain_at(spalte, zeile)             ; 0x41D0E0
    if h == 7: return                         ; ⭐ Höchsthöhe 7
    i = word[0xC40158]++ 
    word[0xC40160 + 6i]     = spalte          ; ⭐ Liste der geänderten Zellen,
    word[0xC40162 + 6i]     = zeile           ;    Schrittweite 6
    terrain_set(spalte, zeile, h + 1)         ; 0x41D170
    hang_ausgleichen(spalte, zeile)           ; 0x4D5DC0

0x4D5DC0(spalte, zeile):                      ; »Hangausgleich«
    h0 = terrain_at(spalte, zeile)
    für die 4 GERADEN Nachbarn (0x53A290):
        wenn im Kartenrahmen und terrain_at(n) + 1 < h0:  zelle_anheben(n)
    für die 4 SCHRÄGEN Nachbarn (0x53A2A0):
        wenn im Kartenrahmen und terrain_at(n) + 2 < h0:  zelle_anheben(n)

0x4D5FD0():                                   ; »nachzeichnen«
    für jede der word[0xC40158] gemerkten Zellen:
        kachel_neu(x, y)                      ; 0x4D6100  (jenseits der alten Grenze)
        für die 4 geraden und die 4 schrägen Nachbarn: kachel_neu(…)
```

⭐ **Die Spielregel, die daraus folgt:** benachbarte Kacheln dürfen sich
**gerade um höchstens 1**, **diagonal um höchstens 2** Höhenstufen
unterscheiden. Wer eine Kachel anhebt, hebt automatisch alle Nachbarn mit an,
die dadurch zu tief lägen — rekursiv. Die Höhe ist auf **0…7** begrenzt.

`0x41D170` ist der Setzer: `byte[dword[0x677E20] + 4·(zeile·[0x542DC4] + spalte) + 2] = h`.
⚠ **Das ist Byte `+2` des 4-Byte-Geländesatzes, nicht `+3`** — `+3` ist das in
BB.8.3 als unidentifiziert gemeldete Sperrkriterium. Zwei verschiedene Felder.

#### 3.3 ⚠ Ein zweiter Nachbarraum bei `0x53A2D0`

`0x4D6100` und `0x4D6580` (beide jenseits der falschen Grenze) benutzen
zusätzlich `0x53A2D0` und `0x53A2B2`. **Ungelesen** — dort steckt vermutlich
die Kachelform-Auswahl (welches Bild eine Kachel bekommt, wenn ihre vier Ecken
verschieden hoch sind).

---

### 4. ⭐ Der 2×2-Eckcode: eine Rechenform, drei Verwender

Fünf Funktionen meines Reviers benutzen **dieselbe** Rechnung auf sec2
(`0xA3AEB0`, Eckhöhengitter 257 × 257):

```
code = 0
für j = 0,1:                       ; Zeilenrichtung
    für i = 0,1:                   ; Spaltenrichtung
        h = byte[0xA3AEB0 + (spalte+i)·257 + (zeile+j)]
        wenn h > 1:  return 0xFF   ; Abbruch
        code = 2·code + h
```

Bitfolge (von MSB nach LSB): (S,Z) · (S+1,Z) · (S,Z+1) · (S+1,Z+1).

Danach wird `code` in einer **kleinen Tafel auf dem Stapel** nachgeschlagen und
deren Index zurückgegeben, sonst `0xFF`:

| Funktion | Tafel | Bedeutung |
|---|---|---|
| `0x4CC000` | *(keine)* | gibt den rohen 4-Bit-Code zurück; 40 Rufstellen aus `0x4CCCB0` und `0x4CD900` (Brückenvorschau) |
| `0x4CC0A0` | **{3, 12, 10, 5}** | Brückenkopf-Richtung 0…3 |
| `0x4CBC90`, `0x4CBD90` | **{5, 3, 10, 12}** | Rampen-Richtung 0…3 — ⭐ **andere Reihenfolge!** |
| `0x4CF020` | **{3, 12, 10, 5, 4, 8, 2, 1}** | Hangform 0…7 |

**Was die Codes sind:**

| Code | Ecken auf Höhe 1 | Gestalt |
|---:|---|---|
| 12 = `1100` | (S,Z), (S+1,Z) | Kante zur kleinen Zeile |
| 3 = `0011` | (S,Z+1), (S+1,Z+1) | Kante zur grossen Zeile |
| 10 = `1010` | (S,Z), (S,Z+1) | Kante zur kleinen Spalte |
| 5 = `0101` | (S+1,Z), (S+1,Z+1) | Kante zur grossen Spalte |
| 8, 4, 2, 1 | je genau eine Ecke | die vier Aussenecken |

**Nullmodell:** aus 16 möglichen 4-Bit-Codes werden genau die vier
»Halbkanten« ausgewählt — 1 aus C(16,4) = 1 820, also **0,055 %** bei
Zufallswahl. Bei `0x4CF020` sind es die vier Kanten **plus** die vier
Einzelecken: 1 aus C(16,8) = 12 870, also **0,008 %**.

⚠ **Berichtigung an BB.7.** Dort steht, die {3,12,10,5}-Prüfung sitze in
`0x4CCCB0` (Brückenvorschau) und die Codes seien »genau die Muster, bei denen
das 2×2-Fenster durch eine gerade Naht zerfällt«. Beides ist zu berichtigen:

1. Die Prüfung sitzt **nicht** in `0x4CCCB0`, sondern in den beiden Helfern
   `0x4CC000` (roher Code) und `0x4CC0A0` (Code + Tafel + Richtungsvergleich).
   `0x4CCCB0` ruft `0x4CC000` **zwanzigmal** und `0x4CC0A0` **achtmal**;
   `0x4CD900` noch einmal genauso oft.
2. »Gerade Naht« ist zu schwach: **es sind Halbkanten, keine Nähte.** Zwei
   *benachbarte* Ecken oben, die anderen zwei unten — also die vier
   **Uferböschungen**. `0x4CF020` beweist es, weil es dieselbe Familie um die
   vier Einzelecken **erweitert**, und Einzelecken sind keine Nähte.
3. ⭐ **Die Reihenfolge der Tafel ist bei Rampe und Brücke verschieden**
   (`{3,12,10,5}` gegen `{5,3,10,12}`). Wer den Nachbau mit einer einzigen
   Richtungstabelle baut, dreht die Rampen um 90°.

---

### 5. ⭐ Der Brückenbau (Helfer)

Die grossen Stücke (`0x4CC280`, `0x4CCCB0`, `0x4CD900`) sind in BB.7 gelesen.
Meine vier Helfer schliessen sie ab.

* **`0x4CC0A0(spalte, zeile, richtung, einheit)` → bool.**
  1. `v = sec6[spalte·256 + zeile]`
  2. wenn `10000 ≤ v < 14000` **und** `byte[0x7847E8 + 22·(v−10000) + 0] == 1`
     **und** `0x433B50(einheit, v−10000)` → ok (siehe Abschnitt 2)
  3. sonst muss `v == 0xFFFD` sein (rau)
  4. Eckcode bilden, in `{3,12,10,5}` nachschlagen → `k`
  5. Rückgabe `k == richtung`
* **`0x4CC1F0(spalte, zeile)` → Index oder `0xFF`.** Sucht in der Brückentafel
  (100 Sätze zu 24 B ab `0xBFEA80`) den ersten mit `+0x12 != 0`, `+0 == spalte`,
  `+1 == zeile`. ⚠ **Ohne Rufstelle** (Abschnitt 11).
* **`0x4CC230(spalte, zeile)`** — merkt `(spalte, zeile)` in
  `dword[0x53C930]` / `dword[0x53C934]` und ruft dann
  **`Zasah(40200, sec6[spalte·256+zeile])`**. ⭐ 40200 (`0x9D08`) ist kein
  Einheitengriff (≥ 8000), sondern eine **Verursacherkennung »die Brücke
  selbst«**; `Zasah` bekommt Angreifer zuerst, Opfer zweitens (geprüft am Rumpf
  von `0x40C9A0`: `word[esp+0x30]` = zweites Argument = das Opfer, das gegen
  `0x1F40` geprüft und in `0x6E26C8 + 78·n` nachgeschlagen wird). 18 Rufstellen
  aus `0x4CC280` — beim Setzen einer Brücke stirbt alles, was auf ihren Kacheln
  steht.
* **`0x4CAE30(brückenindex)`** — zeichnet die Brücke. Bauphase
  `(500 − word[Brücke+0x16]) / 167` → 0…2, daraus ein Kachelversatz `18·Phase`.
  Satzfelder, die ich sicher gelesen habe: `+0` Spalte, `+1` Zeile, `+2` Länge
  bzw. Richtungsmerker, `+3`… Kachelfolge, `+0x13` Zähler, `+0x16` u16 Restzeit,
  `+0x12` Belegtmerker.

⚠ **Berichtigung an BB.7, zweite Zeile:** dort steht »100 Plätze zu 24 Byte bei
`0xBFEA80` (**= sec16**, `0x960` = 2400 = 100·24)«. Die Ladertafel sagt:
**sec17** ist `0xBFEA80` mit 2 400 Byte, **sec16** ist `0x7847E8` mit 88 000 Byte
(die Infanteriezellen). Die Zahl 2 400 stimmt, die Abschnittsnummer nicht.

---

### 6. ⭐ Die Rampe (sec21) — vollständig

OFFENE_FRAGEN Zeile 4651 kennt die Rampe als »`0xC2FCB8`, 4 × 50 (sec21),
macht rauhes Gelände befahrbar: Vorbedingung imap `0xFFFD`, danach `0xFFFE`«.
Drei meiner Funktionen füllen das aus.

**Satzform (4 Byte je Rampe, 50 Stück ab `0xC2FCB8`):**

| Versatz | Bedeutung |
|---|---|
| `+0` | Spalte |
| `+1` | Zeile |
| `+2` | Richtung 0…3 |
| `+3` | Restzeit 200 → 0 |

**`0x4CBB80(index)` — zeichnen:**

```
phase   = (200 − byte[Rampe+3]) / 67          ; 0, 1 oder 2
kachel  = 10723 + byte[Rampe+2] + 8·phase
imap_set(byte[Rampe+0], byte[Rampe+1], kachel)   ; 0x41D140
```

⭐ **Drei Bauphasen, vier Richtungen, Kachelband `10723 … 10734`.** Die
Schrittweite 8 zwischen den Phasen habe ich aus der `lea`-Kette gerechnet
(`add eax,eax` nach `idiv 0x43`, dann `lea [ecx + eax*8]`), nicht aus einer
Tafel abgelesen.

**`0x4CBC90` / `0x4CBD90` — Richtung bestimmen.** Beide bilden den Eckcode
(Abschnitt 4) und schlagen in `{5,3,10,12}` nach. Unterschied:

| | Vorbedingung an `sec6[spalte,zeile]` |
|---|---|
| `0x4CBC90` (1 Rufer, `0x431795`) | **nur `0xFFFD`** |
| `0x4CBD90` (2 Rufer, `0x4094D8` / `0x4096A8`) | `0xFFFD` **oder** eine Infanteriezelle mit genau einem Mann, und der bin ich |

Das ist genau der Unterschied zwischen »**darf ich hier eine Rampe planen?**«
(Vorschau, die Zelle muss leer sein) und »**darf ich hier weiterbauen?**«
(der Pionier steht schon drauf).

---

### 7. ⭐ Waldbrand und brennende Einzelobjekte (sec18 / sec4)

Abschnitt AL.3 hat sec4 als »die brennbaren Einzelobjekte« benannt
(`hori strom` / `dohorel strom`) und den Löschtrupp bei `0x4CA610` verortet.
Meine Funktionen sind der ganze Lebenslauf eines solchen Objekts.

**sec4-Satz (6 Byte, 2 000 Stück ab `0xC03A30`):**

| Versatz | Bedeutung | Beleg |
|---|---|---|
| `+0` | Spalte | `0x4C9F49` schreibt, `0x4CA5AB`/`0x4CA793` lesen |
| `+1` | Zeile | ebenso |
| `+2` | **Art** (0 = brennbar) | `0x4C9F59`; `0x4D5780` setzt beim Kartenleeren `0xFF` = frei |
| `+3` | **Zustand / Zähler** | `0x4CA5CC` setzt, `0x4CA7A4` nullt |
| `+4` | Merker (1 nach Stufe 1) | `0x4CA5D2` |
| `+5` | 0 | `0x4CA5D9` |

**Der Lebenslauf:**

```
0x4C9F20(spalte, zeile, art):                 ; anlegen / anzünden
    gebaeude_abreissen(spalte, zeile, 1000)   ; 0x4C9D50
    i = erster freier sec4-Platz              ; 0x4D5750 (+2 == 0xFF), sonst 1999
    sec4[i] = { spalte, zeile, art, 0 }
    imap_set(spalte, zeile, word[0xBB3B62 + 8·art] + 10000)

0x4CA570(i):                                  ; Stufe 1
    wenn byte[+3] != 0: return
    imap_set(…, word[0xBB3B62 + 8·art] + 10001)
    byte[+3] = (rand()%106) − 106             ; = rand()%106 + 150 als u8
    byte[+4] = 1 ; byte[+5] = 0

0x4CA750(i):                                  ; Stufe 2 / Ende
    imap_set(…, word[0xBB3B62 + 8·art] + 10002)
    byte[+3] = 0
    sec6[spalte·256 + zeile] = 0xFFFE         ; Zelle wieder frei
```

⭐ **Unabhängige Bestätigung einer schon verzeichneten Zahl, aus einer anderen
Stelle und mit dem Mechanismus dazu:** AL.3 sagt, Klasse 0 setze
`Zustand = rand()%106 + 150` und zähle **abwärts**. Im Rumpf steht
`div cx (cx=0x6A=106)` und dann **`sub dl, 0x6A`** — der Rest 0…105 wird also
um 106 **verringert**, was als vorzeichenloses Byte 150…255 ergibt. Beides ist
dieselbe Zahl; das Original rechnet aber mit einem **negativen** Byte und zählt
darum aufwärts gegen 0. Für den Nachbau heisst das: `sbyte`, nicht `byte`.

⭐ **`0x4CA600(spalte, zeile)` — der Löschtrupp, jetzt vollständig:**

```
für sx = spalte−1 … spalte+1:
  für sy = zeile−1 … zeile+1:
    wenn (sx, sy) auf der Karte (0x41D1D0):
      v = sec6[sx·256 + sy]
      wenn 50000 ≤ v < 56000:                       ; Waldfeld
          wenn byte[0xBFF3E2 + 3·(v−50000)] > 1:  := 1
      sonst wenn 61000 ≤ v < 64000:                 ; Einzelobjekt
          wenn byte[sec4[v−61000] + 2] == 0         ; ⭐ NUR Art 0
             und byte[sec4[v−61000] + 3] != 0:
                 byte[sec4[v−61000] + 3] = 0
```

Zwei Ergänzungen zum Pseudocode in AL.3:

1. ⭐ Der Löschtrupp wirkt auf ein **3×3-Feld**, nicht auf eine Zelle.
2. ⭐ Beim Einzelobjekt gibt es die zusätzliche Bedingung **Art (`+2`) == 0** —
   nur die Klasse, die überhaupt brennen kann, wird gelöscht.

**`0x4CAAC0(spalte, zeile)`** ist die Umkehrfrage: »brennt hier Wald?« —
`50000 ≤ v < 56000` und `byte[0xBFF3E2 + 3·(v−50000)] == 1`.
**`0x4CAC20()`** sucht den ersten freien sec18-Platz (6 000 Sätze, `+2 == 0`).

---

### 8. ⭐ Die Verlegungsfahrten (sec49) — der Takt ist gemessen

sec49 (`0xBC0DD0`, 200 Sätze zu 48 B) ist als »die Verlegungsfahrten« bekannt.
Zwei meiner Funktionen bedienen sie.

**`0x4CECC0()` — der Takt.** Wird aus `0x415CF0` gerufen (Hauptschleife):

```
t = dword[0x4FA240]                ; Takte seit Missionsbeginn
wenn t % 2 != 0: return            ; ⭐ nur JEDER ZWEITE Takt
n = (t / 2) % 200                  ; ⭐ genau EINE Fahrt je Durchlauf, reihum
f = 0xBC0DD0 + 48·n
wenn byte[f+0] == 0: return        ; Platz frei
wenn byte[f+0x2D] != 0: return
wenn 0x4CE710(byte[f + 4 + byte[f+0x2C]], byte[f+0x2E]) == 0:
     0x410DF0(n)
```

⭐ **Die Zahl, die der Nachbau braucht: 200 Fahrten werden über 400 Takte
verteilt, eine je zweitem Takt.** Bei 25 Takten/s heisst das: **jede Fahrt wird
alle 16 Sekunden einmal befragt.** Ein Nachbau, der alle 200 in jedem Takt
durchgeht, verhält sich sichtbar anders (schnellere Reaktion, andere
Reihenfolge bei Gleichstand).

**`0x4CEC20(gebäudeplatz)`** — wird aus `0x4C96D2` gerufen, wenn ein Gebäude
verschwindet:

```
ziel = byte[0xC06914 + 76·platz + 0x16]
wenn ziel == 0xFF: return
für n = 0 … 199:
    f = 0xBC0DD0 + 48·n
    wenn byte[f+0] != 0 und byte[f+0x2D] == 0
       und byte[f + 4 + byte[f+0x2C]] == ziel:
            einheit_entfernen(word[f+2])      ; 0x410E60
            byte[f+0] = 0                     ; Platz frei
```

⭐ Das schliesst den Kreis zu OFFENE_FRAGEN Zeile 9182/9186, wo genau dieses
Paar (`einheit_entfernen(word[…+2])`, `byte[…] := 0`) schon steht — hier ist
die **Auslösebedingung**: *das Zielgebäude der Fahrt existiert nicht mehr.*

**Satzform sec49, soweit gelesen:** `+0` belegt, `+2` u16 Einheitennummer,
`+4 … +0x2B` Wegliste aus Gebäudekennungen (bis 40 Stationen), `+0x2C` Index in
diese Liste, `+0x2D` Sperrmerker, `+0x2E` ein Beiwert.
**Gebäudefeld `+0x16` ist die Kennung, mit der eine Fahrt ihr Ziel benennt.**

---

### 9. ⭐ Musik: CD-Audio und MIDI sind zwei getrennte Wege

Elf Funktionen (`0x4D4F10` … `0x4D55C0`) sind die ganze Musikansteuerung. Alle
Zahlen sind MCI-Konstanten aus `mmsystem.h`, also **belegt, nicht gedeutet**.

#### 9.1 CD-Audio über `mciSendCommandA` (`WINMM.dll`)

| C | MCI-Botschaft | Kennwerte | Bedeutung |
|---|---|---|---|
| `0x4D4F10` | `MCI_OPEN` (0x803) | `MCI_OPEN_TYPE` (0x2000), Typ = `"cdaudio"` (`0x539B84`) | Gerät öffnen, Kennung nach `dword[0xBDEA6C]` |
| `0x4D4FA0` | `MCI_STATUS` (0x814) | `MCI_STATUS_ITEM|MCI_TRACK` (0x110), Posten 1 = `LENGTH` | Länge eines Titels |
| `0x4D5100` | `MCI_STATUS` | Posten 7 = `READY` | Gerät bereit |
| `0x4D5150` | `MCI_STATUS` | Posten 4 = `MODE`, Vergleich gegen **526** | ⭐ **`MCI_MODE_PLAY`** — »spielt gerade« |
| `0x4D51A0` | `MCI_STATUS` | Posten 3 = `NUMBER_OF_TRACKS` | Titelzahl |
| `0x4D51F0` | `MCI_STATUS` | Posten 8 = `CURRENT_TRACK` | laufender Titel |
| `0x4D5000` | `MCI_SET` (0x80D) + `MCI_PLAY` (0x806) | Zeitform **10 = `MCI_FORMAT_TMSF`**, dann `MCI_NOTIFY|MCI_FROM|MCI_TO` (0xD) | Titel abspielen |

⭐ **`0x4D5000(hwnd, titel)`** setzt das Zeitformat auf TMSF, fragt über
`0x4D4FA0` die Titellänge (Minuten in `bl`, Sekunden in `bh`) und spielt dann
von `dwFrom = titel` (also 00:00:00 dieses Titels) bis
`dwTo = titel | (Minuten<<8) | (Sekunden<<16)` — **also genau bis zum Ende
dieses einen Titels**, mit `MCI_NOTIFY` an das Fenster.

#### 9.2 MIDI aus Dateien

```
0x4D5240():                                   ; Stücke zählen
    byte[0xBDEA70] = 0
    für n = 0 … 199:
        Name = itoa(n) + ".mid"               ; 0x539CC0 = ".mid"
        wenn nicht vorhanden (0x428FD0): Abbruch
        byte[0xBDEA70]++
    byte[0xBDEA78] = (Anzahl > 2)             ; ⭐ mindestens DREI Stücke nötig
    wenn nicht: byte[0x8934B8] = 0            ; MIDI-Musik AUS
    byte[0xBDEA74] = byte[0x8934B8]

0x4D5310(n):                                  ; Stück abspielen
    wenn byte[0xBDEA74] == 0: return
    wenn n == 0xFF: n = rand()%(Anzahl−1) + 1 ; ⭐ nie Stück 0
    GetCurrentDirectoryA → GetShortPathNameA
    Pfad = Kurzpfad + "\" + itoa(n) + ".mid"
    0x4D5490(dword[0x540748], Pfad)           ; Fenster + Pfad

0x4D55C0(botschaft, wParam):                  ; aus der Fensterprozedur
    wenn botschaft == 0x3B9 (MM_MCINOTIFY) und wParam == 1 (MCI_NOTIFY_SUCCESSFUL):
        0x4D5310(rand()%(Anzahl−1) + 1)       ; ⭐ zufälliges Folgestück
```

⭐ **Drei nachbaubare Regeln, die wir nicht haben:**

1. Die Dateien heissen **`0.mid` … `n.mid` im aktuellen Verzeichnis**, und die
   Zählung bricht bei der ersten Lücke ab.
2. **Weniger als drei Dateien → MIDI-Musik wird abgeschaltet**
   (`byte[0x8934B8] := 0`; das ist die schon bekannte Einstellung Nr. 18
   »MIDI-Musik EIN/AUS«).
3. **Stück 0 wird nie zufällig gezogen** (`rand() % (Anzahl−1) + 1`) — es ist
   offenbar für einen festen Zweck reserviert.

`0x4D4F10`/`0x4D5100` haben je genau eine Rufstelle in `0x418780` bzw.
`0x458640`; die CD-Abfragen `0x4D5150`/`0x4D51A0` werden auch aus `0x414FF3`
und `0x472D40` benutzt.

---

### 10. ⭐ Der Karteneditor steckt in `GAME.EXE`

Fünf Funktionen belegen es wörtlich:

* `0x4C89A0` füllt eine `OPENFILENAME` bei `0xB97AD8` vor:
  `lStructSize = 0x4C`, `hwndOwner = dword[0x540748]`,
  `lpstrFilter = 0x539518` = **`"CW Map File (*.CWM)\0*.cwm\0"`**,
  `nMaxFile = 0x104`, `nMaxFileTitle = 0x200`, `lpstrDefExt = "cwm"`.
* `0x4C8A50` setzt `lpstrFileTitle = "Load Map"`, `Flags = 0x4000C`
  (`OFN_NOLONGNAMES | OFN_HIDEREADONLY | OFN_NOCHANGEDIR`) und ruft
  **`GetOpenFileNameA`**.
* `0x4C8A90` setzt `"Save Map"`, `Flags = 0x4000E` (dazu
  `OFN_OVERWRITEPROMPT`) und ruft **`GetSaveFileNameA`**.
* `0x4C8240` schneidet danach den Pfad ab und lässt nur den Dateinamen stehen
  (rückwärts bis zum letzten `\`, dann in sich verschieben).
* `0x4D5780` = **`karte_leeren(zeilen, spalten)`** — der Neuanfang:

```
für sp = 0 … spalten−1, für ze = 0 … zeilen−1:
    sec6[…]        = 0xFFFE      ; frei
    byte[0x689710 + …] = 0       ; Aufdeckung gelöscht
    sec52[…]       = 0xFFFF      ; Nebelspeicher gelöscht
für i = 0 … 1999:  byte[0xC03A32 + 6i] = 0xFF     ; alle sec4-Marker frei
für i = 0 … 254:   byte[0xC06914 + 76i] = 0       ; ⭐ 255 Gebäudeplätze geleert
```

⚠ **Eine Zahl zum Nachprüfen:** die Gebäudeschleife läuft von `0xC06914` bis
**unter** `0xC0B4C8`, das sind `(0xC0B4C8 − 0xC06914) / 76 = 255` Sätze.
`AIR_RE.md` Zeile 211 rechnet dieselbe Differenz und schreibt **256**.
sec3 ist 22 800 Byte = **300 × 76** ab `0xC06910`. Es sind also drei
verschiedene Zahlen im Umlauf (255 geleert, 256 laut AIR_RE, 300 laut
Ladertafel). ⭐ **Der Rechenweg `(0xC0B4C8 − 0xC06914)/0x4C` ergibt genau 255**;
die 45 Plätze dahinter werden beim Kartenleeren **nicht** angefasst.

Diese fünf hängen an `0x413569` und `0x4158F0` — Funktionen, die selbst keinen
`call` von irgendwo bekommen (siehe Abschnitt 11). **Der Editor ist im
Auslieferungsbau mitgeliefert, aber nicht angeschlossen.**

---

### 11. ⚠ 17 von 66 Funktionen haben keine einzige Rufstelle

Für jede Funktion habe ich gezählt: (a) jedes `E8`/`E9`-rel32 im ganzen `.text`,
das auf sie **oder auf ihren Thunk** zeigt, und (b) jede **relozierte** Stelle im
ganzen Bild, deren Dword auf sie oder ihren Thunk zeigt (Sprungtafeln,
Fensterprozeduren, Zeigerfelder). ⚠ Der Linearabtast wurde **nicht** benutzt.

**Ohne jede Rufstelle:**

```
0x4C82C0  0x4C8960  0x4C9F20  0x4C9FA0  0x4CAC20  0x4CC1F0  0x4CF600
0x4CF610  0x4CF620  0x4D05E0  0x4D0720  0x4D0740  0x4D0760  0x4D10F0
0x4D1160  0x4D22F0  0x4D5F30
```

**Zahl und Nullmodell.** Die Thunktafel `0x401000 … 0x4025A9` hat **1 109**
Einträge, also gibt es 1 109 Funktionen, denen der Binder einen Thunk gegeben
hat. Davon haben **59 (5,3 %)** keine Rufstelle. Im Adressfenster
`0x4C8100 … 0x4D5FD0` liegen **158** Thunkziele = **14,2 %** aller Ziele; bei
gleichmässiger Verteilung wären dort **8,4** der 59 zu erwarten. Tatsächlich
sind es **21** (davon 17 aus meiner Liste, 4 aus schon gelesenen Nachbarn).

> Binomialtest: n = 59, p = 0,142; Erwartung 8,4, Streuung 2,7; beobachtet 21
> → **z ≈ 4,7**, einseitig **p ≈ 1,3 · 10⁻⁶**.

**Die Häufung ist also echt, und sie hat eine Erklärung:** dieses Revier ist der
Ablagestapel des Binders. Drei Sorten stecken darin:

1. **Kleine Zugriffshelfer, die überall eingeschmolzen wurden** — `0x4CF620`
   (`verbuendet(p,q)`), `0x4D0740`/`0x4D0760` (Einheitenfeld `+0`/`+1`),
   `0x4D0720`. Der Rumpf ist da, aber jeder Verwender rechnet die Adresse
   selbst aus (`GAMESTATE_RE.md` Zeile 510/566 zeigt genau das für `0x87B155`).
2. **Editorcode** — `0x4D5F30` (Planieren), `0x4C82C0` (Bitmap laden),
   `0x4C8960` (Farbschlüssel), `0x4CC1F0` (Brücke suchen), `0x4C9F20`
   (Objekt setzen).
3. **Stumpfe Stellen** — `0x4D1160` ist **ein einziges `ret`**, `0x4D5DB0` ist
   `xor eax,eax; ret` und wird trotzdem **14-mal** gerufen (aus `0x406D20`,
   `0x409C0B`, `0x415CF0`, `0x433C20`). ⭐ **Eine Abfrage, die in beiden
   Auslieferungen immer 0 sagt** — an vierzehn Stellen. Wer den Nachbau danach
   verzweigen lässt, baut einen Zweig, den das Original nie geht.

⚠ **Was dieser Befund NICHT sagt:** dass die Mechanik im Spiel fehlt.
`0x4CC1F0` sucht eine Brücke — dass niemand *diese* Fassung ruft, heisst nur,
dass die Suche anderswo eingeschmolzen ist. Für den Nachbau bleiben die Rümpfe
die beste Quelle über die **Satzform**.

---

### 12. Die Wegsuche: drei Platzprüfungen und zwei Ringstücke

#### 12.1 ⭐ `0x4D3390` / `0x4D35C0` / `0x4D3700` — »passt die Einheit dahin?«

Alle drei werden ausschliesslich aus **`0x4D3927` (`Search:`)** gerufen und
beantworten die Frage, ob ein mehrzelliges Fahrzeug auf einen Platz passt.

| C | Kasten | Regel je Zelle |
|---|---|---|
| `0x4D3390` | **2 × 2** | `0xFFFE` (frei) **oder** Einheit `< 8000` mit **Unterklasse `+0x0A` == 0** **oder** Infanteriezelle `10000…13999` mit `byte[Satz+1] == 0` |
| `0x4D35C0` | **2 × 2** | `0xFFFC` (Wasser) **oder** Einheit `< 8000` mit **Unterklasse == 4** |
| `0x4D3700` | **4 × 4** | wie `0x4D35C0` |

Alle drei prüfen vorher den Kartenrand: `spalte + k ≤ dword[0x542DC4]` und
`zeile + k ≤ dword[0x542DF8]` mit `k` = Kastenbreite.

⭐ **Das deckt sich Wort für Wort mit den Passierbarkeitsarten aus BB.1**
(Art 0/4 für Land, Art 6/12 für Wasser) — und ist damit eine unabhängige
Bestätigung dieser Lesung aus einem anderen Rumpf. ⚠ Ein Unterschied: BB.1
Art 6 lässt Unterklasse **{4, 5}** zu, `0x4D35C0`/`0x4D3700` nur **4**. Der
Nachbau darf die beiden nicht gleichsetzen.

⚠ `0x4D3390` ist bei `cfind` als »UNGENAU 97 %« gemeldet. Ich habe C und F
nebeneinander gelesen: der Unterschied ist **reine Registerzuteilung**.

#### 12.2 `0x4D3270` — Wegsuchauftrag stornieren

```
für i = word[0x539B14] … word[0x539B10] − 1:
    wenn word[0xBDA0E8 + 2i] == einheit:
        byte[0xBDA8C0 + i] = 0xFF        ; Art := leer
        return                            ; ⭐ nur der ERSTE Treffer
```

⭐ Damit ist die Rolle der zwei Zeiger belegt: **`word[0x539B14]` ist der
Lesezeiger (Anfang)**, **`word[0x539B10]` der Schreibzeiger (Ende)**.
⚠ **Die Schleife ist nicht ringfest** — sie zählt `cx` linear hoch und
vergleicht mit dem Ende. Ist der Lesezeiger einmal grösser als der
Schreibzeiger (Umlauf), läuft sie über die 1 000 Plätze hinaus, und weil der
Index mit `movsx` vorzeichenbehaftet in `word[esi*2 + 0xBDA0E8]` geht, auch in
negative Adressen. **Als offen gemeldet, nicht als Fund** — entweder läuft der
Ring nie um, oder das Original hat hier eine Schwachstelle.

Rufer: `0x40B56E` und `0x433CF3` (also: Einheit stirbt → Auftrag streichen).

#### 12.3 `0x4D22F0` — die Diagonalfreigabe

Arbeitet auf der **Passierbarkeitskarte** `0xBCA0E8` (256 × 256 Byte, Kladde):

```
; 0x4D22F0(spalte, zeile, dSpalte, dZeile)   — Reihenfolge aus dem Rumpf gelesen
wenn karte[(spalte+dSpalte)·256 + (zeile+dZeile)] != 0:  return   ; Ziel muss 0 sein
wenn karte[(spalte+dSpalte)·256 +  zeile        ] >  1:  return   ; Nachbar in Spaltenrichtung
wenn karte[ spalte        ·256 + (zeile+dZeile) ] >  1:  return   ; Nachbar in Zeilenrichtung
0x4D2210(spalte + dSpalte, zeile + dZeile)
```

⭐ **Die klassische Diagonalregel:** ein Schrägschritt ist nur erlaubt, wenn das
Zielfeld frei ist **und beide anliegenden geraden Felder** höchstens Wert 1
haben — also **nicht um eine Ecke herum**. ⚠ Ohne Rufstelle (Abschnitt 11);
die eingesetzte Fassung ist offenbar eingeschmolzen.

---

### 13. Kleinteile mit Adresse

| C | was sie tut | Beleg |
|---|---|---|
| `0x4CF5A0` / `0x4CF5C0` | **Kameramitte** = `Sichtmass/2 + Kameraecke`; beide aus `0x438BD0` | `0x5387C0`/`0x5387C4` = Sichtbreite/-höhe, `0x5387AC`/`0x5387B0` = Kamera-Kachel (Ladertafel sec7/sec8) |
| `0x4CF600` / `0x4CF610` | **Windrichtung X / Y** | `0x4F8D68` / `0x4F8D6C`, schon in AIR/CAB benannt |
| `0x4CF620` | **`verbuendet(p, q)`** | `byte[0x87B155 + 40·p + q]`, sec53 `+0x15` |
| `0x4D0AA0` | **`terra_place` leeren** — 50 Sätze zu 6 B ab `0xBC6D40`, `+0 := 0` | sec78; die 50 und die 6 stehen im Rumpf (`cmp al, 0x32`, `lea [edx+edx*2]` ×2) |
| `0x4D0800` | `dword[0xA9A1D8] += n` (sec74) | Rufer `0x41ACB0` |
| `0x4D0720` | `sec75[n] := 0xFF`, alter Wert zurück | sec75 = 8 Byte = **je Spieler eines** |
| `0x4D5750` | ersten freien sec4-Platz, sonst **1999** | ⚠ die 1999 ist ein *Überschreiben*, kein Fehler |
| `0x4D5D60` | `Gebäude[platz] + 6 := byte[0xBB41A0 + 10·Typ]`, dann `0x4C95E0(platz, 0)` | `0xBB41A0` ist dieselbe 10-Byte-Tafel, deren `+2` (`0xBB41A2`) laut GAMESTATE_RE die Fussabdruck-Nummer trägt |
| `0x4C9D50` | **Gebäude an (x, y) abreissen** | 10 × 6 Fussabdruck; `word[0xB97B38 + 2·(6·(15·form + dx) + dy)]`; Ausnahmeplatz als drittes Argument (1000 = keine Ausnahme) |
| `0x4C8840` + `0x4C8960` | ⭐ **Farbschlüssel bestimmen und setzen**: `GetDC` → `GetPixel(0,0)` merken → `SetPixel(0,0, farbe)` → `ReleaseDC` → `Lock` → erstes Dword lesen und auf `(1<<Bittiefe)−1` maskieren → `Unlock` → alten Pixel zurückschreiben → `SetColorKey(DDCKEY_SRCBLT)` | vtable `+0x44` = `GetDC`, `+0x64` = `Lock`, `+0x68` = `ReleaseDC`, `+0x74` = `SetColorKey`, `+0x80` = `Unlock`; `0x8876021C` = `DDERR_WASSTILLDRAWING` |
| `0x4C8100` / `0x4C81B0` | Text auf eine gesperrte Oberfläche schreiben (`0x4BA420`), Deskriptor 0x6C Byte | dieselbe `DDERR_WASSTILLDRAWING`-Warteschleife |
| `0x4C9FA0` | `MessageBeep(-1)` **300-mal** | ⚠ ohne Rufstelle — vermutlich ein Alarm aus der Entwicklung |
| `0x4D10F0` | 2×2-Feinraster: `0x4B5C60(2·zeile+j, 2·spalte+i, wert)` | `0x4B5C60` sperrt die Oberfläche `dword[0x540770]` und schreibt ein Byte — also ein **Zeichenraster**, kein Spielraster |

---

### Berichtigungen an bestehenden Dokumenten

1. ⭐⭐ **`funktionen.py` Zeile 170 und `cfind.py` `CRT_C`: `0x4D6000` → `0x4D6A00`.**
   Sechs Spielfunktionen mit 2 416 Byte werden heute fälschlich als Bibliothek
   abgezogen. Messlatte: 90,0 % → **89,8 %**, 1 107 → **1 113** Funktionen.
   (Abschnitt 1)
2. ⭐⭐ **OFFENE_FRAGEN BB.8.2 ist erledigt.** `byte[Satz+1]` der
   Infanteriezellen ist `0xFE − alter sec6-Wert`; `0` heisst »stand auf freiem
   Boden«. Spalte/Zeile liegen bei `+2`/`+3`, nicht bei `+0`/`+1`.
   (Abschnitt 2)
3. ⚠ **OFFENE_FRAGEN BB.7, erste Zeile:** `0xBFEA80` ist **sec17**, nicht
   sec16. sec16 (`0x7847E8`, 88 000 B) sind die Infanteriezellen. Die Zahl
   2 400 = 100 · 24 stimmt.
4. ⚠ **OFFENE_FRAGEN BB.7, zweite Zeile:** die {3,12,10,5}-Prüfung sitzt nicht
   in `0x4CCCB0`, sondern in `0x4CC000`/`0x4CC0A0`. Und die Codes sind **vier
   Halbkanten** (zwei benachbarte Ecken hoch), nicht »gerade Nähte« —
   `0x4CF020` erweitert dieselbe Familie um die vier **Einzelecken**.
   ⭐ Zusätzlich: die Rampe (`0x4CBC90`/`0x4CBD90`) benutzt die **andere
   Reihenfolge** `{5,3,10,12}`.
5. ⚠ **OFFENE_FRAGEN Zeile 5388/5392:** `0x433B50` ist **kein** Bündnistest.
   Der Rumpf vergleicht neunmal `word[Satz+4+2i]` gegen das erste Argument —
   »ist genau diese Einheit in dieser Zelle«. Ein Besitzervergleich kommt darin
   nicht vor.
6. ⚠ **OFFENE_FRAGEN AL.3, Löschtrupp-Pseudocode:** es fehlen zwei Dinge —
   der Trupp wirkt auf ein **3×3**-Feld, und beim Einzelobjekt gilt zusätzlich
   **Art (`+2`) == 0**.
7. ⭐ **AL.3, `Zustand = rand()%106 + 150`:** bestätigt, mit Mechanismus. Das
   Original rechnet `(rand()%106) − 106` in ein **vorzeichenbehaftetes** Byte
   und zählt gegen 0 hoch. Der Nachbau muss `sbyte` benutzen, sonst dreht sich
   die Richtung.
8. ⚠ **`AIR_RE.md` Zeile 211** rechnet `(0xC0B4C8 − 0xC06914)/0x4C = 256`.
   Die Division ergibt **255**. Der Kartenleerer `0x4D5780` leert genau 255
   Plätze; sec3 hat laut Ladertafel 300.
9. ⚠ **Zur Methode aus dem Nachtrag:** der »gleichmässige C→F-Abstand« trennt
   Spielcode und Bibliothek in diesem Bau **nicht** — er ist ab `0x4D411E` bis
   zum Ende von `.text` durchgehend `−0x470`, für Spielcode wie für Bibliothek.
   Umgekehrt ist der Abstand **innerhalb** meines Reviers **nicht** konstant
   (`−0x430` … `−0x480`). `cfind.py` ist trotzdem wertvoll — nur nicht dafür.
10. ⚠ **`cfind.py` meldet sechs Funktionen meines Reviers als »UNGENAU«**
    (`0x4D5780` 98 %, `0x4CAE30` 90 %, `0x4CBC90` 92 %, `0x4CC000` 98 %,
    `0x4CC0A0` 94 %, `0x4D3390` 97 %). Ich habe `0x4D5780`, `0x4CAE30`,
    `0x4CBC90` und `0x4CC000` Befehl für Befehl gegen F gelesen: **alle vier
    sind reine Registerzuteilung und Stapelplatzvergabe** — kein
    Auslieferungsunterschied. Der Fingerabdruck zählt `cmp eax,ecx / jg` gegen
    `cmp ecx,eax / jl` als Unterschied. **Kein zwölfter C/F-Unterschied in
    Revier 8.**

---

### Bauaufgaben, die daraus folgen

1. ⭐⭐ **Geländeanhebung mit Steigungsbegrenzung** (Abschnitt 3). Regel:
   gerade Nachbarn höchstens 1 Stufe, schräge höchstens 2, Höchsthöhe 7,
   rekursives Mitheben. Ohne das entstehen im Editor Klippen, die das Original
   nicht kennt.
2. ⭐⭐ **Der Verlegungstakt** (Abschnitt 8): eine Fahrt je **zweitem** Takt,
   reihum über 200 Plätze. Ein Nachbau, der alle 200 je Takt abarbeitet,
   reagiert 400-mal schneller.
3. ⭐ **Der Rampenbau** (Abschnitt 6): 3 Bauphasen über 200 Takte,
   Kachelband `10723 + Richtung + 8·Phase`, Richtungstafel `{5,3,10,12}` —
   **nicht** die Brückentafel `{3,12,10,5}`.
4. ⭐ **Der Löschtrupp wirkt 3×3** und nur auf Objekte der Art 0.
5. ⭐ **MIDI-Musik** (Abschnitt 9): `0.mid`…`n.mid`, Abbruch bei der ersten
   Lücke, **weniger als drei Dateien schalten die Musik ab**, Zufallsstück nie
   die 0, und nach jedem Stück kommt ein zufälliges nächstes.
6. ⭐ **CD-Audio**: Titel wird von 00:00 bis zur gemessenen Titellänge in
   **TMSF** gespielt, mit `MCI_NOTIFY`; »spielt gerade« = `MCI_STATUS_MODE == 526`.
7. ⭐ **Brücke setzen tötet alles auf ihren Kacheln** — `Zasah` mit
   Verursacherkennung **40200** (Abschnitt 5). Wir haben dafür bisher keine
   Verursacherkennung.
8. ⭐ **`karte_leeren`** (Abschnitt 10) setzt vier Raster **und** die
   Gebäudetafel zurück; wer nur sec6 leert, behält alte Marker und Gebäude.
9. ⚠ **`0x4D5DB0` gibt immer 0 zurück** und wird 14-mal gerufen. Wo unser
   Nachbau an diesen 14 Stellen etwas anderes rechnet, weicht er ab.
10. ⚠ **Infanteriezelle anlegen** (`0x433A50`): das Feld `+1` merkt den
    Untergrund. Wird die Zelle geräumt, muss der Untergrund daraus
    wiederhergestellt werden — sonst wird aus rauem Gelände stillschweigend
    freier Boden.

---

### Was ungedeutet bleibt

1. ⚠ **Die sechs Funktionen `0x4D6090` … `0x4D69FF`** (2 416 B) sind als
   Spielcode **belegt**, aber **nicht gelesen**. Sie hängen an der
   Geländeplanierung und benutzen zwei weitere Nachbartafeln (`0x53A2D0`,
   `0x53A2B2`). Das ist der nächste lohnende Griff — sie sind erreichbar,
   klein und gehören zu einer Mechanik, deren Anfang jetzt steht.
2. ⚠ **`0xC5D620`** — von `0x4D5F30`, `0x4D6100` und `0x4D6510` geschrieben und
   gelesen, in keiner Ladertafel. Weder Grösse noch Bedeutung ermittelt.
3. ⚠ **`0x4CAE30` (Brücke zeichnen), Satzfelder `+3`…`+0x11` und `+0x13`.**
   Ich habe Bauphase (`+0x16`), Spalte, Zeile, Belegtmerker und den
   Kachelversatz 18 sicher; die Kachelfolge selbst nicht durchgerechnet.
4. ⚠ **`0x74EC88`-Zugriffe mit rohem Index.** Zwei meiner Funktionen rechnen
   `byte[0x74EC88 + 22·v]` mit `v` als **rohem sec6-Wert** (10 000…13 999).
   Das ist arithmetisch dasselbe wie `0x7847E8 + 22·(v−10000)`, aber wer nach
   `0x7847E8` grept, findet diese Stellen nicht. **Es kann weitere geben.**
5. ⚠ **`0x4D10F0`** ruft `0x4B5C60` auf einem 2×-Feinraster. Ob das die
   Übersichtskarte, ein Schattenraster oder etwas Drittes ist, habe ich nicht
   ermittelt — `0x4B5C60` schreibt in die Oberfläche `dword[0x540770]`.
6. ⚠ **`sec74` (`0xA9A1D8`, 4 B) und `sec75` (`0xA9A200`, 8 B)** bleiben
   unbenannt. Aus `0x4D0800` und `0x4D0720` weiss ich nur: sec74 ist ein
   summierender Zähler, sec75 hat **ein Byte je Spieler** und wird auf `0xFF`
   gesetzt, wobei der alte Wert zurückgegeben wird.
7. ⚠ **Das Fussabdruckmass 10 × 6 in `0x4C9D50`.** Die Schleifen laufen
   `dx = 0…9`, `dy = 0…5`, die Formtafel `0xB97B38` hat Schrittweite
   `15 · 6` Worte je Form. **10 gegen 15** — die Formtafel ist breiter als die
   Prüfschleife. Entweder ist `15` das Ablagemass und `10` das Prüfmass, oder
   eine der beiden Zahlen bedeutet etwas anderes. Nicht aufgelöst.
8. ⚠ **Warum `0x4D3270` nicht ringfest ist** (Abschnitt 12.2). Als Verdacht
   gemeldet, nicht als Fund.

---

## BQ. ⭐⭐⭐ 100 % — und was die Zahl NICHT sagt (22.08.2026)

```
Funktionen (ohne Thunks)        1113
davon bei uns erwaehnt          1113   (100.0 %)
nach BYTES                   863,904 / 863,904   (100.0 %)
benannt, aber NIE erwaehnt         0
```

**Jede Funktion des Spiels steht jetzt in unseren Unterlagen.** Am Morgen des
22.08.2026 waren es 660 von 1107 (59,6 %); acht Leseagenten haben die restlichen
442 Funktionen in Revieren von je rund 11 KB abgearbeitet (Abschnitte **BI**
bis **BP**).

### BQ.1 ⚠⚠ Die Quote ist eine OBERGRENZE, keine Verstehensquote

Das steht seit jeher im Kopf von `funktionen.py`, und heute ist es wichtiger als
je zuvor: **eine erwähnte Adresse ist nicht dasselbe wie eine verstandene
Funktion.** Was 100 % wirklich heisst:

* Es gibt **keine unbesehene Funktion mehr.** Jede wurde aufgemacht, ihre Rufer
  bestimmt, und sie wurde einem Revier und einer Mechanik zugeordnet.
* Es heisst **nicht**, dass jede erklärt ist. Die acht Berichte führen zusammen
  **rund 130 ausdrücklich offene Punkte** — jeder in einem eigenen Abschnitt
  »Was ungedeutet bleibt«, mit dem, was sicher ist, und dem, was fehlt.
* Mehrere Funktionen sind als **tot** belegt (kein Rufer, kein Zeiger): unter
  anderem `0x434120`, `0x434380`, `0x438A70`, `0x438D10` (ein reines `ret`),
  `0x43B170`, `0x43B800`, `0x43B9C0`. »Gelesen« heisst hier »als tot
  nachgewiesen«, und das ist ein Ergebnis, kein Ausfall.

⭐ **Die ehrlichere Messlatte ab jetzt ist nicht mehr die Quote, sondern die
Zahl der offenen Punkte.** Die Quote kann nicht mehr steigen.

### BQ.2 Die Messlatte selbst wurde unterwegs berichtigt

Die C-Laufzeitbibliothek beginnt bei **`0x4D6A00`**, nicht bei `0x4D6000` —
dazwischen liegen **sechs Spielfunktionen** (2 416 Byte). Der Beleg ist nicht
die Adresse, sondern der **Verweisinhalt**: von 37 relozierten `.data`-Verweisen
dieser sechs zeigt jeder auf Spielstandsglobale aus der Ladertafel, keiner in
die `.idata`; die sicheren CRT-Funktionen dahinter fassen keine davon an.
1107 → **1113** Funktionen, 861 488 → **863 904** Byte. Siehe **BP**.

⚠ **Eine Messlatte, die man selbst gesetzt hat, gehört mitgeprüft.** Diese hier
war um sechs Funktionen zu kurz, und niemand hat es gemerkt, weil die Zahl
»ab etwa 0x4D6000« plausibel aussah.

### BQ.3 ⭐⭐ Der ZWÖLFTE Auslieferungsunterschied — und der zehnte wird anfassbar

**Neu (BL): `0x43C960` gibt es in F nicht.** Sie löscht bei der Eroberung eines
Gebäudes alle Einheiten des alten Besitzers, die auf der Ankerzelle stehen
(Auftrag < 45), und gibt die Zelle frei. **Vierfach belegt**, und nur der erste
Beleg kommt aus dem Werkzeug:

1. `cfind` findet keinen Kandidaten (bester Rest 46 %).
2. Volkszählung aller `0xFFFE`-Schreiber in die imap: **C 27, F 26** — der
   Überschuss sitzt in `0x43C960`.
3. Volkszählung »lies Einheit `+0x14`, `cmp al,0x2D`«: **C 8, F 7** — sieben
   Paare, der Rest wieder `0x43C960`.
4. An der Rufstelle (über eine EXE-weit eindeutige Bytefolge lokalisiert:
   C `0x43CD07`, F `0x43BDA6`) läuft F ohne Aufruf weiter.

**Und ein dreizehnter im selben Rufer** (`0x43CFF5…0x43D04B`, 87 B, in F nicht
vorhanden): erobert ein **KI-Spieler** (`sec53[40p] == 1`) im
**Netz-/Determinismusbetrieb** (`dword[0x539234]`), bekommt jede seiner Basen
**+100 Fahrwerke und +100 Specials**. ⭐ `GAMESTATE_RE.md` §3.87 führt genau
diesen Block mit »both conditions unidentified« — **beide sind jetzt benannt.**

⭐⭐ **Der zehnte Unterschied ist zum ersten Mal ANFASSBAR** (BN). Bisher stützten
ihn vier Zählungen; jetzt gibt es die Funktion, die in F fehlt:
`cfind.py --diff 0x487630 0x485D00` zeigt genau **einen** abweichenden Block —
den 48. Arm des Fensterverteilers, `call 0x480650`, das zweite Hauptmenü mit
»Enzyklopädie«. Gegenproben: C `cmp eax,0x2F` gegen F `cmp eax,0x2E`,
F-Sprungtafel 47 Einträge plus `CCCCCCCC`-Füllung.
**Fünfte Stütze** aus BL: `0x441190` nimmt in C die Fensterarten 9, 35 **und 48**
vom Bildschirmzwang aus, in F nur 9 und 35.

### BQ.4 ⚠⚠ Was ich mir selbst nachweisen lassen musste

Das Werkzeug `cfind.py` ist am Vormittag entstanden und hatte **fünf Fehler**,
jeden hat ein Leseagent gefunden. Sie stehen hier, weil derselbe Fehler sonst
wiederkommt:

1. ⚠⚠ **Die Funktionsgrenzen waren falsch.** Die Anfangsliste enthält auch
   Sprungziele **mitten in Funktionen**, und die erste Fassung nahm jeden
   »Anfang« zugleich als **Ende** des vorigen. `0x444740` wurde so nach 226 von
   593 Byte abgeschnitten. **So sind neun der 25 angeblichen
   Auslieferungsunterschiede entstanden — allesamt Einbildung.**
2. **Die Bibliotheksgrenze** (siehe BQ.2).
3. **Die Ähnlichkeitszahl allein taugt nicht.** Für `0x41A150` schlug sie
   `0x48631F` mit 60 % vor — falsch. Für `0x4AF1C0` meldet sie **74 %**, obwohl
   C und F **dieselben 23 Befehle** enthalten und nur zwei Hälften umsortiert
   sind. **Unter rund 40 Befehlen ist sie aussagelos.**
4. ⚠ **Und die Gegenmassnahme war im ersten Anlauf zu scharf.** »Abstand
   ungleich dem der Nachbarn → falscher Partner« brandmarkte `0x410940 →
   0x410770`, das **bekannt richtige** Paar. Der `.text`-Abstand ist blockweise
   konstant; an einer Blockgrenze weicht er regulär ab.
5. **Der Abdruck liess die Operandenbreite fallen.** Darum bildete er
   `0x41D0E0` (liest `byte[+2]`) und `0x41D110` (liest `word[+0]`) auf
   **dieselbe** F-Funktion ab und widersprach Abschnitt BC. **BC hatte recht.**

⭐⭐ **Und ein Verdacht von mir hat sich als falsch erwiesen.** In BH stand:
»`0x4B6F60` trifft zu 89 %, das sind rund vierzehn abweichende Befehle — zu viel
für Zufall. Verdacht auf einen zwölften Auslieferungsunterschied.« Abschnitt
**BO** hat es von Hand geprüft: restlos Registerzuteilung, `jle` gegen `jge` mit
vertauschten Operanden, und der »abweichende Schwanz« ist die **Sprungtafel
hinter dem `ret`**. Zurückgezogen.
⚠ **Die Rechnung stimmte, die Voraussetzung nicht.** Eine Zahl, die man nicht
geeicht hat, ist keine Messung — sie sieht nur so aus.

### BQ.5 Drei Fehler des Originals, alle in BEIDEN Bauten

* ⭐ **Der Wind trägt einen Klammerfehler** (BJ, `0x422210`): die zwei Klemmen
  für die **Stärke** schreiben die **Richtung** (`0x422277`, `0x422289`). Die
  Stärke ist völlig ungeklemmt, die Richtung wird auf **9** gezwungen —
  ausserhalb ihres eigenen Bereichs 0…7. Drei Leser nehmen sie ungemaskt, einer
  in eine **8-Einträge**-Tafel. Die Nachbarzeile macht es richtig.
* **Das Generatorfenster prüft auf ein offenes DEPOTfenster** (BM, `0x442FB0`:
  öffnet Art 20, prüft Art 23). Zweimal auf einen Generator klicken erzeugt zwei
  Fenster. F `0x441F90` hat dieselbe `cmp al, 0x17`.
* **Fensterart 28 »Stromversorgung« ist unerreichbar** (BM) — der einzige Öffner
  hat keinen Rufer und steht nirgends als Dword, in C wie in F.

### BQ.6 Wie es weitergeht

Die Leseaufgabe ist erledigt; damit rückt Punkt 2 der Reihenfolge nach vorn:
**testen**, dann **Fehler beheben**, dann der **Gefechtsmodus**. Die acht
Berichte führen zusammen rund **80 Bauaufgaben**. Vier, die herausragen:

1. ⭐⭐ **Die Wegsuche ersetzen** (BB) — unsere Navigation ist komplett eigene
   Erfindung, das Original ist eine reine 8-Nachbar-Breitensuche.
2. ⭐⭐ **Die Bauteiltafel je Spieler** (BN) — unsere Forschung wirkt derzeit auf
   **alle** Spieler.
3. ⭐ **Der Angriffsbefehl als Busbefehl 11** (BK) — damit fällt unsere eigene
   Nummer `OursAttack = 2001` weg und der Angriff wird gleichlauffähig.
4. ⭐ **Die verlegte Einheit stirbt mit dem Zug** (BG) — bei uns überlebt sie.

⚠ **Und der Befund des Spielers steht weiter offen:** »seit wir mehr und mehr
hier analysieren geht nämlich einiges nicht mehr in der kampagne«. Abschnitt
**AZ** ist beim Prüflauf vorzulegen. Die Leseaufgabe ist jetzt zu Ende — **die
Reihenfolge sagt, dass er als nächstes prüft.**

---

## BR. ⭐⭐⭐ SEIN ERSTER PRUEFLAUF (23.08.2026) — Kampagne 1

Sieben Meldungen. **Die Bilanz ist wichtiger als die Liste**, weil sie seine
Frage beantwortet (»wenn wir doch alles ausgelesen haben, warum ist dann so viel
verschoben?«):

| | Zahl |
|---|---|
| echte Fehler von uns | **3** |
| originalgetreu, nur ungewohnt | **1** (der Hafen) |
| gar kein Fehler im Code | **1** (`cursor_hints=false` in seiner `settings.cfg`) |
| Folge eines anderen Fehlers | **1** (die 50 $) |
| noch offen | **1** (Kampagnen-KI) |

Dazu **einer, den er nicht gemeldet hat und den ich beim Nachsehen fand**: die
Wegsuche sperrte stehende Einheiten (BR.5).

### BR.1 Der schwarze Bildschirm — `is not Control` ist keine Pruefung

`ChangeSceneToFile` gibt die alte Szene frei; `MainMenu._Ready` raeumt danach
als ERSTE Anweisung die Fensterverwaltung ab — auf toten Knoten. Ein
freigegebenes Godot-Objekt behaelt im C#-Umschlag seinen TYP, der
Mustervergleich gelingt, der Feldzugriff wirft. `_Ready` starb in Zeile eins.

Sechs Stellen (`InDenSchirm`, `FesteLage`, `Treffer`, `Fertig`, `Blende`,
`Zeichenfolge`), jetzt ein Waechter `Lebend()` mit `IsInstanceValid`.
`--fenster-check` Messung 19/20, Nullmodell: »is Control« sagt bei einem
freigegebenen Knoten **True**, `IsInstanceValid` sagt **False**.

### BR.2 Die Wegsuche sprang — eine Welle zu frueh

Waehrend die Welle der Entfernung d abgeraeumt wird, steht `marke` auf 9+d, die
Zellen dieser Welle tragen 8+d, der Vorgaenger 7+d. Der Rueckverfolger begann
bei `marke-1` = 8+D, also auf der **Zielwelle selbst**.

```
map_01, (4,39) -> (4,35)
  falsch:  2,38  2,37     (4,39) auf (2,38) sind ZWEI Spalten
  richtig: 3,38  2,37     identisch mit dem alten A*
```

Das ist zugleich sein »Springen/Beamen«: die Zellen des Weges sind nicht mehr
benachbart.

### BR.3 ⚠⚠ Warum `--wegsuche-check` das nicht sah — ZWEI Gruende

1. Er prueft den Weg nur gegen SICH SELBST (ab Zelle 2). Der Bruch lag zwischen
   START und erster Zelle. Die gelieferte Kette war in sich stimmig.
2. **Er hat seit jeher nur DREI seiner Messungen ausgefuehrt.** Die Probeflaeche
   war auf feste 7x7 freie Zellen verdrahtet — die gibt es weder auf map_01
   noch auf der Gefechtskarte. Der Lauf meldete »NICHT GEMESSEN« und ging
   durch; die drei, die liefen, pruefen nur Tafeln, also gerade nicht die Suche.

Beides behoben. Neue Messungen: »erster Schritt liegt neben dem Start«,
»letzte Zelle IST das Ziel«, und dasselbe ueber den laengsten Weg der Karte.
»Nicht gemessen« ist jetzt DURCHGEFALLEN.

### BR.4 Die 50 $ der Nebenmission — kein eigener Fehler

Die drei Geldregeln (@0x4988A8 / @0x4988D7 / @0x498905) sind richtig gelesen und
richtig gebaut: je 50 $, wenn `units(Klasse 3, Spieler 1)` von 3 auf 2, 1, 0
faellt. map_01 gibt Spieler 1 genau drei Klasse-3-Saetze (unit_type 153,
game_unit_type 4). **Alle drei setzen `v15` voraus**, und `v15` setzt einzig
@0x49885C: `unit_pos(0, +1) < 20` — der Startpanzer muss Zeile 20 erreichen.
Wegen BR.2 kam er nicht hoch.
⚠ **Er muss bestaetigen, dass die Kette jetzt laeuft.**

### BR.5 ⭐⭐ Stehende Einheiten sperrten die PLANUNG (von mir gefunden)

Tafel BB.1, Art 0: eine Zelle mit einer Einheit (`10000..13999`) ist fuer den
Kartenbauer **0 = frei**; nur Festes (`>= 14000`) sperrt. Das Original plant
DURCH und wartet erst beim Fahren (`Can_go` = 1). Wir bauten die Suchkarte aus
`IsFree` und sperrten damit jede besetzte Zelle hart.

`--nav-flut` auf map_01: **921** Zellen erreichbar, aber nur **334**, wenn man
die Karte so ansieht wie die Wegsuche. **587 Zellen gaben die stehenden
Einheiten weg.**

⚠⚠ Das war am 16.08.2026 schon einmal gebaut und am selben Tag zurueckgezogen
(map_NET07: 17 statt 32 angekommen). Zwei Dinge sind anders: damals war es
ERSCHLOSSEN, heute GELESEN — und damals fehlte die zweite Haelfte, der
**50-Schritte-Puffer mit Neuplanung** (sec14, 8000 x 50). Ohne ihn bleibt ein
Weg durch einen Pulk fuer immer ein Weg durch einen Pulk.
⭐ Darum beide zusammen gebaut, jede mit eigenem Schalter.

#### BR.5a ⚠⚠ DAS ERGEBNIS: die Karte kommt NICHT rein, der Puffer bleibt

Gemessen auf **map_04** — 96 eigene Einheiten dicht gepackt auf 8x14 Zellen,
Gegner 40 Zeilen entfernt, Kampagnenkarte (die KI marschiert nicht), Ziel
(12,40), 120 s. Vier Laeufe unter gleichen Bedingungen:

| Variante | Fortschritt oe | gefahrene Zellen | angekommen | tot |
|---|---:|---:|---:|---:|
| alte Karte, kein Puffer | 2,6 | 2901 | 5 | 43 |
| **nur die neue Karte** | 1,6 | **260** | 0 | 3 |
| **nur der 50er-Puffer** | **6,0** | **3055** | **13** | 35 |
| beides zusammen | 1,6 | 260 | 0 | 3 |

**260 gefahrene Zellen statt 2901.** Bei 44 fahrenden Einheiten sind das rund
6 Zellen in 120 s statt 62 — sie kriechen. Der Grund ist genau der, den der
Rueckzieher vom 16.08.2026 genannt hat: plant man DURCH einen Pulk hindurch,
fuehrt fast jeder Weg sofort durch einen Nachbarn, und dort wartet die Einheit
(`Can_go` = 1) auf eine, die selbst wartet.

⚠⚠ **UND EINE EIGENE FEHLDEUTUNG, die hier festgehalten gehoert:** die
niedrige Totenzahl (3 statt 43) hatte ich zuerst als BELEG fuer die neue Karte
gelesen. Sie ist das Gegenteil — wer sich nicht bewegt, kommt nicht in
Reichweite der Gegner. Aufgedeckt hat das erst die neue Fortschrittszahl.

⭐ **Die Messgroesse war der eigentliche Gegner.** »Angekommen« ist eine
Schwelle (`d <= 1`) und springt erst im letzten Augenblick; wer nach der Messzeit
noch faehrt, zaehlt darin wie einer, der nie losgefahren ist — **und ein Toter
zaehlt genauso**. Der `--stuck-check` fuehrt jetzt zusaetzlich den
**Fortschritt** (Startentfernung minus Restentfernung, stetig, kann NEGATIV
werden) und die **gefahrenen Zellen**. Ohne die beiden Zahlen haette ich die
Verschlechterung nicht gesehen — so wie sie am 16.08. niemand gesehen hat.

**Stand:** `--neue-pfadkarte` ist **standardmaessig AUS**, der 50er-Puffer ist
AN (Gegenprobe `--kein-wegpuffer`).

**Was fehlt, und es ist damit benannt:** im Original bleibt ein wartender Fahrer
nicht ewig stehen. Solange bei uns weder ein **Ausweichen des Blockierers** noch
eine **Neuplanung des Wartenden** gebaut ist, ist »durch den Pulk planen« nur
eine andere Art steckenzubleiben. Erst mit diesem dritten Stueck ist die Karte
wieder zu versuchen — der Schalter steht dafuer bereit.

### BR.6 Der Hafen ist ORIGINALGETREU

»Schlachtschiff und Kreuzer spawnen im Hafengebaeude, ich kann sie nicht
anwaehlen.« Bei verstellter Ausfahrt bleibt das Schiff im Dock stehen und
wartet (@0x409CF2/@0x409CF7 ziehen die Spalte direkt um 2 bzw. 4 zurueck); der
Notnagel »irgendeine freie Zelle suchen« wurde bewusst entfernt. Er hat es
selbst geloest, indem er die zwei leichten Kreuzer wegfuhr.
→ **Frage an ihn: soll das GEFECHT eine Meldung bekommen?** Die Kampagne bleibt
still. Derselbe Fall wie der stille Ausbau (BL.4.1).

### BR.7 ⭐ Der Angriffszeiger — kein Fehler im Code

»Das originale Angriffsicon ist auch nicht mehr da.« Die 104 Zeigerbilder sind
vollstaendig da und werden geladen. In **seiner** `settings.cfg` steht
`cursor_hints=false`; `UpdateCursor` steigt damit sofort aus und setzt den
Systempfeil.

⚠ Der Schalter heisst im Einstellungsschirm »**Zeiger zeigt an, was ein Klick
tut**«. Das liest sich wie eine Hilfefunktion — nicht wie »die Mauszeiger des
Originals ueberhaupt«. **Wer ihn ausschaltet, verliert das Fadenkreuz mit den
vier roten Dreiecken und weiss nicht, warum.**
→ **Vorschlag an ihn: Beschriftung aendern** (etwa »Original-Mauszeiger
(Fadenkreuz, Hand, Pfeile)«), oder den Schalter ganz streichen. Seine
Entscheidung, weil es Oberflaeche ist.

### BR.8 ⚠ NOCH OFFEN: die Kampagnen-KI greift nicht an

»zumindest je nach event/kartenbereich«. Der Sichtringdurchlauf `AiSweep`
(← `ai_units` @0x4BF4E0) LAEUFT in der Kampagne, und zwar vor der
Kampagnensperre `AiGesperrt`. Warum er stumm bleibt, ist **noch nicht
gemessen**. Verdacht steht in AZ.3: `sec62` fehlt im Ausleser, dadurch
`sec110 == 0`.

### BR.9 ⭐⭐ Der »Minenleger« schiesst — und das ist GELESEN, nicht erfunden

Gemeldet: »die Einheit Minelayer schiesst irgendwas auf sehr hohe Distanz, was
keinen Sinn macht, weil er ja ein Minenleger ist«. Nachgefragt: **die KI hatte
ihn auf net02 im Gefecht GEBAUT** — er kam also nicht von der Karte.

Der erste Verdacht war ein erfundener Rueckfall, und einer steckte auch dahinter
(siehe den Commit »Wer keinen eigenen Reichweitenwert hat, schiesst nicht
mehr«). Er ist es aber NICHT. Die Bauteiltafel des Originals fuehrt:

```
weapons.json 35: {"name": "Minenleger", "row": 15, "kind": "weapon",
                  "damage": 100, "range_raw": 80, "range_tiles": 8}
```

Der Entwurf `MineLayer` (sec47) traegt `weapon: 15` — und Zeile 15 der
Waffenspalte IST dieser Eintrag. **Der Minenleger ist im Original eine Waffe
mit Schaden 100 und acht Zellen Reichweite.**

⚠ **Die Frage ist damit eine andere geworden: was TUT sein Schuss?** Eine Mine
zu legen ist im Original vermutlich als Geschoss gebaut — bei uns fehlt die
Minenmechanik ganz (`--mine-check` prueft die ROHSTOFFmine, nicht die Landmine).
Also feuert er bei uns als gewoehnliches Geschuetz mit 100 Schaden.

⚠ Und zur »sehr hohen Distanz«: acht Zellen sind nicht viel — aber die
Reichweite misst seit dem 22.08. **elliptisch** (x·40, y·20). Acht Zellen weit
heisst waagerecht acht Spalten, senkrecht aber **sechzehn Zeilen**. Von oben
gesehen sieht das nach viel mehr aus, als die Zahl sagt.

**Zu entscheiden:** die Landmine als Teilsystem bauen (Geschoss legt Mine statt
Schaden), oder den Minenleger bis dahin stummschalten. ⭐ Das Erste ist der
Nachbau, das Zweite ein Notbehelf — und ein Notbehelf, der still bleibt, ist
genau die Sorte, die spaeter niemand mehr findet.

### BR.10 ⭐⭐ Seine Frage nach Spieler 0 hat die ÜBERNAHME aufgedeckt

Gefragt: »ist das überhaupt abhängig vom Startpanzer, weil man ja in Kampagne 1
nach der Brücke die Neutralen zu seinen macht — zählen die da etwa nicht dazu?«

**Für die Bedingung selbst: nein.** `mission_logic.py` deutet hier nichts, es
rechnet: `(addr − ENT_BASE) / 78` → **Satz 0, Feld +1**. Das Original liest
buchstäblich das Zeilenbyte von Einheitensatz 0. Kein Durchlauf, keine
Spielerabfrage.

⚠⚠ **ABER: DAS ORIGINAL VERSCHIEBT DEN SATZ BEI DER ÜBERNAHME, UND WIR NICHT.**

Gelesen (06.08.2026, im Archiv, nicht in dieser Datei — deshalb hier nachgetragen):

* `takeover_scan` @0x411270 findet den Nachbarn und ruft
* `add_change_owner` @0x410F40 — Warteschlange, 1000 × 4 B bei `0x53c938`
  (u16 Platz, u8 Spieler, 0xFFFF = frei), Fehlertext »Too many change owners«
* **Der Abarbeiter @0x411000** läuft jeden Takt: sucht **im 1000er-Block des
  NEUEN Spielers einen freien Platz** (`+0x09 == 0xFF`), **kopiert den ganzen
  78-Byte-Satz** hinüber, **gibt den alten frei** und stempelt die imap neu.

Unser `Takeover.Join` setzt `Owner` und `Team` — mehr nicht. Der Kommentar
darüber sagt sogar »before the record moves«, und genau das Verschieben fehlt.

**Was daran hängt:**

1. Der Besitzer ist im Original `slot / 1000`. Nach einer Übernahme stimmt das
   bei uns nicht mehr — eine Einheit des Spielers 0 trägt weiter Platz 7003.
2. `ai_units(spieler, block)` läuft über den 1000er-Block des Spielers. Im
   Original sind übernommene Einheiten darin, bei uns nicht.
3. ⭐⭐ **Und der Fall, der Kampagne 1 betrifft:** stirbt der Startpanzer, wird
   Satz 0 im Original **frei** (`+0x09 = 0xFF`) — und die nächste übernommene
   Einheit bekommt genau diesen Platz. `unit_pos(0, +1)` liest dann IHRE Zeile,
   und die Kette kann weiterlaufen. Bei uns ist Satz 0 danach für immer weg,
   und die Nebenmission ist tot.

⭐ **Damit ist seine Frage beantwortet und zugleich nicht:** die Neutralen zählen
für die Bedingung nicht — **es sei denn**, sie sind in Satz 0 nachgerückt. Genau
das kann unsere Fassung nicht.

**Zu bauen:** die Warteschlange und der Abarbeiter, mit freier Platzsuche im
Zielblock, Satzkopie, Freigabe des alten Platzes und imap-Neustempelung.

### BR.11 ⚠⚠⚠ MISSION 1 FEUERT KEINE EINZIGE REGEL — die Tore sind verklemmt

Gesucht wurde, warum die 50 $ ausbleiben. Gefunden wurde etwas viel Groesseres.

```
tick-check: Mission 1 nach 60,0 s ohne Zutun — Takt 3000/3000,
Blockdurchlaeufe 30/30, 0 Regeln gefeuert, 33000x ein Tor zu
```

**Null Regeln.** Nicht die Geldkette, nicht die siebzehn Tutorialtexte
(@0x49844D..0x4989E9 ruft show_text/show_text2 SIEBZEHNMAL), nichts. Das Skript
LAEUFT — 3000 Takte, 30 Blockdurchlaeufe —, aber jeder Durchlauf prallt ab.

**Die Verklemmung, an den Daten:**

```
Tor 0x498457..0x4984CD   WENN ticks>10 & var0!=0 & var0!=50 & var0<50
```

Die Regeln 0 (@0x498471), 1 (@0x49847A) und 2 (@0x4984A9) liegen ALLE in
diesem Bereich. Und **Regel 1 ist genau die, die `var0` von 0 auf 1 setzt** —
sie zeigt den ersten Tutorialtext. Das Tor verlangt aber `var0 != 0`, um den
Bereich zu betreten. Das Tor braucht also, was nur hinter ihm gesetzt wird.

**Der Verdacht, und er ist noch nicht belegt:** die drei Tore
(0x498457..0x4984CD, 0x498467..0x49849B, 0x498471..0x4984CD) tragen ALLE
dieselben vier Bedingungen — und dieselben vier stehen noch einmal als `when`
der Regel 0. Das sieht danach aus, als haette `mission_logic.py` **eine einzige
Vergleichskette doppelt verbucht**: einmal als Bedingung der Regel 0 und einmal
als Tor mit dem Sprungziel der ganzen Kette. Dann waere `bis` zu weit gefasst
und schluckt die zwei Regeln dahinter.

⚠ Ob das so ist, entscheidet nur der Block selbst. **Zu tun: 0x498457..0x4984CD
in BEIDEN GAME.EXE befehlsweise lesen und die Sprungziele nachtragen** — nicht
die Heuristik im Leser zurechtbiegen.

⚠⚠ **Und warum das monatelang niemand sah:** `GatesClosed` wird seit jeher
gezaehlt und war **nie gedruckt**. `RulesFired == 0` sieht genau so aus wie
»die Mission hat gerade nichts zu tun«. Die Zahl steht jetzt in `TickLine`.
⭐ Dazu ein zweiter Prueffehler am selben Tag: die Zeile »ausgeloest: 0 Texte,
0 Geldbuchungen« misst NICHTS, wenn `--tick-check=N` mit einem gleich langen
echten Lauf kombiniert wird — `TickCheck` haengt seine Zaehler erst an und ruft
dann `Advance(N − bereits gelaufene Takte)`, also null Takte. Ich habe mich
zweimal darauf gestuetzt.

### BR.12 ⭐⭐⭐ DER BLOCK IST GELESEN: es ist ein VERTEILER, kein Tor

Gelesen in **beiden** Auslieferungen, befehlsgleich — C @0x498452, F @0x497D5C.
Die Form ist in beiden dieselbe (`aekernel-tools/adis.py`, Fingerabdruck
»`cmp eax,0xa` gefolgt von `movsx eax,word[imm32]`«: **genau eine** Stelle je
EXE):

```
        cmp   eax, 0xa
        jle   ENDE            ; ← DAS EINZIGE ECHTE TOR: Takt > 10
        movsx eax, word [var0]
        test  eax, eax
        je    ARM1            ; var0 == 0  -> springt VORWAERTS IN den Bereich
        cmp   eax, 0x32
        je    ARM2            ; var0 == 50 -> springt VORWAERTS IN den Bereich
        cmp   word [var0], 0x32
        jge   ENDE
        inc   word [var0]     ; die Regel, der die vier Bedingungen gehoeren
        jmp   ENDE
ARM1:   show_text(350,250,1) ; var0 = 1
ARM2:   wenn window_open(1) zu: close_texts; show_text(370,270,2); var0 = 51
```

⭐⭐ **DIE REGEL, DIE DARAUS FOLGT, UND SIE GILT FUER ALLE 33 MISSIONEN:**

> **Ein bedingter Sprung, dessen Ziel INNERHALB des Bereichs liegt, ist ein
> VERTEILERARM — kein Tor. Nur ein Sprung ans Bereichsende sperrt.**

Alle drei Tore von Mission 1 fallen darunter: zwei springen unmittelbar auf die
Regeln 1 und 2, und das dritte (`jge ENDE`) ueberspringt Regeln, die ueberhaupt
nur ueber diese Sprünge erreichbar sind. Der Leser hat die Regeln RICHTIG
erkannt und dieselbe Vergleichskette ZUSAETZLICH als Tor verbucht.

**Die Bilanz ueber die Missionen** (60 s, frischer Spielstand, ohne Zutun):

```
M1:  0 Regeln gefeuert, 33000x ein Tor zu     tot
M10: 0                   3000x               tot
M16: 0                  24000x               tot
M19: 0                  15000x               tot
M15: 0                      0x               andere Ursache
M3:  12054               9049x               lebt
```

⚠⚠ **UND DAS IST DIE ANTWORT AUF SEINE FRAGE »das muessen wir besser
hinbekommen«.** Der stehende Prueflauf ueber alle 33 Missionen lautet:

    fuer m in 1..33: --campaign=$m --no-briefing --tick-check=60
    -> 33 von 33 »ohne Zutun«

**Er misst, dass NICHTS geschieht.** Eine Mission, deren Skript vollstaendig tot
ist, besteht ihn makellos — sie ist ja besonders ruhig. Der Prueflauf war die
ganze Zeit gruen, waehrend vier Missionen stillstanden.
⭐ Ab jetzt gehoert `RulesFired` und `GatesClosed` in dieselbe Zeile, und der
Lauf muss verlangen, dass eine Mission ihre Regeln auch WIRKLICH feuert.
