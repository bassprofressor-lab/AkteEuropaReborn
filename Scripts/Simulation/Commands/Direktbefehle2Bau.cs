namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>PAKETE »produktion«, »reparatur«, »depot«, »bahn«, »verkauf« —
/// BASISFENSTER, FLUGHAFEN-, POSTEN-, WERFT- UND MARKTKAUF, REPARATUR, DEPOT,
/// GARAGE, BAHN ÜBER DEN RING</b> (17.10.2026, Direktbefehle2).
///
/// <para><b>Die Nummern des Originals</b> (gelesen 17.10.2026 in C, F
/// gepaart; Fensterklicktafel 0x44DD34):</para>
/// <code>
///   503  Einheit bauen     P1 Entwurf, P2 Spieler, P3 Gebäude   Art 6 »Bauen« 0x44A726 → 0x4B1840
///                          (F 0x4C32F7). Platz- und Teileprobe ÖRTLICH beim Absender.
///   501  Flugzeug kaufen   P1 Zeile, P2 Spieler, P3 Flughafen, P4 Art (Wächter)
///                          — Art 5 »Produzieren« 0x449E33 → spawn_aircraft 0x4B1380
///                          ⚠ SETZUNG der Felder: das Original trägt dort drei Wörter,
///                          deren Bedeutung nur für die KI-Seite gelesen ist.
///   508  Schiff bauen      P1 Zeile, P2 Spieler, P3 Werft (CommandOp.BuildShip)
///   527  Heli kaufen       P1 Posten, P2 Zeile 0 Sprit / 1 Munition, P3 Spieler
///                          — Art 31 0x44C358/0x44C402 → 0x4B2FB0 (F 0x4C35CE)
///   530  Marktkauf         schon gebaut (Marktfenster.cs); der Basisfensterweg zum
///                          Markt geht jetzt auch hindurch.
///   519/521/522/520, 511/517/524/525  Reparatur, Start/Halt — schon gebaut
///                          (PostBuildingJob); Taste K und Minen-/Flughafenfenster
///                          gingen bis heute daran vorbei.
///   504  Aussenden         P1 Einheit, P2 Gebäude — Basis 0x44A228, Depot 0x44C083
///   506  Verwerten         P1 Einheit — Art 6 Knopf 7 0x44A3C3 → 0x4B28E0 (F 0x4C336D)
///   518  Bahnverlegung     P1 Einheit, P2 Quelle, P3 Ziel — 0x449842 → CreateConvoy 0x4CEA90
///   500  Bahnschalter      P1 Linie, P2 Ware, P3 Wert — Fensterart 4
///   2007 UNSERE SETZUNG    Basisfensterzeile, die KEIN Einheitensatz ist (Entwurfs-
///                          nummer im Depot, Flugzeug im Hangar): P1 Gebäude, P2 Zeile,
///                          P3 Handlung 0 Aussenden / 1 Verwerten / 2 Transport, P4
///                          Wächter (Entwurfsnummer bzw. Flugzeugplatz), P5 Ziel.
///                          Unser Depot führt Entwurfsnummern, das Original Sätze.
/// </code>
///
/// <para>⚠ <b>Taste N</b> (<see cref="CycleBuildMenu"/>) bleibt ÖRTLICH — im
/// Original ist die gewählte Zeile ein Feld des FENSTERS (+0x18), nicht des
/// Gebäudes. Der Satz trägt darum die Entwurfsnummer bzw. Zeile selbst, und der
/// Behandler stellt <c>MenuIndex</c> nach dem Bau wieder her; was N verstellt,
/// kommt nie in den Zustand (und steht auch nicht in der Prüfsumme).</para>
///
/// <para>⚠ <b>Wächter</b>: jede Zeilennummer reist mit einer Kennung dessen, was
/// der Spieler sah (Entwurfsnummer, Einheit, Flugzeugplatz, Art). Steht im
/// Behandler etwas anderes an der Zeile, wird verworfen — beide Maschinen
/// gleich.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Unsere Nummer für eine Basisfensterzeile ohne Einheitensatz.</summary>
    public const short OpPanelzeile = 2007;

    // ================= PRODUKTION =============================================

    /// <summary>Taste B: je gewähltem Gebäude die Zeile, die N gewählt hat.</summary>
    public void PostProduzieren()
    {
        if (Alt2("produktion")) { ProduceFromSelection(); return; }
        _order = "";
        foreach (int i in AuswahlNachPlatz())
        {
            if (i < 0 || i >= _entities.Count) continue;
            PostProduktionAn(i, -1);
        }
    }

    /// <summary>Ein Klick auf Zeile <paramref name="row"/> des Bau-/Kauffensters
    /// (Basisfenster, Flughafen, Nachschubposten, Werft, Markt).</summary>
    public void PostBuildPanelPick(int row)
    {
        if (Alt2("produktion")) { BuildPanelPick(row); return; }
        var e = Producer();
        if (e == null || row < 0) return;
        PostProduktionAn(_entities.IndexOf(e), row);
    }

    /// <summary>Den Satz für EIN Gebäude bauen. <paramref name="row"/> = −1:
    /// die Zeile, die das Gebäude gerade zeigt (MenuIndex).</summary>
    private bool PostProduktionAn(int i, int row)
    {
        var e = _entities[i];
        if (!e.IsBuilding || e.Dead) return false;
        byte me = (byte)ViewPlayer;
        if (e.BType == 17)
        {
            // Der Markt verkauft — über 530 wie das Marktfenster.
            var regal = MarketShelf();
            if (row < 0 || row >= regal.Count) { _order = "nichts gewaehlt"; return false; }
            MarktBestellen(regal[row].Nr, e.Slot);
            return true;
        }
        if (e.BType == 9 || IsSupplyDepot(e))
        {
            var m = AirMenu(e);
            if (m.Count == 0) { _order = "nichts kaufbar"; return false; }
            int z = row >= 0 ? row : e.MenuIndex % m.Count;
            if (z >= m.Count) return false;
            if (IsSupplyDepot(e))
                return Emit(CommandRecord.Make(CommandOp.HeliKauf, me, (short)i, (short)z, me, S(m[z].Kind)));
            return Emit(CommandRecord.Make(CommandOp.FlugzeugKauf, me, (short)z, me, (short)i, S(m[z].Kind)));
        }
        if (IsDock(e))
        {
            var m = ShipMenu(e);
            if (m.Count == 0) { _order = "nichts baubar"; return false; }
            int z = row >= 0 ? row : e.MenuIndex % m.Count;
            if (z >= m.Count) return false;
            return Emit(CommandRecord.Make(CommandOp.BuildShip, me, (short)z, me, (short)i, S(m[z].Index)));
        }
        if (IsUnitPlant(e))
        {
            if (_designs == null || _designs.Count == 0) { _order = "keine Designs geladen"; return false; }
            var menu = BuildableFor(e.BType, e.Owner);
            if (menu.Count == 0) { _order = "nichts baubar"; return false; }
            int z = row >= 0 ? row : e.MenuIndex % menu.Count;
            if (z >= menu.Count) return false;
            return Emit(CommandRecord.Make(CommandOp.EinheitBauen, me, (short)menu[z], me, (short)i));
        }
        return false;
    }

    /// <summary>503/501/508/527 — die Bauzeile im Satzkontext, mit dem alten
    /// Weg (<see cref="ProduceFromSelection"/>), danach MenuIndex zurück.</summary>
    private bool ApplyProduktion(in CommandRecord c)
    {
        int geb = c.Op == CommandOp.HeliKauf ? c.P1 : c.P3;
        int spieler = c.Op == CommandOp.HeliKauf ? c.P3 : c.P2;
        if (spieler != c.Player || !BedienbaresGebaeude(c, geb)) return false;
        var e = _entities[geb];
        int zeile;
        switch (c.Op)
        {
            case CommandOp.EinheitBauen:
            {
                if (!IsUnitPlant(e) || _designs == null) return false;
                zeile = BuildableFor(e.BType, e.Owner).IndexOf(c.P1);
                break;
            }
            case CommandOp.FlugzeugKauf:
            case CommandOp.HeliKauf:
            {
                if (c.Op == CommandOp.FlugzeugKauf ? e.BType != 9 : !IsSupplyDepot(e)) return false;
                int z = c.Op == CommandOp.HeliKauf ? c.P2 : c.P1;
                // AirMenu fragt den Sichtspieler (Posten) — im Satzkontext
                var m = ImSatz(c, geb, -1, () => AirMenu(e));
                zeile = z >= 0 && z < m.Count && m[z].Kind == c.P4 ? z : -1;
                break;
            }
            case CommandOp.BuildShip:
            {
                if (!IsDock(e)) return false;
                var m = ShipMenu(e);
                zeile = c.P1 >= 0 && c.P1 < m.Count && m[c.P1].Index == c.P4 ? c.P1 : -1;
                break;
            }
            default: return false;
        }
        if (zeile < 0) return false;                       // Wächter
        int merk = e.MenuIndex;
        e.MenuIndex = zeile;
        long vorher = ProduktionsSpur(e);
        ImSatz(c, geb, -1, () => ProduceFromSelection());
        e.MenuIndex = merk;
        // »angenommen« heisst: es hat gewirkt (bezahlt, gelegt, gestartet) — eine
        // Absage (Teile, Platz, Geld) zählt nicht.
        return ProduktionsSpur(e) != vorher;
    }

    /// <summary>Woran man sieht, dass eine Bauzeile gewirkt hat.</summary>
    private long ProduktionsSpur(Entity e)
    {
        long h = NeubauInListeGelegt;
        h = h * 31 + e.BuildQueue.Count;
        h = h * 31 + _special.Count;
        h = h * 31 + e.StockW + 7L * e.StockF + 13L * e.StockS;
        h = h * 31 + (e.BuildTime > 0f ? 1 : 0);
        h = h * 31 + e.Garage.Count + 3L * e.Depot.Count;
        foreach (int m in _money) h = h * 31 + m;
        return h;
    }

    // ================= REPARATUR / START ======================================

    /// <summary>Taste K — je gewähltem Gebäude der Reparaturbefehl seiner Art
    /// (519/522/520/521), mit der Klicksperre des Originals.</summary>
    public void PostReparaturTaste()
    {
        if (Alt2("reparatur")) { StartRepair(); return; }
        foreach (int i in AuswahlNachPlatz())
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || ReparaturKlickGesperrt(e)) continue;
            short op = BuildingOpFor(e, BuildingJob.Repair);
            if (op != 0) Emit(CommandRecord.Make(op, (byte)ViewPlayer, (short)i));
        }
    }

    /// <summary>Minen-/Flughafenfenster »Start«/»Stop« (517/524…).</summary>
    public void PostBuildingWindowStart()
    {
        if (Alt2("reparatur")) { BuildingWindowStart(); return; }
        var e = Fenstergebaeude();
        if (e == null) return;
        short op = BuildingOpFor(e, BuildingJob.Idle);
        if (op != 0) Emit(CommandRecord.Make(op, (byte)ViewPlayer, (short)_entities.IndexOf(e)));
    }

    /// <summary>Minen-/Flughafenfenster »Reparatur« (522/520).</summary>
    public void PostBuildingWindowRepair()
    {
        if (Alt2("reparatur")) { BuildingWindowRepair(); return; }
        var e = Fenstergebaeude();
        if (e == null || ReparaturKlickGesperrt(e)) return;
        short op = BuildingOpFor(e, BuildingJob.Repair);
        if (op != 0) Emit(CommandRecord.Make(op, (byte)ViewPlayer, (short)_entities.IndexOf(e)));
    }

    // ================= BASISFENSTER: AUSSENDEN / VERWERTEN / TRANSPORT ========

    /// <summary>Der Wächter einer Basisfensterzeile: (Art 0 Garage, 1 Depot,
    /// 2 Hangar; Kennung).</summary>
    private (int Art, int Kennung) PanelzeileKennung(Entity e, int k)
    {
        if (e.Hangar is { Count: > 0 })
            return k >= 0 && k < e.Hangar.Count ? (2, e.Hangar[k]) : (-1, -1);
        if (k >= 0 && k < e.Garage.Count) return (0, _entities.IndexOf(e.Garage[k]));
        int d = k - e.Garage.Count;
        return d >= 0 && d < e.Depot.Count ? (1, e.Depot[d]) : (-1, -1);
    }

    /// <summary>»Aussenden« (Basisfenster). Einheitensatz → 504, sonst 2007.</summary>
    public void PostSendOutFromPanel(int k) => PostPanelzeile(k, 0, -1);

    /// <summary>»Recycle« (Basisfenster). Einheitensatz → 506, sonst 2007.</summary>
    public void PostRecycleFromPanel(int k) => PostPanelzeile(k, 1, -1);

    private void PostPanelzeile(int k, int handlung, int ziel)
    {
        if (Alt2("depot"))
        {
            if (handlung == 0) SendOutFromPanel(k);
            else if (handlung == 1) RecycleFromPanel(k);
            else TransportFromPanel(k, ziel);
            return;
        }
        var e = Producer();
        if (e == null) { _order = "kein Gebaeude gewaehlt"; return; }
        int geb = _entities.IndexOf(e);
        var (art, kenn) = PanelzeileKennung(e, k);
        if (art < 0) { _order = "nichts gewaehlt"; return; }
        byte me = (byte)ViewPlayer;
        if (art == 0 && handlung == 0)
            Emit(CommandRecord.Make(CommandOp.Aussenden, me, (short)kenn, (short)geb));
        else if (art == 0 && handlung == 1 && !RecycleGarageAlt)
            Emit(CommandRecord.Make(CommandOp.Verwerten, me, (short)kenn));
        else
            Emit(CommandRecord.Make(OpPanelzeile, me, (short)geb, (short)k, (short)handlung,
                                    S(kenn), (short)ziel));
    }

    /// <summary>2007 — Basisfensterzeile im Satzkontext (Wächter: Kennung an
    /// der Zeile).</summary>
    private bool ApplyPanelzeile(in CommandRecord c)
    {
        int geb = c.P1, k = c.P2, handlung = c.P3;
        if (!BedienbaresGebaeude(c, geb)) return false;
        var e = _entities[geb];
        var (art, kenn) = PanelzeileKennung(e, k);
        if (art < 0 || kenn != c.P4) return false;
        int ziel = c.P5;
        int vorher = e.Garage.Count + e.Depot.Count + (e.Hangar?.Count ?? 0);
        ImSatz(c, geb, -1, () =>
        {
            if (handlung == 0) SendOutFromPanel(k);
            else if (handlung == 1) RecycleFromPanel(k);
            else TransportFromPanel(k, ziel);
        });
        return e.Garage.Count + e.Depot.Count + (e.Hangar?.Count ?? 0) != vorher;
    }

    /// <summary>504 — Aussenden. P1 Einheit, P2 Gebäude. Depot/Bahnhof: UKOL
    /// 0x33 (0x410420, <see cref="DepotAussenden"/>); Basis/Fabrik: unser
    /// Basisfensterweg.</summary>
    private bool ApplyAussenden(in CommandRecord c)
    {
        int u = c.P1, geb = c.P2;
        if (!EigenesObjekt(c, u) || !BedienbaresGebaeude(c, geb)) return false;
        var b = _entities[geb];
        int k = b.Garage.IndexOf(_entities[u]);
        if (k < 0) return false;
        if (b.BType is 5 or 6 or 12)
        {
            // Der Zähler »Befehle 504« zählt der ABSENDER (PostDepotAussenden).
            int merkZahl = DepotAussendenBefehle;
            ImSatz(c, geb, -1, () => DepotAussenden(new List<int> { u }));
            DepotAussendenBefehle = merkZahl;
        }
        else
            ImSatz(c, geb, -1, () => SendOutFromPanel(k));
        return true;
    }

    /// <summary>506 — Verwerten einer eingefahrenen Einheit. P1 Einheit.</summary>
    private bool ApplyVerwerten(in CommandRecord c)
    {
        int u = c.P1;
        if (!EigenesObjekt(c, u)) return false;
        var unit = _entities[u];
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.Dead) continue;
            int k = b.Garage.IndexOf(unit);
            if (k < 0) continue;
            if (b.Owner != c.Player && b.Owner is >= 0 and <= 7) return false;
            int bi = i;
            ImSatz(c, bi, -1, () => RecycleFromPanel(k));
            return true;
        }
        return false;
    }

    /// <summary>Der Zielklick nach »Transportieren« (Basisfenster). Ersetzt den
    /// unmittelbaren Teil von <see cref="TransportZielGewaehlt"/>.</summary>
    private void PostTransportZiel(int platz, int einheit, int hit)
    {
        if (hit < 0) { _order = "Transport abgebrochen — kein Gebaeude getroffen"; return; }
        var q = Producer();
        if (q == null) return;
        if (einheit >= 0)
            Emit(CommandRecord.Make(CommandOp.Bahnverlegung, (byte)ViewPlayer, (short)einheit,
                                    (short)_entities.IndexOf(q), (short)hit));
        else
            PostPanelzeile(platz + q.Garage.Count, 2, hit);
        _order = "Transport befohlen";
    }

    /// <summary>Bahnhofsfenster »Transportieren« + Kartenklick: je Einheit 518.
    /// Gibt die Zahl der Sätze zurück; 0 = keine Verbindung (die Meldung kommt
    /// hier, beim Absender — wie im Original 0x4497D0).</summary>
    public int PostTransportierenAusBahnhof(List<int> griffe, int zielIdx)
    {
        if (Alt2("bahn")) return TransportierenAusBahnhof(griffe, zielIdx);
        var q = Fenstergebaeude();
        if (q == null || zielIdx < 0 || zielIdx >= _entities.Count) return 0;
        if (!BahnVerbindungDa(q, _entities[zielIdx])) return 0;
        var l = new List<int>(griffe);
        l.Sort();
        int n = 0;
        foreach (int g in l)
            if (Emit(CommandRecord.Make(CommandOp.Bahnverlegung, (byte)ViewPlayer, (short)g,
                                        (short)_entities.IndexOf(q), (short)zielIdx))) n++;
        return n;
    }

    /// <summary>Die Verbindungsprobe des Absenders (nur lesen) — dieselbe
    /// Rechnung wie in <see cref="VerlegePerBahn"/>; meldet wie dort.</summary>
    private bool BahnVerbindungDa(Entity q, Entity ziel)
    {
        if (!ziel.IsBuilding || ziel.Dead || ReferenceEquals(ziel, q)) return false;
        int a = NodeOfBuilding(q.Slot), b = NodeOfBuilding(ziel.Slot);
        Simulation.RailNetwork.OwnerOf = slot =>
        { int i = EntityOfSlot(slot); return i < 0 ? -1 : _entities[i].Owner; };
        var weg = a < 0 || b < 0 ? null
                : Simulation.RailNetwork.FindRoute(_railNodes, _netzLinien, a, b, q.Owner,
                                                   Simulation.RailNetwork.CampaignRules);
        if (weg != null) return true;
        OnMeldung?.Invoke("Es besteht keine Verbindung zu diesem Gebäude",
                          "mit dem Ursprungsgebäude!", 3, false);
        return false;
    }

    /// <summary>518 — Bahnverlegung. P1 Einheit, P2 Quelle, P3 Ziel.</summary>
    private bool ApplyBahnverlegung(in CommandRecord c)
    {
        int u = c.P1, q = c.P2, z = c.P3;
        if (!EigenesObjekt(c, u) || !BedienbaresGebaeude(c, q) || z < 0 || z >= _entities.Count) return false;
        var quelle = _entities[q];
        int n = ImSatz(c, q, -1, () => VerlegePerBahn(quelle, new List<int> { u }, z));
        return n > 0;
    }

    /// <summary>Depot-/Bahnhofsfenster »Aussenden« — je markierter Einheit
    /// 504 (@0x44C083).</summary>
    public void PostDepotAussenden(List<int> griffe)
    {
        if (Alt2("depot")) { DepotAussenden(griffe); return; }
        var b = Fenstergebaeude();
        if (b == null || b.BType is not (5 or 6 or 12)) return;
        var l = new List<int>(griffe);
        l.Sort();
        foreach (int g in l)
        {
            if (g < 0 || g >= _entities.Count || b.Garage.IndexOf(_entities[g]) < 0) continue;
            DepotAussendenBefehle++;          // der Absender zählt (Prüfstand »genau ein 504«)
            Emit(CommandRecord.Make(CommandOp.Aussenden, (byte)ViewPlayer, (short)g,
                                    (short)_entities.IndexOf(b)));
        }
    }

    // ================= BAHNSCHALTER ===========================================

    /// <summary>Fensterart 4 — Befehl 500.</summary>
    public bool PostRailSetFlag(int linie, int ware, int wert)
    {
        if (Alt2("bahn")) return RailSetFlag(linie, ware, wert);
        return Emit(CommandRecord.Make(CommandOp.Bahnschalter, (byte)ViewPlayer,
                                       (short)linie, (short)ware, (short)wert));
    }

    /// <summary>500 — spoj_set_flag 0x4B0220: keine Besitzerprüfung im
    /// Befehl (die Karte bietet nur eigene Linien an).</summary>
    private bool ApplyBahnschalter(in CommandRecord c) => RailSetFlag(c.P1, c.P2, c.P3);
}
