namespace AkteEuropaReborn.UI;

using System;
using Godot;

/// <summary>
/// <b>DIE BEFEHLSLEISTE DER GEWÄHLTEN EINHEIT</b> — und der Ort, an dem
/// »Verkaufen« überhaupt erst auffindbar wird.
///
/// <para><b>Warum es sie gibt.</b> Das Original hat für einen Einheitenbefehl
/// eine eigene Liste: <c>0x4FD660</c>, 30-Byte-Raster, <c>Angreifen · Bewegen ·
/// Beschützen · Selbstzerstörung · Verkaufen · Handsteuerung ·
/// Einheiteninformation · Eingraben · Ausgraben · …</c>. Der Fensterverteiler
/// @0x448746 nimmt den Aktionscode eines Knopfes aus
/// <c>word[Fenster+0x0C+2·Knopf]</c> und springt danach; Code <b>4</b> öffnet
/// das Verkaufsfenster (@0x448800), Code 3 setzt die Selbstzerstörung als
/// Kommando 13, Code 7 das Eingraben als Kommando 26. <b>Es ist also eine
/// Knopfleiste, kein Tastenkürzel.</b></para>
///
/// <para>⚠ <b>Und das ist die Lehre aus vier eigenen Fehlern</b> (C2, C6, D1,
/// E-A): Forschung, Reparatur, Depot und Hangar waren alle vier <i>gebaut</i>
/// und lagen auf den Tasten O, K, Y — und wurden alle vier als »fehlt«
/// gemeldet, weil die Oberfläche schwieg. Eine Mechanik, die nur auf einer
/// Taste liegt, ist für den Spieler nicht vorhanden. Deshalb steht die Leiste
/// da, sobald eine eigene Einheit gewählt ist, statt aufgeklappt werden zu
/// müssen.</para>
///
/// <para><b>⚠ UNSERE Setzungen, benannt:</b> die STELLE (unten mittig über dem
/// Bedienblock — im Original sitzen die Knöpfe im Block selbst, dessen
/// Bildfeldaufteilung wir nicht vollständig gelesen haben), die AUSWAHL der
/// gezeigten Befehle (nur die, die es bei uns wirklich gibt — ein Knopf, der
/// nichts tut, ist schlimmer als keiner), und dass der Preis <b>im Knopf
/// steht</b>. Das Original zeigt ihn erst im Dialog. Er dort schon zu nennen
/// spart einen Klick und macht sichtbar, dass der Wert mit der Hülle
/// fällt.</para>
/// </summary>
public sealed partial class UnitOrderBar : PanelContainer
{
    /// <summary>Was verkauft würde und wofür — <c>null</c> blendet den Knopf
    /// aus. Kommt aus <c>MapEntityLayer.SellChoiceOfSelection</c>.</summary>
    public Func<(string Name, int Price)?>? SellChoice;

    /// <summary>Der Verkauf, nachdem der Spieler zugestimmt hat.</summary>
    public Action? OnSell;

    /// <summary>»Eingraben« / »Ausgraben« — dieselbe Stelle wie Taste E.</summary>
    public Action? OnDigIn;

    /// <summary>»Anhalten« — dieselbe Stelle wie Taste X.</summary>
    public Action? OnStop;

    /// <summary>Die Rückmeldung des letzten Befehls, z. B. warum nichts
    /// geschah.</summary>
    public Func<string>? Note;

    private readonly Button _sell = new(), _dig = new(), _stop = new();
    private readonly Label _note = new();
    private readonly ConfirmDialog _ask = new();

    public UnitOrderBar()
    {
        MouseFilter = MouseFilterEnum.Stop;
        // Wie das Baumenü: auch bei angehaltenem Baum bedienbar.
        ProcessMode = ProcessModeEnum.Always;

        var box = new VBoxContainer();
        AddChild(box);

        var row = new HBoxContainer();
        box.AddChild(row);

        // ⚠ Die Wörter kommen NICHT von uns: es sind die Einträge 4, 7/8 und 26
        // der Befehlsliste des Originals. Sie stehen in Maps/orders.json, das
        // der Import aus 0x4FD660 schreibt — siehe MapEntityLayer.OrderName.
        // Ein selbst erfundenes Wort an dieser Stelle wäre eine Abweichung, die
        // niemand als solche erkennen könnte.
        _sell.Pressed += AskAndSell;
        _dig.Pressed += () => OnDigIn?.Invoke();
        _stop.Pressed += () => OnStop?.Invoke();
        row.AddChild(_sell);
        row.AddChild(_dig);
        row.AddChild(_stop);

        _note.AutowrapMode = TextServer.AutowrapMode.Off;
        box.AddChild(_note);

        AddChild(_ask);
    }

    /// <summary>Die Wörter, die von aussen gesetzt werden — damit die Leiste
    /// die Namen des Originals trägt und nicht unsere. Aufrufer:
    /// <c>MapViewer.BuildUnitOrderBar</c>.</summary>
    public void SetWords(string sell, string digIn, string stop)
    {
        _sellWord = sell; _dig.Text = digIn; _stop.Text = stop;
    }

    private string _sellWord = "Verkaufen";

    /// <summary>
    /// <b>Der Dialog des Originals, wörtlich.</b> Er rechnet nichts und fragt
    /// nichts ab: <c>»Akzeptieren Sie $X für diese Einheit?«</c> (0x4FC85C +
    /// 0x4FC8C0), und der Betrag steht schon fest.
    ///
    /// <para>⚠ <b>Kein Eingabefeld, keine Plus/Minus-Tasten</b> — das ist
    /// gelesen (@0x446470 ist ganz gelesen, es gibt dort keinen Eingabepfad)
    /// und wichtig, weil die naheliegende Annahme »man handelt den Preis aus«
    /// falsch ist. Die frühere Notiz »der Preis, den der Verkäufer selbst
    /// gesetzt hat« ist zurückgezogen: gesetzt hat ihn das SPIEL, mit
    /// 30 % des Werts.</para>
    /// </summary>
    private void AskAndSell()
    {
        var w = SellChoice?.Invoke();
        if (w == null) return;
        _ask.Ask($"Akzeptieren Sie ${w.Value.Price} fuer diese Einheit?",
                 () => OnSell?.Invoke());
    }

    /// <summary>Nachziehen. Gibt zurück, ob die Leiste überhaupt etwas zu
    /// zeigen hat — der Aufrufer blendet sie danach ein oder aus.</summary>
    public bool Refresh()
    {
        var w = SellChoice?.Invoke();
        _sell.Visible = w != null;
        if (w != null) _sell.Text = $"{_sellWord} (${w.Value.Price})";
        string n = Note?.Invoke() ?? "";
        _note.Visible = n.Length > 0;
        _note.Text = n;
        return true;
    }

    /// <summary>Unten mittig, über dem Bedienblock. ⚠ Die Ecken sind alle
    /// vergeben (Bedienblock unten links, Übersichtskarte unten rechts,
    /// Basisfenster oben rechts, Rohstoffleiste oben mittig) — unten mittig ist
    /// das, was übrig ist, und es verdrängt nichts.</summary>
    public void PlaceBottomCentre()
    {
        var vp = GetViewportRect().Size;
        var want = GetCombinedMinimumSize();
        Size = want;
        Position = new Vector2((vp.X - want.X) * 0.5f, vp.Y - want.Y - 12);
    }

    public void SetFont(Font? font, int size)
    {
        if (font == null) return;
        foreach (var c in new Control[] { _sell, _dig, _stop, _note })
        {
            c.AddThemeFontOverride(c is Button ? "font" : "font", font);
            c.AddThemeFontSizeOverride(c is Button ? "font_size" : "font_size", size);
        }
        _ask.SetFont(font, size);
    }

    /// <summary>Eine Ja/Nein-Frage in der Bauart des Originals: eine Zeile, zwei
    /// Knöpfe, kein Eingabefeld.</summary>
    private sealed partial class ConfirmDialog : PanelContainer
    {
        private readonly Label _text = new();
        private readonly Button _yes = new() { Text = "Ja" }, _no = new() { Text = "Nein" };
        private Action? _then;

        public ConfirmDialog()
        {
            Visible = false;
            MouseFilter = MouseFilterEnum.Stop;
            ProcessMode = ProcessModeEnum.Always;
            var box = new VBoxContainer();
            AddChild(box);
            box.AddChild(_text);
            var row = new HBoxContainer();
            box.AddChild(row);
            row.AddChild(_yes);
            row.AddChild(_no);
            // ⚠ Schaltfläche 1 ist das Ja — @0x44B132 prüft `cmp bp, 1`, bevor
            // es Kommando 529 absetzt. Jede andere schliesst nur.
            _yes.Pressed += () => { Visible = false; _then?.Invoke(); _then = null; };
            _no.Pressed += () => { Visible = false; _then = null; };
        }

        public void Ask(string text, Action then)
        {
            _text.Text = text;
            _then = then;
            Visible = true;
            var vp = GetViewportRect().Size;
            var want = GetCombinedMinimumSize();
            Size = want;
            GlobalPosition = new Vector2((vp.X - want.X) * 0.5f, (vp.Y - want.Y) * 0.5f);
        }

        public void SetFont(Font? font, int size)
        {
            if (font == null) return;
            foreach (var c in new Control[] { _text, _yes, _no })
            {
                c.AddThemeFontOverride("font", font);
                c.AddThemeFontSizeOverride("font_size", size);
            }
        }
    }
}
