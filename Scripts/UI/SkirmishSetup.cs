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

    /// <summary>"Rohstoffe": 0 keine · 1 wenige · 2 normal · 3 viele — the
    /// original's own option and its own order (see
    /// <see cref="Import.ExeTables.ResourceLevels"/>). It fills the buildings'
    /// stores at the start of a SKIRMISH; the routine behind it has one caller
    /// and that caller is the game-start message, so a campaign mission keeps
    /// what its level file gives it. Default 2, which is what the numbers call
    /// normal.</summary>
    public static int Resources = 2;

    public const string MenuScene = "res://Scenes/Main/MainMenu.tscn";

    /// <summary>A save the main menu picked, applied by the game screen once
    /// the map is up. Empty means "start fresh". Cleared by whoever uses it, so
    /// a later restart does not silently reload the same game.</summary>
    public static string PendingSave = "";
    public const string GameScene = "res://Scenes/Gameplay/MapViewer.tscn";
}
