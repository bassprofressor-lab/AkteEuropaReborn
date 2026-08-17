namespace AkteEuropaReborn.Import;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Die Leseseite der Tafel »neue technologien« — was
/// <see cref="MissionTechExporter"/> geschrieben hat, zur Laufzeit.
///
/// <para>Gebaut wie <see cref="AkteEuropaReborn.UI.PortraitBank"/>: einmal
/// lesen, Bilder zwischenspeichern, und wenn nichts da ist, sagen WARUM statt
/// still leer zu bleiben. Der Kasten im Briefing ist bei drei der 33 Missionen
/// im Original leer — ein stilles Nichts wäre von einem Fehler nicht zu
/// unterscheiden.</para>
///
/// <para>Die Bilder haben denselben Umweg nötig wie die der Bildbank: vom
/// Einlesen geschriebene PNG gehen an Godots Importschritt vorbei, der
/// Ressourcenlader sieht sie also nicht.</para>
/// </summary>
public static class MissionTechTable
{
    /// <summary>Ein Eintrag, wie ihn der Kasten zeigt.</summary>
    public sealed class Entry
    {
        public string Name = "";
        public int Page, Picture;
    }

    /// <summary>Warum nichts da ist, wenn nichts da ist.</summary>
    public static string Trouble { get; private set; } = "";

    /// <summary>Wieviele Missionen die Datei führt (0…33).</summary>
    public static int Missions { get; private set; }

    /// <summary>Woher die Tafel stammt — die virtuelle Adresse, die der
    /// Exporter nach der Form gefunden hat. Für Prüfstände.</summary>
    public static string TableVa { get; private set; } = "";

    private static bool _read;
    private static readonly Dictionary<int, List<Entry>> _byMission = new();
    private static readonly Dictionary<int, Texture2D?> _pics = new();

    /// <summary>Nach einem Wechsel des Inhalts (der Exporter lief gerade)
    /// nochmal lesen.</summary>
    public static void Forget()
    {
        _read = false;
        _byMission.Clear();
        _pics.Clear();
        Missions = 0;
        Trouble = "";
        TableVa = "";
    }

    private static void Load()
    {
        if (_read) return;
        _read = true;
        string path = Core.Content.Path("Maps/" + MissionTechExporter.JsonName);
        if (!FileAccess.FileExists(path))
        {
            Trouble = $"Maps/{MissionTechExporter.JsonName} fehlt ({path}) — " +
                      "»--tech-export=<Installation>« schreibt sie";
            return;
        }
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) { Trouble = MissionTechExporter.JsonName + " nicht lesbar"; return; }
        using var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary)
        {
            Trouble = MissionTechExporter.JsonName + " ist kein JSON-Objekt";
            return;
        }
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (root.TryGetValue("_table", out var tv) &&
            tv.VariantType == Variant.Type.Dictionary)
        {
            var t = tv.AsGodotDictionary<string, Variant>();
            if (t.TryGetValue("va", out var vv)) TableVa = vv.AsString();
        }
        if (!root.TryGetValue("missions", out var mv) ||
            mv.VariantType != Variant.Type.Dictionary)
        {
            Trouble = MissionTechExporter.JsonName + " nennt kein »missions«";
            return;
        }
        foreach (var kv in mv.AsGodotDictionary<string, Variant>())
        {
            if (kv.Value.VariantType != Variant.Type.Array) continue;
            var list = new List<Entry>();
            foreach (var e in kv.Value.AsGodotArray())
            {
                if (e.VariantType != Variant.Type.Dictionary) continue;
                var d = e.AsGodotDictionary<string, Variant>();
                list.Add(new Entry
                {
                    Name = d.TryGetValue("name", out var n) ? n.AsString() : "",
                    Page = d.TryGetValue("page", out var p) ? p.AsInt32() : 0,
                    Picture = d.TryGetValue("picture", out var b) ? b.AsInt32() : 0,
                });
            }
            _byMission[kv.Key.ToInt()] = list;
        }
        Missions = _byMission.Count;
        if (Missions == 0) Trouble = MissionTechExporter.JsonName + " führt keine Mission";
    }

    /// <summary>Ist die Zuordnung da? Danach sagt <see cref="Trouble"/>, warum
    /// nicht.</summary>
    public static bool Ready { get { Load(); return Missions > 0; } }

    /// <summary>Was Mission <paramref name="mission"/> ankündigt. Eine LEERE
    /// Liste heisst »diese Mission kündigt nichts an« und ist eine Antwort;
    /// <c>null</c> heisst »die Zuordnung ist nicht da«.</summary>
    public static List<Entry>? Of(int mission)
    {
        Load();
        return _byMission.TryGetValue(mission, out var l) ? l : null;
    }

    /// <summary>Alle Missionsnummern, die die Datei führt — für einen
    /// Prüfstand, damit er sie nicht selbst wissen muss.</summary>
    public static IEnumerable<int> KnownMissions()
    {
        Load();
        return _byMission.Keys;
    }

    /// <summary>Ein Bild aus ENCYCLOG.PIC, 60×60; null, wenn es nicht da ist.
    /// </summary>
    public static Texture2D? Picture(int n)
    {
        if (n <= 0) return null;
        if (_pics.TryGetValue(n, out var have)) return have;
        string path = Core.Content.Path($"{MissionTechExporter.PicDir}/p{n:00}.png");
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        if (tex == null && FileAccess.FileExists(path))
        {
            using var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        _pics[n] = tex;
        return tex;
    }

    /// <summary>Eine Zeile für einen Prüflauf, der nicht auf den Schirm sehen
    /// kann.</summary>
    public static string WatchLine()
    {
        Load();
        if (Missions == 0) return "mission-tech: nichts — " + Trouble;
        int filled = 0, entries = 0, withPic = 0;
        foreach (var kv in _byMission)
        {
            if (kv.Value.Count > 0) filled++;
            entries += kv.Value.Count;
            foreach (var e in kv.Value) if (e.Picture > 0) withPic++;
        }
        return $"mission-tech: {entries} Eintraege in {filled} von {Missions} Missionen, " +
               $"{withPic} mit Bild, Tafel {TableVa}";
    }
}
