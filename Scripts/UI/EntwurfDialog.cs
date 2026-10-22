using System;
using Godot;

namespace AkteEuropaReborn.UI;

/// <summary>
/// ⭐ 22.10.2026, bug-592 — <b>die zwei kleinen Fenster des Entwurfsverwalters</b>.
///
/// <list type="bullet">
/// <item><b>Eingabefenster Art 36</b> (<c>0x442380 → 0x45B230(x−3, y−3, Frage, 0x13)</c>):
/// 300 × 60 (<c>+0x8B903E = 0x12C</c>, <c>+0x8B9040 = 0x3C</c>), Frage
/// »Wählen Sie einen Namen für dieses Teil« (<c>0x4FC53C</c>), höchstens 19 Zeichen
/// (<c>+0x8B9242 = 0x13</c>), Zeichenannahme <c>0x4588E0</c> (0x20…0x7A ohne
/// <c>[ ] ^</c>, Backspace). Bestätigen (Modus 2, <c>0x44D0F2</c>) → Befehl 533.
/// Nur eines zugleich (<c>+0x8C3CD8 == 2</c> → nichts tun).</item>
/// <item><b>Abfragefenster Art 10</b> (<c>0x446880 → 0x401B40 → 0x458150</c>): 300 × 100,
/// »Einheitentyp löschen?« (<c>0x4FC9EC</c>), Knopf 1 (<c>0x44B0EB</c>) → Befehl 507.
/// Dass Knopf 1 »Ja« ist, folgt der Schwester 529 (<c>0x44B132</c>, UI/UnitOrderBar.cs).</item>
/// </list>
///
/// <para>⚠ <b>UNSERE SETZUNGEN</b>: die Knopfwörter des Eingabefensters (»OK«/»Abbruch«,
/// im Original ungelesen), Enter = Bestätigen, ESC = Schliessen, Lage mittig, Farben wie
/// die übrigen Fenster; Größen mit der Schrift skaliert.</para>
/// </summary>
public sealed partial class EntwurfDialog : PanelContainer
{
    private readonly Label _frage = new();
    private readonly Label _eingabe = new();
    private readonly Button _ja = new(), _nein = new();
    private Action<string>? _beiName;
    private Action? _beiJa;
    private int _max = 19;
    private string _name = "";

    /// <summary>Welches Fenster offen ist: 0 keins, 36 Eingabe, 10 Abfrage.</summary>
    public int Art { get; private set; }

    public EntwurfDialog()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.30f, 0.28f, 0.25f), BorderColor = new Color(0.55f, 0.51f, 0.45f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
        };
        sb.SetBorderWidthAll(2);
        AddThemeStyleboxOverride("panel", sb);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        AddChild(box);
        _frage.AddThemeColorOverride("font_color", new Color(244 / 255f, 184 / 255f, 28 / 255f));
        box.AddChild(_frage);
        _eingabe.AddThemeColorOverride("font_color", new Color(235 / 255f, 231 / 255f, 231 / 255f));
        box.AddChild(_eingabe);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 16);
        box.AddChild(row);
        row.AddChild(_ja);
        row.AddChild(_nein);
        _ja.FocusMode = FocusModeEnum.None;
        _nein.FocusMode = FocusModeEnum.None;
        _ja.Pressed += Bestaetigen;
        _nein.Pressed += Schliessen;
    }

    /// <summary>Eingabefenster Art 36 öffnen. false = es läuft schon eines.</summary>
    public bool FrageName(string frage, int max, Action<string> dann)
    {
        if (Visible) return false;
        Art = 36; _max = max; _name = ""; _beiName = dann; _beiJa = null;
        _frage.Text = frage;
        _eingabe.Visible = true;
        _ja.Text = "OK"; _nein.Text = "Abbruch";
        Zeigen(300, 60);
        return true;
    }

    /// <summary>Abfragefenster Art 10 öffnen. false = es läuft schon eines.</summary>
    public bool FrageJaNein(string frage, Action ja)
    {
        if (Visible) return false;
        Art = 10; _beiJa = ja; _beiName = null;
        _frage.Text = frage;
        _eingabe.Visible = false;
        _ja.Text = "Ja"; _nein.Text = "Nein";
        Zeigen(300, 100);
        return true;
    }

    public void SetFont(Font? font, int size)
    {
        if (font == null) return;
        foreach (var c in new Control[] { _frage, _eingabe, _ja, _nein })
        {
            c.AddThemeFontOverride("font", font);
            c.AddThemeFontSizeOverride("font_size", size);
        }
        _skala = size / 13f;
    }

    private float _skala = 2f;

    private void Zeigen(int b, int h)
    {
        Neu();
        Visible = true;
        CustomMinimumSize = new Vector2(b, h) * _skala;
        var vp = GetViewportRect().Size;
        var want = GetCombinedMinimumSize();
        Size = want;
        GlobalPosition = new Vector2((vp.X - want.X) * 0.5f, (vp.Y - want.Y) * 0.5f);
    }

    private void Neu() => _eingabe.Text = "> " + _name + "_";

    private void Bestaetigen()
    {
        int art = Art;
        var n = _beiName; var j = _beiJa; string name = _name;
        Schliessen();
        if (art == 36) { if (name.Trim().Length > 0) n?.Invoke(name); }
        else if (art == 10) j?.Invoke();
    }

    private void Schliessen()
    {
        Visible = false;
        Art = 0;
        _beiName = null; _beiJa = null;
    }

    /// <summary>Ein Zeichen wie <c>0x4588E0</c>.</summary>
    public void Zeichen(int c)
    {
        if (Art != 36) return;
        if (c >= 0x20 && c <= 0x7A && c is not ('[' or ']' or '^') && _name.Length < _max) _name += (char)c;
        else if (c == 8 && _name.Length > 0) _name = _name[..^1];
        Neu();
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey k || !k.Pressed) return;
        switch (k.Keycode)
        {
            case Key.Escape: Schliessen(); break;
            case Key.Enter or Key.KpEnter: Bestaetigen(); break;
            case Key.Backspace: Zeichen(8); break;
            default:
                if (Art == 36 && k.Unicode is > 0 and < 0x10000) Zeichen((int)k.Unicode);
                break;
        }
        GetViewport().SetInputAsHandled();
    }
}
