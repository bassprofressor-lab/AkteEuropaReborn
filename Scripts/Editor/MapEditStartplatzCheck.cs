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

/// <summary><c>--map-edit-fahrwerk-check</c> — B6 (12.11.2026): Luftkissen aufs Wasser,
/// Laeufer auf rauen Boden muessen gehen; Reifen aufs Wasser nicht.</summary>
public partial class MapEditOverlay
{
    private async void FahrwerkCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-fahrwerk-check") < 0) return;
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Vector2I? Suche(int klasse)
        {
            for (int r = 3; r < _map.Height - 3; r++)
                for (int c = 3; c < _map.Width - 3; c++)
                    if (MapEditTerrain.ClassAt(_map, c, r) == klasse && MapFactory.Belegt(_map, c, r) == null
                        && _map.CodeAt(c, r) < CwpFile.ObjectCodeBase)
                        return new Vector2I(c, r);
            return null;
        }
        int Index(int typ) { for (int i = 0; i < MapEditUnits.Liste.Length; i++) if (MapEditUnits.Liste[i].Type == typ) return i; return -1; }
        bool Setze(int typ, Vector2I? z)
        {
            if (z is not { } zz) return false;
            _brush = Brush.Einheit; _owner = 0; _unitAt = Index(typ); _say = "";
            StrichBeginnen(true); Anwenden(zz, true); StrichEnden();
            return _say.Contains(" auf (");
        }
        var wasser = Suche(2); var rau = Suche(1);
        bool kissen = Setze(166, wasser);
        string sk = _say;
        bool laeufer = Setze(174, rau);
        string sl = _say;
        var wasser2 = Suche(2);
        bool reifen = Setze(161, wasser2);
        bool ok = wasser != null && rau != null && kissen && laeufer && !reifen;
        GD.Print($"map-edit-fahrwerk-check: Luftkissen auf Wasser {wasser}: {(kissen ? "gesetzt" : "ABGEWIESEN")} ({sk})");
        GD.Print($"map-edit-fahrwerk-check: Laeufer auf rau {rau}: {(laeufer ? "gesetzt" : "ABGEWIESEN")} ({sl})");
        GD.Print($"map-edit-fahrwerk-check: Reifen auf Wasser {wasser2}: {(reifen ? "GESETZT" : "abgewiesen")} — {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}

/// <summary><c>--map-edit-spielen-check</c> — C9: »Spielen« im Bedienfeld druecken (Bildlauf:
/// danach muss der Editorschirm mit dem Fenster »Im Gefecht spielen« stehen).</summary>
public partial class MapEditOverlay
{
    private static bool _spielenGedrueckt;
    private async void SpielenCheck()
    {
        if (_spielenGedrueckt || Array.IndexOf(Core.CommandLine.Args, "--map-edit-spielen-check") < 0) return;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _spielenGedrueckt = true;
        PlayNow();
    }
}

/// <summary><c>--map-edit-wald-check</c> — 12.11.2026: ein Baum des Generators steht in der
/// Waldtafel; wegnehmen gibt die Zelle frei und setzt den Eintrag auf »weg«; ein gesetzter
/// Baum wird Wald; ein Rueckgaengig je Schritt bringt die Karte bytegleich zurueck.</summary>
public partial class MapEditOverlay
{
    private async void WaldCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-wald-check") < 0) return;
        for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        ulong Abdruck()
        {
            ulong h = 14695981039346656037UL;
            foreach (var a in _map.Sections) foreach (byte b in a) { h ^= b; h *= 1099511628211UL; }
            return h;
        }
        Vector2I? baum = null, frei = null;
        for (int r = 4; r < _map.Height - 4 && (baum == null || frei == null); r++)
            for (int c = 4; c < _map.Width - 4; c++)
            {
                int v = BelegungAt(c, r);
                if (baum == null && Import.MapForest.IstWald(v)) baum = new Vector2I(c, r);
                if (frei == null && v == MapFactory.ImapFree && _map.CodeAt(c, r) < CwpFile.ObjectCodeBase) frei = new Vector2I(c, r);
            }
        int waldVorher = Import.MapForest.Waldliste(_map).Count;
        ulong h0 = Abdruck();
        bool wegOk = false, setzOk = false, zurueck1 = false, zurueck2 = false;
        if (baum is { } b0)
        {
            _brush = Brush.Gegenstand; _pinsel = 1;
            StrichBeginnen(false); Anwenden(b0, false); StrichEnden();
            wegOk = BelegungAt(b0.X, b0.Y) == MapFactory.ImapFree && Import.MapForest.Waldliste(_map).Count == waldVorher - 1;
            GD.Print($"map-edit-wald-check: Baum {b0} weg -> Belegung {BelegungAt(b0.X, b0.Y):X4}, Wald {waldVorher} -> {Import.MapForest.Waldliste(_map).Count} — {_say}");
            Rueckgaengig(); zurueck1 = Abdruck() == h0;
        }
        int wi = -1;
        for (int i = 0; i < _props.Count; i++) if (_propInfo.TryGetValue(_props[i], out var pi) && pi.Art == "Wald") { wi = i; break; }
        if (frei is { } f0 && wi >= 0)
        {
            _brush = Brush.Gegenstand; _propAt = wi; _pinsel = 1;
            StrichBeginnen(true); Anwenden(f0, true); StrichEnden();
            setzOk = Import.MapForest.IstWald(BelegungAt(f0.X, f0.Y)) && Import.MapForest.Waldliste(_map).Count == waldVorher + 1;
            GD.Print($"map-edit-wald-check: Baum auf {f0} gesetzt -> Belegung {BelegungAt(f0.X, f0.Y)}, Wald {waldVorher} -> {Import.MapForest.Waldliste(_map).Count}");
            Rueckgaengig(); zurueck2 = Abdruck() == h0;
        }
        bool ok = wegOk && setzOk && zurueck1 && zurueck2;
        GD.Print($"map-edit-wald-check: wegnehmen {(wegOk ? "ok" : "FALSCH")}, setzen {(setzOk ? "ok" : "FALSCH")}, " +
                 $"Rueckgaengig {(zurueck1 && zurueck2 ? "bytegleich" : "ANDERS")} — {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}

/// <summary><c>--map-edit-sofort-check</c> — D4 (12.11.2026): ein Geroellstrich muss ohne F5
/// im Bodenbild ankommen (Bildpunkte der Zelle aendern sich), ein Rueckgaengig ebenso
/// (Bildpunkte wie vorher).</summary>
public partial class MapEditOverlay
{
    private async void SofortCheck()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-sofort-check") < 0) return;
        for (int i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_layer.GetParent() is not Rendering.MapViewer v || v.EditorBodenTextur == null
            || MapEditSession.Cwp == null || MapEditSession.Pal == null)
        { GD.Print("map-edit-sofort-check: DURCHGEFALLEN — kein Bodenbild"); GetTree().Quit(1); return; }
        // ⭐ 13.11.2026 bug-827: eine freie Zelle OHNE Wasser im Umkreis 4 — sonst darf
        // der Kettenring (Geroell braucht Gras zwischen sich und Sand/Wasser) nicht greifen,
        // und gestern lag die Probezelle (78,6) direkt am Ufer.
        bool Trocken(int c, int r)
        {
            for (int dy = -MapEditTerrain.Reach; dy <= MapEditTerrain.Reach; dy++)
                for (int dx = -MapEditTerrain.Reach; dx <= MapEditTerrain.Reach; dx++)
                    if (MapEditTerrain.ClassAt(_map, c + dx, r + dy) is 2 or 3) return false;
            return true;
        }
        Vector2I? z = null;
        for (int r = 8; r < _map.Height - 8 && z == null; r++)
            for (int c = 8; c < _map.Width - 8 && z == null; c++)
                if (MapEditTerrain.ClassAt(_map, c, r) == 0 && _map.FlagAt(c, r) == 0
                    && _map.CodeAt(c, r) < CwpFile.ObjectCodeBase && Trocken(c, r))
                    z = new Vector2I(c, r);
        if (z is not { } zz) { GD.Print("map-edit-sofort-check: DURCHGEFALLEN — keine freie Zelle"); GetTree().Quit(1); return; }
        var p0 = _layer.EditorCellTopLeft(zz.X, zz.Y);
        var rect = new Rect2I((int)p0.X, (int)p0.Y, MapBaker.TileW, MapBaker.TileH);

        // ⚠ Verglichen wird das GEBACKENE Bild, nicht die Textur: kopflos liefert
        // ImageTexture.GetImage() nach Update weiter das erste Bild (Dummy-Renderer).
        // Gemessen 13.11.: Codes 8 -> 255/216/657, Textur 0 Bildpunkte anders.
        var vorherBild = new MapBaker(KartenKopie(), MapEditSession.Cwp, MapEditSession.Pal).Bake();
        int Unterschied(Image a, Image b, out Rect2I bereich)
        {
            bereich = new Rect2I();
            if (a.GetWidth() != b.GetWidth() || a.GetHeight() != b.GetHeight()) return -1;
            var da = a.GetData(); var db = b.GetData();
            if (da.Length != db.Length) return -1;
            int n = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = 0, y1 = 0, w = a.GetWidth();
            int bpp = da.Length / (w * a.GetHeight());
            for (int i = 0; i + 2 < da.Length; i += bpp)
                if (da[i] != db[i] || da[i + 1] != db[i + 1] || da[i + 2] != db[i + 2])
                {
                    n++; int px = (i / bpp) % w, py = (i / bpp) / w;
                    x0 = Math.Min(x0, px); y0 = Math.Min(y0, py); x1 = Math.Max(x1, px); y1 = Math.Max(y1, py);
                }
            if (n > 0) bereich = new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            return n;
        }
        async System.Threading.Tasks.Task<bool> Warten(int soll)
        {
            for (int i = 0; i < 600 && SofortGetauscht < soll; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return SofortGetauscht >= soll;
        }
        var tc = _map.CodeAt(zz.X, zz.Y);
        _brush = Brush.Gelaende; _ground = MapFactory.Ground.Rough; _pinsel = 3;
        StrichBeginnen(true); Anwenden(zz, true); StrichEnden();
        int ring = MapEditTerrain.LastRingGeaendert, abgelehnt = MapEditTerrain.LastRingAbgelehnt;
        bool rau = MapEditTerrain.ClassAt(_map, zz.X, zz.Y) == 1;
        int codeNach = _map.CodeAt(zz.X, zz.Y);
        bool b1 = await Warten(1);
        var ber1 = new Rect2I();
        int diff1 = SofortLetzterBoden == null ? -1 : Unterschied(vorherBild, SofortLetzterBoden, out ber1);
        // streng: Bildpunkte IN der bemalten Zelle (vorher reichte, dass sich die Nachbarn
        // aenderten — genau so sah der Befund aus: Zelle gleich, Rand anders)
        int inZ = SofortLetzterBoden == null ? -1
            : Unterschied(vorherBild.GetRegion(rect), SofortLetzterBoden.GetRegion(rect), out _);
        bool inZelle = inZ > 0;
        GD.Print($"map-edit-sofort-check: Geroell 3x3 auf {zz} (Code {tc} -> {codeNach}, Klasse {(rau ? "rau" : "NICHT rau")}, " +
                 $"Kettenring {ring} Ecken, abgelehnt {abgelehnt}): Bild {(b1 ? "getauscht" : "NICHT getauscht")}, " +
                 $"{diff1} Bildpunkte anders{(diff1 > 0 ? $" in {ber1}" : "")}, in der Zelle {rect} {inZ} — {SofortZeile}");
        Rueckgaengig();
        bool b2 = await Warten(2);
        int diff2 = SofortLetzterBoden == null ? -1 : Unterschied(vorherBild, SofortLetzterBoden, out _);
        GD.Print($"map-edit-sofort-check: Rueckgaengig: Bild {(b2 ? "getauscht" : "NICHT getauscht")}, " +
                 $"{(diff2 == 0 ? "bytegleich wie vorher" : $"{diff2} Bildpunkte ANDERS")}");
        // ---- Probe 2: Geroell auf SAND — der Kettenring muss Gras dazwischenlegen ----
        bool sandOk = true;
        var tcm = MapEditSession.Tiles == null ? null : MapEditTerrain.EckModellFuer(_map);
        Vector2I? zs = null;
        if (tcm != null && tcm.IsChain)
            for (int r = 8; r < _map.Height - 8 && zs == null; r++)
                for (int c = 8; c < _map.Width - 8 && zs == null; c++)
                {
                    int code = _map.CodeAt(c, r);
                    if (MapEditTerrain.ClassAt(_map, c, r) != 0 || _map.FlagAt(c, r) != 0 || !tcm.Knows(code)) continue;
                    int l = tcm.Corner(code, 0);
                    if (l < 0 || tcm.Depth[l] != 1) continue;               // Stufe 1 = Sand (Wasser–Sand–…)
                    bool alle = true;
                    for (int k = 1; k < 4; k++) alle &= tcm.Corner(code, k) == l;
                    if (alle && Trocken(c, r)) zs = new Vector2I(c, r);
                }
        if (zs is { } s2)
        {
            int vor = _map.CodeAt(s2.X, s2.Y);
            _pinsel = 1;
            StrichBeginnen(true); Anwenden(s2, true); StrichEnden();
            int ring2 = MapEditTerrain.LastRingGeaendert, abg2 = MapEditTerrain.LastRingAbgelehnt;
            int nach = _map.CodeAt(s2.X, s2.Y);
            int ecken = 0, bruch = 0;
            for (int r = s2.Y - 1; r <= s2.Y + 1; r++)
                for (int c = s2.X - 1; c <= s2.X + 1; c++)
                {
                    int code = _map.CodeAt(c, r);
                    if (!tcm!.Knows(code)) continue;
                    int lo = 99, hi = -1;
                    for (int k = 0; k < 4; k++) { int d = tcm.Depth[tcm.Corner(code, k)]; lo = Math.Min(lo, d); hi = Math.Max(hi, d); }
                    ecken++; if (hi - lo > 1) bruch++;
                }
            bool geroell = tcm!.Knows(nach) && tcm.Depth[tcm.Corner(nach, 0)] == tcm.MaxDepth;
            sandOk = geroell && abg2 == 0 && ring2 > 0 && bruch == 0;
            GD.Print($"map-edit-sofort-check: Geroell auf Sand {s2} (Code {vor} -> {nach}, {(geroell ? "Geroellbild" : "KEIN Geroellbild")}): " +
                     $"Kettenring {ring2} Ecken, abgelehnt {abg2}, Kettenbrueche in 3x3 {bruch}/{ecken} — {(sandOk ? "ok" : "FALSCH")}");
            Rueckgaengig();
        }
        else GD.Print("map-edit-sofort-check: Geroell auf Sand — keine trockene Sandzelle, Probe entfaellt");
        bool ok = b1 && rau && inZelle && b2 && diff2 == 0 && sandOk;
        GD.Print($"map-edit-sofort-check: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
