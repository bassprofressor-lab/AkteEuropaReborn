namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <c>--waffenbild-probe</c> (bug-514/515/516, KayelGee R5#22/#23/#24), kopflos in
/// K25 (die Bertha) — oder jeder Karte mit Fussvolk und MG-Fahrzeugen.
///
/// <para>Bei 2 s: jede gefundene Schuetzenart feuert EINEN echten Schuss ueber
/// <see cref="Fire"/> auf den naechsten Gegner, gezaehlt werden Leuchtspuren,
/// Bodenstrahlen und Geschosse samt Flugbild. Soll: Fussvolk 190/192/199 und
/// Fahrzeug-MG 24/25 ohne Linie, 191/194 ein Strahl, Bertha Geschosse flug_65
/// (fern, 2 Stueck) bzw. flug_61 (nah). Dazu faehrt die Bertha diagonal; Soll:
/// kein ungerades Rumpfziel (<see cref="Blick16Ungerade"/>).</para>
///
/// <para>⚠ EINGRIFF, gesagt: der Bertha-Schuss geht auf den naechsten Gegner
/// gleich welcher Entfernung — die Nahprobe (Art 21) misst die Weiche
/// <see cref="SchussArt"/> an einer gedachten Zelle 3 Spalten daneben.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool WaffenbildProbeAn;
    private bool _wbFertig, _wbFahrt;
    private float _wbFahrtAb;
    private readonly List<string> _wbZeilen = new();

    private int WbNaechsterGegner(Entity s)
    {
        int best = -1; float bd = float.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var v = _entities[i];
            if (v.IsProp || v.Dead || v.Owner == s.Owner || v.Owner is < 0 or > 7 || Allied(s.Owner, v.Owner)) continue;
            float d = v.Pos.DistanceSquaredTo(s.Pos);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    private string WbSchuss(int si, string wer)
    {
        var s = _entities[si];
        int vi = WbNaechsterGegner(s);
        if (vi < 0) return $"{wer}: kein Gegner";
        var v = _entities[vi];
        int tr = _tracers.Count, st = _strahlen.Count, sh = _shots.Count;
        Fire(si, s, vi, v, ("Probe", 1, 99f));
        var arten = new List<string>();
        for (int k = sh; k < _shots.Count; k++) arten.Add(_shots[k].Kind ?? "ohne Bild");
        int abst = Mathf.Max(Mathf.Abs(s.Col - v.Col), Mathf.Abs(s.Row - v.Row));
        return $"{wer} (Waffe {s.Weapon}, Art {SchussArt(s, v.Col, v.Row)}, {abst} Zellen): Leuchtspuren {_tracers.Count - tr}, " +
               $"Strahlen {_strahlen.Count - st}, Geschosse {_shots.Count - sh}{(arten.Count > 0 ? " [" + string.Join(",", arten) + "]" : "")}";
    }

    public void WaffenbildTakt()
    {
        if (!WaffenbildProbeAn || _wbFertig || _clock < 2f) return;
        if (!_wbFahrt)
        {
            _wbFahrt = true; _wbFahrtAb = _clock;
            var gesehen = new HashSet<string>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Weapon <= 0 || e.Owner is < 0 or > 7) continue;
                string art = e.GameUnitType == 3 ? "Bertha"
                           : e.Infantry >= 0 ? $"Fussvolk {e.Weapon - InfCompBase}"
                           : e.Weapon is 24 or 25 ? $"MG-Fahrzeug {e.Weapon}" : "";
                if (art.Length == 0 || !gesehen.Add(art)) continue;
                _wbZeilen.Add(WbSchuss(i, art));
                if (e.GameUnitType == 3)
                {
                    _wbZeilen.Add($"Bertha Weiche: 3 Zellen -> Art {SchussArt(e, e.Col + 3, e.Row)} (Soll 21), " +
                                  $"6 Zellen diagonal -> Art {SchussArt(e, e.Col + 6, e.Row + 6)} (Soll 20)");
                    // diagonal fahren
                    for (int k = 6; k >= 2; k--)
                        foreach (var (dc, dr) in new[] { (k, k), (-k, k), (k, -k), (-k, -k) })
                            if (_nav != null && _nav.CanEnter(e.Col + dc, e.Row + dr, e.Move))
                            {
                                MissionOrderAt(e.Slot, e.Col + dc, e.Row + dr, -1);
                                _wbZeilen.Add($"Bertha faehrt von ({e.Col},{e.Row}) nach ({e.Col + dc},{e.Row + dr})");
                                goto gefahren;
                            }
                    gefahren:;
                    _wbBertha = i;
                }
            }
            return;
        }
        if (_clock < _wbFahrtAb + 25f) return;
        _wbFertig = true;
        var sb = new StringBuilder("waffenbild-probe:");
        foreach (var z in _wbZeilen) sb.Append("\n   " + z);
        if (_wbBertha >= 0)
        {
            var b = _entities[_wbBertha];
            sb.Append($"\n   Bertha nach 25 s auf ({b.Col},{b.Row}), Rumpfblick {b.Facing}, Rumpfziele ungerade {Blick16Ungerade}x" +
                      (Blick16Alt ? "  ⚠ Nullmodell --blick16-alt" : ""));
        }
        foreach (var k in new[] { "mzf45", "mzf46", "mzf47", "mzf48" }) EffectFrames(k);
        sb.Append("\n   " + SchussbildLine());
        GD.Print(sb.ToString());
    }

    private int _wbBertha = -1;
}
