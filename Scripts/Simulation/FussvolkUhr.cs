namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐ 30.10.2026, bug-688 — <b>DIE FUSSVOLK-UHR <c>0x40F0A0</c></b> (Spielerentscheidung
/// 30.10.: »fussvolk-uhr wie im original, NET07 darf sich aendern«). Lesebericht
/// <c>berichte/pruefung-rest-b-opus.md</c> §1, Bau <c>berichte/bau-fussvolk-uhr.md</c>.
///
/// <para><b>Original (selbst gelesen, C 0x40F0A0…0x40F603; F 0x40EED0, 391/391 Befehle,
/// dieselben 33 Rufe, dieselben Festwerte und dieselben zehn Satzfelder):</b></para>
/// <code>
///   0x40F0CF  NABYTO +0x32 != 0 -> raus                 ; nur nachgeladen, KEIN 30er-Tor
///   0x40F12E  n = word[0x4F5B28 + 2·Reichweite +0x2B]   ; Zaehler der Ringtafel
///   0x40F13B  UKOL +0x14 == 4 -> Auftragsziel = UTOK_NA +0x36, sonst 0xFFFF
///   0x40F168  STRILI_NA +0x34 == 0xFFFF -> Ringsuche k = 0..n (jle) ueber 0x4F5B38:
///             imap &lt; 8000         Einheit: feindlich (0x87B155 == 0) und Probe 0x40193d
///             imap 10000..13999   Infanteriezelle: 0x433FE0 (alle verbuendet -> weiter),
///                                 0x434170 = ERSTER feindlicher Mann der 9 Plaetze, Probe
///             imap 60000..60299   Gebaeude NUR wenn == Auftragsziel (0x40F312)
///             Bruecke/Rampe       nur wenn Auftrag == 40000 + Lage (0x40F3AE)
///             Treffer: STRILI_NA := Ziel, +0x02 := Richtung (0x40F760),
///                      Ziel == Auftragsziel -> POHYB +0x1A := 0xFF (0x40F244),
///                      Schuss 0x40EC70 (setzt NABYTO), ret — der ERSTE Treffer gilt
///   0x40F43B  STRILI_NA gesetzt: tot (+0x0A == 0xFF) / UKOL &gt;= 100 / Probe faellt
///             -> STRILI_NA := 0xFFFF und raus (kein Schuss, keine Suche in diesem Takt);
///             sonst Schuss. KEINE Rueckschaltung aufs Auftragsziel.
/// </code>
/// <para><b>Die zwei Rufer</b> (Thunk 0x4021BC; Tore in F byteidentisch):</para>
/// <list type="bullet">
/// <item><b>Stand</b> @0x409FEE (C) / @0x409F17 (F), am Ende jedes Einheitentakts:
///   UKOL ∈ {0, 2, 3, 4} (0x409FBA…0x409FC9), Klasse +0x0A == 1, kein Schritt
///   (+0x04 == 0xFF), dreht nicht (OTACIM +0x16 == 0).</item>
/// <item><b>Ankunft</b> @0x407D27 (C) / @0x407C51 (F), im Ankunftsarm UKOL 4 (Sprungtafel
///   0x40A0A0, Index 4), also nur mit Angriffsauftrag und nur beim Betreten einer Zelle;
///   danach <c>STRILI_NA == UTOK_NA</c> -> POHYB := 0xFF, +0x06 := 0, +0x04 := 0xFF:
///   <b>der Soldat haelt an</b> (0x407D3B…0x407D52).</item>
/// </list>
/// <para><b>Was die Uhr NICHT anfasst</b> (Feldliste des Rumpfs, C und F gleich: +0x00,
/// +0x01, +0x02, +0x14, +0x1A, +0x2B, +0x32, +0x34, +0x36, +0x3B, +0x3C; am Ziel +0x0A,
/// +0x14): weder den Unterzellen-Versatz +0x1D/+0x1E (Klickkasten, bug-596) noch
/// Ueberfahren (prejet 0x412980 ist ein Fahrzeugarm). Sie rechnet nur in Zellen.
/// Das Eingraben (UKOL 22/23) faellt schon am Standtor heraus.</para>
///
/// <para>⚠ <b>Berichtigung des Leseberichts:</b> 10000…13999 ist die
/// INFANTERIEZELLE (MapForest.cs, 0x433FE0 = pratelska_infa), kein Gebaeude. Ein Gebaeude
/// nimmt die Uhr nur als Auftragsziel; das ist bei uns schon der Gebaeudeangriff
/// (bug-597, Simulation/Gebaeudeangriff.cs, »Fussvolk ERSTE der Tafel«).</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN:</b> (1) »kein Schritt« ist <c>Path == null</c> — ein
/// wartender Soldat mit Weg (+0x04 == 0xFF im Original) schiesst bei uns nicht;
/// (2) unsere UKOL kennt nur 0/3/Tuerwerte, das Tor fragt also 0, 2, 3, 4 ab und laesst
/// unser <c>DugIn</c> (Sofortumschalter, UKOL bleibt 0) durch; (3) die Infanteriezelle hat
/// bei uns keine 9 Plaetze — der erste Mann ist die kleinste Listenstelle in der Zelle;
/// (4) die Probe 0x40193d ist das Schussfenster (wie P4), dazu ungelesen: ins Gebaeude
/// gefahren loest das Ziel; (5) die KI-Sicht (KiAufklaerung) bleibt ein Tor wie in
/// AutoAcquire/P4; (6) ein Gebaeude als Auftragsziel behaelt den Weg von bug-597;
/// (7) die Richtung wird bei JEDEM Schuss gesetzt (das Original schreibt +0x02 nur bei
/// der Suche; unser Gehen dreht <c>Facing</c> mit); (8) im Stand mit Auftrag und einem
/// anderen Schussziel bleibt der Soldat stehen, solange das Auftragsziel im Schussfenster
/// ist, sonst verfolgt er wie bisher (0x409060 -> 0x40FC90 ruft den Auftrag neu; dessen
/// Fahrtor ist zwischen zwei Lesungen strittig, siehe Selbstverteidiger.cs @0x40FD67
/// gegen Gelegenheitsfeuer.cs @0x40FD58).</para>
///
/// <para>Gegenschalter <c>--fussvolk-uhr-alt</c> (Stand bis 30.10.: AutoAcquire alle
/// 0,4 s, nur ohne Ziel und nur im Stand; mit Auftrag nur aufs Auftragsziel).
/// Pruefstand <c>--fussvolk-uhr-check</c>, Nullmodelle <c>--fussvolk-uhr-alt</c>,
/// <c>--fussvolk-halt-aus</c>, <c>--fussvolk-uhr-gebaeude</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--fussvolk-uhr-alt</c> — der Stand bis 30.10.2026.</summary>
    public static bool FussvolkUhrAlt;

    /// <summary><c>--fussvolk-halt-aus</c> — NULLMODELL nur fuer den Pruefstand: der
    /// Ankunftsarm haelt beim Auftragsziel nicht an (0x407D48 fehlt).</summary>
    public static bool FussvolkHaltAus;

    /// <summary><c>--fussvolk-uhr-gebaeude</c> — NULLMODELL nur fuer den Pruefstand: die
    /// Ringsuche nimmt jedes feindliche Gebaeude (die VERMUTUNG des Leseberichts).</summary>
    public static bool FussvolkUhrGebaeude;

    /// <summary>Zaehler fuer Pruefstand und Zeile.</summary>
    public int FuLaeufe, FuSuchen, FuAufnahmen, FuSchuesse, FuGeraeumt, FuAnkuenfte, FuHalte;

    /// <summary>Gilt die Fussvolk-Uhr fuer diese Einheit?</summary>
    private static bool FussUhrAn(Entity e) => !FussvolkUhrAlt && e.Infantry >= 0;

    /// <summary>Die erste Stelle jedes Versatzes in <see cref="FussRingTafel"/> (die Tafel
    /// traegt (−1,−6)/(1,−6) zweimal; es zaehlt die erste). Feld [dx+6, dy+6].</summary>
    private static int[,]? _fussRingStelle;

    private static int FussRingStelle(int dx, int dy)
    {
        if (dx < -6 || dx > 6 || dy < -6 || dy > 6) return int.MaxValue;
        if (_fussRingStelle == null)
        {
            var s = new int[13, 13];
            for (int x = 0; x < 13; x++) for (int y = 0; y < 13; y++) s[x, y] = int.MaxValue;
            for (int k = 0; k < FussRingTafel.Length; k++)
            {
                var (a, b) = FussRingTafel[k];
                if (s[a + 6, b + 6] == int.MaxValue) s[a + 6, b + 6] = k;
            }
            _fussRingStelle = s;
        }
        return _fussRingStelle[dx + 6, dy + 6];
    }

    /// <summary>Die kleinste Tafelstelle, unter der <paramref name="t"/> von
    /// <paramref name="e"/> aus in der imap steht (ein Schiff mit seinem ganzen Rumpf).</summary>
    private static int FussRingStelleVon(Entity e, Entity t)
    {
        int w = 1, h = 1;
        if (t.IsBuilding) { w = Mathf.Max(1, t.FootW); h = Mathf.Max(1, t.FootH); }
        else if (t.Move == Simulation.NavGrid.MoveClass.Ship) w = h = Simulation.NavGrid.HullSide(t.GameUnitType);
        int best = int.MaxValue;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                best = System.Math.Min(best, FussRingStelle(t.Col + x - e.Col, t.Row + y - e.Row));
        return best;
    }

    /// <summary>Nur fuer das Nullmodell <c>--fussvolk-uhr-gebaeude</c>: Abstand zur naechsten
    /// Fussabdruckzelle.</summary>
    private static float FussGebaeudeAbstand(Entity e, Entity b)
    {
        float best = float.MaxValue;
        int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                best = Mathf.Min(best, new Vector2(b.Col + x - e.Col, b.Row + y - e.Row).Length());
        return best;
    }

    /// <summary>Die Ringsuche 0x40F173…0x40F3DD: der ERSTE Treffer in Tafelreihenfolge
    /// (k = 0…n einschliesslich), nicht der naechste. −1 = keiner.</summary>
    private int FussUhrSuche(int i, Entity e)
    {
        int R = Mathf.Clamp(e.Range, 0, 126);
        int n = R < FussRingZaehler.Length ? FussRingZaehler[R] : 0;     // UNSERE SETZUNG R > 15 -> 0
        if (n < 0) return -1;                                             // 0x40F180 jl
        bool kiSicht = KiAufklaerungAn && IstKi(e.Owner);
        int bestK = int.MaxValue, best = -1;
        for (int j = 0; j < _entities.Count; j++)
        {
            if (j == i) continue;
            var t = _entities[j];
            if (t.Dead || t.IsProp) continue;
            // 60000…60299 nur als Auftragsziel (0x40F312) — das ist der Gebaeudeangriff
            if (t.IsBuilding && !FussvolkUhrGebaeude) continue;
            if (!IsHostile(e, t)) continue;                               // 0x87B155 / 0x433FE0
            int k = FussRingStelleVon(e, t);
            if (k > n || k >= bestK) continue;                            // gleiche Zelle: erster Mann
            if (PlasmaSelbstzielVerboten(e, t)) continue;
            float d = t.IsBuilding ? FussGebaeudeAbstand(e, t) : CellDistance(e, t);
            if (!InFiringWindow(e, d)) continue;                          // Probe 0x40193d
            if (kiSicht && !KiKenntRoh(e.Owner, t)) { KiAutoVerworfen++; continue; }
            bestK = k; best = j;
        }
        return best;
    }

    /// <summary>
    /// Die Uhr selbst. <paramref name="auftrag"/> = das Auftragsziel (UKOL 4) oder −1.
    /// Schreibt <see cref="Entity.Schussziel"/> (STRILI_NA) und schiesst.
    /// </summary>
    private void FussUhrTakt(int i, Entity e, int auftrag)
    {
        if (!CanFight(e)) return;
        if (e.Cooldown > 0)                                               // 0x40F0CF NABYTO
        {
            if (Schussgruende) e.Schussgrund = $"Fussvolk-Uhr: nachladen ({e.Cooldown:0.00}s)";
            return;
        }
        FuLaeufe++;
        int s = e.Schussziel;
        if (s >= 0)
        {
            // 0x40F43B: halten oder raeumen
            bool gut = s < _entities.Count;
            if (gut)
            {
                var t = _entities[s];
                gut = !t.Dead && !t.IsProp && !t.IsBuilding && !Untergestellt(t)
                      && InFiringWindow(e, CellDistance(e, t))
                      && !(KiAufklaerungAn && IstKi(e.Owner) && !KiKenntRoh(e.Owner, t));
            }
            if (!gut)
            {
                e.Schussziel = -1; FuGeraeumt++;                          // 0x40F5E4
                if (Schussgruende) e.Schussgrund = "Fussvolk-Uhr: Schussziel geraeumt (Probe 0x40193d)";
                return;
            }
            FussUhrSchuss(i, e, s);
            return;
        }
        FuSuchen++;
        s = FussUhrSuche(i, e);
        if (s < 0)
        {
            if (Schussgruende) e.Schussgrund = $"Fussvolk-Uhr: nichts im Ring (Reichweite {e.Range})";
            return;
        }
        e.Schussziel = s;                                                 // 0x40F236
        if (s != auftrag) FuAufnahmen++;
        FussUhrSchuss(i, e, s);
    }

    /// <summary>Der Schuss 0x40EC70 (Nachladen, Munition, Klang ueber Fire).</summary>
    private void FussUhrSchuss(int i, Entity e, int s)
    {
        var t = _entities[s];
        e.AimFacing = DirToFacing(t.Pos - e.Pos);                         // +0x02 (0x40F760)
        if (!FussrichtungAlt) e.Facing = e.AimFacing;                     // UNSERE SETZUNG (7)
        if (!HasAmmo(e) || GaswerferSchweigt(e)) return;
        if (Schussgruende)
            e.Schussgrund = $"SCHUSS (Fussvolk-Uhr) auf Platz {t.Slot} in {CellDistance(e, t):0.0} Zellen";
        e.Cooldown = NachladenFuer(e, t.Col, t.Row);
        if (e.AmmoMax > 0 && !(CheatAmmo && Cheated(e))) e.Ammo--;
        DebugShots++;
        FuSchuesse++;
        Fire(i, e, s, t, WeaponOf(e.Weapon));
    }

    /// <summary>Der Standrufer @0x409FEE: UKOL 0/2/3/4, kein Schritt, dreht nicht.</summary>
    private void FussUhrStand(int i, Entity e, int auftrag)
    {
        if (e.Path != null || e.DrehWarten != 0) return;                 // UNSERE SETZUNG (1)
        if (e.Ukol is not (0 or 2 or 3 or 4)) return;                     // 0x409FBA…0x409FC9
        FussUhrTakt(i, e, auftrag);
    }

    /// <summary>Ein Fusssoldat mit EINHEIT als Ziel (aus UpdateCombat, nach den Toren).
    /// Ohne Auftrag ist das Ziel ein Fremdeintrag und wird zum Schussziel.</summary>
    private void FussUhrMitZiel(int i, Entity e, Entity t)
    {
        if (!e.Ordered)
        {
            // Das Original kennt kein »Ziel ohne Auftrag«: das ist STRILI_NA.
            if (e.Schussziel < 0) e.Schussziel = e.Target;
            e.Target = -1;
            FussUhrStand(i, e, -1);
            return;
        }
        if (e.Path != null)
        {
            if (Schussgruende) e.Schussgrund = "Fussvolk-Uhr: geht — gesucht wird nur beim Betreten einer Zelle (0x407D27)";
            return;
        }
        FussUhrStand(i, e, e.Target);
        if (e.Schussziel == e.Target) return;                             // 0x40FD10: bleibt
        float dist = CellDistance(e, t);
        if (InFiringWindow(e, dist)) return;                              // UNSERE SETZUNG (8)
        if (dist < RangeMinOf(e)) { e.Target = -1; MinRangeBlocked++; return; }
        if (!e.Mobile) { e.Target = -1; return; }
        // 0x409060 -> 0x40FC90: der Auftrag wird neu gerufen, der Soldat geht zum Ziel
        if (e.ChaseRetry > 0) { e.ChaseRetry--; return; }
        if (_nav == null) return;
        var goal = _nav.NearestFree(new Vector2I(t.Col, t.Row), e.Move, i);
        if (goal == null) { ChaseGiveUp(i, e, "kein Zielfeld"); return; }
        var path = WegAbVormerkung(i, e, goal.Value);
        if (path == null || path.Count == 0) { ChaseGiveUp(i, e, "kein Weg"); return; }
        e.Path = path;
        e.PathIdx = 0;
        e.Goal = goal.Value;
    }

    /// <summary>Der Ankunftsrufer @0x407D27 (UKOL 4) aus der Bewegung, nach dem Betreten
    /// der Zelle. true = halt (0x407D3B…0x407D52: STRILI_NA == UTOK_NA).</summary>
    private bool FussUhrAnkunft(int i, Entity e)
    {
        if (!FussUhrAn(e) || !e.Ordered || e.Target < 0 || e.Target >= _entities.Count) return false;
        var t = _entities[e.Target];
        if (t.IsBuilding || t.Dead) return false;                         // UNSERE SETZUNG (6)
        FuAnkuenfte++;
        FussUhrTakt(i, e, e.Target);
        if (e.Schussziel != e.Target || FussvolkHaltAus) return false;
        FuHalte++;
        return true;
    }

    /// <summary>Die Zaehler als Zeile.</summary>
    public string FussvolkUhrLine()
        => $"fussvolk-uhr{(FussvolkUhrAlt ? " (NULLMODELL --fussvolk-uhr-alt)" : "")}"
         + $"{(FussvolkHaltAus ? " [--fussvolk-halt-aus]" : "")}{(FussvolkUhrGebaeude ? " [--fussvolk-uhr-gebaeude]" : "")}: "
         + $"Laeufe {FuLaeufe}, Suchen {FuSuchen}, Aufnahmen neben dem Auftrag {FuAufnahmen}, Schuesse {FuSchuesse}, "
         + $"geraeumt {FuGeraeumt}, Ankuenfte {FuAnkuenfte}, Halte {FuHalte}";
}
