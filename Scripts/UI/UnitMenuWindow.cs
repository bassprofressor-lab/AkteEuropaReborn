namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>DAS EINHEITENMENÜ — Fensterart 1</b>, das Fenster mit den sechs Symbolen,
/// <b>mit den Kacheln des Originals</b>.
///
/// <para>⚠⚠ 08.09.2026, seine Meldung: »ebenso dieses Rechtsklick auf die
/// Einheit wie ich erwähnt habe, das will ich alles haben«. Gebaut nach
/// <c>berichte/transporterroute-fable.md</c>, Teil 2.</para>
///
/// <para><b>⚠ Eine Berichtigung zur Bedienung, und sie ist gelesen:</b> das
/// Menü hängt <b>nicht</b> an der rechten Maustaste. Der Nachrichtenverteiler
/// (<c>0x412ED6</c>, Tafel <c>0x41458C</c>) schickt <b>WM_LBUTTONDBLCLK</b>
/// (<c>0x4141B4</c>) und die <b>Leertaste</b> (<c>0x413069</c>) nach
/// <c>0x444490</c>; die rechte Taste landet bei <c>0x414328</c> und
/// <b>scrollt die Karte</b>. Bei uns öffnet der Doppelklick — die Leertaste
/// springt weiterhin zur Auswahl, das ist gewachsene Bedienung.</para>
///
/// <para><b>Die Masse und die Kacheln sind die gelesenen:</b></para>
/// <code>
///   Grundmass 80x80, breiter je nach belegten Plaetzen; hier 160x120
///   acht Plaetze: (20,20) (60,20) (100,20) (140,20)
///                 (20,60) (60,60) (100,60) (140,60)
///   je Platz vier Kacheln 0x41 + 4*code .. 0x44 + 4*code, 2x2 zu 20 Punkten
///   Element = ((my-y-20)/40)*4 + (mx-x-20)/40 + 1        (0x45CBA9)
///   Schliesskreuz (x+w-20, y, 20x20) -> -2
/// </code>
///
/// <para><b>Die Codes</b> stellt <c>0x441810</c> zusammen; für den
/// Materialtransporter (Turmaufsatz <c>+0x0C == 0x2E</c>) ergibt das sechs
/// Symbole: <c>0x10</c> Transportzyklus · 1 Bewegen · 2 Beschützen bzw.
/// <c>0x1A</c> Anhalten · 3 Selbstzerstörung · 6 Einheiteninformation ·
/// 5 Handsteuerung.</para>
///
/// <para>⚠ <b>Was davon WIRKT:</b> »Transportzyklus einstellen« und
/// »Anhalten«. Die vier anderen zeichnet das Menü mit den richtigen Kacheln,
/// aber sie sagen beim Druck, dass sie noch nicht gebaut sind — ein Symbol, das
/// stillschweigend nichts tut, wäre schlimmer als eine ehrliche Zeile.</para>
/// </summary>
public sealed partial class UnitMenuWindow : Control
{
    /// <summary>8 x 6 Kacheln — die 160 x 120 des Originals.</summary>
    public const int WTiles = 8, HTiles = 6, Scale = 2;

    /// <summary>Die acht Plätze aus dem Zeichner <c>0x463D60</c>.</summary>
    private static readonly (int X, int Y)[] Slots =
    {
        (20, 20), (60, 20), (100, 20), (140, 20),
        (20, 60), (60, 60), (100, 60), (140, 60),
    };

    /// <summary>Die Kachel des Symbols: <c>0x41 + 4·code</c> (<c>0x457300</c>).
    /// </summary>
    private const int IconBase = 0x41;

    /// <summary>Der Aktionscode je Platz, −1 = leer. Wird von aussen gesetzt.
    /// </summary>
    public int[] Codes = { -1, -1, -1, -1, -1, -1, -1, -1 };

    /// <summary>Was ein Code bedeutet — die Wörter der Befehlsliste des
    /// Originals (<c>0x4FD660</c>, 30-Byte-Raster).</summary>
    public static string CodeWort(int code) => code switch
    {
        0 => "Angreifen",
        1 => "Bewegen",
        2 => "Beschuetzen",
        3 => "Selbstzerstoerung",
        4 => "Verkaufen",
        5 => "Handsteuerung",
        6 => "Einheiteninformation",
        7 => "Eingraben",
        8 => "Ausgraben",
        0x10 => "Transportzyklus einstellen",
        0x1A => "Anhalten",
        _ => $"Befehl {code}",
    };

    /// <summary>Der Druck auf ein Symbol — der Parameter ist der Aktionscode.
    /// </summary>
    public Action<int>? OnCode;
    public Action? OnClose;

    private int _held = -1;

    public static bool Usable => WindowChrome.Atlas != null;

    public UnitMenuWindow()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Draw()
    {
        if (WindowChrome.Atlas == null) return;
        WindowChrome.Paint(this, WTiles, HTiles, Scale);

        for (int i = 0; i < Slots.Length; i++)
        {
            int c = i < Codes.Length ? Codes[i] : -1;
            if (c < 0) continue;
            // ⚠ Die REIHENFOLGE der vier Kacheln ist unsere Setzung: der
            // Bericht nennt »0x41 + 4·code .. 0x44 + 4·code, 2x2 à 20 px«, aber
            // nicht, welche Kachel wohin geht. Genommen ist die naheliegende
            // Lesart links-oben, rechts-oben, links-unten, rechts-unten. Sieht
            // ein Symbol verdreht aus, ist DAS die Stelle.
            int t = IconBase + 4 * c;
            var (x, y) = Slots[i];
            WindowChrome.Tile(this, t + 0, x, y, Scale);
            WindowChrome.Tile(this, t + 1, x + WindowChrome.Cell, y, Scale);
            WindowChrome.Tile(this, t + 2, x, y + WindowChrome.Cell, Scale);
            WindowChrome.Tile(this, t + 3, x + WindowChrome.Cell,
                              y + WindowChrome.Cell, Scale);
            if (_held == i)
            {
                // Ein gedruecktes Symbol bekommt einen Rahmen — UNSER Zusatz;
                // das Original zeigt den Druck ueber die Hilfezeile.
                DrawRect(new Rect2(x * Scale, y * Scale, 40 * Scale, 40 * Scale),
                         WindowChrome.TitleColour, false, 2);
            }
        }
    }

    /// <summary>Die Trefferprüfung des Originals (<c>0x45CBA9</c>).</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;      // Schliesskreuz
        if (x < 20 || y < 20 || x >= w - 20 || y >= h - 20) return 0; // Fensterkoerper
        int el = (int)((y - 20) / 40) * 4 + (int)((x - 20) / 40) + 1;
        return el >= 1 && el <= 8 ? el : 0;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left)
            return;
        if (mb.Pressed)
        {
            int t = Hit(mb.Position);
            _held = t >= 1 && t <= 8 && Codes[t - 1] >= 0 ? t - 1 : -1;
            QueueRedraw();
            if (t != 0) AcceptEvent();
            return;
        }
        int hit = Hit(mb.Position);
        _held = -1;
        QueueRedraw();
        if (hit == 0) return;
        AcceptEvent();
        if (hit == -2) { OnClose?.Invoke(); return; }
        int code = Codes[hit - 1];
        if (code >= 0) OnCode?.Invoke(code);
    }

    /// <summary>Die Hilfezeile des Originals (<c>0x447A63</c>: Text
    /// <c>0x4FD660 + 30·code</c>) — bei uns am Mauszeiger.</summary>
    public override string _GetTooltip(Vector2 pos)
    {
        int t = Hit(pos);
        return t >= 1 && t <= 8 && Codes[t - 1] >= 0 ? CodeWort(Codes[t - 1]) : "";
    }

    /// <summary>Das Menü geht dort auf, wo die Maus war — <c>0x444490</c>
    /// bekommt <c>mx, my</c> und legt das Fenster dorthin.</summary>
    public void PlaceAt(Vector2 mausPos)
    {
        var vp = GetViewportRect().Size;
        Position = new Vector2(Mathf.Clamp(mausPos.X, 0, Mathf.Max(0, vp.X - Size.X)),
                               Mathf.Clamp(mausPos.Y, 0, Mathf.Max(0, vp.Y - Size.Y)));
    }
}
