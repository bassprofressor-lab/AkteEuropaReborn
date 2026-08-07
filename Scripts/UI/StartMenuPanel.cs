namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The start menu of the 1997 game, rebuilt from its own code.
///
/// <para><b>The layout is read, not designed.</b> The hover dispatcher
/// @0x45cb60 tests one rectangle per entry (@0x461101 onwards) and the drawing
/// routine @0x480430 writes the caption at the same place, so both halves agree:
/// every entry is <b>160 x 20 pixels at x = window.x + 20</b>, and the nine of
/// them sit at y + <b>25, 45, 65, 85, 110, 135, 155, 175, 200</b>. The caption
/// is drawn at the box's own corner in palette colour 8.</para>
///
/// <para><b>The captions are the game's</b>, out of the draw calls at
/// 0x480454..0x4805a8: <i>Neues Spiel · Spiel laden · Netzwerkspiel ·
/// Einstellungen · Enzyklopaedie · Intro ansehen · Credits · Naechstes Demo ·
/// Beenden</i>. So is the status line under the pointer: each entry sets a help
/// index into the table at 0x4f0280 (stride 75) — 104..111 for eight of them and
/// <b>118</b> for "Naechstes Demo", which is the one entry that breaks the run.
/// </para>
///
/// <para><b>There is no backdrop picture and this is not a guess.</b>
/// <c>CWMENU.DAT</c> lies in the installation at exactly the size of
/// WINDOWS.CWW and looks the part, but the string "CWMENU" appears in <b>none of
/// the 40 files</b> of the installation, GAME.EXE and RUN.EXE included. Nothing
/// opens it. No other file is named as a menu picture either. So the original
/// draws this menu as a window over the screen, and the frame below —
/// its colours, its border, its highlight — is <b>OURS</b>. The positions,
/// sizes, order, captions and help lines are not.</para>
///
/// <para><b>One entry is added</b>, and it is marked: <i>Gefecht</i>, which the
/// original had no equivalent for (its multiplayer was "Netzwerkspiel"). It is
/// placed at the end rather than in the middle so the original's own order stays
/// readable.</para>
/// </summary>
public partial class StartMenuPanel : Control
{
    /// <summary>The greys of the window ramp, straight out of DATA/01.PAL —
    /// 0x28 (127,115,115), 0x2a (91,83,83), 0x2c (55,47,47), 0x2f (19,19,15).
    /// Hard-coded rather than read at runtime: four numbers do not justify
    /// loading a palette, and they are written down here so the next reader can
    /// check them against the file.</summary>
    internal static Color Pal(int i) => i switch
    {
        0x24 => Color.Color8(187, 179, 179),
        0x28 => Color.Color8(127, 115, 115),
        0x29 => Color.Color8(111, 99, 99),
        0x2a => Color.Color8(91, 83, 83),
        0x2c => Color.Color8(55, 47, 47),
        0x2f => Color.Color8(19, 19, 15),
        _ => Color.Color8(91, 83, 83),
    };

    /// <summary>The original's own geometry, in its 640x480 pixels.</summary>
    public const int EntryW = 160, EntryH = 20, EntryX = 20;
    public const int PanelW = 200, PanelH = 232;

    /// <summary>One row: where it sits, what it says, and the help line the
    /// original shows for it.</summary>
    public readonly record struct Row(int Y, string Caption, int HelpIndex, string Help, Action? Go);

    /// <summary>The nine of the original, in its order and at its offsets. The
    /// help texts are the strings the table at 0x4f0280 holds for those indices,
    /// transcribed without their umlauts because the rest of this screen is.
    /// </summary>
    public static IEnumerable<Row> Original(
        Action newGame, Action load, Action net, Action settings,
        Action encyclopedia, Action intro, Action credits, Action demo, Action quit)
    {
        yield return new(25, "Neues Spiel", 104, "Neues Spiel starten", newGame);
        yield return new(45, "Spiel laden", 105, "Laden eines Spielstands", load);
        yield return new(65, "Netzwerkspiel", 106, "Netzwerk-Spiel einrichten", net);
        yield return new(85, "Einstellungen", 107, "Einstellbare Optionen zeigen", settings);
        yield return new(110, "Enzyklopaedie", 108, "Enzyklopaedie aufschlagen", encyclopedia);
        yield return new(135, "Intro ansehen", 109, "Intro-Film ansehen", intro);
        yield return new(155, "Credits", 110, "Credits zeigen", credits);
        yield return new(175, "Naechstes Demo", 118, "Naechstes Demo zeigen", demo);
        yield return new(200, "Beenden", 111, "Spiel verlassen", quit);
    }

    private Label _status = null!;
    private readonly List<(Row R, Panel Box, Label Text)> _rows = new();
    private readonly Dictionary<string, Panel> _boxes = new();

    /// <summary>Where a row sits on screen, for the harness: a scripted run
    /// cannot click, and a menu whose rows only LOOK right is worth nothing.
    /// Returns false when there is no such caption.</summary>
    public bool RowCentre(string caption, out Vector2 at)
    {
        at = Vector2.Zero;
        if (!_boxes.TryGetValue(caption, out var b) || !GodotObject.IsInstanceValid(b)) return false;
        at = b.GetGlobalRect().GetCenter();
        return true;
    }

    /// <summary>The captions in order, so the harness can report what it saw.</summary>
    public List<string> Captions()
    {
        var l = new List<string>();
        foreach (var (r, _, _) in _rows) l.Add(r.Caption);
        return l;
    }
    private Font? _font;

    /// <summary>How many screen pixels one of the original's. Integer, so the
    /// bitmap typeface stays a bitmap typeface.</summary>
    public int Scale { get; set; } = 2;

    /// <summary>The rows to show, set before the panel enters the tree.</summary>
    public List<Row> Rows { get; } = new();

    /// <summary>Puts a row of ours in after one of the original's and moves
    /// everything below it down by one row's pitch.
    ///
    /// <para>The first try hung the added "Gefecht" on the end, below
    /// <i>Beenden</i> — where nobody looks, and the player duly reported the
    /// skirmish as missing from 0.3.0. It goes under <i>Netzwerkspiel</i> now,
    /// which is where someone looks for a game against an opponent. The
    /// original's order and its spacing survive; one row is inserted and the
    /// rest slides, which is OURS and is the smallest change that makes the
    /// entry findable.</para></summary>
    public static List<Row> InsertAfter(IEnumerable<Row> rows, string afterCaption, Row added)
    {
        var list = new List<Row>(rows);
        int at = list.FindIndex(r => r.Caption == afterCaption);
        if (at < 0) { list.Add(added); return list; }

        int pitch = EntryH;                       // 20, the original's own step
        int y = list[at].Y + pitch;
        list.Insert(at + 1, added with { Y = y });
        for (int i = at + 2; i < list.Count; i++)
            list[i] = list[i] with { Y = list[i].Y + pitch };
        return list;
    }

    /// <summary>Shown under the list — ours, because the original's help line
    /// lives in its side panel, which this screen has not got.</summary>
    public string Footer = "";

    public override void _Ready()
    {
        // anchors AND offsets: the preset alone leaves the control at size zero,
        // and everything anchored to its middle then lands in the top-left corner
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // FONT.CWD, the game's everyday face — FONT2 belongs to the briefing
        _font = BriefingScreen.LegacyFont(second: false);

        // the original's window is 200 x 232 around its nine rows; an added row
        // makes it taller rather than making the rows sit closer
        int bottom = PanelH;
        foreach (var r in Rows) bottom = Math.Max(bottom, r.Y + EntryH + 12);
        int w = PanelW * Scale, h = bottom * Scale;
        var panel = new Panel
        {
            AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -w / 2, OffsetTop = -h / 2, OffsetRight = w / 2, OffsetBottom = h / 2,
        };
        // The window is grey because the game's typeface says so. FONT.CWD's
        // glyphs are not a plain mask: their dark pixels are literal palette
        // index 0x2f = (19,19,15), which is the bottom of the window ramp
        // 0x28..0x2f that WINDOWS.CWW is drawn in — 0x28 = (127,115,115) down to
        // 0x2f. Letters made of that ramp only read on a window of that ramp,
        // which is why a dark panel swallowed them. The exact shades are ours;
        // that they are these greys and not something else is the palette's.
        var style = new StyleBoxFlat
        {
            BgColor = Pal(0x2a),                                  // (91,83,83)
            BorderColor = Pal(0x28),                              // (127,115,115)
            BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        foreach (var r in Rows)
        {
            var box = new Panel
            {
                Position = new Vector2(EntryX * Scale, r.Y * Scale),
                Size = new Vector2(EntryW * Scale, EntryH * Scale),
                MouseFilter = MouseFilterEnum.Stop,
            };
            var flat = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
            box.AddThemeStyleboxOverride("panel", flat);
            panel.AddChild(box);

            var text = new Label
            {
                Text = r.Caption,
                Position = new Vector2(4 * Scale, 1 * Scale),
                Size = new Vector2((EntryW - 8) * Scale, (EntryH - 2) * Scale),
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            // font_color, not Modulate: the exported atlas keeps the glyph body
            // WHITE and the original's drop shadow BLACK, so a colour override
            // tints the letters and leaves the shadow dark — Modulate would
            // dim both and the shadow would swallow the letter
            text.AddThemeColorOverride("font_color",
                r.Go == null ? Pal(0x29) : Color.Color8(235, 231, 231));   // 0xfe, the text colour
            if (_font != null)
            {
                text.AddThemeFontOverride("font", _font);
                text.AddThemeFontSizeOverride("font_size", 13 * Scale);
            }
            else text.AddThemeFontSizeOverride("font_size", 9 * Scale);
            box.AddChild(text);

            var row = r;
            var stylebox = flat;
            var label = text;
            box.MouseEntered += () =>
            {
                stylebox.BgColor = Pal(0x28);        // one step up the same ramp
                _status.Text = row.Help;
            };
            box.MouseExited += () =>
            {
                stylebox.BgColor = new Color(0, 0, 0, 0);
                if (_status.Text == row.Help) _status.Text = Footer;
            };
            box.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    // silent, like the original: neither the row hit test
                    // @0x461101 nor the draw @0x480430 nor the menu handler
                    // @0x447940 calls the sound routine
                    if (row.Go != null) row.Go();
                    else _status.Text = row.Help + "  —  gibt es im Remake noch nicht";
                }
            };
            _rows.Add((row, box, text));
            _boxes[row.Caption] = box;
        }

        // the help line. In the original it is shown in the side panel; there is
        // none here, so it sits under the window — OURS in placement only, the
        // words are the game's.
        _status = new Label
        {
            Text = Footer,
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetTop = h / 2 + 12, OffsetBottom = h / 2 + 40,
            Modulate = new Color(0.62f, 0.68f, 0.75f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_status);

        // The version, bottom right under the window. OURS entirely — the 1997
        // menu shows no version anywhere. It reads `application/config/version`
        // out of project.godot, which is the same string the installer script
        // carries as AppVersion, so there is one place to change it.
        var ver = new Label
        {
            Text = "v" + VersionString,
            HorizontalAlignment = HorizontalAlignment.Right,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
            OffsetTop = -26, OffsetBottom = -8, OffsetRight = -12,
            Modulate = new Color(0.52f, 0.56f, 0.62f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (_font != null) ver.AddThemeFontOverride("font", _font);
        AddChild(ver);
    }

    /// <summary>The build's version, from project.godot. Empty settings fall
    /// back to a dash rather than to an invented number.</summary>
    public static string VersionString
    {
        get
        {
            var v = ProjectSettings.GetSetting("application/config/version");
            string s = v.VariantType == Variant.Type.Nil ? "" : v.AsString();
            return s.Length > 0 ? s : "—";
        }
    }
}
