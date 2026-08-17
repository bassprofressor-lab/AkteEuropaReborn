namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// <b>DIE TAFEL »NEUE TECHNOLOGIEN«</b> — was eine Kampagnenmission dem Spieler
/// im Briefing als NEU ankündigt, gelesen aus GAME.EXE.
///
/// <para><b>Gefunden über den Zeichner, nicht geraten.</b> Der Briefingschirm
/// ist Fensterart <b>0x2B = 43</b> (der Erbauer <c>0x45BC10</c> schreibt
/// <c>mov byte [edx], 0x2b</c> in seinen Fenstersatz). Seine Zeichenroutine
/// steht in der Zeichnertafel <c>@0x487888</c> auf Platz <c>43−1</c> und ist
/// <b>0x486480</b>. Deren letzter Block, <c>0x486AFA…0x486C92</c>, IST der
/// Kasten:</para>
///
/// <code>
///   ; ecx = byte[0x8C3CC9]        der Blätterstand (welcher Eintrag oben steht)
///   lea edx,[ecx+ecx*4] / lea ecx,[ecx+edx*2]     ; ecx = 11*Eintrag
///   ; edx = dword[0x8C34F4]       die MISSIONSNUMMER
///   mov eax,edx / shl edx,3 / sub edx,eax / shl edx,5   ; edx = 224*Mission
///   movsx eax, word [edx + ecx*2 + 0x4FEB20]      ; die ENZYKLOPÄDIESEITE
///   mov  ebx, dword [eax*4 + 0x991820]            ; Seite -> Bildnummer
///   ...
///   ; ENCYCLOG.PIC, Sprung auf 3600*(Bild−1), 3600 Byte lesen  (= 60x60)
///   ; 60 Zeilen a 60 Byte nach  x = 0x1E = 30, y = 0x172 = 370
///   call 0x4021DA(0x32, 0x154, 0x96, 0x0F, 0, ...)  ; SCHWARZER Streifen
///   call 0x401041(0x32, 0x154, 0x4FEB0C + 224*Mission + 22*Eintrag, ...)  ; der NAME
/// </code>
///
/// <para><b>Die Form der Tafel</b>, aus diesen zwei Adressen abgezählt: Basis
/// <c>0x4FEB0C</c> (die Namen) und <c>0x4FEB20 = Basis + 20</c> (die Seiten),
/// also je Eintrag <b>20 Byte Name + 1 Wort Seite = 22 Byte</b>; je Mission
/// <b>224 Byte</b>. Der Erbauer <c>0x45BD17</c> zählt die belegten Einträge und
/// bricht bei <c>cmp ax,0xA</c> ab — <b>10 Einträge</b> je Mission, 10·22 = 220,
/// die restlichen 4 Byte je Satz gehören nicht dazu (sie tragen zwei Worte, die
/// hier NICHT gedeutet werden).</para>
///
/// <para><b>Die Kodierung ist Latin-1</b>, nicht cp437 — »Wüstenwiesel« steht
/// als <c>57 fc 73…</c> da. Dieselbe Falle wie bei ENCYCLOG.TXT und der
/// Befehlsliste, und in der anderen Richtung als bei BRIEFG.TXT.</para>
///
/// <para><b>Gegen beide GAME.EXE geprüft.</b> Die Tafel liegt in der 1.421.824-B-
/// Fassung auf <c>0x4FEB0C</c> und in der 1.420.800-B-Fassung auf
/// <c>0x4FDB4C</c> — <b>40 Sätze (8.960 Byte) Byte für Byte gleich</b>. Gesucht
/// wird sie deshalb nach der FORM (siehe <see cref="FindTable"/>), nie nach
/// einer Adresse.</para>
///
/// <para><b>Was herauskommt</b>, gezählt: <b>74 Einträge</b> über
/// <b>30 der 33 Missionen</b>; ohne Eintrag sind die Missionen <b>1, 4 und
/// 28</b>. Mission 1 ist die Gegenprobe des Spielers (»der Kasten ist leer«),
/// Mission 2 die andere: dort steht »Leichte Bordkanone«, und genau die steht
/// als erster ihrer drei Einträge in der Tafel.</para>
///
/// <para><b>Eine unabhängige Bestätigung</b> von der anderen Seite: der
/// Freischalt-Fahrplan aus dem Missionsaufbau (<c>campaign_schedule.json</c>,
/// Verteiler <c>@0x494274</c>) gibt Mission 2 die Bauteilzeilen 1, 4, 161 und
/// 80. Die Tafel nennt für Mission 2 »Leichte Bordkanone« (Zeile 1),
/// »Maschinengewehr« (Zeile 4) und »Reifen« (Zeile 161) — die drei mit Bild;
/// Zeile 80 ist eine der neun Verbesserungen, die im Original kein Bild haben.
/// Zwei getrennt gelesene Tafeln, dieselbe Aussage.</para>
///
/// <para>⚠ <b>Was NICHT behauptet wird:</b> dass die Tafel der Freischaltung
/// folgt. Mission 6 schaltet Bauteilzeile 5 frei, ihr Kasten kündigt aber
/// »Leichte Infanterie« an, und die erscheint erst in Mission 7 als
/// »2xMaschinengewehr«. Die Tafel ist REDAKTIONELL — sie ist, was das Original
/// zeigt, und mehr wird hier nicht aus ihr gemacht.</para>
///
/// <para><b>Das Bild</b> kommt NICHT aus der Bildbank der Oberfläche
/// (<c>ANIM.CWA</c>, <see cref="AkteEuropaReborn.UI.PortraitBank"/>), sondern
/// aus <b>ENCYCLOG.PIC</b>. Das ist eine der Antworten, die
/// <see cref="EncyclopediaExporter"/> offengelassen hat: 345.600 Byte sind
/// <b>96 Bilder zu 60×60</b> (3.600 Byte), nicht 24 zu 120×120 — die
/// Sprungrechnung <c>3600·(Bild−1)</c> im Zeichner sagt beides, Größe und
/// Anzahl. Die Bildnummer steht in ENCYCLOG.TXT hinter dem Komma der
/// Seitenmarke (<c>#p36,17</c>), und das Wort in unserer Tafel ist die
/// SEITENnummer.</para>
/// </summary>
public sealed class MissionTechExporter
{
    // ---- die Form der Tafel, aus dem Zeichner abgezählt ----------------------

    /// <summary>Bytes je Mission — <c>shl edx,3; sub edx,eax; shl edx,5</c>
    /// @0x486BC7 ist 7·32.</summary>
    public const int Stride = 224;

    /// <summary>Einträge je Mission — die <c>cmp ax,0xA</c> des Erbauers
    /// @0x45BD37, die den Zähler abbricht.</summary>
    public const int Slots = 10;

    /// <summary>Bytes je Eintrag — <c>lea ecx,[ecx+edx*2]</c> macht aus dem
    /// Eintrag 11 Worte.</summary>
    public const int EntrySize = 22;

    /// <summary>Bytes des Namensfeldes — <c>0x4FEB20 − 0x4FEB0C</c>, der Abstand
    /// zwischen dem Zeiger auf den Namen und dem auf die Seite.</summary>
    public const int NameLen = 20;

    /// <summary>Die 33 Kampagnenmissionen. Satz 0 ist leer und gehört keiner
    /// Mission; ab Satz 34 ist die Tafel leer.</summary>
    public const int Missions = 33;

    /// <summary>Ein Bild aus ENCYCLOG.PIC: 60×60 Punkte, 3.600 Byte — die
    /// Sprungweite <c>3600·(Bild−1)</c> @0x486B7C und die 60 Zeilen zu
    /// 15 Dwords @0x486BEE.</summary>
    public const int PicW = 60, PicH = 60;
    public const int PicBytes = PicW * PicH;

    /// <summary>Wohin der Zeichner das Bild legt (im 640×480-Schirm):
    /// <c>x = 0x1E</c>, <c>y = 0x172</c> @0x486C12.</summary>
    public const int PicX = 30, PicY = 370;

    /// <summary>Der schwarze Streifen und die Schrift darauf:
    /// <c>0x4021DA(0x32, 0x154, 0x96, 0x0F, 0, …)</c> @0x486C4F und
    /// <c>0x401041(0x32, 0x154, …)</c> @0x486C8D.</summary>
    public const int BarX = 50, BarY = 340, BarW = 150, BarH = 15;

    /// <summary>Ein Eintrag des Kastens.</summary>
    public sealed class Tech
    {
        /// <summary>Der Name, wie er im Streifen steht — Latin-1 aus der Tafel.</summary>
        public string Name = "";

        /// <summary>Die Enzyklopädieseite, das Wort auf <c>+20</c> des Eintrags.</summary>
        public int Page;

        /// <summary>Die Bildnummer in ENCYCLOG.PIC, 1-basiert; 0 = keines.
        /// Aus ENCYCLOG.TXT, <c>#p&lt;Seite&gt;,&lt;Bild&gt;</c>.</summary>
        public int Picture;
    }

    // ---- die PE-Häppchen ----------------------------------------------------

    private readonly byte[] _d;
    private readonly List<(uint Va, uint Raw, uint RawSize)> _sections = new();

    public MissionTechExporter(byte[] exe)
    {
        _d = exe;
        int e = BitConverter.ToInt32(_d, 0x3c);
        int nsec = BitConverter.ToUInt16(_d, e + 6);
        int optsz = BitConverter.ToUInt16(_d, e + 20);
        int st = e + 24 + optsz;
        for (int i = 0; i < nsec; i++)
        {
            int s = st + i * 40;
            _sections.Add((BitConverter.ToUInt32(_d, s + 12) + 0x400000,
                           BitConverter.ToUInt32(_d, s + 20),
                           BitConverter.ToUInt32(_d, s + 16)));
        }
    }

    /// <summary>Die virtuelle Adresse, auf der die Tafel gefunden wurde — für
    /// Meldungen und für die JSON, damit nachprüfbar bleibt, wo sie herkommt.
    /// 0, solange nicht gesucht wurde.</summary>
    public uint TableVa { get; private set; }

    // ---- die Suche nach der FORM --------------------------------------------

    /// <summary>
    /// Ein Eintrag, wie die Tafel ihn hat: bis zu 20 Zeichen Name, dahinter mit
    /// Nullen aufgefüllt, dann ein Wort. Gibt <c>null</c> zurück, wenn die 22
    /// Byte diese Form NICHT haben — das ist der Prüfstein, mit dem die Tafel
    /// gesucht wird, und er muss scheitern können.
    /// </summary>
    private static Tech? EntryAt(byte[] d, int at)
    {
        if (at < 0 || at + EntrySize > d.Length) return null;
        int z = -1;
        for (int i = 0; i < NameLen; i++)
            if (d[at + i] == 0) { z = i; break; }
        if (z < 0) return null;                       // kein Abschluss im Feld
        for (int i = 0; i < z; i++)
            if (d[at + i] < 0x20 || d[at + i] == 0x7f) return null;
        for (int i = z; i < NameLen; i++)
            if (d[at + i] != 0) return null;          // hinter der Null nur Null
        int page = BitConverter.ToUInt16(d, at + NameLen);
        return new Tech
        {
            Name = Encoding.Latin1.GetString(d, at, z),
            Page = page,
        };
    }

    /// <summary>Ein ganzer Missionssatz, oder <c>null</c>, wenn er die Form
    /// nicht hat. Die Einträge sind GEPACKT — hinter einem leeren kommt keiner
    /// mehr; auch das ist Form und nicht Inhalt, denn der Zähler des Erbauers
    /// @0x45BD17 hört beim ersten leeren auf.</summary>
    private static List<Tech>? RecordAt(byte[] d, int at)
    {
        var list = new List<Tech>();
        bool empty = false;
        for (int k = 0; k < Slots; k++)
        {
            var t = EntryAt(d, at + k * EntrySize);
            if (t == null) return null;
            if (t.Name.Length == 0 && t.Page == 0) { empty = true; continue; }
            if (empty) return null;                   // Loch mitten in der Liste
            if (t.Name.Length == 0 || t.Page < 1 || t.Page > 200) return null;
            list.Add(t);
        }
        return list;
    }

    /// <summary>Wieviele der 34 Sätze mindestens etwas tragen müssen, damit ein
    /// Fund als die Tafel gilt. Gemessen sind 30; die Schwelle liegt darunter,
    /// damit sie nicht die Antwort vorschreibt, und weit über dem, was eine
    /// zufällige Bytefolge schafft.</summary>
    private const int MinFilled = 25;

    /// <summary>
    /// Die Tafel suchen — nach der FORM, über die ganze <c>.data</c>.
    ///
    /// <para>Ein Fund ist eine Stelle, an der <b>34 Sätze hintereinander</b>
    /// (Satz 0 bis 33) die Form haben und mindestens
    /// <see cref="MinFilled"/> davon etwas tragen. Weil die Tafel selbst diese
    /// Form auch dann noch hat, wenn man einen Satz später einsteigt, liefert
    /// die Suche eine Reihe von Treffern im Abstand <see cref="Stride"/> — der
    /// ERSTE ist der Anfang.</para>
    ///
    /// <para>⚠ Damit »der erste« nicht bloss eine Annahme ist, wird er
    /// GEPRÜFT: der Satz DAVOR darf die Form nicht haben. Hätte er sie, wäre
    /// die Suche einen Satz zu spät angekommen und alle Missionsnummern wären
    /// um eins verschoben — der teuerste Fehler, den diese Tafel zulässt.</para>
    ///
    /// <returns>Der Dateiversatz der Tafel, oder −1.</returns>
    /// </summary>
    public int FindTable()
    {
        foreach (var s in _sections)
        {
            if (s.RawSize < Stride * 34) continue;
            int lo = (int)s.Raw, hi = (int)(s.Raw + s.RawSize) - Stride * 34;
            for (int off = lo; off <= hi; off += 2)
            {
                int filled = 0;
                bool ok = true;
                for (int m = 0; m < 34 && ok; m++)
                {
                    var rec = RecordAt(_d, off + m * Stride);
                    if (rec == null) ok = false;
                    else if (rec.Count > 0) filled++;
                }
                if (!ok || filled < MinFilled) continue;
                // der Satz DAVOR muss die Form brechen, sonst stehen wir zu spät
                if (off - Stride >= lo && RecordAt(_d, off - Stride) != null) continue;
                TableVa = s.Va + (uint)(off - s.Raw);
                return off;
            }
        }
        return -1;
    }

    // ---- lesen ---------------------------------------------------------------

    /// <summary>Die ganze Zuordnung Mission → Technik, Missionen 1…33. Leere
    /// Missionen kommen mit leerer Liste vor, denn »diese Mission schaltet
    /// nichts frei« ist eine Aussage und kein Loch.</summary>
    public Dictionary<int, List<Tech>> Read()
    {
        var all = new Dictionary<int, List<Tech>>();
        int at = FindTable();
        if (at < 0) return all;
        for (int m = 1; m <= Missions; m++)
            all[m] = RecordAt(_d, at + m * Stride) ?? new List<Tech>();
        return all;
    }

    /// <summary>Der Rest der Tafel — die Sätze 34…39 — muss leer sein. Eine
    /// Gegenprobe, die scheitern kann: wäre dort noch etwas, hätte die Tafel
    /// mehr als 33 Missionen und die Deutung wäre falsch.</summary>
    public bool TailIsEmpty(int at, int upto = 40)
    {
        for (int m = Missions + 1; m < upto; m++)
        {
            var r = RecordAt(_d, at + m * Stride);
            if (r == null || r.Count > 0) return false;
        }
        return true;
    }

    // ---- ENCYCLOG.TXT: Seite -> Bildnummer -----------------------------------

    /// <summary>
    /// Die Seitenmarken von ENCYCLOG.TXT, <c>#p&lt;Seite&gt;,&lt;Bild&gt;</c>.
    /// ⚠ Latin-1 wie die Tafel selbst, siehe <see cref="EncyclopediaExporter"/>.
    /// Die Bildnummer darf fehlen (<c>#p1,</c>), dann ist sie 0.
    /// </summary>
    public static Dictionary<int, int> PicturesOfPages(byte[] encyclogTxt)
    {
        var map = new Dictionary<int, int>();
        foreach (string line in Encoding.Latin1.GetString(encyclogTxt)
                     .Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (!line.StartsWith("#p", StringComparison.Ordinal)) continue;
            string rest = line[2..];
            int comma = rest.IndexOf(',');
            string numTxt = comma >= 0 ? rest[..comma] : rest;
            if (!int.TryParse(numTxt, out int page)) continue;
            int pic = 0;
            if (comma >= 0) int.TryParse(rest[(comma + 1)..].Trim(), out pic);
            map[page] = pic;
        }
        return map;
    }

    /// <summary>Die Bildnummern in die gelesene Zuordnung eintragen.</summary>
    public static int ApplyPictures(Dictionary<int, List<Tech>> all, Dictionary<int, int> pages)
    {
        int with = 0;
        foreach (var kv in all)
            foreach (var t in kv.Value)
            {
                t.Picture = pages.TryGetValue(t.Page, out int p) ? p : 0;
                if (t.Picture > 0) with++;
            }
        return with;
    }

    // ---- schreiben ------------------------------------------------------------

    /// <summary>Der Name der Zuordnungsdatei unter <c>Maps/</c>.</summary>
    public const string JsonName = "mission_tech.json";

    /// <summary>Der Ordner der Bilder unter dem Inhaltsstamm.</summary>
    public const string PicDir = "UI/tech";

    /// <summary>
    /// Alles schreiben: die Zuordnung nach <c>&lt;dst&gt;/Maps/mission_tech.json</c>
    /// und die gebrauchten Bilder nach <c>&lt;dst&gt;/UI/tech/pNN.png</c>.
    /// </summary>
    /// <param name="exe">GAME.EXE, ganz.</param>
    /// <param name="encyclogTxt">ENCYCLOG.TXT, für Seite → Bildnummer.</param>
    /// <param name="encyclogPic">ENCYCLOG.PIC; null lässt die Bilder aus.</param>
    /// <param name="pal">Die Palette, in der der Briefingschirm gemalt wird —
    /// DATA/01.PAL, dieselbe wie bei
    /// <see cref="BriefingExporter.WriteBackdrop"/>. Der Zeichner setzt keine
    /// eigene, er blittet in die laufende.</param>
    /// <returns>Wieviele Missionen eine Technik tragen, oder −1 bei Misserfolg.</returns>
    public static int WriteAll(byte[] exe, byte[] encyclogTxt, byte[]? encyclogPic,
                               PalFile? pal, string dst, Action<string>? say = null)
    {
        var ex = new MissionTechExporter(exe);
        int at = ex.FindTable();
        if (at < 0)
        {
            say?.Invoke("mission-tech: die Tafel steht nicht in dieser GAME.EXE — " +
                        "nichts geschrieben");
            return -1;
        }
        var all = ex.Read();
        var pages = PicturesOfPages(encyclogTxt);
        int withPic = ApplyPictures(all, pages);

        int filled = 0, entries = 0;
        var used = new SortedSet<int>();
        foreach (var kv in all)
        {
            if (kv.Value.Count > 0) filled++;
            entries += kv.Value.Count;
            foreach (var t in kv.Value) if (t.Picture > 0) used.Add(t.Picture);
        }

        dst = dst.TrimEnd('/', '\\');
        Directory.CreateDirectory(dst + "/Maps");
        File.WriteAllText($"{dst}/Maps/{JsonName}", ToJson(all, ex.TableVa, ex.TailIsEmpty(at)),
                          new UTF8Encoding(false));

        int written = 0;
        if (encyclogPic != null && pal != null)
        {
            Directory.CreateDirectory($"{dst}/{PicDir}");
            foreach (int n in used)
            {
                int o = (n - 1) * PicBytes;
                if (n < 1 || o + PicBytes > encyclogPic.Length) continue;
                var img = Godot.Image.CreateEmpty(PicW, PicH, false, Godot.Image.Format.Rgba8);
                for (int y = 0; y < PicH; y++)
                    for (int x = 0; x < PicW; x++)
                    {
                        byte v = encyclogPic[o + y * PicW + x];
                        img.SetPixel(x, y, Godot.Color.Color8(pal.R[v], pal.G[v], pal.B[v], 255));
                    }
                img.SavePng($"{dst}/{PicDir}/p{n:00}.png");
                written++;
            }
            File.WriteAllText($"{dst}/{PicDir}_index.json",
                "{\"_note\":\"ENCYCLOG.PIC, 60x60 rohe Palettenindizes je Bild, " +
                "Sprungweite 3600*(Bild-1) — abgezaehlt am Zeichner 0x486B7C/0x486BEE; " +
                "Palette DATA/01.PAL wie der Briefinghintergrund\"," +
                $"\"width\":{PicW},\"height\":{PicH}," +
                $"\"bank\":{(encyclogPic.Length / PicBytes)},\"written\":{written}}}",
                new UTF8Encoding(false));
        }

        say?.Invoke($"mission-tech: Tafel @0x{ex.TableVa:x}, {entries} Eintraege in " +
                    $"{filled} von {Missions} Missionen, {withPic} mit Bild, " +
                    $"{written} Bilder geschrieben");
        return filled;
    }

    private static string ToJson(Dictionary<int, List<Tech>> all, uint va, bool tailEmpty)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_note\":\"Was eine Kampagnenmission im Briefingkasten »neue technologien« " +
                  "ankuendigt. Aus der Tafel in GAME.EXE, gefunden ueber die Form; ihre Adresse " +
                  "steht in _table nur als Beleg. Abgeleitete Metadaten, kein Originalinhalt.\",");
        sb.Append("\"_source\":\"Zeichner der Fensterart 43 @0x486480, Block 0x486AFA..0x486C92; " +
                  "Erbauer @0x45BC10. In beiden GAME.EXE (1.421.824 / 1.420.800 B) Byte fuer Byte gleich.\",");
        sb.Append($"\"_table\":{{\"va\":\"0x{va:x}\",\"stride\":{Stride},\"slots\":{Slots}," +
                  $"\"entry\":{EntrySize},\"name_len\":{NameLen}," +
                  $"\"encoding\":\"latin-1\",\"tail_empty\":{(tailEmpty ? "true" : "false")}}},");
        sb.Append($"\"_layout\":{{\"bar\":[{BarX},{BarY},{BarW},{BarH}]," +
                  $"\"picture\":[{PicX},{PicY},{PicW},{PicH}]," +
                  $"\"pic_dir\":\"{PicDir}\"}},");
        sb.Append("\"missions\":{");
        bool first = true;
        foreach (var m in new SortedSet<int>(all.Keys))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{m}\":[");
            var list = all[m];
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"name\":\"{Esc(list[i].Name)}\",\"page\":{list[i].Page}," +
                          $"\"picture\":{list[i].Picture}}}");
            }
            sb.Append(']');
        }
        sb.Append("}}");
        return sb.ToString();
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
            sb.Append(c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\n' => " ", '\r' => "", '\t' => " ",
                _ => c.ToString(),
            });
        return sb.ToString();
    }

    // ---- der bequeme Weg: aus einer Quelle heraus ----------------------------

    /// <summary>
    /// Aus einer Installation (oder einem Pfad auf GAME.EXE selbst) alles
    /// schreiben. Das ist der Weg, den <c>--tech-export=</c> geht, solange der
    /// Haken im regulären Einlesen (<see cref="ContentBuilder"/>) noch fehlt —
    /// derselbe Notbehelf wie <c>--reexport-effects=</c> bei der Bildbank.
    /// </summary>
    /// <returns>Wieviele Missionen eine Technik tragen, oder −1.</returns>
    public static int RunFromSource(string source, string dst, Action<string>? say = null)
    {
        source = source.Trim().Trim('"').TrimEnd('/', '\\');
        string dir = Directory.Exists(source) ? source : Path.GetDirectoryName(source) ?? ".";
        string? exe = File.Exists(source) ? source : Core.ContentSources.ExeIn(dir);
        if (exe == null) { say?.Invoke($"mission-tech: keine GAME.EXE in {dir}"); return -1; }

        string? txt = FirstThatExists(dir, "ENCYCLOG.TXT", "DATA/ENCYCLOG.TXT");
        if (txt == null) { say?.Invoke($"mission-tech: ENCYCLOG.TXT fehlt neben {exe}"); return -1; }
        string? pic = FirstThatExists(dir, "ENCYCLOG.PIC", "DATA/ENCYCLOG.PIC");
        if (pic == null) say?.Invoke($"mission-tech: ENCYCLOG.PIC fehlt neben {exe} — keine Bilder");

        // ⚠ Die Palette liegt NICHT immer bei der GAME.EXE: die Installation auf
        // F: hat die Programmdateien lose, aber kein DATA\ — die .PAL stehen dort
        // auf den Datenträgern. Deshalb greift der Einlesestamm ein, in den
        // ContentBuilder DATA/01.PAL schon kopiert hat. Es ist DIESELBE Palette,
        // die BriefingExporter.WriteBackdrop für den Hintergrund nimmt, und ohne
        // sie kommen 74 Bilder nicht heraus — der Prüfstand hat genau das
        // gemeldet, bevor diese Zeilen hier standen.
        PalFile? pal = null;
        string? palPath = FirstThatExists(dir, "DATA/01.PAL", "01.PAL");
        if (palPath != null) pal = PalFile.Load(palPath);
        else
        {
            string p = Core.Content.Path("DATA/01.PAL");
            if (Godot.FileAccess.FileExists(p))
            {
                pal = PalFile.FromBytes(Godot.FileAccess.GetFileAsBytes(p));
                say?.Invoke($"mission-tech: 01.PAL nicht bei der GAME.EXE — nehme {p}");
            }
            else say?.Invoke("mission-tech: keine 01.PAL gefunden — keine Bilder");
        }

        return WriteAll(File.ReadAllBytes(exe), File.ReadAllBytes(txt),
                        pic != null ? File.ReadAllBytes(pic) : null, pal, dst, say);
    }

    private static string? FirstThatExists(string dir, params string[] names)
    {
        foreach (string n in names)
        {
            string p = dir + "/" + n;
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
