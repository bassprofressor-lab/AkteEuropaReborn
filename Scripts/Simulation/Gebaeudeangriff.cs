using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>bug-597 (KayelGee R7 #7, 22.10.2026) — DER GEBAEUDEANGRIFF VERTEILT SICH.</b>
///
/// <para>Seine Meldung: »Wenn ein Befehl gegeben wird, ein Gebaeude anzugreifen, dann
/// greifen die Einheiten im Original diverse Stellen am Gebaeude an. In Reborn scheinen
/// sie nur eine Stelle anzugreifen. Dadurch greifen die Einheiten deutlich weniger an.«
/// Massgeblich: <c>berichte/gebaeudeangriff-zielstellen-fable.md</c> (Fable, C und F
/// gelesen), Pruefstand nach <c>berichte/gebaeudeangriff-zielstellen-opus.md</c>.</para>
///
/// <para>Bei uns hing der Gebaeudeangriff an DREI Stellen am Anker: Abstand
/// (<c>CellDistance</c> Anker→Anker), Anfahrt (<c>NearestFree(Anker)</c> — alle
/// fuhren auf denselben Punkt) und Einschlag (<c>ShotAim = AuswahlMitte</c> — ein Punkt
/// fuer alle). Das Original:</para>
/// <list type="number">
///   <item><b>Schusszelle je Einheit</b> <c>+0x3B/+0x3C</c>. Die Schiessuhr
///   <c>0x40DDB0</c> (F <c>0x40DBE0</c>) laeuft die Ringspirale <c>0x79A008</c> ab
///   (Anzahl <c>word[0x834A80 + 2R]</c>, R = <c>+0x2B</c>, Ringe 0…R−1), nimmt jede Zelle
///   mit imap <c>== UTOK_NA</c> (60000+Platz, @0x40E103…0x40E111) nach bestandenem
///   Probeschuss <c>0x4543C0</c> (@0x40E125) und speichert sie (@0x40E1FE/0x40E204).
///   ⭐ Der Gebaeudezweig faellt danach in die Schleifenfortschaltung (@0x40E211 →
///   0x40E218, kein <c>ret</c>): ein FAHRZEUG behaelt die LETZTE passende Zelle.
///   Die Suche laeuft nur bei <c>(Takt + Index) % 30 == 0</c> (@0x40DEC2…0x40DECE) und
///   NABYTO == 0. FUSSVOLK (<c>0x40F0A0</c>, F <c>0x40EED0</c>): eigene Tafel
///   <c>0x4F5B38</c>/<c>0x4F5B28</c> einschliesslich, ERSTE Zelle (@0x40F390 <c>ret</c>),
///   jeder Aufruf (nur NABYTO-Tor @0x40F0CF, kein 30er-Tor — selbst nachgelesen).</item>
///   <item><b>Reichweite zur Zelle</b>: Probeschuss-Fenster <c>+0x2A·40 ≤ d ≤ +0x2B·40</c>
///   (<c>0x454280</c> = MINDEST-, <c>0x454200</c> = Hoechstreichweite), Abstand zur
///   Fussabdruckzelle, nie zum Anker. Festgehalten bis Probeschuss/Gebaeudetod sie
///   raeumen (@0x40E5C5…0x40E65C, <c>STRILI_NA := 0xFFFF</c> @0x40E653).</item>
///   <item><b>Anfahrt</b> <c>0x40FC90</c> (F <c>0x40FAC0</c>): Zufallszelle
///   <c>Anker + (rnd%(2sx+8) − 4, rnd%(2sy+6) − 4)</c> (@0x40FE79…0x40FED3), (sx, sy)
///   aus der Sichtmittelpunkt-Tafel <c>0x539D90</c>, Typ &gt; 16 → 0 (@0x40FE63).
///   Der Wuerfel ist <c>0x4C5B30</c>, deterministisch — bei uns
///   <c>Determinism.Roll</c>, x vor y wie im Rumpf.</item>
///   <item><b>Zwei Arme</b>: kommt die Einheit FAHREND am Wegende an und hat das
///   Gebaeude nicht aufgenommen, setzt der Schrittverteiler-Arm <c>0x407D1D</c>
///   <c>UKOL := 0</c> (@0x407E12…0x407E1E, F 0x407D3C…0x407D48) — der Auftrag ist zu
///   Ende. Der Stand-Arm <c>0x409060</c> wuerfelt nur neu.</item>
/// </list>
/// <para>Gegenschalter <c>--gebaeudeziel-alt</c> (alles wie bis 0.6.10: Anker, eine
/// Stelle) und <c>--gebaeudeangriff-ankunft-alt</c> (Ankunft ohne Aufnahme beendet den
/// Auftrag NICHT, die Einheit wuerfelt neu). Pruefstand <c>--gebaeudeangriff-check</c>
/// (GebaeudeangriffCheck.cs).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--gebaeudeziel-alt</c>: der Stand bis 0.6.10 — Abstand Anker→Anker,
    /// Anfahrt <c>NearestFree(Anker)</c>, Einschlag <c>AuswahlMitte</c>, keine Schusszelle.</summary>
    public static bool GebaeudezielAlt;

    /// <summary><c>--gebaeudeangriff-ankunft-alt</c>: eine Einheit, die fahrend am
    /// Wegende ankommt, ohne das Gebaeude aufgenommen zu haben, behaelt den Auftrag
    /// und wuerfelt eine neue Fahrzelle (statt UKOL 0 @0x407E1A).</summary>
    public static bool GebaeudeangriffAnkunftAlt;

    /// <summary>Zaehler fuer Pruefstand und Wache.</summary>
    public int GebaeudeZellenGewaehlt, GebaeudeZellenGeraeumt, GebaeudeFahrtenGewuerfelt,
               GebaeudeAnkunftBeendet, GebaeudeAnkunftNeu, GebaeudeSchuesse, GebaeudeSchuesseAufZelle;

    /// <summary>Wachzeile: wie oft gewuerfelt, aufgenommen, geraeumt, beendet wurde.</summary>
    public string GebaeudeangriffLine()
        => $"gebaeudeangriff: Fahrzellen gewuerfelt {GebaeudeFahrtenGewuerfelt}, Schusszellen gewaehlt {GebaeudeZellenGewaehlt}, "
         + $"geraeumt {GebaeudeZellenGeraeumt}, Ankunft beendet {GebaeudeAnkunftBeendet}, neu gewuerfelt {GebaeudeAnkunftNeu}, "
         + $"Schuesse auf Gebaeude {GebaeudeSchuesse}, davon auf Schusszelle {GebaeudeSchuesseAufZelle}"
         + (GebaeudezielAlt ? " [--gebaeudeziel-alt]" : "") + (GebaeudeangriffAnkunftAlt ? " [--gebaeudeangriff-ankunft-alt]" : "");

    /// <summary>Die Sichtmittelpunkt-Tafel <c>0x539D90</c> (C und F byteidentisch,
    /// am 22.10.2026 aus C nachgelesen: <c>01 01 03 03 03 02 03 02 03 02 02 02 02 02 02 01
    /// 01 02 04 02 04 03 02 02 01 02 02 02 02 02 02 03 02 02 00 00</c>), je Typ (Spalte, Zeile).
    /// ⚠ Fest eingetragen statt aus dem Katalog (<c>sight_col/sight_row</c>), weil der
    /// Katalog (0,0) auslaesst und der Wurf im Netz auf beiden Seiten gleich sein muss.</summary>
    private static readonly (int Sx, int Sy)[] SichtMitteTafel =
    {
        (1, 1), (3, 3), (3, 2), (3, 2), (3, 2), (2, 2), (2, 2), (2, 1), (1, 2),
        (4, 2), (4, 3), (2, 2), (1, 2), (2, 2), (2, 2), (2, 3), (2, 2), (0, 0),
    };

    /// <summary>Die Fussvolk-Versatztafel <c>0x4F5B38</c> (je Eintrag zwei signierte
    /// Worte Spalte/Zeile) mit ihrem Zaehler <c>word[0x4F5B28 + 2R]</c>. Am 22.10.2026 aus
    /// C (Datei 0xF3928) und F (0xF3528) gelesen, beide byteidentisch. ⚠ Die Doppelung
    /// (−1,−6)/(1,−6) an Stelle 108/109 steht SO im Original (statt (−1,6)/(1,6)).</summary>
    private static readonly (int Dx, int Dy)[] FussRingTafel =
    {
        (0, 0), (-1, 0), (0, -1), (1, 0), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1), (0, -2),
        (-2, 0), (2, 0), (0, 2), (-1, -2), (1, -2), (-2, -1), (-2, 1), (-1, 2), (1, 2), (2, -1),
        (2, 1), (-3, 0), (0, -3), (3, 0), (0, 3), (-1, -3), (1, -3), (-3, -1), (-3, 1), (-1, 3),
        (1, 3), (3, -1), (3, 1), (-2, -2), (-2, 2), (2, -2), (2, 2), (-4, 0), (0, -4), (4, 0),
        (0, 4), (-2, -3), (-3, -2), (2, -3), (3, -2), (-3, 2), (-2, 3), (2, 3), (3, 2), (-1, -4),
        (1, -4), (-4, -1), (-4, 1), (4, -1), (4, 1), (-1, 4), (1, 4), (-5, 0), (0, -5), (5, 0),
        (0, 5), (-3, -3), (-3, 3), (3, -3), (3, 3), (-2, -4), (-4, -2), (-4, 2), (-2, 4), (2, -4),
        (4, -2), (4, 2), (2, 4), (-1, -5), (1, -5), (-5, -1), (-5, 1), (-1, 5), (1, 5), (5, -1),
        (5, 1), (-3, -4), (-4, -3), (3, -4), (4, -3), (-4, 3), (-3, 4), (4, 3), (3, 4), (-6, 0),
        (0, -6), (6, 0), (0, 6), (-2, -5), (2, -5), (-5, -2), (-5, 2), (5, -2), (5, 2), (-2, 5),
        (2, 5), (-1, -6), (1, -6), (-6, -1), (-6, 1), (6, -1), (6, 1), (-1, -6), (1, -6), (-3, -5),
        (-5, -3), (3, -5), (5, -3), (-5, 3), (-3, 5), (5, 3), (3, 5), (-4, -4), (-4, 4), (4, -4),
        (4, 4), (-2, -6), (2, -6), (-6, -2), (-6, 2), (6, -2), (6, 2), (-2, 6), (2, 6), (-4, -5),
        (-5, -4), (4, -5), (5, -4), (-5, 4), (-4, 5), (5, 4),
    };

    /// <summary><c>word[0x4F5B28 + 2R]</c> fuer R = 0…15, roh (signiert gelesen, die
    /// Schleife prueft <c>cmp word,0 / jl</c> @0x40F17A). Ab R = 7 stehen dort die Worte
    /// FREMDER Daten (0, 0, 0, −1, 0, 0, −1, 1, 0): ein Soldat mit Reichweite 7…9 prueft
    /// nur die eigene Zelle, mit 10 gar keine. ⚠ UNSERE SETZUNG: R &gt; 15 → 0.</summary>
    private static readonly int[] FussRingZaehler = { 0, 8, 20, 36, 56, 88, 136, 0, 0, 0, -1, 0, 0, -1, 1, 0 };

    /// <summary>Traegt die Zelle (c, r) das imap-Wort <c>60000 + Platz</c> des Gebaeudes
    /// <paramref name="t"/>? Ueber die Griffzellen der Karte; ein Neubau ueber seine
    /// Belegung (Construction stempelt die sperrenden Musterzellen). Die TUER traegt im
    /// Original 0xFFFE (@0x43CAD9) und zaehlt nicht. ⚠ UNSERE SETZUNG: ohne beides
    /// (Karte aus altem Import) das Rechteck FootW×FootH.</summary>
    private bool GebaeudeFussZelle(int ti, Entity t, int c, int r)
    {
        if (_nav == null || !_nav.InBounds(c, r)) return false;
        if (t.Slot >= 0 && _griffPlaetze.Contains(t.Slot))
            return _griffZelle.TryGetValue((c, r), out int platz) && platz == t.Slot;
        if (_nav.OccupantAt(c, r) == ti && _nav.IsImmobileAt(c, r)) return true;
        foreach (var d in t.DoorCells)
            if (c == t.Col + d.Col && r == t.Row + d.Row) return false;
        int w = Mathf.Max(1, t.FootW), h = Mathf.Max(1, t.FootH);
        return c >= t.Col && c < t.Col + w && r >= t.Row && r < t.Row + h;
    }

    /// <summary>Der Probeschuss <c>0x4543C0</c> auf eine Zelle, soweit gelesen: das
    /// Reichweitenfenster (<c>0x454200</c> &lt; d → 0 @0x4543EA, <c>0x454280</c> &gt; d → 0
    /// @0x454401). Unsere Einheiten stehen auf Zellmitten, d ist also der Euklid der Zellen.
    /// ⚠ UNSERE SETZUNG: der stille Probeflug <c>0x453AA0</c> (Einschlagzelle ==
    /// Zielzelle) ist nicht gebaut — Hang und Hindernis lehnen bei uns nicht ab.</summary>
    private static bool GebaeudeProbeschuss(Entity e, Vector2I z, out float d)
    {
        d = new Vector2(z.X - e.Col, z.Y - e.Row).Length();
        return InFiringWindow(e, d);
    }

    /// <summary>
    /// Die Schusszelle der Einheit fuer das Gebaeude <paramref name="t"/> halten oder
    /// suchen. true = es gibt eine, sie liegt im Fenster; <paramref name="dist"/> ist der
    /// Abstand zu ihr, <paramref name="punkt"/> ihre Bildmitte.
    /// </summary>
    private bool GebaeudeSchusszelle(int i, Entity e, Entity t, out float dist, out Vector2 punkt)
    {
        dist = float.MaxValue; punkt = t.Pos;
        // ---- das Zielhalten (@0x40E5C5…0x40E65C, Fussvolk @0x40F4AD…0x40F52E) ----
        if (e.SchussZelleFuer == e.Target && e.SchussZelle is { } z)
        {
            if (GebaeudeFussZelle(e.Target, t, z.X, z.Y) && GebaeudeProbeschuss(e, z, out dist))
            {
                punkt = ZellMitte(z.X, z.Y);
                return true;
            }
            // Probeschuss faellt durch: STRILI_NA := 0xFFFF (@0x40E653), Neusuche
            // vom jetzigen Standort.
            e.SchussZelle = null; e.SchussZelleFuer = -1;
            GebaeudeZellenGeraeumt++;
        }
        else if (e.SchussZelleFuer != e.Target) { e.SchussZelle = null; e.SchussZelleFuer = -1; }

        // ---- die Aufnahme: NABYTO == 0 (0x40DE8A / Fussvolk 0x40F0CF) ----
        if (e.Cooldown > 0)
        {
            if (Schussgruende) e.Schussgrund = $"Gebaeude Platz {t.Slot}: nachladen, keine Schusszelle";
            return false;
        }
        bool fuss = e.Infantry >= 0;
        // Fahrzeug: nur jeden 30. Takt, nach Index versetzt (@0x40DEC2…0x40DECE).
        if (!fuss && (_taktNr + i) % 30 != 0)
        {
            if (Schussgruende) e.Schussgrund = $"Gebaeude Platz {t.Slot}: sucht Schusszelle (alle 30 Takte)";
            return false;
        }
        int R = Mathf.Clamp(e.Range, 0, 126);
        Vector2I? gefunden = null;
        float gefundenD = 0f;
        if (fuss)
        {
            int n = R < FussRingZaehler.Length ? FussRingZaehler[R] : 0;
            for (int k = 0; k <= n && k < FussRingTafel.Length; k++)      // einschliesslich (jle)
            {
                var (dx, dy) = FussRingTafel[k];
                var c = new Vector2I(e.Col + dx, e.Row + dy);
                if (!GebaeudeFussZelle(e.Target, t, c.X, c.Y)) continue;
                if (!GebaeudeProbeschuss(e, c, out float d)) continue;
                gefunden = c; gefundenD = d;
                break;                                                   // @0x40F390 ret: die ERSTE
            }
        }
        else
        {
            int n = Simulation.NavGrid.RingTafelBis(R);                   // Ringe 0…R−1
            for (int k = 0; k < n; k++)
            {
                var (dx, dy) = Simulation.NavGrid.RingEintrag(k);
                var c = new Vector2I(e.Col + dx, e.Row + dy);
                if (!GebaeudeFussZelle(e.Target, t, c.X, c.Y)) continue;
                if (!GebaeudeProbeschuss(e, c, out float d)) continue;
                gefunden = c; gefundenD = d;                             // kein ret: die LETZTE
            }
        }
        if (gefunden is not { } g)
        {
            if (Schussgruende) e.Schussgrund = $"Gebaeude Platz {t.Slot}: keine Fussabdruckzelle im Schussfeld";
            return false;
        }
        e.SchussZelle = g; e.SchussZelleFuer = e.Target;
        GebaeudeZellenGewaehlt++;
        dist = gefundenD;
        punkt = ZellMitte(g.X, g.Y);
        return true;
    }

    /// <summary>
    /// Keine Schusszelle: anfahren wie <c>0x40FC90</c> — eine eigene, gewuerfelte Zelle
    /// im Kasten <c>(2sx+8) × (2sy+6)</c> ab <c>Anker − (4, 4)</c>.
    /// </summary>
    private void GebaeudeAnfahren(int i, Entity e, Entity t)
    {
        // Ein Gebaeude nimmt die Schiessuhr nur mit UKOL 4 auf; ein unbewegliches
        // Geschuetz faehrt nicht. Wie bisher: loslassen (siehe UpdateCombat »zu WEIT«).
        if (!e.Ordered || !e.Mobile)
        {
            TurmZielHalten(e);
            e.Target = -1;
            return;
        }
        if (e.Path != null) return;                       // faehrt schon
        if (e.ChaseRetry > 0) { e.ChaseRetry--; return; }
        if (_nav == null) return;

        int typ = t.BType is >= 0 and <= 16 ? t.BType : 0;          // @0x40FE63: > 16 -> 0
        var (sx, sy) = SichtMitteTafel[typ];
        int tx = t.Col - 4 + Simulation.Determinism.Roll(2 * sx + 8); // @0x40FE79..0x40FEA4
        int ty = t.Row - 4 + Simulation.Determinism.Roll(2 * sy + 6); // @0x40FEA8..0x40FED3
        GebaeudeFahrtenGewuerfelt++;
        // ⚠ UNSERE SETZUNG: in die Karte geklemmt (1 … Breite−2) — das Original
        // klemmt so nur im Spiegelzweig (@0x40FFF1…0x41002C).
        tx = Mathf.Clamp(tx, 1, _nav.Width - 2);
        ty = Mathf.Clamp(ty, 1, _nav.Height - 2);
        // ⚠ OFFEN (Fable §4.3 / §1.4): liegt die Zelle unter der Mindestreichweite,
        // faehrt das Original WEG (Spiegelvektor @0x40FF67…0x41002C). Nicht gebaut.
        // ⚠ UNSERE SETZUNG: eine verstellte Zelle wird ueber NearestFree ersetzt, wie
        // in der Verfolgung von Einheitszielen (0x4D32C0 nicht gelesen).
        var goal = _nav.NearestFree(new Vector2I(tx, ty), e.Move, i);
        if (goal == null) { ChaseGiveUp(i, e, "kein Zielfeld"); return; }
        var path = _nav.FindPath(new Vector2I(e.Col, e.Row), goal.Value, e.Move, i);
        if (path == null || path.Count == 0)
        {
            // schon dort (Weg leer) zaehlt wie Ankunft ohne Aufnahme im Stand: der
            // Stand-Arm 0x409114 wuerfelt nur neu — naechster Versuch nach RetryTicks.
            ChaseGiveUp(i, e, path == null ? "kein Weg" : "schon auf der Fahrzelle");
            return;
        }
        e.Path = path;
        e.PathIdx = 0;
        e.Goal = goal.Value;
        e.GebaeudeFahrzelle = goal.Value;
        e.GebaeudeFahrtFuer = e.Target;
        if (Schussgruende) e.Schussgrund = $"Gebaeude Platz {t.Slot}: faehrt Zufallszelle ({tx},{ty}) an";
    }

    /// <summary>
    /// Der Schrittverteiler-Arm <c>0x407D1D</c> am Wegende (Rufer: Bewegung, wenn der
    /// Weg NICHT abgeschnitten war): Wegbyte 0xFF und ein Nicht-Einheitsziel →
    /// <c>UKOL := 0</c> (@0x407DFD…0x407E1E). Hat die Einheit das Gebaeude schon
    /// aufgenommen (<c>STRILI_NA == UTOK_NA</c> @0x407D3B), steht sie und nichts endet.
    /// </summary>
    private void GebaeudeAnkunft(int i, Entity e)
    {
        int fuer = e.GebaeudeFahrtFuer;
        e.GebaeudeFahrtFuer = -1;
        if (fuer < 0 || GebaeudezielAlt || fuer != e.Target || !e.Ordered) return;
        if (fuer >= _entities.Count || !_entities[fuer].IsBuilding || _entities[fuer].Dead) return;
        if (e.SchussZelleFuer == e.Target && e.SchussZelle != null) return;   // aufgenommen
        if (GebaeudeangriffAnkunftAlt) { GebaeudeAnkunftNeu++; return; }       // wuerfelt neu
        GebaeudeAnkunftBeendet++;
        if (Schussgruende) e.Schussgrund = $"Gebaeude Platz {_entities[fuer].Slot}: angekommen ohne Aufnahme — Auftrag zu Ende (UKOL 0 @0x407E1A)";
        TurmZielHalten(e);
        e.Target = -1;
        e.Ordered = false;
        if (e.Ukol == 4) e.Ukol = 0;
    }

    /// <summary>Der Zielpunkt eines Schusses auf ein Gebaeude: die Zellmitte der
    /// Schusszelle (0x40BB00 @0x40BD57, Feinlage (20,10)) und die Hoehe Gelaende·15 + 10
    /// (@0x40BD8C…0x40BDA0). null = kein Gebaeudeschuss nach neuer Art.</summary>
    private (Vector2 Punkt, float Hoehe)? GebaeudeZielpunkt(Entity shooter, Entity victim, int si, int vi)
    {
        if (!victim.IsBuilding) return null;
        GebaeudeSchussZaehlen(si, vi, shooter, victim);
        GebaeudeSchuesse++;
        if (GebaeudezielAlt) return null;
        if (shooter.SchussZelleFuer != vi || shooter.SchussZelle is not { } z) return null;
        GebaeudeSchuesseAufZelle++;
        return (ZellMitte(z.X, z.Y), ElevOf(z.X, z.Y) * 15 + 10);
    }

    // ---- Messstelle fuer --gebaeudeangriff-check -----------------------------------
    private int _gaZiel = -1;
    private readonly System.Collections.Generic.Dictionary<int, int> _gaSchuesse = new();
    private readonly System.Collections.Generic.Dictionary<int, Vector2I> _gaErsteZelle = new();
    private readonly System.Collections.Generic.HashSet<Vector2I> _gaZellen = new();

    private void GebaeudeSchussZaehlen(int si, int vi, Entity shooter, Entity victim)
    {
        if (_gaZiel < 0 || vi != _gaZiel) return;
        _gaSchuesse[si] = _gaSchuesse.GetValueOrDefault(si) + 1;
        // Nullmodell: ohne Schusszelle zielt der Schuss auf den Anker / die Bildmitte.
        var zelle = !GebaeudezielAlt && shooter.SchussZelleFuer == vi && shooter.SchussZelle is { } z
            ? z : new Vector2I(victim.Col, victim.Row);
        _gaZellen.Add(zelle);
        if (!_gaErsteZelle.ContainsKey(si)) _gaErsteZelle[si] = zelle;
    }
}
