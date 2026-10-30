namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>--fussvolk-uhr-check</b> (30.10.2026, bug-688). Hergestellt, nicht gesucht: ein eigener
/// Fusssoldat F (Reichweite +0x2B 3..6, also Ringzaehler 36..136) und zwei feindliche
/// Fahrzeuge X, Y auf einem freien Streifen; X und Y festgeheftet (je Takt zurueckgestellt,
/// Nachladen gesperrt), alle mit 30000 TP. Gezaehlt wird in <c>Fire</c>.
/// <list type="bullet">
/// <item>U1 — F geht auf X (22 Zellen), Y 2 Zeilen neben dem Weg: Soll Schuesse F→Y IM GEHEN
///   &gt; 0 (Ankunftsarm 0x407D27), Auftragsziel bleibt X. Nullmodell --fussvolk-uhr-alt: 0.</item>
/// <item>U2 — F geht auf X (16 Zellen), sonst niemand: Soll Halt (0x407D48) mit X im
///   Schussfenster, mehr als 1,5 Zellen vor X, Schuss auf X. Nullmodell --fussvolk-halt-aus:
///   F geht bis an X heran (≤ 1,5).</item>
/// <item>U3 — F steht ohne Auftrag, X auf (−3,0) (Tafelstelle 21, Abstand 3,0), Y auf (−2,−2)
///   (Stelle 33, Abstand 2,83): Soll erster Schuss auf X (Tafelreihenfolge). Nullmodell
///   --fussvolk-uhr-alt (naechster): Y.</item>
/// <item>U3b — F steht neben einem feindlichen Gebaeude: Soll 0 Schuesse auf Gebaeude
///   (60000er nur als Auftragsziel, 0x40F312). Nullmodell --fussvolk-uhr-gebaeude: &gt; 0.</item>
/// <item>U4 — Takt: Y tritt zehnmal auf (1,0) (Stelle 3) in den Ring, F nachgeladen: Soll erster Schuss im selben
///   Takt (hoechstens 1 Takt). Nullmodell --fussvolk-uhr-alt (0,4 s): hoechster Verzug &gt; 1.</item>
/// <item>U5 — F steht mit Auftrag auf X (3 Zellen), Y vor ihm (Stelle 3): Soll F→Y &gt; 0 und
///   F→X = 0 (keine Rueckschaltung). Nullmodell --fussvolk-uhr-alt: F→Y = 0.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    private int _fuP = -1, _fuX = -1, _fuY = -1;
    private int _fuPX, _fuPY, _fuPYGehen, _fuPGeb, _fuErstes = -2;

    /// <summary>Aus <c>Fire</c>: Schuesse des Probanden zaehlen.</summary>
    private void FuProbeZaehlen(int si, int vi)
    {
        if (_fuP < 0 || si != _fuP) return;
        if (_fuErstes == -2) _fuErstes = vi;
        if (vi == _fuX) _fuPX++;
        else if (vi == _fuY) { _fuPY++; if (_entities[si].Path != null) _fuPYGehen++; }
        if (vi >= 0 && vi < _entities.Count && _entities[vi].IsBuilding) _fuPGeb++;
    }

    public string FussvolkUhrCheck()
    {
        var sb = new System.Text.StringBuilder("fussvolk-uhr-check"
            + (FussvolkUhrAlt ? " — NULLMODELL --fussvolk-uhr-alt" : "")
            + (FussvolkHaltAus ? " — NULLMODELL --fussvolk-halt-aus" : "")
            + (FussvolkUhrGebaeude ? " — NULLMODELL --fussvolk-uhr-gebaeude" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL: keine Karte").ToString();
        bool okGeb = FuGebaeudeFall(sb);
        int fi = -1;
        for (int i = 0; i < _entities.Count && fi < 0; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || Untergestellt(e) || !CanFight(e)) continue;
            if (e.Owner != ViewPlayer || e.Infantry < 0) continue;
            if (e.Range >= 3 && e.Range <= 6 && RangeOf(e) >= 3f && RangeMinOf(e) <= 1f) fi = i;
        }
        if (fi < 0)
        {
            var rw = new System.Collections.Generic.SortedSet<int>();
            foreach (var e in _entities) if (!e.Dead && e.Infantry >= 0 && e.Owner == ViewPlayer) rw.Add(e.Range);
            return sb.Append($"  KEIN URTEIL U1..U5: kein eigener Fusssoldat mit Reichweite 3..6 (eigene Fussvolk-Reichweiten: {string.Join(",", rw)})").ToString();
        }
        var F = _entities[fi];
        var feinde = new System.Collections.Generic.List<int>();
        for (int i = 0; i < _entities.Count && feinde.Count < 2; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || Untergestellt(e) || e.Infantry >= 0) continue;
            if (IsHostile(F, e)) feinde.Add(i);
        }
        if (feinde.Count < 2) return sb.Append("  KEIN URTEIL: keine zwei feindlichen Fahrzeuge").ToString();
        _fuX = feinde[0]; _fuY = feinde[1];
        var X = _entities[_fuX]; var Y = _entities[_fuY];
        int sc = -1, sr = -1;
        for (int r = 4; r < _nav.Height - 4 && sc < 0; r++)
            for (int c = 16; c < _nav.Width - 36 && sc < 0; c++)
            {
                bool frei = true;
                for (int dy = -2; dy <= 2 && frei; dy++)
                    for (int dx = -14; dx <= 24 && frei; dx++)
                        frei = _nav.CanEnter(c + dx, r + dy, F.Move) && _nav.CanEnter(c + dx, r + dy, X.Move)
                               && _nav.OccupantAt(c + dx, r + dy) < 0;
                if (frei) { sc = c; sr = r; }
            }
        if (sc < 0) return sb.Append("  KEIN URTEIL: kein freier Streifen 39x5").ToString();
        int R = F.Range, n = FussRingZaehler[R];
        sb.AppendLine($"  F Platz {F.Slot} ({LabelOf(F)}, Reichweite +0x2B {R} -> Ringzaehler {n}, Schussfenster {RangeMinOf(F):0.0}..{RangeOf(F):0.0}, "
                    + $"Waffe {F.Weapon}); X Platz {X.Slot} ({LabelOf(X)}, Sp{X.Owner}), Y Platz {Y.Slot} ({LabelOf(Y)}, Sp{Y.Owner}); Streifen ab ({sc},{sr})");
        foreach (int k in new[] { fi, _fuX, _fuY }) { var e = _entities[k]; e.HpMax = e.Hp = 30000; }

        Vector2I xz = default, yz = default;
        void Stellen(int idx, Vector2I z)
        {
            var e = _entities[idx];
            e.Path = null; e.Orders.Clear(); e.Target = -1; e.Ordered = false; e.Schussziel = -1; e.RestZiel = null;
            ProbeVersetzen(idx, z.X, z.Y);
        }
        void Heften()
        {
            foreach (var (k, z) in new[] { (_fuX, xz), (_fuY, yz) })
            {
                var e = _entities[k];
                if (e.Col != z.X || e.Row != z.Y) ProbeVersetzen(k, z.X, z.Y);
                e.Path = null; e.Target = -1; e.Cooldown = 99f;
            }
        }
        void Befehl(int ziel)
        {
            F.Target = ziel; F.Ordered = true; F.Path = null; F.Reserved = null; F.Orders.Clear();
            F.RestZiel = null; F.Schussziel = -1; F.ChaseRetry = 0; AuftragLoeschen(F);
        }
        void Frei()
        {
            F.Target = -1; F.Ordered = false; F.Path = null; F.RestZiel = null; F.Schussziel = -1;
            F.RetryIn = 0; F.ChaseRetry = 0; F.Goal = new Vector2I(F.Col, F.Row); F.Orders.Clear();
        }
        void Null() { _fuPX = _fuPY = _fuPYGehen = _fuPGeb = 0; _fuErstes = -2; }
        bool ok = okGeb;
        _fuP = fi;
        Vector2I weit = new(sc - 13, sr - 2);

        // ---- U1 im Gehen --------------------------------------------------------
        Stellen(fi, new Vector2I(sc, sr));
        xz = new Vector2I(sc + 22, sr); yz = new Vector2I(sc + 8, sr + 2);
        Stellen(_fuX, xz); Stellen(_fuY, yz); Heften();
        F.Cooldown = 0; Null(); Befehl(_fuX);
        int an0 = FuAnkuenfte;
        for (int t = 0; t < 400; t++) { Heften(); SimTickFuerProbe(); }
        bool u1 = _fuPYGehen > 0 && F.Target == _fuX;
        ok &= u1;
        sb.AppendLine($"  U1 geht auf X, Y neben dem Weg: F->Y im Gehen {_fuPYGehen} (gesamt {_fuPY}), F->X {_fuPX}, Auftragsziel X {F.Target == _fuX}, "
                    + $"F jetzt ({F.Col},{F.Row}), Ankuenfte {FuAnkuenfte - an0} — Soll >0 und Ziel X (Nullmodell --fussvolk-uhr-alt: 0) -> {(u1 ? "ja" : "NEIN")}");

        // ---- U2 Halt beim Auftragsziel ------------------------------------------
        Stellen(fi, new Vector2I(sc, sr));
        xz = new Vector2I(sc + 16, sr); yz = weit;
        Stellen(_fuX, xz); Stellen(_fuY, yz); Heften();
        F.Cooldown = 0; Null(); Befehl(_fuX);
        int halt0 = FuHalte, haltTakt = -1, gingLos = -1;
        float haltAbst = -1f;
        for (int t = 0; t < 600; t++)
        {
            Heften(); SimTickFuerProbe();
            if (F.Path != null && gingLos < 0) gingLos = t;
            if (gingLos >= 0 && F.Path == null && haltTakt < 0) { haltTakt = t; haltAbst = CellDistance(F, X); }
        }
        bool u2 = FuHalte > halt0 && haltTakt >= 0 && haltAbst > 1.5f && InFiringWindow(F, haltAbst) && _fuPX > 0;
        ok &= u2;
        sb.AppendLine($"  U2 Halt beim Auftragsziel: Halte {FuHalte - halt0}, steht ab Takt {haltTakt} in {haltAbst:0.0} Zellen vor X, F->X {_fuPX} — "
                    + $"Soll Halt > 1,5 Zellen vor X im Fenster (Nullmodell --fussvolk-halt-aus: <= 1,5) -> {(u2 ? "ja" : "NEIN")}");

        // ---- U3 Tafelreihenfolge ------------------------------------------------
        Stellen(fi, new Vector2I(sc, sr)); Frei();
        xz = new Vector2I(sc - 3, sr); yz = new Vector2I(sc - 2, sr - 2);
        Stellen(_fuX, xz); Stellen(_fuY, yz); Heften();
        F.Cooldown = 0; Null();
        for (int t = 0; t < 60; t++) { Heften(); SimTickFuerProbe(); }
        string Wer(int v) => v == _fuX ? "X" : v == _fuY ? "Y" : v == -2 ? "keiner" : $"#{v}";
        bool u3 = _fuErstes == _fuX;
        ok &= u3;
        sb.AppendLine($"  U3 Tafelreihenfolge: X (-3,0) Stelle {FussRingStelle(-3, 0)} Abstand 3,0 / Y (-2,-2) Stelle {FussRingStelle(-2, -2)} Abstand 2,83: "
                    + $"erster Schuss auf {Wer(_fuErstes)} (F->X {_fuPX}, F->Y {_fuPY}) — Soll X (Nullmodell --fussvolk-uhr-alt: Y) -> {(u3 ? "ja" : "NEIN")}");

        // ---- U4 Takt ------------------------------------------------------------
        Stellen(fi, new Vector2I(sc, sr)); Frei();
        xz = weit; yz = new Vector2I(sc + 20, sr);
        Stellen(_fuX, xz); Stellen(_fuY, yz); Heften();
        var verzug = new System.Collections.Generic.List<int>();
        bool sgU4 = Schussgruende; Schussgruende = true;
        for (int m = 0; m < 10; m++)
        {
            yz = new Vector2I(sc + 20, sr);
            int warten = 30 + (m * 7) % 20;
            for (int t = 0; t < warten; t++) { Heften(); SimTickFuerProbe(); }
            yz = new Vector2I(sc + 1, sr);              // Tafelstelle 3: vor jedem Kartenfeind ausser (0,0)/(−1,0)/(0,−1)
            // frisch nachgeladen und leer: die Karte hat eigene Feinde, die er sonst haelt
            F.Cooldown = 0; F.Schussziel = -1; F.Target = -1; F.Ordered = false;
            Heften(); Null();
            int d = -1;
            for (int t = 0; t < 40 && d < 0; t++) { Heften(); SimTickFuerProbe(); if (_fuErstes != -2) d = t; }   // erster Schuss ueberhaupt: die Karte hat eigene Feinde, die frueher in der Tafel stehen koennen
            verzug.Add(d);
            if (d < 0 || d > 1) sb.AppendLine($"    (Eintritt {m}: F ({F.Col},{F.Row}) Weg {F.Path != null} Ziel {F.Target} Schussziel {F.Schussziel} cd {F.Cooldown:0.00} Drehen {F.DrehWarten} Ukol {F.Ukol} Y ({Y.Col},{Y.Row}) Abstand {CellDistance(F, Y):0.0} Grund: {F.Schussgrund})");
        }
        Schussgruende = sgU4;
        int dmax = -1; bool alle = true;
        foreach (int d in verzug) { if (d < 0) alle = false; dmax = System.Math.Max(dmax, d); }
        bool u4 = alle && dmax <= 1;
        ok &= u4;
        sb.AppendLine($"  U4 Takt (10 Eintritte in den Ring): Verzug bis zum ersten Schuss (auf irgendwen) [{string.Join(", ", verzug)}] Takte, hoechstens {dmax} — "
                    + $"Soll <= 1 (Nullmodell --fussvolk-uhr-alt 0,4 s: > 1) -> {(u4 ? "ja" : "NEIN")}");

        // ---- U5 keine Rueckschaltung im Stand -----------------------------------
        Stellen(fi, new Vector2I(sc, sr)); Frei();
        xz = new Vector2I(sc + 3, sr); yz = new Vector2I(sc + 1, sr);
        Stellen(_fuX, xz); Stellen(_fuY, yz); Heften();
        F.Cooldown = 0; Null(); Befehl(_fuX);
        for (int t = 0; t < 200; t++) { Heften(); SimTickFuerProbe(); }
        bool u5 = _fuPY > 0 && _fuPX == 0 && F.Target == _fuX;
        ok &= u5;
        sb.AppendLine($"  U5 Stand mit Auftrag X (3,0) Stelle {FussRingStelle(3, 0)}, Y (1,0) Stelle {FussRingStelle(1, 0)}: F->Y {_fuPY}, F->X {_fuPX}, "
                    + $"Auftragsziel X {F.Target == _fuX}, F ({F.Col},{F.Row}) — Soll Y>0, X=0 (Nullmodell --fussvolk-uhr-alt: Y=0) -> {(u5 ? "ja" : "NEIN")}");

        _fuP = -1;
        sb.AppendLine("  " + FussvolkUhrLine());
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>U3b: ein eigener Fusssoldat (Reichweite 1..6) auf einer freien Zelle neben einem
    /// feindlichen Gebaeude, ohne Auftrag, 150 Takte. Soll 0 Schuesse auf Gebaeude. true = ok
    /// (auch »entfaellt«).</summary>
    private bool FuGebaeudeFall(System.Text.StringBuilder sb)
    {
        int fi = -1, bi = -1; Vector2I bz = default; int feindGeb = 0;
        for (int i = 0; i < _entities.Count && fi < 0; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || Untergestellt(e) || !CanFight(e)) continue;
            if (e.Owner == ViewPlayer && e.Infantry >= 0 && e.Range >= 1 && e.Range <= 6) fi = i;
        }
        if (fi < 0) { sb.AppendLine("  U3b entfaellt: kein eigener Fusssoldat mit Reichweite 1..6"); return true; }
        var F = _entities[fi];
        int n = FussRingZaehler[F.Range];
        for (int i = 0; i < _entities.Count && bi < 0; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.Dead || !IsHostile(F, b)) continue;
            feindGeb++;
            int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
            for (int dy = -1; dy <= h && bi < 0; dy++)
                for (int dx = -1; dx <= w && bi < 0; dx++)
                {
                    if (dx >= 0 && dx < w && dy >= 0 && dy < h) continue;
                    int c = b.Col + dx, r = b.Row + dy;
                    if (!_nav.CanEnter(c, r, F.Move) || _nav.OccupantAt(c, r) >= 0) continue;
                    var probe = new Entity { Col = c, Row = r };
                    if (FussRingStelleVon(probe, b) > n || !InFiringWindow(F, FussGebaeudeAbstand(probe, b))) continue;
                    bi = i; bz = new Vector2I(c, r);
                }
        }
        if (bi < 0) { sb.AppendLine($"  U3b entfaellt: kein feindliches Gebaeude mit freier Nachbarzelle (feindliche Gebaeude {feindGeb})"); return true; }
        var B = _entities[bi];
        F.HpMax = F.Hp = 30000;
        F.Target = -1; F.Ordered = false; F.Path = null; F.Orders.Clear(); F.Schussziel = -1; F.RestZiel = null;
        ProbeVersetzen(fi, bz.X, bz.Y);
        F.RetryIn = 0; F.ChaseRetry = 0; F.Goal = bz; F.Cooldown = 0;
        _fuP = fi; _fuX = _fuY = -1; _fuPGeb = 0; _fuErstes = -2;
        int s0 = FuSchuesse, a0 = DebugShots;
        for (int t = 0; t < 150; t++) { if (F.Col != bz.X || F.Row != bz.Y) ProbeVersetzen(fi, bz.X, bz.Y); F.Path = null; SimTickFuerProbe(); }
        bool u3b = _fuPGeb == 0;
        sb.AppendLine($"  U3b F Platz {F.Slot} ({LabelOf(F)}, Reichweite {F.Range}) neben Gebaeude Platz {B.Slot} ({LabelOf(B)}, Sp{B.Owner}) auf ({bz.X},{bz.Y}), "
                    + $"Stelle {FussRingStelleVon(F, B)}: Schuesse auf Gebaeude {_fuPGeb}, Uhr-Schuesse gesamt {FuSchuesse - s0} — "
                    + $"Soll 0 (Nullmodell --fussvolk-uhr-gebaeude: >0) -> {(u3b ? "ja" : "NEIN")}");
        _fuP = -1;
        return u3b;
    }
}
