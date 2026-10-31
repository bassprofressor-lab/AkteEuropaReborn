namespace AkteEuropaReborn.Rendering;

using System;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 30.10.2026 (bug-703) — <c>--ki-takt7-check[=takte]</c> (kopflos, Kampagne, Vorgabe
/// 1500 Takte Nachlauf). Prueft, dass der Gruppenangriff 0x4BC540 (Platz 7 der KI-Runde)
/// auch bei einem Rechnerspieler MIT Zielliste laeuft — siehe <see cref="AiTakt7"/>.
///
/// <para><b>Eingriff (sichtbar gemeldet):</b> nach einem Vorlauf, bis ein KI-Spieler mit
/// nicht leerer Liste eine abrufbereite Kampfeinheit (Modus 1/2) hat, wird EINE Einheit
/// des Menschen in den Nachbarsektor dieser Einheit versetzt (freie Zelle, der KI-Einheit
/// am naechsten) und dort festgeheftet, 30000 TP.</para>
/// <list type="number">
/// <item><b>T1 Fall eingetreten:</b> binnen 150 Takten (zwei Laeufe auch im alten 1,5-s-Takt)
/// <c>SektorVersuche</c> gestiegen, mindestens eine Einheit in Modus 3, und das Ziel des
/// Sektorangriffs (<c>TargetIdx</c>) ist der Eindringling.</item>
/// <item><b>T2 Ankunft</b> (Fensterwelt-Regel, die Wirkung mit ihrer Ankunft): im Nachlauf
/// kommt eine Modus-3-Einheit dem Eindringling naeher, oder er verliert TP.</item>
/// </list>
/// <para>Nullmodell <c>--ki-takt7-nur-ohne-ziele</c>: T1 DURCHGEFALLEN (0 Versuche).</para>
/// </summary>
public partial class MapEntityLayer
{
    public string KiTakt7Check(int takte)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new StringBuilder($"ki-takt7-check K{m}, Nachlauf {takte} Takte, " +
                                   $"--ki-takt7-nur-ohne-ziele {KiTakt7NurOhneZiele}, --ki-denktakt-alt {KiDenktaktAlt}\n");
        if (!InCampaign || _nav == null) return sb.Append("ki-takt7-check: KEIN URTEIL (nur mit -- --campaign=N)").ToString();
        MissionScriptTick(0.001f);
        int mensch = ViewPlayer;

        // ---- Vorlauf: ein KI-Spieler mit Liste und eine abrufbereite Kampfeinheit
        AiPlayer? ka = null;
        int ki = -1, vor = 0;
        Vector2I zelle = default;
        int ni = -1;
        for (; vor < 6000 && ki < 0; vor++)
        {
            SimTickFuerProbe();
            if (_taktNr % 25 != 0) continue;
            foreach (var a in _ai)
            {
                if (_missionTargets[a.Player].Count == 0 || AiGesperrt(a.Player)) continue;
                for (int i = 0; i < _entities.Count && ki < 0; i++)
                {
                    var e = _entities[i];
                    if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != a.Player || !e.Mobile) continue;
                    if (!CanFight(e) || e.AiCpu0 is not (1 or 2)) continue;
                    // 0x4BC5B1: nur ein Sektor mit belegt > 0 wird durchgegangen
                    var (bx, by) = AiSektorVon(e.Col, e.Row);
                    if (_aiRaster[a.Player][SektorIndex(bx, by)].Belegt <= 0) continue;
                    if (TakT7Nachbarzelle(a.Player, e, out zelle)) { ka = a; ki = i; }
                }
                if (ki >= 0) break;
            }
        }
        if (ka == null || ki < 0)
            return sb.Append($"ki-takt7-check K{m}: KEIN URTEIL (in {vor} Takten keine abrufbereite Einheit eines KI-Spielers mit Zielliste)").ToString();

        // ---- der Eindringling: ein Landfahrzeug/Fussvolk des Menschen
        for (int i = 0; i < _entities.Count && ni < 0; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != mensch || !e.Mobile || Untergestellt(e)) continue;
            if (!_nav.CanEnter(zelle.X, zelle.Y, e.Move)) continue;
            ni = i;
        }
        if (ni < 0) return sb.Append($"ki-takt7-check K{m}: KEIN URTEIL (keine Einheit des Menschen P{mensch} fuer die Zelle)").ToString();

        var K = _entities[ki];
        var N = _entities[ni];
        N.HpMax = N.Hp = 30000; N.Path = null; N.Orders.Clear(); N.Target = -1; N.Ordered = false;
        ProbeVersetzen(ni, zelle.X, zelle.Y);
        var (ksx, ksy) = AiSektorVon(K.Col, K.Row);
        var (nsx, nsy) = AiSektorVon(zelle.X, zelle.Y);
        sb.AppendLine($"  Vorlauf {vor} Takte; KI P{ka.Player} (Liste {_missionTargets[ka.Player].Count}) #{ki} {K.Name} Modus {K.AiCpu0} " +
                      $"bei ({K.Col},{K.Row}) Sektor ({ksx},{ksy})");
        sb.AppendLine($"  Eingriff: #{ni} {N.Name} (P{mensch}) nach ({zelle.X},{zelle.Y}) Sektor ({nsx},{nsy}), festgeheftet, 30000 TP");

        int v0 = ka.SektorVersuche, an0 = ka.SektorAngriffe, leine0 = ka.SmLeine;
        int kz0 = ka.SektorKeinZiel, wz0 = ka.SektorWegZuWeit, wp0 = WegtorProtokoll.Count;
        int t1 = -1, modus3 = 0;
        int Modus3() => _entities.Count(e => !e.Dead && !e.IsBuilding && e.Owner == ka.Player && e.AiCpu0 == 3);
        void Heften()
        {
            if (N.Dead) return;
            if (N.Col != zelle.X || N.Row != zelle.Y) ProbeVersetzen(ni, zelle.X, zelle.Y);
            N.Path = null; N.Target = -1; N.Cooldown = 99f;
        }
        for (int t = 0; t < 150 && t1 < 0; t++)
        {
            Heften();
            SimTickFuerProbe();
            modus3 = Modus3();
            if (ka.SektorVersuche > v0 && modus3 > 0 && ka.TargetIdx == ni) t1 = t + 1;
        }
        bool T1 = t1 > 0;
        sb.AppendLine($"  T1: Versuche {ka.SektorVersuche - v0}, Angriffe {ka.SektorAngriffe - an0}, Modus 3 {modus3}, " +
                      $"Ziel {(ka.TargetIdx == ni ? "der Eindringling" : ka.TargetIdx.ToString())}" +
                      (T1 ? $" — nach {t1} Takten" : "") + $" -> {(T1 ? "ja" : "NEIN")}");
        sb.AppendLine($"     (kein Ziel {ka.SektorKeinZiel - kz0}x, Weg >= 50 {ka.SektorWegZuWeit - wz0}x" +
                      (WegtorProtokoll.Count > wp0 ? $"; Wegtor: {WegtorProtokoll[^1]}" : "") + ")");

        // ---- T2: die Ankunft
        float Abstand()
        {
            float best = float.MaxValue;
            foreach (var e in _entities)
                if (!e.Dead && !e.IsBuilding && e.Owner == ka.Player && e.AiCpu0 == 3)
                    best = Math.Min(best, Math.Max(Math.Abs(e.Col - N.Col), Math.Abs(e.Row - N.Row)));
            return best;
        }
        float d0 = Abstand(), dmin = d0;
        int hp0 = N.Hp;
        for (int t = 0; t < takte && T1; t++)
        {
            Heften();
            SimTickFuerProbe();
            dmin = Math.Min(dmin, Abstand());
        }
        int schaden = hp0 - (N.Dead ? 0 : N.Hp);
        bool T2 = T1 && (dmin < d0 || schaden > 0);
        string D(float x) => x == float.MaxValue ? "—" : x.ToString("0");
        sb.AppendLine($"  T2 Ankunft: naechste Modus-3-Einheit {D(d0)} -> {D(dmin)} Zellen, Eindringling {schaden} TP verloren, " +
                      $"Leine {ka.SmLeine - leine0}x -> {(T2 ? "ja" : "NEIN")}");
        sb.Append($"ki-takt7-check K{m}: T1 {(T1 ? "ja" : "nein")}, T2 {(T2 ? "ja" : "nein")} — {(T1 && T2 ? "BESTANDEN" : "DURCHGEFALLEN")}");
        return sb.ToString();
    }

    /// <summary>Eine freie Zelle in einem der acht Nachbarsektoren der Einheit, ihr am
    /// naechsten (Schachbrettabstand) und begehbar fuer Rad/Kette.</summary>
    private bool TakT7Nachbarzelle(int p, Entity e, out Vector2I zelle)
    {
        zelle = default;
        var (sx, sy) = AiSektorVon(e.Col, e.Row);
        // Nachbarsektoren, in denen schon ein feindliches Landziel steht, scheiden aus —
        // get_target_in_sector 0x4BC3D0 naehme den mit der kleinsten Nummer, nicht den
        // Eindringling.
        var besetzt = new System.Collections.Generic.HashSet<(int, int)>();
        foreach (var o in _entities)
            if (!o.IsBuilding && !o.IsProp && !o.Dead && o.Mobile && !AiVerbuendet(p, o.Owner))
                besetzt.Add(AiSektorVon(o.Col, o.Row));
        var kand = new System.Collections.Generic.List<(int d, Vector2I z)>();
        for (int r = Math.Max(0, e.Row - 30); r < Math.Min(_nav!.Height, e.Row + 31); r++)
            for (int c = Math.Max(0, e.Col - 30); c < Math.Min(_nav.Width, e.Col + 31); c++)
            {
                var (cx, cy) = AiSektorVon(c, r);
                if (cx == sx && cy == sy) continue;
                if (Math.Abs(cx - sx) > 1 || Math.Abs(cy - sy) > 1 || besetzt.Contains((cx, cy))) continue;
                if (!_nav.CanEnter(c, r, e.Move) || _nav.OccupantAt(c, r) >= 0) continue;
                kand.Add((Math.Max(Math.Abs(c - e.Col), Math.Abs(r - e.Row)), new Vector2I(c, r)));
            }
        // Das Wegtor 0x4BC75C (< 50 Wellen) soll nicht der Grund eines Durchfalls sein:
        // die naechste Zelle, zu der die KI-Einheit einen kurzen Weg hat.
        int idx = _entities.IndexOf(e);
        foreach (var (d, z) in kand.OrderBy(k => k.d).Take(40))
        {
            var weg = _nav.FindPathUr(new Vector2I(e.Col, e.Row), z, e.Move, idx);
            if (weg == null || Simulation.NavGrid.UrWellen >= WegtorGrenze) continue;
            zelle = z;
            return true;
        }
        return false;
    }
}
