using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <c>--schiffstart-probe</c> — <b>STEHT EIN SCHIFF ÜBERHAUPT IM WASSER, UND
/// KANN ES EINEN SCHRITT TUN?</b>
///
/// <para>⚠ <b>Seine Meldung vom 07.09.2026 (Kampagne 5):</b> »von meinen 4
/// Start-Transportern kann ich nur 1nen Bewegen, die anderen 3 machen
/// garnix.«</para>
///
/// <para><b>Der Verdacht, den der Ladelauf schon nennt:</b> drei der vier
/// Frachter (Rumpf 153) kommen als <c>1x1</c> aus der Kartendatei und werden
/// nach der Gattung auf <c>2x2</c> gesetzt (<c>NavGrid.HullSide</c>, aus
/// <c>Can_go</c> gelesen). Wenn die Karte sie aber an einer Stelle abgelegt
/// hat, an der nur EINE Zelle Wasser ist, dann steht das aufgeblasene Schiff
/// mit einer Ecke an Land — und ein Schiff, dessen eigenes Feld nicht
/// befahrbar ist, kann keinen Weg beginnen.</para>
///
/// <para>⚠ Der Lauf misst deshalb ZWEI Dinge getrennt, denn sie haben
/// verschiedene Ursachen: <b>steht</b> es gültig (alle Zellen seines
/// Grundrisses befahrbar), und <b>kann</b> es weg (wie viele der acht
/// Nachbarplätze nimmt es auf). Ein Schiff, das gültig steht und trotzdem
/// keinen Nachbarn hat, liegt in einer zu engen Bucht — das wäre eine Auskunft
/// über die Karte. Ein Schiff, das schon ungültig STEHT, ist unser Fehler.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string SchiffStartProbe()
    {
        var sb = new System.Text.StringBuilder("schiffstart-probe\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();

        int gezaehlt = 0, stehtFalsch = 0, eingeklemmt = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var s = _entities[i];
            if (s.IsBuilding || s.IsProp || s.Dead) continue;
            if (s.GameUnitType is not (4 or 5)) continue;      // nur Schiffe
            if (s.Owner != ViewPlayer) continue;
            gezaehlt++;

            int seite = Mathf.Max(1, s.FootW);
            // 1. Steht es gueltig? Jede Zelle seines eigenen Rechtecks.
            int frei = 0, gesamt = 0;
            for (int dc = 0; dc < seite; dc++)
                for (int dr = 0; dr < seite; dr++)
                {
                    gesamt++;
                    if (_nav.CanEnter(s.Col + dc, s.Row + dr, Simulation.NavGrid.MoveClass.Ship)) frei++;
                }
            bool stehtOk = frei == gesamt;
            if (!stehtOk) stehtFalsch++;

            // 2. Kann es weg? Ein Schritt in jede der acht Richtungen, wieder
            // mit dem GANZEN Rechteck geprueft — so, wie die Wegsuche es tut.
            int wege = 0;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    bool geht = true;
                    for (int dc = 0; dc < seite && geht; dc++)
                        for (int dr = 0; dr < seite && geht; dr++)
                            if (!_nav.CanEnter(s.Col + dx + dc, s.Row + dy + dr,
                                               Simulation.NavGrid.MoveClass.Ship)) geht = false;
                    if (geht) wege++;
                }
            if (stehtOk && wege == 0) eingeklemmt++;

            // ⭐ 07.09.2026 — WELCHE ECKE STIMMT? Steht das Rechteck nicht im
            // Wasser, ist die Frage, ob es an der falschen Ecke aufgeblasen
            // wurde: die Kartenzelle koennte die untere/rechte statt der
            // oberen/linken sein. Vier Ausrichtungen, und wenn genau EINE
            // passt, ist die Deutung entschieden statt geraten.
            string ecken = "";
            if (!stehtOk && seite > 1)
            {
                var namen = new[] { "oben-links (jetzt)", "oben-rechts", "unten-links", "unten-rechts" };
                var vx = new[] { 0, -(seite - 1), 0, -(seite - 1) };
                var vy = new[] { 0, 0, -(seite - 1), -(seite - 1) };
                for (int k = 0; k < 4; k++)
                {
                    int ok = 0;
                    for (int dc = 0; dc < seite; dc++)
                        for (int dr = 0; dr < seite; dr++)
                            if (_nav.CanEnter(s.Col + vx[k] + dc, s.Row + vy[k] + dr,
                                              Simulation.NavGrid.MoveClass.Ship)) ok++;
                    ecken += $"  {namen[k]}: {ok}/{gesamt}";
                }
                // ⭐ Und WAS liegt dort? Ohne die Gelaendearten ist »passt
                // nicht« nicht von »unser Wasser endet zu frueh« zu trennen.
                ecken += System.Environment.NewLine + "      Gelaende 3x3 um die Zelle: ";
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                        ecken += _nav.InBounds(s.Col + dc, s.Row + dr)
                            ? _nav.GroundAt(s.Col + dc, s.Row + dr) switch
                              {
                                  Simulation.NavGrid.Ground.Water => "W",
                                  Simulation.NavGrid.Ground.Free => ".",
                                  Simulation.NavGrid.Ground.Rough => "r",
                                  _ => "#",
                              }
                            : "?";
                    ecken += dr < 1 ? " / " : "";
                }
            }

            // ⭐ Und wie es HEISST — seine Meldung D (»alle meine Schiffe heisen
            // Fortified«). Der Name kommt seit dem 07.09.2026 aus der
            // Schiffstafel ueber den Rumpf, nicht aus dem Einheitenkatalog.
            sb.AppendLine($"  Platz {s.Slot} »{LabelOf(s)}« Rumpf {s.UnitType} ({seite}x{seite}) auf "
                        + $"({s.Col},{s.Row}): eigenes Feld {frei}/{gesamt} befahrbar"
                        + (stehtOk ? "" : "  ⚠ STEHT AN LAND")
                        + $", {wege} von 8 Nachbarplaetzen frei"
                        + (stehtOk && wege == 0 ? "  ⚠ eingeklemmt" : "")
                        + (ecken.Length > 0 ? System.Environment.NewLine + "      Ausrichtung:" + ecken : ""));
        }

        if (gezaehlt == 0) return sb.Append("  kein eigenes Schiff auf dieser Karte").ToString();
        sb.AppendLine($"  {gezaehlt} eigene Schiffe: {stehtFalsch} stehen mit mindestens einer "
                    + $"Ecke an Land, {eingeklemmt} stehen gueltig und kommen trotzdem nicht weg.");
        // ⚠ Der Lauf faellt NUR durch, wenn ein Schiff ungueltig STEHT. Eine
        // enge Bucht ist eine Auskunft ueber die Karte, kein Fehler von uns —
        // dieselbe Unterscheidung, die schon der Schiffstau-Check trifft.
        sb.Append(stehtFalsch == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    public int SchiffStartProbeRc() => SchiffStartProbe().Contains("BESTANDEN") ? 0 : 1;
}
