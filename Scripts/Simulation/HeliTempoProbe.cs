namespace AkteEuropaReborn.Rendering;

using System.Linq;
using Godot;

/// <summary>
/// ⭐ 23.09.2026 — <b>der Prüfstand zum Heli-Zweig</b> (Flugtempo.cs,
/// K21-Meldung »auch Helikopter sind teils sau langsam«).
///
/// <para>Beim Kartenstart fliegt in K20/K21 nichts (<c>jetzt in der Luft: 0</c>),
/// darum setzt der Prüfstand selbst einen Treibstoffheli (Art 13, sp_oben 8,
/// sp_unten 0 — die Werte aller 134 Nachschubhelis der Karten) 20 Zellen neben
/// eine eigene Einheit, deren Tank er auf 10 % stellt, und misst, mit welcher
/// Stufe der Heli bei ihr ankommt und wie tief er beim Anflug fällt.</para>
///
/// <para><b>SOLL neu:</b> kleinste Stufe mit Kundschaft = sp_oben − 2 = <b>6</b>,
/// Ankunft mit 6; geregelt in etwa jedem zweiten Takt.
/// <b>Nullmodell <c>--heli-stufe-alt</c>:</b> Flugzeugzweig — die Stufe fällt im
/// Anflug unter 4 und bei der Ankunft Richtung 0; geregelt in jedem Takt.</para>
///
/// <para>Eingeschaltet mit <c>--heli-tempo-probe</c>; der Schalter wird hier
/// selbst gelesen, damit MapViewer.cs unberührt bleibt.</para>
/// </summary>
public partial class MapEntityLayer
{
    private static readonly bool HeliTempoProbeAn =
        OS.GetCmdlineUserArgs().Contains("--heli-tempo-probe");

    private Special? _htpHeli;
    private int _htpStart = -1, _htpMin = int.MaxValue, _htpMax, _htpAnkunft = -1,
                _htpTakte, _htpRuns0, _htpRegel0, _htpKunde = -1, _htpAnflugTakte;
    private bool _htpFertig, _htpOben;

    /// <summary>Aus <c>UpdateAircraft</c>, einmal je Takt, VOR der Regelung.</summary>
    private void HeliTempoProbeTakt()
    {
        if (!HeliTempoProbeAn || _htpFertig) return;
        if (_htpHeli == null)
        {
            if (_taktNr < 20) return;
            int kunde = -1;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Owner != 0 || e.FuelMax <= 0) continue;
                kunde = i; break;
            }
            if (kunde < 0) { GD.Print("heli-tempo-probe: keine eigene Einheit mit Tank — nichts gemessen"); _htpFertig = true; return; }
            var k = _entities[kunde];
            k.Fuel = k.FuelMax / 10;
            int slot = 0;
            foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
            var start = k.Pos + new Vector2(20 * TileW, 0);
            if (start.X > MapZellSize().X * TileW - TileW) start = k.Pos - new Vector2(20 * TileW, 0);
            _htpHeli = new Special
            {
                Slot = slot, Kind = 13, Name = "Probeheli", TypeName = "Probeheli",
                Col = k.Col, Row = k.Row, Stored = false, Owner = 0, Pos = start,
                Speed = 8, StufeUnten = 0, Stufe = 0,
                Hp = 100, HpMax = 100, Fuel = 2000, FuelMax = 2000,
                Cargo = SupplyCargoFull,
            };
            _special.Add(_htpHeli);
            _htpStart = _taktNr; _htpKunde = kunde;
            _htpRuns0 = SupplyRuns; _htpRegel0 = FlugtempoGeregelt;
            GD.Print($"heli-tempo-probe: Probeheli Art 13 (sp_oben 8, sp_unten 0) bei Takt {_taktNr}, "
                   + $"20 Zellen neben Einheit {kunde} ({k.Col},{k.Row}), Tank auf {k.Fuel}/{k.FuelMax}"
                   + (HeliStufeAlt ? "   [--heli-stufe-alt: Nullmodell]" : ""));
            return;
        }
        var h = _htpHeli;
        _htpTakte++;
        // ⚠ Der Heli startet bei Stufe 0 — das Hochfahren ist kein Bremsen.
        // Gezaehlt wird das Minimum erst, wenn er sp_oben − 2 einmal erreicht hat.
        if (h.Stufe >= h.Speed - 2) _htpOben = true;
        if (h.Customer >= 0)
        {
            _htpAnflugTakte++;
            if (_htpOben) _htpMin = Mathf.Min(_htpMin, h.Stufe);
            _htpMax = Mathf.Max(_htpMax, h.Stufe);
            _htpAnkunft = h.Stufe;
        }
        if (SupplyRuns > _htpRuns0 || _htpTakte > 3000)
        {
            _htpFertig = true;
            int geregelt = FlugtempoGeregelt - _htpRegel0;
            bool neu = !HeliStufeAlt;
            bool ok = neu ? _htpMin >= 6 && _htpAnkunft >= 6 : _htpMin < 6;
            GD.Print($"heli-tempo-probe: {(SupplyRuns > _htpRuns0 ? "abgegeben" : "⚠ NICHT abgegeben")} nach "
                   + $"{_htpTakte} Takten ({_htpAnflugTakte} mit Kundschaft) · Stufe mit Kundschaft (nach dem Hochfahren) "
                   + $"min {(_htpMin == int.MaxValue ? -1 : _htpMin)} / max {_htpMax}, bei der Ankunft {_htpAnkunft} "
                   + $"(sp_oben 8) · {geregelt}x geregelt in {_htpTakte} Takten "
                   + $"(Tor zu {FlugtempoTorZu}x) · SOLL {(neu ? "min 6 = sp_oben−2" : "Nullmodell: min < 6")} -> "
                   + (ok ? "BESTANDEN" : "⚠ DURCHGEFALLEN"));
        }
    }

    /// <summary>Für die Messzeile: Helis in der Luft je Art, Stufe gegen sp_oben.</summary>
    private string HeliJeArt()
    {
        var helis = _special.Where(a => !a.Dead && !a.Stored && a.Kind >= 10).ToList();
        if (helis.Count == 0) return " · Helis in der Luft: 0";
        return " · Helis " + string.Join(", ", helis.GroupBy(a => a.Kind).OrderBy(g => g.Key)
            .Select(g => $"Art {g.Key}: {g.Count()}x Stufe {string.Join("/", g.Select(a => $"{a.Stufe}v{a.Speed}"))}"));
    }
}
