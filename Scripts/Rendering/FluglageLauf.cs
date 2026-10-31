namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 30.10.2026 — <b>bug-696, die flache Fluglage</b> (0x42E340, <c>Simulation/Fluglage.cs</c>,
/// Bericht <c>berichte/bau-fluglage.md</c>): Schalter und Pruefstand an einer Stelle.
/// </summary>
public partial class MapViewer
{
    private bool _fluglageCheck, _fliegerKlickkastenCheck, _flugtempoYCheck, _fluglageUmrechnungCheck, _fliegerAnkerCheck;

    /// <summary>Die Schalter von bug-696; true = verbraucht.</summary>
    private bool FluglageSchalter(string a)
    {
        switch (a)
        {
            case "--fluglage-alt": MapEntityLayer.FluglageAlt = true; return true;
            case "--fluglage-check": _fluglageCheck = true; return true;
            // bug-717 Klickkasten 0x431F7F, bug-718 Flugtempo in y, Umrechnung alter Staende
            case "--flieger-klickkasten-alt": MapEntityLayer.FliegerKlickkastenAlt = true; return true;
            case "--flieger-klickkasten-check": _fliegerKlickkastenCheck = true; return true;
            case "--flugtempo-y-alt": MapEntityLayer.FlugtempoYAlt = true; return true;
            case "--flugtempo-y-check": _flugtempoYCheck = true; return true;
            case "--fluglage-umrechnung-aus": MapEntityLayer.FluglageUmrechnungAus = true; return true;
            case "--fluglage-umrechnung-check": _fluglageUmrechnungCheck = true; return true;
            // bug-727 Bildanker der Flieger (0x42B8A6/0x42BAC9/0x42D6AA)
            case "--flieger-anker-alt": MapEntityLayer.FliegerAnkerAlt = true; return true;
            case "--flieger-anker-check": _fliegerAnkerCheck = true; return true;
            case "--rotorschatten-alt": MapEntityLayer.RotorschattenAlt = true; return true;   // bug-728
        }
        return false;
    }

    /// <summary>Der synchrone Pruefstand; true = er lief (und beendet).</summary>
    private bool FluglagePruefstaende()
    {
        string zeile = _fluglageCheck ? _entities.FluglageCheck()
                      : _fliegerKlickkastenCheck ? _entities.FliegerKlickkastenCheck()
                      : _flugtempoYCheck ? _entities.FlugtempoYCheck()
                      : _fluglageUmrechnungCheck ? _entities.FluglageUmrechnungCheck()
                      : _fliegerAnkerCheck ? _entities.FliegerAnkerCheck() : null;
        if (zeile == null) return false;
        GD.Print(zeile);
        GetTree().Quit(0);
        return true;
    }
}
