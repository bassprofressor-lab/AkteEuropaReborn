namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 30.10.2026 — bug-710..713, das Magnetbomben-Paket (Simulation/Magnetbombe.cs): Schalter und
/// Pruefstand an einer Stelle. Bau: <c>berichte/bau-magnetbombe.md</c>.
/// </summary>
public partial class MapViewer
{
    private bool _mbCheck;

    /// <summary>Die Schalter des Pakets; true = verbraucht.</summary>
    private bool MagnetbombeSchalter(string a)
    {
        switch (a)
        {
            case "--magnetbombe-alt": MapEntityLayer.MagnetbombeAlt = true; return true;
            case "--seeker-alt": MapEntityLayer.SeekerAlt = true; return true;
            case "--membrandroid-alt": MapEntityLayer.MembrandroidAlt = true; return true;
            case "--antimagnet-alt": MapEntityLayer.AntimagnetAlt = true; return true;
            case "--seeker-ziel0-leer": MapEntityLayer.SeekerZiel0Leer = true; return true;
            case "--magnetbombe-check": _mbCheck = true; return true;
        }
        return false;
    }

    /// <summary>Der synchrone Pruefstand; true = er lief (und beendet).</summary>
    private bool MagnetbombePruefstaende()
    {
        if (!_mbCheck) return false;
        GD.Print(_entities.MagnetbombeCheck());
        GetTree().Quit(0);
        return true;
    }
}
