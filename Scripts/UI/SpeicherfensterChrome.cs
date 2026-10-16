namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>»Spiel speichern« (Fensterart 15) und »Spielstand laden« (Fensterart 14)</b> — die
/// zwei Fenster des Originals, MIT dessen Kacheln (16.10.2026, bug-522, KayelGee R5#4:
/// »der Spieler will ausdrücklich das Originalfenster«). Gelesen in
/// <c>berichte/speicherfenster-fable.md</c> (C+F, 0 Unterschiede nach Normierung).
///
/// <para>EINE Klasse für beide Arten: die Zeichenlisten (§2.1/§2.2) sind dieselbe bis auf
/// Höhe, Platzzahl, Knopfzeile und den Eingabemodus — zwei Dateien wären zwei Wahrheiten
/// über denselben Platz. ⚠ UNSERE Aufteilung, nicht die des Originals (dort zwei Zeichner
/// <c>0x473750</c> / <c>0x4733B0</c>, zwei Anleger <c>0x458CB0</c> / <c>0x4589B0</c>).</para>
///
/// <code>
///   Anleger 0x458D5C   Art 15: 300×360 (15×18 Kacheln), Plätze 0..6
///           0x458A5C   Art 14: 300×400 (15×20 Kacheln), Plätze 0..7  (7 = game.007)
///   Zeichner
///     0x4737B3  0x455E50(0,0,15,H)                  Rahmen mit Titelleiste + Kreuz
///     0x4737F6  Titel (10,2) »Spiel speichern« / »Spielstand laden«, Paar 0x96/0xA9
///     0x473895  0x456A50(20, 25+40i, 13, 2)         Innenrahmen 260×40 je Platz
///     0x4738F5  fill(26, 31+40i, 248, 28, 0x8C)     Markierung (Cursor == 1000+i)
///     0x473A75  Art 15 gewählt: NUR Name + »_« bei (40, 44+40i)   (Sprung 0x47395C)
///     0x473991  leer (Name[0]==0): »Leere Position...« (40, 44+40i), Paar 0x24/0x28
///     0x4739CE  Spielername (40, 31+40i) · 0x473A57 »Mission : n« (200, 31+40i)
///     0x473A75  Spielstandname (40, 44+40i)
///     0x473AC0  Knöpfe 5 Kacheln bei (40 | 160, 315)  »Sichern«/»Abbruch«
///     0x473649            Art 14:   (40 | 160, 355)  »Laden«/»Abbruch«
///   Treffer 0x45EDD3 (15) / 0x45EC45 (14): Titel −5, aussen −1, Kreuz −2, Knöpfe 1/2,
///           Platz 1000+i — Art 15 IMMER, Art 14 NUR wenn Name[0] ≠ 0 (0x45ED8B), sonst 0
///   Klick   0x44B52E (15): Platz nur wählbar, solange KEINER gewählt (0x44B568);
///           Sichern ohne Platz = nichts; Sichern → Bus 991 (Platz, Name), zu
///           0x4486A0 (14): Platz immer wählbar; Doppelklick → Laden (0x4486D9);
///           Laden ohne Platz = nichts; Laden → Bus 990, zu
///   Eingabe 0x4588E0: an den VORHANDENEN Namen anhängen, max 20, 0x20..0x7A ohne [ ] ^,
///           Backspace; Enter (0x487A64 → 0x413F9F) = Klick auf »Sichern«
/// </code>
///
/// <para>⚠ <b>UNSERES / SETZUNG</b>, je einzeln:</para>
/// <list type="bullet">
/// <item>Der Titel und »Leere Position...« in EINER Farbe (0x96 bzw. 0x24) statt des
/// Farbpaares des Titelzeichners — wie bei allen Kachelfenstern (<see cref="LokatorChrome"/>).</item>
/// <item>ESC schliesst NUR dieses Fenster. Im Original schliesst das KEYDOWN-ESC
/// (<c>0x41304B → 0x44FE10(0)</c>) ALLE Fenster ausser 9/35/46/48 — das tun wir nicht, weil
/// unser ESC sonst auch das Haupt-Menü-Ziel änderte. Der ESC-Zweig im Zeichner
/// (Namen zurück, <c>0x473809</c>) ist im Original tot und fehlt darum auch hier.</item>
/// <item>Keine Zublende bei Art 15: das Fenster hält den Baum an, und die Blende dreht
/// <see cref="WindowManager.Takt"/> am SimTick — wie beim Haupt-Menü: <c>Wegnehmen</c>.</item>
/// <item>Das Druckbild von »Sichern« nach einem Enter ohne Platz (<c>+0x8C3CE0 := 1</c>)
/// wird nicht nachgezeichnet.</item>
/// </list>
///
/// <para>⭐ <b>Folge aus dem Code, keine Setzung</b> (§3.3): ein Zeichen, das getippt wird,
/// BEVOR ein Platz gewählt ist, bleibt in <c>+0x8C3D58</c> liegen und wird beim nächsten
/// Zeichnen — also nach dem Platzklick — angehängt. Das ist <see cref="_zeichen"/>.</para>
///
/// <para>Der Gegenschalter <c>--speicherfenster-alt</c> (<see cref="Alt"/>) stellt den alten
/// Weg her: Knopf → sofort <c>save_&lt;Unixzeit&gt;</c>, Laden über <see cref="LoadGameScreen"/>,
/// kein F2/F3.</para>
/// </summary>
public sealed partial class SpeicherfensterChrome : Control
{
    public const int ArtLaden = 14, ArtSpeichern = 15;
    public const int WTiles = 15, Scale = 2;

    public const int PlatzX = 20, PlatzY = 25, PlatzSchritt = 40, PlatzW = 260, PlatzH = 40;
    public const int MarkX = 26, MarkY = 31, MarkW = 248, MarkH = 28;
    public const int TextX = 40, MissionX = 200, Zeile1Y = 31, Zeile2Y = 44;
    public const int KnopfTiles = 5;
    public static readonly int[] KnopfX = { 40, 160 };

    /// <summary>»Leere Position...« — Paar 0x24/0x28, wir zeichnen 0x24 = (187,179,179).</summary>
    public static readonly Color LeerFarbe = Color.Color8(187, 179, 179);

    /// <summary><c>--speicherfenster-alt</c> — der alte Weg (sofort speichern, LoadGameScreen).</summary>
    public static bool Alt
    {
        get => _alt ??= Array.IndexOf(Core.CommandLine.Args, "--speicherfenster-alt") >= 0;
        set => _alt = value;
    }
    private static bool? _alt;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    /// <summary>14 oder 15.</summary>
    public readonly int Art;
    public bool Speichern => Art == ArtSpeichern;
    public int HTiles => Speichern ? 18 : 20;
    public int Plaetze => Speichern ? 7 : 8;
    public int KnopfY => Speichern ? 315 : 355;
    private string Titel => Speichern ? "Spiel speichern" : "Spielstand laden";
    private string[] KnopfText => Speichern ? new[] { "Sichern", "Abbruch" } : new[] { "Laden", "Abbruch" };

    // ---- der Fenstersatz -------------------------------------------------------
    /// <summary><c>+0x8B9046 + 21·p</c> — die Namen; im Speicherfenster ist das zugleich
    /// der EINGABEPUFFER des gewählten Platzes.</summary>
    public readonly string[] Namen = new string[Core.SaveGame.Plaetze];
    public readonly int[] Missionen = new int[Core.SaveGame.Plaetze];
    public readonly string[] Spieler = new string[Core.SaveGame.Plaetze];

    /// <summary><c>word[+0x8B9044]</c>: 0 = keiner, 1000 + p = Platz p.</summary>
    public int Cursor { get; private set; }

    /// <summary><c>byte[+0x8C3D58]</c> — das letzte WM_CHAR-Zeichen (Schreiber 0x413F17).</summary>
    private int _zeichen;

    /// <summary>Bus 991: (Platz, Name) schreiben.</summary>
    public Action<int, string>? OnSichern;
    /// <summary>Bus 990: Platz laden.</summary>
    public Action<int>? OnLaden;
    /// <summary>Nach dem Schliessen (Pausenkopplung 0x44730B, Aufräumen).</summary>
    public Action? OnZu;

    /// <summary>true = in der <see cref="WindowManager"/>-Liste (im Spiel); im Startmenü
    /// steht das Fenster allein (dort dreht niemand den Fenstertakt).</summary>
    public bool Verwaltet;

    private int _held = -1;
    private bool _zieht;

    /// <summary>Offen im Sinn des Fenstersatzes — bei Art 14 läuft nach dem Schliessen noch
    /// die Zublende, und in der Zeit soll kein Klick mehr greifen.</summary>
    public bool Offen { get; private set; }

    public SpeicherfensterChrome(int art)
    {
        Art = art == ArtLaden ? ArtLaden : ArtSpeichern;
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        // Art 15 hält den Baum an — ohne Always bekäme das Fenster selbst keinen Klick.
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        TooltipText = " ";
    }

    /// <summary>Der Anleger <c>0x458D6E</c>: je Platz die Datei öffnen und +5/+26/+55 lesen;
    /// Cursor := 0 (die 44324 Byte werden genullt).</summary>
    public void Einlesen()
    {
        for (int p = 0; p < Core.SaveGame.Plaetze; p++)
        {
            var k = p < Plaetze ? Core.SaveGame.PlatzLesen(p) : null;
            Namen[p] = k?.Name ?? "";
            Missionen[p] = k?.Mission ?? 0;
            Spieler[p] = k?.Spieler ?? "";
        }
        Cursor = 0;
        _zeichen = 0;
        _held = -1;
        QueueRedraw();
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    public override void _Draw()
    {
        var f = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || f == null) return;
        WindowChrome.Paint(this, WTiles, HTiles, Scale);                     // 0x4737B3
        Text(f, 10, 2, Titel, WindowChrome.TitleColour);                     // 0x4737F6

        for (int i = 0; i < Plaetze; i++)
        {
            int y = PlatzSchritt * i;
            WindowChrome.PaintInnerFrame(this, PlatzX, PlatzY + y, 13, 2, Scale);   // 0x473895
            bool gewaehlt = Cursor == 1000 + i;
            if (gewaehlt)                                                    // 0x4738F5
                DrawRect(new Rect2(MarkX * Scale, (MarkY + y) * Scale, MarkW * Scale, MarkH * Scale),
                         WindowChrome.LineColour);
            if (gewaehlt && Speichern)
            {
                // Eingabemodus: nur Name + Schreibmarke (Sprung 0x47395C)
                Text(f, TextX, Zeile2Y + y, Namen[i] + "_", WindowChrome.TextColour);
                continue;
            }
            if (Namen[i].Length == 0)                                        // 0x473991
            {
                Text(f, TextX, Zeile2Y + y, "Leere Position...", LeerFarbe);
                continue;
            }
            Text(f, TextX, Zeile1Y + y, Spieler[i], WindowChrome.TextColour);            // 0x4739CE
            Text(f, MissionX, Zeile1Y + y, $"Mission : {Missionen[i]}", WindowChrome.TextColour);  // 0x473A57
            Text(f, TextX, Zeile2Y + y, Namen[i], WindowChrome.TextColour);              // 0x473A75
        }

        var kt = KnopfText;
        for (int i = 0; i < 2; i++)                                          // 0x473AC0 / 0x473AEF
        {
            bool g = _held == i;
            WindowChrome.PaintButton(this, KnopfX[i], KnopfY, KnopfTiles, Scale, g);
            float w = f.GetStringSize(kt[i], HorizontalAlignment.Left, -1, FontSize).X;
            int tx = KnopfX[i] + (KnopfTiles * WindowChrome.Cell - (int)(w / Scale)) / 2;
            Text(f, tx, KnopfY + (g ? 4 : 3), kt[i], WindowChrome.TextColour);
        }
    }

    /// <summary>Der Treffertest <c>0x45EDD3</c> (Art 15) bzw. <c>0x45EC45</c> (Art 14), in
    /// Fensterpunkten (also mal <see cref="Scale"/>).</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (x >= w - 20 && y < 20) return -2;
        if (y < 20) return -5;
        for (int i = 0; i < 2; i++)
            if (x >= KnopfX[i] && x < KnopfX[i] + KnopfTiles * WindowChrome.Cell
                && y >= KnopfY && y < KnopfY + 20) return 1 + i;
        for (int i = 0; i < Plaetze; i++)
        {
            int py = PlatzY + PlatzSchritt * i;
            if (x >= PlatzX && x < PlatzX + PlatzW && y >= py && y < py + PlatzH)
                // ⭐ Art 14 trifft nur belegte Plätze (0x45ED8B), Art 15 immer (0x45EF25)
                return Speichern || Namen[i].Length > 0 ? 1000 + i : 0;
        }
        return 0;
    }

    /// <summary>Hilfezeile 96 »Spielstand-Position« (Art 15) bzw. 95 »Speichern des
    /// Spielstands« (Art 14 — so steht es im Original, auch im Ladefenster). Knöpfe, Titel
    /// und Kreuz setzen keine.</summary>
    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) >= 1000 ? (Speichern ? "Spielstand-Position" : "Speichern des Spielstands") : "";

    /// <summary>Der Klickarm <c>0x44B52E</c> (15) bzw. <c>0x4486A0</c> (14). Gibt zurück,
    /// was geschah — für den Prüfstand.</summary>
    public string Klick(int element, bool doppel = false)
    {
        if (!Offen) return "zu (schon)";
        if (element == -2) { Schliessen(); return "zu"; }
        if (element == 2) { Schliessen(); return "zu (Abbruch)"; }
        if (element == 1)
        {
            if (Cursor == 0) { QueueRedraw(); return "nichts (kein Platz)"; }
            int p = Cursor - 1000;
            if (Speichern)
            {
                string n = Namen[p];
                Schliessen();                   // erst zu (Pause aus), dann schreibt der Empfänger
                OnSichern?.Invoke(p, n);
                return $"gesichert {p} »{n}«";
            }
            Schliessen();
            OnLaden?.Invoke(p);
            return $"geladen {p}";
        }
        if (element >= 1000)
        {
            if (Speichern)
            {
                // 0x44B568: cmp word[Cursor],0 / jne — der erste Platz bleibt gewählt
                if (Cursor == 0)
                {
                    Cursor = element;
                    ZeichenVerarbeiten();       // das liegengebliebene Byte (§3.3)
                }
                QueueRedraw();
                return $"Platz {Cursor - 1000}";
            }
            Cursor = element;                   // 0x4486D2, bedingungslos
            if (doppel) return Klick(1);        // 0x4486D9: Element := 1, zurück an den Anfang
            QueueRedraw();
            return $"Platz {Cursor - 1000}";
        }
        QueueRedraw();
        return "nichts";
    }

    /// <summary>Die Tastenverarbeitung im Zeichner (<c>0x4737FE</c>) und die Eingabe
    /// <c>0x4588E0(W, Name, 20)</c>. Verbraucht <see cref="_zeichen"/>.</summary>
    private void ZeichenVerarbeiten()
    {
        int c = _zeichen;
        if (c == 0 || Cursor == 0) return;
        _zeichen = 0;
        if (c == 0x0D) return;                  // nur das Druckbild von »Sichern« (nicht nachgebaut)
        int p = Cursor - 1000;
        string n = Namen[p];
        if (c >= 0x20 && c <= 0x7A && c is not ('[' or ']' or '^') && n.Length < Core.SaveGame.NameMax)
            Namen[p] = n + (char)c;
        else if (c == 8 && n.Length > 0)
            Namen[p] = n[..^1];
        QueueRedraw();
    }

    /// <summary>Ein WM_CHAR — der Weg <c>0x413E55 → 0x413F17 → 0x487A10</c>. Öffentlich für
    /// den Prüfstand; <see cref="_Input"/> ruft dasselbe.</summary>
    public void Zeichen(int c)
    {
        if (!Offen || !Speichern) return;       // Art 14: Arm 6 = nichts
        _zeichen = c;                           // 0x413F17
        if (Cursor == 0) return;                // 0x487A64: Rückgabe 0, das Byte bleibt stehen
        if (c == 0x0D) { _zeichen = 0; Klick(1); return; }   // Rückgabe 2 → 0x413F9F
        ZeichenVerarbeiten();
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible || !Offen) return;
        if (_zieht)
        {
            if (@event is InputEventMouseMotion mm)
            {
                Position += mm.Relative;
                var vp = GetViewportRect().Size;
                Position = new Vector2(Mathf.Clamp(Position.X, 0, Mathf.Max(0, vp.X - Size.X)),
                                       Mathf.Clamp(Position.Y, 0, Mathf.Max(0, vp.Y - Size.Y)));
                AcceptEvent();
            }
            else if (@event is InputEventMouseButton up
                     && up.ButtonIndex == MouseButton.Left && !up.Pressed)
            { _zieht = false; AcceptEvent(); }
            return;
        }
        if (@event is not InputEventKey k || !k.Pressed) return;
        // ESC kommt als KEYDOWN zuerst und schliesst (0x41304B). Fensterwelt-Regel: ESC
        // im Fenster selbst, weil der Baum bei Art 15 steht.
        if (k.Keycode == Key.Escape) { Schliessen(); AcceptEvent(); return; }
        if (!Speichern) return;                 // Art 14 nimmt keine Tasten
        int c = k.Keycode switch
        {
            Key.Backspace => 8,
            Key.Enter or Key.KpEnter => 0x0D,
            _ => k.Unicode is > 0 and < 0x10000 ? (int)k.Unicode : 0,
        };
        if (c == 0) return;
        Zeichen(c);
        AcceptEvent();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || !Offen) return;
        // Rechtsklick schliesst (0x41428B → 0x4142D7)
        if (mb.ButtonIndex == MouseButton.Right && mb.Pressed) { Schliessen(); AcceptEvent(); return; }
        if (mb.ButtonIndex != MouseButton.Left) return;
        int t = Hit(mb.Position);
        if (mb.Pressed)
        {
            AcceptEvent();
            // WM_LBUTTONDBLCLK (0x4141B4): Element ≠ 0 → 0x4485D0(1)
            if (mb.DoubleClick && t >= 1000) { _held = -1; Klick(t, doppel: true); return; }
            _held = t is 1 or 2 ? t - 1 : -1;
            if (t == -5) _zieht = true;
            QueueRedraw();
            return;
        }
        _held = -1;
        QueueRedraw();
        AcceptEvent();
        if (t is -2 or 1 or 2 || t >= 1000) Klick(t);
    }

    /// <summary>Aufmachen: anlegen (<see cref="Einlesen"/>), sichtbar. Die Lage setzt der
    /// Öffner (Maus − 3 bzw. (200, 100) im Startmenü).</summary>
    public void Oeffnen()
    {
        Einlesen();
        Offen = true;
        Visible = true;
        QueueRedraw();
    }

    /// <summary><c>0x4471A0</c>: zu. Art 15 ohne Blende (Baum steht), Art 14 mit.</summary>
    public void Schliessen()
    {
        if (!Offen) return;
        Offen = false;
        _held = -1; _zieht = false;
        if (Verwaltet && !Speichern) WindowManager.Schliessen(WindowManager.Offen(Art));
        else
        {
            if (Verwaltet) WindowManager.Wegnehmen(WindowManager.Offen(Art));
            Visible = false;
        }
        OnZu?.Invoke();
    }
}
