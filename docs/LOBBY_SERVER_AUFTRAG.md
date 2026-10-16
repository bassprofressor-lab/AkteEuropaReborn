# Auftrag: Lobby- und Relay-Server für »Akte Europa Reborn«

*Dieses Dokument ist der vollständige Auftrag für die Claude-Instanz auf dem
Server. Es beschreibt, WAS gebaut wird und WIE Spiel und Server miteinander
sprechen. Das Spiel (Godot 4.7 / C#) wird gegen genau dieses Protokoll gebaut —
Änderungen am Protokoll bitte nur über eine neue Protokollnummer und mit
Rückmeldung.*

Stand: 16.10.2026 · Protokoll **1**

---

## 0. Worum es geht

»Akte Europa Reborn« ist ein originalgetreuer Nachbau eines Echtzeit-Strategiespiels
von 1997. Der Mehrspieler läuft im **Lockstep**: jeder Rechner rechnet die ganze
Partie selbst, über die Leitung gehen nur die **Befehle** der Spieler (wenige Byte
pro Takt). Damit alle dasselbe rechnen, müssen alle dieselben Befehle in
**derselben Reihenfolge** bekommen.

Der Server hat zwei Aufgaben:

1. **Lobby mit Spielersuche** — Spieler melden sich mit einem Namen an, sehen die
   offenen Spiele, eröffnen eins, treten bei, chatten, stellen ihren Platz ein,
   melden sich bereit, und der Gastgeber startet.
2. **Relay während der Partie** — der Server reicht die Spielnachrichten
   unverändert weiter und gibt dabei **jeder Nachricht eine laufende Nummer**.
   So bekommen alle Rechner alles in genau derselben Reihenfolge, und niemand
   braucht eine Portfreigabe im Router (alle verbinden sich nur nach außen zum
   Server).

**Der Server versteht das Spiel NICHT.** Spieleinstellungen und Spielnachrichten
sind für ihn undurchsichtige Daten (JSON-Objekt bzw. Bytes), die er nur prüft
(Größe, Rate) und weiterreicht. Dadurch muss der Server nicht angepasst werden,
wenn sich das Spiel ändert.

---

## 1. Technik (Empfehlung — Abweichung bitte begründen)

| Punkt | Vorgabe |
|---|---|
| Sprache | **TypeScript auf Node.js 22 LTS** mit dem Paket `ws` (oder Go mit `nhooyr/websocket`, falls auf dem Server schon Go läuft) |
| Verbindung | **WebSocket über TLS** (`wss://`), ein einziger Pfad: `wss://<domain>/ae/v1` |
| TLS | über einen Reverse-Proxy — **Caddy** (holt das Zertifikat selbst) oder nginx + certbot, falls schon vorhanden |
| Betrieb | **Docker Compose** (Dienst `ae-lobby` + Proxy), Neustart `unless-stopped` |
| Speicher | **kein Datenbankzwang**: Lobby und Räume leben im Arbeitsspeicher. Ein Neustart beendet laufende Partien — das ist für Fassung 1 in Ordnung |
| Gesundheit | `GET https://<domain>/ae/health` → `200 {"ok":true,"version":"…","proto":1,"online":n,"rooms":n}` |
| Protokolle | Konsole/`docker logs`, eine Zeile je Ereignis (Anmeldung, Raum auf/zu, Start, Ende, Abbruch, Regelverstoß). **Keine IP-Adressen dauerhaft speichern**, keine Chatinhalte protokollieren |

Rahmen, die der Server durchsetzt:

* Textrahmen (JSON) höchstens **8 KiB**, Binärrahmen höchstens **4 KiB**.
* Höchstens **60 Rahmen pro Sekunde** je Verbindung (Spielrahmen eingeschlossen);
  darüber: `error` mit `code:"rate"`, bei Wiederholung trennen.
* **Ping alle 15 s** (WebSocket-Ping); ohne Antwort nach 45 s gilt die Verbindung
  als verloren.
* Höchstens **8 Spieler je Raum**, höchstens **200 Räume**, höchstens
  **2000 Verbindungen** (Werte als Umgebungsvariablen einstellbar).
* Namen: 1–20 Zeichen, Buchstaben/Ziffern/Leerzeichen/`-_.`, am Rand
  beschnitten; doppelte Namen online bekommen eine Endung `#2`, `#3` …
* Chat: höchstens 200 Zeichen je Nachricht, höchstens 5 Nachrichten in 10 s.

---

## 2. Ablauf in Kürze

```
Spiel                                Server
  |-- verbindet wss://…/ae/v1 ------->|
  |-- hello {name, version, …} ------>|
  |<-------------- welcome {you, …} --|
  |-- list_rooms -------------------->|
  |<--------------- room_list [ … ] --|      (danach Aktualisierungen von selbst)
  |-- create_room / join_room ------->|
  |<-------------- room_state { … } --|      (bei jeder Änderung an alle im Raum)
  |-- set_slot / set_ready / chat --->|
  |-- start (nur Gastgeber) --------->|
  |<----- match_start {seed, …} ------|      (an alle im Raum)
  |== Binärrahmen (Befehle) ========>|
  |<= Binärrahmen mit laufender Nr. ==|      (an ALLE im Raum, auch an den Absender)
  |-- match_end {result} ------------>|
  |<----------------- room_state -----|      (Raum wieder »lobby«)
```

---

## 3. Textnachrichten (JSON)

Jede Nachricht ist ein JSON-Objekt mit dem Feld `"t"` (Typ). Antworten auf eine
Anfrage tragen deren `"rid"` (Anfrage-Nummer, vom Spiel frei vergeben) zurück,
falls eine mitgeschickt wurde. Unbekannte Felder werden ignoriert (Vorwärts-
verträglichkeit).

### 3.1 Anmeldung

**Spiel → Server**
```json
{"t":"hello","proto":1,"name":"chr1zZo","game_version":"0.7.0",
 "content_hash":"a1b2c3d4","resume_token":null}
```
* `game_version` — Spielfassung; der Server bildet daraus die **Spielgruppe**:
  Räume sind nur für Spieler mit **gleicher** `game_version` sichtbar und betretbar.
* `content_hash` — Prüfsumme der eingelesenen Originaldaten (8 Hexzeichen). Wird
  mitgeführt und in `room_state` gezeigt; das **Spiel** entscheidet, ob es bei
  Abweichung warnt. Der Server vergleicht nichts.
* `resume_token` — siehe 3.6 (Wiederverbinden).

**Server → Spiel**
```json
{"t":"welcome","you":{"id":"p_7f3a","name":"chr1zZo"},
 "resume_token":"…zufällig, 32 Byte base64url…",
 "server_version":"1.0.0","motd":"Willkommen!","online":12}
```
Fehler: `{"t":"error","code":"proto","msg":"…"}` (falsche Protokollnummer, Text
mitschicken, was der Server kann) und trennen; `code:"name"` bei ungültigem Namen.

Bevor `hello` angekommen ist, nimmt der Server keine andere Nachricht an.

### 3.2 Lobby: Raumliste

**Spiel → Server**: `{"t":"list_rooms"}` — abonniert die Liste.
**Server → Spiel**: einmal die ganze Liste, danach nur Änderungen:
```json
{"t":"room_list","rooms":[
  {"id":"r_12","name":"Freitagsrunde","host":"chr1zZo","map":"map_NET02",
   "players":2,"max":4,"password":false,"state":"lobby","created":1792140000}]}
{"t":"room_added","room":{…}}
{"t":"room_changed","room":{…}}
{"t":"room_removed","id":"r_12"}
```
`state`: `"lobby"` (betretbar) oder `"in_game"` (läuft, nicht betretbar).
`map` ist ein Text aus den Raumeinstellungen (`settings.map`, s. u.), sonst `""`.
`{"t":"unlist_rooms"}` beendet das Abo (z. B. sobald man in einem Raum ist).

### 3.3 Raum eröffnen, beitreten, verlassen

```json
{"t":"create_room","rid":1,"name":"Freitagsrunde","max":4,"password":"",
 "settings":{"map":"map_NET02","money":5000,"tech":8}}
{"t":"join_room","rid":2,"id":"r_12","password":""}
{"t":"leave_room"}
{"t":"kick","player":"p_9c1d"}               // nur Gastgeber
```
* `max` 2–8. `password` leer = offen; der Server speichert nur einen Hash.
* `settings` — **beliebiges JSON-Objekt** (≤ 4 KiB), nur der Gastgeber ändert es
  (`set_settings`), der Server reicht es unverändert weiter. Einzige Ausnahme:
  `settings.map` (Text) erscheint in der Raumliste.
* Ein Spieler ist in höchstens einem Raum. Wer einen neuen betritt, verlässt den
  alten.
* Verlässt der **Gastgeber** den Raum, wird der dienstälteste Spieler Gastgeber
  (`room_state` zeigt es); ist der Raum leer, wird er gelöscht.

Fehlercodes: `room_full`, `bad_password`, `no_such_room`, `in_game`,
`version_mismatch`, `not_host`, `kicked` (an den Hinausgeworfenen).

### 3.4 Der Raum

**Server → alle im Raum**, nach JEDER Änderung (vollständiger Zustand):
```json
{"t":"room_state","room":{
  "id":"r_12","name":"Freitagsrunde","host":"p_7f3a","max":4,"state":"lobby",
  "settings":{"map":"map_NET02","money":5000,"tech":8},
  "slots":[
    {"slot":0,"player":"p_7f3a","name":"chr1zZo","team":1,"color":0,"ready":true,
     "content_hash":"a1b2c3d4","ping_ms":31},
    {"slot":1,"player":"p_9c1d","name":"KayelGee","team":2,"color":1,"ready":false,
     "content_hash":"a1b2c3d4","ping_ms":48},
    {"slot":2,"player":null,"ai":"normal","team":2,"color":2},
    {"slot":3,"player":null,"ai":null}]}}
```
* `slots` hat genau `max` Einträge. Ein Platz ist **Spieler**, **KI** (`ai` =
  freier Text, z. B. `"leicht"|"normal"|"schwer"`, vom Gastgeber gesetzt) oder **frei**.
* Neue Spieler bekommen den ersten freien Platz.

**Spiel → Server**
```json
{"t":"set_slot","team":2,"color":3}                       // eigener Platz
{"t":"move_slot","to":3}                                  // eigener Platz wechseln (auf freien)
{"t":"set_ai","slot":2,"ai":"schwer","team":2,"color":2}  // nur Gastgeber; ai:null = frei
{"t":"set_settings","settings":{…}}                       // nur Gastgeber
{"t":"set_ready","ready":true}
{"t":"chat","text":"gleich gehts los"}                    // im Raum; außerhalb = Lobby-Chat
```
Jede Änderung an `settings`, `max`, KI-Plätzen oder Plätzen setzt **alle
`ready` auf false** (damit niemand ungewollt startet).

**Server → Spiel**: `{"t":"chat","from":"p_9c1d","name":"KayelGee","text":"…","room":true}`
`room:false` = Lobby-Chat (alle, die nicht in einem Raum sind, und mit gleicher
`game_version`).

### 3.5 Start, Partie, Ende

**Spiel → Server** (nur Gastgeber): `{"t":"start","rid":7}`

Der Server prüft: Raum im Zustand `lobby`, mindestens **2 menschliche Spieler**,
alle menschlichen Spieler `ready`. Dann:

**Server → alle im Raum**
```json
{"t":"match_start","match":"m_5521","seed":2864434397,"start_in_ms":3000,
 "you_slot":1,"room":{… wie room_state …}}
```
* `seed` — **Zufallsstartwert, 32 Bit, vom Server kryptographisch erzeugt.** Alle
  Rechner starten ihren Spielzufall damit.
* `start_in_ms` — Gnadenfrist zum Laden; das Spiel meldet sich danach mit
  `{"t":"loaded"}`. Sobald **alle** `loaded` gemeldet haben (oder nach 120 s die,
  die es geschafft haben), schickt der Server `{"t":"go"}` an alle. Wer bis dahin
  nicht geladen hat, gilt als ausgeschieden (`player_left`, `reason:"timeout"`).
* Raum wechselt auf `in_game` (Raumliste: `room_changed`).

**Während der Partie — Binärrahmen**, siehe Abschnitt 4.

**Ausscheiden, Ende**
```json
{"t":"surrender"}                                    // Spiel -> Server, Spieler gibt auf
{"t":"player_left","player":"p_9c1d","slot":1,"reason":"left|disconnect|timeout|kicked|surrender"}
{"t":"match_end","result":{"winner_team":1,"duration_s":1830}}  // Spiel -> Server
```
* `match_end` schickt **jedes** Spiel, sobald es das Ende selbst festgestellt hat.
  Der Server beendet die Partie, wenn **die Mehrheit** der noch verbundenen
  Spieler dasselbe `result` gemeldet hat, schickt
  `{"t":"match_over","result":{…},"agreed":n,"of":m}` an alle, und setzt den
  Raum zurück auf `lobby` (alle `ready:false`).
* Weichen die Ergebnisse ab: `match_over` mit `"disputed":true` und allen
  gemeldeten Ergebnissen; protokollieren.
* Ist nur noch **ein** Spieler verbunden, endet die Partie nach 60 s von selbst
  (`match_over` mit `"reason":"alone"`).

### 3.6 Wiederverbinden

Reißt die Verbindung ab, hält der Server den Platz **60 Sekunden** frei und
schickt allen im Raum `{"t":"player_lost","player":"…","slot":1,"hold_s":60}`.
Verbindet sich das Spiel neu und schickt in `hello` seinen `resume_token`, bekommt
es seinen Platz zurück:
* in der Lobby: einfach neues `room_state`;
* in der Partie: `{"t":"resume","match":"m_5521","seed":…,"room":{…},"you_slot":1}`
  und danach **alle Binärrahmen ab `"from_seq"`**, die das Spiel in einem
  `{"t":"resync","from_seq":4711}` anfordert. Dafür hält der Server je laufender
  Partie den **ganzen Binärverlauf** im Speicher (Obergrenze 32 MiB je Partie;
  darüber nur die letzten 32 MiB, und `resync` vor dieser Grenze bekommt
  `error code:"history_gone"`).
* Nach 60 s: `player_left` mit `reason:"disconnect"`.

### 3.7 Laufzeit

* `{"t":"ping","rid":n}` → `{"t":"pong","rid":n,"server_ms":…}` (Anwendungs-Ping,
  zusätzlich zum WebSocket-Ping; der Server misst daraus `ping_ms` je Spieler für
  `room_state`, Aktualisierung höchstens alle 5 s).
* `{"t":"error","code":"…","msg":"…","rid":n}` — allgemein.

---

## 4. Binärrahmen (das Relay)

Nur erlaubt, solange der eigene Raum `in_game` ist und `go` verschickt wurde.

**Spiel → Server**: beliebige Bytes `payload` (1–4096 Byte). Der Server liest sie
nicht.

**Server → ALLE im Raum (auch an den Absender)**:

```
Byte 0..3   seq      uint32, little-endian — laufende Nummer je Partie, ab 1, lückenlos
Byte 4      slot     uint8 — Platz des Absenders
Byte 5..8   server_ms uint32, little-endian — ms seit "go" (nur zur Anzeige/Messung)
Byte 9..    payload  unverändert
```

Das ist der Kern: **Die Reihenfolge, in der der Server die Rahmen nummeriert, IST
die verbindliche Reihenfolge für alle.** Der Server muss je Partie strikt der
Reihe nach nummerieren und versenden (eine Schlange je Partie, kein paralleles
Umordnen).

Der Absender bekommt seinen eigenen Rahmen zurück — daran erkennt das Spiel, wann
und an welcher Stelle seine Befehle gelten.

---

## 5. Was der Server ausdrücklich NICHT tut

* Keine Spiellogik, keine Prüfung von Befehlen, keine Siegbewertung (außer dem
  Mehrheitsvergleich der gemeldeten Ergebnisse).
* Keine Konten, keine Passwörter für Spieler, keine E-Mail — Fassung 1 kennt nur
  Namen. (Konten, Ranglisten, Freundeslisten: spätere Protokollnummer.)
* Kein Speichern von Spielständen.

---

## 6. Abnahme — bitte vor der Rückmeldung selbst prüfen

Liefere ein **Testprogramm** (`test/sim.ts` o. ä.), das mit 3 simulierten Spielern
gegen den laufenden Server spielt und Folgendes nachweist (Ausgabe je Punkt
`OK`/`FEHLER`):

1. `hello` mit falscher `proto` → `error proto` + Trennung.
2. Doppelter Name → `#2`.
3. Raum eröffnen → erscheint bei einem Dritten in `room_list`; mit anderer
   `game_version` erscheint er **nicht**.
4. Beitritt mit falschem Passwort → `bad_password`; voller Raum → `room_full`.
5. `set_settings` durch Nicht-Gastgeber → `not_host`; durch Gastgeber → alle `ready:false`.
6. `start` mit einem nicht bereiten Spieler → Fehler; alle bereit → `match_start`
   bei allen mit **gleichem** `seed`, danach `go` nach allen `loaded`.
7. **Reihenfolge**: drei Spieler senden gleichzeitig je 1000 Binärrahmen — alle
   drei empfangen **dieselben 3000 Rahmen in derselben Reihenfolge**, `seq` 1..3000
   lückenlos, jeder Rahmen unverändert, mit richtigem `slot`.
8. Rate-Grenze: 200 Rahmen in einer Sekunde → `error rate`.
9. Verbindung kappen und mit `resume_token` binnen 60 s neu → `resume`, `resync`
   ab einer `seq` liefert die fehlenden Rahmen lückenlos; nach 60 s ohne Rückkehr →
   `player_left disconnect`.
10. Gastgeber verlässt Raum → neuer Gastgeber; letzter verlässt → `room_removed`.
11. `match_end` von 2 von 3 gleich → `match_over agreed:2 of:3`, Raum wieder `lobby`.
12. `GET /ae/health` liefert die Zahlen.

---

## 7. Rückmeldung an mich (das brauche ich, um das Spiel anzubinden)

Bitte am Ende genau das melden:

* die **Adresse**: `wss://…/ae/v1` und `https://…/ae/health`
* `server_version`, Protokoll `1`
* die Ausgabe des Testprogramms (alle 12 Punkte)
* die eingestellten Grenzen (Abschnitt 1), falls abweichend
* wie man den Dienst neu startet, Protokolle ansieht und aktualisiert
  (`docker compose …`), und wo der Quelltext liegt
* jede Stelle, an der du von diesem Auftrag abgewichen bist, mit Grund

---

## 8. Was du vom Betreiber brauchst (bitte zuerst erfragen bzw. prüfen)

* Betriebssystem und ob Docker / Docker Compose schon installiert ist
* eine **(Sub-)Domain**, die per DNS (A/AAAA) auf den Server zeigt, z. B.
  `ae.<seine-domain>` — ohne Domain gibt es kein gültiges TLS-Zertifikat
* ob Port **80 und 443** frei sind oder schon ein Webserver/Proxy läuft
  (dann dort nur einen Pfad `/ae/` weiterleiten statt Caddy neu aufzusetzen)
* Firewall: 80/443 eingehend offen
