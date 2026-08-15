namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>Die Enzyklopädie</b> — die Menüzeile des Originals, und dahinter der
/// Inhalt des Originals.
///
/// <para><b>Wie es dazu kam</b> (17.08.2026, Fehler C19). Der Wunsch war, die
/// Zeile auf unser Wiki zu verlinken. Beim Nachsehen, was das Original hinter
/// ihr hat, lag <c>ENCYCLOG.TXT</c> mit 106 Seiten neben GAME.EXE — Fahrwerke,
/// Waffen, Zubehör, Verbesserungen, Luftwaffe, Marine, Dicke Bertha,
/// Infanterie, Gebäude, alles im Volltext. Der Spieler hat daraufhin
/// entschieden: »ja dann nimm die originale rein«. Das Auslesen steht in
/// <see cref="Import.EncyclopediaExporter"/>, samt der Kodierungsfalle.</para>
///
/// <para><b>Was UNSER ist:</b> die Darstellung. Das Original zeichnet auf einen
/// festen 1997er-Schirm mit eigenen Umbrüchen und einem Bild je Seite; hier
/// steht ein Fenster mit Rollbalken, das den Text neu umbricht. Die
/// VERWEISE sind dagegen die des Originals (<c>#rN</c>) und werden zu Knöpfen —
/// eine Enzyklopädie ohne ihre Querverweise wäre eine Textdatei.</para>
///
/// <para>⚠ <b>Kein Bild.</b> Die Seiten tragen eine Bildnummer, aber
/// <c>ENCYCLOG.PIC</c> ist ungelesen (siehe Exporter). Lieber eine ehrliche
/// Lücke als ein geratenes Bild.</para>
///
/// <para>⚠ Der Rahmen ist der von <see cref="SettingsScreen"/> und nicht der von
/// <see cref="LoadGameScreen"/>: voller Rechteckanker MIT Rändern, darin ein
/// <c>ScrollContainer</c>. Genau daran hing Fehler C20 — ein Fenster, das nur
/// seine Anker setzt, behält ein Rechteck der Größe null und klebt in der
/// linken oberen Ecke.</para>
/// </summary>
public partial class EncyclopediaScreen : Control
{
    private sealed class Page
    {
        public string Title = "", Body = "";
        public readonly List<(int To, string Text)> Links = new();
    }

    private static Dictionary<int, Page>? _pages;
    private static bool _tried;

    /// <summary>Die Startseite — »Inhalt« im Original.</summary>
    private const int FirstPage = 1;

    private readonly List<int> _history = new();
    private int _page = FirstPage;

    private VBoxContainer? _box;
    private Label? _title;
    private Label? _body;
    private VBoxContainer? _links;
    private Button? _back;

    /// <summary>Liest <c>encyclopedia.json</c> einmal. Fehlt sie, sagt der Schirm
    /// das — er erfindet keinen Inhalt.</summary>
    private static void Load()
    {
        if (_tried) return;
        _tried = true;
        string path = Core.Content.Path("UI/encyclopedia.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("pages", out var pv) ||
            pv.VariantType != Variant.Type.Dictionary) return;

        var map = new Dictionary<int, Page>();
        foreach (var kv in pv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int num)) continue;
            if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
            var d = kv.Value.AsGodotDictionary<string, Variant>();
            var p = new Page
            {
                Title = d.TryGetValue("title", out var t) ? t.AsString() : "",
                Body = d.TryGetValue("body", out var b) ? b.AsString() : "",
            };
            if (d.TryGetValue("links", out var lv) && lv.VariantType == Variant.Type.Array)
                foreach (var item in lv.AsGodotArray())
                {
                    if (item.VariantType != Variant.Type.Dictionary) continue;
                    var l = item.AsGodotDictionary<string, Variant>();
                    p.Links.Add((l.TryGetValue("to", out var to) ? to.AsInt32() : -1,
                                 l.TryGetValue("text", out var tx) ? tx.AsString() : ""));
                }
            map[num] = p;
        }
        if (map.Count > 0) _pages = map;
    }

    /// <summary>Für den Prüfstand: wie viele Seiten geladen sind.</summary>
    public static int PageCount { get { Load(); return _pages?.Count ?? 0; } }

    public override void _Ready()
    {
        Load();
        // ⚠ ANKER UND RÄNDER, siehe Klassenkopf und Fehler C20.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var dim = new ColorRect { Color = new Color(0.03f, 0.04f, 0.06f, 1f) };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(dim);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var middle = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(middle);

        _box = new VBoxContainer { CustomMinimumSize = new Vector2(720, 0) };
        _box.AddThemeConstantOverride("separation", 10);
        middle.AddChild(_box);

        var head = new Label
        {
            Text = "ENZYKLOPAEDIE",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.72f, 0.76f, 0.82f),
        };
        head.AddThemeFontSizeOverride("font_size", 22);
        _box.AddChild(head);

        if (_pages == null)
        {
            _box.AddChild(new Label
            {
                Text = "Die Enzyklopaedie ist noch nicht eingespielt.\n" +
                       "Sie entsteht aus ENCYCLOG.TXT der eigenen Spielfassung —\n" +
                       "einmal importieren, dann steht sie hier.",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            AddClose();
            return;
        }

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 30);
        _box.AddChild(_title);

        _body = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.86f, 0.85f, 0.80f),
        };
        _box.AddChild(_body);

        _links = new VBoxContainer();
        _links.AddThemeConstantOverride("separation", 4);
        _box.AddChild(_links);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        _box.AddChild(row);
        _back = new Button { Text = "Zurueck", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _back.Pressed += () =>
        {
            if (_history.Count == 0) return;
            _page = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            Show(_page, remember: false);
        };
        row.AddChild(_back);
        var home = new Button { Text = "Inhalt", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        home.Pressed += () => Show(FirstPage);
        row.AddChild(home);
        var close = new Button { Text = "Schliessen", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        close.Pressed += QueueFree;
        row.AddChild(close);

        Show(FirstPage, remember: false);
    }

    private void AddClose()
    {
        var close = new Button { Text = "Schliessen" };
        close.Pressed += QueueFree;
        _box?.AddChild(close);
    }

    private void Show(int number, bool remember = true)
    {
        if (_pages == null || _title == null || _body == null || _links == null) return;
        if (!_pages.TryGetValue(number, out var p))
        {
            // ⚠ Ein Verweis, der ins Leere zeigt, wird GESAGT und nicht
            // verschluckt: die Seitennummern haben Lücken (1..162 für 106
            // Seiten), und ein stiller Sprung auf die Inhaltsseite würde einen
            // Lesefehler im Exporter verstecken.
            _title.Text = $"Seite {number}";
            _body.Text = "Diese Seite ist in ENCYCLOG.TXT nicht enthalten.";
            foreach (var c in _links.GetChildren()) (c as Node)?.QueueFree();
            return;
        }
        if (remember && number != _page) _history.Add(_page);
        _page = number;

        _title.Text = p.Title.Length > 0 ? p.Title : $"Seite {number}";
        _body.Text = p.Body;
        _body.Visible = p.Body.Length > 0;

        foreach (var c in _links.GetChildren()) (c as Node)?.QueueFree();
        foreach (var (to, text) in p.Links)
        {
            var b = new Button { Text = text, Alignment = HorizontalAlignment.Left };
            int target = to;
            b.Pressed += () => Show(target);
            _links.AddChild(b);
        }
        if (_back != null) _back.Disabled = _history.Count == 0;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            QueueFree();
            GetViewport().SetInputAsHandled();
        }
    }
}
