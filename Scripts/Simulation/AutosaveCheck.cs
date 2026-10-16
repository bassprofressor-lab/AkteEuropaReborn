namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// <c>--autosave-check</c> (bug-524) — das Autosichern des Originals (<see cref="AutosaveTakt"/>,
/// <c>0x415480</c>) auf dem echten Weg: die Uhr läuft als Knoten im Baum, geschrieben wird
/// über <c>SpielstandSchreiben</c>, gelesen über den Anleger des Ladefensters. Gedacht für
/// <c>--campaign=2 --no-briefing --no-movie</c>; beendet sich selbst (kein --quit-after).
///
/// <para>Messschalter <c>--autosave-minuten-test=S</c> (eine »Minute« = S Sekunden; ohne
/// Angabe stellt der Prüfstand 0,6 s ein, 5 Min. = 3 s). Die Einstellung wird über
/// <see cref="UI.Settings.AutosaveTest"/> gestochen — <c>settings.cfg</c> bleibt unberührt.
/// Die Dateien gehen nach <c>user://saves/pruefstand_autosave</c> und werden gelöscht.</para>
///
/// <para>Geprüft: AUS → keine Datei; Umschalter 0→5→10→20→0 mit den Beschriftungen des
/// Originals; 5 Min. → sofort ein GAME.007 (Fälligkeit 0 aus dem BSS) mit Kopf »Autosave«,
/// Platz 7, Mission; vor Ablauf keine zweite Datei; nach Ablauf eine, und zwar IN DER PAUSE
/// (Wanduhr); Schliessen der Einstellungen zieht nur vor; das Ladefenster (Art 14) trifft
/// Platz 7. Nullmodell <c>--autosave-alt</c> → DURCHGEFALLEN.</para>
/// </summary>
public partial class MapViewer
{
    public static bool AutosaveCheckAn;

    private const string AcOrdner = "user://saves/pruefstand_autosave";
    private int _acPhase;               // 0 wartet, 1 läuft, 2 fertig
    private float _acUhr;
    private bool _acOk = true;
    private readonly StringBuilder _acSb = new();

    private void AcPruef(string was, bool ok, string wert = "")
    {
        _acOk &= ok;
        _acSb.Append($"\n   {(ok ? "  ok   " : "⚠ FEHLT")} {was}{(wert.Length > 0 ? $"  [{wert}]" : "")}");
    }

    private void AutosaveCheckTakt(double delta)
    {
        if (!AutosaveCheckAn || _entities == null || _acPhase != 0) return;
        _acUhr += (float)delta;
        if (_acUhr < 6f) return;
        _acPhase = 1;
        _ = AutosaveLauf();
    }

    private static string AcDatei => Core.SaveGame.PathOf(Core.SaveGame.PlatzName(Core.SaveGame.PlatzAutosave));

    private static void AcAufraeumen()
    {
        using var d = DirAccess.Open(AcOrdner);
        if (d != null)
            foreach (string f in d.GetFiles()) DirAccess.RemoveAbsolute($"{AcOrdner}/{f}");
        DirAccess.RemoveAbsolute(AcOrdner);
    }

    /// <summary>Bilder abwarten, bis <paramref name="bisMs"/> (GetTicksMsec) erreicht ist oder
    /// <paramref name="halt"/> wahr wird. Läuft auch bei angehaltenem Baum (process_frame).</summary>
    private async System.Threading.Tasks.Task AcWarten(ulong bisMs, System.Func<bool>? halt = null)
    {
        while (Time.GetTicksMsec() < bisMs)
        {
            if (halt != null && halt()) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async System.Threading.Tasks.Task AutosaveLauf()
    {
        if (AutosaveMinuteSek >= 59.9) AutosaveMinuteSek = 0.6;
        ulong span5 = (ulong)(5 * AutosaveMinuteSek * 1000.0);
        Core.SaveGame.OrdnerPruefstand = AcOrdner;
        AcAufraeumen();
        Core.SaveGame.EnsureFolder();
        _acSb.Clear();
        _acSb.Append($"autosave-check: Ordner {AcOrdner}, Mission {UI.SkirmishSetup.CampaignMission}, "
                   + $"1 Min. = {AutosaveMinuteSek:0.##} s, --autosave-alt {AutosaveAlt}");

        // ---- AUS: keine Datei ---------------------------------------------------
        UI.Settings.AutosaveTest = 0;
        AutosaveFaellig = 0;
        int zahl0 = AutosaveZahl;
        await AcWarten(Time.GetTicksMsec() + 1500);
        AcPruef("Autosichern AUS (Vorgabe 0, BSS): keine Datei", !FileAccess.FileExists(AcDatei)
                && AutosaveZahl == zahl0, $"{AutosaveZahl - zahl0} geschrieben");

        // ---- der Knopf 0x44C7B0 -------------------------------------------------
        var folge = new StringBuilder();
        int v = 0;
        for (int i = 0; i < 4; i++) { v = UI.Settings.AutosaveNaechste(v); folge.Append(UI.Settings.AutosaveText(v)).Append(" · "); }
        string folgeS = folge.ToString();
        AcPruef("Umschalter 0→5→10→20→0 (Tafel 0x44E000), Texte 0x47FCDB",
                folgeS == "Autosichern 5 Min. · Autosichern 10 Min. · Autosichern 20 Min. · Autosichern AUS · "
                && UI.Settings.AutosaveNaechste(7) == 7, folgeS + "7→" + UI.Settings.AutosaveNaechste(7));

        // ---- 5 Min.: erster Autosave sofort --------------------------------------
        UI.Settings.AutosaveTest = UI.Settings.AutosaveNaechste(0);   // ein Druck auf den Knopf
        AutosaveEinstellungZu();                                      // Fenster zu (0x447382)
        AcPruef("Schliessen der Einstellungen laesst die Faelligkeit 0 stehen", AutosaveFaellig == 0,
                $"{AutosaveFaellig}");
        ulong t0 = Time.GetTicksMsec();
        await AcWarten(t0 + 1500, () => FileAccess.FileExists(AcDatei));
        bool da = FileAccess.FileExists(AcDatei);
        var kopf = Core.SaveGame.PlatzLesen(Core.SaveGame.PlatzAutosave);
        AcPruef("5 Min.: GAME.007 sofort im ersten Bild (Faelligkeit 0)", da,
                $"{Time.GetTicksMsec() - t0} ms");
        AcPruef("Kopf: Name »Autosave« (0x4F743C), Platz 7, Mission",
                kopf is { Name: "Autosave", Platz: 7 } && kopf.Value.Mission == UI.SkirmishSetup.CampaignMission,
                kopf == null ? "kein Kopf" : $"»{kopf.Value.Name}« Platz {kopf.Value.Platz} Mission {kopf.Value.Mission}");
        long rest = (long)AutosaveFaellig - (long)Time.GetTicksMsec();
        AcPruef("naechste Faelligkeit = jetzt + 5·60000 ms (skaliert)", rest > (long)span5 - 800 && rest <= (long)span5,
                $"{rest} ms, Soll {span5}");

        // ---- vor Ablauf nichts, nach Ablauf in der Pause --------------------------
        ulong t1 = AutosaveFaellig > span5 ? AutosaveFaellig - span5 : Time.GetTicksMsec();
        DirAccess.RemoveAbsolute(AcDatei);
        await AcWarten(t1 + span5 * 7 / 10);
        AcPruef("vor Ablauf der 5 Min. keine neue Datei", !FileAccess.FileExists(AcDatei));
        bool warPausiert = GetTree().Paused;
        GetTree().Paused = true;
        await AcWarten(t1 + span5 + 1200, () => FileAccess.FileExists(AcDatei));
        bool inPause = FileAccess.FileExists(AcDatei);
        AcPruef("nach Ablauf gesichert — IN DER PAUSE (Wanduhr timeGetTime, keine Pausenabfrage)",
                inPause && GetTree().Paused, $"{(long)Time.GetTicksMsec() - (long)(t1 + span5)} ms nach Ablauf");
        GetTree().Paused = warPausiert;

        // ---- Einstellungen zu: nur vorziehen ------------------------------------
        AutosaveFaellig = Time.GetTicksMsec() + 20 * (ulong)(AutosaveMinuteSek * 1000.0);
        AutosaveEinstellungZu();
        long vor = (long)AutosaveFaellig - (long)Time.GetTicksMsec();
        ulong spaeter = Time.GetTicksMsec() + 300;
        AutosaveFaellig = spaeter;
        AutosaveEinstellungZu();
        AcPruef("Einstellungen zu (0x4473CD): 20→5 Min. zieht vor, 300 ms bleibt",
                vor <= (long)span5 && AutosaveFaellig == spaeter, $"{vor} ms / {(long)AutosaveFaellig - (long)spaeter}");
        UI.Settings.AutosaveTest = 0;                      // keine weiteren Dateien

        // ---- Ladefenster Art 14 zeigt Platz 7 -----------------------------------
        if (LadefensterOeffnen() && _ladeChrome != null)
        {
            const int S = UI.SpeicherfensterChrome.Scale;
            int h = _ladeChrome.Hit(new Vector2(21 * S, 306 * S));
            AcPruef("Ladefenster (Art 14) trifft Platz 7 »Autosave«", h == 1007, $"Hit {h}");
            _ladeChrome.Schliessen();
        }
        else AcPruef("Ladefenster (Art 14) laesst sich oeffnen", false);

        _acPhase = 2;
        AcAufraeumen();
        Core.SaveGame.OrdnerPruefstand = null;
        UI.Settings.AutosaveTest = null;
        GD.Print(_acSb.ToString());
        GD.Print($"autosave-check: {(_acOk ? "BESTANDEN" : "DURCHGEFALLEN")}"
                 + (AutosaveAlt ? "  (Nullmodell --autosave-alt)" : ""));
        GetTree().Quit();
    }
}
