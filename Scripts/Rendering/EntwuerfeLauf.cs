using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 22.10.2026, Testerrunde 0.6.11 Paket D — die Kommandozeile der eigenen Entwürfe
/// (Simulation/EntwurfVerwalten.cs, Simulation/EntwurfVerwaltenCheck.cs,
/// UI/SchriftZweitfarbe.cs, UI/EntwurfDialog.cs).
/// <list type="bullet">
/// <item>bug-592: <c>--entwurf-verwalten-check</c>, Gegenschalter <c>--entwurf-knoepfe-alt</c></item>
/// <item>bug-593: <c>--eigene-zeile-check</c>, Gegenschalter <c>--eigene-zeile-alt</c>,
/// <c>--schrift-zweitfarbe-alt</c></item>
/// <item>bug-594: <c>--entwurf-lebensdauer-check</c>, Gegenschalter <c>--entwuerfe-global</c></item>
/// </list>
/// </summary>
public partial class MapViewer
{
    private UI.EntwurfDialog? _entwurfDialog;
    private string _entwuerfeCheck = "";

    /// <summary>true = Schalter erkannt.</summary>
    private bool EntwuerfeSchalter(string a)
    {
        switch (a)
        {
            case "--entwurf-verwalten-check":
            case "--eigene-zeile-check":
            case "--entwurf-lebensdauer-check":
                _entwuerfeCheck = a; return true;
            case "--entwurf-knoepfe-alt": MapEntityLayer.EntwurfKnoepfeAlt = true; return true;
            case "--eigene-zeile-alt": MapEntityLayer.EigeneZeileAlt = true; return true;
            case "--schrift-zweitfarbe-alt": UI.SchriftZweitfarbe.Alt = true; return true;
            case "--entwuerfe-global": MapEntityLayer.EntwuerfeGlobal = true; return true;
        }
        return false;
    }

    /// <summary>Nach dem Laden: true = ein Prüfstand lief (und hat beendet).
    /// ⚠ Ruft selbst <c>Quit</c> — kein <c>--quit-after</c> dazugeben.</summary>
    private bool EntwuerfePruefstand()
    {
        if (_entwuerfeCheck.Length == 0) return false;
        // Eine Ausnahme darf den Lauf nicht weiterspielen lassen — immer beenden.
        try
        {
            GD.Print(_entwuerfeCheck switch
            {
                "--entwurf-verwalten-check" => _entities.EntwurfVerwaltenCheck(),
                "--eigene-zeile-check" => _entities.EigeneZeileCheck(),
                _ => _entities.EntwurfLebensdauerCheck(),
            });
        }
        catch (System.Exception ex)
        {
            GD.Print($"{_entwuerfeCheck}: AUSNAHME {ex.GetType().Name}: {ex.Message}\n   Ergebnis: DURCHGEFALLEN");
        }
        GetTree().Quit(0);
        return true;
    }
}
