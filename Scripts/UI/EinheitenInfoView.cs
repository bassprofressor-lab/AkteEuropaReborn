namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»EINHEITEN-INFO«</b> — <b>Fensterart 19</b> des Originals (13.09.2026).
///
/// <para>Beim Bau der Radarmasten fiel auf, dass die Zeile »Auflage n/20« im
/// Original in diesem Fenster steht — und dass es bei uns fehlte: der
/// Menüeintrag »Einheiteninformation« sagte nur »Die Werte stehen im
/// Bedienblock«. Seine Ansage: bauen, voller Original-Stil. Lesung:
/// <c>berichte/einheiteninfo-fable.md</c>.</para>
///
/// <para><b>Weg:</b> Einheitenmenü Code 6 → Menüarm <c>0x44884E</c> → Öffner
/// <c>0x4436E0</c> (F <c>0x4426D0</c>, Wache Art 19 + Griff) → Anleger
/// <c>0x459670</c>: Art 19, <b>140 breit</b>, an <b>Maus + 3</b>. Je Einheit
/// ein Fenster, mehrere nebeneinander.</para>
///
/// <para><b>Zeichner <c>0x474FE0</c></b> (F <c>0x4738D0</c>): NUR Text — kein
/// Bild, kein Balken, kein Knopf. Titel »Rang + Name« bei (10, 2) in
/// gewöhnlicher Schrift (das unterste Rangzeichen 0xB4 wird gestrichen,
/// @0x475033); »Energie : a/b« bei (20, 20); ab (20, 35) im 15er-Schritt die
/// Zeilen, die <c>MapEntityLayer.EinheitenInfo</c> baut. Die HÖHE rechnet der
/// Zeichner selbst (@0x4750BD…0x475135), gedeckelt bei 200.</para>
///
/// <para><b>Klicks:</b> Treffertest <c>0x45F64C</c> kennt nur Titel (ziehen),
/// Kreuz und Fläche (Hilfezeile 0x43 »Spezifikationen der Einheit«); der
/// Klickarm ist LEER, nur der Klickton. Rechtsklick schliesst.</para>
///
/// <para>⭐ <b>Nie neu gezeichnet</b> (§4 der Lesung): das Fenster zeigt den
/// Stand vom Öffnen — Energie, Munition, Sprit und Auflage laufen nicht mit.
/// Seine Entscheidung: wie im Original.</para>
///
/// <para>⚠ UNSERE Setzung: Massstab 2, wie bei allen unseren Fenstern.</para>
/// </summary>
public sealed partial class EinheitenInfoView : Control
{
    public const int WTiles = 7;
    public const int Scale = 2;

    /// <summary>Die Einheit (Griff = Entitätsindex) — die Kennung in der
    /// Fensterverwaltung.</summary>
    public int Griff = -1;

    public Action? OnClose;

    private string _titel = "";
    private int _rang = -1;
    private readonly List<string> _zeilen = new();
    private int _hTiles = 8;
    private bool _zieht;

    public int HTiles => _hTiles;
    public IReadOnlyList<string> Zeilen => _zeilen;
    public string Titel => _titel;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    public EinheitenInfoView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        TooltipText = " ";
    }

    /// <summary>Den Schnappschuss vom Öffnen setzen.</summary>
    public void Zeige(int rang, string name, List<string> zeilen, int hoehe)
    {
        _rang = rang;
        _titel = name;
        _zeilen.Clear();
        _zeilen.AddRange(zeilen);
        _hTiles = Mathf.Max(2, hoehe / WindowChrome.Cell);
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale, _hTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        QueueRedraw();
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, WindowChrome.TextColour);

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null) return;
        WindowChrome.Paint(this, WTiles, _hTiles, Scale);
        // Das unterste Rangzeichen streicht der Zeichner (0xB4 -> leer).
        string rang = _rang > 5 ? UnitListView.RangText(font, _rang) : "";
        Text(font, 10, 2, rang + _titel);
        for (int i = 0; i < _zeilen.Count; i++)
            Text(font, 20, i == 0 ? 20 : 35 + 15 * (i - 1), _zeilen[i]);
    }

    /// <summary>−5 Titel, −2 Kreuz, −1 ausserhalb, 0 Fläche (0x45F64C).</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = _hTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (y < 20) return x < w - 20 ? -5 : -2;
        return 0;
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

    public int Rechtsklicks { get; private set; }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex == MouseButton.Right)
        {
            AcceptEvent();
            if (!mb.Pressed) return;
            Rechtsklicks++;
            WindowManager.Elementklang();                        // 0x4142D7
            OnClose?.Invoke();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        AcceptEvent();
        if (!mb.Pressed) { _zieht = false; return; }
        int t = Hit(mb.Position);
        if (t == -5) { _zieht = true; return; }
        if (t == -1) return;
        WindowManager.Elementklang();                            // 0x448603 — der Arm selbst ist leer
        if (t == -2) OnClose?.Invoke();
    }

    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) is 0 or -5 ? "Spezifikationen der Einheit" : "";
}
