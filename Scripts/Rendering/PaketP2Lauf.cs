namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 29.10.2026 — <b>Paket P2 der Testerrunde 0.6.12</b> (KayelGee R8 K8 = bug-650,
/// Reaper R6 = bug-665; <c>berichte/testerrunde-0-6-12.md</c>, Bau:
/// <c>berichte/bau-p2-0-6-12.md</c>): die Schalter und die zwei Pruefstaende, die beide
/// ueber einen SZENENWECHSEL laufen (darum statischer Zustand, wie
/// <c>--spielstand-check</c>).
///
/// <para><b><c>--befehle-laden-check</c></b> (Vorgabe <c>-- --campaign=5 --no-briefing</c>,
/// <c>--befehle-laden-check-nach=S</c>, Vorgabe 8 s): Befehle hinlegen
/// (<see cref="MapEntityLayer.BefehlsProbeLegen"/>), speichern unter einem EIGENEN neuen
/// Namen <c>befehle_laden_check_&lt;Zeit&gt;</c> im Pruefschutz-Ordner, laden ueber den
/// Szenenwechsel, feldweise vergleichen, dann 250 Takte (5 s) Wirkungsprobe. Die eigene
/// Datei wird danach EINZELN mit vollem Namen geloescht. Nullmodell
/// <c>--laden-befehle-alt</c>.</para>
///
/// <para><b><c>--forschung-uebertrag-check</c></b> (<c>-- --campaign=3 --no-briefing</c>):
/// in Mission N aufwerten (163 einmal, 1 zweimal), den Sieg nachbilden
/// (<c>ForschungUebertragAblegen</c>), Mission N+1 ueber das Menue starten (der Weg von
/// »Weiter«), dort die Stufen, den Tank, die KI-Kopie und die Zeile im Forschungsreiter
/// messen; dann in N+1 noch einmal aufwerten und die Mission NEU starten — Soll: wieder der
/// Stand vom Ende von N (0x4D0290). ⚠ Laeuft NUR unter dem Pruefschutz (kopflos): er
/// schreibt <c>[bauteile]</c> in die KOPIE <c>user://pruefstand/campaign.cfg</c>, nie in die
/// des Spielers. Nullmodelle <c>--forschung-uebertrag-alt</c>, <c>--ki-erbt-forschung-alt</c>.</para>
/// </summary>
public partial class MapViewer
{
    private static bool _blAn, _fuAn;
    private static float _blNach = 8f;

    /// <summary>Die Schalter von P2; true = verbraucht.</summary>
    private bool P2Schalter(string a)
    {
        switch (a)
        {
            case "--laden-befehle-alt": MapEntityLayer.LadenBefehleAlt = true; return true;            // bug-650
            case "--forschung-uebertrag-alt": MapEntityLayer.ForschungUebertragAlt = true; return true; // bug-665
            case "--ki-erbt-forschung-alt": MapEntityLayer.KiErbtForschungAlt = true; return true;     // bug-665
            case "--spielstand-bauteile-alt": MapEntityLayer.SpielstandBauteileAlt = true; return true; // bug-665
            case "--spielstand-probe-alt": MapEntityLayer.SpielstandProbeAlt = true; return true;      // Pruefstand
            case "--befehle-laden-check": _blAn = true; UI.HelpWindow.Suppressed = true; return true;
            case "--forschung-uebertrag-check": _fuAn = true; UI.HelpWindow.Suppressed = true; return true;
        }
        if (a.StartsWith("--altstand-laden-check=")) { _alName = a["--altstand-laden-check=".Length..]; return true; }
        if (a.StartsWith("--befehle-laden-check-nach=") &&
            float.TryParse(a["--befehle-laden-check-nach=".Length..], System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float s))
        { _blNach = s; return true; }
        return false;
    }

    // ---- --befehle-laden-check --------------------------------------------------

    /// <summary>0 wartet, 1 gespeichert/laedt, 2 verglichen (Wirkung im naechsten Bild), 3 fertig.</summary>
    private static int _blPhase;
    private static string _blGelegt = "", _blAbdruck = "", _blDatei = "";
    private static readonly List<MapEntityLayer.BefehlsProbe> _blProben = new();
    private static int _blFeldGleich, _blFeldAnders;
    private static string _blFeldText = "";
    private float _blUhr;

    // ---- --altstand-laden-check=<Name> --------------------------------------------
    // Ein VORHANDENER Spielstand (Format 1..3) wird ueber den Weg des Ladefensters
    // (LoadGameScreen.Starten) geladen — NUR gelesen, nichts geschrieben. Zeigt, dass
    // aeltere Formate unter Format 4 weiter laden, und was RoutenNachLaden tut.
    private static string _alName = "";
    private static int _alPhase;

    private void P2Takt(double delta)
    {
        if (_entities == null) return;
        if (_alName.Length > 0 && _alPhase == 0)
        {
            _alPhase = 1;
            if (!UI.LoadGameScreen.Starten(this, _alName))
            { GD.Print($"altstand-laden-check: {_alName} NICHT LESBAR"); GetTree().Quit(); }
            return;
        }
        if (_alName.Length > 0 && _alPhase == 2)
        {
            _alPhase = 3;
            for (int t = 0; t < 100; t++) _entities.SimTickFuerProbe();
            GD.Print($"altstand-laden-check: nach 100 Takten ohne Ausnahme — {_entities.BefehleFingerabdruck()}");
            GetTree().Quit();
            return;
        }
        if (_blAn) BefehleLadenCheckTakt(delta);
        if (_fuAn) ForschungUebertragCheckTakt();
    }

    private void BefehleLadenCheckTakt(double delta)
    {
        _blUhr += (float)delta;
        if (_blPhase == 0 && _blUhr >= _blNach)
        {
            _blProben.Clear();
            _blGelegt = _entities.BefehlsProbeLegen(_blProben);
            _blAbdruck = _entities.BefehleFingerabdruck() + " | " + _entities.LageFingerabdruck();
            if (!Core.Pruefschutz.Aktiv)
            {
                GD.Print("befehle-laden-check: KEIN URTEIL — nur unter dem Pruefschutz (kopflos), der Lauf schriebe sonst in user://saves");
                _blPhase = 3; GetTree().Quit(); return;
            }
            _blDatei = $"befehle_laden_check_{Time.GetUnixTimeFromSystem():0}";
            if (FileAccess.FileExists(Core.SaveGame.PathOf(_blDatei)))
            {
                GD.Print($"befehle-laden-check: KEIN URTEIL — {Core.SaveGame.PathOf(_blDatei)} gibt es schon, nichts ueberschrieben");
                _blPhase = 3; GetTree().Quit(); return;
            }
            string json = _entities.SaveStateJson(MapNames[_mapIndex], "befehle-laden-check");
            if (!Core.SaveGame.Write(_blDatei, json, out string err))
            {
                GD.Print($"befehle-laden-check: Speichern fehlgeschlagen — {err}");
                _blPhase = 3; GetTree().Quit(); return;
            }
            GD.Print($"befehle-laden-check: gespeichert {Core.SaveGame.PathOf(_blDatei)} ({json.Length} Zeichen)");
            _blPhase = 1;
            UI.SkirmishSetup.PendingSave = _blDatei;
            Core.LeaveToMenu.Tidy(this);
            GetTree().ChangeSceneToFile(UI.SkirmishSetup.GameScene);
            return;
        }
        if (_blPhase == 2)
        {
            _blPhase = 3;
            _entities.SetProcess(false);
            _entities.BefehlsProbeSonderLoeschen(_blProben);
            var sb = new StringBuilder();
            var (ok, gem) = _entities.BefehlsWirkung(_blProben, 250, sb);
            bool wirkt = gem > 0 && ok == gem;
            bool felder = _blFeldAnders == 0 && _blFeldGleich > 0;
            GD.Print($"befehle-laden-check K{UI.SkirmishSetup.CampaignMission}" +
                     (MapEntityLayer.LadenBefehleAlt ? " — NULLMODELL --laden-befehle-alt" : "") +
                     $"\n   gelegt: {_blGelegt}" +
                     $"\n   Wege woertlich geladen {MapEntityLayer.BefehleWegeGeladen}, verworfen {MapEntityLayer.BefehleWegeVerworfen}; " +
                     $"Routensaetze nachgelegt {MapEntityLayer.RoutenNachgelegt}" +
                     _blFeldText +
                     $"\n   Feldvergleich: {_blFeldGleich} gleich, {_blFeldAnders} ANDERS" +
                     $"\n   Wirkungsprobe 250 Takte (5 s):{sb}" +
                     $"\n   Wirkung: {ok}/{gem}" +
                     $"\n   {(felder && wirkt ? "BESTANDEN" : "DURCHGEFALLEN")}");
            // Die EIGENE Datei — einzeln, mit vollem Namen.
            string pfad = Core.SaveGame.PathOf(_blDatei);
            if (_blDatei.StartsWith("befehle_laden_check_") && FileAccess.FileExists(pfad))
            {
                var e = DirAccess.RemoveAbsolute(pfad);
                GD.Print($"befehle-laden-check: eigene Datei {pfad} geloescht ({e})");
            }
            GetTree().Quit();
        }
    }

    /// <summary>Aus <c>_Ready</c>, nach dem ganzen Startzweig.</summary>
    private void P2NachLaden()
    {
        if (_entities == null) return;
        if (_blAn && _blPhase == 1)
        {
            var sb = new StringBuilder();
            _blFeldGleich = _blFeldAnders = 0;
            foreach (var p in _blProben)
            {
                int i = _entities.IndexVonOrdinal(p.Ordinal);
                var e = _entities.EinheitAn(i);
                string nachher = e == null ? "— nicht mehr da" : _entities.BefehlsFeldzeile(e);
                bool g = nachher == p.Vorher;
                if (g) _blFeldGleich++; else _blFeldAnders++;
                sb.Append($"\n   {(g ? "  gleich " : "⚠ ANDERS ")} {p.Art}: {p.Vorher}{(g ? "" : $"\n              ->  {nachher}")}");
            }
            string nach = _entities.BefehleFingerabdruck() + " | " + _entities.LageFingerabdruck();
            bool ag = nach == _blAbdruck;
            sb.Append($"\n   {(ag ? "  gleich " : "⚠ ANDERS ")} Abdruck {_blAbdruck}{(ag ? "" : $"\n              ->  {nach}")}");
            if (!ag) _blFeldAnders++; else _blFeldGleich++;
            _blFeldText = sb.ToString();
            _blPhase = 2;
        }
        if (_fuAn) ForschungUebertragCheckNachLaden();
        if (_alName.Length > 0 && _alPhase == 1)
        {
            _alPhase = 2;
            var root = Core.SaveGame.Read(_alName, out _);
            int fmt = root != null && root.TryGetValue("format", out var fv) ? fv.AsInt32() : -1;
            GD.Print($"altstand-laden-check: {_alName} Format {fmt} geladen, Vorlagen {MapEntityLayer.LadenVorlagen}, " +
                     $"Wege woertlich {MapEntityLayer.BefehleWegeGeladen}, Routensaetze nachgelegt {MapEntityLayer.RoutenNachgelegt}, " +
                     $"Bauteile aus dem Stand {_entities.BauteileAusSpielstand}; {_entities.LageFingerabdruck()}; {_entities.BefehleFingerabdruck()}" +
                     (UI.SkirmishSetup.CampaignMission > 0 ? $"; Forschung: {MapEntityLayer.ForschungUebertragZeile}" : ""));
        }
    }

    // ---- --forschung-uebertrag-check ----------------------------------------------

    /// <summary>0 = Mission N laeuft, 1 = Mission N+1 wird gestartet, 2 = N+1 gemessen,
    /// Neustart laeuft, 3 = fertig.</summary>
    private static int _fuPhase;
    private static int _fuMission;
    private static int _fuS163, _fuS1, _fuTank;
    private static long _fuWerte;
    private static string _fuZeile = "";
    private static bool _fuGut = true;

    private static int Wort(byte[] b, int at) => b[at] | (b[at + 1] << 8);

    private void ForschungUebertragCheckTakt()
    {
        if (_fuPhase != 0) return;
        _fuPhase = 1;
        _fuMission = UI.SkirmishSetup.CampaignMission;
        var sb = new StringBuilder($"forschung-uebertrag-check K{_fuMission}"
            + (MapEntityLayer.ForschungUebertragAlt ? " — NULLMODELL --forschung-uebertrag-alt" : "")
            + (MapEntityLayer.KiErbtForschungAlt ? " — NULLMODELL --ki-erbt-forschung-alt" : ""));
        if (_fuMission <= 0 || _fuMission >= 33 || !Core.Pruefschutz.Aktiv)
        {
            GD.Print(sb + "\n   KEIN URTEIL: nur in einer Kampagnenmission 1..32 und nur unter dem Pruefschutz (kopflos)");
            _fuPhase = 3; GetTree().Quit(); return;
        }
        sb.Append($"\n   Mission {_fuMission} Start: {MapEntityLayer.ForschungUebertragZeile}");
        int a1 = _entities.Aufwerten(0, 163), a2 = _entities.Aufwerten(0, 1), a3 = _entities.Aufwerten(0, 1);
        var c163 = _entities.BauteilFuer(0, 163)!; var c1 = _entities.BauteilFuer(0, 1)!;
        _fuS163 = c163[1]; _fuS1 = c1[1]; _fuTank = Wort(c163, 0x1A); _fuWerte = _entities.Block0Werte();
        sb.Append($"\n   aufgewertet: 163 (Wurf {a1}) -> Stufe {_fuS163}, Tank {_fuTank}; 1 (Wuerfe {a2}/{a3}) -> Stufe {_fuS1}; Werte Block 0 Pruefzahl {_fuWerte}");
        _entities.ForschungMissionsende();
        _entities.ForschungUebertragAblegen(_fuMission);       // = der Sieg (0x4CFD80)
        sb.Append($"\n   Sieg nachgebildet: [bauteile] Mission {Campaign.CampaignManager.Bauteile.Mission}, " +
                  $"{Campaign.CampaignManager.Bauteile.Daten.Length} Zeichen in {Campaign.CampaignManager.SavePath}");
        _fuZeile = sb.ToString();
        UI.SkirmishSetup.PendingMission = _fuMission + 1;
        GetTree().Paused = false;
        Core.LeaveToMenu.Tidy(this);
        GetTree().ChangeSceneToFile(UI.SkirmishSetup.MenuScene);
    }

    private void ForschungUebertragCheckNachLaden()
    {
        if (_fuPhase == 1 && UI.SkirmishSetup.CampaignMission == _fuMission + 1)
        {
            var c163 = _entities.BauteilFuer(0, 163)!; var c1 = _entities.BauteilFuer(0, 1)!;
            var k163 = _entities.BauteilFuer(3, 163)!;
            int s163 = c163[1], s1 = c1[1], tank = Wort(c163, 0x1A), ki = k163[1];
            bool erwKi = !MapEntityLayer.KiErbtForschungAlt;
            long werte = _entities.Block0Werte();
            bool ok1 = s163 == _fuS163, ok2 = s1 == _fuS1, ok3 = tank == _fuTank && werte == _fuWerte, ok4 = ki == _fuS163;
            string fenster = _entities.ForschungReiterZeile(163);
            var sb = new StringBuilder(_fuZeile);
            sb.Append($"\n   Mission {_fuMission + 1} Start: {MapEntityLayer.ForschungUebertragZeile}");
            sb.Append($"\n   {(ok1 ? "  ok " : "⚠ FALSCH ")} Stufe 163: {s163} (Soll {_fuS163})");
            sb.Append($"\n   {(ok2 ? "  ok " : "⚠ FALSCH ")} Stufe 1: {s1} (Soll {_fuS1})");
            sb.Append($"\n   {(ok3 ? "  ok " : "⚠ FALSCH ")} Tank 163: {tank} (Soll {_fuTank}); Werte Block 0 (ohne Besitz) Pruefzahl {werte} (Soll {_fuWerte})");
            sb.Append($"\n   {(ok4 ? "  ok " : "⚠ FALSCH ")} KI Spieler 3 Stufe 163: {ki} (Soll {_fuS163}; 0x4B2290)");
            sb.Append($"\n   Besitz +0x00 Spieler 0: 163 = {c163[0]}, 1 = {c1[0]} (set_part nach Fahrplan M{_fuMission + 1})");
            sb.Append($"\n   Forschungsreiter: {fenster}");
            _fuGut = ok1 && ok2 && ok3 && ok4;
            // in N+1 noch einmal aufwerten, dann NEU starten: Soll wieder der Stand vom Ende von N
            int a = _entities.Aufwerten(0, 163);
            sb.Append($"\n   in M{_fuMission + 1} aufgewertet: 163 (Wurf {a}) -> Stufe {_entities.BauteilFuer(0, 163)![1]}; Mission neu starten (0x4D0290)");
            _fuZeile = sb.ToString();
            _fuPhase = 2;
            CallDeferred(nameof(FuNeustart));
            return;
        }
        if (_fuPhase == 2)
        {
            _fuPhase = 3;
            int s = _entities.BauteilFuer(0, 163)![1];
            bool ok = s == _fuS163;
            var sb = new StringBuilder(_fuZeile);
            sb.Append($"\n   {(ok ? "  ok " : "⚠ FALSCH ")} nach Neustart M{UI.SkirmishSetup.CampaignMission}: Stufe 163 {s} (Soll {_fuS163}, Stand vom Ende von M{_fuMission})");
            sb.Append($"\n   {(_fuGut && ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
            GD.Print(sb.ToString());
            GetTree().Quit();
        }
    }

    private void FuNeustart()
    {
        Core.LeaveToMenu.Tidy(this);
        GetTree().ChangeSceneToFile(UI.SkirmishSetup.GameScene);
    }
}

public partial class MapEntityLayer
{
    /// <summary>Die Zeile des Forschungsreiters fuer ein Bauteil, wie der Zeichner sie
    /// bekommt (<see cref="ForschungZeilen"/>, »Name (Stufe)«) — die ANKUNFT im Fenster,
    /// nicht nur die Quelle. ⚠ EINGRIFF: waehlt die erste eigene Basis an.</summary>
    /// <summary>Pruefzahl ueber Stufe und Werte von Block 0 (ohne Besitz +0x00).</summary>
    public long Block0Werte()
    {
        long s = 0;
        for (int r = 0; r < 200; r++)
        {
            var c = BauteilFuer(0, r)!;
            for (int k = 1; k < c.Length; k++) s += c[k] * (long)((k * 7 + r * 13) % 97 + 1);
        }
        return s;
    }

    public string ForschungReiterZeile(int bauteil)
    {
        int basis = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 1 && b.Owner == ViewPlayer) { basis = i; break; }
            if (basis < 0 && b.IsBuilding && !b.IsProp && !b.Dead && b.BType == 1) basis = i;
        }
        if (basis < 0) return "keine Basis — Fenster nicht gemessen";
        var bb = _entities[basis];
        string eingriff = bb.Owner == ViewPlayer ? "" : $"EINGRIFF fremde Basis Platz {bb.Slot} (Spieler {bb.Owner}) fuer die Zeile kurz dem Spieler gegeben; ";
        int merkOwner = bb.Owner, merkSel = _selected;
        bb.Owner = ViewPlayer;
        _selected = basis;
        var zeilen = ForschungZeilen();
        _selected = merkSel; bb.Owner = merkOwner;
        string name = ForschungBauteilName(bauteil);
        foreach (var z in zeilen)
            if (z.Name.StartsWith(name)) return eingriff + $"»{z.Name}« ({zeilen.Count} Zeilen im Angebot)";
        return eingriff + $"{name} nicht im Angebot ({zeilen.Count} Zeilen)";
    }
}
