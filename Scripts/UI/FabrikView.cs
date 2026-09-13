namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>»FABRIK«</b> — das Gebäudefenster der Waffen-, Fahrwerk- und
/// Spezialfabrik (Gebäudeart 2/3/4), <b>Fensterart 8</b> des Originals, mit
/// dessen Kacheln (13.09.2026).
///
/// <para>Seine Meldung: »bei den fabriken nutzen wir das falsche fenster (bei
/// uns ist dies ähnlich zur basis, aber eigentlich ist es ähnlich zu dem von der
/// Mine)«. Er hatte recht — die ganze Lesung steht in
/// <c>berichte/fabrikfenster-fable.md</c>. Klickweg <c>0x4379F0</c> → Arm
/// <c>0x43717D</c> → Öffner <c>0x443E00</c> (F <c>0x442DF0</c>) → Anleger
/// <c>0x457EC0</c>: Art 8, <b>260 × 240</b>.</para>
///
/// <para><b>Zeichner <c>0x46EDC0</c></b> (F <c>0x46D6B0</c>), Punkt für Punkt
/// dieselben Stellen wie das Minenfenster <see cref="MineView"/>:</para>
/// <code>
///   (10,  2)  "⟨ ]|[|{ ⟩ Fabrik " + Name         Farbe 150/169     @0x46EF09
///   (20, 23)  "Energie :"  Balken (80,25) 160x10, Fuellung 152·hp/hpmax x 2
///   (20, 40)  "Strom : " ist "/" soll                             @0x46F0E0
///   (20, 55)  "Status : " Wort (Tafel 0x46FAB8)                   @0x46F612
///   (20, 70)  "Waffen|Fahrwerke|Spezialteile gelagert : ⟨Sinnbild⟩" n "/" Kap
///   (20, 85)  "Terranium gelagert : " n                           @0x46F726
///   (20,100)  "Produktionsgeschwindigkeit : " n                   @0x46F7EA
///   (20,115)  "Lagerausbaukosten : " n                            @0x46F88E
///   (20,130)  "Produktionserweiterungskosten : " n                @0x46F930
///   Knopf (20,150) "Lagerausbau"   (135,150) "Verbessern"          5 Kacheln
///   Knopf (20,175) "Start"|"Anhalten"  (135,175) "Reparieren"
///   (20,200)  "Kontostand : " n                                   @0x46FAA5
/// </code>
///
/// <para><b>Hervorgehoben</b> (<c>0x4BA5E0(…, 154, 156)</c>, Palettenplatz 154
/// = (238, 46, 56)): die Lagerzeile, wenn das Lager voll ist; die Terranium-
/// zeile bei 0; und statt des Statusworts »Status : angehalten«, wenn die
/// Fabrik zwar aktiv ist, aber nicht produzieren kann (kein Terranium oder
/// Lager voll) — genau die zwei Eingangswachen des Takt-Arms 0.</para>
///
/// <para><b>Klicks</b> (Treffertest <c>0x45E399</c>, Klickarm <c>0x44ACF1</c>):
/// Klang 306 vorweg; Knopf 1 Lagerausbau → Befehl 509, Knopf 2 Start/Anhalten
/// → 511, Knopf 3 Verbessern → 510, Knopf 4 Reparieren → 519. Die
/// Vorbedingungen des Klickarms stehen in <c>MapEntityLayer.FabrikKnopf</c>.
/// Rechtsklick schliesst mit Klang 306. Tastatur: nichts.</para>
///
/// <para>⚠ <b>UNSERE Setzungen:</b> Massstab 2; die zweite Farbe der Paare
/// (169 bzw. 156) malen wir nicht — unser Textzeichner ist einfarbig, wie bei
/// allen Fenstern; der gedrückte Knopf bleibt es bis zum nächsten Klick
/// (<c>0x44FC90</c>, nur angelesen).</para>
/// </summary>
public sealed partial class FabrikView : Control
{
    /// <summary>13 × 12 Kacheln — die 260 × 240 aus dem Anleger <c>0x457EC0</c>.</summary>
    public const int WTiles = 13, HTiles = 12;
    public const int Scale = 2;

    private const int TextX = 20;
    private const int BarX = 80, BarY = 25, BarW = 160, BarH = 10;
    private const int ButtonTiles = 5;

    /// <summary>Die Knöpfe in der Reihenfolge der Treffernummern 1…4
    /// (<c>0x45E399</c>): Lagerausbau, Start/Anhalten, Verbessern, Reparieren.</summary>
    private static readonly int[] KnopfX = { 20, 20, 135, 135 };
    private static readonly int[] KnopfY = { 150, 175, 150, 175 };

    /// <summary>Palettenplatz 154 aus <c>DATA/01.PAL</c> — die Hervorhebung.</summary>
    public static readonly Color Hervorhebung = Color.Color8(238, 46, 56);

    /// <summary>Knopf 1…4 gedrückt — der Wirt setzt die Vorbedingungen um.</summary>
    public Action<int>? OnKnopf;
    public Action? OnClose;

    private BuildingWindow.Stand? _stand;
    private int _gedrueckt = -1;
    private bool _zieht;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    /// <summary>Für den Prüfstand.</summary>
    public int LetzterTreffer { get; private set; } = -99;
    public string StatusZeile { get; private set; } = "";
    public string LagerZeile { get; private set; } = "";
    public bool StatusHervorgehoben { get; private set; }

    public FabrikView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    public void Neu() => _gedrueckt = -1;

    public void Zeige(BuildingWindow.Stand s)
    {
        _stand = s;
        Rechne(s);
        QueueRedraw();
    }

    /// <summary>Sinnbild der Fabrikart (0x501CA8/A4/A0).</summary>
    public static string Sinnbild(int art) => art switch { 2 => " ]", 3 => " [", 4 => " {", _ => "" };

    /// <summary>Die Zeilen 9 und 10, wörtlich wie <c>0x46F121…0x46F66E</c> —
    /// herausgezogen, damit der Prüfstand dieselbe Rechnung liest, die gemalt
    /// wird.</summary>
    private void Rechne(BuildingWindow.Stand s)
    {
        int lager = s.FabrikArt switch { 2 => s.StockW, 3 => s.StockF, _ => s.StockS };
        string kopf = s.FabrikArt switch
        {
            2 => "Waffen gelagert : ]",
            3 => "Fahrwerke gelagert : [",
            _ => "Spezialteile gelagert : {",
        };
        Voll = s.Kapazitaet <= lager;                                   // @0x46F1B4
        LagerZeile = kopf + lager + "/" + s.Kapazitaet;

        string wort = s.Zustand switch
        {
            0 => (s.StromSoll != 0 ? (100 * s.StromIst / s.StromSoll).ToString() : "0") + "% Produktion",
            1 => "angehalten",
            2 => "reparieren",
            3 => "vergrößern des Lagers " + s.Fortschritt + "%",
            4 => "verbessern " + s.Fortschritt + "%",
            _ => "",
        };
        StatusHervorgehoben = (s.StockT == 0 || Voll) && s.Zustand == 0;  // @0x46F612
        StatusZeile = StatusHervorgehoben ? "Status : angehalten" : "Status : " + wort;
    }

    public bool Voll { get; private set; }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    public override void _Draw()
    {
        var s = _stand;
        var font = WindowChrome.LegacyFont;
        if (s == null || WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);
        Text(font, 10, 2, Sinnbild(s.FabrikArt) + " Fabrik " + s.Name, WindowChrome.TitleColour);

        Text(font, TextX, 23, "Energie :", WindowChrome.TextColour);
        DrawRect(new Rect2(BarX * Scale, BarY * Scale, BarW * Scale, BarH * Scale), new Color(0, 0, 0), true);
        if (s.HpMax != 0)
            DrawRect(new Rect2((BarX + 4) * Scale, (BarY + 4) * Scale,
                               152 * Mathf.Clamp(s.Hp, 0, s.HpMax) / s.HpMax * Scale, 2 * Scale),
                     WindowChrome.LineColour, true);

        Text(font, TextX, 40, "Strom : " + s.StromIst + "/" + s.StromSoll, WindowChrome.TextColour);
        Text(font, TextX, 70, LagerZeile, Voll ? Hervorhebung : WindowChrome.TextColour);
        Text(font, TextX, 55, StatusZeile, StatusHervorgehoben ? Hervorhebung : WindowChrome.TextColour);
        Text(font, TextX, 85, "Terranium gelagert : " + s.StockT,
             s.StockT == 0 ? Hervorhebung : WindowChrome.TextColour);
        Text(font, TextX, 100, "Produktionsgeschwindigkeit : " + s.Tempo, WindowChrome.TextColour);
        Text(font, TextX, 115, "Lagerausbaukosten : " + s.AusbauKosten, WindowChrome.TextColour);
        Text(font, TextX, 130, "Produktionserweiterungskosten : " + s.ProdKosten, WindowChrome.TextColour);

        for (int k = 0; k < 4; k++)
        {
            string wort = k switch
            {
                0 => "Lagerausbau",
                1 => s.Zustand == 1 ? "Start" : "Anhalten",           // @0x46F9C3
                2 => "Verbessern",
                _ => "Reparieren",
            };
            bool unten = _gedrueckt == k + 1;
            WindowChrome.PaintButton(this, KnopfX[k], KnopfY[k], ButtonTiles, Scale, unten);
            float wpx = font.GetStringSize(wort, HorizontalAlignment.Left, -1, FontSize).X;
            int bx = KnopfX[k] + (int)((ButtonTiles * WindowChrome.Cell * Scale - wpx) / 2 / Scale);
            Text(font, bx, KnopfY[k] + (unten ? 4 : 3), wort, WindowChrome.TextColour);
        }

        Text(font, TextX, 200, "Kontostand : " + s.Geld, WindowChrome.TextColour);
    }

    /// <summary>Treffertest <c>0x45E399</c>: −5 Titel, −2 Kreuz, −1 ausserhalb,
    /// 1 Lagerausbau, 2 Start/Anhalten, 3 Verbessern, 4 Reparieren, sonst 0.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (y < 20) return x < w - 20 ? -5 : -2;
        for (int k = 0; k < 4; k++)
            if (x >= KnopfX[k] && x < KnopfX[k] + 100 && y >= KnopfY[k] && y < KnopfY[k] + 20)
                return k + 1;
        return 0;
    }

    public Rect2 KnopfAufDemSchirm(int treffer)
    {
        int k = treffer - 1;
        var lokal = new Rect2(KnopfX[k] * Scale, KnopfY[k] * Scale, 100 * Scale, 20 * Scale);
        return new Rect2(GetGlobalRect().Position + lokal.Position, lokal.Size);
    }

    public override void _Input(InputEvent @event)
    {
        if (_gedrueckt > 0 && @event is InputEventMouseButton { Pressed: true })
        {
            _gedrueckt = -1;
            QueueRedraw();
        }
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm)
        {
            if (GetParent() is Control eltern)
            {
                eltern.Position += mm.Relative;
                var vp = GetViewportRect().Size;
                eltern.Position = new Vector2(
                    Mathf.Clamp(eltern.Position.X, 0, Mathf.Max(0, vp.X - eltern.Size.X)),
                    Mathf.Clamp(eltern.Position.Y, 0, Mathf.Max(0, vp.Y - eltern.Size.Y)));
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
            WindowManager.Elementklang();                              // 0x4142D7
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
        WindowManager.Elementklang();                                  // 0x448603
        if (t == -2) { OnClose?.Invoke(); return; }
        _gedrueckt = t;                                                // dword[W+0xACA4+4t] := 1
        OnKnopf?.Invoke(t);
        QueueRedraw();
    }

    /// <summary>Die Hilfezeilen 0x20…0x23 (Tafel <c>0x4F0280</c>).</summary>
    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) switch
        {
            1 => "Vergrößern der Lagerkapazität",
            2 => "Produktion einstellen",
            3 => "Verbessern der Produktionsgeschwindigkeit der Fabrik",
            4 => "Reparieren der Fabrik",
            _ => "",
        };
}
