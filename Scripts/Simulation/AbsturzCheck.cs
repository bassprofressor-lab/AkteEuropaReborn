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
        public int Slot, Alt0, Stufe, Col0, Row0;
        public bool Drift;
        // Ergebnis, aus Aufprall()
        public bool Aufgeschlagen;
        public int Takte, Alt, Col, Row, Elev, Schwelle;
        public bool KleinerGleich;
        public string Zellart = "";
        public float Weg;
        // bug-475: was der Aufprall gelegt hat (Soll 6 / 4 / 12 / 30..39)
        public int Wrack, Brand, Bilder, Teilchen;
    }
    private readonly List<AbsturzFall> _absturzFaelle = new();

    /// <summary>Höchstens so lange warten — 67 Takte braucht der längste Fall.</summary>
    private const int AbsturzCheckWartetakte = 200;

    /// <summary><c>--absturz-check</c> anwerfen.</summary>
    public void AbsturzCheckStart() => _absturzCheck = true;

    private void AbsturzCheckTakt()
    {
        if (_absturzCheck) { AbsturzCheckAufbauen(); return; }
        if (_absturzCheckTakte <= 0) return;
        bool alleUnten = true;
        foreach (var f in _absturzFaelle) if (!f.Aufgeschlagen) alleUnten = false;
        if (++_absturzCheckTakte < AbsturzCheckWartetakte && !alleUnten) return;
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

        var faelle = new List<(string Titel, Vector2I? Zelle, int Stufe)>
        {
            ($"frei, Hoehe {hochElev}", hoch, 0),
            ("frei, Hoehe 0", flach, 0),
            ($"Drift (Stufe 5, Richtung 90) ab Hoehe {hochElev}", hoch, 5),
            ("ueber einer Einheit", einheit, 0),
            ("ueber Fussvolk", fuss, 0),
            ("ueber einem Gebaeude", gebaeude, 0),
            ("ueber einem Gegenstand (gesperrt)", gesperrt, 0),
        };
        int slot = 900;
        foreach (var x in _special) slot = Mathf.Max(slot, x.Slot + 1);
        var sb = new System.Text.StringBuilder("absturz-check: Aufbau\n");
        foreach (var (titel, zelle, stufe) in faelle)
        {
            if (zelle == null) { sb.AppendLine($"   {titel}: nicht stellbar auf dieser Karte"); continue; }
            var z = zelle.Value;
            var a = new Special
            {
                Slot = slot++, Kind = 1, Name = "Probeflieger", TypeName = "Probeflieger",
                Owner = me, Stored = false, HomeSlot = -1,
                Col = z.X, Row = z.Y, Pos = CellCenter(z.X, z.Y),
                Hp = 10, HpMax = 10, Defence = 0, Attack = 0, Ammo = 0, AmmoMax = 0,
                Fuel = 999, FuelMax = 999, Speed = 5, StufeUnten = 0, Stufe = stufe, Dir = 90,
                Alt = FlughoeheMax, Sollhoehe = FlughoeheMax,
            };
            a.Goal = a.Pos;
            _special.Add(a);
            _absturzFaelle.Add(new AbsturzFall { Titel = titel, Slot = a.Slot, Alt0 = a.Alt, Stufe = stufe, Drift = stufe > 0, Col0 = z.X, Row0 = z.Y });
            FlugzeugAbschiessen(a, "absturz-check");
            sb.AppendLine($"   {titel}: Platz {a.Slot} auf ({z.X},{z.Y}) Hoehe {_nav.ElevAt(z.X, z.Y)}, Alt {a.Alt}");
        }
        GD.Print(sb.ToString().TrimEnd());
        if (_absturzFaelle.Count == 0) { GD.Print("absturz-check: KEIN URTEIL — kein Fall stellbar"); return; }
        _absturzCheckTakte = 1;
    }

    private void AbsturzCheckAuswerten()
    {
        var sb = new System.Text.StringBuilder("absturz-check\n");
        int ok = 0, gezaehlt = 0;
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
            bool passt = altOk && driftOk && stehtOk && wrackOk;
            if (passt) ok++;
            sb.AppendLine($"   {(passt ? "ok  " : "⚠ FALSCH")} {f.Titel}: Zelle ({f.Col},{f.Row}) Hoehe {f.Elev} " +
                          $"[{f.Zellart}] {(f.KleinerGleich ? "<=" : "<")} {f.Elev * AltJeHoehenstufe}+{f.Schwelle}: " +
                          $"Alt {f.Alt0} -> {f.Alt} nach {f.Takte} Takten (Soll {sollAlt} nach {n}), " +
                          $"Weg {f.Weg:0} px" +
                          (f.Drift ? " (Soll > 0" + (f.Weg >= TileW ? ", Zelle gewechselt" : "") + ")" : " (Soll 0)") +
                          $"; Aufprall: Wrackteile {f.Wrack}, Brandherde {f.Brand}, Explosionsbilder {f.Bilder}, " +
                          $"Teilchen {f.Teilchen} (Soll 6 / 4 / 12 / 30..39{(GebaeudebrandAus ? ", Herde 0 unter --gebaeudebrand-aus" : "")})" +
                          (wrackOk ? "" : " ⚠"));
        }
        bool bestanden = gezaehlt > 0 && ok == gezaehlt;
        sb.Append(bestanden
            ? $"   ✅ BESTANDEN — {ok} von {gezaehlt} Faellen: Aufprall bei Gelaende*15 + Schwelle, Drift und Wrack/Brandherde wie gelesen"
            : $"   ❌ DURCHGEFALLEN — {gezaehlt - ok} von {gezaehlt} Faellen weichen ab");
        if (AbsturzAlt) sb.Append("   [Nullmodell --absturz-alt: Alt 0 nach 67 Takten, MUSS durchfallen]");
        if (AbsturzDriftAus) sb.Append("   [Nullmodell --absturz-drift-aus: Weg 0, der Driftfall MUSS durchfallen]");
        if (AbsturzWrackAlt) sb.Append("   [Nullmodell --absturz-wrack-alt: Wrackteile/Brandherde/Bilder 0, JEDER Fall MUSS durchfallen]");
        GD.Print(sb);
    }
}
