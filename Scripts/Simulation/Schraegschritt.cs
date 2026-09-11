namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using Step = AkteEuropaReborn.Simulation.NavGrid.Step;
using MoveClass = AkteEuropaReborn.Simulation.NavGrid.MoveClass;
using Ground = AkteEuropaReborn.Simulation.NavGrid.Ground;

/// <summary>
/// <b>K4 — DER SCHRAEGSCHRITT</b> (gebaut 11.09.2026 nach
/// <c>berichte/schraegschritt-k4.md</c>, seine Ansage »ja bau beides wie im
/// original«).
///
/// <para><b>Gelesen:</b> <c>Can_go</c> prueft bei ungerader Richtung
/// (@0x405BE4 <c>test bl,1</c>) DREI Zellen, in dieser Reihenfolge:</para>
/// <code>
///   Ziel d          @0x405DE2..0x405E89   prejet -> gemerkt F+0x24 | Freund gefragt | sonst 0
///   Flanke A = d−1  @0x405EA7..0x405FA2   frei/RAU weiter | prejet -> F+0x26 | Freund gefragt | sonst 0
///   Flanke B = d+1  @0x405FB1..0x40616B   frei/RAU weiter | prejet -> F+0x28 | Freund gefragt -> SOFORT 1
///   Ende @0x406054  Ergebnis = 2 − (jemand gefragt)
///        @0x40609C  NUR bei 2: alle gemerkten Zellen ueberfahren (Ziel, d−1, d+1)
/// </code>
/// <para>Die Flanken sind die zwei geraden Nachbarn, die die Ecke
/// einschliessen. Auf ihnen gilt: Feind-Fahrzeug, Wasser, gesperrt,
/// Gebaeude/Objekt -> 0; eigene Einheit -> gebeten (0x404D20); <b>rau ist
/// erlaubt</b> (@0x405F30/@0x406011), obwohl es als Ziel sperrt — dieselbe
/// Dreiwerte-Regel wie im Kartenbauer 0x4D1216. Hover (0x4057D8) nimmt auf der
/// Flanke auch Wasser; Walker (0x405A50) und Fussvolk (0x406223) pruefen
/// dieselben Flanken ohne zu ueberfahren, und Fussvolk wird auf einer Flanke von
/// JEDEM Fahrzeug gesperrt, auch einem eigenen (@0x406337).</para>
///
/// <para>⚠ UNSERE NAEHERUNGEN: eine volle Neunerzelle gibt es bei uns nicht —
/// eine Infanteriezelle hat fuer Fussvolk immer »Platz«. Klasse 3 (vier
/// Rumpfzellen, @0x406419) ist nicht nachgebaut. Und die zweite Anfrage an eine
/// Einheit, die schon ausweicht (@0x404DD4), bleibt wie in Ausweichen.cs.</para>
///
/// <para>Gegenschalter <c>--ueberfahren-nur-ziel</c>: der Schritt fragt wieder nur
/// das Ziel. Pruefstand <c>--schraegschritt-check</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--ueberfahren-nur-ziel</c> — der Stand vor dem 11.09.2026.</summary>
    public static bool UeberfahrenNurZiel;

    private static int RichtungVon(int dx, int dy)
    {
        for (int k = 0; k < 8; k++)
            if (Simulation.NavGrid.UrDirs[k].X == dx && Simulation.NavGrid.UrDirs[k].Y == dy) return k;
        return -1;
    }

    /// <summary>
    /// Das Urteil ueber EINE Flanke — die Tafel aus Abschnitt F1 des Berichts.
    /// <paramref name="fuss"/> ist der Soldat, der bei Erfolg ueberfahren wird
    /// (nur Raeder/Ketten), sonst −1.
    /// </summary>
    private Step FlankeUrteil(int i, Entity e, int c, int r, out int fuss)
    {
        fuss = -1;
        if (_nav == null || !_nav.InBounds(c, r) || _nav.IstTuerGesperrt(c, r)) return Step.Blocked;
        int occ = _nav.OccupantAt(c, r);
        if (occ >= 0 && occ != i && occ < _entities.Count && !_entities[occ].Dead)
        {
            var o = _entities[occ];
            if (_nav.CrushableAt(c, r, i) == occ)
            {
                // Fussvolk auf der Flanke
                if (e.Infantry >= 0) return Step.Free;                  // 0x433DF0: Platz
                var u = FussvolkImWeg(i, e, o);                         // K2-Weiche
                if (u == Step.Free && e.Move == MoveClass.Vehicle) fuss = occ;
                return u;
            }
            if (_nav.IsImmobileAt(c, r) || o.IsBuilding || o.IsProp) return Step.Blocked;  // >= 14000
            if (e.Infantry >= 0) return Step.Blocked;                   // @0x406337: jedes Fahrzeug
            return IsHostile(e, o) ? Step.Blocked : Step.GiveWay;       // pratelsky 0x4054D0
        }
        return _nav.GroundAt(c, r) switch
        {
            Ground.Free or Ground.Rough => Step.Free,                   // @0x405F30: rau erlaubt
            Ground.Water => e.Move == MoveClass.Hover ? Step.Free : Step.Blocked,   // @0x405897
            _ => Step.Blocked,
        };
    }

    /// <summary>
    /// Der Schraegschritt nach <c>Can_go</c>. Gibt <c>true</c> zurueck, wenn der
    /// Schritt beginnen darf (dann ist das Ueberfahren schon geschehen); sonst
    /// ist der Takt ueber den Geduld- oder Wartezweig erledigt.
    /// </summary>
    private bool SchraegschrittFrei(int i, Entity e, Vector2I next, Step zielSay)
    {
        int d = RichtungVon(next.X - e.Col, next.Y - e.Row);
        if (d < 0 || _nav == null) return zielSay == Step.Free;

        // das Ziel: was Ask sagt, und ein Fusssoldat darauf nach K2
        int zielFuss = -1;
        var zielUrteil = zielSay;
        if (zielUrteil == Step.Free)
        {
            int foot = _nav.CrushableAt(next.X, next.Y, i);
            if (foot >= 0)
            {
                zielUrteil = FussvolkImWeg(i, e, _entities[foot]);
                if (zielUrteil == Step.Free && e.Infantry < 0) zielFuss = foot;
            }
        }

        bool gefragt = false;
        var merk = new List<int>(3);
        for (int k = 0; k < 3; k++)
        {
            Vector2I z;
            Step u;
            int f;
            if (k == 0) { z = next; u = zielUrteil; f = zielFuss; }
            else
            {
                var off = Simulation.NavGrid.UrDirs[k == 1 ? (d + 7) & 7 : (d + 1) & 7];
                z = new Vector2I(e.Col + off.X, e.Row + off.Y);
                u = FlankeUrteil(i, e, z.X, z.Y, out f);
            }

            if (u == Step.Blocked)
            {
                SchraegZaehlen(i, 1);
                Fahrgrund(e, $"Schraegschritt nach ({next.X},{next.Y}): "
                           + $"{(k == 0 ? "Ziel" : k == 1 ? "Flanke d-1" : "Flanke d+1")} ({z.X},{z.Y}) sperrt");
                GeduldZweig(i, e);
                return false;
            }
            if (u == Step.GiveWay)
            {
                SchraegZaehlen(i, 0);
                int wer = _nav.OccupantAt(z.X, z.Y);
                if (AusweichenAn && wer >= 0 && wer != i && !AusweichenAnfragen(i, wer, d))
                {
                    Fahrgrund(e, $"Nr. {wer} auf ({z.X},{z.Y}) weicht NICHT aus — Geduld {e.Block}");
                    GeduldZweig(i, e);                  // Can_go = 0
                    return false;
                }
                gefragt = true;
                if (k == 2) break;                      // @0x40616B: Flanke B gibt sofort 1
                continue;
            }
            if (f >= 0) merk.Add(f);
        }

        if (gefragt) { WarteZweig(i, e); return false; }   // Can_go = 1
        SchraegZaehlen(i, 2);
        foreach (int f in merk) RunOverFoot(i, e, f);       // @0x4060B2: Ziel, d−1, d+1
        return true;
    }

    private void SchraegZaehlen(int i, int wert)
    {
        if (!_sCheckAn) return;
        if (!_sZaehler.TryGetValue(i, out var z)) { z = new int[3]; _sZaehler[i] = z; }
        z[wert]++;
    }
}
