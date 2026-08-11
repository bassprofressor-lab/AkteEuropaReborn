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

    /// <summary>⚠ <b>UNSERE SETZUNG</b> — und die einzige hier. Sekunden je
    /// Streckenschritt einer Fahrt. Der Automat zählt Takte, und wie lang ein
    /// Takt in Sekunden ist, steht nirgends in den Daten. Wir nehmen dieselbe
    /// Zahl, mit der die gezeichneten Waggons schon laufen
    /// (<see cref="TrainStepSeconds"/>), damit Bild und Ware zusammenpassen:
    /// eine NET05-Linie ist im Mittel 33 Schritte lang, also rund 11 s je
    /// Richtung und mit den beiden Standzeiten rund 35 s je Umlauf.</summary>
    private const float RailStepSeconds = TrainStepSeconds;

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

    private void UpdateFreight(float dt)
    {
        if (_railLines.Count == 0) return;
        if (_bldBySlot.Count == 0) RebuildRailIndex();
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
                // unterwegs — der Zug-Tick bewegt ihn und meldet die Ankunft
                l.Travel -= dt;
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

        l.TravelFull = l.Steps * RailStepSeconds;   // ⚠ unsere Setzung
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
        if (!_lineRoute.TryGetValue(l.Slot, out var route) || route.Count < 2) return;
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
        RailPlaceWagons(l, route);
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

    private void RailPlaceWagons(RailLine l, List<Vector2> route)
    {
        if (!_freightWagons.TryGetValue(l.Slot, out var list) || list.Count == 0) return;
        float p = l.TravelFull <= 0f ? 1f : 1f - Mathf.Clamp(l.Travel / l.TravelFull, 0f, 1f);
        int last = route.Count - 1;
        int lead = Mathf.RoundToInt(p * last);
        if (l.Dir == 1) lead = last - lead;
        _linePiece.TryGetValue(l.Slot, out var pcs);
        foreach (var w in list)
        {
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
            int step = Mathf.Clamp(lead - (l.Dir == 0 ? 1 : -1) * w.Index, 0, last);
            w.Step = step;
            w.Dir = l.Dir == 0 ? 1 : -1;
            var pt = route[step];
            w.Col = pt.X; w.Row = pt.Y;
            if (pcs != null && step < pcs.Count) w.Piece = pcs[step];
        }
    }

    private void RailMoveWagons()
    {
        foreach (var l in _railLines)
        {
            if (!_freightWagons.TryGetValue(l.Slot, out var list) || list.Count == 0) continue;
            if (_lineRoute.TryGetValue(l.Slot, out var route) && route.Count >= 2)
                RailPlaceWagons(l, route);
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
        sb.Append($"\n  Fahrzeit UNSERE SETZUNG: {RailStepSeconds:0.00}s je Streckenschritt; " +
                  $"Automat {1f / RailTickSeconds:0.0}x/s (Bild%5 @0x41638D bei TickScale {TickScale}), " +
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

        if (first) return sb.ToString();

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
