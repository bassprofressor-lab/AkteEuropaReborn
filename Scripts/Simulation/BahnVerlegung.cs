namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>»Transportieren« aus dem Bahnhof — mit echten Einheiten</b>
/// (22.09.2026, berichte/bahnhof-transport-fable.md §4, Bauliste 8).
///
/// <para>Bis heute verlegte die Bahn nur ENTWURFSNUMMERN aus dem Basisfenster
/// (<see cref="TransportFromPanel"/>), und am Ziel landete die Nummer im Depot.
/// Das Original verlegt die EINHEIT selbst:</para>
/// <code>
///   Karte Betriebsart 5, Zielgebaeude anklicken       0x44978B
///   Flutsuche 0x4CE710(Ziel, Quelle); 0 -> Meldung    »Es besteht keine Verbindung zu diesem Gebäude«
///                                                     / »mit dem Ursprungsgebäude!« (0x4FBBDC/0x4FBC40)
///   je markierter Zeile Befehl 518 -> CreateConvoy 0x4CEA90:
///       Satz in 0xBC0DD0, UKOL := 0x37, aus der Bahnhofsliste (0x43C430)
///   Zug faehrt, Umstieg wie gehabt
///   Ankunft 0x410DF0: UKOL := 0x38, +0x15 := Zielgebaeude
///   Arm 0x409E4D: Tuer des Ziels frei? sonst warten; Einheit AN DIE TUER,
///       dann 0x40AFE0: fahre nach (x + 2 − rand%5, y + 5 − rand%3)
/// </code>
/// <para>⇒ Die Einheit erscheint an der Tür des Zielgebäudes und fährt auf den
/// Vorplatz — sie wird <b>nicht</b> eingelagert. <c>0x40AFE0</c> ist derselbe
/// Austritt wie beim Depot (<see cref="AustrittsZiel"/>, depot = true).</para>
///
/// <para>Gegenschalter <c>--verlegung-depot-alt</c>: die Einheit geht am Ziel
/// wie bisher ins Depot (als Entwurf).</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool VerlegungDepotAlt;

    /// <summary>Wie oft eine Einheit aus der BASIS per Bahn verlegt wurde (bug-372).</summary>
    public int BahnVerlegtAusBasis;

    /// <summary>UKOL der fahrenden (0x37) und der angekommenen (0x38) Einheit.</summary>
    public const int UkolImZug = 0x37, UkolAngekommen = 0x38;

    public int BahnVerlegtEinheiten, BahnAnTuerAngekommen, BahnAnTuerWartet, BahnKeineVerbindung;

    private readonly List<(Entity U, Entity Ziel)> _ankunftTuer = new();

    /// <summary>
    /// Befehl 518 je markierter Einheit aus dem BAHNHOFSFENSTER
    /// (Quelle = das offene Gebäudefenster). Gibt die Zahl der gestarteten
    /// Fahrten zurück; 0 = keine Verbindung (Meldung ist dann offen).
    /// </summary>
    public int TransportierenAusBahnhof(List<int> griffe, int zielIdx)
        => VerlegePerBahn(Fenstergebaeude(), griffe, zielIdx);

    /// <summary>Dasselbe mit ausdruecklicher Quelle — das Basisfenster haengt an
    /// <c>Producer()</c>, nicht an <c>Fenstergebaeude()</c> (bug-372).</summary>
    private int VerlegePerBahn(Entity? q, List<int> griffe, int zielIdx)
    {
        // ⭐ 30.09.2026, bug-372 — auch die BASIS (Typ 1) ist Quelle: das
        // Basisfenster schickt denselben Befehl 518 (@0x44A32F -> 0x4498A3),
        // und CreateConvoy 0x4CEA90 weicht erst @0x4CEB99 nach der Quelle ab
        // (berichte/createconvoy-basis-fable.md §1). Gegenschalter
        // --basis-verlegung-alt: nur Bahnhof und Feldbahnhof wie bisher.
        if (q == null) return 0;
        if (q.BType is not (6 or 12) && (BasisVerlegungAlt || q.BType != 1)) return 0;
        if (zielIdx < 0 || zielIdx >= _entities.Count) return 0;
        var ziel = _entities[zielIdx];
        if (!ziel.IsBuilding || ziel.Dead || ReferenceEquals(ziel, q)) return 0;

        int a = NodeOfBuilding(q.Slot), b = NodeOfBuilding(ziel.Slot);
        Simulation.RailNetwork.OwnerOf = slot =>
        { int i = EntityOfSlot(slot); return i < 0 ? -1 : _entities[i].Owner; };
        var weg = a < 0 || b < 0 ? null
                : Simulation.RailNetwork.FindRoute(_railNodes, _netzLinien, a, b, q.Owner,
                                                   Simulation.RailNetwork.CampaignRules);
        if (weg == null)
        {
            BahnKeineVerbindung++;
            OnMeldung?.Invoke("Es besteht keine Verbindung zu diesem Gebäude",
                              "mit dem Ursprungsgebäude!", 3, false);
            return 0;
        }
        int n = 0;
        foreach (int g in griffe)
        {
            if (g < 0 || g >= _entities.Count) continue;
            var u = _entities[g];
            if (!q.Garage.Remove(u)) continue;               // 0x43C430 / Basis 0x43C120
            u.InGebaeude = null;
            // ⭐⭐ 30.09.2026, bug-372 — DIE BASIS SCHREIBT DAS TEMPO NEU, der
            // Bahnhof nicht. CreateConvoy 0x4CEA90 @0x4CEB99 (O, C und F):
            //   al = byte [0xC06914 + 76·Quelle]   ; Gebaeudetyp
            //   cmp al,1 / jbe -> 0x43C120 »Robot not found« (Basis ausdocken)
            //                     0x43C1BC  +0x20 := Entwurf +0x1D, bedingungslos
            //   sonst          -> 0x43C430 »Robot not found 2« (Bahnhofsliste),
            //                     schreibt +0x20 NICHT
            // Eine vom Plasma gelaehmte Einheit faehrt aus der Basis also geheilt
            // los, aus dem Bahnhof gelaehmt. Tank und Munition schreibt 0x43C120
            // hier NICHT (die fuellt nur der Aussende-Weg 0x410420).
            // Gegenschalter: derselbe wie bug-371, --ausdock-tempo-alt — es ist
            // dieselbe Routine 0x43C120.
            if (q.BType == 1 && !AusdockTempoAlt && EntwurfsTempo(u) is { } tempo)
            {
                if (u.Speed != tempo) AusdockTempoNeu++;
                u.Speed = tempo;
            }
            u.Ukol = UkolImZug;                              // +0x14 := 0x37
            RailTransferStartEinheit(u, weg, q.Owner);
            BahnVerlegtEinheiten++;
            if (q.BType == 1) BahnVerlegtAusBasis++;
            n++;
        }
        _order = n > 0 ? $"{n} Einheit(en) fahren nach {(ziel.Name.Length > 0 ? ziel.Name : "Gebaeude " + ziel.Slot)}" : _order;
        QueueRedraw();
        return n;
    }

    /// <summary>Ankunft einer echten Einheit (0x410DF0): sie wartet auf die Tür.</summary>
    private void BahnAnkunftEinheit(Entity u, Entity ziel)
    {
        if (VerlegungDepotAlt)
        {
            // Der alte Weg: in die Garage des Ziels (untergestellt).
            u.Ukol = UkolUntergestellt;
            u.InGebaeude = ziel;
            ziel.Garage.Add(u);
            return;
        }
        u.Ukol = UkolAngekommen;
        _ankunftTuer.Add((u, ziel));
    }

    /// <summary>Der UKOL-0x38-Arm <c>0x409E4D</c>, je Takt: Tür frei → an die
    /// Tür, dann auf den Vorplatz. Sonst warten.</summary>
    private void BahnAnkunftTakt()
    {
        if (_ankunftTuer.Count == 0 || _nav == null) return;
        for (int i = _ankunftTuer.Count - 1; i >= 0; i--)
        {
            var (u, ziel) = _ankunftTuer[i];
            if (u.Dead) { _ankunftTuer.RemoveAt(i); continue; }
            if (ziel.Dead)
            {
                // ⚠ UNSERE Setzung: das Original haelt die Einheit mit UKOL 0x38
                // am Gebaeudeplatz; was bei dessen Verlust geschieht, ist fuer
                // DIESEN Zustand nicht gelesen. Wir loeschen sie wie 0x4CEC20
                // die Wartenden.
                u.Dead = true;
                RailTransfersLost++;
                _ankunftTuer.RemoveAt(i);
                continue;
            }
            int ui = _entities.IndexOf(u);
            int tx = ziel.Col + ziel.DoorCol, ty = ziel.Row + ziel.DoorRow;
            if (!_nav.IsFree(tx, ty, u.Move, ui)) { BahnAnTuerWartet++; continue; }
            _ankunftTuer.RemoveAt(i);
            u.Ukol = 0;
            u.Col = tx; u.Row = ty;
            u.Elev = ElevOf(tx, ty);
            u.Pos = BodyCenterAt(u, tx, ty);
            u.Footprint = CellRect(_ox, _oy, tx, ty, u.Elev);
            u.Target = -1; u.Path = null; u.Goal = new Vector2I(tx, ty);
            _nav.SetHull(ui, Simulation.NavGrid.HullSide(u.GameUnitType));
            _nav.SetOccupant(tx, ty, ui, u.Infantry >= 0);
            var vor = AustrittsZiel(ziel, depot: true);      // 0x40AFE0
            if (_nav.InBounds(vor.X, vor.Y))
            {
                var weg = _nav.FindPath(new Vector2I(tx, ty), vor, u.Move, ui);
                if (weg is { Count: > 0 }) { u.Path = weg; u.PathIdx = 0; u.Goal = vor; }
            }
            BahnAnTuerAngekommen++;
            NoteEvent(ziel, $"{EinheitenWort(u)} mit der Bahn angekommen");
        }
    }
}
