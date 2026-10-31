namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>--gelegenheitsfeuer-check</b> (29.10.2026, bug-645/646, Faelle G1..G5 aus
/// <c>berichte/ki-zielwahl-r8-opus.md</c> §3 B1, G5 = Restauftrag). Hergestellt, nicht
/// gesucht: ein eigenes Fahrzeug A (Turm, Reichweite 3..10, keine Mindestreichweite)
/// und zwei Feinde X (Auftragsziel) und Y (Gelegenheit) werden auf einen freien,
/// befahrbaren Streifen gestellt; X und Y sind festgeheftet (je Takt zurueckgestellt,
/// Nachladen gesperrt, ausser in G3), alle drei mit 30000 TP.
/// <list type="bullet">
/// <item>G1 — A faehrt auf X (22 Zellen), Y 2 Zeilen neben dem Weg in A's Reichweite:
///   Soll Schuesse A→Y &gt; 0, davon 0 im Stand, A's Ziel bleibt X. Nullmodell 0.</item>
/// <item>G2 — X und Y beide in Reichweite, Y naeher: Soll ≥ 90 % der Schuesse auf X.
///   Nullmodell 100 %.</item>
/// <item>G3 — Y beschiesst A von hinten aus AUSSERHALB A's Reichweite (Y.Range Eingriff):
///   Soll 0 Schuesse A→Y, kein Selbstverteidiger-Eintrag, A faehrt weiter. Nullmodell gleich
///   (= K4-Rest originaltreu).</item>
/// <item>G4 — ein eigener Fusssoldat mit Auftrag auf X, Y neben ihm: 0 Schuesse im Gehen;
///   nach »Halt« &gt; 0. Nullmodell gleich.</item>
/// <item>G5 — X stirbt (TP 1): Soll Restauftrag, A steht danach hoechstens 1,5 Zellen von
///   der Todeszelle, nicht untaetig. Nullmodell: kein Restauftrag, A bleibt stehen.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    public static bool GelegenheitsfeuerCheckAn;

    private int _gfpA = -1, _gfpX = -1, _gfpY = -1;
    private int _gfpAX, _gfpAY, _gfpYA, _gfpStandY, _gfpAlle;

    /// <summary>Aus <c>Fire</c>: Schuesse der Probanden zaehlen.</summary>
    private void GfProbeZaehlen(int si, int vi)
    {
        if (_gfpA < 0) return;
        if (si == _gfpA)
        {
            _gfpAlle++;
            if (vi == _gfpX) _gfpAX++;
            else if (vi == _gfpY) { _gfpAY++; if (_entities[si].Path == null) _gfpStandY++; }
        }
        else if (si == _gfpY && vi == _gfpA) _gfpYA++;
    }

    public string GelegenheitsfeuerCheck()
    {
        var sb = new System.Text.StringBuilder("gelegenheitsfeuer-check"
            + (GelegenheitsfeuerAlt ? " — NULLMODELL --gelegenheitsfeuer-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL: keine Karte").ToString();
        // ---- die Probanden -----------------------------------------------------
        int ai = -1, fi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || Untergestellt(e) || !CanFight(e)) continue;
            if (e.Owner != ViewPlayer) continue;
            float r = RangeOf(e);
            if (ai < 0 && e.Infantry < 0 && e.Weapon != 0 && e.GameUnitType == 0 && r >= 3 && r <= 10
                && RangeMinOf(e) <= 1 && WeaponRowOf(e.Weapon) != 8 && !GaswerferSchweigt(e)) ai = i;
            if (fi < 0 && e.Infantry >= 0 && r >= 2 && RangeMinOf(e) <= 1) fi = i;
        }
        if (ai < 0) return sb.Append("  KEIN URTEIL: kein eigenes Fahrzeug mit Turm, Reichweite 3..10").ToString();
        var A = _entities[ai];
        int feindSp = -1;
        var feinde = new System.Collections.Generic.List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || Untergestellt(e) || e.Infantry >= 0) continue;
            if (!IsHostile(A, e)) continue;
            if (feindSp < 0) feindSp = e.Owner;
            feinde.Add(i);
            if (feinde.Count == 3) break;
        }
        if (feinde.Count < 2) return sb.Append("  KEIN URTEIL: keine zwei feindlichen Fahrzeuge").ToString();
        _gfpX = feinde[0]; _gfpY = feinde[1];
        var X = _entities[_gfpX]; var Y = _entities[_gfpY];
        // ---- der Streifen: 34 x 9 Zellen befahrbar und frei ---------------------
        int sc = -1, sr = -1;
        for (int r = 4; r < _nav.Height - 4 && sc < 0; r++)
            for (int c = 16; c < _nav.Width - 36 && sc < 0; c++)
            {
                bool frei = true;
                for (int dy = -2; dy <= 2 && frei; dy++)
                    for (int dx = -14; dx <= 24 && frei; dx++)
                        frei = _nav.CanEnter(c + dx, r + dy, A.Move) && _nav.OccupantAt(c + dx, r + dy) < 0;
                if (frei) { sc = c; sr = r; }
            }
        if (sc < 0) return sb.Append("  KEIN URTEIL: kein freier Streifen 39x5").ToString();
        int rng = Mathf.FloorToInt(RangeOf(A));
        sb.AppendLine($"  A Platz {A.Slot} ({LabelOf(A)}, Reichweite {RangeOf(A):0.0}, Waffe {A.Weapon}); X Platz {X.Slot} "
                    + $"({LabelOf(X)}, Sp{X.Owner}), Y Platz {Y.Slot} ({LabelOf(Y)}, Sp{Y.Owner}); Streifen ab ({sc},{sr})");
        foreach (int k in new[] { ai, _gfpX, _gfpY }) { var e = _entities[k]; e.HpMax = e.Hp = 30000; }
        if (fi >= 0) _entities[fi].HpMax = _entities[fi].Hp = 30000;

        Vector2I xz = default, yz = default;
        bool yFrei = false;
        void Stellen(int idx, Vector2I z) { var e = _entities[idx]; e.Path = null; e.Orders.Clear(); e.Target = -1; e.Ordered = false; ProbeVersetzen(idx, z.X, z.Y); }
        void Heften()
        {
            foreach (var (k, z) in new[] { (_gfpX, xz), (_gfpY, yz) })
            {
                var e = _entities[k];
                if (e.Col != z.X || e.Row != z.Y) ProbeVersetzen(k, z.X, z.Y);
                e.Path = null;
                if (!(k == _gfpY && yFrei)) e.Cooldown = 99f;
            }
        }
        void Befehl(int idx, int ziel)
        {
            var e = _entities[idx];
            e.Target = ziel; e.Ordered = true; e.Path = null; e.Reserved = null; e.Orders.Clear();
            e.RestZiel = null; e.Schussziel = -1; AuftragLoeschen(e);   // frischer Stand je Fall
        }
        void Null() { _gfpAX = _gfpAY = _gfpYA = _gfpStandY = 0; }
        bool ok = true;

        // ---- G1 -----------------------------------------------------------------
        Stellen(ai, new Vector2I(sc, sr));
        xz = new Vector2I(sc + 22, sr); yz = new Vector2I(sc + 8, sr + Mathf.Min(2, rng - 1));
        Stellen(_gfpX, xz); Stellen(_gfpY, yz); Heften();
        A.Cooldown = 0; Null();
        Befehl(ai, _gfpX);
        _gfpA = ai;
        int gfA0 = GfAufnahmen;
        for (int t = 0; t < 400; t++) { Heften(); SimTickFuerProbe(); }
        bool zielX = A.Target == _gfpX;
        bool g1 = _gfpAY > 0 && _gfpStandY == 0 && zielX;
        ok &= g1;
        sb.AppendLine($"  G1 Fahrt auf X, Y neben dem Weg: Schuesse A->Y {_gfpAY} (davon im Stand {_gfpStandY}), A->X {_gfpAX}, "
                    + $"Auftragsziel noch X {zielX}, A jetzt ({A.Col},{A.Row}), Aufnahmen {GfAufnahmen - gfA0} — "
                    + $"Soll >0, 0 im Stand, Ziel X (Nullmodell: 0) -> {(g1 ? "ja" : "NEIN")}");

        // ---- G2 -----------------------------------------------------------------
        Stellen(ai, new Vector2I(sc, sr));
        xz = new Vector2I(sc + Mathf.Max(2, rng - 1), sr); yz = new Vector2I(sc + 1, sr + 1);
        Stellen(_gfpX, xz); Stellen(_gfpY, yz); Heften();
        A.Cooldown = 0; Null();
        Befehl(ai, _gfpX);
        for (int t = 0; t < 600; t++) { Heften(); SimTickFuerProbe(); }
        int summe = _gfpAX + _gfpAY;
        float anteil = summe > 0 ? 100f * _gfpAX / summe : 0f;
        bool g2 = summe > 0 && anteil >= 90f;
        ok &= g2;
        sb.AppendLine($"  G2 X und Y in Reichweite (Y naeher): A->X {_gfpAX}, A->Y {_gfpAY}, Anteil X {anteil:0.0} % — Soll >= 90 % "
                    + $"(Nullmodell 100 %) -> {(g2 ? "ja" : "NEIN")}");

        // ---- G3 -----------------------------------------------------------------
        Stellen(ai, new Vector2I(sc, sr));
        xz = new Vector2I(sc + 22, sr); yz = new Vector2I(sc - (rng + 3), sr);
        Stellen(_gfpX, xz); Stellen(_gfpY, yz);
        int yRange0 = Y.Range;
        Y.Range = rng + 8; yFrei = true; Y.Cooldown = 0;
        Heften(); A.Cooldown = 0; Null();
        int svA = 0;
        Befehl(ai, _gfpX);
        int col0 = A.Col;
        for (int t = 0; t < 250; t++)
        {
            Heften(); SimTickFuerProbe();
            foreach (var sv in _svTafel) if (sv.Opfer == ai) { svA++; break; }
        }
        yFrei = false; Y.Range = yRange0;
        bool urteil3 = _gfpYA > 0;
        bool g3 = !urteil3 || (_gfpAY == 0 && svA == 0 && A.Target == _gfpX && A.Col > col0);
        ok &= g3;
        sb.AppendLine($"  G3 Y schiesst von ausserhalb (Y {rng + 3} Zellen hinter A, Y-Reichweite {rng + 8}): Y->A {_gfpYA}, A->Y {_gfpAY}, "
                    + $"Selbstverteidiger-Eintrag fuer A in {svA} Takten, A von Spalte {col0} nach {A.Col}, Ziel X {A.Target == _gfpX} — "
                    + (urteil3 ? $"Soll 0 / 0 / weiter -> {(g3 ? "ja" : "NEIN")}" : "KEIN URTEIL (Y schoss nicht)"));

        // ---- G4 Fussvolk ----------------------------------------------------------
        if (fi < 0) sb.AppendLine("  G4 entfaellt: kein eigener Fusssoldat");
        else
        {
            var F = _entities[fi];
            _gfpA = fi;
            Stellen(ai, new Vector2I(sc - 12, sr + 2));
            Stellen(fi, new Vector2I(sc, sr));
            xz = new Vector2I(sc + 22, sr); yz = new Vector2I(sc + 3, sr + 1);
            Stellen(_gfpX, xz); Stellen(_gfpY, yz); Heften();
            F.Cooldown = 0; Null();
            Befehl(fi, _gfpX);
            int gehend = 0, gehTakte = 0;
            for (int t = 0; t < 120; t++)
            {
                Heften();
                int vor = _gfpAlle;
                SimTickFuerProbe();
                if (F.Path != null) { gehTakte++; gehend += _gfpAlle - vor; }
            }
            F.Target = -1; F.Ordered = false; F.Path = null; F.RestZiel = null;
            F.RetryIn = 0; F.ChaseRetry = 0; F.Goal = new Vector2I(F.Col, F.Row); F.Orders.Clear(); F.Cooldown = 0;
            // 31.10.2026: der Halt von Hand mitten in einer Drehung liess OTACIM stehen — es
            // zaehlt nur im Schritt herunter, und die Fussvolk-Uhr feuert nur bei 0 (G4 kippte
            // je nach Verlauf, 027e117). Wie KiGaEingreifen: Bremse mit loesen.
            F.DrehWarten = 0;
            // jetzt steht er; Y neben ihn
            yz = new Vector2I(F.Col + 1, F.Row + 1 <= sr + 2 ? F.Row + 1 : F.Row - 1);
            int nachher0 = _gfpAlle;
            bool sg0 = Schussgruende; Schussgruende = true;
            for (int t = 0; t < 200; t++) { Heften(); SimTickFuerProbe(); }
            Schussgruende = sg0;
            int steh = _gfpAlle - nachher0;
            if (steh == 0) sb.AppendLine($"    (Fusssoldat ({F.Col},{F.Row}), Y ({yz.X},{yz.Y}), Weg {(F.Path != null)}, Ziel {F.Target}, feindlich {IsHostile(F, Y)}, Abstand {CellDistance(F, Y):0.0}, Reichweite {RangeOf(F):0.0}/{RangeMinOf(F):0.0}, Ukol {F.Ukol}, Dreh {F.DrehWarten}, Grund: {F.Schussgrund})");
            // bug-688: mit der Fussvolk-Uhr schiesst er im Gehen beim Betreten einer Zelle
            // (Ankunftsarm 0x407D27) — das Soll im Gehen haengt am Schalter --fussvolk-uhr-alt.
            bool g4 = gehTakte > 0 && (FussvolkUhrAlt ? gehend == 0 : gehend > 0) && steh > 0;
            ok &= g4;
            sb.AppendLine($"  G4 Fusssoldat Platz {F.Slot} mit Auftrag im Gehen ({gehTakte} Takte): Schuesse (auf irgendwen) {gehend}; "
                        + $"nach Halt im Stand: {steh} — Soll {(FussvolkUhrAlt ? "0" : ">0 (Fussvolk-Uhr, Ankunftsarm)")} / >0 -> {(g4 ? "ja" : "NEIN")}");
            _gfpA = ai;
        }

        // ---- G5 Restauftrag ---------------------------------------------------------
        Stellen(ai, new Vector2I(sc, sr));
        xz = new Vector2I(sc + Mathf.Max(2, rng - 1), sr); yz = new Vector2I(sc - 13, sr - 2);
        Stellen(_gfpX, xz); Stellen(_gfpY, yz); Heften();
        X.HpMax = X.Hp = 1;
        A.Cooldown = 0; Null();
        Befehl(ai, _gfpX);
        int rest0 = GfRestauftraege, tot = -1;
        for (int t = 0; t < 400; t++)
        {
            if (!X.Dead) Heften();
            else if (tot < 0) tot = t;
            SimTickFuerProbe();
        }
        float abst = new Vector2(A.Col - xz.X, A.Row - xz.Y).Length();
        bool urteil5 = X.Dead;
        bool g5 = GfRestauftraege > rest0 && abst <= 1.5f && A.RestZiel != null && !Untaetig(A);
        ok &= !urteil5 || g5;
        sb.AppendLine($"  G5 Auftragsziel stirbt (Takt {tot}): Restauftraege {GfRestauftraege - rest0}, A jetzt {abst:0.0} Zellen von der Todeszelle "
                    + $"({xz.X},{xz.Y}), Restziel {(A.RestZiel is { } rz ? $"({rz.X},{rz.Y})" : "keins")}, untaetig {Untaetig(A)} — "
                    + (!urteil5 ? "KEIN URTEIL (X starb nicht)"
                       : $"Soll Restauftrag, <= 1,5 Zellen, nicht untaetig (Nullmodell: keiner, bleibt stehen) -> {(g5 ? "ja" : "NEIN")}"));
        _gfpA = -1;
        sb.AppendLine("  " + GelegenheitsfeuerLine());
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
