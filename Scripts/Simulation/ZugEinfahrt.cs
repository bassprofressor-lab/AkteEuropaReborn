namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ 01.10.2026 — bug-382, DIE ZUG-EINFAHRT. Seine Beobachtung am Original:
/// »Im Original fahren die Züge richtig in die Gebäude rein, bei uns verschwinden
/// die einfach am Gebäude.« Bauvorlage berichte/zug-einfahrt-fable.md §7 E1–E5,
/// Sollwerte §8; die Schalter stehen in ZugSchalter.cs.
///
/// <code>
///   E1  Waggon im Fach i+2 VOR den Kacheln der Zeile i     @0x4B43F3 / @0x42E1F2   --zug-einfahrt-alt
///   E2  Fahrweg bis Routenpunkt 1 / delka−2 (Randmitte)    @0x42E141..@0x42E14F    --zug-einfahrt-alt
///   E3  je Waggon gelöscht, Nachläufer 4/7/11 Takte später @0x4C6C47 / @0x4C709C   --zug-einfahrt-alt
///   E4  Gleis im Fach seiner Zelle (Versatz 0 statt 2)     @0x42DFE9 / @0x4B43F3   --gleisfach-alt
///   E5  Prüfstand --zug-einfahrt-check (je Takt sichtbarer Anteil, Rückstand, Endpunkt)
/// </code>
///
/// <para>⚠ Das »Hineinfahren« ist im Original NUR Malerordnung + Kachelform: kein Tor,
/// keine Animation, kein Teilbild (Bericht §2/§3). Die Überdeckung entsteht allein
/// daraus, dass ein Waggon auf Routenzeile Z (Fach Z+yoff+2) VOR den Körperkacheln
/// der Zeilen ≥ Z+yoff gezeichnet wird.</para>
/// </summary>
public partial class MapEntityLayer
{
    // ---- E2: der verlängerte Fahrweg -----------------------------------------

    /// <summary>Der Waggonweg einer Linie: die gezeichnete Kette (<c>RailPathOf</c>),
    /// vorn und hinten um den letzten GEZEICHNETEN Routenpunkt des Originals
    /// verlängert (Routenpunkt 1 bzw. delka−2, @0x42E141..@0x42E14F: gezeichnet nur
    /// bei 1 ≤ cursor ≤ delka−2).</summary>
    private sealed class ZugWegDaten
    {
        public List<Vector2> Quelle = null!;
        public List<Vector2> Pts = null!;
        public float[] Cum = null!, Lift = null!;
        /// <summary>1, wenn vorn ein Punkt vorangestellt wurde (Kettenstelle = Wegstelle − Vorn).</summary>
        public int Vorn, Hinten;
        /// <summary>Das Bild auf dem Verlängerungsstück, in KETTENRICHTUNG (−1 = keins).</summary>
        public int StueckVorn = -1, StueckHinten = -1;
        /// <summary>Der Originalpunkt am Kettenanfang (Bud1) und am Kettenende (Bud2),
        /// Zellkoordinaten wie <c>w.Col/w.Row</c>.</summary>
        public Vector2 OrigA, OrigB;
        public bool HatOrig;
    }

    private readonly Dictionary<int, ZugWegDaten> _zugWeg = new();

    /// <summary>Wieviele Wegenden nicht verlängert wurden (Punkt schon am Kettenende,
    /// oder er läge rückwärts / weiter als 2,5 Zellen weg).</summary>
    public int ZugWegNichtVerlaengert, ZugWegVerlaengert;

    /// <summary>Der letzte Fortschrittswert je Waggon (Prüfstand).</summary>
    private readonly Dictionary<Wagon, float> _zugPw = new();

    /// <summary>Routenpunkt → Zellkoordinate unserer Kette, so dass <c>RailPoint</c>
    /// den Bezugspunkt des Originals trifft: halbe Zeile (x, y.5) → Feinlage (0, 10),
    /// die LINKE Kante der Zelle auf halber Höhe = (x − 0,5, y); ganze Zeile (x, y.0)
    /// → Feinlage (20, 0) = (x, y − 0,5). @0x42E200 / 0x4C6A79, Bericht §1.1.</summary>
    private static Vector2 ZugRoutenpunktZelle(Vector2 rp)
    {
        float fy = rp.Y - Mathf.Floor(rp.Y);
        return Mathf.Abs(fy - 0.5f) < 0.25f
            ? new Vector2(rp.X - 0.5f, Mathf.Floor(rp.Y))
            : new Vector2(rp.X, rp.Y - 0.5f);
    }

    private ZugWegDaten? ZugWegOf(int line, List<Vector2> route)
    {
        if (_zugWeg.TryGetValue(line, out var got) && ReferenceEquals(got.Quelle, route)) return got;
        var pd = RailPathOf(line);
        if (pd == null || !ReferenceEquals(pd.Pts, route) || route.Count < 2
            || pd.Lift == null || pd.Lift.Length != route.Count) return null;
        var weg = new ZugWegDaten { Quelle = route };
        var pts = new List<Vector2>(route);
        var lift = new List<float>(pd.Lift);
        if (_lineRoute.TryGetValue(line, out var rt) && rt.Count >= 4)
        {
            int n = rt.Count;
            Vector2 a = ZugRoutenpunktZelle(rt[1]), b = ZugRoutenpunktZelle(rt[n - 3]);
            // Die Kette läuft Bud1 → Bud2 (RailChainFlipped), die Route nicht zwingend.
            bool gedreht = (a - route[0]).LengthSquared() + (b - route[^1]).LengthSquared()
                         > (a - route[^1]).LengthSquared() + (b - route[0]).LengthSquared();
            var vorn = gedreht ? b : a;
            var hinten = gedreht ? a : b;
            weg.OrigA = vorn; weg.OrigB = hinten; weg.HatOrig = true;
            // Stück je Routenschritt: pieces[i] = Schritt i−1 → i (pieces[0] = pieces[1]).
            if (_linePiece.TryGetValue(line, out var rp) && rp.Count == n)
            {
                weg.StueckVorn = gedreht ? (rp[n - 3] + 4) & 7 : rp[2];
                weg.StueckHinten = gedreht ? (rp[2] + 4) & 7 : rp[n - 3];
            }
            if (Verlaengerbar(vorn, route[0], route[1]))
            {
                pts.Insert(0, vorn);
                lift.Insert(0, LiftAn(vorn, route[0], pd.Lift[0]));
                weg.Vorn = 1;
                ZugWegVerlaengert++;
            }
            else ZugWegNichtVerlaengert++;
            if (Verlaengerbar(hinten, route[^1], route[^2]))
            {
                pts.Add(hinten);
                lift.Add(LiftAn(hinten, route[^1], pd.Lift[^1]));
                weg.Hinten = 1;
                ZugWegVerlaengert++;
            }
            else ZugWegNichtVerlaengert++;
        }
        weg.Pts = pts;
        weg.Lift = lift.ToArray();
        weg.Cum = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            weg.Cum[i] = weg.Cum[i - 1] + RailLifted(pts[i - 1], weg.Lift[i - 1])
                                              .DistanceTo(RailLifted(pts[i], weg.Lift[i]));
        _zugWeg[line] = weg;
        return weg;

        // ⚠ UNSERE SETZUNG: verlängert wird nur nach VORN (weg von der Kette) und
        // höchstens 2,5 Zellen; liegt der Punkt schon am Kettenende, bleibt es dabei.
        static bool Verlaengerbar(Vector2 p, Vector2 ende, Vector2 davor)
        {
            var d = p - ende;
            float len = d.Length();
            if (len < 0.05f || len > 2.5f) return false;
            return d.Dot(ende - davor) > 0f;
        }

        // Dieselbe Bildhöhe wie am Kettenende, auch wenn die gerundete Zelle des
        // neuen Punkts eine andere Höhenstufe trägt (RailPoint zieht ElevOf·15 ab).
        float LiftAn(Vector2 p, Vector2 ende, float liftEnde)
            => liftEnde + (ElevOf(Mathf.RoundToInt(ende.X), Mathf.RoundToInt(ende.Y))
                         - ElevOf(Mathf.RoundToInt(p.X), Mathf.RoundToInt(p.Y))) * 15f;
    }

    /// <summary>Lage, Höhe und (auf dem Verlängerungsstück) Bild eines Waggons auf dem
    /// verlängerten Weg. <paramref name="wegF"/> ist die Wegstelle.</summary>
    private void ZugWegSetzen(Wagon w, ZugWegDaten weg, float wegF, int last, int dir)
    {
        if (wegF < 0f) return;
        var pt = RailPathPoint(weg.Pts, wegF);
        w.Col = pt.X; w.Row = pt.Y;
        w.Lift = RailLiftAt(weg.Lift, wegF);
        int cp = -1;
        if (weg.Vorn == 1 && wegF < 1f) cp = weg.StueckVorn;
        else if (weg.Hinten == 1 && wegF > weg.Pts.Count - 2) cp = weg.StueckHinten;
        if (cp >= 0) w.Piece = dir > 0 ? cp : (cp + 4) & 7;
    }

    // ---- E3: Fortschritt und Rückstand je Waggon ------------------------------

    /// <summary>Der Fahrtfortschritt der Linie: 0 bei der Abfahrt, 1 bei der Ankunft
    /// des Spitzenwaggons, im NACHLAUF darüber hinaus (damit die Waggons 1..3 einzeln
    /// an derselben Stelle ankommen). Steht die Linie, 2 — dann ist kein Waggon da
    /// (in den 20 Standrunden gibt es im Original keinen Satz).</summary>
    private float ZugFortschritt(RailLine l)
    {
        float alt = l.TravelFull <= 0f ? 1f : 1f - Mathf.Clamp(l.Travel / l.TravelFull, 0f, 1f);
        if (ZugStehtAmBahnsteig || l.TravelFull <= 0f) return alt;
        if (l.Nachlauf >= 0f) return 1f + l.Nachlauf / l.TravelFull;
        if (!l.Rollt) return 2f;
        return alt;
    }

    /// <summary>Der Rückstand von Waggon <paramref name="k"/> als FAHRZEIT: 0/4/7/11
    /// Takte (Startzähler 20/40/25/40 @0x4C687C, Abzug 8 je Takt), ein Takt =
    /// 1/TickScale s. ⚠ Bis bug-377 war es eine Wegstrecke (1/5 Zelle je Takt
    /// über die Kettenlänge) — dann stimmt der Abstand der Ankunft nur auf geraden
    /// Linien, deren Kette so lang ist wie die Route. Die ZEIT ist, was das Original
    /// zählt.</summary>
    private float ZugRueckstand(RailLine l, int k)
        => l.TravelFull <= 0f ? 0f : RailWagonLagTicks[k] / (float)TickScale / l.TravelFull;

    // ---- E5: Deckung eines Waggons durch Gebäudekacheln ------------------------

    private readonly Dictionary<int, Image?> _zeBild = new();

    private Image? ZeWaggonBild(Wagon w)
    {
        int part = WagonPart.TryGetValue(w.Index, out var pp) ? pp : 58;
        int piece = w.Index == 3 ? (w.Piece + 4) & 7 : w.Piece;   // wie DrawWagon
        int key = part * 8 + (piece & 7);
        if (_zeBild.TryGetValue(key, out var img)) return img;
        string p = Core.Content.Path($"Units/train/{part}/f{piece & 7}.png");
        img = FileAccess.FileExists(p) ? Image.LoadFromFile(p) : null;
        _zeBild[key] = img;
        return img;
    }

    /// <summary>
    /// Wieviele deckende Waggonpixel (Alpha &gt; 64) liegen unter einer Körperkachel
    /// des Gebäudes <paramref name="b"/>, deren Zeile ≥ <paramref name="abUns"/>
    /// (UNSERE Ordnung) bzw. ≥ <paramref name="abOrig"/> (Original-Ordnung, Fach
    /// i+2 vor den Kacheln der Zeile i) ist, und welche Gebäudezeile (relativ zur
    /// Ecke) wieviel deckt. Dieselbe Kachelwahl wie <c>DrawBuildingTiles</c>
    /// (oberste belegte Lage, nur Körperzellen; flache Kacheln sind Boden).
    /// ⚠ Gerüst/Ruine sind nicht nachgebildet (Prüfstand, lebende Bahnhöfe).
    /// </summary>
    private (int Gesamt, int Uns, int Orig, string Zeilen) ZeDeckung(Wagon w, Entity b,
                                                                     int abUns, int abOrig)
    {
        var img = ZeWaggonBild(w);
        var atlas = Patterns?.AtlasImage;
        if (img == null || atlas == null || Patterns == null || b.Dead) return (0, 0, 0, "");
        int W = img.GetWidth(), H = img.GetHeight();
        var at = RailLifted(new Vector2(w.Col, w.Row), w.Lift);
        var o = at - ComposedAnchor + WagonOverRail;
        int ox = Mathf.RoundToInt(o.X), oy = Mathf.RoundToInt(o.Y);
        var maske = new bool[W * H];
        var hoechste = new int[W * H];
        int gesamt = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                hoechste[y * W + x] = int.MinValue;
                if (img.GetPixel(x, y).A > 0.25f) { maske[y * W + x] = true; gesamt++; }
            }
        if (gesamt == 0) return (0, 0, 0, "");
        var jeZeile = new SortedDictionary<int, int>();
        var bt = Patterns.GetBuildingType(b.BildArt);
        int first = bt.FirstPattern, stack = DamageFrame(b);
        if (first < 0 || stack < 1) return (gesamt, 0, 0, "");
        var anim = BuildingAnimCells(b);
        int ab = Mathf.Min(abUns, abOrig);
        for (int dx = 0; dx < Import.CwpFile.PatternWidth; dx++)
            for (int dy = 0; dy < Import.CwpFile.PatternHeight; dy++)
            {
                int c = b.Col + dx, r = b.Row + dy;
                if (r < ab) continue;
                for (int k = stack - 1; k >= 0; k--)
                {
                    int code = BuildingCellTile(first, k, dx, dy, anim);
                    if (code == 0 || !Patterns.TryGetTile(code, out var t)) continue;
                    bool koerper = GebaeudeBlock ? t.H > FlachBisPx : _koerperZelle.Contains((c, r));
                    if (!koerper) break;
                    int sx = Mathf.RoundToInt(_ox + c * Import.MapBaker.TileW);
                    int sy = Mathf.RoundToInt(_oy + r * Import.MapBaker.TileH
                                              - ElevOf(c, r) * Import.MapBaker.ElevStep
                                              + Import.MapBaker.BlitAnchor + t.YOff);
                    int x0 = Mathf.Max(sx, ox), x1 = Mathf.Min(sx + t.W, ox + W);
                    int y0 = Mathf.Max(sy, oy), y1 = Mathf.Min(sy + t.H, oy + H);
                    int n = 0;
                    for (int py = y0; py < y1; py++)
                        for (int px = x0; px < x1; px++)
                        {
                            int i = (py - oy) * W + (px - ox);
                            if (!maske[i]) continue;
                            if (atlas.GetPixel(t.X + px - sx, t.Y + py - sy).A <= 0.25f) continue;
                            n++;
                            if (r > hoechste[i]) hoechste[i] = r;
                        }
                    if (n > 0 && r >= abUns) jeZeile[dy] = jeZeile.GetValueOrDefault(dy) + n;
                    break;
                }
            }
        int uns = 0, orig = 0;
        for (int i = 0; i < hoechste.Length; i++)
        {
            if (!maske[i] || hoechste[i] == int.MinValue) continue;
            if (hoechste[i] >= abUns) uns++;
            if (hoechste[i] >= abOrig) orig++;
        }
        var sb = new System.Text.StringBuilder();
        foreach (var kv in jeZeile)
            sb.Append($"{(sb.Length > 0 ? " " : "")}Z{kv.Key} {100f * kv.Value / gesamt:0}%");
        return (gesamt, uns, orig, sb.ToString());
    }

    /// <summary>Ab welcher Gebäudezeile die Körperkacheln über dem Waggon liegen —
    /// in UNSERER Zeichenschleife (je nach Schalter) und im Original (Z + yoff).</summary>
    private (int Uns, int Orig) ZeAbZeile(Wagon w)
    {
        int fach = WaggonFach(w);              // Z + yoff + 2, Original-Fach
        int orig = fach - 2;                   // Fach i+2 vor den Kacheln der Zeile i
        int uns = ZugfachAlt ? int.MaxValue : ZugEinfahrtAlt ? fach : orig;
        return (uns, orig);
    }

    // ---- E5: der Prüfstand -----------------------------------------------------

    /// <summary>Wann die Schlusszeile kommt (Spielsekunden), <c>--zug-einfahrt-check=N</c>.</summary>
    public static float ZugEinfahrtCheckSekunden = 80f;

    /// <summary><c>--zug-einfahrt-linie=N</c>: nur diese Linie fotografieren.</summary>
    public static int ZugEinfahrtBildLinie = -1;

    private sealed class ZeSpur
    {
        public bool Sicht, Verlaengert;
        public int Linie, Fahrt, Ende, Typ, Dir;
        public float Abstand, Uns = 1f, Orig = 1f;
        public string Zeilen = "";
        public readonly List<(int T, float Uns, float Orig, string Zeilen)> Verlauf = new();
    }

    private sealed class ZeEreignis
    {
        public bool Verlaengert;
        public int Linie, Fahrt, Index, Ende, Typ, Dir, Takt;
        public float Abstand, Uns, Orig;
        public List<(int T, float Uns, float Orig, string Zeilen)> Verlauf = null!;
    }

    private readonly Dictionary<Wagon, ZeSpur> _zeSpur = new();
    private readonly List<ZeEreignis> _zeEreignisse = new();
    private int _zeTakt, _zeUnterwegs, _zeOrdnungAnders, _zeOrdnungBilder, _zeOrdnungVerdeckt;
    private float _zeZeit, _zeMelde;
    private bool _zeFertig;

    /// <summary>Der Takt des Prüfstands (zählt je SimTick).</summary>
    public int ZeTakt => _zeTakt;

    /// <summary>Aus <c>SimTick</c>, je Takt, wenn <c>--zug-einfahrt-check</c> oder
    /// <c>--zug-demo-check</c> läuft (der zweite liest V2/V3 von hier).</summary>
    private void PollZugEinfahrtCheck(float dt)
    {
        _zeTakt++;
        ZeBild();
        if (!ZugEinfahrtCheckAn || _zeFertig) return;
        _zeZeit += dt;
        _zeMelde += dt;
        if (_zeMelde >= 10f) { _zeMelde = 0f; GD.Print($"[{_zeZeit:0}s] " + ZugEinfahrtZeile(false)); }
        if (_zeZeit >= ZugEinfahrtCheckSekunden)
        {
            _zeFertig = true;
            GD.Print(ZugEinfahrtZeile(true));
        }
    }

    private void ZeBild()
    {
        var da = new HashSet<Wagon>();
        foreach (var w in _wagons)
        {
            if (!w.Freight) continue;
            var l = ZdLinie(w.Line);
            if (l == null) continue;
            da.Add(w);
            if (!_zeSpur.TryGetValue(w, out var s)) _zeSpur[w] = s = new ZeSpur();
            if (w.Hidden)
            {
                if (s.Sicht) ZeVerschwunden(w, s);
                s.Sicht = false;
                s.Verlauf.Clear();
                continue;
            }
            s.Sicht = true;
            s.Linie = w.Line; s.Fahrt = l.Starts; s.Dir = l.Dir;
            s.Ende = l.Dir == 0 ? l.Bud2 : l.Bud1;
            var b = RailBuilding(s.Ende);
            s.Typ = b?.BType ?? -1;
            s.Abstand = float.MaxValue;
            s.Uns = s.Orig = 1f; s.Zeilen = "";
            var pd = RailPathOf(w.Line);
            var weg = pd == null ? null : ZugWegOf(w.Line, pd.Pts);
            if (weg == null || !weg.HatOrig) continue;
            var ziel = l.Dir == 0 ? weg.OrigB : weg.OrigA;
            s.Verlaengert = (l.Dir == 0 ? weg.Hinten : weg.Vorn) == 1;
            s.Abstand = new Vector2((w.Col - ziel.X) * TileW, (w.Row - ziel.Y) * TileH).Length();
            if (s.Abstand > 3f * TileW || b == null) continue;
            var (ab, abOrig) = ZeAbZeile(w);
            var d = ZeDeckung(w, b, ab, abOrig);
            if (d.Gesamt <= 0) continue;
            s.Uns = 1f - (float)d.Uns / d.Gesamt;
            s.Orig = 1f - (float)d.Orig / d.Gesamt;
            s.Zeilen = d.Zeilen;
            _zeOrdnungBilder++;
            if (d.Orig > 0) _zeOrdnungVerdeckt++;
            if (Mathf.Abs(s.Uns - s.Orig) > 0.005f) _zeOrdnungAnders++;
            s.Verlauf.Add((_zeTakt, s.Uns, s.Orig, s.Zeilen));
            if (s.Verlauf.Count > 24) s.Verlauf.RemoveAt(0);
        }
        // Waggons, die ganz aus der Liste verschwunden sind (eigene Züge, RailClearWagons)
        foreach (var k in new List<Wagon>(_zeSpur.Keys))
            if (!da.Contains(k))
            {
                if (_zeSpur[k].Sicht) ZeVerschwunden(k, _zeSpur[k]);
                _zeSpur.Remove(k);
            }
    }

    private void ZeVerschwunden(Wagon w, ZeSpur s)
    {
        // weit draußen verschwunden = kein Linienende (Explosion, Neustart) — nicht gezählt
        if (s.Abstand > 3f * TileW) { _zeUnterwegs++; return; }
        _zeEreignisse.Add(new ZeEreignis
        {
            Verlaengert = s.Verlaengert, Linie = s.Linie, Fahrt = s.Fahrt, Index = w.Index, Ende = s.Ende, Typ = s.Typ,
            Dir = s.Dir, Takt = _zeTakt, Abstand = s.Abstand, Uns = s.Uns, Orig = s.Orig,
            Verlauf = new List<(int, float, float, string)>(s.Verlauf),
        });
    }

    /// <summary>Fahrten, in denen ein Nachläufer im SELBEN Takt wie Waggon 0 verschwand
    /// (V3 des --zug-demo-check liest das).</summary>
    private int ZeGleichzeitig()
    {
        int n = 0;
        foreach (var g in ZeFahrten())
        {
            if (!g.TryGetValue(0, out var e0)) continue;
            foreach (var kv in g) if (kv.Key > 0 && kv.Value.Takt == e0.Takt) { n++; break; }
        }
        return n;
    }

    private List<Dictionary<int, ZeEreignis>> ZeFahrten()
    {
        var fahrten = new Dictionary<(int, int, int), Dictionary<int, ZeEreignis>>();
        foreach (var e in _zeEreignisse)
        {
            var key = (e.Linie, e.Fahrt, e.Ende);
            if (!fahrten.TryGetValue(key, out var g)) fahrten[key] = g = new Dictionary<int, ZeEreignis>();
            g.TryAdd(e.Index, e);
        }
        return new List<Dictionary<int, ZeEreignis>>(fahrten.Values);
    }

    private static string ZeTypName(int t) => t switch
    {
        1 => "Basis", 2 or 3 => "Fabrik", 6 => "Bahnstation", 12 => "Feldbahnhof", _ => $"Typ {t}",
    };

    private string ZugEinfahrtZeile(bool schluss)
    {
        var sb = new System.Text.StringBuilder("zug-einfahrt-check: ");
        var schalter = new List<string>();
        if (ZugEinfahrtAlt) schalter.Add("--zug-einfahrt-alt");
        if (GleisfachAlt) schalter.Add("--gleisfach-alt");
        if (ZugfachAlt) schalter.Add("--zugfach-alt");
        if (ZugStehtAmBahnsteig) schalter.Add("--zug-steht-am-bahnsteig");
        if (schalter.Count > 0) sb.Append($"⚠ NULLMODELL {string.Join(" ", schalter)} | ");

        // (A) kein Waggon verschwindet VOR dem Gebäude (> 1 Zelle vor dem Originalpunkt)
        int vorzeitig = 0; float vorMax = 0f; string vorWo = "";
        // (C) Endpunkt des Spitzenwaggons gegen den Originalpunkt
        int n0 = 0, n0Kette = 0; float abMax = 0f, abSum = 0f; string abWo = "", kWo = "";
        foreach (var e in _zeEreignisse)
        {
            if (e.Abstand > TileW)
            {
                vorzeitig++;
                if (e.Abstand > vorMax)
                {
                    vorMax = e.Abstand;
                    vorWo = $"Linie {e.Linie} W{e.Index} {e.Abstand:0} px vor dem Endpunkt an {ZeTypName(e.Typ)} Platz {e.Ende}";
                }
            }
            // ⚠ Enden, deren Weg NICHT verlaengert werden konnte (der Routenpunkt
            // laege rueckwaerts / > 2,5 Zellen weg — DM_4 Linie 0, deren Kette der
            // Export nicht trennt, Bericht §5.5), zaehlen getrennt.
            if (e.Index == 0 && !e.Verlaengert)
            {
                n0Kette++;
                if (kWo.Length == 0) kWo = $"Linie {e.Linie} an {ZeTypName(e.Typ)} Platz {e.Ende} {e.Abstand:0} px";
            }
            else if (e.Index == 0)
            {
                n0++; abSum += e.Abstand;
                if (e.Abstand > abMax) { abMax = e.Abstand; abWo = $"Linie {e.Linie} an {ZeTypName(e.Typ)} Platz {e.Ende}"; }
            }
        }
        // (B) Rückstand der Nachläufer beim Verschwinden: 4/7/11 Takte
        int fahrtenVoll = 0, rueckOk = 0; string rueckWo = "";
        var rueckSumme = new float[4];
        foreach (var g in ZeFahrten())
        {
            if (g.Count < 4 || !g.ContainsKey(0)) continue;
            fahrtenVoll++;
            bool ok = true;
            for (int i = 1; i < 4; i++)
            {
                if (!g.TryGetValue(i, out var ei)) { ok = false; continue; }
                int d = ei.Takt - g[0].Takt;
                rueckSumme[i] += d;
                if (Mathf.Abs(d - RailWagonLagTicks[i]) > 1) ok = false;
            }
            if (ok) rueckOk++;
            else if (rueckWo.Length == 0)
                rueckWo = $"Linie {g[0].Linie} an {ZeTypName(g[0].Typ)}: " +
                          string.Join("/", System.Linq.Enumerable.Select(new[] { 1, 2, 3 }, i => g.TryGetValue(i, out var x) ? (x.Takt - g[0].Takt).ToString() : "?"));
        }
        // (D) Ordnung und Deckung: Bahnstation/Feldbahnhof, Spitzenwaggon im letzten Bild
        int nHalle = 0; float halleUns = 0f, halleOrig = 0f;
        var jeTyp = new SortedDictionary<int, (int N, float Uns, float Orig, float Ab)>();
        foreach (var e in _zeEreignisse)
        {
            if (e.Index != 0) continue;
            var t = jeTyp.GetValueOrDefault(e.Typ);
            jeTyp[e.Typ] = (t.N + 1, t.Uns + 1f - e.Uns, t.Orig + 1f - e.Orig, t.Ab + e.Abstand);
            if (e.Typ is 6) { nHalle++; halleUns += 1f - e.Uns; halleOrig += 1f - e.Orig; }
        }
        // Die vier Faelle aus Bericht §4 (DM_4), Spitzenwaggon im letzten Bild, verdeckt:
        // ⚠ nur zur Auskunft — die senkrechte Lage ist dort V ±15 px.
        var faelle = new System.Text.StringBuilder();
        foreach (var (fl, fp, soll, name) in new[] { (3, 28, 72, "Bahnstation links"), (15, 57, 100, "Feldbahnhof-Durchfahrt"),
                                                     (8, 8, 3, "Fabrik rechts"), (15, 0, 0, "Basis") })
        {
            var e = _zeEreignisse.Find(x => x.Index == 0 && x.Linie == fl && x.Ende == fp);
            if (e == null) continue;
            faelle.Append($"{(faelle.Length > 0 ? ", " : "")}{name} L{fl}/P{fp} {100f * (1f - e.Uns):0} % (Soll {soll})");
        }

        bool a = _zeEreignisse.Count > 0 && vorzeitig == 0;
        bool bOk = fahrtenVoll > 0 && rueckOk == fahrtenVoll;
        bool c = n0 > 0 && abMax <= 10f;
        bool dOk = _zeOrdnungAnders == 0 && nHalle > 0 && halleUns / nHalle >= 0.40f;
        string Ok(bool x) => x ? "OK" : "ABWEICHUNG";

        sb.Append($"{_zeEreignisse.Count} Waggons an Linienenden verschwunden ({_zeUnterwegs} unterwegs, nicht gezaehlt)");
        sb.Append($" | A vor dem Gebaeude (> 40 px vor dem Originalpunkt) verschwunden: {vorzeitig}" +
                  (vorWo.Length > 0 ? $" (weitester: {vorWo})" : "") + $" {Ok(a)}");
        sb.Append($" | B Rueckstand beim Verschwinden (Soll 4/7/11 Takte, ±1): {rueckOk} von {fahrtenVoll} Fahrten" +
                  (fahrtenVoll > 0 ? $", Mittel {rueckSumme[1] / fahrtenVoll:0.0}/{rueckSumme[2] / fahrtenVoll:0.0}/{rueckSumme[3] / fahrtenVoll:0.0}" : "") +
                  (rueckWo.Length > 0 ? $" (erste falsche: {rueckWo})" : "") + $" {Ok(bOk)}");
        sb.Append($" | C Spitzenwaggon letztes Bild gegen Routenpunkt 1/delka-2: {n0} Ankuenfte, " +
                  (n0 > 0 ? $"Mittel {abSum / n0:0.0} px, groesster {abMax:0.0} px{(abWo.Length > 0 ? $" ({abWo})" : "")} (Soll <= 10)" +
                  (n0Kette > 0 ? $"; dazu {n0Kette} an NICHT verlaengerten Enden ({kWo}), nicht gewertet" : "") : "—") + $" {Ok(c)}");
        sb.Append($" | D Malerordnung: {_zeOrdnungBilder} Bilder am Linienende, davon {_zeOrdnungVerdeckt} im Original " +
                  $"verdeckt, unsere Ordnung abweichend in {_zeOrdnungAnders}; Bahnstation " +
                  $"Spitzenwaggon im letzten Bild verdeckt: " +
                  (nHalle > 0 ? $"{100f * halleUns / nHalle:0} % in {nHalle} Ankuenften (Original-Ordnung {100f * halleOrig / nHalle:0} %, Soll >= 40, Bericht §4/§8: 72)" : "—") +
                  (faelle.Length > 0 ? $"; Faelle §4: {faelle}" : "") +
                  $" {Ok(dOk)}");
        sb.Append($" | Weg verlaengert {ZugWegVerlaengert}, nicht {ZugWegNichtVerlaengert}");
        if (schluss)
        {
            var z = new System.Text.StringBuilder();
            z.Append("\nzug-einfahrt-check: je Endgebaeude (Spitzenwaggon, letztes Bild): ");
            foreach (var kv in jeTyp)
                z.Append($"[{ZeTypName(kv.Key)} {kv.Value.N}x verdeckt {100f * kv.Value.Uns / kv.Value.N:0} % " +
                         $"(Original {100f * kv.Value.Orig / kv.Value.N:0} %), Abstand {kv.Value.Ab / kv.Value.N:0.0} px] ");
            sb.Append(z);
            sb.Append(ZeProfile());
            sb.Append($"\nzug-einfahrt-check: bug-382 {(a && bOk && c && dOk ? "BESTANDEN" : "DURCHGEFALLEN")}" +
                      $" (A {Ok(a)}, B {Ok(bOk)}, C {Ok(c)}, D {Ok(dOk)})");
        }
        return sb.ToString();
    }

    /// <summary>Je Takt der sichtbare Anteil — für die ersten vollständigen Fahrten an
    /// einer Bahnstation, einem Feldbahnhof und einer Basis/Fabrik (Bericht §8:
    /// Bahnstation links 100 % → 100 % → 28 % → gelöscht).</summary>
    private string ZeProfile()
    {
        var sb = new System.Text.StringBuilder();
        var gezeigt = new HashSet<string>();
        foreach (var g in ZeFahrten())
        {
            if (!g.TryGetValue(0, out var e0)) continue;
            string art = e0.Typ is 6 ? "6" : e0.Typ is 12 ? "12" : e0.Typ is 1 ? "1" : e0.Typ is 2 or 3 ? "2" : "";
            if (art.Length == 0 || !gezeigt.Add(art + (e0.Typ == 6 ? $"/{e0.Linie}" : ""))) continue;
            if (gezeigt.Count > 6) break;
            sb.Append($"\nzug-einfahrt-check: PROFIL Linie {e0.Linie} an {ZeTypName(e0.Typ)} Platz {e0.Ende} " +
                      $"({(e0.Dir == 0 ? "Hinfahrt" : "Rueckfahrt")}), sichtbar je Takt vor dem Verschwinden:");
            for (int i = 0; i < 4; i++)
            {
                if (!g.TryGetValue(i, out var e)) continue;
                sb.Append($"\n    W{i} weg im Takt +{e.Takt - e0.Takt}, letzter Abstand {e.Abstand:0} px:");
                foreach (int off in new[] { 10, 5, 3, 1 })
                {
                    var hit = e.Verlauf.FindLast(v => v.T <= e.Takt - off);
                    if (hit.Zeilen == null) { sb.Append($"  T-{off} —"); continue; }
                    sb.Append($"  T-{off} {100f * hit.Uns:0} %" + (hit.Zeilen.Length > 0 ? $" ({hit.Zeilen})" : "") +
                              (Mathf.Abs(hit.Uns - hit.Orig) > 0.005f ? $" [Orig {100f * hit.Orig:0} %]" : ""));
                }
                sb.Append("  -> geloescht");
            }
        }
        return sb.ToString();
    }

    // ---- E5: der Bildabzug -------------------------------------------------------

    private int _zeBildLinie = -1;

    /// <summary>Für <c>--zug-einfahrt-check --shot=…</c>: sobald ein Spitzenwaggon
    /// (bevorzugt an einer Bahnstation, dann Feldbahnhof) höchstens 2,5 Zellen vor
    /// seinem Originalendpunkt steht, der Kartenpunkt dieses Endpunkts; sonst null.</summary>
    public Vector2? ZugEinfahrtBildZiel()
    {
        Vector2? best = null; int bestRang = int.MaxValue; int bestLinie = -1;
        foreach (var w in _wagons)
        {
            if (!w.Freight || w.Hidden || w.Index != 0) continue;
            if (_zeBildLinie >= 0 && w.Line != _zeBildLinie) continue;
            if (ZugEinfahrtBildLinie >= 0 && w.Line != ZugEinfahrtBildLinie) continue;
            var l = ZdLinie(w.Line);
            if (l == null || !l.Rollt) continue;
            var pd = RailPathOf(w.Line);
            var weg = pd == null ? null : ZugWegOf(w.Line, pd.Pts);
            if (weg == null || !weg.HatOrig) continue;
            var ziel = l.Dir == 0 ? weg.OrigB : weg.OrigA;
            float ab = new Vector2((w.Col - ziel.X) * TileW, (w.Row - ziel.Y) * TileH).Length();
            if (ab > 2.5f * TileW || ab < 1.2f * TileW) continue;
            var b = RailBuilding(l.Dir == 0 ? l.Bud2 : l.Bud1);
            int rang = b?.BType == 6 ? 0 : b?.BType == 12 ? 1 : 2;
            if (rang < bestRang) { bestRang = rang; best = RailPoint(ziel); bestLinie = w.Line; }
        }
        if (best != null && (_zeBildLinie < 0 && (bestRang == 0 || ZugEinfahrtBildLinie >= 0 || _zeZeit > 40f)))
            _zeBildLinie = bestLinie;
        return _zeBildLinie >= 0 && bestLinie == _zeBildLinie ? best : null;
    }

    /// <summary>Eine Zeile zum Bild: die vier Waggons der fotografierten Linie.</summary>
    public string ZugEinfahrtBildZeile()
    {
        var sb = new System.Text.StringBuilder($"Linie {_zeBildLinie}, Takt {_zeTakt}:");
        foreach (var w in _wagons)
        {
            if (w.Line != _zeBildLinie || !w.Freight) continue;
            _zeSpur.TryGetValue(w, out var s);
            sb.Append($" W{w.Index} " + (w.Hidden ? "geloescht" :
                      $"({w.Col:0.00},{w.Row:0.00}) pw {_zugPw.GetValueOrDefault(w):0.000} sichtbar {100f * (s?.Uns ?? 1f):0} %" +
                      (s != null && s.Zeilen.Length > 0 ? $" [{s.Zeilen}]" : "")));
        }
        return sb.ToString();
    }
}
