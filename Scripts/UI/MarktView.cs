namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»GESCHÄFTSZENTRUM«</b> — das Fenster des Handelspostens (Gebäudeart 17),
/// <b>Fensterart 33</b> des Originals, mit dessen eigenen Kacheln (13.09.2026).
///
/// <para>Seine Meldung aus Kampagne 11: »Das Geschäftszentrum hat ein eigenes
/// Menu, wir nutzen wieder das der Basis, das muss behoben werden.« Die ganze
/// Lesung steht in <c>berichte/geschaeftszentrum-fenster-fable.md</c>; hier nur,
/// was der Zeichner braucht.</para>
///
/// <para><b>Klickweg:</b> Zeigerwahl <c>0x432569</c> (nur mit einem FAHRZEUG
/// des Betrachters auf einer der vier Platten) → Tafel <c>0x4379F0[16]</c> →
/// <c>0x437341</c> → Öffner <c>0x443CF0</c> (F <c>0x442CE0</c>) → Anleger
/// <c>0x45AFD0</c>: Art <b>33</b>, <b>360 × 260</b>. Der Takt-Arm der Art 17
/// <c>0x43E90C</c> schliesst es, sobald keine Einheit mehr auf einer Platte
/// steht.</para>
///
/// <para><b>Zeichner <c>0x47DF70</c></b> (F <c>0x47C860</c>, 1130/1130 Befehle
/// gleich), relativ zur Fensterecke:</para>
/// <code>
///   (10,  2)  "Geschäftszentrum"                                @0x47E059
///   (280,30)  Bildrahmen 3x3      (20,30) Listenrahmen 11x9       @0x47DFF2/0x47E016
///   (240,30)  Rollbalken 9 Kacheln, nur bei mehr als 10 Angeboten @0x47E212
///   (32, 50+15i) i &lt; 10: gewählt -> Balken (30,y) 195x14, 0x8C   @0x47E2A6
///             Rangzeichen + Name, "$Preis" rechtsbündig bis 222   @0x47E321/0x47E490
///   nur die gewählte Zeile: Bild (280,30), "E" (270,100),
///             Balken (280,101) 60x10 / (284,105) 52*hp/hpmax x 2,
///             ab (265,115) im 15er-Schritt: Waffe · Nachladen ·
///             [+0x10] · Antrieb · A/V [2x] · Geschw. · Sicht · [Reichw.]
///   (20,220)  "Kontostand: $n"                                   @0x47ED70
///   (160,218) Knopf "Bestellen", 4 Kacheln                       @0x47ED9F
/// </code>
///
/// <para><b>Treffertest <c>0x4608BD</c></b>: Titel −5, Kreuz −2, (240,30) 2 hoch,
/// (240,190) 3 runter, Bahn (240,50,20×140) −6, Liste (20,50,220×150) 1000+Zeile,
/// (160,218,80×20) 1 »Bestellen«. <b>Klickarm <c>0x44C626</c></b>: Klang 306
/// vorweg; Zeile = Auswahl, <b>Doppelklick = Bestellen</b>; »Bestellen« schickt
/// Befehl <b>530</b> (Regalplatz, Betrachter, Gebäudeplatz) OHNE jede Vorbedingung
/// — kein Absagefenster, kein Absageklang. Rechtsklick schliesst.</para>
///
/// <para>⭐ Die Zähler des Fenstersatzes (Anzahl +0x0C, Rollstand +0x0E, Auswahl
/// +0x10, Regalplatz +0x12, Pfeilflaggen +0xACAC/+0xACB0) zieht im Original DER
/// ZEICHNER nach — hier <see cref="Nachziehen"/>, gerufen genau dann, wenn das
/// Original zeichnet: beim Öffnen, nach einem Klick hinein, nach einem Kauf.</para>
///
/// <para>⚠ UNSERE Setzungen: Massstab 2; das leere Regal liest im Original
/// Stapelmüll als Regalplatz (§3.4 der Lesung) — hier sendet »Bestellen« dann
/// nichts; der gedrückte Knopf löst sich beim nächsten Klick (wie im Depot).</para>
/// </summary>
public sealed partial class MarktView : Control
{
    public const int WTiles = 18, HTiles = 13;
    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;
    private const int BildX = 280, BildY = 30, BildTiles = 3;
    private const int ListeX = 20, ListeY = 30, ListeW = 11, ListeH = 9;
    private const int ZeileX = 32, ZeileY0 = 50, ZeilenSchritt = 15, PreisEnde = 222;
    private const int BalkenX = 30, BalkenW = 195, BalkenH = 14;
    public const int RollX = 240, RollY = 30, RollTiles = 9;
    private const int EX = 270, EY = 100;
    private const int EBarX = 280, EBarY = 101, EBarW = 60, EBarH = 10;
    private const int WertX = 265, WertY0 = 115, WertSchritt = 15;
    private const int KontoX = 20, KontoY = 220;
    private const int KnopfX = 160, KnopfY = 218, KnopfTiles = 4;

    /// <summary>Zehn sichtbare Zeilen (0x47E250).</summary>
    public const int Zeilen = 10;

    public Action? OnClose;

    /// <summary>»Bestellen« — der Regalplatz (bei uns: die Angebotsnummer).
    /// Befehl 530 geht vom Wirt aus.</summary>
    public Action<int>? OnBestellen;

    /// <summary>Jeder Klick ins Fenster zeichnet neu — dafür holt der Wirt den
    /// frischen Stand und ruft <see cref="Zeige"/>.</summary>
    public Action? OnChanged;

    private BuildingWindow.Stand? _stand;

    // ---- der Fenstersatz W ---------------------------------------------------
    private int _roll;          // +0x0E
    private int _auswahl;       // +0x10, 1000-basiert, 0 = leer
    private int _regalplatz = -1;   // +0x12
    private bool _hoch, _runter;    // +0xACAC / +0xACB0
    private bool _knopfGedrueckt;   // +0xACA8
    private bool _zieht;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    // ---- für den Prüfstand ---------------------------------------------------
    public int Anzahl => _stand?.MarktZeilen.Count ?? 0;
    public int Rollstand => _roll;
    public int Auswahl => _auswahl;
    public int Regalplatz => _regalplatz;
    public bool HatRollbalken => Anzahl > Zeilen;
    public int Klicks { get; private set; }
    public int Bestellungen { get; private set; }
    public int LetzterTreffer { get; private set; } = -99;
    public int Wertezeilen { get; private set; }

    public MarktView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Ein frisches Fenster: der Anleger nullt den Satz (0x45B03F).</summary>
    public void Neu()
    {
        _roll = 0; _auswahl = 0; _regalplatz = -1;
        _hoch = _runter = false;
        _knopfGedrueckt = false;
    }

    public void Zeige(BuildingWindow.Stand s)
    {
        _stand = s;
        Nachziehen();
        QueueRedraw();
    }

    /// <summary>Die Rechnungen des Zeichners, Nr 5–11 der Koordinatentafel.</summary>
    private void Nachziehen()
    {
        int n = Anzahl;                                              // Nr 5
        if (n <= Zeilen) _roll = 0;                                  // Nr 6
        else _roll = Mathf.Clamp(_roll, 0, n - Zeilen);
        if (n > 0)                                                   // Nr 7
        {
            if (_auswahl < 1000) _auswahl = 1000;
            if (_auswahl > 999 + n) _auswahl = 999 + n;
        }
        else _auswahl = 0;
        if (_hoch)                                                   // Nr 8
        {
            if (_roll > 0) _roll--;
            _hoch = false;
            if (_auswahl - 1000 > _roll + 9) _auswahl--;
        }
        if (_runter)                                                 // Nr 9
        {
            if (n - Zeilen > _roll) _roll++;
            _runter = false;
            if (_auswahl - 1000 < _roll) _auswahl++;
        }
        _regalplatz = n > 0 ? _stand!.MarktZeilen[_auswahl - 1000].Nr : -1;   // Nr 11
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    private void Rechteck(int x, int y, int w, int h, Color c)
        => DrawRect(new Rect2(x * Scale, y * Scale, w * Scale, h * Scale), c, true);

    private static int Breite(Font f, string s)
        => (int)(f.GetStringSize(s, HorizontalAlignment.Left, -1, FontSize).X / Scale);

    public override void _Draw()
    {
        var s = _stand;
        var font = WindowChrome.LegacyFont;
        Wertezeilen = 0;
        if (s == null || WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);                         // Nr 1
        WindowChrome.PaintInnerFrame(this, BildX, BildY, BildTiles, BildTiles, Scale);  // Nr 2
        WindowChrome.PaintInnerFrame(this, ListeX, ListeY, ListeW, ListeH, Scale);      // Nr 3
        Text(font, TitleX, TitleY, "Geschäftszentrum", WindowChrome.TitleColour);       // Nr 4

        int n = s.MarktZeilen.Count;
        if (n > Zeilen)                                                          // Nr 10
            WindowChrome.PaintScrollbar(this, RollX, RollY, RollTiles, _roll, n - (Zeilen - 1), Scale);

        for (int i = 0; i < Zeilen && _roll + i < n; i++)                        // Nr 12
        {
            int k = _roll + i;
            var z = s.MarktZeilen[k];
            int y = ZeileY0 + ZeilenSchritt * i;
            bool gewaehlt = _auswahl - 1000 == k;
            if (gewaehlt)
            {
                Rechteck(BalkenX, y, BalkenW, BalkenH, WindowChrome.LineColour);    // 12a
                if (PortraitBank.Ready && z.Werte.ChassisPic > 0)                   // 12b
                    PortraitBank.DrawPictures(this,
                        new Rect2(BildX * Scale, BildY * Scale,
                                  BildTiles * WindowChrome.Cell * Scale,
                                  BildTiles * WindowChrome.Cell * Scale),
                        z.Werte.ChassisPic, z.Werte.TurretPic);
            }
            Text(font, ZeileX, y, UnitListView.RangText(font, z.Werte.Rang) + z.Werte.Name,
                 WindowChrome.TextColour);                                          // 12c
            string preis = "$" + z.Preis;                                           // 12d
            Text(font, PreisEnde - Breite(font, preis), y, preis, WindowChrome.TextColour);
            if (gewaehlt) Werteblock(font, z.Werte);                                // Nr 13
        }

        Text(font, KontoX, KontoY, "Kontostand: $" + s.Geld, WindowChrome.TextColour);  // Nr 14

        WindowChrome.PaintButton(this, KnopfX, KnopfY, KnopfTiles, Scale, _knopfGedrueckt);  // Nr 15
        const string wort = "Bestellen";
        float wpx = font.GetStringSize(wort, HorizontalAlignment.Left, -1, FontSize).X;
        int bx = KnopfX + (int)((KnopfTiles * WindowChrome.Cell * Scale - wpx) / 2 / Scale);
        Text(font, bx, KnopfY + (_knopfGedrueckt ? 4 : 3), wort, WindowChrome.TextColour);
    }

    /// <summary>Nr 13a–13k — der Werteblock des Depots, fünf Punkte höher und
    /// ohne die Zeile für +0x0E.</summary>
    private void Werteblock(Font font, BuildingWindow.DepotZeile u)
    {
        Text(font, EX, EY, "E", WindowChrome.TextColour);
        Rechteck(EBarX, EBarY, EBarW, EBarH, new Color(0, 0, 0));
        if (u.HpMax > 0)
            Rechteck(EBarX + 4, EBarY + 4, 52 * Mathf.Clamp(u.Hp, 0, u.HpMax) / u.HpMax, 2,
                     WindowChrome.LineColour);
        int wy = WertY0;
        void Wert(string t)
        {
            if (t.Length > 0) { Text(font, WertX, wy, t, WindowChrome.TextColour); Wertezeilen++; }
            wy += WertSchritt;
        }
        if (u.Waffe.Length > 0 || u.HatWaffe)
        {
            Wert(u.Waffe);                                                  // 13d
            Wert("Nachladen " + u.Nachladen);                               // 13e
        }
        if (u.Verbesserung.Length > 0) Wert(u.Verbesserung);                // 13f
        Wert(u.Antrieb);                                                    // 13g
        Wert("A/V " + (u.Zwilling ? "2x" : "") + u.Angriff + "/" + u.Verteidigung);   // 13h
        Wert("Geschw. " + u.Geschw);                                        // 13i
        Wert("Sicht " + u.Sicht);                                           // 13j
        if (u.Reichw != 0) Wert("Reichw. " + u.Reichw + "/" + u.MinReichw); // 13k
    }

    /// <summary>Der Treffertest <c>0x4608BD</c>.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (y < 20) return x < w - 20 ? -5 : -2;
        bool In(int fx, int fy, int fw, int fh) => x >= fx && x < fx + fw && y >= fy && y < fy + fh;
        if (In(RollX, RollY, 20, 20)) return 2;                          // @0x460982
        if (In(RollX, 190, 20, 20)) return 3;                            // @0x4609B6
        if (In(RollX, 50, 20, 140)) return -6;                           // @0x4609ED
        if (In(20, 50, 220, 150))                                        // @0x460A57
        {
            int zeile = ((int)y - 50) / ZeilenSchritt + _roll;
            return zeile < Anzahl ? 1000 + zeile : 0;
        }
        if (In(KnopfX, KnopfY, KnopfTiles * WindowChrome.Cell, 20)) return 1;   // @0x460A94
        return 0;
    }

    /// <summary>Wo ein Treffer auf dem SCHIRM liegt — für den Prüfstand.</summary>
    public Rect2 FeldAufDemSchirm(int treffer)
    {
        Rect2 lokal = treffer switch
        {
            1 => new Rect2(KnopfX, KnopfY, KnopfTiles * WindowChrome.Cell, 20),
            2 => new Rect2(RollX, RollY, 20, 20),
            3 => new Rect2(RollX, 190, 20, 20),
            _ => new Rect2(20, ZeileY0 + ZeilenSchritt * (treffer - 1000 - _roll), 220, ZeilenSchritt),
        };
        return new Rect2(GetGlobalRect().Position + lokal.Position * Scale, lokal.Size * Scale);
    }

    public override void _Input(InputEvent @event)
    {
        if (_knopfGedrueckt && @event is InputEventMouseButton { Pressed: true })
        {
            _knopfGedrueckt = false;
            QueueRedraw();
        }
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
        if (@event is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex == MouseButton.Right)
        {
            AcceptEvent();
            if (!mb.Pressed) return;
            WindowManager.Elementklang();                                  // 0x4142D7
            OnClose?.Invoke();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        AcceptEvent();
        if (!mb.Pressed) { _zieht = false; return; }
        Klick(Hit(mb.Position), mb.DoubleClick, mb.Position);
    }

    /// <summary>Der Klickarm <c>0x44C626</c> — öffentlich, damit der Prüfstand
    /// denselben Arm trifft wie die Maus.</summary>
    public void Klick(int t, bool doppel, Vector2 punkt)
    {
        LetzterTreffer = t;
        if (t == -5) { _zieht = true; return; }
        if (t == -1) return;
        Klicks++;
        WindowManager.Elementklang();                                      // 0x448603
        if (t == -2) { OnClose?.Invoke(); return; }

        if (t >= 1000)
        {
            _auswahl = t;                                                  // @0x44C647
            if (doppel)
            {
                // @0x44C66F: word[0x4FD654] := 1, 0x4485D0(0) — wie »Bestellen«,
                // mit zweitem Klickton. Die Auswahl zieht der Zeichner vorher nach.
                Nachziehen();
                Klick(1, false, punkt);
                return;
            }
        }
        else if (t == 1)
        {
            _knopfGedrueckt = true;                                        // +0xACA8 := 1
            if (_regalplatz >= 0) { Bestellungen++; OnBestellen?.Invoke(_regalplatz); }
        }
        else if (t == 2) _hoch = true;
        else if (t == 3) _runter = true;
        else if (t == -6 && HatRollbalken)
        {
            // 0x457140 wertet die Maus beim Zeichnen selbst aus.
            int neu = WindowChrome.ScrollHit(RollX, RollY, RollTiles, Anzahl - (Zeilen - 1), punkt / Scale);
            if (neu >= 0) _roll = neu;
        }
        if (OnChanged != null) OnChanged();                                // 0x487630
        else Nachziehen();
        QueueRedraw();
    }

    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) switch
        {
            1 => "Kaufauftrag absenden",                                   // Hilfe 0x57
            >= 1000 => "Auflistung der auf dem Markt zum Erwerb angebotenen Einheiten",   // 0x61
            _ => "",
        };
}
