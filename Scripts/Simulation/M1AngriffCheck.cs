namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <c>--m1-angriff-check</c> (03.10.2026, bug-409, berichte/m1-gegner-vorbei-fable.md §6):
/// Kampagne 1 kopflos. Der Panzer (Platz 0) fährt bei t = 2 s nach (4,30) —
/// damit wird seine Startzelle (4,39) leer, und M1 Regel 5/6 schickt 15 Takte
/// später die Plätze 1000…1003 los. Gemessen:
/// <list type="bullet">
/// <item>Angriffsbefehle angewendet (BefehleAngewendet[1]) — Soll 4/4;
/// Nullmodell <c>--skriptangriff-alt</c>: 0 Angriffe, 4 Fahrbefehle.</item>
/// <item>je Platz Target == Panzer und Ordered — Soll 4/4, alt 0/4.</item>
/// <item>Abstand jedes Verfolgers zum Panzer bei 10/30/60/90 s und das Minimum.</item>
/// <item>Schüsse Spieler 1 → Spieler 0 (BeschussZaehlen) — Soll &gt; 0.</item>
/// </list>
/// Kampfausgänge NIE aus einem Lauf: <c>--determinism-seed=1..5</c>.
/// </summary>
public partial class MapEntityLayer
{
    public static bool M1AngriffCheckAn;
    private int _m1Panzer = -1;
    private bool _m1Befohlen, _m1Fertig;
    private float _m1LeerAb = -1, _m1BefehlAb = -1;
    private int _m1AngewVor, _m1FahrVor, _m1BeschussVor, _m1TargetMax;
    private readonly int[] _m1Idx = { -1, -1, -1, -1 };
    private readonly float[] _m1StartAbstand = { -1, -1, -1, -1 };
    private readonly float[] _m1MinAbstand = { 999, 999, 999, 999 };
    private readonly List<string> _m1Proben = new();
    private int _m1ProbeNr;
    private Vector2I _m1Ziel;
    private static readonly float[] M1ProbeZeiten = { 10, 30, 60, 90, 120, 180 };

    public string M1AngriffStart()
    {
        EnsureMissionScript();
        for (int i = 0; i < _entities.Count; i++)
            if (!_entities[i].IsBuilding && !_entities[i].IsProp && _entities[i].Slot == 0)
            { _m1Panzer = i; break; }
        if (_m1Panzer < 0) return "KEIN Platz 0";
        for (int k = 0; k < 4; k++)
            for (int i = 0; i < _entities.Count; i++)
                if (!_entities[i].IsBuilding && !_entities[i].IsProp && _entities[i].Slot == 1000 + k)
                { _m1Idx[k] = i; break; }
        Schussgruende = true;                 // nur Text: warum steht/schiesst wer
        _m1AngewVor = BefehleAngewendet[1];
        _m1FahrVor = BefehleAngewendet[0];
        _m1BeschussVor = _beschuss[1, 0];
        var p = _entities[_m1Panzer];
        return $"Panzer Platz 0 Typ {p.UnitType} bei ({p.Col},{p.Row}) Verfolger-Indizes "
             + string.Join(",", _m1Idx) + (SkriptangriffAlt ? "  [--skriptangriff-alt]" : "");
    }

    private float M1Abstand(int k)
    {
        if (_m1Idx[k] < 0) return -1;
        var a = _entities[_m1Idx[k]];
        var p = _entities[_m1Panzer];
        if (a.Dead) return -1;
        return new Vector2(a.Col - p.Col, a.Row - p.Row).Length();
    }

    /// <summary>Ziel des Panzers: eine ERREICHBARE Zelle möglichst nahe (4,28)
    /// (nach Norden, Zeile &lt; 30 ist die Bedingung von #013), mindestens 9
    /// Zellen vom Start — mit Weg gesucht, nicht geraten: der erste Lauf mit
    /// (4,30) endete bei (1,35), nur 5 Zellen vom Start, und dann lief selbst
    /// der alte Fahrbefehl zufällig in Reichweite.</summary>
    private Vector2I M1ZielSuchen(Entity p)
    {
        var start = new Vector2I(p.Col, p.Row);
        var ziel = start;
        float best = 1e9f;
        if (_nav == null) return ziel;
        for (int r = 0; r < 70; r++)
            for (int c = 0; c < 50; c++)
            {
                if (new Vector2(c - p.Col, r - p.Row).Length() < 9f) continue;
                float d = new Vector2(c - 4, r - 28).Length();
                if (d >= best || !_nav.CanEnter(c, r, p.Move)) continue;
                var weg = _nav.FindPath(start, new Vector2I(c, r), p.Move, _m1Panzer);
                if (weg == null || weg.Count == 0 || weg[^1] != new Vector2I(c, r)) continue;
                best = d; ziel = new Vector2I(c, r);
            }
        return ziel;
    }

    /// <summary>Ein Bild messen; true = fertig.</summary>
    public bool M1AngriffMessen()
    {
        if (_m1Panzer < 0 || _m1Fertig) return true;
        var p = _entities[_m1Panzer];
        if (!_m1Befohlen && _clock >= 2f)
        {
            _m1Befohlen = true;
            _m1Ziel = M1ZielSuchen(p);
            _m1Proben.Add($"t={_clock:0.0}s Panzer fährt nach ({_m1Ziel.X},{_m1Ziel.Y})");
            _sel.Clear(); _sel.Add(_m1Panzer); SetPrimary();
            PostMove(CellCenter(_m1Ziel.X, _m1Ziel.Y));
        }
        if (_m1LeerAb < 0 && (p.Col != 4 || p.Row != 39)) _m1LeerAb = _clock;
        int befohlen = MissionAngriffeAbgesetzt + MissionAngriffRueckfaelle;
        if (_m1BefehlAb < 0 && (befohlen > 0 || BefehleAngewendet[0] - _m1FahrVor > 1))
        {
            // der Skriptbefehl ist da (im alten Stand: ein zweiter Fahrbefehl nach dem des Panzers)
            if (befohlen > 0 || SkriptangriffAlt) _m1BefehlAb = _clock;
            for (int k = 0; k < 4; k++) _m1StartAbstand[k] = M1Abstand(k);
        }
        int tgt = 0;
        for (int k = 0; k < 4; k++)
        {
            if (_m1Idx[k] < 0) continue;
            var a = _entities[_m1Idx[k]];
            if (!a.Dead && a.Target == _m1Panzer && a.Ordered) tgt++;
            float d = M1Abstand(k);
            if (d >= 0 && _m1BefehlAb >= 0) _m1MinAbstand[k] = Mathf.Min(_m1MinAbstand[k], d);
        }
        _m1TargetMax = Mathf.Max(_m1TargetMax, tgt);
        if (_m1ProbeNr < M1ProbeZeiten.Length && _clock >= M1ProbeZeiten[_m1ProbeNr])
        {
            var sb = new StringBuilder($"t={M1ProbeZeiten[_m1ProbeNr]:0}s Panzer ({p.Col},{p.Row}) Hp {p.Hp}"
                                     + $"{(p.Dead ? " TOT" : "")}; Abstand");
            for (int k = 0; k < 4; k++)
            {
                float d = M1Abstand(k);
                var a = _m1Idx[k] >= 0 ? _entities[_m1Idx[k]] : null;
                sb.Append($" {1000 + k}:{(d < 0 ? "tot" : d.ToString("0.0"))}"
                        + (a == null || a.Dead ? "" : $"@({a.Col},{a.Row}) T{(a.Target == _m1Panzer ? "=P0" : a.Target.ToString())}"));
            }
            sb.Append($"; Schüsse Sp1→Sp0 {_beschuss[1, 0] - _m1BeschussVor}; Ziel=P0 {tgt}/4");
            _m1Proben.Add(sb.ToString());
            _m1ProbeNr++;
        }
        if (_m1ProbeNr >= M1ProbeZeiten.Length || p.Dead) _m1Fertig = true;
        return _m1Fertig;
    }

    public string M1AngriffBericht()
    {
        var sb = new StringBuilder("m1-angriff-check:\n");
        foreach (var z in _m1Proben) sb.AppendLine("  " + z);
        int angew = BefehleAngewendet[1] - _m1AngewVor;
        int schuesse = _beschuss[1, 0] - _m1BeschussVor;
        float verzug = _m1BefehlAb >= 0 && _m1LeerAb >= 0 ? _m1BefehlAb - _m1LeerAb : -1;
        sb.AppendLine($"  Startzelle (4,39) leer ab {_m1LeerAb:0.00}s, Skriptbefehl ab {_m1BefehlAb:0.00}s"
                    + $" (Verzug {verzug:0.00}s, Soll 15 Takte ≈ 0,3 s)");
        sb.AppendLine($"  Angriffe abgesetzt {MissionAngriffeAbgesetzt}, Rückfälle {MissionAngriffRueckfaelle}, "
                    + $"angewendet {angew}" + (AngriffAbgewiesen.Length > 0 ? $" (zuletzt abgewiesen: {AngriffAbgewiesen})" : ""));
        sb.AppendLine($"  Target == Platz 0 und Ordered: höchstens {_m1TargetMax}/4");
        // »heran« = auf höchstens 4,5 Zellen an den Panzer (Reichweite MG 4,
        // Fußvolk 2) — misst, ob sie ihn VERFOLGEN, nicht nur zur Startzelle laufen.
        int faellt = 0;
        for (int k = 0; k < 4; k++)
        {
            bool f = _m1StartAbstand[k] > 0 && _m1MinAbstand[k] <= 4.5f;
            if (f) faellt++;
            sb.AppendLine($"  {1000 + k}: Abstand beim Befehl {_m1StartAbstand[k]:0.0}, kleinster {_m1MinAbstand[k]:0.0}"
                        + (f ? "  heran" : "  NICHT heran"));
        }
        sb.AppendLine($"  Schüsse Sp1→Sp0: {schuesse}");
        sb.AppendLine("  " + ChaseWatchLine());
        for (int k = 0; k < 4; k++)
            if (_m1Idx[k] >= 0 && !_entities[_m1Idx[k]].Dead)
            {
                var a = _entities[_m1Idx[k]];
                sb.AppendLine($"  {1000 + k} am Ende ({a.Col},{a.Row}) Weg {(a.Path == null ? "keiner" : $"{a.PathIdx}/{a.Path.Count}")}, "
                            + $"Grund: {a.Schussgrund}");
            }
        bool neu = angew == 4 && _m1TargetMax == 4 && faellt == 4 && schuesse > 0;
        bool alt = angew == 0;
        bool ok = SkriptangriffAlt ? alt : neu;
        sb.Append($"  Ergebnis: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}"
                + (SkriptangriffAlt ? " (Nullmodell --skriptangriff-alt: 0 Angriffe erwartet)" : "")
                + $" — angewendet {angew}/4, Ziel {_m1TargetMax}/4, heran {faellt}/4, Schüsse {schuesse}");
        return sb.ToString();
    }

    // ===== --m2-angriff-check (Gegenprobe C des Berichts) ======================
    // M2 Regel 38 (@0x498EEC): sobald ein Krumlov-Kopf Spieler 0 gehört und eine
    // Einheit von Spieler 0 auf (3,60) steht, setzt das Skript sieben P4-Einheiten
    // und schickt sie mit order_at(4000+i, x, y, 60000, 0) los. Hier wird die
    // Bedingung HERGESTELLT (Kopf 1 → Spieler 0, eine P0-Einheit nach (3,60)
    // gesetzt), dann läuft das echte Skript. Gemessen: Ziel der sieben, Leben von
    // Gebäude 0, obj_owner(0), v[70]/v[71]/v[101] — die Geldkette (Regel 45:
    // v[101]==2, P4 leer, obj_owner(0)==255 → 400 $) darf nicht brechen.
    public static bool M2AngriffCheckAn;
    private int _m2Geb = -1, _m2HpStart, _m2ProbeNr, _m2TargetMax;
    private bool _m2Gesetzt, _m2Fertig;
    private readonly List<string> _m2Proben = new();
    private static readonly float[] M2ProbeZeiten = { 5, 20, 40, 60, 90, 120 };

    public string M2AngriffStart()
    {
        EnsureMissionScript();
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].IsBuilding && _entities[i].Slot == 0) { _m2Geb = i; break; }
        if (_m2Geb < 0) return "KEIN Gebäude 0";
        var g = _entities[_m2Geb];
        _m2HpStart = g.Hp;
        return $"Gebäude 0 Art {g.BType} Besitzer {g.Owner} bei ({g.Col},{g.Row}) Hp {g.Hp}/{g.HpMax}"
             + (SkriptangriffAlt ? "  [--skriptangriff-alt]" : "");
    }

    public bool M2AngriffMessen()
    {
        if (_m2Geb < 0 || _m2Fertig || _mscript == null) return true;
        if (!_m2Gesetzt && _clock >= 1f)
        {
            _m2Gesetzt = true;
            foreach (var b in _entities)
                if (b.IsBuilding && b.Slot == 1) b.Owner = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != 0 || !e.Mobile) continue;
                _nav?.ClearOccupant(e.Col, e.Row, i);
                e.Col = 3; e.Row = 60; e.Path = null;
                e.Pos = BodyCenterAt(e, e.Col, e.Row);
                _nav?.SetOccupant(e.Col, e.Row, i);
                break;
            }
        }
        int p4 = 0, tgt = 0;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.Dead || e.Slot < 4000 || e.Slot > 4006) continue;
            p4++;
            if (e.Target == _m2Geb && e.Ordered) tgt++;
        }
        _m2TargetMax = Mathf.Max(_m2TargetMax, tgt);
        if (_m2ProbeNr < M2ProbeZeiten.Length && _clock >= M2ProbeZeiten[_m2ProbeNr])
        {
            var g = _entities[_m2Geb];
            int oo = g.BType == 0 || g.Dead ? 12 : g.Owner is >= 0 and <= 7 ? g.Owner : 255;
            _m2Proben.Add($"t={M2ProbeZeiten[_m2ProbeNr]:0}s Gebäude 0 Hp {g.Hp}{(g.Dead ? " ZERSTÖRT" : "")}, "
                        + $"obj_owner(0)={oo}; P4 lebend {p4}, Ziel=Geb0 {tgt}; v70={_mscript.VarAt(70)} "
                        + $"v71={_mscript.VarAt(71)} v101={_mscript.VarAt(101)}; Schaden am Gebäude {_beschussAufGeb0}");
            _m2ProbeNr++;
        }
        if (_m2ProbeNr >= M2ProbeZeiten.Length) _m2Fertig = true;
        return _m2Fertig;
    }

    private int _beschussAufGeb0 => _entities.Count > 0 && _m2Geb >= 0 ? _m2HpStart - _entities[_m2Geb].Hp : 0;

    public string M2AngriffBericht()
    {
        var sb = new StringBuilder("m2-angriff-check:\n");
        foreach (var z in _m2Proben) sb.AppendLine("  " + z);
        sb.Append($"  Angriffe abgesetzt {MissionAngriffeAbgesetzt}, Rückfälle {MissionAngriffRueckfaelle}, "
                + $"Ziel=Geb0 höchstens {_m2TargetMax}/7");
        return sb.ToString();
    }
}
