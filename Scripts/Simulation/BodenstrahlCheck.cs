namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>--bodenstrahl-check</b> und der Bildlauf <b>--bodenstrahl-bild=&lt;png&gt;</b>
/// (29.10.2026, bug-658, KayelGee R8 K16) — siehe <see cref="BodenstrahlTakt"/>.
///
/// <para>Ein Laser-Fusssoldat (Waffe 191/194) und ein feindliches Fahrzeug werden auf
/// ebenen, freien Zellen in d = 2, 3, 4 Zellen waagerechtem Abstand gestellt; der Soldat
/// bekommt einen Angriffsbefehl, gemessen wird der ERSTE angekommene Strahl: seine Laenge
/// L (Muendung bis Bildpunkt des Ziels), die Punkte und die Takte bis zum Treffer.
/// Soll (0x454CF0 Bodenzweig): Punkte = ⌊L⌋ − 4, Takte = ⌈(L − 4)/20⌉. Nullmodell
/// <c>--bodenstrahl-alt</c>: Punkte = Takte − 1, Takte = ⌈L/40⌉ → DURCHGEFALLEN.</para>
/// <para>⚠ EINGRIFFE: gibt es keinen Laser-Soldaten, bekommt ein eigener Fusssoldat die
/// Waffe 191 (gesagt); TP 30000 fuer beide; das Ziel ist festgeheftet.</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _bsS = -1, _bsZ = -1;
    private Vector2I _bsZz;

    /// <summary>Den Schuetzen und das Ziel in <paramref name="d"/> Zellen Abstand stellen.
    /// Gibt die Bildlage des Schuetzen zurueck (fuer die Kamera), null = kein Aufbau.</summary>
    public Vector2? BodenstrahlAufbau(int d, System.Text.StringBuilder log)
    {
        if (_nav == null) { log.Append("bodenstrahl: KEIN URTEIL — keine Karte"); return null; }
        if (_bsS < 0)
        {
            for (int i = 0; i < _entities.Count && _bsS < 0; i++)
            {
                var e = _entities[i];
                if (!e.IsProp && !e.Dead && !Untergestellt(e) && FussStrahl(e)) _bsS = i;
            }
            if (_bsS < 0)
                for (int i = 0; i < _entities.Count && _bsS < 0; i++)
                {
                    var e = _entities[i];
                    if (e.IsProp || e.Dead || Untergestellt(e) || e.Infantry < 0 || e.Owner != ViewPlayer) continue;
                    log.AppendLine($"bodenstrahl: EINGRIFF — Fusssoldat Platz {e.Slot} bekommt Waffe {InfCompBase + 191} (Laser)");
                    e.Weapon = InfCompBase + 191;
                    _bsS = i;
                }
            if (_bsS < 0) { log.Append("bodenstrahl: KEIN URTEIL — kein Fusssoldat"); return null; }
            var s0 = _entities[_bsS];
            for (int pass = 0; pass < 2 && _bsZ < 0; pass++)
                for (int i = 0; i < _entities.Count && _bsZ < 0; i++)
                {
                    var e = _entities[i];
                    if (pass == 0 && e.GameUnitType != 0) continue;        // zuerst ein Landfahrzeug
                    if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Mobile && e.Infantry < 0 && !Untergestellt(e) && IsHostile(s0, e)) _bsZ = i;
                }
            if (_bsZ < 0) { log.Append("bodenstrahl: KEIN URTEIL — kein feindliches Fahrzeug"); return null; }
        }
        var S = _entities[_bsS]; var Z = _entities[_bsZ];
        S.HpMax = S.Hp = 30000; Z.HpMax = Z.Hp = 30000;
        // ebene, freie Zeile: Schuetze auf c, Ziel auf c + d
        for (int r = 6; r < _nav.Height - 6; r++)
            for (int c = 6; c < _nav.Width - 12; c++)
            {
                bool ok = true;
                for (int k = 0; k <= 6 && ok; k++)
                    ok = _nav.CanEnter(c + k, r, Simulation.NavGrid.MoveClass.Vehicle) && ElevOf(c + k, r) == ElevOf(c, r)
                         && (_nav.OccupantAt(c + k, r) < 0 || _nav.OccupantAt(c + k, r) == _bsS || _nav.OccupantAt(c + k, r) == _bsZ);
                if (!ok) continue;
                S.Path = null; S.Orders.Clear(); Z.Path = null; Z.Orders.Clear(); Z.Target = -1;
                ProbeVersetzen(_bsS, c, r);
                _bsZz = new Vector2I(c + d, r);
                ProbeVersetzen(_bsZ, _bsZz.X, _bsZz.Y);
                S.Target = _bsZ; S.Ordered = true; S.Cooldown = 0;
                S.Facing = S.AimFacing = DirToFacing(Z.Pos - S.Pos);
                log.AppendLine($"bodenstrahl: Schuetze Platz {S.Slot} ({LabelOf(S)}, Waffe {S.Weapon}, Reichweite {RangeOf(S):0.0}) auf ({c},{r}), "
                             + $"Ziel Platz {Z.Slot} ({LabelOf(Z)}) auf ({_bsZz.X},{_bsZz.Y}), {d} Zellen");
                return S.Pos;
            }
        log.Append("bodenstrahl: KEIN URTEIL — keine ebene freie Zeile");
        return null;
    }

    /// <summary>Je Takt (Bildlauf und Pruefstand): das Ziel festheften.</summary>
    private void BsHeften()
    {
        if (_bsZ < 0) return;
        var Z = _entities[_bsZ];
        if (Z.Col != _bsZz.X || Z.Row != _bsZz.Y) ProbeVersetzen(_bsZ, _bsZz.X, _bsZz.Y);
        Z.Path = null; Z.Cooldown = 99f;
    }

    public string BodenstrahlCheck()
    {
        var sb = new System.Text.StringBuilder("bodenstrahl-check"
            + (BodenstrahlAlt ? " — NULLMODELL --bodenstrahl-alt" : "") + "\n");
        bool ok = true; int gemessen = 0;
        foreach (int d in new[] { 2, 3, 4 })
        {
            var log = new System.Text.StringBuilder();
            if (BodenstrahlAufbau(d, log) == null) return sb.Append(log).ToString();
            sb.Append("  ").AppendLine(log.ToString().TrimEnd().Replace("\n", "\n  "));
            if (RangeOf(_entities[_bsS]) < d) { sb.AppendLine($"  d {d}: ausser Reichweite, entfaellt"); continue; }
            int an0 = BsAngekommen;
            for (int t = 0; t < 400 && BsAngekommen == an0; t++) { BsHeften(); SimTickFuerProbe(); }
            if (BsAngekommen == an0) { sb.AppendLine($"  d {d}: kein Strahl angekommen (400 Takte) — {_entities[_bsS].Schussgrund}"); ok = false; continue; }
            gemessen++;
            var (L, p, tk) = BsLetzter;
            int sollP = Mathf.Max(0, (int)L - BodenZuschlag), sollT = Mathf.Max(1, Mathf.CeilToInt((L - BodenZuschlag) / BodenSchritt));
            int altT = Mathf.CeilToInt(L / 40f), altP = altT - 1;
            bool tr = p == sollP && tk == sollT;
            ok &= tr;
            sb.AppendLine($"  d {d}: L {L:0.0} px — Punkte {p}, Takte bis Treffer {tk}; Soll {sollP} / {sollT} (0x454CF0), "
                        + $"Nullmodell {altP} / {altT} -> {(tr ? "ja" : "NEIN")}");
        }
        sb.AppendLine("  " + BodenstrahlLine());
        sb.Append(gemessen == 0 ? "  KEIN URTEIL" : ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Fuer den Bildlauf: ist ein Bodenstrahl im dritten Takt unterwegs?</summary>
    public bool BodenstrahlBildBereit()
    {
        BsHeften();
        foreach (var s in _strahlen) if (s.Boden && s.Takte >= 3) return true;
        return false;
    }

    public string BodenstrahlBildText()
    {
        foreach (var s in _strahlen)
            if (s.Boden)
                return $"Strahl L {s.Laenge:0.0} px, Takt {s.Takte}, Punkte bisher {s.Punkte}, Effekte {_effects.Count}"
                     + (BodenstrahlAlt ? ", NULLMODELL --bodenstrahl-alt" : "");
        return "kein Strahl";
    }

    /// <summary>Die Zeile fuer jeden Lauf (beim Ausstieg).</summary>
    public string BodenstrahlLine()
        => BsAngekommen == 0
           ? "bodenstrahl: kein Fussvolk-Laser angekommen — dieser Lauf sagt darueber NICHTS"
           : $"bodenstrahl{(BodenstrahlAlt ? " (NULLMODELL --bodenstrahl-alt)" : "")}: {BsAngekommen} angekommen, "
           + $"Punkte je Strahl {(float)BsPunkteSumme / BsAngekommen:0.0}, Takte je Strahl {(float)BsTakteSumme / BsAngekommen:0.0}";
}
