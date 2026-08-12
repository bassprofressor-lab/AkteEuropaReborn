using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// DAS BAHNSYSTEM (SPOJ) — so laufen die Teile im Original zusammen.
///
/// Die Frage des Spielers war: »wie bekomme ich die Ressourcen von den Fabriken
/// zusammen?« Die Antwort steht vollständig in GAME.EXE, auf BEIDEN Fassungen
/// gegengeprüft (Regel 8; Leser <c>aekernel-tools/spoj_re.py</c>):
///
/// <list type="number">
/// <item><b>Der Zug lädt am Abfahrtsgebäude und lädt am Ankunftsgebäude ab.</b>
/// <c>spoj_launch</c> @0x4C6410 (F: 0x4C5FC0) zieht die vier Waren vom
/// Quellgebäude ab (<c>sub word [ecx+0xc0693c], ax</c>), der Zug-Tick @0x4C69C0
/// schreibt sie dem Zielgebäude gut (<c>add word [eax+0xc0693c], cx</c>,
/// @0x4C6D17 / @0x4C716C). <b>Nichts anderes im ganzen Programm bewegt die vier
/// Lagerfelder für den Spieler.</b></item>
///
/// <item><b>Je Linie vier Warenschalter</b>, SPOJ-Satz +0x08..+0x0b (0 Waffen,
/// 1 Fahrwerk, 2 Spezial, 3 Terranium): <b>1</b> = Knoten1→Knoten2, <b>0</b> =
/// umgekehrt, <b>2</b> = fährt hier nicht. Das Ladewerk prüft wörtlich
/// <c>schalter[k] + richtung == 1</c> (@0x4C65BA/0x4C65E2/0x4C660A/0x4C6632).</item>
///
/// <item><b>Das Spiel stellt die Schalter SELBST ein.</b>
/// <c>spoj_set_default_transport</c> @0x4B1170 (F: 0x4B0AA0) kopiert vier Byte
/// aus der 12×12-Typmatrix @0x504128 (F: 0x503168, <b>byte-identisch, 0 von 576
/// verschieden</b>) — Index <c>(12·(typA−1) + (typB−1))·4</c>, wobei die
/// Werft-Station (16) als Seedock (11) zählt (<c>cmp bl, 0xf</c> @0x4B12B2).
/// Der Kartenlader @0x41F2A2 wendet sie auf JEDE Linie an, der Gebäude-Tick
/// @0x43CEA2 erneuert sie bei JEDEM Besitzerwechsel.
/// ⚠ <b>Darum sind die Schalter in der Kartendatei bedeutungslos</b> — sie
/// werden beim Laden überschrieben. Diese Klasse rechnet sie deshalb aus der
/// Matrix nach, genau wie das Original, statt sie zu importieren.</item>
///
/// <item><b>Die Züge fahren von allein.</b> <c>spoj_tick</c> @0x4C7840 ist ein
/// Automat über <c>faze</c> (+0xd5):
/// <code>
///   knoten1 == 0xFF                       -> Linie unbenutzt
///   faze ∉ {3,4} und Besitzer verschieden -> faze := 4   (Linie tot)
///   faze == 4 und Besitzer gleich         -> faze := 0   (Linie lebt wieder)
///   faze == 0    -> faze := 1, spoj_launch(linie, 0, 0)
///   faze &lt;= 9  -> nichts; der Zug ist unterwegs
///   sonst faze++ ;  100 -> spoj_launch(linie,0,1), faze := 2
///                   200 -> spoj_launch(linie,0,0), faze := 1
/// </code>
/// Bei Ankunft setzt der Zug-Tick <c>faze := 0x50</c> (80, am Knoten2) bzw.
/// <c>0xb4</c> (180, am Knoten1) — @0x4C6C68 / @0x4C70BD. Daraus folgt die
/// <b>Standzeit: je 20 spoj_tick-Runden</b> an jedem Ende.</item>
///
/// <item><b>Beladung:</b> reihum eine Einheit je Ware, Budget <c>mov al, 0xc8</c>
/// = <b>200 je Fahrt</b> (@0x4C6652), begrenzt durch den Bestand des
/// Abfahrtsgebäudes.</item>
/// </list>
///
/// <b>Was die Matrix in den eigenen Worten des Spiels sagt</b> (aus der Tabelle
/// gelesen, nicht gedeutet): Mine → Fabrik/Bahnstation/Feldbahnhof Terranium ·
/// Bahnstation/Feldbahnhof → Fabrik Terranium · Fabrik → Basis/Bahnstation/
/// Feldbahnhof/Flughafen/Seedock ihr eigenes Bauteil · Bahnstation und
/// Feldbahnhof → Basis/Flughafen/Seedock alle drei Bauteile. <b>Das Netz läuft
/// also über die Bahnhöfe, nicht Fabrik-an-Basis.</b> Auf NET05 sind acht
/// Feldbahnhöfe der Umschlagplatz für 8 Basen, 15+17+9 Fabriken und 9 Minen.
///
/// <b>⚠ UNSERE SETZUNG ist allein die FAHRZEIT</b> (<see cref="RailStepSeconds"/>):
/// der Automat zählt Takte, und wie lang ein Takt in Sekunden ist, steht
/// nirgends. Alles andere — Standzeit, Ladebudget, Richtungsregel, Matrix,
/// Besitzerprüfung — ist aus dem Programm gelesen.
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public const int GoodW = 0, GoodF = 1, GoodS = 2, GoodT = 3;

    /// <summary>Die vier Waren in der Reihenfolge der Lagerfelder +0x2c..+0x32.</summary>
    public static readonly string[] GoodName = { "Waffen", "Fahrwerk", "Spezial", "Terranium" };

    /// <summary>Die 12×12×4-Vorbelegungsmatrix, <b>wörtlich</b> aus GAME.EXE
    /// VA 0x504128 (F: 0x503168 — 0 von 576 Byte verschieden). Zeile = Typ des
    /// Gebäudes an Knoten1, Spalte = Typ an Knoten2, vier Byte je Zelle
    /// (Waffen, Fahrwerk, Spezial, Terranium): 1 = Knoten1→Knoten2,
    /// 0 = Knoten2→Knoten1, 2 = fährt nicht.
    /// Erzeugt von <c>aekernel-tools/spoj_re.py csharp</c>.</summary>
    public static readonly byte[] SpojDefault =
    {
        2,2,2,2, 0,2,2,2, 2,0,2,2, 2,2,0,2, 2,2,2,2, 0,0,0,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 0,0,0,2,   // 1 Basis
        1,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 1,2,2,0, 2,2,2,2, 2,2,2,2, 1,2,2,2, 2,2,2,0, 1,2,2,2, 1,2,2,0,   // 2 Waffen-Fabrik
        2,1,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,1,2,0, 2,2,2,2, 2,2,2,2, 2,1,2,2, 2,2,2,0, 2,1,2,2, 2,1,2,0,   // 3 Fahrwerk-Fabrik
        2,2,1,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,1,0, 2,2,2,2, 2,2,2,2, 2,2,1,2, 2,2,2,0, 2,2,1,2, 2,2,1,0,   // 4 Spezial-Fabrik
        2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2,   // 5 Depot
        1,1,1,2, 0,2,2,1, 2,0,2,1, 2,2,0,1, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 1,1,1,2, 2,2,2,0, 1,1,1,2, 2,2,2,2,   // 6 Bahnstation
        2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2,   // 7 Generator
        2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2,   // 8 Radarstellung
        2,2,2,2, 0,2,2,2, 2,0,2,2, 2,2,0,2, 2,2,2,2, 0,0,0,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 0,0,0,2,   // 9 Flughafen
        2,2,2,2, 2,2,2,1, 2,2,2,1, 2,2,2,1, 2,2,2,2, 2,2,2,1, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,1,   // 10 Mine
        2,2,2,2, 0,2,2,2, 2,0,2,2, 2,2,0,2, 2,2,2,2, 0,0,0,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 0,0,0,2,   // 11 Seedock
        1,1,1,2, 0,2,2,1, 2,0,2,1, 2,2,0,1, 2,2,2,2, 2,2,2,2, 2,2,2,2, 2,2,2,2, 1,1,1,2, 2,2,2,0, 1,1,1,2, 2,2,2,2,   // 12 Feldbahnhof
    };

    /// <summary>Werft-Station (16) zählt als Seedock (11) — <c>cmp bl, 0xf</c>
    /// @0x4B12B2, beide Endpunkte gleich behandelt.</summary>
    private static int FoldType(int t) => t == 16 ? 11 : t;

    /// <summary>Die vier Warenschalter einer Linie zwischen zwei Gebäudetypen,
    /// gerechnet wie <c>spoj_set_default_transport</c> @0x4B1170. Gibt
    /// <c>{2,2,2,2}</c> („fährt nichts") für alles ausserhalb der Matrix.</summary>
    public static void SpojModeFor(int typeA, int typeB, byte[] into)
    {
        int a = FoldType(typeA) - 1, b = FoldType(typeB) - 1;
        if (a is < 0 or >= 12 || b is < 0 or >= 12)
        {
            into[0] = into[1] = into[2] = into[3] = 2;
            return;
        }
        int i = (12 * a + b) * 4;
        into[0] = SpojDefault[i]; into[1] = SpojDefault[i + 1];
        into[2] = SpojDefault[i + 2]; into[3] = SpojDefault[i + 3];
    }

    /// <summary>Eine SPOJ-Linie mit ihrem Zug. <c>Faze</c> ist das Satzbyte
    /// +0xd5 und trägt dieselben Zahlen wie im Original.</summary>
    public sealed class RailLine
    {
        public int Slot, Bud1, Bud2, Steps;
        public readonly byte[] Mode = { 2, 2, 2, 2 };
        public int Faze;                        // Satz +0xd5
        public readonly int[] Cargo = new int[4];
        public int Dir;                         // 0 = Bud1->Bud2, 1 = zurueck
        public float Travel;                    // Sekunden bis zur Ankunft (unsere Setzung)
        public float TravelFull;
        public int Trips;
        public readonly long[] MovedGood = new long[4];
        public long Moved;
        public bool WasFaze4;

        /// <summary>true = die Waggons dieser Linie hat der Automat selbst
        /// angelegt (die Karte brachte keine mit) und räumt sie am Ziel wieder
        /// weg. false = von der Karte übernommen, die bleiben stehen.</summary>
        public bool OwnWagons;
    }

    private readonly List<RailLine> _railLines = new();
    private readonly Dictionary<int, Entity> _bldBySlot = new();

    /// <summary>Wie oft der Automat je Sekunde läuft. <b>Aus den Daten:</b> die
    /// Hauptschleife ruft <c>spoj_tick</c> nur, wenn <c>bildzähler % 5 == 0</c>
    /// (@0x41638D, F: @0x4161CD — beide Fassungen, derselbe Zähler wie der
    /// Gebäude-Tick, der jedes Bild läuft). Unsere Wirtschaft rechnet
    /// <see cref="TickScale"/> = 16 Originaltakte je Sekunde, also läuft der
    /// Automat 16/5 = 3,2 mal je Sekunde. Damit ist die <b>Standzeit</b> an
    /// jedem Ende 20 Runden = 6,25 s — gerechnet, nicht gesetzt.</summary>
    private const float RailTickSeconds = 5f / TickScale;

    /// <summary>
    /// <b>⚠ 13.08.2026 — KEINE SETZUNG MEHR.</b> Die Fahrzeit einer Linie ist
    /// die Summe der Takte ihrer Streckenschritte: <b>5 Takte je gerader, 4 je
    /// diagonaler Schritt</b>, gerechnet aus dem Schrittpreis (40 bzw. 28,
    /// @0x4C6E53) und dem Abzug je Takt (sec44 +0x0c = 8 auf allen 1439
    /// Waggons aller 30 Karten) — die ganze Ableitung steht bei
    /// <see cref="TrainStepTicksStraight"/>. <see cref="RailTravelSeconds"/>
    /// rechnet sie je Linie aus deren eigenen Streckencodes aus, statt eine
    /// Zahl mit der Schrittzahl zu multiplizieren. Diese Konstante ist nur noch
    /// der Rückfall für eine Linie ohne Codes.</summary>
    private const float RailStepSeconds = TrainStepSeconds;

    /// <summary>
    /// <b>Wieviele TAKTE jeder Waggon hinter Waggon 0 herfährt</b> — gemessen,
    /// nicht gesetzt.
    ///
    /// <para><c>spoj_launch</c> legt bei der Abfahrt nur Waggon 0 an
    /// (Startzähler 20), und Waggon <c>w+1</c> entsteht erst, wenn <c>w</c>
    /// seinen Streckenzeiger auf 1 schaltet — mit den Startzählern
    /// <b>20 / 40 / 25 / 40</b> aus den Sprungtabellen @0x4C687C und @0x4C688C
    /// (F: 0x4C6428 / 0x4C6438, beide Fassungen gleich) und dem Abzug 8 je Takt
    /// (sec44 +0x0c). Über 709 Linien aus 54 Karten nachgefahren: die Abstände
    /// sind 4, 3 und 4 Takte, 21632 Messungen je Paar, kein Gegenbeispiel.</para>
    ///
    /// <para>Zum Vergleich kostet ein Streckenstück 5 Takte (gerade) oder 4
    /// (diagonal) — der Rückstand ist also KLEINER als ein Schritt. Deshalb
    /// stehen zwei Waggons im Original ständig auf derselben Zelle und trotzdem
    /// nie auf demselben Punkt.</para></summary>
    private static readonly int[] RailWagonLagTicks = { 0, 4, 7, 11 };

    /// <summary>Die Fahrzeit dieser Linie in Sekunden, aus ihren eigenen
    /// Streckencodes. Ein UNGERADES Stück ist der Halbschritt einer Diagonale
    /// und kostet 4 Takte, ein gerades eine ganze Zelle und kostet 5.
    ///
    /// <para><c>_linePiece</c> trägt <c>delka + 1</c> Einträge, und der erste
    /// ist eine Wiederholung des zweiten (so legt <c>CwmExtra.Links</c> ihn
    /// an). Gezählt wird deshalb ab 1 — das sind genau die <c>delka</c>
    /// Schritte der Linie.</para></summary>
    private float RailTravelSeconds(RailLine l)
    {
        if (_linePiece.TryGetValue(l.Slot, out var pcs) && pcs.Count > 1)
        {
            float t = 0f;
            for (int i = 1; i < pcs.Count; i++)
                t += (pcs[i] & 1) != 0 ? TrainStepSecondsDiagonal : TrainStepSeconds;
            return t;
        }
        return l.Steps * RailStepSeconds;
    }

    /// <summary>Ladebudget je Fahrt — <c>mov al, 0xc8</c> @0x4C6652.</summary>
    private const int RailLoadBudget = 200;

    /// <summary>Standzeit an einem Endpunkt: von 80 auf 100 bzw. von 180 auf 200,
    /// je einen Schritt je Automatenrunde (@0x4C78FF).</summary>
    private const int RailDwellTicks = 20;

    private float _railAcc;

    /// <summary>Nur zum Melden: Fahrten und bewegte Menge über den ganzen Lauf.</summary>
    public int RailTrips;
    public long RailMoved;
    private readonly long[] _railMovedGood = new long[4];

    /// <summary>Aus dem `links`-Feld der Karte: eine Linie mit ihren beiden
    /// Endgebäuden. Die Schalter kommen NICHT aus der Datei — siehe Klassenkopf.</summary>
    private void AddRailLine(int slot, int bud1, int bud2, int steps)
    {
        if (slot < 0 || bud1 < 0 || bud2 < 0 || bud1 == bud2) return;
        _railLines.Add(new RailLine { Slot = slot, Bud1 = bud1, Bud2 = bud2, Steps = Mathf.Max(1, steps) });
    }

    /// <summary>Gebäude nach Platznummer — der Zug spricht seine Endpunkte über
    /// den Knoten an, und der Knoten nennt die Platznummer (sec33 +0x00).</summary>
    private Entity? RailBuilding(int slot)
    {
        if (_bldBySlot.TryGetValue(slot, out var hit)) return hit;
        return null;
    }

    private void RebuildRailIndex()
    {
        _bldBySlot.Clear();
        foreach (var e in _entities)
            if (e.IsBuilding && !_bldBySlot.ContainsKey(e.Slot)) _bldBySlot[e.Slot] = e;
    }

    private static int RailStore(Entity e, int good) => good switch
    {
        GoodW => e.StockW, GoodF => e.StockF, GoodS => e.StockS, _ => e.StockT,
    };

    private static void RailAdd(Entity e, int good, int n)
    {
        // Das Original rechnet in u16 (`add word …`), also deckeln wir dort.
        switch (good)
        {
            case GoodW: e.StockW = Mathf.Clamp(e.StockW + n, 0, 65535); break;
            case GoodF: e.StockF = Mathf.Clamp(e.StockF + n, 0, 65535); break;
            case GoodS: e.StockS = Mathf.Clamp(e.StockS + n, 0, 65535); break;
            default: e.StockT = Mathf.Clamp(e.StockT + n, 0, 65535); break;
        }
    }

    /// <summary>Fährt diese Ware von diesem Gebäude aus über irgendeine Linie
    /// fort? Damit tritt der Nahweg-Ersatz genau dort zurück, wo die Bahn die
    /// Arbeit schon tut — und nur dort.</summary>
    private bool RailCarriesFrom(int slot, int good)
    {
        foreach (var l in _railLines)
        {
            if (l.Faze == 4) continue;
            if (l.Bud1 == slot && l.Mode[good] == 1) return true;
            if (l.Bud2 == slot && l.Mode[good] == 0) return true;
        }
        return false;
    }

    public bool HasRailFreight => _railLines.Count > 0;

    // ---- der Automat --------------------------------------------------------

    /// <summary>Die Dauer des letzten Takts — die Flüssigkeitszahl misst px je
    /// TAKT, nicht je Sekunde, und ein langer Takt bewegt den Waggon weiter.
    /// Ohne diese Zahl daneben ist nicht zu unterscheiden, ob das Modell hakt
    /// oder nur ein Bild lang gebraucht hat.</summary>
    private float _railLastDt;

    private void UpdateFreight(float dt)
    {
        _railLastDt = dt;
        if (_railLines.Count == 0) return;
        if (_bldBySlot.Count == 0) RebuildRailIndex();
        // ⚠ 11.08.2026 — die FAHRT laeuft je Bild, der AUTOMAT weiter im Takt.
        //
        // Vorher zog RailStep die Fahrzeit in Stufen von RailTickSeconds
        // (0,3125 s) ab, und ein Streckenschritt dauert 0,35 s: die Zugspitze
        // ruckte also einmal je Automatenrunde um fast eine ganze Zelle. Die
        // Summe bleibt dieselbe — es wird nur nicht mehr in Brocken abgezogen.
        foreach (var l in _railLines)
            if (l.Faze is >= 1 and <= 9 && l.Travel > 0f) l.Travel -= dt;

        _railAcc += dt;
        int guard = 0;
        while (_railAcc >= RailTickSeconds && guard++ < 8)
        {
            _railAcc -= RailTickSeconds;
            RailStep(RailTickSeconds);
        }
    }

    /// <summary>Eine Runde <c>spoj_tick</c> @0x4C7840 über alle Linien.</summary>
    private void RailStep(float dt)
    {
        foreach (var l in _railLines)
        {
            var a = RailBuilding(l.Bud1);
            var b = RailBuilding(l.Bud2);

            // ⚠ UNSERE ERGÄNZUNG: ein zerstörtes Endgebäude legt die Linie still
            // wie ein fremder Besitzer. Das Original prüft an dieser Stelle nur
            // den Besitzer — es räumt einen zerstörten Satz aber ohnehin weg,
            // und wir behalten ihn.
            if (a == null || b == null || a.Dead || b.Dead) { l.Faze = 4; continue; }

            // faze 3 überspringt die Besitzerprüfung, faze 4 ist die tote Linie
            if (l.Faze == 4)
            {
                if (a.Owner != b.Owner) continue;
                l.Faze = 0;                       // @0x4C78E0
            }
            else if (l.Faze != 3 && a.Owner != b.Owner)
            {
                l.Faze = 4;                       // @0x4C789D
                l.WasFaze4 = true;
                continue;
            }

            // @0x43CEA2: die Schalter werden bei jedem Besitzerwechsel neu aus
            // der Matrix gesetzt. Wir rechnen sie jede Runde nach — dasselbe
            // Ergebnis, ohne den Wechsel abfangen zu müssen.
            SpojModeFor(a.BType, b.BType, l.Mode);

            if (l.Faze == 0)
            {
                l.Faze = 1;
                RailLaunch(l, 0, a, b);
            }
            else if (l.Faze <= 9)
            {
                // unterwegs — die Fahrzeit laeuft schon in UpdateFreight je
                // Bild herunter (siehe dort); hier wird nur die Ankunft
                // gemeldet, damit sie im Takt des Automaten faellt.
                if (l.Travel <= 0f) RailArrive(l, a, b);
            }
            else
            {
                l.Faze++;
                if (l.Faze == 100) { RailLaunch(l, 1, a, b); l.Faze = 2; }
                else if (l.Faze == 200) { RailLaunch(l, 0, a, b); l.Faze = 1; }
            }
        }
    }

    /// <summary><c>spoj_launch</c> @0x4C6410: reihum eine Einheit je Ware vom
    /// Abfahrtsgebäude, höchstens <see cref="RailLoadBudget"/> je Fahrt.</summary>
    private void RailLaunch(RailLine l, int dir, Entity a, Entity b)
    {
        var src = dir == 0 ? a : b;
        l.Dir = dir;
        for (int k = 0; k < 4; k++) l.Cargo[k] = 0;

        // `schalter[k] + richtung == 1` — @0x4C65BA und die drei Zwillinge
        var avail = new int[4];
        for (int k = 0; k < 4; k++)
            avail[k] = l.Mode[k] + dir == 1 ? RailStore(src, k) : 0;

        int budget = RailLoadBudget;
        while (budget > 0 && (avail[0] | avail[1] | avail[2] | avail[3]) > 0)
            for (int k = 0; k < 4 && budget > 0; k++)
            {
                if (avail[k] <= 0) continue;
                avail[k]--; l.Cargo[k]++; budget--;
            }

        for (int k = 0; k < 4; k++) if (l.Cargo[k] > 0) RailAdd(src, k, -l.Cargo[k]);

        l.TravelFull = RailTravelSeconds(l);        // aus den Streckencodes, gerechnet
        l.Travel = l.TravelFull;
        RailSpawnWagons(l);
    }

    /// <summary>Ankunft: entladen (@0x4C6D17 / @0x4C716C) und faze auf 80 bzw.
    /// 180 setzen (@0x4C6C68 / @0x4C70BD) — von dort zählt der Automat die
    /// 20 Runden Standzeit ab.</summary>
    private void RailArrive(RailLine l, Entity a, Entity b)
    {
        var dst = l.Dir == 0 ? b : a;
        for (int k = 0; k < 4; k++)
        {
            if (l.Cargo[k] <= 0) continue;
            RailAdd(dst, k, l.Cargo[k]);
            l.MovedGood[k] += l.Cargo[k];
            _railMovedGood[k] += l.Cargo[k];
            l.Moved += l.Cargo[k];
            RailMoved += l.Cargo[k];
            l.Cargo[k] = 0;
        }
        l.Trips++;
        RailTrips++;
        l.Travel = 0f;
        l.Faze = l.Dir == 0 ? 100 - RailDwellTicks : 200 - RailDwellTicks;
        RailClearWagons(l);
    }

    // ---- die sichtbaren Waggons --------------------------------------------
    //
    // »Züge entstehen aus dem Automaten, sie sind kein Kartenobjekt«: alle vier
    // Gefechtskarten mit Linien liefern 0 Zugsätze und jede Linie mit faze == 0
    // (NET02 33/33, NET04 36/36, NET05 35/35, NET08 42/42).
    //
    // Zwei Fälle, und beide werden hier vom Fahrplan bedient:
    //  * Die Karte bringt Waggons mit (die .DM-Karten, 96..133 Stück): dann
    //    ÜBERNIMMT der Automat sie, sobald ihre Linie zum ersten Mal abfährt.
    //    Damit fällt der alte Notbehelf in UpdateTrains weg, der einen Zug am
    //    Streckenende einfach umdrehen liess (»the original's end handling was
    //    not reconstructed«) — es ist jetzt rekonstruiert: der Zug fährt eine
    //    Fahrt und STEHT dann 20 Automatenrunden am Bahnsteig.
    //  * Die Karte bringt keine mit: dann legt der Automat vier an, wie das
    //    Original. ⚠ Das setzt eine gezeichnete Strecke voraus, und die kommt
    //    aus sec122 — die haben nur die .DM-Karten. Auf den .CWM-Gefechtskarten
    //    fährt die Ware, es ist nur nichts zu sehen (offener Punkt im Import).

    private readonly Dictionary<int, List<Wagon>> _freightWagons = new();

    private void RailSpawnWagons(RailLine l)
    {
        if (!_lineCell.TryGetValue(l.Slot, out var route) || route.Count < 2) return;
        if (!_freightWagons.TryGetValue(l.Slot, out var list))
        {
            _freightWagons[l.Slot] = list = new List<Wagon>();
            // erst übernehmen, was die Karte für diese Linie schon mitbringt
            foreach (var w in _wagons)
                if (w.Line == l.Slot) { w.Freight = true; list.Add(w); }
            list.Sort((x, y) => x.Index.CompareTo(y.Index));
            l.OwnWagons = list.Count == 0;
            if (l.OwnWagons)
                for (int i = 0; i < 4; i++)
                {
                    var w = new Wagon { Line = l.Slot, Index = i, Freight = true, Dir = 1 };
                    list.Add(w);
                    _wagons.Add(w);
                }
        }
        var pd = RailPathOf(l.Slot);
        RailPlaceWagons(l, pd?.Pts ?? route, pd?.Cum);
    }

    /// <summary>Am Ziel angekommen: selbst angelegte Waggons verschwinden wieder,
    /// von der Karte übernommene bleiben stehen, wo sie hielten.</summary>
    private void RailClearWagons(RailLine l)
    {
        if (!l.OwnWagons) return;
        if (!_freightWagons.TryGetValue(l.Slot, out var list)) return;
        foreach (var w in list) _wagons.Remove(w);
        _freightWagons.Remove(l.Slot);
    }

    /// <summary><paramref name="route"/> ist seit dem 12.08.2026 die
    /// ZELLENKETTE der Linie (<c>_lineCell</c>), nicht mehr die Route auf
    /// halben Zeilen. Der Zug faehrt damit auf genau den Zellen, auf denen
    /// auch das Gleis liegt — vorher lag jeder Punkt auf halber Zeile 10 px
    /// neben der Schiene, und der Waggonabstand musste ueber
    /// <c>StepBackColumns</c> geschaetzt werden, weil ein Routenschritt mal
    /// eine ganze und mal eine halbe Zelle war. Eine Zelle ist jetzt eine
    /// Zelle: Waggon <c>i</c> steht <c>i</c> Zellen zurueck, fertig.</summary>
    private void RailPlaceWagons(RailLine l, List<Vector2> route, float[] cum = null)
    {
        if (!_freightWagons.TryGetValue(l.Slot, out var list) || list.Count == 0) return;
        float p = l.TravelFull <= 0f ? 1f : 1f - Mathf.Clamp(l.Travel / l.TravelFull, 0f, 1f);
        int last = route.Count - 1;
        // ⚠ 11.08.2026 — GLEITEND statt springend. Hier stand
        // `Mathf.RoundToInt(p * last)`, und damit sass jeder Waggon immer auf
        // einem ganzen Routenschritt: der Zug HUEPFTE je Schritt um eine ganze
        // Zelle (40 px) weiter, gemeldet als »fährt auch etwas ruckelig«.
        // Der Bruchteil bleibt jetzt erhalten und wird zwischen zwei Schritten
        // ausgemittelt. Das BILD (w.Piece) haengt weiter am ganzen Schritt —
        // ein Schienenstueck gibt es nur in acht Richtungen, ein halbes gibt es
        // nicht.
        _lineCellPiece.TryGetValue(l.Slot, out var pcs);
        // Der Rueckstand in ZELLEN unserer Kette. ⚠ Er darf NICHT ueber die
        // Fahrzeit gerechnet werden: die zaehlt STRECKENSCHRITTE, und eine
        // Diagonale sind zwei davon auf einer Zelle. Auf Linie 4 der map_NET02
        // stehen 9 Schritte gegen 5 Zellen -- der erste Anlauf hat den Abstand
        // dadurch auf 7,6 px zusammenschrumpfen lassen statt der rund 32 px,
        // die das Original haelt.
        //
        // Ein gerader Schritt kostet 5 Takte und ist genau eine Zelle
        // (Schrittpreis 40, Abzug 8 -- @0x4C6E53 / sec44 +0x0c). Ein Takt ist
        // damit ein Fuenftel Zelle. ⚠ UNSERE NAEHERUNG: auf einer Diagonale
        // kostet eine Zelle im Original acht Takte (zwei Halbschritte zu 4),
        // dort waere der Rueckstand also etwas kleiner. Unsere Kette kennt den
        // Unterschied nicht mehr, seit eine Diagonale EINE Zelle ist.
        float lagCells = last > 0 ? 1f / 5f / last : 0f;
        // ⚠ Die Reihenfolge ist jetzt Vorschrift, nicht Zufall: die Kupplung
        // hängt Waggon i an die BEREITS GESETZTE Stelle von i-1. Eigene Züge
        // legt RailSpawnWagons der Reihe nach an, von der Karte übernommene
        // nicht zwingend — deshalb hier einmal sortieren.
        list.Sort((a, b) => a.Index.CompareTo(b.Index));
        // ⚠ 15.08.2026 — ZWEI DURCHGÄNGE, und der Grund ist ein Ei-und-Henne:
        // die Kupplung braucht die POSE beider Waggons (ein waagerechtes Bild
        // ist 41 px lang, ein senkrechtes 23), die Pose steht aber erst fest,
        // wenn der Waggon PLATZIERT ist. Der erste Anlauf hat deshalb `w.Piece`
        // aus dem VORIGEN Takt gelesen — und lag genau in dem einen Takt falsch,
        // in dem ein Waggon um die Ecke geht: auf Linie 31 forderte die Rechnung
        // 32 px (waagerechte Lok) für ein Bild, das dann senkrecht mit 23 px
        // gezeichnet wurde. 10,0 px Lücke, und der Zähler meldete »Abstand
        // eingehalten«.
        //
        // Durchgang 0 setzt alle vier auf ihre getaktete Stelle und damit ihre
        // Pose; Durchgang 1 kuppelt mit den nun feststehenden Posen. Beides ist
        // dieselbe Schleife — die Platzierung darf nicht zweimal dastehen.
        for (int pass = 0; pass < 2; pass++)
        foreach (var w in list)
        {
            // ⚠ 13.08.2026 — DER RUECKSTAND IST EINE ZEIT, KEINE ZELLENZAHL.
            //
            // Hier stand `step = clamp(lead - dir*Index, 0, last)`: alle vier
            // Waggons hingen an EINER Zugspitze mit Indexversatz, und an der
            // Endstation lief jeder in dieselbe Klammer. Gemessen standen
            // dadurch in ~10 % der Linienbilder mehrere Waggons auf derselben
            // Zelle -- im schlimmsten Fall alle vier auf derselben
            // Fliesskommastelle, also vier Sprites Pixel auf Pixel.
            //
            // Das Original kennt gar keine Zugspitze. Jeder Waggon ist ein
            // eigener Satz (Platz = Linie + 60*Waggonnummer, Feld 0xB95F48 mit
            // 240 Saetzen; die Umrechnung steht in spoj_launch @0x4C6713,
            // F: 0x4C62BE) mit eigenem Zaehler +0x08 und eigenem
            // Streckenzeiger +0x0a. spoj_launch legt bei der Abfahrt NUR
            // Waggon 0 an; Waggon w+1 entsteht erst in dem Takt, in dem w
            // seinen Zeiger auf 1 schaltet (@0x4C6D6B, rueckwaerts @0x4C7204).
            // Die vier Startzaehler sind fest 20/40/25/40 (Sprungtabellen
            // @0x4C687C/0x4C688C, F: 0x4C6428/0x4C6438), der Abzug ist 8 je
            // Takt -- daraus die gemessenen Rueckstaende 0/4/7/11 Takte
            // (21632 Messungen je Paar, kein Gegenbeispiel).
            //
            // Weil ein Streckenstueck 4 oder 5 Takte dauert, der Rueckstand
            // aber nur 3..4, stehen zwei Waggons im Original in 54,76 % aller
            // Bilder auf DERSELBEN ZELLE -- und trotzdem in 0 von 216606
            // Bildern auf demselben Punkt (kleinster Abstand 12 px). Es trennt
            // sie die Feinlage, nicht die Zelle. Genau das bilden wir hier ab:
            // der Rueckstand geht als ZEIT in den Fahrtfortschritt, und
            // gerundet wird erst ganz am Ende.
            //
            // An der Endstation wird nicht geklemmt. Das Original loescht
            // jeden Waggon einzeln (`+0x00 := 0`); bei uns faellt er aus dem
            // Fortschrittsband und wird schlicht nicht gezeichnet -- deshalb
            // erscheinen die vier bei der Abfahrt nacheinander und
            // verschwinden bei der Ankunft nacheinander, wie im Original.
            int k = Mathf.Clamp(w.Index, 0, RailWagonLagTicks.Length - 1);
            float pw = p - RailWagonLagTicks[k] * lagCells;
            w.Hidden = pw < 0f || pw > 1f;

            // ⚠ Der Fortschritt läuft über die BOGENLÄNGE, nicht über die
            // Gliedzahl — sonst fährt der Zug auf kurzen Gliedern langsam und
            // auf langen schnell, und die Waggons stauchen sich an jeder Ecke.
            // Warum das bei RailPathData steht.
            float q = Mathf.Clamp(pw, 0f, 1f);
            if (l.Dir == 1) q = 1f - q;
            float leadF = cum != null && cum.Length == route.Count
                        ? RailArcToIndex(cum, q * cum[^1])
                        : q * last;
            w.RawLeadF = leadF;
            // Verkürzt wird immer gegen den ROHWERT, nie gegen das Ergebnis des
            // letzten Takts — sonst zöge sich der Zug über die Fahrt hinweg
            // immer weiter zusammen.
            if (pass == 1)
                leadF = RailCouple(route, list, w, pcs, leadF, l.Dir == 0 ? 1 : -1);
            w.LeadF = leadF;
            int lead = Mathf.FloorToInt(leadF);
            float frac = leadF - lead;
            // ⚠ 11.08.2026 — hier stand der Faktor 2, und das war der Grund
            // fuer beide Beobachtungen des Spielers auf einmal: »die Bahn war
            // weder sauber zusammengebaut, noch war dort eine Bahnstrecke«.
            //
            // Ein Streckenschritt ist eine Zelle, und ein Waggon ist eine Zelle
            // breit. Mit Abstand 2 klaffte zwischen jedem Waggon eine Luecke —
            // der Zug sah aus wie vier einzelne Wagen. Und weil der BILDINDEX
            // eines Waggons SEIN SCHIENENSTUECK ist (Zeichenpfad @0x42B4C0,
            // Tabelle SpojPiece @0x5393F0), riss damit auch das Gleis auf:
            // sichtbares Gleis gibt es nur dort, wo ein Waggon steht, und mit
            // Luecken dazwischen ist keine durchgehende Strecke zu sehen.
            //
            // Abstand 1: die vier Waggons haengen aneinander und ihre vier
            // Schienenstuecke ergeben ein durchgehendes Stueck Gleis.
            //
            // ⚠ OFFEN, Stand 11.08.2026: eine Strecke OHNE Zug zeichnen wir
            // gar nicht. Gesucht wurde am zweiten Zweig desselben Zeichenpfads
            // -- ab @0x42b550 arbeitet er nicht am Waggon, sondern an der
            // Kartenzelle (Satz 0xb95f50, Schrittweite 24, +0x03 ist ein
            // Streckencode 0..6), und @0x42b624 rechnet
            //     bx = word[code*2 + 0x4fa218] + Grundbild(Teil 57)
            // Die acht Eintraege dieser Tabelle sind 46, 47, 44, 45, 42, 43,
            // 40, 41 -- also Bild 40..47 von Teil 57, was nach
            // CwrFile.PartFrame Block 5 waere, und die Zuordnung ist glatt
            // `Richtung = Stueck XOR 6`.
            //
            // Das sah nach dem gesuchten blanken Gleiskoerper aus. Ist es
            // NICHT: Block 5 von Teil 57 wurde exportiert und angesehen -- acht
            // WAGENKOERPER, kein Gleis. Die Rechnung stimmt also, aber unser
            // PartBase(57) ist nicht das, was das Spiel unter 0x77c956 fuehrt,
            // oder der Zweig zeichnet etwas anderes als eine Strecke. Solange
            // das nicht geklaert ist, wird hier nichts behauptet.
            // ⚠ 11.08.2026 — der Abstand zaehlt SPALTEN, nicht Schritte.
            //
            // Ein Waggonbild ist wie ein Gleisbild genau eine Zelle breit
            // (gemessen x 10..49 = 40 px). Eine isometrische Diagonale legt die
            // Route aber als Treppe aus (1,0) und (0,0.5): zwei Schritte, eine
            // Spalte. Mit einem Schritt Abstand standen dort zwei Waggons in
            // derselben Spalte, auf der Geraden dagegen einer je Spalte -- der
            // Zug riss also genau auf den Diagonalen auseinander. Gemeldet als
            // »der zug hat immer noch luecken zwischen seinen wagons/locks«.
            // ⚠ 12.08.2026 — der Abstand ist wieder EIN SCHRITT, weil ein
            // Schritt jetzt eine ZELLE ist. StepBackColumns hat Spalten
            // gezaehlt, um die Treppe einer Diagonale auszugleichen; die
            // Treppe gibt es in der Zellenkette nicht mehr.
            int dir = l.Dir == 0 ? 1 : -1;
            int step = Mathf.Clamp(lead, 0, last);
            w.Step = step;
            w.Dir = dir;
            // ⚠ 12.08.2026 — hier stand `step + dir`, und das hat die halbe
            // Fahrt zum Zittern gebracht: auf der RUECKFAHRT lief der Waggon
            // innerhalb einer Zelle rueckwaerts und sprang am Uebergang zwei
            // vor (gemessen: -37 px / +78 px im Wechsel, netto die richtige
            // eine Zelle). Auf der Hinfahrt stimmte es, deshalb hat es keine
            // Messung gesehen -- die 2,0..4,5 px vom 11.08. sind vor der
            // ersten Umkehr entstanden.
            //
            // `lead = floor(leadF)` und `frac = leadF - lead` heissen IMMER
            // »zwischen route[lead] und route[lead+1]«, auch wenn leadF faellt.
            // Der Nachbar in Bruchteilsrichtung ist damit richtungsunabhaengig
            // `step + 1`. Die Fahrtrichtung steckt schon in `step` selbst
            // (`lead - dir*Index` legt Waggon i hinter die Spitze) und in
            // `w.Dir`, das das Bild waehlt.
            var pt = RailPathPoint(route, leadF);
            w.Col = pt.X; w.Row = pt.Y;
            // ⚠ 15.08.2026 — DAS BILD MUSS DIE FAHRTRICHTUNG ZEIGEN, nicht die
            // Kettenrichtung. `pcs[step]` ist die Richtung von Zelle step zur
            // NAECHSTEN in Kettenreihenfolge. Der Zug faehrt aber nur auf der
            // Hinfahrt in dieser Reihenfolge; auf der Rueckfahrt laeuft er
            // dagegen — und zeigte trotzdem weiter kettenvorwaerts. Damit stand
            // auf jeder rueckfahrenden Linie der ganze Zug verkehrt herum, Lok
            // voran am falschen Ende. Genau das ist als »es gibt sogar Loks die
            // vertauscht sind, aber nicht alle« gemeldet worden — nicht alle,
            // weil es immer nur die Linien trifft, die gerade zurueckfahren.
            //
            // Die Gegenrichtung eines Stuecks ist `(stueck + 4) & 7`; dieselbe
            // Rechnung benutzt das Original fuer die Schublok (@0x42B542).
            if (pcs != null && step < pcs.Count)
                w.Piece = dir > 0 ? pcs[step] : (pcs[step] + 4) & 7;
        }
    }

    /// <summary>
    /// <b>DIE RANDMITTEN — gemessen, und der Einbau ist damit BLOCKIERT.</b>
    ///
    /// <para>Das Original setzt einen Waggon nicht in die Zellmitte, sondern
    /// auf eine <b>Randmitte</b>, und was von beidem gilt, entscheidet die
    /// <b>Parität der HALBZEILE</b> (Wortsatz 0xB95D60, je Waggonplatz):
    /// Feinlage <c>(20,0)</c> bei gerader, <c>(0,10)</c> bei ungerader
    /// Halbzeile (@0x4C6A87 / @0x4C6E23). Das ist belegt: die zwölf
    /// Routencodes @0x5043C0 gehen gegen <c>Stück→Pixel</c> @0x539400 nur mit
    /// genau dieser Setzung auf — zwölf unabhängige Gleichungen, alle
    /// erfüllt.</para>
    ///
    /// <para><b>Wir können es nicht einbauen, und das ist gemessen, nicht
    /// gemutmaßt.</b> Unsere Kette kommt seit dem 12.08. aus sec22 und kennt
    /// nur GANZE Zellen — die Parität steckt allein in den Streckencodes. Über
    /// fünf NET-Karten gegeneinander gehalten:</para>
    /// <code>
    ///   sec22-Zellen                  3079
    ///   Routenzellen                  3338
    ///   davon in sec22                2816   (84 %)
    ///   sec22-Zellen ohne Routenpunkt  263
    ///   Zellen mit BEIDEN Paritäten    311
    /// </code>
    /// <para>Für rund <b>eine von sechs</b> Zellen wäre die Parität also
    /// unbekannt oder mehrdeutig. Sie zu setzen hieße raten.</para>
    ///
    /// <para>⭐ <b>Und darin steckt der eigentliche Befund:</b> Gleis und Zug
    /// laufen im Original auf ZWEI VERSCHIEDENEN Strukturen — die Strecke in
    /// sec22, der Zug über die Streckencodes (<c>+0x0a</c> ist ein Zeiger in
    /// die Route). Die beiden decken sich nur zu 84 %, und das ist kein
    /// Lesefehler, sondern die Bauart. Unsere Umstellung vom 12.08., den Zug
    /// auf die sec22-Kette zu setzen, war eine Vereinfachung; die treue Fassung
    /// führt beide nebeneinander und lässt die Feinlage die Waggons auf die
    /// Schiene setzen. Das ist ein eigener Durchgang, kein Zusatz hier.</para>
    ///
    /// <b>Die Zahl gegen »ruckelt es?«</b> — nicht beurteilt, gemessen: die
    /// größte Ortsänderung eines Waggons von einem Bild zum nächsten, in
    /// Kartenpixeln, und die daraus folgende Geschwindigkeit in Zellen je
    /// Sekunde.
    ///
    /// <para>Zum Vergleich: das ORIGINAL bewegt einen Waggon in Stufen von
    /// <c>abzug · Δ / preis</c> = 8·40/40 = <b>8 px je Takt</b> auf gerader
    /// Strecke (@0x4C6BA1), also fünf Sprünge je Zelle. Bleibt unsere Zahl je
    /// Bild darunter, läuft der Zug feiner als das Original.</para>
    ///
    /// <para>⚠ 12.08.2026 — hier stand »ein Sprung über 100 px ist keine
    /// Bewegung, sondern ein Wechsel der Fahrt und zählt nicht mit«. Das war
    /// eine SETZUNG, kein Kriterium, und sie hat danebengelegen: die Umkehr des
    /// gemessenen Waggons springt um zwei Zellen = 78 px, also UNTER die
    /// Grenze. Damit stieg die Zahl über einen langen Lauf von 4,5 auf 78 px
    /// und sah aus wie ein Ruckeln, das es nicht gibt. Jetzt entscheidet, was
    /// wirklich geschah — siehe <see cref="RailWagonTurnCount"/>.</para>
    /// </summary>
    public float RailWagonMaxPxPerFrame, RailWagonCellsPerSec;
    public string RailWagonJumpWhere = "";

    /// <summary>Die Fahrtwechsel des gemessenen Waggons (Richtungswechsel oder
    /// Sprung über mehr als einen Schritt), getrennt gezählt statt an einer
    /// Pixelgrenze weggeworfen. <see cref="RailWagonTurnMaxPx"/> ist der
    /// größte dabei zurückgelegte Weg.</summary>
    public int RailWagonTurnCount;
    public float RailWagonTurnMaxPx;

    /// <summary>
    /// <b>Das Band, in dem die Fahrgeschwindigkeit wirklich liegt</b> — px je
    /// SEKUNDE, über alle Fahrbilder.
    ///
    /// <para>⚠ 15.08.2026 — <see cref="RailWagonMaxPxPerFrame"/> misst den Weg
    /// je TAKT, und der hängt an der Bildzeit: auf map_DM_4 meldete er 18,55 px
    /// gegen 2,21 px auf map_NET02. Instrumentiert war die Antwort eindeutig —
    /// <b>jeder große Schritt hatte exakt 124 px/s</b>, nur <c>dt</c> schwankte
    /// zwischen 82 und 104 ms, weil die grosse Karte kopflos mit rund elf
    /// Bildern je Sekunde läuft. Die Zahl vermischte also Modellgüte mit
    /// Bildzeit und konnte beides nicht auseinanderhalten.</para>
    ///
    /// <para><b>Die Gegenprobe steckt in der Zahl selbst:</b> das Original
    /// bewegt einen Waggon 8 px je 20-ms-Takt = <b>400 px/s</b>; bei unserem
    /// <c>TickScale 16</c> gegen dessen 50 sind das <b>128 px/s</b>. Gemessen
    /// 124 — das Band ist die richtige Antwort auf »ruckelt es?«, denn ein
    /// enges Band heisst gleichmässig, ganz gleich wie lang ein Bild
    /// dauert.</para></summary>
    public float RailWagonPxPerSecMin = float.MaxValue, RailWagonPxPerSecMax;

    /// <summary><b>Die Frage, die bisher kein Zählwerk gestellt hat:</b> stehen
    /// die vier Waggons einer Linie auch auf VIER Zellen? Beim Wenden klemmt
    /// <c>step</c> in <see cref="RailPlaceWagons"/> auf <c>last</c>, also sitzen
    /// sie eine Weile übereinander. Gezählt wird das Schlimmste und wie oft es
    /// vorkommt; <b>ob es im Bild stört, sagt ein Bild und nicht diese Zahl.</b>
    /// </summary>
    public int RailSquashWorst, RailSquashFrames, RailSquashSeen;

    /// <summary>Ab welchem Abstand zwei Waggons als „aufeinander" gelten, in
    /// Kartenpixeln. Der kleinste Abstand, den das Original je erzeugt, ist
    /// <b>12 px</b> (nachgefahren über 709 Linien, 118 612 Bilder mit zwei
    /// Waggons auf derselben Zelle, kleinster Bildabstand 12,0 px bei 40 px
    /// Zellbreite) — wer darunter liegt, deckt einen Waggon zu.</summary>
    private const float RailSquashPx = 10f;

    /// <summary>Der kleinste Abstand, den das Original zwischen zwei Waggons je
    /// erzeugt — <b>12 px</b>, nachgefahren über 709 Linien und 216 606 Bilder.
    /// Die Kupplung darf nicht darunter ziehen; siehe
    /// <see cref="RailCouple"/>.</summary>
    private const float RailWagonMinPx = 12f;

    /// <summary>Dasselbe für DIESES Bild statt über den ganzen Lauf, samt der
    /// Zelle, auf der es passiert. Damit kann <c>--shot-when=squash</c> auf den
    /// Augenblick warten und die Kamera hinstellen — die Stauchung dauert nur
    /// etwa eine Sekunde je Abfahrt, ein Bild auf gut Glück trifft sie in
    /// einem von zehn Fällen.</summary>
    public int RailSquashNow;
    public Vector2 RailSquashAt;
    public int RailSquashLine = -1;

    /// <summary>
    /// <c>--rail-hit-check</c> — beschiesst eine heile Gleiszelle so lange, bis
    /// sie bricht, und repariert sie danach wieder.
    ///
    /// <para>Er ÜBT den Weg aus, statt eine Zahl zu setzen: er ruft
    /// <see cref="RailHit"/> mit einem Schaden und liest danach ab, was die
    /// Zeichenliste hergibt. Was er sehen können muss: dass die Trefferpunkte
    /// wirklich fallen (und nicht in einem Schritt auf null springen), dass das
    /// Bild über 100 geht, dass der Träger VERSCHWINDET und statt dessen ein
    /// Trümmerfeld liegt, und dass die Stützenfassungen daneben neu bestimmt
    /// werden.</para></summary>
    /// <summary>Ein echtes Geschoss auf eine Zelle setzen und den Einschlag von
    /// <c>UpdateProjectiles</c> zustellen lassen — <b>der Weg, den das Spiel
    /// nimmt</b>, nicht der Aufruf der Rechnung dahinter.</summary>
    private void RailFireProbe(int col, int row, int damage)
    {
        var at = RailCellPoint(col, row);
        _shots.Add(new Projectile
        {
            Pos = at, Aim = at, Target = -1, Shooter = -1, Damage = damage,
            Facing = 0, Kind = "rocket_l", Speed = 190f,
        });
        UpdateProjectiles(1f / 60f);
    }

    /// <summary>
    /// <b>Hängt der Zug zusammen?</b> — der Abstand zwischen den GEZEICHNETEN
    /// Bildern zweier aufeinanderfolgender Waggons, in Kartenpixeln.
    ///
    /// <para>Die Frage lässt sich weder am Rückstand allein noch an der
    /// Zellenzahl beantworten: <b>Lok und Waggon sind verschieden breit</b>
    /// (Teil 57 je nach Richtung 23..41 px, Teil 58 nur 22..28), und der
    /// Rückstand ist mit 0/4/7/11 Takten ungleichmässig. Erst beides zusammen
    /// ergibt die Lücke, die man sieht — gemessen wird deshalb das, was
    /// wirklich auf dem Schirm steht.</para>
    ///
    /// <para>0 heisst: die Bilder berühren sich oder überlappen. Alles darüber
    /// ist eine sichtbare Lücke.</para></summary>
    /// <summary>Nur fuer die Fehlersuche: jede Luecke einzeln ausgeben.</summary>
    private static bool RailGapTrace;

    public float RailGapWorst;
    public int RailGapPairs, RailGapOpen;

    private readonly Dictionary<int, Rect2I> _trainBox = new();

    /// <summary>Der undurchsichtige Kasten eines Zugbildes auf seiner Leinwand —
    /// einmal aus dem Bild gelesen und behalten.</summary>
    private Rect2I TrainBox(int part, int facing)
    {
        int key = part * 8 + (facing & 7);
        if (_trainBox.TryGetValue(key, out var r)) return r;
        r = new Rect2I(0, 0, 0, 0);
        string p = Core.Content.Path($"Units/train/{part}/f{facing & 7}.png");
        if (Godot.FileAccess.FileExists(p))
        {
            var img = Image.LoadFromFile(p);
            if (img != null) r = img.GetUsedRect();
        }
        _trainBox[key] = r;
        return r;
    }

    /// <summary>Der Kasten eines Waggons in KARTENPIXELN, so wie gezeichnet.</summary>
    private Rect2 WagonRect(Wagon w)
        => WagonRectOf(w.Index, w.Piece, new Vector2(w.Col, w.Row));

    /// <summary>Dasselbe für eine Stelle, an der noch gar kein Waggon steht —
    /// die Kupplung probiert damit Plätze durch, ohne etwas zu setzen. ⚠ Diese
    /// Rechnung muss die des Zeichenpfads bleiben; sie ist die einzige Stelle,
    /// an der beide zusammenkommen.</summary>
    private Rect2 WagonRectOf(int index, int piece, Vector2 cell)
    {
        int part = index == 0 || index == 3 ? 57 : 58;
        int pc = (index == 3 ? (piece + 4) : piece) & 7;
        var box = TrainBox(part, pc);
        var at = RailPoint(cell) - ComposedAnchor + WagonOverRail;
        return new Rect2(at.X + box.Position.X, at.Y + box.Position.Y, box.Size.X, box.Size.Y);
    }

    /// <summary>
    /// <b>DIE KUPPLUNG</b> — der Rückstand darf einen Waggon nicht weiter
    /// hinten absetzen, als sein Bild lang ist.
    ///
    /// <para><b>Der Befund, der dazu geführt hat.</b> Gemessen über einen Lauf
    /// auf map_NET02 (1&nbsp;010&nbsp;650 Waggonpaare in Takten):</para>
    /// <code>
    ///   je Paar    W0-W1  92747/346027   W1-W2 158428/337749   W2-W3 86774/326874
    ///   je Stueck  f0  18576/225972   f2 145119/273690
    ///              f4  17092/225280   f6 157162/285708
    /// </code>
    /// <para>Das Muster ist eindeutig: <b>f2 und f6 sind die WAAGERECHTEN
    /// Posen</b> (Lok 41×22 bzw. 39×22 px), dort klafft es in gut der Hälfte
    /// aller Takte; f0/f4 sind die senkrechten (23×35, 23×29) und dort fast
    /// nie. Es ist also keine Ecke und kein Bildfehler, sondern schlichte
    /// Arithmetik:</para>
    /// <code>
    ///   Rueckstand 0/4/7/11 Takte = 0/0,8/1,4/2,2 Zellen
    ///   waagerecht:  eine Zelle = 40 px  ->  Abstand 32 / 24 / 32 px
    ///   Bildbreiten: Lok 41, Waggon 22, Waggon 22, Schublok 39
    ///   noetig:      30,5 / 21 / 29,5     vorhanden: 32 / 24 / 32
    ///                                     ---------------------------
    ///                                     Fehlbetrag 1,5 / 3 / 2,5 px
    ///   senkrecht:   eine Zelle = 20 px  ->  Abstand 16 / 12 / 16 px
    ///                bei 24..35 px Bildhoehe ueberlappen sie ohnehin
    /// </code>
    /// <para>Der schlimmste Einzelfall ist eine <b>Treppe</b>: auf Linie 10 der
    /// map_NET02 läuft die Kette (112,212)→(112,211)→(111,211)→(111,210), also
    /// eine isometrische Diagonale als Einzelzellen. Der vordere Waggon steht
    /// dort schon auf dem senkrechten Schenkel (schmale Pose, 23 px) und der
    /// folgende noch 32 px daneben auf dem waagerechten — <b>10,0 px Lücke</b>,
    /// die grösste des ganzen Laufs.</para>
    ///
    /// <para><b>⚠ DAS IST EINE BEWUSSTE ABWEICHUNG VOM ORIGINAL, und sie wird
    /// hier so benannt.</b> Die Rückstände 0/4/7/11 Takte sind gemessen (21632
    /// Messungen je Paar, kein Gegenbeispiel) — sie sind nicht falsch. Rechnet
    /// man sie gegen die Bildbreiten, ergibt sich, dass <b>das Original selbst
    /// diese Fugen zeigt</b>. Der Spieler hat sie zweimal gemeldet
    /// (»der zug ist immer noch nicht sauber zusammenhängend«) und die Aufgabe
    /// des Tages lautet »damit die beiden Grafiken sauber sind und hübsch
    /// aussehen«. Also wird hier zugunsten des Bildes entschieden.</para>
    ///
    /// <para><b>Der Eingriff ist der kleinstmögliche:</b> der Rückstand wird
    /// nur <b>VERKÜRZT</b>, nie verlängert. Wo die getaktete Stelle schon
    /// Berührung ergibt — auf jedem senkrechten Schenkel, an jedem Halt —
    /// bleibt sie unangetastet, dort ändert sich also gar nichts. Gezogen wird
    /// entlang der ECHTEN Kette in Kartenpixeln (<see cref="RailWalk"/>), nicht
    /// in Zellen: damit folgt der Nachläufer um Ecken herum und die Treppe
    /// reisst nicht mehr auf. Fahrt, Geschwindigkeit und Fahrzeit von Waggon 0
    /// rührt das nicht an — der Zug ist nur kürzer, nicht schneller.</para>
    /// </summary>
    private float RailCouple(List<Vector2> route, List<Wagon> list, Wagon w,
                             List<int> pcs, float leadF, int dir)
    {
        if (w.Index <= 0 || w.Hidden) return leadF;
        Wagon front = null;
        foreach (var q in list)
            if (q.Index == w.Index - 1) { front = q; break; }
        if (front == null || front.Hidden) return leadF;
        // ⚠ HIER WIRD NICHTS MEHR AUSGERECHNET, SONDERN GEMESSEN — und das ist
        // die Lehre aus DREI gescheiterten Anläufen an derselben Stelle:
        //
        //   1. halbe Bildmasse    unterstellt, der Kasten sitze mittig über dem
        //      Anker. Tut er nicht: Teil 57 Bild 0 steht auf Spalte 12..35 einer
        //      64er Leinwand, sein Mittelpunkt liegt 6,5 px links vom Anker.
        //      Blieb 8,5 px Lücke, während die Rechnung »passt« meldete.
        //   2. echte Kanten, Achse aus dem PIXELvektor. Auf einer Rampe zieht
        //      der Höhenabzug (15 px je Stufe) einen waagerechten Schritt so
        //      weit nach oben, dass er als senkrecht gilt.
        //   3. echte Kanten, Achse aus der ZELLrichtung. Scheitert an der ECKE,
        //      und die ist der eigentliche Fall: auf Linie 21 steht Waggon 0 auf
        //      (58,87) und dreht dort nach UNTEN, Waggon 1 steht 32 px RECHTS
        //      davon auf dem waagerechten Schenkel. Die beiden haben gar keine
        //      gemeinsame Achse — welche Kante zählt, ist nicht entscheidbar.
        //
        // Was in allen drei Fällen dieselbe Frage beantwortet hätte: berühren
        // sich die beiden KÄSTEN? Die stehen fertig da (WagonRect), also wird
        // genau das gefragt. Der Nachläufer wird von seinem getakteten Platz aus
        // in höchstens zehn Halbierungsschritten an die Stelle geholt, an der
        // die Berührung gerade eintritt — nie weiter als bis zum vorderen. Das
        // gilt für Gerade, Ecke, Treppe und Rampe gleichermassen, weil es über
        // keine davon eine Annahme macht.
        var rf = WagonRect(front);
        if (WagonRectAt(w.Index, route, pcs, leadF, dir).Intersects(rf, true))
            return leadF;                                  // beruehren sich schon
        // ⚠ DIE GRENZE, BIS ZU DER GEZOGEN WERDEN DARF, ist gemessen und nicht
        // gesetzt: das Original bringt zwei Waggons nie näher als 12 px
        // zusammen (709 Linien, 216606 Bilder, kleinster Abstand 12,0 px bei
        // 40 px Zellbreite). Ohne diese Grenze holt die Halbierung an einer Ecke
        // den Nachläufer beliebig weit heran — die Kästen berühren sich dann
        // zwar, aber ein Waggon deckt den anderen zu.
        float bound = RailWalk(route, front.LeadF, -dir * RailWagonMinPx);
        if (!WagonRectAt(w.Index, route, pcs, bound, dir).Intersects(rf, true))
        {
            // Selbst am Anschlag klafft es. Dann ist die Ecke schuld und nicht
            // der Rückstand; näher als 12 px wird trotzdem nicht gezogen.
            w.Need = Mathf.Abs(bound - leadF); w.Coupled = bound;
            return bound;
        }
        float lo = leadF, hi = bound;         // lo klafft, hi liegt auf
        for (int it = 0; it < 10; it++)
        {
            float mid = (lo + hi) * 0.5f;
            if (WagonRectAt(w.Index, route, pcs, mid, dir).Intersects(rf, true))
                hi = mid;
            else lo = mid;
        }
        w.Need = Mathf.Abs(hi - leadF); w.Coupled = hi;
        return hi;
    }

    /// <summary>Der Bildkasten, den Waggon <paramref name="index"/> HÄTTE, wenn
    /// er an der Kettenstelle <paramref name="leadF"/> stünde — mit der Pose,
    /// die dort gilt. Dieselbe Rechnung wie in
    /// <see cref="RailPlaceWagons"/>, nur ohne den Waggon anzufassen.</summary>
    private Rect2 WagonRectAt(int index, List<Vector2> route, List<int> pcs,
                              float leadF, int dir)
    {
        int last = route.Count - 1;
        int step = Mathf.Clamp(Mathf.FloorToInt(leadF), 0, last);
        var pt = RailPathPoint(route, leadF);
        int piece = pcs != null && step < pcs.Count
            ? (dir > 0 ? pcs[step] : (pcs[step] + 4) & 7) : 0;
        return WagonRectOf(index, piece, pt);
    }

    /// <summary>Von der Kettenstelle <paramref name="from"/> aus
    /// <paramref name="distSigned"/> KARTENPIXEL weit an der Kette
    /// entlanggehen und die erreichte Kettenstelle zurückgeben.
    ///
    /// <para>Der Weg wird über <see cref="RailPoint"/> gemessen, also mit
    /// Zellbreite 40, Zellhöhe 20 und dem Höhenabzug — ein Schritt nach rechts
    /// ist doppelt so weit wie einer nach unten, und genau daran ist die
    /// Rechnung in Zellen bisher gescheitert. Am Kettenende wird angehalten,
    /// nicht umgeschlagen.</para></summary>
    private float RailWalk(List<Vector2> route, float from, float distSigned)
    {
        int last = route.Count - 1;
        if (last <= 0) return 0f;
        float posF = Mathf.Clamp(from, 0f, last);
        float rest = Mathf.Abs(distSigned);
        int step = distSigned >= 0f ? 1 : -1;
        for (int guard = 0; guard < route.Count + 4 && rest > 0.001f; guard++)
        {
            int i = step > 0 ? Mathf.FloorToInt(posF) : Mathf.CeilToInt(posF) - 1;
            if (i < 0 || i >= last) break;
            float segLen = RailPoint(route[i]).DistanceTo(RailPoint(route[i + 1]));
            if (segLen <= 0.001f) { posF = step > 0 ? i + 1 : i; continue; }
            float t = posF - i;
            float avail = (step > 0 ? 1f - t : t) * segLen;
            if (avail >= rest) { posF += step * rest / segLen; break; }
            rest -= avail;
            posF = step > 0 ? i + 1 : i;
        }
        return Mathf.Clamp(posF, 0f, last);
    }

    /// <summary>
    /// <b>Die Lücke über den ganzen Lauf statt in einem Bild</b> — und vor allem:
    /// <b>WO</b> sie herkommt.
    ///
    /// <para><see cref="RailMeasureGaps"/> sieht nur den Augenblick, in dem die
    /// Meldung geschrieben wird. „16 von 67 Paaren, größte 8,9 px" sagt damit
    /// nicht, ob die Lücke an einem bestimmten Waggonpaar hängt, an einer
    /// bestimmten Fahrtrichtung oder überall gleichmässig auftritt — und ohne
    /// das lässt sich die Ursache nicht ansprechen.</para>
    ///
    /// <para>Deshalb wird hier <b>in jedem Takt</b> gezählt, aufgeschlüsselt nach
    /// Waggonpaar (0-1, 1-2, 2-3) und nach dem <b>Gleisstück</b>, auf dem der
    /// vordere der beiden steht. Ein Stück ist eine Richtung: 0/6/7 waagerecht,
    /// 1/8/9 senkrecht, 2..5 die Ecken. Verteilt sich die Lücke gleichmässig,
    /// ist der Rückstand zu gross; hängt sie an einzelnen Stücken, ist es die
    /// Form der Bilder.</para></summary>
    public float RailGapEverWorst;
    public string RailGapEverWhere = "";
    public readonly int[] RailGapByPair = new int[3];
    public readonly int[] RailGapSeenByPair = new int[3];
    public readonly int[] RailGapByPiece = new int[10];
    public readonly int[] RailGapSeenByPiece = new int[10];

    private void RailCensusGaps(List<Wagon> list)
    {
        for (int i = 1; i < list.Count && i <= 3; i++)
        {
            var a = list[i - 1]; var b = list[i];
            if (a.Hidden || b.Hidden) continue;
            int pc = a.Piece & 7;
            RailGapSeenByPair[i - 1]++;
            RailGapSeenByPiece[pc]++;
            var ra = WagonRect(a); var rb = WagonRect(b);
            if (ra.Intersects(rb)) continue;
            float dx = Mathf.Max(0f, Mathf.Max(rb.Position.X - (ra.Position.X + ra.Size.X),
                                               ra.Position.X - (rb.Position.X + rb.Size.X)));
            float dy = Mathf.Max(0f, Mathf.Max(rb.Position.Y - (ra.Position.Y + ra.Size.Y),
                                               ra.Position.Y - (rb.Position.Y + rb.Size.Y)));
            float gap = Mathf.Sqrt(dx * dx + dy * dy);
            if (gap <= 0.5f) continue;
            RailGapByPair[i - 1]++;
            RailGapByPiece[pc]++;
            if (gap > RailGapEverWorst)
            {
                RailGapEverWorst = gap;
                RailGapEverWhere =
                    $"Linie {a.Line} W{a.Index}(Teil {(a.Index is 0 or 3 ? 57 : 58)} " +
                    $"Bild {a.Piece}, {ra.Size.X:0}x{ra.Size.Y:0}) bei ({a.Col:0.0},{a.Row:0.0}) " +
                    $"-> W{b.Index}(Teil {(b.Index is 0 or 3 ? 57 : 58)} Bild {b.Piece}, " +
                    $"{rb.Size.X:0}x{rb.Size.Y:0}) bei ({b.Col:0.0},{b.Row:0.0})" +
                    $" [Kette {a.LeadF:0.000} -> {b.LeadF:0.000}, noetig {b.Need:0.0} px, " +
                    $"gekuppelt {b.Coupled:0.000}]";
            }
        }
    }

    /// <summary>Die Aufschlüsselung als Text — je Waggonpaar und je Gleisstück,
    /// jeweils „mit Lücke von gesehen".</summary>
    public string RailGapReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(" | Luecke je Paar:");
        for (int i = 0; i < 3; i++)
            sb.Append($" W{i}-W{i + 1} {RailGapByPair[i]}/{RailGapSeenByPair[i]}");
        sb.Append("  je Stueck:");
        for (int p = 0; p < 8; p++)
            if (RailGapSeenByPiece[p] > 0)
                sb.Append($" f{p} {RailGapByPiece[p]}/{RailGapSeenByPiece[p]}");
        if (RailGapEverWorst > 0f)
            sb.Append($"  schlimmste ueberhaupt {RailGapEverWorst:0.0} px: {RailGapEverWhere}");
        return sb.ToString();
    }

    private void RailMeasureGaps()
    {
        RailGapWorst = 0f; RailGapPairs = 0; RailGapOpen = 0;
        foreach (var kv in _freightWagons)
        {
            var list = kv.Value;
            if (list.Count < 2) continue;
            for (int i = 1; i < list.Count; i++)
            {
                var a = list[i - 1]; var b = list[i];
                if (a.Hidden || b.Hidden) continue;
                var ra = WagonRect(a); var rb = WagonRect(b);
                RailGapPairs++;
                if (ra.Intersects(rb)) continue;               // beruehren sich
                float dx = Mathf.Max(0f, Mathf.Max(rb.Position.X - (ra.Position.X + ra.Size.X),
                                                   ra.Position.X - (rb.Position.X + rb.Size.X)));
                float dy = Mathf.Max(0f, Mathf.Max(rb.Position.Y - (ra.Position.Y + ra.Size.Y),
                                                   ra.Position.Y - (rb.Position.Y + rb.Size.Y)));
                float gap = Mathf.Sqrt(dx * dx + dy * dy);
                if (gap > 0.5f) RailGapOpen++;
                if (gap > RailGapWorst) RailGapWorst = gap;
                if (RailGapTrace && gap > 0.5f)
                    GD.Print($"[luecke] {gap:0.0} px | Linie {a.Line} " +
                             $"W{a.Index}(Teil {(a.Index is 0 or 3 ? 57 : 58)} Bild {a.Piece}) " +
                             $"bei ({a.Col:0.0},{a.Row:0.0}) Breite {ra.Size.X:0}x{ra.Size.Y:0}  ->  " +
                             $"W{b.Index}(Teil {(b.Index is 0 or 3 ? 57 : 58)} Bild {b.Piece}) " +
                             $"bei ({b.Col:0.0},{b.Row:0.0}) Breite {rb.Size.X:0}x{rb.Size.Y:0}");
            }
        }
    }

    /// <summary>
    /// <b>Schliessen die Ecken?</b> — der Abstand zwischen den beiden
    /// Anschlusspunkten zweier benachbarter Gleisstücke, in Kartenpixeln.
    ///
    /// <para>An den Bildern gemessen (Teil 64, Leinwand 64×56): jedes Stück
    /// trägt seine Anschlüsse an festen Stellen — links auf Spalte 10, rechts
    /// auf 49, oben auf Zeile 20, unten auf 41. Die HÖHE dort ist je Bild
    /// verschieden:</para>
    /// <code>
    ///   f0  links 31,0  rechts 31,0      f4  links 31,7  unten 27,1
    ///   f1  oben  29,5  unten  29,5      f5  links 30,3  oben  27,9
    ///   f2  rechts 31,7 unten  31,9      f6  links 16,3  rechts 30,3  RAMPE
    ///   f3  rechts 30,3 oben   31,1      f7  links 30,7  rechts 16,3  RAMPE
    /// </code>
    /// <para>Die Rampen tragen also <b>14 px</b> Höhenunterschied im Bild — und
    /// unser <see cref="RailPoint"/> zieht zusätzlich <c>Höhe·15</c> ab. Ob
    /// beides zusammengeht, entscheidet, ob die Strecke an einer Geländestufe
    /// schliesst; genau das misst dieser Zähler. Er ist der erste, der die
    /// gemeldeten »Ecken und Knicke schliessen nicht« überhaupt sehen
    /// kann.</para></summary>
    public float RailJointWorst;
    public int RailJointPairs, RailJointOpen;
    public string RailJointWorstWhere = "";

    /// <summary>Je Bild: Höhe des linken und des rechten Anschlusses, Spalte des
    /// oberen und des unteren. <c>NaN</c> heisst: diese Seite ist nicht
    /// angeschlossen.</summary>
    /// <remarks>⚠ Der Anschluss braucht <b>x UND y</b>. Der erste Anlauf hielt
    /// die Zeile für fest (20 oben, 41 unten) und speicherte nur die Spalte —
    /// das stimmt für f1, aber nicht für die senkrechten Rampen: <b>f8 reicht
    /// von Zeile 5 bis 41</b> (oben 15 px höher), <b>f9 nur von 19 bis 26</b>
    /// (unten 15 px höher). Mit der falschen Tabelle meldete der Zähler an
    /// jeder senkrechten Rampe eine Kerbe, die es nicht gibt.</remarks>
    private static readonly float[,] RailPortAt =
    {
        // links x,y   rechts x,y   oben x,y     unten x,y      (NaN = keine Seite)
        { 10f, 31.0f, 49f, 31.0f, float.NaN, 0f, float.NaN, 0f },        // f0 L-R
        { float.NaN, 0f, float.NaN, 0f, 29.5f, 20f, 29.5f, 41f },        // f1 T-B
        { float.NaN, 0f, 49f, 31.7f, float.NaN, 0f, 31.9f, 41f },        // f2 R-B
        { float.NaN, 0f, 49f, 30.3f, 31.1f, 20f, float.NaN, 0f },        // f3 R-T
        { 10f, 31.7f, float.NaN, 0f, float.NaN, 0f, 27.1f, 41f },        // f4 L-B
        { 10f, 30.3f, float.NaN, 0f, 27.9f, 20f, float.NaN, 0f },        // f5 L-T
        { 10f, 16.3f, 49f, 30.3f, float.NaN, 0f, float.NaN, 0f },        // f6 Rampe, links hoeher
        { 10f, 30.7f, 49f, 16.3f, float.NaN, 0f, float.NaN, 0f },        // f7 Rampe, rechts hoeher
        { float.NaN, 0f, float.NaN, 0f, 29.5f, 5f, 29.5f, 41f },         // f8 Rampe, oben hoeher
        { float.NaN, 0f, float.NaN, 0f, 29.5f, 20f, 29.5f, 26f },        // f9 Rampe, unten hoeher
    };

    /// <summary>
    /// <b>DER WEG DER WAGGONS — die Randmitten der GEZEICHNETEN Schiene statt
    /// der Zellmitten.</b>
    ///
    /// <para>Gemeldet: »wenn eine Bahnlinie diagonal geradlinig verläuft, macht
    /// die Bahn trotzdem Zicke Zacke beim Fahren«. Das ist genau richtig
    /// beobachtet, und es liegt nicht am Zug, sondern am WEG.</para>
    ///
    /// <para>Eine isometrische Diagonale steht in sec22 als <b>Treppe aus
    /// Einzelzellen</b>: …(32,59) (31,59) (31,60) (30,60)… Die Gleisgrafik macht
    /// daraus eine glatte Schräge — nachgesehen mit
    /// <c>aekernel-tools/rail_joint_look.py</c>, die Eckstücke f2/f5 bilden eine
    /// durchgehende Linie. Der Zug fuhr aber auf den ZELLMITTEN, und die sind
    /// die Treppe selbst: 40 px nach links, 20 px nach unten, 40 nach links,
    /// 20 nach unten. Also Zickzack mit rund 20 px Ausschlag, obwohl die
    /// Schiene darunter schnurgerade liegt.</para>
    ///
    /// <para><b>Die Randmitten stehen in der Kunst, nicht in einer Annahme.</b>
    /// <see cref="RailPortAt"/> ist an den Bildern gemessen: das Stück f2
    /// verlässt seine Zelle unten auf Spalte 31,9, f4 auf 27,1, das gerade
    /// Stück f1 auf 29,5 — symmetrisch um die Mitte. Genau dieser seitliche
    /// Versatz macht aus der Treppe eine Schräge, und er ist es, den der Zug
    /// mitnehmen muss.</para>
    ///
    /// <para>Der Knoten für Zelle <c>i</c> ist deshalb die Stelle, an der die
    /// Schiene in sie HEREINkommt: auf der Seite links/rechts die halbe Zelle
    /// zur Seite, auf oben/unten die halbe Zelle hoch oder tief PLUS den
    /// gemessenen seitlichen Versatz.</para>
    ///
    /// <para>⚠ <b>Vom Anschlusspunkt zählt nur das x.</b> Sein y trägt bei den
    /// RAMPEN die Deckhöhe mit (f6 links 16,3 statt 30,3 — das sind die 14 px
    /// einer Geländestufe, f8 oben sogar 5 statt 20). Die Höhe rechnet
    /// <see cref="RailPoint"/> aber selbst über <c>ElevOf</c>; sie hier ein
    /// zweites Mal einzurechnen würde den Zug an jeder Rampe um eine
    /// Dreiviertelzelle nach oben werfen. Die Zeile ist deshalb glatt ±0,5.</para>
    /// </summary>
    private List<Vector2> RailDrawnPath(List<Vector2> cells, List<int> frames)
    {
        var path = new List<Vector2>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            int f = i < frames?.Count ? frames[i] % 10 : -1;
            // Die Seite, auf der die Schiene in diese Zelle hereinkommt. Bei der
            // ersten Zelle gibt es keinen Vorgaenger — dort die Gegenseite des
            // Ausgangs, also das freie Ende der Linie.
            int side;
            if (i > 0) side = RailPortTo(cells[i], cells[i - 1]);
            else
            {
                // Erste Zelle: die Gegenseite des Ausgangs, also das freie Ende.
                // ⚠ Nur wenn es einen Ausgang GIBT — RailOppositePort(-1) liefert
                // sonst stillschweigend „oben" und legt den Startknoten auf eine
                // Kante, die es nicht gibt.
                int outp = cells.Count > 1 ? RailPortTo(cells[0], cells[1]) : -1;
                side = outp < 0 ? -1 : RailOppositePort(outp);
            }
            path.Add(RailEdgePoint(cells[i], f, side));
        }
        return path;
    }

    /// <summary>Die Randmitte einer Zellseite als Bruchzahl IN ZELLEN, mit dem
    /// seitlichen Versatz, den das Gleisbild dort wirklich hat.</summary>
    /// <summary>Wie oft ein Knoten auf die ZELLMITTE zurückfallen musste, weil
    /// es gar keine Nachbarzelle gibt — dort liegt die Kette nicht Kante an
    /// Kante, sie springt. Das ist kein Fehler des Weges, sondern der bekannte
    /// Punkt »Liniennummer 0 sammelt Fremdzellen« (25 von 1193 auf map_NET02),
    /// und er ist es, der die letzten Ausreisser im Weg je Takt erzeugt.</summary>
    public int RailPathNodes, RailPathFallback;

    private Vector2 RailEdgePoint(Vector2 cell, int frame, int side)
    {
        RailPathNodes++;
        // Ohne Nachbarzelle gibt es keine Seite — nur dann bleibt die Zellmitte.
        // Das passiert an einer Kette, die nicht Kante an Kante liegt.
        if (side < 0) { RailPathFallback++; return cell; }
        float px = frame is >= 0 and <= 9 ? RailPortAt[frame, side * 2] : float.NaN;
        // ⚠ Bedient das Bild diese Seite nicht, bleibt der Knoten trotzdem auf
        // der KANTENMITTE — nur ohne den gemessenen seitlichen Versatz. Der
        // erste Anlauf hat hier die ZELLMITTE genommen, und das ist eine halbe
        // Zelle daneben: gemessen 25 von 1193 Knoten, jeder ein Knick von bis zu
        // 20 px, den die Schiene nicht hat (schlimmster Sprung 12,65 px in einem
        // Takt). Vorkommen tut es an Kreuzungszellen, wo die Karte zwei
        // Gleissaetze trägt und wir einen davon in die Kette nehmen, und an
        // Linienenden, deren Bild die freie Seite nicht kennt.
        float lat = float.IsNaN(px) ? 0f : (px - 29.5f) / TileW;
        if (float.IsNaN(px)) RailPathFallback++;
        // ⚠ 0,49 und nicht 0,5. Auf genau der halben Zelle liegt die
        // Rundungsgrenze von RailPoint, das die Geländehöhe über
        // `ElevOf(round(spalte), round(zeile))` liest — ein Knoten dort erwischt
        // die Höhe der NACHBARzelle, und an jeder Geländestufe sprang der Zug
        // deshalb um 15 px (gemessen 12,09 px in einem Takt bei 2 px Soll,
        // Richtungswechsel bis 180°). Ein Hundertstel Zelle nach innen ist
        // 0,4 px im Bild und macht die Rundung eindeutig.
        const float H = 0.49f;
        return side switch
        {
            PortL => new Vector2(cell.X - H, cell.Y),
            PortR => new Vector2(cell.X + H, cell.Y),
            PortT => new Vector2(cell.X + lat, cell.Y - H),
            _     => new Vector2(cell.X + lat, cell.Y + H),
        };
    }

    /// <summary>
    /// Der Weg einer Linie samt <b>aufsummierter Länge in Kartenpixeln</b>.
    ///
    /// <para>⚠ Die Längen sind der Grund, warum das hier zusammen liegt. Der
    /// Fortschritt einer Fahrt lief bis zum 15.08.2026 in SCHRITTEN: <c>p</c>
    /// mal Anzahl der Kettenglieder. Das ging, solange ein Glied entweder 40 px
    /// (waagerecht) oder 20 px (senkrecht) lang war — schon da fuhr der Zug auf
    /// senkrechten Schenkeln halb so schnell, gemessen 122 gegen 31 px/s. Mit
    /// den Randmitten sind die Glieder 20..45 px lang, und dann rutscht es
    /// sichtbar: gemessen 14,27 px in einem Takt gegen 3,66 vorher, und drei
    /// Waggons stauchten sich an den kurzen Gliedern zusammen.</para>
    ///
    /// <para>Deshalb läuft der Fortschritt jetzt über die BOGENLÄNGE. Das ist
    /// zugleich das treue Modell: das Original zieht je Takt einen festen Betrag
    /// ab (8 px, sec44 +0x0c), also fährt es mit gleicher Geschwindigkeit,
    /// gleich in welche Richtung.</para></summary>
    private sealed class RailPathData
    {
        public List<Vector2> Pts;
        public float[] Cum;                 // Cum[i] = Länge bis Knoten i
        public float Len => Cum.Length > 0 ? Cum[^1] : 0f;
    }

    private readonly Dictionary<int, RailPathData> _linePath = new();

    private RailPathData RailPathOf(int line)
    {
        if (_linePath.TryGetValue(line, out var got)) return got;
        if (!_lineCell.TryGetValue(line, out var cells) || cells.Count == 0) return null;
        _lineCellFrame.TryGetValue(line, out var frames);
        // --rail-lay=mitten: die Gegenprobe faehrt wieder auf den Zellmitten.
        var pts = RailProbeCellCentres ? new List<Vector2>(cells)
                                       : RailDrawnPath(cells, frames);
        var cum = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            cum[i] = cum[i - 1] + RailPoint(pts[i - 1]).DistanceTo(RailPoint(pts[i]));
        got = new RailPathData { Pts = pts, Cum = cum };
        _linePath[line] = got;
        return got;
    }

    /// <summary>Die Stelle <paramref name="leadF"/> auf dem Weg, als
    /// Zellkoordinate.
    ///
    /// <para>⚠ HIER STAND EINE HÖHENGLÄTTUNG, und sie war falsch begründet: sie
    /// nahm die Höhe der beiden KNOTEN, und die liegen auf Zellgrenzen — dort
    /// trifft die Rundung mal die eigene, mal die Nachbarzelle. Gemessen wurde
    /// es dadurch schlechter (904 px/s Spitze statt 103, mittlerer
    /// Richtungswechsel 2,5° statt 0,7°). Gelöst ist es statt dessen an der
    /// Wurzel: die Knoten sitzen auf <c>±0,49</c> statt <c>±0,5</c> und rundon
    /// damit eindeutig auf ihre eigene Zelle (siehe
    /// <see cref="RailEdgePoint"/>).</para></summary>
    private Vector2 RailPathPoint(List<Vector2> route, float leadF)
    {
        int last = route.Count - 1;
        int step = Mathf.Clamp(Mathf.FloorToInt(leadF), 0, last);
        int nxt = Mathf.Clamp(step + 1, 0, last);
        float frac = leadF - step;
        return route[step].Lerp(route[nxt], frac);
    }

    /// <summary>Bogenlänge → Kettenstelle als Bruchzahl. Binäre Suche, damit es
    /// je Waggon und Takt nicht über den ganzen Weg läuft.</summary>
    private static float RailArcToIndex(float[] cum, float s)
    {
        int last = cum.Length - 1;
        if (last <= 0) return 0f;
        s = Mathf.Clamp(s, 0f, cum[last]);
        int lo = 0, hi = last;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (cum[mid] <= s) lo = mid; else hi = mid;
        }
        float seg = cum[hi] - cum[lo];
        return seg <= 0.0001f ? lo : lo + (s - cum[lo]) / seg;
    }

    /// <summary>Kettenstelle → Bogenlänge, die Umkehrung.</summary>
    private static float RailIndexToArc(float[] cum, float leadF)
    {
        int last = cum.Length - 1;
        if (last <= 0) return 0f;
        leadF = Mathf.Clamp(leadF, 0f, last);
        int i = Mathf.Min(Mathf.FloorToInt(leadF), last - 1);
        return cum[i] + (leadF - i) * (cum[i + 1] - cum[i]);
    }

    /// <summary>Der Anschlusspunkt eines Stücks in Kartenpixeln, oder
    /// <c>null</c>, wenn das Bild diese Seite gar nicht bedient. Seite: 0 links,
    /// 1 rechts, 2 oben, 3 unten.</summary>
    private Vector2? RailPort(int col, int row, int frame, int side)
    {
        if (frame is < 0 or > 9) return null;
        float px = RailPortAt[frame, side * 2], py = RailPortAt[frame, side * 2 + 1];
        if (float.IsNaN(px)) return null;
        var at = RailPoint(new Vector2(col, row)) - ComposedAnchor + new Vector2(0, RailDeckOffset);
        return at + new Vector2(px, py);
    }

    /// <summary>Die Summe der MITTENVERSÄTZE — nur noch als Beiwerk. Sie war
    /// bis zum 15.08.2026 die Zahl, die »Ecken schliessen nicht« belegen
    /// sollte; sie belegt es nicht (siehe <see cref="RailMeasureJoints"/>). Als
    /// Mittelwert sagt sie, wie schräg die Kunst ihre Eckstücke anlegt.</summary>
    public float RailPortOffSum;

    /// <summary>Die undurchsichtigen Punkte eines Gleisbildes, einmal gelesen —
    /// Leinwand 64×56, <c>true</c> heisst sichtbar.</summary>
    private readonly Dictionary<int, bool[]> _railMask = new();
    private const int RailMaskW = 64, RailMaskH = 56;

    private bool[] RailMask(int frame)
    {
        if (_railMask.TryGetValue(frame, out var m)) return m;
        m = new bool[RailMaskW * RailMaskH];
        string p = Core.Content.Path($"Units/train/rail64/f{frame}.png");
        if (Godot.FileAccess.FileExists(p))
        {
            var img = Image.LoadFromFile(p);
            if (img != null)
                for (int y = 0; y < Mathf.Min(RailMaskH, img.GetHeight()); y++)
                    for (int x = 0; x < Mathf.Min(RailMaskW, img.GetWidth()); x++)
                        m[y * RailMaskW + x] = img.GetPixel(x, y).A > 0f;
        }
        _railMask[frame] = m;
        return m;
    }

    /// <summary>
    /// <b>Klafft zwischen zwei benachbarten Gleisstücken ein Loch?</b> Gemessen
    /// an den Bildpunkten, nicht an einem Modell: der kleinste Abstand zwischen
    /// einem sichtbaren Punkt des einen und einem des anderen Bildes, in
    /// Kartenpixeln. <c>0</c> heisst überlappend, <c>1</c> Kante an Kante, alles
    /// darüber ist ein sichtbares Loch dieser Breite.
    ///
    /// <para>Die zweite Leinwand liegt gegen die erste um
    /// <c>(spaltenversatz·40, zeilenversatz·20 − höhenstufen·15)</c> verschoben
    /// — dieselbe Rechnung, mit der gezeichnet wird. Das Ergebnis hängt nur an
    /// (Bild, Seite, Bild, Höhenstufe) und wird deshalb behalten; auf einer
    /// Karte kommen ein paar Dutzend solcher Fälle vor, nicht tausend.</para>
    /// </summary>
    private readonly Dictionary<(int, int, int, int), float> _jointHole = new();

    private float RailJointHole(int fa, int side, int fb, int dElev)
    {
        if (fa is < 0 or > 9 || fb is < 0 or > 9) return 0f;
        var key = (fa, side, fb, dElev);
        if (_jointHole.TryGetValue(key, out float got)) return got;
        int dc = side switch { 0 => -1, 1 => 1, _ => 0 };
        int dr = side switch { 2 => -1, 3 => 1, _ => 0 };
        int ox = dc * TileW, oy = dr * TileH - dElev * 15;
        var ma = RailMask(fa); var mb = RailMask(fb);
        float best = float.MaxValue;
        for (int ay = 0; ay < RailMaskH && best > 0f; ay++)
            for (int ax = 0; ax < RailMaskW; ax++)
            {
                if (!ma[ay * RailMaskW + ax]) continue;
                for (int by = 0; by < RailMaskH; by++)
                    for (int bx = 0; bx < RailMaskW; bx++)
                    {
                        if (!mb[by * RailMaskW + bx]) continue;
                        float ddx = bx + ox - ax, ddy = by + oy - ay;
                        float d = Mathf.Max(Mathf.Abs(ddx), Mathf.Abs(ddy));
                        if (d < best) { best = d; if (best <= 0f) break; }
                    }
                if (best <= 0f) break;
            }
        if (best == float.MaxValue) best = 0f;      // ein Bild ist leer
        _jointHole[key] = best;
        return best;
    }

    private void RailMeasureJoints()
    {
        RailJointWorst = 0f; RailJointPairs = 0; RailJointOpen = 0;
        RailJointWorstWhere = ""; RailPortOffSum = 0f;
        var frameAt = new Dictionary<(int, int), int>();
        foreach (var c in _railCells) if (!c.Broken) frameAt[(c.Col, c.Row)] = c.Base;

        foreach (var kv in _lineCell)
        {
            var cells = kv.Value;
            for (int i = 1; i < cells.Count; i++)
            {
                int ac = Mathf.RoundToInt(cells[i - 1].X), ar = Mathf.RoundToInt(cells[i - 1].Y);
                int bc = Mathf.RoundToInt(cells[i].X), br = Mathf.RoundToInt(cells[i].Y);
                if (!frameAt.TryGetValue((ac, ar), out int af) ||
                    !frameAt.TryGetValue((bc, br), out int bf)) continue;
                int dc = bc - ac, dr = br - ar;
                int sideA, sideB;
                if (dc == 1 && dr == 0) { sideA = 1; sideB = 0; }
                else if (dc == -1 && dr == 0) { sideA = 0; sideB = 1; }
                else if (dc == 0 && dr == 1) { sideA = 3; sideB = 2; }
                else if (dc == 0 && dr == -1) { sideA = 2; sideB = 3; }
                else continue;
                var pa = RailPort(ac, ar, af, sideA);
                var pb = RailPort(bc, br, bf, sideB);
                if (pa == null || pb == null) continue;
                RailJointPairs++;
                RailPortOffSum += (pa.Value - pb.Value).Length();
                // ⚠ 15.08.2026 — GEZAEHLT WIRD DAS LOCH, NICHT DER MITTENVERSATZ.
                //
                // Hier stand `d = |pa - pb|` gegen eine Schwelle von 2 px, und die
                // Zahl (204 von 1119, schlimmster 4,1 px) hat als »Ecken und
                // Knicke schliessen nicht« gelesen. Sie misst das aber nicht.
                // aekernel-tools/rail_joint_look.py hat die 438 schlimmsten
                // Stoesse aufgezeichnet und ANGESEHEN: alle 4,1-px-Faelle sind
                // f2->f5 bzw. f4->f3, und was sie im Bild bilden, ist eine
                // GLATTE DURCHGEHENDE DIAGONALE. Keine Kerbe, kein Loch.
                //
                // Der Versatz ist gewollt: ein Eckstueck verlaesst seine Zelle
                // nicht in der Mitte, sondern um 2,4 px zur Seite, damit die
                // Treppe aus Einzelzellen als Schraege liest. Die Zahlen zeigen
                // das von selbst -- f4 unten 27,1 und f2 unten 31,9 liegen
                // symmetrisch um f1 unten 29,5, und dasselbe oben mit f5 27,9
                // und f3 31,1. Es sind ausschliesslich die Seiten OBEN und
                // UNTEN betroffen, links/rechts nie.
                //
                // Die Frage, die zaehlt, ist: klafft zwischen den beiden
                // Schienen ein LOCH? Das entscheiden die Bildpunkte, und die
                // stehen fertig da.
                float d = RailJointHole(af, sideA, bf, ElevOf(bc, br) - ElevOf(ac, ar));
                if (d > 1f) RailJointOpen++;
                if (d > RailJointWorst)
                {
                    RailJointWorst = d;
                    RailJointWorstWhere = $"({ac},{ar}) f{af} -> ({bc},{br}) f{bf}";
                }
            }
        }
    }

    /// <summary>Die Verteilung der vier Stützenfassungen, als Text.</summary>
    private string RailPylonKindsText()
    {
        var k = new int[4];
        foreach (var c in RailCellFrames()) if (c.P && c.K is >= 0 and <= 3) k[c.K]++;
        return $"65:{k[0]} 66:{k[1]} 67:{k[2]} 68:{k[3]}";
    }

    public string RailHitCheck()
    {
        var sb = new System.Text.StringBuilder();
        // ⚠ KEINE MASTZELLE nehmen: ein Platz mit index%6==0 laesst sich gar
        // nicht brechen, weil der Stuetzendurchgang `bild := 20*k + bild%10`
        // zurueckschreibt und ihn damit sofort unter 100 drueckt. Der erste
        // Anlauf hatte genau so eine erwischt (Bild 20) und meldete nach zwanzig
        // Treffern "zerschossen=False" — richtig, aber blind fuer alles andere.
        RailCell? pick = null;
        foreach (var c in _railCells)
            if (!c.Broken && !c.Pylon && c.Frame != 255 && c.Hp > 0) { pick = c; break; }
        if (pick == null) return "rail-hit-check: keine heile Gleiszelle ohne Mast auf dieser Karte";

        int col = pick.Col, row = pick.Row;
        int tiles0 = RailTiles().Count, deb0 = RailBrokenDrawn;
        string pyl0 = RailPylonKindsText();
        sb.Append($"rail-hit-check: Zelle ({col},{row}) Bild {pick.Frame} " +
                  $"Trefferpunkte {pick.Hp}\n");
        // 0) ⚠ ZUERST der WEG DES SPIELS, nicht die Mechanik: ein echtes
        //    Geschoss auf die Zelle setzen und den Einschlag von
        //    UpdateProjectiles zustellen lassen. Ein Pruefstand, der nur
        //    RailHit ruft, prueft die Rechnung und nicht, ob der Einschlag sie
        //    ueberhaupt erreicht — dieselbe Falle wie am 10.08. bei der
        //    Produktion.
        int hpBefore = pick.Hp;
        RailFireProbe(col, row, 10);
        sb.Append($"  Weg des Spiels: ein Geschoss (Schaden 10) auf die Zelle -> " +
                  $"Trefferpunkte {hpBefore} -> {pick.Hp}" +
                  (pick.Hp == hpBefore - 10 ? "  der Einschlag kommt an"
                                            : "  ⚠ der Einschlag erreicht das Gleis NICHT") + "\n");

        // 1) ein Treffer, der NICHT reicht
        int dmg = Mathf.Max(1, pick.Hp / 3);
        bool broke = RailHit(col, row, dmg);
        sb.Append($"  Schaden {dmg}: gebrochen={broke}, Trefferpunkte jetzt {pick.Hp}" +
                  (broke ? "  ⚠ zu frueh gebrochen" : "  richtig") + "\n");
        // 2) so lange weiter, bis sie bricht
        int shots = 1;
        while (!pick.Broken && shots < 20) { RailHit(col, row, dmg); shots++; }
        int tiles1 = RailTiles().Count, deb1 = RailBrokenDrawn;
        sb.Append($"  nach {shots} Treffern: Bild {pick.Frame} " +
                  $"(Variante {pick.BrokenVariant}), zerschossen={pick.Broken}\n");
        sb.Append($"  Zeichenliste: {tiles0} -> {tiles1} Stuecke, " +
                  $"Truemmer {deb0} -> {deb1}" +
                  // mehr als eines ist RICHTIG: der Bruch reisst die Nachbarn mit
                  (deb1 > deb0 ? "  richtig" : "  ⚠ kein Truemmerfeld dazu") + "\n");
        // Die Stuetzenfassung sagt, ob die Strecke ueber der Stuetze weiterlaeuft
        // — ein Bruch daneben MUSS sie aendern (rail_pylon_pass @0x4B0350 laeuft
        // im Original nach jedem Treffer).
        string pyl1 = RailPylonKindsText();
        sb.Append($"  Stuetzenfassungen: {pyl0} -> {pyl1}" +
                  (pyl0 == pyl1 ? "  ⚠ unveraendert — lief der Stuetzendurchgang?"
                                : "  neu bestimmt") + "\n");
        // 2b) die drei Dinge, die der Bruch AUSSER dem eigenen Bild anrichtet
        // ⚠ Ein Nachbar, der ein MASTPLATZ ist, kann nicht kaputt bleiben — der
        // Stuetzendurchgang heilt ihn im selben Atemzug. Erwartet werden darum
        // nur die Nachbarn OHNE Mast; der erste Anlauf zaehlte alle und meldete
        // "1 von 3", obwohl die Mechanik stimmte.
        int nb = 0, nbHas = 0, nbMast = 0;
        foreach (var (dc, dr) in RailBreakSpread)
            foreach (var c in _railCells)
            {
                if (c.Col != col + dc || c.Row != row + dr || c.Frame == 255) continue;
                if (c.Pylon) nbMast++; else { nbHas++; if (c.Broken) nb++; }
                break;
            }
        sb.Append($"  Nachbarn mitgerissen: {nb} von {nbHas} ohne Mast " +
                  $"({nbMast} Mastplaetze dabei, die heilen sofort)" +
                  (nb == nbHas ? "  richtig (@0x504428)" : "  ⚠ nicht alle") + "\n");
        int faze = -1;
        foreach (var l in _railLines) if (l.Slot == pick.Line) faze = l.Faze;
        sb.Append($"  Linie {pick.Line}: faze {faze}" +
                  (faze == 3 ? "  stillgelegt (@0x4B06F9)" : "  ⚠ nicht stillgelegt") + "\n");
        int mast = 0, mastBroken = 0;
        foreach (var c in _railCells)
            if (c.Pylon) { mast++; if (c.Broken) mastBroken++; }
        sb.Append($"  Mastplaetze: {mast}, davon zerschossen {mastBroken}" +
                  (mastBroken == 0 ? "  richtig — der Stuetzendurchgang heilt sie (4.DM: 0 von 49)"
                                   : "  ⚠ ein Mastplatz bleibt kaputt") + "\n");

        // 3) reparieren
        int brokenBefore = RailBrokenOnLine(pick.Line);
        bool healed = RailRepair(col, row);
        int tiles2 = RailTiles().Count, deb2 = RailBrokenDrawn;
        sb.Append($"  repariert={healed}: Bild {pick.Frame}, Trefferpunkte {pick.Hp} " +
                  "(bleiben absichtlich niedrig, @0x4099B6 setzt sie nicht zurueck)\n");
        sb.Append($"  Zeichenliste: {tiles2} Stuecke, Truemmer {deb2}" +
                  $"  (auf Linie {pick.Line} noch {RailBrokenOnLine(pick.Line)} von " +
                  $"{brokenBefore} kaputt)\n");
        int faze2 = -1;
        foreach (var l in _railLines) if (l.Slot == pick.Line) faze2 = l.Faze;
        sb.Append($"  Linie {pick.Line}: faze {faze2}" +
                  (RailBrokenOnLine(pick.Line) > 0
                       ? (faze2 == 3 ? "  bleibt still, es ist noch etwas kaputt"
                                     : "  ⚠ laeuft an, obwohl noch etwas kaputt ist")
                       : (faze2 == 0 ? "  laeuft wieder an (@0x409AB5)"
                                     : "  ⚠ bleibt still, obwohl alles heil ist")));
        return sb.ToString();
    }

    /// <summary>
    /// <b>Ein Einschlag auf eine Gleiszelle.</b> Gibt zurück, ob dabei ein
    /// Stück zerbrochen ist.
    ///
    /// <para>Die Einschlagsroutine des Originals @0x40D799 läuft über alle 3000
    /// Plätze, vergleicht Spalte und Zeile mit <c>[0x53C930]</c>/<c>[0x53C934]</c>
    /// — also mit GENAU der getroffenen Zelle, kein Umkreis — und hält die
    /// Trefferpunkte gegen den Schaden: reichen sie nicht, ruft sie
    /// <c>rail_hit(platz, 1)</c> @0x40D7E2, sonst <c>tp -= schaden</c>
    /// @0x40D7EC.</para>
    ///
    /// <para><c>rail_hit</c> @0x4B0460 rechnet dann <c>bild += (10 + zufall&amp;1)·10</c>
    /// — daher die beiden Trümmervarianten — und lässt anschliessend
    /// <see cref="RailPylonPass"/> laufen, weil sich damit ändert, ob die
    /// Strecke über einer Stütze weitergeht.</para>
    ///
    /// <para>⚠ <b>Eine ganze Linie fällt nie aus</b>, nur die getroffene Zelle.
    /// Und die Trefferpunkte werden beim Reparieren NICHT zurückgesetzt
    /// (@0x4099B6 fasst nur das Bild an) — ein einmal beschädigtes Gleis bleibt
    /// also weicher. Das ist gelesen, nicht gedeutet.</para>
    ///
    /// <para>⚠ UNSERE LÜCKE: das Original wirft bei <c>rail_hit</c> Rauch und
    /// Splitter. Wir zeigen nur die Explosion, die der Einschlag ohnehin
    /// erzeugt — der eigene Effekt ist nicht nachgebaut.</para></summary>
    public bool RailHit(int col, int row, int damage) => RailHit(col, row, damage, true);

    /// <summary>Die vier orthogonalen Nachbarn, die ein Bruch mitreisst —
    /// Tabelle @0x504428: (0,−1), (−1,0), (1,0), (0,1).</summary>
    private static readonly (int C, int R)[] RailBreakSpread =
        { (0, -1), (-1, 0), (1, 0), (0, 1) };

    private bool RailHit(int col, int row, int damage, bool spread)
    {
        bool broke = false;
        foreach (var c in _railCells)
        {
            if (c.Col != col || c.Row != row || c.Frame == 255 || c.Broken) continue;
            if (c.Hp > damage) { c.Hp -= damage; continue; }
            // ⚠ Die Trefferpunkte werden hier NICHT auf null gesetzt. Der erste
            // Anlauf tat das und war erfunden: in 4.DM tragen die zerschossenen
            // Zellen Trefferpunkte 1..135, das Original laesst +0x03 beim Bruch
            // also stehen (@0x40D7E2 ruft nur rail_hit, @0x40D7EC ist der
            // andere Zweig). Zusammen damit, dass auch die Reparatur sie nicht
            // zuruecksetzt, bleibt ein einmal beschaedigtes Gleis dauerhaft
            // weicher — und genau das steht in den Spielstaenden.
            c.Frame += (10 + (int)(GD.Randi() & 1)) * 10;
            broke = true;
            if (!spread) continue;
            // ⚠ 15.08.2026 — EIN BRUCH REISST DIE VIER NACHBARN MIT.
            // rail_hit @0x4B0460 geht die Tabelle @0x504428 ab und ruft sich
            // fuer jede Nachbarzelle mit Gleis selbst auf — aber mit
            // Zweitargument 0 (@0x4B06B0). Das ist die REKURSIONSBREMSE: der
            // Bruch springt genau eine Zelle weit, nicht die Strecke entlang,
            // und der mitgerissene Nachbar loest weder Stuetzendurchgang noch
            // faze-Wechsel aus.
            foreach (var (dc, dr) in RailBreakSpread)
                RailHit(col + dc, row + dr, int.MaxValue, false);
            // Und die LINIE wird stillgelegt: faze := 3 (@0x4B06F9). spoj_tick
            // @0x4C7840 faellt bei 3 durch alle Zweige und zaehlt sie nicht
            // hoch -- es faehrt kein neuer Zug los, bis repariert ist.
            foreach (var l in _railLines) if (l.Slot == c.Line) l.Faze = 3;
        }
        if (broke && spread) { RailPylonPass(); _railTiles = null; }
        return broke;
    }

    /// <summary>
    /// <b>Ein Stück Gleis wieder heil machen.</b> @0x4099E7 rechnet
    /// <c>bild := bild % 10</c> (<c>div 10</c>), spielt Effekt <c>0x2d</c> und
    /// lässt <see cref="RailPylonPass"/> laufen (@0x409A04, @0x409A0C).
    ///
    /// <para>⚠ Die TREFFERPUNKTE bleiben, wie sie sind — das Original setzt sie
    /// hier nicht zurück. Wer das ändert, erfindet.</para></summary>
    public bool RailRepair(int col, int row)
    {
        bool healed = false;
        int line = -1;
        foreach (var c in _railCells)
        {
            if (c.Col != col || c.Row != row || !c.Broken) continue;
            c.Frame %= 10;
            line = c.Line;
            healed = true;
        }
        if (!healed) return false;
        RailPylonPass();
        _railTiles = null;
        // Ist auf dieser Linie NICHTS mehr kaputt, laeuft sie wieder an:
        // @0x409AB5 setzt faze := 0, und spoj_tick macht daraus im naechsten
        // Durchgang sofort faze := 1 und startet spoj_launch.
        foreach (var c in _railCells) if (c.Line == line && c.Broken) return true;
        foreach (var l in _railLines) if (l.Slot == line && l.Faze == 3) l.Faze = 0;
        return true;
    }

    /// <summary>Wieviele Stücke dieser Linie zerschossen sind — für den
    /// Prüfstand und für die Reparaturfahrt.</summary>
    public int RailBrokenOnLine(int line)
    {
        int n = 0;
        foreach (var c in _railCells) if (c.Line == line && c.Broken) n++;
        return n;
    }

    /// <summary>
    /// <b>Die Stellen, an denen sich die Strecke ansehen lohnt</b> — je eine
    /// Zelle für Rampe, Kurve, senkrechter Lauf, Stütze, Streckenende,
    /// Linienanschluss und ein fahrender Zug.
    ///
    /// <para>Warum das hier steht statt im Prüfstand: ein Grafikfehler an der
    /// Bahn ist nur im BILD zu sehen, und jedes Bild kostete bisher einen
    /// eigenen Lauf von anderthalb Minuten — samt geratener Koordinaten, weil
    /// von außen niemand weiß, wo auf einer 230×230-Karte eine Rampe liegt.
    /// <c>--rail-tour</c> fotografiert diese Liste in EINEM Lauf.</para>
    ///
    /// <para>Die Auswahl ist bewusst nach FORM getroffen, nicht nach Karte:
    /// dieselbe Fahne liefert auf jeder Karte die vergleichbaren Stellen.</para>
    /// </summary>
    /// <summary>
    /// <b>Die Kartenstelle einer Zelle, so wie GEZEICHNET wird</b> — für die
    /// Kamera des Prüfstands.
    ///
    /// <para>⚠ 13.08.2026 — das ist NICHT <c>spalte·40, zeile·20</c>, und der
    /// Unterschied hat schon zweimal Zeit gekostet. <see cref="RailPoint"/>
    /// rechnet <c>_ox + spalte·40 + 20</c> und
    /// <c>_oy + zeile·20 − höhe·15 + 10</c>: dazu kommen also der Zeichen-
    /// ursprung der Karte (auf der ersten Kampagnenkarte 115 px), die halbe
    /// Zelle und die Geländehöhe. <c>--look</c> hat all das ignoriert und
    /// deshalb systematisch danebengezielt — auf der Rampenzelle 163,46 um
    /// zweieinhalb Spalten. Wer eine Zelle fotografieren will, nimmt
    /// DIESEN Punkt.</para></summary>
    public Vector2 RailCellPoint(int col, int row) => RailPoint(new Vector2(col, row));

    public List<(string Label, int Col, int Row)> RailTourSpots()
    {
        var outp = new List<(string, int, int)>();
        var seen = new HashSet<string>();
        void Take(string label, int col, int row)
        {
            if (seen.Add(label)) outp.Add((label, col, row));
        }

        // Zellen nach Zelle greifbar machen, für die Nachbarproben
        var at = new Dictionary<(int, int), RailCell>();
        foreach (var c in _railCells) if (!c.Broken) at[(c.Col, c.Row)] = c;

        foreach (var c in _railCells)
        {
            if (c.Broken) continue;
            if (c.Base is >= 6 and <= 9) Take("rampe", c.Col, c.Row);
            if (c.Base is >= 2 and <= 5) Take("kurve", c.Col, c.Row);
            if (c.Pylon && c.PylonKind != 0) Take("streckenende", c.Col, c.Row);
            if (c.Pylon && c.PylonKind == 0 && c.Base == 0) Take("stuetze", c.Col, c.Row);
            // Ein senkrechter Lauf: Bild 1 mit Bild 1 darüber UND darunter.
            if (c.Base == 1
                && at.TryGetValue((c.Col, c.Row - 1), out var up) && up.Base == 1
                && at.TryGetValue((c.Col, c.Row + 1), out var dn) && dn.Base == 1)
                Take("senkrecht", c.Col, c.Row);
        }
        // Ein zerschossenes Stück, falls die Karte eines mitbringt — dort klafft
        // bei uns ein Loch, und genau das gehört angesehen.
        foreach (var c in _railCells)
            if (c.Broken) { Take("zerschossen", c.Col, c.Row); break; }

        // Der Anschluss: das ERSTE und das LETZTE Feld der längsten Linie.
        int best = -1, bestN = 0;
        foreach (var kv in _lineCell)
            if (kv.Value.Count > bestN) { bestN = kv.Value.Count; best = kv.Key; }
        if (best >= 0 && bestN >= 2)
        {
            var cells = _lineCell[best];
            Take("anschluss-anfang", Mathf.RoundToInt(cells[0].X), Mathf.RoundToInt(cells[0].Y));
            Take("anschluss-ende", Mathf.RoundToInt(cells[^1].X), Mathf.RoundToInt(cells[^1].Y));
        }

        // Ein fahrender Zug, damit Waggon UND Gleis im selben Bild stehen.
        foreach (var w in _wagons)
            if (w.Freight) { Take("zug", Mathf.RoundToInt(w.Col), Mathf.RoundToInt(w.Row)); break; }

        return outp;
    }

    /// <summary>
    /// <c>--shot-when=diagonal</c> — steht gerade ein gezeichneter Waggon auf
    /// einem ECKSTÜCK (Bild f2..f5)? Dann liegt er auf einer Treppe, also auf
    /// einer isometrischen Diagonale, und genau dort war »macht Zicke Zacke beim
    /// Fahren« gemeldet.
    ///
    /// <para>Warum es das braucht: eine Treppe ist ein kleiner Teil der Strecke,
    /// und ein Bild auf gut Glück trifft sie selten — zwei Versuche von Hand
    /// zeigten leeres Gleis. Ein Fehler, den nur das Bild zeigt, ist ohne Bild
    /// nicht zu beurteilen.</para></summary>
    public bool RailWagonOnCorner(out Vector2 at, out int line)
    {
        at = Vector2.Zero; line = -1;
        foreach (var kv in _freightWagons)
        {
            if (!_lineCellFrame.TryGetValue(kv.Key, out var fr)) continue;
            if (!_lineCell.TryGetValue(kv.Key, out var cells)) continue;
            foreach (var w in kv.Value)
            {
                if (w.Hidden || !w.Freight) continue;
                int i = Mathf.Clamp(w.Step, 0, Mathf.Min(cells.Count, fr.Count) - 1);
                if (i < 0) continue;
                int f = fr[i] % 10;
                if (f is < 2 or > 5) continue;
                at = cells[i]; line = kv.Key;
                return true;
            }
        }
        return false;
    }

    /// <summary>Die Zellen aller Waggons der gestauchten Linie, als Text neben
    /// das Bild — ein Foto allein sagt nicht, welche Sprites übereinander
    /// liegen.</summary>
    public string RailSquashWagons()
    {
        if (RailSquashLine < 0 || !_freightWagons.TryGetValue(RailSquashLine, out var list))
            return "(keine)";
        var sb = new System.Text.StringBuilder();
        foreach (var w in list)
            sb.Append($"[{w.Index}] Schritt {w.Step} bei ({w.Col:0.00},{w.Row:0.00})  ");
        return sb.ToString().TrimEnd();
    }

    private Vector2 _railProbePos;
    /// <summary>Die letzte Bewegung des Probewaggons und die Statistik über den
    /// Winkel zwischen zwei Bewegungen — der einzige Zähler, der „Zicke Zacke"
    /// sehen kann. 0° heisst geradeaus, 90° heisst Treppe.</summary>
    private Vector2 _railProbeLast;
    public float RailZigSum, RailZigWorst;
    public int RailZigCount;

    private int _railProbeLine = -1, _railProbeIdx = -1;
    private int _railProbeStep = -1, _railProbeDir;

    private void RailMoveWagons()
    {
        RailSquashNow = 0;
        foreach (var l in _railLines)
        {
            if (!_freightWagons.TryGetValue(l.Slot, out var list) || list.Count == 0) continue;
            if (_lineCell.TryGetValue(l.Slot, out var cells) && cells.Count >= 2)
            {
                // ⚠ Nicht die Zellmitten, sondern die Randmitten der
                // gezeichneten Schiene — sonst faehrt der Zug die Treppe, die
                // die Grafik als Schraege zeigt (siehe RailDrawnPath).
                var pd = RailPathOf(l.Slot);
                RailPlaceWagons(l, pd?.Pts ?? cells, pd?.Cum);
                if (_railProbeLine < 0 && l.Faze is >= 1 and <= 9)
                {
                    _railProbeLine = l.Slot; _railProbeIdx = list[0].Index;
                    RailWagonCellsPerSec = l.TravelFull > 0f ? (cells.Count - 1) / l.TravelFull : 0f;
                }
                // Stauchung: wieviele Waggons DIESER Linie teilen sich in
                // DIESEM Bild eine Zelle? Nur bei fahrenden Linien gefragt —
                // am Bahnsteig darf der Zug stehen, wie er will.
                if (list.Count > 1 && l.Faze is >= 1 and <= 9)
                {
                    RailSquashSeen++;
                    // ⚠ 13.08.2026 — gezaehlt wird jetzt der PUNKT, nicht die
                    // Zelle. Zwei Waggons auf derselben Zelle sind im Original
                    // der Normalfall (54,76 % aller Bilder, nachgefahren ueber
                    // 709 Linien); sie duerfen nur nicht auf DEMSELBEN Punkt
                    // liegen, denn dann sieht man einen statt zweier. Der
                    // kleinste Abstand des Originals ist 12 px bei 40 px
                    // Zellbreite -- alles darunter ist unser Fehler.
                    // Nicht gezeichnete Waggons zaehlen nicht mit.
                    int worst = 1; var wo = list[0];
                    foreach (var a in list)
                    {
                        if (a.Hidden) continue;
                        int n = 0;
                        foreach (var b in list)
                            if (!b.Hidden
                                && Mathf.Abs(b.Col - a.Col) * TileW < RailSquashPx
                                && Mathf.Abs(b.Row - a.Row) * TileH < RailSquashPx) n++;
                        if (n > worst) { worst = n; wo = a; }
                    }
                    if (worst > 1) RailSquashFrames++;
                    if (worst > RailSquashWorst) RailSquashWorst = worst;
                    if (worst > RailSquashNow)
                    {
                        RailSquashNow = worst;
                        RailSquashAt = new Vector2(wo.Col, wo.Row);
                        RailSquashLine = l.Slot;
                    }
                    RailCensusGaps(list);
                }
            }
        }
        foreach (var w in _wagons)
        {
            if (w.Line != _railProbeLine || w.Index != _railProbeIdx) continue;
            var now = new Vector2(w.Col * TileW, w.Row * TileH);
            float d = (now - _railProbePos).Length();
            // Fahrt oder Fahrtwechsel — entschieden am SCHRITT, nicht an einer
            // Pixelgrenze: einen Schritt weiter in derselben Richtung ist
            // Fahrt, alles andere ist Abfahrt, Umkehr oder Klemmen.
            bool fahrt = w.Dir == _railProbeDir && Mathf.Abs(w.Step - _railProbeStep) <= 1;
            if (_railProbeStep >= 0)
            {
                if (fahrt)
                {
                    if (d > RailWagonMaxPxPerFrame)
                    {
                        RailWagonMaxPxPerFrame = d;
                        RailWagonJumpWhere = $"Linie {w.Line} Schritt {_railProbeStep}->{w.Step} " +
                            $"bei ({w.Col:0.00},{w.Row:0.00})";
                    }
                    // ⚠ 15.08.2026 — DIE GESCHWINDIGKEIT ist die Zahl, die
                    // »ruckelt es?« beantwortet, nicht der Weg je Takt. Siehe
                    // RailWagonPxPerSecMin.
                    if (_railLastDt > 0.0005f && d > 0.01f)
                    {
                        float v = d / _railLastDt;
                        if (v < RailWagonPxPerSecMin) RailWagonPxPerSecMin = v;
                        if (v > RailWagonPxPerSecMax) RailWagonPxPerSecMax = v;
                    }
                }
                else { RailWagonTurnCount++; if (d > RailWagonTurnMaxPx) RailWagonTurnMaxPx = d; }
                // ⚠ 15.08.2026 — DER ZAEHLER FUER »ZICKE ZACKE«. Gemeldet:
                // »wenn eine Bahnlinie diagonal geradlinig verlaeuft, macht die
                // Bahn trotzdem Zicke Zacke beim Fahren«. Keine der bisherigen
                // Zahlen konnte das sehen: Geschwindigkeit, Weg je Takt und
                // Fahrtwechsel messen alle nur den BETRAG. Zickzack ist aber
                // eine Sache der RICHTUNG — der Winkel zwischen zwei
                // aufeinanderfolgenden Bewegungen. Auf einer glatten Schraege
                // liegt er bei 0, auf einer Treppe wechselt er zwischen 0 und
                // 90 Grad. Gezaehlt werden Mittelwert und Groesstwert, und nur
                // waehrend echter Fahrt.
                if (fahrt && d > 0.01f)
                {
                    var step = now - _railProbePos;
                    if (_railProbeLast != Vector2.Zero)
                    {
                        float cos = step.Normalized().Dot(_railProbeLast.Normalized());
                        float ang = Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)));
                        RailZigSum += ang; RailZigCount++;
                        if (ang > RailZigWorst) RailZigWorst = ang;
                    }
                    _railProbeLast = step;
                }
            }
            _railProbePos = now; _railProbeStep = w.Step; _railProbeDir = w.Dir;
            break;
        }
    }

    // ---- Prüfstand ----------------------------------------------------------

    private static string TypeShort(Entity? e) => e == null ? "?" : e.BType switch
    {
        1 => "Basis", 2 => "WFabrik", 3 => "FFabrik", 4 => "SFabrik", 5 => "Depot",
        6 => "Bahnhof", 7 => "Gener", 8 => "Radar", 9 => "Flugh", 10 => "Mine",
        11 => "Seedock", 12 => "Feldbhf", 13 => "Kraftw", 14 => "Nachsch",
        15 => "Feldmine", 16 => "Werft", _ => "Typ" + e.BType,
    };

    private static string ModeWord(int m) => m switch { 1 => "->", 0 => "<-", _ => "." };

    /// <summary>`--rail-check`, Kopfteil: was die Karte an Linien mitbringt und
    /// was die Matrix daraus macht. Einmal am Anfang.</summary>
    public string RailCheckHead()
    {
        RebuildRailIndex();
        if (_railLines.Count == 0)
            return $"rail-check: diese Karte hat 0 SPOJ-Linien " +
                   $"({_rail.Count} Knoten im Graphen) — die Bahn kann hier nichts fahren, " +
                   "der Nahweg-Ersatz in Haul/Consignee traegt allein";

        var sb = new System.Text.StringBuilder();
        int live = 0, dead = 0;
        var perGood = new int[4];
        var perPair = new Dictionary<string, int>();
        foreach (var l in _railLines)
        {
            var a = RailBuilding(l.Bud1);
            var b = RailBuilding(l.Bud2);
            if (a == null || b == null) { dead++; continue; }
            SpojModeFor(a.BType, b.BType, l.Mode);
            bool any = false;
            for (int k = 0; k < 4; k++) if (l.Mode[k] != 2) { perGood[k]++; any = true; }
            if (any) live++; else dead++;
            if (any)
            {
                string key = $"{TypeShort(a)}-{TypeShort(b)}";
                perPair[key] = perPair.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        }
        sb.Append($"rail-check: {_railLines.Count} SPOJ-Linien, {live} tragen Ware, {dead} nicht");
        sb.Append("   je Ware:");
        for (int k = 0; k < 4; k++) sb.Append($" {GoodName[k]} {perGood[k]}");
        sb.Append($"\n  Fahrzeit GERECHNET: {TrainStepTicksStraight} Takte je gerader / " +
                  $"{TrainStepTicksDiagonal} je diagonaler Schritt (Schrittpreis 40/28 @0x4C6E53, " +
                  $"Abzug 8/Takt aus sec44+0x0c, 1439 von 1439 Waggons) = " +
                  $"{TrainStepSeconds:0.000}s / {TrainStepSecondsDiagonal:0.000}s bei TickScale {TickScale}; " +
                  $"im ORIGINAL bei {OriginalTicksPerSecond:0} Hz (SetTimer 20ms @0x415BC5) " +
                  $"{TrainStepTicksStraight / OriginalTicksPerSecond:0.000}s / " +
                  $"{TrainStepTicksDiagonal / OriginalTicksPerSecond:0.000}s" +
                  $"\n  Automat {1f / RailTickSeconds:0.0}x/s (Bild%5 @0x41638D bei TickScale {TickScale}), " +
                  $"Standzeit {RailDwellTicks} Runden = {RailDwellTicks * RailTickSeconds:0.0}s je Ende, " +
                  $"Ladebudget {RailLoadBudget}/Fahrt (0x4C6652)");

        // Was die Matrix je Linie entscheidet — die ersten zwölf im Klartext
        int shown = 0;
        foreach (var l in _railLines)
        {
            var a = RailBuilding(l.Bud1);
            var b = RailBuilding(l.Bud2);
            if (a == null || b == null) continue;
            bool any = false;
            for (int k = 0; k < 4; k++) if (l.Mode[k] != 2) any = true;
            if (!any || shown++ >= 12) continue;
            var g = new List<string>();
            for (int k = 0; k < 4; k++)
                if (l.Mode[k] != 2)
                    g.Add($"{GoodName[k]}{ModeWord(l.Mode[k])}");
            sb.Append($"\n  Linie {l.Slot,2}: {TypeShort(a)}({l.Bud1}) P{a.Owner} <-> " +
                      $"{TypeShort(b)}({l.Bud2}) P{b.Owner}  {l.Steps,3} Schritte  {string.Join(" ", g)}");
        }
        var pairs = new List<string>();
        foreach (var kv in perPair) pairs.Add($"{kv.Key} x{kv.Value}");
        pairs.Sort();
        sb.Append("\n  Paare: " + string.Join(", ", pairs));
        return sb.ToString();
    }

    /// <summary>
    /// <b>Die Zahl gegen »die Schiene schwebt«</b>: der Höhenunterschied in
    /// Pixeln zwischen der letzten Gleiszelle und dem Schienendeck des
    /// Gebäudes, je Linienende und je Gebäudeart.
    ///
    /// <para>Gerechnet in <see cref="MapEntityLayer.RailDeckOffSum"/>; sie muss
    /// 0 sein. Ein Vielfaches von 20 heißt falsche Anschlusszeile, ein
    /// Vielfaches von 15 eine Geländestufe zwischen Gebäude und Gleis (dafür
    /// hat das Original die Rampen f6..f9, die wir nicht legen), ein bis zwei
    /// Pixel heißen falscher Deckversatz.</para>
    ///
    /// <para>Gegenprobe: <c>--rail-lay=nodock</c> nimmt das Nachführen heraus,
    /// dann steigt die Zahl.</para>
    /// </summary>
    private string RailDeckReport()
    {
        if (RailDeckOffCount == 0) return " | Anschlusszeile: kein Ende mit gemessenem Anbau";
        var sb = new System.Text.StringBuilder();
        sb.Append($" | Anschlusszeile: {RailDeckFlush} von {RailDeckOffCount} Enden treffen sie " +
                  $"(0 px), schlimmste Abweichung {RailDeckOffMax} px, " +
                  $"im Mittel {(float)RailDeckOffSum / RailDeckOffCount:0.0} px");
        var per = new List<string>();
        foreach (var kv in RailDeckByType)
            per.Add($"{TypeShort(new Entity { BType = kv.Key })} " +
                    $"{kv.Value.Flush}/{kv.Value.Flush + kv.Value.Off} buendig" +
                    (kv.Value.Off > 0 ? $" (bis {kv.Value.Worst} px)" : ""));
        per.Sort();
        sb.Append("  je Art: " + string.Join(", ", per));
        return sb.ToString();
    }

    /// <summary>
    /// <b>Woher die Strecke kommt — und wie weit unsere alte Ableitung
    /// danebenlag.</b> Die Zahl, mit der sich »du liest irgendwas falsch«
    /// beantworten lässt: nur die Karte (sec22) weiß, welche Zelle ein Gleis
    /// trägt und welches Bild sie zeigt.
    ///
    /// <para>»nur wir« = Zellen, auf die wir ein Stück gelegt haben, die die
    /// Karte gar nicht als Gleis führt. »nur Karte« = Gleiszellen, die wir
    /// übersehen haben. »anderes Bild« = Zellen, die beide kennen, an denen
    /// unsere Ableitung aus den Nachbarn aber eine andere Form gewählt hätte —
    /// dort stand im Bild eine Kurve, wo eine Gerade hingehört, oder umgekehrt.
    /// Alle drei müssen nicht 0 sein: sie SIND der Fehler, und sie stehen hier,
    /// damit die Behebung eine Zahl hat.</para>
    /// </summary>
    private string RailSourceReport()
    {
        if (RailCellsFromMap == 0)
            return " | Quelle: sec22 fehlt, Strecke ABGELEITET (unsere Konstruktion)";
        var sb = new System.Text.StringBuilder();
        sb.Append($" | Quelle: sec22 der Karte, {RailCellsFromMap} Gleiszellen");
        if (RailCellsBroken > 0)
            sb.Append($" ({RailCellsBroken} zerschossen, davon {RailBrokenDrawn} als " +
                      "Truemmer gelegt — Teil 69, Bild 4020..4039)");
        sb.Append($" | alte Ableitung wich ab: {RailDiffOnlyOurs} Zellen nur wir, " +
                  $"{RailDiffOnlyMap} nur Karte, {RailDiffFrame} von {RailDiffChecked} " +
                  "gemeinsamen mit anderem Bild");
        var per = new int[10];
        var kind = new int[4];
        int pylon = 0;
        foreach (var c in RailCellFrames())
        {
            if (c.F is >= 0 and <= 9) per[c.F]++;
            if (!c.P) continue;
            pylon++;
            if (c.K is >= 0 and <= 3) kind[c.K]++;
        }
        sb.Append("  Bilder:");
        for (int i = 0; i < 10; i++) if (per[i] > 0) sb.Append($" f{i} {per[i]}");
        sb.Append($"  (f6..f9 = RAMPEN, {per[6] + per[7] + per[8] + per[9]} Stueck)");
        sb.Append($"  Stuetzen {pylon} (Platz%6==0, @0x42D4B1), Fassung " +
                  $"65:{kind[0]} 66:{kind[1]} 67:{kind[2]} 68:{kind[3]} (@0x4B0350)");
        if (RailChainSplit > 0)
            sb.Append($"  ⚠ {RailChainSplit} Linien lagen in mehreren Stuecken, " +
                      $"{RailChainDropped} Fremdzellen aus der Kette genommen " +
                      "(betrifft nur den Fahrweg, gezeichnet werden sie weiter)");
        return sb.ToString();
    }

    private readonly Dictionary<int, int[]> _railStart = new();

    /// <summary>`--rail-check`, laufende Meldung: der Automat, die Fahrten und
    /// — das eigentliche Beweisstück — <b>wie sich die Lager der Zielgebäude
    /// über die Zeit ändern</b>. Eine einzelne Momentaufnahme beweist nichts.</summary>
    public string RailCheckLine()
    {
        if (_railLines.Count == 0)
            return "rail-check: 0 Linien — nichts zu fahren";
        RebuildRailIndex();

        // Merkzettel beim ersten Aufruf: der Anfangsbestand jedes Endgebaeudes
        bool first = _railStart.Count == 0;
        foreach (var l in _railLines)
            foreach (int s in new[] { l.Bud1, l.Bud2 })
            {
                var e = RailBuilding(s);
                if (e == null || _railStart.ContainsKey(s)) continue;
                _railStart[s] = new[] { e.StockW, e.StockF, e.StockS, e.StockT };
            }

        int fahrend = 0, wartend = 0, tot = 0, frisch = 0;
        int geladen = 0;
        foreach (var l in _railLines)
        {
            if (l.Faze == 4) tot++;
            else if (l.Faze == 0) frisch++;
            else if (l.Faze <= 9) { fahrend++; for (int k = 0; k < 4; k++) geladen += l.Cargo[k]; }
            else wartend++;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"rail-check: {_railLines.Count} Linien — {fahrend} unterwegs (mit {geladen} Stueck), " +
                  $"{wartend} am Bahnsteig, {frisch} noch nicht los, {tot} tot (faze 4, verschiedene Besitzer)" +
                  $" | {RailTrips} Fahrten, {RailMoved} Stueck bewegt (");
        for (int k = 0; k < 4; k++) sb.Append($"{(k > 0 ? " " : "")}{GoodName[k]} {_railMovedGood[k]}");
        int fw = 0;
        foreach (var w in _wagons) if (w.Freight) fw++;
        sb.Append($") | {fw} von {_wagons.Count} Waggons am Fahrplan");

        sb.Append($" | Kreuzungen: {RailCrossings} Zellen mit zwei Gleissaetzen, " +
                  "beide gezeichnet");
        RailMeasureJoints();
        sb.Append($" | Ecken: {RailJointOpen} von {RailJointPairs} Stoessen mit einem LOCH " +
                  $"zwischen den Schienen, breitester {RailJointWorst:0} px");
        if (RailJointOpen > 0) sb.Append($" bei {RailJointWorstWhere}");
        if (RailJointPairs > 0)
            sb.Append($" (Mittenversatz im Mittel {RailPortOffSum / RailJointPairs:0.0} px " +
                      "— gewollt, siehe RailMeasureJoints)");
        sb.Append($" | Gleis: {RailTilesDrawn} Stuecke gezeichnet, " +
                  $"{RailTilesLoose} davon NICHT Kante an Kante");
        sb.Append(RailSourceReport());
        // Die Zahl fuer »buendig«: wieviele Linienenden lagen NICHT auf der
        // Anschlusszeile ihres Endgebaeudes. Gezaehlt wird VOR dem Ruecken,
        // damit --rail-lay=nodock (Gegenprobe) dieselbe Zahl zeigt und sich
        // nur das Bild aendert; RailDockMoved sagt, wieviele geholt wurden.
        // ⚠ 13.08.2026 — hier stand »Anschluss: 0 von 0 Enden lagen NICHT auf
        // der Anschlusszeile, 0 nachgefuehrt«. Die Zeile berichtete ueber das
        // RUECKEN, und das laeuft seit sec22 gar nicht mehr — eine Null von
        // Null, die sich wie ein bestandener Prueflauf liest. Sie steht nur
        // noch da, wo wirklich gerueckt wird; sonst zaehlt die Frage, die
        // vorher niemand stellte: erreicht das Ende sein Gebaeude ueberhaupt?
        if (RailDockChecked > 0)
            sb.Append($" | Anschluss: {RailDockOff} von {RailDockChecked} Enden lagen " +
                      $"NICHT auf der Anschlusszeile, {RailDockMoved} nachgefuehrt");
        sb.Append($" | Linienenden: {RailEndFar} von {RailEndChecked} weiter als 2 Zellen " +
                  $"vom Endgebaeude (schlimmstes {RailEndWorst}");
        sb.Append(RailEndWorst > 2 ? $": {RailEndWorstWhere})" : ")");
        // ⚠ Der Kopf dieser Zeile hiess "Deckhoehe" und misst die Hoehe NICHT:
        // RailDeckPixel steht auf beiden Seiten der Differenz und kuerzt sich
        // heraus (siehe RailDeckOffSum). Sie misst die ZEILE. Die Hoehe misst
        // aekernel-tools/rail_deck_overlay.py gegen die Gebaeudegrafik.
        sb.Append(RailDeckReport());
        // Der Beweis fuer »faehrt gleitend statt zu huepfen«: die Stelle des
        // ersten fahrenden Waggons auf ein Hundertstel genau. Steht dort eine
        // ganze Zahl, springt er von Schritt zu Schritt.
        foreach (var w in _wagons)
            if (w.Freight)
            {
                sb.Append($" | Waggon {w.Index} auf Linie {w.Line} bei " +
                          $"({w.Col:0.00},{w.Row:0.00}) Schritt {w.Step}");
                // ⚠ Die Geschwindigkeit steht VORN, denn sie beantwortet die
                // Frage; der Weg je Takt haengt an der Bildzeit und ist nur
                // noch Beiwerk (siehe RailWagonPxPerSecMin).
                string band = RailWagonPxPerSecMax > 0f
                    ? $"bis {RailWagonPxPerSecMax:0} px/s (kleinster Wert " +
                      $"{RailWagonPxPerSecMin:0}, an den Fahrtenden)"
                    : "noch nichts gefahren";
                sb.Append($" | Lauf: {RailWagonCellsPerSec:0.00} Zellen/s, Geschwindigkeit " +
                          $"{band} (Original 400 px/s bei 50 Hz, bei TickScale 16 also 128)" +
                          $", Weg je Takt hoechstens {RailWagonMaxPxPerFrame:0.00} px" +
                          (RailWagonMaxPxPerFrame > 4f ? $" ({RailWagonJumpWhere})" : "") +
                          $", Weg: {RailPathFallback} von {RailPathNodes} Knoten ohne " +
                          "Nachbarzelle (dort liegt die Kette nicht Kante an Kante)" +
                          (RailZigCount > 0
                               ? $", Richtungswechsel je Takt im Mittel " +
                                 $"{RailZigSum / RailZigCount:0.0}°, schlimmstenfalls " +
                                 $"{RailZigWorst:0}° ({RailZigCount} Messungen)"
                               : "") +
                          $", {RailWagonTurnCount} Fahrtwechsel getrennt gezaehlt " +
                          $"(groesster Sprung {RailWagonTurnMaxPx:0.00} px)");
                sb.Append($" | Stauchung: {RailSquashFrames} von {RailSquashSeen} Linienbildern mit " +
                          $"zwei Waggons naeher als {RailSquashPx:0} px, schlimmstenfalls " +
                          $"{RailSquashWorst} (Original: kleinster Abstand 12 px)");
                RailMeasureGaps();
                sb.Append($" | Zusammenhang: {RailGapOpen} von {RailGapPairs} Waggonpaaren mit " +
                          $"sichtbarer Luecke, groesste {RailGapWorst:0.0} px");
                sb.Append(RailGapReport());
                break;
            }
        return sb.ToString();

        // Die Frage des Spielers war »wie laufen die Teile zusammen«, also wird
        // sie hier beantwortet: die drei Bauteile nach Gebaeudeart, Anfang ->
        // jetzt. Bei den BASEN muessen sie ankommen — dort baut das Original.
        var roll = new Dictionary<string, int[]>();   // art -> was/ist je Ware x2
        foreach (var kv in _railStart)
        {
            var e = RailBuilding(kv.Key);
            if (e == null) continue;
            string art = e.BType switch
            {
                1 => "Basen", 2 or 3 or 4 => "Fabriken", 6 or 12 => "Bahnhoefe",
                10 or 15 => "Minen", 9 => "Flughaefen", 11 or 16 => "Docks",
                _ => "sonstige",
            };
            if (!roll.TryGetValue(art, out var acc)) roll[art] = acc = new int[9];
            for (int k = 0; k < 4; k++) { acc[k] += kv.Value[k]; acc[4 + k] += RailStore(e, k); }
            acc[8]++;
        }
        foreach (string art in new[] { "Basen", "Bahnhoefe", "Fabriken", "Minen", "Flughaefen", "Docks" })
        {
            if (!roll.TryGetValue(art, out var acc)) continue;
            sb.Append($"\n  {art,-11}({acc[8],2}): ");
            for (int k = 0; k < 4; k++)
                sb.Append($"{GoodName[k][0]} {acc[k]}->{acc[4 + k]}" +
                          (acc[4 + k] > acc[k] ? "+" : acc[4 + k] < acc[k] ? "-" : "=") + "  ");
        }

        // Die drei Gebaeude, deren Lager am staerksten gewachsen ist — mit
        // Anfangs- und Jetztwert, damit die Bewegung sichtbar wird
        var rank = new List<(int Slot, int Grow, Entity E)>();
        foreach (var kv in _railStart)
        {
            var e = RailBuilding(kv.Key);
            if (e == null) continue;
            int now = e.StockW + e.StockF + e.StockS + e.StockT;
            int was = kv.Value[0] + kv.Value[1] + kv.Value[2] + kv.Value[3];
            if (now != was) rank.Add((kv.Key, now - was, e));
        }
        rank.Sort((x, y) => y.Grow.CompareTo(x.Grow));
        int n = 0;
        foreach (var r in rank)
        {
            if (n++ >= 4) break;
            var s0 = _railStart[r.Slot];
            sb.Append($"\n  {TypeShort(r.E)}({r.Slot}) P{r.E.Owner}: " +
                      $"W {s0[0]}->{r.E.StockW}  F {s0[1]}->{r.E.StockF}  " +
                      $"S {s0[2]}->{r.E.StockS}  T {s0[3]}->{r.E.StockT}" +
                      (r.Grow > 0 ? $"   GEWACHSEN +{r.Grow}" : $"   abgegeben {r.Grow}"));
        }
        if (rank.Count == 0) sb.Append("\n  (noch kein Lager veraendert)");
        return sb.ToString();
    }
}
