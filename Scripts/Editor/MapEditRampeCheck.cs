namespace AkteEuropaReborn.Editor;

using System;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// <c>--map-edit-rampe-check</c> — D9 (13.11.2026): die Editorrampe (Werkzeug 0, »Landungsrampe«).
/// Uferzelle suchen, ueber den Mausweg setzen, mit DERSELBEN Messlatte pruefen wie die 24
/// Originalrampen (<see cref="RampenMuster.Pruefen"/>), Fehlklick abgewiesen, Rueckgaengig/
/// Wiederholen bytegleich, gespeichert: Satz in <c>ramps_table</c>, Lage 200+Platz in <c>ramps</c>.
/// Gegenprobe <c>--editor-rampe-aus</c> → DURCHGEFALLEN. <c>--rampe-shot=&lt;datei&gt;</c>: Fensterbild.
/// </summary>
public partial class MapEditOverlay
{
    private async void RampeCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-rampe-check") < 0) return;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ulong Abdruck()
        {
            ulong h = 14695981039346656037UL;
            foreach (var a in _map.Sections) foreach (byte b in a) { h ^= b; h *= 1099511628211UL; }
            return h;
        }
        bool ok = true;
        Vector2I? zelle = null;
        string grund = "";
        for (int r = 4; r < _map.Height - 4 && zelle == null; r++)
            for (int c = 4; c < _map.Width - 4 && zelle == null; c++)
                if (MapRampEdit.Planen(_map, MapEditSession.Cwp, c, r, MapEditSession.Seed, out _, out string w) >= 0) zelle = new Vector2I(c, r);
                else if (grund.Length == 0 && w.Contains("Kachelsatz")) grund = w;
        if (zelle is not { } z0)
        {
            MapRampEdit.Planen(_map, MapEditSession.Cwp, 10, 10, 0, out _, out string w0);
            GD.Print($"map-edit-rampe-check: DURCHGEFALLEN — keine Uferzelle fuer eine Rampe ({(grund.Length > 0 ? grund : w0)})");
            GetTree().Quit(1); return;
        }
        _brush = Brush.Bruecke; _brueckeArt = 1; _pinsel = 1;
        ulong h0 = Abdruck();
        var klassenVorher = new System.Collections.Generic.List<(int C, int R, int K)> { (z0.X, z0.Y, MapEditTerrain.ClassAt(_map, z0.X, z0.Y)) };
        // Fehlklick: eine Wasserzelle
        Vector2I? nass = null;
        for (int r = 4; r < _map.Height - 4 && nass == null; r++)
            for (int c = 4; c < _map.Width - 4 && nass == null; c++)
                if (MapEditTerrain.ClassAt(_map, c, r) == 2) nass = new Vector2I(c, r);
        if (nass is { } n0)
        {
            StrichBeginnen(true); Anwenden(n0, true); StrichEnden();
            bool abg = Abdruck() == h0 && _say.StartsWith("keine Rampe");
            GD.Print($"map-edit-rampe-check: Fehlklick auf Wasser {n0}: {(abg ? "abgewiesen" : "NICHT abgewiesen")} — {_say}");
            ok &= abg;
        }
        var bk0 = new MapBaker(KartenKopie(), MapEditSession.Cwp!, MapEditSession.Pal!);
        var boden0 = bk0.Bake(); var obj0 = bk0.ObjectLayer();
        StrichBeginnen(true); Anwenden(z0, true); StrichEnden();
        ulong h1 = Abdruck();
        {
            var bk1 = new MapBaker(KartenKopie(), MapEditSession.Cwp!, MapEditSession.Pal!);
            var boden1 = bk1.Bake(); var obj1 = bk1.ObjectLayer();
            var p0 = _layer.EditorCellTopLeft(z0.X, z0.Y);
            var rc = new Rect2I((int)p0.X, (int)p0.Y - 10, MapBaker.TileW, MapBaker.TileH + 20);
            int D(Image? a, Image? b)
            {
                if (a == null || b == null) return -1;
                var da = a.GetRegion(rc).GetData(); var db = b.GetRegion(rc).GetData(); int n = 0;
                for (int i = 0; i < da.Length && i < db.Length; i++) if (da[i] != db[i]) n++;
                return n;
            }
            GD.Print($"map-edit-rampe-check: gebacken an der Rampenzelle: Boden {D(boden0, boden1)} Bytes anders, Objektebene {D(obj0, obj1)} Bytes anders");
        }
        GD.Print($"map-edit-rampe-check: Rampe auf {z0}: {_say}");
        var rampen = CwmExtra.Ramps(_map);
        ok &= rampen.Count >= 1 && h1 != h0;

        int falsch = RampenMuster.Pruefen(new[] { ("Editor", _map) }, s => GD.Print("map-edit-rampe-check: " + s.Replace("rampen-muster: ", "")));
        GD.Print($"map-edit-rampe-check: Messlatte der Originalrampen: {(falsch == 0 ? "jede Regel ohne Ausnahme" : $"{falsch} Regeln VERLETZT")}");
        ok &= falsch == 0;

        Rueckgaengig();
        bool zurueck = Abdruck() == h0;
        Wiederholen();
        bool wieder = Abdruck() == h1;
        GD.Print($"map-edit-rampe-check: Rueckgaengig {(zurueck ? "bytegleich" : "ANDERS")}, Wiederholen {(wieder ? "bytegleich" : "ANDERS")}");
        ok &= zurueck && wieder;

        // 4b. WEGNEHMEN (Werkzeug 7) — Satz leer, Lage 0, keine Objektkachel, jede Zelle wieder ihre alte Klasse
        {
            var keepB = _brush;
            _brush = Brush.Wegnehmen;
            StrichBeginnen(false); Anwenden(z0, false); StrichEnden();
            string weg = _say;
            var l20w = _map.Sec(20)!;
            int lagen = 0, objekte = 0, klasseFalsch = 0;
            foreach (var (c, r, k) in klassenVorher)
            {
                if (l20w[c * MapFactory.ImapStride + r] >= 100) lagen++;
                // nur BAUWERKSkacheln zaehlen: der Pinsel pflanzt beim Nachkacheln selbst Bewuchs
                int cd = _map.CodeAt(c, r);
                if (cd is >= 10000 and < 10240 || cd is >= MapRampEdit.KachelBasis and < MapRampEdit.KachelBasis + 16) objekte++;
                if (MapEditTerrain.ClassAt(_map, c, r) != k) klasseFalsch++;
            }
            bool wegOk = CwmExtra.Ramps(_map).Count == 0 && lagen == 0 && objekte == 0 && klasseFalsch == 0;
            GD.Print($"map-edit-rampe-check: Wegnehmen: {weg} — Saetze {CwmExtra.Ramps(_map).Count}, Lagen {lagen}, Bauwerkskacheln {objekte}, Klasse anders {klasseFalsch} von {klassenVorher.Count} — {(wegOk ? "ok" : "FALSCH")}");
            ok &= wegOk;
            Rueckgaengig();
            bool wiederDa = Abdruck() == h1;
            GD.Print($"map-edit-rampe-check: Wegnehmen rueckgaengig: {(wiederDa ? "bytegleich" : "ANDERS")}");
            ok &= wiederDa;
            _brush = keepB;
        }

        string? shot = Core.CommandLine.Args.FirstOrDefault(x => x.StartsWith("--rampe-shot="))?["--rampe-shot=".Length..];
        if (shot != null && GetViewport().GetCamera2D() is { } cam)
        {
            cam.Zoom = new Vector2(3, 3);
            cam.GlobalPosition = _layer.GetGlobalTransform() * (_layer.EditorCellTopLeft(z0.X, z0.Y) + new Vector2(20, 10));
            _mine.Clear(); QueueRedraw();          // die gelbe Aenderungsmarke deckt sonst die Rampe
            for (int i = 0; i < 600 && SofortGetauscht < 1; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(shot);
            GD.Print($"map-edit-rampe-check: Bild {shot}");
            if (MapEditSession.Cwp is { } cwp && MapEditSession.Pal is { } pal)
                for (int k = 0; k < 8; k++)
                {
                    var f = cwp.DecodeObject(MapRampEdit.KachelBasis + k);
                    GD.Print($"map-edit-rampe-check: Kachel {MapRampEdit.KachelBasis + k}: {f.Width}x{f.Height}");
                    if (!f.IsEmpty) CwpFile.ToImage(f, pal).SavePng($"{shot}_k{k}.png");
                }
        }

        string? made = MapEditSession.Save(_ => { });
        if (made != null && rampen.Count > 0)
        {
            var r0 = rampen[^1];
            string p = Core.Content.Path($"Maps/{made}.entities.json");
            var ent = FileAccess.FileExists(p) ? System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(p)) : null;
            bool satz = ent?["ramps_table"]?.AsArray().Any(n => (int?)n?["slot"] == r0.Slot && (int?)n?["col"] == r0.Col && (int?)n?["count"] == 200) == true;
            string pm = Core.Content.Path($"Maps/{made}.json");
            var meta = FileAccess.FileExists(pm) ? System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(pm)) : null;
            bool lage = meta?["ramps"]?.AsArray().Any(n => (int?)n?["col"] == r0.Col && (int?)n?["row"] == r0.Row && (int?)n?["lage"] == 200 + r0.Slot) == true;
            GD.Print($"map-edit-rampe-check: gespeichert ({made}): Satz in ramps_table {(satz ? "da" : "FEHLT")}, Lage 200+{r0.Slot} {(lage ? "da" : "FEHLT")}");
            ok &= satz && lage;
        }
        else { ok = false; GD.Print("map-edit-rampe-check: nicht gespeichert"); }

        GD.Print($"map-edit-rampe-check: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
