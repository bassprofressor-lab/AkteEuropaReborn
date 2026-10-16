using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ bug-586 — die Kommandozeile: <c>--werteliste-eigen-check</c> (Prüfstand,
/// Simulation/WertelisteEigenCheck.cs) und <c>--werteliste-nachtrag-alt</c>
/// (Gegenschalter: ein frisch erstellter Entwurf kommt nicht ins Wertebuch).
/// </summary>
public partial class MapViewer
{
    private bool _wertelisteEigenCheck;

    /// <summary>true = Schalter erkannt.</summary>
    private bool WertelisteEigenSchalter(string a)
    {
        switch (a)
        {
            case "--werteliste-eigen-check": _wertelisteEigenCheck = true; return true;
            case "--werteliste-nachtrag-alt": UI.UnitStatBook.NachtragAlt = true; return true;
        }
        return false;
    }

    /// <summary>Nach dem Laden: true = der Prüfstand lief (und hat beendet).
    /// ⚠ Ruft selbst <c>Quit</c> — kein <c>--quit-after</c> dazugeben.</summary>
    private bool WertelisteEigenPruefstand()
    {
        if (!_wertelisteEigenCheck) return false;
        GD.Print(_entities.WertelisteEigenCheck());
        GetTree().Quit(0);
        return true;
    }
}
