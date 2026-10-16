namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// <c>--speicherfenster-check</c> (bug-522, KayelGee R5#4) — die Fenster »Spiel speichern«
/// (Art 15) und »Spielstand laden« (Art 14) auf dem ECHTEN Weg: F2/F3 als Tasten, Klicks
/// und Zeichen als Ereignisse (<c>PushInput</c>), Laden per Doppelklick mit Szenenwechsel.
/// Gedacht für <c>--campaign=2 --no-briefing</c>; beendet sich selbst (kein --quit-after).
///
/// <para>Geprüft (Bericht §6 Punkt 7): Trefferpunkte beider Fenster, Pause an/aus und die
/// fremde Pause, Enter und Sichern ohne Platz tun nichts, der erste Platz bleibt gewählt,
/// Zeichenfilter <c>[ ] ^</c> und ≥ 0x7B, 20-Zeichen-Grenze, Backspace, Enter schreibt
/// <c>GAME.003</c> mit Name/Mission/Spieler im Kopf, der Puffer ist der VORHANDENE Name, ein
/// liegengebliebenes Zeichen wird nach dem Platzklick angehängt (§3.3), leerer Name →
/// »Leere Position...« und im Ladefenster nicht anwählbar, Doppelklick lädt — und danach
/// derselbe Fingerabdruck wie bei <c>--spielstand-check</c>.</para>
///
/// <para>⚠ Die Spielstände gehen in <c>user://saves/pruefstand_speicherfenster</c>
/// (<see cref="Core.SaveGame.OrdnerPruefstand"/>) und werden am Ende gelöscht — der
/// Benutzerordner ist mit dem Spiel des Spielers geteilt. Nullmodell
/// <c>--speicherfenster-alt</c>: kein Fenster, F2 tut nichts, der alte Knopf schreibt
/// <c>save_*</c> → DURCHGEFALLEN.</para>
/// </summary>
public partial class MapViewer
{
    public static bool SpeicherfensterCheckAn;

    private const string SfOrdner = "user://saves/pruefstand_speicherfenster";

    /// <summary>0 = wartet, 1 = läuft, 2 = lädt (Szenenwechsel), 3 = geladen, wartet auf
    /// den Skriptzustand, 4 = fertig.</summary>
    private static int _sfPhase;
    private static bool _sfOk = true;
    private static readonly StringBuilder _sfSb = new();
    private static string _sfVorher = "", _sfSkriptVorher = "";
    private float _sfUhr;

    private static string JaNein(bool b) => b ? "ja" : "NEIN";

    private static void SfPruef(string was, bool ok, string wert = "")
    {
        _sfOk &= ok;
        _sfSb.Append($"\n   {(ok ? "  ok   " : "⚠ FEHLT")} {was}{(wert.Length > 0 ? $"  [{wert}]" : "")}");
    }

    private void SpeicherfensterCheckTakt(double delta)
    {
        if (!SpeicherfensterCheckAn || _entities == null) return;
        _sfUhr += (float)delta;
        if (_sfPhase == 0 && _sfUhr >= 8f)
        {
            _sfPhase = 1;
            _ = SpeicherfensterLauf();
            return;
        }
        if (_sfPhase == 3 && MapEntityLayer.SkriptZustandGeladen.Length > 0)
        {
            bool gleich = MapEntityLayer.SkriptZustandGeladen == _sfSkriptVorher;
            SfPruef("Skriptzustand nach dem Laden zeichengleich", gleich,
                    $"{_sfSkriptVorher.Length} / {MapEntityLayer.SkriptZustandGeladen.Length} Zeichen");
            SfEnde();
        }
        else if (_sfPhase == 3 && _sfUhr > 5f)
        {
            SfPruef("Skriptzustand nach dem Laden", _sfSkriptVorher.Length == 0,
                    _sfSkriptVorher.Length == 0 ? "Karte ohne Skript" : "VERLOREN");
            SfEnde();
        }
    }

    private static void SfAufraeumen()
    {
        using var d = DirAccess.Open(SfOrdner);
        if (d != null)
            foreach (string f in d.GetFiles()) DirAccess.RemoveAbsolute($"{SfOrdner}/{f}");
        DirAccess.RemoveAbsolute(SfOrdner);
    }

    private static int SfDateien(out string namen)
    {
        var sb = new StringBuilder();
        int n = 0;
        using var d = DirAccess.Open(SfOrdner);
        if (d != null)
            foreach (string f in d.GetFiles()) { n++; sb.Append(f).Append(' '); }
        namen = sb.ToString().Trim();
        return n;
    }

    private void SfEnde()
    {
        _sfPhase = 4;
        SfAufraeumen();
        Core.SaveGame.OrdnerPruefstand = null;
        GD.Print(_sfSb.ToString());
        GD.Print($"speicherfenster-check: {(_sfOk ? "BESTANDEN" : "DURCHGEFALLEN")}"
                 + (UI.SpeicherfensterChrome.Alt ? "  (Nullmodell --speicherfenster-alt)" : ""));
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task SfBilder(int n)
    {
        for (int i = 0; i < n; i++)
        {
            // ⚠ nach dem Laden ist diese Szene frei — dann nicht mehr nach dem Baum fragen
            if (!IsInstanceValid(this) || !IsInsideTree()) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task SfTaste(Key k, long unicode = 0)
    {
        GetViewport().PushInput(new InputEventKey { Keycode = k, Unicode = (char)unicode, Pressed = true });
        GetViewport().PushInput(new InputEventKey { Keycode = k, Unicode = (char)unicode, Pressed = false });
        await SfBilder(1);
    }

    private async System.Threading.Tasks.Task SfTippen(string s)
    {
        foreach (char ch in s)
        {
            Key k = ch is >= 'a' and <= 'z' ? Key.A + (ch - 'a') : Key.None;
            await SfTaste(k, ch);
        }
    }

    private async System.Threading.Tasks.Task SfKlick(UI.SpeicherfensterChrome c, int x, int y,
                                                      bool doppel = false, bool rechts = false)
    {
        var punkt = c.GetGlobalTransformWithCanvas() * new Vector2(x * UI.SpeicherfensterChrome.Scale,
                                                                   y * UI.SpeicherfensterChrome.Scale);
        var knopf = rechts ? MouseButton.Right : MouseButton.Left;
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = knopf, Pressed = true, Position = punkt }, true);
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = knopf, Pressed = false, Position = punkt }, true);
        if (doppel)
            GetViewport().PushInput(new InputEventMouseButton
            { ButtonIndex = knopf, Pressed = true, DoubleClick = true, Position = punkt }, true);
        await SfBilder(2);
    }

    private bool SfTreffer(UI.SpeicherfensterChrome c, (int X, int Y, int Soll, string Was)[] tests, out string zeile)
    {
        var sb = new StringBuilder();
        bool ok = true;
        foreach (var t in tests)
        {
            int h = c.Hit(new Vector2(t.X, t.Y));
            ok &= h == t.Soll;
            sb.Append($"{t.Was} {h}{(h == t.Soll ? "" : $"≠{t.Soll}")} · ");
        }
        zeile = sb.ToString();
        return ok;
    }

    private async System.Threading.Tasks.Task SpeicherfensterLauf()
    {
        const int S = UI.SpeicherfensterChrome.Scale;
        Core.SaveGame.OrdnerPruefstand = SfOrdner;
        SfAufraeumen();
        Core.SaveGame.EnsureFolder();
        _sfSb.Clear();
        _sfSb.Append($"speicherfenster-check: Ordner {SfOrdner}, Mission {UI.SkirmishSetup.CampaignMission}, "
                   + $"--speicherfenster-alt {UI.SpeicherfensterChrome.Alt}");

        // ---- Art 15 über F2 ----------------------------------------------------
        bool vorherPausiert = GetTree().Paused;
        await SfTaste(Key.F2);
        var c = _speicherChrome;
        bool offen = c != null && c.Offen && UI.WindowManager.Offen(UI.SpeicherfensterChrome.ArtSpeichern) != null;
        SfPruef("F2 öffnet »Spiel speichern« (Art 15)", offen);
        if (!offen || c == null)
        {
            // Nullmodell: der alte Weg — sofort save_<Unixzeit>, kein Fenster.
            string n = SpielstandSchreiben();
            int z = SfDateien(out string namen);
            _sfSb.Append($"\n   Nullmodell: alter Knopf schreibt »{n}« ({z} Datei: {namen}), kein Platz, kein Name");
            SfPruef("Platzdatei GAME.003 entsteht", false, "kein Fenster");
            SfEnde();
            return;
        }
        SfPruef("Art 15 hält das Spiel an, Merker gesetzt", !vorherPausiert && GetTree().Paused && _speicherPauseEigen);
        SfPruef("Art 15 Mass 300×360 (×2)", c.Size == new Vector2(600, 720), $"{c.Size}");
        bool h15 = SfTreffer(c, new (int, int, int, string)[]
        {
            (41 * S, 316 * S, 1, "Sichern"), (161 * S, 316 * S, 2, "Abbruch"),
            (21 * S, 26 * S, 1000, "Platz0"), (21 * S, 266 * S, 1006, "Platz6"),
            (21 * S, 306 * S, 0, "unter Platz6"), (279 * S, 64 * S, 1000, "Platz0 rechts"),
            (19 * S, 26 * S, 0, "links daneben"), (599, 1, -2, "Kreuz"), (20, 10, -5, "Titel"),
        }, out string hz15);
        SfPruef("Treffer Art 15 (0x45EDD3)", h15, hz15);

        // Enter und Sichern ohne Platz: nichts
        await SfTaste(Key.Enter);
        await SfKlick(c, 41, 316);
        SfPruef("Enter / Sichern ohne Platz tun nichts", c.Offen && SfDateien(out _) == 0 && c.Cursor == 0);

        // Platz 3 wählen, dann Platz 5 — der erste bleibt (0x44B568)
        await SfKlick(c, 30, 25 + 40 * 3 + 10);
        await SfKlick(c, 30, 25 + 40 * 5 + 10);
        SfPruef("Platz 3 gewählt, Platz 5 ändert nichts", c.Cursor == 1003, $"Cursor {c.Cursor}");

        await SfTippen("ab[c]^d");
        // ⚠ Der Bericht (§6 Punkt 7) erwartet hier »abd«. 0x4588E0 prüft aber JEDES Zeichen
        // einzeln: [ ] ^ fallen weg, das c dazwischen bleibt — also »abcd«.
        SfPruef("Zeichenfilter ohne [ ] ^ (Zeichen für Zeichen)", c.Namen[3] == "abcd", $"»{c.Namen[3]}«");
        await SfTippen(new string('x', 20));
        SfPruef("höchstens 20 Zeichen", c.Namen[3].Length == 20, $"{c.Namen[3].Length}");
        for (int i = 0; i < 16; i++) await SfTaste(Key.Backspace);
        await SfTippen("Ä{|}~");
        SfPruef("Backspace; Ä { | } ~ abgewiesen", c.Namen[3] == "abcd", $"»{c.Namen[3]}«");
        await SfTippen(" Test");

        // Was beim Laden verloren gehen KANN, und der Fingerabdruck — der Baum steht.
        string gelegt = _entities.SpielstandProbeVorbereiten();
        _sfVorher = _entities.SpielstandFingerabdruck();
        _sfSkriptVorher = _entities.SkriptZustandJetzt();
        _sfSb.Append($"\n   gelegt: {gelegt}");

        await SfTaste(Key.Enter);
        var k3 = Core.SaveGame.PlatzLesen(3);
        string pfad3 = Core.SaveGame.PathOf(Core.SaveGame.PlatzName(3));
        SfPruef("Enter schreibt GAME.003, Fenster zu, Pause aufgehoben",
                k3 != null && !c.Offen && !GetTree().Paused, pfad3);
        SfPruef("Kopf: Name/Mission/Spieler",
                k3 is { } kk && kk.Name == "abcd Test" && kk.Mission == UI.SkirmishSetup.CampaignMission
                && kk.Spieler == SpielernameSetzung,
                k3 is { } k ? $"»{k.Name}« Mission {k.Mission} »{k.Spieler}«" : "keine Datei");

        // Fremde Pause bleibt stehen (0x443B26 Merker 0)
        GetTree().Paused = true;
        SpeicherfensterOeffnen();
        bool merker0 = !_speicherPauseEigen;
        await SfKlick(c, 161, 316);                 // Abbruch
        SfPruef("fremde Pause: Merker 0, nach Abbruch weiter pausiert", merker0 && !c.Offen && GetTree().Paused);
        GetTree().Paused = false;

        // Leerer Name auf Platz 0 — wird geschrieben, zeigt sich leer (§6 Rückfrage 1: original)
        SpeicherfensterOeffnen();
        await SfKlick(c, 30, 25 + 10);
        await SfKlick(c, 41, 316);                  // Sichern mit Platz, Name leer
        var k0 = Core.SaveGame.PlatzLesen(0);
        SfPruef("leerer Name: GAME.000 geschrieben, Name leer", k0 is { Name: "" } && !c.Offen);

        // Wieder auf: Platz 3 trägt seinen Namen, der Puffer IST der Name (anhängen)
        SpeicherfensterOeffnen();
        bool gelesen = c.Namen[3] == "abcd Test" && c.Namen[0] == "" && c.Missionen[3] == UI.SkirmishSetup.CampaignMission;
        await SfTippen("Q");                        // liegt, bevor ein Platz gewählt ist (§3.3)
        await SfKlick(c, 30, 25 + 40 * 3 + 10);
        SfPruef("Anleger liest Name/Mission; Puffer = vorhandener Name + liegengebliebenes Zeichen",
                gelesen && c.Namen[3] == "abcd TestQ", $"»{c.Namen[3]}«");
        await SfTaste(Key.Escape);
        SfPruef("ESC schliesst (ohne zu schreiben), Pause aufgehoben",
                !c.Offen && !GetTree().Paused && Core.SaveGame.PlatzLesen(3)?.Name == "abcd Test");
        SpeicherfensterOeffnen();
        await SfKlick(c, 100, 200, rechts: true);
        SfPruef("Rechtsklick schliesst", !c.Offen && !GetTree().Paused);

        // ---- Art 14 über F3 ----------------------------------------------------
        await SfTaste(Key.F3);
        var l = _ladeChrome;
        bool lOffen = l != null && l.Offen && UI.WindowManager.Offen(UI.SpeicherfensterChrome.ArtLaden) != null;
        SfPruef("F3 öffnet »Spielstand laden« (Art 14), keine Pause", lOffen && !GetTree().Paused);
        if (!lOffen || l == null) { SfEnde(); return; }
        for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
        SfPruef("Art 14 Mass 300×400 (×2)", l.Size == new Vector2(600, 800), $"{l.Size}");
        bool h14 = SfTreffer(l, new (int, int, int, string)[]
        {
            (41 * S, 356 * S, 1, "Laden"), (161 * S, 356 * S, 2, "Abbruch"),
            (21 * S, 26 * S, 0, "Platz0 leerer Name"), (21 * S, 146 * S, 1003, "Platz3"),
            (21 * S, 306 * S, 0, "Platz7 leer"), (21 * S, 66 * S, 0, "Platz1 keine Datei"),
            (599, 1, -2, "Kreuz"), (20, 10, -5, "Titel"),
        }, out string hz14);
        SfPruef("Treffer Art 14 (0x45EC45): nur belegte Plätze", h14, hz14);
        await SfKlick(l, 41, 356);
        SfPruef("Laden ohne Platz tut nichts", l.Offen && l.Cursor == 0);
        await SfKlick(l, 30, 25 + 10);              // leerer Platz 0: trifft nicht
        SfPruef("Klick auf leeren Platz wählt nicht", l.Cursor == 0, $"Cursor {l.Cursor}");

        // Doppelklick auf Platz 3 = Laden (0x4486D9) → Szenenwechsel
        _sfPhase = 2;
        await SfKlick(l, 30, 25 + 40 * 3 + 10, doppel: true);
        // Kommt hier noch etwas an, hat der Szenenwechsel nicht stattgefunden.
        if (IsInstanceValid(this) && IsInsideTree() && _sfPhase == 2 && l.Offen)
        {
            SfPruef("Doppelklick lädt", false, $"Cursor {l.Cursor}");
            SfEnde();
        }
    }

    /// <summary>Aus <c>_Ready</c>, nach dem ganzen Startzweig — wie
    /// <c>SpielstandCheckNachLaden</c>.</summary>
    private void SpeicherfensterCheckNachLaden()
    {
        if (!SpeicherfensterCheckAn || _sfPhase != 2 || _entities == null) return;
        _sfPhase = 3;
        _sfUhr = 0f;
        SfPruef("Doppelklick lädt GAME.003 (Szenenwechsel)", true);
        string nachher = _entities.SpielstandFingerabdruck();
        string[] a = _sfVorher.Split(" | "), b = nachher.Split(" | ");
        int ab = 0;
        var sb = new StringBuilder();
        for (int i = 0; i < a.Length; i++)
        {
            string n = i < b.Length ? b[i] : "—";
            if (a[i] == n) continue;
            ab++;
            sb.Append($" {a[i]} -> {n};");
        }
        SfPruef("Zustand nach dem Laden DECKUNGSGLEICH (Fingerabdruck wie --spielstand-check)",
                ab == 0, ab == 0 ? $"{a.Length} Teile" : $"{ab} von {a.Length}:{sb}");
        SfPruef("Baum läuft nach dem Laden", !GetTree().Paused);
    }
}
