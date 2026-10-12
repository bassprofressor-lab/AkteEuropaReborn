namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ <b>bug-493 — DER NEBEL ALS SCHLEIER STATT ALS VERGROESSERTES SCHACHBRETT</b>
/// (12.10.2026). Gemeldet: »Unser Fog of War sieht komisch aus auf Eiskarten
/// wie K26, also das Raster/Kacheln des Fog of Wars. Sieht im Let's Play anders
/// aus.« Bericht <c>berichte/nebel-eis.md</c>.
///
/// <para><b>Was das Original tut</b> (selbst nachgelesen, C-Fassung):
/// <c>0x4AC990</c> schreibt Farbe 47 (<c>b0 2f</c>) in 20 Zeilen zu 20 Punkten
/// mit Schrittweite 2 (<c>88 06 83 c6 02</c>) und Zeilenversatz
/// <c>(ch xor ff) and 1</c> <b>direkt in die Bildflaeche</b> (Zeilenlaenge
/// <c>[0xA3AE7C]</c>) — ein Schachbrett aus einzelnen BILDSCHIRMpunkten, 1:1.
/// Die 285 Masken des Nebeldurchgangs <c>0x4B47A8</c> sind dasselbe Muster. Auf
/// 640×480 (und erst recht im Video eines Let's Play) verschwimmt das zu einem
/// gleichmaessigen 50-%-Schleier. Die Masken sind in allen 23 Kachelsaetzen
/// gleich (md5 gleich bis auf Satz 11, zwei Punkte), Farbe 47 ist in allen
/// Paletten <c>#13130F</c> — am Kachelsatz liegt es NICHT.</para>
///
/// <para><b>Was wir taten:</b> die Masken als Bild mit der Kamera vergroessert,
/// Filter <c>Nearest</c>. Bei Zoom 1,6..2,4 wird aus dem Ein-Punkt-Schachbrett
/// ein Muster aus 2er- und 1er-Laeufen — alle paar Bildpunkte ein Phasensprung,
/// sichtbar als Gitterlinien (gemessen im Bild K26: ein 1er-Lauf alle 12–13
/// Laeufe). Auf Eis (mittlere Bodenhelligkeit 126 gegen 61 auf K1) ist der
/// Abstand Nebelfarbe–Boden 2,5-mal so gross, darum faellt es dort auf.</para>
///
/// <para><b>Gebaut:</b> die Masken werden zur FLAECHE (ein Punkt gehoert dazu,
/// wenn er selbst oder mindestens zwei seiner vier Nachbarn Maskenpunkte sind,
/// <see cref="Import.NebelMasken.Flaeche"/>), in eine <see cref="CanvasGroup"/>
/// gezeichnet — dort vereinigen sich ueberlappende Masken wie im Original, wo
/// dieselbe Farbe auf denselben Punkt faellt — und die Gruppe einmal mit Farbe
/// 47 und Deckkraft 0,5 aufgetragen. Gleiche Helligkeit wie das Schachbrett,
/// kein Muster, das sich mit dem Zoom schlaegt.</para>
///
/// <para>⚠ <b>UNSERE SETZUNG / Abweichungen:</b> (1) wo im Original zwei Masken
/// GEGENPHASIG uebereinanderliegen (ungerade Hoehe ·15, Hangform 7 beginnt mit
/// x=0), wird das Schachbrett dort voll — wir zeigen 50 %. (2) Der Rand der
/// Flaeche ist um hoechstens einen Punkt anders als die Grenze der
/// Punktdichte. Gegenschalter <c>--nebel-schachbrett-alt</c> (Stand bug-427).
/// Pruefstand <c>--nebel-raster-check[=zoom,…]</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--nebel-schachbrett-alt</c> — Stand bug-427 (04.10.2026):
    /// die Masken als Schachbrett mit der Kamera vergroessert.</summary>
    public static bool NebelSchachbrettAlt;

    private ImageTexture? _nebelFlaeche;
    private bool[]? _nebelFlaecheBits;
    private CanvasGroup? _nebelGruppe;
    private Node2D? _nebelFlaechenKnoten, _bandKnoten;
    private int _nebelSchleierZellen;

    /// <summary>Wird gerade der Schleier gezeichnet (statt Schachbrett/Rampe)?</summary>
    public bool NebelSchleierAktiv => NebelmaskenAktiv && !NebelSchachbrettAlt && _nebelFlaeche != null;

    private const string SchleierShader = @"
shader_type canvas_item;
render_mode unshaded;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
uniform vec4 nebel : source_color;
uniform float dichte;
void fragment() {
    float a = textureLod(screen_texture, SCREEN_UV, 0.0).a;
    COLOR = vec4(nebel.rgb, a > 0.5 ? dichte : 0.0);
}";

    /// <summary>Aus NebelmaskenLaden: die Flaeche zum Atlas, und einmal die
    /// Knoten.</summary>
    private void NebelFlaecheLaden(Image? atlas)
    {
        _nebelFlaeche = null; _nebelFlaecheBits = null;
        if (atlas == null) return;
        var img = Import.NebelMasken.Flaeche(atlas);
        _nebelFlaeche = ImageTexture.CreateFromImage(img);
        _nebelFlaecheBits = Import.NebelMasken.Deckung(img);
        if (_nebelGruppe != null) return;
        var mat = new ShaderMaterial { Shader = new Shader { Code = SchleierShader } };
        mat.SetShaderParameter("nebel", Color.Color8(FogR, FogG, FogB));
        mat.SetShaderParameter("dichte", FogDim);
        _nebelGruppe = new CanvasGroup { Name = "NebelSchleier", Material = mat, Visible = false };
        _nebelFlaechenKnoten = new Node2D { Name = "NebelFlaeche" };
        _nebelFlaechenKnoten.Draw += () => NebelSchleierZeichnen(_nebelFlaechenKnoten);
        _nebelGruppe.AddChild(_nebelFlaechenKnoten);
        AddChild(_nebelGruppe);
        // das Gummiband muss UEBER dem Nebel bleiben wie bisher
        _bandKnoten = new Node2D { Name = "Band", Visible = false };
        _bandKnoten.Draw += () =>
        {
            if (_band is not { } band) return;
            var b = band.Abs();
            _bandKnoten.DrawRect(b, new Color(0.3f, 1f, 0.5f, 0.12f));
            _bandKnoten.DrawRect(b, new Color(0.3f, 1f, 0.5f, 0.9f), false, 1.5f);
        };
        AddChild(_bandKnoten);
    }

    /// <summary>Aus _Process: sichtbar schalten und je Bild neu zeichnen.</summary>
    private void NebelSchleierTakt()
    {
        if (_nebelGruppe == null) return;
        bool an = FogActive && _fog != null && NebelSchleierAktiv;
        _nebelGruppe.Visible = an;
        _bandKnoten!.Visible = an;
        if (!an) return;
        _nebelFlaechenKnoten!.QueueRedraw();
        _bandKnoten.QueueRedraw();
    }

    private void NebelSchleierZeichnen(Node2D ziel)
    {
        if (_fog == null || _nebelFlaeche == null) return;
        _nebelSchleierZellen = NebelmaskenGezeichnet = NebelmaskenZeichnenIn(ziel, _nebelFlaeche);
    }

    // ---- der Pruefstand --------------------------------------------------------

    /// <summary>
    /// <c>--nebel-raster-check[=z1,z2,…]</c>: setzt zusammen, was beide Wege
    /// zeichnen (ohne GPU: Schachbrett = Vereinigung der Maskenpunkte, Schleier
    /// = Vereinigung der Flaechen mal 0,5), tastet es wie die Kamera mit
    /// Nearest bei mehreren Zoomstufen ab und zaehlt in jeder Bildzeile die
    /// <b>Phasenspruenge</b> (Laeufe gleicher Punkte, deren Laenge vom
    /// haeufigsten Lauf abweicht) je 1000 Nebelbildpunkte — das sind die
    /// Gitterlinien. Dazu die mittlere Deckung beider Wege (Helligkeit).
    /// <para>Soll: Schleier 0 Spruenge bei jedem Zoom und Deckung gleich dem
    /// Schachbrett (±0,02). Nullmodell: das Schachbrett hat bei Zoom 1 nur die
    /// Saumnaehte des Originals und bei 1,6/1,92/2,4 deutlich mehr.</para>
    /// </summary>
    public string NebelRasterCheck(float[]? zooms)
    {
        var sb = new System.Text.StringBuilder("nebel-raster-check (bug-493)\n");
        if (_fog == null || _nebelDeckung == null || _nebelFlaecheBits == null)
            return sb.Append($"  keine Masken ({Import.NebelMasken.Herkunft}) — der Lauf sagt NICHTS\n  DURCHGEFALLEN").ToString();
        bool war = ForceFog;
        ForceFog = true;
        UpdateFog();
        sb.Append($"  Masken: {Import.NebelMasken.Herkunft}; Zeichnung im Spiel: "
                + $"{(NebelSchleierAktiv ? "SCHLEIER" : NebelmaskenAktiv ? "SCHACHBRETT (--nebel-schachbrett-alt)" : "Rampe")}\n");
        int w = _fog.Width, h = _fog.Height;
        int hubMax = _fogElevMax * Import.MapBaker.ElevStep + Import.NebelMasken.Hub;
        int yMin = _oy - hubMax, rasterH = h * TileH + hubMax + Import.NebelMasken.ZelleH, rasterW = w * TileW;
        var punkte = Raster(_nebelDeckung);
        var flaeche = Raster(_nebelFlaecheBits);
        long fl = 0, pk = 0;
        for (int i = 0; i < flaeche.Length; i++) { if (flaeche[i]) { fl++; if (punkte[i]) pk++; } }
        float deckAlt = fl > 0 ? (float)pk / fl : 0f;     // Anteil dunkler Punkte in der Flaeche
        sb.Append($"  Nebelflaeche {fl} Weltpunkte; Schachbrett deckt davon {deckAlt:0.000}, Schleier {FogDim:0.000} (Deckkraft)\n");
        bool ok = fl > 0 && Mathf.Abs(deckAlt - FogDim) <= 0.02f;

        zooms ??= new[] { 1.6f, 1.92f, 2f, 2.4f, 3f };
        bool schleier = NebelSchleierAktiv;
        float naht = -1f;                                  // Zoom 1: die Naehte des Originals
        foreach (float z in System.Linq.Enumerable.Prepend(zooms, 1f))
        {
            long nebelPx = 0, sprAlt = 0, sprNeu = 0;
            int bildH = (int)(rasterH * z), bildW = (int)(rasterW * z);
            var zeileAlt = new bool[bildW];
            var zeileNeu = new bool[bildW];
            var gebiet = new bool[bildW];
            for (int sy = 0; sy < bildH; sy += 7)
            {
                int y = (int)(sy / z);
                if (y >= rasterH) break;
                // eine Bildzeile wie die Kamera mit Nearest; Gebiet = was einer
                // der beiden Wege bedeckt
                for (int sx = 0; sx < bildW; sx++)
                {
                    int x = Mathf.Min((int)(sx / z), rasterW - 1);
                    int i = y * rasterW + x;
                    zeileAlt[sx] = punkte[i];
                    zeileNeu[sx] = flaeche[i];
                    gebiet[sx] = punkte[i] || flaeche[i];
                    if (gebiet[sx]) nebelPx++;
                }
                sprAlt += Spruenge(zeileAlt, gebiet, bildW, true);
                sprNeu += Spruenge(zeileNeu, gebiet, bildW, false);
            }
            float jeAlt = nebelPx > 0 ? sprAlt * 1000f / nebelPx : 0f, jeNeu = nebelPx > 0 ? sprNeu * 1000f / nebelPx : 0f;
            sb.Append($"  Zoom {z:0.00}: {nebelPx} Nebelbildpunkte (jede 7. Zeile); Phasenspruenge je 1000 — Schachbrett {jeAlt:0.0}, Schleier {jeNeu:0.0}\n");
            if (naht < 0f) naht = jeAlt;
            // geurteilt wird ueber den Weg, der im Spiel GEZEICHNET wird:
            // mit --nebel-schachbrett-alt muss der Lauf durchfallen (Nullmodell)
            ok &= (schleier ? jeNeu : jeAlt) <= naht + 1f;
        }
        sb.Append("  ⚠ Die Spruenge des Schachbretts bei Zoom 1 sind die Naehte des Originals selbst\n"
                + "    (gegenphasige Masken); was darueber hinausgeht, macht die Vergroesserung.\n");
        UpdateFog();
        ForceFog = war;
        sb.Append($"  Soll: der gezeichnete Weg ({(schleier ? "Schleier" : "Schachbrett")}) hat bei keinem Zoom mehr Spruenge je 1000\n"
                + $"    als das Schachbrett bei Zoom 1 plus 1 ({naht:0.0} + 1); Deckung Schachbrett = Schleier ±0,02\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();

        // Laeufe gleicher Werte je Strecke im Gebiet; erster und letzter Lauf
        // einer Strecke sind vom Rand abgeschnitten und zaehlen nicht.
        // Schachbrett: Sprung = Lauf, dessen Laenge vom haeufigsten abweicht.
        // Schleier: JEDER innere Lauf ist ein Sprung (er sollte einfarbig sein).
        static long Spruenge(bool[] v, bool[] gebiet, int n, bool schachbrett)
        {
            var innen = new List<int>();
            var strecke = new List<int>();
            int lauf = 0;
            for (int sx = 0; sx <= n; sx++)
            {
                bool drin = sx < n && gebiet[sx];
                if (!drin)
                {
                    if (lauf > 0) strecke.Add(lauf);
                    for (int k = 1; k < strecke.Count - 1; k++) innen.Add(strecke[k]);
                    strecke.Clear(); lauf = 0;
                    continue;
                }
                if (lauf > 0 && v[sx] != v[sx - 1]) { strecke.Add(lauf); lauf = 0; }
                lauf++;
            }
            if (!schachbrett) return innen.Count;
            var zahl = new Dictionary<int, int>();
            foreach (int l in innen) zahl[l] = zahl.GetValueOrDefault(l) + 1;
            int modal = 0, best = -1;
            foreach (var kv in zahl) if (kv.Value > best) { best = kv.Value; modal = kv.Key; }
            long m = 0;
            foreach (int l in innen) if (l != modal) m++;
            return m;
        }

        bool[] Raster(bool[] bits)
        {
            var r = new bool[rasterW * rasterH];
            for (int rr = 0; rr < h; rr++)
                for (int c = 0; c < w; c++)
                {
                    int k = NebelKachel(c, rr);
                    if (k < 0) continue;
                    int b = k / Import.NebelMasken.Hangformen, hf = k % Import.NebelMasken.Hangformen;
                    int y0 = _oy + rr * TileH - NebelElev(c, rr) * Import.MapBaker.ElevStep - Import.NebelMasken.Hub - yMin;
                    for (int ly = 0; ly < Import.NebelMasken.ZelleH; ly++)
                    {
                        int yy = y0 + ly;
                        if (yy < 0 || yy >= rasterH) continue;
                        int zeile = (b * Import.NebelMasken.ZelleH + ly) * Import.NebelMasken.AtlasB + hf * TileW;
                        for (int lx = 0; lx < TileW; lx++)
                            if (bits[zeile + lx]) r[yy * rasterW + c * TileW + lx] = true;
                    }
                }
            return r;
        }
    }
}
