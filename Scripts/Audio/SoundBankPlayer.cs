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
    public static void Play(int slot, float volumeDb = 0f, float pan = 0f)
    {
        if (!UI.Settings.SoundOn) return;
        var stream = Stream(slot);
        if (stream == null) return;
        var p = Free();
        if (p == null) return;
        p.Stream = stream;
        p.VolumeDb = volumeDb + UI.Settings.SfxVolumeDb;
        SetPan(p, pan);
        p.Play();
    }

    // ---- ein Klang hat einen ORT ---------------------------------------------
    //
    // Gelesen am 11.08.2026 aus `play_sound` @0x4047E0 (F: über die Form
    // gefunden, siehe unten). Die Routine bekommt nicht nur eine Klangnummer,
    // sondern eine STELLE AUF DER KARTE — eine Sprungtabelle @0x404A18 mit vier
    // Fällen holt sie entweder aus einem Gebäudeplatz (Bytes +0x00/+0x01 des
    // Satzes bei 0x6E26C8), aus einem Einheitenplatz (Worte +0x00/+0x02 bei
    // 0x591EF0), direkt als x/y, oder gar nicht. Dann rechnet sie:
    //
    //     dx = x - halbeBreite - kameraX        (0x5387C0 / 0x5387AC)
    //     dy = y - halbeHoehe  - kameraY        (0x5387C4 / 0x5387B0)
    //     daempfung = -(int)(sqrt(dx*dx + dy*dy) * 40.0)     @0x40495E..0x40496F
    //     panorama  = 200 * dx                              @0x404971..0x40497A
    //     beide geklammert auf [-10000, +10000]             @0x404981..0x4049A3
    //
    // -10000 und +10000 sind DirectSounds eigene Grenzen (DSBVOLUME_MIN,
    // DSBPAN_LEFT/RIGHT), die Lautstärke also in HUNDERTSTEL-Dezibel:
    // **0,4 dB je Zelle Abstand vom Bildmittelpunkt**, still erst bei 250.
    //
    // ⚠ Dass x/y ZELLEN sind und nicht Pixel, ist nicht angenommen, sondern
    // erzwungen: der Gebäudezweig @0x40484E holt sie als **Bytes** — auf einer
    // 254 × 254 grossen Karte (10.160 px breit) kann ein Byte keine Pixellage
    // halten. Also rechnet die ganze Routine in Zellen, und damit auch Kamera
    // und halbe Bildgrösse. Gegenprobe an den Karten: das Wort +0x00 eines
    // Einheitensatzes ist `row*256 + col` (map_01, 10 von 10) — die Zellnummer,
    // nicht eine Pixellage.
    //
    // ⚠ Was das NICHT erklärt: auf einer kleinen Karte wie map_01 (42 × 72) ist
    // der weiteste Punkt 38 Zellen weg, also nur 15 dB leiser. Wer dort von
    // Anfang an Schüsse hört, hört sie leiser — aber er hört sie. Die Frage,
    // WARUM ohne Zutun geschossen wird, ist eine andere und hier nicht
    // beantwortet.
    //
    // ⚠ Regel 8, sauber durchgehalten: die Konstante liegt in der C:-Fassung auf
    // 0x4F0058 und in der F:-Fassung auf 0x4EF058. Gefunden wurde sie über die
    // FORM (`fsqrt` unmittelbar vor `fmul qword ptr [imm32]`) — dieses Paar
    // kommt in jeder der beiden EXE **genau einmal** vor, und beide Male ist die
    // Konstante **40.0**.
    //
    // ⚠ Bis hierher spielte die Engine JEDEN Klang mit voller Lautstärke, egal
    // wo er entstand. Auf map_01 hört der Spieler damit vom ersten Augenblick an
    // ein Gefecht, das am anderen Ende der Karte stattfindet.
    //
    // ⚠ ~~UNGEBAUT und ausdrücklich als Lücke stehengelassen: das PANORAMA.
    // Godots `AudioStreamPlayer` kann nicht schwenken; dafür bräuchte es einen
    // eigenen Bus je Kanal mit `AudioEffectPanner`. Die Zahl ist gelesen
    // (200 je Zelle), gebaut ist sie nicht.~~
    // GEBAUT am 14.08.2026, und zwar genau so: ein Bus je Kanal mit
    // `AudioEffectPanner`, angelegt von `MakePanBus`. Siehe `PanOf`.

    /// <summary>Wo das Ohr steht: der Mittelpunkt des Bildes in Kartenzellen.
    /// NaN heißt »keine Karte offen« — dann wird nichts gedämpft, sonst wären
    /// Menü- und Briefingklänge still.</summary>
    public static Vector2 ListenerCell = new(float.NaN, float.NaN);

    /// <summary>Um wieviel ein Klang an dieser Zelle leiser ist, in Dezibel.
    /// Die Rechnung von @0x40495E, nur durch 100 geteilt, weil Godot in Dezibel
    /// rechnet und DirectSound in Hundertsteln.</summary>
    public static float DistanceDb(float col, float row)
    {
        if (float.IsNaN(ListenerCell.X) || float.IsNaN(ListenerCell.Y)) return 0f;
        float dx = col - ListenerCell.X, dy = row - ListenerCell.Y;
        int hundredths = (int)(Mathf.Sqrt(dx * dx + dy * dy) * DistanceFactor);
        if (hundredths > 10000) hundredths = 10000;     // DSBVOLUME_MIN
        return -hundredths / 100f;
    }

    /// <summary>Die 40.0 von 0x4F0058 (F: 0x4EF058), in beiden Fassungen gleich.</summary>
    public const float DistanceFactor = 40f;

    /// <summary>
    /// DAS PANORAMA — <c>panorama = 200 · dx</c> @0x404971, geklammert auf
    /// [−10000, +10000].
    ///
    /// <para>Das sind DirectSounds eigene Grenzen (<c>DSBPAN_LEFT</c> /
    /// <c>DSBPAN_RIGHT</c>), und sie sind eine Hundertstel-Dezibel-Skala wie die
    /// Dämpfung daneben. Godot rechnet in −1..+1, also durch 10000 geteilt.
    /// Ausgereizt ist der Regler damit bei <b>50 Zellen</b> seitlichem Abstand:
    /// darüber hinaus bleibt er stehen, wie im Original.</para>
    ///
    /// <para>⚠ Nur <c>dx</c>. Ein Klang, der genau über oder unter dem Ohr
    /// entsteht, kommt aus der Mitte, so weit weg er auch sei — das Original
    /// fragt <c>dy</c> für das Panorama gar nicht ab, <c>dy</c> geht allein in
    /// die Entfernung. Wer hier eine Winkelrechnung einsetzte, machte es
    /// »richtiger« und damit falsch.</para>
    /// </summary>
    public static float PanOf(float col)
    {
        if (float.IsNaN(ListenerCell.X)) return 0f;
        int hundredths = (int)((col - ListenerCell.X) * PanFactor);
        if (hundredths > 10000) hundredths = 10000;      // DSBPAN_RIGHT
        if (hundredths < -10000) hundredths = -10000;    // DSBPAN_LEFT
        return hundredths / 10000f;
    }

    /// <summary>Die 200 von @0x404971 — Panoramaeinheiten je Kartenzelle.</summary>
    public const float PanFactor = 200f;

    /// <summary>Ein Klang, der irgendwo auf der Karte entsteht.</summary>
    public static void PlayAt(int slot, float col, float row, float volumeDb = 0f)
    {
        float d = DistanceDb(col, row);
        if (d <= -100f) return;      // -10000 ist die Stille selbst
        Play(slot, volumeDb + d, PanOf(col));
    }

    /// <summary>
    /// DEN SCHWENKREGLER DIESES KANALS SETZEN — und warum jeder Kanal seinen
    /// eigenen Bus braucht.
    ///
    /// <para>Ein <c>AudioStreamPlayer</c> kann nicht schwenken; schwenken kann
    /// nur ein <c>AudioEffectPanner</c>, und der sitzt an einem BUS. Teilten
    /// sich zwei gleichzeitig spielende Klänge einen Bus, bekäme der ältere den
    /// Schwenk des jüngeren — ein Schuss am linken Kartenrand wanderte nach
    /// rechts, weil daneben jemand anderes geschossen hat. Darum ein Bus je
    /// Kanal, <see cref="Voices"/> Stück, angelegt wenn der Kanal entsteht.</para>
    ///
    /// <para>⚠ Fehlt der Bus (Godot legt keinen an, wenn der Klangtreiber der
    /// Blindtreiber ist), bleibt der Kanal auf »Master« und spielt ohne
    /// Schwenk. Ein stummer Kanal wäre der schlechtere Tausch.</para>
    /// </summary>
    private static void SetPan(AudioStreamPlayer p, float pan)
    {
        if (!_panBus.TryGetValue(p, out int bus)) return;
        if (AudioServer.GetBusEffect(bus, 0) is AudioEffectPanner panner)
            panner.Pan = Mathf.Clamp(pan, -1f, 1f);
    }

    private static readonly Dictionary<AudioStreamPlayer, int> _panBus = new();

    /// <summary>
    /// Wieviele der <see cref="Voices"/> Busse wirklich einen Schwenkregler
    /// tragen — am <c>AudioServer</c> nachgeschlagen, nicht aus einer eigenen
    /// Liste geglaubt.
    ///
    /// <para>⚠ <b>Gezählt werden BUSSE, nicht Kanäle</b>, und das ist der zweite
    /// Anlauf. Der erste ließ den Prüfstand die zwölf Kanäle anlegen, um sie
    /// zählen zu können — und erzeugte damit 40 Fehlerzeilen »Playback can only
    /// happen when a node is inside the scene tree«, weil ein zum Zählen
    /// angelegter Kanal noch nicht im Baum hing. Die Gegenprobe war eindeutig:
    /// derselbe Lauf ohne <c>--sound-check</c> hatte 0 solche Zeilen. Ein
    /// Prüfstand, der seinen Gegenstand kaputtmacht, um ihn zu messen, taugt
    /// nicht — die Busse hängen ohnehin nicht am Kanal, also werden sie auch
    /// ohne einen angelegt.</para>
    /// </summary>
    public static int PanBusCount()
    {
        EnsurePanBuses();
        int n = 0;
        for (int i = 0; i < Voices; i++)
        {
            int idx = AudioServer.GetBusIndex($"SndPan{i}");
            if (idx >= 0 && AudioServer.GetBusEffect(idx, 0) is AudioEffectPanner) n++;
        }
        return n;
    }

    /// <summary>Legt die <see cref="Voices"/> Busse samt Schwenkregler an, ohne
    /// einen einzigen Kanal zu erzeugen. Mehrfach aufrufbar: vorhandene Busse
    /// werden am Namen wiedererkannt.</summary>
    public static void EnsurePanBuses()
    {
        for (int i = 0; i < Voices; i++) MakePanBus(null, i);
    }

    /// <summary>Legt Bus und Schwenkregler für einen neuen Kanal an und gibt den
    /// Busnamen zurück — oder <c>"Master"</c>, wenn es nicht geht.</summary>
    private static string MakePanBus(AudioStreamPlayer? p, int nr)
    {
        string name = $"SndPan{nr}";
        int idx = AudioServer.GetBusIndex(name);
        if (idx < 0)
        {
            idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, name);
            AudioServer.SetBusSend(idx, "Master");
            AudioServer.AddBusEffect(idx, new AudioEffectPanner(), 0);
            idx = AudioServer.GetBusIndex(name);
        }
        if (idx < 0) return "Master";
        if (p != null) _panBus[p] = idx;
        return name;
    }

    /// <summary>
    /// Prüfstand: spielt einen erzeugten Ton über GENAU denselben Kanalpool wie
    /// jeder Spielklang und gibt den Kanal zurück, damit ein Test messen kann,
    /// was mit ihm geschieht — vor allem, ob er bei angehaltenem Baum überhaupt
    /// weiterläuft. Kein Spielweg ruft das auf; der erzeugte Ton ist nötig, weil
    /// ein Entwicklungsbaum ohne importierten Inhalt keinen einzigen Klang hat.
    /// </summary>
    public static AudioStreamPlayer? Probe(float volumeDb = -40f)
    {
        var p = Free();
        if (p == null) return null;
        // Fünf Sekunden, nicht eine: die MCI-Aufrufe im selben Prüflauf brauchen
        // Zeit, und ein Ton, der inzwischen ausgelaufen ist, sieht wie ein
        // eingefrorener Ton aus. Erst dieser Unterschied macht die Messung
        // "läuft der Klang bei angehaltenem Baum weiter?" überhaupt aussagekräftig.
        int rate = Import.SoundBank.SampleRate;
        var pcm = new byte[rate * 5];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (byte)(sbyte)(Math.Sin(i * 2.0 * Math.PI * 440.0 / rate) * 100);
        p.Stream = new AudioStreamWav
        {
            Data = pcm,
            Format = AudioStreamWav.FormatEnum.Format8Bits,
            MixRate = rate,
            Stereo = false,
        };
        p.VolumeDb = volumeDb + UI.Settings.SfxVolumeDb;   // wie in Play()
        p.Play();
        return p;
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
        if (!EnsureHost()) return null;
        foreach (var p in _pool) if (!p.Playing) return p;
        return NewVoice();
    }

    private static bool EnsureHost()
    {
        var root = ((SceneTree?)Engine.GetMainLoop())?.Root;
        if (root == null) return false;
        if (_host == null || !GodotObject.IsInstanceValid(_host))
        {
            _host = new Node { Name = "SoundBank" };
            root.AddChild(_host);
            _pool.Clear();
        }
        return true;
    }

    /// <summary>Legt EINEN neuen Kanal an, oder null wenn der Pool voll ist.
    /// Getrennt von <see cref="Free"/>, weil »gib mir einen freien« und »lege
    /// einen an« zwei verschiedene Fragen sind — sie zu vermengen war der
    /// Fehler, den der Prüfstand mit »1 von 12« gefunden hat.</summary>
    private static AudioStreamPlayer? NewVoice()
    {
        if (!EnsureHost() || _pool.Count >= Voices) return null;
        var np = new AudioStreamPlayer();
        np.Bus = MakePanBus(np, _pool.Count);
        _host!.AddChild(np);
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
        // ⚠ Die Busse bleiben STEHEN. Sie heissen SndPan0..N und werden von
        // MakePanBus an ihrem Namen wiedererkannt; ein Aufraeumen waere hier
        // eine Falle, weil die Busnummern sich beim Entfernen verschieben und
        // ein noch spielender Kanal dann auf einen fremden Bus zeigte.
        _panBus.Clear();
        if (_host != null && GodotObject.IsInstanceValid(_host)) _host.QueueFree();
        _host = null;
        _loaded = false;
        Ready = false;
    }
}
