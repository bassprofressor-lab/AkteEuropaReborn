namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>PAKET 4 GRAFIK — DIE NEBELKANTE WIE IM ORIGINAL</b> (04.10.2026,
/// bug-427). KayelGee: »Der Sichtnebel ist stellenweise nicht weich, sondern hat
/// rechteckige Kanten« (sein Bild <c>Bug Bilder/kayelgeebugbild4.png</c>, K1).
///
/// <para><b>Zuerst gemessen, dann gebaut.</b> Sein Bild liegt auf K1 bei
/// Kartenbildpunkt (104, 904), Vergroesserung 1,6 (Wasserkanten-Korrelation
/// 0,89), der Panzer auf Zelle (11, 47), Hoehe 3. Dieselbe Stelle unter
/// <c>--nebel-flach</c> (Nebeltextur ohne Hoehenhub und Schuerze): <b>die Treppe
/// ist weg</b> (Scratchpad <c>p4/vergleich_flach.png</c>) — Deutung N4(a) der
/// Lesung bestaetigt: der Hoehenversatz der 4×4-Rampe macht die Rechteckstufen.
/// </para>
///
/// <para><b>Gebaut: der Nebeldurchgang des Originals</b>
/// (<c>0x4B472A..0x4B485E</c>, letzter Durchgang vor dem Seitenwechsel), Zelle
/// fuer Zelle mit den 285 Originalmasken (Import/NebelMasken.cs):</para>
/// <code>
///   0x4B4774  sx := j·40 − feinX ; sy := i·20 − Hoehe·15 − feinY
///   0x4B47A8  sec50 == 1 (beobachtet)         -> nichts
///             sec50 == 2 (Saum)               -> Kachel 0x41FC60 (Eckenmuster B, Hangart)
///             sec50 == 0, Hangart != 0        -> Kachel 0x41FC60 mit B = 0
///             sec50 == 0, Hangart == 0        -> Schachbrett 0x4AC990 40×20
///   0x4B47F6  Blit 0x4ACCD0(kachel, sx, sy − 50)
/// </code>
/// <para>Das Schachbrett <c>0x4AC990</c> ist deckungsgleich mit der Maske B=0/C=0
/// (Kachel 1381: 400 Punkte, Zeile 0 ab x=1) und wird darum aus dem Atlas
/// gezeichnet. Alle Masken sind EINE Farbe (47), die Reihenfolge der Zellen ist
/// fuer das Bild darum gleichgueltig.</para>
///
/// <para>⚠ <b>Abweichungen, benannt:</b> (1) Muster B = 15 (alle vier Ecken hell)
/// liegt hinter dem Tafelende (Index 2280 von 2280); das Original liest dort
/// den naechsten Block (<c>0xBACBA0</c>, Zellanimationen) — wir zeichnen
/// <b>nichts</b> (V). (2) Das Original laesst eine flache Nebelzelle aus, die
/// nicht GANZ im Bild liegt (<c>sx+40 &lt; 0x5387C8</c> …); am Bildrand zeichnen
/// wir sie trotzdem. (3) Die Uebersichtskarte bleibt bei der Rampe.</para>
///
/// <para>Gegenschalter: <c>--nebel-rampe-alt</c> (die Rampe von feec479),
/// Die kleine Abhilfe an der Rampe (<c>--nebel-schuerze-alt</c> laut Baubrief) war
/// gebaut, wirkte gemessen nicht (18 Rechteckbloecke vorher wie nachher) und ist
/// wieder heraus.
/// Pruefstand <c>--nebelkante-check[=c,r]</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--nebel-rampe-alt</c> — Stand feec479: die 4×4-Texel-Rampe
    /// in Vollton-Alpha statt der Originalmasken.</summary>
    public static bool NebelRampeAlt;


    private ImageTexture? _nebelAtlas;
    private bool[]? _nebelDeckung;
    private int _nebelKachelsatz = -1;

    /// <summary>Wie viele Masken im letzten Bild gezeichnet wurden.</summary>
    public int NebelmaskenGezeichnet;

    /// <summary>Beim Laden der Karte (LoadPatterns kennt den Kachelsatz).</summary>
    private void NebelmaskenLaden(int tileset)
    {
        if (_nebelKachelsatz == tileset && _nebelAtlas != null) return;
        _nebelKachelsatz = tileset;
        _nebelAtlas = null; _nebelDeckung = null;
        var img = Import.NebelMasken.Laden(tileset);
        if (img == null) { GD.Print($"nebelmasken: {Import.NebelMasken.Herkunft} — es bleibt die Rampe"); return; }
        _nebelAtlas = ImageTexture.CreateFromImage(img);
        _nebelDeckung = Import.NebelMasken.Deckung(img);
        GD.Print($"nebelmasken: Kachelsatz {tileset:00}, {Import.NebelMasken.Herkunft}");
    }

    /// <summary>Werden gerade die Originalmasken gezeichnet?</summary>
    public bool NebelmaskenAktiv => !NebelRampeAlt && _nebelAtlas != null;

    /// <summary>Welche Maske eine Zelle bekommt: <c>B·19 + Hangart</c>, oder −1
    /// fuer keine (siehe Klassenkopf, 0x4B47A8).</summary>
    private int NebelKachel(int c, int r)
    {
        if (_fog == null) return -1;
        byte z = _fog.At(c, r);
        if (z == Simulation.FogGrid.Watched) return -1;
        int hang = _nav?.FlagAt(c, r) ?? 0;
        if (hang < 0 || hang >= Import.NebelMasken.Hangformen) return -1;
        if (z == Simulation.FogGrid.Saum)
        {
            int b = Import.NebelMasken.MusterAusEcken(_fog.CornerAt(c, r));
            if (b < 0 || b >= Import.NebelMasken.Muster) return -1;   // B 15: hinter dem Tafelende
            return b * Import.NebelMasken.Hangformen + hang;
        }
        return hang;                                            // B = 0
    }

    private int NebelElev(int c, int r)
    {
        int w = _fog!.Width;
        return _fogElev.Length == w * _fog.Height ? _fogElev[r * w + c] : 0;
    }

    /// <summary>Der Nebeldurchgang, nur ueber den sichtbaren Ausschnitt.</summary>
    private void NebelmaskenZeichnen()
    {
        if (_fog == null || _nebelAtlas == null) return;
        var welt = GetGlobalTransformWithCanvas().AffineInverse() * GetViewportRect();
        int w = _fog.Width, h = _fog.Height;
        int hubMax = _fogElevMax * Import.MapBaker.ElevStep + Import.NebelMasken.Hub;
        int c0 = Mathf.Max(0, Mathf.FloorToInt((welt.Position.X - _ox) / TileW) - 1);
        int c1 = Mathf.Min(w - 1, Mathf.FloorToInt((welt.End.X - _ox) / TileW) + 1);
        int r0 = Mathf.Max(0, Mathf.FloorToInt((welt.Position.Y - _oy) / TileH) - 2);
        int r1 = Mathf.Min(h - 1, Mathf.FloorToInt((welt.End.Y - _oy + hubMax) / TileH) + 1);
        const int ZB = Import.NebelMasken.ZelleB, ZH = Import.NebelMasken.ZelleH;
        int n = 0;
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                int k = NebelKachel(c, r);
                if (k < 0) continue;
                int b = k / Import.NebelMasken.Hangformen, hf = k % Import.NebelMasken.Hangformen;
                float x = _ox + c * TileW;
                float y = _oy + r * TileH - NebelElev(c, r) * Import.MapBaker.ElevStep - Import.NebelMasken.Hub;
                DrawTextureRectRegion(_nebelAtlas, new Rect2(x, y, ZB, ZH), new Rect2(hf * ZB, b * ZH, ZB, ZH));
                n++;
            }
        NebelmaskenGezeichnet = n;
    }

    // ---- der Pruefstand --------------------------------------------------------

    /// <summary>Deckt die Maske einer Zelle der Spalte c den Weltbildpunkt (x, y)?
    /// Dieselbe Rechnung wie <see cref="NebelmaskenZeichnen"/>, ohne GPU.</summary>
    private bool MaskeDeckt(int x, int y, int c, int rUm)
    {
        int lx = x - (_ox + c * TileW);
        if (lx < 0 || lx >= TileW || _nebelDeckung == null) return false;
        for (int r = Mathf.Max(0, rUm - 4); r <= Mathf.Min(_fog!.Height - 1, rUm + 8); r++)
        {
            int k = NebelKachel(c, r);
            if (k < 0) continue;
            int ly = y - (_oy + r * TileH - NebelElev(c, r) * Import.MapBaker.ElevStep - Import.NebelMasken.Hub);
            if (ly < 0 || ly >= Import.NebelMasken.ZelleH) continue;
            int b = k / Import.NebelMasken.Hangformen, hf = k % Import.NebelMasken.Hangformen;
            if (_nebelDeckung[(b * Import.NebelMasken.ZelleH + ly) * Import.NebelMasken.AtlasB + hf * TileW + lx]) return true;
        }
        return false;
    }

    /// <summary>Deckung der Rampe am Weltbildpunkt, 0..1 von »voll«.</summary>
    private float RampeDeckung(int x, int y)
    {
        if (_fogPixels == null) return 0f;
        int tx = Mathf.FloorToInt((x + 0.5f - _fogRect.Position.X) * _fogTexW / _fogRect.Size.X);
        int ty = Mathf.FloorToInt((y + 0.5f - _fogRect.Position.Y) * _fogTexH / _fogRect.Size.Y);
        if (tx < 0 || ty < 0 || tx >= _fogTexW || ty >= _fogTexH) return 0f;
        return _fogPixels[(ty * _fogTexW + tx) * 4 + 3] / (255f * FogDim);
    }

    /// <summary>
    /// Zaehlt die <b>vollgedeckten Saumzellen an Hoehenstufen</b>: eine flache
    /// Saumzelle (Hangart 0), deren Hoehe sich zur Zelle davor oder dahinter
    /// aendert; gelesen werden nur die Viertel ihrer HELLEN Ecken und davon nur
    /// die Bildpunkte, die im Bild nicht von einer vorderen Zelle verdeckt sind.
    /// Ist dort die Deckung ≥ 95 % der vollen (Rampe: Alpha/0,5; Maske:
    /// Punktdichte/0,5), ist die Kante an dieser Zelle ein Rechteck — die
    /// Treppe aus seinem Bild. Soll 0.
    /// </summary>
    private int VollgedeckteSaumzellen(out int geprueft, out int verdeckt, out string beispiel)
    {
        geprueft = verdeckt = 0; beispiel = "";
        if (_fog == null) return 0;
        bool masken = NebelmaskenAktiv;
        if (!masken) BuildFogTexture();
        int w = _fog.Width, h = _fog.Height, voll = 0;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                if (_fog.At(c, r) != Simulation.FogGrid.Saum) continue;
                if ((_nav?.FlagAt(c, r) ?? 0) != 0) continue;
                int e = NebelElev(c, r);
                bool stufe = (r + 1 < h && NebelElev(c, r + 1) != e) || (r > 0 && NebelElev(c, r - 1) != e);
                if (!stufe) continue;
                int bits = _fog.CornerAt(c, r);
                int x0 = _ox + c * TileW, y0 = _oy + r * TileH - e * Import.MapBaker.ElevStep;
                int sicht = 0; float summe = 0f;
                for (int q = 0; q < 4; q++)
                {
                    if ((bits & (1 << q)) == 0) continue;
                    int qx = x0 + (q & 1) * (TileW / 2), qy = y0 + (q >> 1) * (TileH / 2);
                    for (int y = qy; y < qy + TileH / 2; y++)
                    {
                        bool zu = false;
                        for (int k = 1; k <= 8 && r + k < h && !zu; k++)
                            if (_oy + (r + k) * TileH - NebelElev(c, r + k) * Import.MapBaker.ElevStep <= y) zu = true;
                        if (zu) continue;
                        for (int x = qx; x < qx + TileW / 2; x++)
                        {
                            sicht++;
                            summe += masken ? (MaskeDeckt(x, y, c, r) ? 2f : 0f) : RampeDeckung(x, y);
                        }
                    }
                }
                if (sicht < 20) { verdeckt++; continue; }
                geprueft++;
                float d = summe / sicht;
                if (d < 0.95f) continue;
                voll++;
                if (beispiel.Length == 0)
                    beispiel = $"      z. B. Zelle ({c},{r}) Hoehe {e}, Ecken {bits}: Deckung {d:0.00} auf {sicht} sichtbaren Punkten der hellen Ecken\n";
            }
        return voll;
    }

    /// <summary>
    /// ⭐ <b>Die Treppe als Zahl: gleichfoermige Rechteckbloecke im Nebel.</b>
    /// Gemessen an seinem Bild: die Stufen sind Bloecke einer Zelle Breite in
    /// EINER mittleren Helligkeit, mit waagerechten und senkrechten Kanten. So
    /// etwas entsteht, wo die Deckung ueber die ganze Zellbreite UND ueber
    /// mindestens 8 Bildzeilen gleich bleibt und weder klar noch voll ist
    /// (0,1..0,95 der vollen). Eine Rampe aendert sich von Zeile zu Zeile, eine
    /// Maske ist 0 oder voll (Punktdichte im 2×2-Fenster) — nur ein Band, das
    /// eine Texelzeile WIEDERHOLT (die Schuerze), erfuellt es.
    /// <para>Gezaehlt wird je Spalte und Band; <paramref name="bereich"/> in
    /// Zellen, null = ganze Karte.</para>
    /// </summary>
    private int RechteckBloecke(Rect2I? bereich, out string beispiel, out int kanten)
    {
        beispiel = ""; kanten = 0;
        if (_fog == null) return 0;
        bool masken = NebelmaskenAktiv;
        int w = _fog.Width, h = _fog.Height;
        int hubMax = _fogElevMax * Import.MapBaker.ElevStep + Import.NebelMasken.Hub;
        int yMin = _oy - hubMax, rasterH = h * TileH + hubMax + Import.NebelMasken.ZelleH;
        bool[]? raster = null;
        if (masken)
        {
            // was der Nebeldurchgang zeichnen wuerde, als Bitfeld
            raster = new bool[w * TileW * rasterH];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int k = NebelKachel(c, r);
                    if (k < 0) continue;
                    int b = k / Import.NebelMasken.Hangformen, hf = k % Import.NebelMasken.Hangformen;
                    int y0 = _oy + r * TileH - NebelElev(c, r) * Import.MapBaker.ElevStep - Import.NebelMasken.Hub - yMin;
                    for (int ly = 0; ly < Import.NebelMasken.ZelleH; ly++)
                    {
                        int yy = y0 + ly;
                        if (yy < 0 || yy >= rasterH) continue;
                        for (int lx = 0; lx < TileW; lx++)
                            if (_nebelDeckung![(b * Import.NebelMasken.ZelleH + ly) * Import.NebelMasken.AtlasB + hf * TileW + lx])
                                raster[yy * w * TileW + c * TileW + lx] = true;
                    }
                }
        }
        else BuildFogTexture();

        float D(int x, int y)       // Weltbildpunkt -> Deckung, 1 = voll
        {
            if (raster != null)
            {
                int rx = x - _ox, ry = y - yMin, n = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int xx = rx + dx, yy = ry + dy;
                        if (xx >= 0 && yy >= 0 && xx < w * TileW && yy < rasterH && raster[yy * w * TileW + xx]) n++;
                    }
                return n / 2f;
            }
            return RampeDeckung(x, y);
        }

        var rb = bereich ?? new Rect2I(0, 0, w, h);
        int bloecke = 0, harte = 0;
        string erstes = "";
        for (int c = Mathf.Max(0, rb.Position.X); c < Mathf.Min(w, rb.End.X); c++)
        {
            int x0 = _ox + c * TileW;
            int ya = _oy + Mathf.Max(0, rb.Position.Y) * TileH - hubMax;
            int yb = _oy + Mathf.Min(h, rb.End.Y) * TileH;
            int lauf = 0; float wert = -1f;
            bool warHart = false;
            for (int y = ya; y < yb; y++)
            {
                // HARTE KANTE: ueber die ganze Zellbreite springt die Deckung
                // innerhalb von 3 Zeilen gleichsinnig um >= 0,6 — eine
                // waagerechte Rechteckkante. Eine Rampe steigt hoechstens um
                // eine Texelstufe, eine gebogene Maske nie ueber die ganze Breite.
                int auf = 0, ab = 0;
                for (int x = x0 + 2; x < x0 + TileW - 2; x += 2)
                {
                    float j = D(x, y + 2) - D(x, y - 1);
                    if (j >= 0.6f) auf++; else if (j <= -0.6f) ab++;
                }
                bool hart = auf >= 16 || ab >= 16;
                if (hart && !warHart) harte++;
                warHart = hart;
                float sum = 0f, mn = 9f, mx = -9f;
                for (int x = x0 + 2; x < x0 + TileW - 2; x += 2)
                {
                    float d = D(x, y);
                    sum += d; mn = Mathf.Min(mn, d); mx = Mathf.Max(mx, d);
                }
                float m = sum / ((TileW - 4) / 2);
                bool gleich = mx - mn <= 0.08f && m >= 0.1f && m <= 0.95f;
                if (gleich && lauf > 0 && Mathf.Abs(m - wert) <= 0.05f) lauf++;
                else { if (lauf >= 8) Zaehle(c, y - lauf, lauf, wert); lauf = gleich ? 1 : 0; wert = m; }
            }
            if (lauf >= 8) Zaehle(c, yb - lauf, lauf, wert);
        }
        beispiel = erstes;
        kanten = harte;
        return bloecke;

        void Zaehle(int c, int y, int n, float m)
        {
            bloecke++;
            if (erstes.Length == 0)
                erstes = $"      z. B. Spalte {c}, Weltzeile {y}: {n} Zeilen gleichfoermig Deckung {m:0.00}\n";
        }
    }

    /// <summary><c>--nebelkante-check[=c,r]</c>: die eigene Einheit auf (c, r)
    /// (Vorgabe K1: 11,47 — die Stelle seines Bildes), dann ein Stempelraster
    /// ueber die ganze Karte wie <c>--nebelhoehe-check</c>.</summary>
    public string NebelkanteCheck(Vector2I? ort)
    {
        var sb = new System.Text.StringBuilder("nebelkante-check (bug-427)\n");
        if (_fog == null) return sb.Append("  kein Nebel geladen — der Lauf sagt NICHTS\n  DURCHGEFALLEN").ToString();
        bool war = ForceFog;
        ForceFog = true;
        sb.Append($"  Zeichnung: {(NebelmaskenAktiv ? "ORIGINALMASKEN" : "Rampe")} ({Import.NebelMasken.Herkunft}), "
                + $"--nebel-rampe-alt {NebelRampeAlt}, --nebel-flach {NebelFlach}\n");
        bool ok = true;

        // A — seine Stelle
        var stelle = NebelkanteStelleSetzen(ort);
        UpdateFog();
        int vA = VollgedeckteSaumzellen(out int gA, out int zA, out string bA);
        var um = new Rect2I(NebelkanteZelle.X - 16, NebelkanteZelle.Y - 16, 33, 33);
        int kA = RechteckBloecke(um, out string rA, out int hA);
        sb.Append($"  A {stelle}:\n");
        sb.Append($"    im Umkreis 16: Rechteckbloecke (gleichfoermig, >= 8 Zeilen) {kA}, harte Zellbreitenkanten {hA}\n{rA}");
        sb.Append($"    Saumzellen an Hoehenstufen: {gA} geprueft ({zA} verdeckt), vollgedeckt {vA}\n{bA}");
        ok &= kA == 0 && vA == 0;

        // B — Raster ueber die Karte
        var raster = new List<(int, int, int, int)>();
        for (int r = 4; r < _fog.Height; r += 9)
            for (int c = 4 + (r / 9) % 2 * 5; c < _fog.Width; c += 11)
                raster.Add((c, r, 4, 0));
        _fog.Update(raster);
        int vB = VollgedeckteSaumzellen(out int gB, out int zB, out string bB);
        int kB = RechteckBloecke(null, out string rB, out int hB);
        sb.Append($"  B Raster ({raster.Count} Stempel r 3), ganze Karte:\n");
        sb.Append($"    Rechteckbloecke {kB}, harte Zellbreitenkanten {hB}\n{rB}");
        sb.Append($"    Saumzellen an Hoehenstufen: {gB} geprueft ({zB} verdeckt), vollgedeckt {vB}\n{bB}");
        ok &= kB == 0 && vB == 0 && gB > 0;
        sb.Append("  ⚠ »vollgedeckte Saumzellen« ist die Zahl aus der Lesung (N4); sie faellt auch\n"
                + "    fuer die alte Rampe nicht — die Treppe entsteht nicht dort, sondern als\n"
                + "    gleichfoermiges Schuerzenband. Urteil ueber BEIDE Zahlen.\n");
        UpdateFog();
        ForceFog = war;
        sb.Append($"  Soll: 0 Rechteckbloecke und 0 vollgedeckte Saumzellen\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Stellt die erste eigene Einheit (Fahrzeug) auf die Zelle und
    /// gibt den Kamerapunkt zurueck — fuer Pruefstand und Bildlauf.</summary>
    private string NebelkanteStelleSetzen(Vector2I? ort)
    {
        int vi = -1;
        for (int i = 0; i < _entities.Count && vi < 0; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Owner == ViewPlayer && e.Infantry < 0) vi = i;
        }
        if (vi < 0) { NebelkanteZelle = new Vector2I(_fog!.Width / 2, _fog.Height / 2); return "keine eigene Einheit — Startstand"; }
        if (ort is { } o && _nav != null && _nav.InBounds(o.X, o.Y))
        {
            ProbeVersetzen(vi, o.X, o.Y);
            _entities[vi].Path = null;
        }
        NebelkanteKamera = _entities[vi].Pos;
        NebelkanteZelle = new Vector2I(_entities[vi].Col, _entities[vi].Row);
        return $"Einheit {LabelOf(_entities[vi])} auf ({_entities[vi].Col},{_entities[vi].Row}) Hoehe {ElevOf(_entities[vi].Col, _entities[vi].Row)}";
    }

    public Vector2 NebelkanteKamera;
    public Vector2I NebelkanteZelle;

    /// <summary>Bildlauf: Stelle setzen, Nebel rechnen, Kamerapunkt.</summary>
    public Vector2 NebelkanteBildVorbereiten(Vector2I? ort)
    {
        ForceFog = true;
        GD.Print("nebelkante-bild: " + NebelkanteStelleSetzen(ort));
        UpdateFog();
        _fogDrawn = -1;
        QueueRedraw();
        return NebelkanteKamera;
    }
}
