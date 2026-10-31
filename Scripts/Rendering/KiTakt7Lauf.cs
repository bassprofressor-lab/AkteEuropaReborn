namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 30.10.2026 — bug-703..705: KI-Platz 7 auch mit Zielliste, der Denktakt des Originals
/// in der Kampagne, die Registerschleifen der Setup-Ziele. Schalter und Pruefstand an einer
/// Stelle. Bau: <c>berichte/bau-ki-takt7.md</c>.
/// </summary>
public partial class MapViewer
{
    private int _kiTakt7Check;

    /// <summary>Die Schalter von bug-703..705; true = verbraucht.</summary>
    private bool KiTakt7Schalter(string a)
    {
        switch (a)
        {
            case "--ki-takt7-nur-ohne-ziele": MapEntityLayer.KiTakt7NurOhneZiele = true; return true;   // bug-703 Gegenschalter
            case "--ki-denktakt-alt": MapEntityLayer.KiDenktaktAlt = true; return true;                 // bug-704 Gegenschalter
            case "--setup-ziele-eigen-alt": MapEntityLayer.SetupZieleEigenAlt = true; return true;      // bug-705 Gegenschalter
            case "--ki-takt7-check": _kiTakt7Check = 1500; return true;
        }
        if (a.StartsWith("--ki-takt7-check=") && int.TryParse(a["--ki-takt7-check=".Length..], out int n))
        {
            _kiTakt7Check = n;
            return true;
        }
        return false;
    }

    /// <summary>Der synchrone Pruefstand; true = er lief (und beendet).</summary>
    private bool KiTakt7Pruefstaende()
    {
        if (_kiTakt7Check <= 0) return false;
        GD.Print(_entities.KiTakt7Check(_kiTakt7Check));
        GetTree().Quit(0);
        return true;
    }
}
