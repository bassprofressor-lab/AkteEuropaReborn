namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>Fable D5, Pruefweg <c>--endbild-probe=&lt;bild.png&gt;</c>: nach 4 s fallen alle Feinde,
/// das Gefecht entscheidet sich ueber den normalen Weg (CheckEnd), dann ein Bild des Endbilds.</summary>
public partial class MapViewer
{
    private async System.Threading.Tasks.Task EndbildProbe(string datei)
    {
        await ToSignal(GetTree().CreateTimer(4.0, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"endbild-probe: {_entities.EndbildProbeFeindeWeg()} Feinde gefallen");
        for (int i = 0; i < 40 && !_ended; i++)
            await ToSignal(GetTree().CreateTimer(0.25, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(GetTree().CreateTimer(1.5, processAlways: true), SceneTreeTimer.SignalName.Timeout);
        var err = GetViewport().GetTexture().GetImage().SavePng(datei);
        GD.Print($"endbild-probe: Endbild {( _ended ? "offen" : "NICHT offen")}, Bild {datei} ({err}); {_entities.GefechtErgebnis().Text}");
        GetTree().Quit(0);
    }
}
