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
    /// check them against the file.
    ///
    /// <para>Nachgerechnet am 11.08.2026: die Datei ist 776 statt 768 Bytes
    /// lang, hat also einen <b>8 Byte langen Kopf</b> (<c>08 03 00 00 23 b1 00
    /// 00</c>) — Farbe i steht auf 8 + 3·i. Erst damit kommen die vier Werte
    /// oben heraus; ohne den Kopf gerechnet ergibt 0x28 (163,163,163). Die
    /// Zahlen hier stimmten also, die Rechnung dahinter steht jetzt auch
    /// da.</para></summary>
    internal static Color Pal(int i) => i switch
    {
        0x08 => Color.Color8(43, 143, 11),
        0x24 => Color.Color8(187, 179, 179),
        0x28 => Color.Color8(127, 115, 115),
        0x29 => Color.Color8(111, 99, 99),
        0x2a => Color.Color8(91, 83, 83),
        0x2c => Color.Color8(55, 47, 47),
        0x2f => Color.Color8(19, 19, 15),
        // die beiden Farben der Titelleiste, aus demselben 01.PAL — der
        // Zeichenaufruf @0x480430 schiebt sie als 0x96 und 0xa9 mit
        0x96 => Color.Color8(244, 184, 28),
        0xa9 => Color.Color8(171, 135, 31),
        0xfe => Color.Color8(235, 231, 231),
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

    /// <summary>Eine Zeile umbeschriften, ohne ihren Platz, ihre Reihenfolge
    /// oder ihren Hilfeindex anzutasten.
    ///
    /// <para>⚠ <b>Jede Verwendung ist eine UNSERE SETZUNG</b>, denn die neun
    /// Beschriftungen in <see cref="Original"/> sind aus den Zeichenaufrufen
    /// 0x480454..0x4805a8 gelesen. Deshalb bleibt der Hilfeindex stehen: er
    /// zeigt weiter auf den Eintrag des Originals, aus dem diese Zeile
    /// stammt, und die Herkunft geht nicht verloren.</para>
    ///
    /// <para>Gebraucht wird das genau einmal, fuer <i>Neues Spiel</i> →
    /// <i>Kampagne</i>: die Zeile startet nicht mehr wortlos die naechste
    /// Mission, sondern oeffnet die Missionsuebersicht
    /// (<see cref="CampaignScreen"/>), und »Neues Spiel« waere fuer einen
    /// Bildschirm, auf dem man auch eine alte Mission wiederholt, der falsche
    /// Name.</para></summary>
    public static List<Row> Recaption(IEnumerable<Row> rows, string caption,
                                      string to, string help)
    {
        var list = new List<Row>(rows);
        int at = list.FindIndex(r => r.Caption == caption);
        if (at >= 0) list[at] = list[at] with { Caption = to, Help = help };
        return list;
    }

    /// <summary>Eine Zeile ganz herausnehmen und alles darunter um eine
    /// Zeilenhoehe hochziehen — das Gegenstueck zu <see cref="InsertAfter"/>.
    ///
    /// <para>⚠ <b>Jede Verwendung ist UNSERE SETZUNG</b>, und zwar eine
    /// staerkere als <see cref="Recaption"/>: dort bleibt die Zeile des
    /// Originals mit ihrem Hilfeindex stehen und heisst nur anders, hier
    /// verschwindet sie. Was das Original an dieser Stelle hatte, steht dann nur
    /// noch in <see cref="Original"/> — deshalb wird dort NICHTS gestrichen, und
    /// deshalb gehoert jeder Aufruf hier begruendet.</para>
    ///
    /// <para>⚠ <b>Verschoben wird um den EIGENEN Platz der Zeile, nicht um
    /// <see cref="EntryH"/></b> — und das ist am Bild korrigiert. Die neun
    /// Y-Werte des Originals sind NICHT gleichmaessig: vor »Enzyklopaedie«,
    /// »Intro ansehen« und »Beenden« stehen 25 statt 20, damit gruppiert das
    /// Original. Der erste Anlauf zog um die feste Zeilenhoehe 20 hoch, und im
    /// Bildschirmfoto stand danach zwischen »Enzyklopaedie« und »Credits« ein
    /// Abstand von 25 — eine Gruppengrenze, die das Original an dieser Stelle
    /// nicht hat. Ich hatte im Kommentar behauptet, das sei nicht zu sehen; es
    /// war zu sehen. Mit dem Abstand zum VORGAENGER verschwindet der Platz der
    /// Zeile ganz, samt ihres Vorabstands, und die Gruppierung darunter bleibt
    /// die des Originals.</para></summary>
    public static List<Row> Without(IEnumerable<Row> rows, string caption)
    {
        var list = new List<Row>(rows);
        int at = list.FindIndex(r => r.Caption == caption);
        if (at < 0) return list;

        // Der Platz, den diese Zeile einnimmt: ihr Abstand zum Vorgaenger. Fuer
        // die erste Zeile gibt es keinen — dann bleibt es bei der Zeilenhoehe.
        int gone = at > 0 ? list[at].Y - list[at - 1].Y : EntryH;

        list.RemoveAt(at);
        for (int i = at; i < list.Count; i++)
            list[i] = list[i] with { Y = list[i].Y - gone };
        return list;
    }

    /// <summary>Shown under the list — ours, because the original's help line
    /// lives in its side panel, which this screen has not got.</summary>
    public string Footer = "";

    /// <summary>Was der X-Knopf tun soll. ⚠ UNSERE SETZUNG, siehe
    /// <see cref="BuildTitleBar"/> — im Original ist nicht gelesen, wohin er
    /// führt. Bleibt er ungesetzt, wird kein X gezeichnet.</summary>
    public Action? Close;

    /// <summary>Die Titelleiste des Menüfensters — <b>gelesen</b>.
    ///
    /// <para>Dieselbe Zeichenroutine, aus der <see cref="Original"/> seine neun
    /// Zeilen hat, schreibt vor ihnen den Fenstertitel: @0x4803e4 lädt
    /// <c>0x4f7538</c> = <b>"Akte Europa"</b> in einen Puffer, und der Aufruf
    /// @0x480430 setzt ihn mit den Argumenten <c>(0xa, 2, text, fenster, 0x96,
    /// 0xa9)</c> ab — also auf <b>x = 10, y = 2</b> in Fensterkoordinaten, in
    /// den Palettenfarben 0x96 (244,184,28) und 0xa9 (171,135,31). Zum
    /// Vergleich die Zeile darunter @0x480454: <c>(0x14, 0x19, 8, …)</c> —
    /// x = 20, y = 25, das ist <see cref="EntryX"/> und die erste Zeile. Beide
    /// Zahlenpaare kommen aus demselben Aufrufmuster, das Fenster ist also
    /// wirklich so gebaut.</para>
    ///
    /// <para><b>⚠ UNSERE SETZUNGEN hier:</b> die Höhe der Leiste (22 — gelesen
    /// ist nur, dass der Titel auf y=2 sitzt und die erste Zeile auf y=25),
    /// ihr dunklerer Grund (0x2c derselben Fensterrampe), und der X-Knopf:
    /// dass er da ist, steht auf dem Bildschirmfoto des Originals; was er tut,
    /// ist nicht gelesen. Er ist hier auf »Beenden« gelegt, weil das die
    /// einzige Deutung ist, die niemanden in eine Sackgasse führt.</para>
    ///
    /// <para><b>Und das Wort REBORN ist UNSERES</b> — 1997 stand da nur »Akte
    /// Europa«. Es steht deshalb hinter dem Originaltitel, in einer anderen
    /// Farbe, durch einen Punkt abgesetzt: was das Original sagte, sagt es
    /// weiter, und was wir hinzufügen, ist als Zusatz zu erkennen. Gezeichnet
    /// ist nichts — es ist dieselbe FONT.CWD, die auch die Zeilen setzt.</para>
    /// </summary>
    private void BuildTitleBar(Panel panel, int w)
    {
        const int BarH = 22;                       // ⚠ unsere Setzung

        var bar = new ColorRect
        {
            Color = Pal(0x2c),
            Position = new Vector2(0, 0),
            Size = new Vector2(w, BarH * Scale),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        panel.AddChild(bar);
        // eine Kante nach unten, damit die Leiste vom Fenster abgesetzt ist
        panel.AddChild(new ColorRect
        {
            Color = Pal(0x28),
            Position = new Vector2(0, BarH * Scale),
            Size = new Vector2(w, Scale),
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var title = new Label
        {
            Text = "Akte Europa",
            Position = new Vector2(10 * Scale, 2 * Scale),     // gelesen: (0xa, 2)
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeColorOverride("font_color", Pal(0x96));   // gelesen: 0x96
        StyleLegacy(title);
        panel.AddChild(title);

        // UNSERE Zutat, und sie sagt es durch ihre Stellung und ihre Farbe.
        // Der Platz dahinter wird GEMESSEN und nicht geschätzt — beim ersten
        // Versuch stand hier eine feste Zahl, und "REBORN" lag quer über dem
        // "Europa".
        float after = 10 * Scale + (_font != null
            ? _font.GetStringSize(title.Text, HorizontalAlignment.Left, -1, 13 * Scale).X
            : 68 * Scale);
        var reborn = new Label
        {
            Text = "· REBORN",
            Position = new Vector2(after + 8 * Scale, 2 * Scale),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        reborn.AddThemeColorOverride("font_color", Color.Color8(150, 205, 235));
        StyleLegacy(reborn);
        panel.AddChild(reborn);

        if (Close == null) return;
        // Der X-Knopf. Rot wie auf dem Foto, aus derselben Palette: 0x08 ist
        // dort (43,143,11) und damit grün — das Rot des Fotos steht in keiner
        // gelesenen Farbstelle, also ist dieser Ton UNSERER.
        var x = new Panel
        {
            Position = new Vector2((PanelW - 17) * Scale, 4 * Scale),
            Size = new Vector2(13 * Scale, 13 * Scale),
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "Beenden — ⚠ unsere Deutung des X, das Original ist dazu ungelesen",
        };
        x.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.Color8(168, 44, 36),
            BorderColor = Pal(0x2f),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        var xl = new Label
        {
            Text = "x",
            Size = new Vector2(13 * Scale, 13 * Scale),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        xl.AddThemeColorOverride("font_color", Pal(0x2f));
        StyleLegacy(xl);
        x.AddChild(xl);
        var close = Close;
        x.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) close();
        };
        panel.AddChild(x);
    }

    /// <summary>Der Kopf über dem Fenster. <b>UNSERER, ganz und gar</b> — das
    /// Startbild von 1997 hat ihn nicht, dort steht das Fenster allein auf dem
    /// laufenden Demo. Er ist hier, weil der Spieler auf einen Blick sehen
    /// soll, dass dies die Neufassung ist und nicht das Original.
    ///
    /// <para>Gezeichnet wird trotzdem nichts Fremdes: die Buchstaben kommen aus
    /// FONT2.CWD, der zweiten Schrift des Spiels, die schon die Briefings
    /// setzt, und die Linien sind zwei Rechtecke aus Godot. Kein
    /// Originalbild wird angefasst, unsere Auslieferung trägt weiter keine
    /// Originalinhalte.</para></summary>
    /// <summary>
    /// <b>EIN UMRISS STATT EINES SCHLEIERS.</b>
    ///
    /// <para>⚠ 18.08.2026, gemeldet als »in der Demo haben die Gebäude die
    /// helleren Bodenmuster, aber die Umgebung wirkt wie dunkler. Das ist im
    /// Gefecht nicht so oder in der Kampagne.«</para>
    ///
    /// <para>Der Bericht trifft: <see cref="MenuBackdrop"/> legte <b>35 %
    /// Schwarz über das ganze Bild</b>. Der Grund war gut — dieselbe
    /// Bitmapschrift verschwindet auf hellem Schnee —, der Preis aber viel zu
    /// hoch: die Kulisse wurde für <b>vier</b> freistehende Beschriftungen
    /// abgedunkelt (Titel, Untertitel, Fußzeile, Versionsnummer). Alles andere
    /// sitzt im deckenden Kasten und hätte den Schleier nie gebraucht.</para>
    ///
    /// <para>Ein schwarzer Umriss löst dasselbe Problem an der Stelle, an der
    /// es auftritt. Die Kulisse ist damit so hell wie Gefecht und Kampagne —
    /// und die zeigen dieselben Karten mit denselben Gebäudeböden.</para>
    ///
    /// <para>⚠ Unsere Zutat, wie der Schleier vorher: das Original stellt sein
    /// Menüfenster ohne beides aufs Bild, weil sein Fenster halb so groß ist und
    /// weniger Schrift daneben steht.</para></summary>
    /// <param name="dick">Wie breit der Umriss ist. Der TITEL braucht mehr: er
    /// steht in der Palettenfarbe des Originals (ein gedecktes Ocker), und die
    /// hat auf hellem Schnee von sich aus kaum Abstand. Die Fusszeilen sind
    /// hell und kommen mit weniger aus.</param>
    private static void Umriss(Label l, int dick = 6)
    {
        l.AddThemeConstantOverride("outline_size", dick);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.92f));
    }

    /// <summary>
    /// <b>Ein Schatten, der auch unter einer BITMAPSCHRIFT liegt.</b>
    ///
    /// <para>⚠ <see cref="Umriss"/> tut bei der Titelschrift NICHTS: sie ist die
    /// Bitmapschrift des Originals, und <c>outline_size</c> gilt nur für
    /// Schriften, die einen Umriss zeichnen können. Der Aufruf lief wirkungslos
    /// durch — aufgefallen ist es am Bildschirmfoto, nicht am Übersetzer. Genau
    /// die Sorte Schalter, die man für gesetzt hält.</para>
    ///
    /// <para>Hier steht dieselbe Beschriftung noch einmal in Schwarz, zwei
    /// Punkte nach rechts unten. Das wirkt mit jeder Schrift, weil es keine
    /// Eigenschaft der Schrift benutzt.</para></summary>
    private static Label Schatten(Label l)
    {
        var sch = new Label
        {
            Text = l.Text,
            HorizontalAlignment = l.HorizontalAlignment,
            AnchorLeft = l.AnchorLeft, AnchorRight = l.AnchorRight,
            AnchorTop = l.AnchorTop, AnchorBottom = l.AnchorBottom,
            OffsetLeft = l.OffsetLeft + 2, OffsetRight = l.OffsetRight + 2,
            OffsetTop = l.OffsetTop + 2, OffsetBottom = l.OffsetBottom + 2,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        sch.AddThemeColorOverride("font_color", new Color(0, 0, 0, 0.75f));
        var f = l.GetThemeFont("font");
        if (f != null) sch.AddThemeFontOverride("font", f);
        sch.AddThemeFontSizeOverride("font_size", l.GetThemeFontSize("font_size"));
        return sch;
    }

    private void BuildRebornHead(int h)
    {
        var font2 = BriefingScreen.LegacyFont(second: true);

        var wordmark = new Label
        {
            Text = "AKTE EUROPA",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetTop = -h / 2 - 78, OffsetBottom = -h / 2 - 40,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        wordmark.AddThemeColorOverride("font_color", Pal(0x24));
        if (font2 != null) wordmark.AddThemeFontOverride("font", font2);
        wordmark.AddThemeFontSizeOverride("font_size", 13 * (Scale + 1));
        // ⚠ KEIN Umriss hier: der Titel steht in der BITMAPschrift des
        // Originals, und die kennt `outline_size` nicht — der Aufruf lief
        // wirkungslos durch. Am Bildschirmfoto gesehen, nicht vermutet.
        // Ein doppelt gesetzter Schatten wirkt dagegen mit jeder Schrift.
        AddChild(Schatten(wordmark));
        AddChild(wordmark);

        var sub = new Label
        {
            Text = "R E B O R N",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetTop = -h / 2 - 38, OffsetBottom = -h / 2 - 12,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        sub.AddThemeColorOverride("font_color", Pal(0x96));
        if (font2 != null) sub.AddThemeFontOverride("font", font2);
        sub.AddThemeFontSizeOverride("font_size", 13 * Scale);
        AddChild(Schatten(sub));
        AddChild(sub);

        // zwei Goldstriche links und rechts von REBORN, damit die Zeile als
        // Zusatz und nicht als zweiter Titel liest
        foreach (int side in new[] { -1, 1 })
            AddChild(new ColorRect
            {
                Color = Pal(0xa9),
                AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
                OffsetLeft = side < 0 ? -150 : 76, OffsetRight = side < 0 ? -76 : 150,
                OffsetTop = -h / 2 - 26, OffsetBottom = -h / 2 - 24,
                MouseFilter = MouseFilterEnum.Ignore,
            });
    }

    /// <summary>Die Spielschrift auf ein Etikett legen, in derselben Größe wie
    /// die Menüzeilen. Ohne die Schrift bleibt Godots eigene stehen, nur
    /// kleiner gesetzt — ein fehlender Import soll das Menü nicht kosten.</summary>
    private void StyleLegacy(Label l)
    {
        if (_font != null)
        {
            l.AddThemeFontOverride("font", _font);
            l.AddThemeFontSizeOverride("font_size", 13 * Scale);
        }
        else l.AddThemeFontSizeOverride("font_size", 9 * Scale);
    }

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

        BuildTitleBar(panel, w);
        BuildRebornHead(h);

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
        Umriss(_status);
        AddChild(_status);

        // The version, bottom right under the window. OURS entirely — the 1997
        // menu shows no version anywhere. It reads `application/config/version`
        // out of project.godot.
        //
        // ⚠⚠ 21.08.2026 — HIER STAND EINE FALSCHE BERUHIGUNG: »which is the
        // same string the installer script carries as AppVersion, so there is
        // one place to change it«. Es sind VIER Stellen, und sie sind
        // auseinandergelaufen: beim Sprung auf 0.7.0 wurden AppVersion in der
        // .iss und file_version/product_version in export_presets.cfg gesetzt —
        // und ausgerechnet DIESE hier vergessen, also die EINZIGE, die der
        // Spieler zu sehen bekommt. Gemeldet mit »im Menü steht immer noch die
        // 0.6.0«.
        //
        // Ein Kommentar, der »es gibt nur eine Stelle« behauptet, ist schlimmer
        // als gar keiner: er hält den Nächsten davon ab nachzusehen. Die vier
        // Stellen werden jetzt von `packaging/pruefe_version.py` gegeneinander
        // geprüft — eine Zusicherung, die nachrechnet, statt einer, die es
        // behauptet.
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
        Umriss(ver);
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
