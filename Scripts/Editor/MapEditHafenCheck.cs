namespace AkteEuropaReborn.Editor;

using System;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// <c>--map-edit-hafen-check</c> — bug-832 (14.11.2026): der Hafen im Gebaeudepinsel.
/// <list type="number">
/// <item>Hafen auf Land → abgewiesen, kein Gebaeude mehr.</item>
/// <item>Eine Werft an Land am Ufer, dann ein Hafen ≤ 15 Zellen davon im Wasser: sec3 Typ 11, Eigner der
/// Werft, sec29[cisTyp] = (Hafen, Werft), jede Musterzelle vorher Wasser.</item>
/// <item>Werft wegnehmen (Rechtsklick): der Hafen geht mit, sec29 leer, die Wasserzellen sind wieder
/// Wasser (Klasse 2), nicht gesperrt.</item>
/// <item>Strg+Z: Karte bytegleich wie vor dem Wegnehmen.</item>
/// <item>Speichern: <c>shipyard</c> des Hafens in der <c>.entities.json</c> = Werftplatz.</item>
/// </list>
/// Gegenprobe <c>--editor-hafen-pinsel-alt</c>: der Hafen geht nicht ins Wasser → DURCHGEFALLEN.
/// </summary>
public partial class MapEditOverlay
{
    private async void HafenCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-hafen-check") < 0) return;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        const string T = "map-edit-hafen-check";
        var s3 = _map.Sec(3)!;
        int Gebaeude() { int n = 0; for (int o = 0; o + CwmData.BuildingStride <= s3.Length; o += CwmData.BuildingStride) if (s3[o + 0x18] == 1) n++; return n; }
        bool Steht(int slot, int typ) => slot >= 0 && s3[slot * CwmData.BuildingStride + 0x18] == 1 && s3[slot * CwmData.BuildingStride + 0x04] == typ;
        bool Klick(Vector2I c, bool links) { int v = Gebaeude(); StrichBeginnen(links); Anwenden(c, links); StrichEnden(); return Gebaeude() != v; }
        _brush = Brush.Gebaeude; _owner = 0;

        // 1. Hafen auf Land
        _typ = MapNeutrals.HafenTyp;
        Vector2I land = default; bool p1 = false;
        for (int r = 10; r < _map.Height - 12 && land == default; r += 3)
            for (int c = 10; c < _map.Width - 12; c += 3)
                if (MapEditTerrain.ClassAt(_map, c, r) == 0 && MapEditTerrain.ClassAt(_map, c + 3, r + 3) == 0) { land = new Vector2I(c, r); break; }
        if (land != default) p1 = !Klick(land, true);
        GD.Print($"{T}: Hafen auf Land {land}: {(p1 ? "abgewiesen" : "GESETZT")} — {(p1 ? "ok" : "FALSCH")} ({_say})");

        // 2. Werft am Ufer, Hafen im Wasser
        var foot = _layer.EditorFootCells(MapNeutrals.HafenTyp);
        int werft = -1, hafen = -1; Vector2I wo = default, ho = default; bool vorherWasser = false;
        for (int r = 8; r < _map.Height - 12 && hafen < 0; r += 2)
            for (int c = 8; c < _map.Width - 12 && hafen < 0; c += 2)
            {
                // nur Plaetze mit Wasser in der Naehe
                bool nass = false;
                for (int d = -8; d <= 8 && !nass; d += 2)
                    for (int e = -8; e <= 8 && !nass; e += 2) nass = MapEditTerrain.ClassAt(_map, c + d, r + e) == 2;
                if (!nass) continue;
                _typ = MapNeutrals.WerftTyp;
                if (!Klick(new Vector2I(c, r), true)) continue;
                werft = _nextSlot - 1; wo = new Vector2I(c, r);
                _typ = MapNeutrals.HafenTyp;
                for (int dy = -15; dy <= 15 && hafen < 0; dy++)
                    for (int dx = -15; dx <= 15 && hafen < 0; dx++)
                    {
                        var hc = wo + new Vector2I(dx, dy);
                        bool wasser = foot.All(f => MapEditTerrain.ClassAt(_map, hc.X + f.Dx, hc.Y + f.Dy) == 2);
                        if (!wasser) continue;
                        if (Klick(hc, true)) { hafen = _nextSlot - 1; ho = hc; vorherWasser = true; }
                    }
                if (hafen < 0) { _typ = MapNeutrals.WerftTyp; Klick(wo, false); werft = -1; }
            }
        var s29 = _map.Sec(29)!;
        int idx = hafen >= 0 ? s3[hafen * CwmData.BuildingStride + 0x19] : -1;
        int l0 = idx >= 0 ? s29[idx * 4] | (s29[idx * 4 + 1] << 8) : -1, l2 = idx >= 0 ? s29[idx * 4 + 2] | (s29[idx * 4 + 3] << 8) : -1;
        int eignerH = hafen >= 0 ? s3[hafen * CwmData.BuildingStride + 5] : -1, eignerW = werft >= 0 ? s3[werft * CwmData.BuildingStride + 5] : -2;
        bool p2 = Steht(werft, 16) && Steht(hafen, 11) && vorherWasser && l0 == hafen && Steht(l2, 16) && eignerH == s3[l2 * CwmData.BuildingStride + 5];
        GD.Print($"{T}: Werft Platz {werft} auf {wo}, Hafen Platz {hafen} auf {ho}, sec29[{idx}] = ({l0}, {l2}), Eigner H{eignerH}/W{eignerW} — {(p2 ? "ok" : "FALSCH")} ({_say})");

        bool ok = p1 && p2;
        if (p2)
        {
            // 3. die verknuepfte Werft wegnehmen (das kann eine Generatorwerft sein, wenn sie naeher lag)
            int lw = l2;
            var lwo = new Vector2I(s3[lw * CwmData.BuildingStride] | (s3[lw * CwmData.BuildingStride + 1] << 8),
                                   s3[lw * CwmData.BuildingStride + 2] | (s3[lw * CwmData.BuildingStride + 3] << 8));
            var vorher = _map.Sections.Select(x => (byte[])x.Clone()).ToList();
            _typ = MapNeutrals.WerftTyp;
            Klick(lwo + new Vector2I(1, 1), false);
            int wasserZurueck = foot.Count(f => MapEditTerrain.ClassAt(_map, ho.X + f.Dx, ho.Y + f.Dy) == 2);
            bool p3 = !Steht(lw, 16) && !Steht(hafen, 11) && s29.Skip(idx * 4).Take(4).All(b => b == 0) && wasserZurueck == foot.Count;
            GD.Print($"{T}: Werft weg: Werft {(Steht(lw, 16) ? "STEHT" : "weg")}, Hafen {(Steht(hafen, 11) ? "STEHT" : "weg")}, Wasser {wasserZurueck}/{foot.Count} — {(p3 ? "ok" : "FALSCH")} ({_say})");

            // 4. Strg+Z
            Rueckgaengig();
            s3 = _map.Sec(3)!; s29 = _map.Sec(29)!;
            bool p4 = vorher.Count == _map.Sections.Count && vorher.Select((b, i) => b.AsSpan().SequenceEqual(_map.Sections[i])).All(x => x);
            GD.Print($"{T}: Rueckgaengig {(p4 ? "bytegleich" : "ANDERS")}");
            ok &= p3 && p4;

            // 5. speichern
            string? made = MapEditSession.Save(_ => { });
            if (made != null)
            {
                string p = Core.Content.Path($"Maps/{made}.entities.json");
                var json = FileAccess.FileExists(p) ? System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(p)) : null;
                var b = json?["buildings"]?.AsArray().FirstOrDefault(n => (int?)n?["slot"] == hafen);
                int? sy = (int?)b?["shipyard"];
                bool p5 = sy == lw;
                GD.Print($"{T}: gespeichert ({made}): Hafen shipyard {sy?.ToString() ?? "FEHLT"} (soll {lw}) — {(p5 ? "ok" : "FALSCH")}");
                ok &= p5;
            }
            else { GD.Print($"{T}: nicht gespeichert"); ok = false; }
        }

        GD.Print($"{T}: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
