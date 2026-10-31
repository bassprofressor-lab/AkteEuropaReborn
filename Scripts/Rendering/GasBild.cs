namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ <b>DAS GASBILD WIE DER BLITTER 0x4AC830</b> (30.10.2026, bug-723,
/// berichte/bau-gaswolke.md §9).
///
/// <para><b>Original:</b> der Zeichner Art 14 (0x42B6E4) legt je Teilchen ein Bild der
/// Folge 69 auf den Bildschirm, aber der Blitter 0x4AC830 kopiert dessen Bildpunkte
/// NICHT: fuer jeden deckenden Punkt der Schablone (Byte != 0xFF) schreibt er
/// <c>Ziel := CWG[Ziel]</c>, die 256-Byte-Tafel NN.CWG (0xB136B8). Liegen n Teilchen
/// uebereinander, wirkt die Tafel n-mal: <c>CWG^n</c>.</para>
///
/// <para><b>Bei uns:</b> der Boden ist RGB, aber aus palettierten Kacheln gebacken — gemessen
/// trifft JEDER Bildpunkt der Bodenbilder eine Palettenfarbe genau (NET07 25,8 Mio., Karte 01
/// 2,4 Mio., Karte 25 32,1 Mio. Punkte: 100,0000 %). Darum rechnet ein Shader zurueck:</para>
/// <list type="number">
/// <item>Eine Nebenleinwand (SubViewport, gleiche Groesse und Kameralage) zaehlt je Bildpunkt,
/// wie viele Schablonen ihn decken (additiv, 1/255 je Bild).</item>
/// <item>Ein Rechteck ueber dem Sichtfeld liest den Bildschirm (hint_screen_texture), sucht die
/// Farbe binaer in der sortierten Spielpalette (genauer Treffer, sonst die naechste Farbe)
/// und schreibt <c>Palette[CWG^n[Index]]</c> aus einer Tafel 256 × 33 (n bis 32).</item>
/// </list>
/// <para>⚠ UNSERE SETZUNGEN: das Gas liegt ueber allem der Weltebene (das Original reiht es
/// zeilenweise zwischen die Einheiten ein); Punkte, die nicht genau eine Palettenfarbe
/// tragen (unsere Schatten sind Faktoren, kein Palettenwert), nehmen die naechste Farbe;
/// n wird bei 32 gekappt. Gegenschalter <c>--gasbild-mischung</c>: die Mischung vom
/// 30.10. mittags (Gaswolke.cs).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--gasbild-mischung</c> — bug-723, der Stand vom 30.10.2026 (Mischfarbe 60 %).</summary>
    public static bool GasbildMischung;

    /// <summary>Der Kachelsatz der Karte (LoadPatterns), fuer NN.PAL/NN.CWG.</summary>
    private int _gasTileset = -1;
    private int _gasTafelSatz = -2;
    private ImageTexture? _gasSchluessel, _gasTafel;
    private int _gasSchluesselN;
    private SubViewport? _gasMaskeVp;
    private Node2D? _gasMaskeKnoten, _gasSchicht;
    private ShaderMaterial? _gasSchichtMat;
    public int GasbildTeilchen;
    public string GasbildHerkunft = "";

    private const int GasStufen = 33;

    private const string GasMaskeShader = @"
shader_type canvas_item;
render_mode unshaded, blend_add;
void fragment() {
    float a = texture(TEXTURE, UV).a;
    COLOR = vec4(1.0 / 255.0, 0.0, 0.0, a > 0.5 ? 1.0 : 0.0);
}";

    private const string GasSchichtShader = @"
shader_type canvas_item;
render_mode unshaded;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform sampler2D maske : repeat_disable, filter_nearest;
uniform sampler2D schluessel : repeat_disable, filter_nearest;
uniform sampler2D tafel : repeat_disable, filter_nearest;
uniform int n_schluessel;
int key_of(vec3 c) {
    ivec3 q = ivec3(round(c * 255.0));
    return q.r * 65536 + q.g * 256 + q.b;
}
void fragment() {
    int n = int(round(textureLod(maske, SCREEN_UV, 0.0).r * 255.0));
    if (n <= 0) discard;
    n = min(n, 32);
    vec3 c = textureLod(screen_texture, SCREEN_UV, 0.0).rgb;
    int k = key_of(c);
    int lo = 0, hi = n_schluessel - 1, idx = -1;
    for (int s = 0; s < 9 && lo <= hi; s++) {
        int mid = (lo + hi) / 2;
        vec4 t = texelFetch(schluessel, ivec2(mid, 0), 0);
        int km = key_of(t.rgb);
        if (km == k) { idx = int(round(t.a * 255.0)); break; }
        if (km < k) lo = mid + 1; else hi = mid - 1;
    }
    if (idx < 0) {
        float best = 1e9;
        for (int i = 0; i < n_schluessel; i++) {
            vec4 t = texelFetch(schluessel, ivec2(i, 0), 0);
            vec3 d = t.rgb - c;
            float e = dot(d, d);
            if (e < best) { best = e; idx = int(round(t.a * 255.0)); }
        }
    }
    COLOR = vec4(texelFetch(tafel, ivec2(idx, n), 0).rgb, 1.0);
}";

    /// <summary>NN.PAL und NN.CWG lesen, Schluessel- und Stufentafel bauen. false = fehlt.</summary>
    private bool GasTafelnLaden()
    {
        if (_gasTafelSatz == _gasTileset) return _gasTafel != null;
        _gasTafelSatz = _gasTileset;
        _gasTafel = _gasSchluessel = null;
        if (_gasTileset < 0) { GasbildHerkunft = "kein Kachelsatz"; return false; }
        string pp = Core.Content.Path($"DATA/{_gasTileset:00}.PAL"), cp = Core.Content.Path($"DATA/{_gasTileset:00}.CWG");
        if (!FileAccess.FileExists(pp) || !FileAccess.FileExists(cp))
        {
            GasbildHerkunft = $"DATA/{_gasTileset:00}.PAL/.CWG fehlt (Teil »oberflaeche« neu einlesen)";
            return false;
        }
        var pal = FileAccess.GetFileAsBytes(pp);
        var cwg = FileAccess.GetFileAsBytes(cp);
        if (pal.Length < Import.PalFile.FileSize || cwg.Length < 256) { GasbildHerkunft = "PAL/CWG zu kurz"; return false; }
        var p = Import.PalFile.FromBytes(pal);
        // Schluessel: jede RGB-Farbe einmal, der KLEINSTE Index gewinnt (UNSERE SETZUNG bei
        // Doppelfarben), sortiert nach r·65536 + g·256 + b
        var erst = new SortedDictionary<int, int>();
        for (int i = 0; i < 256; i++)
        {
            int k = p.R[i] * 65536 + p.G[i] * 256 + p.B[i];
            if (!erst.ContainsKey(k)) erst[k] = i;
        }
        var sb = new byte[256 * 4];
        int n = 0;
        foreach (var kv in erst)
        {
            int i = kv.Value;
            sb[4 * n] = p.R[i]; sb[4 * n + 1] = p.G[i]; sb[4 * n + 2] = p.B[i]; sb[4 * n + 3] = (byte)i;
            n++;
        }
        _gasSchluesselN = n;
        _gasSchluessel = ImageTexture.CreateFromImage(Image.CreateFromData(256, 1, false, Image.Format.Rgba8, sb));
        var tb = new byte[256 * GasStufen * 4];
        for (int i = 0; i < 256; i++)
        {
            int j = i;
            for (int s = 0; s < GasStufen; s++)
            {
                int o = 4 * (s * 256 + i);
                tb[o] = p.R[j]; tb[o + 1] = p.G[j]; tb[o + 2] = p.B[j]; tb[o + 3] = 255;
                j = cwg[j];
            }
        }
        _gasTafel = ImageTexture.CreateFromImage(Image.CreateFromData(256, GasStufen, false, Image.Format.Rgba8, tb));
        GasbildHerkunft = $"DATA/{_gasTileset:00}.CWG, {n} Palettenfarben";
        return true;
    }

    /// <summary>Die Knoten einmal anlegen; aus GaswolkeZeichnen.</summary>
    private void GasbildKnoten()
    {
        if (_gasSchicht != null) return;
        _gasMaskeVp = new SubViewport
        {
            Name = "GasMaske", TransparentBg = true, Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
        };
        _gasMaskeKnoten = new Node2D { Name = "GasSchablonen", Material = new ShaderMaterial { Shader = new Shader { Code = GasMaskeShader } } };
        _gasMaskeKnoten.Draw += GasSchablonenZeichnen;
        _gasMaskeVp.AddChild(_gasMaskeKnoten);
        AddChild(_gasMaskeVp);
        _gasSchichtMat = new ShaderMaterial { Shader = new Shader { Code = GasSchichtShader } };
        _gasSchicht = new Node2D { Name = "GasSchicht", Material = _gasSchichtMat, Visible = false };
        _gasSchicht.Draw += () =>
        {
            var vp = GetViewport();
            var inv = (vp.CanvasTransform * GetGlobalTransform()).AffineInverse();
            var r = vp.GetVisibleRect();
            var a = inv * r.Position; var b = inv * r.End;
            _gasSchicht.DrawRect(new Rect2(a, b - a).Abs(), Colors.White);
        };
        AddChild(_gasSchicht);
    }

    /// <summary>bug-723: der neue Weg. false = nicht zustaendig (Mischung oder fehlende Tafeln).</summary>
    private bool GasbildCwg()
    {
        if (!GasTafelnLaden()) { if (_gasSchicht != null) _gasSchicht.Visible = false; return false; }
        if (_gasSchicht == null) { if (!_gasKnotenBestellt) { _gasKnotenBestellt = true; Callable.From(GasbildKnoten).CallDeferred(); } return false; }
        var vp = GetViewport();
        // Die Leinwand in der Groesse des GERENDERTEN Bildes (Fenster mit Inhaltsskalierung:
        // sichtbares Rechteck 1600×900, Bild 1920×1080) — sonst trifft SCREEN_UV daneben.
        var bild = (Vector2)vp.GetTexture().GetSize();
        var sicht = vp.GetVisibleRect().Size;
        var s = new Vector2(bild.X / Mathf.Max(1f, sicht.X), bild.Y / Mathf.Max(1f, sicht.Y));
        _gasMaskeVp!.Size = (Vector2I)bild;
        if (!_gasMassGemeldet) { _gasMassGemeldet = true; GD.Print($"gasbild: Bild {bild}, sichtbar {sicht}, Leinwand {_gasMaskeVp.Size}, Fenster {GetWindow().Size}, Inhalt {GetWindow().ContentScaleSize}"); }
        _gasMaskeVp.CanvasTransform = Transform2D.Identity.Scaled(s) * vp.CanvasTransform * GetGlobalTransform();
        _gasSchichtMat!.SetShaderParameter("maske", _gasMaskeVp.GetTexture());
        _gasSchichtMat.SetShaderParameter("schluessel", _gasSchluessel);
        _gasSchichtMat.SetShaderParameter("tafel", _gasTafel);
        _gasSchichtMat.SetShaderParameter("n_schluessel", _gasSchluesselN);
        // Die Zaehlleinwand laeuft auch unter --gasbild-mischung mit (fuer die Bildprobe);
        // die Schicht selbst zeichnet dann nicht, die Mischung kommt aus GaswolkeZeichnen.
        _gasSchicht!.Visible = _gasAnzahl > 0 && !GasbildVerbergen && !GasbildMischung;
        _gasMaskeKnoten!.QueueRedraw();
        _gasSchicht.QueueRedraw();
        return !GasbildMischung;
    }

    /// <summary>Fuer die Bildprobe: die Schicht einen Rahmen lang aus.</summary>
    public bool GasbildVerbergen;
    private bool _gasKnotenBestellt, _gasMassGemeldet;

    /// <summary>Die Schablonen in die Nebenleinwand — dieselbe Lage wie <see cref="GaswolkeZeichnen"/>.</summary>
    private void GasSchablonenZeichnen()
    {
        GasbildTeilchen = 0;
        if (_gasMaskeKnoten == null || _gasAnzahl == 0) return;
        var frames = EffectFrames("gas");
        if (frames.Count == 0) return;
        for (int k = 0; k < GasPlaetze; k++)
        {
            if (!_gas[k].Belegt) continue;
            var g = _gas[k];
            int cx = g.X / GasFein, cy = g.Y / GasFein + 1;
            if (FogActive && !Watched(cx, cy)) continue;                        // sec50 an (cx, cy+1)
            int feinX = (g.X % GasFein) / 3, feinY = 20 + (g.Y % GasFein) / -6;
            int hub = HubOf(cx, cy, feinX, feinY);
            int f = (_taktNr + k) % 20;                                         // @0x42B703
            if (f >= frames.Count) continue;
            _gasMaskeKnoten.DrawTexture(frames[f], new Vector2(_ox + cx * TileW + feinX - 20, _oy + cy * TileH - hub - feinY));
            GasbildTeilchen++;
        }
    }

    /// <summary>Fuer die Bildprobe: die Zaehlleinwand als Bild.</summary>
    public Image? GasMaskeBild() => _gasMaskeVp?.GetTexture().GetImage();

    /// <summary>Fuer die Bildprobe: die Zelle der ersten eigenen Einheit (dort ist Sicht).</summary>
    public Vector2I GasbildProbeZelle()
    {
        foreach (var e in _entities)
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Owner == ViewPlayer) return new Vector2I(e.Col, e.Row);
        return new Vector2I(_nav?.Width / 2 ?? 10, _nav?.Height / 2 ?? 10);
    }

    /// <summary>Fuer die Bildprobe: eine dichte Wolke an die Kartenstelle legen (EINGRIFF).</summary>
    public Vector2 GasbildProbeLegen(int col, int row)
    {
        for (int i = 0; i < 60; i++)
            GasAnlegen(col * GasFein + (i * 37) % 360 - 120, row * GasFein + (i * 53) % 240 - 120, 0, 0, 250);
        return CellCenter(col, row);
    }
}
