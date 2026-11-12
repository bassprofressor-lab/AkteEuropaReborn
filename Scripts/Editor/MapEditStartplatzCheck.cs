namespace AkteEuropaReborn.Editor;

using System;
using System.Linq;
using Godot;
using AkteEuropaReborn.Import;

/// <summary><c>--map-edit-start-check</c> — Pruefstand fuer das Startplatz-Werkzeug (P6/D5,
/// 12.11.2026). Geht die Mauswege (StrichBeginnen/Anwenden/StrichEnden).</summary>
public partial class MapEditOverlay
{
    private void StartCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-start-check") < 0) return;
        var sb = new System.Text.StringBuilder("map-edit-start-check\n");
        ulong Abdruck()
        {
            ulong h = 14695981039346656037UL;
            foreach (var a in _map.Sections) foreach (byte b in a) { h ^= b; h *= 1099511628211UL; }
            return h;
        }
        int Basen(int p) => GebaeudeSaetze().Values.Count(g => g.Owner == p && g.Typ == MapGenerator.BaseType);
        int Werke(int p) => GebaeudeSaetze().Values.Count(g => g.Owner == p && g.Typ == MapGenerator.FactoryType);
        int Einheiten(int p)
        {
            var s = _map.Sec(5); int n = 0;
            if (s == null) return 0;
            for (int o = 0; o + CwmData.EntityStride <= s.Length; o += CwmData.EntityStride)
                if (s[o + 0x09] != 0xFF && o / CwmData.EntityStride / CwmData.EntitiesPerPlayer == p) n++;
            return n;
        }
        (int Col, int Row)? BasisVon(int p)
        {
            foreach (var g in GebaeudeSaetze().Values)
                if (g.Owner == p && g.Typ == MapGenerator.BaseType) return (g.Col, g.Row);
            return null;
        }

        ulong h0 = Abdruck();
        int b0 = Basen(0), w0 = Werke(0), e0 = Einheiten(0);
        var alt = BasisVon(0);
        sb.AppendLine($"  vorher Spieler 1: Basen {b0}, Werke {w0}, Einheiten {e0}, Basis bei {alt}");

        // 1. Fehlschlag: auf Zelle (0,0) passt nichts -> Karte unveraendert, kein Schritt mehr
        _brush = Brush.Startplatz; _owner = 0;
        int schritte0 = _zurueck.Count;
        StrichBeginnen(true); Anwenden(new Vector2I(0, 0), true); StrichEnden();
        bool fehlOk = Abdruck() == h0 && _zurueck.Count == schritte0;
        sb.AppendLine($"  Fehlschlag (0,0): {(fehlOk ? "Karte BYTEGLEICH, kein Schritt" : "VERAENDERT")} — {_say}");

        // 2. Verschieben an die erste Stelle, an der das Paket passt (weit weg vom alten)
        bool gesetzt = false;
        for (int r = 4; r < _map.Height - 14 && !gesetzt; r += 2)
            for (int c = 4; c < _map.Width - 18 && !gesetzt; c += 2)
            {
                if (alt is { } a && Math.Abs(a.Col - c) < 30 && Math.Abs(a.Row - r) < 30) continue;
                StrichBeginnen(true); Anwenden(new Vector2I(c, r), true); StrichEnden();
                if (_say.StartsWith("Startplatz Spieler")) gesetzt = true;
            }
        var neu = BasisVon(0);
        sb.AppendLine($"  verschoben: {gesetzt} — {_say}");
        sb.AppendLine($"  nachher Spieler 1: Basen {Basen(0)}, Werke {Werke(0)}, Einheiten {Einheiten(0)}, Basis bei {neu}");
        bool verschiebOk = gesetzt && Basen(0) == b0 && Werke(0) == w0 && neu != alt && Einheiten(0) == StartTruppe;

        // 3. ein Rueckgaengig -> bytegleich zum Anfang
        Rueckgaengig();
        bool zurueckOk = Abdruck() == h0;
        sb.AppendLine($"  1x Rueckgaengig: Karte {(zurueckOk ? "BYTEGLEICH zum Anfang" : "ANDERS")}, Basis bei {BasisVon(0)}");

        bool ok = fehlOk && verschiebOk && zurueckOk;
        sb.Append(ok ? "map-edit-start-check: BESTANDEN" : "map-edit-start-check: DURCHGEFALLEN");
        GD.Print(sb.ToString());
        GetTree().Quit(ok ? 0 : 1);
    }
}

/// <summary><c>--map-edit-kamera-check</c> — C1 (12.11.2026): Blick verschieben, F5,
/// nach dem Neuladen muss die Kamera wieder dort stehen (Gegenprobe --editor-kamera-alt).</summary>
public partial class MapEditOverlay
{
    private static int _kamPhase;
    private static Vector2 _kamZiel, _kamZielZoom;

    private async void KameraCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-kamera-check") < 0) return;
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var cam = GetViewport().GetCamera2D();
        if (cam == null) { GD.Print("map-edit-kamera-check: DURCHGEFALLEN — keine Kamera"); GetTree().Quit(1); return; }
        if (_kamPhase == 0)
        {
            _kamPhase = 1;
            _kamZielZoom = new Vector2(2.5f, 2.5f);
            _kamZiel = cam.Position + new Vector2(240, 160);
            cam.Zoom = _kamZielZoom; cam.Position = _kamZiel;
            GD.Print($"map-edit-kamera-check: vorher Blick ({_kamZiel.X:0},{_kamZiel.Y:0}) Zoom 2.5 — F5");
            SaveAndReload();
            return;
        }
        float d = cam.Position.DistanceTo(_kamZiel);
        bool ok = d < 1f && Mathf.Abs(cam.Zoom.X - _kamZielZoom.X) < 0.01f;
        GD.Print($"map-edit-kamera-check: nach F5 Blick ({cam.Position.X:0},{cam.Position.Y:0}) Zoom {cam.Zoom.X:0.##}, " +
                 $"Abstand {d:0} px — {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}

/// <summary><c>--map-edit-minikarte-check</c> — D10 (12.11.2026): ein echter Mausklick
/// auf die Minikarte im Bearbeitungsmodus muss die Kamera versetzen und darf die Karte
/// NICHT bemalen.</summary>
public partial class MapEditOverlay
{
    private async void MinikarteCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-minikarte-check") < 0) return;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var mini = new System.Collections.Generic.List<Node>();
        void Suche(Node n) { if (n is Rendering.Minimap) mini.Add(n); foreach (Node k in n.GetChildren()) Suche(k); }
        Suche(GetTree().Root);
        var cam = GetViewport().GetCamera2D();
        if (mini.Count == 0 || cam == null)
        { GD.Print($"map-edit-minikarte-check: DURCHGEFALLEN — Minikarte {mini.Count}, Kamera {cam != null}"); GetTree().Quit(1); return; }
        var m = (Rendering.Minimap)mini[0];
        ulong Abdruck()
        {
            ulong h = 14695981039346656037UL;
            foreach (var a in _map.Sections) foreach (byte b in a) { h ^= b; h *= 1099511628211UL; }
            return h;
        }
        _brush = Brush.Gelaende;
        ulong h0 = Abdruck();
        var r = m.GetGlobalRect();
        var vorher = cam.Position;
        bool treffer = true;
        // ⚠ Input.ParseInputEvent rechnet in FENSTERpunkten, die Minikarte liegt in
        // Spielflaechen-Punkten (Fenster 1920x1080, Flaeche 1600x900): umrechnen.
        var nachFenster = GetViewport().GetFinalTransform();
        foreach (var flaeche in new[] { r.Position + r.Size * new Vector2(0.5f, 0.1f), r.Position + r.Size * new Vector2(0.9f, 0.5f), r.Position + r.Size * new Vector2(0.3f, 0.9f) })
        {
            var punkt = nachFenster * flaeche;
            ulong hv = Abdruck(); int sv = m.Spruenge;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = punkt, GlobalPosition = punkt });
            for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            foreach (bool down in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton
                { ButtonIndex = MouseButton.Left, Pressed = down, Position = punkt, GlobalPosition = punkt });
                for (int i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var unter = GetViewport().GuiGetHoveredControl();
            if (m.Spruenge == sv || Abdruck() != hv) treffer = false;
            GD.Print($"map-edit-minikarte-check:   Klick ({punkt.X:0},{punkt.Y:0}): Sprung {(m.Spruenge > sv ? "ja" : "NEIN")}, " +
                     $"Karte {(Abdruck() == hv ? "gleich" : "BEMALT")}, unter der Maus: {unter?.GetType().Name ?? "-"} {unter?.Name}");
        }
        float weg = cam.Position.DistanceTo(vorher);
        bool karteGleich = Abdruck() == h0;
        bool ok = weg > 50 && karteGleich && m.Visible && treffer;
        GD.Print($"map-edit-minikarte-check: Minikarte {r.Size.X:0}x{r.Size.Y:0} bei ({r.Position.X:0},{r.Position.Y:0}), " +
                 $"Kamera {weg:0} px versetzt, Karte {(karteGleich ? "unveraendert" : "BEMALT")}, Spruenge {m.Spruenge} — " +
                 (ok ? "BESTANDEN" : "DURCHGEFALLEN"));
        GetTree().Quit(ok ? 0 : 1);
    }
}

/// <summary><c>--map-edit-spiegel-check</c> — D6 (12.11.2026): Taste M schaltet das Spiegeln
/// (und NICHT den Entwurfsschirm des Spiels), ein Geroellstrich landet zugleich auf der
/// gespiegelten Zelle, ein Gebaeude nicht; ein Rueckgaengig nimmt beide zurueck.</summary>
public partial class MapEditOverlay
{
    private async void SpiegelCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-spiegel-check") < 0) return;
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ulong Abdruck()
        {
            ulong h = 14695981039346656037UL;
            foreach (var a in _map.Sections) foreach (byte b in a) { h ^= b; h *= 1099511628211UL; }
            return h;
        }
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.M, PhysicalKeycode = Key.M, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.M, PhysicalKeycode = Key.M, Pressed = false });
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        bool designer = _layer.Designer.Active;
        bool an = _spiegeln;
        // eine freie Zelle suchen, deren Spiegelzelle auch frei ist
        Vector2I? z = null;
        for (int r = 4; r < _map.Height / 2 && z == null; r++)
            for (int c = 4; c < _map.Width - 4 && z == null; c++)
            {
                int sc = _map.Width - 1 - c, sr = _map.Height - 1 - r;
                if (MapEditTerrain.ClassAt(_map, c, r) == 0 && MapEditTerrain.ClassAt(_map, sc, sr) == 0
                    && _map.CodeAt(c, r) < CwpFile.ObjectCodeBase && _map.CodeAt(sc, sr) < CwpFile.ObjectCodeBase)
                    z = new Vector2I(c, r);
            }
        ulong h0 = Abdruck();
        bool spiegelOk = false, rueckOk = false;
        if (z is { } zz)
        {
            _brush = Brush.Gelaende; _ground = MapFactory.Ground.Rough; _pinsel = 1;
            StrichBeginnen(true); Anwenden(zz, true); StrichEnden();
            int sc = _map.Width - 1 - zz.X, sr = _map.Height - 1 - zz.Y;
            spiegelOk = MapEditTerrain.ClassAt(_map, zz.X, zz.Y) == 1 && MapEditTerrain.ClassAt(_map, sc, sr) == 1;
            GD.Print($"map-edit-spiegel-check: Geroell auf ({zz.X},{zz.Y}) -> Spiegelzelle ({sc},{sr}) " +
                     $"{(spiegelOk ? "auch rau" : "NICHT rau")} — {_say}");
            Rueckgaengig();
            rueckOk = Abdruck() == h0;
        }
        bool ok = an && !designer && spiegelOk && rueckOk;
        GD.Print($"map-edit-spiegel-check: Taste M -> Spiegeln {(an ? "AN" : "aus")}, Entwurfsschirm {(designer ? "OFFEN" : "zu")}, " +
                 $"1x Rueckgaengig {(rueckOk ? "bytegleich" : "ANDERS")} — {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
