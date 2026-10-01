namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--basis-verlegung-check</c> — der Pruefstand zu bug-372 (30.09.2026,
/// berichte/createconvoy-basis-fable.md §6).
///
/// <code>
///   Basis:    Fahrzeug einlagern, Tempo := 2 (Plasma), Basisfenster
///             »Transportieren« auf die Zeile, Zielgebaeude klicken
///             Soll: Abfahrt und Ankunft Tempo = Entwurf (0x43C1BC)
///             --ausdock-tempo-alt: bleibt 2 · --basis-verlegung-alt: faehrt nicht
///   Bahnhof:  dasselbe aus einem Bahnhof (NULLMODELL, 0x43C430)
///             Soll: bleibt 2 — sonst schreibt unser Code das Tempo zu breit
/// </code>
/// <para>⚠ Die Einheiten sind ECHTE Karteneinheiten, eingelagert ueber
/// <c>Einfahren</c> — dieselbe Garage, die der Spieler fuellt (Do-Not-Repeat
/// 28.09.: keine Kruecke, die das Abfragefeld selbst setzt).</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _bvCheck;               // 0 = starten, 1 = auf Ankunft warten, -1 = fertig
    private int _bvTakt0;
    private readonly List<(string Wo, int Ui, int Entwurf, int Abfahrt, int Soll)> _bvFaelle = new();

    private const int BvGelaehmt = 2, BvHoechstTakte = 30000;

    private void PollBasisVerlegungCheck()
    {
        if (_bvCheck < 0 || _nav == null) return;
        if (_bvCheck == 0) { BvStart(); return; }

        bool alleDa = true;
        foreach (var f in _bvFaelle)
        {
            var u = _entities[f.Ui];
            if (!u.Dead && (u.Ukol != 0 || u.InGebaeude != null || _ankunftTuer.Exists(a => ReferenceEquals(a.U, u))))
                alleDa = false;
        }
        int dt = _taktNr - _bvTakt0;
        if (!alleDa && dt < BvHoechstTakte) return;

        bool ok = true;
        foreach (var f in _bvFaelle)
        {
            var u = _entities[f.Ui];
            bool da = !u.Dead && u.Ukol == 0 && u.InGebaeude == null;
            bool abOk = f.Abfahrt == f.Soll, anOk = !da || u.Speed == f.Soll;
            ok &= abOk && anOk;
            GD.Print($"basis-verlegung-check: {f.Wo}: Entwurf {f.Entwurf}, gelaehmt {BvGelaehmt} -> "
                   + $"Abfahrt {f.Abfahrt}, Ankunft {(da ? u.Speed.ToString() : "nicht angekommen")} "
                   + $"(Soll {f.Soll}) {(abOk && anOk ? "OK" : "ABWEICHUNG")}");
        }
        GD.Print($"basis-verlegung-check: {dt} Takte, an der Tuer angekommen {BahnAnTuerAngekommen}, "
               + $"aus der Basis verlegt {BahnVerlegtAusBasis}");
        GD.Print($"basis-verlegung-check: bug-372 {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        _bvCheck = -1;
    }

    private void BvStart()
    {
        _bvCheck = -1;
        var genommen = new HashSet<int>();

        // --- Basis ---------------------------------------------------------
        int basis = BvGebaeude(1, out int zielB);
        if (basis < 0)
        {
            GD.Print($"basis-verlegung-check: keine Basis mit Bahnweg zu einem anderen Gebaeude "
                   + $"({_railNodes.Count} Knoten)");
            foreach (var q in _entities)
                if (q.IsBuilding && !q.IsProp && !q.Dead && q.BType == 1)
                {
                    int kn = NodeOfBuilding(q.Slot);
                    GD.Print($"   Basis Platz {q.Slot} ({q.Col},{q.Row}) Besitzer {q.Owner} "
                           + $"Knoten {kn} Tueren {q.DoorCells.Count}");
                    if (kn >= 0 && _railNodes[kn].Links is { } links)
                        foreach (int ls in links)
                            if (ls >= 0 && _netzLinien.TryGetValue(ls, out var l))
                            {
                                int o = l.Node1 == kn ? l.Node2 : l.Node1;
                                bool da = _railNodes.TryGetValue(o, out var on);
                                int ob = da ? on.Building : -1;
                                int oi = EntityOfSlot(ob);
                                GD.Print($"      Linie {ls}: Used {l.Used} Faze {l.Faze} -> Knoten {o} "
                                       + $"Typ {(da ? on.Type : -1)} Platz {ob} "
                                       + $"Besitzer {(oi < 0 ? -1 : _entities[oi].Owner)}");
                            }
                }
            return;
        }
        int ub = BvFahrzeug(genommen);
        if (ub < 0) { GD.Print("basis-verlegung-check: kein Fahrzeug mit Entwurfstempo > 2"); return; }
        var b = _entities[basis];
        var u = _entities[ub];
        int entwurf = EntwurfsTempo(u)!.Value;
        Einfahren(b, ub, u);
        if (!b.Garage.Contains(u)) { GD.Print("basis-verlegung-check: Basis voll, Einheit nicht eingelagert"); return; }
        u.Speed = BvGelaehmt;

        // Der Weg des Spielers: Basis waehlen, Zeile, Knopf, Zielklick.
        _selected = basis;
        // 01.10.2026: erst ohne markierte Zeile — Original @0x44A301 meldet den
        // (falschen) Text 0x4FBBDC und wartet auf KEIN Ziel.
        TransportArmFromPanel(-1);
        string sollLeer = BasisOhneWahlAlt ? "nichts gewaehlt"
                                           : "Es besteht keine Verbindung zu diesem Gebaeude";
        GD.Print($"basis-verlegung-check: ohne Zeile -> »{_order}«, wartet={TransportArmed} "
               + (_order == sollLeer && !TransportArmed ? "BESTANDEN" : "DURCHGEFALLEN"));
        TransportArmFromPanel(b.Garage.IndexOf(u));
        GD.Print($"basis-verlegung-check: Basis Platz {b.Slot}, {EinheitenWort(u)} eingelagert, Knopf -> "
               + $"»{_order}«");
        if (_transportEinheit >= 0) TransportZielGewaehlt(zielB);
        else _transportWartet = -1;
        int sollB = BasisVerlegungAlt || AusdockTempoAlt ? BvGelaehmt : entwurf;
        // --basis-verlegung-alt: sie faehrt gar nicht los; Abfahrt = Tempo in der Garage.
        _bvFaelle.Add(("Basis", ub, entwurf, u.Speed, sollB));
        GD.Print($"basis-verlegung-check: Basis -> {BuildingTypeName(_entities[zielB].BType)} "
               + $"Platz {_entities[zielB].Slot}: »{_order}«, im Zug {(u.Ukol == UkolImZug)}");
        if (BasisVerlegungAlt)
        {
            bool bleibt = b.Garage.Contains(u);
            GD.Print($"basis-verlegung-check: [--basis-verlegung-alt] Einheit bleibt in der Basis: {bleibt} "
                   + $"{(bleibt ? "OK" : "ABWEICHUNG")}");
            GD.Print($"basis-verlegung-check: bug-372 {(bleibt ? "BESTANDEN" : "DURCHGEFALLEN")}");
            return;
        }

        // --- Bahnhof (Nullmodell) -------------------------------------------
        int bhf = BvGebaeude(6, out int zielH);
        if (bhf < 0) bhf = BvGebaeude(12, out zielH);
        int uh = bhf < 0 ? -1 : BvFahrzeug(genommen);
        if (uh >= 0)
        {
            var h = _entities[bhf];
            var v = _entities[uh];
            int ev = EntwurfsTempo(v)!.Value;
            Einfahren(h, uh, v);
            if (h.Garage.Contains(v))
            {
                v.Speed = BvGelaehmt;
                VerlegePerBahn(h, new List<int> { uh }, zielH);
                _bvFaelle.Add(("Bahnhof (Nullmodell, 0x43C430)", uh, ev, v.Speed, BvGelaehmt));
            }
        }
        else GD.Print("basis-verlegung-check: kein Bahnhof mit Bahnweg — Nullmodell entfaellt");

        _bvTakt0 = _taktNr;
        _bvCheck = 1;
    }

    /// <summary>Ein Gebaeude des Typs mit Bahnanschluss und einem Weg zu irgendeinem
    /// anderen Gebaeude. Beide gehen an den Spieler.</summary>
    private int BvGebaeude(int typ, out int ziel)
    {
        ziel = -1;
        Simulation.RailNetwork.OwnerOf = slot =>
        { int i = EntityOfSlot(slot); return i < 0 ? -1 : _entities[i].Owner; };
        for (int i = 0; i < _entities.Count; i++)
        {
            var q = _entities[i];
            if (!q.IsBuilding || q.IsProp || q.Dead || q.BType != typ || q.DoorCells.Count == 0) continue;
            int a = NodeOfBuilding(q.Slot);
            if (a < 0) continue;
            for (int j = 0; j < _entities.Count; j++)
            {
                var z = _entities[j];
                if (j == i || !z.IsBuilding || z.IsProp || z.Dead || z.DoorCells.Count == 0) continue;
                int bn = NodeOfBuilding(z.Slot);
                if (bn < 0 || bn == a) continue;
                // ⚠ Quelle UND Ziel gehen an den Spieler — die Wegsuche laeuft nur
                // ueber eigene Knoten (auf K21 haengt die Basis an Feldbahnhof 57,
                // der zu Beginn niemandem gehoert). Pruefstandsetzung, gemeldet.
                int altQ = q.Owner, altZ = z.Owner;
                q.Owner = q.Team = ViewPlayer;
                z.Owner = z.Team = ViewPlayer;
                if (Simulation.RailNetwork.FindRoute(_railNodes, _netzLinien, a, bn, q.Owner,
                        Simulation.RailNetwork.CampaignRules) != null)
                {
                    GD.Print($"basis-verlegung-check: Quelle {BuildingTypeName(q.BType)} Platz {q.Slot} "
                           + $"(Besitzer war {altQ}), Ziel {BuildingTypeName(z.BType)} Platz {z.Slot} "
                           + $"(Besitzer war {altZ}) -> Spieler {ViewPlayer}");
                    ziel = j; return i;
                }
                q.Owner = q.Team = altQ;
                z.Owner = z.Team = altZ;
            }
        }
        return -1;
    }

    private int BvFahrzeug(HashSet<int> genommen)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (genommen.Contains(i) || u.IsBuilding || u.IsProp || u.Dead || !u.Mobile) continue;
            if (u.Move != Simulation.NavGrid.MoveClass.Vehicle || u.InGebaeude != null) continue;
            int alt = u.Owner;
            u.Owner = u.Team = ViewPlayer;
            if (EntwurfsTempo(u) is > BvGelaehmt) { genommen.Add(i); return i; }
            u.Owner = u.Team = alt;
        }
        return -1;
    }
}
