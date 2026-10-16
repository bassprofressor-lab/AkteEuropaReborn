namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <c>--bertha-demo-check=N</c> (bug-554, KayelGee 14.10., Meldung 15): die Sonde fuer
/// UI/BerthaDemoCheck.cs — Abstand der Feinde zur Bertha je Sekunde.
/// </summary>
public partial class MapEntityLayer
{
    // =====================================================================
    //  --bertha-demo-check=N (Meldung 15): Menue-Demo N, wer laeuft von der Bertha weg?
    // =====================================================================
    private readonly Dictionary<int, (float D0, float DMin, float DMax, string Zug0)> _bdStart = new();
    private readonly Dictionary<int, List<string>> _bdSpur = new();

    private string BdZug(Entity e)
    {
        string ziel = e.Target >= 0 && e.Target < _entities.Count
            ? $"Ziel Platz {_entities[e.Target].Slot}{(_entities[e.Target].GameUnitType == 3 ? " (BERTHA)" : "")}" : "kein Ziel";
        return $"UKOL {e.Ukol}, {(e.Ordered ? "befohlen" : "selbst")}, {ziel}, "
             + (e.Path != null ? $"faehrt nach ({e.Goal.X},{e.Goal.Y})" : "steht")
             + $", Auftraege {e.Orders.Count}{(IstKi(e.Owner) ? ", KI" : ", keine KI")}";
    }

    /// <summary>Eine Messung: Abstand jeder bewaffneten, beweglichen Einheit, die der Bertha
    /// feind ist und hoechstens 20 Zellen weit steht, zur Bertha (Zellen).</summary>
    private readonly List<(int, int)> _bdBertha = new();

    public void BerthaDemoSonde(float zeit)
    {
        int bi = _entities.FindIndex(e => e.GameUnitType == 3 && !e.Dead && !e.IsBuilding && !e.IsProp);
        if (bi < 0) return;
        var b = _entities[bi];
        if (_bdBertha.Count == 0 || _bdBertha[^1] != (b.Col, b.Row)) _bdBertha.Add((b.Col, b.Row));
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding || !e.Mobile || i == bi || !IsHostile(b, e)) continue;
            float d = new Vector2(e.Col - b.Col, e.Row - b.Row).Length();
            if (!_bdStart.TryGetValue(i, out var s))
            {
                if (d > 20f) continue;
                _bdStart[i] = (d, d, d, BdZug(e));
                _bdSpur[i] = new List<string>();
            }
            else _bdStart[i] = (s.D0, Mathf.Min(s.DMin, d), Mathf.Max(s.DMax, d), s.Zug0);
            _bdSpur[i].Add($"{zeit:0}s:{d:0.0}@({e.Col},{e.Row})");
        }
    }

    public string BerthaDemoBericht(int demo, float dauer)
    {
        var sb = new StringBuilder($"bertha-demo-check: Demo {demo}, {dauer:0.0} s Spielzeit\n");
        int bi = _entities.FindIndex(e => e.GameUnitType == 3 && !e.IsBuilding && !e.IsProp);
        if (bi < 0) return sb.Append("  keine Bertha auf dieser Karte").ToString();
        var b = _entities[bi];
        sb.AppendLine($"  Bertha Platz {b.Slot} Spieler {b.Owner} auf ({b.Col},{b.Row}){(b.Dead ? " TOT" : "")}, {BdZug(b)}");
        sb.AppendLine("  Bertha-Weg: " + string.Join(" ", _bdBertha.ConvertAll(p => $"({p.Item1},{p.Item2})")));
        int weg = 0, hin = 0, gleich = 0;
        foreach (var (i, s) in _bdStart)
        {
            var e = _entities[i];
            float d1 = e.Dead ? float.NaN : new Vector2(e.Col - b.Col, e.Row - b.Row).Length();
            string urteil = float.IsNaN(d1) ? "tot" : d1 > s.D0 + 2f ? "WEG" : d1 < s.D0 - 2f ? "hin" : "bleibt";
            if (urteil == "WEG") weg++; else if (urteil == "hin") hin++; else if (urteil == "bleibt") gleich++;
            sb.AppendLine($"  {urteil,-6} Platz {e.Slot} Sp {e.Owner} {LabelOf(e)}: Abstand {s.D0:0.0} -> {(float.IsNaN(d1) ? "-" : d1.ToString("0.0"))} "
                        + $"(min {s.DMin:0.0}, max {s.DMax:0.0}); Anfang: {s.Zug0}; Ende: {BdZug(e)}");
            sb.AppendLine("         Spur " + string.Join(" ", _bdSpur[i]));
        }
        sb.Append($"  zusammen: weg {weg}, hin {hin}, bleibt {gleich}, tot {_bdStart.Count - weg - hin - gleich}");
        return sb.ToString();
    }

}
