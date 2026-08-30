using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>--ki-probe — REAGIERT DIE STREIFE, WENN MAN IHR NAHE GENUG KOMMT?</b>
///
/// <para>Gebaut am 30.08.2026 aus seiner Meldung: »es müssten die einheiten von
/// der oberen rechten basis schon auf mich in der stadt treffen«.</para>
///
/// <para><b>Warum es diesen Prüfstand braucht.</b> Der erste Messlauf sagte
/// <c>»P1 0 losgeschickt, 420 untaetige angesehen, im Ring 846 Einheiten,
/// 0 feindlich«</c> und dazu <c>»naechster Feind Abstand 47,2 -> AUSSERHALB des
/// Rings«</c>. Daraus folgt <b>nichts</b> über die Frage, ob die Streife
/// reagieren WÜRDE: sie hatte nie Gelegenheit. In einem kopflosen Lauf bewegt
/// sich niemand auf den Gegner zu, und »0 losgeschickt« sieht dann genauso aus
/// wie ein kaputter Durchlauf. Das ist Regel 33 — der Prüfstand muss den
/// Gegenstand enthalten, sonst ist sein Ergebnis keines.</para>
///
/// <para><b>Was er tut:</b> er stellt eine eigene fahrende Einheit zwei Zellen
/// neben die nächstgelegene Einheit eines Computerspielers und sieht nach, ob
/// dessen Streife sie innerhalb weniger Sekunden aufnimmt.</para>
///
/// <para><b>Die Messlatte steht vorher fest, und sie ist gelesen.</b> Der
/// Erbauer der Ringtafeln <c>@0x438790</c> sagt, was ein Ring ist:</para>
/// <code>
///   si = 0
///   fuer radius = 0 .. 126:
///       word[radius*2 + 0x834A80] = si          ; T[radius]
///       fuer dy = -radius .. radius:
///           fuer dx = -radius .. radius:
///               wenn round(sqrt(dx*dx + dy*dy) + K) == radius:   ; K = qword[0x4F0268]
///                   OFF[si++] = (dx, dy)
/// </code>
/// <para>⭐ <b>Der Index IST also der Radius in Zellen</b>, und die Metrik ist
/// der gerundete euklidische Abstand — beides war bisher unsere Setzung und ist
/// jetzt belegt. <c>ai_units</c> @0x4BF4E0 scannt <c>OFF[T[k] .. T[+0x2c+1]-1]</c>,
/// also die Ringe von <c>k</c> bis einschliesslich <c>+0x2c</c>; zu zwei Dritteln
/// ist <c>k = +0x2c</c> (nur der äusserste Ring), zu einem Drittel
/// <c>k = +0x2b</c>. Eine Einheit mit Sicht 3 reicht damit <b>drei Zellen
/// weit</b> — und ein Ziel in zwei Zellen Abstand liegt im Drittelfall drin,
/// im Zweidrittelfall nicht. Der Prüfstand wartet darum mehrere
/// Streifendurchgänge ab und nennt beide Fälle.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Wie nah der Prüfstand die eigene Einheit heranstellt. Zwei
    /// Zellen: nah genug für jeden Ring ab 2, weit genug, dass es nicht schon
    /// die Waffenreichweite allein erklärt.</summary>
    private const int KiProbeAbstand = 2;

    /// <summary>Wieviele Sekunden gewartet wird. Ein Streifendurchgang ist
    /// <c>AiSweepSeconds/8</c> je Block und acht Blöcke — nach zwei Sekunden ist
    /// jede Einheit mindestens einmal drangewesen; wir geben acht.</summary>
    private const float KiProbeSekunden = 8f;

    private int _kiProbe = -1, _kiProbeOpfer = -1, _kiProbeGegner = -1;
    private float _kiProbeUhr;

    /// <summary><c>--ki-probe</c> starten.</summary>
    public void KiProbeStart() { _kiProbe = 0; }

    private void PollKiProbe(float dt)
    {
        if (_kiProbe < 0 || _nav == null) return;
        switch (_kiProbe)
        {
            case 0:
            {
                // Die nächstgelegene Einheit eines Computerspielers, und eine
                // eigene fahrende dazu.
                int best = -1;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
                    if (e.Owner == ViewPlayer || e.Owner is < 0 or > 7) continue;
                    if (!SkirmishAiActive || !AiHostileFor(ViewPlayer, e.Owner)) continue;
                    best = i; break;
                }
                for (int i = 0; i < _entities.Count && _kiProbeOpfer < 0; i++)
                {
                    var e = _entities[i];
                    if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
                    if (e.Owner == ViewPlayer) _kiProbeOpfer = i;
                }
                _kiProbeGegner = best;
                if (best < 0 || _kiProbeOpfer < 0)
                {
                    GD.Print("ki-probe: kein Gegnerpaar auf dieser Karte");
                    _kiProbe = -1; return;
                }

                var g = _entities[best];
                var o = _entities[_kiProbeOpfer];
                // Eine freie Zelle im gewuenschten Abstand suchen.
                Vector2I? ziel = null;
                for (int d = KiProbeAbstand; d <= KiProbeAbstand + 1 && ziel == null; d++)
                    for (int dy = -d; dy <= d && ziel == null; dy++)
                    for (int dx = -d; dx <= d; dx++)
                    {
                        if (Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dy * dy)) != d) continue;
                        int c = g.Col + dx, r = g.Row + dy;
                        if (!_nav.InBounds(c, r)) continue;
                        if (!_nav.IsFree(c, r, o.Move)) continue;
                        ziel = new Vector2I(c, r); break;
                    }
                if (ziel == null)
                {
                    GD.Print($"ki-probe: um Platz {g.Slot} auf ({g.Col},{g.Row}) ist "
                           + $"in {KiProbeAbstand} Zellen nichts frei");
                    _kiProbe = -1; return;
                }

                _nav.ClearOccupant(o.Col, o.Row, _kiProbeOpfer);
                if (o.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, _kiProbeOpfer);
                o.Reserved = null; o.Path = null; o.Orders.Clear(); o.Target = -1;
                o.Col = ziel.Value.X; o.Row = ziel.Value.Y;
                o.Elev = ElevOf(o.Col, o.Row);
                o.Pos = BodyCenterAt(o, o.Col, o.Row);
                o.Footprint = CellRect(_ox, _oy, o.Col, o.Row, o.Elev);
                _nav.SetOccupant(o.Col, o.Row, _kiProbeOpfer, o.Infantry >= 0);

                float ab = Mathf.Sqrt((o.Col - g.Col) * (o.Col - g.Col)
                                    + (o.Row - g.Row) * (o.Row - g.Row));
                GD.Print($"ki-probe: eigene Einheit Platz {o.Slot} auf ({o.Col},{o.Row}) "
                       + $"gestellt, {ab:0.0} Zellen neben P{g.Owner} Platz {g.Slot} "
                       + $"auf ({g.Col},{g.Row}) — dessen Reichweite {g.Range}, "
                       + $"Sicht {g.Sight}. Erwartet: die Streife nimmt sie auf, "
                       + $"sobald {ab:0.0} <= Sicht {g.Sight}.");
                _kiProbeUhr = KiProbeSekunden;
                _kiProbe = 1;
                return;
            }

            case 1:
            {
                _kiProbeUhr -= dt;
                if (_kiProbeUhr > 0f) return;
                var g = _entities[_kiProbeGegner];
                bool faehrt = g.Path is { Count: > 0 } || g.Orders.Count > 0 || g.Target >= 0;
                GD.Print($"ki-probe: nach {KiProbeSekunden:0} s — P{g.Owner} Platz {g.Slot} "
                       + $"{(faehrt ? "REAGIERT" : "steht weiter")} "
                       + $"(Ziel {g.Target}, Weg {(g.Path?.Count ?? 0)}, "
                       + $"Befehle {g.Orders.Count})");
                GD.Print("   " + AiUnitLine());
                _kiProbe = -1;
                return;
            }
        }
    }
}
