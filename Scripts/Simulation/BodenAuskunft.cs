using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>--boden-um=&lt;spalte&gt;,&lt;zeile&gt; — DIE BODENAUSKUNFT.</b>
///
/// <para>Gebaut am 30.08.2026, weil dieselbe Frage zum dritten Mal kam: »kann
/// diese Einheit hier hin, und wenn nein, warum nicht«. Bisher war die Antwort
/// jedes Mal eine Bastelei aus Kartendatei-Werkzeugen; das hier ist sie in
/// einem Zug, aus dem LAUFENDEN Spiel und damit aus genau den Daten, die die
/// Wegsuche benutzt.</para>
///
/// <para><b>Was es zeigt und warum genau das.</b> Das Original entscheidet in
/// <c>Can_go</c> <c>@0x4055D0</c> über drei Stufen, und die Auskunft nennt alle
/// drei nebeneinander:</para>
/// <list type="number">
/// <item><b>Die Gattung</b> (<c>+0x0A</c>) wählt den Arm — Sprungtafel
/// <c>0x40678C</c>, sechs Einträge.</item>
/// <item><b>Das Fahrwerk</b> (<c>+0x0B</c>) verzweigt darin genau zweimal:
/// <c>cmp al,7</c> → Hover, <c>cmp al,0x11</c> → Walker, sonst der gemeinsame
/// Rad-/Kettenweg <c>@0x405BD7</c>.</item>
/// <item><b>Die imap-Klasse</b> der Zelle entscheidet: Rad/Kette lässt nur
/// <c>0xFFFE</c> durch (<c>@0x405CD8</c>), Walker auch <c>0xFFFD</c>
/// (<c>@0x405A07</c>), Hover zusätzlich <c>0xFFFC</c>
/// (<c>@0x405769</c>).</item>
/// </list>
///
/// <para>⚠ Dazu die Höhe und die Kachelflagge — <b>nicht</b> weil das Original
/// sie in <c>Can_go</c> prüft (es tut es nicht), sondern weil UNSER
/// <see cref="Simulation.NavGrid.MaxClimb"/> es tut. Steht in einer Meldung
/// »kommt nicht hin« und die imap ist frei, ist die Höhenstufe der nächste
/// Verdächtige, und sie ist unsere Setzung.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Die Zelle, um die gefragt wurde; −1 = niemand hat gefragt.</summary>
    public static int BodenUmCol = -1, BodenUmRow = -1;

    /// <summary>Wie weit im Umkreis. Fünf Zellen nach jeder Seite sind ein
    /// 11×11-Feld — genug, um ein Ufer und seine Zufahrt auf einmal zu sehen.
    /// </summary>
    public static int BodenUmWeite = 5;

    /// <summary>Die Auskunft als Text. Leer, wenn nicht gefragt wurde.</summary>
    public string BodenAuskunft()
    {
        if (BodenUmCol < 0 || _nav == null) return "";
        int c0 = BodenUmCol, r0 = BodenUmRow, w = BodenUmWeite;
        var sb = new System.Text.StringBuilder();
        sb.Append($"boden-um ({c0},{r0}), Umkreis {w}:\n");
        sb.Append("  Bodenklasse   . = frei (imap 0xFFFE, ALLE duerfen)   "
                + "r = rau (0xFFFD, nur Walker und Infanterie)\n");
        sb.Append("                w = Wasser (0xFFFC, nur Schiff und Hover)   "
                + "# = gesperrt\n");

        sb.Append("      ");
        for (int c = c0 - w; c <= c0 + w; c++) sb.Append($"{c % 100,3}");
        sb.Append('\n');
        for (int r = r0 - w; r <= r0 + w; r++)
        {
            sb.Append($"  r{r,-3} ");
            for (int c = c0 - w; c <= c0 + w; c++)
            {
                if (!_nav.InBounds(c, r)) { sb.Append("  ?"); continue; }
                char z = _nav.GroundAt(c, r) switch
                {
                    Simulation.NavGrid.Ground.Free => '.',
                    Simulation.NavGrid.Ground.Rough => 'r',
                    Simulation.NavGrid.Ground.Water => 'w',
                    _ => '#',
                };
                // die gefragte Zelle in Klammern, damit man sie im Raster findet
                sb.Append(c == c0 && r == r0 ? $" [{z}" : $"  {z}");
            }
            sb.Append('\n');
        }

        // Hoehe und Flagge NUR, wenn sie ueberhaupt etwas sagen — sonst ist es
        // eine Wand aus Nullen, in der man den einen Wert nicht mehr sieht.
        int hMin = int.MaxValue, hMax = int.MinValue, flagAnders = 0;
        for (int r = r0 - w; r <= r0 + w; r++)
            for (int c = c0 - w; c <= c0 + w; c++)
            {
                if (!_nav.InBounds(c, r)) continue;
                int h = _nav.ElevAt(c, r);
                if (h < hMin) hMin = h;
                if (h > hMax) hMax = h;
                if (_nav.FlagAt(c, r) != 0) flagAnders++;
            }
        if (hMin > hMax) return sb.ToString();
        sb.Append($"  Hoehe {hMin}..{hMax}");
        if (hMax - hMin > Simulation.NavGrid.MaxClimb)
            sb.Append($"  ⚠ Sprung groesser als MaxClimb={Simulation.NavGrid.MaxClimb} "
                    + "(UNSERE Setzung, das Original prueft in Can_go keine Hoehe)");
        sb.Append($"; {flagAnders} Zellen mit Kachelflagge != 0\n");

        // Und wer steht da? Ohne das haelt man eine belegte Zelle fuer gesperrt.
        for (int r = r0 - w; r <= r0 + w; r++)
            for (int c = c0 - w; c <= c0 + w; c++)
            {
                if (!_nav.InBounds(c, r)) continue;
                int oi = _nav.OccupantAt(c, r);
                if (oi < 0 || oi >= _entities.Count) continue;
                var e = _entities[oi];
                if (e.IsBuilding || e.IsProp) continue;
                sb.Append($"  auf ({c},{r}): Platz {e.Slot} P{e.Owner} "
                        + $"Gattung {e.GameUnitType} Fahrwerk {e.Chassis} "
                        + $"({Simulation.NavGrid.ClassOf(e.GameUnitType, e.Chassis)})\n");
            }
        return sb.ToString();
    }
}
