namespace AkteEuropaReborn.UI;

using System;
using Godot;
using AkteEuropaReborn.Editor;
using AkteEuropaReborn.Import;

/// <summary>
/// Die zwei Schalter des Karteneditors:
/// <code>
///   --map-new=&lt;name&gt;,&lt;breite&gt;,&lt;hoehe&gt;,&lt;kachelsatz&gt;
///                [,flach]            alles auf Hoehe 0 — die Gegenprobe
///                [,leer]             keine Startbasen (reine Landschaft)
///                [,ohnetabelle]      Gegenprobe: ohne die gemessene Kacheltabelle
///                [,ohnenaht]         Gegenprobe: ohne die Nahtwahl (TileSeams)
///                [,naht=&lt;n&gt;]         wie viele Wuerfe je Zelle verglichen werden
///                [,spieler=&lt;n&gt;]      wie viele Startplaetze (1..5)
///                [,truppe=&lt;n&gt;]       Fahrzeuge je Spieler
///                [,wasser=&lt;prozent&gt;] Wasseranteil, sonst der Median 18,55 %
///                [,samen=&lt;n&gt;]        der Wuerfelsamen
///                [,boden=&lt;n&gt;]        welcher Bodenblock (siehe TilePalette)
///                [,from=&lt;ordner&gt;]    wo NN.CWP/NN.PAL liegen
///                [,quelle=&lt;x.CWM&gt;]   statt erzeugen: diese Karte einlesen
///   --map-check=&lt;name&gt;
/// </code>
/// <para>Beispiele:</para>
/// <code>
///   … --headless --path . -- --map-new=edit47,80,80,47,boden=1
///   … --headless --path . -- --map-check=map_edit47
///   … --headless --path . -- "--map-new=probe01,0,0,0,quelle=Assets/Legacy/LEVELS/01.CWM"
/// </code>
///
/// <para><b>Der EINSTIEG liegt nicht hier, sondern in
/// <c>MainMenu._Ready</c></b> — bei den anderen kopflosen Schaltern, wo er vor
/// dem Start der Menuekulisse aussteigt. Hier liegen nur noch die beiden
/// Laeufe. Zur Vorgeschichte siehe den Vermerk unten in dieser Datei.</para>
///
/// <para><s>Warum das hier steht und nicht in MainMenu.cs.</s> Genau aus dem
/// Grund, den <c>Core/MainMenuCommandLine.cs</c> schon beschreibt: an
/// <c>MainMenu._Ready</c> — wo die Schalterschleife von <c>--import=</c> bis
/// <c>--selftest-sounds=</c> steht — arbeitet gerade jemand anderes, und ein
/// Zusammenstoss dort waere teurer als diese Datei. <c>MainMenu</c> ist eine
/// <c>partial class</c>, also darf dieses Stueck von ihr woanders liegen.</para>
///
/// <para>⚠ <b>Warum <c>_Notification</c> und nicht <c>_EnterTree</c>.</b>
/// <c>_EnterTree</c> ist in <c>MainMenuCommandLine.cs</c> schon vergeben, und
/// eine Methode laesst sich nicht zweimal ueberschreiben. <c>_Notification</c>
/// mit <c>NotificationEnterTree</c> ist derselbe Zeitpunkt, nur der andere
/// Eingang — und vor allem VOR <c>_Ready</c>, also vor der Pruefung
/// <c>Core.Content.Ready</c>: ein Kartenlauf soll auch dann etwas tun, wenn
/// noch nichts eingespielt ist und das Menue sonst den Importbildschirm zeigen
/// wuerde. Wenn <c>MainMenu.cs</c> eines Tages selbst
/// <c>Core.CommandLine.Args</c> liest, gehoeren diese zwei Schalter in die
/// Schleife dort und diese Datei kann weg.</para>
/// </summary>
public partial class MainMenu
{
    // ⚠ 15.08.2026 — HIER STAND EINE UMLEITUNG, UND SIE IST WEG.
    //
    // Die zwei Schalter hingen an `_Notification(NotificationEnterTree)`, weil
    // an der Schalterschleife in MainMenu._Ready gerade jemand anderes
    // arbeitete. Das war der falsche Zeitpunkt: `_Ready` lief danach trotzdem
    // ganz durch und startete die Menuekulisse, die ihr 20..33 Megapixel
    // grosses Bild auf einem Nebenlaeufer laedt. Waehrend die Engine schon
    // herunterfuhr, griff der ins Leere -- Zugriffsfehler 0xC0000005 in
    // Image.LoadFromFile, Rueckgabewert 139. Der Ausweg war eine Wartezeit von
    // vier Sekunden vor dem Quit (Leave/MapEditorQuit/StopBackdrops/
    // BackdropGrace, alles hier).
    //
    // Die Schalter stehen jetzt in MainMenu._Ready selbst, wo jeder andere
    // kopflose Schalter sitzt und wo der Ausstieg VOR StartBackdrop() liegt.
    // Damit braucht es weder Wartezeit noch Kulissenbremse noch einen zweiten
    // Eingang in den Lebenslauf des Knotens. Was hier bleibt, sind die zwei
    // Laeufe selbst.

    private static void Say(string s) => GD.Print("map-editor: " + s);

    /// <summary>Die Menuezeile »Karteneditor« — der Schirm, der dieselben zwei
    /// Laeufe ohne Befehlszeile erreichbar macht. Steht hier und nicht in
    /// <c>MainMenu.cs</c>, aus demselben Grund wie der Rest dieser Datei: dort
    /// wird gerade gearbeitet, und <c>MainMenu</c> ist eine
    /// <c>partial class</c>.</summary>
    private void ShowMapEditor() => AddChild(new MapEditorScreen { OnView = ViewMade });

    /// <summary>Eine eben erzeugte Karte ANSEHEN — kein Gefecht. Warum nicht,
    /// steht bei <see cref="SkirmishSetup.ViewMap"/>: eine erzeugte Karte hat 0
    /// Einheiten und 0 Gebaeude, und ein Gefecht darauf gilt im ersten Takt als
    /// verloren.
    ///
    /// <para>Die Kulisse geht VOR dem Szenenwechsel weg — derselbe Grund wie bei
    /// <c>StartMission</c>: sonst laegen die 20..33 Megapixel des laufenden
    /// Demos und das Bild der neuen Karte gleichzeitig im Speicher.</para></summary>
    private void ViewMade(string mapName)
    {
        SkirmishSetup.Active = false;
        SkirmishSetup.CampaignMission = 0;
        SkirmishSetup.ViewMap = mapName;
        Say($"ansehen: {mapName} im Kartenbetrachter (kein Gefecht)");
        StopBackdrop();
        GetTree().ChangeSceneToFile(SkirmishSetup.GameScene);
    }

    /// <summary>
    /// <c>--map-new=&lt;name&gt;,&lt;breite&gt;,&lt;hoehe&gt;,&lt;kachelsatz&gt;</c> —
    /// erzeugt und schreibt. Rueckgabe ist der Rueckgabewert des Laufs.
    /// </summary>
    private static int MapNew(string arg) => MapNew(arg, Say);

    /// <summary>
    /// Derselbe Lauf, aber mit einem eigenen Berichterstatter — so bekommt der
    /// Schirm (<see cref="MapEditorScreen"/>) jede Zeile in sein Fenster, und
    /// zwar aus <b>demselben Weg</b>, den die geprueften Schalter nehmen.
    ///
    /// <para>⚠ Das ist der ganze Sinn dieser Aufteilung: die Oberflaeche baut
    /// die Zeichenkette <c>name,breite,hoehe,kachelsatz[,flach][,boden=n]</c>
    /// zusammen und uebergibt sie hier. Sie hat keinen zweiten, eigenen
    /// Schreibweg — was am 12.08.2026 mit <c>--map-new=pruef01,64,64,47</c>
    /// gemessen wurde (Rueckgabe 0, danach <c>--map-check</c> ohne
    /// Beanstandung), gilt damit auch fuer den Knopf.</para>
    /// </summary>
    internal static int MapNew(string arg, Action<string> say)
    {
        var p = arg.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 4)
        {
            say("--map-new=<name>,<breite>,<hoehe>,<kachelsatz>"
                + "[,flach][,leer][,ohnetabelle][,ohnenaht][,boden=<n>][,from=<ordner>]"
                + "[,quelle=<datei.CWM>][,spieler=<n>][,truppe=<n>]"
                + "[,wasser=<prozent>][,samen=<n>]");
            return 2;
        }
        string name = p[0];
        if (!int.TryParse(p[1], out int w) || !int.TryParse(p[2], out int h) ||
            !int.TryParse(p[3], out int ts))
        { say("Breite, Hoehe und Kachelsatz muessen Zahlen sein"); return 2; }

        bool flat = false;
        string? from = null;
        int pick = 0;
        string? source = null;
        int players = MapGenerator.PlayersDefault, troop = MapGenerator.TroopDefault;
        double waterShare = MapTerrain.WaterShareDefault;
        uint seed = 1;
        bool useModel = true, useSeams = true;
        int seamTries = TileSeams.TriesDefault;
        for (int i = 4; i < p.Length; i++)
        {
            if (p[i] == "flach" || p[i] == "flat") flat = true;
            else if (p[i] == "leer") players = 0;
            else if (p[i] == "ohnetabelle") useModel = false;
            else if (p[i] == "ohnenaht") useSeams = false;
            else if (p[i].StartsWith("naht="))
            {
                int.TryParse(p[i]["naht=".Length..], out seamTries);
                if (seamTries <= 1) { useSeams = false; seamTries = 1; }
            }
            else if (p[i].StartsWith("from=")) from = p[i]["from=".Length..];
            else if (p[i].StartsWith("boden=")) int.TryParse(p[i]["boden=".Length..], out pick);
            else if (p[i].StartsWith("quelle=")) source = p[i]["quelle=".Length..];
            else if (p[i].StartsWith("spieler=")) int.TryParse(p[i]["spieler=".Length..], out players);
            else if (p[i].StartsWith("truppe=")) int.TryParse(p[i]["truppe=".Length..], out troop);
            else if (p[i].StartsWith("samen=")) { uint.TryParse(p[i]["samen=".Length..], out uint s); seed = s == 0 ? 1 : s; }
            else if (p[i].StartsWith("wasser="))
            {
                if (double.TryParse(p[i]["wasser=".Length..],
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out double pc))
                    waterShare = pc > 1.0 ? pc / 100.0 : pc;
            }
        }

        // Eine Quelldatei sagt selbst, welchen Kachelsatz sie braucht — der
        // vierte Wert der Befehlszeile gilt dann nur noch als Rueckfallwert.
        CwmFile? opened = null;
        if (source != null)
        {
            try { opened = CwmFile.Load(source); }
            catch (Exception e) { say($"{source}: {e.Message}"); return 2; }
            ts = opened.Tileset;
            w = opened.Width; h = opened.Height;
        }

        var files = MapGenerator.FindTileset(ts, from);
        if (files == null)
        {
            say($"Kachelsatz {ts:00} nicht gefunden — weder im Entwicklungsbaum " +
                "(Assets/Legacy/DATA) noch unter user://data/DATA noch auf einer eingelegten CD. " +
                "Mit »,from=<ordner>« laesst sich der Ordner nennen.");
            return 2;
        }

        // ⚠ Den Kachelsatz NACHZIEHEN, wenn er nur im Entwicklungsbaum liegt.
        // ContentBuilder.CopyTilesets steht am Ende eines vollen Imports; am
        // 13.08.2026 existierte user://data/DATA darum gar nicht, und in einer
        // ausgelieferten Fassung waere der Editor ohne Kachelsatz gewesen. Der
        // Editor zieht ihn jetzt selbst nach — und uebt damit denselben Weg aus,
        // den der Import nimmt, ohne dass ein voller Import laufen muss.
        {
            string root = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\');
            long n = ContentBuilder.CopyTileset(ts, files.Value.Cwp, files.Value.Pal, root,
                                                say, out bool copied);
            if (copied)
                say($"Kachelsatz {ts:00} nach {root}/DATA nachgezogen ({n / 1024} KiB) — " +
                    "ohne ihn laeuft der Editor nur im Entwicklungsbaum");
        }

        try
        {
            var cwp = CwpFile.Load(files.Value.Cwp);
            var pal = PalFile.Load(files.Value.Pal);
            say($"Kachelsatz aus {files.Value.Cwp} ({cwp.FrameCount} Bodenkacheln, " +
                $"{cwp.ObjectCount} Objekte)");

            string outName = name.StartsWith("map_") ? name : "map_" + name;
            CwmFile m;
            if (source != null)
            {
                // ⚠ Der GEGENPROBEN-Weg, und der einzige Grund, aus dem er hier
                // steht: er schickt eine GELIEFERTE Karte durch denselben
                // Schreibweg, den der Editor nimmt. Kommen dabei dieselben
                // .json und .entities.json heraus wie beim Import, dann hat das
                // Herausziehen von ContentBuilder.ExportMap nichts verbogen.
                // Spaeter ist es ausserdem der Weg, auf dem der Editor eine
                // vorhandene Karte OEFFNET.
                m = opened!;
                say($"Quelle {source}: {m.Width}x{m.Height}, Kachelsatz {m.Tileset:00}, " +
                    $"{m.Sections.Count} Abschnitte, Mission \"{m.Mission}\"");
            }
            else
            {
                var palette = new TilePalette(cwp, ts, pick);
                if (!palette.Ok)
                {
                    say(palette.Describe());
                    say("kein Bodenblock — ohne ganze Bodenkacheln haette die Karte nur Loecher");
                    return 1;
                }
                // ⚠ Die gemessene Kacheltabelle ist der Unterschied zwischen
                // »Wiese mit einem Fleck Wasser« und einer Landschaft: sie
                // liefert die Uferkacheln, die Hangposen und die Baeume, die der
                // Bodenblock nicht kennt. ,ohnetabelle ist die GEGENPROBE — sie
                // schaltet sie ab, und dann muss der Pruefstand bei »Landzellen
                // am Wasser mit Innenland-Code« von 0 auf ~100 % springen.
                var model = useModel ? TileModel.Learn(ts, say) : null;
                if (model != null) say(model.Describe());
                // ⚠ ,ohnenaht ist die GEGENPROBE zur Nahtwahl: sie schaltet sie
                // ab, und dann muss der Anteil harter Naehte von ~1 % auf ~8,6 %
                // zurueckspringen (map_seams.py). Ohne diese Gegenprobe waere
                // nicht zu sehen, ob die Nahtwahl ueberhaupt etwas tut.
                var seams = useSeams ? new TileSeams(cwp, pal) { Tries = seamTries } : null;
                say(useSeams
                    ? $"Nahtwahl EIN: bester aus {seamTries} gewichteten Wuerfen je Zelle"
                    : "Nahtwahl AUS (Gegenprobe): ein Wurf je Zelle, wie bis zum 13.08.2026");
                // ⚠ Der Kachelsatz geht MIT: MapDeposits braucht die Grundflaeche
                // der Feld-Rohstoffmine (Typ 15, 30 Zellen in 10x6), sonst legt es
                // Vorkommen, auf denen keine Mine steht — gemessen 0 Bauplaetze
                // trotz gelegter Vorkommen.
                m = MapGenerator.Build(outName, w, h, ts, palette, flat, say,
                                       players, troop, waterShare, seed, model, seams, cwp);
            }
            MapGenerator.Write(m, cwp, pal, outName, say);
            say($"fertig: jetzt --map-check={outName} und --map={outName}");
            return 0;
        }
        catch (Exception e)
        {
            say("fehlgeschlagen: " + e);
            return 1;
        }
    }
}
