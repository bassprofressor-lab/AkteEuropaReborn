namespace AkteEuropaReborn.UI;

using Godot;
using System.Collections.Generic;

/// <summary>
/// <b>»MITNEHMBARE EINHEITEN«</b> — das Fenster zwischen zwei
/// Kampagnenmissionen, <b>Fensterart 38</b> des Originals
/// (C <c>0x482290</c>, 8423 B, 600×420; F <c>0x480A70</c>).
/// Gelesen am 20.08.2026, nachdem der Spieler das Bildschirmfoto vom Ende der
/// Mission 25 beigebracht hat.
///
/// <para><b>Warum es das überhaupt geben muss.</b> Ohne dieses Fenster beginnt
/// Mission 26 mit <b>null</b> eigenen Einheiten — die Karte trägt für Spieler 0
/// keine, das Skript setzt keine, der Init-Arm setzt keine. Der Spieler stand
/// vor zehn Gegnern und war in Sekunden zerlegt. Die fünf Einheiten, die im
/// Original dastehen, kommen <b>aus der Vormission</b>.</para>
///
/// <para><b>Was das Original hier zeigt</b>, mit seinen eigenen Wörtern
/// (Zeichen bei C <c>0x502640</c>–<c>0x502740</c>, F <c>0x501680</c>, Versatz
/// 0xFC0):</para>
/// <code>
///  (10,2)   "Sie koennen " &lt;n&gt; " von Ihren Einheiten zur naechsten Mission mitnehmen."
///  (390,30) "Mitnehmbare Einheiten"          Liste rechts
///  (20,30)  Liste links — was mitgenommen wird
///  (225,265)"Berkewitz Corp. bezahlt Ihnen $" &lt;n&gt;
///  (225,280)"fuer Ihre restlichen Einheiten."
///  (210,378)"Kontostand : $" &lt;Geld&gt;
///  (20,375) KNOPF "Mitnehmen &gt;&gt;"                El 1
///  (390,235)KNOPF "&lt;&lt; Zuruecklassen"             El 2
///  (340,375)KNOPF "Start der naechsten Mission"  El 7
/// </code>
///
/// <para><b>Die zwei Zahlen, die dahinterstehen</b>, sind gemessen und nicht
/// gesetzt:</para>
/// <list type="bullet">
/// <item><b>Wieviele</b> mitdürfen: so viele, wie die nächste Mission
/// <b>Stellplätze</b> hat — <c>place_carry(col,row,0)</c> je Platz im Init-Arm.
/// Für Mission 26 sind das fünf, und das Bildschirmfoto sagt »Sie können 5 …«.
/// Siehe <see cref="Campaign.CampaignManager.SpotsFor"/>.</item>
/// <item><b>Was der Rest bringt</b>: <b>30 %</b> des Einheitenwerts,
/// abgerundet. Aus <c>0x4C1720</c>: <c>esi += 3·Wert / 10</c>, summiert über
/// den 1000er-Block des Menschen.</item>
/// </list>
///
/// <para>⚠ <b>UNSERE ABWEICHUNG, ausdrücklich:</b> das Original merkt sich in
/// <c>word[0x9937B8 + 2·i]</c> nur die <b>Platznummern</b> — die Einheitensätze
/// bleiben schlicht im Speicher stehen, weil die nächste Karte den Block des
/// Menschen nicht belegt. Bei uns wird zwischen zwei Missionen alles neu
/// geladen, eine Platznummer zeigte danach ins Leere. Wir merken uns deshalb
/// Entwurf, Restleben und Namen und stellen die Einheit wieder her. Das Ergebnis
/// ist dasselbe; der Weg ist unserer.</para>
///
/// <para>⚠ <b>Das Aussehen ist unseres</b>, wie beim <see cref="BaseWindow"/>:
/// wir haben die Kacheln des Originals nicht. Übernommen sind seine
/// <b>Wörter</b>, seine <b>Anordnung</b> (zwei Listen, Wahl in der Mitte, drei
/// Knöpfe) und die zwei Zahlen oben. Wer später die echten Kacheln baut, findet
/// die Koordinaten im Kasten darüber.</para>
/// </summary>
public sealed partial class CarryWindow : PanelContainer
{
    /// <summary>Eine wählbare Einheit — was <c>CarryCandidates</c> liefert.</summary>
    public readonly struct Row
    {
        public Row(int index, string name, int design, int energie, int wert)
        { Index = index; Name = name; Design = design; Energie = energie; Wert = wert; }
        public readonly int Index, Design, Energie, Wert;
        public readonly string Name;
    }

    private readonly ItemList _frei = new(), _mit = new();
    private readonly Label _kopf = new(), _geld = new(), _konto = new();
    private readonly Button _nehmen = new(), _lassen = new(), _weiter = new();

    private List<Row> _alle = new();
    private readonly List<int> _gewaehlt = new();      // Stellen in _alle
    private int _plaetze, _kontostand;

    /// <summary>Wird gedrückt, wenn »Start der nächsten Mission« kommt — mit
    /// dem, was mitgeht, und dem Erlös für den Rest.</summary>
    public System.Action<List<Row>, int>? OnStart;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(600, 420);
        var senk = new VBoxContainer();
        senk.AddThemeConstantOverride("separation", 8);
        AddChild(senk);

        _kopf.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        senk.AddChild(_kopf);

        var mitte = new HBoxContainer();
        mitte.AddThemeConstantOverride("separation", 10);
        mitte.SizeFlagsVertical = SizeFlags.ExpandFill;
        senk.AddChild(mitte);

        var links = new VBoxContainer();
        links.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        links.AddChild(new Label { Text = "Mitnehmen" });
        _mit.CustomMinimumSize = new Vector2(180, 340);
        _mit.SizeFlagsVertical = SizeFlags.ExpandFill;
        links.AddChild(_mit);
        mitte.AddChild(links);

        var knoepfe = new VBoxContainer();
        knoepfe.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _nehmen.Text = "Mitnehmen >>";
        _lassen.Text = "<< Zuruecklassen";
        knoepfe.AddChild(_nehmen);
        knoepfe.AddChild(_lassen);
        mitte.AddChild(knoepfe);

        var rechts = new VBoxContainer();
        rechts.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rechts.AddChild(new Label { Text = "Mitnehmbare Einheiten" });
        _frei.CustomMinimumSize = new Vector2(180, 340);
        _frei.SizeFlagsVertical = SizeFlags.ExpandFill;
        rechts.AddChild(_frei);
        mitte.AddChild(rechts);

        senk.AddChild(_geld);
        var unten = new HBoxContainer();
        unten.AddThemeConstantOverride("separation", 12);
        _weiter.Text = "Start der naechsten Mission";
        unten.AddChild(_konto);
        unten.AddChild(_weiter);
        senk.AddChild(unten);

        foreach (var b in new[] { _nehmen, _lassen, _weiter }) b.FocusMode = FocusModeEnum.None;
        _nehmen.Pressed += Nehmen;
        _lassen.Pressed += Lassen;
        _weiter.Pressed += Weiter;
        // Doppelklick tut dasselbe wie der Knopf daneben — das erwartet jeder,
        // und es kostet zwei Zeilen.
        _frei.ItemActivated += _ => Nehmen();
        _mit.ItemActivated += _ => Lassen();
    }

    /// <summary>Das Fenster füllen. <paramref name="plaetze"/> ist die Zahl der
    /// Stellplätze der NÄCHSTEN Mission — mehr geht nicht, weil mehr gar nicht
    /// aufgestellt werden könnte.</summary>
    public void Fill(List<Row> kandidaten, int plaetze, int kontostand)
    {
        _alle = kandidaten;
        _plaetze = plaetze;
        _kontostand = kontostand;
        _gewaehlt.Clear();
        Refresh();
    }

    private void Nehmen()
    {
        int k = Gewaehlt(_frei);
        if (k < 0) return;
        // ⚠ Die Zahl der Plaetze ist eine harte Grenze, kein Vorschlag.
        if (_gewaehlt.Count >= _plaetze) return;
        var offen = Offen();
        if (k >= offen.Count) return;
        _gewaehlt.Add(offen[k]);
        Refresh();
    }

    private void Lassen()
    {
        int k = Gewaehlt(_mit);
        if (k < 0 || k >= _gewaehlt.Count) return;
        _gewaehlt.RemoveAt(k);
        Refresh();
    }

    private void Weiter()
    {
        var mit = new List<Row>();
        foreach (int i in _gewaehlt) mit.Add(_alle[i]);
        OnStart?.Invoke(mit, Erloes());
    }

    private static int Gewaehlt(ItemList l)
    {
        var s = l.GetSelectedItems();
        return s.Length > 0 ? s[0] : -1;
    }

    private List<int> Offen()
    {
        var o = new List<int>();
        for (int i = 0; i < _alle.Count; i++)
            if (!_gewaehlt.Contains(i)) o.Add(i);
        return o;
    }

    /// <summary>Was Berkewitz für alles zahlt, was NICHT mitgeht — 30 % je
    /// Einheit, jede für sich abgerundet (das Original teilt je Einheit, nicht
    /// am Ende: <c>esi += 3·Wert / 10</c> in der Schleife).</summary>
    public int Erloes()
    {
        int summe = 0;
        foreach (int i in Offen())
            summe += Campaign.CampaignManager.SellPrice(_alle[i].Wert);
        return summe;
    }

    private void Refresh()
    {
        _kopf.Text = $"Sie koennen {_plaetze} von Ihren Einheiten " +
                     "zur naechsten Mission mitnehmen.";
        _frei.Clear();
        foreach (int i in Offen())
            _frei.AddItem($"{_alle[i].Name}  ({_alle[i].Energie}%)  ${_alle[i].Wert}");
        _mit.Clear();
        foreach (int i in _gewaehlt)
            _mit.AddItem($"{_alle[i].Name}  ({_alle[i].Energie}%)");

        int erloes = Erloes();
        _geld.Text = $"Berkewitz Corp. bezahlt Ihnen ${erloes} " +
                     "fuer Ihre restlichen Einheiten.";
        _konto.Text = $"Kontostand : ${_kontostand + erloes}";
        _nehmen.Disabled = _gewaehlt.Count >= _plaetze || Offen().Count == 0;
        _lassen.Disabled = _gewaehlt.Count == 0;
    }
}
