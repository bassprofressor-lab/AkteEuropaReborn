namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»FORSCHUNGSERGEBNISSE«</b> — <b>Fensterart 29</b> des Originals, das
/// dritte und kleinste der drei Auskunftsfenster des Haupt-Menüs
/// (<see cref="MainMenuWindow"/>).
///
/// <para>⭐⭐ <b>Der Name führt in die Irre: das Fenster zeigt die
/// ERFINDUNGEN, nicht die Forschung.</b> Es geht die Bauteiltafel
/// (<c>0x5045A0</c>, 58 Byte je Satz) durch und zeigt jeden Satz, dessen
/// <b>Techstufe <c>+0x24</c> gleich 10</b> ist (<c>cmp byte [edx*2 + 0x5045C4],
/// cl</c> mit <c>cl = 10</c> @0x47CB3E). Und die 10 schreibt genau EINE Stelle:
/// <c>0x4AB1B6</c>, der Erfindungsvorgang — bei uns
/// <see cref="Rendering.MapEntityLayer.Erfinden"/>, wo <c>B[0x24] = 10</c>
/// steht. Kein Bauteil der Auslieferung trägt die 10; die stehen alle auf
/// 1…8.</para>
///
/// <para>⚠ <b>In einer frischen Kampagne ist dieses Fenster LEER</b>, und das
/// ist richtig so. Wer hier etwas sieht, hat erfunden.</para>
///
/// <code>
///   Anleger 0x45A359   220 x 220 = 11 x 11 Kacheln
///   0x47CAD9  Titel "Forschungsergebnisse" (0x501EC8) auf (10, 2), 0x96/0xA9
///   0x47CAF8  Innenrahmen (20, 20), 8 x 9 Kacheln  = 160 x 180 Punkte
///   0x47CC26  hoechstens ZEHN Zeilen
///   0x47CC8A  Zeile y = 15·i + 35, x = 32 (@0x47CC8E)
///   0x47CC03  Rollbalken (x=180, y=20, 9 Kacheln), Anzahl = Zaehler − 9
///   KEINE Knoepfe — kein einziger Ruf von 0x401820 in der ganzen Funktion.
/// </code>
///
/// <para>⚠ <b>Der Name kommt aus <c>+0x25</c></b> (dem LANGEN Namensfeld,
/// <c>0x5045C5 + 58·k</c> @0x47CC53) und wird mit <c>0x401041</c> gezeichnet —
/// <b>ohne Farbargument</b>, also in der Vorgabefarbe. Bei einer Erfindung legt
/// unser Erzeuger denselben Namen auf <c>+0x02</c> und <c>+0x25</c>, so wie
/// <c>0x4AB335</c> es tut.</para>
///
/// <para>⚠⚠ <b>ZWEI FEHLER DES ORIGINALS, beide nachgebaut und beide
/// abschaltbar</b> — siehe
/// <see cref="Rendering.MapEntityLayer.ForschungsergebnisseZeilen"/>. Sie
/// werden hier NICHT stillschweigend berichtigt: ein Nachbau, der die Fehler
/// der Vorlage wegputzt, ist kein Nachbau mehr.</para>
/// </summary>
public sealed partial class ResearchListView : Control
{
    /// <summary>Die Fensterart des Originals.</summary>
    public const int Art = 29;

    /// <summary>11 x 11 Kacheln — die 220 x 220 des Anlegers
    /// <c>0x45A359</c>.</summary>
    public const int WTiles = 11, HTiles = 11;

    public const int Scale = 2;

    private const int TitleX = 10, TitleY = 2;

    /// <summary>Der Innenrahmen (@0x47CAF8): (20, 20), 8 x 9 Kacheln.</summary>
    public const int RahmenX = 20, RahmenY = 20, RahmenW = 8, RahmenH = 9;

    /// <summary>Zehn Zeilen (@0x47CC26), x = 32 (@0x47CC8E),
    /// y = 15·i + 35 (@0x47CC8A).</summary>
    public const int Zeilen = 10, ZeilenHoehe = 15, TextX = 32, TextY0 = 35;

    /// <summary>Der Rollbalken (@0x47CC03) — schmaler und kürzer als in den
    /// zwei anderen Listen.</summary>
    public const int RollX = 180, RollY = 20, RollTiles = 9;

    public Action? OnClose;

    private readonly List<string> _namen = new();
    private int _stand;
    private bool _zieht;

    public int Anzahl => _namen.Count;
    public int Stand => _stand;

    public static bool Usable => WindowChrome.Atlas != null
                                 && WindowChrome.LegacyFont != null;

    /// <summary><c>--forschungsliste-alt</c> — es gibt kein Fenster, der Knopf
    /// im Haupt-Menü bleibt gedimmt. Nullmodell von
    /// <c>--forschungsliste-check</c>.</summary>
    public static bool Alt;

    public ResearchListView()
    {
        CustomMinimumSize = new Vector2(WTiles * WindowChrome.Cell * Scale,
                                        HTiles * WindowChrome.Cell * Scale);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
    }

    public void Zeige(IEnumerable<string> namen)
    {
        _namen.Clear();
        _namen.AddRange(namen);
        _stand = _namen.Count <= Zeilen
            ? 0 : Mathf.Clamp(_stand, 0, _namen.Count - Zeilen);
        QueueRedraw();
    }

    /// <summary>Der Rollbalken erscheint erst ab elf Einträgen
    /// (<c>cmp …, 0xA</c> @0x47CBD8).</summary>
    public bool HatRollbalken => _namen.Count > Zeilen;

    /// <summary>Die »Anzahl« für den Rollbalken: Zähler − 9 (@0x47CBDE).</summary>
    public int RollAnzahl => Mathf.Max(1, _namen.Count - (Zeilen - 1));

    private static int FontSize => WindowChrome.FontCell * Scale;

    private void Text(Font f, int x, int y, string s, Color c)
        => DrawString(f, new Vector2(x * Scale, y * Scale + f.GetAscent(FontSize)),
                      s, HorizontalAlignment.Left, -1, FontSize, c);

    public override void _Draw()
    {
        var font = WindowChrome.LegacyFont;
        if (WindowChrome.Atlas == null || font == null) return;

        WindowChrome.Paint(this, WTiles, HTiles, Scale);
        Text(font, TitleX, TitleY, "Forschungsergebnisse", WindowChrome.TitleColour);
        WindowChrome.PaintInnerFrame(this, RahmenX, RahmenY, RahmenW, RahmenH, Scale);

        for (int i = 0; i < Zeilen; i++)
        {
            int idx = _stand + i;
            if (idx >= _namen.Count) break;               // @0x47CC1E
            Text(font, TextX, TextY0 + ZeilenHoehe * i, _namen[idx],
                 WindowChrome.TextColour);
        }

        if (HatRollbalken)                               // @0x47CBD8
            WindowChrome.PaintScrollbar(this, RollX, RollY, RollTiles,
                                        _stand, RollAnzahl, Scale);
    }

    /// <summary>−2 Schliesskreuz, −3 Rollbalken, sonst 0 (ziehen). ⚠ Eine
    /// Zeile ist NICHT anklickbar: das Original hat hier weder Auswahlbalken
    /// noch Cursor.</summary>
    public int Hit(Vector2 p)
    {
        float x = p.X / Scale, y = p.Y / Scale;
        int w = WTiles * WindowChrome.Cell;
        if (x >= w - 20 && x < w && y >= 0 && y < 20) return -2;
        if (HatRollbalken
            && WindowChrome.ScrollHit(RollX, RollY, RollTiles, RollAnzahl,
                                      new Vector2(x, y)) >= 0) return -3;
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
        if (mb.Pressed) { if (t == 0) _zieht = true; AcceptEvent(); return; }
        if (_zieht) { _zieht = false; AcceptEvent(); return; }
        if (t == 0) return;
        AcceptEvent();
        if (t == -2) { OnClose?.Invoke(); return; }
        int neu = WindowChrome.ScrollHit(RollX, RollY, RollTiles, RollAnzahl,
                                         mb.Position / Scale);
        if (neu >= 0)
        {
            _stand = Mathf.Clamp(neu, 0, Mathf.Max(0, _namen.Count - Zeilen));
            QueueRedraw();
        }
    }

    /// <summary>ESC schliesst das Fenster — derselbe Grund wie in den zwei
    /// anderen Listen: der Baum steht.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            OnClose?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    public override string _GetTooltip(Vector2 pos) => Hit(pos) switch
    {
        -2 => "Die Forschungsergebnisse schliessen",
        -3 => "Rollen",
        _ => _namen.Count == 0
             ? "Hier stehen die ERFINDUNGEN (Techstufe 10). Noch ist keine gemacht."
             : "",
    };
}
