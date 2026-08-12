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

    /// <summary>
    /// <c>--map-new=&lt;name&gt;,&lt;breite&gt;,&lt;hoehe&gt;,&lt;kachelsatz&gt;</c> —
    /// erzeugt und schreibt. Rueckgabe ist der Rueckgabewert des Laufs.
    /// </summary>
    private static int MapNew(string arg)
    {
        var p = arg.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 4)
        {
            Say("--map-new=<name>,<breite>,<hoehe>,<kachelsatz>"
                + "[,flach][,boden=<n>][,from=<ordner>][,quelle=<datei.CWM>]");
            return 2;
        }
        string name = p[0];
        if (!int.TryParse(p[1], out int w) || !int.TryParse(p[2], out int h) ||
            !int.TryParse(p[3], out int ts))
        { Say("Breite, Hoehe und Kachelsatz muessen Zahlen sein"); return 2; }

        bool flat = false;
        string? from = null;
        int pick = 0;
        string? source = null;
        for (int i = 4; i < p.Length; i++)
        {
            if (p[i] == "flach" || p[i] == "flat") flat = true;
            else if (p[i].StartsWith("from=")) from = p[i]["from=".Length..];
            else if (p[i].StartsWith("boden=")) int.TryParse(p[i]["boden=".Length..], out pick);
            else if (p[i].StartsWith("quelle=")) source = p[i]["quelle=".Length..];
        }

        // Eine Quelldatei sagt selbst, welchen Kachelsatz sie braucht — der
        // vierte Wert der Befehlszeile gilt dann nur noch als Rueckfallwert.
        CwmFile? opened = null;
        if (source != null)
        {
            try { opened = CwmFile.Load(source); }
            catch (Exception e) { Say($"{source}: {e.Message}"); return 2; }
            ts = opened.Tileset;
            w = opened.Width; h = opened.Height;
        }

        var files = MapGenerator.FindTileset(ts, from);
        if (files == null)
        {
            Say($"Kachelsatz {ts:00} nicht gefunden — weder im Entwicklungsbaum " +
                "(Assets/Legacy/DATA) noch unter user://data/DATA noch auf einer eingelegten CD. " +
                "Mit »,from=<ordner>« laesst sich der Ordner nennen.");
            return 2;
        }

        try
        {
            var cwp = CwpFile.Load(files.Value.Cwp);
            var pal = PalFile.Load(files.Value.Pal);
            Say($"Kachelsatz aus {files.Value.Cwp} ({cwp.FrameCount} Bodenkacheln, " +
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
                Say($"Quelle {source}: {m.Width}x{m.Height}, Kachelsatz {m.Tileset:00}, " +
                    $"{m.Sections.Count} Abschnitte, Mission \"{m.Mission}\"");
            }
            else
            {
                var palette = new TilePalette(cwp, ts, pick);
                if (!palette.Ok)
                {
                    Say(palette.Describe());
                    Say("kein Bodenblock — ohne ganze Bodenkacheln haette die Karte nur Loecher");
                    return 1;
                }
                m = MapGenerator.Build(outName, w, h, ts, palette, flat, Say);
            }
            MapGenerator.Write(m, cwp, pal, outName, Say);
            Say($"fertig: jetzt --map-check={outName} und --map={outName}");
            return 0;
        }
        catch (Exception e)
        {
            Say("fehlgeschlagen: " + e);
            return 1;
        }
    }
}
