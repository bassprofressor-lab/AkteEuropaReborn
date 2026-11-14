namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;

// ============================================================================
// ⭐ 14.11.2026 — bug-832, zweite Haelfte: DER HAFEN IM GEBAEUDEPINSEL.
//
// Der Generator setzt Hafen (11) und Werft (16) seit 410e8ae als Paar (MapNeutrals). Der Pinsel
// pruefte den Hafen noch wie jedes Gebaeude (frei + sec2 >= 2, also LAND) und schrieb cisTyp 0
// ohne sec29 — der Hafen stand an Land und zeigte auf Platz 0 (die Basis).
//
// GEMESSEN (scratchpad hafen.py, 13.11.): Hafen 36x auf den Originalen, Grundflaeche 90..100 %
// Wasser; Werft 37x, 0 % Wasser; paarweise, gleicher Eigner, Abstand 6..15 Zellen; sec29 (4 B je
// Hafen, Index = cisTyp +0x19): +0x00 der Hafen, +0x02 die Werft.
//
// UNSERE SETZUNGEN:
//  * jede Musterzelle des Hafens muss Wasser (Klasse 2) sein — daran erkennt man auch, dass dort
//    nichts steht (ein Gebaeude traegt seinen Griff statt 0xFFFC in der imap);
//  * verknuepft wird mit der NAECHSTEN Werft, die noch keinen Hafen hat, hoechstens 15 Zellen
//    entfernt (Ankerabstand, wie gemessen). Ohne eine solche Werft wird abgewiesen;
//  * der Hafen bekommt den Eigner seiner Werft (das Original reicht ihn bei der Einnahme mit der
//    Werft weiter, Capture.cs 0x43CEEC);
//  * eine Werft wegnehmen nimmt ihren Hafen mit (Strg+Z holt beide zurueck), einen Hafen
//    wegnehmen leert seinen sec29-Satz.
// Gegenschalter --editor-hafen-pinsel-alt (der alte Weg: Hafen wie jedes Gebaeude an Land).
// ============================================================================
public partial class MapEditOverlay
{
    public static bool HafenPinselAlt => Array.IndexOf(Core.CommandLine.Args, "--editor-hafen-pinsel-alt") >= 0;

    /// <summary>Hoechster Ankerabstand Hafen–Werft (gemessen 6..15).</summary>
    public const int HafenWerftMax = 15;

    /// <summary>Den Hafen setzen. Rueckgabe false: kein Hafen gesetzt, <c>_say</c> sagt warum.</summary>
    private bool HafenSetzen(Vector2I cell, CwpFile cwp, List<(int Dx, int Dy)> foot, int fw, int fh)
    {
        foreach (var (dx, dy) in foot)
        {
            int c = cell.X + dx, r = cell.Y + dy;
            if (MapEditTerrain.ClassAt(_map, c, r) != 2)
            { _say = $"({c},{r}) ist kein freies Wasser — der Hafen steht ganz im Wasser, die Werft an Land"; return false; }
            if (MapFactory.Belegt(_map, c, r) is { } belegt) { _say = belegt; return false; }
        }

        var werft = NaechsteFreieWerft(cell.X, cell.Y);
        if (werft is not { } w)
        { _say = $"keine Werft ohne Hafen in {HafenWerftMax} Zellen — erst eine Werft an Land ans Ufer setzen"; return false; }

        int idx = MapNeutrals.NaechsterHafenIndex(_map);
        if (idx < 0) { _say = "die Karte traegt schon 50 Häfen — mehr kennt das Original nicht"; return false; }
        _nextSlot = NextBuildingSlot();
        if (_nextSlot < 0) { _say = "kein Gebaeudeplatz mehr frei (255)"; return false; }

        int hp = MapNeutrals.HpOf(MapNeutrals.HafenTyp);
        if (!MapFactory.PutBuilding(_map, _nextSlot, MapNeutrals.HafenTyp, w.Owner, idx, cell.X, cell.Y,
                                    fw, fh, hp, "", MapNeutrals.DoorsOf(MapNeutrals.HafenTyp), cwp: cwp))
        { _say = "kein Gebaeudeplatz mehr frei (255)"; return false; }
        MapNeutrals.HafenVerknuepfen(_map, idx, _nextSlot, w.Slot);

        _layer.EditorAddBuilding(_nextSlot, MapNeutrals.HafenTyp, w.Owner, cell.X, cell.Y, fw, fh, hp);
        _mine.Add((cell.X, cell.Y, Brush.Gebaeude));
        MapEditSession.Dirty = true;
        _say = $"Hafen auf ({cell.X},{cell.Y}), {fw}x{fh}, zur Werft auf Platz {w.Slot} ({w.Col},{w.Row}), " +
               $"{OwnerName(w.Owner)}" + (w.Owner != _owner ? " (Eigner der Werft)" : "");
        _nextSlot++;
        return true;
    }

    /// <summary>Die naechste Werft (16) ohne Hafen, Ankerabstand hoechstens <see cref="HafenWerftMax"/>.</summary>
    private (int Slot, int Col, int Row, int Owner)? NaechsteFreieWerft(int col, int row)
    {
        var s3 = _map.Sec(3);
        if (s3 == null) return null;
        var vergeben = WerftenMitHafen();
        (int, int, int, int)? best = null;
        int bestD = int.MaxValue;
        for (int slot = 0; (slot + 1) * CwmData.BuildingStride <= s3.Length; slot++)
        {
            int o = slot * CwmData.BuildingStride;
            if (s3[o + 0x18] != 1 || s3[o + 0x04] != MapNeutrals.WerftTyp || vergeben.Contains(slot)) continue;
            int c = s3[o] | (s3[o + 1] << 8), r = s3[o + 2] | (s3[o + 3] << 8);
            int d = (c - col) * (c - col) + (r - row) * (r - row);
            if (d > HafenWerftMax * HafenWerftMax || d >= bestD) continue;
            bestD = d; best = (slot, c, r, s3[o + 0x05]);
        }
        return best;
    }

    /// <summary>Werftplaetze, auf die ein stehender Hafen ueber sec29 +0x02 zeigt.</summary>
    private HashSet<int> WerftenMitHafen()
    {
        var res = new HashSet<int>();
        foreach (var (_, werft, _) in Haefen()) res.Add(werft);
        return res;
    }

    /// <summary>Alle stehenden Haefen: (Platz, Werftplatz aus sec29, sec29-Index).</summary>
    private List<(int Slot, int Werft, int Idx)> Haefen()
    {
        var res = new List<(int, int, int)>();
        var s3 = _map.Sec(3);
        var s29 = _map.Sec(29);
        if (s3 == null || s29 == null) return res;
        for (int slot = 0; (slot + 1) * CwmData.BuildingStride <= s3.Length; slot++)
        {
            int o = slot * CwmData.BuildingStride;
            if (s3[o + 0x18] != 1 || s3[o + 0x04] != MapNeutrals.HafenTyp) continue;
            int idx = s3[o + 0x19];
            if ((idx + 1) * 4 > s29.Length) continue;
            res.Add((slot, s29[idx * 4 + 2] | (s29[idx * 4 + 3] << 8), idx));
        }
        return res;
    }

    /// <summary>Vor dem Wegnehmen von Platz <paramref name="slot"/>: ein Hafen leert seinen
    /// sec29-Satz, eine Werft nimmt ihren Hafen mit. Rueckgabe: Zusatz fuer die Statuszeile.</summary>
    private string HafenPaarLoesen(int slot)
    {
        if (HafenPinselAlt) return "";
        var s3 = _map.Sec(3);
        var s29 = _map.Sec(29);
        if (s3 == null || s29 == null || (slot + 1) * CwmData.BuildingStride > s3.Length) return "";
        int typ = s3[slot * CwmData.BuildingStride + 0x04];
        string zusatz = "";
        foreach (var (h, werft, idx) in Haefen())
        {
            if (typ == MapNeutrals.HafenTyp && h == slot)
                for (int k = 0; k < 4; k++) s29[idx * 4 + k] = 0;
            else if (typ == MapNeutrals.WerftTyp && werft == slot)
            {
                for (int k = 0; k < 4; k++) s29[idx * 4 + k] = 0;
                int ho = h * CwmData.BuildingStride;
                int hc = s3[ho] | (s3[ho + 1] << 8), hr = s3[ho + 2] | (s3[ho + 3] << 8);
                if (MapFactory.ClearBuilding(_map, h))
                {
                    _layer.EditorRemoveBuilding(h);
                    _mine.RemoveAll(x => x.What == Brush.Gebaeude && x.Col == hc && x.Row == hr);
                    zusatz = $", ihr Hafen (Platz {h}) mit";
                }
            }
        }
        return zusatz;
    }
}
