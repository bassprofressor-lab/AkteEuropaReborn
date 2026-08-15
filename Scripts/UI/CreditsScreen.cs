namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// <b>Credits</b> — die Menüzeile des Originals (Platz 155, Hilfeindex 110,
/// »Credits zeigen« @0x4F0ABA), die bis zum 17.08.2026 nichts tat.
///
/// <para>⚠⚠ <b>DIE CREDITS DES ORIGINALS KÖNNEN WIR NICHT ZEIGEN, und das steht
/// hier statt einer Erfindung.</b> Gesucht wurde an drei Stellen:</para>
/// <list type="bullet">
/// <item>In GAME.EXE: das Wort »Credits« kommt <b>zweimal</b> vor, beide Male
/// als MENÜBESCHRIFTUNG (0xF0ABA »Credits zeigen«, 0x10030C »Credits«). Kein
/// Namensblock, keine Rollentexte.</item>
/// <item>Neben GAME.EXE: keine Datei, die danach aussieht (dort liegen
/// ENCYCLOG/HELPG/BRIEFG/OBJECTG, sonst Grafik und Klang).</item>
/// <item>Auf den CDs: <c>MOVIES\</c> trägt <b>34 Filme für 33 Missionen</b> —
/// 1..33 plus <c>34.RPL</c>, und <b>34.RPL liegt auf BEIDEN CDs</b>, während
/// die Missionsfilme sich aufteilen. Ein Film ausserhalb der Missionsreihe, den
/// beide Scheiben mitbringen: das ist der Kandidat für den Abspann. ⚠ <b>Das
/// ist ein Indiz, kein Beleg</b> — wir spielen kein .RPL, also ist ungeprüft,
/// was darin steht.</item>
/// </list>
///
/// <para>Was das Original SICHER ist, steht hier und ist belegt: Entwickler
/// <b>Virtual X-citement</b>, Publisher <b>Eidos Interactive</b>, 1997. Die
/// einzelnen Namen des Teams stehen in keiner Datei, die wir lesen — sie hier
/// aus dem Netz hinzuschreiben wäre eine Behauptung über Menschen, und dafür
/// gilt dieselbe Regel wie für jede andere: erst der Beleg, dann der Text.</para>
///
/// <para>Die Reborn-Seite ist dagegen bekannt und steht im Repository selbst
/// (README.de.md, packaging/AkteEuropaReborn.iss): <b>chr1zZo</b>.</para>
/// </summary>
public partial class CreditsScreen : Control
{
    /// <summary>Wer das Remake baut. ⚠ Aus dem Repository genommen
    /// (<c>AppCopyright</c> im Installerskript), nicht geraten.</summary>
    public const string RebornAuthor = "chr1zZo";

    public override void _Ready()
    {
        // ⚠ Anker UND Ränder — siehe LoadGameScreen._Ready, Fehler C20.
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

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(680, 0) };
        box.AddThemeConstantOverride("separation", 6);
        middle.AddChild(box);

        Head(box, "AKTE EUROPA", 30, new Color(0.90f, 0.80f, 0.35f));
        Head(box, "REBORN", 20, new Color(0.55f, 0.72f, 0.90f));
        Gap(box, 14);

        Head(box, "Das Original, 1997", 18, new Color(0.72f, 0.76f, 0.82f));
        Line(box, "Entwicklung", "Virtual X-citement");
        Line(box, "Herausgeber", "Eidos Interactive");
        Note(box,
            "Die Namen des Teams von 1997 stehen in keiner Datei, die diese\n" +
            "Fassung liest. Der Abspann des Originals ist vermutlich der Film\n" +
            "34.RPL — er liegt als einziger ausserhalb der 33 Missionsfilme\n" +
            "und auf BEIDEN CDs. Wir spielen kein .RPL, also steht hier nicht,\n" +
            "was darin zu sehen ist.");
        Gap(box, 14);

        Head(box, "Reborn", 18, new Color(0.72f, 0.76f, 0.82f));
        Line(box, "Reverse Engineering, Engine, Alles", RebornAuthor);
        Line(box, "Quelltext", "github.com/bassprofressor-lab/AkteEuropaReborn");
        Line(box, "Webseite", "openreborn.com");
        Line(box, "Lizenz", "GPL-3.0");
        Gap(box, 14);

        Note(box,
            "Ausgeliefert wird nur die Engine. Gelaende, Einheiten, Karten,\n" +
            "Tabellen und Klaenge entstehen auf dem eigenen Rechner aus der\n" +
            "eigenen Fassung des Spiels von 1997. Es werden keine Spieldateien\n" +
            "mitgeliefert.");
        Gap(box, 16);

        var close = new Button { Text = "Schliessen" };
        close.Pressed += QueueFree;
        box.AddChild(close);
    }

    private static void Head(Control box, string text, int size, Color col)
    {
        var l = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center, Modulate = col,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        box.AddChild(l);
    }

    private static void Line(Control box, string role, string who)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        var a = new Label
        {
            Text = role, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right,
            Modulate = new Color(0.62f, 0.64f, 0.66f),
        };
        var b = new Label
        {
            Text = who, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Modulate = new Color(0.92f, 0.91f, 0.86f),
        };
        row.AddChild(a);
        row.AddChild(b);
        box.AddChild(row);
    }

    private static void Note(Control box, string text)
    {
        box.AddChild(new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.55f, 0.56f, 0.58f),
        });
    }

    private static void Gap(Control box, int px)
        => box.AddChild(new Control { CustomMinimumSize = new Vector2(0, px) });

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            QueueFree();
            GetViewport().SetInputAsHandled();
        }
    }
}
