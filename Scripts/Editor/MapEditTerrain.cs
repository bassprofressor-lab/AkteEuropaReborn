namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using AkteEuropaReborn.Import;

/// <summary>
/// DER GELÄNDEPINSEL — eine Zelle umklassen oder anheben, und alles
/// nachziehen, was daran hängt.
///
/// <para><b>Warum das mehr ist als »ein Byte setzen«.</b> Eine Zelle steht in
/// DREI Rastern gleichzeitig (siehe <see cref="MapFactory"/>): dem Bild (sec1),
/// der imap (sec6) und dem Zonenraster (sec2). <see cref="MapFactory.Paint"/>
/// setzt die drei in einem Zug — das ist der ganze Grund, warum es die Methode
/// gibt. Aber der KACHELCODE einer Zelle hängt nicht nur an ihr selbst: der
/// Schlüssel der gemessenen Tabelle besteht aus Geländeklasse, Hangbyte,
/// Wassermaske der vier Nachbarn und Wasserabstand
/// (<see cref="TileModel.Key"/>). Wer eine Zelle zu Wasser macht, ändert damit
/// die Wassermaske ihrer vier Nachbarn und den Wasserabstand von allem im
/// Umkreis von <see cref="TileModel.DistCap"/>.</para>
///
/// <para>Darum malt dieser Pinsel nicht eine Zelle, sondern <b>zieht einen
/// Umkreis nach</b>: Hangbyte und Kachelcode werden für die Zelle und alles im
/// Radius <c>DistCap + 1</c> neu bestimmt. Ohne das bekäme man Wiese, die an
/// Wasser grenzt — genau der Fehler, den <see cref="MapGenerator"/> mit der
/// Ufer-Nachbedingung schon einmal gemessen hat (im Original 0 von 27.114).</para>
///
/// <para><b>Zwei gemessene Schranken, die der Pinsel NICHT brechen darf</b>
/// (beide aus <see cref="MapTerrain"/>, dort an den 26 gelieferten Karten
/// gezählt):</para>
/// <list type="number">
///   <item><b>Kein Sprung über 1.</b> Von 1.202.757 Nachbarpaaren haben 111
///     einen Sprung über <see cref="MapTerrain.MaxStep"/> — 0,0092 %.</item>
///   <item><b>Keine nicht darstellbare Hangform.</b>
///     <see cref="MapTerrain.HangByte"/> gibt −1, wenn zwei gegenüberliegende
///     oder drei Nachbarn höher sind; im Original kommt das 3 mal in 605.090
///     Zellen vor.</item>
/// </list>
/// <para>⚠ Der Generator LÖST das durch Absenken (<c>Repair</c>), also durch
/// Ändern anderer Zellen. Ein Pinsel darf das nicht: wer eine Zelle anhebt und
/// dabei stillschweigend drei andere absenkt, hat etwas anderes getan als
/// angeklickt wurde. Dieser hier <b>weist die Änderung ab und nennt den
/// Grund</b>. Das ist unsere Setzung, und sie ist die vorsichtigere.</para>
/// </summary>
public static class MapEditTerrain
{
    /// <summary>Wie weit um die geänderte Zelle herum nachgezogen wird. Der
    /// Wasserabstand reicht <see cref="TileModel.DistCap"/> weit, die
    /// Wassermaske eine Zelle — die Summe ist der Umkreis, in dem sich ein
    /// Schlüssel überhaupt ändern kann.</summary>
    public const int Reach = TileModel.DistCap + 1;

    /// <summary>Die Geländeklasse einer Zelle, aus der imap gelesen — dieselbe
    /// Zahl, mit der <see cref="TileModel"/> gefüttert wurde (0 frei, 1 rau,
    /// 2 Wasser, 3 gesperrt).</summary>
    public static int ClassAt(CwmFile m, int col, int row)
    {
        var imap = m.Sec(6);
        if (imap == null || col < 0 || col >= m.Width || row < 0 || row >= m.Height) return 3;
        int i = (col * MapFactory.ImapStride + row) * 2;
        if (i + 1 >= imap.Length) return 3;
        int v = imap[i] | (imap[i + 1] << 8);
        return v switch
        {
            MapFactory.ImapFree => 0,
            MapFactory.ImapRough => 1,
            MapFactory.ImapWater => 2,
            _ => 3,
        };
    }

    /// <summary>Das ganze Klassenraster als Feld — <see cref="TileModel"/>
    /// braucht es für Wassermaske und -abstand, und beide sind auf dem ganzen
    /// Raster definiert. ⚠ Wird bei jedem Strich neu gebaut; bei 254x254 sind
    /// das 64.516 Bytes und einmal Durchlaufen, also nichts, wofür sich ein
    /// zweiter Zustand danebenzustellen lohnt, der veralten kann.</summary>
    private static byte[] Grid(CwmFile m)
    {
        var g = new byte[m.Width * m.Height];
        for (int r = 0; r < m.Height; r++)
            for (int c = 0; c < m.Width; c++)
                g[r * m.Width + c] = (byte)ClassAt(m, c, r);
        return g;
    }

    // ========================================================================
    //  Die Klasse einer Zelle ändern
    // ========================================================================

    /// <summary>
    /// Eine Zelle auf eine andere Geländeklasse setzen.
    /// </summary>
    /// <returns>null wenn es ging, sonst der Grund.</returns>
    public static string? PaintClass(CwmFile m, TileModel? model, TilePalette? pal,
                                     int col, int row, MapFactory.Ground g, uint seed,
                                     TileSeams? seams = null)
    {
        if (col < 0 || col >= m.Width || row < 0 || row >= m.Height) return "ausserhalb der Karte";
        if (ClassAt(m, col, row) == (int)g) return null;      // schon so, kein Strich

        int elev = m.ElevAt(col, row);
        // ⚠ Wasser liegt auf seiner eigenen Hoehe. Im Original haben 36.278 von
        // 36.288 Kantenpaaren Wasser->Land die Hoehendifferenz 0, und +1 kommt
        // 0 mal vor (MapGenerator, Fehler 3 des Vorgaengers). Eine Zelle auf
        // Hoehe 4 zu Wasser zu machen ergaebe einen Teich auf dem Berg.
        if (g == MapFactory.Ground.Water && elev != WaterLevel(m, col, row))
            return $"Wasser liegt auf Hoehe {WaterLevel(m, col, row)}, diese Zelle auf {elev} — " +
                   "erst die Hoehe angleichen";

        MapFactory.Paint(m, col, row, m.CodeAt(col, row), elev, g);
        Refresh(m, model, pal, col, row, seed, seams);
        return null;
    }

    /// <summary>Auf welcher Hoehe das Wasser dieser Karte liegt. Gesucht wird
    /// die Hoehe der naechsten vorhandenen Wasserzelle; gibt es gar keine, ist
    /// es 0 — der Wasserspiegel, den <see cref="MapTerrain"/> setzt.</summary>
    private static int WaterLevel(CwmFile m, int col, int row)
    {
        for (int rad = 1; rad <= 24; rad++)
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != rad) continue;
                    int c = col + dx, r = row + dy;
                    if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) continue;
                    if (ClassAt(m, c, r) == 2) return m.ElevAt(c, r);
                }
        return 0;
    }

    // ========================================================================
    //  Die Höhe einer Zelle ändern
    // ========================================================================

    /// <summary>
    /// Eine Zelle anheben oder absenken — aber nur, wenn beide gemessenen
    /// Schranken danach noch halten.
    /// </summary>
    /// <returns>null wenn es ging, sonst der Grund.</returns>
    public static string? ChangeHeight(CwmFile m, TileModel? model, TilePalette? pal,
                                       int col, int row, int delta, uint seed,
                                       TileSeams? seams = null)
    {
        if (col < 0 || col >= m.Width || row < 0 || row >= m.Height) return "ausserhalb der Karte";
        int now = m.ElevAt(col, row);
        int want = now + delta;
        if (want < 0) return "tiefer als 0 geht nicht";
        if (want > MaxElev) return $"hoeher als {MaxElev} kommt in keiner gelieferten Karte vor";

        // ⚠ Wasser bleibt auf seiner Hoehe — sonst entstuende der Teich auf dem
        // Berg, den die Ufer-Messung ausschliesst.
        if (ClassAt(m, col, row) == 2) return "eine Wasserzelle behaelt ihre Hoehe";

        // (a) kein Sprung ueber MaxStep zu einem der vier Nachbarn
        int[] dc = { 1, -1, 0, 0 }, dr = { 0, 0, 1, -1 };
        for (int k = 0; k < 4; k++)
        {
            int c = col + dc[k], r = row + dr[k];
            if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) continue;
            int ne = m.ElevAt(c, r);
            if (Math.Abs(want - ne) > MapTerrain.MaxStep)
                return $"({c},{r}) liegt auf {ne} — der Sprung waere {Math.Abs(want - ne)}, " +
                       $"erlaubt ist {MapTerrain.MaxStep} (gemessen: 111 Ausnahmen in 1.202.757 Paaren)";
        }

        // (b) keine nicht darstellbare Hangform, weder hier noch bei den vieren
        int old = now;
        SetElev(m, col, row, want);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int c = col + dx, r = row + dy;
                if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) continue;
                if (HangOf(m, c, r) >= 0) continue;
                SetElev(m, col, row, old);            // zurueck, nichts geschehen
                return $"({c},{r}) haette danach eine Hangform, die das Spiel nicht " +
                       "zeichnen kann (zwei gegenueberliegende oder drei hoehere Nachbarn)";
            }

        Refresh(m, model, pal, col, row, seed, seams);
        return null;
    }

    /// <summary>Die hoechste Stufe, die in den gelieferten Karten vorkommt —
    /// <see cref="MapGenerator"/> berichtet die Verteilung 0..7.</summary>
    public const int MaxElev = 7;

    private static void SetElev(CwmFile m, int col, int row, int elev)
        => m.SetCell(col, row, m.CodeAt(col, row), elev, m.FlagAt(col, row));

    /// <summary>Das Hangbyte, das diese Zelle nach ihren vier Nachbarn haette —
    /// <see cref="MapTerrain.HangByte"/>, also dieselbe gemessene Regel wie im
    /// Generator, nicht eine zweite Fassung davon. −1 = nicht darstellbar.</summary>
    private static int HangOf(CwmFile m, int col, int row)
    {
        int e = m.ElevAt(col, row);
        bool Up(int c, int r)
            => c >= 0 && c < m.Width && r >= 0 && r < m.Height && m.ElevAt(c, r) > e;
        return MapTerrain.HangByte(Up(col + 1, row), Up(col, row + 1),
                                   Up(col - 1, row), Up(col, row - 1));
    }

    // ========================================================================
    //  Den Umkreis nachziehen
    // ========================================================================

    /// <summary>Wie viele Zellen der letzte Strich neu gekachelt hat, wie oft
    /// dabei kein Eintrag zu finden war, und wie oft die Ufer-Nachbedingung
    /// zugeschlagen hat — fuer den Pruefstand.</summary>
    public static int LastRetiled { get; private set; }
    public static int LastMissing { get; private set; }
    /// <summary>Wie oft der letzte Strich eine Uferzelle mit einem
    /// Innenland-Code erwischt und ersetzt hat, und wie oft das MISSLANG.</summary>
    public static int LastShoreFixed { get; private set; }
    public static int LastShoreLeft { get; private set; }

    /// <summary>Wie viele Zellen der letzte Strich UNANGETASTET gelassen hat,
    /// weil auf ihnen ein Gegenstand steht — siehe <see cref="Refresh"/>.</summary>
    public static int LastPropsKept { get; private set; }

    // ========================================================================
    //  Einen Gegenstand wieder wegnehmen
    // ========================================================================

    /// <summary>
    /// <b>EINEN GEGENSTAND VON EINER ZELLE NEHMEN.</b>
    ///
    /// <para>Gebaeude, Gleise und Einheiten liessen sich schon wegnehmen, ein
    /// Gegenstand nicht — der Editor sagte dazu »dafuer den Boden neu malen«,
    /// und das ging nicht einmal: <see cref="PaintClass"/> steigt sofort aus,
    /// wenn die Zelle die gewuenschte Klasse schon hat, und ein Gegenstand
    /// aendert die Klasse ja gerade nicht. Der Bewuchs war damit unloeschbar.</para>
    ///
    /// <para><b>Wie es geht.</b> Ein Gegenstand IST der Kachelcode der Zelle
    /// (>= <see cref="CwpFile.ObjectCodeBase"/>) — es gibt keinen zweiten Ort,
    /// an dem er stuende. Ihn wegzunehmen heisst also: den Code durch einen
    /// BODENCODE ersetzen, und zwar durch genau den, den der Generator fuer
    /// diese Zelle gezogen haette. Darum wird nicht irgendein Bodenblock-Eintrag
    /// gesetzt, sondern der Umkreis mit <see cref="Refresh"/> neu bestimmt — mit
    /// Nahtwahl, Rueckfall und Ufer-Nachbedingung, wie ueberall sonst. Der
    /// Vorbelegung mit dem Bodenblock ist nur noetig, damit die Zelle waehrend
    /// des Zugs nicht mehr als Gegenstand gilt und der Umkreislauf sie anfasst.</para>
    ///
    /// <para>⚠ Die Gelaendeklasse bleibt unangetastet, spiegelbildlich zu
    /// <c>MapEditOverlay.PutProp</c>: das Setzen hat die imap nicht angefasst,
    /// also darf das Wegnehmen es auch nicht. Sonst haette ein Baum, den man
    /// setzt und wieder wegnimmt, die Karte begehbarer gemacht als vorher.</para>
    /// </summary>
    /// <returns>null wenn es ging, sonst der Grund.</returns>
    public static string? RemoveProp(CwmFile m, TileModel? model, TilePalette? pal,
                                     int col, int row, uint seed, TileSeams? seams = null)
    {
        if (col < 0 || col >= m.Width || row < 0 || row >= m.Height) return "ausserhalb der Karte";
        int had = m.CodeAt(col, row);
        if (had < CwpFile.ObjectCodeBase)
            return $"auf ({col},{row}) steht kein Gegenstand — Kachelcode {had} ist Boden";
        if (model == null && pal == null)
            return "keine Kacheltabelle und kein Bodenblock — dann bliebe ein Loch statt Boden";

        // Vorbelegung, damit Refresh die Zelle als Boden behandelt. Der Wert
        // wird gleich wieder ersetzt; er zaehlt nur, wenn die Tabelle fuer
        // diesen Schluessel gar nichts hat und auch der Rueckfall leer ist.
        int cls = ClassAt(m, col, row);
        var block = cls == 2 ? TilePalette.Water : pal?.Ground;
        int seedCode = block is { Length: > 0 } ? block[0] : 0;
        m.SetCell(col, row, seedCode, m.ElevAt(col, row), m.FlagAt(col, row));

        Refresh(m, model, pal, col, row, seed, seams);

        int now = m.CodeAt(col, row);
        if (now >= CwpFile.ObjectCodeBase)
            return $"({col},{row}) traegt immer noch den Code {now} — nicht weggenommen";
        return null;
    }

    /// <summary>
    /// Hangbyte und Kachelcode im Umkreis neu bestimmen.
    ///
    /// <para>⚠ Erst ALLE Hangbytes, dann alle Kacheln: das Hangbyte geht in den
    /// Schluessel ein, also waere eine Kachel, die vor dem Hangbyte ihres
    /// Nachbarn gewaehlt wird, aus einem veralteten Schluessel gezogen.</para>
    ///
    /// <para>⚠ <b>Und dann dreimal genau das, was <see cref="MapGenerator"/>
    /// tut</b> — nicht »so aehnlich«: die Nahtwahl mit Westen und Norden, der
    /// Rueckfall auf den Bodenblock, und die Ufer-Nachbedingung mit ihren zwei
    /// Rettungsstufen. <b>Die ersten beiden fehlten hier, und beide Male hat es
    /// der Pruefstand gemessen:</b> ohne Nahtwahl sprangen die harten Naehte von
    /// 3,44 % auf 6,86 %, ohne Nachbedingung stand nach dem Malen 1 Uferzelle
    /// mit Innenland-Code da (Messlatte: 0 von 27.114 in 26 gelieferten Karten),
    /// und <c>map-check</c> ging von OK auf 1 Beanstandung. Ein Pinsel, der die
    /// Kachel anders zieht als der Generator, malt sichtbare Flicken.</para>
    /// </summary>
    private static void Refresh(CwmFile m, TileModel? model, TilePalette? pal,
                                int col, int row, uint seed, TileSeams? seams)
    {
        LastRetiled = LastMissing = LastShoreFixed = LastShoreLeft = LastPropsKept = 0;
        var grid = Grid(m);
        var dist = TileModel.WaterDistance(m.Width, m.Height, grid);
        var inner = pal == null ? new HashSet<int>() : new HashSet<int>(pal.Ground);
        Func<int, int, bool, double>? seamCost = seams == null ? null : seams.Cost;
        int tries = seams?.Tries ?? 1;

        for (int dy = -Reach; dy <= Reach; dy++)
            for (int dx = -Reach; dx <= Reach; dx++)
            {
                int c = col + dx, r = row + dy;
                if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) continue;
                int f = HangOf(m, c, r);
                m.SetCell(c, r, m.CodeAt(c, r), m.ElevAt(c, r), f < 0 ? 0 : f);
            }

        // ⚠ Zeilenweise von oben links nach unten rechts, wie im Generator:
        // die Nahtwahl fragt nach dem WESTLICHEN und dem NOERDLICHEN Nachbarn,
        // und nur in dieser Reihenfolge sind beide schon neu gesetzt. Ausserhalb
        // des Umkreises steht der alte Code, und der ist der richtige Nachbar —
        // dort hat sich ja nichts geaendert.
        for (int dy = -Reach; dy <= Reach; dy++)
            for (int dx = -Reach; dx <= Reach; dx++)
            {
                int c = col + dx, r = row + dy;
                if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) continue;
                // ⚠ EINE ZELLE MIT GEGENSTAND WIRD NICHT NEU GEKACHELT — und
                // das ist seit dem Oeffnen vorhandener Karten keine Kleinigkeit
                // mehr. Der Kachelcode IST hier der Gegenstand (Baum, Fels,
                // Bruecke: >= CwpFile.ObjectCodeBase, gemessen 644 Zellen auf
                // map_01, 3434 auf map_14, 6805 auf map_NET02 — und in allen
                // drei Karten liegt KEIN Code zwischen 1666 und 9999, die zwei
                // Schwellen sind sich also einig). Wer ihn im Umkreis
                // ueberschreibt, RADIERT den Gegenstand weg, nur weil zwei
                // Zellen weiter Gelaende gemalt wurde. Auf einer erzeugten Karte
                // fiel das nicht auf: die trug bis heute gar keine Gegenstaende.
                //
                // ⚠ <b>Die Gegenprobe ist GELAUFEN, nicht behauptet.</b> Ohne
                // dieses <c>continue</c> (das <c>LastPropsKept++</c> blieb
                // stehen, damit sich sonst nichts aendert) meldete
                // <c>--map-edit-check</c> auf map_01 nach ACHT Gelaendestrichen
                // <b>634 von 644</b> alten Gegenstandszellen — zehn Baeume weg.
                // Mit dem <c>continue</c>: <b>653 von 653</b>.
                if (m.CodeAt(c, r) >= CwpFile.ObjectCodeBase) { LastPropsKept++; continue; }
                int i = r * m.Width + c;
                int cls = grid[i];
                int wm = TileModel.WaterMask(m.Width, m.Height, grid, c, r);
                // ⚠ DERSELBE Wurf wie im Generator (seed ^ 0xC0DE, nicht ein
                // eigener): eine Zelle, die nur nachgezogen wird, weil der
                // Nachbar sich geaendert hat, bekommt damit genau die Kachel
                // zurueck, die vorher darauf lag. Ein eigener Wurf wuerfelte
                // den halben Umkreis bei jedem Klick neu.
                uint roll = MapTerrain.Hash(c, r, seed ^ 0xC0DEu);
                int west = c > 0 ? m.CodeAt(c - 1, r) : -1;
                int north = r > 0 ? m.CodeAt(c, r - 1) : -1;

                int code = model?.Pick(cls, m.FlagAt(c, r), wm, dist[i], roll,
                                       seamCost, tries, west, north) ?? -1;
                if (code < 0)
                {
                    // ⚠ Derselbe Rueckfall wie im Generator: der Bodenblock,
                    // wenn die gemessene Tabelle fuer diesen Schluessel nichts
                    // hat. Er kennt kein Ufer — deshalb wird er GEZAEHLT.
                    LastMissing++;
                    var block = cls == 2 ? TilePalette.Water : pal?.Ground;
                    if (block == null || block.Length == 0) continue;
                    code = block[roll % (uint)block.Length];
                }

                // DIE UFER-NACHBEDINGUNG, woertlich aus MapGenerator: eine
                // Landzelle mit Wasserabstand ≤ 1 darf keinen Innenland-Code
                // tragen. Erst die Klasse behalten und den Abstand neu ziehen,
                // dann den Abstand halten und die Klasse aufgeben.
                if (cls != 2 && dist[i] <= 1 && inner.Contains(code))
                {
                    if (model != null && model.TakeMerged(cls, 1, roll, out int rescue)
                        && !inner.Contains(rescue))
                    { code = rescue; LastShoreFixed++; }
                    else if (model != null && model.TakeAnyAt(1, roll, out int any)
                             && !inner.Contains(any))
                    { code = any; LastShoreFixed++; }
                    else LastShoreLeft++;
                }

                m.SetCell(c, r, code, m.ElevAt(c, r), m.FlagAt(c, r));
                LastRetiled++;
            }
    }
}
