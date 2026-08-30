namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Bakes a map file into the picture the game draws: terrain, the elevation
/// skirt, and the objects on top.
///
/// Ported from cwm_render.py, which is where the placement was worked out:
///
///  * the map is drawn as a RECTANGLE — `x = col*40`, `y = row*20` — the
///    isometric look comes from the tile art, not from a staggered grid;
///  * a raised cell is shifted up by `elev*15`, and a sprite is anchored with
///    `-50 + its own y offset` so its foot sits on the bottom of the cell;
///  * a grid code below 1666 is a terrain frame from the tileset's first
///    directory, a code of 10000 or more an object from the second.
///
/// Three passes, and the middle one is the reason maps stopped having holes:
///
///  0. the ELEVATION SKIRT — a cell raised by E is stacked from level 0 up to
///     E, because the vertical face it exposes underneath would otherwise be
///     see-through. Tiles are 20 px tall against a 15 px step, so the 5 px
///     overlap closes the column completely.
///  A/B. an opaque backdrop under every partial tile, then the cell's own
///     terrain detail.
///  C. the objects, top row to bottom row, so nearer ones overlap farther ones.
///
/// A backdrop tile must genuinely fill a 40x20 cell. Coverage alone is not
/// enough: sliver ridges (40x5) and cliff faces (40x35) report full coverage of
/// their own box and still leave three quarters of the cell empty, which is
/// exactly the bug that produced black gaps before.
/// </summary>
public sealed class MapBaker
{
    public const int TileW = 40, TileH = 20, ElevStep = 15, GroundMax = 1666;

    /// <summary><c>--kein-objektboden</c> — der Stand von vor dem 28.08.2026:
    /// eine Objektzelle bekommt flach nur die <c>basis</c>-Flutfuellung, nicht
    /// ihre eigene Kachel. Siehe die Begruendung im Durchgang A/B.</summary>
    public static bool KeinObjektboden;

    /// <summary><c>--kohle-aus-code</c> — der Stand von vor dem 28.08.2026:
    /// verkohlte und abgebrannte Kachel eines zerstoerbaren Objekts als
    /// <c>code+1</c>/<c>code+2</c> statt aus der Arttafel.</summary>
    public static bool KohleAusCode;

    /// <summary>Zerstoerbare Objekte, bei denen die Arttafel eine ANDERE
    /// Brandkachel liefert als der Zellcode. ⚠ Eigene Zahl: ueber alle Karten
    /// muessen es genau 20 sein, alle auf map_16 — bleibt sie 0, ist die
    /// Aenderung wirkungslos durchgelaufen.</summary>
    public int KohleAusArttafel;

    /// <summary><c>--synthese-hintergrund</c> — die Bodensynthese auch als
    /// HINTERGRUND jeder Zelle statt der Flutfuellung <c>BuildBase</c>.
    /// ⚠⚠ AUS, und das ist gemessen: 994 Zellen auf map_02 bekommen ihre wahre
    /// Kachel nie gemalt (Gebaeude, Wald), und dort waere der Hintergrund das
    /// Einzige, was man sieht — die Stadt bekaeme Fels statt Strasse. Was der
    /// Schalter erreichen sollte, macht die NEBELDECKE, und die wirkt nur im
    /// Nebel.</summary>
    public static bool SyntheseHintergrund;

    /// <summary>Wieviele Zellen ihre EIGENE Objektkachel flach bekommen haben,
    /// die vorher nur die Flutfuellung sahen. 0 hiesse: die Aenderung greift
    /// nicht.</summary>
    public int ObjektbodenFlach;
    public const int BlitAnchor = -50;

    private readonly CwmFile _map;
    private readonly CwpFile _tiles;
    private readonly PalFile _pal;

    private sealed class Sprite
    {
        public int W, H, YOff;
        public byte[] Rgba = Array.Empty<byte>();     // premultiplied by nothing: a or 0
        public bool Full;                             // fills a whole ground cell
    }

    private readonly Dictionary<int, Sprite?> _frame = new();
    private readonly Dictionary<int, Sprite?> _object = new();

    public int Width => _map.Width;
    public int Height => _map.Height;
    public int PixelW { get; private set; }
    public int PixelH { get; private set; }

    /// <summary>Wieviele Zellen NUR über die Zeichenlage aus Sektion 20 in den
    /// verzahnten Durchgang kommen (<c>imap == 0xFFFF</c> und Lagenbyte ≥ 100)
    /// — Brücken und Rampen. Über alle Karten gemessen: 578. Steht in der
    /// Meldung, damit ein Neubacken sichtbar macht, ob die Regel greift.
    /// </summary>
    public int LagenZellen;

    /// <summary>Wieviele Zellen FLACH gemalt wurden, weil sie ein befahrbares
    /// Bauwerk tragen (Bruecke/Mole/Rampe). Die Zahl belegt, dass die Regel
    /// greift - steht sie auf 0, ist Sektion 20 nicht da.</summary>
    public int BefahrbarFlach;

    /// <summary>Wieviele Zellen als GELAENDER erkannt wurden (imap 0xFFFF mit
    /// Lagenbyte >= 100) und damit ins Zeilenfach gehoeren statt flach gebacken
    /// zu werden. ⚠ Steht sie auf 0, waehrend BefahrbarFlach > 0 ist, dann
    /// fehlt der Bruecke ihr Gelaender - genau der Fehler vom 25.08.</summary>
    public int GelaenderAufragend;
    public int OriginY { get; private set; }

    public MapBaker(CwmFile map, CwpFile tiles, PalFile pal)
    {
        _map = map; _tiles = tiles; _pal = pal;
    }

    /// <summary>How many object cells the last bake left to the live renderer
    /// because a building stands on them — see <see cref="BuildingCells"/>.</summary>
    public int BuildingCellsSkipped { get; private set; }

    /// <summary>Cells whose object tile the map only carries because a BUILDING
    /// stands there — those the live renderer draws, so they must not be baked
    /// into the picture.</summary>
    public int MissedBuildingCells { get; private set; }

    /// <summary>
    /// The grid cells that belong to a standing building.
    ///
    /// <para><b>Why this exists.</b> The original writes a building's tiles into
    /// the map grid as <c>tile + 0x2710</c> (@0x4C8E2D), so to the baker they
    /// look like any other object and used to be burnt into the map picture. A
    /// destroyed building could then never stop being drawn — the pixels were
    /// part of the map. Now the baker leaves them out and the renderer draws the
    /// building itself, which is what lets it show its RUIN when it falls (see
    /// <see cref="BuildingPatterns.RuinPattern"/>).</para>
    ///
    /// <para>A cell is only claimed when the grid code there is EXACTLY the
    /// building's own tile plus <see cref="CwpFile.ObjectCodeBase"/>. Anything
    /// else the map put on that cell stays baked, and the count of cells that did
    /// NOT match is reported as <see cref="MissedBuildingCells"/> — if a reading
    /// were wrong, that number would not be zero.</para>
    /// </summary>
    /// <summary>
    /// ⭐⭐⭐ 24.08.2026 — <b>DIE KULISSENBAUTEN GEHOEREN IN DIE AUFRAGENDE
    /// SCHICHT, NICHT INS GELAENDEBILD.</b>
    ///
    /// <para>Gemeldet: »ich sehe schon Gebaeude im Fog of War trotzdem«, und
    /// belegt mit einem Bild aus dem Let's Play (8:53): im unerkundeten Gebiet
    /// ist NICHTS als der blanke Boden — kein Baum, keine Kiste, keine Fabrik,
    /// nur ein Kran statt zwei.</para>
    ///
    /// <para>Der Nebelriegel an den Gebaeudesaetzen half nicht, und die Messung
    /// sagte warum: <c>fog-verborgen: 4 von 4 Gebaeuden</c> — sie WAREN
    /// verborgen, das Bauwerk stand trotzdem da. Es steckt im gebackenen
    /// Kartenbild, weil <c>b.IsBuilt == 0</c> hier bisher schlicht uebersprungen
    /// wurde und die Zellen damit ganz normal ins Gelaende geblittet
    /// wurden.</para>
    ///
    /// <para>Ein solcher Bau ist aber kein Boden: er ragt auf, verdeckt
    /// Einheiten und muss im Nebel verschwinden koennen. Er kommt darum in die
    /// zweite Ebene, wie ein Baum. <see cref="Kulisse"/> haelt seine
    /// Zellen.</para>
    /// </summary>
    private bool[] Kulisse = System.Array.Empty<bool>();

    /// <summary>Je Kulissenzelle die VORDERSTE Zeile ihres Bauwerks, sonst -1.
    ///
    /// <para>⚠⚠ 25.08.2026, gemeldet als »da gibt es wieder ein Kaestchen an dem
    /// Gebaeudetyp, der Einheiten verdeckt«. Eine Kulisse kam kachelweise ins
    /// Zeilenfach IHRER EIGENEN Zelle - das Hallendach ragt aber ueber mehrere
    /// Zellen nach oben, seine oberen Kacheln landeten in weit hinteren Faechern
    /// und uebermalten alles, was davor stand. Ein echtes Gebaeude macht es
    /// anders: es kommt als GANZES ins Fach seiner Tuerzeile
    /// (MapEntityLayer.BuildingDrawRowFor). Fuer eine Kulisse gibt es keine
    /// Tuer, also nimmt sie ihre vorderste Grundrisszeile - dasselbe
    /// Ergebnis.</para></summary>
    private int[] KulisseFach = System.Array.Empty<int>();

    private bool[] BuildingCells(ushort[] code)
    {
        int w = Width, h = Height;
        var claimed = new bool[w * h];
        Kulisse = new bool[w * h];
        KulisseFach = new int[w * h];
        for (int i = 0; i < KulisseFach.Length; i++) KulisseFach[i] = -1;
        BuildingCellsSkipped = MissedBuildingCells = 0;
        if (!_tiles.HasBuildings) return claimed;

        foreach (var b in CwmData.Buildings(_map))
        {
            // ⚠ Kulisse (IsBuilt == 0) wird NICHT beansprucht — der Zeichner
            // stellt sie nicht her —, aber ihre Zellen werden gemerkt, damit
            // der Backofen sie in die zweite Ebene legt statt ins Gelaende.
            if (b.IsBuilt == 0)
            {
                var kt = _tiles.GetBuildingType(b.Type);
                if (kt.IsEmpty) continue;
                for (int x = 0; x < CwpFile.PatternWidth; x++)
                    for (int y = 0; y < CwpFile.PatternHeight; y++)
                    {
                        int t = _tiles.PatternTile(kt.FirstPattern, x, y);
                        if (t == 0) continue;
                        int c = b.Col + x, r = b.Row + y;
                        if (c < 0 || c >= w || r < 0 || r >= h) continue;
                        int i = r * w + c;
                        if (code[i] == t + CwpFile.ObjectCodeBase) Kulisse[i] = true;
                    }
                // Die vorderste Zeile dieses Bauwerks - erst jetzt bekannt,
                // darum ein zweiter Durchgang ueber dieselben Zellen.
                int vorn = -1;
                for (int x = 0; x < CwpFile.PatternWidth; x++)
                    for (int y = 0; y < CwpFile.PatternHeight; y++)
                    {
                        int c2 = b.Col + x, r2 = b.Row + y;
                        if (c2 < 0 || c2 >= w || r2 < 0 || r2 >= h) continue;
                        if (Kulisse[r2 * w + c2] && r2 > vorn) vorn = r2;
                    }
                if (vorn >= 0)
                    for (int x = 0; x < CwpFile.PatternWidth; x++)
                        for (int y = 0; y < CwpFile.PatternHeight; y++)
                        {
                            int c2 = b.Col + x, r2 = b.Row + y;
                            if (c2 < 0 || c2 >= w || r2 < 0 || r2 >= h) continue;
                            int i2 = r2 * w + c2;
                            if (Kulisse[i2]) KulisseFach[i2] = vorn;
                        }
                continue;
            }
            var bt = _tiles.GetBuildingType(b.Type);
            if (bt.IsEmpty) continue;
            for (int x = 0; x < CwpFile.PatternWidth; x++)
                for (int y = 0; y < CwpFile.PatternHeight; y++)
                {
                    int t = _tiles.PatternTile(bt.FirstPattern, x, y);
                    if (t == 0) continue;
                    int c = b.Col + x, r = b.Row + y;
                    if (c < 0 || c >= w || r < 0 || r >= h) continue;
                    int i = r * w + c;
                    if (code[i] == t + CwpFile.ObjectCodeBase)
                    { claimed[i] = true; BuildingCellsSkipped++; }
                    else MissedBuildingCells++;
                }
        }
        return claimed;
    }

    /// <summary>
    /// The pixels the last bake did NOT draw because a building stands there —
    /// one flag per pixel of the finished picture, or null when nothing was
    /// skipped.
    ///
    /// <para>Why a pixel mask and not the cell list: a building tile is far
    /// taller than its cell and hangs upwards over its neighbours, so "which
    /// cells belong to a building" does not say which pixels changed. This is
    /// filled by <see cref="MarkSkipped"/>, which walks exactly the pixels
    /// <see cref="Blit"/> would have written.</para>
    ///
    /// <para>Who needs it: <c>selftest-bake</c> holds our picture against the
    /// Python reference, and that reference still BAKES its buildings. Without
    /// this mask the test can only say "4 % of the pixels differ" and not
    /// whether the ground underneath is still right.</para>
    /// </summary>
    public bool[]? SkippedPixels { get; private set; }

    private void MarkSkipped(Sprite? s, int col, int row, int elev)
    {
        if (s == null) return;
        SkippedPixels ??= new bool[PixelW * PixelH];
        int sx = col * TileW;
        int sy = OriginY + row * TileH - elev * ElevStep + BlitAnchor + s.YOff;
        for (int y = 0; y < s.H; y++)
        {
            int dy = sy + y;
            if (dy < 0 || dy >= PixelH) continue;
            int srcRow = y * s.W * 4;
            for (int x = 0; x < s.W; x++)
            {
                if (s.Rgba[srcRow + x * 4 + 3] == 0) continue;   // transparent
                int dx = sx + x;
                if (dx < 0 || dx >= PixelW) continue;
                SkippedPixels[dy * PixelW + dx] = true;
            }
        }
    }

    private Sprite? Make(CwpFile.Frame? f)
    {
        if (f == null || f.IsEmpty) return null;
        var s = new Sprite { W = f.Width, H = f.Height, YOff = f.YOffset };
        s.Rgba = new byte[f.Width * f.Height * 4];
        int opaque = 0;
        for (int i = 0; i < f.Width * f.Height; i++)
        {
            if (!f.Opaque[i]) continue;
            byte p = f.Pixels[i];
            s.Rgba[i * 4 + 0] = _pal.R[p];
            s.Rgba[i * 4 + 1] = _pal.G[p];
            s.Rgba[i * 4 + 2] = _pal.B[p];
            s.Rgba[i * 4 + 3] = 255;
            opaque++;
        }
        double cov = 100.0 * opaque / (f.Width * f.Height);
        s.Full = cov >= 99.0 && f.Width >= TileW && f.Height >= 18 && f.Height <= 22;
        return s;
    }

    private Sprite? Frame(int i)
    {
        if (_frame.TryGetValue(i, out var s)) return s;
        s = i >= 0 && i < _tiles.FrameCount ? Make(_tiles.DecodeFrame(i)) : null;
        _frame[i] = s;
        return s;
    }

    /// <summary>
    /// <b>⭐⭐⭐ DIE BODENSYNTHESE DES ORIGINALS</b> — <c>0x41FA10</c>, die Kachel,
    /// die das Original in seine BEKANNTE Karte schreibt, solange eine Zelle
    /// nicht erkundet ist.
    ///
    /// <para>Das Original führt zwei Kachelkarten: die WAHRE (<c>0x677E20</c>,
    /// aus sec1) und die BEKANNTE (<c>0x5539D0</c> = sec51, 256×256 Wörter,
    /// spaltenweise) — und <b>der Zeichner liest nur die bekannte</b>
    /// (<c>0x401410</c> → <c>0x41D0C0</c>). Beim Missionsstart füllt
    /// <c>0x41FAE0</c> sie: Lagenbyte &lt; 100 → diese Synthese, sonst die wahre
    /// Kachel. Ein unerkundeter Waldeintrag ist dort schlicht <b>Gras</b> — darum
    /// braucht der Zeichner gar keinen Sichttest, und darum ist die BRÜCKE
    /// (Lage 101) im Nebel sichtbar, ein Baum aber nicht. Beides genau so auf
    /// seinem Let's-Play-Standbild.</para>
    ///
    /// <para><b>Die Rechnung, ganz gelesen</b> (<c>0x401631</c> → <c>0x4ACDE0</c>):</para>
    /// <code>
    ///   idx = Spalte*257 + Zeile                 ; das KNOTENgitter = sec2
    ///   a=Ecke[idx]  b=Ecke[idx+257]  c=Ecke[idx+1]  d=Ecke[idx+258]
    ///   m = min(a,b,c,d)                         ; die Geländeklasse
    ///   Muster = 1000(a−m) + 100(b−m) + 10(c−m) + (d−m)
    ///   M      = Index von Muster in der 16er-Tafel (@0x4F89F8)
    ///   Index  = 19 * (15*(m + (M!=0 ? 4 : 0)) + M) + Schrägenart
    ///   Satz   = CwpFile.Bodenvariante(Index)
    ///   Kachel = Basis + (Zufall mod Anzahl);  Anzahl 0 → nichts
    /// </code>
    ///
    /// <para>⚠ Die Ecken sitzen im KNOTENgitter, nicht zellweise: sec2 ist
    /// <b>257×257</b> Byte (0x10201), nicht Breite×Höhe.</para>
    ///
    /// <para><b>Gegengeprobt, bevor eine Zeile Zeichner angefasst wurde:</b> auf
    /// einer Zelle ohne Objekt und mit Lage &lt; 100 ist die bekannte Karte
    /// gleich der wahren, die Synthese MUSS also im Variantenbereich der echten
    /// Kachel landen. Über alle gelieferten Karten und <b>20 Kachelsätze:
    /// 426.827 richtig, 36 daneben — 99,992 %</b> (map_02 allein 4030/4030).
    /// ⚠ Die 36 (1 auf map_01, 35 auf NET05) sind noch nicht eingeordnet.</para>
    ///
    /// <para>⚠ Der Zufall ist hier an die ZELLE gebunden, nicht an einen Lauf:
    /// ein Backofen muss zweimal dasselbe Bild liefern, sonst ist kein
    /// Bildvergleich mehr möglich.</para>
    /// </summary>
    private static readonly int[] Eckenmuster =
        { 0, 101, 11, 1010, 1100, 1, 10, 1000, 100, 111, 1011, 1110, 1101, 110, 1001, 1111 };

    /// <summary>Wieviele Zellen eine Synthese bekommen haben und wie oft die
    /// Tafel nichts hergab. Ohne die zweite Zahl sähe ein leerer Boden aus wie
    /// ein richtiger.</summary>
    public int SyntheseZellen, SyntheseLeer, SyntheseBoden;

    /// <summary>
    /// <b>⭐⭐⭐ DIE NEBELDECKE — Zellen, deren WAHRE Kachel nicht die ist, die das
    /// Original im unerkundeten Gebiet zeigt.</b>
    ///
    /// <para>Gemeldet, nachdem Objekte und Gebäude im Nebel richtig
    /// verschwanden: es blieben »füllstellen« stehen — auf seinem Bild liest
    /// man den ganzen Grundriss der Gegnerbasis an ihren Betonplatten und
    /// Strassen ab. Der Grund: unser Kartenbild trägt die WAHRE Kachel, das
    /// Original zeigt bis zum Aufdecken die synthetisierte.</para>
    ///
    /// <para>Gemessen an map_02: von 5427 Zellen (ohne Lagenbyte ≥ 100) liegen
    /// <b>4030</b> in der Variantenfamilie ihrer Synthese — dort sieht man
    /// keinen Unterschied — und <b>1397</b> weichen ab. Sie tragen allesamt
    /// Codes ≥ 10000, also Bodendekor aus der dir2-Bank: genau die Platten und
    /// Wege. Nur diese Zellen brauchen eine Decke.</para>
    ///
    /// <para>⚠ Zellen mit Lagenbyte ≥ 100 bleiben aussen vor — das Original
    /// schreibt für sie von Anfang an die wahre Kachel in die bekannte Karte
    /// (@0x41FB28), und darum ist die BRÜCKE im Nebel zu sehen.</para>
    ///
    /// <para>Je Eintrag: die Zelle, wohin die Kachel gehört, und ihr Platz im
    /// Streifen (<see cref="BurntAtlas"/>).</para></summary>
    public readonly List<(int Col, int Row, int X, int Y, int Slot)> NebelBoden = new();

    private int SyntheseKachel(int col, int row)
    {
        var kn = _map.Sec(2);
        if (kn == null || kn.Length < 257 * 257) return -1;
        int i = col * 257 + row;
        if (i + 258 >= kn.Length) return -1;
        int a = kn[i], b = kn[i + 257], c = kn[i + 1], d = kn[i + 258];
        int m = Math.Min(Math.Min(a, b), Math.Min(c, d));
        int muster = 1000 * (a - m) + 100 * (b - m) + 10 * (c - m) + (d - m);
        int mi = Array.IndexOf(Eckenmuster, muster);
        if (mi < 0) mi = 16;
        int idx = 19 * (15 * (m + (mi != 0 ? 4 : 0)) + mi) + _map.FlagAt(col, row);
        var (basis, anzahl) = _tiles.Bodenvariante(idx);
        if (anzahl <= 0) { SyntheseLeer++; return -1; }
        SyntheseZellen++;
        // ⚠ zellgebunden, damit derselbe Backofen zweimal dasselbe Bild liefert
        int wurf = (col * 73856093 ^ row * 19349663) & 0x7FFFFFFF;
        return basis + wurf % anzahl;
    }

    private Sprite? ObjectSprite(int code)
    {
        if (_object.TryGetValue(code, out var s)) return s;
        s = null;
        try { s = Make(_tiles.DecodeObject(code)); }
        catch (ArgumentOutOfRangeException) { }
        _object[code] = s;
        return s;
    }

    // ---- the canvas --------------------------------------------------------

    private byte[] _canvas = Array.Empty<byte>();

    /// <summary>
    /// <b>WIE HOCH RAGT EIN OBJEKT ÜBER SEINE ZELLE?</b> — die Messung, aus der
    /// die Schwelle für »verdeckt Einheiten« kommen muss.
    ///
    /// <para>Anlass: gemeldet als »im Original verdecken z. B. auch Bäume
    /// Einheiten, bei uns nicht«. Der Grund steht in <see cref="Bake"/>,
    /// Durchgang C: Objekte werden ins Kartenbild <b>eingebacken</b> und liegen
    /// damit unter allem; nur GEBÄUDE sind ausgenommen und werden lebend
    /// gezeichnet. Ein eingebackener Baum <i>kann</i> nichts verdecken.</para>
    ///
    /// <para>Für Gebäudekacheln gibt es die gemessene Schwelle
    /// <c>MapEntityLayer.FlachBisPx = 25</c> — flach bleibt im Boden, Aufragendes
    /// kommt ins Zeilenfach. Für Objekte fehlt die entsprechende Zahl, und
    /// geraten wird sie nicht. Diese Zeile zählt aus, wie weit jedes
    /// vorkommende Objekt über die UNTERKANTE seiner Zelle hinausragt; die
    /// Schwelle gehört dann in eine Lücke der Verteilung, wie bei den
    /// Gebäudekacheln auch.</para>
    ///
    /// <para>Gerechnet wie in <see cref="Blit"/>: das Sprite beginnt bei
    /// <c>row·TileH + BlitAnchor + YOff</c> gegen den Zellursprung, die Zelle
    /// endet bei <c>row·TileH + TileH</c>. Der Überstand nach oben ist also
    /// <c>TileH − (BlitAnchor + YOff)</c> … minus dem, was das Sprite selbst
    /// nach unten reicht — hier gezählt wird die <b>sichtbare Höhe über der
    /// Zellunterkante</b>: <c>(BlitAnchor + YOff + H) </c> ist die Unterkante des
    /// Sprites, und <c>TileH</c> die der Zelle.</para></summary>
    /// <returns>Je Objektcode: Breite, Höhe, YOff und der Überstand über der
    /// Zellunterkante, dazu wie oft der Code auf dieser Karte vorkommt.</returns>
    public List<(int Code, int W, int H, int YOff, int Rise, int Count)> ObjectHeights()
    {
        var zahl = new Dictionary<int, int>();
        var rec = _map.Records;
        for (int i = 0; i < Width * Height; i++)
        {
            int c = BitConverter.ToUInt16(rec, i * 4);
            if (c >= GroundMax) zahl[c] = zahl.TryGetValue(c, out int n) ? n + 1 : 1;
        }

        var raus = new List<(int, int, int, int, int, int)>();
        foreach (var kv in zahl)
        {
            var s = ObjectSprite(kv.Key);
            if (s == null) continue;
            int oben = BlitAnchor + s.YOff;           // Oberkante gegen den Zellursprung
            int rise = TileH - oben;                  // sichtbare Hoehe ueber der Zellunterkante
            raus.Add((kv.Key, s.W, s.H, s.YOff, rise, kv.Value));
        }
        raus.Sort((a, b) => a.Item5 != b.Item5 ? a.Item5 - b.Item5 : a.Item1 - b.Item1);
        return raus;
    }

    /// <summary>
    /// <b>AB WANN RAGT EIN OBJEKT AUF</b> — also ab wann es eine Einheit
    /// verdecken kann und darum nicht mehr in den Boden gebacken werden darf.
    ///
    /// <para>⚠⚠ <b>18.08.2026 — DIE FRAGE WAR FALSCH GESTELLT.</b> Hier stand
    /// bis heute <c>RagtAbPx = 25</c>, und der Kommentar sagte selbst, dass die
    /// 25 von den GEBÄUDEkacheln <b>übernommen</b> und nicht gemessen war. Die
    /// Messung, die sie hätte tragen sollen, gibt es auch gar nicht: über 36
    /// Karten und 13.491 Objektbilder sitzt bei 20 px der grösste Haufen, und
    /// <b>darüber läuft die Verteilung ohne Lücke bis 70 px durch</b> (siehe
    /// <see cref="ObjectHeights"/>). Eine Lücke, in die eine Schwelle gehört,
    /// ist da nicht.</para>
    ///
    /// <para><b>Weil das Original gar keine Höhe fragt.</b> Sein Zeichner
    /// (@0x4B4150) teilt die Zellen nach der BELEGUNGSKARTE auf: der flache
    /// Durchgang @0x4B41EB überspringt jede Zelle ab Belegung 14000, und der
    /// verzahnte Durchgang @0x4B43BB — der, in dem Einheiten und Kacheln
    /// zeilenweise abwechseln — nimmt ausdrücklich die Belegungen
    /// <b>50000..63999</b> (@0x4B446C). Das ist die ganze Regel. Sie steht
    /// samt Adressen bei <see cref="MapForest.ImZeilenfach"/>.</para>
    ///
    /// <para>Gemessen, was der Wechsel ausmacht: von 68.391 Objektzellen der 23
    /// mitgelieferten <c>.CWM</c> kommen jetzt <b>38.306</b> ins Fach (37.231
    /// Wald + 1.075 Objekt) statt der bisherigen Schätzung — und 14.710 Zellen,
    /// die nur Bodenschmuck sind, bleiben im Bild, wo sie hingehören.</para>
    ///
    /// <para>⚠ <see cref="ObjectHeights"/> bleibt stehen. Nicht als Schwelle,
    /// sondern als Prüfstand: er zeigt, dass die Höhe die Frage NICHT
    /// beantworten kann.</para></summary>

    /// <summary>Die zweite Ebene: nur die aufragenden Objekte, alles andere
    /// durchsichtig. <c>null</c>, solange keines vorkam.</summary>
    private byte[]? _objects;

    /// <summary>Wo die aufragenden Objekte in der zweiten Ebene liegen — je
    /// Eintrag die Zelle und das Rechteck im Bild. Der Zeichner braucht beides:
    /// die Zelle für das Zeilenfach, das Rechteck zum Ausschneiden.
    ///
    /// <para><c>Kohle</c> ist der Platz der VERKOHLTEN Fassung im Streifen
    /// (<see cref="BurntAtlas"/>) oder −1, wenn die Zelle kein Wald ist;
    /// <c>KX/KY</c> ist, wo sie hin muss — sie hat einen eigenen Anschlag,
    /// weil der verkohlte Baum ein anderes Bild ist als der grüne.
    /// <c>Asche</c> und <c>AX/AY</c> sind dasselbe für die Kachel, die
    /// ÜBRIGBLEIBT, wenn das Feuer aus ist (Stumpf bzw. blanker Boden).</para>
    /// </summary>
    /// <para>⭐⭐ 24.08.2026 — <c>Imap</c> und <c>Code</c> kamen dazu. Das
    /// Original fuehrt zerstoerbare Kartenobjekte in einer EIGENEN Brandliste
    /// (0xC03A30, 6 Byte je Eintrag) mit einer ART, und die Art entscheidet
    /// ueber die Verhaltensklasse (Arttafel 0xBB3B60) und ueber die
    /// Ersatzkachel: <b>Grundkachel + 10001 = brennt</b>, <b>+ 10002 =
    /// zerstoert</b>. Unser Objektsatz trug bisher nur Lage und Bilder — damit
    /// war das ganze Teilsystem nicht baubar (siehe
    /// Rendering/BrennendeObjekte.cs).
    ///
    /// <para><c>Imap</c> ist der rohe Belegungswert aus Sektion 6: 50000..55999
    /// Wald, 61000..63999 zerstoerbares Objekt — daraus faellt die Art. <c>Code</c>
    /// ist die Kachelnummer derselben Zelle, auf die das Original die
    /// +10001/+10002 rechnet.</para></summary>
    /// <summary>
    /// <b>⭐⭐⭐ 27.08.2026 — <c>Eigen</c> ist neu, und es behebt den
    /// Brueckenfehler.</b>
    ///
    /// <para><c>X/Y/W/H</c> ist die Stelle im zusammengesetzten Bild
    /// <c>&lt;karte&gt;.objects.png</c> — und der Zeichner hat von dort ein
    /// RECHTECK ausgeschnitten und an dieselbe Stelle gemalt. Das geht schief,
    /// sobald sich zwei aufragende Kacheln ueberlappen: das Rechteck der einen
    /// enthaelt dann Bildpunkte der anderen und malt sie ein ZWEITES Mal — zu
    /// einem spaeteren Zeitpunkt, naemlich nach den Einheiten dazwischen.</para>
    ///
    /// <para><b>Gemessen an map_02, der gemeldeten Bruecke:</b> das Gelaender
    /// der Zeile 22 liegt bei y 587…624 (h 38), das der Zeile 24 bei y 617…664
    /// (h 48) — <b>acht Zeilen Ueberlappung</b>. Genau diese acht Zeilen sind
    /// im Bake in allen drei Zellen <b>40 von 40 Bildpunkten voll deckend</b>,
    /// ab der neunten faellt es auf 4/12/4 — das sind erst die echten Streben.
    /// Der volle Block ist die Unterkante der Zeile-22-Kachel, im Original
    /// laengst gemalt, bei uns mit dem Zeile-24-Rechteck ueber die Fahrzeuge
    /// der Zeile 23 gelegt. Sein Bild: »Beide Fahrzeuge werden teilweise von
    /// der Brueckenstrasse verdeckt.«</para>
    ///
    /// <para>⚠ Die ZEICHENREIHENFOLGE war nie der Fehler — sie stimmt mit dem
    /// verzahnten Durchgang <c>@0x4B43BB</c> ueberein (je Zeile erst die
    /// Einheiten, dann die aufragenden Kacheln). Falsch war die
    /// ZUGEHOERIGKEIT der Bildpunkte.</para>
    ///
    /// <para><c>Eigen</c> ist darum der Platz der EIGENEN Kachel im Streifen
    /// (<see cref="BurntAtlas"/>), genau wie <c>Kohle</c> und <c>Asche</c> es
    /// laengst sind: Quelle aus dem Streifen, Ziel an der Zelle. Damit kann
    /// kein fremder Bildpunkt mehr mitkommen. Rueckfall:
    /// <c>--objekt-rechteck</c>.</para>
    /// </summary>
    public readonly List<(int Col, int Row, int X, int Y, int W, int H,
                          int Kohle, int KX, int KY,
                          int Asche, int AX, int AY,
                          int Imap, int Code, int Art,
                          int Klasse, int Basis, int Eigen, int Boden, int Lage)> Objects = new();

    /// <summary>
    /// <b>DER STREIFEN MIT DEN VERKOHLTEN BÄUMEN.</b>
    ///
    /// <para>Ein brennender Baum ist im Original keine Zutat, sondern ein
    /// KACHELTAUSCH: <c>zapal</c> @0x4CACE5 schreibt die verkohlte Fassung an
    /// die Stelle des grünen Baums (Rechnung und Adressen bei
    /// <see cref="MapForest.Verkohlt"/>). Diese Kachel muss also im Bild
    /// liegen, bevor irgendetwas brennt.</para>
    ///
    /// <para><b>Warum ein Streifen und keine dritte Ebene:</b> beide Fassungen
    /// hängen nur an der Baumart (0, 19 oder 38) und der Geländeart (0..18) —
    /// also höchstens <b>2 × 57</b> verschiedene Bilder je Karte, egal
    /// wie viele Bäume darauf stehen. Eine ganze zweite Leinwand dafür wäre bei
    /// map_01 rund 10 MB für 563 Zellen. Der Streifen hängt darum UNTEN an
    /// <c>&lt;karte&gt;.objects.png</c> an; seine Y-Werte liegen ab
    /// <see cref="PixelH"/>.</para>
    ///
    /// <para>Je Eintrag: der Kachelcode und sein Rechteck im Streifen, dazu
    /// <c>YOff</c> — der Zeichner braucht ihn, um die Kachel gegen die Zelle zu
    /// setzen, und der ist bei der verkohlten Fassung ein anderer als beim
    /// grünen Baum.</para></summary>
    public readonly List<(int Code, int X, int Y, int W, int H, int YOff)> BurntAtlas = new();

    private byte[]? _burnt;                 // der Streifen, PixelW x _burntH
    private int _burntH;

    /// <summary>Die zweite Ebene als Bild, oder <c>null</c>, wenn kein Objekt
    /// aufragte. ⚠ Erst nach <see cref="Bake"/> gefüllt. Ist ein Streifen mit
    /// verkohlten Bäumen entstanden, hängt er unten an.</summary>
    public Image? ObjectLayer()
    {
        if (_objects == null) return null;
        if (_burnt == null || _burntH <= 0)
            return Image.CreateFromData(PixelW, PixelH, false, Image.Format.Rgba8, _objects);
        var alles = new byte[PixelW * (PixelH + _burntH) * 4];
        Array.Copy(_objects, alles, _objects.Length);
        Array.Copy(_burnt, 0, alles, _objects.Length, _burnt.Length);
        return Image.CreateFromData(PixelW, PixelH + _burntH, false, Image.Format.Rgba8, alles);
    }

    /// <summary>Wie <see cref="Blit"/>, aber auf eine übergebene Leinwand — und
    /// ⚠ mit DURCHSICHTIGKEIT: die zweite Ebene liegt über dem Kartenbild, ein
    /// undurchsichtiger Rand würde den Boden ringsum ausstanzen.</summary>
    private void BlitTo(byte[] dst, Sprite? s, int col, int row, int elev)
    {
        if (s == null) return;
        int sx = col * TileW;
        int sy = OriginY + row * TileH - elev * ElevStep + BlitAnchor + s.YOff;
        for (int y = 0; y < s.H; y++)
        {
            int dy = sy + y;
            if (dy < 0 || dy >= PixelH) continue;
            int srcRow = y * s.W * 4;
            int dstRow = dy * PixelW * 4;
            for (int x = 0; x < s.W; x++)
            {
                if (s.Rgba[srcRow + x * 4 + 3] == 0) continue;
                int dx = sx + x;
                if (dx < 0 || dx >= PixelW) continue;
                int d = dstRow + dx * 4, o = srcRow + x * 4;
                dst[d] = s.Rgba[o];
                dst[d + 1] = s.Rgba[o + 1];
                dst[d + 2] = s.Rgba[o + 2];
                dst[d + 3] = 255;
            }
        }
    }

    private void Blit(Sprite? s, int col, int row, int elev)
    {
        if (s == null) return;
        int sx = col * TileW;
        int sy = OriginY + row * TileH - elev * ElevStep + BlitAnchor + s.YOff;
        for (int y = 0; y < s.H; y++)
        {
            int dy = sy + y;
            if (dy < 0 || dy >= PixelH) continue;
            int srcRow = y * s.W * 4;
            int dstRow = dy * PixelW * 4;
            for (int x = 0; x < s.W; x++)
            {
                if (s.Rgba[srcRow + x * 4 + 3] == 0) continue;   // transparent
                int dx = sx + x;
                if (dx < 0 || dx >= PixelW) continue;
                int d = dstRow + dx * 4, o = srcRow + x * 4;
                _canvas[d] = s.Rgba[o];
                _canvas[d + 1] = s.Rgba[o + 1];
                _canvas[d + 2] = s.Rgba[o + 2];
                _canvas[d + 3] = 255;
            }
        }
    }

    /// <summary>Every cell gets the nearest tile that really fills it, so the
    /// bake has an opaque floor under coasts, transitions and object cells.</summary>
    private int[] BuildBase(ushort[] code, byte[] elev)
    {
        int w = Width, h = Height;
        var grid = new int[w * h];
        for (int i = 0; i < w * h; i++)
        {
            grid[i] = -1;
            if (code[i] < GroundMax && Frame(code[i])?.Full == true) grid[i] = code[i];
        }
        var holes = new Queue<int>();
        for (int i = 0; i < w * h; i++) if (grid[i] < 0) holes.Enqueue(i);
        int guard = 0, limit = 200000 + w * h * 4;
        int[] dc = { 1, -1, 0, 0, 1, -1, 1, -1 };
        int[] dr = { 0, 0, 1, -1, 1, -1, -1, 1 };
        while (holes.Count > 0 && guard++ < limit)
        {
            int i = holes.Dequeue();
            if (grid[i] >= 0) continue;
            int c = i % w, r = i / w, found = -1;
            // ⭐ 25.08.2026 - DIE MEHRHEIT DER NACHBARN, nicht der erste.
            //
            // Hier stand »nimm den ersten Nachbarn, der schon Boden hat«, und
            // die Reihenfolge ist fest (rechts, links, unten, oben, dann die
            // Diagonalen). Unter einer Halle auf einem BETONplatz griff damit
            // regelmaessig das Gras vom Rand: gemeldet als Flickenteppich, wo
            // Beton liegen muesste, sichtbar sobald die Kulissenbauten in die
            // zweite Ebene wandern. Dieselbe Ursache wie beim fehlenden Fluss
            // unter der Bruecke - eine geratene Fuellung.
            //
            // Die Mehrheit ist keine Messung des Originals, sondern die bessere
            // Schaetzung: eine 3x2-Halle hat rundum Beton und nur an einer Ecke
            // Gras. Bei Gleichstand gewinnt der erste - das ist der alte Weg.
            var stimmen = new Dictionary<int, int>();
            for (int k = 0; k < 8; k++)
            {
                int nc = c + dc[k], nr = r + dr[k];
                if (nc < 0 || nc >= w || nr < 0 || nr >= h) continue;
                int v = grid[nr * w + nc];
                if (v < 0) continue;
                stimmen[v] = stimmen.GetValueOrDefault(v) + 1;
                if (found < 0) found = v;
            }
            if (stimmen.Count > 1)
            {
                int best = found, bestN = 0;
                foreach (var kv in stimmen)
                    if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
                found = best;
            }
            if (found < 0) holes.Enqueue(i);
            else grid[i] = found;
        }
        return grid;
    }

    /// <summary>Bake, and hand back the finished picture.</summary>
    public Image Bake(bool fill = true, bool objects = true)
    {
        int w = Width, h = Height;
        var rec = _map.Records;
        var code = new ushort[w * h];
        var elev = new byte[w * h];
        var flag = new byte[w * h];
        int maxElev = 0;
        for (int i = 0; i < w * h; i++)
        {
            int o = i * 4;
            code[i] = BitConverter.ToUInt16(rec, o);
            elev[i] = rec[o + 2];
            flag[i] = rec[o + 3];
            if (elev[i] > maxElev) maxElev = elev[i];
        }

        OriginY = maxElev * ElevStep + 60;
        PixelW = w * TileW;
        PixelH = h * TileH + OriginY + 40;
        _canvas = new byte[PixelW * PixelH * 4];

        var basis = fill ? BuildBase(code, elev) : null;

        // pass 0 — the elevation skirt
        if (basis != null)
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    if (basis[i] < 0) continue;
                    var s = Frame(basis[i]);
                    for (int lvl = 0; lvl <= elev[i]; lvl++) Blit(s, c, r, lvl);
                }

        // ⚠ 28.08.2026 NACH VORN GEZOGEN: der Streifen wird schon im
        //   Durchgang A/B gebraucht (die NEBELDECKE, siehe NebelBoden).
        //   Die Reihenfolge im Streifen aendert sich dadurch nicht --
        //   Streifenplatz vergibt die Plaetze in Aufrufreihenfolge, und
        //   die Gegenprobe ist die Zahl der Eintraege je Karte.
        var kohleSlot = new Dictionary<int, int>();
        var kohleSpr = new List<Sprite>();

        // ⚠⚠ 28.08.2026 NACH VORN: die Clear-Aufrufe standen hinter Durchgang
        //   A/B, und seit die NEBELDECKE dort schon Streifenplaetze vergibt,
        //   loeschten sie genau die Eintraege wieder, die eben angelegt
        //   worden waren — `kohleSlot` behielt die Nummern, `BurntAtlas` war
        //   leer. Sichtbar wurde es daran, dass `boden` aus dem JSON
        //   verschwand, OHNE dass eine Zahl im Bericht sich ruehrte.
        Objects.Clear();
        BurntAtlas.Clear();
        NebelBoden.Clear();

        // Eine Ersatzkachel in den Streifen legen — oder ihren Platz
        // wiederfinden, denn dieselben paar Codes kommen tausendfach vor.
        // −1, wenn der Kachelsatz sie nicht hat; dann bleibt der Baum eben
        // stehen, statt dass ein Loch entsteht.
        int Streifenplatz(int tileCode)
        {
            if (kohleSlot.TryGetValue(tileCode, out int slot)) return slot;
            // ⚠ Die Bildbank waehlt der CODE, wie im Original @0x4B428F:
            //   unter 10000 die Bodenbank, darueber die Objektbank. Vorher
            //   stand hier nur ObjectSprite — damit lieferte jede
            //   SYNTHETISIERTE Bodenkachel unter 10000 stillschweigend -1.
            var ks = tileCode >= CwpFile.ObjectCodeBase ? ObjectSprite(tileCode)
                                                        : Frame(tileCode);
            if (ks == null) return -1;
            slot = kohleSpr.Count;
            kohleSlot[tileCode] = slot;
            kohleSpr.Add(ks);
            BurntAtlas.Add((tileCode, 0, 0, ks.W, ks.H, ks.YOff));
            return slot;
        }

        // passes A and B — backdrop, then the cell's own detail
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int i = r * w + c;
                bool isObj = code[i] >= GroundMax;
                // ⚠⚠ 25.08.2026 - BEFAHRBARE BAUWERKE WERDEN FLACH GEMALT.
                // Gemeldet: unter unserer Bruecke fehlte der FLUSS, darunter lag
                // Gras/Stein. Der Grund: eine Objektzelle bekommt hier nie ihre
                // eigene Kachel, sondern nur die `basis`-Fuellung - und die ist
                // eine FLUTFUELLUNG von den acht Nachbarn (BuildBase). Ueber
                // Wasser greift sie das Ufer.
                //
                // Eine Bruecke/Mole (Lagenbyte 100+n) oder Rampe (200+n) ist
                // aber kein Baum, hinter dem man steht, sondern etwas, worauf
                // man FAEHRT. Ihre Kachel gehoert ins Kartenbild - dann steht
                // das Wasser darunter wie im Original, das Gelaender ragt wie
                // gehabt in die Zeile darueber, und eine Einheit darauf wird
                // danach gezeichnet statt darunter zu verschwinden.
                // GG 26.08.2026 - DIE TRENNUNG KOMMT AUS DER BELEGUNG.
                // Hier stand `Lage(...) >= 100`, also das Lagenbyte - damit
                // wurden Fahrbahn UND Gelaender flach gebacken, und das
                // Gelaender ragte nicht mehr auf. Das Original entscheidet am
                // imap: 0xFFFE (Fahrbahn) kommt in den flachen Durchgang,
                // 0xFFFF (Gelaender) mit Lagenbyte >= 99 wird dort
                // uebersprungen und gehoert ins Zeilenfach.
                // Herleitung Befehl fuer Befehl: MapForest.ImFlachenDurchgang.
                int imapC = MapForest.Imap(_map, c, r);
                int lageC = MapForest.Lage(_map, c, r);
                bool befahrbar = MapForest.ImFlachenDurchgang(imapC, lageC)
                                 && lageC >= 100;
                if (befahrbar) BefahrbarFlach++;
                if (imapC == 0xFFFF && lageC >= 100) GelaenderAufragend++;
                // ⭐⭐⭐ 28.08.2026 — DAS ORIGINAL MALT DIE EIGENE KACHEL,
                // AUCH WENN SIE EINE OBJEKTKACHEL IST.
                //
                // Durchgang 1 des Originals (@0x4B41EB) holt den Kachelcode mit
                // `0x401410` = `word[0x5539D0 + (spalte*256+zeile)*2]` — der
                // Laufzeit-Codetafel aus sec1 word[+0] — und malt ihn, sobald
                // die Durchgangsbedingung greift. OB der Code eine Objektkachel
                // ist, fragt es dabei NICHT; es waehlt nur die Bildbank:
                //     @0x4B428F  cmp ax, 0x2710 ; jge  -> dir2, sonst dir1
                // Genau diese Grenze ist unser CwpFile.ObjectCodeBase.
                //
                // Wir haben stattdessen `!isObj` verlangt und einer Objektzelle
                // nur die `basis`-Fuellung gegeben — eine FLUTFUELLUNG aus den
                // acht Nachbarn (BuildBase), also eine Erfindung. Was das
                // anrichtet, steht seit dem 24.08. in MapObjects: nimmt man das
                // Objekt weg, kommen »rechteckige Wasser- und Felsflecken quer
                // ueber die Wiese« zum Vorschein. Genau darum liess sich das
                // Ausblenden von Objekten im unerkundeten Nebel nicht bauen.
                //
                // GEMESSEN, vor dem Bau: Zellen, die das Original flach malt und
                // wir nicht — map_01 11, map_02 497, map_03 50, map_04 110,
                // map_05 72. Fast alle `imap 0xFFFE, Lage 0`.
                //
                // ⚠ Fuer WALDzellen (Lage 0, imap 50000..) greift die Bedingung
                // NICHT — dort malt auch das Original flach nichts, die
                // aufragende Kachel bringt ihren Boden selbst mit. Die
                // basis-Fuellung bleibt dort als Untergrund noetig.
                //
                // Rueckfall: --kein-objektboden.
                bool eigenFlach = !KeinObjektboden
                                  && MapForest.ImFlachenDurchgang(imapC, lageC);
                // ⭐⭐⭐ 28.08.2026 — DER HINTERGRUND IST DIE SYNTHESE, NICHT
                // UNSERE FLUTFUELLUNG.
                //
                // Gemeldet, nachdem die Objekte im Nebel richtig verschwanden:
                // »man sieht die gebaeude nicht, aber leere felder die nicht
                // sauber sind«. Genau so: unter einem ausgeblendeten Gebaeude
                // lag weiter `basis` — die Flutfuellung aus den acht Nachbarn
                // (BuildBase), unsere Erfindung. Sie ist der Behelf, den die
                // Bodensynthese des Originals ersetzt: 0x41FAE0 schreibt fuer
                // JEDE Zelle mit Lagenbyte < 100 die synthetisierte Kachel in
                // die bekannte Karte, ganz gleich was darauf steht.
                //
                // ⚠ Fuer eine Zelle, die ihr Objekt/Gebaeude spaeter ohnehin
                // deckt, ist das folgenlos — sichtbar wird es genau dann, wenn
                // etwas WEGGELASSEN wird. Darum ist es hier richtig und nicht
                // erst beim Zeichnen.
                //
                // Rueckfall: --flutfuellung (der Stand von vor dem 28.08.2026).
                // ⚠⚠ 28.08.2026 ZURUECKGENOMMEN, am selben Tag, an dem sie kam.
                //   Der Hintergrund ist wieder `basis`; die Synthese steht nur
                //   noch fuer die NEBELDECKE bereit (`--synthese-hintergrund`
                //   schaltet sie zurueck ein).
                //
                //   Gemeldet: »die stadt in kampagne 2 hast du zu felsgrafik
                //   gemacht als boden anstatt strasse/beton«. Nachgemessen:
                //   994 Zellen auf map_02 tragen einen Objektcode und bekommen
                //   ihre WAHRE Kachel nie gemalt — darunter die Gebaeudezellen
                //   der Stadt (Belegung 60001..60009). Der Hintergrund ist dort
                //   das Einzige, was zu sehen ist, und die Synthese liefert dort
                //   Fels statt der Strasse.
                //
                //   ⭐ Sie war ohnehin ueberfluessig geworden: was sie erreichen
                //   sollte — sauberer Boden unter einem im Nebel ausgeblendeten
                //   Gebaeude — macht seit 7868aa8 die NEBELDECKE, und die wirkt
                //   NUR im Nebel und nur auf gemessenen Zellen. Ein Eingriff ins
                //   Kartenbild, den man auch bei vollem Licht sieht, war der
                //   falsche Ort.
                int syntheseB = SyntheseHintergrund ? SyntheseKachel(c, r) : -1;
                if (syntheseB >= 0) SyntheseBoden++;
                int b = syntheseB >= 0 ? syntheseB : (basis != null ? basis[i] : -1);
                bool ownIsFull = !isObj && Frame(code[i])?.Full == true;
                if (b >= 0 && !ownIsFull)
                    Blit(b >= CwpFile.ObjectCodeBase ? ObjectSprite(b) : Frame(b), c, r, elev[i]);
                // ⭐⭐⭐ DIE NEBELDECKE. Weicht die wahre Kachel von der Synthese
                // ab, muss der Zeichner im unerkundeten Gebiet die Synthese
                // zeigen — siehe den Kopf von NebelBoden.
                // ⚠ Die NEBELDECKE haengt NICHT am Hintergrundschalter — sie
                //   rechnet ihre Synthese selbst.
                int syntheseD = KeinObjektboden ? -1 : SyntheseKachel(c, r);
                if (syntheseD >= 0 && lageC < 100)
                {
                    var sy = syntheseD >= CwpFile.ObjectCodeBase
                             ? ObjectSprite(syntheseD) : Frame(syntheseD);
                    if (sy != null && syntheseD != code[i])
                    {
                        int slot = Streifenplatz(syntheseD);
                        if (slot >= 0)
                            NebelBoden.Add((c, r, c * TileW,
                                OriginY + r * TileH - elev[i] * ElevStep + BlitAnchor + sy.YOff,
                                slot));
                    }
                }
                if (!isObj) Blit(Frame(code[i]), c, r, elev[i]);
                else if (befahrbar || eigenFlach)
                {
                    // ⚠ Die Bildbank waehlt der CODE, nicht die Frage, ob die
                    // Zelle befahrbar ist — wie im Original @0x4B428F.
                    Blit(code[i] >= CwpFile.ObjectCodeBase
                             ? ObjectSprite(code[i]) : Frame(code[i]),
                         c, r, elev[i]);
                    if (eigenFlach && !befahrbar) ObjektbodenFlach++;
                }
            }

        // pass C — objects, back to front. Buildings are left out: they are
        // drawn live so they can fall down. See BuildingCells.
        //
        // ⚠⚠ 18.08.2026 — UND DIE ZELLEN DES VERZAHNTEN DURCHGANGS EBENSO.
        // Gemeldet: »im Original verdecken z. B. auch Baeume Einheiten, bei uns
        // nicht«. Der Grund stand genau hier: ein eingebackener Baum liegt
        // UNTER allem, was danach gezeichnet wird, und kann darum nichts
        // verdecken — im Gegensatz zum Gebaeude, das seit jeher ausgenommen ist.
        //
        // WELCHE Zellen aufragen, entscheidet seit heute die BELEGUNGSKARTE der
        // Kartendatei und keine geratene Pixelschwelle mehr: das Original
        // zeichnet in seinem verzahnten Durchgang @0x4B43BB ausdruecklich die
        // Belegungen 50000..63999 (@0x4B446C) und sonst nichts. Begruendung,
        // Adressen und Messung stehen bei MapForest.ImZeilenfach.
        //
        // ⚠ Ein WALD kann brennen, und Brennen ist im Original ein
        // Kacheltausch. Darum wandert jede Waldzelle zusaetzlich mit ihrer
        // VERKOHLTEN Fassung in den Streifen — siehe BurntAtlas.
        _burnt = null; _burntH = 0;
        var isBuilding = BuildingCells(code);
        // Kachelcode der verkohlten Fassung -> Platz im Streifen. Hoechstens 57
        // je Karte, darum eine kleine Tafel und kein Bild je Zelle.
        if (objects)
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    int i = r * w + c;
                    if (code[i] < GroundMax) continue;
                    if (isBuilding[i]) { MarkSkipped(ObjectSprite(code[i]), c, r, elev[i]); continue; }
                    var sp = ObjectSprite(code[i]);
                    int imap = MapForest.Imap(_map, c, r);
                    // ⚠⚠ 19.08.2026 — die ZEICHENLAGE aus Sektion 20 entscheidet
                    // mit. Siehe MapForest.ImZeilenfach; sie bringt die 578
                    // Zellen mit `imap == 0xFFFF` herein, die das Original
                    // aufragen laesst und wir bisher haben durchfallen lassen.
                    //
                    // ⚠ DIESE AENDERUNG WIRKT ERST NACH EINEM NEUEN EINLESEN.
                    // Die gebackenen Karten liegen fertig im Nutzerordner; wer
                    // sie nicht neu backt, sieht nichts davon. Genau das ist
                    // schon einmal als »Baeume verdecken nicht« gemeldet worden
                    // und wurde zweimal falsch erklaert. Damit es beim naechsten
                    // Mal auffaellt, wird gezaehlt und gesagt.
                    int lage = MapForest.Lage(_map, c, r);
                    if (imap == 0xFFFF && lage >= 100) LagenZellen++;
                    // ⚠ 24.08.2026 — hier stand `|| Kulisse[i]`, damit
                    // Kulissenbauten in die zweite Ebene wandern und im Nebel
                    // verschwinden koennen. Zurueckgenommen am selben Abend:
                    // unter ihren Zellen liegt kein gemalter Boden (siehe
                    // Durchgang A/B), also blieb ein Loch. Die Zellenliste
                    // `Kulisse` bleibt stehen — sie ist richtig gelesen und die
                    // halbe Arbeit fuer den Tag, an dem der Boden da ist.
                    // ⭐ 25.08.2026 - DIE KULISSENBAUTEN WIEDER DAZU.
                    // Gemeldet: »die neutralen Gebaeude verdecken keine
                    // Einheiten, Einheiten scheinen durch«. Eine Kulisse
                    // (IsBuilt == 0) ist kein Boden: sie ragt auf und muss
                    // verdecken. Der Versuch vom 24.08. wurde zurueckgenommen,
                    // weil unter ihren Zellen kein Boden gemalt war - und genau
                    // dieser Boden kommt heute aus demselben Durchgang, der die
                    // Bruecken flach malt. Wird an dieser Karte nachgemessen.
                    if (sp != null && (MapForest.ImZeilenfach(imap, lage) || Kulisse[i]))
                    {
                        BlitTo(_objects ??= new byte[PixelW * PixelH * 4], sp, c, r, elev[i]);

                        // Die verkohlte Fassung — nur fuer WALD; ein
                        // zerstoerbares Objekt (61000..) hat eine andere
                        // Mechanik (Schadensstufen, @0x40D4FB) und ist hier
                        // nicht dran.
                        int kohle = -1, kx = 0, ky = 0;
                        int asche = -1, ax = 0, ay = 0;
                        if (MapForest.IstWald(imap))
                        {
                            // Zwei Ersatzkacheln je Waldzelle: die VERKOHLTE,
                            // solange sie brennt (zapal @0x4CACE5), und die
                            // ABGEBRANNTE, wenn das Feuer aus ist (Brandtakt
                            // @0x4CA424, Protokollzeile »dohorel forest«).
                            kohle = Streifenplatz(MapForest.Verkohlt(code[i], flag[i]));
                            asche = Streifenplatz(MapForest.Abgebrannt(code[i], flag[i]));
                            if (kohle >= 0)
                            {
                                kx = c * TileW;
                                ky = OriginY + r * TileH - elev[i] * ElevStep
                                     + BlitAnchor + kohleSpr[kohle].YOff;
                            }
                            if (asche >= 0)
                            {
                                ax = c * TileW;
                                ay = OriginY + r * TileH - elev[i] * ElevStep
                                     + BlitAnchor + kohleSpr[asche].YOff;
                            }
                        }
                        // ⭐⭐⭐ 24.08.2026 — DIE ZWEI ERSATZBILDER EINES
                        // ZERSTOERBAREN OBJEKTS, nach demselben Muster wie beim
                        // Wald. Das Original tauscht die Kachel:
                        //   Grundkachel + 10001 = brennt      (@0x4CA59B)
                        //   Grundkachel + 10002 = zerstoert   (@0x4CA772)
                        // und weil `code = 10000 + Grundkachel` ist (an allen
                        // fuenf Objekten von map_01 nachgeprueft), sind das
                        // schlicht die beiden NAECHSTEN Kachelcodes.
                        //
                        // ⚠ Sie gehen in dieselben Felder wie beim Wald
                        // (burnt/ash): der Zeichner kann dann beides gleich
                        // behandeln. Unterschieden wird ueber `imap`, nicht
                        // ueber »hat eine verkohlte Kachel«.

                        // ⭐⭐⭐ 24.08.2026 — DIE ART EINES ZERSTOERBAREN OBJEKTS.
                        //
                        // Der Belegungswert eines solchen Objekts ist
                        // 61000 + INDEX in eine eigene Liste; das Original haelt
                        // sie bei 0xC03A30, 6 Byte je Eintrag, rund 2000 Plaetze
                        // (0xC03A30…0xC06912, Anzahl @0x41F2E1). Und das ist
                        // SEKTION 4 der Kartendatei: 0x2EE0 = 12000 Byte
                        // = 2000 x 6, byteweise dieselbe Groesse.
                        //
                        // Der Eintrag: +0 Spalte, +1 Zeile, +2 ART, +3 Zustand.
                        // Die ART ist die Zeile in der Arttafel 0xBB3B60 und
                        // entscheidet ueber Verhaltensklasse und Grundkachel —
                        // siehe Rendering/BrennendeObjekte.cs.
                        int art = -1;
                        if (imap >= 61000 && imap < 64000)
                        {
                            var s4 = _map.Sec(4);
                            int k = (imap - 61000) * 6;
                            if (s4 != null && k + 3 < s4.Length) art = s4[k + 2];
                        }
                        // ⭐ Und gleich aufgeloest: Verhaltensklasse und
                        // Grundkachel stehen in der Arttafel der KACHELDATEI
                        // (CwpFile.ObjType). Sie hier nachzuschlagen erspart es,
                        // die Tafel je Tileset in die Laufzeit zu schleppen —
                        // und sie ist je Tileset eine andere, waere dort also
                        // ein zweiter Zuordnungskreis.
                        int klasse = -1, grundkachel = -1;
                        if (art >= 0) (klasse, grundkachel) = _tiles.ObjType(art);
                        // ⭐⭐⭐ 28.08.2026 — KOHLE UND ASCHE KOMMEN AUS DER
                        // ARTTAFEL, NICHT AUS DEM ZELLCODE.
                        //
                        // Hier stand `code[i] + 1` und `code[i] + 2`, begruendet mit
                        // »code = 10000 + Grundkachel, an den fuenf Objekten von map_01
                        // nachgeprueft«. Das war eine STICHPROBE, und sie hat die
                        // Ausnahme nicht getroffen.
                        //
                        // Das Original rechnet ZUSTANDSUNABHAENGIG aus der Arttafel, an
                        // zwei Stellen woertlich:
                        //     0x4CA593  mov ax, word ptr [eax*8 + 0xBB3B62]
                        //     0x4CA59B  add ax, 0x2711        ; +10001 brennt
                        //     0x4CA76A  mov ax, word ptr [eax*8 + 0xBB3B62]
                        //     0x4CA772  add ax, 0x2712        ; +10002 Asche
                        // `eax` ist dabei das ARTBYTE des Objektsatzes
                        // (byte[esi + 0xC03A32]), nicht der Code der Zelle.
                        //
                        // GEMESSEN ueber alle Kartendateien: 2.426 von 2.446 Objektzellen
                        // stimmen ohnehin ueberein, 20 nicht — alle auf 16.CWM, alle um
                        // genau +1: die Karte legt dort schon die »brennt«-Kachel als
                        // Zustand 0 hin. Bei uns zeigte ein angezuendetes Objekt dieser 20
                        // sofort das Zerstoert-Bild und als Asche eine familienfremde
                        // Kachel. Nullmodell: bei 3.583 verschiedenen Objektcodes traefe
                        // der Zufall zu 0,03 %.
                        //
                        // ⚠ Der Block steht JETZT hinter der Artaufloesung — vorher
                        // stand er davor und `grundkachel` gab es hier noch nicht.
                        // ⚠ Faellt die Arttafel aus, bleibt es beim alten Weg: eine Karte
                        // ohne Arttafel soll nicht schlechter dastehen als vorher.
                        // Rueckfall: --kohle-aus-code.
                        if (imap >= 61000 && imap < 64000)
                        {
                            int brandbasis = (!KohleAusCode && grundkachel >= 0)
                                ? grundkachel + CwpFile.ObjectCodeBase
                                : code[i];
                            if (brandbasis != code[i]) KohleAusArttafel++;
                            kohle = Streifenplatz(brandbasis + 1);
                            asche = Streifenplatz(brandbasis + 2);
                            if (kohle >= 0)
                            {
                                kx = c * TileW;
                                ky = OriginY + r * TileH - elev[i] * ElevStep
                                     + BlitAnchor + kohleSpr[kohle].YOff;
                            }
                            if (asche >= 0)
                            {
                                ax = c * TileW;
                                ay = OriginY + r * TileH - elev[i] * ElevStep
                                     + BlitAnchor + kohleSpr[asche].YOff;
                            }
                        }
                        // ⚠ Das FACH (zweites Feld) ist fuer eine Kulisse die
                        // vorderste Zeile ihres Bauwerks, die ZEICHENPOSITION
                        // (Y darunter) bleibt die der Kachel.
                        // ⚠⚠ 25.08.2026 ZURUECKGENOMMEN: hier stand kurzzeitig das
                        // Fach der vordersten Bauwerkszeile (KulisseFach). Der
                        // Gedanke war, es wie ein echtes Gebaeude zu behandeln -
                        // aber ein Gebaeude ist EIN Bild in EINEM Fach, eine
                        // Kulisse sind EINZELKACHELN. Alle ins vorderste Fach zu
                        // legen laesst sie SPAETER zeichnen und damit MEHR
                        // uebermalen: der gemeldete Panzer war danach ganz weg
                        // statt halb. KulisseFach bleibt berechnet stehen - es
                        // wird gebraucht, sobald die Kacheln als ein
                        // zusammenhaengendes Bild gezeichnet werden.
                        // ⭐ Die EIGENE Kachel in denselben Streifen — siehe
                        // den Kopf von Objects. Ohne sie schneidet der Zeichner
                        // ein Rechteck aus dem zusammengesetzten Bild und nimmt
                        // die Nachbarkachel mit.
                        int eigen = Streifenplatz(code[i]);
                        // ⭐⭐⭐ 28.08.2026 — DER BODEN UNTER DEM OBJEKT, so wie ihn
                        // das Original in seine BEKANNTE Karte schreibt, solange
                        // die Zelle nicht erkundet ist. Siehe SyntheseKachel.
                        // Damit kann der Zeichner ein Objekt im Nebel WEGLASSEN,
                        // ohne ein Loch zu hinterlassen — genau die Vorbedingung,
                        // an der die Ausblendung am 24.08.2026 gescheitert ist.
                        int syn = SyntheseKachel(c, r);
                        int boden = syn >= 0 ? Streifenplatz(syn) : -1;
                        Objects.Add((c, r, c * TileW,
                                     OriginY + r * TileH - elev[i] * ElevStep + BlitAnchor + sp.YOff,
                                     sp.W, sp.H, kohle, kx, ky, asche, ax, ay,
                                     imap, code[i], art, klasse, grundkachel, eigen, boden,
                                     // ⭐⭐⭐ 30.08.2026 — DAS LAGENBYTE MUSS MIT.
                                     // Der Zeichner braucht es fuer den Nebel: das
                                     // Original entscheidet beim Fuellen seiner BEKANNTEN
                                     // Kachelkarte @0x41FAE0 an genau diesem Byte
                                     // (`cmp cl, 0x64`), ob eine Zelle die SYNTHESE oder
                                     // ihre WAHRE Kachel bekommt. Siehe MapObjects.
                                     lage));
                        continue;
                    }
                    Blit(sp, c, r, elev[i]);
                }

        // Der Streifen: die gesammelten verkohlten Bilder nebeneinander, mit
        // Umbruch an PixelW. Er haengt unten an der zweiten Ebene an, seine
        // Y-Werte beginnen darum bei PixelH.
        if (kohleSpr.Count > 0)
        {
            int zx = 0, zy = 0, zeileH = 0;
            for (int k = 0; k < kohleSpr.Count; k++)
            {
                var ks = kohleSpr[k];
                if (zx + ks.W > PixelW && zx > 0) { zx = 0; zy += zeileH; zeileH = 0; }
                var e = BurntAtlas[k];
                BurntAtlas[k] = (e.Code, zx, PixelH + zy, ks.W, ks.H, ks.YOff);
                zx += ks.W;
                if (ks.H > zeileH) zeileH = ks.H;
            }
            _burntH = zy + zeileH;
            _burnt = new byte[PixelW * _burntH * 4];
            for (int k = 0; k < kohleSpr.Count; k++)
            {
                var ks = kohleSpr[k];
                var e = BurntAtlas[k];
                for (int y = 0; y < ks.H; y++)
                {
                    int dy = e.Y - PixelH + y;
                    if (dy < 0 || dy >= _burntH) continue;
                    for (int x = 0; x < ks.W; x++)
                    {
                        int o = (y * ks.W + x) * 4;
                        if (ks.Rgba[o + 3] == 0) continue;
                        int dx = e.X + x;
                        if (dx < 0 || dx >= PixelW) continue;
                        int d = (dy * PixelW + dx) * 4;
                        _burnt[d] = ks.Rgba[o];
                        _burnt[d + 1] = ks.Rgba[o + 1];
                        _burnt[d + 2] = ks.Rgba[o + 2];
                        _burnt[d + 3] = 255;
                    }
                }
            }
            // ⚠ Ohne aufragende Objekte gibt es keine zweite Ebene, an die der
            // Streifen haengen koennte — und ohne Waldzelle gibt es keinen
            // Streifen. Beides zusammen kann also nicht vorkommen; die Zusage
            // steht hier, damit ObjectLayer sie nicht pruefen muss.
            _objects ??= new byte[PixelW * PixelH * 4];
        }

        return Image.CreateFromData(PixelW, PixelH, false, Image.Format.Rgba8, _canvas);
    }
}
