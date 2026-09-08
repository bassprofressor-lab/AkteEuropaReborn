using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b><c>--schiffkonvoi-probe</c> — kommen MEHRERE Schiffe zusammen ans Ziel?</b>
///
/// <para>Gebaut am 08.09.2026 auf seine Meldung »die Schiffsnavigation ist
/// aktuell ein Krampf«. Der <c>--schiffabstand-probe</c> misst den ABSTAND von
/// zwei Booten und sagt nichts ueber das Vorankommen; genau darum ging es ihm
/// aber. Diese Probe misst das Vorankommen einer GRUPPE — und nur eine Gruppe
/// kann sich selbst im Weg stehen.</para>
///
/// <para><b>Der Aufbau:</b> alle eigenen Schiffe anwaehlen, EIN Klick auf eine
/// weit entfernte Wasserzelle (also ueber <see cref="PostMove"/> mit seiner
/// Streuung, genau wie im Spiel), dann laufen lassen.</para>
///
/// <para><b>Die Messlatte steht vorher fest</b> und besteht aus vier Zahlen —
/// eine allein waere zu wenig, denn »keiner kommt an« kann auch heissen, dass
/// das Ziel unerreichbar ist:</para>
/// <list type="bullet">
///   <item>wie viele <b>angekommen</b> sind (Abstand zum eigenen Ziel &lt;= 2),</item>
///   <item>wie weit die uebrigen im Mittel noch weg sind,</item>
///   <item>wie viel <b>Strecke</b> die Gruppe zusammen zurueckgelegt hat,</item>
///   <item>wie oft ein Schritt <b>blockiert</b> war und wie oft neu geplant
///   wurde.</item>
/// </list>
///
/// <para>Damit ist ein A/B moeglich: <c>--schiffe-weichen-aus</c> stellt den
/// Stand vom 08.09.2026 wieder her, und die vier Zahlen sagen, welcher besser
/// faehrt.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _skpStufe = -1;
    private float _skpUhr;
    private readonly System.Collections.Generic.List<int> _skpSchiffe = new();
    private readonly System.Collections.Generic.List<Vector2> _skpStart = new();
    private Vector2I _skpZiel;
    private int _skpBlockVor, _skpPlanVor, _skpAufVor;

    private const float SkpSekunden = 60f;

    public void SchiffKonvoiProbeStart() { _skpStufe = 0; _skpUhr = 5f; }

    private void PollSchiffKonvoiProbe(float dt)
    {
        if (_skpStufe < 0 || _nav == null) return;
        switch (_skpStufe)
        {
            case 0:
                _skpUhr -= dt;
                if (_skpUhr > 0f) return;
                _skpStufe = 1;
                return;

            case 1:
            {
                _skpSchiffe.Clear(); _skpStart.Clear();
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.Dead || e.IsBuilding || e.IsProp || !e.Mobile) continue;
                    if (e.Move != Simulation.NavGrid.MoveClass.Ship) continue;
                    if (e.Owner != ViewPlayer) continue;
                    _skpSchiffe.Add(i);
                }
                if (_skpSchiffe.Count < 2)
                {
                    GD.Print($"schiffkonvoi-probe: nur {_skpSchiffe.Count} eigene(s) Schiff(e) "
                           + "— eine Gruppe braucht zwei, nicht stellbar");
                    _skpStufe = -1; return;
                }

                // Ein weit entferntes Wasserziel, das der ERSTE erreichen kann.
                var a = _entities[_skpSchiffe[0]];
                int seite = Simulation.NavGrid.HullSide(a.GameUnitType);
                Vector2I? ziel = null; int weit = 0;
                for (int c = 0; c < _nav.Width; c += 2)
                    for (int r = 0; r < _nav.Height; r += 2)
                    {
                        if (_nav.AskRumpf(c, r, a.Move, _skpSchiffe[0], seite)
                            != Simulation.NavGrid.Step.Free) continue;
                        int d = Mathf.Max(Mathf.Abs(c - a.Col), Mathf.Abs(r - a.Row));
                        if (d > weit && d <= 35) { weit = d; ziel = new Vector2I(c, r); }
                    }
                if (ziel == null || weit < 8)
                {
                    GD.Print("schiffkonvoi-probe: kein Wasserziel in 8..35 Zellen — nicht stellbar");
                    _skpStufe = -1; return;
                }
                _skpZiel = ziel.Value;

                _sel.Clear();
                foreach (int i in _skpSchiffe) { _sel.Add(i); _skpStart.Add(_entities[i].Pos); }
                int n = PostMove(CellCenter(_skpZiel.X, _skpZiel.Y));
                _skpBlockVor = SchiffBlockiert; _skpPlanVor = SchiffNeugeplant;
                _skpAufVor = SchiffAufgegeben;

                GD.Print($"schiffkonvoi-probe AUFBAU: {_skpSchiffe.Count} eigene Schiffe, "
                       + $"EIN Klick auf ({_skpZiel.X},{_skpZiel.Y}), Entfernung {weit} Zellen, "
                       + $"{n} Fahrbefehl(e); Ausweichen fuer Schiffe: "
                       + (SchiffeWeichenAus ? "AN (alter Stand)" : "AUS (gelesen)"));
                GD.Print($"   ERWARTET nach {SkpSekunden:0} s: die meisten sind da, und die "
                       + "Zahl der blockierten Schritte bleibt klein.");
                _skpUhr = SkpSekunden; _skpStufe = 2;
                return;
            }

            case 2:
            {
                _skpUhr -= dt;
                if (_skpUhr > 0f) return;

                int da = 0; float restSumme = 0; float strecke = 0;
                for (int k = 0; k < _skpSchiffe.Count; k++)
                {
                    var e = _entities[_skpSchiffe[k]];
                    strecke += _skpStart[k].DistanceTo(e.Pos) / TileW;
                    float rest = Mathf.Max(Mathf.Abs(e.Goal.X - e.Col), Mathf.Abs(e.Goal.Y - e.Row));
                    if (rest <= 2) da++; else restSumme += rest;
                }
                int offen = _skpSchiffe.Count - da;
                GD.Print($"schiffkonvoi-probe ERGEBNIS nach {SkpSekunden:0} s "
                       + $"({(SchiffeWeichenAus ? "ALT: Schiffe bitten um Platz" : "NEU: Schiffe warten")}): "
                       + $"{da} von {_skpSchiffe.Count} am eigenen Ziel, die uebrigen im Mittel "
                       + $"{(offen > 0 ? restSumme / offen : 0):0.0} Zellen entfernt; "
                       + $"zusammen {strecke:0.0} Zellen gefahren; blockiert "
                       + $"{SchiffBlockiert - _skpBlockVor}x, neu geplant "
                       + $"{SchiffNeugeplant - _skpPlanVor}x, aufgegeben "
                       + $"{SchiffAufgegeben - _skpAufVor}x");
                _skpStufe = -1;
                return;
            }
        }
    }
}
