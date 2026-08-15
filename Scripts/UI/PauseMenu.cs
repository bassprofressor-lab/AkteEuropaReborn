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
        // ⚠ 17.08.2026 — siehe LoadGameScreen._Ready (Fehler C20): nur die Anker
        // zu setzen lässt die Ränder auf 0 stehen, das ColorRect blieb null
        // Pixel gross und hat nichts abgedunkelt.
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        dim.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(dim);

        var rows = new System.Collections.Generic.List<(string Text, Action Hit)>
        {
            ("Weiter", () => Resumed?.Invoke()),
            // UNSERE SETZUNG: die Zeile steht direkt unter "Weiter" und nicht am
            // Ende. Der Spieler öffnet dieses Menü im Gefecht meist genau deswegen
            // — Ton oder Bild passt nicht —, und "Beenden" soll die letzte Zeile
            // bleiben, damit nichts Gefährliches nach oben rutscht.
            ("Einstellungen", OpenSettings),
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

        ReadCheckArgs();
    }

    // ---- Einstellungen aus dem laufenden Spiel --------------------------------

    /// <summary>Der Einstellungsschirm, solange er offen ist.</summary>
    private SettingsScreen? _settings;

    /// <summary>Steht er wirklich noch? <c>QueueFree</c> lässt das Feld sonst auf
    /// ein totes Objekt zeigen.</summary>
    private bool SettingsOpen =>
        _settings != null && IsInstanceValid(_settings) && _settings.IsInsideTree();

    /// <summary>
    /// Zeigt <see cref="SettingsScreen"/> über dem Pausenfenster.
    ///
    /// <para>⚠ Er hängt als KIND dieses Schirms und nicht am CanvasLayer darüber.
    /// Ein Control unter einem CanvasLayer bekommt vom Layout die Größe NULL —
    /// genau daran hing das Pausenmenü am 08.08. in der linken oberen Ecke. Dieses
    /// Control hier setzt seine Größe in <see cref="_Ready"/> selbst auf die des
    /// Viewports, also greift das Vollbild-Preset des Einstellungsschirms darunter
    /// und sein <c>CenterContainer</c> misst gegen eine echte Fläche.</para>
    ///
    /// <para>Das Pausenfenster bleibt darunter stehen und wird nur verdeckt (der
    /// Einstellungsschirm hat einen fast blickdichten Hintergrund und schluckt
    /// Mausereignisse). Beim Schließen ist es deshalb sofort wieder da, ohne dass
    /// etwas neu gebaut oder ein Zustand wiederhergestellt werden müsste.</para>
    /// </summary>
    private void OpenSettings()
    {
        if (SettingsOpen) return;
        _settings = new SettingsScreen { InGame = true };
        AddChild(_settings);
    }

    /// <summary>ESC again closes it, like every other screen in this game.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        // Liegt der Einstellungsschirm oben, gehört ESC ihm: er schließt sich und
        // man steht wieder im Pausenmenü, das Spiel läuft NICHT weiter. Er meldet
        // die Taste zwar selbst als behandelt (und kommt in der Baumreihenfolge
        // auch vor uns dran), aber ein Schirm, der das Spiel fortsetzt, weil eine
        // Taste einmal durchrutscht, wäre ein teurer Fehler — darum zusätzlich
        // dieser Riegel.
        if (SettingsOpen) return;

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Resumed?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---- Prüfstand ------------------------------------------------------------
    //
    // Ein Bild des GESCHLOSSENEN Menüs beweist nichts, und ob eine Änderung im
    // laufenden Spiel wirklich ankommt, kann man nicht behaupten — man muss es
    // messen. Zwei eigene Schalter, nach dem Vorbild von --script-check:
    //   --pause-settings   klappt den Einstellungsschirm sofort auf (für --shot)
    //   --settings-check   misst und schreibt das Ergebnis, dann Ende
    // Beide setzen --pause voraus, das MapViewer schon mitbringt.

    private bool _check;
    private int _frames;
    private Godot.AudioStreamPlayer? _probeA, _probeB;
    private int _oldSfx, _oldMusic, _oldFps;
    private bool _oldVsync;

    private void ReadCheckArgs()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--pause-settings") OpenSettings();
            else if (a == "--settings-check") { _check = true; OpenSettings(); }
        }
    }

    public override void _Process(double delta)
    {
        if (!_check) return;
        _frames++;
        if (_frames == 2) CheckStart();
        // ESC durch die normale Eingabekette schicken — nicht Close() aufrufen.
        // Die Frage ist ja gerade, wer die Taste bekommt.
        else if (_frames == 10)
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        else if (_frames == 22) CheckEnd();
    }

    private void CheckStart()
    {
        _oldSfx = Settings.SfxVolume;
        _oldMusic = Settings.MusicVolume;
        _oldFps = Settings.FpsLimit;
        _oldVsync = Settings.VSync;

        GD.Print("== Einstellungen im Pausenmenü — Prüfstand ==");
        GD.Print($"Baum angehalten:            {GetTree().Paused}");
        GD.Print($"Pausenmenü darf laufen:     {CanProcess()}  (ProcessMode {ProcessMode})");
        GD.Print($"Einstellungsschirm im Baum: {SettingsOpen}");
        if (_settings != null)
            GD.Print($"  darf laufen:              {_settings.CanProcess()}  (ProcessMode {_settings.ProcessMode})");
        GD.Print($"  Rechteck:                 {_settings?.GetGlobalRect()}   Viewport {GetViewportRect().Size}");
        GD.Print($"  gemessene Spalte:         {_settings?.BoxRect}");

        // Ton: derselbe Weg wie jeder Spielklang (SoundBankPlayer-Kanalpool), nur
        // mit erzeugtem Ton, weil in diesem Baum kein Inhalt importiert ist.
        _probeA = Audio.SoundBankPlayer.Probe();
        GD.Print($"Klang bei SfxVolume={Settings.SfxVolume}: "
               + $"VolumeDb={_probeA?.VolumeDb:F2} (Settings.SfxVolumeDb={Settings.SfxVolumeDb:F2})");
        GD.Print($"  Kanal darf laufen:        {_probeA?.CanProcess()}  (ProcessMode {_probeA?.ProcessMode})");

        // Lautstärke MITTEN im angehaltenen Spiel ändern und den nächsten Klang
        // fragen, welchen Pegel er bekommt.
        Settings.SfxVolume = 20;
        _probeB = Audio.SoundBankPlayer.Probe();
        GD.Print($"Klang nach Änderung auf 20: VolumeDb={_probeB?.VolumeDb:F2} "
               + $"(Settings.SfxVolumeDb={Settings.SfxVolumeDb:F2})");

        // Musik: MCI liegt außerhalb des Szenenbaums, eine Pause erreicht sie
        // nicht. Ohne .mid im Inhaltsordner ist nichts offen — dann sagt es das.
        Settings.MusicVolume = 35;
        Audio.MidiMusic.Volume(35);
        GD.Print($"Musik: Available={Audio.MidiMusic.Available} Track={Audio.MidiMusic.Track}");
        GD.Print($"  MCI setaudio:             {Audio.MidiMusic.MciVolumeCode} \"{Audio.MidiMusic.LastError}\"");
        GD.Print($"  midiOutSetVolume:         {Audio.MidiMusic.VolumeCode} (0 = angenommen)");

        // "MIDI-Musik an/aus" aus und wieder an, mitten im Spiel.
        Audio.MidiMusic.Stop();
        int off = Audio.MidiMusic.Track;
        bool back = Audio.MidiMusic.Resume();
        GD.Print($"  aus -> Track={off}; wieder an -> {back}, Track={Audio.MidiMusic.Track}");

        // Bild: Settings.Apply() redet direkt mit dem DisplayServer.
        Settings.VSync = !_oldVsync;
        Settings.FpsLimit = 144;
        Settings.Apply();
        GD.Print($"Bild: VSync gesetzt auf {!_oldVsync} -> DisplayServer meldet "
               + $"{DisplayServer.WindowGetVsyncMode()}");
        GD.Print($"Bild: Bildratengrenze 144 -> Engine.MaxFps = {Engine.MaxFps}");
    }

    private void CheckEnd()
    {
        // Läuft ein Klang bei angehaltenem Baum weiter? Wenn die Abspielposition
        // nach 20 Bildern noch bei 0 steht, ist er eingefroren.
        GD.Print($"Klang nach 20 Bildern bei angehaltenem Spiel: "
               + $"Position {_probeA?.GetPlaybackPosition():F3} s, Playing={_probeA?.Playing}, "
               + $"StreamPaused={_probeA?.StreamPaused}");

        // Requirement: ESC im Einstellungsschirm führt ins Pausenmenü zurück und
        // NICHT ins Spiel. Also: Schirm weg, Pausenmenü noch da, Baum noch an.
        GD.Print($"Nach ESC im Einstellungsschirm: Schirm offen={SettingsOpen}, "
               + $"Pausenmenü im Baum={IsInsideTree()}, Baum angehalten={GetTree().Paused}");

        Settings.SfxVolume = _oldSfx;
        Settings.MusicVolume = _oldMusic;
        Settings.FpsLimit = _oldFps;
        Settings.VSync = _oldVsync;
        Settings.Apply();
        GD.Print("Einstellungen zurückgesetzt.");
        GetTree().Quit(0);
    }
}
