using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b><c>--schiff-log</c> — je Sekunde EINE Zeile fuer jedes Schiff, mit dem
/// GRUND, warum es gerade nicht vorankommt.</b>
///
/// <para>Gebaut am 08.09.2026 auf seine Meldung »die Schiffsnavigation ist
/// aktuell ein Krampf«. Genau dieselbe Bauweise wie <c>--schuss-log</c> und
/// <c>--entlade-log</c>: er faehrt, ich lese die Zahlen. Der Vorteil ist
/// derselbe — »es hakt« ist ein Eindruck, »42x GiveWay auf derselben Zelle, 3x
/// aufgegeben, 7x neu geplant« ist eine Aussage.</para>
///
/// <para>Die Zeile nennt in dieser Reihenfolge: Lage, Ziel, Weg (Zeiger/Laenge),
/// Vormerkung, Geduld (<c>Block</c>), Wiederholung (<c>RetryIn</c>), und den
/// letzten GRUND — gesetzt an den Stellen, an denen der Schritt wirklich
/// ausfaellt (<c>BlockedStep</c>, <c>Repath</c>, <c>AufgebenWeilNahGenug</c>).</para>
///
/// <para>⚠ Der Grund wird nur mitgeschrieben, wenn der Mitschnitt laeuft
/// (<see cref="Fahrgruende"/>) — sonst kostet jede blockierte Zelle eine
/// Zeichenkette.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--schiff-log</c> — der Mitschnitt.</summary>
    public static bool SchiffLog;

    /// <summary><c>--schiff-log-alle</c> — nicht nur Schiffe, jede Einheit.</summary>
    public static bool SchiffLogAlle;

    /// <summary>Ob der Grund ueberhaupt gemerkt wird.</summary>
    public static bool Fahrgruende;

    private float _schiffLogTimer;

    /// <summary>Wie oft ein Schiff im laufenden Spiel an einer Zelle haengen
    /// blieb, aufgegeben hat oder neu geplant hat — die drei Zahlen, die
    /// »Krampf« zu etwas Messbarem machen.</summary>
    public int SchiffBlockiert, SchiffAufgegeben, SchiffNeugeplant;

    /// <summary>Den Grund merken — an den Stellen, an denen der Schritt
    /// ausfaellt.</summary>
    private void Fahrgrund(Entity e, string was)
    {
        if (Fahrgruende) e.Fahrgrund = was;
    }

    private void SchiffLogTakt(float dt)
    {
        if (!SchiffLog) return;
        Fahrgruende = true;
        _schiffLogTimer -= dt;
        if (_schiffLogTimer > 0f) return;
        _schiffLogTimer = 1f;

        int faehrt = 0, steht = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding) continue;
            bool schiff = e.Move == Simulation.NavGrid.MoveClass.Ship;
            if (!SchiffLogAlle && !schiff) continue;
            if (e.Path != null) faehrt++; else steht++;

            string weg = e.Path == null ? "keiner" : $"{e.PathIdx}/{e.Path.Count}";
            string naechste = "-";
            if (e.Path != null && e.PathIdx < e.Path.Count && _nav != null)
            {
                var n = e.Path[e.PathIdx];
                var frage = _nav.Ask(n.X, n.Y, e.Move, i);
                naechste = $"({n.X},{n.Y}) {frage}";
                if (frage != Simulation.NavGrid.Step.Free)
                    naechste += " — " + _nav.WarumGesperrt(n.X, n.Y, e.Move, i);
            }

            GD.Print($"schiff: Platz {e.Slot} Sp{e.Owner} \"{LabelOf(e)}\" ({e.Col},{e.Row}) "
                   + $"Rumpf {Simulation.NavGrid.HullSide(e.GameUnitType)} Ziel "
                   + $"({e.Goal.X},{e.Goal.Y}) Weg {weg} naechste {naechste} "
                   + $"Vormerkung {(e.Reserved is { } v ? $"({v.X},{v.Y})" : "-")} "
                   + $"Geduld {e.Block} Wiederholung {e.RetryIn} Schritt "
                   + $"{e.Progress}/{e.StepCost}: "
                   + (e.Fahrgrund.Length > 0 ? e.Fahrgrund : "— faehrt oder hat nichts vor —"));
        }
        GD.Print($"schiff: {faehrt} unterwegs, {steht} stehend; im Lauf {SchiffBlockiert}x "
               + $"blockiert, {SchiffNeugeplant}x neu geplant, {SchiffAufgegeben}x aufgegeben "
               + $"(Takt {DebugTicks})");
    }
}
