namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 22.10.2026, Paket N — zwei Netzprüfstände im Taktgeber, Zwei-Prozess-Lauf wie
/// <c>--net-check</c> (Aufruf siehe <see cref="NetGameRunner"/>).
///
/// <list type="bullet">
/// <item><c>--net-neustart-check[=T]</c> (Vorgabe 600): der MITSPIELER geht in Takt T den
/// ganzen Spielerweg — Menüknopf, Haupt-Menü Art 17, Knopf 6, Abfrage Art 10/3, »Ja« →
/// 980 über den Ring. <b>Soll:</b> 980 wirkt auf BEIDEN Maschinen genau einmal im selben
/// Takt, danach ist der Abdruck gleich dem des gemeinsamen Anfangs, und die Abdrücke
/// beider Seiten bleiben bis zum Ende gleich. <b>Nullmodell</b>
/// <c>--neustart-oertlich</c>: nur der Absender spielt den Startstand ein → die Abdrücke
/// laufen auseinander.</item>
/// <item><c>--pausenfenster-check</c>: der VERMITTLER drückt in Takt 300 P, der MITSPIELER
/// klickt in Takt 400 »Weiter« (Treffertest). <b>Soll:</b> auf beiden Maschinen steht Art
/// 40 vom selben Takt bis zum selben Takt, Pausenwort 1000 dazwischen, genau zwei 993.
/// <b>Nullmodell</b> <c>--pausenfenster-alt</c>: kein Fenster Art 40 → ROT (die Pause selbst
/// läuft weiter durch, ersatzweise über 993).</item>
/// </list>
/// </summary>
public sealed partial class NetGameRunner
{
    private int _nsCheck;
    private bool _pfCheck;
    private int _nsTakt = -1, _nsGesehen;
    private bool _nsGleich;
    private int _pfAuf = -1, _pfZu = -1, _pfPauseAn = -1, _pfPauseAus = -1;
    private Vector2 _pfLage, _pfGroesse;
    private int _pfLetztePause;

    private void PaketNTakt()
    {
        int r = _tick - _start;
        var mv = MapViewer.Aktiv;

        if (_nsCheck > 0)
        {
            if (r == _nsCheck && !_link.IsHost)
                Say($"   Takt {_tick}: NEUSTART-PROBE — {mv?.NetzProbeNeustart() ?? "kein MapViewer"}");
            if (_sim.NeustartAngewandt != _nsGesehen)
            {
                _nsGesehen = _sim.NeustartAngewandt;
                _nsTakt = _tick;
                _nsGleich = _sim.NeustartAbdruckNach == _sim.NetzStartAbdruck;
                Say($"   Takt {_tick}: 980 gewirkt (#{_nsGesehen}), Abdruck {_sim.NeustartAbdruckNach:X16}, " +
                    $"Anfang {_sim.NetzStartAbdruck:X16} -> {(_nsGleich ? "GLEICH" : "UNGLEICH")}");
            }
        }

        if (_pfCheck)
        {
            if (r == 300 && _link.IsHost) Say($"   Takt {_tick}: PAUSE-PROBE — {mv?.NetzProbeTasteP() ?? "kein MapViewer"}");
            if (r == 400 && !_link.IsHost) Say($"   Takt {_tick}: PAUSE-PROBE — {mv?.NetzProbeWeiter() ?? "kein MapViewer"}");
            var f = UI.PausenFenster.Offen;
            bool auf = f != null && f.Visible;
            if (auf && _pfAuf < 0) { _pfAuf = _tick; _pfLage = f!.Position; _pfGroesse = f.Size; }
            if (!auf && _pfAuf >= 0 && _pfZu < 0) _pfZu = _tick;
            int p = _sim.NetzPause;
            if (p != _pfLetztePause)
            {
                if (p == 1000 && _pfPauseAn < 0) _pfPauseAn = _tick;
                if (p == 0 && _pfPauseAn >= 0 && _pfPauseAus < 0) _pfPauseAus = _tick;
                Say($"   Takt {_tick}: Pausenwort {_pfLetztePause} -> {p}, Fenster Art 40 {(auf ? "OFFEN" : "zu")}");
                _pfLetztePause = p;
            }
        }
    }

    private bool PaketNUrteil()
    {
        bool ok = true;
        int n(short op) => _sim.AngenommenJeBefehl.TryGetValue(op, out int k) ? k : 0;
        if (_nsCheck > 0)
        {
            bool gut = _sim.NeustartAngewandt == 1 && _nsGleich;
            Say($"NEUSTART-ERGEBNIS: 980 gewirkt {_sim.NeustartAngewandt}x in Takt {_nsTakt}, Abdruck danach " +
                $"{_sim.NeustartAbdruckNach:X16}, Anfang {_sim.NetzStartAbdruck:X16} " +
                $"({(_nsGleich ? "gleich" : "UNGLEICH")}), angewandt 980={n(Simulation.Commands.CommandOp.Neustart)}" +
                (MapEntityLayer.NeustartOertlich ? " ⚠ NULLMODELL --neustart-oertlich" : "") +
                $" -> {(gut ? "BESTANDEN" : "DURCHGEFALLEN")}");
            ok &= gut;
        }
        if (_pfCheck)
        {
            int z = n(Simulation.Commands.CommandOp.Pause);
            var schirm = UI.WindowManager.Schirmmass();
            var soll = new Vector2((schirm.X - _pfGroesse.X) / 2f, schirm.Y / 5f);
            bool gut = _pfAuf >= 0 && _pfZu > _pfAuf && _pfPauseAn == _pfAuf && _pfPauseAus == _pfZu && z == 2
                       && (_pfLage - soll).Length() < 1f
                       && _pfGroesse == new Vector2(180 * UI.PausenFenster.Scale, 60 * UI.PausenFenster.Scale);
            Say($"PAUSENFENSTER-ERGEBNIS: Art 40 auf Takt {_pfAuf}, zu Takt {_pfZu}; Pausenwort 1000 von {_pfPauseAn} " +
                $"bis {_pfPauseAus}; Lage ({_pfLage.X:0},{_pfLage.Y:0}) Soll ({soll.X:0},{soll.Y:0}) bei Schirm " +
                $"{schirm.X:0}x{schirm.Y:0}, Größe {_pfGroesse.X:0}x{_pfGroesse.Y:0}; 993 angewandt {z}" +
                (UI.PausenFenster.Alt ? " ⚠ NULLMODELL --pausenfenster-alt" : "") +
                $" -> {(gut ? "BESTANDEN" : "DURCHGEFALLEN")}");
            ok &= gut;
        }
        return ok;
    }
}
