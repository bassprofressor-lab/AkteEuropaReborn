namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// "Spiel laden" out of the main menu: pick one of the saves and start it.
///
/// <para>The row is the original's — it sits second in the 1997 menu and was
/// dead until now. What it opens is OURS, like the save format itself (see
/// <see cref="Core.SaveGame"/>).</para>
///
/// <para>Loading works the same way starting a mission does: the screen sets
/// the map and mission the save names, hands the save over in
/// <see cref="SkirmishSetup.PendingSave"/>, and switches to the game scene.
/// That scene loads the map from the player's own content and then puts the
/// state on top — so a save never carries a map with it and cannot go stale
/// against reimported content.</para>
/// </summary>
public partial class LoadGameScreen : Control
{
    private const int RowH = 22, RowW = 300, PanelW = 340, Scale = 2;

    public override void _Ready()
    {
        // ⚠ 17.08.2026 — HIER STAND `SetAnchorsPreset`, UND DAS IST DER FEHLER C20
        // (»man drückt drauf und das Fenster ist nicht mittig, sondern oben
        // links«). Die zwei Namen sehen gleich aus und tun Verschiedenes:
        // `SetAnchorsPreset` setzt NUR die Anker und lässt die Ränder stehen —
        // ein frisch angelegtes Control hat die alle auf 0, behält also ein
        // Rechteck der GRÖSSE NULL in der linken oberen Ecke. Der Panel darunter
        // hängt an Anker 0,5 mit `-pw/2`, und die halbe Breite von null ist null:
        // das Fenster landet exakt dort, wo es gemeldet wurde.
        // `SetAnchorsAndOffsetsPreset` setzt beides und füllt wirklich.
        //
        // ⚠ Es ist DERSELBE Fehler, der in SettingsScreen.cs:56 schon einmal
        // gefunden und dort im Kommentar festgehalten wurde (»Nothing measures a
        // container there, so it kept a zero-size rect … up and to the left of
        // the menu, which is where it was reported«). Er ist wiedergekommen,
        // weil die Kur dort im Fliesstext stand und nicht am Aufruf.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f) };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(dim);

        var saves = Core.SaveGame.List();
        int rows = Mathf.Max(1, saves.Count) + 1;          // + "Zurueck"
        int h = 26 + rows * (RowH + 6) + 10;
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
            Text = saves.Count > 0 ? "Spiel laden" : "Kein Spielstand vorhanden",
            HorizontalAlignment = HorizontalAlignment.Center,
            Position = new Vector2(0, 6 * Scale),
            Size = new Vector2(pw, 16 * Scale),
        };
        if (font != null) title.AddThemeFontOverride("font", font);
        panel.AddChild(title);

        int y = 30;
        foreach (var (name, label, _) in saves)
        {
            var b = new Button
            {
                Text = label,
                Position = new Vector2((PanelW - RowW) / 2 * Scale, y * Scale),
                Size = new Vector2(RowW * Scale, RowH * Scale),
                ClipText = true,
            };
            if (font != null) b.AddThemeFontOverride("font", font);
            string pick = name;
            b.Pressed += () => Start(pick);
            panel.AddChild(b);
            y += RowH + 6;
        }

        var back = new Button
        {
            Text = "Zurueck",
            Position = new Vector2((PanelW - RowW) / 2 * Scale, y * Scale),
            Size = new Vector2(RowW * Scale, RowH * Scale),
        };
        if (font != null) back.AddThemeFontOverride("font", font);
        back.Pressed += QueueFree;
        panel.AddChild(back);
    }

    private void Start(string name)
    {
        var root = Core.SaveGame.Read(name, out string err);
        if (root == null) { GD.PrintErr($"Spielstand: {err}"); return; }

        SkirmishSetup.Map = root.TryGetValue("map", out var mv) ? mv.AsString() : "";
        SkirmishSetup.CampaignMission =
            root.TryGetValue("campaign_mission", out var cm) ? cm.AsInt32() : 0;
        SkirmishSetup.Active = root.TryGetValue("skirmish", out var sk) && sk.AsBool();
        SkirmishSetup.PendingSave = name;
        GetTree().ChangeSceneToFile(SkirmishSetup.GameScene);
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
