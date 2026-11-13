namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// ⭐ 13.11.2026 — D9 (Fable-Pruefung): <c>--bruecken-muster</c> — DIE MESSLATTE fuer die
/// Editorbruecke. Liest jede Bruecke (sec17) aller Original-CWM unter
/// <c>Assets/Legacy/LEVELS</c> und zaehlt Zelle fuer Zelle, ob Kachel, Suchkarte (sec6),
/// Lage (sec20), Hoehe und Byte +3 der gelesenen Regel von <c>0x4CC280</c> folgen
/// (Simulation/Pionierbruecke.cs, PionierbrueckeHoch.cs). Mit <c>=&lt;cwm-datei&gt;</c>
/// statt der Originale prueft dieselbe Probe eine Editorkarte (<see cref="Pruefen"/>).
/// </summary>
public static class BrueckenMuster
{
    /// <summary>Die Zellen einer Kartenbruecke aus ihrem sec17-Satz: Ecke oben links,
    /// <c>+0x02</c> = 1 senkrecht, Laenge k (Wasserzellen), 3 Streifen × (k+2).
    /// Rueckgabe je Zelle: Streifen s (0..2), Position p (0..k+1).</summary>
    public static IEnumerable<(int C, int R, int S, int P)> Zellen(CwmExtra.Bridge b)
    {
        bool senk = b.Dir != 0;
        for (int s = 0; s < 3; s++)
            for (int p = 0; p < b.Len + 2; p++)
                yield return senk ? (b.Col + s, b.Row + p, s, p) : (b.Col + p, b.Row + s, s, p);
    }

    /// <summary>Die Lage einer Zelle im Kachelfeld (0x4CC280): waagerecht 3·s + Pos,
    /// senkrecht 9 + 3·s + Pos; Pos 0 Anfang, 1 Mitte, 2 Ende.</summary>
    public static int Lage(bool senk, int s, int p, int len)
    {
        int pos = p == 0 ? 0 : p == len + 1 ? 2 : 1;
        return (senk ? 9 : 0) + 3 * s + pos;
    }

    public static int Pruefen(IEnumerable<(string Name, CwmFile M)> karten, Action<string> say)
    {
        int brueck = 0, zellen = 0;
        var z = new Dictionary<string, (int Ok, int Alle)>();
        void Z(string k, bool ok) { z.TryGetValue(k, out var v); z[k] = (v.Ok + (ok ? 1 : 0), v.Alle + 1); }
        var saetze = new Dictionary<int, int>();
        var laengen = new Dictionary<int, int>();
        int gezeigt = 0;
        foreach (var (name, m) in karten)
        {
            var imap = m.Sec(6); var lage20 = m.Sec(20);
            foreach (var b in CwmExtra.Bridges(m))
            {
                brueck++;
                bool senk = b.Dir != 0;
                laengen[b.Len] = laengen.GetValueOrDefault(b.Len) + 1;
                int satz = b.Feld[0] / 120;
                saetze[satz] = saetze.GetValueOrDefault(satz) + 1;
                Z("Laenge 1..3", b.Len is >= 1 and <= 3);
                Z("TP 500", b.Hp == 500);
                int kopfH = m.ElevAt(senk ? b.Col + 1 : b.Col, senk ? b.Row : b.Row + 1);
                var dump = new List<string>();
                foreach (var (c, r, s, p) in Zellen(b))
                {
                    zellen++;
                    int code = m.CodeAt(c, r);
                    int roh = code - 10000;
                    bool mitte = s == 1;
                    bool kopf = mitte && (p == 0 || p == b.Len + 1);
                    Z("Code >= 10000", code >= 10000);
                    bool lok = roh >= 0 && (roh % 120) % 18 == Lage(senk, s, p, b.Len);
                    Z("Lage = (Code % 120) % 18", lok);
                    if (!lok) say($"bruecken-muster:   Ausnahme {name} Platz {b.Slot} ({s},{p}) k={b.Len} {(senk ? "senk" : "waag")}: Code-10000 {roh} (%18 = {roh % 18}, erwartet {Lage(senk, s, p, b.Len)}, Stufe {(roh % 54) / 18})");
                    Z("Bildsatz = Feld[0]/120 je Zelle", roh >= 0 && roh / 120 == satz);
                    int iv = imap == null ? -1 : imap[(c * MapFactory.ImapStride + r) * 2] | (imap[(c * MapFactory.ImapStride + r) * 2 + 1] << 8);
                    Z(mitte ? "sec6 Fahrbahn 0xFFFE" : "sec6 Gelaender 0xFFFF", iv == (mitte ? 0xFFFE : 0xFFFF));
                    int l20 = lage20 == null ? -1 : lage20[c * MapFactory.ImapStride + r];
                    Z("sec20 = 100 + Platz", l20 == 100 + b.Slot);
                    int h = m.ElevAt(c, r);
                    if (mitte && !kopf) Z("Hoehe Fahrbahn = Kopf + 1", h == kopfH + 1);
                    else Z("Hoehe Kopf/Gelaender = Kopf", h == kopfH);
                    int f = m.FlagAt(c, r);
                    if (kopf) Z("Byte+3 Kopf (1/3 waagerecht, 2/4 senkrecht)",
                                f == (senk ? (p == 0 ? 2 : 4) : (p == 0 ? 1 : 3)));
                    else if (mitte) Z("Byte+3 Fahrbahn 0", f == 0);
                    else Z("Byte+3 Gelaender 0", f == 0);
                    // das 3x5-Feld: Index s*5+p (Probe, welche Ordnung gilt)
                    if (p < 5) Z("Feld[s*5+p] = Code-10000 (mod 256)", (b.Feld[s * 5 + p] & 0xFF) == (roh & 0xFF));
                    if (gezeigt < 2) dump.Add($"({s},{p}) code {roh} imap {iv:X4} l20 {l20} h {h} f {f}");
                }
                {
                    var r17 = m.Sec(17)!; int o17 = b.Slot * CwmExtra.BridgeStride;
                    Z("sec17 +0x14 = 0", r17[o17 + 0x14] == 0);
                    Z("sec17 +0x15 = 0", r17[o17 + 0x15] == 0);
                    Z("sec17 +0x02 nur 0/1", r17[o17 + 0x02] <= 1);
                }
                if (gezeigt < 2)
                {
                    say($"bruecken-muster: {name} Platz {b.Slot} Ecke ({b.Col},{b.Row}) {(senk ? "senkrecht" : "waagerecht")} k={b.Len} " +
                        $"Feld [{string.Join(" ", b.Feld)}]");
                    say("   " + string.Join(" | ", dump));
                    gezeigt++;
                }
            }
        }
        say($"bruecken-muster: {brueck} Bruecken, {zellen} Zellen; Laengen {string.Join(" ", laengen.OrderBy(k => k.Key).Select(k => $"{k.Key}x{k.Value}"))}; " +
            $"Bildsaetze {string.Join(" ", saetze.OrderBy(k => k.Key).Select(k => $"{k.Key}x{k.Value}"))}");
        int falsch = 0;
        foreach (var (k, v) in z)
        {
            say($"bruecken-muster:   {k}: {v.Ok} von {v.Alle}");
            if (v.Ok != v.Alle) falsch++;
        }
        return falsch;
    }

    /// <summary><c>--bruecken-muster</c> (Originale) oder <c>--bruecken-muster=&lt;cwm&gt;</c>.</summary>
    public static int Run(string arg, Action<string> say)
    {
        var karten = new List<(string, CwmFile)>();
        if (arg.Length > 0) karten.Add((arg, CwmFile.Load(arg)));
        else
            foreach (var f in Directory.GetFiles(ProjectSettings.GlobalizePath("res://Assets/Legacy/LEVELS"), "*.CWM").OrderBy(x => x))
                karten.Add((Path.GetFileName(f), CwmFile.Load(f)));
        int falsch = Pruefen(karten, say);
        say($"bruecken-muster: {(falsch == 0 ? "alle Regeln ohne Ausnahme" : $"{falsch} Regeln mit Ausnahmen")}");
        return 0;
    }
}
