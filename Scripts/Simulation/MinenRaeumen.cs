namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>MINENFELDER, EINFAHRT UND RÄUMEN</b> (15.09.2026, Kampagne 15 »Minefields«).
/// Gelesen in <c>berichte/minen-opus.md</c>, beide GAME.EXE.
///
/// <list type="number">
/// <item><b>Die Mission legt ihre Minen selbst.</b> 17 von 18 Rufern von
/// <c>lay_mine</c> (C 0x421940 / F 0x420B00) stehen in den SETUP-Blöcken der
/// Missionen 15, 20, 25 — Doppelschleifen über Rechtecke, ausgelesen von
/// <c>aekernel-tools/mission_minen.py</c> nach <c>Data/mission_scripts.json</c>
/// (<c>minen</c>). Kampagnenkarten bringen keine Minen mit (Kopfbyte +3 = 1 in
/// allen 23 CWM). M15: 11 Rechtecke für Spieler 1, 837 Zellen, 458 gelegt.</item>
///
/// <item><b>Das Tor <c>word[+0x06] &lt; 0</c> ist die EINFAHRT.</b> KOLIK wird beim
/// Zellwechsel auf halbem Weg negativ (C 0x40799D) und steigt bis 0 zur Zellmitte
/// (»on square« C 0x407A4D). Bei uns wechselt Col/Row erst bei der Ankunft; die
/// Entsprechung ist: die Minenzelle ist das Ziel des laufenden Schrittes
/// (<c>Reserved</c>) und mindestens die halbe Schrittstrecke ist gefahren.</item>
///
/// <item><b>Der Minenräumer räumt beim Überfahren</b> — »on square« (C 0x407B04 /
/// F 0x407A2E): <c>+0x0E == 0x44</c> ruft <c>0x421E10(Spalte, Zeile, eigener
/// Spieler)</c>, das die erste FEINDLICHE Mine auf der eigenen Zelle freigibt und
/// Klang 40 an der Kartenstelle spielt. Kein Radius, kein Befehl, keine Dauer.
/// HELPG #058: »Wenn ein Minenentferner eine Mine passiert, entfernt er sie
/// automatisch.«</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>
    /// <b>Rollt die Einheit gerade in die Zelle (spalte, zeile) ein?</b> — die
    /// Entsprechung von <c>word[+0x06] &lt; 0</c> (C 0x42178B).
    ///
    /// <para>⚠ UNSERE Übersetzung, aber aus gelesenen Zahlen: das Original wechselt
    /// Spalte/Zeile, sobald KOLIK die Hälfte des Schrittes erreicht (80 von 160
    /// gerade, 120 von 240 schräg), und setzt ihn dann auf <c>si − (2·kosten−1)</c>,
    /// also negativ, bis zur Zellmitte. Bei uns steht <c>Progress</c> in derselben
    /// Einheit über den ganzen Schritt; »negativ« heisst hier »Progress ≥ halbe
    /// Schrittlänge, Ziel ist diese Zelle, noch nicht angekommen«.</para>
    /// </summary>
    private bool MinenEinfahrt(Entity e, int col, int row)
    {
        if (e.Reserved is not { } z || z.X != col || z.Y != row) return false;
        if (e.StepCost <= 0) return false;
        long voll = (long)e.StepCost * SimHz;
        return 2L * e.Progress >= voll && e.Progress < voll;
    }

    /// <summary>
    /// <b>0x421E10</b> — die erste FEINDLICHE Mine auf (spalte, zeile) freigeben und
    /// Klang 40 an der Kartenstelle spielen. Aufgerufen aus der Zellankunft, wenn die
    /// Einheit den Minenräumer trägt (<c>Part == 0x44</c>).
    /// <para>⚠⚠ Die Notiz in OFFENE_FRAGEN §9 (»prüft Bündnis, betrifft die eigenen
    /// Minen«) ist falsch: <c>test al,al; je Treffer</c> — Treffer bei
    /// <c>T[Leger, Räumer] == 0</c>, also Feindschaft.</para>
    /// </summary>
    private bool MinenRaeumen(int col, int row, int spieler)
    {
        for (int i = 0; i < _minen.Length; i++)
        {
            ref var m = ref _minen[i];
            if (!m.Aktiv || m.Col != col || m.Row != row) continue;
            if (!MineFeindlich(m.Player, spieler)) continue;
            m.Aktiv = false;
            MinenGeraeumt++;
            Audio.GameSounds.PlayAt(40, col, row);          // C 0x421E95, Modus 2
            return true;                                     // »eine je Ankunft«
        }
        return false;
    }

    /// <summary>Die Zellankunft (»on square«): ein Minenräumer räumt unter sich.
    /// Gegenschalter <c>--minenraeumen-aus</c>.</summary>
    private void MinenRaeumerAnkunft(Entity e)
    {
        if (MinenraeumenAus || e.Dead || e.IsBuilding || e.Part != MinenImmun) return;
        if (e.Owner is < 0 or > 7) return;
        MinenRaeumen(e.Col, e.Row, e.Owner);
    }

    /// <summary>
    /// Die Minenfelder des SETUP-Blocks legen — in der Reihenfolge des Originals
    /// (Schleifenform <c>"yx"</c> = Zeile außen), damit die 500er-Schranke dieselben
    /// Minen trifft. Gegenschalter <c>--missionsminen-aus</c>.
    /// </summary>
    public void MissionsMinenLegen(IReadOnlyList<(int X0, int X1, int Y0, int Y1, int Leger, bool ZeileAussen)> felder)
    {
        MinenLeeren();
        MissionsminenFelder.Clear();
        if (MissionsminenAus || felder.Count == 0) return;
        foreach (var f in felder)
        {
            int vor = MinenGelegt, kein = MinenKeinPlatz;
            if (f.ZeileAussen)
            {
                for (int y = f.Y0; y < f.Y1; y++)
                    for (int x = f.X0; x < f.X1; x++) MineLegen(x, y, f.Leger);
            }
            else
            {
                for (int x = f.X0; x < f.X1; x++)
                    for (int y = f.Y0; y < f.Y1; y++) MineLegen(x, y, f.Leger);
            }
            MissionsminenFelder.Add((f.X0, f.X1, f.Y0, f.Y1, f.Leger, MinenGelegt - vor, MinenKeinPlatz - kein));
        }
        GD.Print($"Minen: {MinenGelegt} gelegt aus {felder.Count} Feldern des Missionsaufbaus"
                 + (MinenKeinPlatz > 0 ? $", {MinenKeinPlatz}x kein Platz" : "")
                 + (MinenUnterFahrzeug > 0 ? $", {MinenUnterFahrzeug} unter Fahrzeugen" : ""));
    }

    /// <summary>Je Feld: Grenzen, Leger, gelegt, kein Platz — für den Prüfstand.</summary>
    public readonly List<(int X0, int X1, int Y0, int Y1, int Leger, int Gelegt, int KeinPlatz)> MissionsminenFelder = new();

    public int MinenAktiv()
    {
        int n = 0;
        foreach (var m in _minen) if (m.Aktiv) n++;
        return n;
    }

    private int MinenAufZelle(int col, int row, int spieler = -1)
    {
        int n = 0;
        foreach (var m in _minen)
            if (m.Aktiv && m.Col == col && m.Row == row && (spieler < 0 || m.Player == spieler)) n++;
        return n;
    }

    // ---- --missionsminen-check ---------------------------------------------------

    /// <summary>
    /// <c>--missionsminen-check</c>, Kampagne 15. Soll aus berichte/minen-opus.md §1.2:
    /// 458 gelegt, 0× kein Platz, 7 unter Fahrzeugen, je Feld 104/26/33/59/42/60/29/44/30/6/25.
    /// Nullmodelle: <c>--minen-legetor-alt</c> → 500 gelegt und Platzmangel;
    /// <c>--missionsminen-aus</c> → 0.
    /// </summary>
    public string MissionsminenCheck()
    {
        var sb = new System.Text.StringBuilder("missionsminen-check\n");
        EnsureMissionScript();   // die Minen kommen aus dem Missionsaufbau, nicht von der Karte
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (MissionsminenAus) sb.AppendLine("  ⚠ NULLMODELL --missionsminen-aus: hier MUSS der Lauf durchfallen");
        if (MinenLegetorAlt) sb.AppendLine("  ⚠ NULLMODELL --minen-legetor-alt: hier MUSS der Lauf durchfallen");
        int[] sollFeld = { 104, 26, 33, 59, 42, 60, 29, 44, 30, 6, 25 };
        sb.AppendLine($"  {MissionsminenFelder.Count} Felder gelegt, aktiv {MinenAktiv()}, gelegt {MinenGelegt}, "
                    + $"kein Platz {MinenKeinPlatz}, unter Fahrzeugen {MinenUnterFahrzeug}");
        for (int k = 0; k < MissionsminenFelder.Count; k++)
        {
            var f = MissionsminenFelder[k];
            string soll = k < sollFeld.Length ? $" (Soll {sollFeld[k]})" : "";
            sb.AppendLine($"    Feld {k + 1}: Spalten {f.X0}..{f.X1 - 1}, Zeilen {f.Y0}..{f.Y1 - 1}, P{f.Leger}: "
                        + $"{f.Gelegt} gelegt{soll}, {f.KeinPlatz}x kein Platz");
        }
        bool felderOk = MissionsminenFelder.Count == sollFeld.Length;
        for (int k = 0; felderOk && k < sollFeld.Length; k++) felderOk &= MissionsminenFelder[k].Gelegt == sollFeld[k];
        Soll(MinenGelegt == 458 && MinenKeinPlatz == 0, $"458 gelegt, 0x kein Platz (ist {MinenGelegt}, {MinenKeinPlatz})");
        Soll(MinenUnterFahrzeug == 7, $"7 unter Fahrzeugen (ist {MinenUnterFahrzeug})");
        Soll(felderOk, "je Feld die Zahlen aus der imap-Rechnung");
        sb.AppendLine($"  --missionsminen-aus {MissionsminenAus}, --minen-legetor-alt {MinenLegetorAlt}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    // ---- --minenfahrt-check ------------------------------------------------------

    /// <summary>
    /// <c>--minenfahrt-check</c>, Kampagne 15, der ECHTE Fahrweg (SimTick, Befehl):
    /// <list type="bullet">
    /// <item>A. ein gewöhnliches eigenes Fahrzeug fährt auf eine feindliche Mine: sie
    /// geht hoch, und zwar im Takt, in dem es die HALBE Schrittstrecke in die
    /// Minenzelle zurückgelegt hat (Nullmodell <c>--minen-tor-alt</c>: schon beim
    /// Schrittbeginn).</item>
    /// <item>B. der Minenräumer (Part 68) fährt über eine Minenreihe: jede feindliche
    /// Mine auf einer befahrenen Zelle ist geräumt, er nimmt 0 Schaden, eine EIGENE
    /// Mine auf seinem Ziel bleibt liegen (Nullmodelle <c>--minenraeumen-aus</c>:
    /// nichts geräumt; <c>--minen-ausnahme-alt</c>: er nimmt Schaden).</item>
    /// <item>C. ein Fußsoldat auf einer Mine löst nicht aus.</item>
    /// </list>
    /// ⚠ EINGRIFFE: Einheiten werden versetzt, Gottmodus bleibt aus.
    /// </summary>
    public string MinenfahrtCheck()
    {
        var sb = new System.Text.StringBuilder("minenfahrt-check\n");
        EnsureMissionScript();
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        void Takte(int n) { for (int t = 0; t < n; t++) SimTickFuerProbe(); }
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (MinenTorAlt) sb.AppendLine("  ⚠ NULLMODELL --minen-tor-alt: A MUSS scheitern");
        if (MinenraeumenAus) sb.AppendLine("  ⚠ NULLMODELL --minenraeumen-aus: B MUSS scheitern");
        if (MinenAusnahmeAlt) sb.AppendLine("  ⚠ NULLMODELL --minen-ausnahme-alt: B MUSS scheitern");
        sb.AppendLine($"  Minen aktiv beim Start: {MinenAktiv()}");
        if (MinenAktiv() == 0) return sb.Append("  keine Minen gelegt — ungeprueft\n  DURCHGEFALLEN").ToString();

        // eine feindliche Minenspalte: drei Minen untereinander, darueber eine freie Zelle
        Vector2I? Spalte(int ausser)
        {
            for (int i = 0; i < _minen.Length; i++)
            {
                var m = _minen[i];
                if (!m.Aktiv || !MineFeindlich(m.Player, ViewPlayer) || m.Col == ausser) continue;
                if (MinenAufZelle(m.Col, m.Row + 1) == 0 || MinenAufZelle(m.Col, m.Row + 2) == 0) continue;
                if (MinenAufZelle(m.Col, m.Row - 1) > 0) continue;
                if (_nav.BesetztVon(m.Col, m.Row - 1) >= 0 || _nav.BesetztVon(m.Col, m.Row) >= 0
                    || _nav.BesetztVon(m.Col, m.Row + 1) >= 0 || _nav.BesetztVon(m.Col, m.Row + 2) >= 0
                    || _nav.BesetztVon(m.Col, m.Row + 3) >= 0) continue;
                if (_nav.GroundAt(m.Col, m.Row - 1) != Simulation.NavGrid.Ground.Free) continue;
                if (_nav.GroundAt(m.Col, m.Row + 3) != Simulation.NavGrid.Ground.Free) continue;
                return new Vector2I(m.Col, m.Row);
            }
            return null;
        }
        int Eigene(bool raeumer, bool fuss)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || !e.Mobile || e.Owner != ViewPlayer || Untergestellt(e)) continue;
                if (fuss != (e.Infantry >= 0)) continue;
                if (!fuss && raeumer != (e.Part == MinenImmun)) continue;
                if (!fuss && e.Move == Simulation.NavGrid.MoveClass.Ship) continue;
                return i;
            }
            return -1;
        }
        void Ruhe(Entity e) { e.Path = null; e.Orders.Clear(); e.Target = -1; e.StepCost = 0; e.Progress = 0; }

        // ---- A. gewoehnliches Fahrzeug -------------------------------------------
        var sa = Spalte(-1);
        int ai = Eigene(raeumer: false, fuss: false);
        if (sa == null || ai < 0) Soll(false, $"A: keine Minenspalte ({sa}) oder kein Fahrzeug ({ai})");
        else
        {
            var a = _entities[ai];
            ProbeVersetzen(ai, sa.Value.X, sa.Value.Y - 1); Ruhe(a);
            if (a.FuelMax > 0) a.Fuel = a.FuelMax;
            _sel.Clear(); _sel.Add(ai); _selected = ai;
            int hp0 = a.Hp, aus0 = MinenAusgeloest;
            bool posted = PostMoveOne(ai, CellCenter(sa.Value.X, sa.Value.Y), false);
            string beiAusloesung = "nie";
            bool halb = false;
            int t = 0;
            // Vor jedem Takt merken, wo die Einheit steht: der Minentakt laeuft VOR der
            // Bewegung, sieht also genau diesen Zustand.
            while (t < 600 && MinenAusgeloest == aus0 && !a.Dead)
            {
                int prog = a.Progress, cost = a.StepCost;
                var res = a.Reserved;
                SimTickFuerProbe(); t++;
                if (MinenAusgeloest != aus0)
                {
                    long voll = (long)cost * SimHz;
                    halb = res == sa && voll > 0 && 2L * prog >= voll;
                    beiAusloesung = $"Takt {t}, Ziel {(res?.ToString() ?? "-")}, Schritt {prog}/{voll} ({(voll > 0 ? 100.0 * prog / voll : 0):0}%)";
                }
            }
            Soll(posted && MinenAusgeloest == aus0 + 1 && (a.Hp < hp0 || a.Dead),
                 $"A: Fahrzeug Platz {a.Slot} faehrt auf die Mine ({sa.Value.X},{sa.Value.Y}): ausgeloest {MinenAusgeloest - aus0}, TP {hp0} -> {a.Hp}");
            Soll(halb, $"A: ausgeloest erst ab halber Strecke in die Minenzelle — {beiAusloesung}");
            _sel.Clear(); _selected = -1;
        }

        // ---- B. der Minenraeumer --------------------------------------------------
        var sb2 = Spalte(sa?.X ?? -1);
        int ri = Eigene(raeumer: true, fuss: false);
        if (sb2 == null || ri < 0) Soll(false, $"B: keine zweite Minenspalte ({sb2}) oder kein Minenraeumer ({ri})");
        else
        {
            var r = _entities[ri];
            var start = new Vector2I(sb2.Value.X, sb2.Value.Y - 1);
            var ziel = new Vector2I(sb2.Value.X, sb2.Value.Y + 3);
            ProbeVersetzen(ri, start.X, start.Y); Ruhe(r);
            if (r.FuelMax > 0) r.Fuel = r.FuelMax;
            MineLegen(ziel.X, ziel.Y, r.Owner);                     // eine EIGENE Mine auf dem Ziel
            int hp0 = r.Hp, ger0 = MinenGeraeumt, opf0 = MinenOpfer.Count;
            var besucht = new HashSet<Vector2I>();
            var feindVorher = new Dictionary<Vector2I, int>();
            for (int dy = 0; dy <= 3; dy++)
            {
                var c = new Vector2I(sb2.Value.X, sb2.Value.Y + dy);
                int n = 0;
                foreach (var m in _minen) if (m.Aktiv && m.Col == c.X && m.Row == c.Y && MineFeindlich(m.Player, r.Owner)) n++;
                feindVorher[c] = n;
            }
            _sel.Clear(); _sel.Add(ri); _selected = ri;
            bool posted = PostMoveOne(ri, CellCenter(ziel.X, ziel.Y), false);
            int t = 0;
            while (t < 1500 && !(r.Col == ziel.X && r.Row == ziel.Y && r.Path == null) && !r.Dead)
            {
                SimTickFuerProbe(); t++;
                besucht.Add(new Vector2I(r.Col, r.Row));
            }
            int sollGeraeumt = 0, nochDa = 0;
            foreach (var (c, n) in feindVorher)
            {
                if (!besucht.Contains(c) || n == 0) continue;
                sollGeraeumt++;
                foreach (var m in _minen) if (m.Aktiv && m.Col == c.X && m.Row == c.Y && MineFeindlich(m.Player, r.Owner)) nochDa++;
            }
            int geraeumt = MinenGeraeumt - ger0;
            Soll(posted && sollGeraeumt > 0 && geraeumt >= sollGeraeumt && nochDa == 0,
                 $"B: Minenraeumer Platz {r.Slot} von ({start.X},{start.Y}) nach ({ziel.X},{ziel.Y}) in {t} Takten, "
                 + $"steht ({r.Col},{r.Row}): befahrene Minenzellen {sollGeraeumt}, geraeumt {geraeumt}, dort noch liegend {nochDa}");
            int raeumerMinen = MinenOpfer.GetRange(opf0, MinenOpfer.Count - opf0).FindAll(x => x == ri).Count;
            // ⚠ Gemessen wird die AUSLOESUNG am Raeumer, nicht seine TP: auf der Karte
            // steht Feindbeschuss (erster Lauf: TP 60 -> 14 bei 0 Minentreffern).
            Soll(raeumerMinen == 0, $"B: keine Mine loest am Raeumer aus: {raeumerMinen}x (TP {hp0} -> {r.Hp}, Ausnahme griff {MinenImmunFall}x)");
            Soll(MinenAufZelle(ziel.X, ziel.Y, r.Owner) == 1, $"B: die EIGENE Mine auf ({ziel.X},{ziel.Y}) liegt noch: {MinenAufZelle(ziel.X, ziel.Y, r.Owner)}");
            _sel.Clear(); _selected = -1;
        }

        // ---- C. Fussvolk -----------------------------------------------------------
        int fi = Eigene(raeumer: false, fuss: true);
        var sc = Spalte(-1);
        if (fi < 0 || sc == null) sb.AppendLine("  (C entfaellt: kein Fusssoldat oder keine freie Minenspalte)");
        else
        {
            var f = _entities[fi];
            ProbeVersetzen(fi, sc.Value.X, sc.Value.Y - 1); Ruhe(f);
            int hp0 = f.Hp, aus0 = MinenAusgeloest, fuss0 = MinenFussvolk, opf0 = MinenOpfer.Count;
            PostMoveOne(fi, CellCenter(sc.Value.X, sc.Value.Y + 1), false);
            Takte(600);
            int fussMinen = MinenOpfer.GetRange(opf0, MinenOpfer.Count - opf0).FindAll(x => x == fi).Count;
            Soll(fussMinen == 0 && MinenFussvolk > fuss0,
                 $"C: Fusssoldat Platz {f.Slot} ueber ({sc.Value.X},{sc.Value.Y}): ausgeloest {MinenAusgeloest - aus0}, TP {hp0} -> {f.Hp}, "
                 + $"als Fussvolk uebergangen {MinenFussvolk - fuss0}x, steht ({f.Col},{f.Row})");
        }

        sb.AppendLine($"  --minen-tor-alt {MinenTorAlt}, --minenraeumen-aus {MinenraeumenAus}, --minen-ausnahme-alt {MinenAusnahmeAlt}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
