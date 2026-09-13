namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»DEPOT«</b> — das Gebäudefenster der Gebäudeart 5, <b>Fensterart 23</b>
/// des Originals, mit dessen eigenen Kacheln (13.09.2026).
///
/// <para>Seine Meldung aus Kampagne 10: »auf der karte haben wir das erste mal
/// Depots. Da fehlt bei uns noch das Menu, bitte alles Original halten, keine
/// eigenbaude.« Die ganze Lesung steht in <c>berichte/depotfenster-fable.md</c>;
/// hier nur, was der Zeichner braucht.</para>
///
/// <para><b>Klickweg:</b> Tafel <c>0x4379F0</c>, Art 5 → Arm <c>0x4371A9</c> →
/// Öffner <c>0x4437C0</c> (F <c>0x4427B0</c>) → Anleger <c>0x459DF0</c>
/// (F <c>0x458A90</c>): Art <b>23</b>, <b>360 × 260</b> (@0x459E7F/0x459E88).</para>
///
/// <para><b>Zeichner <c>0x4790A0</c></b> (F <c>0x477990</c>, in beiden EXE
/// befehlsgleich), alles relativ zur Fensterecke:</para>
/// <code>
///   (10,  2)  "Depot " + Name                               @0x47918A
///   (20, 23)  "Energie :"   Balken (80,25) 260x10 schwarz,  @0x4791A2
///             Fuellung (84,29) 252*hp/hpmax x 2, Farbe 0x8C  @0x47921D
///   (20, 40)  Innenrahmen 11 x 8 Kacheln                    @0x4792AE
///   (30, 50+15i) je belegtem Platz, hoechstens 6:
///             markiert -> Balken (30,y) 195x14, 0x8C         @0x479332
///             Rangzeichen + Name + ("   " + 100*Hp/HpMax + "%"
///                                   | " (Ausgesandt)")       @0x479553
///   (20,205)  Knopf "Aussenden", 11 Kacheln                 @0x4795A7
///   nur bei GENAU EINER Markierung:
///   (280,40)  Innenrahmen 3x3, Bild der Einheit             @0x4795D8/0x47966B
///   (270,104) "E", Balken (280,105) 60x10 / (284,109) 52*Hp/HpMax x 2
///   (265,120) im 15er-Schritt: Waffe(n) · Nachladen · [Oberteil] ·
///             [Verbesserung] · Antrieb · A/V [2x] · Geschw. · Sicht · [Reichw.]
/// </code>
///
/// <para><b>Treffertest <c>0x45FAD0</c></b>: Titel −5 (ziehen), Kreuz −2,
/// Knopf (20,205,220×20) → 1, Zeile i (20,50+15i,220×15) → 2+i, nur solange
/// der Platz belegt ist. <b>Klickarm <c>0x44BFDF</c></b>: Klang 306 vorweg;
/// eine Zeile schaltet ihre Markierung UM (mehrere zugleich möglich, Einzel- und
/// Doppelklick sind eins); »Aussenden« schickt Befehl 504 für JEDE markierte
/// Zeile. <b>Rechtsklick</b> ins Fenster schliesst es (<c>0x4142D7</c>, mit
/// Klang 306). Tastatur: nichts (<c>0x487AD6</c>).</para>
///
/// <para>⭐ <b>Das Fenster zeigt den Stand vom Öffnen bzw. vom letzten Klick
/// hinein</b> — das Original zeichnet es nicht je Takt neu (§7 der Lesung).
/// Darum hält diese Klasse einen SCHNAPPSCHUSS, und <see cref="Zeige"/> wird nur
/// beim Öffnen und nach einem Klick gerufen.</para>
///
/// <para>⚠ <b>Was UNSERE Setzung ist, steht hier:</b> (1) der Massstab 2, wie
/// bei allen unseren Fenstern; (2) die Markierungen hängen an der EINHEIT und
/// nicht an der Platznummer — das Original rückt die Flaggen beim Ausdocken mit
/// den Plätzen nach (<c>0x43C6F0</c>), was auf dasselbe hinausläuft; (3) das
/// »2x« der Zwillingswaffe fragen wir über die Geschossart der Waffe ab
/// (Feld +0x15 der Tafel <c>0x4F98E8</c>), das Original über
/// <c>0x43B3F0</c> — dieselbe Tafelspalte, ein anderer Weg dorthin; (4) wie
/// lange der gedrückte Knopf gedrückt bleibt, ist nur angelesen
/// (<c>0x44FC90</c>): bis zum nächsten Klick irgendwo.</para>
/// </summary>
public sealed partial class DepotView : Control
{
    /// <summary>18 × 13 Kacheln — die 360 × 260 aus dem Anleger.</summary>
    public const int WTiles = 18, HTiles = 13;

    /// <summary>Doppelt, wie bei allen unseren Fenstern. UNSERE Setzung.</summary>
    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;
    private const int EnergieX = 20, EnergieY = 23;
    private const int BarX = 80, BarY = 25, BarW = 260, BarH = 10;
    private const int ListeX = 20, ListeY = 40, ListeW = 11, ListeH = 8;
    private const int ZeileX = 30, ZeileY0 = 50, ZeilenSchritt = 15;
    private const int BalkenW = 195, BalkenH = 14;
    private const int TrefferX = 20, TrefferW = 220, TrefferH = 15;
    private const int KnopfX = 20, KnopfY = 205, KnopfTiles = 11;
    private const int BildX = 280, BildY = 40, BildTiles = 3;
    private const int EX = 270, EY = 104;
    private const int EBarX = 280, EBarY = 105, EBarW = 60, EBarH = 10;
    private const int WertX = 265, WertY0 = 120, WertSchritt = 15;

    /// <summary>Sechs Plätze (sec25 +0x02…+0x0D).</summary>
    public const int Plaetze = 6;

    public Action? OnClose;

    /// <summary>»Aussenden« — die Griffe (Entitätsindizes) aller markierten
    /// Zeilen, in Platzreihenfolge. Danach ruft der Wirt <see cref="Zeige"/>
    /// mit dem neuen Stand.</summary>
    public Action<List<int>>? OnAussenden;

    /// <summary>Jeder Klick ins Fenster zeichnet neu — dafür holt der Wirt den
    /// frischen Stand.</summary>
    public Action? OnChanged;

    private BuildingWindow.Stand? _stand;
    private readonly HashSet<int> _marken = new();
    private bool _knopfGedrueckt;
    private bool _zieht;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    /// <summary>Für den Prüfstand: wieviele Zeilen zuletzt gezeigt, wieviele
    /// markiert, ob der Infoblock stand, und wieviele Wertezeilen er hatte.</summary>
    public int Zeilen => _stand?.DepotZeilen.Count ?? 0;
    public int Markiert => _marken.Count;
    public bool InfoblockGezeichnet { get; private set; }
    public int Wertezeilen { get; private set; }
    public int Klicks { get; private set; }
    public int Rechtsklicks { get; private set; }
    public int LetzterTreffer { get; private set; } = -99;

    public DepotView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Ein frisches Fenster: alle Markierungen 0 (der Anleger nullt den
    /// Satz @0x459E5F).</summary>
    public void Neu()
    {
        _marken.Clear();
        _knopfGedrueckt = false;
    }

    /// <summary>Den Schnappschuss setzen. Markierungen von Einheiten, die nicht
    /// mehr im Depot stehen, fallen weg — das ist das Nachrücken der Flaggen in
    /// <c>0x43C6F0</c>.</summary>
    public void Zeige(BuildingWindow.Stand s)
    {
        _stand = s;
        var da = new HashSet<int>();
        foreach (var z in s.DepotZeilen) da.Add(z.Griff);
        _marken.RemoveWhere(g => !da.Contains(g));
        QueueRedraw();
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    private void Rechteck(int x, int y, int w, int h, Color c)
        => DrawRect(new Rect2(x * Scale, y * Scale, w * Scale, h * Scale), c, true);

    /// <summary>Die Zeile, wie <c>0x479553</c> sie baut.</summary>
    public static string ZeilenText(Font? font, BuildingWindow.DepotZeile z)
    {
        string kopf = UnitListView.RangText(font, z.Rang) + z.Name;
        if (z.Ausgesandt) return kopf + " (Ausgesandt)";            // 0x502108
        int prozent = z.HpMax > 0 ? 100 * Mathf.Max(0, z.Hp) / z.HpMax : 0;
        return kopf + "   " + prozent + "%";                        // 0x5016BC / 0x5016B8
    }

    public override void _Draw()
    {
        var s = _stand;
        var font = WindowChrome.LegacyFont;
        InfoblockGezeichnet = false;
        Wertezeilen = 0;
        if (s == null || WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);
        Text(font, TitleX, TitleY, "Depot " + s.Name, WindowChrome.TitleColour);

        Text(font, EnergieX, EnergieY, "Energie :", WindowChrome.TextColour);
        Rechteck(BarX, BarY, BarW, BarH, new Color(0, 0, 0));
        if (s.HpMax > 0)
            Rechteck(BarX + 4, BarY + 4, 252 * Mathf.Clamp(s.Hp, 0, s.HpMax) / s.HpMax, 2,
                     WindowChrome.LineColour);

        WindowChrome.PaintInnerFrame(this, ListeX, ListeY, ListeW, ListeH, Scale);

        int zahl = Mathf.Min(s.DepotZeilen.Count, Plaetze);
        BuildingWindow.DepotZeile? einzige = null;
        int markiert = 0;
        for (int i = 0; i < zahl; i++)
        {
            var z = s.DepotZeilen[i];
            int y = ZeileY0 + ZeilenSchritt * i;
            if (_marken.Contains(z.Griff))
            {
                Rechteck(ZeileX, y, BalkenW, BalkenH, WindowChrome.LineColour);
                markiert++;
                einzige ??= z;
            }
            Text(font, ZeileX, y, ZeilenText(font, z), WindowChrome.TextColour);
        }

        WindowChrome.PaintButton(this, KnopfX, KnopfY, KnopfTiles, Scale, _knopfGedrueckt);
        const string wort = "Aussenden";
        float wpx = font.GetStringSize(wort, HorizontalAlignment.Left, -1, FontSize).X;
        int bx = KnopfX + (int)((KnopfTiles * WindowChrome.Cell * Scale - wpx) / 2 / Scale);
        Text(font, bx, KnopfY + (_knopfGedrueckt ? 4 : 3), wort, WindowChrome.TextColour);

        // ---- der Infoblock, nur bei GENAU EINER Markierung (@0x4795B3) -------
        if (markiert != 1 || einzige == null) return;
        InfoblockGezeichnet = true;
        var u = einzige;
        WindowChrome.PaintInnerFrame(this, BildX, BildY, BildTiles, BildTiles, Scale);
        if (PortraitBank.Ready && u.ChassisPic > 0)
            PortraitBank.DrawPictures(this,
                new Rect2(BildX * Scale, BildY * Scale,
                          BildTiles * WindowChrome.Cell * Scale,
                          BildTiles * WindowChrome.Cell * Scale),
                u.ChassisPic, u.TurretPic);

        Text(font, EX, EY, "E", WindowChrome.TextColour);
        Rechteck(EBarX, EBarY, EBarW, EBarH, new Color(0, 0, 0));
        if (u.HpMax > 0)
            Rechteck(EBarX + 4, EBarY + 4, 52 * Mathf.Clamp(u.Hp, 0, u.HpMax) / u.HpMax, 2,
                     WindowChrome.LineColour);

        int wy = WertY0;
        void Wert(string t)
        {
            // ⚠ Auch eine Zeile, deren Name bei uns fehlt, rückt weiter — das
            // Original schreibt sie immer und zählt dann y += 15.
            if (t.Length > 0) { Text(font, WertX, wy, t, WindowChrome.TextColour); Wertezeilen++; }
            wy += WertSchritt;
        }
        if (u.Waffe.Length > 0 || u.HatWaffe)
        {
            Wert(u.Waffe);                                            // @0x47988A
            Wert("Nachladen " + u.Nachladen);                         // @0x47996C
        }
        else if (u.Oberteil.Length > 0) Wert(u.Oberteil);             // @0x479A9C
        if (u.Verbesserung.Length > 0) Wert(u.Verbesserung);          // @0x479BFF
        Wert(u.Antrieb);                                              // @0x479D60
        Wert("A/V " + (u.Zwilling ? "2x" : "") + u.Angriff + "/" + u.Verteidigung);  // @0x479F59
        Wert("Geschw. " + u.Geschw);                                  // @0x47A024
        Wert("Sicht " + u.Sicht);                                     // @0x47A0F0
        if (u.Reichw != 0) Wert("Reichw. " + u.Reichw + "/" + u.MinReichw);   // @0x47A2A4
    }

    /// <summary>Der Treffertest <c>0x45FAD0</c>: −5 Titel, −2 Kreuz, −1
    /// ausserhalb, 1 Knopf, 2+i Zeile i, sonst 0.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (y < 20) return x < w - 20 ? -5 : -2;
        if (x >= KnopfX && x < KnopfX + KnopfTiles * WindowChrome.Cell
            && y >= KnopfY && y < KnopfY + 20) return 1;
        int belegt = Mathf.Min(_stand?.DepotZeilen.Count ?? 0, Plaetze);
        for (int i = 0; i < belegt; i++)
        {
            int oben = ZeileY0 + ZeilenSchritt * i;
            if (x >= TrefferX && x < TrefferX + TrefferW && y >= oben && y < oben + TrefferH)
                return 2 + i;
        }
        return 0;
    }

    /// <summary>Wo der Knopf bzw. eine Zeile auf dem SCHIRM liegt — für den
    /// Prüfstand, damit sein Klick dieselbe Rechnung trifft.</summary>
    public Rect2 FeldAufDemSchirm(int treffer)
    {
        Rect2 lokal = treffer == 1
            ? new Rect2(KnopfX, KnopfY, KnopfTiles * WindowChrome.Cell, 20)
            : new Rect2(TrefferX, ZeileY0 + ZeilenSchritt * (treffer - 2), TrefferW, TrefferH);
        return new Rect2(GetGlobalRect().Position + lokal.Position * Scale, lokal.Size * Scale);
    }

    public override void _Input(InputEvent @event)
    {
        // Der gedrückte Knopf löst sich beim nächsten Klick irgendwo (0x44FC90,
        // nur angelesen — siehe Kopf).
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

        // ⭐ Rechtsklick ins Fenster schliesst es, mit dem Klick (0x4142D7).
        if (mb.ButtonIndex == MouseButton.Right)
        {
            AcceptEvent();
            if (!mb.Pressed) return;
            Rechtsklicks++;
            WindowManager.Elementklang();
            OnClose?.Invoke();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;
        AcceptEvent();
        if (!mb.Pressed)
        {
            _zieht = false;
            return;
        }

        int t = Hit(mb.Position);
        LetzterTreffer = t;
        if (t == -5) { _zieht = true; return; }
        if (t == 0 || t == -1) return;

        Klicks++;
        WindowManager.Elementklang();                       // 0x448603, vor jeder Weiche
        if (t == -2) { OnClose?.Invoke(); return; }

        var s = _stand;
        if (s == null) return;
        if (t == 1)
        {
            _knopfGedrueckt = true;                          // dword[W+0xACA8] := 1
            var griffe = new List<int>();
            int zahl = Mathf.Min(s.DepotZeilen.Count, Plaetze);
            for (int i = 0; i < zahl; i++)
                if (_marken.Contains(s.DepotZeilen[i].Griff)) griffe.Add(s.DepotZeilen[i].Griff);
            // ⚠ Nichts markiert: nichts gesendet, nur der Klick (§3.2).
            if (griffe.Count > 0) OnAussenden?.Invoke(griffe);
        }
        else
        {
            int i = t - 2;
            if (i >= 0 && i < s.DepotZeilen.Count)
            {
                int g = s.DepotZeilen[i].Griff;
                if (!_marken.Remove(g)) _marken.Add(g);      // UMSCHALTEN @0x44C0CD
            }
        }
        OnChanged?.Invoke();                                  // 0x4412E0: neu zeichnen
        QueueRedraw();
    }

    /// <summary>Die Hilfezeilen des Originals (Tafel <c>0x4F0280</c>, Zeilen
    /// 0x46 und 0x47).</summary>
    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) switch
        {
            1 => "Aussenden der angewählten Einheiten",
            >= 2 => "Liste der gelagerten Einheiten",
            _ => "",
        };
}
