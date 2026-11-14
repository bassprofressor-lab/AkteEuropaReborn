namespace AkteEuropaReborn.Network;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

/// <summary>
/// ⭐ 14.11.2026 — Fable D8: <b>die Prüfsumme der eingelesenen Spieldaten</b> (<c>content_hash</c>
/// der Lobby). Bisher schickte das Spiel immer <c>"00000000"</c>.
///
/// <para>Zwei Rechner mit derselben Fassung können trotzdem verschiedene Daten haben — ein anderer
/// Importstand, eine andere CD-Ausgabe, eine abgebrochene Einlesung. Dann rechnen sie dieselbe Partie
/// verschieden und laufen auseinander, und das sieht aus wie ein Rechenfehler. Gerechnet über das,
/// was die Simulation liest: die Spieltabellen (<c>Maps/*.json</c> ohne Karten), die
/// Einheitenverzeichnisse und die gelieferten Mehrspielerkarten NET01–NET08. Bilder, Töne und
/// Filme zählen nicht (sie ändern die Rechnung nicht).</para>
///
/// <para>Der Server vergleicht nichts (LOBBY_SERVER_AUFTRAG.md §3.2); das Spiel WARNT im Raum bei
/// einem Mitspieler mit anderer Summe — es sperrt nicht, denn eine Abweichung in einer Karte, die
/// nicht gespielt wird, schadet nicht. Einmal je Programmlauf gerechnet, zwischengespeichert nach
/// Dateigröße und -zeit (53 MB). Gegenschalter <c>--inhaltssumme-alt</c> (wieder »00000000«).</para>
/// </summary>
public static class Inhaltssumme
{
    private static string? _summe;
    private static string _schluessel = "";

    public static bool Alt => System.Array.IndexOf(Core.CommandLine.Args, "--inhaltssumme-alt") >= 0;

    /// <summary>8 Hexzeichen; »00000000«, wenn nichts eingelesen ist (oder unter dem Gegenschalter).</summary>
    public static string Wert()
    {
        if (Alt) return "00000000";
        // Pruefschalter: andere Spieldaten vortaeuschen (Gegenprobe der Warnung im Raum)
        foreach (string a in Core.CommandLine.Args)
            if (a.StartsWith("--inhaltssumme-test=")) return a["--inhaltssumme-test=".Length..];
        var dateien = Dateien();
        string schluessel = string.Join("|", dateien.Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}"));
        if (_summe != null && schluessel == _schluessel) return _summe;
        ulong t0 = Time.GetTicksMsec();
        ulong h = 14695981039346656037UL;
        void Mische(byte b) { h ^= b; h *= 1099511628211UL; }
        var puffer = new byte[1 << 16];
        foreach (var f in dateien)
        {
            foreach (char c in f.Name) Mische((byte)c);
            using var s = f.OpenRead();
            int n;
            while ((n = s.Read(puffer, 0, puffer.Length)) > 0)
                for (int i = 0; i < n; i++) { h ^= puffer[i]; h *= 1099511628211UL; }
        }
        uint kurz = (uint)(h ^ (h >> 32));
        _summe = dateien.Count == 0 ? "00000000" : kurz.ToString("x8");
        _schluessel = schluessel;
        GD.Print($"inhaltssumme: {_summe} über {dateien.Count} Dateien ({dateien.Sum(f => f.Length) / 1024} KB) in {Time.GetTicksMsec() - t0} ms");
        return _summe;
    }

    private static List<FileInfo> Dateien()
    {
        var liste = new List<FileInfo>();
        string karten = System.IO.Path.Combine(Core.Content.UserDir, "Maps");
        if (Directory.Exists(karten))
            foreach (string p in Directory.GetFiles(karten, "*.json"))
            {
                string n = System.IO.Path.GetFileName(p);
                if (!n.StartsWith("map_") || IstNetzkarte(n)) liste.Add(new FileInfo(p));
            }
        foreach (string rel in new[] { "Units/units_index.json", "Units/parts_index.json" })
        {
            string p = System.IO.Path.Combine(Core.Content.UserDir, rel);
            if (File.Exists(p)) liste.Add(new FileInfo(p));
        }
        liste.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return liste;
    }

    private static bool IstNetzkarte(string n)
        => n.Length >= 9 && n.StartsWith("map_NET0") && char.IsDigit(n[8]) && n.EndsWith(".json");
}
