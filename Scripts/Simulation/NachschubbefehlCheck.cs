namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--nachschubbefehl-check</c> (bug-519, KayelGee R5#7). Bei 2 s: der erste
/// eigene Treibstoffheli (Art 13, nicht eingelagert, mit Ladung) wird gewaehlt;
/// eine eigene Bodeneinheit mit Sprit bekommt 90 % (⚠ EINGRIFF, gesagt: ueber der
/// 65-%-Schwelle, die der Heli von selbst nie nimmt). Dann der Zeiger ueber ihr
/// und der Klick ueber <see cref="PostNachschubKlick"/> wie in MapViewer.
/// Soll: Zeiger Tanken, Befehl angenommen, nach hoechstens 60 s Sprit 100 %.
/// Nullmodell <c>--nachschubbefehl-alt</c>: kein Zeiger, kein Flug.
/// </summary>
public partial class MapEntityLayer
{
    public static bool NachschubbefehlCheckAn;
    private int _nbHeli = -1, _nbKunde = -1, _nbPhase;
    private float _nbAb, _nbVoll = -1;
    private int _nbSek = -1;
    private string _nbStart = "";

    public void NachschubbefehlTakt()
    {
        if (!NachschubbefehlCheckAn || _nbPhase >= 2 || _clock < 2f) return;
        if (_nbPhase == 0)
        {
            _nbPhase = 1; _nbAb = _clock;
            for (int k = 0; k < _special.Count; k++)
            {
                var s = _special[k];
                if (s.Dead || s.Stored || s.Kind != 13 || s.Owner != ViewPlayer || s.Cargo <= 0) continue;
                _nbHeli = k; break;
            }
            if (_nbHeli < 0)
            {
                // ⚠ ein eingelagerter Heli wird fuer die Messung in die Luft gesetzt
                for (int k = 0; k < _special.Count; k++)
                {
                    var s = _special[k];
                    if (s.Dead || s.Kind != 13 || s.Owner != ViewPlayer) continue;
                    s.Stored = false; s.Cargo = SupplyCargoFull; _nbHeli = k;
                    _nbStart = "⚠ EINGRIFF: Heli aus dem Hangar gesetzt, Ladung voll; ";
                    break;
                }
            }
            var a = _nbHeli >= 0 ? _special[_nbHeli] : null;
            if (a == null) { GD.Print("nachschubbefehl-check: kein eigener Treibstoffheli — sagt NICHTS"); _nbPhase = 2; return; }
            // der Kunde mit dem GROESSTEN Abstand zum naechsten Gegner — sonst stirbt
            // er auf einer Kampfkarte unterwegs (DM_7, erster Lauf), und der Heli
            // laesst ihn zu Recht los
            float best = -1f;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer || e.Infantry >= 0 || e.FuelMax <= 0 || !e.Mobile) continue;
                float frei = float.MaxValue;
                foreach (var g in _entities)
                    if (!g.Dead && !g.IsProp && g.Owner is >= 0 and <= 7 && g.Owner != ViewPlayer && !Allied(ViewPlayer, g.Owner))
                        frei = Mathf.Min(frei, Mathf.Max(Mathf.Abs(g.Col - e.Col), Mathf.Abs(g.Row - e.Row)));
                if (frei > best) { best = frei; _nbKunde = i; }
            }
            if (_nbKunde < 0) { GD.Print("nachschubbefehl-check: keine eigene Einheit mit Sprit — sagt NICHTS"); _nbPhase = 2; return; }
            var c = _entities[_nbKunde];
            c.Fuel = c.FuelMax * 9 / 10;
            _sel.Clear(); _selAir = _nbHeli; SetPrimary();
            var hint = CursorHintAt(c.Pos);
            int vorher = NachschubBefehleAngenommen;
            bool klick = PostNachschubKlick(c.Pos);
            _nbStart += $"Heli {a.Slot} (Ladung {a.Cargo}) bei ({a.Col},{a.Row}), Kunde {LabelOf(c)} Platz {c.Slot} bei ({c.Col},{c.Row}), " +
                        $"{a.Pos.DistanceTo(c.Pos) / TileW:0.0} Zellen weit, Sprit {c.Fuel}/{c.FuelMax}; Zeiger {hint}, Klick {(klick ? "BEFIEHLT" : "nichts")}";
            _ = vorher;
            return;
        }
        var heli = _special[_nbHeli];
        var kunde = _entities[_nbKunde];
        if ((int)(_clock - _nbAb) != _nbSek && _nbSek < 8)
        {
            _nbSek = (int)(_clock - _nbAb);
            GD.Print($"nachschubbefehl-check t+{_nbSek}s: Kunde {heli.Customer}, Ziel {heli.Goal}, Spielerziel {heli.PlayerGoal}, " +
                     $"Lage ({heli.Col},{heli.Row}), Hoehe {heli.Alt}/{heli.Sollhoehe}, Ladung {heli.Cargo}, Order {heli.Order}");
        }
        if (_nbVoll < 0 && kunde.Fuel >= kunde.FuelMax) _nbVoll = _clock - _nbAb;
        if (_nbVoll >= 0 || _clock >= _nbAb + 60f)
        {
            _nbPhase = 2;
            bool ok = _nbVoll >= 0 && !NachschubbefehlAlt;
            GD.Print("nachschubbefehl-check" + (NachschubbefehlAlt ? " (⚠ Nullmodell --nachschubbefehl-alt)" : "") + ":" +
                     $"\n   {_nbStart}" +
                     $"\n   Befehle angenommen {NachschubBefehleAngenommen}; Heli Kunde {(heli.Customer == _nbKunde ? "= die Einheit" : heli.Customer.ToString())}, " +
                     $"Abstand jetzt {heli.Pos.DistanceTo(kunde.Pos) / TileW:0.0} Zellen" +
                     $"\n   Sprit {kunde.Fuel}/{kunde.FuelMax}{(_nbVoll >= 0 ? $", voll nach {_nbVoll:0.0} s" : ", NICHT voll nach 60 s")}" +
                     $"\n   {(NachschubbefehlAlt ? (_nbVoll < 0 ? "Nullmodell wie erwartet: kein Flug" : "⚠ Nullmodell tankte trotzdem") : ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        }
    }
}
