namespace AkteEuropaReborn.Editor;

using System;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// <c>--map-edit-lager-check</c> — D9 (13.11.2026): das Lager eines Gebaeudes.
/// <list type="number">
/// <item>Basis (Art 1) mit »viel« setzen → sec3 +0x2C/+0x2E/+0x30 = 1500/2000/1000.</item>
/// <item>Stufe »wenig« waehlen und die stehende Basis anklicken → 500/500/500, kein zweites Gebaeude.</item>
/// <item>Waffen-Fabrik (Art 2) mit Terranium 1000 → +0x32 = 1000, Teile 0.</item>
/// <item>Speichern: dieselben Werte in der <c>.entities.json</c> (w/ch/sp/terranium), die das Spiel laedt.</item>
/// </list>
/// Gegenprobe <c>--editor-lager-aus</c>: alles 0 → DURCHGEFALLEN.
/// Mit <c>--lager-shot=&lt;datei&gt;</c> (Fensterlauf) ein Bild des Bedienfelds.
/// </summary>
public partial class MapEditOverlay
{
    private async void LagerCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-lager-check") < 0) return;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool ok = true;
        int Gebaeude() { int n = 0; var s = _map.Sec(3)!; for (int o = 0; o + CwmData.BuildingStride <= s.Length; o += CwmData.BuildingStride) if (s[o + 0x18] == 1) n++; return n; }
        (int W, int F, int S, int T) Lager(int slot)
        {
            var s = _map.Sec(3)!; int o = slot * CwmData.BuildingStride;
            int U(int at) => s[o + at] | (s[o + at + 1] << 8);
            return (U(0x2c), U(0x2e), U(0x30), U(0x32));
        }
        // einen Platz suchen, an dem der Pinsel das Gebaeude annimmt
        int Setzen(int typ, out Vector2I wo)
        {
            wo = default;
            _brush = Brush.Gebaeude; _typ = typ; _owner = 0;
            int vor = Gebaeude();
            for (int r = 8; r < _map.Height - 12; r += 2)
                for (int c = 8; c < _map.Width - 12; c += 2)
                {
                    PutBuilding(new Vector2I(c, r));
                    if (Gebaeude() > vor) { wo = new Vector2I(c, r); return _nextSlot - 1; }
                }
            return -1;
        }

        _teileStufe = 3;
        int basis = Setzen(1, out var bwo);
        var l1 = basis >= 0 ? Lager(basis) : default;
        bool p1 = basis >= 0 && l1.W == 1500 && l1.F == 2000 && l1.S == 1000;
        GD.Print($"map-edit-lager-check: Basis »viel« auf {bwo}, Platz {basis}: {l1} — {(p1 ? "ok" : "FALSCH")} ({_say})");

        _teileStufe = 1; _brush = Brush.Gebaeude; _typ = 1;
        int n0 = Gebaeude();
        if (basis >= 0) PutBuilding(bwo + new Vector2I(1, 1));
        var l2 = basis >= 0 ? Lager(basis) : default;
        bool p2 = basis >= 0 && l2.W == 500 && l2.F == 500 && l2.S == 500 && Gebaeude() == n0;
        GD.Print($"map-edit-lager-check: Klick auf die Basis mit »wenig«: {l2}, Gebaeude {n0} -> {Gebaeude()} — {(p2 ? "ok" : "FALSCH")} ({_say})");

        _terraStufe = Array.IndexOf(TerraFabrik, 1000);
        int fab = Setzen(2, out var fwo);
        var l3 = fab >= 0 ? Lager(fab) : default;
        bool p3 = fab >= 0 && l3.T == 1000 && l3.W == 0 && l3.F == 0 && l3.S == 0;
        GD.Print($"map-edit-lager-check: Waffen-Fabrik Terranium 1000 auf {fwo}, Platz {fab}: {l3} — {(p3 ? "ok" : "FALSCH")} ({_say})");
        ok = p1 && p2 && p3;

        string? shot = Core.CommandLine.Args.FirstOrDefault(x => x.StartsWith("--lager-shot="))?["--lager-shot=".Length..];
        if (shot != null)
        {
            _brush = Brush.Gebaeude; _typ = 1; _teileStufe = 2;
            Refresh();
            for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(shot + "_basis.png");
            _typ = 2; _terraStufe = 3; Refresh();
            for (int i = 0; i < 20; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().GetTexture().GetImage().SavePng(shot + "_fabrik.png");
            GD.Print($"map-edit-lager-check: Bilder {shot}_basis.png / _fabrik.png");
        }

        string? made = MapEditSession.Save(_ => { });
        if (made != null)
        {
            string p = Core.Content.Path($"Maps/{made}.entities.json");
            var json = FileAccess.FileExists(p) ? System.Text.Json.Nodes.JsonNode.Parse(FileAccess.GetFileAsString(p)) : null;
            var bl = json?["buildings"]?.AsArray();
            string Zeile(int slot)
            {
                var b = bl?.FirstOrDefault(n => (int?)n?["slot"] == slot);
                return b == null ? "FEHLT" : $"{(int?)b["w"]}/{(int?)b["ch"]}/{(int?)b["sp"]}/{(int?)b["terranium"]}";
            }
            string zb = Zeile(basis), zf = Zeile(fab);
            bool p4 = zb == "500/500/500/0" && zf == "0/0/0/1000";
            GD.Print($"map-edit-lager-check: gespeichert ({made}): Basis {zb}, Fabrik {zf} — {(p4 ? "ok" : "FALSCH")}");
            ok &= p4;
        }
        else { GD.Print("map-edit-lager-check: nicht gespeichert"); ok = false; }

        GD.Print($"map-edit-lager-check: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
