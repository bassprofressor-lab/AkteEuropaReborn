namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// The mission briefings — the text the campaign opens each mission with, and
/// which the remake has been dropping straight past into the map.
///
/// <b>BRIEFG.TXT</b> lives in the InstallShield cabinet next to everything else
/// (18,149 bytes, entry 8 of 38). Its shape, read off the file rather than
/// guessed:
///
/// <code>
///   #001 Mission 1&lt;CR&gt;&lt;LF&gt;          record head: number, then a title
///   ^ Wir treten jetzt in die …     the body starts after "^ "
///   … bedroht werden.&lt;CR&gt;&lt;LF&gt;       CR LF ends a paragraph
///   … abgesetzt, in&lt;CR&gt;dem der …    a bare CR is only the original's line break
/// </code>
///
/// Thirty-three records, one per campaign mission, numbered the way the missions
/// are. The encoding is cp437 like the rest of the game's text — the umlauts sit
/// at 0x81/0x84/0x94/0xe1 and come out as control characters in latin-1, which
/// is exactly the mistake the exe tables cost a run to find.
///
/// <para><b>One decision is ours and is marked as such:</b> a bare CR is the
/// original's own line break, chosen for its fixed-width screen. This screen is
/// not that screen, so a bare CR becomes a space and the text is re-wrapped;
/// only CR LF keeps its meaning as a paragraph break. Keeping the hard breaks
/// would ladder the text down the page at any other width.</para>
///
/// <para>The titles the file carries are just "Mission 1" … "Mission 33"; the
/// mission's real name comes from the map, and campaign.json already has it.</para>
///
/// <para>BRIEFG.DAT — 350,502 bytes of palette indices that go with these — is
/// read here too: the first 307,200 are the 640x480 backdrop
/// (<see cref="WriteBackdrop"/>), and the 43,302 that follow are a bank of 29
/// pictures whose sizes live in the loader rather than in the file
/// (<see cref="Bank"/>). Nine of them are the game's emblem
/// (<see cref="WriteEmblem"/>); the big watermark behind the text is a file of
/// its own, SYMBOL.DAT (<see cref="WriteWatermark"/>).</para>
/// </summary>
public sealed class BriefingExporter
{
    private readonly string _dst;
    private readonly string _ui;

    /// <summary>How many briefings were written.</summary>
    public int Briefings;

    /// <summary>Set once the backdrop was written.</summary>
    public bool Backdrop;

    public BriefingExporter(string mapsDir, string uiDir = "")
    {
        _dst = mapsDir.TrimEnd('/', '\\');
        _ui = uiDir.TrimEnd('/', '\\');
    }

    // ---- BRIEFG.DAT: the screen the briefing was shown on -------------------

    /// <summary>The briefing screen's backdrop: 640x480 palette indices, raw.
    ///
    /// The loader @0x45bddc says so outright — it allocates exactly the file's
    /// 350,502 bytes, reads the whole file in one go, and then sets
    /// <c>width = 0x280, height = 0x1e0</c>. 640x480 is 307,200 of those bytes;
    /// what the remaining 43,302 are is not known and is not touched here.
    ///
    /// The same routine loads <b>FONT2.CWD</b> immediately before it, which is
    /// what that second, thinner typeface was for — this screen. It is not
    /// exported yet, so the remake writes the briefing in FONT.CWD.
    ///
    /// The screen has no palette of its own: the loader opens no .PAL, so it
    /// draws in whatever palette is current. It is written out in the terrain
    /// palette, the same choice the panel and the font already make.
    /// </summary>
    public const int BackdropW = 640, BackdropH = 480;

    /// <summary>The white plate the text is written on, measured off the image:
    /// every column and row solidly palette index 144 (255,255,247). It comes
    /// out at exactly 320x240, which is how round it is known to be right.</summary>
    public const int TextX = 296, TextY = 79, TextW = 320, TextH = 240;

    public void WriteBackdrop(byte[] dat, PalFile pal, Action<string>? say = null)
    {
        if (_ui.Length == 0 || dat.Length < BackdropW * BackdropH) return;
        Directory.CreateDirectory(_ui);
        var img = Godot.Image.CreateEmpty(BackdropW, BackdropH, false, Godot.Image.Format.Rgba8);
        for (int y = 0; y < BackdropH; y++)
            for (int x = 0; x < BackdropW; x++)
            {
                byte v = dat[y * BackdropW + x];
                img.SetPixel(x, y, Godot.Color.Color8(pal.R[v], pal.G[v], pal.B[v], 255));
            }
        img.SavePng($"{_ui}/briefing_bg.png");
        File.WriteAllText($"{_ui}/briefing_index.json",
            $"{{\"_note\":\"BRIEFG.DAT, {BackdropW}x{BackdropH} raw palette indices; " +
            "size and dimensions from the loader @0x45bddc\"," +
            $"\"width\":{BackdropW},\"height\":{BackdropH}," +
            $"\"text\":{{\"x\":{TextX},\"y\":{TextY},\"w\":{TextW},\"h\":{TextH}}}}}",
            new UTF8Encoding(false));
        Backdrop = true;
        say?.Invoke($"Briefing-Bildschirm: {BackdropW}x{BackdropH}, Textfeld {TextW}x{TextH}");
    }

    // ---- MAP.DAT: the radar monitor on that screen ---------------------------

    /// <summary>MAP.DAT — 13,465,320 bytes, and it divides without remainder:
    /// <b>33 groups of 10 pictures of 202 x 202</b>. 33 is the number of
    /// campaign missions, and the group is an animation.
    ///
    /// <para>The loader @0x45c093 says all of it. It seeks to
    /// <c>(n-1) * 408040</c> — and 408040 is 10 x 202 x 202 — then reads
    /// <b>202 rows of 202 bytes</b> into the work buffer at <b>x = 0x11 = 17,
    /// y = 0x26 = 38</b>. That is exactly where the briefing backdrop has its
    /// radar monitor.</para>
    ///
    /// <para>What the pictures are: the map of Europe with a small yellow cross
    /// on the mission's own location, and the ten frames of a group close a
    /// targeting reticle in around it. The backdrop carries a map of its own in
    /// that monitor, but a different one — 34% of the bytes match, so the same
    /// map drawn without this overlay.</para>
    ///
    /// <para><b>That the cross is the mission's place is checked, not assumed.</b>
    /// Frame 0 of every group holds exactly one 20-pixel cross, all 33 of them at
    /// different coordinates, and four land where the mission's own briefing says
    /// they should: <b>5 at (4,168)</b>, the far south-west corner — "Die
    /// Kanarischen Inseln"; <b>13 at (81,114)</b> — "bevor unsere Verbaende die
    /// Pyrenaeen ueberqueren"; <b>25 at (85,82)</b> — "Expeditionseinheiten haben
    /// endlich Belgien"; <b>26 at (111,50)</b>, the top — "Keine guten
    /// Neuigkeiten aus dem Norden".</para>
    ///
    /// <para>Which of the ten the game shows when is not read; the remake plays
    /// them in order and stops on the last, which is OURS.</para>
    /// </summary>
    public const int RadarW = 202, RadarH = 202, RadarFrames = 10, RadarX = 17, RadarY = 38;

    /// <summary>How many missions were written.</summary>
    public int Radars;

    public void WriteRadar(byte[] map, PalFile pal, Action<string>? say = null)
    {
        if (_ui.Length == 0) return;
        int group = RadarW * RadarH * RadarFrames;
        int groups = map.Length / group;
        if (groups == 0) { say?.Invoke("MAP.DAT zu kurz — kein Radarbild"); return; }

        int frames = 0;
        for (int m = 0; m < groups; m++)
        {
            string dir = $"{_ui}/radar/{m + 1}";
            Directory.CreateDirectory(dir);
            for (int f = 0; f < RadarFrames; f++)
            {
                int at = m * group + f * RadarW * RadarH;
                var img = Godot.Image.CreateEmpty(RadarW, RadarH, false, Godot.Image.Format.Rgba8);
                for (int y = 0; y < RadarH; y++)
                    for (int x = 0; x < RadarW; x++)
                    {
                        byte v = map[at + y * RadarW + x];
                        img.SetPixel(x, y, Godot.Color.Color8(pal.R[v], pal.G[v], pal.B[v], 255));
                    }
                img.SavePng($"{dir}/f{f}.png");
                frames++;
            }
        }
        Radars = groups;
        File.WriteAllText($"{_ui}/radar/radar_index.json",
            "{\"_note\":\"MAP.DAT, 33 groups of 10 pictures of 202x202; loader @0x45c093 seeks " +
            "(mission-1)*408040 and reads 202 rows of 202 to x=17 y=38 of the briefing screen\"," +
            $"\"missions\":{groups},\"frames\":{RadarFrames}," +
            $"\"w\":{RadarW},\"h\":{RadarH},\"x\":{RadarX},\"y\":{RadarY}}}",
            new UTF8Encoding(false));
        say?.Invoke($"Radarbilder: {groups} Missionen mit je {RadarFrames} Bildern ({frames} Dateien)");
    }

    // ---- DAS EMBLEM VON AKTE EUROPA -----------------------------------------

    /// <summary>
    /// <b>DIE BILDBANK IM SCHWANZ VON BRIEFG.DAT — 29 Bilder, keine Kopfdaten.</b>
    ///
    /// <para>Die 43.302 Byte hinter dem 640×480-Hintergrund waren monatelang
    /// »eine Bank mit Kopfdaten«. Sie hat keine. <b>Die Gliederung steht im
    /// Maschinencode</b>, nicht in der Datei: der Lader schreibt bei
    /// <c>0x45BE6C..0x45C04E</c> Breite und Höhe von 29 Einträgen von Hand in
    /// eine Tabelle bei <c>[ebx+0x8C3404]</c> (Eintrag i: Breite auf +8i, Höhe
    /// auf +8i+2, Zeiger auf +8i+4), und die Schleife bei <c>0x45C05F</c>
    /// rechnet die Zeiger nur noch fort:</para>
    ///
    /// <code>
    ///   Zeiger[i] = Zeiger[i-1] + Breite[i-1] * Höhe[i-1]
    /// </code>
    ///
    /// <para>Eintrag 0 ist der Hintergrund selbst (640×480 auf Dateianfang), die
    /// 29 anderen liegen lückenlos dahinter. <b>Die Probe: die Summe ihrer
    /// Flächen ist 43.302 — genau der Schwanz.</b> Kein Kopf, keine Packung,
    /// kein Rest. Deshalb zerfiel jede geratene Breite: die Bilder sind
    /// verschieden breit und stehen ohne Trennzeichen aneinander.</para>
    ///
    /// <para>⚠ <b>In BEIDEN GAME.EXE gegengeprüft</b> (die Datenadressen
    /// unterscheiden sich — 0x8C3404 gegen 0x8C2464 —, die Tabelle ist
    /// zeichengleich): 29 Einträge, Summe 43.302 in beiden.</para>
    ///
    /// <para>Was drinsteht, Eintrag für Eintrag: 1–6 sind sechs 49×15-Streifen,
    /// 7–10 vier 25×28, 11–12 zwei 63×28, <b>13–16 die vier Wettertafeln</b>
    /// (SKY:CLOUDED, OVERCAST …, die alte Fehlspur), 17–18 zwei 22×13, 19–20
    /// zwei Winzlinge von 6×2 — und <b>21–29 die neun Bilder des Emblems</b>.</para>
    /// </summary>
    public static readonly (int W, int H)[] Bank =
    {
        (49, 15), (49, 15), (49, 15), (49, 15), (49, 15), (49, 15),   //  1..6
        (25, 28), (25, 28), (25, 28), (25, 28),                       //  7..10
        (63, 28), (63, 28),                                           // 11..12
        (53, 54), (53, 54), (53, 54), (53, 54),                       // 13..16  Wettertafeln
        (22, 13), (22, 13),                                           // 17..18
        (6, 2), (6, 2),                                               // 19..20
        (57, 40), (57, 40), (57, 40), (57, 40), (57, 40),             // 21..25  EMBLEM
        (57, 40), (57, 40), (57, 40), (57, 40),                       // 26..29  EMBLEM
    };

    /// <summary>Der Versatz von Eintrag <paramref name="index"/> (1-basiert wie
    /// im Spiel) in BRIEFG.DAT. Eintrag 0 ist der Hintergrund auf 0.</summary>
    public static int BankOffset(int index)
    {
        int at = BackdropW * BackdropH;
        for (int i = 1; i < index && i <= Bank.Length; i++) at += Bank[i - 1].W * Bank[i - 1].H;
        return at;
    }

    /// <summary>Die Summe aller 29 Flächen — muss 43.302 sein, also genau der
    /// Schwanz von BRIEFG.DAT. Der Prüfstand rechnet das nach.</summary>
    public static int BankBytes()
    {
        int n = 0;
        foreach (var (w, h) in Bank) n += w * h;
        return n;
    }

    /// <summary>Das Emblem in den zwei Nischen: Bank-Einträge <b>21..29</b>,
    /// je <b>57×40</b>. Die Blitstelle <c>@0x4864DE</c> / <c>@0x48652D</c>
    /// schreibt sie auf <b>(285,432)</b> und <b>(575,432)</b>.
    ///
    /// <para>⚠ Das Bild ist 57×40, die Markierungsfläche im Hintergrundbild nur
    /// 55×38. Das Original zeichnet also <b>zwei Spalten und zwei Zeilen über
    /// die Nische hinaus</b>; nachgemessen fällt der Überstand rechts auf
    /// Palettenindex 47 (fast schwarz) und unten auf den Rahmen — er sieht man
    /// nicht. Das ist so gemessen und nicht zurechtgeschnitten.</para></summary>
    public const int EmblemFirst = 21, EmblemFrames = 9, EmblemW = 57, EmblemH = 40;
    public const int EmblemLX = 285, EmblemRX = 575, EmblemY = 432;

    /// <summary>
    /// <b>WELCHES DER NEUN BILDER GERADE STEHT</b> — die Rechnung des Originals,
    /// Befehl für Befehl von <c>@0x4864BC</c> (links) und <c>@0x486504</c>
    /// (rechts) abgelesen:
    ///
    /// <code>
    ///   links :  ecx = zaehler &amp; 0x0f ;  eax = |ecx - 8| ;  Bild = eax + 0x15
    ///   rechts:  eax = (zaehler + 8) &amp; 0x0f ; eax = |eax - 8| ; Bild = eax + 0x15
    /// </code>
    ///
    /// <para>Der Betrag macht das <b>Ping-Pong</b>: über 16 Zählerschritte läuft
    /// die Bildnummer 29,28,…,21,22,…,28 — der Schimmer wandert nach links und
    /// wieder zurück.</para>
    ///
    /// <para>Und die rechte Nische ist das <b>exakte Spiegelbild</b> der linken.
    /// Ausgerechnet über alle 16 Stände:</para>
    /// <code>
    ///   links   8 7 6 5 4 3 2 1 0 1 2 3 4 5 6 7
    ///   rechts  0 1 2 3 4 5 6 7 8 7 6 5 4 3 2 1
    /// </code>
    /// <para>— die <b>Summe ist überall 8</b>. ⚠ »Um 8 versetzt« wäre zu grob
    /// gesagt und stand hier zuerst falsch: an den Ständen 4 und 12 steht in
    /// beiden Nischen dasselbe Bild, die zwei Bahnen kreuzen sich in der Mitte.
    /// Der Prüfstand <c>--emblem-check</c> misst die Spiegelsumme, weil nur die
    /// ausnahmslos gilt.</para>
    ///
    /// <para>Rückgabe ist 0..8, also der Bildindex in unserem PNG-Satz
    /// (Bank-Eintrag 21 + Rückgabe).</para></summary>
    public static int NicheFrame(int counter, bool right)
    {
        int c = right ? (counter + 8) & 0x0f : counter & 0x0f;
        return System.Math.Abs(c - 8);
    }

    /// <summary>
    /// <b>DAS WASSERZEICHEN IST EINE EIGENE DATEI: SYMBOL.DAT.</b>
    ///
    /// <para>748.800 Byte, und sie teilt sich ohne Rest: <b>9 Bilder von
    /// 320×260</b>. Dass das die richtige Teilung ist, sagt der Lader selbst —
    /// <c>@0x45C110</c> öffnet SYMBOL.DAT und liest <b>260 Zeilen zu je 320
    /// Byte</b> in den Hintergrundpuffer auf <c>y+0x4F</c>, <c>x+0x128</c>, also
    /// auf <b>(296,79)</b>. Das ist Punkt für Punkt die Textplatte.</para>
    ///
    /// <para>Und der Zeichner <c>@0x486549</c> holt die anderen acht nach: bei
    /// Zählerstand <b>10..18</b> springt er in der Datei auf
    /// <c>(zaehler-10) * 325 * 256</c> — und 325·256 sind 83.200, also
    /// 320·260 — und blittet dasselbe Rechteck neu. <b>Das Wasserzeichen dreht
    /// sich einmal um die senkrechte Achse</b> (Bild 4 ist die Kante: nur 13
    /// Spalten belegt gegen 232 bei Bild 0) und bleibt dann auf Bild 8 stehen.
    /// Ausserhalb von 10..18 wird es nicht angefasst.</para>
    ///
    /// <para>⚠ 260 Zeilen, die Platte ist 240 hoch: die letzten 20 Zeilen laufen
    /// unter die Platte. Nachgemessen ist der Hintergrund dort ohnehin
    /// Palettenindex 47 und die Bilder sind dort leer — der Überlauf ist
    /// unsichtbar, aber er ist echt und wird nicht abgeschnitten.</para></summary>
    public const int MarkW = 320, MarkH = 260, MarkFrames = 9, MarkX = 296, MarkY = 79;

    /// <summary>Der Zählerbereich, in dem das Original das Wasserzeichen
    /// nachzieht — <c>cmp ax,0x0a / jb</c> und <c>cmp ax,0x13 / jae</c>
    /// @0x486550. Gibt 0..8 zurück oder −1 für »nichts zu tun«.</summary>
    public static int MarkFrame(int counter) =>
        counter >= 10 && counter < 19 ? counter - 10 : -1;

    /// <summary>Wie viele Nischenbilder geschrieben wurden.</summary>
    public int Emblems;

    /// <summary>Wie viele Wasserzeichenbilder geschrieben wurden.</summary>
    public int Marks;

    /// <summary>Die neun Nischenbilder aus BRIEFG.DAT nach
    /// <c>UI/emblem/niche/f0..f8.png</c>.</summary>
    public void WriteEmblem(byte[] dat, PalFile pal, Action<string>? say = null)
    {
        if (_ui.Length == 0) return;
        int need = BankOffset(EmblemFirst + EmblemFrames);
        if (dat.Length < need)
        {
            say?.Invoke($"BRIEFG.DAT zu kurz fuer das Emblem ({dat.Length} < {need})");
            return;
        }
        string dir = _ui + "/emblem/niche";
        Directory.CreateDirectory(dir);
        for (int f = 0; f < EmblemFrames; f++)
        {
            int at = BankOffset(EmblemFirst + f);
            var img = Godot.Image.CreateEmpty(EmblemW, EmblemH, false, Godot.Image.Format.Rgba8);
            for (int y = 0; y < EmblemH; y++)
                for (int x = 0; x < EmblemW; x++)
                {
                    byte v = dat[at + y * EmblemW + x];
                    img.SetPixel(x, y, Godot.Color.Color8(pal.R[v], pal.G[v], pal.B[v], 255));
                }
            img.SavePng($"{dir}/f{f}.png");
            Emblems++;
        }
        say?.Invoke($"Emblem (Nischen): {Emblems} Bilder {EmblemW}x{EmblemH} " +
                    $"aus BRIEFG.DAT ab Versatz {BankOffset(EmblemFirst)}");
    }

    /// <summary>Die neun Wasserzeichenbilder aus SYMBOL.DAT nach
    /// <c>UI/emblem/mark/f0..f8.png</c>, dazu <c>emblem_index.json</c>.</summary>
    public void WriteWatermark(byte[] sym, PalFile pal, Action<string>? say = null)
    {
        if (_ui.Length == 0) return;
        int frame = MarkW * MarkH;
        if (sym.Length < frame * MarkFrames)
        {
            say?.Invoke($"SYMBOL.DAT zu kurz ({sym.Length} < {frame * MarkFrames})");
            return;
        }
        string dir = _ui + "/emblem/mark";
        Directory.CreateDirectory(dir);
        for (int f = 0; f < MarkFrames; f++)
        {
            int at = f * frame;
            var img = Godot.Image.CreateEmpty(MarkW, MarkH, false, Godot.Image.Format.Rgba8);
            for (int y = 0; y < MarkH; y++)
                for (int x = 0; x < MarkW; x++)
                {
                    byte v = sym[at + y * MarkW + x];
                    img.SetPixel(x, y, Godot.Color.Color8(pal.R[v], pal.G[v], pal.B[v], 255));
                }
            img.SavePng($"{dir}/f{f}.png");
            Marks++;
        }
        File.WriteAllText($"{_ui}/emblem/emblem_index.json",
            "{\"_note\":\"Das Emblem von Akte Europa. Nischen: BRIEFG.DAT-Bank Eintraege 21..29, " +
            "je 57x40 - die Groessentabelle der 29 Eintraege steht im Lader @0x45BE6C..0x45C04E, " +
            "ihre Flaechensumme ist 43302 = der Schwanz der Datei. Wasserzeichen: SYMBOL.DAT, " +
            "9 Bilder 320x260, Lader @0x45C110 blittet 260 Zeilen zu 320 Byte auf (296,79), " +
            "Zeichner @0x486549 zieht bei Zaehlerstand 10..18 die uebrigen nach.\"," +
            $"\"niche\":{{\"frames\":{EmblemFrames},\"w\":{EmblemW},\"h\":{EmblemH}," +
            $"\"left\":{EmblemLX},\"right\":{EmblemRX},\"y\":{EmblemY}," +
            "\"phase\":\"links |c&15-8|+21, rechts |((c+8)&15)-8|+21 - @0x4864BC / @0x486504\"}," +
            $"\"mark\":{{\"frames\":{MarkFrames},\"w\":{MarkW},\"h\":{MarkH}," +
            $"\"x\":{MarkX},\"y\":{MarkY},\"window\":[10,18]}}}}",
            new UTF8Encoding(false));
        say?.Invoke($"Emblem (Wasserzeichen): {Marks} Bilder {MarkW}x{MarkH} aus SYMBOL.DAT");
    }

    /// <summary>One briefing: the mission it belongs to, its title and the
    /// body as paragraphs.</summary>
    public sealed class Briefing
    {
        public int Mission;
        public string Title = "";
        public List<string> Paragraphs = new();
    }

    /// <summary>Parses BRIEFG.TXT. Kept separate from writing so the self test
    /// can read the same bytes without a file being produced.</summary>
    public static List<Briefing> Parse(byte[] raw)
    {
        // cp437 char by char: Cp437.GetString stops at the first zero and drops
        // everything below 0x20, which would eat the CRs this format is built on
        var sb = new StringBuilder(raw.Length);
        foreach (byte b in raw) sb.Append(Cp437.Char(b));
        string text = sb.ToString();

        var list = new List<Briefing>();
        Briefing? cur = null;
        var body = new StringBuilder();

        void Close()
        {
            if (cur == null) return;
            foreach (string p in body.ToString().Split('\n'))
            {
                string t = p.Replace('\r', ' ').Trim();      // bare CR -> space: OURS
                while (t.Contains("  ")) t = t.Replace("  ", " ");
                if (t.Length > 0) cur.Paragraphs.Add(t);
            }
            if (cur.Paragraphs.Count > 0 || cur.Title.Length > 0) list.Add(cur);
            body.Clear();
        }

        foreach (string line in text.Split('\n'))
        {
            string l = line.TrimEnd('\r');
            if (l.StartsWith("#") && l.Length >= 4 && int.TryParse(l.Substring(1, 3), out int no))
            {
                Close();
                cur = new Briefing { Mission = no, Title = l[4..].Trim() };
                continue;
            }
            if (cur == null) continue;
            string s = l;
            if (s.StartsWith("^")) s = s[1..];               // the body marker
            body.Append(s).Append('\n');
        }
        Close();
        return list;
    }

    public void Write(byte[] raw, Action<string>? say = null)
    {
        var list = Parse(raw);
        Directory.CreateDirectory(_dst);
        var sb = new StringBuilder(1 << 16);
        sb.Append("{\"_note\":\"mission briefings from BRIEFG.TXT in the InstallShield cabinet, cp437\",");
        sb.Append("\"_format\":\"records '#NNN title', body after '^ '; CR LF is a paragraph, ");
        sb.Append("a bare CR is the original's own line break\",");
        sb.Append("\"_wrap\":\"a bare CR is turned into a space and the text re-wrapped by the ");
        sb.Append("screen - OURS, because the original's breaks belong to its fixed-width display\",");
        sb.Append("\"briefings\":{");
        bool first = true;
        foreach (var b in list)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{b.Mission}\":{{\"title\":\"{Esc(b.Title)}\",\"paragraphs\":[");
            for (int i = 0; i < b.Paragraphs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"\"{Esc(b.Paragraphs[i])}\"");
            }
            sb.Append("]}");
            Briefings++;
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/briefings.json", sb.ToString(), new UTF8Encoding(false));
        say?.Invoke($"Briefings: {Briefings} Missionstexte");
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c < 0x20 ? "" : c.ToString(),
            });
        return sb.ToString();
    }
}
