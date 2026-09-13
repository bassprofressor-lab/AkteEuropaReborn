namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>DAS MELDUNGSFENSTER</b> — <b>Fensterart 13</b> des Originals (13.09.2026).
///
/// <para>Gebaut für die Absagen des Fabrikfensters (»Sie haben leider nicht genug
/// Geld.«, »Kann nicht erweitern. / Sie haben bereits den Maximalwert
/// erreicht.«), die bis dahin nur in der Statuszeile standen. Lesung:
/// <c>berichte/meldungsfenster-fable.md</c>.</para>
///
/// <para><b>Öffner <c>0x4469A0(x, y, Zeile1, Zeile2, Standzeit, Mittig)</c></b>
/// (F <c>0x445940</c>). Anleger <c>0x4586D0</c>: Breite
/// <c>(⌊max(Textbreite)/20⌋ + 4)·20</c>, Höhe 60 (eine Zeile) bzw. 80 (zwei).
/// Zeichner <c>0x4732A0</c>: Rahmen OHNE Titelzeile (7. Argument von
/// <c>0x455E50</c> = 0), Zeile 1 mittig bei y = 25, Zeile 2 mittig bei y = 40,
/// gewöhnliche Schrift. Kein Knopf, kein Symbol.</para>
///
/// <para><b>Treffertest <c>0x45EB9F</c>:</b> obere Reihe ziehen, Ecke 20×20
/// schliessen; Klickarm leer (Klickton), Tasten nichts, Rechtsklick schliesst.
/// Die Standzeit zählt die Fensterverwaltung (alle 20 Takte, <c>0x4505F0</c>).
/// Das Spiel läuft weiter.</para>
///
/// <para>⚠ UNSERE Setzungen: Massstab 2; die Textbreite messen wir an unserer
/// Schrift (die Glyphenbreiten von FONT.CWD sind ungelesen); die Zu-Blende der
/// Verwaltung bleibt (im Original unerreichbar, §5.3 der Lesung).</para>
/// </summary>
public sealed partial class MeldungView : Control
{
    public const int Scale = 2;

    public Action? OnClose;

    private string _z1 = "", _z2 = "";
    private int _wTiles = 6, _hTiles = 3;
    private bool _zieht;

    public string Zeile1 => _z1;
    public string Zeile2 => _z2;
    public int WTiles => _wTiles;
    public int HTiles => _hTiles;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    public MeldungView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    /// <summary>Textbreite in Originalpunkten (0x4BA160) — an unserer Schrift gemessen.</summary>
    private static int Breite(Font f, string s)
        => s.Length == 0 ? 0 : (int)(f.GetStringSize(s, HorizontalAlignment.Left, -1, FontSize).X / Scale);

    /// <summary>Der Anleger <c>0x4586D0</c>. ⚠ Zeile 1 steht in einem 50-Byte-Fach:
    /// ein längerer Text wird dort gekappt, die Breite aber aus dem ganzen Text
    /// gerechnet (§2 der Lesung) — wörtlich übernommen.</summary>
    public void Anlegen(string zeile1, string zeile2)
    {
        var font = WindowChrome.LegacyFont;
        int w1 = font != null ? Breite(font, zeile1) : zeile1.Length * 6;
        int w2 = font != null ? Breite(font, zeile2) : zeile2.Length * 6;
        _wTiles = Math.Max(w1, w2) / WindowChrome.Cell + 4;
        _hTiles = zeile2.Length > 0 ? 4 : 3;
        _z1 = zeile1.Length > 50 ? zeile1[..50] : zeile1;
        _z2 = zeile2;
        CustomMinimumSize = new Vector2(_wTiles * WindowChrome.Cell * Scale, _hTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        QueueRedraw();
    }

    private void Mittig(Font f, int y, string s)
    {
        if (s.Length == 0) return;
        int x = (_wTiles * WindowChrome.Cell - Breite(f, s)) / 2;
        DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                   s, HorizontalAlignment.Left, -1, FontSize, WindowChrome.TextColour);
    }

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null) return;
        WindowChrome.Paint(this, _wTiles, _hTiles, Scale, titled: false);
        Mittig(font, 25, _z1);
        Mittig(font, 40, _z2);
    }

    /// <summary>−5 obere Reihe, −2 Ecke, −1 ausserhalb, sonst 0.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = _wTiles * WindowChrome.Cell, h = _hTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (y < 20) return x < w - 20 ? -5 : -2;
        return 0;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm) { Position += mm.Relative; AcceptEvent(); }
        else if (@event is InputEventMouseButton up && up.ButtonIndex == MouseButton.Left && !up.Pressed)
        { _zieht = false; AcceptEvent(); }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || !mb.Pressed) return;
        if (mb.ButtonIndex == MouseButton.Right)
        {
            AcceptEvent();
            WindowManager.Elementklang();
            OnClose?.Invoke();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        AcceptEvent();
        int t = Hit(mb.Position);
        if (t == -5) { _zieht = true; return; }
        if (t == -1) return;
        WindowManager.Elementklang();                          // 0x448603, Arm leer
        if (t == -2) OnClose?.Invoke();
    }
}
