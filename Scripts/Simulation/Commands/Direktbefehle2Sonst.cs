namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>PAKETE »flughafen«, »route«, »entwurf«, »pionier«, »sonst«, »pause«,
/// »spielstand«</b> (17.10.2026, Direktbefehle2).
///
/// <para><b>Die Nummern des Originals</b> (gelesen 17.10.2026 in C, F gepaart):</para>
/// <code>
///   534  Bombe wechseln     P1 Flughafen, P2 Flugzeug     0x449AF1 → 0x4513F0 (F 0x4C38A9)
///   535  Flugzeug verwerten P1 Flugzeug, P2 Flughafen     0x449CAB → 0x4B27D0 (F 0x4C38C6)
///   537  Patrouille kippen  P1 Gebäude                    0x449B48, +0x43 := 1 − +0x43
///   502  Zielwahl/Hand      P1 Platz, P2 Modus 7/1/6, P3/P4 Zelle, P5 Ziel (0x450310)
///   2008 UNSERE SETZUNG     Staffelmarke an der Hangarzeile (2. Klick @0x44A028):
///                           P1 Flugzeug, P2 Marke. In der Absenderkarte steht für
///                           diesen Klick kein Busbefehl — (V) örtlich im Original?
///                           Die Marke ist bei uns Zustand (a.Staffel), also Ring.
///   512  Routenfach         P1 Fach 0..4, P2 Route, P3 Gebäude / 0xFF   0x4362E0
///                           (F 0x4C341B); Leeren = fünfmal mit 0xFF (0x44B73F)
///   513  Route starten      P1 Transporter                0x44B8BB → 0x410870
///   514  Route anhalten     P1 Transporter                0x4489C3 → 0x4108D0
///   505  Entwurf anlegen    P1 Waffe, P2 Ausrüstung, P3 Fahrwerk, P4 Spieler,
///                           Name — 0x44AA0F → 0x4B2510 (F 0x4C3339). ⚠ SETZUNG: die
///                           Zuordnung P1..P3 zu den Fensterfeldern +0x1E/+0x18/+0x12
///                           ist nicht gelesen; der NAME reist in P5..P11 (14 Zeichen,
///                           Latin-1) statt im 20-Byte-Text an +0x24 (P15 ist bei uns
///                           ein Wort).
///   16   Pionierauftrag     P1 Pionier, P2/P3 Zelle, P4 1 Brücke / 2 Mole / 4 Reparatur
///                           0x438081 → fahre 0x40B070 (F 0x4C2B0A)
///   993  Pause              keine Felder, Umschalter word[0x4FA280] 0 ↔ 1000
///                           (0x4C4635, F 0x4C41A7), Absender Taste P 0x4136F5 u. a.
///   991  Speichern          P1 Platz, Name (bei uns P2..P11, 20 Zeichen)  0x4C4571
///   990  Laden              P1 Platz                     0x44B4D2 → 0x4C44E8
///   2009 UNSERE SETZUNG     Schonfrist beenden (Fenster der Nachfrist), P1 Spieler
/// </code>
///
/// <para>⚠ Pause, Speichern und Laden gehen NUR IM NETZ über den Ring. Im
/// Einzelspieler bleibt der Baum-Halt (eine Maschine — dieselbe Wirkung ohne
/// Takt Verzug; ein Halt über den Ring bräuchte einen laufenden Takt, um
/// wieder aufzuheben).</para>
/// </summary>
public partial class MapEntityLayer
{
    public const short OpStaffelmarke = 2008, OpSchonfrist = 2009;

    // ================= NAMEN IN WÖRTERN =======================================

    /// <summary>Einen Namen in <paramref name="n"/> Wörter packen (2 Zeichen
    /// Latin-1 je Wort, ab P<paramref name="ab"/>).</summary>
    private static void NamePacken(ref CommandRecord c, string name, int ab, int n)
    {
        for (int k = 0; k < n; k++)
        {
            int lo = 2 * k < name.Length ? name[2 * k] & 0xFF : 0;
            int hi = 2 * k + 1 < name.Length ? name[2 * k + 1] & 0xFF : 0;
            c[ab + k] = (short)(lo | (hi << 8));
        }
    }

    private static string NameEntpacken(in CommandRecord c, int ab, int n)
    {
        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < n; k++)
        {
            int w = (ushort)c[ab + k];
            int lo = w & 0xFF, hi = (w >> 8) & 0xFF;
            if (lo == 0) break;
            sb.Append((char)lo);
            if (hi == 0) break;
            sb.Append((char)hi);
        }
        return sb.ToString();
    }

    // ================= FLUGHAFEN ==============================================

    public void PostPatrouilleKippen(int gebaeude)
    {
        if (Alt2("flughafen")) { PatrouilleKippen(gebaeude); return; }
        Emit(CommandRecord.Make(CommandOp.Patrouille, (byte)ViewPlayer, (short)gebaeude));
    }

    public void PostBombeWechseln(int platz, int flughafen)
    {
        if (Alt2("flughafen")) { BombeWechseln(platz, flughafen); return; }
        Emit(CommandRecord.Make(CommandOp.BombeWechseln, (byte)ViewPlayer, (short)flughafen, (short)platz));
    }

    public bool PostRecycleAmFlughafen(int gebaeude, int slot)
    {
        if (Alt2("flughafen")) return RecycleAmFlughafen(gebaeude, slot);
        return Emit(CommandRecord.Make(CommandOp.FlugzeugVerwerten, (byte)ViewPlayer, (short)slot, (short)gebaeude));
    }

    public void PostStaffelUmschalten(int platz, int marke)
    {
        if (Alt2("flughafen")) { StaffelUmschalten(platz, marke); return; }
        if (StaffelAus || marke == 0xFF) return;
        Emit(CommandRecord.Make(OpStaffelmarke, (byte)ViewPlayer, (short)platz, (short)marke));
    }

    /// <summary>Gehört der Flughafen mit diesem Platz dem Spieler?</summary>
    private bool FlughafenVon(in CommandRecord c, int slot, out Entity? e)
    {
        e = GebaeudeMitSlot(slot);
        return e != null && e.BType == 9 && (e.Owner == c.Player || e.Owner is < 0 or > 7);
    }

    private bool FlugzeugVon(in CommandRecord c, int slot)
    {
        var a = FlugzeugAufPlatz(slot);
        return a != null && a.Owner == c.Player;
    }

    private bool ApplyPatrouille(in CommandRecord c)
    {
        if (!FlughafenVon(c, c.P1, out _)) return false;
        PatrouilleKippen(c.P1);
        return true;
    }

    private bool ApplyBombeWechseln(in CommandRecord c)
    {
        if (!FlughafenVon(c, c.P1, out _) || !FlugzeugVon(c, c.P2)) return false;
        int vor = BombenDrehungen;
        BombeWechseln(c.P2, c.P1);
        return BombenDrehungen > vor;
    }

    private bool ApplyFlugzeugVerwerten(in CommandRecord c)
    {
        if (!FlughafenVon(c, c.P2, out _) || !FlugzeugVon(c, c.P1)) return false;
        int p1 = c.P1, p2 = c.P2;
        return ImSatz(c, -1, -1, () => RecycleAmFlughafen(p2, p1));
    }

    private bool ApplyStaffelmarke(in CommandRecord c)
    {
        if (!FlugzeugVon(c, c.P1) || c.P2 is < 0 or > 9) return false;
        StaffelUmschalten(c.P1, c.P2);
        return true;
    }

    /// <summary>502 — Modus 6 Handsteuerung (Taste 9), 7 Ziel, 1 Zelle.</summary>
    private bool ApplyFlugzeugstart(in CommandRecord c)
    {
        var a = FlugzeugAufPlatz(c.P1);
        if (a == null || a.Owner != c.Player) return false;
        if (c.P2 == ZielModusHand)
        {
            bool eigen = c.Player == ViewPlayer;
            int slot = c.P1;
            return eigen ? HandsteuerungLuftBeginnen(slot)
                         : ImSatz(c, -1, -1, () => HandsteuerungLuftBeginnen(slot, oertlich: false));
        }
        if (c.P2 != ZielModusZiel && c.P2 != ZielModusZelle) return false;
        if (_nav == null || !_nav.InBounds(c.P3, c.P4)) return false;
        int ziel = c.P2 == ZielModusZiel && c.P5 >= 0 && c.P5 < _entities.Count ? c.P5 : -1;
        return FlugzeugAngreifen(a, ziel, ZellMitte(c.P3, c.P4));
    }

    // ================= TRANSPORTROUTE (Fensterart 16) =========================

    /// <summary>Menüzeile »Transportzyklus einstellen« (0x44897D): Fenster
    /// örtlich auf, Befehl 514.</summary>
    public bool PostRouteFensterOeffnen()
    {
        if (Alt2("route")) return RouteFensterOeffnen();
        int i = RouteAuswahlWagen();
        if (i < 0) return false;
        var u = _entities[i];
        if (RouteVon(u) == null && (KeineTransportrouten || !IstTransporter(u)))
        { RouteNote = "dieser Wagen fuehrt keinen Umschlagsatz"; return false; }
        Emit(CommandRecord.Make(CommandOp.RouteAnhalten, (byte)ViewPlayer, (short)i));
        RouteFensterWagen = i;
        RouteWahlModus = 2;
        RouteNote = "Ziel anklicken";
        return true;
    }

    /// <summary>Gebäudeklick im Routenfenster (0x4495BD): Fach und Gebäude
    /// werden HIER bestimmt (aus dem Satz, den der Spieler sieht), Befehl 512.</summary>
    public bool PostRouteKlickAufGebaeude(Vector2 mapPos)
    {
        if (Alt2("route")) return RouteKlickAufGebaeude(mapPos);
        if (RouteWahlModus == 0 || RouteFensterWagen < 0) return false;
        return PostRouteKlickAufGebaeude(Pick(mapPos));
    }

    /// <summary>Derselbe Klick mit dem getroffenen Listenplatz.</summary>
    public bool PostRouteKlickAufGebaeude(int hit)
    {
        if (RouteWahlModus == 0 || RouteFensterWagen < 0) return false;
        var r = RouteFensterSatz();
        if (hit < 0 || !_entities[hit].IsBuilding) { RouteNote = "das ist kein Gebaeude"; return false; }
        var b = _entities[hit];
        if (b.Owner != ViewPlayer) { RouteNote = "nur eigene Gebaeude"; return false; }
        void Fach(int f, int g) => Emit(CommandRecord.Make(CommandOp.RouteFach, (byte)ViewPlayer,
                                                           (short)f, (short)RouteFensterWagen, (short)g));
        if (RouteWahlModus == 2)
        {
            Fach(4, b.Slot);
            RouteWahlModus = 1;
            RouteNote = $"Ziel: {BuildingTypeName(b.BType)} — jetzt die Quellen anklicken";
            return true;
        }
        int[] q = r?.Quelle ?? new[] { -1, -1, -1, -1 };
        for (int k = 0; k < 4; k++)
            if (q[k] == b.Slot) { Fach(k, 0xFF); RouteNote = $"{BuildingTypeName(b.BType)} wieder abgewaehlt"; return true; }
        for (int k = 0; k < 4; k++)
            if (q[k] < 0) { Fach(k, b.Slot); RouteNote = $"Quelle {k + 1}: {BuildingTypeName(b.BType)}"; return true; }
        RouteNote = "alle vier Quellfaecher sind belegt";
        return false;
    }

    /// <summary>Knopf »Leeren« — fünfmal 512 mit 0xFF (0x44B73F).</summary>
    public void PostRouteLeeren()
    {
        if (Alt2("route")) { RouteLeeren(); return; }
        if (RouteFensterWagen < 0) return;
        for (int f = 0; f < 5; f++)
            Emit(CommandRecord.Make(CommandOp.RouteFach, (byte)ViewPlayer, (short)f,
                                    (short)RouteFensterWagen, (short)0xFF));
        RouteWahlModus = 2;
        RouteNote = "geleert — Ziel anklicken";
    }

    /// <summary>Knopf »Start« (0x44B7E5): Probe beim Absender, Befehl 513.</summary>
    public bool PostRouteStartKnopf()
    {
        if (Alt2("route")) return RouteStartKnopf();
        var r = RouteFensterSatz();
        if (r == null || RouteFensterWagen < 0) return false;
        bool keineQuelle = true;
        foreach (int q in r.Quelle) if (q >= 0) keineQuelle = false;
        if (keineQuelle || r.Ziel < 0)
        {
            RouteNote = "Kann nicht starten. Waehlen Sie bitte Start- und Zielgebaeude.";
            return false;
        }
        Emit(CommandRecord.Make(CommandOp.RouteStarten, (byte)ViewPlayer, (short)RouteFensterWagen));
        RouteNote = "Transport gestartet";
        RouteFensterSchliessen();
        return true;
    }

    private bool ApplyRouteAnhalten(in CommandRecord c)
    {
        if (!EigenesObjekt(c, c.P1)) return false;
        var u = _entities[c.P1];
        if ((RouteVon(u) ?? RouteAnlegen(u)) == null) return false;
        RouteAnhalten(u);
        return true;
    }

    /// <summary>512 — 0x4362E0(satz, fach, wert): Fach 4 ist das Ziel, eine
    /// Quelle, die das Ziel ist, fällt heraus.</summary>
    private bool ApplyRouteFach(in CommandRecord c)
    {
        int fach = c.P1, wagen = c.P2, g = c.P3 == 0xFF ? -1 : c.P3;
        if (fach is < 0 or > 4 || !EigenesObjekt(c, wagen)) return false;
        var r = RouteVon(_entities[wagen]) ?? RouteAnlegen(_entities[wagen]);
        if (r == null) return false;
        if (g >= 0 && (GebaeudePlatz(g) is not { } b || b.Owner != c.Player)) return false;
        if (fach == 4)
        {
            r.Ziel = g;
            if (g >= 0) for (int k = 0; k < 4; k++) if (r.Quelle[k] == g) r.Quelle[k] = -1;
        }
        else
        {
            r.Quelle[fach] = g;
            if (g >= 0 && r.Ziel == g) r.Ziel = -1;
        }
        return true;
    }

    private bool ApplyRouteStarten(in CommandRecord c)
    {
        if (!EigenesObjekt(c, c.P1)) return false;
        var u = _entities[c.P1];
        var r = RouteVon(u);
        if (r == null || r.Ziel < 0) return false;
        bool keine = true;
        foreach (int q in r.Quelle) if (q >= 0) keine = false;
        if (keine) return false;
        RouteStarten(u, r);
        return true;
    }

    // ================= ENTWURF (505) ==========================================

    /// <summary>Wird aus <see cref="AcceptDesign"/> gerufen, wenn der Ring gilt.</summary>
    private void PostEntwurf(int prop, int equip, int weapon, string name)
    {
        var c = CommandRecord.Make(CommandOp.EntwurfAnlegen, (byte)ViewPlayer,
                                   (short)weapon, (short)equip, (short)prop, (short)ViewPlayer);
        NamePacken(ref c, name, 5, 7);
        Emit(c);
        Designer.Say("Entwurf wird angelegt …");
    }

    private bool ApplyEntwurf(in CommandRecord c)
    {
        if (_designs == null || c.P4 != c.Player) return false;
        string name = NameEntpacken(c, 5, 7);
        if (name.Length == 0) return false;
        foreach (var d in _designs)
            if (d.Name == name) { if (c.Player == ViewPlayer) Designer.Say("gibt es schon"); return false; }
        var made = new Design(name, c.P3, c.P2, c.P1, true, -1);
        _designs.Add(made);
        // ⚠ Im Netz wird NICHT nach designs.cfg geschrieben — die Datei hielte
        // sonst auch die Entwürfe des Partners (SaveOwnDesigns schreibt jeden
        // Slot −1). SETZUNG: Netzentwürfe bleiben in der Partie.
        if (!ImNetz && !_entwurfProbe) SaveOwnDesigns();
        if (c.Player == ViewPlayer)
            Designer.Say($"uebernommen — {made.CostW}/{made.CostF}/{made.CostS} Teile, " +
                         $"{made.Hp} HP, Angriff {made.Attack}");
        GD.Print($"designer: '{name}' angelegt (Befehl 505, Spieler {c.Player}) — Fahrwerk {c.P3}, " +
                 $"Waffe {c.P1}, Ausruestung {c.P2}");
        return true;
    }

    // ================= PIONIER (16) ===========================================

    private bool PostPionier(int idx, int col, int row, int order)
    {
        int modus = order == OrderBruecke ? 1 : order == OrderMole ? 2 : 4;
        BuildOrderNote = order == OrderBruecke ? "Bruecke bauen: befohlen" : "Pionier: befohlen";
        return Emit(CommandRecord.Make(CommandOp.Pionierauftrag, (byte)ViewPlayer, (short)idx,
                                       (short)col, (short)row, (short)modus));
    }

    private bool ApplyPionier(in CommandRecord c)
    {
        if (!EigenesObjekt(c, c.P1)) return false;
        int idx = c.P1, col = c.P2, row = c.P3;
        if (_nav == null || !_nav.InBounds(col, row)) return false;
        int order = c.P4 switch { 1 => OrderBruecke, 2 => OrderMole, 4 => OrderAusbessern, _ => -1 };
        if (order < 0) return false;
        string merk = BuildOrderNote;
        bool ok = ImSatz(c, idx, -1, () => order == OrderBruecke ? BrueckeKlick(idx, col, row)
                                                                : MoleKlick(idx, col, row, order));
        if (c.Player != ViewPlayer) BuildOrderNote = merk;
        return ok;
    }

    // ================= SCHONFRIST, NEBEL ======================================

    public void PostFinishGrace()
    {
        if (Alt2("sonst")) { FinishGrace(); return; }
        Emit(CommandRecord.Make(OpSchonfrist, (byte)ViewPlayer, (short)ViewPlayer));
    }

    private bool ApplySchonfrist(in CommandRecord c)
    {
        if (c.P1 != c.Player || _mscript == null) return false;   // im Gefecht ohne Wirkung
        FinishGrace();
        return true;
    }

    /// <summary>Taste J im Netz: der Nebel ist Teil der Partie (die KI-Aufklärung
    /// fragt <see cref="FogActive"/>) und steht dort fest AN — SETZUNG.</summary>
    public static bool NebelImNetzFest => ImNetz && !Alt2("sonst");

    // ================= PAUSE (993) ============================================

    /// <summary>word[0x4FA280]: 0 = läuft, 1000 = Pause (nur im Netz über den
    /// Ring gesetzt).</summary>
    public int NetzPause;

    /// <summary>Meldet der Oberfläche den Pausenwechsel (an/aus, wer).</summary>
    public System.Action<bool, int>? OnNetzPause;

    public void PostPause()
        => Emit(CommandRecord.Make(CommandOp.Pause, (byte)ViewPlayer));

    /// <summary>0x4C4635: 0 → 1000 (Fenster Art 40 auf), 1000 → 0 (zu),
    /// jeder andere Wert → nichts.</summary>
    private bool ApplyPause(in CommandRecord c)
    {
        if (NetzPause == 0) NetzPause = 1000;
        else if (NetzPause == 1000) NetzPause = 0;
        else return false;
        OnNetzPause?.Invoke(NetzPause != 0, c.Player);
        return true;
    }

    // ================= SPIELSTAND (990/991) ===================================

    /// <summary>Die Oberfläche schreibt bzw. lädt — sie kennt Kartenname und
    /// Kopf. Gerufen aus dem Behandler, auf JEDER Maschine im selben Takt.</summary>
    public System.Func<int, string, bool>? OnNetzSpeichern;
    public System.Func<int, bool>? OnNetzLaden;

    public void PostSpeichern(int platz, string name)
    {
        var c = CommandRecord.Make(CommandOp.Speichern, (byte)ViewPlayer, (short)platz);
        NamePacken(ref c, name, 2, 10);
        Emit(c);
    }

    public void PostLaden(int platz)
        => Emit(CommandRecord.Make(CommandOp.Laden, (byte)ViewPlayer, (short)platz));

    private bool ApplySpeichern(in CommandRecord c)
        => OnNetzSpeichern != null && OnNetzSpeichern(c.P1, NameEntpacken(c, 2, 10));

    private bool ApplyLaden(in CommandRecord c)
        => OnNetzLaden != null && OnNetzLaden(c.P1);

    /// <summary>990 im Netz: den Stand IN DIE LAUFENDE PARTIE legen (kein
    /// Szenenwechsel — der Taktgeber lebt weiter). Der Sichtspieler bleibt
    /// der örtliche; die Auswahl ist leer (ApplySaveState).</summary>
    public bool NetzStandEinspielen(Godot.Collections.Dictionary<string, Variant> root)
    {
        int merk = _viewPlayer;
        ApplySaveState(root);
        _viewPlayer = merk;
        // Der Stand kennt keine Handsteuerung (uk 3 steht nicht in der Datei,
        // die Flugzeugsätze werden aber fortgeschrieben): auf JEDER Maschine aus.
        foreach (var s in _special) s.Hand = false;
        HandLuftIdx = -1;
        HandsteuerungIdx = -1;
        RouteFensterSchliessen();
        UpdatePanel();
        QueueRedraw();
        return true;
    }
}
