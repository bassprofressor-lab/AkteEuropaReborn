namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»MITNEHMBARE EINHEITEN«</b> — das Fenster zwischen zwei Kampagnenmissionen,
/// <b>Fensterart 38</b> des Originals, mit dessen eigenen Kacheln (13.09.2026).
///
/// <para>Seine Meldung am Ende von Kampagne 11: »dort kommt aber unser eigenbau
/// fenster! mach es bitte auch original!« Lesung:
/// <c>berichte/mitnahmefenster-fable.md</c>.</para>
///
/// <para><b>Öffner <c>0x4421A0</c></b> (F <c>0x441190</c>) im Zustand 100 nach dem
/// Abschlussfenster, nur wenn die LAUFENDE Mission Mitnahmeplätze setzt
/// (<c>byte[0x4FA27C]</c>); Anleger <c>0x45B4E0</c>: 600 × 420 an (10, 10).</para>
///
/// <para><b>Zeichner <c>0x482290</c></b> (F <c>0x480960</c>), relativ zur Ecke:</para>
/// <code>
///   (10,2)    "Sie können n von Ihren Einheiten zur nächsten Mission mitnehmen." 150/169
///   (20,30)   Rahmen 9x17 — LINKS die eigenen Fahrzeuge, 20 Zeilen ab (32,50)
///   (200,30)  Rollbalken 17 Kacheln ab 21
///   (390,30)  "Mitnehmbare Einheiten"; (390,50) Rahmen 9x9 — RECHTS die Tafel,
///             10 Zeilen ab (402,70); (570,50) Rollbalken 9 Kacheln ab 11
///   gewählt:  Balken 155x14 (30|400, y); Bild (240,30)|(320,30), "E" (230|310,100),
///             Werteblock ab (225|305,115)
///   (225,265) "Berkewitz Corp. bezahlt Ihnen $n"  (225,280) "für Ihre restlichen Einheiten."
///   (210,378) "Kontostand : $n" — OHNE die Auszahlung
///   (20,375)  "Mitnehmen >>" 9 Kacheln — nur wenn links etwas steht UND n > rechts
///   (390,235) "&lt;&lt; Zurücklassen" 9 Kacheln — nur wenn rechts etwas steht
///   (340,375) "Start der nächsten Mission" 12 Kacheln
/// </code>
///
/// <para><b>Treffertest <c>0x461827</c></b>, <b>Klickarm <c>0x44D617</c></b>:
/// Zeile = Auswahl, Doppelklick links = Mitnehmen, rechts = Zurücklassen; volle
/// Tafel = kein Knopf, still; nach JEDEM Klick die Auszahlung neu rechnen.
/// <b>Start, Kreuz, Rechtsklick und ESC schliessen gleich</b> (<c>0x4471A0</c> →
/// Zustand 200, Stapelkopie).</para>
///
/// <para>⚠ UNSERE Setzungen: Massstab 2; die Rollbahn wertet der Klick aus
/// (im Original liest <c>0x457140</c> die gehaltene Maus beim Zeichnen).</para>
/// </summary>
public sealed partial class MitnahmeView : Control
{
    public const int WTiles = 30, HTiles = 21;
    public const int Scale = 2;
    public const int LageX = 10, LageY = 10;
    public const int Tafelplaetze = 20;
    public const int ZeilenLinks = 20, ZeilenRechts = 10;

    // ---- was der Wirt liefert ------------------------------------------------
    public Func<ICollection<int>, List<int>>? Links;
    public Func<int, BuildingWindow.DepotZeile>? Zeile;
    public Func<ICollection<int>, int>? Auszahlung;
    /// <summary>Start (Knopf, Kreuz, Rechtsklick, ESC): die Griffe der Tafel in
    /// Platzreihenfolge und die zuletzt angezeigte Auszahlung.</summary>
    public Action<List<int>, int>? OnStart;

    private int _n, _konto, _auszahlung;
    private readonly int[] _tafel = new int[Tafelplaetze];
    private List<int> _links = new();
    private readonly List<int> _rechts = new();
    private readonly Dictionary<int, BuildingWindow.DepotZeile> _zeilen = new();

    private int _rollL, _selL, _griffL = -1;   // +0x0E +0x10 +0x12
    private int _rollR, _selR, _griffR = -1;   // +0x16 +0x18 +0x1A
    private bool _hochL, _runterL, _hochR, _runterR;
    private bool _nehmenGedrueckt, _lassenGedrueckt;
    private bool _zieht, _zu;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    // ---- für den Prüfstand ---------------------------------------------------
    public int Anzahl => _links.Count;
    public int AnzahlRechts => _rechts.Count;
    public int AuswahlLinks => _selL;
    public int AuswahlRechts => _selR;
    public int GriffLinks => _griffL;
    public int GriffRechts => _griffR;
    public int AuszahlungAnzeige => _auszahlung;
    public int N => _n;
    public int LetzterTreffer { get; private set; } = -99;
    public bool MitnehmenDa => _links.Count != 0 && _n > _rechts.Count;
    public bool ZuruecklassenDa => _rechts.Count != 0;
    public IReadOnlyList<int> Rechts => _rechts;
    public IReadOnlyList<int> LinksListe => _links;

    public MitnahmeView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale, HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        Position = new Vector2(LageX * Scale, LageY * Scale);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Öffner <c>0x4421A0</c> + Anleger: Tafel leer, Auszahlung für alles.</summary>
    public void Oeffnen(int n, int konto)
    {
        _n = n; _konto = konto;
        Array.Fill(_tafel, -1);
        _rollL = _selL = _rollR = _selR = 0;
        _griffL = _griffR = -1;
        _hochL = _runterL = _hochR = _runterR = false;
        _nehmenGedrueckt = _lassenGedrueckt = false;
        _zu = false;
        Position = new Vector2(LageX * Scale, LageY * Scale);
        _auszahlung = Auszahlung?.Invoke(TafelMenge()) ?? 0;          // 0x4421FE
        Nachziehen();
        QueueRedraw();
    }

    private HashSet<int> TafelMenge()
    {
        var m = new HashSet<int>();
        foreach (int g in _tafel) if (g >= 0) m.Add(g);
        return m;
    }

    private BuildingWindow.DepotZeile? Daten(int griff)
    {
        if (_zeilen.TryGetValue(griff, out var z)) return z;
        if (Zeile == null) return null;
        z = Zeile(griff);
        _zeilen[griff] = z;
        return z;
    }

    /// <summary>Die Rechnungen des Zeichners, Nr 6–17 der Koordinatentafel.</summary>
    private void Nachziehen()
    {
        _zeilen.Clear();
        _links = Links?.Invoke(TafelMenge()) ?? new List<int>();       // Nr 6
        int n = _links.Count;
        _rollL = n <= ZeilenLinks ? 0 : Mathf.Clamp(_rollL, 0, n - ZeilenLinks);  // Nr 7
        if (n > 0) _selL = Mathf.Clamp(_selL < 1000 ? 1000 : _selL, 1000, 999 + n);   // Nr 8
        else _selL = 0;
        if (_hochL)                                                     // Nr 9
        {
            if (_rollL > 0) _rollL--;
            _hochL = false;
            if (n > 0 && _selL - 1000 > _rollL + ZeilenLinks - 1) _selL = 1000 + _rollL + ZeilenLinks - 1;
            if (n > 0 && _selL > 999 + n) _selL = 999 + n;
        }
        if (_runterL)                                                   // Nr 10
        {
            if (n - ZeilenLinks > _rollL) _rollL++;
            _runterL = false;
            if (n > 0 && _selL - 1000 < _rollL) _selL = 1000 + _rollL;
        }
        _griffL = n > 0 ? _links[_selL - 1000] : -1;                    // Nr 12

        _rechts.Clear();                                                // Nr 13
        foreach (int g in _tafel) if (g >= 0) _rechts.Add(g);
        int m = _rechts.Count;
        _rollR = m <= ZeilenRechts ? 0 : Mathf.Clamp(_rollR, 0, m - ZeilenRechts); // Nr 14
        if (m > 0) _selR = Mathf.Clamp(_selR < 2000 ? 2000 : _selR, 2000, 1999 + m);
        else _selR = 0;
        if (_hochR)                                                     // Nr 15
        {
            if (_rollR > 0) _rollR--;
            _hochR = false;
            if (m > 0 && _selR - 2000 > _rollR + ZeilenRechts - 1) _selR = 2000 + _rollR + ZeilenRechts - 1;
        }
        if (_runterR)
        {
            if (m - ZeilenRechts > _rollR) _rollR++;
            _runterR = false;
            if (m > 0 && _selR - 2000 < _rollR) _selR = 2000 + _rollR;
        }
        _griffR = m > 0 ? _rechts[_selR - 2000] : -1;                   // Nr 17
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    private void Rechteck(int x, int y, int w, int h, Color c)
        => DrawRect(new Rect2(x * Scale, y * Scale, w * Scale, h * Scale), c, true);

    private void Knopf(Font font, int x, int y, int kacheln, string wort, bool gedrueckt)
    {
        WindowChrome.PaintButton(this, x, y, kacheln, Scale, gedrueckt);
        float wpx = font.GetStringSize(wort, HorizontalAlignment.Left, -1, FontSize).X;
        int bx = x + (int)((kacheln * WindowChrome.Cell * Scale - wpx) / 2 / Scale);
        Text(font, bx, y + (gedrueckt ? 4 : 3), wort, WindowChrome.TextColour);
    }

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);                                   // Nr 1
        WindowChrome.PaintInnerFrame(this, 20, 30, 9, 17, Scale);                          // Nr 2
        WindowChrome.PaintInnerFrame(this, 390, 50, 9, 9, Scale);                          // Nr 3
        Text(font, 390, 30, "Mitnehmbare Einheiten", WindowChrome.TextColour);             // Nr 4
        Text(font, 10, 2, "Sie können " + _n + " von Ihren Einheiten zur nächsten Mission mitnehmen.",
             WindowChrome.TitleColour);                                                    // Nr 5

        int n = _links.Count, m = _rechts.Count;
        if (n > ZeilenLinks)                                                               // Nr 11
            WindowChrome.PaintScrollbar(this, 200, 30, 17, _rollL, n - (ZeilenLinks - 1), Scale);
        if (m > ZeilenRechts)                                                              // Nr 16
            WindowChrome.PaintScrollbar(this, 570, 50, 9, _rollR, m - (ZeilenRechts - 1), Scale);

        for (int i = 0; i < ZeilenLinks && _rollL + i < n; i++)                           // Nr 18
        {
            int k = _rollL + i, y = 50 + 15 * i;
            var z = Daten(_links[k]);
            if (z == null) continue;
            bool gew = _selL - 1000 == k;
            if (gew) Rechteck(30, y, 155, 14, WindowChrome.LineColour);
            Text(font, 32, y, UnitListView.RangText(font, z.Rang) + z.Name, WindowChrome.TextColour);
            if (gew) Block(font, z, 240, 230, 225);
        }
        for (int i = 0; i < ZeilenRechts && _rollR + i < m; i++)                          // Nr 19
        {
            int k = _rollR + i, y = 70 + 15 * i;
            var z = Daten(_rechts[k]);
            if (z == null) continue;
            bool gew = _selR - 2000 == k;
            if (gew) Rechteck(400, y, 155, 14, WindowChrome.LineColour);
            Text(font, 402, y, UnitListView.RangText(font, z.Rang) + z.Name, WindowChrome.TextColour);
            if (gew) Block(font, z, 320, 310, 305);
        }

        Text(font, 210, 378, "Kontostand : $" + _konto, WindowChrome.TextColour);          // Nr 20
        Text(font, 225, 265, "Berkewitz Corp. bezahlt Ihnen $" + _auszahlung, WindowChrome.TextColour);  // Nr 21
        Text(font, 225, 280, "für Ihre restlichen Einheiten.", WindowChrome.TextColour);   // Nr 22
        if (MitnehmenDa) Knopf(font, 20, 375, 9, "Mitnehmen >>", _nehmenGedrueckt);        // Nr 23
        if (ZuruecklassenDa) Knopf(font, 390, 235, 9, "<< Zurücklassen", _lassenGedrueckt); // Nr 24
        Knopf(font, 340, 375, 12, "Start der nächsten Mission", false);                   // Nr 25
    }

    /// <summary>Bildrahmen, Bild, »E«-Balken und Werteblock (18c–18h / 19c).</summary>
    private void Block(Font font, BuildingWindow.DepotZeile u, int bildX, int eX, int wertX)
    {
        WindowChrome.PaintInnerFrame(this, bildX, 30, 3, 3, Scale);
        if (PortraitBank.Ready && u.ChassisPic > 0)
            PortraitBank.DrawPictures(this,
                new Rect2(bildX * Scale, 30 * Scale, 3 * WindowChrome.Cell * Scale, 3 * WindowChrome.Cell * Scale),
                u.ChassisPic, u.TurretPic);
        Text(font, eX, 100, "E", WindowChrome.TextColour);
        Rechteck(bildX, 101, 60, 10, new Color(0, 0, 0));
        if (u.HpMax > 0)
            Rechteck(bildX + 4, 105, 52 * Mathf.Clamp(u.Hp, 0, u.HpMax) / u.HpMax, 2, WindowChrome.LineColour);
        int wy = 115;
        void Wert(string t)
        {
            if (t.Length > 0) Text(font, wertX, wy, t, WindowChrome.TextColour);
            wy += 15;
        }
        if (u.Waffe.Length > 0 || u.HatWaffe) { Wert(u.Waffe); Wert("Nachladen " + u.Nachladen); }
        if (u.Verbesserung.Length > 0) Wert(u.Verbesserung);
        Wert(u.Antrieb);
        Wert("A/V " + (u.Zwilling ? "2x" : "") + u.Angriff + "/" + u.Verteidigung);
        Wert("Geschw. " + u.Geschw);
        Wert("Sicht " + u.Sicht);
        if (u.Reichw != 0) Wert("Reichw. " + u.Reichw + "/" + u.MinReichw);
    }

    /// <summary>Der Treffertest <c>0x461827</c>.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (y < 20) return x < w - 20 ? -5 : -2;
        bool In(int fx, int fy, int fw, int fh) => x >= fx && x < fx + fw && y >= fy && y < fy + fh;
        if (In(200, 30, 20, 20)) return 3;
        if (In(200, 350, 20, 20)) return 4;
        if (In(570, 50, 20, 20)) return 5;
        if (In(570, 210, 20, 20)) return 6;
        if (In(20, 50, 180, 300)) { int z = ((int)y - 50) / 15 + _rollL; if (z < _links.Count) return 1000 + z; }
        if (In(400, 70, 180, 150)) { int z = ((int)y - 70) / 15 + _rollR; if (z < _rechts.Count) return 2000 + z; }
        if (In(20, 375, 180, 20) && MitnehmenDa) return 1;
        if (In(390, 235, 180, 20) && ZuruecklassenDa) return 2;
        if (In(340, 375, 240, 20)) return 7;
        return 0;
    }

    public Rect2 FeldAufDemSchirm(int treffer)
    {
        Rect2 lokal = treffer switch
        {
            1 => new Rect2(20, 375, 180, 20),
            2 => new Rect2(390, 235, 180, 20),
            7 => new Rect2(340, 375, 240, 20),
            >= 2000 => new Rect2(400, 70 + 15 * (treffer - 2000 - _rollR), 180, 15),
            _ => new Rect2(20, 50 + 15 * (treffer - 1000 - _rollL), 180, 15),
        };
        return new Rect2(GetGlobalRect().Position + lokal.Position * Scale, lokal.Size * Scale);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } && (_nehmenGedrueckt || _lassenGedrueckt))
        { _nehmenGedrueckt = _lassenGedrueckt = false; QueueRedraw(); }
        if (!_zieht) return;
        if (@event is InputEventMouseMotion mm)
        {
            var vp = GetViewportRect().Size;
            Position = new Vector2(Mathf.Clamp(Position.X + mm.Relative.X, 0, Mathf.Max(0, vp.X - Size.X)),
                                   Mathf.Clamp(Position.Y + mm.Relative.Y, 0, Mathf.Max(0, vp.Y - Size.Y)));
            AcceptEvent();
        }
        else if (@event is InputEventMouseButton up && up.ButtonIndex == MouseButton.Left && !up.Pressed)
        { _zieht = false; AcceptEvent(); }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex == MouseButton.Right)
        {
            AcceptEvent();
            if (!mb.Pressed) return;
            WindowManager.Elementklang();                      // 0x4142D7 -> 0x4471A0
            Starten();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        AcceptEvent();
        if (!mb.Pressed) { _zieht = false; return; }
        Klick(Hit(mb.Position), mb.DoubleClick, mb.Position);
    }

    /// <summary>ESC — <c>0x44FE10(0)</c> schliesst auch dieses Fenster = Start.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Starten();
        }
    }

    /// <summary>Der Klickarm <c>0x44D617</c>.</summary>
    public void Klick(int t, bool doppel, Vector2 punkt)
    {
        LetzterTreffer = t;
        if (t == -5) { _zieht = true; return; }
        if (t == -1) return;
        WindowManager.Elementklang();                          // 0x448603
        if (t == -2) { Starten(); return; }

        if (t >= 2000)
        {
            _selR = t;                                         // @0x44D677
            if (doppel) { Nachziehen(); Klick(2, false, punkt); return; }
        }
        else if (t >= 1000)
        {
            _selL = t;                                         // @0x44D629
            if (doppel) { Nachziehen(); Klick(1, false, punkt); return; }
        }
        else if (t == 1)                                       // @0x44D6D3
        {
            if (_n > 0 && _griffL >= 0)
            {
                for (int j = 0; j < _n && j < Tafelplaetze; j++)
                    if (_tafel[j] < 0) { _tafel[j] = _griffL; break; }   // keiner frei -> still
            }
            _nehmenGedrueckt = true;
        }
        else if (t == 2)                                       // @0x44D74F
        {
            for (int j = 0; j < Tafelplaetze; j++) if (_tafel[j] == _griffR && _griffR >= 0) _tafel[j] = -1;
            _lassenGedrueckt = true;
        }
        else if (t == 3) _hochL = true;
        else if (t == 4) _runterL = true;
        else if (t == 5) _hochR = true;
        else if (t == 6) _runterR = true;
        else if (t == 7) { Starten(); return; }                // @0x44D7D9
        else if (t == 0)
        {
            // 0x457140 liest beim Zeichnen die gehaltene Maus auf der Bahn.
            var q = punkt / Scale;
            if (_links.Count > ZeilenLinks)
            {
                int neu = WindowChrome.ScrollHit(200, 30, 17, _links.Count - (ZeilenLinks - 1), q);
                if (neu >= 0) _rollL = neu;
            }
            if (_rechts.Count > ZeilenRechts)
            {
                int neu = WindowChrome.ScrollHit(570, 50, 9, _rechts.Count - (ZeilenRechts - 1), q);
                if (neu >= 0) _rollR = neu;
            }
        }
        Nachziehen();
        _auszahlung = Auszahlung?.Invoke(TafelMenge()) ?? 0;   // @0x44D7E6
        Nachziehen();
        QueueRedraw();
    }

    /// <summary>Schliessen = Start (0x4471A0 @0x4472D3: Zustand 200, Stapelkopie).</summary>
    private void Starten()
    {
        if (_zu) return;
        _zu = true;
        var mit = new List<int>();
        foreach (int g in _tafel) if (g >= 0) mit.Add(g);
        OnStart?.Invoke(mit, Auszahlung?.Invoke(TafelMenge()) ?? _auszahlung);
    }

    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) switch
        {
            1 => "Diese Einheit hinüber zur nächsten Mission nehmen",
            2 => "Diese Einheit doch nicht zur nächsten Mission nehmen",
            7 => "Weiter zur nächsten Mission",
            _ => "",
        };
}
