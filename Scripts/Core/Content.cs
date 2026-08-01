namespace AkteEuropaReborn.Core;

using Godot;

/// <summary>
/// Where the game's content comes from.
///
/// The remake follows the OpenRA / devilutionX model: the build we hand out
/// contains only our own engine, and the actual content — terrain, sprites,
/// maps, tables — is derived on the player's own machine from the player's own
/// copy of Akte Europa. Nothing of the 1997 game is redistributed.
///
/// At runtime everything therefore goes through <see cref="Path"/>, which
/// prefers the imported content under <c>user://data/</c> and only falls back
/// to <c>res://Assets/Legacy/</c> — the development tree, which is not part of
/// an export.
/// </summary>
public static class Content
{
    public const string UserRoot = "user://data/";
    public const string DevRoot = "res://Assets/Legacy/";

    /// <summary>A file that only exists once the content has been imported.</summary>
    private const string Canary = "Maps/map_NET07.entities.json";

    /// <summary>Full path for a content-relative name such as
    /// "Maps/ships.json" or "Units/hull/150/f0.png".</summary>
    public static string Path(string rel)
    {
        string user = UserRoot + rel;
        if (FileAccess.FileExists(user)) return user;
        return DevRoot + rel;
    }

    /// <summary>True once the content is available from either root.</summary>
    public static bool Ready =>
        FileAccess.FileExists(UserRoot + Canary) || FileAccess.FileExists(DevRoot + Canary);

    /// <summary>Where the imported content lives, absolute, for messages.</summary>
    public static string UserDir => ProjectSettings.GlobalizePath(UserRoot);

    /// <summary>The menu scene, so the import screen can reload itself.</summary>
    public const string MenuSceneSafe = "res://Scenes/Main/MainMenu.tscn";

    /// <summary>Does this look like an Akte Europa installation?</summary>
    public static bool LooksLikeOriginal(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        foreach (string probe in new[] { "GAME.EXE", "game.exe" })
            if (FileAccess.FileExists(dir.TrimEnd('/', '\\') + "/" + probe)) return true;
        return false;
    }
}
