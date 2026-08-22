namespace AkteEuropaReborn.UI;

using Godot;
using System.Collections.Generic;

/// <summary>
/// <b>»GRUPPIEREN«</b> — Fensterart <b>25</b> des Originals (C <c>0x47AAE0</c>,
/// F <c>0x4793D0</c>, 280×220), gelesen am 20.08.2026.
///
/// <para><b>Zehn Gruppen</b>, jede ein Satz von 422 Byte: <c>+0x00</c> Name
/// (22 B), <c>+0x16</c> zweihundert Mitglieder als u16 mit <c>0xFFFF</c> für
/// einen freien Platz. Zehn × 422 = 4220 = <c>0x107C</c> — genau die Grösse des
/// Abschnitts <b>sec81</b>. Die Adressrechnung stimmt an drei unabhängigen
/// Stellen überein (Zeichner 0x47AC12, Tastenarm 0x41313D, Speicherer
/// 0x438F37).</para>
///
/// <para><b>Was »Speichern« im Original tut</b> (0x438F00), Schritt für
/// Schritt:</para>
/// <list type="number">
/// <item>Es <b>ersetzt</b> die Gruppe vollständig — es hängt nichts an.</item>
/// <item>Es trägt jedes Mitglied <b>aus den anderen neun aus</b>
/// (Doppelschleife @0x438F69). Eine Einheit kann nur in genau einer Gruppe
/// sein.</item>
/// </list>
///
/// <para>⚠ <b>Und es gibt keinen Tastenweg zum Speichern.</b> Der Speicherer
/// hat <b>genau einen</b> Rufer, und das ist dieser Knopf.
/// <c>Strg+Zahl</c> öffnet bloss dieses Fenster mit der Zeile vorgewählt und
/// tauft die Gruppe auf <b>»Group N«</b>. Bei uns war <c>Strg+Zahl</c> bisher
/// das Speichern selbst — das war unsere Setzung, und sie ist jetzt zugunsten
/// des Originals gefallen.</para>
///
/// <para>⚠ <b>Das Aussehen ist unseres.</b> Übernommen sind die Wörter, die
/// Zahl zehn, der Namenszwang (»NONAME« bei leerem Namen) und die
/// Ausschliesslichkeit. Der Kasten oben in
/// <see cref="LocatorWindow"/> nennt die Koordinaten des Originals für einen
/// späteren Nachbau mit den echten Kacheln.</para>
/// </summary>
public sealed partial class GroupWindow : PanelContainer
{
    private readonly ItemList _liste = new();
    private readonly LineEdit _name = new();
    private readonly Button _speichern = new(), _abrufen = new(), _zu = new();

    /// <summary>Die zehn Zeilen: Name und wieviele darin stehen.</summary>
    public System.Func<List<(string Name, int Zahl)>>? Rows;

    /// <summary>Die laufende Auswahl als Gruppe <c>n</c> sichern (1…10).</summary>
    public System.Action<int, string>? OnStore;

    /// <summary>Gruppe <c>n</c> abrufen.</summary>
    public System.Action<int>? OnRecall;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(280, 220);
        var senk = new VBoxContainer();
        senk.AddThemeConstantOverride("separation", 6);
        AddChild(senk);

        var kopf = new HBoxContainer();
        kopf.AddChild(new Label { Text = "Gruppieren", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _zu.Text = "X";
        _zu.FocusMode = FocusModeEnum.None;
        kopf.AddChild(_zu);
        senk.AddChild(kopf);

        _liste.CustomMinimumSize = new Vector2(240, 150);
        _liste.SizeFlagsVertical = SizeFlags.ExpandFill;
        senk.AddChild(_liste);

        _name.PlaceholderText = "Name (hoechstens 20 Zeichen)";
        _name.MaxLength = 20;
        senk.AddChild(_name);

        var reihe = new HBoxContainer();
        reihe.AddThemeConstantOverride("separation", 20);
        _speichern.Text = "Gruppe speichern";
        _abrufen.Text = "Abrufen";
        foreach (var b in new[] { _speichern, _abrufen })
        { b.FocusMode = FocusModeEnum.None; reihe.AddChild(b); }
        senk.AddChild(reihe);

        _liste.ItemSelected += _ => NameNachziehen();
        _speichern.Pressed += () =>
        {
            int i = Gewaehlt();
            if (i < 0) return;
            // ⭐ Der Namensfilter des Originals (BN.12) — die Spielschrift
            // hat fuer alles ausserhalb 0x20..0x7A und fuer [ ] ^ keine Kachel.
            OnStore?.Invoke(i + 1, SkirmishSetup.FilterName(_name.Text));
            Refresh();
            WindowManager.Schliessen(WindowManager.ArtGruppen);
        };
        _abrufen.Pressed += () => { int i = Gewaehlt(); if (i >= 0) { OnRecall?.Invoke(i + 1); WindowManager.Schliessen(WindowManager.ArtGruppen); } };
        // ⭐ Ueber die Verwaltung zu, damit die Zublende ueber sechs
        // Bilder laeuft (BM.10) statt hart zu verschwinden.
        _zu.Pressed += () => WindowManager.Schliessen(WindowManager.ArtGruppen);
    }

    private int Gewaehlt()
    {
        var s = _liste.GetSelectedItems();
        return s.Length > 0 ? s[0] : -1;
    }

    private void NameNachziehen()
    {
        var r = Rows?.Invoke();
        int i = Gewaehlt();
        _name.Text = r != null && i >= 0 && i < r.Count ? r[i].Name : "";
    }

    /// <summary>Aufmachen mit der Zeile vorgewählt und, wenn die Gruppe noch
    /// keinen Namen hat, auf <b>»Group N«</b> getauft — genau das tut
    /// <c>Strg+Zahl</c> im Original (0x442C70).</summary>
    public void Open(int gruppe)
    {
        Refresh();
        int i = gruppe - 1;
        if (i >= 0 && i < _liste.ItemCount) _liste.Select(i);
        NameNachziehen();
        if (_name.Text.Length == 0) _name.Text = $"Group {gruppe}";
        // ⭐ Ueber die Fensterverwaltung (BM): sie sperrt das zweite
        // Oeffnen, holt nach vorn und blendet ueber vier Bilder auf.
        // ⚠ Ist es schon offen, tut ein zweiter Ruf NICHTS -- genau das ist
        // die Doppeloeffnungssperre des Originals.
        if (WindowManager.Offen(WindowManager.ArtGruppen) == null)
            WindowManager.Oeffnen(WindowManager.ArtGruppen, this);
        else
            WindowManager.NachVorn(WindowManager.Offen(WindowManager.ArtGruppen));
        Show();
    }

    public void Refresh()
    {
        var r = Rows?.Invoke();
        int merk = Gewaehlt();
        _liste.Clear();
        if (r == null) return;
        for (int i = 0; i < r.Count; i++)
        {
            // Die Ziffer davor ist die TASTE: das Original zeichnet dort
            // gerahmte Tastenkappen (Zeichen 0xAA..0xB3 der FONT.CWD). Wir
            // schreiben sie hin, weil die Zuordnung Taste↔Zeile sonst nur im
            // Kopf des Spielers steht. ⚠ Gruppe 10 liegt auf der NULL.
            int taste = i + 1 == 10 ? 0 : i + 1;
            _liste.AddItem($"[{taste}] {(r[i].Name.Length > 0 ? r[i].Name : "—")}" +
                           (r[i].Zahl > 0 ? $"  ({r[i].Zahl})" : ""));
        }
        if (merk >= 0 && merk < _liste.ItemCount) _liste.Select(merk);
    }
}
