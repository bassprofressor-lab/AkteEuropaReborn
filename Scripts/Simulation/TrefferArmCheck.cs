namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--trefferarm-check</c> (11.09.2026) — <b>geht ein Treffer durch den Arm,
/// den das Zellwort des Opfers verlangt?</b>
///
/// <para>Vier Teile, jeder mit gezogenen Wuerfen statt einer nachgerechneten
/// Formel: die Werte kommen aus dem Weg, der im Spiel laeuft
/// (<c>ShotDamage</c>, <c>ApplyHit</c>, <c>ApplyMissionHits</c>), und werden
/// gegen das BAND geprueft, das der gelesene Arm zulaesst.</para>
/// <list type="number">
/// <item>Schuss auf Fussvolk: 400 Wuerfe liegen im Band des
///   Infanteriezellen-Arms — und das Paar ist so gewaehlt, dass sich dieses Band
///   vom Einheitenarm unterscheidet (sonst sagt der Teil NICHTS).</item>
/// <item>Erfahrung: ein Treffer auf Fussvolk aendert Rang und Erfahrung des
///   Schuetzen nicht; ein Treffer auf ein Fahrzeug darf es (Gegenprobe).</item>
/// <item>Skripttreffer 40050 (hit_cell, SETUP): 60 Treffer auf einen
///   Fusssoldaten und ein Fahrzeug liegen im Band ihres Arms, nicht bei der
///   halben Huelle.</item>
/// <item>fire_at: 60 Treffer auf einen Fusssoldaten liegen im SCHUSSband des
///   Schuetzen.</item>
/// </list>
/// <para>⚠ EINGRIFF: die Trefferpunkte der Opfer werden fuer die Messung auf
/// 10000 gesetzt (damit niemand stirbt) und danach zurueckgestellt; Rang und
/// Erfahrung des Schuetzen ebenso. Nullmodelle <c>--infanterie-einheitenarm</c>
/// (Teile 1, 2, 4) und <c>--skripttreffer-halbe-huelle</c> (Teile 3, 4).</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Wie oft <c>Erfahren</c> gerufen wurde — auch mit Zuwachs 0.</summary>
    public int ErfahrenRufe;

    /// <summary>Welche Werte ein Arm mit diesem Kern zulaesst. Einheitenarm:
    /// unter 1 -> 0 (bei ≤ −2) oder rand%10/3; Infanteriezellen-Arm: rand%10/7.</summary>
    private static bool ImBand(int wert, int kern, bool infanterie)
    {
        if (wert >= 1 && wert >= kern - 4 && wert <= kern + 4) return true;
        if (kern - 4 >= 1) return false;                       // die Klemme ist unerreichbar
        return infanterie ? wert is 0 or 1 : wert is >= 0 and <= 3;
    }

    public string TrefferArmCheck()
    {
        var sb = new System.Text.StringBuilder("trefferarm-check\n");
        bool ok = true;

        // ---- das Paar: bewaffneter Schuetze und Fusssoldat, Baender verschieden ----
        int si = -1, vi = -1, besterAbstand = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var s = _entities[i];
            if (s.Dead || s.IsProp || s.IsBuilding || s.Attack <= 0) continue;
            for (int j = 0; j < _entities.Count; j++)
            {
                var v = _entities[j];
                if (j == i || v.Dead || v.IsProp || v.Infantry < 0) continue;
                int es = ElevOf(s.Col, s.Row), ev = ElevOf(v.Col, v.Row);
                int d = Mathf.Abs(InfanterieKern(s.Rating28, s.Attack + 2 * es, v, ev) - ShotCore(s, v, es, ev));
                if (d > besterAbstand) { besterAbstand = d; si = i; vi = j; }
            }
        }
        if (si < 0) return sb.Append("  kein Schuetze/Fusssoldat auf der Karte — sagt NICHTS").ToString();
        var sch = _entities[si];
        var opfer = _entities[vi];
        int elS = ElevOf(sch.Col, sch.Row), elV = ElevOf(opfer.Col, opfer.Row);
        int kernInf = InfanterieKern(sch.Rating28, sch.Attack + 2 * elS, opfer, elV);
        int kernEin = ShotCore(sch, opfer, elS, elV);
        sb.Append($"  Schuetze {LabelOf(sch)} (A{sch.Attack} R{sch.Rating28} h{elS}) -> {LabelOf(opfer)} "
                + $"(V{opfer.Defence} R{opfer.Rating28} h{elV}): Kern Infanteriearm {kernInf}, Einheitenarm {kernEin}\n");
        if (besterAbstand < 5)
            sb.Append("  ⚠ die beiden Kerne liegen weniger als 5 auseinander — Teil 1 trennt die Arme kaum\n");

        // ---- Teil 1: 400 Schuesse ----
        int minS = int.MaxValue, maxS = int.MinValue, ausserInf = 0, ausserEin = 0;
        for (int k = 0; k < 400; k++)
        {
            int w = ShotDamage(sch, opfer, 0);
            minS = Mathf.Min(minS, w); maxS = Mathf.Max(maxS, w);
            if (!ImBand(w, kernInf, true)) ausserInf++;
            if (!ImBand(w, kernEin, false)) ausserEin++;
        }
        // ⚠ 11.09.2026 — KEINE Ausnahme fuer nahe Kerne. Der erste Lauf liess
        // »Abstand < 5« durch, und das Nullmodell bestand damit: die Werte des
        // Einheitenarms (0..3) liegen im Band des Infanteriearms. Getragen wird
        // das Urteil davon, dass Wuerfe AUSSERHALB des Einheitenarms fallen.
        bool t1 = ausserInf == 0 && ausserEin > 0;
        ok &= t1;
        sb.Append($"  1 Schuss auf Fussvolk: 400 Wuerfe {minS}…{maxS}; ausserhalb Infanteriearm {ausserInf}, "
                + $"ausserhalb Einheitenarm {ausserEin}  {(t1 ? "ja" : "NEIN")}\n");

        // ---- Teil 2: Erfahrung ----
        // ⚠ 11.09.2026 — gezaehlt wird der RUF von Erfahren, nicht die
        // Erfahrung: bei kleinen Treffern ist der Zuwachs 0, und dann trennt
        // »unveraendert« den Arm nicht vom Nullmodell (erster Lauf).
        int hpO = opfer.Hp, rang0 = sch.Rating28, erf0 = sch.Erfahrung;
        opfer.Hp = 10000;
        int rufe0 = ErfahrenRufe;
        for (int k = 0; k < 20; k++) ApplyHit(si, vi, opfer, 0);
        int rufeInf = ErfahrenRufe - rufe0;
        opfer.Hp = hpO;
        sch.Rating28 = rang0; sch.Erfahrung = erf0;
        int rufeFz = -1;
        string gegen = "kein Fahrzeug als Gegenprobe";
        for (int j = 0; j < _entities.Count; j++)
        {
            var v = _entities[j];
            if (j == si || v.Dead || v.IsProp || v.IsBuilding || v.Infantry >= 0) continue;
            int hpV = v.Hp;
            v.Hp = 10000;
            rufe0 = ErfahrenRufe;
            for (int k = 0; k < 20; k++) ApplyHit(si, j, v, 0);
            rufeFz = ErfahrenRufe - rufe0;
            v.Hp = hpV;
            sch.Rating28 = rang0; sch.Erfahrung = erf0;
            gegen = $"20 Treffer auf {LabelOf(v)}: {rufeFz}x gerufen";
            break;
        }
        bool t2 = rufeInf == 0 && rufeFz > 0;
        ok &= t2;
        sb.Append($"  2 Erfahrung: 20 Treffer auf Fussvolk: {rufeInf}x Erfahren gerufen; "
                + $"Gegenprobe {gegen}  {(t2 ? "ja" : "NEIN")}\n");

        // ---- Teil 3: Skripttreffer 40050 ----
        (int Aus, int Min, int Max, int N) Skript(Entity v, int kern, bool inf)
        {
            int hp = v.Hp, aus = 0, mn = int.MaxValue, mx = int.MinValue;
            for (int k = 0; k < 60; k++)
            {
                v.Hp = 10000;
                ApplyMissionHits(new[] { (v.Col, v.Row) }, funken: false);
                int w = 10000 - v.Hp;
                mn = Mathf.Min(mn, w); mx = Mathf.Max(mx, w);
                if (!ImBand(w, kern, inf)) aus++;
            }
            v.Hp = hp;
            return (aus, mn, mx, 60);
        }
        int kernSkriptInf = InfanterieKern(0, SetupSchaden, opfer, elV);
        var r3a = Skript(opfer, kernSkriptInf, true);
        sb.Append($"  3 Skripttreffer auf Fussvolk: Kern {kernSkriptInf}, 60 Treffer {r3a.Min}…{r3a.Max}, "
                + $"ausserhalb {r3a.Aus}  {(r3a.Aus == 0 ? "ja" : "NEIN")}\n");
        ok &= r3a.Aus == 0;
        int fz = -1;
        for (int j = 0; j < _entities.Count && fz < 0; j++)
        {
            var v = _entities[j];
            if (!v.Dead && !v.IsProp && !v.IsBuilding && v.Infantry < 0 && v.Mobile
                && _nav?.OccupantAt(v.Col, v.Row) == j) fz = j;
        }
        if (fz >= 0)
        {
            var v = _entities[fz];
            int ev = ElevOf(v.Col, v.Row);
            int kernSkriptEin = 30 * SetupSchaden / 40 - (30 + v.Rating28 / 5) * (v.Defence + 2 * ev) / 50;
            var r3b = Skript(v, kernSkriptEin, false);
            sb.Append($"    auf {LabelOf(v)}: Kern {kernSkriptEin}, 60 Treffer {r3b.Min}…{r3b.Max}, "
                    + $"ausserhalb {r3b.Aus}  {(r3b.Aus == 0 ? "ja" : "NEIN")}\n");
            ok &= r3b.Aus == 0;
        }
        else sb.Append("    ⚠ kein Fahrzeug fuer den Einheitenarm\n");

        // ---- Teil 4: fire_at ----
        {
            // fire_at spart eigenes/verbuendetes Fussvolk aus — das Opfer muss
            // FREMD sein. ⚠ 11.09.2026: auf K6 war das Paar aus Teil 1 verbuendet
            // und der Teil sagte nichts. Gibt es kein fremdes Opfer, wird der
            // Besitzer umgesetzt (Eingriff, genannt) und zurueckgestellt.
            int altBesitzer = opfer.Owner;
            string eingriff = "";
            if (opfer.Owner >= 0 && sch.Owner >= 0 && Allied(sch.Owner, opfer.Owner))
            {
                for (int p = 0; p <= 7; p++)
                    if (!Allied(sch.Owner, p)) { opfer.Owner = p; break; }
                eingriff = opfer.Owner != altBesitzer ? $" ⚠ EINGRIFF Besitzer {altBesitzer} -> {opfer.Owner}" : "";
            }
            if (Allied(sch.Owner, opfer.Owner))
                sb.Append("  4 fire_at: ⚠ kein fremder Besitzer herstellbar — Teil sagt NICHTS\n");
            else
            {
                int hp = opfer.Hp, aus = 0, mn = int.MaxValue, mx = int.MinValue;
                for (int k = 0; k < 60; k++)
                {
                    opfer.Hp = 10000;
                    ApplyMissionHits(new[] { (opfer.Col, opfer.Row) }, funken: false, schuetze: si);
                    int w = 10000 - opfer.Hp;
                    mn = Mathf.Min(mn, w); mx = Mathf.Max(mx, w);
                    if (!ImBand(w, kernInf, true)) aus++;
                }
                opfer.Hp = hp;
                sch.Rating28 = rang0; sch.Erfahrung = erf0;
                bool t4 = aus == 0 && mn < 5000;
                sb.Append($"  4 fire_at auf Fussvolk: 60 Treffer {mn}…{mx} (Schussband um {kernInf}), "
                        + $"ausserhalb {aus}  {(t4 ? "ja" : "NEIN")}{eingriff}\n");
                ok &= t4;
            }
            opfer.Owner = altBesitzer;
        }

        sb.Append($"  Gegenschalter --infanterie-einheitenarm: {InfanterieArmAlt}, "
                + $"--skripttreffer-halbe-huelle: {SkripttrefferHalbeHuelle}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
