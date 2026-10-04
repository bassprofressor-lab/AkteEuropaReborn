using System.Collections.Generic;
using System.Text;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐⭐ <b>PAKET 1 BEDIENUNG — die Prüfstände der Kartenseite</b> (04.10.2026,
/// KayelGee-Meldungen 2–4, <c>berichte/maus-befehle-fable.md</c>):
/// <list type="bullet">
/// <item><c>--bodenangriff-leer-probe</c> (bug-416): Strg-Angriff auf eine LEERE
/// Zelle in Reichweite — schiesst die Einheit, und bleibt der Auftrag stehen?
/// Nullmodell <c>--bodenangriff-zielpruefung</c>: Auftrag im ersten Takt zu Ende,
/// 0 Schüsse.</item>
/// <item><c>--bodenangriff-abbruch-probe</c> (bug-417): Zellangriff auf WALD, dann
/// ein Fahrbefehl 10 Zellen weg — ist das Bodenziel weg, fährt die Einheit, und
/// schiesst sie nicht mehr? Nullmodell <c>--bodenziel-haftet</c>.</item>
/// <item>Hilfen für <c>--formation-check</c> (bug-418, Lauf in
/// <c>Rendering/BedienungLauf.cs</c>).</item>
/// </list>
/// Alle gehen über die echten Absender (<see cref="PostAttackGround"/>,
/// <see cref="PostMove"/>) und den Befehlsring; die Eingriffe (Versetzen, Munition,
/// Gottmodus) sind als solche ausgeschrieben.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Wie viele Einheiten angewählt sind (Bodenauswahl).</summary>
    public int AuswahlZahl => _sel.Count;

    /// <summary>Skriptvariable v[n] der laufenden Mission, −1 ohne Skript.</summary>
    public int SkriptVar(int n) => _mscript?.Var(n) ?? -1;

    /// <summary>Für Prüfstände: das Missionsskript anlegen (wie
    /// <c>--untermission-check</c>), true wenn es eins gibt.</summary>
    public bool SkriptBereitFuerProbe() { MissionScriptTick(0.001f); return _mscript != null; }

    /// <summary>Für Prüfstände: v[n] setzen (EINGRIFF).</summary>
    public void SkriptVarFuerProbe(int n, int wert) => _mscript?.SetVarFuerProbe(n, wert);

    private void BedTakte(int n) { for (int t = 0; t < n; t++) SimTickFuerProbe(); }

    private static void BedRuhe(Entity e)
    {
        e.Target = -1; e.Ordered = false; e.Path = null; e.Orders.Clear(); e.AngriffsZelle = null;
    }

    /// <summary>Eine eigene, bewaffnete Landeinheit (Fahrzeug), die fährt.</summary>
    private List<int> BedSchuetzen()
    {
        var l = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer) continue;
            if (!CanFight(e) || e.DugIn || Untergestellt(e) || !e.Mobile) continue;
            if (e.Weapon == 0 || IsEquipmentMount(e.Weapon)) continue;
            if (e.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
            if (e.FuelMax > 0 && e.Fuel <= 0) continue;
            l.Add(i);
        }
        return l;
    }

    private float BedReichweite(Entity e) => e.Range > 0 ? e.Range : RangeOf(e);

    // ======================= bug-416 ============================================

    /// <summary><c>--bodenangriff-leer-probe</c>.</summary>
    public string BodenangriffLeerProbe()
    {
        var sb = new StringBuilder("bodenangriff-leer-probe (bug-416)\n");
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (BodenangriffZielpruefung)
            sb.AppendLine("  ⚠ NULLMODELL --bodenangriff-zielpruefung: hier MUSS der Auftrag sofort enden");
        int idx = -1; Vector2I? ziel = null;
        foreach (int k in BedSchuetzen())
        {
            var e = _entities[k];
            float r = BedReichweite(e), rmin = RangeMinOf(e);
            int rr = (int)r;
            for (int d = 2; d <= rr && ziel == null; d++)
                for (int dc = -d; dc <= d && ziel == null; dc++)
                    for (int dr = -d; dr <= d && ziel == null; dr++)
                    {
                        if (System.Math.Max(System.Math.Abs(dc), System.Math.Abs(dr)) != d) continue;
                        int c = e.Col + dc, w = e.Row + dr;
                        if (!_nav.InBounds(c, w) || ZelleHatZiel(c, w)) continue;
                        float ab = BodenZellAbstand(e, c, w);
                        if (ab > r || ab < rmin) continue;
                        ziel = new Vector2I(c, w);
                    }
            if (ziel != null) { idx = k; break; }
        }
        if (idx < 0 || ziel == null)
            return sb.Append("  keine eigene bewaffnete Landeinheit mit leerer Zelle in Reichweite — ungeprueft\n  DURCHGEFALLEN").ToString();
        var u = _entities[idx];
        BedRuhe(u);
        u.AmmoMax = System.Math.Max(u.AmmoMax, 999); u.Ammo = u.AmmoMax;
        CheatGodMode = true;
        sb.AppendLine($"  Schuetze {LabelOf(u)} Platz {u.Slot} auf ({u.Col},{u.Row}), Reichweite {BedReichweite(u):0.#}; "
                    + $"LEERE Zielzelle ({ziel.Value.X},{ziel.Value.Y}), Abstand {BodenZellAbstand(u, ziel.Value.X, ziel.Value.Y):0.##}, "
                    + $"ZelleHatZiel {ZelleHatZiel(ziel.Value.X, ziel.Value.Y)}");
        sb.AppendLine("  ⚠ EINGRIFF: Munition 999, Gottmodus (Gegner sollen die Messung nicht beenden)");
        _sel.Clear(); _sel.Add(idx); _selected = idx;
        int bef0 = BodenBefohlen, sch0 = BodenSchuesse, fer0 = BodenFertig;
        bool ab0 = PostAttackGround(ZellMitte(ziel.Value.X, ziel.Value.Y));
        BedTakte(250);
        int dBef = BodenBefohlen - bef0, dSch = BodenSchuesse - sch0, dFer = BodenFertig - fer0;
        bool aktiv = u.AngriffsZelle != null;
        CheatGodMode = false;
        sb.AppendLine($"  nach 250 Takten: abgesetzt {ab0}, befohlen +{dBef}, Schuesse +{dSch}, beendet +{dFer}, "
                    + $"Auftrag noch aktiv {aktiv}, Einheit auf ({u.Col},{u.Row})");
        sb.AppendLine("  ERWARTET (Original 0x407383/0x40FC90): Schuesse > 0, Auftrag aktiv, beendet 0");
        bool ok = dBef > 0 && dSch > 0 && aktiv && dFer == 0;
        if (BodenangriffZielpruefung)
            sb.AppendLine($"  Nullmodell: {(dFer > 0 && dSch == 0 ? "zeigt den alten Fehler (sofort beendet, 0 Schuesse)" : "zeigt ihn NICHT")}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    // ======================= bug-417 ============================================

    /// <summary><c>--bodenangriff-abbruch-probe</c>: Baum angreifen, dann fahren.</summary>
    public string BodenangriffAbbruchProbe()
    {
        var sb = new StringBuilder("bodenangriff-abbruch-probe (bug-417)\n");
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (BodenzielHaftet)
            sb.AppendLine("  ⚠ NULLMODELL --bodenziel-haftet: hier MUSS die Einheit stehen bleiben und weiterschiessen");
        int idx = -1; Vector2I wald = default, platz = default, weg = default;
        foreach (int k in BedSchuetzen())
        {
            var e = _entities[k];
            float r = BedReichweite(e), rmin = RangeMinOf(e);
            // die naechsten Waldzellen zu dieser Einheit
            var waelder = new List<Vector2I>();
            foreach (var o in _objDraw)
                if (o.IstWald && !o.Abgebrannt) waelder.Add(new Vector2I(o.Col, o.Row));
            waelder.Sort((a, b) => (System.Math.Abs(a.X - e.Col) + System.Math.Abs(a.Y - e.Row))
                                   .CompareTo(System.Math.Abs(b.X - e.Col) + System.Math.Abs(b.Y - e.Row)));
            for (int wi = 0; wi < waelder.Count && wi < 40 && idx < 0; wi++)
            {
                var wz = waelder[wi];
                // ein freier Standplatz 2..3 Zellen vom Wald, in Reichweite
                for (int d = 2; d <= 3 && idx < 0; d++)
                    for (int dc = -d; dc <= d && idx < 0; dc++)
                        for (int dr = -d; dr <= d && idx < 0; dr++)
                        {
                            if (System.Math.Max(System.Math.Abs(dc), System.Math.Abs(dr)) != d) continue;
                            var p = new Vector2I(wz.X + dc, wz.Y + dr);
                            if (!_nav.InBounds(p.X, p.Y) || !_nav.IsFree(p.X, p.Y, e.Move, k)) continue;
                            float ab = new Vector2(p.X - wz.X, p.Y - wz.Y).Length();
                            if (ab > r || ab < rmin) continue;
                            // ein Fahrziel 10 Zellen weiter, vom Wald weg, mit Weg
                            var dir = new Vector2I(System.Math.Sign(dc), System.Math.Sign(dr));
                            if (dir == Vector2I.Zero) continue;
                            var fern = _nav.NearestFree(new Vector2I(
                                Mathf.Clamp(p.X + dir.X * 10, 0, _nav.Width - 1),
                                Mathf.Clamp(p.Y + dir.Y * 10, 0, _nav.Height - 1)), e.Move, k);
                            if (fern == null) continue;
                            if (System.Math.Max(System.Math.Abs(fern.Value.X - p.X), System.Math.Abs(fern.Value.Y - p.Y)) < 7) continue;
                            var pf = _nav.FindPath(p, fern.Value, e.Move, k);
                            if (pf == null || pf.Count == 0) continue;
                            idx = k; wald = wz; platz = p; weg = fern.Value;
                        }
            }
            if (idx >= 0) break;
        }
        if (idx < 0)
            return sb.Append("  keine Einheit/Wald/Fahrziel-Lage gefunden — ungeprueft\n  DURCHGEFALLEN").ToString();
        var u = _entities[idx];
        ProbeVersetzen(idx, platz.X, platz.Y);
        BedRuhe(u);
        u.AmmoMax = System.Math.Max(u.AmmoMax, 999); u.Ammo = u.AmmoMax;
        CheatGodMode = true;
        sb.AppendLine($"  ⚠ EINGRIFF: {LabelOf(u)} Platz {u.Slot} nach ({platz.X},{platz.Y}), Munition 999, Gottmodus; "
                    + $"Wald ({wald.X},{wald.Y}), Fahrziel ({weg.X},{weg.Y})");
        _sel.Clear(); _sel.Add(idx); _selected = idx;
        int sch0 = BodenSchuesse;
        bool ab0 = PostAttackGround(ZellMitte(wald.X, wald.Y));
        BedTakte(120);
        int schVorher = BodenSchuesse - sch0;
        sb.AppendLine($"  Zellangriff auf den Wald abgesetzt {ab0}: nach 120 Takten {schVorher} Schuesse, Bodenziel {(u.AngriffsZelle?.ToString() ?? "-")}");
        var start = new Vector2I(u.Col, u.Row);
        int gel0 = BodenzielGeloescht;
        int n = PostMove(ZellMitte(weg.X, weg.Y));
        BedTakte(3);
        int sch1 = BodenSchuesse;
        BedTakte(500);
        int schDanach = BodenSchuesse - sch1;
        int gefahren = System.Math.Max(System.Math.Abs(u.Col - start.X), System.Math.Abs(u.Row - start.Y));
        bool weg0 = u.AngriffsZelle == null;
        CheatGodMode = false;
        sb.AppendLine($"  Fahrbefehl ({n} Satz) nach ({weg.X},{weg.Y}): nach 500 Takten auf ({u.Col},{u.Row}), "
                    + $"gefahren {gefahren} Zellen, Bodenziel {(u.AngriffsZelle?.ToString() ?? "-")}, "
                    + $"Schuesse seit dem Befehl +{schDanach}, AuftragLoeschen griff {BodenzielGeloescht - gel0}x");
        sb.AppendLine("  ERWARTET (fahre 0x40B070: UKOL := 2, +0x36 := 0xFFFF): Bodenziel weg, >= 5 Zellen gefahren, 0 Schuesse");
        bool ok = schVorher > 0 && n > 0 && weg0 && gefahren >= 5 && schDanach == 0;
        if (BodenzielHaftet)
            sb.AppendLine($"  Nullmodell: {(!weg0 && gefahren < 5 ? "zeigt den alten Fehler (Einheit bleibt, Ziel haftet)" : "zeigt ihn NICHT")}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    // ======================= bug-418 (Hilfen) ====================================

    private int[] _fmIdx = System.Array.Empty<int>();
    private Vector2I[] _fmStart = System.Array.Empty<Vector2I>();

    /// <summary><c>--formation-check</c>, Aufbau: drei eigene Fahrzeuge in
    /// L-Aufstellung (c,r), (c+2,r), (c,r+2) auf freiem Gelände, Ziel 15 Zellen
    /// rechts. Gibt die Klickzelle zurück oder null.</summary>
    public Vector2I? FormationAufbau(StringBuilder sb)
    {
        if (_nav == null) return null;
        var l = new List<int>();
        for (int i = 0; i < _entities.Count && l.Count < 3; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer || !e.Mobile || e.DugIn) continue;
            if (Untergestellt(e) || e.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
            if (e.FuelMax > 0 && e.Fuel <= 0) continue;
            l.Add(i);
        }
        if (l.Count < 3) { sb.AppendLine("  keine drei eigenen Fahrzeuge"); return null; }
        var a0 = _entities[l[0]];
        var mc = a0.Move;
        bool Frei(int c, int r) => _nav.InBounds(c, r) && _nav.IsFree(c, r, mc, -1);
        // Suchreihenfolge: nach Abstand zur ersten Einheit
        var kand = new List<Vector2I>();
        for (int r = 1; r < _nav.Height - 4; r++)
            for (int c = 1; c < _nav.Width - 19; c++)
                kand.Add(new Vector2I(c, r));
        kand.Sort((p, q) => (System.Math.Abs(p.X - a0.Col) + System.Math.Abs(p.Y - a0.Row))
                            .CompareTo(System.Math.Abs(q.X - a0.Col) + System.Math.Abs(q.Y - a0.Row)));
        int versuche = 0;
        foreach (var p in kand)
        {
            int c = p.X, r = p.Y;
            bool alle = true;
            // Start-L und das ganze Zielfeld 3x3 frei
            foreach (var z in new[] { new Vector2I(c, r), new Vector2I(c + 2, r), new Vector2I(c, r + 2) })
                if (!Frei(z.X, z.Y)) { alle = false; break; }
            if (!alle) continue;
            for (int dc = 15; dc <= 17 && alle; dc++)
                for (int dr = 0; dr <= 2 && alle; dr++)
                    if (!Frei(c + dc, r + dr)) alle = false;
            if (!alle) continue;
            if (++versuche > 30) break;
            var weg = _nav.FindPath(new Vector2I(c, r), new Vector2I(c + 15, r), mc, -1);
            if (weg == null || weg.Count == 0) continue;
            _fmIdx = l.ToArray();
            _fmStart = new[] { new Vector2I(c, r), new Vector2I(c + 2, r), new Vector2I(c, r + 2) };
            FormationZuruecksetzen();
            sb.AppendLine($"  Aufbau: Plaetze {_entities[l[0]].Slot}/{_entities[l[1]].Slot}/{_entities[l[2]].Slot} "
                        + $"auf ({c},{r}) ({c + 2},{r}) ({c},{r + 2}) [EINGRIFF: versetzt], Klick ({c + 15},{r})");
            return new Vector2I(c + 15, r);
        }
        sb.AppendLine("  kein freies Gelaende fuer L-Aufstellung + Zielfeld gefunden");
        return null;
    }

    /// <summary>Die drei Prüfeinheiten zurück auf ihre L-Plätze, ohne Auftrag,
    /// angewählt.</summary>
    public void FormationZuruecksetzen()
    {
        _sel.Clear();
        for (int k = 0; k < _fmIdx.Length; k++)
        {
            var e = _entities[_fmIdx[k]];
            BedRuhe(e);
            if (e.Reserved is { } rc) { _nav!.ClearOccupant(rc.X, rc.Y, _fmIdx[k]); e.Reserved = null; }
            ProbeVersetzen(_fmIdx[k], _fmStart[k].X, _fmStart[k].Y);
            e.Goal = new Vector2I(-1, -1);
            e.StepCost = 0; e.Progress = 0;
            _sel.Add(_fmIdx[k]);
        }
        _selected = _fmIdx.Length > 0 ? _fmIdx[0] : -1;
    }

    /// <summary>Die Fahrziele (<c>Entity.Goal</c>) und Warteschlangenlängen der
    /// drei Prüfeinheiten nach <paramref name="takte"/> Takten.</summary>
    public (Vector2I[] Ziele, int[] Reihe) FormationStand(int takte)
    {
        BedTakte(takte);
        var z = new Vector2I[_fmIdx.Length];
        var q = new int[_fmIdx.Length];
        for (int k = 0; k < _fmIdx.Length; k++) { z[k] = _entities[_fmIdx[k]].Goal; q[k] = _entities[_fmIdx[k]].Orders.Count; }
        return (z, q);
    }

    /// <summary>Für <c>--zeigerbank-check</c>: die erste eigene fahrende Einheit
    /// (oder −1).</summary>
    public int ErsteEigeneFahrende()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == ViewPlayer && e.Mobile) return i;
        }
        return -1;
    }

    /// <summary>Die Kartenmitte einer Zelle — der Klickpunkt eines Prüfstands.</summary>
    public Vector2 ZellMitteFuerProbe(int c, int r) => ZellMitte(c, r);

    /// <summary><c>--formation-check</c>: nur die erste Prüfeinheit bleibt gewählt.</summary>
    public void AuswahlNurErste()
    {
        _sel.Clear();
        if (_fmIdx.Length > 0) { _sel.Add(_fmIdx[0]); _selected = _fmIdx[0]; }
    }

    /// <summary>Für Prüfstände: genau diese Einheit anwählen (oder nichts bei −1).</summary>
    public void AuswahlFuerProbe(int i)
    {
        _sel.Clear(); _selAir = -1;
        if (i >= 0) { _sel.Add(i); _selected = i; } else _selected = -1;
    }
}
