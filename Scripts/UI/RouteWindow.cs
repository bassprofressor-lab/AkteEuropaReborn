namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>»VON · NACH · LEEREN · START«</b> — das Routenfenster des Transporters,
/// <b>Fensterart 16</b> des Originals (Anleger <c>0x458FD0</c>, Zeichner
/// <c>0x473BF0</c>, Trefferprüfung <c>0x45EF48</c>, Klickarm <c>0x44B625</c>).
/// Gebaut am 08.09.2026 nach <c>berichte/transporterroute-fable.md</c>, Teil 2.
///
/// <para><b>Die Anordnung ist die des Originals</b>, Punkt für Punkt aus dem
/// Zeichner:</para>
/// <code>
///   Fenster 280 x 120
///   "Von :"   (20, 35)      die vier Quellnamen bei (70, 35 + 15·i)
///   "Nach :"  (20, 95)      der Zielname oder "leer" bei (70, 95)
///   vier Knoepfe auf y+14, je 60x20:
///        Von (20) · Nach (80) · Leeren (140) · Start (200)
///   »Nach« ist beim Aufgehen gedrueckt (+0xACAC := 1)
/// </code>
///
/// <para><b>Die Hilfezeilen sind die des Originals</b> (Trefferprüfung
/// <c>0x45EF48</c>, Hilfetexte 0x2F..0x32): »Wählen des Startpunktes«, »Wählen
/// des Zielpunktes«, »Löschen des Transport-Zyklus«, »Transport starten«.</para>
///
/// <para>⚠ <b>Unser Aussehen</b>, wie bei jedem Fenster hier: wir haben die
/// Kacheln des Originals nicht. Übernommen sind seine <b>Wörter</b>, seine
/// <b>Anordnung</b> und sein <b>Ablauf</b> (Nach → Ziel → Von → Quellen →
/// Start). Die Gebäude werden bei uns auf der Hauptkarte angeklickt statt auf
/// der Planungskarte in Betriebsart 4 — siehe
/// <c>Simulation/TransportrouteFenster.cs</c>.</para>
/// </summary>
public sealed partial class RouteWindow : PanelContainer
{
    /// <summary>Was das Fenster anzeigt — kommt aus
    /// <c>MapEntityLayer.RouteAnzeige</c>.</summary>
    public Func<(string[] Quellen, string Ziel, bool Gestartet)>? Inhalt;

    /// <summary>Welcher Knopf gerade gedrückt ist: 1 = Von, 2 = Nach.</summary>
    public Func<int>? Modus;

    /// <summary>Die Rückmeldezeile (»Kann nicht starten.« …).</summary>
    public Func<string>? Note;

    public Action<int>? OnModus;     // 1 = Von, 2 = Nach
    public Action? OnLeeren;
    public Action? OnStart;
    public Action? OnClose;

    private readonly Label _von = new(), _nach = new(), _note = new();
    private readonly Label[] _quelle = { new(), new(), new(), new() };
    private readonly Button _bVon = new(), _bNach = new(), _bLeer = new(), _bStart = new();

    public RouteWindow()
    {
        CustomMinimumSize = new Vector2(320, 190);
        MouseFilter = MouseFilterEnum.Stop;

        var aussen = new VBoxContainer();
        aussen.AddThemeConstantOverride("separation", 4);
        AddChild(aussen);

        var kopf = new HBoxContainer();
        var titel = new Label { Text = "Transportzyklus", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var zu = new Button { Text = "X", CustomMinimumSize = new Vector2(24, 0) };
        zu.Pressed += () => OnClose?.Invoke();
        kopf.AddChild(titel); kopf.AddChild(zu);
        aussen.AddChild(kopf);

        // »Von :« und die vier Quellen — im Original (20,35) und (70, 35+15i).
        aussen.AddChild(_von);
        _von.Text = "Von :";
        foreach (var q in _quelle)
        {
            var zeile = new HBoxContainer();
            zeile.AddChild(new Control { CustomMinimumSize = new Vector2(50, 0) });
            zeile.AddChild(q);
            aussen.AddChild(zeile);
        }

        // »Nach :« — im Original (20,95), der Name bei (70,95).
        var nachZeile = new HBoxContainer();
        nachZeile.AddChild(new Label { Text = "Nach :", CustomMinimumSize = new Vector2(50, 0) });
        nachZeile.AddChild(_nach);
        aussen.AddChild(nachZeile);

        // Die vier Knoepfe, in der Reihenfolge des Originals.
        var reihe = new HBoxContainer();
        reihe.AddThemeConstantOverride("separation", 4);
        Anlegen(_bVon, "Von", "Waehlen des Startpunktes", () => OnModus?.Invoke(1), reihe);
        Anlegen(_bNach, "Nach", "Waehlen des Zielpunktes", () => OnModus?.Invoke(2), reihe);
        Anlegen(_bLeer, "Leeren", "Loeschen des Transport-Zyklus", () => OnLeeren?.Invoke(), reihe);
        Anlegen(_bStart, "Start", "Transport starten", () => OnStart?.Invoke(), reihe);
        aussen.AddChild(reihe);

        _note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _note.CustomMinimumSize = new Vector2(300, 0);
        aussen.AddChild(_note);
    }

    private static void Anlegen(Button b, string text, string hilfe, Action tat, Container wohin)
    {
        b.Text = text;
        b.TooltipText = hilfe;              // die Hilfezeile des Originals
        b.CustomMinimumSize = new Vector2(60, 22);
        b.ToggleMode = false;
        b.Pressed += tat;
        wohin.AddChild(b);
    }

    /// <summary>Den Inhalt nachziehen. ⚠ Der gedrückte Knopf wird MARKIERT und
    /// nicht bloss gefärbt: im Original ist die Druckflagge des Fenstersatzes
    /// (<c>+0xACA8</c>/<c>+0xACAC</c>) genau das, was den Kartenklick
    /// deutet — der Spieler muss sehen, welcher der beiden gerade gilt.</summary>
    public void Refresh()
    {
        if (Inhalt == null) return;
        var (quellen, ziel, gestartet) = Inhalt();
        for (int k = 0; k < 4; k++) _quelle[k].Text = quellen[k];
        _nach.Text = ziel;
        int m = Modus?.Invoke() ?? 0;
        _bVon.Text = m == 1 ? "[Von]" : "Von";
        _bNach.Text = m == 2 ? "[Nach]" : "Nach";
        _note.Text = (Note?.Invoke() ?? "") + (gestartet ? "   (Route laeuft)" : "");
    }

    /// <summary>Unten links, ueber dem Bedienblock — dieselbe Ecke wie die
    /// Befehlsleiste, damit beide zusammen lesbar bleiben.</summary>
    public void PlaceBottomLeft()
    {
        var s = GetViewportRect().Size;
        Position = new Vector2(24, s.Y - Size.Y - 140);
    }
}
