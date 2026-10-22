namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// Die Pruefstaende zu Paket E (22.10.2026): <c>--fussstreuung-check</c> (bug-596)
/// und <c>--fussvolk-klickkasten-check</c> (bug-602). Beide rechnen ihre Sollwerte
/// UNABHAENGIG aus den Rohsaetzen der Kartendatei (<c>raw +0x1D/+0x1E</c>, Klassenbyte
/// <c>+0x0A</c>) und den Konstanten der EXE, nicht aus <see cref="FussvolkBlit"/>.
/// Gedacht fuer K4 (map_04: 91 Fusssaetze, alle mit dx/dy ≠ 0).
///
/// <para>⚠ EINGRIFFE (angesagt): Probanden werden mit <c>AwStelle</c> umgestellt und
/// fuer die Dauer der Messung dem Betrachter zugeschlagen (Besitzer/Team), ihr Leben
/// wird je Takt aufgefuellt; im Klickkastenteil bekommt ein Fahrzeug zeitweise die
/// Bauteilzeile 0x47 (Materialtransporter). Alles wird zurueckgesetzt, soweit die
/// Messung es nicht verbraucht (Teil 3 bewegt die Soldaten endgueltig).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die Kartendatei des geladenen Spiels (fuer die Pruefstaende).</summary>
    private string _entitiesPfad = "";

    /// <summary>Unabhaengig gelesen: Slot → (dx, dy) aus <c>raw +0x1D/+0x1E</c> aller
    /// Saetze mit Klassenbyte <c>+0x0A == 1</c>.</summary>
    private Dictionary<int, (int Dx, int Dy)> RohStreuungLesen()
    {
        var d = new Dictionary<int, (int, int)>();
        if (_entitiesPfad.Length == 0 || !FileAccess.FileExists(_entitiesPfad)) return d;
        var root = JsonNode.Parse(FileAccess.GetFileAsString(_entitiesPfad));
        if (root?["entities"] is not JsonArray ev) return d;
        foreach (var it in ev)
        {
            if (it is not JsonObject o) continue;
            string raw = GetS(o, "raw");
            if (raw.Length < 0x3a * 2) continue;
            int B(int k) => System.Convert.ToInt32(raw.Substring(k * 2, 2), 16);
            if (B(0x0a) != 1) continue;
            int slot = GetI(o, "slot", -1);
            if (slot < 0) continue;
            d[slot] = ((sbyte)B(0x1d), (sbyte)B(0x1e));
        }
        return d;
    }

    /// <summary>Der Blitpunkt des Originals, unabhaengig gerechnet (0x43032D/0x430332/0x4303CF):
    /// x = Spalte·40 + 5 + dx, y = Zeile·20 − 24 − H(20+dx, 10+dy) − dy.</summary>
    private Vector2 OriginalFussBlit(int col, int row, int dx, int dy)
        => new(_ox + col * 40 + 5 + dx,
               _oy + row * 20 - 24 - Simulation.Hang.Hub(ElevOf(col, row), HangArt(col, row), 20 + dx, 10 + dy) - dy);

    /// <summary>Die angehobene Zellmitte, unabhaengig gerechnet.</summary>
    private Vector2 OriginalPos(int col, int row)
        => new(_ox + col * 40 + 20, _oy + row * 20 + 10 - Simulation.Hang.HubStehend(ElevOf(col, row), HangArt(col, row)));

    private bool FvSteht(Entity e)
        => !e.Dead && !e.IsBuilding && !e.IsProp && e.InGebaeude == null && !Untergestellt(e)
           && e.Reserved == null && (e.Path == null || e.PathIdx >= e.Path.Count);

    // ======================================================================
    //  --fussstreuung-check (bug-596)
    // ======================================================================

    public string FussstreuungCheckLine()
    {
        var sb = new StringBuilder("fussstreuung-check (bug-596)\n");
        string schalter = (FussstreuungAus ? " [Nullmodell --fussstreuung-aus]" : "")
                        + (NeunerzelleAus ? " [Nullmodell --neunerzelle-aus]" : "")
                        + (FussankerTiefAlt ? " [Nullmodell --fussanker-tief-alt]" : "");
        if (_nav == null) return sb.Append("  keine Karte — NICHT GEMESSEN").ToString();
        FussStreuungZuteilen();
        var roh = RohStreuungLesen();
        sb.Append($"  Karte {_entitiesPfad}: {roh.Count} Fusssaetze (Klasse 1) in der Datei{schalter}\n");

        // ---- Teil 1: Kartenwerte --------------------------------------------------
        int n1 = 0, t1 = 0, nz1 = 0;
        foreach (var e in _entities)
        {
            if (e.Infantry < 0 || e.IsBuilding || !roh.TryGetValue(e.Slot, out var rv)) continue;
            n1++;
            if (rv.Dx != 0 || rv.Dy != 0) nz1++;
            var (dx, dy) = StreuWirksam(e);
            if (dx == rv.Dx && dy == rv.Dy) t1++;
        }
        bool ok1 = n1 >= 20 && t1 == n1;
        sb.Append($"  {(ok1 ? "ok  " : "⚠ ROT")} 1 Kartenwerte: {t1}/{n1} Soldaten tragen raw +0x1D/+0x1E ({nz1} davon ≠ 0); Soll alle, n ≥ 20\n");

        // ---- Teil 2: Blitlage stehend -------------------------------------------
        int n2 = 0, t2 = 0;
        var sum = Vector2.Zero;
        foreach (var e in _entities)
        {
            if (e.Infantry < 0 || !roh.TryGetValue(e.Slot, out var rv) || !FvSteht(e)) continue;
            var soll = OriginalFussBlit(e.Col, e.Row, rv.Dx, rv.Dy) - new Vector2(Import.CwrFile.CanvasXPad, 0);
            var abw = FussvolkOrt(e) - soll;
            n2++; sum += abw;
            if (abw.Length() < 0.5f) t2++;
        }
        bool ok2 = n2 >= 20 && t2 == n2;
        var mittel = n2 > 0 ? sum / n2 : Vector2.Zero;
        sb.Append($"  {(ok2 ? "ok  " : "⚠ ROT")} 2 Leinwand stehend = Original-Blit (Sp·40+5+dx−4, Z·20−24−H'−dy): {t2}/{n2} auf 0,5 px; "
                + $"mittlere Abweichung ({mittel.X:0.0}, {mittel.Y:0.0}) px\n");

        // ---- Teil 3: Gruppe auf eine Zelle --------------------------------------
        string teil3 = FsGruppe(out bool ok3a, out bool ok3b, out bool gemessen3, out var probanden);
        sb.Append(teil3);

        // ---- Teil 4: Spielstand ---------------------------------------------------
        string teil4 = FsSpielstand(roh, probanden, out bool ok4);
        sb.Append(teil4);

        sb.Append($"  Zaehler: gehasht {StreuGehasht}, Neunerzelle abgewiesen {NeunerzelleAbgewiesen}\n");
        bool alles = ok1 && ok2 && gemessen3 && ok3a && ok3b && ok4;
        sb.Append(alles ? "  BESTANDEN" : gemessen3 ? "  DURCHGEFALLEN" : "  DURCHGEFALLEN (Teil 3 nicht gemessen)");
        return sb.ToString();
    }

    /// <summary>Teil 3: erst 6, dann 12 Soldaten ohne Formation auf eine freie Zelle.</summary>
    private string FsGruppe(out bool okA, out bool okB, out bool gemessen, out List<int> probanden)
    {
        okA = okB = gemessen = false;
        probanden = new List<int>();
        var sb = new StringBuilder();
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        var kand = new List<int>();
        for (int pass = 0; pass < 2 && kand.Count < 12; pass++)
            for (int i = 0; i < _entities.Count && kand.Count < 12; i++)
            {
                var e = _entities[i];
                if (e.Infantry < 0 || !FvSteht(e) || !e.Mobile || (pass == 0) != (e.Owner == me)) continue;
                if (e.Move == Simulation.NavGrid.MoveClass.Ship) continue;
                kand.Add(i);
            }
        if (kand.Count < 12) return $"  ⚠ 3 Gruppe: nur {kand.Count} stehende Soldaten — NICHT GEMESSEN\n";
        probanden.AddRange(kand);
        var ks = new HashSet<int>(kand);

        // Zielzelle: 7x7 begehbar fuer Fussvolk und leer, keine Tuersperre, kein
        // anderer Lebender (Einheit, nicht Gebaeude) naeher als 8 Zellen.
        var mc = _entities[kand[0]].Move;
        Vector2I? ziel = null;
        for (int r = 7; r < _nav!.Height - 7 && ziel == null; r++)
            for (int c = 7; c < _nav.Width - 7 && ziel == null; c++)
            {
                bool frei = true;
                for (int dy = -3; dy <= 3 && frei; dy++)
                    for (int dx = -3; dx <= 3 && frei; dx++)
                        frei = _nav.CanEnter(c + dx, r + dy, mc)
                               && _nav.OccupantAt(c + dx, r + dy) < 0 && !_nav.IstTuerGesperrt(c + dx, r + dy);
                if (!frei) continue;
                foreach (var (o, k) in Indexed())
                {
                    if (ks.Contains(k) || o.Dead || o.IsProp || o.IsBuilding) continue;
                    if (Mathf.Max(Mathf.Abs(o.Col - c), Mathf.Abs(o.Row - r)) < 8) { frei = false; break; }
                }
                if (frei) ziel = new Vector2I(c, r);
            }
        if (ziel is not { } T) return "  ⚠ 3 Gruppe: keine freie 7x7-Flaeche fern von anderen — NICHT GEMESSEN\n";

        foreach (int k in kand) { var e = _entities[k]; e.Owner = e.Team = me; e.ShownOwner = -1; }
        var start = new List<Vector2I>();
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == 3)
                    start.Add(new Vector2I(T.X + dx, T.Y + dy));

        (int ImZiel, int Zellen, int Bildpunkte, int Takte) Lauf(int anzahl)
        {
            var wer = kand.GetRange(0, anzahl);
            for (int k = 0; k < wer.Count; k++) AwStelle(wer[k], start[k]);
            _sel.Clear();
            foreach (int k in wer) _sel.Add(k);
            PostMove(CellCenter(T.X, T.Y));
            RingLeeren();
            int ruhig = 0, t = 0;
            for (; t < 3000 && ruhig < 120; t++)
            {
                foreach (int k in wer) { var e = _entities[k]; e.Hp = e.HpMax; e.Target = -1; }
                SimTickFuerProbe();
                bool alle = true;
                foreach (int k in wer) if (!FvSteht(_entities[k])) { alle = false; break; }
                ruhig = alle ? ruhig + 1 : 0;
            }
            int imZiel = 0;
            var zellen = new HashSet<Vector2I>();
            var punkte = new HashSet<Vector2I>();
            foreach (int k in wer)
            {
                var e = _entities[k];
                zellen.Add(new Vector2I(e.Col, e.Row));
                if (e.Col != T.X || e.Row != T.Y) continue;
                imZiel++;
                var p = FussvolkOrt(e);
                punkte.Add(new Vector2I(Mathf.RoundToInt(p.X), Mathf.RoundToInt(p.Y)));
            }
            return (imZiel, zellen.Count, punkte.Count, t);
        }

        int abgVor = NeunerzelleAbgewiesen;
        var a = Lauf(6);
        okA = a.ImZiel == 6 && a.Bildpunkte == 6;
        sb.Append($"  {(okA ? "ok  " : "⚠ ROT")} 3a 6 Soldaten ohne Formation auf ({T.X},{T.Y}): in der Zielzelle {a.ImZiel}, Zellen {a.Zellen}, "
                + $"verschiedene Bildpunkte in der Zielzelle {a.Bildpunkte} (nach {a.Takte} Takten); Soll 6/6. "
                + "Nullmodell --fussstreuung-aus: 1 Bildpunkt\n");
        var b = Lauf(12);
        okB = b.ImZiel == 9 && b.Zellen >= 2;
        sb.Append($"  {(okB ? "ok  " : "⚠ ROT")} 3b 12 Soldaten: in der Zielzelle {b.ImZiel}, Zellen {b.Zellen}, Bildpunkte {b.Bildpunkte} "
                + $"(nach {b.Takte} Takten, Neunerzelle {NeunerzelleAbgewiesen - abgVor}x abgewiesen); Soll 9 in der Zielzelle, Rest daneben. "
                + "Nullmodell --neunerzelle-aus: 12\n");
        gemessen = true;
        _sel.Clear();
        return sb.ToString();
    }

    private IEnumerable<(Entity E, int I)> Indexed()
    {
        for (int i = 0; i < _entities.Count; i++) yield return (_entities[i], i);
    }

    /// <summary>Teil 4: Spielstand hin und zurueck im Speicher (keine Datei, bug-548):
    /// (a) mit den neuen Feldern muss jeder Versatz bleiben, auch ein verstellter;
    /// (b) ohne sie (alter Stand) kommen die Kartensoldaten mit den Rohwerten zurueck.</summary>
    private string FsSpielstand(Dictionary<int, (int Dx, int Dy)> roh, List<int> probanden, out bool ok)
    {
        ok = false;
        var sb = new StringBuilder();
        // Eingriff: einen Kartensoldaten verstellen — er darf beim Laden NICHT auf den
        // Kartenwert zurueckfallen.
        Entity? verstellt = null;
        foreach (var e in _entities)
            if (e.Infantry >= 0 && !e.Dead && roh.ContainsKey(e.Slot)) { verstellt = e; break; }
        if (verstellt == null) return "  ⚠ 4 Spielstand: kein Kartensoldat — NICHT GEMESSEN\n";
        verstellt.StreuX = (sbyte)(verstellt.StreuX == -17 ? 16 : -17);
        verstellt.StreuY = (sbyte)(verstellt.StreuY == 6 ? -5 : 6);
        var vorher = new Dictionary<int, (int, int)>();
        foreach (var e in _entities)
            if (e.Infantry >= 0 && e.Slot >= 0 && e.StreuGesetzt) vorher[e.Slot] = (e.StreuX, e.StreuY);
        string json = SaveStateJson("fussstreuung-check", "probe");

        (int Gleich, int N) Vergleich(Dictionary<int, (int, int)> soll)
        {
            int g = 0, n = 0;
            foreach (var e in _entities)
            {
                if (e.Infantry < 0 || !soll.TryGetValue(e.Slot, out var s)) continue;
                n++;
                if (e.StreuGesetzt && e.StreuX == s.Item1 && e.StreuY == s.Item2) g++;
            }
            return (g, n);
        }

        var j = new Json();
        bool gelesen = j.Parse(json) == Error.Ok && j.Data.VariantType == Variant.Type.Dictionary;
        if (!gelesen) return "  ⚠ 4 Spielstand: nicht lesbar — DURCHGEFALLEN\n";
        ApplySaveState(j.Data.AsGodotDictionary<string, Variant>());
        var a = Vergleich(vorher);
        bool okA = a.N >= 20 && a.Gleich == a.N;

        // (b) derselbe Stand ohne streu_x/streu_y — so sieht ein Spielstand vor 0.6.11 aus.
        // Die Vorlagen des Lesers sind im Spiel die FRISCH geladene Karte; hier die
        // laufende Liste — also erst die Kartenwerte wiederherstellen (Eingriff).
        foreach (var e in _entities)
            if (e.Infantry >= 0 && roh.TryGetValue(e.Slot, out var rw)) { e.StreuX = (sbyte)rw.Dx; e.StreuY = (sbyte)rw.Dy; }
        var j2 = new Json();
        j2.Parse(json);
        var root = j2.Data.AsGodotDictionary<string, Variant>();
        int entfernt = 0;
        if (root.TryGetValue("entities", out var ev) && ev.VariantType == Variant.Type.Array)
            foreach (var it in ev.AsGodotArray())
            {
                if (it.VariantType != Variant.Type.Dictionary) continue;
                var d = it.AsGodotDictionary<string, Variant>();
                if (d.Remove("streu_x")) entfernt++;
                d.Remove("streu_y");
            }
        ApplySaveState(root);
        FussStreuungZuteilen();
        var sollRoh = new Dictionary<int, (int, int)>();
        foreach (var kv in roh) sollRoh[kv.Key] = (kv.Value.Dx, kv.Value.Dy);
        var b = Vergleich(sollRoh);
        bool okB = b.N >= 20 && b.Gleich == b.N && entfernt > 0;
        ok = okA && okB;
        sb.Append($"  {(okA ? "ok  " : "⚠ ROT")} 4a Spielstand (im Speicher): {a.Gleich}/{a.N} Versaetze gleich, darunter der verstellte "
                + $"Platz {verstellt.Slot} → ({(sbyte)vorher[verstellt.Slot].Item1},{(sbyte)vorher[verstellt.Slot].Item2})\n");
        sb.Append($"  {(okB ? "ok  " : "⚠ ROT")} 4b alter Stand ohne streu_x/streu_y ({entfernt} Felder entfernt): {b.Gleich}/{b.N} Kartensoldaten "
                + "wieder mit raw +0x1D/+0x1E\n");
        _ = probanden;
        return sb.ToString();
    }

    // ======================================================================
    //  --fussvolk-klickkasten-check (bug-602)
    // ======================================================================

    private struct KkErgebnis
    {
        public int N, Aus, V, Innen, InnenN, AussenN, Eigen, AussenFehl, R1Fehl, R2, M, R3, R4Fehl;
        public int TransportGemessen, TransportOk, SchwelleOk, SchwelleN;
        public bool Bestanden => N >= 20 && V * 2 >= N && Eigen >= N && InnenN >= N && Innen == InnenN && AussenFehl == 0 && R1Fehl == 0
                                 && R2 == N && R3 == M && R4Fehl == 0
                                 && TransportOk == TransportGemessen && SchwelleOk == SchwelleN;
        public string Zeile =>
            $"Soldaten {N} (mit dx/dy != 0: {V}; Punkte im Fahrzeug-Sperrgebiet {Aus}), Innen = Sieger des Originals {Innen}/{InnenN} "
            + $"(davon der Soldat selbst {Eigen}), Aussen != Sieger des Originals {AussenFehl}/{AussenN}, "
            + $"Rahmen R1-Fehlwahl {R1Fehl}/{N}, R2 {R2}/{N}; Fahrzeuge {M}, R3 {R3}/{M}, R4-Fehlwahl {R4Fehl}/{M}; "
            + $"Transporter {TransportOk}/{TransportGemessen}, Schwelle {SchwelleOk}/{SchwelleN}";
    }

    public string FussvolkKlickkastenCheckLine()
    {
        var sb = new StringBuilder("fussvolk-klickkasten-check (bug-602)\n");
        if (_nav == null) return sb.Append("  keine Karte — NICHT GEMESSEN").ToString();
        FussStreuungZuteilen();
        var roh = RohStreuungLesen();
        string schalter = (FussvolkKlickkastenAlt ? " [Nullmodell --fussvolk-klickkasten-alt]" : "")
                        + (GummibandKastenAlt ? " [Nullmodell --gummiband-kasten-alt]" : "")
                        + (GummibandTransporterAlt ? " [Nullmodell --gummiband-transporter-alt]" : "")
                        + (GummibandSchwelleAlt ? " [Nullmodell --gummiband-schwelle-alt]" : "")
                        + (FussstreuungAus ? " [--fussstreuung-aus]" : "");
        bool nebelVor = PickOhneNebel;
        PickOhneNebel = true;
        var haupt = KkMessen(roh);
        sb.Append($"  {haupt.Zeile}{schalter}\n");

        // Ankunft mitmessen: dieselben Punkte im selben Lauf unter jedem Gegenschalter.
        void Gegen(string name, System.Action an, System.Action aus)
        {
            an();
            var g = KkMessen(roh);
            aus();
            sb.Append($"  Gegenprobe im Lauf {name}: {(g.Bestanden ? "⚠ BESTEHT (misst nicht)" : "ROT wie erwartet")} — {g.Zeile}\n");
        }
        bool k0 = FussvolkKlickkastenAlt, g0 = GummibandKastenAlt, t0 = GummibandTransporterAlt, s0 = GummibandSchwelleAlt;
        if (!k0) Gegen("--fussvolk-klickkasten-alt", () => FussvolkKlickkastenAlt = true, () => FussvolkKlickkastenAlt = false);
        if (!g0) Gegen("--gummiband-kasten-alt", () => GummibandKastenAlt = true, () => GummibandKastenAlt = false);
        if (!t0) Gegen("--gummiband-transporter-alt", () => GummibandTransporterAlt = true, () => GummibandTransporterAlt = false);
        if (!s0) Gegen("--gummiband-schwelle-alt", () => GummibandSchwelleAlt = true, () => GummibandSchwelleAlt = false);
        PickOhneNebel = nebelVor;
        _sel.Clear();
        SetPrimary();
        if (haupt.N > 0 && haupt.V == 0)
            return sb.Append("  KEIN URTEIL: dx/dy fehlen").ToString();
        return sb.Append(haupt.Bestanden ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    private KkErgebnis KkMessen(Dictionary<int, (int Dx, int Dy)> roh)
    {
        var r = new KkErgebnis();
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        // Die Kaesten des ORIGINALS fuer alle Einheiten, schalterunabhaengig: Fussvolk
        // 0x431DD9 am unabhaengig gerechneten Blitpunkt; alles andere (Fahrzeug, Bertha,
        // Schiff) ist ein Sperrgebiet: ein Klickpunkt darin wird nicht gewertet (unser
        // Fahrzeugkasten liegt 1 px neben dem des Originals, R6 Punkt 12 — nicht Auftrag).
        var fuss = new List<(int I, int Row, Rect2 R)>();
        var sperr = new List<Rect2>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (o.Dead || o.IsBuilding || o.IsProp || Untergestellt(o)) continue;
            if (!PickOhneNebel && ImNebelVerborgen(o)) continue;
            if (o.NoStructure && !Anfassbar(o)) continue;
            if (o.Infantry >= 0)
            {
                var (odx, ody) = roh.TryGetValue(o.Slot, out var rv) ? rv : ((int)o.StreuX, (int)o.StreuY);
                var ob = OriginalFussBlit(o.Col, o.Row, odx, ody);
                fuss.Add((i, o.Row, new Rect2(ob + new Vector2(5, 13), new Vector2(14, 25))));
            }
            else
            {
                sperr.Add(BodyRect(o).Grow(2));
                var op = OriginalPos(o.Col, o.Row);
                sperr.Add(new Rect2(op + new Vector2(-24, -36), new Vector2(46, 53)));
            }
        }
        // Wer gewinnt im Original? Hinterste Zeile zuerst, darin der erste Satz (0x431C31).
        int OrigSieger(Vector2 p)
        {
            int best = -1, bestRow = int.MaxValue;
            foreach (var (k, row, box) in fuss)
                if (row < bestRow && box.HasPoint(p)) { best = k; bestRow = row; }
            return best;
        }
        bool Gesperrt(Vector2 p) { foreach (var q in sperr) if (q.HasPoint(p)) return true; return false; }
        int PickEinheit(Vector2 p)
        {
            int k = Pick(p);
            return k >= 0 && !_entities[k].IsBuilding && !_entities[k].IsProp ? k : -1;
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Infantry < 0 || !roh.TryGetValue(e.Slot, out var rv) || !FvSteht(e)) continue;
            if (e.NoStructure && !Anfassbar(e)) continue;
            var bb = OriginalFussBlit(e.Col, e.Row, rv.Dx, rv.Dy);
            float bx = bb.X, by = bb.Y;
            var punkte = new[]
            {
                new Vector2(bx + 11, by + 15), new Vector2(bx + 11, by + 36),   // K1, K2 innen
                new Vector2(bx + 11, by + 12), new Vector2(bx + 11, by + 38),   // K3, K4
                new Vector2(bx + 4, by + 25), new Vector2(bx + 19, by + 25),    // K5, K6
            };
            r.N++;
            if (rv.Dx != 0 || rv.Dy != 0) r.V++;
            for (int q = 0; q < 6; q++)
            {
                if (Gesperrt(punkte[q])) { r.Aus++; continue; }   // nicht gewertet
                int soll = OrigSieger(punkte[q]);
                int ist = PickEinheit(punkte[q]);
                if (q < 2) { r.InnenN++; if (ist == soll) r.Innen++; if (soll == i) r.Eigen++; }
                else { r.AussenN++; if (ist != soll) r.AussenFehl++; }
            }

            // Rahmen: der Soldat gehoert fuer den Augenblick dem Betrachter
            int ow = e.Owner, tm = e.Team;
            e.Owner = e.Team = me;
            BoxSelect(new Rect2(bx + 5, by + 13, 13, 24), false);           // R1 = Originalkasten
            if (_sel.Contains(i)) r.R1Fehl++;
            BoxSelect(new Rect2(bx + 19, by + 19, 2, 2), false);            // R2 um (bx+20, by+20)
            if (_sel.Contains(i)) r.R2++;
            e.Owner = ow; e.Team = tm;
        }

        // Fahrzeuge: R3 (Punkt sy+40, x-Ueberlappung) und R4 (ganzer Kasten bis Pos.y−7)
        var fahrzeuge = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Infantry >= 0 || e.GameUnitType != 0 || !FvSteht(e) || e.Part == MaterialtransporterTeil) continue;
            if (e.Move == Simulation.NavGrid.MoveClass.Ship || e.FootW > 1 || e.FootH > 1) continue;
            if (e.NoStructure && !Anfassbar(e)) continue;
            fahrzeuge.Add(i);
            var pos = OriginalPos(e.Col, e.Row);
            r.M++;
            int ow = e.Owner, tm = e.Team;
            e.Owner = e.Team = me;
            BoxSelect(new Rect2(pos.X - 14, pos.Y - 6, 4, 2), false);
            if (_sel.Contains(i)) r.R3++;
            BoxSelect(new Rect2(pos.X - 22, pos.Y - 34, 42, 27), false);
            if (_sel.Contains(i)) r.R4Fehl++;
            e.Owner = ow; e.Team = tm;
        }

        // Materialtransporter (Eingriff: B bekommt zeitweise +0x0E = 0x47)
        if (fahrzeuge.Count >= 2)
        {
            int ia = fahrzeuge[0], ib = fahrzeuge[1];
            var A = _entities[ia]; var B = _entities[ib];
            int oa = A.Owner, ta = A.Team, ob = B.Owner, tb = B.Team, pb = B.Part;
            A.Owner = A.Team = B.Owner = B.Team = me;
            B.Part = MaterialtransporterTeil;
            var pa = OriginalPos(A.Col, A.Row); var pbp = OriginalPos(B.Col, B.Row);
            var ra = new Rect2(pa.X - 14, pa.Y - 6, 4, 2);
            var rb = new Rect2(pbp.X - 14, pbp.Y - 6, 4, 2);
            BoxSelect(ra.Merge(rb), false);
            r.TransportGemessen++;
            if (_sel.Contains(ia) && !_sel.Contains(ib)) r.TransportOk++;   // Soll: nur der Panzer
            BoxSelect(rb, false);
            r.TransportGemessen++;
            if (_sel.Contains(ib)) r.TransportOk++;                          // allein: gewaehlt
            B.Part = pb;
            A.Owner = oa; A.Team = ta; B.Owner = ob; B.Team = tb;
        }

        // Schwelle: Kartenpunkte = Bildschirmpunkte (Zoom 1)
        (Vector2 Zug, bool Soll)[] zuege =
        {
            (new Vector2(8, 0), false), (new Vector2(11, 0), true),
            (new Vector2(7, 7), false), (new Vector2(0, -11), true), (new Vector2(10, 10), false),
        };
        foreach (var (zug, soll) in zuege)
        {
            r.SchwelleN++;
            if (IstRahmen(Vector2.Zero, zug, Vector2.Zero, zug) == soll) r.SchwelleOk++;
        }
        return r;
    }
}

public partial class MapViewer
{
    private bool _fussstreuungCheck, _fussvolkKlickkastenCheck;

    /// <summary>bug-596/602 — die Schalter von Paket E. true = erkannt.</summary>
    private bool FussvolkSchalter(string a)
    {
        switch (a)
        {
            case "--fussstreuung-aus": MapEntityLayer.FussstreuungAus = true; return true;
            case "--neunerzelle-aus": MapEntityLayer.NeunerzelleAus = true; return true;
            case "--fussanker-tief-alt": MapEntityLayer.FussankerTiefAlt = true; return true;
            case "--fussvolk-klickkasten-alt": MapEntityLayer.FussvolkKlickkastenAlt = true; return true;
            case "--gummiband-kasten-alt": MapEntityLayer.GummibandKastenAlt = true; return true;
            case "--gummiband-transporter-alt": MapEntityLayer.GummibandTransporterAlt = true; return true;
            case "--gummiband-schwelle-alt": MapEntityLayer.GummibandSchwelleAlt = true; return true;
            case "--fussstreuung-check": _fussstreuungCheck = true; return true;
            case "--fussvolk-klickkasten-check": _fussvolkKlickkastenCheck = true; return true;
        }
        return false;
    }

    /// <summary>Nach dem Laden (Skript steht): true = ein Pruefstand lief und hat beendet.</summary>
    private bool FussvolkPruefstand()
    {
        if (!(_fussstreuungCheck || _fussvolkKlickkastenCheck) || !_entities.ErwartungBereit()) return false;
        // erst der Klickkasten (stehende Karte), dann die Streuung (bewegt Soldaten)
        if (_fussvolkKlickkastenCheck) GD.Print(_entities.FussvolkKlickkastenCheckLine());
        if (_fussstreuungCheck) GD.Print(_entities.FussstreuungCheckLine());
        _fussstreuungCheck = _fussvolkKlickkastenCheck = false;
        GetTree().Quit(0);
        return true;
    }
}
