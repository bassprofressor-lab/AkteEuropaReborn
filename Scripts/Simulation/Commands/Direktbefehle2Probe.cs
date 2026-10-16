namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--net-direktbefehle2</c> — der Netzprüfstand des zweiten Pakets: reihum
/// jede Eingabe dieses Pakets über DENSELBEN Absender, den Taste, Knopf oder
/// Kartenklick nehmen (17.10.2026, berichte/desync2-opus.md). Was hier vorab
/// angewählt wird, ist Auswahl — örtlich wie ein Klick.
///
/// <para>⚠ Der Spielstand-Teil schreibt <c>GAME.005</c> in den Benutzerordner
/// (Platz 5, im Lauf von beiden Prozessen gleich) — nach dem Lauf löschen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Wie viele Schritte die Reihe hat.</summary>
    public const int NetzProbe2Arten = 34;

    /// <summary>Speicherplatz des Prüfstands.</summary>
    public const int NetzProbe2Platz = 5;

    private int EigenesGebaeude(int owner, System.Func<Entity, bool> art)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding && !e.Dead && !e.IsProp && e.Owner == owner && art(e)) return i;
        }
        return -1;
    }

    private void Waehle(int i)
    {
        _sel.Clear(); _selAir = -1;
        if (i >= 0) _sel.Add(i);
        _selected = i;
    }

    private Special? EigenesFlugzeug(int owner, System.Func<Special, bool> f)
    {
        foreach (var a in _special)
            if (!a.Dead && a.Owner == owner && !a.Absturz && f(a)) return a;
        return null;
    }

    /// <summary>Einen Schritt der Reihe ausführen; gibt zurück, was geschah.</summary>
    /// <summary>Die Reihenfolge: Aussenden/Verwerten/Bahn gleich nach dem Bau,
    /// solange die frische Einheit noch in der Garage steht.</summary>
    private static readonly int[] NetzProbe2Reihe =
        { 0, 8, 12, 1, 9, 10, 2, 3, 4, 5, 6, 7, 11, 13, 14, 15, 16, 17, 18, 19, 20, 21,
          23, 22, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33 };

    internal string NetzProbe2(int schritt, int me, int nr)
    {
        int art = NetzProbe2Reihe[schritt % NetzProbe2Reihe.Length];
        // Die Tasten bleiben gedrückt, bis Schritt 14 bzw. 16 sie loslässt — die
        // Übernahme wirkt erst nach dem Vorlauf.
        int basis = EigenesGebaeude(me, e => e.BType == 1);
        int flug = EigenesGebaeude(me, e => e.BType == 9);
        switch (art)
        {
            case 0: if (basis < 0) return "keine Basis"; Waehle(basis); PostProduzieren(); return "B Basis (503)";
            case 1: if (basis < 0) return "keine Basis"; Waehle(basis); PostBuildPanelPick(nr % 3); return "Basisfenster Zeile (503)";
            case 2: if (flug < 0) return "kein Flughafen"; Waehle(flug); PostBuildPanelPick(nr % 2); return "Flughafen kaufen (501)";
            case 3:
            {
                int p = -1;
                for (int i = 0; i < _entities.Count; i++) if (IsSupplyDepot(_entities[i]) && !_entities[i].Dead) { p = i; break; }
                if (p < 0) return "kein Nachschubposten";
                Waehle(p); PostBuildPanelPick(nr % 2); return "Posten Heli (527)";
            }
            case 4:
            {
                int d = EigenesGebaeude(me, IsDock);
                if (d < 0) return "keine Werft"; Waehle(d); PostBuildPanelPick(0); return "Werft (508)";
            }
            case 5:
                _sel.Clear();
                for (int i = 0; i < _entities.Count; i++)
                    if (_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].Owner == me) _sel.Add(i);
                PostReparaturTaste(); return $"Taste K ({_sel.Count} Gebaeude)";
            case 6:
            case 7:
            {
                int m = EigenesGebaeude(me, e => e.BType is 10 or 9);
                if (m < 0) return "keine Mine/kein Flughafen";
                Waehle(m);
                if (art == 6) PostBuildingWindowStart(); else PostBuildingWindowRepair();
                return art == 6 ? "Fenster Start (517/524)" : "Fenster Reparatur (522/520)";
            }
            case 8:
            case 9:
            {
                int b = EigenesGebaeude(me, e => e.BType is 1 && (e.Garage.Count + e.Depot.Count) > 0);
                if (b < 0) return Einfahren(me, e => e.BType == 1, "Basis");
                Waehle(b);
                var e2 = _entities[b];
                int rows = e2.Garage.Count + e2.Depot.Count;
                if (art == 8) PostSendOutFromPanel(nr % rows); else PostRecycleFromPanel(rows - 1);
                return art == 8 ? "Aussenden (504/2007)" : "Verwerten (506/2007)";
            }
            case 10:
            {
                int d = EigenesGebaeude(me, e => e.BType is 5 or 6 or 12 && e.Garage.Count > 0);
                if (d < 0) return Einfahren(me, e => e.BType is 5 or 6 or 12, "Depot");
                Waehle(d);
                var l = new List<int>();
                foreach (var u in _entities[d].Garage) l.Add(_entities.IndexOf(u));
                PostDepotAussenden(l); return $"Depot aussenden ({l.Count}x 504)";
            }
            case 11:
                if (_railLines.Count == 0) return "keine Bahnlinie";
                PostRailSetFlag(_railLines[nr % _railLines.Count].Slot, nr % 4, nr % 3);
                return "Bahnschalter (500)";
            case 12:
            {
                int q = EigenesGebaeude(me, e => e.BType is 1 or 6 or 12 && e.Garage.Count > 0);
                if (q < 0) return "keine Quelle mit Einheiten";
                int z = EigenesGebaeude(me, e => !ReferenceEquals(e, _entities[q]) && NodeOfBuilding(e.Slot) >= 0);
                if (z < 0) return "kein Ziel mit Bahnanschluss";
                Waehle(q);
                int n = PostTransportierenAusBahnhof(new List<int> { _entities.IndexOf(_entities[q].Garage[0]) }, z);
                return $"Bahnverlegung ({n}x 518)";
            }
            case 13:
            {
                // die LETZTE fahrbereite: die ersten sechs fährt der Prüfstand
                var u = CommandCheckMobileUnits(20);
                if (u.Count < 7) return "zu wenig Einheiten";
                int h = u[^1];
                Waehle(h);
                if (HandsteuerungIdx != h) PostHandsteuerungUmschalten();
                HandTasteRechts = (nr & 1) == 0; HandTasteRunter = (nr & 1) != 0;
                HandMausKarte = _entities[h].Pos + new Vector2(40 * ((nr % 3) - 1), 20);
                return "Hand Boden an (4, dann 1/2 je Takt)";
            }
            case 14:
            {
                HandTasteLinks = HandTasteRechts = HandTasteHoch = HandTasteRunter = false;
                if (HandsteuerungIdx < 0) return "keine Handsteuerung";
                var e = _entities[HandsteuerungIdx];
                PostHandKlick(CellCenter(e.Col + 3, e.Row));
                HandMausKarte = null;
                PostHandBodenAbgeben();
                return "Hand Boden Schuss + aus (9, 5)";
            }
            case 15:
            {
                var a = EigenesFlugzeug(me, x => !x.Stored && !x.IsSupply);
                if (a == null) return "kein fliegendes Flugzeug";
                _sel.Clear(); _selected = -1; _selAir = _special.IndexOf(a);
                if (HandLuftIdx < 0) { PostHandLuftTasteH(); return "Hand Luft an (4)"; }
                HandTasteLinks = true; HandTasteSteigen = (nr & 1) == 0; HandTasteHoch = true;
                HandTasteSchuss = (nr % 3) == 0;
                return "Hand Luft Tasten (8 je Takt)";
            }
            case 16:
                if (HandLuftIdx < 0) return "keine Handsteuerung Luft";
                if (nr % 3 != 2) return "Hand Luft fliegt weiter";
                HandTasteLinks = HandTasteHoch = HandTasteSteigen = HandTasteSchuss = false;
                if ((nr & 1) == 0) PostHandLuftBeenden("Probe"); else PostHandLuftTasteH();
                return (nr & 1) == 0 ? "Hand Luft aus (30)" : "Hand Luft aus (5)";
            case 17:
            {
                if (nr % 3 != 0) return "Selbstzerstoerung ausgesetzt";
                var u = CommandCheckMobileUnits(20);
                if (u.Count < 8) return "zu wenig Einheiten";
                Waehle(u[^2]); return PostSelbstzerstoerung() + " (13)";
            }
            case 18:
            {
                int p = ErsterPionier();
                if (p < 0) return "kein Pionier";
                var e = _entities[p];
                PostPionier(p, e.Col + 1, e.Row, nr % 2 == 0 ? OrderBruecke : OrderMole);
                return "Pionier (16)";
            }
            case 19:
                if (flug < 0) return "kein Flughafen"; PostPatrouilleKippen(_entities[flug].Slot); return "Patrouille (537)";
            case 20:
            {
                if (flug < 0) return "kein Flughafen";
                var a = EigenesFlugzeug(me, x => x.IstBomber && x.HomeSlot == _entities[flug].Slot);
                if (a == null) return "kein Bomber";
                PostBombeWechseln(a.Slot, _entities[flug].Slot); return "Bombe (534)";
            }
            case 21:
            {
                var a = EigenesFlugzeug(me, x => x.HomeSlot >= 0 && !x.IsSupply);
                if (a == null) return "kein Flugzeug";
                PostStaffelUmschalten(a.Slot, 0); return "Staffelmarke (2008)";
            }
            case 22:
            {
                if (flug < 0) return "kein Flughafen";
                var f = _entities[flug];
                int n = PostStaffelStarten(f.Slot, 0xFF, -1, f.Col + 6, f.Row + 4);
                return $"Zielwahl ({n}x 502)";
            }
            case 23:
            {
                if (flug < 0) return "kein Flughafen";
                var a = EigenesFlugzeug(me, x => x.Stored && x.HomeSlot == _entities[flug].Slot);
                if (a == null)
                {
                    var f = EigenesFlugzeug(me, x => !x.Stored && !x.IsSupply);
                    if (f == null) return "kein Flugzeug";
                    Emit(Simulation.Commands.CommandRecord.Make(OpProbeHerrichten, (byte)me, 2, (short)f.Slot, (short)flug));
                    return "kein Flugzeug im Hangar — Pruefstand lagert eines ein (2099)";
                }
                if (nr % 2 != 0) return "Verwerten ausgesetzt";
                PostRecycleAmFlughafen(_entities[flug].Slot, a.Slot); return "Flugzeug verwerten (535)";
            }
            case 24:
            {
                int w = -1;
                for (int i = 0; i < _entities.Count; i++)
                    if (!_entities[i].Dead && _entities[i].Owner == me && IstTransporter(_entities[i])) { w = i; break; }
                if (w < 0) return "kein Transporter";
                Waehle(w);
                if (!PostRouteFensterOeffnen()) return "Routenfenster zu: " + RouteNote;
                var r = RouteFensterSatz();
                if (r != null && r.Ziel >= 0 && System.Array.Exists(r.Quelle, q => q >= 0))
                    return "Route (514) — schon gesetzt";
                int ziel = basis, quelle = EigenesGebaeude(me, e => e.BType is 10 or 2 or 3 or 4);
                if (ziel < 0 || quelle < 0) return "Route: kein Ziel/keine Quelle";
                // wie zwei Klicks: Nach (Ziel), dann Von (Quelle)
                RouteWahlModus = 2;
                bool a1 = PostRouteKlickAufGebaeude(ziel);
                bool a2 = PostRouteKlickAufGebaeude(quelle);
                return $"Route (514, 512) Klicks {a1}/{a2} {RouteNote}";
            }
            case 25:
                if (RouteFensterWagen < 0) return "kein Routenfenster";
                return PostRouteStartKnopf() ? "Route Start (513)" : "Route Start: " + RouteNote;
            case 26:
            {
                if (_designs == null || _designs.Count == 0) return "keine Entwuerfe";
                var d = _designs[nr % _designs.Count];
                PostEntwurf(d.Propulsion, d.Equip, d.Weapon, $"Netz{nr}");
                return "Entwurf (505)";
            }
            case 27: case 28: PostPause(); return art == 27 ? "Pause an (993)" : "Pause aus (993)";
            case 29: PostSpeichern(NetzProbe2Platz, $"Netz {nr}"); return "Speichern (991)";
            case 30: PostLaden(NetzProbe2Platz); return "Laden (990)";
            case 31: PostFinishGrace(); return "Schonfrist (2009)";
            case 32:
            {
                int m = -1;
                for (int i = 0; i < _entities.Count; i++) if (_entities[i].IsBuilding && _entities[i].BType == 17 && !_entities[i].Dead) { m = i; break; }
                if (m < 0) return "kein Markt";
                if (MarketShelf().Count == 0)
                {
                    // Das Regal füllt ein Verkauf (529, schon im Ring) — im Gefecht sofort.
                    var u = CommandCheckMobileUnits(20);
                    if (u.Count < 8) return "Markt ohne Angebot";
                    return $"Markt ohne Angebot — Verkauf (529) zu {PostSell(u[^4])}";
                }
                Waehle(m);
                return PostProduktionAn(m, nr % MarketShelf().Count) ? "Markt (530)" : "Markt: " + _order;
            }
            default:
                if (RouteFensterWagen < 0) return "kein Routenfenster";
                PostRouteLeeren(); RouteFensterSchliessen(); return "Route leeren (5x 512)";
        }
    }

    /// <summary>Hat das Gebäude keine Zeilen, fährt eine eigene Einheit hinein —
    /// über PostMove auf die Türzelle (Befehl 3, wie ein Rechtsklick).</summary>
    private string Einfahren(int me, System.Func<Entity, bool> art, string was)
    {
        int b = EigenesGebaeude(me, art);
        if (b < 0) return $"kein Gebaeude ({was})";
        var u = CommandCheckMobileUnits(20);
        if (u.Count < 8) return "zu wenig Einheiten zum Einfahren";
        // Der Prüfstand richtet her — über den Ring (2099), auf beiden Seiten gleich.
        Emit(Simulation.Commands.CommandRecord.Make(OpProbeHerrichten, (byte)me, 1, (short)u[^3], (short)b));
        return $"{was} leer — Pruefstand stellt eine Einheit unter (2099)";
    }

    /// <summary>⚠ NUR PRÜFSTAND: 2099 »herrichten« — wirkt nur, solange
    /// <c>--net-direktbefehle2</c> läuft (<see cref="NetzProbe2An"/>). P1 = Art
    /// (1 Einheit P2 in Gebäude P3 einstellen, 2 Flugzeug P2 im Flughafen P3
    /// einlagern).</summary>
    public const short OpProbeHerrichten = 2099;
    public static bool NetzProbe2An;

    private bool ApplyProbeHerrichten(in Simulation.Commands.CommandRecord c)
    {
        if (!NetzProbe2An) return false;
        int b = c.P3;
        if (b < 0 || b >= _entities.Count || !_entities[b].IsBuilding || _entities[b].Owner != c.Player) return false;
        var g = _entities[b];
        if (c.P1 == 1)
        {
            int ui = c.P2;
            if (!EigenesObjekt(c, ui) || _entities[ui].IsBuilding || _entities[ui].InGebaeude != null) return false;
            int vor = g.Garage.Count;
            Einfahren(g, ui, _entities[ui]);
            return g.Garage.Count > vor;
        }
        if (c.P1 == 2)
        {
            var a = FlugzeugAufPlatz(c.P2);
            if (a == null || a.Owner != c.Player || a.Stored || a.Absturz || g.BType != 9) return false;
            a.Hand = false; a.Heimkehr = false; a.Target = -1; a.Goal = null;
            a.Stored = true; a.HomeSlot = g.Slot;
            g.Hangar ??= new List<int>();
            if (!g.Hangar.Contains(a.Slot)) g.Hangar.Add(a.Slot);
            return true;
        }
        return false;
    }

    /// <summary>Was diese Seite hat — für die Kopfzeile des Prüfstands.</summary>
    internal string NetzProbe2Bestand(int me)
    {
        var arten = new SortedDictionary<int, int>();
        foreach (var e in _entities)
            if (e.IsBuilding && !e.Dead && e.Owner == me) { arten.TryGetValue(e.BType, out int k); arten[e.BType] = k + 1; }
        int markt = 0, posten = 0, transp = 0, pion = 0;
        foreach (var e in _entities)
        {
            if (e.Dead) continue;
            if (e.IsBuilding && e.BType == 17) markt++;
            if (IsSupplyDepot(e)) posten++;
            if (e.Owner == me && IstTransporter(e)) transp++;
            if (e.Owner == me && !e.IsBuilding && IstPionier(e)) pion++;
        }
        var sb = new System.Text.StringBuilder("Gebaeude je Art:");
        foreach (var kv in arten) sb.Append($" {kv.Key}x{kv.Value}");
        sb.Append($"; Markt {markt}, Posten {posten}, Bahnlinien {_railLines.Count}, Transporter {transp}, Pioniere {pion}");
        return sb.ToString();
    }
}

public partial class MapEntityLayer
{
    /// <summary><c>--pruefsumme-voll</c>: der Zustand des zweiten Pakets in die
    /// Prüfsumme (siehe <see cref="Simulation.Determinism.PruefsummeVoll"/>).</summary>
    private void DeterminismVollAnhaengen(ref Simulation.Determinism.Hasher h)
    {
        foreach (var e in _entities)
        {
            if (e.IsProp) continue;
            h.Add(e.Garage.Count); h.Add(e.Depot.Count); h.Add(e.BuildQueue.Count);
            h.Add(e.Patrouille); h.Add(e.TurmWunsch); h.Add(e.Bauart); h.Add(e.Ukol);
        }
        foreach (var a in _special)
        {
            h.Add(a.Staffel); h.Add(a.Waffenart); h.Add(a.Hand); h.Add(a.Dir);
            h.Add(a.Stufe); h.Add(a.Alt); h.Add(a.Heimkehr);
        }
        foreach (var l in _railLines) { h.Add(l.Slot); foreach (byte m in l.Mode) h.Add(m); }
        foreach (var r in _routen)
        {
            h.Add(r.Ziel); h.Add(r.Gestartet);
            foreach (int q in r.Quelle) h.Add(q);
        }
        h.Add(_designs?.Count ?? -1);
        h.Add(NetzPause);
        h.Add(GameSpeed);
    }
}
