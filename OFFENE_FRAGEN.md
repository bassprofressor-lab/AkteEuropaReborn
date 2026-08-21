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
Leser), sec108 (KI-**Angriffsgruppen**, »Attack group not available«), sec110 /
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
| **1 … 34** | 3 574 | **nummerierte Linienzüge** — ⚠ ungeklärt, s. u. |
| **98** | 1 102 | Einzelzellen; 90,5 % tragen `0xFFFF` (gesperrt) in sec6 |
| **99** | 322 | **Türzelle eines Gebäudes** |
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

### Das Ergebnis: 130 Abschnitte, ein einziger wirklich toter

| | |
|---|---|
| Abschnitte erhoben (beide EXE) | **130** |
| **ohne jeden Benutzer ausser Lader und Speicherer** | **1 — sec36** |
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
| Satzweite 214 gültig | **780/780 = 100 %** | 212: 2,6 % · 213: 4,6 % · 215: 14,6 % |
| Streckenzelle → Bit gesetzt | **3577/4961 = 72,1 %** | Index vertauscht 1,69 % · **Zufallszelle 0,73 %** |
| Fehlstellen je Linie | genau vorn 2 + hinten 3, **215/215** | — |

⚠ **Das war für uns keine Neuentdeckung, sondern eine Bestätigung über Kreuz:**
unsere `RailLine` führt seit langem `Bud1`, `Bud2`, `Steps` und `Faze (+0xd5)` —
genau diesen Satz. Neu sind die **Endpunkte** `+0x02…+0x05`, die **Schritttafel**
`0x5043C0` und `sec35` vollständig.

⚠ Offen: warum 42 von 215 Linien **kein** Bit tragen, und dass 40 % der gesetzten
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
⭐ `sec77[p] == 1 ⇒ sec53[p].Zustand == 1` (aktive KI) in **28 von 28** — geprüft
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
