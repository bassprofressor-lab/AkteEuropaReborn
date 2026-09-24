namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐⭐ <b>DER ZUG AUF ZERSCHOSSENEM GLEIS</b> (23.09.2026, bug-364,
/// berichte/k21-bahn-schaden-nachschub.md).
///
/// <para>Gemeldet in K21: »AI sowie meine Züge fahren auch auf zerstörten
/// strecken weiter«. Bei uns hatte ein Gleisbruch keine Wirkung auf den
/// Verkehr: <c>RailHit</c> setzte faze 3, <c>RailStep</c> behandelte die 3 aber
/// wie »unterwegs« und lieferte ab. Der Stillstand steht jetzt in
/// <c>RailStep</c>; hier steht der zweite Teil, die Zellpruefung des Waggons.</para>
///
/// <code>
/// Waggontakt 0x4C69C0(waggon), je betretene Zelle:
///   cl = sec20[spalte*256 + zeile]              @0x4C69FE (Lagentafel 0x542E18)
///   1 &lt;= cl &lt; 80 -> rail_broken(sp, ze)         @0x4C6A12 -> 0x4B0A00
///      heil          -> weiterfahren              @0x4C6A3A
///      zerschossen   -> Belegungsprobe            @0x4C6A1E
///   Belegungsprobe: word[0xBDEA80 + 2*zelle] in 60000..60300
///                   (Fussabdruck eines Gebaeudes) -> weiterfahren
///                   sonst -> 0x4C739C: zug_vernichten(waggon % 60)  -> 0x4C7990
///                   Protokoll »Exploding train« (0x539484)
/// </code>
///
/// <para>⚠ UNSERE SETZUNGEN, jede einzeln:</para>
/// <list type="bullet">
/// <item>Die Zelle des Waggons ist seine GERUNDETE Lage auf dem Zeichenweg —
///   das Original prueft beim Betreten, wir etwa eine halbe Zelle frueher.</item>
/// <item>Geprueft werden nur Gleiszellen DERSELBEN Linie. Das Original fragt
///   sec20 ohne Linie; an einer Kreuzung koennte ein Bruch der anderen Linie
///   auch diesen Zug treffen. Ohne die Einschraenkung liefe unsere Linie nach
///   der Explosion auf faze 0 wieder an und explodierte an derselben Stelle
///   erneut, weil ihre eigene Reparatur den fremden Bruch nicht kennt.</item>
/// <item>Der Fall cl == 0 (Waggon auf einer Zelle ganz ohne Gleis) faellt weg:
///   unsere Waggons fahren nur auf der Zellenkette der Linie.</item>
/// <item>Nach der Explosion steht die Linie auf faze 3, solange sie einen Bruch
///   hat (sonst 0). Was das Original nach 0x4C7990 in faze schreibt, ist
///   ungelesen (berichte §a, »(U) offen«).</item>
/// <item>Das Bild der Explosion ist unser »explosion« wie beim Absturz — der
///   Effekt von 0x4C7990 ist nicht gelesen.</item>
/// </list>
/// <para>Gegenschalter <c>--zug-faehrt-durch</c> (Stand vor dem 23.09.),
/// Pruefstand <c>--gleisbruch-zug-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Wie oft ein Zug an einem Bruch explodiert ist.</summary>
    public int ZugExplosionen;

    private readonly Dictionary<int, int> _zugExplosionenJeLinie = new();

    /// <summary>Gleiszellen nach Lage — die Zellpruefung laeuft je Bild und
    /// Waggon, eine Suche durch alle Gleiszellen waere dort zu teuer.</summary>
    private Dictionary<(int, int), List<RailCell>>? _gleisNachZelle;
    private int _gleisNachZelleAnzahl = -1;

    private List<RailCell>? GleisAuf(int c, int r)
    {
        if (_gleisNachZelle == null || _gleisNachZelleAnzahl != _railCells.Count)
        {
            _gleisNachZelle = new Dictionary<(int, int), List<RailCell>>();
            foreach (var z in _railCells)
            {
                if (!_gleisNachZelle.TryGetValue((z.Col, z.Row), out var l))
                    _gleisNachZelle[(z.Col, z.Row)] = l = new List<RailCell>();
                l.Add(z);
            }
            _gleisNachZelleAnzahl = _railCells.Count;
        }
        return _gleisNachZelle.TryGetValue((c, r), out var got) ? got : null;
    }

    /// <summary>rail_broken(sp, ze) 0x4B0A00 fuer DIESE Linie — ein Mast bricht
    /// nie (RailHit), er zaehlt also nicht.</summary>
    private bool GleisZerschossen(int c, int r, int linie)
    {
        var l = GleisAuf(c, r);
        if (l == null) return false;
        foreach (var z in l)
            if (z.Line == linie && z.Broken && !z.Pylon && z.Frame != 255) return true;
        return false;
    }

    /// <summary>Aus <c>UpdateFreight</c>, je Bild: steht ein sichtbarer Waggon
    /// eines fahrenden Zugs auf einem Bruch, explodiert der Zug.</summary>
    private void GleisbruchTakt()
    {
        if (GleisbruchZugCheckAn) GleisbruchCheckTakt();
        if (ZugFaehrtDurch) return;
        foreach (var l in _railLines)
        {
            if (!l.Rollt) continue;
            if (!_freightWagons.TryGetValue(l.Slot, out var list) || list.Count == 0) continue;
            foreach (var w in list)
            {
                if (w.Hidden) continue;
                int c = Mathf.RoundToInt(w.Col), r = Mathf.RoundToInt(w.Row);
                if (!GleisZerschossen(c, r, l.Slot)) continue;
                // Belegungsprobe: im Fussabdruck eines Gebaeudes faehrt er weiter.
                if (GebaeudeAufZelle(c, r) >= 0) continue;
                ZugExplodiert(l, w, c, r);
                break;
            }
        }
    }

    /// <summary><c>zug_vernichten</c> 0x4C7990: Ladung weg, Mitfahrer tot, Waggons
    /// weg — auch die, die die Karte mitgebracht hat.</summary>
    private void ZugExplodiert(RailLine l, Wagon w, int c, int r)
    {
        int ladung = 0;
        for (int k = 0; k < 4; k++) ladung += l.Cargo[k];
        int tote = RailTransfersKillOnLine(l.Slot);
        _effects.Add(new Effect { Pos = RailPoint(new Vector2(w.Col, w.Row)),
                                  Kind = "explosion", FrameTime = 0.05f });
        RailLineDestroy(l.Slot);          // Ladung, eigene Waggons, Fahrzeit
        // RailClearWagons raeumt nur SELBST angelegte Waggons; die der Karte
        // gehen hier mit unter. Beim naechsten Start legt der Automat neue an.
        if (_freightWagons.TryGetValue(l.Slot, out var rest))
        {
            foreach (var x in rest) _wagons.Remove(x);
            _freightWagons.Remove(l.Slot);
        }
        l.Rollt = false;
        l.Faze = RailBrokenOnLine(l.Slot) > 0 ? 3 : 0;
        ZugExplosionen++;
        _zugExplosionenJeLinie[l.Slot] = _zugExplosionenJeLinie.GetValueOrDefault(l.Slot) + 1;
        GD.Print($"zug: Linie {l.Slot} explodiert bei ({c},{r}) — Ladung {ladung} weg, " +
                 $"{tote} Mitfahrer tot, faze := {l.Faze} (Original 0x4C739C -> zug_vernichten 0x4C7990)");
    }

    // ---- der Pruefstand --gleisbruch-zug-check ------------------------------
    //
    // Er sucht einen fahrenden Zug (Linie 4 bevorzugt, K21: 57 <-> Basis),
    // bricht das Gleis ein Stueck VOR ihm und sieht zu:
    //   Soll:        >= 1 Explosion, 0 Ankuenfte, 0 Starts solange gebrochen,
    //                nach der Reparatur ein neuer Start.
    //   Nullmodell (--zug-faehrt-durch): der Zug kommt an, 0 Explosionen.

    private int _gbPhase;
    private float _gbZeit, _gbFenster;
    private int _gbLinie = -1, _gbTrips0, _gbStarts0, _gbExpl0, _gbStartsBruch, _gbFaze3;
    private int _gbProben;
    private long _gbMoved0;
    private (int, int) _gbZelle;
    private bool _gbFertig;

    private void GleisbruchCheckTakt()
    {
        if (_gbFertig) return;
        _gbZeit += _railLastDt;
        switch (_gbPhase)
        {
            case 0:
                if (GleisbruchScharf()) { _gbPhase = 1; _gbZeit = 0f; }
                else if (_gbZeit > 240f) GleisbruchCheckEnde("kein fahrender Zug mit Strecke gefunden");
                break;
            case 1:
            {
                var l = RailLineBySlot(_gbLinie);
                if (l == null) { GleisbruchCheckEnde("Linie weg"); return; }
                _gbProben++;
                if (l.Faze == 3) _gbFaze3++;
                if (_gbZeit < _gbFenster) return;
                _gbStartsBruch = l.Starts - _gbStarts0;
                // Reparieren, wie es die Reparaturkette taete.
                foreach (var z in new List<RailCell>(_railCells))
                    if (z.Line == _gbLinie && z.Broken) RailRepair(z.Col, z.Row);
                _gbPhase = 2; _gbZeit = 0f;
                _gbStarts0 = l.Starts;
                break;
            }
            case 2:
            {
                var l = RailLineBySlot(_gbLinie);
                if (l == null) { GleisbruchCheckEnde("Linie weg"); return; }
                if (l.Starts > _gbStarts0 || _gbZeit > 30f) GleisbruchCheckEnde(null);
                break;
            }
        }
    }

    private RailLine? RailLineBySlot(int slot)
    {
        foreach (var l in _railLines) if (l.Slot == slot) return l;
        return null;
    }

    private bool GleisbruchScharf()
    {
        RailLine? wahl = null;
        foreach (var l in _railLines)
        {
            if (!l.Rollt || l.TravelFull <= 0f) continue;
            float p = 1f - l.Travel / l.TravelFull;
            if (p < 0.1f || p > 0.45f) continue;
            if (!_lineCell.TryGetValue(l.Slot, out var cells) || cells.Count < 8) continue;
            if (!_freightWagons.TryGetValue(l.Slot, out var list) || list.Count == 0) continue;
            if (wahl == null || l.Slot == 4) wahl = l;
        }
        if (wahl == null) return false;

        var kette = _lineCell[wahl.Slot];
        Wagon? spitze = null;
        foreach (var w in _freightWagons[wahl.Slot])
            if (!w.Hidden && (spitze == null || w.Index < spitze.Index)) spitze = w;
        if (spitze == null) return false;
        int bei = 0; float best = float.MaxValue;
        for (int i = 0; i < kette.Count; i++)
        {
            float d = kette[i].DistanceSquaredTo(new Vector2(spitze.Col, spitze.Row));
            if (d < best) { best = d; bei = i; }
        }
        int dir = wahl.Dir == 0 ? 1 : -1;
        for (int i = bei + 3 * dir; i > 1 && i < kette.Count - 2; i += dir)
        {
            int c = (int)kette[i].X, r = (int)kette[i].Y;
            if (GebaeudeAufZelle(c, r) >= 0) continue;
            var auf = GleisAuf(c, r);
            if (auf == null) continue;
            bool geht = false;
            foreach (var z in auf)
                if (z.Line == wahl.Slot && !z.Pylon && !z.Broken && z.Frame != 255) geht = true;
            if (!geht) continue;
            _gbLinie = wahl.Slot;
            _gbTrips0 = wahl.Trips; _gbStarts0 = wahl.Starts; _gbMoved0 = wahl.Moved;
            _gbExpl0 = _zugExplosionenJeLinie.GetValueOrDefault(wahl.Slot);
            _gbFenster = wahl.TravelFull * 1.5f + 5f;
            _gbZelle = (c, r);
            RailHit(c, r, int.MaxValue);
            GD.Print($"gleisbruch-zug-check: Linie {wahl.Slot}, Zug bei Kettenglied {bei}/{kette.Count} " +
                     $"(Richtung {wahl.Dir}), Gleis gebrochen bei ({c},{r}) — {RailBrokenOnLine(wahl.Slot)} " +
                     $"Zellen zerschossen, faze {wahl.Faze}, Fenster {_gbFenster:0.0} s");
            return true;
        }
        return false;
    }

    private void GleisbruchCheckEnde(string? fehler)
    {
        _gbFertig = true;
        var sb = new StringBuilder("gleisbruch-zug-check: ");
        if (fehler != null) { GD.Print(sb.Append("DURCHGEFALLEN — ").Append(fehler).ToString()); return; }
        var l = RailLineBySlot(_gbLinie)!;
        int expl = _zugExplosionenJeLinie.GetValueOrDefault(_gbLinie) - _gbExpl0;
        int an = l.Trips - _gbTrips0;
        long ware = l.Moved - _gbMoved0;
        bool neu = l.Starts > _gbStarts0;
        sb.Append($"Linie {_gbLinie} Bruch ({_gbZelle.Item1},{_gbZelle.Item2}): {expl} Explosion(en), " +
                  $"{an} Ankunft/Ankuenfte, {ware} Stueck zugestellt, {_gbStartsBruch} Starts solange gebrochen, " +
                  $"faze 3 in {_gbFaze3}/{_gbProben} Proben, nach Reparatur {(neu ? "neu gestartet" : "KEIN Start")}");
        if (ZugFaehrtDurch)
        {
            bool erwartet = expl == 0 && an >= 1;
            sb.Append(erwartet ? " — NULLMODELL wie erwartet (Zug faehrt durch und kommt an)"
                               : " — NULLMODELL NICHT wie erwartet");
        }
        else
        {
            bool ok = expl >= 1 && an == 0 && ware == 0 && _gbStartsBruch == 0 && neu;
            sb.Append(ok ? " — BESTANDEN" : " — DURCHGEFALLEN");
        }
        GD.Print(sb.ToString());
    }
}
