namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»LOKATOR«</b> — Fensterart <b>24</b> des Originals, <b>mit dessen eigenen Kacheln</b>
/// (04.10.2026, bug-424; KayelGee: »der Lokator wirkt wie ein Prototyp, nicht im Stil der
/// anderen Fenster«). Gelesen in <c>berichte/infofenster-fable.md</c> §5 (C).
///
/// <para><b>Der Zeichner <c>0x47A740</c></b> (bis <c>ret</c> @<c>0x47AA1F</c>):</para>
/// <code>
///   0x455E50(0, 0, 14, 7, …, 1, 0)             Rahmen mit Titelleiste, 280×140 (Anleger 0x459F30)
///   0x456A50(20, 20, 12, 4)                    Innenrahmen 240×80
///   0x4BA5E0(10, 2, "Lokator", 0x96, 0xA9)     Titel, gold
///   i = 0..3, y = 30 + 15·i:  "F" + (i+5) + " " + Name[0x799FA8 + 21·i]
///     word[W+0x0C] − i == 1000 (Eingabemodus) → fill_rect(30, y, 220, 14, 0x8C) + Eingabe + "_"
///     0x4BA420(40, y, Puffer)
///   0x456670("Lokalisieren", 30, 105, 5) · 0x456670("Sichern", 150, 105, 5)
/// </code>
/// <para><b>Treffertest <c>0x45FC4B</c></b>: Kreuz (W−20,0,20,20) → −2 · Titel (0,0,W−20,20) → −5
/// (ziehen) · Zeile (30, 30+15i, 220, 15) → 1000+i (Hilfe 0x48) · (30,105,100,20) → 1 (0x49)
/// · (150,105,100,20) → 2 (0x4A). <b>Klick <c>0x44C0EB</c></b>: Zeile → <c>word[W+0x0C] :=
/// Element</c> (Eingabemodus); Knopf 1 nur mit gewählter Zeile → Lokalisieren (0x438AE0),
/// Fenster zu; Knopf 2 → Sichern (0x438BD0, leerer Name → »NONAME« 0x4FAB90), zu; ein Knopf
/// ohne gewählte Zeile tut nichts. Tasten nur bei <c>+0x0C ≠ 0</c>, Eingabetaste → 2.</para>
///
/// <para>⚠ <b>UNSERES</b>: die Titelfarbe ist eine (<see cref="WindowChrome.TitleColour"/>)
/// statt der zwei des Titelzeichners — wie bei allen Kachelfenstern; ESC schliesst (die
/// Fensterregel des Baums); der Knopf ohne Zeile ist bei <c>Sichern</c> derselbe Fall wie
/// bei <c>Lokalisieren</c> (der Klickblock prüft <c>+0x0C</c> beim Sichern NICHT
/// ausdrücklich — gesichert würde dann Zeile <c>Low(0)+0x18</c>, V: das wäre ein Fehlgriff
/// in eine fremde Tafel; wir tun nichts). Die Lage beim Öffnen: <c>0x442D40(100, 100)</c>
/// im Schirm des Originals, bei uns mal <see cref="Scale"/>.</para>
///
/// <para>Der alte Aufbau aus Godot-Bausteinen steht als <see cref="LocatorWindow"/> daneben und
/// kommt mit <c>--lokatorfenster-alt</c> zurück.</para>
/// </summary>
public sealed partial class LokatorChrome : Control
{
    /// <summary>14 × 7 Kacheln — die 280 × 140 des Anlegers <c>0x459F30</c>.</summary>
    public const int WTiles = 14, HTiles = 7, Scale = 2;

    public const int ZeileX = 40, ZeileY = 30, ZeileSchritt = 15;
    public const int FeldX = 30, FeldW = 220, FeldH = 14, TrefferH = 15;
    public const int KnopfY = 105, KnopfTiles = 5;
    public static readonly int[] KnopfX = { 30, 150 };
    private static readonly string[] KnopfText = { "Lokalisieren", "Sichern" };

    /// <summary><c>--lokatorfenster-alt</c> — der alte Aufbau (<see cref="LocatorWindow"/>).</summary>
    public static bool Alt;

    public static bool Usable => WindowChrome.Atlas != null && WindowChrome.LegacyFont != null;

    /// <summary>Die vier Zeilen: Name und ob der Punkt gesetzt ist.</summary>
    public System.Func<List<(string Name, bool Gesetzt)>>? Rows;
    public System.Action<int>? OnLocate;
    public System.Action<int, string>? OnSave;

    /// <summary><c>word[W+0x0C] − 1000</c>: die Zeile im Eingabemodus, −1 = keine.</summary>
    public int Zeile { get; private set; } = -1;
    public string Eingabe { get; private set; } = "";

    private int _held = -1;
    private bool _zieht;

    public LokatorChrome()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
    }

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    public override void _Draw()
    {
        var f = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || f == null) return;
        WindowChrome.Paint(this, WTiles, HTiles, Scale);
        WindowChrome.PaintInnerFrame(this, 20, 20, 12, 4, Scale);
        Text(f, 10, 2, "Lokator", WindowChrome.TitleColour);

        var r = Rows?.Invoke();
        for (int i = 0; i < 4; i++)
        {
            int y = ZeileY + ZeileSchritt * i;
            string name = r != null && i < r.Count && r[i].Gesetzt ? r[i].Name : "";
            string zeile = $"F{i + 5} ";
            if (i == Zeile)
            {
                DrawRect(new Rect2(FeldX * Scale, y * Scale, FeldW * Scale, FeldH * Scale),
                         WindowChrome.LineColour);
                zeile += Eingabe + "_";
            }
            else zeile += name;
            Text(f, ZeileX, y, zeile, WindowChrome.TextColour);
        }

        for (int i = 0; i < 2; i++)
        {
            bool g = _held == i;
            WindowChrome.PaintButton(this, KnopfX[i], KnopfY, KnopfTiles, Scale, g);
            float w = f.GetStringSize(KnopfText[i], HorizontalAlignment.Left, -1, FontSize).X;
            int tx = KnopfX[i] + (KnopfTiles * WindowChrome.Cell - (int)(w / Scale)) / 2;
            Text(f, tx, KnopfY + (g ? 4 : 3), KnopfText[i], WindowChrome.TextColour);
        }
    }

    /// <summary>Der Treffertest <c>0x45FC4B</c>, in Fensterpunkten (also mal <see cref="Scale"/>).</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell, h = HTiles * WindowChrome.Cell;
        if (x < 0 || y < 0 || x >= w || y >= h) return -1;
        if (x >= w - 20 && y < 20) return -2;
        if (y < 20) return -5;
        for (int i = 0; i < 4; i++)
        {
            int zy = ZeileY + ZeileSchritt * i;
            if (x >= FeldX && x < FeldX + FeldW && y >= zy && y < zy + TrefferH) return 1000 + i;
        }
        for (int i = 0; i < 2; i++)
            if (x >= KnopfX[i] && x < KnopfX[i] + KnopfTiles * WindowChrome.Cell
                && y >= KnopfY && y < KnopfY + 20) return 1 + i;
        return 0;
    }

    /// <summary>Die Hilfezeilen 0x48/0x49/0x4A (Tafel <c>0x4F0280</c>).</summary>
    public override string _GetTooltip(Vector2 pos)
        => Hit(pos) switch
        {
            >= 1000 => "Gesicherte Kartenansichten",
            1 => "Zur gesicherten Kartenansicht",
            2 => "Kartenansicht speichern",
            _ => "",
        };

    /// <summary>Der Klickblock <c>0x44C0EB</c>. Gibt zurück, was geschah — für den Prüfstand.</summary>
    public string Klick(int element)
    {
        if (element >= 1000)
        {
            ZeileWaehlen(element - 1000);
            return $"Zeile {Zeile}";
        }
        if (element == -2) { Schliessen(); return "zu"; }
        if (element is 1 or 2)
        {
            if (Zeile < 0) { QueueRedraw(); return "nichts (keine Zeile)"; }
            int i = Zeile;
            if (element == 1) OnLocate?.Invoke(i);
            else
            {
                string n = SkirmishSetup.FilterName(Eingabe);
                OnSave?.Invoke(i, n.Length > 0 ? n : "NONAME");
            }
            Schliessen();
            return element == 1 ? $"lokalisiert {i}" : $"gesichert {i}";
        }
        QueueRedraw();
        return "nichts";
    }

    private void ZeileWaehlen(int i)
    {
        Zeile = Mathf.Clamp(i, 0, 3);
        var r = Rows?.Invoke();
        // 0x4588E0(W, Name, 20): der Eingabepuffer übernimmt den gesicherten Namen.
        Eingabe = r != null && Zeile < r.Count && r[Zeile].Gesetzt ? r[Zeile].Name : "";
        if (Eingabe.Length > 20) Eingabe = Eingabe[..20];
        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible) return;
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
        if (k.Keycode == Key.Escape) { Schliessen(); AcceptEvent(); return; }
        // ⭐ Tasten nur im Eingabemodus (word[W+0x0C] ≠ 0, OFFENE_FRAGEN 14486).
        if (Zeile < 0) return;
        if (k.Keycode == Key.Backspace)
        {
            if (Eingabe.Length > 0) Eingabe = Eingabe[..^1];
            QueueRedraw(); AcceptEvent(); return;
        }
        if (k.Keycode is Key.Enter or Key.KpEnter) { Klick(2); AcceptEvent(); return; }
        long u = k.Unicode;
        if (u >= 32 && u < 127 && Eingabe.Length < 20)
        {
            Eingabe += (char)u;
            QueueRedraw(); AcceptEvent();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb) return;
        // Rechtsklick schliesst, wie bei den anderen Kachelfenstern.
        if (mb.ButtonIndex == MouseButton.Right && mb.Pressed) { Schliessen(); AcceptEvent(); return; }
        if (mb.ButtonIndex != MouseButton.Left) return;
        int t = Hit(mb.Position);
        if (mb.Pressed)
        {
            _held = t is 1 or 2 ? t - 1 : -1;
            if (t == -5) _zieht = true;
            QueueRedraw(); AcceptEvent(); return;
        }
        _held = -1;
        QueueRedraw();
        AcceptEvent();
        if (t is -2 or 1 or 2 || t >= 1000) Klick(t);
    }

    /// <summary>Aufmachen; <paramref name="zeile"/> ≥ 0 wählt die Zeile vor — das tut
    /// <c>Strg+F5..F8</c> (Öffner 0x442D40). Der Knopf am Bedienblock öffnet ohne Zeile.</summary>
    public void Open(int zeile)
    {
        if (zeile >= 0) ZeileWaehlen(zeile);
        else { Zeile = -1; Eingabe = ""; }
        if (WindowManager.Offen(WindowManager.ArtMerkpunkte) == null)
        {
            Position = new Vector2(100, 100) * Scale;
            WindowManager.Oeffnen(WindowManager.ArtMerkpunkte, this);
        }
        else
            WindowManager.NachVorn(WindowManager.Offen(WindowManager.ArtMerkpunkte));
        Visible = true;
        QueueRedraw();
    }

    public void Schliessen() => WindowManager.Schliessen(WindowManager.ArtMerkpunkte);
}
