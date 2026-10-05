namespace AkteEuropaReborn.Rendering;

using Godot;
using System.Text;

/// <summary>
/// <b><c>--treffer-check</c> — bug-438/439/440, KayelGee Runde 4 (Meldungen 2,
/// 36, 5, 16).</b> Drei Teile, jeder fragt <see cref="Pick"/> bzw.
/// <see cref="CursorHintAt"/> — die Stellen, die auch die Maus fragt.
///
/// <list type="number">
///   <item><b>Stapel</b> (bug-438): drei eigene Fahrzeuge in (c, r), (c, r+1),
///   (c, r+2); vier Klicks nach berichte/auswahl-treffer-fable.md §2. Soll 4/4,
///   Nullmodell <c>--pick-vorderster-alt</c> 2/4.</item>
///   <item><b>Gebaeudezellen</b> (bug-439): je Gebaeude mit Griffzellen jede
///   Zelle seines Musterrechtecks — Griff- und Tuerzellen muessen es treffen,
///   die anderen nicht; dazu die Zelle unter jeder Tuer. Nullmodell
///   <c>--gebaeudepick-bbox-alt</c>: Fremdzellen und »vor der Tuer« treffen.</item>
///   <item><b>Herrenlos</b> (bug-440): jedes herrenlose Gebaeude ausser Art 13
///   und Art 17 gibt ohne Bedingung <c>Ground</c>; der Nachschubposten nur mit
///   eigenem Fahrzeug auf (x+3, y+3) <c>Neutral</c>; Kulissen (Art ≥ 18) haben in
///   Tab-Stellung 1 keinen Balken. Nullmodell <c>--neutralzeiger-alt</c>.</item>
/// </list>
/// Der Nebel ist aus (<see cref="PickOhneNebel"/>); die Einheiten werden nach
/// dem Lauf zurueckgestellt, der Lauf endet ohnehin.
/// </summary>
public partial class MapEntityLayer
{
    public string TrefferCheckLine()
    {
        var sb = new StringBuilder("treffer-check\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();
        bool nebelVor = PickOhneNebel;
        PickOhneNebel = true;
        bool ok = true;
        ok &= StapelTeil(sb);
        ok &= GebaeudezellenTeil(sb);
        ok &= HerrenlosTeil(sb);
        PickOhneNebel = nebelVor;
        sb.Append(ok ? "treffer-check: BESTANDEN" : "treffer-check: DURCHGEFALLEN");
        return sb.ToString();
    }

    private bool StapelTeil(StringBuilder sb)
    {
        var drei = new System.Collections.Generic.List<int>();
        for (int i = 0; i < _entities.Count && drei.Count < 3; i++)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead || Untergestellt(u)) continue;
            if (u.Owner != ViewPlayer || u.GameUnitType != 0) continue;
            drei.Add(i);
        }
        if (drei.Count < 3) { sb.Append("  Stapel: weniger als drei eigene Fahrzeuge — uebersprungen\n"); return true; }

        // Eine ebene, freie Spalte suchen: drei Zellen gleicher Hub, und im
        // Umkreis kein anderer Satz, der einen Kasten in die Klickpunkte haengt.
        var a0 = _entities[drei[0]];
        (int c, int r)? platz = null;
        for (int rad = 0; rad < 30 && platz == null; rad++)
            for (int dc = -rad; dc <= rad && platz == null; dc++)
                for (int dr = -rad; dr <= rad && platz == null; dr++)
                {
                    int c = a0.Col + dc, r = a0.Row + dr;
                    if (c < 2 || r < 3 || c >= _nav.Width - 2 || r >= _nav.Height - 5) continue;
                    int h = HubOf(c, r);
                    if (HubOf(c, r + 1) != h || HubOf(c, r + 2) != h) continue;
                    bool frei = true;
                    for (int k = 0; k < _entities.Count && frei; k++)
                    {
                        if (drei.Contains(k)) continue;
                        var o = _entities[k];
                        if (o.Dead) continue;
                        int w = Mathf.Max(1, o.FootW), hh = Mathf.Max(1, o.FootH);
                        if (o.Col - 2 <= c && c <= o.Col + w + 1 && o.Row - 3 <= r + 2 && r - 3 <= o.Row + hh)
                            frei = false;
                    }
                    if (frei) platz = (c, r);
                }
        if (platz is not { } p) { sb.Append("  Stapel: keine freie ebene Spalte gefunden — uebersprungen\n"); return true; }

        var alt = new (int Col, int Row, Vector2 Pos)[3];
        for (int k = 0; k < 3; k++)
        {
            var u = _entities[drei[k]];
            alt[k] = (u.Col, u.Row, u.Pos);
            u.Col = p.c; u.Row = p.r + k; u.Pos = CellCenter(p.c, p.r + k);
        }
        var nord = _entities[drei[0]].Pos; var mitte = _entities[drei[1]].Pos; var sued = _entities[drei[2]].Pos;
        var faelle = new (string Name, Vector2 Punkt, int Soll)[]
        {
            ("Rumpfmitte Mitte  Pos_M+(0,-2) ", mitte + new Vector2(0, -2),  drei[1]),
            ("ueber Mitte       Pos_M+(0,-20)", mitte + new Vector2(0, -20), drei[0]),
            ("Rumpfmitte Sued   Pos_S+(0,-2) ", sued + new Vector2(0, -2),   drei[2]),
            ("unter Nord        Pos_N+(0,+10)", nord + new Vector2(0, 10),   drei[0]),
        };
        string Name(int i) => i == drei[0] ? "Nord" : i == drei[1] ? "Mitte" : i == drei[2] ? "Sued" : i < 0 ? "nichts" : $"Satz {i}";
        int gut = 0;
        sb.Append($"  Stapel bei ({p.c},{p.r}..{p.r + 2}), Pos_N {nord}, Pos_M {mitte}, Pos_S {sued}\n");
        foreach (var f in faelle)
        {
            int t = Pick(f.Punkt);
            bool g = t == f.Soll;
            if (g) gut++;
            sb.Append($"    {f.Name}: trifft {Name(t)}, Soll {Name(f.Soll)}{(g ? "" : "   ⚠")}\n");
        }
        for (int k = 0; k < 3; k++)
        {
            var u = _entities[drei[k]];
            (u.Col, u.Row, u.Pos) = alt[k];
        }
        sb.Append($"  Stapel: {gut}/4 richtig{(PickVordersterAlt ? " (Nullmodell --pick-vorderster-alt)" : "")}\n");
        return gut == 4;
    }

    private bool GebaeudezellenTeil(StringBuilder sb)
    {
        int geb = 0, griffGut = 0, griff = 0, fremdTreffer = 0, fremd = 0, vorGut = 0, vor = 0, verdeckt = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead || b.BType <= 0) continue;
            if (b.Slot < 0 || !_griffPlaetze.Contains(b.Slot)) continue;
            geb++;
            int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
            for (int dc = 0; dc < w; dc++)
                for (int dr = 0; dr < h; dr++)
                {
                    int c = b.Col + dc, r = b.Row + dr;
                    bool tuer = false;
                    foreach (var d in b.DoorCells) if (b.Col + d.Col == c && b.Row + d.Row == r) tuer = true;
                    bool istGriff = tuer || (_griffZelle.TryGetValue((c, r), out int pl) && pl == b.Slot);
                    int t = Pick(MitteVon(c, r));
                    if (t >= 0 && !_entities[t].IsBuilding) { verdeckt++; continue; }
                    if (istGriff) { griff++; if (t == i) griffGut++; }
                    else { fremd++; if (t == i) fremdTreffer++; }
                }
            foreach (var d in b.DoorCells)
            {
                int c = b.Col + d.Col, r = b.Row + d.Row + 1;
                int t = Pick(MitteVon(c, r));
                if (t >= 0 && !_entities[t].IsBuilding) { verdeckt++; continue; }
                vor++;
                if (t != i) vorGut++;
            }
        }
        if (geb == 0) { sb.Append("  Gebaeudezellen: keine Gebaeude mit Griffzellen — uebersprungen\n"); return true; }
        bool ok = griffGut == griff && fremdTreffer == 0 && vorGut == vor;
        sb.Append($"  Gebaeudezellen: {geb} Gebaeude, Griff+Tuer {griffGut}/{griff} getroffen, "
                + $"Fremdzellen {fremdTreffer}/{fremd} getroffen, vor der Tuer {vorGut}/{vor} frei, "
                + $"{verdeckt} Zellen von Einheiten verdeckt (nicht gewertet)"
                + (GebaeudepickBboxAlt ? " (Nullmodell --gebaeudepick-bbox-alt)" : "") + (ok ? "\n" : "   ⚠\n"));
        return ok;
    }

    private bool HerrenlosTeil(StringBuilder sb)
    {
        int n = 0, gut = 0, balken = 0;
        bool ok = true;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead || b.BType <= 0) continue;
            if (b.Owner is >= 0 and <= 7) continue;
            if (b.BType is 13 or MarketType) continue;
            if (b.NoStructure && !Anfassbar(b)) continue;
            // eine Griffzelle des Gebaeudes als Klickpunkt
            Vector2? punkt = null;
            int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
            for (int dc = 0; dc < w && punkt == null; dc++)
                for (int dr = 0; dr < h && punkt == null; dr++)
                {
                    var q = MitteVon(b.Col + dc, b.Row + dr);
                    if (Pick(q) == i) punkt = q;
                }
            if (punkt is not { } pt) continue;
            n++;
            var hint = CursorHintAt(pt);
            bool g = hint == Hint.Ground;
            if (g) gut++;
            if (b.BType >= 18 && TabBalkenFuer(b, 1) != null) balken++;
            sb.Append($"    Platz {b.Slot,3} Art {b.BType,3} bei ({b.Col},{b.Row}) built {b.Built}: Zeiger {hint}{(g ? "" : "   ⚠")}\n");
        }
        if (n > 0)
        {
            ok &= gut == n && balken == 0;
            sb.Append($"  Herrenlos: {n} Gebaeude, Zeiger {gut}/{n} Boden, Kulissenbalken {balken}"
                    + (NeutralzeigerAlt ? " (Nullmodell --neutralzeiger-alt)" : "") + (ok ? "\n" : "   ⚠\n"));
        }
        else sb.Append("  Herrenlos: keine (ausser 13/17) — uebersprungen\n");

        // Nachschubposten: leer -> Ground, mit eigenem Fahrzeug auf der Platte -> Neutral.
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.Dead || b.BType != SupplyDepotType) continue;
            int pc = b.Col + 3, pr = b.Row + 3;
            Vector2? punkt = null;
            int idx = _entities.IndexOf(b);
            int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
            for (int dc = 0; dc < w && punkt == null; dc++)
                for (int dr = 0; dr < h && punkt == null; dr++)
                {
                    var q = MitteVon(b.Col + dc, b.Row + dr);
                    if (Pick(q) == idx) punkt = q;
                }
            if (punkt is not { } pt) { sb.Append($"  Posten {b.Slot}: keine anklickbare Zelle   ⚠\n"); ok = false; continue; }
            bool leerBesetzt = PostenPlatteBesetzt(b, ViewPlayer);
            var leer = CursorHintAt(pt);
            // ein eigenes Fahrzeug auf die Platte stellen
            Entity? fz = null;
            foreach (var u in _entities)
                if (!u.IsBuilding && !u.IsProp && !u.Dead && u.Owner == ViewPlayer && u.Infantry < 0 && !Untergestellt(u)) { fz = u; break; }
            string mit = "kein Fahrzeug";
            bool mitOk = true;
            if (fz != null)
            {
                int fi = _entities.IndexOf(fz);
                var (oc, or, op) = (fz.Col, fz.Row, fz.Pos);
                int vorher = _nav.OccupantAt(pc, pr);
                _nav.SetOccupant(oc, or, -1);
                _nav.SetOccupant(pc, pr, fi);
                fz.Col = pc; fz.Row = pr; fz.Pos = CellCenter(pc, pr);
                var h2 = CursorHintAt(pt);
                mit = h2.ToString();
                mitOk = h2 == Hint.Neutral;
                _nav.SetOccupant(pc, pr, vorher);
                _nav.SetOccupant(oc, or, fi);
                (fz.Col, fz.Row, fz.Pos) = (oc, or, op);
            }
            bool leerOk = leerBesetzt || leer == Hint.Ground || PostenfensterOhneEinheit || NeutralzeigerAlt;
            ok &= leerOk && mitOk;
            sb.Append($"  Posten {b.Slot} bei ({b.Col},{b.Row}), Platte ({pc},{pr}): leer {leer}, mit Fahrzeug {mit}"
                    + (leerOk && mitOk ? "\n" : "   ⚠\n"));
        }
        return ok;
    }

    /// <summary>Die Mitte der ersten Zelle (Zeile fuer Zeile), auf der
    /// <see cref="GebaeudeAufZelle"/> das Gebaeude findet — fuer Pruefstaende,
    /// die ein Gebaeude anklicken wollen. Sonst der Anker.</summary>
    private Vector2 GriffpunktVon(Entity b)
    {
        int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
        for (int dr = 0; dr < h; dr++)
            for (int dc = 0; dc < w; dc++)
                if (GebaeudeAufZelle(b, b.Col + dc, b.Row + dr)) return MitteVon(b.Col + dc, b.Row + dr);
        return MitteVon(b.Col, b.Row);
    }
}
