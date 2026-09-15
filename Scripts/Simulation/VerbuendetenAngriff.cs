namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>Strg auf Verbuendete, und Strg nimmt nicht mehr ein</b> (15.09.2026, seine
/// Entscheidungen zu <c>berichte/verbuendete-fable.md</c> §4.2 und §8.3).
///
/// <para><b>a. Strg nimmt nicht mehr ein.</b> Seine Worte: »da wir ja jetzt gegnerische
/// Gebaeude korrekt einnehmen koennen, braucht es den Strg fuer Gebaeudeeinnahme nicht
/// mehr, erst recht wenn er im Original sowieso nicht existiert«. Die Geste stammte vom
/// 17.08.2026 (Fehler C9/C11, <c>PostCapture</c>) und war als UNSERE Zutat vermerkt.
/// Im Original setzt Strg mit Auswahl Zeigerart 2 OHNE Zielpruefung (<c>@0x43202E</c>),
/// der Klickarm 2 (<c>0x437417</c>) ist der Angriff. Eingenommen wird ueber den
/// Einnahmezeiger (gewoehnlicher Rechtsklick) oder durch Draufstellen.
/// Gegenschalter <c>--strg-einnahme-alt</c>.</para>
///
/// <para><b>b. Strg greift Verbuendete an, wie im Original.</b> Die Zieluebersetzung
/// <c>0x4353F0</c> und der Behandler fragen kein Buendnis; die Buendnisfrage sitzt
/// allein im Klickarm 2: ein <b>einzeln gewaehlter Fusssoldat</b> gegen eine verbuendete
/// EINHEIT tut nichts (C <c>0x4374DD</c> / F <c>0x43663D</c>). Fahrzeuge und Gruppen
/// duerfen. Das Geschoss fliegt ueber Verbuendete hinweg, ausser der Schuetze fuehrt
/// den befohlenen Angriff genau auf diese Einheit (<c>UKOL 4 + UTOK_NA</c>, C
/// <c>0x45294F</c>); die Infanterieuhr kennt diese Ausnahme nicht (C <c>0x40F208</c> /
/// F <c>0x40F03A</c>). Der gewoehnliche Rechtsklick auf einen Verbuendeten bleibt die
/// Fahrt (Zeigerart 3). Gegenschalter <c>--kein-angriff-auf-verbuendete</c>.</para>
///
/// <para>⚠ UNSERE Setzungen: (1) ein Fusssoldat, der in einer Gruppe ein verbuendetes
/// Ziel bekommt, LAESST ES FALLEN — ob die Infanterieuhr es haelt oder verwirft, ist
/// nicht gelesen, nur dass sie nicht schiesst. (2) Der Selbstverteidiger des Originals
/// (<c>0x411770</c>, nach 20 Takten, ohne Buendnisfrage) ist nicht gebaut: ein
/// beschossener Verbuendeter schiesst bei uns nicht zurueck (<c>ApplyHit</c> fragt
/// <c>IsHostile</c>). (3) Eigene Einheiten bleiben unangreifbar; das Original prueft
/// dort ebenfalls nichts, gefragt war nur nach Verbuendeten.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--kein-angriff-auf-verbuendete</c> — der Stand bis 15.09.2026: ein
    /// Verbuendeter ist auch mit Strg kein Ziel.</summary>
    public static bool KeinAngriffAufVerbuendete;

    /// <summary><c>--strg-einnahme-alt</c> — der Stand vom 17.08. bis 15.09.2026:
    /// Strg+Rechtsklick auf ein fremdes Gebaeude schickt zur Tuer.</summary>
    public static bool StrgEinnahmeAlt;

    /// <summary>Wie oft ein einzelner Fusssoldat mit Strg auf einen Verbuendeten nichts tat.</summary>
    public int StrgFusssoldatVerweigert;

    /// <summary>Wie oft ein Fusssoldat ein verbuendetes Ziel fallen liess (Infanterieuhr).</summary>
    public int FussVerbuendetFallengelassen;

    /// <summary>Gehoert <paramref name="b"/> einem ANDEREN, verbuendeten Spieler als <paramref name="a"/>?</summary>
    private bool VerbuendetesZiel(Entity a, Entity b)
        => a.Owner is >= 0 and <= 7 && b.Owner is >= 0 and <= 7
           && a.Owner != b.Owner && Allied(a.Owner, b.Owner);

    /// <summary>Steht unter dem Zeiger etwas eines verbuendeten Spielers? Dort ist der
    /// gewoehnliche Rechtsklick die Fahrt (Zeigerart 3), kein Angriff.</summary>
    public bool VerbuendeterHier(Vector2 mapPos)
    {
        if (KeinAngriffAufVerbuendete) return false;       // IstAngriffsziel weist ohnehin ab
        int hit = Pick(mapPos);
        if (hit < 0) return false;
        var x = _entities[hit];
        return !x.IsProp && !x.Dead && x.Owner is >= 0 and <= 7
               && x.Owner != ViewPlayer && Allied(ViewPlayer, x.Owner);
    }

    /// <summary>Klickarm 2, C <c>0x4374DD</c>: genau ein Fusssoldat gewaehlt, unter dem
    /// Zeiger eine verbuendete EINHEIT — der Klick tut nichts.</summary>
    public bool StrgAngriffVerweigert(Vector2 mapPos)
    {
        if (KeinAngriffAufVerbuendete || _sel.Count != 1) return false;
        int si = -1;
        foreach (int i in _sel) si = i;
        if (si < 0 || si >= _entities.Count) return false;
        var s = _entities[si];
        if (s.IsBuilding || s.Infantry < 0) return false;
        int hit = Pick(mapPos);
        if (hit < 0) return false;
        var x = _entities[hit];
        return !x.IsBuilding && !x.IsProp && !x.Dead && VerbuendetesZiel(s, x);
    }

    /// <summary>Strg+Rechtsklick — der Arm der Zeigerart 2. Aufgerufen aus MapViewer und
    /// aus dem Pruefstand, damit beide denselben Weg gehen.</summary>
    public void StrgRechtsklick(Vector2 mapPos, bool queue)
    {
        if (StrgAngriffVerweigert(mapPos))
        {
            StrgFusssoldatVerweigert++;
            _order = "Ein einzelner Fusssoldat greift keinen Verbuendeten an.";
            UpdatePanel();
            return;
        }
        if (StrgEinnahmeAlt && PostCapture(mapPos, queue)) return;
        if (!PostAttack(mapPos, queue) && !PostAttackGround(mapPos, queue))
            PostMove(mapPos, queue);
    }

    // ---- --verbuendeten-angriff-check -------------------------------------------

    /// <summary>
    /// <c>--verbuendeten-angriff-check</c>, Kampagne 14 (Spieler 3 verbuendet), OHNE
    /// <c>--fog</c>. Vier Aussagen, alle ueber <see cref="StrgRechtsklick"/> und den
    /// Behandler (Busbefehl 11, Takte):
    /// 1. Fahrzeug + Strg auf verbuendete Einheit: Ziel gesetzt, Treffer (TP sinken).
    /// 2. Einzelner Fusssoldat + Strg auf dieselbe: nichts; in der Gruppe mit dem
    ///    Fahrzeug geht der Befehl durch (Gegenprobe, dass die Sperre nur ihn trifft).
    /// 3. Der gewoehnliche Rechtsklick sieht den Verbuendeten (kein Angriff), den Feind nicht.
    /// 4. Fahrzeug + Strg auf ein einnehmbares feindliches Gebaeude: ANGRIFF, keine Fahrt zur Tuer.
    /// Nullmodelle: <c>--kein-angriff-auf-verbuendete</c> faellt an 1 durch,
    /// <c>--strg-einnahme-alt</c> an 4. ⚠ EINGRIFFE: zwei eigene Einheiten werden versetzt.
    /// </summary>
    public string VerbuendetenAngriffCheck()
    {
        var sb = new System.Text.StringBuilder("verbuendeten-angriff-check\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        void Takte(int n) { for (int t = 0; t < n; t++) SimTickFuerProbe(); }
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (KeinAngriffAufVerbuendete) sb.AppendLine("  ⚠ NULLMODELL --kein-angriff-auf-verbuendete: Aussage 1 MUSS scheitern");
        if (StrgEinnahmeAlt) sb.AppendLine("  ⚠ NULLMODELL --strg-einnahme-alt: Aussage 4 MUSS scheitern");
        Schussgruende = true;
        // ein kopfloser Lauf hat nichts erkundet — wie --gebaeudeklick-check
        bool nebelVor = PickOhneNebel;
        PickOhneNebel = true;

        // ---- die Beteiligten ----------------------------------------------------
        int ai = -1, fi = -1, vi = -1, gi = -1;
        var fahrzeuge = new System.Collections.Generic.List<int>();
        var fuss = new System.Collections.Generic.List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.HpMax <= 0 || Untergestellt(e)) continue;
            bool fremdVerb = e.Owner is >= 0 and <= 7 && e.Owner != ViewPlayer
                             && !IsNeutralPlayer(e.Owner) && Allied(ViewPlayer, e.Owner);
            if (!e.IsBuilding && fremdVerb && e.Infantry < 0 && ai < 0) ai = i;
            if (e.Owner != ViewPlayer || e.IsBuilding || !e.Mobile || !CanFight(e)) continue;
            (e.Infantry >= 0 ? fuss : fahrzeuge).Add(i);
        }
        if (fahrzeuge.Count > 0) vi = fahrzeuge[0];
        if (fuss.Count > 0) fi = fuss[0];
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead || b.Owner is < 0 or > 7) continue;
            if (b.Owner == ViewPlayer || Allied(ViewPlayer, b.Owner) || !Capturable(b)) continue;
            gi = i; break;
        }
        if (ai < 0 || vi < 0 || fi < 0)
            return sb.Append($"  Beteiligte fehlen (verbuendete Einheit {ai}, eigenes Fahrzeug {vi}, eigener Fusssoldat {fi}) — ungeprueft\n  DURCHGEFALLEN").ToString();
        var a = _entities[ai];

        Vector2I? Frei(Entity nahe, int r0, int r1, int idx)
        {
            for (int r = r0; r <= r1; r++)
                for (int dc = -r; dc <= r; dc++)
                    for (int dr = -r; dr <= r; dr++)
                    {
                        if (System.Math.Max(System.Math.Abs(dc), System.Math.Abs(dr)) != r) continue;
                        int c = nahe.Col + dc, z = nahe.Row + dr;
                        if (_nav.InBounds(c, z) && _nav.IsFree(c, z, _entities[idx].Move, idx)) return new Vector2I(c, z);
                    }
            return null;
        }
        string Treffer(Vector2 pos)
        {
            int h = Pick(pos);
            if (h < 0) return "Pick nichts";
            var x = _entities[h];
            return $"Pick {h} (Platz {x.Slot}, Sp {x.Owner}, {(x.IsProp ? "Kulisse" : x.IsBuilding ? "Gebaeude" : x.Infantry >= 0 ? "Fuss" : "Fahrzeug")})";
        }
        void Ruhe(Entity e) { e.Target = -1; e.Ordered = false; e.Path = null; e.Orders.Clear(); e.AngriffsZelle = null; }

        // das erste Fahrzeug/der erste Soldat, der neben dem Verbuendeten Platz findet
        // (ein Schiff findet an Land keinen)
        Vector2I? pv = null, pf = null;
        foreach (int k in fahrzeuge) { pv = Frei(a, 2, 4, k); if (pv != null) { vi = k; break; } }
        foreach (int k in fuss) { pf = Frei(a, 3, 6, k); if (pf != null) { fi = k; break; } }
        if (pv == null || pf == null) return sb.Append("  kein freier Platz neben dem Verbuendeten — ungeprueft\n  DURCHGEFALLEN").ToString();
        var v = _entities[vi]; var f = _entities[fi];
        sb.AppendLine($"  verbuendet: Platz {a.Slot} Spieler {a.Owner} auf ({a.Col},{a.Row}) TP {a.Hp}/{a.HpMax}; "
                    + $"Fahrzeug Platz {v.Slot} Waffe {v.Weapon}; Fusssoldat Platz {f.Slot}");
        ProbeVersetzen(vi, pv.Value.X, pv.Value.Y); Ruhe(v);
        ProbeVersetzen(fi, pf.Value.X, pf.Value.Y); Ruhe(f);
        sb.AppendLine($"  ⚠ EINGRIFF: Fahrzeug nach ({pv.Value.X},{pv.Value.Y}), Fusssoldat nach ({pf.Value.X},{pf.Value.Y})");

        // ---- 2. einzelner Fusssoldat: nichts --------------------------------------
        _sel.Clear(); _sel.Add(fi); _selected = fi;
        int verw0 = StrgFusssoldatVerweigert;
        sb.AppendLine($"  (vor dem Klick: Auswahl {_sel.Count}, Fuss {f.Infantry}, CanFight {CanFight(f)}, IstAngriffsziel {IstAngriffsziel(f, a)}, "
                    + $"{Treffer(a.Pos)}, verweigert {StrgAngriffVerweigert(a.Pos)})");
        StrgRechtsklick(a.Pos, false);
        Takte(20);
        bool nichts = f.Target < 0 && f.AngriffsZelle == null && f.Path == null;
        Soll(KeinAngriffAufVerbuendete || (StrgFusssoldatVerweigert == verw0 + 1 && nichts),
             $"einzelner Fusssoldat, Strg auf die verbuendete Einheit ({Treffer(a.Pos)}, soll {ai}): verweigert {StrgFusssoldatVerweigert - verw0}, "
             + $"Ziel {f.Target}, Bodenziel {(f.AngriffsZelle?.ToString() ?? "-")}, Weg {(f.Path != null ? "ja" : "nein")}");

        // ---- 3. der gewoehnliche Rechtsklick --------------------------------------
        int ei = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && e.Owner is >= 0 and <= 7 && e.Owner != ViewPlayer
                && !Allied(ViewPlayer, e.Owner) && !Untergestellt(e) && Pick(e.IsBuilding ? BodyRect(e).GetCenter() : e.Pos) == i)
            { ei = i; break; }
        }
        bool vHier = VerbuendeterHier(a.Pos);
        bool fHier = ei >= 0 && VerbuendeterHier(_entities[ei].IsBuilding ? BodyRect(_entities[ei]).GetCenter() : _entities[ei].Pos);
        Soll(KeinAngriffAufVerbuendete || (vHier && ei >= 0 && !fHier),
             $"gewoehnlicher Rechtsklick: ueber dem Verbuendeten Fahrt {vHier}, ueber Feind Platz {(ei >= 0 ? _entities[ei].Slot : -1)} Fahrt {fHier}");

        // ---- 1. Fahrzeug + Strg: Ziel und Treffer ---------------------------------
        Ruhe(f);
        _sel.Clear(); _sel.Add(vi); _selected = vi;
        int hp0 = a.Hp;
        StrgRechtsklick(a.Pos, false);
        int t1 = 0;
        while (t1 < 60 && v.Target != ai) { Takte(1); t1++; }
        bool zielGesetzt = v.Target == ai;
        int t2 = 0;
        while (zielGesetzt && t2 < 1500 && a.Hp >= hp0 && !a.Dead) { Takte(1); t2++; }
        bool getroffen = a.Hp < hp0 || a.Dead;
        Soll(zielGesetzt, $"Fahrzeug, Strg auf die verbuendete Einheit: Ziel {v.Target} nach {t1} Takten, »{AngriffAbgewiesen}«, »{_order}«");
        Soll(getroffen, $"  ... und trifft: TP {hp0} -> {a.Hp} nach {t2} Takten (Schussgrund »{v.Schussgrund}«)");

        // ---- 2b. Gegenprobe: in der Gruppe geht der Befehl durch ------------------
        if (!a.Dead)
        {
            Ruhe(v); Ruhe(f);
            _sel.Clear(); _sel.Add(vi); _sel.Add(fi); _selected = vi;
            int verw1 = StrgFusssoldatVerweigert, fall0 = FussVerbuendetFallengelassen;
            StrgRechtsklick(a.Pos, false);
            int t3 = 0;
            while (t3 < 60 && v.Target != ai) { Takte(1); t3++; }
            Takte(40);
            Soll(KeinAngriffAufVerbuendete || (StrgFusssoldatVerweigert == verw1 && v.Target == ai),
                 $"Gruppe Fahrzeug + Fusssoldat, Strg: nicht verweigert ({StrgFusssoldatVerweigert - verw1}), Fahrzeugziel {v.Target}; "
                 + $"Fusssoldat liess das Ziel fallen {FussVerbuendetFallengelassen - fall0}x (Infanterieuhr)");
        }
        else sb.AppendLine("  (Gegenprobe Gruppe entfaellt: der Verbuendete ist schon zerstoert)");

        // ---- 4. Strg auf ein feindliches Gebaeude: Angriff, keine Einnahme --------
        if (gi < 0) sb.AppendLine("  ⚠ kein einnehmbares feindliches Gebaeude — Aussage 4 ungeprueft");
        else
        {
            var g = _entities[gi];
            Ruhe(v); Ruhe(f);
            _sel.Clear(); _sel.Add(vi); _selected = vi;
            var mitte = BodyRect(g).GetCenter();
            StrgRechtsklick(mitte, false);
            int t4 = 0;
            while (t4 < 60 && v.Target != gi && v.AngriffsZelle == null) { Takte(1); t4++; }
            Soll(v.Target == gi,
                 $"Fahrzeug, Strg auf {BuildingName(g)} Platz {g.Slot} (Spieler {g.Owner}, {Treffer(mitte)}, soll {gi}): Angriffsziel {v.Target} (soll {gi}), "
                 + $"Weg {(v.Path != null ? $"nach ({v.Goal.X},{v.Goal.Y})" : "keiner")}, »{_order}«");
        }

        _sel.Clear(); _selected = -1;
        PickOhneNebel = nebelVor;
        sb.AppendLine($"  --kein-angriff-auf-verbuendete {KeinAngriffAufVerbuendete}, --strg-einnahme-alt {StrgEinnahmeAlt}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
