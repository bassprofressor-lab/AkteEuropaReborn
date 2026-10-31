using System.Collections.Generic;
using System.Text;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ <b>JEDES GEFECHT STARTET KLASSISCH MIT STARTBASIS</b> (30.10.2026, bug-733).
///
/// <para><b>Spielerentscheidung 30.10.:</b> »im gefecht sollte es nur das klassische mit
/// startbasis geben, kein modus wie dort das nur einheiten da sind und aufeinander drauf
/// holzen«, dazu »wir nehmen deine vorschläge« zum Plan
/// <c>berichte/gefecht-startbasis-plan.md</c>.</para>
///
/// <para>⚠⚠ <b>DAS GANZE IST UNSERE ABWEICHUNG.</b> Das Original stellt nie eine Basis
/// auf: <c>add_building</c> (C 0x4C91B0 / F 0x4C8D60) hat in beiden Bauten genau drei
/// Rufer, alle im Leerlauf eines Baufahrzeugs (Depot, Feld-Rohstoffmine, Generator), und
/// <c>gefecht_starten</c> (C 0x41A150 / F 0x419F90) legt kein Gebaeude an (Plan §2). NET07
/// ist im Original eine reine Schlachtkarte. Erlaubt ist die Abweichung durch die
/// Trennachse Gefecht = Wettkampfmodus; die Kampagne laeuft hier nie durch
/// (<c>StartSkirmish</c> nur bei <c>CampaignMission == 0</c>).</para>
///
/// <para><b>Die Regel</b> (UNSERE SETZUNG in jedem Punkt):</para>
/// <list type="number">
///   <item>Nach <c>ClearIdleSlots</c> und B8 (<c>GrantStartingBases</c>) bekommt jeder
///   Mitspieler, der dann noch <b>keine Basis</b> hat, eine neu aufgestellte <b>Basis plus
///   eine Fabrik</b> (Spielerentscheidung 1). Wer eine Basis, aber keine Fabrik hat
///   (NET02..06 nach B8), bekommt seit 31.10.2026 die Fabrik dazu (--startbasis-fabrik-nur-neu).</item>
///   <item>Genau diese Plaetze werden vorher auf <see cref="StarterTroop"/> = 6 Einheiten
///   ausgeduennt, wie <c>KeepStartingTroop(6)</c> (Spielerentscheidung 2) — je PLATZ, nicht
///   je Karte: ein Platz mit Kartenbasis behaelt seine Truppe.</item>
///   <item>Basis und Fabrik bekommen das Lager der gewaehlten Rohstoffstufe aus derselben
///   Tafel wie <c>ApplyResources</c> (Basis W/F/S, Fabrik T).</item>
///   <item>Der Bauplatz wird in Ringen gesucht, ganzzahlig, ohne Wurf und ohne
///   Fliesskomma, in aufsteigender Platzreihenfolge — netzfest wie B8.</item>
///   <item>Findet ein KI-Platz keinen Bauplatz, spielt er nicht mit (Vorschlag F4 b): er
///   faellt aus der KI-Liste, seine Truppe wird wie bei <c>ClearIdleSlots</c> geraeumt. Der
///   Mensch spielt dann ohne Basis, mit einer Meldung.</item>
/// </list>
///
/// <para>Nicht nach einem Spielstand (bug-507: die Basis steht dann schon in der Datei).
/// Gegenschalter <c>--gefecht-startbasis-aus</c> (geht im Netz mit, <c>NetSession</c>,
/// bug-734). Pruefstand <c>--startbasis-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--gefecht-startbasis-aus</c> — bug-733: keine Startbasis, keine
    /// Ausduennung; der Stand bis 30.10.2026. Wird im Netz vom Vermittler uebernommen.</summary>
    public static bool GefechtStartbasisAus;

    /// <summary><c>--gefecht-startbasis-fabrik=N</c> — welche Fabrik dazukommt (2 Waffen, 3
    /// Fahrwerk, 4 Spezial). ⚠ UNSERE SETZUNG; begruendet im Bericht
    /// berichte/bau-gefecht-startbasis.md (gemessen, welches Lager der KI zuerst
    /// ausgeht). 0 = nur die Basis.</summary>
    public static int StartbasisFabrikTyp = 4;

    /// <summary><c>--startbasis-heim-alt</c> — bug-735: B8 mittelt den Heimpunkt wieder
    /// ueber ALLE Saetze des Platzes, Gebaeude und Typ-0-Skriptplaetze eingeschlossen
    /// (der Stand, in dem NET06 die Basen von P0 und P6 vertauscht).</summary>
    public static bool StartbasisHeimAlt;

    /// <summary>Bis wohin die Basis vom Heimpunkt abruecken darf, in Zellen
    /// (Tschebyschow-Abstand des Basismittelpunkts). ⚠ UNSERE SETZUNG: die Naeherung im Plan
    /// §1c fand auf NET07 den naechsten Bauplatz in 7 bis 21 Zellen.</summary>
    public const int StartbasisRadius = 24;

    /// <summary>Bis wohin die Fabrik vom Basismittelpunkt abruecken darf. ⚠ UNSERE SETZUNG,
    /// weit unter <c>HaulRange</c> = 40, damit der Nahweg Fabrik → Basis traegt.</summary>
    public const int StartbasisFabrikRadius = 16;

    /// <summary>Gesetzt vom MapViewer bei <c>--imap-abgleich-check</c>: der Abgleich laeuft
    /// dann VOR dem Aufstellen (die neue Basis stempelt die imap, Plan §4.8).</summary>
    public static bool ImapAbgleichVorStartbasis;

    /// <summary>Das Ergebnis des vorgezogenen Abgleichs, oder null.</summary>
    public string? ImapAbgleichErgebnis { get; private set; }

    /// <summary>Die Mitspieler dieser Partie (Mensch(en) + KI), aufsteigend — fuer den
    /// Pruefstand.</summary>
    public readonly List<int> StartbasisMitspieler = new();

    /// <summary>Was die Startbasis je Platz getan hat — fuer Meldung und Pruefstand.</summary>
    public readonly List<StartbasisEintrag> StartbasisProtokoll = new();

    public sealed class StartbasisEintrag
    {
        public int Platz;
        public (int C, int R) Heim;
        public int Basis = -1, Fabrik = -1;
        public int Abstand = -1;
        public int Geduennt;
        public bool Weg = true;
        /// <summary>Basis aus Karte/B8, nur die Fabrik kam dazu (31.10.2026).</summary>
        public bool NurFabrik;
        public string Notiz = "";
    }

    /// <summary><c>--startbasis-fabrik-nur-neu</c> — nur wer eine NEUE Basis bekommt, bekommt
    /// auch eine Fabrik (der Stand von 027e117: NET02..06-Plaetze mit B8-Basis ohne Fabrik).</summary>
    public static bool StartbasisFabrikNurNeu;

    private bool HatFabrik(int p)
    {
        foreach (var e in _entities)
            if (e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == p && e.BType is 2 or 3 or 4) return true;
        return false;
    }

    private int ErsteBasis(int p)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == p && e.BType == 1) return i;
        }
        return -1;
    }

    /// <summary>Ob diese Partie ueberhaupt eine Startbasis aufstellen durfte (nicht
    /// abgeschaltet, kein Spielstand). Fuer den Pruefstand.</summary>
    public bool StartbasisLief { get; private set; }

    /// <summary>
    /// bug-735 — der Heimpunkt eines Platzes aus seinen <b>Einheiten</b>: ganzzahliger
    /// Schwerpunkt der lebenden Nicht-Gebaeude des Platzes, ohne Gebaeude und ohne
    /// Typ-0-Skriptplaetze. Null, wenn der Platz keine Einheit hat.
    /// </summary>
    private (int C, int R)? HeimzelleMobil(int p)
    {
        long sc = 0, sr = 0; int n = 0;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p) continue;
            sc += e.Col; sr += e.Row; n++;
        }
        return n > 0 ? ((int)(sc / n), (int)(sr / n)) : null;
    }

    /// <summary>
    /// Die Startbasis aufstellen. Gerufen aus <c>StartSkirmish</c> hinter B8 und VOR
    /// <c>EnableSkirmishAi</c>. <paramref name="foes"/> wird gekuerzt, wenn ein KI-Platz
    /// keinen Bauplatz findet. Gibt die Zahl der neuen Basen zurueck.
    /// </summary>
    private int GefechtStartbasisAufstellen(List<int> mitspieler, List<int> foes, int stufe)
    {
        StartbasisProtokoll.Clear();
        StartbasisLief = false;
        StartbasisMitspieler.Clear();
        StartbasisMitspieler.AddRange(mitspieler);
        StartbasisMitspieler.Sort();
        if (!GefechtStartbasisAus && PruefstandBrauchtKartentruppe() is { } pst)
        {
            // ⚠ Prüfstände, die bestimmte Einheiten der NET07-Kartentruppe brauchen (einen
            // Fusssoldaten, sechs Stahlsucher), kippen an der Ausduennung, nicht an der
            // Regel. Sie laufen darum auf dem alten Stand — gemessen in
            // berichte/bau-gefecht-startbasis.md. Die Regel selbst bleibt unveraendert.
            GefechtStartbasisAus = true;
            GD.Print($"Gefecht-Startbasis: {pst} braucht die volle Kartentruppe — " +
                     "dieser Lauf wie mit --gefecht-startbasis-aus");
        }
        if (GefechtStartbasisAus || _nav == null) return 0;
        StartbasisLief = true;

        // Plan §4.8: der Abgleich gegen die Kartendatei misst die imap, wie die Karte sie
        // bringt — also vor dem ersten Stempel.
        if (ImapAbgleichVorStartbasis) ImapAbgleichErgebnis = ImapAbgleichCheck();

        var plaetze = new List<int>(mitspieler);
        plaetze.Sort();
        var ohne = new List<int>();
        var nurFabrik = new List<int>();
        foreach (int p in plaetze)
        {
            if (p is < 0 or > 7) continue;
            if (!HasBase(p)) ohne.Add(p);
            else if (!StartbasisFabrikNurNeu && !HatFabrik(p)) nurFabrik.Add(p);
        }
        if (ohne.Count == 0 && nurFabrik.Count == 0) return 0;

        // erst duennen, DANN suchen: CanBuild zaehlt Einheiten als belegt (Plan §1c, NET07
        // P7 hat mit voller Truppe einen einzigen Bauplatz im 40er-Quadrat)
        var geduennt = StartbasisDuennen(ohne, StarterTroop);

        LoadResources();
        ResourceLevel? r = _resLevels != null && stufe >= 0 && stufe < _resLevels.Count
                           ? _resLevels[stufe] : null;

        int neu = 0;
        foreach (int p in ohne)
        {
            var ein = new StartbasisEintrag { Platz = p, Geduennt = geduennt.GetValueOrDefault(p) };
            StartbasisProtokoll.Add(ein);
            var heim = HeimzelleMobil(p) ?? HomeCell(p);
            if (heim is not { } h) { ein.Notiz = "keine Einheit, kein Heimpunkt"; ein.Weg = false; continue; }
            ein.Heim = h;
            int start = StartbasisWegbereiter(p);

            var b = StartbasisPlatzSuchen(1, h.C, h.R, StartbasisRadius, start, out int abstand);
            if (b is not { } bo)
            {
                ein.Notiz = $"KEIN BAUPLATZ fuer die Basis in {StartbasisRadius} Zellen";
                continue;
            }
            var basis = PlaceBuilding(Patterns!, 1, bo.X, bo.Y, p);
            if (basis == null) { ein.Notiz = "PlaceBuilding lehnte die Basis ab"; continue; }
            StartbasisLager(basis, r);
            ein.Basis = _entities.Count - 1;
            ein.Abstand = abstand;
            neu++;

            if (StartbasisFabrikTyp is 2 or 3 or 4)
            {
                var fb = BuildingFootprint(1);
                var f = StartbasisPlatzSuchen(StartbasisFabrikTyp, bo.X + fb.X / 2, bo.Y + fb.Y / 2,
                                              StartbasisFabrikRadius, start, out _);
                if (f is { } fo && PlaceBuilding(Patterns!, StartbasisFabrikTyp, fo.X, fo.Y, p) is { } fab)
                {
                    StartbasisLager(fab, r);
                    ein.Fabrik = _entities.Count - 1;
                }
                else ein.Notiz = $"kein Bauplatz fuer die Fabrik in {StartbasisFabrikRadius} Zellen";
            }
        }

        // ⭐ 31.10.2026 (Spielerentscheidung: »NET02 bis 06 auch mit Fabrik«) — wer schon eine
        // Basis hat (Karte oder B8), aber keine Fabrik, bekommt die Fabrik dazu, neben seine
        // erste Basis. Keine Ausduennung, keine neue Basis. Gegenschalter
        // --startbasis-fabrik-nur-neu (der Stand von 027e117).
        //
        // ⚠ UNSERE SETZUNG, gemessen 31.10.: auf NET02..06 fand die Ringsuche fuer 8 von 20
        // Plaetzen in 16 Zellen keinen Bauplatz (dicht bebaute Karten, teils nicht einmal in
        // 40), aber JEDER davon hatte eine NEUTRALE Fabrik in 10..23 Zellen. Darum wie B8 bei
        // der Basis: zuerst die naechste noch freie neutrale Fabrik (Art 2/3/4) innerhalb
        // HaulRange = 40 Zellen uebergeben — Abstand in Zellen, ganzzahlig, bei Gleichstand
        // der kleinere Satzindex, Plaetze aufsteigend (netzfest). Erst wenn keine da ist, wird
        // eine Fabrik der Art StartbasisFabrikTyp aufgestellt.
        var vergeben = new HashSet<int>();
        foreach (int p in nurFabrik)
        {
            if (StartbasisFabrikTyp is not (2 or 3 or 4)) break;
            int bi = ErsteBasis(p);
            if (bi < 0) continue;
            var ein = new StartbasisEintrag { Platz = p, Basis = bi, NurFabrik = true };
            StartbasisProtokoll.Add(ein);
            var bo = _entities[bi];
            ein.Heim = (bo.Col, bo.Row);

            int nb = -1; long nd = 40L * 40L + 1;
            for (int k = 0; k < _entities.Count; k++)
            {
                var x = _entities[k];
                if (!x.IsBuilding || x.IsProp || x.Dead || x.Owner != NeutralOwner || x.BType is not (2 or 3 or 4)) continue;
                if (vergeben.Contains(k)) continue;
                long dc = x.Col - bo.Col, dr = x.Row - bo.Row;
                long d = dc * dc + dr * dr;
                if (d < nd) { nd = d; nb = k; }
            }
            if (nb >= 0)
            {
                Hand(_entities[nb], p);
                StartbasisLager(_entities[nb], r);
                vergeben.Add(nb);
                ein.Fabrik = nb;
                ein.Notiz = $"neutrale Fabrik uebergeben, Abstand^2 {nd}";
                continue;
            }

            var fb = BuildingFootprint(1);
            var f = StartbasisPlatzSuchen(StartbasisFabrikTyp, bo.Col + fb.X / 2, bo.Row + fb.Y / 2,
                                          StartbasisFabrikRadius, StartbasisWegbereiter(p), out _);
            if (f is { } fo && PlaceBuilding(Patterns!, StartbasisFabrikTyp, fo.X, fo.Y, p) is { } fab)
            {
                StartbasisLager(fab, r);
                ein.Fabrik = _entities.Count - 1;
            }
            else ein.Notiz = $"keine neutrale Fabrik in 40 und kein Bauplatz in {StartbasisFabrikRadius} Zellen";
        }

        // Vorschlag F4 (b): ein KI-Platz ohne Basis spielt nicht mit
        var raus = new List<int>();
        foreach (var ein in StartbasisProtokoll)
            if (ein.Basis < 0 && foes.Contains(ein.Platz)) raus.Add(ein.Platz);
        foreach (int p in raus)
        {
            foes.Remove(p);
            mitspieler.Remove(p);
            StartbasisMitspieler.Remove(p);
        }
        if (raus.Count > 0) ClearIdleSlots(mitspieler);

        _sel.Clear();
        QueueRedraw();
        GD.Print(StartbasisZeile());
        return neu;
    }

    /// <summary>Die Pruefstaende, die an der Ausduennung kippen (gemessen 31.10.2026):
    /// <c>--gelegenheitsfeuer-check</c> (G4 »entfaellt: kein eigener Fusssoldat«) und
    /// <c>--magnetbombe-check</c> (»KEIN URTEIL: weniger als 6 Stahlsucher«). Nicht im
    /// Netz (dort zaehlt allein die Sitzung).</summary>
    private static readonly string[] _pruefstaendeMitKartentruppe =
        { "--gelegenheitsfeuer-check", "--magnetbombe-check" };

    private static string? PruefstandBrauchtKartentruppe()
    {
        if (Network.NetworkManager.Link != null) return null;
        foreach (string s in _pruefstaendeMitKartentruppe)
            if (System.Array.IndexOf(Core.CommandLine.Args, s) >= 0) return s;
        return null;
    }

    /// <summary>Die Meldung zum Start, eine Zeile.</summary>
    public string StartbasisZeile()
    {
        var sb = new StringBuilder("Gefecht-Startbasis (bug-733, unsere Abweichung): ");
        if (!StartbasisLief) return sb.Append(GefechtStartbasisAus ? "aus (--gefecht-startbasis-aus)"
                                                                   : "nicht gelaufen").ToString();
        if (StartbasisProtokoll.Count == 0) return sb.Append("jeder Mitspieler hat schon eine Basis").ToString();
        var teile = new List<string>();
        foreach (var e in StartbasisProtokoll)
        {
            string s = $"P{e.Platz} Heim ({e.Heim.C},{e.Heim.R})";
            if (e.Basis >= 0)
            {
                var b = _entities[e.Basis];
                s += e.NurFabrik ? $" Basis aus Karte/B8 ({b.Col},{b.Row})" : $" -> Basis ({b.Col},{b.Row}) Ring {e.Abstand}";
                if (e.Fabrik >= 0) s += $", Fabrik Art {_entities[e.Fabrik].BType} ({_entities[e.Fabrik].Col},{_entities[e.Fabrik].Row})";
            }
            else s += " -> KEINE BASIS";
            if (e.Geduennt > 0) s += $", {e.Geduennt} Einheiten entfernt";
            if (e.Notiz.Length > 0) s += $" [{e.Notiz}]";
            teile.Add(s);
        }
        return sb.Append(string.Join("; ", teile)).ToString();
    }

    /// <summary>Das Lager der Startbasen und -fabriken jetzt, je Platz — die Messzeile fuer
    /// die Wahl der Fabrik (welches Teil geht der KI zuerst aus?).</summary>
    public string StartbasisLagerZeile()
    {
        if (!StartbasisLief || StartbasisProtokoll.Count == 0) return "";
        var teile = new List<string>();
        foreach (var e in StartbasisProtokoll)
        {
            if (e.Basis < 0) continue;
            var b = _entities[e.Basis];
            string s = $"P{e.Platz} Basis {(b.Dead ? "ZERSTOERT" : $"W{b.StockW} F{b.StockF} S{b.StockS}")}" +
                       (b.Owner != e.Platz && !b.Dead ? $" (jetzt P{b.Owner})" : "");
            if (e.Fabrik >= 0)
            {
                var f = _entities[e.Fabrik];
                s += $", Fabrik Art {f.BType} {(f.Dead ? "ZERSTOERT" : $"T{f.StockT} W{f.StockW} F{f.StockF} S{f.StockS}")}";
            }
            teile.Add(s);
        }
        return "Startbasis-Lager (bug-733): " + string.Join("; ", teile);
    }

    /// <summary>Wie <c>KeepStartingTroop</c>, aber nur fuer die genannten Plaetze: die
    /// niedrigsten Satznummern bleiben, also die Auswahl der Karte.</summary>
    private Dictionary<int, int> StartbasisDuennen(List<int> plaetze, int keep)
    {
        var order = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.IsBuilding || e.Dead || !plaetze.Contains(e.Owner)) continue;
            order.Add(i);
        }
        // stabil: gleiche Satznummer -> kleinerer Index zuerst
        order.Sort((a, b) =>
        {
            int c = _entities[a].Slot.CompareTo(_entities[b].Slot);
            return c != 0 ? c : a.CompareTo(b);
        });
        var kept = new int[8];
        var weg = new Dictionary<int, int>();
        foreach (int i in order)
        {
            var e = _entities[i];
            int p = e.Owner;
            if (kept[p] < keep) { kept[p]++; continue; }
            e.Dead = true;
            e.Hp = 0;
            e.DeadTime = 999f;              // lange tot: kein Wrack, kein Rauch bei t=0
            e.Path = null;
            e.Target = -1;
            e.Orders.Clear();
            _nav?.ClearOccupant(e.Col, e.Row, i);
            weg[p] = weg.GetValueOrDefault(p) + 1;
        }
        return weg;
    }

    /// <summary>Das Lager nach der Rohstoffstufe, derselbe Fall wie in
    /// <c>ApplyResources</c>.</summary>
    private static void StartbasisLager(Entity e, ResourceLevel? r)
    {
        if (r == null) return;
        switch (FillOf(e.BType))
        {
            case ResourceFill.Stores:
                e.StockW = r.Weapons; e.StockF = r.Chassis; e.StockS = r.Special;
                break;
            case ResourceFill.Factory:
                e.StockW = e.StockF = e.StockS = 0;
                e.StockT = r.Terranium;
                break;
        }
    }

    /// <summary>Die Einheit, von der aus die Tuer erreichbar sein muss: die erste lebende
    /// Bodeneinheit (Fahrzeugklasse) des Platzes nach Index, sonst -1.</summary>
    private int StartbasisWegbereiter(int p)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p) continue;
            if (e.Move == Simulation.NavGrid.MoveClass.Vehicle) return i;
        }
        return -1;
    }

    /// <summary>
    /// Die Ringsuche. Kandidat ist der MITTELPUNKT des Gebaeudes (Ursprung + halber
    /// Grundriss); Ring r = Tschebyschow-Abstand zum Ausgangspunkt, 0..<paramref name="radius"/>.
    /// Im Ring gewinnt der kleinste quadrierte Abstand, dann die kleinere Zeile, dann die
    /// kleinere Spalte. Ganzzahlig, ohne Wurf. Gibt den Ursprung zurueck.
    /// </summary>
    private Vector2I? StartbasisPlatzSuchen(int typ, int mc, int mr, int radius, int wegbereiter,
                                            out int ring)
    {
        ring = -1;
        if (Patterns == null || _nav == null) return null;
        var fp = BuildingFootprint(typ);
        int hx = fp.X / 2, hy = fp.Y / 2;
        var kand = new List<(int D2, int Y, int X)>();
        for (int r = 0; r <= radius; r++)
        {
            kand.Clear();
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != r) continue;
                    kand.Add((dx * dx + dy * dy, mr + dy, mc + dx));
                }
            kand.Sort();
            foreach (var (_, y, x) in kand)
            {
                int oc = x - hx, or = y - hy;
                if (!StartbasisPasst(typ, oc, or, wegbereiter)) continue;
                ring = r;
                return new Vector2I(oc, or);
            }
        }
        return null;
    }

    /// <summary>Tuer (Ursprung + erste Tuer der Typtafel) eines Typs, oder null.</summary>
    private static Vector2I? StartbasisTuer(int typ)
    {
        var d = BuildingDoors(typ);
        return d.Count > 0 ? d[0] : null;
    }

    /// <summary>
    /// Passt ein Gebaeude der Art <paramref name="typ"/> mit Ursprung (c, r)?
    /// <list type="number">
    ///   <item><c>CanBuild</c> — die Pruefung des Originals (<c>can_build_here</c>
    ///   0x4203C0); Einheiten zaehlen als belegt.</item>
    ///   <item>Tuer und Vorplatz: die Tuerzelle und die drei Zellen darunter sind fuer
    ///   Fahrzeuge befahrbar und leer. Richtung »darunter« wie der Vorplatz des Originals
    ///   (Einnahmeabschluss C 0x43D070: x = Tuer−2..Tuer+2, y = Tuer+1..Tuer+4).</item>
    ///   <item>Abstand 1 Zelle zu jedem anderen Gebaeude (Grundrissrechteck), und kein
    ///   Grundriss auf dem Vorplatz (5×4) eines anderen — und umgekehrt.</item>
    ///   <item>Die Zelle vor der Tuer ist von <paramref name="wegbereiter"/> aus erreichbar
    ///   (<c>FindPath</c>), wenn der Platz eine Bodeneinheit hat.</item>
    /// </list>
    /// ⚠ Punkte 2–4 sind UNSERE SETZUNG.
    /// </summary>
    private bool StartbasisPasst(int typ, int c, int r, int wegbereiter)
    {
        if (!CanBuild(Patterns!, typ, c, r)) return false;
        var fp = BuildingFootprint(typ);
        if (StartbasisTuer(typ) is not { } tuer) return false;
        int tx = c + tuer.X, ty = r + tuer.Y;
        for (int k = 0; k <= 3; k++)
        {
            int y = ty + k;
            if (!_nav!.InBounds(tx, y)) return false;
            if (_nav.OccupantAt(tx, y) != -1 || _nav.IstTuerGesperrt(tx, y)) return false;
            // die Tuerzelle selbst ist Musterzelle des Gebaeudes; befahrbar muss der Boden sein
            if (_nav.GroundAt(tx, y) != Simulation.NavGrid.Ground.Free) return false;
        }

        var meinR = new Rect2I(c, r, fp.X, fp.Y);
        var meinAbstand = meinR.Grow(1);
        var meinV = new Rect2I(tx - 2, ty + 1, 5, 4);
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead || b.NoStructure) continue;
            var bR = new Rect2I(b.Col, b.Row, System.Math.Max(1, b.FootW), System.Math.Max(1, b.FootH));
            if (meinAbstand.Intersects(bR)) return false;
            if (meinV.Intersects(bR)) return false;
            if (b.DoorCells.Count > 0)
            {
                var bV = new Rect2I(b.Col + b.DoorCol - 2, b.Row + b.DoorRow + 1, 5, 4);
                if (meinR.Intersects(bV)) return false;
            }
        }

        if (wegbereiter >= 0)
        {
            var u = _entities[wegbereiter];
            var weg = _nav!.FindPath(new Vector2I(u.Col, u.Row), new Vector2I(tx, ty + 1),
                                     Simulation.NavGrid.MoveClass.Vehicle, wegbereiter);
            bool da = u.Col == tx && u.Row == ty + 1;
            if (!da && (weg == null || weg.Count == 0)) return false;
        }
        return true;
    }

    // ================= der Pruefstand ==========================================

    /// <summary>
    /// <c>--startbasis-check</c> (bug-733) — gleich nach dem Start:
    /// <list type="number">
    ///   <item>jeder Mitspieler hat eine lebende Basis mit Lager (W+F+S &gt; 0);</item>
    ///   <item>wer eine NEUE Basis bekam, hat dazu eine Fabrik mit Terranium; ebenso, wer eine
    ///   Basis ohne Fabrik hatte (31.10.2026);</item>
    ///   <item>an jeder neuen Basis und Fabrik sind Tuer und die drei Zellen davor frei und
    ///   von einer eigenen Bodeneinheit erreichbar; kein Grundriss ueberlappt ein anderes
    ///   Gebaeude;</item>
    ///   <item>auf NET07 hat jeder Mitspieler genau 6 Einheiten.</item>
    /// </list>
    /// Nullmodell <c>--gefecht-startbasis-aus</c>: NET07 hat keine Basis → DURCHGEFALLEN.
    /// </summary>
    public string StartbasisCheck(string karte, IEnumerable<int> mitspieler)
    {
        var sb = new StringBuilder($"startbasis-check {karte}"
            + (GefechtStartbasisAus ? " — NULLMODELL --gefecht-startbasis-aus" : "") + "\n");
        sb.AppendLine("  " + StartbasisZeile());
        bool ok = true;
        var neu = new Dictionary<int, StartbasisEintrag>();
        foreach (var e in StartbasisProtokoll) neu[e.Platz] = e;
        foreach (int p in mitspieler)
        {
            int basen = 0, fabriken = 0, einheiten = 0;
            long lager = 0, terra = 0;
            foreach (var e in _entities)
            {
                if (e.Dead || e.IsProp || e.Owner != p) continue;
                if (!e.IsBuilding) { einheiten++; continue; }
                if (e.BType == 1) { basen++; lager += e.StockW + e.StockF + e.StockS; }
                if (e.BType is 2 or 3 or 4) { fabriken++; terra += e.StockT; }
            }
            bool pOk = basen >= 1 && lager > 0;
            // 31.10.2026: jeder Mitspieler hat auch eine Fabrik (Nullmodell --startbasis-fabrik-nur-neu
            // faellt auf NET02..06 durch)
            pOk &= StartbasisFabrikTyp == 0 || (fabriken >= 1 && terra > 0);
            string z = $"  P{p}: {basen} Basis/Basen (Lager {lager}), {fabriken} Fabrik(en) (T {terra}), {einheiten} Einheiten";
            if (neu.TryGetValue(p, out var ein))
            {
                pOk &= ein.Basis >= 0 && ein.Fabrik >= 0 && terra > 0;
                foreach (int idx in ein.NurFabrik ? new[] { ein.Fabrik } : new[] { ein.Basis, ein.Fabrik })
                {
                    if (idx < 0) continue;
                    string t = StartbasisTuerPruefen(idx, p);
                    z += $"; {(_entities[idx].BType == 1 ? "Basis" : "Fabrik")} ({_entities[idx].Col},{_entities[idx].Row}) {t}";
                    pOk &= t.StartsWith("frei");
                }
                z += ein.NurFabrik ? $" [Basis aus Karte/B8, Fabrik dazu: {(ein.Notiz.StartsWith("neutrale") ? "neutrale uebergeben" : "aufgestellt")}]" : " [neu]";
            }
            else z += " [Basis aus Karte/B8]";
            if (karte == "map_NET07")
            {
                z += $", Soll 6 Einheiten";
                pOk &= einheiten == StarterTroop;
            }
            ok &= pOk;
            sb.AppendLine(z + (pOk ? "" : "  <- FEHLT"));
        }
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    /// <summary>Tuer und Vorplatz eines aufgestellten Gebaeudes: Tuer gesetzt, die drei
    /// Zellen darunter leer und befahrbar, keine Ueberlappung, erreichbar.</summary>
    private string StartbasisTuerPruefen(int idx, int p)
    {
        var b = _entities[idx];
        if (_nav == null || b.DoorCells.Count == 0) return "OHNE TUER";
        int tx = b.Col + b.DoorCol, ty = b.Row + b.DoorRow;
        for (int k = 1; k <= 3; k++)
            if (_nav.OccupantAt(tx, ty + k) != -1 || !_nav.CanEnter(tx, ty + k, Simulation.NavGrid.MoveClass.Vehicle))
                return $"VORPLATZ ({tx},{ty + k}) BELEGT/GESPERRT";
        var bR = new Rect2I(b.Col, b.Row, b.FootW, b.FootH);
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (i == idx || !o.IsBuilding || o.IsProp || o.Dead || o.NoStructure) continue;
            if (bR.Intersects(new Rect2I(o.Col, o.Row, System.Math.Max(1, o.FootW), System.Math.Max(1, o.FootH))))
                return $"UEBERLAPPT Gebaeude {i}";
        }
        int w = StartbasisWegbereiter(p);
        if (w < 0) return "frei (keine Bodeneinheit fuer den Weg)";
        var u = _entities[w];
        var weg = _nav.FindPath(new Vector2I(u.Col, u.Row), new Vector2I(tx, ty + 1),
                                Simulation.NavGrid.MoveClass.Vehicle, w);
        return weg != null && weg.Count > 0 ? $"frei, Weg {weg.Count} Schritte" : "NICHT ERREICHBAR";
    }
}
