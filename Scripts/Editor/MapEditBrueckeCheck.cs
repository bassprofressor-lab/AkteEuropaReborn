namespace AkteEuropaReborn.Editor;

using System;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// <c>--map-edit-bruecke-check</c> — D9 (13.11.2026): die Editorbruecke.
/// <list type="number">
/// <item>eine Uferzelle suchen, an der <see cref="MapBridgeEdit.Planen"/> eine Bruecke zulaesst,
/// und sie ueber den Mausweg (StrichBeginnen/Anwenden/StrichEnden) bauen;</item>
/// <item>die neue Bruecke mit DERSELBEN Messlatte pruefen wie die 52 Originalbruecken
/// (<see cref="BrueckenMuster.Pruefen"/>): jede Regel ohne Ausnahme;</item>
/// <item>ein Klick fern vom Wasser wird abgewiesen und aendert nichts;</item>
/// <item>Rueckgaengig bringt die Karte bytegleich zurueck, Wiederholen die Bruecke;</item>
/// <item>gespeichert: der Satz in <c>bridges</c> und die Lagen 100+Platz in <c>ramps</c>
/// (das, was das Spiel laedt).</item>
/// </list>
/// Gegenprobe <c>--editor-bruecke-aus</c> → DURCHGEFALLEN. Mit <c>--bruecke-shot=&lt;datei&gt;</c>
/// (Fensterlauf) ein Bild der Bruecke.
/// </summary>
public partial class MapEditOverlay
{
    private async void BrueckeCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-bruecke-check") < 0) return;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ulong Abdruck()
        {
            ulong h = 14695981039346656037UL;
            foreach (var a in _map.Sections) foreach (byte b in a) { h ^= b; h *= 1099511628211UL; }
            return h;
        }
        bool ok = true;
        // 1. eine Uferzelle, an der es geht — bevorzugt k = 2 (die haeufigste Laenge nach 3)
        Vector2I? zelle = null; int kGef = 0;
        for (int r = 4; r < _map.Height - 4 && (zelle == null || kGef != 2); r++)
            for (int c = 4; c < _map.Width - 4; c++)
            {
                var pl = MapBridgeEdit.Planen(_map, c, r, out _);
                if (pl == null) continue;
                if (zelle == null || pl.K == 2) { zelle = new Vector2I(c, r); kGef = pl.K; }
                if (kGef == 2) break;
            }
        if (zelle is not { } z0)
        {
            MapBridgeEdit.Planen(_map, 10, 10, out string w0);
            GD.Print($"map-edit-bruecke-check: DURCHGEFALLEN — keine Uferzelle fuer eine Bruecke ({w0})");
            GetTree().Quit(1); return;
        }
        // 3. Fehlklick zuerst: eine Landzelle ohne Wasser daneben
        Vector2I? fern = null;
        for (int r = 4; r < _map.Height - 4 && fern == null; r++)
            for (int c = 4; c < _map.Width - 4 && fern == null; c++)
                if (MapEditTerrain.ClassAt(_map, c, r) == 0 && MapEditTerrain.ClassAt(_map, c, r - 1) == 0
                    && MapEditTerrain.ClassAt(_map, c, r + 1) == 0 && MapEditTerrain.ClassAt(_map, c - 1, r) == 0
                    && MapEditTerrain.ClassAt(_map, c + 1, r) == 0) fern = new Vector2I(c, r);
        _brush = Brush.Bruecke; _pinsel = 1;
        ulong h0 = Abdruck();
        var klassenVorher = new System.Collections.Generic.List<(int C, int R, int K)>();
        foreach (var (c, r, _, _) in MapBridgeEdit.Planen(_map, z0.X, z0.Y, out _)!.Zellen)
            klassenVorher.Add((c, r, MapEditTerrain.ClassAt(_map, c, r)));
        bool abgewiesen = true;
        if (fern is { } f0)
        {
            StrichBeginnen(true); Anwenden(f0, true); StrichEnden();
            abgewiesen = Abdruck() == h0 && _say.StartsWith("keine Brücke");
            GD.Print($"map-edit-bruecke-check: Fehlklick auf {f0}: {(abgewiesen ? "abgewiesen" : "NICHT abgewiesen")} — {_say}");
        }
        ok &= abgewiesen;

        StrichBeginnen(true); Anwenden(z0, true); StrichEnden();
        ulong h1 = Abdruck();
        GD.Print($"map-edit-bruecke-check: Bruecke von {z0}: {_say}");
        var br = CwmExtra.Bridges(_map);
        bool gebaut = br.Count >= 1 && h1 != h0;
        ok &= gebaut;

        // 2. dieselbe Messlatte wie die Originale
        int falsch = BrueckenMuster.Pruefen(new[] { ("Editor", _map) }, s => GD.Print("map-edit-bruecke-check: " + s.Replace("bruecken-muster: ", "")));
        GD.Print($"map-edit-bruecke-check: Messlatte der Originalbruecken: {(falsch == 0 ? "jede Regel ohne Ausnahme" : $"{falsch} Regeln VERLETZT")}");
        ok &= falsch == 0 && gebaut;

        // 4. Rueckgaengig / Wiederholen
        Rueckgaengig();
        bool zurueck = Abdruck() == h0;
        Wiederholen();
        bool wieder = Abdruck() == h1;
        GD.Print($"map-edit-bruecke-check: Rueckgaengig {(zurueck ? "bytegleich" : "ANDERS")}, Wiederholen {(wieder ? "bytegleich" : "ANDERS")}");
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
            bool wegOk = CwmExtra.Bridges(_map).Count == 0 && lagen == 0 && objekte == 0 && klasseFalsch == 0;
            GD.Print($"map-edit-bruecke-check: Wegnehmen: {weg} — Saetze {CwmExtra.Bridges(_map).Count}, Lagen {lagen}, Bauwerkskacheln {objekte}, Klasse anders {klasseFalsch} von {klassenVorher.Count} — {(wegOk ? "ok" : "FALSCH")}");
            ok &= wegOk;
            Rueckgaengig();
            bool wiederDa = Abdruck() == h1;
            GD.Print($"map-edit-bruecke-check: Wegnehmen rueckgaengig: {(wiederDa ? "bytegleich" : "ANDERS")}");
            ok &= wiederDa;
            _brush = keepB;
        }

        string? shot = Core.CommandLine.Args.FirstOrDefault(x => x.StartsWith("--bruecke-shot="))?["--bruecke-shot=".Length..];
        if (shot != null && br.Count > 0 && GetViewport().GetCamera2D() is { } cam)
        {
            var b = br[^1];
            cam.GlobalPosition = _layer.GetGlobalTransform() * (_layer.EditorCellTopLeft(b.Col + 1, b.Row + 1) + new Vector2(20, 10));
            for (int i = 0; i < 600 && SofortGetauscht < 1; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(shot);
            GD.Print($"map-edit-bruecke-check: Bild {shot} (Sofortbild getauscht {SofortGetauscht}x)");
        }

        // 5. gespeichert
        string? made = MapEditSession.Save(_ => { });
        if (made != null && br.Count > 0)
        {
            var b = br[^1];
            string p = Core.Content.Path($"Maps/{made}.entities.json");
            var ent = FileAccess.FileExists(p) ? System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(p)) : null;
            bool satz = ent?["bridges"]?.AsArray().Any(n => (int?)n?["slot"] == b.Slot && (int?)n?["col"] == b.Col) == true;
            string pm = Core.Content.Path($"Maps/{made}.json");
            var meta = FileAccess.FileExists(pm) ? System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(pm)) : null;
            int lagen = meta?["ramps"]?.AsArray().Count(n => (int?)n?["lage"] == 100 + b.Slot) ?? -1;
            int soll = 3 * (b.Len + 2);
            GD.Print($"map-edit-bruecke-check: gespeichert ({made}): Satz in bridges {(satz ? "da" : "FEHLT")}, Lagen 100+{b.Slot}: {lagen} von {soll}");
            ok &= satz && lagen == soll;
        }
        else { ok = false; GD.Print("map-edit-bruecke-check: nicht gespeichert"); }

        GD.Print($"map-edit-bruecke-check: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
