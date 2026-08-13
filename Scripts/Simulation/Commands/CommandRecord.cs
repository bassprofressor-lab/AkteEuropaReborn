namespace AkteEuropaReborn.Simulation.Commands;

using System;

/// <summary>
/// EIN BEFEHLSSATZ — 236 Byte, so wie das Original von 1997 ihn führt.
///
/// <para><b>Woher das kommt.</b> Nicht erfunden. Das Original hat genau einen
/// Weg, auf dem eine Spielereingabe in den Zustand gelangt: einen Ringpuffer
/// von Sätzen mit einem einzigen Verteiler. Gemessen in BEIDEN GAME.EXE auf
/// dieser Maschine (Regel 5: nach der Form, nicht nach der Adresse gesucht;
/// <c>aekernel-tools/cmd_fields.py</c>, <c>cmd_opcodes.py</c>, <c>cmd_dis.py</c>):</para>
/// <code>
///                                    F:\Akte Europa   C:\…\Akte Europa
///   Verteiler                        0x4C2262         0x4C26E0
///   Ring (Basis)                     0xB4FA38         0xB509D8
///   Kladde (ein Satz im Bau)         0xB89438         0xB8A3D8
///   Lesezeiger  (ausführen)          0x53828C         (verschoben)
///   Schreibzeiger (eintragen)        0x538290
///   Sendezeiger (weiterleiten)       0x538294
/// </code>
///
/// <para><b>Die Schrittweite ist 236 Byte</b>, und das steht nicht in einer
/// Konstante, sondern in der Adressrechnung des Verteilers (@0x4C2240 in
/// F:\…): <c>ecx=i; shl ecx,3; sub ecx,eax</c> → 7i, <c>lea edx,[eax+ecx*4]</c>
/// → 29i, <c>lea eax,[eax+edx*2]</c> → 59i, <c>lea edx,[eax*4]</c> →
/// <b>236i</b>. Dieselben fünf Befehle stehen an sechs weiteren Stellen
/// (Eintragen, Weiterleiten, Empfangen, Protokollieren). Die beiden
/// <c>rep movsd</c> mit <c>ecx=0x3b</c> (@0x4C1CB2 und @0x40453F) kopieren
/// 59 Dwords = 236 Byte, und der Netzversand @0x4046EF schiebt
/// <c>0xEC = 236</c> in <c>IDirectPlay::Send</c>. Drei unabhängige Zahlen,
/// dieselbe Länge.</para>
///
/// <para><b>Die Felder</b> — und zwar aus der eigenen Fehlerausgabe des Spiels,
/// nicht geraten. @0x4C20AF..0x4C212E steht ein Ausdruck mit der Formatzeile
/// <c>"M:%d %d %d %d %d %d %d %d %d %d %d %d"</c> (0x5383BC): zwölf Zahlen,
/// nämlich der Opcode und danach elf Worte ab +0x08 in Zweierschritten. Damit
/// ist die Parameterreihe belegt, nicht vermutet:</para>
/// <code>
///   +0x00  s16   Opcode        (movsx — vorzeichenbehaftet gelesen)
///   +0x02  u8    Merker        1 = »ist weitergeleitet« (@0x4C1FFB setzt es,
///                              @0x404521 fragt es beim Empfangen)
///   +0x03  u8    —             wird nirgends gelesen (unser Spielerfeld, s.u.)
///   +0x04  s32   Fälligkeit    -1 = sofort; sonst der Uhrenwert [0x538264],
///                              den der Absender @0x4C5B50 einträgt
///   +0x08  s16   P1  …  +0x18  s16 P9    — die vom Code benutzten Parameter
///   +0x1A  s16   P10, +0x1C s16 P11      — nur vom Protokollierer gedruckt
///   +0x22  s16   —             in post() @0x4C1C7A aus [0x4F9248] gesetzt;
///                              BEDEUTUNG UNGEKLÄRT, wird nie gelesen
///   +0x24  s16   P15           benutzt von Opcode 30, 505, 533, 537
///   … bis +0xEB                 nie berührt
/// </code>
/// <para><b>Zahl dahinter</b> (<c>cmd_fields.py</c>, roher Dword-Scan über
/// <c>.text</c>, Regel 4): P1 wird in F:\… an <b>120</b> Stellen adressiert,
/// P2 an 77, P3 an 48, P4 an 25, P5 an 15, P6..P9 an 5–7, P10/P11 an je 1,
/// +0x24 an 10.</para>
///
/// <para>⚠ <b>Die zwei GAME.EXE sind nicht dasselbe Programm</b>, und das ist
/// hier keine Nebensache. In C:\… ist jede dieser Zahlen <b>genau um 1
/// kleiner</b> (119/76/47/24/14/4/4/5/6) und P10/P11 kommen auf <b>0</b>. Die
/// fehlende Stelle ist immer dieselbe: der Protokollierer. In C:\… fehlen die
/// Formatzeile <c>M:%d …</c>, die drei Wiederholungsdateien
/// (<c>c:\replay.mes</c>, <c>.txt</c>, <c>.beg</c>) und alle ihre
/// Fundstellen — direkte
/// Bytesuche: keine. <c>F:\Akte Europa</c> ist ein <b>Entwicklungsbau</b>.</para>
///
/// <para>Deshalb, genau abgegrenzt: <b>P1..P9 und P15 sind in BEIDEN Bauten
/// durch benutzenden Code belegt.</b> Die Existenz von <b>P10/P11 ist nur im
/// Entwicklungsbau belegt</b>, und dort nur durch den Protokollierer — kein
/// Behandler liest sie. Wir führen sie mit, weil der Satz sie hat; wir stützen
/// nichts darauf.</para>
///
/// <para><b>UNSERE SETZUNG</b> ist genau ein Feld: <see cref="Player"/> liegt
/// auf +0x03. Das Original hat für den Absender KEIN Satzfeld — es holt ihn aus
/// dem Transport (<c>IDirectPlay::Receive</c> gibt <c>idFrom</c> heraus,
/// @0x404490) und ein Befehl wie 508 trägt den Spieler als P1. +0x03 ist das
/// Auffüllbyte hinter dem Merker und wird im Original an keiner Stelle gelesen
/// (0 Fundstellen im Dword-Scan) — deshalb ist es der Platz, an dem wir den
/// Absender mitführen können, ohne die gelesene Form zu verbiegen.</para>
///
/// <para>Alles andere in dieser Datei ist Abschrift. Wo eine Bedeutung nicht
/// gelesen ist, steht das dabei und wird nicht gefüllt (Regel 3, Regel 6).</para>
///
/// <para>⚠ <b>Warum der Name <c>CommandRecord</c> und nicht <c>Command</c>.</b>
/// Es gibt im Bau bereits ZWEI Typen namens <c>Command</c>:
/// <c>AkteEuropaReborn.Simulation.Command</c> (SimulationState.cs:243) und
/// <c>AkteEuropaReborn.Simulation.Components.Command</c>
/// (Components/Components.cs:217), beide vom 19.07., beide ein erfundener
/// ECS-Entwurf (<c>CommandType.Harvest</c>, <c>SetRallyPoint</c> …), der mit
/// dem Original nichts zu tun hat und den nichts benutzt. Sie stehen dem Namen
/// im Weg, aber sie gehören nicht alle mir, und ein Aufräumen wäre eine
/// Änderung ohne Messung. Also weicht der neue Typ aus — und der Bericht nennt
/// die Stelle.</para>
/// </summary>
public struct CommandRecord
{
    /// <summary>Die Satzlänge des Originals. Siehe die Herleitung im
    /// Klassenkopf — drei unabhängige Belege für dieselbe Zahl.</summary>
    public const int Stride = 236;

    /// <summary>So viele Parameter führt der Satz nach der Formatzeile
    /// <c>M:%d …</c> mit (P1..P11 auf +0x08..+0x1C).</summary>
    public const int Params = 11;

    // ---- Feldversätze, alle aus dem Code gelesen ----------------------------
    private const int OffOp = 0x00;
    private const int OffFlag = 0x02;
    private const int OffPlayer = 0x03;   // ⚠ UNSERE SETZUNG (s. Klassenkopf)
    private const int OffDue = 0x04;
    private const int OffP1 = 0x08;
    private const int OffUnknown22 = 0x22;
    private const int OffP15 = 0x24;

    /// <summary>Der Opcode. <see cref="CommandOp"/> nennt die Bereiche und die
    /// Nummern, für die eine Bedeutung belegt ist.</summary>
    public short Op;

    /// <summary>+0x02. Im Original 1, sobald der Satz weitergeleitet wurde.
    /// Wir führen ihn mit, damit ein Satz auf der Platte derselbe ist wie im
    /// Original — die Weiterleitung selbst ist noch nicht gebaut.</summary>
    public byte Flag;

    /// <summary>+0x03, ⚠ UNSERE SETZUNG: der Spieler, der den Befehl gegeben
    /// hat. Das Original führt ihn nicht im Satz (Begründung im
    /// Klassenkopf).</summary>
    public byte Player;

    /// <summary>+0x04. Der Takt, ab dem der Befehl fällig ist; -1 = sofort.
    /// Das Original vergleicht dieses Feld gegen seine Uhr [0x538264], bevor es
    /// den Satz durchlässt (@0x4C1DF9..0x4C1E0D) — genau das ist die Stelle, an
    /// der im Lockstep der Verzögerungsausgleich sitzt.</summary>
    public int Due;

    /// <summary>+0x22. Im Original in <c>post()</c> aus [0x4F9248] gesetzt und
    /// danach nie gelesen. ⚠ Bedeutung UNGEKLÄRT — wird mitgeführt und nicht
    /// gedeutet.</summary>
    public short Unknown22;

    private short _p1, _p2, _p3, _p4, _p5, _p6, _p7, _p8, _p9, _p10, _p11;

    /// <summary>+0x24, im Original »P15« der Parameterreihe (Opcode 30, 505,
    /// 533, 537 lesen es). Die Lücke von P11 bis P15 ist im Original
    /// unberührt.</summary>
    public short P15;

    /// <summary>Parameter 1..11, wie der Protokollierer sie zählt.</summary>
    public short this[int n]
    {
        get => n switch
        {
            1 => _p1, 2 => _p2, 3 => _p3, 4 => _p4, 5 => _p5, 6 => _p6,
            7 => _p7, 8 => _p8, 9 => _p9, 10 => _p10, 11 => _p11,
            _ => throw new ArgumentOutOfRangeException(nameof(n), n, "P1..P11"),
        };
        set
        {
            switch (n)
            {
                case 1: _p1 = value; break;
                case 2: _p2 = value; break;
                case 3: _p3 = value; break;
                case 4: _p4 = value; break;
                case 5: _p5 = value; break;
                case 6: _p6 = value; break;
                case 7: _p7 = value; break;
                case 8: _p8 = value; break;
                case 9: _p9 = value; break;
                case 10: _p10 = value; break;
                case 11: _p11 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(n), n, "P1..P11");
            }
        }
    }

    public short P1 { get => _p1; set => _p1 = value; }
    public short P2 { get => _p2; set => _p2 = value; }
    public short P3 { get => _p3; set => _p3 = value; }
    public short P4 { get => _p4; set => _p4 = value; }
    public short P5 { get => _p5; set => _p5 = value; }

    public static CommandRecord Make(short op, byte player, params short[] p)
    {
        var c = new CommandRecord { Op = op, Player = player, Due = -1 };
        for (int i = 0; i < p.Length && i < Params; i++) c[i + 1] = p[i];
        return c;
    }

    // ---- Satz <-> 236 Byte --------------------------------------------------

    /// <summary>Den Satz an die Stelle des Originals schreiben. Die Versätze
    /// sind die gelesenen; alles, was das Original nicht berührt, bleibt 0.
    /// Little-Endian, weil x86.</summary>
    public void WriteTo(Span<byte> dst)
    {
        if (dst.Length < Stride)
            throw new ArgumentException($"ein Befehlssatz braucht {Stride} Byte", nameof(dst));
        dst[..Stride].Clear();
        Put16(dst, OffOp, Op);
        dst[OffFlag] = Flag;
        dst[OffPlayer] = Player;
        Put32(dst, OffDue, Due);
        for (int n = 1; n <= Params; n++) Put16(dst, OffP1 + 2 * (n - 1), this[n]);
        Put16(dst, OffUnknown22, Unknown22);
        Put16(dst, OffP15, P15);
    }

    public static CommandRecord ReadFrom(ReadOnlySpan<byte> src)
    {
        if (src.Length < Stride)
            throw new ArgumentException($"ein Befehlssatz braucht {Stride} Byte", nameof(src));
        var c = new CommandRecord
        {
            Op = Get16(src, OffOp),
            Flag = src[OffFlag],
            Player = src[OffPlayer],
            Due = Get32(src, OffDue),
            Unknown22 = Get16(src, OffUnknown22),
            P15 = Get16(src, OffP15),
        };
        for (int n = 1; n <= Params; n++) c[n] = Get16(src, OffP1 + 2 * (n - 1));
        return c;
    }

    private static void Put16(Span<byte> d, int o, short v)
    { d[o] = (byte)(v & 0xFF); d[o + 1] = (byte)((v >> 8) & 0xFF); }

    private static void Put32(Span<byte> d, int o, int v)
    {
        d[o] = (byte)(v & 0xFF); d[o + 1] = (byte)((v >> 8) & 0xFF);
        d[o + 2] = (byte)((v >> 16) & 0xFF); d[o + 3] = (byte)((v >> 24) & 0xFF);
    }

    private static short Get16(ReadOnlySpan<byte> s, int o) => (short)(s[o] | (s[o + 1] << 8));

    private static int Get32(ReadOnlySpan<byte> s, int o)
        => s[o] | (s[o + 1] << 8) | (s[o + 2] << 16) | (s[o + 3] << 24);

    // ---- Vergleichen --------------------------------------------------------

    /// <summary>
    /// Prüfsumme über den Satz — die Zahl, mit der zwei Maschinen sich später
    /// darüber verständigen, ob sie denselben Befehl gesehen haben.
    ///
    /// <para>Sie geht über die <b>236 Byte in der Form des Originals</b>, nicht
    /// über die C#-Felder: damit prüft sie die Verschriftlichung mit und nicht
    /// nur den Inhalt. FNV-1a, 64 bit — dieselbe Familie wie
    /// <see cref="Determinism.Hasher"/>, damit die Zahlen im Protokoll
    /// derselben Sorte sind.</para>
    /// </summary>
    public ulong Hash64()
    {
        Span<byte> b = stackalloc byte[Stride];
        WriteTo(b);
        return Hash64(b);
    }

    public static ulong Hash64(ReadOnlySpan<byte> bytes)
    {
        ulong h = 0xCBF29CE484222325UL;
        foreach (byte x in bytes) { h ^= x; h *= 0x100000001B3UL; }
        return h;
    }

    /// <summary>Der Satz in der Schreibweise des Originals. Die
    /// <c>replay.txt</c> des Spiels bekommt @0x4C2223 mit
    /// <c>"%d %d %d\n"</c> genau Opcode, P1 und P2; die <c>M:</c>-Zeile
    /// @0x4C2128 alle elf. Wir drucken die lange Form, damit ein Protokoll von
    /// uns neben eines des Originals gelegt werden kann.</summary>
    public override string ToString()
        => $"M:{Op} {_p1} {_p2} {_p3} {_p4} {_p5} {_p6} {_p7} {_p8} {_p9} {_p10} {_p11}";

    /// <summary>Lange Form mit den Feldern, die die <c>M:</c>-Zeile nicht
    /// zeigt — für unsere eigenen Protokolle.</summary>
    public string Describe()
        => $"Op {Op} ({CommandOp.NameOf(Op)}) Spieler {Player} fällig {Due} " +
           $"P[{_p1},{_p2},{_p3},{_p4},{_p5},{_p6},{_p7},{_p8},{_p9}] P15 {P15}";
}
