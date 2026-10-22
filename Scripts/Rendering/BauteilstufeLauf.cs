using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ bug-589 — die Kommandozeile: <c>--bauteilstufe-check[=Bauteil]</c> (Prüfstand,
/// Simulation/BauteilstufeCheck.cs, Vorgabe 163 = 6x6 Reifen) und die Gegenschalter
/// <c>--bauteilstufe-global-alt</c> (Klammer aus der EXE-Tafel, Stand bis 0.6.10),
/// <c>--werteliste-global-alt</c> (Produktions-Werteliste aus der Grundtafel) und
/// <c>--klammerschreibweise-alt</c> (Leerzeichen vor der Klammer wie bis 0.6.10).
/// </summary>
public partial class MapViewer
{
    private int _bauteilstufeCheck;

    /// <summary>true = Schalter erkannt.</summary>
    private bool BauteilstufeSchalter(string a)
    {
        if (a == "--bauteilstufe-check") { _bauteilstufeCheck = 163; return true; }
        if (a.StartsWith("--bauteilstufe-check="))
        {
            _bauteilstufeCheck = int.TryParse(a["--bauteilstufe-check=".Length..], out int b) && b > 0 ? b : 163;
            return true;
        }
        switch (a)
        {
            case "--bauteilstufe-global-alt": UI.UnitStatBook.BauteilStufeGlobalAlt = true; return true;
            case "--werteliste-global-alt": UI.BaseWindow.WertelisteGlobalAlt = true; return true;
            case "--klammerschreibweise-alt": UI.UnitStatBook.KlammerschreibweiseAlt = true; return true;
        }
        return false;
    }

    /// <summary>Nach dem Laden: true = der Prüfstand lief (und hat beendet).
    /// ⚠ Ruft selbst <c>Quit</c> — kein <c>--quit-after</c> dazugeben.</summary>
    private bool BauteilstufePruefstand()
    {
        if (_bauteilstufeCheck <= 0) return false;
        GD.Print(_entities.BauteilstufeCheck(_bauteilstufeCheck));
        GetTree().Quit(0);
        return true;
    }
}
