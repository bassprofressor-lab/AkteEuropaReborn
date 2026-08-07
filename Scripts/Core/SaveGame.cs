namespace AkteEuropaReborn.Core;

using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// Saving and loading a game in progress.
///
/// <para><b>Why this is NOT the original's .DM format, and the number that
/// settles it.</b> A save of the 1997 game is the same container as a level —
/// header plus sections — but where a `.CWM` stops after 38 sections, a `.DM`
/// carries <b>all 131</b> (see <see cref="Import.CwmFile"/>). We read that
/// format and check it section by section, but we only <b>understand</b>
/// <b>21</b> of those sections: 2, 3, 4, 5, 6, 16, 19, 27, 28, 33, 34, 44, 47,
/// 53, 69, 73, 96, 119, 120, 121, 122. Writing a `.DM` would mean inventing the
/// other 110 — the file would neither load in the original nor carry anything
/// we did not already know. So this format is <b>OURS</b>, and it says so.</para>
///
/// <para><b>What it stores.</b> A save names the map it started from and then
/// the whole live state on top of it: every entity with the fields that change
/// while playing, the money, the fog. The map itself — terrain, the baked
/// picture, the tileset patterns — is not copied; it is reloaded from the
/// content the player's own discs produced, exactly as a fresh start does.</para>
///
/// <para>Text, not packed bytes: a save that can be read in an editor is worth
/// more during development than a small one, and these files run to a few
/// hundred kilobytes at most.</para>
/// </summary>
public static class SaveGame
{
    /// <summary>Bumped whenever the fields below change meaning. A save from an
    /// older build is refused with a word rather than half-loaded.</summary>
    public const int Format = 1;

    public static string Folder => "user://saves";

    public static string PathOf(string name) => $"{Folder}/{name}.aesave";

    /// <summary>The saves that are there, newest first.</summary>
    public static List<(string Name, string Label, ulong When)> List()
    {
        var l = new List<(string, string, ulong)>();
        using var d = DirAccess.Open(Folder);
        if (d == null) return l;
        foreach (string f in d.GetFiles())
        {
            if (!f.EndsWith(".aesave")) continue;
            string name = f[..^7];
            string label = name;
            ulong when = 0;
            // the label lives in the file, so a renamed file still says what it is
            using var fh = FileAccess.Open($"{Folder}/{f}", FileAccess.ModeFlags.Read);
            if (fh != null)
            {
                var json = new Json();
                if (json.Parse(fh.GetAsText()) == Error.Ok &&
                    json.Data.VariantType == Variant.Type.Dictionary)
                {
                    var root = json.Data.AsGodotDictionary<string, Variant>();
                    if (root.TryGetValue("label", out var lv)) label = lv.AsString();
                    if (root.TryGetValue("when", out var wv)) when = (ulong)wv.AsInt64();
                }
            }
            l.Add((name, label, when));
        }
        l.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        return l;
    }

    public static void EnsureFolder()
    {
        if (!DirAccess.DirExistsAbsolute(Folder)) DirAccess.MakeDirRecursiveAbsolute(Folder);
    }

    /// <summary>Writes the text a caller has assembled. Returns false and says
    /// why rather than failing quietly.</summary>
    public static bool Write(string name, string json, out string error)
    {
        error = "";
        EnsureFolder();
        using var f = FileAccess.Open(PathOf(name), FileAccess.ModeFlags.Write);
        if (f == null) { error = $"{PathOf(name)}: {FileAccess.GetOpenError()}"; return false; }
        f.StoreString(json);
        return true;
    }

    public static GDict? Read(string name, out string error)
    {
        error = "";
        string p = PathOf(name);
        if (!FileAccess.FileExists(p)) { error = $"{p} fehlt"; return null; }
        using var f = FileAccess.Open(p, FileAccess.ModeFlags.Read);
        if (f == null) { error = $"{p}: {FileAccess.GetOpenError()}"; return null; }
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary)
        { error = $"{p}: unlesbar"; return null; }
        var root = json.Data.AsGodotDictionary<string, Variant>();
        int fmt = root.TryGetValue("format", out var fv) ? fv.AsInt32() : 0;
        if (fmt != Format)
        { error = $"Spielstand-Format {fmt}, erwartet {Format}"; return null; }
        return root;
    }

    /// <summary>A name that sorts by time and cannot collide.</summary>
    public static string NewName() => $"save_{Time.GetUnixTimeFromSystem():0}";

    /// <summary>Small helpers so the writer below reads like the data it
    /// produces instead of like string handling.</summary>
    public sealed class Writer
    {
        private readonly StringBuilder _sb = new(1 << 20);
        private bool _first = true;

        public Writer Open() { _sb.Append('{'); _first = true; return this; }
        public Writer Close() { _sb.Append('}'); _first = false; return this; }

        private void Comma() { if (!_first) _sb.Append(','); _first = false; }

        public Writer Num(string k, long v) { Comma(); _sb.Append('"').Append(k).Append("\":").Append(v); return this; }
        public Writer Num(string k, float v) { Comma(); _sb.Append('"').Append(k).Append("\":").Append(v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); return this; }
        public Writer Bool(string k, bool v) { Comma(); _sb.Append('"').Append(k).Append("\":").Append(v ? "true" : "false"); return this; }
        public Writer Str(string k, string v) { Comma(); _sb.Append('"').Append(k).Append("\":\"").Append(Esc(v)).Append('"'); return this; }
        public Writer Raw(string k, string v) { Comma(); _sb.Append('"').Append(k).Append("\":").Append(v); return this; }

        public Writer ArrayStart(string k) { Comma(); _sb.Append('"').Append(k).Append("\":["); _first = true; return this; }
        public Writer ArrayEnd() { _sb.Append(']'); _first = false; return this; }
        public Writer ItemStart() { Comma(); _sb.Append('{'); _first = true; return this; }
        public Writer ItemEnd() { _sb.Append('}'); _first = false; return this; }

        public override string ToString() => _sb.ToString();

        private static string Esc(string s)
        {
            var b = new StringBuilder(s.Length + 8);
            foreach (char c in s)
                b.Append(c switch
                {
                    '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                    _ => c < ' ' ? $"\\u{(int)c:x4}" : c.ToString(),
                });
            return b.ToString();
        }
    }
}
