namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// ⭐ 13.11.2026 — D9 (Fable-Pruefung): DIE BRUECKE IM EDITOR (Taste 0).
///
/// <para><b>Geschrieben wird, was der Brueckenbau des Originals schreibt</b> (<c>0x4CC280</c>,
/// gelesen in Simulation/Pionierbruecke.cs und PionierbrueckeHoch.cs), aber in die DATEI statt in
/// den laufenden Zustand. <b>GEMESSEN an allen 52 Bruecken der 23 Original-CWM</b>
/// (<c>--bruecken-muster</c>, 705 Zellen) — jede Regel ohne Ausnahme:</para>
/// <list type="bullet">
/// <item>3 Streifen × (k+2) Zellen, k = 1..3 Wasserzellen; sec17-Satz: Ecke oben links
///   +0x00/+0x01, +0x02 = 1 senkrecht, +0x03..+0x11 das 3x5-Feld (Feld[s·5+p] = Code − 10000),
///   +0x12 = 1, +0x13 = k, +0x16 = 500 TP, +0x14/+0x15 = 0.</item>
/// <item>Kachel = 10000 + 120·Bildsatz + 54·Variante + Lage; Lage waagerecht 3·s + Pos,
///   senkrecht 9 + 3·s + Pos (Pos 0 Anfang, 1 Mitte, 2 Ende).</item>
/// <item>sec6: Fahrbahn (Mittelstreifen) 0xFFFE, Gelaender 0xFFFF; sec20 = 100 + Platz.</item>
/// <item>Hoehe: Fahrbahn ueber dem Wasser = Kopf + 1, Koepfe und Gelaender = Kopf.</item>
/// <item>Byte +3: Koepfe waagerecht 1/3, senkrecht 2/4; sonst 0.</item>
/// </list>
///
/// <para><b>UNSERE SETZUNGEN:</b> Bildsatz 0 (51 von 52 Kartenbruecken; der Pionier baut
/// Satz 1, den Holzsteg). Die Variante je ZELLE aus einem festen Hash (die Karten mischen
/// sie je Zelle, z. B. Feld 63 10 10 10 65 …). Die Startzelle muss Land sein (frei oder rau
/// — der Pionier verlangt rau, im Editor setzt man die Bruecke ohne ihn), beide Ufer gleich
/// hoch (die Karten: Kopf = Kopf, 104 von 104), und nichts darf auf den Zellen stehen
/// (der Pionier erschlaegt es, der Editor weist ab). Gegenschalter <c>--editor-bruecke-aus</c>
/// (Werkzeug weist ab). Wegnehmen: Rueckgaengig (Strg+Z).</para>
/// </summary>
public static class MapBridgeEdit
{
    public static bool Aus => Array.IndexOf(Core.CommandLine.Args, "--editor-bruecke-aus") >= 0;

    /// <summary>Die Richtungstafel der Bruecke (0x4CC280: {3,12,10,5}) — oben, unten, rechts, links.</summary>
    private static readonly (int X, int Y)[] Lauf = { (0, -1), (0, 1), (1, 0), (-1, 0) };
    public const int MaxWasser = 3, Tp = 500, Bildsatz = 0;

    public sealed class Plan
    {
        public int Col, Row, Richtung, K, EckeC, EckeR;
        public bool Senkrecht;
        public List<(int C, int R, int S, int P)> Zellen = new();   // S, P in Satzkoordinaten
    }

    /// <summary>Die Bruecke, die von dieser Uferzelle aus entstuende — oder der Grund, warum nicht.</summary>
    public static Plan? Planen(CwmFile m, int col, int row, out string warum)
    {
        warum = "";
        if (Aus) { warum = "Brücken sind mit --editor-bruecke-aus abgeschaltet"; return null; }
        if (col < 0 || row < 0 || col >= m.Width || row >= m.Height) { warum = "außerhalb der Karte"; return null; }
        if (MapEditTerrain.ClassAt(m, col, row) is not (0 or 1))
        { warum = "eine Brücke beginnt auf einer Uferzelle (Land, frei oder rau)"; return null; }
        int d = -1;
        for (int i = 0; i < 4; i++)
        {
            int c = col + Lauf[i].X, r = row + Lauf[i].Y;
            if (MapEditTerrain.ClassAt(m, c, r) != 2) continue;
            if (d >= 0) { warum = "Wasser an zwei Seiten — die Richtung ist nicht eindeutig"; return null; }
            d = i;
        }
        if (d < 0) { warum = "neben dieser Zelle ist kein Wasser"; return null; }
        int k = -1;
        for (int n = 1; n <= MaxWasser + 1; n++)
        {
            int c = col + Lauf[d].X * n, r = row + Lauf[d].Y * n;
            int cls = MapEditTerrain.ClassAt(m, c, r);
            if (c < 0 || r < 0 || c >= m.Width || r >= m.Height) break;
            if (cls == 2) continue;
            if (cls is 0 or 1 && n - 1 >= 1) k = n - 1;
            break;
        }
        if (k < 0) { warum = "das Gegenufer liegt weiter als drei Wasserzellen (oder ist kein Land)"; return null; }
        var ziel = (C: col + Lauf[d].X * (k + 1), R: row + Lauf[d].Y * (k + 1));
        if (m.ElevAt(ziel.C, ziel.R) != m.ElevAt(col, row))
        { warum = $"die Ufer liegen verschieden hoch ({m.ElevAt(col, row)} und {m.ElevAt(ziel.C, ziel.R)}) — erst angleichen"; return null; }

        var plan = new Plan { Col = col, Row = row, Richtung = d, K = k, Senkrecht = d is 0 or 1 };
        var quer = (X: Lauf[d].Y, Y: Lauf[d].X);
        var roh = new List<(int C, int R)>();
        for (int s = -1; s <= 1; s++)
            for (int p = 0; p < k + 2; p++)
                roh.Add((col + Lauf[d].X * p + quer.X * s, row + Lauf[d].Y * p + quer.Y * s));
        int ec = int.MaxValue, er = int.MaxValue;
        foreach (var (c, r) in roh) { ec = Math.Min(ec, c); er = Math.Min(er, r); }
        plan.EckeC = ec; plan.EckeR = er;
        var lage20 = m.Sec(20);
        foreach (var (c, r) in roh)
        {
            if (c < 0 || r < 0 || c >= m.Width || r >= m.Height) { warum = "die Brücke ragt über den Kartenrand"; return null; }
            if (lage20 != null && lage20[c * MapFactory.ImapStride + r] >= 100) { warum = $"({c},{r}) trägt schon eine Brücke oder Rampe"; return null; }
            if (MapFactory.Belegt(m, c, r) is { } b) { warum = $"({c},{r}): {b}"; return null; }
            if (m.CodeAt(c, r) >= CwpFile.ObjectCodeBase) { warum = $"({c},{r}) trägt ein Objekt (Baum, Fels …) — erst wegnehmen"; return null; }
            int sS = plan.Senkrecht ? c - ec : r - er, pS = plan.Senkrecht ? r - er : c - ec;
            plan.Zellen.Add((c, r, sS, pS));
        }
        if (FreierPlatz(m) < 0) { warum = "kein freier Brückenplatz (100)"; return null; }
        return plan;
    }

    public static int FreierPlatz(CwmFile m)
    {
        var s = m.Sec(17);
        if (s == null) return -1;
        for (int i = 0; i < CwmExtra.BridgeSlots && (i + 1) * CwmExtra.BridgeStride <= s.Length; i++)
            if (s[i * CwmExtra.BridgeStride + 0x12] == 0) return i;
        return -1;
    }

    /// <summary>Die Bruecke in die Karte schreiben. Rueckgabe: der sec17-Platz, oder −1.</summary>
    public static int Setzen(CwmFile m, Plan plan, uint seed)
    {
        int slot = FreierPlatz(m);
        var s17 = m.Sec(17); var imap = m.Sec(6); var lage20 = m.Sec(20);
        if (slot < 0 || s17 == null || imap == null || lage20 == null) return -1;
        int o = slot * CwmExtra.BridgeStride;
        Array.Clear(s17, o, CwmExtra.BridgeStride);
        int kopfH = m.ElevAt(plan.Col, plan.Row);
        foreach (var (c, r, sS, pS) in plan.Zellen)
        {
            int pos = pS == 0 ? 0 : pS == plan.K + 1 ? 2 : 1;
            int lage = (plan.Senkrecht ? 9 : 0) + 3 * sS + pos;
            int variante = (int)(MapTerrain.Hash(c, r, seed ^ 0xB2D6u) & 1);
            int roh = 120 * Bildsatz + 54 * variante + lage;
            bool mitte = sS == 1, kopf = mitte && (pS == 0 || pS == plan.K + 1);
            int h = mitte && !kopf ? kopfH + 1 : kopfH;
            int flag = !kopf ? 0 : plan.Senkrecht ? (pS == 0 ? 2 : 4) : (pS == 0 ? 1 : 3);
            m.SetCell(c, r, 10000 + roh, h, flag);
            int i = (c * MapFactory.ImapStride + r) * 2;
            int iv = mitte ? 0xFFFE : 0xFFFF;
            imap[i] = (byte)(iv & 0xFF); imap[i + 1] = (byte)(iv >> 8);
            lage20[c * MapFactory.ImapStride + r] = (byte)(100 + slot);
            if (pS < 5) s17[o + 3 + sS * 5 + pS] = (byte)roh;
        }
        s17[o + 0x00] = (byte)plan.EckeC; s17[o + 0x01] = (byte)plan.EckeR;
        s17[o + 0x02] = (byte)(plan.Senkrecht ? 1 : 0);
        s17[o + 0x12] = 1;
        s17[o + 0x13] = (byte)plan.K;
        s17[o + 0x16] = Tp & 0xFF; s17[o + 0x17] = Tp >> 8;
        return slot;
    }
}

public partial class MapEditOverlay
{
    private void PutBruecke(Vector2I cell)
    {
        var plan = MapBridgeEdit.Planen(_map, cell.X, cell.Y, out string warum);
        if (plan == null) { _say = $"keine Brücke: {warum}"; return; }
        int slot = MapBridgeEdit.Setzen(_map, plan, MapEditSession.Seed);
        if (slot < 0) { _say = "keine Brücke: der Satz ließ sich nicht schreiben"; return; }
        _mine.Add((cell.X, cell.Y, Brush.Bruecke));
        MapEditSession.Dirty = true;
        _say = $"Brücke Platz {slot}: {plan.K} Wasserzelle(n), {(plan.Senkrecht ? "senkrecht" : "waagerecht")}, " +
               $"{plan.Zellen.Count} Zellen ab ({plan.EckeC},{plan.EckeR})";
    }

    /// <summary>Die Vorschau: die ganze Bruecke gruen, oder die Startzelle rot.</summary>
    private void BrueckeVorschauZeichnen()
    {
        if (_brush != Brush.Bruecke || _hover is not { } h) return;
        if (_brueckeArt == 1)
        {
            if (MapRampEdit.Planen(_map, MapEditSession.Cwp, h.X, h.Y, MapEditSession.Seed, out _, out _) < 0) return;
            DrawRect(new Rect2(_layer.EditorCellTopLeft(h.X, h.Y), MapBaker.TileW, MapBaker.TileH), new Color(0.4f, 1f, 0.5f, 0.4f));
            return;
        }
        var plan = MapBridgeEdit.Planen(_map, h.X, h.Y, out _);
        if (plan == null) return;
        var col = new Color(0.4f, 1f, 0.5f, 0.95f);
        foreach (var (c, r, sS, _) in plan.Zellen)
        {
            var p = _layer.EditorCellTopLeft(c, r);
            DrawRect(new Rect2(p, MapBaker.TileW, MapBaker.TileH), new Color(col.R, col.G, col.B, sS == 1 ? 0.35f : 0.18f));
        }
    }
}
