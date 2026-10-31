namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 30.10.2026 — bug-690, der Angriffsarm des KI-Gruppenlaufs C 0x4BD1E6 (Simulation/
/// KiGruppenangriff.cs): Schalter und Pruefstand an einer Stelle. Bau:
/// <c>berichte/bau-ki-gruppenangriff.md</c>.
/// </summary>
public partial class MapViewer
{
    private int _kiGaCheck;

    /// <summary>Die Schalter des Angriffsarms; true = verbraucht.</summary>
    private bool KiGruppenangriffSchalter(string a)
    {
        switch (a)
        {
            case "--ki-gruppenangriff-einmal": MapEntityLayer.KiGruppenangriffEinmal = true; return true;   // Gegenschalter
            case "--ki-gruppenangriff-natur": MapEntityLayer.KiGaNatur = true; return true;                 // nur messen
            case "--gefecht-gruppenangriff-alt": MapEntityLayer.GefechtGruppenangriffAlt = true; return true; // bug-714, Gegenschalter Gefecht
            case "--ki-gruppenangriff-check": _kiGaCheck = 20000; return true;
        }
        if (a.StartsWith("--ki-gruppenangriff-check=") && int.TryParse(a["--ki-gruppenangriff-check=".Length..], out int n))
        {
            _kiGaCheck = n;
            return true;
        }
        return false;
    }

    /// <summary>Der synchrone Pruefstand; true = er lief (und beendet).</summary>
    private bool KiGruppenangriffPruefstaende()
    {
        if (_kiGaCheck <= 0) return false;
        GD.Print(_entities.KiGruppenangriffCheck(_kiGaCheck));
        GetTree().Quit(0);
        return true;
    }
}
