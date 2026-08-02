namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// The options screen: an overlay over the main menu, in the same plain style.
/// Every switch takes effect the moment it is flipped and is written to
/// `user://settings.cfg` at once — there is no "apply" button to forget.
///
/// OURS, like <see cref="Settings"/> itself.
/// </summary>
public partial class SettingsScreen : Control
{
    private static readonly int[] FpsChoices = { 0, 60, 120, 144, 240 };

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect
        {
            Color = new Color(0.04f, 0.05f, 0.07f, 0.96f),
            AnchorRight = 1, AnchorBottom = 1,
        });

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = GrowDirection.Both, GrowVertical = GrowDirection.Both,
            CustomMinimumSize = new Vector2(520, 0),
        };
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);

        var title = new Label
        {
            Text = "EINSTELLUNGEN",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        box.AddChild(title);
        box.AddChild(new HSeparator());

        box.AddChild(Head("Bild"));
        box.AddChild(Check("Vollbild", Settings.Fullscreen,
            v => { Settings.Fullscreen = v; Settings.Apply(); }));
        box.AddChild(Check("Bildsynchronisation (VSync)", Settings.VSync,
            v => { Settings.VSync = v; Settings.Apply(); }));

        var fps = new OptionButton();
        foreach (int f in FpsChoices) fps.AddItem(f == 0 ? "unbegrenzt" : $"{f} Bilder/s");
        fps.Selected = System.Math.Max(0, System.Array.IndexOf(FpsChoices, Settings.FpsLimit));
        fps.ItemSelected += i =>
        {
            Settings.FpsLimit = FpsChoices[Mathf.Clamp((int)i, 0, FpsChoices.Length - 1)];
            Settings.Apply();
        };
        box.AddChild(Row("Bildratengrenze", fps));

        box.AddChild(new HSeparator());
        box.AddChild(Head("Steuerung"));
        box.AddChild(Check("Rechte Maustaste gedrueckt halten schiebt die Karte",
            Settings.RightDragPan, v => Settings.RightDragPan = v));
        box.AddChild(Check("Zeiger zeigt an, was ein Klick tut",
            Settings.CursorHints, v => Settings.CursorHints = v));

        var pan = new HSlider
        {
            MinValue = 300, MaxValue = 2000, Step = 50, Value = Settings.PanSpeed,
            CustomMinimumSize = new Vector2(220, 0),
        };
        var panVal = new Label { Text = $"{Settings.PanSpeed}" };
        pan.ValueChanged += v => { Settings.PanSpeed = (int)v; panVal.Text = $"{(int)v}"; };
        var panRow = new HBoxContainer();
        panRow.AddChild(new Label { Text = "Tastatur-Kartenlauf", CustomMinimumSize = new Vector2(260, 0) });
        panRow.AddChild(pan);
        panRow.AddChild(panVal);
        box.AddChild(panRow);

        box.AddChild(new HSeparator());
        box.AddChild(Head("Ton"));
        box.AddChild(Check("Klaenge", Settings.SoundOn, v => Settings.SoundOn = v));

        var vol = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 5, Value = Settings.SfxVolume,
            CustomMinimumSize = new Vector2(220, 0),
        };
        var volVal = new Label { Text = $"{Settings.SfxVolume}" };
        vol.ValueChanged += v =>
        {
            Settings.SfxVolume = (int)v;
            volVal.Text = $"{(int)v}";
            Audio.GameSounds.Play(Audio.GameSounds.Click);   // hear what you set
        };
        var volRow = new HBoxContainer();
        volRow.AddChild(new Label { Text = "Lautstaerke", CustomMinimumSize = new Vector2(260, 0) });
        volRow.AddChild(vol);
        volRow.AddChild(volVal);
        box.AddChild(volRow);

        // "MIDI-Musik an/aus" is the original's own wording for this switch —
        // its help line sits in the table at 0x4f0280
        box.AddChild(Check("MIDI-Musik an/aus", Settings.MusicOn, v =>
        {
            Settings.MusicOn = v;
            if (!v) Audio.MidiMusic.Stop();
        }));

        var mvol = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 5, Value = Settings.MusicVolume,
            CustomMinimumSize = new Vector2(220, 0),
        };
        var mvolVal = new Label { Text = $"{Settings.MusicVolume}" };
        mvol.ValueChanged += v =>
        {
            Settings.MusicVolume = (int)v;
            mvolVal.Text = $"{(int)v}";
            Audio.MidiMusic.Volume((int)v);
        };
        var mvolRow = new HBoxContainer();
        mvolRow.AddChild(new Label { Text = "Musiklautstaerke", CustomMinimumSize = new Vector2(260, 0) });
        mvolRow.AddChild(mvol);
        mvolRow.AddChild(mvolVal);
        box.AddChild(mvolRow);

        // The two speech switches are the original's own, down to their names,
        // and both blocks of samples are identified. What is not identified is
        // which sample goes with which event, so the switches are shown with
        // that said out loud rather than hidden.
        box.AddChild(new Label
        {
            Text = "Gesprochenes: die Missions-Briefings werden vorgelesen (Klang 500 + Mission,\n"
                 + "gegen BRIEFG.TXT geprueft: r = 0,98 ueber alle 33). Die 104 Meldungen und die\n"
                 + "gesprochenen Hilfetexte sind abgeleitet, aber noch keinem Ereignis zugeordnet —\n"
                 + "die beiden Schalter tragen darum die Namen des Originals und warten darauf.",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.6f, 0.64f, 0.7f),
        });
        box.AddChild(Check("Meldungen (Original: byte[0x991708], Klaenge 150..253)",
            Settings.Announcements, v => Settings.Announcements = v));
        box.AddChild(Check("Hilfe-Sprache (Original: byte[0x8934c4], Klaenge 1000+n)",
            Settings.HelpVoice, v => Settings.HelpVoice = v));

        box.AddChild(new HSeparator());
        var back = new Button { Text = "ZURUECK", CustomMinimumSize = new Vector2(0, 40) };
        back.Pressed += Close;
        box.AddChild(back);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close() => QueueFree();

    private static Label Head(string t)
    {
        var l = new Label { Text = t, Modulate = new Color(0.75f, 0.8f, 0.86f) };
        l.AddThemeFontSizeOverride("font_size", 20);
        return l;
    }

    private static HBoxContainer Row(string label, Control c)
    {
        var h = new HBoxContainer();
        h.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(260, 0) });
        c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(c);
        return h;
    }

    private static CheckBox Check(string label, bool on, System.Action<bool> set)
    {
        var c = new CheckBox { Text = label, ButtonPressed = on };
        c.Toggled += v => set(v);
        return c;
    }
}
