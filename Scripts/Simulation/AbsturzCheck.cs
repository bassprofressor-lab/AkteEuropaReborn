namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--absturz-check</c> — <b>wann ein fallendes Fluggerät den Boden erreicht</b>
/// (bug-473, 07.10.2026, <c>berichte/absturz-aufprall-fable.md</c> §8).
///
/// <para>Der Prüfstand stellt Probeflieger auf Höhe 135 über ausgesuchte Zellen,
/// schießt sie über den EINEN Weg ab (<see cref="MapEntityLayer.FlugzeugAbschiessen"/>)
/// und liest je Aufprall: Zelle, Geländehöhe, Zellart, Schwelle, Höhe beim
/// Aufprall, Takte, Weg. Die Sollwerte sind <b>unabhängig gerechnet</b> (nicht aus
/// <c>BodenErreicht</c> abgelesen): aus Starthöhe 135, Fall 2 je Takt und der
/// Zahl <c>Gelände·15 + Schwelle</c> der Aufprallzelle folgt die Taktzahl
/// <c>n</c> und die Aufprallhöhe eindeutig — über Höhe 4 freiem Boden
/// <b>Alt 61 nach 37 Takten</b>, über Höhe 0 <b>Alt 1 nach 67</b>.</para>
///
/// <para><b>Nullmodelle:</b> <c>--absturz-alt</c> muss durchfallen (jede Zeile
/// Alt 0 nach ⌈135/2⌉ = 67 Takten, ohne Blick auf das Gelände);
/// <c>--absturz-drift-aus</c> muss am Fall »Drift« durchfallen (Weg 0 px).</para>
///
/// <para>⭐ <b>bug-482 (07.10.2026) — das KLANGPROTOKOLL und die WRACK-FEINLAGE</b>
/// (<c>berichte/aufprall-klang-wrack-fable.md</c> §8). Der Prüfstand hängt sich
/// an <see cref="Audio.SoundBankPlayer.Horcher"/> (jeder Klang mit Takt und
/// Zelle, VOR jedem Riegel — die Ankunft wird mitgemessen) und prüft:</para>
/// <list type="bullet">
/// <item><b>A</b> im Abschusstakt genau 1 × Klang 70 je Fall; 138 genau für den
/// Fall mit Typ 13 (der Fall »frei, Hoehe 0« fliegt als Nachschubheli).
/// Nullmodell <c>--abschussklang-aus</c>: 0.</item>
/// <item><b>B</b> im Aufpralltakt so viele Zünderklänge 400..405 wie Brandherde
/// (Zähler), und mindestens so viele kommen im 2×2 an.
/// Nullmodell <c>--brandklang-aus</c>: 0.</item>
/// <item><b>C</b> in den Takten +1..+10 n Klänge 400..415 aus dem 2×2,
/// n ~ B(10·Herde, 0,2) → bei 4 Herden 3..14; ab +11 keiner mehr. ⚠ Nicht
/// trennbar (und darum ausgelassen, nicht gefallen): wenn der Schaden des
/// Aufpralls ein Gebäude zündet oder das 2×2 eines anderen Falls binnen
/// 60 Takten überlappt.</item>
/// <item><b>D</b> je Wrackteil <c>Pos − Zellmitte(X, Y+1)</c>: dx ∈ [0, TileW),
/// dy ∈ (−2·TileH, 0]. Nullmodell <c>--wrackteil-lage-alt</c>: mittig ±½ Zelle.</item>
/// </list>
///
/// <para>⚠ Karte: eine mit Höhen — <c>map_01</c> (Kampagne 1) hat Höhen 0…7.
/// Fälle, die die Karte nicht hergibt (keine Einheit, kein Gegenstand …),
/// werden als »nicht stellbar« genannt und zählen nicht als Fehler.</para>
/// </summary>
public partial class MapEntityLayer
{
    private bool _absturzCheck;
    private int _absturzCheckTakte;

    private sealed class AbsturzFall
    {
        public string Titel = "";
        public int Slot, Alt0, Stufe, Col0, Row0, Kind;
        public bool Drift;
        // Ergebnis, aus Aufprall()
        public bool Aufgeschlagen;
        public int Takte, Alt, Col, Row, Elev, Schwelle;
        public bool KleinerGleich;
        public string Zellart = "";
        public float Weg;
        // bug-475: was der Aufprall gelegt hat (Soll 6 / 4 / 12 / 30..39)
        public int Wrack, Brand, Bilder, Teilchen;
        // bug-482: Takt des Aufpralls, Zünderklänge (Zähler), Fremdzündungen
        // durch den Schaden, Versätze der Wrackteile
        public int AufprallTick = -1, ZuendKlaenge, FremdZuendungen;
        public readonly List<Vector2> WrackVersatz = new();
    }
    private readonly List<AbsturzFall> _absturzFaelle = new();

    /// <summary>bug-482: das Klangprotokoll — Takt, Platz, Zelle (−1 ohne Ort).</summary>
    private readonly List<(int Tick, int Slot, int Col, int Row)> _absturzKlang = new();
    private int _absturzSchussTick = -1, _absturzZuendMerker;

    /// <summary>Höchstens so lange warten — 67 Takte braucht der längste Fall,
    /// dazu 60 Takte Nachbrennen für das Klangprotokoll (C).</summary>
    private const int AbsturzCheckWartetakte = 200;

    /// <summary><c>--absturz-check</c> anwerfen.</summary>
    public void AbsturzCheckStart() => _absturzCheck = true;

    private void AbsturzCheckTakt()
    {
        if (_absturzCheck) { AbsturzCheckAufbauen(); return; }
        if (_absturzCheckTakte <= 0) return;
        bool alleUnten = true;
        int letzter = 0;
        foreach (var f in _absturzFaelle)
        {
            if (!f.Aufgeschlagen) alleUnten = false;
            else letzter = Mathf.Max(letzter, f.AufprallTick);
        }
        // bug-482: nach dem letzten Aufprall noch 61 Takte mithören (Soll C: ab +11 still)
        bool nachgehoert = alleUnten && DebugTicks > letzter + 61;
        if (++_absturzCheckTakte < AbsturzCheckWartetakte && !nachgehoert) return;
        _absturzCheckTakte = -1;
        AbsturzCheckAuswerten();
    }

    /// <summary>Der Haken aus <c>Aufprall()</c> — VOR dem Schaden gelesen.</summary>
    private void AbsturzCheckNotieren(Special a, AbsturzBeginn? beginn, int elev, int schwelle,
                                      bool kleinerGleich, string zellart)
    {
        foreach (var f in _absturzFaelle)
        {
            if (f.Slot != a.Slot || f.Aufgeschlagen) continue;
            f.Aufgeschlagen = true;
            f.Takte = beginn?.Takte ?? -1;
            f.Alt = a.Alt; f.Col = a.Col; f.Row = a.Row;
            f.Elev = elev; f.Schwelle = schwelle; f.KleinerGleich = kleinerGleich; f.Zellart = zellart;
            f.Weg = beginn != null ? a.Pos.DistanceTo(beginn.Pos) : 0f;
            (f.Wrack, f.Brand, f.Bilder, f.Teilchen) = _aufprallZuletzt;
            f.AufprallTick = DebugTicks;
            f.ZuendKlaenge = BrandZuendKlaenge - _absturzZuendMerker;
            _absturzZuendMerker = BrandZuendKlaenge;
            f.WrackVersatz.Clear();
            f.WrackVersatz.AddRange(_wrackteilVersatz);
        }
    }

    /// <summary>Der zweite Haken aus <c>Aufprall()</c>, NACH dem Schaden: hat der
    /// Schaden selbst noch Brände gezündet (Gebäude-Stufenwechsel)? Dann ist das
    /// Nachbrennen dieses Falls nicht vom Absturz allein.</summary>
    private void AbsturzCheckNachSchaden(Special a)
    {
        foreach (var f in _absturzFaelle)
        {
            if (f.Slot != a.Slot || f.AufprallTick != DebugTicks) continue;
            f.FremdZuendungen = BrandZuendKlaenge - _absturzZuendMerker;
            _absturzZuendMerker = BrandZuendKlaenge;
        }
    }

    private void AbsturzCheckAufbauen()
    {
        _absturzCheck = false;
        if (_nav == null) { GD.Print("absturz-check: KEIN URTEIL — keine Karte"); return; }
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        // ---- die Zellen suchen ---------------------------------------------
        bool Frei(int c, int r) => _nav.GroundAt(c, r) == Simulation.NavGrid.Ground.Free
                                   && _nav.OccupantAt(c, r) < 0
                                   && AbsturzSchwelle(c, r, out _, out string z) == SchwelleFrei && z == "frei";
        Vector2I? hoch = null, flach = null, gesperrt = null, einheit = null, fuss = null, gebaeude = null;
        int hochElev = -1;
        for (int r = 1; r < _nav.Height - 1; r++)
            for (int c = 1; c < _nav.Width - 1; c++)
            {
                int e = _nav.ElevAt(c, r);
                if (Frei(c, r))
                {
                    // Höhe 4 ist der Fall des Berichts; sonst die höchste freie Zelle.
                    if (e == 4 && hochElev != 4) { hoch = new Vector2I(c, r); hochElev = 4; }
                    else if (hochElev != 4 && e > hochElev && e >= 1) { hoch = new Vector2I(c, r); hochElev = e; }
                    if (e == 0 && flach == null) flach = new Vector2I(c, r);
                }
                else if (gesperrt == null && _nav.OccupantAt(c, r) < 0
                         && AbsturzSchwelle(c, r, out _, out string z) == SchwelleGesperrt && z == "gesperrt")
                    gesperrt = new Vector2I(c, r);
            }
        foreach (var e in _entities)
        {
            if (e.Dead || e.IsProp) continue;
            if (e.IsBuilding) { gebaeude ??= new Vector2I(e.Col, e.Row); continue; }
            if (e.Infantry >= 0) fuss ??= new Vector2I(e.Col, e.Row);
            else if (_nav.OccupantAt(e.Col, e.Row) >= 0) einheit ??= new Vector2I(e.Col, e.Row);
        }

        // bug-482: der Fall »frei, Hoehe 0« fliegt als Typ 13 (Nachschubheli) —
        // nur dann kommt zum Klang 70 die Sprachzeile 138 (@0x425F17).
        var faelle = new List<(string Titel, Vector2I? Zelle, int Stufe, int Kind)>
        {
            ($"frei, Hoehe {hochElev}", hoch, 0, 1),
            ("frei, Hoehe 0 (Typ 13)", flach, 0, AbschussSprachzeileAbTyp),
            ($"Drift (Stufe 5, Richtung 90) ab Hoehe {hochElev}", hoch, 5, 1),
            ("ueber einer Einheit", einheit, 0, 1),
            ("ueber Fussvolk", fuss, 0, 1),
            ("ueber einem Gebaeude", gebaeude, 0, 1),
            ("ueber einem Gegenstand (gesperrt)", gesperrt, 0, 1),
        };
        int slot = 900;
        foreach (var x in _special) slot = Mathf.Max(slot, x.Slot + 1);

        // bug-482: ab jetzt jeden Klang mit Takt und Zelle mitschreiben
        _absturzKlang.Clear();
        _absturzSchussTick = DebugTicks;
        _absturzZuendMerker = BrandZuendKlaenge;
        Audio.SoundBankPlayer.Horcher = (s, c, r) =>
            _absturzKlang.Add((DebugTicks, s, float.IsNaN(c) ? -1 : (int)c, float.IsNaN(r) ? -1 : (int)r));

        var sb = new System.Text.StringBuilder("absturz-check: Aufbau\n");
        foreach (var (titel, zelle, stufe, kind) in faelle)
        {
            if (zelle == null) { sb.AppendLine($"   {titel}: nicht stellbar auf dieser Karte"); continue; }
            var z = zelle.Value;
            var a = new Special
            {
                Slot = slot++, Kind = kind, Name = "Probeflieger", TypeName = "Probeflieger",
                Owner = me, Stored = false, HomeSlot = -1,
                Col = z.X, Row = z.Y, Pos = CellCenter(z.X, z.Y),
                Hp = 10, HpMax = 10, Defence = 0, Attack = 0, Ammo = 0, AmmoMax = 0,
                Fuel = 999, FuelMax = 999, Speed = 5, StufeUnten = 0, Stufe = stufe, Dir = 90,
                Alt = FlughoeheMax, Sollhoehe = FlughoeheMax,
            };
            a.Goal = a.Pos;
            _special.Add(a);
            _absturzFaelle.Add(new AbsturzFall { Titel = titel, Slot = a.Slot, Alt0 = a.Alt, Stufe = stufe, Drift = stufe > 0, Col0 = z.X, Row0 = z.Y, Kind = kind });
            FlugzeugAbschiessen(a, "absturz-check");
            sb.AppendLine($"   {titel}: Platz {a.Slot} auf ({z.X},{z.Y}) Hoehe {_nav.ElevAt(z.X, z.Y)}, Alt {a.Alt}");
        }
        GD.Print(sb.ToString().TrimEnd());
        if (_absturzFaelle.Count == 0) { GD.Print("absturz-check: KEIN URTEIL — kein Fall stellbar"); Audio.SoundBankPlayer.Horcher = null; return; }
        _absturzCheckTakte = 1;
    }

    /// <summary>Die vier Zellen des Brand-2×2 eines Falls (innerhalb der Karte).</summary>
    private List<(int, int)> AbsturzZweiMalZwei(AbsturzFall f)
    {
        var l = new List<(int, int)>();
        foreach (var (dc, dr) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            if (_nav == null || _nav.InBounds(f.Col + dc, f.Row + dr)) l.Add((f.Col + dc, f.Row + dr));
        return l;
    }

    private int AbsturzKlaenge(List<(int, int)> zellen, int tickVon, int tickBis, int slotVon, int slotBis)
    {
        int n = 0;
        foreach (var k in _absturzKlang)
            if (k.Tick >= tickVon && k.Tick <= tickBis && k.Slot >= slotVon && k.Slot <= slotBis
                && zellen.Contains((k.Col, k.Row))) n++;
        return n;
    }

    private void AbsturzCheckAuswerten()
    {
        Audio.SoundBankPlayer.Horcher = null;
        var sb = new System.Text.StringBuilder("absturz-check\n");
        int ok = 0, gezaehlt = 0;

        // ---- A: der Abschuss (alle Fälle im selben Takt abgeschossen) --------
        int n70 = 0, n138 = 0, soll138 = 0;
        foreach (var k in _absturzKlang)
        {
            if (k.Tick != _absturzSchussTick) continue;
            if (k.Slot == Audio.GameSounds.AircraftExplodes) n70++;
            if (k.Slot == Audio.GameSounds.AircraftExplodes2) n138++;
        }
        foreach (var f in _absturzFaelle) if (f.Kind >= AbschussSprachzeileAbTyp) soll138++;
        bool abschussOk = n70 == _absturzFaelle.Count && n138 == soll138;
        sb.AppendLine($"   {(abschussOk ? "ok  " : "⚠ FALSCH")} Abschussklang im Takt {_absturzSchussTick}: " +
                      $"{n70}x 70 (Soll {_absturzFaelle.Count}, je Fall einer @0x425F0F), {n138}x 138 (Soll {soll138}, nur Typ >= 13 @0x425F53)");

        foreach (var f in _absturzFaelle)
        {
            gezaehlt++;
            if (!f.Aufgeschlagen)
            {
                sb.AppendLine($"   ⚠ FALSCH {f.Titel}: nach {AbsturzCheckWartetakte} Takten NICHT aufgeschlagen");
                continue;
            }
            // ---- das SOLL, unabhängig gerechnet (Bericht §8) -----------------
            // Grenze B = Gelände·15 + Schwelle der Aufprallzelle; Fall −2 je Takt
            // aus Alt0; »<=«: kleinstes n mit Alt0 − 2n <= B; »<«: … < B.
            int b = f.Elev * AltJeHoehenstufe + f.Schwelle;
            int n = f.KleinerGleich
                ? Mathf.CeilToInt((f.Alt0 - b) / 2f)
                : (f.Alt0 - b) / 2 + 1;
            if (n < 0) n = 0;
            int sollAlt = f.Alt0 - 2 * n;
            bool altOk = f.Alt == sollAlt && f.Takte == n;
            bool driftOk = !f.Drift || (f.Weg > 0f && (f.Weg < TileW || (f.Col, f.Row) != (f.Col0, f.Row0)));
            bool stehtOk = f.Drift || f.Weg < 0.5f;
            // bug-475: der Aufprall selbst — 6 Wrackteile (0x423A2F, bl = 6),
            // 4 Brandherde (0x423A98..0x423AC9), 12 Explosionsbilder (2+5+5 in
            // 0x4AF000), 30..39 Teilchen (10+rand%10 Brocken + 20 Splitter).
            // Am Kartenrand duerfen Herde fehlen (Klemme), das wird genannt.
            bool randnah = f.Col + 1 >= (_nav?.Width ?? 256) || f.Row + 1 >= (_nav?.Height ?? 256);
            bool brandOk = GebaeudebrandAus ? f.Brand == 0 : (f.Brand == 4 || (randnah && f.Brand >= 2));
            bool wrackOk = f.Wrack == 6 && f.Bilder == 12 && f.Teilchen is >= 30 and <= 39 && brandOk;

            // ---- bug-482 B/C: der Klang des Aufpralls und des Nachbrennens ----
            var zellen = AbsturzZweiMalZwei(f);
            int t0 = f.AufprallTick;
            int zuendAn = AbsturzKlaenge(zellen, t0, t0, 400, 405);
            bool zuendOk = GebaeudebrandAus
                ? f.ZuendKlaenge == 0
                : f.ZuendKlaenge == f.Brand && zuendAn >= f.Brand;
            bool trennbar = f.FremdZuendungen == 0;
            foreach (var g in _absturzFaelle)
            {
                if (g == f || !g.Aufgeschlagen) continue;
                if (Mathf.Abs(g.AufprallTick - f.AufprallTick) > 60) continue;
                foreach (var z in AbsturzZweiMalZwei(g)) if (zellen.Contains(z)) trennbar = false;
            }
            int nach = AbsturzKlaenge(zellen, t0 + 1, t0 + 10, 400, 415);
            int nach410 = AbsturzKlaenge(zellen, t0 + 1, t0 + 10, 410, 415);
            int spaeter = AbsturzKlaenge(zellen, t0 + 11, t0 + 60, 400, 415);
            // n ~ B(T, 0,2), T zwischen 9·Herde (Ticker läuft im Aufpralltakt mit)
            // und 10·Herde: lo aus T = 9·Herde (Mittel − 2σ), hi aus T = 10·Herde
            // (Mittel + 2,5σ) → bei 4 Herden 3..14.
            int lo = Mathf.Max(0, Mathf.CeilToInt(9 * f.Brand * 0.2f - 2f * Mathf.Sqrt(9 * f.Brand * 0.16f)));
            int hi = Mathf.FloorToInt(10 * f.Brand * 0.2f + 2.5f * Mathf.Sqrt(10 * f.Brand * 0.16f));
            bool nachOk = GebaeudebrandAus || BrandklangAus
                ? nach == 0 && spaeter == 0
                : nach >= lo && nach <= hi && spaeter == 0;
            if (!trennbar && !(GebaeudebrandAus || BrandklangAus)) nachOk = true;   // ausgelassen, nicht gefallen
            // Nullmodell --brandklang-aus: Zünder 0 ≠ Herde 4 → zuendOk fällt; C muss still sein.
            if (BrandklangAus && !GebaeudebrandAus) zuendOk = f.ZuendKlaenge == f.Brand;   // = 0 == 4 → falsch

            // ---- bug-482 D: die Wrackteile -------------------------------------
            int dOk = 0;
            foreach (var v in f.WrackVersatz)
                if (v.X >= 0f && v.X < TileW && v.Y <= 0f && v.Y > -2f * TileH) dOk++;
            bool wrackLageOk = f.WrackVersatz.Count == f.Wrack && dOk == f.Wrack;

            bool passt = altOk && driftOk && stehtOk && wrackOk && zuendOk && nachOk && wrackLageOk;
            if (passt) ok++;
            sb.AppendLine($"   {(passt ? "ok  " : "⚠ FALSCH")} {f.Titel}: Zelle ({f.Col},{f.Row}) Hoehe {f.Elev} " +
                          $"[{f.Zellart}] {(f.KleinerGleich ? "<=" : "<")} {f.Elev * AltJeHoehenstufe}+{f.Schwelle}: " +
                          $"Alt {f.Alt0} -> {f.Alt} nach {f.Takte} Takten (Soll {sollAlt} nach {n}), " +
                          $"Weg {f.Weg:0} px" +
                          (f.Drift ? " (Soll > 0" + (f.Weg >= TileW ? ", Zelle gewechselt" : "") + ")" : " (Soll 0)") +
                          $"; Aufprall: Wrackteile {f.Wrack}, Brandherde {f.Brand}, Explosionsbilder {f.Bilder}, " +
                          $"Teilchen {f.Teilchen} (Soll 6 / 4 / 12 / 30..39{(GebaeudebrandAus ? ", Herde 0 unter --gebaeudebrand-aus" : "")})" +
                          (wrackOk ? "" : " ⚠"));
            sb.AppendLine($"        Klang: Takt {t0} Zuender {f.ZuendKlaenge}x 400..405 (Soll {f.Brand}), im 2x2 angekommen {zuendAn}" +
                          (zuendOk ? "" : " ⚠") +
                          $"; Takte +1..+10: {nach} Klaenge 400..415 (davon {nach410} aus 410..415; Soll {lo}..{hi}" +
                          (trennbar ? "" : ", NICHT TRENNBAR: " + (f.FremdZuendungen > 0 ? $"{f.FremdZuendungen} Fremdzuendung(en) durch den Schaden" : "2x2 eines anderen Falls ueberlappt") + ", ausgelassen") +
                          $"); +11..+60: {spaeter} (Soll 0)" + (nachOk ? "" : " ⚠") +
                          $"; Wrackteile dx in [0,{TileW}) und dy in (-{2 * TileH},0]: {dOk} von {f.WrackVersatz.Count}" +
                          (wrackLageOk ? "" : " ⚠"));
        }
        bool bestanden = gezaehlt > 0 && ok == gezaehlt && abschussOk;
        sb.Append(bestanden
            ? $"   ✅ BESTANDEN — {ok} von {gezaehlt} Faellen: Aufprall bei Gelaende*15 + Schwelle, Drift, Wrack/Brandherde, Klang (70, 4x400..405, Nachbrennen) und Wrack-Feinlage wie gelesen"
            : $"   ❌ DURCHGEFALLEN — {gezaehlt - ok} von {gezaehlt} Faellen weichen ab{(abschussOk ? "" : ", Abschussklang falsch")}");
        if (AbsturzAlt) sb.Append("   [Nullmodell --absturz-alt: Alt 0 nach 67 Takten, MUSS durchfallen]");
        if (AbsturzDriftAus) sb.Append("   [Nullmodell --absturz-drift-aus: Weg 0, der Driftfall MUSS durchfallen]");
        if (AbsturzWrackAlt) sb.Append("   [Nullmodell --absturz-wrack-alt: Wrackteile/Brandherde/Bilder 0, JEDER Fall MUSS durchfallen]");
        if (AbschussklangAus) sb.Append("   [Nullmodell --abschussklang-aus: 0x 70, MUSS durchfallen]");
        if (BrandklangAus) sb.Append("   [Nullmodell --brandklang-aus: Zuender 0 statt 4, MUSS durchfallen]");
        if (WrackteilLageAlt) sb.Append("   [Nullmodell --wrackteil-lage-alt: Wrackteile mittig, MUSS durchfallen]");
        GD.Print(sb);
    }
}
