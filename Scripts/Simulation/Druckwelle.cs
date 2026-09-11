namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE DRUCKWELLE DER ART 7 — sechs Ringe, alle drei Takte, 109 Zellen</b>
/// (gebaut 11.09.2026 nach <c>berichte/art7-gaswerfer.md</c> 1–2, seine Ansage
/// »bau die offenen punkte auch wie im original«).
///
/// <para><b>Gelesen:</b></para>
/// <code>
///   0x452190  Geschossflug: in JEDEM Trefferzweig `cmp Art,7 / je` -> 0x454510(Zelle)
///             STATT Zasah; Ankunft am Ziel (@0x452FC2) zuendet Art 7 in jeder Hoehe
///   0x454510  Klang 410, Klang 400 (Modus 2, an der Zelle), 0x454560
///   0x454560  Tafel 0x88E390, 50 Plaetze: ersten freien suchen, keiner -> nichts;
///             Lichtquelle 0x4222C0(x, y, 8, 150); Bild 510 + rand%9; Radius := 1
///   0x454600  nur wenn Takt % 3 == 0; je Platz: soll = r − 1, Fenster ±6,
///             Randpruefung = AUSLASSEN, trunc(sqrt(dx²+dy²)) == soll ->
///             Zasah(50000 + |dx|+|dy|, Zellwort); rand%7 -> Bilder; Klang 400 mit 1/(2r);
///             r += 1, r >= 7 -> Platz frei
///   0x40CC54  Band 50000..50019: Rang 0, Angriff 200 / (|dx|+|dy| + 1), keine Buendnisfrage
///   0x40B769  Tod einer ZBRAN-8-Einheit mit NABYTO == 0 -> 0x454560 an ihrer Zelle
///   0x452638  Flug: gerade auf die ZIELZELLE (kein Nachfuehren), Steigen +7 bis 150,
///             Reisen, Stuerzen −7 sobald |Zielhoehe − Hoehe|/7 + 1 >= Restschritte
///   0x40C57D  ZBRAN 8 verbraucht keine Munition
/// </code>
///
/// <para>Je Zelle traegt <see cref="SkripttrefferZelle"/> den Treffer — dessen vier
/// Arme (Einheit, Fussvolk, Gebaeude, Wald/Objekt) sind die des Bandes 40000 mit
/// Rang 0, und das Band 50000 rechnet genauso, nur mit dem Angriff aus dem
/// Abstand.</para>
///
/// <para>⚠ UNSERE SETZUNGEN, benannt: die Lichtquelle (Radius 8, 150 Takte) ist
/// NICHT gebaut; von den Bildern je Zelle zeigen wir nur die Arme 0..2 (Folge
/// 510..518 = <c>sprengung0..8</c>), die Folgen 310..315, 227..233, die Truemmer
/// (0x4AD520) und die Krater (0x4A98A0) sind nicht ausgefuehrt bzw. nicht
/// gedeutet; die Bildversaetze der Wirkungsbilder sind ungelesen, die Bilder
/// sitzen auf der Zellmitte; die Abschussstatistik fuer Spieler 0
/// (<c>byte[0x87D6A8]</c> ohne Schreiber) ist nicht nachgebaut; das Gleis
/// (<c>RailHit</c>) wird vom Art-7-Einschlag nicht mehr getroffen, weil
/// ungelesen ist, ob 0x40D799 fuer die Welle laeuft; die Rakete wird weiter in
/// Bodenhoehe gezeichnet (der Zeichner 0x42B198 ist fuer die Hoehe nicht
/// gelesen); die ±10-Zellen-Streuung des Zweigs 0x40BB58 bei Ziel 0xFFFF ist
/// nicht gebaut.</para>
///
/// <para>Gegenschalter <c>--art7-einzeltreffer</c> (der Stand vor dem 11.09.:
/// ein Treffer mit Waffenschaden auf der Zielzelle, Flug wie jede gerade Bahn,
/// Munition wie jeder Schuss). Pruefstand <c>--druckwelle-check</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--art7-einzeltreffer</c></summary>
    public static bool Art7Einzeltreffer;

    private struct DruckwellenPlatz
    {
        public int Radius;       // +0, 0 = frei
        public int Col, Row;     // +1, +2
        public int Takt;         // nur fuer den Pruefstand
    }

    private const int DwPlaetze = 50, DwFenster = 6, DwRinge = 6;
    private readonly DruckwellenPlatz[] _druckwellen = new DruckwellenPlatz[DwPlaetze];
    public int DwAngemeldet, DwVoll, DwZellen;

    private static bool Art7Druckwelle(in Projectile p) => p.Art == 7 && !Art7Einzeltreffer;

    /// <summary>0x454510 — der Vorspann am Einschlag: die zwei Klaenge 410/400
    /// sind genau unser <see cref="Audio.GameSounds.Explosion(float, float)"/>.</summary>
    private void DruckwelleZuenden(int c, int r)
    {
        Audio.GameSounds.Explosion(c, r);
        DruckwelleAnmelden(c, r);
    }

    /// <summary>0x454560</summary>
    private void DruckwelleAnmelden(int c, int r)
    {
        int k = System.Array.FindIndex(_druckwellen, w => w.Radius == 0);
        if (k < 0) { DwVoll++; return; }                                       // @0x454575
        // 0x4222C0 Lichtquelle (8, 150) — NICHT gebaut, siehe Kopf
        _effects.Add(new Effect { Pos = ZellMitte(c, r), Kind = "sprengung" + Simulation.Determinism.Roll(9),
                                  FrameTime = 0.04f });                        // @0x454596: 510 + rand%9
        _druckwellen[k] = new DruckwellenPlatz { Radius = 1, Col = c, Row = r, Takt = _taktNr };
        DwAngemeldet++;
        if (_dwCheckAn) _dwAnmeldungen.Add((k, c, r, _taktNr));
    }

    /// <summary>0x454600 — jeden Takt gerufen, wirkt nur bei <c>Takt % 3 == 0</c>.</summary>
    private void DruckwelleTakt()
    {
        if (_taktNr % 3 != 0) return;
        for (int k = 0; k < DwPlaetze; k++)
        {
            int r = _druckwellen[k].Radius;
            if (r == 0) continue;
            int x = _druckwellen[k].Col, y = _druckwellen[k].Row;
            int soll = r - 1;
            int n = 0;
            for (int row = y - DwFenster; row <= y + DwFenster; row++)
                for (int col = x - DwFenster; col <= x + DwFenster; col++)
                {
                    if (_nav == null || col < 0 || row < 0 || col >= _nav.Width || row >= _nav.Height)
                        continue;                                                // 0x41D1D0: auslassen
                    int dx = x - col, dy = y - row;
                    if ((int)System.Math.Sqrt(dx * dx + dy * dy) != soll) continue;   // _ftol rundet ab
                    int m = System.Math.Abs(dx) + System.Math.Abs(dy);
                    int besatz = -1, hpVor = 0;
                    if (_dwCheckAn)
                    {
                        _dwZellen.Add((k, soll, col, row, _taktNr));
                        besatz = _nav.OccupantAt(col, row);
                        if (besatz >= 0 && besatz < _entities.Count) hpVor = _entities[besatz].Hp;
                    }
                    SkripttrefferZelle(col, row, 200 / (m + 1), "DRUCKWELLE");   // 0x40CC66
                    if (besatz >= 0 && besatz < _entities.Count)                 // Pruefstand: Schaden IM Durchgang
                        _dwSchaden[besatz] = _dwSchaden.GetValueOrDefault(besatz) + hpVor - _entities[besatz].Hp;
                    n++;
                    // rand%7 -> Sprungtafel 0x4549CC; nur die Arme 0..2 haben ausgefuehrte Bilder
                    if (Simulation.Determinism.Roll(7) <= 2)
                        _effects.Add(new Effect { Pos = ZellMitte(col, row),
                                                  Kind = "sprengung" + Simulation.Determinism.Roll(9),
                                                  FrameTime = 0.04f });
                    if (Simulation.Determinism.Roll(2 * soll + 2) < 1)            // @0x454930
                        Audio.GameSounds.PlayAt(Audio.GameSounds.ExplosionHigh, col, row);
                }
            DwZellen += n;
            // ein Tod in diesem Durchgang kann einen Platz neu belegt haben — nur
            // der eigene Platz wird weitergezaehlt, wie @0x454999
            if (_druckwellen[k].Col == x && _druckwellen[k].Row == y && _druckwellen[k].Radius == r)
                _druckwellen[k].Radius = r + 1 >= DwRinge + 1 ? 0 : r + 1;
        }
    }

    /// <summary>0x452638 — Steigen, Reisen, Stuerzen. <paramref name="rest"/> sind
    /// die Restschritte VOR diesem Schritt (0 heisst angekommen, gilt als 1).</summary>
    private static void Art7Flughoehe(ref Projectile p, int rest)
    {
        if (!p.FlugBegonnen)
        {
            p.FlugBegonnen = true;
            p.Flughoehe = Mathf.RoundToInt(p.HoeheStart);
            p.Steigzustand = 1;                                                  // 0x451E85: 1.0f
        }
        if (rest <= 0) rest = 1;
        switch (p.Steigzustand)
        {
            case 1:
                p.Flughoehe += 7;
                if (p.Flughoehe >= 150) { p.Flughoehe = 150; p.Steigzustand = 0; }
                break;
            case 2:
                p.Flughoehe -= 7;
                break;
            default:
                if (System.Math.Abs(Mathf.RoundToInt(p.HoeheZiel) - p.Flughoehe) / 7 + 1 >= rest)
                    p.Steigzustand = 2;
                break;
        }
    }

    /// <summary>@0x40B769 — der Tod einer Mittelstreckenrakete, die nicht
    /// nachlaedt.</summary>
    private void TodDerMittelstreckenrakete(Entity v)
    {
        if (Art7Einzeltreffer || WeaponRowOf(v.Weapon) != 8 || v.Cooldown > 0) return;
        DruckwelleAnmelden(v.Col, v.Row);
    }

    // ================= der Pruefstand ==========================================

    private bool _dwCheckAn;
    private readonly List<(int Platz, int C, int R, int Takt)> _dwAnmeldungen = new();
    private readonly List<(int Platz, int Ring, int C, int R, int Takt)> _dwZellen = new();
    private readonly Dictionary<int, int> _dwSchaden = new();
    private int _dwSchuetze = -1, _dwArt = -1, _dwSchussTakt, _dwMaxHoehe = int.MinValue, _dwSturzAb = -1;
    private int _dwZielC, _dwZielR;
    private bool _dwGeschossen;
    private string _dwNotiz = "", _dwGrund = "";

    private sealed class DwOpfer
    {
        public string Titel = "";
        public int Idx = -1, Dx, Dy, HpVorher, KernSoll;
    }

    private readonly List<DwOpfer> _dwOpfer = new();

    /// <summary>
    /// <c>--druckwelle-check</c> — ein Schuetze mit Waffe 28 schiesst per fire_at
    /// 7 Zellen weit auf eine freie Zelle T; um T stehen drei Fahrzeuge mit 999 TP
    /// und ohne Waffe, alle beim Spieler wie der Schuetze: A auf T+(3,0), B auf
    /// T+(0,2), C auf T+(6,0). Der Schaden wird IM Durchgang gemessen.
    /// <para>Soll: Anmeldung auf T; sechs Durchgaenge im Abstand von 3 Takten mit
    /// 1/8/16/20/24/40 Zellen, Summe 109; A im Ring 3 mit dem Schaden des
    /// Einheitenarms bei Angriff 50; B getroffen; C nie; Flughoehe erreicht 150 und
    /// stuerzt.</para>
    /// <para>⚠ EINGRIFFE: Waffe 28 und Reichweite ≥ 10 fuer den Schuetzen, Versetzen,
    /// Besitzer, 999 TP und Waffe 0 fuer die Opfer, Nachladen 9999 nach dem Schuss.
    /// Nullmodell <c>--art7-einzeltreffer</c>.</para>
    /// </summary>
    public void DruckwelleCheckStart()
    {
        if (_nav == null) return;
        _dwCheckAn = true;
        var benutzt = new HashSet<int>();
        bool Frei(int k)
        {
            var x = _entities[k];
            return !benutzt.Contains(k) && !x.Dead && !x.IsProp && !x.IsBuilding && x.Mobile
                   && x.Ukol < 50 && !Untergestellt(x) && x.Infantry < 0 && x.GameUnitType == 0;
        }

        // die Zellen muessen leer sein, sonst stimmt die Belegung nicht
        bool Leer(int c, int r) => c >= 0 && r >= 0 && c < _nav.Width && r < _nav.Height
                                   && _nav.OccupantAt(c, r) < 0 && GebaeudeAufZelle(c, r) < 0;
        int sc = -1, sr = -1;
        for (int versuch = 0; versuch < 8; versuch++)
        {
            if (!FreierStreifen(out int c, out int r)) break;
            int tc = c + 7;
            if (tc + DwFenster >= _nav.Width || r - DwFenster < 0 || r + DwFenster >= _nav.Height) continue;
            if (Leer(tc + 3, r) && Leer(tc, r + 2) && Leer(tc + 6, r)) { sc = c; sr = r; break; }
        }
        if (sc < 0) { _dwNotiz = "kein freier Platz fuer Schuetze, Ziel und drei Opfer"; GD.Print("druckwelle-check: " + _dwNotiz); return; }

        for (int k = 0; k < _entities.Count; k++)
            if (Frei(k) && _entities[k].Owner == ViewPlayer) { _dwSchuetze = k; break; }
        if (_dwSchuetze < 0)
            for (int k = 0; k < _entities.Count; k++)
                if (Frei(k)) { _dwSchuetze = k; break; }
        if (_dwSchuetze < 0) { _dwNotiz = "kein Fahrzeug fuer den Schuetzen"; return; }
        benutzt.Add(_dwSchuetze);
        var s = _entities[_dwSchuetze];
        s.Owner = ViewPlayer; s.Weapon = 28; s.Range = System.Math.Max(s.Range, 10);
        s.Target = -1; s.Path = null; s.Ordered = false; s.Cooldown = 0;
        if (s.AmmoMax > 0) s.Ammo = s.AmmoMax;
        ProbeVersetzen(_dwSchuetze, sc, sr);
        _dwArt = Simulation.DesignMath.SoundClass(WeaponRowOf(s.Weapon));
        _dwZielC = sc + 7; _dwZielR = sr;

        // die Opfer — das mit der kleinsten Verteidigung fuer A, damit der Kern traegt
        var kandidaten = new List<int>();
        for (int k = 0; k < _entities.Count; k++) if (Frei(k)) kandidaten.Add(k);
        kandidaten.Sort((a, b) => _entities[a].Defence.CompareTo(_entities[b].Defence));
        void Opfer(string titel, int dx, int dy, int besitzer)
        {
            int pos = kandidaten.FindIndex(k => !benutzt.Contains(k));
            if (pos < 0) { _dwNotiz += $" kein Opfer {titel};"; return; }
            int idx = kandidaten[pos];
            benutzt.Add(idx);
            var o = _entities[idx];
            o.Owner = besitzer >= 0 ? besitzer : o.Owner;
            o.Weapon = 0; o.Target = -1; o.Path = null; o.Ordered = false; o.Hp = 999;
            ProbeVersetzen(idx, _dwZielC + dx, _dwZielR + dy);
            int m = System.Math.Abs(dx) + System.Math.Abs(dy);
            int elev = ElevOf(o.Col, o.Row);
            int kern = 30 * (200 / (m + 1)) / 40 - (30 + o.Rating28 / 5) * (o.Defence + 2 * elev) / 50;
            _dwOpfer.Add(new DwOpfer { Titel = titel, Idx = idx, Dx = dx, Dy = dy, HpVorher = o.Hp, KernSoll = kern });
        }
        // ⚠ alle beim Spieler: ein Feind der KI faehrt vor oder nach der Welle
        // weg und wird beschossen (erster Lauf K6: A 68 Schaden, auf (38,21)).
        // Die Welle fragt kein Buendnis — B und A zeigen das zugleich.
        Opfer("A Ring 3 (3,0)     ", 3, 0, ViewPlayer);
        Opfer("B Ring 2 (0,2)     ", 0, 2, ViewPlayer);
        Opfer("C ausserhalb (6,0) ", 6, 0, ViewPlayer);

        _dwSchussTakt = _taktNr;
        _dwGeschossen = FireAtAusfuehren(_dwSchuetze, _dwZielC, _dwZielR);
        _dwGrund = FireAtGrund;
        s.Cooldown = 9999f;
        GD.Print($"druckwelle-check: Schuetze {_dwSchuetze} ({LabelOf(s)}) auf ({sc},{sr}), Art {_dwArt}, "
               + $"Ziel ({_dwZielC},{_dwZielR}), {(_dwGeschossen ? "geschossen" : "kein Schuss: " + _dwGrund)}{_dwNotiz}");
    }

    /// <summary>Fuer den Pruefstand: die Flughoehe des Schuetzen-Geschosses.</summary>
    private void DwFlugNotieren(in Projectile p)
    {
        if (!_dwCheckAn || p.Shooter != _dwSchuetze || !p.FlugBegonnen) return;
        _dwMaxHoehe = System.Math.Max(_dwMaxHoehe, p.Flughoehe);
        if (p.Steigzustand == 2 && _dwSturzAb < 0) _dwSturzAb = p.Flughoehe;
    }

    public string DruckwelleCheckLine()
    {
        var sb = new System.Text.StringBuilder("druckwelle-check\n");
        if (!_dwCheckAn) return sb.Append("  nicht gestartet — der Lauf sagt NICHTS").ToString();
        if (_dwSchuetze < 0 || _dwOpfer.Count < 3)
            return sb.Append($"  ⚠ nicht herstellbar ({_dwNotiz}) — sagt NICHTS\n  DURCHGEFALLEN").ToString();
        bool ok = true;
        void Zeile(string text, bool soll) { ok &= soll; sb.Append($"  {text}  {(soll ? "ja" : "NEIN")}\n"); }

        Zeile($"Schuss: {(_dwGeschossen ? "geschossen" : "kein Schuss — " + _dwGrund)}, Art {_dwArt} (Soll 7)",
              _dwGeschossen && _dwArt == 7);
        Zeile($"Flug: hoechste Hoehe {(_dwMaxHoehe == int.MinValue ? "—" : _dwMaxHoehe.ToString())} (Soll 150), "
            + $"Sturz ab {(_dwSturzAb < 0 ? "—" : _dwSturzAb.ToString())}", _dwMaxHoehe == 150 && _dwSturzAb >= 0);

        var anm = _dwAnmeldungen.FindIndex(a => a.C == _dwZielC && a.R == _dwZielR);
        string anmText = _dwAnmeldungen.Count == 0 ? "keine"
            : string.Join(", ", _dwAnmeldungen.ConvertAll(a => $"({a.C},{a.R}) nach {a.Takt - _dwSchussTakt} Takten"));
        Zeile($"Anmeldung: {anmText} (Soll ({_dwZielC},{_dwZielR}))", anm >= 0);

        // die Durchgaenge der Welle auf T
        int[] sollZahl = { 1, 8, 16, 20, 24, 40 };
        var durchgaenge = new List<(int Ring, int Takt, int N)>();
        if (anm >= 0)
        {
            int start = _dwAnmeldungen[anm].Takt, platz = _dwAnmeldungen[anm].Platz;
            foreach (var z in _dwZellen)
            {
                // dieselbe Tafelstelle, innerhalb der 18 Takte, die sechs Ringe brauchen koennen
                if (z.Platz != platz || z.Takt < start || z.Takt > start + 18) continue;
                int i = durchgaenge.FindIndex(d => d.Ring == z.Ring && d.Takt == z.Takt);
                if (i < 0) durchgaenge.Add((z.Ring, z.Takt, 1));
                else durchgaenge[i] = (z.Ring, z.Takt, durchgaenge[i].N + 1);
            }
            bool reihe = durchgaenge.Count == 6;
            for (int i = 0; i < durchgaenge.Count && reihe; i++)
            {
                reihe &= durchgaenge[i].Ring == i && durchgaenge[i].N == sollZahl[i];
                if (i > 0) reihe &= durchgaenge[i].Takt - durchgaenge[i - 1].Takt == 3;
            }
            int erst = durchgaenge.Count > 0 ? durchgaenge[0].Takt - start : -1;
            reihe &= erst is >= 0 and <= 3 && durchgaenge.Count > 0 && durchgaenge[0].Takt % 3 == 0;
            int summe = 0; foreach (var d in durchgaenge) summe += d.N;
            Zeile($"Wellen: {string.Join(" / ", durchgaenge.ConvertAll(d => $"Ring {d.Ring}: {d.N} Zellen @+{d.Takt - start}"))}, "
                + $"Summe {summe} (Soll 1/8/16/20/24/40 im Abstand 3, erster Durchgang 0..3 Takte nach der Anmeldung, 109)",
                  reihe && summe == 109);
        }
        else Zeile("Wellen: keine Anmeldung auf T — keine Durchgaenge", false);

        // das Nullmodell der Rundung, gerechnet: mit Math.Round statt Abrunden
        int trunc = 0, rund = 0;
        for (int dy = -6; dy <= 6; dy++)
            for (int dx = -6; dx <= 6; dx++)
            {
                double d = System.Math.Sqrt(dx * dx + dy * dy);
                if ((int)d <= 5) trunc++;
                if ((int)System.Math.Round(d) <= 5) rund++;
            }
        sb.Append($"  Rundung gerechnet: Abrunden {trunc} Zellen, Runden {rund} Zellen\n");

        foreach (var o in _dwOpfer)
        {
            var e = _entities[o.Idx];
            int tc = _dwZielC + o.Dx, tr = _dwZielR + o.Dy;
            bool getroffen = _dwZellen.Exists(z => z.C == tc && z.R == tr);
            int schaden = _dwSchaden.GetValueOrDefault(o.Idx);
            // was der Einheitenarm bei Rang 0 zulaesst (@0x40CEB4..0x40CEE3)
            var erlaubt = new HashSet<int>();
            for (int w = -4; w <= 4; w++)
            {
                int v = o.KernSoll + w;
                if (v >= 1) erlaubt.Add(v);
                else if (v <= -2) erlaubt.Add(0);
                else for (int q = 0; q <= 3; q++) erlaubt.Add(q);
            }
            bool platz = e.Col == tc && e.Row == tr;
            bool soll = o.Titel[0] switch
            {
                'A' => getroffen && erlaubt.Contains(schaden) && !erlaubt.Contains(0),
                'B' => getroffen && schaden > 0,
                _ => !getroffen && schaden == 0,
            };
            Zeile($"{o.Titel}: {(getroffen ? "getroffen" : "nicht getroffen")}, Schaden {schaden} "
                + $"(Kern {o.KernSoll} ±4, Vert {e.Defence}, Rang {e.Rating28}){(platz ? "" : $" ⚠ steht jetzt auf ({e.Col},{e.Row})")}", soll);
        }
        sb.Append($"  Tafel: {DwAngemeldet} angemeldet, {DwZellen} Zellen, {DwVoll}x voll; "
                + $"Gegenschalter --art7-einzeltreffer: {Art7Einzeltreffer}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
