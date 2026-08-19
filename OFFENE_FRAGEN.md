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

### Die Auswahlmarkierung ist ein SPRITE, keine vier gezeichneten Linien

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
