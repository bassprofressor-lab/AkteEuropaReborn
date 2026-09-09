namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»EINHEITENLISTE«</b> — <b>Fensterart 22</b> des Originals, mit dessen
/// eigenen Kacheln. Das zweite der drei Auskunftsfenster des Haupt-Menüs
/// (<see cref="MainMenuWindow"/>), und mit 7258 Byte der grösste Zeichner der
/// ganzen Oberfläche.
///
/// <para>Gelesen am 09.09.2026 aus <c>0x476D00</c>; Langfassung in
/// <c>berichte/fensterlisten-fable.md</c>. Jede Zahl unten steht mit ihrer
/// Adresse.</para>
///
/// <code>
///   Anleger 0x459AA9   640 x 320 = 32 x 16 Kacheln
///   0x476DA3  Titel "Einheitenliste" (0x501EF4) auf (10, 2), Farbe 0x96/0xA9
///   0x476F50  Trennlinie (20, 35, 570, 1), Farbe 0x8C   — Reiter »Roboter«
///   0x4778B6  dieselbe Linie, aber 490 breit             — Schiffe
///   0x478175  dieselbe Linie, 490 breit                  — Flugzeuge
///   0x47710C  Auswahlbalken y beginnt bei 39, Schritt 15 (@0x4777A3)
///   0x477191  Auswahlbalken (20, y, 570|490, 15), Farbe 0x8C
///   0x477285  Textzeile y = 15·i + 40
///   0x4777B1  16 Zeilen
///   0x4770FD  Rollbalken (x=600, y=40, 11 Kacheln), in allen drei Reitern
///   0x4788EF  Knopf "Roboter"   (20, 285), 5 Kacheln, Zustand +0x8C3CE0
///   0x47891B  Knopf "Schiffe"  (125, 285), 5 Kacheln, Zustand +0x8C3CE4
///   0x47894A  Knopf "Flugzeuge"(230, 285), 5 Kacheln, Zustand +0x8C3CE8
/// </code>
///
/// <para>⭐ <b>Die drei Knöpfe SIND die Reiter.</b> Der Zeichner ist dreimal
/// fast dasselbe hintereinander, und geschaltet wird über genau die drei
/// Globalen, die auch die Knopfzustände tragen. Ist keine gesetzt, stehen nur
/// Titel, Hintergrund und Knöpfe da.</para>
///
/// <para>⚠ <b>BERICHTIGUNG 09.09.2026 nachmittags.</b> Hier stand, das Original
/// zeige beim Aufmachen eine LEERE Liste und unsere Vorwahl »Roboter« sei eine
/// Abweichung. <b>Das war falsch:</b> der Erzeuger des Fensters setzt die
/// Reiterflagge <c>+0x8C3CE0</c> selbst auf 1 (<c>0x459ACC</c>) — »Roboter« ist
/// auch im Original vorgewählt. Der Gegenschalter
/// <c>--einheitenliste-leer</c> zeigt damit einen Zustand, den es im Original
/// gar nicht gibt; er bleibt nur als Messwerkzeug stehen.</para>
///
/// <para><b>Sieben Spalten, nicht vier</b> (y = 20). Der Vorindex kannte nur
/// die ersten vier; »Fahrwerk«, »Sprit« und »Verbesserung« kommen hinzu — und
/// <b>»Verbesserung« gibt es nur bei den Robotern</b> (kein Aufruf mit x=505 in
/// den zwei anderen Blöcken). Darum ist auch die Trennlinie dort 570 statt 490
/// breit: sie hört auf, wo die letzte Spalte aufhört.</para>
///
/// <para>⭐ <b>Der Rang steht als ZEICHEN vor dem Namen</b> (<c>0x4649F0</c>,
/// gerufen @0x4771CA für Roboter und @0x477B15 für Schiffe — <b>bei den
/// Flugzeugen NICHT</b>). Die Funktion sucht in einer Tafel mit Schrittweite 2
/// ab <c>0x500E21</c> die erste Schwelle, die ≥ dem Rang (<c>+0x28</c>) ist —
/// <b>5, 20, 40, 75, 110, 170, 254, 255</b> — und gibt <c>Index − 0x4C</c>
/// zurück, als Byte also <b>0xB4 + Index</b>. Findet sie keine, gibt sie 0
/// (kein Zeichen).</para>
///
/// <para>⚠⚠ <b>BERICHTIGUNG 09.09.2026 nachmittags.</b> Hier stand »ob unsere
/// Schrift diese acht Zeichen hat, ist nicht gesagt«. <b>Sie hat sie.</b>
/// <c>FONT.CWD</c> führt 160 Sätze à 131 Byte (Zeichen = Satz + 0x20); auf
/// 0xB4…0xBB liegen <b>acht Rangplaketten</b> mit wörtlichen Palettenbytes
/// darin — 0xB4 ist ein leerer 9-Punkt-Einzug (Rang 0), die anderen zunehmend
/// verziert. Unser Ausgeber schneidet nichts ab, er <b>benennt falsch</b>:
/// <c>InterfaceExporter.WriteFont</c> schickt alles ab 0x80 durch cp437, und
/// Satz 0xB4 wird dadurch zu <c>┤</c> = U+2524. Die Glyphen stehen also in
/// <c>akte_font.fnt</c>, nur unter Rahmenlinien-Nummern. Solange das so ist,
/// fragt <see cref="RangText"/> vergeblich und lässt den Rang weg —
/// <see cref="RangZeichenDa"/> sagt dem Prüfstand, was herauskam. Der Weg zur
/// Behebung steht in <c>berichte/fensterlisten-fable-2.md</c>, Abschnitt
/// II.</para>
///
/// <para>⭐⭐ <b>DER KLICKWEG, gelesen am 09.09.2026 nachmittags</b>
/// (<c>berichte/fensterlisten-fable-2.md</c>). Er ist ZWEIGETEILT, und das war
/// vormittags noch zusammengezogen:</para>
/// <list type="bullet">
///   <item><b>Einzelklick</b> (WM_LBUTTONDOWN → Verteiler mit Arg 0): setzt den
///   Cursor auf <c>1000 + Listenplatz</c> (@0x44BDBE) und zeichnet neu
///   (@0x44BFCA). <b>Sonst nichts</b> — kein Sprung, keine Anwahl.</item>
///   <item><b>Doppelklick</b> (WM_LBUTTONDBLCLK → Arg 1) macht VIER Dinge:
///   die bisherige Anwahl aufheben (<c>0x433010</c>), das Objekt anwählen
///   (<c>0x4331E0</c>, nur wenn <c>UKOL &lt; 0x2D</c> @0x44BE53), die Karte
///   zentrieren (@0x44BE31), und <b>das Fenster schliessen</b>
///   (@0x44BF2F).</item>
/// </list>
///
/// <para>⚠ <b>Beides läuft auf dem DRUCK, nicht auf dem Loslassen</b> — im
/// Original ohnehin, und bei uns ist es Pflicht: Godot setzt
/// <c>InputEventMouseButton.DoubleClick</c> auf dem ZWEITEN DRUCK, beim
/// Loslassen ist die Fahne wieder falsch. Genau daran ist bug-115 gescheitert
/// (»doppelklick geht bei keiner einheit«). Die Reiterknöpfe bleiben davon
/// unberührt: die brauchen Druck und Loslassen für ihr Bild.</para>
///
/// <para>⚠ <b>Was bei uns anders heisst:</b> das Original holt den
/// Einheitennamen aus zwei LAUFZEITtafeln (<c>0x51CE22</c> für Entwürfe,
/// <c>0x82F728</c> für Erfindungen), die in der Datei leer sind — wir nehmen
/// <c>LabelOf</c>, also denselben Namen, den auch der Bedienblock zeigt (siehe
/// bug-114). Und die Spalte »Aufbauteil« kommt aus <c>MountName</c> statt aus
/// den Rohbytes +0x0D/+0x0E, aus demselben Grund.</para>
/// </summary>
public sealed partial class UnitListView : Control
{
    /// <summary>Die Fensterart des Originals.</summary>
    public const int Art = 22;

    /// <summary>32 x 16 Kacheln — die 640 x 320 des Anlegers
    /// <c>0x459AA9</c>.</summary>
    public const int WTiles = 32, HTiles = 16;

    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;

    /// <summary>16 Zeilen (@0x4777B1), Höhe 15 (@0x4777A3), Text ab y = 40
    /// (@0x477285), Balken ab y = 39 (@0x47710C).</summary>
    public const int Zeilen = 16, ZeilenHoehe = 15, TextY0 = 40, BalkenY0 = 39;

    private const int KopfY = 20, LinieY = 35, LinieX = 20;

    /// <summary>Der Rollbalken (@0x4770FD) — in allen drei Reitern derselbe.</summary>
    public const int RollX = 600, RollY = 40, RollTiles = 11;

    /// <summary>Die drei Reiterknöpfe (@0x4788EF, @0x47891B, @0x47894A).</summary>
    public const int KnopfY = 285, KnopfTiles = 5;
    private static readonly int[] KnopfX = { 20, 125, 230 };
    public static readonly string[] Reiter = { "Roboter", "Schiffe", "Flugzeuge" };

    /// <summary>Die sieben Spalten-x. Schiffe und Flugzeuge nehmen die ersten
    /// SECHS — »Verbesserung« gibt es dort nicht.</summary>
    private static readonly int[] SpaltenX = { 25, 165, 225, 305, 365, 445, 505 };

    private static readonly string[] Koepfe =
    {
        "Name", "Zustand", "Aufbauteil", "Munition", "Fahrwerk", "Sprit",
        "Verbesserung",
    };

    /// <summary>Die Rangschwellen aus <c>0x500E21</c> (Schrittweite 2).</summary>
    private static readonly int[] RangSchwellen = { 5, 20, 40, 75, 110, 170, 254, 255 };

    /// <summary>Das erste Rangzeichen, <c>0 − 0x4C</c> als Byte
    /// (@0x464A0E).</summary>
    public const int RangZeichen0 = 0xB4;

    /// <summary>Eine Zeile. Der Wirt füllt sie; das Fenster rechnet nichts
    /// aus. −1 in einem Zahlenfeld heisst »diese Spalte zeigt das Original für
    /// diese Einheit nicht« — was etwas anderes ist als 0.</summary>
    public sealed class Zeile
    {
        public string Name = "";
        public int Rang = -1;                 // +0x28; −1 = kein Rangzeichen
        public int Hp, HpMax;
        public string Aufbauteil = "", Fahrwerk = "", Verbesserung = "";
        public int Ammo = -1, AmmoMax = -1;
        public int Fuel = -1, FuelMax = -1;
        public int Index = -1;                // zum Anspringen
    }

    public Action? OnClose;

    /// <summary>Eine Zeile wurde MARKIERT (Einzelklick). Das Original zeichnet
    /// danach nur neu (@0x44BFCA) — mehr braucht der Wirt nicht zu tun.</summary>
    public Action<int>? OnPick;

    /// <summary>Eine Zeile wurde AUSGELÖST (Doppelklick). Der Wirt wählt an,
    /// zentriert die Karte und schliesst das Fenster — siehe den Klassenkopf.
    /// </summary>
    public Action<int>? OnActivate;
    /// <summary>Der Wirt liefert die Zeilen eines Reiters nach.</summary>
    public Func<int, List<Zeile>>? Quelle;

    private readonly List<Zeile> _zeilen = new();
    private int _reiter = -1, _stand, _cursor = -1, _gedrueckt = -1;
    private bool _zieht;

    public int Anzahl => _zeilen.Count;
    public int Reiterwahl => _reiter;
    public int Stand => _stand;

    /// <summary>Die markierte Zeile, −1 = keine. ⚠ Der Prüfstand liest sie:
    /// »das Fenster ist noch offen« beweist nicht, dass der Klick ANKAM.
    /// Diese Zahl beweist es.</summary>
    public int Cursor => _cursor;

    /// <summary>Wie oft eine Zeile gedrückt wurde, und wie oft davon als
    /// DOPPELklick — nur für den Prüfstand.</summary>
    public static int Zeilendrucke, Doppeldrucke;

    /// <summary>Ob die Schrift die Rangzeichen 0xB4…0xBB kennt — für den
    /// Prüfstand.</summary>
    public static bool RangZeichenDa { get; private set; }

    public static bool Usable => WindowChrome.Atlas != null
                                 && WindowChrome.LegacyFont != null;

    /// <summary><c>--einheitenliste-alt</c> — es gibt kein Einheitenlisten-
    /// fenster, der Knopf im Haupt-Menü bleibt gedimmt. Nullmodell von
    /// <c>--einheitenliste-check</c>.</summary>
    public static bool Alt;

    /// <summary><c>--einheitenliste-leer</c> — treu statt bequem: kein Reiter
    /// ist vorgewählt, das Fenster geht LEER auf, wie im Original. ⚠ Der
    /// Gegenschalter zu unserer einzigen bewussten Abweichung.</summary>
    public static bool LeerAufmachen;

    public UnitListView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Das Fenster aufmachen. Ohne <c>--einheitenliste-leer</c> steht
    /// »Roboter« vorgewählt da — UNSERE Setzung, siehe Klassenkopf.</summary>
    public void Oeffnen() => Waehle(LeerAufmachen ? -1 : 0);

    public void Waehle(int reiter)
    {
        _reiter = reiter;
        _zeilen.Clear();
        if (reiter >= 0 && Quelle != null) _zeilen.AddRange(Quelle(reiter));
        _stand = _zeilen.Count <= Zeilen
            ? 0 : Mathf.Clamp(_stand, 0, _zeilen.Count - Zeilen);
        _cursor = -1;
        QueueRedraw();
    }

    /// <summary>Wieviele Spalten dieser Reiter hat: sieben bei den Robotern,
    /// sechs sonst (@0x4778CE gegen @0x476F68).</summary>
    public int SpaltenZahl => _reiter == 0 ? 7 : 6;

    /// <summary>Die Breite der Trennlinie und des Auswahlbalkens: 570 bei den
    /// Robotern (@0x476F50), 490 sonst (@0x4778B6).</summary>
    public int LinieW => _reiter == 0 ? 570 : 490;

    public bool HatRollbalken => _zeilen.Count > Zeilen;
    public int RollAnzahl => Mathf.Max(1, _zeilen.Count - (Zeilen - 1));

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    /// <summary>Das Rangzeichen zu einem Rang (<c>0x4649F0</c>), oder leer.
    /// ⚠ Auch leer, wenn die Schrift das Zeichen nicht kennt.</summary>
    public static string RangText(Font? font, int rang)
    {
        if (rang < 0) return "";
        for (int i = 0; i < RangSchwellen.Length; i++)
        {
            if (RangSchwellen[i] < rang) continue;
            int code = RangZeichen0 + i;               // al − 0x4C @0x464A0E
            if (font == null) { RangZeichenDa = false; return ""; }

            // ⭐ ZWEI Namen für dieselbe Plakette, und beide werden gefragt.
            //
            // Seit dem 09.09.2026 legt der Ausgeber die Plaketten in den
            // Privatbereich (Import.InterfaceExporter.PlaqueBase). Wer seine
            // Inhalte seither NICHT neu eingelesen hat, hat aber noch die alte
            // Schrift, in der sie über cp437 auf Rahmenlinien getauft wurden
            // (Satz 0xB4 → U+2524). ⚠ Beide Fassungen sind im Umlauf, und ein
            // Rang, der nur nach einem Neueinlesen erscheint, wäre für ihn
            // von einem Fehler nicht zu unterscheiden.
            long neu = Import.InterfaceExporter.PlaqueBase + code;
            if (font.HasChar(neu)) { RangZeichenDa = true; return char.ConvertFromUtf32((int)neu); }
            long alt = Import.Cp437.Char((byte)code);
            if (font.HasChar(alt)) { RangZeichenDa = true; return char.ConvertFromUtf32((int)alt); }

            RangZeichenDa = false;
            return "";
        }
        return "";                                     // Rang > 254 → 0 @0x464A0A
    }

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);
        Text(font, TitleX, TitleY, "Einheitenliste", WindowChrome.TitleColour);

        // Die drei Reiterknoepfe — sie stehen IMMER da, auch wenn kein Reiter
        // gewaehlt ist.
        for (int k = 0; k < 3; k++)
        {
            bool an = _reiter == k || _gedrueckt == k;
            WindowChrome.PaintButton(this, KnopfX[k], KnopfY, KnopfTiles, Scale, an);
            float wpx = font.GetStringSize(Reiter[k], HorizontalAlignment.Left, -1,
                                           FontSize).X;
            int bx = KnopfX[k] + (int)((KnopfTiles * WindowChrome.Cell * Scale - wpx)
                                       / 2 / Scale);
            Text(font, bx, KnopfY + (an ? 4 : 3), Reiter[k], WindowChrome.TextColour);
        }

        if (_reiter < 0) return;                       // kein Reiter → nur Knoepfe

        for (int s = 0; s < SpaltenZahl; s++)
            Text(font, SpaltenX[s], KopfY, Koepfe[s], WindowChrome.TextColour);
        DrawRect(new Rect2(LinieX * Scale, LinieY * Scale, LinieW * Scale, 1 * Scale),
                 WindowChrome.LineColour, true);

        for (int i = 0; i < Zeilen; i++)
        {
            int idx = _stand + i;
            if (idx >= _zeilen.Count) break;
            var z = _zeilen[idx];

            if (idx == _cursor)
                DrawRect(new Rect2(LinieX * Scale, (BalkenY0 + ZeilenHoehe * i) * Scale,
                                   LinieW * Scale, ZeilenHoehe * Scale),
                         WindowChrome.LineColour, true);

            int y = TextY0 + ZeilenHoehe * i;
            Text(font, SpaltenX[0], y, RangText(font, z.Rang) + z.Name,
                 WindowChrome.TextColour);
            Text(font, SpaltenX[1], y, $"{z.Hp}/{z.HpMax}",
                 BuildingListView.BruchFarbe(z.Hp, z.HpMax));
            if (z.Aufbauteil.Length > 0)
                Text(font, SpaltenX[2], y, z.Aufbauteil, WindowChrome.TextColour);
            if (z.AmmoMax >= 0)
                Text(font, SpaltenX[3], y, $"{z.Ammo}/{z.AmmoMax}",
                     BuildingListView.BruchFarbe(z.Ammo, z.AmmoMax));
            if (z.Fahrwerk.Length > 0)
                Text(font, SpaltenX[4], y, z.Fahrwerk, WindowChrome.TextColour);
            if (z.FuelMax >= 0)
                Text(font, SpaltenX[5], y, $"{z.Fuel}/{z.FuelMax}",
                     BuildingListView.BruchFarbe(z.Fuel, z.FuelMax));
            if (SpaltenZahl > 6 && z.Verbesserung.Length > 0)
                Text(font, SpaltenX[6], y, z.Verbesserung, WindowChrome.TextColour);
        }

        if (HatRollbalken)
            WindowChrome.PaintScrollbar(this, RollX, RollY, RollTiles,
                                        _stand, RollAnzahl, Scale);
    }

    /// <summary>−2 Schliesskreuz, −3 Rollbalken, 1..16 eine Zeile,
    /// −11/−12/−13 die drei Reiter, sonst 0 (ziehen).</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;
        for (int k = 0; k < 3; k++)
            if (x >= KnopfX[k] && x < KnopfX[k] + KnopfTiles * WindowChrome.Cell
                && y >= KnopfY && y < KnopfY + 20) return -11 - k;
        if (HatRollbalken
            && WindowChrome.ScrollHit(RollX, RollY, RollTiles, RollAnzahl,
                                      new Vector2(x, y)) >= 0) return -3;
        if (_reiter >= 0 && x >= LinieX && x < LinieX + LinieW)
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
        int t = Hit(mb.Position);
        if (mb.Pressed)
        {
            // ⭐ DIE ZEILE WIRD AUF DEM DRUCK AUSGEWERTET — im Original wie bei
            // uns. ⚠ Und die Doppelklickfahne MUSS hier gefragt werden: Godot
            // setzt sie auf dem zweiten DRUCK und nimmt sie beim Loslassen
            // wieder zurueck (bug-115).
            if (t >= 1)
            {
                int z = _stand + (t - 1);
                if (z >= 0 && z < _zeilen.Count)
                {
                    _cursor = z;                       // @0x44BDBE
                    Zeilendrucke++;
                    QueueRedraw();
                    if (mb.DoubleClick)
                    {
                        Doppeldrucke++;
                        OnActivate?.Invoke(_zeilen[z].Index);
                    }
                    else OnPick?.Invoke(_zeilen[z].Index);
                }
                AcceptEvent();
                return;
            }
            _gedrueckt = t <= -11 ? -11 - t : -1;
            if (t == 0) _zieht = true;
            QueueRedraw();
            AcceptEvent();
            return;
        }
        _gedrueckt = -1;
        if (_zieht) { _zieht = false; QueueRedraw(); AcceptEvent(); return; }
        if (t == 0) { QueueRedraw(); return; }
        AcceptEvent();
        if (t == -2) { OnClose?.Invoke(); return; }
        if (t <= -11) { _stand = 0; Waehle(-11 - t); return; }
        if (t == -3)
        {
            int neu = WindowChrome.ScrollHit(RollX, RollY, RollTiles, RollAnzahl,
                                             mb.Position / Scale);
            if (neu >= 0)
            {
                _stand = Mathf.Clamp(neu, 0, Mathf.Max(0, _zeilen.Count - Zeilen));
                QueueRedraw();
            }
        }
    }

    /// <summary>ESC schliesst die Liste — aus demselben Grund wie in
    /// <see cref="BuildingListView"/>: der Baum steht.</summary>
    /// <summary>Wie oft die Liste ueberhaupt eine unbehandelte Eingabe gesehen
    /// hat — nur fuer den Pruefstand. Ohne diese Zahl weiss man bei einem
    /// stummen ESC nicht, ob es NICHT ANKAM oder ob es ANKAM und nichts tat.</summary>
    public static int Eingaben;

    public override void _UnhandledInput(InputEvent @event)
    {
        Eingaben++;
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            OnClose?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    public override string _GetTooltip(Vector2 pos)
    {
        int h = Hit(pos);
        if (h == -2) return "Die Einheitenliste schliessen";
        if (h == -3) return "Rollen";
        if (h <= -11) return Reiter[-11 - h];
        if (h < 1 || _reiter < 0) return "";
        float x = pos.X / Scale;
        for (int s = SpaltenZahl - 1; s >= 0; s--)
            if (x >= SpaltenX[s]) return Koepfe[s];
        return "";
    }
}
