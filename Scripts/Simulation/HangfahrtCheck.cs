namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <c>--hangfahrt-check</c> (03.10.2026, bug-410, berichte/hangfahrt-zeichnen-fable.md §6):
/// ein eigenes Fahrzeug wird auf 60 % Leben gesetzt, angewählt und quer über
/// Hangzellen geschickt. Je Bild gezählt:
/// <list type="bullet">
/// <item>C — Zeichenzelle: ab Schrittanteil &gt; 0,5 die Zielzelle, sonst keine
/// (Nullmodell <c>--hangpose-zellende</c>: nie gesetzt).</item>
/// <item>E — Kippbild an der Kante gezeigt (Nullmodell <c>--kippbild-aus</c>: 0).</item>
/// <item>D — Fahrzittern 0 und 1 gesehen (Nullmodell <c>--fahrzittern-aus</c>: nur 0).</item>
/// <item>A — Balken-Hub Fahrzeug 25 = Rahmenoberkante Zeile·20 − Hub − 15 wie
/// 0x4B6F60 (Nullmodell <c>--balkenanker-alt</c>: 35).</item>
/// </list>
/// Mit <c>--shot=…</c> (ohne --headless) Bilder: gekippt und am Ende stehend.
/// </summary>
public partial class MapEntityLayer
{
    public static bool HangfahrtCheckAn;
    private int _hfEinheit = -1;
    private readonly List<Vector2I> _hfZiele = new();
    private int _hfZielNr, _hfBilder, _hfFahrend, _hfZelleRichtig, _hfZelleFalsch, _hfKipp, _hfStill;
    private readonly HashSet<int> _hfZittern = new();
    private int _hfHub = -1;

    /// <summary>Einheit wählen, beschädigen, anwählen, Hangziele sammeln. Null = keine Einheit.</summary>
    public string? HangfahrtStart()
    {
        if (_nav == null || _flagLookup == null) return null;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != 0 || e.Infantry >= 0) continue;
            if (e.Move != Simulation.NavGrid.MoveClass.Vehicle || e.HpMax <= 0 || e.Chassis == 0x11) continue;
            _hfEinheit = i;
            break;
        }
        if (_hfEinheit < 0) return null;
        var u = _entities[_hfEinheit];
        u.Hp = u.HpMax * 6 / 10;
        // Hangzellen der Klasse 1..4 nach Abstand; Ziel = drei Zellen jenseits der
        // Zelle QUER zur Kante (Klasse 1/3: W–O, 2/4: N–S), wie die Kipptafel 0x4FA4B0.
        var kandidaten = _flagLookup
            .Where(kv => kv.Value is >= 1 and <= 4)
            .Select(kv => (c: kv.Key.Item1, r: kv.Key.Item2, k: kv.Value))
            .OrderBy(z => (z.c - u.Col) * (z.c - u.Col) + (z.r - u.Row) * (z.r - u.Row))
            .Take(400);
        foreach (var z in kandidaten)
        {
            var d = z.k is 1 or 3 ? new Vector2I(1, 0) : new Vector2I(0, 1);
            var a = new Vector2I(z.c, z.r) - d * 3;
            var b = new Vector2I(z.c, z.r) + d * 3;
            if (!_nav.CanEnter(a.X, a.Y, u.Move) || !_nav.CanEnter(b.X, b.Y, u.Move)) continue;
            if (!_nav.CanEnter(z.c, z.r, u.Move)) continue;
            _hfZiele.Add(a);
            _hfZiele.Add(b);
            if (_hfZiele.Count >= 12) break;
        }
        _sel.Clear();
        _sel.Add(_hfEinheit);
        SetPrimary();
        if (_hfZiele.Count > 0) PostMove(CellCenter(_hfZiele[0].X, _hfZiele[0].Y));
        return $"Platz {u.Slot} Typ {u.UnitType} bei ({u.Col},{u.Row}), Hp {u.Hp}/{u.HpMax}, "
             + $"{_hfZiele.Count / 2} Hangquerungen geplant";
    }

    /// <summary>Ein Bild messen. Rückgabe: Kamera-Lage, wenn gerade ein Kippbild steht.</summary>
    public (Vector2 pos, bool kipp, bool fertig) HangfahrtMessen()
    {
        if (_hfEinheit < 0) return (Vector2.Zero, false, true);
        var e = _entities[_hfEinheit];
        _hfBilder++;
        _hfHub = (int)Mathf.Round(BalkenHub(e));
        bool kipp = false;
        if (e.SchrittAnteil >= 0)
        {
            _hfFahrend++;
            _hfZittern.Add(e.Zittern);
            bool soll = e.SchrittAnteil > 0.5f;
            bool ist = e.Zeichenzelle != null && e.Zeichenzelle != new Vector2I(e.Col, e.Row);
            if (soll == ist) _hfZelleRichtig++; else _hfZelleFalsch++;
            int k = KippBild(e, SlopeClassOf(e));
            if (k >= 0 && GetKippTexture(e.UnitType, PoseOf(e), k) != null) { _hfKipp++; kipp = true; }
            _hfStill = 0;
        }
        else _hfStill++;
        // stillstehend: nächste Querung
        bool fertig = false;
        if (_hfStill > 30)
        {
            _hfZielNr++;
            if (_hfZielNr < _hfZiele.Count && _hfBilder < 60 * 240)
            {
                _sel.Clear(); _sel.Add(_hfEinheit); SetPrimary();
                PostMove(CellCenter(_hfZiele[_hfZielNr].X, _hfZiele[_hfZielNr].Y));
                _hfStill = 0;
            }
            else fertig = true;
        }
        return (e.Pos, kipp, fertig);
    }

    public string HangfahrtBericht()
    {
        var sb = new StringBuilder("hangfahrt-check:\n");
        int soll = BalkenankerAlt ? 35 : 25;
        bool c = HangposeZellende ? _hfZelleFalsch > 0 : _hfZelleFalsch == 0 && _hfZelleRichtig > 0;
        bool e = KippbildAus ? _hfKipp == 0 : _hfKipp > 0;
        bool d = FahrzitternAus ? !_hfZittern.Contains(1) : _hfZittern.SetEquals(new[] { 0, 1 });
        bool a = _hfHub == soll;
        sb.AppendLine($"  Bilder {_hfBilder}, fahrend {_hfFahrend}, Querungen {_hfZielNr}/{_hfZiele.Count}");
        sb.AppendLine($"  C Zeichenzelle: richtig {_hfZelleRichtig}, falsch {_hfZelleFalsch}"
                    + (HangposeZellende ? " (Nullmodell --hangpose-zellende: falsch > 0 erwartet)" : ""));
        sb.AppendLine($"  E Kippbild gezeigt: {_hfKipp} Bilder (Laden: {KippGezeigt} da, {KippFehlt} fehlt)");
        sb.AppendLine($"  D Zittern gesehen: {{{string.Join(",", _hfZittern.OrderBy(x => x))}}}");
        sb.AppendLine($"  A Balken-Hub Fahrzeug: {_hfHub} (Soll {soll})");
        sb.Append($"  Ergebnis: {(a && c && d && e && _hfFahrend > 0 ? "BESTANDEN" : "DURCHGEFALLEN")}"
                + $" — A {(a ? "ja" : "NEIN")}, C {(c ? "ja" : "NEIN")}, D {(d ? "ja" : "NEIN")}, E {(e ? "ja" : "NEIN")}");
        return sb.ToString();
    }
}
