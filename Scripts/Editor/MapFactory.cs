namespace AkteEuropaReborn.Editor;

using System;
using AkteEuropaReborn.Import;

/// <summary>
/// Eine Karte im Arbeitsspeicher aufbauen — die Werkbank des Karteneditors.
///
/// <para><b>Warum es diese Klasse gibt.</b> <see cref="CwmFile.Create"/> legt
/// nur den Speicher an; leer im Sinne der Leser ist eine nullgefuellte Karte
/// aber NICHT. Drei Abschnitte tragen einen eigenen Leerwert, und wer ihn
/// vergisst, bekommt aus einer frisch angelegten Karte einen Haufen Gespenster
/// geliefert — nachgezaehlt an den Lesern selbst:</para>
/// <list type="bullet">
///   <item>sec5, 8000 Einheitensaetze zu 78 Byte: frei ist ein Platz, wenn
///     <c>+0x09 == 0xFF</c> ist (<see cref="CwmData.Entities"/>). Nullgefuellt
///     also <b>8000 Einheiten</b>.</item>
///   <item>sec4, 2000 Marken zu 6 Byte: leer bei <c>+0x02 == 0xFF</c>
///     (<see cref="CwmData.Markers"/>). Nullgefuellt <b>2000 Marken</b>.</item>
///   <item>sec22, 3000 Gleiszellen zu 5 Byte: leer bei <c>+0x02 == 0xFF</c>
///     (<see cref="CwmExtra.RailCells"/>). Nullgefuellt <b>3000 Gleisstuecke</b>
///     auf Zelle (0,0).</item>
/// </list>
/// <para>Alle uebrigen Leser bis sec38 nehmen Null richtig als leer: sec3 ueber
/// »Typ 0 und kein Name«, sec19/sec28/sec33/sec34 ueber ihre
/// <c>AllZero</c>-Pruefung, sec16 ueber die imap (die keine Infanteriezelle
/// nennt). Und alles ab sec39 traegt eine <c>.CWM</c> ohnehin nicht, also
/// liefern Ziele, Geld, Spieler, Bauplaene und Zuege leere Listen — genau wie
/// bei den 23 gelieferten Kampagnenkarten.</para>
///
/// <para><b>Drei Raster muessen zusammenpassen</b>, und <see cref="Paint"/>
/// setzt sie darum immer gemeinsam:</para>
/// <list type="number">
///   <item>sec1, der Zellensatz: Kachelcode, Hoehe, Flagge. Das BILD.</item>
///   <item>sec6, die imap: 0xFFFE frei, 0xFFFD rau, 0xFFFC Wasser, 0xFFFF
///     gesperrt — spaltenweise, Index <c>col*256 + row</c>. Das ist die Karte,
///     der <c>Can_go</c> @0x4055D0 jede Bewegungsfrage stellt, und aus ihr
///     faellt der <c>terrain</c>-Block der <c>.entities.json</c>.</item>
///   <item>sec2, das Zonenraster 257x257, Index <c>c*257 + r</c>: 0
///     unpassierbar (in allen 23 Karten jede Wasserkachel, 0 Gegenbeispiele),
///     2 offenes Land.</item>
/// </list>
/// <para>Laufen die drei auseinander, sieht die Karte aus wie Wiese und faehrt
/// sich wie ein See. Das ist der Fehler, den diese Klasse unmoeglich macht.</para>
/// </summary>
public static class MapFactory
{
    // ---- die imap-Werte, aus Can_go @0x4055D0 (siehe CwmData.Terrain) -------
    public const int ImapFree = 0xFFFE, ImapRough = 0xFFFD, ImapWater = 0xFFFC, ImapBlocked = 0xFFFF;

    /// <summary>Die sec2-Klassen, wie <see cref="CwmData.Zones"/> sie liest.</summary>
    public const byte ZoneImpassable = 0, ZoneShore = 1, ZoneLand = 2;

    public const int ImapStride = 256, ZoneStride = CwmData.ZoneStride;

    /// <summary>Was eine Zelle IST — die vier Klassen, die <c>Can_go</c>
    /// unterscheidet, hier als das, was der Editor malt.</summary>
    public enum Ground { Free = 0, Rough = 1, Water = 2, Blocked = 3 }

    /// <summary>
    /// Eine leere, in sich stimmige Karte: alle Zellen gesperrt und ohne
    /// Kachel, alle Leerwerte gesetzt. Danach ist <see cref="Paint"/> dran.
    /// </summary>
    public static CwmFile Empty(int width, int height, int tileset, string stem)
    {
        var m = CwmFile.Create(width, height, tileset, stem);

        // die drei Abschnitte mit eigenem Leerwert (siehe Klassenkommentar)
        MarkEmpty(m, 5, CwmData.EntityStride, 0x09);
        MarkEmpty(m, 4, CwmData.MarkerStride, 0x02);
        MarkEmpty(m, 22, CwmExtra.RailStride, 0x02);

        // die imap: erst alles gesperrt, damit auch die Flaeche AUSSERHALB der
        // Karte nicht als Einheitenplatz 0 gelesen wird (0x0000 waere genau
        // das). Was zur Karte gehoert, setzt Paint.
        var imap = m.Sec(6)!;
        for (int i = 0; i < imap.Length; i += 2) { imap[i] = 0xFF; imap[i + 1] = 0xFF; }

        return m;
    }

    /// <summary>Jeden Satz eines Abschnitts an einer Stelle auf 0xFF — der
    /// Leerwert, den der jeweilige Leser prueft.</summary>
    private static void MarkEmpty(CwmFile m, int section, int stride, int at)
    {
        var s = m.Sec(section);
        if (s == null) return;
        for (int o = at; o < s.Length; o += stride) s[o] = 0xFF;
    }

    /// <summary>
    /// Eine Zelle malen: Kachel, Hoehe und Gelaendeklasse in EINEM Zug, damit
    /// Bild, imap und Zonenraster nicht auseinanderlaufen koennen.
    ///
    /// <para>Die Flagge (sec1 +3) bleibt 0. Sie ist der dritte der vier Tests
    /// von <c>can_build_here</c> @0x4203C0 (gelesen @0x41C2D0) — eine Zelle mit
    /// Flagge ungleich 0 ist kein Bauplatz. Der Editor setzt sie nirgends,
    /// also ist jede gemalte Zelle in dieser Hinsicht baubar.</para>
    /// </summary>
    public static void Paint(CwmFile m, int col, int row, int code, int elev, Ground ground)
    {
        if (col < 0 || col >= m.Width || row < 0 || row >= m.Height) return;
        m.SetCell(col, row, code, elev, 0);

        var imap = m.Sec(6);
        if (imap != null)
        {
            int i = (col * ImapStride + row) * 2;
            int v = ground switch
            {
                Ground.Free => ImapFree,
                Ground.Rough => ImapRough,
                Ground.Water => ImapWater,
                _ => ImapBlocked,
            };
            if (i + 1 < imap.Length) { imap[i] = (byte)(v & 0xFF); imap[i + 1] = (byte)(v >> 8); }
        }

        var zone = m.Sec(2);
        if (zone != null)
        {
            int i = col * ZoneStride + row;
            if (i < zone.Length)
                zone[i] = ground switch
                {
                    Ground.Water => ZoneImpassable,
                    Ground.Rough => ZoneShore,
                    Ground.Free => ZoneLand,
                    _ => ZoneImpassable,
                };
        }
    }

    /// <summary>Eine Marke in sec4 setzen. Typ 0x70..0x74 sind die
    /// Startmarken je Spieler (<see cref="CwmData.Markers"/>).</summary>
    public const int MarkerStartBase = 0x70;

    public static void PutMarker(CwmFile m, int slot, int col, int row, int type)
    {
        var s = m.Sec(4);
        if (s == null) return;
        int o = slot * CwmData.MarkerStride;
        if (o + CwmData.MarkerStride > s.Length) return;
        s[o] = (byte)col; s[o + 1] = (byte)row; s[o + 2] = (byte)type;
        s[o + 3] = 0; s[o + 4] = 0; s[o + 5] = 0;
    }
}
