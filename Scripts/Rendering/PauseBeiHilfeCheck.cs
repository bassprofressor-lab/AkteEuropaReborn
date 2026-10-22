namespace AkteEuropaReborn.Rendering;

using System.Threading.Tasks;
using Godot;

/// <summary>Taktnummer für den Prüfstand der »Pause bei Hilfe«.</summary>
public partial class MapEntityLayer
{
    internal int TaktNrFuerProbe => _taktNr;
}

/// <summary>
/// ⭐ 22.10.2026, Paket N2 (bug-625) — <c>--pause-bei-hilfe-check</c>, Einzelspiel kopflos
/// (z. B. <c>--skirmish=map_NET07</c>). Die Simulation taktet nur der Prüfstand.
/// <list type="number">
/// <item>Option EIN (über <see cref="UI.Settings.PauseOnHelpTest"/>, settings.cfg bleibt
/// unberührt): Hilfefenster öffnen (show_text, <see cref="UI.HelpWindow.ShowOhneRiegel"/>)
/// → genau ein 993 im Ring, Pausenwort 1000, Art 40 offen, der Takt steht.</item>
/// <item>Hilfe schliessen wie der Spieler → kein 993, Art 40 bleibt offen, Takt steht.</item>
/// <item>Bei stehender Pause ein zweites Hilfefenster (show_text2, <see cref="UI.HelpWindow.Show"/>)
/// → 993 schaltet UM: Pausenwort 0, Art 40 zu, der Takt läuft.</item>
/// <item>Dasselbe Fenster noch einmal, solange es offen ist → kein 993 (F 0x442367/0x4424E4).</item>
/// <item>Option AUS: Hilfefenster öffnen → kein 993, keine Pause, kein Art 40.</item>
/// </list>
/// <b>Nullmodell</b> <c>--pause-bei-hilfe-aus</c> (der Weg bis 0.6.11, Baum anhalten): kein
/// 993 → DURCHGEFALLEN.
/// </summary>
public partial class MapViewer
{
    public static bool PauseBeiHilfeCheckAn => Schalter("--pause-bei-hilfe-check");

    private async Task PauseBeiHilfeCheckLauf()
    {
        _entities.SetProcess(false);
        _entities.SetPhysicsProcess(false);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        void Takte(int n) { for (int t = 0; t < n; t++) _entities.SimTickFuerProbe(); }
        int Zahl993() => _entities.AngenommenJeBefehl.TryGetValue(Simulation.Commands.CommandOp.Pause, out int k) ? k : 0;
        bool Art40() => UI.PausenFenster.Offen is { } f && IsInstanceValid(f) && f.Visible
                        && UI.WindowManager.Offen(UI.PausenFenster.Art) != null;
        string Lage() => $"993={Zahl993()} gesendet={UI.HelpWindow.PauseBeiHilfeGesendet} Wort={_entities.NetzPause} " +
                         $"Art40={(Art40() ? "OFFEN" : "zu")} Takt={_entities.TaktNrFuerProbe} Hilfe offen={UI.HelpWindow.OpenCount} " +
                         $"Baum angehalten={GetTree().Paused}";

        // zwei Hilfetexte, die es gibt
        int a = -1, b = -1;
        for (int i = 1; i <= 600 && b < 0; i++)
            if (UI.HelpWindow.TextOf(i) is { Count: > 0 }) { if (a < 0) a = i; else b = i; }
        if (b < 0)
        {
            GD.Print("pause-bei-hilfe-check: DURCHGEFALLEN — keine Hilfetexte (help.json fehlt)");
            GetTree().Quit(1);
            return;
        }
        bool alt = UI.HelpWindow.PauseBeiHilfeAlt;
        if (alt) GD.Print("  ⚠ NULLMODELL --pause-bei-hilfe-aus: der alte Weg (Baum anhalten, kein 993)");
        UI.HelpWindow.VergissGezeigte();
        UI.Settings.PauseOnHelpTest = true;
        var wurzel = GetTree().Root;

        Takte(5);
        GD.Print($"pause-bei-hilfe-check Anfang: {Lage()}");
        int z0 = Zahl993();

        // Teil 1: Option EIN, Hilfe auf (show_text)
        UI.HelpWindow.ShowOhneRiegel(wurzel, a, 120, 120);
        Takte(1);
        int t1 = _entities.TaktNrFuerProbe;
        Takte(5);
        bool teil1 = Zahl993() == z0 + 1 && _entities.NetzPause == 1000 && Art40() && _entities.TaktNrFuerProbe == t1;
        GD.Print($"pause-bei-hilfe-check Teil 1 (EIN, Hilfe #{a} auf): {Lage()} -> {(teil1 ? "OK" : "FALSCH")}");

        // Teil 2: Hilfe zu → Art 40 bleibt
        int zu = UI.HelpWindow.SchliesseAlleWieSpieler();
        GetTree().Paused = false;   // Nullmodell: der alte Weg gibt den Baum hier ohnehin frei
        Takte(5);
        bool teil2 = zu == 1 && Zahl993() == z0 + 1 && _entities.NetzPause == 1000 && Art40() && _entities.TaktNrFuerProbe == t1;
        GD.Print($"pause-bei-hilfe-check Teil 2 (Hilfe zu, {zu} Fenster): {Lage()} -> {(teil2 ? "OK, Art 40 bleibt" : "FALSCH")}");

        // Teil 3: bei stehender Pause noch ein Hilfefenster (show_text2) → Umschalter
        UI.HelpWindow.Show(wurzel, b, 200, 200);
        Takte(1);
        int t3 = _entities.TaktNrFuerProbe;
        Takte(5);
        bool teil3 = Zahl993() == z0 + 2 && _entities.NetzPause == 0 && !Art40() && _entities.TaktNrFuerProbe == t3 + 5;
        GD.Print($"pause-bei-hilfe-check Teil 3 (Pause steht, Hilfe #{b} auf): {Lage()} -> {(teil3 ? "OK, umgeschaltet" : "FALSCH")}");

        // Teil 4: dasselbe Fenster noch einmal, solange offen → nichts
        int g4 = UI.HelpWindow.PauseBeiHilfeGesendet;
        UI.HelpWindow.ShowOhneRiegel(wurzel, b, 200, 200);
        Takte(3);
        bool teil4 = UI.HelpWindow.PauseBeiHilfeGesendet == g4 && Zahl993() == z0 + 2 && _entities.NetzPause == 0;
        GD.Print($"pause-bei-hilfe-check Teil 4 (#{b} schon offen, nochmal): {Lage()} -> {(teil4 ? "OK, kein 993" : "FALSCH")}");
        UI.HelpWindow.SchliesseAlleWieSpieler();
        GetTree().Paused = false;

        // Teil 5: Option AUS
        UI.Settings.PauseOnHelpTest = false;
        int g5 = UI.HelpWindow.PauseBeiHilfeGesendet, z5 = Zahl993(), t5 = _entities.TaktNrFuerProbe;
        UI.HelpWindow.ShowOhneRiegel(wurzel, a, 120, 120);
        bool baum5 = GetTree().Paused;
        Takte(5);
        bool teil5 = UI.HelpWindow.PauseBeiHilfeGesendet == g5 && Zahl993() == z5 && _entities.NetzPause == 0 && !Art40()
                     && !baum5 && _entities.TaktNrFuerProbe == t5 + 5;
        GD.Print($"pause-bei-hilfe-check Teil 5 (AUS, Hilfe #{a} auf): {Lage()} -> {(teil5 ? "OK, nichts" : "FALSCH")}");
        UI.HelpWindow.SchliesseAlleWieSpieler();
        UI.Settings.PauseOnHelpTest = null;
        GetTree().Paused = false;

        bool gut = teil1 && teil2 && teil3 && teil4 && teil5;
        GD.Print($"PAUSE-BEI-HILFE-ERGEBNIS: Teil1 {teil1} Teil2 {teil2} Teil3 {teil3} Teil4 {teil4} Teil5 {teil5}, " +
                 $"993 angewandt {Zahl993() - z0}, von Hilfefenstern gesendet {UI.HelpWindow.PauseBeiHilfeGesendet}" +
                 (alt ? " ⚠ NULLMODELL --pause-bei-hilfe-aus" : "") +
                 $" -> {(gut ? "BESTANDEN" : "DURCHGEFALLEN")}");
        GetTree().Quit(gut ? 0 : 1);
    }

    /// <summary><c>--net-pause-bei-hilfe-check</c>: der Mitspieler öffnet ein Hilfefenster
    /// (show_text) und schliesst es gleich wieder.</summary>
    internal string NetzProbeHilfe()
    {
        int id = -1;
        for (int i = 1; i <= 600 && id < 0; i++)
            if (UI.HelpWindow.TextOf(i) is { Count: > 0 }) id = i;
        if (id < 0) return "KEIN Hilfetext (help.json fehlt)";
        UI.Settings.PauseOnHelpTest = true;
        int vor = UI.HelpWindow.PauseBeiHilfeGesendet;
        var w = UI.HelpWindow.ShowOhneRiegel(GetTree().Root, id, 120, 120);
        int zu = UI.HelpWindow.SchliesseAlleWieSpieler();
        return $"Hilfefenster #{id} {(w != null ? "auf" : "NICHT auf")} und wieder zu ({zu}), 993 gesendet " +
               $"{UI.HelpWindow.PauseBeiHilfeGesendet - vor}";
    }
}
