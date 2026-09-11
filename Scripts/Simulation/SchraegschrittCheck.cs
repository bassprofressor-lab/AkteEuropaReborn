namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--schraegschritt-check</c> (11.09.2026) — <b>fragt ein schraeger Schritt
/// die zwei Flanken?</b> Siehe Simulation/Schraegschritt.cs.
///
/// <para>Ein Rad/Ketten-Fahrer faehrt die reine Diagonale (c,r) → (c+4,r+4).
/// Fuer den Schritt (c+1,r+1) → (c+2,r+2) (Richtung 7) sind die Flanken
/// <b>A = d−1 = (c+2, r+1)</b> und <b>B = d+1 = (c+1, r+2)</b>. Das
/// Gegenueber wird NACH der Wegsuche gestellt — sonst misst man die Planung.</para>
/// <list type="bullet">
/// <item>A feindlicher Fusssoldat auf Flanke B: Soll tot, ueberfahren, liegt auf
///   SEINER Zelle; der Fahrer tut den Schritt.</item>
/// <item>B eigener Fusssoldat auf Flanke B: Soll lebt, gebeten ≥ 1.</item>
/// <item>C feindliches Fahrzeug auf Flanke A: Soll blockiert ≥ 1, und der
///   Schritt ueber die Ecke faellt NICHT.</item>
/// </list>
/// <para>⚠ EINGRIFFE, genannt: Versetzen, Waffen ab, und die Besitzer werden
/// gesetzt (Fahrer = Spieler, damit keine KI seinen Weg abraeumt; Gegenueber
/// feindlich bzw. eigen). Nullmodell <c>--ueberfahren-nur-ziel</c>: A lebt, B
/// wird nicht gebeten, C schneidet die Ecke.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private bool _sCheckAn;
    private readonly Dictionary<int, int[]> _sZaehler = new();    // [gebeten, blockiert, frei]
    private readonly List<(int Fahrer, Vector2I Von, Vector2I Nach)> _sSchritte = new();

    /// <summary><c>--schraegschritt-faelle=…</c> (Standard ABC).</summary>
    public static string SchraegschrittFaelle = "ABC";

    private sealed class SchraegFall
    {
        public char Name;
        public string Titel = "", Weg = "", Eingriff = "";
        public int Fahrer = -1, Andere = -1, C, R;
    }

    private readonly List<SchraegFall> _sFaelle = new();
    private readonly List<Rect2I> _sFlaechen = new();

    public void SchraegschrittCheckStart()
    {
        if (_nav == null) return;
        _sCheckAn = true;
        Fahrgruende = true;
        var benutzt = new HashSet<int>();
        int feind = -1;
        for (int p = 0; p <= 7 && feind < 0; p++)
            if (!Allied(ViewPlayer, p)) feind = p;

        bool Frei(int k)
        {
            var x = _entities[k];
            return !benutzt.Contains(k) && !x.Dead && !x.IsProp && !x.IsBuilding && x.Mobile
                   && x.Ukol < 50 && !Untergestellt(x);
        }
        int Suche(System.Func<Entity, bool> art)
        {
            for (int k = 0; k < _entities.Count; k++)
                if (Frei(k) && art(_entities[k])) return k;
            return -1;
        }
        bool RadKette(Entity x) => x.Infantry < 0 && x.GameUnitType == 0
                                   && x.Move == Simulation.NavGrid.MoveClass.Vehicle;

        void Fall(char name, string titel)
        {
            if (!SchraegschrittFaelle.Contains(name)) return;
            var f = new SchraegFall { Name = name, Titel = titel };
            _sFaelle.Add(f);
            int fa = Suche(RadKette);
            if (fa < 0) { f.Weg = "kein Rad/Ketten-Fahrer"; return; }
            benutzt.Add(fa);
            int an = name == 'C' ? Suche(RadKette) : Suche(x => x.Infantry >= 0);
            if (an < 0) { f.Weg = "kein Gegenueber"; return; }
            benutzt.Add(an);
            if (name != 'B' && feind < 0) { f.Weg = "kein feindlicher Besitzer"; return; }
            if (!FreieFlaeche(out f.C, out f.R)) { f.Weg = "keine freie Flaeche"; return; }

            var e1 = _entities[fa];
            var e2 = _entities[an];
            int alt1 = e1.Owner, alt2 = e2.Owner;
            e1.Owner = ViewPlayer;
            e2.Owner = name == 'B' ? ViewPlayer : feind;
            f.Eingriff = $"⚠ EINGRIFF Besitzer Fahrer {alt1}->{e1.Owner}, Gegenueber {alt2}->{e2.Owner}";
            foreach (var x in new[] { e1, e2 }) { x.Weapon = 0; x.Target = -1; x.Path = null; x.Ordered = false; }

            ProbeVersetzen(fa, f.C, f.R);
            var ziel = new Vector2I(f.C + 4, f.R + 4);
            var weg = _nav.FindPath(new Vector2I(f.C, f.R), ziel, e1.Move, fa);
            bool diagonal = weg != null && weg.Count == 4;
            for (int k = 0; diagonal && k < 4; k++)
                diagonal = weg![k] == new Vector2I(f.C + k + 1, f.R + k + 1);
            if (!diagonal)
            {
                f.Weg = $"Weg nicht diagonal ({weg?.Count ?? 0} Schritte)";
                return;
            }
            // das Gegenueber NACH der Wegsuche
            if (name == 'C') ProbeVersetzen(an, f.C + 2, f.R + 1);         // Flanke A
            else ProbeVersetzen(an, f.C + 1, f.R + 2);                     // Flanke B
            e1.Path = weg; e1.PathIdx = 0; e1.Goal = ziel; e1.Ordered = true;
            f.Fahrer = fa; f.Andere = an;
            f.Weg = "4 Schritte diagonal";
        }

        Fall('A', "Feind-Fussvolk auf der Flanke ");
        Fall('B', "eigenes Fussvolk auf der Flanke");
        Fall('C', "Feind-Fahrzeug auf der Flanke ");
        foreach (var f in _sFaelle)
            GD.Print($"schraegschritt-check: {f.Name} {f.Titel.Trim()} — Flaeche ({f.C},{f.R}), {f.Weg}");
    }

    public string SchraegschrittCheckLine()
    {
        var sb = new System.Text.StringBuilder("schraegschritt-check\n");
        if (!_sCheckAn) return sb.Append("  nicht gestartet — der Lauf sagt NICHTS").ToString();
        bool ok = _sFaelle.Count > 0;
        foreach (var f in _sFaelle)
        {
            if (f.Fahrer < 0)
            {
                ok = false;
                sb.Append($"  {f.Name} {f.Titel}: ⚠ nicht herstellbar ({f.Weg}) — sagt NICHTS\n");
                continue;
            }
            var fa = _entities[f.Fahrer];
            var an = _entities[f.Andere];
            var z = _sZaehler.TryGetValue(f.Fahrer, out var zz) ? zz : new int[3];
            var von = new Vector2I(f.C + 1, f.R + 1);
            var nach = new Vector2I(f.C + 2, f.R + 2);
            bool ecke = _sSchritte.Exists(s => s.Fahrer == f.Fahrer && s.Von == von && s.Nach == nach);
            int schraeg = _sSchritte.FindAll(s => s.Fahrer == f.Fahrer && s.Von.X != s.Nach.X && s.Von.Y != s.Nach.Y).Count;
            int gerade = _sSchritte.FindAll(s => s.Fahrer == f.Fahrer).Count - schraeg;
            bool ueber = _ueberfahrenOpfer.Contains(f.Andere);
            var soll = f.Name switch
            {
                'A' => an.Dead && ueber && an.Col == f.C + 1 && an.Row == f.R + 2 && ecke,
                'B' => !an.Dead && !ueber && z[0] > 0,
                _ => z[1] > 0 && !ecke,
            };
            ok &= soll;
            sb.Append($"  {f.Name} {f.Titel}: Gegenueber {(an.Dead ? "TOT" : "lebt")}"
                    + $"{(ueber ? " (ueberfahren)" : "")} auf ({an.Col},{an.Row}); Fahrer auf ({fa.Col},{fa.Row}), "
                    + $"Schritt ueber die Ecke {(ecke ? "ja" : "nein")}; gebeten {z[0]} / blockiert {z[1]} / frei {z[2]}; "
                    + $"{schraeg} schraege, {gerade} gerade Schritte  {(soll ? "ja" : "NEIN")}  {f.Eingriff}\n");
            if (!soll && !string.IsNullOrEmpty(fa.Fahrgrund))
                sb.Append($"      letzter Fahrgrund: {fa.Fahrgrund}\n");
        }
        sb.Append($"  Gegenschalter --ueberfahren-nur-ziel: {UeberfahrenNurZiel}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Eine 7x7-Flaeche um (c..c+4, r..r+4), frei fuer Fahrzeuge und
    /// ohne rauen Boden (sonst waere der Weg nicht zwingend diagonal), weit
    /// genug von den anderen Faellen.</summary>
    private bool FreieFlaeche(out int col, out int row)
    {
        col = row = -1;
        for (int r = 6; r < _nav!.Height - 12; r += 2)
            for (int c = 6; c < _nav.Width - 12; c += 2)
            {
                var box = new Rect2I(c - 4, r - 4, 13, 13);
                bool nah = false;
                foreach (var b in _sFlaechen) nah |= b.Intersects(box);
                if (nah) continue;
                bool gut = true;
                for (int x = c - 1; x <= c + 5 && gut; x++)
                    for (int y = r - 1; y <= r + 5 && gut; y++)
                        gut = _nav.GroundAt(x, y) == Simulation.NavGrid.Ground.Free
                           && _nav.IsFree(x, y, Simulation.NavGrid.MoveClass.Vehicle, -1);
                if (!gut) continue;
                col = c; row = r;
                _sFlaechen.Add(box);
                return true;
            }
        return false;
    }
}
