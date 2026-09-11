namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--ueberfahren-check</c> und <c>--sprengung-check</c> (11.09.2026) —
/// siehe Simulation/Ueberfahren.cs.
///
/// <para><b>--ueberfahren-check</b> stellt FUENF Faelle her, jeder auf einem
/// eigenen freien Streifen der Karte: Fahrer auf (c, r), Fusssoldat auf
/// (c+3, r), Ziel (c+6, r). Alle Beteiligten ohne Waffe, damit der Lauf etwas
/// ueber das Fahren sagt und nicht ueber das Schiessen (derselbe Grund wie in
/// <c>DebugDemoCrush</c>).</para>
/// <list type="bullet">
/// <item>A Rad/Kette gegen Feind — Soll: tot durch Treffer, Halten 8…11 Takte, Fahrer durch</item>
/// <item>B Rad/Kette gegen Freund — Soll: lebt, der Fahrer hat gebeten</item>
/// <item>C Hover gegen Feind — Soll: lebt, blockiert</item>
/// <item>D Walker gegen Feind — Soll: lebt, blockiert</item>
/// <item>E Fussvolk gegen Feind — Soll: beide leben, der Fahrer geht durch</item>
/// </list>
/// <para>⚠ EINGRIFFE, alle genannt: Einheiten werden VERSETZT, entwaffnet, und
/// fuer C/D bekommt ein Fahrzeug die Fortbewegungsart Hover bzw. Walker
/// (die Weiche fragt genau diese Klasse). Nullmodelle:
/// <c>--ueberfahren-alle</c> (C, D, E sterben), <c>--ueberfahren-loeschen</c>
/// (A ohne Halten).</para>
///
/// <para><b>--sprengung-check</b>: ein Fahrzeug in einer freien Gegend, ein
/// Fusssoldat auf Nachbar 0 (0,1), ein Fahrzeug auf Nachbar 6 (1,0); das
/// mittlere wird zerstoert. Soll: acht Zellen in der Tafelreihenfolge
/// angefasst, beide Nachbarn getroffen, der Schaden ist genau der Abzug an
/// ihren Trefferpunkten. Nullmodell <c>--sprengung-ohne-nachbarn</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private sealed class UeberfahrFall
    {
        public string Name = "";
        public int Fahrer = -1, Fuss = -1, Col, Row;
        public Simulation.NavGrid.MoveClass? Klasse;
        public string Weg = "", Eingriff = "";
    }

    private bool _uCheckAn;

    /// <summary><c>--ueberfahren-faelle=…</c> — welche der Faelle A..E
    /// hergestellt werden (Standard alle).</summary>
    public static string UeberfahrenFaelle = "ABCDE";

    private readonly List<UeberfahrFall> _uFaelle = new();
    private readonly Dictionary<int, int[]> _uZaehler = new();   // [GiveWay, Blocked, Free]
    private readonly HashSet<int> _uZeilen = new();

    public void UeberfahrenCheckStart()
    {
        if (_nav == null) return;
        _uCheckAn = true;
        Fahrgruende = true;                  // damit die Zeile sagt, WARUM ein Fahrer steht
        _uFaelle.Clear(); _uZaehler.Clear(); _uZeilen.Clear(); _haltGemessen.Clear();
        var benutzt = new HashSet<int>();

        // ⚠ Nur, wer frei auf der Karte steht: nichts aus einem Transporter
        // oder Gebaeude (UKOL >= 50), sonst versetzt der Pruefstand eine
        // Ladung.
        bool Frei(int i)
        {
            var e = _entities[i];
            return !benutzt.Contains(i) && !e.Dead && !e.IsProp && !e.IsBuilding && e.Mobile
                   && e.Ukol < 50 && !Untergestellt(e);
        }
        bool IstRadKette(Entity e) => e.Infantry < 0 && e.GameUnitType == 0
                                      && e.Move == Simulation.NavGrid.MoveClass.Vehicle;
        bool IstFussvolk(Entity e) => e.Infantry >= 0;

        // ⚠ 11.09.2026 — FAHRER UND SOLDAT ALS PAAR suchen. Der erste Anlauf
        // nahm den ersten Fahrer und suchte dann einen Feind — auf K7 (1⇄2,
        // 2⇄3 verbuendet) gab es fuer drei der fuenf Faelle keinen, und der Lauf
        // sagte fuer sie NICHTS.
        (int, int) Paar(System.Func<Entity, bool> fahrerArt, bool feind)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                if (!Frei(i) || !fahrerArt(_entities[i])) continue;
                for (int j = 0; j < _entities.Count; j++)
                    if (j != i && Frei(j) && IstFussvolk(_entities[j])
                        && IsHostile(_entities[i], _entities[j]) == feind)
                        return (i, j);
            }
            return (-1, -1);
        }

        void Fall(string name, System.Func<Entity, bool> fahrerArt, bool feind,
                  Simulation.NavGrid.MoveClass? klasse, bool besitzerTauschen = false,
                  bool fahrerZuSpieler = false)
        {
            // --ueberfahren-faelle=ABCD: nur diese Faelle — sie konkurrieren
            // auf einer Karte um dieselben wenigen frei stehenden Soldaten
            if (!UeberfahrenFaelle.Contains(name[0])) return;
            var (fahrer, fussIdx) = Paar(fahrerArt, feind);
            string eingriff = "";
            // ⚠ 11.09.2026 — FALL E GIBT ES AUF KEINER KARTE VON SELBST: auf K4,
            // K5, K6 und K7 steht beim Start kein Fusssoldat einem FEINDLICHEN
            // frei gegenueber. Dann wird der Besitzer eines Soldaten umgesetzt —
            // ein Eingriff, und die Zeile sagt es.
            if (fahrer < 0 && besitzerTauschen)
                for (int i = 0; i < _entities.Count && fahrer < 0; i++)
                {
                    if (!Frei(i) || !fahrerArt(_entities[i])) continue;
                    for (int j = 0; j < _entities.Count && fahrer < 0; j++)
                    {
                        if (j == i || !Frei(j) || !IstFussvolk(_entities[j])) continue;
                        var fj = _entities[j];
                        int alt = fj.Owner;
                        for (int p = 0; p <= 7 && fahrer < 0; p++)
                        {
                            fj.Owner = p;
                            if (IsHostile(_entities[i], fj) == feind)
                            { fahrer = i; fussIdx = j; eingriff = $"⚠ EINGRIFF Besitzer {alt} -> {p}"; }
                        }
                        if (fahrer < 0) fj.Owner = alt;
                    }
                }
            // ⚠ und fuer E: der Fahrer wird dem SPIELER zugeschlagen, damit keine
            // KI seinen Weg abraeumt; passt die Feindschaft danach nicht mehr,
            // bekommt das Opfer einen passenden Besitzer. Beides ein Eingriff.
            if (fahrer >= 0 && fahrerZuSpieler && _entities[fahrer].Owner != ViewPlayer)
            {
                var fa0 = _entities[fahrer];
                var fu0 = _entities[fussIdx];
                eingriff = $"⚠ EINGRIFF Fahrer-Besitzer {fa0.Owner} -> {ViewPlayer}"
                         + (eingriff.Length > 0 ? ", " + eingriff.Replace("⚠ EINGRIFF ", "Opfer-") : "");
                fa0.Owner = ViewPlayer;
                if (IsHostile(fa0, fu0) != feind)
                {
                    int altF = fu0.Owner;
                    bool gefunden = false;
                    for (int p = 0; p <= 7 && !gefunden; p++)
                    {
                        fu0.Owner = p;
                        gefunden = IsHostile(fa0, fu0) == feind;
                    }
                    if (gefunden) eingriff += $", Opfer-Besitzer {altF} -> {fu0.Owner}";
                    else { fu0.Owner = altF; fahrer = fussIdx = -1; }
                }
            }
            var f = new UeberfahrFall { Name = name, Fahrer = fahrer, Fuss = fussIdx, Klasse = klasse,
                                        Eingriff = eingriff };
            _uFaelle.Add(f);
            if (fahrer < 0) { f.Weg = "kein passendes Paar auf der Karte"; return; }
            benutzt.Add(fahrer);
            benutzt.Add(f.Fuss);
            if (!FreierStreifen(out f.Col, out f.Row)) { f.Weg = "kein freier Streifen"; return; }

            var fa = _entities[fahrer];
            var fu = _entities[f.Fuss];
            if (klasse is { } k) fa.Move = k;
            foreach (var x in new[] { fa, fu }) { x.Weapon = 0; x.Target = -1; x.Path = null; x.Ordered = false; }
            ProbeVersetzen(fahrer, f.Col, f.Row);
            ProbeVersetzen(f.Fuss, f.Col + 3, f.Row);
            var ziel = new Vector2I(f.Col + 6, f.Row);
            var weg = _nav.FindPath(new Vector2I(f.Col, f.Row), ziel, fa.Move, fahrer);
            if (weg == null || weg.Count == 0) { f.Weg = "kein Weg"; return; }
            fa.Path = weg; fa.PathIdx = 0; fa.Goal = ziel; fa.Ordered = true;
            f.Weg = $"{weg.Count} Schritte";
        }

        // ⚠ E ZUERST: es braucht ZWEI freie Fusssoldaten, A..D verbrauchten auf
        // K6 vorher alle. Die Zeilen werden unten nach dem Namen ausgegeben.
        // ⚠ Der Fahrer von E gehoert dem SPIELER: ein Fussoldat eines
        // Rechnerspielers verlor auf K6 seinen Weg noch vor dem ersten Schritt
        // (»Weg keiner«, kein Fahrgrund) — die KI raeumt ihn ab, und die
        // Schrittregel wurde nie erreicht.
        Fall("E Fussvolk gegen Feind  ", IstFussvolk, true, null,
             besitzerTauschen: true, fahrerZuSpieler: true);
        Fall("A Rad/Kette gegen Feind ", IstRadKette, true, null, besitzerTauschen: true);
        Fall("B Rad/Kette gegen Freund", IstRadKette, false, null, besitzerTauschen: true);
        Fall("C Hover gegen Feind     ", IstRadKette, true, Simulation.NavGrid.MoveClass.Hover, besitzerTauschen: true);
        Fall("D Walker gegen Feind    ", IstRadKette, true, Simulation.NavGrid.MoveClass.Walker, besitzerTauschen: true);
        _uFaelle.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
        foreach (var f in _uFaelle)
            GD.Print($"ueberfahren-check: {f.Name.Trim()} — Fahrer {f.Fahrer}, Fuss {f.Fuss}, "
                   + $"Streifen ({f.Col},{f.Row}), {f.Weg}");
    }

    public string UeberfahrenCheckLine()
    {
        var sb = new System.Text.StringBuilder("ueberfahren-check\n");
        if (!_uCheckAn) return sb.Append("  nicht gestartet — der Lauf sagt NICHTS").ToString();
        bool ok = true;
        foreach (var f in _uFaelle)
        {
            if (f.Fuss < 0 || f.Fahrer < 0 || f.Weg.StartsWith("k"))
            {
                ok = false;
                sb.Append($"  {f.Name}: ⚠ nicht herstellbar ({f.Weg}) — sagt NICHTS\n");
                continue;
            }
            var fa = _entities[f.Fahrer];
            var fu = _entities[f.Fuss];
            var z = _uZaehler.TryGetValue(f.Fahrer, out var zz) ? zz : new int[3];
            bool durch = fa.Col >= f.Col + 4;
            bool ueberfahren = _ueberfahrenOpfer.Contains(f.Fuss);
            int halt = _haltGemessen.TryGetValue(fu, out var h) ? h : -1;
            bool treffer = false;
            foreach (var t in _trefferLog)
                if (t.Opfer == fu && t.Angriff == 200) { treffer = true; break; }

            bool soll = f.Name[0] switch
            {
                'A' => fu.Dead && ueberfahren && treffer && halt is >= 8 and <= 11 && durch,
                'B' => !fu.Dead && !ueberfahren && z[0] > 0,
                'C' or 'D' => !fu.Dead && !ueberfahren && z[1] > 0,
                _ => !fu.Dead && !fa.Dead && !ueberfahren && z[2] > 0 && durch,
            };
            ok &= soll;
            sb.Append($"  {f.Name}: Fuss {(fu.Dead ? "TOT" : "lebt")} (TP {fu.Hp}/{fu.HpMax}"
                    + $"{(ueberfahren ? ", ueberfahren" : "")}{(treffer ? ", Treffer 200" : "")}"
                    + $"{(halt >= 0 ? $", hielt {halt} Takte" : "")}), "
                    + $"Fahrer auf ({fa.Col},{fa.Row}){(durch ? " durch" : " davor")}, "
                    + $"gebeten {z[0]} / blockiert {z[1]} / frei {z[2]}  {(soll ? "ja" : "NEIN")}"
                    + $"{(f.Eingriff.Length > 0 ? "  " + f.Eingriff : "")}\n");
            // ⚠ Ohne den Zustand des Fahrers ist »kam nie an« nicht von »durfte
            // nicht fahren« zu unterscheiden (Fall E am 11.09.: alle Zaehler 0).
            if (!soll)
                sb.Append($"      Fahrer: Besitzer {fa.Owner}{(IsStandby(fa.Owner) ? " (Bereitschaft)" : "")}, "
                        + $"{fa.Move}, UKOL {fa.Ukol}, Weg {(fa.Path == null ? "keiner" : $"{fa.PathIdx}/{fa.Path.Count}")}, "
                        + $"Ziel ({fa.Goal.X},{fa.Goal.Y}), Reserviert {(fa.Reserved is { } rv ? $"({rv.X},{rv.Y})" : "-")}, "
                        + $"Mobile {fa.Mobile}, Fuss Besitzer {fu.Owner} auf ({fu.Col},{fu.Row}), "
                        + $"letzter Fahrgrund: {(string.IsNullOrEmpty(fa.Fahrgrund) ? "-" : fa.Fahrgrund)}\n");
        }
        sb.Append($"  Gegenschalter --ueberfahren-alle: {UeberfahrenAlle}, "
                + $"--ueberfahren-loeschen: {UeberfahrenLoeschen}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    public string SprengungCheck()
    {
        var sb = new System.Text.StringBuilder("sprengung-check\n");
        if (_nav == null) return sb.Append("  keine Karte — sagt NICHTS").ToString();
        int mitte = -1, fuss = -1, nachbar = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding) continue;
            if (e.Infantry >= 0) { if (fuss < 0) fuss = i; continue; }
            if (e.GameUnitType != 0 || e.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
            if (mitte < 0) mitte = i; else if (nachbar < 0) nachbar = i;
        }
        if (mitte < 0 || fuss < 0 || nachbar < 0 || !FreierStreifen(out int c, out int r))
            return sb.Append("  Fall nicht herstellbar — sagt NICHTS").ToString();
        c += 3;                                                     // Mitte des Streifens

        var vm = _entities[mitte]; var vf = _entities[fuss]; var vn = _entities[nachbar];
        foreach (var x in new[] { vm, vf, vn }) { x.Weapon = 0; x.Target = -1; x.Path = null; }
        ProbeVersetzen(mitte, c, r);
        ProbeVersetzen(fuss, c + 0, r + 1);                         // Nachbar 0 der Tafel
        ProbeVersetzen(nachbar, c + 1, r);                          // Nachbar 6 der Tafel
        int hpF = vf.Hp, hpN = vn.Hp;
        sb.Append($"  ⚠ EINGRIFF: {LabelOf(vm)} auf ({c},{r}), {LabelOf(vf)} auf ({c},{r + 1}) "
                + $"TP {hpF}, {LabelOf(vn)} auf ({c + 1},{r}) TP {hpN}\n");

        _sprengLog = new();
        _trefferLog.Clear();
        Kill(mitte, vm, -1, "Pruefstand Sprengung");
        var log = _sprengLog;
        _sprengLog = null;

        // die acht Zellen in der Reihenfolge der Tafel 0x4F5AF0
        var zellen = log.FindAll(x => x.Was == "Zelle");
        bool reihenfolge = zellen.Count >= 8;
        for (int k = 0; k < 8 && reihenfolge; k++)
            reihenfolge = zellen[k].C == c + SprengNachbarn[k].Dx && zellen[k].R == r + SprengNachbarn[k].Dy;
        int sF = -1, sN = -1;
        foreach (var t in _trefferLog)
        {
            if (t.Opfer == vf && t.Angriff == 15 && sF < 0) sF = t.Schaden;
            if (t.Opfer == vn && t.Angriff == 15 && sN < 0) sN = t.Schaden;
        }
        bool abzugF = sF >= 0 && (vf.Dead ? sF >= hpF : hpF - vf.Hp == sF);
        bool abzugN = sN >= 0 && (vn.Dead ? sN >= hpN : hpN - vn.Hp == sN);
        sb.Append($"  Zellen angefasst: {zellen.Count} (in der Tafelreihenfolge: {(reihenfolge ? "ja" : "NEIN")})\n");
        sb.Append($"  Fusssoldat: Treffer {(sF < 0 ? "KEINER" : sF.ToString())}, TP {hpF} -> {vf.Hp}"
                + $"{(vf.Dead ? " tot" : "")}  {(abzugF ? "ja" : "NEIN")}\n");
        sb.Append($"  Nachbarfahrzeug: Treffer {(sN < 0 ? "KEINER" : sN.ToString())}, TP {hpN} -> {vn.Hp}"
                + $"{(vn.Dead ? " tot" : "")}  {(abzugN ? "ja" : "NEIN")}\n");
        sb.Append($"  alle Eintraege: {string.Join("; ", log.FindAll(x => x.Was != "Zelle").ConvertAll(x => $"({x.C},{x.R}) {x.Was} {x.Schaden}{(x.Tot ? " tot" : "")}"))}\n");
        sb.Append($"  Gegenschalter --sprengung-ohne-nachbarn: {SprengungOhneNachbarn}\n");
        sb.Append(reihenfolge && abzugF && abzugN ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>⚠ Pruefstand-Eingriff: eine Einheit auf eine Zelle stellen, mit
    /// Belegung.</summary>
    private void ProbeVersetzen(int idx, int c, int r)
    {
        var e = _entities[idx];
        _nav!.ClearOccupant(e.Col, e.Row, idx);
        if (e.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, idx);
        e.Reserved = null;
        e.Col = c; e.Row = r;
        e.Pos = CellCenter(c, r);
        _nav.SetOccupant(c, r, idx, e.Infantry >= 0);
    }

    /// <summary>Ein Streifen von 7 Zellen, flach, frei fuer alle drei
    /// Landklassen, mit freien Zeilen darueber und darunter (der Freund soll
    /// ausweichen koennen), weit genug vom naechsten Streifen.</summary>
    private bool FreierStreifen(out int col, out int row)
    {
        col = row = -1;
        for (int r = 6; r < _nav!.Height - 6; r++)
        {
            bool nah = false;
            for (int d = -4; d <= 4 && !nah; d++) nah = _uZeilen.Contains(r + d);
            if (nah) continue;
            for (int c = 6; c < _nav.Width - 13; c++)
            {
                int el = _nav.ElevAt(c, r);
                bool gut = true;
                // ⚠ 11.09.2026 gelockert: 9x5 auf EINER Hoehe fand auf K4 keinen
                // Platz. Die Fahrzeile selbst bleibt flach und fuer alle Klassen
                // frei; die Nachbarzeilen muessen nur befahrbar sein (dorthin
                // weicht der Freund aus).
                for (int x = c - 1; x <= c + 7 && gut; x++)
                    for (int y = r - 1; y <= r + 1 && gut; y++)
                        gut = _nav.IsFree(x, y, Simulation.NavGrid.MoveClass.Vehicle, -1)
                           && (y != r || (_nav.IsFree(x, y, Simulation.NavGrid.MoveClass.Hover, -1)
                                          && _nav.ElevAt(x, y) == el));
                if (!gut) continue;
                col = c; row = r;
                _uZeilen.Add(r);
                return true;
            }
        }
        return false;
    }
}
