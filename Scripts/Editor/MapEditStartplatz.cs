namespace AkteEuropaReborn.Editor;

using System;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// ⭐ 12.11.2026 — P6/D5 (Fable-Pruefung): DAS STARTPLATZ-WERKZEUG (Taste 9). Ein Klick
/// setzt fuer den gewaehlten Spieler das ganze Paket wie der Generator: Basis, rechts
/// daneben das Werk, darunter die Truppe (6 Fahrzeuge). Hat der Spieler schon einen
/// Startplatz (seine erste Basis, das naechste Werk daneben, seine Einheiten darunter),
/// wird der vorher weggenommen — so »verschiebt« man ihn. Alles ein Rueckgaengig-Schritt;
/// passt das Paket nicht, wird der Schritt ganz zurueckgenommen. UNSERE ZUTAT.
/// </summary>
public partial class MapEditOverlay
{
    private const int StartTruppe = 6;

    private (int W, int H) StartplatzMass()
    {
        var (bw, bh) = _layer.EditorFootprint(MapGenerator.BaseType);
        var (fw, fh) = _layer.EditorFootprint(MapGenerator.FactoryType);
        return (bw + 1 + fw, Math.Max(bh, fh) + 3);
    }

    private void PutStartplatz(Vector2I cell)
    {
        if (_owner is < 0 or > 7) { _say = "ein Startplatz gehört einem Spieler (Sp 1..8)"; return; }
        int p = _owner;
        int ui = Array.FindIndex(MapEditUnits.Liste, u => u.Type == MapGenerator.TroopUnitType);
        if (ui < 0) ui = 0;

        // 1. den alten Startplatz dieses Spielers wegnehmen
        int weg = StartplatzWegnehmen(p);

        // 2. das neue Paket ueber dieselben Wege wie die Einzelwerkzeuge
        int typ0 = _typ, unit0 = _unitAt;
        string Fehler(string was) => $"Startplatz passt hier nicht ({was}: {_say}) — nichts geändert";
        try
        {
            var (bw, bh) = _layer.EditorFootprint(MapGenerator.BaseType);
            _say = ""; _typ = MapGenerator.BaseType; PutBuilding(cell);
            if (!_say.StartsWith("Typ ")) { Zuruecknehmen(Fehler("Basis")); return; }
            _say = ""; _typ = MapGenerator.FactoryType; PutBuilding(new Vector2I(cell.X + bw + 1, cell.Y));
            if (!_say.StartsWith("Typ ")) { Zuruecknehmen(Fehler("Werk")); return; }
            var (sw, _) = StartplatzMass();
            int gesetzt = 0;
            _unitAt = ui;
            for (int k = 0; k < sw * 3 && gesetzt < StartTruppe; k++)
            {
                var z = new Vector2I(cell.X + k % sw, cell.Y + bh + 1 + k / sw);
                if (z.X >= _map.Width || z.Y >= _map.Height) continue;
                _say = "";
                PutUnit(z);
                if (_say.Contains(" auf (")) gesetzt++;
            }
            if (gesetzt == 0) { Zuruecknehmen("Startplatz: keine Zelle für die Truppe frei — nichts geändert"); return; }
            _say = $"Startplatz Spieler {p + 1} auf ({cell.X},{cell.Y}): Basis, Werk, {gesetzt} Fahrzeuge" +
                   (weg > 0 ? $" — der alte Startplatz ist weg ({weg} Teile)" : "");
        }
        finally { _typ = typ0; _unitAt = unit0; }
    }

    /// <summary>D5 (12.11.2026): die SPIELERZAHL der Karte — wer eine Basis hat. Sie aendert
    /// sich mit diesem Werkzeug: einen freien Spieler waehlen und klicken = ein Spieler mehr,
    /// Rechtsklick = einer weniger. Bisher stand das nirgends.</summary>
    private string SpielerZeile()
    {
        var mit = new System.Collections.Generic.SortedSet<int>();
        foreach (var g in GebaeudeSaetze().Values)
            if (g.Typ == MapGenerator.BaseType && g.Owner is >= 0 and <= 7) mit.Add(g.Owner);
        string wer = mit.Count == 0 ? "keiner" : string.Join(", ", System.Linq.Enumerable.Select(mit, p => $"Sp {p + 1}"));
        int frei = -1;
        for (int p = 0; p < 8 && frei < 0; p++) if (!mit.Contains(p)) frei = p;
        return $"Startplatz — die Karte hat {mit.Count} Spieler ({wer}). " +
               (frei >= 0 ? $"Mehr Spieler: Sp {frei + 1} wählen und klicken. " : "") +
               "Weniger: Rechtsklick nimmt den Startplatz des gewählten Spielers weg.";
    }

    /// <summary>Den Schritt, den StrichBeginnen gemerkt hat, ganz zuruecknehmen.</summary>
    private void Zuruecknehmen(string grund)
    {
        Rueckgaengig();
        if (_vor.Count > 0) _vor.RemoveAt(_vor.Count - 1);   // kein »Wiederholen« eines Fehlschlags
        _say = grund;
    }

    /// <summary>Erste Basis des Spielers, das naechste Werk (hoechstens 16 Zellen), seine
    /// Einheiten im Rechteck darunter. Rueckgabe: wie viele Teile weggenommen wurden.</summary>
    private int StartplatzWegnehmen(int p)
    {
        var geb = GebaeudeSaetze();
        int basis = -1;
        foreach (var (slot, g) in geb)
            if (g.Owner == p && g.Typ == MapGenerator.BaseType && (basis < 0 || slot < basis)) basis = slot;
        if (basis < 0) return 0;
        var b = geb[basis];
        int werk = -1; double wd = 17;
        foreach (var (slot, g) in geb)
            if (g.Owner == p && g.Typ == MapGenerator.FactoryType)
            {
                double d = Math.Sqrt(Math.Pow(g.Col - b.Col, 2) + Math.Pow(g.Row - b.Row, 2));
                if (d < wd) { wd = d; werk = slot; }
            }
        int n = 0;
        foreach (int slot in new[] { basis, werk })
        {
            if (slot < 0) continue;
            if (MapFactory.ClearBuilding(_map, slot)) { _layer.EditorRemoveBuilding(slot); n++; }
        }
        var (sw, sh) = StartplatzMass();
        var (_, bh) = _layer.EditorFootprint(MapGenerator.BaseType);
        var s = _map.Sec(5);
        if (s == null) return n;
        for (int o = 0; o + CwmData.EntityStride <= s.Length; o += CwmData.EntityStride)
        {
            if (s[o + 0x09] == 0xFF) continue;
            if (o / CwmData.EntityStride / CwmData.EntitiesPerPlayer != p) continue;
            int c = s[o], r = s[o + 1];
            // die Truppe steht unter der Platte — beim Generator ab Zeile Basis+5, auf
            // gespiegelten Platten links der Basis; darum ein grosszuegiges Feld
            if (c < b.Col - (sw - 6) || c >= b.Col + sw || r < b.Row + 3 || r > b.Row + sh + 2) continue;
            if (MapFactory.ClearEntityAt(_map, c, r) >= 0) { _gone.Add((c, r)); n++; }
        }
        return n;
    }
}
