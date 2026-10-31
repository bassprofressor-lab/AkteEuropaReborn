namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐ <b>GRUNDRISS GEGEN DIE BELEGUNGSKARTE</b> (30.10.2026, bug-706).
///
/// <para><b>Befund:</b> Unser Gelaende (<c>terrain</c> im Kartenimport) ist die sec6 der
/// Kartendatei, Zelle fuer Zelle (Abgleich ueber 33 Kampagnenkarten und NET07: 0
/// Abweichungen, aekernel-tools/imap_abgleich.py). Die Legetore von Mine und Falle fragten
/// aber ZUSAETZLICH <see cref="GebaeudeAufZelle"/>, und das ist das ganze Rechteck
/// <c>FootW × FootH</c> jedes Gebaeudes. Das Original fragt nur die imap; dort traegt ein
/// Gebaeude nur die Zellen, die es wirklich stempelt (60000 + Platz), seine freien
/// Grundrisszellen bleiben 0xFFFE. Beispiel K25 (35,59): sec6 0xFFFE in 25.CWM und in allen
/// fuenf K25-DM-Staenden, bei uns im Rechteck eines Gebaeudes — die Falle P4 des Aufbaus
/// fiel weg.</para>
///
/// <para><b>Behoben:</b> Bei der Frage »ist die Zelle frei« zaehlt nur noch Boden und
/// Belegung (beides aus sec6 bzw. unserem Stempel). Gegenschalter <c>--imap-abgleich-alt</c>
/// (wieder mit Grundrissrechteck).</para>
///
/// <para>⚠ NICHT behoben, Ursache nicht eindeutig: (168,96) auf K25 traegt in 25.CWM das
/// zerstoerbare Objekt 61106 (Art 64) — trotzdem liegt dort in allen fuenf DM-Staenden die
/// Falle des Aufbaus. Ebenso liegen in allen fuenf Staenden 12 Minen auf Zellen, die in der
/// CWM WALD sind (15 andere Waldzellen der Rechtecke bleiben frei). Zur Setup-Zeit waren
/// diese Zellen im Original also frei; wer sie freigibt, ist ungelesen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--imap-abgleich-alt</c> — der Stand bis 30.10.2026.</summary>
    public static bool ImapAbgleichAlt;

    /// <summary>Die Gebaeudefrage der Legetore: mit Grundrissrechteck nur unter dem
    /// Gegenschalter.</summary>
    private bool GrundrissSperrt(int c, int r) => ImapAbgleichAlt && GebaeudeAufZelle(c, r) >= 0;

    /// <summary><c>--imap-abgleich-check</c>: zaehlt auf der geladenen Karte die Zellen,
    /// die im Grundrissrechteck eines Gebaeudes liegen, aber frei und unbelegt sind (dort
    /// sagte das alte Tor »Gebaeude«, die imap »frei«), getrennt nach Kartengebaeude und
    /// gebautem; und probiert, ob Mine und Falle dort jetzt gehen.</summary>
    public string ImapAbgleichCheck()
    {
        var sb = new StringBuilder("imap-abgleich-check\n");
        EnsureMissionScript();
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        int frei = 0, rau = 0, wasser = 0, gesperrt = 0, belegt = 0, gebaeude = 0;
        var je = new Dictionary<int, int>();
        Vector2I? probe = null;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            gebaeude++;
            int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
            for (int dc = 0; dc < w; dc++)
                for (int dr = 0; dr < h; dr++)
                {
                    int c = b.Col + dc, r = b.Row + dr;
                    if (!_nav.InBounds(c, r)) continue;
                    if (_nav.BesetztVon(c, r) >= 0) { belegt++; continue; }
                    switch (_nav.GroundAt(c, r))
                    {
                        case Simulation.NavGrid.Ground.Free:
                            frei++; je[b.BType] = je.GetValueOrDefault(b.BType) + 1;
                            if (probe == null && !MineOderFalleAufZelle(c, r)) probe = new Vector2I(c, r);
                            break;
                        case Simulation.NavGrid.Ground.Rough: rau++; break;
                        case Simulation.NavGrid.Ground.Water: wasser++; break;
                        default: gesperrt++; break;
                    }
                }
        }
        sb.AppendLine($"  {gebaeude} Gebaeude; Grundrisszellen: belegt {belegt}, gesperrt {gesperrt}, "
                    + $"FREI und unbelegt {frei}, rau {rau}, Wasser {wasser}");
        var arten = new List<string>();
        foreach (var kv in je) arten.Add($"Art {kv.Key}: {kv.Value}");
        sb.AppendLine("  freie Grundrisszellen je Gebaeudeart: " + string.Join(", ", arten));
        bool ok = true;
        if (probe is { } p)
        {
            bool m = MineLegen(p.X, p.Y, 1);
            for (int i = 0; i < _minen.Length; i++)
                if (_minen[i].Aktiv && _minen[i].Col == p.X && _minen[i].Row == p.Y) { _minen[i].Aktiv = false; break; }
            ok = m != ImapAbgleichAlt;
            sb.AppendLine($"  Probe: Mine auf freie Grundrisszelle ({p.X},{p.Y}) -> {(m ? "gelegt" : "abgelehnt")} "
                        + $"(Soll {(ImapAbgleichAlt ? "abgelehnt, --imap-abgleich-alt" : "gelegt wie im Original")})");
        }
        else sb.AppendLine("  (keine freie Grundrisszelle — Probe entfaellt)");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
