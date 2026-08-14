namespace AkteEuropaReborn.Campaign;

using System.Collections.Generic;
using Godot;

/// <summary>
/// DAS ZIEL DER HAUPTMISSION, im Klartext des Originals.
///
/// <para><b>Warum es diese Klasse braucht.</b> Das Kampagnen-HUD zeigt bisher
/// nur UNTERmissionen, und das hat einen Grund: ueber 28 Missionen sind alle 91
/// verfolgten Ziele Untermissionen (78 beginnen woertlich mit
/// <c>@Untermission</c>). Das Hauptziel steht in keiner dieser Listen — es ist
/// die ENDREGEL, und die hat gar keinen Text. Nur <c>OBJECTG.TXT</c> hat einen,
/// und die liegt seit dem Einspieler als <c>UI/objectives.json</c> vor
/// (<see cref="Import.ObjectivesExporter"/>).</para>
///
/// <para><b>Die Nummer ist die Missionsnummer</b>, 001..033 ohne Luecke — und
/// das ist gegen die BRIEFINGS geprueft, nicht gegen die Kartennamen. An denen
/// waere es beinahe gescheitert: <c>map_02</c> heisst »Hidden Bases«, waehrend
/// #002 vom Auftanken spricht, und <c>map_03</c> heisst »Wood raiders«, waehrend
/// #003 einen Forschungskomplex nennt. Beides sind interne KARTENnamen. Die
/// Briefings sagen es eindeutig: Briefing 2 nennt woertlich »jede Moeglichkeit
/// zum @Auftanken der mechanisierten Kampfeinheiten« (#002 »Tanken Sie Einheiten
/// auf«), Briefing 3 ein »geheimes @Forschungszentrum« (#003 »Besetzen Sie den
/// Forschungskomplex«), Briefing 6 »eine feindliche @Radarstellung« (#006
/// »Zerstoeren Sie die gegnerische Radarstellung«). Drei Treffer, kein
/// Gegenbeispiel.</para>
///
/// <para>⚠ Gezeigt wird der Text, wie er dasteht. Er sagt NICHT, ob ein Ziel
/// schon erfuellt ist — das weiss nur die Endregel, und die hat keinen Text. Wer
/// hier einen Haken hinmalen wollte, muesste Text und Regel einander zuordnen,
/// und diese Zuordnung ist ungelesen. Lieber eine ehrliche Zeile als ein
/// erfundener Fortschritt.</para>
/// </summary>
public static class MissionObjectives
{
    private static Godot.Collections.Dictionary<string, Variant>? _goals;
    private static bool _tried;

    /// <summary>Die Ziele einer Mission, oder eine leere Liste.</summary>
    public static List<string> For(int mission)
    {
        var list = new List<string>();
        if (!_tried)
        {
            _tried = true;
            string path = Core.Content.Path("UI/objectives.json");
            if (FileAccess.FileExists(path))
            {
                using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
                var json = new Json();
                if (f != null && json.Parse(f.GetAsText()) == Error.Ok &&
                    json.Data.VariantType == Variant.Type.Dictionary)
                {
                    var root = json.Data.AsGodotDictionary<string, Variant>();
                    if (root.TryGetValue("goals", out var gv) &&
                        gv.VariantType == Variant.Type.Dictionary)
                        _goals = gv.AsGodotDictionary<string, Variant>();
                }
            }
            GD.Print(_goals != null
                ? $"Missionsziele: {_goals.Count} Missionen aus {path}"
                : "Missionsziele: objectives.json fehlt — das HUD zeigt kein Hauptziel " +
                  "(--reexport-help=<Quelle> schreibt sie; OBJECTG.TXT liegt auch in " +
                  "DATA1.CAB auf CD 1)");
        }
        if (_goals == null) return list;
        if (!_goals.TryGetValue(mission.ToString(), out var v) ||
            v.VariantType != Variant.Type.Array) return list;
        foreach (var p in v.AsGodotArray()) list.Add(p.AsString());
        return list;
    }

    /// <summary>Die Ziele als eine Zeile fuer das HUD.</summary>
    public static string Line(int mission)
    {
        var g = For(mission);
        return g.Count == 0 ? "" : "AUFTRAG: " + string.Join("  ·  ", g);
    }
}
