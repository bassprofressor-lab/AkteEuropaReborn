namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>Die Transportlinie</b> — Fensterart 4 des Originals, 300 × 100 (22.09.2026).
///
/// <para>Gemeldet aus Kampagne 21: »ich kann nach wie vor keine einheiten bauen
/// trotz das die züge schon mehrmals gefahren sind«. Zwischen zwei Bahnhöfen
/// fährt die Matrix des Originals nichts; der Spieler stellt die Linien hier
/// von Hand um. Lesung <c>berichte/bahnhof-transport-fable.md</c> §3.1.</para>
///
/// <para><b>Öffner</b> <c>0x445D70</c> (Linie, x, y), einziger Rufer die Karte in
/// Betriebsart 1 (@0x4492CC). <b>Anleger</b> <c>0x4578C0</c>: Art 4, 300 × 100,
/// <c>+0x0C := Linie</c>. <b>Zeichner <c>0x464BE0</c></b> (F <c>0x4634D0</c>):</para>
/// <code>
///   Rahmen OHNE Titelzeile (0x455E50, 7. Arg 0)
///   (20,38)  "Zu " + Name des Gebaeudes an Knoten 1
///   (20,53)  "Zu " + Name an Knoten 2
///   (20,68)  "Angehalten"                              0x501770
///   Trennlinien (20,35, 260x1) und (235,18, 1x62), 0x8C
///   Sinnbilder "]" "[" "{" "^" an (240/250/260/270, 18)
///   je Ware k ein "X" an (240 + 10k, 38 + 15·Schalter[k])
/// </code>
/// <para><b>Treffertest</b> <c>0x45D0C7</c>: Titel −5, Kreuz −2,
/// (240, 35, 40×45) → <c>1 + 4·Zeile + Spalte</c> (1…12), Hilfe 4. <b>Druckarm</b>
/// <c>0x449936</c>: Befehl 500 (Linie, Ware = (T−1)&amp;3, Wert = (T−1)&gt;&gt;2).
/// Wert 0 = fährt zu Knoten 1, 1 = zu Knoten 2, 2 = angehalten.</para>
///
/// <para>⚠ UNSERE Setzung: der Massstab 2 wie bei allen Fenstern; das »Ziehen«
/// über die obersten 20 Punkte, obwohl das Fenster keine Titelzeile malt — der
/// Treffertest des Originals liefert dort trotzdem −5.</para>
/// </summary>
public sealed partial class TransportlinieView : Control
{
    public const int WTiles = 15, HTiles = 5;
    public const int Scale = 2;

    private const int ZeileX = 20, Zeile1Y = 38, Zeile2Y = 53, Zeile3Y = 68;
    private const int TafelX = 240, TafelY = 35, TafelW = 40, TafelH = 45;
    private const int SinnY = 18, Spalte = 10, ZeilenSchritt = 15;

    public int Linie { get; private set; } = -1;
    private string _name1 = "", _name2 = "";
    private byte[] _schalter = { 2, 2, 2, 2 };
    private bool _zieht;

    /// <summary>Befehl 500: (Linie, Ware, Wert).</summary>
    public Action<int, int, int>? OnBefehl500;
    public Action? OnClose;

    public int Klicks { get; private set; }
    public int LetzterTreffer { get; private set; } = -99;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    public TransportlinieView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    public void Zeige(int linie, string name1, string name2, byte[] schalter)
    {
        Linie = linie;
        _name1 = name1;
        _name2 = name2;
        _schalter = schalter;
        QueueRedraw();
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    private void Rechteck(int x, int y, int w, int h, Color c)
        => DrawRect(new Rect2(x * Scale, y * Scale, w * Scale, h * Scale), c, true);

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null || Linie < 0) return;
        WindowChrome.Paint(this, WTiles, HTiles, Scale, titled: false);
        Text(font, ZeileX, Zeile1Y, "Zu " + _name1, WindowChrome.TextColour);
        Text(font, ZeileX, Zeile2Y, "Zu " + _name2, WindowChrome.TextColour);
        Text(font, ZeileX, Zeile3Y, "Angehalten", WindowChrome.TextColour);
        Rechteck(20, 35, 260, 1, WindowChrome.LineColour);
        Rechteck(235, 18, 1, 62, WindowChrome.LineColour);
        string[] sinn = { BaseWindow.IconW, BaseWindow.IconF, BaseWindow.IconS, "^" };
        for (int k = 0; k < 4; k++)
        {
            var col = BaseWindow.Sinnbildfarbe(sinn[k][0]) ?? WindowChrome.TextColour;
            Text(font, TafelX + Spalte * k, SinnY, sinn[k], col);
            int w = Mathf.Clamp(_schalter[k], 0, 2);
            Text(font, TafelX + Spalte * k, Zeile1Y + ZeilenSchritt * w, "X", WindowChrome.TextColour);
        }
    }

    /// <summary>Der Treffertest <c>0x45D0C7</c>.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (y < 20) return x < w - 20 ? -5 : -2;
        if (x >= TafelX && x < TafelX + TafelW && y >= TafelY && y < TafelY + TafelH)
            return 1 + 4 * (int)((y - TafelY) / ZeilenSchritt) + (int)((x - TafelX) / Spalte);
        return 0;
    }

    /// <summary>Wo das Feld (Ware, Wert) auf dem SCHIRM liegt — für den Prüfstand.</summary>
    public Vector2 FeldAufDemSchirm(int ware, int wert)
        => GetGlobalRect().Position
           + new Vector2(TafelX + Spalte * ware + 5, TafelY + ZeilenSchritt * wert + 7) * Scale;

    public override void _Input(InputEvent @event)
    {
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm) { Position += mm.Relative; AcceptEvent(); }
        else if (@event is InputEventMouseButton up && up.ButtonIndex == MouseButton.Left && !up.Pressed)
        { _zieht = false; AcceptEvent(); }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex == MouseButton.Right)
        {
            AcceptEvent();
            if (!mb.Pressed) return;
            WindowManager.Elementklang();
            OnClose?.Invoke();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        AcceptEvent();
        if (!mb.Pressed) { _zieht = false; return; }
        int t = Hit(mb.Position);
        LetzterTreffer = t;
        if (t == -5) { _zieht = true; return; }
        if (t is 0 or -1) return;
        Klicks++;
        WindowManager.Elementklang();
        if (t == -2) { OnClose?.Invoke(); return; }
        if (t is >= 1 and <= 12) Druecke(t);
    }

    /// <summary>Der Druckarm <c>0x449936</c> — auch für den Prüfstand.</summary>
    public void Druecke(int t)
    {
        int ware = (t - 1) & 3, wert = (t - 1) >> 2;
        OnBefehl500?.Invoke(Linie, ware, wert);
    }

    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) is >= 1 and <= 12 ? "Einrichten der gewünschten Transportlinien" : "";
}
