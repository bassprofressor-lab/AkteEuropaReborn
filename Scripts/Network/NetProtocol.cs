namespace AkteEuropaReborn.Network;

using System;
using System.Collections.Generic;
using System.Text;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// DIE PAKETE — und wovon ihre Form abgeschrieben ist.
///
/// <para><b>Das Lockstep-Protokoll des Originals ist GELESEN</b>, am 15.08.2026 in
/// beiden GAME.EXE, und es ist genau dasselbe Gespräch, das hier über ENet
/// geführt wird. Die Fundstellen (F:\Akte Europa; in C:\… dieselbe Form an
/// verschobener Adresse):</para>
/// <code>
///   Opcode 1003  Behandler @0x4C46D7:  [0xB89418 + P1] = 1
///                »Spieler P1 ist bereit« — ein Byte je Spielerplatz.
///   Opcode  978  Absender  @0x41516F:  Kladde.Op = 0x3D2;
///                P1..P8 = die acht Bereitmarken [0xB89418..0xB8941F],
///                P9 (+0x18) = die Rundennummer [0x4F9248]
///                Behandler @0x4C3B47:
///                   die acht Bytes zurück in [0xB89418..],
///                   [0x4F6F28] = 1                 ← DIE SPERRE FÄLLT
///                   if ([0xB89418+ich] != 0) verwerfen
///                   if (P9 != Runde && P9 != Runde+1) verwerfen
///                   Kladde.Op = 0x3EB (1003); Kladde.P1 = ich; post()
///                   if (Ring[Lesezeiger].P9 != Runde+1) verwerfen
///                   return 1                       ← die Pumpe BLEIBT STEHEN
///   die Sperre  @0x415039: if ([0x4F6F28] == 0xFF) {           ; wir warten
///                              if (GetTickCount() &gt; t0 + 0x1388)   ; 5000 ms
///                                  »Warte auf Server« (0x4FA570)
///                          }
///                @0x4150DE: Zwischenstufe bei t0 + 0x3E8        ; 1000 ms
///                @0x4C5B78: je Spielerplatz eine Frist von 0x7D0 ; 2000 ms
///   die Rolle   @0x4C1C5B in post():  [0x538270] == 0  ⇒ ICH BIN MITSPIELER
///                (der Satz geht NUR über IDirectPlay::Send hinaus),
///                                     [0x538270] != 0  ⇒ ICH BIN VERMITTLER
///                (der Satz geht in den eigenen Ring und von dort weiter).
///                Der Absender von 978 @0x41514B läuft NUR beim Vermittler.
///   Prüfsumme   @0x40455D..0x4045CD (nur beim Mitspieler, [0x538270] == 0):
///                [0x4F4A34] += Op + P1 + P2 + P3 + P4 ; [0x4F4A30]++
///   ⚠ NEU GELESEN: +0x22 jedes Satzes ist die RUNDENNUMMER.
///                post() @0x4C1C75: [0xB8945A] = word([0x4F9248]) — dasselbe
///                Globale, das 978 in P9 mitführt. Im Bericht der Befehlsschicht
///                stand +0x22 noch als »Bedeutung UNGEKLÄRT«.
/// </code>
///
/// <para><b>Was daran UNSERE SETZUNG ist</b> (das Gefecht ist ausdrücklich
/// Wettkampfmodus und darf abweichen):</para>
/// <list type="number">
///   <item><b>ENet statt DirectPlay.</b> DirectPlay ist tot; die Architektur
///   dahinter — Vermittler, 236-Byte-Sätze, Ring, Rundenfreigabe — ist die
///   gelesene.</item>
///   <item><b>Runde = Simulationstakt.</b> Das Original führt in
///   <c>[0x4F9248]</c> eine eigene, gröbere Rundenzahl (80 Fundstellen, sie
///   treibt auch Missionsskripte). Bei uns ist die Runde der Takt, weil der Satz
///   die Fälligkeit in +0x04 schon in Takten trägt und der Takt die einzige
///   Einheit ist, die auf zwei Maschinen dieselbe ist.</item>
///   <item><b>Die Freigabe rechnet jeder selbst.</b> Im Original baut der
///   Vermittler aus den 1003-Meldungen die 978-Freigabe und schickt sie
///   herum — das kostet den Mitspieler eine volle Runde mehr (1003 hin, 978
///   zurück). Bei uns trägt <b>ein</b> Paket (<see cref="NetPacket.Bereit"/>)
///   die Meldung UND die Befehle dieses Takts, der Vermittler leitet es
///   unverändert weiter, und jeder Teilnehmer stellt selbst fest, dass alle
///   gemeldet haben. Die Aussage ist dieselbe (»der Takt ist frei, wenn jeder
///   Spieler gemeldet hat«), der Weg ist einen Sprung kürzer.</item>
///   <item><b>Meldung und Befehle sind EIN Paket.</b> Im Original reisen die
///   Befehlssätze getrennt von 1003/978. Damit kann ein Satz nach der Freigabe
///   ankommen und auf zwei Maschinen in verschiedenen Takten wirken — genau die
///   Fehlerklasse, an der der Netzmodus des Originals wettkampfuntauglich ist.
///   Bei uns ist »ich bin bereit« und »das sind meine Befehle für diesen Takt«
///   eine einzige, unteilbare Aussage.</item>
///   <item><b>Die Reihenfolge im Takt ist festgelegt:</b> nach Spielernummer,
///   darin nach Paketreihenfolge. Das Original verlässt sich auf die
///   Ankunftsreihenfolge im Ring, und die ist auf zwei Maschinen verschieden.</item>
/// </list>
///
/// <para>Alle Zahlen little-endian, weil der Satz es ist (x86).</para>
/// </summary>
public enum NetPacket : byte
{
    /// <summary>Mitspieler → Vermittler: »ich bin da«, mit Protokollnummer.</summary>
    Hallo = 1,

    /// <summary>Vermittler → alle: die Partie. Karte, <b>KEIM</b>, Spielerzahl,
    /// eigene Spielernummer, Vorlauf und die Einstellungen des Gastgebers.
    /// Das Gegenstück im Original ist Kommando 979 (trägt Techstandard, Konto,
    /// Rohstoffe, Wetter, Start ein) gefolgt von 981 (Start).</summary>
    Sitzung = 2,

    /// <summary>Jeder → alle: »meine Simulation hat schon N Takte hinter sich«.
    /// ⚠ Nur für den gemeinsamen Anfang; siehe
    /// <c>NetGameRunner</c>, Abschnitt Vorlauf.</summary>
    Anfang = 3,

    /// <summary>Jeder → alle: das TAKTPAKET. »Ich bin für Takt t bereit, und das
    /// sind meine Sätze für Takt t.« Entspricht 1003 + 978 des Originals,
    /// zusammengelegt (siehe Klassenkopf, Punkt 4).</summary>
    Bereit = 4,

    /// <summary>Jeder → alle: die Prüfsumme über den ganzzahligen Zustand nach
    /// Takt t. Entspricht der laufenden Summe [0x4F4A34] des Originals.</summary>
    Abdruck = 5,

    /// <summary>Jeder → alle, NUR nach einem Fehlbefund: der ganze Zahlenauszug
    /// des Takts. Erst damit lässt sich sagen, WELCHE Einheit WELCHES Feld —
    /// eine Prüfsumme allein sagt das nie (Regel 10).</summary>
    AbdruckLang = 6,

    /// <summary>Jeder → alle: »ich habe ein Auseinanderlaufen festgestellt«, mit
    /// dem Takt und der Stelle als Text.</summary>
    Auseinander = 7,

    // ---- die LAN-Suche (UDP, nicht ENet) — siehe NetDiscovery.cs ------------
    //
    // ⚠ Dieselbe Nummernreihe und derselbe Schreiber/Leser, obwohl es ein
    // anderer Sockel ist. Zwei Paketformate mit zwei Zählungen wären zwei Orte,
    // an denen eine Nummer doppelt vergeben werden kann — und ein Paket, das auf
    // dem falschen Sockel landet, wird so als »kenne ich nicht« verworfen statt
    // als etwas anderes gedeutet.

    /// <summary>Suchender → Rundruf: »wer hat eine offene Partie?«</summary>
    Frage = 8,

    /// <summary>Gastgeber → Suchender (Einzelsendung): »ich, und zwar so.«</summary>
    Angebot = 9,
}

/// <summary>Die Partie, wie der Vermittler sie verteilt. Alles, was auf beiden
/// Maschinen gleich sein MUSS, steht hier drin und nur hier.</summary>
public sealed class NetSession
{
    /// <summary>⚠ Wird bei jeder Änderung der Paketform erhöht. Zwei Fassungen
    /// mit verschiedener Nummer spielen NICHT zusammen — sie würden
    /// auseinanderlaufen, und ein Auseinanderlaufen aus diesem Grund ist der
    /// teuerste, weil er wie ein Rechenfehler aussieht.</summary>
    public const int Protokoll = 1;

    public string Map = "";
    public uint Seed;
    /// <summary>Wie viele Menschen mitspielen (der Vermittler eingeschlossen).</summary>
    public int Players = 2;
    /// <summary>Meine Nummer in diesem Gespräch, 0 = Vermittler.</summary>
    public int Me;
    /// <summary>Der Spielerplatz der Karte, den ich führe (0..7).</summary>
    public int[] Slot = Array.Empty<int>();
    /// <summary>⚠ UNSERE SETZUNG, siehe <c>NetGameRunner.Lead</c>.</summary>
    public int Lead = 6;
    public int AiCount;
    public int Level;
    public int Techstandard = 1;
    public int Resources = 2;
    public bool AllUnits;

    public int MySlot => Me >= 0 && Me < Slot.Length ? Slot[Me] : 0;

    public override string ToString()
        => $"Karte {Map}, Keim {Seed}, {Players} Mensch(en) auf Plätzen " +
           $"[{string.Join(",", Slot)}], ich bin Nr. {Me} (Platz {MySlot}), " +
           $"Vorlauf {Lead} Takte, KI {AiCount}, Stufe {Level}, " +
           $"Techstandard {Techstandard}, Rohstoffe {Resources}, " +
           $"alle Einheiten {(AllUnits ? "ja" : "nein")}";
}

/// <summary>Bytes schreiben. Klein und ohne Abhängigkeiten — ein Paket ist bei
/// uns eine Folge von Zahlen und Sätzen, nichts weiter.</summary>
public sealed class NetWriter
{
    private byte[] _b = new byte[256];
    private int _n;

    public NetWriter(NetPacket kind) => U8((byte)kind);

    private void Need(int more)
    {
        if (_n + more <= _b.Length) return;
        int cap = _b.Length;
        while (cap < _n + more) cap *= 2;
        Array.Resize(ref _b, cap);
    }

    public NetWriter U8(byte v) { Need(1); _b[_n++] = v; return this; }
    public NetWriter Bool(bool v) => U8(v ? (byte)1 : (byte)0);

    public NetWriter U16(int v)
    {
        Need(2);
        _b[_n++] = (byte)(v & 0xFF);
        _b[_n++] = (byte)((v >> 8) & 0xFF);
        return this;
    }

    public NetWriter I32(int v)
    {
        Need(4);
        _b[_n++] = (byte)(v & 0xFF); _b[_n++] = (byte)((v >> 8) & 0xFF);
        _b[_n++] = (byte)((v >> 16) & 0xFF); _b[_n++] = (byte)((v >> 24) & 0xFF);
        return this;
    }

    public NetWriter U32(uint v) => I32(unchecked((int)v));

    public NetWriter I64(long v)
    {
        I32(unchecked((int)v));
        I32(unchecked((int)(v >> 32)));
        return this;
    }

    public NetWriter U64(ulong v) => I64(unchecked((long)v));

    public NetWriter Text(string s)
    {
        byte[] u = Encoding.UTF8.GetBytes(s ?? "");
        U16(u.Length);
        Need(u.Length);
        Array.Copy(u, 0, _b, _n, u.Length);
        _n += u.Length;
        return this;
    }

    /// <summary>Ein Befehlssatz — <b>236 Byte in der Form des Originals</b>, nicht
    /// die C#-Felder. Was über die Leitung geht, ist damit byteweise dasselbe,
    /// was im Ring liegt und was in die Prüfsumme eingeht.</summary>
    public NetWriter Record(in CommandRecord c)
    {
        Need(CommandRecord.Stride);
        c.WriteTo(_b.AsSpan(_n, CommandRecord.Stride));
        _n += CommandRecord.Stride;
        return this;
    }

    public byte[] Done()
    {
        var outp = new byte[_n];
        Array.Copy(_b, outp, _n);
        return outp;
    }
}

/// <summary>
/// Bytes lesen. ⚠ <b>Wirft nie.</b> Ein zu kurzes oder verdrehtes Paket setzt
/// <see cref="Bad"/> und liefert danach Nullen — der Aufrufer prüft EINMAL am
/// Ende und verwirft das ganze Paket. Ein Netzleser, der bei Unsinn eine
/// Ausnahme wirft, ist eine Abschaltmöglichkeit für jeden, der ein krummes Paket
/// schicken kann.
/// </summary>
public sealed class NetReader
{
    private readonly byte[] _b;
    private int _n;

    public NetReader(byte[] bytes) { _b = bytes ?? Array.Empty<byte>(); }

    public bool Bad { get; private set; }
    public int Left => _b.Length - _n;

    public NetPacket Kind => _b.Length > 0 ? (NetPacket)_b[0] : 0;

    /// <summary>Hinter den Pakettyp springen.</summary>
    public NetReader Begin() { _n = 1; if (_b.Length < 1) Bad = true; return this; }

    private bool Take(int n)
    {
        if (Bad || _n + n > _b.Length) { Bad = true; return false; }
        return true;
    }

    public byte U8() => Take(1) ? _b[_n++] : (byte)0;
    public bool Bool() => U8() != 0;
    public int U16() { if (!Take(2)) return 0; int v = _b[_n] | (_b[_n + 1] << 8); _n += 2; return v; }

    public int I32()
    {
        if (!Take(4)) return 0;
        int v = _b[_n] | (_b[_n + 1] << 8) | (_b[_n + 2] << 16) | (_b[_n + 3] << 24);
        _n += 4;
        return v;
    }

    public uint U32() => unchecked((uint)I32());

    public long I64()
    {
        int lo = I32(), hi = I32();
        return unchecked((long)((ulong)(uint)lo | ((ulong)(uint)hi << 32)));
    }

    public ulong U64() => unchecked((ulong)I64());

    public string Text()
    {
        int n = U16();
        if (n < 0 || !Take(n)) return "";
        string s = Encoding.UTF8.GetString(_b, _n, n);
        _n += n;
        return s;
    }

    public CommandRecord Record()
    {
        if (!Take(CommandRecord.Stride)) return default;
        var c = CommandRecord.ReadFrom(_b.AsSpan(_n, CommandRecord.Stride));
        _n += CommandRecord.Stride;
        return c;
    }

    public List<long> Longs()
    {
        int n = I32();
        var l = new List<long>();
        // ⚠ Deckel: ein Auszug ist so lang wie der Zustand, nicht beliebig.
        // Ohne den legt ein Paket mit n = 2^31 den Empfänger schlafen.
        if (n < 0 || n > 4_000_000) { Bad = true; return l; }
        for (int i = 0; i < n; i++) l.Add(I64());
        return l;
    }
}
