namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 30.10.2026, bug-685 (berichte/pruefung-rest-b-opus.md §5) — <b>DIE KARTENRAMPE WIRD
/// LEBEND GEZEICHNET, NICHT MEHR INS BODENBILD GEBACKEN.</b> Dieselbe Ursache wie bug-644
/// (KayelGee R8 K2, Brueckengelaender), dieselbe Loesung wie dort
/// (Simulation/KartenbrueckeLebend.cs).
///
/// <para><b>Befund:</b> eine Rampe der Karte (sec20 = 200 + Platz, Kachel <c>10723 + v</c>)
/// lag im gebackenen <c>map_NN.png</c>. Ihr Bild ragt 1…5 Zeilen ueber die Zelle hinaus
/// (h 21…25, unten buendig). Der Abriss <c>0x4CBAB0</c> legt nur die Truemmerkachel
/// <c>10747 + v</c> darueber, und die deckt den Ueberstand nicht: je v
/// 141/144/40/40/174/157/2/1 Punkte blieben stehen. 90 Rampenzellen auf 11 Karten, 69
/// davon tragen eine Rampenkachel.</para>
///
/// <para><b>Original:</b> kein Bodenbild; der flache Durchgang malt jede Zelle je Bild aus
/// ihrem Kachelwort (<c>0x401410</c>), und der Abriss ersetzt das Kachelwort — die alte
/// Rampe wird gar nicht mehr gemalt.</para>
///
/// <para><b>Gebaut:</b> (1) der Backofen laesst Zellen mit Lage 200…249 und Rampenkachel
/// 10723…10730 weg (<c>Import/MapBaker.RampeLebend</c>, an beiden Stellen wie bei der
/// Bruecke); (2) hier wird jede HEILE Kartenrampe im Bodendurchgang aus ihrer
/// Kartenkachel gemalt, als unterste Lage vor Vorschau und Stufenbildern
/// (<see cref="ZeichneMolen"/>). Hat sie eine Schadensstufe, malt sie wie bisher
/// ZeichneMolen (10723 + v + 8·Stufe, deckt voll); nach dem Abriss malt hier nichts mehr.
/// (3) Die Uebersichtskarte bekommt dieselben Kacheln.</para>
///
/// <para>⚠ <b>Braucht einen Neuexport der Karten</b>: <c>InhaltsStand.Noetig["karten"] = 4</c>.
/// Mit Daten eines aelteren Imports steht die heile Rampe doppelt (gebacken + lebend,
/// deckungsgleich) und der Rest nach dem Abriss bleibt.</para>
///
/// <para>⚠ <b>UNSERE SETZUNG</b> (wie bei der Bruecke): was nach der Rampenzelle in
/// Zeilenfolge gebacken wurde und in ihr Bild hineinragt, liegt jetzt UNTER ihr statt
/// darueber. Am Bildvergleich gemessen (berichte/bau-rampen-helistrahl.md).</para>
///
/// <para>Gegenschalter <c>--rampen-gebacken-alt</c>: der alte Weg, soweit er mit den neuen
/// Daten geht — die Kachel jeder Kartenrampe wird IMMER als unterste Lage gemalt, auch
/// beschaedigt und nach dem Abriss, wie das gebackene Bild. Damit steht der Rest wieder
/// da. Pruefstand <c>--rampen-streifen-check</c> (kopflos, Rendering/PaketP3Lauf.cs).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--rampen-gebacken-alt</c> — Nullmodell zu bug-685.</summary>
    public static bool RampenGebackenAlt;

    /// <summary>Die Kartenrampen mit Rampenkachel: Zelle, Kartenkachel, LADEhoehe.</summary>
    private readonly List<(Vector2I Zelle, int Code, int Elev)> _kartenRampen = new();

    public int KartenrampenGezeichnet;

    /// <summary>Beim Laden, nach sec21 (Simulation/Bruecken.cs LiesBruecken): welche
    /// Kartenrampen der Backofen weggelassen hat — dieselbe Regel wie dort.</summary>
    private void KartenRampenAnlegen()
    {
        _kartenRampen.Clear();
        foreach (var r in _moleSaetze)
        {
            if (!r.Karte) continue;
            int key = r.Col * 1024 + r.Row;
            if (!_rampen.TryGetValue(key, out int lage) || !_rampenKachel.TryGetValue(key, out int code)) continue;
            if (!Import.MapBaker.RampeLebend(lage, code)) continue;
            _kartenRampen.Add((new Vector2I(r.Col, r.Row), code, ElevOf(r.Col, r.Row)));
        }
        if (_kartenRampen.Count > 0)
            GD.Print($"rampen: {_kartenRampen.Count} Kartenrampe(n) lebend gezeichnet (bug-685)"
                   + (RampenGebackenAlt ? " — --rampen-gebacken-alt: immer, auch nach dem Abriss" : ""));
    }

    /// <summary>Welche Rampenkacheln jetzt lebend zu malen sind.</summary>
    private IEnumerable<(Vector2I Zelle, int Code, int Elev)> KartenrampenKacheln()
    {
        foreach (var (z, code, elev) in _kartenRampen)
        {
            if (!RampenGebackenAlt && RampeAn(z.X, z.Y) is not { Karte: true, Tp: >= 200 }) continue;
            yield return (z, code, RampenGebackenAlt ? elev : ElevOf(z.X, z.Y));
        }
    }

    private Rect2 KartenrampeZiel(Vector2I z, int elev, (Rect2 Feld, int YOff) k)
    {
        var ziel = CellRect(_ox, _oy, z.X, z.Y, elev);
        return new Rect2(new Vector2(ziel.Position.X, ziel.Position.Y + Import.MapBaker.BlitAnchor + k.YOff),
                         k.Feld.Size);
    }

    /// <summary>Im Bodendurchgang, als UNTERSTE Lage der Molen (ZeichneMolen).</summary>
    private void ZeichneKartenrampen(Texture2D tex)
    {
        KartenrampenGezeichnet = 0;
        foreach (var (z, code, elev) in KartenrampenKacheln())
        {
            if (StreifenKachel(code) is not { } k) continue;
            // ⚠ clip_uv AUS wie bei der Kartenbruecke (KartenbrueckeLebend.cs): mit Godots
            // Vorgabe zeigt bei Zoom 2 die erste Zeile der Kachel die Nachbarzeile.
            DrawTextureRectRegion(tex, KartenrampeZiel(z, elev, k), k.Feld, null, false, BrueckeClipUvAlt);
            KartenrampenGezeichnet++;
        }
    }

    /// <summary>Fuer die Uebersichtskarte (an KartenfahrbahnUebersicht angehaengt).</summary>
    private IEnumerable<(Rect2 Ziel, Rect2 Quelle)> KartenrampenUebersicht()
    {
        foreach (var (z, code, elev) in KartenrampenKacheln())
            if (StreifenKachel(code) is { } k) yield return (KartenrampeZiel(z, elev, k), k.Feld);
    }

    /// <summary>
    /// <c>--rampen-streifen-check</c> (kopflos, z. B. <c>-- --campaign=5</c>) — je heiler
    /// Kartenrampe mit Rampenkachel: die deckenden Punkte (α ≥ 0,5) ihrer Kachel; welche
    /// davon im Bild stehen (GEBACKEN = das Bodenbild traegt an der Stelle genau die Farbe
    /// der Kachel; LEBEND = der Zeichner malt sie); dann der echte Abriss
    /// (<see cref="RampeAbreissen"/>) und die REST-Punkte, die danach weiter im Bild stehen
    /// und die Truemmerkachel 10747 + v nicht deckt. Soll 0 je Rampe. Nullmodell
    /// <c>--rampen-gebacken-alt</c> (oder Daten ohne Neueinlesen): 141/144/40/40/174/157/2/1
    /// je v. Dazu die Gegenprobe vor dem Abriss: jede heile Rampe steht ganz im Bild.
    /// </summary>
    /// <param name="boden">das Bodenbild <c>map_NN.png</c>, wie es der Spieler hat</param>
    /// <param name="objekte"><c>map_NN.objects.png</c> (darin der Streifen)</param>
    public string RampenStreifenCheck(Image? boden, Image? objekte)
    {
        var sb = new StringBuilder("rampen-streifen-check"
            + (RampenGebackenAlt ? " — NULLMODELL --rampen-gebacken-alt" : "") + "\n");
        if (boden == null || objekte == null) return sb.Append("  KEIN URTEIL — Bodenbild/Objektebene nicht lesbar").ToString();
        if (_kartenRampen.Count == 0)
            return sb.Append($"  KEIN URTEIL — keine Kartenrampe mit Rampenkachel ({RampenZahl} sec21-Saetze, "
                           + $"{RampenZellen} Lagenzellen)").ToString();
        bool Deckt(int x, int y) => objekte.GetPixel(x, y).A >= 0.5f;
        bool Gleich(Color a, Color b) => Mathf.Abs(a.R - b.R) < 0.02f && Mathf.Abs(a.G - b.G) < 0.02f && Mathf.Abs(a.B - b.B) < 0.02f;
        int restSumme = 0, gemessen = 0, ganzVor = 0;
        var restJeV = new SortedDictionary<int, string>();
        foreach (var (z, code, elev) in new List<(Vector2I, int, int)>(_kartenRampen))
        {
            var rp = RampeAn(z.X, z.Y);
            if (rp is not { Karte: true, Tp: >= 200 }) continue;
            if (StreifenKachel(code) is not { } k) { sb.AppendLine($"  ({z.X},{z.Y}): Kachel {code} fehlt im Streifen"); continue; }
            int v = code - Import.MapBaker.RampenKachelBasis;
            var tr = StreifenKachel(Import.MapBaker.RampenKachelBasis + 24 + v);
            var ziel = KartenrampeZiel(z, elev, k);
            var punkte = new List<(int Wx, int Wy, Color F)>();
            for (int y = 0; y < (int)k.Feld.Size.Y; y++)
                for (int x = 0; x < (int)k.Feld.Size.X; x++)
                {
                    int sx = (int)k.Feld.Position.X + x, sy = (int)k.Feld.Position.Y + y;
                    if (Deckt(sx, sy)) punkte.Add(((int)ziel.Position.X + x, (int)ziel.Position.Y + y, objekte.GetPixel(sx, sy)));
                }
            // Bildpunkt im Bodenbild = Weltpunkt: das Bodenbild liegt bei (0, 0), und der
            // Backofen setzt die Kachel auf sy + YOff mit sy = _oy − GroundLift + Zeile·20 −
            // Hoehe·15 — genau dorthin, wo CellRect + BlitAnchor + YOff hinzeigt (GroundLift =
            // −BlitAnchor = 50). ⚠ Der erste Lauf zog GroundLift noch einmal ab: 8 von 995.
            bool Gebacken((int Wx, int Wy, Color F) p)
            {
                int bx = p.Wx, by = p.Wy;
                return bx >= 0 && by >= 0 && bx < boden.GetWidth() && by < boden.GetHeight() && Gleich(boden.GetPixel(bx, by), p.F);
            }
            bool lebendVor = false;
            foreach (var q in KartenrampenKacheln()) if (q.Zelle == z) lebendVor = true;
            int gebacken = 0, sichtbarVor = 0;
            foreach (var p in punkte) if (Gebacken(p)) gebacken++;
            // ⚠ Je RAMPE entschieden, nicht je Punkt: steht hoechstens die Haelfte der Punkte
            // farbgleich im Bodenbild, ist die Rampe nicht gebacken, und die Treffer sind
            // Farbzufall der Grundfuellung (gemessen: neue Daten 0..57 von 880..995 je Rampe, alte 807..995).
            bool rampeGebacken = gebacken * 2 > punkte.Count;
            foreach (var p in punkte) if (rampeGebacken && Gebacken(p) || lebendVor) sichtbarVor++;
            if (sichtbarVor == punkte.Count) ganzVor++;
            RampeAbreissen(rp);
            bool lebendNach = false;
            foreach (var q in KartenrampenKacheln()) if (q.Zelle == z) lebendNach = true;
            Rect2? tz = tr is { } t ? KartenrampeZiel(z, ElevOf(z.X, z.Y), t) : null;
            int rest = 0;
            foreach (var p in punkte)
            {
                if (!(lebendNach || rampeGebacken && Gebacken(p))) continue;
                bool gedeckt = false;
                if (tr is { } t2 && tz is { } r2)
                {
                    int tx = p.Wx - (int)r2.Position.X, ty = p.Wy - (int)r2.Position.Y;
                    gedeckt = tx >= 0 && ty >= 0 && tx < (int)t2.Feld.Size.X && ty < (int)t2.Feld.Size.Y
                              && Deckt((int)t2.Feld.Position.X + tx, (int)t2.Feld.Position.Y + ty);
                }
                if (!gedeckt) rest++;
            }
            gemessen++; restSumme += rest;
            restJeV[v] = restJeV.TryGetValue(v, out var alt) ? $"{alt},{rest}" : $"{rest}";
            sb.AppendLine($"  Rampe Platz {rp.Slot} ({z.X},{z.Y}) v {v}: {punkte.Count} deckende Punkte, vorher gebacken "
                        + $"{gebacken}{(rampeGebacken ? "" : " (Farbzufall, nicht gebacken)")}, lebend {(lebendVor ? "ja" : "nein")}, im Bild {sichtbarVor}/{punkte.Count}; "
                        + $"nach dem Abriss lebend {(lebendNach ? "JA" : "nein")}, REST {rest}"
                        + (tr == null ? " (Truemmerkachel fehlt im Streifen)" : ""));
        }
        sb.Append("  Rest je v:");
        foreach (var (v, s) in restJeV) sb.Append($" v{v}={s}");
        sb.AppendLine();
        sb.AppendLine($"  {gemessen} Rampen abgerissen, REST-Punkte gesamt {restSumme} (Soll 0); heil ganz im Bild vorher {ganzVor}/{gemessen}");
        sb.Append(gemessen == 0 ? "  KEIN URTEIL" : restSumme == 0 && ganzVor == gemessen ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
