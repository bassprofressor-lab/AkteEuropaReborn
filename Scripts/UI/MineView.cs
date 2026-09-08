namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>»TERRANIUM-MINE«</b> — das Gebäudefenster der Mine, <b>Fensterart 18</b>
/// des Originals, <b>mit dessen eigenen Kacheln</b>.
///
/// <para>⚠⚠ 08.09.2026, seine Meldung: »die mine hat ein eigenes tolles
/// gebäudemenu, wir nutzen dort wieder unser eigenbau, das muss auch angepasst
/// werden«. Dieselbe Lehre wie beim Routenfenster, beim Gruppenfenster und beim
/// Kaufmenü des Nachschubpostens: <b>ein Fenster wird mit den Kacheln des
/// Originals gebaut, nicht mit Godot-Möbeln.</b> Vorbild ist
/// <see cref="SupplyShopView"/>.</para>
///
/// <para><b>Wie es gefunden wurde — über den Verteiler, nicht geraten.</b>
/// Der Klickverteiler »Fenster dieses Gebäudes öffnen« steht auf
/// <c>0x437140</c> und springt über die Tafel <c>0x4379F0</c> (Index =
/// Gebäudeart − 1). <b>Die Arten 10 und 15 teilen sich den Arm
/// <c>0x43725F</c></b>, der über den Sprungtisch <c>0x402167</c> auf
/// <c>0x4438A0</c> führt. Dort steht die Doppelöffnungssperre
/// (<c>cmp al, 0x12</c> @0x4438DD), und der Anleger <c>0x459530</c> schreibt
/// den Fenstersatz:</para>
/// <code>
///   0x4595A9  mov byte  [edx], 0x12                ; FENSTERART 18
///   0x4595BF  mov word  [ebx+0x8B903E], 0x104      ; BREITE  260 = 13 Kacheln
///   0x4595C8  mov word  [ebx+0x8B9040], 0x0F0      ; HOEHE   240 = 12 Kacheln
///   0x4595D1  mov word  [ebx+0x8B9044], dx         ; das GEBAEUDE
/// </code>
///
/// <para><b>Der Zeichner ist <c>0x474220</c></b> (Zeichnertafel
/// <c>0x487888</c>, Platz 18−1 → Sprungtisch → <c>0x474220</c>). Aus ihm sind
/// alle Koordinaten hier abgelesen, Punkt für Punkt:</para>
/// <code>
///   Titel  "Terranium-Mine "                       (0x502014, @0x47422F)
///   (20, 23)  "Energie :"        Balken (80,25) 160x10, Fuellung ab (84,29)
///   (20, 40)  "Strom : "  a "/" b
///   (20, 55)  "Status : " ...
///   (20, 70)  "Rohstoffvorkommen: " ...
///   (20, 85)  "Terranium gelagert : "
///   (20,100)  "Produktionsgeschw. : "  "%"
///   (20,115)  "Lagerausbaukosten : "
///   (20,130)  "Minenerweiterungskosten : "
///   Knopf (20,150) "Ausbau"        (135,150) "Verbessern"
///   Knopf (20,175) "Start"/"Anhalten"  (135,175) "Reparatur"
///   (20,200)  "Kontostand : "
/// </code>
///
/// <para>Die sieben Stufen des Vorkommens stehen ebenfalls wörtlich in der EXE
/// (<c>0x501FA4…0x501FE4</c>): <b>komplett abgebaut, kaum mehr, sehr gering,
/// gering, mittel, hoch, sehr hoch</b>; die fünf Zustände sind
/// <b>… % produzieren, angehalten, reparieren, Lagerausbau …, Verbesserung
/// …</b>.</para>
///
/// <para>⚠ <b>Was wir NICHT haben, steht in der Zeile selbst.</b> Das Original
/// zeigt »Strom : a/b« mit zwei Zahlen; eine Stromabrechnung JE GEBÄUDE führen
/// wir nicht (nur die des Spielers, @0x440270). Die Zeile zeigt darum
/// <c>an</c>/<c>aus</c> — und sagt damit weniger, statt eine Zahl zu erfinden.
/// Dasselbe gilt für die Minenerweiterungskosten, die bei uns keinen eigenen
/// Preis haben.</para>
///
/// <para>⚠ <b>Was NICHT gebaut ist, steht am Knopf.</b> »Ausbau« und
/// »Verbessern« sind im Original vorhanden, bei uns aber ohne Mechanik — sie
/// stehen gedimmt da und sagen es im Hinweistext. Ein Knopf, der stumm nichts
/// tut, ist von einem kaputten nicht zu unterscheiden.</para>
/// </summary>
public sealed partial class MineView : Control
{
    /// <summary>13 x 12 Kacheln — die 260 x 240 aus <c>0x4595BF</c>/
    /// <c>0x4595C8</c>.</summary>
    public const int WTiles = 13, HTiles = 12;

    /// <summary>Wie bei allen unseren Fenstern: doppelt, damit die 20er-Kachel
    /// auf heutigen Schirmen lesbar bleibt. UNSERE Setzung.</summary>
    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;

    /// <summary>Die neun Zeilen des Zeichners, alle auf x = 20.</summary>
    private const int TextX = 20;
    private const int YEnergie = 23, YStrom = 40, YStatus = 55, YVorkommen = 70,
                      YLager = 85, YTempo = 100, YAusbau = 115, YErweiter = 130,
                      YKonto = 200;

    /// <summary>Der Energiebalken: Rahmen <c>0x4021DA(80, 25, 160, 10, 0)</c>
    /// @0x474340, Füllung ab <c>(84, 29)</c> @0x47439E.</summary>
    private const int BarX = 80, BarY = 25, BarW = 160, BarH = 10;

    /// <summary>Die vier Knöpfe, <c>0x401820</c> @0x474B8F, @0x474BBE,
    /// @0x474C17 und @0x474C4B. Das vierte Argument ist die Breite in
    /// Kacheln: <b>5</b>.</summary>
    private const int ButtonTiles = 5;
    private static readonly int[] ButtonX = { 20, 135, 20, 135 };
    private static readonly int[] ButtonY = { 150, 150, 175, 175 };

    /// <summary>Die sieben Stufen aus <c>0x501FA4…0x501FE4</c>, von leer nach
    /// voll.</summary>
    private static readonly string[] Stufen =
    {
        "komplett abgebaut", "kaum mehr", "sehr gering", "gering",
        "mittel", "hoch", "sehr hoch",
    };

    public Action? OnClose;
    public Action? OnStart;
    public Action? OnStop;
    public Action? OnRepair;

    private BuildingWindow.Stand? _stand;
    private int _held = -1;
    private bool _zieht;

    /// <summary>Wie viele Knöpfe zuletzt bedienbar waren — für den
    /// Prüfstand.</summary>
    public int Buttons { get; private set; }

    public static bool Usable => WindowChrome.Atlas != null;

    public MineView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    public void Zeige(BuildingWindow.Stand s)
    {
        _stand = s;
        // ⚠ HIER und nicht erst im Zeichner: der Pruefstand liest die Zahl,
        // bevor ein Bild gelaufen ist, und meldete darum »0 Knoepfe«.
        // Bedienbar sind die zwei unteren (Start/Anhalten, Reparatur); die
        // zwei oberen zeigt das Original, ihre Mechanik gibt es bei uns nicht.
        Buttons = 2;
        QueueRedraw();
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    /// <summary>Die Stufe des Vorkommens, wie das Original sie benennt.
    /// ⚠ UNSERE Setzung ist die EINTEILUNG: der Zeichner rechnet sie aus dem
    /// Verhältnis zum Anfangsbestand, welche Schwellen er nimmt, ist nicht
    /// gelesen. Sieben gleich breite Stufen sind das Einfachste, was die sieben
    /// Wörter überhaupt benutzt; »komplett abgebaut« ist dabei nicht eine
    /// Stufe, sondern die Null.</summary>
    private static string StufeVon(int rest, int start)
    {
        if (rest <= 0) return Stufen[0];
        if (start <= 0) return Stufen[^1];
        int k = 1 + 5 * Mathf.Min(rest, start) / start;      // 1..6
        return Stufen[Mathf.Clamp(k, 1, Stufen.Length - 1)];
    }

    public override void _Draw()
    {
        var s = _stand;
        var font = WindowChrome.LegacyFont;
        if (s == null || WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);

        // Der Titel steht so in der EXE (0x502014) — mit dem Leerzeichen am
        // Ende, hinter das das Original den Namen setzt.
        Text(font, TitleX, TitleY, "Terranium-Mine " + s.Name, WindowChrome.TitleColour);

        Text(font, TextX, YEnergie, "Energie :", WindowChrome.TitleColour);
        // Rahmen und Fuellung wie @0x474340 / @0x47439E.
        DrawRect(new Rect2(BarX * Scale, BarY * Scale, BarW * Scale, BarH * Scale),
                 new Color(0, 0, 0), true);
        if (s.HpMax > 0)
        {
            int voll = (BarW - 8) * Mathf.Clamp(s.Hp, 0, s.HpMax) / s.HpMax;
            DrawRect(new Rect2((BarX + 4) * Scale, (BarY + 4) * Scale,
                               voll * Scale, (BarH - 8) * Scale),
                     WindowChrome.TitleColour, true);
        }

        // ⚠ »Strom : a/b« im Original; wir haben je Gebaeude keine Zahl —
        // siehe Kopf.
        Text(font, TextX, YStrom, $"Strom : {(s.Laeuft ? "an" : "aus")}",
             WindowChrome.TextColour);
        Text(font, TextX, YStatus, $"Status : {s.Status}", WindowChrome.TextColour);
        Text(font, TextX, YVorkommen,
             "Rohstoffvorkommen: " + StufeVon(s.Vorkommen, s.VorkommenStart),
             WindowChrome.TextColour);
        Text(font, TextX, YLager, $"Terranium gelagert : {s.StockT}",
             WindowChrome.TextColour);
        Text(font, TextX, YTempo, $"Produktionsgeschw. : {Mathf.Max(0, s.Grad)}%",
             WindowChrome.TextColour);
        Text(font, TextX, YAusbau, $"Lagerausbaukosten : {s.AusbauKosten}",
             WindowChrome.TextColour);
        Text(font, TextX, YErweiter, "Minenerweiterungskosten : —",
             WindowChrome.TextColour);

        Buttons = 0;
        for (int i = 0; i < 4; i++)
        {
            string wort = i switch
            {
                0 => "Ausbau",
                1 => "Verbessern",
                2 => s.Laeuft ? "Anhalten" : "Start",
                _ => "Reparatur",
            };
            bool geht = i >= 2;
            bool held = _held == i;
            WindowChrome.PaintButton(this, ButtonX[i], ButtonY[i], ButtonTiles, Scale, held);
            float wpx = font.GetStringSize(wort, HorizontalAlignment.Left, -1, FontSize).X;
            int bx = ButtonX[i] + (int)((ButtonTiles * WindowChrome.Cell * Scale - wpx)
                                        / 2 / Scale);
            var col = WindowChrome.TextColour;
            if (!geht) col = new Color(col.R, col.G, col.B, 0.45f);
            Text(font, bx, ButtonY[i] + (held ? 4 : 3), wort, col);
            if (geht) Buttons++;
        }

        Text(font, TextX, YKonto, $"Kontostand : ${s.Geld}", WindowChrome.TextColour);
    }

    /// <summary>Das Feld eines Knopfes auf dem SCHIRM — der Prüfstand fragt
    /// damit dieselbe Rechnung, die auch <see cref="Hit"/> benutzt.</summary>
    public Rect2? KnopfFeld(int k)
    {
        if (k < 0 || k >= 4) return null;
        var lokal = new Rect2(ButtonX[k] * Scale, ButtonY[k] * Scale,
                              ButtonTiles * WindowChrome.Cell * Scale, 20 * Scale);
        return new Rect2(GetGlobalRect().Position + lokal.Position, lokal.Size);
    }

    /// <summary>Das Schliesskreuz gibt −2, die vier Knöpfe 1..4, sonst 0
    /// (ziehen) — dieselbe Ordnung wie <see cref="RouteWindow.Hit"/>.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;
        for (int i = 0; i < 4; i++)
            if (x >= ButtonX[i] && x < ButtonX[i] + ButtonTiles * WindowChrome.Cell
                && y >= ButtonY[i] && y < ButtonY[i] + 20) return i + 1;
        return 0;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm)
        {
            var eltern = GetParent() as Control;
            if (eltern != null)
            {
                eltern.Position += mm.Relative;
                var vp = GetViewportRect().Size;
                eltern.Position = new Vector2(
                    Mathf.Clamp(eltern.Position.X, 0, Mathf.Max(0, vp.X - eltern.Size.X)),
                    Mathf.Clamp(eltern.Position.Y, 0, Mathf.Max(0, vp.Y - eltern.Size.Y)));
            }
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
            if (t == 0) _zieht = true;
            AcceptEvent();
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
            case 3: if (_stand is { Laeuft: true }) OnStop?.Invoke(); else OnStart?.Invoke(); break;
            case 4: OnRepair?.Invoke(); break;
        }
    }

    /// <summary>Was das Original am Knopf sagen würde. ⚠ Bei den zwei Knöpfen
    /// ohne Mechanik steht der Grund da, nicht nichts.</summary>
    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) switch
        {
            1 => "Der Ausbau der Mine ist bei uns nicht gebaut.",
            2 => "Die Verbesserung der Mine ist bei uns nicht gebaut.",
            3 => "Die Foerderung anhalten oder wieder aufnehmen",
            4 => "Die Mine reparieren",
            _ => "",
        };
}
