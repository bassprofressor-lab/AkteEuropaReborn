namespace AkteEuropaReborn.Rendering;

using System.Text;

/// <summary>
/// <c>--werteliste-eigen-check</c> (bug-586, KayelGee R6: »Erstellte Einheiten
/// zeigen im Produktionsfenster nicht ihre Werte an«). Ablauf wie im Spiel: das
/// Wertebuch ist schon gelesen (das Basisfenster war offen), dann legt der Spieler
/// im Erstellungsfenster einen Entwurf an (echter Weg <c>DesignerInput(0, 0,
/// true)</c>, <c>designs.cfg</c> bleibt unberührt), und gemessen wird
/// <list type="number">
/// <item>die Zeile steht in der Produktionsliste der Basis (<see cref="BuildPanelRows"/>),</item>
/// <item>die Werteliste rechts (<c>BaseWindow.StatsFor</c>) ist NICHT leer,</item>
/// <item>»Energie« und »A/V« darin sind die der Spielformel
/// (<see cref="Simulation.DesignMath.Compute"/>), dieselben, die das Erstellen meldet.</item>
/// </list>
/// Nullmodell <c>--werteliste-nachtrag-alt</c>: Teil 2 MUSS durchfallen.
/// </summary>
public partial class MapEntityLayer
{
    public string WertelisteEigenCheck()
    {
        var sb = new StringBuilder("werteliste-eigen-check (bug-586)"
                                   + (UI.UnitStatBook.NachtragAlt ? " (⚠ Nullmodell --werteliste-nachtrag-alt: Teil 2 MUSS fallen)" : ""));
        LoadDesigns();
        LoadDesignParts();
        bool alleTeile = false;
        if (!Designer.Ready)
        {
            // ⚠ EINGRIFF wie --entwurf-dunkel-check: K1 besitzt noch keine Teile
            alleTeile = true; EntwurfAlleTeile = true; LoadDesignParts();
            sb.Append("\n   ⚠ EINGRIFF: EntwurfAlleTeile (die Mission besitzt noch keine Teile)");
        }
        if (_designs == null || !Designer.Ready) return sb.Append("\n   keine Entwurfslisten — sagt NICHTS\n   Ergebnis: DURCHGEFALLEN").ToString();
        UI.UnitStatBook.Load();                                     // das Basisfenster war schon offen
        sb.Append($"\n   Wertebuch gelesen, {System.Linq.Enumerable.Count(UI.UnitStatBook.All())} Eintraege");

        bool aktiv = Designer.Active;
        if (!aktiv) Designer.Toggle();
        // eine Kombination, die es noch nicht gibt
        while (Designer.Cursor != DesignScreen.Row.Propulsion) Designer.Move(1);
        string name = Designer.ProposedName();
        for (int k = 0; k < 40 && _designs.Exists(d => d.Name == name); k++)
        {
            Designer.Change(1);
            name = Designer.ProposedName();
        }
        int p = Designer.CurrentPropulsion?.Id ?? 0, w = Designer.CurrentWeapon?.Id ?? 0,
            q = Designer.CurrentEquipment?.Id ?? 0;
        int vorher = _designs.Count;
        _entwurfProbe = true;                                       // designs.cfg bleibt unberuehrt
        DesignerInput(0, 0, true);
        _entwurfProbe = false;
        if (!aktiv) Designer.Close();
        bool angelegt = _designs.Count == vorher + 1;
        sb.Append($"\n   {(angelegt ? "ok  " : "FEHL")} Teil 0: Entwurf »{name}« angelegt (Fahrwerk {p}, Aufbau {w}, Verbesserung {q})"
                  + (angelegt ? "" : $" »{Designer.Message}«"));
        if (!angelegt) return sb.Append("\n   Ergebnis: DURCHGEFALLEN").ToString();

        // Teil 1: in der Produktionsliste der Basis
        int sel = _selected;
        Entity? basis = null; int besitzer = -99;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.BType != 1) continue;
            if (basis == null || e.Owner == ViewPlayer) { basis = e; _selected = i; }
            if (e.Owner == ViewPlayer) break;
        }
        bool zeile = false;
        if (basis != null)
        {
            if (basis.Owner != ViewPlayer) { besitzer = basis.Owner; basis.Owner = ViewPlayer; }
            foreach (var r in BuildPanelRows()) if (r.Name == name) zeile = true;
            if (besitzer != -99) basis.Owner = besitzer;
        }
        _selected = sel;
        if (basis == null) zeile = true;                          // nicht messbar, kein Urteil
        sb.Append($"\n   {(basis == null ? "--  " : zeile ? "ok  " : "FEHL")} Teil 1: Zeile in der Produktionsliste"
                  + (basis == null ? " — KEIN URTEIL, keine Basis auf der Karte" : besitzer != -99 ? $" (⚠ Eingriff: Basis Platz {basis.Slot} kurz dem Betrachter zugeschrieben)" : ""));

        // Teil 2/3: die Werteliste rechts
        string werte = UI.BaseWindow.StatsFor(name);
        var soll = Simulation.DesignMath.Compute(w, p, q);
        bool da = werte.Length > 0;
        bool stimmt = da && werte.Contains($"Energie : {soll.Hp}\n") && werte.Contains($"A/V {soll.Attack}/{soll.Defence}\n");
        sb.Append($"\n   {(da ? "ok  " : "FEHL")} Teil 2: Werteliste {(da ? "gefuellt" : "LEER")}: "
                  + (da ? werte.Replace("\n", " | ") : "—"));
        sb.Append($"\n   {(stimmt ? "ok  " : "FEHL")} Teil 3: Soll Energie {soll.Hp}, A/V {soll.Attack}/{soll.Defence}, "
                  + $"Geschw. {soll.Speed}, Sprit {soll.Fuel} (DesignMath.Compute)");

        // aufraeumen
        if (alleTeile) { EntwurfAlleTeile = false; LoadDesignParts(); }
        _designs.RemoveAll(d => d.Slot < 0 && d.Name == name);
        UI.UnitStatBook.Austragen(name);

        bool alles = zeile && da && stimmt;
        if (UI.UnitStatBook.NachtragAlt)
            return sb.Append($"\n   Ergebnis: {(!da ? "NULLMODELL ROT wie erwartet (Teil 2 leer)" : "⚠ NULLMODELL GRUEN — der Pruefstand misst nichts")}").ToString();
        return sb.Append($"\n   Ergebnis: {(alles ? "BESTANDEN" : "DURCHGEFALLEN")}").ToString();
    }
}
