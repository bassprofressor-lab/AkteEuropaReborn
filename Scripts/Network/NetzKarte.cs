#nullable enable
namespace AkteEuropaReborn.Network;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Godot;

/// <summary>
/// ⭐ 11.11.2026 — <b>EIGENE KARTEN IM NETZSPIEL</b>: die Karte reist mit.
///
/// <para>Spielt der Gastgeber eine Karte, die nicht mit dem Spiel kommt (aus dem
/// Karteneditor), schickt er VOR dem Sitzungspaket die beiden Dateien, die die
/// Simulation liest — <c>map_X.json</c> (Gelände) und <c>map_X.entities.json</c>
/// (Einheiten, Gebäude) —, gepackt und in Stücken (Paket 12). Der Mitspieler legt sie
/// unter <c>map_netz_&lt;Prüfsumme&gt;</c> ab (nie unter dem Namen des Gastgebers: das
/// überschriebe womöglich eine gleichnamige EIGENE Karte), backt das Kartenbild aus
/// seinem eigenen Kachelsatz und legt danach die empfangenen Dateien byte-gleich
/// zurück — die Simulation rechnet so auf allen Rechnern aus denselben Bytes.</para>
///
/// <para>⚠ UNSERE ZUTAT. Das Original kennt nur die mitgelieferten Netzkarten
/// (<c>net01…net08.cwm</c> + <c>levels\</c> der CD, AV.14) und schickt nur den
/// Kartennamen (Befehl 982).</para>
/// </summary>
public static class NetzKarte
{
    public const int Stueck = 3000;

    /// <summary>Prüfschalter <c>--netzkarte-immer</c>: die gleichnamige Karte hier NICHT nehmen,
    /// sondern die des Gastgebers empfangen und backen (zwei Prozesse auf einer Maschine).</summary>
    public static bool ImmerEmpfangen => Array.IndexOf(Core.CommandLine.Args, "--netzkarte-immer") >= 0;

    private static string MapsDir => ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/Maps";

    public static bool IstMitgeliefert(string map) => Editor.MapOpen.IsDelivered(map);

    public static string LokalerName(ulong hash) => $"map_netz_{hash:X16}";

    /// <summary>Liegt die Karte (samt gebackenem Bild) hier schon?</summary>
    public static bool Vorhanden(string map)
        => File.Exists($"{MapsDir}/{map}.json") && File.Exists($"{MapsDir}/{map}.entities.json")
           && File.Exists($"{MapsDir}/{map}.png");

    /// <summary>FNV-1a 64 über Gelände- und Einheitendatei, so wie sie auf der Platte stehen.</summary>
    public static ulong Pruefsumme(string map)
    {
        ulong h = 14695981039346656037UL;
        foreach (string ext in new[] { ".json", ".entities.json" })
        {
            string p = $"{MapsDir}/{map}{ext}";
            if (!File.Exists(p)) return 0;
            foreach (byte b in File.ReadAllBytes(p)) { h ^= b; h *= 1099511628211UL; }
            h ^= 0xFF; h *= 1099511628211UL;
        }
        return h == 0 ? 1 : h;
    }

    /// <summary>Die Pakete einer Karte: [12][hash u64][nr u16][von u16][len u16][Bytes].</summary>
    public static List<byte[]> Pakete(string map, ulong hash)
    {
        byte[] json = File.ReadAllBytes($"{MapsDir}/{map}.json");
        byte[] ent = File.ReadAllBytes($"{MapsDir}/{map}.entities.json");
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var bw = new BinaryWriter(gz))
        {
            bw.Write(json.Length); bw.Write(json);
            bw.Write(ent.Length); bw.Write(ent);
        }
        byte[] all = ms.ToArray();
        int n = (all.Length + Stueck - 1) / Stueck;
        var res = new List<byte[]>(n);
        for (int i = 0; i < n; i++)
        {
            int len = Math.Min(Stueck, all.Length - i * Stueck);
            var w = new NetWriter(NetPacket.Karte).U64(hash).U16((ushort)i).U16((ushort)n).U16((ushort)len);
            var head = w.Done();
            var pk = new byte[head.Length + len];
            head.CopyTo(pk, 0);
            Buffer.BlockCopy(all, i * Stueck, pk, head.Length, len);
            res.Add(pk);
        }
        GD.Print($"netzkarte: {map} gepackt {json.Length + ent.Length} -> {all.Length} B in {n} Paketen, Prüfsumme {hash:X16}");
        return res;
    }

    // ---- Empfang -----------------------------------------------------------------

    private static readonly Dictionary<ulong, byte[]?[]> _teile = new();
    private static readonly HashSet<ulong> _fertig = new();
    public static string Fehler { get; private set; } = "";

    /// <summary>Wie weit der Empfang einer Karte ist (für die Statuszeile).</summary>
    public static (int Da, int Von) Fortschritt(ulong hash)
    {
        if (_fertig.Contains(hash)) return (1, 1);
        if (!_teile.TryGetValue(hash, out var a)) return (0, 0);
        int da = 0; foreach (var x in a) if (x != null) da++;
        return (da, a.Length);
    }

    public static bool Fertig(ulong hash) => _fertig.Contains(hash) || Vorhanden(LokalerName(hash));

    /// <summary>Ein Kartenpaket aufnehmen (Leser steht hinter der Paketart).</summary>
    public static void Nimm(NetReader r, byte[] roh, int kopf)
    {
        ulong hash = r.U64();
        int nr = r.U16(), von = r.U16(), len = r.U16();
        if (r.Bad || von == 0 || nr >= von) return;
        if (_fertig.Contains(hash) || Vorhanden(LokalerName(hash))) { _fertig.Add(hash); return; }
        if (!_teile.TryGetValue(hash, out var a) || a.Length != von) { a = new byte[]?[von]; _teile[hash] = a; }
        int start = roh.Length - len;
        if (start < 0) return;
        a[nr] = roh[start..];
        foreach (var x in a) if (x == null) return;

        // komplett
        _teile.Remove(hash);
        try { Ablegen(hash, a!); _fertig.Add(hash); }
        catch (Exception e) { Fehler = "Karte konnte nicht abgelegt werden: " + e.Message; GD.PrintErr("netzkarte: " + Fehler); }
    }

    private static void Ablegen(ulong hash, byte[]?[] teile)
    {
        using var ms = new MemoryStream();
        foreach (var t in teile) ms.Write(t!);
        ms.Position = 0;
        byte[] json, ent;
        using (var gz = new GZipStream(ms, CompressionMode.Decompress))
        using (var br = new BinaryReader(gz))
        {
            json = br.ReadBytes(br.ReadInt32());
            ent = br.ReadBytes(br.ReadInt32());
        }
        string name = LokalerName(hash);
        Directory.CreateDirectory(MapsDir);
        File.WriteAllBytes($"{MapsDir}/{name}.json", json);
        File.WriteAllBytes($"{MapsDir}/{name}.entities.json", ent);

        // Bild backen: öffnen wie der Editor (oeffne=), mit dem EIGENEN Kachelsatz schreiben …
        var m = Editor.MapOpen.Load(name, s => GD.Print("netzkarte: " + s))
                ?? throw new InvalidDataException("MapOpen.Load lieferte nichts");
        var files = Editor.MapGenerator.FindTileset(m.Tileset, null)
                    ?? throw new FileNotFoundException($"Kachelsatz {m.Tileset:00} fehlt hier");
        var cwp = Import.CwpFile.Load(files.Cwp);
        var pal = Import.PalFile.Load(files.Pal);
        Editor.MapGenerator.Write(m, cwp, pal, name, s => GD.Print("netzkarte: " + s));
        // … und die Simulationsdateien wieder byte-gleich zurück: gerechnet wird aus
        // genau den Bytes des Gastgebers, nicht aus einem Rundlauf (MapOpen kennt nicht jeden Block).
        //
        // ⭐ 12.11.2026 — A4 (Fable-Pruefung): DIE BILDTEILE DER .json GEHOEREN ZUM
        // EIGENEN objects.png. objects[].bild, burnt und nebelboden[].slot zeigen auf
        // Streifenkacheln des objects.png, das DIESER Backlauf gebaut hat — die Nummern
        // des Gastgebers zeigten hier auf andere Kacheln (Wasser und Palmen im Gebuesch).
        // Sie liest nur der Zeichner (Rendering/MapObjects.cs), nicht die Simulation.
        // Also: Gastgeber-.json, aber diese drei Teile aus dem eigenen Backlauf.
        // Gegenschalter --netzkarte-bild-alt (alles vom Gastgeber, wie 0.7.0-Vorab).
        string eigen = File.ReadAllText($"{MapsDir}/{name}.json");
        string zusammen = BildTeileTauschen(json, eigen, out string notiz);
        Import.SicherSchreiben.Text($"{MapsDir}/{name}.json", zusammen);
        Import.SicherSchreiben.Bytes($"{MapsDir}/{name}.entities.json", ent);
        GD.Print($"netzkarte: {name} empfangen und gebacken — {notiz}");
    }

    private static readonly string[] BildTeile = { "objects", "burnt", "nebelboden" };

    /// <summary>A4: Gastgeber-.json mit den Bildteilen des eigenen Backlaufs. Die
    /// uebrigen Teile bleiben Token fuer Token die des Gastgebers.</summary>
    private static string BildTeileTauschen(byte[] gastJson, string eigenJson, out string notiz)
    {
        string gast = new System.Text.UTF8Encoding(false).GetString(gastJson);
        if (Array.IndexOf(Core.CommandLine.Args, "--netzkarte-bild-alt") >= 0)
        { notiz = "Bildteile vom Gastgeber (--netzkarte-bild-alt)"; return gast; }
        var g = System.Text.Json.Nodes.JsonNode.Parse(gast)!.AsObject();
        var e = System.Text.Json.Nodes.JsonNode.Parse(eigenJson)!.AsObject();
        var teile = new List<string>();
        foreach (string k in BildTeile)
        {
            var neu = e[k]?.DeepClone();
            if (neu == null) { g.Remove(k); continue; }
            g[k] = neu;
            teile.Add($"{k} {(neu is System.Text.Json.Nodes.JsonArray a ? a.Count : 1)}");
        }
        notiz = "Bildteile aus eigenem Backlauf (" + string.Join(", ", teile) + "), Rest inhaltsgleich vom Gastgeber";
        return g.ToJsonString();
    }
}
