namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

/// <summary>
/// The last of the four extractors: the game's own look. Its typeface, its side
/// panel and its effects, all three out of the cabinet the discs carry them in.
///
///   UI/akte_font.png + .fnt   FONT.CWD as a BMFont Godot loads as a real Font
///   UI/panel.png              PANEL.DTA, 204 x 170
///   UI/portraits/pNN.png      ANIM.CWA 400..403 — die 86 Bauteilbilder
///   Effects/&lt;name&gt;/fNN.png    hand-picked ANIM.CWA sequences
///
/// Ported from font_export.py, ui_export.py and export_effects.py.
/// </summary>
public sealed class InterfaceExporter
{
    private readonly PalFile _pal;
    private readonly string _ui, _fx;

    public int Glyphs, PanelPixels, Effects, EffectFrames;

    /// <summary>Wieviele der 86 Bauteilbilder geschrieben wurden, und wieviele
    /// davon leer sind (Bild 0 ist es).</summary>
    public int Portraits, PortraitsEmpty;

    public InterfaceExporter(PalFile pal, string uiDir, string effectsDir)
    {
        _pal = pal;
        _ui = uiDir.TrimEnd('/', '\\');
        _fx = effectsDir.TrimEnd('/', '\\');
    }

    // ---- FONT.CWD -----------------------------------------------------------

    public const int GlyphRecord = 131, CellHeight = 13, GlyphCount = 160, AtlasCols = 16;

    /// <summary>How dark the second colour slot is relative to the first — the
    /// measured mean of the seven pairs the game's own callers pass. Kept as a
    /// number so the Python reference and this agree by construction rather
    /// than by memory: font_export.py has the same 0.76.</summary>
    public const float ShadeF = 0.76f;
    public const string FontName = "akte_font";

    /// <summary>The second typeface, FONT2.CWD — same 160-glyph layout, thinner
    /// letters. It belongs to the briefing screen: the loader @0x45bddc reads it
    /// immediately before BRIEFG.DAT, which is that screen's backdrop.</summary>
    public const string Font2Name = "akte_font2";

    /// <summary>How many typefaces were written.</summary>
    public int Fonts;

    /// <summary>FONT.CWD: 160 records of 131 bytes, `[u8 width][width x 13]`,
    /// and the character is the record index plus 0x20 (glyph blitter
    /// @0x4ba2b0, which multiplies the index by 131).
    ///
    /// The cell is a MASK, and it has <b>two</b> replaceable slots, not one.
    /// The glyph blitter @0x4ba2b0 is four lines long:
    /// <code>
    ///   b == 0xFF -> skip          ; transparent
    ///   b == 0xFE -> arg4          ; the first colour the caller passes
    ///   b == 0x24 -> arg5          ; the second one
    ///   else      -> write b       ; a literal palette index
    /// </code>
    ///
    /// <para>⚠ <b>Corrected 02.08.2026.</b> This used to call 0x24 "the shadow
    /// colour" and write it BLACK. It is not a shadow and it is not fixed: the
    /// callers (@0x4ba499..0x4ba4c5 and @0x45c2f6) always push a PAIR out of one
    /// ramp — (0x99,0x9c) (0x96,0xa9) (0x7c,0x7f) (0x54,0x57) (0x62,0x65)
    /// (0x35,0x37) (6,7) — and measured in DATA/01.PAL the second is the same
    /// hue at <b>0.62 to 0.84 of the first's brightness, mean 0.76</b>. It is a
    /// shading colour.</para>
    ///
    /// <para>So 0xFE goes in WHITE and 0x24 at that 0.76 grey, and a Godot
    /// `font_color` then modulates both — text in the colour, shading three
    /// quarters of it, which is the relation the original has. Writing it black
    /// is what swallowed the start menu's letters on a dark window.</para>
    /// </summary>
    public void WriteFont(byte[] font, string name = FontName, string source = "FONT.CWD")
    {
        Directory.CreateDirectory(_ui);
        int maxW = 1;
        for (int i = 0; i < GlyphCount; i++)
        {
            int b = i * GlyphRecord;
            if (b < font.Length) maxW = Math.Max(maxW, font[b]);
        }
        int rows = (GlyphCount + AtlasCols - 1) / AtlasCols;
        var atlas = Image.CreateEmpty(AtlasCols * maxW, rows * CellHeight, false, Image.Format.Rgba8);
        atlas.Fill(new Color(0, 0, 0, 0));

        var chars = new StringBuilder();
        int count = 0;
        for (int i = 0; i < GlyphCount; i++)
        {
            int b = i * GlyphRecord;
            if (b + GlyphRecord > font.Length) break;
            int w = font[b];
            int gx = i % AtlasCols * maxW, gy = i / AtlasCols * CellHeight;
            for (int y = 0; y < CellHeight; y++)
                for (int x = 0; x < w; x++)
                {
                    // the cell is 130 bytes, so a glyph holds at most 130
                    // pixels: at width 11 or more the last row does not fit and
                    // is simply not there. Reading on would take the bytes of
                    // the NEXT glyph — six coloured specks in the atlas, which
                    // is how this was caught.
                    int k = y * w + x;
                    if (k >= GlyphRecord - 1 || b + 1 + k >= font.Length) continue;
                    byte v = font[b + 1 + k];
                    if (v == 0xFF) continue;
                    Color c = v switch
                    {
                        0xFE => new Color(1, 1, 1, 1),                 // slot 1, modulated
                        0x24 => new Color(ShadeF, ShadeF, ShadeF, 1),  // slot 2, three quarters of it
                        _ => Color.Color8(_pal.R[v], _pal.G[v], _pal.B[v], 255),
                    };
                    atlas.SetPixel(gx + x, gy + y, c);
                }
            int code = i + 0x20;
            int uni = code >= 0x80 ? Cp437.Char((byte)code) : code;
            chars.Append($"char id={uni} x={gx} y={gy} width={w} height={CellHeight} ");
            // ⚠⚠ 19.08.2026 — HIER STAND `w + 1`, UND DAS WAR DER GROESSTE
            // ANTEIL AM ZU BREITEN SATZ.
            //
            // Gemeldet, mehrfach: »der Text im Kampagnen-Vorschaufenster ist
            // immer noch zu gross«. Das Original rueckt um GENAU die
            // Glyphenbreite vor, ohne Zuschlag — nachgelesen an zwei Stellen,
            // die dieselbe Tafel summieren:
            //
            //   Breitenrechnung 0x45A560 (in beiden GAME.EXE gleiche Form):
            //     movsx ax, byte[eax + ecx*2 + 0xB253E0]   ; eax+ecx*2 = 131*c
            //     add   bp, ax                             ; ROH addiert
            //   Glyphenzeichner 0x4BA2B0, Rueckgabewert:
            //     mov eax, [esp+0x10]   ; die Breite
            //     add eax, edx          ; neues x = x + Breite
            //
            // ⚠ Gemessen am UMBRUCH, nicht am eingestellten Punktwert — das ist
            // die Lehre aus einem frueheren Fehlversuch. Derselbe Missionstext
            // braucht im Original 5163 px Gesamtvorschub, mit `w+1` 6050:
            // **17,2 % zu breit**. Bei einer mittleren Glyphenbreite von 5,64 px
            // ist ein zusaetzlicher Bildpunkt fast ein Fuenftel.
            //
            // ⚠ Die Zwillingsfassung in aekernel-tools/font_export.py hatte
            // denselben Fehler und ist mitgeaendert.
            chars.Append($"xoffset=0 yoffset=0 xadvance={w} page=0 chnl=15\n");
            count++;
            Glyphs++;
        }
        atlas.SavePng($"{_ui}/{name}.png");

        var fnt = new StringBuilder();
        fnt.Append($"info face=\"Akte Europa\" size={CellHeight} bold=0 italic=0 charset=\"\" ");
        fnt.Append("unicode=1 stretchH=100 smooth=0 aa=1 padding=0,0,0,0 spacing=0,0\n");
        fnt.Append($"common lineHeight={CellHeight + 2} base={CellHeight - 2} ");
        fnt.Append($"scaleW={atlas.GetWidth()} scaleH={atlas.GetHeight()} pages=1 packed=0\n");
        fnt.Append($"page id=0 file=\"{name}.png\"\n");
        fnt.Append($"chars count={count}\n");
        fnt.Append(chars);
        File.WriteAllText($"{_ui}/{name}.fnt", fnt.ToString(), new UTF8Encoding(false));

        var idx = new StringBuilder();
        idx.Append($"{{\"source\":\"{source}\",\"glyphs\":{count},\"cell_h\":{CellHeight},");
        idx.Append($"\"atlas\":[{atlas.GetWidth()},{atlas.GetHeight()}],");
        idx.Append("\"note\":\"text pixels white (modulate with font_color), shadow pixels ");
        idx.Append("black; chars >=0x80 are cp437\"}");
        File.WriteAllText($"{_ui}/{name}_index.json", idx.ToString(), new UTF8Encoding(false));
        Fonts++;
    }

    // ---- PANEL.DTA ----------------------------------------------------------

    public const int PanelW = 204, PanelH = 170;

    /// <summary>PANEL.DTA is a raw raster with no header at all; its WIDTH was
    /// the whole puzzle. The loader @0x4b9f70 walks the buffer building run
    /// lengths, and the index of a row's last byte starts at 0xCB = 203 and
    /// advances by 0xCC = 204 (@0x4ba03c) — so 204 bytes to the row, and
    /// 34680 / 204 = 170 rows exactly. 0xFF is transparent.</summary>
    public void WritePanel(byte[] panel)
    {
        Directory.CreateDirectory(_ui);
        if (panel.Length != PanelW * PanelH)
            throw new InvalidDataException($"PANEL.DTA ist {panel.Length} Bytes, erwartet {PanelW * PanelH}");
        var img = Image.CreateEmpty(PanelW, PanelH, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int y = 0; y < PanelH; y++)
            for (int x = 0; x < PanelW; x++)
            {
                byte v = panel[y * PanelW + x];
                if (v == 0xFF) continue;
                img.SetPixel(x, y, Color.Color8(_pal.R[v], _pal.G[v], _pal.B[v], 255));
                PanelPixels++;
            }
        img.SavePng($"{_ui}/panel.png");

        var sb = new StringBuilder();
        sb.Append($"{{\"source\":\"PANEL.DTA\",\"size\":[{PanelW},{PanelH}],\"stride\":{PanelW},");
        sb.Append("\"stride_evidence\":\"loader 0x4b9f70: row end index starts 0xCB, ");
        sb.Append("advances by 0xCC (@0x4ba03c) until 0x8777\",");
        sb.Append("\"display_box\":{\"x\":8,\"y\":43,\"w\":153,\"h\":94},");
        sb.Append("\"transparent\":255,\"palette\":\"DATA/01.PAL\"}");
        File.WriteAllText($"{_ui}/panel_index.json", sb.ToString(), new UTF8Encoding(false));
    }

    // ---- WINDOWS.CWW: die WINDFAHNE ----------------------------------------

    /// <summary>Die acht Stellungen der Windfahne aus <c>WINDOWS.CWW</c>.
    ///
    /// <para><b>Was das Ding ist</b>, sagt das Bedienfeld selbst: <c>panel_draw</c>
    /// enthält GENAU EINEN Bildaufruf (@0x46FF62), und der rechnet
    /// <c>261 + ((byte[0x4F8D68] + 4) &amp; 7)</c> und zeichnet bei (90, 147).
    /// <c>0x4F8D68</c> ist die WINDRICHTUNG — dieselbe Stelle, aus der die
    /// Waldbrandausbreitung ihre Richtung nimmt. Das kleine runde Ding unten in
    /// der Mitte ist also keine Kompassrose, sondern eine <b>Windfahne</b>, und
    /// das <c>+4</c> heißt: sie zeigt in die GEGENRICHTUNG.</para>
    ///
    /// <para><b>Der Bestand</b>: <c>0x455D50</c> liest <c>windows.cww</c> am
    /// Stück nach <c>0x8938D8</c> (<c>fread(…, 1, 0x21BB0, f)</c>), und die
    /// Zeichenroutine @0x455DB0 rechnet die Satzadresse als <c>440·Nummer</c>
    /// (<c>lea</c>-Kette @0x455DB9..0x455DCF) und beginnt die Bildpunkte bei
    /// <c>+0x16</c>. Die Datei ist 138.160 Byte = <b>314 Sätze zu 440</b>, und
    /// 440 − 0x16 = 418 = <b>22 × 19</b> — die Bildgröße geht also genau auf.</para>
    ///
    /// <para>⚠⚠ <b>DAS SATZFORMAT, BERICHTIGT.</b> Zuerst hatte ich die 418
    /// Bytes hinter dem Kopf als flaches 22×19-Feld gelesen — 418 = 22·19 geht
    /// glatt auf, und das Bild sah RICHTIG aus. Es war trotzdem falsch. Der
    /// Blit-Rumpf @0x455DDF..0x455E25 sagt es genau: er zählt <b>20 Zeilen</b>
    /// (<c>mov dword [esp+0xc], 0x14</c> @0x455DC3), nimmt je Zeile zwei
    /// Kopfbytes — <c>[+0] Startspalte</c>, <c>[+1] Länge</c> (@0x455DE3,
    /// @0x455DE9) — kopiert danach <c>Länge</c> Bildpunkte und rückt um
    /// <c>0x16 = 22</c> Byte vor (@0x455DFA). Ein Satz ist also
    /// <b>20 Zeilen à 22 Byte</b> = 440, und die Bildpunkte einer Zeile
    /// beginnen bei <c>+2</c>.
    ///
    /// Die flache Lesart nahm die zwei Kopfbytes der NÄCHSTEN Zeile als
    /// Bildpunkte 20 und 21 der laufenden und verschob damit jede Zeile um
    /// zwei Punkte. Bei der Fahne fiel das kaum auf, weil alle 20 Zeilen
    /// <c>0/20</c> tragen (nachgezählt) — ein Beinahe-Treffer, kein Treffer.</para>
    ///
    /// <para>Die Palette läuft über denselben Weg wie alles andere
    /// (<see cref="PalFile"/> nimmt die Rohwerte). ⚠ Gegengeprüft an den
    /// Bildschirmfotos des Spielers: schwarzes Zifferblatt, Messinggehäuse,
    /// rot-orange Nadel — Pixel für Pixel dieselben Farben. (Beim Lesen hatte
    /// ich zuerst mit ×4 gestreckt und ein olivgrünes Blatt bekommen; das war
    /// mein Fehler, nicht der der Datei.)</para></summary>
    public const int VaneW = 20, VaneH = 20, VaneFirst = 261, VaneCount = 8;
    private const int CwwStride = 440, CwwRow = 22, CwwRows = 20;

    public int VaneFrames { get; private set; }

    public void WriteWindVane(byte[] cww)
    {
        Directory.CreateDirectory(_ui);
        int need = CwwStride * (VaneFirst + VaneCount);
        if (cww.Length < need)
            throw new InvalidDataException(
                $"WINDOWS.CWW ist {cww.Length} Bytes, fuer Satz {VaneFirst + VaneCount - 1} " +
                $"werden {need} gebraucht");
        var img = Image.CreateEmpty(VaneW * VaneCount, VaneH, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int k = 0; k < VaneCount; k++)
        {
            for (int y = 0; y < CwwRows; y++)
            {
                int row = CwwStride * (VaneFirst + k) + CwwRow * y;
                int x0 = cww[row], len = cww[row + 1];
                for (int i = 0; i < len && i < CwwRow - 2; i++)
                {
                    int x = x0 + i;
                    if (x >= VaneW) break;
                    byte v = cww[row + 2 + i];
                    if (v == 0xFF) continue;
                    img.SetPixel(k * VaneW + x, y,
                                 Color.Color8(_pal.R[v], _pal.G[v], _pal.B[v], 255));
                }
            }
            VaneFrames++;
        }
        img.SavePng($"{_ui}/windvane.png");

        var sb = new StringBuilder();
        sb.Append("{\"source\":\"WINDOWS.CWW\",\"record_stride\":440,");
        sb.Append("\"rows\":20,\"row_stride\":22,\"row\":\"[startspalte][laenge][bis 20 punkte]\",");
        sb.Append($"\"first\":{VaneFirst},\"count\":{VaneCount},");
        sb.Append($"\"size\":[{VaneW},{VaneH}],\"layout\":\"frame = (wind + 4) & 7\",");
        sb.Append("\"draw_at\":[90,147],\"evidence\":\"panel_draw @0x46FF3B liest ");
        sb.Append("byte[0x4F8D68], addiert 4, mod 8, plus 0x105; zeichnet @0x46FF62\",");
        sb.Append("\"transparent\":255,\"palette\":\"DATA/01.PAL (unskaliert)\"}");
        File.WriteAllText($"{_ui}/windvane_index.json", sb.ToString(), new UTF8Encoding(false));
    }

    // ---- ANIM.CWA -----------------------------------------------------------

    /// <summary>The sequences picked off the 141-sequence contact sheet. Which
    /// ones they are is OUR choice — the blob holds far more than the remake
    /// draws.</summary>
    private static readonly (string Name, int Seq)[] Picked =
    {
        ("muzzle", 232), ("explosion", 48), ("blast", 550), ("wreck", 0),
        // ⚠ 19.08.2026 — DIE ZWEITE FLAMME. Der Zeichner des Originals
        // @0x42B461 rechnet `edi = (index & 1) * 2 + 0x226`, also **550 ODER
        // 552** je nachdem, ob der Tafelindex des Baums gerade oder ungerade
        // ist. Beide Folgen haben in ANIM.CWA sieben Bilder (550 ab Bild 1459,
        // 552 ab 1473; die 551 dazwischen ist ebenfalls belegt, wird von DIESEM
        // Weg aber nicht gerufen). Wir hatten nur die eine — damit flackerten
        // alle brennenden Baeume mit demselben Bild.
        ("blast2", 552),
        // ⚠ 18.08.2026 — DAS FEUER. Gemeldet als »in Original Kampagne 1 gibt
        // es z.B. von Haus aus ein paar brennende Baeume, die haben wir
        // garnicht«. Die Folge 82 ist NICHT geraten: die Trefferroutine Zasah
        // schiebt sie @0x40CB07 zusammen mit Spalte, Zeile und Hoehe der
        // getroffenen Sache in den Effektaufruf —  ist die einzige
        // kleine Zahl im ganzen Rumpf.
        ("fire", 82),
        // The BUILDING DOORS, and this one is not our choice — the game says so.
        // The draw code @0x42B338 computes `tile*4 + word[0x7a44fe] + phase`,
        // and 0x7a44fe is the `first frame` field of ANIM.CWA sequence 301
        // (the loader @0x435710 reads the 4000-byte sequence table to 0x7a4048,
        // and 0x7a44fe - 0x7a4048 = 1206 = 301*4 + 2).
        //
        // The arithmetic closes: sequence 301 holds **76 frames = 19 doors x 4
        // phases**, and the highest door tile the jump table @0x42bdd8 hands
        // out is 18 — 18*4 + 3 = 75, exactly the last frame. Rendered and
        // looked at: they are doors, and phase 0 against phase 3 is shut
        // against open.
        ("door", 301),
    };

    /// <summary>
    /// <b>⚠ 19.08.2026 — HIER STAND EINE LISTE VON ZWEI, UND DAS SPIEL HAT
    /// DREISSIG.</b>
    ///
    /// <para>Bis heute wurden genau zwei Flugbilder ausgegeben:
    /// <c>("rocket_l", 64)</c> und <c>("rocket_h", 65)</c>. Die beiden Zahlen
    /// waren richtig — aber es waren zwei von dreissig, herausgegriffen, weil
    /// wir nur zwei Waffen als »Raketenwerfer« eingestuft hatten. Welche Waffe
    /// ein fliegendes Geschoss hat, entscheidet das Spiel selbst, und zwar in
    /// der Geschosstafel <c>0x4F98E8</c>: Feld +0x02 ist die Flugfolge,
    /// Feld +0x06 die Einschlagfolge, <b>30000 heisst keine</b>.</para>
    ///
    /// <para>Dass 30000 die Marke ist und keine Folgennummer, ist keine
    /// Vermutung: ANIM.CWA fuehrt <see cref="AnimFile.SeqCount"/> = <b>1000</b>
    /// Folgen. 30000 gibt es dort nicht.</para>
    ///
    /// <para>Ausgezaehlt kommen dabei <b>30 Flugfolgen</b> und <b>15
    /// Einschlagfolgen</b> heraus. Zwei der Einschlagfolgen (87 und 309) sind in
    /// ANIM.CWA <b>leer</b> — die betreffenden Waffen zeigen im Original also
    /// keinen Einschlag, und das ist ein Befund, kein Fehler. Sie werden hier
    /// uebersprungen, damit die Laufzeit gar nicht erst nach ihnen sucht.</para>
    ///
    /// <para><b>Die Richtung.</b> Der Zeichner @0x42B198 rechnet
    /// <c>Bild = Folgenanfang + Satz[+0x26] + Richtung</c> und holt die Richtung
    /// nur, wenn die Geschossart in <b>2..86</b> liegt (@0x42B177:
    /// <c>lea ecx,[edi-2]; cmp ecx,0x54; ja</c> → sonst <c>xor cl,cl</c>).
    /// Deshalb wird die Richtung hier NICHT am Bilderzaehler festgemacht,
    /// sondern zur Laufzeit an der Art — eine Folge wie 61 (ein einziges Bild)
    /// wird von Art 1 UND Art 21 benutzt, und nur die zweite ist im
    /// Richtungsbereich.</para>
    ///
    /// <para>⚠ OFFEN: ob <c>Satz[+0x26]</c> in Achterschritten zaehlt. Wir
    /// zeichnen weiter <c>Bild = Phase*8 + Richtung</c>, was bei den Folgen
    /// 64/65 sichtbar stimmt; die Formel des Originals liesse auch
    /// <c>Phase + Richtung</c> zu. Das steht in OFFENE_FRAGEN.md.</para>
    /// </summary>
    private static string FlightDir(int seq) => $"flug_{seq}";
    private static string ImpactDir(int seq) => $"schlag_{seq}";

    public void WriteEffects(AnimFile anim) => WriteEffects(anim, null);

    public void WriteEffects(AnimFile anim, ExeTables? exe)
    {
        Directory.CreateDirectory(_fx);
        var sb = new StringBuilder();
        sb.Append("{\"source\":\"ANIM.CWA\",\"palette\":\"DATA/01.PAL\",\"effects\":{");
        bool first = true;

        foreach (var (name, seq) in Picked)
        {
            var frames = Decode(anim, seq, int.MaxValue);
            if (frames.Count == 0) continue;
            var (w, h) = Bounds(frames);
            for (int i = 0; i < frames.Count; i++)
                Save($"{_fx}/{name}/f{i:00}.png", Canvas(frames[i], w, h));
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{name}\":{{\"sequence\":{seq},\"frames\":{frames.Count},");
            sb.Append($"\"w\":{w},\"h\":{h},\"anchor\":[{w / 2},{h / 2}]}}");
            Effects++;
        }

        // Die Flug- und Einschlagbilder kommen aus der Geschosstafel, nicht aus
        // einer Liste hier. Ohne GAME.EXE bleibt es beim Alten -- dann fehlen
        // sie eben, statt dass geraten wird.
        if (exe != null)
        {
            var tafel = exe.Projectiles();
            var flug = new SortedSet<int>();
            var schlag = new SortedSet<int>();
            foreach (var p in tafel)
            {
                if (p.Speed == 0) continue;              // unbenutzte Zeile
                if (p.Flight != ExeTables.KeineFolge) flug.Add(p.Flight);
                if (p.Impact != ExeTables.KeineFolge) schlag.Add(p.Impact);
            }

            // FLUG: unangetastete Nummerierung f0..fN, denn der Zeichner holt
            // sich das Bild ueber ProjectileTexture(art, richtung) ohne Polster.
            foreach (int seq in flug)
            {
                var frames = Decode(anim, seq, int.MaxValue);
                if (frames.Count == 0) continue;
                var (w, h) = Bounds(frames);
                for (int f = 0; f < frames.Count; f++)
                    Save($"{_fx}/{FlightDir(seq)}/f{f}.png", Canvas(frames[f], w, h));
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"\"{FlightDir(seq)}\":{{\"sequence\":{seq},");
                sb.Append($"\"frames\":{frames.Count},\"w\":{w},\"h\":{h},");
                sb.Append($"\"anchor\":[{w / 2},{h / 2}]}}");
                Effects++;
            }

            // EINSCHLAG: gepolstert f00.., denn den holt EffectFrames.
            foreach (int seq in schlag)
            {
                var frames = Decode(anim, seq, int.MaxValue);
                if (frames.Count == 0) continue;         // 87 und 309 sind leer
                var (w, h) = Bounds(frames);
                for (int f = 0; f < frames.Count; f++)
                    Save($"{_fx}/{ImpactDir(seq)}/f{f:00}.png", Canvas(frames[f], w, h));
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"\"{ImpactDir(seq)}\":{{\"sequence\":{seq},");
                sb.Append($"\"frames\":{frames.Count},\"w\":{w},\"h\":{h},");
                sb.Append($"\"anchor\":[{w / 2},{h / 2}]}}");
                Effects++;
            }
        }

        sb.Append("}}");
        File.WriteAllText($"{_fx}/effects_index.json", sb.ToString(), new UTF8Encoding(false));

        // ⚠ Die Bauteilbilder hängen HIER dran, obwohl sie nach UI/ gehören: sie
        // kommen aus derselben ANIM.CWA, und damit schreibt sie sowohl der
        // vollständige Import als auch `--reexport-effects=<Quelle>`, ohne dass
        // ein zweiter Schalter in einer fremden Datei nötig wäre.
        WritePortraits(anim);
        WritePanelIcons(anim);
    }

    // ---- die MAUSZEIGER aus ROBO.CWR ---------------------------------------

    /// <summary>
    /// <b>Alle Mauszeiger des Spiels</b> als PNG, einer je Bild.
    ///
    /// <para>Sie liegen im Anhang von ROBO.CWR — siehe
    /// <see cref="CwrFile.Cursors"/> für die Tafel, den Blob und die
    /// Codestellen. Geschrieben wird nach <c>UI/cursors/tNN_fM.png</c>, dazu
    /// ein Verzeichnis mit Maßen und Nullpunkt.</para>
    ///
    /// <para>⚠ <b>Der Griff liegt bei (32, 32), in BEIDEN Achsen.</b> Das
    /// Original zeichnet ab (MausX−32, MausY−32), also fällt die Maus auf
    /// den Bildpunkt (32, 32) der gezeichneten Fläche.
    ///
    /// ⚠⚠ Hier stand zuerst <c>(32, 32−yoff)</c> — der Gedanke war, dass ein
    /// Bild mit <c>yoff</c>=16 sechzehn Punkte tiefer anfängt. Das ist wahr und
    /// hier trotzdem falsch: <see cref="Canvas"/> legt den Versatz bereits als
    /// Rand oben an, die Fläche ist <c>yoff+h</c> hoch. Wer ihn noch einmal
    /// abzieht, zählt ihn doppelt.
    ///
    /// NACHGEMESSEN an den geschriebenen Bildern (Deckkraft &gt; 0): der
    /// Angriffszeiger füllt x 17..48, y 16..47 — genau 32×32 —, seine Mitte
    /// liegt also auf (32,5 / 31,5), und (32,32) ist die Mitte des Fadenkreuzes.
    /// Beim Pfeil (Typ 0, x 30..48, y 30..57) trifft (32,32) die SPITZE. Beide
    /// Proben gehen nur mit dem unveränderten 32 auf.</para>
    /// </summary>
    public void WriteCursors(CwrFile robo)
    {
        string dir = _ui + "/cursors";
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        sb.Append("{\"source\":\"ROBO.CWR\",\"palette\":\"DATA/01.PAL\",");
        sb.Append("\"hotspot_from_origin\":").Append(CwrFile.CursorHotspot);
        sb.Append(",\"attack\":").Append(CwrFile.CursorAttack);
        sb.Append(",\"move\":").Append(CwrFile.CursorMove);
        sb.Append(",\"cursors\":{");
        bool first = true;
        int bilder = 0;
        foreach (var c in robo.Cursors())
        {
            var (w, h) = Bounds(c.Frames);
            for (int i = 0; i < c.Frames.Count; i++)
            {
                Save($"{dir}/t{c.Type:00}_f{i}.png", Canvas(c.Frames[i], w, h));
                bilder++;
            }
            if (!first) sb.Append(',');
            first = false;
            int yoff = c.Frames[0].YOffset;
            sb.Append($"\"{c.Type}\":{{\"frames\":{c.Frames.Count},\"w\":{w},");
            sb.Append($"\"h\":{h},\"yoff\":{yoff},");
            sb.Append($"\"hotspot\":[{CwrFile.CursorHotspot},");
            sb.Append($"{CwrFile.CursorHotspot}]}}");
            Cursors++;
        }
        sb.Append("}}");
        File.WriteAllText($"{dir}/cursors_index.json", sb.ToString(), new UTF8Encoding(false));
        GD.Print($"Mauszeiger: {Cursors} Arten mit {bilder} Bildern aus dem " +
                 $"ROBO.CWR-Anhang -> {dir}");
    }

    /// <summary>Wie viele Zeigerarten <see cref="WriteCursors"/> geschrieben
    /// hat.</summary>
    public int Cursors { get; private set; }

    // ---- die drei Symbole im Bedienblock ------------------------------------

    /// <summary>Die Folge, in der Herz, Kanister und Patronen liegen. Der
    /// Zeichner 0x44FEB0 liest sie über <c>word[0x7A44FA]</c>, und das ist der
    /// Startrahmen der Folge — nicht eine feste Bildnummer.</summary>
    public const int PanelIconSeq = 300;

    /// <summary>Die drei Versatzwerte in der Folge. Sie stehen als
    /// Tabellenzugriff im Code: <c>[eax*4 + 0x8155A0]</c>, <c>+0x8155A4</c> und
    /// <c>+0x8155A8</c> gegen den Tabellenanfang 0x815580 — also
    /// <c>0x20/4 = 8</c>, 9 und 10.</summary>
    public static readonly int[] PanelIconAt = { 8, 9, 10 };

    public static readonly string[] PanelIconName = { "heart", "fuel", "ammo" };

    /// <summary>
    /// <b>HERZ, KANISTER UND PATRONEN</b> — die drei Symbole, die im
    /// Bedienblock links neben den Statusbalken stehen.
    ///
    /// <para>Sie waren lange gesucht und standen in <see cref="Rendering
    /// .MapEntityLayer"/> als ausdrückliche Lücke vermerkt (»weder in PANEL.DTA
    /// noch in CONTROL.CWD«). Beides stimmte — sie liegen in <b>ANIM.CWA</b>,
    /// derselben Datei wie die Explosionen.</para>
    ///
    /// <para><b>Die Fundstelle</b> ist der Zeichner <c>0x44FEB0</c>, erreicht
    /// aus <c>panel_draw</c> (0x46FE10) über <c>0x46FF29</c>:
    /// <list type="bullet">
    /// <item><c>0x44FEF8: movsx eax, word[0x7A44FA]</c> — der Startrahmen der
    /// Folge 300</item>
    /// <item><c>0x44FF03: mov ecx, [eax*4 + 0x8155A0]</c> → Versatz 8, gezeichnet
    /// an (72, 61)</item>
    /// <item><c>0x44FF42: [eax*4 + 0x8155A4]</c> → Versatz 9, an (72, 81)</item>
    /// <item><c>0x44FF83: [eax*4 + 0x8155A8]</c> → Versatz 10, an (72, 101)</item>
    /// </list>
    /// Ein zweiter Zweig ab <c>0x44FF95</c> zeichnet dieselben drei Bilder für
    /// ein Objekt aus dem Bereich 0x4E20..0x4F4C (Flugzeugplatz) bei x=71.</para>
    ///
    /// <para>⚠ <b>Die Folge, nicht die Bildnummer.</b> Der Code rechnet den
    /// Rahmen aus dem Folgenanfang aus; wir tun dasselbe. Bei der ausgelieferten
    /// ANIM.CWA fällt daraus 960/961/962, aber diese Zahlen stehen NICHT im
    /// Programm und werden darum auch hier nicht hingeschrieben.</para>
    ///
    /// <para>Nebenbefund, nicht gebaut: derselben Folge gehört bei Versatz 11
    /// ein <b>Blitz</b> an, den 0x44FEB0 nicht zeichnet (kein Zugriff auf
    /// 0x8155AC). Wo er hingehört, ist ungelesen.</para>
    /// </summary>
    public void WritePanelIcons(AnimFile anim)
    {
        Directory.CreateDirectory(_ui);
        var (count, start) = anim.Sequence(PanelIconSeq);
        var sb = new StringBuilder();
        sb.Append("{\"source\":\"ANIM.CWA\",\"sequence\":").Append(PanelIconSeq);
        sb.Append(",\"first_frame\":").Append(start).Append(",\"icons\":{");
        int written = 0;
        for (int i = 0; i < PanelIconAt.Length; i++)
        {
            int off = PanelIconAt[i];
            if (off >= count) continue;
            var f = anim.Frame(start + off);
            if (f == null) continue;
            var img = Canvas(f, f.Width, f.YOffset + f.Height);
            Save($"{_ui}/panel_{PanelIconName[i]}.png", img);
            if (written > 0) sb.Append(',');
            sb.Append($"\"{PanelIconName[i]}\":{{\"offset\":{off},");
            sb.Append($"\"frame\":{start + off},\"w\":{f.Width},");
            sb.Append($"\"h\":{f.YOffset + f.Height},\"yoff\":{f.YOffset}}}");
            written++;
        }
        sb.Append("}}");
        File.WriteAllText($"{_ui}/panel_icons_index.json", sb.ToString(),
                          new UTF8Encoding(false));
        GD.Print($"Bedienblock-Symbole: {written} von {PanelIconAt.Length} aus " +
                 $"ANIM.CWA Folge {PanelIconSeq} (Rahmen {start}+" +
                 string.Join("/", PanelIconAt) + $") -> {_ui}");
    }

    // ---- die 86 Bauteilbilder aus ANIM.CWA ----------------------------------

    /// <summary>Die vier Folgen, in denen die Bank liegt. Sie sind
    /// LÜCKENLOS — 1176 + 57 = 1233, + 10 = 1243, + 7 = 1250, + 12 = 1262 —
    /// darum ist »Bildnummer« schlicht <c>Rahmen − seq400.start</c>.</summary>
    public const int PortraitSeq0 = 400, PortraitSeqN = 4;

    /// <summary>Die Kantenlänge der Leinwand. Gemessen, nicht gewählt: über
    /// alle 86 Bilder ist <c>max(Breite, yoff + Zeilen)</c> genau
    /// <b>60</b> — und der Vorschaukasten des Originals ist
    /// <c>0x456A50(520, 40, 3, 3)</c>, also 3 Zellen von 20 px = ebenfalls
    /// 60×60. Die zwei Zahlen treffen sich, und das ist der Grund, warum jedes
    /// Bild auf DIESELBE Leinwand gelegt werden darf.</summary>
    public const int PortraitBox = 60;

    /// <summary>Mit diesem Palettenindex füllt <c>0x456A50</c> das Innere der
    /// Mulde, damit das Bild randlos darin sitzt (in DATA/01.PAL 19,19,15).
    /// </summary>
    public const byte PortraitFillIndex = 0x2F;

    /// <summary>
    /// Die Bank der Bauteil- und Einheitenbilder: <b>ANIM.CWA Folgen 400…403 =
    /// Rahmen 1176…1261 = 86 Bilder</b>, jedes auf eine 60×60-Leinwand mit
    /// seinem <c>yoff</c> an der Blit-Stelle.
    ///
    /// <para><b>Was das für Bilder sind.</b> Pro Fahrwerk, pro Aufbauteil, pro
    /// Verbesserung, pro Schiffsrumpf, pro Flugzeugtyp und pro Infanterist genau
    /// eines, 3D-gerendert, von schräg oben. Folge 400 hat 57 (1…18 Fahrwerke,
    /// 21…39 Aufbauteile, 40…54 Verbesserungen, 56 die dunkle »?«-Tafel), 401
    /// zehn Schiffsrümpfe, 402 sieben Flugzeuge, 403 drei Fußsoldaten und neun
    /// Gesichter.</para>
    ///
    /// <para><b>Gewählt wird ein Bild über <c>PARTS.CWD +0x0D</c></b> — dasselbe
    /// 58-Byte-Feld, das bei uns als <c>Maps/component_stats.json</c> liegt.
    /// Zeichner ist <c>0x4508A0</c>; sein Fall 5 rechnet
    /// <c>Bild = word[0x7A468A] + icon</c> (0x7A468A ist Folge 400, Feld
    /// <c>start_frame</c>) und holt den Rahmenzeiger aus
    /// <c>dword[0x815580 + 4·Bild]</c>.</para>
    ///
    /// <para>⚠ <b>Das <c>yoff</c>-Byte ist der ganze Punkt dieses Exports.</b>
    /// Ein älterer Kratzexport (Temp/opencode/…/ANIM/frames/) hat dieselben
    /// Rahmen, wirft aber <c>yoff</c> weg: Bild 1177 kommt dort als 51×38
    /// heraus, richtig ist 51×60 auf der Blit-Leinwand. Und genau dieses Byte
    /// setzt Turm und Fahrwerk zueinander — ohne es lassen sich die beiden nicht
    /// stapeln, und man sieht es erst am fertigen Bild. Der gemeinsame Dekoder
    /// <see cref="CwpFile.DecodeFrameAt"/> trägt es als
    /// <c>Frame.YOffset</c> mit, <see cref="Canvas"/> setzt es ein.</para>
    ///
    /// <para><b>Deckkraft:</b> der Blitter <c>0x4AC1B0</c> prüft <c>0xFF</c> nur
    /// im Zweig <c>mode == 1</c> (0x4AC281 <c>cmp al,0xFF; je</c>), sonst kopiert
    /// er blank (0x4AC275) — <b>Index 0 ist NICHT durchsichtig</b>. Genau das
    /// tut der Dekoder über <c>Frame.Opaque</c>; was von keiner Zeile bedeckt
    /// ist, bleibt auf der Leinwand durchsichtig, damit die Muldenfüllung
    /// (Index 0x2F) durchscheint.</para>
    /// </summary>
    public void WritePortraits(AnimFile anim)
    {
        var (c0, start) = anim.Sequence(PortraitSeq0);
        if (c0 == 0) return;

        // die Folgen der Reihe nach; lückenlos ist eine BEHAUPTUNG, also wird
        // sie geprüft und im Index festgehalten
        var seqs = new List<(int Seq, int Count, int Start)>();
        int total = 0, next = start;
        bool contiguous = true;
        for (int s = PortraitSeq0; s < PortraitSeq0 + PortraitSeqN; s++)
        {
            var (c, st) = anim.Sequence(s);
            if (c == 0) continue;
            if (st != next) contiguous = false;
            seqs.Add((s, c, st));
            total += c;
            next = st + c;
        }

        string dir = _ui + "/portraits";
        Directory.CreateDirectory(dir);

        var meta = new StringBuilder();
        int written = 0, empty = 0, maxW = 0, maxH = 0;
        for (int k = 0; k < total; k++)
        {
            var f = anim.Frame(start + k);
            if (f == null) { empty++; continue; }
            maxW = Math.Max(maxW, f.Width);
            maxH = Math.Max(maxH, f.YOffset + f.Height);
            var img = Canvas(f, PortraitBox, PortraitBox);
            img.SavePng($"{dir}/p{k:00}.png");
            written++;
            Portraits++;
            if (written > 1) meta.Append(',');
            meta.Append($"{{\"picture\":{k},\"frame\":{start + k},\"rows\":{f.Height},");
            meta.Append($"\"yoff\":{f.YOffset},\"w\":{f.Width}}}");
        }
        PortraitsEmpty = empty;

        var sb = new StringBuilder();
        sb.Append("{\"source\":\"ANIM.CWA\",\"palette\":\"DATA/01.PAL\",");
        // ⚠ zwei ZAHLEN, keine »400..403«-Schreibweise: das war einen Lauf lang
        // kein gültiges JSON, und der Lader meldete »kein JSON-Objekt« —
        // die ganze Bank blieb damit unsichtbar, obwohl alle 86 PNG dalagen.
        sb.Append($"\"sequences\":[{PortraitSeq0},{PortraitSeq0 + PortraitSeqN - 1}],");
        sb.Append($"\"first_frame\":{start},\"pictures\":{total},");
        sb.Append($"\"contiguous\":{(contiguous ? "true" : "false")},");
        sb.Append($"\"box\":[{PortraitBox},{PortraitBox}],");
        sb.Append($"\"measured_max\":[{maxW},{maxH}],");
        sb.Append($"\"fill_index\":{PortraitFillIndex},");
        sb.Append($"\"fill_rgb\":[{_pal.R[PortraitFillIndex]},{_pal.G[PortraitFillIndex]},");
        sb.Append($"{_pal.B[PortraitFillIndex]}],");
        sb.Append("\"ranges\":{");
        for (int i = 0; i < seqs.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"\"{seqs[i].Seq}\":{{\"first_picture\":{seqs[i].Start - start},");
            sb.Append($"\"count\":{seqs[i].Count}}}");
        }
        sb.Append("},");
        sb.Append("\"selector\":\"component_stats row +0x0D (PARTS.CWD +0x0D, Satz 58 B); ");
        sb.Append("Zeichner 0x4508A0 Fall 5: Bild = word[0x7A468A] + icon\",");
        sb.Append("\"compose\":\"kind 0 (Entwurf): erst sec47 +0x18 = FAHRWERK, ");
        sb.Append("darueber sec47 +0x17 = WAFFE, gleicher Ursprung. Die ");
        sb.Append("Verbesserung (+0x19) hat ein Bild, wird aber NICHT gezeichnet.\",");
        sb.Append($"\"list\":[{meta}]}}");
        File.WriteAllText($"{_ui}/portraits_index.json", sb.ToString(), new UTF8Encoding(false));

        // ⚠ Der Aufrufer (ContentBuilder) meldet nur Schriften, Panel und
        // Effekte — diese Zeile MUSS also hier stehen, sonst steht im Protokoll
        // eines Imports nichts über die Bank, und dann weiss niemand, ob sie
        // geschrieben wurde. Sie nennt die Zahlen, an denen man es sieht.
        GD.Print($"Bauteilbilder: {written} von {total} aus ANIM.CWA {PortraitSeq0}.." +
                 $"{PortraitSeq0 + PortraitSeqN - 1} (Rahmen {start}..{start + total - 1}), " +
                 $"{empty} leer, groesstes {maxW}x{maxH} auf {PortraitBox}x{PortraitBox}, " +
                 $"lueckenlos={(contiguous ? "ja" : "NEIN")} -> {dir}");
    }

    private static List<CwpFile.Frame> Decode(AnimFile a, int seq, int limit)
    {
        var (count, start) = a.Sequence(seq);
        var list = new List<CwpFile.Frame>();
        for (int i = 0; i < count && i < limit; i++)
        {
            var f = a.Frame(start + i);
            if (f != null) list.Add(f);
        }
        return list;
    }

    /// <summary>One canvas for the whole sequence, so every frame shares an
    /// origin and the renderer can draw them all at the same anchor.</summary>
    private static (int W, int H) Bounds(List<CwpFile.Frame> frames)
    {
        int w = 1, h = 1;
        foreach (var f in frames)
        {
            w = Math.Max(w, f.Width);
            h = Math.Max(h, f.YOffset + f.Height);
        }
        return (w, h);
    }

    private Image Canvas(CwpFile.Frame f, int w, int h)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        for (int y = 0; y < f.Height; y++)
            for (int x = 0; x < f.Width; x++)
            {
                int o = y * f.Width + x;
                if (!f.Opaque[o]) continue;
                int cy = f.YOffset + y;
                if (x >= w || cy >= h) continue;
                byte i = f.Pixels[o];
                img.SetPixel(x, cy, Color.Color8(_pal.R[i], _pal.G[i], _pal.B[i], 255));
            }
        return img;
    }

    private void Save(string path, Image img)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        img.SavePng(path);
        EffectFrames++;
    }
}
