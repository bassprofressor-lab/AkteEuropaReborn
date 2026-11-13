namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// ⭐ 13.11.2026 — D9: BRUECKE UND RAMPE WEGNEHMEN (Rechtsklick mit Werkzeug 0, oder
/// »7 Wegnehmen«). Erkannt wird das Bauwerk an der Lage (sec20): 100…199 Bruecke Nr. Lage−100,
/// 200…249 Rampe Nr. Lage−200 — dieselbe Zuordnung, die das Spiel benutzt (CwmFile, 195/195).
///
/// <para><b>Was zurueckkommt</b> — UNSERE SETZUNG, denn das Original kennt nur den ABRISS
/// (<c>0x4CB0A0</c>/<c>0x4CBAB0</c>: Truemmerkachel, die Zelle bleibt Bauwerk) und keinen Editor:
/// die Zellen bekommen ihre ROLLE zurueck — bei der Bruecke die Satzpositionen 1…k Wasser
/// (auf der Hoehe des Wasserspiegels der Umgebung), die Koepfe-Reihen 0 und k+1 Land; die
/// Rampe Land. Dann zieht der Gelaendepinsel (<see cref="MapEditTerrain.PaintClass"/>, Eckweg)
/// die Kacheln nach, wie beim Malen. Der Satz (sec17 bzw. sec21) wird geleert, die Lage 0.
/// Vorher war die Bruecke beim »Wegnehmen« ein GEGENSTAND: die Kachel ging, der sec17-Satz
/// blieb verwaist stehen.</para>
/// </summary>
public static class MapBauwerkAbriss
{
    /// <summary>Was an dieser Zelle steht: 1 Bruecke, 2 Rampe, 0 nichts.</summary>
    public static int ArtAn(CwmFile m, int col, int row, out int slot)
    {
        slot = -1;
        var l20 = m.Sec(20);
        if (l20 == null || col < 0 || row < 0 || col >= m.Width || row >= m.Height) return 0;
        int l = l20[col * MapFactory.ImapStride + row];
        if (l is >= 100 and < 200) { slot = l - 100; return 1; }
        if (l is >= 200 and < 250) { slot = l - 200; return 2; }
        return 0;
    }

    /// <summary>Wegnehmen. Rueckgabe: Text fuer die Statuszeile, oder null wenn dort nichts steht.</summary>
    public static string? Wegnehmen(CwmFile m, int col, int row, TileModel? model, TilePalette? pal, uint seed, TileSeams? seams)
    {
        int art = ArtAn(m, col, row, out int slot);
        if (art == 0) return null;
        var l20 = m.Sec(20)!; var imap = m.Sec(6)!;
        var zellen = new List<(int C, int R, bool Wasser)>();
        // ⭐ Die alte KLASSE steht noch in sec2, dem ECKraster (Schrittweite 257, die Bruecke
        // schreibt dort nichts). GEMESSEN an der Probekarte (Samen 11, 22 500 Zellen): alle vier
        // Ecken 0 <=> Wasser (3747 von 3747, keine andere Zelle); eine Ecke 0 => rau (650 von
        // 650 Zellen mit Ecke 0 und Land sind rau); sonst frei (14 078 frei, aber 1617 raue im
        // Binnenland wuerden frei — UNSERE SETZUNG, unter Bruecke und Rampe liegen Uferzellen).
        // ⚠ Die Bruecken-GEOMETRIE taugt dafuer nicht: an der Probebruecke lag nur der
        // Mittelstreifen ueber Wasser, ein Gelaender auf rauem Land.
        var zone = m.Sec(2);
        int Ecke(int c, int r) => zone != null && c * MapFactory.ZoneStride + r < zone.Length ? zone[c * MapFactory.ZoneStride + r] : MapFactory.ZoneLand;
        MapFactory.Ground KlasseVorher(int c, int r)
        {
            int mn = Math.Min(Math.Min(Ecke(c, r), Ecke(c + 1, r)), Math.Min(Ecke(c, r + 1), Ecke(c + 1, r + 1)));
            int mx = Math.Max(Math.Max(Ecke(c, r), Ecke(c + 1, r)), Math.Max(Ecke(c, r + 1), Ecke(c + 1, r + 1)));
            return mx == 0 ? MapFactory.Ground.Water : mn == 0 ? MapFactory.Ground.Rough : MapFactory.Ground.Free;
        }
        if (art == 1)
        {
            CwmExtra.Bridge? b = null;
            foreach (var x in CwmExtra.Bridges(m)) if (x.Slot == slot) b = x;
            if (b == null)
            {
                // Lage ohne Satz: nur die Lagen dieser Nummer loeschen
                for (int c = 0; c < m.Width; c++)
                    for (int r = 0; r < m.Height; r++)
                        if (l20[c * MapFactory.ImapStride + r] == 100 + slot) l20[c * MapFactory.ImapStride + r] = 0;
                return $"Brückenlage {100 + slot} ohne Satz gelöscht";
            }
            foreach (var (c, r, _, _) in BrueckenMuster.Zellen(b))
                zellen.Add((c, r, KlasseVorher(c, r) == MapFactory.Ground.Water));
            var s17 = m.Sec(17)!;
            Array.Clear(s17, slot * CwmExtra.BridgeStride, CwmExtra.BridgeStride);
        }
        else
        {
            zellen.Add((col, row, KlasseVorher(col, row) == MapFactory.Ground.Water));
            var s21 = m.Sec(21)!;
            int o = slot * CwmExtra.RampStride;
            s21[o] = 0; s21[o + 1] = 0; s21[o + 2] = CwmExtra.RampEmpty; s21[o + 3] = 0;
        }
        // 1. die Bauwerkszellen frei machen: Kachel aus dem Objektband, Lage 0, Byte +3 0,
        //    Wasser auf den Wasserspiegel, Klasse vorlaeufig gesperrt (sonst steigt PaintClass aus)
        int landH = m.ElevAt(zellen[0].C, zellen[0].R);
        foreach (var (c, r, w) in zellen)
        {
            if (!w) { landH = m.ElevAt(c, r); break; }
        }
        foreach (var (c, r, w) in zellen)
        {
            int h = w ? MapEditTerrain.WasserSpiegel(m, c, r) : landH;
            m.SetCell(c, r, w ? 0 : NachbarBoden(m, c, r), h, 0);
            l20[c * MapFactory.ImapStride + r] = 0;
            int i = (c * MapFactory.ImapStride + r) * 2;
            imap[i] = 0xFF; imap[i + 1] = 0xFF;
        }
        // 2. die Rolle zurueck und nachkacheln (Eckweg wie beim Malen)
        int nass = 0;
        foreach (var (c, r, w) in zellen)
        {
            MapEditTerrain.PaintClass(m, model, pal, c, r, KlasseVorher(c, r), seed, seams);
            if (w) nass++;
        }
        return art == 1 ? $"Brücke Platz {slot} weggenommen — {nass} Zellen wieder Wasser, {zellen.Count - nass} Land"
                        : $"Rampe Platz {slot} weggenommen — die Zelle ist wieder Ufer";
    }

    /// <summary>Ein Bodencode aus der Nachbarschaft (Landzelle ohne Objekt) als Platzhalter,
    /// bis der Pinsel nachkachelt.</summary>
    private static int NachbarBoden(CwmFile m, int col, int row)
    {
        for (int rad = 1; rad <= 3; rad++)
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    int c = col + dx, r = row + dy;
                    if (c < 0 || r < 0 || c >= m.Width || r >= m.Height) continue;
                    int code = m.CodeAt(c, r);
                    if (code >= 8 && code < CwpFile.ObjectCodeBase && MapEditTerrain.ClassAt(m, c, r) is 0 or 1) return code;
                }
        return 8;
    }
}

public partial class MapEditOverlay
{
    /// <summary>Rechtsklick mit Werkzeug 0 / »Wegnehmen« auf einer Bruecke oder Rampe.</summary>
    private bool BauwerkWegnehmen(Vector2I cell)
    {
        string? t = MapBauwerkAbriss.Wegnehmen(_map, cell.X, cell.Y, MapEditSession.Tiles, MapEditSession.Palette,
                                              MapEditSession.Seed, MapEditSession.Seams);
        if (t == null) return false;
        _mine.Add((cell.X, cell.Y, Brush.Bruecke));
        MapEditSession.Dirty = true;
        _say = t;
        return true;
    }
}
