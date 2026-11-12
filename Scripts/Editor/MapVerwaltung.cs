namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// D8 (Fable-Pruefung, 12.11.2026): KARTENVERWALTUNG — eine eigene Karte
/// umbenennen, kopieren oder loeschen.
///
/// <para>Eine Karte sind vier Dateien in <c>user://data/Maps/</c>:
/// <c>map_X.json</c> (Bildraster), <c>map_X.entities.json</c> (Spielstand der Karte),
/// <c>map_X.png</c> (gebackenes Bild) und <c>map_X.objects.png</c> (aufragende
/// Objekte). Gemessen an map_eigen01: weitere Dateien mit dem Namen gibt es im
/// Benutzerverzeichnis nicht. Zwei davon tragen den Namen auch INNEN, im Kopf:
/// <c>"map"</c> (json: <c>map_X</c>, entities: <c>X</c>) und <c>"mission"</c>
/// (»Editor map_X 168x168«). Beim Umbenennen und Kopieren werden nur diese zwei
/// Kopffelder ersetzt — ersetzt wird im Text und nur der erste Treffer, damit der
/// Rest der Datei Byte fuer Byte bleibt, wie er war.</para>
///
/// <para>⚠ Mitgelieferte Karten (01..33, NET, DM) werden NIE angefasst
/// (<see cref="MapOpen.IsDelivered"/>); vorhandene Ziele werden nie
/// ueberschrieben. Beides ist UNSERE SETZUNG und die vorsichtigere.</para>
/// </summary>
public static class MapVerwaltung
{
    public static readonly string[] Endungen = { ".json", ".entities.json", ".png", ".objects.png" };

    private static string Voll(string name) => name.StartsWith("map_") ? name : "map_" + name;

    public static List<string> Dateien(string name)
    {
        var l = new List<string>();
        foreach (string e in Endungen)
        {
            string p = $"{MapOpen.MapsDir}/{Voll(name)}{e}";
            if (File.Exists(p)) l.Add(p);
        }
        return l;
    }

    public static bool Gibt(string name) => File.Exists($"{MapOpen.MapsDir}/{Voll(name)}.json");

    /// <summary>Warum diese Karte nicht verwaltet werden darf, oder null.</summary>
    public static string? Gesperrt(string name)
    {
        if (MapOpen.IsDelivered(Voll(name))) return "mitgelieferte Karten bleiben unangetastet";
        if (Editor.MapEditSession.Name == Voll(name) && Editor.MapEditSession.Dirty)
            return "die Karte hat ungespeicherte Pinselstriche";
        return null;
    }

    /// <summary>Kopiert alle Dateien und setzt die Namen im Kopf. null wenn es ging.</summary>
    public static string? Kopieren(string von, string nach)
    {
        string a = Voll(von), b = Voll(nach);
        if (!Gibt(a)) return $"{a[4..]} gibt es nicht";
        if (MapOpen.IsDelivered(b)) return $"»{b[4..]}« ist der Name einer mitgelieferten Karte";
        if (Gibt(b) || Dateien(b).Count > 0) return $"»{b[4..]}« gibt es schon";
        var geschrieben = new List<string>();
        try
        {
            foreach (string e in Endungen)
            {
                string q = $"{MapOpen.MapsDir}/{a}{e}", z = $"{MapOpen.MapsDir}/{b}{e}";
                if (!File.Exists(q)) continue;
                if (e.EndsWith(".json"))
                {
                    string t = File.ReadAllText(q);
                    t = KopfName(t, e == ".json" ? a : a[4..], e == ".json" ? b : b[4..]);
                    t = Ersetze(t, "\"mission\":\"", a, b);
                    File.WriteAllText(z, t);
                }
                else File.Copy(q, z);
                geschrieben.Add(z);
            }
        }
        catch (Exception ex)
        {
            foreach (string z in geschrieben) { try { File.Delete(z); } catch { } }
            return "Kopieren ging nicht: " + ex.Message;
        }
        return null;
    }

    /// <summary>Kopieren, dann das Original loeschen.</summary>
    public static string? Umbenennen(string von, string nach)
    {
        if (Gesperrt(von) is { } g) return g;
        string? f = Kopieren(von, nach);
        if (f != null) return f;
        return Loeschen(von);
    }

    public static string? Loeschen(string name)
    {
        if (Gesperrt(name) is { } g) return g;
        var l = Dateien(name);
        if (l.Count == 0) return $"{Voll(name)[4..]} gibt es nicht";
        try { foreach (string p in l) File.Delete(p); }
        catch (Exception ex) { return "Loeschen ging nicht: " + ex.Message; }
        if (Editor.MapEditSession.Name == Voll(name)) Editor.MapEditSession.Drop();
        return null;
    }

    /// <summary>Das Feld <c>"map":"…"</c> im Kopf — der erste Treffer.</summary>
    private static string KopfName(string t, string alt, string neu)
    {
        string such = $"\"map\":\"{alt}\"";
        int i = t.IndexOf(such, StringComparison.Ordinal);
        if (i < 0 || i > 200) return t;
        return t[..i] + $"\"map\":\"{neu}\"" + t[(i + such.Length)..];
    }

    /// <summary>Im Feld nach <paramref name="feld"/> (erster Treffer) den alten Namen
    /// durch den neuen ersetzen — als ganzes Wort.</summary>
    private static string Ersetze(string t, string feld, string alt, string neu)
    {
        int i = t.IndexOf(feld, StringComparison.Ordinal);
        if (i < 0 || i > 400) return t;
        int s = i + feld.Length, e = t.IndexOf('"', s);
        if (e < 0) return t;
        string w = Regex.Replace(t[s..e], $@"\b{Regex.Escape(alt)}\b", neu);
        return t[..s] + w + t[e..];
    }
}
