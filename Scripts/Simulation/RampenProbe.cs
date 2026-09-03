using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>`--rampen-probe` — DREHT SICH EINE EINHEIT AUF DER BRÜCKENRAMPE IM KREIS?</b>
///
/// <para>⚠ 03.09.2026, aus seinem Spiellauf der Kampagne 3 gemeldet:
/// »Einheiten die eine Brücke überfahren und sozusagen dort sind wo die
/// Schräge/Steigung drinne ist machen dort kurz wie einen Threesixty, sieht
/// lustig aus, fahren aber normal über die Brücke, der Effekt ist also immer
/// nur beim Auffahren bzw. Abfahren der Brücke.«</para>
///
/// <para><b>Warum eine eigene Probe.</b> Die Blickrichtung ist bei uns kein
/// Zustand, den man ablesen könnte — sie wird in JEDEM Takt aus
/// <c>dest − Pos</c> neu gerechnet (<see cref="MapEntityLayer.DirToFacing(Vector2)"/>),
/// und der Fahrer dreht dann eine Stufe je Takt darauf zu. Ein »Threesixty«
/// heisst also: die GEWÜNSCHTE Richtung springt auf der Rampe, und zwar so
/// weit, dass der kürzeste Weg um den Kreis mehrere Stufen lang ist. Das ist
/// mit blossem Nachrechnen nicht zu entscheiden, weil zwei Rechnungen
/// ineinandergreifen: die Höhenanhebung der ZELLMITTE
/// (<see cref="MapEntityLayer.HubOf"/>, in <c>dest</c>) und der laufende
/// Feinversatz der FAHRT (<see cref="MapEntityLayer.FahrtY"/>, in <c>Pos</c>).
/// Auf ebenem Gelände sind beide gleich; auf einer Rampe nicht.</para>
///
/// <para><b>Was gemessen wird.</b> Eine eigene fahrende Einheit wird vor eine
/// Rampenzelle gestellt und quer darüber geschickt. Je Takt werden Zelle und
/// Blickrichtung mitgeschrieben; gezählt wird, wie oft die Richtung WECHSELT
/// und wie gross der grösste Sprung dabei war.</para>
///
/// <para><b>Nullmodell — und es ist ein scharfes.</b> Derselbe Lauf mit
/// <c>--kein-hang</c> schaltet die ganze Schrägenrechnung ab (Höhe wirkt dann
/// gar nicht mehr auf die Bildlage). Kommt der Dreher DORT auch, liegt es
/// nicht an der Rampe, und die Probe hat die Ursache widerlegt statt sie zu
/// bestätigen. Ohne diese Gegenprobe ist eine Zahl hier nichts wert: eine
/// Einheit wechselt die Blickrichtung auch auf ebenem Gelände, sobald der Weg
/// eine Ecke macht.</para>
///
/// <para>⚠ Die Probe SETZT die Einheit um (auf die Zelle vor der Rampe). Das
/// ist zulässig, weil sie nichts über Wegsuche aussagen soll, sondern nur über
/// die Blickrichtung auf einer Schräge — aber es ist eine Setzung, und darum
/// steht sie hier.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private bool _rampenProbeLaeuft;
    private int _rampenSlot = -1;
    private int _rampenVonX, _rampenVonY, _rampenNachX, _rampenNachY;
    private int _rampenZelleX, _rampenZelleY;
    private readonly System.Collections.Generic.List<(int Takt, int Col, int Row, int Facing, int Art)>
        _rampenSpur = new();
    private int _rampenTakt;

    /// <summary>Die Rampenzelle, über die gefahren wird — die erste Zelle mit
    /// einer Schrägenart, deren beide Nachbarn in Fahrtrichtung befahrbar sind.
    /// Gesucht wird zuerst WAAGERECHT (Osten/Westen), denn genau die
    /// waagerechte Brücke hat er gemeldet.</summary>
    private bool RampeSuchen(out int cx, out int cy, out int dx, out int dy)
    {
        cx = cy = dx = dy = 0;
        if (_nav == null) return false;
        // Zwei Durchgänge: erst die Auf-/Abfahrten nach OSTEN (Art 1/3), dann
        // die nach SÜDEN (Art 2/4). Die Artnummern stehen in Simulation.Hang.
        foreach (var (rx, ry) in new[] { (1, 0), (0, 1) })
        {
            foreach (var ((c, r), _) in _elevLookup)
            {
                if (HangArt(c, r) == 0) continue;
                // drei Zellen davor und drei dahinter müssen befahrbar sein —
                // sonst kommt die Einheit gar nicht erst in Fahrt.
                bool frei = true;
                for (int k = -3; k <= 3 && frei; k++)
                {
                    int x = c + rx * k, y = r + ry * k;
                    if (!_nav.IsWalkable(x, y, Simulation.NavGrid.MoveClass.Vehicle)) frei = false;
                }
                if (!frei) continue;
                cx = c; cy = r; dx = rx; dy = ry;
                return true;
            }
        }
        return false;
    }

    public string RampenProbeStart()
    {
        if (_nav == null) return "rampen-probe: ⚠ keine Karte — die Probe misst NICHTS";
        if (!RampeSuchen(out int cx, out int cy, out int dx, out int dy))
            return "rampen-probe: ⚠ keine Rampenzelle mit freier An- und Abfahrt auf " +
                   "dieser Karte — die Probe misst NICHTS (map_02 / Kampagne 2 hat welche)";

        int i = _entities.FindIndex(e => !e.IsBuilding && !e.IsProp && !e.Dead && e.Mobile
                                         && e.Owner == 0 && e.Infantry < 0);
        if (i < 0)
            return "rampen-probe: ⚠ keine eigene FAHRENDE Einheit — die Probe misst NICHTS";

        var u = _entities[i];
        _rampenZelleX = cx; _rampenZelleY = cy;
        _rampenVonX = cx - dx * 3; _rampenVonY = cy - dy * 3;
        _rampenNachX = cx + dx * 3; _rampenNachY = cy + dy * 3;

        // umsetzen — mit Buchführung im Gitter, sonst bleibt der alte Platz belegt
        _nav.ClearOccupant(u.Col, u.Row, i);
        u.Col = _rampenVonX; u.Row = _rampenVonY;
        u.Elev = ElevOf(u.Col, u.Row);
        u.Pos = BodyCenterAt(u, u.Col, u.Row);
        u.Footprint = CellRect(_ox, _oy, u.Col, u.Row, u.Elev);
        u.Path = null; u.PathIdx = 0; u.Reserved = null; u.StepCost = 0; u.Progress = 0;
        _nav.SetOccupant(u.Col, u.Row, i, false);
        if (u.FuelMax > 0) u.Fuel = u.FuelMax;

        _rampenSlot = u.Slot;
        _rampenProbeLaeuft = true;
        _rampenTakt = 0;
        _rampenSpur.Clear();

        MissionOrderAt(u.Slot, _rampenNachX, _rampenNachY, -1);

        return $"rampen-probe: Rampenzelle ({cx},{cy}) Art {HangArt(cx, cy)} " +
               $"Hoehe {ElevOf(cx, cy)}, Fahrtrichtung ({dx},{dy})\n" +
               $"   Platz {u.Slot} \"{LabelOf(u)}\" von ({_rampenVonX},{_rampenVonY}) " +
               $"nach ({_rampenNachX},{_rampenNachY})\n" +
               $"   Schrägenrechnung: {(Simulation.Hang.Aus ? "AUS (--kein-hang, NULLMODELL)" : "an")}\n";
    }

    public void RampenProbeTick()
    {
        if (!_rampenProbeLaeuft) return;
        _rampenTakt++;
        var u = _entities.Find(e => e.Slot == _rampenSlot);
        if (u == null) return;
        // Nur die WECHSEL aufschreiben — eine Zeile je Takt wäre ein Roman, und
        // die Frage ist ohnehin nur, wann und wie weit die Richtung springt.
        if (_rampenSpur.Count == 0 || _rampenSpur[^1].Facing != u.Facing
                                   || _rampenSpur[^1].Col != u.Col || _rampenSpur[^1].Row != u.Row)
            _rampenSpur.Add((_rampenTakt, u.Col, u.Row, u.Facing, HangArt(u.Col, u.Row)));
    }

    public string RampenProbeLine()
    {
        if (!_rampenProbeLaeuft) return "rampen-probe: nicht gestartet";
        var sb = new System.Text.StringBuilder(
            $"rampen-probe: Rampenzelle ({_rampenZelleX},{_rampenZelleY}), " +
            $"Platz {_rampenSlot}, Schräge {(Simulation.Hang.Aus ? "AUS" : "an")}\n" +
            "   Takt  Zelle       Art  Blick            Sprung\n");

        int wechsel = 0, groesster = 0, wechselAufRampe = 0;
        int vorher = -1;
        foreach (var (takt, c, r, f, art) in _rampenSpur)
        {
            int sprung = 0;
            if (vorher >= 0 && f != vorher)
            {
                int diff = ((f - vorher) % 8 + 8) % 8;
                sprung = System.Math.Min(diff, 8 - diff);
                wechsel++;
                if (sprung > groesster) groesster = sprung;
                if (art != 0) wechselAufRampe++;
            }
            sb.Append($"   {takt,4}  ({c,3},{r,3})  {art,3}  {f} {FacingWord(f),-13} " +
                      (sprung > 0 ? $"{sprung}" : "") + "\n");
            vorher = f;
        }

        sb.Append($"   {_rampenSpur.Count} Einträge, {wechsel} Richtungswechsel " +
                  $"({wechselAufRampe} davon auf einer Schrägenzelle), " +
                  $"grösster Sprung {groesster} Stufen\n");
        sb.Append("   ⭐ Erwartet auf einer GERADEN Fahrt über eine Rampe: 0 Wechsel.\n" +
                  "     Ein »Threesixty« sind mindestens 4 Wechsel auf derselben Zelle.\n" +
                  "   ⚠ NULLMODELL: derselbe Lauf mit --kein-hang. Sind die Zahlen dort\n" +
                  "     gleich, liegt es NICHT an der Schrägenrechnung.\n");
        return sb.ToString();
    }

    /// <summary>0 = sauber (keine Wechsel), 1 = die Richtung springt.</summary>
    public int RampenProbeRc() => _rampenSpur.Count > 1 && RampenWechsel() > 0 ? 1 : 0;

    private int RampenWechsel()
    {
        int n = 0, vorher = -1;
        foreach (var (_, _, _, f, _) in _rampenSpur)
        {
            if (vorher >= 0 && f != vorher) n++;
            vorher = f;
        }
        return n;
    }
}
