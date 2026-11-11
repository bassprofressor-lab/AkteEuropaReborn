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
        var dir = DirAccess.Open(Core.Content.Path("Maps"));
        if (dir == null) return;
        foreach (string f in dir.GetFiles())
        {
            if (!f.EndsWith(".entities.json")) continue;
            string map = f[..^".entities.json".Length];
            if (Editor.MapOpen.IsDelivered(map)) continue;
            if (Maps.FindIndex(m => m.File == map) >= 0) continue;
            Maps.Add((map, EigenerTitel(map)));
        }
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
}
