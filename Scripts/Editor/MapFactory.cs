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

    /// <summary>
    /// EIN GEBAEUDE WIEDER WEGNEHMEN — der Ruecknahmeknopf des Editors.
    ///
    /// <para>Zwei Dinge muessen zurueck, und beide hat <see cref="PutBuilding"/>
    /// gesetzt: der Satz in sec3 und die Belegung in der imap. Nur den Satz zu
    /// leeren waere der schlimmere Fehler von beiden — das Gebaeude verschwaende
    /// aus dem Bild, seine Grundflaeche bliebe aber fuer immer unpassierbar und
    /// unbebaubar, und niemand saehe warum.</para>
    ///
    /// <para><b>Worauf die imap zurueckgesetzt wird</b>: auf den Wert, den das
    /// Zonenraster sec2 fuer diese Zelle nennt. <see cref="Paint"/> setzt beide
    /// in EINEM Zug, also ist die Umkehrung auf einer vom Editor erzeugten Karte
    /// exakt und nicht geraten — und der Editor bearbeitet nur solche
    /// (<see cref="MapEditSession"/>). ⚠ Die eine Unschaerfe ist benannt: Zone 0
    /// steht sowohl fuer Wasser als auch fuer gesperrt, und <c>Paint</c> bildet
    /// beide darauf ab. Zurueck geht es darum auf <b>gesperrt</b> — der
    /// vorsichtigere der zwei Werte, denn eine faelschlich begehbare Wasserzelle
    /// waere ein Weg fuer Fahrzeuge, wo keiner sein darf.</para>
    ///
    /// <para>⚠ <b>Der Platz wird NICHT nachgerueckt.</b> Die Saetze dahinter
    /// behalten ihre Nummer, und der frei gewordene bleibt frei. Das Original
    /// nummeriert seine Gebaeude nicht um, und der Handgriff in der imap
    /// (<see cref="BuildingHandle"/>) haengt an der Platznummer: wer
    /// nachrueckte, muesste jede Zelle jedes dahinterliegenden Gebaeudes
    /// mitziehen.</para>
    /// </summary>
    /// <returns>false, wenn dort gar kein gebautes Gebaeude steht.</returns>
    public static bool ClearBuilding(CwmFile m, int slot)
    {
        var s = m.Sec(3);
        if (s == null || slot < 0) return false;
        int o = slot * CwmData.BuildingStride;
        if (o + CwmData.BuildingStride > s.Length) return false;
        if (s[o + 0x18] != 1) return false;                  // steht nichts drauf

        int col = s[o] | (s[o + 1] << 8);
        int row = s[o + 2] | (s[o + 3] << 8);

        // die imap wieder freigeben — nur die Zellen, die WIRKLICH diesem
        // Gebaeude gehoeren. Der Handgriff sagt es; ein Vergleich der Zellen
        // waere Raterei, sobald zwei Gebaeude sich beruehren.
        var imap = m.Sec(6);
        var zone = m.Sec(2);
        if (imap != null)
        {
            int handle = BuildingHandle(slot);
            for (int dr = 0; dr < CwpFile.PatternHeight; dr++)
                for (int dc = 0; dc < CwpFile.PatternWidth; dc++)
                {
                    int c = col + dc, r = row + dr;
                    if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) continue;
                    int i = (c * ImapStride + r) * 2;
                    if (i + 1 >= imap.Length) continue;
                    if ((imap[i] | (imap[i + 1] << 8)) != handle) continue;
                    int z = zone != null && c * ZoneStride + r < zone.Length
                          ? zone[c * ZoneStride + r] : ZoneLand;
                    int v = z switch
                    {
                        ZoneLand => ImapFree,
                        ZoneShore => ImapRough,
                        _ => ImapBlocked,          // Zone 0: Wasser ODER gesperrt
                    };
                    imap[i] = (byte)(v & 0xFF); imap[i + 1] = (byte)(v >> 8);
                }
        }

        for (int k = 0; k < CwmData.BuildingStride; k++) s[o + k] = 0;
        return true;
    }

    // ========================================================================
    //  sec22 — DAS GLEIS
    // ========================================================================

    /// <summary>3000 Plaetze zu 5 Byte, wie <see cref="CwmExtra.RailCells"/> sie
    /// liest. Bild 0xFF heisst »Platz leer«.</summary>
    public const int RailSlots = 3000, RailStride = 5, RailEmpty = 0xFF;

    /// <summary>Trefferpunkte eines frisch gelegten Gleisstuecks. GELESEN:
    /// <c>rail_add</c> @0x4AFADA schreibt <c>0x96</c> = 150.</summary>
    public const int RailHp = 150;

    /// <summary>
    /// EIN GLEISSTUECK LEGEN ODER WEGNEHMEN.
    ///
    /// <para>Der Weg ist der von <c>rail_add</c> @0x4AFA90: den ersten Platz mit
    /// Bild 0xFF suchen und die fuenf Byte hineinschreiben. Steht auf der Zelle
    /// schon eines, wird dessen Platz benutzt statt ein zweites anzulegen —
    /// zwei Stuecke auf derselben Zelle kennt das Original nicht.</para>
    ///
    /// <para><paramref name="frame"/> ist das BILD und wird nicht geraten,
    /// sondern von <see cref="RailFrame"/> aus den Nachbarn bestimmt.</para>
    /// </summary>
    /// <returns>Der benutzte Platz, oder −1 wenn alle 3000 belegt sind.</returns>
    public static int PutRail(CwmFile m, int col, int row, int frame, int line)
    {
        var s = m.Sec(22);
        if (s == null || col < 0 || col >= m.Width || row < 0 || row >= m.Height) return -1;
        int n = Math.Min(RailSlots, s.Length / RailStride);
        int free = -1;
        for (int i = 0; i < n; i++)
        {
            int o = i * RailStride;
            if (s[o + 2] == RailEmpty) { if (free < 0) free = i; continue; }
            if (s[o] == col && s[o + 1] == row)                    // schon eines da
            {
                if (frame < 0) { s[o + 2] = RailEmpty; return i; }  // wegnehmen
                s[o + 2] = (byte)frame; s[o + 4] = (byte)line;
                return i;
            }
        }
        if (frame < 0 || free < 0) return -1;
        int at = free * RailStride;
        s[at] = (byte)col; s[at + 1] = (byte)row;
        s[at + 2] = (byte)frame; s[at + 3] = RailHp; s[at + 4] = (byte)line;
        return free;
    }

    /// <summary>Traegt diese Zelle ein Gleisstueck?</summary>
    public static bool HasRail(CwmFile m, int col, int row)
    {
        var s = m.Sec(22);
        if (s == null) return false;
        int n = Math.Min(RailSlots, s.Length / RailStride);
        for (int i = 0; i < n; i++)
        {
            int o = i * RailStride;
            if (s[o + 2] != RailEmpty && s[o] == col && s[o + 1] == row) return true;
        }
        return false;
    }

    /// <summary>
    /// WELCHES BILD EINE GLEISZELLE TRAEGT — aus ihren Nachbarn, nach der an
    /// <b>9846 Gleiszellen aller Karten</b> gemessenen Tafel (die Zahlen stehen
    /// bei <see cref="CwmExtra.RailCell"/>):
    /// <code>
    ///   0  links–rechts     1  oben–unten
    ///   2  rechts–unten     3  rechts–oben
    ///   4  links–unten      5  links–oben
    /// </code>
    ///
    /// <para><b>DIE VIER RAMPENBILDER 6..9 KOMMEN AUS DEM GELAENDEBYTE</b>
    /// (sec1 +3), und seit dem 14.08.2026 setzt der Editor sie:</para>
    /// <code>
    ///   links–rechts, Byte 3 -> 6   (Rampe, links hoeher)
    ///   links–rechts, Byte 1 -> 7   (Rampe, rechts hoeher)
    ///   oben–unten,   Byte 4 -> 8   (Rampe, oben hoeher)
    ///   oben–unten,   Byte 2 -> 9   (Rampe, unten hoeher)
    /// </code>
    ///
    /// <para>⚠ Hier stand »die setzt der Editor NICHT«, weil im Kopf von
    /// <see cref="CwmExtra.RailCell"/> nur EINE Richtung gemessen war: jede
    /// Zelle MIT Bild 6 hat Byte 3 (147 von 147). Das ist nicht dasselbe wie
    /// »jede waagerechte Zelle mit Byte 3 hat Bild 6«. <b>Die Umkehrung ist am
    /// 14.08.2026 nachgemessen</b>, ueber die acht gelieferten Karten mit
    /// Gleis:</para>
    /// <code>
    ///   L–R, Byte 3 -> Bild 6:  111 von 111
    ///   L–R, Byte 1 -> Bild 7:  148 von 150   (die zwei anderen sind 107 und
    ///                                          117, also ZERSTOERTE 7 —
    ///                                          bild % 10 == 7, siehe rail_hit
    ///                                          @0x4B0460)
    ///   T–B, Byte 4 -> Bild 8:  136 von 136
    ///   T–B, Byte 2 -> Bild 9:   94 von  94
    /// </code>
    /// <para>Kein Gegenbeispiel. Die Regel gilt also in beide Richtungen, und
    /// damit ist das Rampenbild <b>ableitbar</b> statt geraten. Es bleibt eine
    /// Sache des GELAENDES: wer eine Rampe will, malt die Stufe — der Editor
    /// erfindet keine.</para>
    ///
    /// <para>Ein Stueck ohne jeden Nachbarn bekommt 0 (links–rechts) — das
    /// Original haelt dafuer keine eigene Form bereit, und eine Zelle muss ein
    /// Bild haben.</para>
    /// </summary>
    /// <param name="flag">Das Gelaendebyte sec1 +3 dieser Zelle
    /// (<see cref="CwmFile.FlagAt"/>). 0 = eben, kein Rampenbild.</param>
    public static int RailFrame(bool left, bool right, bool up, bool down, int flag = 0)
    {
        if (left && right) return flag == 3 ? 6 : flag == 1 ? 7 : 0;
        if (up && down) return flag == 4 ? 8 : flag == 2 ? 9 : 1;
        if (right && down) return 2;
        if (right && up) return 3;
        if (left && down) return 4;
        if (left && up) return 5;
        if (up || down) return flag == 4 ? 8 : flag == 2 ? 9 : 1;
        return flag == 3 ? 6 : flag == 1 ? 7 : 0;
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

    // ========================================================================
    //  Gebaeude und Einheiten — was eine Karte SPIELBAR macht
    // ========================================================================
    //
    // ⚠ Bis heute schrieb der Editor NUR Marken, und deshalb hiess der Knopf
    // »Karte ansehen«: eine Karte mit 0 Einheiten und 0 Gebaeuden hat keinen
    // besetzten Platz (`SkirmishAi.StartSkirmish` gibt −1) und gilt im ersten
    // Takt als verloren (`MapEntityLayer.Verdict` ueber `AssetsOf(me) == 0`).
    //
    // Alle Feldlagen unten sind die der Leser in CwmData — sec3 fuer Gebaeude
    // (76 Byte, 300 Saetze), sec5 fuer Einheiten (78 Byte, 8000 Saetze in acht
    // Bloecken zu 1000, und der BLOCK ist der Besitzer). Die Werte selbst sind
    // an gelieferten Karten abgelesen; wo das der Fall ist, steht die Karte
    // dabei.

    /// <summary>Ab hier ist ein imap-Wert ein statischer Gegenstand und keine
    /// Gelaendeklasse — <see cref="CwmData.Terrain"/> liest alles ab 14000 als
    /// gesperrt, und Gebaeude tragen Griffe ab 50000
    /// (<c>CwmData.Building.FootW</c>).</summary>
    public const int StaticHandleBase = 50000;

    /// <summary>Der Griff, den ein Gebaeude in die imap stempelt. Der Leser
    /// verlangt nur »ab 50000 und unter 0xFFFC« und nimmt DIE ZELLEN MIT
    /// DEMSELBEN GRIFF als Grundflaeche; der Zahlenwert selbst wird nirgends
    /// aufgeloest (die <c>ref</c>-Angabe der <c>.entities.json</c> ist reine
    /// Auskunft). 51000 + Platz haelt Abstand zu den Griffen der
    /// Gelaendegegenstaende, die die gelieferten Karten ab 50000 vergeben.</summary>
    public static int BuildingHandle(int slot) => StaticHandleBase + 1000 + slot;

    /// <summary>
    /// Ein Gebaeude nach sec3 schreiben und seine Grundflaeche in die imap
    /// stempeln.
    ///
    /// <para><paramref name="doors"/> sind Zellenversaetze vom Anker. Die
    /// TUERZELLE bleibt in der imap FREI — dort kommen die gebauten Einheiten
    /// heraus (<c>col + door_col</c>, <c>row + door_row</c>, @0x410441), und in
    /// den gelieferten Karten ist sie in 511 von 519 Faellen frei.</para>
    ///
    /// <para>+0x18 (<c>IsBuilt</c>) MUSS 1 sein: der Gebaeudetakt @0x43CB29 tut
    /// ohne die Flagge nichts, und in den 23 Kampagnenkarten traegt sie jeder
    /// der 684 Saetze vom Typ 1..16 (0 Gegenbeispiele).</para>
    /// </summary>
    public static bool PutBuilding(CwmFile m, int slot, int type, int owner, int cisTyp,
                                   int col, int row, int footW, int footH,
                                   int hp, string name, (int Col, int Row)[] doors,
                                   int stockW = 0, int stockF = 0, int stockS = 0,
                                   int terranium = 0)
    {
        var s = m.Sec(3);
        if (s == null) return false;
        int o = slot * CwmData.BuildingStride;
        if (o + CwmData.BuildingStride > s.Length) return false;

        void U16(int at, int v) { s[o + at] = (byte)(v & 0xFF); s[o + at + 1] = (byte)((v >> 8) & 0xFF); }
        U16(0x00, col); U16(0x02, row);
        s[o + 0x04] = (byte)type;
        s[o + 0x05] = (byte)owner;
        U16(0x06, hp); U16(0x16, hp);
        s[o + 0x18] = 1;                       // IsBuilt — ohne die Flagge tot
        s[o + 0x19] = (byte)cisTyp;
        s[o + 0x1a] = (byte)cisTyp;            // rail: laufende Nummer, wie in map_02
        // der Name, 0x11 Byte, nullterminiert. Cp437 hat nur einen Leser, keinen
        // Schreiber; die Namen des Editors sind ASCII, alles darueber wird '?'.
        for (int k = 0; k < 0x11; k++)
            s[o + 0x1b + k] = k < name.Length && name[k] < 0x80 ? (byte)name[k]
                            : k < name.Length ? (byte)'?' : (byte)0;
        U16(0x2c, stockW); U16(0x2e, stockF); U16(0x30, stockS); U16(0x32, terranium);
        s[o + 0x34] = (byte)doors.Length;
        for (int d = 0; d < doors.Length && o + 0x35 + 3 * d + 2 < s.Length; d++)
        {
            s[o + 0x35 + 3 * d] = (byte)doors[d].Col;
            s[o + 0x36 + 3 * d] = (byte)doors[d].Row;
            s[o + 0x37 + 3 * d] = 0;           // Torzustand: 0 auf allen 798
        }
        s[o + 0x41] = 0;

        // die Grundflaeche in die imap, die Tuerzellen ausgenommen
        var imap = m.Sec(6);
        if (imap != null)
        {
            int handle = BuildingHandle(slot);
            for (int dr = 0; dr < footH; dr++)
                for (int dc = 0; dc < footW; dc++)
                {
                    bool door = false;
                    foreach (var d in doors) if (d.Col == dc && d.Row == dr) door = true;
                    if (door) continue;
                    int c = col + dc, r = row + dr;
                    if (c < 0 || c >= m.Width || r < 0 || r >= m.Height) continue;
                    int i = (c * ImapStride + r) * 2;
                    if (i + 1 >= imap.Length) continue;
                    imap[i] = (byte)(handle & 0xFF);
                    imap[i + 1] = (byte)(handle >> 8);
                }
        }
        return true;
    }

    /// <summary>
    /// Eine Einheit nach sec5 schreiben und ihre Zelle in die imap stempeln.
    ///
    /// <para>⚠ <b>Der Besitzer ist kein Feld, sondern der Block:</b> die
    /// Feindschaftstests @0x409bde und @0x40d058 und der Sprite-Zeichner
    /// @0x42a825 rechnen alle <c>index / 1000</c>. <paramref name="slot"/> ist
    /// darum <c>spieler * 1000 + nummer</c>.</para>
    ///
    /// <para>+0x09 (<c>Kind</c>) darf nicht 0xFF sein, das ist der Leerwert.
    /// Die Zahlen kommen von den gelieferten Karten: <c>code_base</c> = 0x2710
    /// auf allen, <c>kind</c>/<c>subclass</c> = 0 bei Bodenfahrzeugen.</para>
    /// </summary>
    public static bool PutEntity(CwmFile m, int slot, int col, int row, int unitType,
                                 int category, int weapon, int hp, int facing = 0)
    {
        var s = m.Sec(5);
        if (s == null) return false;
        int o = slot * CwmData.EntityStride;
        if (o + CwmData.EntityStride > s.Length) return false;
        Array.Clear(s, o, CwmData.EntityStride);

        s[o + 0x00] = (byte)col; s[o + 0x01] = (byte)row;
        s[o + 0x02] = (byte)facing; s[o + 0x03] = (byte)facing;
        s[o + 0x08] = 100;                     // Energie
        s[o + 0x09] = 0;                       // Kind: 0 = Bodenfahrzeug, NICHT 0xFF
        s[o + 0x0a] = 0;                       // Subclass
        s[o + 0x0c] = (byte)weapon;
        s[o + 0x0f] = (byte)unitType;
        s[o + 0x24] = 0x10; s[o + 0x25] = 0x27; // code_base 0x2710, auf allen Karten
        s[o + 0x26] = (byte)category;
        s[o + 0x29] = 100;                     // EnergieMax
        s[o + 0x2e] = (byte)(hp & 0xFF); s[o + 0x2f] = (byte)(hp >> 8);
        s[o + 0x30] = (byte)(hp & 0xFF); s[o + 0x31] = (byte)(hp >> 8);

        var imap = m.Sec(6);
        if (imap != null && col >= 0 && col < m.Width && row >= 0 && row < m.Height)
        {
            int i = (col * ImapStride + row) * 2;
            if (i + 1 < imap.Length)
            {
                imap[i] = (byte)(slot & 0xFF);
                imap[i + 1] = (byte)((slot >> 8) & 0xFF);
            }
        }
        return true;
    }
}
