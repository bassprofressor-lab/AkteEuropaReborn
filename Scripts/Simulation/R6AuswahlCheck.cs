namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Die Pruefstaende zu KayelGee Runde 6 (Meldung vom 14.10.2026), Teil
/// »Auswahl, Depot, Einnahme« — <c>--r6-check=NAME</c>. Jeder Teil hat einen
/// Aufbau (<see cref="R6Start"/>) und eine Auswertung nach einigen Sekunden
/// (<see cref="R6Auswerten"/>); der MapViewer beendet den Lauf danach selbst.
/// </summary>
public partial class MapEntityLayer
{
    private int _r6Basis = -1;
    private readonly List<int> _r6Insassen = new();

    public string R6Start(string name) => name switch
    {
        "tuermarken" => R6TuermarkenStart(),
        _ => $"r6-check: unbekannter Teil »{name}«",
    };

    public string R6Auswerten(string name) => name switch
    {
        "tuermarken" => R6TuermarkenAuswerten(),
        _ => "",
    };

    // ======================================================================
    //  tuermarken — bug-560 (R6#4): zwei graue Balken vor dem Depot
    // ======================================================================
    //
    // Aufbau: eine FREMDE Basis mit Tuer bekommt zwei Insassen (einer
    // eingefahren, UKOL 50, einer ausgesandt und wartend, UKOL 51), dann nimmt
    // der Spieler sie ueber CaptureDone ein — derselbe Abschluss wie im Spiel.
    // Gezaehlt wird nach 3 s: Tuermarken, die der Zeichner malen wuerde, und
    // Lebensbalken (Stellung 1) im Umkreis der Tuer.
    // NULLMODELL --tuermarken-alt: 2 Marken (Tuer + Zelle davor) im Abbau.
    private string R6TuermarkenStart()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        int fremd = me == 1 ? 2 : 1;
        for (int k = 0; k < _entities.Count; k++)
        {
            var b = _entities[k];
            if (b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 1 && b.DoorCells.Count > 0 && Capturable(b))
            { _r6Basis = k; break; }
        }
        if (_r6Basis < 0) return "r6-check tuermarken: keine einnehmbare Basis — sagt NICHTS";
        var basis = _entities[_r6Basis];
        basis.Owner = basis.Team = basis.ShownOwner = fremd;
        _r6Insassen.Clear();
        for (int k = 0; k < _entities.Count && _r6Insassen.Count < 2; k++)
        {
            var x = _entities[k];
            if (x.Dead || x.IsProp || x.IsBuilding || !x.Mobile || x.Infantry >= 0 || x.GameUnitType != 0
                || x.Ukol >= 50 || Untergestellt(x)) continue;
            x.Owner = x.Team = fremd;
            Einfahren(basis, k, x);
            _r6Insassen.Add(k);
        }
        if (_r6Insassen.Count == 2) _entities[_r6Insassen[1]].Ukol = UkolVerlaesst;   // ausgesandt, wartet
        CaptureDone(_r6Basis, basis, me);
        return $"r6-check tuermarken: Basis {basis.Slot} ({basis.Col},{basis.Row}) von P{fremd} mit {_r6Insassen.Count} Insassen "
             + $"(50 + 51) von P{me} eingenommen; Fortschritt nach Abschluss {basis.CaptureProgress}/{basis.CaptureTotal}, "
             + $"Eindringling {basis.Intruder}" + (TuermarkenAlt ? "  ⚠ Nullmodell --tuermarken-alt" : "");
    }

    private string R6TuermarkenAuswerten()
    {
        if (_r6Basis < 0) return "r6-check tuermarken: kein Aufbau";
        var basis = _entities[_r6Basis];
        int tc = basis.Col + basis.DoorCol, tr = basis.Row + basis.DoorRow;
        int marken = TuermarkenGezeichnet();
        int balken = 0, tote = 0;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp) continue;
            if (Mathf.Abs(e.Col - tc) > 2 || e.Row < tr - 1 || e.Row > tr + 4) continue;
            if (e.Dead) { tote++; continue; }
            if (TabBalkenFuer(e, 1) != null) balken++;
        }
        int insassenTot = 0;
        foreach (int k in _r6Insassen) if (_entities[k].Dead) insassenTot++;
        bool ok = marken == 0 && insassenTot == _r6Insassen.Count;
        return "r6-check tuermarken:" +
               $"\n   nach 3 s: Fortschritt {basis.CaptureProgress}/{basis.CaptureTotal} (Abbau), Eindringling {basis.Intruder}" +
               $"\n   Tuermarken, die der Zeichner malt: {marken} (Farbe {(basis.Intruder < 0 || basis.Intruder > 7 ? "PropColor hellgrau" : "Spieler")})" +
               $"\n   Insassen geloescht {insassenTot}/{_r6Insassen.Count}, Lebensbalken lebender Einheiten an der Tuer: {balken}, tote Saetze dort: {tote}" +
               $"\n   {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}";
    }
}
