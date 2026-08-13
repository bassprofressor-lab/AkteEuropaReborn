namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// A map file: `NN.CWM` is a campaign level, `N.DM` a full saved state.
///
/// Both are the same container — a small header followed by a run of sections
/// that the loader (`map_loader` @0x41e070) reads one after the other through
/// the same (de)compressing reader (@0x4023a6). Section 1 is the terrain
/// record grid and is W*H*4 bytes; sections 2 onwards have fixed decoded sizes
/// (see <see cref="CwmSections"/>). A .DM carries all 131 with nothing left
/// over; a .CWM stops after 38.
///
/// The compression is a plain run marker: the first byte of a chunk is the
/// marker; wherever it appears, the next byte is a count and the one after it
/// the value to repeat — a count of zero means the marker byte itself. Small
/// sections (up to 0x64 bytes) are stored raw.
///
/// Ported from the Python reader and checked against it, see ImportSelfTest.
/// </summary>
public sealed class CwmFile
{
    public string Stem = "";
    public string Mission = "";
    public string Comment = "";

    /// <summary>Die Missionsnummer aus dem Dateikopf — <c>word[0x539934]</c>.
    /// Kampagne 1..15, Netzkarten 51..58; keine Karte traegt 0.</summary>
    public int MissionNumber;

    public int Tileset, Width, Height;
    public bool Compressed;
    public int TrailingBytes;

    /// <summary>Section 1 is at index 0 — the terrain record grid.</summary>
    public readonly List<byte[]> Sections = new();

    /// <summary>Section numbers as the documentation names them: sec1 is the
    /// record grid, so `Sec(3)` is the building table.</summary>
    public byte[]? Sec(int number)
        => number >= 1 && number - 1 < Sections.Count ? Sections[number - 1] : null;

    public byte[] Records => Sections.Count > 0 ? Sections[0] : Array.Empty<byte>();

    /// <summary>
    /// Die ROHSTOFFVORKOMMEN dieser Karte — <c>(spalte, zeile, menge)</c>.
    ///
    /// <para>⚠ <b>Das steht in KEINEM Abschnitt einer <c>.CWM</c>, und das ist
    /// kein Versehen.</b> Im Original füllt das MISSIONSSKRIPT die Liste, über
    /// <c>add_terra_place(spalte, zeile, menge)</c> (C: 0x4D0A10, F: 0x4D05C0) im
    /// SETUP-Block — gemessen 50 Aufrufe in 8 Missionen, in beiden Fassungen
    /// gleich, alle mit drei Konstanten (<c>aekernel-tools/mission_terra.py</c>).
    /// Eine gelieferte Karte trägt sie also nicht, und dieses Feld bleibt beim
    /// Laden LEER.</para>
    ///
    /// <para>Gefüllt wird es nur von <see cref="Editor.MapDeposits"/> für eine
    /// <b>erzeugte</b> Karte, die kein Missionsskript hat und darum sonst gar
    /// keine Vorkommen hätte (gemessen: Feld-Rohstoffmine 0 Bauplätze). Von hier
    /// geht es über <c>ContentBuilder.MapMeta</c> in die Karten-<c>.json</c>, wo
    /// <see cref="Simulation.NavGrid"/> es beim Laden wieder herausliest — also
    /// in dieselbe Liste, aus der <c>CellOnDeposit</c> @0x4205C0 fragt.</para>
    /// </summary>
    public readonly List<(int Col, int Row, int Amount)> Terra = new();

    // ---- eine Karte im ARBEITSSPEICHER bauen --------------------------------
    //
    // Bis hierher war diese Klasse ein reiner Leser. Der Karteneditor braucht
    // die Gegenrichtung — aber NICHT bis zur Datei: eine `.CWM` zurueckzu-
    // schreiben ist weder noetig noch belegbar (der Kartendialog @0x4c8600 /
    // @0x4c8640 hat null Aufrufer, und 5 der 37 Abschnitte — sec18, sec20,
    // sec21, sec25, sec32 — sind in allen 23 gelieferten Karten belegt und
    // ungelesen; was da hineingehoerte, weiss niemand).
    //
    // Was reicht, ist eine CwmFile IM SPEICHER: dahinter haengt der ganze
    // vorhandene Schreibweg — `ContentBuilder.ExportMap` → `MapBaker.Bake` →
    // `EntitiesJson` — der genau das Format schreibt, das die Engine laedt.

    /// <summary>Wie viele Abschnitte eine <c>.CWM</c> traegt: der Satzraster
    /// plus 37 weitere. Gemessen an den 23 gelieferten Kampagnenkarten, die
    /// nach dem 38. abbrechen (eine <c>.DM</c> traegt alle 131).</summary>
    public const int CwmSectionCount = 38;

    /// <summary>
    /// Eine leere Karte im Arbeitsspeicher: Kopfangaben gesetzt, alle
    /// Abschnitte in ihrer gelesenen Groesse angelegt und mit Null gefuellt.
    ///
    /// <para>⚠ <b>Null ist nicht ueberall »leer«.</b> Drei Abschnitte kennen
    /// einen eigenen Leerwert, und wer ihn nicht setzt, bekommt aus einer
    /// nullgefuellten Karte 8000 Einheiten, 2000 Marken und 3000 Gleisstuecke
    /// geliefert — siehe <c>Editor.MapFactory.Empty</c>, wo das gesetzt wird.
    /// Diese Stelle hier legt nur den Speicher an.</para>
    ///
    /// <para><paramref name="missionNumber"/> ist der Kampagnenzaehler aus dem
    /// Dateikopf. <b>UNSERE Setzung:</b> 0 fuer eine selbst erzeugte Karte —
    /// keine der 23 gelieferten traegt 0 (1..15 Kampagne, 51..58 Netz), also
    /// ist die Zahl eindeutig als »keine Originalkarte« zu lesen.</para>
    /// </summary>
    public static CwmFile Create(int width, int height, int tileset,
                                 string stem = "neu", string mission = "",
                                 int sections = CwmSectionCount, int missionNumber = 0)
    {
        if (width <= 0 || width > 254 || height <= 0 || height > 254)
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Kartengroesse {width}x{height} — die groesste gelieferte Karte ist 254x254");
        var m = new CwmFile
        {
            Stem = stem,
            Mission = mission.Length > 0 ? mission : stem,
            Comment = "",
            MissionNumber = missionNumber,
            Tileset = tileset,
            Width = width,
            Height = height,
            Compressed = false,
        };
        m.Sections.Add(new byte[width * height * 4]);
        int n = Math.Min(sections - 1, CwmSections.Sizes.Length);
        for (int i = 0; i < n; i++) m.Sections.Add(new byte[CwmSections.Sizes[i]]);
        return m;
    }

    /// <summary>Einen ganzen Abschnitt setzen — die Groesse muss die des
    /// Laders sein, sonst liest jeder Leser daneben.</summary>
    public void SetSection(int number, byte[] data)
    {
        if (number < 1 || number - 1 >= Sections.Count)
            throw new ArgumentOutOfRangeException(nameof(number));
        int want = number == 1 ? Width * Height * 4 : CwmSections.Sizes[number - 2];
        if (data.Length != want)
            throw new ArgumentException($"sec{number} ist {want} Byte gross, nicht {data.Length}");
        Sections[number - 1] = data;
    }

    /// <summary>Jedes Byte eines Abschnitts auf denselben Wert — fuer die
    /// Leerwerte 0xFF, die drei Abschnitte brauchen.</summary>
    public void FillSection(int number, byte value)
    {
        var s = Sec(number) ?? throw new ArgumentOutOfRangeException(nameof(number));
        Array.Fill(s, value);
    }

    /// <summary>Der Zellensatz aus sec1: Code u16, Hoehe, Flagge — genau die
    /// vier Byte, die <see cref="MapBaker.Bake"/> und
    /// <c>ContentBuilder.MapMeta</c> wieder herauslesen.</summary>
    public void SetCell(int col, int row, int code, int elev, int flag)
    {
        if (col < 0 || col >= Width || row < 0 || row >= Height) return;
        var rec = Records;
        int o = (row * Width + col) * 4;
        rec[o] = (byte)(code & 0xFF);
        rec[o + 1] = (byte)((code >> 8) & 0xFF);
        rec[o + 2] = (byte)elev;
        rec[o + 3] = (byte)flag;
    }

    public int CodeAt(int col, int row)
        => col < 0 || col >= Width || row < 0 || row >= Height ? 0
         : BitConverter.ToUInt16(Records, (row * Width + col) * 4);

    public int ElevAt(int col, int row)
        => col < 0 || col >= Width || row < 0 || row >= Height ? 0
         : Records[(row * Width + col) * 4 + 2];

    public int FlagAt(int col, int row)
        => col < 0 || col >= Width || row < 0 || row >= Height ? 0
         : Records[(row * Width + col) * 4 + 3];

    private sealed class Reader
    {
        private readonly byte[] _d;
        public int P;
        public Reader(byte[] d) { _d = d; }
        public int Left => _d.Length - P;
        public byte U8() => _d[P++];
        public ushort U16() { ushort v = BitConverter.ToUInt16(_d, P); P += 2; return v; }
        public uint U32() { uint v = BitConverter.ToUInt32(_d, P); P += 4; return v; }
        public byte[] Take(int n)
        {
            if (n < 0 || P + n > _d.Length) throw new EndOfStreamException();
            var b = new byte[n];
            Array.Copy(_d, P, b, 0, n);
            P += n;
            return b;
        }
    }

    /// <summary>One run-length chunk: marker, then value/count pairs.</summary>
    private static byte[] DecodeChunk(byte[] body)
    {
        byte marker = body[0];
        var outp = new List<byte>(body.Length * 2);
        int i = 1, len = body.Length;
        while (i < len)
        {
            byte b = body[i];
            if (b == marker)
            {
                byte cnt = body[i + 1];
                if (cnt != 0)
                {
                    byte v = body[i + 2];
                    for (int k = 0; k < cnt; k++) outp.Add(v);
                    i += 3;
                }
                else { outp.Add(marker); i += 2; }
            }
            else { outp.Add(b); i++; }
        }
        return outp.ToArray();
    }

    private static byte[] ReadSection(Reader r, int outSize, bool compressed)
    {
        if (!(compressed && outSize > 0x64)) return r.Take(outSize);
        var outp = new List<byte>(outSize);
        while (outp.Count < outSize)
        {
            int size = (int)r.U32();
            var body = r.Take(size - 4);
            outp.AddRange(DecodeChunk(body));
        }
        return outp.ToArray();
    }

    private static string CStr(byte[] b) => Cp437.GetString(b, 0, b.Length);

    public static CwmFile Load(string path)
    {
        var m = FromBytes(File.ReadAllBytes(path));
        m.Stem = Path.GetFileNameWithoutExtension(path);
        return m;
    }

    public static CwmFile FromBytes(byte[] d)
    {
        var r = new Reader(d);
        var m = new CwmFile();
        byte magic = r.U8();
        if (magic != 0x43) throw new InvalidDataException("Keine CWM/DM-Datei");
        byte comp = r.U8();
        r.U8();                       // sub
        r.U8();                       // b3
        m.Tileset = r.U8();
        m.Compressed = comp == 1;
        m.Comment = CStr(r.Take(21));
        // ⚠ NICHT "immer 1". Es ist die MISSIONSNUMMER — der Lader liest genau
        // diese zwei Byte nach `word[0x539934]` (@0x41E242), dem Kampagnen-
        // zaehler, an dem Diplomatie, KI-Datei, Bauplaene und Missionslogik
        // haengen. Gemessen: 01..15.CWM tragen 1..15, NET01..08 tragen 51..58.
        m.MissionNumber = r.U16();
        m.Mission = CStr(r.Take(21));
        m.Width = r.U16();
        m.Height = r.U16();
        int flag = r.U16();
        if (flag == 0xffff) r.Take(0x14);

        m.Sections.Add(ReadSection(r, m.Width * m.Height * 4, m.Compressed));
        foreach (int size in CwmSections.Sizes)
        {
            try { m.Sections.Add(ReadSection(r, size, m.Compressed)); }
            catch (Exception) { break; }        // a .CWM simply ends early
        }
        m.TrailingBytes = r.Left;
        return m;
    }
}
