# Änderungen

Alle nennenswerten Änderungen an Akte Europa Reborn. Ausgeliefert wird **nur
die Engine** — Gelände, Einheiten, Karten, Tabellen und Klänge entstehen auf
dem eigenen Rechner aus der eigenen Fassung des Spiels von 1997.

*In English: [CHANGELOG.md](CHANGELOG.md). Ältere Fassungen als 0.5.0 sind
bisher nur dort beschrieben.*

## 0.5.0 — 12.08.2026

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

### Bekannte Grenzen

- **Die Waggons einer Linie können auf einer Zelle stehen.** Gemessen: in rund
  10 % der Bilder einer fahrenden Linie, im schlimmsten Fall alle vier auf
  derselben Fließkommastelle — man sieht dann einen Waggon statt vier. Die
  Ursache ist verstanden: die Waggons hängen an einer gemeinsamen Zugspitze mit
  Versatz und klemmen an der Endstation alle in dieselbe Grenze. **Absichtlich
  nicht behoben**: das Original gibt jedem Waggon einen eigenen Zähler und
  einen eigenen Streckenzeiger, und die Routine, die ihre Abfahrt zeitversetzt,
  ist ungelesen. Jede Abhilfe davor wäre eine Erfindung.
- **Die Anstoßregel der Nebenmission von Kampagne 2 ist nicht eingetragen.**
  Sie braucht ein ODER, und das Regelvokabular kennt nur UND. Die Nebenmission
  läuft deshalb gar nicht erst an — eine sichtbare Lücke statt einer stillen,
  falschen Belohnung.
- **Mission 5 ist auf der Skriptseite vollständig, im Spiel aber noch nicht
  gewinnbar.** Sie braucht eine laufende Produktion; der Prüfstand stellt sie
  her.
- **Die Missionen 21 und 28 haben kein Skript** — als einzige der 33.
- **Die Einheitenklassen 1..4 werden nicht überall auseinandergehalten**; wo
  eine Regel sie braucht, steht das in der Datendatei.
- **Das Klangpanorama ist gelesen und ungebaut**, und der Einschlag bei
  direktem Beschuss ist gar nicht gelesen. Ein erfundener Einschlag bei jedem
  Gewehrschuss wäre schlechter als keiner.
- **Die Rohrlänge (14 px) ist unsere.** Die richtige Zahl ist gelesen — sie
  steht in SHOOT.CWT, 2400 Sätze zu vier Punkten —, aber diese Datei läuft noch
  nicht durch den Import.

⚠ **Nach dem Aktualisieren einmal neu einspielen.** Hilfetexte, die
Bahn-Zellen, die Rampenbilder und der reparierte Kachel-Atlas entstehen beim
Import; wer seinen alten Datenordner behält, bekommt sie nicht.
`--reexport-states` und `--reexport-units` zusammen genügen für die Bahn.

## Ältere Fassungen

0.4.0 und älter sind bisher nur in [CHANGELOG.md](CHANGELOG.md) beschrieben.
