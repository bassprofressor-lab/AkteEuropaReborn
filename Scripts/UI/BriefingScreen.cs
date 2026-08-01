namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// What the campaign was missing: the mission's own briefing, shown before the
/// map instead of dropping the player straight into it.
///
/// The text is the game's, out of BRIEFG.TXT (see
/// <see cref="Import.BriefingExporter"/>); the typeface is the game's, the same
/// bitmap font the HUD uses. The layout is ours — the original drew its briefing
/// on a fixed 320x200 screen with a picture behind it, and BRIEFG.DAT, the
/// picture file, has not been decoded.
///
/// It sits in front of everything as its own CanvasLayer, the way the end-of-
/// mission banner does, and hands control on with a callback rather than
/// changing the scene itself — so the one place that starts a mission stays the
/// one place that starts a mission.
/// </summary>
public partial class BriefingScreen : CanvasLayer
{
    private readonly List<string> _paragraphs;
    private readonly string _title;
    private readonly System.Action _go;

    public BriefingScreen(string title, List<string> paragraphs, System.Action onContinue)
    {
        _title = title;
        _paragraphs = paragraphs;
        _go = onContinue;
        Layer = 10;
    }

    public override void _Ready()
    {
        // fully opaque: the menu behind it must not read through the text
        AddChild(new ColorRect
        {
            Color = new Color(0.03f, 0.04f, 0.05f),
            AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        });

        // The original's own briefing screen, if it has been imported: BRIEFG.DAT
        // is a 640x480 picture with a white plate at (296,79) 320x240 that the
        // text is written on. Drawn at a whole multiple so the pixels stay square,
        // with the text laid into the plate. Without it, the plain layout below.
        if (BuildBackdrop()) return;

        // Fill the screen with a margin rather than centring a fixed block: a
        // briefing runs from two paragraphs to a page and a half, and a centred
        // box of a guessed height pushes the long ones off the top and bottom.
        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, side is "left" or "right" ? 60 : 34);
        AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        margin.AddChild(box);

        var font = LegacyFont();

        var head = new Label
        {
            Text = _title.ToUpperInvariant(),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Style(head, font, 2, head: true);
        box.AddChild(head);
        box.AddChild(new HSeparator());

        // the text scrolls, so even the longest briefing is fully readable
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);

        var body = new Label
        {
            Text = string.Join("\n\n", _paragraphs),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Style(body, font, 2);
        scroll.AddChild(body);

        box.AddChild(new HSeparator());
        var go = new Button { Text = "Auftrag annehmen  [Enter]" };
        go.Pressed += Continue;
        box.AddChild(go);
        go.CallDeferred(Control.MethodName.GrabFocus);
    }

    // ---- the original's screen ---------------------------------------------

    /// <summary>Where the text goes on BRIEFG.DAT, measured off the picture:
    /// every row and column of the plate is palette index 144, and it comes out
    /// at exactly 320x240 — see <see cref="Import.BriefingExporter"/>.</summary>
    private const int BgW = 640, BgH = 480, PlateX = 296, PlateY = 79, PlateW = 320, PlateH = 240;

    /// <summary>Builds the faithful screen. False if the picture is not there,
    /// in which case the caller falls back to the plain layout.</summary>
    private bool BuildBackdrop()
    {
        string path = Core.Content.Path("UI/briefing_bg.png");
        Texture2D? tex = null;
        if (ResourceLoader.Exists(path)) tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null && FileAccess.FileExists(path))
        {
            // imported content has no Godot import step — read the file directly
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        if (tex == null) return false;

        var view = GetViewport().GetVisibleRect().Size;
        int scale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(view.X / BgW, view.Y / BgH)));
        var size = new Vector2(BgW * scale, BgH * scale);
        var at = ((view - size) * 0.5f).Floor();

        var pic = new TextureRect
        {
            Texture = tex,
            Position = at,
            Size = size,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        AddChild(pic);

        var font = LegacyFont();

        // the mission's name on the picture's own header strip
        var head = new Label
        {
            Text = _title.ToUpperInvariant(),
            Position = at + new Vector2(PlateX * scale, (PlateY - 26) * scale),
            Size = new Vector2(PlateW * scale, 22 * scale),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Style(head, font, scale, head: true);
        AddChild(head);

        // the text goes on the white plate, so it is written dark
        var scroll = new ScrollContainer
        {
            Position = at + new Vector2((PlateX + 8) * scale, (PlateY + 6) * scale),
            Size = new Vector2((PlateW - 16) * scale, (PlateH - 12) * scale),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);

        var body = new Label
        {
            Text = string.Join("\n\n", _paragraphs),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2((PlateW - 16) * scale, 0),
        };
        Style(body, font, scale, dark: true);
        scroll.AddChild(body);

        var go = new Button
        {
            Text = "Auftrag annehmen  [Enter]",
            Position = at + new Vector2(PlateX * scale, (PlateY + PlateH + 10) * scale),
            Size = new Vector2(PlateW * scale, 26 * scale),
        };
        go.Pressed += Continue;
        AddChild(go);
        go.CallDeferred(Control.MethodName.GrabFocus);
        return true;
    }

    /// <summary>The game's own bitmap font, loaded the way MapViewer loads it —
    /// imported resource first, raw .fnt second, because content derived on the
    /// player's machine never went through Godot's import step.</summary>
    private static Font? LegacyFont()
    {
        // FONT2.CWD is this screen's own typeface — the loader @0x45bddc reads
        // it immediately before the backdrop. FONT.CWD stands in for content
        // imported before it was exported.
        string path = Core.Content.Path("UI/akte_font2.fnt");
        if (!FileAccess.FileExists(path) && !ResourceLoader.Exists(path))
            path = Core.Content.Path("UI/akte_font.fnt");
        FontFile? f = null;
        if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is FontFile res)
            f = (FontFile)res.Duplicate();       // ours to configure, not the HUD's
        else if (FileAccess.FileExists(path))
        {
            var bmp = new FontFile();
            if (bmp.LoadBitmapFont(path) == Error.Ok) f = bmp;
        }
        if (f == null) return null;

        // A bitmap font has one size, and Godot renders it at that size no matter
        // what font_size says — until scaling is allowed. Whole multiples only, so
        // the 13 px glyphs stay sharp instead of turning to mush.
        f.FixedSizeScaleMode = TextServer.FixedSizeScaleMode.IntegerOnly;

        // FONT.CWD holds 160 glyphs, cp437 0x20..0xBF — and the game's own
        // briefing text uses 0xE1, the sharp s ("in einem Fluß notwassern",
        // "außerdem"). There is no glyph for it, in FONT.CWD or in the
        // unused FONT2.CWD, so the original could not have drawn one either.
        // Rather than quietly rewriting the game's words to "ss", the missing
        // characters come from the default face; everything the original has,
        // the original draws.
        f.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        return f;
    }

    /// <summary>The atlas holds the glyphs white and the shadow black, so the
    /// colour comes from the modulation rather than the pixels.</summary>
    private static void Style(Label l, Font? font, int scale, bool head = false, bool dark = false)
    {
        if (font != null)
        {
            l.AddThemeFontOverride("font", font);
            // the atlas cell is 13 px high; whole multiples keep it crisp, the
            // same rule MapViewer follows for the HUD
            l.AddThemeFontSizeOverride("font_size", 13 * scale);
        }
        else l.AddThemeFontSizeOverride("font_size", scale > 1 ? 28 : 17);
        l.AddThemeColorOverride("font_color",
            dark ? new Color(0.09f, 0.09f, 0.07f)          // on the white plate
                 : head ? new Color(1f, 0.86f, 0.45f)
                        : new Color(0.95f, 0.96f, 0.97f));
    }

    // ---- harness -----------------------------------------------------------

    private int _frames;

    /// <summary>`--briefing-shot=<path>` photographs the screen and quits, so
    /// what it actually looks like can be checked rather than assumed.</summary>
    public override void _Process(double delta)
    {
        string want = "";
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--briefing-shot=")) want = a["--briefing-shot=".Length..];
        if (want.Length == 0 || _frames++ < 20) return;
        RenderingServer.ForceDraw();
        GetViewport().GetTexture().GetImage().SavePng(want);
        GD.Print($"briefing-shot -> {want}");
        GetTree().Quit();
    }

    private bool _done;

    private void Continue()
    {
        if (_done) return;
        _done = true;
        QueueFree();
        _go();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } k) return;
        if (k.Keycode is Key.Enter or Key.KpEnter or Key.Space or Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Continue();
        }
    }

    // ---- the text ----------------------------------------------------------

    /// <summary>The briefing for a mission, or null if there is none. Missing
    /// text is not an error: a skirmish has no briefing, and neither has a
    /// build whose content was imported before this table existed.</summary>
    public static (string Title, List<string> Paragraphs)? For(int mission)
    {
        string path = Core.Content.Path("Maps/briefings.json");
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return null;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return null;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("briefings", out var bv) ||
            bv.VariantType != Variant.Type.Dictionary) return null;
        var all = bv.AsGodotDictionary<string, Variant>();
        if (!all.TryGetValue(mission.ToString(), out var one) ||
            one.VariantType != Variant.Type.Dictionary) return null;
        var d = one.AsGodotDictionary<string, Variant>();

        var paras = new List<string>();
        if (d.TryGetValue("paragraphs", out var pv) && pv.VariantType == Variant.Type.Array)
            foreach (var p in pv.AsGodotArray()) paras.Add(p.AsString());
        if (paras.Count == 0) return null;
        return (d.TryGetValue("title", out var tv) ? tv.AsString() : $"Mission {mission}", paras);
    }
}
