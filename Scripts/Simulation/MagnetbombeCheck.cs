namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <b>--magnetbombe-check</b> (30.10.2026, bug-710..713), kopflos auf NET07. Gezaehlt wird die
/// ANKUNFT: Klaenge ueber den Horcher, Treffer ueber <c>_trefferLog</c>/<c>_mbLog</c>.
/// <list type="bullet">
/// <item>F0 Startziel: alle Stahlsucher tragen +0x40 aus der Karte; im ersten Takt fahren sie
///   zur Zelle von Einheit 0. Nullmodell <c>--seeker-ziel0-leer</c>: keiner.</item>
/// <item>F1 Suche: Probe-Bombe B ohne Ziel, feindliches Fahrzeug X 8 Zellen weit: Fund genau bei
///   (Uhr+Platz)%100 == 11, Ziel = erstes feindliches Fahrzeug der Spirale. Nullmodell <c>--seeker-alt</c>.</item>
/// <item>F2 Hinfahrt und Klang 37: B faehrt los; Klang 37 genau in den Takten mit Ziel und
///   (Uhr+Platz)%40 != 0; B kommt neben X an und zuendet. Nullmodell <c>--seeker-alt</c>: 0.</item>
/// <item>F3 Naehezuendung: B2, X2 feindlich (1,0), F eigen (−1,−1), Y feindlich (2,0): Treffer mit
///   Angriff 250 auf X2, F und B2 selbst, KEINER auf Y; Klang 36. Nullmodell <c>--magnetbombe-alt</c>.</item>
/// <item>F4 Befehl 28: B3 neben einem Gebaeude, kein Feind daneben; Menuezeile 0x23 -> Ring ->
///   Gebaeude Angriff 20. Nullmodell <c>--magnetbombe-alt</c>.</item>
/// <item>F5 Tod: B4 stirbt, der eigene Nachbar bekommt 250. Nullmodell <c>--magnetbombe-alt</c>.</item>
/// <item>F6 Antimagnetiker: fahrender feindlicher Stahlsucher E in 8 Zellen: angehalten bei
///   (Uhr+Platz)%20 == 9, Suchziel 0xFFFF, Klang 47 einmal. Nullmodell <c>--antimagnet-alt</c>.</item>
/// <item>F7 Membrandroid: feindlicher Soldat NEBEN M zuendet nicht; IN der Zelle: Soldat Angriff
///   100, Fahrzeug daneben 30, M fort ohne Leiche, Klang 36. Nullmodell <c>--membrandroid-alt</c>.</item>
/// </list>
/// ⚠ EINGRIFFE: Einheiten werden versetzt, festgeheftet, mit 30000 TP versehen; ungenutzte
/// Stahlsucher werden fuer die Dauer des Pruefstands entschaerft (+0x0D/+0x0F := 0); fuer F6/F7
/// werden Ausruestung bzw. Waffe umgeschrieben (+0x0E := 0x4C / 0xC5); fehlende Bomben werden
/// aus fremden Stahlsuchern genommen (Besitzer umgeschrieben).
/// </summary>
public partial class MapEntityLayer
{
    public string MagnetbombeCheck()
    {
        var sb = new StringBuilder("magnetbombe-check"
            + (MagnetbombeAlt ? " — NULLMODELL --magnetbombe-alt" : "")
            + (SeekerAlt ? " — NULLMODELL --seeker-alt" : "")
            + (AntimagnetAlt ? " — NULLMODELL --antimagnet-alt" : "")
            + (MembrandroidAlt ? " — NULLMODELL --membrandroid-alt" : "")
            + (SeekerZiel0Leer ? " — NULLMODELL --seeker-ziel0-leer" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL: keine Karte").ToString();
        var klaenge = new List<(int Slot, float C, float R, int Takt)>();
        var vorher = Audio.SoundBankPlayer.Horcher;
        Audio.SoundBankPlayer.Horcher = (s, c, r) => { vorher?.Invoke(s, c, r); klaenge.Add((s, c, r, _taktNr)); };
        _sprengLog = new List<(int C, int R, string Was, int Schaden, bool Tot)>();
        _mbLog = new List<(int C, int R, int Angriff, string Grund)>();
        var urteile = new List<(string Teil, bool Ok)>();
        try { MbCheckLauf(sb, klaenge, urteile); }
        finally
        {
            Audio.SoundBankPlayer.Horcher = vorher;
            _sprengLog = null; _mbLog = null; _mbProbe = -1;
        }
        bool alle = urteile.Count > 0 && urteile.All(u => u.Ok);
        sb.AppendLine("  Teile " + string.Join(" ", urteile.Select(u => $"{u.Teil}={(u.Ok ? "gruen" : "ROT")}")));
        sb.AppendLine("  " + MagnetZeile());
        sb.Append($"  Ergebnis: {(alle ? "BESTANDEN" : "DURCHGEFALLEN")} ({urteile.Count(u => u.Ok)} gruen, {urteile.Count(u => !u.Ok)} rot)");
        return sb.ToString();
    }

    private void MbCheckLauf(StringBuilder sb, List<(int Slot, float C, float R, int Takt)> klaenge, List<(string, bool)> urteile)
    {
        void Urteil(string teil, bool ok, string was)
        {
            urteile.Add((teil, ok));
            sb.AppendLine($"  {(ok ? "ok  " : "ROT ")} {teil} {was}");
        }

        // ---- F0 Startziel aus der Karte ---------------------------------------------
        var bomben = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Comp0F == StahlsucherFahrwerk && e.Infantry < 0) bomben.Add(i);
        }
        int null0 = MbPlatzIndex(0);
        var u0 = null0 >= 0 ? _entities[null0] : null;
        sb.AppendLine($"  Stahlsucher {bomben.Count}: " + string.Join(" ", bomben.Select(b =>
            $"P{_entities[b].Owner}/{_entities[b].Slot}@({_entities[b].Col},{_entities[b].Row}) Ziel {(_entities[b].SuchZiel == 0xFFFF ? "-" : _entities[b].SuchZiel.ToString())} "
            + $"Waffe {_entities[b].Comp0D} TP {_entities[b].Hp} Reichw {RangeOf(_entities[b]):0.0}")));
        sb.AppendLine($"  Einheit 0: {(u0 == null ? "keine" : $"»{LabelOf(u0)}« Platz {u0.Slot}, Spieler {u0.Owner}, ({u0.Col},{u0.Row}), Gattung {u0.GameUnitType}, Waffe {u0.Comp0D}, TP {u0.Hp}")}");
        if (bomben.Count < 6) { sb.AppendLine("  KEIN URTEIL: weniger als 6 Stahlsucher"); return; }
        int mitNull = bomben.Count(b => _entities[b].SuchZiel == 0);
        int zuNull = 0;
        if (u0 != null)
        {
            SimTickFuerProbe();
            foreach (int b in bomben)
            {
                var e = _entities[b];
                if (e.Path != null && Mathf.Abs(e.Goal.X - u0.Col) <= 2 && Mathf.Abs(e.Goal.Y - u0.Row) <= 2) zuNull++;
            }
        }
        bool f0 = mitNull == bomben.Count && zuNull >= bomben.Count - 2;
        Urteil("F0", f0,
            $"Startziel: {mitNull}/{bomben.Count} tragen +0x40 = 0, nach 1 Takt fahren {zuNull} zur Zelle von Einheit 0 — Soll "
            + $"alle und >= {bomben.Count - 2} (Nullmodelle --seeker-alt: keiner faehrt; --seeker-ziel0-leer: keiner traegt 0) -> {(f0 ? "ja" : "NEIN")}");

        // ---- Herrichten ----------------------------------------------------------------
        // Alle Stahlsucher entschaerfen; die Faelle nehmen sich ihre Bombe einzeln.
        var merk = new Dictionary<int, (int D, int F)>();
        foreach (int b in bomben) { merk[b] = (_entities[b].Comp0D, _entities[b].Comp0F); _entities[b].Comp0D = 0; _entities[b].Comp0F = 0; }
        int nimm = 0;
        int Bombe(int owner)
        {
            int b = bomben[nimm++ % bomben.Count];
            var e = _entities[b];
            (e.Comp0D, e.Comp0F) = merk[b];
            e.Owner = owner;                                     // ⚠ EINGRIFF
            e.SuchZiel = 0xFFFF; e.Path = null; e.Target = -1; e.Orders.Clear(); e.Ukol = 0; e.Ordered = false;
            e.HpMax = Mathf.Max(e.HpMax, 1); e.Hp = e.HpMax;
            return b;
        }
        void Entschaerfen(int b) { _entities[b].Comp0D = 0; _entities[b].Comp0F = 0; _entities[b].Path = null; }
        int fremd = -1;
        for (int p = 0; p < 8 && fremd < 0; p++) if (p != ViewPlayer && MineFeindlich(ViewPlayer, p)) fremd = p;
        if (fremd < 0) { sb.AppendLine("  KEIN URTEIL: kein feindlicher Spieler"); return; }
        var fahrzeuge = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding || !e.Mobile || e.Infantry >= 0 || Untergestellt(e)) continue;
            if (e.GameUnitType != 0 || bomben.Contains(i) || e.Comp0D == MagnetbombeWaffe || e.Slot == 0) continue;   // Einheit 0 bleibt, wo sie ist
            fahrzeuge.Add(i);
        }
        int nf = 0;
        int Fahrzeug(int owner)
        {
            int k = fahrzeuge[nf++ % fahrzeuge.Count];
            var e = _entities[k];
            e.Owner = owner;                                     // ⚠ EINGRIFF
            e.HpMax = e.Hp = 30000; e.Path = null; e.Target = -1; e.Orders.Clear(); e.Cooldown = 99f;
            return k;
        }
        if (fahrzeuge.Count < 8) { sb.AppendLine("  KEIN URTEIL: zu wenige Fahrzeuge"); return; }
        void Stellen(int idx, int c, int r)
        {
            var e = _entities[idx];
            e.Path = null; e.Orders.Clear(); e.Target = -1; e.Ordered = false; e.Schussziel = -1; e.RestZiel = null;
            ProbeVersetzen(idx, c, r);
        }
        var geheftet = new List<(int Idx, int C, int R)>();
        void Heften()
        {
            foreach (var (k, c, r) in geheftet)
            {
                var e = _entities[k];
                if (e.Dead) continue;
                if (e.Col != c || e.Row != r) ProbeVersetzen(k, c, r);
                e.Path = null; e.Target = -1; e.Cooldown = 99f;
            }
        }
        void Takte(int n) { for (int t = 0; t < n; t++) { Heften(); SimTickFuerProbe(); } }
        (int C, int R) Flaeche(int w, int h)
        {
            var mv = Simulation.NavGrid.MoveClass.Vehicle;
            for (int r = 4; r < _nav!.Height - h - 4; r += 2)
                for (int c = 4; c < _nav.Width - w - 4; c += 2)
                {
                    bool frei = true;
                    for (int dy = 0; dy < h && frei; dy++)
                        for (int dx = 0; dx < w && frei; dx++)
                            frei = _nav.CanEnter(c + dx, r + dy, mv) && _nav.OccupantAt(c + dx, r + dy) < 0
                                   && GebaeudeAufZelle(c + dx, r + dy) < 0 && !MbFussvolkAuf(c + dx, r + dy)
                                   && _nav.ElevAt(c + dx, r + dy) == _nav.ElevAt(c, r);
                    if (!frei) continue;
                    // kein fremdes Fahrzeug in 33 Zellen (die Spirale hat 3000 Schritte)
                    bool ruhig = true;
                    foreach (var e in _entities)
                        if (!e.Dead && !e.IsProp && !e.IsBuilding && Mathf.Abs(e.Col - c - w / 2) < w / 2 + 33
                            && Mathf.Abs(e.Row - r - h / 2) < h / 2 + 33 && !geheftet.Any(g => _entities[g.Idx] == e)) { ruhig = false; break; }
                    if (ruhig) return (c, r);
                }
            for (int r = 4; r < _nav.Height - h - 4; r += 2)
                for (int c = 4; c < _nav.Width - w - 4; c += 2)
                {
                    bool frei = true;
                    for (int dy = 0; dy < h && frei; dy++)
                        for (int dx = 0; dx < w && frei; dx++)
                            frei = _nav.CanEnter(c + dx, r + dy, Simulation.NavGrid.MoveClass.Vehicle) && _nav.OccupantAt(c + dx, r + dy) < 0
                                   && GebaeudeAufZelle(c + dx, r + dy) < 0 && !MbFussvolkAuf(c + dx, r + dy);
                    if (frei) return (c, r);
                }
            return (-1, -1);
        }
        int Treffer(Entity v, int angriff) => _trefferLog.Count(t => t.Opfer == v && t.Angriff == angriff);
        int Klang(int nr, int ab) => klaenge.Count(k => k.Slot == nr && k.Takt > ab);

        // ---- F1/F2 Suche, Hinfahrt, Klang 37, Zuendung ---------------------------------
        {
            var (sc, sr) = Flaeche(14, 3);
            if (sc < 0) { sb.AppendLine("  KEIN URTEIL F1/F2: keine freie Flaeche 14x3"); }
            else
            {
                int b = Bombe(ViewPlayer), x = Fahrzeug(fremd);
                var B = _entities[b]; var X = _entities[x];
                Stellen(b, sc, sr + 1); Stellen(x, sc + 8, sr + 1);
                geheftet.Clear(); geheftet.Add((x, sc + 8, sr + 1));
                _trefferLog.Clear(); _mbProbe = b; _mbProbeFund.Clear();
                int t0 = _taktNr, k0 = klaenge.Count;
                int erwartet37 = 0, fundTakt = -1, losTakt = -1, zuendTakt = -1;
                int z0 = MbZuendungen;
                for (int t = 0; t < 600 && !B.Dead; t++)
                {
                    int T = _taktNr + 1;
                    bool hatZiel = B.SuchZiel != 0xFFFF;
                    bool ziel = hatZiel && !X.Dead;
                    Heften();
                    SimTickFuerProbe();
                    if (ziel && (T + B.Slot) % 40 != 0 && !MagnetbombeAlt && !SeekerAlt) erwartet37++;
                    if (fundTakt < 0 && _mbProbeFund.Count > 0) fundTakt = _mbProbeFund[0].Takt;
                    if (losTakt < 0 && B.Path != null) losTakt = _taktNr;
                    if (zuendTakt < 0 && MbZuendungen > z0) { zuendTakt = _taktNr; break; }
                }
                int gehoert37 = klaenge.Skip(k0).Count(k => k.Slot == Audio.GameSounds.TargetFound);
                int zielFund = _mbProbeFund.Count > 0 ? _mbProbeFund[0].Ziel : -1;
                bool f1 = fundTakt >= 0 && (fundTakt + B.Slot) % 100 == 11 && zielFund == X.Slot && fundTakt - t0 <= 100;
                Urteil("F1", f1, $"Suche: B Platz {B.Slot} bei ({sc},{sr + 1}), X »{LabelOf(X)}« P{X.Owner} 8 Zellen weit: Fund im Takt "
                    + $"{(fundTakt < 0 ? "-" : $"{fundTakt} ((Uhr+Platz)%100 = {(fundTakt + B.Slot) % 100})")}, Ziel {zielFund} (X = {X.Slot}) "
                    + $"— Soll 11 und X binnen 100 Takten (Nullmodell --seeker-alt: kein Fund) -> {(f1 ? "ja" : "NEIN")}");
                bool f2 = losTakt >= 0 && losTakt - fundTakt <= 1 && erwartet37 > 0 && gehoert37 == erwartet37
                          && zuendTakt >= 0 && Treffer(X, 250) >= 1;
                Urteil("F2", f2, $"Hinfahrt: los im Takt {losTakt} (Fund {fundTakt}), Klang 37 gehoert {gehoert37} / erwartet {erwartet37} "
                    + $"(Ziel gehalten und (Uhr+Platz)%40 != 0), Zuendung im Takt {zuendTakt}, X mit Angriff 250 getroffen {Treffer(X, 250)}x, "
                    + $"Wuerfelvergessen {MbVergessenWurf} — Soll los <= 1 Takt nach Fund, gleich viele, Zuendung (Nullmodell --seeker-alt: 0) -> {(f2 ? "ja" : "NEIN")}");
                _mbProbe = -1;
                Entschaerfen(b);
                geheftet.Clear();
            }
        }

        // ---- F3 Naehezuendung und Radius ------------------------------------------------
        {
            var (cx, cy) = Flaeche(7, 5);
            if (cx < 0) sb.AppendLine("  KEIN URTEIL F3: keine freie Flaeche");
            else
            {
                cx += 2; cy += 2;
                int b = Bombe(ViewPlayer), x = Fahrzeug(fremd), f = Fahrzeug(ViewPlayer), y = Fahrzeug(fremd);
                var B = _entities[b]; var X = _entities[x]; var F = _entities[f]; var Y = _entities[y];
                geheftet.Clear();
                Stellen(b, cx, cy); geheftet.Add((b, cx, cy));
                Stellen(x, cx + 1, cy); geheftet.Add((x, cx + 1, cy));
                Stellen(f, cx - 1, cy - 1); geheftet.Add((f, cx - 1, cy - 1));
                Stellen(y, cx + 3, cy); geheftet.Add((y, cx + 3, cy));
                // Y 3 Zellen weit (eine leere Zelle dazwischen): ausserhalb der 8 Nachbarn
                _trefferLog.Clear(); _mbLog!.Clear();
                int k0 = _taktNr, hpB = B.Hp, sp0 = MbSprengungen;
                Takte(1);
                int tx = Treffer(X, 250), tf = Treffer(F, 250), ty = _trefferLog.Count(t => t.Opfer == Y), tb = Treffer(B, 250);
                int k36 = Klang(SprengKlang, k0);
                bool f3 = tx >= 1 && tf >= 1 && ty == 0 && tb >= 1 && k36 >= 1 && tx == tf && k36 == MbSprengungen - sp0;
                var sx = _trefferLog.Where(t => t.Opfer == X).Select(t => t.Schaden).ToList();
                Urteil("F3", f3, $"Naehezuendung: X (1,0) feindlich {tx}x / F (−1,−1) EIGEN {tf}x / B selbst {tb}x mit Angriff 250, "
                    + $"Y (3,0) {ty}x, Klang 36 {k36}x bei {MbSprengungen - sp0} Sprengungen; Schaden an X {string.Join("+", sx)} "
                    + $"(Verteidigung {X.Defence}), B TP {hpB} -> {B.Hp}{(B.Dead ? " TOT" : "")} — Soll X=F>=1, B>=1, Y 0 "
                    + $"(Nullmodell --magnetbombe-alt: 0) -> {(f3 ? "ja" : "NEIN")}");
                if (B.Dead && MbSprengungen - sp0 >= 2)
                    sb.AppendLine($"       ⚠ B starb am eigenen Treffer: der Tod (0x40B7DA) sprengt ein ZWEITES Mal — X {tx}x, wie im Original (Tod nach ZBRAN)");
                Entschaerfen(b);
                geheftet.Clear();
            }
        }

        // ---- F4 Befehl 28 ueber die Menuezeile ------------------------------------------
        {
            int bc = -1, br = -1;
            for (int gi = 0; gi < _entities.Count && bc < 0; gi++)
            {
                var g = _entities[gi];
                if (!g.IsBuilding || g.Dead || g.IsProp) continue;
                for (int dx = -1; dx <= Mathf.Max(1, g.FootW) && bc < 0; dx++)
                {
                    int c = g.Col + dx, r = g.Row + Mathf.Max(1, g.FootH);
                    if (!_nav.InBounds(c, r) || !_nav.CanEnter(c, r, Simulation.NavGrid.MoveClass.Vehicle) || _nav.OccupantAt(c, r) >= 0) continue;
                    if (GebaeudeAufZelle(c, r) >= 0 || MbFussvolkAuf(c, r)) continue;
                    bool feind = false, geb = false;
                    foreach (var (nx, ny) in MagnetNachbarn)
                    {
                        if (MbFahrzeugAuf(c + nx, r + ny) >= 0) feind = true;
                        if (GebaeudeAufZelle(c + nx, r + ny) == gi) geb = true;
                    }
                    if (!feind && geb) { bc = c; br = r; }
                }
            }
            if (bc < 0) sb.AppendLine("  KEIN URTEIL F4: kein Gebaeude mit freier Nachbarzelle");
            else
            {
                int b = Bombe(ViewPlayer);
                var B = _entities[b];
                B.Weapon = 0x27;                                   // Aufsatz der M-Bombe (Karte: 39)
                geheftet.Clear();
                Stellen(b, bc, br); geheftet.Add((b, bc, br));
                _mbLog!.Clear(); _trefferLog.Clear(); _sprengLog!.Clear();
                WaehleFuerProbe(b);
                int k0 = _taktNr - 1, b28 = MbBefehl28;            // der Ring kann im selben Takt wirken
                int code = MenueCodes(b)[0];
                string antwort = MenueAktion(code);
                Takte(2);
                int geb20 = _mbLog.Count(m => m.Angriff == 20);
                int gebLog = _sprengLog.Count(s => s.Was.StartsWith("Gebaeude"));
                bool f4 = code == CodeMagnetSprengung && MbBefehl28 - b28 == 1 && geb20 >= 1 && gebLog >= 1 && Klang(SprengKlang, k0) >= 1;
                Urteil("F4", f4, $"Befehl 28: Menuezeile c1 = 0x{code:X2}, Antwort »{antwort}«, Befehl angenommen {MbBefehl28 - b28}x, "
                    + $"Gebaeudezellen mit Angriff 20: {geb20} (getroffen {gebLog}), Klang 36 {Klang(SprengKlang, k0)}x "
                    + $"— Soll 0x23, 1, >=1 (Nullmodell --magnetbombe-alt: »noch nicht gebaut«, 0) -> {(f4 ? "ja" : "NEIN")}");
                Entschaerfen(b);
                geheftet.Clear();
            }
        }

        // ---- F5 Sprengung beim Tod ------------------------------------------------------
        {
            var (cx, cy) = Flaeche(5, 3);
            if (cx < 0) sb.AppendLine("  KEIN URTEIL F5: keine freie Flaeche");
            else
            {
                cx += 1; cy += 1;
                int b = Bombe(ViewPlayer), f = Fahrzeug(ViewPlayer);
                var B = _entities[b]; var F = _entities[f];
                geheftet.Clear();
                Stellen(b, cx, cy); geheftet.Add((b, cx, cy));
                Stellen(f, cx + 1, cy); geheftet.Add((f, cx + 1, cy));
                _trefferLog.Clear();
                int k0 = _taktNr, tod0 = MbTodSprengungen;
                Kill(b, B, -1, "Pruefstand --magnetbombe-check F5");
                int tf = Treffer(F, 250);
                bool f5 = MbTodSprengungen - tod0 == 1 && tf == 1 && Klang(SprengKlang, k0 - 1) >= 1;
                Urteil("F5", f5, $"Tod: Sprengungen beim Tod {MbTodSprengungen - tod0}, eigener Nachbar mit Angriff 250 {tf}x, Klang 36 "
                    + $"{Klang(SprengKlang, k0 - 1)}x — Soll 1/1/>=1 (Nullmodell --magnetbombe-alt: 0) -> {(f5 ? "ja" : "NEIN")}");
                geheftet.Clear();
            }
        }

        // ---- F6 Antimagnetiker ----------------------------------------------------------
        {
            var (ac, ar) = Flaeche(16, 3);
            if (ac < 0) sb.AppendLine("  KEIN URTEIL F6: keine freie Flaeche");
            else
            {
                ar += 1;
                int a = Fahrzeug(ViewPlayer), e = Bombe(fremd);
                var A = _entities[a]; var E = _entities[e];
                int teil = A.Part;
                A.Part = AntimagnetTeil;                           // ⚠ EINGRIFF: Ausruestung 76
                geheftet.Clear();
                Stellen(a, ac, ar); geheftet.Add((a, ac, ar));
                Stellen(e, ac + 8, ar);
                AiWalkTo(e, new Vector2I(ac + 15, ar));             // UKOL 2: faehrt weg
                E.SuchZiel = A.Slot;                               // ein Ziel, das gleich verworfen werden muss
                int k0 = _taktNr, halt0 = MbAntimagnetHalte, haltTakt = -1;
                for (int t = 0; t < 25 && haltTakt < 0; t++)
                {
                    Heften(); SimTickFuerProbe();
                    if (MbAntimagnetHalte > halt0) haltTakt = _taktNr;
                }
                int k47 = Klang(AntimagnetKlang, k0);
                bool f6 = haltTakt >= 0 && (haltTakt + A.Slot) % 20 == 9 && E.SuchZiel == 0xFFFF && k47 == 1
                          && (E.Path == null || E.Path.Count == 0 || _taktNr == haltTakt);
                Urteil("F6", f6, $"Antimagnetiker Platz {A.Slot}: feindlicher Stahlsucher P{E.Owner}/{E.Slot} 8 Zellen weit, faehrt: angehalten im Takt "
                    + $"{(haltTakt < 0 ? "-" : $"{haltTakt} ((Uhr+Platz)%20 = {(haltTakt + A.Slot) % 20})")}, Weg {(E.Path == null ? "keiner" : "noch da")}, "
                    + $"Suchziel {(E.SuchZiel == 0xFFFF ? "0xFFFF" : E.SuchZiel.ToString())}, Klang 47 {k47}x "
                    + $"— Soll 9, keiner, 0xFFFF, 1 (Nullmodell --antimagnet-alt: faehrt weiter, 0) -> {(f6 ? "ja" : "NEIN")}");
                A.Part = teil;
                Entschaerfen(e);
                geheftet.Clear();
            }
        }

        // ---- F7 Membrandroid ------------------------------------------------------------
        {
            int m = -1, s = -1;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry < 0 || Untergestellt(e)) continue;
                if (m < 0) m = i; else if (s < 0) { s = i; break; }
            }
            var (cx, cy) = Flaeche(5, 4);
            if (m < 0 || s < 0 || cx < 0) sb.AppendLine("  KEIN URTEIL F7: keine zwei Fusssoldaten oder keine Flaeche");
            else
            {
                cx += 2; cy += 1;
                var M = _entities[m]; var S = _entities[s];
                int teil = M.Part;
                M.Owner = ViewPlayer; S.Owner = fremd;            // ⚠ EINGRIFF
                M.Part = MembranTeil;                              // ⚠ EINGRIFF: Waffe 197
                S.HpMax = S.Hp = 30000; S.Cooldown = 99f; M.Cooldown = 99f;
                int v = Fahrzeug(ViewPlayer);
                var V = _entities[v];
                geheftet.Clear();
                Stellen(m, cx, cy); geheftet.Add((m, cx, cy));
                Stellen(s, cx + 1, cy); geheftet.Add((s, cx + 1, cy));
                Stellen(v, cx, cy + 1); geheftet.Add((v, cx, cy + 1));
                _trefferLog.Clear();
                int z0 = MbMembranZuendungen;
                Takte(5);
                int nebenan = MbMembranZuendungen - z0;
                geheftet.RemoveAll(g => g.Idx == s);
                Stellen(s, cx, cy); geheftet.Add((s, cx, cy));
                int k0 = _taktNr;
                Takte(1);
                int zuendung = MbMembranZuendungen - z0 - nebenan;
                int ts = Treffer(S, 100), tv = Treffer(V, 30);
                bool f7 = nebenan == 0 && zuendung == 1 && ts >= 1 && tv >= 1 && M.Dead && M.OhneLeiche && Klang(SprengKlang, k0) >= 1;
                Urteil("F7", f7, $"Membrandroid Platz {M.Slot}: feindlicher Soldat daneben 5 Takte -> {nebenan} Zuendungen; in der Zelle -> {zuendung}, "
                    + $"Soldat Angriff 100 {ts}x, Fahrzeug daneben Angriff 30 {tv}x, M {(M.Dead ? (M.OhneLeiche ? "fort ohne Leiche" : "tot mit Leiche") : "LEBT")}, "
                    + $"Klang 36 {Klang(SprengKlang, k0)}x — Soll 0, 1, >=1, >=1, fort (Nullmodell --membrandroid-alt: 0) -> {(f7 ? "ja" : "NEIN")}");
                if (!M.Dead) M.Part = teil;
                geheftet.Clear();
            }
        }
        foreach (var (b, w) in merk) if (!_entities[b].Dead && _entities[b].Comp0F == 0) (_entities[b].Comp0D, _entities[b].Comp0F) = w;
    }
}
