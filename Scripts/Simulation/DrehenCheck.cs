namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// <c>--drehen-check</c> (03.10.2026, bug-406/407/408, berichte/turm-koerper-drehen-fable.md
/// §6 — Prüfstände A/B/C in einem). Kopflos, je SPIELTAKT gemessen (Haken nach
/// <c>_taktNr++</c>, der Zustand ist also der vom Ende des vorigen Taktes). Ein
/// eigenes Fahrzeug mit Turmwaffe fährt auf einer freien Geraden:
/// <list type="number">
/// <item>Anlauf auf +2 (Blick festlegen).</item>
/// <item>B/C — Kehrtwende nach −3: Takte Rumpf (erste Stufe bis Anfahren, Soll 9;
/// <c>--koerperdrehung-alt</c> 4) und Turm (erste bis letzte Stufe, Soll 6, vor dem
/// Rumpf fertig; <c>--turm-alt</c> = Rumpf).</item>
/// <item>A — im zweiten Schritt, mitten drin, Gegenbefehl nach +4: Schrittabbrüche
/// (Soll 0), größter Weg je Takt gegen den normalen (Soll ≤ 1,05), Geisterzellen
/// (Soll 0). Nullmodell <c>--gegenbefehl-alt</c>: Abbruch ≥ 1 und schneller/Geist.</item>
/// <item>C — Fahrt nach −3, unterwegs ein fernes Feindziel mit 0,5 s Nachladen: der
/// Turm wendet sich erst nach dem Nachladen wieder dem Endziel zu
/// (<c>--turm-alt</c>: sofort).</item>
/// <item>C — Fahrt zu einem Ziel schräg zur Bahn: Anteil der Fahrtakte mit Turm aufs
/// Endziel (Soll ≥ 90 %; <c>--turm-alt</c> darunter).</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    public static bool DrehenCheckAn;
    public bool DrehenFertig { get; private set; }
    public string DrehenBerichtText { get; private set; } = "";

    private int _drKandidaten;
    private float _drStartLage;
    private readonly List<string> _drFehlZiel = new();
    private int _drE = -1, _drPhase, _drTakt, _drGesamt;
    private Vector2I _drD, _drNull;
    private string _drStart = "";
    private Vector2 _drPosVor;
    private int _drRumpfVor, _drTurmVor;
    // B/C Kehrtwende
    private int _drRumpfErst = -1, _drAnfahrt = -1, _drTurmErst = -1, _drTurmLetzt = -1, _drTurmSoll = -1;
    // A Gegenbefehl
    private float _drWegNormal, _drWegNach;
    private int _drAbbruchVor, _drAbbrueche = -1, _drGeister = -1, _drSprungVor, _drSpruenge;
    private int _drGegenTakt = -1;
    // C Nachladen
    private int _drHaltStart = -1, _drNachladen = -1, _drTurmLos = -1, _drHaltBlick = -1, _drTurmZurueck = -1;
    // C Endziel
    private int _drFahrTakte, _drAufsZiel;

    private Vector2I DrZelle(int n) => _drNull + _drD * n;

    private void DrFahre(Vector2I z)
    {
        Emit(CommandRecord.Make(CommandOp.Move, (byte)ViewPlayer,
             (short)_drE, (short)z.X, (short)z.Y, 0));
    }

    private bool DrSteht(Entity e) => (e.Path == null || e.PathIdx >= e.Path.Count) && e.Reserved == null;

    private static int ZielBlick(Entity e)
    {
        var d = new Vector2((e.Goal.X - e.Col) * TileW, (e.Goal.Y - e.Row) * TileH);
        return d.LengthSquared() < 0.0001f ? -1 : DirToFacing(d);
    }

    /// <summary>Einheit und Gerade wählen. Null = nichts gefunden.</summary>
    private string? DrehenStart()
    {
        if (_nav == null) return null;
        _drKandidaten = 0;
        var dirs = new[] { new Vector2I(1, 0), new Vector2I(0, 1), new Vector2I(-1, 0), new Vector2I(0, -1),
                           new Vector2I(1, 1), new Vector2I(-1, 1), new Vector2I(1, -1), new Vector2I(-1, -1) };
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer || e.Infantry >= 0 || !e.Mobile) continue;
            if (e.Move != Simulation.NavGrid.MoveClass.Vehicle || e.Weapon is < 1 or > 40) continue;
            if (e.Chassis == 9 || e.GameUnitType > 2 || (e.FuelMax > 0 && e.Fuel < 40) || e.DugIn) continue;
            _drKandidaten++;
            // eine freie Gerade −3…+4 in der Nähe (Umkreis 12), erreichbar
            var orte = new List<Vector2I>();
            for (int dy = -12; dy <= 12; dy++)
                for (int dx = -12; dx <= 12; dx++) orte.Add(new Vector2I(e.Col + dx, e.Row + dy));
            orte.Sort((p, q) => (p - new Vector2I(e.Col, e.Row)).LengthSquared()
                                .CompareTo((q - new Vector2I(e.Col, e.Row)).LengthSquared()));
            foreach (var x in orte)
                foreach (var d in dirs)
                {
                    bool frei = true;
                    for (int n = -3; n <= 4 && frei; n++)
                    {
                        var z = x + d * n;
                        frei = _nav.CanEnter(z.X, z.Y, e.Move) && _nav.IsFree(z.X, z.Y, e.Move, i);
                    }
                    if (!frei) continue;
                    if (x != new Vector2I(e.Col, e.Row))
                    {
                        var p = _nav.FindPath(new Vector2I(e.Col, e.Row), x, e.Move, i);
                        if (p == null || p.Count == 0) continue;
                    }
                    _drE = i; _drD = d; _drNull = x;
                    e.Cooldown = 0; e.Target = -1;
                    return $"Platz {e.Slot} Typ {e.UnitType} Waffe {e.Weapon} Fahrwerk {e.Chassis} "
                         + $"Tempo {e.Speed} Sprit {e.Fuel}/{e.FuelMax} von ({e.Col},{e.Row}), "
                         + $"Gerade ab ({x.X},{x.Y}) Richtung ({d.X},{d.Y})";
                }
        }
        return null;
    }

    /// <summary>Der Haken je Spieltakt.</summary>
    private void DrehenCheckTakt()
    {
        if (DrehenFertig) return;
        _drGesamt++;
        if (_drPhase == 0)
        {
            var s = DrehenStart();
            _drStart = s ?? $"KEIN passendes eigenes Fahrzeug mit freier Geraden ({_drKandidaten} Kandidaten, Spieler {ViewPlayer})";
            if (s == null) { if (_drGesamt > 120) DrehenAbschluss(); return; }
            DrFahre(DrZelle(0));                       // erst auf die Gerade
            _drPhase = 9; _drTakt = 0;
            return;
        }
        var e = _entities[_drE];
        if (e.Dead) { _drStart += " — EINHEIT VERLOREN"; DrehenAbschluss(); return; }
        _drTakt++;
        int rumpf = e.Facing, turm = TurmRichtung(e);
        float weg = e.Pos.DistanceTo(_drPosVor);
        bool faehrt = weg > 0.01f;

        switch (_drPhase)
        {
            case 9:                                     // auf die Gerade fahren
                if (_drTakt > 5 && DrSteht(e))
                {
                    if (e.Col != _drNull.X || e.Row != _drNull.Y)
                    { _drStart += $" — Gerade nicht erreicht, steht auf ({e.Col},{e.Row})"; DrehenAbschluss(); return; }
                    DrFahre(DrZelle(2));
                    _drPhase = 1; _drTakt = 0;
                }
                else if (_drTakt > 1500) { _drStart += " — Anfahrt zur Geraden zu lang"; DrehenAbschluss(); return; }
                break;

            case 1:                                     // Anlauf auf +2
                if (_drTakt > 5 && DrSteht(e))
                {
                    DrFahre(DrZelle(-3));
                    _drPhase = 2; _drTakt = 0;
                    _drSprungVor = SprungZahl;
                }
                break;

            case 2:                                     // Kehrtwende messen, dann Gegenbefehl
                if (_drRumpfErst < 0 && rumpf != _drRumpfVor) _drRumpfErst = _drGesamt;
                if (_drTurmErst < 0 && turm != _drTurmVor) _drTurmErst = _drGesamt;
                if (_drTurmErst >= 0 && _drTurmLetzt < 0 && _drTurmSoll >= 0 && turm == _drTurmSoll)
                    _drTurmLetzt = _drGesamt;
                if (_drTurmSoll < 0 && e.Path != null) _drTurmSoll = ZielBlick(e);
                if (_drRumpfErst >= 0 && _drAnfahrt < 0 && faehrt) _drAnfahrt = _drGesamt;
                if (_drAnfahrt >= 0 && faehrt && e.SchrittRichtung >= 0)
                    _drWegNormal = Mathf.Max(_drWegNormal, weg);
                // zweiter Schritt (Zelle +1 verlassen), mitten drin → Gegenbefehl
                if (_drGegenTakt < 0 && e.Col == DrZelle(1).X && e.Row == DrZelle(1).Y
                    && e.Reserved != null && e.SchrittAnteil is > 0.3f and < 0.5f)
                {
                    _drAbbruchVor = SchrittAbgebrochen;
                    DrFahre(DrZelle(4));
                    _drGegenTakt = _drGesamt;
                    _drPhase = 3; _drTakt = 0;
                }
                else if (_drTakt > 600) { _drStart += " — Phase 2 ohne Gegenbefehl"; DrehenAbschluss(); return; }
                break;

            case 3:                                     // nach dem Gegenbefehl
                if (faehrt) _drWegNach = Mathf.Max(_drWegNach, weg);
                // ⭐ hangunabhängig: wie weit beginnt der laufende Schritt, gemessen an
                // einer Zelle? Aus der Zellmitte = 1,00; aus der Zwischenlage bis 2.
                if (e.Reserved is { } rz)
                {
                    var ziel = BodyCenterAt(e, rz.X, rz.Y);
                    float soll = ziel.DistanceTo(BodyCenterAt(e, e.Col, e.Row));
                    if (soll > 0.5f) _drStartLage = Mathf.Max(_drStartLage, ziel.DistanceTo(e.StepFrom) / soll);
                }
                if (_drTakt > 5 && DrSteht(e))
                {
                    _drAbbrueche = SchrittAbgebrochen - _drAbbruchVor;
                    _drSpruenge = SprungZahl - _drSprungVor;
                    _drGeister = 0;
                    for (int r = 0; r < _nav!.Height; r++)
                        for (int c = 0; c < _nav.Width; c++)
                            if (_nav.OccupantAt(c, r) == _drE && (c != e.Col || r != e.Row)) _drGeister++;
                    DrFahre(DrZelle(-3));
                    _drPhase = 4; _drTakt = 0;
                }
                else if (_drTakt > 900) { _drStart += " — Phase 3 kommt nicht zum Stehen"; DrehenAbschluss(); return; }
                break;

            case 4:                                     // Nachladen: fernes Ziel einspeisen
                if (_drHaltStart < 0)
                {
                    if (!(faehrt && e.SchrittAnteil > 0.1f)) break;
                    int feind = -1;
                    float best = -1;
                    for (int k = 0; k < _entities.Count; k++)
                    {
                        var f = _entities[k];
                        if (f.Dead || f.IsProp || f.Owner == e.Owner || f.Owner < 0) continue;
                        float dd = f.Pos.DistanceSquaredTo(e.Pos);
                        if (dd > best) { best = dd; feind = k; }
                    }
                    if (feind < 0) { _drPhase = 5; _drTakt = 0; break; }
                    int zb = ZielBlick(e);
                    _drHaltBlick = zb < 0 ? 0 : (zb + 4) % 8;
                    if (!TurmAlt) { e.AimFacing = _drHaltBlick; e.TurmWunsch = -1; e.TurmAussetzen = false; }
                    else _drHaltBlick = turm;
                    e.Target = feind; e.Ordered = false; e.Cooldown = 0.5f;
                    _drHaltStart = _drGesamt;
                    turm = TurmRichtung(e);
                    break;
                }
                if (_drNachladen < 0 && e.Cooldown <= 0) _drNachladen = _drGesamt - _drHaltStart;
                if (_drTurmLos < 0 && turm != _drHaltBlick) _drTurmLos = _drGesamt - _drHaltStart;
                if (_drTurmZurueck < 0 && _drGesamt - _drHaltStart >= 2 && turm == ZielBlick(e))
                    _drTurmZurueck = _drGesamt - _drHaltStart;
                if (DrSteht(e) && _drTakt > 5)
                {
                    // Endziel-Fahrt: ein Ziel schräg zur Bahn suchen
                    var here = new Vector2I(e.Col, e.Row);
                    var off = new[] { new Vector2I(6, 2), new Vector2I(-6, 2), new Vector2I(6, -2), new Vector2I(-6, -2),
                                      new Vector2I(2, 6), new Vector2I(-2, 6), new Vector2I(2, -6), new Vector2I(-2, -6),
                                      new Vector2I(5, 2), new Vector2I(-5, -2), new Vector2I(2, 5), new Vector2I(-2, -5) };
                    Vector2I? ziel = null;
                    foreach (var o in off)
                    {
                        var z = here + o;
                        if (!_nav!.CanEnter(z.X, z.Y, e.Move) || !_nav.IsFree(z.X, z.Y, e.Move, _drE)) continue;
                        var p = _nav.FindPath(here, z, e.Move, _drE);
                        if (p != null && p.Count > 0 && p.Count <= 9) { ziel = z; break; }
                    }
                    if (ziel == null) { _drStart += " — kein Schrägziel gefunden"; DrehenAbschluss(); return; }
                    DrFahre(ziel.Value);
                    _drPhase = 5; _drTakt = 0;
                }
                else if (_drTakt > 900) { _drStart += " — Phase 4 kommt nicht zum Stehen"; DrehenAbschluss(); return; }
                break;

            case 5:                                     // Turm aufs Endziel während der Fahrt
                // gezählt wird nur ohne Schussziel — mit Ziel zeigt der Turm aufs Ziel
                if (faehrt && e.Path != null && e.Reserved != null
                    && e.Target < 0 && e.TurmHalteZiel < 0 && e.AngriffsZelle == null)
                {
                    _drFahrTakte++;
                    if (turm == ZielBlick(e)) _drAufsZiel++;
                    else if (_drFehlZiel.Count < 12)
                        _drFehlZiel.Add($"T{_drGesamt}:({e.Col},{e.Row}) Turm {turm} Ziel {ZielBlick(e)} Rumpf {rumpf} Wunsch {e.TurmWunsch}");
                }
                if ((_drTakt > 5 && DrSteht(e)) || _drTakt > 900) { DrehenAbschluss(); return; }
                break;
        }
        _drPosVor = e.Pos;
        _drRumpfVor = rumpf;
        _drTurmVor = turm;
    }

    private void DrehenAbschluss()
    {
        DrehenFertig = true;
        var sb = new StringBuilder("drehen-check:\n");
        sb.AppendLine($"  Einheit: {_drStart}");
        sb.AppendLine($"  Schalter: koerperdrehung-alt {KoerperdrehungAlt}, turm-alt {TurmAlt}, gegenbefehl-alt {GegenbefehlAlt}");
        int rumpfT = _drRumpfErst >= 0 && _drAnfahrt >= 0 ? _drAnfahrt - _drRumpfErst : -1;
        int turmT = _drTurmErst >= 0 && _drTurmLetzt >= 0 ? _drTurmLetzt - _drTurmErst : -1;
        bool turmVor = _drTurmLetzt >= 0 && _drAnfahrt >= 0 && _drTurmLetzt < _drAnfahrt;
        int rumpfSoll = KoerperdrehungAlt ? 4 : 9;
        bool b = rumpfT == rumpfSoll;
        sb.AppendLine($"  B Rumpf 180°: {rumpfT} Takte bis Anfahrt (Soll {rumpfSoll}"
                    + (KoerperdrehungAlt ? ", Nullmodell" : "; --koerperdrehung-alt 4") + ")");
        // Nullmodell: der Turm klebt am Rumpf — gleicher erster Takt, keine eigene 6-Takt-Drehung
        bool c1 = TurmAlt ? _drTurmErst == _drRumpfErst && turmT != 6
                          : turmT == 6 && (turmVor || KoerperdrehungAlt);
        sb.AppendLine($"  C Turm 180°: {turmT} Takte erste→letzte Stufe, vor dem Rumpf fertig: {(turmVor ? "ja" : "nein")} "
                    + (TurmAlt ? "(Nullmodell: läuft mit dem Rumpf, nicht 6)" : "(Soll 6, ja)"));
        bool c2 = TurmAlt ? _drTurmZurueck is >= 0 and <= 3
                          : _drNachladen >= 0 && _drTurmZurueck >= _drNachladen && _drTurmZurueck <= _drNachladen + 9
                            && Mathf.Abs(_drTurmLos - _drNachladen) <= 2;
        sb.AppendLine($"  C Turm nach Reichweitenende: Nachladen endet nach {_drNachladen} Takten, Turm dreht los nach {_drTurmLos}, "
                    + $"zeigt wieder aufs Endziel nach {_drTurmZurueck} "
                    + (TurmAlt ? "(Nullmodell: sofort, ≤ 3)" : "(Soll: los = Nachladeende ±2, zurück ≤ Nachladen + 9)"));
        float anteil = _drFahrTakte > 0 ? (float)_drAufsZiel / _drFahrTakte : 0f;
        bool c3 = TurmAlt ? anteil < 0.9f : anteil >= 0.9f;
        sb.AppendLine($"  C Turm aufs Endziel in Fahrt: {_drAufsZiel}/{_drFahrTakte} Takte = {anteil * 100f:0.0} % "
                    + (TurmAlt ? "(Nullmodell: < 90 %)" : "(Soll ≥ 90 %)"));
        float verh = _drWegNormal > 0 ? _drWegNach / _drWegNormal : -1f;
        bool a = GegenbefehlAlt
            ? _drAbbrueche >= 1 && (_drStartLage > 1.1f || _drGeister > 0)
            : _drAbbrueche == 0 && _drStartLage is > 0 and <= 1.01f && _drGeister == 0;
        sb.AppendLine($"  A Gegenbefehl: Schrittabbrüche {_drAbbrueche}, Weg je Takt max {_drWegNach:0.00} px gegen normal "
                    + $"{_drWegNormal:0.00} px (Verhältnis {verh:0.00}), Geisterzellen {_drGeister}, Sprünge {_drSpruenge} "
                    + $"; Schrittstrecke/Zellstrecke max {_drStartLage:0.00} "
                    + (GegenbefehlAlt ? "(Nullmodell: Abbruch ≥ 1 und Strecke > 1,1 oder Geist)" : "(Soll Abbruch 0, Strecke 1,00, Geist 0)"));
        if (_drFehlZiel.Count > 0) sb.AppendLine("  C3-Abweichungen: " + string.Join(" | ", _drFehlZiel));
        sb.AppendLine($"  Zähler: Gegenbefehl ohne Abbruch {GegenbefehlOhneAbbruch}, Turmstufen {TurmStufen}, Takte {_drGesamt}");
        bool c = c1 && c2 && c3;
        sb.Append($"  Ergebnis: {(a && b && c ? "BESTANDEN" : "DURCHGEFALLEN")} — A {(a ? "ja" : "NEIN")}, "
                + $"B {(b ? "ja" : "NEIN")}, C {(c ? "ja" : "NEIN")} (C1 {(c1 ? "ja" : "NEIN")}, C2 {(c2 ? "ja" : "NEIN")}, C3 {(c3 ? "ja" : "NEIN")})");
        DrehenBerichtText = sb.ToString();
    }
}
