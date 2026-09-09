namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>»CD-SPIELER«</b> — <b>Fensterart 12</b> des Originals, mit dessen eigenen
/// Kacheln. Der zwölfte und letzte Knopf des Haupt-Menüs
/// (<see cref="MainMenuWindow"/>).
///
/// <para>⚠⚠ 09.09.2026, seine Meldung: »CD Spieler ist noch ausgegraut, ich
/// glaub da konnte man einfach die musik wechseln, die laufen soll.« —
/// <b>Er hatte recht</b>, und gelesen ist es jetzt auch
/// (<c>berichte/fensterlisten-fable-2.md</c> hat den Vorläufer, die Lesung
/// dieses Fensters steht unten).</para>
///
/// <code>
///   Anleger 0x458489   180 x 100 = 9 x 5 Kacheln
///   0x472F1F  Titel "CD-Spieler" (0x501D98) auf (10, 2), Farbe 0x96/0xA9
///   0x472F59  "ANHALTEN"(0x501D8C) / "SPIELEN"(0x501D80)  (20,20), 4 Kacheln
///   0x472FFD  "&lt;" (0x501D64)                              (20,40), 2 Kacheln
///   0x473026  "&gt;" (0x501D60)                              (60,40), 2 Kacheln
///   0x472F82  "EINEN"  (0x501D78)                        (100,20), 3 Kacheln
///   0x472FAB  "ALLE"   (0x501D70)                        (100,40), 3 Kacheln
///   0x472FD4  "ZUFALL" (0x501D68)                        (100,60), 3 Kacheln
///   0x473176  Anzeige "NN/MM" auf (20, 64), beide Zahlen ZWEISTELLIG
/// </code>
///
/// <para>⭐ <b>Der Vorindex kannte nur vier Knöpfe</b> (ANHALTEN, SPIELEN,
/// EINEN, ALLE) — im Befehlsstrom stehen <b>sechs</b>: <c>&lt;</c>, <c>&gt;</c>
/// und <c>ZUFALL</c> kommen hinzu. Genau die drei machen das Fenster erst zu
/// dem, wofür er es gehalten hat.</para>
///
/// <para><b>Die drei Modusknöpfe sind eine RADIOGRUPPE</b> (der Verteilerarm
/// <c>0x44B46E</c> löscht erst alle drei Plätze und setzt dann den getroffenen),
/// und sie wirken <b>nicht sofort</b>, sondern erst am Stückende — der
/// Notify-Behandler <c>0x4585A0</c> verzweigt über <c>byte[0x500E0C]</c>:
/// 0 EINEN (dieselbe Spur), 1 ALLE (nächste, am Ende Umbruch), 2 ZUFALL.
/// Die drei anderen sind TASTER und wirken beim Druck.</para>
///
/// <para>⚠⚠ <b>UNSERE bewusste Abweichung, und sie ist die ganze Idee des
/// Fensters bei uns:</b> das Original spielt <b>Audio-Spuren der Spiel-CD</b>
/// über MCI (<c>"cdaudio"</c>, <c>0x4D5000</c> ff.) — und es tat das nur, wenn
/// die Einstellung »MIDI-Musik« AUS war; der CD-Spieler war der <b>Ersatz</b>
/// für die MIDI-Musik. Wir haben keine CD, sondern die MIDI-Stücke des Spiels.
/// Dieses Fenster steuert darum <see cref="Audio.MidiMusic"/>: dieselben sechs
/// Knöpfe, dieselbe Anzeige, dieselben Folgeregeln — nur ist es bei uns
/// <b>die</b> Musik statt ihres Ersatzes. Seine Ansage dazu: »na die original
/// musik einspielen, wie ist mir egal«.</para>
///
/// <para>⚠ Zwei kleinere Setzungen fallen daraus ab: (1) unsere Stückliste ist
/// <b>1 … Anzahl−1</b>, weil Stück 0 dem Menü gehört (das Original startet auf
/// Spur 2 und bricht auf Spur 1 um — dieselbe Absicht, andere Zählung);
/// (2) welchen Modus das Original beim Start vorwählt, ist ungelesen, wir
/// nehmen ZUFALL, weil das die Regel ist, nach der wir bisher gespielt
/// haben.</para>
/// </summary>
public sealed partial class CdPlayerView : Control
{
    /// <summary>Die Fensterart des Originals.</summary>
    public const int Art = 12;

    /// <summary>9 x 5 Kacheln — die 180 x 100 des Anlegers
    /// <c>0x458489</c>.</summary>
    public const int WTiles = 9, HTiles = 5;

    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;

    /// <summary>Die Anzeige »NN/MM« (@0x473176).</summary>
    private const int AnzeigeX = 20, AnzeigeY = 64;

    /// <summary>Die sechs Knöpfe: x, y, Breite in Kacheln — alle aus dem
    /// Befehlsstrom.</summary>
    private static readonly (int X, int Y, int Tiles)[] Knoepfe =
    {
        (20, 20, 4),    // 0  SPIELEN / ANHALTEN
        (20, 40, 2),    // 1  <
        (60, 40, 2),    // 2  >
        (100, 20, 3),   // 3  EINEN
        (100, 40, 3),   // 4  ALLE
        (100, 60, 3),   // 5  ZUFALL
    };

    /// <summary>Wörtlich aus der EXE. Der erste Knopf heisst je nach Zustand
    /// anders (<c>0x501D8C</c> / <c>0x501D80</c>).</summary>
    private static readonly string[] Wort =
        { "SPIELEN", "<", ">", "EINEN", "ALLE", "ZUFALL" };

    /// <summary>Die Hinweistexte des Originals, Tafel <c>0x4F0280 + 75·n</c>,
    /// Nummern 41…46.</summary>
    private static readonly string[] Hinweis =
    {
        "START/STOP Wiedergabe", "Vorheriger Titel", "Nächster Titel",
        "Einen Titel loopen", "Alle Titel loopen", "Zufallsauswahl",
    };

    public Action? OnClose;

    private int _held = -1;
    private bool _zieht;

    public static bool Usable => WindowChrome.Atlas != null
                                 && WindowChrome.LegacyFont != null;

    /// <summary><c>--cdspieler-alt</c> — der Stand vor dem 09.09.2026: der Knopf
    /// im Haupt-Menü bleibt gedimmt. Nullmodell von
    /// <c>--cdspieler-check</c>.</summary>
    public static bool Alt;

    public CdPlayerView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Das höchste wählbare Stück. ⚠ Stück 0 gehört dem Menü, also
    /// zählt die Liste ab 1 — siehe Klassenkopf.</summary>
    public static int Letztes => Mathf.Max(1, Audio.MidiMusic.TrackCount - 1);

    /// <summary>Was in der Anzeige steht, zweistellig wie im Original
    /// (@0x473032: <c>"0"</c> davor, wenn kleiner als 10).</summary>
    public static string Anzeige()
        => $"{Mathf.Clamp(Audio.MidiMusic.Gewaehlt, 1, Letztes):00}/{Letztes:00}";

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    /// <summary>Ist dieser Knopf gerade »an«? Nur die drei Modusknöpfe haben
    /// einen bleibenden Zustand — die anderen drei sind Taster.</summary>
    public static bool An(int k) => k switch
    {
        3 => Audio.MidiMusic.Folge == Audio.MidiMusic.FolgeEinen,
        4 => Audio.MidiMusic.Folge == Audio.MidiMusic.FolgeAlle,
        5 => Audio.MidiMusic.Folge == Audio.MidiMusic.FolgeZufall,
        _ => false,
    };

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);
        Text(font, TitleX, TitleY, "CD-Spieler", WindowChrome.TitleColour);

        for (int k = 0; k < Knoepfe.Length; k++)
        {
            var (x, y, tiles) = Knoepfe[k];
            bool an = An(k) || _held == k;
            WindowChrome.PaintButton(this, x, y, tiles, Scale, an);

            // Knopf 0 heisst ANHALTEN, solange etwas laeuft (@0x472F3A).
            string w = k == 0 && Audio.MidiMusic.Laeuft ? "ANHALTEN" : Wort[k];
            float wpx = font.GetStringSize(w, HorizontalAlignment.Left, -1, FontSize).X;
            int bx = x + (int)((tiles * WindowChrome.Cell * Scale - wpx) / 2 / Scale);
            Text(font, bx, y + (an ? 4 : 3), w, WindowChrome.TextColour);
        }

        Text(font, AnzeigeX, AnzeigeY, Anzeige(), WindowChrome.TextColour);
    }

    /// <summary>−2 Schliesskreuz, 0..5 ein Knopf, sonst −1 (ziehen).</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;
        for (int k = 0; k < Knoepfe.Length; k++)
        {
            var (bx, by, tiles) = Knoepfe[k];
            if (x >= bx && x < bx + tiles * WindowChrome.Cell
                && y >= by && y < by + 20) return k;
        }
        return -1;
    }

    /// <summary>
    /// Einen Knopf auslösen — derselbe Weg, den auch der Klick nimmt, damit der
    /// Prüfstand ihn ohne Maus gehen kann.
    ///
    /// <para>Die drei Taster tun im Original genau das (Zeichner
    /// <c>0x472DD1</c>…<c>0x472E9F</c>): <b>SPIELEN/ANHALTEN</b> schaltet um;
    /// <b>&lt;</b> und <b>&gt;</b> verschieben die Spur um eins, begrenzt auf
    /// 1…Anzahl, und <b>starten sofort neu, wenn gerade etwas läuft</b>.</para>
    /// </summary>
    public void Ausloesen(int k)
    {
        switch (k)
        {
            case 0:                                        // @0x472DD1
                if (Audio.MidiMusic.Laeuft) Audio.MidiMusic.Stop();
                else Audio.MidiMusic.Play(Mathf.Clamp(Audio.MidiMusic.Gewaehlt, 1, Letztes));
                break;
            case 1:                                        // @0x472E20  <
            case 2:                                        // @0x472E5E  >
            {
                int neu = Audio.MidiMusic.Gewaehlt + (k == 1 ? -1 : 1);
                if (neu < 1 || neu > Letztes) break;        // das Original klemmt, es bricht nicht um
                if (Audio.MidiMusic.Laeuft) Audio.MidiMusic.Play(neu);
                else Audio.MidiMusic.Waehle(neu);
                break;
            }
            case 3: Audio.MidiMusic.Folge = Audio.MidiMusic.FolgeEinen; break;   // @0x472DA8
            case 4: Audio.MidiMusic.Folge = Audio.MidiMusic.FolgeAlle; break;    // @0x472DB9
            case 5: Audio.MidiMusic.Folge = Audio.MidiMusic.FolgeZufall; break;  // @0x472DCA
        }
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm)
        {
            Position += mm.Relative;
            var vp = GetViewportRect().Size;
            Position = new Vector2(
                Mathf.Clamp(Position.X, 0, Mathf.Max(0, vp.X - Size.X)),
                Mathf.Clamp(Position.Y, 0, Mathf.Max(0, vp.Y - Size.Y)));
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
        int t = Hit(mb.Position);
        if (mb.Pressed)
        {
            _held = t >= 0 ? t : -1;
            if (t == -1) _zieht = true;
            QueueRedraw();
            AcceptEvent();
            return;
        }
        _held = -1;
        if (_zieht) { _zieht = false; QueueRedraw(); AcceptEvent(); return; }
        if (t == -1) { QueueRedraw(); return; }
        AcceptEvent();
        if (t == -2) { OnClose?.Invoke(); return; }
        Ausloesen(t);
    }

    /// <summary>ESC schliesst — wie bei den drei Listenfenstern, und aus
    /// demselben Grund: der Baum steht.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            OnClose?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    public override string _GetTooltip(Vector2 pos)
    {
        int h = Hit(pos);
        if (h == -2) return "Den CD-Spieler schliessen";
        return h >= 0 && h < Hinweis.Length ? Hinweis[h] : "";
    }
}
