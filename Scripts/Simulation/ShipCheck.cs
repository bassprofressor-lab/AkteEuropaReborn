namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <c>--ship-check</c> — WAS EIN SCHIFF BELEGT, UND WAS ES BELEGEN MUESSTE.
///
/// <para><b>Der Befund, den dieser Prüfstand festhält (14.08.2026).</b> Das
/// Original prüft für ein Schiff den GANZEN RUMPF, wir prüfen eine einzige
/// Zelle. Beides ist gelesen, nicht vermutet:</para>
///
/// <para><b>1. Wie das Original es tut.</b> <c>Can_go</c> @0x4055D0 verteilt auf
/// <c>byte[+0x0A]</c> — die Zeile, die das Spiel selbst <c>»unit type:«</c>
/// nennt (Formatzeile @0x4F6734) — über die Sprungtafel @0x40678C mit sechs
/// Einträgen für die Werte 0..5. Zwei davon sind die Schiffe, und beide
/// verlangen in der imap den Wert <b>0xFFFC</b> (Wasser) oder sich selbst:</para>
/// <code>
///   unit type 4  @0x406669  prueft VIER Zellen:  (c,r) (c+1,r) (c,r+1) (c+1,r+1)
///                           also einen 2x2-Block
///   unit type 5  @0x40671B  prueft SECHZEHN:     zwei Schleifen zu je vier
///                           (+0x200 je Spalte, +2 je Zeile) — ein 4x4-Block
/// </code>
/// <para>⚠ Die Gattung <b>2</b> zeigt in derselben Tafel auf den FEHLERzweig
/// @0x40569D — sie ist unbenutzt. Die Karten bestätigen das unabhängig: über
/// alle 29 gelieferten Karten kommt <c>subclass 2</c> <b>kein einziges Mal</b>
/// vor (0 von 4474 Einheiten).</para>
///
/// <para><b>2. Was die Karten sagen.</b> Über die 29 gelieferten Karten:
/// <b>172</b> Einheiten mit <c>subclass 4</c> und <b>38</b> mit <c>subclass 5</c>.
/// Ihre Grundflächen decken sich Zelle für Zelle mit den zwei Zweigen:
/// <b>2x2 in 163 von 163</b> auflösbaren Fällen für Gattung 4, <b>4x4 in 32 von
/// 32</b> für Gattung 5. Und <b>193 von 210</b> Schiffen (91,9 %) liegen mit
/// dem GANZEN Rumpf auf Wasser — die Karten befolgen die Rumpfregel also
/// selbst.</para>
///
/// <para><b>3. Was WIR tun.</b> <see cref="Simulation.NavGrid.CanEnter"/> und
/// <see cref="Simulation.NavGrid.IsFree"/> nehmen überhaupt keinen Grundriss
/// entgegen — sie prüfen <c>(c,r)</c> und sonst nichts. Und
/// <c>SetOccupant(e.Col, e.Row, …)</c> stempelt genau eine Zelle. Ein
/// Schlachtschiff mit 4x4-Rumpf belegt bei uns also <b>eine von sechzehn</b>
/// Zellen. Die einzige schiffsspezifische Zeile in <c>CanStep</c> überspringt
/// unseren eigenen Steigungstest (<c>MaxClimb</c>, dort selbst als UNSERE
/// Setzung markiert) — mit dem Rumpf hat sie nichts zu tun.</para>
///
/// <para><b>Warum hier gemessen und nicht behoben wird.</b> Die Behebung ändert
/// <c>NavGrid</c> an seiner breitesten Stelle (jeder Aufrufer von
/// <c>IsFree</c>/<c>CanEnter</c>/<c>SetOccupant</c>) und damit das Verhalten
/// jeder Einheit auf jeder Karte. Das ist keine Zeile, die man nebenbei
/// mitnimmt. Dieser Prüfstand gibt dem Befund die Zahl, an der die Behebung
/// sich messen lassen muss — vorher <b>1</b> belegte Zelle je Schiff, nachher
/// <b>4</b> bzw. <b>16</b>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die zwei Gattungen, die <c>Can_go</c> aufs Wasser lässt —
    /// <see cref="Simulation.NavGrid.ArtIsShip"/> sagt dasselbe, und beide
    /// stammen aus der Sprungtafel @0x40678C.</summary>
    private static bool ShipSubclass(int sub) => sub is 4 or 5;

    /// <summary>Die Rumpfgrösse, die das Original für diese Gattung prüft —
    /// GEMESSEN aus den zwei Zweigen und aus den Karten (163/163 bzw. 32/32).
    /// Sie ist die Messlatte, nicht der Grundriss, den die Karte mitbringt:
    /// weicht der ab, ist genau das der Befund.</summary>
    private static int HullSide(int sub) => sub == 5 ? 4 : 2;

    public string ShipCheckLine()
    {
        var sb = new StringBuilder();
        int ships = 0, hullWater = 0, hullCells = 0, waterCells = 0;
        int stamped = 0, footMatches = 0, footOther = 0;
        var sizes = new SortedDictionary<string, int>();
        var lines = new List<string>();

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (!ShipSubclass(e.Subclass)) continue;
            ships++;

            int side = HullSide(e.Subclass);
            int fw = Mathf.Max(1, e.FootW), fh = Mathf.Max(1, e.FootH);
            string key = $"subclass{e.Subclass}:{fw}x{fh}";
            sizes[key] = sizes.TryGetValue(key, out int n0) ? n0 + 1 : 1;
            if (fw == side && fh == side) footMatches++; else footOther++;

            // Das Gelaende unter dem Rumpf, wie das Original ihn prueft —
            // side x side ab der Satzzelle.
            int water = 0, cells = 0, mine = 0;
            for (int dy = 0; dy < side; dy++)
                for (int dx = 0; dx < side; dx++)
                {
                    int c = e.Col + dx, r = e.Row + dy;
                    cells++;
                    if (_nav != null && _nav.InBounds(c, r) &&
                        _nav.CanEnter(c, r, Simulation.NavGrid.MoveClass.Ship)) water++;
                    if (_nav != null && _nav.OccupantAt(c, r) == i) mine++;
                }
            hullCells += cells; waterCells += water; stamped += mine;
            if (water == cells) hullWater++;
            else if (lines.Count < 6)
                lines.Add($"Schiff {i} (subclass {e.Subclass}) auf ({e.Col},{e.Row}): " +
                          $"{water} von {cells} Rumpfzellen befahrbar");
        }

        if (ships == 0)
            return "ship-check: kein Schiff auf dieser Karte — " +
                   "der Zaehler kann hier NICHTS aussagen (Regel 30)";

        sb.Append($"ship-check: {ships} Schiffe, Grundflaechen {string.Join(" ", Fmt(sizes))}\n");
        sb.Append($"   Rumpf ganz befahrbar: {hullWater} von {ships}" +
                  $" — Messlatte aus den gelieferten Karten: 193 von 210 (91,9 %)\n");
        sb.Append($"   Grundriss der Karte == Rumpf des Originals: {footMatches}, abweichend {footOther}" +
                  " (gemessen 2x2 163/163 fuer subclass 4, 4x4 32/32 fuer subclass 5)\n");
        // ⚠ 14.08.2026 — DIESE ZEILE BEHAUPTETE FRUEHER IMMER DEN FEHLER.
        // Sie stammt aus dem Lauf, der ihn gefunden hat, und sagte auch dann
        // "MUSS gleich sein, ist es aber nicht", als es gleich WAR (map_DM_3
        // 104 von 104). Ein Prueftext, der auf jeder Karte dasselbe sagt,
        // prueft nichts mehr (Regel 30) — jetzt entscheidet die Zahl.
        sb.Append(stamped == hullCells
            ? $"   BELEGTE Zellen: {stamped} von {hullCells} Rumpfzellen — vollstaendig. " +
              "Der ganze Rumpf ist gestempelt,\n" +
              "     wie Can_go @0x4055D0 ihn prueft (4 Zellen bei subclass 4 @0x406669, " +
              "16 bei subclass 5 @0x40671B).\n" +
              "     Vor der Behebung war es EINE Zelle je Schiff."
            : $"   ⚠ BELEGTE Zellen: {stamped} von {hullCells} Rumpfzellen — es fehlen " +
              $"{hullCells - stamped}.\n" +
              "     Erwartet wird der ganze Rumpf (Can_go @0x4055D0: 4 Zellen bei subclass 4\n" +
              "     @0x406669, 16 bei subclass 5 @0x40671B). Fehlende Zellen heissen: dort\n" +
              "     kann ein zweites Schiff oder ein Landfahrzeug hineinfahren.\n" +
              "     ⚠ Ein Satz, dessen Grundriss in der KARTE 1x1 ist, obwohl seine Gattung\n" +
              "     einen Rumpf verlangt, faellt hier ebenfalls auf — das ist dann eine\n" +
              "     Aussage ueber die Karte, nicht ueber den Stempel.");
        foreach (string l in lines) sb.Append("\n   " + l);
        return sb.ToString();
    }

    private static IEnumerable<string> Fmt(SortedDictionary<string, int> d)
    {
        foreach (var kv in d) yield return $"{kv.Key}x{kv.Value}";
    }
}
