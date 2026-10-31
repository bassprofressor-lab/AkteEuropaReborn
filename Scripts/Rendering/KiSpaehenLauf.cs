namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 30.10.2026 — bug-729..732, die KI-Schwaechen aus <c>berichte/gefecht-ki-stillstand-opus.md</c>
/// §6 (Spaehschleife, Spaehausfall, Sprit, Skriptplaetze): Schalter und Pruefstand an einer
/// Stelle. Bau: <c>berichte/bau-ki-spaehen-sprit.md</c>, Pruefstand Simulation/KiSpaehenCheck.cs.
/// </summary>
public partial class MapViewer
{
    private int _kiSpaehenCheck;

    /// <summary>Die Gegenschalter und der Pruefstand; true = verbraucht.</summary>
    private bool KiSpaehenSchalter(string a)
    {
        switch (a)
        {
            case "--spaehen-sperre-alt": MapEntityLayer.SpaehenSperreAlt = true; return true;            // bug-729
            case "--ki-sprit-aufnahme-alt": MapEntityLayer.KiSpritAufnahmeAlt = true; return true;       // bug-731
            case "--ki-ziel-skriptplatz-alt": MapEntityLayer.KiZielSkriptplatzAlt = true; return true;   // bug-732
            case "--ki-spaehen-check": _kiSpaehenCheck = 45000; return true;                             // 900 s
        }
        if (a.StartsWith("--ki-spaehen-check=") && int.TryParse(a["--ki-spaehen-check=".Length..], out int n))
        {
            _kiSpaehenCheck = n;
            return true;
        }
        return false;
    }

    /// <summary>Der synchrone Pruefstand; true = er lief (und beendet).</summary>
    private bool KiSpaehenPruefstaende()
    {
        if (_kiSpaehenCheck <= 0) return false;
        GD.Print(_entities.KiSpaehenCheck(_kiSpaehenCheck));
        GetTree().Quit(0);
        return true;
    }
}
