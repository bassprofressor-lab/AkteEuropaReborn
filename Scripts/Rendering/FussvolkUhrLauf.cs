namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 30.10.2026 — bug-688, die Fussvolk-Uhr 0x40F0A0 (Simulation/FussvolkUhr.cs): Schalter
/// und Pruefstand an einer Stelle. Bau: <c>berichte/bau-fussvolk-uhr.md</c>.
/// </summary>
public partial class MapViewer
{
    private bool _fuCheck;

    /// <summary>Die Schalter der Fussvolk-Uhr; true = verbraucht.</summary>
    private bool FussUhrSchalter(string a)
    {
        switch (a)
        {
            case "--fussvolk-uhr-alt": MapEntityLayer.FussvolkUhrAlt = true; return true;
            case "--fussvolk-halt-aus": MapEntityLayer.FussvolkHaltAus = true; return true;          // Nullmodell
            case "--fussvolk-uhr-gebaeude": MapEntityLayer.FussvolkUhrGebaeude = true; return true;  // Nullmodell
            case "--fussvolk-uhr-check": _fuCheck = true; return true;
        }
        return false;
    }

    /// <summary>Der synchrone Pruefstand; true = er lief (und beendet).</summary>
    private bool FussUhrPruefstaende()
    {
        if (!_fuCheck) return false;
        GD.Print(_entities.FussvolkUhrCheck());
        GetTree().Quit(0);
        return true;
    }
}
