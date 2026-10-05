namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <b><c>--depotauslass-check</c> — bug-456/457 (KayelGee R4, Meldungen 20 und
/// 40).</b> Ein eigenes Depot (K10), darin eine eigene Einheit, ausgesandt
/// (UKOL 51, wie Befehl 504 @0x410441).
///
/// <list type="number">
///   <item><b>A — Zelle unter der Tuer belegt</b> von einer eigenen Einheit, die
///   sich nicht wegschicken laesst (unbeweglich gemacht): die Ausgesandte bleibt
///   in der Liste, auf der Tuer steht NIEMAND (Soll; Nullmodell
///   <c>--auslass-nur-tuer-alt</c>: sie steht auf der Tuer fest).</item>
///   <item><b>B — »wait B«</b>: eine eigene, untaetige Einheit auf der Tuerzelle;
///   binnen weniger Takte hat sie einen Weg 3..5 Zeilen nach Sueden, und die
///   Ausgesandte kommt heraus (Soll; Nullmodell <c>--wait-ab-aus</c>: der
///   Blockierer steht, die Ausgesandte bleibt drin).</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    public string DepotauslassCheckLine()
    {
        var sb = new StringBuilder("depotauslass-check\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();
        int bi = DepotIndex();
        if (bi < 0) return sb.Append("  kein eigenes Depot (K10 nehmen) — NICHT GEMESSEN").ToString();
        var b = _entities[bi];
        int tx = b.Col + b.DoorCol, ty = b.Row + b.DoorRow;
        sb.Append($"  Depot Platz {b.Slot} bei ({b.Col},{b.Row}), Tuer ({tx},{ty})\n");

        // drei eigene Fahrzeuge: eines ins Depot, zwei als Blockierer
        var fz = new List<int>();
        for (int i = 0; i < _entities.Count && fz.Count < 3; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != ViewPlayer || e.Infantry >= 0) continue;
            if (Untergestellt(e) || e.GameUnitType != 0) continue;
            fz.Add(i);
        }
        if (fz.Count < 3) return sb.Append("  weniger als drei eigene Fahrzeuge — NICHT GEMESSEN").ToString();
        // Tuer und Zelle darunter vorher freimachen (fremde Belegung wegstellen)
        foreach (var (c, r) in new[] { (tx, ty), (tx, ty + 1) })
        {
            int o = _nav.OccupantAt(c, r);
            if (o >= 0 && o < _entities.Count && !_entities[o].IsBuilding && !fz.Contains(o))
                return sb.Append($"  Tuer/Zelle darunter belegt von Satz {o} — NICHT GEMESSEN").ToString();
        }

        // die Einheit ins Depot
        int ui = fz[0];
        var u = _entities[ui];
        _nav.ClearOccupant(u.Col, u.Row, ui);
        if (u.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, ui);
        u.Reserved = null; u.Path = null; u.Orders.Clear(); u.Target = -1;
        u.Ukol = UkolUntergestellt; u.InGebaeude = b;
        b.Garage.Add(u);
        u.Col = tx; u.Row = ty;

        bool ok = true;

        // ---- A: Zelle darunter von einer unbeweglichen eigenen Einheit belegt ----
        var blockA = _entities[fz[1]];
        AwStelle(fz[1], new Vector2I(tx, ty + 1));
        bool mobilVor = blockA.Mobile;
        blockA.Mobile = false;                  // laesst sich nicht wegschicken
        u.Ukol = UkolVerlaesst;                 // Befehl 504
        for (int t = 0; t < 20; t++) SimTickFuerProbe();
        bool inListeA = b.Garage.Contains(u);
        int aufTuerA = _nav.OccupantAt(tx, ty);
        bool aOk = inListeA && aufTuerA != ui;
        ok &= aOk;
        sb.Append($"  A Zelle darunter belegt: Ausgesandte in der Liste {inListeA}, auf der Tuer steht "
                + $"{(aufTuerA < 0 ? "niemand" : aufTuerA == ui ? "die Ausgesandte (steckt fest)" : $"Satz {aufTuerA}")}"
                + $"{(AuslassNurTuerAlt ? " (Nullmodell --auslass-nur-tuer-alt)" : "")}{(aOk ? "" : "   ⚠")}\n");
        blockA.Mobile = mobilVor;
        // A aufraeumen: Blockierer weg, Ausgesandte zurueck in die Liste
        AwStelle(fz[1], new Vector2I(blockA.Col + 6, blockA.Row));
        if (!b.Garage.Contains(u))
        {
            int ux = _entities.IndexOf(u);
            _nav.ClearOccupant(u.Col, u.Row, ux);
            u.Path = null; u.InGebaeude = b; b.Garage.Add(u);
        }
        u.Ukol = UkolUntergestellt; u.Col = tx; u.Row = ty;
        for (int t = 0; t < 3; t++) SimTickFuerProbe();

        // ---- B: »wait B« — eine eigene, untaetige Einheit auf der Tuer ----
        var blockB = _entities[fz[2]];
        AwStelle(fz[2], new Vector2I(tx, ty));
        blockB.Ukol = UkolFrei; blockB.Path = null; blockB.Target = -1;
        int wegVor = WaitAbWeggeschickt;
        u.Ukol = UkolVerlaesst;
        Vector2I? zielB = null;
        int takteBisHeraus = -1;
        for (int t = 0; t < 120; t++)
        {
            SimTickFuerProbe();
            if (zielB == null && blockB.Path is { Count: > 0 }) zielB = blockB.Goal;
            if (takteBisHeraus < 0 && !b.Garage.Contains(u)) takteBisHeraus = t + 1;
        }
        int weg = WaitAbWeggeschickt - wegVor;
        bool zielOk = zielB is { } g && g.Y - ty >= 2 && g.Y - ty <= 5 && System.Math.Abs(g.X - tx) <= 2;
        bool bOk = weg >= 1 && zielOk && takteBisHeraus > 0;
        ok &= bOk;
        sb.Append($"  B wait B: Blockierer weggeschickt {weg}x, Ziel {(zielB?.ToString() ?? "keins")} "
                + $"(Soll y = Tuer+3..5, x = Tuer±2), Ausgesandte heraus nach {takteBisHeraus} Takten"
                + $"{(WaitAbAus ? " (Nullmodell --wait-ab-aus)" : "")}{(bOk ? "" : "   ⚠")}\n");
        sb.Append(ok ? "depotauslass-check: BESTANDEN" : "depotauslass-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}

/// <summary>
/// <b><c>--vorplatz-check</c> — bug-458 (KayelGee R4, Meldung 26).</b> Eine
/// fremde Basis mit Tuer; drei eigene Fahrzeuge in das 5 × 4 vor der Tuer;
/// Einnahmeabschluss (<c>CaptureDone</c>); dann 60 Takte. Soll: alle drei haben
/// sofort einen Weg sechs Zeilen nach Sueden, keine ist danach eingefahren.
/// Nullmodell <c>--vorplatz-raeumung-aus</c>: 0 losgeschickt.
/// </summary>
public partial class MapEntityLayer
{
    public string VorplatzCheckLine()
    {
        var sb = new System.Text.StringBuilder("vorplatz-check\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();
        int bi = -1;
        for (int i = 0; i < _entities.Count && bi < 0; i++)
        {
            var b = _entities[i];
            if (b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 1 && b.DoorCells.Count > 0
                && b.Owner != ViewPlayer && b.Built != 0) bi = i;
        }
        if (bi < 0) return sb.Append("  keine fremde Basis mit Tuer (K2 nehmen) — NICHT GEMESSEN").ToString();
        var basis = _entities[bi];
        int tx = basis.Col + basis.DoorCol, ty = basis.Row + basis.DoorRow;
        var fz = new System.Collections.Generic.List<int>();
        for (int i = 0; i < _entities.Count && fz.Count < 3; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != ViewPlayer || e.Infantry >= 0) continue;
            if (Untergestellt(e) || e.GameUnitType != 0) continue;
            fz.Add(i);
        }
        if (fz.Count < 3) return sb.Append("  weniger als drei eigene Fahrzeuge — NICHT GEMESSEN").ToString();
        var plaetze = new[] { new Godot.Vector2I(tx, ty + 1), new Godot.Vector2I(tx - 1, ty + 2), new Godot.Vector2I(tx + 1, ty + 2) };
        for (int k = 0; k < 3; k++)
        {
            if (!_nav.IsFree(plaetze[k].X, plaetze[k].Y, _entities[fz[k]].Move, fz[k]))
                return sb.Append($"  Vorplatzzelle {plaetze[k]} nicht frei — NICHT GEMESSEN").ToString();
            AwStelle(fz[k], plaetze[k]);
        }
        int garageVor = basis.Garage.Count;
        CaptureDone(bi, basis, ViewPlayer);
        int richtig = 0;
        foreach (int k in fz)
        {
            var e = _entities[k];
            if (e.Path is { Count: > 0 } && e.Goal.Y == System.Math.Min(e.Row + 6, _nav.Height - 1)) richtig++;
        }
        for (int t = 0; t < 60; t++) SimTickFuerProbe();
        int drin = 0;
        foreach (int k in fz) if (basis.Garage.Contains(_entities[k]) || Untergestellt(_entities[k])) drin++;
        bool ok = richtig == 3 && drin == 0;
        sb.Append($"  Basis Platz {basis.Slot}, Tuer ({tx},{ty}); losgeschickt {VorplatzGeschickt} von {VorplatzGefunden}, "
                + $"Ziel sechs Zeilen suedlich {richtig}/3, nach 60 Takten eingefahren {drin} (Garage vorher {garageVor}, nachher {basis.Garage.Count})"
                + (VorplatzRaeumungAus ? " (Nullmodell --vorplatz-raeumung-aus)" : "") + "\n");
        sb.Append(ok ? "vorplatz-check: BESTANDEN" : "vorplatz-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}

/// <summary><b><c>--recycle-garage-check</c> — bug-459 (KayelGee R4, Meldung 21).</b>
/// Eine EINGEFAHRENE Einheit mit halbem Leben im Basisfenster verwerten; Lager
/// += Preis·50/100 je Teil wie 0x4B28E0. Nullmodell <c>--recycle-garage-alt</c>.</summary>
public partial class MapEntityLayer
{
    public string RecycleGarageCheckLine()
    {
        var sb = new StringBuilder("recycle-garage-check\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();
        bool ok = false;
        // ---- C: Recycle einer EINGEFAHRENEN Einheit (bug-459, Meldung 21) ----
        // Recycle gibt es nur im BASISfenster (Befehl 506, Treffer 7) — ein Gebaeude,
        // fuer das Producer() antwortet; sonst bleibt C ungeprueft.
        int rb = -1, selMerk = _selected;
        for (int i = 0; i < _entities.Count && rb < 0; i++)
        {
            var geb = _entities[i];
            if (!geb.IsBuilding || geb.Dead || geb.Owner != ViewPlayer || !Einfahrt_GarageTyp(geb)) continue;
            _selected = i;
            if (Producer() == geb) rb = i;
        }
        _selected = selMerk;
        if (rb < 0)
        {
            sb.Append("  C Recycle: kein eigenes Gebaeude mit Basisfenster — ungeprueft (K21 nehmen)\n");
        }
        else
        {
        var b = _entities[rb]; int bi = rb;
        if (b.Garage.Count == 0)
        {
            int vi = -1;
            for (int i = 0; i < _entities.Count && vi < 0; i++)
            {
                var e = _entities[i];
                if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Mobile && e.Owner == ViewPlayer && e.Infantry < 0
                    && e.GameUnitType == 0 && !Untergestellt(e)) vi = i;
            }
            if (vi < 0) return sb.Append("  kein eigenes Fahrzeug — NICHT GEMESSEN").ToString();
            var v = _entities[vi];
            _nav.ClearOccupant(v.Col, v.Row, vi);
            v.Path = null; v.Orders.Clear(); v.Target = -1; v.Ukol = UkolUntergestellt; v.InGebaeude = b;
            b.Garage.Add(v);
        }
        var rz = b.Garage[0];
        rz.Hp = rz.HpMax / 2;
        int pct = rz.HpMax > 0 ? 100 * rz.Hp / rz.HpMax : 0;
        int sollW = UI.UnitStatBook.WeaponPrice(rz.Comp0D) * pct / 100;
        int sollF = UI.UnitStatBook.ChassisPrice(rz.Comp0F) * pct / 100;
        int sollS = UI.UnitStatBook.EquipPrice(rz.Part) * pct / 100 + UI.UnitStatBook.EquipPrice(rz.Equipment) * pct / 100;
        int w0 = b.StockW, f0 = b.StockF, s0 = b.StockS, n0 = b.Garage.Count;
        int selVor = _selected;
        _selected = bi;
        RecycleFromPanel(0);
        _selected = selVor;
        int dw = b.StockW - w0, df = b.StockF - f0, ds = b.StockS - s0;
        bool cOk = b.Garage.Count == n0 - 1 && rz.Dead && dw == sollW && df == sollF && ds == sollS;
        ok = cOk;
        sb.Append($"  C Recycle eingefahren (Teile {rz.Comp0D}/{rz.Comp0F}/{rz.Part}/{rz.Equipment}, {pct} %): Lager +W{dw} +F{df} +S{ds} "
                + $"(Soll W{sollW} F{sollF} S{sollS}), Liste {n0} -> {b.Garage.Count}, »{_order}«"
                + (RecycleGarageAlt ? " (Nullmodell --recycle-garage-alt)" : "") + (cOk ? "" : "   ⚠") + "\n");
        }
        sb.Append(ok ? "recycle-garage-check: BESTANDEN" : "recycle-garage-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}

/// <summary>Helfer fuer <c>--depotreiter-check</c> (bug-461).</summary>
public partial class MapEntityLayer
{
    /// <summary>Das erste eigene Gebaeude mit Basisfenster und Garage, angewaehlt; sonst −1.</summary>
    public int ProbeBasisfensterWaehlen()
    {
        int merk = _selected;
        for (int i = 0; i < _entities.Count; i++)
        {
            var g = _entities[i];
            if (!g.IsBuilding || g.Dead || g.Owner != ViewPlayer || !Einfahrt_GarageTyp(g)) continue;
            _selected = i;
            if (Producer() == g) { UpdatePanel(); return i; }
        }
        _selected = merk;
        return -1;
    }
}
