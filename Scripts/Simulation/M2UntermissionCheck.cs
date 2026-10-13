namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <c>--m2-untermission-check</c> (KayelGee R5#8/#14, berichte/kayelgee-runde5-m2.md §6):
/// M2 Regel 38 (@0x498EEC) feuert, wenn eine Krumlov-Basis (Gebaeude 1 oder 2)
/// Spieler 0 gehoert UND eine Einheit von Spieler 0 auf (3,60) steht. Der alte
/// <c>--m2-angriff-check</c> stellte beides HER (Besitzer gesetzt, Einheit
/// hingestellt) und war gruen — er misst den Weg des Spielers nicht.
///
/// <para>Hier ohne Eingriff am Besitzer: Teil C — eine eigene Einheit nimmt die
/// naehere Basis ueber den KLICKWEG ein (Einnahmezeiger an der Tuer, dann
/// <see cref="PostCapture"/> wie MapViewer beim Rechtsklick); Teil A — danach
/// faehrt eine zweite eigene Bodeneinheit mit einem gewoehnlichen Fahrbefehl
/// nach (3,61), also ueber (3,60). Gemessen: obj_owner(1/2), imap(3,60), v70
/// (Riegel R38), v101, die sieben Plaetze 4000..4006, Text 120.</para>
///
/// <para>⚠ EINGRIFF, gesagt: die zwei Einheiten werden NAHE an ihr Ziel gesetzt
/// (Einnehmer zwei Zeilen unter die Tuer, Fahrer an den Nordeingang des
/// Durchgangs), damit der Lauf nicht an der Anfahrt durch die Karte haengt —
/// die Anfahrt misst er also NICHT.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool M2UntermissionCheckAn;
    private int _m2uBasis = -1, _m2uEinnehmer = -1, _m2uFahrer = -1, _m2uPhase;
    private float _m2uEingenommenAb = -1, _m2uR38Ab = -1, _m2uAuf360Ab = -1, _m2uFahrtAb = -1;
    private string _m2uZeiger = "", _m2uNotiz = "";
    private bool _m2uCaptureOk, _m2uFertig, _m2uGeklickt;
    private int _m2uBesatzung;
    private readonly List<string> _m2uProben = new();

    private int M2uObjOwner(int slot) => _mscript?.ObjOwner?.Invoke(slot) ?? -1;

    public string M2UntermissionStart()
    {
        EnsureMissionScript();
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        // die Basis, die einer eigenen Einheit am naechsten liegt
        int best = int.MaxValue;
        Entity? erster = null;
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == me && e.Mobile) { erster = e; break; }
        if (erster == null) return "keine eigene Einheit — sagt NICHTS";
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.Dead || b.BType != 1 || b.Slot is not (1 or 2)) continue;
            int d = Mathf.Abs(b.Col - erster.Col) + Mathf.Abs(b.Row - erster.Row);
            if (d < best) { best = d; _m2uBasis = i; }
        }
        if (_m2uBasis < 0) return "keine Basis 1/2 — sagt NICHTS";
        var basis = _entities[_m2uBasis];
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != me || !e.Mobile) continue;
            if (_m2uEinnehmer < 0 && e.GameUnitType == 0) _m2uEinnehmer = i;
            else if (_m2uFahrer < 0 && e.Infantry < 0 && e.GameUnitType == 0) _m2uFahrer = i;
        }
        if (_m2uEinnehmer < 0 || _m2uFahrer < 0) return "keine zwei eigenen Bodeneinheiten — sagt NICHTS";
        return $"Basis {basis.Slot} ({basis.Col},{basis.Row}) Besitzer {basis.Owner}, Tuer ({basis.Col + basis.DoorCol},{basis.Row + basis.DoorRow}); " +
               $"Einnehmer Platz {_entities[_m2uEinnehmer].Slot}, Fahrer Platz {_entities[_m2uFahrer].Slot}; " +
               $"obj_owner(1)={M2uObjOwner(1)} obj_owner(2)={M2uObjOwner(2)} v70={_mscript?.VarAt(70)}";
    }

    private void M2uSetzen(int idx, int c, int r)
    {
        var e = _entities[idx];
        _nav?.ClearOccupant(e.Col, e.Row, idx);
        e.Col = c; e.Row = r; e.Path = null; e.Reserved = null;
        e.Pos = BodyCenterAt(e, c, r);
        _nav?.SetOccupant(c, r, idx);
    }

    /// <summary>Eine freie, befahrbare Zelle nahe (c,r).</summary>
    private Vector2I M2uFrei(Entity e, int c, int r)
    {
        for (int rad = 0; rad < 6; rad++)
            for (int dr = -rad; dr <= rad; dr++)
                for (int dc = -rad; dc <= rad; dc++)
                {
                    int x = c + dc, y = r + dr;
                    if (_nav != null && _nav.CanEnter(x, y, e.Move) && _nav.OccupantAt(x, y) < 0)
                        return new Vector2I(x, y);
                }
        return new Vector2I(c, r);
    }

    public bool M2UntermissionMessen()
    {
        if (_m2uFertig || _m2uBasis < 0 || _mscript == null) return true;
        var basis = _entities[_m2uBasis];
        int tc = basis.Col + basis.DoorCol, tr = basis.Row + basis.DoorRow;
        if (_m2uPhase == 0 && _clock >= 1f)
        {
            _m2uPhase = 1;
            var ein = _entities[_m2uEinnehmer];
            var z = M2uFrei(ein, tc, tr + 2);
            M2uSetzen(_m2uEinnehmer, z.X, z.Y);
            // ⚠ EINGRIFF: die Wache der Basis (fremde Einheiten im Umkreis von
            // 10 Zellen) wird entfernt — der Spieler raeumt sie vorher weg,
            // sonst stirbt der Einnehmer an der Tuer.
            int weg = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                var x = _entities[i];
                if (x.IsBuilding || x.IsProp || x.Dead || x.Owner == ein.Owner || x.Owner is < 0 or > 7) continue;
                if (Mathf.Abs(x.Col - tc) > 10 || Mathf.Abs(x.Row - tr) > 10) continue;
                Kill(i, x, -1, "m2-untermission-check: Wache entfernt"); weg++;
            }
            _m2uProben.Add($"t={_clock:0.0}s Einnehmer auf ({z.X},{z.Y}) gesetzt, {weg} Wachen entfernt — die Sicht zieht nach, Klick bei 2,5 s");
        }
        if (_m2uPhase == 1 && !_m2uGeklickt && _clock >= 2.5f)
        {
            _m2uGeklickt = true;
            var ein = _entities[_m2uEinnehmer];
            var z = new Vector2I(ein.Col, ein.Row);
            _sel.Clear(); _sel.Add(_m2uEinnehmer); SetPrimary();
            var tuer = CellCenter(tc, tr);
            var hint = CursorHintAt(tuer);
            _m2uZeiger = hint.ToString();
            // der Weg des Rechtsklicks: Einnahmezeiger -> PostCapture
            _m2uCaptureOk = EinnahmezeigerHier(tuer) && PostCapture(tuer, false);
            _m2uProben.Add($"t={_clock:0.0}s Einnehmer auf ({z.X},{z.Y}), Zeiger auf der Tuer: {hint}, PostCapture {(_m2uCaptureOk ? "angenommen" : "ABGELEHNT")}");
            // warum: Nebel, Pick, Zeiger je Zelle des Fussabdrucks
            int pk = Pick(tuer);
            var zellen = new StringBuilder();
            for (int r = basis.Row - 1; r < basis.Row + Mathf.Max(1, basis.FootH) + 1; r++)
            {
                zellen.Append($"\n      Zeile {r}: ");
                for (int c = basis.Col; c < basis.Col + Mathf.Max(1, basis.FootW); c++)
                {
                    var h = CursorHintAt(CellCenter(c, r));
                    zellen.Append(h switch { Hint.Einnahme => "E", Hint.Enemy => "A", Hint.Ground => ".", Hint.Neutral => "n", _ => "?" });
                }
            }
            _m2uProben.Add($"   Basis im Nebel {ImNebelVerborgen(basis)}, aufgedeckt {GebaeudeAufgedeckt(basis)}, Built {basis.Built}, Doors {basis.Doors}, " +
                           $"Fuss {basis.FootW}x{basis.FootH}, Pick(Tuer) = {(pk == _m2uBasis ? "die Basis" : pk.ToString())}, Einnehmer GameUnitType {ein.GameUnitType}" +
                           $"\n   Zeiger je Zelle ab Spalte {basis.Col} (E=Einnahme A=Angriff .=Boden n=neutral):{zellen}");
        }
        // ⚠ EINGRIFF, laufend: was waehrend der Einnahme aus der Basis kommt
        // (die Besatzung), wird ebenfalls entfernt — der Spieler kaempft es nieder.
        if (_m2uPhase == 1 && _m2uEingenommenAb < 0)
            for (int i = 0; i < _entities.Count; i++)
            {
                var x = _entities[i];
                if (x.IsBuilding || x.IsProp || x.Dead || x.Owner == 0 || x.Owner is < 0 or > 7) continue;
                if (Mathf.Abs(x.Col - tc) > 10 || Mathf.Abs(x.Row - tr) > 10 || Untergestellt(x)) continue;
                Kill(i, x, -1, "m2-untermission-check: Besatzung entfernt"); _m2uBesatzung++;
            }
        if (_m2uPhase == 1 && _m2uEingenommenAb < 0 && (M2uObjOwner(1) == 0 || M2uObjOwner(2) == 0))
        {
            _m2uEingenommenAb = _clock;
            _m2uProben.Add($"t={_clock:0.0}s EINGENOMMEN ({_m2uBesatzung} Besatzung entfernt): obj_owner(1)={M2uObjOwner(1)} obj_owner(2)={M2uObjOwner(2)}");
        }
        if (_m2uPhase == 1 && _m2uEingenommenAb >= 0 && _clock >= _m2uEingenommenAb + 1f)
        {
            _m2uPhase = 2;
            var f = _entities[_m2uFahrer];
            var z = M2uFrei(f, 3, 52);
            M2uSetzen(_m2uFahrer, z.X, z.Y);
            _sel.Clear(); _sel.Add(_m2uFahrer); SetPrimary();
            PostMove(CellCenter(3, 61));
            _m2uFahrtAb = _clock;
            _m2uProben.Add($"t={_clock:0.0}s Fahrer auf ({z.X},{z.Y}) faehrt nach (3,61), Weg {(f.Path?.Count ?? 0)} Zellen");
        }
        if (_m2uPhase == 2)
        {
            var f = _entities[_m2uFahrer];
            if (_m2uAuf360Ab < 0 && f.Col == 3 && f.Row == 60)
            {
                _m2uAuf360Ab = _clock;
                _m2uProben.Add($"t={_clock:0.0}s Fahrer auf (3,60), imap(3,60)={_mscript.ImapAt?.Invoke(3, 60)}");
            }
            if (_m2uR38Ab < 0 && _mscript.VarAt(70) != 0)
            {
                _m2uR38Ab = _clock;
                _m2uProben.Add($"t={_clock:0.0}s R38 GEFEUERT (v70={_mscript.VarAt(70)})");
            }
            if (_clock >= _m2uFahrtAb + 40f || (_m2uR38Ab >= 0 && _clock >= _m2uR38Ab + 10f)) _m2uFertig = true;
        }
        if (_m2uPhase == 1 && _clock >= 240f)
        {
            _m2uNotiz = "Einnahme nach 240 s nicht abgeschlossen";
            _m2uFertig = true;
        }
        return _m2uFertig;
    }

    public string M2UntermissionBericht()
    {
        var sb = new StringBuilder("m2-untermission-check:\n");
        foreach (var z in _m2uProben) sb.AppendLine("  " + z);
        if (_m2uNotiz.Length > 0) sb.AppendLine("  ⚠ " + _m2uNotiz);
        int p4 = 0;
        foreach (var e in _entities) if (!e.IsBuilding && !e.Dead && e.Slot is >= 4000 and <= 4006) p4++;
        if (_m2uEinnehmer >= 0)
        {
            var ein = _entities[_m2uEinnehmer];
            sb.AppendLine($"  Einnehmer am Ende ({ein.Col},{ein.Row}) Ukol {ein.Ukol}{(ein.Dead ? " TOT" : "")}, Basis Besitzer {_entities[_m2uBasis].Owner}");
        }
        if (_m2uFahrer >= 0)
        {
            var f = _entities[_m2uFahrer];
            sb.AppendLine($"  Fahrer am Ende ({f.Col},{f.Row}){(f.Dead ? " TOT" : "")}, Weg {(f.Path == null ? "keiner" : $"{f.PathIdx}/{f.Path.Count}")}, Fahrgrund »{f.Fahrgrund}«");
        }
        sb.AppendLine($"  obj_owner(1)={M2uObjOwner(1)} obj_owner(2)={M2uObjOwner(2)} v70={_mscript?.VarAt(70)} v101={_mscript?.VarAt(101)}, Partisanen 4000..4006: {p4}/7");
        bool ok = _m2uCaptureOk && _m2uEingenommenAb >= 0 && _m2uAuf360Ab >= 0 && _m2uR38Ab >= 0 && p4 == 7;
        string wo = !_m2uCaptureOk ? "der Einnahmeklick" : _m2uEingenommenAb < 0 ? "die Einnahme" :
                    _m2uAuf360Ab < 0 ? "die Fahrt ueber (3,60)" : _m2uR38Ab < 0 ? "R38 trotz Bedingung" : p4 != 7 ? "die Partisanen" : "";
        sb.Append($"  {(ok ? "BESTANDEN" : $"DURCHGEFALLEN an: {wo}")}");
        return sb.ToString();
    }
}
