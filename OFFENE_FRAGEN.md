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

### NICHT GEBAUT: die Wurfbahn

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

### NICHT GEBAUT: die acht Einschlagszweige

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

### NICHT GEBAUT: Transport

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

### ⭐ Sektion 20, die Zeichenlage — GEBAUT, aber ⚠ NOCH WIRKUNGSLOS

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

### Der Transport — vorbereitet, aber ⚠ noch nicht gebaut

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

### ❓ Was ich brauche: welcher Satz gilt bei einer Kollision?

Auf `08.CWM` beanspruchen **zwei** Sätze dieselben drei Einheiten (23, 24, 28),
und beide bestehen die Zeigerprobe:

* Satz 7, Träger 1 (Rumpf 73, Zelle 19,51): der **lückenlose** Lauf 15…29 —
  genau seine 15 Plätze voll.
* Satz 3, Träger 3 (Rumpf 72, Zelle 16,50): 23, 24, 28 — drei verstreute
  Stücke **daraus**.

Satz 3 sieht aus wie eine Leiche vom Umladen. **Meine Setzung: der erste
Anspruch gilt** — betrifft 3 von 65 Frachtplätzen und nur diese eine Karte.
Aus den Daten ist nicht zu entscheiden, welchen das Original nimmt.

**Was mir hülfe:** In Mission 8 die beiden Schiffe bei (19,51) und (16,50)
anwählen und nachsehen, wieviel jedes trägt — 15 und 0, oder 12 und 3? Das
entscheidet es in einer halben Minute, und ich müsste nichts raten.
