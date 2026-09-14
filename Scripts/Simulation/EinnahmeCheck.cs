namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>--einnahme-check — was die Einnahme ausser dem Besitzer noch aendert</b>
/// (14.09.2026, seine zwei Meldungen aus Kampagne 13, siehe
/// Simulation/EinnahmeAbschluss.cs).
///
/// <para><b>Fall R (Route):</b> ein Wagen von Spieler P pendelt Fabrik → Basis
/// (beide P). Nach der ersten Einfahrt wird die Fabrik von uns eingenommen
/// (<c>CaptureDone</c>). Soll: Route steht, danach hoechstens EINE Einfahrt
/// (der Nachlauf, §3.2 des Berichts), kein Warenumschlag mehr.
/// Nullmodell <c>--einnahme-routen-alt</c>: der Kreislauf muss wieder da sein.</para>
///
/// <para><b>Fall I (Insassen):</b> eine Basis von Spieler P bekommt eine eingefahrene
/// Einheit und einen Depoteintrag, dann wird sie eingenommen. Soll: Garage und
/// Depot leer, die Einheit geloescht. Nullmodell <c>--einnahme-insassen-alt</c>.</para>
///
/// <para><b>Fall T (Gebaeudetod):</b> eine Route mit Ziel = ein Gebaeude, das
/// Gebaeude stirbt (<c>Kill</c>). Soll: Ziel leer, Route steht.</para>
///
/// <para>⚠ EINGRIFFE: Besitzer von Fabrik, Basis und Wagen; ein Fahrzeug wird zum
/// Transporter; Lager der Fabrik W/F/S := 40; die Einnahme wird direkt
/// abgeschlossen (kein Belagerer); ein Gebaeude wird getoetet.</para>
/// </summary>
public partial class MapEntityLayer
{
    private bool _enCheckAn;
    private int _enStart, _enWagen = -1, _enFabrik = -1, _enBasis = -1, _enP = -1;
    private int _enEinnahmeTakt = -1, _enEinfahrtenVor, _enEinfahrtenNach, _enLetzteUkol = -1;
    private int _enUmschlagBei = 0, _enUmschlagNach = 0, _enEinfahrtenAnderswo, _enPrevFahrziel = -1, _enPrevUmschlag;
    private string _enNotiz = "", _enInsassen = "", _enTod = "";
    private bool _enInsassenOk, _enTodOk;

    public void EinnahmeCheckStart()
    {
        if (_nav == null) return;
        _enCheckAn = true;
        _enStart = _taktNr;

        // ---- Fall R: das engste Fabrik-Basis-Paar ----------------------------
        int bestF = -1, bestB = -1, bestD = int.MaxValue;
        for (int fi = 0; fi < _entities.Count; fi++)
        {
            var f = _entities[fi];
            if (!f.IsBuilding || f.IsProp || f.Dead || f.BType is not (2 or 3 or 4) || f.DoorCells.Count == 0) continue;
            for (int bi = 0; bi < _entities.Count; bi++)
            {
                var b = _entities[bi];
                if (!b.IsBuilding || b.IsProp || b.Dead || b.BType != 1) continue;
                int d = Mathf.Max(Mathf.Abs(b.Col - f.Col), Mathf.Abs(b.Row - f.Row));
                if (d < bestD) { bestD = d; bestF = fi; bestB = bi; }
            }
        }
        _enP = ViewPlayer == 1 ? 2 : 1;
        if (bestF < 0) { _enNotiz = "kein Fabrik-Basis-Paar"; }
        else
        {
            for (int k = 0; k < _entities.Count; k++)
            {
                var x = _entities[k];
                if (x.Dead || x.IsProp || x.IsBuilding || !x.Mobile || x.Infantry >= 0 || x.GameUnitType != 0
                    || x.Ukol >= 50 || Untergestellt(x)) continue;
                _enWagen = k; break;
            }
            if (_enWagen < 0) _enNotiz = "kein Fahrzeug";
            else
            {
                _enFabrik = bestF; _enBasis = bestB;
                var f = _entities[bestF]; var b = _entities[bestB]; var u = _entities[_enWagen];
                f.Owner = f.Team = f.ShownOwner = _enP; b.Owner = b.Team = b.ShownOwner = _enP;
                u.Owner = _enP; u.Team = _enP;
                f.StockW = System.Math.Max(f.StockW, 40); f.StockF = System.Math.Max(f.StockF, 40); f.StockS = System.Math.Max(f.StockS, 40);
                u.Part = TransporterTeil; u.Weapon = 0; u.FuelMax = System.Math.Max(u.FuelMax, 999); u.Fuel = u.FuelMax;
                u.Target = -1; u.Path = null; u.Orders.Clear(); u.Ordered = false; u.Ukol = UkolFrei;
                _sel.Remove(_enWagen);
                var tuer = new Vector2I(f.Col + f.DoorCells[0].Col, f.Row + f.DoorCells[0].Row);
                var z = _nav.NearestFree(tuer + new Vector2I(0, 4), u.Move, _enWagen) ?? tuer + new Vector2I(0, 4);
                ProbeVersetzen(_enWagen, z.X, z.Y);
                var r = RouteVon(u) ?? RouteAnlegen(u);
                if (r == null) { _enNotiz = "kein Umschlagsatz"; _enWagen = -1; }
                else
                {
                    r.Ziel = b.Slot;
                    r.Quelle[0] = f.Slot; r.Quelle[1] = r.Quelle[2] = r.Quelle[3] = -1;
                    RouteStarten(u, r);
                    _enNotiz = $"Fabrik Platz {f.Slot} ({f.Col},{f.Row}) Tuer 0 ({tuer.X},{tuer.Y}), Basis Platz {b.Slot}, "
                             + $"Abstand {bestD}, Wagen {_enWagen} von Spieler {_enP} auf ({u.Col},{u.Row})";
                }
            }
        }
        GD.Print($"einnahme-check: R {_enNotiz}");

        // ---- Fall I: eine andere Basis mit Insassen ---------------------------
        int basisI = -1;
        for (int k = 0; k < _entities.Count; k++)
        {
            var b = _entities[k];
            if (b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 1 && k != _enBasis && b.DoorCells.Count > 0) { basisI = k; break; }
        }
        if (basisI < 0) _enInsassen = "⚠ keine zweite Basis — sagt NICHTS";
        else
        {
            var b = _entities[basisI];
            b.Owner = b.Team = b.ShownOwner = _enP;
            int ui = -1;
            for (int k = 0; k < _entities.Count; k++)
            {
                var x = _entities[k];
                if (k == _enWagen || x.Dead || x.IsProp || x.IsBuilding || !x.Mobile || x.GameUnitType != 0
                    || x.Ukol >= 50 || Untergestellt(x)) continue;
                ui = k; break;
            }
            if (ui < 0) _enInsassen = "⚠ keine Einheit zum Einfahren — sagt NICHTS";
            else
            {
                var u = _entities[ui];
                u.Owner = u.Team = _enP;
                Einfahren(b, ui, u);
                b.Depot.Add(0);
                int vorG = b.Garage.Count, vorD = b.Depot.Count;
                int geloeschtVor = InsassenGeloescht;
                CaptureDone(basisI, b, ViewPlayer);
                _enInsassenOk = vorG >= 1 && b.Garage.Count == 0 && b.Depot.Count == 0 && u.Dead && b.Owner == ViewPlayer;
                _enInsassen = $"Basis Platz {b.Slot}: vorher Garage {vorG} Depot {vorD}, nachher Garage {b.Garage.Count} "
                            + $"Depot {b.Depot.Count}, Einheit {ui} tot {u.Dead}, geloescht {InsassenGeloescht - geloeschtVor}, "
                            + $"Besitzer {b.Owner}";
            }
        }
        GD.Print($"einnahme-check: I {_enInsassen}");

        // ---- Fall T: Gebaeudetod streicht --------------------------------------
        Transportroute? rt = null;
        int opfer = -1;
        for (int k = 0; k < _entities.Count; k++)
        {
            var x = _entities[k];
            if (x.IsBuilding && !x.IsProp && !x.Dead && k != _enFabrik && k != _enBasis && k != basisI && x.BType is (2 or 3 or 4 or 1))
            { opfer = k; break; }
        }
        if (opfer >= 0)
        {
            rt = new Transportroute { Ziel = _entities[opfer].Slot, Gestartet = true };
            rt.Quelle[0] = _entities[opfer].Slot;
            _routen.Add(rt);
            Kill(opfer, _entities[opfer], -1, "einnahme-check Fall T");
            _enTodOk = rt.Ziel == -1 && rt.Quelle[0] == -1 && !rt.Gestartet;
            _enTod = $"Gebaeude {opfer} Art {_entities[opfer].BType}: Route danach Ziel {rt.Ziel} Quelle0 {rt.Quelle[0]} "
                   + $"gestartet {rt.Gestartet}";
            _routen.Remove(rt);
        }
        else _enTod = "⚠ kein Gebaeude — sagt NICHTS";
        GD.Print($"einnahme-check: T {_enTod}");
    }

    private void EinnahmeCheckTakt()
    {
        if (_enWagen < 0) return;
        var u = _entities[_enWagen];
        bool rein = u.Ukol == UkolImGebaeude && _enLetzteUkol != UkolImGebaeude;
        _enLetzteUkol = u.Ukol;
        int umschlag = RouteVon(u) is { } r ? r.GeladenGesamt + r.AbgeladenGesamt : 0;
        if (_enEinnahmeTakt < 0)
        {
            if (rein) _enEinfahrtenVor++;
            // eingenommen wird, sobald der Wagen einmal drin war und wieder draussen ist
            if (_enEinfahrtenVor > 0 && u.Ukol == UkolFrei)
            {
                _enEinnahmeTakt = _taktNr - _enStart;
                _enUmschlagBei = umschlag;
                var f = _entities[_enFabrik];
                CaptureDone(_enFabrik, f, ViewPlayer);
            }
        }
        else
        {
            // ⚠ NUR die eingenommene Fabrik zaehlt: der stehende Wagen kommt im Original
            // in den Pool der KI (0x4BAEB0) und darf eine ANDERE Route bekommen
            // (erster Lauf: Quelle 9 -> Ziel 7, das ist kein Fehler).
            var f = _entities[_enFabrik];
            int fahrziel = RouteVon(u)?.Fahrziel ?? -1;
            if (rein && fahrziel == f.Slot) _enEinfahrtenNach++;
            if (rein && fahrziel != f.Slot) _enEinfahrtenAnderswo++;
            // Umschlag an DIESER Fabrik: der Zaehler des Wagens waechst in dem Takt, in
            // dem er mit Fahrziel == Fabrik umlaedt (UKOL 53/25 raeumen das Fahrziel danach).
            if (_enPrevFahrziel == f.Slot && umschlag > _enPrevUmschlag) _enUmschlagNach += umschlag - _enPrevUmschlag;
        }
        _enPrevFahrziel = RouteVon(u)?.Fahrziel ?? -1;
        _enPrevUmschlag = umschlag;
    }

    public string EinnahmeCheckLine()
    {
        var sb = new System.Text.StringBuilder("einnahme-check\n");
        if (!_enCheckAn) return sb.Append("  nicht gestartet — der Lauf sagt NICHTS").ToString();
        int takte = _taktNr - _enStart;
        bool ok = true;
        if (_enWagen < 0) { ok = false; sb.Append($"  R ⚠ nicht herstellbar ({_enNotiz}) — sagt NICHTS\n"); }
        else
        {
            var u = _entities[_enWagen];
            var r = RouteVon(u);
            int nach = _enEinnahmeTakt < 0 ? 0 : takte - _enEinnahmeTakt;
            var f = _entities[_enFabrik];
            bool quelleNochDa = r != null && (r.Ziel == f.Slot || System.Array.IndexOf(r.Quelle, f.Slot) >= 0);
            bool soll = _enEinnahmeTakt >= 0 && nach >= 1500 && !quelleNochDa
                        && _enEinfahrtenNach <= 1 && _enUmschlagNach == 0;
            ok &= soll;
            sb.Append($"  R Einfahrten vor der Einnahme {_enEinfahrtenVor}, eingenommen nach "
                    + $"{(_enEinnahmeTakt >= 0 ? $"{_enEinnahmeTakt} Takten" : "NIE")}; danach in {nach} Takten an der "
                    + $"eingenommenen Fabrik {f.Slot} (Besitzer jetzt {f.Owner}): Einfahrten {_enEinfahrtenNach} (Soll ≤ 1), "
                    + $"Umschlag {_enUmschlagNach} (Soll 0), Route fuehrt sie noch: {quelleNochDa} (Soll False); "
                    + $"anderswo eingefahren {_enEinfahrtenAnderswo}; Route jetzt gestartet {r?.Gestartet}, Ziel {r?.Ziel}, "
                    + $"Quelle0 {r?.Quelle[0]}, Wagen ({u.Col},{u.Row}) UKOL {u.Ukol}  {(soll ? "ja" : "NEIN")}\n");
        }
        ok &= _enInsassenOk;
        sb.Append($"  I {_enInsassen}  {(_enInsassenOk ? "ja" : "NEIN")}\n");
        ok &= _enTodOk;
        sb.Append($"  T {_enTod}  {(_enTodOk ? "ja" : "NEIN")}\n");
        sb.Append($"  gestrichen {RoutenGestrichen}, angehalten {RoutenAngehalten}, Insassen geloescht {InsassenGeloescht}, "
                + $"Depoteintraege {DepotEintraegeGeloescht}; --einnahme-routen-alt {EinnahmeRoutenAlt}, "
                + $"--einnahme-insassen-alt {EinnahmeInsassenAlt}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
