namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <c>--bertha-nachladen-check</c> (bug-550, KayelGee 14.10.): die Nachladezeit der
/// Dicken Bertha je Geschossart, synchron (SimTickFuerProbe), beendet sich selbst.
/// Die Probenhilfen ProbeRuhe/ProbeFreiNahe nutzt auch Eigenbeschuss.cs.
/// </summary>
public partial class MapEntityLayer
{
    public static bool BerthaNachladenCheck;

    private void ProbeRuhe(Entity e)
    {
        e.Path = null; e.Orders.Clear(); e.Target = -1; e.Ordered = false; e.AngriffsZelle = null;
    }

    private Vector2I? ProbeFreiNahe(int col, int row, int r0, int r1, int idx, bool genau = false)
    {
        for (int r = r0; r <= r1; r++)
            for (int dc = -r; dc <= r; dc++)
                for (int dr = -r; dr <= r; dr++)
                {
                    if (System.Math.Max(System.Math.Abs(dc), System.Math.Abs(dr)) != r) continue;
                    if (genau && dr != 0) continue;
                    int c = col + dc, z = row + dr;
                    if (_nav!.InBounds(c, z) && _nav.IsFree(c, z, _entities[idx].Move, idx)) return new Vector2I(c, z);
                }
        return null;
    }

    // =====================================================================
    //  --bertha-nachladen-check (bug-550), Karte map_25 oder ein DM mit Bertha
    // =====================================================================
    public string BerthaNachladenPruefung()
    {
        var sb = new StringBuilder("bertha-nachladen-check" + (BerthaNachladenAlt ? "  ⚠ NULLMODELL --bertha-nachladen-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        int bi = _entities.FindIndex(e => e.GameUnitType == 3 && !e.Dead && !e.IsBuilding && !e.IsProp);
        if (bi < 0) return sb.Append("  keine Dicke Bertha auf dieser Karte\n  DURCHGEFALLEN").ToString();
        var b = _entities[bi];
        int vi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry >= 0 || !e.Mobile || Untergestellt(e) || e.GameUnitType != 0) continue;
            if (IsHostile(b, e)) { vi = i; break; }
        }
        if (vi < 0) return sb.Append("  kein feindliches Fahrzeug\n  DURCHGEFALLEN").ToString();
        var v = _entities[vi];
        sb.AppendLine($"  Bertha Platz {b.Slot} Spieler {b.Owner} auf ({b.Col},{b.Row}), Satz +0x3D = {b.Reload} "
                    + $"(alter Weg: {(b.Reload > 0 ? $"{b.Reload} Takte" : "1,10 s Ersatz")}), Reichweite {b.Range}");

        var ergebnis = new List<string>();
        bool ok = true;
        foreach (var (abstand, artSoll, lo, hi) in new[] { (6, 20, 40, 59), (3, 21, 5, 7) })
        {
            ProbeRuhe(b); ProbeRuhe(v);
            // eine freie Zelle in genau diesem Schachbrettabstand, auf der richtigen Seite der
            // Weiche 160 Feineinheiten (Art 20 darueber, Art 21 bis dahin)
            Vector2I? p = null;
            for (int dc = -abstand; dc <= abstand && p == null; dc++)
                for (int dr = -abstand; dr <= abstand && p == null; dr++)
                {
                    if (System.Math.Max(System.Math.Abs(dc), System.Math.Abs(dr)) != abstand) continue;
                    int c = b.Col + dc, r = b.Row + dr;
                    bool fern = new Vector2(dc * 40, dr * 40).Length() > 160f;
                    if (fern == (artSoll == 20) && _nav.InBounds(c, r) && _nav.IsFree(c, r, v.Move, vi)) p = new Vector2I(c, r);
                }
            if (p == null) { sb.AppendLine($"  kein Platz {abstand} Zellen neben der Bertha"); ok = false; continue; }
            ProbeVersetzen(vi, p.Value.X, p.Value.Y);
            int art = SchussArt(b, v.Col, v.Row);
            b.Target = vi; b.Ordered = true; b.Cooldown = 0f;
            var abstaende = new List<int>();
            var gruende = new Dictionary<string, int>();
            Schussgruende = true;
            int letzter = -1, schuesse = 0, wegGenommen = 0;
            for (int t = 0; t < 1500 && schuesse < 13; t++)
            {
                float vor = b.Cooldown;
                b.Ammo = System.Math.Max(b.Ammo, System.Math.Max(1, b.AmmoMax));   // ⚠ EINGRIFF: Munition
                v.Hp = v.HpMax; ProbeRuhe(v);                                     // ⚠ EINGRIFF: Ziel lebt und steht
                b.Target = vi; b.Ordered = true;
                if (b.Dead) { sb.AppendLine($"  ⚠ die Bertha ist in Takt {t} tot"); break; }
                b.Hp = 30000;                                                     // ⚠ EINGRIFF: Bertha ueberlebt jeden Treffer
                if (b.Path != null || b.Orders.Count > 0) { wegGenommen++; b.Path = null; b.Orders.Clear(); }   // ⚠ EINGRIFF: KI-Fahrt
                SimTickFuerProbe();
                string g = (b.Schussgrund ?? "").Split('(')[0].Trim();
                gruende[g] = gruende.TryGetValue(g, out int gz) ? gz + 1 : 1;
                if (b.Cooldown > vor + 0.001f)
                {
                    if (letzter >= 0) abstaende.Add(t - letzter);
                    letzter = t; schuesse++;
                }
            }
            int min = abstaende.Count > 0 ? abstaende.Min() : -1, max = abstaende.Count > 0 ? abstaende.Max() : -1;
            double mittel = abstaende.Count > 0 ? abstaende.Average() : 0;
            // Takte zu je 1/50 s; der Cooldown laeuft in Sekunden ab -> +1 Takt Rundung zugelassen
            bool gut = (art == artSoll && abstaende.Count >= 5 && min >= lo && max <= hi + 1);
            ok &= gut;
            sb.AppendLine($"  {(gut ? "ok  " : "⚠ FALSCH")} Ziel {abstand} Zellen ({v.Col},{v.Row}): Art {art} (Soll {artSoll}), "
                        + $"{schuesse} Schuesse, Abstand {min}..{max} Takte, Mittel {mittel:0.0} Takte = {mittel / SimHz:0.00} s "
                        + $"[Mindestreichweite {b.RangeMin}, Munition {b.Ammo}/{b.AmmoMax}, zuletzt »{b.Schussgrund}«, KI-Fahrten genommen {wegGenommen}, Gruende je Takt: "
                        + string.Join(", ", gruende.Select(kv => $"»{kv.Key}« {kv.Value}")) + "] "
                        + $"(Soll {lo}..{hi} Takte, 0x40C4F1)");
        }
        ProbeRuhe(b); b.Hp = System.Math.Min(b.Hp, b.HpMax);
        sb.AppendLine($"  gesetzte Takte: Art 20 {BerthaNachladen20}x {(BerthaNachladen20 > 0 ? $"{BerthaTakte20Min}..{BerthaTakte20Max}" : "-")}, "
                    + $"Art 21 {BerthaNachladen21}x {(BerthaNachladen21 > 0 ? $"{BerthaTakte21Min}..{BerthaTakte21Max}" : "-")}");
        sb.AppendLine("  ⚠ EINGRIFFE: das Ziel wird versetzt, steht still und wird je Takt geheilt; die Bertha bekommt Munition");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

}
