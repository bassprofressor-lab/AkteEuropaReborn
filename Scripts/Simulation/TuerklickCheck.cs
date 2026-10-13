namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--tuerklick-check</c> (bug-512, KayelGee R5#9): eine eigene Einheit A steht
/// direkt UNTER der Tuer einer eigenen Basis und ist gewaehlt, die Maus steht auf
/// der Tuerzelle. Soll: Zeiger »Einfahrt« (dann
/// befiehlt der Linksklick, MapViewer.LinksklickBefiehlt), und A faehrt ein.
/// Nullmodell <c>--tuerklick-einheit-alt</c>: Zeiger »eigene Einheit« — der Klick
/// waehlt A an.
///
/// <para>⚠ Zwei EINGRIFFE, gesagt: die Basis wird dem Spieler zugeschrieben
/// (K2: er nimmt sie erst ein), und A wird auf die Zelle unter der Tuer gesetzt.
/// Der Befehl geht ueber <see cref="PostMove"/>, den Weg, den KartenBefehl fuer
/// einen Einfahrklick nimmt.</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _tkB = -1;

    public string TuerklickCheck()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        Entity? basis = null;
        foreach (var x in _entities)
            if (x.IsBuilding && !x.Dead && x.BType == 1 && x.DoorCells.Count > 0) { basis = x; break; }
        if (basis == null) return "tuerklick-check: keine Basis mit Tuer — sagt NICHTS";
        basis.Owner = basis.Team = basis.ShownOwner = me;
        int tc = basis.Col + basis.DoorCol, tr = basis.Row + basis.DoorRow;

        int ia = -1, ib = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != me || e.Infantry >= 0) continue;
            if (ia < 0) ia = i; else if (ib < 0) { ib = i; break; }
        }
        if (ia < 0 || ib < 0) return "tuerklick-check: keine zwei eigenen Fahrzeuge — sagt NICHTS";
        var a = _entities[ia];
        _nav?.ClearOccupant(a.Col, a.Row, ia);
        a.Col = tc; a.Row = tr + 1; a.Path = null; a.Reserved = null;
        a.Pos = BodyCenter(a);
        _nav?.SetOccupant(a.Col, a.Row, ia, false, immobile: false);

        // ⚠ Seine Meldung meint DIESE Einheit: »stelle ich eine Einheit vor die
        // Tuer, kann ich DIE Einheit nicht mehr ins Depot schicken« — gewaehlt ist
        // also die Einheit vor der Tuer selbst; der Klick auf die Tuer waehlte sie
        // nur erneut an.
        _sel.Clear(); _sel.Add(ia); _selected = ia; _tkB = ia;
        var tuer = CellCenter(tc, tr);
        var hint = CursorHintAt(tuer);
        int pick = Pick(tuer);
        bool befiehlt = hint is not (Hint.Own or Hint.OwnFoot or Hint.Neutral);
        if (befiehlt) PostMove(tuer, false, false);
        return "tuerklick-check" + (TuerklickEinheitAlt ? " (⚠ Nullmodell --tuerklick-einheit-alt)" : "") +
               $"\n   Basis {basis.Slot}, Tuer ({tc},{tr}); Einheit {a.Slot} darunter auf ({a.Col},{a.Row}), sie selbst ist gewaehlt" +
               $"\n   Pick auf der Tuer trifft: {(pick == ia ? "die Einheit DAVOR" : pick >= 0 ? $"Satz {pick}" : "nichts")}" +
               $"\n   Zeiger: {hint}  ->  Linksklick {(befiehlt ? "BEFIEHLT (Fahrt zur Tuer)" : "WAEHLT AN")}" +
               $"\n   {(hint == Hint.Einfahrt ? "BESTANDEN" : "DURCHGEFALLEN")}";
    }

    /// <summary>Spaeter: ist B eingefahren?</summary>
    public string TuerklickNachher()
        => _tkB < 0 ? "" : $"tuerklick-check nachher: gewaehlte Einheit untergestellt = {Untergestellt(_entities[_tkB])} (Ukol {_entities[_tkB].Ukol}), " +
             $"steht auf ({_entities[_tkB].Col},{_entities[_tkB].Row}), Weg {(_entities[_tkB].Path?.Count ?? 0)} Zellen, Fahrgrund »{_entities[_tkB].Fahrgrund}«";
}
