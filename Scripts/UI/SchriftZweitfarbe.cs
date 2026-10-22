using Godot;

namespace AkteEuropaReborn.UI;

/// <summary>
/// ⭐ 22.10.2026, bug-593 (Reaper R2b: »die eigenen waren etwas rötlicher«) —
/// <b>die zweifarbige Schrift der eigenen Bauzeile</b>.
///
/// <para><b>Original</b> (<c>berichte/eigene-entwuerfe-opus.md</c> R2b): die Zeile
/// der Bauliste <c>0x469D38</c> (F <c>0x468628</c>ff.) fragt Satz <c>+0x01</c>; bei
/// einem selbst erstellten Entwurf ruft sie <c>0x4020A4 → 0x4BA5E0(…, 0x50, 0x54)</c>,
/// und der Glyphenzeichner <c>0x4BA2B0</c> malt Zellbyte <c>0xFE</c> in Platz
/// <b>80 (255,255,235)</b> und <c>0x24</c> in Platz <b>84 (227,199,147)</b>
/// (<c>0x4BA378…0x4BA38E</c>). Der gewöhnliche Weg nimmt 254/36. <c>0x24</c> ist der
/// GRÖSSERE Teil der Glyphen (2285 gegen 1534 Pixel in FONT.CWD) — darum der warme,
/// ockerfarbene Eindruck.</para>
///
/// <para><b>Bei uns</b> backt der Importeur <c>0xFE</c> weiß und <c>0x24</c> als
/// 0.76-Grau (<c>InterfaceExporter.ShadeF</c>), und Godot moduliert beides mit EINER
/// Farbe — für Platz 80 ergäbe das (194,194,179) statt (227,199,147).</para>
///
/// <para>⚠ <b>UNSERE SETZUNG (Bauweise)</b>: kein neuer gebackener Bogen (das wäre
/// eine Importeuränderung samt <c>InhaltsStand.Noetig</c>), sondern zur Laufzeit eine
/// umgefärbte Kopie der Schrift: weiße Pixel → Platz 80, 0.76-graue → Platz 84, alle
/// wörtlichen Palettenpixel unverändert; gezeichnet mit Modulation Weiß. Nur für die
/// eigene Bauzeile; die übrigen Farbpaare des Originals bleiben bei der 0.76-Näherung.
/// Sinnbilder ] [ { werden in dieser Zeile ebenfalls 80/84 (wie im Original).</para>
///
/// <para>Gegenschalter <c>--schrift-zweitfarbe-alt</c> (eine Farbe, Platz 80 moduliert).</para>
/// </summary>
public static class SchriftZweitfarbe
{
    /// <summary><c>--schrift-zweitfarbe-alt</c>.</summary>
    public static bool Alt;

    public static readonly Color Platz80 = Color.Color8(255, 255, 235);
    public static readonly Color Platz84 = Color.Color8(227, 199, 147);

    private static Font? _basis;
    private static FontFile? _eigen;

    /// <summary>Wie viele Pixel beim Umfärben getroffen wurden (0xFE-, 0x24-Teil).</summary>
    public static int PixelFe, Pixel24;

    /// <summary>Grauwert des Glyphenteils 0x24 im gebackenen Bogen.</summary>
    public static int Grau24 => Mathf.RoundToInt(Import.InterfaceExporter.ShadeF * 255f);

    /// <summary>Die umgefärbte Schrift zu <paramref name="basis"/>, oder null.</summary>
    public static Font? Eigene(Font? basis)
    {
        if (Alt || basis == null) return null;
        if (ReferenceEquals(_basis, basis)) return _eigen;
        _basis = basis;
        _eigen = Bauen(basis);
        return _eigen;
    }

    /// <summary>Welche Schrift und welche Modulation eine Bauzeile bekommt — EINE
    /// Stelle für Zeichner (<c>BaseWindow.Sheet</c>) und Prüfstand.</summary>
    public static (Font Schrift, Color Farbe, bool Zweifarbig) ZeilenSchrift(bool own, Font basis,
                                                                            Color rowFg, Color ownFg)
    {
        if (!own) return (basis, rowFg, false);
        var e = Eigene(basis);
        return e != null ? (e, new Color(1, 1, 1), true) : (basis, ownFg, false);
    }

    private static int Klasse(Color c)
    {
        if (c.A < 0.5f) return 0;
        int r = c.R8, g = c.G8, b = c.B8;
        if (r == 255 && g == 255 && b == 255) return 0xFE;
        if (r == g && g == b && Mathf.Abs(r - Grau24) <= 1) return 0x24;
        return 0;
    }

    private static FontFile? Bauen(Font basis)
    {
        FontFile? f = null;
        string path = Core.Content.Path("UI/akte_font.fnt");
        if (FileAccess.FileExists(path))
        {
            var neu = new FontFile();
            if (neu.LoadBitmapFont(path) == Error.Ok) f = neu;
        }
        if (f == null && basis is FontFile ff) f = ff.Duplicate() as FontFile;
        if (f == null) return null;
        if (basis is FontFile bf) f.FixedSizeScaleMode = bf.FixedSizeScaleMode;
        PixelFe = Pixel24 = 0;
        foreach (Vector2I size in f.GetSizeCacheList(0))
            for (int t = 0; t < f.GetTextureCount(0, size); t++)
            {
                var src = f.GetTextureImage(0, size, t);
                if (src == null || src.IsCompressed()) continue;
                var img = (Image)src.Duplicate();
                if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
                for (int y = 0; y < img.GetHeight(); y++)
                    for (int x = 0; x < img.GetWidth(); x++)
                    {
                        var c = img.GetPixel(x, y);
                        switch (Klasse(c))
                        {
                            case 0xFE: img.SetPixel(x, y, new Color(Platz80, c.A)); PixelFe++; break;
                            case 0x24: img.SetPixel(x, y, new Color(Platz84, c.A)); Pixel24++; break;
                        }
                    }
                f.SetTextureImage(0, size, t, img);
            }
        GD.Print($"Schrift: eigene Bauzeile zweifarbig (Platz 80/84), {PixelFe} + {Pixel24} Pixel umgefaerbt");
        return f;
    }

    /// <summary>Für den Prüfstand: die Farbe, mit der ein Pixel des Glyphenteils
    /// <paramref name="teil"/> (0xFE oder 0x24) in einer Bauzeile gemalt wird —
    /// Bogenpixel der gewählten Schrift mal Modulation. null = nicht messbar.</summary>
    public static Color? GemalterPixel(Font basis, bool own, int teil, Color rowFg, Color ownFg)
    {
        if (basis is not FontFile bf) return null;
        var (schrift, farbe, _) = ZeilenSchrift(own, basis, rowFg, ownFg);
        if (schrift is not FontFile sf) return null;
        foreach (Vector2I size in bf.GetSizeCacheList(0))
        {
            if (bf.GetTextureCount(0, size) == 0 || sf.GetTextureCount(0, size) == 0) continue;
            var a = bf.GetTextureImage(0, size, 0);
            var b = sf.GetTextureImage(0, size, 0);
            if (a == null || b == null) continue;
            if (a.GetFormat() != Image.Format.Rgba8) { a = (Image)a.Duplicate(); a.Convert(Image.Format.Rgba8); }
            if (b.GetFormat() != Image.Format.Rgba8) { b = (Image)b.Duplicate(); b.Convert(Image.Format.Rgba8); }
            for (int y = 0; y < a.GetHeight(); y++)
                for (int x = 0; x < a.GetWidth(); x++)
                    if (Klasse(a.GetPixel(x, y)) == teil)
                    {
                        var p = b.GetPixel(x, y);
                        return new Color(p.R * farbe.R, p.G * farbe.G, p.B * farbe.B, 1f);
                    }
        }
        return null;
    }
}
