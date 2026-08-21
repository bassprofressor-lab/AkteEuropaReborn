namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Rendering;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// Das laufende Spiel hinter dem Startmenü — der »Attract-Modus« von 1997.
///
/// <para><b>Was dort läuft, ist gelesen und keine Vermutung.</b> Das Original
/// spielt im Menü keine aufgezeichneten Eingaben ab und auch keinen Film: es
/// lädt einen <b>fertigen Spielstand</b> und lässt ihn weiterlaufen. Die Kette
/// steht vollständig in GAME.EXE (Dateiversätze über den PE-Kopf umgerechnet,
/// .text: VA = Datei + 0x400c00; .data: VA = Datei + 0x402200 — geprüft am
/// Anker »Nächstes Demo zeigen«, der genau auf 0x4f0280 + 118·75 = 0x4F2512
/// liegt, also auf dem Hilfsindex, den <see cref="StartMenuPanel"/> für diese
/// Zeile gelesen hat):</para>
///
/// <list type="number">
/// <item><b>Beim Start</b> @0x4150e9: <c>push 0xff; push 0x4f7468; call
/// 0x402527</c> — 0x4f7468 ist die Zeichenkette <c>"1.dm"</c>. Gleich daneben
/// @0x4150fc wird die Demo-Nummer <c>byte[0x540734] = 1</c> gesetzt und
/// @0x415103 der Ablaufzähler <c>dword[0x540740]</c> aus
/// <c>word[0x4f70b2]</c> gefüllt.</item>
/// <item><b>Der Weiterschalter</b> @0x415d8b: <c>dword[0x540740]</c> wird
/// heruntergezählt; bei 0 wird die Demo-Nummer erhöht und
/// @0x415db9 <c>cmp dl, 0xd / jbe / mov dl, 1</c> — also <b>Demos 1 bis 13,
/// dann von vorn</b>. Der Dateiname wird @0x415e37 aus der Nummer und der
/// Endung <c>".dm"</c> (0x4f7d7c) zusammengesetzt und mit derselben Routine
/// 0x402527 geladen. Danach setzt @0x415ea9 den Zähler neu aus
/// <c>word[0x4f70b0 + 2·nr]</c>.</item>
/// <item><b>Der Menüknopf</b> setzt <c>dword[0x540740] = 2</c> (@0x44cfe3 und
/// @0x44dc4d) — der Zähler läuft im nächsten Takt ab und der Weiterschalter
/// von oben holt das nächste Demo. Deshalb heißt die Zeile
/// »Nächstes Demo«.</item>
/// </list>
///
/// <para>Und die dreizehn Dateien liegen da: <c>LEVELS\1.DM … 13.DM</c>. Der
/// Import liest sie schon lange als vollständige Spielstände (CwmFile: »a .DM
/// carries all 131 sections«), er hat sie bloß als Spielstände von
/// Kampagnenkarten geführt und nicht als das, wofür das Programm sie hält.
/// Beide Deutungen stimmen: es SIND Spielstände — und das Menü spielt sie ab.
/// </para>
///
/// <para><b>Auf beiden Programmständen geprüft.</b> Das Bytemuster des Umlaufs
/// <c>80 FA 0D 76 02 B2 01</c> kommt in jeder der zwei Fassungen genau einmal
/// vor: 0x415db9 in der Fassung vom Januar 1998 (1.421.824 B) und 0x415bf9 in
/// der vom September 1997 (1.420.800 B). Dreizehn Demos in beiden.</para>
///
/// <para><b>⚠ UNSERE SETZUNGEN</b>, weil dazu nichts gelesen wurde:</para>
/// <list type="bullet">
/// <item>Die <b>Zeiteinheit</b> des Zählers 0x540740. Die Tabelle 0x4f70b0
/// enthält für Demo 1..13 die Werte 400, 350, 550, 350, 150, 350, 550, 300,
/// 300, 400, 400, 400, 220. Genommen sind sie als Simulationstakte zu 50 Hz
/// (<see cref="Campaign.MissionScript.TicksPerSecond"/>), also 3 bis 11
/// Sekunden. Wieviele Takte das Original pro Sekunde durch diese Stelle
/// schickt, steht hier nicht.</item>
/// <item>Die <b>Untergrenze</b> <see cref="MinSeconds"/>. Unsere Karten sind
/// gebackene PNG von 20 bis 33 Megapixeln, ein Wechsel kostet Ladezeit; mit
/// den 3 Sekunden von Demo 5 wäre das Menü eine Diaschau aus Rucklern.</item>
/// <item>Die <b>Kameraführung</b>. Das Original schiebt den Ausschnitt im Demo
/// selbst irgendwie — gelesen ist davon nichts. Hier steht die Kamera bei
/// einem Hauptquartier, schwenkt dort ein wenig und schneidet nach acht
/// Sekunden zum nächsten (siehe <c>Drift</c>).</item>
/// <item>Es laufen nur die Demos, die der Import gebacken hat. Der baut
/// zur Zeit 1.DM, 3.DM und 4.DM (ContentBuilder.DmStems); die übrigen zehn
/// fehlen als Karte, nicht als Erkenntnis. Was hier gefunden wird, wird
/// gespielt — kommen die zehn dazu, laufen sie ohne Änderung mit.</item>
/// </list>
/// </summary>
public partial class MenuBackdrop : CanvasLayer
{
    /// <summary>Die Reihenfolge des Originals: 1 … 13, dann wieder 1
    /// (@0x415db9). Gespielt wird davon, was importiert ist.</summary>
    public const int DemoCount = 13;

    /// <summary>Die Laufzeiten aus <c>word[0x4f70b0 + 2·nr]</c>, Index 0
    /// unbenutzt — so wie das Programm die Tabelle anspricht.</summary>
    private static readonly int[] Ticks =
    { 0, 400, 350, 550, 350, 150, 350, 550, 300, 300, 400, 400, 400, 220 };

    /// <summary>⚠ UNSERE SETZUNG: kürzer als das lassen wir kein Demo laufen,
    /// weil ein Wechsel bei uns eine große PNG kostet.</summary>
    private const float MinSeconds = 20f;

    /// <summary>⚠ UNSERE SETZUNG: wie weit die Karte vergrößert wird. 2 zeigt
    /// bei 1280x720 denselben Weltausschnitt wie die 640x360 des Originals in
    /// seiner oberen Hälfte, und ganzzahlig bleibt Pixelkunst Pixelkunst.
    /// </summary>
    private const float Zoom = 2f;

    /// <summary>Welche Demos wirklich da sind, als Kartenname und Nummer.</summary>
    private readonly List<(int No, string Map)> _demos = new();
    private int _at = -1;

    private Node2D _world = null!;
    private Sprite2D _sprite = null!;
    private MapEntityLayer? _entities;
    private ColorRect _veil = null!;

    private float _left;                 // Sekunden bis zum nächsten Demo
    private Vector2 _cam;
    private readonly List<Vector2> _anchors = new();
    private int _anchorAt;
    private float _dwell;                // Verweilzeit am Ankerpunkt

    // das Laden läuft nebenher, damit das Menü sofort dasteht
    private System.Threading.Tasks.Task<Image?>? _loading;
    private string _loadingMap = "";
    private float _fade;

    // Bildratenmessung, siehe _Process
    private float _probe = -1f, _probeWorst;
    private int _probeFrames;

    /// <summary>Die Demos, die der Import gebacken hat, in der Reihenfolge des
    /// Originals. Leer heißt: es gibt keinen Hintergrund, und dann gibt es auch
    /// keinen — erfunden wird keiner.</summary>
    public static List<(int No, string Map)> Available()
    {
        var l = new List<(int, string)>();
        for (int n = 1; n <= DemoCount; n++)
        {
            string map = $"map_DM_{n}";
            if (FileAccess.FileExists(Core.Content.Path($"Maps/{map}.entities.json"))
                && FileAccess.FileExists(Core.Content.Path($"Maps/{map}.png")))
                l.Add((n, map));
        }
        return l;
    }

    public override void _Ready()
    {
        // hinter das Menü. Das Menü selbst ist ein Control im Standardlayer 0;
        // eine eigene Ebene darunter kann es weder überdecken noch dessen
        // Mausereignisse abfangen.
        Layer = -1;

        _world = new Node2D { Scale = new Vector2(Zoom, Zoom) };
        AddChild(_world);
        _sprite = new Sprite2D { Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
        _world.AddChild(_sprite);

        // ⚠ UNSER Schleier. Im Original steht das Menüfenster ohne ihn auf dem
        // Bild; unser Fenster ist doppelt so groß und die Schrift ist dieselbe
        // Bitmapschrift, die auf hellem Schnee verschwindet. 35 % Schwarz ist
        // das wenigste, bei dem die Zeilen noch zu lesen sind.
        // ⚠⚠ 18.08.2026 — DER SCHLEIER IST WEG. Gemeldet: »in der Demo haben die
        // Gebaeude die helleren Bodenmuster, aber die Umgebung wirkt wie
        // dunkler. Das ist im Gefecht nicht so oder in der Kampagne.«
        //
        // Genau das tat er: 35 % Schwarz ueber das GANZE Bild. Der Grund war
        // gut (die Bitmapschrift verschwindet auf hellem Schnee), der Preis zu
        // hoch — abgedunkelt wurde die ganze Kulisse fuer VIER freistehende
        // Beschriftungen. Alles andere sitzt im deckenden Menuekasten.
        //
        // Die Lesbarkeit macht jetzt ein Umriss an genau diesen vier Zeilen,
        // siehe StartMenuPanel.Umriss. Die Farbe bleibt hier stehen und ist
        // durchsichtig: wer den Schleier wiederhaben will, aendert EINE Zahl,
        // und wer nach der abgedunkelten Kulisse sucht, findet die Stelle.
        _veil = new ColorRect { Color = new Color(0, 0, 0, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _veil.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_veil);

        // Der Nebel gehört ins Spiel, nicht in die Kulisse: ein Spielstand
        // bringt seinen Erkundungsstand nicht mit, also wäre der halbe
        // Bildschirm schwarz. Die EINSTELLUNG des Spielers wird dabei nicht
        // angefasst — siehe Settings.FogSuppressed.
        Settings.FogSuppressed = true;

        // ⚠ 19.08.2026 — DIESER RIEGEL WURDE NIE GESETZT, und er ist trotzdem
        // nicht die Kur gewesen. Beides gehoert hierher.
        //
        // Im ganzen Baum standen zwei Zuweisungen auf `HelpWindow.Suppressed`,
        // BEIDE auf `false` (`_ExitTree` und `Stop`). Ein Riegel, der nur
        // geoeffnet wird, ist keiner — das allein rechtfertigt diese Zeile.
        //
        // ⚠⚠ ABER: die Begruendung, mit der er am 18.08. gebaut wurde, traegt
        // NICHT. Sie lautete »die Kulisse spielt einen echten Spielstand, und
        // der bringt seine Missionsregeln mit — deren `show_text` feuert im
        // Menue genauso«. Nachgemessen mit `--kulissen-check=900`: in 900
        // Bildern hat der Riegel **null** Fenster abgefangen. Die Demos sind
        // `map_DM_*`, also GEFECHTSkarten, und die haben gar kein
        // Missionsskript.
        //
        // Der gemeldete Fehler (»Popups im Menue, die die Maus fangen«) hatte
        // eine ganz andere Ursache: eine stehengebliebene PAUSE. Siehe
        // Core/LeaveToMenu.Tidy. Diese Zeile bleibt als Sperre fuer den Fall,
        // dass eine Kulisse doch einmal eine Kampagnenkarte zeigt — sie ist ein
        // Guertel, kein Heilmittel, und steht ausdruecklich als solcher da.
        HelpWindow.Suppressed = true;

        // ⚠⚠ 21.08.2026 — HIER STAND NUR DIE HALBE SPERRE, und das war der
        // gemeldete Fehler: »im Hauptmenü in der Demo ballert manchmal massiv
        // ›Wählen Sie den Zielpunkt‹ als Sound«.
        //
        // Das Ohr unendlich weit weg zu stellen drückt jeden Klang unter die
        // Hörschwelle — aber NUR die mit Ort, also die über PlayAt. Die
        // SPRACHMELDUNGEN laufen über Play, ohne Ort, weil sie im Original ans
        // Ohr des Spielers gehen und nicht von der Karte kommen. Sie liefen
        // ungebremst weiter, während die Demo im Hintergrund Befehle gab und
        // Einheiten antworteten.
        //
        // Die Dämpfung bleibt (sie ist die des Originals, @0x40495E), aber der
        // Riegel darüber ist neu und deckt BEIDE Wege ab.
        Audio.SoundBankPlayer.Suppressed = true;
        Audio.SoundBankPlayer.ListenerCell = new Vector2(1e6f, 1e6f);

        _demos.AddRange(Available());
        if (_demos.Count == 0)
        {
            GD.Print("Menü: keine .DM-Demos importiert — der Hintergrund bleibt leer");
            return;
        }
        GD.Print("Menü: Demos " + string.Join(", ", _demos.ConvertAll(d => $"{d.No}.DM")));
        Next();
    }

    public override void _ExitTree()
    {
        Settings.FogSuppressed = false;
        HelpWindow.Suppressed = false;
        // ⚠ Die Zahl gehoert gedruckt. Ohne sie ist »im Menue war es still«
        // nicht von »es kam ohnehin nichts« zu unterscheiden — und genau das
        // waere ein Riegel, den man nie beim Versagen erwischt.
        if (Audio.SoundBankPlayer.SuppressedCount > 0)
            GD.Print($"Menue: {Audio.SoundBankPlayer.SuppressedCount} Klaenge der Kulisse "
                     + "abgefangen (Sprachmeldungen laufen ohne Ort und werden von der "
                     + "Daempfung nicht erreicht)");
        Audio.SoundBankPlayer.Suppressed = false;
        Audio.SoundBankPlayer.ListenerCell = new Vector2(float.NaN, float.NaN);
    }

    /// <summary>Sofort aufhören: keine Simulation mehr, kein Bild mehr, und vor
    /// allem die Textur los. Gerufen bevor die Kartenszene ihre eigene
    /// 30-MB-Karte lädt — QueueFree allein greift erst nach dem laufenden Bild,
    /// und solange lägen beide gleichzeitig im Speicher.</summary>
    public void Stop()
    {
        SetProcess(false);
        _demos.Clear();
        _loading = null;
        if (_entities != null && IsInstanceValid(_entities))
        { _entities.SetProcess(false); _entities.QueueFree(); _entities = null; }
        if (IsInstanceValid(_sprite)) { _sprite.Texture = null; _sprite.QueueFree(); }
        Settings.FogSuppressed = false;
        HelpWindow.Suppressed = false;
        // ⚠ Die Zahl gehoert gedruckt. Ohne sie ist »im Menue war es still«
        // nicht von »es kam ohnehin nichts« zu unterscheiden — und genau das
        // waere ein Riegel, den man nie beim Versagen erwischt.
        if (Audio.SoundBankPlayer.SuppressedCount > 0)
            GD.Print($"Menue: {Audio.SoundBankPlayer.SuppressedCount} Klaenge der Kulisse "
                     + "abgefangen (Sprachmeldungen laufen ohne Ort und werden von der "
                     + "Daempfung nicht erreicht)");
        Audio.SoundBankPlayer.Suppressed = false;
        Audio.SoundBankPlayer.ListenerCell = new Vector2(float.NaN, float.NaN);
    }

    /// <summary>Das nächste Demo, wie es die Menüzeile tut: eins weiter, hinter
    /// dem letzten wieder von vorn.</summary>
    private int _tonMerker;

    public void Next()
    {
        if (_demos.Count == 0) return;
        // ⚠ Beim Wechsel mitzaehlen, nicht erst beim Verlassen: wer das Menue
        // nie schliesst, saehe die Zahl sonst gar nicht — und genau der Fall
        // (Menue laeuft lange, Demo gibt Befehle) ist der gemeldete.
        if (Audio.SoundBankPlayer.SuppressedCount > _tonMerker)
        {
            GD.Print($"Menue: {Audio.SoundBankPlayer.SuppressedCount - _tonMerker} Klaenge "
                     + $"der Demo abgefangen (insgesamt {Audio.SoundBankPlayer.SuppressedCount})");
            _tonMerker = Audio.SoundBankPlayer.SuppressedCount;
        }
        _at = (_at + 1) % _demos.Count;
        var (no, map) = _demos[_at];
        _left = Mathf.Max(MinSeconds, Ticks[no] / (float)Campaign.MissionScript.TicksPerSecond);

        // Das Bild ist 20 bis 33 Megapixel groß; auf dem Hauptfaden geladen
        // stünde das Menü dafür eine Sekunde still. Der Faden liest nur die
        // Datei — die Textur und alles andere entsteht im Bild darauf.
        _loadingMap = map;
        string png = ProjectSettings.GlobalizePath(Core.Content.Path($"Maps/{map}.png"));
        _loading = System.Threading.Tasks.Task.Run(() => Image.LoadFromFile(png));
    }

    /// <summary>Welches Demo gerade läuft — für die Fußzeile des Menüs.</summary>
    public string Caption => _at < 0 || _demos.Count == 0
        ? "" : $"Demo {_demos[_at].No} von {DemoCount}";

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        if (_loading is { IsCompleted: true })
        {
            var img = _loading.Result;
            _loading = null;
            if (img == null) GD.PrintErr($"Menü: {_loadingMap}.png liess sich nicht laden");
            else Show(img, _loadingMap);
        }

        if (_sprite.Texture == null) return;

        // Die Bildrate der Kulisse, einmal gemessen und einmal gemeldet. Ein
        // Startmenü, das ruckelt, wäre schlechter als eins ohne Hintergrund —
        // also soll die Zahl in der Ausgabe stehen und nicht im Bauchgefühl.
        // erst wenn eingeblendet ist: sonst misst die Probe das Aufbaubild mit,
        // und das eine teure Bild beim Kartenwechsel ist ohnehin bekannt
        if (_probe >= 0f && _fade >= 1f)
        {
            _probe += dt;
            _probeFrames++;
            _probeWorst = Mathf.Max(_probeWorst, dt);
            if (_probe >= 3f)
            {
                GD.Print($"Menü: Kulisse {_probeFrames / _probe:0} Bilder/s im Mittel, " +
                         $"längstes Bild {_probeWorst * 1000f:0} ms");
                _probe = -1f;
            }
        }

        // ⚠⚠ 18.08.2026 — KEIN AUFBLENDEN MEHR. Gemeldet: »im Hauptmenu hast du
        // scheinbar einen Effekt genommen der von dunkler zu hell macht gleich
        // am Anfang, deswegen sind am Anfang kurz wieder die hellen Stellen der
        // Gebaeude zu sehen, das kannst du rausnehmen, lass es von Anfang an
        // hell ohne den Effekt.«
        //
        // Er hat recht, und der Effekt war doppelt schaedlich: waehrend er lief,
        // war die Kulisse dunkler als die Gebaeudeboeden — also genau der
        // Kontrast, den der Schleier vorher dauerhaft erzeugt hat und der heute
        // frueh weggenommen wurde. Das Aufblenden hat ihn fuer eine Sekunde
        // zurueckgeholt.
        //
        // ⚠ `_fade` bleibt als Feld stehen und wird weiter gesetzt: an ihm
        // haengt die Sonde bei `_probe` (»erst messen, wenn das Bild steht«).
        // Nur die HELLIGKEIT haengt nicht mehr daran.
        if (_fade < 1f) _fade = 1f;

        // Der Ablaufzähler des Originals, in Sekunden statt in Takten.
        _left -= dt;
        if (_left <= 0f && _loading == null) { Next(); return; }

        Drift(dt);
    }

    /// <summary>Kamera: sie STEHT bei einer Basis und schwenkt dort nur leicht,
    /// und nach <see cref="Dwell"/> Sekunden schneidet sie zur nächsten.
    ///
    /// <para>Der erste Wurf glitt zwischen den Basen hin und her. Auf 1.DM
    /// liegen die fünf Basen über 10160 Pixel Karte verteilt — die Kulisse
    /// zeigte deshalb die meiste Zeit leeren Schnee, und genau das ist auf dem
    /// Bildschirmfoto des Originals nicht zu sehen: dort steht das Menü über
    /// einer vollen Szene. Ein Schnitt zeigt in derselben Zeit dreimal etwas.
    /// ⚠ Beides ist UNSERES, gelesen ist zur Kameraführung nichts.</para>
    /// </summary>
    private void Drift(float dt)
    {
        if (_anchors.Count == 0) return;
        _dwell -= dt;
        if (_dwell <= 0f)
        {
            _anchorAt = (_anchorAt + 1) % _anchors.Count;
            _dwell = Dwell;
            _swing = 0f;
            _fade = 1f;                     // kein Aufblenden mehr, siehe oben
        }
        // ein knapper Schwenk um den Standpunkt, damit das Bild lebt
        _swing += dt;
        _cam = _anchors[_anchorAt] + new Vector2(Mathf.Sin(_swing * 0.25f) * 90f,
                                                 Mathf.Cos(_swing * 0.17f) * 45f);
        Place();
    }

    /// <summary>⚠ UNSERE SETZUNG: wie lange die Kamera bei einer Basis
    /// bleibt.</summary>
    private const float Dwell = 8f;
    private float _swing;

    private void Place()
    {
        var view = GetViewport().GetVisibleRect().Size;
        var map = _sprite.Texture!.GetSize();
        var half = view / (2f * Zoom);
        // klemmen: kleiner als der Ausschnitt ist keine unserer Karten, aber
        // Mathf.Max hält auch diesen Fall aus
        float x = Mathf.Clamp(_cam.X, Mathf.Min(half.X, map.X / 2f), Mathf.Max(half.X, map.X - half.X));
        float y = Mathf.Clamp(_cam.Y, Mathf.Min(half.Y, map.Y / 2f), Mathf.Max(half.Y, map.Y - half.Y));
        _world.Position = view / 2f - new Vector2(x, y) * Zoom;
    }

    private void Show(Image img, string map)
    {
        _sprite.Texture = ImageTexture.CreateFromImage(img);

        // Für jedes Demo eine FRISCHE Entitätenschicht.
        //
        // Der erste Wurf lud in dieselbe Schicht nach. Beim Wechsel von 1.DM
        // (Winter) auf 3.DM (Sommer) standen daraufhin Gebäude der Winterkarte
        // im Bild — irgendwo hinter Load() hängt Bildmaterial am vorigen
        // Kachelsatz. Das gehört nicht mir (Scripts/Rendering/MapEntityLayer.cs),
        // und der Fall tritt im Spiel auch nicht auf: dort lädt jede Szene
        // genau eine Karte. Eine neue Schicht je Demo umgeht ihn ganz und gibt
        // nebenbei den Speicher der vorigen frei.
        if (_entities != null && IsInstanceValid(_entities)) _entities.QueueFree();
        _entities = new MapEntityLayer();
        _world.AddChild(_entities);
        _entities.Load(map, LoadMeta(map));
        // Die Entitätenschicht bringt ihre eigene Anzeigeebene mit (CanvasLayer
        // 3, über allem) und schreibt dort Mission und Auswahl hin. Im Spiel
        // gehört das ins Bedienfeld — über dem Startmenü hat es nichts zu
        // suchen, und es lag auch prompt quer über der Kulisse.
        _entities.SetPanelTextVisible(false);

        // Alle Seiten spielen. StartCampaign mit -1 heisst: kein Platz gehört
        // einem Menschen, also bekommt jede Seite mit Hauptquartier die KI —
        // das ist genau die Regel, die SkirmishAi.StartCampaign nimmt, wenn zu
        // einer Karte keine Diplomatie vorliegt (CampaignMission ist im Menü 0).
        int seen = _entities.StartCampaign(-1, MapEntityLayer.AiLevel.Normal);

        _anchors.Clear();
        for (int p = 0; p < 8; p++)
            if (_entities.PlayerHome(p) is { } home) _anchors.Add(home);
        if (_anchors.Count == 0) _anchors.Add(_sprite.Texture.GetSize() / 2f);
        _anchorAt = 0;
        _cam = _anchors[0];
        _dwell = Dwell;
        _swing = 0f;
        // ⚠ Von Anfang an hell — siehe oben. Kein Modulate mehr.
        _fade = 1f;
        _world.Modulate = new Color(1, 1, 1, 1);
        _probe = 0f; _probeFrames = 0; _probeWorst = 0f;
        Place();
        GD.Print($"Menü: Demo {map} läuft ({_anchors.Count} Basen, Blick von Platz {seen})");
    }

    private static GDict LoadMeta(string name)
    {
        string p = Core.Content.Path($"Maps/{name}.json");
        if (!FileAccess.FileExists(p)) return new GDict();
        using var f = FileAccess.Open(p, FileAccess.ModeFlags.Read);
        if (f == null) return new GDict();
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
            return new GDict();
        return json.Data.AsGodotDictionary<string, Variant>();
    }
}
