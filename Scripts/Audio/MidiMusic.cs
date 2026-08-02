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

    private const string Alias = "aer_music";

    /// <summary>Windows, and the content folder holds at least one .mid.</summary>
    public static bool Available =>
        OperatingSystem.IsWindows() && FileAccess.FileExists(Core.Content.Path("Sound/0.mid"));

    /// <summary>What is playing, or -1.</summary>
    public static int Track { get; private set; } = -1;

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
        return true;
    }

    /// <summary>MCI takes 0..1000. Not every sequencer honours it — the return
    /// code is kept but a refusal is not treated as a failure of the playback.
    /// </summary>
    public static void Volume(int percent)
    {
        if (!_open) return;
        int v = Math.Clamp(percent, 0, 100) * 10;
        Send($"setaudio {Alias} volume to {v}", quiet: true);
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
