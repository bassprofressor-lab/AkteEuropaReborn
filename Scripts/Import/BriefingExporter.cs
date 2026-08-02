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
/// <para>BRIEFG.DAT — 350,502 bytes of palette indices that go with these — is a
/// separate question and is not read here.</para>
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
