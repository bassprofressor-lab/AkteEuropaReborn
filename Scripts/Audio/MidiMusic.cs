namespace AkteEuropaReborn.Audio;

using System;
using System.Runtime.InteropServices;
using System.Text;
using Godot;

/// <summary>
/// The music: 0.MID .. 5.MID, played the way the original plays them.
///
/// <para><b>Why MCI and not a stream.</b> The game's own music is six MIDI files
/// and nothing else — no rendered audio anywhere on the discs. Godot cannot play
/// MIDI. The two ways out are to render them with a synthesiser and a sound font
/// (which means shipping or requiring a sound bank nobody chose) or to do what
/// the original does and hand the file to Windows. The original's own strings
/// settle it: <c>"open sequencer!%s alias playerSnd"</c> and
/// <c>"play playerSnd from %d notify"</c>, with the name built from
/// <c>"\%d.mid"</c> (@0x4f7918, used @0x416bfb). That is MCI, so this is MCI.</para>
///
/// <para>Windows only, and it says so instead of pretending: on anything else
/// every call returns quietly and <see cref="Available"/> is false.</para>
///
/// <para>The alias is ours (<c>aer_music</c>) — using the original's
/// <c>playerSnd</c> would collide with the real game if both ran at once.</para>
/// </summary>
public static class MidiMusic
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MciSendString(string command, StringBuilder? ret, int retLen, IntPtr hwnd);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    private static extern bool MciGetErrorString(int error, StringBuilder text, int len);

    /// <summary>Der zweite Weg zur Lautstärke, siehe <see cref="Volume"/>.
    /// <c>hmo</c> darf laut Doku auch eine Gerätenummer sein.</summary>
    [DllImport("winmm.dll", EntryPoint = "midiOutSetVolume")]
    private static extern int MidiOutSetVolume(IntPtr hmo, uint volume);

    private const string Alias = "aer_music";

    /// <summary>Windows, and the content folder holds at least one .mid.</summary>
    public static bool Available =>
        OperatingSystem.IsWindows() && FileAccess.FileExists(Core.Content.Path("Sound/0.mid"));

    /// <summary>What is playing, or -1.</summary>
    public static int Track { get; private set; } = -1;

    /// <summary>How many pieces the game ships: 0.MID .. 5.MID.</summary>
    public const int TrackCount = 6;

    /// <summary>
    /// Start the music for a mission, and keep it going.
    ///
    /// <para>⚠ WHICH piece belongs to WHICH mission is OURS. The original picks
    /// it by name: <c>play_music</c> @0x4D5490 takes a file name and opens it
    /// through MCI (<c>"open sequencer!%s alias playerSnd"</c>), and its caller
    /// @0x416C10 assembles that name from several parts — which parts was not
    /// read. Until it is, the mission number simply cycles through the six
    /// pieces, and piece 0 is left to the menu.</para>
    ///
    /// <para>The original plays a piece and stops (see <see cref="Play"/>), so
    /// this does the same; <see cref="Poll"/> starts the next one when the last
    /// has run out, which is our doing as well.</para>
    /// </summary>
    public static void StartForMission(int mission)
    {
        if (!Available || !UI.Settings.MusicOn) return;
        int t = TrackCount <= 1 ? 0 : 1 + Math.Abs(mission) % (TrackCount - 1);
        bool ok = Play(t);
        GD.Print(ok ? $"Musik: Stueck {t} laeuft (Mission {mission})"
                    : $"Musik: Stueck {t} startet nicht — {LastError} (MCI {LastCode})");
    }

    /// <summary>Ours: when a piece has run out, put the next one on. Call it
    /// from a tick; it costs one MCI status query and only when music is
    /// supposed to be running.</summary>
    public static void Poll()
    {
        if (!Available || !UI.Settings.MusicOn || Track < 0 || !_open) return;
        var sb = new StringBuilder(64);
        if (MciSendString($"status {Alias} mode", sb, sb.Capacity, IntPtr.Zero) != 0) return;
        if (sb.ToString().Trim() != "stopped") return;
        Play(TrackCount <= 1 ? 0 : 1 + (Track % (TrackCount - 1)));
    }

    /// <summary>The last MCI return code and its own message — kept so a failure
    /// can be reported with a number instead of a shrug.</summary>
    public static int LastCode { get; private set; }
    public static string LastError { get; private set; } = "";

    private static bool _open;

    /// <summary>Plays track N from the start, looping is not asked for: the
    /// original plays a piece and stops.</summary>
    public static bool Play(int track)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (!UI.Settings.MusicOn) return false;

        string res = Core.Content.Path($"Sound/{track}.mid");
        if (!FileAccess.FileExists(res)) { LastError = $"{res} fehlt"; return false; }
        string path = ProjectSettings.GlobalizePath(res);

        Stop();
        // quotes, because the path runs through the player's own folders
        if (!Send($"open \"{path}\" type sequencer alias {Alias}")) return false;
        _open = true;
        Volume(UI.Settings.MusicVolume);
        if (!Send($"play {Alias} from 0")) { Stop(); return false; }
        Track = track;
        _lastTrack = track;
        return true;
    }

    /// <summary>Welches Stück zuletzt lief. <see cref="Track"/> wird beim Stoppen
    /// auf -1 gesetzt, das hier bleibt stehen.</summary>
    private static int _lastTrack = -1;

    /// <summary>
    /// "MIDI-Musik an/aus" wieder auf AN: dasselbe Stück noch einmal auflegen.
    ///
    /// <para>UNSERE SETZUNG, und sie schließt eine Lücke. <see cref="Stop"/>
    /// schließt den Sequenzer; aufgemacht wird er sonst nur von
    /// <see cref="StartForMission"/>, also beim Missionsstart. Wer die Musik im
    /// laufenden Spiel ausschaltete und es sich anders überlegte, bekam bis zur
    /// nächsten Mission nichts mehr zu hören. Wo nichts lief, bevor gestoppt
    /// wurde, nehmen wir Stück 0 — das ist das Stück, das im Menü liegt.</para>
    /// </summary>
    public static bool Resume()
    {
        if (!Available || !UI.Settings.MusicOn) return false;
        return Play(_lastTrack >= 0 ? _lastTrack : 0);
    }

    /// <summary>Was <c>midiOutSetVolume</c> zuletzt zurückgab. 0 = angenommen.
    /// -1 heißt: gar nicht erst versucht (nicht Windows).</summary>
    public static int VolumeCode { get; private set; } = -1;

    /// <summary>Was <c>setaudio</c> zuletzt zurückgab, siehe unten.</summary>
    public static int MciVolumeCode { get; private set; } = -1;

    /// <summary>
    /// Die Musiklautstärke, 0..100.
    ///
    /// <para>⚠ <b>Gemessen am 10.08.2026, und der bisherige Weg wirkt nicht.</b>
    /// Die Zeile hier schickte nur <c>setaudio aer_music volume to N</c> (MCI
    /// nimmt 0..1000). Der Rückgabewert wurde still verworfen — und er lautet
    /// <b>261, "Unbekannter Befehl."</b>: der MCI-<i>sequencer</i> kennt
    /// <c>setaudio</c> nicht. Der Regler im Einstellungsschirm hat also nie
    /// etwas getan, und weil der Fehler mit <c>quiet: true</c> unterdrückt war,
    /// hat es auch nie jemand gesehen.</para>
    ///
    /// <para>Der zweite Weg ist der, den Windows für MIDI vorsieht:
    /// <c>midiOutSetVolume</c> mit der Gerätenummer 0 und links/rechts im
    /// unteren/oberen Wort. UNSERE SETZUNG — das Original von 1997 regelt die
    /// Musik über den Mixer der Soundkarte, den es so nicht mehr gibt. Der
    /// <c>setaudio</c>-Befehl bleibt trotzdem stehen: ein anderer Sequenzer
    /// (etwa ein installierter Software-Synthesizer) kann ihn beherrschen, und
    /// dann greift er zuerst.</para>
    ///
    /// <para>Beide Rückgabewerte werden jetzt behalten statt weggeworfen, damit
    /// ein Prüflauf sie zitieren kann.</para>
    ///
    /// <para>⚠ Was noch offen ist: <c>midiOutSetVolume</c> stellt bei manchen
    /// Treibern die Lautstärke des MIDI-Geräts geräteweit und über das Programm
    /// hinaus. Der Wert wird beim Beenden nicht zurückgesetzt. Gemessen ist hier
    /// nur, dass der Aufruf angenommen wird (Rückgabe 0) — dass die Musik danach
    /// tatsächlich leiser klingt, ist nicht gemessen und wird nicht behauptet.
    /// </para>
    /// </summary>
    public static void Volume(int percent)
    {
        int p = Math.Clamp(percent, 0, 100);
        if (_open)
        {
            Send($"setaudio {Alias} volume to {p * 10}", quiet: true);
            MciVolumeCode = LastCode;
        }
        if (!OperatingSystem.IsWindows()) { VolumeCode = -1; return; }
        // 0..100 -> 0..0xFFFF, gleiche Lautstärke links wie rechts
        uint v = (uint)(p * 0xFFFF / 100);
        VolumeCode = MidiOutSetVolume(IntPtr.Zero, v | (v << 16));
    }

    public static void Stop()
    {
        if (!OperatingSystem.IsWindows() || !_open) return;
        Send($"stop {Alias}", quiet: true);
        Send($"close {Alias}", quiet: true);
        _open = false;
        Track = -1;
    }

    private static bool Send(string command, bool quiet = false)
    {
        int rc = MciSendString(command, null, 0, IntPtr.Zero);
        LastCode = rc;
        if (rc == 0) { LastError = ""; return true; }
        var sb = new StringBuilder(256);
        LastError = MciGetErrorString(rc, sb, sb.Capacity) ? sb.ToString() : $"MCI-Fehler {rc}";
        if (!quiet) GD.PrintErr($"Musik: \"{command}\" — {LastError} ({rc})");
        return false;
    }
}
