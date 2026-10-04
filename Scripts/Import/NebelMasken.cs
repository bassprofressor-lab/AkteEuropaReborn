namespace AkteEuropaReborn.Import;

using System;
using System.IO;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE 285 NEBELMASKEN DES ORIGINALS</b> (04.10.2026, bug-427, Lesung
/// <c>berichte/nebel-explosionen-fable.md</c> §1.3).
///
/// <para><b>Woher.</b> Der Nebeldurchgang <c>0x4B472A..0x4B485E</c> holt die
/// Kachel einer Saumzelle und einer geneigten Nebelzelle ueber <c>0x41FC60</c>
/// aus der Tafel <c>0xBAC72C</c> — und <c>0xBAC72C = 0xBAA800 + 4·19·15·7</c>
/// ist die <b>Familie 7</b> der Variantentafel der Bodensynthese, die
/// <c>0x4C8DAD</c> (<c>fread 0x23A0</c>) aus dem Aux-Block ab Offset
/// <c>0x0c</c> JEDER <c>.CWP</c> liest. <see cref="CwpFile.Bodenvariante"/>
/// liest denselben Block schon. Index = <c>19·(15·7 + B) + Hangart</c>, mit
/// B = Platz des Eckenmusters in der 16er-Tafel <c>0x4F89F8</c>.</para>
///
/// <para><b>Was es ist</b> (selbst ausgezaehlt an <c>user://data/DATA</c>,
/// 23 Kachelsaetze): 285 belegte Saetze = 15 Muster × 19 Hangformen, je
/// GENAU EINE Kachel; jede ist eine 50-%-Schachbrettmaske in Palettenfarbe 47
/// (dazu 6 Punkte Farbe 1 in drei Masken der Hangform 18 — Datenrest des
/// Originals, wird mitgezeichnet). Die flache Vollmaske B=0/C=0 ist Kachel
/// 1381, 40×20 bei yoff 50, Zeile 0 ab x=1 — dasselbe Schachbrett, das
/// <c>0x4AC990</c> fuer eine flache Nebelzelle malt.</para>
///
/// <para><b>Die Eckenlage — gemessen, nicht geraten:</b> an den 15 flachen
/// Masken wurde je Viertel der Zelle gezaehlt, wo die Maske FEHLT (= helle
/// Ecke). Ergebnis: die Ziffern des Eckenmusters stehen fuer
/// <c>1000·OL + 100·OR + 10·UL + 1·UR</c> (B1 »0101« frei rechts, B2 »0011«
/// frei unten, B5 »0001« frei unten rechts, B13 »0110« frei OR+UL …, alle 14
/// Muster ausser 0 stimmen). In Bits von <c>FogGrid.CornerAt</c>
/// (Bit0 OL, Bit1 OR, Bit2 UL, Bit3 UR) ist das <see cref="MusterAusEcken"/>.
/// </para>
///
/// <para><b>Wie sie zum Spieler kommen.</b> Der Import schreibt sie je
/// Kachelsatz neben die Gebaeudemuster (<c>Buildings/tileset_NN_nebel.png</c>,
/// <see cref="ContentBuilder.ExportBuildingPatterns"/> — derselbe Weg fuer
/// vollen Import, <c>--reexport-buildings</c> und Karteneditor). Fehlt die
/// Datei (Import aelter als dieser Bau), baut <see cref="Laden"/> sie zur
/// Laufzeit aus <c>user://data/DATA/NN.CWP</c> — die kopiert der normale Import
/// seit dem 13.08.2026 mit (<c>CopyTilesets</c>). Erst wenn auch die fehlt,
/// bleibt die alte Rampe (<c>--nebel-rampe-alt</c>).</para>
/// </summary>
public static class NebelMasken
{
    public const int Muster = 15, Hangformen = 19, Familie = 7;

    /// <summary>Eine Zelle im Atlas: 40 breit, 70 hoch. Die Maske liegt darin
    /// mit ihrem <c>yoff</c> — gezeichnet wird die ganze Zelle an
    /// <c>(sx, sy − 50)</c>, genau wie der Blit <c>0x4ACCD0(…, sx, sy − 50)</c>
    /// @0x4B47F6. Hoechste Maske: Hangform 15/16, yoff 20 + 50 Zeilen = 70.</summary>
    public const int ZelleB = 40, ZelleH = 70, Hub = 50;

    public const int AtlasB = Hangformen * ZelleB, AtlasH = Muster * ZelleH;

    /// <summary>Die 16er-Tafel <c>0x4F89F8</c> (dieselbe wie
    /// <c>MapBaker.Eckenmuster</c>).</summary>
    private static readonly int[] Eckenmuster =
        { 0, 101, 11, 1010, 1100, 1, 10, 1000, 100, 111, 1011, 1110, 1101, 110, 1001, 1111 };

    /// <summary>Muster-Index B aus den vier Eckbits von <c>FogGrid.CornerAt</c>
    /// (1 = hell). 15 heisst »alle vier hell« — das liegt hinter dem Tafelende
    /// (Index 2280 von 2280), siehe Aufrufer.</summary>
    public static int MusterAusEcken(int bits)
    {
        int ziffern = ((bits & 1) != 0 ? 1000 : 0) + ((bits & 2) != 0 ? 100 : 0)
                    + ((bits & 4) != 0 ? 10 : 0) + ((bits & 8) != 0 ? 1 : 0);
        return Array.IndexOf(Eckenmuster, ziffern);
    }

    /// <summary>Die Masken eines Kachelsatzes als Bild (Farben aus der
    /// Palette) und als Deckmaske je Atlasbildpunkt. <paramref name="gefunden"/>
    /// = belegte Saetze (Soll 285), <paramref name="beschnitten"/> = Punkte, die
    /// nicht in die 40×70-Zelle passten (Soll 0).</summary>
    public static Image? Bauen(CwpFile cwp, PalFile pal, out int gefunden, out int beschnitten)
    {
        gefunden = 0; beschnitten = 0;
        var img = Image.CreateEmpty(AtlasB, AtlasH, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int b = 0; b < Muster; b++)
            for (int c = 0; c < Hangformen; c++)
            {
                var (basis, anzahl) = cwp.Bodenvariante(Hangformen * (Muster * Familie + b) + c);
                if (anzahl <= 0) continue;
                CwpFile.Frame f;
                try { f = cwp.DecodeFrame(basis); }
                catch (Exception) { continue; }
                if (f.IsEmpty) continue;
                gefunden++;
                int x0 = c * ZelleB, y0 = b * ZelleH + f.YOffset;
                for (int y = 0; y < f.Height; y++)
                    for (int x = 0; x < f.Width; x++)
                    {
                        int o = y * f.Width + x;
                        if (!f.Opaque[o]) continue;
                        if (x >= ZelleB || f.YOffset + y >= ZelleH) { beschnitten++; continue; }
                        byte i = f.Pixels[o];
                        img.SetPixel(x0 + x, y0 + y, Color.Color8(pal.R[i], pal.G[i], pal.B[i], 255));
                    }
            }
        return gefunden > 0 ? img : null;
    }

    public static string Datei(int tileset) => $"tileset_{tileset:00}_nebel.png";

    /// <summary>Beim Import: <c>&lt;dir&gt;/tileset_NN_nebel.png</c>.</summary>
    public static int Export(CwpFile cwp, PalFile pal, int tileset, string dir)
    {
        try
        {
            var img = Bauen(cwp, pal, out int n, out _);
            if (img == null) return 0;
            Directory.CreateDirectory(dir);
            img.SavePng($"{dir}/{Datei(tileset)}");
            return n;
        }
        catch (Exception e) { GD.PrintErr($"Nebelmasken Kachelsatz {tileset:00}: {e.Message}"); return 0; }
    }

    /// <summary>Woher die Masken dieses Laufs kamen — fuer die Pruefzeile.</summary>
    public static string Herkunft = "nicht geladen";

    /// <summary>
    /// Zur Laufzeit: erst die vom Import geschriebene Datei, dann der
    /// Kachelsatz selbst. Null = keine Masken (dann bleibt die Rampe).
    /// </summary>
    public static Image? Laden(int tileset)
    {
        string p = Core.Content.Path($"Buildings/{Datei(tileset)}");
        if (Godot.FileAccess.FileExists(p))
        {
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(p));
            if (img != null && img.GetWidth() == AtlasB && img.GetHeight() == AtlasH)
            {
                if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
                Herkunft = $"Import ({p})";
                return img;
            }
        }
        string cwp = Core.Content.Path($"DATA/{tileset:00}.CWP");
        string pal = Core.Content.Path($"DATA/{tileset:00}.PAL");
        if (!Godot.FileAccess.FileExists(cwp) || !Godot.FileAccess.FileExists(pal))
        {
            Herkunft = $"FEHLT — weder {Datei(tileset)} noch DATA/{tileset:00}.CWP";
            return null;
        }
        try
        {
            var img = Bauen(CwpFile.FromBytes(Godot.FileAccess.GetFileAsBytes(cwp)),
                            PalFile.FromBytes(Godot.FileAccess.GetFileAsBytes(pal)), out int n, out _);
            Herkunft = img != null ? $"zur Laufzeit aus DATA/{tileset:00}.CWP ({n} Masken)"
                                   : $"DATA/{tileset:00}.CWP ohne Nebelmasken";
            return img;
        }
        catch (Exception e)
        {
            Herkunft = $"DATA/{tileset:00}.CWP nicht lesbar: {e.Message}";
            return null;
        }
    }

    /// <summary>Dieselben Masken als Bitfeld (true = Maskenpunkt), damit der
    /// Pruefstand ohne GPU zusammensetzen kann, was gezeichnet wird.</summary>
    public static bool[] Deckung(Image atlas)
    {
        var d = new bool[AtlasB * AtlasH];
        var raw = atlas.GetData();
        for (int i = 0; i < d.Length; i++) d[i] = raw[i * 4 + 3] != 0;
        return d;
    }
}
