namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--nebelrest-check</c> — <b>bleibt Nebel auf einem aufgedeckten Gebäude
/// liegen?</b> (10.09.2026, bug-166/173.)
///
/// <para>⚠⚠ Seine Meldung: »die bunker zeigen immer noch teils grass anstatt
/// der korrekten zerstoerten bunkergrafik«. Die Ursache war nicht der Zeichner,
/// sondern der NEBEL: <c>bug-164</c> hat nur den LESERIEGEL gebaut (ein
/// Gebaeude gilt als aufgedeckt, sobald EINE seiner Zellen gesehen ist), aber
/// nicht den SCHREIBWEG. Das Original ruft aus dem Stempler (@0x420272) und dem
/// Saum (@0x41FFEF) die Routine <c>0x41FE20</c>, und die schreibt den GANZEN
/// Fussabdruck ins Gedaechtnis.</para>
///
/// <para>Folge: der Koerper wird ganz gezeichnet (der Riegel ist ja offen),
/// waehrend die Nebeldecke auf jede nie gesehene Zelle ihre Graskachel legt.
/// Beim heilen Bunker faellt das nicht auf — seine Randkacheln SIND Wiese.
/// Bei der Ruine ist der Rand Schutt, und das Gras steht mitten darin.</para>
///
/// <para><b>Warum es einen eigenen Stand braucht:</b> beim Missionsstart ist
/// kein fremdes Gebaeude aufgedeckt (13 von 13 verborgen), der Fall entsteht
/// erst beim Erkunden. <c>--gebaeudeklick-check</c> und <c>--ruin-check</c>
/// messen ohne Nebel und koennen ihn grundsaetzlich nicht sehen. Dieser Stand
/// stellt ihn her: er merkt EINE Zelle jedes Gebaeudes als gesehen — also
/// genau die Teilsicht, die ein Spieler vom Rand her hat — und zaehlt danach,
/// wie viele Zellen des Fussabdrucks noch im Nebel liegen.</para>
///
/// <para>Nullmodell <c>--gebaeude-nur-lesen</c>: dort muss der Rest stehen
/// bleiben.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string NebelrestCheck()
    {
        var sb = new System.Text.StringBuilder("nebelrest-check\n");
        if (_fog == null) return sb.Append("  kein Nebel geladen — der Lauf sagt NICHTS").ToString();

        int gebaeude = 0, restZellen = 0, mitRest = 0;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp || e.Dead || e.BildArt <= 0) continue;
            int w = Mathf.Max(1, e.FootW), h = Mathf.Max(1, e.FootH);

            // ⚠ DEN FALL HERSTELLEN: eine einzelne Zelle sehen, so wie ein
            // Spieler, der von einer Seite herankommt. Die MITTE waere zu
            // guenstig — der Rand ist der Fall, der im Spiel auftritt.
            _fog.Merken(e.Col, e.Row);
            e.Aufgedeckt = false;                 // den Zwischenspeicher zuruecksetzen
            if (!GebaeudeAufgedeckt(e)) continue; // gilt es ueberhaupt als aufgedeckt?

            gebaeude++;
            int offen = 0;
            for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < h; dy++)
                    if (!_fog.IsSeen(e.Col + dx, e.Row + dy)) offen++;
            restZellen += offen;
            if (offen > 0)
            {
                mitRest++;
                sb.Append($"  Platz {e.Slot,3} Art {e.BildArt,3} ({e.Col},{e.Row}) "
                        + $"Grundriss {w}x{h}: {offen} von {w * h} Zellen noch im Nebel "
                        + "⚠ dort legt die Nebeldecke ihre Graskachel\n");
            }
        }

        sb.Append($"  {gebaeude} Gebaeude aufgedeckt, {mitRest} davon mit Nebelrest, "
                + $"{restZellen} Zellen insgesamt\n");
        sb.Append($"  Gegenschalter --gebaeude-nur-lesen: {GebaeudeNurLesen}\n");
        sb.Append(gebaeude == 0 ? "  ⚠ kein Gebaeude aufgedeckt — der Lauf sagt NICHTS"
                  : restZellen == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
