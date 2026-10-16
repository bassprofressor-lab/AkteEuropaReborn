namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 16.10.2026, bug-524 — <b>DAS AUTOSICHERN DES ORIGINALS</b> (Entscheidung des Spielers:
/// »alles wie im Original, Autosave auch bauen«). Lesung: <c>berichte/speicherfenster-fable.md</c>
/// §3.5 und die Nachlesung <c>berichte/autosave-credits-demo-opus.md</c>.
///
/// <para><b>Die Routine</b> C <c>0x415480</c> / F <c>0x4152C1</c> (Hauptlauf, VOR
/// <c>Main_funct</c> 0x415CF0, also auch in der Pause):</para>
/// <code>
///   jetzt = timeGetTime()                      ; [0xC6587C] — WANDUHR in ms, keine Spielzeit
///   if byte[0x4F6F9C] == 0                     ; kein Demo (Menükulisse)
///      and byte[0x9927C0] != 0                 ; Einstellung in Minuten
///      and jetzt >= dword[0x87B058]:           ; Fälligkeit (BSS, beim Start 0)
///        dword[0x87B058] = jetzt + 60000·min   ; 0x4154C9..0x4154E6
///        Bus 991 (0x3DF), Platz 7, Name »Autosave« (0x4F743C) → 0x4C4571 → 0x41D210("game.007")
/// </code>
/// <para>Daraus, und jedes davon steht im Code unten:</para>
/// <list type="bullet">
/// <item><b>Wanduhr</b>, nicht Spieltakt: Geschwindigkeit und Pause ändern nichts, und
/// gespeichert wird auch IN der Pause (keine Pausenabfrage im Weg, der Bus führt 991 im
/// Einzelspiel sofort aus, <c>0x4C2190</c> → <c>0x404700</c>).</item>
/// <item>Die Fälligkeit ist <b>eine globale</b> und überlebt Missionswechsel und Laden (nur
/// zwei Schreiber, 0x4154E6 und 0x4473D1). Sie beginnt bei 0 — <b>der erste Autosave kommt
/// gleich im ersten Bild der ersten Mission</b>, danach alle n Minuten; ist sie beim
/// nächsten Missionsstart schon vorbei, sofort wieder.</item>
/// <item>Schliessen des Einstellungsfensters (Art 0x22 @0x447382): ist Autosichern an, wird
/// die Fälligkeit auf <c>min(alt, jetzt + n·60000)</c> gesetzt — nur vorgezogen.</item>
/// <item><b>Keine Meldung</b> (0x4C4571 schreibt und kehrt zurück) — bei uns nur die
/// Protokollzeile.</item>
/// <item>Kein Gefecht/Netz-Riegel: nur die Demo-Fahne. Im Netzspiel reiht der Sender
/// 0x4C2190 den Befehl in die Netzschlange (<c>0x4C21C8</c>), im Abspielmodus
/// (<c>byte[0x539260] == 2</c>) verwirft er ihn.</item>
/// </list>
/// <para>⚠ SETZUNGEN: (1) <b>Kopflos</b> sichert nur der Prüfstand — sonst schriebe jeder
/// Prüflauf bei eingeschaltetem Autosichern sofort einen GAME.007 in die echten Spielstände.
/// (2) Gefecht und unser Netzspiel sichern wie die Kampagne (die Menükulisse ist kein
/// MapViewer, sie sichert nie — das ist die Demo-Fahne). Gegenschalter
/// <c>--autosave-alt</c>: nie automatisch sichern (Stand 0.6.9).</para>
/// </summary>
public partial class MapViewer
{
    /// <summary><c>--autosave-alt</c>: nie automatisch sichern (Stand 0.6.9).</summary>
    public static bool AutosaveAlt;

    /// <summary><c>dword[0x87B058]</c> in ms auf <see cref="Time.GetTicksMsec"/> — statisch,
    /// weil global im Original; beginnt bei 0 (BSS).</summary>
    public static ulong AutosaveFaellig;

    /// <summary>NUR ZUM MESSEN, <c>--autosave-minuten-test=S</c>: eine »Minute« dauert S
    /// Sekunden (sonst 60).</summary>
    public static double AutosaveMinuteSek = 60.0;

    /// <summary>Wie viele Autosaves dieser Programmlauf geschrieben hat — für den Prüfstand.</summary>
    public static int AutosaveZahl;

    private Node? _autosaveUhr;

    private static ulong AutosaveSpanne(int minuten) => (ulong)(minuten * AutosaveMinuteSek * 1000.0);

    /// <summary>Aus <c>_Ready</c>: die Uhr als eigener Knoten mit <c>ProcessMode.Always</c>,
    /// denn der MapViewer selbst steht in der Pause still, die Routine des Originals nicht.</summary>
    private void AutosaveAnlegen()
    {
        if (_autosaveUhr != null || AutosaveAlt) return;
        _autosaveUhr = new AutosaveUhr { Viewer = this, Name = "AutosaveUhr", ProcessMode = ProcessModeEnum.Always };
        AddChild(_autosaveUhr);
    }

    /// <summary>Einmal je Bild, auch in der Pause — <c>0x415480</c>.</summary>
    public void AutosaveTakt()
    {
        if (AutosaveAlt || _entities == null) return;
        // SETZUNG (1): kopflos nur unter dem Prüfstand.
        if (DisplayServer.GetName() == "headless" && !AutosaveCheckAn) return;
        // ... und der Prüfstand nur in seinen eigenen Ordner, nie vor seinem Start.
        if (AutosaveCheckAn && Core.SaveGame.OrdnerPruefstand != AcOrdner) return;
        int min = UI.Settings.AutosaveMinuten;            // byte[0x9927C0]
        if (min == 0) return;
        ulong jetzt = Time.GetTicksMsec();                 // timeGetTime
        if (jetzt < AutosaveFaellig) return;               // jl 0x415516
        AutosaveFaellig = jetzt + AutosaveSpanne(min);     // 0x4154E6
        string datei = SpielstandSchreiben(Core.SaveGame.PlatzAutosave, "Autosave", melden: false);
        if (datei.Length > 0) AutosaveZahl++;
        GD.Print($"autosave: Platz {Core.SaveGame.PlatzAutosave} »Autosave« ({min} Min., " +
                 $"Pause {GetTree().Paused}, naechstes in {AutosaveSpanne(min) / 1000.0:0.#} s)");
    }

    /// <summary><c>0x447382..0x4473D1</c>: beim Schliessen des Einstellungsfensters die
    /// Fälligkeit höchstens vorziehen.</summary>
    public static void AutosaveEinstellungZu()
    {
        int min = UI.Settings.AutosaveMinuten;
        if (min == 0) return;
        ulong neu = Time.GetTicksMsec() + AutosaveSpanne(min);
        if (AutosaveFaellig > neu) AutosaveFaellig = neu;  // cmp eax,ecx; jle → bleibt
    }

    private partial class AutosaveUhr : Node
    {
        public MapViewer? Viewer;
        public override void _Process(double delta)
        {
            if (Viewer != null && IsInstanceValid(Viewer)) Viewer.AutosaveTakt();
        }
    }
}
