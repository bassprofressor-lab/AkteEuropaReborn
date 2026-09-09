namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>»HAUPT-MENÜ«</b> — das Menü IM LAUFENDEN SPIEL, <b>Fensterart 17</b> des
/// Originals, mit dessen eigenen Kacheln.
///
/// <para>⚠⚠ 08.09.2026, seine Bestellung: »es gibt ein tolles originales
/// Haupt-Menü, was sogar das Laden innerhalb des Spieles ermöglicht, falls eine
/// Entscheidung falsch war … es hat ganz viel tolle Felder die tolle
/// Informationen zeigen«. Bis heute stand an dieser Stelle
/// <see cref="PauseMenu"/> — Eigenbau aus Godot-Möbeln mit fünf Zeilen.
/// Dieselbe Lehre wie beim Minenfenster: <b>ein Fenster wird mit den Kacheln
/// des Originals gebaut, nicht mit Godot-Möbeln.</b> Vorbild ist
/// <see cref="MineView"/>.</para>
///
/// <para><b>Wie es gefunden wurde — vier Schritte, keiner geraten.</b></para>
/// <list type="number">
///   <item><b>Die Fensterart.</b> <c>aekernel-tools/FENSTER_RE.md</c> Zeile 33:
///   Art 17 = Zeichner <c>0x473EC0</c>, 686 B, »Haupt-Menü«.</item>
///   <item><b>Der Anleger</b> ist <c>0x4593F0</c>. Er schreibt die Art und das
///   Mass:
///   <code>
///     0x459469  mov byte [edx], 0x11             ; FENSTERART 17
///     0x45947A  mov word [ebx+0x8B903E], 0x0C8   ; BREITE 200 = 10 Kacheln
///     0x459483  mov word [ebx+0x8B9040], 0x12C   ; HOEHE  300 = 15 Kacheln
///   </code>
///   ⭐ <b>Gegenprobe der Methode:</b> derselbe Abtast liest für Art 18
///   260x240 und für Art 25 280x220 — genau die Masse, die
///   <see cref="MineView"/> und <see cref="GroupWindowChrome"/> schon tragen.
///   Die Zahl 200x300 kommt also aus einem Leser, der an zwei bekannten Werten
///   stimmt.</item>
///   <item><b>Der Öffner</b> ist <c>0x443C40</c> (er sucht erst ein offenes
///   Fenster der Art 0x11 — <c>cmp al, 0x11</c> @0x443C7D, die
///   Doppelöffnungssperre), gerufen aus dem Bedienverteiler <c>0x4485D0</c>,
///   Arm 4 von vier (<c>jmp [eax*4+0x44DF04]</c> @0x44AF8D). Und dort steht
///   auch die <b>Lage</b>:
///   <code>
///     0x44AFF7  x = (Schirmbreite[0xB136B0] - 0xC8) / 2
///     0x44B00C  y = (Schirmhoehe [0x5387CC] - 0x12C) / 2
///   </code>
///   <b>Das Fenster steht mittig</b> — nicht über die Anlegertafel, sondern
///   weil sein Öffner es so ausrechnet.</item>
///   <item><b>Der Zeichner</b> <c>0x473EC0</c> gibt Titel und Knöpfe. Der Titel
///   »Haupt-Menü« (<c>0x501F20</c>) läuft über <c>0x4020A4</c> @0x473F60 auf
///   (10, 2). Die zwölf Knöpfe laufen alle über den Knopfzeichner
///   <c>0x401820</c>, und dessen viertes Argument ist die <b>Breite in
///   Kacheln: 8</b> (=160; mit x=20 also 20+160+20 = die 200 des Fensters).
///   ⭐ Gegenprobe: <see cref="MineView"/> schiebt an derselben Stelle eine 5,
///   und seine Knöpfe sind 100 Punkte breit.</item>
/// </list>
///
/// <para><b>Die zwölf Knöpfe, Punkt für Punkt aus dem Zeichner abgelesen</b>
/// (x=20 bei allen, Reihenfolge und y wie im Befehlsstrom):</para>
/// <code>
///    y= 25  "Missionsinformation"    y=145  "Enzyklopädie"
///    y= 45  "Einheitenliste"         y=170  "Spielstand speichern"
///    y= 65  "Gebäudeliste"           y=190  "Altes Spiel laden"
///    y= 85  "Forschungsergebnisse"   y=210  "Spiel beenden"
///    y=105  "Untermissionen"         y=240  "Einstellungen"
///    y=125  "Mission wiederholen"    y=260  "CD-Spieler"
/// </code>
/// <para>⚠ <b>Die zwei Lücken sind echt.</b> Der Abstand ist neunmal 20, aber
/// 145→170 sind 25 und 210→240 sind 30. So steht es im Befehlsstrom
/// (<c>push 0x91</c>, <c>push 0xAA</c>; <c>push 0xD2</c>, <c>push 0xF0</c>) —
/// sie trennen die Auskunftsknöpfe vom Spielstand und den vom System. Wer sie
/// glattzieht, verliert die Gliederung des Originals.</para>
///
/// <para>⚠ <b>Es gibt kein »Weiter«.</b> Das Original schliesst dieses Fenster
/// über das Schliesskreuz — eine dreizehnte Zeile wäre unsere Zutat. ESC tut
/// dasselbe (UNSERE Setzung, weil ESC das Fenster bei uns auch öffnet).</para>
///
/// <para>⚠ <b>Was NICHT gebaut ist, steht am Knopf</b> — dieselbe Regel wie bei
/// <see cref="MineView"/>: fünf Knöpfe zeigen auf Fenster, die es bei uns nicht
/// gibt (Einheitenliste = Art 22, Gebäudeliste = Art 27,
/// Forschungsergebnisse = Art 29, Untermissionen, CD-Spieler = Art 12). Sie
/// stehen gedimmt da und sagen den Grund im Hinweistext. Ein Knopf, der stumm
/// nichts tut, ist von einem kaputten nicht zu unterscheiden.</para>
/// </summary>
public sealed partial class MainMenuWindow : Control
{
    /// <summary>Die Fensterart des Originals.</summary>
    public const int Art = 17;

    /// <summary>10 x 15 Kacheln — die 200 x 300 aus <c>0x45947A</c>/
    /// <c>0x459483</c>.</summary>
    public const int WTiles = 10, HTiles = 15;

    /// <summary>Wie bei allen unseren Fenstern: doppelt, damit die 20er-Kachel
    /// auf heutigen Schirmen lesbar bleibt. UNSERE Setzung.</summary>
    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;

    /// <summary>Das vierte Argument von <c>0x401820</c>: Knopfbreite in
    /// Kacheln.</summary>
    public const int ButtonTiles = 8;
    private const int ButtonX = 20;

    /// <summary>Die zwölf y aus dem Zeichner, in seiner Reihenfolge.</summary>
    private static readonly int[] ButtonY =
        { 25, 45, 65, 85, 105, 125, 145, 170, 190, 210, 240, 260 };

    /// <summary>Die zwölf Beschriftungen, wörtlich aus der EXE
    /// (<c>0x501F08</c> … <c>0x501D98</c>).</summary>
    public static readonly string[] Labels =
    {
        "Missionsinformation", "Einheitenliste", "Gebäudeliste",
        "Forschungsergebnisse", "Untermissionen", "Mission wiederholen",
        "Enzyklopädie", "Spielstand speichern", "Altes Spiel laden",
        "Spiel beenden", "Einstellungen", "CD-Spieler",
    };

    /// <summary>Der Grund, warum ein Knopf bei uns nichts tut — leer heisst
    /// »der geht«. ⚠ Steht hier und nicht im Zeichner, damit der Prüfstand
    /// dieselbe Liste liest wie das Bild.</summary>
    private static readonly string[] Grund =
    {
        // ⚠ Seit dem 09.09.2026 gibt es auch die Einheitenliste — derselbe
        // Grund wie bei der Gebaeudeliste eine Zeile tiefer.
        "", "",
        // ⚠ Seit dem 09.09.2026 GIBT es die Gebaeudeliste. Der Grund bleibt
        // trotzdem leer: sie kann abgeschaltet sein (--gebaeudeliste-alt) oder
        // mangels Kacheln fehlen, und dann sagt der Rueckfalltext »ist hier
        // nicht eingehaengt« die Wahrheit, waehrend »nicht gebaut« luege.
        "",
        // ⚠ Seit dem 09.09.2026 gibt es auch die Forschungsergebnisse.
        "",
        "Untermissionen gibt es bei uns nicht.", "", "", "", "", "",
        // ⚠ Seit dem 09.09.2026 gibt es auch den CD-Spieler — bei uns spielt
        // er die MIDI-Musik statt der CD, siehe CdPlayerView.
        "", "",
    };

    // Die sieben Wege, die es bei uns wirklich gibt. Der Wirt haengt sie ein;
    // was er nicht einhaengt, steht gedimmt da wie ein ungebauter Knopf.
    public Action? OnClose;
    public Action? OnMissionInfo;
    public Action? OnUnitList;
    public Action? OnBuildingList;
    public Action? OnResearchList;
    public Action? OnCdPlayer;
    public Action? OnRestart;
    public Action? OnEncyclopedia;
    public Action? OnSave;
    public Action? OnLoad;
    public Action? OnQuit;
    public Action? OnSettings;

    private int _held = -1;
    private bool _zieht;

    /// <summary>Wie viele Knöpfe bedienbar sind — für den Prüfstand.</summary>
    public int Buttons { get; private set; }

    /// <summary>Wie viele Knöpfe es überhaupt gibt — zwölf, und das ist eine
    /// gelesene Zahl.</summary>
    public static int Anzahl => ButtonY.Length;

    public static bool Usable => WindowChrome.Atlas != null
                                 && WindowChrome.LegacyFont != null;

    /// <summary><c>--hauptmenue-alt</c> — der Stand vor dem 09.09.2026: ESC
    /// macht wieder den Eigenbau <see cref="PauseMenu"/> auf statt die
    /// Fensterart 17. Der Gegenschalter zu jeder Änderung, und zugleich das
    /// Nullmodell von <c>--hauptmenue-check</c>.</summary>
    public static bool Alt;

    public MainMenuWindow()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;   // läuft, während der Baum steht
    }

    /// <summary>Ob der Knopf <paramref name="i"/> etwas tut. Ein Knopf ohne
    /// eingehängten Weg ist so gut wie einer ohne Mechanik.</summary>
    public bool Geht(int i) => i switch
    {
        0 => OnMissionInfo != null,
        1 => OnUnitList != null,
        2 => OnBuildingList != null,
        3 => OnResearchList != null,
        11 => OnCdPlayer != null,
        5 => OnRestart != null,
        6 => OnEncyclopedia != null,
        7 => OnSave != null,
        8 => OnLoad != null,
        9 => OnQuit != null,
        10 => OnSettings != null,
        _ => false,
    };

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);

        // Der Titel steht so in der EXE (0x501F20), gezeichnet auf (10, 2).
        Text(font, TitleX, TitleY, "Haupt-Menü", WindowChrome.TitleColour);

        Buttons = 0;
        for (int i = 0; i < ButtonY.Length; i++)
        {
            bool geht = Geht(i);
            bool held = _held == i;
            WindowChrome.PaintButton(this, ButtonX, ButtonY[i], ButtonTiles, Scale, held);

            string wort = Labels[i];
            float wpx = font.GetStringSize(wort, HorizontalAlignment.Left, -1, FontSize).X;
            int bx = ButtonX + (int)((ButtonTiles * WindowChrome.Cell * Scale - wpx)
                                     / 2 / Scale);
            var col = WindowChrome.TextColour;
            if (!geht) col = new Color(col.R, col.G, col.B, 0.45f);
            Text(font, bx, ButtonY[i] + (held ? 4 : 3), wort, col);
            if (geht) Buttons++;
        }
    }

    /// <summary>Das Feld eines Knopfes auf dem SCHIRM — der Prüfstand fragt
    /// damit dieselbe Rechnung, die auch <see cref="Hit"/> benutzt.</summary>
    public Rect2? KnopfFeld(int k)
    {
        if (k < 0 || k >= ButtonY.Length) return null;
        var lokal = new Rect2(ButtonX * Scale, ButtonY[k] * Scale,
                              ButtonTiles * WindowChrome.Cell * Scale, 20 * Scale);
        return new Rect2(GetGlobalRect().Position + lokal.Position, lokal.Size);
    }

    /// <summary>Das Schliesskreuz gibt −2, die zwölf Knöpfe 1..12, sonst 0
    /// (ziehen) — dieselbe Ordnung wie <see cref="MineView.Hit"/>.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;
        for (int i = 0; i < ButtonY.Length; i++)
            if (x >= ButtonX && x < ButtonX + ButtonTiles * WindowChrome.Cell
                && y >= ButtonY[i] && y < ButtonY[i] + 20) return i + 1;
        return 0;
    }

    /// <summary>Den Knopf <paramref name="i"/> auslösen — derselbe Weg, den
    /// auch der Klick nimmt. Der Prüfstand ruft ihn, ohne eine Maus zu haben.
    /// Gibt false, wenn der Knopf nichts tut.</summary>
    public bool Ausloesen(int i)
    {
        if (!Geht(i)) return false;
        switch (i)
        {
            case 0: OnMissionInfo?.Invoke(); break;
            case 1: OnUnitList?.Invoke(); break;
            case 2: OnBuildingList?.Invoke(); break;
            case 3: OnResearchList?.Invoke(); break;
            case 11: OnCdPlayer?.Invoke(); break;
            case 5: OnRestart?.Invoke(); break;
            case 6: OnEncyclopedia?.Invoke(); break;
            case 7: OnSave?.Invoke(); break;
            case 8: OnLoad?.Invoke(); break;
            case 9: OnQuit?.Invoke(); break;
            case 10: OnSettings?.Invoke(); break;
            default: return false;
        }
        return true;
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
        if (hit == -2) { OnClose?.Invoke(); return; }
        Ausloesen(hit - 1);
    }

    /// <summary>
    /// ESC schliesst das Menü.
    ///
    /// <para>⚠⚠ Diese Methode MUSS hier stehen und nicht beim Wirt. Solange das
    /// Menü offen ist, steht der Baum (<c>GetTree().Paused</c>) — der
    /// <c>MapViewer</c>, der ESC sonst auswertet, bekommt die Taste dann gar
    /// nicht mehr. Nur ein Knoten mit <c>ProcessModeEnum.Always</c> sieht sie,
    /// und das ist dieser. Derselbe Griff steht seit jeher in
    /// <see cref="PauseMenu"/>; wer ihn beim Umbau vergisst, baut ein Fenster,
    /// das sich mit der Taste, die es geöffnet hat, nicht mehr schliessen
    /// lässt.</para>
    ///
    /// <para>⚠ Der Riegel darüber ist derselbe wie im Eigenbau: hängt ein
    /// eigener Schirm (Einstellungen, Enzyklopädie, Laden) als Kind darin, dann
    /// gehört ESC DEM — sonst schlösse ein Druck beides auf einmal.</para>
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (GetChildCount() > 0) return;
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            OnClose?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Was am Knopf steht, wenn er nichts tut. ⚠ Der Grund gehört
    /// dahin, nicht ins Nichts.</summary>
    public override string _GetTooltip(Vector2 pos)
    {
        int h = Hit(pos);
        if (h == -2) return "Das Menü schliessen";
        if (h < 1) return "";
        int i = h - 1;
        if (Geht(i)) return Labels[i];
        return Grund[i].Length > 0 ? Grund[i]
                                   : $"»{Labels[i]}« ist hier nicht eingehängt.";
    }
}
