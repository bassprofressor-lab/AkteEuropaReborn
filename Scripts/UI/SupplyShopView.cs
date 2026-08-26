namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// <b>»ANGEBOT DES NACHSCHUBPOSTENS« — Fensterart 31, gezeichnet wie das
/// Original.</b> Gebaut am 25.08.2026 auf die Meldung »Du hast wie ein eigenes
/// Kaufmenü gebaut, das hat nichts mit dem Original zu tun. Da musst du
/// irgendwo die Grafiken finden dazu.«
///
/// <para>Die Grafiken sind gefunden: <c>WINDOWS.CWW</c>, 314 Kacheln zu 20x20
/// (siehe <see cref="WindowChrome"/> und
/// <see cref="AkteEuropaReborn.Import.InterfaceExporter.WriteWindowChrome"/>).
/// Dieses Fenster stellt sie so zusammen, wie der Zeichner <c>0x47D340</c> es
/// tut — mit SEINEN Koordinaten, nicht mit unseren.</para>
///
/// <para><b>Alles, was hier an Zahlen steht, ist gelesen:</b></para>
/// <list type="table">
/// <item><term>Fenstergrösse</term><description><b>260 x 100 = 13 x 5
/// Kacheln</b>, aus dem Öffner der Fensterart 31 (@0x45AC59:
/// <c>mov word[…+0x8B903E], 0x104</c> / <c>[…+0x8B9040], 0x64</c>)</description></item>
/// <item><term>Titel</term><description>»Angebot des Nachschubpostens«
/// (0x5021EC) bei <b>(10, 2)</b>, Farben 0x96/0xa9 — @0x47D3C1..0x47D3E0</description></item>
/// <item><term>linke Spalte</term><description>»Treibstoff-Heli.« (0x5021D8)
/// bei <b>(20, 20)</b> @0x47D3FE, »Kostet : $« (0x5021C8) + <c>word[0x52FAC0]</c>
/// bei <b>(20, 35)</b> @0x47D47F, Knopf »Kaufen« (0x5021C0) bei <b>(20, 50)</b>,
/// <b>4 Kacheln</b> breit @0x47D4A8</description></item>
/// <item><term>rechte Spalte</term><description>»Munitions-Heli.« (0x5021AC)
/// bei <b>(140, 20)</b> @0x47D4C9, Preis <c>word[0x52FAC4]</c> bei
/// <b>(140, 35)</b> @0x47D548, Knopf bei <b>(140, 50)</b> @0x47D579</description></item>
/// <item><term>unten</term><description>»Kontostand : $« (0x50196C) +
/// <c>dword[0xA9C600 + 4·Spieler]</c> bei <b>(20, 75)</b> @0x47D606</description></item>
/// <item><term>Schliesskreuz</term><description>Kachel 13 auf
/// <b>(240, 0)</b> — der Rahmenzeichner setzt sie fest auf (W-1, 0),
/// @0x4561AF</description></item>
/// </list>
///
/// <para>⚠ <b>Was UNSER ist</b> und nicht des Originals: die
/// <see cref="Scale">Vergrösserung um 2</see> (ein 260x100-Fenster wäre auf
/// einem heutigen Schirm eine Briefmarke — dieselbe Setzung wie beim Bedienfeld
/// und der Schrift), und dass ein Knopf ohne Geld dahinter <b>blass</b> statt
/// gedrückt gezeichnet wird. Das Original prüft den Kontostand erst BEIM
/// Klicken (@0x44C2B9: <c>cmp dword[ecx*4 + 0xA9C600], eax</c>) und zeigt dem
/// Spieler bis dahin einen ganz gewöhnlichen Knopf.</para>
///
/// <para>⚠ <b>Ohne eingelesene Inhalte zeichnet dieses Fenster NICHTS</b> —
/// <see cref="WindowChrome.Atlas"/> ist dann null. Das ist Absicht: ein
/// nachgemalter Rahmen wäre genau der Fehler, der gemeldet wurde.
/// <see cref="Usable"/> sagt es dem Rufer, damit er auf die alten Möbel
/// zurückfallen kann.</para>
/// </summary>
public sealed partial class SupplyShopView : Control
{
    /// <summary>Fenstergrösse in Kacheln — 13 x 5, siehe Klassentext.</summary>
    public const int WTiles = 13, HTiles = 5;

    /// <summary>UNSERE Vergrösserung. Ganzzahlig, sonst wird die Bitmapschrift
    /// zu Matsch (dieselbe Regel wie in MapViewer.ApplyLegacyFont).</summary>
    public const int Scale = 2;

    // Die Koordinaten des Originals, in dessen eigenen Punkten.
    private const int TitleX = 10, TitleY = 2;
    private const int NameY = 20, PriceY = 35, ButtonY = 50, BalanceY = 75;
    private const int ButtonTiles = 4;
    private static readonly int[] ColumnX = { 20, 140 };

    public System.Action? OnClose;

    private BuildingWindow.Stand? _stand;

    /// <summary>Welcher Knopf gerade gedrückt gehalten wird: 0/1 = Spalte,
    /// 2 = Schliesskreuz, -1 = keiner.</summary>
    private int _held = -1;

    /// <summary>Wieviele Kaufknöpfe zuletzt gezeichnet wurden — für den
    /// Prüfstand.</summary>
    public int Buttons { get; private set; }

    /// <summary>Ob die Originalgrafiken da sind. Ist das falsch, muss der Rufer
    /// die alte Darstellung zeigen.</summary>
    public static bool Usable => WindowChrome.Atlas != null;

    public override void _Ready()
    {
        // Bitmapkacheln und Bitmapschrift: alles andere als Nearest macht aus
        // 20x20-Kacheln bei ganzzahliger Vergrösserung weiche Ränder.
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
    }

    /// <summary>Nach einem Kauf: der Rufer soll seinen Stand neu holen.</summary>
    public System.Action? OnChanged;

    /// <summary>Den Stand uebernehmen und neu zeichnen. ⚠ NICHT <c>Show</c>
    /// genannt — das ist bei einem Control schon vergeben.</summary>
    public void Zeige(BuildingWindow.Stand stand)
    {
        _stand = stand;
        _held = -1;
        QueueRedraw();
    }

    private int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    public override void _Draw()
    {
        var s = _stand;
        var font = WindowChrome.LegacyFont;
        if (s == null || WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);

        // Der Titel steht so in der EXE (0x5021EC). Dass das Bildschirmfoto
        // Grossbuchstaben zeigt, ist die SCHRIFT: FONT.CWD hat nur Kapitälchen.
        Text(font, TitleX, TitleY, "Angebot des Nachschubpostens",
             WindowChrome.TitleColour);

        Buttons = 0;
        for (int i = 0; i < s.Angebote.Count && i < ColumnX.Length; i++)
        {
            var a = s.Angebote[i];
            int x = ColumnX[i];
            Text(font, x, NameY, a.Name, WindowChrome.TextColour);
            // »Kostet : $« ist die Zeichenkette, die Zahl kommt aus einer
            // Globalen des Spiels — beides getrennt, wie im Original.
            Text(font, x, PriceY, $"Kostet : ${a.Preis}", WindowChrome.TextColour);

            bool held = _held == i;
            WindowChrome.PaintButton(this, x, ButtonY, ButtonTiles, Scale, held);
            // Der Knopfzeichner setzt die Beschriftung mittig und rückt sie im
            // gedrückten Zustand um einen Punkt nach unten (@0x456781 »+3«
            // gegen @0x4567B7 »+4«).
            float wpx = font.GetStringSize(
                "Kaufen", HorizontalAlignment.Left, -1, FontSize).X;
            int bx = x + (int)((ButtonTiles * WindowChrome.Cell * Scale - wpx) / 2 / Scale);
            var col = WindowChrome.TextColour;
            if (a.Kaufen == null || !a.Bezahlbar) col = new Color(col.R, col.G, col.B, 0.45f);
            Text(font, bx, ButtonY + (held ? 4 : 3), "Kaufen", col);
            Buttons++;
        }

        Text(font, ColumnX[0], BalanceY, $"Kontostand : ${s.Geld}",
             WindowChrome.TextColour);
    }

    // ---- Treffertest -------------------------------------------------------

    private static Rect2 Box(int x, int y, int wTiles)
        => new(x * Scale, y * Scale,
               wTiles * WindowChrome.Cell * Scale, WindowChrome.Cell * Scale);

    /// <summary>Das Feld eines Kaufknopfes in EIGENEN Punkten — dieselbe
    /// Rechnung, die <see cref="Hit"/> benutzt.
    ///
    /// <para>⚠ Es gibt sie fuer den Pruefstand, und zwar ueber
    /// <see cref="Box"/> statt ueber eine zweite Rechnung: ein Pruefstand, der
    /// sich die Knopflage selbst ausrechnet, trifft immer — auch dann, wenn der
    /// Zeichner woanders malt.</para></summary>
    public Rect2 Knopffeld(int i)
        => i >= 0 && i < ColumnX.Length ? Box(ColumnX[i], ButtonY, ButtonTiles)
                                        : new Rect2();

    /// <summary>Welcher Knopf liegt unter dem Zeiger? 0/1 Spalte, 2 Kreuz,
    /// -1 keiner.</summary>
    private int Hit(Vector2 p)
    {
        if (Box((WTiles - 1) * WindowChrome.Cell, 0, 1).HasPoint(p)) return 2;
        for (int i = 0; i < ColumnX.Length; i++)
            if (Box(ColumnX[i], ButtonY, ButtonTiles).HasPoint(p)) return i;
        return -1;
    }

    /// <summary>Wieviele Mausereignisse dieses Fenster ueberhaupt GESEHEN hat,
    /// und welcher Knopf zuletzt getroffen wurde. ⚠ Nur fuer den Pruefstand —
    /// ohne diese zwei Zahlen laesst sich "der Klick kauft nicht" nicht von
    /// "der Klick kommt gar nicht an" unterscheiden, und das sind zwei ganz
    /// verschiedene Fehler.</summary>
    public int GesehenKlicks { get; private set; }
    public int LetzterTreffer { get; private set; } = -99;
    public Vector2 LetzterPunkt { get; private set; } = new(-1, -1);
    public Rect2 LetzterKasten { get; private set; }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton) GesehenKlicks++;
        if (WindowManager.KlickProtokoll && @event is InputEventMouseButton pb
            && pb.ButtonIndex == MouseButton.Left)
            GD.Print($"klick-log: POSTEN sieht {(pb.Pressed ? "DRUCK " : "LOS   ")} "
                   + $"oertlich {pb.Position} | Fenster global {GetGlobalRect()} "
                   + $"| Skala {Scale} | Knopf0 {Knopffeld(0)} Knopf1 {Knopffeld(1)} "
                   + $"| Treffer {Hit(pb.Position)}");
        if (@event is not InputEventMouseButton mb
            || mb.ButtonIndex != MouseButton.Left) return;
        var s = _stand;
        if (s == null) return;

        if (mb.Pressed)
        {
            _held = Hit(mb.Position);
            QueueRedraw();
            if (_held >= 0) AcceptEvent();
            return;
        }

        int auf = Hit(mb.Position);
        LetzterTreffer = auf;
        LetzterPunkt = mb.Position;
        LetzterKasten = Knopffeld(0);
        int war = _held;
        _held = -1;
        QueueRedraw();
        if (auf < 0 || auf != war) return;
        AcceptEvent();

        if (auf == 2) { OnClose?.Invoke(); return; }
        if (auf < s.Angebote.Count)
        {
            var a = s.Angebote[auf];
            // ⚠ Der Kaufweg gehört dem Angebot, nicht dem Fenster — sonst gäbe
            // es einen zweiten Satz Wahrheiten über den Preis.
            if (a.Kaufen != null && a.Bezahlbar) { a.Kaufen.Invoke(); OnChanged?.Invoke(); }
        }
    }
}
