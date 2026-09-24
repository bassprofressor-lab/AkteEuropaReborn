namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 24.09.2026 — <b>die Maustasten wie im Original</b> (seine Entscheidung:
/// »mach wie im original, mit schalter«).
///
/// <para>Beleg (berichte/gleisreparatur-klick-fable.md, C und F gleich): jeder
/// Zielbefehl auf der Karte ist im Original der LINKSKLICK beim Loslassen
/// (WM_LBUTTONUP 0x414119 -> 0x414182 setzt 0x502AB8 -> Verteiler 0x437060, der
/// allein nach der Zeigerart 0x502AD4 verteilt). Die rechte Taste gibt nie einen
/// Zielbefehl: 0x205 -> 0x414409 hebt eine Befehlsart auf (0x502ACC := 0
/// @0x414469) oder waehlt ab (0x433010 @0x4144CC); gehalten rollt sie die Karte.</para>
///
/// <para>⚠ NICHT GEBAUT: der Zweig @0x41449E (Rechtsklick, gewaehlte Einheit mit
/// Auftrag +0x14 == 1 -> Befehl 5) — ungedeutet. Gegenschalter <c>--maus-alt</c>
/// (MapViewer.MausAlt): links waehlen, rechts befehlen, wie bis zum 23.09.</para>
///
/// <para>Hier nur die Hilfen fuer den Pruefstand <c>--maus-check</c>
/// (MapViewer.MausLauf), der ECHTE Mausereignisse an die Karte schickt.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Zwei eigene Landfahrzeuge und eine freie Zelle vier Felder neben
    /// dem ersten. Null, wenn die Karte das nicht hergibt.</summary>
    public (int a, int b, Vector2 posA, Vector2 posB, Vector2I frei, Vector2 posFrei)? MausProbeAufbau()
    {
        int a = -1, b = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || !e.Mobile || e.Owner != ViewPlayer) continue;
            if (e.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
            if (a < 0) a = i; else { b = i; break; }
        }
        if (a < 0 || b < 0 || _nav == null) return null;
        var ea = _entities[a];
        Vector2I? frei = null;
        foreach (var d in new[] { new Vector2I(4, 0), new Vector2I(-4, 0), new Vector2I(0, 4), new Vector2I(0, -4) })
        {
            var z = new Vector2I(ea.Col + d.X, ea.Row + d.Y);
            if (!_nav.CanEnter(z.X, z.Y, Simulation.NavGrid.MoveClass.Vehicle)) continue;
            if (Pick(CellCenter(z.X, z.Y)) >= 0) continue;   // es muss LEERES Gelaende sein
            frei = z; break;
        }
        if (frei == null) return null;
        return (a, b, CellCenter(ea.Col, ea.Row), _entities[b].Pos, frei.Value,
                CellCenter(frei.Value.X, frei.Value.Y));
    }

    /// <summary>Wohin will die Einheit? (Goal)</summary>
    public Vector2I MausProbeZiel(int i) => _entities[i].Goal;

    /// <summary>Die Einheit bleibt stehen, wo sie ist (Ziel = eigene Zelle).</summary>
    public void MausProbeHalt(int i)
    {
        var e = _entities[i];
        e.Path = null; e.Goal = new Vector2I(e.Col, e.Row);
    }

    public void MausProbeWaehle(int i) { _sel.Clear(); _sel.Add(i); _selected = i; SetPrimary(); }
}
