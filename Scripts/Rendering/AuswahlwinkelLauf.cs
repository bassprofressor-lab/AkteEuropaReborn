using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ bug-590 — die Kommandozeile: <c>--auswahlwinkel-check</c> (Prüfstand,
/// Simulation/AuswahlwinkelCheck.cs) und der Gegenschalter
/// <c>--auswahlwinkel-haupt-alt</c> (Hauptauswahl wieder heller/stärker, Stand bis 0.6.10).
/// </summary>
public partial class MapViewer
{
    private bool _auswahlwinkelCheck;

    /// <summary>true = Schalter erkannt.</summary>
    private bool AuswahlwinkelSchalter(string a)
    {
        switch (a)
        {
            case "--auswahlwinkel-check": _auswahlwinkelCheck = true; return true;
            case "--auswahlwinkel-haupt-alt": MapEntityLayer.AuswahlwinkelHauptAlt = true; return true;
        }
        return false;
    }

    /// <summary>Nach dem Laden: true = der Prüfstand lief (und hat beendet).
    /// ⚠ Ruft selbst <c>Quit</c> — kein <c>--quit-after</c> dazugeben.</summary>
    private bool AuswahlwinkelPruefstand()
    {
        if (!_auswahlwinkelCheck) return false;
        GD.Print(_entities.AuswahlwinkelCheck());
        GetTree().Quit(0);
        return true;
    }
}
