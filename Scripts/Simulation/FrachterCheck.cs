namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--frachter-check</c> (14.09.2026, seine Meldung aus K14: »bei selbstgebauten
/// Frachtern keine Einheiten einladen«). Ein Frachter wird an einer Werft gebaut
/// (derselbe Weg wie im Spiel, <c>LaunchShip</c>), laeuft aus, dann wird ein eigener
/// Fusssoldat neben ihn gestellt und ueber <c>BeladeVersuch</c> — den Weg des
/// Befehls — an Bord geschickt. Soll: Transportsatz vorhanden, Soldat an Bord.
/// Nullmodell <c>--frachtersatz-alt</c>: »kein eigener Traeger in der Naehe«.
/// ⚠ EINGRIFFE: Werft an den Spieler, Bauwahl auf den Frachter, ein Fusssoldat
/// wird versetzt.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--frachtersatz-alt</c> — der Stand bis 14.09.2026: ein gebauter
    /// Frachter bekommt keinen Transportsatz und nimmt niemanden auf.</summary>
    public static bool FrachtersatzAlt;

    /// <summary>Wieviele gebaute Frachter einen Transportsatz bekamen.</summary>
    public int FrachtersatzAngelegt;

    public string FrachterCheck()
    {
        var sb = new System.Text.StringBuilder("frachter-check\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        void Takte(int n) { for (int t = 0; t < n; t++) SimTickFuerProbe(); }
        if (FrachtersatzAlt) sb.AppendLine("  ⚠ NULLMODELL --frachtersatz-alt: hier MUSS das Einladen scheitern");

        Entity? dock = null;
        ShipDesign? frachter = null;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead || !IsDock(b)) continue;
            foreach (var x in ShipMenu(b))
                if (x.Chassis == 153 || x.Name.Contains("Frachter")) { dock = b; frachter = x; break; }
            if (dock != null) break;
        }
        if (dock == null || frachter == null || _nav == null)
            return sb.Append("  keine Werft mit Frachter im Angebot — ungeprueft\n  DURCHGEFALLEN").ToString();
        dock.Owner = dock.Team = ViewPlayer;
        dock.BuildIndex = frachter.Index;
        sb.AppendLine($"  ⚠ EINGRIFF: Dock Platz {dock.Slot} auf ({dock.Col},{dock.Row}) an Spieler {ViewPlayer}, Bauwahl {frachter.Name}");

        int vor = _entities.Count;
        LaunchShip(dock);
        var f = _entities.Count > vor ? _entities[^1] : null;
        if (f == null) return sb.Append("  LaunchShip legte kein Schiff an\n  DURCHGEFALLEN").ToString();
        Soll(f.Chassis == TraegerRumpf, $"gebautes Schiff »{frachter.Name}« traegt +0x0B {f.Chassis} (73)");
        Soll(FrachtersatzAlt ? !_bordDeckel.ContainsKey(f.Slot) : _bordDeckel.ContainsKey(f.Slot),
             $"Transportsatz fuer Platz {f.Slot}: {_bordDeckel.ContainsKey(f.Slot)}");
        int w = 0;
        while (f.LeavingDock >= 0 && w < 400) { Takte(1); w++; }
        Takte(5);
        sb.AppendLine($"  (ausgelaufen nach {w} Takten, liegt auf ({f.Col},{f.Row}), Besitzer {f.Owner})");

        // ein eigener Fusssoldat an Land neben den Frachter
        int si = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || e.Infantry < 0 || !e.Mobile || Untergestellt(e)) continue;
            if (_frachtPlaetze.ContainsKey(e.Slot)) continue;
            si = i; break;
        }
        if (si < 0) return sb.Append("  kein Fusssoldat zum Einladen — ungeprueft\n  DURCHGEFALLEN").ToString();
        var s = _entities[si];
        s.Owner = s.Team = f.Owner;
        Vector2I? platz = null;
        for (int r = 1; r <= 5 && platz == null; r++)
            for (int dc = -r; dc <= r + 1 && platz == null; dc++)
                for (int dr = -r; dr <= r + 1 && platz == null; dr++)
                {
                    int c = f.Col + dc, z = f.Row + dr;
                    if (!_nav.InBounds(c, z) || !_nav.IsWalkable(c, z) || _nav.OccupantAt(c, z) >= 0) continue;
                    platz = new Vector2I(c, z);
                }
        if (platz == null) return sb.Append("  kein Landplatz neben dem Frachter — ungeprueft\n  DURCHGEFALLEN").ToString();
        ProbeVersetzen(si, platz.Value.X, platz.Value.Y);
        s.Path = null; s.Orders.Clear();
        int traeger = BeladeVersuch(si, melden: false);
        bool anBord = traeger == f.Slot;
        Soll(FrachtersatzAlt ? !anBord : anBord,
             $"Fusssoldat auf ({platz.Value.X},{platz.Value.Y}) geht an Bord: Traeger {traeger} (Frachter {f.Slot}), »{_order}«");

        // ---- der KLICKWEG (Befehl 17), seine zweite Meldung ------------------
        if (FrachtersatzAlt) return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
        if (EinsteigbefehlAlt) sb.AppendLine("  ⚠ NULLMODELL --einsteigbefehl-alt: der Klick ist ein Fahrbefehl, der Soldat MUSS draussen bleiben");
        int s2 = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (i == si || e.Dead || e.IsBuilding || e.IsProp || e.Infantry < 0 || !e.Mobile || Untergestellt(e)) continue;
            if (_frachtPlaetze.ContainsKey(e.Slot)) continue;
            s2 = i; break;
        }
        if (s2 < 0) return sb.Append("  Klickweg: kein zweiter Fusssoldat — ungeprueft\n  DURCHGEFALLEN").ToString();
        var u2 = _entities[s2];
        u2.Owner = u2.Team = f.Owner;
        Vector2I? weit = null;
        for (int r = 7; r <= 14 && weit == null; r++)
            for (int dc = -r; dc <= r && weit == null; dc++)
                for (int dr = -r; dr <= r && weit == null; dr++)
                {
                    if (System.Math.Max(System.Math.Abs(dc), System.Math.Abs(dr)) != r) continue;
                    int c = f.Col + dc, z = f.Row + dr;
                    if (!_nav.InBounds(c, z) || !_nav.IsWalkable(c, z) || _nav.OccupantAt(c, z) >= 0) continue;
                    if (RampeBeladen(c, z)) continue;
                    if (_nav.FindPath(new Vector2I(c, z), new Vector2I(platz.Value.X, platz.Value.Y), u2.Move, s2) == null) continue;
                    weit = new Vector2I(c, z);
                }
        if (weit == null) return sb.Append("  Klickweg: kein erreichbarer Platz 7..14 Zellen vom Frachter — ungeprueft\n  DURCHGEFALLEN").ToString();
        ProbeVersetzen(s2, weit.Value.X, weit.Value.Y);
        u2.Path = null; u2.Orders.Clear();
        _sel.Clear(); _sel.Add(s2); _selected = s2;
        bool genommen = PostBoardKlick(f.Pos);
        if (!genommen) PostMove(f.Pos);          // was der Rechtsklick sonst taete
        int takte = 0;
        while (takte < 1500 && !_frachtPlaetze.ContainsKey(u2.Slot)) { Takte(1); takte++; }
        bool drin = _frachtPlaetze.ContainsKey(u2.Slot);
        Soll(EinsteigbefehlAlt ? !drin : drin,
             $"Klick auf den Frachter mit Fusssoldat von ({weit.Value.X},{weit.Value.Y}): Befehl 17 {(genommen ? "abgesetzt" : "NICHT abgesetzt (Fahrbefehl)")}, "
             + $"an Bord {drin} nach {takte} Takten, steht jetzt ({u2.Col},{u2.Row}), Auftrag {u2.EinsteigTraeger}, »{_order}«");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
