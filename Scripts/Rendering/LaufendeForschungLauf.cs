namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// ⭐ 29.10.2026, bug-666 — <b><c>--laufende-forschung-check</c></b>: geht eine LAUFENDE
/// Forschung (sec96/97) durch Speichern und Laden, und WIRKT sie danach gleich?
///
/// <para>Ablauf (Vorgabe <c>-- --campaign=3 --no-briefing</c>,
/// <c>--laufende-forschung-check-nach=S</c>, Vorgabe 8 s): eine Forschung in einer Basis auf
/// dem echten Weg <c>ForschungKaufen</c> starten (Aufwertung, Preis ≥ 300 Takte), 60 Takte
/// laufen lassen, unter EIGENEM neuen Namen <c>laufende_forschung_check_&lt;Zeit&gt;</c> im
/// Pruefschutz-Ordner speichern. Dann im SELBEN Spiel weiterlaufen lassen bis zum Abschluss
/// (= »ohne Speichern«, Takte T0), laden ueber den Szenenwechsel, den Laufsatz Feld fuer Feld
/// vergleichen und wieder bis zum Abschluss zaehlen (T1). Soll: Laufsatz gleich,
/// T0 = T1 = Restdauer beim Speichern, und die Stufe des Bauteils steigt in beiden um 1.
/// Die eigene Datei wird danach EINZELN mit vollem Namen geloescht.</para>
///
/// <para>Nullmodell <c>--laufende-forschung-alt</c>: nach dem Laden ist der Laufsatz weg,
/// die Forschung schliesst nie ab → DURCHGEFALLEN. ⚠ Laeuft NUR unter dem Pruefschutz.</para>
/// </summary>
public partial class MapViewer
{
    private static bool _lfAn;
    private static float _lfNach = 8f;
    /// <summary>0 wartet, 1 gespeichert/laedt, 2 verglichen (Zaehlung im naechsten Bild), 3 fertig.</summary>
    private static int _lfPhase;
    private static int _lfPlatz = -1, _lfRest, _lfT0, _lfBauteil = -1, _lfSpieler, _lfStufeVor, _lfStufeT0, _lfRestBeiLaden;
    private static string _lfText = "", _lfVorher = "", _lfAbdruck = "", _lfDatei = "";
    private static bool _lfFelderGleich;
    private float _lfUhr;

    private bool LfSchalter(string a)
    {
        switch (a)
        {
            case "--laufende-forschung-alt": MapEntityLayer.LaufendeForschungAlt = true; return true;   // bug-666
            case "--laufende-forschung-check": _lfAn = true; UI.HelpWindow.Suppressed = true; return true;
        }
        if (a.StartsWith("--laufende-forschung-check-nach=") &&
            float.TryParse(a["--laufende-forschung-check-nach=".Length..], System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float s))
        { _lfNach = s; return true; }
        return false;
    }

    private int LfStufe() => _lfBauteil >= 0 ? _entities.BauteilFuer(_lfSpieler, _lfBauteil)![1] : -1;

    private void LfEnde(string grund)
    {
        GD.Print("laufende-forschung-check: KEIN URTEIL — " + grund);
        _lfPhase = 3; GetTree().Quit();
    }

    private void LaufendeForschungCheckTakt(double delta)
    {
        if (!_lfAn || _entities == null) return;
        _lfUhr += (float)delta;
        if (_lfPhase == 0 && _lfUhr >= _lfNach)
        {
            if (!Core.Pruefschutz.Aktiv) { LfEnde("nur unter dem Pruefschutz (kopflos), der Lauf schriebe sonst in user://saves"); return; }
            _entities.SetProcess(false);
            var sb = new StringBuilder($"laufende-forschung-check K{UI.SkirmishSetup.CampaignMission}" +
                                       (MapEntityLayer.LaufendeForschungAlt ? " — NULLMODELL --laufende-forschung-alt" : ""));
            _lfSpieler = _entities.ViewPlayer is >= 0 and <= 7 ? _entities.ViewPlayer : 0;
            int idx = _entities.ForschungProbeBasis(_lfSpieler, out string eingriff);
            if (idx < 0) { LfEnde("keine Basis auf der Karte"); return; }
            sb.Append("\n   gelegt: " + eingriff);
            _lfPlatz = _entities.ForschungProbeKaufen(idx, 300, sb);
            if (_lfPlatz < 0) { GD.Print(sb); LfEnde("keine Forschung gestartet"); return; }
            _lfBauteil = _entities.LaufsatzBauteil(_lfPlatz);
            for (int t = 0; t < 60; t++) _entities.SimTickFuerProbe();          // »ein Stueck laufen lassen«
            if (!_entities.LaufsatzBelegt(_lfPlatz)) { GD.Print(sb); LfEnde("nach 60 Takten schon fertig"); return; }
            _lfRest = _entities.LaufsatzRest(_lfPlatz);
            _lfStufeVor = LfStufe();
            _lfVorher = _entities.LaufsatzZeile(_lfPlatz);
            _lfAbdruck = _entities.LaufendeForschungFingerabdruck();
            sb.Append($"\n   nach 60 Takten: {_lfVorher}; Stufe vorher {_lfStufeVor}");

            _lfDatei = $"laufende_forschung_check_{Time.GetUnixTimeFromSystem():0}";
            if (FileAccess.FileExists(Core.SaveGame.PathOf(_lfDatei)))
            { LfEnde($"{Core.SaveGame.PathOf(_lfDatei)} gibt es schon, nichts ueberschrieben"); return; }
            string json = _entities.SaveStateJson(MapNames[_mapIndex], "laufende-forschung-check");
            if (!Core.SaveGame.Write(_lfDatei, json, out string err)) { LfEnde("Speichern fehlgeschlagen — " + err); return; }
            sb.Append($"\n   gespeichert {Core.SaveGame.PathOf(_lfDatei)} ({json.Length} Zeichen)");

            // OHNE Speichern: dasselbe Spiel weiter bis zum Abschluss.
            _lfT0 = _entities.ForschungTakteBisAbschluss(_lfPlatz, _lfRest + 200);
            _lfStufeT0 = LfStufe();
            sb.Append($"\n   ohne Laden: fertig nach {_lfT0} Takten (Rest war {_lfRest}), Stufe {_lfStufeVor} -> {_lfStufeT0}");
            _lfText = sb.ToString();

            _lfPhase = 1;
            UI.SkirmishSetup.PendingSave = _lfDatei;
            Core.LeaveToMenu.Tidy(this);
            GetTree().ChangeSceneToFile(UI.SkirmishSetup.GameScene);
            return;
        }
        if (_lfPhase == 2)
        {
            _lfPhase = 3;
            _entities.SetProcess(false);
            // Was zwischen dem Laden und diesem Bild schon gelaufen ist, zaehlt mit.
            int restJetzt = _entities.LaufsatzRest(_lfPlatz);
            int vorab = restJetzt >= 0 && _lfRestBeiLaden >= 0 ? _lfRestBeiLaden - restJetzt : 0;
            int t = _entities.ForschungTakteBisAbschluss(_lfPlatz, _lfRest + 200);
            int t1 = t > 0 ? t + vorab : -1;
            int stufeT1 = LfStufe();
            bool zeit = _lfT0 == _lfRest && t1 == _lfT0;
            bool wirkt = _lfBauteil < 0 || (_lfStufeT0 == _lfStufeVor + 1 && stufeT1 == _lfStufeVor + 1);
            var sb = new StringBuilder(_lfText);
            sb.Append($"\n   {(t1 > 0 && t1 == _lfT0 ? "  gleich " : "⚠ ANDERS ")} nach dem Laden: " +
                      (t1 > 0 ? $"fertig nach {t1} Takten ({vorab} davon vor der Zaehlung)" : $"NICHT fertig in {_lfRest + 200} Takten") +
                      $" — ohne Laden {_lfT0}, Rest beim Speichern {_lfRest}");
            sb.Append($"\n   {(wirkt ? "  gleich " : "⚠ ANDERS ")} Stufe Bauteil {_lfBauteil} (Spieler {_lfSpieler}): " +
                      $"vorher {_lfStufeVor}, ohne Laden {_lfStufeT0}, nach dem Laden {stufeT1}");
            sb.Append($"\n   {(_lfFelderGleich && zeit && wirkt ? "BESTANDEN" : "DURCHGEFALLEN")}");
            GD.Print(sb.ToString());
            string pfad = Core.SaveGame.PathOf(_lfDatei);
            if (_lfDatei.StartsWith("laufende_forschung_check_") && FileAccess.FileExists(pfad))
            {
                var e = DirAccess.RemoveAbsolute(pfad);
                GD.Print($"laufende-forschung-check: eigene Datei {pfad} geloescht ({e})");
            }
            GetTree().Quit();
        }
    }

    /// <summary>Aus <c>P2NachLaden</c> (aus <c>_Ready</c>, nach dem ganzen Startzweig).</summary>
    private void LaufendeForschungCheckNachLaden()
    {
        if (!_lfAn || _lfPhase != 1 || _entities == null) return;
        _lfPhase = 2;
        string nachher = _entities.LaufsatzZeile(_lfPlatz);
        string abdruck = _entities.LaufendeForschungFingerabdruck();
        _lfRestBeiLaden = _entities.LaufsatzRest(_lfPlatz);
        bool g1 = nachher == _lfVorher, g2 = abdruck == _lfAbdruck;
        _lfFelderGleich = g1 && g2;
        var sb = new StringBuilder(_lfText);
        sb.Append($"\n   geladen: {MapEntityLayer.LaufendeForschungGeladen} Laufsaetze aus dem Stand");
        sb.Append($"\n   {(g1 ? "  gleich " : "⚠ ANDERS ")} Laufsatz {_lfVorher}{(g1 ? "" : $"\n              ->  {nachher}")}");
        sb.Append($"\n   {(g2 ? "  gleich " : "⚠ ANDERS ")} {_lfAbdruck}{(g2 ? "" : $"\n              ->  {abdruck}")}");
        _lfText = sb.ToString();
    }
}
