namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// Stufe 1 des Karteneditors: aus nichts als Breite, Hoehe und Kachelsatz eine
/// Karte, die die Engine wirklich laedt und auf der man wirklich bauen kann.
///
/// <para><b>Das ist ein PRUEFSTAND, keine Oberflaeche.</b> Der Sinn ist der
/// Weg — <see cref="MapFactory"/> → <see cref="ContentBuilder.ExportMap"/> →
/// <c>user://data/Maps/&lt;name&gt;.{png,json,entities.json}</c> — und nicht
/// das Aussehen der erzeugten Landschaft. Eine Maus-Oberflaeche kann spaeter
/// dieselben zwei Aufrufe machen.</para>
///
/// <para><b>Die Form ist UNSERE Setzung, ganz und gar.</b> Aus den gelieferten
/// Karten laesst sich ablesen, WELCHE Codes Wasser und welche Boden sind (siehe
/// <see cref="TilePalette"/>), aber nicht, wie eine Karte auszusehen hat. Was
/// hier steht — Wasserrand, Strandring, Hochebene, See — ist gewaehlt, und zwar
/// nach genau einer Bedingung, die gemessen ist:</para>
///
/// <para>⚠ <b>Eine flache Karte auf Hoehe 0 hat NULL Bauplaetze.</b>
/// <c>can_build_here</c> @0x4203C0 verlangt je Musterzelle Hangbyte 0 <b>und
/// alle vier Eckhoehen ≥ 2</b> (@0x420360, vier <c>cmp … 2 / jae</c>). Ueber die
/// 44 eingespielten Karten liegen 447.314 von 1.282.512 Zellen auf Hoehe ≥ 2,
/// also 34,88 % — die gelieferten Karten erfuellen die Bedingung auf gut einem
/// Drittel ihrer Flaeche. Diese hier setzt das Landesinnere darum auf
/// <see cref="LandElev"/> = 2.</para>
///
/// <para>Die zweite gemessene Schranke ist <c>NavGrid.MaxClimb</c> = 3: ein
/// Hoehensprung von 3 oder mehr ist fuer Bodeneinheiten nicht mehr begehbar.
/// Alle Stufen hier sind darum 1 hoch, und der Berg bekommt einen Absatz.</para>
/// </summary>
public static class MapGenerator
{
    // ---- UNSERE Setzungen, alle vier ---------------------------------------

    /// <summary>Wie breit der Wasserrand ist.</summary>
    public const int WaterMargin = 3;

    /// <summary>Die Hoehe des Landesinneren. 2 ist keine Wahl, sondern die
    /// Untergrenze aus <c>can_build_here</c> — siehe Klassenkommentar.</summary>
    public const int LandElev = 2;

    /// <summary>Die Hoehe des Strandrings: eine Stufe unter dem Land, damit der
    /// Uebergang zum Wasser nicht senkrecht ist.</summary>
    public const int ShoreElev = 1;

    /// <summary>Die Kuppe des Bergs. Zwei Stufen ueber dem Land, mit einem
    /// Absatz auf 3 — jeder Einzelschritt bleibt unter MaxClimb.</summary>
    public const int HillElev = 4;

    /// <summary>
    /// Baut die Karte. Der Kachelsatz wird gebraucht, weil erst er sagt, welche
    /// Codes ueberhaupt ganze Bodenzellen sind.
    /// </summary>
    public static CwmFile Build(string stem, int width, int height, int tileset,
                                TilePalette pal, bool flat, Action<string> say)
    {
        var m = MapFactory.Empty(width, height, tileset, stem);
        m.Mission = $"Editor {stem} {width}x{height}";

        int[] land = pal.Ground;
        int[] water = TilePalette.Water;

        // Ein Berg im linken oberen Viertel und ein See im rechten unteren —
        // beide gross genug, dass sie im Bild zu sehen sind, und beide weit
        // genug von den Startmarken weg.
        var hill = Box(width / 6, height / 6, Math.Max(4, width / 5), Math.Max(4, height / 8));
        var lake = Box(width - width / 3, height - height / 3,
                       Math.Max(3, width / 6), Math.Max(3, height / 8));

        for (int r = 0; r < height; r++)
            for (int c = 0; c < width; c++)
            {
                int distEdge = Math.Min(Math.Min(c, r), Math.Min(width - 1 - c, height - 1 - r));
                bool isSea = distEdge < WaterMargin;
                bool isLake = !flat && Inside(lake, c, r);
                bool isShore = !isSea && !isLake &&
                               (distEdge == WaterMargin || Touches(lake, c, r, !flat));

                int elev, code;
                MapFactory.Ground g;
                if (isSea || isLake)
                {
                    // Die acht Wasserkacheln gestreut, wie die gelieferten
                    // Karten sie streuen (34.022..34.540 Vorkommen je Code).
                    code = water[Scatter(c, r) % water.Length];
                    elev = 0;
                    g = MapFactory.Ground.Water;
                }
                else
                {
                    code = land[Scatter(c, r) % land.Length];
                    g = MapFactory.Ground.Free;
                    elev = flat ? 0
                         : isShore ? ShoreElev
                         : Inside(hill, c, r) ? HillElev
                         : Touches(hill, c, r, true) ? HillElev - 1
                         : LandElev;
                }
                MapFactory.Paint(m, c, r, code, elev, g);
            }

        // Startmarken: Typ 0x70 + Spieler (CwmData.Markers). Zwei Spieler, in
        // gegenueberliegenden Ecken des Landesinneren.
        int inset = WaterMargin + 3;
        MapFactory.PutMarker(m, 0, inset, height - 1 - inset, MapFactory.MarkerStartBase + 0);
        MapFactory.PutMarker(m, 1, width - 1 - inset, inset, MapFactory.MarkerStartBase + 1);

        say($"erzeugt: {width}x{height}, {pal.Describe()}");
        say(flat ? "FLACH (Gegenprobe: alles auf Hoehe 0)"
                 : $"Hoehen: Wasser 0, Strand {ShoreElev}, Land {LandElev}, Berg {HillElev}");
        return m;
    }

    // ---- Schreiben ----------------------------------------------------------

    /// <summary>
    /// Die Karte dorthin schreiben, wo die Engine sie findet:
    /// <c>user://data/Maps/map_&lt;name&gt;.*</c>. Genau der Weg, den auch der
    /// Import nimmt — <see cref="ContentBuilder.ExportMap"/>.
    /// </summary>
    public static string Write(CwmFile m, CwpFile cwp, PalFile pal, string outName, Action<string> say)
    {
        string maps = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/Maps";
        var img = ContentBuilder.ExportMap(m, cwp, pal, maps, outName, out var baker, out var doc);
        say($"{outName}: Bild {img.GetWidth()}x{img.GetHeight()} px " +
            $"(erwartet {baker.PixelW}x{baker.PixelH}), Ursprung y={baker.OriginY}");
        var t = doc.Terrain;
        say($"{outName}: Gelaende frei {t.Histogram[0]} / rau {t.Histogram[1]} / " +
            $"wasser {t.Histogram[2]} / gesperrt {t.Histogram[3]}, {t.Inferred} abgeleitet");
        say($"{outName}: {doc.Entities.Count} Einheiten, {doc.Buildings.Count} Gebaeude, " +
            $"{doc.Markers.Count} Marken, {doc.RailCells.Count} Gleiszellen " +
            "(alle vier muessen bei einer leeren Karte stimmen, siehe MapFactory)");
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

    // ---- Kleinkram ----------------------------------------------------------

    private readonly record struct Rect(int X0, int Y0, int X1, int Y1);

    private static Rect Box(int x, int y, int w, int h) => new(x, y, x + w - 1, y + h - 1);

    private static bool Inside(Rect b, int c, int r)
        => c >= b.X0 && c <= b.X1 && r >= b.Y0 && r <= b.Y1;

    /// <summary>Der Ring UM einen Kasten — der Absatz des Bergs, das Ufer des
    /// Sees.</summary>
    private static bool Touches(Rect b, int c, int r, bool on)
        => on && !Inside(b, c, r) &&
           c >= b.X0 - 1 && c <= b.X1 + 1 && r >= b.Y0 - 1 && r <= b.Y1 + 1;

    /// <summary>Streut die acht Varianten eines Blocks. Fest verdrahtet statt
    /// gewuerfelt, damit zwei Laeufe dieselbe Karte ergeben und ein Pruefstand
    /// eine Aenderung ueberhaupt sehen kann.</summary>
    private static int Scatter(int c, int r) => (c * 7 + r * 5 + ((c * r) & 3)) & 0x7FFFFFFF;
}
