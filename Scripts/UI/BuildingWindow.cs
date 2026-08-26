namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE GEBÄUDEFENSTER</b> — Bahnhof, Flughafen und Terranium-Mine (gebaut
/// am 21.08.2026), dazu der <b>Nachschubposten</b> (Art 31, 25.08.2026).
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
/// <para>⚠ <b>EINE KLASSE für vier Fenster</b>, und das ist eine Entscheidung,
/// keine Nachlässigkeit: das Original hat vier getrennte Funktionen, aber sie
/// stellen dieselben Möbel auf — Titelleiste, Werteliste, Knöpfe. Was sich
/// unterscheidet, ist der Inhalt, und der steht hier in vier getrennten
/// Bauwegen. Vier fast gleiche Klassen wären dieselbe Sache mit vierfacher
/// Pflege.</para>
///
/// <para>⚠ <b>Der Nachschubposten (Art 31) fällt aus der Reihe</b>, und das
/// steht hier, damit es nicht wie ein Versehen aussieht: er hat WEDER
/// »Energie :« NOCH eine Statuszeile (seine fünf Zeichenketten sind
/// »Angebot des Nachschubpostens«, »Kostet : $«, »Treibstoff-Heli.«,
/// »Kaufen«, »Munitions-Heli.«), er ordnet seinen Inhalt in ZWEI SPALTEN
/// statt in eine Liste, und er ist das einzige der vier, das mit GELD
/// bezahlt.</para>
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

        /// <summary>
        /// ⭐⭐ <b>»ANGEBOT DES NACHSCHUBPOSTENS«</b>, Fensterart <b>31</b>
        /// (<c>0x1F</c>), Zeichner C <c>0x47D340</c>, 726 Byte — gebaut am
        /// 25.08.2026.
        ///
        /// <para><b>Woher die Zuordnung kommt.</b> Der Klickverteiler
        /// <c>0x4379F0</c> führt Gebäudeart <b>14</b> über den Arm
        /// <c>0x437315</c> auf den Öffner <c>0x443090</c>, und der legt
        /// Fensterart <b>31</b> an (Revier 5, Anlegertafel). Der Zeichner
        /// <c>0x47D340</c> trägt die Zeichenketten »Angebot des
        /// Nachschubpostens«, »Kostet : $«, »Treibstoff-Heli.«, »Kaufen« und
        /// »Munitions-Heli.« — <b>das Fenster benennt sich selbst</b>.</para>
        ///
        /// <para>⭐ Und es gibt eine zweite, unabhängige Quelle für die 31:
        /// <c>0x451160</c> (Revier 6) fragt »ist ein Fenster der Art
        /// <c>0x1F</c> mit Unterart <c>n</c> offen?« — dieselbe Zahl, aus einem
        /// anderen Teil des Programms.</para>
        ///
        /// <para>⚠ <b>Es ist das EINZIGE dieser vier Fenster, das mit GELD
        /// bezahlt.</b> Der Zwei-Tasten-Dialog <c>0x44C2B9</c> verzweigt auf
        /// <c>0x44C2CF</c> (»Sprithelikopter kaufen«, 0x4F19F0) und
        /// <c>0x44C37C</c> (»Munitionshelikopter kaufen«, 0x4F1A3B) und prüft in
        /// BEIDEN Zweigen <c>cmp dword [ecx*4 + 0xA9C600], eax</c> — den
        /// Kontostand gegen einen Preis, und sonst nichts: kein Hangar, keine
        /// Teile, kein Besitzer.</para>
        /// </summary>
        Nachschubposten = 31,
    }

    /// <summary>
    /// Ein <b>Kaufangebot</b> des Nachschubpostens — eine Spalte des Fensters.
    ///
    /// <para>⭐ Der Zeichner <c>0x47D340</c> führt genau ZWEI davon
    /// (»Treibstoff-Heli.« und »Munitions-Heli.«), jede mit eigener Zeile
    /// »Kostet : $« und eigenem Knopf »Kaufen« — und der Klickblock
    /// <c>0x44C2B9</c> hat dazu passend genau zwei Tasten.</para>
    ///
    /// <para>⚠ <b>Der Kaufweg hängt am Angebot, nicht am Fenster.</b> Das
    /// Fenster weiß nicht, wie man einen Helikopter herstellt, und soll es
    /// nicht wissen: <c>Kaufen</c> ist derselbe Klickweg, den auch das
    /// Bedienfeld nimmt (<c>MapEntityLayer.BuildPanelPick</c>). Ein zweiter
    /// Kaufweg wäre ein zweiter Satz Wahrheiten über den Preis.</para>
    /// </summary>
    public sealed class Angebot
    {
        public string Name = "";
        public int Preis;
        public bool Bezahlbar;
        public System.Action? Kaufen;

        /// <summary>Woher der Preis stammt — die Adresse der Globalen im
        /// Original. ⚠ Sie steht im Hinweistext des Knopfes, damit ein
        /// Rückfallwert nicht wie ein gelesener aussieht.</summary>
        public string PreisQuelle = "";
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

        /// <summary>Was der Nachschubposten anbietet — je Angebot eine Spalte.
        /// Bei den anderen drei Fenstern leer.</summary>
        public List<Angebot> Angebote = new();
    }

    public System.Func<Stand?>? Daten;
    public System.Action? OnClose;
    public System.Action? OnStart;
    public System.Action? OnStop;
    public System.Action? OnRepair;

    private Art _art = Art.Bahnhof;

    /// <summary>Die Kennung, unter der das Fenster in der Verwaltung steht
    /// (bei Gebäudefenstern der Gebäudeplatz, <c>word[+0x0C]</c> im Original).
    /// ⚠ Sie wird gebraucht, damit das Schliesskreuz DASSELBE Fenster
    /// schliesst, das aufgemacht wurde.</summary>
    private int _kennung = -1;
    private readonly Label _titel = new(), _energie = new(), _status = new();

    /// <summary>Die ganze Godot-Moebelreihe. ⭐ Seit dem 25.08.2026 ein Feld
    /// und nicht mehr eine oertliche Veraenderliche: der Nachschubposten
    /// blendet sie AUS und zeigt stattdessen
    /// <see cref="SupplyShopView"/> — die Originalgrafiken aus
    /// WINDOWS.CWW.</summary>
    private readonly VBoxContainer _senk = new();

    /// <summary>⭐ Fensterart 31 mit den Moebeln des Originals. Null, solange
    /// <c>_Ready</c> nicht lief; unbrauchbar (und damit uebersprungen), solange
    /// der Spieler seine Inhalte nicht eingelesen hat.</summary>
    private SupplyShopView? _posten;

    private readonly VBoxContainer _mitte = new();
    private readonly HBoxContainer _knoepfe = new();
    private readonly Button _zu = new();
    private readonly List<Label> _werte = new();

    /// <summary>Wie viele bedienbare Knöpfe zuletzt aufgestellt wurden.
    /// ⚠ Der Nachschubposten hängt seine Knöpfe in die SPALTEN und nicht in
    /// die Knopfreihe; ohne diesen Zähler hätte <see cref="WatchLine"/> für
    /// ihn »0 Knoepfe« gemeldet und damit wie ein kaputtes Fenster
    /// ausgesehen.</summary>
    private int _knopfZahl;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(320, 200);
        var senk = _senk;
        senk.AddThemeConstantOverride("separation", 5);
        AddChild(senk);

        // ⭐ 25.08.2026 — DIE FENSTERMOEBEL DES ORIGINALS. Der Nachschubposten
        // wird nicht mehr aus Godot-Bausteinen gebaut, sondern aus den
        // 20x20-Kacheln von WINDOWS.CWW (Ausgeber
        // InterfaceExporter.WriteWindowChrome, Zeichnung SupplyShopView).
        _posten = new SupplyShopView { Visible = false };
        _posten.OnClose = Schliessen;
        _posten.OnChanged = Refresh;
        AddChild(_posten);

        var kopf = new HBoxContainer();
        _titel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        kopf.AddChild(_titel);
        _zu.Text = "X";
        _zu.FocusMode = FocusModeEnum.None;
        // ⚠⚠ 25.08.2026 — DAS KREUZ GING AN DER VERWALTUNG VORBEI. Hier stand
        // blosses `Hide()`. Die Fensterverwaltung führte das Fenster danach
        // WEITER als offen, und weil ein Klick auf dasselbe Gebäude dann nur
        // noch `NachVorn` ruft (MapViewer, Doppelöffnungssperre BM.2), ging es
        // NIE WIEDER AUF. Aufgefallen ist es erst am Nachschubposten, wo das
        // Fenster der einzige Weg zum Kauf ist — an Bahnhof, Flughafen und Mine
        // stand dieselbe Falle seit dem 22.08. still herum.
        _zu.Pressed += Schliessen;
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

    /// <summary>Zumachen — ueber die Fensterverwaltung, nicht per blossem
    /// <c>Hide()</c>. ⚠ Herausgezogen am 25.08.2026, weil das Schliesskreuz
    /// jetzt an ZWEI Stellen haengt: am Godot-Knopf der drei Gebaeudefenster
    /// und an Kachel 13 des Nachschubpostens. Zwei Wege, die dasselbe tun
    /// muessen, gehoeren in eine Methode.</summary>
    private void Schliessen()
    {
        WindowManager.Schliessen((int)_art, _kennung);
        Hide();
        OnClose?.Invoke();
    }

    /// <summary>
    /// Aufmachen.
    ///
    /// <para>⚠⚠ <b>25.08.2026 — DAS EREIGNIS FÄLLT NICHT MEHR HIER.</b> Hier
    /// stand <c>Campaign.CampaignHints.Ereignis = (int)art;</c>. Das war der
    /// richtige Wert an der falschen Stelle: das Original hat für
    /// <c>byte[0x539930]</c> im ganzen Programm <b>genau einen</b> Setzer
    /// (<c>0x4412C2</c> in <c>0x441270</c>, der Funktion, die ein Fenster in die
    /// Reihenfolgeliste einträgt — Vollerhebung über die Relokationstafel:
    /// 69 Verweise, 0 unklar). Bei uns ist das
    /// <see cref="WindowManager.Oeffnen"/>, und dort steht es jetzt. Ein
    /// Zeichner, der sein eigenes Ereignis setzt, ist ein zweiter Setzer — und
    /// zwei Setzer laufen frueher oder spaeter auseinander.</para>
    ///
    /// <para>⚠ Der Wert ÄNDERT SICH DADURCH NICHT: der Rufer (MapViewer) ruft
    /// unmittelbar vorher <c>Oeffnen(urArt, …)</c> mit genau der Zahl, die
    /// hier als <paramref name="art"/> ankommt — unsere Aufzählung trägt die
    /// Fensterarten des Originals als Werte.</para>
    ///
    /// <para>⚠ Es ist ein EREIGNIS, kein Zustand — die Tore setzen es nach dem
    /// Feuern auf 0 zurück. Wer es als »Fenster ist offen« führte, bekäme ein
    /// Tor, das nach dem Schliessen weiterfeuert.</para></summary>
    public void Open(Art art, int kennung = -1)
    {
        _art = art;
        _kennung = kennung;
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

        // ⭐⭐ 25.08.2026 — DER NACHSCHUBPOSTEN GEHT SEINEN EIGENEN WEG.
        // Gemeldet: »Du hast wie ein eigenes Kaufmenue gebaut, das hat nichts
        // mit dem Original zu tun.« Stimmt — hier standen Godot-Moebel. Jetzt
        // zeichnet SupplyShopView die Kacheln aus WINDOWS.CWW auf den
        // Koordinaten des Zeichners 0x47D340.
        // ⚠ Nur, WENN die Kacheln da sind. Ohne eingelesene Inhalte faellt das
        // Fenster auf die alte Darstellung zurueck, statt leer zu bleiben.
        bool original = _art == Art.Nachschubposten && _posten != null
                        && SupplyShopView.Usable;
        if (_posten != null) _posten.Visible = original;
        _senk.Visible = !original;
        if (original)
        {
            // Der Godot-Rahmen muss weg, sonst steht er hinter dem des
            // Originals.
            AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            CustomMinimumSize = new Vector2(
                SupplyShopView.WTiles * WindowChrome.Cell * SupplyShopView.Scale,
                SupplyShopView.HTiles * WindowChrome.Cell * SupplyShopView.Scale);
            Size = CustomMinimumSize;
            _posten!.Zeige(s);
            _titel.Text = "Angebot des Nachschubpostens";
            _knopfZahl = System.Math.Min(s.Angebote.Count, 2);
            return;
        }
        RemoveThemeStyleboxOverride("panel");
        CustomMinimumSize = new Vector2(320, 200);

        _titel.Text = _art switch
        {
            Art.Bahnhof => $"Bahnhof — {s.Name}",
            Art.Flughafen => $"Flughafen — {s.Name}",
            // ⭐ Der Wortlaut des Originals, byteweise aus dem Zeichner
            // 0x47D340. ⚠ Das Bildschirmfoto des Spielers zeigt ihn in
            // GROSSBUCHSTABEN; die Zeichenkette in der EXE steht gemischt da.
            // Ob das an der Schriftart des Originals liegt oder an einer
            // zweiten Zeichenkette, ist NICHT gemessen — darum steht hier die
            // Kette, wie sie in der EXE steht, und nicht eine umgeformte.
            Art.Nachschubposten => "Angebot des Nachschubpostens",
            _ => $"Terranium-Mine — {s.Name}",
        };
        // ⚠ »Energie :« und die Statuszeile teilen sich die drei GEBÄUDEfenster.
        // Der Nachschubposten hat beide NICHT: seine fünf Zeichenketten sind
        // »Angebot des Nachschubpostens«, »Kostet : $«, »Treibstoff-Heli.«,
        // »Kaufen«, »Munitions-Heli.« — keine Energie, kein Status. Eine Zeile
        // dazuzuerfinden wäre dasselbe wie eine wegzulassen.
        bool posten = _art == Art.Nachschubposten;
        _energie.Visible = !posten;
        _status.Visible = !posten;
        _energie.Text = $"Energie : {s.Hp} / {s.HpMax}";
        _status.Text = $"Status : {s.Status}";

        foreach (var k in _mitte.GetChildren()) { _mitte.RemoveChild((Node)k); ((Node)k).QueueFree(); }
        foreach (var k in _knoepfe.GetChildren()) { _knoepfe.RemoveChild((Node)k); ((Node)k).QueueFree(); }
        _werte.Clear();
        _knopfZahl = 0;

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

            case Art.Nachschubposten:
                // ⭐ ZWEI SPALTEN NEBENEINANDER, wie im Bild des Spielers und
                // wie im Zeichner: Name, darunter »Kostet : $n«, darunter der
                // Knopf »Kaufen«. Die Reihenfolge der Spalten ist die der
                // Vorlagentabelle (Satz 5 = Sprit-, Satz 6 = Munitionsheli),
                // nicht unsere Wahl.
                var spalten = new HBoxContainer();
                spalten.AddThemeConstantOverride("separation", 16);
                spalten.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                _mitte.AddChild(spalten);
                foreach (var ang in s.Angebote)
                {
                    var sp = new VBoxContainer();
                    sp.AddThemeConstantOverride("separation", 4);
                    sp.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    sp.AddChild(Zeile(ang.Name));
                    // ⭐ »Kostet : $« ist die Zeichenkette 0x5021C8, und die Zahl
                    // dahinter kommt aus einer Globalen — nicht aus dem
                    // Fenster. Welche, steht am Knopf.
                    sp.AddChild(Zeile($"Kostet : ${ang.Preis}"));
                    var k = new Button
                    {
                        Text = "KAUFEN",
                        FocusMode = FocusModeEnum.None,
                        Disabled = ang.Kaufen == null || !ang.Bezahlbar,
                    };
                    k.CustomMinimumSize = new Vector2(104, 22);
                    // ⚠ Der Hinweistext nennt die Herkunft des Preises. Ein
                    // Rückfallwert soll nicht wie ein gelesener aussehen.
                    k.TooltipText = ang.Kaufen == null
                        ? "Kein Kaufweg angeschlossen."
                        : ang.Bezahlbar
                            ? $"{ang.Name} fuer ${ang.Preis} kaufen  (Preis: {ang.PreisQuelle})"
                            : $"Sie besitzen nicht genuegend Geld! ({ang.Name} kostet "
                              + $"${ang.Preis}, Kontostand ${s.Geld})";
                    if (!k.Disabled)
                    {
                        var tat = ang.Kaufen!;
                        k.Pressed += () => { tat(); Refresh(); };
                    }
                    sp.AddChild(k);
                    spalten.AddChild(sp);
                    _knopfZahl++;
                }
                if (s.Angebote.Count == 0)
                    _mitte.AddChild(Zeile("— kein Angebot —"));
                // ⭐ Über die GANZE Breite darunter, wie im Bild. »Kontostand«
                // ist das Wort, das das Original auch am Geschäftszentrum
                // benutzt (0x502248).
                _mitte.AddChild(Zeile($"Kontostand : ${s.Geld}"));
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
    /// nicht nachprüfbar. Und sie nennt BEIDE Ereignisfelder, weil unsere
    /// Engine das eine Byte des Originals auf zwei aufteilt (siehe
    /// <see cref="WindowManager.Ereignismelder"/>): stimmten sie einmal nicht
    /// mehr überein, wäre das genau hier zu sehen.</summary>
    public string WatchLine()
        => $"gebaeude-fenster: {(Visible ? "offen" : "zu")}, Art {(int)_art} "
         + $"({_art}), Kennung {_kennung}, Titel \"{_titel.Text}\", "
         + $"{_knoepfe.GetChildCount() + _knopfZahl} Knoepfe, "
         + $"Ereignisbyte {Campaign.CampaignHints.Ereignis} "
         + $"(Verwaltung zuletzt {WindowManager.EreignisZuletzt}, "
         + $"{WindowManager.EreignisGesetzt} Setzungen)";
}
