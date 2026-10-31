namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// Die drei Nachpruefungen zu bug-696 (30.10.2026, <c>berichte/bau-fluglage.md</c> §8):
/// <list type="bullet">
/// <item><c>--flieger-klickkasten-check</c> (bug-717) — Klicks an den vier Kanten, in der
/// Mitte und an einer Ecke, fuer drei Feinlagen in derselben Zelle; Soll nach 0x431F7F
/// (streng, 70 × 50, an der Zellecke). Nullmodell <c>--flieger-klickkasten-alt</c>.</item>
/// <item><c>--flugtempo-y-check</c> (bug-718) — dieselbe Maschine fliegt 10 Zellen nach Osten
/// und 10 nach Sueden; Soll nach 0x425085: gleich viele Takte (isotrop in Zellen), und
/// <c>400 / (sp·Massstab)</c> Takte. Nullmodell <c>--flugtempo-y-alt</c>: Sueden halb so
/// lang.</item>
/// <item><c>--fluglage-umrechnung-check</c> — ein Stand mit Flieger in der Luft, in der ALTEN
/// Lage geschrieben (ohne Schluessel <c>fluglage</c>), in der neuen geladen: Zelle, Lage und
/// Ziel muessen stimmen. Nullmodell <c>--fluglage-umrechnung-aus</c>.</item>
/// </list>
/// ⚠ EINGRIFF jeweils: ein Probehubschrauber Art 10 des Betrachters (Huelle 100000, keine
/// Munition, Hoehe 135); der Lauf endet danach.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--fluglage-umrechnung-aus</c> — Nullmodell: ein alter Stand wird NICHT
    /// umgerechnet.</summary>
    public static bool FluglageUmrechnungAus;

    private Special ProbeHeli(int col, int row, string name)
    {
        int slot = 900;
        foreach (var x in _special) slot = Mathf.Max(slot, x.Slot + 1);
        var a = new Special
        {
            Slot = slot, Kind = 10, Name = name, TypeName = name,
            Owner = ViewPlayer, Stored = false, HomeSlot = -1,
            Col = col, Row = row, Pos = FlugZelle(col, row),
            Hp = 100000, HpMax = 100000, Attack = 0, Ammo = 0, AmmoMax = 0,
            Fuel = 99999, FuelMax = 99999, Speed = 5, Stufe = 5, StufeUnten = 5,
            Alt = 135, Sollhoehe = 135, Order = 1,
        };
        _special.Add(a);
        return a;
    }

    /// <summary>Eine Zelle, um die herum (±r) kein anderes eigenes Flugzeug fliegt.</summary>
    private Vector2I FreieFlugzelle(int rand, int abstand)
    {
        for (int r = rand; r < _nav!.Height - rand; r++)
            for (int c = rand; c < _nav.Width - rand; c++)
            {
                bool frei = true;
                foreach (var s in _special)
                    if (!s.Dead && !s.Stored && Mathf.Abs(s.Col - c) <= abstand && Mathf.Abs(s.Row - r) <= abstand)
                    { frei = false; break; }
                if (frei && ElevOf(c, r) > 0) return new Vector2I(c, r);
            }
        return new Vector2I(rand, rand);
    }

    // ------------------------------------------------------------------ bug-717
    public string FliegerKlickkastenCheck()
    {
        var sb = new System.Text.StringBuilder("flieger-klickkasten-check"
            + (FliegerKlickkastenAlt ? " — NULLMODELL --flieger-klickkasten-alt" : "")
            + (FluglageAlt ? " — --fluglage-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL — keine Karte").ToString();
        var z = FreieFlugzelle(6, 4);
        var a = ProbeHeli(z.X, z.Y, "Probeheli");
        int idx = _special.Count - 1;
        sb.AppendLine($"  ⚠ EINGRIFF: Probeheli Platz {a.Slot} auf ({z.X},{z.Y}), Gelaende {ElevOf(z.X, z.Y)}, Hoehe {a.Alt}");
        int falsch = 0, proben = 0;
        float? l0 = null, t0 = null; bool ecke = true;
        foreach (var (fx, fy) in new[] { (0, 0), (20, 10), (39, 19) })
        {
            a.Pos = new Vector2(_ox + z.X * TileW + fx, _oy + z.Y * TileH + fy);
            AirZelleNachLage(a);
            // Soll: Original-Bildschirm (Rollung 0) — Eintrag (sx, sy) = Zellecke, Kasten
            // (sx−30, sx+40) × (sy−alt−10, sy−alt+40) streng; Rumpf-Blit (sx+FeinX−43,
            // sy+FeinY/2−alt−35). Auf unser Bild gelegt ueber den Blitpunkt, an dem wir zeichnen.
            float sx = z.X * TileW, sy = z.Y * TileH;
            var blitO = new Vector2(sx + fx - 43, sy + fy - a.Alt - 35);
            var blitU = AirBlit(a);   // bug-727
            var d = blitU - blitO;
            float L = sx - 30 + d.X, R = sx + 40 + d.X, T = sy - a.Alt - 10 + d.Y, B = sy - a.Alt + 40 + d.Y;
            float mx = (L + R) / 2f, my = (T + B) / 2f;
            var faelle = new (string Wo, Vector2 P, bool Soll)[]
            {
                ("Mitte", new(mx, my), true), ("links innen", new(L + 0.5f, my), true), ("links aussen", new(L - 0.5f, my), false),
                ("links genau", new(L, my), false), ("rechts innen", new(R - 0.5f, my), true), ("rechts aussen", new(R + 0.5f, my), false),
                ("oben innen", new(mx, T + 0.5f), true), ("oben aussen", new(mx, T - 0.5f), false),
                ("unten innen", new(mx, B - 0.5f), true), ("unten aussen", new(mx, B + 0.5f), false),
                ("Ecke innen", new(L + 0.5f, T + 0.5f), true), ("Rumpfpunkt", AirRumpf(a), true),
            };
            string fehl = "";
            foreach (var (wo, p, soll) in faelle)
            {
                proben++;
                bool ist = PickAir(p) == idx;
                if (ist != soll) { falsch++; fehl += $" {wo}({(ist ? "trifft" : "verfehlt")})"; }
            }
            // die Kastenlage in unserem Bild: haengt sie an der Zelle (Soll) oder am Bild?
            float lU = d.X, tU = d.Y;   // Versatz Original→unser Bild
            if (l0 == null) { l0 = L; t0 = T; } else if (Mathf.Abs(L - l0.Value) > 0.01f || Mathf.Abs(T - t0!.Value) > 0.01f) ecke = false;
            sb.AppendLine($"  Fein ({fx},{fy}): Soll-Kasten x {L:0.0}..{R:0.0}, y {T:0.0}..{B:0.0} (Versatz Original→Bild {lU:0.0}/{tU:0.0}); "
                        + $"{faelle.Length} Klicks, {(fehl.Length == 0 ? "alle wie Soll" : "FALSCH:" + fehl)}");
        }
        // das Nullmodell muss an der Kastenlage scheitern, nicht an der Rechnung oben: der
        // alte Kasten haengt am Rumpf und wandert darum mit der Feinlage.
        sb.AppendLine($"  Soll-Kasten bleibt bei Feinlage 0..39 an der Zellecke stehen: {(ecke ? "ja" : "NEIN")} (Rechnung, beide Fassungen gleich)");
        _special.RemoveAt(idx);
        if (_selAir == idx) _selAir = -1;
        sb.Append($"  {falsch} von {proben} Klicks falsch -> " + (falsch == 0 && ecke ? "BESTANDEN" : "DURCHGEFALLEN"));
        return sb.ToString();
    }

    // ------------------------------------------------------------------ bug-727
    /// <summary><c>--flieger-anker-check</c>: liegen Rumpf, Rotor und Schatten dort, wo das
    /// Original sie blittet? Soll im Bildschirm des Originals (Rollung 0) nach 0x42B8A6 /
    /// 0x42BAC9 / 0x42D6AA, in unseren Kartenraum gelegt ueber die Eichung am FAHRZEUG:
    /// Fahrzeug-Blit des Originals <c>(Spalte·40, Zeile·20 − 35 − Hub)</c> (0x4301D1) gegen
    /// unseren (Zellmitte − EinheitenAnker + (CanvasXPad, 0)). Damit haengt das Soll nicht am
    /// Fliegeranker. Nullmodell <c>--flieger-anker-alt</c>.</summary>
    public string FliegerAnkerCheck()
    {
        var sb = new System.Text.StringBuilder("flieger-anker-check"
            + (FliegerAnkerAlt ? " — NULLMODELL --flieger-anker-alt" : "")
            + (RotorschattenAlt ? " — NULLMODELL --rotorschatten-alt" : "") + "\n");   // bug-728
        if (_nav == null) return sb.Append("  KEIN URTEIL — keine Karte").ToString();
        Entity? f = null;
        foreach (var e in _entities)
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Mobile && e.Infantry < 0
                && e.Move != Simulation.NavGrid.MoveClass.Ship) { f = e; break; }
        if (f == null) return sb.Append("  KEIN URTEIL — kein Fahrzeug zum Eichen").ToString();
        var pad = new Vector2(Import.CwrFile.CanvasXPad, 0);
        var fBlitU = CellCenter(f.Col, f.Row) - EinheitenAnker(f) + pad;
        var fBlitO = new Vector2(f.Col * TileW, f.Row * TileH - 35 - HubOf(f.Col, f.Row));
        var M = fBlitU - fBlitO;
        sb.AppendLine($"  Eichung am Fahrzeug Platz {f.Slot} ({LabelOf(f)}) auf ({f.Col},{f.Row}): Original→Karte ({M.X:0.0}, {M.Y:0.0}); _ox/_oy ({_ox}, {_oy})");
        var z = FreieFlugzelle(6, 4);
        var a = ProbeHeli(z.X, z.Y, "Probeheli");
        int idx = _special.Count - 1;
        a.Airframe = 120;                   // bug-728: Hubschrauberrumpf, der Zeichner gibt ihm den Rotor
        float maxR = 0, maxS = 0, maxRot = 0; int n = 0, rotorschatten = 0;
        foreach (var (dc, fx, fy, alt) in new[] { (0, 0, 0, 135), (0, 20, 10, 135), (0, 39, 19, 90), (3, 5, 15, 60), (7, 33, 2, 120) })
        {
            int c = Mathf.Clamp(z.X + dc, 1, _nav.Width - 2), r = z.Y;
            a.Pos = new Vector2(_ox + c * TileW + fx, _oy + r * TileH + fy);
            a.Alt = alt; AirZelleNachLage(a);
            int hub = HubOf(c, r, fx, TileH - fy);
            var rumpfO = new Vector2(c * TileW + fx - 43, r * TileH + fy - alt - 35);     // 0x42B8A6..0x42B8DF
            var schattenO = new Vector2(c * TileW + fx - 43, r * TileH + fy - hub - 35);  // 0x42D6AA..0x42D798
            var rotorO = rumpfO + new Vector2(0, -4);                                     // 0x42BAC9
            var rumpfU = AirRumpf(a) - FliegerAnker + pad;
            var schattenU = AirSchatten(a) - FliegerAnker + pad;
            var rotorU = AirRumpf(a) - FliegerAnker + RotorVersatz + pad;
            float dR = (rumpfU - (rumpfO + M)).Length(), dS = (schattenU - (schattenO + M)).Length(), dRot = (rotorU - (rotorO + M)).Length();
            maxR = Mathf.Max(maxR, dR); maxS = Mathf.Max(maxS, dS); maxRot = Mathf.Max(maxRot, dRot); n++;
            // bug-728: der Schattenzeichner 0x42D666 blittet nur den Rumpfschatten — Soll 0
            if (RotorschattenZeichnen(a)) rotorschatten++;
            sb.AppendLine($"  ({c},{r}) Fein ({fx},{fy}) Hoehe {alt}, Bodenanhebung {hub}: Rumpf {dR:0.00} px, Schatten {dS:0.00} px, Rotor {dRot:0.00} px daneben"
                        + (dR > 0.5f ? $" (Versatz {(rumpfU - rumpfO - M).X:0.0}/{(rumpfU - rumpfO - M).Y:0.0})" : ""));
        }
        _special.RemoveAt(idx);
        if (_selAir == idx) _selAir = -1;
        bool ok = maxR < 0.5f && maxS < 0.5f && maxRot < 0.5f && rotorschatten == 0;   // bug-728
        sb.AppendLine($"  Rotorschatten (Teil 111): {rotorschatten} von {n} Lagen gezeichnet (Soll 0, 0x42D666 hat keinen; bug-728)");
        sb.Append($"  {n} Lagen, groesste Abweichung Rumpf {maxR:0.00} / Schatten {maxS:0.00} / Rotor {maxRot:0.00} px -> " + (ok ? "BESTANDEN" : "DURCHGEFALLEN"));
        return sb.ToString();
    }

    // ------------------------------------------------------------------ bug-718
    public string FlugtempoYCheck()
    {
        var sb = new System.Text.StringBuilder("flugtempo-y-check"
            + (FlugtempoYAlt ? " — NULLMODELL --flugtempo-y-alt" : "")
            + (FluglageAlt ? " — --fluglage-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL — keine Karte").ToString();
        var z = FreieFlugzelle(14, 12);
        const int N = 10;
        int Flug(int dc, int dr, out float px)
        {
            var a = ProbeHeli(z.X, z.Y, "Probeheli");
            int idx = _special.Count - 1;
            var ziel = FlugZelle(z.X + dc * (N + 3), z.Y + dr * (N + 3));   // hinter der Messlinie
            a.PlayerGoal = ziel; a.Goal = ziel;
            var start = a.Pos; int takte = -1;
            for (int t = 1; t <= 3000; t++)
            {
                a.Hp = a.HpMax; a.Fuel = a.FuelMax; a.Sollhoehe = 135;
                a.Speed = a.Stufe = a.StufeUnten = 5;            // feste Stufe, keine Regelung
                UpdateAircraft(SimDt);
                if (a.Dead) break;
                bool da = dc != 0 ? a.Col >= z.X + N : a.Row >= z.Y + N;
                if (da) { takte = t; break; }
            }
            px = a.Pos.DistanceTo(start);
            _special.RemoveAt(idx);
            return takte;
        }
        int tx = Flug(1, 0, out float px), ty = Flug(0, 1, out float py);
        // Soll (0x425085): je Takt sp Feineinheiten, 40 je Zelle in x UND y. Bei uns ist ein
        // Takt SimDt und eine Feineinheit 1 px in x — Massstab AirPxPerSpeed (UNSERE SETZUNG
        // »Lufttakt«, Bombenabwurf.cs). Fuer N Zellen ab der Zellmitte: (N − 0,5)·40 Feineinheiten.
        float fein = 5 * AirPxPerSpeed * SimDt;
        int soll = Mathf.CeilToInt((N - 0.5f) * TileW / fein);
        sb.AppendLine($"  Start ({z.X},{z.Y}), Stufe 5 fest = {fein:0.000} Feineinheiten je Takt; {N} Zellen ab Zellmitte, Soll {soll} Takte je Richtung");
        sb.AppendLine($"  Osten: {tx} Takte ({px:0} px im Bild); Sueden: {ty} Takte ({py:0} px im Bild)");
        bool gleich = tx > 0 && ty > 0 && Mathf.Abs(tx - ty) <= 1;
        bool absolut = Mathf.Abs(tx - soll) <= 1 && Mathf.Abs(ty - soll) <= 1;
        sb.AppendLine($"  Sueden/Osten {(tx > 0 ? (float)ty / tx : 0f):0.00} (Soll 1,00, im Bildraum isotrop waere 0,50) -> {(gleich ? "ja" : "NEIN")}; "
                    + $"gegen die Formel -> {(absolut ? "ja" : "NEIN")}");
        sb.Append(gleich && absolut ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Umrechnung
    public string FluglageUmrechnungCheck()
    {
        var sb = new System.Text.StringBuilder("fluglage-umrechnung-check"
            + (FluglageUmrechnungAus ? " — NULLMODELL --fluglage-umrechnung-aus" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL — keine Karte").ToString();
        if (FluglageAlt || FlughuepfenAlt) return sb.Append("  KEIN URTEIL — der Pruefstand schaltet die alte Lage selbst").ToString();
        // dieselbe unruhige Strecke wie --fluglage-check
        const int Laenge = 24;
        int bestR = -1, bestC = -1, bestScore = -1;
        for (int r = 4; r < _nav.Height - 4; r++)
            for (int c0 = 3; c0 + Laenge < _nav.Width - 3; c0++)
            {
                int score = 0;
                for (int c = c0 + 1; c <= c0 + Laenge; c++)
                    score += (ElevOf(c, r) != ElevOf(c - 1, r) ? 2 : 0) + (HangArt(c, r) != 0 ? 1 : 0);
                if (score > bestScore) { bestScore = score; bestR = r; bestC = c0; }
            }
        // 1. in der ALTEN Lage fliegen und speichern
        FluglageAlt = true;
        var a = ProbeHeli(bestC, bestR, "Probeheli");
        int slot = a.Slot;
        var zielAlt = FlugZelle(bestC + Laenge, bestR);
        a.PlayerGoal = zielAlt; a.Goal = zielAlt;
        for (int t = 0; t < 320; t++) { a.Hp = a.HpMax; a.Sollhoehe = 135; UpdateAircraft(SimDt); }
        float x = a.Pos.X, yWahr = _oy + bestR * TileH + TileH / 2f;
        int colWahr = Mathf.FloorToInt((x - _ox) / TileW);
        sb.AppendLine($"  alte Lage: Probeheli Platz {slot} auf Zeile {bestR}, nach 320 Takten Pos ({a.Pos.X:0.0},{a.Pos.Y:0.0}), "
                    + $"Zelle alt ({a.Col},{a.Row}); wahr ({colWahr},{bestR}), y {yWahr:0.0}");
        string json = SaveStateJson("pruefstand", "fluglage-umrechnung");
        FluglageAlt = false;
        bool ohneMarke = !json.Contains("\"fluglage\"");
        var parser = new Json();
        if (parser.Parse(json) != Error.Ok || parser.Data.VariantType != Variant.Type.Dictionary)
            return sb.Append("  KEIN URTEIL — Stand unlesbar").ToString();
        // 2. in der NEUEN Lage laden
        bool aus = FluglageUmrechnungAus;
        int vorher = FluglageUmgerechnet;
        if (aus) parser.Data.AsGodotDictionary<string, Variant>()["fluglage"] = 1;   // Nullmodell: als neu ausgeben
        ApplySaveState(parser.Data.AsGodotDictionary<string, Variant>());
        var b = _special.Find(s => s.Slot == slot);
        if (b == null) return sb.Append("  KEIN URTEIL — Probe nach dem Laden weg").ToString();
        float dy = Mathf.Abs(b.Pos.Y - yWahr), dx = Mathf.Abs(b.Pos.X - x);
        float gy = b.PlayerGoal is { } pg ? Mathf.Abs(pg.Y - yWahr) : float.NaN;
        float zy = b.Goal is { } g ? Mathf.Abs(g.Y - yWahr) : float.NaN;
        bool zelleOk = b.Col == colWahr && b.Row == bestR;
        // ⚠ UNSERE SETZUNG: Soll fuer die Lage ist »unter einer halben Zellhoehe« — der alte
        // Stand traegt die Anhebung, die in Pos steckte (HubPx), NICHT (SaveState.LuftOhne);
        // die Umrechnung schaetzt sie (HubAnLage). Die ZELLE muss genau stimmen.
        bool lageOk = dx < 0.5f && dy < TileH / 2f;
        bool zielOk = !float.IsNaN(gy) && gy < 0.5f && (float.IsNaN(zy) || zy < 0.5f);
        sb.AppendLine($"  Stand ohne Schluessel fluglage: {(ohneMarke ? "ja" : "NEIN")}; umgerechnet {FluglageUmgerechnet - vorher} Flugzeug(e)");
        sb.AppendLine($"  neu geladen: Zelle ({b.Col},{b.Row}) gegen wahr ({colWahr},{bestR}) -> {(zelleOk ? "ja" : "NEIN")}; "
                    + $"Lage Abweichung x {dx:0.00} / y {dy:0.00} px (Soll y < 10, Schaetzung) -> {(lageOk ? "ja" : "NEIN")}; "
                    + $"Spielerziel y {gy:0.00} px, Flugziel y {zy:0.00} px -> {(zielOk ? "ja" : "NEIN")}");
        sb.Append(ohneMarke && zelleOk && lageOk && zielOk ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
