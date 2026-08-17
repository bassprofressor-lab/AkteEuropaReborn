namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

/// <summary>
/// Turns a player's own Akte Europa installation into the content the remake
/// runs on, writing it into <c>user://data</c>. This is the piece that makes
/// the OpenRA model real: the build we hand out carries none of the 1997 game.
///
/// It uses the four ported readers and the baker, every one of which was
/// checked against the tooling that produced the content the game has been
/// running on: CWP and PAL, the CWM container, ROBO.CWR and the GAME.EXE
/// tables, plus <see cref="MapBaker"/>.
///
/// What it does NOT yet produce is listed by <see cref="Missing"/> and shown to
/// the player, because a half-filled content folder that pretends to be
/// complete is worse than one that says what it is.
/// </summary>
public sealed class ContentBuilder
{
    public readonly List<string> Log = new();
    public int MapsBaked, TablesWritten, EntitiesWritten, SpriteFrames;
    public int SoundsWritten, MusicWritten;

    /// <summary>The (chassis, weapon) pairs actually placed on the maps. They
    /// fall out of the baking anyway, so the sprite export composes exactly the
    /// combinations the game will ask for and nothing else.</summary>
    private readonly SortedSet<(int UnitType, int Weapon)> _combos = new();

    /// <summary>What the catalogue needs from the maps: how often each unit
    /// type occurs and how the buildings break down by type.</summary>
    private readonly CatalogueExporter.Tally _tally = new();

    /// <summary>The design list lives in sec47, which only a .DM carries — and
    /// they do not all hold the same number of designs, so all of them are kept
    /// and the fullest one wins.</summary>
    private readonly List<CwmFile> _designSources = new();

    /// <summary>The campaign missions, by their file number.</summary>
    private readonly List<(int Number, string Map, CwmFile File)> _missions = new();

    private readonly Core.ContentSources.Source _src;
    private readonly string _dst;      // user://data, as an OS path

    public ContentBuilder(Core.ContentSources.Source source)
    {
        _src = source;
        _dst = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\');
    }

    /// <summary>A single folder — the case the folder picker produces.</summary>
    public ContentBuilder(string sourceDir)
        : this(Core.ContentSources.FromFolder(sourceDir)
               ?? new Core.ContentSources.Source
               {
                   Kind = Core.ContentSources.Kind.Installation,
                   Label = sourceDir,
                   Roots = { sourceDir.TrimEnd('/', '\\') },
                   Exe = Core.ContentSources.ExeIn(sourceDir),
                   Cabinet = Core.ContentSources.CabinetIn(sourceDir),
               })
    { }

    /// <summary>The saved missions that exist only as .DM, under the names the
    /// game already uses for them — they would otherwise collide with the
    /// campaign levels 1, 3 and 4.</summary>
    private static readonly (string Stem, string Name)[] DmStems =
    {
        ("1", "DM_1"), ("2", "DM_2"), ("3", "DM_3"), ("4", "DM_4"),
        ("5", "DM_5"), ("6", "DM_6"), ("7", "DM_7"), ("8", "DM_8"),
        ("9", "DM_9"), ("10", "DM_10"), ("11", "DM_11"), ("12", "DM_12"),
        ("13", "DM_13"),
    };

    // ⚠ 11.08.2026 — hier standen nur 1, 3 und 4. Das war zu wenig, seit
    // gelesen ist, WAS diese Dateien sind: die DREIZEHN DEMOS des Startmenues.
    // Das Original laedt sie mit derselben Routine wie einen Spielstand
    // (@0x4150e9 mit "1.dm"), zaehlt hoch und faengt nach der dreizehnten
    // wieder von vorn an (@0x415db9: cmp dl,0xd / jbe / mov dl,1). Wer nur drei
    // baeckt, bekommt im Menue auch nur drei zu sehen.

    /// <summary>A program file: lying loose in an installation, inside the
    /// cabinet on a disc. The cabinet is opened once and kept, because it is
    /// 47 MB and five of these come out of it.</summary>
    private IscFile? _cab;
    private bool _cabTried;

    private byte[]? Asset(string name)
    {
        string? loose = Find(name) ?? Find("DATA/" + name);
        if (loose != null) return File.ReadAllBytes(loose);
        return Cabinet()?.Extract(name);
    }

    /// <summary>The cabinet, opened once and kept. Separate from
    /// <see cref="Asset"/> because SOUNDS.CWN wants unpacking to a file rather
    /// than into an array.</summary>
    private IscFile? Cabinet()
    {
        if (!_cabTried)
        {
            _cabTried = true;
            try { if (_src.Cabinet != null) _cab = IscFile.Load(_src.Cabinet); }
            catch (Exception) { _cab = null; }
        }
        return _cab;
    }

    /// <summary>The first root that holds this relative path, or null. The
    /// discs are searched in the order they were found, so CD1 answers for
    /// everything it has and CD2 fills in the rest.</summary>
    private string? Find(string rel)
    {
        foreach (string r in _src.Roots)
            if (File.Exists(r + "/" + rel)) return r + "/" + rel;
        return null;
    }

    public bool Run(Action<string>? progress = null)
    {
        void Say(string s) { Log.Add(s); progress?.Invoke(s); GD.Print("import: " + s); }

        Say(_src.Describe());
        Directory.CreateDirectory(_dst + "/Maps");

        // ---- the tables that live in the exe --------------------------------
        // On a CD the executable is not lying about: it sits in DATA1.CAB, and
        // the disc build is not the one the tables were read out of, so the
        // reader looks for them by content. Both was measured, see RunIsc.
        try
        {
            byte[]? exe;
            string where;
            if (_src.Exe != null) { exe = File.ReadAllBytes(_src.Exe); where = _src.Exe; }
            else { exe = Asset("GAME.EXE"); where = (_src.Cabinet ?? "?") + " → GAME.EXE"; }
            if (exe == null)
            {
                Say("GAME.EXE weder als Datei noch im Kabinett gefunden.");
            }
            else
            {
                var t = ExeTables.FromBytes(exe);
                _exe = t;
                WriteShips(t);
                WriteAircraft(t);
                WriteSightCircle(t);
                TablesWritten += 3;
                Say($"Tabellen aus {where} geschrieben (Schiffe, Flugzeuge)" +
                    (t.Relocated ? " — anderer Programmstand, Tabellen inhaltlich gefunden" : ""));
                // a Werft can build designs that stand on no map, so their
                // chassis and weapon belong in the sprite set as well
                // (@0x4b2b20 writes exactly these two components)
                foreach (var d in t.Ships())
                    _combos.Add((d.Chassis, d.Weapon != 0
                        ? t.StatsFor(d.Weapon)?.ComponentId ?? 0 : 0));
            }
        }
        catch (Exception e) { Say("GAME.EXE: " + e.Message); }

        // ---- the maps -------------------------------------------------------
        // Whatever the source actually carries, not a fixed list: CD 2 brings
        // the campaign levels 16 to 33, which no single installation had.
        foreach (var (stem, path) in Levels("*.CWM"))
            BakeOne(path, "map_" + stem, Say);
        foreach (var (stem, name) in DmStems)
        {
            string? p = Find($"LEVELS/{stem}.DM");
            if (p != null) BakeOne(p, "map_" + name, Say);
        }

        // ---- the unit sprites ------------------------------------------------
        // ROBO.CWR is in the cabinet on a disc and lies loose in an
        // installation; the palette is the terrain one, as in the game.
        try
        {
            byte[]? robo = Asset("ROBO.CWR");
            string? pal = Find("DATA/01.PAL");
            if (robo == null || pal == null)
            {
                Say("ROBO.CWR oder 01.PAL nicht gefunden — keine Einheitengrafiken");
            }
            else
            {
                var ex = new UnitsExporter(CwrFile.FromBytes(robo), PalFile.Load(pal), _exe,
                                           _dst + "/Units");
                ex.Run(_combos, Say);
                SpriteFrames = ex.Frames;
                Say($"Einheitengrafiken: {ex.Frames} Bilder geschrieben");
            }
        }
        catch (Exception e) { Say("ROBO.CWR: " + e.Message); }

        // ---- the interface and the effects -----------------------------------
        // FONT.CWD, PANEL.DTA and ANIM.CWA all come out of the same cabinet as
        // ROBO.CWR, and all three are drawn in the terrain palette.
        try
        {
            string? palPath = Find("DATA/01.PAL");
            if (palPath == null) { Say("01.PAL fehlt — keine Oberflaeche"); }
            else
            {
                var pal = PalFile.Load(palPath);
                var ui = new InterfaceExporter(pal, _dst + "/UI", _dst + "/Effects");
                var font = Asset("FONT.CWD");
                var font2 = Asset("FONT2.CWD");
                var panel = Asset("PANEL.DTA");
                var anim = Asset("ANIM.CWA");
                if (font != null) ui.WriteFont(font);
                // the briefing screen's own typeface — same layout, thinner
                // letters; the loader @0x45bddc reads it right before BRIEFG.DAT
                if (font2 != null)
                    ui.WriteFont(font2, InterfaceExporter.Font2Name, "FONT2.CWD");
                if (panel != null) ui.WritePanel(panel);
                if (anim != null) ui.WriteEffects(AnimFile.FromBytes(anim));
                Say($"Oberflaeche: {ui.Fonts} Schriften mit {ui.Glyphs} Glyphen, " +
                    $"Panel {(panel != null ? "ja" : "nein")}, " +
                    $"{ui.Effects} Effekte mit {ui.EffectFrames} Bildern");
            }
        }
        catch (Exception e) { Say("Oberflaeche: " + e.Message); }

        // ---- the design and catalogue tables ---------------------------------
        try
        {
            var cat = new CatalogueExporter(_exe, _dst + "/Maps");
            cat.Run(_tally, _designSources, Say);
            cat.WriteCampaign(_missions, Say);
            TablesWritten += 8;
        }
        catch (Exception e) { Say("Katalogtabellen: " + e.Message); }

        // ---- the mission briefings -------------------------------------------
        // Out of the same cabinet, and the last thing the campaign was missing:
        // until now it opened a mission without a word of what it was about.
        try
        {
            var txt = Asset("BRIEFG.TXT");
            if (txt == null) Say("BRIEFG.TXT fehlt — keine Missionstexte");
            else
            {
                var br = new BriefingExporter(_dst + "/Maps", _dst + "/UI");
                br.Write(txt, Say);
                TablesWritten++;
                var dat = Asset("BRIEFG.DAT");
                string? bgPal = Find("DATA/01.PAL");
                if (dat != null && bgPal != null) br.WriteBackdrop(dat, PalFile.Load(bgPal), Say);
                // the radar monitor on that same screen — MAP.DAT is 13 MB, so
                // it goes through the path rather than Asset(), like SOUNDS.CWN
                // loose in an installation, inside DATA1.CAB on a disc — 13 MB,
                // so Asset() may hold it whole where SOUNDS.CWN may not
                string? mapDat = Find("MAP.DAT") ?? Find("DATA/MAP.DAT");
                byte[]? radar = mapDat != null ? File.ReadAllBytes(mapDat) : Asset("MAP.DAT");
                if (radar != null && bgPal != null)
                    br.WriteRadar(radar, PalFile.Load(bgPal), Say);

                // ⚠ 18.08.2026 — DAS EMBLEM VON AKTE EUROPA, beide Darstellungen.
                // Die neun Nischenbilder stecken im Schwanz von BRIEFG.DAT (die
                // Bank ohne Kopfdaten, siehe BriefingExporter.Bank), das grosse
                // Wasserzeichen ist SYMBOL.DAT — 748.800 Byte = 9 x 320 x 260,
                // und der Lader @0x45C110 liest genau das auf die Textplatte.
                if (dat != null && bgPal != null) br.WriteEmblem(dat, PalFile.Load(bgPal), Say);
                string? symDat = Find("SYMBOL.DAT") ?? Find("DATA/SYMBOL.DAT");
                byte[]? sym = symDat != null ? File.ReadAllBytes(symDat) : Asset("SYMBOL.DAT");
                if (sym != null && bgPal != null)
                    br.WriteWatermark(sym, PalFile.Load(bgPal), Say);
            }
        }
        catch (Exception e) { Say("Briefings: " + e.Message); }

        // ---- die Hilfe- und Untermissionstexte ---------------------------------
        // Aus derselben Quelle wie BRIEFG.TXT, und das Stück, das der Kampagne
        // ihren tutorialartigen Anfang gibt: Mission 1 ruft daraus siebzehn
        // Fenster auf. Siehe HelpExporter.
        try
        {
            var help = Asset("HELPG.TXT");
            if (help == null) Say("HELPG.TXT fehlt — keine Hilfetexte");
            else
            {
                var hx = new HelpExporter(_dst + "/UI");
                hx.Write(help, Say);
                TablesWritten++;
            }
        }
        catch (Exception e) { Say("Hilfetexte: " + e.Message); }

        // ---- die Enzyklopädie des Originals ------------------------------------
        // ⚠ 17.08.2026 — ENCYCLOG.TXT, 106 Seiten, und sie lag die ganze Zeit
        // neben GAME.EXE. Gefunden nur, weil beim Anschliessen der Menuezeile
        // »Enzyklopaedie« nachgesehen wurde, was das ORIGINAL dort hat, statt
        // gleich einen Weblink hinzuschreiben. Siehe EncyclopediaExporter —
        // dort steht auch, warum diese eine Datei Latin-1 ist und HELPG.TXT
        // daneben cp437.
        try
        {
            var enc = Asset("ENCYCLOG.TXT");
            if (enc == null) Say("ENCYCLOG.TXT fehlt — keine Enzyklopaedie");
            else
            {
                var ex = new EncyclopediaExporter(_dst + "/UI");
                ex.Write(enc, Say);
                TablesWritten++;
            }
        }
        catch (Exception e) { Say("Enzyklopaedie: " + e.Message); }

        // ---- die Missionsziele im Klartext -------------------------------------
        // OBJECTG.TXT, aus derselben Quelle wie BRIEFG.TXT und HELPG.TXT — sie
        // liegt im Namensverzeichnis von DATA1.CAB direkt zwischen HELPG.TXT und
        // OPTIONS.CFG. ⚠ Das ist der Punkt: auf dieser Maschine ist sie LOSE nur
        // in einer der beiden Installationen zu finden, weshalb sie zuerst als
        // »nur dort vorhanden« galt. Ueber Asset() steht sie jeder Installation
        // zur Verfuegung, und nur deshalb kann das Kampagnen-HUD das Hauptziel
        // im Originaltext zeigen statt in unserer Formulierung.
        try
        {
            var obj = Asset("OBJECTG.TXT");
            if (obj == null) Say("OBJECTG.TXT fehlt — keine Missionsziele im Klartext");
            else
            {
                var ox = new ObjectivesExporter(_dst + "/UI");
                ox.Write(obj, Say);
                TablesWritten++;
            }
        }
        catch (Exception e) { Say("Missionsziele: " + e.Message); }

        // ---- the sound bank ---------------------------------------------------
        // SOUNDS.CWN is 79 MB and lies loose beside the exe, so it is opened as
        // a stream and never goes through Asset(), which reads a whole file into
        // memory. The six .MID files are copied through: the original plays them
        // with MCI and so do we, which means they are wanted as they are.
        string? unpacked = null;
        try
        {
            string? snd = Find("SOUNDS.CWN") ?? Find("sounds.cwn") ?? Find("DATA/SOUNDS.CWN");
            // On the DISCS it is not lying about: SOUNDS.CWN, MAP.DAT and the
            // rest are inside DATA1.CAB. Found the hard way — a CD install would
            // have come out silent. It is unpacked to a file rather than into
            // memory, because 79 MB and because the reader wants to seek.
            if (snd == null && Cabinet() is { } cab && cab.Find("SOUNDS.CWN") != null)
            {
                unpacked = _dst + "/SOUNDS.CWN.tmp";
                Say("SOUNDS.CWN liegt im Kabinett — wird ausgepackt (79 MB)");
                long n = cab.ExtractTo("SOUNDS.CWN", unpacked);
                if (n > 0) snd = unpacked;
            }
            if (snd == null) Say("SOUNDS.CWN nicht gefunden — kein Ton");
            else
            {
                using var bank = new SoundBank(snd);
                var se = new SoundExporter(_dst + "/Sound");
                se.Write(bank, Say);
                if (_exe != null) se.WriteWeaponSounds(_exe, Say);
                se.WriteMusic(n => Find(n) ?? Find("DATA/" + n), Asset, Say);
                SoundsWritten = se.Written;
                MusicWritten = se.Music;
            }
        }
        catch (Exception e) { Say("Ton: " + e.Message); }
        finally
        {
            // the unpacked copy has done its job once the WAVs are written
            if (unpacked != null)
                try { File.Delete(unpacked); } catch (Exception) { /* leave it */ }
        }

        // ---- die Kachelsaetze selbst, fuer den Karteneditor ------------------
        CopyTilesets(Say);

        Say($"fertig: {MapsBaked} Karten, {EntitiesWritten} Spielstaende, " +
            $"{TablesWritten} Tabellen, {SpriteFrames} Einheitenbilder, " +
            $"{SoundsWritten} Klaenge, {MusicWritten} Musikstuecke, " +
            $"{TilesetsCopied} Kachelsaetze");
        foreach (string m in Missing()) Say("fehlt noch: " + m);
        return MapsBaked > 0;
    }

    private ExeTables? _exe;

    /// <summary>Wie viele <c>NN.CWP</c>/<c>NN.PAL</c>-Paare der Import
    /// mitgenommen hat.</summary>
    public int TilesetsCopied;

    /// <summary>
    /// <c>NN.CWP</c> und <c>NN.PAL</c> nach <c>user://data/DATA</c> kopieren.
    ///
    /// <para>⚠ <b>Warum das dazugehoert.</b> Im SPIEL oeffnet die Engine nie eine
    /// <c>.CWP</c> — die Kartenbilder sind vorgebacken. Der KARTENEDITOR muss
    /// aber backen, und dafuer braucht er den Kachelsatz. Bis heute kopierte der
    /// Import ihn nicht mit, also fand
    /// <c>Editor.MapGenerator.FindTileset</c> ihn nur im Entwicklungsbaum
    /// (<c>Assets/Legacy/DATA</c>) oder auf einer eingelegten CD: in der
    /// ausgelieferten Fassung lief der Editor gar nicht.</para>
    ///
    /// <para>Kopiert werden alle Kachelsaetze 00..99, die die Quellen tragen —
    /// zusammen wenige Megabyte, und der Editor soll nicht auf die 26
    /// Kachelsaetze der gelieferten Karten beschraenkt sein.</para>
    /// </summary>
    private void CopyTilesets(Action<string> say)
    {
        try
        {
            string dir = _dst + "/DATA";
            Directory.CreateDirectory(dir);
            long bytes = 0;
            for (int ts = 0; ts < 100; ts++)
            {
                string? cwp = Find($"DATA/{ts:00}.CWP"), pal = Find($"DATA/{ts:00}.PAL");
                if (cwp == null || pal == null) continue;
                long n = CopyTileset(ts, cwp, pal, _dst, say, out _);
                if (n > 0) { bytes += n; TilesetsCopied++; }
            }
            say($"Kachelsaetze fuer den Editor: {TilesetsCopied} Paare NN.CWP/NN.PAL " +
                $"nach {dir} ({bytes / 1024 / 1024} MiB)");
        }
        catch (Exception e) { say("Kachelsaetze: " + e.Message); }
    }

    /// <summary>
    /// EIN Kachelsatz nach <c>&lt;dst&gt;/DATA</c>. Herausgezogen und oeffentlich,
    /// damit der Editor ihn EINZELN nachziehen kann.
    ///
    /// <para>⚠ Der Grund ist gemessen: <see cref="CopyTilesets"/> laeuft am ENDE
    /// eines vollen Imports, hinter 79 MB ausgepacktem Ton. Am 13.08.2026 war
    /// <c>user://data/DATA</c> darum gar nicht vorhanden — der Ordner existierte
    /// nicht —, obwohl der Aufruf im Import steht: der letzte Import lief, bevor
    /// es ihn gab. Der Editor zieht den Kachelsatz jetzt selbst nach, wenn er ihn
    /// nur im Entwicklungsbaum findet, und damit ist dieser Weg auch OHNE einen
    /// vollen Import ausgeuebt. Ein Weg, den nichts ausuebt, ist kein Weg.</para>
    /// </summary>
    /// <returns>Wie viele Bytes am Ziel liegen, oder 0 bei einem Fehlschlag.
    /// <paramref name="copied"/> sagt, ob dafuer etwas geschrieben wurde — sonst
    /// lag es schon richtig da.</returns>
    public static long CopyTileset(int tileset, string cwp, string pal, string dstRoot,
                                   Action<string> say, out bool copied)
    {
        copied = false;
        try
        {
            string dir = dstRoot.TrimEnd('/', '\\') + "/DATA";
            Directory.CreateDirectory(dir);
            string dc = $"{dir}/{tileset:00}.CWP", dp = $"{dir}/{tileset:00}.PAL";
            if (Path.GetFullPath(cwp) == Path.GetFullPath(dc)) return 0;   // schon dort
            // schon einmal kopiert und gleich lang: nicht bei jedem Lauf 3,5 MB
            // schaufeln. Beim IMPORT gilt das auch — dort wird ohnehin aus
            // derselben Quelle kopiert.
            if (!(File.Exists(dc) && File.Exists(dp)
                  && new FileInfo(dc).Length == new FileInfo(cwp).Length
                  && new FileInfo(dp).Length == new FileInfo(pal).Length))
            {
                File.Copy(cwp, dc, true);
                File.Copy(pal, dp, true);
                copied = true;
            }
            return new FileInfo(dc).Length + new FileInfo(dp).Length;
        }
        catch (Exception e)
        {
            say($"Kachelsatz {tileset:00} nicht kopiert: {e.Message}");
            return 0;
        }
    }

    /// <summary>Every level the sources hold, by stem, first root winning.</summary>
    private List<(string Stem, string Path)> Levels(string pattern)
    {
        var seen = new Dictionary<string, string>();
        foreach (string r in _src.Roots)
        {
            string dir = r + "/LEVELS";
            if (!Directory.Exists(dir)) continue;
            foreach (string p in Directory.GetFiles(dir, pattern))
            {
                string stem = Path.GetFileNameWithoutExtension(p).ToUpperInvariant();
                if (!seen.ContainsKey(stem)) seen[stem] = p;
            }
        }
        var list = new List<(string, string)>();
        foreach (var kv in seen) list.Add((kv.Key, kv.Value));
        list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return list;
    }

    /// <summary>
    /// Rewrite only the `.entities.json` of every level the sources hold and
    /// leave the baked pictures where they are.
    ///
    /// The pictures are 400 MB and take minutes; the game state is small and
    /// changes whenever a section is newly understood — the passability out of
    /// the imap was the case that made this worth having. A map the sources do
    /// not carry keeps the file it has, and the runtime says so when it loads a
    /// state that predates a block it wants.
    /// </summary>
    public bool ReexportStates(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport: " + s); progress?.Invoke(s); }
        Directory.CreateDirectory($"{_dst}/Maps");

        int done = 0, failed = 0;
        void One(string path, string outName)
        {
            try
            {
                var m = CwmFile.Load(path);
                var doc = EntitiesJson.Decode(m);
                File.WriteAllText($"{_dst}/Maps/{outName}.entities.json",
                                  EntitiesJson.Write(doc, outName["map_".Length..]),
                                  new UTF8Encoding(false));
                done++;
                var t = doc.Terrain;
                Say($"{outName}: frei {t.Histogram[0]} / grob {t.Histogram[1]} / " +
                    $"wasser {t.Histogram[2]} / gesperrt {t.Histogram[3]}, " +
                    $"{t.Inferred} abgeleitet, {doc.InfantryCells.Count} Infanteriezellen");
            }
            catch (Exception e) { failed++; Say($"{outName}: {e.Message}"); }
        }

        foreach (var (stem, path) in Levels("*.CWM")) One(path, "map_" + stem);
        foreach (var (stem, name) in DmStems)
        {
            string? p = Find($"LEVELS/{stem}.DM");
            if (p != null) One(p, "map_" + name);
        }

        Say($"fertig: {done} Spielstaende neu geschrieben, {failed} fehlgeschlagen");
        return done > 0 && failed == 0;
    }

    /// <summary>
    /// Rewrite only `campaign.json` — the list of missions and the maps they run
    /// on. Nothing is baked and no level is decoded.
    ///
    /// <para>⚠ It reads the maps ALREADY IMPORTED, not the sources. That is the
    /// whole point: the list was written once, on 2026-08-07, when only disc 1's
    /// fifteen levels were in — and the second disc's import on 08-10 brought
    /// map_16..map_33 in but LEFT THE LIST ALONE, because only the full
    /// <see cref="Run"/> calls <c>WriteCampaign</c>. Eighteen missions sat on
    /// disk unreachable. Rebuilding from the imported maps also means a player
    /// needs no disc in the drive to repair it.</para>
    ///
    /// <para>Everything the list carries is in the baker's own `map_NN.json`:
    /// `mission` (the title), `tileset`, `width`, `height`. Only the slot names
    /// ("Mission 7" and the like) come out of GAME.EXE, and they are optional —
    /// without an exe the list is written without them rather than not at
    /// all.</para>
    ///
    /// <para>A numeric stem is a campaign level; `map_DM_1` and friends are
    /// saved games and stay out, exactly as in <see cref="BakeOne"/>.</para>
    /// </summary>
    public bool ReexportCampaign(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-campaign: " + s); progress?.Invoke(s); }

        // the slot names only — a missing exe costs the "slot_name" field and
        // nothing else, so it is not a reason to give up
        try
        {
            byte[]? exe = _src.Exe != null ? File.ReadAllBytes(_src.Exe) : Asset("GAME.EXE");
            if (exe != null) _exe = ExeTables.FromBytes(exe);
            else Say("GAME.EXE nicht gefunden — Liste ohne die Namen der Missionsplaetze");
        }
        catch (Exception e) { Say("GAME.EXE: " + e.Message); }

        string dir = _dst + "/Maps";
        if (!Directory.Exists(dir)) { Say($"{dir} gibt es nicht — nichts importiert"); return false; }

        var rows = new List<CatalogueExporter.Row>();
        int skipped = 0;
        foreach (string p in Directory.GetFiles(dir, "map_*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(p);
            // map_NN.entities.json also matches the pattern — its stem still
            // ends in ".entities", and it is not the meta file
            if (name.EndsWith(".entities", StringComparison.Ordinal)) continue;
            string stem = name["map_".Length..];
            if (!int.TryParse(stem, out int no) || no <= 0) { skipped++; continue; }
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p));
                var r = doc.RootElement;
                rows.Add(new CatalogueExporter.Row(
                    no, name,
                    r.TryGetProperty("mission", out var t) ? t.GetString() ?? "" : "",
                    Int(r, "width"), Int(r, "height"), Int(r, "tileset")));
            }
            catch (Exception e) { Say($"{name}: {e.Message}"); }
        }

        if (rows.Count == 0) { Say("keine eingespielte Kampagnenkarte gefunden"); return false; }

        var cat = new CatalogueExporter(_exe, dir);
        cat.WriteCampaign(rows, Say);
        rows.Sort((a, b) => a.Number.CompareTo(b.Number));
        Say($"fertig: {cat.Missions} Missionen ({rows[0].Number}..{rows[^1].Number}), "
            + $"{skipped} Spielstaende uebergangen");
        return cat.Missions > 0;

        static int Int(System.Text.Json.JsonElement e, string k)
            => e.TryGetProperty(k, out var v) && v.TryGetInt32(out int i) ? i : 0;
    }

    /// <summary>Nur die Hilfetexte neu schreiben. HELPG.TXT ist 101 KB und
    /// steht in jeder Installation und in jedem Kabinett; ein voller Import
    /// dafür wäre Minuten für eine Sekunde Arbeit.</summary>
    public bool ReexportHelp(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-help: " + s); progress?.Invoke(s); }
        try
        {
            var raw = Asset("HELPG.TXT");
            if (raw == null) { Say("HELPG.TXT nicht gefunden"); return false; }
            var hx = new HelpExporter(_dst + "/UI");
            hx.Write(raw, Say);
            // Die Missionsziele kommen gleich mit: dieselbe Quelle, dieselben
            // paar Kilobyte, und wer die Texte neu schreibt, meint sie mit.
            var obj = Asset("OBJECTG.TXT");
            if (obj == null) Say("OBJECTG.TXT nicht gefunden — Missionsziele bleiben, wie sie sind");
            else new ObjectivesExporter(_dst + "/UI").Write(obj, Say);
            // Und die Enzyklopaedie aus demselben Grund: dieselbe Quelle,
            // dieselben paar Kilobyte.
            var enc = Asset("ENCYCLOG.TXT");
            if (enc == null) Say("ENCYCLOG.TXT nicht gefunden — Enzyklopaedie bleibt, wie sie ist");
            else new EncyclopediaExporter(_dst + "/UI").Write(enc, Say);
            return hx.Texts > 0;
        }
        catch (Exception e) { Say(e.Message); return false; }
    }

    /// <summary>
    /// Rewrite only the catalogue tables out of GAME.EXE — weapons, research,
    /// designs, units, building types. Nothing is decoded from a level and no
    /// picture is touched, so it costs a second instead of minutes.
    ///
    /// Worth having for the same reason as <see cref="ReexportStates"/>: a table
    /// changes whenever a row of the stats block is newly understood, and the
    /// weapon names were read from six of fifty components until 2026-08-06.
    /// </summary>
    /// <summary>
    /// <c>--reexport-entities</c> — <b>nur die <c>*.entities.json</c> neu
    /// schreiben</b>, ohne die Karten neu zu backen.
    ///
    /// <para><b>Warum es das gibt:</b> die Spielstände tragen Felder, die der
    /// Ausleser jahrelang nicht gelesen hat — zuletzt die fünf, ohne die ein
    /// Flugzeug stillsteht (Feinlage, Richtung, Auftrag; siehe
    /// <c>CwmExtra.Special.FineX</c>, Fehler D6). Wer die nachträgt, braucht die
    /// Kartendaten neu, aber nicht die 20–33-Megapixel-Bilder: das Backen ist
    /// der teure Teil und hat mit dem Spielstand nichts zu tun.</para>
    ///
    /// <para>⚠ Der volle Weg wäre <c>--import-cd</c>, und der überschreibt den
    /// gesamten importierten Inhalt des Spielers. Für ein nachgetragenes Feld
    /// ist das zu grob — deshalb dieser Teilweg, wie es ihn für Kataloge,
    /// Gebäude, Einheiten und Effekte schon gibt.</para>
    ///
    /// <para>⚠ Er schreibt nach <c>user://data/Maps</c>. Das ist der Ort, den
    /// <c>Core.Content.Path</c> BEVORZUGT — eine Änderung nur im Projektbaum
    /// bliebe wirkungslos (Regel 13, hier schon mehrfach teuer gewesen).</para></summary>
    public bool ReexportEntities(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-entities: " + s); progress?.Invoke(s); }
        Directory.CreateDirectory($"{_dst}/Maps");

        int ok = 0, failed = 0;
        void One(string path, string outName)
        {
            try
            {
                var doc = EntitiesJson.Decode(CwmFile.Load(path));
                File.WriteAllText($"{_dst}/Maps/{outName}.entities.json",
                                  EntitiesJson.Write(doc, outName.StartsWith("map_")
                                                          ? outName["map_".Length..] : outName),
                                  new UTF8Encoding(false));
                ok++;
            }
            catch (Exception e) { failed++; Say($"{outName}: {e.Message}"); }
        }

        foreach (var (stem, path) in Levels("*.CWM")) One(path, "map_" + stem);
        foreach (var (stem, name) in DmStems)
        {
            string? p = Find($"LEVELS/{stem}.DM");
            if (p != null) One(p, "map_" + name);
        }

        // ⚠ Regel 33: eine Zahl, die belegt, dass der Lauf etwas getan hat.
        // "fertig" ohne Anzahl ist von "keine Karte gefunden" nicht zu
        // unterscheiden — und ohne Karten in Reichweite tut er genau nichts.
        Say(ok == 0
            ? "KEINE Karte gefunden — zeigt der Pfad auf die Installation oder die CDs?"
            : $"{ok} Karten neu geschrieben, {failed} Fehler");
        return ok > 0;
    }

    /// <summary>
    /// <b>WIE HOCH RAGEN DIE OBJEKTE?</b> — kein Export, sondern eine Messung.
    ///
    /// <para>Anlass: gemeldet als »im Original verdecken z. B. auch Bäume
    /// Einheiten, bei uns nicht«. Bäume werden in <see cref="MapBaker.Bake"/>
    /// Durchgang C ins Kartenbild eingebacken; nur Gebäude sind ausgenommen und
    /// werden lebend gezeichnet. Ein eingebackener Baum kann nichts
    /// verdecken.</para>
    ///
    /// <para>Die Kur ist dieselbe wie bei den Gebäudekacheln: <b>flach bleibt im
    /// Boden, Aufragendes kommt ins Zeilenfach</b>. Dafür braucht es eine
    /// Schwelle, und die Schwelle der Gebäude (<c>FlachBisPx = 25</c>) sitzt in
    /// einer <b>gemessenen Lücke</b> der Höhenverteilung. Diese Zeile zählt
    /// dieselbe Verteilung für die OBJEKTE aus, über alle Karten aller
    /// Tilesets — damit die Schwelle wieder aus den Daten kommt und nicht aus
    /// dem Gefühl.</para></summary>
    /// <summary>
    /// <b>Nur die KARTENBILDER neu backen</b> — Bild, zweite Ebene und Meta.
    ///
    /// <para>Nötig geworden am 18.08.2026: aufragende Objekte gehören seither
    /// nicht mehr ins Kartenbild, sondern in eine zweite Ebene
    /// (<c>&lt;karte&gt;.objects.png</c>), damit ein Baum eine Einheit verdecken
    /// kann. Eine Karte aus einem älteren Import hat diese Datei nicht; der
    /// Zeichner kommt damit zurecht und verdeckt dort eben nichts.</para>
    ///
    /// <para>Ein VOLLER Import wäre dafür Minuten für eine Sache, die die
    /// Spielstände, Tabellen, Klänge und Bilder gar nicht berührt — deshalb
    /// dieser eigene Weg, wie ihn <see cref="ReexportEntities"/> und die
    /// anderen auch gehen.</para></summary>
    public bool ReexportMaps(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-maps: " + s); progress?.Invoke(s); }
        Directory.CreateDirectory($"{_dst}/Maps");

        int ok = 0, failed = 0;
        long mitEbene = 0, objZellen = 0;
        void One(string path, string outName)
        {
            if (!File.Exists(path)) return;
            try
            {
                var m = CwmFile.Load(path);
                string? cwp = Find($"DATA/{m.Tileset:00}.CWP");
                string? pal = Find($"DATA/{m.Tileset:00}.PAL");
                if (cwp == null || pal == null) { failed++; Say($"{outName}: Tileset {m.Tileset:00} fehlt"); return; }
                var img = ExportMap(m, CwpFile.Load(cwp), PalFile.Load(pal), $"{_dst}/Maps",
                                    outName, out var baker, out _);
                ok++;
                if (baker.Objects.Count > 0) { mitEbene++; objZellen += baker.Objects.Count; }
                Say($"{outName}: {img.GetWidth()}x{img.GetHeight()}, " +
                    $"{baker.Objects.Count} aufragende Objekte in die zweite Ebene");
            }
            catch (Exception e) { failed++; Say($"{outName}: {e.Message}"); }
        }

        foreach (var (stem, path) in Levels("*.CWM")) One(path, "map_" + stem);
        foreach (var (stem, name) in DmStems)
        {
            string? p = Find($"LEVELS/{stem}.DM");
            if (p != null) One(p, "map_" + name);
        }

        // ⚠ Regel 33: eine Zahl, die belegt, dass der Lauf etwas getan hat.
        Say(ok == 0
            ? "KEINE Karte gefunden — zeigt der Pfad auf die Installation oder die CDs?"
            : $"{ok} Karten neu gebacken, {failed} Fehler; {mitEbene} davon mit zweiter Ebene, "
              + $"{objZellen} aufragende Objekte insgesamt");
        return ok > 0;
    }

    public bool ReportObjectHeights(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("objekt-hoehen: " + s); progress?.Invoke(s); }

        // Ueberstand -> (wie viele CODES, wie viele ZELLEN ueber alle Karten)
        var proRise = new SortedDictionary<int, (int Codes, long Cells)>();
        var gesehen = new HashSet<(int Tileset, int Code)>();
        int karten = 0, fehler = 0;

        void One(string path, string name)
        {
            try
            {
                var m = CwmFile.Load(path);
                string? cwp = Find($"DATA/{m.Tileset:00}.CWP");
                string? pal = Find($"DATA/{m.Tileset:00}.PAL");
                if (cwp == null || pal == null) return;
                var baker = new MapBaker(m, CwpFile.Load(cwp), PalFile.Load(pal));
                baker.Bake(fill: false, objects: false);      // nur die Masse setzen
                karten++;
                foreach (var o in baker.ObjectHeights())
                {
                    bool neu = gesehen.Add((m.Tileset, o.Code));
                    proRise.TryGetValue(o.Rise, out var v);
                    proRise[o.Rise] = (v.Codes + (neu ? 1 : 0), v.Cells + o.Count);
                }
            }
            catch (Exception e) { fehler++; Say($"{name}: {e.Message}"); }
        }

        foreach (var (stem, path) in Levels("*.CWM")) One(path, "map_" + stem);
        foreach (var (stem, name) in DmStems)
        {
            string? p = Find($"LEVELS/{stem}.DM");
            if (p != null) One(p, "map_" + name);
        }

        if (karten == 0)
        { Say("KEINE Karte gefunden — zeigt der Pfad auf die Installation oder die CDs?"); return false; }

        Say($"{karten} Karten, {gesehen.Count} verschiedene Objektbilder, {fehler} Fehler");
        Say("Ueberstand ueber der Zellunterkante -> Bilder / Zellen:");
        int letzte = -999;
        foreach (var kv in proRise)
        {
            // ⚠ Die LUECKEN sind der eigentliche Gegenstand: dort gehoert die
            // Schwelle hin. Sie werden darum ausdruecklich genannt und nicht
            // nur durch fehlende Zeilen angedeutet.
            if (letzte > -999 && kv.Key > letzte + 1)
                Say($"   -- LUECKE {letzte + 1}..{kv.Key - 1} px --");
            Say($"   {kv.Key,4} px : {kv.Value.Codes,4} Bilder, {kv.Value.Cells,7} Zellen");
            letzte = kv.Key;
        }
        return true;
    }

    public bool ReexportTables(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-tables: " + s); progress?.Invoke(s); }
        try
        {
            byte[]? exe = _src.Exe != null ? File.ReadAllBytes(_src.Exe) : Asset("GAME.EXE");
            if (exe == null) { Say("GAME.EXE nicht gefunden"); return false; }
            _exe = ExeTables.FromBytes(exe);
        }
        catch (Exception e) { Say("GAME.EXE: " + e.Message); return false; }

        Directory.CreateDirectory($"{_dst}/Maps");

        // The building types and the unit catalogue are counted off the LEVELS,
        // so they are only rewritten when levels are in reach — and the counter
        // below decides it, not a hope. Point this at the discs for the full
        // set; an installation carries fewer levels and says so.
        int levels = 0;
        foreach (var (stem, path) in Levels("*.CWM"))
        {
            try { _tally.Add(EntitiesJson.Decode(CwmFile.Load(path))); levels++; }
            catch (Exception e) { Say($"{stem}: {e.Message}"); }
        }
        foreach (var (stem, _name) in DmStems)
        {
            string? p = Find($"LEVELS/{stem}.DM");
            if (p == null) continue;
            try { _tally.Add(EntitiesJson.Decode(CwmFile.Load(p))); levels++; }
            catch (Exception e) { Say($"{stem}: {e.Message}"); }
        }
        Say($"{levels} Karten gezaehlt");

        try
        {
            var cat = new CatalogueExporter(_exe, _dst + "/Maps");
            if (levels > 0) cat.Run(_tally, _designSources, Say);
            else
            {
                // ⚠ RunExeOnly, not Run: without a level the tally is empty and
                // Run would replace good tables with empty ones. That happened
                // once, on 2026-08-06, the first time these were re-exported on
                // their own — building_types.json and unit_catalog.json went to
                // zero entries without a word of complaint.
                Say("keine Karten in den Quellen — nur die Tabellen aus GAME.EXE");
                cat.RunExeOnly(Say);
            }
            // Die Waffenklaenge kommen ebenfalls allein aus GAME.EXE und hingen
            // bisher am vollen Import — dabei aendert sich gerade dort etwas,
            // wenn eine Zeile des Satzes neu gelesen wird (11.08.: +0x02 ist
            // das Muendungsfeuer).
            if (_exe != null)
            {
                new SoundExporter(_dst + "/Sound").WriteWeaponSounds(_exe, Say);
                // Schiffe und Flugzeuge stehen ebenfalls nur in GAME.EXE, und
                // am 11.08.2026 kam der Flugzeugtabelle ein Feld dazu.
                WriteShips(_exe);
                WriteAircraft(_exe);
                Say("Schiffe und Flugzeuge neu geschrieben");
            }
            Say($"fertig: {cat.Weapons} Bauteile benannt, {cat.WeaponTypes} Bauarten, "
                + $"{cat.InfantryArms} Infanteriewaffen");
            return cat.Weapons > 0;
        }
        catch (Exception e) { Say("Katalogtabellen: " + e.Message); return false; }
    }

    /// <summary>
    /// Rewrite only <c>Buildings/tileset_nn.*</c> — the patterns, the cell
    /// animations and the tile atlas — straight from the .CWP of every tileset
    /// in reach.
    ///
    /// <para>Why this exists: those files are normally written as a side effect
    /// of baking a map, and baking all 26 takes minutes. Nothing here touches a
    /// baked picture, so a change that only adds tiles to the atlas (as the cell
    /// animations did on 08.08.2026) does not need a full import.</para>
    ///
    /// <para>⚠ It is NOT a substitute for one when the BAKER changes — a cell
    /// the baker decides to leave to the renderer is burnt into the map picture,
    /// and only a re-bake moves that line.</para>
    /// </summary>
    public bool ReexportBuildings(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-buildings: " + s); progress?.Invoke(s); }
        int ok = 0, missing = 0;
        for (int tileset = 0; tileset < 100; tileset++)
        {
            string? cwp = Find($"DATA/{tileset:00}.CWP");
            string? pal = Find($"DATA/{tileset:00}.PAL");
            if (cwp == null || pal == null) { if (cwp != null) missing++; continue; }
            try
            {
                WriteBuildingPatterns(CwpFile.Load(cwp), PalFile.Load(pal), tileset);
                ok++;
            }
            catch (Exception e) { Say($"{tileset:00}: {e.Message}"); missing++; }
        }
        Say($"{ok} Tilesets geschrieben, {missing} uebersprungen");
        return ok > 0 && missing == 0;
    }

    /// <summary>Nur die Effektbilder aus ANIM.CWA neu schreiben. Nötig, sobald
    /// eine Folge dazukommt — am 11.08.2026 die beiden anderen Mündungsfeuer
    /// (102 und 143), die vorher fehlten und deshalb bei jeder Waffe durch die
    /// 232 ersetzt waren.</summary>
    public bool ReexportEffects(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-effects: " + s); progress?.Invoke(s); }
        try
        {
            string? palPath = Find("DATA/01.PAL");
            byte[]? anim = Asset("ANIM.CWA");
            if (palPath == null || anim == null)
            {
                Say("ANIM.CWA oder 01.PAL nicht gefunden");
                return false;
            }
            var ui = new InterfaceExporter(PalFile.Load(palPath), _dst + "/UI", _dst + "/Effects");
            ui.WriteEffects(AnimFile.FromBytes(anim));
            Say($"{ui.Effects} Effekte mit {ui.EffectFrames} Bildern");
            return ui.Effects > 0;
        }
        catch (Exception e) { Say(e.Message); return false; }
    }

    /// <summary>
    /// Rewrite only the unit pictures. Needs the (unit_type, weapon) pairs the
    /// maps actually use, so the levels are decoded for their entities — but
    /// nothing is baked and no picture of a map is touched.
    /// </summary>
    public bool ReexportUnits(Action<string>? progress = null)
    {
        void Say(string s) { GD.Print("reexport-units: " + s); progress?.Invoke(s); }
        try
        {
            byte[]? exe = _src.Exe != null ? File.ReadAllBytes(_src.Exe) : Asset("GAME.EXE");
            if (exe != null)
            {
                var t = ExeTables.FromBytes(exe);
                _exe = t;
                // a Werft can build designs that stand on no map, so their
                // chassis and weapon belong in the sprite set too — leaving them
                // out shrank hull/154..156 back to the old canvas
                foreach (var d in t.Ships())
                    _combos.Add((d.Chassis, d.Weapon != 0
                        ? t.StatsFor(d.Weapon)?.ComponentId ?? 0 : 0));
            }
        }
        catch (Exception e) { Say("GAME.EXE: " + e.Message); }

        // ⚠ The combos come from the IMPORTED game states, not from the LEVELS
        // folder. The first version read the levels and quietly shrank the set:
        // the folder here holds 23 of the 44 maps, so the pairs that only exist
        // on discs 2's missions vanished and the index files were rewritten
        // without them. What has already been imported is the complete list, and
        // it needs no disc in the drive.
        int maps = 0;
        foreach (string p in Directory.GetFiles(_dst + "/Maps", "*.entities.json"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p));
                if (!doc.RootElement.TryGetProperty("entities", out var arr) ||
                    arr.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                foreach (var e in arr.EnumerateArray())
                {
                    if (!e.TryGetProperty("raw", out var rv)) continue;
                    string hex = rv.GetString() ?? "";
                    if (hex.Length < 0x0d * 2) continue;
                    int ut = Convert.ToInt32(hex.Substring(0x0f * 2, 2), 16);
                    int weap = Convert.ToInt32(hex.Substring(0x0c * 2, 2), 16);
                    _combos.Add((ut, weap));
                }
                maps++;
            }
            catch (Exception e) { Say($"{Path.GetFileName(p)}: {e.Message}"); }
        }
        Say($"{maps} eingespielte Karten gelesen, {_combos.Count} Kombinationen");

        byte[]? robo = Asset("ROBO.CWR");
        string? pal = Find("DATA/01.PAL");
        if (robo == null || pal == null) { Say("ROBO.CWR oder 01.PAL nicht gefunden"); return false; }

        Directory.CreateDirectory(_dst + "/Units");
        var ex = new UnitsExporter(CwrFile.FromBytes(robo), PalFile.Load(pal), _exe, _dst + "/Units");
        ex.Run(_combos, Say);
        SpriteFrames = ex.Frames;
        Say($"fertig: {ex.Frames} Bilder, {ex.Hulls} Fahrwerke, {ex.Turrets} Waffen, {ex.Combos} Kombinationen");
        return ex.Frames > 0;
    }

    /// <summary>
    /// <b>Der Schreibweg, in einem Aufruf.</b> Aus einer <see cref="CwmFile"/>
    /// werden die drei Dateien, die die Engine wirklich laedt:
    /// <c>&lt;outName&gt;.png</c>, <c>&lt;outName&gt;.json</c> und
    /// <c>&lt;outName&gt;.entities.json</c>.
    ///
    /// <para>⚠ Herausgezogen aus <see cref="BakeOne"/> am 12.08.2026, Zeile fuer
    /// Zeile unveraendert — <b>der Karteneditor braucht genau diesen Weg</b>,
    /// nur mit einer im Speicher gebauten Karte statt einer geladenen. Wer eine
    /// <c>CwmFile</c> hat, egal woher, bekommt hier den Exporteur geschenkt.
    /// <c>BakeOne</c> ruft seither diese Stelle, es gibt also keinen zweiten
    /// Schreibweg, der auseinanderlaufen koennte.</para>
    ///
    /// <para>Was hier NICHT passiert und beim Import daneben stehen bleibt: die
    /// Gebaeudemuster des Kachelsatzes (<see cref="WriteBuildingPatterns"/>),
    /// die Sammlung der Fahrwerk/Waffe-Kombinationen und die Missionsliste. Das
    /// haengt am Import, nicht an der einzelnen Karte.</para>
    /// </summary>
    public static Godot.Image ExportMap(CwmFile m, CwpFile cwp, PalFile pal,
                                        string mapsDir, string outName,
                                        out MapBaker baker, out EntitiesJson.Doc doc)
    {
        Directory.CreateDirectory(mapsDir);
        baker = new MapBaker(m, cwp, pal);
        var img = baker.Bake();
        img.SavePng($"{mapsDir}/{outName}.png");
        // ⚠ 18.08.2026 — DIE ZWEITE EBENE: nur die aufragenden Objekte, alles
        // andere durchsichtig. Ohne sie koennen Baeume keine Einheit verdecken
        // (siehe MapBaker.RagtAbPx). Fehlt die Datei, faellt der Zeichner auf
        // den alten Zustand zurueck — eine Karte aus einem aelteren Import
        // bleibt also spielbar, sie verdeckt nur nichts.
        var ol = baker.ObjectLayer();
        if (ol != null) ol.SavePng($"{mapsDir}/{outName}.objects.png");
        File.WriteAllText($"{mapsDir}/{outName}.json", MapMeta(m, baker), new UTF8Encoding(false));
        // the game state: units, buildings, markers, rails, zones. Without
        // it a map draws but nothing stands on it — and Content.Ready looks
        // for exactly this file to decide whether content exists at all.
        doc = EntitiesJson.Decode(m);
        File.WriteAllText($"{mapsDir}/{outName}.entities.json",
                          EntitiesJson.Write(doc, outName.StartsWith("map_") ? outName["map_".Length..] : outName),
                          new UTF8Encoding(false));
        return img;
    }

    private void BakeOne(string path, string outName, Action<string> say)
    {
        if (!File.Exists(path)) return;
        try
        {
            var m = CwmFile.Load(path);
            string? cwp = Find($"DATA/{m.Tileset:00}.CWP");
            string? pal = Find($"DATA/{m.Tileset:00}.PAL");
            if (cwp == null || pal == null)
            {
                say($"{outName}: Tileset {m.Tileset:00} fehlt");
                return;
            }
            var cwpFile = CwpFile.Load(cwp);
            var palFile = PalFile.Load(pal);
            // The building patterns belong to the TILESET, not the map, so they
            // are written once per tileset — several maps share one. Without
            // them nothing can be built: the engine never opens a .CWP.
            WriteBuildingPatterns(cwpFile, palFile, m.Tileset);
            var img = ExportMap(m, cwpFile, palFile, $"{_dst}/Maps", outName,
                                out var baker, out var doc);
            // the weapon is entity +0x0c; the chassis is the unit_type
            foreach (var e in doc.Entities)
                _combos.Add((e.UnitType, e.Raw.Length > 0x0c ? e.Raw[0x0c] : 0));
            _tally.Add(doc);
            if (m.Sec(47) != null) _designSources.Add(m);
            // A numeric stem on a .CWM is a campaign mission and its number is
            // the mission number (see CatalogueExporter.WriteCampaign). The
            // extension matters: `1.DM` has the stem "1" as well, and letting
            // it through puts the saved state of level 26 in as mission 1.
            if (Path.GetExtension(path).Equals(".CWM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(m.Stem, out int no) && no > 0)
                _missions.Add((no, outName, m));
            MapsBaked++;
            EntitiesWritten++;
            say($"{outName}: {img.GetWidth()}x{img.GetHeight()} gebacken, " +
                $"{doc.Entities.Count} Einheiten, {doc.Buildings.Count} Gebaeude, " +
                $"{baker.BuildingCellsSkipped} Gebaeudekacheln dem Renderer " +
                $"ueberlassen ({baker.MissedBuildingCells} nicht erkannt)");
        }
        catch (Exception e) { say($"{outName}: {e.Message}"); }
    }

    /// <summary>Tilesets already written this run — several maps share one.</summary>
    private readonly HashSet<int> _tilesetsWritten = new();

    /// <summary>The buildable types and their patterns, once per tileset. A
    /// file without the building tail is skipped in silence: every shipped
    /// .CWP has it, and a missing one simply means nothing can be built
    /// there.</summary>
    private void WriteBuildingPatterns(CwpFile cwp, PalFile pal, int tileset)
    {
        if (!cwp.HasBuildings || !_tilesetsWritten.Add(tileset)) return;
        // EIN Schreibweg, damit Import und Editor nicht auseinanderlaufen koennen
        ExportBuildingPatterns(cwp, pal, tileset, _dst);
        BuildingTilesets++;
    }

    /// <summary>How many tileset pattern files this run wrote.</summary>
    public int BuildingTilesets;

    /// <summary>
    /// Dieselben Dateien, aber OHNE einen laufenden Import — der Karteneditor
    /// braucht sie.
    ///
    /// <para>⚠ Der Anlass ist ein gemessener Ausfall: <see cref="ExportMap"/>
    /// wurde am 12.08.2026 aus <see cref="BakeOne"/> herausgezogen, damit der
    /// Editor denselben Schreibweg nimmt — aber der Aufruf von
    /// <see cref="WriteBuildingPatterns"/> blieb in <c>BakeOne</c> zurueck. Eine
    /// vom Editor erzeugte Karte auf einem Kachelsatz, den keine gelieferte
    /// Karte benutzt, hatte darum keine <c>Buildings/tileset_nn.json</c>, und
    /// ohne die kann nichts gebaut und kein Gebaeude gezeichnet werden: die
    /// Engine oeffnet im Spiel nie eine <c>.CWP</c>.</para>
    ///
    /// <para>Rueckgabe ist die Zahl der geschriebenen Musterbilder, 0 wenn die
    /// <c>.CWP</c> keinen Gebaeudeteil hat.</para>
    /// </summary>
    public static int ExportBuildingPatterns(CwpFile cwp, PalFile pal, int tileset, string dstRoot)
    {
        if (!cwp.HasBuildings) return 0;
        string dir = $"{dstRoot.TrimEnd('/', '\\')}/Buildings";
        Directory.CreateDirectory(dir);
        File.WriteAllText($"{dir}/tileset_{tileset:00}.json",
                          BuildingPatterns.Write(cwp, tileset), new UTF8Encoding(false));
        var (png, meta) = BuildingPatterns.WriteAtlas(cwp, pal, tileset);
        if (png == null || meta.Length == 0) return 0;
        png.SavePng($"{dir}/tileset_{tileset:00}_tiles.png");
        File.WriteAllText($"{dir}/tileset_{tileset:00}_tiles.json", meta, new UTF8Encoding(false));
        return png.GetWidth() * png.GetHeight();
    }

    /// <summary>The map's own description: size, tileset, and the per-cell grid
    /// the game needs for elevation and walkability.</summary>
    private static string MapMeta(CwmFile m, MapBaker b)
    {
        var sb = new StringBuilder(1 << 20);
        sb.Append('{');
        sb.Append($"\"map\":\"{Esc(m.Stem)}\",\"mission\":\"{Esc(m.Mission)}\",");
        sb.Append($"\"tileset\":{m.Tileset},\"width\":{m.Width},\"height\":{m.Height},");
        sb.Append($"\"tile_w\":{MapBaker.TileW},\"tile_h\":{MapBaker.TileH},");
        sb.Append($"\"elev_step\":{MapBaker.ElevStep},");
        sb.Append($"\"pixel_w\":{b.PixelW},\"pixel_h\":{b.PixelH},\"origin_y\":{b.OriginY},");
        // Die ROHSTOFFVORKOMMEN. ⚠ Bei einer GELIEFERTEN Karte ist die Liste
        // leer und der Block fehlt: dort legt das Missionsskript sie an
        // (add_terra_place, C: 0x4D0A10 / F: 0x4D05C0), nicht die Kartendatei —
        // siehe CwmFile.Terra. Gefuellt ist sie nur bei einer vom Editor
        // ERZEUGTEN Karte, die kein Missionsskript hat; Simulation.NavGrid liest
        // sie hier wieder heraus, damit CellOnDeposit @0x4205C0 etwas zu fragen
        // hat.
        // ⚠ Die AUFRAGENDEN OBJEKTE: je Eintrag die Zelle (fuer das Zeilenfach)
        // und das Rechteck in der zweiten Ebene (zum Ausschneiden). Der
        // Zeichner braucht beides; sortiert wird bei ihm, nicht hier.
        if (b.Objects.Count > 0)
        {
            sb.Append("\"objects_note\":\"aufragende Objekte (> MapBaker.RagtAbPx ueber der ");
            sb.Append("Zellunterkante). Sie stehen NICHT im Kartenbild, sondern in ");
            sb.Append("<karte>.objects.png, und werden im Zeilenfach zwischen die Einheiten ");
            sb.Append("gezeichnet — sonst koennte ein Baum nichts verdecken.\",");
            sb.Append("\"objects\":[");
            for (int i = 0; i < b.Objects.Count; i++)
            {
                var o = b.Objects[i];
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"col\":{o.Col},\"row\":{o.Row},\"x\":{o.X},\"y\":{o.Y},");
                sb.Append($"\"w\":{o.W},\"h\":{o.H}}}");
            }
            sb.Append("],");
        }

        if (m.Terra.Count > 0)
        {
            sb.Append("\"terra_source\":\"Editor/MapDeposits — UNSERE ZUTAT; im Original ");
            sb.Append("add_terra_place(spalte,zeile,menge) im SETUP-Block, C:0x4D0A10 F:0x4D05C0\",");
            sb.Append("\"terra\":[");
            for (int i = 0; i < m.Terra.Count; i++)
            {
                var (c, r, a) = m.Terra[i];
                if (i > 0) sb.Append(',');
                sb.Append($"[{c},{r},{a}]");
            }
            sb.Append("],");
        }
        sb.Append("\"tiles\":[");
        var rec = m.Records;
        bool first = true;
        for (int r = 0; r < m.Height; r++)
            for (int c = 0; c < m.Width; c++)
            {
                int o = (r * m.Width + c) * 4;
                int code = BitConverter.ToUInt16(rec, o);
                int elev = rec[o + 2], flag = rec[o + 3];
                bool isObj = code >= MapBaker.GroundMax;
                if (!first) sb.Append(',');
                first = false;
                sb.Append($"{{\"col\":{c},\"row\":{r},\"code\":{code},\"elev\":{elev},");
                sb.Append($"\"flag\":{flag},\"object\":{(isObj ? "true" : "false")},");
                sb.Append($"\"sx\":{c * MapBaker.TileW},");
                sb.Append($"\"sy\":{b.OriginY + r * MapBaker.TileH - elev * MapBaker.ElevStep - 50}}}");
            }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private void WriteShips(ExeTables t)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_source\":\"GAME.EXE 0x52eda0\",\"default\":[");
        var list = t.Ships();
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"slot\":{i},\"player\":0,\"index\":{d.Index},\"enable\":{d.Enable},");
            sb.Append($"\"name\":\"{Esc(d.Name)}\",\"weapon\":{d.Weapon},\"chassis\":{d.Chassis},");
            sb.Append($"\"variant\":{d.Variant},\"cost_w\":{d.CostW},\"cost_ch\":{d.CostF},");
            sb.Append($"\"cost_sp\":{d.CostS},\"speed\":{d.Speed},\"energie\":{d.Energie},");
            sb.Append($"\"attack\":{d.Attack},\"defence\":{d.Defence},\"range1\":{d.Range1},");
            sb.Append($"\"range2\":{d.Range2},\"sight\":{d.Sight},\"ammo\":{d.Ammo},");
            sb.Append($"\"fuel\":{d.Fuel},\"reload\":{d.Reload},\"tech\":{d.Tech}}}");
        }
        sb.Append("],\"missions\":{},\"docks\":{}}");
        File.WriteAllText(_dst + "/Maps/ships.json", sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The shape the game reveals around a unit — 20 x 20 spans out of
    /// the executable, so the fog of war opens exactly the cells the original
    /// opens. See <see cref="ExeTables.SightCircleTable"/> for how the main
    /// loop's own "unexplored" step led to it.</summary>
    private void WriteSightCircle(ExeTables t)
    {
        if (!t.SightCircleFound)
        {
            Log.Add("Sichtkreis: Tabelle in diesem Programmstand nicht gefunden");
            return;
        }
        int[] v = t.SightCircle();
        var sb = new StringBuilder(1 << 12);
        sb.Append("{\"_note\":\"the reveal shape, 20x20 u16 at 0x4f8a48: row = sight radius, ");
        sb.Append("column = steps in from the circle's rim, value = half-width. Read by the ");
        sb.Append("stamp @0x4200c0, which clamps the radius to 19; the step that calls it is ");
        sb.Append("the main loop's own 'unexplored' @0x4205b0, run every fifth tick\",");
        sb.Append($"\"radii\":{ExeTables.SightRadii},\"max_radius\":{ExeTables.SightMax},\"span\":[");
        for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(v[i]); }
        sb.Append("]}");
        File.WriteAllText(_dst + "/Maps/sight_circle.json", sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    /// <c>aircraft.json</c> — und die drei fehlenden Bytes.
    ///
    /// <para>⚠⚠ <b>HIER FEHLT DER PREIS, und die Gegenseite fehlt auch noch.</b>
    /// Diese Datei schreibt speed, hp, payload, airframe, attack, defence,
    /// sight, ammo und fuel — aber nicht cost_w/cost_f/cost_s. Die Engine liest
    /// ueber <c>Core.Content.Path</c> genau diese Kopie unter
    /// <c>user://data/Maps</c>, und die verdeckt die reichere Fassung im Baum
    /// (<c>Assets/Legacy/Maps/aircraft.json</c> aus <c>aircraft_export.py</c>,
    /// die die Preise traegt). Nachgesehen am 13.08.2026: die Kopie unter
    /// <c>user://data</c> hat genau 12 Felder, keines davon ein Preis. Deshalb
    /// steht in <c>Rendering.MapEntityLayer.AirPriceByPayload</c> ein Rueckfall,
    /// und ohne ihn waeren Flugzeuge umsonst (gemessen: nach 40 Kaeufen stand das
    /// Teilelager unveraendert auf 300/400/200).</para>
    ///
    /// <para>⚠ <b>Und die Adresse, die im Umlauf ist, ist um EINS verschoben.</b>
    /// Notiert war <c>+0x1F/+0x20/+0x21</c> — das sind die Abstaende in
    /// <b>sec120</b> einer Karte, wo ein fuehrendes Freigabe-Byte alles um eins
    /// schiebt (<c>CwmExtra.AirDesigns</c> liest dort CostW/CostF/CostS an
    /// 0x1f/0x20/0x21 und Speed an 0x22). In der VORLAGENTABELLE der EXE
    /// @0x51b021, aus der <c>ExeTables.Aircraft</c> liest, liegen sie deshalb an
    /// <b>0x1E / 0x1F / 0x20</b>; 0x21 ist dort die Geschwindigkeit. Wer die
    /// notierte Adresse uebernimmt, schreibt cost_w = cost_f, cost_f = cost_s und
    /// cost_s = Geschwindigkeit — und es faellt nicht auf, weil ein Vergleich
    /// gegen sec120 an denselben verschobenen Abstaenden trotzdem stimmt.</para>
    ///
    /// <para><b>Belegt an den Bytes, 8 von 8:</b> die acht Saetze der EXE geben
    /// an 0x1E/0x1F/0x20 die Werte 50/50/0 (Jagdflieger), 80/70/10 (Bomber),
    /// 0/40/30 (Spion), 0/30/50 (Transportheli), 60/40/0 (Kampfhubschrauber),
    /// 0/30/40 (Treibstoffheli), 0/30/40 (Munitionheli) und 0/30/150
    /// (Mechanikerheli) — Zeichen fuer Zeichen die Tabelle, die
    /// <c>AirPriceByPayload</c> aus 13 Karten mit sec120 zurueckgerechnet hat.
    /// </para>
    ///
    /// <para><b>Was zu tun ist, und warum es hier NICHT getan wurde:</b>
    /// <c>ExeTables.AircraftTemplate</c> braucht drei Felder und drei Zeilen
    /// (<c>CostW = r[0x1e], CostF = r[0x1f], CostS = r[0x20]</c>), und danach
    /// gehoeren sie in die Zeile unten. <c>Scripts/Import/ExeTables.cs</c> steht
    /// heute unter fremder Hand. Und die Reihenfolge ist wichtig: solange nur
    /// EINE der beiden Seiten steht, sind Flugzeuge umsonst — der Rueckfall in
    /// <c>AirPriceByPayload</c> greift naemlich nur, wenn die Summe der drei
    /// Preise 0 ist. Schreibt diese Zeile drei Nullen mit, bleibt die Summe 0 und
    /// nichts aendert sich; schreibt sie falsche Werte, greift der Rueckfall
    /// nicht mehr und es wird still falsch abgerechnet.</para>
    ///
    /// <para>⚠ Die Zeile <c>»n mit gelesenem Preis«</c> im Gefechtsprotokoll ist
    /// dabei KEIN Beleg: sie meldete »8 mit gelesenem Preis«, obwohl die Datei
    /// keinen Preis traegt — gezaehlt wird erst, nachdem der Rueckfall die Werte
    /// eingesetzt hat.</para>
    /// </summary>
    private void WriteAircraft(ExeTables t)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_source\":\"GAME.EXE 0x51b021\",\"types\":[");
        var list = t.Aircraft();
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"index\":{a.Index},\"name\":\"{Esc(a.Name)}\",\"short\":\"{Esc(a.Short)}\",");
            sb.Append($"\"speed\":{a.Speed},\"hp\":{a.Hp},\"payload\":{a.Payload},\"airframe\":{a.Airframe},");
            sb.Append($"\"attack\":{a.Attack},\"defence\":{a.Defence},\"sight\":{a.Sight},");
            // ⚠ »kind« ist das sec19-Kind (+0x2d) und die Zahl, aus der das
            // Flugzeugbild kommt. Ohne sie musste die Engine es aus der
            // Nutzlast erschliessen, und drei der acht Vorlagen fielen durch —
            // siehe ExeTables.AircraftTemplate.Kind.
            sb.Append($"\"ammo\":{a.Ammo},\"fuel\":{a.Fuel},\"kind\":{a.Kind}}}");
        }
        sb.Append("]}");
        File.WriteAllText(_dst + "/Maps/aircraft.json", sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>What the importer cannot produce yet, named so the screen can
    /// say it. Each line is a Python extractor still to be ported; the readers
    /// they need are all in place.</summary>
    /// <summary>What the importer cannot derive — now nothing. The list stays
    /// so the screen can go on asking, and so the next gap has somewhere to be
    /// written down instead of being discovered by a black screen.</summary>
    public static List<string> Missing() => new();
}
