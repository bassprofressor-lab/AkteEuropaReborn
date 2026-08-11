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

    /// <summary>Alles vergessen — für einen Prüfstand, der nach einem Import
    /// neu laden will.</summary>
    public static void Forget() { _texts = null; _tried = false; }

    /// <summary>Wieviele Fenster gerade offen sind. Der Block fragt das selbst
    /// (0x4D0600, »ist Fenster n noch offen?«).</summary>
    public static int OpenCount => Open.Count;

    /// <summary><c>close_message_windows()</c> @0x447560.</summary>
    public static void CloseAll()
    {
        foreach (var w in Open.ToArray()) w.QueueFree();
        Open.Clear();
    }

    /// <summary>Ein Fenster aufmachen. <paramref name="ox"/>/<paramref name="oy"/>
    /// sind die Stelle im 640×480-Raster des Originals.</summary>
    public static HelpWindow? Show(Node host, int id, int ox, int oy)
    {
        var paras = TextOf(id);
        if (paras == null || paras.Count == 0)
        {
            GD.PrintErr($"Hilfetext {id} gibt es nicht — Fenster faellt aus");
            return null;
        }
        var w = new HelpWindow(id, paras, ox, oy);
        Layer(host).AddChild(w);
        Open.Add(w);
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
        AddChild(label);

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
        Open.Remove(this);
        QueueFree();
    }
}
