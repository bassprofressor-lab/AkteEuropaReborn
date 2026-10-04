using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ <b>PAKET 3 BEWEGUNG — die Kommandozeile</b> (04.10.2026, bug-425): Schalter
/// <c>--anfahren-check[=c,r]</c> (Prüfstand, Simulation/AnfahrenCheck.cs),
/// <c>--zielwahl-alt</c> (Gegenschalter: Zielwahl und Absender wie HEAD feec479),
/// <c>--geduld-satzwert</c> (Messschalter: Geduld beim Fahrbefehl aus dem Kartensatz
/// +0x1C, Bericht bewegung-anfahren-fable §3 C).
/// </summary>
public partial class MapViewer
{
    private bool _anfahrenCheck;

    /// <summary>true = Schalter erkannt.</summary>
    private bool AnfahrenSchalter(string a)
    {
        switch (a)
        {
            case "--anfahren-check": _anfahrenCheck = true; return true;
            case "--zielwahl-alt": MapEntityLayer.ZielwahlAlt = true; return true;
            case "--geduld-satzwert": MapEntityLayer.GeduldSatzwert = true; return true;
        }
        if (a.StartsWith("--anfahren-check="))
        {
            var q = a["--anfahren-check=".Length..].Split(',');
            if (q.Length == 2 && int.TryParse(q[0], out int c) && int.TryParse(q[1], out int r))
                MapEntityLayer.AnfahrenPulkZiel = new Vector2I(c, r);
            _anfahrenCheck = true;
            return true;
        }
        return false;
    }

    /// <summary>Nach dem Laden: true = der Prüfstand lief (und hat beendet).
    /// ⚠ Ruft selbst <c>Quit</c> — kein <c>--quit-after</c> dazugeben.</summary>
    private bool AnfahrenPruefstand()
    {
        if (!_anfahrenCheck) return false;
        GD.Print(_entities.AnfahrenCheck());
        GetTree().Quit(0);
        return true;
    }
}
