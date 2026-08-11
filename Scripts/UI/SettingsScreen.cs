namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// The options screen: an overlay over the main menu, in the same plain style.
/// Every switch takes effect the moment it is flipped and is written to
/// `user://settings.cfg` at once — there is no "apply" button to forget.
///
/// OURS, like <see cref="Settings"/> itself.
///
/// <para><b>Seit 10.08.2026 auch aus dem Pausenmenü.</b> Derselbe Schirm, kein
/// zweiter: <see cref="PauseMenu"/> hängt ihn als eigenes Kind ein und setzt
/// <see cref="InGame"/>. UNSERE SETZUNG ist dabei — neben dem Pausenmenü
/// selbst, das es 1997 nicht gibt —, dass im laufenden Spiel <b>dieselbe</b>
/// Auswahl gezeigt wird und nicht eine gekürzte: die Abstufungen (Lautstärken
/// in Schritten von 5 über 0..100, Bildratengrenze aus 0/60/120/144/240) und
/// welche Bildeinstellungen es überhaupt gibt (Vollbild, VSync, Bildratengrenze)
/// sind unsere Wahl und stehen unverändert. Was das laufende Spiel NICHT mehr
/// abholt, sagt der Schirm dazu, statt es wegzulassen — siehe die Zeile unter
/// dem Kartenlauf.</para>
/// </summary>
public partial class SettingsScreen : Control
{
    private static readonly int[] FpsChoices = { 0, 60, 120, 144, 240 };

    /// <summary>Aus dem Pausenmenü heraus geöffnet, also bei angehaltenem Spiel.
    /// Ändert nur zwei Dinge: die Rückkehr sagt, wohin sie führt, und der Schirm
    /// nennt die eine Einstellung, die erst beim nächsten Missionsstart greift —
    /// lieber ausgesprochen als stillschweigend wirkungslos.</summary>
    public bool InGame { get; init; }

    /// <summary>Prüfstand: das Rechteck der gemessenen Spalte. Ein Schirm unter
    /// einem CanvasLayer bekommt gern die Größe null; damit kann ein Test das
    /// prüfen, statt es auf einem Bild schätzen zu müssen.</summary>
    public Rect2 BoxRect => _box != null && IsInstanceValid(_box) ? _box.GetGlobalRect() : new Rect2();

    private VBoxContainer? _box;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        // ⚠ Ohne das nimmt der Schirm im Pausenmenü keine Eingabe an: das
        // Pausenmenü setzt GetTree().Paused = true, und ein Control, das dabei
        // nicht laufen darf, bekommt weder Klicks noch Tasten. (Als Kind des
        // Pausenmenüs würde Inherit hier zwar auch reichen — aber der Schirm
        // hängt nicht immer dort, und eine stumme Oberfläche ist ein Fehler, den
        // man erst im Spiel merkt.) Im Hauptmenü ist nichts angehalten, dort
        // ändert die Zeile nichts.
        ProcessMode = ProcessModeEnum.Always;
        var backdrop = new ColorRect { Color = new Color(0.04f, 0.05f, 0.07f, 0.96f) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        // The box used to hang off anchors at 0.5 under a plain Control. Nothing
        // measures a container there, so it kept a zero-size rect and the whole
        // screen was drawn from that point outwards — up and to the left of the
        // menu, which is where it was reported. Same frame the setup screen uses
        // (MainMenu._Ready): a full-rect scroller, a CenterContainer inside it,
        // the column inside that. It also survives a 720p window.
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var middle = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(middle);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        box.AddThemeConstantOverride("separation", 10);
        middle.AddChild(box);
        _box = box;

        var title = new Label
        {
            Text = "EINSTELLUNGEN",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        box.AddChild(title);
        if (InGame)
            box.AddChild(new Label
            {
                Text = "Das Spiel ist angehalten. Ton und Bild wirken sofort.\n"
                     + "ESC führt zurück ins Pausenmenü — nicht ins Gefecht.",
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = new Color(0.6f, 0.64f, 0.7f),
            });
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
        // Gemessen, nicht vermutet: MapViewer liest den Wert einmal beim Aufbau
        // in ein Feld (_keyPanSpeed, MapViewer._Ready) und danach nie wieder.
        // Ein Regler, der im laufenden Spiel nichts tut, muss das sagen.
        if (InGame)
            box.AddChild(new Label
            {
                Text = "  (wirkt erst beim nächsten Missionsstart)",
                Modulate = new Color(0.6f, 0.64f, 0.7f),
            });

        // FogOfWarSetting und nicht FogOfWar: solange das Demo hinter dem
        // Startmenü läuft, ist der Nebel unterdrückt (Settings.FogSuppressed),
        // und der Haken soll trotzdem zeigen, was eingestellt ist.
        box.AddChild(Check("Nebel des Krieges (im Spiel: Taste J)",
            Settings.FogOfWarSetting, v => Settings.FogOfWar = v));

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
            // hear what you set — a 0.29 s shot, not the 1.8 s spoken line the
            // old "Click" constant pointed at
            Audio.GameSounds.Play(Audio.GameSounds.VolumeProbe);
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
            // Ausschalten hielt die Musik schon immer an. Wiedereinschalten tat
            // bisher NICHTS — der Sequenzer war zu, und aufgemacht wird er nur
            // beim Missionsstart. Im Hauptmenü fiel das nicht auf, im laufenden
            // Spiel wäre es genau der Fall, den der Spieler meint.
            if (v) Audio.MidiMusic.Resume(); else Audio.MidiMusic.Stop();
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
        var back = new Button
        {
            Text = InGame ? "ZURUECK ZUM PAUSENMENUE" : "ZURUECK",
            CustomMinimumSize = new Vector2(0, 40),
        };
        back.Pressed += Close;
        box.AddChild(back);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            // Behandelt melden, damit die Taste nicht zusätzlich an das
            // Pausenmenü darunter geht — dort würde sie das Spiel fortsetzen,
            // und man wäre statt im Pausenmenü wieder im Gefecht.
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Weg mit dem Schirm. Aus dem Pausenmenü heraus kommt darunter das
    /// Pausenmenü wieder zum Vorschein — es stand die ganze Zeit da, nur
    /// verdeckt —, das Spiel bleibt angehalten.</summary>
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
