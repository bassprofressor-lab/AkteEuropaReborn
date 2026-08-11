namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Die Missionsuebersicht der Kampagne: alle Missionen auf einen Blick, die
/// geschafften wieder waehlbar, die gesperrten schattiert.
///
/// <para>⚠ <b>UNSERE ZUTAT, ganz und gar.</b> Das Startmenue von 1997 hat keine
/// solche Uebersicht. Seine Liste kennt genau zwei Wege in ein Spiel — »Neues
/// Spiel« (Hilfeindex 104, »Neues Spiel starten«) und »Spiel laden« (105) —,
/// und »Neues Spiel« beginnt ohne Rueckfrage bei dem Stand, den der
/// Kampagnenzaehler <c>word[0x539934]</c> gerade hat. Es gibt im Original
/// keinen Bildschirm, der 33 Missionen nebeneinander zeigt, und keine
/// Kachelgrafik, aus der man einen bauen koennte. Alles hier ist erfunden:
/// die Anordnung, die drei Zustaende, die Rahmen, die Beschriftungen.</para>
///
/// <para><b>Was NICHT erfunden ist</b>, damit sich die Seite einfuegt: die
/// Farben sind die Fensterrampe aus DATA/01.PAL, die schon
/// <see cref="StartMenuPanel"/> benutzt (0x28/0x2a/0x2c/0x2f, Gold 0x96/0xa9),
/// die Schrift ist FONT.CWD als BMFont, und die Missionsnamen und ihre
/// Reihenfolge stammen aus <c>user://data/Maps/campaign.json</c>, das beim
/// Einlesen aus den Levelsaetzen selbst entsteht.</para>
///
/// <para><b>Die drei Zustaende</b> — so gewuenscht, mit einer notwendigen
/// Ergaenzung:</para>
/// <list type="bullet">
/// <item><b>Geschafft</b> (<c>Index &lt;= Completed</c>): waehlbar, mit
/// goldenem Rahmen. Wortwoertlich der Wunsch »die, die man schon erfolgreich
/// abgeschlossen hat, sind dann wie waehlbar (aktiv rahmen)«.</item>
/// <item><b>Als naechstes dran</b> (die erste noch nicht geschaffte, also
/// <see cref="Campaign.CampaignManager.Next"/>): ebenfalls waehlbar, mit hellem
/// Rahmen und einem <c>»</c> davor. ⚠ <b>Diese Ergaenzung ist unsere und sie
/// ist zwingend:</b> waeren nur die geschafften waehlbar, koennte eine frische
/// Kampagne — Completed = 0 — ueberhaupt nicht begonnen werden.</item>
/// <item><b>Noch nicht freigeschaltet</b>: schattiert, dunkler Grund, matte
/// Schrift, nicht anklickbar.</item>
/// </list>
///
/// <para>Freigeschaltet heisst hier genau das, was
/// <see cref="Campaign.CampaignManager.Completed"/> in
/// <c>user://campaign.cfg</c> festhaelt: die hoechste beendete Missionsnummer.
/// Es gibt keine zweite Buchfuehrung neben dieser.</para>
/// </summary>
public sealed partial class CampaignScreen : Control
{
    /// <summary>Wie viele Kacheln nebeneinander. Drei, weil eine Kachel den
    /// laengsten Titel (»11 — Trading Center«) in der Spielschrift bei
    /// Vergroesserung 2 noch fasst.</summary>
    private const int Cols = 3;
    private const int Scale = 2;
    private const int TileW = 168, TileH = 24;

    private readonly Action<Campaign.CampaignManager.Mission> _start;
    private Label _status = null!;
    private string _footer = "";

    /// <param name="start">Was beim Anklicken einer Mission geschehen soll.
    /// Wird von aussen gereicht, damit dieser Bildschirm nichts ueber Briefing
    /// und Szenenwechsel wissen muss — genau wie
    /// <see cref="MissionEndWindow.OnContinue"/>.</param>
    public CampaignScreen(Action<Campaign.CampaignManager.Mission> start) => _start = start;

    public override void _Ready()
    {
        // ⚠ Anker UND Kanten. Der Voreinsteller allein laesst das Steuerelement
        // in der Groesse null stehen — dieselbe Falle, die im Kopf von
        // StartMenuPanel._Ready notiert ist. Beim ersten Anlauf klebte das
        // Fenster deshalb halb ausserhalb der linken oberen Ecke und das Raster
        // war gar nicht da: alles darin hatte die Hoehe null.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        // der laufende Demohintergrund bleibt sichtbar, wird aber abgedunkelt —
        // dieselbe Loesung wie im LoadGameScreen
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(dim);

        var font = BriefingScreen.LegacyFont(second: false);
        var missions = Campaign.CampaignManager.Missions;
        int done = Campaign.CampaignManager.Completed;
        var next = Campaign.CampaignManager.Next();

        // Das Fenster haengt an allen vier Seiten mit Abstand, statt eine feste
        // Groesse zu bekommen: 33 Kacheln sind auf einem 1280x720-Fenster
        // hoeher als der Bildschirm, und ein Raster mit fester Hoehe haette
        // unten einfach aufgehoert. So kostet ein kleines Fenster einen
        // Rollbalken und keinen Inhalt — dieselbe Lehre wie beim
        // Gefechtsaufbau.
        int wantW = 2 * 14 * Scale + Cols * TileW * Scale + (Cols - 1) * 8 * Scale;
        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0, AnchorBottom = 1,
            GrowHorizontal = GrowDirection.Both,
            OffsetLeft = -wantW / 2, OffsetRight = wantW / 2,
            OffsetTop = 16, OffsetBottom = -16,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = StartMenuPanel.Pal(0x2a),
            BorderColor = StartMenuPanel.Pal(0x28),
            BorderWidthTop = 2, BorderWidthBottom = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2,
        });
        AddChild(panel);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 0);
        panel.AddChild(col);

        // ---- Titelleiste, gebaut wie die des Startmenues ---------------------
        // Dort ist sie gelesen (@0x4803e4/@0x480430: Titel auf x=10,y=2 in den
        // Farben 0x96/0xa9). HIER ist sie nachgemacht, denn dieses Fenster gibt
        // es im Original nicht — nur ihre Form und ihre Farben sind uebernommen.
        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = StartMenuPanel.Pal(0x2c),
            ContentMarginLeft = 10 * Scale, ContentMarginRight = 8 * Scale,
            ContentMarginTop = 2 * Scale, ContentMarginBottom = 2 * Scale,
            BorderColor = StartMenuPanel.Pal(0x28), BorderWidthBottom = Scale,
        });
        col.AddChild(bar);
        var barRow = new HBoxContainer();
        bar.AddChild(barRow);
        var title = new Label { Text = "Kampagne", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeColorOverride("font_color", StartMenuPanel.Pal(0x96));
        Style(title, font);
        barRow.AddChild(title);
        // Der Zusatz sagt, dass diese Seite unsere ist — dieselbe Farbe und
        // dieselbe Stellung wie »· REBORN« in der Startmenueleiste.
        var mine = new Label { Text = "· Uebersicht (Remake)" };
        mine.AddThemeColorOverride("font_color", Color.Color8(150, 205, 235));
        Style(mine, font);
        barRow.AddChild(mine);

        var pad = new MarginContainer();
        foreach (string m in new[] { "margin_left", "margin_right" })
            pad.AddThemeConstantOverride(m, 14 * Scale);
        pad.AddThemeConstantOverride("margin_top", 8 * Scale);
        pad.AddThemeConstantOverride("margin_bottom", 6 * Scale);
        pad.SizeFlagsVertical = SizeFlags.ExpandFill;
        col.AddChild(pad);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6 * Scale);
        pad.AddChild(body);

        if (missions.Count == 0)
        {
            var none = new Label
            {
                Text = "Keine Kampagne eingelesen.\n" +
                       "Die Missionsliste entsteht beim Einlesen der Spielkopie\n" +
                       "und liegt danach in user://data/Maps/campaign.json.",
                HorizontalAlignment = HorizontalAlignment.Center,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
            };
            none.AddThemeColorOverride("font_color", StartMenuPanel.Pal(0x24));
            Style(none, font);
            body.AddChild(none);
        }
        else
        {
            // ⚠ Der Zeiger ist ">" und nicht "»": die Spielschrift hat auf Platz
            // 187 zwar ein Zeichen, aber nicht das franzoesische Anfuehrungs-
            // zeichen — FONT.CWD folgt oberhalb von 127 einer eigenen Belegung.
            // Nachgesehen im ausgegebenen akte_font.fnt, nachdem im ersten Bild
            // ein fremder Buchstabe vor Mission 02 stand.
            var legend = new Label
            {
                // kurz genug fuer EINE Zeile bei 1096 Punkten Fensterbreite —
                // eine zweite Zeile kostet die unterste Kachelreihe
                Text = "Gold: geschafft   > hell: als naechstes dran   matt: gesperrt",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            legend.AddThemeColorOverride("font_color", StartMenuPanel.Pal(0x24));
            Style(legend, font);
            body.AddChild(legend);

            var scroll = new ScrollContainer
            {
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            body.AddChild(scroll);

            var grid = new GridContainer
            {
                Columns = Cols,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            grid.AddThemeConstantOverride("h_separation", 8 * Scale);
            grid.AddThemeConstantOverride("v_separation", 4 * Scale);
            scroll.AddChild(grid);

            foreach (var m in missions)
                grid.AddChild(Tile(m, m.Index <= done, next != null && m.Index == next.Index, font));
        }

        // ---- Fusszeile -------------------------------------------------------
        _footer = missions.Count == 0
            ? ""
            : next == null
                ? $"Alle {missions.Count} Missionen geschafft — jede laesst sich wieder spielen"
                : $"{missions.Count} Missionen · {done} geschafft · als naechstes {next.Label}";
        // Autowrap, damit eine lange Zeile das Fenster nicht ueber den
        // Bildschirmrand hinaus aufzieht: ein Etikett ohne Umbruch verlangt
        // seine ganze Breite als Mindestbreite, und der PanelContainer gibt sie
        // ihm — gegen die Anker. Genau das ist beim ersten Bild passiert.
        _status = new Label
        {
            Text = _footer,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _status.AddThemeColorOverride("font_color", StartMenuPanel.Pal(0x24));
        Style(_status, font);
        body.AddChild(_status);

        var back = new Button { Text = "Zurueck", CustomMinimumSize = new Vector2(0, TileH * Scale) };
        Style(back, font);
        back.Pressed += QueueFree;
        body.AddChild(back);
    }

    /// <summary>Eine Missionskachel in einem der drei Zustaende.</summary>
    private Button Tile(Campaign.CampaignManager.Mission m, bool completed, bool isNext, Font? font)
    {
        bool open = completed || isNext;
        var b = new Button
        {
            Text = (isNext ? "> " : "") + m.Label,
            CustomMinimumSize = new Vector2(TileW * Scale, TileH * Scale),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            Disabled = !open,
            FocusMode = FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
        };
        Style(b, font);

        // Der »aktive Rahmen«: golden bei geschafft, hell bei der naechsten.
        // Gesperrt heisst dunkler Grund ohne Rahmen — das ist das »schattiert«
        // aus dem Wunsch, und es ist mehr als nur blasse Schrift, damit man den
        // Unterschied auch aus zwei Metern Abstand sieht.
        Color edge = completed ? StartMenuPanel.Pal(0x96)
                   : isNext ? StartMenuPanel.Pal(0xfe)
                   : StartMenuPanel.Pal(0x2c);
        Color bg = open ? StartMenuPanel.Pal(0x2c) : StartMenuPanel.Pal(0x2f);

        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            var box = new StyleBoxFlat
            {
                BgColor = state == "hover" || state == "pressed" ? StartMenuPanel.Pal(0x28) : bg,
                BorderColor = edge,
                ContentMarginLeft = 6 * Scale, ContentMarginRight = 4 * Scale,
                ContentMarginTop = 2, ContentMarginBottom = 2,
            };
            box.SetBorderWidthAll(open ? 2 : 1);
            b.AddThemeStyleboxOverride(state, box);
        }
        var fg = completed ? StartMenuPanel.Pal(0xfe)
               : isNext ? StartMenuPanel.Pal(0xfe)
               : StartMenuPanel.Pal(0x29);
        foreach (string c in new[] { "font_color", "font_hover_color",
                                     "font_pressed_color", "font_disabled_color" })
            b.AddThemeColorOverride(c, fg);

        // Was die Kachel unten in der Fusszeile erzaehlt. Die Kartengroesse und
        // der Kartenname kommen aus derselben eingelesenen Beschreibung, aus der
        // auch die Vorschau im Gefecht ihre Zeile nimmt.
        string help = open
            ? (completed ? "Geschafft — noch einmal spielen: " : "Als naechstes: ") + m.Label
            : $"{m.Label} — noch nicht freigeschaltet; " +
              $"erst Mission {m.Index - 1} beenden";
        b.TooltipText = help + "\n" + MapPreview.Caption(m.Map);
        b.MouseEntered += () => _status.Text = help;
        b.MouseExited += () => { if (_status.Text == help) _status.Text = _footer; };
        if (open) b.Pressed += () => { _start(m); QueueFree(); };
        return b;
    }

    /// <summary>Die Spielschrift auflegen. Fehlt sie — nichts eingelesen —,
    /// bleibt Godots eigene stehen; ein fehlender Import soll die Seite nicht
    /// kosten (dieselbe Vorsichtsmassnahme wie in StartMenuPanel).</summary>
    private static void Style(Control c, Font? font)
    {
        if (font == null) return;
        c.AddThemeFontOverride("font", font);
        c.AddThemeFontSizeOverride("font_size", 13 * Scale);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            QueueFree();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Fuer den Pruefstand: welche Missionen die Seite als waehlbar
    /// zeichnet. Ein Bildschirm, der nur RICHTIG AUSSIEHT, ist nichts wert —
    /// dieselbe Ueberlegung wie hinter <c>--menu-click</c>.</summary>
    public static List<string> PlayableNow()
    {
        var l = new List<string>();
        int done = Campaign.CampaignManager.Completed;
        var next = Campaign.CampaignManager.Next();
        foreach (var m in Campaign.CampaignManager.Missions)
            if (m.Index <= done || (next != null && m.Index == next.Index)) l.Add(m.Label);
        return l;
    }
}
