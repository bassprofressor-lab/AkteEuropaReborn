namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;
using AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 12.11.2026 — P5 (Fable-Pruefung D2/D3/D7): MALEN WIE GEWOHNT.
///
/// <list type="bullet">
///   <item><b>Ziehen</b> mit gedrueckter Maustaste fuer Bodenart, Hoehe, Wald/Fels,
///     Gleise und Wegnehmen — jede Zelle einmal je Strich. Gebaeude und Einheiten
///     bleiben ein Klick je Stueck.</item>
///   <item><b>Pinselgroesse</b> 1, 3 oder 5 (Tasten <c>[</c> <c>]</c> oder die Knoepfe)
///     fuer Bodenart, Hoehe, Wald/Fels und Wegnehmen.</item>
///   <item><b>Rueckgaengig / Wiederholen</b> (Strg+Z / Strg+Y): je Strich ein
///     Schnappschuss der Karte im Speicher (alle Abschnitte, Vorkommen, Kreuze).
///     Gebaeude werden in der Ansicht sofort nachgezogen; Gelaende war schon
///     vorher erst nach F5 im Bild — das gilt auch fuers Rueckgaengig.</item>
///   <item><b>Vorkommen</b> (Taste 8): links setzt ein Terranium-Vorkommen, rechts
///     nimmt das naechste im Umkreis von 3 Zellen weg. Alle Vorkommen stehen als
///     Raute im Bild.</item>
/// </list>
/// Alles UNSERE ZUTAT; die Zahlen (30 Schritte, Ring 3, Abstand 8) sind UNSERE SETZUNG.
/// </summary>
public partial class MapEditOverlay
{
    private int _pinsel = 1;
    private bool _strichLinks, _strichRechts;
    private readonly HashSet<(int, int)> _strichZellen = new();

    private sealed class Stand
    {
        public List<byte[]> Abschnitte = new();
        public List<(int Col, int Row, int Amount)> Terra = new();
        public List<(int Col, int Row)> Gone = new();
        public List<(int Col, int Row, Brush What)> Mine = new();
    }

    private readonly List<Stand> _zurueck = new();
    private readonly List<Stand> _vor = new();
    private const int StaendeMax = 30;

    private static bool Ziehbar(Brush b) => b is Brush.Gelaende or Brush.Hoehe or Brush.Gegenstand
                                                 or Brush.Gleis or Brush.Wegnehmen;
    private static bool Flaechig(Brush b) => b is Brush.Gelaende or Brush.Hoehe or Brush.Gegenstand
                                                  or Brush.Wegnehmen;

    private Stand Festhalten()
    {
        var s = new Stand();
        foreach (var a in _map.Sections) s.Abschnitte.Add((byte[])a.Clone());
        s.Terra.AddRange(_map.Terra);
        s.Gone.AddRange(_gone);
        s.Mine.AddRange(_mine);
        return s;
    }

    /// <summary>Beim Druecken der Maustaste: Stand merken, Strich beginnen.</summary>
    private void StrichBeginnen(bool links)
    {
        _zurueck.Add(Festhalten());
        if (_zurueck.Count > StaendeMax) _zurueck.RemoveAt(0);
        _vor.Clear();
        _strichZellen.Clear();
        _strichLinks = links;
        _strichRechts = !links;
    }

    private void StrichEnden()
    {
        bool warStrich = _strichLinks || _strichRechts;
        _strichLinks = _strichRechts = false;
        _strichZellen.Clear();
        if (warStrich && Bildwirksam(_brush)) SofortbildPlanen();   // D4
    }

    /// <summary>Den Pinsel an dieser Zelle anwenden — mit Groesse, jede Zelle einmal je Strich.</summary>
    private void Anwenden(Vector2I mitte, bool links)
    {
        int r = Flaechig(_brush) ? _pinsel / 2 : 0;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var c = new Vector2I(mitte.X + dx, mitte.Y + dy);
                if (c.X < 0 || c.Y < 0 || c.X >= _map.Width || c.Y >= _map.Height) continue;
                if (!_strichZellen.Add((c.X, c.Y))) continue;
                string say0 = "";
                if (links) Put(c); else Take(c);
                say0 = _say;
                // ⭐ 12.11.2026 D6: SPIEGELN BEIM MALEN — dieselbe Zelle, um die Kartenmitte
                // gedreht (180°, wie der Generator symmetrische Karten baut, P6). Nur fuer
                // Gelaende, Hoehe, Wald/Fels und Gleise: Gebaeude, Einheiten, Startplaetze
                // und Vorkommen haengen an Spielern und Grundflaechen — die spiegelt das
                // Werkzeug nicht (UNSERE SETZUNG, steht in der Leiste).
                if (_spiegeln && Spiegelbar(_brush))
                {
                    var s = new Vector2I(_map.Width - 1 - c.X, _map.Height - 1 - c.Y);
                    if (_strichZellen.Add((s.X, s.Y)))
                    {
                        if (links) Put(s); else Take(s);
                        _say = say0 + "  (gespiegelt)";
                    }
                }
            }
    }

    /// <summary>D6: Spiegeln beim Malen an/aus (Taste M).</summary>
    private bool _spiegeln;
    private static bool Spiegelbar(Brush b) => b is Brush.Gelaende or Brush.Hoehe or Brush.Gegenstand or Brush.Gleis;

    /// <summary>Beim Ziehen: neue Zelle unter der Maus?</summary>
    private void Ziehen(Vector2I? zelle)
    {
        if (zelle is not { } z || !Ziehbar(_brush)) return;
        if (_strichLinks && Input.IsMouseButtonPressed(MouseButton.Left)) Anwenden(z, true);
        else if (_strichRechts && Input.IsMouseButtonPressed(MouseButton.Right)) Anwenden(z, false);
        else StrichEnden();
    }

    private void PinselGroesse(int d)
    {
        int[] g = { 1, 3, 5 };
        int i = Array.IndexOf(g, _pinsel);
        _pinsel = g[Math.Clamp(i + d, 0, g.Length - 1)];
        _say = $"Pinselgröße {_pinsel}x{_pinsel}";
    }

    // ---- Rueckgaengig ----------------------------------------------------------

    private void Rueckgaengig() { Springen(_zurueck, _vor, "rückgängig"); SofortbildPlanen(); }   // D4
    private void Wiederholen() { Springen(_vor, _zurueck, "wiederholt"); SofortbildPlanen(); }

    private void Springen(List<Stand> von, List<Stand> nach, string wort)
    {
        if (von.Count == 0) { _say = wort == "rückgängig" ? "nichts mehr rückgängig zu machen" : "nichts zu wiederholen"; return; }
        var ziel = von[^1];
        von.RemoveAt(von.Count - 1);
        nach.Add(Festhalten());

        var gebVorher = GebaeudeSaetze();
        var einhVorher = EinheitenZellen();
        for (int i = 0; i < _map.Sections.Count && i < ziel.Abschnitte.Count; i++)
        {
            var a = _map.Sections[i];
            var b = ziel.Abschnitte[i];
            if (a.Length == b.Length) Buffer.BlockCopy(b, 0, a, 0, a.Length);
            else _map.Sections[i] = (byte[])b.Clone();
        }
        _map.Terra.Clear(); _map.Terra.AddRange(ziel.Terra);
        _gone.Clear(); _gone.AddRange(ziel.Gone);
        _mine.Clear(); _mine.AddRange(ziel.Mine);

        // die Ansicht: Gebaeude stehen lebend in der Liste — nachziehen
        var gebNachher = GebaeudeSaetze();
        foreach (var (slot, alt) in gebVorher)
            if (!gebNachher.TryGetValue(slot, out var neu) || neu != alt) _layer.EditorRemoveBuilding(slot);
        foreach (var (slot, neu) in gebNachher)
            if (!gebVorher.TryGetValue(slot, out var alt) || alt != neu)
            {
                var (fw, fh) = _layer.EditorFootprint(neu.Typ);
                _layer.EditorAddBuilding(slot, neu.Typ, neu.Owner, neu.Col, neu.Row, fw, fh, neu.Hp);
            }
        // Einheiten, die durch den Sprung verschwinden, bekommen das Kreuz (wie Wegnehmen)
        var einhNachher = EinheitenZellen();
        foreach (var z in einhVorher)
            if (!einhNachher.Contains(z) && !_gone.Contains(z)) _gone.Add(z);

        MapEditSession.Dirty = true;
        _say = $"{wort} — Gebäude sofort, Gelände/Objekte/Gleise sichtbar nach F5 " +
               $"(noch {_zurueck.Count} Schritte zurück, {_vor.Count} vor)";
        Refresh();
        QueueRedraw();
    }

    private Dictionary<int, (int Typ, int Owner, int Col, int Row, int Hp)> GebaeudeSaetze()
    {
        var d = new Dictionary<int, (int, int, int, int, int)>();
        var s = _map.Sec(3);
        if (s == null) return d;
        for (int i = 0; i < 255; i++)
        {
            int o = i * CwmData.BuildingStride;
            if (o + CwmData.BuildingStride > s.Length) break;
            if (s[o + 0x18] != 1) continue;
            d[i] = (s[o + 4], s[o + 5], s[o] | (s[o + 1] << 8), s[o + 2] | (s[o + 3] << 8),
                    s[o + 6] | (s[o + 7] << 8));
        }
        return d;
    }

    private HashSet<(int Col, int Row)> EinheitenZellen()
    {
        var h = new HashSet<(int, int)>();
        var s = _map.Sec(5);
        if (s == null) return h;
        for (int o = 0; o + CwmData.EntityStride <= s.Length; o += CwmData.EntityStride)
            if (s[o + 0x09] != 0xFF) h.Add((s[o], s[o + 1]));
        return h;
    }

    // ---- Vorkommen -------------------------------------------------------------

    private const int VorkommenAbstand = 8, VorkommenFang = 3;

    private void PutVorkommen(Vector2I c)
    {
        if (MapFactory.Belegt(_map, c.X, c.Y) is { } belegt) { _say = belegt; return; }
        if (MapEditTerrain.ClassAt(_map, c.X, c.Y) is 2 or 3)
        { _say = $"({c.X},{c.Y}): ein Vorkommen gehört auf begehbares Land"; return; }
        foreach (var (tc, tr, _) in _map.Terra)
        {
            int dx = tc - c.X, dy = tr - c.Y;
            if (dx * dx + dy * dy < VorkommenAbstand * VorkommenAbstand)
            { _say = $"zu nah am Vorkommen ({tc},{tr}) — mindestens {VorkommenAbstand} Zellen Abstand"; return; }
        }
        _map.Terra.Add((c.X, c.Y, MapDeposits.Amount));
        MapEditSession.Dirty = true;
        _say = $"Vorkommen ({MapDeposits.Amount} Terranium) auf ({c.X},{c.Y}) — {_map.Terra.Count} auf der Karte";
    }

    private void TakeVorkommen(Vector2I c)
    {
        int best = -1, bd = int.MaxValue;
        for (int i = 0; i < _map.Terra.Count; i++)
        {
            int dx = _map.Terra[i].Col - c.X, dy = _map.Terra[i].Row - c.Y, d = dx * dx + dy * dy;
            if (d <= VorkommenFang * VorkommenFang && d < bd) { bd = d; best = i; }
        }
        if (best < 0) { _say = "kein Vorkommen in der Naehe"; return; }
        var t = _map.Terra[best];
        _map.Terra.RemoveAt(best);
        MapEditSession.Dirty = true;
        _say = $"Vorkommen ({t.Col},{t.Row}) weg — {_map.Terra.Count} auf der Karte";
    }

    /// <summary>Alle Vorkommen als Raute (vorher unsichtbar im Editor).</summary>
    private void VorkommenZeichnen()
    {
        var gelb = new Color(1f, 0.75f, 0.1f, 0.95f);
        foreach (var (c, r, _) in _map.Terra)
        {
            if (c < 0 || r < 0 || c >= _map.Width || r >= _map.Height) continue;
            var p = _layer.EditorCellTopLeft(c, r) + new Vector2(MapBaker.TileW / 2f, MapBaker.TileH / 2f);
            float s = _brush == Brush.Vorkommen ? 14f : 9f;
            var pts = new[] { p + new Vector2(0, -s), p + new Vector2(s, 0), p + new Vector2(0, s), p + new Vector2(-s, 0) };
            DrawColoredPolygon(pts, new Color(gelb.R, gelb.G, gelb.B, 0.45f));
            DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, gelb, 2f);
        }
    }
}

public partial class MapEditOverlay
{
    /// <summary><c>--map-edit-p5-check</c> — der Pruefstand fuer P5: geht dieselben Wege wie
    /// die Maus (StrichBeginnen/Anwenden/StrichEnden) und prueft, dass Rueckgaengig die Karte
    /// BYTEGLEICH zurueckbringt und Wiederholen den Stand danach.</summary>
    private void P5Check()
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--map-edit-p5-check") < 0) return;
        var sb = new System.Text.StringBuilder("map-edit-p5-check\n");
        ulong Abdruck()
        {
            ulong h = 14695981039346656037UL;
            foreach (var a in _map.Sections) foreach (byte b in a) { h ^= b; h *= 1099511628211UL; }
            foreach (var (c, r, n) in _map.Terra) { h ^= (ulong)(c * 7919 + r * 31 + n); h *= 1099511628211UL; }
            return h;
        }
        bool FreiFeld(int c0, int r0, int n)
        {
            for (int dy = 0; dy < n; dy++)
                for (int dx = 0; dx < n; dx++)
                {
                    int c = c0 + dx, r = r0 + dy;
                    if (c >= _map.Width || r >= _map.Height) return false;
                    if (MapEditTerrain.ClassAt(_map, c, r) != 0 || MapFactory.Belegt(_map, c, r) != null) return false;
                }
            return true;
        }
        (int C, int R) Suche(int n, int abC, int abR)
        {
            for (int r = abR; r < _map.Height - n; r++)
                for (int c = abC; c < _map.Width - n; c++)
                    if (FreiFeld(c, r, n)) return (c, r);
            return (-1, -1);
        }
        ulong h0 = Abdruck();
        int terra0 = _map.Terra.Count, geb0 = GebaeudeSaetze().Count;

        // 1. Bodenart rau mit Pinsel 3
        var (fc, fr) = Suche(8, 4, 4);
        if (fc < 0) { GD.Print(sb + "KEIN URTEIL: kein freies 8x8-Feld"); GetTree().Quit(2); return; }
        _brush = Brush.Gelaende; _ground = MapFactory.Ground.Rough; _pinsel = 3;
        StrichBeginnen(true); Anwenden(new Vector2I(fc + 1, fr + 1), true); StrichEnden();
        int rau = 0;
        for (int dy = 0; dy < 3; dy++) for (int dx = 0; dx < 3; dx++)
            if (MapEditTerrain.ClassAt(_map, fc + dx, fr + dy) == 1) rau++;
        sb.AppendLine($"  Pinsel 3x3 Bodenart rau: {rau}/9 Zellen ({_say})");

        // 2. Ziehen: ein Strich ueber drei Zellen mit Pinsel 1 (Hoehe +1)
        _brush = Brush.Hoehe; _pinsel = 1;
        StrichBeginnen(true);
        for (int i = 0; i < 3; i++) Anwenden(new Vector2I(fc + 5, fr + i), true);
        Anwenden(new Vector2I(fc + 5, fr), true);    // dieselbe Zelle im selben Strich: nicht doppelt
        StrichEnden();
        sb.AppendLine($"  Ziehen Höhe: Strich über 3 Zellen, Zellen im Strich {_strichZellen.Count} (nach Ende 0) — {_say}");

        // 3. ein Gebaeude
        _brush = Brush.Gebaeude;
        int geb = 0;
        for (int r = 2; r < _map.Height - 8 && geb == 0; r++)
            for (int c = 2; c < _map.Width - 12 && geb == 0; c++)
            {
                _say = "";
                StrichBeginnen(true); Anwenden(new Vector2I(c, r), true); StrichEnden();
                if (_say.StartsWith("Typ ")) geb = 1; else _zurueck.RemoveAt(_zurueck.Count - 1);
            }
        sb.AppendLine($"  Gebäude gesetzt: {geb} — Saetze {geb0} -> {GebaeudeSaetze().Count}");

        // 4. ein Vorkommen, weit weg von den vorhandenen
        _brush = Brush.Vorkommen;
        int vk = 0;
        for (int r = 4; r < _map.Height - 4 && vk == 0; r += 3)
            for (int c = 4; c < _map.Width - 4 && vk == 0; c += 3)
            {
                int vor = _map.Terra.Count;
                StrichBeginnen(true); Anwenden(new Vector2I(c, r), true); StrichEnden();
                if (_map.Terra.Count > vor) vk = 1; else _zurueck.RemoveAt(_zurueck.Count - 1);
            }
        sb.AppendLine($"  Vorkommen gesetzt: {vk} — {terra0} -> {_map.Terra.Count}");

        ulong h1 = Abdruck();
        int schritte = _zurueck.Count;
        for (int i = 0; i < schritte; i++) Rueckgaengig();
        ulong hz = Abdruck();
        bool zurueckOk = hz == h0 && _map.Terra.Count == terra0 && GebaeudeSaetze().Count == geb0;
        sb.AppendLine($"  {schritte}x Rückgängig: Karte {(hz == h0 ? "BYTEGLEICH zum Anfang" : "ANDERS")}, " +
                      $"Vorkommen {_map.Terra.Count}/{terra0}, Gebäude {GebaeudeSaetze().Count}/{geb0}");
        for (int i = 0; i < schritte; i++) Wiederholen();
        ulong hv = Abdruck();
        sb.AppendLine($"  {schritte}x Wiederholen: Karte {(hv == h1 ? "BYTEGLEICH zum Stand danach" : "ANDERS")}");
        bool ok = rau == 9 && geb == 1 && vk == 1 && zurueckOk && hv == h1 && hz != h1;
        sb.Append(ok ? "map-edit-p5-check: BESTANDEN" : "map-edit-p5-check: DURCHGEFALLEN");
        GD.Print(sb.ToString());
        GetTree().Quit(ok ? 0 : 1);
    }
}
