namespace AkteEuropaReborn.UI;

using AkteEuropaReborn.Rendering;

/// <summary>
/// What the main menu hands to the game scene. Static because the two scenes
/// never exist at the same time — the menu frees itself before the map loads.
/// </summary>
public static class SkirmishSetup
{
    /// <summary>True while a game started from the menu is running; the game
    /// scene then goes back to the menu on Esc instead of quitting.</summary>
    public static bool Active;

    public static string Map = "map_NET07";
    public static int Human;                       // which player the human is
    public static int AiCount = 3;                 // how many opponents
    public static MapEntityLayer.AiLevel Level = MapEntityLayer.AiLevel.Normal;

    /// <summary>The campaign mission being played, or 0 for a skirmish. Set so
    /// the game scene knows to record the mission as finished when it is won —
    /// a skirmish has nothing to record.</summary>
    public static int CampaignMission;

    public const string MenuScene = "res://Scenes/Main/MainMenu.tscn";
    public const string GameScene = "res://Scenes/Gameplay/MapViewer.tscn";
}
