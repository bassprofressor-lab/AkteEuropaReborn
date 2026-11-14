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
        if (ClassAt(m, col, row) == (int)g && !GeroellBildFehlt(m, col, row, g)) return null;  // schon so, kein Strich

        int elev = m.ElevAt(col, row);
        // ⚠ Wasser liegt auf seiner eigenen Hoehe. Im Original haben 36.278 von
        // 36.288 Kantenpaaren Wasser->Land die Hoehendifferenz 0, und +1 kommt
        // 0 mal vor (MapGenerator, Fehler 3 des Vorgaengers). Eine Zelle auf
        // Hoehe 4 zu Wasser zu machen ergaebe einen Teich auf dem Berg.
        if (g == MapFactory.Ground.Water && elev != WaterLevel(m, col, row))
            return $"Wasser liegt auf Hoehe {WaterLevel(m, col, row)}, diese Zelle auf {elev} — " +
                   "erst die Hoehe angleichen";

        MapFactory.Paint(m, col, row, m.CodeAt(col, row), elev, g);
        Refresh(m, model, pal, col, row, seed, seams, (int)g);
        return null;
    }

    /// <summary>Auf welcher Hoehe das Wasser dieser Karte liegt. Gesucht wird
    /// die Hoehe der naechsten vorhandenen Wasserzelle; gibt es gar keine, ist
    /// es 0 — der Wasserspiegel, den <see cref="MapTerrain"/> setzt.</summary>
    /// <summary>D9 (13.11.2026): der Wasserspiegel der Umgebung, fuer den Bruecken-/Rampenabriss.</summary>
    public static int WasserSpiegel(CwmFile m, int col, int row) => WaterLevel(m, col, row);

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

    /// <summary>⭐ 14.11.2026 — Editor-F (Fable-Pruefung): DAS ERGEBNIS EINES STRICHS als ein Objekt.
    /// Bis heute neun einzelne statische Zaehler (Letzter.Retiled, Letzter.Missing, …), die jeder Strich
    /// zuruecksetzte und hochzaehlte; jetzt ein Satz je Strich (<see cref="Letzter"/>), neu in
    /// <c>Refresh</c>. Die Bedeutung jedes Feldes ist dieselbe wie vorher.</summary>
    public sealed class StrichErgebnis
    {
        /// <summary>Zellen neu gekachelt / davon ohne Eintrag (aus dem Bodenblock).</summary>
        public int Retiled, Missing;
        /// <summary>Uferzellen mit Innenland-Code ersetzt / nicht ersetzbar.</summary>
        public int ShoreFixed, ShoreLeft;
        /// <summary>Zellen mit Gegenstand unangetastet (siehe Refresh).</summary>
        public int PropsKept;
        /// <summary>bug-819: nach Ecken neu gezogen / liegen gelassen.</summary>
        public int CornerPicked, CornerKept;
        /// <summary>bug-827: Kettenring — Ecken umgestofft / Ring abgelehnt.</summary>
        public int RingGeaendert, RingAbgelehnt;
    }

    /// <summary>Das Ergebnis des letzten Strichs (fuer Meldung und Pruefstand).</summary>
    public static StrichErgebnis Letzter { get; private set; } = new();

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
        BelegungFreigeben(m, col, row);
        return null;
    }

    /// <summary>⭐ 12.11.2026 (Wald brennt): nach dem Wegnehmen eines Gegenstands die
    /// BELEGUNG der Zelle zuruecksetzen. Bisher blieb sie stehen — ein weggenommener Baum
    /// liess eine gesperrte Zelle zurueck (Belegung 0xFFFF) bzw. einen Waldeintrag, der
    /// weiter brennen koennte. Waldeintrag auf Zustand 0 (»weg«, MapForest), Belegung
    /// aus dem Zonenraster wie bei MapFactory.ClearBuilding. Nur fuer Wald, zerstoerbare
    /// Objekte und 0xFFFF — eine Einheit oder ein Gebaeude auf der Zelle bleibt.
    /// Gegenschalter --generator-wald-alt.</summary>
    private static void BelegungFreigeben(CwmFile m, int col, int row)
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--generator-wald-alt") >= 0) return;
        var imap = m.Sec(MapForest.ImapSektion);
        if (imap == null) return;
        int i = (col * MapFactory.ImapStride + row) * 2;
        if (i + 1 >= imap.Length) return;
        int v = imap[i] | (imap[i + 1] << 8);
        bool wald = MapForest.IstWald(v);
        bool objekt = v >= MapForest.ObjektVon && v < MapForest.ObjektBis;
        if (!wald && !objekt && v != MapFactory.ImapBlocked) return;
        if (wald && m.Sec(MapForest.WaldSektion) is { } w)
        {
            int k = (v - MapForest.WaldVon) * MapForest.WaldStride;
            if (k + 2 < w.Length) w[k + 2] = 0;                  // Zustand 0 = weg
        }
        var zone = m.Sec(2);
        int z = zone == null ? MapFactory.ZoneLand : MapDeposits.Zone(zone, m.Width, m.Height, col, row);
        int neu = z >= MapFactory.ZoneLand ? MapFactory.ImapFree : z == MapFactory.ZoneShore ? MapFactory.ImapRough
                : v == MapFactory.ImapBlocked ? MapFactory.ImapBlocked : MapFactory.ImapWater;
        imap[i] = (byte)(neu & 0xFF); imap[i + 1] = (byte)(neu >> 8);
    }

    /// <summary>⭐ 12.11.2026: einen gesetzten BAUM in die Waldtafel eintragen (Belegung
    /// 50000 + k, Zustand 1) — derselbe Eintrag wie beim Erzeugen. Rueckgabe: ob es ging
    /// (die Tafel hat 6000 Plaetze; ein Platz mit Zustand 0 wird wiederverwendet).</summary>
    public static bool WaldSetzen(CwmFile m, int col, int row)
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--generator-wald-alt") >= 0) return false;
        var imap = m.Sec(MapForest.ImapSektion);
        var w = m.Sec(MapForest.WaldSektion);
        if (imap == null || w == null) return false;
        int max = Math.Min(w.Length / MapForest.WaldStride, MapForest.WaldBis - MapForest.WaldVon);
        for (int k = 0; k < max; k++)
        {
            if (w[k * MapForest.WaldStride + 2] != 0) continue;
            int i = (col * MapFactory.ImapStride + row) * 2;
            if (i + 1 >= imap.Length) return false;
            int v = MapForest.WaldVon + k;
            imap[i] = (byte)(v & 0xFF); imap[i + 1] = (byte)(v >> 8);
            w[k * MapForest.WaldStride] = (byte)col; w[k * MapForest.WaldStride + 1] = (byte)row;
            w[k * MapForest.WaldStride + 2] = 1;
            return true;
        }
        return false;
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
                                int col, int row, uint seed, TileSeams? seams, int malKlasse = -1)
    {
        Letzter = new StrichErgebnis();
        var grid = Grid(m);
        var dist = TileModel.WaterDistance(m.Width, m.Height, grid);
        var inner = pal == null ? new HashSet<int>() : new HashSet<int>(pal.Ground);
        Func<int, int, bool, double>? seamCost = seams == null ? null : seams.Cost;
        int tries = seams?.Tries ?? 1;

        FlagBeimLegen.Clear();
        for (int dy = -Reach; dy <= Reach; dy++)
            for (int dx = -Reach; dx <= Reach; dx++)
            {
                int c = col + dx, r = row + dy;
                if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) continue;
                FlagBeimLegen[(c, r)] = m.FlagAt(c, r);
                int f = HangOf(m, c, r);
                m.SetCell(c, r, m.CodeAt(c, r), m.ElevAt(c, r), f < 0 ? 0 : f);
            }

        // ⭐ 12.11.2026 bug-819: DER ECKWEG. Der Generator waehlt seine Kacheln seit
        // bug-771 nach den VIER ECKSTOFFEN (TileCorners); dieser Pinsel zog bis heute
        // aus der alten Tabelle (TileModel) und kachelte dabei den ganzen Umkreis neu.
        // Auf einer erzeugten Karte passten danach die Ecken nicht mehr: nach drei
        // Anhebungen und acht Geroellstrichen meldete map-check 8 »Wasserflecken im
        // Land« (blaue Punkte), vorher 0. Jetzt: die Eckstoffe des Umkreises aus den
        // liegenden Kacheln lesen, bei einer Klassenmalerei die vier Ecken der Zelle
        // auf den Stoff der neuen Klasse setzen, und nur Zellen neu ziehen, deren
        // Ecken oder Hangbyte sich geaendert haben — der Rest bleibt liegen. Passt
        // keine gemessene Kachel zu Ecken+Hangbyte, faellt die Zelle auf den alten
        // Weg zurueck (gezaehlt). Gegenschalter --editor-pinsel-ecken-alt.
        var tc = EckModell(m);
        int[]? vtx = null; int vx0 = col - Reach, vy0 = row - Reach, vw = 2 * Reach + 2;
        int ziel = -1;
        if (tc != null)
        {
            vtx = new int[vw * vw];
            for (int vy = 0; vy < vw; vy++)
                for (int vx = 0; vx < vw; vx++)
                    vtx[vy * vw + vx] = EckStoff(m, tc, vx0 + vx, vy0 + vy);
            ziel = malKlasse >= 0 ? ZielStoff(tc, malKlasse) : -1;
            if (ziel >= 0)
            {
                // ⭐ 13.11.2026 bug-827: DER KETTENRING. Eine Ecke darf sich hoechstens
                // um so viele Kettenstufen vom Zielstoff unterscheiden, wie sie Ecken
                // von der bemalten Zelle entfernt liegt (Schachbrettabstand). Damit
                // legt sich um Geroell in Sand von selbst ein Grasring, und keine
                // Zelle mischt zwei Stoffe, die in der Kette nicht benachbart sind
                // (Satz 47: Wasser–Sand–Gras–Geroell). Die Randecken des Fensters
                // liegen R=4 entfernt und bleiben damit immer, wie sie sind.
                // ⚠ Wasserecken werden NIE umgestoffen (Wasser ist Klasse und Hoehe,
                // nicht nur Bild) — muesste eine, bleibt es bei den vier Ecken der
                // Zelle wie bis gestern (gezaehlt in Letzter.RingAbgelehnt).
                // ⚠ Gemessen 13.11. (--map-edit-check, Zelle (78,5) am Ufer): lehnt der Ring
                // ab und das Geroellbild setzt trotzdem die vier Ecken, entsteht ein
                // Wasserfleck im Eckraster (Ecke 79,5). Dann gilt fuer das GEROELLBILD
                // wieder der Weg von gestern (StoffDerKlasse, fuer rau: keiner).
                if (tc.IsChain && !KettenringAlt && !RingSetzen(tc, vtx, vw, col - vx0, row - vy0, ziel))
                {
                    Letzter.RingAbgelehnt++;
                    int vorher = StoffDerKlasse(tc, malKlasse);
                    if (vorher != ziel) ziel = vorher;
                }
                if (ziel >= 0)
                    for (int k = 0; k < 4; k++)
                        vtx[(row - vy0 + k / 2) * vw + (col - vx0 + k % 2)] = ziel;
            }
        }
        int QuadAus(int c, int r)
        {
            int a0 = vtx![(r - vy0) * vw + (c - vx0)], a1 = vtx[(r - vy0) * vw + (c - vx0 + 1)];
            int a2 = vtx[(r - vy0 + 1) * vw + (c - vx0)], a3 = vtx[(r - vy0 + 1) * vw + (c - vx0 + 1)];
            if (a0 < 0 || a1 < 0 || a2 < 0 || a3 < 0) return -1;
            return TileCorners.Quad(a0, a1, a2, a3);
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
                // dieses <c>continue</c> (das <c>Letzter.PropsKept++</c> blieb
                // stehen, damit sich sonst nichts aendert) meldete
                // <c>--map-edit-check</c> auf map_01 nach ACHT Gelaendestrichen
                // <b>634 von 644</b> alten Gegenstandszellen — zehn Baeume weg.
                // Mit dem <c>continue</c>: <b>653 von 653</b>.
                if (m.CodeAt(c, r) >= CwpFile.ObjectCodeBase) { Letzter.PropsKept++; continue; }
                int i = r * m.Width + c;
                // ⚠ Gemessen (Kachelsatz 47): »rau« hat KEINEN eigenen Eckstoff — Geroell
                // gibt es in den Originalen nur am Hang. Fuer die bemalte Zelle selbst
                // bleibt dann der alte Weg (sonst hiesse sie rau und saehe aus wie Wiese);
                // ihre Nachbarn bleiben liegen.
                bool alterWeg = malKlasse >= 0 && ziel < 0 && c == col && r == row;
                if (tc != null && !alterWeg)
                {
                    int quad = QuadAus(c, r);
                    int alt = m.CodeAt(c, r);
                    int flag = m.FlagAt(c, r);
                    if (quad >= 0 && tc.Knows(alt) && tc.QuadOf(alt) == quad && tc.Has(quad, flag)
                        && FlagBeimLegen.TryGetValue((c, r), out int f0) && f0 == flag)
                    { Letzter.CornerKept++; continue; }               // Ecken und Hang wie vorher: liegen lassen
                    if (quad >= 0 && tc.Has(quad, flag))
                    {
                        uint roll0 = MapTerrain.Hash(c, r, seed ^ 0xC0DEu);
                        int w0 = c > 0 && m.CodeAt(c - 1, r) < TileCorners.GroundMax ? m.CodeAt(c - 1, r) : -1;
                        int n0 = r > 0 && m.CodeAt(c, r - 1) < TileCorners.GroundMax ? m.CodeAt(c, r - 1) : -1;
                        int k0 = tc.Pick(quad, flag, roll0, seamCost, Math.Min(tries, 12), w0, n0);
                        if (k0 >= 0)
                        {
                            m.SetCell(c, r, k0, m.ElevAt(c, r), flag);
                            Letzter.CornerPicked++; Letzter.Retiled++;
                            continue;
                        }
                    }
                }
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
                    Letzter.Missing++;
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
                    { code = rescue; Letzter.ShoreFixed++; }
                    else if (model != null && model.TakeAnyAt(1, roll, out int any)
                             && !inner.Contains(any))
                    { code = any; Letzter.ShoreFixed++; }
                    else Letzter.ShoreLeft++;
                }

                m.SetCell(c, r, code, m.ElevAt(c, r), m.FlagAt(c, r));
                Letzter.Retiled++;
            }
    }

    // ---- bug-819: der Eckweg ------------------------------------------------


    public static bool EckenAlt => Array.IndexOf(Core.CommandLine.Args, "--editor-pinsel-ecken-alt") >= 0;

    private static readonly Dictionary<(int, int), int> FlagBeimLegen = new();
    private static readonly Dictionary<int, TileCorners?> _eck = new();

    /// <summary>Fuer Pruefstaende: das Eckmodell der Karte (null mit Gegenschalter).</summary>
    public static TileCorners? EckModellFuer(CwmFile m) => EckModell(m);

    /// <summary>Das Eckmodell des Kachelsatzes, einmal gelernt und behalten.</summary>
    private static TileCorners? EckModell(CwmFile m)
    {
        if (EckenAlt) return null;
        int ts = m.Tileset;
        if (!_eck.TryGetValue(ts, out var tc))
        {
            try { tc = TileCorners.Learn(ts, _ => { }, MapEditSession.Cwp); }
            catch (Exception) { tc = null; }
            _eck[ts] = tc;
        }
        return tc;
    }

    /// <summary>Der Stoff einer Ecke: die Mehrheit der bis zu vier anliegenden
    /// Bodenkacheln (dieselbe Regel wie map-check »Wasserflecken im Eckraster«).
    /// −1, wenn keine anliegende Kachel bekannt ist.</summary>
    private static int EckStoff(CwmFile m, TileCorners tc, int x, int y)
    {
        var n = new int[16];
        int best = -1, bestN = 0;
        void V(int c, int r, int k)
        {
            if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) return;
            int code = m.CodeAt(c, r);
            if (code >= TileCorners.GroundMax || !tc.Knows(code)) return;
            int l = tc.Corner(code, k);
            if (l < 0 || l > 15) return;
            n[l]++;
            if (n[l] > bestN) { bestN = n[l]; best = l; }
        }
        V(x - 1, y - 1, 3); V(x, y - 1, 2); V(x - 1, y, 1); V(x, y, 0);
        return best;
    }

    // ---- bug-827: Geroellbild und Kettenring --------------------------------


    public static bool KettenringAlt => Array.IndexOf(Core.CommandLine.Args, "--editor-kettenring-aus") >= 0;
    public static bool GeroellBildAlt => Array.IndexOf(Core.CommandLine.Args, "--editor-geroellbild-aus") >= 0;

    /// <summary>
    /// ⭐ 13.11.2026 bug-827: der Stoff, den eine Malklasse im Bild bekommt.
    /// <para><b>GEMESSEN (Satz 47, NET02+NET07):</b> »rau« hat KEIN eigenes Bild. Die
    /// gleichfoermigen Kacheln je Stoff tragen als Zellklasse frei/rau/Wasser/gesperrt:
    /// Gras 18518/3446/0/97, Sand 8399/219/0/17, Geroell(148) 4356/417/0/19 — rau ist
    /// in den Originalen meist Gras am Hang, und das Geroellbild ist fast immer »frei«.
    /// <see cref="StoffDerKlasse"/> fand fuer rau deshalb nichts, die bemalte Zelle
    /// ging den alten Weg und bekam eine Kachel, die aussah wie vorher: ein 3x3-Strich
    /// auf Sand aenderte keinen einzigen Bildpunkt (--map-edit-sofort-check).</para>
    /// <para><b>UNSERE SETZUNG:</b> der Geroellpinsel (so heisst er im Bedienfeld) malt
    /// das GEROELLBILD — den Stoff am wasserfernen Ende der Kette, wenn der nicht der
    /// Stoff von »frei« ist — und setzt die Zelle auf rau. Gegenschalter
    /// <c>--editor-geroellbild-aus</c>.</para>
    /// </summary>
    private static int ZielStoff(TileCorners tc, int klasse)
    {
        int l = StoffDerKlasse(tc, klasse);
        if (l >= 0 || klasse != (int)MapFactory.Ground.Rough || GeroellBildAlt || !tc.IsChain) return l;
        int ende = tc.LabelOfDepth[tc.MaxDepth];
        if (ende == tc.WaterLabel || ende == StoffDerKlasse(tc, (int)MapFactory.Ground.Free)) return -1;
        int q = TileCorners.Quad(ende, ende, ende, ende);
        return tc.Has(q, 0) ? ende : -1;
    }

    /// <summary>Ob eine Zelle schon die Klasse hat, aber noch nicht das Bild des
    /// Geroellpinsels — dann ist ein Strich doch noetig.</summary>
    private static bool GeroellBildFehlt(CwmFile m, int col, int row, MapFactory.Ground g)
    {
        if (g != MapFactory.Ground.Rough) return false;
        var tc = EckModell(m);
        if (tc == null) return false;
        int ziel = ZielStoff(tc, (int)g);
        if (ziel < 0) return false;
        int code = m.CodeAt(col, row);
        if (code >= CwpFile.ObjectCodeBase || !tc.Knows(code)) return false;
        for (int k = 0; k < 4; k++) if (tc.Corner(code, k) != ziel) return true;
        return false;
    }

    /// <summary>Den Kettenring legen: jede Ecke v wird auf
    /// Zielstufe ± Schachbrettabstand(v, Zelle) begrenzt. Ist die alte Karte in sich
    /// kettentreu, ist es danach auch das Ergebnis (die Begrenzung aendert sich je
    /// Nachbarecke um hoechstens eine Stufe). false, wenn dabei eine Wasserecke
    /// umgestofft werden muesste — dann wird NICHTS geaendert.</summary>
    private static bool RingSetzen(TileCorners tc, int[] vtx, int vw, int lx, int ly, int ziel)
    {
        int z = tc.Depth[ziel];
        var neu = (int[])vtx.Clone();
        int geaendert = 0;
        for (int y = 0; y < vw; y++)
            for (int x = 0; x < vw; x++)
            {
                int l = vtx[y * vw + x];
                if (l < 0) continue;
                int d = Math.Max(Math.Max(0, Math.Max(lx - x, x - (lx + 1))),
                                 Math.Max(0, Math.Max(ly - y, y - (ly + 1))));
                if (d == 0) continue;                              // die vier Zellecken setzt der Aufrufer
                int t = Math.Clamp(tc.Depth[l], z - d, z + d);
                if (t == tc.Depth[l]) continue;
                if (l == tc.WaterLabel) return false;
                neu[y * vw + x] = tc.LabelOfDepth[t];
                geaendert++;
            }
        Array.Copy(neu, vtx, vtx.Length);
        Letzter.RingGeaendert += geaendert;
        return true;
    }

    /// <summary>Der Stoff, der eine Klasse am haeufigsten traegt: der Stoff, dessen
    /// gleichfoermige Kachel (vier gleiche Ecken) diese Klasse hat, mit dem
    /// groessten gemessenen Anteil. −1, wenn es keinen gibt.</summary>
    private static int StoffDerKlasse(TileCorners tc, int klasse)
    {
        int best = -1; double bestA = -1;
        for (int l = 0; l < tc.Labels; l++)
        {
            int q = TileCorners.Quad(l, l, l, l);
            if (!tc.Has(q, 0) || tc.ClassOf(q) != klasse) continue;
            double a = l < tc.LabelShare.Length ? tc.LabelShare[l] : 0;
            if (a > bestA) { bestA = a; best = l; }
        }
        return best;
    }
}
