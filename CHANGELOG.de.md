# Änderungen

Alle nennenswerten Änderungen an Akte Europa Reborn. Ausgeliefert wird **nur
die Engine** — Gelände, Einheiten, Karten, Tabellen und Klänge entstehen auf
dem eigenen Rechner aus der eigenen Fassung des Spiels von 1997.

*In English: [CHANGELOG.md](CHANGELOG.md). Ältere Fassungen als 0.5.0 sind
bisher nur dort beschrieben.*

## 0.6.0 — unveröffentlicht

Hierher geht alles, was nach 0.5.0 entsteht. Vier Bereiche, in der Reihenfolge,
in der sie gewählt wurden:

- **Multiplayer online.** Über das LAN hinaus: ein Server als Vermittler,
  Keimverteilung durch den Server, Verzögerungsausgleich, Prüfsummen und
  Wiederverbindung — und davor die Frage, die der LAN-Beleg nicht beantworten
  konnte: Fließkomma-Determinismus zwischen zwei *verschiedenen* Maschinen.
  (Die zweite Frage, die hier stand — die Befehle der Computerspieler durch den
  Befehlsring —, ist beantwortet, siehe unten.)
- **Der Karteneditor.** Einzelne Zellen malen, Einheiten, Gebäude und Gleise
  setzen, die Spielerzahl wählen, vorhandene Karten öffnen — und eine erzeugte
  Karte vollwertig spielbar machen, nicht bloß begehbar.
- **Gefecht.** Der Wettkampfmodus, der es werden soll: die Entscheidung Depot oder
  Warteschlange, die Wirtschaft in der Oberfläche, die Ausgewogenheit dazu.
- **Zug und Strecke.** Die Waggon-Feinlage in Sektion 44 und die Reparaturkette —
  beides erledigt, siehe unten. Offen bleibt der Zug, der an einem Bruch
  explodiert, und eine Linie auf `map_DM_6`, deren Kette nicht zu ihrer
  angegebenen Länge passt. (Die „25 Kettenstellen ohne Nachbarzelle", die hier
  standen, waren ein Lesefehler an unserem eigenen Zähler — auf diesen Karten
  gibt es keine einzige.)

Die Kampagne bleibt originaltreu; Gefecht und Mehrspieler dürfen bewusst
abweichen, und jede Abweichung wird als unsere gekennzeichnet.

### Einheiten

- ⭐ **Der Mechaniker repariert wieder, was neben ihm steht** — und er heißt auch
  wieder so. Gemeldet als »die Reparatureinheit nennt sich noch Bauteil 43 und
  repariert keine Fahrzeuge«. Beides stimmte, und beides hing an einem
  Satzfeld, das wir gar nicht gelesen hatten: **+0x0E**, die Bauteilzeile. Der
  Name kam aus +0x10 (der *dritten* Komponente — Schild, Kamikaze,
  Spiegelbild), und die stand bei einem Mechaniker auf Null.

  Die Wirkung ist jetzt gelesen: die Weiche @0x40730B prüft `byte[+0x0e]` gegen
  **70**, und `mechanic_tick` @0x411F40 ruft alle 30 Takte viermal `repair_at`
  auf — die **vier orthogonalen** Nachbarzellen, nicht die Diagonalen. Was dort
  steht, bekommt einen Punkt (eine Einheit) oder **fünf** (ein Gebäude), ohne
  dass der Besitzer gefragt wird. Gleich daneben steht das **Reparateur**-Modul
  (@0x40731B, `byte[+0x10] == 86`): alle **100** Takte ein Punkt, und nur für
  sich selbst.

  ⚠ Aufgefallen ist es nicht beim Lesen. Ich hatte geantwortet, es sei
  *originalgetreu*, dass diese Einheit nichts repariert — belegt mit den 21
  Fundstellen des Feldes +0x10, von denen nur zwei einen Wert prüfen. Der
  Befund stimmte; er betraf nur das falsche Feld. Gefunden hat es der Spieler,
  indem er die **hauseigene Enzyklopädie** aufschlug: »Mechaniker reparieren
  automatisch alle Einheiten, die sich neben ihnen befinden.« Die Datei liegt
  seit jeher neben dem Spiel und war als Quelle nie gefragt worden.

  Neu ist `--mechaniker-check`: er stellt seinen Fall selbst her (ein
  Mechaniker, ein beschädigtes Fahrzeug daneben, eins diagonal, eins sechs
  Felder weiter) und misst +5/+0/+0 — die Rate, die dasteht.

- ⭐ **Schlachtschiff und Kreuzer springen nicht mehr, sobald sie fahren.**
  Gemeldet: »ich hatte einen Kreuzer mit einer Langstreckenrakete, die Rakete
  wird irgendwo außerhalb vom Boot abgeschossen paar Felder entfernt im
  Wasser«. Der Bericht stimmt auf die Zahl: **1,68 Felder**.

  Die Ursache lag nicht bei der Rakete. Ein Schiff mit 4×4 Grundriss bekommt
  beim Laden seine Lage als **Mitte des Grundrisses**; der **Fahrschritt**
  rechnete danach mit der **Zellmitte** und ließ den Versatz von (60,30) weg.
  Beim ersten Schritt sprang das Schiff also — und mit ihm alles, was an seiner
  Lage hängt: Bild, Mündungsfeuer, Auswahlrahmen, Lebensbalken. Sichtbar wurde
  davon die Rakete, weil sie im leeren Wasser startete.

  ⚠ Betroffen sind **nur mehrzellige** Einheiten — für alles 1×1, also für
  fast jede Einheit, ist die neue Rechnung Zeichen für Zeichen die alte.
  Gemessen mit `--schiff-waffe-check` (neu): Abweichung nach 600 Schritten
  **1,68 → 0,00** Felder; ohne die Reparatur meldet er weiter FEHLER.

  ⚠ **Was NICHT geändert wurde:** dass auf Schlachtschiff und Kreuzer kein
  Geschütz gezeichnet wird. Das ist eine frühere **Entscheidung des Spielers**
  — das Original liest an dieser Stelle selbst eine uninitialisierte
  Stack-Zelle (»Wrong chassis of ship«), ist dort also nicht nachbaubar, und
  gewählt wurde »keine Waffe zeichnen« statt eigener Montagepunkte.

- ⭐⚠ **Befohlene Einheiten geben nicht mehr sofort auf — und eingekeilte
  fahren wieder los.** Gefunden, weil `--befehl-check` ROT meldete; der
  Prüfstand vergleicht den Befehlsring gegen den alten Direktweg. Der Ring ist
  der Weg, den **jeder Klick des Spielers heute geht**, und ihm fehlten drei
  Dinge, die der alte Weg tut:

  - **kein Weg gefunden** → der alte Weg behält das Ziel und versucht es
    wieder (`RetryIn`); der Ring tat gar nichts. Wer von den eigenen Leuten
    eingekeilt stand, stand **bis zum Missionsende**. Das war genau die
    Reparatur vom 16.08. — beim Umbau auf den Ring ist sie nicht mitgekommen.
  - **Weg gefunden** → `RetryIn` wurde nicht zurückgesetzt.
  - **frischer Befehl** → die Geduld (`Block`) blieb auf **0**, also gab der
    Wagen beim **ersten** versperrten Takt auf, statt zu warten.

  Die dritte Zeile erklärt auch den Prüfstand: die Geduld wird **gewürfelt**,
  und ein Weg, der würfelt, neben einem, der es nicht tut, lässt die
  Zufallsströme auseinanderlaufen. ⚠ Nebenbei ist der Wurf jetzt an der
  richtigen Stelle: im **Behandler**, der auf allen Maschinen läuft, statt beim
  Absender, der nur auf einer läuft.

  Gemessen: `--befehl-check` grün in allen vier Varianten (1 Einheit, 8
  Einheiten, Klick auf Nachbarzelle, Befehl bei Takt 200), Giftprobe schlägt
  weiter an, `--stuck-check` meldet 21 von 21 mit Weg.

- ⭐ **Flugzeuge lassen sich anwählen — und im GEFECHT auch steuern.**
  Gemeldet: »außerdem kann ich die Einheiten nicht anwählen. Im Gefecht wäre
  es doch sinnvoll die Einheiten eigenständig zu steuern oder nicht?«

  Das Anwählen gab es schon — aber nur, wo unter dem Flugzeug **gar nichts**
  lag. Ein Flieger über einem fremden Panzer, einem Gebäude oder einem Baum
  war unanklickbar. Jetzt gewinnt er überall dort, wo nichts liegt, das der
  Spieler ohnehin befehligen könnte; dafür sind nur noch die **eigenen**
  Flugzeuge anwählbar.

  Der Flugbefehl selbst ist eine ⚠ **bewusste Abweichung** und steht als
  solche im Quelltext. Belegt ist sie durch einen Negativbefund: das Zielfeld
  eines Flugzeugs (`+0x14/+0x15`) wird an zwanzig Stellen geschrieben, **alle**
  im Flugtakt selbst, **keine** in einem Befehlsbehandler. Im Original handeln
  Flugzeuge selbständig. Die Mechanik dagegen ist gelesen und wird nur
  ausgelöst: Auftrag **1** heißt »flieg nach (x,y)«, und
  `air_back_to_airport` @0x42646D setzt genau diese drei Bytes.

  ⚠⚠ **Die Kampagne bleibt originaltreu.** Der Befehl wird dort **verworfen**,
  und zwar im Behandler, nicht in der Eingabe — eine Sperre in der Eingabe
  wäre auf der zweiten Maschine eines Netzspiels nicht vorhanden. Gemessen:
  im Gefecht »1 Satz abgesetzt, Ziel erreicht«, in der Kampagne »1 Satz
  abgesetzt, **0 beim Behandler angekommen**«.

- ⭐ **Flugzeuge verlassen die Karte nicht mehr.** Gemeldet als »fliegen
  geradlinig Richtung Norden, sogar außerhalb der Map«. Ein Flugzeug ohne Ziel
  fliegt geradeaus — das ist gelesen —, aber begrenzt war nur seine *Zelle*,
  nicht seine *Lage*: das Flugzeug war längst neben der Karte, während seine
  Zelle am Rand klebte. Am Rand fliegt es jetzt heim (`air_back_to_airport`,
  die Antwort des Originals) und macht kehrt, wenn es keinen Flughafen gibt.
  ⚠ Dass man sie im Gefecht nicht anwählen kann, bleibt offen.

- ⭐ **Der Boden eines Gebäudes verdeckt nichts mehr.** Mit Bildern gemeldet:
  »Dort ist immer das Stück Schiene nicht sichtbar, und wenn eine Einheit
  reinfährt, wird sie von der Bodengrafik überdeckt. Die Einheit lässt sich
  trotzdem noch anwählen!« Genau das »noch anwählbar« war der Schlüssel — das
  Fahrzeug war da, es wurde nur übermalt.
  ⚠ **Die naheliegende Erklärung war falsch und ist zurückgezogen:** das
  Gebäudemuster rage über den Grundriss hinaus. Gemessen ist der Grundriss
  **genau** die belegte Musterfläche (Mine 9×6 und Muster bis (8,5), Basis 7×6
  und bis (6,5), Waffen-Fabrik 8×5 und bis (7,4) — **zwölf von zwölf Typen
  deckungsgleich**). Es gibt keinen Überhang.
  Was übermalte, waren die **flachen** Kacheln. Ein Gebäude besteht mehrheitlich
  aus ihnen — Basis **30 von 37**, Waffen-Fabrik 29 von 38, Kraftwerk 23 von 26
  —, und das sind Beton, Schotter, Schatten und Rampe. Im Original sind sie
  **Gelände**: die Stempelschleife @`0x4C97B4` schreibt sie mit der Bereichsmarke
  `+10000` in die Kartenzelle, und der Gebäudezeichner @`0x42B1DE` malt nur ein
  Sprite und die Türen — sechzig Kacheln kommen dort nirgends vor. Bei uns liefen
  sie als Sprite im Zeilenfach ihres Gebäudes mit und deckten zu, was auf ihnen
  stand: auf `map_NET02` **749** solche Kacheln und **80** Gleiszellen darunter,
  auf `map_DM_4` 489 und 58. Sie liegen jetzt in einem eigenen Durchgang unter
  allem Beweglichen. ⚠ Dass wir dabei zwischen flach und aufragend **teilen**,
  ist unsere Setzung — dem Original genügt sein zweites Sprite, das wir nicht
  haben; die Schwelle ist an der Lücke 24/25 px gemessen. Gegenprobe
  `--boden-alt`, Prüfstand `--overdraw-check`.

- **Wann ein Gebäude verdeckt, entscheidet seine TÜR.** Gelesen am Einreiher
  @`0x42FD47`: ein Gebäude **ohne** Tür kommt in die Zeichenzeile `Zeile + 3`,
  eines **mit** Tür in `Zeile + Zeilenversatz der Tür`. Und die Bildlage zieht
  nicht mit — @`0x42FDD5` nimmt denselben Betrag wieder zurück, ausgerechnet
  bleibt für jeden Türwert dieselbe Stelle. **Die Tür verschiebt die
  Malerordnung, nie das Bild.** Die Werte sind je Typ konstant, ausgezählt über
  alle **798** Türen der 23 Karten: Basis (4,2) 73/73, die drei Fabriken zwei
  bei (2,3) und (5,3), Flughafen (5,4) 39/39, Mine (5,3) 49/49, Werft (2,3)
  13/13. Damit gehört die **Basis** ein Fach früher als unsere bisherige feste 3
  und der **Flughafen** eines später. ⚠ Unterm Strich verdeckt dadurch etwas
  mehr, nicht weniger (auf `map_NET02` 727 → 741 Kacheln) — die Änderung ist
  Treue, keine Verbesserung, und sie behebt den Punkt oben nicht.
  Gegenprobe `--tuer-alt`.

- ⭐ **Einheiten verschwinden hinter Gebäuden.** Bisher lag jede Einheit über
  jedem Gebäude — ein Panzer stand auf dem Dach der Basis. Wie es das Original
  macht, ist gelesen: es **verdeckt**, es macht nichts durchscheinend. Es *hat*
  einen durchscheinenden Zeichner (Mischtafel 256×256 bei `0xA3AFB1`), aber der
  hat **genau einen** Aufrufer gegen neun für den normalen, und der hängt an
  einem Gebäudefeld, nicht daran, ob etwas dahintersteht. Stattdessen fächert
  das Original seine Zeichenliste nach Schirmzeile (Verteiler `@0x42C8C0`, 30
  Arten) — Malerordnung.
  Der Rumpf einer Einheit läuft jetzt in demselben zeilenweisen Durchgang mit,
  in dem schon das Gleis lief. Auswahlklammern und Ziellinien bleiben oben: das
  sind Bedienhilfen, keine Weltobjekte. Gegenprobe `--no-unit-occlusion`.

- **Ein gefallener Fusssoldat verschwand nicht mehr.** Gemessen über alle 24
  Sätze und 8 Richtungen: Laufzyklus (Block 0–7) und Stehen (11) sind
  lückenlos, die Sterbebilder nicht — Block 12 in 21, Block 13 in 69 und Block
  14 in 63 von je 192 Fällen tragen höchstens vier Bildpunkte. Sie dekodieren,
  also hat der Export sie als gültige Dateien geschrieben und die Anzeige ein
  leeres Bild gezeichnet. Jetzt werden fast leere Bilder gar nicht erst
  geschrieben, und die Anzeige fällt vom zeitrichtigen Block rückwärts auf den
  letzten zurück, den es für diese Richtung wirklich gibt: **192 von 192**
  (Satz, Richtung) haben ein Leichenbild, 0 ohne.

- **Das Kugelroller-Fahrgestell ist in allen acht Richtungen sichtbar.** Es war
  in sieben von acht unsichtbar, und der Grund lag in einer alten, ausdruecklich
  als ungeklaert vermerkten Stelle des Exporters. Nachgezaehlt ueber den ganzen
  Teilebestand von ROBO.CWR: **35 von 36** belegten Komponenten tragen ihre
  volle Achter-Zeile bei Block 0, **Komponente 9 als einzige bei Block 5** — und
  zwar in allen drei Gruppen. Angesehen sind es dort acht saubere Drehungen
  desselben Fahrgestells, waehrend die fuenf Einzelbilder davor verschiedene
  NEIGUNGEN derselben Ansicht sind. Damit ist die zweite alte Lesart (»die
  belegten Bilder der Reihe nach sind die acht Richtungen«, `copy_units.py`)
  widerlegt. Der Export sucht den Block jetzt, statt ihn zu setzen: vorher 64
  von 65 Fahrgestellen vollstaendig, jetzt **65 von 65**.
  ⚠ Offen bleibt, WARUM dieses eine Teil seine Bloecke andersherum legt — dass
  Block 5 fuer den Kugelroller »ebener Boden« heisst, ist erschlossen und nicht
  gelesen, und seine Hangbilder bleiben deshalb unangetastet.

- **Eine Einheit, die zum Schiessen anhaelt, faehrt jetzt danach weiter.** Bisher
  war der Fahrbefehl weg, sobald ein Ziel in Reichweite kam: die Einheit blieb
  stehen, feuerte — und stand danach fuer immer. Auf map_NET07 war das genau die
  eine von vierzig, die zwei Zellen vor ihrem Ziel liegenblieb. Ebenso behaelt
  jetzt eine Einheit ihr Ziel, wenn im Augenblick des Befehls gar kein Weg frei
  ist (weil die eigenen Leute im Weg stehen), und versucht es eine Sekunde
  spaeter noch einmal, statt den Befehl stillschweigend fallenzulassen.
  ⚠ Ob das Original nach einem Gefecht weiterfaehrt, ist nicht gelesen — das ist
  unsere Setzung, und sie ist als solche ausgeschildert.
- **Achtzehn Kartenbilder trugen ihre Gebäude noch im Boden.** Der Kartenbacker
  lässt ein stehendes Gebäude seit einer älteren Kur **absichtlich** aus dem
  Bild heraus — sonst blieben seine Bildpunkte für immer im Gelände stehen, und
  ein zerstörtes Gebäude könnte nie seine Ruine zeigen. Die Bilder der Kampagnen
  16 bis 33 stammten aus der Zeit **davor**. Aufgefallen ist es dem
  Rundlauf-Prüfstand, der seit Tagen bei 50 von 68 stand: alle achtzehn
  scheiterten **allein am Bild**, die Daten liefen sauber rund. Solange das
  Gebäude steht, sieht man nichts — zwei Aufnahmen derselben Stelle sind
  punktgleich, weil die Engine das Gebäude deckungsgleich darüber zeichnet;
  sichtbar war der Unterschied nur auf der Minikarte, und schädlich wäre er erst
  beim Einsturz geworden. **Eine Neuinstallation war davon nie betroffen** — sie
  backt mit dem heutigen Stand.
- **Ein eingenommenes Werk bekommt jetzt seinen Anfangsbestand.** Die Meldung
  war, dass die Einnahme von Horni in Kampagne 3 nichts bewirkt. Sie bewirkt im
  Original etwas: sobald der Platz einem gehoert und eine eigene Einheit auf der
  Tuerzelle steht, schreibt das Missionsskript dem Gebaeude 180 Waffen- und 127
  Fahrwerkteile ins Lager; Dolni bekommt 330 und 237. Bei uns war die Wirkung
  dieser Regel schlicht **leer** — die Bedingung stand richtig da und tat nichts.
  Dieselbe Form steht zwanzigmal im Spiel, in fuenf Missionen, und sie SETZT das
  Lager statt es zu erhoehen — in Kampagne 33 ist derselbe Befehl deshalb eine
  Strafe statt einer Belohnung. Die Betraege haengen am Taktzaehler des
  Missionsblocks und schwanken um bis zu neunzehn, wie im Original; Kampagne 25
  rechnet mit einem Faktor drei. Gemessen: Kampagne 3 vier von vier, Kampagne 6
  vier von vier, Kampagne 2 zwei von vier.
  Auch Kampagne 33 ist jetzt gebaut: sie rechnet als einzige **quadratisch**
  (der Betrag haengt vom Quadrat des Taktzaehlers ab), und alle zwoelf ihrer
  Bestueckungen treffen ihren Wert.
- **Das Original zaehlt Zellen, nicht Bildpunkte — und der Boden bremst
  niemanden.** Die zweite Haelfte der gemeldeten Frage nach der Geschwindigkeit
  ist gelesen. Jede Einheit fuehrt einen Schrittzaehler, den das Spiel selbst
  »kolik« nennt; er waechst je Takt um die Geschwindigkeit der Einheit, und die
  Zelle ist voll, wenn er 80 erreicht — 120 bei einem Schritt ueber Eck. Mehr
  steht da nicht. Kein Gelaende, keine Steigung: die Bewegungsschleife des
  Spiels von 1997 fasst das Gelaenderaster kein einziges Mal an, und die
  Geschwindigkeit einer Einheit wird im ganzen Programm nur an einer Stelle
  veraendert — bei einem Treffer, wo sie halbiert wird.
  Damit ist unser Aufschlag von 45 % fuer Geroell **zurueckgenommen**; er war
  geraten und falsch. Wichtiger ist aber, was er verdeckt hat: wir sind mit
  fester Bildpunktgeschwindigkeit auf die naechste Zellmitte zugefahren, und in
  der schraegen Ansicht sind die acht Nachbarzellen verschieden weit weg. Je
  nach Himmelsrichtung fuhr dieselbe Einheit deshalb bis zu **doppelt** so lange
  ueber eine Zelle. Jetzt ist es die eine Zahl des Originals: gerade 1, schraeg
  1,503. Gemessen auf map_NET07 an 8275 fertigen Zellschritten, Geroell gegen
  freien Boden 0,999 statt vorher 1,610, schraeg gegen gerade 1,503 statt 1,386.
  Nebenher faellt eine Fliesskomma-Rechnung aus dem Netzspiel heraus: wann eine
  Einheit ankommt, entscheidet jetzt eine ganze Zahl.
- **Waffen haben eine Mindestreichweite, und wir haben sie uebersehen.** Beim
  Nachpruefen der Reichweiten kam ein Feld ans Licht, das seit je in den
  Kartendaten steht und im ganzen Spiel von 1997 genau **einmal** gelesen wird —
  zweiundzwanzig Byte neben der Reichweite, in derselben Entscheidung: zu weit
  weg wird der Schuss verworfen, **zu nah ebenso**. Die Karten bestaetigen es
  ohne Gegenbeispiel: 620 der 4476 Einheiten tragen einen solchen Wert, und er
  ist 620 von 620 kleiner als die Reichweite (3 bei Reichweite 8, 5 bei 14 …) —
  und nur die weit reichenden haben ueberhaupt einen. Ein Geschuetz laesst ein
  Ziel jetzt los, wenn es zu nah herangekommen ist, statt weiter darauf zu
  halten; naeher heranzufahren macht es schliesslich schlimmer. Auf `map_DM_1`
  gemessen: 18 Einheiten mit Mindestreichweite, 30 Ziele deswegen
  fallengelassen, mit `--no-min-range` null. ⚠ Die Meldung sagt ausserdem, wenn
  eine Karte gar keine solche Einheit hat — dort ist die Null kein Ergebnis.


- **Eine Gruppe bleibt an der Engstelle nicht mehr liegen.** Gemeldet als
  »Gruppenauswahl und hintereinander weg fahren wie über brücken lässt einheiten
  nicht mehr fahren, gerade wenn ein Fahrweg durch die brücke blockiert ist,
  weil gerade jemand anders drüber fährt«. Wir haben bisher 0,7 Sekunden
  gewartet, **einmal** einen neuen Weg gesucht und bei Misserfolg den Weg
  weggeworfen — danach stand die Einheit für immer, bis der Spieler von Hand neu
  klickte. Auf einer einspurigen Brücke ist der Weg im Augenblick des
  Neuplanens fast immer belegt, also traf es die halbe Gruppe. Das Spiel von
  1997 macht es anders, und zwar an der Wurzel: seine Bewegungsfrage kennt
  **drei** Antworten, nicht zwei — nein, *ja aber jemand muss ausweichen*, frei.
  Vor einer Wand zu warten ist sinnlos, hinter einer fahrenden Einheit zu warten
  ist genau richtig, und wir hatten beides in einen Topf geworfen. Jetzt wartet
  eine Einheit hinter einer anderen und behält ihren Weg; vor etwas
  Unbeweglichem läuft ein Geduldszähler, und wenn er abläuft, wird neu geplant
  und der Zähler **neu gesetzt** statt aufzugeben. Die Zahlen sind die des
  Originals (15 + Wurf%15 beim Betreten einer Zelle, 40 + Wurf%20 danach, und
  einmal je 60 Takte ein Rütteln, wenn jemand im Weg steht). Gemessen mit dem
  neuen `--stuck-check` über drei Karten, auf denen die Frage stellbar ist:
  **8 liegengebliebene Einheiten vorher, 0 nachher**; `--stuck-check=alt` stellt
  die alte Fassung im selben Programm nach und fällt durch. Der Zwilling bleibt
  über 30 Sekunden bitgleich.

- **Bomben treffen die Mitte des Rumpfes.** Ein Angriff aus der Luft zielte
  bisher auf die Satzzelle einer Einheit — bei einem Schlachtschiff also auf
  seine linke obere Ecke. Das Spiel von 1997 rückt den Zielpunkt nach der
  Gattung des Ziels, und dieser Versatz ist nichts anderes als die Mitte des
  Rumpfes: eine Zelle bei den kleinen, zwei bei den großen. Es ist dieselbe
  Tafel, die auch über die Rumpfgröße entscheidet.
- **Ein Bomber fliegt einen zweiten Anlauf, statt über dem Ziel zu hängen.** Er
  wirft ab, dreht auf einen ausgewürfelten Punkt **10 bis 19 Zellen** weit ab
  (jede Achse mit eigenem Vorzeichen), kommt zurück und greift erneut an — bis
  die Munition leer ist. Auf dem Rückweg wirft er nicht; das ist keine Bequemlichkeit,
  sondern die Sperre des Originals, das den Abwurf an einen Auftragszustand
  bindet, den es beim Abdrehen wegnimmt. Das Spiel von 1997 benennt die Stelle
  selbst mit „Over target while attack".
  ⚠ **Und damit ziehe ich eine eigene Zurücknahme zurück.** Ich hatte gemeldet,
  das Original kenne diese Schleife nicht, und eine frühere Behauptung darüber
  widerrufen. Der Widerruf war selbst falsch: richtig daran war nur, dass die
  zwei Zielversätze die Mitte des Rumpfes sind und kein Überflug. Die Schleife
  steht an einer anderen Stelle — hinter einer Bedingung, die ich damals nicht
  weiterverfolgt hatte, in einem zweiten Verteiler, den das Spiel erst betritt,
  wenn das Flugzeug genau auf seiner Zielzelle steht. Gemessen: 71 Schleifen in
  gut zwei Minuten auf einer Karte mit zehn Bombern.
- **Die Bahn repariert sich wieder.** Ein Fahrzeug mit dem Gleisaufsatz
  arbeitet zwanzig Takte an einem zerschossenen Stück, macht es heil — und
  sucht sich danach **selbst** das nächste auf derselben Linie. Genau so steht
  die Kette im Original; sie hat nur darauf gewartet, dass es bei uns einen Weg
  gab, ihr einen Auftrag zu geben.
- **Ein Schiff belegt jetzt seinen ganzen Rumpf.** Das Spiel von 1997 prüft für
  ein kleines Schiff **vier** Zellen und für ein großes **sechzehn**; wir prüften
  eine. Ein Schlachtschiff belegte damit ein Sechzehntel seiner selbst — zwei
  Schiffe konnten ineinander stehen, und ein Landfahrzeug fuhr durch drei Viertel
  davon. Die Rumpfgröße liegt jetzt in der Wegekarte selbst, damit Belegen und
  Freigeben nicht auseinanderlaufen können.
- **Versorgungshelikopter fliegen heim, wenn niemand mehr Nachschub braucht** —
  so wie im Original. Sie blieben bisher über den versorgten Einheiten stehen.
  Und sie kehren um, solange der Sprit noch für den Rückweg reicht, statt erst
  bei leerem Tank. **Wohin** sie fliegen, war dabei zunächst falsch geraten: wir
  schickten sie zum Nachschub-Posten. Das Spiel von 1997 würfelt stattdessen
  **irgendein eigenes Gebäude** aus und streut das Ziel um bis zu fünf Zellen —
  ohne jede Prüfung, was für ein Gebäude das ist. Damit erklärt sich auch, warum
  ein Heli auf einer Karte ohne Flugplatz heimkommt: er sucht keinen.
  Nachgemessen auf einer Demokarte: von 19 Helis hingen erst 15 ohne Auftrag in
  der Luft, jetzt **keiner** — und die Zahl derer, die daheim stehen, wächst
  über die Laufzeit von 4 auf 9. ⚠ **Unsere Abweichung:** wer gar kein eigenes
  Gebäude mehr hat, bleibt bei uns stehen. Das Spiel von 1997 schickte ihn in
  diesem Fall auf eine ausgewürfelte Kartenzelle — einen Fehler, der dort nur
  deshalb nicht auffällt, weil ohne Gebäude die Partie ohnehin vorbei ist.
- **Ein Fluggerät hatte nur die halbe Drehung.** Der Rumpf stand quer zum
  Flugpfeil, und die Ursache war weder ein Versatz noch ein Zustandsfehler,
  sondern der **Export**: ein Fluggerät besitzt im Spiel von 1997 **sechzehn**
  Bilder, wir haben acht davon exportiert — und zwar nicht jede zweite Stufe,
  sondern **die ersten acht**, also eine halbe Umdrehung. Ein Flugzeug konnte
  gar nicht nach hinten schauen. Belegt an der Teiletafel selbst: die acht
  Luftteile liegen 16 Bilder auseinander, und alle 16 sind verschieden.
  Jetzt werden alle sechzehn exportiert und die Blickrichtung wird in
  **22,5-Grad-Schritten** gerechnet, mit der Formel des Originals. Gemessen: 11
  verschiedene Stufen gleichzeitig am Himmel, 8 davon ungerade — die kann es mit
  acht Bildern nicht geben.
  ⚠ **Damit fällt eine eigene Eichung.** Der zuvor eingetragene „Versatz 2, zwei
  unabhängige Wege" ist zurückgezogen: die Eichung an den Panzern lief auf dem
  halben Sprite-Satz, und der zweite Weg war gar keiner — der Versatz in der
  Formel des Originals ist derselbe, den unsere Richtungsrechnung schon hatte,
  nur in Sechzehnteln. Wer beides anwendet, dreht um 180 Grad zuviel. Es gilt
  jetzt die Rechnung des Originals allein.
  ⚠ Was **bleibt**, ist Original und kein Fehler: eine Drehung geht sechs Grad
  je Takt, eine volle Wendung dauert 60 Takte. Nach einem Zielwechsel fliegt ein
  Heli also bis zu 30 Takte lang seitwärts, bevor er sich ausgerichtet hat.

### Gefecht

- ⭐ **Einheiten lassen sich verkaufen.** Der Befehl gehört dem Original — es
  führt ihn als Eintrag 4 seiner eigenen Befehlsliste (»Angreifen, Bewegen,
  Beschützen, Selbstzerstörung, **Verkaufen**, …«), und damit ist er ein Befehl
  an die *Einheit*, kein Knopf im Marktfenster: man muss dafür nirgendwohin
  fahren.
  Den Preis rechnet das Spiel, nicht der Verkäufer — **30 % des Werts**, und der
  Wert hängt an der Hülle: eine angeschlagene Einheit bringt weniger. Der Dialog
  fragt nur noch »Akzeptieren Sie $X für diese Einheit?«, wörtlich wie das
  Original. Der Laden verlangt umgekehrt **250 %**; die Spanne des
  Geschäftszentrums ist also **8 : 1**.
  ⚠ **Kampagne und Gefecht laufen hier bewusst verschieden.** In der Kampagne
  ist es originalgetreu: die Einheit bleibt stehen, ein Abholer fährt vom
  Kartenrand heran, und erst bei seiner Ankunft gibt es Geld (gemessen: 2,83 s
  auf `map_01`). Im Gefecht kommt das Geld sofort — ein Wettkampfmodus, in dem
  eine Einnahme sechs Sekunden hinter der Entscheidung herläuft, ist schlechter
  zu spielen.
  ⚠ Was der Abholer **nicht** hat, ist ein Bild: ob das Original ihn überhaupt
  zeichnet, ist nicht gelesen, und ihm eines zu erfinden wäre eine Erfindung an
  der sichtbarsten Stelle des Spiels. Seine Fahrt dagegen ist ganz nachgebaut,
  bis zum Abbremsen auf den letzten zehn Feldern.
  Gemessen mit `--sell-check` in beiden Modi, mit Gegenprobe in beide
  Richtungen: `$4000 → $4058` nach dem Warten, `$44850 → $44917` sofort.

- ⭐ **Der Laden füllt sich nach.** Alle zwei Sekunden sieht das
  Geschäftszentrum nach, ob noch fünfzehn Stück ausliegen; wenn nicht, kommen
  bis zu neun neue dazu. Und was dabei entsteht, ist keine Zufallsauswahl,
  sondern der Spannungsbogen des Marktes: das Spiel führt zwei Listen — die
  Entwürfe, deren Bauteile der Spieler schon hat, und die, die er noch **nicht
  bauen kann** —, und hält von den zweiten immer etwa zwei im Regal. In
  Mission 1 hat er von 52 Fahrzeugentwürfen **keinen einzigen** freigeschaltet;
  der Laden ist dort also die einzige Quelle für Fahrzeuge überhaupt. Bis
  Mission 32 dreht es sich um (51 frei, 1 gesperrt) und er wird zur
  Bequemlichkeit.
  **Fußsoldaten verkauft er nie** — der Filter des Originals lässt nur Fahrwerke
  ab 160 durch, und das sind genau die Fahrzeuge. Gegenprobe an den Daten: die
  Ware, die auf den dreizehn Gefechtskarten wirklich liegt, erfüllt diesen
  Filter **225 von 225 Mal**.
  ⚠ Neu und vorher unbekannt: **die Ware hat Kampferfahrung, und die macht den
  Preis.** Das Spiel würfelt sie aus — meist keine, in einem von sechs Fällen
  Stufe 1, ganz selten Stufe 6 —, und weil der Wert einer Einheit mit der Stufe
  von 0,10 auf 7,00 steigt, kostet derselbe Entwurf als Veteran das
  **Siebzigfache**. Ein Nutzfahrzeug (Radar, Reparatur, Minenleger) bekommt nie
  Erfahrung; nur Kampfeinheiten. Dass diese Zahlen wirklich die Erfahrung sind,
  steht in ihnen selbst: alle sieben liegen genau **eins über** einer
  Stufengrenze.
  ⚠ Ein Fehler des Originals, nachgebaut: die höchste Veteranenstufe kann im
  Laden **nie** erscheinen — ihr Zweig hängt an einer Bedingung, die nicht
  eintreten kann.

- ⭐ **Ein Fahrzeug kann Radarmasten setzen** — und das Original hat sehr wohl
  Baufahrzeuge, anders als wir bisher notiert hatten. In seiner eigenen
  Befehlsliste stehen »Depot bauen«, »Mine bauen«, »Generator bauen« und
  »Radar setzen«; was es nicht gibt, ist das Wiederaufrichten einer Ruine.
  Der **Radar Installer** trägt den »Radarstab Ausleger« und hat **zwanzig
  Masten** an Bord. Ein gesetzter Mast öffnet den Nebel im Umkreis von zehn
  Feldern — auch für Verbündete — und bleibt liegen, wenn das Fahrzeug
  weiterfährt.
  ⚠ Ein Mast ist **kein Gebäude**: keine Trefferpunkte, kein Grundriss, er hat
  im Original eine eigene Liste mit 200 Plätzen. Ist sie voll, geht nichts mehr.
  Gemessen: eine stockdunkle Stelle (0 von 625 Feldern sichtbar), Mast gesetzt,
  Fahrzeug weggeschickt — **333 Felder bleiben offen**, gehalten allein vom
  Mast.
  Die anderen drei Bauaufträge brauchen einen Platzierungsmodus — siehe die
  nächste Zeile.

- 🐞 **Das Spiel schloss sich kurz nach dem Hauptmenü — behoben.** Nach rund
  acht Sekunden beendete es sich ohne Meldung. Die Ursache lag nicht im Spiel,
  sondern im **nebenläufigen Müllsammler** von .NET: er geriet mit Godots
  interner Buchhaltung ins Gehege. Unsere eigenen Prüfläufe hatten den
  Sammler seit jeher abgeschaltet — im ausgelieferten Spiel war er an, und
  deshalb war jeder Prüflauf grün, während das Spiel abstürzte. Jetzt gilt die
  Einstellung für **jeden** Bau.
  Gemessen: vorher 3 von 3 Abstürzen nach acht Sekunden, danach 3 von 3
  Läufen ohne Zwischenfall.
  ⚠ Und das ausgelieferte Spiel **schreibt jetzt ein Protokoll**
  (`%APPDATA%\Godot\app_userdata\AkteEuropaReborn\logs`). Bisher hinterliess
  ein Absturz dort keine Spur.

- ⭐ **Mission 21 hat ihre Siegbedingung — damit tragen ALLE 33 Missionen der
  Kampagne ihre eigene.** Sie verlangt neun bestimmte **Bahnverbindungen**:
  jede muss unversehrt sein und beide Endbahnhöfe müssen Ihnen gehören. Der
  Regelleser konnte das nicht lesen, weil das Original es als Schleife
  schreibt; jetzt erkennt er diese Form.
  Gemessen: keine der neun Verbindungen gehört Ihnen zu Beginn — übernimmt man
  die neun Endgebäude, ist die Mission binnen einer Sekunde **erfüllt**.

- ⭐ **Mission 28 hat endlich ihre Siegbedingung** — damit trugen **32 von 33**
  Missionen ihr Skript. Ihre Bedingung ist ein
  Oder aus drei Und-Gruppen: drei Wissenschaftler, und **einer** von ihnen muss
  lebend an der Ausstiegsstelle stehen. Der Leser konnte so etwas bisher nicht
  ausdrücken und liess die Mission darum ganz weg — sie endete nie von selbst.
  ⚠ Nebenher aufgefallen und behoben: das Werkzeug, das die Aufbauregeln
  einträgt, hätte in drei anderen Missionen je einen von Hand nachgetragenen
  Wächter überschrieben. Ein Zähler wäre dort von Spielbeginn an gelaufen
  statt erst nach seinem Auslöser.

- ⭐ **Der farbige Ring unter jeder Einheit ist weg.** Er war nie Teil des
  Originals, sondern eine Hilfe aus der Frühzeit dieses Remakes: damals gab es
  noch keine Einheitenbilder, und ein farbiger Punkt mit Ring war das Einzige,
  woran man sah, wo eine Einheit steht und wem sie gehört. Heute steht ihr Bild
  darüber. In den Einstellungen lässt er sich wieder einschalten — dort steht
  auch dabei, dass es unsere Zutat ist.
  ⚠ Damit erledigt sich der alte Bericht über »orange Ringe ohne Körper«: das
  war kein Zeichenfehler. Nachgemessen auf vier Karten über je 3000–4000 Takte
  mit 13 bis 30 gefallenen Einheiten — **kein einziger** Ring ohne Körper; die
  sichtbaren gehörten immer zu lebenden Einheiten.
  Eine Einheit, für die es kein Bild gibt, bekommt weiterhin ihren Punkt —
  sonst wäre sie unsichtbar statt auffällig.

- ⭐ **»Abbrechen« und »Alle starten« sind jetzt Knöpfe.** Einen laufenden Bau
  abzubrechen lag nur auf Umschalt+B, die Flugzeuge eines Flughafens
  loszuschicken nur auf Y — die Bestandszeile sagte dem Spieler sogar »(Y
  startet)«. Beim Messen kam heraus, dass der Hangar dabei **nie leer wurde**:
  die Flugzeuge flogen, standen aber weiter in der Liste des Flughafens. Auch
  das ist behoben.

- ⭐ **Fabriken haben endlich ein Fenster — und zwei Knöpfe darin.**
  **Lagerausbau** und **Produktionserweiterung** gab es seit langem, sie lagen
  aber nur auf den Tasten V und C und waren damit praktisch nicht vorhanden.
  Beim Einbau der Knöpfe kam heraus, dass das Fabrikfenster **überhaupt nie
  aufging**: eine Fabrik zählte nicht als bauendes Gebäude. Jetzt öffnet es
  sich, zeigt ihr Lager mit **Platz und Tempo** und sagt, dass eine Fabrik
  Teile herstellt und keine Entwürfe. Der Preis steht im Knopf, denn er wächst
  mit jedem Ausbau um die Hälfte.
  Gemessen über den Knopfweg: Konto −$20, Platz **90 → 100**, Preis
  **$20 → $30** — und der Preis der *anderen* Ausbaustufe bleibt bei $50, denn
  es sind zwei getrennte Felder.
  ⚠ Nebenher beantwortet: eine Produktionserweiterung hebt **nicht** die
  Nennleistung der Anlage, sondern ihr Tempo. Der Strombedarf bleibt gleich.

- ⭐ **Depot, Mine und Generator lassen sich bauen.** Damit sind alle vier
  Bauaufträge des Originals da. Ein **Gebäude-Techniker** kann »Depot bauen«
  und »Mine bauen«, ein **Generatorenbauer** »Generator bauen«. Der Knopf
  schaltet den Zeiger um, der Klick wählt die Stelle — und dann **fährt das
  Fahrzeug erst hin**. Gebaut wird bei der Ankunft, nicht beim Klick: wird das
  Fahrzeug unterwegs abgedrängt, verfällt der Auftrag, genau wie im Original.
  Rechtsklick oder Esc bricht ab, und die Vorschau zeigt schon beim Zielen, ob
  die Stelle trägt.
  ⚠ **Das Fahrzeug ist der Preis.** Auf dem ganzen Weg wird kein einziger
  Rohstoff abgebucht — statt dessen geht das Fahrzeug im fertigen Gebäude auf.
  ⚠ Eine Mine hängt nicht an einer Zelle, sondern an einem **Vorkommen**: der
  Klick wählt das Vorkommen aus, und sie entsteht um ein Feld nach links und
  **zwei** nach oben versetzt — Depot und Generator nur um je eines. Diese
  Ungleichheit ist die des Originals und nicht geglättet.
  Gemessen für alle drei: nach dem Befehl steht noch **nichts**, die Einheit
  fährt; bei der Ankunft steht das Gebäude auf dem Feld, das aus der
  Originalfassung gelesen ist, und das Fahrzeug ist weg. Die gebaute Mine
  bringt **5000 Terranium** mit — vorher waren es −1, also nichts, und das hat
  erst der Prüflauf ans Licht gebracht.

- ⭐ **Der Strom ist angeschlossen — und er bremst.** Kraftwerke liefern 90 und
  gehören niemandem, Generatoren 50 und gehören ihrem Spieler; Fabriken und
  Minen verbrauchen. Reicht es nicht, laufen alle Anlagen **anteilig
  langsamer** — sie stehen nicht still, sie schaffen es nur seltener. Und
  beides zusammen zählt: die eigenen Generatoren und die herrenlosen
  Kraftwerke der Karte.
  Die zwei kleinen Balken rechts neben dem orangen Blitz im Bedienblock sind
  jetzt gefüllt: oben die erbrachte Leistung, unten der Bedarf. Der Platz war
  von Anfang an da und leer — es sind die Strombalken des Originals, an seiner
  Stelle und in seinen Massen.
  Gemessen: 224 Teile bei voller Versorgung, 136 bei 55 % — vorhergesagt waren
  123, und der Unterschied liegt innerhalb dessen, was der Zufall hergibt.
  ⚠ Nebenbei bekam die **Mine** ihre Förderchance, die ihr bisher ganz fehlte;
  ohne sie hätte der Strommangel dort gar nichts bewirkt.

- ⭐ **Schiffe laufen aus dem Dock aus.** Gemeldet war »sie spawnen direkt im
  Seedock, anstatt daneben« — und die Antwort ist die umgekehrte: das Original
  setzt das Schiff **wirklich ins Dock** und holt es danach heraus. Wir taten
  weder das eine noch das andere: das fertige Schiff sprang aus dem Nichts
  neben das Dock, und war dort kein Platz, entstand **gar nichts** — der Bau
  hing dann unsichtbar fest, ohne dass irgendwo etwas stand.
  Jetzt liegt das frische Schiff sichtbar im Dock und legt ab, sobald eine der
  beiden Ausfahrten frei ist. Ist keine frei, **bleibt es liegen und sagt
  warum** — auf `map_DM_4` sind die Ausfahrten nämlich genau die Liegeplätze,
  und wer dort schon zwei Schiffe hat, bekommt das dritte nicht ins Wasser.
  Gemessen: gebaut → im Dock, dreimal gewartet, dann ausgelaufen — und zwar
  genau auf dem Takt, den das Original dafür vorsieht.
  ⚠ Dabei ist ein erfundener Ausweichplatz entfallen: unser Code suchte hinter
  den zwei gelesenen Ausfahrten noch eine dritte, beliebige Stelle. Die war ein
  Notbehelf gegen unser eigenes Verhalten und hat jetzt keinen Grund mehr.

- ⭐ **Gekaufte Ware wird geliefert.** Bisher stand sie im selben Augenblick
  neben dem Markt. Im Original fährt sie: der Käufer bezahlt sofort, der Platz
  im Laden gilt als verkauft, und alle paar Sekunden macht sich ein Transport
  auf den Weg zu **einem Gebäude des Käufers** — mit allem, was zum selben Ziel
  geht, bis zu zwanzig Stück auf einmal. Erst dort steht die Einheit.
  In der Kampagne ist das jetzt so; im Gefecht bleibt es sofort, aus demselben
  Grund wie beim Verkauf.
  ⚠ **Wohin geliefert wird, ist unsere Wahl** — wir nehmen das eigene Gebäude,
  das dem Markt am nächsten liegt. Das Original lässt den Käufer es aussuchen;
  wie es diese Auswahl anbietet, ist nicht gelesen, und dafür einen Dialog zu
  erfinden wäre eine Erfindung an sichtbarer Stelle. Wer gar kein Gebäude hat,
  bekommt den Kauf abgelehnt, statt Ware ins Nichts zu schicken.
  Gemessen: Markt auf Spalte 80, Ziel auf Spalte 15 — und die gekaufte Einheit
  steht am Ziel, nicht am Laden. Und in der Kampagne entsteht im Takt des Kaufs
  **nichts**; das ist die Probe, die zeigt, dass wirklich gefahren wird.

- **Die Preise der Karten stehen in zwei Gruppen, und das ist ein Befund über
  die Dateien.** Beim Nachrechnen der gespeicherten Ladenpreise fiel ein exakter
  Faktor 5/3 auf. Über alle dreizehn Karten aufgeschlüsselt gibt es genau zwei
  Gruppen und keine dritte: `map_DM_1` ist mit **2,5 × Wert** bepreist — das ist
  die Formel, die im Spielcode steht — und die zwölf anderen mit 1,5 × Wert.
  `map_DM_1` bestätigt damit die ganze Rechnung auf einen Schlag: Kosten,
  Hülle, Erfahrung, Stufentafel und beide Multiplikatoren, **18 von 18**.
  Geradegerückt wird nichts: was in der Karte steht, bleibt; was der Laden
  nachlegt, folgt dem Code.

- **Behoben: die Computerspieler stürzten in jedem Gefecht ab**, sobald einer
  das erste Mal etwas fertigbaute — die Routine, die ihre Depots leert, lief
  über eine Liste, die sie beim Aussenden selbst verlängert. Danach brach der
  ganze KI-Takt ab, Bild für Bild. Jetzt schickt ein Computerspieler seine
  fertigen Einheiten wirklich los (gemessen: 8 in 25 Sekunden statt eines
  Abbruchs beim ersten).

- **Die gewählte Einheit hat jetzt eine Befehlsleiste** — Verkaufen (mit dem
  Preis im Knopf), Ein-/Ausgraben, Anhalten. Anlass ist eine Lehre aus vier
  eigenen Fehlern: Forschung, Reparatur, Depot und Hangar waren alle vier
  gebaut, lagen auf den Tasten O, K und Y — und wurden alle vier als »fehlt«
  gemeldet, weil die Oberfläche schwieg. Eine Mechanik, die nur auf einer Taste
  liegt, ist für den Spieler nicht vorhanden. Das Original hat für seine
  Einheitenbefehle ebenfalls eine Knopfleiste.

- **Die Kartenvorschau sagt jetzt, wieviele Basen es gibt.** Anlass war »die
  Gegner-KI macht mal was, mal nicht — manche bauen gar nicht erst los«. Die
  Ursache ist keine der KI, sondern der Karte: gebaut wird nur in einer BASIS,
  und viele Karten haben weniger davon als Startplätze — `map_DM_4` **2 Basen
  für 5 Plätze**, `map_DM_11` 2 für 6, `map_NET07` **keine** für 8. Wer keine
  bekommt, sieht zu. Das stand nirgends; jetzt steht es unter der Vorschau,
  mit Warnzeichen wenn es nicht reicht.
  ⚠ Zwei eigene Diagnosen sind dabei gefallen: die KI baut sehr wohl ohne
  Bauprogramm (10 Einheiten in 60 s gemessen), und sie greift auch an (6 in der
  Welle, 1 Angriff). Beides hatte ich vorher falsch aus den Zählern gelesen.

- ⭐ **Feindliche Gebäude lassen sich einnehmen — Strg+Rechtsklick.** Gemeldet
  war »Werft und Seedock lassen sich nicht einnehmen, nur Angreifen kann man
  sie« und »von KI eingenommene Gebäude kann man nicht einnehmen, nur
  zerstören«. Der neue Prüfstand `--door-check` hat zuerst gezeigt, dass es
  NICHT daran liegt: die Türen sind erreichbar (Werft-Station 1/1 bzw. 2/2,
  Basis, Fabriken, Flughafen, Mine, Bahnhöfe vollständig). Die Ursache war der
  Klickweg — er versucht zuerst einen Angriff, und ein feindliches Gebäude *ist*
  ein Ziel, also kam der Bewegungsbefehl nie dran und die Einheit konnte die
  Türzelle gar nicht erreichen. Bei neutralen Gebäuden greift niemand an, deshalb
  ging es dort und nur dort.
  Gemessen: eine feindliche Basis wechselt jetzt den Besitzer, **unbeschädigt**
  (1200/1200). Gegenprobe `--capture-by-attack`: sie bleibt fremd und steht auf
  **0/1200** — genau das gemeldete »nur zerstören«.
  ⚠ Seedock (0 Türen in 39 von 39) und Kraftwerk (0 in 262) bleiben
  uneinnehmbar; das ist das Original, und der Knopf sagt es jetzt statt zu
  schweigen. Der Hafen wechselt mit seiner Werft-Station.
- **Eine neue Einheit kommt aus der Tür.** War seit langem gelesen
  (`@0x410441`: die erzeugte Einheit bekommt `col + door_col` / `row +
  door_row`) und nie gebaut — sie erschien an der Ankerzelle des Gebäudes, also
  je nach Grundriss an der falschen Seite. Gemessen: 4 von 4 aus der Tür.
- **Die Bauwarteschlange.** Mehrfach dieselbe Einheit zu bestellen hat bisher
  jedesmal bezahlt und den laufenden Bau nur neu angestossen — drei Klicks,
  dreimal Teile weg, eine Einheit. Jetzt reihen sich Bestellungen auf: bezahlt
  wird beim Einreihen, hoechstens sechs warten (die Zahl des Depots im Original,
  `cmp al,6` @0x467FBF), **Umschalt+B** nimmt die letzte zurueck und erstattet
  sie, und die Rohstoffleiste zeigt, was laeuft und was dahintersteht.
  Gemessen mit `--queue-check=4`: 4 bestellt, 80/160/0 bezahlt (genau 4x der
  Preis), 4 angekommen, Schlange leer. Die Gegenprobe `--no-build-queue` stellt
  den alten Stand her und meldet dort 80/160/0 bezahlt gegen ein Soll von
  20/40/0 — der gemeldete Verlust, hergestellt und wieder wegmessbar.
  ⚠ Eine Warteschlange ist **unsere Zutat**; das Original fuehrt fuer eine
  Einheit gar keine Bauzeit, sondern ein Depot mit sechs Plaetzen.
- **Der Techstandard steht in der Vorgabe auf 8 statt auf 1.** Auf Stufe 1 gibt
  der Flughafen nur die zwei Versorgungshelis frei — gelesen ist daran, dass ein
  frisches Original auf 1 startet (@0x4426F4), nicht, dass 1 eine gute
  Wettkampfvorgabe ist. Unter dem Regler steht jetzt eine Zeile, die **ausrechnet**
  (nicht aufschreibt), was die gewaehlte Stufe freigibt und was die naechste
  dazubringt. Eine bestehende Installation traegt die alte 1 in ihrer
  `settings.cfg`; sie wird **einmalig** angehoben und danach nie wieder
  angefasst.
- **Forschung und Reparatur sind erreichbar.** Beide Mechaniken gab es seit
  langem auf den Tasten O und K, aber die Reiter des Basisfensters sagten
  »noch nicht angeschlossen«, und damit war die Frage »wo kann ich forschen?«
  unbeantwortbar. Die Reiter zeigen jetzt Stand, Kosten und naechstes Vorhaben,
  und der Knopf heisst, was er tut. ⚠ Fuers Reparieren braucht es **keine
  Einheit**: das Gebaeude repariert sich selbst.
- **Der Flughafen rechnet in Teilen, nicht in Dollar.** Bezahlt hat er die ganze
  Zeit richtig (Teilelager des Gebaeudes, wie `build_in_airport` @0x4BB3D0), nur
  seine Kopfzeile und die Zeile im Bedienblock schrieben `$150` hin — der Preis
  des Versorgungsdepots, das tatsaechlich Geld nimmt. Woher die Teile kommen,
  ist jetzt auch beantwortet: **ueber die Bahn** (Typmatrix @0x504128), der
  Nahweg beliefert allein die Basis.
- **Versorgungshelis bleiben nicht mehr stehen.** Ein leerer Heli suchte
  ausschliesslich einen Nachschub-Posten (Typ 14) — und **map_NET02 hat sieben
  Flughaefen und keinen einzigen Posten**, NET08 drei und keinen, DM_11 gar
  nichts. Dort war ein Heli nach fuenf Lieferungen fuer immer erledigt. Findet
  er keinen Posten, laedt er jetzt am Flughafen oder der Basis seines eigenen
  Spielers nach und sagt in der Ausgabe, dass er es getan hat. ⚠ Eine bewusste
  Abweichung; wo es einen Posten gibt, gewinnt weiter der Posten (auf NET04
  gemessen: 1 am Posten, 0 Abweichungen).
- **Die Rohstoffleiste sagt, dass sie eine Summe ist.** Sie zeigt »gesamt (n)«
  mit der Zahl der gezaehlten Gebaeude — dass sie mit dem Lager einer einzelnen
  Basis nicht uebereinstimmt, war vorher nicht zu erkennen. Ausserdem zaehlen
  jetzt **Flughafen und Werft-Station** mit: aus beiden wird bezahlt, und genau
  das ist die Regel, nach der die Auswahl schon vorher gebildet war.
- **Der eigene Startplatz steht auf der Minimap.** Eine weisse Raute mit dunklem
  Rand, dort, wo die Partie begonnen hat. Sie wandert nicht mit: genommen wird
  der Punkt einmal beim Start und dann nie wieder: wer seine Basis verliert,
  soll trotzdem noch sehen, wo er hergekommen ist. Unsere Zutat, wie die
  Minimap selbst.
- **Jeder Mitspieler faengt mit einer Basis an, auch der Rechner.** Auf den
  Eroberungskarten stehen 4 bis 8 Basen neutral herum, und wer zuerst eine
  erreicht, hat sie: das ist ein Wettlauf und kein Gefecht. Jetzt bekommt jeder
  Mitspieler beim Start die ihm naechste zugeteilt, die uebrigen Gebaeude
  bleiben zu besetzen. Die Zuteilung rechnet **ganzzahlig in Zellen** und in
  fester Reihenfolge, weil sie im Lockstep-Pfad liegt und auf zwei Maschinen
  dasselbe ergeben muss. Auf `map_NET04` gemessen: 4 von 4 Mitspielern bekommen
  eine, die neutralen Gebaeude gehen von 61 auf 57 und die neutralen Basen von 8
  auf 4 zurueck; `--no-start-base` stellt den alten Stand her.
  Eine bewusste Abweichung vom Original, die Kampagne bleibt unberuehrt.

### Kampagne und Oberfläche

- ⭐ **Die Enzyklopädie des Originals ist im Spiel.** Der Menüpunkt sollte auf
  unser Wiki verlinken. Beim Nachsehen, was das *Original* hinter der Zeile hat,
  lag **`ENCYCLOG.TXT` mit 106 Seiten** neben GAME.EXE — Fahrwerke, Waffen,
  Zubehör, Verbesserungen, Luftwaffe, Marine, Dicke Bertha, Infanterie,
  Gebäude, im Volltext und mit **149 Querverweisen**, die jetzt anklickbar sind.
  ⚠ **Die Kodierung ist eine Falle:** `HELPG.TXT` daneben ist cp437,
  `ENCYCLOG.TXT` ist **Latin-1**. Mit dem falschen Leser wird aus »Räder«
  »RΣder«. Die Datei entscheidet, nicht der Ordner.
  ⚠ Ohne Bild: die Seiten tragen eine Bildnummer bis 97, `ENCYCLOG.PIC` fasst
  aber nur 24 Bilder — die Zuordnung ist ungelesen und wird nicht geraten.
- **Credits.** Die Zeile des Originals führt jetzt irgendwohin. Sie zeigt, was
  belegt ist (Virtual X-citement, Eidos Interactive, 1997) und die
  Reborn-Seite — und sagt dazu, was **nicht** belegt ist: die Namen des Teams
  von 1997 stehen in keiner Datei, die diese Fassung liest. Der Abspann des
  Originals ist vermutlich `34.RPL` — der einzige Film ausserhalb der 33
  Missionsfilme, und der einzige, der auf **beiden** CDs liegt. Ein Indiz, kein
  Beleg; wir spielen kein .RPL.

- **»Spiel laden« sitzt wieder in der Mitte.** Der Schirm hat seine Anker
  gesetzt, aber nicht seine Raender — damit behielt er ein Rechteck der Groesse
  null in der linken oberen Ecke, und das Fenster darin wurde von dort aus
  gezeichnet. Genau derselbe Fehler war im Einstellungsschirm schon einmal
  gefunden und dort im Kommentar festgehalten worden; er ist wiedergekommen,
  weil die Kur im Fliesstext stand und nicht am Aufruf. Dieselbe Zeile hat
  nebenbei drei abdunkelnde Flaechen repariert, die nichts abgedunkelt haben,
  und den Deckel des Abschlussfensters, der keine Maus abgefangen hat.


- **Das Editorfeld stand im Gefecht, und die Missions-Popups standen im
  Hauptmenü.** Zwei Meldungen, eine Ursache. Ein Szenenwechsel ersetzt nur die
  laufende Szene; wer als Geschwister davon unter der Wurzel hängt, überlebt ihn
  — und genau dort hängen zwei Helfer mit Absicht: der Wächter des
  Karteneditors, damit er sich an die *nächste* Karte hängen kann, und die
  Ebene der Hilfefenster, damit die Kamera sie nicht aus dem Bild trägt. Beide
  hatten einen Einschalter und keinen Ausschalter. Der Bearbeitungsmodus wurde
  nie zurückgenommen — die Methode dafür stand da und wurde im ganzen Programm
  **kein einziges Mal** gerufen —, und die Fenster räumte nur das *Laden* einer
  Karte weg, ein Weg, den das Hauptmenü nie nimmt. Die Kur sitzt jetzt am
  **Eingang** des Menüs statt an den neun Ausgängen: wer dort steht, hat die
  Spielwelt verlassen, egal durch welche Tür. Dazu ein Prüfstand, der den
  echten Ausstieg geht (`--leave-check`) — und eine Gegenprobe, die die alte
  Fassung im selben Programm nachstellt (`--leave-check=alt`) und dabei
  durchfallen **muss**, sonst wäre nicht zu sehen, ob der Zähler überhaupt
  etwas sehen kann.
- **Die Trefferrechnung nahm die Höhe des Schützen nicht mit.** Gemeldet als
  „Team 2 nimmt kaum Schaden" — und zu Recht: das Spiel von 1997 rechnet die
  Höhe auf **beiden** Seiten ein, bei uns fehlte die eine Hälfte, und zwei
  Felder waren obendrein vertauscht. Ausgelöst hat das ein falscher Kommentar.
  Behoben; der Unterschied, der bleibt, ist die Höhenregel des Originals.
- **Das Kampagnen-HUD zeigt den Auftrag.** Bisher standen dort nur die
  Nebenmissionen — das Hauptziel ist die Siegbedingung, und die hat gar keinen
  Text. Den Text hat das Original selbst, in `OBJECTG.TXT`; sie liegt auf CD 1
  im selben Archiv, aus dem schon die Briefings und die Hilfetexte kommen. 33
  Missionen, 58 Ziele, im Wortlaut von 1997.
- **Die technischen Zeilen und die Tastenlegende verlassen das HUD im Spiel.**
  Kartenname, Rasterweite, Kachelsatz, Bildgröße und die zwei Zeilen mit den
  Tastenkürzeln sind Angaben über die Datei und über die Bedienung, nicht über
  das Schlachtfeld. Im Kartenbetrachter bleiben sie stehen, und `--hud-debug`
  holt sie überall zurück.
- **Versorgungshelikopter** setzen ihre Blickrichtung jetzt auch auf dem letzten
  Schritt vor dem Ziel. Wohin einer *ohne* Auftrag sieht, ist im Original
  nachgelesen: er behält die Richtung seines letzten Fluges — die Luftschleife
  fasst die Blickrichtung überhaupt nicht an.

### Karteneditor

- **Gelände und Höhe lassen sich malen.** Zwei neue Pinsel: der eine setzt die
  Geländeart einer Zelle (frei, rau, Wasser, gesperrt), der andere hebt und
  senkt sie. Ein Strich ändert dabei nie nur die angeklickte Zelle — der
  Kachelschlüssel hängt an der Neigung, an den vier Nachbarn und am Abstand zum
  Wasser, also werden bis zu **81 Zellen** nachgezogen. Sie ziehen ihre Kachel
  aus derselben Rechnung wie der Kartengenerator, samt demselben Wurf; eine nur
  mitgezogene Zelle bekommt darum genau ihre alte Kachel zurück.
  ⚠ **Der Pinsel weigert sich, statt zu reparieren.** Der Generator löst
  Höhenkonflikte, indem er *andere* Zellen absenkt. Ein Pinsel darf das nicht —
  er täte dann etwas anderes als angeklickt wurde. Er lehnt ab und sagt, warum.
- **Der Prüfstand hat den ersten Pinsel zweimal verworfen.** Beide Male hatte er
  die Karte messbar verschlechtert: harte Nähte von 3,4 auf 6,9 Prozent, eine
  Uferzelle mit Innenland-Kachel, eine Beanstandung in der Kartenprüfung. Der
  Grund war beide Male derselbe — er zog die Kachel anders als der Generator.
  `--map-edit-check` stellt jetzt neun Zahlen **vor und nach** dem Malen
  nebeneinander; ohne sie hätte man den Fehler nur daran gesehen, dass eine
  gemalte Stelle „irgendwie anders aussieht".
- **Die Spieler 2 bis 5 waren nie anwählbar.** In der Leiste stand „0..7 =
  Eigner", aber die Ziffern 1 bis 4 wurden schon vorher als Pinselwahl
  abgefangen — beim Einheitenpinsel ließ sich damit gar kein anderer Eigner
  einstellen als der erste. Der Eigner geht jetzt reihum.
- **Die „zerstückelten Gebäude" waren gar keine Gebäude.** Basis und Fabrik einer
  erzeugten Karte sind Bild für Bild dieselben wie auf einer gelieferten. Was
  zerstückelt aussah, waren **einzelne Gebäudekacheln, die als Bewuchs über das
  Gelände gestreut** waren — samt der schwarzen Innenkacheln, die nur im Verbund
  einen Sinn ergeben. Sie kamen dorthin, weil das Spiel von 1997 die Kacheln
  eines Gebäudes ins Kartenraster schreibt und die gemessene Kacheltabelle sie
  darum für Bewuchs hielt. In den gelieferten Karten liegt jede Gebäudekachel im
  Rahmen eines Gebäudes — 2094 von 2094 auf der einen, 160 von 160 auf der
  anderen, und keine einzige steht allein. Die Kartenprüfung zählt beides jetzt
  mit.
- **Einheiten von Hand setzen.** Sechzehn Bauarten, vom Reifen bis zum
  Schlachtschiff, jede mit den Werten, die sie auf den gelieferten Karten
  wirklich trägt — Leben, Tank, Angriff und Gattung aus den Rohsätzen gelesen,
  nicht gewählt. Der Spieler wird mitgewählt, und der Editor weist ab, was nicht
  stehen könnte: ein Schiff auf der Wiese ist eine Einheit, die sich nie bewegt.
- **Der Editor hat einen Pinsel.** Auf der Karte lassen sich jetzt **Gebäude**
  (jede Art, die der Kachelsatz kennt — militärisch wie zivil, mit wählbarem
  Eigner bis hin zu herrenlos und Kulisse), **Gegenstände** und **Bahngleise**
  von Hand setzen. Ein Gebäude steht sofort da; Gegenstände und Gleise kommen
  beim Speichern ins Bild, und bis dahin zeigt der Schirm eine Vorschau und sagt
  dazu, dass es eine ist. Ein Gebäude kommt nur auf einen echten Bauplatz — und
  wo nicht, nennt der Schirm die Zelle und den Grund. Das Bild eines Gleisstücks
  wird nicht gewählt, sondern aus seinen Nachbarn bestimmt, nach der Tafel, die
  das Spiel von 1997 selbst führt.
- **Eine erzeugte Karte ist jetzt eine Eroberungskarte.** Sie bekommt neutrale
  Gebäude wie die gelieferten Gefechtskarten: Zahl, Arten, Türen, Trefferpunkte
  und Abstände sind aus sieben gelieferten Karten gemessen; verteilt und gesetzt
  wird von uns. Aus vier Gebäuden werden auf einer großen Karte über siebzig,
  darunter Flughäfen, Fabriken und Basen zum Einnehmen.

### Klang

- **Ein Klang kommt jetzt von links oder rechts.** Die Dämpfung nach Entfernung
  gab es schon, das Panorama war gelesen und ausdrücklich als Lücke
  stehengelassen. Das Spiel von 1997 rechnet `panorama = 200 · dx` und klammert
  auf DirectSounds eigene Grenzen — ausgereizt ist der Regler damit bei **50
  Zellen** seitlichem Abstand. Gebaut wie es gehen muss: ein eigener Klangbus je
  Kanal mit Schwenkregler, zwölf Stück. Teilten sie sich einen, bekäme ein
  Schuss am linken Kartenrand den Schwenk des nächsten Schusses.
  ⚠ **Nur `dx`.** Ein Klang genau über oder unter dem Ohr kommt aus der Mitte,
  so weit weg er auch sei — das Original fragt `dy` für das Panorama gar nicht
  ab. Eine Winkelrechnung wäre „richtiger" und damit falsch.
  Auf einer großen Karte gemessen: 255 Objekte, Werte von −1,00 bis +1,00, 93
  ganz links, 35 ganz rechts.

### Zug und Strecke

- ⭐ **Die Waggons zeigen dorthin, wo das Gleis hinführt.** Gemeldet mit zwei
  Bildern (»wie doof der Zug oft aussieht«): auf einer schrägen Strecke standen
  zwei gekuppelte Waggons in **entgegengesetzte** Richtungen und drei in drei
  Richtungen, während das Gleis darunter glatt diagonal durchlief.
  Die Ursache war ein einziger Ausdruck, der nur **vier** der acht Richtungen
  kannte und bei einer Diagonale die x-Komponente ersatzlos verwarf. Seine
  Begründung stand daneben und war der Denkfehler: die ungeraden Stücke seien
  »Halbschritte einer Diagonale, die es auf einer Zellenkette nicht gibt«. Die
  Diagonale gibt es sehr wohl — sie ist als **Treppe** ausgelegt, (±1,0) gefolgt
  von (0,±1).
  Die Zählweise steht im Original, Bildtabelle @`0x539400`: 0 = S, 1 = SW,
  2 = W, 3 = NW, 4 = N, 5 = NO, 6 = O, 7 = SO, Gegenrichtung `+4`, und die
  Diagonalen sind **halbe** Zellschritte (±20,±10 gegen 40 bzw. 20). Die
  Richtung kommt jetzt aus der Zentraldifferenz über die Nachbarzellen.
  Nach Ursache aufgeschlüsselt war der Befund eindeutig — auf `map_NET02` waren
  von den **geraden** Zellen **0 von 738** falsch, von den **Treppen** aber
  **389 von 389**; auf `map_DM_4` 0 von 469 gegen **354 von 354**.
  ⚠ Der Prüfstand `--wagon-facing-check` kann die Kur nicht bestätigen (er zieht
  seinen Sollwert aus derselben Rechnung); dass sie wirkt, zeigen ein Bildpaar
  am selben Waggon zur selben Spielzeit und die **Kupplung**, die aus anderer
  Quelle misst: dort kamen vorher nur die vier Stücke f0/f2/f4/f6 überhaupt vor,
  jetzt alle acht, sichtbare Lücken bleiben **0 von 45** bzw. **0 von 51** und
  die Lückenquote sinkt um 3 bzw. 8 %. Gegenprobe `--stueck-alt`.

- **`--rail-gap-check`: wie weit ist das letzte Gleisstück vom Gebäude?** Zu
  »oft fehlt noch ein kleines Stück von der Bahnstrecke«. Die alte Zahl konnte
  es nicht sehen — sie meldet »0 von 70 weiter als **2** Zellen«, und eine
  Lücke von einer Zelle ist 40 px. Gemessen: 20 von 70 (NET05), 20 von 66
  (NET02), 16 von 48 (DM_4) Enden mit Lücke, **immer genau eine Zelle** und
  **nur an Bahnstation und Feldbahnhof**. ⚠ Die Lücke steht in den
  KARTENDATEN — die erste Grundrissspalte trägt dort gar keine Gleiszelle, das
  Gebäudebild setzt die Schiene fort, und senkrecht sitzt es bündig (32/32,
  6/6, 7/7, 0 px). An einer der Fundstellen nachgesehen: kein Loch. **Nicht
  reproduziert** — der Prüfstand nennt jetzt Karte, Linie, Zelle und Gebäude.

- ⭐ **Der Zug fährt die Diagonale, die gezeichnet dasteht.** Zum zweiten Mal
  gemeldet (»wenn eine Bahnstrecke sauber diagonal ist … so fährt aber der Zug
  nicht, der Zug macht Zicke Zacke, wobei die Strecke sauber ist«) — und die
  vorhandene Zahl konnte es gar nicht sehen: `--rail-check` meldet den
  **mittleren** Richtungswechsel je Takt, und ein Zickzack wechselt abwechselnd
  um +δ und −δ, im Mittel bleibt davon nichts übrig.
  Der neue Prüfstand **`--rail-zigzag`** hält deshalb die GEZEICHNETE Schiene
  (Anschlusspunkte aus der an den Bildern gemessenen Tafel) gegen den
  GEFAHRENEN Weg, **je Knick einzeln**. Damit war es sofort da, und die Quote
  nach Ursache aufgeschlüsselt war eindeutig: von den Stellen, an denen die
  Schiene gezeichnet gerade ist, knickte der Weg auf NET05 an 7, auf NET02 an
  24, auf DM_4 an 23 — und **jede einzelne davon an einer RAMPE**. Auf ebenem
  Gleis: **0 von rund 2700**.
  Die Ursache steht in der Zahl: die schlimmste Abweichung war **15,3 px**, und
  15 px ist genau eine Höhenstufe. Die Höhe des Weges war eine **Treppenfunktion
  je Zelle** (`ElevOf(round(x),round(y))·15`), während die gezeichnete Rampe
  **stetig** steigt — an jeder Zellgrenze einer Rampe sprang der Waggon eine
  volle Stufe. Jetzt kommt die Höhe aus der **Kunst**: die Tafel der
  Anschlusspunkte sagt je Bild und Seite, wie hoch die Schiene dort liegt
  (f6/f7 14,7 px, f8/f9 15 px über dem ebenen Wert).
  Gemessen danach: geknickt **0 / 4 / 12** statt 7 / 24 / 23, schlimmster Knick
  **7,3°** statt 27,5°, Formabweichung auf Rampen **0,2–0,3 px** statt
  7,4–9,3 px. Gegenprobe **`--no-rail-lift`** stellt den alten Stand her.
  ⚠ Ohne Rückschritt: Ecken 0 von 949, Anschlusszeile 45 von 45 bündig,
  Waggonlücken 0 von 48 — alle unverändert.

- **Die Waggon-Feinlage ist gelesen.** Zwei Felder im Waggonsatz standen seit
  Beginn ohne Namen da. Sie sind der Versatz des Waggons **innerhalb** seiner
  Zelle, in Bildpunkten, und das Spiel setzt sie aus einer einzigen Größe: der
  Parität der Halbzeile, in der der Waggon steht. Ungerade heißt eine halbe
  Kachel nach unten, gerade eine halbe nach rechts — das sind genau die
  **Randmitten**, auf denen auch die Schienenbilder ihre Enden haben. An den
  gelieferten Karten nachgezählt: von 162 Waggons, die auf einem der beiden
  Ausgangswerte stehen, folgen **161** der Regel. Die übrigen tausend sind
  Zwischenstände einer Fahrt, im Takt des Fortschritts.
- **Die Waggons stehen auf den Randmitten — nachgemessen, und zwar gegen die
  Regel des Originals.** Nachgezählt über drei Karten sitzt **kein einziger**
  von 1193, 1000 und 1305 Wegknoten auf der Zellmitte; die Zellmitte ist der
  Mittelpunkt der isometrischen Raute und liegt auf keinem der beiden
  Eckengitter. Bisher hat unsere Strecke die Ecke aus der Nachbarzelle
  abgeleitet und aus den an den Bildern gemessenen Anschlüssen — das Spiel von
  1997 nimmt statt dessen die Parität der Halbzeile. Beide Wege lassen sich
  jetzt gegeneinander halten, und auf den Zellen, über die die Gegenprobe
  überhaupt etwas sagen kann, stimmen sie **1271 zu 0** überein — restlos.
  ⚠ Die erste Fassung dieses Eintrags stand hier mit „88 bis 93 Prozent" und
  schob den Rest auf die bekannte Lücke zwischen Gleis- und Zugstruktur des
  Originals. Das war falsch, und der Fehler lag bei uns: nach Bildart getrennt
  sitzt **jede einzelne** Abweichung auf einem **Eckstück** und keine auf einer
  Geraden oder Rampe (402:0, 473:0, 396:0 gegen 192:88, 155:66, 353:54). Ein
  Eckstück verbindet eine waagerechte mit einer senkrechten Kante, hat also je
  einen Anschluss auf *jedem* der beiden Gitter — eine Tafel, die je Zelle nur
  eine Parität hält, kann dort gar nicht beide treffen. Es war die Buchführung,
  nicht die Strecke.
  ⚠ Die Parität **steuert nichts** und soll es nicht: sie ist die zweite,
  unabhängige Auskunft, und wer sie zur Regel machte, hätte hinterher keine
  Gegenprobe mehr.
- **Der letzte Waggon einer Linie stand neben dem Gleis.** Das freie Ende einer
  Strecke wurde als „die Gegenseite des Ausgangs" gerechnet. Für ein gerades
  Stück stimmt das — für ein **Eckstück** nicht: geht die Schiene nach rechts
  hinaus und unten weiter, ist die Gegenseite *links*, und dort liegt gar keine
  Schiene. Das freie Ende liegt unten. Der Endknoten saß dadurch eine halbe
  Zelle in jeder Achse daneben, rund 22 Bildpunkte. Richtig ist nicht „die
  Gegenseite", sondern „die andere Seite, die dieses Gleisbild wirklich hat".
  Betroffen waren **25, 28 und 30** Knoten auf den drei Karten — und
  nachgemessen sind das **ausnahmslos Linienenden**, kein einziger mitten in
  der Kette. Die Gegenprobe an der Regel des Originals verbessert sich auf allen
  drei Karten (666:94 → 672:88, 667:73 → 674:66, 853:66 → 865:54); die
  bekannten Kennzahlen bleiben Zahl für Zahl gleich. Mit `--rail-lay=altport`
  lässt sich der alte Stand daneben legen — ohne ihn wäre „4,12 px je Takt"
  eine Behauptung; so ist belegt, dass es die 4,11 vorher schon gab.

### Mehrspieler

- **Die Computerspieler müssen nicht durch den Befehlsring** — und das ist
  gemessen, nicht angenommen. Im Programm von 1997 erreicht **genau eines von 21
  Zielen** der Computerspieler-Runde den Befehlsbus (eine Gruppenbewegung);
  Produktion, Einheitendurchlauf und Transport schreiben ihre Felder direkt. Und
  weil die Runde jeden Platz überspringt, auf dem ein Mensch sitzt, rechnet im
  Netzspiel **jede Maschine ihre Computerspieler selbst**. Nachgeprüft wurde, ob
  unsere das auch aushalten: drei Läufe mit zwei echten Prozessen und
  *verschiedenen* Spielerplätzen, darunter einer über 6000 Takte, in dem beide
  Computerspieler ein Gebäude einnehmen und je vier Einheiten bauen — also
  gerade der Zweig, der würfelt. Beide Maschinen kamen an jedem Prüftakt auf
  dieselbe Zahl.
- **Der Netz-Prüfstand meldet jetzt, was die Computerspieler tun.** Er tat es
  vorher nicht, und deshalb war sein grünes Ergebnis wertlos: ein Lauf, in dem
  die Computerspieler nur dastehen, sieht genauso aus wie einer, in dem sie
  spielen. Ihre Zahlen stehen jetzt an jedem Prüftakt und am Ende im Protokoll,
  auf beiden Seiten.

## 0.5.0 — 13.08.2026

Die Fassung, in der die Kampagne anfängt, sich selbst zu entscheiden, und die
Bahn anfängt zu fahren. Erfunden ist daran fast nichts: die Baupläne der
Computerspieler, der Fahrplan, der die Entwürfe freischaltet, die
Siegbedingungen der Missionen, der Takt, in dem die Missionslogik läuft, die
Form jedes einzelnen Gleisstücks — alles aus dem Programm von 1997 gelesen und
gegen **beide** GAME.EXE dieses Rechners gehalten. Was nur eine von beiden
hergibt, ist ein Lesefehler und kein Befund.

Zwei Lehren haben sich bezahlt gemacht, und sie sind der Grund, warum unter
den Einträgen Zahlen stehen. **Ein Prüfstand, der nur in eine Richtung
drücken kann, prüft nur eine Richtung** — der, der Dinge ausschließlich
*zerstören* konnte, hat drei verkehrt herum gelesene Siegbedingungen einen
ganzen Tag lang als bestanden gemeldet. Und **ein Prüfstand, der unsere
Ableitung mit sich selbst vergleicht, prüft gar nichts**: `--rail-check`
meldete eine makellose Strecke, während rund die Hälfte davon falsch lag — er
hielt unsere Konstruktion gegen unsere Konstruktion. Die Antwort stand die
ganze Zeit in der Kartendatei.

### Die Kampagne entscheidet sich selbst

- **33 Missionen statt 15.** Alle Karten waren längst eingespielt — es fehlte
  nur die Liste, die die Engine liest. Der neue Schalter
  `--reexport-campaign` schreibt sie aus den bereits importierten Karten, ohne
  CD und ohne Neubacken.
- **Die Missionen führen ihre eigene Logik**, aus dem Code gelesen statt
  angenähert: **270 Regeln über 31 Missionen** — 119 Textfenster, 61 Klänge,
  59 Geldzahlungen, 19 Zielvorgaben, 8 Verkäufe, 4 Angriffsbefehle. Dreizehn
  Wirkungen und sieben Bedingungen sind in dieser Fassung dazugekommen, darunter
  `text`, `money`, `sound`, `order`, `change_owner`, `set_relation` und
  `stop_transport`. Auf beiden GAME.EXE gelesen: 33 Missionen gleich, 0
  abweichend.
- **Die Missionslogik läuft im Takt des Originals, und der Takt ist gemessen.**
  `SetTimer(fenster, 1, 0x14, NULL)` ergibt **50 Takte je Sekunde**, und die
  Uhr zählt **250 Takte auf eine Spielminute** — eine Spielminute sind also
  fünf reale Sekunden. Wir hatten die Regeln einmal je *Bild* ausgewertet und
  eine Spielminute als reale Minute gerechnet, zwölffach zu langsam. Jeder der
  33 Missionsblöcke ist außerdem **zweiteilig**: ein Tor auf `Takt mod 100`
  trennt, was jeden Takt läuft, von dem, was jeden hundertsten läuft.
- **Zähler haben Wächter.** Drei von fünf hatten ihren bei der Übersetzung
  verloren — deshalb meldete Kampagne 2 schon 1,7 Sekunden nach dem Start eine
  erledigte Nebenmission, ohne dass der Spieler irgendetwas getan hätte. Jetzt
  sind es über die ersten zehn Sekunden 0 Texte, 0 Geldbuchungen, 0 Klänge.
- **Mission 1 ist wieder das Tutorial, das sie im Original ist.** Ihr Block
  öffnet siebzehn Hilfefenster aus HELPG.TXT, schickt vier Angreifer los und
  führt eine **Untermission** — »Versenken Sie die Transportschiffe.
  @Bezahlung — 50$ für jeden versenkten« — mit den drei Zahlungen des
  Originals.
- **Die Missionen lesen die Belegungskarte.** `imap(spalte, zeile)` war der
  fehlende Auslöser: Kampagne 1 beginnt ihren Angriff, sobald eine Einheit auf
  Zelle (39, 4) steht. Damit kamen 28 Regeln in 12 Missionen dazu.
- **Sprungziele sind lesbar, also sind Zähler sichtbar.** Das Original schreibt
  einen Zähler als Durchfall-Gegenstück eines Sprungs; ein Leser, der jeden
  angesprungenen Block verwarf, hat sie nie gesehen — und deshalb sind die vier
  Angreifer in Kampagne 1 nie losgefahren.
- **Drei Siegbedingungen waren verkehrt herum, sechs fehlten.** Zwei der drei
  waren beim Missionsstart wahr — die Mission war in derselben Sekunde
  gewonnen, in der sie begann, und im Prüflauf sah das aus wie ein Erfolg.
  `OBJECTG.TXT`, das alle 33 Missionsziele im Klartext nennt, sagt es
  andersherum.
- **Die Blockvariablen haben Erzeuger — und einen Anfangswert.** Mission 7 will
  einen Zähler auf 2 haben und erhöht ihn genau einmal, weil der Setup ihn bei
  1 anfangen lässt. Ohne diese zweite Hälfte war die Mission ein Rätsel.
- **Die Computerspieler bauen nach den Plänen des Originals** — ein Programm
  von bis zu 50 Zeilen, eine Zeile alle 50 Takte, das das Spiel selbst
  »vyroba« nennt. Es steht in keiner Karte, sondern als gerade Codestrecke in
  GAME.EXE, ausgewählt über die Missionsnummer. 106 Programme auf beiden
  Fassungen gleich.
- **Entwürfe werden nach dem Fahrplan des Originals freigeschaltet.** Die
  Datei, die vorher mitgeliefert wurde, war messbar unvollständig — Entwurf 52
  gibt das Spiel ab Mission 6, sie ab Mission 8 — und stimmte erst nach
  Mission 33 wieder überein, weshalb es nie aufgefallen war.
- **Die Infanterie fehlte in der Bauliste.** Der Entwurfsfilter ließ nur
  Fahrwerke 160..175 durch, und die Infanterie sitzt auf 148 und 149. Weil die
  frühe Kampagne zuerst fast nur Infanterie freischaltet, blieben dem
  Computerspieler genau zwei baubare Entwürfe: ein Transporter und ein
  Chaingun Tank. Genau das war die Meldung »die KI baut nur Transporter«.
- **Auf einer Kampagnenkarte marschiert nichts mehr von selbst.** Das Original
  schickt keine Einheit irgendwohin; es greift an, was ihr in den eigenen
  Sichtring läuft. Wer marschiert, tut es, weil die **Mission** ihm ein Ziel
  gegeben hat (`add_target`, aus der Ziel-Liste der KI gelesen). Angriffswelle
  und Gebäudegreifer sind unsere Zutat und laufen jetzt nur noch im Gefecht.
- **Der Kontostand geht in die nächste Mission mit.** Bisher fing jede
  Kampagnenmission bei $0 an. Im Original wird der Kontostand nirgends
  überschrieben — alle drei Schreibstellen *addieren* —, er läuft also durch.
  Das deckt sich mit dem Bildschirmfoto des Originals: »Missionsbezahlung
  $320«, »Kontostand $470«.
- **Die Missionsbezahlung ist eine feste Zahl je Mission**, 36 Konstanten aus
  GAME.EXE gelesen, die am Missionsende auf den Kontostand kommen. Die frühere
  Lesart — »Bezahlung ist die Summe der Skriptzahlungen« — war als unsere
  markiert und war falsch.
- **Eine Kampagnenübersicht**, 33 Kacheln: abgeschlossene Missionen wählbar,
  die nächste auch, der Rest schattiert.

### Die Bahn

Aus »Linien stehen auf dem Papier« ist eine fahrende Hochbahn geworden, über
etwa ein Dutzend Schritte — die meisten davon angestoßen davon, dass der
Spieler auf den Bildschirm gesehen hat.

- **Die Linien hatten keine Geometrie.** Das y der Linienenden wurde aus sec122
  gelesen, und die hat **keine** `.CWM` — nur die drei Spielstände. Es steht in
  sec34 direkt neben dem x. **609 von 609 Linien** haben jetzt eine Strecke.
- **Das y lief über ein Byte hinaus.** Es zählt halbe Zeilen, auf einer Karte
  über 128 Zeilen läuft es also über: 29 Linien lagen um 128 Zeilen daneben.
  Welcher Kandidat stimmt, entscheiden die **Endgebäude** — eine Bahnlinie
  fängt an einem an und hört am anderen auf. 1144 von 1218 Enden liegen jetzt
  auf ihrem Gebäude, vorher 530.
- **Es gibt ein Gleis, und es ist Teil 64/65 aus ROBO.CWR.** Danach hatte nie
  jemand gesucht, weil der Bildindex eines Waggons *sein* Schienenstück ist —
  sichtbares Gleis gab es also nur dort, wo ein Waggon stand.
- **Es ist eine Hochbahn auf Stützen**, festgestellt an einem Bildschirmfoto
  des Originals: die Böcke stehen in der Landschaft, die Schiene läuft oben
  darüber. Wir hatten sie flach auf den Boden gelegt.
- ⭐ **Die Strecke steht in der KARTE — wir hatten sie erfunden.** Das ist der
  größte Befund dieser Fassung. Sektion 22 führt die fertige Zellenliste, 3000
  Sätze zu 5 Byte: Spalte, Zeile, **Bild**, Trefferpunkte, Liniennummer.
  Gemessen gegen das, was unsere Ableitung auf einer Karte hervorbrachte:
  **472 Gleisstücke, die es dort gar nicht gibt, 359 übersehene, und 235 von
  810 gemeinsamen Zellen mit einem anderen Bild** — rund die halbe Strecke. Der
  Spieler sah es am Bild und musste es **viermal** melden, bevor die richtige
  Frage gestellt war; und die lautete nicht »stimmt unsere Form?«, sondern
  *»führen die Daten das vielleicht schon?«*.
- **Rampen gehören zum Vokabular.** Bild 6..9 sind Rampen, und jede einzelne
  liegt auf einer Zelle mit passendem Geländebyte — 147 von 147, 170 von 170,
  180 von 180, 118 von 118. Dasselbe Byte ist übrigens gar kein Flag, sondern
  die **Hangform**, und genau die liest auch der Zug des Originals, wenn er
  einen Waggon an einer Steigung um 15 px anhebt.
- **Stützen stehen an jeder sechsten Zelle**, in einer von vier Fassungen, je
  nachdem ob die Strecke darüber weiterläuft — beides gelesen, beides keine
  Setzung von uns mehr.
- **Die Linie trifft das Gebäude auf seiner Anschlusszeile**, +1 oder +2 je
  nach Art. 232 von 234 Enden sitzen jetzt auf den Pixel bündig, wo vorher auf
  einer Karte **kein einziges** der 42 Enden bündig war. Der alte offene Punkt
  »die Basis liegt 11 px daneben, über alle Karten konstant« war genau dieser
  Zwei-Zeilen-Versatz.
- **Die Fahrgeschwindigkeit ist ausgerechnet, nicht gewählt** — aus dem
  50-Hz-Takt, einem Schrittpreis von 40 bzw. 28 und einem Abzug von 8 je Takt,
  der auf allen 1439 Waggons über alle 30 Karten derselbe ist. Fünf Takte je
  gerader, vier je diagonaler Schritt.
- **Die Waggons hängen aneinander.** Sie standen zwei Streckenschritte
  auseinander, was den Zug nicht nur wie vier einzelne Wagen aussehen ließ,
  sondern das Gleis gleich mit aufriss.
- **Die halbe Fahrt hat gezittert.** Auf der Rückfahrt lief der Waggon
  innerhalb einer Zelle *rückwärts* und sprang am Übergang zwei vor — netto
  eine Zelle, weshalb weder Fahrzeit noch Streckenlage etwas davon gemerkt
  haben. Die größte Ortsänderung von Bild zu Bild fiel über denselben Lauf von
  **77,96 px auf 2,21 px**. Gemessen worden war es nie, weil die Hinfahrt
  stimmt und die frühere Messung vor der ersten Umkehr entstand.
- **Die Ware fährt wirklich.** Der Zug lädt am Abfahrtsgebäude und liefert am
  Ankunftsgebäude ab; welche Ware in welche Richtung geht, steht in einer
  12×12-Matrix in der EXE, die auf beiden Fassungen byte-gleich ist. Nichts
  sonst im ganzen Programm rührt diese vier Lagerfelder an.
- **Zerstörbar ja, reparierbar ja, baubar nein** — jeweils mit Fundstelle,
  damit die Frage nicht wiederkommt. Eine getroffene Zelle geht kaputt, eine
  ganze Linie fällt nie aus. Ein Fahrzeug repariert und sucht danach von selbst
  das nächste kaputte Stück. Kein Befehl im Original legt ein neues Gleis.
- ⭐ **Das Gleisdeck lag 40 px zu tief — die Böcke hingen in der Luft.** Beide
  GAME.EXE setzen Kacheln auf `zeile·20 − höhe·15 − 50` und Gleis auf `− 62`,
  und unser **x stimmte längst auf den Pixel** — genau das war der Beleg, dass
  allein y falsch war, und zwar um exakt zwei Zellzeilen. Gegenprobe ohne
  Bauen: Teil 65 ist 83 Zeilen hoch mit dem Fuß auf Zeile 82, und der landete
  vorher 40 px *unter* seiner eigenen Zelle. ⚠ Das Werkzeug, das die alte Zahl
  »belegt« hatte, war selbstbezüglich — es setzte die Schiene mit unserer
  eigenen Zahl.
- **Die Waggons sind gekuppelt: 0 sichtbare Lücken statt 338 000.** Erst
  gemessen, wo es klafft (waagerecht in gut der Hälfte aller Takte, senkrecht
  fast nie), dann die Ursache gerechnet: der Rückstand 0/4/7/11 Takte ergibt
  32/24/32 px, die Bilder sind aber 41/22/22/39 px lang. ⚠ **Das Original
  zeigt diese Fugen selbst.** Hier ist zugunsten des Bildes entschieden, und
  zwar so knapp wie möglich: verkürzt wird nur, nie verlängert, und nie unter
  12 px — den kleinsten Abstand, den das Original je erzeugt. Über
  1 010 014 gemessene Waggonpaare bleibt keine Lücke.
- **»Ecken und Knicke schließen nicht« ist nicht reproduzierbar** — der Zähler
  maß die falsche Sache. Seine 4,1 px sind der **gewollte** Versatz, mit dem
  ein Eckstück seine Zelle verlässt, damit die Treppe als Schräge liest (unten
  27,1 · 29,5 · 31,9, symmetrisch, und nur auf der Achse oben/unten). Er misst
  jetzt das **Loch** zwischen den Bildpunkten: 0 von 1119, 0 von 508, 0 von 785
  auf drei Karten.
- ⭐ **Der Zug fährt auf der gezeichneten Schiene, nicht auf den Zellmitten.**
  Eine isometrische Diagonale steht in der Karte als Treppe aus Einzelzellen —
  der Zug lief die Treppe, während die Grafik darunter eine glatte Schräge
  zeigt. Gemeldet als »macht Zicke Zacke beim Fahren«. Die Randmitten stehen in
  der Kunst und sind gemessen; damit ist die alte Sperre **umgangen, ohne sie
  zu brechen** — die Halbzeilen-Parität des Originals bleibt unlesbar, wird
  aber nicht mehr gebraucht. Mittlerer Richtungswechsel je Takt **1,6° → 0,7°**.
  Der Fortschritt läuft dazu über die **Bogenlänge** statt über die Gliedzahl:
  das Original zieht je Takt einen festen Betrag ab, fährt also überall gleich
  schnell.
- **Der Zug sitzt auf der Schiene statt darüber.** Beim Nachziehen der Deckhöhe
  war das Delta aus dem *Rahmen des Originals* genommen worden statt der an den
  Bildern gemessenen Zahl — genau der Fehler, vor dem der Kommentar eine Zeile
  höher warnt.
- **Jede Kreuzung hatte einen Strang zu wenig**, und auf der Rückfahrt stand
  der ganze Zug verkehrt herum, die Lok voran am falschen Ende.

### Grafik und Animation

- **Mechs und Spinnen laufen.** Feld +0x11 ist die Laufphase des Fahrwerks,
  gegengeprüft an 1360 Fahrzeugen über 29 Karten ohne Gegenbeispiel. ⚠ Die
  *Taktung* ist unsere — das Original spielt die Gangbilder nicht ab.
- **Gebäude waren weiß.** Der Kachel-Atlas wurde als eine einzige Spalte
  geschrieben und überschritt bei 30 von 35 Kachelsätzen die Höchsthöhe einer
  Textur; die Grafikkarte lehnte sie ab und gab eine Textur mit toter Kennung
  zurück, die als weiße Fläche gezeichnet wird. Deshalb sah ausgerechnet
  Mission 1 in Ordnung aus und alles ab Mission 2 nicht. Der Atlas bricht jetzt
  in Spalten um.
- **Die mittleren Gebäudebilder sind Schadensstufen, nicht Bauschritte** —
  `bild = (hp_max − hp) / (hp_max / musterzahl)`, übereinandergestempelt,
  gegengeprüft an 36 Karten und 1451 Sätzen.
- **Die Infanterie ballert nicht mehr im Anmarsch.** Sie zeigte die Schusspose,
  sobald sie ein Ziel *hatte* — ein Ziel zu haben heißt aber nicht, darauf zu
  feuern. Die Pose gilt jetzt nur noch eine kurze Frist nach einem wirklichen
  Schuss. ⚠ Diese Frist ist unsere; das Original zählt sie in einem Feld, das
  wir nicht gelesen haben.
- **Handwaffen bekommen keinen Mündungsfeuerball.** Es wurde für jede Waffe
  dieselbe ANIM.CWA-Folge gespielt, Kanone wie Gewehr. Infanterie braucht gar
  keine: ihre Schusspose trägt den roten Blitz **im Sprite**, Bild für Bild
  nachgesehen.
- **Das Mündungsfeuer sitzt am Rohr, nicht am Rumpf.** Es wurde mit einem frei
  erfundenen Versatz von 8 Pixeln nach oben gesetzt und traf damit den Rumpf.
  Jetzt geht es von demselben Punkt aus, über dem das Original den Turm
  montiert — die Rechnung dafür stand für das Turmbild ohnehin schon da.
- **Versorgungshelis erzeugen kein Treffer-Sprite mehr**, wenn sie Sprit oder
  Munition abliefern. Der Effekt war unsere Zutat, und der Kommentar, der ihn
  eingeführt hat, sagte sogar »a blast would read as a hit«, bevor er zum
  Feuerball griff.
- **Der Tote steht nicht mehr auf.** Ein Treffer auf eine Leiche lief bis in
  die Todesroutine durch, und die setzt die Sterbezeit zurück — die
  Umfall-Bilder fingen wieder von vorn an, samt Klang. Zwei der vier Wege
  hinein hatten keinen Schutz; ein Flugzeug hält sein Ziel im eigenen Satz und
  schoss deshalb dauerhaft auf denselben Toten.
- **Unbewaffnete Fahrzeuge tragen ihren Aufsatz und schießen nicht mehr.** Die
  Waffensuche fiel für jedes unbekannte Bauteil auf eine erfundene Waffe
  zurück, Baufahrzeuge haben also wirklich geschossen. Das Spiel unterscheidet
  bewaffnet von unbewaffnet am Feld +0x0d, auf beiden Fassungen belegt und
  gegen 1226 bewaffnete und 218 unbewaffnete Einheiten geprüft, ohne
  Gegenbeispiel. Ausrüstungsträger hatten außerdem gar keinen Aufsatz im Bild.
- **Typ 0 heißt »kein Gebäude«.** Sieben Sätze auf einer Kampagnenkarte tragen
  Typen außerhalb 1..16 — Vorkommen und ein Platzhalter —, und der Platzhalter
  stand mitten in einer Stadt, als wäre er ein Basisgebäude. Sie bleiben in der
  Liste, weil die Missionsskripte sie abfragen, werden aber nicht mehr
  gezeichnet, angewählt oder gezählt.
- **Höheres Gelände gibt mehr Sicht.** Für eine Landeinheit rechnet das
  Original `radius = Höhe + Sicht − 1`; es ist ein größerer *Kreis*, keine
  Sichtlinie — die Stempelroutine liest überhaupt kein Gelände. Gebäude nehmen
  eine wörtliche 10, Schiffe ihr eigenes Feld. Die Formel wurde in beiden
  Fassungen an ihrer Form gefunden, und das `− 1` gehört dazu: auf flachem
  Boden sieht eine Einheit einen Ring weniger weit als ihr blanker Sichtwert.
- ⭐ **Die Strecke wirft Schatten — 1193 Masken, die seit dem ersten Import
  ungenutzt lagen.** Jedes Gleisstück bringt seine Schattenmaske mit (Bilder
  10..19), versetzt nach unten rechts, wohin bei Licht von oben links der
  Schatten fällt. Wie dunkel, ist **gemessen** und nicht gewählt: das Original
  färbt den Untergrund über eine 256-Byte-Tabelle um, und über die Farben, die
  im Gelände wirklich vorkommen, ergibt das 0,775/0,831/0,820 je Kanal bei
  3,1 % Restfehler. Genommen ist der einzelne Faktor 0,809 — 19 % Schwarz.
- ⭐ **Die Hangposen: 4128 Bilder, die nie exportiert wurden.** Ein Teil führt
  sechs Blöcke je Gruppe, und der Block ist die **Neigung**, mit der eine
  Einheit auf schrägem Boden steht. Der Exporter schrieb nur Block 0 — und das
  Bittere daran: der **Turmsitz** wurde längst nach der Hangklasse gerückt, das
  Bild nicht. Der Rumpf blieb flach, während der Boden unter ihm kippte und der
  Turm schon zur Seite rutschte.
- **Die ganze Bank liegt jetzt auf der Platte**: 81 Teile, 3535 Bilder. Der
  Exporter schrieb nur, was auf einer Karte steht oder baubar ist — die 601
  gespeicherten Entwürfe greifen aber auf Bauteile, die auf keiner der 44
  Karten vorkommen: **Minenleger, Flak-Geschütz, Antiradar** und sieben
  weitere. Wer so etwas konstruierte, hatte ein unsichtbares Geschütz.
- **Teil 111 ist der Rotorschatten, kein Rotor.** Der Hubschrauber blinkte mit
  10 Hz zwischen Rotor und schwarzem Kreuz, weil die Maske als zweite
  Rotorphase gezeichnet wurde. Und die acht Bilder des Rotors sind **Phasen**,
  keine Richtungen — er drehte sich gar nicht.

### Die Oberfläche

- **Das Startmenü hat seine Titelleiste und sein Demo wieder.** Das Original
  spielt hinter dem Menü keinen Film ab — es lädt einen fertigen Spielstand und
  lässt ihn weiterlaufen, im Wechsel durch **dreizehn** davon (`1.DM`…`13.DM`);
  genau die schaltet die Zeile »Nächstes Demo« weiter. Titel »Akte Europa« und
  seine Stelle sind gelesen, das »REBORN« darunter ist unseres.
- **»Neues Spiel« heißt jetzt »Kampagne«** und zeigt alle 33 Missionen.
- **Der Gefechtsschirm ist nach Spielmodus und Karten geordnet**, und die drei
  Kampagnenkarten sind daraus verschwunden — eine Kampagnenkarte bringt ihr
  Missionsskript, ihre Diplomatie und ihren Freischalt-Fahrplan mit, und im
  Gefecht läuft nichts davon. Das Durchblättern mit `[` und `]` ist in einer
  laufenden Partie ebenfalls still.
- **Das Bedienfeld gehört nach unten LINKS**, nicht nach rechts. Das Original
  hat gar kein Seitenpanel: die Karte füllt das Fenster, und der Block sitzt in
  der Ecke. Alles andere im Original sind frei schwebende Fenster mit
  Titelleiste und X.
- **Die Missionsuhr steht im Bedienfeld**, an der Ecke, an der das Original sie
  zeichnet (x = 23, y = 148 von PANEL.DTA), und sie zeigt **Stunden:Minuten der
  Spielzeit** — dieselben zwei Bytes, die auch die Statistikseite druckt. Wir
  hatten Echtzeit gedruckt und liefen damit zwölffach zu schnell.
- **Die Missionsziele stehen in der Statuszeile.** Der Block führt sie längst —
  `v[101+k]` ist der Zustand des k-ten Ziels, `v[131+k]` seine Textnummer — und
  angezeigt hat sie nie jemand. Deshalb war die Untermission in Kampagne 1 nicht
  zu sehen, geschweige denn als erfüllt zu erkennen.
- **Das Basisfenster ist wieder ein Fenster**: Titelleiste, Energie, Status,
  die vier Reiter (Depot / Produktion / Forschung / Reparatur), die Liste in
  der Zeilenhöhe des Originals, die drei Teilebestände, vier Knöpfe und eine
  Werteliste rechts. Die drei Rohstoff-Sinnbilder sind keine Erfindung — das
  Original schreibt sie als die gewöhnlichen Zeichen `]`, `[` und `{`, und die
  sind in seiner Schrift genau diese drei Teilesymbole.
- **Das Fenster »Erstellung«** — Fahrwerk, Aufbauteil, Verbesserung und die
  Preise, die Zahl für Zahl mit dem Bildschirmfoto des Originals
  übereinstimmen. Es führt keinen eigenen Zustand: jeder Klick geht durch
  dieselben Schritte wie der Tastaturweg, die beiden können also nicht
  auseinanderlaufen.
- **Das Abschlussfenster** — »MISSION ERFOLGREICH BEENDET«: die Tabelle über
  acht Spalten, Missionszeit, Abschüsse und Verluste, Untermissionen, die
  Missionsbezahlung in Orange, der Kontostand und ein **Weiter**-Knopf, der die
  nächste Mission startet. Abschüsse und Verluste kamen bisher nur aus einem
  Spielstand, eine frisch gestartete Kampagnenmission meldete also 0 und 0.
- **Hilfefenster lassen sich schließen und bleiben geschlossen.** Eine Regel
  ohne Riegel ruft im selben Takt »alle Fenster schließen« und »Text zeigen« —
  ein weggeklicktes Fenster wurde also im selben Atemzug neu gebaut und wirkte
  starr. Dazu ein sichtbares X: das Original sagt in seinem eigenen Text #001,
  dass rechte Maustaste oder ESC schließt, aber wer das Fenster für starr hält,
  liest den Satz nicht mehr.
- **Einstellungen im Pausenmenü**, und das Fenster startet mit 1600×900 und
  ist veränderbar.
- **Der Karteneditor hat eine Zeile im Hauptmenü.** Es gab ihn schon, aber nur
  hinter zwei Schaltern der Befehlszeile — also hinter etwas, das ein Spieler
  nicht hat. ⚠ Er ist ein **Gelände-Generator**, kein Editor im Wortsinn:
  Größe, Kachelsatz, Bodenblock, erzeugen, prüfen, ansehen. Malen, Einheiten
  setzen und das Öffnen vorhandener Karten fehlen, und eine erzeugte Karte
  lässt sich nicht spielen — deshalb heißt der Knopf »Karte ansehen« und nicht
  »Spielen«.
- **Gefecht: »Alle Einheiten«** — eine Option von uns, und der Anlass ist eine
  Lücke in den Daten. Die Gefechtskarten tragen **null Flugzeugvorlagen**; der
  Flughafen hatte nichts anzubieten, gleich wieviel im Lager lag. Mit dem Haken
  bekommen alle acht Spieler die volle Auswahl — **Boden 601 statt 65 Entwürfe,
  Luft 8 statt 0, See 10 statt 2** —, die Gegner eingeschlossen, sonst wäre es
  kein Gefecht, sondern ein Vorteil. ⚠ Dabei fiel auf, dass die
  Flugzeugvorlagen **keine Preise** tragen: die Flugzeuge wären umsonst
  gewesen. Geholt sind sie aus den 13 Karten, die sie führen — je Typ ein Preis
  über 104 Sätze, kein Gegenbeispiel.

### Spielregeln

- **Einheiten entstehen in der BASIS, nicht in der Fabrik.** Gemeldet war ein
  Teilezähler der Fabrik, der immer wieder auf 10 steigt und auf 0 fällt — das
  ist richtig so, der Zug holt die Teile ab. Falsch war die Stelle, an der wir
  bauen ließen. Das Spiel sagt es in seinen eigenen Hilfetexten, die
  Produktionsschaltfläche liest die drei Lager des Gebäudes, dessen Fenster
  offen ist, und die Routine des Computerspielers legt ihre Zeile in eine
  Basis. Der Rückweg Basis → Fabrik ist gestrichen; das Original kennt ihn
  nicht, und er hat die Basis leergezogen.
- **Versorgungshelis kauft man am Nachschub-Posten**, mit Geld, aus einem
  Zwei-Tasten-Dialog — und **keine Kampagnenkarte 1..15 trägt überhaupt einen
  Flughafen**, weshalb Kampagne 2 nicht durchspielbar war. Der Posten auf jener
  Karte gehört niemandem und bleibt es; der Dialog des Originals prüft an
  dieser Stelle den Kontostand und keinen Besitzer, also tun wir es auch nicht.
- **Der Klick im Panel baut auch wirklich.** Er erreichte den Flugzeugkauf nur
  für den Flughafen, der Nachschub-Posten fiel durch: das Panel zeigte seine
  zwei Zeilen, der Klick lief ins Leere. ⚠ Der Prüfstand hat es zweimal nicht
  gesehen, weil er den Kauf direkt aufrief statt zu klicken.
- **Flugzeuge haben ihre Geschwindigkeit.** Der Leser nahm die Vorlage ein Feld
  zu weit, `aircraft.json` trug also gar kein »speed«, und jedes Flugzeug flog
  mit 7 px/s, während ein Fahrzeug 24..84 fährt. Von einer zweiten,
  unabhängigen Stelle in den Kartendateien bestätigt.
- **Ein Klang hat einen Ort.** Ein Schuss am anderen Ende der Karte war genauso
  laut wie einer daneben. Das Original dämpft nach Abstand mit der Konstante 40
  — 0,4 dB je Zelle —, in beiden Fassungen an ihrer Form gefunden. ⚠ Das
  Panorama ist gelesen und **nicht** gebaut.

### Unter der Haube

- **In der ausgelieferten Fassung kam kein einziger Schalter an.**
  `--campaign=3` startete nichts, lautlos, mit Rückgabewert 0: Godot reicht nur
  weiter, was **hinter** `--` steht. Im Entwicklungslauf fiel es nie auf, weil
  `--path .` den Trenner ohnehin erzwingt. Es gibt jetzt eine Brücke, die die
  Schalter so oder so findet.
- **Neue Prüfstände**, jeder gebaut, um eine bestimmte Fehlerklasse zu sehen,
  statt ein grünes Ergebnis zu bestätigen: `--rail-check`, `--rail-lay` (die
  Gegenprobe), `--tick-check`, `--produce-check`, `--econ-check`,
  `--pay-check`, `--unarmed-check`, `--depot-check`, `--sound-check`,
  `--infdeath-check`, `--tutorial-check`, `--script-coverage`, `--skirmish`,
  `--end-window`, `--shot-when=squash`. Mehrere davon wurden gegengeprobt,
  indem die Korrektur testweise wieder herausgenommen wurde.
- **Teil-Neuexporte**, damit eine Änderung keinen vollen Import kostet:
  `--reexport-campaign`, `--reexport-help`, `--reexport-tables`,
  `--reexport-buildings`, `--reexport-units`, `--reexport-effects`,
  `--reexport-states`.

### Mehrspieler, und der Determinismus, den er zuerst brauchte

- ⚠ **Die Simulation lief auf BILDZEIT.** Gleicher Keim, gleiche Karte, gleiche
  *simulierte* zehn Sekunden — und je nach Bildrate drei verschiedene Zustände:

  | Bilder/s | Takte | Prüfsumme | Energie | Schüsse |
  |---|---|---|---|---|
  | 30 | 300 | `5071A756A80B2634` | 36911 | 58 |
  | 60 | 600 | `8C5F21CD5F8AF503` | 36965 | 55 |
  | 144 | 1440 | `F45A1091E165730F` | 36911 | 58 |

  `_Process(double delta)` reichte die echte verstrichene Bildzeit in die
  Spielwelt, allein in einer Datei an 74 Stellen: wer schneller zeichnet,
  würfelt öfter und schießt öfter. Zwei Zwillinge mit 1/60 und 1/30 liefen nach
  **18 Takten** auseinander. Alles, was den Zustand anfasst, steht jetzt in einem
  Takt mit fester Länge, und es gibt **einen** Würfel statt eines je Fabrik frisch
  und *zufällig* gekeimten.
- **Eingaben sind Befehle geworden — erst gelesen, dann gebaut.** Das Original
  hat genau einen Weg von der Eingabe in den Zustand, und das ist eine
  Lockstep-Befehlsschlange: 236-Byte-Sätze in einem Ring mit 1000 Plätzen.
  Gefunden in **beiden** GAME.EXE an ihrer FORM, nicht an ihrer Adresse
  (Verteiler `0x4C2262` / `0x4C26E0`, Ring `0xB4FA38` / `0xB509D8`). Ein Klick ist
  jetzt ein Satz und wirkt am **Anfang** des nächsten Takts, nie mitten im Bild.
- **Zwei echte Prozesse spielen dieselbe Partie über ENet.** Sie verbinden sich,
  verteilen Karte und Keim, schicken 600 Takte lang Befehle über die Leitung und
  melden an **jedem** Prüftakt dieselbe Prüfsumme. Die Gegenprobe — auf einer
  Seite wird ein Satz verschluckt — schlägt an und nennt Takt, Einheit und Feld.
  Zuvor wurde das Lockstep-Protokoll des Originals vollständig gelesen: Opcode
  1003 »Spieler ist bereit«, 978 die Rundenfreigabe, `+0x22` die Rundennummer.
- **Eine LAN-Lobby, damit niemand eine IP tippt.** Absichtlich Frage-und-Antwort
  statt Leuchtfeuer: der Suchende weiß, wann seine Suche anfing, und kann darum
  ehrlich »nach 1200 ms nichts gefunden« sagen — bei einem Leuchtfeuer sind »ich
  habe nichts gehört« und »ich habe nicht lange genug gehorcht« derselbe Zustand.
- **»Partie starten« hat das Programm beendet.** Kein Netzfehler: eine
  Zwanzig-Sekunden-Grenze, die für einen kopflosen Prüfstand *richtig* ist (ein
  ewiges Warten hält die Bausperre) und auf dem Spielerweg schlicht falsch. Die
  zwei Wege sind jetzt getrennt.
- **Der Mehrspieler lebt in seiner eigenen Zeile im Hauptmenü.** »Netzwerkspiel«
  war die ganze Zeit da (Platz 65, Hilfeindex 106) und tat nichts; sie heißt jetzt
  Multiplayer und führt hin. Im Gefecht ist von Netz nichts mehr zu sehen, und
  »Intro ansehen« ist aus dem Hauptmenü heraus.

### Gefecht

- **Die Luftliste hängt am Techstandard 1..8, nicht an einer Option von uns** —
  Tor `0x419F30`, gemessen auf drei Karten (Stufe 1 → 2, 4 → 3, 5 → 4, 6 → 6
  Vorlagen; keine Stufe ergibt ein leeres Menü). Zwei eigene Begründungen fallen
  damit: die Gefechtskarten tragen Sektion 120 **gar nicht** (alle 23 `.CWM`
  enden bei Sektion 39, Kopfbyte 1 statt 2 — 23 von 23 gegen 13 von 13), und die
  Vorlagen der EXE **haben** Preise. Eine *fehlende* Sektion überschreibt nichts,
  also bleibt im Original die Tabelle der EXE stehen.
- **Die Zeile »Techstandard« kommt erst jetzt, da sie wirkt** — ein Schalter ohne
  Wirkung ist genau das, woran der Spieler vorher hängengeblieben ist. Nichts
  daran ist unsere Setzung: der Bereich ist der des Knopfs, die Vorgabe die eines
  frischen Spiels, die Beschriftung die des Originals.
- **Eine stehende Rohstoffleiste mit dem Zuwachs je Sekunde** (Taste `Q`). Die
  ist unsere Zutat und ist so gekennzeichnet. Der Befund dahinter: das Original
  führt für Fabriken **keine Bauzeit** — alle drei Herstellungswege setzen die
  Einheit im selben Takt in die Welt, in dem der Befehl abgearbeitet wird. Es hat
  stattdessen ein Depot mit sechs Plätzen.
- Der Haken für die Luftwaffe war an seiner Stelle nicht wahrzunehmen und ist
  durch die gelesene Regel oben ersetzt.

### Die Kampagne

- ⚠ **Die Uhr war eine Bedingung, und der Regelleser hat sie still verworfen.**
  Ein Leser, der für ein ungelesenes Glied nichts zurückgibt, lässt `set_rules`
  die **ganze Regel** wegwerfen, und niemand erfährt davon. Jetzt wird jedes
  verworfene Glied benannt und gezählt: 207 Regeln ohne gelesene Bedingung, 118
  unlesbare Vergleichswerte, 91 Vergleiche ohne Aufruf. Uhr und Einheitenindex
  nachgetragen ergaben **+83 Regeln in 25 Missionen**.
- **Die Kampagne setzt ihre Einheiten und schickt sie los**: `place_unit` an
  `0x4D0810` — 60 Aufrufe in 14 Missionen, die größte Lücke im Vokabular — samt
  der Befehle, die dazugehören.
- **Mission 23 ist spielbar.** Die »vier Eckhöhen« eines Bauplatzes sind gar
  keine Höhen: es sind **Klassen** aus Sektion 2 (0 unpassierbar, 1 Ufer/Sand, 2
  offenes Land, 3 besonders), und `corners_carry` verlangt offenes Land auf allen
  vier Ecken. Die Feld-Rohstoffmine ging dort von **0 auf 57** Bauplätze.
- ⚠ **Die Belegungskarte ist SPALTENWEISE** (`Spalte*256 + Zeile`), belegt aus dem
  Rumpf, der sie stempelt. Unser Leser hatte sie vertauscht, was 29 Bedingungen
  in 12 Missionen betraf; **8 davon konnten nie wahr werden**, und Mission 7 war
  unspielbar. Das Regelwerk kennt jetzt außerdem **ODER**.
- **Zurückgezogen: »Mission 5 gewinnt nicht.«** Der Defekt lag im Prüfstand, der
  ein *beliebiges* Bauwerk übergab — auf einer Karte einen Skriptplatz ohne
  Gebäudeart.
- **Die »22 gleich / 4 abweichend« von `--selftest-cwm` waren vier vertauschte
  Vergleichsdateien.** `10.CWM` und `10.DM` haben denselben Dateistamm, wer beide
  exportiert, überschreibt die Kampagnenkarte still mit einem **Spielstand**. Mit
  zwei sauberen Sätzen: 26 Karten gleich, 0 abweichend.

### Der Karteneditor

- **Die Messlatte des Geländegenerators sind die 26 gelieferten Karten.** Jeder
  Zähler war grün, während das Bild ein Schachbrett war — Wasseranteil,
  Hangbytes, Höhensprünge alle im Rahmen, und die Kacheln setzten sich im
  Nachbarn trotzdem nicht fort. **Die Naht entscheidet, nicht der Schlüssel**:
  ein Schlüssel sagt, welche Codes das Original in einer *Lage* benutzt, nie
  welchen davon es *neben* welchen setzt. Harte Nähte von **8,65 %** auf
  0,22–3,11 % (Median der gelieferten Karten: 0,58 %).
- ⚠ Eine Kernzahl dieser Messung war falsch und hat sich selbst verraten: die
  Teile summierten sich auf 607.090 Zellen, während die 26 Karten 605.090 haben.
  Es sind **29.990** Zellen mit zwei angrenzenden höheren Nachbarn, nicht 31.990
  — und die drei *gegenüberliegenden* Fälle stehen jetzt mit ihrer Stelle da.
- **Erzeugte Karten haben Rohstoffvorkommen.** Im Original legt sie das
  **Missionsskript** (`add_terra_place`, 50 Aufrufe in 8 Missionen) — eine
  erzeugte Karte hat keins, ihr Boden war also leer. Die Feld-Rohstoffmine findet
  jetzt **8, 8 und 48** Bauplätze, wo sie keinen fand, mit der Dichte am Original
  geeicht (0,23 gegen 0,24 Vorkommen je 1000 begehbare Zellen). ⚠ Jede Verteilung
  dort ist **unsere Zutat** und sagt das an fünf Stellen.
- **Und ein echter Wirtschaftsfehler, der auch die Kampagne traf:**
  `Entity.Deposit` fing bei −1 an, eine **gebaute** Mine förderte also nie.
  Gemessen über zehn Wirtschaftstakte: 5000 → 4950 im Boden, 50 gefördert, 50 im
  Lager der Mine.

### Die Einheitenbilder

- **86 Bilder, aus den ANIM.CWA-Folgen 400..403** (Rahmen 1176..1261, lückenlos,
  am Dateikopf nachgerechnet). Die Aufteilung ist restlos vergeben: 0..56
  Bauteile, 57..66 Schiffsrümpfe, 67..73 Flugzeuge, 74..85 Personen. Byte **+0x0D**
  des 58-Byte-Bauteilsatzes *ist* die Bildnummer. Sie erscheinen jetzt an allen
  drei Stellen, die der Spieler genannt hat: im Bedienblock unten links, im
  modularen Bausystem und in der Basis.
- ⚠ **Für Gebäude gibt es keines — und das Original hat auch keines.** Der
  Zeichner hat genau sechs Fälle, und keiner nimmt ein Gebäude; er berührt die
  Gebäudetafel nie; und ein Gebäude kann gar nicht das angewählte Objekt *sein*
  (die Anwählroutine kennt nur Landeinheiten und Flugzeugplätze, der Zahlenraum
  dazwischen wird nie geschrieben). Vier unabhängige Messungen, alle mit rohen
  Abtasten.
- ⚠ **Zwei »Bytetafeln« waren Sprungtafeln.** Bei `0x450C98` stehen 13
  Codeadressen — 12 Fälle plus **Fehlerzweig**, keine 13. Einheit. Bei `0x450D60`
  stehen zehn, und die zweite trägt die eigentliche Permutation, weil Rumpf 151
  unter den zehn Entwürfen **zweimal** vorkommt; »Rumpf − 150« hätte der
  Flak-Barkasse die Raketen des L.Kreuzers und dem Schlachtschiff den kleinen
  Flak-Kahn gegeben.
- ⚠ **Das `yoff`-Byte gehört zum Bild.** Ohne es kommt Rahmen 1177 als 51x38
  heraus, wo er auf der Blit-Leinwand 51x60 ist — und genau dieses Byte setzt Turm
  und Fahrwerk zueinander.

### Schiffe und Fahrzeuge stehen auf ihrer eigenen Zelle

- **Schlachtschiff und Kreuzer: das BILD lag falsch, nicht der Rahmen.**
  Auswahlrahmen, Lebensbalken und Besitzerring standen eine halbe Schiffslänge
  neben dem Schiff. Der Rahmen ist die Grundrissfläche aus der Belegungskarte und
  stimmt aufs Byte (4x4 beim Schlachtschiff, die Satzzelle ist die linke obere,
  kein Gegenbeispiel bei 56 gestempelten Schiffen). Das Original zeichnet auf die
  **Satzzelle** — kein Zeichenfall trägt einen Term über die Größe der Einheit,
  die Ausdehnung steckt im Bild selbst. 121 Einheiten berichtigt.
- **Die Waffe der Rümpfe 157 und 158 wird nicht mehr gezeichnet.** Sie haben
  keinen Montagepunkt — und das Original hat auch keinen Wert: sein Schiffszeichner
  hat eine Weiche mit drei Fällen, alles andere fällt in
  `"Wrong chassis of ship"` und liest danach eine Stack-Zelle, die **nie
  geschrieben** wird. Originaltreu ist hier nicht herstellbar, darum die
  Entscheidung des Spielers: keine Waffe. Die Rumpfbilder tragen ihre Geschütze
  ohnehin selbst.
- **64 Landeinheiten: ein zweizelliger Stempel ist kein Grundriss, sondern ein
  Schritt.** Schiffe stempeln volle Rechtecke; Landeinheiten sind erdrückend
  einzellig und tragen im Ausnahmefall **genau zwei** Zellen, nie drei. Die zweite
  liegt in der **Blickrichtung** — eine geschlossene Windrose gegen `facing`, 60
  von 64, und 55 von 55 für die Blicke 1..7. Das ist die für den nächsten Schritt
  reservierte Zelle, kein Körper. Standpunkt auf dem sichtbaren Bild: **vorher 2
  von 64, nachher 52 von 64**.

### Die Bahn, Fortsetzung

- **`delka` ist die Länge der Streckencodes, nicht die Zellenzahl.** Über alle 30
  Karten gezählt: `delka` minus Zellenzahl ist **4 in 369 von 371** Linien, und
  die 4 ist hergeleitet und nicht geraten — richtet man die Kette aus Sektion 22
  auf die `delka+1` Routenpunkte aus, fallen immer genau fünf Punkte weg, zwei an
  einem Ende und drei am anderen. Die zwei Ausnahmen sind genau die zwei Karten
  mit Fremdzellen unter der Nummer 0, also der Fehler, den die Regel sichtbar
  macht, und kein Gegenbeispiel.
- **Das fehlende letzte Gleisstück fehlt nicht — es liegt unter dem Gebäude.**
  Gemessen an 476 Linienenden: 224 (47 %) sind überdeckt, 128 davon vollständig,
  und bei Fabrik, Mine und Flughafen sind es 166 von 166, weil ihre Enden zwei
  Reihen weiter innen liegen. ⚠ **Das Original verdeckt sie genauso**, belegt aus
  seiner gefächerten Zeichenliste (Gleis in Fach Zeile+2, Gebäude bei Zeile+5).
  Der Spieler hat originaltreu entschieden, und diese Entscheidung steht im Code,
  damit sie später niemand für einen Fehler hält.

### Behoben, und eine Diagnose zurückgezogen

- ⚠ **Ein `ConfigFile` je Lesezugriff hat das Programm beim Beenden abstürzen
  lassen.** »Leaked unsafe reference to object« in Serie, danach `0xC0000005` im
  Finalizer, Rückgabewert 139 oder 132. Die zweite Hälfte dieses Fehlers wäre ohne
  den Absturz nie aufgefallen: jeder Zugriff war ein **Plattenzugriff**, und vier
  der Einstellungen werden im Bildlauf gefragt — die Einstellungsdatei wurde also
  bis zu 60 mal je Sekunde gelesen. Die Einstellungen halten jetzt eines, das
  Einheitenbuch und die Kampagne geben ihres frei. ⚠ Die Kampagne darf **keines
  halten**: `--fresh-campaign` räumt ihren Stand von *außen* weg, und ein
  gehaltenes Abbild würde den alten Fortschritt weiter behaupten. Beides belegt.
- ⚠ **Die 97 Leckzeilen zu `JSON`/`Image` sind KEINE fehlende Freigabe.** Sonden
  an der echten Ausstiegsstelle haben 97 nicht freigegebene Bilder auf drei Wegen
  hergestellt — weggeworfen, in einer statischen Liste festgehalten, und in der
  Form *aller* echten Ladestellen — und jeder Lauf meldete **null** Leckzeilen und
  Rückgabewert 0. Es ist ein Wettlauf beim Herunterfahren. Die ganze
  Kandidatenliste ist damit vom Tisch, und die Fehldiagnose steht aufgeschrieben,
  damit sie niemand wiederholt.

### Bekannte Grenzen

- **Die Waggons einer Linie können auf einer Zelle stehen.** Gemessen: in rund
  10 % der Bilder einer fahrenden Linie, im schlimmsten Fall alle vier auf
  derselben Fließkommastelle — man sieht dann einen Waggon statt vier. Die
  Ursache ist verstanden: die Waggons hängen an einer gemeinsamen Zugspitze mit
  Versatz und klemmen an der Endstation alle in dieselbe Grenze. **Absichtlich
  nicht behoben**: das Original gibt jedem Waggon einen eigenen Zähler und
  einen eigenen Streckenzeiger, und die Routine, die ihre Abfahrt zeitversetzt,
  ist ungelesen. Jede Abhilfe davor wäre eine Erfindung.
- ~~**Die Anstoßregel der Nebenmission von Kampagne 2 ist nicht eingetragen.**~~
  **Noch in dieser Fassung geschlossen.** Das Vokabular kennt jetzt **ODER** — das
  Original schreibt es als zwei Abfragen hintereinander —, und drei Regeln, die es
  gar nicht in die Datei geschafft hatten, kamen damit mit (Kampagne 2 bei
  `0x498EEC`, Kampagne 3 bei `0x4996B7`, Mission 15 bei `0x49D89D`). Kampagne 2s
  Nebenmission läuft damit überhaupt erst an.
- ~~**Mission 5 ist im Spiel noch nicht gewinnbar.**~~ **Zurückgezogen** — der
  Defekt lag im Prüfstand, der ein beliebiges Bauwerk übergab. `--produce-check`
  zeigt unabhängig, dass Mission 5 in sechs Sekunden gewinnt, sobald sie ihre
  Fabriken hat.
- **Die Missionen 21 und 28 haben kein Skript** — als einzige der 33.
- **Die Einheitenklassen 1..4 werden nicht überall auseinandergehalten**; wo
  eine Regel sie braucht, steht das in der Datendatei.
- **Das Klangpanorama ist gelesen und ungebaut**, und der Einschlag bei
  direktem Beschuss ist gar nicht gelesen. Ein erfundener Einschlag bei jedem
  Gewehrschuss wäre schlechter als keiner.
- **Die Rohrlänge (14 px) ist unsere.** Die richtige Zahl ist gelesen — sie
  steht in SHOOT.CWT, 2400 Sätze zu vier Punkten —, aber diese Datei läuft noch
  nicht durch den Import.
- **Der Mehrspieler ist zwischen zwei Prozessen auf EINER Maschine belegt.** Das
  ist blind für genau eine Fehlerklasse: `Entity.Pos` ist `float`, und zwei
  verschiedene Rechner müssen sich darüber nicht einig sein. Ungeprüft. Außerdem
  wird **die Spielernummer im Paket geglaubt**, und die KI schreibt weiter direkt
  in den Zustand statt durch den Befehlsring — auf einer Maschine rechnen beide
  Seiten sie gleich, die gleichen Prüfsummen sagen über eine KI-Partie also
  weniger, als sie aussehen.
- **Große erzeugte Karten stürzen beim Laden ab.** Eine 254×254-Karte kam einmal
  durch und danach sechsmal nicht, mit demselben Wettlauf beim Herunterfahren wie
  bei den Leckzeilen oben (`Godot.DisposablesTracker`, Rückgabewert 139). Große
  Karten sind damit derzeit nicht verlässlich prüfbar.
- **Der Inhaltsbauer schreibt keine Flugzeugpreise**, deshalb muss der Rückfall
  bleiben, der sie aus der Nutzlast herleitet — ohne ihn wären Flugzeuge umsonst,
  und das ist gemessen, nicht befürchtet.
- **Der Geländegenerator kann die Klasse 3 der Sektion 2 nicht**, in der 32 % der
  Vorkommen des Originals liegen.

⚠ **Nach dem Aktualisieren einmal neu einspielen.** Hilfetexte, die
Bahn-Zellen, die Rampenbilder und der reparierte Kachel-Atlas entstehen beim
Import; wer seinen alten Datenordner behält, bekommt sie nicht.
`--reexport-states` und `--reexport-units` zusammen genügen für die Bahn.

## Ältere Fassungen

0.4.0 und älter sind bisher nur in [CHANGELOG.md](CHANGELOG.md) beschrieben.
