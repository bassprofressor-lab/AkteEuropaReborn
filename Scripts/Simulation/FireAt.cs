namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>fire_at IST EIN SCHUSS</b> — und ein Zellgeschoss trifft, was auf der
/// Zelle steht (gebaut 11.09.2026 nach <c>berichte/fireat-buendnis.md</c>).
///
/// <para><b>Die Kette im Original:</b> <c>0x4D0AD0(einheit, x, y)</c> schiebt
/// 65000 und ruft <c>0x40C8C0</c>: ZBRAN 0 → nichts, 9 → Gaswerfer
/// (<c>0x40104B</c>), 0x12 → nichts, sonst die Schussroutine
/// <c>0x40BB00(einheit, x, y, 65000)</c>:</para>
/// <code>
///   0x40BB2E  Tuerzelle (Lage 99) der Schuetzenzelle -> kein Schuss
///   0x40BB44  Munition +0x39 == 0                    -> kein Schuss
///   0x40BC78  NABYTO +0x32 != 0 (laedt nach)         -> kein Schuss
///   0x40BD57  Zielpunkt = Zellmitte (20/10), Hoehe·15
///   0x40BF64  Reichweite +0x2B·40 < d  oder  +0x2A·40 > d -> kein Schuss
///   0x40BFBB  Zwilling; 0x40C288 Streuung je Geschoss; 0x451B40 anlegen
///             (Art 12 trifft sofort @0x451CF7)
///   0x40C451  Nachladen setzen; 0x40C575 Klang; 0x40C57D Munition −1 (ZBRAN 8 nicht)
/// </code>
///
/// <para><b>Der Einschlag</b> (Geschosstakt <c>0x452190</c>) ruft Zasah fuer das
/// Zellwort, und NUR hier sitzt die Buendnisfrage (@0x452944): ein
/// verbuendetes Fahrzeug — das Geschoss fliegt durch; eine Infanteriezelle mit
/// lauter Verbuendeten (<c>pratelska_infa</c> @0x452B9C) — durch, sonst Zasah,
/// und der spart verbuendete Maenner einzeln aus (@0x40D084); Gebaeude —
/// ohne Frage. Ausnahmen: Art 7 (Flaechentreffer) und der befohlene Angriff auf
/// genau diese Einheit.</para>
///
/// <para>⚠ UNSERE SETZUNGEN, benannt: der Gaswerfer (ZBRAN 9, 0x40104B) ist
/// ungelesen und nimmt die Schussroutine; das Nachladen setzt
/// <c>ReloadOf</c> wie jeder unserer Schuesse (ohne das <c>rand&amp;3</c>
/// @0x40C451); der Flaechentreffer der Art 7 (0x454510) ist nicht gebaut.</para>
///
/// <para>Gegenschalter: <c>--fireat-sofort</c> (Sofort-Treffer auf alle Einheiten
/// der Zelle, der Stand vom Vormittag), <c>--zellgeschoss-ohne-einheit</c> (ein
/// Zellgeschoss trifft keine Einheit, der Stand vor dem 11.09.2026).
/// Pruefstand <c>--fireat-check</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--fireat-sofort</c></summary>
    public static bool FireAtSofort;

    /// <summary><c>--zellgeschoss-ohne-einheit</c></summary>
    public static bool ZellEinschlagAlt;

    /// <summary>Warum der letzte fire_at nicht schoss.</summary>
    public string FireAtGrund = "";

    public int FireAtSchuesse, FireAtVerweigert;

    /// <summary>fire_at fuer den Listenplatz <paramref name="si"/> — derselbe
    /// Einstieg fuer das Missionsskript und den Pruefstand.</summary>
    private bool FireAtAusfuehren(int si, int col, int row)
    {
        if (si < 0 || si >= _entities.Count) { FireAtGrund = "kein Schuetze"; return false; }
        var s = _entities[si];
        if (FireAtSofort)
        {
            // der Stand vom Vormittag, WORTGLEICH in der Wirkung
            if (s.Ammo == 0 && s.AmmoMax > 0) { FireAtGrund = "keine Munition"; return false; }
            ApplyMissionHits(new[] { (col, row) }, schuetze: si);
            if (s.AmmoMax > 0) s.Ammo = Mathf.Max(0, s.Ammo - 1);
            return true;
        }
        return FireAtSchuss(si, col, row);
    }

    private bool FireAtSchuss(int si, int col, int row)
    {
        var s = _entities[si];
        bool Nein(string grund) { FireAtGrund = grund; FireAtVerweigert++; return false; }

        // 0x40C8C0 — die Weiche nach ZBRAN (Turmbauteil − 20)
        if (s.Weapon <= 0) return Nein("keine Waffe (ZBRAN 0, 0x40C8C0)");
        int zbran = s.Weapon is >= 20 and < 70 ? s.Weapon - 20 : -1;
        if (zbran == 0x12) return Nein("ZBRAN 0x12 — 0x40C8C0 tut nichts");

        // 0x40BB2E — aus der Tuerzelle wird nicht geschossen
        var hier = CellCenter(s.Col, s.Row);
        foreach (var b in _entities)
            if (b.IsBuilding && !b.IsProp && !b.Dead && AufDerTuerzelle(b, hier))
                return Nein("Tuerzelle (Lage 99, @0x40BB2E)");
        if (!HasAmmo(s)) return Nein("keine Munition (@0x40BB44)");
        if (s.Cooldown > 0) return Nein("laedt nach (NABYTO, @0x40BC78)");

        // 0x40BF64 — Reichweite in Bildpunkten
        var mitte = ZellMitte(col, row);
        float d = s.Pos.DistanceTo(mitte);
        int weit = s.Range > 0 ? s.Range : Mathf.RoundToInt(RangeOf(s));
        if (weit * 40 < d) return Nein($"zu weit ({d:0} px > {weit}·40, @0x40BF64)");
        if (s.RangeMin * 40 > d) return Nein($"zu nah ({d:0} px < {s.RangeMin}·40)");

        s.Cooldown = ReloadOf(s);                                          // 0x40C451
        if (s.AmmoMax > 0 && zbran != 8 && !(CheatAmmo && Cheated(s))) s.Ammo--;   // 0x40C57D
        DebugShots++;
        FireAtSchuesse++;
        FireAtGrund = "";
        BodenSchuss(si, s, new Vector2I(col, row), mitte, WeaponOf(s.Weapon).Damage);
        return true;
    }

    /// <summary>
    /// Was ein Geschoss auf der Einschlagzelle trifft — die Tafel aus 1.3 des
    /// Berichts. <paramref name="art"/> 12 (Flamme, sofort @0x451CF7) und 7
    /// (Flaechentreffer) fragen das Buendnis nicht.
    /// </summary>
    private void ZellEinschlag(int si, int c, int r, int schaden, int art)
    {
        if (_nav == null || c < 0 || r < 0 || c >= _nav.Width || r >= _nav.Height) return;
        var sch = si >= 0 && si < _entities.Count ? _entities[si] : null;
        bool fragen = art != 7 && art != 12 && sch != null;

        // Fussvolk der Zelle
        var maenner = new List<int>();
        for (int k = 0; k < _entities.Count; k++)
        {
            var m = _entities[k];
            if (!m.Dead && !m.IsProp && !m.IsBuilding && m.Infantry >= 0 && m.Col == c && m.Row == r)
                maenner.Add(k);
        }
        if (maenner.Count > 0)
        {
            bool alleVerbuendet = sch != null && maenner.TrueForAll(k => Allied(sch.Owner, _entities[k].Owner));
            if (!(fragen && alleVerbuendet))                                  // @0x452B9C: durchfliegen
                foreach (int k in maenner)
                {
                    var m = _entities[k];
                    if (sch != null && Allied(sch.Owner, m.Owner)) continue;     // @0x40D084
                    ApplyHit(si, k, m, schaden);
                }
        }

        // ein Fahrzeug der Belegung
        int occ = _nav.OccupantAt(c, r);
        if (occ >= 0 && occ != si && occ < _entities.Count)
        {
            var o = _entities[occ];
            if (!o.Dead && !o.IsProp && !o.IsBuilding && o.Infantry < 0
                && !(fragen && !IsHostile(sch!, o)))                           // @0x452944: durchfliegen
                ApplyHit(si, occ, o, schaden);
        }

        // ein Gebaeude — ohne Frage (0x452D1E..0x452DED)
        if (GebaeudeAufZelle(c, r) is var bi and >= 0)
            ApplyHit(si, bi, _entities[bi], schaden);
    }

    // ================= der Pruefstand ==========================================

    private bool _fireAtCheckAn;
    private readonly List<(int Schuetze, int Opfer)> _fireAtTreffer = new();
    private readonly Dictionary<int, int> _fireAtEinschlag = new();

    private sealed class FireAtFall
    {
        public char Name;
        public string Titel = "", Notiz = "", Grund = "";
        public int Schuetze = -1, Opfer = -1, Takt;
        public bool Geschossen;
    }

    private readonly List<FireAtFall> _faFaelle = new();

    /// <summary>
    /// <c>--fireat-check</c> — vier Faelle, jeder auf einem freien Streifen:
    /// F Schuetze → Zelle mit FEINDLICHEM Fahrzeug (Soll: Geschoss, Einschlag nach
    /// n &gt; 0 Takten, getroffen); V → Zelle mit VERBUENDETEM Fahrzeug (Soll:
    /// Geschoss, Einschlag, NICHT getroffen); N derselbe Schuetze wie F sofort
    /// noch einmal (Soll: »laedt nach«); W eine Zelle jenseits der Reichweite
    /// (Soll: »zu weit«).
    /// <para>⚠ EINGRIFFE: Versetzen, Besitzer setzen, Opfer ohne Waffe; nach dem
    /// Schuss wird das Nachladen des Schuetzen auf 9999 gestellt, damit er nicht
    /// regulaer weiterschiesst. Nullmodell <c>--fireat-sofort</c>.</para>
    /// </summary>
    public void FireAtCheckStart()
    {
        if (_nav == null) return;
        _fireAtCheckAn = true;
        var benutzt = new HashSet<int>();
        int feind = -1;
        for (int p = 0; p <= 7 && feind < 0; p++)
            if (!Allied(ViewPlayer, p)) feind = p;

        bool Frei(int k)
        {
            var x = _entities[k];
            return !benutzt.Contains(k) && !x.Dead && !x.IsProp && !x.IsBuilding && x.Mobile
                   && x.Ukol < 50 && !Untergestellt(x) && x.Infantry < 0 && x.GameUnitType == 0;
        }
        int Schuetze()
        {
            for (int k = 0; k < _entities.Count; k++)
            {
                if (!Frei(k)) continue;
                var x = _entities[k];
                if (x.Weapon <= 0 || x.Attack <= 0) continue;
                int art = Simulation.DesignMath.SoundClass(WeaponRowOf(x.Weapon));
                if (FlightKind(art) == null || art is 7 or 12) continue;   // ein echtes Flug-Geschoss
                return k;
            }
            return -1;
        }
        int Fahrzeug()
        {
            for (int k = 0; k < _entities.Count; k++) if (Frei(k)) return k;
            return -1;
        }

        FireAtFall Fall(char name, string titel, bool mitOpfer, bool opferFeind)
        {
            var f = new FireAtFall { Name = name, Titel = titel };
            _faFaelle.Add(f);
            f.Schuetze = Schuetze();
            if (f.Schuetze < 0) { f.Notiz = "kein Schuetze mit Flug-Geschoss"; return f; }
            benutzt.Add(f.Schuetze);
            if (!FreierStreifen(out int c, out int r)) { f.Notiz = "kein freier Streifen"; f.Schuetze = -1; return f; }
            var s = _entities[f.Schuetze];
            s.Owner = ViewPlayer;
            s.Target = -1; s.Path = null; s.Ordered = false; s.Cooldown = 0;
            if (s.AmmoMax > 0) s.Ammo = s.AmmoMax;
            ProbeVersetzen(f.Schuetze, c, r);
            if (mitOpfer)
            {
                f.Opfer = Fahrzeug();
                if (f.Opfer < 0) { f.Notiz = "kein Opferfahrzeug"; f.Schuetze = -1; return f; }
                benutzt.Add(f.Opfer);
                var o = _entities[f.Opfer];
                o.Owner = opferFeind ? (feind >= 0 ? feind : o.Owner) : ViewPlayer;
                o.Weapon = 0; o.Target = -1; o.Path = null;
                ProbeVersetzen(f.Opfer, c + 3, r);
                f.Notiz = $"⚠ EINGRIFF Schuetze Besitzer {ViewPlayer}, Opfer Besitzer {o.Owner}";
            }
            f.Takt = _taktNr;
            return f;
        }

        var ff = Fall('F', "Feind-Fahrzeug auf der Zelle ", true, true);
        if (ff.Schuetze >= 0)
        {
            var o = _entities[ff.Opfer];
            ff.Geschossen = FireAtAusfuehren(ff.Schuetze, o.Col, o.Row);
            ff.Grund = FireAtGrund;
            // N: derselbe Schuetze sofort noch einmal
            var fn = new FireAtFall { Name = 'N', Titel = "sofort noch einmal          ", Schuetze = ff.Schuetze, Takt = _taktNr };
            _faFaelle.Add(fn);
            fn.Geschossen = FireAtAusfuehren(ff.Schuetze, o.Col, o.Row);
            fn.Grund = FireAtGrund;
            _entities[ff.Schuetze].Cooldown = 9999f;
        }
        var fv = Fall('V', "verbuendetes Fahrzeug        ", true, false);
        if (fv.Schuetze >= 0)
        {
            var o = _entities[fv.Opfer];
            fv.Geschossen = FireAtAusfuehren(fv.Schuetze, o.Col, o.Row);
            fv.Grund = FireAtGrund;
            _entities[fv.Schuetze].Cooldown = 9999f;
        }
        var fw = Fall('W', "Zelle jenseits der Reichweite", false, false);
        if (fw.Schuetze >= 0)
        {
            var s = _entities[fw.Schuetze];
            int weit = s.Range > 0 ? s.Range : Mathf.RoundToInt(RangeOf(s));
            int zc = Mathf.Min(_nav.Width - 2, s.Col + weit * 2 + 3);
            if (s.Pos.DistanceTo(ZellMitte(zc, s.Row)) <= weit * 40)
            { fw.Notiz = $"keine Zelle jenseits der Reichweite ({weit}) auf der Karte"; fw.Schuetze = -1; }
            else
            {
                fw.Geschossen = FireAtAusfuehren(fw.Schuetze, zc, s.Row);
                fw.Grund = FireAtGrund;
                s.Cooldown = 9999f;
            }
        }
        foreach (var f in _faFaelle)
            GD.Print($"fireat-check: {f.Name} {f.Titel.Trim()} — Schuetze {f.Schuetze}, Opfer {f.Opfer}, "
                   + $"{(f.Geschossen ? "geschossen" : "kein Schuss: " + f.Grund)} {f.Notiz}");
    }

    public string FireAtCheckLine()
    {
        var sb = new System.Text.StringBuilder("fireat-check\n");
        if (!_fireAtCheckAn) return sb.Append("  nicht gestartet — der Lauf sagt NICHTS").ToString();
        bool ok = _faFaelle.Count > 0;
        foreach (var f in _faFaelle)
        {
            if (f.Schuetze < 0)
            {
                ok = false;
                sb.Append($"  {f.Name} {f.Titel}: ⚠ nicht herstellbar ({f.Notiz}) — sagt NICHTS\n");
                continue;
            }
            int einschlag = _fireAtEinschlag.TryGetValue(f.Schuetze, out var t) ? t - f.Takt : -1;
            bool getroffen = f.Opfer >= 0 && _fireAtTreffer.Exists(x => x.Schuetze == f.Schuetze && x.Opfer == f.Opfer);
            bool soll = f.Name switch
            {
                'F' => f.Geschossen && einschlag > 0 && getroffen,
                'V' => f.Geschossen && einschlag > 0 && !getroffen,
                'N' => !f.Geschossen && f.Grund.Contains("laedt"),
                _ => !f.Geschossen && f.Grund.Contains("zu weit"),
            };
            ok &= soll;
            string opfer = f.Opfer >= 0 ? $", {LabelOf(_entities[f.Opfer])} {(getroffen ? "GETROFFEN" : "nicht getroffen")} (TP {_entities[f.Opfer].Hp})" : "";
            sb.Append($"  {f.Name} {f.Titel}: {(f.Geschossen ? "geschossen" : "kein Schuss — " + f.Grund)}"
                    + $"{(einschlag >= 0 ? $", Einschlag nach {einschlag} Takten" : f.Geschossen ? ", KEIN Einschlag gemessen" : "")}"
                    + $"{opfer}  {(soll ? "ja" : "NEIN")}  {f.Notiz}\n");
        }
        sb.Append($"  Gegenschalter --fireat-sofort: {FireAtSofort}, --zellgeschoss-ohne-einheit: {ZellEinschlagAlt}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
