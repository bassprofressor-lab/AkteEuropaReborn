using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ bug-585 — die Kommandozeile: <c>--sprit-weiter-check</c> (Prüfstand,
/// Simulation/SpritWeiter.cs) und <c>--sprit-weiter-alt</c> (Gegenschalter: der
/// Weg einer leergefahrenen Einheit wird wie vor bug-585 verworfen).
/// </summary>
public partial class MapViewer
{
    private bool _spritWeiterCheck;

    /// <summary>true = Schalter erkannt.</summary>
    private bool SpritWeiterSchalter(string a)
    {
        switch (a)
        {
            case "--sprit-weiter-check": _spritWeiterCheck = true; return true;
            case "--sprit-weiter-alt": MapEntityLayer.SpritWeiterAlt = true; return true;
        }
        return false;
    }

    /// <summary>Nach dem Laden: true = der Prüfstand lief (und hat beendet).
    /// ⚠ Ruft selbst <c>Quit</c> — kein <c>--quit-after</c> dazugeben.</summary>
    private bool SpritWeiterPruefstand()
    {
        if (!_spritWeiterCheck) return false;
        GD.Print(_entities.SpritWeiterCheck());
        GetTree().Quit(0);
        return true;
    }
}
