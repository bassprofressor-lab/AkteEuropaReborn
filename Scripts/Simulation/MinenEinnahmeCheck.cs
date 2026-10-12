namespace AkteEuropaReborn.Rendering;

using System.Linq;
using Godot;

/// <summary>
/// <b>--minen-einnahme-check — kommt der fremde Wagen aus der eingenommenen Mine?</b>
/// (12.10.2026, gemeldet in K27: »Ich musste eine Mine zerstoeren, weil dort noch
/// ein feindlicher Transporter drin stand« — eingenommen, Sieg hing daran).
///
/// <para>Soll laut berichte/einnahme-transporter-fable.md §3.2: ein Wagen, der bei
/// der Einnahme in Fabrik/Mine steht (UKOL 54), kommt nach den 100 Takten einmal
/// leer heraus (53), faehrt 3..5 Zeilen nach Sueden und steht.</para>
///
/// <para>Ablauf: laufen lassen, bis ein fremder Wagen IN einer fremden Mine steht
/// (UKOL 54 mit Fahrziel = Mine, oder <c>InGebaeude</c> = Mine), dann die Mine fuer
/// den Spieler einnehmen (<c>CaptureDone</c>) und 30 s weiterlaufen. Bestanden,
/// wenn der Wagen danach draussen auf der Karte steht.</para>
///
/// <para>⚠ EINGRIFF: ohne Schalter wird die Einnahme ohne Belagerer abgeschlossen.
/// <c>--minen-check-belagert</c>: ein Landfahrzeug wird dem Spieler zugeschrieben,
/// auf (Tuer0.X, Tuer0.Y+1) gestellt, die Einnahme laeuft echt, nach 20 s faehrt
/// es weg. <c>--minen-check-platz=N</c> nur diese Mine, <c>--minen-check-liste</c>
/// nur die Ausgaenge aller Minen.</para>
///
/// <para>Stand 12.10.2026 (bug-498, OFFEN), K27 Seed 7, Mine 25: ohne Belagerer
/// BESTANDEN; belagert geht der Wagen auf UKOL 53 (unsichtbar) und ist gesperrt,
/// solange die Zelle vor der Tuer belegt ist — faehrt der Belagerer weg, kommt er
/// heraus. Beim Spieler starb Wagen 1003 aber auf (181,47), NEBEN der Tuer
/// (182,46): dieser Zustand ist noch nicht nachgestellt.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--minen-check-belagert</c>: eigene Fahrzeuge vor jede Tuer stellen.</summary>
    public static bool MinenCheckBelagert;
    /// <summary><c>--minen-check-liste</c>: nur alle Minen und ihre Ausgaenge, dann laufen lassen.</summary>
    public static bool MinenNurListe;
    /// <summary><c>--minen-check-platz=N</c>: nur diese Mine.</summary>
    public static int MinenCheckPlatz = -1;
    private int _belagerer = -1;

    public string MinenEinnahmeCheck(int warteSekunden)
    {
        var sb = new System.Text.StringBuilder(
            $"minen-einnahme-check K{UI.SkirmishSetup.CampaignMission}, Spieler {ViewPlayer}\n");

        Entity? DrinIn(Entity u)
        {
            if (u.InGebaeude is { } g && IsMine(g)) return g;
            if (u.Ukol is UkolImGebaeude or UkolAusgang && RouteVon(u) is { } r)
            {
                var b = GebaeudePlatz(r.Fahrziel);
                if (b != null && IsMine(b)) return b;
            }
            return null;
        }

        string MinenListe()
        {
            var s = new System.Text.StringBuilder();
            for (int mi = 0; mi < _entities.Count; mi++)
            {
                var m = _entities[mi];
                if (!IsMine(m) || m.IsProp || m.Dead || m.DoorCells.Count == 0 || _nav == null) continue;
                int ax = m.Col + m.DoorCells[^1].Col, ay = m.Row + m.DoorCells[^1].Row;
                bool frei = _nav.ImapGanzFrei(ax, ay + 1, Simulation.NavGrid.MoveClass.Vehicle);
                int drin = _entities.Count(u => !u.Dead && !u.IsBuilding && u.Ukol is UkolImGebaeude or UkolAusgang
                                               && RouteVon(u) is { } r && r.Fahrziel == m.Slot);
                s.AppendLine($"    Mine Platz {m.Slot} Spieler {m.Owner} Anker ({m.Col},{m.Row}) Ausgang ({ax},{ay}) darunter " +
                             (frei ? "frei" : $"GESPERRT: {(_nav.BesetztVon(ax, ay + 1) >= 0 ? $"Nr. {_nav.BesetztVon(ax, ay + 1)}" : _nav.WarumGesperrt(ax, ay + 1, Simulation.NavGrid.MoveClass.Vehicle, -1))}") +
                             $", Wagen drin {drin}");
            }
            return s.ToString();
        }
        sb.AppendLine($"  Karte {_nav?.Width}x{_nav?.Height}, Minen beim Start:");
        sb.Append(MinenListe());
        if (MinenNurListe)
        {
            int gesp0 = RouteAusgangGesperrt;
            for (int t = 1; t <= warteSekunden * SimHz; t++) SimTickFuerProbe();
            sb.AppendLine($"  nach {warteSekunden} s (Ausgang gesperrt {RouteAusgangGesperrt - gesp0} Takte, Grund: {RouteAusgangGrund}):");
            sb.Append(MinenListe());
            return sb.ToString();
        }
        int wi = -1, w = 0;
        Entity? mine = null;
        for (; w < warteSekunden * SimHz && wi < 0; w++)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                var u = _entities[i];
                if (u.Dead || u.IsBuilding || u.IsProp || u.Owner == ViewPlayer) continue;
                var g = DrinIn(u);
                if (g == null || g.Dead || g.Owner == ViewPlayer) continue;
                if (MinenCheckPlatz >= 0 && g.Slot != MinenCheckPlatz) continue;
                wi = i; mine = g; break;
            }
            if (wi < 0) SimTickFuerProbe();
        }
        if (wi < 0 || mine == null)
            return sb.Append($"  nach {w / SimHz} s kein fremder Wagen in einer fremden Mine — ungeprueft\n  DURCHGEFALLEN").ToString();

        var wagen = _entities[wi];
        string Zustand() =>
            $"UKOL {wagen.Ukol}, InGebaeude {(wagen.InGebaeude == null ? "-" : wagen.InGebaeude.Slot.ToString())}, " +
            $"Zelle ({wagen.Col},{wagen.Row}), Route {(RouteVon(wagen) is { } r ? $"Ziel {r.Ziel} Fahrziel {r.Fahrziel} gestartet {r.Gestartet} Warten {r.Warten}" : "keine")}, " +
            $"tot {wagen.Dead}";
        sb.AppendLine($"  gewartet {w / SimHz} s: Wagen Platz {wagen.Slot} (Spieler {wagen.Owner}) in Mine Platz {mine.Slot} " +
                      $"(Spieler {mine.Owner}, Art {mine.BType}, Garage {mine.Garage.Count})");
        sb.AppendLine($"  vor der Einnahme: {Zustand()}");
        sb.AppendLine($"  Mine Anker ({mine.Col},{mine.Row}), Tueren " + string.Join(" ", mine.DoorCells.Select(d => $"({mine.Col + d.Col},{mine.Row + d.Row})")));
        // Variante =belagert: eigene Fahrzeuge auf die Zellen vor JEDER Tuer, wie
        // nach einer echten Einnahme (Belagerer stehen noch da).
        if (MinenCheckBelagert && _nav != null && mine.DoorCells.Count > 0)
        {
            // ECHTE Einnahme: ein eigenes Fahrzeug auf (Tuer0.X, Tuer0.Y+1), der
            // Einnahme-Arm laeuft von selbst, bis die Mine uns gehoert.
            int x = mine.Col + mine.DoorCells[0].Col, y = mine.Row + mine.DoorCells[0].Row + 1;
            int besetzt = _nav.OccupantAt(x, y);
            sb.AppendLine($"  Belagerer-Zelle ({x},{y}) besetzt von {besetzt}");
            // K27: der Spieler hat dann keine freie Einheit — ein beliebiges
            // Landfahrzeug wird ihm zugeschrieben (Eingriff, nur Pruefstand).
            int ei = Enumerable.Range(0, _entities.Count).FirstOrDefault(i => { var q = _entities[i];
                return !q.Dead && !q.IsBuilding && !q.IsProp && i != wi && q.InGebaeude == null && q.Infantry < 0
                    && q.Move == Simulation.NavGrid.MoveClass.Vehicle && q.Ukol == UkolFrei && RouteVon(q) == null; }, -1);
            if (ei >= 0) { var q = _entities[ei]; sb.AppendLine($"  Belagerer umgeschrieben von Spieler {q.Owner}"); q.Owner = q.Team = q.ShownOwner = ViewPlayer; }
            if (ei < 0 || besetzt >= 0) return sb.Append("  kein Belagerer setzbar\n  DURCHGEFALLEN").ToString();
            var e = _entities[ei];
            sb.AppendLine($"  Belagerer: Platz {e.Slot} {LabelOf(e)} Bewegung {e.Move}");
            _nav.ClearOccupant(e.Col, e.Row, ei);
            if (e.Reserved is { } rv) _nav.ClearOccupant(rv.X, rv.Y, ei);
            e.Reserved = null; e.Col = x; e.Row = y; e.Path = null; e.Orders.Clear(); e.Target = -1; e.Hp = e.HpMax * 10;
            e.Elev = ElevOf(x, y); e.Pos = CellCenter(x, y); e.Footprint = CellRect(_ox, _oy, x, y, e.Elev);
            _nav.SetOccupant(x, y, ei, false);
            int t0 = 0;
            for (; t0 < 300 * SimHz && mine.Owner != ViewPlayer; t0++)
            {
                SimTickFuerProbe();
                if (t0 % (10 * SimHz) == 0) sb.AppendLine($"  belagert +{t0 / SimHz,3} s: Einnahme {mine.CaptureProgress}/{mine.CaptureTotal}, Wagen {Zustand()}");
            }
            sb.AppendLine($"  Mine eingenommen nach {t0 / SimHz} s: Besitzer {mine.Owner}, Belagerer jetzt ({e.Col},{e.Row})");
            sb.AppendLine($"  Vorplatzraeumung: gefunden {VorplatzGefunden}, geschickt {VorplatzGeschickt}");
            _belagerer = ei;
        }
        else
            CaptureDone(_entities.IndexOf(mine), mine, ViewPlayer);
        int gesperrtVor = RouteAusgangGesperrt;
        bool draussen = false;
        for (int t = 1; t <= 60 * SimHz; t++)
        {
            SimTickFuerProbe();
            if (t == 20 * SimHz && _belagerer >= 0 && _nav != null)
            {
                // wie der Spieler: die Belagerer fahren nach der Einnahme weg
                var e = _entities[_belagerer];
                var ziel = _nav.NearestFree(new Vector2I(e.Col, Mathf.Min(e.Row + 10, _nav.Height - 1)), e.Move, _belagerer);
                var weg = ziel == null ? null : _nav.FindPath(new Vector2I(e.Col, e.Row), ziel.Value, e.Move, _belagerer);
                if (weg is { Count: > 0 }) { e.Path = weg; e.PathIdx = 0; e.Goal = ziel!.Value; e.StepCost = 0; }
                sb.AppendLine($"  +20 s: Belagerer weggeschickt nach {ziel} ({(weg?.Count ?? 0)} Schritte)");
            }
            if (t == 30 * SimHz && _belagerer >= 0)
            {
                var e = _entities[_belagerer];
                sb.AppendLine($"  +30 s: Belagerer bei ({e.Col},{e.Row}), Zelle (182,47) im Gitter besetzt von {_nav?.OccupantAt(182, 47)}");
            }
            if (t % (10 * SimHz) == 0) sb.AppendLine($"  +{t / SimHz,2} s: {Zustand()}");
            draussen = !wagen.Dead && wagen.InGebaeude == null && wagen.Ukol is not (UkolImGebaeude or UkolAusgang);
        }
        sb.AppendLine($"  Ausgang gesperrt {RouteAusgangGesperrt - gesperrtVor} Takte{(RouteAusgangGrund.Length > 0 ? $", erster Grund: {RouteAusgangGrund}" : "")}");
        sb.Append(draussen ? "  BESTANDEN (Wagen steht draussen)" : "  DURCHGEFALLEN (Wagen noch drin)");
        return sb.ToString();
    }
}
