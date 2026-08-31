#nullable enable
namespace AkteEuropaReborn.Core;

using System.Text.Json.Nodes;
using Godot;

/// <summary>
/// <b>Kartendaten-JSON OHNE Godot-Variants lesen.</b>
///
/// <para>⭐ 31.08.2026 — ABSTURZBEHEBUNG AN DER WURZEL, siehe
/// berichte/absturz-fable.md und berichte/sturm-fable.md. Das Kartenladen las
/// <c>map_*.json</c> und <c>map_*.entities.json</c> über Godots
/// <c>Json.Parse</c> und lief dann je Zelle/Objekt über
/// <c>Godot.Collections.Dictionary</c> — auf map_DM_4 sind das rund 81 000
/// Unterwörterbücher (40 000 tiles + 34 468 nebelboden + 5 055 objects +
/// 1 386 burnt) mit je 5–8 Zugriffen, und JEDER davon legt C#-seitig
/// <c>Variant</c>+<c>Disposer</c>+<c>WeakReference</c> an, die sich in Godots
/// <c>DisposablesTracker</c> ein- und über den Finalizer-Faden wieder
/// austragen. Dieses Rennen (Hauptfaden <c>TryAdd</c> gegen Finalizer
/// <c>TryRemove</c>) hat 7 von 10 kopflosen Prüfläufen getötet; die
/// <c>using</c>-Härtungen vom 12.08. und 31.08. haben es nur verschoben
/// (gemessen 3/10 bzw. 5/10 — der Sturm wandert zur nächsten ungehärteten
/// Schleife, Beleg: F-Dump in <c>LoadObjectLayer</c>).</para>
///
/// <para><b>Die Kur:</b> die zwei Kartendateien mit <c>System.Text.Json</c>
/// lesen. <c>JsonNode</c>/<c>JsonObject</c> sind reine .NET-Objekte ohne
/// Finalizer und ohne Tracker-Eintrag — es entsteht gar nichts mehr, was der
/// Tracker verwalten müsste. Nur das ÖFFNEN der Datei bleibt bei Godots
/// <c>FileAccess</c>, weil die Pfade <c>user://</c>-Pfade sind.</para>
///
/// <para>⚠ Godot-Semantik, die hier NACHGEBAUT ist, damit sich das Verhalten
/// nicht ändert: <c>Variant.AsInt32()</c> schneidet Fließkommazahlen ab
/// (20.5 → 20) und liest Zahlen aus Strings; <c>AsString()</c> macht aus der
/// Zahl 21 den Text »21«; <c>AsBool()</c> nimmt Zahl ≠ 0 als wahr. Ein
/// fehlender Schlüssel und JSON-<c>null</c> liefern den Vorgabewert.</para>
/// </summary>
public static class JsonMeta
{
    /// <summary>Eine JSON-Datei als Objekt lesen; bei jedem Fehler ein LEERES
    /// Objekt (dieselbe Rückfallform wie die alten <c>LoadMeta</c>-Leser:
    /// keine Ausnahme, die Karte lädt eben ohne Meta).</summary>
    public static JsonObject Lies(string pfad)
    {
        if (!FileAccess.FileExists(pfad)) return new JsonObject();
        using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
        if (f == null) return new JsonObject();
        return Parse(f.GetAsText());
    }

    /// <summary>Text als JSON-Objekt; leeres Objekt bei Fehler oder wenn die
    /// Wurzel kein Objekt ist.</summary>
    public static JsonObject Parse(string text)
    {
        // ⚠ Ein UTF-8-BOM am Textanfang liesse System.Text.Json den GANZEN
        // Text verwerfen (Godots Json.Parse war da tolerant) — die Karte
        // laedde dann still ohne Meta. Abschneiden kostet nichts.
        text = text.TrimStart('\uFEFF');
        try { return JsonNode.Parse(text) as JsonObject ?? new JsonObject(); }
        catch (System.Text.Json.JsonException) { return new JsonObject(); }
    }

    /// <summary>Knoten als int — Godots <c>AsInt32</c>-Semantik (Fließkomma
    /// abgeschnitten, bool 0/1, Zahl im String gelesen).</summary>
    public static int AsI(JsonNode? n, int def = 0)
    {
        if (n is not JsonValue v) return def;
        if (v.TryGetValue(out int i)) return i;
        if (v.TryGetValue(out long l)) return unchecked((int)l);
        if (v.TryGetValue(out double d)) return (int)d;
        if (v.TryGetValue(out bool b)) return b ? 1 : 0;
        if (v.TryGetValue(out string? s) && int.TryParse(s, out int si)) return si;
        return def;
    }

    /// <summary>Knoten als double.</summary>
    public static double AsD(JsonNode? n, double def = 0)
    {
        if (n is not JsonValue v) return def;
        if (v.TryGetValue(out double d)) return d;
        if (v.TryGetValue(out long l)) return l;
        if (v.TryGetValue(out bool b)) return b ? 1 : 0;
        return def;
    }

    /// <summary>Knoten als bool — Godots <c>AsBool</c>: Zahl ≠ 0 ist wahr.</summary>
    public static bool AsB(JsonNode? n)
    {
        if (n is not JsonValue v) return false;
        if (v.TryGetValue(out bool b)) return b;
        if (v.TryGetValue(out double d)) return d != 0;
        return false;
    }

    /// <summary>Knoten als Text — Zahlen werden zu ihrem Zahltext, wie bei
    /// <c>Variant.AsString()</c>.</summary>
    public static string AsS(JsonNode? n, string def = "")
        => n is JsonValue v ? v.ToString() : def;

    public static int GetI(JsonObject d, string k, int def = 0)
        => d.TryGetPropertyValue(k, out var n) && n != null ? AsI(n, def) : def;

    public static string GetS(JsonObject d, string k, string def = "")
        => d.TryGetPropertyValue(k, out var n) && n != null ? AsS(n, def) : def;

    public static bool GetB(JsonObject d, string k)
        => d.TryGetPropertyValue(k, out var n) && AsB(n);
}
