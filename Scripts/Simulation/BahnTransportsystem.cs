namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ <b>Das Transportsystem — die Karte in Betriebsart 1</b> (22.09.2026,
/// berichte/bahnhof-transport-fable.md §3.2, Bauliste 5).
///
/// <para>Mausbewegung der Karte (Arm ab <c>0x447B3C</c>): im <b>5×5-Umfeld</b>
/// der Mauszelle das Linienbyte suchen; eine Linie gilt nur, wenn <b>beide
/// Endgebäude dem Betrachter gehören</b> (@0x447C02/0x447C23). Der Zeichner hebt
/// sie in Farbe 0x99 hervor; ein Klick öffnet Fensterart 4 für sie.</para>
///
/// <para>⚠ UNSER Weg zum Linienbyte: das Original liest
/// <c>byte[0x542E18 + 256·Spalte + Zeile]</c>; wir nehmen die Zellketten der
/// Linien (<see cref="_lineCell"/>), aus denen auch Gleis und Zug gezeichnet
/// werden. Welche Linie gewinnt, wenn zwei im Umfeld liegen, ist im Original
/// die Suchreihenfolge des Umfelds — wir nehmen die der Mauszelle nächste.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--transportsystem-aus</c> — der Knopf tut nichts (Stand vor dem
    /// 22.09.2026, damals gesperrt).</summary>
    public static bool TransportsystemAus;

    private Dictionary<Vector2I, int>? _linieJeZelle;

    private Dictionary<Vector2I, int> LinieJeZelle()
    {
        if (_linieJeZelle != null) return _linieJeZelle;
        var d = new Dictionary<Vector2I, int>();
        foreach (var kv in _lineCell)
            foreach (var c in kv.Value)
                d.TryAdd(new Vector2I(Mathf.RoundToInt(c.X), Mathf.RoundToInt(c.Y)), kv.Key);
        return _linieJeZelle = d;
    }

    /// <summary>Die Linie unter der Maus (5×5), deren beide Enden dem Betrachter
    /// gehören — oder -1.</summary>
    public int TransportsystemLinieBei(int c, int r)
    {
        var d = LinieJeZelle();
        int best = -1, bestD = int.MaxValue;
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
        {
            if (!d.TryGetValue(new Vector2I(c + dx, r + dy), out int linie)) continue;
            var (b1, b2) = RailEndsOf(linie);
            var a = RailBuilding(b1); var b = RailBuilding(b2);
            if (a == null || b == null || a.Dead || b.Dead) continue;
            if (a.Owner != ViewPlayer || b.Owner != ViewPlayer) continue;   // 0x447C02/0x447C23
            int dd = dx * dx + dy * dy;
            if (dd < bestD) { bestD = dd; best = linie; }
        }
        return best;
    }

    /// <summary>Die Zellen einer Linie (für die Hervorhebung), oder null.</summary>
    public List<Vector2>? LinienZellen(int linie)
        => linie >= 0 && _lineCell.TryGetValue(linie, out var z) ? z : null;

    /// <summary>Die Namen der Gebäude an Knoten 1 und 2 der Linie (für Art 4).</summary>
    public (string N1, string N2) LinienNamen(int linie)
    {
        var (b1, b2) = RailEndsOf(linie);
        string N(int slot) => RailBuilding(slot) is { } e
            ? (e.Name.Length == 0 || PlatzhalterName(e) ? BuildingTypeName(e.BType) : e.Name) : "?";
        return (N(b1), N(b2));
    }

    // GebaeudeAufZelle(c, r) steht schon in MapObjects.cs (dieselbe Rechnung).
}
