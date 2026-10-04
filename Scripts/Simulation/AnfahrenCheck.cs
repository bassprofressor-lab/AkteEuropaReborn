using System.Collections.Generic;
using System.Text;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐⭐ <b>PAKET 3 BEWEGUNG — der Prüfstand <c>--anfahren-check</c></b> (04.10.2026,
/// bug-425, KayelGee: »Im Original reagieren die Einheiten schneller auf
/// Bewegungsbefehle … bei Reborn bewegen sich Einheiten gar nicht erst, wenn der Weg
/// durch andere Einheiten blockiert ist«; Bauvorlage berichte/bewegung-anfahren-fable.md
/// §3). Kopflos, synchron über <see cref="SimTickFuerProbe"/>, Befehle über den
/// ECHTEN Absender <see cref="PostMove"/> (wie der Klick des Spielers).
/// <list type="number">
/// <item><b>Teil 1 — Einzelbefehl</b> (Karte mit &lt; 10 eigenen Einheiten, K1): nach 2 s
/// ein Fahrbefehl 9 Zellen weit. Gezählt: Takt des Satzes, des Weges, der ersten
/// Vormerkung, des ersten Fortschritts. Soll: Weg ≤ 1 Takt nach dem Klick,
/// Fortschritt nach den Drehtakten des Rumpfs (bug-407: 3·(Stufen−1);
/// <c>--koerperdrehung-alt</c>: Stufen).</item>
/// <item><b>Teil 2 — Pulk</b> (≥ 10 eigene Einheiten, K4): alle eigenen Fahrenden auf
/// EINEN Klick (Vorgabe (12,40), <c>--anfahren-check=pulk,c,r</c>). Nach 1 Takt, 2 s
/// und 10 s: Einheiten ohne Satz (Soll 0), mit <c>RetryIn &gt; 0</c> (Soll 0 nach 2 s),
/// gefahrene Zellen, »kein Weg«, Aufgeben; Histogramm des ersten Schritts.</item>
/// <item><b>Teil B — Fahrzeug auf dem Ziel</b>: eine Einheit auf die Zelle einer
/// ANDEREN eigenen Einheit. Original <c>Search:</c> @0x4D3A53: das Ziel bleibt, der Weg
/// endet auf ihr. Nullmodell <c>--zielwahl-alt</c>: Ziel vorab verlegt.</item>
/// <item><b>Teil A — Totzone</b>: ein Klick, um den im Umkreis 8 keine freie Zelle
/// liegt. Original: der Satz geht raus (Befehl 3 prüft nichts), das Ersatzziel kommt aus
/// der Ringtafel bis Radius 50. Nullmodell <c>--zielwahl-alt</c>: KEIN Satz.</item>
/// </list>
/// Nullmodelle: <c>--zielwahl-alt</c> (A und B müssen fallen), <c>--pfadkarte-alt</c>
/// (0.6.5-Suchkarte: Teil 2 RetryIn &gt; 0, B fällt, weil die Zielwahl an der
/// durchlässigen Karte hängt). Würfelabhängig (Geduld, 1/60) — Keimreihe
/// <c>--determinism-seed=1..N</c>.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--anfahren-check</c>: Ziel des Pulks (null = (12,40)).</summary>
    public static Vector2I? AnfahrenPulkZiel;

    private List<int> AnEigene()
    {
        var l = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer || !e.Mobile || e.DugIn) continue;
            if (Untergestellt(e) || e.LeavingDock >= 0) continue;
            if (e.FuelMax > 0 && e.Fuel <= 0) continue;
            l.Add(i);
        }
        return l;
    }

    private int AnPost(IEnumerable<int> wer, Vector2I zelle)
    {
        _sel.Clear(); _selAir = -1;
        foreach (int i in wer) _sel.Add(i);
        _selected = _sel.Count > 0 ? new List<int>(_sel)[0] : -1;
        return PostMove(CellCenter(zelle.X, zelle.Y));
    }

    /// <summary>⚠ Pruefstand-Eingriff: alle eigenen Einheiten ohne Auftrag auf ihre
    /// Zelle stellen (laufender Schritt verworfen).</summary>
    private void AnAlleAnhalten()
    {
        foreach (int i in AnEigene())
        {
            var e = _entities[i];
            BedRuhe(e);
            e.RetryIn = 0; e.StepCost = 0; e.Progress = 0;
            e.Goal = new Vector2I(-1, -1);
            ProbeVersetzen(i, e.Col, e.Row);
        }
    }

    private static int AnStufen(int a, int b) { int d = System.Math.Abs(a - b) % 8; return System.Math.Min(d, 8 - d); }

    public string AnfahrenCheck()
    {
        var sb = new StringBuilder("anfahren-check (bug-425)\n");
        sb.AppendLine($"  Schalter: zielwahl-alt {ZielwahlAlt}, pfadkarte neu {Simulation.NavGrid.NeuePfadkarte}, "
                    + $"koerperdrehung-alt {KoerperdrehungAlt}, geduld-satzwert {GeduldSatzwert}");
        if (ZielwahlAlt) sb.AppendLine("  ⚠ NULLMODELL --zielwahl-alt: Teil A und B MUESSEN durchfallen");
        if (!Simulation.NavGrid.NeuePfadkarte) sb.AppendLine("  ⚠ NULLMODELL --pfadkarte-alt (0.6.5): Teil 2/B MUESSEN durchfallen");
        if (_nav == null) return sb.Append("  kein Gitter\n  Ergebnis: DURCHGEFALLEN").ToString();
        bool alles = true;
        void Soll(bool ok, string was) { alles &= ok; sb.AppendLine($"  {(ok ? "ok  " : "FEHL")} {was}"); }

        var eigene = AnEigene();
        sb.AppendLine($"  eigene fahrbereite Einheiten: {eigene.Count} (Spieler {ViewPlayer})");
        if (eigene.Count == 0) return sb.Append("  Ergebnis: DURCHGEFALLEN").ToString();

        if (eigene.Count < 10) AnTeil1(sb, eigene, Soll);
        else AnTeil2(sb, eigene, Soll);

        if (eigene.Count >= 10) AnAlleAnhalten();   // Teil 2 hat alle in Fahrt gesetzt
        AnTeilB(sb, Soll);
        AnTeilA(sb, Soll);

        sb.AppendLine($"  Zaehler: Ersatzziel genommen {Simulation.NavGrid.ErsatzzielGenommen}, ohne Treffer "
                    + $"{Simulation.NavGrid.ErsatzzielOhneTreffer}, Klick ohne Satz {KlickOhneSatz}, kein Weg {KeinWegGemeldet}, "
                    + $"Aufgegeben {Aufgegeben}");
        return sb.Append($"  Ergebnis: {(alles ? "BESTANDEN" : "DURCHGEFALLEN")}").ToString();
    }

    // ---------------- Teil 1: Einzelbefehl ------------------------------------------
    private void AnTeil1(StringBuilder sb, List<int> eigene, System.Action<bool, string> Soll)
    {
        int u = -1;
        foreach (int i in eigene)
            if (_entities[i].Move == Simulation.NavGrid.MoveClass.Vehicle && _entities[i].Infantry < 0) { u = i; break; }
        if (u < 0) { Soll(false, "Teil 1: kein eigenes Fahrzeug"); return; }
        var e = _entities[u];
        for (int t = 0; t < 2 * SimHz; t++) SimTickFuerProbe();        // t = 2 s
        if (e.Dead) { Soll(false, "Teil 1: Einheit vor dem Befehl verloren"); return; }
        var von = new Vector2I(e.Col, e.Row);
        Vector2I? ziel = null;
        // zuerst die Geraden/Schraegen 9 Zellen weit (Bericht: (4,30) = 9 nach Norden),
        // dann jede Zelle in 6..10 Zellen, nach Abstand
        var offs = new List<Vector2I> { new(0, -9), new(0, 9), new(9, 0), new(-9, 0),
                                        new(6, -6), new(-6, 6), new(6, 6), new(-6, -6) };
        var rest = new List<Vector2I>();
        for (int dy = -10; dy <= 10; dy++)
            for (int dx = -10; dx <= 10; dx++)
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) >= 6) rest.Add(new Vector2I(dx, dy));
        rest.Sort((p, q) => p.LengthSquared().CompareTo(q.LengthSquared()));
        offs.AddRange(rest);
        foreach (var o in offs)
        {
            var z = von + o;
            if (!_nav!.InBounds(z.X, z.Y) || !_nav.IsFree(z.X, z.Y, e.Move, u)) continue;
            var p = _nav.FindPath(von, z, e.Move, u);
            if (p != null && p.Count > 0) { ziel = z; break; }
        }
        if (ziel == null) { Soll(false, $"Teil 1: kein freies Ziel 9 Zellen um ({von.X},{von.Y})"); return; }
        var z0 = ziel.Value;
        e.Goal = new Vector2I(-1, -1);
        int blick0 = e.Facing;
        var pos0 = e.Pos;
        int n = AnPost(new[] { u }, z0);
        int tSatz = -1, tWeg = -1, tRes = -1, tFort = -1, stufen = -1, drehs = 0, fVor = e.Facing;
        for (int t = 1; t <= 600 && tFort < 0; t++)
        {
            SimTickFuerProbe();
            if (tSatz < 0 && e.Goal == z0) tSatz = t;
            if (tWeg < 0 && e.Path is { Count: > 0 } p && e.Goal == z0)
            {
                tWeg = t;
                var s0 = p[0];
                int will = DirToFacing(new Vector2((s0.X - von.X) * TileW, (s0.Y - von.Y) * TileH));
                stufen = AnStufen(blick0, will);
            }
            if (e.Facing != fVor) { drehs++; fVor = e.Facing; }
            if (tRes < 0 && e.Reserved != null) tRes = t;
            if (tFort < 0 && e.Pos.DistanceTo(pos0) > 0.01f) tFort = t;
        }
        int soll = stufen < 0 ? -1 : KoerperdrehungAlt ? stufen : stufen <= 0 ? 0 : 3 * (stufen - 1);
        int dreh = tFort >= 0 && tWeg >= 0 ? tFort - tWeg : -1;
        sb.AppendLine($"  Teil 1: Platz {e.Slot} von ({von.X},{von.Y}) nach ({z0.X},{z0.Y}), Saetze {n}, Blick {blick0}, "
                    + $"Rumpfstufen {stufen} (gezaehlt {drehs})");
        sb.AppendLine($"          Takte ab Klick: Satz {tSatz}, Weg {tWeg}, Vormerkung {tRes}, erster Fortschritt {tFort}");
        Soll(n == 1 && tSatz == 1 && tWeg == 1, $"Teil 1: Satz und Weg im 1. Takt nach dem Klick (Original: Klick-Takt + 1, 0x415FE5/0x416242)");
        Soll(dreh >= soll && dreh <= soll + 1,
             $"Teil 1: Weg -> erster Fortschritt {dreh} Takte (Soll {soll} = Rumpfdrehung "
             + (KoerperdrehungAlt ? "1/Stufe, --koerperdrehung-alt" : "3·(Stufen−1), bug-407") + ", +1 Spiel)");
    }

    // ---------------- Teil 2: Pulk --------------------------------------------------
    private void AnTeil2(StringBuilder sb, List<int> eigene, System.Action<bool, string> Soll)
    {
        var z = AnfahrenPulkZiel ?? new Vector2I(12, 40);
        if (!_nav!.InBounds(z.X, z.Y)) { Soll(false, $"Teil 2: Ziel ({z.X},{z.Y}) ausserhalb"); return; }
        var von = new Dictionary<int, Vector2I>();
        var vorZelle = new Dictionary<int, Vector2I>();
        var erst = new Dictionary<int, int>();
        foreach (int i in eigene)
        {
            var e = _entities[i];
            e.Goal = new Vector2I(-1, -1);
            von[i] = new Vector2I(e.Col, e.Row);
            vorZelle[i] = von[i];
        }
        for (int t = 0; t < 2 * SimHz; t++) SimTickFuerProbe();        // t = 2 s (Ring laeuft)
        foreach (int i in eigene)
        {
            var e = _entities[i];
            von[i] = new Vector2I(e.Col, e.Row); vorZelle[i] = von[i];
            e.Goal = new Vector2I(-1, -1);
        }
        int kw0 = KeinWegGemeldet, ag0 = Aufgegeben, kos0 = KlickOhneSatz;
        int n = AnPost(eigene, z);
        int gefahren = 0;
        string Stand(int t)
        {
            int ohneSatz = 0, retry = 0, ohneWeg = 0, tot = 0, nah = 0;
            long abst = 0; int lebend = 0;
            foreach (int i in eigene)
            {
                var e = _entities[i];
                if (e.Dead) { tot++; continue; }
                lebend++;
                if (e.Goal.X < 0) ohneSatz++;
                if (e.RetryIn > 0) retry++;
                if (e.Path == null) ohneWeg++;
                int d = Mathf.Max(Mathf.Abs(e.Col - z.X), Mathf.Abs(e.Row - z.Y));
                abst += d; if (d <= 3) nah++;
            }
            _anOhneSatz = ohneSatz; _anRetry = retry;
            return $"nach {t,3} Takten: ohne Satz {ohneSatz}, RetryIn>0 {retry}, ohne Weg {ohneWeg}, tot {tot}, "
                 + $"gefahrene Zellen {gefahren}, <=3 am Klick {nah}, mittl. Abstand {(lebend > 0 ? (double)abst / lebend : 0):0.0}, "
                 + $"kein Weg +{KeinWegGemeldet - kw0}, Aufgegeben +{Aufgegeben - ag0}";
        }
        sb.AppendLine($"  Teil 2: {eigene.Count} Einheiten -> Klick ({z.X},{z.Y}), Saetze {n}, Klick ohne Satz +{KlickOhneSatz - kos0}");
        int ohne1 = -1, retry2 = -1;
        for (int t = 1; t <= 10 * SimHz; t++)
        {
            SimTickFuerProbe();
            foreach (int i in eigene)
            {
                var e = _entities[i];
                if (e.Dead) continue;
                var c = new Vector2I(e.Col, e.Row);
                if (c != vorZelle[i]) { gefahren++; vorZelle[i] = c; }
                if (!erst.ContainsKey(i) && (e.Reserved != null || c != von[i])) erst[i] = t;
            }
            if (t == 1) { sb.AppendLine("          " + Stand(t)); ohne1 = _anOhneSatz; }
            if (t == 2 * SimHz) { sb.AppendLine("          " + Stand(t)); retry2 = _anRetry; }
            if (t == 10 * SimHz) sb.AppendLine("          " + Stand(t));
        }
        // Histogramm des ersten Schritts
        int[] grenzen = { 1, 5, 20, 50, 100, 250, 500 };
        var fach = new int[grenzen.Length + 1];
        foreach (int i in eigene)
        {
            if (!erst.TryGetValue(i, out int t)) { fach[^1]++; continue; }
            int k = 0; while (k < grenzen.Length && t > grenzen[k]) k++;
            fach[k]++;
        }
        var h = new StringBuilder();
        int unten = 1;
        for (int k = 0; k < grenzen.Length; k++) { h.Append($"[{unten}..{grenzen[k]}] {fach[k]}  "); unten = grenzen[k] + 1; }
        h.Append($"nie {fach[^1]}");
        sb.AppendLine($"          erster Schritt (Takt): {h}");
        Soll(ohne1 == 0, $"Teil 2: nach 1 Takt ohne Satz {ohne1} (Soll 0 — das Original schickt jedem Gewaehlten Befehl 3)");
        Soll(retry2 == 0, $"Teil 2: nach 2 s mit RetryIn {retry2} (Soll 0 — durchlaessige Suchkarte, BB.1)");
    }

    private int _anOhneSatz, _anRetry;

    // ---------------- Teil B: Fahrzeug auf der Zielzelle ----------------------------
    private void AnTeilB(StringBuilder sb, System.Action<bool, string> Soll)
    {
        var eigene = AnEigene();
        int u = -1, v = -1;
        foreach (int i in eigene)
        {
            var a = _entities[i];
            if (a.Move != Simulation.NavGrid.MoveClass.Vehicle || a.Infantry >= 0 || a.Path != null || a.Reserved != null) continue;
            foreach (int j in eigene)
            {
                if (j == i) continue;
                var b = _entities[j];
                if (b.Infantry >= 0 || b.Move != Simulation.NavGrid.MoveClass.Vehicle || b.Path != null || b.Reserved != null) continue;
                if (_nav!.HullOf(j) > 1) continue;
                int d = Mathf.Max(Mathf.Abs(a.Col - b.Col), Mathf.Abs(a.Row - b.Row));
                if (d < 3 || d > 15) continue;
                // erreichbar bis neben das Ziel?
                var p = _nav.FindPath(new Vector2I(a.Col, a.Row), new Vector2I(b.Col, b.Row), a.Move, i);
                if (p == null || p.Count == 0) continue;
                u = i; v = j; break;
            }
            if (u >= 0) break;
        }
        if (u < 0) { sb.AppendLine("  Teil B: kein Paar stehender eigener Fahrzeuge in 3..15 Zellen — ungeprueft"); return; }
        var eu = _entities[u]; var ev = _entities[v];
        var zz = new Vector2I(ev.Col, ev.Row);
        int n = AnPost(new[] { u }, zz);
        for (int t = 0; t < 2; t++) SimTickFuerProbe();
        var ende = eu.Path is { Count: > 0 } pp ? pp[^1] : new Vector2I(-1, -1);
        sb.AppendLine($"  Teil B: Platz {eu.Slot} ({eu.Col},{eu.Row}) auf die Zelle von Platz {ev.Slot} ({zz.X},{zz.Y}): "
                    + $"Saetze {n}, Weg endet auf ({ende.X},{ende.Y}), Goal ({eu.Goal.X},{eu.Goal.Y})");
        Soll(n == 1 && ende == zz,
             "Teil B: der Weg endet AUF dem Fahrzeug (Search: @0x4D3A53 — Fahrzeugzelle ist zulaessiges Ziel)");
        // Ausgang beobachten (keine Messlatte): nah genug / Ausweichen
        int ag0 = Aufgegeben;
        var spur = new StringBuilder();
        Vector2I uv = new(eu.Col, eu.Row), vv = new(ev.Col, ev.Row);
        int t2 = 0;
        for (; t2 < 10 * SimHz && eu.Path != null; t2++)
        {
            SimTickFuerProbe();
            var un = new Vector2I(eu.Col, eu.Row); var vn = new Vector2I(ev.Col, ev.Row);
            if ((un != uv || vn != vv) && spur.Length < 600)
                spur.Append($" T{t2 + 1}: {eu.Slot}@({un.X},{un.Y}) {ev.Slot}@({vn.X},{vn.Y}) Beleger Ziel {_nav!.OccupantAt(zz.X, zz.Y)};");
            uv = un; vv = vn;
        }
        int dEnd = Mathf.Max(Mathf.Abs(eu.Col - zz.X), Mathf.Abs(eu.Row - zz.Y));
        if (spur.Length > 0) sb.AppendLine($"          Spur:{spur}");
        bool doppelt = eu.Col == ev.Col && eu.Row == ev.Row && !eu.Dead && !ev.Dead;
        Soll(!doppelt, $"Teil B: keine zwei Einheiten auf einer Zelle am Ende ({(doppelt ? "DOPPELT" : "getrennt")})");
        sb.AppendLine($"          nach <=10 s: steht auf ({eu.Col},{eu.Row}) = {dEnd} Zellen vom Ziel, Weg "
                    + $"{(eu.Path == null ? "fertig" : "laeuft")}, Platz {ev.Slot} jetzt ({ev.Col},{ev.Row}){(ev.Dead ? " TOT" : Untergestellt(ev) ? " untergestellt" : "")}, Aufgegeben +{Aufgegeben - ag0}");
        BedRuhe(eu);
    }

    // ---------------- Teil A: Klick in eine Totzone ---------------------------------
    private void AnTeilA(StringBuilder sb, System.Action<bool, string> Soll)
    {
        var eigene = AnEigene();
        int u = -1;
        // erst ein Fahrzeug, sonst jede eigene Landeinheit (Schiffe haben die alte Zielwahl)
        foreach (int i in eigene)
            if (_entities[i].Move == Simulation.NavGrid.MoveClass.Vehicle && _entities[i].Infantry < 0) { u = i; break; }
        if (u < 0)
            foreach (int i in eigene)
                if (_entities[i].Move != Simulation.NavGrid.MoveClass.Ship && _nav!.HullOf(i) <= 1) { u = i; break; }
        if (u < 0) { sb.AppendLine("  Teil A: keine eigene Landeinheit — ungeprueft"); return; }
        var e = _entities[u];
        var von = new Vector2I(e.Col, e.Row);
        // die naechste Zelle (Abstand zur Einheit), um die im Umkreis 8 nichts frei
        // ist, aber im Ring < 50 eine zulaessige und erreichbare Zelle liegt
        var tot = new List<Vector2I>();
        for (int r = 1; r < _nav!.Height - 1; r++)
            for (int c = 1; c < _nav.Width - 1; c++)
            {
                bool frei = false;
                for (int dy = -8; dy <= 8 && !frei; dy++)
                    for (int dx = -8; dx <= 8 && !frei; dx++)
                        frei = _nav.IsFree(c + dx, r + dy, e.Move, u);
                if (!frei) tot.Add(new Vector2I(c, r));
            }
        if (tot.Count == 0) { sb.AppendLine("  Teil A: keine Totzone (Umkreis 8 ohne freie Zelle) auf dieser Karte — ungeprueft"); return; }
        tot.Sort((p, q) => (p - von).LengthSquared().CompareTo((q - von).LengthSquared()));
        // bevorzugt eine Totzone, deren Ersatzziel erreichbar ist (hoechstens 40 Wegsuchen)
        Vector2I k = tot[0]; Vector2I? ersK = _nav.ErsatzZiel(k, e.Move, u); bool erreichbar = false;
        for (int j = 0; j < tot.Count && j < 40; j++)
        {
            var ers = _nav.ErsatzZiel(tot[j], e.Move, u);
            if (ers == null) continue;
            var p = _nav.FindPath(von, ers.Value, e.Move, u);
            if (p == null || p.Count == 0) continue;
            k = tot[j]; ersK = ers; erreichbar = true; break;
        }
        int kos0 = KlickOhneSatz;
        e.Goal = new Vector2I(-1, -1);
        int n = AnPost(new[] { u }, k);
        for (int t = 0; t < 2; t++) SimTickFuerProbe();
        var ende = e.Path is { Count: > 0 } pp ? pp[^1] : new Vector2I(-1, -1);
        sb.AppendLine($"  Teil A: Platz {e.Slot} ({von.X},{von.Y}) -> Klick ({k.X},{k.Y}) ({tot.Count} Totzonen-Zellen), im Umkreis 8 nichts frei: "
                    + $"Saetze {n}, Klick ohne Satz +{KlickOhneSatz - kos0}, Goal ({e.Goal.X},{e.Goal.Y}), Weg endet auf ({ende.X},{ende.Y}), "
                    + $"Ersatz der Ringtafel {(ersK is { } q ? $"({q.X},{q.Y})" : "keiner")}{(erreichbar ? "" : " (nicht erreichbar — Weg darf fehlen)")}");
        Soll(n == 1 && e.Goal == k && (!erreichbar || ende == ersK),
             "Teil A: der Klick wird gesendet (Befehl 3 prueft nichts) und die Wegsuche nimmt das Ersatzziel (Ringtafel 0x79A008, r < 50)");
        BedRuhe(e);
    }
}
