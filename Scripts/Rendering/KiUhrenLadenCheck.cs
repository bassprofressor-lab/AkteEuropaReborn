namespace AkteEuropaReborn.Rendering;

using System.Linq;
using Godot;

/// <summary>
/// ⭐ 30.10.2026 — die Schalter und Prüfstände von bug-682..684
/// (berichte/bau-spielstand-ki-klon.md). Aus <see cref="P2Schalter"/>, <c>P2Takt</c> und
/// <c>P2NachLaden</c> (Rendering/PaketP2Lauf.cs).
///
/// <para><c>--ki-uhren-laden-check</c> (bug-683, kopflos, Gefecht, z. B.
/// <c>--skirmish=map_NET07</c>): läuft bis zur ersten Gefechtsgruppe, dann 3 s weiter, und
/// speichert. <b>Ohne Laden</b> läuft das Spiel weiter bis zur nächsten Gruppe DESSELBEN
/// Spielers (Bezug). Dann wird der Stand geladen (Szenenwechsel, ganzer Startzweig) und
/// wieder bis zur nächsten Gruppe gespielt. <b>Soll:</b> AttackTimer nach dem Laden =
/// vorher, die Gruppe nach dem Laden kommt im selben Abstand zum Speichern wie ohne Laden
/// (± 1 Denktakt; der Würfel steht nicht im Stand), »Feind naht« in den ersten 5 s gleich
/// oft wie ohne Laden. <b>Nullmodell</b> <c>--ki-uhren-spielstand-alt</c>: Gruppe nach
/// ≤ 5 s → DURCHGEFALLEN.</para>
///
/// <para><c>--klon-check[=S]</c> (bug-684): nach S Sekunden (Vorgabe 60)
/// <see cref="MapEntityLayer.KlonCheck"/>.</para>
/// </summary>
public partial class MapViewer
{
    private static bool _kuAn;
    private static float _klonNach = -1f;

    /// <summary>Die Schalter von bug-682..684; true = verbraucht.</summary>
    private bool SkkSchalter(string a)
    {
        switch (a)
        {
            case "--gefecht-laden-besitz-alt": MapEntityLayer.GefechtLadenBesitzAlt = true; return true;   // bug-682
            case "--ki-uhren-spielstand-alt": MapEntityLayer.KiUhrenSpielstandAlt = true; return true;     // bug-683
            case "--ki-uhren-laden-check": _kuAn = true; UI.HelpWindow.Suppressed = true; return true;      // bug-683
            case "--klon-flach": MapEntityLayer.KlonFlach = true; return true;                               // bug-684
            case "--klon-check": _klonNach = 60f; UI.HelpWindow.Suppressed = true; return true;               // bug-684
        }
        if (a.StartsWith("--klon-check=") &&
            float.TryParse(a["--klon-check=".Length..], System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float s))
        { _klonNach = s; UI.HelpWindow.Suppressed = true; return true; }
        return false;
    }

    // ---- --ki-uhren-laden-check ------------------------------------------------------

    /// <summary>0 wartet auf die erste Gruppe, 1 = 3 s danach, 2 = gespeichert, Bezug läuft,
    /// 3 = laedt, 4 = geladen, misst, 5 = fertig.</summary>
    private static int _kuPhase;
    private static int _kuP = -1, _kuLogNach, _kuFnVorher, _kuFnBezug;
    private static float _kuGruppeUhr, _kuSpeicherUhr, _kuTimerVorher, _kuFnRestVorher, _kuBezugAbstand = -1f, _kuDenk;
    private static int _kuTaktVorher;
    private static string _kuDatei = "";
    private float _kuLadeUhr = -1f, _kuTimerNachher = float.NaN, _kuFnRestNachher;
    private int _kuLogLaden, _kuFnLaden, _kuTaktNachher;
    private float _klonUhr;

    private void SkkTakt(double delta)
    {
        if (_entities == null) return;
        if (_klonNach >= 0f)
        {
            _klonUhr += (float)delta;
            if (_klonUhr >= _klonNach)
            {
                GD.Print(_entities.KlonCheck());
                _klonNach = -1f;
                GetTree().Quit();
            }
        }
        if (_kuAn) KiUhrenLadenCheckTakt();
    }

    private int KuLogZahl(int p) => MapEntityLayer.GefechtGruppenLog.Count(x => x.P == p);

    private void KiUhrenLadenCheckTakt()
    {
        float uhr = _entities.SpielUhrFuerProbe;
        if (_kuPhase == 0)
        {
            if (!UI.SkirmishSetup.Active || UI.SkirmishSetup.CampaignMission > 0 || !Core.Pruefschutz.Aktiv)
            {
                GD.Print("ki-uhren-laden-check: KEIN URTEIL — nur im Gefecht und unter dem Pruefschutz (kopflos)");
                _kuPhase = 5; GetTree().Quit(); return;
            }
            if (MapEntityLayer.GefechtGruppenLog.Count > 0)
            {
                _kuP = MapEntityLayer.GefechtGruppenLog[0].P;
                _kuGruppeUhr = uhr;
                _kuPhase = 1;
                GD.Print($"ki-uhren-laden-check: erste Gruppe von P{_kuP} bei {uhr:0.00} s, Takt {_entities.TaktNrUhrenProbe}");
            }
            else if (uhr > 120f) { GD.Print("ki-uhren-laden-check: KEIN URTEIL — in 120 s keine Gefechtsgruppe"); _kuPhase = 5; GetTree().Quit(); }
            return;
        }
        if (_kuPhase == 1 && uhr >= _kuGruppeUhr + 3f)
        {
            _kuSpeicherUhr = uhr;
            _kuTimerVorher = _entities.KiAttackTimerFuerProbe(_kuP);
            _kuFnRestVorher = _entities.FeindNahtRestFuerProbe;
            _kuTaktVorher = _entities.TaktNrUhrenProbe;
            _kuDenk = _entities.KiDenktaktFuerProbe;
            _kuLogNach = KuLogZahl(_kuP);
            _kuFnVorher = _entities.FeindNahtProtokoll.Count;
            _kuDatei = $"ki_uhren_laden_check_{Time.GetUnixTimeFromSystem():0}";
            if (FileAccess.FileExists(Core.SaveGame.PathOf(_kuDatei)))
            { GD.Print($"ki-uhren-laden-check: KEIN URTEIL — {Core.SaveGame.PathOf(_kuDatei)} gibt es schon"); _kuPhase = 5; GetTree().Quit(); return; }
            string json = _entities.SaveStateJson(MapNames[_mapIndex], "ki-uhren-laden-check");
            if (!Core.SaveGame.Write(_kuDatei, json, out string err))
            { GD.Print($"ki-uhren-laden-check: Speichern fehlgeschlagen — {err}"); _kuPhase = 5; GetTree().Quit(); return; }
            GD.Print($"ki-uhren-laden-check: gespeichert bei {uhr:0.000} s, Takt {_kuTaktVorher}, P{_kuP} AttackTimer {_kuTimerVorher:0.000} s, " +
                     $"Feind-naht-Sperre {_kuFnRestVorher:0.000} s, Denktakt {_kuDenk:0.0} s -> {Core.SaveGame.PathOf(_kuDatei)}");
            _kuPhase = 2;
            return;
        }
        if (_kuPhase == 2)
        {
            if (uhr <= _kuSpeicherUhr + 5f) _kuFnBezug = _entities.FeindNahtProtokoll.Count - _kuFnVorher;
            bool neu = KuLogZahl(_kuP) > _kuLogNach;
            if (neu || uhr > _kuSpeicherUhr + 90f)
            {
                _kuBezugAbstand = neu ? uhr - _kuSpeicherUhr : -1f;
                GD.Print($"ki-uhren-laden-check: BEZUG ohne Laden — naechste Gruppe P{_kuP} " +
                         (neu ? $"{_kuBezugAbstand:0.00} s nach dem Speichern" : "in 90 s keine") +
                         $", Feind naht in den ersten 5 s: {_kuFnBezug}");
                _kuPhase = 3;
                UI.SkirmishSetup.PendingSave = _kuDatei;
                Core.LeaveToMenu.Tidy(this);
                GetTree().ChangeSceneToFile(UI.SkirmishSetup.GameScene);
            }
            return;
        }
        if (_kuPhase == 4)
        {
            if (_kuLadeUhr < 0f) return;
            if (uhr <= _kuLadeUhr + 5f) _kuFnLaden = _entities.FeindNahtProtokoll.Count;
            bool neu = KuLogZahl(_kuP) > _kuLogLaden;
            if (!neu && uhr <= _kuLadeUhr + 90f) return;
            _kuPhase = 5;
            float abstand = neu ? uhr - _kuLadeUhr : -1f;
            bool timer = !float.IsNaN(_kuTimerNachher) && Mathf.Abs(_kuTimerNachher - _kuTimerVorher) < 1e-4f;
            bool takt = _kuTaktNachher == _kuTaktVorher;
            bool fnRest = Mathf.Abs(_kuFnRestNachher - _kuFnRestVorher) < 0.01f;
            bool zeit = _kuBezugAbstand >= 0f && abstand >= 0f && Mathf.Abs(abstand - _kuBezugAbstand) <= _kuDenk + 0.05f;
            bool fn = _kuFnLaden == _kuFnBezug;
            bool gut = timer && takt && fnRest && zeit && fn;
            GD.Print("ki-uhren-laden-check" + (MapEntityLayer.KiUhrenSpielstandAlt ? " — NULLMODELL --ki-uhren-spielstand-alt" : "") +
                     $"\n   Spieler P{_kuP}, gespeichert {_kuSpeicherUhr:0.000} s, geladen {_kuLadeUhr:0.000} s, Uhren gelesen fuer {MapEntityLayer.KiUhrenGeladen} KI" +
                     $"\n   {(timer ? "  gleich " : "⚠ ANDERS ")} AttackTimer {_kuTimerVorher:0.0000} -> {_kuTimerNachher:0.0000}" +
                     $"\n   {(takt ? "  gleich " : "⚠ ANDERS ")} Takt {_kuTaktVorher} -> {_kuTaktNachher}" +
                     $"\n   {(fnRest ? "  gleich " : "⚠ ANDERS ")} Feind-naht-Sperre {_kuFnRestVorher:0.000} -> {_kuFnRestNachher:0.000} s" +
                     $"\n   {(zeit ? "  gleich " : "⚠ ANDERS ")} naechste Gruppe nach dem Speichern: ohne Laden {Abst(_kuBezugAbstand)}, " +
                     $"mit Laden {Abst(abstand)} (erlaubt +- {_kuDenk:0.0} s = 1 Denktakt)" +
                     $"\n   {(fn ? "  gleich " : "⚠ ANDERS ")} Feind naht in den ersten 5 s: ohne Laden {_kuFnBezug}, mit Laden {_kuFnLaden}" +
                     $"\n   -> {(gut ? "BESTANDEN" : "DURCHGEFALLEN")}");
            string pfad = Core.SaveGame.PathOf(_kuDatei);
            if (_kuDatei.StartsWith("ki_uhren_laden_check_") && FileAccess.FileExists(pfad))
            {
                var e = DirAccess.RemoveAbsolute(pfad);
                GD.Print($"ki-uhren-laden-check: eigene Datei {pfad} geloescht ({e})");
            }
            GetTree().Quit();
        }
    }

    private static string Abst(float s) => s < 0f ? "keine in 90 s" : $"{s:0.00} s";

    /// <summary>Aus <c>_Ready</c>, nach dem ganzen Startzweig.</summary>
    private void SkkNachLaden()
    {
        if (_entities == null || !_kuAn || _kuPhase != 3) return;
        _kuPhase = 4;
        _kuLadeUhr = _entities.SpielUhrFuerProbe;
        _kuTimerNachher = _entities.KiAttackTimerFuerProbe(_kuP);
        _kuFnRestNachher = _entities.FeindNahtRestFuerProbe;
        _kuTaktNachher = _entities.TaktNrUhrenProbe;
        _kuLogLaden = KuLogZahl(_kuP);
        _kuFnLaden = 0;
    }
}
