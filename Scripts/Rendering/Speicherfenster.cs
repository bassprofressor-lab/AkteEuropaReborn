namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// bug-522 (KayelGee R5#4) — die Öffner der Fenster »Spiel speichern« (Art 15) und
/// »Spielstand laden« (Art 14) im laufenden Spiel, ihr Schreib- und Ladeweg und der
/// Missionsende-Stand auf Platz 7. Das Fenster selbst steht in
/// <see cref="UI.SpeicherfensterChrome"/>, die Lesung in <c>berichte/speicherfenster-fable.md</c>.
///
/// <para><b>Öffner</b> (§1): F2 → <c>0x443AA0(MausX−3, MausY−3)</c>, F3 →
/// <c>0x443B90(MausX−3, MausY−3)</c>; Haupt-Menü Knopf 8/9 dieselben, danach das Menü zu
/// (<c>0x44BA04</c>/<c>0x44BA35</c>). Doppelöffnungswache je Art (<c>0x443ADD</c>).</para>
///
/// <para><b>Pause</b> (<c>0x443B26</c>/<c>0x44730B</c>): Art 15 hält das Spiel an, wenn es
/// nicht schon steht, und merkt sich das (<c>byte[0x4FAEEC]</c> = <see cref="_speicherPauseEigen"/>);
/// beim Schliessen hebt es NUR die eigene Pause auf. Art 14 pausiert nicht.</para>
///
/// <para>⚠ SETZUNG beim Weg aus dem Haupt-Menü: das Original öffnet ERST das Fenster und
/// schliesst DANN das Menü. Bei uns hält das Menü selbst den Baum an (<see cref="ClosePause"/>
/// gibt ihn frei) — in der Reihenfolge des Originals sähe Art 15 »schon pausiert«, merkte sich
/// nichts, und das Schliessen des Menüs gäbe das Spiel unter dem offenen Speicherfenster frei.
/// Darum hier: Menü zu, dann Fenster auf — dann pausiert das Fenster selbst. Ob das Menü des
/// Originals überhaupt pausiert (<c>word[0x4FA280]</c>), ist nicht gelesen.</para>
/// </summary>
public partial class MapViewer
{
    private CanvasLayer? _speicherLayer;
    private UI.SpeicherfensterChrome? _speicherChrome, _ladeChrome;

    /// <summary><c>byte[0x4FAEEC]</c>: hat das Speicherfenster die Pause gesetzt?</summary>
    private bool _speicherPauseEigen;

    /// <summary><c>--missionsende-stand-aus</c> — kein »Ende von Mission #n« auf Platz 7.</summary>
    public static bool MissionsendeStandAus;

    /// <summary>Spielername im Kopf (+55). ⚠ SETZUNG: das Original nimmt den Profilnamen aus
    /// dem Startmenü (<c>0x87B141 + 40·byte[0x4FA284]</c>, Eingabe <c>0x44D096</c>) — ein
    /// Profil haben wir nicht, es steht »SPIELER« wie im Abschlussfenster.</summary>
    public const string SpielernameSetzung = "SPIELER";

    private bool SpeicherfensterNeu => !UI.SpeicherfensterChrome.Alt && UI.SpeicherfensterChrome.Usable;

    private UI.SpeicherfensterChrome SpeicherChromeHolen(int art)
    {
        if (_speicherLayer == null)
        {
            // über dem Lokator (94), unter nichts Wichtigem — wie die anderen Kachelfenster
            _speicherLayer = new CanvasLayer { Layer = 95, ProcessMode = ProcessModeEnum.Always };
            AddChild(_speicherLayer);
        }
        if (art == UI.SpeicherfensterChrome.ArtSpeichern)
        {
            if (_speicherChrome == null)
            {
                _speicherChrome = new UI.SpeicherfensterChrome(art) { Verwaltet = true };
                _speicherLayer.AddChild(_speicherChrome);
                // Bus 991 → Empfänger 0x4C4571 → Schreiber 0x41D210("game.00" + Platz)
                // Direktbefehle2: im Netz über den Ring, damit JEDE Maschine im selben Takt schreibt
                _speicherChrome.OnSichern = (p, n) =>
                {
                    if (MapEntityLayer.ImNetz && !MapEntityLayer.Alt2("spielstand")) { _entities.PostSpeichern(p, n); return; }
                    SpielstandSchreiben(p, n);
                };
                _speicherChrome.OnZu = () =>
                {
                    // 0x44730B: Art 15 && byte[0x4FAEEC] → noch einmal 993 → Pause aus
                    if (_speicherPauseEigen && MapEntityLayer.ImNetz && !MapEntityLayer.Alt2("pause"))
                    { if (_entities.NetzPause != 0) _entities.PostPause(); }
                    else if (_speicherPauseEigen) GetTree().Paused = false;
                    _speicherPauseEigen = false;
                };
            }
            return _speicherChrome;
        }
        if (_ladeChrome == null)
        {
            _ladeChrome = new UI.SpeicherfensterChrome(art) { Verwaltet = true };
            _speicherLayer.AddChild(_ladeChrome);
            // Bus 990 → 0x4C44E8 → Lader 0x41E070("game.00n")
            _ladeChrome.OnLaden = p =>
            {
                // Direktbefehle2: im Netz 990 über den Ring — geladen wird IN die laufende Partie
                if (MapEntityLayer.ImNetz && !MapEntityLayer.Alt2("spielstand")) { _entities.PostLaden(p); return; }
                UI.LoadGameScreen.Starten(this, Core.SaveGame.PlatzName(p));
            };
        }
        return _ladeChrome;
    }

    /// <summary><c>0x443AA0(MausX−3, MausY−3)</c> — »Spiel speichern« aufmachen.
    /// Gibt false zurück, wenn schon offen (Wache 0x443ADD) oder nicht baubar.</summary>
    public bool SpeicherfensterOeffnen()
    {
        if (!SpeicherfensterNeu) return false;
        if (UI.WindowManager.Offen(UI.SpeicherfensterChrome.ArtSpeichern) != null) return false;
        var c = SpeicherChromeHolen(UI.SpeicherfensterChrome.ArtSpeichern);
        c.Oeffnen();
        var f = UI.WindowManager.Oeffnen(UI.SpeicherfensterChrome.ArtSpeichern, c);
        UI.WindowManager.AnDieMaus(f);
        // ⚠⚠ Fensterwelt-Regel: die Aufblende VON HAND zu Ende drehen — gleich steht der Baum.
        for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
        // 0x443B26: schon pausiert → Merker 0; sonst 993 (Pause an), Merker 1
        if (MapEntityLayer.ImNetz && !MapEntityLayer.Alt2("pause"))
        {
            // 0x443B3F: nur wenn noch nicht pausiert → 993 (im Netz über den Ring)
            _speicherPauseEigen = _entities.NetzPause == 0;
            if (_speicherPauseEigen) _entities.PostPause();
        }
        else if (GetTree().Paused) _speicherPauseEigen = false;
        else { GetTree().Paused = true; _speicherPauseEigen = true; }
        return true;
    }

    /// <summary><c>0x443B90(MausX−3, MausY−3)</c> — »Spielstand laden« aufmachen. Keine Pause.</summary>
    public bool LadefensterOeffnen()
    {
        if (!SpeicherfensterNeu) return false;
        if (UI.WindowManager.Offen(UI.SpeicherfensterChrome.ArtLaden) != null) return false;
        var c = SpeicherChromeHolen(UI.SpeicherfensterChrome.ArtLaden);
        c.Oeffnen();
        var f = UI.WindowManager.Oeffnen(UI.SpeicherfensterChrome.ArtLaden, c);
        UI.WindowManager.AnDieMaus(f);
        // Der Baum steht hier nicht — trotzdem von Hand, damit ein Öffnen aus einem
        // angehaltenen Zustand (Hilfefenster, Pruefstand) nicht als Strich dasteht.
        for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
        return true;
    }

    /// <summary>Bus 991 / Empfänger <c>0x4C4571</c>: Name nach <c>0x542DC8</c>, Datei
    /// <c>"game.00" + Platz</c>. Leerer Name wird geschrieben wie im Original — der Platz
    /// zeigt danach »Leere Position...« und ist im Ladefenster nicht anwählbar.</summary>
    private string SpielstandSchreiben(int platz, string platzName, bool melden = true)
    {
        string name = Core.SaveGame.PlatzName(platz);
        // SETZUNG: im Gefecht Missionszahl 0 (word[0x539934] im Gefecht nicht gelesen)
        int mission = UI.SkirmishSetup.CampaignMission;
        var kopf = new Core.SaveGame.PlatzKopf(platz, platzName, mission, SpielernameSetzung);
        string label = $"{name} »{platzName}« — {MapNames[_mapIndex]} — {System.DateTime.Now:dd.MM.yyyy HH:mm}";
        string json = _entities.SaveStateJson(MapNames[_mapIndex], label, kopf);
        bool ok = Core.SaveGame.Write(name, json, out string err);
        string zeile = ok ? $"Spielstand gespeichert: Platz {platz} »{platzName}«"
                          : $"Speichern fehlgeschlagen: {err}";
        GD.Print(zeile);
        // ⚠ UNSERES (bug-509): das Original meldet nach dem Sichern NICHTS (§3.4). Die
        // Meldezeile bleibt, weil sie nicht stört — sie steht unten in der Statuszeile.
        if (melden) _entities.Say(zeile);
        return ok ? name : "";
    }

    /// <summary><c>0x4CFC10</c>: am Missionsende »Ende von Mission #n« nach <c>game.007</c>
    /// — VOR der Untermissionsprüfung, nicht im Netzspiel.
    /// ⚠ SETZUNG (V): nur bei GEWONNENER Mission; ob 0x4CFC10 auch bei einer Niederlage
    /// läuft, ist nicht gelesen. Gegenschalter <c>--missionsende-stand-aus</c>.</summary>
    private void MissionsendeStand(bool won, bool record)
    {
        if (!record || !won || MissionsendeStandAus || UI.SpeicherfensterChrome.Alt) return;
        SpielstandSchreiben(Core.SaveGame.PlatzAutosave,
                            $"Ende von Mission #{UI.SkirmishSetup.CampaignMission}", melden: false);
    }
}
