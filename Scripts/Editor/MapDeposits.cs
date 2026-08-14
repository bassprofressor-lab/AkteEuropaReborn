namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using AkteEuropaReborn.Import;

/// <summary>
/// DIE ROHSTOFFVORKOMMEN einer erzeugten Karte.
///
/// <para><b>⚠ UNSERE ZUTAT, und zwar die ganze Verteilung.</b> Im Original kommen
/// die Vorkommen NICHT aus der Karte: das Missionsskript legt sie über
/// <c>add_terra_place(spalte, zeile, menge)</c> (C: <c>0x4D0A10</c>,
/// F: <c>0x4D05C0</c>) im SETUP-Block an — gemessen <b>50 Aufrufe in 8
/// Missionen</b>, jeder mit drei Konstanten, in beiden Fassungen gleich
/// (<c>aekernel-tools/mission_terra.py</c>). Eine erzeugte Karte hat kein
/// Missionsskript. Es gibt also keine gelesene Vorschrift, WOHIN die Vorkommen
/// einer neuen Karte gehören; die Regeln unten sind gesetzt, nicht gelesen.
/// Gelesen sind nur die ZAHLEN, auf die sie abgebildet werden.</para>
///
/// <para><b>Was ohne sie war, mit Zahl:</b> auf einer erzeugten Karte meldete
/// <c>--build-check</c> für die Feld-Rohstoffmine (Typ 15) <b>0 Bauplätze</b> und
/// der Kontostand blieb <b>0</b> — nichts im Boden, also nichts zu holen. Auf
/// <c>map_23</c> waren es nach der Bauplatz-Reparatur 57.</para>
///
/// <para><b>DIE MESSLATTE</b>, aus den 50 Originalvorkommen gegen sec2 und die
/// imap der acht Karten gehalten (<c>aekernel-tools/terra_stats.py</c>):</para>
/// <code>
///   Vorkommen je Karte      2 .. 11        (map_26 = 2, map_23 = 11)
///   je 1000 begehbare Zellen 0,23          (Spanne 0,16 .. 0,37)
///   Menge                   5000 x50       — EINE Zahl, 50 von 50
///   sec2-Klasse             2 x34 (68 %), 3 x16 (32 %); nie 0, nie 1
///   imap-Klasse             frei 48 (96 %), rau 1, gesperrt 1
///   tragende Anker von 9    Mittel 8,78, min 5, max 9; ohne Anker 0 von 50
///   Abstand zum Kartenrand  Mittel 39,3, min 5, 10 %-Quantil 11, max 93
///   naechster Nachbar       Mittel 46,5, min 11,4; unter 20 Zellen 24 %
/// </code>
///
/// <para>⚠ <b>Die Zeile »tragende Anker« oben ist die PUNKTprüfung</b> — eine
/// Zelle für sich, ohne die Grundfläche der Mine. Mit der ganzen Grundfläche
/// (30 Zellen in 10x6) sagt <c>--terra-check</c> auf <c>map_23</c> etwas anderes,
/// und das ist die Messlatte, gegen die hier gelegt wird: <b>57 von 99 Ankern</b>
/// (5,18 von 9 im Mittel), und <b>3 der 11 Vorkommen tragen keine einzige
/// Mine</b>. Die beiden Zahlen sind nicht dieselbe Frage; sie stehen darum beide
/// da, statt eine als die andere auszugeben.</para>
///
/// <para><b>Wovon abgewichen wird, ausdrücklich:</b> die sec2-Klasse <b>3</b>
/// (»besonderes Land«, 32 % der Originalvorkommen) kann hier nicht getroffen
/// werden — <see cref="MapFactory.Paint"/> schreibt nur 0, 1 und 2, der Generator
/// legt also gar kein besonderes Land an. Alle erzeugten Vorkommen liegen darum
/// auf Klasse 2. Und die 4 % der Originale auf »rau«/»gesperrt« werden nicht
/// nachgebildet: ein Vorkommen ohne einen einzigen Bauplatz wäre für den Spieler
/// nur Verdruss, und ohne Anker liegt im Original keines.</para>
///
/// <para><b>Und die dritte Abweichung, gemessen und nicht schöngeredet:</b> im
/// Original liegen <b>24 % der Vorkommen weniger als 20 Zellen</b> vom nächsten
/// entfernt (12 von 50) — es gibt PAARE. Hier sind es <b>0 %</b> (gemessen über
/// 15 Vorkommen auf drei erzeugten Karten: nächster Nachbar min 27,9, Mittel
/// 51,2 gegen 46,5). Der Greedy-Lauf mit Mindestabstand streut gleichmässiger,
/// als das Original legt. Wer die Paare will, braucht eine zweite Regel; sie ist
/// hier nicht erfunden worden.</para>
///
/// <para><b>Gefragt wird die FERTIGE Karte</b>, nicht das Höhenfeld: gelesen
/// werden sec6 (die imap) und sec2 (die Zonenklassen), so wie
/// <c>can_build_here</c> @0x4203C0 und <c>corners_carry</c> @0x4211A0 sie lesen.
/// Deshalb läuft dieser Schritt NACH den Startbasen — was unter einer Basis
/// liegt, ist kein Bauplatz, und ein Vorkommen darunter wäre verschenkt.</para>
/// </summary>
public static class MapDeposits
{
    /// <summary>Die Menge je Vorkommen. GEMESSEN: 5000 in 50 von 50 Aufrufen.
    /// </summary>
    public const int Amount = 5000;

    /// <summary>Vorkommen je 1000 begehbaren Zellen. GEMESSEN über die acht
    /// Karten: 0,23 (je Karte 0,16 .. 0,37).</summary>
    public const double PerThousandWalkable = 0.23;

    /// <summary>Nie weniger als so viele — <c>map_26</c> ist die dünnste
    /// Originalkarte und hat 2. Eine erzeugte 64x64-Karte käme über die Dichte
    /// nur auf 1, und eine Karte mit einem Vorkommen ist keine Karte, auf der
    /// sich »Bauen Sie fünf Rohstoffminen« stellen liesse.</summary>
    public const int Fewest = 2;

    /// <summary>Nie mehr als so viele: das Original bricht mit »Cannot add more
    /// terra_places« @0x4D05E7 ab, und die Tafel bei 0x677448 hat 50 Plätze
    /// (Schritt 14).</summary>
    public const int Most = 50;

    /// <summary>Mindestabstand zum Kartenrand. GEMESSEN: min 5 von 50.</summary>
    public const int EdgeMin = 5;

    /// <summary>Mindestabstand zum nächsten Vorkommen. GEMESSEN: der kleinste
    /// Nachbarabstand der 50 ist 11,4 Zellen.</summary>
    public const int SpacingMin = 11;

    /// <summary>Wieviele der neun Anker im 3x3-Fenster mindestens eine Mine
    /// tragen müssen — mit der GANZEN Grundfläche geprüft.
    ///
    /// <para>⚠ Die Zahl 1 ist nicht Bequemlichkeit, sie ist die Messlatte:
    /// <c>--terra-check</c> auf <c>map_23</c> zählt über die 11 Originalvorkommen
    /// 99 Anker und <b>57 tragende</b> (5,18 von 9 im Mittel), und <b>3 der 11
    /// Vorkommen tragen gar keine Mine</b> — (200,64), (123,58) und (231,173).
    /// Die 10x6-Grundfläche ist eng. Verlangt wird darum, dass wenigstens EIN
    /// Anker trägt: mehr als das Original selbst hält, aber weniger wäre ein
    /// Vorkommen, auf dem der Spieler nichts bauen kann.</para>
    ///
    /// <para>Die andere Zahl in <c>terra_stats.py</c> (Mittel 8,78, min 5) ist
    /// mit der PUNKTprüfung gemessen, ohne Grundfläche; die beiden sind nicht
    /// dieselbe Frage und werden hier nicht vermischt.</para></summary>
    public const int AnchorsMin = 1;

    /// <summary>sec2 ab dieser Klasse trägt ein Gebäude — <c>corners_carry</c>
    /// @0x4211A0 auf allen vier Ecken.</summary>
    public const int MinCornerClass = MapCheck.MinCornerClass;

    /// <summary>Der Gebäudetyp, um den es geht — die Feld-Rohstoffmine.</summary>
    public const int TypeFieldMine = 15;

    /// <summary>
    /// Die Vorkommen in die Karte legen und in <see cref="CwmFile.Terra"/>
    /// hinterlegen. Rückgabe ist die Zahl der gelegten Vorkommen.
    ///
    /// <para>⚠ <paramref name="cwp"/> ist NICHT nebensächlich, und es hat einen
    /// Fehlschlag gekostet: die Mine ist kein Punkt. Ihr Muster (Typ 15,
    /// Kachelsatz 47) belegt <b>30 Zellen</b> in einem 10x6-Raster, und
    /// <c>can_build_here</c> prüft JEDE davon. Ein Vorkommen, dessen neun Anker
    /// nur SELBST auf offenem Land liegen, taugt darum nichts: gemessen am
    /// 13.08.2026 auf <c>map_terra96</c> — 2 Vorkommen, alle 9 Anker »tragend«
    /// nach der Punktprüfung, und <c>--build-check</c> meldete weiter
    /// <b>0 Bauplätze</b>. Mit dem Muster wird die ganze Grundfläche gefragt,
    /// also dasselbe, was der Bautechniker fragt.</para>
    /// </summary>
    public static int Place(CwmFile m, uint seed, CwpFile? cwp, Action<string> say)
    {
        m.Terra.Clear();
        var imap = m.Sec(6);
        var zone = m.Sec(2);
        if (imap == null || zone == null)
        {
            say("Vorkommen: die Karte hat kein sec2/sec6 — nichts gelegt");
            return 0;
        }

        // Die Grundflaeche der Mine, aus dem Kachelsatz. Fehlt sie, wird nur die
        // Ankerzelle geprueft — dann sagt die Meldung das auch, statt eine
        // Genauigkeit vorzuspiegeln, die nicht da ist.
        var foot = new List<(int Dx, int Dy)>();
        if (cwp != null && cwp.HasBuildings)
        {
            var bt = cwp.GetBuildingType(TypeFieldMine);
            if (!bt.IsEmpty)
                for (int dx = 0; dx < CwpFile.PatternWidth; dx++)
                    for (int dy = 0; dy < CwpFile.PatternHeight; dy++)
                        if (cwp.PatternTile(bt.FirstPattern, dx, dy) != 0) foot.Add((dx, dy));
        }
        if (foot.Count == 0) foot.Add((0, 0));

        int w = m.Width, h = m.Height;
        int walkable = 0;
        for (int c = 0; c < w; c++)
            for (int r = 0; r < h; r++)
                // ⚠ OHNE die Hangbyte-Frage: die Messlatte 0,23 je 1000 ist gegen
                // »imap frei« gemessen (terra_stats.py), nicht gegen »baubar«.
                // Mit einer anderen Grundmenge wäre der Vergleich keiner.
                if (ImapFree(m, imap, c, r)) walkable++;

        int want = (int)Math.Round(PerThousandWalkable * walkable / 1000.0);
        want = Math.Clamp(want, Fewest, Most);

        // Rand und Abstand schrumpfen mit der Karte mit: auf 254x254 sind es die
        // gemessenen 5 und 11, auf einer kleinen Karte wären sie unerfüllbar und
        // es käme gar kein Vorkommen heraus — was schlimmer ist als ein enger
        // Abstand. Die Meldung nennt die benutzten Werte, damit nicht heimlich
        // gegen eine andere Messlatte gelegt wird.
        int edge = Math.Min(EdgeMin, Math.Max(0, (Math.Min(w, h) - 4) / 4));
        int spacing = Math.Min(SpacingMin, Math.Max(2, Math.Min(w, h) / 4));

        // Die Bewerber, in einer aus dem Samen gewürfelten Reihenfolge. Greedy
        // mit Mindestabstand ergibt die gemessene Mischung von weit gestreut und
        // ein paar nahen Paaren (im Original 24 % unter 20 Zellen) von selbst —
        // eine feste Rasterung ergäbe sie nicht.
        var cand = new List<(uint Key, int Col, int Row, int Anchors)>();
        for (int c = edge; c < w - edge; c++)
            for (int r = edge; r < h - edge; r++)
            {
                if (!Free(m, imap, c, r)) continue;
                if (!Corners(zone, w, h, c, r)) continue;
                int a = Anchors(m, imap, zone, c, r, foot);
                if (a < AnchorsMin) continue;
                cand.Add((MapTerrain.Hash(c, r, seed ^ 0x7E88Au), c, r, a));
            }
        cand.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key)
                          : x.Col != y.Col ? x.Col.CompareTo(y.Col)
                          : x.Row.CompareTo(y.Row));

        long sq = (long)spacing * spacing;
        int anchorSum = 0, anchorMin = 9;
        foreach (var k in cand)
        {
            if (m.Terra.Count >= want) break;
            bool tooClose = false;
            foreach (var (dc, dr, _) in m.Terra)
            {
                long dx = k.Col - dc, dy = k.Row - dr;
                if (dx * dx + dy * dy < sq) { tooClose = true; break; }
            }
            if (tooClose) continue;
            m.Terra.Add((k.Col, k.Row, Amount));
            anchorSum += k.Anchors;
            anchorMin = Math.Min(anchorMin, k.Anchors);
        }

        int n = m.Terra.Count;
        say($"Vorkommen (UNSERE ZUTAT — im Original legt sie das Missionsskript, " +
            $"add_terra_place C:0x4D0A10): {n} von {want} gewuenscht, {Amount} Einheiten " +
            $"je Stueck; {walkable} begehbare Zellen ergeben {1000.0 * n / Math.Max(1, walkable):0.00} " +
            $"je 1000 (Messlatte 0,23; Original 0,16..0,37)");
        say($"Vorkommen: {cand.Count} Bewerber, Rand >= {edge} (Messlatte 5), " +
            $"Abstand >= {spacing} (Messlatte 11), Grundflaeche der Mine {foot.Count} Zellen" +
            (n > 0 ? $", tragende Anker Mittel {anchorSum / (double)n:0.00} von 9, min {anchorMin} " +
                     "(Messlatte map_23 mit derselben Grundflaeche: 57 von 99, also 5,2 von 9)" : ""));
        if (n < want)
            say($"Vorkommen: ⚠ {want - n} weniger als gewuenscht — das Gelaende gibt " +
                "keine weiteren Stellen her, die Rand UND Abstand UND Anker erfuellen");
        return n;
    }

    /// <summary>
    /// Die beiden ZELLENfragen von <c>can_build_here</c> @0x4203C0, die ohne das
    /// Zonenraster gehen: die imap muss <b>frei</b> sein (0xFFFE, der Wert, den
    /// <c>Can_go</c> @0x4055D0 als frei liest; Index <c>col*256 + row</c>, u16)
    /// und das <b>Hangbyte</b> (sec1 +3) muss 0 sein.
    ///
    /// <para>⚠ Das Hangbyte fehlte hier am 13.08.2026 und war die Hälfte des
    /// Fehlschlags: <c>--terra-check</c> auf <c>map_23</c> nennt es selbst als
    /// Grund (»Hangbyte 1 x3, Hangbyte 2 x2« von 42 durchgefallenen Ankern), und
    /// der Generator setzt es auf jeder Hangzelle. Ohne diese Frage lagen
    /// Vorkommen auf Hängen, und die Mine hatte trotz Vorkommen 0
    /// Bauplätze.</para></summary>
    internal static bool Free(CwmFile m, byte[] imap, int c, int r)
        => ImapFree(m, imap, c, r) && m.FlagAt(c, r) == 0;

    /// <summary>Nur die imap-Frage, ohne das Hangbyte — die Grundmenge, gegen die
    /// die Dichte gemessen ist.</summary>
    internal static bool ImapFree(CwmFile m, byte[] imap, int c, int r)
    {
        if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) return false;
        int i = (c * MapFactory.ImapStride + r) * 2;
        if (i + 1 >= imap.Length) return false;
        return (imap[i] | (imap[i + 1] << 8)) == MapFactory.ImapFree;
    }

    /// <summary>sec2 einer Zelle, Index <c>col*257 + row</c>.</summary>
    internal static int Zone(byte[] zone, int w, int h, int c, int r)
    {
        if (c < 0 || c >= w || r < 0 || r >= h) return 0;
        int i = c * MapFactory.ZoneStride + r;
        return i < zone.Length ? zone[i] : 0;
    }

    /// <summary><c>corners_carry</c> @0x4211A0 — alle vier Ecken sec2 &gt;= 2.
    /// </summary>
    internal static bool Corners(byte[] zone, int w, int h, int c, int r)
        => Zone(zone, w, h, c, r) >= MinCornerClass
        && Zone(zone, w, h, c + 1, r) >= MinCornerClass
        && Zone(zone, w, h, c, r + 1) >= MinCornerClass
        && Zone(zone, w, h, c + 1, r + 1) >= MinCornerClass;

    /// <summary>Wieviele der neun Anker im 3x3-Fenster eines Vorkommens
    /// (<c>CellOnDeposit</c> @0x4205C0) eine Mine TRAGEN — geprüft wird die ganze
    /// Grundfläche <paramref name="foot"/>, Zelle für Zelle, wie
    /// <c>can_build_here</c> @0x4203C0 sie prüft.</summary>
    private static int Anchors(CwmFile m, byte[] imap, byte[] zone, int c, int r,
                              List<(int Dx, int Dy)> foot)
    {
        int n = 0;
        for (int dy = 0; dy < 3; dy++)
            for (int dx = 0; dx < 3; dx++)
            {
                bool ok = true;
                foreach (var (fx, fy) in foot)
                    if (!Free(m, imap, c + dx + fx, r + dy + fy) ||
                        !Corners(zone, m.Width, m.Height, c + dx + fx, r + dy + fy)) { ok = false; break; }
                if (ok) n++;
            }
        return n;
    }
}
