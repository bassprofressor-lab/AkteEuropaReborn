namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 30.10.2026 — bug-719..721, Teleporter, Illusion und Spiegelung (Simulation/Teleporter.cs,
/// Simulation/Illusion.cs): Schalter und Pruefstaende an einer Stelle. Bau:
/// <c>berichte/bau-teleporter.md</c>.
/// </summary>
public partial class MapViewer
{
    private bool _teleCheck, _illusionCheck, _spiegelCheck;

    /// <summary>Die Schalter; true = verbraucht.</summary>
    private bool TeleporterSchalter(string a)
    {
        switch (a)
        {
            case "--teleporter-aus": MapEntityLayer.TeleporterAus = true; return true;
            case "--teleporter-depot-alt": MapEntityLayer.TeleporterDepotAlt = true; return true;     // bug-724
            case "--zwilling-aussenden-alt": MapEntityLayer.ZwillingAussendenAlt = true; return true; // bug-725
            case "--spiegel-flackern-alt": MapEntityLayer.SpiegelFlackernAlt = true; return true;     // bug-726
            case "--illusion-aus": MapEntityLayer.IllusionAus = true; return true;
            case "--spiegelung-aus": MapEntityLayer.SpiegelungAus = true; return true;
            case "--teleport-check": _teleCheck = true; return true;
            case "--illusion-check": _illusionCheck = true; return true;
            case "--spiegelung-check": _spiegelCheck = true; return true;
        }
        return false;
    }

    /// <summary>Die synchronen Pruefstaende; true = einer lief (und beendet).</summary>
    private bool TeleporterPruefstaende()
    {
        if (!_teleCheck && !_illusionCheck && !_spiegelCheck) return false;
        if (_teleCheck) GD.Print(_entities.TeleportCheck());
        if (_illusionCheck) GD.Print(_entities.IllusionCheck());
        if (_spiegelCheck) GD.Print(_entities.SpiegelungCheck());
        GetTree().Quit(0);
        return true;
    }
}
