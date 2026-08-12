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

    private void UpdateFreight(float dt)
    {
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

    /// <summary><paramref name="route"/> ist seit dem 12.08.2026 die
    /// ZELLENKETTE der Linie (<c>_lineCell</c>), nicht mehr die Route auf
    /// halben Zeilen. Der Zug faehrt damit auf genau den Zellen, auf denen
    /// auch das Gleis liegt — vorher lag jeder Punkt auf halber Zeile 10 px
    /// neben der Schiene, und der Waggonabstand musste ueber
    /// <c>StepBackColumns</c> geschaetzt werden, weil ein Routenschritt mal
    /// eine ganze und mal eine halbe Zelle war. Eine Zelle ist jetzt eine
    /// Zelle: Waggon <c>i</c> steht <c>i</c> Zellen zurueck, fertig.</summary>
    private void RailPlaceWagons(RailLine l, List<Vector2> route)
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

            float leadF = Mathf.Clamp(pw, 0f, 1f) * last;
            if (l.Dir == 1) leadF = last - leadF;
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
            int nxt = Mathf.Clamp(step + 1, 0, last);
            var pt = route[step].Lerp(route[nxt], frac);
            w.Col = pt.X; w.Row = pt.Y;
            if (pcs != null && step < pcs.Count) w.Piece = pcs[step];
        }
    }

    /// <summary>
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

    /// <summary>Die Fahrtwechsel des gemessenen Waggons (Richtungswechsel oder
    /// Sprung über mehr als einen Schritt), getrennt gezählt statt an einer
    /// Pixelgrenze weggeworfen. <see cref="RailWagonTurnMaxPx"/> ist der
    /// größte dabei zurückgelegte Weg.</summary>
    public int RailWagonTurnCount;
    public float RailWagonTurnMaxPx;

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

    /// <summary>Dasselbe für DIESES Bild statt über den ganzen Lauf, samt der
    /// Zelle, auf der es passiert. Damit kann <c>--shot-when=squash</c> auf den
    /// Augenblick warten und die Kamera hinstellen — die Stauchung dauert nur
    /// etwa eine Sekunde je Abfahrt, ein Bild auf gut Glück trifft sie in
    /// einem von zehn Fällen.</summary>
    public int RailSquashNow;
    public Vector2 RailSquashAt;
    public int RailSquashLine = -1;

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
                RailPlaceWagons(l, cells);
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
                if (fahrt) { if (d > RailWagonMaxPxPerFrame) RailWagonMaxPxPerFrame = d; }
                else { RailWagonTurnCount++; if (d > RailWagonTurnMaxPx) RailWagonTurnMaxPx = d; }
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
                  $"vom Endgebaeude (schlimmstes {RailEndWorst})");
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
                sb.Append($" | Lauf: {RailWagonCellsPerSec:0.00} Zellen/s, groesste Aenderung " +
                          $"{RailWagonMaxPxPerFrame:0.00} px je Bild (Original: 8 px je Takt, @0x4C6BA1)" +
                          $", davon getrennt {RailWagonTurnCount} Fahrtwechsel " +
                          $"(groesster Sprung {RailWagonTurnMaxPx:0.00} px)");
                sb.Append($" | Stauchung: {RailSquashFrames} von {RailSquashSeen} Linienbildern mit " +
                          $"zwei Waggons naeher als {RailSquashPx:0} px, schlimmstenfalls " +
                          $"{RailSquashWorst} (Original: kleinster Abstand 12 px)");
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
