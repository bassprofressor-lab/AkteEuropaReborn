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
- **Ein Fluggerät blickt dorthin, wo es hinfliegt.** Bisher stand der Rumpf quer
  zum Flugpfeil. Der Zustand hatte immer gestimmt, falsch war die Zuordnung von
  Richtung zu Bild — das Original rechnet sie mit einem festen Versatz von 90
  Grad aus. Zwei Wege, die nichts voneinander wissen, kommen auf dieselbe Zahl:
  eine Eichung an den Panzern und die Rechnung im Zeichenpfad des Originals.
  ⚠ Was **bleibt**, ist Original und kein Fehler: eine Drehung geht sechs Grad
  je Takt, eine volle Wendung dauert 60 Takte. Nach einem Zielwechsel fliegt ein
  Heli also bis zu 30 Takte lang seitwärts, bevor er sich ausgerichtet hat.

### Kampagne und Oberfläche

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
