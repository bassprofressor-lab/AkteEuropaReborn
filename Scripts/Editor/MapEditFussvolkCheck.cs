namespace AkteEuropaReborn.Editor;

using System;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// <c>--map-edit-fussvolk-check</c> — D9 (13.11.2026): Fussvolk setzen, mit Blickrichtung.
/// Setzt je einen Leichten (148) und einen Schweren Soldaten (149) mit Richtung 5 und prueft
/// <list type="bullet">
/// <item>den Kartensatz: gleich der gemessenen Vorlage bis auf Lage, Richtung und Zellversatz;
/// Richtung +0x02/+0x03 = 5; Versatz im Bereich des Originals (−18..18 / −7..7);</item>
/// <item>die lebende Einheit: Fussvolksatz, Richtung, Versatz, und dass das Stehbild existiert;</item>
/// <item>nach dem Speichern den Rohsatz in der <c>.entities.json</c>, den das Spiel laedt.</item>
/// </list>
/// Gegenprobe: mit <c>--editor-fussvolk-aus</c> steht kein Fussvolk in der Liste → DURCHGEFALLEN.
/// </summary>
public partial class MapEditOverlay
{
    private async void FussvolkCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-fussvolk-check") < 0) return;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        const int Richtung = 5;
        bool ok = true;
        var zellen = new System.Collections.Generic.List<Vector2I>();
        for (int r = 6; r < _map.Height - 6 && zellen.Count < 2; r++)
            for (int c = 6; c < _map.Width - 6 && zellen.Count < 2; c += 3)
                if (MapEditTerrain.ClassAt(_map, c, r) == 0 && _map.FlagAt(c, r) == 0
                    && MapFactory.Belegt(_map, c, r) == null
                    && (_layer.NavGridOf() is not { } nav || nav.CanEnter(c, r, Simulation.NavGrid.ClassOf(1, -1))))
                    zellen.Add(new Vector2I(c, r));
        if (zellen.Count < 2) { GD.Print("map-edit-fussvolk-check: DURCHGEFALLEN — keine freien Zellen"); GetTree().Quit(1); return; }

        var gesetzt = new System.Collections.Generic.List<(int Typ, Vector2I Zelle, int Slot)>();
        int k = 0;
        foreach (int typ in new[] { 148, 149 })
        {
            int idx = Array.FindIndex(MapEditUnits.Liste, u => u.Type == typ);
            if (idx < 0) { GD.Print($"map-edit-fussvolk-check: Typ {typ} NICHT in der Liste"); ok = false; continue; }
            _brush = Brush.Einheit; _owner = 0; _unitAt = idx; _blick = Richtung;
            var z = zellen[k++];
            PutUnit(z);
            int slot = SlotBei(z, typ);
            GD.Print($"map-edit-fussvolk-check: {MapEditUnits.Liste[idx].Name} auf {z} — {_say}");
            if (slot < 0) { GD.Print("map-edit-fussvolk-check:   kein Satz geschrieben"); ok = false; continue; }
            gesetzt.Add((typ, z, slot));

            // 1. der Kartensatz gegen die Vorlage
            var s = _map.Sec(5)!; int o = slot * CwmData.EntityStride;
            var v = MapEditUnits.Vorlage(typ)!;
            var frei = new[] { 0x00, 0x01, 0x02, 0x03, 0x1D, 0x1E };
            int abw = 0;
            for (int b = 0; b < v.Length; b++) if (!frei.Contains(b) && s[o + b] != v[b]) abw++;
            sbyte dx = (sbyte)s[o + 0x1D], dy = (sbyte)s[o + 0x1E];
            bool satzOk = abw == 0 && s[o + 2] == Richtung && s[o + 3] == Richtung
                          && dx is >= -18 and <= 18 && dy is >= -7 and <= 7 && s[o + 0x0B] == MapEditUnits.Fussvolksatz(typ);
            GD.Print($"map-edit-fussvolk-check:   Satz {slot}: {abw} Byte weichen von der Vorlage ab, Richtung {s[o + 2]}/{s[o + 3]}, " +
                     $"Versatz ({dx},{dy}), Fussvolksatz {s[o + 0x0B]} — {(satzOk ? "ok" : "FALSCH")}");
            ok &= satzOk;

            // 2. die lebende Einheit
            var info = _layer.EditorUnitInfo(slot);
            string bild = Core.Content.Path($"Units/infantry/{MapEditUnits.Fussvolksatz(typ)}/f{Richtung}_b11.png");
            bool bildDa = FileAccess.FileExists(bild);
            bool lebtOk = info is { } e && e.Infantry == MapEditUnits.Fussvolksatz(typ) && e.Facing == Richtung
                          && e.StreuX == dx && e.StreuY == dy && bildDa;
            GD.Print($"map-edit-fussvolk-check:   Einheit: {(info is { } i2 ? $"Fussvolksatz {i2.Infantry}, Richtung {i2.Facing}, Versatz ({i2.StreuX},{i2.StreuY})" : "FEHLT")}, " +
                     $"Stehbild {(bildDa ? "da" : "FEHLT")} — {(lebtOk ? "ok" : "FALSCH")}");
            ok &= lebtOk;
        }

        // 2b. mit --fussvolk-shot=<datei> (Fensterlauf): acht Soldaten, einer je Richtung,
        // in einer Reihe; die Kamera faehrt hin, und das Bild wird gespeichert.
        string? shot = Core.CommandLine.Args.FirstOrDefault(x => x.StartsWith("--fussvolk-shot="))?["--fussvolk-shot=".Length..];
        if (shot != null && gesetzt.Count > 0)
        {
            var z0 = gesetzt[0].Zelle + new Vector2I(0, 3);
            for (int f = 0; f < 8; f++)
            {
                var z = z0 + new Vector2I(f, 0);
                if (MapFactory.Belegt(_map, z.X, z.Y) != null) continue;
                _unitAt = Array.FindIndex(MapEditUnits.Liste, u => u.Type == (f % 2 == 0 ? 148 : 149));
                _blick = f; PutUnit(z);
            }
            if (GetViewport().GetCamera2D() is { } cam)
            {
                cam.GlobalPosition = _layer.GetGlobalTransform() * (_layer.EditorCellTopLeft(z0.X + 4, z0.Y) + new Vector2(0, 10));
                cam.Zoom = new Vector2(3, 3);
            }
            _brush = Brush.Einheit; _blick = 3;
            _unitAt = Array.FindIndex(MapEditUnits.Liste, u => u.Type == 148);
            FillList(); SelectInList(); Refresh();
            for (int i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(shot);
            GD.Print($"map-edit-fussvolk-check: Bild {shot} ({img.GetWidth()}x{img.GetHeight()})");
        }

        // 3. gespeichert: der Rohsatz, den das Spiel laedt
        string? made = MapEditSession.Save(_ => { });
        if (made != null)
        {
            string p = Core.Content.Path($"Maps/{made}.entities.json");
            if (!FileAccess.FileExists(p)) p = Core.Content.Path($"Maps/map_{made}.entities.json");
            var json = FileAccess.FileExists(p) ? System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(p)) : null;
            var liste = json?["entities"]?.AsArray();
            foreach (var (typ, z, _) in gesetzt)
            {
                var e = liste?.FirstOrDefault(n => (int?)n?["unit_type"] == typ && (int?)n?["col"] == z.X && (int?)n?["row"] == z.Y);
                string raw = (string?)e?["raw"] ?? "";
                bool dateiOk = raw.Length >= 0x0C * 2
                               && Convert.ToInt32(raw.Substring(0x0B * 2, 2), 16) == MapEditUnits.Fussvolksatz(typ)
                               && Convert.ToInt32(raw.Substring(0x02 * 2, 2), 16) == Richtung;
                GD.Print($"map-edit-fussvolk-check:   gespeichert ({made}): Typ {typ} auf {z} " +
                         $"{(e == null ? "NICHT gefunden" : dateiOk ? "mit Fussvolksatz und Richtung" : "FALSCH")}");
                ok &= dateiOk;
            }
        }
        else { GD.Print("map-edit-fussvolk-check: nicht gespeichert"); ok = false; }

        GD.Print($"map-edit-fussvolk-check: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }

    private int SlotBei(Vector2I z, int typ)
    {
        var s = _map.Sec(5);
        if (s == null) return -1;
        for (int slot = 0; (slot + 1) * CwmData.EntityStride <= s.Length; slot++)
        {
            int o = slot * CwmData.EntityStride;
            if (s[o + 0x09] != 0xFF && s[o] == z.X && s[o + 1] == z.Y && s[o + 0x0F] == typ) return slot;
        }
        return -1;
    }
}
