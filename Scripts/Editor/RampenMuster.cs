namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// ⭐ 13.11.2026 — D9: <c>--rampen-muster</c> — DIE MESSLATTE fuer die Editorrampe. Liest
/// jede Rampe (sec21) aller Original-CWM unter <c>Assets/Legacy/LEVELS</c> und zaehlt, ob
/// Satz, Kachel, Suchkarte (sec6), Lage (sec20), Richtung, Hoehe und Byte +3 der Regel des
/// Molenbaus folgen (Simulation/Landungsbruecke.cs: Bild 2·Richtung + Zufall&amp;1, Kachel
/// 10723 + Bild, sec6 0xFFFE, sec20 200 + Platz, 200 TP). Mit <c>=&lt;cwm&gt;</c> eine Datei.
/// </summary>
public static class RampenMuster
{
    /// <summary>Richtungen der Rampe: 0 links, 1 oben, 2 rechts, 3 unten (Landungsbruecke.cs).</summary>
    public static readonly (int X, int Y)[] Seiten = { (-1, 0), (0, -1), (1, 0), (0, 1) };

    public static int Pruefen(IEnumerable<(string Name, CwmFile M)> karten, Action<string> say)
    {
        var z = new Dictionary<string, (int Ok, int Alle)>();
        void Z(string k, bool ok) { z.TryGetValue(k, out var v); z[k] = (v.Ok + (ok ? 1 : 0), v.Alle + 1); }
        var hDiff = new Dictionary<string, int>();
        var flags = new Dictionary<int, int>();
        int n = 0, echt = 0, rest = 0;
        foreach (var (name, m) in karten)
        {
            var imap = m.Sec(6); var l20 = m.Sec(20);
            foreach (var r in CwmExtra.Ramps(m))
            {
                n++;
                int c = r.Col, w = r.Row;
                Z("alle Saetze: Bild 0..7", r.Mark is >= 0 and <= 7);
                Z("alle Saetze: TP 200", r.Count == 200);
                Z("alle Saetze: sec20 = 200 + Platz", l20 != null && l20[c * MapFactory.ImapStride + w] == 200 + r.Slot);
                // ⚠ Restsaetze ohne Rampenkachel (08/09/NET07, bug-694 — 09 traegt die
                // Koordinaten von 08) sind keine gebauten Rampen: die Regeln unten gelten
                // nur fuer Saetze, deren Kachel liegt.
                if (m.CodeAt(c, w) != 10723 + r.Mark) { rest++; continue; }
                echt++;
                say($"rampen-muster:   echt: {name} Kachelsatz {m.Tileset} ({c},{w}) Bild {r.Mark}");
                int i = (c * MapFactory.ImapStride + w) * 2;
                int iv = imap == null ? -1 : imap[i] | (imap[i + 1] << 8);
                Z("echte: sec6 0xFFFE", iv == 0xFFFE);

                int wasser = -1, zahl = 0;
                for (int d = 0; d < 4; d++)
                {
                    int cc = c + Seiten[d].X, rr = w + Seiten[d].Y;
                    int cls = MapEditTerrain.ClassAt(m, cc, rr);
                    // ⚠ Wasser auch dort, wo eine andere Rampe/Bruecke liegt, zaehlt hier nicht
                    // Wasser = Klasse 2, oder eine Wasserkachel (Code 0..7) unter Klasse 3
                    if (cls == 2 || m.CodeAt(cc, rr) < 8) { zahl++; wasser = d; }
                }
                if (zahl == 1) Z("echte: Richtung = Bild/2 (Wasserseite)", r.Mark / 2 == wasser);
                Z("echte: genau 1 Wassernachbar", zahl == 1);
                if (zahl >= 1)
                {
                    int wd = wasser;
                    int hw = m.ElevAt(c + Seiten[wd].X, w + Seiten[wd].Y);
                    int hl = m.ElevAt(c - Seiten[wd].X, w - Seiten[wd].Y);
                    string k = $"Rampe−Wasser {m.ElevAt(c, w) - hw:+0;-0;0}, Rampe−Land {m.ElevAt(c, w) - hl:+0;-0;0}";
                    hDiff[k] = hDiff.GetValueOrDefault(k) + 1;
                }
                if (iv != 0xFFFE || zahl != 1)
                {
                    var nb = string.Join(" ", Seiten.Select(sd => $"{MapEditTerrain.ClassAt(m, c + sd.X, w + sd.Y)}/{m.CodeAt(c + sd.X, w + sd.Y)}"));
                    say($"rampen-muster:   Ausnahme {name} Platz {r.Slot} ({c},{w}) Bild {r.Mark}: Code {m.CodeAt(c, w)}, sec6 {iv:X4}, Wassernachbarn {zahl}, h {m.ElevAt(c, w)}, Nachbarn L/O/R/U Klasse/Code {nb}");
                }
                int f = m.FlagAt(c, w);
                flags[f] = flags.GetValueOrDefault(f) + 1;
            }
        }
        say($"rampen-muster: {n} Rampensaetze, davon {echt} mit Rampenkachel, {rest} Restsaetze ohne Kachel");
        foreach (var (k, v) in hDiff.OrderByDescending(x => x.Value)) say($"rampen-muster:   Hoehen {k}: {v}x");
        say($"rampen-muster:   Byte+3: {string.Join(" ", flags.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}x"))}");
        int falsch = 0;
        foreach (var (k, v) in z)
        {
            say($"rampen-muster:   {k}: {v.Ok} von {v.Alle}");
            if (v.Ok != v.Alle) falsch++;
        }
        return falsch;
    }

    public static int Run(string arg, Action<string> say)
    {
        var karten = new List<(string, CwmFile)>();
        if (arg.Length > 0) karten.Add((arg, CwmFile.Load(arg)));
        else
            foreach (var f in Directory.GetFiles(ProjectSettings.GlobalizePath("res://Assets/Legacy/LEVELS"), "*.CWM").OrderBy(x => x))
                karten.Add((Path.GetFileName(f), CwmFile.Load(f)));
        int falsch = Pruefen(karten, say);
        say($"rampen-muster: {(falsch == 0 ? "alle Regeln ohne Ausnahme" : $"{falsch} Regeln mit Ausnahmen")}");
        return 0;
    }
}
