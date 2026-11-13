namespace AkteEuropaReborn.Editor;

using System;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// <c>--map-edit-satz-check</c> — B3 (13.11.2026): jede Fahrzeugbauart der Liste setzen und pruefen,
/// dass die uebrigen Satzfelder (<see cref="MapEditUnits.SatzFelderOf"/>) im Kartensatz stehen und
/// der Entwurf +0x3E/+0x43 nach dem Speichern in der <c>.entities.json</c> ankommt; dazu der
/// Entwurfsname aus der globalen Tafel. Gegenprobe <c>--editor-satzfelder-alt</c> → DURCHGEFALLEN.
/// </summary>
public partial class MapEditOverlay
{
    private async void SatzCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-satz-check") < 0) return;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool ok = true;
        var gesetzt = new System.Collections.Generic.List<(int Typ, int Slot, Vector2I Z)>();
        int r0 = 6;
        foreach (var u in MapEditUnits.Liste)
        {
            if (u.Art == 1) continue;                                   // Fussvolk: eigener Pruefstand
            int idx = Array.IndexOf(MapEditUnits.Liste, u);
            int slot = -1; Vector2I zz = default;
            int side = Simulation.NavGrid.HullSide(u.Art);
            _brush = Brush.Einheit; _owner = 0; _unitAt = idx; _blick = 0;
            for (int r = r0; r < _map.Height - 8 && slot < 0; r++)
                for (int c = 6; c < _map.Width - 8 && slot < 0; c += 2)
                {
                    int k = MapEditTerrain.ClassAt(_map, c, r);
                    if (!(u.Art is 4 or 5 ? k == 2 : k is 0 or 1) || MapFactory.Belegt(_map, c, r) != null) continue;
                    PutUnit(new Vector2I(c, r));
                    slot = SlotBei(new Vector2I(c, r), u.Type);
                    if (slot >= 0) zz = new Vector2I(c, r);
                }
            if (slot < 0) { GD.Print($"map-edit-satz-check: {u.Name}: kein Platz ({_say})"); ok = false; continue; }
            var s = _map.Sec(5)!; int o = slot * CwmData.EntityStride;
            var soll = MapEditUnits.SatzFelderOf(u.Type);
            int falsch = soll.Count(f => s[o + f.Off] != f.Wert);
            bool hat = soll.Length > 0 && falsch == 0;
            GD.Print($"map-edit-satz-check: {u.Name,-18} Platz {slot}: {soll.Length} Felder, {falsch} falsch, Entwurf {s[o + 0x3E]}/{s[o + 0x43]} — {(hat ? "ok" : "FALSCH")}");
            ok &= hat;
            gesetzt.Add((u.Type, slot, zz));
        }
        string? made = MapEditSession.Save(_ => { });
        if (made != null)
        {
            string p = Core.Content.Path($"Maps/{made}.entities.json");
            var ent = FileAccess.FileExists(p) ? System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(p)) : null;
            var liste = ent?["entities"]?.AsArray();
            int angekommen = 0;
            foreach (var (typ, _, z) in gesetzt)
            {
                var e = liste?.FirstOrDefault(n => (int?)n?["unit_type"] == typ && (int?)n?["col"] == z.X && (int?)n?["row"] == z.Y);
                string raw = (string?)e?["raw"] ?? "";
                int soll = MapEditUnits.SatzFelderOf(typ).FirstOrDefault(f => f.Off == 0x3E).Wert;
                if (raw.Length > 0x3E * 2 && Convert.ToInt32(raw.Substring(0x3E * 2, 2), 16) == soll) angekommen++;
            }
            GD.Print($"map-edit-satz-check: gespeichert ({made}): Entwurf in {angekommen} von {gesetzt.Count} Saetzen angekommen");
            ok &= angekommen == gesetzt.Count && gesetzt.Count > 0;
        }
        else ok = false;
        GD.Print($"map-edit-satz-check: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
