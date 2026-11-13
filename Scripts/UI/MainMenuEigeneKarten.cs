#nullable enable
namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// ⭐ 11.11.2026 — <b>EIGENE KARTEN IN GEFECHT UND MULTIPLAYER</b>.
///
/// <para>Was der Karteneditor schreibt (<c>map_&lt;name&gt;.entities.json</c> in
/// <c>user://data/Maps</c>), steht jetzt in der Kartenliste unter den mitgelieferten
/// Gefechtskarten — gewünscht: »eine map sollte auch multiplayer spielbar sein«. Im
/// Netzspiel reist eine solche Karte mit (<see cref="Network.NetzKarte"/>);
/// empfangene Karten (<c>map_netz_…</c>) stehen danach ebenfalls in der Liste.</para>
///
/// <para>Nicht aufgenommen: die mitgelieferten Karten (Kampagne, NET, DM, game007 —
/// <see cref="Editor.MapOpen.IsDelivered"/>); die Gefechtsauswahl der mitgelieferten
/// steht fest in <c>Maps</c>. UNSERE ZUTAT.</para>
/// </summary>
public partial class MainMenu
{
    private static void EigeneKartenAufnehmen()
    {
        // ⭐ 13.11.2026 — Fable B1: Core.Content.Path prueft mit FileExists, und ein ORDNER ist
        // keine Datei — »Maps« fiel darum immer auf res://Assets/Legacy/Maps zurueck, wo keine
        // eigene Karte liegt. Die Liste blieb in BEIDEN exe leer. Jetzt direkt der Datenordner
        // des Spielers (dorthin schreibt der Editor). Gegenschalter --eigene-karten-alt.
        string ordner = System.Array.IndexOf(Core.CommandLine.Args, "--eigene-karten-alt") >= 0
            ? Core.Content.Path("Maps") : Core.Content.UserRoot + "Maps";
        var dir = DirAccess.Open(ordner);
        if (dir == null) { GD.Print($"eigene karten: Ordner {ordner} nicht lesbar"); return; }
        var aufgenommen = new System.Collections.Generic.List<string>();
        foreach (string f in dir.GetFiles())
        {
            if (!f.EndsWith(".entities.json")) continue;
            string map = f[..^".entities.json".Length];
            // eine abgebrochene mitgereiste Karte (ohne Fertig-Marke, Fable A3) nicht anbieten
            if (map.StartsWith("map_netz_") && !FileAccess.FileExists($"{ordner}/{map}.fertig")) continue;
            if (Editor.MapOpen.IsDelivered(map)) continue;
            if (Maps.FindIndex(m => m.File == map) >= 0) continue;
            Maps.Add((map, EigenerTitel(map)));
            aufgenommen.Add(map);
        }
        GD.Print($"eigene karten: {aufgenommen.Count} aus {ordner} — {string.Join(", ", aufgenommen)}");
    }

    private static string EigenerTitel(string map)
        => map.StartsWith("map_netz_")
            ? $"Empfangene Karte ({map[9..17]})"
            : $"Eigene Karte: {map[4..]}";

    /// <summary>Eine Karte, die das Netz gebracht hat, auswählbar machen.</summary>
    private int KarteAufnehmen(string map)
    {
        int mi = Maps.FindIndex(m => m.File == map);
        if (mi >= 0) return mi;
        if (!FileAccess.FileExists(Core.Content.Path($"Maps/{map}.entities.json"))) return -1;
        Maps.Add((map, EigenerTitel(map)));
        return Maps.Count - 1;
    }

    /// <summary>bug-803: liegt ein sichtbarer Schirm ueber dem Startmenue (Unterschirm,
    /// Gefechtsaufbau, Karteneditor …)? Jeder sichtbare Control-Knoten, der im Baum NACH dem
    /// Startmenue haengt, wird darueber gezeichnet.</summary>
    private bool UeberDemStartmenue()
    {
        if (_start == null) return false;
        if (_setup is { Visible: true }) return true;
        bool danach = false;
        foreach (var k in GetChildren())
        {
            if (k == _start) { danach = true; continue; }
            if (danach && k is Control { Visible: true } c && !c.IsQueuedForDeletion()
                && c.Size.X > 200 && c.Size.Y > 200) return true;
        }
        return false;
    }
}
