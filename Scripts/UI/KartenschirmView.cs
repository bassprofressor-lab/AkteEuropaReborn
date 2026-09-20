namespace AkteEuropaReborn.UI;

using Godot;
using System;

/// <summary>
/// <b>FENSTERART 3 — DER KARTENSCHIRM »Luft-Einsatzplan«</b> (20.09.2026,
/// Bauaufgabe 2 aus <c>berichte/flughafenfenster-k20-fable.md</c> §3.3).
///
/// <para>Das ist das Fenster, das der Spieler meint, wenn er sagt, man setze
/// das Angriffsziel »via Minimap«: der ANGRIFF-Knopf des Flughafens öffnet es
/// <b>an der Mausstelle</b>, und der Klick darin schickt die Staffel los.</para>
///
/// <para><b>GELESEN</b> (C; F <c>0x443D50</c> ist erst zu 79 % aufgelöst und
/// hier <b>nicht</b> gegengelesen):</para>
/// <code>
///   Oeffner 0x444D90(x, y, Fenster, zoom)
///     gibt es schon ein Art-3-Fenster im Modus 2 -> nach vorn holen, Ende   @0x444DA0
///     W = dword[0x542DC4] ; H = dword[0x542DF8]                            (in ZELLEN)
///     Kacheln_breit = ceil(s*W / 20) + 2 ; Kacheln_hoch = ceil(s*H / 20) + 2  @0x444EE4..0x444F06
///     Titel 0x4FC668 = "Luft-Einsatzplan"                                   @0x444EC2
///     +0xA223 := Zoomindex ; +0xA1A0 := 2 (Zielwahl) ; +0xA1A2 := Flughafenfenster
///     Kartenbild bei ((Breite - s*W)/2, (Hoehe - s*H)/2)                    @0x444F9D
///
///   Klick (Art-3-Arm 0x448F01, Element 0 = Koerper)
///     Zelle = (Maus - Fenster - Versatz) / s                                @0x4490DC..0x44911A
///     Modus 2 -> 0x41D1D0(x,y) und dann 0x450310(Flughafenfenster, x, y)    @0x4492DC
///     Element 2/3 = Zoom + / − : schliessen und mit neuem Zoomindex neu oeffnen
/// </code>
///
/// <para>⚠⚠ <b>ZWEI BERICHTIGUNGEN AM BERICHT</b>, beide aus eigener Lesung am
/// 20.09.2026 — wer dem Bericht folgt, baut das Fenster doppelt so gross:</para>
/// <list type="number">
///   <item>Die Zoomtafel steht bei <b><c>0x4FD610</c></b> und ist
///   <b>[1, 2, 3, 0]</b>. Der Bericht las sie bei <c>0x4FD614</c> — das ist
///   dieselbe Tafel um einen Eintrag verschoben, daher seine [2,3,0,0].</item>
///   <item>Geöffnet wird mit <b>1 px je Zelle</b>, nicht mit 2: der
///   ANGRIFF-Knopf ruft <c>0x444D90(MausX−3, MausY−3, Fenster, <b>0</b>)</c>
///   (@0x449A8F..0x449AA8), und gezeichnet wird mit <c>Tafel[Zoomindex]</c>
///   (@0x444ECD). Die <b>2</b> im Bericht ist der Wert, den der Fit-Test
///   @0x444E18 prüft — er liest <c>Tafel[Zoomindex+1]</c> und entscheidet
///   damit nur, <b>ob »Zoom +« überhaupt geht</b>: das Ergebnis landet als
///   Flagbit in <c>cl</c> (@0x444DFE <c>inc cl</c> gegen @0x444EBA
///   <c>and cl, 2</c>) und wird dem Fensterbauer übergeben. Der gezeichnete
///   Maßstab ändert sich dabei nicht.</item>
/// </list>
///
/// <para>⚠ <b>Was UNSER ist:</b> das <b>Bild</b> im Körper. Wir malen dort
/// unsere Übersichtskarte (<see cref="Rendering.Minimap"/>) auf die gelesene
/// Fläche — dasselbe Gelände, derselbe Nebel, dieselben Punkte. Wie das
/// Original seinen Kartenschirm füllt (<c>0x4B7ED0</c>), ist <b>ungelesen</b>.
/// Gelesen und übernommen sind: Grösse, Rand, Titel, Zoomstufen, die Mitte-
/// Ausrichtung und die Zellrechnung des Klicks.</para>
///
/// <para>⚠ Die Art-3-Modi <b>1, 3, 4 und 5</b> sind ungelesen; gebaut ist nur
/// <b>Modus 2</b> (Zielwahl) und <b>Modus 0</b> (Bildausschnitt setzen) ist
/// das, was unsere stehende Übersicht ohnehin tut.</para>
///
/// <para>Gegenschalter <c>--zielwahl-minimap</c>: die Zielwahl nimmt wieder die
/// <b>stehende</b> Übersichtskarte statt dieses Fensters (der Stand vom
/// 19.09.). Prüfstand <c>--kartenschirm-probe</c>.</para>
/// </summary>
public sealed partial class KartenschirmView : Control
{
    /// <summary>Die Zoomtafel <c>0x4FD610</c>: <b>1, 2, 3</b> Bildpunkte je
    /// Zelle. Der vierte Eintrag ist 0 und damit das Ende.</summary>
    public static readonly int[] Zoomtafel = { 1, 2, 3 };

    /// <summary>Der Titel, <c>0x4FC668</c> — selbst aus der EXE gelesen.</summary>
    public const string Titel = "Luft-Einsatzplan";

    /// <summary>Der Rand: <b>eine Kachel</b> auf jeder Seite (die <c>+2</c> in
    /// <c>ceil(s·W/20) + 2</c> @0x444EED).</summary>
    public const int RandTiles = 1;

    /// <summary>Wie weit oben-links der Mausstelle das Fenster aufgeht
    /// (@0x449A97/@0x449AA3: <c>dword[0x502AA8] − 3</c>).</summary>
    public const int MausVersatz = 3;

    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;

    /// <summary>Die zwei Zoomknöpfe, Element 2 und 3 des Originals. ⚠ Ihre
    /// LAGE und GRÖSSE sind unsere Setzung — die Lesung nennt die Elemente und
    /// was sie tun, nicht ihre Rechtecke. Sie sitzen rechts in der Titelzeile,
    /// weil der KÖRPER die gelesene Grösse hat und unten kein Platz frei ist,
    /// den das Original dort hätte.</summary>
    private const int ZoomTiles = 2;

    /// <summary>Schmaler als das passen die zwei Knöpfe nicht neben den Titel —
    /// dann bleiben sie weg. Eine sehr kleine Karte ist damit nicht zoombar;
    /// das ist besser, als die gelesene Fenstergrösse dafür zu dehnen.</summary>
    private const int ZoomMindestTiles = 9;

    private Rendering.Minimap? _karte;
    private int _zoom;
    private Vector2I _zellen;

    /// <summary>Der Flughafen, dessen Fenster den Knopf gedrückt hat —
    /// <c>+0xA1A2</c>. Das Fenster trägt ihn nur, es benutzt ihn nicht.</summary>
    public int Flughafen { get; private set; } = -1;

    /// <summary>Der Zoomindex, <c>+0xA223</c>.</summary>
    public int Zoomindex => _zoom;

    /// <summary>Bildpunkte je Zelle, wie gerade gezeichnet.</summary>
    public int Massstab => Zoomtafel[Mathf.Clamp(_zoom, 0, Zoomtafel.Length - 1)];

    /// <summary>Eine Zelle wurde angeklickt.</summary>
    public Action<int, int>? OnZelle;

    /// <summary>Das Fenster wurde geschlossen — im Modus 2 heisst das
    /// <b>Abbruch</b> (<c>0x447222</c> setzt <c>0x4FD640 := 0</c>).</summary>
    public Action? OnClose;

    /// <summary>Der Zoom wurde geändert; der Halter baut das Fenster neu auf
    /// (das Original schliesst und öffnet wirklich neu, @0x448F84).</summary>
    public Action<int>? OnZoom;

    /// <summary>Womit der Körper gefüllt wird — dieselben Zulieferer wie die
    /// stehende Übersichtskarte.</summary>
    public Action<Rendering.Minimap>? Fuellen;

    private bool _zieht;

    public KartenschirmView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Zeichnet dieses Fenster mit den Möbeln des Originals?</summary>
    public static bool Usable => WindowChrome.Atlas != null;

    /// <summary>
    /// <b>Die Fenstergrösse in Kacheln</b> — @0x444EE4..0x444F06:
    /// <c>ceil(s·W/20) + 2</c>. Eine Grösse, eine Funktion: der Prüfstand
    /// fragt hier und rechnet nicht selbst nach.
    /// </summary>
    public static Vector2I KachelMass(Vector2I zellen, int zoomindex)
    {
        int s = Zoomtafel[Mathf.Clamp(zoomindex, 0, Zoomtafel.Length - 1)];
        return new Vector2I(
            Mathf.CeilToInt(s * zellen.X / (float)WindowChrome.Cell) + 2 * RandTiles,
            Mathf.CeilToInt(s * zellen.Y / (float)WindowChrome.Cell) + 2 * RandTiles);
    }

    /// <summary>
    /// <b>Passt der NÄCHSTGRÖSSERE Zoom noch auf den Schirm?</b> @0x444E18
    /// prüft <c>Tafel[Zoomindex+1]</c> gegen <c>dword[0xB136B0]</c> (Breite)
    /// und <c>dword[0x5387CC]</c> (Höhe) und macht daraus das Flagbit, mit dem
    /// der Knopf »Zoom +« steht oder fällt.
    /// </summary>
    public static bool ZoomAufGehtNoch(Vector2I zellen, int zoomindex, Vector2 schirm)
    {
        int naechster = zoomindex + 1;
        if (naechster >= Zoomtafel.Length || Zoomtafel[naechster] == 0) return false;
        var k = KachelMass(zellen, naechster);
        return k.X * WindowChrome.Cell * Scale < schirm.X
            && k.Y * WindowChrome.Cell * Scale < schirm.Y;
    }

    public void Zeige(Vector2I zellen, int zoomindex, int flughafen)
    {
        _zellen = zellen;
        _zoom = Mathf.Clamp(zoomindex, 0, Zoomtafel.Length - 1);
        Flughafen = flughafen;

        var k = KachelMass(_zellen, _zoom);
        CustomMinimumSize = new Vector2(k.X * WindowChrome.Cell * Scale,
                                        k.Y * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;

        // Der Koerper: s*W x s*H, ZENTRIERT im Fenster (@0x444F9D).
        _karte ??= NeueKarte();
        int s = Massstab;
        var bild = new Vector2(s * _zellen.X, s * _zellen.Y) * Scale;
        _karte.Size = bild;
        _karte.Position = ((Size - bild) * 0.5f).Round();
        _karte.Visible = true;
        _karte.QueueRedraw();
        QueueRedraw();
    }

    private Rendering.Minimap NeueKarte()
    {
        var m = new Rendering.Minimap { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(m);
        Fuellen?.Invoke(m);
        return m;
    }

    /// <summary>Wo die Kacheln des Körpers liegen — der Versatz aus @0x444F9D.
    /// ⚠ Der Prüfstand fragt hier, damit er die Zellrechnung nicht doppelt.</summary>
    public Vector2 Bildversatz
        => _karte?.Position ?? Vector2.Zero;

    /// <summary>
    /// <b>Die Zellrechnung des Klicks</b> — @0x4490DC..0x44911A:
    /// <c>(Maus − Fenster − Versatz) / s</c>. Eine Grösse, eine Funktion.
    /// </summary>
    /// <returns>null, wenn der Punkt neben der Karte liegt — das Original tut
    /// dann nichts (<c>0x41D1D0</c> liefert 0).</returns>
    public Vector2I? ZelleUnter(Vector2 imFenster)
    {
        var p = (imFenster - Bildversatz) / (Massstab * Scale);
        int cx = Mathf.FloorToInt(p.X), cy = Mathf.FloorToInt(p.Y);
        if (cx < 0 || cy < 0 || cx >= _zellen.X || cy >= _zellen.Y) return null;
        return new Vector2I(cx, cy);
    }

    public override void _Draw()
    {
        var k = KachelMass(_zellen, _zoom);
        WindowChrome.Paint(this, k.X, k.Y, Scale);
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(TitleX, TitleY + 12) * Scale, Titel,
                   HorizontalAlignment.Left, -1, 12 * Scale, WindowChrome.TitleColour);

        // Die zwei Zoomknoepfe. ⚠ Lage und Groesse sind UNSERE Setzung (siehe
        // oben); »Zoom +« steht nur, solange der naechste Massstab auf den
        // Schirm passt — DAS ist die gelesene Bedingung (@0x444E39/@0x444E5E),
        // nur an unserer Stelle gezeichnet.
        if (!ZoomKnoepfeSichtbar) return;
        Knopf(font, ZoomMinusX, ZoomY, "-", _zoom > 0);
        Knopf(font, ZoomPlusX, ZoomY, "+",
              ZoomAufGehtNoch(_zellen, _zoom, GetViewportRect().Size));
    }

    private void Knopf(Font font, int x, int y, string wort, bool geht)
    {
        WindowChrome.PaintButton(this, x, y, ZoomTiles, Scale);
        float w = font.GetStringSize(wort, HorizontalAlignment.Left, -1, 12 * Scale).X;
        int bx = x + (int)((ZoomTiles * WindowChrome.Cell * Scale - w) / 2 / Scale);
        var col = WindowChrome.TextColour;
        if (!geht) col = new Color(col.R, col.G, col.B, 0.45f);
        DrawString(font, new Vector2(bx, y + 15) * Scale, wort,
                   HorizontalAlignment.Left, -1, 12 * Scale, col);
    }

    private bool ZoomKnoepfeSichtbar => KachelMass(_zellen, _zoom).X >= ZoomMindestTiles;
    private int ZoomY => 0;
    private int ZoomPlusX => (KachelMass(_zellen, _zoom).X - 2 * ZoomTiles) * WindowChrome.Cell;
    private int ZoomMinusX => (KachelMass(_zellen, _zoom).X - 3 * ZoomTiles) * WindowChrome.Cell;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            { OnClose?.Invoke(); AcceptEvent(); return; }
            if (mb.ButtonIndex != MouseButton.Left) return;

            if (!mb.Pressed) { _zieht = false; return; }
            var p = mb.Position / Scale;

            // Element 2/3 — Zoom. ⚠ VOR der Titelzeile geprueft: die Knoepfe
            // liegen IN ihr, und wer zuerst auf »ziehen« prueft, bekommt einen
            // Knopf, den man nur verschieben kann.
            int zy = ZoomY;
            if (ZoomKnoepfeSichtbar && p.Y >= zy && p.Y < zy + WindowChrome.Cell)
            {
                if (p.X >= ZoomPlusX && p.X < ZoomPlusX + WindowChrome.Cell)
                {
                    if (ZoomAufGehtNoch(_zellen, _zoom, GetViewportRect().Size))
                        OnZoom?.Invoke(_zoom + 1);
                    AcceptEvent(); return;
                }
                if (p.X >= ZoomMinusX && p.X < ZoomMinusX + WindowChrome.Cell)
                {
                    if (_zoom > 0) OnZoom?.Invoke(_zoom - 1);
                    AcceptEvent(); return;
                }
            }

            // Die Titelzeile zieht das Fenster — wie bei den anderen Fenstern
            // auch (seine Meldung vom 19.09.: »kann es auch nicht verschieben«).
            if (p.Y < WindowChrome.Cell) { _zieht = true; AcceptEvent(); return; }

            // Element 0 — der Koerper.
            if (ZelleUnter(mb.Position) is { } z) OnZelle?.Invoke(z.X, z.Y);
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion mm && _zieht)
        {
            Position += mm.Relative;
            AcceptEvent();
        }
    }
}
