namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>»VON · NACH · LEEREN · START«</b> — das Routenfenster des Transporters,
/// <b>Fensterart 16</b> des Originals, <b>mit dessen eigenen Kacheln</b>
/// (Anleger <c>0x458FD0</c>, Zeichner <c>0x473BF0</c>, Trefferprüfung
/// <c>0x45EF48</c>, Klickarm <c>0x44B625</c>).
///
/// <para>⚠⚠ 08.09.2026, seine Meldung zur ersten Fassung: »aber wieso nehmen
/// wir das originale nicht? … das sieht auch nach unserem Eigenbau aus«. Er hat
/// recht, und die Grafiken waren längst da: <see cref="WindowChrome"/> hält seit
/// dem 25.08.2026 den Rahmen, den Knopf und die Schrift aus
/// <c>WINDOWS.CWW</c>. Dieselbe Meldung hatte er damals zum Kaufmenü gegeben —
/// die Lehre ist also eine wiederholte: <b>ein Fenster wird mit den Kacheln des
/// Originals gebaut, nicht mit Godot-Knöpfen.</b> Vorbild ist
/// <see cref="SupplyShopView"/>.</para>
///
/// <para><b>Die Masse sind die gelesenen</b>, Punkt für Punkt:</para>
/// <code>
///   Fenster 280 x 120                 = 14 x 6 Kacheln zu 20
///   vier Knoepfe auf y = 14, je 60x20 = 3 Kacheln breit
///        Von (x=20) · Nach (80) · Leeren (140) · Start (200)
///   "Von :"   (20, 35)   die vier Quellnamen bei (70, 35 + 15*i)
///   "Nach :"  (20, 95)   Zielname oder "leer" bei (70, 95)
///   Schliesskreuz oben rechts (w-20, 0, 20x20) -> Element -2
/// </code>
///
/// <para>Die Hilfezeilen des Originals (Texte 0x2F..0x32) hängen an den
/// Knöpfen: »Wählen des Startpunktes«, »Wählen des Zielpunktes«, »Löschen des
/// Transport-Zyklus«, »Transport starten«.</para>
/// </summary>
public sealed partial class RouteWindow : Control
{
    /// <summary>14 x 6 Kacheln — die 280 x 120 des Originals.</summary>
    public const int WTiles = 14, HTiles = 6, Scale = 2;

    /// <summary>Die vier Knöpfe: x in Fensterpunkten, Breite 3 Kacheln.</summary>
    private static readonly int[] ButtonX = { 20, 80, 140, 200 };
    private const int ButtonY = 14, ButtonTiles = 3;
    private static readonly string[] ButtonText = { "Von", "Nach", "Leeren", "Start" };
    private static readonly string[] ButtonHelp =
    {
        "Waehlen des Startpunktes", "Waehlen des Zielpunktes",
        "Loeschen des Transport-Zyklus", "Transport starten",
    };

    public Func<(string[] Quellen, string Ziel, bool Gestartet)>? Inhalt;
    public Func<int>? Modus;              // 1 = Von, 2 = Nach
    public Func<string>? Note;
    public Action<int>? OnModus;
    public Action? OnLeeren;
    public Action? OnStart;
    public Action? OnClose;

    private int _held = -1;

    public static bool Usable => WindowChrome.Atlas != null;

    public RouteWindow()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null || Inhalt == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);

        var (quellen, ziel, gestartet) = Inhalt();
        int modus = Modus?.Invoke() ?? 0;

        // Die vier Knoepfe. ⚠ GEDRUECKT ist im Original eine Flagge im
        // Fenstersatz (+0xACA8 Von, +0xACAC Nach) — und genau die entscheidet,
        // was ein Kartenklick bedeutet. Sie MUSS sichtbar sein.
        for (int i = 0; i < 4; i++)
        {
            bool gedrueckt = _held == i || (i == 0 && modus == 1) || (i == 1 && modus == 2);
            WindowChrome.PaintButton(this, ButtonX[i], ButtonY, ButtonTiles, Scale, gedrueckt);
            float wpx = font.GetStringSize(ButtonText[i], HorizontalAlignment.Left, -1,
                                           FontSize).X;
            int tx = ButtonX[i] + (ButtonTiles * WindowChrome.Cell
                                   - (int)(wpx / Scale)) / 2;
            Text(font, tx, ButtonY + (gedrueckt ? 4 : 3), ButtonText[i],
                 WindowChrome.TextColour);
        }

        Text(font, 20, 35, "Von :", WindowChrome.TitleColour);
        for (int i = 0; i < 4; i++)
            Text(font, 70, 35 + 15 * i, quellen[i], WindowChrome.TextColour);
        Text(font, 20, 95, "Nach :", WindowChrome.TitleColour);
        Text(font, 70, 95, ziel, WindowChrome.TextColour);

        string n = Note?.Invoke() ?? "";
        if (gestartet) n = n.Length > 0 ? n + "  (laeuft)" : "(laeuft)";
        if (n.Length > 0) Text(font, 20, 110, n, WindowChrome.TextColour);
    }

    /// <summary>Die Trefferprüfung des Originals (<c>0x45EF48</c>): das
    /// Schliesskreuz gibt −2, die vier Knöpfe 1..4, sonst 0.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;
        for (int i = 0; i < 4; i++)
            if (x >= ButtonX[i] && x < ButtonX[i] + ButtonTiles * WindowChrome.Cell
                && y >= ButtonY && y < ButtonY + 20) return i + 1;
        return 0;
    }

    /// <summary>Ziehen am Fensterkoerper — die Trefferprüfung <c>0x45EF48</c>
    /// sagt es woertlich: »sonst 0 (ziehen)«.</summary>
    private bool _zieht;

    public override void _Input(InputEvent @event)
    {
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm)
        {
            Position += mm.Relative;
            var vp = GetViewportRect().Size;
            Position = new Vector2(Mathf.Clamp(Position.X, 0, Mathf.Max(0, vp.X - Size.X)),
                                   Mathf.Clamp(Position.Y, 0, Mathf.Max(0, vp.Y - Size.Y)));
            AcceptEvent();
        }
        else if (@event is InputEventMouseButton up
                 && up.ButtonIndex == MouseButton.Left && !up.Pressed)
        { _zieht = false; AcceptEvent(); }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left)
            return;
        if (mb.Pressed)
        {
            int t = Hit(mb.Position);
            _held = t >= 1 ? t - 1 : -1;
            QueueRedraw();
            if (t == 0) { _zieht = true; AcceptEvent(); return; }
            if (t != 0) AcceptEvent();
            return;
        }
        if (_zieht) { _zieht = false; AcceptEvent(); return; }
        int hit = Hit(mb.Position);
        _held = -1;
        QueueRedraw();
        if (hit == 0) return;
        AcceptEvent();
        switch (hit)
        {
            case -2: OnClose?.Invoke(); break;
            case 1: OnModus?.Invoke(1); break;       // Von
            case 2: OnModus?.Invoke(2); break;       // Nach
            case 3: OnLeeren?.Invoke(); break;
            case 4: OnStart?.Invoke(); break;
        }
    }

    /// <summary>Der Hilfetext des Originals zu jedem Knopf — er hängt bei uns
    /// am Mauszeiger, weil wir keine Hilfezeile im Bedienblock haben.</summary>
    public override string _GetTooltip(Vector2 pos)
    {
        int t = Hit(pos);
        return t >= 1 && t <= 4 ? ButtonHelp[t - 1] : "";
    }

    public void Refresh() => QueueRedraw();

    /// <summary>Unten links über dem Bedienblock.</summary>
    public void PlaceBottomLeft()
    {
        var s = GetViewportRect().Size;
        Position = new Vector2(24, s.Y - Size.Y - 150);
    }
}
