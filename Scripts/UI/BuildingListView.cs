namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»GEBÄUDELISTE«</b> — <b>Fensterart 27</b> des Originals, mit dessen
/// eigenen Kacheln. Das erste der drei Auskunftsfenster, auf die das
/// Haupt-Menü (<see cref="MainMenuWindow"/>) zeigt.
///
/// <para>Gelesen am 09.09.2026 aus dem Zeichner <c>0x47BB10</c> (2650 B);
/// Langfassung in <c>berichte/fensterlisten-fable.md</c>. Jede Zahl unten ist
/// aus dem Befehlsstrom abgelesen, die Adresse steht dabei.</para>
///
/// <code>
///   Anleger 0x459D29   620 x 300 = 31 x 15 Kacheln
///   0x47BBB3  Titel "Gebäudeliste" (0x501EE4) auf (10, 2), Farbe 0x96/0xA9
///   0x47BC97  Trennlinie (20, 35, 550, 1), Farbe 0x8C
///   Spaltenköpfe alle auf y = 20:
///     25 "Name"     225 "Zustand"  285 "Status"
///    385 "]"        425 "["        465 "{"       505 "^"      545 "}"
///   0x47BE6E  Auswahlbalken y beginnt bei 39, Schritt 15 (@0x47C43D)
///   0x47BEEB  Auswahlbalken (20, y, 550, 15), Farbe 0x8C
///   0x47BF24  Textzeile y = 15·i + 40
///   0x47C44B  16 Zeilen
///   0x47BE5F  Rollbalken (x=580, y=40, 11 Kacheln), Anzahl = Einträge − 15
/// </code>
///
/// <para><b>Der Filter ist der des Originals</b> (@0x47BBF2…0x47BC12): eine
/// Gebäudeart kommt in die Liste, wenn sie <b>nicht</b> 0, 8, 11, 13 oder 14
/// ist und <b>≤ 16</b> — und wenn das Gebäude dem Betrachter gehört. ⚠ Art 11
/// (Hafen) fällt damit heraus, obwohl der Namensgeber <c>0x459110</c> eigens
/// ein Präfix für sie führt. Das ist das Original, nicht unser Versehen.</para>
///
/// <para>⭐ <b>Die vier Zeichen <c>] [ { ^</c> sind keine Buchstaben.</b> Der
/// Textzeichner des Originals (<c>0x4BA420</c>) gibt sechs Zeichen über die
/// Tafel <c>0x4BA504</c> ein eigenes Farbpaar — es sind die Rohstoffsymbole.
/// Welche Spalte welcher Rohstoff ist, sagt der Zeichner selbst: die
/// Waffenfabrik füllt nur <c>]</c> und <c>^</c>, die Fahrwerkfabrik nur
/// <c>[</c> und <c>^</c>, die Spezialfabrik nur <c>{</c> und <c>^</c>. Also
/// <b>] = Waffen, [ = Fahrwerk, { = Spezial, ^ = Terranium</b>. ⚠ Das ist die
/// einzige VERMUTUNG in diesem Fenster; sie steht auch im Hinweistext der
/// Spalte, damit niemand sie für gelesen hält.</para>
///
/// <para>⚠ <b>Die achte Spalte <c>}</c> bei x=545 zeichnet NICHTS.</b> Der Kopf
/// steht da, aber in der ganzen Zeilenschleife gibt es keinen Zeichenaufruf mit
/// x=0x221. Wir malen den Kopf mit und lassen die Spalte leer — so, wie das
/// Original es tut. Wer den Kopf weglässt, verschweigt eine offene Frage.</para>
///
/// <para>⭐⭐ <b>Und das Bild beantwortet sie halb.</b> Sobald das Fenster mit
/// der Schrift des Originals läuft, sieht man, was die fünf Zeichen sind:
/// <b>echte Sinnbilder</b> — und <c>}</c> ist ein <b>BLITZ</b>. Die leere
/// achte Spalte ist also die <b>Stromspalte</b>. Das passt genau auf das, was
/// beim Minenfenster schon aufgefallen war: eine Stromabrechnung JE GEBÄUDE
/// führt das Original nicht, nur die des Spielers (<c>0x440270</c>). Die Spalte
/// war vorgesehen und ist nie gefüllt worden. ⚠ Der Blitz ist gesehen, die
/// Deutung »Strom« ist damit sehr gut gestützt, aber es bleibt eine
/// Deutung.</para>
///
/// <para>⭐⭐ <b>HIER WEICHEN WIR BEWUSST AB — seine Entscheidung vom
/// 09.09.2026.</b> Nachmittags gelesen
/// (<c>berichte/fensterlisten-fable-2.md</c>): das Original hat für dieses
/// Fenster <b>gar keinen Zeilenklick</b>. Sein Arm im Klickverteiler ist
/// <c>0x44DC79</c>, ein LEERER AUSGANG; die 16 Zeilenrechtecke stehen zwar im
/// Treffertest (@0x460255), aber der Verteiler verwirft sie — <b>der Cursor
/// dieses Fensters wird nie gesetzt, und der Auswahlbalken @0x47BEC1 ist toter
/// Code.</b> Auch die zwei Pfeilkacheln sind wirkungslos: niemand setzt
/// <c>+0x8C3CE0/E4</c> für Art 27, gerollt wird allein über den Rollbalken.</para>
///
/// <para>Auf die Frage »treu oder Verbesserung« hat er entschieden: <b>die
/// Verbesserung bleibt.</b> Zeilen sind bei uns anklickbar, der Doppelklick
/// springt und schliesst — dieselbe Bedienung wie in der Einheitenliste, wo sie
/// GELESEN ist. Das Original hat hier erkennbar eine Schablone stehenlassen und
/// keine Entscheidung getroffen. ⚠ Wer das zurücknehmen will, findet in
/// <see cref="ZeilenklickAus"/> den Schalter.</para>
///
/// <para>⚠ <b>Was wir NICHT haben:</b> das Original wählt sein Statuswort über
/// typeigene Laufzeittafeln (<c>0x878E5A</c>, <c>0x87A2C2</c>, <c>0x87943A</c>,
/// <c>0x878AD2</c>); wir haben stattdessen <c>Entity.State</c>. Die WÖRTER sind
/// die des Originals, die Zuordnung Zustandszahl → Wort ebenfalls — nur der
/// Weg dorthin ist unserer.</para>
/// </summary>
public sealed partial class BuildingListView : Control
{
    /// <summary>Die Fensterart des Originals.</summary>
    public const int Art = 27;

    /// <summary>31 x 15 Kacheln — die 620 x 300 des Anlegers
    /// <c>0x459D29</c>.</summary>
    public const int WTiles = 31, HTiles = 15;

    /// <summary>Wie bei allen unseren Fenstern doppelt. UNSERE Setzung.</summary>
    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;

    /// <summary>16 Zeilen (@0x47C44B), Höhe 15 (@0x47C43D), Text ab y = 40
    /// (@0x47BF24), Balken ab y = 39 (@0x47BE6E).</summary>
    public const int Zeilen = 16, ZeilenHoehe = 15, TextY0 = 40, BalkenY0 = 39;

    /// <summary>Kopfzeile und Trennlinie (@0x47BCAF, @0x47BC97).</summary>
    private const int KopfY = 20, LinieY = 35, LinieX = 20, LinieW = 550;

    /// <summary>Der Rollbalken (@0x47BE5F).</summary>
    public const int RollX = 580, RollY = 40, RollTiles = 11;

    /// <summary>Die acht Spalten, x aus dem Befehlsstrom.</summary>
    private static readonly int[] SpaltenX =
        { 25, 225, 285, 385, 425, 465, 505, 545 };

    /// <summary>Die acht Köpfe, wörtlich aus der EXE (<c>0x502100</c>,
    /// <c>0x5020F4</c>, <c>0x50192C</c>, <c>0x4F650C</c>, <c>0x50170C</c>,
    /// <c>0x501708</c>, <c>0x501704</c>, <c>0x502180</c>).</summary>
    private static readonly string[] Koepfe =
        { "Name", "Zustand", "Status", "]", "[", "{", "^", "}" };

    /// <summary>Was die vier Symbolspalten bedeuten — nur für den Hinweistext.
    /// ⚠ VERMUTUNG, siehe Klassenkopf.</summary>
    private static readonly string[] SymbolSinn =
    {
        "", "", "", "Waffen", "Fahrwerk", "Spezial", "Terranium",
        "Strom — das Sinnbild ist ein Blitz, aber schon das Original zeichnet in dieser Spalte nichts.",
    };

    /// <summary>Eine Zeile. Der Wirt füllt sie; dieses Fenster rechnet
    /// nichts aus.</summary>
    public sealed class Zeile
    {
        public string Name = "";
        public int Hp, HpMax;
        public string Status = "";
        /// <summary>Waffen, Fahrwerk, Spezial, Terranium — oder −1, wo das
        /// Original für diese Gebäudeart nichts zeigt.</summary>
        public int StockW = -1, StockF = -1, StockS = -1, StockT = -1;
        /// <summary>Der Platz, damit der Klick etwas anspringen kann.</summary>
        public int Slot = -1;
    }

    public Action? OnClose;

    /// <summary>Eine Zeile wurde MARKIERT (Einzelklick).</summary>
    public Action<int>? OnPick;

    /// <summary>Eine Zeile wurde AUSGELÖST (Doppelklick) — springen und
    /// schliessen, wie in <see cref="UnitListView"/>.</summary>
    public Action<int>? OnActivate;

    private readonly List<Zeile> _zeilen = new();
    private int _stand;                        // Rollstand, +0x8B9816
    private int _cursor = -1;                  // Auswahl, 1000+i im Original
    private bool _zieht;

    /// <summary>Wieviele Einträge die Liste hat — für den Prüfstand.</summary>
    public int Anzahl => _zeilen.Count;

    /// <summary>Der Rollstand, 0 … Anzahl−16.</summary>
    public int Stand => _stand;

    public static bool Usable => WindowChrome.Atlas != null
                                 && WindowChrome.LegacyFont != null;

    /// <summary><c>--zeilenklick-aus</c> — die Treue statt der Bequemlichkeit:
    /// die Zeilen sind nicht anklickbar, wie im Original. Der Gegenschalter zu
    /// seiner Entscheidung vom 09.09.2026, siehe Klassenkopf.</summary>
    public static bool ZeilenklickAus;

    /// <summary><c>--gebaeudeliste-alt</c> — der Stand vor dem 09.09.2026:
    /// es gibt kein Gebäudelistenfenster, der Knopf im Haupt-Menü bleibt
    /// gedimmt. Der Gegenschalter zu dieser Änderung und das Nullmodell von
    /// <c>--gebaeudeliste-check</c>.</summary>
    public static bool Alt;

    public BuildingListView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    public void Zeige(IEnumerable<Zeile> zeilen)
    {
        _zeilen.Clear();
        _zeilen.AddRange(zeilen);
        // Die Klemme des Originals (@0x47BC49…0x47BC71): passt alles hinein,
        // steht der Balken auf 0; sonst hoechstens Anzahl−16.
        _stand = _zeilen.Count <= Zeilen
            ? 0 : Mathf.Clamp(_stand, 0, _zeilen.Count - Zeilen);
        _cursor = -1;
        QueueRedraw();
    }

    /// <summary>Ob ein Rollbalken erscheint — im Original erst, wenn mehr
    /// Einträge da sind als Zeilen (<c>cmp cx, 0x10</c> @0x47BE2D).</summary>
    public bool HatRollbalken => _zeilen.Count > Zeilen;

    /// <summary>Die »Anzahl«, die der Rollbalken bekommt: Einträge − 15
    /// (<c>sub cx, 0xf</c>, dieselbe Rechnung wie in Art 22 @0x4770D1).</summary>
    public int RollAnzahl => Mathf.Max(1, _zeilen.Count - (Zeilen - 1));

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    /// <summary>Die Farbe eines Bruchs »a/b« (@0x479070, @0x47C015…0x47C069):
    /// über 50 % Fliesstext, 10…50 % 0x97, darunter 0x9B.</summary>
    public static Color BruchFarbe(int a, int b)
    {
        int p = b > 0 ? 100 * a / b : 0;
        if (p > 50) return WindowChrome.TextColour;
        return p >= 10 ? WindowChrome.WarnColour : WindowChrome.AlarmColour;
    }

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);
        Text(font, TitleX, TitleY, "Gebäudeliste", WindowChrome.TitleColour);

        for (int s = 0; s < SpaltenX.Length; s++)
            Text(font, SpaltenX[s], KopfY, Koepfe[s], WindowChrome.TextColour);
        DrawRect(new Rect2(LinieX * Scale, LinieY * Scale,
                           LinieW * Scale, 1 * Scale),
                 WindowChrome.LineColour, true);

        for (int i = 0; i < Zeilen; i++)
        {
            int idx = _stand + i;
            if (idx >= _zeilen.Count) break;           // @0x47BEB0
            var z = _zeilen[idx];

            if (idx == _cursor)                        // @0x47BEC1
                DrawRect(new Rect2(LinieX * Scale, (BalkenY0 + ZeilenHoehe * i) * Scale,
                                   LinieW * Scale, ZeilenHoehe * Scale),
                         WindowChrome.LineColour, true);

            int y = TextY0 + ZeilenHoehe * i;
            Text(font, SpaltenX[0], y, z.Name, WindowChrome.TextColour);
            Text(font, SpaltenX[1], y, $"{z.Hp}/{z.HpMax}", BruchFarbe(z.Hp, z.HpMax));
            Text(font, SpaltenX[2], y, z.Status, WindowChrome.TextColour);
            Lager(font, 3, y, z.StockW);
            Lager(font, 4, y, z.StockF);
            Lager(font, 5, y, z.StockS);
            Lager(font, 6, y, z.StockT);
            // Spalte 7 (»}«, x=545) bleibt leer — auch im Original.
        }

        if (HatRollbalken)                             // @0x47BE2D
            WindowChrome.PaintScrollbar(this, RollX, RollY, RollTiles,
                                        _stand, RollAnzahl, Scale);
    }

    private void Lager(Font font, int spalte, int y, int wert)
    {
        if (wert < 0) return;                          // Art zeigt diese Spalte nicht
        Text(font, SpaltenX[spalte], y, wert.ToString(), WindowChrome.TextColour);
    }

    /// <summary>Das Schliesskreuz gibt −2, eine Zeile 1..16, der Rollbalken
    /// −3, sonst 0 (ziehen).</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;
        if (HatRollbalken
            && WindowChrome.ScrollHit(RollX, RollY, RollTiles, RollAnzahl,
                                      new Vector2(x, y)) >= 0) return -3;
        if (!ZeilenklickAus && x >= LinieX && x < LinieX + LinieW)
            for (int i = 0; i < Zeilen; i++)
            {
                int oben = BalkenY0 + ZeilenHoehe * i;
                if (y >= oben && y < oben + ZeilenHoehe
                    && _stand + i < _zeilen.Count) return i + 1;
            }
        return 0;
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
        if (!mb.Pressed) { if (_zieht) { _zieht = false; AcceptEvent(); } return; }

        int t = Hit(mb.Position);
        // ⭐ Die Zeile wird auf dem DRUCK ausgewertet, und die Doppelklickfahne
        // MUSS hier gefragt werden — Godot setzt sie auf dem zweiten Druck und
        // nimmt sie beim Loslassen zurueck (bug-115).
        if (t >= 1)
        {
            int z = _stand + (t - 1);
            if (z >= 0 && z < _zeilen.Count)
            {
                _cursor = z;
                QueueRedraw();
                if (mb.DoubleClick) OnActivate?.Invoke(_zeilen[z].Slot);
                else OnPick?.Invoke(_zeilen[z].Slot);
            }
            AcceptEvent();
            return;
        }
        if (t == 0) { _zieht = true; AcceptEvent(); return; }
        AcceptEvent();
        if (t == -2) { OnClose?.Invoke(); return; }
        if (t == -3)
        {
            // Der Rollbalken wertet im Original SELBST aus (0x457140) — bei uns
            // fragt der Klickweg dieselbe Rechnung.
            int neu = WindowChrome.ScrollHit(RollX, RollY, RollTiles, RollAnzahl,
                                             mb.Position / Scale);
            if (neu >= 0)
            {
                _stand = Mathf.Clamp(neu, 0, Mathf.Max(0, _zeilen.Count - Zeilen));
                QueueRedraw();
            }
        }
    }

    /// <summary>ESC schliesst die Liste.
    ///
    /// <para>⚠ Aus demselben Grund wie in <see cref="MainMenuWindow"/>: der
    /// Baum steht, der <c>MapViewer</c> sieht die Taste nicht. Und weil dieses
    /// Fenster NACH dem Menü eingehängt wird, bekommt es die Taste zuerst und
    /// verbraucht sie — ESC schliesst also erst die Liste und beim zweiten Mal
    /// das Menü, nicht beides auf einmal.</para></summary>
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
        if (h == -2) return "Die Gebäudeliste schliessen";
        if (h == -3) return "Rollen";
        if (h < 1) return "";
        // ⚠ Der Hinweis nennt die Spalte, ueber der der Zeiger steht — und bei
        // den vier Symbolspalten sagt er dazu, dass die Deutung eine VERMUTUNG
        // ist. Ein Symbol, das man nicht lesen kann, braucht das.
        float x = pos.X / Scale;
        for (int s = SpaltenX.Length - 1; s >= 0; s--)
            if (x >= SpaltenX[s])
                return SymbolSinn[s].Length > 0
                    ? (s == 7 ? SymbolSinn[s]
                              : $"»{Koepfe[s]}« — vermutlich {SymbolSinn[s]}")
                    : Koepfe[s];
        return "";
    }
}
