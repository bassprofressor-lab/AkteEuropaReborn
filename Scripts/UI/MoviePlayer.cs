namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// <b>DAS KINO</b> — die Filme des Originals im Remake, 20.08.2026.
///
/// <para>Vor jeder Kampagnenmission läuft ihr Film: <b>Filmnummer =
/// Missionsnummer</b>, gelesen aus dem Kartenwechsel des Originals
/// (<c>cmp …,0x10</c> @0x4CFA98 — ab 16 liegt er auf der zweiten Scheibe).
/// Dazu <c>INTRO.RPL</c> und <c>34.RPL</c> als Abspann.</para>
///
/// <para><b>Entscheidungen des Spielers</b>, nicht unsere:</para>
/// <list type="bullet">
///   <item>Der Film läuft <b>vor dem Briefing</b>.</item>
///   <item><b>ESC überspringt.</b></item>
/// </list>
///
/// <para><b>⚠ Wir liefern keinen einzigen Film aus.</b> 853 MB bleiben beim
/// Spieler — auf seinen CDs unter <c>MOVIES\</c> und, wenn er das Original
/// installiert hat, zusätzlich in dessen <c>Movies\</c>-Ordner (dort 17 Stück,
/// die übrigen nur auf Scheibe 2). Fehlt der Film, geht es <b>stillschweigend
/// weiter</b> — ein Spieler ohne eingelegte Scheibe soll nicht vor einer
/// Fehlermeldung stehen, sondern spielen können. Der Protokollauszug sagt
/// trotzdem, dass und warum übersprungen wurde.</para>
///
/// <para><b>⚠ Ein Film ist ein DIFFERENZFORMAT</b> (<see cref="Escape124"/>):
/// jedes Bild baut auf dem vorigen auf und auf Codebüchern, die über Bilder
/// hinweg stehenbleiben. Es lässt sich deshalb <b>nicht springen</b> —
/// vorwärts überspringen ist billig, an eine Stelle springen hiesse, von vorn
/// zu dekodieren. Für uns spielt das keine Rolle: wir laufen ohnehin von vorn.
/// </para>
///
/// <para><b>Der Ton läuft</b>, und er ist die Uhr: das Bild hängt an der
/// Abspielstelle des Tons, nicht an der Bildrate der Anzeige. Damit kann der
/// Film weder wegdriften noch stottern, wenn ein Einzelbild einmal länger
/// braucht. ⚠ Nur wenn ein Film gar keinen Ton trägt, zählt eine eigene Uhr.
/// </para>
///
/// <para>⚠ Der Ton wird <b>im Ganzen</b> dekodiert, bevor der Film anläuft
/// (rund 15 MB für 175 Sekunden). Das ist kein Geiz an der falschen Stelle,
/// sondern nötig: ein Tonstück des Originals hat <b>keinen eigenen Kopf</b>,
/// Vorhersage und Schrittweite laufen über die Stückgrenzen weiter — man kann
/// nicht mittendrin einsteigen.</para>
///
/// <para><b>⚠ Die Bildrate steht in der Datei und ist nicht überall gleich:</b>
/// <c>INTRO.RPL</c> läuft mit <b>20</b>, die übrigen mit <b>25</b>. Wer 25 fest
/// verdrahtet, spielt den Vorspann um ein Fünftel zu schnell.</para>
/// </summary>
public partial class MoviePlayer : CanvasLayer
{
    /// <summary>Wo die Filme liegen dürfen — die zwei Scheiben und eine
    /// Installation des Originals. Die Reihenfolge ist die der Kosten: was auf
    /// der Platte liegt, wird der CD vorgezogen.</summary>
    public static readonly string[] Places =
    {
        @"C:\Program Files (x86)\Akte Europa\Movies",
        @"D:\MOVIES", @"E:\MOVIES",
    };

    /// <summary>Schalter für Prüfläufe: mit <c>true</c> wird kein Film
    /// gespielt. ⚠ Ohne ihn hinge jeder kopflose Lauf minutenlang im Kino.
    /// </summary>
    public static bool Disabled { get; set; }

    private readonly RplFile _film;
    private readonly Escape124 _dec;
    private readonly Action _weiter;
    private readonly TextureRect _bild;
    private readonly ImageTexture _tex;
    private readonly Image _img;
    private readonly byte[] _rgb8;
    private int _naechstes;
    private double _uhr;
    private readonly double _proBild;
    private bool _fertig;
    private readonly AudioStreamPlayer? _ton;

    /// <summary>Lief die Menuemusik, als der Film anfing? Dann geht sie
    /// danach weiter. Siehe <see cref="MusikAnhalten"/>.</summary>
    private readonly bool _musikLief;

    /// <summary>
    /// Den Film einer Mission suchen. <c>null</c>, wenn er nirgends liegt —
    /// dann wird stillschweigend übersprungen.
    /// </summary>
    public static string? Find(int nummer)
    {
        string name = nummer <= 0 ? "INTRO.RPL" : $"{nummer}.RPL";
        foreach (string d in Places)
        {
            string p = System.IO.Path.Combine(d, name);
            if (System.IO.File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>
    /// <b>Den Film einer Mission spielen, dann <paramref name="weiter"/>.</b>
    ///
    /// <para>Gibt <c>false</c>, wenn nicht gespielt wird (kein Film, oder
    /// <see cref="Disabled"/>) — der Aufrufer macht dann sofort weiter und
    /// braucht keinen zweiten Weg.</para>
    /// </summary>
    public static bool Play(Node eltern, int nummer, Action weiter)
    {
        if (Disabled) return false;
        string? pfad = Find(nummer);
        if (pfad == null)
        {
            GD.Print($"Film {nummer}: nicht gefunden — uebersprungen. Gesucht in " +
                     string.Join(", ", Places));
            return false;
        }
        try
        {
            var mp = new MoviePlayer(pfad, weiter);
            eltern.AddChild(mp);
            return true;
        }
        catch (Exception e)
        {
            // ⚠ Ein kaputter Film darf die Mission nicht aufhalten.
            GD.PrintErr($"Film {nummer}: {e.Message} — uebersprungen");
            return false;
        }
    }

    private MoviePlayer(string pfad, Action weiter)
    {
        _film = new RplFile(pfad);
        _weiter = weiter;
        _dec = new Escape124(_film.Width, _film.Height);
        _proBild = _film.Fps > 1 ? 1.0 / _film.Fps : 1.0 / 25.0;
        _rgb8 = new byte[_film.Width * _film.Height * 3];
        _img = Image.CreateEmpty(_film.Width, _film.Height, false, Image.Format.Rgb8);
        _tex = ImageTexture.CreateFromImage(_img);

        Layer = 90;
        var hg = new ColorRect { Color = Colors.Black };
        hg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(hg);
        _bild = new TextureRect
        {
            Texture = _tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _bild.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_bild);

        _musikLief = MusikAnhalten();
        _ton = TonAnlegen();
        GD.Print($"Film: {System.IO.Path.GetFileName(pfad)}, {_film.Chunks.Count} Bilder, " +
                 $"{_film.Width}x{_film.Height}, {_film.Fps:0.##} B/s " +
                 $"({_film.Chunks.Count / Math.Max(1.0, _film.Fps):0} s), " +
                 $"Ton {(_ton == null ? "keiner" : $"{_film.SoundRate} Hz {_film.SoundChannels}-kanalig")}" +
                 " — ESC ueberspringt");
        _ton?.Play();
        SetProcess(true);
        SetProcessInput(true);
    }

    public override void _Input(InputEvent e)
    {
        if (_fertig) return;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GD.Print($"Film: mit ESC uebersprungen bei Bild {_naechstes} von {_film.Chunks.Count}");
            Ende();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// <b>Die Menuemusik anhalten, solange der Film laeuft.</b>
    ///
    /// <para>⚠ 20.08.2026, gemeldet: »videos laufen, nur hoert man die Musik
    /// vermutlich aus dem Hauptmenu?«. Genau so war es. Ein Film bringt seine
    /// eigene Tonspur mit — zwei Stuecke uebereinander sind keine Untermalung,
    /// sondern ein Fehler.</para>
    ///
    /// <para>⚠ <b>Warum das nicht schon <c>StopBackdrop()</c> erledigt hat:</b>
    /// die Menuemusik ist in Wahrheit die MISSIONSMUSIK der Kulisse — die
    /// Kulisse spielt einen echten Spielstand, und der ruft
    /// <see cref="Audio.MidiMusic.StartForMission"/>. Die Kulisse wird vor dem
    /// Briefing abgeraeumt, die Musik nicht: sie laeuft ueber MCI im PROZESS,
    /// nicht am Knoten. Ein weggeraeumter Knoten nimmt sie darum nicht mit.
    /// </para>
    ///
    /// <para>Nach dem Film geht dasselbe Stueck weiter
    /// (<see cref="Audio.MidiMusic.Resume"/>), damit das Briefing klingt wie
    /// vorher — hier wird nur der Film freigeraeumt, sonst nichts.</para>
    /// </summary>
    private static bool MusikAnhalten()
    {
        if (Audio.MidiMusic.Track < 0) return false;
        Audio.MidiMusic.Stop();
        GD.Print("Film: Menuemusik angehalten");
        return true;
    }

    /// <summary>Den ganzen Ton des Films dekodieren und einen Spieler dafür
    /// anlegen. <c>null</c>, wenn der Film keinen trägt.
    ///
    /// <para>⚠ <b>Im Ganzen</b>, siehe Klassenkommentar: ein Tonstück hat
    /// keinen eigenen Kopf. <see cref="RplAudio"/> ist gegen ffmpeg geprüft —
    /// 35 Filme, 187.107.038 Abtastungen, byteweise genau.</para></summary>
    private AudioStreamPlayer? TonAnlegen()
    {
        short[] pcm;
        try { pcm = RplAudio.DecodeAll(_film); }
        catch (Exception e) { GD.PrintErr($"Film: Ton nicht lesbar ({e.Message}) — stumm"); return null; }
        if (pcm.Length == 0) return null;
        var roh = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, roh, 0, roh.Length);
        var w = new AudioStreamWav
        {
            Data = roh,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            Stereo = _film.SoundChannels >= 2,
            MixRate = _film.SoundRate > 0 ? _film.SoundRate : 22050,
        };
        var sp = new AudioStreamPlayer { Stream = w, Bus = "Master" };
        AddChild(sp);
        return sp;
    }

    public override void _Process(double delta)
    {
        if (_fertig) return;
        // ⚠ DER TON IST DIE UHR. Haengt das Bild an der Anzeige, driftet es
        // gegen den Ton weg — bei 175 Sekunden faellt schon ein Prozent auf.
        // Nur ohne Ton zaehlt die eigene Uhr.
        int soll;
        if (_ton != null && _ton.Playing)
            soll = (int)(_ton.GetPlaybackPosition() * _film.Fps) + 1;
        else
        {
            _uhr += delta;
            soll = (int)(_uhr / _proBild) + 1;
        }
        // ⚠ Aufholen, aber nicht unbegrenzt: bleibt die Anzeige einmal haengen,
        // soll der Film nicht in Zeitraffer nachziehen. Hoechstens drei Bilder
        // je Durchgang — mehr sieht niemand. Dekodiert werden muss trotzdem
        // JEDES, das Format laesst kein Ueberspringen zu.
        int erlaubt = 3;
        while (_naechstes < soll && erlaubt-- > 0)
            if (!NaechstesBild()) { Ende(); return; }
        // Der Ton ist zu Ende und das Bild auch: Schluss.
        if (_naechstes >= _film.Chunks.Count && (_ton == null || !_ton.Playing)) Ende();
    }

    /// <summary>Ein Bild weiter. <c>false</c> heisst: der Film ist zu Ende.
    /// ⚠ Es wird IMMER dekodiert, auch wenn nicht angezeigt wird — jedes Bild
    /// baut auf dem vorigen auf.</summary>
    private bool NaechstesBild()
    {
        if (_naechstes >= _film.Chunks.Count) return false;
        byte[] roh;
        try { roh = _film.ReadVideo(_naechstes); }
        catch (Exception e)
        {
            // ⚠ Die CD kann waehrend des Films herausgenommen werden.
            GD.PrintErr($"Film: Lesefehler bei Bild {_naechstes} ({e.Message}) — Ende");
            return false;
        }
        _naechstes++;
        if (!_dec.DecodeFrame(roh))
        {
            // »Codebuchgroesse 0« — 7 von 104.488 Bildern im ganzen Spiel.
            // Das Vorbild bleibt stehen, genau wie bei ffmpeg.
            return true;
        }
        Zeigen();
        return true;
    }

    /// <summary>RGB555 little-endian in das RGB8, das Godot nimmt.
    /// ⚠ Die fünf Bits werden <b>gespreizt</b> (<c>v&lt;&lt;3 | v&gt;&gt;2</c>),
    /// nicht bloss geschoben: sonst bliebe Weiss bei 248 stehen und das ganze
    /// Bild wäre einen Hauch zu dunkel.</summary>
    private void Zeigen()
    {
        var src = _dec.Frame;
        for (int i = 0, o = 0; o < _rgb8.Length; i += 2, o += 3)
        {
            int v = src[i] | (src[i + 1] << 8);
            int r = (v >> 10) & 31, g = (v >> 5) & 31, b = v & 31;
            _rgb8[o] = (byte)((r << 3) | (r >> 2));
            _rgb8[o + 1] = (byte)((g << 3) | (g >> 2));
            _rgb8[o + 2] = (byte)((b << 3) | (b >> 2));
        }
        _img.SetData(_film.Width, _film.Height, false, Image.Format.Rgb8, _rgb8);
        _tex.Update(_img);
    }

    private void Ende()
    {
        if (_fertig) return;
        _fertig = true;
        _ton?.Stop();
        if (_musikLief && Audio.MidiMusic.Resume()) GD.Print("Film: Menuemusik laeuft weiter");
        SetProcess(false);
        SetProcessInput(false);
        QueueFree();
        _weiter();
    }
}
