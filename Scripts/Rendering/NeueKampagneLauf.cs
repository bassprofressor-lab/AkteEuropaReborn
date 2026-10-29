namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// ⭐ 29.10.2026, bug-667 — <b><c>--forschung-anfangsstand-check</c></b> (Vorgabe
/// <c>-- --campaign=3 --no-briefing --no-movie</c>; Bau: <c>berichte/bau-neue-kampagne-0-6-12.md</c>).
/// Laeuft ueber vier Szenenwechsel, darum statischer Zustand wie
/// <c>--forschung-uebertrag-check</c> (PaketP2Lauf.cs):
/// <list type="number">
/// <item>Mission N: die KOPIE <c>user://pruefstand/campaign.cfg</c> auf einen frischen Stand
/// mit <c>completed = N-1</c> setzen und Mission N neu starten — damit wird N »im
/// Kampagnenfluss erreicht« (Riegel <c>mission == Completed+1</c>).</item>
/// <item>Mission N: aufwerten (163 einmal, 1 zweimal), den Sieg nachbilden
/// (<c>ForschungMissionsende</c>, <c>Finished</c>, <c>ForschungUebertragAblegen</c> —
/// dieselben drei Rufe wie <c>MapViewer.ShowEnd</c>), N+1 ueber das Menue (Weg von »Weiter«).</item>
/// <item>Mission N+1: Stand messen (= Ende von N, Soll A), weiter aufwerten (163 zweimal,
/// 1 einmal), Sieg nachbilden (Stand B), dann N+1 ERNEUT ueber das Menue — dieselbe Tuer
/// <c>MainMenu.StartMission</c>, durch die die Kachel der Missionsuebersicht geht.</item>
/// <item>Soll: Stand A (Anfang von N+1). Nullmodell <c>--forschung-anfangsstand-alt</c>:
/// Stand B (Ende von N+1) — dann DURCHGEFALLEN.</item>
/// </list>
/// ⚠ Nur unter dem Pruefschutz (kopflos); sonst KEIN URTEIL, nichts geschrieben.
/// </summary>
public partial class MapViewer
{
    private static bool _faAn;
    private static int _faPhase, _faMission;
    private static int _faA163, _faA1, _faB163, _faB1;
    private static long _faAWerte, _faBWerte;
    private static string _faZeile = "";

    /// <summary>Die Schalter von bug-667; true = verbraucht. Aus <see cref="P2Schalter"/>.</summary>
    private bool FaSchalter(string a)
    {
        switch (a)
        {
            case "--forschung-anfangsstand-alt": Campaign.CampaignManager.ForschungAnfangsstandAlt = true; return true;
            case "--forschung-anfangsstand-check": _faAn = true; UI.HelpWindow.Suppressed = true; return true;
        }
        return false;
    }

    private void FaEnde(string text)
    {
        GD.Print(text);
        _faPhase = 9;
        GetTree().Quit();
    }

    /// <summary>Phase 0 im ersten Bild von Mission N.</summary>
    private void ForschungAnfangsstandCheckTakt()
    {
        if (_faPhase != 0) return;
        _faPhase = 1;
        _faMission = UI.SkirmishSetup.CampaignMission;
        var sb = new StringBuilder($"forschung-anfangsstand-check K{_faMission}"
            + (Campaign.CampaignManager.ForschungAnfangsstandAlt ? " — NULLMODELL --forschung-anfangsstand-alt" : ""));
        if (_faMission < 2 || _faMission >= 33 || !Core.Pruefschutz.Aktiv || MapEntityLayer.ForschungUebertragAlt)
        {
            FaEnde(sb + "\n   KEIN URTEIL: nur in einer Kampagnenmission 2..32, nur unter dem Pruefschutz (kopflos), ohne --forschung-uebertrag-alt");
            return;
        }
        // die KOPIE frisch machen: Reset (= Weg von »Neue Kampagne«), dann N-1 geschafft
        Campaign.CampaignManager.Reset();
        Campaign.CampaignManager.Completed = _faMission - 1;
        sb.Append($"\n   Vorbereitung: {Campaign.CampaignManager.SavePath} zurueckgesetzt, completed = {_faMission - 1}; Mission {_faMission} neu starten (im Fluss erreicht)");
        _faZeile = sb.ToString();
        CallDeferred(nameof(FaNeuStart));
    }

    private void FaNeuStart()
    {
        GetTree().Paused = false;
        Core.LeaveToMenu.Tidy(this);
        GetTree().ChangeSceneToFile(UI.SkirmishSetup.GameScene);
    }

    private void FaUeberMenue(int mission)
    {
        UI.SkirmishSetup.PendingMission = mission;
        GetTree().Paused = false;
        Core.LeaveToMenu.Tidy(this);
        GetTree().ChangeSceneToFile(UI.SkirmishSetup.MenuScene);
    }

    private void FaSieg(int mission, StringBuilder sb)
    {
        _entities.ForschungMissionsende();
        Campaign.CampaignManager.Finished(mission);
        _entities.ForschungUebertragAblegen(mission);
        sb.Append($"\n   Sieg M{mission} nachgebildet: completed = {Campaign.CampaignManager.Completed}, " +
                  $"[bauteile] Mission {Campaign.CampaignManager.Bauteile.Mission}");
    }

    /// <summary>⚠ Gemessen am 29.10.: ein ZWEITER kopfloser Lauf (anderer Agent) kopiert beim
    /// Start die echte campaign.cfg ueber dieselbe Kopie <c>user://pruefstand/campaign.cfg</c> —
    /// im ersten Nullmodelllauf stand nach dem Sieg in M3 ploetzlich <c>completed = 33</c>.
    /// Dann gibt es kein Urteil statt eines falschen.</summary>
    private bool FaKopieFremd(int mission, StringBuilder sb)
    {
        if (Campaign.CampaignManager.Completed == mission && Campaign.CampaignManager.Bauteile.Mission == mission) return false;
        FaEnde(sb + $"\n   KEIN URTEIL: die Kopie {Campaign.CampaignManager.SavePath} wurde von einem anderen Lauf " +
               $"ueberschrieben (completed {Campaign.CampaignManager.Completed}, Soll {mission}) — allein wiederholen");
        return true;
    }

    private (int S163, int S1, long Werte) FaStand()
        => (_entities.BauteilFuer(0, 163)![1], _entities.BauteilFuer(0, 1)![1], _entities.Block0Werte());

    /// <summary>Aus <see cref="P2NachLaden"/>.</summary>
    private void ForschungAnfangsstandCheckNachLaden()
    {
        if (_entities == null) return;
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new StringBuilder(_faZeile);
        if (_faPhase == 1 && m == _faMission)
        {
            sb.Append($"\n   M{m} Start: {MapEntityLayer.ForschungUebertragZeile}");
            int a1 = _entities.Aufwerten(0, 163), a2 = _entities.Aufwerten(0, 1), a3 = _entities.Aufwerten(0, 1);
            var (s163, s1, w) = FaStand();
            _faA163 = s163; _faA1 = s1; _faAWerte = w;
            sb.Append($"\n   in M{m} aufgewertet: 163 (Wurf {a1}) -> Stufe {s163}; 1 (Wuerfe {a2}/{a3}) -> Stufe {s1}; Pruefzahl Block 0 {w}  (= Stand A)");
            FaSieg(m, sb);
            if (FaKopieFremd(m, sb)) return;
            sb.Append($"\n   Mission {m + 1} ueber das Menue (Weg von »Weiter«)");
            _faZeile = sb.ToString();
            _faPhase = 2;
            CallDeferred(nameof(FaWeiter));
            return;
        }
        if (_faPhase == 2 && m == _faMission + 1)
        {
            sb.Append($"\n   M{m} Start: {MapEntityLayer.ForschungUebertragZeile}");
            var (s163, s1, w) = FaStand();
            bool gleich = s163 == _faA163 && s1 == _faA1 && w == _faAWerte;
            string anf = Campaign.CampaignManager.ForschungAnfangsstandOf(m);
            sb.Append($"\n   {(gleich ? "  ok " : "⚠ FALSCH ")} M{m} beginnt mit Stand A: 163 Stufe {s163}, 1 Stufe {s1}, Pruefzahl {w}");
            sb.Append($"\n   [{Campaign.CampaignManager.ForschungAnfangAbschnitt}] {m}: {(anf.Length > 0 ? anf.Length + " Zeichen" : "LEER")}" +
                      $"; [{Campaign.CampaignManager.ForschungAnfangAbschnitt}] {_faMission}: {Campaign.CampaignManager.ForschungAnfangsstandOf(_faMission).Length} Zeichen");
            if (!gleich) { FaEnde(sb + "\n   DURCHGEFALLEN (der Weiterweg traegt Stand A nicht — bug-665 pruefen)"); return; }
            int b1 = _entities.Aufwerten(0, 163), b2 = _entities.Aufwerten(0, 163), b3 = _entities.Aufwerten(0, 1);
            (_faB163, _faB1, _faBWerte) = FaStand();
            sb.Append($"\n   in M{m} weiter aufgewertet: 163 (Wuerfe {b1}/{b2}) -> Stufe {_faB163}; 1 (Wurf {b3}) -> Stufe {_faB1}; Pruefzahl {_faBWerte}  (= Stand B)");
            FaSieg(m, sb);
            if (FaKopieFremd(m, sb)) return;
            sb.Append($"\n   Mission {m} ERNEUT ueber das Menue (dieselbe Tuer wie die Kachel der Missionsuebersicht)");
            _faZeile = sb.ToString();
            _faPhase = 3;
            CallDeferred(nameof(FaNochmal));
            return;
        }
        if (_faPhase == 3 && m == _faMission + 1)
        {
            sb.Append($"\n   M{m} erneut, Start: {MapEntityLayer.ForschungUebertragZeile}");
            var (s163, s1, w) = FaStand();
            bool istA = s163 == _faA163 && s1 == _faA1 && w == _faAWerte;
            bool istB = s163 == _faB163 && s1 == _faB1 && w == _faBWerte;
            bool unterscheidbar = _faAWerte != _faBWerte;
            if (Campaign.CampaignManager.Completed != m)
            { FaEnde(sb + $"\n   KEIN URTEIL: Kopie von einem anderen Lauf ueberschrieben (completed {Campaign.CampaignManager.Completed}, Soll {m})"); return; }
            sb.Append($"\n   gemessen: 163 Stufe {s163}, 1 Stufe {s1}, Pruefzahl {w} -> " +
                      (istA ? "Stand A (Anfang von M" + m + ")" : istB ? "Stand B (Ende von M" + m + ")" : "WEDER A NOCH B"));
            sb.Append($"\n   {(istA ? "  ok " : "⚠ FALSCH ")} Soll Stand A: 163 Stufe {_faA163}, 1 Stufe {_faA1}, Pruefzahl {_faAWerte}");
            sb.Append($"\n   {(unterscheidbar ? "  ok " : "⚠ ")} A und B unterscheidbar (Pruefzahl {_faAWerte} / {_faBWerte})");
            FaEnde(sb + "\n   " + (!unterscheidbar ? "KEIN URTEIL (A = B)" : istA ? "BESTANDEN" : "DURCHGEFALLEN"));
        }
    }

    private void FaWeiter() => FaUeberMenue(_faMission + 1);
    private void FaNochmal() => FaUeberMenue(_faMission + 1);
}
