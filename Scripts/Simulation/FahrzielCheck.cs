namespace AkteEuropaReborn.Rendering;

using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// <b><c>--fahrziel-check</c> — bug-452 (KayelGee R4, Meldung 10).</b> Ein
/// eigenes bewaffnetes Fahrzeug beschiesst einen festgestellten Gegner in
/// Reichweite; dann bekommt es einen Fahrbefehl quer zur Ziellinie, der es in
/// Reichweite laesst.
///
/// <para>Gemessen, im Spieltakt:</para>
/// <list type="number">
///   <item>Ziel im Takt nach dem Befehl noch gesetzt (Soll ja; Nullmodell
///   <c>--fahrbefehl-raeumt-ziel</c>: nein);</item>
///   <item>Anteil der Fahrtakte (ab Takt 12, wenn der Turm aufs Ziel gedreht
///   haben kann), in denen der Turm hoechstens eine Stufe neben der
///   Zielrichtung steht — Soll ≥ 90 % — und nie zum Fahrziel gedreht ist.</item>
/// </list>
/// Der Gegner wird festgehalten (unbeweglich, volle Trefferpunkte), damit er
/// weder wegfaehrt noch stirbt.
/// </summary>
public partial class MapEntityLayer
{
    public static bool FahrzielCheckAn;
    public bool FahrzielFertig { get; private set; }
    public string FahrzielBericht { get; private set; } = "";

    private int _fzPhase, _fzE = -1, _fzZ = -1, _fzTakt, _fzGesamt;
    private int _fzFahrtakte, _fzImZiel, _fzNahZiel, _fzWeg, _fzAusReichweite;
    private bool _fzZielNachBefehl;
    private string _fzStart = "";

    private void FahrzielCheckTakt()
    {
        if (FahrzielFertig || _nav == null) return;
        _fzGesamt++;
        if (_fzPhase == 0)
        {
            if (!FzAufbauen()) { if (_fzGesamt > 120) FzAbschluss("KEIN Aufbau: " + _fzStart); return; }
            _fzPhase = 1; _fzTakt = 0;
            return;
        }
        var e = _entities[_fzE];
        var z = _entities[_fzZ];
        z.Hp = z.HpMax;
        if (e.Dead) { FzAbschluss("Messeinheit verloren"); return; }
        _fzTakt++;
        if (_fzPhase == 1)
        {
            // erst schiessen lassen
            if (e.Target != _fzZ) { e.Target = _fzZ; e.Ordered = true; }
            if (_fzTakt < 20) return;
            // quer zur Ziellinie fahren, 3 Zellen
            var d = new Vector2I(z.Col - e.Col, z.Row - e.Row);
            var quer = new Vector2I(-Mathf.Sign(d.Y), Mathf.Sign(d.X));
            if (quer == Vector2I.Zero) quer = new Vector2I(0, 1);
            Vector2I? ziel = null;
            foreach (int sgn in new[] { 1, -1 })
                for (int n = 3; n >= 2 && ziel == null; n--)
                {
                    var c = new Vector2I(e.Col, e.Row) + quer * sgn * n;
                    if (_nav.InBounds(c.X, c.Y) && _nav.CanEnter(c.X, c.Y, e.Move) && _nav.IsFree(c.X, c.Y, e.Move, _fzE))
                        ziel = c;
                }
            if (ziel is not { } g) { FzAbschluss("keine freie Zelle quer zur Ziellinie"); return; }
            Emit(CommandRecord.Make(CommandOp.Move, (byte)ViewPlayer, (short)_fzE, (short)g.X, (short)g.Y, 0));
            _fzStart += $", Fahrt quer nach ({g.X},{g.Y})";
            _fzPhase = 2; _fzTakt = 0;
            return;
        }
        if (_fzPhase == 2)
        {
            if (_fzTakt == 2) _fzZielNachBefehl = e.Target == _fzZ;
            bool faehrt = e.Path != null || e.Reserved != null;
            if (_fzTakt >= 12 && faehrt)
            {
                if (CellDistance(e, z) > RangeOf(e)) _fzAusReichweite++;
                else
                {
                    _fzFahrtakte++;
                    int diff = ((TurmRichtung(e) - DirToFacing(z.Pos - e.Pos)) % 8 + 8) % 8;
                    if (diff == 0) _fzImZiel++;
                    if (diff is 0 or 1 or 7) _fzNahZiel++;
                    // zum ENDZIEL statt zum Ziel: das ist der gemeldete Fehler
                    var g = new Vector2((e.Goal.X - e.Col) * TileW, (e.Goal.Y - e.Row) * TileH);
                    if (diff != 0 && g.LengthSquared() > 0.01f && TurmRichtung(e) == DirToFacing(g)) _fzWeg++;
                }
            }
            if (_fzTakt > 300 || (_fzTakt > 12 && !faehrt)) FzAbschluss(null);
        }
    }

    private bool FzAufbauen()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer || e.Infantry >= 0 || !e.Mobile) continue;
            if (e.Move != Simulation.NavGrid.MoveClass.Vehicle || e.Weapon is < 1 or > 40 || e.GameUnitType > 2) continue;
            if ((e.FuelMax > 0 && e.Fuel < 40) || e.DugIn || !TurmEigen(e)) continue;
            int r = Mathf.Max(2, (int)RangeOf(e) - 1);
            // ein Gegner, der festgestellt werden kann
            int feind = -1;
            for (int k = 0; k < _entities.Count && feind < 0; k++)
            {
                var f = _entities[k];
                if (f.Dead || f.IsBuilding || f.IsProp || f.Infantry >= 0 || !IsHostile(e, f) || f.GameUnitType > 2) continue;
                feind = k;
            }
            if (feind < 0) { _fzStart = "kein Gegner"; return false; }
            foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
            {
                var c = new Vector2I(e.Col, e.Row) + d * r;
                if (!_nav.InBounds(c.X, c.Y) || !_nav.IsFree(c.X, c.Y, _entities[feind].Move, feind)) continue;
                AwStelle(feind, c);
                var f = _entities[feind];
                f.Mobile = false; f.Hp = f.HpMax;
                _fzE = i; _fzZ = feind;
                e.Cooldown = 0; e.Target = feind; e.Ordered = true;
                _fzStart = $"Platz {e.Slot} Waffe {e.Weapon} Reichweite {RangeOf(e):0.0} bei ({e.Col},{e.Row}), "
                         + $"Gegner Platz {f.Slot} auf ({c.X},{c.Y})";
                return true;
            }
        }
        _fzStart = "kein Fahrzeug mit eigenem Turm und freier Zelle";
        return false;
    }

    private void FzAbschluss(string? fehler)
    {
        FahrzielFertig = true;
        if (fehler != null) { FahrzielBericht = "fahrziel-check: NICHT GEMESSEN — " + fehler; return; }
        int anteil = _fzFahrtakte == 0 ? 0 : 100 * _fzImZiel / _fzFahrtakte;
        int nah = _fzFahrtakte == 0 ? 0 : 100 * _fzNahZiel / _fzFahrtakte;
        // Der Turm dreht eine Stufe je zwei Takte (bug-406); faehrt die Einheit
        // nah am Ziel quer, laeuft die Zielrichtung ihm um eine Stufe davon. Das
        // ist Nachdrehen, kein Fehler — gewertet wird »hoechstens eine Stufe
        // daneben« und »nie in Fahrtrichtung statt aufs Ziel«.
        bool ok = _fzZielNachBefehl && _fzFahrtakte >= 5 && nah >= 90 && _fzWeg == 0;
        FahrzielBericht = "fahrziel-check\n"
            + $"  {_fzStart}\n"
            + $"  Ziel nach dem Fahrbefehl: {(_fzZielNachBefehl ? "behalten" : "GELOESCHT")}\n"
            + $"  Turm in Zielrichtung: genau {_fzImZiel}/{_fzFahrtakte} ({anteil} %), hoechstens eine Stufe daneben "
            + $"{_fzNahZiel}/{_fzFahrtakte} ({nah} %), zum Fahrziel gedreht {_fzWeg}, "
            + $"{_fzAusReichweite} Takte ausser Reichweite\n"
            + $"  Muendungsfeuer (bug-454): {MuendungSchuesse} Schuesse, davon {MuendungAmTurm} mit Turm neben der "
            + $"Zielrichtung — dort sitzt das Feuer jetzt am Turm{(MuendungAlt ? " (Nullmodell --muendung-alt: am Ziel)" : "")}\n"
            + (ok ? "fahrziel-check: BESTANDEN" : "fahrziel-check: DURCHGEFALLEN")
            + (FahrbefehlRaeumtZiel ? " (Nullmodell --fahrbefehl-raeumt-ziel)" : "");
    }
}
