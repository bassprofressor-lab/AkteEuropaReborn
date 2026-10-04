namespace AkteEuropaReborn.Audio;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Godot;

/// <summary>
/// ⭐⭐ 01.10.2026 (bug-376) — <b>unser eigener MIDI-Abspieler</b>, damit der
/// Musikregler wirkt.
///
/// <para><b>Warum es ihn gibt.</b> Bis heute hat <see cref="MidiMusic"/> die
/// <c>.mid</c> dem MCI-Sequenzer von Windows übergeben, so wie das Original
/// (<c>"open sequencer!%s alias playerSnd"</c> @0x4D5543). Dieser Sequenzer
/// kennt keinen Lautstärkebefehl (<c>setaudio</c> → 261, gemessen 10.08.), und
/// der einzige andere Weg, <c>midiOutSetVolume</c>, stellt die GANZE Anwendung
/// leise (gemeldet 21.08., ersatzlos entfernt). Was bleibt: die Noten selbst
/// senden und die Lautstärke als MIDI-Daten regeln — Controller 7
/// »Channel Volume« je Kanal. Das trifft nur die Musik, nichts sonst.</para>
///
/// <para>⚠ <b>UNSERE ZUTAT, keine Nachbildung.</b> Das Original hat keinen
/// Musikregler, nur den Knopf »MIDI-Musik EIN/AUS« (Art 34, <c>0x502304</c> /
/// <c>0x5022F0</c>) und importiert aus WINMM weder <c>midiOut*</c> noch eine
/// Lautstärkefunktion (Bytesuche über beide EXE, berichte/musik-video-fable.md
/// §1.3). Der Gegenschalter <c>--musik-mci</c> führt zurück auf den MCI-Weg
/// des Originals.</para>
///
/// <para>⚠⚠ <b>NIE <c>midiOutSetVolume</c>.</b> Siehe <see cref="MidiMusic.Volume"/>,
/// Eintrag vom 21.08.2026.</para>
///
/// <para><b>SysEx wird übersprungen, nicht gesendet.</b> Gezählt am 01.10.2026:
/// die sechs Stücke <c>0.mid … 5.mid</c> enthalten <b>kein einziges</b>
/// SysEx-Ereignis (F0/F7). <c>midiOutLongMsg</c> bräuchte einen vorbereiteten
/// Puffer (<c>midiOutPrepareHeader</c>, festgehaltener Speicher, Warten auf
/// <c>MHDR_DONE</c>) — Code, der für diese Dateien nie liefe und darum nie
/// geprüft wäre. Der Leser zählt SysEx trotzdem (<see cref="MidiDatei.SysEx"/>),
/// damit es der Prüfstand meldet, falls je eine andere Datei welche trägt.</para>
/// </summary>
public static class MidiSequencer
{
    // ---------------------------------------------------------------- winmm

    [DllImport("winmm.dll")]
    private static extern int midiOutOpen(out IntPtr hmo, uint deviceId, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern int midiOutShortMsg(IntPtr hmo, uint msg);

    [DllImport("winmm.dll")]
    private static extern int midiOutReset(IntPtr hmo);

    [DllImport("winmm.dll")]
    private static extern int midiOutClose(IntPtr hmo);

    [DllImport("winmm.dll")]
    private static extern uint midiOutGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint ms);

    /// <summary>MIDI_MAPPER = (UINT)-1: der Mapper wählt das Standardgerät —
    /// unter Windows 11 der »Microsoft GS Wavetable Synth«, derselbe, den der
    /// MCI-Sequenzer benutzt. Gerät 0 ist der Rückfall.</summary>
    private const uint MidiMapper = 0xFFFFFFFF;

    /// <summary><c>--musikwechsel-alt</c>: beim Stückwechsel das Gerät schließen und
    /// neu öffnen wie bis zum 02.10.2026 (~230 ms Hänger, bug-413).</summary>
    public static bool WechselAlt
    {
        get => _wechselAlt ??= Array.IndexOf(Core.CommandLine.Args, "--musikwechsel-alt") >= 0;
        set => _wechselAlt = value;
    }
    private static bool? _wechselAlt;

    // ---------------------------------------------------------------- Datei

    /// <summary>Ein gesendetes Ereignis: Zeitpunkt in Mikrosekunden ab Stückbeginn
    /// und die fertig gepackte Kurznachricht (Status | d1 &lt;&lt; 8 | d2 &lt;&lt; 16).</summary>
    public readonly record struct Ereignis(long Mikro, uint Nachricht);

    /// <summary>Eine gelesene Standard-MIDI-Datei, alle Spuren zu einer
    /// Zeitfolge zusammengeführt.</summary>
    public sealed class MidiDatei
    {
        public int Format, Spuren, Teilung;
        public readonly List<Ereignis> Ereignisse = new();
        public int Notenanfaenge, Meta, SysEx, Tempowechsel, Cc7;
        public long DauerMikro;
    }

    /// <summary>
    /// SMF lesen: Format 0 und 1 (2 wird wie 1 behandelt), Delta-Zeiten,
    /// laufender Status, Tempo-Meta <c>FF 51</c>, Ende der Spur <c>FF 2F</c>.
    ///
    /// <para>Zusammenführen: alle Spuren auf absolute Ticks, dann <b>stabil</b>
    /// nach Tick sortiert — bei gleichem Tick bleibt die Reihenfolge der Spur
    /// und der Spuren untereinander erhalten (Spur 0 mit dem Tempo zuerst). Die
    /// Tempokarte gilt in Format 1 für alle Spuren.</para>
    /// </summary>
    public static MidiDatei Lies(byte[] b)
    {
        int p = 0;
        if (b.Length < 14 || Txt(b, 0) != "MThd") throw new FormatException("kein MThd");
        int kopf = (int)U32(b, 4);
        var d = new MidiDatei { Format = U16(b, 8), Spuren = U16(b, 10), Teilung = U16(b, 12) };
        p = 8 + kopf;

        // (Tick, Reihenfolge, Nachricht, Tempo) — Tempo > 0 heisst Tempowechsel
        var roh = new List<(long Tick, int Folge, uint Msg, int Tempo)>();
        long endeTick = 0;
        int folge = 0;
        for (int s = 0; s < d.Spuren; s++)
        {
            // ⚠ Fremde Blöcke (nicht MTrk) zwischen den Spuren erlaubt die Norm — überspringen.
            while (p + 8 <= b.Length && Txt(b, p) != "MTrk") p += 8 + (int)U32(b, p + 4);
            if (p + 8 > b.Length) throw new FormatException($"Spur {s} fehlt");
            int len = (int)U32(b, p + 4);
            int q = p + 8, ende = Math.Min(b.Length, q + len);
            long tick = 0;
            int laufend = 0;
            while (q < ende)
            {
                tick += Vlq(b, ref q);
                int st = b[q];
                if (st >= 0x80) q++;
                else if (laufend == 0) throw new FormatException($"laufender Status ohne Vorgaenger, Spur {s}");
                else st = laufend;

                if (st == 0xFF)
                {
                    int typ = b[q++];
                    int l = (int)Vlq(b, ref q);
                    d.Meta++;
                    if (typ == 0x51 && l == 3)
                    {
                        int tempo = (b[q] << 16) | (b[q + 1] << 8) | b[q + 2];
                        roh.Add((tick, folge++, 0, Math.Max(1, tempo)));
                        d.Tempowechsel++;
                    }
                    q += l;
                    if (typ == 0x2F) break;
                    // Meta- und SysEx-Ereignisse heben den laufenden Status auf
                    laufend = 0;
                }
                else if (st == 0xF0 || st == 0xF7)
                {
                    int l = (int)Vlq(b, ref q);
                    q += l;
                    d.SysEx++;          // übersprungen, siehe Klassenkommentar
                    laufend = 0;
                }
                else
                {
                    laufend = st;
                    int hi = st & 0xF0;
                    int d1 = b[q++] & 0x7F;
                    int d2 = hi is 0xC0 or 0xD0 ? 0 : b[q++] & 0x7F;
                    if (hi == 0x90 && d2 > 0) d.Notenanfaenge++;
                    if (hi == 0xB0 && d1 == 7) d.Cc7++;
                    roh.Add((tick, folge++, (uint)(st | (d1 << 8) | (d2 << 16)), 0));
                }
            }
            endeTick = Math.Max(endeTick, tick);
            p = p + 8 + len;
        }

        // stabil: erst Tick, dann die Lesereihenfolge
        roh.Sort((x, y) => x.Tick != y.Tick ? x.Tick.CompareTo(y.Tick) : x.Folge.CompareTo(y.Folge));

        // Ticks -> Mikrosekunden. Teilung mit gesetztem Hochbit ist SMPTE
        // (-Bilder/s im Hochbyte, Ticks je Bild im Tiefbyte), sonst PPQN.
        bool smpte = (d.Teilung & 0x8000) != 0;
        double proTickSmpte = smpte
            ? 1e6 / ((256 - (d.Teilung >> 8)) * Math.Max(1, d.Teilung & 0xFF)) : 0;
        int ppqn = Math.Max(1, d.Teilung & 0x7FFF);
        double tempoUs = 500000;            // Vorgabe der Norm: 120 Schläge/min
        double us = 0;
        long letzter = 0;
        foreach (var e in roh)
        {
            us += (e.Tick - letzter) * (smpte ? proTickSmpte : tempoUs / ppqn);
            letzter = e.Tick;
            if (e.Tempo > 0) tempoUs = e.Tempo;
            else d.Ereignisse.Add(new Ereignis((long)us, e.Msg));
        }
        us += (endeTick - letzter) * (smpte ? proTickSmpte : tempoUs / ppqn);
        d.DauerMikro = (long)us;
        return d;
    }

    private static string Txt(byte[] b, int p) => p + 4 <= b.Length ? Encoding.ASCII.GetString(b, p, 4) : "";
    private static int U16(byte[] b, int p) => (b[p] << 8) | b[p + 1];
    private static uint U32(byte[] b, int p) => (uint)((b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]);

    private static long Vlq(byte[] b, ref int q)
    {
        long v = 0;
        for (int i = 0; i < 4; i++)
        {
            int c = b[q++];
            v = (v << 7) | (uint)(c & 0x7F);
            if (c < 0x80) break;
        }
        return v;
    }

    // ---------------------------------------------------------------- Lautstärke

    /// <summary>
    /// Die Rechnung des Reglers: <c>min(127, cc7 · p / 100)</c>, gerundet.
    /// <para>⚠ UNSERE SETZUNG: linear auf dem Controllerwert. Der GS-Synth
    /// deutet CC 7 ungefähr als 40·log10(v/127) dB — 50 % am Regler sind also
    /// rund −12 dB, nicht »halb so laut«. Das ist dieselbe Kurve, die jedes
    /// Sequenzerprogramm mit seinem Kanalregler zeigt.</para>
    /// </summary>
    public static int Skaliere(int cc7, int prozent)
        => Math.Clamp((Math.Clamp(cc7, 0, 127) * Math.Clamp(prozent, 0, 100) + 50) / 100, 0, 127);

    /// <summary>Ist die Nachricht ein CC 7, wird ihr Wert skaliert und der
    /// Rohwert in <paramref name="letzter"/> gemerkt; alles andere bleibt.</summary>
    public static uint SkaliereNachricht(uint msg, int prozent, int[]? letzter)
    {
        if ((msg & 0xF0) != 0xB0 || ((msg >> 8) & 0x7F) != 7) return msg;
        int ch = (int)(msg & 0x0F);
        int roh = (int)((msg >> 16) & 0x7F);
        if (letzter != null) letzter[ch] = roh;
        return (msg & 0xFFFF) | ((uint)Skaliere(roh, prozent) << 16);
    }

    /// <summary>Was beim Ziehen des Reglers an die 16 Kanäle geht: der zuletzt
    /// von der Datei gesetzte CC 7 (Vorgabe 100, wie es die GM-Norm für
    /// »nicht gesetzt« vorsieht), neu skaliert.</summary>
    public static uint[] Nachsenden(int[] letzter, int prozent)
    {
        var r = new uint[16];
        for (int ch = 0; ch < 16; ch++)
            r[ch] = (uint)(0xB0 | ch | (7 << 8) | (Skaliere(letzter[ch], prozent) << 16));
        return r;
    }

    // ---------------------------------------------------------------- Wiedergabe

    private static readonly object _riegel = new();
    private static IntPtr _hmo = IntPtr.Zero;
    private static Thread? _faden;
    private static volatile bool _halt;
    private static volatile int _prozent = 100;

    /// <summary>Der zuletzt von der Datei gesetzte CC 7 je Kanal — ROH, vor
    /// der Skalierung. Für den Prüfstand im Pausenmenü (»die Ankunft mitmessen«).</summary>
    public static readonly int[] LetzterCc7 = new int[16];

    /// <summary>Das Stück ist bis zum Ende gelaufen (ersetzt <c>MM_MCINOTIFY</c>
    /// bzw. <c>status … mode = stopped</c>). <see cref="MidiMusic.Poll"/> fragt das ab.</summary>
    public static bool Beendet { get; private set; }

    public static bool Offen => _hmo != IntPtr.Zero;
    public static int LastCode { get; private set; }
    public static string LastError { get; private set; } = "";

    /// <summary>Ein Stück von vorn spielen. Stoppt ein laufendes vorher.</summary>
    public static bool Start(MidiDatei datei, int prozent)
    {
        // ⭐ 03.10.2026 (bug-413, KayelGee: »Wenn ein Lied zu Ende ist und das
        // nächste lädt, dann hängt das Spiel kurz«) — das Gerät bleibt beim
        // Stückwechsel OFFEN. Gemessen (Microsoft GS Wavetable Synth, Windows 11,
        // 5 Wiederholungen): midiOutClose ~101 ms + midiOutOpen ~128 ms = ~230 ms
        // im Hauptfaden je Wechsel; Klänge aus + midiOutReset allein 1–2 ms.
        // Gegenschalter --musikwechsel-alt: wie bis zum 02.10. schließen und neu öffnen.
        Stop(schliessen: WechselAlt);
        if (!OperatingSystem.IsWindows()) { LastError = "nur unter Windows"; return false; }
        _prozent = Math.Clamp(prozent, 0, 100);

        IntPtr h = _hmo;
        if (h == IntPtr.Zero)
        {
            int rc = midiOutOpen(out h, MidiMapper, IntPtr.Zero, IntPtr.Zero, 0);
            if (rc != 0) rc = midiOutOpen(out h, 0, IntPtr.Zero, IntPtr.Zero, 0);
            LastCode = rc;
            if (rc != 0)
            {
                LastError = $"midiOutOpen {rc} ({midiOutGetNumDevs()} MIDI-Ausgaenge)";
                return false;
            }
        }
        else
        {
            LastCode = 0;
            // offen geblieben: Regler des Vorgängerstücks (Pitch Bend, Modulation,
            // Halten …) zurück — CC 121 »Reset All Controllers« je Kanal.
            lock (_riegel)
                for (int ch = 0; ch < 16; ch++)
                    midiOutShortMsg(h, (uint)(0xB0 | ch | (121 << 8)));
        }
        LastError = "";
        lock (_riegel)
        {
            _hmo = h;
            for (int ch = 0; ch < 16; ch++) LetzterCc7[ch] = 100;
            // ⚠ Erst alle 16 Kanäle auf die Vorgabe 100 skaliert, sonst bliebe ein
            // Kanal, für den die Datei keinen CC 7 setzt, auf voller Lautstärke
            // (gezählt: in jedem der sechs Stücke setzt nur ein Teil der Kanäle CC 7).
            foreach (uint m in Nachsenden(LetzterCc7, _prozent)) midiOutShortMsg(_hmo, m);
        }
        Beendet = false;
        _halt = false;
        _faden = new Thread(() => Abspielen(datei)) { IsBackground = true, Name = "MidiSequencer" };
        _faden.Start();
        return true;
    }

    private static void Abspielen(MidiDatei d)
    {
        timeBeginPeriod(1);
        try
        {
            var uhr = Stopwatch.StartNew();
            foreach (var e in d.Ereignisse)
            {
                if (!Warte(uhr, e.Mikro)) return;
                lock (_riegel)
                {
                    if (_halt || _hmo == IntPtr.Zero) return;
                    midiOutShortMsg(_hmo, SkaliereNachricht(e.Nachricht, _prozent, LetzterCc7));
                }
            }
            // Bis zum Ende der längsten Spur warten — ausklingende Noten, Pausen am Schluss.
            if (!Warte(uhr, d.DauerMikro)) return;
            Beendet = true;
        }
        finally { timeEndPeriod(1); }
    }

    /// <summary>Bis zum Zeitpunkt warten, in Schritten von höchstens 10 ms, damit
    /// <see cref="Stop"/> den Faden schnell beenden kann. false = angehalten.</summary>
    private static bool Warte(Stopwatch uhr, long mikro)
    {
        while (!_halt)
        {
            long rest = mikro - uhr.ElapsedTicks * 1_000_000L / Stopwatch.Frequency;
            if (rest <= 0) return true;
            if (rest > 1500) Thread.Sleep((int)Math.Min(rest / 1000 - 1, 10));
            else Thread.Yield();
        }
        return false;
    }

    /// <summary>Den Regler übernehmen: 16 × CC 7 neu, aus dem zuletzt gesetzten
    /// Rohwert je Kanal. Wirkt sofort auch auf klingende Noten.</summary>
    public static void SetVolume(int prozent)
    {
        _prozent = Math.Clamp(prozent, 0, 100);
        lock (_riegel)
        {
            if (_hmo == IntPtr.Zero) return;
            foreach (uint m in Nachsenden(LetzterCc7, _prozent)) midiOutShortMsg(_hmo, m);
        }
    }

    /// <summary>Anhalten: Faden beenden, alle Klänge aus (CC 120 »All Sound Off«,
    /// CC 123 »All Notes Off«), <c>midiOutReset</c>, <c>midiOutClose</c>.</summary>
    /// <param name="schliessen">false = Klänge aus und <c>midiOutReset</c>, aber das
    /// Gerät bleibt offen (Stückwechsel, bug-413).</param>
    public static void Stop(bool schliessen = true)
    {
        _halt = true;
        var f = _faden;
        if (f != null && f.IsAlive && f != Thread.CurrentThread) f.Join(500);
        _faden = null;
        lock (_riegel)
        {
            if (_hmo == IntPtr.Zero) return;
            for (int ch = 0; ch < 16; ch++)
            {
                midiOutShortMsg(_hmo, (uint)(0xB0 | ch | (120 << 8)));
                midiOutShortMsg(_hmo, (uint)(0xB0 | ch | (123 << 8)));
            }
            midiOutReset(_hmo);
            if (!schliessen) return;
            midiOutClose(_hmo);
            _hmo = IntPtr.Zero;
        }
    }

    // ---------------------------------------------------------------- Prüfstand

    /// <summary>
    /// <c>--musik-check</c> (kopflos): alle Stücke lesen und zählen, die
    /// CC-7-Rechnung prüfen, einen kleinen Kunst-SMF mit laufendem Status,
    /// zwei Spuren und Tempowechsel prüfen. Öffnet KEIN MIDI-Gerät — ob es
    /// klingt und leiser wird, muss der Spieler hören.
    /// </summary>
    public static string Pruefstand()
    {
        var sb = new StringBuilder();
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"musik-check: {(b ? "ok  " : "FEHL")} {was}\n"); ok &= b; }

        int n = 0;
        for (; n < 200; n++)
        {
            string res = Core.Content.Path($"Sound/{n}.mid");
            if (!FileAccess.FileExists(res)) break;
            try
            {
                var d = Lies(FileAccess.GetFileAsBytes(res));
                long s = d.DauerMikro / 1_000_000;
                sb.Append($"musik-check: {n}.mid Format {d.Format}, {d.Spuren} Spuren, Teilung {d.Teilung}, "
                        + $"{d.Ereignisse.Count} Ereignisse, {d.Notenanfaenge} Notenanfaenge, {d.Cc7}x CC7, "
                        + $"{d.Tempowechsel} Tempo, {d.SysEx} SysEx, Dauer {s / 60}:{s % 60:00}\n");
                Soll(d.Notenanfaenge > 0 && d.DauerMikro > 0, $"{n}.mid hat Noten und Dauer");
                if (d.SysEx > 0) sb.Append($"musik-check: ⚠ {n}.mid traegt {d.SysEx} SysEx — werden UEBERSPRUNGEN\n");
            }
            catch (Exception e) { Soll(false, $"{n}.mid lesbar — {e.Message}"); }
        }
        Soll(n > 0, $"{n} Stuecke gefunden (erstes: {Core.Content.Path("Sound/0.mid")})");
        if (OperatingSystem.IsWindows())
            sb.Append($"musik-check: {midiOutGetNumDevs()} MIDI-Ausgaenge gemeldet (nur gezaehlt, nicht geoeffnet)\n");

        // Die Rechnung des Reglers
        Soll(Skaliere(100, 50) == 50, "Volume 50: CC7 100 -> 50");
        Soll(Skaliere(100, 0) == 0, "Volume 0: CC7 100 -> 0");
        Soll(Skaliere(127, 100) == 127 && Skaliere(100, 100) == 100, "Volume 100: unveraendert");
        Soll(Skaliere(126, 50) == 63, "Volume 50: CC7 126 -> 63");
        var letzter = new int[16];
        Array.Fill(letzter, 100);
        uint m = SkaliereNachricht(0x006407B3, 50, letzter);   // Kanal 4, CC7 = 100
        Soll(m == 0x003207B3 && letzter[3] == 100, $"CC7 im Datenstrom umgeschrieben ({m:X8})");
        Soll(SkaliereNachricht(0x00403C93, 50, letzter) == 0x00403C93, "Note bleibt unberuehrt");
        letzter[3] = 80;
        var nach = Nachsenden(letzter, 50);
        Soll(((nach[3] >> 16) & 0x7F) == 40 && ((nach[0] >> 16) & 0x7F) == 50,
             "Regler nachsenden: Kanal 4 (80) -> 40, Kanal 1 (Vorgabe 100) -> 50");
        Soll(Array.TrueForAll(Nachsenden(letzter, 0), x => ((x >> 16) & 0x7F) == 0), "Regler 0: alle 16 Kanaele 0");

        // Kunst-SMF: Format 1, 96 PPQN, Spur 0 Tempo 1 s/Viertel, Spur 1 Note an,
        // nach einer Viertel Note aus mit LAUFENDEM STATUS, dann CC 7.
        byte[] kunst =
        {
            (byte)'M',(byte)'T',(byte)'h',(byte)'d', 0,0,0,6, 0,1, 0,2, 0,96,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k', 0,0,0,11, 0,0xFF,0x51,3,0x0F,0x42,0x40, 0,0xFF,0x2F,0,
            (byte)'M',(byte)'T',(byte)'r',(byte)'k', 0,0,0,15, 0,0x90,0x3C,0x64, 0x60,0x3C,0x00, 0,0xB0,0x07,0x50, 0,0xFF,0x2F,0,
        };
        try
        {
            var k = Lies(kunst);
            Soll(k.Ereignisse.Count == 3 && k.Notenanfaenge == 1, $"Kunst-SMF: 3 Ereignisse, 1 Notenanfang ({k.Ereignisse.Count}/{k.Notenanfaenge})");
            Soll(k.Ereignisse.Count == 3 && k.Ereignisse[1].Nachricht == 0x00003C90 && k.Ereignisse[1].Mikro == 1_000_000,
                 "Kunst-SMF: laufender Status + Tempo -> Note aus bei 1,000 s");
            Soll(k.DauerMikro == 1_000_000, $"Kunst-SMF: Dauer 1 s ({k.DauerMikro} us)");
        }
        catch (Exception e) { Soll(false, $"Kunst-SMF lesbar — {e.Message}"); }

        sb.Append(ok ? "musik-check: BESTANDEN" : "musik-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}
