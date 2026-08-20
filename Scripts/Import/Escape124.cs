namespace AkteEuropaReborn.Import;

using System;

/// <summary>
/// <b>DER BILDSTROM DER FILME</b> — Eidos <b>ESCAPE 124</b>, der Bilddekoder
/// hinter <see cref="RplFile"/>. Gelesen am 20.08.2026.
///
/// <para><b>Woher diese Fassung stammt:</b> aus <c>F:\Akte Europa\EDEC.DLL</c>
/// (116.224 B, 32-bit PE, Basis 0x10000000), Ausfuhr <c>EC_Frame</c> →
/// <c>RVA 0x311f</c>. Jede Zahl und jede Reihenfolge unten steht mit der
/// Adresse dabei, an der sie in der DLL steht. <b>Es ist keine Portierung von
/// FFmpegs <c>escape124.c</c></b> — FFmpeg diente nur als Gegenprüfer, nicht
/// als Vorlage. Die Namen der drei Codebücher sind unsere.</para>
///
/// <para><b>Der Bitstrom, von aussen nach innen.</b></para>
///
/// <para><b>1. Bildkopf</b> (2 Doppelworte, ⚠ NICHT Teil des Bitstroms):
/// <c>frame_flags</c> und die wiederholte Bildgrösse. Der Bitstrom beginnt bei
/// Byte 8. Der Dekoder der DLL liest die Fahnen als <c>[esi-8]</c> und prüft
/// nur eines: <c>flags &amp; 0x7800000</c>. Ist das null, <b>bleibt das Bild
/// unverändert stehen</b> (@0x10003277).</para>
///
/// <para><b>2. Der Bitleser ist NIEDERWERTIG ZUERST</b> (@0x100032b6,
/// <c>shrd eax, ebx, cl</c> über ein 64-Bit-Fenster aus little-endian
/// Doppelworten): Bit 0 des ersten Bytes ist das erste Bit. Der Zähler <c>cl</c>
/// läuft von −32 aufwärts; ⚠ wer hier hochwertig-zuerst liest, bekommt schon
/// beim ersten Codebuch Unsinn.</para>
///
/// <para><b>3. Drei Codebücher</b>, jedes nur dann neu gelesen, wenn sein Bit
/// gesetzt ist — sonst gilt <b>das des letzten Bildes weiter</b>. Genau darum
/// ist Bild 0 kein Beweis und Bild 400 sehr wohl einer.</para>
/// <list type="bullet">
///   <item><c>0x20000</c> (@0x100032a5) → <b>Buch 0</b>: Tiefe = 4 Bits,
///   <c>1 &lt;&lt; Tiefe</c> Einträge.</item>
///   <item><c>0x40000</c> (@0x100032e1) → <b>Buch 1</b>: Tiefe = 4 Bits,
///   <c>(1 &lt;&lt; Tiefe) × Superblöcke</c> Einträge — <b>je Superblock ein
///   eigenes Fenster</b> (@0x1000330d, <c>mul</c> mit der Superblockzahl).</item>
///   <item><c>0x80000</c> (@0x10003323) → <b>Buch 2</b>: Anzahl = 20 Bits,
///   Tiefe = <c>ceil(log2(Anzahl))</c> (@0x10003353, die Schleife zählt in
///   <c>ch</c> mit). ⚠ Anzahl 0 ist der Fehlerfall — siehe unten.</item>
/// </list>
///
/// <para><b>4. Ein Codebucheintrag</b> ist <b>34 Bit</b> (@0x10001000):
/// 4 Bit Muster, dann 15 Bit Farbe A, dann 15 Bit Farbe B (RGB555). Er
/// beschreibt <b>eine Zelle von 2×2 Bildpunkten</b>; Musterbit <i>j</i>
/// (0=links oben, 1=rechts oben, 2=links unten, 3=rechts unten) wählt B statt
/// A. Abgelegt wird er als <b>zwei Doppelworte zu je acht Byte</b>: obere
/// Zeile, untere Zeile (@0x1000123e ff.) — daher der Griff
/// <c>Index × 8</c> (@0x1000443e, <c>shl edx, 3</c>).</para>
///
/// <para><b>5. Ein Superblock</b> ist <b>8×8 Bildpunkte</b> = 4×4 Zellen. Vor
/// jedem Superblock steht ein <b>Sprungzähler</b> (@0x10003543): 1 Bit; ist es
/// gesetzt, +1 und 3 Bit dazu; sind die 7, 7 Bit dazu; sind die 0x7f, 12 Bit
/// dazu. Der Blockzeiger rückt um <c>1 + Sprung</c> weiter (er startet bei
/// −1, @0x100033d2/0x100035a9).</para>
///
/// <para><b>6. Der Superblock selbst</b> (@0x100043dc):</para>
/// <list type="number">
///   <item>1 Bit. Ist es <b>0</b>, folgt die <b>Sammelschleife</b>: je Runde
///   ein Codebuchwort, dann <b>17 Bit</b> = 16-Bit-Zellmaske + Stoppbit
///   (@0x1000449d). Das Wort wird in <b>alle</b> Zellen der Maske geschrieben;
///   die Masken werden verodert. Weiter, solange Bit 16 = 0 (@0x10004924).</item>
///   <item>1 Bit. Ist es <b>0</b>: 4 Bit Nibbelwahl — je gesetztem Bit ist das
///   zugehörige Nibbel der 16-Bit-Maske <c>0xF</c>, sonst kommen 4 Bit aus dem
///   Strom (@0x10004964, sechzehn abgewickelte Fälle). Die so gelesene Maske
///   wird mit der Sammelmaske <b>XOR</b>-verknüpft, und für <b>jedes</b>
///   gesetzte Bit folgt ein eigenes Codebuchwort (@0x10004b5e).</item>
///   <item>Ist es <b>1</b>: der Sonderpfad (@0x10005acf), und der gilt nur,
///   wenn <c>frame_flags &amp; 0x10000</c> gesetzt ist. Er liest Paare aus
///   (Codebuchwort, 4-Bit-Zellnummer), bis ein Einsbit kommt.</item>
/// </list>
///
/// <para><b>7. ⚠ Die Zellnummern haben ZWEI Ordnungen, und das ist die
/// Falle.</b> In der Maske stehen die 16 Zellen in <b>Morton-Ordnung</b>:
/// Bit b liegt bei <c>cx = b0 | b2&lt;&lt;1</c>, <c>cy = b1 | b3&lt;&lt;1</c>
/// (abgelesen an den 16 Schreibzweigen ab 0x10004568). Abgearbeitet werden
/// sie aber <b>zeilenweise</b> — 0,1,4,5, 2,3,6,7, 8,9,12,13, 10,11,14,15
/// (die Folge der <c>test ebp,N</c> ab 0x10004b5e). Solange ein Wort für alle
/// Zellen gilt, fällt der Unterschied nicht auf; in der Schlussstufe zieht
/// <b>jede</b> Zelle ihr eigenes Wort, und dort verschiebt die falsche
/// Reihenfolge den ganzen Rest des Bildes. Im <b>Sonderpfad</b> dagegen ist
/// die 4-Bit-Zellnummer schlicht <c>cy = n&gt;&gt;2, cx = n&amp;3</c>
/// (@0x10005bb4, Tafel 0x100190c8).</para>
///
/// <para><b>8. Das Buchwechselspiel.</b> Vor jedem Wort steht 1 Bit; ist es
/// gesetzt, folgt ein zweites, und der Zustand <c>ch</c> springt
/// (@0x10004419): <c>neu = f(ch + bit)</c> mit <c>f = 1,2,0,1</c> — das ist
/// <c>sub al,2 / sbb al,0 / and al,3</c> nachgerechnet. ⚠ <c>ch</c> ist
/// <b>nicht</b> die Buchnummer: <c>ch=0</c> meint Buch 1, <c>ch=1</c> Buch 0,
/// <c>ch=2</c> Buch 2 (an den drei Zweigen 0x1000447a / 0x10004430 /
/// 0x10004455 abgelesen). <c>ch</c> startet je Bild bei 0 (@0x100033d9), gilt
/// also über Superblockgrenzen hinweg.</para>
///
/// <para><b>Was hier NICHT nachgebaut ist</b>, weil die Filme es nicht
/// benutzen: die <b>Alphamaske</b> (64 Byte je Superblock, Bit 6 je Bildpunkt,
/// @0x10004568 <c>test …,0x4040</c>; im Original an
/// <c>EC_AlphaMapApply</c> gehängt), die <b>Farbwandlung</b> in ein anderes
/// Ausgabeformat als RGB555 (@0x10001165, Schiebewerte 0x1001910d ff.) und die
/// <b>18 Farbfilter</b> (@0x1000103e — Invertieren, Kanal isolieren, halbieren
/// …), die über ein Feld des Aufrufers gewählt werden. Wir geben rohes RGB555
/// aus, so wie ffmpeg.</para>
///
/// <para><b>Die Messung.</b> Gegen <c>ffmpeg -pix_fmt rgb555le</c> byteweise
/// verglichen: <b>94.186 Bilder aus allen 35 Filmen, 94.186 bildpunktgenau,
/// 0 daneben</b> — lückenlos ab Bild 0, tiefstes Bild 34.RPL Nr. 5049. Der
/// Prüfstand ist <c>ImportSelfTest.RunRpl</c>. Gegenprobe in einer <b>zweiten
/// Ausgabeform</b> (ffmpeg <c>rgb24</c> gegen unser selbst hochgerechnetes
/// RGB555): INTRO.RPL 250 von 250 genau — die Übereinstimmung hängt also
/// nicht an einer günstig gewählten Vergleichsform.</para>
///
/// <para>⚠ <b>Eine bekannte Grenze, gemessen und nicht behoben.</b> Wenn der
/// Bitstrom eines Bildes <b>mitten in einem Superblock</b> endet, sind sich
/// die drei Dekoder uneins. Wir brechen das Bild ab und behalten, was bis
/// dahin geschrieben wurde; ffmpeg kommt auf etwas anderes; und die DLL liest
/// schlicht in die dahinterliegenden Tonbytes weiter (die Nachladezweige ab
/// <c>0x10011e90</c> prüfen keine Länge). Betroffen ist <b>1 Bild von
/// 104.488</b> — das letzte von <c>4.RPL</c>, Nr. 5051: 352 Bit Strom, bei
/// Bitposition 336 will die Sammelschleife 17 Bit und bekommt 16. Es weicht um
/// <b>52 von 76.800 Bildpunkten</b> in <b>einem</b> Superblock ab. Die
/// naheliegende Kur — einen unvollständig gelesenen Superblock ganz verwerfen
/// — wurde ausprobiert und ändert an genau diesen 52 Punkten <b>nichts</b>;
/// sie ist also nicht die Regel, die ffmpeg anwendet, und wurde darum nicht
/// eingebaut.</para>
/// </summary>
public sealed class Escape124
{
    /// <summary>Bildbreite in Bildpunkten.</summary>
    public int Width { get; }

    /// <summary>Bildhöhe in Bildpunkten.</summary>
    public int Height { get; }

    /// <summary>Der laufende Bildspeicher, <b>RGB555 little-endian</b>, zwei
    /// Byte je Bildpunkt — dasselbe, was ffmpeg als <c>rgb555le</c> ausgibt.
    /// ⚠ Er <b>bleibt zwischen den Bildern stehen</b>; das Format ist ein
    /// Differenzformat.</summary>
    public byte[] Frame { get; }

    /// <summary>Wahr, wenn das letzte Bild an <b>»Codebuchgrösse 0«</b>
    /// gescheitert ist. Über alle 35 Filme trifft das <b>7 von 104.488</b>
    /// Bildern (0,0067 %); ffmpeg gibt dort ebenfalls kein Bild aus.</summary>
    public bool LastWasBadCodebook { get; private set; }

    private readonly int _sbPerRow;
    private readonly int _numSuperblocks;
    private readonly int _strideBytes;

    // Die drei Codebücher, je Eintrag zwei Doppelworte (obere/untere Zeile
    // einer 2x2-Zelle). ⚠ Sie überleben das Bild — genau darum reicht es
    // nicht, Bild 0 zu prüfen.
    private uint[][] _cb = new uint[3][];
    private readonly int[] _depth = new int[3];

    private byte[] _bits = Array.Empty<byte>();
    private int _bitPos, _bitLen;

    /// <summary>Morton: Maskenbit → Zellspalte/Zellzeile. Abgelesen an den
    /// sechzehn Schreibzweigen ab 0x10004568.</summary>
    private static readonly int[] CellX = new int[16];
    private static readonly int[] CellY = new int[16];

    /// <summary>Die Reihenfolge, in der die Schlussstufe die Maskenbits
    /// abfragt — zeilenweise, nicht aufsteigend (@0x10004b5e ff.).</summary>
    private static readonly int[] RowOrder =
        { 0, 1, 4, 5, 2, 3, 6, 7, 8, 9, 12, 13, 10, 11, 14, 15 };

    /// <summary>Der Buchwechsel: <c>neu = Trans[ch + bit]</c>
    /// (@0x10004422 <c>sub al,2 / sbb al,0 / and al,3</c>).</summary>
    private static readonly int[] Trans = { 1, 2, 0, 1 };

    /// <summary><c>ch</c> → Buchnummer. ⚠ Nicht die Einheitsabbildung.</summary>
    private static readonly int[] BookOf = { 1, 0, 2 };

    static Escape124()
    {
        for (int b = 0; b < 16; b++)
        {
            CellX[b] = (b & 1) | (((b >> 2) & 1) << 1);
            CellY[b] = ((b >> 1) & 1) | (((b >> 3) & 1) << 1);
        }
    }

    public Escape124(int width, int height)
    {
        if (width <= 0 || height <= 0 || (width & 7) != 0 || (height & 7) != 0)
            throw new ArgumentException($"Escape124: {width}x{height} ist kein Vielfaches von 8");
        Width = width;
        Height = height;
        _sbPerRow = width / 8;
        _numSuperblocks = _sbPerRow * (height / 8);
        _strideBytes = width * 2;
        Frame = new byte[width * height * 2];
    }

    /// <summary>Setzt Bildspeicher und Codebücher zurück — für einen neuen
    /// Film oder einen Sprung an den Anfang.</summary>
    public void Reset()
    {
        Array.Clear(Frame);
        _cb = new uint[3][];
        _depth[0] = _depth[1] = _depth[2] = 0;
        LastWasBadCodebook = false;
    }

    /// <summary>
    /// Ein Bild dekodieren. <paramref name="chunk"/> sind die Rohbytes aus
    /// <see cref="RplFile.ReadVideo"/> <b>einschliesslich</b> der zwei
    /// Kopf-Doppelworte.
    /// </summary>
    /// <returns><c>true</c>, wenn danach ein gültiges Bild in
    /// <see cref="Frame"/> steht (auch wenn es unverändert blieb);
    /// <c>false</c> nur im Fehlerfall »Codebuchgrösse 0«, in dem auch ffmpeg
    /// kein Bild ausgibt.</returns>
    public bool DecodeFrame(ReadOnlySpan<byte> chunk)
    {
        LastWasBadCodebook = false;
        if (chunk.Length < 8) return true;
        uint flags = (uint)(chunk[0] | (chunk[1] << 8) | (chunk[2] << 16) | (chunk[3] << 24));

        // @0x10003277: die einzige Fahnenprüfung des Originals. Null heisst
        // »Bild bleibt stehen« — und ein stehendes Bild ist ein gueltiges.
        if ((flags & 0x7800000) == 0) return true;

        SetBits(chunk[8..]);

        if ((flags & 0x20000) != 0)                       // @0x100032a5
        {
            int d = (int)Bits(4);
            _depth[0] = d;
            _cb[0] = Unpack(1 << d);
        }
        if ((flags & 0x40000) != 0)                       // @0x100032e1
        {
            int d = (int)Bits(4);
            _depth[1] = d;
            _cb[1] = Unpack((1 << d) * _numSuperblocks);
        }
        if ((flags & 0x80000) != 0)                       // @0x10003323
        {
            uint n = Bits(20);
            if (n == 0)
            {
                // ⚠ Hier gehen Original und ffmpeg AUSEINANDER: die DLL
                // ueberspringt bloss das Buch (@0x1000334e je) und rechnet mit
                // dem alten weiter, ffmpeg bricht mit »Invalid codebook size 0«
                // ab und gibt kein Bild aus. Wir tun es wie ffmpeg, damit der
                // Vergleich eine Bedeutung hat -- und sagen es hier, statt es
                // zu verstecken. 7 von 104.488 Bildern sind betroffen.
                LastWasBadCodebook = true;
                return false;
            }
            // @0x10003350: xor ch,ch / dec eax / [inc ch / shr eax,1 / jne]
            // ⚠ Das ist eine ABWEISENDE Schleife am ENDE, also mindestens EIN
            // Durchlauf: fuer n = 1 kommt 1 heraus, nicht 0. `bitlength(n-1)`
            // allein waere an genau dieser einen Stelle falsch (und in
            // 94.186 geprueften Bildern faellt es nicht auf, weil kein
            // Codebuch der 35 Filme genau einen Eintrag hat).
            int d = Math.Max(1, BitLength(n - 1));
            _depth[2] = d;
            _cb[2] = Unpack((int)n);
        }

        int ch = 0;                                        // @0x100033d9
        int sb = -1;                                       // @0x10003282 + der erste Durchlauf
        while (true)
        {
            if (_bitLen - _bitPos < 1) break;
            // Sprungzaehler @0x10003543
            uint skip = Bits(1);
            if (skip != 0)
            {
                skip = 1;
                uint t = Bits(3);
                skip += t;
                if (t == 7)
                {
                    t = Bits(7);
                    skip += t;
                    if (t == 0x7f) skip += Bits(12);
                }
            }
            sb += 1 + (int)skip;
            if (sb >= _numSuperblocks) break;              // @0x100035bf

            int bx = (sb % _sbPerRow) * 8;
            int by = (sb / _sbPerRow) * 8;
            int at = by * _strideBytes + bx * 2;

            uint multi = 0;
            if (_bitLen - _bitPos < 1) break;
            if (Bits(1) == 0)                              // @0x100043e8
            {
                while (true)
                {
                    if (!Macroblock(ref ch, sb, out uint top, out uint bot)) return true;
                    if (_bitLen - _bitPos < 17) return true;
                    uint x = Bits(17);                     // @0x1000449d
                    multi |= x;
                    uint m = x & 0xffff;
                    for (int b = 0; m != 0 && b < 16; b++)
                        if (((m >> b) & 1) != 0) Put(at, CellX[b], CellY[b], top, bot);
                    if ((x & 0x10000) != 0) break;         // @0x10004924
                }
            }

            if (_bitLen - _bitPos < 1) return true;
            if (Bits(1) != 0)                              // @0x1000493b
            {
                // Sonderpfad @0x10005acf — nur bei gesetztem Bit 16.
                if ((flags & 0x10000) == 0) continue;
                while (true)
                {
                    if (_bitLen - _bitPos < 1) return true;
                    if (Bits(1) != 0) break;               // Stoppbit @0x10005aeb
                    if (!Macroblock(ref ch, sb, out uint top, out uint bot)) return true;
                    if (_bitLen - _bitPos < 4) return true;
                    int n = (int)Bits(4);                  // @0x10005ba0, Zellnummer zeilenweise
                    Put(at, n & 3, n >> 2, top, bot);
                }
                continue;
            }

            if (_bitLen - _bitPos < 4) return true;
            uint sel = Bits(4);                            // @0x1000495a
            uint mask = 0;
            for (int k = 0; k < 4; k++)
            {
                uint nib;
                if (((sel >> k) & 1) != 0) nib = 0xf;
                else
                {
                    if (_bitLen - _bitPos < 4) return true;
                    nib = Bits(4);
                }
                mask |= nib << (4 * k);
            }
            mask = (mask ^ multi) & 0xffff;                // @0x100049b7 xor ebp, eax
            if (mask == 0) continue;                       // @0x10004b5e
            foreach (int b in RowOrder)
            {
                if (((mask >> b) & 1) == 0) continue;
                if (!Macroblock(ref ch, sb, out uint top, out uint bot)) return true;
                Put(at, CellX[b], CellY[b], top, bot);
            }
        }
        return true;
    }

    /// <summary>Ein Codebuchwort holen: Buchwechsel, dann Index.
    /// @0x100043f0 / 0x10004b76 / 0x10005af3 — dreimal derselbe Rumpf.</summary>
    private bool Macroblock(ref int ch, int sb, out uint top, out uint bot)
    {
        top = bot = 0;
        if (_bitLen - _bitPos < 1) return false;
        if (Bits(1) != 0)
        {
            if (_bitLen - _bitPos < 1) return false;
            ch = Trans[ch + (int)Bits(1)];
        }
        int k = BookOf[ch];
        int d = _depth[k];
        if (_bitLen - _bitPos < d) return false;
        int i = d != 0 ? (int)Bits(d) : 0;
        // ⚠ Buch 1 hat je Superblock ein eigenes Fenster (@0x1000348b, der
        // laufende Zeiger 0x100190a0 rueckt je uebersprungenem Block weiter).
        if (k == 1) i += sb << _depth[1];
        uint[] cb = _cb[k];
        if (cb == null || (i * 2 + 1) >= cb.Length) return false;
        top = cb[i * 2];
        bot = cb[i * 2 + 1];
        return true;
    }

    /// <summary>Eine 2×2-Zelle schreiben: oberes und unteres Doppelwort,
    /// je zwei Bildpunkte (@0x10004578/0x10004590).</summary>
    private void Put(int at, int cx, int cy, uint top, uint bot)
    {
        int o = at + cy * 2 * _strideBytes + cx * 4;
        byte[] f = Frame;
        f[o] = (byte)top; f[o + 1] = (byte)(top >> 8);
        f[o + 2] = (byte)(top >> 16); f[o + 3] = (byte)(top >> 24);
        o += _strideBytes;
        f[o] = (byte)bot; f[o + 1] = (byte)(bot >> 8);
        f[o + 2] = (byte)(bot >> 16); f[o + 3] = (byte)(bot >> 24);
    }

    /// <summary>Ein Codebuch lesen — je Eintrag 34 Bit (@0x10001000).</summary>
    private uint[] Unpack(int size)
    {
        if (size <= 0) return Array.Empty<uint>();
        // 34 Bit je Eintrag; was nicht mehr in den Strom passt, gibt es nicht.
        long have = (_bitLen - _bitPos) / 34L;
        int n = (int)Math.Min(size, Math.Max(have, 0));
        var cb = new uint[size * 2];
        for (int e = 0; e < n; e++)
        {
            uint m = Bits(4);
            uint v = Bits(30);
            uint a = v & 0x7fff;
            uint b = (v >> 15) & 0x7fff;
            uint p0 = (m & 1) != 0 ? b : a;
            uint p1 = (m & 2) != 0 ? b : a;
            uint p2 = (m & 4) != 0 ? b : a;
            uint p3 = (m & 8) != 0 ? b : a;
            cb[e * 2] = p0 | (p1 << 16);
            cb[e * 2 + 1] = p2 | (p3 << 16);
        }
        return cb;
    }

    private static int BitLength(uint v)
    {
        int n = 0;
        while (v != 0) { v >>= 1; n++; }
        return n;
    }

    // ---- Bitleser, niederwertig zuerst (@0x100032b6) ------------------------

    private void SetBits(ReadOnlySpan<byte> data)
    {
        // ⚠ Acht Byte Luft: ein Griff nach 32 Bit fasst bis zu fuenf Byte an,
        // und am Stromende darf das nicht ueber den Rand greifen.
        if (_bits.Length < data.Length + 8) _bits = new byte[data.Length + 8];
        else Array.Clear(_bits, 0, Math.Min(_bits.Length, data.Length + 8));
        data.CopyTo(_bits);
        Array.Clear(_bits, data.Length, 8);
        _bitPos = 0;
        _bitLen = data.Length * 8;
    }

    private uint Bits(int k)
    {
        if (k <= 0) return 0;
        int p = _bitPos;
        if (p + k > _bitLen) { _bitPos = _bitLen; return 0; }
        int b = p >> 3, o = p & 7;
        ulong w = (ulong)_bits[b] | ((ulong)_bits[b + 1] << 8) | ((ulong)_bits[b + 2] << 16) |
                  ((ulong)_bits[b + 3] << 24) | ((ulong)_bits[b + 4] << 32);
        _bitPos = p + k;
        return (uint)((w >> o) & ((1UL << k) - 1));
    }
}
