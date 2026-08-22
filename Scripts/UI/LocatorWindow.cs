namespace AkteEuropaReborn.UI;

using Godot;
using System.Collections.Generic;

/// <summary>
/// <b>»LOKATOR«</b> — Fensterart <b>24</b> des Originals (C <c>0x47A740</c>,
/// F <c>0x479030</c>, 280×140), gelesen am 20.08.2026.
///
/// <para>Vier Merkpunkte, jeder eine gemerkte Stelle der Karte. Das Original
/// schreibt seine Tastenbelegung selbst in die Zeilen — sie heissen
/// <b>»F5 …«</b> bis <b>»F8 …«</b>, zusammengeklebt aus <c>"F"</c>,
/// <c>itoa(i+5)</c>, einem Leerzeichen und dem Namen.</para>
///
/// <code>
///  (10,2)    "Lokator"                     Titel
///  (20,20)   Kasten 240x80                 die vier Zeilen
///  (40,30+15i) "F<n> " + Name (+ "_")      Zeile i, y-Schritt 15
///  (30,105)  KNOPF "Lokalisieren"  100x20  El 1
///  (150,105) KNOPF "Sichern"       100x20  El 2
/// </code>
///
/// <para><b>Was die zwei Knöpfe tun</b>, aus dem Klickblock C <c>0x44C0EB</c>:
/// »Lokalisieren« ruft <c>0x438AE0</c> und schliesst das Fenster,
/// »Sichern« ruft <c>0x438BD0</c> und schliesst es ebenfalls. Beide
/// Speicherroutinen haben <b>genau einen Rufer</b> — es gibt keinen
/// Tastenweg zum Sichern.</para>
///
/// <para>⚠ <b>Gesichert wird die Zelle in der MITTE des Ausschnitts</b>
/// (<c>0x438BD0</c>: <c>col = links + Breite/2</c>), nicht die Mausstelle. Und
/// ein leerer Name wird auf <b>»NONAME«</b> gesetzt (0x4FAB90). Beides ist
/// gelesen und hier so gebaut.</para>
///
/// <para>⚠ <b>Das Aussehen ist unseres</b> — wir haben die Kacheln des
/// Originals nicht. Übernommen sind seine <b>Wörter</b>, die <b>Zahl vier</b>,
/// die <b>Tastennamen in den Zeilen</b> und die zwei Knöpfe mit ihrer
/// Wirkung. Die Koordinaten oben stehen da, damit ein späterer Nachbau mit den
/// echten Kacheln sie nicht neu suchen muss.</para>
/// </summary>
public sealed partial class LocatorWindow : PanelContainer
{
    private readonly ItemList _liste = new();
    private readonly LineEdit _name = new();
    private readonly Button _hin = new(), _sichern = new(), _zu = new();

    /// <summary>Springt zu Merkpunkt <c>i</c>.</summary>
    public System.Action<int>? OnLocate;

    /// <summary>Sichert Merkpunkt <c>i</c> unter diesem Namen.</summary>
    public System.Action<int, string>? OnSave;

    /// <summary>Liefert die vier Zeilen: Name und ob der Punkt gesetzt ist.</summary>
    public System.Func<List<(string Name, bool Gesetzt)>>? Rows;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(280, 140);
        var senk = new VBoxContainer();
        senk.AddThemeConstantOverride("separation", 6);
        AddChild(senk);

        var kopf = new HBoxContainer();
        kopf.AddChild(new Label { Text = "Lokator", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _zu.Text = "X";
        _zu.FocusMode = FocusModeEnum.None;
        kopf.AddChild(_zu);
        senk.AddChild(kopf);

        _liste.CustomMinimumSize = new Vector2(240, 80);
        senk.AddChild(_liste);

        _name.PlaceholderText = "Name (hoechstens 20 Zeichen)";
        _name.MaxLength = 20;          // die Grenze des Originals: 0x4588E0(…, 20)
        senk.AddChild(_name);

        var reihe = new HBoxContainer();
        reihe.AddThemeConstantOverride("separation", 20);
        _hin.Text = "Lokalisieren";
        _sichern.Text = "Sichern";
        foreach (var b in new[] { _hin, _sichern })
        { b.FocusMode = FocusModeEnum.None; b.CustomMinimumSize = new Vector2(100, 20); reihe.AddChild(b); }
        senk.AddChild(reihe);

        _liste.ItemSelected += _ => NameNachziehen();
        _hin.Pressed += () => { int i = Gewaehlt(); if (i >= 0) { OnLocate?.Invoke(i); WindowManager.Schliessen(WindowManager.ArtMerkpunkte); } };
        _sichern.Pressed += () =>
        {
            int i = Gewaehlt();
            if (i < 0) return;
            // ⭐ Namensfilter wie bei den Gruppen — siehe SkirmishSetup.FilterName.
            OnSave?.Invoke(i, SkirmishSetup.FilterName(_name.Text));
            Refresh();
            Hide();                    // das Original schliesst nach beiden Knöpfen
        };
        // ⭐ Ueber die Verwaltung zu, damit die Zublende ueber sechs
        // Bilder laeuft (BM.10) statt hart zu verschwinden.
        _zu.Pressed += () => WindowManager.Schliessen(WindowManager.ArtMerkpunkte);
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

    /// <summary>Aufmachen mit der Zeile <paramref name="zeile"/> vorgewählt —
    /// genau das tut <c>Strg+F5..F8</c> im Original (0x442D40).</summary>
    public void Open(int zeile)
    {
        Refresh();
        if (zeile >= 0 && zeile < _liste.ItemCount) _liste.Select(zeile);
        NameNachziehen();
        // ⭐ Ueber die Fensterverwaltung (BM): sie sperrt das zweite
        // Oeffnen, holt nach vorn und blendet ueber vier Bilder auf.
        // ⚠ Ist es schon offen, tut ein zweiter Ruf NICHTS -- genau das ist
        // die Doppeloeffnungssperre des Originals.
        if (WindowManager.Offen(WindowManager.ArtMerkpunkte) == null)
            WindowManager.Oeffnen(WindowManager.ArtMerkpunkte, this);
        else
            WindowManager.NachVorn(WindowManager.Offen(WindowManager.ArtMerkpunkte));
        Show();
    }

    public void Refresh()
    {
        var r = Rows?.Invoke();
        int merk = Gewaehlt();
        _liste.Clear();
        if (r == null) return;
        for (int i = 0; i < r.Count; i++)
            // Der Wortlaut des Originals: "F5 <Name>". Ein leerer Punkt zeigt
            // das ausdrücklich an, statt bloss eine leere Zeile zu sein — sonst
            // sieht »nicht gesetzt« aus wie »Name vergessen«.
            _liste.AddItem($"F{i + 5} {(r[i].Gesetzt ? r[i].Name : "—")}");
        if (merk >= 0 && merk < _liste.ItemCount) _liste.Select(merk);
    }
}
