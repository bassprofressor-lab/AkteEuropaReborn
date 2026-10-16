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
        "gummiband" => R6GummibandStart(),
        "einfahrzone" => R6EinfahrzoneStart(),
        "klickkasten" => R6KlickkastenCheck(),
        _ => $"r6-check: unbekannter Teil »{name}«",
    };

    public string R6Auswerten(string name) => name switch
    {
        "tuermarken" => R6TuermarkenAuswerten(),
        "gummiband" => R6GummibandAuswerten(),
        "einfahrzone" => R6EinfahrzoneAuswerten(),
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

    // ======================================================================
    //  gummiband — bug-561 (R6#5/#5.1), bug-562 (R6#10, Nullmodell
    //  --gummiband-gebaeude-alt: die Basis wird mitgewaehlt)
    // ======================================================================
    //
    // Aufbau: eine Basis wird dem Spieler zugeschrieben (gesagt), SECHS eigene
    // Fahrzeuge fahren ueber Einfahren hinein, fuenf davon werden ausgesandt
    // (UKOL 51, wartend), eines bleibt eingefahren (50). Dann zieht der
    // Pruefstand ein Auswahlrechteck ueber Tuer, Vorplatz und Basiskoerper
    // (BoxSelect, derselbe Weg wie MapViewer beim Loslassen) und gibt der
    // Auswahl einen Fahrbefehl. Soll: 0 Depoteinheiten gewaehlt, 0 bekommen
    // einen Weg. NULLMODELL --gummiband-ukol-alt: 6 gewaehlt, Wege > 0.
    private int _r6gWege, _r6gGebaeude;
    private string R6GummibandStart()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        _r6Basis = -1;
        for (int k = 0; k < _entities.Count; k++)
        {
            var b = _entities[k];
            if (b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 1 && b.DoorCells.Count > 0) { _r6Basis = k; break; }
        }
        if (_r6Basis < 0) return "r6-check gummiband: keine Basis mit Tuer — sagt NICHTS";
        var basis = _entities[_r6Basis];
        basis.Owner = basis.Team = basis.ShownOwner = me;
        _r6Insassen.Clear();
        int tc = basis.Col + basis.DoorCol, tr = basis.Row + basis.DoorRow;
        for (int k = 0; k < _entities.Count && _r6Insassen.Count < DepotSlots; k++)
        {
            var x = _entities[k];
            if (x.Dead || x.IsProp || x.IsBuilding || !x.Mobile || x.Infantry >= 0 || x.GameUnitType != 0
                || x.Ukol >= 50 || Untergestellt(x)) continue;
            x.Owner = x.Team = me;
            // ⚠ Eingriff, gesagt: wie im Spiel steht sie beim Einfahren auf der Tuerzelle.
            ProbeVersetzen(k, tc, tr);
            Einfahren(basis, k, x);
            _r6Insassen.Add(k);
        }
        for (int n = 1; n < _r6Insassen.Count; n++) _entities[_r6Insassen[n]].Ukol = UkolVerlaesst;
        var a = CellCenter(tc - 3, basis.Row) - new Vector2(0, 30);
        var z = CellCenter(tc + 3, tr + 3);
        _sel.Clear();
        int uebVor = GummibandUebersprungen;
        BoxSelect(new Rect2(a, z - a), false);
        int depotGewaehlt = 0, gebaeude = 0;
        foreach (int i in _sel)
        {
            if (_r6Insassen.Contains(i)) depotGewaehlt++;
            if (_entities[i].IsBuilding) gebaeude++;
        }
        int gewaehlt = _sel.Count;
        IssueMove(CellCenter(tc, System.Math.Min(tr + 8, (_nav?.Height ?? tr + 9) - 1)));
        _r6gGebaeude = gebaeude;
        _r6gWege = 0;
        foreach (int k in _r6Insassen) if (_entities[k].Path is { Count: > 0 }) _r6gWege++;
        return $"r6-check gummiband: Basis {basis.Slot}, Tuer ({tc},{tr}), {_r6Insassen.Count} Einheiten drin (1x 50, {_r6Insassen.Count - 1}x 51)"
             + (GummibandUkolAlt ? "  ⚠ Nullmodell --gummiband-ukol-alt" : "")
             + $"\n   Rechteck ueber Basis, Tuer und Vorplatz: {gewaehlt} gewaehlt, davon Depoteinheiten {depotGewaehlt}, Gebaeude {gebaeude}; "
             + $"am UKOL-Riegel uebersprungen {GummibandUebersprungen - uebVor}"
             + $"\n   Fahrbefehl an die Auswahl: Depoteinheiten mit Weg {_r6gWege}";
    }

    private string R6GummibandAuswerten()
    {
        if (_r6Basis < 0) return "r6-check gummiband: kein Aufbau";
        var basis = _entities[_r6Basis];
        int tc = basis.Col + basis.DoorCol, tr = basis.Row + basis.DoorRow;
        int drin = 0, unsichtbarUnterwegs = 0;
        foreach (int k in _r6Insassen)
        {
            var u = _entities[k];
            if (!basis.Garage.Contains(u)) continue;
            drin++;
            if (u.Col != tc || u.Row != tr || u.Path is { Count: > 0 }
                || u.Pos.DistanceTo(CellCenter(tc, tr)) > 1f) unsichtbarUnterwegs++;
        }
        // bug-562: kein Gebaeude im Rechteck gewaehlt (0x434700 kennt nur Einheitensaetze).
        bool ok = _r6gWege == 0 && unsichtbarUnterwegs == 0 && _r6gGebaeude == 0;
        return "r6-check gummiband:" +
               $"\n   nach 3 s: noch in der Liste {drin}, davon unsichtbar unterwegs (Weg oder fort von der Tuer) {unsichtbarUnterwegs}; "
             + $"Auslass {DepotAusgelassen}, gewartet {DepotWartetAnTuer}"
             + $"\n   {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}";
    }

    // ======================================================================
    //  einfahrzone — bug-564 (R6#9): oberer Teil der Depottuer
    // ======================================================================
    //
    // Aufbau: eigene Basis (gesagt), ein eigenes Fahrzeug A drei Zeilen unter der
    // Tuer, gewaehlt. Die Maus steht auf der Zelle UEBER der Tuer. Soll: Zeiger
    // Einfahrt, der Klick (KartenBefehl-Weg: EinfahrtZiel -> PostMove) zielt auf
    // die Tuerzelle; nach 3 s ist A eingefahren oder steht auf der Tuer.
    // NULLMODELL --einfahrzone-alt: Zeiger »eigenes Gebaeude«, kein Fahrbefehl.
    private int _r6eA = -1;
    private string R6EinfahrzoneStart()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        _r6Basis = -1;
        for (int k = 0; k < _entities.Count; k++)
        {
            var b = _entities[k];
            if (b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 1 && b.DoorCells.Count > 0) { _r6Basis = k; break; }
        }
        if (_r6Basis < 0) return "r6-check einfahrzone: keine Basis mit Tuer — sagt NICHTS";
        var basis = _entities[_r6Basis];
        basis.Owner = basis.Team = basis.ShownOwner = me;
        int tc = basis.Col + basis.DoorCol, tr = basis.Row + basis.DoorRow;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != me || e.Infantry >= 0 || Untergestellt(e)) continue;
            _r6eA = i; break;
        }
        if (_r6eA < 0) return "r6-check einfahrzone: kein eigenes Fahrzeug — sagt NICHTS";
        var a = _entities[_r6eA];
        var frei = _nav!.NearestFree(new Vector2I(tc, tr + 3), a.Move, _r6eA) ?? new Vector2I(tc, tr + 3);
        ProbeVersetzen(_r6eA, frei.X, frei.Y);
        a.Path = null; a.Orders.Clear(); a.Target = -1; a.Ukol = UkolFrei;
        _sel.Clear(); _sel.Add(_r6eA); _selected = _r6eA;
        var oben = CellCenter(tc, tr - 1);
        var hint = CursorHintAt(oben);
        bool befiehlt = hint is not (Hint.Own or Hint.OwnFoot or Hint.Neutral);
        var ziel = EinfahrtZiel(oben);
        var zz = CellAt(ziel);
        if (befiehlt) PostMove(ziel, false, false);
        _r6eHint = hint; _r6eZiel = zz ?? new Vector2I(-1, -1);
        return "r6-check einfahrzone" + (EinfahrzoneAlt ? "  ⚠ Nullmodell --einfahrzone-alt" : "") +
               $"\n   Basis {basis.Slot}, Tuer ({tc},{tr}); Einheit {a.Slot} auf ({a.Col},{a.Row}) gewaehlt; Maus auf ({tc},{tr - 1}) ueber der Tuer" +
               $"\n   Zeiger {hint} -> Linksklick {(befiehlt ? $"BEFIEHLT, Ziel ({_r6eZiel.X},{_r6eZiel.Y})" : "WAEHLT AN (die Basis)")}";
    }
    private Hint _r6eHint;
    private Vector2I _r6eZiel;

    private string R6EinfahrzoneAuswerten()
    {
        if (_r6Basis < 0 || _r6eA < 0) return "r6-check einfahrzone: kein Aufbau";
        var basis = _entities[_r6Basis];
        var a = _entities[_r6eA];
        int tc = basis.Col + basis.DoorCol, tr = basis.Row + basis.DoorRow;
        bool drin = basis.Garage.Contains(a);
        bool aufTuer = a.Col == tc && a.Row == tr;
        bool ok = _r6eHint == Hint.Einfahrt && _r6eZiel == new Vector2I(tc, tr) && (drin || aufTuer || a.Path is { Count: > 0 });
        return "r6-check einfahrzone:" +
               $"\n   nach 3 s: Einheit eingefahren {drin}, auf der Tuer {aufTuer}, steht auf ({a.Col},{a.Row}), Weg {(a.Path?.Count ?? 0)}" +
               $"\n   {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}";
    }

    // ======================================================================
    //  klickkasten — R6#12 »Einheiten anwaehlbar, wenn die Maus weit oberhalb ist«
    // ======================================================================
    //
    // Kein Bau: misst, wie weit der Klickkasten (BodyRect) ueber das sichtbare Bild
    // reicht, und stellt den Kasten des Originals daneben. Original-Treffertest
    // Fahrzeug C 0x431CBC..0x431CE6: mx+3 > sx, sy+10 < my, sx+40 > mx, sy+60 > my,
    // also x [sx-2, sx+39], y [sy+11, sy+59]; Blitpunkt C 0x4301D1/0x430212
    // sy = Zeile*20 - 35 - Hub (+ Feinversatz), sx = Spalte*40 - 20 (+ Fein) —
    // in unseren Koordinaten sx = Pos.x - 20, sy = Pos.y - 45 (EinheitenAnker 24,45).
    // NULLMODELL: der Originalkasten ist rein gerechnet; weicht unser Kasten um
    // mehr als 2 px ab, ist es UNSER Fehler, sonst originaltreu.
    private string R6KlickkastenCheck()
    {
        int n = 0, abw = 0;
        float kSum = 0, kMin = float.MaxValue, kMax = float.MinValue, oSum = 0;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry >= 0 || e.GameUnitType != 0 || Untergestellt(e)) continue;
            if (e.Move == Simulation.NavGrid.MoveClass.Ship) continue;
            var tex = AuswahlBild(e);
            var img = tex?.GetImage();
            if (img == null) continue;
            int top = -1;
            for (int y = 0; y < img.GetHeight() && top < 0; y++)
                for (int x = 0; x < img.GetWidth(); x++)
                    if (img.GetPixel(x, y).A > 0.3f) { top = y; break; }
            if (top < 0) continue;
            float sichtOben = e.Pos.Y - EinheitenAnker(e).Y + top;
            var box = BodyRect(e);
            float origOben = e.Pos.Y - 45 + 11;
            float kopf = sichtOben - box.Position.Y;
            n++; kSum += kopf; kMin = Mathf.Min(kMin, kopf); kMax = Mathf.Max(kMax, kopf);
            oSum += sichtOben - origOben;
            if (Mathf.Abs(box.Position.Y - origOben) > 2f || Mathf.Abs(box.Position.X - (e.Pos.X - 22)) > 2f) abw++;
        }
        if (n == 0) return "r6-check klickkasten: keine Fahrzeuge mit Bild — sagt NICHTS";
        return "r6-check klickkasten:" +
               $"\n   {n} Fahrzeuge: Kasten reicht ueber den obersten sichtbaren Bildpunkt hinaus: Mittel {kSum / n:0.0} px (min {kMin:0}, max {kMax:0}); Original-Kasten: Mittel {oSum / n:0.0} px" +
               $"\n   Kasten bei uns Pos+(-21,-35) 42x49, Original Pos+(-22,-34) 42x49; Abweichung > 2 px bei {abw} Einheiten" +
               $"\n   {(abw == 0 ? "ORIGINALTREU (kein Bau)" : "ABWEICHUNG")}";
    }
}
