namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// The player's own settings, kept in `user://settings.cfg` next to the campaign
/// progress and the saved designs — same mechanism (Godot's ConfigFile), same
/// place, so everything the player owns lives together.
///
/// <para><b>⚠ Corrected 02.08.2026: the original DID have an options screen.</b>
/// The note here used to say it had none worth recovering. The help lines the
/// game shows under a hovered control say otherwise — they sit in one table
/// (base 0x4f0280, stride 75) and name eight switches:
/// <i>Hilfe-Texte ein-/ausschalten · Sprachausgabe der Hilfe-Fenster · Sprach-
/// ausgabe der Einheiten · MIDI-Musik an/aus · Automatische Sicherung ·
/// Bildschirm-Scrollen aktiv (mit RECHTER MAUSTASTE) oder passiv · Spiel
/// anhalten waehrend eines Hilfe-Fensters · Online-Hilfe</i>, plus
/// "Einstellbare Optionen zeigen" in the start menu. <c>OPTIONS.CFG</c> beside
/// the exe is 24 bytes and carries eight 0/1 bytes at +0x0c, which is the same
/// count. So the right-button panning below is NOT ours after all — the
/// original offers exactly that choice.</para>
///
/// <para>What is still ours: window, synchronisation, frame limit, pointer
/// hints, keyboard pan speed, and the volumes. The switches the original names
/// and we cannot yet honour are not shown as sliders that do nothing.</para>
/// </summary>
public static class Settings
{
    public const string SavePath = "user://settings.cfg";

    public static bool Fullscreen { get => B("fullscreen", false); set => Set("fullscreen", value); }
    public static bool VSync { get => B("vsync", true); set => Set("vsync", value); }
    /// <summary>0 = no limit.</summary>
    public static int FpsLimit { get => I("fps_limit", 0); set => Set("fps_limit", value); }
    /// <summary>Pointer changes shape over friend and foe.</summary>
    public static bool CursorHints { get => B("cursor_hints", true); set => Set("cursor_hints", value); }
    /// <summary>Holding the right button drags the map.</summary>
    public static bool RightDragPan { get => B("right_drag_pan", true); set => Set("right_drag_pan", value); }
    /// <summary>Keyboard panning speed in map pixels per second at zoom 1.</summary>
    public static int PanSpeed { get => I("pan_speed", 900); set => Set("pan_speed", value); }

    /// <summary>Nebel des Krieges. The original has the switch too — its
    /// exploration step @0x4205b0 checks <c>byte[0x4f8a3c]</c> and, when that is
    /// clear, marks everything seen instead of stamping. Default on, as the
    /// game ships it.</summary>
    public static bool FogOfWar { get => !FogSuppressed && B("fog", true); set => Set("fog", value); }

    /// <summary>Der GESPEICHERTE Wert, ungeachtet der Unterdrückung — für den
    /// Einstellungsschirm, der zeigen muss, was der Spieler eingestellt hat,
    /// und nicht, was gerade auf dem Bildschirm passiert.</summary>
    public static bool FogOfWarSetting => B("fog", true);

    /// <summary>Solange gesetzt, liefert <see cref="FogOfWar"/> false, ohne die
    /// Einstellung zu ändern. Gebraucht von <see cref="MenuBackdrop"/>: das
    /// Demo im Menü ist ein geladener Spielstand ohne Erkundungsstand, und mit
    /// Nebel wäre die Kulisse schwarz. Ausserhalb des Menüs steht das Feld auf
    /// false, also entscheidet dort weiter allein die Einstellung.</summary>
    public static bool FogSuppressed;

    // ---- sound --------------------------------------------------------------

    /// <summary>Effects at all. Kept apart from the volume so silence is a
    /// decision and not a slider at the bottom of its travel.</summary>
    public static bool SoundOn { get => B("sound", true); set => Set("sound", value); }

    /// <summary>0..100.</summary>
    public static int SfxVolume { get => I("sfx_volume", 80); set => Set("sfx_volume", value); }

    /// <summary>"MIDI-Musik an/aus" — the original's own wording for it.</summary>
    public static bool MusicOn { get => B("music", true); set => Set("music", value); }

    /// <summary>0..100. MCI takes a whole-number volume, so this is not dB.</summary>
    public static int MusicVolume { get => I("music_volume", 70); set => Set("music_volume", value); }

    /// <summary>"Meldungen" — the original's own name for it, and the switch
    /// behind the sound routine's guarded band: numbers <b>150..253</b> only
    /// play when <c>byte[0x991708]</c> is set (@0x4047fa). All 104 of them are
    /// preloaded, and the bank's directory delimits exactly that block with
    /// holes on either side. We have the samples; which announcement is which is
    /// not read yet.</summary>
    public static bool Announcements { get => B("announce", true); set => Set("announce", value); }

    /// <summary>"Hilfe-Sprache" — <c>byte[0x8934c4]</c>, checked @0x44330c right
    /// before the call that plays 1000 + the help text's number.
    ///
    /// <para>This is the flag that settled the options screen's reading order.
    /// The screen loads a flag and then pushes the two captions it chooses
    /// between, so the flag comes BEFORE its labels — which puts 0x8934c4 on
    /// "Hilfe-Sprache". The other reading would put it on "Hilfe-Fenster", and
    /// that cannot be: a flag that gates a spoken sound is not the switch for
    /// whether help windows appear. A label next to a field is a hint; a value
    /// that gates a known behaviour is a proof.</para></summary>
    public static bool HelpVoice { get => B("help_voice", false); set => Set("help_voice", value); }

    /// <summary>The effect volume as Godot wants it. 0 becomes silence rather
    /// than -inf arithmetic.</summary>
    public static float SfxVolumeDb =>
        SfxVolume <= 0 ? -80f : (float)(20.0 * System.Math.Log10(SfxVolume / 100.0));

    /// <summary>Hands the display settings to the engine. Called once when a
    /// scene comes up and again whenever the screen changes something, so a
    /// change takes effect immediately instead of at the next start.</summary>
    public static void Apply()
    {
        DisplayServer.WindowSetMode(Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = FpsLimit;
    }

    // ---- the file ---------------------------------------------------------

    private static ConfigFile Load()
    {
        var c = new ConfigFile();
        c.Load(SavePath);
        return c;
    }

    private static bool B(string k, bool d) => (bool)Load().GetValue("options", k, d);
    private static int I(string k, int d) => (int)Load().GetValue("options", k, d);

    private static void Set(string k, Variant v)
    {
        var c = Load();
        c.SetValue("options", k, v);
        c.Save(SavePath);
    }
}
