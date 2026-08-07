namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// ESC in a running game: hold everything and offer the way out.
///
/// <para><b>OURS from end to end.</b> The 1997 game has no pause screen — ESC
/// there leaves to the menu straight away. The player asked for one (Fehlerliste
/// Punkt 9): "wenn man ESC drückt im Gefecht oder Kampagne, dass dies gleich
/// Pause ist und man dort wählen kann wie Neustart oder Beenden. Da kann dann
/// auch Speichern rein. Spielstand Laden muss ins Hauptmenü."</para>
///
/// <para>The look follows <see cref="StartMenuPanel"/> so it does not read as a
/// different program: the same grey window out of the game's own palette ramp,
/// the same font, rows of the same height.</para>
/// </summary>
public partial class PauseMenu : Control
{
    /// <summary>Raised when the player picks something. The host decides what
    /// "restart" and "quit" mean — this screen only asks.</summary>
    public event Action? Resumed;
    public event Action? Restarted;
    public event Action? Quit;
    public event Action? SaveRequested;

    /// <summary>Whether a save entry is offered at all. Off until saving
    /// exists, so the screen never shows a button that does nothing.</summary>
    public bool CanSave { get; init; }

    private const int RowH = 22, RowW = 150, PanelW = 190, Scale = 2;

    public override void _Ready()
    {
        // Sit over the whole viewport and swallow clicks.
        //
        // ⚠ Both lines are needed, and the second one is the fix for Fehlerliste
        // Punkt 20 ("Pausenmenü nicht mittig"). This screen hangs under the HUD's
        // CanvasLayer, and a CanvasLayer is not a Control: the layout pass leaves
        // a Control under it at size ZERO. The panel below centres itself on the
        // parent with anchors of 0.5, so on a zero-sized parent it centred on
        // (0,0) and half of it hung off the top-left corner of the screen —
        // photographed, that is exactly what one saw.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Size = GetViewportRect().Size;
        GetViewport().SizeChanged += () => Size = GetViewportRect().Size;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;      // runs while the tree is paused

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.45f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(dim);

        var rows = new System.Collections.Generic.List<(string Text, Action Hit)>
        {
            ("Weiter", () => Resumed?.Invoke()),
            ("Neu starten", () => Restarted?.Invoke()),
        };
        if (CanSave) rows.Add(("Speichern", () => SaveRequested?.Invoke()));
        rows.Add(("Beenden", () => Quit?.Invoke()));

        int h = 18 + rows.Count * (RowH + 6) + 12;
        int pw = PanelW * Scale, ph = h * Scale;
        var panel = new Panel
        {
            AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -pw / 2, OffsetTop = -ph / 2, OffsetRight = pw / 2, OffsetBottom = ph / 2,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = StartMenuPanel.Pal(0x2a),
            BorderColor = StartMenuPanel.Pal(0x28),
            BorderWidthTop = 2, BorderWidthBottom = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2,
        });
        AddChild(panel);

        var font = BriefingScreen.LegacyFont(second: false);
        var title = new Label
        {
            Text = "Pause",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 6 * Scale),
            Size = new Vector2(pw, 16 * Scale),
        };
        if (font != null) title.AddThemeFontOverride("font", font);
        panel.AddChild(title);

        for (int i = 0; i < rows.Count; i++)
        {
            var (text, hit) = rows[i];
            var b = new Button
            {
                Text = text,
                Position = new Vector2((PanelW - RowW) / 2 * Scale, (24 + i * (RowH + 6)) * Scale),
                Size = new Vector2(RowW * Scale, RowH * Scale),
            };
            if (font != null) b.AddThemeFontOverride("font", font);
            b.Pressed += () => hit();
            panel.AddChild(b);
        }
    }

    /// <summary>ESC again closes it, like every other screen in this game.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Resumed?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }
}
