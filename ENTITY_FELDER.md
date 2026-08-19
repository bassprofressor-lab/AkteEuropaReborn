# Der Einheitensatz — mit den Namen des Spiels

**Stand 19.08.2026.** Diese Tafel ist nicht abgeleitet und nicht gedeutet. Sie
steht so im Programm: das Original hat einen eigenen Aufzeichner
(Marken `RECORDING` / `REPLAY`, Rumpf `0x416CCA..0x417EF9`), der je Takt jedes
Feld jeder Einheit mit **seinem Namen** in eine Textdatei schreibt.

## Wie sie gewonnen wurde

Der Aufzeichner schreibt Paare aus Name und Wert. Im Maschinencode heisst das:
eine Absolutadresse aus dem Einheitensatz, und **genau fünf Byte danach** ein
`push` auf die Namenskette. Gesucht wurde nach dieser Form — nicht nach festen
Adressen.

**Die Probe:** dasselbe wurde in **beiden** GAME.EXE dieses Rechners gemacht.
Der Sockel des Einheitensatzes ist verschieden (`0x6E26C8` gegen `0x6E1728`),
also wurde nicht die Adresse verglichen, sondern der **Abstand zu dem, was
`RX` liest**. Ergebnis: **56 Namen in beiden Fassungen, 46 davon mit identischem
Versatz.** Die zehn übrigen sind gar keine Einheitenfelder — es sind Namen aus
anderen Tafeln (`rail`, `Budova`, `DEF_robots`, `ZK` …), die zwischen den
Fassungen um einen anderen Betrag gewandert sind. Dass der Prüfstand sie
aussortiert statt sie mitzuzählen, ist der eigentliche Beleg dafür, dass er
misst und nicht bestätigt.

## Die Tafel

| Versatz | Name des Spiels | tschechisch, sinngemäss | unsere Deutung |
|---|---|---|---|
| `+0x00` | `RX` | — | Spalte ✔ |
| `+0x01` | `RY` | — | Zeile ✔ |
| `+0x02` | `OT_PODV` | *otočení podvozku* — Drehung des **Fahrwerks** | `Facing` ✔ |
| `+0x03` | `OT_HLAV` | *otočení hlavně* — Drehung des **Rohrs** | `Aim` ✔ |
| `+0x04` | `POHYB` | Bewegung | — |
| `+0x06` | `KOLIK` | »wieviel« — der Fortschritt in der Zelle | ✔ (der Zähler bis 80) |
| `+0x08` | — | | `Hp` |
| `+0x09` | `faze` | Phase | ⚠ wir prüfen `== 0xFF` als **leerer Platz** |
| `+0x0A` | — | | Gattung (Schiff = 4/5) |
| `+0x0B` | `SPODEK` | **Unterteil** | `Chassis` ✔ |
| `+0x0C` | `VRSEK` | **Oberteil** | `Weapon` ✔ (der Turm *ist* die Waffe) |
| `+0x0D` | `ZBRAN` | **Waffe** | ✔ (seit dem Schiffsbefund bekannt) |
| `+0x0E` | `top_spec` | besonderes Oberteil | `Comp0E` |
| `+0x0F` | `l_engine` | Antrieb | `UnitType` ✔ — die »Rumpftypen« 160…175 **sind** die Antriebe |
| `+0x11` | `ANIM_SPODEK` | Bildlauf des Unterteils | `Comp11` |
| `+0x13` | `POD` | *pod* — darunter | — |
| `+0x14` | `UKOL` | **Auftrag** | ✔ |
| `+0x15` | `AKCE` | Aktion | — |
| `+0x16` | `OTACIM` | »ich drehe« | — |
| `+0x17` | `OTOC_HLAVEN` | »dreh das Rohr« | — |
| `+0x18` | `CX` | Zielspalte | — |
| `+0x19` | `CY` | Zielzeile | — |
| `+0x1A` | `DALSI_SMER` | nächste Richtung | — |
| `+0x1B` | `OZNACEN` | **markiert** = ausgewählt | — |
| `+0x1C` | `CEKANI` | Warten | — |
| `+0x1D` | `SMX` | Richtung X | — |
| `+0x1E` | `SMY` | Richtung Y | — |
| `+0x20` | `speed` | | `Speed` ✔ |
| `+0x26` | — | | `Attack` |
| `+0x27` | — | | `Defence` |
| `+0x28` | `exp` (obere Hälfte) | Erfahrung | **Rang** ✔ |
| `+0x29` | — | | `HpMax` |
| `+0x2B` | `range` | Reichweite | ✔ |
| `+0x2E` | — | | Sprit |
| `+0x32` | `NABYTO` | **geladen** | — |
| `+0x34` | `STRILI_NA` | »schiesst auf« | — |
| `+0x36` | `UTOK_NA` | »greift an« | — |
| `+0x3D` | `RELOAD` | Nachladezeit | `Reload` ✔ |
| `+0x40` | `trans` | Transport | ⚠ wir haben keinen Transport |
| `+0x43` | `rob_prod` | Roboterfertigung | — |
| `+0x4A` | `inter1` | | — |
| `+0x4B` | `inter2` | | — |
| `+0x4C` | `exp` (untere Hälfte) | Erfahrungs**punkte** | ✔ |

Weitere Namen, die der Aufzeichner führt, deren Feld sich aber nicht über die
Fünf-Byte-Form binden liess (sie werden über ein Register geholt): `ANIM`,
`ANIMS`, `strela` (Geschoss), `cil` (Ziel), `weap`, `chas`, `spec`, `anga`,
`sklad` / `max_sklad` (Lager), `robot`, `activ`, `jedu` (»ich fahre«), `dalsi`,
`znak` (Zeichen), `pin`, `zdroj0…3` (Quellen), `DALSI_SMER_CESTA`,
`pro_left_fce`, `CPU0`, `CPU1`.

## Was diese Tafel bestätigt hat

* `+0x02` ist der **Rumpf**, `+0x03` der **Aufbau** — unser Importeur trennt
  beides seit jeher richtig (`Facing = raw[0x02]`, `Aim = raw[0x03]`). Damit
  löst sich auch der Widerspruch um die sechzehn Schiffsrichtungen auf, siehe
  `OFFENE_FRAGEN.md`, Abschnitt C.
* `+0x3D` heisst wirklich `RELOAD`. Die Erfahrungsformel stützt sich darauf.
* `+0x28` und `+0x4C` sind **eine** Grösse: das Spiel druckt sie als
  `exp: (byte[+0x28] << 8) | byte[+0x4C]`.
* `+0x0F` trägt den **Antrieb**. Dass wir das Feld `UnitType` nennen und die
  Werte 160…175 die Antriebsnamen (Spinne, Reifen, Ketten …) tragen, ist damit
  kein Zufall, sondern richtig.

## Was noch NICHT gemacht ist

Der Aufzeichner selbst ist **nicht nachgebaut**. Er schreibt `replay.beg`
(Anfangszustand), `replay.mes` (Befehlsstrom) und `replay.txt` (Protokoll) und
vergleicht beim Abspielen Takt für Takt. Das ist der Prüfstand, an dem
»Mehrspieler online« hängt — und der Grund, ihn zu bauen, ist genau diese
Tafel: sie sagt, **welche** Felder je Takt übereinstimmen müssen.
