namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE DREI FEHLENDEN GEBÄUDEFENSTER</b> — Bahnhof, Flughafen und
/// Terranium-Mine. Gebaut am 21.08.2026.
///
/// <para><b>Warum sie fehlten und warum sie jetzt kommen:</b> der gemeinsame
/// Vorspann der Kampagne hat <b>drei Tore</b>, die nicht an einer Einheit
/// hängen, sondern daran, dass eines dieser Fenster <i>geöffnet wurde</i>
/// (<see cref="Campaign.CampaignHints"/>, Ereignisbyte C <c>0x539930</c>).
/// Solange es die Fenster nicht gab, waren drei von 34 Toren nicht auswertbar —
/// und die Hilfetexte #73, #76 und #78 im Spiel nicht erreichbar.</para>
///
/// <para><b>Woher der Inhalt stammt.</b> Aus dem Fensterverteiler des Originals
/// (<c>aekernel-tools/FENSTER_RE.md</c>) und den Zeichenketten der drei
/// Funktionen selbst — <b>das Spiel benennt seine Felder</b>:</para>
/// <list type="table">
/// <item><term>Art 2, <c>0x463FB0</c>, 2090 B</term><description>»Bahnhof«,
/// »Energie :«, »Im Lager :«, »Waffen«, »Fahrwerk«, »Spezial«, »Terranium«,
/// »Transportsystem«, Knöpfe »Aussenden« (6,235,20) und »Transportieren«
/// (6,235,160).</description></item>
/// <item><term>Art 5, <c>0x465050</c>, 9024 B</term><description>»Flughafen«,
/// »Energie :«, die drei Reiter »Lager« (4,40,20), »Hangar« (4,40,100),
/// »Produktion« (4,40,180), dazu »Status«, »Teile gelagert«, »Lagerplatz«,
/// »Erweiterungskosten«, »Kontostand« und die Knöpfe »Verbessern« (5,185,20),
/// »Reparieren« (5,185,240), »Angriff«, »Patrouille AN«/»AUS«,
/// »Handsteuerung«, »Bombe wechseln«, »Recycle«,
/// »Produzieren«.</description></item>
/// <item><term>Art 18, <c>0x474220</c>, 2808 B</term><description>»Terranium-
/// Mine«, »Energie :«, »Strom :«, »Rohstoffvorkommen:«, »komplett abgebaut«,
/// »Status : angehalten«, Knöpfe »Ausbau« (5,150,20), »Verbessern«
/// (5,150,135), »Start«/»Anhalten« (5,175,20), »Reparatur«
/// (5,175,135).</description></item>
/// </list>
///
/// <para>⚠ <b>EINE KLASSE für drei Fenster</b>, und das ist eine Entscheidung,
/// keine Nachlässigkeit: das Original hat drei getrennte Funktionen, aber sie
/// stellen dieselben Möbel auf — Titelleiste, Energiebalken, Werteliste,
/// Knopfreihen. Was sich unterscheidet, ist der Inhalt, und der steht hier in
/// drei getrennten Bauwegen. Drei fast gleiche Klassen wären dieselbe Sache mit
/// dreifacher Pflege.</para>
///
/// <para>⚠ <b>Was NICHT gebaut ist, steht am Knopf selbst.</b> Angeschlossen
/// sind nur die Knöpfe, deren Mechanik es bei uns gibt (die Mine kann starten
/// und anhalten, alle drei können reparieren). Die übrigen stehen da, weil das
/// Original sie zeigt — sie sind gesperrt und sagen im Hinweistext, warum. Ein
/// Knopf, der stumm nichts tut, ist von einem kaputten nicht zu
/// unterscheiden.</para>
///
/// <para>⚠ <b>Unsere Zutaten:</b> Farben, Rahmen und Schriftgrössen, wie bei
/// allen unseren Fenstern. Die Zahlen in Klammern oben sind die
/// Knopfkoordinaten des Originals und stehen hier als Beleg, nicht als
/// Pixelvorgabe — unsere Oberfläche skaliert.</para>
/// </summary>
public sealed partial class BuildingWindow : PanelContainer
{
    /// <summary>Welches der drei Fenster. ⭐ Die Zahlen sind die
    /// <b>Fensterarten des Originals</b> und zugleich die Werte, die das
    /// Ereignisbyte annimmt — darum keine eigene Aufzählung mit eigenen
    /// Nummern, sondern genau diese.</summary>
    public enum Art
    {
        Bahnhof = 2,
        Flughafen = 5,
        Mine = 18,
    }

    /// <summary>Was das Fenster über das Gebäude wissen muss. Ein einziger
    /// Satz, damit der Zeichner nichts aus dem Spielzustand holen muss.</summary>
    public sealed class Stand
    {
        public string Name = "";
        public int Hp, HpMax;
        public string Status = "";
        public int StockW, StockF, StockS, StockT;
        public int Kapazitaet, AusbauKosten, Geld;
        public int Vorkommen = -1, VorkommenStart;   // nur Mine
        public int Grad = -1;
        public bool Laeuft;                          // Mine: Zustand aktiv?
        public List<string> Hangar = new();          // nur Flughafen
        public int HangarPlaetze;
    }

    public System.Func<Stand?>? Daten;
    public System.Action? OnClose;
    public System.Action? OnStart;
    public System.Action? OnStop;
    public System.Action? OnRepair;

    private Art _art = Art.Bahnhof;
    private readonly Label _titel = new(), _energie = new(), _status = new();
    private readonly VBoxContainer _mitte = new();
    private readonly HBoxContainer _knoepfe = new();
    private readonly Button _zu = new();
    private readonly List<Label> _werte = new();

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(320, 200);
        var senk = new VBoxContainer();
        senk.AddThemeConstantOverride("separation", 5);
        AddChild(senk);

        var kopf = new HBoxContainer();
        _titel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        kopf.AddChild(_titel);
        _zu.Text = "X";
        _zu.FocusMode = FocusModeEnum.None;
        _zu.Pressed += () => { Hide(); OnClose?.Invoke(); };
        kopf.AddChild(_zu);
        senk.AddChild(kopf);

        // »Energie :« steht in allen drei Fenstern an derselben Stelle — es ist
        // die einzige Zeile, die alle drei Funktionen des Originals teilen.
        senk.AddChild(_energie);
        senk.AddChild(_status);

        _mitte.AddThemeConstantOverride("separation", 2);
        senk.AddChild(_mitte);

        _knoepfe.AddThemeConstantOverride("separation", 8);
        senk.AddChild(_knoepfe);
    }

    /// <summary>
    /// Aufmachen. ⭐ <b>Und hier fällt das Ereignis</b>, das die Kontexthilfe
    /// braucht: das Original setzt <c>byte[0x539930]</c> auf die Fensterart,
    /// sobald ein Fenster aufgeht (Schreiber C <c>0x441270</c>).
    ///
    /// <para>⚠ Es ist ein EREIGNIS, kein Zustand — die Tore setzen es nach dem
    /// Feuern auf 0 zurück. Wer es als »Fenster ist offen« führte, bekäme ein
    /// Tor, das nach dem Schliessen weiterfeuert.</para></summary>
    public void Open(Art art)
    {
        _art = art;
        Campaign.CampaignHints.Ereignis = (int)art;
        Refresh();
        Show();
    }

    private static Label Zeile(string text) => new()
    {
        Text = text,
        Modulate = new Color(0.82f, 0.84f, 0.80f),
    };

    private Button Knopf(string text, System.Action? tat, string? gesperrtWarum = null)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        b.CustomMinimumSize = new Vector2(104, 22);
        if (gesperrtWarum != null || tat == null)
        {
            b.Disabled = true;
            // ⚠ Der Grund gehört an den Knopf. Ein gesperrter Knopf ohne
            // Begründung sieht aus wie ein Fehler des Spiels.
            b.TooltipText = gesperrtWarum
                ?? "Im Original vorhanden, bei uns noch nicht angeschlossen.";
        }
        else b.Pressed += () => { tat(); Refresh(); };
        return b;
    }

    public void Refresh()
    {
        var s = Daten?.Invoke();
        if (s == null) { Hide(); return; }

        _titel.Text = _art switch
        {
            Art.Bahnhof => $"Bahnhof — {s.Name}",
            Art.Flughafen => $"Flughafen — {s.Name}",
            _ => $"Terranium-Mine — {s.Name}",
        };
        _energie.Text = $"Energie : {s.Hp} / {s.HpMax}";
        _status.Text = $"Status : {s.Status}";

        foreach (var k in _mitte.GetChildren()) ((Node)k).QueueFree();
        foreach (var k in _knoepfe.GetChildren()) ((Node)k).QueueFree();
        _werte.Clear();

        switch (_art)
        {
            case Art.Bahnhof:
                // »Im Lager :« mit den vier Beständen, in der Reihenfolge des
                // Originals: Waffen, Fahrwerk, Spezial, Terranium.
                _mitte.AddChild(Zeile("Im Lager :"));
                _mitte.AddChild(Zeile($"    Waffen        {s.StockW}"));
                _mitte.AddChild(Zeile($"    Fahrwerk      {s.StockF}"));
                _mitte.AddChild(Zeile($"    Spezial       {s.StockS}"));
                _mitte.AddChild(Zeile($"    Terranium     {s.StockT}"));
                _mitte.AddChild(Zeile("Transportsystem"));
                _knoepfe.AddChild(Knopf("Aussenden", null,
                    "Das Aussenden eines Zuges laeuft bei uns ueber die Strecke selbst, "
                    + "nicht ueber diesen Knopf."));
                _knoepfe.AddChild(Knopf("Transportieren", null,
                    "Die Verlegung ist gebaut (Gueterzug), haengt aber an der Einheit "
                    + "und nicht an diesem Fenster."));
                break;

            case Art.Flughafen:
                _mitte.AddChild(Zeile("Lager"));
                _mitte.AddChild(Zeile($"    Teile gelagert   W {s.StockW}  F {s.StockF}  S {s.StockS}"));
                _mitte.AddChild(Zeile($"    Lagerplatz       {s.Kapazitaet}"));
                _mitte.AddChild(Zeile($"    Erweiterungskosten {s.AusbauKosten}"));
                _mitte.AddChild(Zeile($"    Kontostand       {s.Geld}"));
                _mitte.AddChild(Zeile($"Hangar   {s.Hangar.Count} / {s.HangarPlaetze}"));
                // ⚠ Die Plätze einzeln, nicht als Zahl: das Original zeigt den
                // HANGAR als Liste, und ein leerer Platz ist eine eigene Aussage.
                for (int i = 0; i < s.HangarPlaetze; i++)
                    _mitte.AddChild(Zeile($"    {i + 1}. "
                        + (i < s.Hangar.Count ? s.Hangar[i] : "—")));
                _knoepfe.AddChild(Knopf("Reparieren", OnRepair));
                _knoepfe.AddChild(Knopf("Verbessern", null,
                    "Der Lagerausbau des Flughafens ist nicht gebaut."));
                _knoepfe.AddChild(Knopf("Produzieren", null,
                    "Flugzeugbau laeuft bei uns ueber das Baumenue der Basis."));
                break;

            default:
                _mitte.AddChild(Zeile($"Strom : {(s.Laeuft ? "an" : "aus")}"));
                // ⭐ »komplett abgebaut« ist der Wortlaut des Originals für ein
                // erschöpftes Vorkommen — nicht »0«.
                _mitte.AddChild(Zeile(s.Vorkommen <= 0
                    ? "Rohstoffvorkommen: komplett abgebaut"
                    : $"Rohstoffvorkommen: {s.Vorkommen}"
                      + (s.VorkommenStart > 0 ? $" von {s.VorkommenStart}" : "")));
                if (s.Grad >= 0) _mitte.AddChild(Zeile($"Guete : {s.Grad}"));
                _mitte.AddChild(Zeile($"Im Lager :   Terranium {s.StockT}"));
                _knoepfe.AddChild(s.Laeuft
                    ? Knopf("Anhalten", OnStop)
                    : Knopf("Start", OnStart));
                _knoepfe.AddChild(Knopf("Reparatur", OnRepair));
                _knoepfe.AddChild(Knopf("Ausbau", null,
                    "Der Ausbau der Mine ist nicht gebaut."));
                _knoepfe.AddChild(Knopf("Verbessern", null,
                    "Die Verbesserung der Mine ist nicht gebaut."));
                break;
        }
    }

    /// <summary>Eine Zeile für den Prüfstand. ⚠ Sie nennt die Fensterart als
    /// ZAHL, weil genau die im Ereignisbyte landet — ein Name wäre hier
    /// nicht nachprüfbar.</summary>
    public string WatchLine()
        => $"gebaeude-fenster: {(Visible ? "offen" : "zu")}, Art {(int)_art} "
         + $"({_art}), Titel \"{_titel.Text}\", {_knoepfe.GetChildCount()} Knoepfe, "
         + $"Ereignisbyte {Campaign.CampaignHints.Ereignis}";
}
