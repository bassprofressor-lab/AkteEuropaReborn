namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

/// <summary>
/// The last of the four extractors: the game's own look. Its typeface, its side
/// panel and its effects, all three out of the cabinet the discs carry them in.
///
///   UI/akte_font.png + .fnt   FONT.CWD as a BMFont Godot loads as a real Font
///   UI/panel.png              PANEL.DTA, 204 x 170
///   Effects/&lt;name&gt;/fNN.png    hand-picked ANIM.CWA sequences
///
/// Ported from font_export.py, ui_export.py and export_effects.py.
/// </summary>
public sealed class InterfaceExporter
{
    private readonly PalFile _pal;
    private readonly string _ui, _fx;

    public int Glyphs, PanelPixels, Effects, EffectFrames;

    public InterfaceExporter(PalFile pal, string uiDir, string effectsDir)
    {
        _pal = pal;
        _ui = uiDir.TrimEnd('/', '\\');
        _fx = effectsDir.TrimEnd('/', '\\');
    }

    // ---- FONT.CWD -----------------------------------------------------------

    public const int GlyphRecord = 131, CellHeight = 13, GlyphCount = 160, AtlasCols = 16;
    public const string FontName = "akte_font";

    /// <summary>The second typeface, FONT2.CWD — same 160-glyph layout, thinner
    /// letters. It belongs to the briefing screen: the loader @0x45bddc reads it
    /// immediately before BRIEFG.DAT, which is that screen's backdrop.</summary>
    public const string Font2Name = "akte_font2";

    /// <summary>How many typefaces were written.</summary>
    public int Fonts;

    /// <summary>FONT.CWD: 160 records of 131 bytes, `[u8 width][width x 13]`,
    /// and the character is the record index plus 0x20 (glyph blitter
    /// @0x4ba2b0, which multiplies the index by 131).
    ///
    /// The cell is a MASK, not a picture: the engine puts the current text
    /// colour wherever it finds 0xFE. So the atlas stores text pixels WHITE,
    /// which a Godot `font_color` then modulates, and shadow pixels (0x24)
    /// BLACK, which survives the modulation and keeps the drop shadow dark
    /// whatever colour the text is. 0xFF is transparent, anything else is a
    /// literal palette index and is written as that colour.</summary>
    public void WriteFont(byte[] font, string name = FontName, string source = "FONT.CWD")
    {
        Directory.CreateDirectory(_ui);
        int maxW = 1;
        for (int i = 0; i < GlyphCount; i++)
        {
            int b = i * GlyphRecord;
            if (b < font.Length) maxW = Math.Max(maxW, font[b]);
        }
        int rows = (GlyphCount + AtlasCols - 1) / AtlasCols;
        var atlas = Image.CreateEmpty(AtlasCols * maxW, rows * CellHeight, false, Image.Format.Rgba8);
        atlas.Fill(new Color(0, 0, 0, 0));

        var chars = new StringBuilder();
        int count = 0;
        for (int i = 0; i < GlyphCount; i++)
        {
            int b = i * GlyphRecord;
            if (b + GlyphRecord > font.Length) break;
            int w = font[b];
            int gx = i % AtlasCols * maxW, gy = i / AtlasCols * CellHeight;
            for (int y = 0; y < CellHeight; y++)
                for (int x = 0; x < w; x++)
                {
                    // the cell is 130 bytes, so a glyph holds at most 130
                    // pixels: at width 11 or more the last row does not fit and
                    // is simply not there. Reading on would take the bytes of
                    // the NEXT glyph — six coloured specks in the atlas, which
                    // is how this was caught.
                    int k = y * w + x;
                    if (k >= GlyphRecord - 1 || b + 1 + k >= font.Length) continue;
                    byte v = font[b + 1 + k];
                    if (v == 0xFF) continue;
                    Color c = v switch
                    {
                        0xFE => new Color(1, 1, 1, 1),                 // text, modulated
                        0x24 => new Color(0, 0, 0, 220f / 255f),       // shadow, stays dark
                        _ => Color.Color8(_pal.R[v], _pal.G[v], _pal.B[v], 255),
                    };
                    atlas.SetPixel(gx + x, gy + y, c);
                }
            int code = i + 0x20;
            int uni = code >= 0x80 ? Cp437.Char((byte)code) : code;
            chars.Append($"char id={uni} x={gx} y={gy} width={w} height={CellHeight} ");
            chars.Append($"xoffset=0 yoffset=0 xadvance={w + 1} page=0 chnl=15\n");
            count++;
            Glyphs++;
        }
        atlas.SavePng($"{_ui}/{name}.png");

        var fnt = new StringBuilder();
        fnt.Append($"info face=\"Akte Europa\" size={CellHeight} bold=0 italic=0 charset=\"\" ");
        fnt.Append("unicode=1 stretchH=100 smooth=0 aa=1 padding=0,0,0,0 spacing=0,0\n");
        fnt.Append($"common lineHeight={CellHeight + 2} base={CellHeight - 2} ");
        fnt.Append($"scaleW={atlas.GetWidth()} scaleH={atlas.GetHeight()} pages=1 packed=0\n");
        fnt.Append($"page id=0 file=\"{name}.png\"\n");
        fnt.Append($"chars count={count}\n");
        fnt.Append(chars);
        File.WriteAllText($"{_ui}/{name}.fnt", fnt.ToString(), new UTF8Encoding(false));

        var idx = new StringBuilder();
        idx.Append($"{{\"source\":\"{source}\",\"glyphs\":{count},\"cell_h\":{CellHeight},");
        idx.Append($"\"atlas\":[{atlas.GetWidth()},{atlas.GetHeight()}],");
        idx.Append("\"note\":\"text pixels white (modulate with font_color), shadow pixels ");
        idx.Append("black; chars >=0x80 are cp437\"}");
        File.WriteAllText($"{_ui}/{name}_index.json", idx.ToString(), new UTF8Encoding(false));
        Fonts++;
    }

    // ---- PANEL.DTA ----------------------------------------------------------

    public const int PanelW = 204, PanelH = 170;

    /// <summary>PANEL.DTA is a raw raster with no header at all; its WIDTH was
    /// the whole puzzle. The loader @0x4b9f70 walks the buffer building run
    /// lengths, and the index of a row's last byte starts at 0xCB = 203 and
    /// advances by 0xCC = 204 (@0x4ba03c) — so 204 bytes to the row, and
    /// 34680 / 204 = 170 rows exactly. 0xFF is transparent.</summary>
    public void WritePanel(byte[] panel)
    {
        Directory.CreateDirectory(_ui);
        if (panel.Length != PanelW * PanelH)
            throw new InvalidDataException($"PANEL.DTA ist {panel.Length} Bytes, erwartet {PanelW * PanelH}");
        var img = Image.CreateEmpty(PanelW, PanelH, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int y = 0; y < PanelH; y++)
            for (int x = 0; x < PanelW; x++)
            {
                byte v = panel[y * PanelW + x];
                if (v == 0xFF) continue;
                img.SetPixel(x, y, Color.Color8(_pal.R[v], _pal.G[v], _pal.B[v], 255));
                PanelPixels++;
            }
        img.SavePng($"{_ui}/panel.png");

        var sb = new StringBuilder();
        sb.Append($"{{\"source\":\"PANEL.DTA\",\"size\":[{PanelW},{PanelH}],\"stride\":{PanelW},");
        sb.Append("\"stride_evidence\":\"loader 0x4b9f70: row end index starts 0xCB, ");
        sb.Append("advances by 0xCC (@0x4ba03c) until 0x8777\",");
        sb.Append("\"display_box\":{\"x\":8,\"y\":43,\"w\":153,\"h\":94},");
        sb.Append("\"transparent\":255,\"palette\":\"DATA/01.PAL\"}");
        File.WriteAllText($"{_ui}/panel_index.json", sb.ToString(), new UTF8Encoding(false));
    }

    // ---- ANIM.CWA -----------------------------------------------------------

    /// <summary>The sequences picked off the 141-sequence contact sheet. Which
    /// ones they are is OUR choice — the blob holds far more than the remake
    /// draws.</summary>
    private static readonly (string Name, int Seq)[] Picked =
    {
        ("muzzle", 232), ("explosion", 48), ("blast", 550), ("wreck", 0),
    };

    /// <summary>Sequences 64 and 65 are rockets drawn as 8 facings x 3 phases
    /// (`frame = phase*8 + facing`), so they are written per facing and a
    /// projectile simply picks the sprite for its direction.</summary>
    private static readonly (string Name, int Seq)[] Rockets =
    {
        ("rocket_l", 64), ("rocket_h", 65),
    };

    public void WriteEffects(AnimFile anim)
    {
        Directory.CreateDirectory(_fx);
        var sb = new StringBuilder();
        sb.Append("{\"source\":\"ANIM.CWA\",\"palette\":\"DATA/01.PAL\",\"effects\":{");
        bool first = true;

        foreach (var (name, seq) in Picked)
        {
            var frames = Decode(anim, seq, int.MaxValue);
            if (frames.Count == 0) continue;
            var (w, h) = Bounds(frames);
            for (int i = 0; i < frames.Count; i++)
                Save($"{_fx}/{name}/f{i:00}.png", Canvas(frames[i], w, h));
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{name}\":{{\"sequence\":{seq},\"frames\":{frames.Count},");
            sb.Append($"\"w\":{w},\"h\":{h},\"anchor\":[{w / 2},{h / 2}]}}");
            Effects++;
        }

        foreach (var (name, seq) in Rockets)
        {
            var frames = Decode(anim, seq, 24);
            if (frames.Count < 8) continue;
            var (w, h) = Bounds(frames);
            for (int f = 0; f < 8; f++)                  // phase 0 = the whole rocket
                Save($"{_fx}/{name}/f{f}.png", Canvas(frames[f], w, h));
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{name}\":{{\"sequence\":{seq},\"facings\":8,");
            sb.Append($"\"phases\":{frames.Count / 8},\"w\":{w},\"h\":{h},");
            sb.Append($"\"anchor\":[{w / 2},{h / 2}],\"layout\":\"frame = phase*8 + facing\"}}");
            Effects++;
        }

        sb.Append("}}");
        File.WriteAllText($"{_fx}/effects_index.json", sb.ToString(), new UTF8Encoding(false));
    }

    private static List<CwpFile.Frame> Decode(AnimFile a, int seq, int limit)
    {
        var (count, start) = a.Sequence(seq);
        var list = new List<CwpFile.Frame>();
        for (int i = 0; i < count && i < limit; i++)
        {
            var f = a.Frame(start + i);
            if (f != null) list.Add(f);
        }
        return list;
    }

    /// <summary>One canvas for the whole sequence, so every frame shares an
    /// origin and the renderer can draw them all at the same anchor.</summary>
    private static (int W, int H) Bounds(List<CwpFile.Frame> frames)
    {
        int w = 1, h = 1;
        foreach (var f in frames)
        {
            w = Math.Max(w, f.Width);
            h = Math.Max(h, f.YOffset + f.Height);
        }
        return (w, h);
    }

    private Image Canvas(CwpFile.Frame f, int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                int o = y * f.Width + x;
                if (!f.Opaque[o]) continue;
                int cy = f.YOffset + y;
                if (x >= w || cy >= h) continue;
                byte i = f.Pixels[o];
                img.SetPixel(x, cy, Color.Color8(_pal.R[i], _pal.G[i], _pal.B[i], 255));
            }
        return img;
    }

    private void Save(string path, Image img)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        img.SavePng(path);
        EffectFrames++;
    }
}
