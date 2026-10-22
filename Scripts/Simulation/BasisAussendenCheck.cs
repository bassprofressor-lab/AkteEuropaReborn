namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// <b><c>--basis-aussenden-check</c> — bug-591 (KayelGee R7 #3, 22.10.2026).</b>
/// Seine Meldung: »Wenn Einheiten ins Depot gesendet werden und danach wieder
/// ausgesendet werden, kann es passieren, dass die Einheit einfach neben der
/// Basis auftaucht.« (berichte/depot-ein-aussenden-opus.md)
///
/// <para>Der Prüfstand nimmt den WEG DES SPIELERS: Basisfenster, Zeilen
/// anklicken (Mehrfachauswahl bug-461), Knopf »Aussenden« → Befehl 504 über den
/// Ring (<c>RingLeeren</c>), dann laufen die Takte. Gemessen wird je Einheit die
/// ERSTE SICHTBARE Zelle (nicht mehr in der Liste, nicht untergestellt) und wie
/// viele Einheiten im selben Takt herauskamen.</para>
/// <list type="bullet">
///   <item><b>A</b> — drei Fahrzeuge, freie Tür. Soll: alle drei zuerst auf der
///   Türzelle, höchstens eine je Takt (0x43DE58), <c>SpawnedBesideDoor</c> +0.
///   Nullmodell <c>--basis-aussenden-sofort</c>: zwei von drei daneben.</item>
///   <item><b>B</b> — dieselben drei, ein unbeweglicher eigener Blockierer UNTER
///   der Tür (Folgezelle, 0x43DCCC). Soll: keine sichtbar, alle drei in der Liste
///   mit UKOL 0x33. Dann SPIELSTAND hin und zurück (im Speicher, keine Datei) —
///   die Wartenden müssen ihn überstehen —, Blockierer weg, alle drei kommen an
///   der Tür heraus. Nullmodell <c>--basis-aussenden-sofort</c>: alle daneben.</item>
///   <item><b>C</b> (B2) — Fussvolk in der Basis, ein unbewegliches Fahrzeug AUF
///   der Tür. Soll: der Soldat wartet. Nullmodell <c>--auslass-rueckfall-alt</c>:
///   er steht neben der Tür.</item>
///   <item><b>D</b> (B2) — eine PRODUZIERTE Zeile (Entwurfsnummer) bei belegter
///   Tür. Soll: Absage, keine Einheit daneben. Nullmodell
///   <c>--auslass-rueckfall-alt</c>: eine neue Einheit neben der Tür.</item>
/// </list>
/// <para>⚠ EINGRIFFE (angesagt): die Probanden werden über die echte
/// <c>Einfahren</c> in die Basis gestellt (<c>DepotProbeBefuellen</c>);
/// Blockierer werden mit <c>AwStelle</c> gesetzt und <c>Mobile = false</c>, damit
/// »wait B/C« (bug-457) sie nicht wegschickt.</para>
/// </summary>
public partial class MapEntityLayer
{
    private bool BaSichtbar(Entity b, Entity u)
        => !u.Dead && !b.Garage.Contains(u) && !Untergestellt(u);

    /// <summary>Tür und Folgezelle von fremder Belegung räumen (eigene bewegliche
    /// Einheiten werden ein Stück weggestellt). false, wenn das nicht geht.</summary>
    private bool BaTuerRaeumen(int tx, int ty, HashSet<int> ausser)
    {
        if (_nav == null) return false;
        foreach (var (c, r) in new[] { (tx, ty), (tx, ty + 1) })
        {
            int o = _nav.OccupantAt(c, r);
            if (o < 0 || o >= _entities.Count || ausser.Contains(o)) continue;
            var e = _entities[o];
            if (e.IsBuilding || e.IsProp) return false;
            var weg = _nav.NearestFree(new Vector2I(c + 8, r + 6), e.Move);
            if (weg == null) return false;
            AwStelle(o, weg.Value);
        }
        return _nav.IsFree(tx, ty, Simulation.NavGrid.MoveClass.Vehicle)
            && !ZelleDarunterBelegt(tx, ty + 1);
    }

    /// <summary>Die Zeilen des Basisfensters anklicken und »Aussenden« drücken —
    /// der Spielerweg (504/2007 über den Ring) — und den Ring leeren.</summary>
    private int BaAussendenKlicken(UI.BaseWindow w, IEnumerable<int> zeilen)
    {
        w.Visible = true; w.Refresh();
        foreach (int z in zeilen) w.ProbeDepotKlick(z);
        int markiert = w.ProbeMarkiert;
        w.ProbeAussenden();
        RingLeeren();
        return markiert;
    }

    public string BasisAussendenCheckLine(UI.BaseWindow? w)
    {
        var sb = new StringBuilder("basis-aussenden-check\n");
        string schalter = (BasisAussendenSofort ? " [Nullmodell --basis-aussenden-sofort]" : "")
                        + (AuslassRueckfallAlt ? " [Nullmodell --auslass-rueckfall-alt]" : "");
        if (_nav == null || w == null) return sb.Append("  keine Karte/kein Basisfenster — NICHT GEMESSEN").ToString();
        int gb = ProbeBasisfensterWaehlen();
        if (gb < 0 || _entities[gb].BType != 1)
            return sb.Append("  keine eigene Basis (Art 1) mit Fenster — NICHT GEMESSEN (K21 nehmen)").ToString();
        var b = _entities[gb];
        int bslot = b.Slot;
        int tx = b.Col + b.DoorCol, ty = b.Row + b.DoorRow;
        sb.Append($"  Basis Platz {b.Slot} bei ({b.Col},{b.Row}), Tuer ({tx},{ty}){schalter}\n");

        // Blockierer zuerst bestimmen und festhalten (Mobile=false), damit
        // DepotProbeBefuellen ihn nicht mit hineinstellt.
        int blocker = -1;
        for (int i = 0; i < _entities.Count && blocker < 0; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != ViewPlayer || e.Infantry >= 0) continue;
            if (Untergestellt(e) || e.InGebaeude != null || e.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
            blocker = i;
        }
        if (blocker < 0) return sb.Append("  kein eigenes Fahrzeug als Blockierer — NICHT GEMESSEN").ToString();
        var bl = _entities[blocker];
        int blSlot = bl.Slot;
        bl.Mobile = false;
        bool ok = true;
        int garageVor = b.Garage.Count;

        // ================= A: freie Tür, drei Zeilen ==========================
        int drin = DepotProbeBefuellen(gb, 3);
        var probanden = new List<Entity>();
        foreach (var u in b.Garage) if (u.Infantry < 0) probanden.Add(u);
        if (drin < 3 || probanden.Count < 3)
        { bl.Mobile = true; return sb.Append($"  nur {drin} Fahrzeuge eingefahren (Soll 3) — NICHT GEMESSEN").ToString(); }
        if (probanden.Count > 3) probanden.RemoveRange(0, probanden.Count - 3);
        var ausser = new HashSet<int>();
        if (!BaTuerRaeumen(tx, ty, ausser))
        { bl.Mobile = true; return sb.Append("  Tuer/Folgezelle nicht freizubekommen — NICHT GEMESSEN").ToString(); }
        var zeilen = new List<int>();
        foreach (var u in probanden) zeilen.Add(b.Garage.IndexOf(u));
        int besideVor = SpawnedBesideDoor;
        int markiert = BaAussendenKlicken(w, zeilen);
        var erste = new Dictionary<Entity, (int Takt, Vector2I Zelle)>();
        int maxJeTakt = 0;
        void Beobachten(int takte)
        {
            for (int t = -1; t < takte; t++)
            {
                if (t >= 0) SimTickFuerProbe();
                int neu = 0;
                foreach (var u in probanden)
                    if (!erste.ContainsKey(u) && BaSichtbar(b, u))
                    { erste[u] = (t + 1, new Vector2I(u.Col, u.Row)); neu++; }
                maxJeTakt = System.Math.Max(maxJeTakt, neu);
            }
        }
        Beobachten(400);
        int anTuer = 0, daneben = 0;
        var teile = new List<string>();
        foreach (var u in probanden)
        {
            if (!erste.TryGetValue(u, out var f)) { teile.Add("nie heraus"); continue; }
            bool tuer = f.Zelle.X == tx && f.Zelle.Y == ty;
            if (tuer) anTuer++; else daneben++;
            teile.Add($"Takt {f.Takt} {(tuer ? "Tuer" : $"({f.Zelle.X},{f.Zelle.Y}) DANEBEN")}");
        }
        int beside = SpawnedBesideDoor - besideVor;
        bool aOk = markiert == 3 && anTuer == 3 && daneben == 0 && maxJeTakt <= 1 && beside == 0;
        ok &= aOk;
        sb.Append($"  A freie Tuer: {markiert} Zeilen markiert, erste Zelle je Einheit [{string.Join("; ", teile)}], "
                + $"an der Tuer {anTuer}/3, daneben {daneben}, hoechstens {maxJeTakt} je Takt, SpawnedBesideDoor +{beside} "
                + $"(Soll 3/3, 0, <=1, +0){(aOk ? "" : "   ⚠")}\n");

        // ================= B: Folgezelle blockiert, Spielstand =================
        // die drei wieder hinein (die Echte Einfahren), Blockierer unter die Tuer
        foreach (var u in probanden)
            if (!u.Dead && !b.Garage.Contains(u)) Einfahren(b, _entities.IndexOf(u), u);
        bool alleDrin = probanden.TrueForAll(u => b.Garage.Contains(u));
        ausser.Clear(); ausser.Add(blocker);
        BaTuerRaeumen(tx, ty, ausser);
        AwStelle(blocker, new Vector2I(tx, ty + 1));
        erste.Clear(); maxJeTakt = 0;
        zeilen.Clear();
        foreach (var u in probanden) zeilen.Add(b.Garage.IndexOf(u));
        besideVor = SpawnedBesideDoor;
        markiert = BaAussendenKlicken(w, zeilen);
        Beobachten(200);
        int sichtbarB = erste.Count, wartendB = 0;
        foreach (var u in probanden) if (b.Garage.Contains(u) && u.Ukol == UkolVerlaesst) wartendB++;
        string zeileB = "";
        if (BasisAussendenSofort)
            foreach (var u in probanden)
                if (erste.TryGetValue(u, out var f)) zeileB += $" ({f.Zelle.X},{f.Zelle.Y})";
        bool bOk = alleDrin && sichtbarB == 0 && wartendB == 3;
        ok &= bOk;
        sb.Append($"  B Folgezelle blockiert: {markiert} markiert, sichtbar {sichtbarB}{zeileB}, wartend in der Liste mit UKOL 0x33 {wartendB}/3, "
                + $"SpawnedBesideDoor +{SpawnedBesideDoor - besideVor} (Soll 0 sichtbar, 3 wartend){(bOk ? "" : "   ⚠")}\n");

        // Spielstand hin und zurueck — im Speicher, ohne Datei (bug-548).
        if (bOk)
        {
            var schluessel = new List<int>();
            foreach (var u in probanden) schluessel.Add(u.Slot * 16 + u.Owner);
            string json = SaveStateJson("basis-aussenden-check", "probe");
            var j = new Json();
            bool gelesen = j.Parse(json) == Error.Ok && j.Data.VariantType == Variant.Type.Dictionary;
            if (gelesen) ApplySaveState(j.Data.AsGodotDictionary<string, Variant>());
            var b2 = _entities.Find(x => x.IsBuilding && !x.Dead && x.Slot == bslot && x.BType == 1);
            int wartend2 = 0;
            var probanden2 = new List<Entity>();
            if (b2 != null)
                foreach (var u in b2.Garage)
                    if (schluessel.Contains(u.Slot * 16 + u.Owner)) { probanden2.Add(u); if (u.Ukol == UkolVerlaesst) wartend2++; }
            int bl2 = _entities.FindIndex(x => !x.IsBuilding && !x.Dead && x.Slot == blSlot && x.Owner == ViewPlayer);
            bool ladenOk = gelesen && b2 != null && wartend2 == 3;
            // Blockierer weg -> jetzt muessen alle drei an der Tuer heraus
            int heraus2 = 0, anTuer2 = 0, maxJe2 = 0;
            if (ladenOk && bl2 >= 0)
            {
                var weg = _nav.NearestFree(new Vector2I(tx + 10, ty + 8), _entities[bl2].Move);
                if (weg != null) AwStelle(bl2, weg.Value);
                var erste2 = new Dictionary<Entity, Vector2I>();
                for (int t = 0; t < 400; t++)
                {
                    SimTickFuerProbe();
                    int neu = 0;
                    foreach (var u in probanden2)
                        if (!erste2.ContainsKey(u) && BaSichtbar(b2!, u)) { erste2[u] = new Vector2I(u.Col, u.Row); neu++; }
                    maxJe2 = System.Math.Max(maxJe2, neu);
                }
                heraus2 = erste2.Count;
                foreach (var z in erste2.Values) if (z.X == tx && z.Y == ty) anTuer2++;
            }
            bool lOk = ladenOk && heraus2 == 3 && anTuer2 == 3 && maxJe2 <= 1;
            ok &= lOk;
            sb.Append($"  B Spielstand (im Speicher): gelesen {gelesen}, nach dem Laden wartend {wartend2}/3; Blockierer weg -> "
                    + $"heraus {heraus2}/3, an der Tuer {anTuer2}/3, hoechstens {maxJe2} je Takt (Soll 3/3/3, <=1){(lOk ? "" : "   ⚠")}\n");
            // Nach dem Laden zeigen alle Griffe auf neue Saetze
            gb = _entities.FindIndex(x => x.IsBuilding && !x.Dead && x.Slot == bslot && x.BType == 1);
            if (gb < 0) return sb.Append("  Basis nach dem Laden verloren — C/D NICHT GEMESSEN\nbasis-aussenden-check: DURCHGEFALLEN").ToString();
            b = _entities[gb];
            blocker = bl2;
            if (blocker < 0) return sb.Append("  Blockierer nach dem Laden verloren — C/D NICHT GEMESSEN\nbasis-aussenden-check: DURCHGEFALLEN").ToString();
            bl = _entities[blocker];
            _selected = gb;
        }
        else sb.Append("  B Spielstand: uebersprungen (B nicht bestanden)\n");

        // ================= C: Fussvolk, Fahrzeug AUF der Tuer (B2) =============
        int fi = -1;
        for (int i = 0; i < _entities.Count && fi < 0; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != ViewPlayer || e.Infantry < 0) continue;
            if (Untergestellt(e) || e.InGebaeude != null) continue;
            fi = i;
        }
        if (fi < 0 && _designs != null)
        {
            // ⚠ EINGRIFF (angesagt): kein eigenes Fussvolk auf der Karte — einen
            // Soldaten aus einem baubaren Infanterieentwurf über den ALTEN
            // Depotweg erzeugen (Tür frei), nur als Proband.
            foreach (int di in BuildableFor(b.BType, b.Owner))
            {
                if (_designs[di % _designs.Count].Weapon < UI.PortraitBank.InfWeaponFrom) continue;
                ausser.Clear(); ausser.Add(blocker);
                if (_nav.OccupantAt(tx, ty) == blocker)
                {
                    var weg = _nav.NearestFree(new Vector2I(tx + 10, ty + 8), bl.Move);
                    if (weg != null) AwStelle(blocker, weg.Value);
                }
                BaTuerRaeumen(tx, ty, ausser);
                b.Depot.Add(di);
                int vorAnz = _entities.Count;
                if (SendOutOfDepot(b, b.Depot.Count - 1) && _entities.Count > vorAnz && _entities[^1].Infantry >= 0)
                {
                    fi = _entities.Count - 1;
                    sb.Append($"  C ⚠ EINGRIFF: kein Fussvolk auf der Karte — Proband »{_entities[fi].Name}« erzeugt\n");
                }
                else if (b.Depot.Count > 0 && b.Depot[^1] == di) b.Depot.RemoveAt(b.Depot.Count - 1);
                break;
            }
        }
        if (fi < 0) sb.Append("  C Fussvolk: kein eigenes Fussvolk auf der Karte — NICHT GEMESSEN\n");
        else
        {
            var soldat = _entities[fi];
            ausser.Clear(); ausser.Add(blocker);
            BaTuerRaeumen(tx, ty, ausser);
            bl.Mobile = false;
            AwStelle(blocker, new Vector2I(tx, ty));
            Einfahren(b, fi, soldat);
            bool soldatDrin = b.Garage.Contains(soldat);
            int zeile = b.Garage.IndexOf(soldat);
            int mk = soldatDrin ? BaAussendenKlicken(w, new[] { zeile }) : 0;
            Vector2I? zelleC = null;
            for (int t = 0; t < 120 && zelleC == null; t++)
            {
                SimTickFuerProbe();
                if (BaSichtbar(b, soldat)) zelleC = new Vector2I(soldat.Col, soldat.Row);
            }
            bool wartet = b.Garage.Contains(soldat) && soldat.Ukol == UkolVerlaesst;
            // und danach: Tür frei -> der Soldat tritt auf der TÜR aus
            Vector2I? zelleC2 = null;
            if (wartet)
            {
                var weg = _nav.NearestFree(new Vector2I(tx + 10, ty + 8), bl.Move);
                if (weg != null) AwStelle(blocker, weg.Value);
                for (int t = 0; t < 120 && zelleC2 == null; t++)
                {
                    SimTickFuerProbe();
                    if (BaSichtbar(b, soldat)) zelleC2 = new Vector2I(soldat.Col, soldat.Row);
                }
                sb.Append($"  C Tuer wieder frei: Soldat "
                        + (zelleC2 is { } z2 ? $"erschienen auf ({z2.X},{z2.Y}) {(z2.X == tx && z2.Y == ty ? "Tuer" : "DANEBEN")}" : "NIE erschienen") + "\n");
            }
            bool cOk = soldatDrin && mk == 1 && zelleC == null && wartet
                    && zelleC2 is { } zz && zz.X == tx && zz.Y == ty;
            ok &= cOk;
            sb.Append($"  C Fussvolk, Fahrzeug auf der Tuer: eingefahren {soldatDrin}, markiert {mk}, "
                    + (zelleC is { } zc ? $"erschienen auf ({zc.X},{zc.Y}) {(zc.X == tx && zc.Y == ty ? "Tuer" : "DANEBEN")}" : "nicht erschienen")
                    + $", wartet in der Liste {wartet} (Soll: wartet){(cOk ? "" : "   ⚠")}\n");
        }

        // ================= D: produzierte Zeile, Tuer belegt (B2) ==============
        {
            ausser.Clear(); ausser.Add(blocker);
            if (_nav.OccupantAt(tx, ty) != blocker) { BaTuerRaeumen(tx, ty, ausser); AwStelle(blocker, new Vector2I(tx, ty)); }
            var menu = BuildableFor(b.BType, b.Owner);
            if (menu.Count == 0 || _designs == null) sb.Append("  D produzierte Zeile: kein Entwurf baubar — NICHT GEMESSEN\n");
            else
            {
                b.Depot.Add(menu[0]);
                int depotVor = b.Depot.Count;
                int einheitenVor = 0;
                foreach (var e in _entities) if (!e.IsBuilding && !e.IsProp && !e.Dead) einheitenVor++;
                int zeile = b.Garage.Count + b.Depot.Count - 1;
                int mk = BaAussendenKlicken(w, new[] { zeile });
                int einheitenNach = 0;
                Entity? neu = null;
                foreach (var e in _entities)
                    if (!e.IsBuilding && !e.IsProp && !e.Dead) { einheitenNach++; neu = e; }
                bool dOk = mk == 1 && b.Depot.Count == depotVor && einheitenNach == einheitenVor;
                ok &= dOk;
                string wo = einheitenNach > einheitenVor && neu != null
                    ? $"neue Einheit auf ({neu.Col},{neu.Row}) {(neu.Col == tx && neu.Row == ty ? "Tuer" : "DANEBEN")}"
                    : "keine neue Einheit";
                sb.Append($"  D produzierte Zeile, Tuer belegt: markiert {mk}, Depot {depotVor} -> {b.Depot.Count}, {wo}, "
                        + $"Meldung »{_order}« (Soll: Absage, Zeile bleibt){(dOk ? "" : "   ⚠")}\n");
                if (b.Depot.Count == depotVor) b.Depot.RemoveAt(b.Depot.Count - 1);
            }
        }
        bl.Mobile = true;
        sb.Append($"  (Garage vorher {garageVor}, Auslass gewartet {AuslassTuerzwangGewartet}x)\n");
        sb.Append(ok ? "basis-aussenden-check: BESTANDEN" : "basis-aussenden-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}

public partial class MapViewer
{
    private string? _basisAussendenCheck;

    /// <summary>bug-591: <c>--basis-aussenden-check</c> — siehe
    /// <see cref="MapEntityLayer.BasisAussendenCheckLine"/>.</summary>
    private bool BasisAussendenCheckLauf()
    {
        if (_basisAussendenCheck == null || !_entities.ErwartungBereit()) return false;
        _basisAussendenCheck = null;
        GD.Print(_entities.BasisAussendenCheckLine(_baseWindow));
        GetTree().Quit(0);
        return true;
    }
}
