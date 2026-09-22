namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»BAHNHOF«</b> — das Gebäudefenster der Gebäudearten 6 (Bahnstation) und 12
/// (Feldbahnhof), <b>Fensterart 2</b> des Originals, mit dessen Kacheln
/// (22.09.2026).
///
/// <para>Gemeldet aus Kampagne 21: »die bahnhöfe nutzen wieder unser eigenbau
/// wenn man drauf klickt. auch das transportsystem muss funktionieren«. Es war
/// kein Rückfall — das Fenster war seit dem 21.08. ein Godot-Eigenbau mit zwei
/// gesperrten Knöpfen. Lesung <c>berichte/bahnhof-transport-fable.md</c> §2.</para>
///
/// <para><b>Öffner</b> <c>0x4445D0</c> (Art 6) / <c>0x444680</c> (Art 12), F
/// <c>0x443670</c>. <b>Anleger</b> <c>0x4575F0</c>: Art 2, <b>300 × 280</b>.
/// <b>Zeichner <c>0x463FB0</c></b> (F <c>0x462860</c>):</para>
/// <code>
///   (10,  2)  Name + " Bahnhof"                         0x501758
///   (20, 23)  "Energie :"  Balken (80,25) 200x10,       Fuellung (84,29) 192*Hp/HpMax x 2, 0x8C
///   (20, 43)  "Im Lager :"
///   (80, 43/58/73/88)  "Waffen" "Fahrwerk" "Spezial" "Terranium"
///             Werte rechtsbuendig an x = 280, mit Sinnbild ] [ { ^
///   (20,105)  Knopf "Transportsystem", 13 Kacheln
///   (20,130)  Innenrahmen 13 x 5 Kacheln
///   (35,136+15i)  bis 6 Insassen; markiert: Balken (30,y) 235x14, 0x8C
///             Rang + Name + ("(ausgesandt)" | "   " + Hp% + "%")
///   (20,235)  Knopf "Aussenden", 6 Kacheln      (160,235) "Transportieren", 6 Kacheln
/// </code>
/// <para><b>Treffertest <c>0x45CCD6</c></b>: Titel −5, Kreuz −2, (20,105,260×20) → 1,
/// (20,235,120×20) → 2, (160,235,120×20) → 3, Zeile i (35,130+15i,220×20) → 4+i
/// solange belegt. <b>Druckarm <c>0x448C4B</c></b>: 1 → Karte in Betriebsart 1;
/// 2 → Befehl 504 je Markierung, sonst »Sie haben keine Einheit gewählt!«;
/// 3 → Karte in Betriebsart 5, ohne Markierung dieselbe Meldung; 4…9 Marke
/// umschalten.</para>
///
/// <para>⚠ UNSERE Setzung: Massstab 2; Markierungen hängen an der Einheit (wie
/// im Depot); der gedrückte Knopf löst sich beim nächsten Klick.</para>
/// </summary>
public sealed partial class BahnhofView : Control
{
    public const int WTiles = 15, HTiles = 14;
    public const int Scale = 2;
    public const int Plaetze = 6;

    private const int ZeileX = 35, ZeileY0 = 136, Schritt = 15;
    private const int TrefferX = 35, TrefferY0 = 130, TrefferW = 220, TrefferH = 20;

    public Action? OnClose;
    public Action? OnChanged;
    public Action? OnTransportsystem;
    public Action<List<int>>? OnAussenden;
    public Action<List<int>>? OnTransportieren;
    /// <summary>»Sie haben keine Einheit gewählt!« (0x4FBB78).</summary>
    public Action<string>? OnMeldung;

    private BuildingWindow.Stand? _stand;
    private readonly HashSet<int> _marken = new();
    private int _gedrueckt;
    private bool _zieht;

    public int Zeilen => Mathf.Min(_stand?.DepotZeilen.Count ?? 0, Plaetze);
    public int Markiert => _marken.Count;
    public int Klicks { get; private set; }
    public int LetzterTreffer { get; private set; } = -99;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    public BahnhofView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    public void Neu() { _marken.Clear(); _gedrueckt = 0; }

    public void Zeige(BuildingWindow.Stand s)
    {
        _stand = s;
        var da = new HashSet<int>();
        foreach (var z in s.DepotZeilen) da.Add(z.Griff);
        _marken.RemoveWhere(g => !da.Contains(g));
        QueueRedraw();
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    private void Rechteck(int x, int y, int w, int h, Color c)
        => DrawRect(new Rect2(x * Scale, y * Scale, w * Scale, h * Scale), c, true);

    /// <summary>Rechtsbündig an <paramref name="rechts"/>, Sinnbild in seiner Farbe.</summary>
    private void TextRechtsSinn(Font f, int rechts, int y, string t)
    {
        var teile = BaseWindow.ZerlegeSinnbilder(t, WindowChrome.TextColour);
        float breite = 0f;
        foreach (var (st, _) in teile) breite += f.GetStringSize(st, HorizontalAlignment.Left, -1, FontSize).X;
        float px = rechts * Scale - breite;
        foreach (var (st, col) in teile)
        {
            DrawString(f, new Vector2(px, y * Scale + f.GetAscent(FontSize)), st,
                       HorizontalAlignment.Left, -1, FontSize, col);
            px += f.GetStringSize(st, HorizontalAlignment.Left, -1, FontSize).X;
        }
    }

    private void Knopf(Font f, int x, int y, int tiles, string wort, bool gedrueckt)
    {
        WindowChrome.PaintButton(this, x, y, tiles, Scale, gedrueckt);
        float wpx = f.GetStringSize(wort, HorizontalAlignment.Left, -1, FontSize).X;
        int bx = x + (int)((tiles * WindowChrome.Cell * Scale - wpx) / 2 / Scale);
        Text(f, bx, y + (gedrueckt ? 4 : 3), wort, WindowChrome.TextColour);
    }

    /// <summary>Die Zeile, wie <c>0x4645C8…0x4647A8</c> sie baut.</summary>
    public static string ZeilenText(Font? font, BuildingWindow.DepotZeile z)
    {
        string kopf = UnitListView.RangText(font, z.Rang) + z.Name;
        if (z.Ausgesandt) return kopf + "(ausgesandt)";              // 0x5016C0
        int prozent = z.HpMax > 0 ? 100 * Mathf.Max(0, z.Hp) / z.HpMax : 0;
        return kopf + "   " + prozent + "%";                          // 0x5016B8
    }

    public override void _Draw()
    {
        var s = _stand;
        var font = WindowChrome.LegacyFont;
        if (s == null || WindowChrome.Atlas == null || font == null) return;
        WindowChrome.Paint(this, WTiles, HTiles, Scale);
        Text(font, 10, 2, s.Name + " Bahnhof", WindowChrome.TitleColour);

        Text(font, 20, 23, "Energie :", WindowChrome.TextColour);
        Rechteck(80, 25, 200, 10, new Color(0, 0, 0));
        if (s.HpMax > 0)
            Rechteck(84, 29, 192 * Mathf.Clamp(s.Hp, 0, s.HpMax) / s.HpMax, 2, WindowChrome.LineColour);

        Text(font, 20, 43, "Im Lager :", WindowChrome.TextColour);
        string[] namen = { "Waffen", "Fahrwerk", "Spezial", "Terranium" };
        string[] sinn = { BaseWindow.IconW, BaseWindow.IconF, BaseWindow.IconS, "^" };
        int[] werte = { s.StockW, s.StockF, s.StockS, s.StockT };
        for (int k = 0; k < 4; k++)
        {
            int y = 43 + 15 * k;
            Text(font, 80, y, namen[k], WindowChrome.TextColour);
            TextRechtsSinn(font, 280, y, sinn[k] + werte[k]);
        }

        Knopf(font, 20, 105, 13, "Transportsystem", _gedrueckt == 1);
        WindowChrome.PaintInnerFrame(this, 20, 130, 13, 5, Scale);
        for (int i = 0; i < Zeilen; i++)
        {
            var z = s.DepotZeilen[i];
            int y = ZeileY0 + Schritt * i;
            if (_marken.Contains(z.Griff)) Rechteck(30, y, 235, 14, WindowChrome.LineColour);
            Text(font, ZeileX, y, ZeilenText(font, z), WindowChrome.TextColour);
        }
        Knopf(font, 20, 235, 6, "Aussenden", _gedrueckt == 2);
        Knopf(font, 160, 235, 6, "Transportieren", _gedrueckt == 3);
    }

    /// <summary>Der Treffertest <c>0x45CCD6</c>.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (y < 20) return x < w - 20 ? -5 : -2;
        if (x >= 20 && x < 280 && y >= 105 && y < 125) return 1;
        if (x >= 20 && x < 140 && y >= 235 && y < 255) return 2;
        if (x >= 160 && x < 280 && y >= 235 && y < 255) return 3;
        for (int i = 0; i < Zeilen; i++)
        {
            int oben = TrefferY0 + Schritt * i;
            if (x >= TrefferX && x < TrefferX + TrefferW && y >= oben && y < oben + TrefferH) return 4 + i;
        }
        return 0;
    }

    /// <summary>Mitte eines Treffers auf dem SCHIRM — für den Prüfstand.</summary>
    public Vector2 FeldAufDemSchirm(int t)
    {
        Vector2 lokal = t switch
        {
            1 => new Vector2(150, 115),
            2 => new Vector2(80, 245),
            3 => new Vector2(220, 245),
            _ => new Vector2(TrefferX + 60, TrefferY0 + Schritt * (t - 4) + 8),
        };
        return GetGlobalRect().Position + lokal * Scale;
    }

    public override void _Input(InputEvent @event)
    {
        if (_gedrueckt != 0 && @event is InputEventMouseButton { Pressed: true })
        { _gedrueckt = 0; QueueRedraw(); }
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm)
        {
            if (GetParent() is Control eltern) eltern.Position += mm.Relative;
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
            WindowManager.Elementklang();
            OnClose?.Invoke();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        AcceptEvent();
        if (!mb.Pressed) { _zieht = false; return; }
        int t = Hit(mb.Position);
        if (t == -5) { _zieht = true; return; }
        Druecke(t);
    }

    /// <summary>Der Druckarm <c>0x448C4B</c> — auch für den Prüfstand.</summary>
    public void Druecke(int t)
    {
        LetzterTreffer = t;
        if (t is 0 or -1 or -5) return;
        Klicks++;
        WindowManager.Elementklang();                         // 0x448603
        if (t == -2) { OnClose?.Invoke(); return; }
        var s = _stand;
        if (s == null) return;
        var griffe = new List<int>();
        for (int i = 0; i < Zeilen; i++)
            if (_marken.Contains(s.DepotZeilen[i].Griff)) griffe.Add(s.DepotZeilen[i].Griff);
        switch (t)
        {
            case 1:
                _gedrueckt = 1;
                OnTransportsystem?.Invoke();
                break;
            case 2:
                _gedrueckt = 2;
                if (griffe.Count == 0) OnMeldung?.Invoke("Sie haben keine Einheit gewählt!");
                else OnAussenden?.Invoke(griffe);
                break;
            case 3:
                _gedrueckt = 3;
                if (griffe.Count == 0) OnMeldung?.Invoke("Sie haben keine Einheit gewählt!");
                else OnTransportieren?.Invoke(griffe);
                break;
            default:
                int i2 = t - 4;
                if (i2 >= 0 && i2 < Zeilen)
                {
                    int g = s.DepotZeilen[i2].Griff;
                    if (!_marken.Remove(g)) _marken.Add(g);
                }
                break;
        }
        OnChanged?.Invoke();
        QueueRedraw();
    }

    /// <summary>Die Hilfezeilen 1/2/3/88 (Tafel <c>0x4F0280</c>).</summary>
    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) switch
        {
            1 => "Setzen der Transportwege",
            2 => "Aussenden der angewählten Einheiten",
            3 => "Transportieren der angewählten Einheiten",
            >= 4 => "Auflistung der gelagerten Einheiten",
            _ => "",
        };
}
