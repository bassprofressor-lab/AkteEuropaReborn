namespace AkteEuropaReborn.Editor;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// <b>DER RUECKWEG — aus <c>map_*.json</c> und <c>map_*.entities.json</c> wieder
/// eine <see cref="CwmFile"/> im Arbeitsspeicher.</b>
///
/// <para>Bis heute konnte der Karteneditor nur die Karte bearbeiten, die
/// derselbe Lauf eben erzeugt hatte; die Grenze stand ausdruecklich in
/// <see cref="MapEditSession"/>. Diese Datei hebt sie auf. Der Weg ist die
/// Umkehrung von <c>ContentBuilder.ExportMap</c>: was <c>MapMeta</c> und
/// <see cref="EntitiesJson"/> hinausgeschrieben haben, wird hier Abschnitt fuer
/// Abschnitt wieder eingelesen. Danach haengt der ganze vorhandene Weg wieder
/// dran — der Pinsel <see cref="MapEditOverlay"/> arbeitet darauf und
/// <see cref="MapEditSession.Save"/> schreibt sie zurueck.</para>
///
/// <para><b>Warum nicht einfach die <c>.CWM</c> lesen?</b> Weil das nur fuer die
/// 49 gelieferten Karten ginge. Eine vom Editor erzeugte Karte hat keine
/// <c>.CWM</c> — sie besteht nur aus den drei geschriebenen Dateien. Genau die
/// will der Spieler wieder aufmachen, und darum ist der Rueckweg aus dem JSON
/// der einzige, der alle Karten erreicht. (Der Weg ueber die Originaldatei
/// existiert daneben weiter: <c>--map-new=…,quelle=…</c>.)</para>
///
/// <para><b>⚠ WAS DER EXPORT NICHT FUEHRT — gemessen, nicht vermutet.</b> Diese
/// Felder stehen in KEINER der zwei Dateien und koennen darum auch nicht
/// zurueckkommen. Gemerkt daran, dass sie in <c>ContentBuilder.MapMeta</c> und
/// <see cref="EntitiesJson.Write"/> nirgends vorkommen:</para>
/// <list type="bullet">
///   <item><b>Die Missionsnummer</b> (<c>CwmFile.MissionNumber</c>, der
///     Kampagnenzaehler <c>word[0x539934]</c>, 1..15 und 51..58). Eine geoeffnete
///     Karte bekommt darum 0 — dieselbe <b>UNSERE SETZUNG</b>, die
///     <c>CwmFile.Create</c> fuer eine neu erzeugte Karte trifft.</item>
///   <item><b>Der Dateikommentar</b> (<c>CwmFile.Comment</c>, 21 Byte im Kopf).</item>
///   <item><b>Die fuenf belegten, aber ungelesenen Abschnitte</b> sec18, sec20,
///     sec21, sec25 und sec32. Sie werden von keinem Leser angefasst, stehen in
///     keiner Ausgabe und kommen als Null zurueck.</item>
///   <item><b>Jedes Byte eines Satzes, das kein Leser liest</b> — zum Beispiel
///     die Gebaeudebytes +0x08..+0x15 und +0x42..+0x4b, oder das dritte Byte
///     jeder Tuer (Torzustand). Fuer den Rundlauf DER AUSGABE macht das nichts
///     (was niemand liest, schreibt auch niemand hinaus); fuer eine
///     zurueckgeschriebene <c>.CWM</c> waere es ein Verlust — nur schreibt
///     dieses Projekt keine <c>.CWM</c>, siehe <see cref="CwmFile"/>.</item>
/// </list>
///
/// <para><b>Was NICHT verloren geht, obwohl man es vermuten koennte:</b> die
/// imap (sec6) steht vollstaendig in der Ausgabe, nur auf zwei Bloecke verteilt
/// — <c>spatial.nonempty</c> nennt jede Zelle unter 0xFFFC mit ihrem GENAUEN
/// Wert, und <c>terrain.rle</c> deckt den Rest, dessen vier Klassen umkehrbar
/// auf 0xFFFE/0xFFFD/0xFFFC/0xFFFF fuehren. Einheiten und Marken tragen ihre
/// ganzen Rohbytes (<c>raw</c>) mit. Der einzige Teil der imap, den niemand
/// hinausschreibt, ist die Flaeche AUSSERHALB von Breite x Hoehe; sie kommt als
/// 0xFFFF zurueck, so wie <see cref="MapFactory.Empty"/> sie anlegt.</para>
/// </summary>
public static class MapOpen
{
    /// <summary>Wo die drei Kartendateien liegen — derselbe Ordner, in den
    /// <see cref="MapGenerator.Write"/> schreibt.</summary>
    public static string MapsDir
        => ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/Maps";

    /// <summary>Der volle Dateiname, mit <c>map_</c> davor.</summary>
    public static string FullName(string name) => name.StartsWith("map_") ? name : "map_" + name;

    /// <summary>
    /// Eine geschriebene Karte wieder in den Arbeitsspeicher holen.
    /// </summary>
    /// <param name="name">Der Kartenname, mit oder ohne <c>map_</c>.</param>
    /// <returns>null, wenn eine der zwei Dateien fehlt oder nicht zu lesen ist.</returns>
    public static CwmFile? Load(string name, Action<string> say)
    {
        string outName = FullName(name);
        string dir = MapsDir;
        string metaPath = $"{dir}/{outName}.json";
        string entPath = $"{dir}/{outName}.entities.json";
        foreach (string p in new[] { metaPath, entPath })
            if (!File.Exists(p)) { say($"oeffnen: FEHLT {p}"); return null; }

        using var metaDoc = JsonDocument.Parse(File.ReadAllText(metaPath));
        using var entDoc = JsonDocument.Parse(File.ReadAllText(entPath));
        var meta = metaDoc.RootElement;
        var ent = entDoc.RootElement;

        int w = I(meta, "width"), h = I(meta, "height"), ts = I(meta, "tileset");
        if (w <= 0 || h <= 0) { say($"oeffnen: {outName} nennt die Groesse {w}x{h}"); return null; }

        // ---- wie viele Abschnitte? -----------------------------------------
        // ⚠ Das ist keine Kosmetik, sondern der Unterschied zwischen »leer« und
        // »acht Nullen«: `CwmExtra.Money` und `CwmExtra.Players` liefern aus
        // einem NULLGEFUELLTEN sec73/sec53 acht Saetze statt einer leeren Liste.
        // Wer einer Kampagnenkarte (38 Abschnitte) alle 131 gaebe, bekaeme beim
        // Zurueckschreiben also acht Spieler und acht Kontostaende geschenkt,
        // die die Originalausgabe nicht hat. Umgekehrt braucht ein Spielstand
        // sie. Entschieden wird darum am INHALT: traegt die Ausgabe irgendetwas
        // aus dem Bereich hinter sec38, ist es ein Spielstand und bekommt alle.
        bool full = Len(ent, "money") > 0 || Len(ent, "players") > 0
                 || Len(ent, "targets") > 0 || Len(ent, "progress") > 0
                 || Len(ent, "air_designs") > 0 || Len(ent, "ship_designs") > 0
                 || Len(ent, "trains") > 0;
        int sections = full ? CwmSections.Count : CwmFile.CwmSectionCount;

        var m = CwmFile.Create(w, h, ts, Str(meta, "map"), Str(meta, "mission"), sections);
        // Die drei Abschnitte mit eigenem Leerwert — sonst liefert eine leere
        // Karte 8000 Einheiten, 2000 Marken und 3000 Gleisstuecke.
        MarkEmpty(m, 5, CwmData.EntityStride, 0x09);
        MarkEmpty(m, 4, CwmData.MarkerStride, 0x02);
        MarkEmpty(m, 22, CwmExtra.RailStride, 0x02);

        int cells = Tiles(m, meta);
        int imapCells = Imap(m, ent);
        int zoneCells = Zones(m, ent);
        int units = Entities(m, ent);
        int marks = Markers(m, ent);
        int rails = Rails(m, ent);
        int blds = Buildings(m, ent);
        int inf = Infantry(m, ent);
        int spec = Specials(m, ent);
        int dep = Deposits(m, ent);
        int inst = Instances(m, ent);
        int nodes = RailNodes(m, ent);
        int links = Links(m, ent);
        int terra = Terra(m, meta);
        int rest = full ? Full(m, ent) : 0;
        Rescue(m, ent, say);

        say($"oeffnen {outName}: {w}x{h}, Kachelsatz {ts:00}, {sections} Abschnitte " +
            (full ? "(Spielstand — die Ausgabe traegt Felder hinter sec38)"
                  : "(Leveldatei — nichts hinter sec38 in der Ausgabe)"));
        say($"  sec1 {cells} Zellen, sec6 {imapCells} imap-Zellen, sec2 {zoneCells} Zonen, " +
            $"sec5 {units} Einheiten, sec4 {marks} Marken, sec3 {blds} Gebaeude, " +
            $"sec22 {rails} Gleiszellen");
        say($"  sec16 {inf} Infanteriezellen, sec19 {spec} Sondersaetze, sec28 {dep} Vorkommen, " +
            $"{inst} Gebaeudesaetze mit Zustand, sec33 {nodes} Gleisknoten, sec34 {links} Linien" +
            (terra > 0 ? $", {terra} Rohstoffvorkommen (aus »terra« der .json)" : "") +
            (full ? $", {rest} Saetze hinter sec38" : ""));
        return m;
    }

    // =======================================================================
    //  sec1 — der Zellensatz. Kachelcode, Hoehe, Gelaendebyte.
    // =======================================================================

    /// <summary>Aus <c>tiles</c> der <c>map_*.json</c>. Verlustfrei: die drei
    /// Zahlen SIND die vier Byte des Satzes (<c>code</c> als u16).</summary>
    private static int Tiles(CwmFile m, JsonElement meta)
    {
        int n = 0;
        if (!meta.TryGetProperty("tiles", out var tiles)) return 0;
        foreach (var t in tiles.EnumerateArray())
        {
            m.SetCell(I(t, "col"), I(t, "row"), I(t, "code"), I(t, "elev"), I(t, "flag"));
            n++;
        }
        return n;
    }

    /// <summary>Die Rohstoffvorkommen — nur eine vom Editor erzeugte Karte hat
    /// sie, siehe <see cref="CwmFile.Terra"/>.</summary>
    private static int Terra(CwmFile m, JsonElement meta)
    {
        if (!meta.TryGetProperty("terra", out var arr)) return 0;
        foreach (var e in arr.EnumerateArray())
        {
            var it = e.EnumerateArray();
            it.MoveNext(); int c = it.Current.GetInt32();
            it.MoveNext(); int r = it.Current.GetInt32();
            it.MoveNext(); int a = it.Current.GetInt32();
            m.Terra.Add((c, r, a));
        }
        return m.Terra.Count;
    }

    // =======================================================================
    //  sec6 — die imap. ZWEI Bloecke der Ausgabe, ein Abschnitt.
    // =======================================================================

    /// <summary>
    /// Die imap aus <c>terrain.rle</c> und <c>spatial.nonempty</c>.
    ///
    /// <para>Der Reihenfolge nach: erst traegt jede Zelle den Wert ihrer
    /// GELAENDEKLASSE (0→0xFFFE frei, 1→0xFFFD rau, 2→0xFFFC Wasser, 3→0xFFFF
    /// gesperrt), dann wird jede Zelle mit einem GRIFF darueber geschrieben.
    /// Das ist genau die Umkehrung von <see cref="CwmData.Terrain"/>, die einen
    /// Griff ja erst zur Klasse macht — <c>spatial.nonempty</c> haelt den
    /// Zahlenwert selbst fest, und darum geht der Weg zurueck.</para>
    ///
    /// <para>⚠ Ausserhalb von Breite x Hoehe bleibt 0xFFFF stehen. Die Ausgabe
    /// nennt diese Flaeche nicht, und 0xFFFF ist der vorsichtige Wert: 0x0000
    /// waere der Einheitenplatz 0.</para>
    /// </summary>
    private static int Imap(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(6);
        if (s == null) return 0;
        for (int i = 0; i < s.Length; i++) s[i] = 0xFF;

        int w = m.Width, h = m.Height, n = 0;
        if (ent.TryGetProperty("terrain", out var terr) && terr.TryGetProperty("rle", out var rle))
        {
            int at = 0;
            foreach (var pair in rle.EnumerateArray())
            {
                var it = pair.EnumerateArray();
                it.MoveNext(); int v = it.Current.GetInt32();
                it.MoveNext(); int run = it.Current.GetInt32();
                int val = v switch
                {
                    0 => MapFactory.ImapFree, 1 => MapFactory.ImapRough,
                    2 => MapFactory.ImapWater, _ => MapFactory.ImapBlocked,
                };
                for (int k = 0; k < run && at < w * h; k++, at++)
                {
                    int col = at % w, row = at / w;          // die rle laeuft ZEILENWEISE
                    Put16(s, (col * MapFactory.ImapStride + row) * 2, val);
                    n++;
                }
            }
        }
        if (ent.TryGetProperty("spatial", out var sp) && sp.TryGetProperty("nonempty", out var ne))
            foreach (var c in ne.EnumerateArray())
                Put16(s, (I(c, "col") * MapFactory.ImapStride + I(c, "row")) * 2, I(c, "value"));
        return n;
    }

    // =======================================================================
    //  sec2 — das Zonenraster
    // =======================================================================

    private static int Zones(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(2);
        if (s == null || !ent.TryGetProperty("zones", out var z) ||
            !z.TryGetProperty("grid", out var grid)) return 0;
        int row = 0, n = 0;
        foreach (var line in grid.EnumerateArray())
        {
            int col = 0;
            foreach (var v in line.EnumerateArray())
            {
                int i = col * MapFactory.ZoneStride + row;
                if (i < s.Length) { s[i] = (byte)v.GetInt32(); n++; }
                col++;
            }
            row++;
        }
        return n;
    }

    // =======================================================================
    //  sec5 / sec4 / sec22 — was seine ROHBYTES mitbringt
    // =======================================================================

    /// <summary>Die Einheiten. <c>raw</c> ist der GANZE Satz von 78 Byte —
    /// verlustfrei, jedes Byte.</summary>
    private static int Entities(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(5);
        if (s == null || !ent.TryGetProperty("entities", out var arr)) return 0;
        int n = 0;
        foreach (var e in arr.EnumerateArray())
        {
            int o = I(e, "slot") * CwmData.EntityStride;
            if (o < 0 || o + CwmData.EntityStride > s.Length) continue;
            if (!Hex(Str(e, "raw"), s, o, CwmData.EntityStride)) continue;
            n++;
        }
        return n;
    }

    /// <summary>Die Marken. <c>raw</c> ist der ganze Satz von 6 Byte.</summary>
    private static int Markers(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(4);
        if (s == null || !ent.TryGetProperty("markers", out var arr)) return 0;
        int n = 0;
        foreach (var e in arr.EnumerateArray())
        {
            int o = I(e, "slot") * CwmData.MarkerStride;
            if (o < 0 || o + CwmData.MarkerStride > s.Length) continue;
            if (!Hex(Str(e, "raw"), s, o, CwmData.MarkerStride)) continue;
            n++;
        }
        return n;
    }

    /// <summary>Das Gleis. Kurz geschrieben, aber vollstaendig: die fuenf Zahlen
    /// je Zelle SIND die fuenf Byte (Spalte, Zeile, Bild, TP, Linie).</summary>
    private static int Rails(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(22);
        if (s == null || !ent.TryGetProperty("rail_cells", out var arr)) return 0;
        int n = 0;
        foreach (var e in arr.EnumerateArray())
        {
            var it = e.EnumerateArray();
            it.MoveNext(); int idx = it.Current.GetInt32();
            it.MoveNext(); int col = it.Current.GetInt32();
            it.MoveNext(); int row = it.Current.GetInt32();
            it.MoveNext(); int frame = it.Current.GetInt32();
            it.MoveNext(); int line = it.Current.GetInt32();
            it.MoveNext(); int hp = it.Current.GetInt32();
            int o = idx * CwmExtra.RailStride;
            if (o < 0 || o + CwmExtra.RailStride > s.Length) continue;
            s[o] = (byte)col; s[o + 1] = (byte)row; s[o + 2] = (byte)frame;
            s[o + 3] = (byte)hp; s[o + 4] = (byte)line;
            n++;
        }
        return n;
    }

    // =======================================================================
    //  sec3 — die Gebaeude
    // =======================================================================

    /// <summary>
    /// Die Gebaeude, Feld fuer Feld — der einzige der grossen Abschnitte, dessen
    /// Ausgabe KEINE Rohbytes fuehrt.
    ///
    /// <para>Geschrieben werden genau die Stellen, die <see cref="CwmData"/>
    /// wieder liest; die Feldlagen sind die von
    /// <see cref="MapFactory.PutBuilding"/>. Was dazwischen liegt und niemand
    /// liest (+0x08..+0x15, +0x17, +0x42..+0x4b, das dritte Byte jeder Tuer),
    /// kommt als Null zurueck — <b>fuer den Rundlauf der Ausgabe folgenlos, fuer
    /// eine <c>.CWM</c> waere es ein Verlust</b>.</para>
    ///
    /// <para>⚠ Die imap wird hier NICHT gestempelt. Sie steht schon vollstaendig
    /// aus <see cref="Imap"/> — samt der Gebaeudegriffe, aus denen
    /// <c>CwmData.Footprint</c> die Grundflaeche liest. Wer hier noch einmal
    /// stempelte, ueberschriebe die echten Griffe mit
    /// <c>MapFactory.BuildingHandle</c> und daemmte damit fremde
    /// Gebaeudeflaechen ein.</para>
    /// </summary>
    private static int Buildings(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(3);
        if (s == null || !ent.TryGetProperty("buildings", out var arr)) return 0;
        int n = 0;
        foreach (var b in arr.EnumerateArray())
        {
            int o = I(b, "slot") * CwmData.BuildingStride;
            if (o < 0 || o + CwmData.BuildingStride > s.Length) continue;
            Put16(s, o + 0x00, I(b, "col"));
            Put16(s, o + 0x02, I(b, "row"));
            s[o + 0x04] = (byte)I(b, "type");
            s[o + 0x05] = (byte)I(b, "owner");
            Put16(s, o + 0x06, I(b, "hp"));
            Put16(s, o + 0x16, I(b, "hp_max"));
            s[o + 0x18] = (byte)I(b, "built");
            s[o + 0x19] = (byte)I(b, "cis_typ");
            s[o + 0x1a] = (byte)I(b, "rail");
            Name(s, o + 0x1b, 0x11, Str(b, "name"));
            Put16(s, o + 0x2c, I(b, "w"));
            Put16(s, o + 0x2e, I(b, "ch"));
            Put16(s, o + 0x30, I(b, "sp"));
            Put16(s, o + 0x32, I(b, "terranium"));
            int doors = I(b, "doors");
            s[o + 0x34] = (byte)doors;
            if (b.TryGetProperty("door_cells", out var dc))
            {
                int d = 0;
                foreach (var cell in dc.EnumerateArray())
                {
                    if (o + 0x35 + 3 * d + 2 >= s.Length) break;
                    s[o + 0x35 + 3 * d] = (byte)I(cell, "col");
                    s[o + 0x36 + 3 * d] = (byte)I(cell, "row");
                    d++;
                }
            }
            s[o + 0x41] = (byte)I(b, "ident");
            n++;
        }
        return n;
    }

    /// <summary>Zu welchem Abschnitt und welcher Satzlaenge eine Gebaeudeart
    /// ihren Zustandssatz fuehrt — dieselbe Tafel wie
    /// <c>CwmData.InstanceSection</c>, die dort privat ist.
    /// ⚠ Doppelt gefuehrtes Wissen; gehoerte oeffentlich nach
    /// <c>Scripts/Import/CwmData.cs</c>, an der hier nicht geschrieben wird.</summary>
    private static readonly Dictionary<int, (int Section, int Stride)> InstanceSection = new()
    {
        { 1, (23, 16) },  { 2, (24, 14) },  { 3, (24, 14) },  { 4, (24, 14) },
        { 6, (30, 14) },  { 7, (26, 4) },   { 9, (27, 52) },  { 10, (28, 18) },
        { 11, (29, 4) },  { 12, (30, 14) }, { 13, (31, 4) },  { 15, (28, 18) },
    };

    /// <summary>
    /// Der Zustandssatz eines Gebaeudes — Status, Foerderchance, Lagerplatz,
    /// Preise, Hangar, Schiffswerft. Er liegt NICHT bei den Gebaeuden, sondern
    /// in einem Abschnitt je Art, indiziert mit <c>cis_typ</c>.
    ///
    /// <para>⚠ <b>sec28 wird zweimal beschrieben</b>, von hier und von
    /// <see cref="Deposits"/> — die Terraniummine fuehrt ihren Zustand und ihr
    /// Vorkommen im SELBEN Satz. Die Reihenfolge ist darum festgelegt: erst die
    /// Vorkommen (die den Rueckverweis +0x00 setzen), dann die Zustaende (die
    /// +0x03..+0x06 und die Preise setzen). Der Lagerplatz +0x08 steht in beiden
    /// und ist derselbe Wert.</para>
    /// </summary>
    private static int Instances(CwmFile m, JsonElement ent)
    {
        if (!ent.TryGetProperty("buildings", out var arr)) return 0;
        int n = 0;
        foreach (var b in arr.EnumerateArray())
        {
            int typ = I(b, "type"), cis = I(b, "cis_typ");
            if (cis > 49 || !InstanceSection.TryGetValue(typ, out var e)) continue;
            var s = m.Sec(e.Section);
            if (s == null || (cis + 1) * e.Stride > s.Length) continue;
            int o = cis * e.Stride;
            bool touched = false;

            // sec29: +0x02 ist NICHT der Status, sondern die Schiffswerft
            if (b.TryGetProperty("shipyard", out var sy))
            { s[o + 0x02] = (byte)sy.GetInt32(); touched = true; }
            else if (b.TryGetProperty("state", out var st))
            { s[o + 0x02] = (byte)st.GetInt32(); touched = true; }

            if (b.TryGetProperty("eff_num", out _))
            {
                s[o + 0x03] = (byte)I(b, "eff_num");
                s[o + 0x04] = (byte)I(b, "eff_den");
                s[o + 0x05] = (byte)I(b, "prod_speed");
                s[o + 0x06] = (byte)I(b, "upgrade_step");
                Put16(s, o + 0x08, I(b, "capacity"));
                // Fabrik +0x0a/+0x0c, Mine +0x0e/+0x10 — der Missionsvorlauf
                // @0x440887 setzt beide mit denselben zwei Zahlen, nur zwei
                // Felder auseinander.
                bool mine = typ is 10 or 15;
                Put16(s, o + (mine ? 0x0e : 0x0a), I(b, "cost_store"));
                Put16(s, o + (mine ? 0x10 : 0x0c), I(b, "cost_prod"));
                touched = true;
            }

            if (b.TryGetProperty("hangar_size", out var hs))
            {
                s[o + 0x03] = (byte)hs.GetInt32();
                int k = 0;
                if (b.TryGetProperty("hangar", out var hg))
                    foreach (var v in hg.EnumerateArray())
                    {
                        if (o + 0x0b + k >= s.Length || k >= 52 - 0x0b) break;
                        s[o + 0x0b + k] = (byte)v.GetInt32();
                        k++;
                    }
                s[o + 0x04] = (byte)k;
                touched = true;
            }
            if (touched) n++;
        }
        return n;
    }

    /// <summary>sec28 — die Terraniumvorkommen. Muss VOR
    /// <see cref="Instances"/> laufen, siehe dort.</summary>
    private static int Deposits(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(28);
        if (s == null || !ent.TryGetProperty("deposits", out var arr)) return 0;
        int n = 0;
        foreach (var d in arr.EnumerateArray())
        {
            int o = I(d, "slot") * CwmExtra.DepositStride;
            if (o < 0 || o + CwmExtra.DepositStride > s.Length) continue;
            Put16(s, o + 0x00, I(d, "building"));
            Put16(s, o + 0x08, I(d, "capacity"));
            Put16(s, o + 0x0a, I(d, "grade"));
            Put16(s, o + 0x0c, I(d, "terranium"));
            n++;
        }
        return n;
    }

    // =======================================================================
    //  sec16 / sec19 — Infanteriezellen und die Sondereinheiten
    // =======================================================================

    /// <summary>Die Infanteriezellen. ⚠ Die leeren Plaetze werden auf 0xFFFF
    /// gesetzt, nicht auf 0: der Leser nimmt jeden Wert unter 8000 als besetzten
    /// Platz, und 0 waere der Einheitenplatz 0.</summary>
    private static int Infantry(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(16);
        if (s == null || !ent.TryGetProperty("infantry_cells", out var arr)) return 0;
        int n = 0;
        foreach (var c in arr.EnumerateArray())
        {
            int o = I(c, "index") * 22;
            if (o < 0 || o + 22 > s.Length) continue;
            s[o] = (byte)I(c, "col"); s[o + 1] = (byte)I(c, "row");
            for (int k = 0; k < 9; k++) Put16(s, o + 2 + k * 2, 0xFFFF);
            int j = 0;
            if (c.TryGetProperty("slots", out var sl))
                foreach (var v in sl.EnumerateArray())
                {
                    if (j >= 9) break;
                    Put16(s, o + 2 + j * 2, v.GetInt32());
                    j++;
                }
            n++;
        }
        return n;
    }

    /// <summary>sec19 — Flugzeuge und Nachschubhelikopter. Die Ausgabe nennt
    /// jedes Feld, das der Leser liest; die uebrigen 30 Byte des 68er-Satzes
    /// kommen als Null zurueck.</summary>
    private static int Specials(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(19);
        if (s == null || !ent.TryGetProperty("special", out var arr)) return 0;
        int n = 0;
        foreach (var e in arr.EnumerateArray())
        {
            int o = I(e, "slot") * CwmExtra.SpecialStride;
            if (o < 0 || o + CwmExtra.SpecialStride > s.Length) continue;
            Put16(s, o + 0x00, I(e, "col"));
            Put16(s, o + 0x02, I(e, "row"));
            s[o + 0x08] = (byte)I(e, "kind");
            s[o + 0x09] = (byte)I(e, "owner");
            s[o + 0x0d] = (byte)I(e, "speed");
            s[o + 0x16] = (byte)I(e, "ammo");
            s[o + 0x17] = (byte)I(e, "ammo_max");
            s[o + 0x19] = (byte)I(e, "hp");
            s[o + 0x1a] = (byte)I(e, "hp_max");
            Put16(s, o + 0x1c, I(e, "fuel"));
            Put16(s, o + 0x1e, I(e, "fuel_max"));
            s[o + 0x20] = (byte)I(e, "payload");
            s[o + 0x21] = (byte)I(e, "airframe");
            s[o + 0x22] = (byte)I(e, "attack");
            s[o + 0x23] = (byte)I(e, "defence");
            s[o + 0x24] = (byte)I(e, "sight");
            Put16(s, o + 0x2e, I(e, "customer"));
            s[o + 0x31] = (byte)I(e, "cargo");
            Name(s, o + 0x3b, CwmExtra.SpecialStride - 0x3b, Str(e, "name"));
            n++;
        }
        return n;
    }

    // =======================================================================
    //  sec33 / sec34 / sec122 — das Gleisnetz
    // =======================================================================

    /// <summary>sec33 — die Knotentafel.
    ///
    /// <para>⚠⚠ <b>20.08.2026 — DIESE ROUTINE HAT DEN GEBÄUDETYP ZERSTÖRT.</b>
    /// Hier stand »die fuenf Verweisplaetze +0x02..+0x06«, und danach
    /// <c>for (k = 2; k &lt; 7; k++) s[o+k] = 0xFF;</c> mit Anschlüssen ab
    /// <c>j = 2</c>. <c>+0x02</c> ist aber der <b>Gebäudetyp</b>, es gibt nur
    /// <b>vier</b> Anschlüsse (<c>+0x03..+0x06</c>) — belegt am Anleger
    /// <c>AllocNode</c> C <c>0x4B00A0</c>, siehe
    /// <see cref="Import.CwmExtra.RailNode"/>.</para>
    ///
    /// <para>Jedes Speichern schrieb dem Knoten also <b>0xFF</b> als Typ, oder
    /// die erste Liniennummer. Das ist die schlimmere Hälfte des Fehlerpaares:
    /// beim Lesen entstand nur eine erfundene Linie, hier ging eine echte
    /// Angabe <b>verloren</b> — und zwar in die Datei hinein. Der Typ 0 heisst
    /// für das Original »Satz frei«; ein Knoten mit 0xFF ist weder frei noch
    /// gültig, und <c>AllocNode</c> gibt ihn nie wieder aus.</para>
    ///
    /// <para>⚠ <c>0xFF</c> bleibt für die <b>Anschlüsse</b> richtig: dort ist
    /// 0 die gültige Linie 0, ein Leerwert muss also 0xFF sein. Nur eben nicht
    /// für +0x02.</para></summary>
    private static int RailNodes(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(33);
        if (s == null || !ent.TryGetProperty("rail_nodes", out var arr)) return 0;
        int n = 0;
        foreach (var e in arr.EnumerateArray())
        {
            int o = I(e, "node") * 8;
            if (o < 0 || o + 8 > s.Length) continue;
            Put16(s, o, I(e, "building"));
            // +0x02 = Gebaeudetyp. Steht er im JSON, kommt er von dort; sonst
            // bleibt stehen, was schon da ist. ⚠ NICHT ueberschreiben, wenn
            // wir ihn nicht kennen — lieber der alte Wert als 0xFF.
            if (e.TryGetProperty("type", out var ty)) s[o + 2] = (byte)ty.GetInt32();
            // Nur die VIER Anschluesse leeren.
            for (int k = 3; k < 7; k++) s[o + k] = 0xFF;
            int j = 3;
            if (e.TryGetProperty("links", out var ls))
                foreach (var v in ls.EnumerateArray())
                {
                    if (j > 6) break;
                    s[o + j] = (byte)v.GetInt32();
                    j++;
                }
            n++;
        }
        return n;
    }

    /// <summary>
    /// sec34 — die Bahnlinien, und der eine Abschnitt, dessen Rueckweg wirklich
    /// gerechnet werden muss.
    ///
    /// <para>Die Ausgabe nennt die Strecke nicht als Codes, sondern als GEGANGENE
    /// Punkte (<c>route</c>) und als Gleisteile (<c>pieces</c>). Der Weg zurueck
    /// geht ueber <c>CwmExtra.SpojStep</c>: aus zwei aufeinanderfolgenden
    /// Punkten fallen <c>dx</c> und <c>dy</c> (in halben Zeilen) heraus.</para>
    ///
    /// <para>⚠ <b>Die Schritttafel ist NICHT eindeutig</b> — fuenf Werte kommen
    /// doppelt vor: (0,1) unter Code 4 und 9, (0,−1) unter 7 und 11, (1,0) unter
    /// 2 und 12, (−1,−1) unter 5 und 14, (0,0) unter 13 und 15. Das GLEISTEIL
    /// entscheidet: <c>SpojPiece</c> gibt 7 gegen 1, 5 gegen 3, 6 gegen 0 und 3
    /// gegen 0 — vier der fuenf Paare sind damit getrennt. Das fuenfte (13
    /// gegen 15) traegt in beiden Faellen Schritt (0,0) und Teil 0, ist also in
    /// der Ausgabe gar nicht unterscheidbar; genommen wird 13.</para>
    /// </summary>
    private static int Links(CwmFile m, JsonElement ent)
    {
        var s = m.Sec(34);
        if (s == null || !ent.TryGetProperty("links", out var arr)) return 0;
        var s122 = m.Sec(122);
        int n = 0;
        foreach (var e in arr.EnumerateArray())
        {
            int o = I(e, "slot") * CwmExtra.SpojStride;
            if (o < 0 || o + CwmExtra.SpojStride > s.Length) continue;
            int y1 = I(e, "y1"), y2 = I(e, "y2");
            s[o + 0] = (byte)I(e, "node1");
            s[o + 1] = (byte)I(e, "node2");
            s[o + 2] = (byte)I(e, "x1");
            s[o + 3] = (byte)(y1 & 0xFF);
            s[o + 4] = (byte)I(e, "x2");
            s[o + 5] = (byte)(y2 & 0xFF);
            s[o + 0x0c] = (byte)I(e, "delka");
            s[o + 0xd5] = (byte)I(e, "faze");
            if (s122 != null)
            {
                int r = I(e, "slot") * 4;
                if (r + 4 <= s122.Length) { Put16(s122, r, y1); Put16(s122, r + 2, y2); }
            }
            Route(s, o, e);
            n++;
        }
        return n;
    }

    /// <summary>Die Streckencodes aus <c>route</c> und <c>pieces</c>
    /// zurueckrechnen — siehe <see cref="Links"/>.</summary>
    private static void Route(byte[] s, int o, JsonElement e)
    {
        if (!e.TryGetProperty("route", out var route)) return;
        var xs = new List<int>();
        var ys = new List<int>();          // in HALBEN Zeilen
        foreach (var p in route.EnumerateArray())
        {
            var it = p.EnumerateArray();
            it.MoveNext(); xs.Add(it.Current.GetInt32());
            it.MoveNext(); ys.Add((int)Math.Round(it.Current.GetDouble() * 2));
        }
        var pieces = new List<int>();
        if (e.TryGetProperty("pieces", out var pc))
            foreach (var v in pc.EnumerateArray()) pieces.Add(v.GetInt32());

        for (int k = 0; k + 1 < xs.Count; k++)
        {
            if (o + 0x0d + k >= o + CwmExtra.SpojStride) break;
            int dx = xs[k + 1] - xs[k], dy = ys[k + 1] - ys[k];
            int want = k + 1 < pieces.Count ? pieces[k + 1] : -1;
            int code = -1;
            // ⚠ 20.08.2026 — hier stand `c < 16`, und das konnte beim
            // SCHREIBEN einer Karte einen Routencode 12..15 erzeugen. Die
            // Tafel des Originals hat zwoelf Eintraege; 12..15 waren
            // Fremdbytes der naechsten Tafel. Siehe CwmExtra.SpojStep.
            for (int c = 0; c < CwmExtra.SpojCodeCount; c++)
                if (CwmExtra.SpojStep[c].Dx == dx && CwmExtra.SpojStep[c].Dy == dy &&
                    (want < 0 || CwmExtra.SpojPiece[c] == want))
                { code = c; break; }
            s[o + 0x0d + k] = (byte)(code < 0 ? 0 : code);
        }
    }

    // =======================================================================
    //  hinter sec38 — was nur ein Spielstand fuehrt
    // =======================================================================

    /// <summary>Die Abschnitte, die eine <c>.CWM</c> gar nicht hat: Ziele,
    /// Kontostaende, Fortschritt, Spieler, Bauplaene, Zuege.</summary>
    private static int Full(CwmFile m, JsonElement ent)
    {
        int n = 0;

        if (m.Sec(69) is { } s69 && ent.TryGetProperty("targets", out var tg))
            foreach (var t in tg.EnumerateArray())
            {
                int o = (I(t, "player") * CwmExtra.TargetsPerPlayer + I(t, "slot")) * CwmExtra.TargetStride;
                if (o < 0 || o + CwmExtra.TargetStride > s69.Length) continue;
                s69[o] = (byte)I(t, "type");
                s69[o + 1] = (byte)I(t, "importance");
                Put16(s69, o + 2, I(t, "building"));
                s69[o + 4] = (byte)I(t, "destroyed");
                n++;
            }

        if (m.Sec(73) is { } s73 && ent.TryGetProperty("money", out var mo))
        {
            int i = 0;
            foreach (var v in mo.EnumerateArray())
            {
                if (i * 4 + 4 > s73.Length) break;
                Put32(s73, i * 4, v.GetInt32());
                i++; n++;
            }
        }

        if (m.Sec(96) is { } s96 && ent.TryGetProperty("progress", out var pr))
            foreach (var p in pr.EnumerateArray())
            {
                int o = I(p, "slot") * 16;
                if (o < 0 || o + 16 > s96.Length) continue;
                if (Hex(Str(p, "raw"), s96, o, 16)) n++;
            }

        if (m.Sec(53) is { } s53 && ent.TryGetProperty("players", out var pl))
            foreach (var p in pl.EnumerateArray())
            {
                int o = I(p, "player") * 40;
                if (o < 0 || o + 40 > s53.Length) continue;
                s53[o] = (byte)I(p, "flag");
                Name(s53, o + 1, 6, Str(p, "name"));
                Name(s53, o + 7, 0x15 - 7, Str(p, "comment"));
                if (p.TryGetProperty("allies", out var al))
                    foreach (var v in al.EnumerateArray())
                    {
                        int j = v.GetInt32();
                        if (j >= 0 && j < 8) s53[o + 0x15 + j] = 1;
                    }
                Put32(s53, o + 0x20, I(p, "kills"));
                Put32(s53, o + 0x24, I(p, "losses"));
                n++;
            }

        // ⚠ sec120: der KURZNAME +0x16 ist 12 Byte lang, die drei PREISE liegen
        // aber auf +0x1f/+0x20/+0x21 — also MITTEN im Kurznamen. Der Leser
        // bricht am ersten Nullbyte ab, ein Kurzname bis 8 Zeichen ist darum
        // heil. Ein laengerer wuerde von den Preisen abgeschnitten; das zaehlt
        // MapRound und nennt die Zahl, statt es zu verschweigen.
        if (m.Sec(120) is { } s120 && ent.TryGetProperty("air_designs", out var ad))
            foreach (var a in ad.EnumerateArray())
            {
                int o = I(a, "slot") * 48;
                if (o < 0 || o + 48 > s120.Length) continue;
                s120[o] = (byte)I(a, "enable");
                Name(s120, o + 0x01, 0x15, Str(a, "name"));
                Name(s120, o + 0x16, 12, Str(a, "short"));
                s120[o + 0x1f] = (byte)I(a, "cost_w");
                s120[o + 0x20] = (byte)I(a, "cost_f");
                s120[o + 0x21] = (byte)I(a, "cost_s");
                s120[o + 0x22] = (byte)I(a, "speed");
                s120[o + 0x23] = (byte)I(a, "hp");
                s120[o + 0x24] = (byte)I(a, "payload");
                s120[o + 0x25] = (byte)I(a, "airframe");
                s120[o + 0x26] = (byte)I(a, "attack");
                s120[o + 0x27] = (byte)I(a, "defence");
                s120[o + 0x28] = (byte)I(a, "sight");
                s120[o + 0x29] = (byte)I(a, "ammo");
                Put16(s120, o + 0x2c, I(a, "fuel"));
                n++;
            }

        if (m.Sec(119) is { } s119 && ent.TryGetProperty("ship_designs", out var sd))
            foreach (var d in sd.EnumerateArray())
            {
                int o = I(d, "slot") * CwmExtra.ShipStride;
                if (o < 0 || o + CwmExtra.ShipStride > s119.Length) continue;
                s119[o] = (byte)I(d, "enable");
                Name(s119, o + 0x01, 0x15, Str(d, "name"));
                s119[o + 0x16] = (byte)I(d, "weapon");
                s119[o + 0x17] = (byte)I(d, "chassis");
                s119[o + 0x18] = (byte)I(d, "variant");
                s119[o + 0x19] = (byte)I(d, "cost_w");
                s119[o + 0x1a] = (byte)I(d, "cost_ch");
                s119[o + 0x1b] = (byte)I(d, "cost_sp");
                s119[o + 0x1c] = (byte)I(d, "speed");
                s119[o + 0x1d] = (byte)I(d, "energie");
                s119[o + 0x1e] = (byte)I(d, "attack");
                s119[o + 0x1f] = (byte)I(d, "defence");
                Put16(s119, o + 0x20, I(d, "range1"));
                Put16(s119, o + 0x22, I(d, "range2"));
                s119[o + 0x24] = (byte)I(d, "sight");
                s119[o + 0x25] = (byte)I(d, "ammo");
                Put16(s119, o + 0x26, I(d, "fuel"));
                s119[o + 0x28] = (byte)I(d, "reload");
                n++;
            }

        // Die Zuege: sec44 traegt den ganzen Satz als Rohbytes, das y in HALBEN
        // Zeilen steht daneben in sec121.
        if (m.Sec(44) is { } s44 && ent.TryGetProperty("trains", out var tr))
            foreach (var t in tr.EnumerateArray())
            {
                int slot = I(t, "slot");
                int o = slot * CwmExtra.TrainStride;
                if (o < 0 || o + CwmExtra.TrainStride > s44.Length) continue;
                if (!Hex(Str(t, "raw"), s44, o, CwmExtra.TrainStride)) continue;
                if (m.Sec(121) is { } s121 && slot * 2 + 2 <= s121.Length)
                    Put16(s121, slot * 2, I(t, "y_half"));
                n++;
            }

        return n;
    }

    // =======================================================================
    //  die Rettung der leeren Saetze
    // =======================================================================

    /// <summary>
    /// <b>DER SATZ, DER AUS LAUTER NULLEN BESTEHT UND TROTZDEM DA IST.</b>
    ///
    /// <para>Vier Leser erkennen einen freien Platz daran, dass sein ganzer Satz
    /// null ist (<c>CwmExtra.AllZero</c>): die Gleisknoten sec33, die Bahnlinien
    /// sec34, die Sondereinheiten sec19 und die Vorkommen sec28. Steht ein Satz
    /// in der Ausgabe, dessen sämtliche AUSGEGEBENEN Felder null sind, dann war
    /// im Original ein Byte ungleich null, das <b>niemand ausgibt</b> — sonst
    /// hätte der Leser ihn gar nicht erst aufgeführt. Schreibt man ihn naiv
    /// zurück, ist er auf einmal ganz null, und der Satz VERSCHWINDET.</para>
    ///
    /// <para><b>Gemessen, nicht ausgedacht:</b> auf <c>map_DM_4</c> trifft es
    /// <b>einen von 33 Gleisknoten</b> — Knoten 105 mit
    /// <c>building 0, links [0,0,0,0,0]</c>. Alle sieben gelesenen Byte sind
    /// null, also muss das achte (+0x07) es nicht gewesen sein; der Rundlauf
    /// meldete vor dieser Stelle »rail_nodes: 33 gegen 32 Eintraege«.</para>
    ///
    /// <para><b>Was hier gemessen ist und was UNSERE SETZUNG:</b> dass das
    /// ungelesene Byte ungleich null WAR, ist bewiesen (sonst stünde der Satz
    /// nicht in der Ausgabe). <b>⚠ UNSERE SETZUNG ist der Wert 1</b> — der wahre
    /// steht nirgends. Er landet auf einem Byte, das kein Leser dieses Projekts
    /// anfasst; im Original mag es eine Bedeutung haben, die wir nicht kennen.
    /// Die Alternative wäre, den Satz sang- und klanglos fallen zu lassen, und
    /// das ist der schlechtere der zwei Fehler: ein verschwundener Gleisknoten
    /// zerreißt dem Spieler sein Netz, ein falsches Byte an einer Stelle, die
    /// niemand liest, tut nichts.</para>
    /// </summary>
    private static int Rescue(CwmFile m, JsonElement ent, Action<string> say)
    {
        int n = 0;
        n += RescueList(m, ent, "rail_nodes", "node", 33, 8, 0x07);
        n += RescueList(m, ent, "links", "slot", 34, CwmExtra.SpojStride, 0x0b);
        n += RescueList(m, ent, "special", "slot", 19, CwmExtra.SpecialStride, 0x07);
        n += RescueList(m, ent, "deposits", "slot", 28, CwmExtra.DepositStride, 0x07);
        if (n > 0)
            say($"  ⚠ {n} Satz/Saetze waeren als »leer« gelesen worden, obwohl die Ausgabe sie " +
                "fuehrt — ihr einziger von null verschiedener Wert steht in einem Byte, das der " +
                "Export nicht kennt. UNSERE SETZUNG: eine 1 auf ein ungelesenes Byte, damit der " +
                "Satz nicht verschwindet (siehe MapOpen.Rescue)");
        return n;
    }

    private static int RescueList(CwmFile m, JsonElement ent, string key, string slotKey,
                                  int section, int stride, int at)
    {
        var s = m.Sec(section);
        if (s == null || !ent.TryGetProperty(key, out var arr)) return 0;
        int n = 0;
        foreach (var e in arr.EnumerateArray())
        {
            int o = I(e, slotKey) * stride;
            if (o < 0 || o + stride > s.Length) continue;
            if (!CwmExtra.AllZero(s, o, stride)) continue;
            s[o + at] = 1;
            n++;
        }
        return n;
    }

    // =======================================================================
    //  Kleinteile
    // =======================================================================

    private static void MarkEmpty(CwmFile m, int section, int stride, int at)
    {
        var s = m.Sec(section);
        if (s == null) return;
        for (int o = at; o < s.Length; o += stride) s[o] = 0xFF;
    }

    private static void Put16(byte[] s, int at, int v)
    {
        if (at < 0 || at + 1 >= s.Length) return;
        s[at] = (byte)(v & 0xFF); s[at + 1] = (byte)((v >> 8) & 0xFF);
    }

    private static void Put32(byte[] s, int at, long v)
    {
        if (at < 0 || at + 3 >= s.Length) return;
        s[at] = (byte)(v & 0xFF); s[at + 1] = (byte)((v >> 8) & 0xFF);
        s[at + 2] = (byte)((v >> 16) & 0xFF); s[at + 3] = (byte)((v >> 24) & 0xFF);
    }

    /// <summary>Eine Zeichenkette in einen Namensplatz, nullterminiert.
    ///
    /// <para>⚠ <b>Der Rueckweg von <see cref="Cp437"/>, und der fehlt dort.</b>
    /// <c>Cp437</c> hat einen Leser und keinen Schreiber; die Umkehrtafel steht
    /// darum hier. Sie gehoerte nach <c>Scripts/Import/Cp437.cs</c>, an dem hier
    /// nicht geschrieben wird — <b>gesagt statt genommen</b>.</para></summary>
    private static void Name(byte[] s, int at, int len, string text)
    {
        for (int k = 0; k < len && at + k < s.Length; k++)
            s[at + k] = k < text.Length ? Cp437Byte(text[k]) : (byte)0;
    }

    private const string Cp437High =
        "ÇüéâäàåçêëèïîìÄÅ" +
        "ÉæÆôöòûùÿÖÜ¢£¥₧ƒ" +
        "áíóúñÑªº¿⌐¬½¼¡«»" +
        "░▒▓│┤╡╢╖╕╣║╗╝╜╛┐" +
        "└┴┬├─┼╞╟╚╔╩╦╠═╬╧" +
        "╨╤╥╙╘╒╓╫╪┘┌█▄▌▐▀" +
        "αßΓπΣσµτΦΘΩδ∞φε∩" +
        "≡±≥≤⌠⌡÷≈°∙·√ⁿ²■ ";

    private static byte Cp437Byte(char c)
    {
        if (c < 0x80) return (byte)c;
        int i = Cp437High.IndexOf(c);
        return i >= 0 ? (byte)(0x80 + i) : (byte)'?';
    }

    /// <summary>Hexziffern zurueck in Bytes. false, wenn die Laenge nicht
    /// stimmt — dann bleibt der Platz lieber leer, als halb beschrieben.</summary>
    private static bool Hex(string hex, byte[] dst, int at, int len)
    {
        if (hex.Length != len * 2) return false;
        for (int k = 0; k < len; k++)
        {
            if (!byte.TryParse(hex.AsSpan(k * 2, 2), NumberStyles.HexNumber,
                               CultureInfo.InvariantCulture, out byte b)) return false;
            dst[at + k] = b;
        }
        return true;
    }

    private static int I(JsonElement e, string key, int def = 0)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
           ? v.GetInt32() : def;

    private static string Str(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
           ? v.GetString() ?? "" : "";

    private static int Len(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
           ? v.GetArrayLength() : 0;
}
