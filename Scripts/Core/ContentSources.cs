namespace AkteEuropaReborn.Core;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;

/// <summary>
/// Where the original data can come from on this machine — the first-start
/// question, answered the way OpenRA answers it: install from the discs you
/// own, or fetch the freely available files.
///
/// Three kinds of source, and the screen names the one it found:
///
///  * <b>Disc</b> — an Akte Europa CD in a drive. Both discs carry <c>DATA\</c>
///    and <c>LEVELS\</c> loose, and CD1 additionally holds <c>DATA1.CAB</c>,
///    the InstallShield cabinet with GAME.EXE and the sprite banks. The two
///    discs complement each other: CD2 carries the campaign levels 16 to 33
///    and twenty tilesets CD1 does not have, so both are offered together.
///  * <b>Installation</b> — a folder with GAME.EXE next to DATA and LEVELS.
///  * <b>Derived</b> — a folder that already holds imported content.
///
/// The download is described but deliberately NOT filled in: see
/// <see cref="Download"/>.
/// </summary>
public static class ContentSources
{
    public enum Kind { Disc, Installation, Derived }

    public sealed class Source
    {
        public Kind Kind;
        public string Label = "";
        /// <summary>Folders that hold DATA/ and LEVELS/, in search order.</summary>
        public List<string> Roots = new();
        /// <summary>An InstallShield cabinet to take GAME.EXE and the sprite
        /// banks from, when they are not lying loose.</summary>
        public string? Cabinet;
        /// <summary>GAME.EXE, when it lies loose.</summary>
        public string? Exe;

        public bool Complete => Exe != null || Cabinet != null;

        public string Describe() => Kind switch
        {
            Kind.Disc => $"{Label} — {Roots.Count} Datentraeger" +
                         (Cabinet != null ? ", mit Programmdateien" : ", OHNE GAME.EXE (CD 1 fehlt)"),
            Kind.Installation => $"Installation erkannt: {Label}",
            _ => $"Fertiger Datenordner: {Label}",
        };
    }

    /// <summary>A folder counts as an original if it has the two data folders;
    /// GAME.EXE may be missing when it is a disc, because there it lives inside
    /// the cabinet.</summary>
    public static bool HasGameData(string dir)
    {
        dir = dir.TrimEnd('/', '\\');
        return Directory.Exists(dir + "/DATA") && Directory.Exists(dir + "/LEVELS");
    }

    public static string? ExeIn(string dir)
    {
        foreach (string n in new[] { "GAME.EXE", "game.exe" })
            if (File.Exists(dir.TrimEnd('/', '\\') + "/" + n))
                return dir.TrimEnd('/', '\\') + "/" + n;
        return null;
    }

    public static string? CabinetIn(string dir)
    {
        foreach (string n in new[] { "DATA1.CAB", "data1.cab" })
        {
            string p = dir.TrimEnd('/', '\\') + "/" + n;
            if (File.Exists(p) && Import.IscFile.LooksLikeCabinet(p)) return p;
        }
        return null;
    }

    /// <summary>Every cabinet lying on a drive right now — the self-test uses
    /// this so it can check against a real disc when one is inserted.</summary>
    public static List<string> Cabinets()
    {
        var list = new List<string>();
        foreach (string root in DriveRoots())
        {
            string? c = CabinetIn(root);
            if (c != null) list.Add(c);
        }
        return list;
    }

    /// <summary>The discs currently inserted, gathered into ONE source: the
    /// campaign needs both, and the importer searches them in order.</summary>
    public static Source? Discs()
    {
        var roots = new List<string>();
        string? cab = null;
        var labels = new List<string>();
        foreach (string root in DriveRoots())
        {
            if (!HasGameData(root)) continue;
            roots.Add(root);
            labels.Add(LabelOf(root));
            cab ??= CabinetIn(root);
        }
        if (roots.Count == 0) return null;
        return new Source
        {
            Kind = Kind.Disc,
            Label = string.Join(" + ", labels),
            Roots = roots,
            Cabinet = cab,
        };
    }

    /// <summary>What a folder the player picked actually is.</summary>
    public static Source? FromFolder(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        dir = dir.TrimEnd('/', '\\');
        if (File.Exists(dir + "/Maps/map_NET07.entities.json") ||
            Directory.Exists(dir + "/Assets/Legacy/Maps"))
            return new Source { Kind = Kind.Derived, Label = dir, Roots = { dir } };
        if (!HasGameData(dir) && ExeIn(dir) == null) return null;
        return new Source
        {
            Kind = Kind.Installation,
            Label = dir,
            Roots = { dir },
            Exe = ExeIn(dir),
            Cabinet = CabinetIn(dir),
        };
    }

    private static IEnumerable<string> DriveRoots()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception) { yield break; }
        foreach (var d in drives)
        {
            bool ready;
            try { ready = d.IsReady; } catch (Exception) { continue; }
            if (!ready) continue;
            if (d.DriveType is not (DriveType.CDRom or DriveType.Removable)) continue;
            yield return d.RootDirectory.FullName.TrimEnd('/', '\\');
        }
    }

    private static string LabelOf(string root)
    {
        try
        {
            var d = new DriveInfo(root);
            string v = d.VolumeLabel;
            return string.IsNullOrWhiteSpace(v) ? root : $"{v} ({root})";
        }
        catch (Exception) { return root; }
    }

    // ---- the freeware download ---------------------------------------------

    /// <summary>
    /// The other half of the OpenRA model: fetch the files instead of reading a
    /// disc. The machinery is here and works — fetch, show progress, check the
    /// SHA-256, unpack, then import exactly as a folder source.
    ///
    /// <b>The source itself is deliberately empty.</b> Inventing a download
    /// address for a 1997 commercial game would be guessing, and guessing about
    /// where the player's copy comes from is not ours to do. Fill
    /// <see cref="Url"/> and <see cref="Sha256"/> in with a source you know to
    /// be legitimate and the button enables itself; until then it says so.
    /// </summary>
    public static class Download
    {
        public const string Url = "";
        public const string Sha256 = "";
        public const long ExpectedBytes = 0;

        public static bool Configured => Url.Length > 0 && Sha256.Length == 64;

        public static string Explain() => Configured
            ? $"Lädt {ExpectedBytes / (1024 * 1024)} MB und prueft die Pruefsumme."
            : "Noch keine Bezugsquelle hinterlegt — siehe ContentSources.Download.";
    }
}
