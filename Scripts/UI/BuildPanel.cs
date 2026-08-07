namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The production list, drawn inside the recessed display box of the original
/// PANEL.DTA frame: what the selected factory, dock or airfield can build, what
/// it costs, and whether it can be paid for. Click a line to build it.
///
/// <para><b>What the original does, read off its building panel
/// @0x467c60.</b> Selecting a building there opens a screen of its own — not
/// this side panel — with four tabs in a row at y=55: <b>Depot</b> (x 20),
/// <b>Produktion</b> (100), <b>Forschung</b> (180), <b>Reparatur</b> (260),
/// drawn through 0x401820. Above them it prints <c>"Energie :"</c> at (20, 23)
/// and one of the four <c>"Status : …"</c> lines at (20, 40). The tab's body is
/// a list at <b>x = 32, y = 90 + 15·row</b> (@0x46829F: <c>lea edx,[ebx+ebx*2+0x12]</c>
/// then <c>lea eax,[edx+edx*4]</c>, so 15·row + 90), and along the bottom sit
/// <b>Aussenden</b> (20, 230), <b>Transportieren</b> (20, 255) and
/// <b>Recycle</b> (140, 255).</para>
///
/// <para>The Depot tab's list is the building's own units: <b>six u16 slots at
/// 0x878e5c, stride 16</b> per building (index from the record's +0x19), 0xFFFF
/// for empty, and the count is capped at 6 (@0x467FBF <c>cmp al, 6</c>). An
/// entry whose order byte (+0x14) is 0x33 gets <c>" (ausgesandt)"</c> appended
/// (@0x4681B7).</para>
///
/// <para><b>⚠ So this panel is OURS in its placement, and knowingly.</b> The
/// remake has no building screen; it has the side panel, and the side panel's
/// display box is where the player already looks. What is taken from the
/// original is its <b>word</b> for the thing — "Produktion" (0x501934) — and
/// its <b>row step of 15</b>. What is ours is that the list lives in the
/// 153×94 box instead of a screen of its own, that a click builds instead of
/// merely selecting, and the affordability marking, which the original solves
/// somewhere this has not read. A later session that builds the real building
/// screen has the coordinates above to do it with.</para>
/// </summary>
public partial class BuildPanel : Control
{
    /// <summary>One buildable thing.</summary>
    public readonly struct Row
    {
        public Row(string name, string cost, bool affordable, bool current)
        { Name = name; Cost = cost; Affordable = affordable; Current = current; }
        public string Name { get; }
        /// <summary>What it costs, already in the units that building pays in —
        /// parts for a factory, money for the airfield and the dock.</summary>
        public string Cost { get; }
        public bool Affordable { get; }
        /// <summary>What the factory would build next — the same pick the N key
        /// steps through, so the two ways of choosing agree.</summary>
        public bool Current { get; }
    }

    private Func<List<Row>>? _rows;
    private Func<string>? _title;
    private Action<int>? _pick;
    private List<Row> _shown = new();

    /// <summary>The original's own row step, scaled with the panel.</summary>
    public const int RowStep = 15;

    public void Setup(Func<List<Row>> rows, Func<string> title, Action<int> pick)
    {
        _rows = rows;
        _title = title;
        _pick = pick;
        QueueRedraw();
    }

    /// <summary>The game's own typeface, and the height of one row. The step is
    /// the original's 15 times whatever the panel is scaled by, so the list
    /// keeps the spacing the building screen has.</summary>
    public void SetFont(Font font, int size, float scale)
    {
        _font = font;
        _fontSize = size;
        _step = Mathf.Max(8f, RowStep * scale);
        QueueRedraw();
    }

    private Font? _font;
    private int _fontSize = 13;
    private int _hover = -1;

    private float _step = RowStep;
    private float Step => _step;

    /// <summary>How many rows fit below the title line.</summary>
    public int Capacity => Mathf.Max(0, (int)((Size.Y - Step) / Step));

    public override void _Draw()
    {
        if (_rows == null || _font == null) return;
        _shown = _rows();
        float step = Step;

        string head = _title?.Invoke() ?? "PRODUKTION";
        DrawString(_font, new Vector2(2, step - 4), head,
                   HorizontalAlignment.Left, -1, _fontSize, HeadColour);

        int n = Mathf.Min(_shown.Count, Capacity);
        for (int i = 0; i < n; i++)
        {
            var r = _shown[i];
            float y = step * (i + 2) - 4;
            if (r.Current)
                DrawRect(new Rect2(0, y - step + 4, Size.X, step), CurrentBg);
            if (i == _hover)
                DrawRect(new Rect2(0, y - step + 4, Size.X, step), HoverBg);

            var c = !r.Affordable ? TooDear : r.Current ? CurrentFg : Normal;
            DrawString(_font, new Vector2(3, y), r.Name.ToUpper(),
                       HorizontalAlignment.Left, Size.X - 46, _fontSize, c);
            DrawString(_font, new Vector2(Size.X - 46, y), r.Cost,
                       HorizontalAlignment.Right, 44, _fontSize, c);
        }
        if (_shown.Count > n)
            DrawString(_font, new Vector2(3, Size.Y - 2),
                       $"+{_shown.Count - n}", HorizontalAlignment.Left, -1, _fontSize, Normal);
    }

    private static readonly Color HeadColour = new(0.85f, 0.88f, 0.6f);
    private static readonly Color Normal = new(0.78f, 0.82f, 0.85f);
    private static readonly Color CurrentFg = new(1f, 1f, 1f);
    private static readonly Color TooDear = new(0.62f, 0.42f, 0.38f);
    private static readonly Color CurrentBg = new(0.20f, 0.30f, 0.22f, 0.9f);
    private static readonly Color HoverBg = new(1f, 1f, 1f, 0.10f);

    private int RowAt(Vector2 local)
    {
        float step = Step;
        int i = (int)((local.Y + 4) / step) - 1;
        return i >= 0 && i < Mathf.Min(_shown.Count, Capacity) ? i : -1;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm)
        {
            int h = RowAt(mm.Position);
            if (h != _hover) { _hover = h; QueueRedraw(); }
            return;
        }
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb)
        {
            int i = RowAt(mb.Position);
            if (i >= 0) { _pick?.Invoke(i); QueueRedraw(); }
            AcceptEvent();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _hover != -1) { _hover = -1; QueueRedraw(); }
    }

    /// <summary>For a scripted run, which cannot look at the screen.</summary>
    public string WatchLine()
    {
        var rows = _rows?.Invoke() ?? new List<Row>();
        int aff = 0, cur = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Affordable) aff++;
            if (rows[i].Current) cur = i;
        }
        return $"bau-panel: {(Visible ? "sichtbar" : "aus")}, {rows.Count} Zeilen " +
               $"({Capacity} passen), {aff} bezahlbar, Auswahl " +
               (cur < 0 ? "-" : $"{cur + 1} \"{rows[cur].Name}\"");
    }
}
