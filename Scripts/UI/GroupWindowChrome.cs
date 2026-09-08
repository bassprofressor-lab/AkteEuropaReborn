namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»GRUPPIEREN«</b> — Fensterart <b>25</b> des Originals (C <c>0x47AAE0</c>,
/// 740 B, <b>280×220</b>), <b>mit dessen eigenen Kacheln</b>.
///
/// <para>⚠⚠ 08.09.2026, seine Meldung: »bei gruppe taucht immer noch unser
/// eigener fensteraufbau auf«. Er hat recht — und es ist dieselbe Lehre wie
/// beim Kaufmenü (25.08.) und beim Routenfenster (heute früh): <b>ein Fenster
/// wird mit den Kacheln aus WINDOWS.CWW gebaut</b>, nicht aus
/// Godot-Bausteinen. Der alte Aufbau steht als <see cref="GroupWindow"/>
/// daneben und kommt mit <c>--gruppenfenster-alt</c> zurück.</para>
///
/// <para><b>Was hier GELESEN ist:</b> die Fensterart (25), die Grösse
/// (280×220), die Wörter (»Gruppieren«, »Gruppe speichern«), die <b>Zahl
/// zehn</b>, der Namenszwang (»NONAME«) und die Ausschliesslichkeit einer
/// Einheit — alles im Kopf von <see cref="GroupWindow"/> belegt.</para>
///
/// <para>⚠ <b>Was UNSERES ist:</b> die Punkte innerhalb des Fensters. Für die
/// Art 25 sind sie nicht gelesen; genommen ist die Anordnung der <b>Art 24</b>
/// (Lokator), die gelesen IST und dieselbe Bauform hat — Titel bei (10,2),
/// Zeilen ab (40, 30) im 15er-Schritt, Knöpfe 100×20 unten. Dazu der zweite
/// Knopf »Abrufen«: das Original ruft eine Gruppe über <c>Strg+Zahl</c> ab, und
/// das gibt es bei uns auch — der Knopf ist eine Zugabe, keine Lesung.</para>
///
/// <para>⭐ Der <b>Name wird in der Zeile selbst</b> geschrieben, mit dem
/// Unterstrich als Schreibmarke — genau so zeichnet es der Lokator
/// (<c>"F&lt;n&gt; " + Name + "_"</c>), und die Fensterarten 24 und 25 sind die
/// zwei mit Eingabefeld (Tastentafel <c>0x487A10</c>).</para>
/// </summary>
public sealed partial class GroupWindowChrome : Control
{
    /// <summary>14 x 11 Kacheln — die 280 x 220 des Originals.</summary>
    public const int WTiles = 14, HTiles = 11, Scale = 2;

    private const int ZeileX = 40, ZeileY = 30, ZeileSchritt = 15;
    private const int KnopfY = 190, KnopfTiles = 5;      // 100 px, wie beim Lokator
    private static readonly int[] KnopfX = { 30, 150 };
    private static readonly string[] KnopfText = { "Gruppe speichern", "Abrufen" };

    public System.Func<List<(string Name, int Zahl)>>? Rows;
    public System.Action<int, string>? OnStore;
    public System.Action<int>? OnRecall;
    public System.Action? OnClose;

    private int _zeile;
    private string _name = "";
    private int _held = -1;
    private bool _zieht;

    public static bool Usable => WindowChrome.Atlas != null;

    public GroupWindowChrome()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
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

        Text(f, 10, 2, "Gruppieren", WindowChrome.TitleColour);

        var r = Rows?.Invoke();
        for (int i = 0; i < 10; i++)
        {
            // Die Ziffer davor ist die TASTE — Gruppe 10 liegt auf der Null.
            int taste = i + 1 == 10 ? 0 : i + 1;
            string name = r != null && i < r.Count ? r[i].Name : "";
            int zahl = r != null && i < r.Count ? r[i].Zahl : 0;
            string zeile = i == _zeile
                         ? $"{taste} {_name}_"
                         : $"{taste} {(name.Length > 0 ? name : "-")}"
                           + (zahl > 0 ? $"  ({zahl})" : "");
            Text(f, ZeileX, ZeileY + ZeileSchritt * i, zeile,
                 i == _zeile ? WindowChrome.TitleColour : WindowChrome.TextColour);
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

    /// <summary>Schliesskreuz −2 · Zeile 1..10 · Knopf 11/12 · sonst 0
    /// (ziehen).</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;
        for (int i = 0; i < 2; i++)
            if (x >= KnopfX[i] && x < KnopfX[i] + KnopfTiles * WindowChrome.Cell
                && y >= KnopfY && y < KnopfY + 20) return 11 + i;
        if (x >= 20 && x < w - 20 && y >= ZeileY && y < ZeileY + ZeileSchritt * 10)
        {
            int i = (int)((y - ZeileY) / ZeileSchritt);
            if (i >= 0 && i < 10) return i + 1;
        }
        return 0;
    }

    public override void _Input(InputEvent @event)
    {
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
        // ⭐ Das Eingabefeld: die Fensterarten 24 und 25 sind die zwei mit
        // Namenszeile (Tastentafel 0x487A10). Geschrieben wird IN die Zeile.
        if (!Visible || @event is not InputEventKey k || !k.Pressed) return;
        if (k.Keycode == Key.Backspace)
        {
            if (_name.Length > 0) _name = _name[..^1];
            QueueRedraw(); AcceptEvent(); return;
        }
        if (k.Keycode is Key.Enter or Key.KpEnter) { Speichern(); AcceptEvent(); return; }
        if (k.Keycode == Key.Escape) { OnClose?.Invoke(); AcceptEvent(); return; }
        long u = k.Unicode;
        if (u >= 32 && u < 127 && _name.Length < 20)
        {
            _name += (char)u;
            QueueRedraw(); AcceptEvent();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left)
            return;
        if (mb.Pressed)
        {
            int t = Hit(mb.Position);
            _held = t >= 11 ? t - 11 : -1;
            if (t == 0) { _zieht = true; AcceptEvent(); return; }
            QueueRedraw(); AcceptEvent(); return;
        }
        if (_zieht) { _zieht = false; AcceptEvent(); return; }
        int hit = Hit(mb.Position);
        _held = -1;
        QueueRedraw();
        if (hit == 0) return;
        AcceptEvent();
        if (hit == -2) { OnClose?.Invoke(); return; }
        if (hit >= 1 && hit <= 10) { ZeileWaehlen(hit - 1); return; }
        if (hit == 11) Speichern();
        else if (hit == 12) { OnRecall?.Invoke(_zeile + 1); OnClose?.Invoke(); }
    }

    private void Speichern()
    {
        // ⭐ Der Namensfilter des Originals (BN.12): die Spielschrift hat fuer
        // alles ausserhalb 0x20..0x7A und fuer [ ] ^ keine Kachel.
        OnStore?.Invoke(_zeile + 1, SkirmishSetup.FilterName(_name));
        OnClose?.Invoke();
    }

    private void ZeileWaehlen(int i)
    {
        _zeile = i;
        var r = Rows?.Invoke();
        _name = r != null && i < r.Count ? r[i].Name : "";
        QueueRedraw();
    }

    /// <summary>Aufmachen mit der Zeile vorgewählt und, wenn die Gruppe noch
    /// keinen Namen hat, auf <b>»Group N«</b> getauft — genau das tut
    /// <c>Strg+Zahl</c> im Original (<c>0x442C70</c>).</summary>
    public void Open(int gruppe)
    {
        ZeileWaehlen(Mathf.Clamp(gruppe - 1, 0, 9));
        if (_name.Length == 0) _name = $"Group {gruppe}";
        Visible = true;
        var vp = GetViewportRect().Size;
        Position = ((vp - Size) * 0.5f).Round();
        QueueRedraw();
    }

    public void Refresh() => QueueRedraw();
}
