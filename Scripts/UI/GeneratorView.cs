namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>»GENERATOR«</b> — das Gebäudefenster der Gebäudeart 7, <b>Fensterart 20</b>
/// des Originals, mit dessen eigenen Kacheln (13.09.2026, Kampagne 13). Lesung:
/// <c>berichte/generator-fable.md</c>.
///
/// <para><b>Klickweg:</b> allgemeiner Bauwerkszweig der Zeigerwahl (Besitzer ==
/// Betrachter, built ≠ 0, Bauzustand &lt; 100) → Arm <c>0x437207</c> → Öffner
/// <c>0x442FB0</c> → Anleger <c>0x4597B0</c>: <b>220 × 100</b>.</para>
///
/// <para><b>Zeichner <c>0x476410</c></b> (F <c>0x474D00</c>, 314/314 gleich):</para>
/// <code>
///   (10, 2)  "Generator" 150/169
///   (20,20)  "Energie :"  Balken (80,22) 120x10 schwarz, (84,26) 112*hp/hpmax x 2, Farbe 140
///   (20,35)  "Stromerzeugung : " + sec26 +0x02
///   (20,50)  "Stromanteil : "   + sec26 +0x02 + "/" + erbracht(Betrachter)
///   (20,65)  "Stromverbrauch : " + Bedarf(Betrachter)
/// </code>
/// <para><b>Keine Knöpfe</b>, Treffertest <c>0x45F7BA</c> nur Titel/Kreuz, Klickarm
/// leer (nur Klang 306), Rechtsklick und ESC schliessen, Hilfezeile 0x45
/// »Spezifikationen des Kraftwerks«.</para>
///
/// <para>⚠ UNSERE Setzungen: Massstab 2; der Wirt zeichnet bei jedem Refresh neu
/// (das Original ist ein Standbild vom Öffnen); die Doppelöffnungswache des
/// Originals prüft irrtümlich Art 23 — nicht nachgebaut, je Gebäude ein Fenster.</para>
/// </summary>
public sealed partial class GeneratorView : Control
{
    public const int WTiles = 11, HTiles = 5;
    public const int Scale = 2;

    public Action? OnClose;

    private BuildingWindow.Stand? _stand;
    private bool _zieht;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    /// <summary>Für den Prüfstand: die drei Zeilen, wie sie zuletzt gezeichnet wurden.</summary>
    public string Zeile1 { get; private set; } = "";
    public string Zeile2 { get; private set; } = "";
    public string Zeile3 { get; private set; } = "";

    public GeneratorView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale, HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    public void Zeige(BuildingWindow.Stand s)
    {
        _stand = s;
        Zeile1 = "Stromerzeugung : " + s.StromErzeugung;
        Zeile2 = "Stromanteil : " + s.StromErzeugung + "/" + s.StromErbracht;
        Zeile3 = "Stromverbrauch : " + s.StromBedarf;
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
        var s = _stand;
        var font = WindowChrome.LegacyFont;
        if (s == null || WindowChrome.Atlas == null || font == null) return;
        WindowChrome.Paint(this, WTiles, HTiles, Scale);                          // Nr 1
        Text(font, 10, 2, "Generator", WindowChrome.TitleColour);                  // Nr 2
        Text(font, 20, 20, "Energie :", WindowChrome.TextColour);                  // Nr 3
        Rechteck(80, 22, 120, 10, new Color(0, 0, 0));                            // Nr 4
        if (s.HpMax > 0)                                                          // Nr 5
            Rechteck(84, 26, 112 * Mathf.Clamp(s.Hp, 0, s.HpMax) / s.HpMax, 2, WindowChrome.LineColour);
        Text(font, 20, 35, Zeile1, WindowChrome.TextColour);                       // Nr 6
        Text(font, 20, 50, Zeile2, WindowChrome.TextColour);                       // Nr 7
        Text(font, 20, 65, Zeile3, WindowChrome.TextColour);                       // Nr 8
    }

    /// <summary>Treffertest <c>0x45F7BA</c>: −5 Titel, −2 Kreuz, −1 ausserhalb, sonst 0.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (y < 20) return x < w - 20 ? -5 : -2;
        return 0;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm)
        {
            if (GetParent() is Control eltern)
            {
                var vp = GetViewportRect().Size;
                eltern.Position = new Vector2(
                    Mathf.Clamp(eltern.Position.X + mm.Relative.X, 0, Mathf.Max(0, vp.X - eltern.Size.X)),
                    Mathf.Clamp(eltern.Position.Y + mm.Relative.Y, 0, Mathf.Max(0, vp.Y - eltern.Size.Y)));
            }
            AcceptEvent();
        }
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
            WindowManager.Elementklang();                                         // 0x4142D7
            OnClose?.Invoke();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        AcceptEvent();
        if (!mb.Pressed) { _zieht = false; return; }
        int t = Hit(mb.Position);
        if (t == -5) { _zieht = true; return; }
        if (t == -1) return;
        WindowManager.Elementklang();                                             // 0x448603, Arm leer
        if (t == -2) OnClose?.Invoke();
    }

    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) >= -2 && Hit(pos) != -1 ? "Spezifikationen des Kraftwerks" : "";
}
