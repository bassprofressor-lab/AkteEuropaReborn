namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// The little picture of a map next to the skirmish settings.
///
/// The baked map PNGs are up to 10160 x 5285 pixels, so they are not something
/// a menu can load on every click. Each one is therefore shrunk once and the
/// result kept as a thumbnail under `Maps/thumbs/`; from then on the menu only
/// ever touches a file of a few kilobytes. The first look at a map costs a
/// second, every look after that costs nothing.
///
/// OURS: the original had no map preview in its network setup — this is a
/// convenience of the remake, not a recovered screen.
/// </summary>
public static class MapPreview
{
    /// <summary>Longest side of a thumbnail, in pixels.</summary>
    private const int MaxSide = 384;

    private static readonly Dictionary<string, Texture2D?> _cache = new();

    /// <summary>The thumbnail for a baked map, or null if the map has no
    /// picture (not imported yet, or the import failed).</summary>
    public static Texture2D? Of(string name)
    {
        if (_cache.TryGetValue(name, out var hit)) return hit;
        var tex = Build(name);
        _cache[name] = tex;
        return tex;
    }

    private static Texture2D? Build(string name)
    {
        string thumb = Core.Content.UserRoot + "Maps/thumbs/" + name + ".png";
        if (FileAccess.FileExists(thumb))
        {
            var cached = Image.LoadFromFile(thumb);
            if (cached != null) return ImageTexture.CreateFromImage(cached);
        }

        string full = Core.Content.Path("Maps/" + name + ".png");
        if (!FileAccess.FileExists(full)) return null;
        var img = Image.LoadFromFile(full);
        if (img == null || img.GetWidth() == 0) return null;

        float s = (float)MaxSide / Mathf.Max(img.GetWidth(), img.GetHeight());
        if (s < 1f)
            img.Resize(Mathf.Max(1, (int)(img.GetWidth() * s)),
                       Mathf.Max(1, (int)(img.GetHeight() * s)),
                       Image.Interpolation.Bilinear);

        DirAccess.MakeDirRecursiveAbsolute(Core.Content.UserRoot + "Maps/thumbs");
        img.SavePng(thumb);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>One line about the map for under the picture: the name the
    /// original gave it and its size in cells, both straight out of the baked
    /// metadata.</summary>
    public static string Caption(string name)
    {
        string p = Core.Content.Path("Maps/" + name + ".json");
        if (!FileAccess.FileExists(p)) return "nicht importiert";
        using var f = FileAccess.Open(p, FileAccess.ModeFlags.Read);
        if (f == null) return "";
        var json = Json.ParseString(f.GetAsText());
        if (json.VariantType != Variant.Type.Dictionary) return "";
        var d = json.AsGodotDictionary<string, Variant>();
        string mission = d.TryGetValue("mission", out var m) ? m.AsString().Trim() : "";
        int w = d.TryGetValue("width", out var wv) ? wv.AsInt32() : 0;
        int h = d.TryGetValue("height", out var hv) ? hv.AsInt32() : 0;
        string size = w > 0 ? $"{w} x {h} Felder" : "";
        string head = mission.Length > 0 ? $"„{mission}“  —  {size}" : size;
        string slots = Slots(name);
        return slots.Length > 0 ? head + "\n" + slots : head;
    }

    /// <summary>What the start slots of this map are worth, said before the
    /// player starts rather than found out afterwards.
    ///
    /// ⚠ CORRECTED (0.4.0): the line used to count BUILDINGS, and a building is
    /// not what lets a player build. A factory is — the Waffen-, Fahrwerk- or
    /// Spezial-Fabrik, building types 2, 3 and 4. Measured across all 44 maps:
    /// <b>none of the eight NET maps gives any slot a factory</b>, while the
    /// campaign levels hold between 1 and 23. So NET07's "three built slots"
    /// never meant a build-up was possible there, and saying so cost the player
    /// a game in which he could do nothing at all.
    ///
    /// The counts come from the same entities.json the game plays from, so this
    /// line cannot drift away from what actually happens.</summary>
    public static string Slots(string name)
    {
        string p = Core.Content.Path("Maps/" + name + ".entities.json");
        if (!FileAccess.FileExists(p)) return "";
        using var f = FileAccess.Open(p, FileAccess.ModeFlags.Read);
        if (f == null) return "";

        // System.Text.Json, not Godot's: an entities.json holds hundreds of
        // buildings, and reading them through Variants left that many
        // finalizable objects behind. A headless run that quits straight after
        // then died with an access violation in godotsharp_variant_destroy,
        // long after the work was done. Nothing here needs a Variant.
        var per = new int[8];      // buildings per slot
        var fab = new int[8];      // of those, factories (types 2/3/4)
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(f.GetAsText());
            if (!doc.RootElement.TryGetProperty("buildings", out var arr) ||
                arr.ValueKind != System.Text.Json.JsonValueKind.Array) return "";
            // the file stores a building's owner the way the game state does;
            // only slots 0..7 are counted, anything else (11 = neutral, 255 =
            // none) is left alone rather than guessed at
            foreach (var b in arr.EnumerateArray())
            {
                if (!b.TryGetProperty("owner", out var ov) ||
                    !ov.TryGetInt32(out int o) || o is < 0 or >= 8) continue;
                per[o]++;
                if (b.TryGetProperty("type", out var tv) && tv.TryGetInt32(out int t) &&
                    t is 2 or 3 or 4) fab[o]++;
            }
        }
        catch (System.Exception e) { GD.PrintErr("Startplaetze: " + e.Message); return ""; }

        var makers = new List<int>();
        for (int i = 0; i < 8; i++) if (fab[i] > 0) makers.Add(i);
        var built = new List<int>();
        for (int i = 0; i < 8; i++) if (per[i] > 0) built.Add(i);

        if (makers.Count == 0)
            return built.Count == 0
                ? "Kein Startplatz hat ein Gebaeude — wird mit den Truppen der Karte gespielt"
                : $"{built.Count} bebaute Startplaetze, aber KEINE Fabrik — " +
                  "hier wird nicht gebaut, die Karte wird mit ihren Truppen gespielt";

        int lo = int.MaxValue, hi = 0;
        foreach (int i in makers) { lo = Mathf.Min(lo, fab[i]); hi = Mathf.Max(hi, fab[i]); }
        string counts = string.Join("/", makers.ConvertAll(i => fab[i].ToString()));
        float spread = (float)hi / lo;
        return $"{makers.Count} Startplaetze mit Fabrik ({counts}) — " +
               $"je Platz bleiben {Rendering.MapEntityLayer.StarterTroop} Einheiten stehen" +
               (makers.Count > 1 && spread >= 2f ? $"; unausgeglichen, {spread:0.0}:1" : "");
    }
}
