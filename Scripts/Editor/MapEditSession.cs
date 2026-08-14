namespace AkteEuropaReborn.Editor;

using System;
using Godot;
using AkteEuropaReborn.Import;

/// <summary>
/// DIE KARTE, DIE GERADE BEARBEITET WIRD — und warum sie hier liegen muss.
///
/// <para>Der Karteneditor erzeugt eine <see cref="CwmFile"/> im Arbeitsspeicher,
/// schreibt sie mit <see cref="MapGenerator.Write"/> auf die Platte und laesst
/// sie danach vom Kartenbetrachter aus den geschriebenen Dateien LADEN. Zum
/// Bearbeiten braucht es beide Haelften gleichzeitig: das geladene Bild, damit
/// man sieht, wohin man klickt, und die <c>CwmFile</c>, weil nur sie sich
/// zurueckschreiben laesst. Der Szenenwechsel dazwischen raeumt aber jeden
/// Knoten weg.</para>
///
/// <para>Darum liegt sie hier, statisch, wie <see cref="UI.SkirmishSetup"/> es
/// fuer den Gefechtsaufbau tut — und aus demselben Grund.</para>
///
/// <para><s>⚠ <b>Bewusste Grenze:</b> bearbeitet wird immer die Karte, die
/// dieser Lauf eben erzeugt hat. Eine schon vorhandene Karte von der Platte
/// zurueckzulesen waere ein eigener Weg (aus <c>map_*.json</c> und
/// <c>map_*.entities.json</c> wieder eine <c>CwmFile</c> bauen), und der Spieler
/// hat ihn ausdruecklich hintangestellt.</s></para>
///
/// <para><b>15.08.2026 — DIE GRENZE IST WEG.</b> Der Rueckweg steht in
/// <see cref="MapOpen"/>: aus den zwei geschriebenen Dateien wird wieder eine
/// <c>CwmFile</c>, und die haelt dieselbe Stelle hier. Erreichbar ueber den
/// Knopf »VORHANDENE OEFFNEN« im <see cref="MapEditorScreen"/> und ueber
/// <c>--map-new=&lt;name&gt;,0,0,0,oeffne=&lt;karte&gt;</c>.</para>
///
/// <para><b>Und der Preis dafuer ist gemessen.</b> <see cref="MapRound"/> oeffnet
/// eine Karte, speichert sie ohne jede Aenderung und haelt das Ergebnis Feld
/// fuer Feld gegen das Original — <c>--map-check=&lt;name&gt;,rundlauf</c>. Ueber
/// alle <b>67</b> Karten des Ordners: <b>0</b> Abweichungen in Gelaende, Hoehen,
/// Kachelcodes, Gegenstaenden, Gebaeuden, Einheiten, Marken, Gleisen, Linien,
/// Knoten, Vorkommen, Infanteriezellen, Spielern, Konten, Zielen und Bauplaenen;
/// die <c>map_*.json</c> kommt Zeichen fuer Zeichen zurueck. Was NICHT
/// rundlaeuft, steht namentlich bei <see cref="MapRound.IsKnownGap"/> und bei
/// <see cref="MapOpen"/>.</para>
/// </summary>
public static class MapEditSession
{
    /// <summary>Die Karte im Arbeitsspeicher. Null = nichts zu bearbeiten.</summary>
    public static CwmFile? Map { get; private set; }

    /// <summary>Der Kachelsatz dazu — gebraucht fuer Grundflaechen, Muster und
    /// das Neubacken beim Speichern.</summary>
    public static CwpFile? Cwp { get; private set; }

    public static PalFile? Pal { get; private set; }

    /// <summary>Der Dateiname ohne <c>map_</c> und ohne Endung.</summary>
    public static string Name { get; private set; } = "";

    /// <summary>Der Bearbeitungsmodus ist eingeschaltet — der Kartenbetrachter
    /// haengt dann den <see cref="MapEditOverlay"/> ein.</summary>
    public static bool Active { get; set; }

    /// <summary>Seit dem letzten Speichern geaendert.</summary>
    public static bool Dirty { get; set; }

    /// <summary>
    /// DIE GEMESSENE KACHELTABELLE, dieselbe, aus der der Generator seine
    /// Kacheln zieht.
    ///
    /// <para>⚠ Sie gehoert hierher und wird nicht im Pinsel neu gelernt. Der
    /// Grund steht bei <see cref="TileModel.Learn"/>: eine SELBST ERZEUGTE
    /// Karte darf nicht in die Tabelle, und der Pinsel arbeitet auf genau so
    /// einer. Wer beim ersten Strich neu lernte, lernte aus der eben
    /// geschriebenen Ausgabe — die Bestaetigung einer Ableitung durch sich
    /// selbst, und der Fehler saehe wie ein Fortschritt aus.</para>
    /// </summary>
    public static TileModel? Tiles { get; private set; }

    /// <summary>Der Bodenblock als Rueckfall, wenn die Tabelle fuer einen
    /// Schluessel nichts hat — derselbe, den <see cref="MapGenerator"/> nimmt.</summary>
    public static TilePalette? Palette { get; private set; }

    /// <summary>
    /// DIE NAHTWAHL — und sie gehoert hierher, weil ihr Fehlen GEMESSEN ist.
    ///
    /// <para>Der erste Gelaendepinsel zog seinen Umkreis ohne sie nach. Der
    /// Pruefstand hat es sofort gesehen: harte Naehte <b>3,44 % → 6,86 %</b> auf
    /// derselben Karte, nur weil rund tausend Zellen neu gekachelt wurden. Eine
    /// Kachel, die ohne Blick auf ihren westlichen und noerdlichen Nachbarn
    /// gezogen wird, passt eben in einem von vierzehn Faellen nicht.</para>
    /// </summary>
    public static TileSeams? Seams { get; private set; }

    /// <summary>Der Wuerfelsamen der Karte. Damit malt derselbe Strich auf
    /// derselben Zelle dieselbe Kachel — ein Editor, der bei jedem Klick
    /// wuerfelt, laesst sich nicht nachvollziehen.</summary>
    public static uint Seed { get; private set; } = 1;

    public static void Hold(CwmFile m, CwpFile? cwp, PalFile? pal, string name,
                            TileModel? tiles = null, TilePalette? palette = null,
                            uint seed = 1, TileSeams? seams = null)
    {
        Map = m; Cwp = cwp; Pal = pal; Name = name; Dirty = false;
        Tiles = tiles; Palette = palette; Seed = seed; Seams = seams;
    }

    public static void Drop()
    {
        Map = null; Cwp = null; Pal = null; Name = ""; Active = false; Dirty = false;
        Tiles = null; Palette = null; Seed = 1; Seams = null;
    }

    /// <summary>
    /// DEN BEARBEITUNGSMODUS EINSCHALTEN — und der Weg dorthin ist der
    /// eigentliche Kniff dieser Datei.
    ///
    /// <para>Der <see cref="MapEditOverlay"/> muss sich an die
    /// <c>MapEntityLayer</c> der NAECHSTEN Szene haengen, und die entsteht erst
    /// nach dem Szenenwechsel. Der uebliche Weg waere eine Zeile in
    /// <c>MapViewer._Ready</c> — nur arbeitet an <c>Scripts/Rendering/</c>
    /// gerade jemand anderes (Regel 19).</para>
    ///
    /// <para>Es geht ohne: ein Knoten, der <b>direkt an der Wurzel</b> haengt
    /// und nicht in der laufenden Szene, ueberlebt <c>ChangeSceneToFile</c> und
    /// <c>ReloadCurrentScene</c> — genau so funktionieren Autoloads. Der Waechter
    /// hoert auf <c>SceneTree.NodeAdded</c> und haengt sich an die erste
    /// <c>MapEntityLayer</c>, die auftaucht. Damit braucht der Bearbeitungsmodus
    /// <b>keine einzige Zeile</b> in einer fremden Datei.</para>
    ///
    /// <para>⚠ Das ist ein Umweg um eine Zusammenarbeitsfrage, nicht die
    /// schoenere Bauform. Wenn <c>MapViewer</c> wieder frei ist, gehoert der
    /// Einhaenger dorthin, und dieser Waechter kann weg.</para>
    /// </summary>
    private static MapEditWatcher? _watcher;

    public static void Watch(SceneTree tree)
    {
        if (_watcher != null && GodotObject.IsInstanceValid(_watcher)) return;
        _watcher = new MapEditWatcher();
        tree.Root.CallDeferred(Node.MethodName.AddChild, _watcher);
    }

    /// <summary>Zurueckschreiben — derselbe Weg, den der Generator geht, samt
    /// Neubacken des Kartenbildes. Das ist der Grund, warum Gegenstaende und
    /// Gleise beim Speichern richtig im Bild landen, ohne dass der Editor eine
    /// zweite Zeichenroutine braeuchte.</summary>
    public static string? Save(Action<string> say)
    {
        if (Map == null || Cwp == null || Pal == null) { say("Editor: nichts zu speichern"); return null; }
        // ⚠ MapGenerator.Write gibt das VERZEICHNIS zurueck, nicht den
        // Kartennamen — gemerkt, weil der Prueflauf »gespeichert als
        // C:/…/data/Maps« meldete. Wer den Rueckgabewert als Namen
        // weiterreicht, schickt dem Kartenbetrachter einen Pfad, und der findet
        // ihn in seiner Kartenliste nicht: er zeigt dann stillschweigend die
        // zuletzt gewaehlte Karte. Genau die stille Kartenvertauschung, vor der
        // MapViewer an zwei Stellen warnt.
        MapGenerator.Write(Map, Cwp, Pal, Name, say);
        Dirty = false;
        return Name;
    }
}

public sealed partial class MapEditWatcher : Node
{
    public override void _Ready()
    {
        Name = "MapEditWatcher";
        GetTree().NodeAdded += OnAdded;
    }

    private void OnAdded(Node n)
    {
        if (!MapEditSession.Active || n is not Rendering.MapEntityLayer layer) return;
        // Erst wenn der Knoten steht: der Ueberzug fragt beim Aufbau die
        // Kachelmuster und die Gebaeudeliste, und die stehen in _Ready.
        layer.CallDeferred(Node.MethodName.AddChild, MakeOverlay(layer));
    }

    private static Node MakeOverlay(Rendering.MapEntityLayer layer)
        => new MapEditOverlay(layer) { ZIndex = 4000 };
}
