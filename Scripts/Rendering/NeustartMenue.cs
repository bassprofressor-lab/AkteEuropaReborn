namespace AkteEuropaReborn.Rendering;

using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// ⭐ 22.10.2026, Paket N (Testerrunde 0.6.11) — die Oberfläche zu Neustart 980 (bug-615),
/// Pause/Taste P (bug-617) und dem Haupt-Menü im Netz (bug-618). Berichte
/// <c>berichte/neustart-980-netz-opus.md</c> und <c>berichte/pausenfenster-40-opus.md</c>.
///
/// <para><b>Haupt-Menü im Netz</b> (Spielerentscheidung 22.10.): der Menüknopf des
/// Bedienfelds öffnet Art 17 auch im Netz, wie das Original (Bedienfeld Knopf 4 → C
/// 0x44AFF7, kein Verweis auf den Netzschalter 0x539234 im Weg). Was darin den
/// Gleichschritt bräche, läuft über den Ring oder ist gesperrt — Begründung je Knopf in
/// <see cref="NetzMenueSperren"/>. ⚠ SETZUNG: im Netz hält das Menü den Baum nicht an.
/// Gegenschalter <c>--netz-menue-alt</c> (Menüknopf = Pause 993 wie bis 0.6.11).</para>
/// </summary>
public partial class MapViewer
{
    private static bool Schalter(string s) => Array.IndexOf(Core.CommandLine.Args, s) >= 0;

    /// <summary><c>--taste-p-navi-alt</c>: P schaltet im Einzelspiel wieder die Navi-Anzeige.</summary>
    public static bool TastePNaviAlt => Schalter("--taste-p-navi-alt");

    /// <summary><c>--netz-menue-alt</c>: der Menüknopf pausiert im Netz (993) statt Art 17.</summary>
    public static bool NetzMenueAlt => Schalter("--netz-menue-alt");

    /// <summary><c>--pause-esc-oertlich</c>: die ESC-Falle des Originals (C 0x4472F5), NUR zum
    /// Messen — hebt die Pause ohne Ring nur hier auf.</summary>
    public static bool PauseEscOertlich => Schalter("--pause-esc-oertlich");

    /// <summary>Für die Netzprüfstände (NetGameRunner kennt nur die Simulation).</summary>
    internal static MapViewer? Aktiv;

    /// <summary>Taste P — F 0x4134C5: <c>word[0xB89438] := 0x3E1</c> (993), Bus. ⭐ Paket N:
    /// auch im Einzelspiel. Die Navi-Anzeige (unser Begehbarkeitsraster, im Original ohne
    /// Gegenstück — also auch ohne Taste) bleibt über <c>--nav</c> erreichbar.</summary>
    internal void TasteP()
    {
        if (!TastePNaviAlt || (MapEntityLayer.ImNetz && !MapEntityLayer.Alt2("pause"))) _entities.PostPause();
        else _entities.ToggleNav();
    }

    /// <summary>Der Menüknopf des Bedienfelds (Knopf 4, C 0x44AFF7): Art 17 auf bzw. zu.</summary>
    private void MenueKnopf()
    {
        if (_pause != null || _hauptmenue != null) { ClosePause(); return; }
        if (MapEntityLayer.ImNetz && !MapEntityLayer.Alt2("pause") && NetzMenueAlt) { _entities.PostPause(); return; }
        if (UI.MainMenuWindow.Usable && !UI.MainMenuWindow.Alt) { OeffneHauptmenue(); return; }
        TogglePause();
    }

    /// <summary>
    /// <b>Was das Haupt-Menü im Netz darf.</b> Jeder der zwölf Knöpfe, mit Grund:
    /// <list type="bullet">
    /// <item>Missionsinformation, Untermissionen: nur Kampagne — Netzspiele sind Gefechte
    /// (Untermissionen werden ausdrücklich gesperrt, sie wechseln die Szene).</item>
    /// <item>Einheiten-, Gebäude-, Forschungsliste, Enzyklopädie, CD-Spieler: lesen nur bzw.
    /// wählen ÖRTLICH an (die Auswahl reist erst mit einem Befehl) — frei.</item>
    /// <item>Einstellungen: frei. Grundtakt wirkt im Netz nicht (SimZeit, bug-527), der
    /// Nebel steht dort fest (NebelImNetzFest), »Gruppe/Formation« wirkt beim ABSENDER
    /// (der Wunschort reist im Satz, CommandBridge.PostMove), der Rest ist Ton und Bild.</item>
    /// <item>Mission wiederholen: Abfrage, dann 980 über den Ring (netzfest).</item>
    /// <item>Spielstand speichern / Altes Spiel laden: nur über die Fenster Art 15/14, die
    /// 991/990 über den Ring schicken. Ohne sie (Eigenbau-Schirm) gesperrt — der lüde mit
    /// Szenenwechsel bzw. schriebe nur hier.</item>
    /// <item>Spiel beenden: gesperrt. Das Original verlässt ein Netzspiel über 995/996
    /// (Unterart 4, Netztor C 0x44B1BD); das ist nicht gebaut, und ein örtlicher
    /// Szenenwechsel ließe den Taktgeber der anderen ins Leere warten (und unseren als
    /// Waisen zurück).</item>
    /// </list></summary>
    private void NetzMenueSperren(UI.MainMenuWindow w)
    {
        const string netz = "Im Netzspiel gesperrt: ";
        w.OnQuit = null;
        w.GrundHier[9] = netz + "»Spiel verlassen« (Befehl 995/996) ist noch nicht gebaut.";
        w.OnSubMissions = null;
        w.GrundHier[4] = netz + "Untermissionen gibt es nur in der Kampagne.";
        if (!SpeicherfensterNeu || MapEntityLayer.Alt2("spielstand"))
        {
            w.OnSave = null; w.OnLoad = null;
            w.GrundHier[7] = w.GrundHier[8] = netz + "Speichern/Laden nur über die Fenster Art 15/14 (Befehl 991/990).";
        }
        if (MapEntityLayer.NeustartAlt || MapEntityLayer.Alt2("neustart"))
        {
            w.OnRestart = null;
            w.GrundHier[5] = netz + "--neustart-alt lädt örtlich neu.";
        }
        w.QueueRedraw();
    }

    /// <summary>Knopf 6 »Mission wiederholen« (C 0x44B9D3): Menü zu, dann die Abfrage Art 10
    /// Unterart 3 (C 0x446370); »Ja« schickt 980 (F 0x44A17F). ⚠ SETZUNG: die Abfrage steht
    /// mittig wie alle EntwurfDialog-Fenster, nicht bei (MausX−3, MausY−3).</summary>
    private void NeustartKnopf()
    {
        ClosePause();
        if (MapEntityLayer.NeustartAlt)
        {
            if (MapEntityLayer.ImNetz) { _entities.Say("Im Netzspiel nicht verfügbar (--neustart-alt)."); return; }
            GD.Print("neustart: --neustart-alt — sofort neu laden, ohne Abfrage und ohne 980");
            GetTree().ReloadCurrentScene();
            return;
        }
        if (_entwurfDialog == null || !_entwurfDialog.FrageJaNein(MapEntityLayer.NeustartFrage, () => _entities.PostNeustart()))
            GD.Print("neustart: Abfrage nicht geöffnet (es steht schon ein Abfragefenster)");
    }

    /// <summary>Behandler 980 im Einzelspiel: der Missions-Reset = die Szene neu.</summary>
    private void NeustartEinzelspiel()
    {
        ClosePause();
        Callable.From(() => GetTree().ReloadCurrentScene()).CallDeferred();
    }

    /// <summary>Behandler 980 im Netz: wie 0x44FE10(1) die Fenster zu (der Stand selbst ist
    /// schon eingespielt). Die Pause schliesst der Behandler über OnNetzPause.</summary>
    private void NeustartNetzFensterZu()
    {
        ClosePause();
        if (_entwurfDialog is { Visible: true }) _entwurfDialog.NeinKlick();
        UpdateUnitOrderBar();
    }

    // ================= Prüfstandsgriffe für NetGameRunner ======================

    /// <summary><c>--net-neustart-check</c>: der ganze Weg eines Spielers — Menüknopf,
    /// Knopf 6, Abfrage, »Ja«.</summary>
    internal string NetzProbeNeustart()
    {
        MenueKnopf();
        if (_hauptmenue == null) return "KEIN Haupt-Menü im Netz";
        bool geht = _hauptmenue.Ausloesen(5);
        var d = UI.EntwurfDialog.Letzter;
        if (!geht || d == null || !d.Visible || d.Art != 10) return $"keine Abfrage (Knopf geht {geht})";
        string frage = d.Frage;
        d.JaKlick();
        return $"Menü → Knopf 6 → Abfrage »{frage}« → Ja";
    }

    /// <summary><c>--pausenfenster-check</c>: Taste P.</summary>
    internal string NetzProbeTasteP() { TasteP(); return "Taste P"; }

    /// <summary><c>--pausenfenster-check</c>: »Weiter« über den Treffertest; ohne Fenster
    /// (Nullmodell) ersatzweise 993 wie P, damit der Ablauf weiterläuft.</summary>
    internal string NetzProbeWeiter()
    {
        var f = UI.PausenFenster.Offen;
        if (f == null || !f.Visible) { _entities.PostPause(); return "KEIN Fenster Art 40 — ersatzweise 993"; }
        int t = f.Klick(UI.PausenFenster.KnopfMitte);
        return $"Klick »Weiter« (Treffer {t})";
    }

    // ================= --neustart-check (Einzelspiel) ==========================

    private static int _nsPhase;
    private static string _nsF0 = "", _nsF300 = "";
    public static bool NeustartCheckAn => Schalter("--neustart-check");

    private async Task NeustartCheckLauf()
    {
        // ⚠ Ein Durchfall im ersten Lauf kann eine Szene im Flug haben (das Nullmodell lädt
        // sofort neu) — GetTree().Quit() wartet bis zum Bildende, der Wechsel kommt zuerst,
        // und der neue Lauf finge von vorn an. Gemessen: 1829 Neustarts in 10 Minuten.
        if (_nsPhase == 3) { GetTree().Quit(1); return; }
        // Die Simulation taktet nur noch der Prüfstand — sonst hinge der Vergleich an der
        // Bildzeit des ersten Bildes.
        _entities.SetProcess(false);
        _entities.SetPhysicsProcess(false);
        for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        void Takte(int n) { for (int t = 0; t < n; t++) _entities.SimTickFuerProbe(); }
        int Zahl980() => _entities.AngenommenJeBefehl.TryGetValue(Simulation.Commands.CommandOp.Neustart, out int k) ? k : 0;
        string f0 = _entities.NeustartFingerabdruck();
        // Teil 0 (in beiden Läufen gleich): etwas tun, damit sich Stand, Takt und Uhr
        // sichtbar vom Anfang entfernen.
        var units = _entities.CommandCheckMobileUnits(6);
        if (units.Count > 0)
        {
            _entities.CommandCheckSelect(units);
            var cell = _entities.CommandCheckClickCell(units[0], 6);
            if (cell.X >= 0) _entities.PostMove(_entities.CellCenterFor(cell));
        }

        if (_nsPhase == 1)
        {
            Takte(300);
            string f300 = _entities.NeustartFingerabdruck();
            bool a = f0 == _nsF0, b = f300 == _nsF300;
            GD.Print($"neustart-check Teil 3 (nach dem Neustart):\n  Anfang  {f0}\n  vorher  {_nsF0}  -> {(a ? "GLEICH" : "UNGLEICH")}\n" +
                     $"  Takt 300 {f300}\n  vorher   {_nsF300}  -> {(b ? "GLEICH" : "UNGLEICH")}");
            GD.Print(a && b
                ? "neustart-check: BESTANDEN — Abfrage, Nein ohne Wirkung, Ja = genau ein 980, Mission auf Startstand (Takt, Uhr, Geld, Einheiten, Forschung, Prüfsumme) und läuft gleich weiter"
                : "neustart-check: DURCHGEFALLEN — der Neustart führt nicht auf den Startstand zurück");
            _nsPhase = 2;
            GetTree().Quit(a && b ? 0 : 1);
            return;
        }

        Takte(300);
        string f300a = _entities.NeustartFingerabdruck();
        GD.Print($"neustart-check Teil 0: {units.Count} Einheit(en) losgeschickt\n  Anfang   {f0}\n  Takt 300 {f300a}");

        // Teil 1: die Abfrage über den Menüweg, »Nein«.
        bool alt = MapEntityLayer.NeustartAlt;
        if (alt) GD.Print("  ⚠ NULLMODELL --neustart-alt: der Knopf lädt sofort neu, ohne Abfrage und ohne 980");
        MenueKnopf();
        bool menue = _hauptmenue != null;
        if (menue) _hauptmenue!.Ausloesen(5); else NeustartKnopf();
        var d = UI.EntwurfDialog.Letzter;
        bool frage = d != null && d.Visible && d.Art == 10 && d.Frage == MapEntityLayer.NeustartFrage;
        GD.Print($"neustart-check Teil 1: Weg {(menue ? "Haupt-Menü Art 17 Knopf 6" : "ohne Art 17")}, Abfrage " +
                 (frage ? $"Art 10 »{d!.Frage}«" : "FEHLT"));
        if (!frage)
        {
            GD.Print($"neustart-check: DURCHGEFALLEN — keine Abfrage, 980 im Ring: {Zahl980()} (alter Weg lädt ohne Frage neu)");
            { _nsPhase = 3; GetTree().Quit(1); }
            return;
        }
        d!.NeinKlick();
        Takte(5);
        string fNein = _entities.NeustartFingerabdruck();
        bool neinOk = Zahl980() == 0 && _entities.NeustartAngewandt == 0 && fNein != f0;
        GD.Print($"neustart-check Teil 1b »Nein«: 980 angewandt {Zahl980()}, Stand {fNein} -> {(neinOk ? "keine Wirkung" : "FALSCH")}");

        // Teil 2: noch einmal, »Ja« → genau ein 980 im Ring, im Folgetakt angewandt.
        MenueKnopf();
        if (_hauptmenue != null) _hauptmenue.Ausloesen(5); else NeustartKnopf();
        d = UI.EntwurfDialog.Letzter;
        if (d == null || !d.Visible) { GD.Print("neustart-check: DURCHGEFALLEN — zweite Abfrage fehlt"); { _nsPhase = 3; GetTree().Quit(1); } return; }
        d.JaKlick();
        int vor = Zahl980();
        Takte(1);
        int nach = Zahl980();
        GD.Print($"neustart-check Teil 2 »Ja«: 980 angewandt {vor} -> {nach} (Takt {_entities.NeustartTakt})");
        if (!neinOk || nach != 1)
        {
            GD.Print("neustart-check: DURCHGEFALLEN — " + (!neinOk ? "»Nein« hat gewirkt" : "kein 980 im Ring"));
            { _nsPhase = 3; GetTree().Quit(1); }
            return;
        }
        _nsF0 = f0; _nsF300 = f300a; _nsPhase = 1;
        GD.Print("neustart-check: Szene wird neu geladen (Missions-Reset), Teil 3 folgt");
    }
}
