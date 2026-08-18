namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Das Hilfefenster der Kampagne — die kleinen Meldungen, die eine Mission
/// mitten ins Bild stellt.
///
/// <para><b>Woher es kommt</b> (11.08.2026): der Missionsblock ruft
/// <c>show_text(x, y, id, 0)</c> @0x443490 bzw. <c>show_text2</c> @0x4432E0 mit
/// einer Nummer aus HELPG.TXT und einer Stelle auf dem Bildschirm. Über alle 33
/// Blöcke sind das <b>119 Fenster</b>. Mission 1 besteht fast nur daraus: sie
/// zeigt siebzehn davon und ist damit das Tutorial des Originals.</para>
///
/// <para><b>Was das Original selbst sagt</b>, und was hier darum so gebaut ist:
/// Text #001 erklärt seine eigene Bedienung — »In der Mitte wird das
/// @Hilfefenster angezeigt. Zu Schließen sind alle diese Fenster mit der
/// @Rechten @Maus @Taste oder der @ESC Taste.« Genau das tut es hier. Und
/// <c>close_message_windows()</c> @0x447560 räumt alle Fenster der Art 30 weg —
/// das ist <see cref="CloseAll"/>, und die Blöcke rufen es vor fast jedem neuen
/// Fenster (40 Mal über alle Missionen).</para>
///
/// <para><b>⚠ 18.08.2026 — DIE SIEBZEHN SIND JETZT NACHGEZÄHLT, und in unserer
/// Datei standen elf.</b> Der Block von Mission 1 ruft
/// <c>show_text</c>/<c>show_text2</c> mit #001..#006, #008..#013, #018, #020,
/// #039, #040 und #110. Sechs davon fehlten in
/// <c>Data/mission_scripts.json</c>, weil der Regelleser ihre Bedingung nicht
/// las — darunter #001, das Willkommensfenster selbst. Was nachgetragen wurde
/// und wie es gemessen ist, steht dort unter <c>_hilfefenster</c>; der
/// Prüfstand heisst <c>--hilfe-check</c> und meldet »17 Fenster, 17 mit Text,
/// 17 können feuern, Trockenlauf 17/17«.</para>
///
/// <para><b>Die REIHENFOLGE hängt an einer eigenen Abfrage:</b>
/// <c>window_open(n)</c> @0x4D0600 → @0x4475B0, »ist Hilfefenster n noch
/// offen?«. Sie läuft dasselbe Fensterfeld ab, das <see cref="CloseAll"/>
/// räumt, und sucht ein Fenster der Art 30 mit +0xACA0 == n. Mission 1 zeigt
/// #002 erst, wenn der Spieler #001 weggeklickt hat, und #012 erst nach #011 —
/// das Tutorial wartet also wirklich auf den Spieler und nicht auf eine Uhr.
/// Hier ist das <see cref="IsOpen"/>.</para>
///
/// <para><b>Und das Original hat einen EIGENEN Einmal-Riegel je Text</b>, den
/// wir nicht nachgebaut hatten: <c>show_text2</c> prüft als erstes
/// <c>byte[0x87AE00 + id]</c> und kehrt um, wenn dort schon etwas steht und das
/// vierte Argument 0 ist (@0x4432F6 ff.); beim Anzeigen setzt es dieselbe
/// Stelle auf 1 (@0x443340). ALLE siebzehn Aufrufe von Mission 1 geben als
/// viertes Argument 0 — <b>jeder Text kommt also höchstens einmal</b>. Das
/// deckt sich mit dem, was <see cref="Dismissed"/> hier bewirkt, nur setzt das
/// Original den Riegel schon beim ERSTEN ZEIGEN und nicht erst beim
/// Wegklicken. ⚠ Der Unterschied ist nicht gemessen und darum auch nicht
/// nachgebaut; er fällt nur auf, wenn eine Regel dasselbe Fenster zweimal
/// öffnen wollte, ohne dass der Spieler es angefasst hat.</para>
///
/// <para><b>Das <c>@</c></b> vor einem Wort hebt es hervor; der Import lässt es
/// stehen (siehe <c>Import.HelpExporter</c>), gedeutet wird es hier.</para>
///
/// <para>⚠ <b>UNSERE Setzungen, und sie sind es ausdrücklich:</b> Farbe, Rahmen
/// und Breite des Fensters. Das Original zeichnet auf einem 640×480-Schirm mit
/// eigener Grafik (HELPG.PIC, 129.600 Byte = 360×360, ungelesen); hier steht ein
/// Panel im Stil der übrigen Oberfläche. Die STELLE dagegen ist die des
/// Originals: x/y kommen aus dem Block und werden vom 640×480-Raster auf das
/// Fenster umgerechnet.</para>
/// </summary>
public sealed partial class HelpWindow : PanelContainer
{
    /// <summary>Das Raster, in dem das Original seine x/y meint.</summary>
    public const int OrigW = 640, OrigH = 480;

    private static readonly List<HelpWindow> Open = new();

    /// <summary>Die Texte aus HELPG.TXT, einmal geladen.</summary>
    private static Godot.Collections.Dictionary<string, Variant>? _texts;
    private static bool _tried;

    /// <summary>Die Absätze eines Hilfetexts, oder null. Öffentlich, damit ein
    /// Prüfstand fragen kann, ohne ein Fenster zu bauen.</summary>
    public static List<string>? TextOf(int id)
    {
        if (!_tried)
        {
            _tried = true;
            string path = Core.Content.Path("UI/help.json");
            if (FileAccess.FileExists(path))
            {
                using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
                var json = new Json();
                if (f != null && json.Parse(f.GetAsText()) == Error.Ok &&
                    json.Data.VariantType == Variant.Type.Dictionary)
                {
                    var root = json.Data.AsGodotDictionary<string, Variant>();
                    if (root.TryGetValue("texts", out var tv) &&
                        tv.VariantType == Variant.Type.Dictionary)
                        _texts = tv.AsGodotDictionary<string, Variant>();
                }
            }
            GD.Print(_texts != null
                ? $"Hilfetexte: {_texts.Count} Saetze aus {path}"
                : "Hilfetexte: help.json fehlt — die Missionen zeigen keine Fenster " +
                  "(--reexport-help=<Quelle> schreibt sie)");
        }
        if (_texts == null) return null;
        if (!_texts.TryGetValue(id.ToString(), out var v) ||
            v.VariantType != Variant.Type.Array) return null;
        var arr = v.AsGodotArray();
        var list = new List<string>();
        foreach (var p in arr) list.Add(p.AsString());
        return list;
    }

    /// <summary>Was der Spieler in dieser Mission schon weggeklickt hat.
    ///
    /// <para>⚠ 11.08.2026, zweiter Anlauf. Der erste Fix verhinderte nur das
    /// Flackern: eine Regel ohne Riegel ruft jeden Takt
    /// <c>close_message_windows</c> + <c>show_text</c>, und ein WEGGEKLICKTES
    /// Fenster war aus <c>Open</c> verschwunden — also fand <c>Show</c> keines
    /// mit dieser Nummer und baute es neu. Das Popup kam sofort zurück, und für
    /// den Spieler sah es aus, als liesse es sich gar nicht schliessen.</para>
    ///
    /// <para>Wer eine Meldung weggeklickt hat, bekommt sie in dieser Mission
    /// nicht wieder. Das ist genau das, was der Riegel des Originals bewirkt
    /// (<c>v[n] == 0</c> vor der Regel) — nur an der Stelle, an der wir ihn
    /// sicher setzen können, solange der Regelleser ihn bei dieser Form noch
    /// nicht erkennt.</para></summary>
    private static readonly HashSet<int> Dismissed = new();

    /// <summary>Alles vergessen — beim Missionsstart und für einen Prüfstand,
    /// der nach einem Import neu laden will.</summary>
    public static void Forget()
    {
        _texts = null; _tried = false;
        Dismissed.Clear();
        foreach (var w in Open.ToArray()) w.QueueFree();
        Open.Clear();
    }

    /// <summary>Wieviele Fenster gerade offen sind. Der Block fragt das selbst
    /// (0x4D0600, »ist Fenster n noch offen?«).</summary>
    public static int OpenCount => Open.Count;

    /// <summary><c>window_open(n)</c> @0x4D0600 → @0x4475B0 — <b>ist
    /// Hilfefenster n noch offen?</b>
    ///
    /// <para>Das Original läuft dafür das Fensterfeld ab (Basis 0x8B9038,
    /// Schrittweite 0xAD24, zwölf Plätze) und gibt 1 zurück, wenn ein Fenster
    /// der Art 0x1E (30) mit +0xACA0 == n dabei ist — dieselbe Art 30, die
    /// <see cref="CloseAll"/> wegräumt. Hier ist das Feld die Liste
    /// <c>Open</c>, und der Vergleich geht über <see cref="Id"/>.</para>
    ///
    /// <para>⚠ Ein VORGEMERKTES Fenster gilt als geschlossen. Im Original
    /// schliesst <c>close_message_windows()</c> sofort; unser Aufschub (siehe
    /// <see cref="CloseAll"/>) ist ein Notbehelf gegen das Neuöffnen im selben
    /// Takt und darf die Antwort nicht verändern — sonst hinge die
    /// Tutorialkette einen Takt lang an einem Fenster, das das Original in
    /// derselben Zeile schon weggeräumt hat.</para>
    ///
    /// <para>Daran hängt die ganze Reihenfolge von Mission 1: #002 kommt erst,
    /// wenn der Spieler #001 weggeklickt hat, #012 erst nach #011.</para>
    /// </summary>
    public static bool IsOpen(int id)
    {
        foreach (var w in Open)
            if (w.Id == id && !w._pendingClose) return true;
        return false;
    }

    /// <summary><c>close_message_windows()</c> @0x447560 — aber AUFGESCHOBEN.
    ///
    /// <para>⚠ 11.08.2026, gemeldet als »die Popups lassen sich nicht
    /// wegklicken«. Es lag nicht am Wegklicken: die Missionsregeln rufen fast
    /// immer erst <c>close_message_windows</c> und dann <c>show_text</c>, und
    /// eine Regel ohne Riegel feuert JEDEN TAKT. Das Fenster wurde also
    /// geschlossen und im selben Atemzug neu geöffnet — wer es wegklickte, sah
    /// es sofort wieder.</para>
    ///
    /// <para>Der Riegel steht im Original vor der Regel (<c>v[n] == 0</c>) und
    /// ist unserem Leser bei einigen Regeln durchgerutscht. Bis der nachgezogen
    /// ist, wird hier dasselbe erreicht wie mit der Frage, die die Blöcke
    /// selbst stellen — »ist Fenster n noch offen?« (0x4D0600): das Schliessen
    /// wird VORGEMERKT, und ein <c>Show</c> mit derselben Nummer nimmt die
    /// Vormerkung zurück, statt das Fenster neu zu bauen. Erst
    /// <see cref="CommitClose"/> am Ende des Takts räumt wirklich weg.</para>
    ///
    /// <para>Ein vom Spieler weggeklicktes Fenster ist aus <c>Open</c>
    /// verschwunden und kommt so auch nicht zurück.</para>
    /// </summary>
    public static void CloseAll()
    {
        foreach (var w in Open) w._pendingClose = true;
    }

    /// <summary>Was noch vorgemerkt ist, jetzt wirklich schliessen.</summary>
    public static void CommitClose()
    {
        for (int i = Open.Count - 1; i >= 0; i--)
            if (Open[i]._pendingClose) { Open[i].QueueFree(); Open.RemoveAt(i); }
    }

    private bool _pendingClose;

    /// <summary>Ein Fenster aufmachen. <paramref name="ox"/>/<paramref name="oy"/>
    /// sind die Stelle im 640×480-Raster des Originals.</summary>
    /// <summary>
    /// <b>SOLANGE GESETZT, GEHT KEIN FENSTER AUF.</b>
    ///
    /// <para>⚠⚠ 18.08.2026, gemeldet: »Popups aus Kampagnen öffnen sich wieder
    /// im Hauptmenü, nachdem man die Kampagne beendet hat.«</para>
    ///
    /// <para>Es ist NICHT die alte Mission, die nachhallt — <see
    /// cref="Core.LeaveToMenu.Tidy"/> ruft <see cref="Forget"/> und räumt sie
    /// weg. Die Fenster sind <b>neu</b>: die Menü-Kulisse spielt einen echten
    /// Spielstand (siehe <see cref="MenuBackdrop"/>, die Demos 1..13 des
    /// Originals), und ein Spielstand bringt seine <b>Missionsregeln</b> mit.
    /// Deren <c>show_text</c> feuert im Menü genauso wie im Spiel.</para>
    ///
    /// <para>Dieselbe Bauart wie <see cref="Settings.FogSuppressed"/>: der
    /// Riegel steht in der Kulisse, nicht in der Regel — die Regel ist
    /// richtig, sie läuft nur am falschen Ort.</para></summary>
    /// <summary>
    /// <b>DAS SPIEL STEHT, SOLANGE EIN HILFEFENSTER OFFEN IST.</b>
    ///
    /// <para>⚠ 18.08.2026, aus der Beobachtung des Spielers am Original: »auch
    /// dass das Spiel während eines Tutorialfensters pausiert, bis man bei
    /// Pause Fenster weiter drückt«.</para>
    ///
    /// <para><b>Das Original hat dafür sogar einen eigenen Schalter</b>, und er
    /// steht seit dem 02.08.2026 in unserem eigenen Quelltext, ohne dass er je
    /// umgesetzt wurde: die Hilfezeilen der Optionen (Tafel 0x4F0280,
    /// Schrittweite 75) nennen acht Schalter, und einer davon heisst wörtlich
    /// <i>»Spiel anhalten waehrend eines Hilfe-Fensters«</i>. In
    /// <c>OPTIONS.CFG</c> neben der Exe stehen acht 0/1-Bytes auf +0x0C — also
    /// dieselbe Anzahl.</para>
    ///
    /// <para>⚠ <b>WELCHES der acht Bytes es ist, ist NICHT gelesen.</b> Die
    /// Reihenfolge der Hilfezeilen muss nicht die der Bytes sein. Unsere
    /// Vorgabe ist deshalb <b>an</b> — so, wie der Spieler es am Original
    /// gesehen hat — und sie steht als eigene Einstellung da, nicht als
    /// stillschweigende Setzung.</para>
    ///
    /// <para>Das Fenster selbst läuft weiter (<c>ProcessMode.Always</c>, siehe
    /// unten) — sonst könnte man es nicht mehr wegklicken.</para></summary>
    public static bool PauseWhileOpen
    {
        get => Settings.PauseOnHelp;
        set => Settings.PauseOnHelp = value;
    }

    /// <summary>Anhalten, solange eines offen ist — und wieder loslassen, wenn
    /// keines mehr da ist. ⚠ An EINER Stelle, damit nicht jeder Aufrufer daran
    /// denken muss; und ⚠ nur, wenn nicht ohnehin schon jemand anders angehalten
    /// hat (das Pausenmenü), sonst liefe das Spiel beim Schliessen des Fensters
    /// hinter dem Pausenmenü weiter.</summary>
    private static void Anhalten(Node host)
    {
        if (!PauseWhileOpen || !PauseErlaubt()) return;
        var tree = host.GetTree();
        if (tree == null) return;
        bool willst = Open.Count > 0;
        if (willst) { _hatAngehalten = true; tree.Paused = true; }
        else if (_hatAngehalten) { _hatAngehalten = false; tree.Paused = false; }
    }

    private static bool _hatAngehalten;

    /// <summary><b>OHNE BILDSCHIRM WIRD NICHT ANGEHALTEN.</b>
    ///
    /// <para>⚠ 18.08.2026, und es ist genau die Falle, vor der die Arbeitsweise
    /// warnt: ein Prüfstand, der stillsteht, sieht aus wie eine Mission, die
    /// nichts tut. Seit Mission 1 ihr Willkommensfenster #001 wieder öffnet
    /// (elf Takte nach dem Start), hielt jeder <c>--headless</c>-Lauf dort an
    /// — und in einem headless-Lauf gibt es niemanden, der das Fenster
    /// wegklicken könnte. <c>--script-check</c> wartet 15 s Spielzeit, die
    /// dann nie vergehen: der Lauf endete ohne eine einzige Zeile, und das las
    /// sich wie ein kaputtes Skript, nicht wie ein angehaltenes Spiel.</para>
    ///
    /// <para>Darum: das Anhalten gilt nur, wo es auch jemanden gibt, der weiter
    /// drücken kann. <c>--no-help-pause</c> schaltet es zusätzlich im Fenster
    /// ab — für einen Bildschirmfoto-Lauf, der über ein Fenster hinauskommen
    /// soll.</para></summary>
    private static bool PauseErlaubt()
    {
        if (_erlaubt.HasValue) return _erlaubt.Value;
        bool aus = false;
        foreach (string a in Core.CommandLine.Args)
            if (a == "--no-help-pause") aus = true;
        if (DisplayServer.GetName() == "headless") aus = true;
        _erlaubt = !aus;
        if (aus)
            GD.Print("Hilfefenster: das Spiel wird NICHT angehalten " +
                     "(kein Bildschirm oder --no-help-pause) — sonst haelt ein " +
                     "Fenster, das niemand wegklicken kann, jeden Prueflauf an");
        return _erlaubt.Value;
    }

    private static bool? _erlaubt;

    public static bool Suppressed;

    /// <summary>Wie viele Fenster der Riegel abgefangen hat — ⚠ ohne die Zahl
    /// ist »es geht keines auf« nicht von »es käme ohnehin keines« zu
    /// unterscheiden.</summary>
    public static int SuppressedCount { get; private set; }

    public static HelpWindow? Show(Node host, int id, int ox, int oy)
    {
        if (Suppressed) { SuppressedCount++; return null; }
        // Vom Spieler weggeklickt? Dann kommt es nicht wieder.
        if (Dismissed.Contains(id)) return null;

        // Schon offen (auch nur vorgemerkt)? Dann bleibt es einfach stehen.
        foreach (var open in Open)
            if (open.Id == id) { open._pendingClose = false; return open; }

        var paras = TextOf(id);
        if (paras == null || paras.Count == 0)
        {
            GD.PrintErr($"Hilfetext {id} gibt es nicht — Fenster faellt aus");
            return null;
        }
        var w = new HelpWindow(id, paras, ox, oy);
        Layer(host).AddChild(w);
        Open.Add(w);
        Anhalten(host);
        return w;
    }

    /// <summary>Die Ebene, auf der die Fenster liegen.
    ///
    /// ⚠ Sie MUSS eine eigene <c>CanvasLayer</c> sein. Der erste Versuch hing
    /// sie an die Wurzel — dort liegen sie in der WELTLEINWAND, und die wird
    /// von der Kamera mitverschoben: das Fenster stand an der berechneten
    /// Stelle, meldete `sichtbar = true`, hatte die richtige Grösse und war im
    /// Bild trotzdem nirgends, weil die Kamera es aus dem Sichtfeld getragen
    /// hatte. Die Statusleiste des Spiels liegt aus demselben Grund auf einer
    /// eigenen Ebene.</summary>
    private const string LayerName = "HelpLayer";

    private static CanvasLayer Layer(Node host)
    {
        var root = host.GetTree().Root;
        foreach (var c in root.GetChildren())
            if (c is CanvasLayer cl && cl.Name == LayerName) return cl;
        // ueber der Statusleiste, damit eine Meldung nie hinter dem Panel
        // verschwindet
        var made = new CanvasLayer { Name = LayerName, Layer = 90 };
        root.AddChild(made);
        return made;
    }

    public int Id { get; }

    private HelpWindow(int id, List<string> paras, int ox, int oy)
    {
        Id = id;
        MouseFilter = MouseFilterEnum.Stop;
        // ⚠ Always: das Fenster muss auch bei angehaltenem Baum noch auf die
        // rechte Maustaste hören — dieselbe Falle, die der SettingsScreen am
        // 10.08. gekostet hat.
        ProcessMode = ProcessModeEnum.Always;

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.07f, 0.94f),
            BorderColor = new Color(0.62f, 0.55f, 0.28f),
            ContentMarginLeft = 14, ContentMarginRight = 14,
            ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        style.SetBorderWidthAll(2);
        AddThemeStyleboxOverride("panel", style);

        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(360, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("default_color", new Color(0.86f, 0.86f, 0.80f));
        label.Text = Markup(paras);

        // ⚠ Ein sichtbares X. Das Original sagt in Text #001 zwar selbst, dass
        // man mit der rechten Maustaste oder ESC schliesst — aber der Spieler
        // hat das Fenster fuer »starr« gehalten, weil nichts danach aussah.
        // Der Knopf ist UNSERE Zutat und aendert am Weg nichts: er ruft
        // dasselbe Dismiss wie die beiden Tasten.
        var row = new HBoxContainer();
        row.AddChild(label);
        var close = new Button
        {
            Text = "X", Flat = true, FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(28, 28),
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            TooltipText = "Schliessen (rechte Maustaste oder ESC)",
        };
        close.AddThemeColorOverride("font_color", new Color(0.85f, 0.78f, 0.45f));
        close.Pressed += Dismiss;
        row.AddChild(close);
        AddChild(row);

        _ox = ox;
        _oy = oy;
    }

    private readonly int _ox, _oy;

    public override void _Ready()
    {
        // ⚠ Erst HIER, nicht im Konstruktor: `GetViewportRect` braucht einen
        // Knoten, der im Baum haengt, und wirft sonst — der erste Lauf des
        // Pruefstands hat genau das gemeldet.
        var vp = GetViewportRect().Size;
        Position = new Vector2(vp.X * _ox / OrigW, vp.Y * _oy / OrigH);
        // und dann so weit hereingeschoben, dass es ganz im Bild steht — UNSERE
        // Zutat, weil unser Fenster nicht 640×480 ist
        CallDeferred(nameof(KeepOnScreen));
    }

    private void KeepOnScreen()
    {
        var vp = GetViewportRect().Size;
        // ⚠ Ein Control, das an einem Fenster und nicht an einem Container
        // haengt, bekommt KEINE Layoutgroesse — es bleibt 0x0 und klemmt seinen
        // Inhalt weg. Im ersten Lauf stand das Fenster an der richtigen Stelle,
        // meldete `sichtbar True` und war im Bild trotzdem nicht da.
        Size = GetCombinedMinimumSize();
        var sz = Size;
        Position = new Vector2(
            Mathf.Clamp(Position.X, 8f, Mathf.Max(8f, vp.X - sz.X - 8f)),
            Mathf.Clamp(Position.Y, 8f, Mathf.Max(8f, vp.Y - sz.Y - 8f)));
    }

    /// <summary>Das <c>@</c> des Originals: es hebt das FOLGENDE WORT hervor.
    /// »@Rechten @Maus @Taste« sind drei Auszeichnungen, keine eine.</summary>
    public static string Markup(List<string> paras)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < paras.Count; i++)
        {
            if (i > 0) sb.Append("\n\n");
            foreach (string word in paras[i].Split(' '))
            {
                if (word.Length > 1 && word[0] == '@')
                    sb.Append("[color=#e8c05a]").Append(Esc(word[1..])).Append("[/color]");
                else sb.Append(Esc(word));
                sb.Append(' ');
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string Esc(string s) => s.Replace("[", "[lb]");

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed &&
            mb.ButtonIndex == MouseButton.Right)
        {
            AcceptEvent();
            Dismiss();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // ESC schliesst das oberste Fenster — »oder der @ESC Taste«, #001.
        // Nur das oberste, damit ESC nicht die ganze Reihe wegraeumt und dann
        // ins Pausenmenue durchschlaegt.
        if (e is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Escape
            && Open.Count > 0 && Open[^1] == this)
        {
            GetViewport().SetInputAsHandled();
            Dismiss();
        }
    }

    private void Dismiss()
    {
        Dismissed.Add(Id);
        Open.Remove(this);
        Anhalten(this);
        QueueFree();
    }
}
