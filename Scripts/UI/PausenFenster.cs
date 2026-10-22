namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// ⭐ 22.10.2026, Paket N (bug-616) — <b>Fensterart 40 »Pause«</b>, mit den Kacheln des
/// Originals. Lesung: <c>berichte/pausenfenster-40-opus.md</c>; Zeichner und Sender in F
/// selbst gegengelesen.
///
/// <list type="bullet">
/// <item><b>Öffner</b> C 0x441EC0 (F 0x440EA0): Doppelöffnungssperre auf Art 0x28, Lage
/// <c>x = (Schirmbreite − B)/2</c>, <c>y = Schirmhöhe/5</c> — bei uns
/// <see cref="WindowManager.FesteLage"/> (Art 40).</item>
/// <item><b>Anleger</b> C 0x45B770: <c>byte[+0] := 0x28</c>, Breite 0xB4 = <b>180</b>,
/// Höhe 0x3C = <b>60</b> → 9 × 3 Kacheln.</item>
/// <item><b>Zeichner</b> F 0x4845E0 (C 0x485F10): Rahmen <c>0x40116D</c>, Titel »Pause«
/// bei (10, 2) in 0x96/0xA9 (F 0x484661..0x484680), Knopf »Weiter« bei (40, 25), <b>5
/// Kacheln</b> breit (F 0x48469E..0x4846A2), Druckzustand <c>+0xACA8</c>.</item>
/// <item><b>Treffer</b> C 0x461C5D: Titelleiste (B−20 breit) −5 = ziehen, Kreuz −2,
/// »Weiter« 1, Rest 0, außerhalb −1. Jeder Treffer außer −5 spielt Klang 306 (C 0x448603).</item>
/// <item><b>Wirkung</b>: −2 (C 0x448656) und 1 (F 0x44C809) schicken 993; Rechtsklick auf
/// Art 40 ebenso (F 0x414118). Das Fenster schliesst sich NICHT selbst — das tut der
/// 993-Behandler (F 0x4C41E7 → Schliesser, Wort := 0).</item>
/// </list>
///
/// <para>⚠ Gegenschalter <c>--pausenfenster-alt</c>: die Godot-Zeile aus
/// <c>Rendering/NetzPause.cs</c> wie bis 0.6.11. Ohne eingelesene Kacheln fällt das Spiel
/// von selbst auf diese Zeile zurück (<see cref="Usable"/>).</para>
/// </summary>
public sealed partial class PausenFenster : Control
{
    public const int Art = 40;
    public const int WTiles = 9, HTiles = 3;
    public const int Scale = 2;
    private const int TitleX = 10, TitleY = 2;
    private const int KnopfX = 40, KnopfY = 25, KnopfKacheln = 5;

    /// <summary><c>--pausenfenster-alt</c>.</summary>
    public static bool Alt => Array.IndexOf(Core.CommandLine.Args, "--pausenfenster-alt") >= 0;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    /// <summary>Das gerade gezeigte Fenster (für die Prüfstände).</summary>
    public static PausenFenster? Offen;

    /// <summary>»Weiter«, Kreuz, Rechtsklick → 993.</summary>
    public Action? OnWeiter;

    /// <summary>Wie oft ein Klick 993 ausgelöst hat (Prüfstand).</summary>
    public int Ausgeloest;

    private bool _gedrueckt, _zieht;

    public PausenFenster()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale, HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Beim Öffnen (Behandler, Wort 0 → 1000): Knopf nicht gedrückt.</summary>
    public void Zuruecksetzen() { _gedrueckt = false; _zieht = false; QueueRedraw(); }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null) return;
        WindowChrome.Paint(this, WTiles, HTiles, Scale);
        Text(font, TitleX, TitleY, "Pause", WindowChrome.TitleColour);   // C 0x50286C / F 0x5018AC
        WindowChrome.PaintButton(this, KnopfX, KnopfY, KnopfKacheln, Scale, _gedrueckt);
        string wort = "Weiter";                                          // C 0x50275C / F 0x50179C
        float wpx = font.GetStringSize(wort, HorizontalAlignment.Left, -1, FontSize).X;
        int bx = KnopfX + (int)((KnopfKacheln * WindowChrome.Cell * Scale - wpx) / 2 / Scale);
        Text(font, bx, KnopfY + (_gedrueckt ? 4 : 3), wort, WindowChrome.TextColour);
    }

    /// <summary>C 0x461C5D, Fensterart 40 — Punkt in FENSTERkoordinaten (Bildpunkte).</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int b = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x >= 0 && x < b - 20 && y >= 0 && y < 20) return -5;
        if (x < 0 || y < 0 || x >= b || y >= h) return -1;
        if (x >= b - 20 && y < 20) return -2;
        if (x >= KnopfX && x < KnopfX + KnopfKacheln * WindowChrome.Cell && y >= KnopfY && y < KnopfY + 20) return 1;
        return 0;
    }

    /// <summary>Die Mitte des Knopfes »Weiter« in Fensterkoordinaten (Prüfstand).</summary>
    public static Vector2 KnopfMitte => new Vector2((KnopfX + KnopfKacheln * 10) * Scale, (KnopfY + 10) * Scale);

    /// <summary>Ein Linksklick (losgelassen) — derselbe Weg für Maus und Prüfstand.
    /// Gibt den Treffer zurück.</summary>
    public int Klick(Vector2 p)
    {
        int t = Hit(p);
        if (t == -5 || t == -1) return t;
        WindowManager.Elementklang();                                    // 306, C 0x448603
        if (t == -2 || t == 1)
        {
            if (t == 1) { _gedrueckt = true; QueueRedraw(); }            // +0xACA8 := 1
            Ausgeloest++;
            OnWeiter?.Invoke();
        }
        return t;
    }

    /// <summary>Rechtsklick auf Art 40 → 993 (F 0x414118), ohne Klang.</summary>
    public void Rechtsklick() { Ausgeloest++; OnWeiter?.Invoke(); }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex == MouseButton.Right && mb.Pressed) { Rechtsklick(); AcceptEvent(); return; }
        if (mb.ButtonIndex != MouseButton.Left) return;
        if (mb.Pressed)
        {
            if (Hit(mb.Position) == -5) _zieht = true;
            AcceptEvent();
            return;
        }
        if (_zieht) { _zieht = false; AcceptEvent(); return; }
        Klick(mb.Position);
        AcceptEvent();
    }

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
        else if (@event is InputEventMouseButton up && up.ButtonIndex == MouseButton.Left && !up.Pressed)
        { _zieht = false; AcceptEvent(); }
    }
}
