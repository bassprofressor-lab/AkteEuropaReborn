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
