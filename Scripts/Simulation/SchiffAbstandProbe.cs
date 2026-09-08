using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>--schiffabstand-probe — kommen zwei Schiffe KANTE AN KANTE?</b>
///
/// <para>Seine Meldung: zwischen zwei Booten bleibt immer <b>ein Feld frei</b>.
/// Der Fable-Leselauf (<c>berichte/schiffsbewegung-fable.md</c>, Abschnitt 3)
/// sagt dazu klar: <b>»Ja.«</b> Das Original prueft beim Schritt genau das
/// Zielrechteck (4 bzw. 16 Zellen), liest keine Zelle ausserhalb, kennt fuer
/// Schiffe weder Ausweichen noch Mindestabstand — und <b>reserviert keine
/// Zielzellen</b> (<c>0x4052D0</c> kehrt fuer alles ausser Klasse 0 und 1 sofort
/// zurueck, <c>0x405325</c>).</para>
///
/// <para>Bei uns dagegen haelt eine fahrende Einheit <b>zwei Anker</b>: ihre
/// Zelle und die vorgemerkte (<c>SetOccupant(next…)</c> im Bewegungstakt). Ein
/// 2x2-Schiff belegt damit waehrend eines Schrittes bis zu <b>sechs</b> Zellen
/// statt vier — und genau das ist der Zwischenraum, den er sieht.</para>
///
/// <para><b>⚠ Erst messen, dann umbauen.</b> Die Reservierung sitzt im Kern der
/// Bewegung und haengt an der Ankerbuchhaltung (siehe <c>NavGrid._anker</c>, die
/// aus einem eigenen Fehler entstanden ist). Diese Probe stellt den Fall, damit
/// der Umbau eine Messlatte hat, die vorher REISST:</para>
///
/// <list type="number">
///   <item><b>Szene 1 — an ein STEHENDES Schiff heranfahren.</b> A haelt, B
///   bekommt als Ziel den Anker Kante an Kante. Erwartet: B steht am Ende genau
///   <c>Rumpf</c> Zellen neben A (2 bei einem 2x2), nicht 3.</item>
///   <item><b>Szene 2 — HINTEREINANDER fahren.</b> Beide fahren dieselbe
///   Strecke, B hinter A. Gemessen wird der kleinste Abstand, den B ueberhaupt
///   erreicht.</item>
/// </list>
///
/// <para>⚠ Die Probe faehrt ueber <see cref="PostMoveOne"/>, also ueber den
/// Befehlsweg des Spiels, und laesst danach den NORMALEN Takt laufen. Eine Probe,
/// die den Bewegungsschritt selbst nachbaut, wuerde genau das nicht messen,
/// worum es geht.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _sapStufe = -1, _sapA = -1, _sapB = -1;
    private float _sapUhr, _sapTicker;
    private int _sapEngste = int.MaxValue;
    private Vector2I _sapZiel;

    /// <summary>Wie lange je Szene gemessen wird.</summary>
    private const float SapSzene1 = 40f, SapSzene2 = 40f;

    public void SchiffAbstandProbeStart() { _sapStufe = 0; _sapUhr = 5f; }

    /// <summary>Der Ankerabstand zweier Schiffe in Zellen — 2 heisst bei einem
    /// 2x2-Rumpf »Kante an Kante«, 3 ist das eine freie Feld.</summary>
    private static int SapAnkerAbstand(Entity a, Entity b)
        => Mathf.Max(Mathf.Abs(a.Col - b.Col), Mathf.Abs(a.Row - b.Row));

    private bool SapIstSchiff(Entity e)
        => !e.Dead && !e.IsProp && !e.IsBuilding && e.Mobile
        && e.Move == Simulation.NavGrid.MoveClass.Ship;

    private void PollSchiffAbstandProbe(float dt)
    {
        if (_sapStufe < 0 || _nav == null) return;
        switch (_sapStufe)
        {
            case 0:
                _sapUhr -= dt;
                if (_sapUhr > 0f) return;
                _sapStufe = 1;
                return;

            case 1:
            {
                // Zwei eigene Schiffe, so nah beieinander wie moeglich.
                _sapA = -1; _sapB = -1;
                int besteEnt = int.MaxValue;
                // ⚠ ERST die eigenen, sonst irgendein Paar DESSELBEN Besitzers.
                // Ohne den Rueckfall ist die Probe auf den meisten Karten »nicht
                // stellbar« — ein Befund ueber die Auswahl, nicht ueber die
                // Schiffe. ⭐ Und der GROESSTE Rumpf zuerst: bei einem 4x4 liegen
                // die zwei Anker zwoelf von sechzehn Zellen uebereinander, dort
                // muesste ein Zwischenraum am ehesten auffallen.
                for (int runde = 0; runde < 2 && _sapA < 0; runde++)
                {
                    int besterRumpf = 0;
                    for (int i = 0; i < _entities.Count; i++)
                    {
                        if (!SapIstSchiff(_entities[i])) continue;
                        if (runde == 0 && _entities[i].Owner != ViewPlayer) continue;
                        for (int j = i + 1; j < _entities.Count; j++)
                        {
                            if (!SapIstSchiff(_entities[j])) continue;
                            if (_entities[j].Owner != _entities[i].Owner) continue;
                            if (runde == 0 && _entities[j].Owner != ViewPlayer) continue;
                            int rumpfIJ = Mathf.Min(
                                Simulation.NavGrid.HullSide(_entities[i].GameUnitType),
                                Simulation.NavGrid.HullSide(_entities[j].GameUnitType));
                            int d = SapAnkerAbstand(_entities[i], _entities[j]);
                            if (rumpfIJ > besterRumpf || (rumpfIJ == besterRumpf && d < besteEnt))
                            { besterRumpf = rumpfIJ; besteEnt = d; _sapA = i; _sapB = j; }
                        }
                    }
                }
                if (_sapA < 0)
                {
                    int schiffe = 0, beweglich = 0;
                    foreach (var q in _entities)
                    {
                        if (q.Dead || q.IsProp || q.IsBuilding) continue;
                        if (q.GameUnitType is 4 or 5) schiffe++;
                        if (SapIstSchiff(q)) beweglich++;
                    }
                    GD.Print($"schiffabstand-probe: kein Paar — Gattung 4/5 auf der Karte: "
                           + $"{schiffe}, davon beweglich mit Klasse Ship: {beweglich} "
                           + "— nicht stellbar");
                    _sapStufe = -1; return;
                }

                var a = _entities[_sapA]; var b = _entities[_sapB];
                int rumpf = Simulation.NavGrid.HullSide(a.GameUnitType);

                // A haelt an, damit die Szene ruhig ist.
                a.Path = null; a.Orders.Clear(); a.Target = -1;

                // Der Zielanker Kante an Kante — die vier Seiten der Reihe nach.
                Vector2I? ziel = null;
                var seiten = new[] { new Vector2I(rumpf, 0), new Vector2I(-rumpf, 0),
                                     new Vector2I(0, rumpf), new Vector2I(0, -rumpf) };
                foreach (var s in seiten)
                {
                    var z = new Vector2I(a.Col + s.X, a.Row + s.Y);
                    // ⚠ Mit dem RUMPF fragen, nicht mit einer Zelle: AskRumpf
                    // ist dieselbe Schleife, die auch der Schritt stellt.
                    if (_nav.AskRumpf(z.X, z.Y, b.Move, _sapB,
                                      Simulation.NavGrid.HullSide(b.GameUnitType))
                        != Simulation.NavGrid.Step.Free) continue;
                    ziel = z; break;
                }
                if (ziel == null)
                {
                    GD.Print($"schiffabstand-probe: um Platz {a.Slot} ({a.Col},{a.Row}) ist keine "
                           + "der vier Kantenlagen frei — nicht stellbar");
                    _sapStufe = -1; return;
                }
                _sapZiel = ziel.Value;

                bool ab = PostMoveOne(_sapB, CellCenter(_sapZiel.X, _sapZiel.Y), false);
                GD.Print($"schiffabstand-probe SZENE 1: A Platz {a.Slot} \"{LabelOf(a)}\" "
                       + $"haelt auf ({a.Col},{a.Row}), Rumpf {rumpf}x{rumpf}; "
                       + $"B Platz {b.Slot} \"{LabelOf(b)}\" auf ({b.Col},{b.Row}) soll nach "
                       + $"({_sapZiel.X},{_sapZiel.Y}), Ankerabstand jetzt "
                       + $"{SapAnkerAbstand(a, b)}; Fahrbefehl {(ab ? "raus" : "NICHT abgesetzt")}");
                GD.Print($"   ERWARTET: B steht am Ende auf ({_sapZiel.X},{_sapZiel.Y}), also "
                       + $"Ankerabstand {rumpf} — »Kante an Kante«. {rumpf + 1} ist das eine "
                       + "freie Feld, das er meldet.");
                if (!ab) { _sapStufe = -1; return; }
                _sapUhr = SapSzene1; _sapTicker = 0f; _sapEngste = int.MaxValue;
                _sapStufe = 2;
                return;
            }

            case 2:
            {
                _sapUhr -= dt; _sapTicker -= dt;
                var a = _entities[_sapA]; var b = _entities[_sapB];
                int d = SapAnkerAbstand(a, b);
                if (d < _sapEngste) _sapEngste = d;
                bool da = b.Col == _sapZiel.X && b.Row == _sapZiel.Y;
                if (_sapTicker <= 0f)
                {
                    _sapTicker = 4f;
                    GD.Print($"   [t] B auf ({b.Col},{b.Row}) Ankerabstand {d} "
                           + $"Weg {(b.Path == null ? "keiner" : $"{b.PathIdx}/{b.Path.Count}")} "
                           + $"Vormerkung {(b.Reserved is { } q ? $"({q.X},{q.Y})" : "-")} "
                           + $"Geduld {b.Block} | Ziel erreicht: {da}");
                }
                if (!da && _sapUhr > 0f) return;

                int rumpf = Simulation.NavGrid.HullSide(a.GameUnitType);
                GD.Print($"schiffabstand-probe SZENE 1 ERGEBNIS: B steht auf ({b.Col},{b.Row}), "
                       + $"Ankerabstand {d} (engster im Lauf: {_sapEngste}), Soll {rumpf} -> "
                       + (d <= rumpf ? "KANTE AN KANTE (bestanden)"
                        : d == rumpf + 1 ? "EIN FELD ZUVIEL — genau seine Meldung"
                                         : $"{d - rumpf} Felder zuviel"));
                if (!da)
                    GD.Print("   ⚠ B hat sein Ziel gar nicht erreicht — dann sagt der Abstand "
                           + $"nichts. Gesperrt? {_nav.WarumGesperrt(_sapZiel.X, _sapZiel.Y, b.Move, _sapB, Simulation.NavGrid.HullSide(b.GameUnitType))}");

                // Szene 2: hintereinander dieselbe Strecke.
                var ziel2 = SapFernzielSuchen(a);
                if (ziel2 == null)
                {
                    GD.Print("schiffabstand-probe: kein Fernziel im Wasser gefunden — "
                           + "Szene 2 faellt aus");
                    _sapStufe = -1; return;
                }
                bool ab1 = PostMoveOne(_sapA, CellCenter(ziel2.Value.X, ziel2.Value.Y), false);
                bool ab2 = PostMoveOne(_sapB, CellCenter(ziel2.Value.X, ziel2.Value.Y), false);
                GD.Print($"schiffabstand-probe SZENE 2: beide nach ({ziel2.Value.X},{ziel2.Value.Y}) "
                       + $"— Befehle {(ab1 ? "A raus" : "A NICHT")}, {(ab2 ? "B raus" : "B NICHT")}");
                GD.Print("   ERWARTET: der engste Ankerabstand waehrend der Fahrt ist "
                       + $"{rumpf}; alles darueber ist der Zwischenraum.");
                _sapEngste = int.MaxValue;
                _sapUhr = SapSzene2; _sapTicker = 0f; _sapStufe = 3;
                return;
            }

            case 4:
            {
                _sapUhr -= dt; _sapTicker -= dt;
                var a4 = _entities[_sapA]; var b4 = _entities[_sapB];
                int d4 = SapAnkerAbstand(a4, b4);
                if (d4 < _sapEngste) _sapEngste = d4;
                if (_sapTicker <= 0f)
                {
                    _sapTicker = 5f;
                    GD.Print($"   [t] A ({a4.Col},{a4.Row}) B ({b4.Col},{b4.Row}) Abstand {d4} "
                           + $"(engster {_sapEngste}) Wege {(a4.Path == null ? "-" : "A faehrt")} "
                           + $"{(b4.Path == null ? "-" : "B faehrt")}");
                }
                if (_sapUhr > 0f && !(a4.Path == null && b4.Path == null && _sapUhr < SapSzene2 - 6f))
                    return;
                SapSzene3Ende();
                return;
            }

            case 3:
            {
                _sapUhr -= dt; _sapTicker -= dt;
                var a = _entities[_sapA]; var b = _entities[_sapB];
                int d = SapAnkerAbstand(a, b);
                if (d < _sapEngste) _sapEngste = d;
                if (_sapTicker <= 0f)
                {
                    _sapTicker = 5f;
                    GD.Print($"   [t] A ({a.Col},{a.Row}) B ({b.Col},{b.Row}) Abstand {d} "
                           + $"(engster {_sapEngste})");
                }
                if (_sapUhr > 0f) return;

                int rumpf = Simulation.NavGrid.HullSide(a.GameUnitType);
                GD.Print($"schiffabstand-probe SZENE 2 ERGEBNIS: engster Ankerabstand "
                       + $"{_sapEngste}, Soll {rumpf} -> "
                       + (_sapEngste <= rumpf ? "KANTE AN KANTE (bestanden)"
                                              : $"{_sapEngste - rumpf} Felder zuviel"));

                // ⭐ SZENE 3 — DER KLICK DES SPIELERS: beide angewaehlt, EIN
                // Rechtsklick. Das ist der Fall, den er wirklich macht, und er
                // laeuft ueber PickGoalCell — die Spirale, die jeder Einheit ein
                // eigenes Ziel im Umkreis gibt. ⚠ Sie zaehlt in EINZELZELLEN,
                // waehrend ein 2x2-Schiff Anker im Abstand von ZWEI braucht;
                // genau hier steht im Fable-Bericht der dritte Verdaechtige
                // (»eine Zielverlegung durch die Spirale, die bei uns anders
                // sortiert ist«).
                var ziel3 = SapFernzielSuchen(a);
                if (ziel3 == null)
                {
                    GD.Print("schiffabstand-probe: kein Fernziel — Szene 3 faellt aus");
                    _sapStufe = -1; return;
                }
                _sapZiel = ziel3.Value;
                _sel.Clear(); _sel.Add(_sapA); _sel.Add(_sapB);
                UI.WindowManager.KlickProtokoll = true;      // welche Zelle wird zum Ziel?
                int gesetzt = PostMove(CellCenter(_sapZiel.X, _sapZiel.Y));
                UI.WindowManager.KlickProtokoll = false;
                GD.Print($"schiffabstand-probe SZENE 3: beide angewaehlt, EIN Klick auf "
                       + $"({_sapZiel.X},{_sapZiel.Y}) -> {gesetzt} Fahrbefehl(e)");
                GD.Print($"   ERWARTET: am Ende stehen sie {rumpf} Anker auseinander, "
                       + "Kante an Kante.");
                _sapEngste = int.MaxValue;
                _sapUhr = SapSzene2; _sapTicker = 0f; _sapStufe = 4;
                return;
            }
        }
    }

    /// <summary>Szene 3 laeuft aus: das Ergebnis des Spielerklicks.</summary>
    private void SapSzene3Ende()
    {
        var a = _entities[_sapA]; var b = _entities[_sapB];
        int rumpf = Simulation.NavGrid.HullSide(a.GameUnitType);
        int d = SapAnkerAbstand(a, b);
        GD.Print($"schiffabstand-probe SZENE 3 ERGEBNIS: A ({a.Col},{a.Row}) B ({b.Col},{b.Row}), "
               + $"Ankerabstand {d} (engster im Lauf {_sapEngste}), Soll {rumpf} -> "
               + (d <= rumpf ? "KANTE AN KANTE (bestanden)"
                : d == rumpf + 1 ? "EIN FELD ZUVIEL — genau seine Meldung"
                                 : $"{d - rumpf} Felder zuviel")
               + $" | A steht: {a.Path == null}, B steht: {b.Path == null}");
        _sapStufe = -1;
    }

    /// <summary>Eine Wasserzelle, die weit genug weg ist, dass beide Schiffe
    /// wirklich fahren muessen.</summary>
    private Vector2I? SapFernzielSuchen(Entity a)
    {
        if (_nav == null) return null;
        Vector2I? beste = null; int besteEnt = 0;
        int seite = Mathf.Max(1, a.FootW);
        for (int c = 0; c < _nav.Width; c += 2)
            for (int r = 0; r < _nav.Height; r += 2)
            {
                if (_nav.AskRumpf(c, r, a.Move, _sapA, seite) != Simulation.NavGrid.Step.Free)
                    continue;
                int d = Mathf.Max(Mathf.Abs(c - a.Col), Mathf.Abs(r - a.Row));
                if (d > besteEnt && d <= 25) { besteEnt = d; beste = new Vector2I(c, r); }
            }
        return besteEnt >= 6 ? beste : null;
    }
}
