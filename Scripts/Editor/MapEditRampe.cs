namespace AkteEuropaReborn.Editor;

using System;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// ⭐ 13.11.2026 — D9 (Fable-Pruefung): DIE LANDUNGSRAMPE IM EDITOR (Werkzeug 0, Auswahl
/// »Landungsrampe«). Die Rampe ist der Fahrzeug-Landeplatz am Ufer: Fahrzeuge steigen nur
/// auf Zellen mit Lage ≥ 200 auf Schiffe ein und aus (@0x438440, @0x409383).
///
/// <para><b>Geschrieben wird, was der Molenbau des Originals schreibt</b> (Simulation/
/// Landungsbruecke.cs, Lesung berichte/landungsbruecke-fable.md): EINE Zelle, sec21-Satz
/// {Spalte, Zeile, Bild, 200 TP}, Bild = 2·Richtung + Zufall&amp;1, Kachel 10723 + Bild,
/// sec6 := 0xFFFE, sec20 := 200 + Platz.</para>
///
/// <para><b>GEMESSEN an den 23 Original-CWM</b> (<c>--rampen-muster</c>): 45 Rampensaetze,
/// davon 24 mit Rampenkachel — an diesen 24 gilt jede Regel ohne Ausnahme: sec6 0xFFFE,
/// genau ein Wassernachbar, Richtung = Wasserseite (0 links, 1 oben, 2 rechts, 3 unten),
/// Rampe, Wasser und Land dahinter auf GLEICHER Hoehe, Byte +3 = 0. Die 21 Saetze ohne
/// Kachel (08, 09, NET07; 09 traegt die Koordinaten von 08) sind Restsaetze der Tafel
/// (bug-694) und keine Vorlage.</para>
///
/// <para><b>UNSERE SETZUNGEN:</b> die Startzelle darf frei ODER rau sein (der Pionier
/// verlangt rau; im Editor setzt man ohne ihn); das Bildbit aus einem festen Hash; die
/// Rampe muss auf der Hoehe ihres Wassers liegen (24 von 24). Gegenschalter
/// <c>--editor-rampe-aus</c>.</para>
/// </summary>
public static class MapRampEdit
{
    public static bool Aus => Array.IndexOf(Core.CommandLine.Args, "--editor-rampe-aus") >= 0;
    public const int KachelBasis = 10723, Tp = 200;

    /// <summary>Richtung 0..3 und das Bild, oder −1 mit Grund.</summary>
    public static int Planen(CwmFile m, CwpFile? cwp, int col, int row, uint seed, out int bild, out string warum)
    {
        bild = -1; warum = "";
        if (Aus) { warum = "Rampen sind mit --editor-rampe-aus abgeschaltet"; return -1; }
        if (col < 0 || row < 0 || col >= m.Width || row >= m.Height) { warum = "außerhalb der Karte"; return -1; }
        if (MapEditTerrain.ClassAt(m, col, row) is not (0 or 1)) { warum = "eine Rampe liegt auf einer Uferzelle (Land, frei oder rau)"; return -1; }
        if (MapFactory.Belegt(m, col, row) is { } b) { warum = b; return -1; }
        if (m.CodeAt(col, row) >= CwpFile.ObjectCodeBase) { warum = "dort liegt ein Objekt (Baum, Fels, Brücke …) — erst wegnehmen"; return -1; }
        var l20 = m.Sec(20);
        if (l20 != null && l20[col * MapFactory.ImapStride + row] >= 100) { warum = "dort liegt schon eine Brücke oder Rampe"; return -1; }
        int d = -1;
        for (int i = 0; i < 4; i++)
        {
            int c = col + RampenMuster.Seiten[i].X, r = row + RampenMuster.Seiten[i].Y;
            if (c < 0 || r < 0 || c >= m.Width || r >= m.Height) continue;
            if (MapEditTerrain.ClassAt(m, c, r) != 2) continue;
            if (d >= 0) { warum = "Wasser an zwei Seiten — die Richtung ist nicht eindeutig"; return -1; }
            d = i;
        }
        if (d < 0) { warum = "neben dieser Zelle ist kein Wasser"; return -1; }
        int hw = m.ElevAt(col + RampenMuster.Seiten[d].X, row + RampenMuster.Seiten[d].Y);
        if (m.ElevAt(col, row) != hw)
        { warum = $"die Rampe muss auf der Höhe des Wassers liegen ({hw}), die Zelle liegt auf {m.ElevAt(col, row)}"; return -1; }
        if (FreierPlatz(m) < 0) { warum = "kein freier Rampenplatz (50)"; return -1; }
        bild = 2 * d + (int)(MapTerrain.Hash(col, row, seed ^ 0x4A3Eu) & 1);
        bool bildDa;
        try { bildDa = cwp == null || !cwp.DecodeObject(KachelBasis + bild).IsEmpty; }
        catch (ArgumentOutOfRangeException) { bildDa = false; }
        if (!bildDa) { warum = $"dieser Kachelsatz hat kein Rampenbild (Code {KachelBasis + bild})"; return -1; }
        return d;
    }

    public static int FreierPlatz(CwmFile m)
    {
        var s = m.Sec(21);
        if (s == null) return -1;
        for (int i = 0; i < CwmExtra.RampSlots && (i + 1) * CwmExtra.RampStride <= s.Length; i++)
            if (s[i * CwmExtra.RampStride + 2] == CwmExtra.RampEmpty) return i;
        return -1;
    }

    public static int Setzen(CwmFile m, int col, int row, int bild)
    {
        int slot = FreierPlatz(m);
        var s21 = m.Sec(21); var imap = m.Sec(6); var l20 = m.Sec(20);
        if (slot < 0 || s21 == null || imap == null || l20 == null) return -1;
        int o = slot * CwmExtra.RampStride;
        s21[o] = (byte)col; s21[o + 1] = (byte)row; s21[o + 2] = (byte)bild; s21[o + 3] = Tp;
        m.SetCell(col, row, KachelBasis + bild, m.ElevAt(col, row), 0);
        int i = (col * MapFactory.ImapStride + row) * 2;
        imap[i] = 0xFE; imap[i + 1] = 0xFF;
        l20[col * MapFactory.ImapStride + row] = (byte)(200 + slot);
        return slot;
    }
}

public partial class MapEditOverlay
{
    /// <summary>Werkzeug 0: 0 = Brücke, 1 = Landungsrampe.</summary>
    private int _brueckeArt;

    private void PutRampe(Vector2I cell)
    {
        int d = MapRampEdit.Planen(_map, MapEditSession.Cwp, cell.X, cell.Y, MapEditSession.Seed, out int bild, out string warum);
        if (d < 0) { _say = $"keine Rampe: {warum}"; return; }
        int slot = MapRampEdit.Setzen(_map, cell.X, cell.Y, bild);
        if (slot < 0) { _say = "keine Rampe: der Satz ließ sich nicht schreiben"; return; }
        _mine.Add((cell.X, cell.Y, Brush.Bruecke));
        MapEditSession.Dirty = true;
        string seite = d switch { 0 => "links", 1 => "oben", 2 => "rechts", _ => "unten" };
        _say = $"Landungsrampe Platz {slot} auf ({cell.X},{cell.Y}), Wasser {seite}";
    }
}
