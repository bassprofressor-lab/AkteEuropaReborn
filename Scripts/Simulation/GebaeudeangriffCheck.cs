namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <b><c>--gebaeudeangriff-check[=Platz]</c> — bug-597 (KayelGee R7 #7, 22.10.2026).</b>
/// Nach <c>berichte/gebaeudeangriff-zielstellen-opus.md</c> »Prüfstand«, mit den
/// Berichtigungen aus <c>…-fable.md</c> §8.
///
/// <para><b>A</b> — acht eigene Kampfeinheiten (Fahrzeuge und, wenn vorhanden, Fussvolk)
/// stehen auf einem Ring etwa sieben Zellen um den Fussabdruck eines feindlichen
/// Gebaeudes, alle acht Richtungen. Angriffsbefehl über den SPIELERWEG
/// (<c>PostAttack</c> auf die Gebaeudemitte → Befehl 11 → Ring → <c>RingLeeren</c>),
/// dann 1500 Takte. Soll: <b>alle 8 schiessen</b> und es werden <b>mindestens 4
/// verschiedene Zellen</b> angezielt. Nullmodell <c>--gebaeudeziel-alt</c>: genau eine
/// (der Anker).</para>
/// <para><b>B</b> — eine neunte Einheit (Fahrzeug) mit Reichweite 1: sie kann keine
/// Fussabdruckzelle aufnehmen (Ring 0 ist die eigene Zelle), faehrt die gewuerfelte
/// Zelle an und kommt an. Soll: Auftrag zu Ende (UKOL 0 @0x407E1A). Nullmodell
/// <c>--gebaeudeangriff-ankunft-alt</c>: sie behaelt das Ziel.</para>
/// <para>⚠ EINGRIFFE (angesagt): Probanden werden mit <c>AwStelle</c> gesetzt; jeden
/// Takt werden Probanden und Gebaeude auf volle Huelle, die Probanden auf volle
/// Munition gestellt (sonst entscheidet das Gefecht, wer »nie geschossen« hat); die
/// neunte Einheit bekommt Reichweite 1 / Mindestreichweite 0. Geurteilt wird erst nach
/// <c>RingLeeren</c> (bug-600).</para>
/// </summary>
public partial class MapEntityLayer
{
    private static readonly (int Dx, int Dy)[] GaRichtungen =
        { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };

    public string GebaeudeangriffCheckLine(string arg)
    {
        var sb = new StringBuilder("gebaeudeangriff-check\n");
        string schalter = (GebaeudezielAlt ? " [Nullmodell --gebaeudeziel-alt]" : "")
                        + (GebaeudeangriffAnkunftAlt ? " [Nullmodell --gebaeudeangriff-ankunft-alt]" : "");
        if (_nav == null) return sb.Append("  keine Karte — NICHT GEMESSEN").ToString();

        // ---- die Probanden: eigene, bewegliche Kampfeinheiten ------------------
        var fahrzeuge = new List<int>();
        var fussvolk = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != ViewPlayer) continue;
            if (Untergestellt(e) || e.InGebaeude != null || !CanFight(e)) continue;
            if (e.Move is Simulation.NavGrid.MoveClass.Ship) continue;
            if (e.Infantry >= 0) { if (e.Range is >= 1 and <= 6) fussvolk.Add(i); }
            else if (e.Range >= 3 && RangeMinOf(e) <= 1) fahrzeuge.Add(i);
        }
        var probanden = new List<int>();
        for (int k = 0; k < fussvolk.Count && probanden.Count < 3; k++) probanden.Add(fussvolk[k]);
        foreach (int f in fahrzeuge) if (probanden.Count < 8) probanden.Add(f);
        foreach (int f in fussvolk) if (probanden.Count < 8 && !probanden.Contains(f)) probanden.Add(f);
        int neunte = -1;
        foreach (int f in fahrzeuge) if (!probanden.Contains(f)) { neunte = f; break; }
        if (neunte < 0) foreach (int f in fussvolk) if (!probanden.Contains(f)) { neunte = f; break; }
        if (probanden.Count < 8 || neunte < 0)
        {
            int eigen = 0, beweglich = 0, kampf = 0, unter = 0;
            foreach (var e in _entities)
            {
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer) continue;
                eigen++; if (e.Mobile) beweglich++; if (CanFight(e)) kampf++; if (Untergestellt(e)) unter++;
                if (CanFight(e)) sb.Append($"   Platz {e.Slot} inf {e.Infantry} R {e.Range}/{e.RangeMin} Move {e.Move} InGeb {e.InGebaeude != null} ({e.Col},{e.Row})\n");
            }
            sb.Append($"  (Betrachter {ViewPlayer}: eigene Einheiten {eigen}, beweglich {beweglich}, kampffaehig {kampf}, untergestellt {unter})\n");
            return sb.Append($"  zu wenige eigene Kampfeinheiten ({fahrzeuge.Count} Fahrzeuge, {fussvolk.Count} Fussvolk; "
                           + "Soll 8 + 1) — NICHT GEMESSEN (K6 oder K4 nehmen)").ToString();
        }

        // ---- das Ziel: ein feindliches Gebaeude mit grossem Fussabdruck ---------
        int ziel = -1, besteFlaeche = 0;
        float besterAbstand = float.MaxValue;
        var p0 = _entities[probanden[0]];
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.Dead || b.IsProp || !IstAngriffsziel(p0, b)) continue;
            if (arg.Length > 0 && int.TryParse(arg, out int platz) && b.Slot != platz) continue;
            int fussZellen = 0;
            for (int c = b.Col - 1; c < b.Col + b.FootW + 2; c++)
                for (int r = b.Row - 1; r < b.Row + b.FootH + 2; r++)
                    if (GebaeudeFussZelle(i, b, c, r)) fussZellen++;
            if (b.FootW < 3 || b.FootH < 3 || fussZellen < 12) continue;   // ein Grossbau
            // das NAECHSTE erreichbare Gebaeude (vom ersten Probanden aus)
            var naheB = _nav.NearestFree(new Vector2I(b.Col + b.FootW / 2, b.Row + b.FootH / 2), p0.Move, probanden[0]);
            if (naheB == null || _nav.FindPath(new Vector2I(p0.Col, p0.Row), naheB.Value, p0.Move, probanden[0]) == null) continue;
            float abst = new Vector2(b.Col - p0.Col, b.Row - p0.Row).Length();
            if (ziel < 0 || abst < besterAbstand) { besterAbstand = abst; besteFlaeche = fussZellen; ziel = i; }
        }
        if (ziel < 0) return sb.Append("  kein erreichbares feindliches Gebaeude >= 3x3 mit >= 12 Fussabdruckzellen — NICHT GEMESSEN").ToString();
        var g = _entities[ziel];
        sb.Append($"  Ziel: Gebaeude Platz {g.Slot} Art {g.BType} Besitzer {g.Owner} Anker ({g.Col},{g.Row}) "
                + $"{g.FootW}x{g.FootH}, {besteFlaeche} Fussabdruckzellen{schalter}\n");
        for (int r = g.Row; r < g.Row + g.FootH; r++)
        {
            sb.Append($"    Zeile {r,3}: ");
            for (int c = g.Col; c < g.Col + g.FootW; c++) sb.Append(GebaeudeFussZelle(ziel, g, c, r) ? '#' : '.');
            sb.Append('\n');
        }

        // ---- aufstellen ----------------------------------------------------------
        int mx = g.Col + g.FootW / 2, my = g.Row + g.FootH / 2;

        var alle = new List<int>(probanden) { neunte };
        var belegt = new HashSet<Vector2I>();
        // Abstand einer Zelle zur naechsten Fussabdruckzelle
        float FussAbstand(Vector2I z)
        {
            float best = float.MaxValue;
            for (int c = g.Col - 1; c < g.Col + g.FootW + 2; c++)
                for (int r = g.Row - 1; r < g.Row + g.FootH + 2; r++)
                    if (GebaeudeFussZelle(ziel, g, c, r))
                        best = Mathf.Min(best, new Vector2(c - z.X, r - z.Y).Length());
            return best;
        }
        int[] radien = { 7, 8, 6, 9, 10, 11, 12 };
        for (int k = 0; k < alle.Count; k++)
        {
            var e = _entities[alle[k]];
            // Erreichbar heisst: es gibt einen Weg zur freien Zelle nahe der Gebaeudemitte.
            var nahe = _nav.NearestFree(new Vector2I(mx, my), e.Move, alle[k]);
            Vector2I? z = null;
            for (int rr = 0; rr < radien.Length && z == null; rr++)
                for (int dd = 0; dd < 8 && z == null; dd++)
                {
                    var (dx, dy) = GaRichtungen[(k + dd) % 8];
                    int ax = g.FootW / 2 + radien[rr] + (k >= 8 ? 2 : 0), ay = g.FootH / 2 + radien[rr];
                    var wunsch = new Vector2I(Mathf.Clamp(mx + dx * ax, 2, _nav.Width - 3),
                                              Mathf.Clamp(my + dy * ay, 2, _nav.Height - 3));
                    var kand = _nav.NearestFree(wunsch, e.Move, alle[k]);
                    if (kand == null || belegt.Contains(kand.Value) || FussAbstand(kand.Value) < 5f) continue;
                    if (nahe == null || _nav.FindPath(kand.Value, nahe.Value, e.Move, alle[k]) == null) continue;
                    z = kand;
                }
            if (z == null) return sb.Append($"  keine erreichbare freie Zelle fuer Platz {e.Slot} — NICHT GEMESSEN").ToString();
            AwStelle(alle[k], z.Value);
            belegt.Add(z.Value);
            e.Cooldown = 0; e.Ammo = e.AmmoMax; e.Ordered = false; e.Target = -1;
            e.ChaseRetry = 0; e.RetryIn = 0;
            e.SchussZelle = null; e.SchussZelleFuer = -1; e.GebaeudeFahrtFuer = -1;
        }
        var n9 = _entities[neunte];
        int range9 = n9.Range, rmin9 = n9.RangeMin;

        // ---- der Spielerweg: Auswahl, Angriff, Ring --------------------------------
        _gaZiel = ziel; _gaSchuesse.Clear(); _gaErsteZelle.Clear(); _gaZellen.Clear();
        _sel.Clear();
        foreach (int k in alle) _sel.Add(k);
        // ⚠ EINGRIFF: das Gebaeude kann noch im Nebel liegen (die Probanden sind eben
        // erst hingestellt) — fuer den einen Klick zaehlt der Nebel nicht.
        bool nebelVor = PickOhneNebel;
        PickOhneNebel = true;
        Vector2 klick = BodyRect(g).GetCenter();
        if (Pick(klick) != ziel)
        {
            // die Mitte des Bildes ist nicht immer Griff — eine Fussabdruckzelle nehmen
            bool fertig = false;
            for (int c = g.Col; c < g.Col + g.FootW && !fertig; c++)
                for (int r = g.Row; r < g.Row + g.FootH && !fertig; r++)
                    if (GebaeudeFussZelle(ziel, g, c, r) && Pick(CellCenter(c, r)) == ziel)
                    { klick = CellCenter(c, r); fertig = true; }
        }
        int gepickt = Pick(klick);
        bool abgesetzt = PostAttack(klick);
        PickOhneNebel = nebelVor;
        int ringTakte = RingLeeren();
        // EINGRIFF B erst nach dem Befehl (CanFight): Fahrzeug Reichweite 1 (Ringe < 1 =
        // nur die eigene Zelle), Fussvolk 0 (Zaehler 0x4F5B28[0] = 0 → nur die eigene Zelle).
        n9.Range = n9.Infantry >= 0 ? 0 : 1; n9.RangeMin = 0;
        int angenommen = 0;
        foreach (int k in alle) if (_entities[k].Target == ziel && _entities[k].Ordered) angenommen++;
        sb.Append($"  Angriff ueber PostAttack/Ring: abgesetzt {abgesetzt}, Ring geleert nach {ringTakte} Takt(en), "
                + $"angenommen {angenommen}/9 (Pick {(gepickt == ziel ? "Gebaeude" : $"DANEBEN: {gepickt}")})\n");

        // ---- laufen lassen -----------------------------------------------------------
        const int Takte = 1500;
        var ersterSchuss = new Dictionary<int, int>();
        var auftragEnde = new Dictionary<int, (int Takt, string Grund)>();
        bool gruendeVor = Schussgruende;
        Schussgruende = true;
        var standErster = new Dictionary<int, Vector2I>();
        int ende9 = -1;
        int gewuerfeltVor = GebaeudeFahrtenGewuerfelt, gewaehltVor = GebaeudeZellenGewaehlt;
        for (int t = 0; t < Takte; t++)
        {
            foreach (int k in alle)
            {
                var e = _entities[k];
                if (e.Dead) continue;
                e.Hp = e.HpMax; if (e.AmmoMax > 0) e.Ammo = e.AmmoMax;   // EINGRIFF
            }
            g.Hp = g.HpMax;                                              // EINGRIFF
            SimTickFuerProbe();
            foreach (int k in probanden)
                if (!ersterSchuss.ContainsKey(k) && _gaSchuesse.GetValueOrDefault(k) > 0)
                { ersterSchuss[k] = t; standErster[k] = new Vector2I(_entities[k].Col, _entities[k].Row); }
            if (ende9 < 0 && n9.Target != ziel) ende9 = t;
            foreach (int k in probanden)
                if (!auftragEnde.ContainsKey(k) && _entities[k].Target != ziel)
                    auftragEnde[k] = (t, _entities[k].Schussgrund);
        }

        // ---- urteilen -------------------------------------------------------------------
        int schuetzen = 0, ankunftEnde = 0, ankunftAusser = 0;
        var zellen = new HashSet<Vector2I>();
        var standZellen = new HashSet<Vector2I>();
        foreach (int k in probanden)
        {
            var e = _entities[k];
            int n = _gaSchuesse.GetValueOrDefault(k);
            if (n > 0) schuetzen++;
            else if (auftragEnde.TryGetValue(k, out var aEnde) && aEnde.Grund.Contains("UKOL 0"))
            {
                ankunftEnde++;
                if (FussAbstand(new Vector2I(e.Col, e.Row)) > RangeOf(e)) ankunftAusser++;
            }
            if (_gaErsteZelle.TryGetValue(k, out var z0)) zellen.Add(z0);
            if (standErster.TryGetValue(k, out var s0)) standZellen.Add(s0);
            string art = e.Infantry >= 0 ? "Fussvolk" : "Fahrzeug";
            sb.Append($"   Platz {e.Slot,5} {art,-8} R {e.Range}/{RangeMinOf(e):0} : "
                    + (n > 0
                        ? $"erster Schuss Takt {ersterSchuss[k],4} von ({standErster[k].X},{standErster[k].Y}) "
                          + $"auf Zelle ({z0.X},{z0.Y}), {n} Schuesse, jetzt Zelle "
                          + (e.SchussZelle is { } zj ? $"({zj.X},{zj.Y})" : "-")
                        : $"NIE geschossen, steht ({e.Col},{e.Row}) {FussAbstand(new Vector2I(e.Col, e.Row)):0.0} Zellen vor dem Fussabdruck, Ziel {e.Target} (Gebaeude = {ziel}), "
                          + (auftragEnde.TryGetValue(k, out var ae) ? $"Auftrag zu Ende Takt {ae.Takt} »{ae.Grund}«, " : "")
                          + $"Weg {(e.Path == null ? "-" : e.Path.Count.ToString())}, "
                          + $"Grund »{e.Schussgrund}«")
                    + "\n");
        }
        // ⚠ Die Messlatte »alle 8 schiessen« (Opus) haelt das ORIGINAL selbst nicht:
        // der Kasten 0x40FC90 reicht bis 4 Zellen vor den Anker, eine Eckzelle liegt
        // dort ausser Reichweite, und wer sie fahrend erreicht, hat den Auftrag
        // beendet (UKOL 0 @0x407E1A). Also: jede der 8 schiesst ODER ist vom
        // Ankunftsarm beendet; mindestens 6 schiessen. --gebaeudeangriff-ankunft-alt
        // zeigt, dass ohne den Arm alle 8 schiessen.
        bool aOk = schuetzen + ankunftEnde == 8 && schuetzen >= 6 && zellen.Count >= 4;
        sb.Append($"  A: Schuetzen {schuetzen}/8 (Soll alle, ausser vom Ankunftsarm beendet: {ankunftEnde}, "
                + $"davon ausser Reichweite stehend {ankunftAusser}; mindestens 6), verschiedene Zielzellen {zellen.Count} (Soll >= 4), "
                + $"verschiedene Standzellen {standZellen.Count}, Fahrzellen gewuerfelt {GebaeudeFahrtenGewuerfelt - gewuerfeltVor}, "
                + $"Schusszellen gewaehlt {GebaeudeZellenGewaehlt - gewaehltVor}{(aOk ? "" : "   ⚠")}\n");
        bool bOk = ende9 >= 0 && n9.Target != ziel && !n9.Ordered;
        sb.Append($"  B: Einheit Platz {n9.Slot} ({(n9.Infantry >= 0 ? "Fussvolk" : "Fahrzeug")}) mit Reichweite {n9.Range}: "
                + (ende9 >= 0 ? $"Auftrag zu Ende in Takt {ende9}" : "Auftrag NICHT zu Ende")
                + $", steht ({n9.Col},{n9.Row}), Fahrzelle "
                + (n9.GebaeudeFahrzelle is { } fz ? $"({fz.X},{fz.Y})" : "-")
                + $", Ankunft beendet {GebaeudeAnkunftBeendet}x, neu gewuerfelt {GebaeudeAnkunftNeu}x "
                + $"(Soll: zu Ende, UKOL 0 @0x407E1A){(bOk ? "" : "   ⚠")}\n");
        n9.Range = range9; n9.RangeMin = rmin9;
        Schussgruende = gruendeVor;
        _gaZiel = -1;
        bool ok = abgesetzt && angenommen == 9 && aOk && bOk;
        sb.Append(ok ? "gebaeudeangriff-check: BESTANDEN" : "gebaeudeangriff-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}

public partial class MapViewer
{
    private string? _gebaeudeangriffCheck;

    /// <summary>bug-597: <c>--gebaeudeangriff-check</c> — siehe
    /// <see cref="MapEntityLayer.GebaeudeangriffCheckLine"/>.</summary>
    private bool GebaeudeangriffCheckLauf()
    {
        if (_gebaeudeangriffCheck == null || !_entities.ErwartungBereit()) return false;
        string arg = _gebaeudeangriffCheck;
        _gebaeudeangriffCheck = null;
        GD.Print(_entities.GebaeudeangriffCheckLine(arg));
        GetTree().Quit(0);
        return true;
    }
}
