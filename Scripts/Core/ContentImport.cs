namespace AkteEuropaReborn.Core;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Getting the content onto the player's machine, the OpenRA way: the build we
/// hand out carries none of the 1997 game, and this is what fills
/// <c>user://data/</c> from what the player already owns.
///
/// This file handles the simple case: a folder that already holds DERIVED
/// content (Maps/, Units/, …), copied across as it stands — what the project's
/// own tooling produces.
///
/// The other two sources are elsewhere, because they are real work rather than
/// a copy: <see cref="ContentSources"/> finds the original discs and
/// installations, and <see cref="Import.ContentBuilder"/> derives the content
/// from them.
/// </summary>
public static class ContentImport
{
    /// <summary>Folders the game reads at runtime.</summary>
    private static readonly string[] Wanted = { "Maps", "Units", "UI", "Effects" };

    public enum SourceKind { Nothing, Derived, Original }

    public static SourceKind Classify(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return SourceKind.Nothing;
        dir = dir.TrimEnd('/', '\\');
        if (FileAccess.FileExists(dir + "/Maps/map_NET07.entities.json")) return SourceKind.Derived;
        if (Content.LooksLikeOriginal(dir)) return SourceKind.Original;
        // maybe they pointed at the parent of the derived tree
        if (DirAccess.DirExistsAbsolute(dir + "/Assets/Legacy/Maps")) return SourceKind.Derived;
        return SourceKind.Nothing;
    }

    /// <summary>Copy a derived content tree into user://data. Returns the number
    /// of files copied, or -1 with a reason.</summary>
    public static int ImportDerived(string dir, out string message)
    {
        dir = dir.TrimEnd('/', '\\');
        if (DirAccess.DirExistsAbsolute(dir + "/Assets/Legacy/Maps")) dir += "/Assets/Legacy";
        if (!DirAccess.DirExistsAbsolute(dir + "/Maps"))
        {
            message = "Kein Maps-Ordner gefunden.";
            return -1;
        }
        string target = ProjectSettings.GlobalizePath(Content.UserRoot).TrimEnd('/', '\\');
        DirAccess.MakeDirRecursiveAbsolute(target);
        int n = 0;
        foreach (string sub in Wanted)
        {
            if (!DirAccess.DirExistsAbsolute(dir + "/" + sub)) continue;
            n += CopyTree(dir + "/" + sub, target + "/" + sub);
        }
        message = n > 0
            ? $"{n} Dateien nach {target} kopiert."
            : "Nichts zu kopieren gefunden.";
        return n;
    }

    private static int CopyTree(string from, string to)
    {
        DirAccess.MakeDirRecursiveAbsolute(to);
        int n = 0;
        var da = DirAccess.Open(from);
        if (da == null) return 0;
        foreach (string f in da.GetFiles())
        {
            if (f.EndsWith(".import")) continue;      // editor bookkeeping
            if (DirAccess.CopyAbsolute(from + "/" + f, to + "/" + f) == Error.Ok) n++;
        }
        foreach (string d in da.GetDirectories())
            n += CopyTree(from + "/" + d, to + "/" + d);
        return n;
    }

    /// <summary>What the importer still cannot derive. Kept here so the screen
    /// can be specific; the list itself lives with the builder that knows it.
    /// </summary>
    public static List<string> OriginalTodo() => Import.ContentBuilder.Missing();
}
