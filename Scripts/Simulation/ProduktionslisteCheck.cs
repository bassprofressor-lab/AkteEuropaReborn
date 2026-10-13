namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;

/// <summary>
/// <c>--produktionsliste-check</c> (bug-510, KayelGee R5#11 = R4#19): die
/// PRODUKTIONSLISTE der Basis — <see cref="BuildPanelRows"/>, genau die Zeilen,
/// die der Spieler im Basisfenster sieht. ⚠ Nicht das Erstellen-Fenster: das hat
/// <c>--fensterwerte-check</c> gemessen, und deshalb war der Fix aus Runde 4 gruen
/// und die Meldung trotzdem offen.
///
/// <para>Soll in der Kampagne: die Entwuerfe des Fahrplans bis zu dieser Mission
/// (M2: »Maschinengewehr«) plus eigene Entwuerfe plus erforschte
/// Ausruestungstraeger. Dafuer legt er einen eigenen Entwurf an (wie das
/// Erstellen-Fenster, Slot −1) — der MUSS erscheinen. Gehoert dem Spieler keine
/// Basis, wird die erste Basis der Karte fuer die Messung zugeschrieben und wieder
/// zurueckgegeben (gesagt). Nullmodell <c>--bauliste-grundtafel-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string ProduktionslisteCheck()
    {
        LoadDesigns();
        var sb = new StringBuilder("produktionsliste-check" +
                                   (BaulisteGrundtafelAlt ? " (⚠ Nullmodell --bauliste-grundtafel-alt)" : ""));
        if (_designs == null) return sb.Append(": keine Entwuerfe — sagt NICHTS").ToString();

        // ein eigener Entwurf, wie ihn das Erstellen-Fenster anlegt
        var mg = _designs.Find(d => d.Name == "Maschinengewehr");
        var vorbild = mg.Name != null ? mg : _designs[0];
        const string eigen = "Pruefentwurf";
        _designs.Add(new Design(eigen, vorbild.Propulsion, vorbild.Equip, vorbild.Weapon, true, -1));

        int vorher = _selected;
        Entity? basis = null; int besitzer = -99;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.BType != 1) continue;
            if (basis == null || e.Owner == ViewPlayer) { basis = e; _selected = i; }
            if (e.Owner == ViewPlayer) break;
        }
        if (basis == null) return sb.Append(": keine Basis auf der Karte — sagt NICHTS").ToString();
        if (basis.Owner != ViewPlayer)
        {
            besitzer = basis.Owner;
            basis.Owner = ViewPlayer;
            sb.Append($"\n   ⚠ EINGRIFF: Basis Platz {basis.Slot} (Spieler {besitzer}) fuer die Messung dem Betrachter zugeschrieben");
        }
        var rows = BuildPanelRows();
        if (besitzer != -99) basis.Owner = besitzer;
        _selected = vorher;
        _designs.RemoveAt(_designs.Count - 1);

        var namen = new List<string>();
        foreach (var r in rows) namen.Add(r.Name);
        var soll = new SortedSet<string> { eigen };
        foreach (var d in _designs) if (d.Slot < 0 && FitsFactory(d, 1)) soll.Add(d.Name);   // eigene Entwuerfe
        if (_fahrplanFrei != null)
            foreach (var d in _designs)
                if (d.Slot >= 0 && _fahrplanFrei.Contains(d.Slot % DesignsPerPlayer) && FitsFactory(d, 1))
                    soll.Add(d.Name);
        var ist = new SortedSet<string>(namen);
        var fremd = new SortedSet<string>(ist); fremd.ExceptWith(soll);
        var fehlt = new SortedSet<string>(soll); fehlt.ExceptWith(ist);
        // Ausruestungstraeger, die das Skript besitzen laesst, duerfen dazukommen
        int traeger = fremd.RemoveWhere(n => _designs.Exists(d => d.Name == n && d.Weapon >= 65 && d.Weapon <= 88
                                                    && _kampagnenTraeger.Contains(d.Weapon)));
        sb.Append($"\n   Ausruestungen im Besitz (Skript): {(_kampagnenTraeger.Count == 0 ? "keine" : string.Join(",", _kampagnenTraeger))}"
                  + (traeger > 0 ? $" -> {traeger} Traegerzeilen zugelassen" : ""));
        sb.Append($"\n   Mission {UI.SkirmishSetup.CampaignMission}, Fahrplan {(_fahrplanFrei == null ? "—" : string.Join(",", _fahrplanFrei))}");
        sb.Append($"\n   {rows.Count} Zeilen: {string.Join(", ", namen.Count > 12 ? namen.GetRange(0, 12) : namen)}{(namen.Count > 12 ? " …" : "")}");
        sb.Append($"\n   Soll: {string.Join(", ", soll)}");
        if (fehlt.Count > 0) sb.Append($"\n   FEHLT: {string.Join(", ", fehlt)}");
        if (fremd.Count > 0) sb.Append($"\n   ZU VIEL ({fremd.Count}): {string.Join(", ", fremd.Count > 10 ? new List<string>(fremd).GetRange(0, 10) : new List<string>(fremd))}");
        sb.Append($"\n   {(fehlt.Count == 0 && fremd.Count == 0 ? "BESTANDEN" : "DURCHGEFALLEN")}");
        return sb.ToString();
    }
}
