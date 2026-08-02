namespace AkteEuropaReborn.Audio;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

/// <summary>
/// Playing the sounds the importer derived from SOUNDS.CWN.
///
/// The bank's own split is kept: the 202 entries the original preloads into
/// DirectSound buffers (a positive length in the directory, see
/// <see cref="Import.SoundBank"/>) are held in memory once they have been asked
/// for; the 290 it reads on demand — the speech, 57 minutes of it — are loaded
/// when played and dropped again. Holding all 492 would be 80 MB of PCM for
/// three and a half minutes of it that ever repeats.
///
/// <para><b>One conversion is ours</b> and cannot be avoided: the bank is 8-bit
/// UNSIGNED, which is what a .wav file stores, but Godot's
/// <c>AudioStreamWav.Format.Format8Bits</c> is SIGNED. So every byte is flipped
/// by 0x80 when the stream is built. Nothing else is touched — no resampling,
/// no normalising, no filtering.</para>
///
/// <para>The voices are the game's, so the channel count is the game's problem
/// too: a small pool of players, and a new sound takes the oldest free one
/// rather than stacking without limit.</para>
/// </summary>
public static class SoundBankPlayer
{
    /// <summary>One entry of Sound/sounds.json.</summary>
    public sealed class Entry
    {
        public int Index;
        public string File = "";
        public int Bytes;
        public double Seconds;
        public bool Preloaded;
    }

    private static readonly Dictionary<int, Entry> _index = new();
    private static readonly Dictionary<int, AudioStreamWav> _kept = new();
    private static Node? _host;
    private static readonly List<AudioStreamPlayer> _pool = new();
    private static bool _loaded;

    /// <summary>How many players may sound at once. Ours — the original has its
    /// own channel table (0x833a16, 10 x 200) which has not been read.</summary>
    public const int Voices = 12;

    /// <summary>True once sounds.json was found and read.</summary>
    public static bool Ready { get; private set; }

    /// <summary>Every sound the importer wrote, by its slot number.</summary>
    public static IReadOnlyDictionary<int, Entry> Index { get { Load(); return _index; } }

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        string path = Core.Content.Path("Sound/sounds.json");
        if (!FileAccess.FileExists(path)) return;
        try
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            using var doc = JsonDocument.Parse(f.GetAsText());
            foreach (var e in doc.RootElement.GetProperty("sounds").EnumerateArray())
            {
                var s = new Entry
                {
                    Index = e.GetProperty("index").GetInt32(),
                    File = e.GetProperty("file").GetString() ?? "",
                    Bytes = e.GetProperty("bytes").GetInt32(),
                    Seconds = e.GetProperty("seconds").GetDouble(),
                    Preloaded = e.GetProperty("preloaded").GetBoolean(),
                };
                _index[s.Index] = s;
            }
            Ready = _index.Count > 0;
        }
        catch (Exception e) { GD.PrintErr("Ton: sounds.json — " + e.Message); }
    }

    /// <summary>The stream for one slot, or null if the slot is empty or the
    /// file is missing. Preloaded entries stay in memory afterwards.</summary>
    public static AudioStreamWav? Stream(int slot)
    {
        Load();
        if (_kept.TryGetValue(slot, out var have)) return have;
        if (!_index.TryGetValue(slot, out var e)) return null;

        string path = Core.Content.Path("Sound/" + e.File);
        if (!FileAccess.FileExists(path)) return null;
        byte[] wav = FileAccess.GetFileAsBytes(path);
        if (wav.Length <= 44) return null;

        // the 44-byte header this importer writes is canonical, so the payload
        // starts at 44; unsigned -> signed is the one conversion we make
        var pcm = new byte[wav.Length - 44];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (byte)(wav[44 + i] ^ 0x80);

        var s = new AudioStreamWav
        {
            Data = pcm,
            Format = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = Import.SoundBank.SampleRate,
            Stereo = false,
        };
        if (e.Preloaded) _kept[slot] = s;
        return s;
    }

    /// <summary>Plays a slot. Silently does nothing when the slot is empty, the
    /// content has not been imported, or the volume is at zero — a missing sound
    /// must never be able to stop the game.</summary>
    public static void Play(int slot, float volumeDb = 0f)
    {
        if (!UI.Settings.SoundOn) return;
        var stream = Stream(slot);
        if (stream == null) return;
        var p = Free();
        if (p == null) return;
        p.Stream = stream;
        p.VolumeDb = volumeDb + UI.Settings.SfxVolumeDb;
        p.Play();
    }

    private static AudioStreamPlayer? _voice;

    /// <summary>The spoken word — a briefing runs 15 to 42 seconds, so it gets a
    /// player of its own that can be stopped, instead of a slot in the pool that
    /// the next explosion would take.</summary>
    public static bool PlayVoice(int slot, float volumeDb = 0f)
    {
        StopVoice();
        if (!UI.Settings.SoundOn) return false;
        var stream = Stream(slot);
        if (stream == null) return false;
        var tree = (SceneTree?)Engine.GetMainLoop();
        if (tree?.Root == null) return false;
        _voice = new AudioStreamPlayer { Bus = "Master", Stream = stream };
        tree.Root.AddChild(_voice);
        _voice.VolumeDb = volumeDb + UI.Settings.SfxVolumeDb;
        _voice.Play();
        return true;
    }

    public static void StopVoice()
    {
        if (_voice != null && GodotObject.IsInstanceValid(_voice))
        {
            _voice.Stop();
            _voice.QueueFree();
        }
        _voice = null;
    }

    private static AudioStreamPlayer? Free()
    {
        var tree = (SceneTree?)Engine.GetMainLoop();
        var root = tree?.Root;
        if (root == null) return null;

        if (_host == null || !GodotObject.IsInstanceValid(_host))
        {
            _host = new Node { Name = "SoundBank" };
            root.AddChild(_host);
            _pool.Clear();
        }
        foreach (var p in _pool) if (!p.Playing) return p;
        if (_pool.Count >= Voices) return null;
        var np = new AudioStreamPlayer { Bus = "Master" };
        _host.AddChild(np);
        _pool.Add(np);
        return np;
    }

    /// <summary>Drops the cached streams and the pool — used when the content
    /// folder changes under a running game (the import screen does that).</summary>
    public static void Forget()
    {
        _kept.Clear();
        _index.Clear();
        _pool.Clear();
        if (_host != null && GodotObject.IsInstanceValid(_host)) _host.QueueFree();
        _host = null;
        _loaded = false;
        Ready = false;
    }
}
