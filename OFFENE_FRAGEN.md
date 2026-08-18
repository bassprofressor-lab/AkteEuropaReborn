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

### 1. Vier Untermissionen, die wir nicht erfüllen können

Von ursprünglich acht sind vier übrig. Ihr **Text** steht fest (aus
`HELPG.TXT`), die **Codestelle** auch — was fehlt, ist die Bedingung davor,
die unser Leser nicht liest.

| Mission | Ziel laut Spieltext | Codestelle |
|---|---|---|
| M6 / 1 | »Befreiung des Hadgi Ibn Mustaffa und seine Begleitung zum Lagerzentrum-Nord« | `0x49A81E` |
| M14 / 2 | »Ziel der Mission in 45 Minuten zu beenden« | `0x49D6AB` |
| M20 / 1 | »Entfernung jeglicher Bodensysteme der Droiden« | `0x49F3D6` |
| M24 / 1 | »Beseitigung der Bodeninstallationen« | `0x4A18D4` |

**Was mir hülfe:** was der Spieler dort *tut*, wenn das Ziel umspringt. Bei M6
etwa: wohin genau muss Hadgi gebracht werden, und woran merkt man, dass es
gezählt hat?

### 2. Zwei Untermissionen, die NUR wir erfüllen

Umgekehrter Fall — wir setzen sie auf »erfüllt«, das Original schreibt an der
Stelle keine literale 10.

* **M24 / Ziel 3** — unentscheidbar, dort schreibt nur ein Register.
* **M25 / Ziel 1** — sieht nach unserer eigenen Zutat aus.

**Was mir hülfe:** ob diese zwei Ziele im Original überhaupt erfüllbar sind.

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
