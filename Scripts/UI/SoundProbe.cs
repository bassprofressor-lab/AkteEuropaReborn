namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Audio;

/// <summary>
/// Every sound the bank holds, in a list you can click through.
///
/// This is the bench the mapping is done on: 492 sounds is far more than the
/// dozen the game's own call sites name, and the rest can only be identified by
/// listening. The list therefore shows what is KNOWN about each — its slot
/// number, its length, whether the original preloads it, and the event it was
/// traced to, with the address that says so.
///
/// Reached with <c>--sound-probe</c>.
/// </summary>
public partial class SoundProbe : Control
{
    /// <summary>What the disassembly settled. Everything else is listed as
    /// unknown rather than given a plausible name.</summary>
    private static readonly Dictionary<int, string> Named = new()
    {
        [0] = "Rakete? — @0x452a1a, Modus 3, im Raketenmodul",
        [37] = "Ziel gefunden — @0x411ab0 (\"Check seeker\")",
        [59] = "Spiegelung ein — @0x43a9b9, in @0x43a978",
        [70] = "Flugzeug explodiert — @0x425f0f (\"axplode air\")",
        [114] = "Bomber schiesst — @0x427416 (\"bomber shoot\")",
        [128] = "Abbau — @0x43e6cd (\"mining 3\")",
        [129] = "Aufwertung — @0x43e837 (\"upgrading\")",
        [130] = "Erweiterung — @0x43e794 (\"enlarging\")",
        [131] = "Infanterie zerstoert — @0x40d37c (\"Hit to exploding infantry!!!\")",
        [132] = "Gebaeude wird besetzt — @0x43cc73 (\"Ihre Basis wird besetzt\")",
        [136] = "Forschung gemeldet — @0x4ab41b (\"Nachricht des FORSCHUNGSLABORS:\")",
        [138] = "Flugzeug explodiert, zweiter Teil — @0x425f53",
        [140] = "Abgewiesen — @0x44b6e9 (\"nicht genuegend Einzelteile\")",
        [141] = "Abgewiesen — @0x44b8ad (\"Kann nicht starten.\")",
        [306] = "Briefing auf — @0x44d8b9 (\"End of briefing starts\")",
        [350] = "Briefing zu — @0x44d976 (\"End of briefing\")",
        [400] = "Explosion, zweiter Teil — @0x454536 (mit 410 zusammen)",
        [410] = "Explosion — @0x454525, an einer Kartenposition",
        [600] = "Oberflaechenklick — @0x487c00, aus 89 Stellen gerufen",
        [601] = "Oberflaeche, zweiter — @0x487c20, aus 62 Stellen gerufen",
    };

    /// <summary>What a whole block is, where the block could be pinned down but
    /// its single entries could not. Shown behind the number so the list reads
    /// as three quarters known instead of three quarters blank.</summary>
    private static string BlockOf(int i) => i switch
    {
        >= 0 and <= 39 => "Schuss (20 Waffenklassen à 2)",
        >= 150 and <= 253 => "Meldung (Schalter byte[0x991708])",
        >= 501 and <= 533 => "Briefing Mission " + (i - 500) + " (r=0,98 gegen BRIEFG.TXT)",
        >= 1001 => "Hilfe-Sprache, Text " + (i - 1000) + " (Schalter byte[0x8934c4])",
        _ => "",
    };

    private Label _status = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new Color(0.05f, 0.06f, 0.08f), AnchorRight = 1, AnchorBottom = 1 });

        SoundBankPlayer.Load();
        var all = new List<SoundBankPlayer.Entry>(SoundBankPlayer.Index.Values);
        all.Sort((a, b) => a.Index.CompareTo(b.Index));

        var box = new VBoxContainer();
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);

        int pre = 0;
        double secs = 0;
        foreach (var e in all) { if (e.Preloaded) pre++; secs += e.Seconds; }

        var head = new Label
        {
            Text = $"Klangbank — {all.Count} Klaenge, {pre} vorgeladen / {all.Count - pre} bei Bedarf, " +
                   $"{secs / 60:0.0} Minuten, {Import.SoundBank.SampleRate} Hz / {Import.SoundBank.Bits} Bit mono",
        };
        head.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(head);

        _status = new Label { Text = "Klick spielt. Esc schliesst.", Modulate = new Color(0.6f, 0.7f, 0.8f) };
        box.AddChild(_status);

        var music = new HBoxContainer();
        music.AddChild(new Label { Text = "Musik:  " });
        for (int t = 0; t <= 5; t++)
        {
            int track = t;
            var mb = new Button { Text = $"{t}.mid" };
            mb.Pressed += () =>
            {
                bool ok = MidiMusic.Play(track);
                _status.Text = ok
                    ? $"Musik {track}.mid laeuft (MCI-Rueckgabe 0)"
                    : $"Musik {track}.mid: {MidiMusic.LastError} (MCI {MidiMusic.LastCode})";
            };
            music.AddChild(mb);
        }
        var stop = new Button { Text = "Stopp" };
        stop.Pressed += () => { MidiMusic.Stop(); _status.Text = "Musik gestoppt"; };
        music.AddChild(stop);
        box.AddChild(music);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);

        foreach (var e in all)
        {
            var row = new HBoxContainer();
            var b = new Button { Text = $"{e.Index:0000}", CustomMinimumSize = new Vector2(70, 0) };
            var entry = e;
            b.Pressed += () =>
            {
                SoundBankPlayer.Play(entry.Index);
                _status.Text = $"Klang {entry.Index}: {entry.Bytes} Bytes, " +
                               $"{entry.Seconds:0.00} s, " +
                               (entry.Preloaded ? "vorgeladen" : "bei Bedarf") +
                               (Named.TryGetValue(entry.Index, out var n) ? " — " + n : "");
            };
            row.AddChild(b);
            string what = Named.TryGetValue(entry.Index, out var name) ? name : BlockOf(entry.Index);
            row.AddChild(new Label
            {
                Text = $"{entry.Seconds,6:0.00} s   " + (entry.Preloaded ? "vorgeladen" : "bei Bedarf") +
                       "   " + what,
                Modulate = Named.ContainsKey(entry.Index) ? new Color(0.85f, 0.9f, 0.7f)
                         : what.Length > 0 ? new Color(0.72f, 0.78f, 0.72f)
                                           : new Color(0.6f, 0.63f, 0.68f),
            });
            list.AddChild(row);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            MidiMusic.Stop();
            GetTree().Quit();
        }
    }
}
