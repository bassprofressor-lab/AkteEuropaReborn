namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// Der Kartengenerator. Aus Breite, Hoehe und Kachelsatz eine Karte, die die
/// Engine laedt, auf der man bauen kann und auf der ein Gefecht laeuft.
///
/// <para>⚠ <b>13.08.2026 — NEU GESCHRIEBEN.</b> Der Vorgaenger legte einen
/// Wasserrand, einen rechteckigen Berg und einen rechteckigen See auf eine
/// Ebene und malte alles mit acht Bodenkacheln aus. Der Spieler dazu:
/// »bisher kommt da nur eine fertige flache karte raus mit nem fleck wasser
/// drin«. Das war keine Frage des Geschmacks, sondern messbar falsch — die
/// Zahlen stehen bei <see cref="MapTerrain"/> und <see cref="TileModel"/>. Die
/// drei Fehler, jeder mit seiner Gegenzahl aus den 26 gelieferten Karten:</para>
/// <list type="number">
///   <item><b>Kein Uferuebergang.</b> Von 27.114 Landzellen am Wasser tragen im
///     Original <b>0</b> einen Innenland-Code — der Generator setzte auf 100 %
///     seiner Uferzellen Wiese direkt an Wasser.</item>
///   <item><b>Kein Hangbyte.</b> Von 81.357 Zellen mit einem hoeheren Nachbarn
///     tragen im Original 393 das Hangbyte 0 (0,48 %) — der Generator setzte es
///     auf allen. Damit war jede Hangzelle als Bauplatz gezaehlt, und keine
///     Hangkachel wurde gemalt.</item>
///   <item><b>Ufer eine Stufe zu hoch.</b> Im Original haben 36.278 von 36.288
///     Kantenpaaren Wasser→Land die Hoehendifferenz 0, und +1 kommt 0-mal vor —
///     genau das baute der Generator (Strand auf 1, Wasser auf 0).</item>
/// </list>
///
/// <para><b>Der Weg</b>: <see cref="MapTerrain"/> (Hoehen, Wasser, Klassen,
/// Hangbytes, alles gegen gemessene Schranken) → <see cref="TileModel"/> (der
/// Kachelcode, abgelesen aus den gelieferten Karten desselben Kachelsatzes) →
/// <see cref="MapFactory"/> (die drei Raster in einem Zug) → Startbasen und
/// Truppen → <see cref="ContentBuilder.ExportMap"/>. Geprueft wird mit
/// <see cref="MapCheck"/>, und der laeuft auch auf einer GELIEFERTEN Karte
/// (<c>--map-check=map_01</c>) — nur so ist zu sehen, ob ein Zaehler ueberhaupt
/// unterscheidet.</para>
/// </summary>
public static class MapGenerator
{
    /// <summary>Wie viele Spieler eine erzeugte Karte bekommt, wenn keine Zahl
    /// genannt ist. Startmarken gibt es fuer 0x70..0x74, also fuenf.</summary>
    public const int PlayersDefault = 2, PlayersMax = 5;

    /// <summary>Wie viele Einheiten je Spieler. UNSERE Setzung.</summary>
    public const int TroopDefault = 6;

    /// <summary>Die Basis: Typ 1, Grundflaeche 6x4 (in 82 von 83 gemessenen
    /// Faellen), eine Tuer auf (4,2) — 73 von 73 —, 1200 Trefferpunkte
    /// (map_02, map_09).</summary>
    public const int BaseType = 1, BaseFootW = 6, BaseFootH = 4, BaseHp = 1200;

    /// <summary>Die Fahrzeugfabrik: Typ 2, zwei Tueren auf (2,3) und (5,3) — 99
    /// von 99 —, 1000 Trefferpunkte, Grundflaeche 6x4 wie in den NET-Karten.
    /// Sie muss dabei sein, damit <c>SkirmishAi.PlayersWithFactory</c> den Platz
    /// ueberhaupt als Aufbauplatz kennt.</summary>
    public const int FactoryType = 2, FactoryFootW = 6, FactoryFootH = 4, FactoryHp = 1000;

    /// <summary>Ein Fahrzeug, wie die gelieferten Karten es tragen: unit_type
    /// 161, category 6, code_base 0x2710, 440 Trefferpunkte (map_09, 75 Saetze).
    /// </summary>
    public const int TroopUnitType = 161, TroopCategory = 6, TroopHp = 440;

    /// <summary>
    /// Baut die Karte im Arbeitsspeicher.
    /// </summary>
    /// <param name="players">0 = keine Startbasen (die alte, reine Landschaft).</param>
    public static CwmFile Build(string stem, int width, int height, int tileset,
                                TilePalette pal, bool flat, Action<string> say,
                                int players = PlayersDefault, int troop = TroopDefault,
                                double waterShare = MapTerrain.WaterShareDefault,
                                uint seed = 1, TileModel? model = null)
    {
        var m = MapFactory.Empty(width, height, tileset, stem);
        m.Mission = $"Editor {stem} {width}x{height}";

        // ---- 1. das Gelaende ---------------------------------------------------
        var t = MapTerrain.Build(width, height, seed, waterShare, flat, say);
        if (!flat) t.SetClasses(seed, say);

        // ---- 2. die Startplaetze INS GELAENDE, vor dem Malen -------------------
        // ⚠ Die Reihenfolge ist der Fehler, der hier fast drinstand: planiert man
        // erst und malt dann, passt die Kachel zur Hoehe; malt man erst und
        // planiert dann, liegen Bild und Hoehe auseinander — genau der Fehler,
        // den MapFactory.Paint sonst unmoeglich macht.
        var starts = new List<Start>();
        if (players > 0 && !flat)
        {
            var locked = new bool[width * height];
            starts = Reserve(t, Math.Min(players, PlayersMax), locked, say);
            t.Refresh(locked, say);
        }
        else if (players > 0)
            say("FLACH: keine Startplaetze — auf Hoehe 0 gibt es keinen Bauplatz, " +
                "das ist der Sinn der Gegenprobe");

        // ---- 3. die Kacheln ----------------------------------------------------
        // Der Rueckfallblock: wenn eine gemessene Tabelle da ist, ihr Innenland-
        // Block, sonst der erste ganze Bodenlauf aus TilePalette. Bei Kachelsatz
        // 01 sind das 90..97 gegen 8..15 — beides ganze Bodenkacheln, aber nur
        // eines davon benutzt die gelieferte Karte im Landesinneren.
        var inner = model?.InnerBlock();
        int[] ground = inner is { Count: TilePalette.GroundBlock } ? inner.ToArray() : pal.Ground;
        int[] water = TilePalette.Water;
        say($"Rueckfallblock: {string.Join(",", ground)} " +
            (inner is { Count: TilePalette.GroundBlock }
                ? "(Innenland-Block aus der gemessenen Tabelle)"
                : "(erster ganzer Bodenlauf aus TilePalette)"));
        int fromModel = 0, fromBlock = 0;
        var wdist = TileModel.WaterDistance(width, height, t.Class);
        for (int r = 0; r < height; r++)
            for (int c = 0; c < width; c++)
            {
                int i = r * width + c;
                int cls = t.Class[i];
                int wm = TileModel.WaterMask(width, height, t.Class, c, r);
                uint roll = MapTerrain.Hash(c, r, seed ^ 0xC0DEu);

                int code = model?.Pick(cls, t.Flag[i], wm, wdist[i], roll) ?? -1;
                if (code >= 0) fromModel++;
                else
                {
                    // Rueckfall ohne gemessene Tabelle: der Bodenblock aus
                    // TilePalette. Der malt kein Ufer — darum zaehlt der
                    // Pruefstand die Uferzellen mit Innenland-Code, und darum
                    // ist diese Zahl im Bericht wichtig.
                    code = cls == 2 ? water[roll % (uint)water.Length]
                                    : ground[roll % (uint)ground.Length];
                    fromBlock++;
                }

                MapFactory.Paint(m, c, r, code, t.Elev[i], (MapFactory.Ground)cls);
                // ⚠ Paint setzt die Flagge auf 0 — das Hangbyte gehoert aber zur
                // Zelle und ist die gemessene Regel aus MapTerrain.
                m.SetCell(c, r, code, t.Elev[i], t.Flag[i]);
            }
        say($"Kacheln: {fromModel} aus der gemessenen Tabelle, {fromBlock} aus dem " +
            $"Bodenblock ({pal.Describe()})" +
            (model != null ? $"; Rueckfaelle im Schluessel {model.Fallbacks}, Faecher unter " +
                             $"{TileModel.MinSupport} Beobachtungen uebergangen {model.ThinKeys}, " +
                             $"ohne Eintrag {model.Missing}" : ""));
        say("Gelaende: " + t.Describe());

        // ---- 4. Basen, Truppen, Marken -----------------------------------------
        if (starts.Count > 0) Populate(m, t, starts, troop, say);

        return m;
    }

    // ========================================================================
    //  Startplaetze — was die Karte spielbar macht
    // ========================================================================

    /// <summary>Ein Startplatz: die planierte Hochebene und die Ankerzellen der
    /// zwei Gebaeude darauf.</summary>
    private readonly record struct Start(int Player, int Col, int Row, int W, int H);

    /// <summary>Wie viele Zellen ein Startplatz braucht: Basis, eine Zelle Luft,
    /// Fabrik, und eine Zeile darunter fuer die Truppe.</summary>
    private const int PlotW = BaseFootW + 1 + FactoryFootW + 2;
    private const int PlotH = BaseFootH + 4;

    /// <summary>
    /// Fuer jeden Spieler eine Hochebene ins Gelaende schneiden.
    ///
    /// <para>Der Platz muss <c>can_build_here</c> @0x4203C0 auf seiner ganzen
    /// Flaeche erfuellen — imap frei, Hangbyte 0, alle vier Eckhoehen ≥ 2
    /// (@0x420360). Gesucht wird um den Wunschort herum ein Rechteck, das samt
    /// drei Zellen Rand KEIN Wasser enthaelt: nur dann laesst sich planieren,
    /// ohne die gemessene Ufer-Regel zu brechen (Ufer auf Wasserhoehe, 36.278 von
    /// 36.288). Findet sich keiner, bekommt dieser Spieler keinen Platz — und das
    /// steht dann in der Meldung, statt still eine unspielbare Karte zu liefern.
    /// </para>
    /// </summary>
    private static List<Start> Reserve(MapTerrain t, int players, bool[] locked, Action<string> say)
    {
        var made = new List<Start>();
        int w = t.W, h = t.H;
        if (w < PlotW + 8 || h < PlotH + 8)
        {
            say($"zu klein fuer einen Startplatz: {w}x{h}, gebraucht werden mindestens " +
                $"{PlotW + 8}x{PlotH + 8} Zellen — keine Basen");
            return made;
        }
        foreach (var (wc, wr) in Spots(w, h, players, made.Count))
        {
            int p = made.Count;
            var found = Search(t, wc, wr, made);
            if (found == null)
            {
                say($"Spieler {p}: kein wasserfreier Platz von {PlotW}x{PlotH} in der Naehe " +
                    $"von ({wc},{wr}) — dieser Spieler bleibt ohne Basis");
                continue;
            }
            var (col, row) = found.Value;
            t.Plateau(col, row, PlotW, PlotH + 1, MapCheck.MinCornerHeight, locked);
            made.Add(new Start(p, col, row, PlotW, PlotH));
        }
        return made;
    }

    /// <summary>Vom Wunschort aus nach aussen suchen, bis ein wasserfreies
    /// Rechteck da ist. Der Abstand zu bereits gesetzten Plaetzen muss
    /// mindestens die halbe kuerzere Kartenseite betragen, damit zwei Spieler
    /// nicht in derselben Ecke starten.</summary>
    private static (int, int)? Search(MapTerrain t, int wc, int wr, List<Start> taken)
    {
        int w = t.W, h = t.H;
        int minGap = Math.Max(PlotW + 4, Math.Min(w, h) / 3);
        for (int ring = 0; ring < Math.Max(w, h); ring += 2)
            for (int dr = -ring; dr <= ring; dr += 2)
                for (int dc = -ring; dc <= ring; dc += 2)
                {
                    if (ring > 0 && Math.Abs(dr) != ring && Math.Abs(dc) != ring) continue;
                    int col = Math.Clamp(wc + dc, 4, w - PlotW - 5);
                    int row = Math.Clamp(wr + dr, 4, h - PlotH - 6);
                    if (!t.WaterFree(col, row, PlotW, PlotH + 1, 3)) continue;
                    bool near = false;
                    foreach (var s in taken)
                        if (Math.Abs(s.Col - col) + Math.Abs(s.Row - row) < minGap) near = true;
                    if (near) continue;
                    return (col, row);
                }
        return null;
    }

    /// <summary>Basis, Fabrik, Truppe und Startmarke auf die planierten Plaetze.
    /// Die Grundflaechen und Tueren sind an gelieferten Karten abgelesen, siehe
    /// die Festwerte oben.</summary>
    private static void Populate(CwmFile m, MapTerrain t, List<Start> starts, int troop,
                                 Action<string> say)
    {
        int bldSlot = 0, units = 0, blds = 0;
        foreach (var s in starts)
        {
            int bcol = s.Col, brow = s.Row;
            int fcol = s.Col + BaseFootW + 1, frow = s.Row;
            if (MapFactory.PutBuilding(m, bldSlot, BaseType, s.Player, s.Player, bcol, brow,
                                       BaseFootW, BaseFootH, BaseHp, $"Start{s.Player + 1}",
                                       new[] { (4, 2) }, 100, 200, 500))
            { bldSlot++; blds++; }
            if (MapFactory.PutBuilding(m, bldSlot, FactoryType, s.Player, s.Player, fcol, frow,
                                       FactoryFootW, FactoryFootH, FactoryHp, $"Werk{s.Player + 1}",
                                       new[] { (2, 3), (5, 3) }, 0, 0, 0, 1000))
            { bldSlot++; blds++; }

            // die Truppe unter die Gebaeude, jede Zelle einzeln geprueft
            int put = 0;
            for (int k = 0; k < s.W * 3 && put < troop; k++)
            {
                int uc = s.Col + (k % s.W), ur = s.Row + BaseFootH + 1 + k / s.W;
                if (uc < 0 || uc >= m.Width || ur < 0 || ur >= m.Height) continue;
                if (t.Class[ur * t.W + uc] > 1) continue;             // Wasser oder gesperrt
                if (m.Sec(6) is { } imap)
                {
                    int at = (uc * MapFactory.ImapStride + ur) * 2;
                    if (at + 1 < imap.Length &&
                        BitConverter.ToUInt16(imap, at) < MapFactory.ImapWater) continue;
                }
                if (MapFactory.PutEntity(m, s.Player * CwmData.EntitiesPerPlayer + put, uc, ur,
                                         TroopUnitType, TroopCategory, 0, TroopHp, k & 7))
                { put++; units++; }
            }

            // Die Startmarke VOR die Basis, auf die planierte Zeile unter den
            // Gebaeuden. ⚠ Sie stand am 13.08.2026 zweimal falsch: erst in der
            // Mitte der Grundflaeche (dort ist die imap der Gebaeudegriff, der
            // Pruefstand flutete 0 Zellen weit), dann auf der Tuerzelle — die
            // laesst die imap frei, ist aber von der eigenen Grundflaeche
            // eingeschlossen, also flutete er 1 Zelle weit. Der Zaehler
            // »erreichbare Zellen« hat beide Male genau das gesagt.
            MapFactory.PutMarker(m, s.Player, bcol + 2, brow + BaseFootH,
                                 MapFactory.MarkerStartBase + s.Player);
            say($"Spieler {s.Player}: Basis ({bcol},{brow}) {BaseFootW}x{BaseFootH}, " +
                $"Fabrik ({fcol},{frow}), {put} von {troop} Fahrzeugen, " +
                $"Startmarke 0x{MapFactory.MarkerStartBase + s.Player:X2}");
        }
        say($"Startplaetze: {starts.Count} Spieler, {blds} Gebaeude, {units} Einheiten " +
            "(ohne die gaebe SkirmishAi.StartSkirmish −1 und die Karte waere im ersten " +
            "Takt verloren)");
    }

    /// <summary>Die Wunschorte verteilen: gegenueberliegende Ecken bei zwei
    /// Spielern, sonst auf einem Kreis. UNSERE Setzung.</summary>
    private static List<(int Col, int Row)> Spots(int w, int h, int players, int _)
    {
        var o = new List<(int, int)>();
        int mx = Math.Max(4, w / 8), my = Math.Max(4, h / 8);
        if (players <= 2)
        {
            o.Add((mx, my));
            if (players == 2) o.Add((w - mx - PlotW, h - my - PlotH));
            return o;
        }
        for (int p = 0; p < players; p++)
        {
            double a = 2 * Math.PI * p / players - Math.PI / 2;
            o.Add(((int)(w / 2 + Math.Cos(a) * (w / 2 - mx - PlotW / 2)),
                   (int)(h / 2 + Math.Sin(a) * (h / 2 - my - PlotH / 2))));
        }
        return o;
    }

    // ---- Schreiben ----------------------------------------------------------

    /// <summary>
    /// Die Karte dorthin schreiben, wo die Engine sie findet:
    /// <c>user://data/Maps/map_&lt;name&gt;.*</c>. Genau der Weg, den auch der
    /// Import nimmt — <see cref="ContentBuilder.ExportMap"/>.
    ///
    /// <para>⚠ Und die GEBAEUDEMUSTER des Kachelsatzes dazu
    /// (<see cref="ContentBuilder.ExportBuildingPatterns"/>): die Engine oeffnet
    /// im Spiel nie eine <c>.CWP</c>, also kann ohne sie kein Gebaeude gezeichnet
    /// und keines gebaut werden. Der Import schreibt sie je Kachelsatz, der
    /// Editor tat es bis heute nicht — auf einem Kachelsatz, den keine
    /// gelieferte Karte benutzt, stand die Basis darum unsichtbar da.</para>
    /// </summary>
    public static string Write(CwmFile m, CwpFile cwp, PalFile pal, string outName, Action<string> say)
    {
        string root = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\');
        string maps = root + "/Maps";
        int px = ContentBuilder.ExportBuildingPatterns(cwp, pal, m.Tileset, root);
        say(px > 0
            ? $"Gebaeudemuster Kachelsatz {m.Tileset:00}: Bildtafel {px} Pixel nach {root}/Buildings"
            : $"Gebaeudemuster Kachelsatz {m.Tileset:00}: die .CWP hat keinen Gebaeudeteil — " +
              "auf dieser Karte kann nichts gebaut werden");

        var img = ContentBuilder.ExportMap(m, cwp, pal, maps, outName, out var baker, out var doc);
        say($"{outName}: Bild {img.GetWidth()}x{img.GetHeight()} px " +
            $"(erwartet {baker.PixelW}x{baker.PixelH}), Ursprung y={baker.OriginY}");
        var t = doc.Terrain;
        say($"{outName}: Gelaende frei {t.Histogram[0]} / rau {t.Histogram[1]} / " +
            $"wasser {t.Histogram[2]} / gesperrt {t.Histogram[3]}, {t.Inferred} abgeleitet");
        say($"{outName}: {doc.Entities.Count} Einheiten, {doc.Buildings.Count} Gebaeude, " +
            $"{doc.Markers.Count} Marken, {doc.RailCells.Count} Gleiszellen");
        say($"geschrieben nach {maps}/{outName}.{{png,json,entities.json}}");
        return maps;
    }

    // ---- den Kachelsatz finden ---------------------------------------------

    /// <summary>
    /// <c>NN.CWP</c> und <c>NN.PAL</c>. Die Engine oeffnet im Spiel NIE eine
    /// <c>.CWP</c> — das Bild ist vorgebacken —, der Editor muss aber backen,
    /// also braucht er den Kachelsatz. Gesucht wird im Entwicklungsbaum, im
    /// eingespielten Ordner und zuletzt auf einer eingelegten Spiel-CD.
    /// </summary>
    public static (string Cwp, string Pal)? FindTileset(int tileset, string? hint)
    {
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(hint))
        {
            dirs.Add(hint!.TrimEnd('/', '\\'));
            dirs.Add(hint!.TrimEnd('/', '\\') + "/DATA");
        }
        dirs.Add(ProjectSettings.GlobalizePath(Core.Content.DevRoot).TrimEnd('/', '\\') + "/DATA");
        dirs.Add(ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/DATA");
        var discs = Core.ContentSources.Discs();
        if (discs != null) foreach (string r in discs.Roots) dirs.Add(r + "/DATA");

        foreach (string d in dirs)
        {
            string cwp = $"{d}/{tileset:00}.CWP", pal = $"{d}/{tileset:00}.PAL";
            if (File.Exists(cwp) && File.Exists(pal)) return (cwp, pal);
        }
        return null;
    }
}
