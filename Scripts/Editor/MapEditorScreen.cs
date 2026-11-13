namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using AkteEuropaReborn.Editor;

/// <summary>
/// Der Karteneditor als Schirm — dieselben zwei Laeufe, die es seit dem
/// 12.08.2026 als Schalter <c>--map-new=</c> und <c>--map-check=</c> gibt, nur
/// ohne Befehlszeile.
///
/// <para>⚠ <b>11.11.2026 (bug-739, M11): DER KOPF UNTEN IST VOM 12.08. UND IN
/// VIER PUNKTEN UEBERHOLT.</b> (1) Der Generator setzt seit dem 13./14.08. je
/// Spieler Basis, Fabrik und 6 Fahrzeuge, dazu neutrale Gebaeude und Vorkommen —
/// eine erzeugte Karte hat also Einheiten und Gebaeude. (2) Es gibt den Pinsel
/// (»Karte bearbeiten«, <see cref="MapEditOverlay"/>) und das Oeffnen einer
/// vorhandenen Karte (<see cref="MapOpen"/>; gelieferte nur als Kopie). (3) Die
/// »Startmarken 0x70..« sind eine ueberholte Lesung: sec4 ist die Objekttafel
/// (Bericht M6). (4) Das Kopfstueck »kein Spielen-Knopf« gilt nicht mehr, sobald
/// Paket E2 den Knopf »Im Gefecht spielen« bringt. Der alte Text bleibt als
/// Vorgeschichte stehen.</para>
///
/// <para><b>Was dieser Schirm KANN, ist genau das, was der Editor kann</b>, und
/// keine Zeile mehr. Nachgelesen in <see cref="MapGenerator"/>,
/// <see cref="MapFactory"/>, <see cref="TilePalette"/> und
/// <see cref="MapCheck"/>:</para>
/// <list type="bullet">
///   <item>Ein GELAENDE erzeugen: Wasserrand (3 Zellen), Strandring, Landesinneres
///     auf Hoehe 2, ein Berg links oben, ein See rechts unten — und zwei
///     Startmarken (Typ 0x70/0x71) in gegenueberliegenden Ecken. Die Form ist
///     UNSERE Setzung, ihre eine gemessene Bedingung steht im Kopf von
///     <see cref="MapGenerator"/>: unter Hoehe 2 gibt es keinen Bauplatz.</item>
///   <item>Die geschriebenen Dateien PRUEFEN: <see cref="MapCheck"/> liest
///     <c>map_*.json</c>, <c>map_*.entities.json</c> und <c>map_*.png</c>
///     zurueck und zaehlt Bauplaetze, Gelaende und Erreichbarkeit nach.</item>
/// </list>
///
/// <para><b>Was er NICHT kann</b> — und was dieser Schirm deshalb auch nicht
/// anbietet, statt es vorzugaukeln: keine Maus auf der Karte, kein Malen
/// einzelner Zellen, kein Setzen von Einheiten, Gebaeuden, Rohstoffen oder
/// Gleisen, kein Oeffnen einer erzeugten Karte zum Weiterbearbeiten, keine Wahl
/// der Spielerzahl (es sind immer zwei Marken). Eine erzeugte Karte ist reines
/// Gelaende: <b>0 Einheiten, 0 Gebaeude</b>.</para>
///
/// <para>⚠ <b>Darum gibt es hier keinen »Spielen«-Knopf, sondern »Ansehen«.</b>
/// Gemessen am 12.08.2026 mit <c>--skirmish=map_pruef01 --quit-after=3</c>:
/// <c>StartSkirmish</c> findet auf einer erzeugten Karte keinen besetzten Platz,
/// und die Abrechnung meldet im ersten Takt »Gemetzel entschieden: MISSION
/// GESCHEITERT«. Ein Gefecht darauf waere also verloren, bevor das Bild steht.
/// »Ansehen« zeigt die Karte im Kartenbetrachter — ohne Gefecht, ohne KI, ohne
/// Abrechnung (siehe <see cref="SkirmishSetup.ViewMap"/>).</para>
///
/// <para>Der Aufbau ist der des Gefechtsschirms und des Einstellungsschirms:
/// voller Deckel, Rollbereich, mittige Spalte — damit er auch in einem
/// 720p-Fenster nicht oben und unten abgeschnitten wird.</para>
/// </summary>
public partial class MapEditorScreen : Control
{
    /// <summary>Wohin »Ansehen« fuehrt. Setzt das Hauptmenue, weil nur es die
    /// Menuekulisse aus dem Speicher nehmen kann, bevor die naechste
    /// 20..33-Megapixel-Karte geladen wird.</summary>
    public Action<string>? OnView { get; init; }

    /// <summary>Wohin »Bearbeiten« fuehrt — derselbe Kartenbetrachter, aber mit
    /// eingeschaltetem <see cref="MapEditSession"/>. Setzt das Hauptmenue, aus
    /// demselben Grund wie <see cref="OnView"/>.</summary>
    public Action<string>? OnEdit { get; init; }

    /// <summary>bug-743 (11.11.2026): wohin »Im Gefecht spielen« fuehrt — setzt
    /// das Hauptmenue (MainMenu.PlayMade), aus demselben Grund wie
    /// <see cref="OnView"/>.</summary>
    /// <summary>C9 (12.11.2026): Karte, eigener Platz (0..7), Zahl der KI-Gegner, KI-Stufe.</summary>
    public Action<string, int, int, Rendering.MapEntityLayer.AiLevel>? OnPlay { get; init; }

    // ---- UNSERE Setzungen fuer die Eingabefelder ----------------------------
    //
    // Die Obergrenze ist GELESEN: CwmFile.Create wirft ueber 254 ("die groesste
    // gelieferte Karte ist 254x254"). Die Untergrenze ist UNSERE: bei 3 Zellen
    // Wasserrand, einem Strandring und Startmarken 6 Zellen vom Rand bleibt
    // unter 24 Zellen kaum noch Landesinneres uebrig.
    private const int MinSize = 24, MaxSize = 254;

    /// <summary>Kachelsatz 47 zuerst — ⚠ unsere Wahl, und nur, weil er der
    /// heute durchgemessene ist. Fehlt er, wird der erste gefundene
    /// genommen.</summary>
    private const int PreferredTileset = 47;

    private LineEdit _name = null!;

    // ---- 11.11.2026 bug-800/801: Groessen, Spieler, Kachelsatzbeschreibung ----
    /// <summary>Eine feste Kartengroesse zum Anklicken.</summary>
    private readonly record struct Groesse(string Name, int W, int H, string Spieler,
                                           int MinP, int MaxP, string Herkunft);

    /// <summary>
    /// DIE FESTEN GROESSEN — GEMESSEN an den gelieferten Karten
    /// (scratchpad/editorui/maps.py, 11.11.2026: Width/Height aus
    /// user://data/Maps/map_*.json, besetzte Plaetze = Eigner 0..7 der
    /// Einheiten in map_*.entities.json). Jede Groesse ist die einer
    /// Originalkarte; NAME und SPIELEREMPFEHLUNG sind UNSERE SETZUNG.
    /// <list type="bullet">
    ///   <item>NET-Karten: NET01 168x168 (4 Plaetze), NET02 230x230 (6),
    ///     NET03 150x150 (4), NET04 150x200 (8), NET05 254x254 (8), NET06
    ///     254x254 (8), NET07 254x125 (8), NET08 254x200 (4).</item>
    ///   <item>Kampagne: kleinste 30x80 (K4), K8 100x100, K18 120x120, K24
    ///     150x150, K19 168x168, K21 200x200, K29 230x230, K23/K32/K33 254x254.</item>
    /// </list>
    /// ⚠ Eine Zelle ist 40x20 Bildpunkte: 254x125 Zellen sind im Bild etwa
    /// quadratisch (10160x2500), 150x150 Zellen doppelt so breit wie hoch.
    /// </summary>
    private static readonly Groesse[] Groessen =
    {
        new("Klein",       100, 100, "2 Spieler",       2, 2, "K8 »The gate« 100×100 (Kampagne); keine NET-Karte ist kleiner als 150×150"),
        new("Mittel",      150, 150, "2–4 Spieler",     2, 4, "NET03 »Deep forest« 150×150, 4 Plätze; K24 »Forest Flames«"),
        new("Groß",       168, 168, "4 Spieler",       3, 4, "NET01 »Swamp fight« 168×168, 4 Plätze; K19 »Danube Delta«"),
        new("Sehr groß",  230, 230, "4–6 Spieler",     4, 6, "NET02 »Sands of doom« 230×230, 6 Plätze; K29 »Icefield«"),
        new("Riesig",      254, 254, "6–8 Spieler",     6, 8, "NET05 »River combat« und NET06 »Spring streams« 254×254, je 8 Plätze — die groesste Karte"),
        new("Breit",       254, 125, "8 Spieler",       6, 8, "NET07 »Desert death« 254×125, 8 Plätze (im Bild fast quadratisch)"),
        new("Hochformat",  150, 200, "8 Spieler",       6, 8, "NET04 »Islands« 150×200, 8 Plätze"),
    };

    /// <summary>Welche Groesse zur Spielerzahl vorgeschlagen wird (Index in
    /// <see cref="Groessen"/>). UNSERE SETZUNG: 2 → Mittel, 3–4 → Gross,
    /// 5–6 → Sehr gross, 7–8 → Riesig.</summary>
    private static int VorschlagFuer(int players) => players switch
    {
        <= 2 => 1, <= 4 => 2, <= 6 => 3, _ => 4,
    };

    private int _players = 2;
    private int _sizeAt = 1;
    private readonly List<Button> _playerButtons = new(), _sizeButtons = new();
    private CheckButton _ownSize = null!;
    private Label _sizeHint = null!;
    private TextureRect _preview = null!;
    private Label _tsName = null!, _tsText = null!, _tsUsed = null!, _tsMore = null!;
    private HBoxContainer _tsStoffe = null!;
    private SpinBox _w = null!, _h = null!, _pick = null!;
    private OptionButton _tileset = null!;
    private CheckBox _flat = null!;
    private LineEdit _from = null!;
    private Button _create = null!, _check = null!, _view = null!, _edit = null!, _play = null!;
    private Label? _grauHinweis;

    public override void _Process(double delta)
    {
        if (_grauHinweis != null) _grauHinweis.Visible = _check.Disabled;
    }

    /// <summary>Die Auswahl der VORHANDENEN Karten und der Knopf dazu. Siehe
    /// <see cref="OnOpen"/>.</summary>
    private OptionButton _existing = null!;
    private Button _open = null!;

    /// <summary>Die Kartennamen hinter den Zeilen von <see cref="_existing"/>,
    /// mit <c>map_</c> davor.</summary>
    private readonly List<string> _existingNames = new();
    private RichTextLabel _log = null!;
    private Label _status = null!;

    /// <summary>Die Kachelsatznummern hinter den Zeilen des Auswahlfelds.</summary>
    private readonly List<int> _tilesets = new();

    /// <summary>Die zuletzt erfolgreich geschriebene Karte, mit <c>map_</c>
    /// davor. Leer, solange nichts erzeugt wurde — daran haengen die Knoepfe
    /// »Pruefen« und »Ansehen«.</summary>
    private string _made = "";

    /// <summary>Was der Harnisch nach dem Erzeugen noch druecken soll —
    /// »pruefen«, »ansehen« oder nichts. Siehe das Ende von <c>_Ready</c>.</summary>
    private string _autoRun = "";

    /// <summary>Welche VORHANDENE Karte der Harnisch aufmachen soll
    /// (<c>--map-editor-open=</c>) — leer heisst: erzeugen statt oeffnen.</summary>
    private string _autoOpen = "";

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var backdrop = new ColorRect { Color = new Color(0.04f, 0.05f, 0.07f, 1f) };   // P3/C8 (12.11.2026): deckend, das Startmenue schien durch
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);

        var middle = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(middle);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(1320, 0) };
        box.AddThemeConstantOverride("separation", 10);
        middle.AddChild(box);

        var title = new Label { Text = "KARTENEDITOR", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        box.AddChild(title);
        box.AddChild(new Label
        {
            // Sagt in einer Zeile, was herauskommt — und was nicht. Beides ist
            // aus MapGenerator abgelesen und nicht geschaetzt.
            // bug-739 (11.11.2026) berichtigt: der Generator setzt seit dem
            // 13./14.08. je Spieler Basis + Fabrik + Truppe und neutrale Gebaeude.
            Text = "Erzeugt Gelände mit Startplätzen (je Spieler Basis, Fabrik, Truppe), "
                 + "neutralen Gebäuden und Vorkommen. Gleise und Gegenstände setzt der Pinsel.",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.7f, 0.75f, 0.8f),
        });
        box.AddChild(new HSeparator());

        // ---- links die Karte, rechts der Bericht ------------------------------
        //
        // Nebeneinander und nicht untereinander: untereinander stand der Knopf
        // »KARTE ERZEUGEN« im ersten Bild (1280x720) unterhalb des Fensterrands
        // und war nur ueber den Rollbalken zu erreichen. Dieselbe Aufteilung
        // benutzt der Gefechtsschirm.
        var lower = new HBoxContainer();
        lower.AddThemeConstantOverride("separation", 12);
        box.AddChild(lower);

        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 6);
        var formGroup = Group("Neue Karte", form);
        formGroup.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // bug-800 (11.11.2026): links Formular UND Knoepfe untereinander, damit
        // »KARTE ERZEUGEN« auch bei 1080 Zeilen ohne Rollen sichtbar bleibt.
        var leftCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        leftCol.AddThemeConstantOverride("separation", 8);
        lower.AddChild(leftCol);
        formGroup.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        leftCol.AddChild(formGroup);
        // bug-801 (11.11.2026): rechts die Landschaft (Vorschau + Beschreibung)
        // und darunter der Bericht.
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        right.AddThemeConstantOverride("separation", 10);
        lower.AddChild(right);

        _name = new LineEdit
        {
            Text = FreierName(),                           // A1 (12.11.2026)
            PlaceholderText = "eigen01",
            TooltipText = "Die Dateien heissen danach map_<name>.png, .json und .entities.json in "
                        + Core.Content.UserDir + "Maps.",
        };
        // ⭐ 12.11.2026 — P4/D1 (Fable-Pruefung): ZUFALL BEIM ERZEUGEN. Gleiche
        // Einstellungen ergaben immer dieselbe Karte (Samen fest 1). Jetzt eine
        // Zufallszahl, die man sieht (eine gute Karte laesst sich wiederfinden),
        // und »Neu wuerfeln«.
        _samen = new LineEdit
        {
            Text = NeuerSamen().ToString(), CustomMinimumSize = new Vector2(130, 0),
            TooltipText = "Dieselbe Zahl mit denselben Einstellungen ergibt dieselbe Karte — "
                        + "so findet man eine gelungene wieder.",
        };
        var wuerfeln = new Button { Text = "Neu würfeln", CustomMinimumSize = new Vector2(0, 34) };
        Kachelknopf(wuerfeln);
        wuerfeln.Pressed += () => _samen.Text = NeuerSamen().ToString();
        var nameZeile = new HBoxContainer();
        nameZeile.AddThemeConstantOverride("separation", 8);
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameZeile.AddChild(_name);
        nameZeile.AddChild(new Label { Text = "Zufallszahl" });
        nameZeile.AddChild(_samen);
        nameZeile.AddChild(wuerfeln);
        form.AddChild(Row("Name", nameZeile));

        // ⭐ 11.11.2026 — bug-800: SPIELERZAHL UND FESTE GROESSEN zum Anklicken.
        // Spielermeldung: »bei den größen sollte man feste werte wählen können
        // die halt so klassisch sind und nicht einzeln hochklicken können/müssen,
        // gerade 64x64 ist viel zu klein für multiplayer!« Die Groessen sind die
        // der gelieferten Karten (Tafel Groessen, gemessen); die Zuordnung zur
        // Spielerzahl ist UNSERE SETZUNG. Die Zaehler bleiben unter »Eigene
        // Groesse« erreichbar — der Lauf liest weiter _w/_h.
        var players = new HBoxContainer();
        players.AddThemeConstantOverride("separation", 4);
        var playerGroup = new ButtonGroup();
        for (int n = 2; n <= 8; n++)
        {
            int nn = n;
            var b = new Button
            {
                Text = n.ToString(), ToggleMode = true, ButtonGroup = playerGroup,
                CustomMinimumSize = new Vector2(54, 38),
                TooltipText = $"{n} Startplätze — je Platz Basis, Fabrik und Truppe.",
            };
            Kachelknopf(b);
            b.Pressed += () => SetPlayers(nn, true);
            _playerButtons.Add(b);
            players.AddChild(b);
        }
        form.AddChild(Row("Spieler", players));

        string sizeHint = $"{MinSize} bis {MaxSize} Zellen. Die {MaxSize} sind gelesen — so groß "
                        + "ist die groesste gelieferte Karte, darueber weist CwmFile.Create ab. Die "
                        + $"{MinSize} sind UNSERE Setzung: darunter bleibt nach Wasserrand (3), "
                        + "Strandring und den 6 Zellen bis zum Startplatz kaum Landesinneres übrig.";
        _w = new SpinBox { MinValue = MinSize, MaxValue = MaxSize, Value = 150, TooltipText = sizeHint };
        _h = new SpinBox { MinValue = MinSize, MaxValue = MaxSize, Value = 150, TooltipText = sizeHint };

        var sizeGrid = new GridContainer { Columns = 4 };
        sizeGrid.AddThemeConstantOverride("h_separation", 6);
        sizeGrid.AddThemeConstantOverride("v_separation", 6);
        var sizeGroup = new ButtonGroup();
        for (int i = 0; i < Groessen.Length; i++)
        {
            int ii = i;
            var g = Groessen[i];
            var b = new Button
            {
                Text = $"{g.Name}  {g.W}×{g.H}\n{g.Spieler}",
                ToggleMode = true, ButtonGroup = sizeGroup,
                CustomMinimumSize = new Vector2(180, 56),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = $"{g.W}×{g.H} Zellen. Vorbild: {g.Herkunft}",
            };
            Kachelknopf(b);
            b.Pressed += () => SetSize(ii);
            _sizeButtons.Add(b);
            sizeGrid.AddChild(b);
        }
        form.AddChild(new Label { Text = "Kartengröße (wie die Originalkarten)" });
        form.AddChild(sizeGrid);
        _sizeHint = Hint("");
        form.AddChild(_sizeHint);

        // »Eigene Groesse« — ausgeklappt nur auf Wunsch.
        _ownSize = new CheckButton { Text = "Eigene Größe eingeben", TooltipText = sizeHint };
        form.AddChild(_ownSize);
        var size = new HBoxContainer { Visible = false };
        size.AddThemeConstantOverride("separation", 8);
        _w.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _h.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        size.AddChild(_w);
        size.AddChild(new Label { Text = "x" });
        size.AddChild(_h);
        var sizeRow = Row($"Breite x Höhe ({MinSize}..{MaxSize})", size);
        sizeRow.Visible = false;
        form.AddChild(sizeRow);
        _ownSize.Toggled += on =>
        {
            sizeRow.Visible = size.Visible = on;
            foreach (var b in _sizeButtons) b.Disabled = on;
            UpdateSizeHint();
        };
        _w.ValueChanged += _ => UpdateSizeHint();
        _h.ValueChanged += _ => UpdateSizeHint();

        // ⭐ 11.11.2026 — bug-801: der Kachelsatz mit Namen, Beschreibung und
        // Vorschau (KachelsatzInfo). Die Nummer steht weiter dabei.
        _tileset = new OptionButton();
        form.AddChild(Row("Landschaft (Kachelsatz)", _tileset));

        // ⭐ 12.11.2026 — P4/D1: Wasser und neutrale Gebaeude als feste Auswahlen.
        // »mittel« und »normal« sind die gemessenen Werte des Generators; die
        // anderen Stufen sind UNSERE SETZUNG.
        var mengen = new HBoxContainer();
        mengen.AddThemeConstantOverride("separation", 4);
        void Stufen(string titel, (string Text, double Wert, string Tipp)[] stufen, int vorgabe, Action<double> setze)
        {
            if (mengen.GetChildCount() > 0) mengen.AddChild(new Control { CustomMinimumSize = new Vector2(18, 0) });
            mengen.AddChild(new Label { Text = titel });
            var g = new ButtonGroup();
            for (int i = 0; i < stufen.Length; i++)
            {
                var st = stufen[i];
                var b = new Button
                {
                    Text = st.Text, ToggleMode = true, ButtonGroup = g, ButtonPressed = i == vorgabe,
                    CustomMinimumSize = new Vector2(0, 34), TooltipText = st.Tipp,
                };
                Kachelknopf(b);
                b.Pressed += () => setze(st.Wert);
                mengen.AddChild(b);
            }
        }
        Stufen("Wasser", new[]
        {
            ("wenig", 0.08, "etwa 8 % der Karte Wasser — fast alles Land"),
            ("mittel", MapTerrain.WaterShareDefault, "etwa 19 % — gemessen an den Originalkarten"),
            ("viel", 0.32, "etwa 32 % — Seen und Küsten, Schiffe lohnen sich"),
        }, 1, v => _wasser = v);
        Stufen("Neutrale Gebäude", new[]
        {
            ("keine", 0.0, "keine herrenlosen Gebäude zum Einnehmen"),
            ("wenige", 0.5, "halb so viele wie in den Originalkarten"),
            ("normal", 1.0, "so dicht wie in den Originalkarten"),
            ("viele", 1.7, "deutlich mehr Gebäude zum Einnehmen"),
        }, 2, v => _neutral = v);
        // ⭐ 12.11.2026 — P6/D6: punktsymmetrische Karte (180°) fuer faire Mehrspielerkarten
        Stufen("Symmetrisch", new[]
        {
            ("nein", 0.0, "freie Landschaft"),
            ("ja", 1.0, "jede Hälfte ist das um 180° gedrehte Spiegelbild der anderen — Gelände, " +
                        "Höhen, Wald, Startplätze und Vorkommen; nur bei gerader Spielerzahl"),
        }, 0, v => _symmetrisch = v > 0);
        form.AddChild(Row("Mengen", mengen));
        _tileset.ItemSelected += _ => ShowTilesetInfo();

        var info = new VBoxContainer();
        info.AddThemeConstantOverride("separation", 6);
        _preview = new TextureRect
        {
            CustomMinimumSize = new Vector2(400, 225),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        var previewFrame = new PanelContainer();
        var pfStyle = new StyleBoxFlat { BgColor = new Color(0.02f, 0.02f, 0.03f), BorderColor = new Color(0.3f, 0.34f, 0.4f) };
        pfStyle.SetBorderWidthAll(1);
        previewFrame.AddThemeStyleboxOverride("panel", pfStyle);
        previewFrame.AddChild(_preview);
        info.AddChild(previewFrame);
        var infoText = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        infoText.AddThemeConstantOverride("separation", 4);
        _tsName = new Label { Modulate = new Color(0.95f, 0.85f, 0.55f) };
        _tsName.AddThemeFontSizeOverride("font_size", 20);
        _tsText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(380, 0) };
        _tsUsed = Hint("");
        _tsStoffe = new HBoxContainer();
        _tsStoffe.AddThemeConstantOverride("separation", 3);
        _tsMore = Hint("");
        infoText.AddChild(_tsName);
        infoText.AddChild(_tsText);
        infoText.AddChild(_tsUsed);
        infoText.AddChild(_tsStoffe);
        infoText.AddChild(_tsMore);
        info.AddChild(infoText);
        right.AddChild(Group("Landschaft", info));

        // ---- Fortgeschritten: was nur der Kenner braucht ---------------------
        var adv = new CheckButton { Text = "Fortgeschritten (Bodenblock, Kachelsatz-Ordner, Gegenprobe)" };
        form.AddChild(adv);
        var advBox = new VBoxContainer { Visible = false };
        advBox.AddThemeConstantOverride("separation", 6);
        form.AddChild(advBox);
        adv.Toggled += on => advBox.Visible = on;
        form = advBox;   // die drei folgenden Zeilen landen im Klappteil

        _pick = new SpinBox
        {
            MinValue = 0, MaxValue = 7, Value = 0,
            TooltipText = "Welcher der gefundenen Bodenläufe die Wiese wird. Welche es in diesem "
                        + "Kachelsatz gibt, steht im Bericht (»volle Bodenläufe ab 8«) — die "
                        + "Kachel selbst sagt es nicht.",
        };
        form.AddChild(Row("Bodenblock", _pick));

        _from = new LineEdit { PlaceholderText = @"nur nötig, wenn kein Kachelsatz gefunden wird" };
        var fromRow = new HBoxContainer();
        fromRow.AddThemeConstantOverride("separation", 8);
        _from.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        fromRow.AddChild(_from);
        var browse = new Button { Text = "Durchsuchen …" };
        fromRow.AddChild(browse);
        form.AddChild(Row("Ordner mit NN.CWP", fromRow));
        form.AddChild(Hint("Sonst wird gesucht: Entwicklungsbaum, user://data/DATA, eingelegte CD."));

        _flat = new CheckBox
        {
            Text = "flach (alles auf Höhe 0)",
            TooltipText = "Die Gegenprobe des Editors, nicht zum Spielen: eine flache Karte hat "
                        + "NULL Bauplätze, und die Prüfung muss danach genau das beanstanden.",
        };
        form.AddChild(Row("Gegenprobe", _flat));
        form.AddChild(Hint("⚠ Flach heißt NULL Bauplätze (can_build_here: vier Eckhoehen ≥ 2)."));

        // ---- rechts der Bericht ----------------------------------------------
        //
        // Wort fuer Wort dieselben Zeilen, die der kopflose Lauf auf die Konsole
        // schreibt. Sie stehen hier, weil sonst niemand sieht, welcher
        // Bodenblock genommen wurde, wie viele Bauplaetze herauskamen und woran
        // ein Lauf gescheitert ist.
        _log = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(380, 130),
            BbcodeEnabled = false,
            ScrollFollowing = true,
            SelectionEnabled = true,
            Text = "Noch nichts erzeugt.",
        };
        // D8 (12.11.2026): VORSCHAU der in der Liste gewaehlten Karte, ueber dem Bericht
        var berichtBox = new VBoxContainer();
        berichtBox.AddThemeConstantOverride("separation", 4);
        var vRow = new HBoxContainer();
        vRow.AddThemeConstantOverride("separation", 8);
        _vorschau = new TextureRect
        {
            CustomMinimumSize = new Vector2(150, 96),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        vRow.AddChild(_vorschau);
        _vorschauText = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Modulate = new Color(0.8f, 0.85f, 0.9f),
        };
        _vorschauText.AddThemeFontSizeOverride("font_size", 14);
        vRow.AddChild(_vorschauText);
        berichtBox.AddChild(vRow);
        // C10 (12.11.2026): erst eine kurze Zusammenfassung, die technischen Zeilen
        // (»can_build_here«, »Bodenblock«, »Faecher«) nur auf Wunsch.
        _technisch = new CheckButton { Text = "Technische Einzelheiten zeigen", FocusMode = FocusModeEnum.None };
        _technisch.Toggled += _ => BerichtZeigen();
        berichtBox.AddChild(_technisch);
        berichtBox.AddChild(_log);
        right.AddChild(Group("Gewählte Karte und Bericht", berichtBox));

        // ---- die Knoepfe -----------------------------------------------------
        _create = new Button { Text = "KARTE ERZEUGEN", CustomMinimumSize = new Vector2(0, 44) };
        _create.Pressed += OnCreate;
        leftCol.AddChild(_create);

        // ---- die vorhandene Karte -------------------------------------------
        var openRow = new HBoxContainer();
        openRow.AddThemeConstantOverride("separation", 8);
        leftCol.AddChild(openRow);
        _existing = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        openRow.AddChild(_existing);
        _open = new Button
        {
            Text = "VORHANDENE ÖFFNEN",
            CustomMinimumSize = new Vector2(0, 44),
            // bug-739 (11.11.2026): Text berichtigt — gelieferte Karten nur als Kopie.
            TooltipText = "Liest map_*.json und map_*.entities.json wieder zu einer Karte im "
                        + "Arbeitsspeicher zusammen. Beim Öffnen wird NICHTS geschrieben. Eine "
                        + "GELIEFERTE Karte (01..33, NET, DM) wird nie angefasst: bearbeitet "
                        + "wird eine Kopie <name>_kopie. Eine eigene Karte speichert F5 im Pinsel "
                        + "an Ort und Stelle.",
        };
        _open.Pressed += OnOpen;
        openRow.AddChild(_open);

        // ⭐ 12.11.2026 — D8 (Fable-Pruefung): KARTENVERWALTUNG. Eigene und empfangene
        // Karten umbenennen, kopieren, loeschen (mit Rueckfrage). Mitgelieferte bleiben
        // gesperrt — die Knoepfe sind dann grau und sagen warum.
        var verwRow = new HBoxContainer();
        verwRow.AddThemeConstantOverride("separation", 8);
        leftCol.AddChild(verwRow);
        _umbenennen = VerwKnopf("Umbenennen …", "Gibt der gewählten Karte einen neuen Namen.", verwRow);
        _umbenennen.Pressed += () => NamensFrage("Karte umbenennen", "Umbenennen", true);
        _kopieren = VerwKnopf("Kopie anlegen …", "Legt eine Kopie der gewählten Karte unter neuem Namen an.", verwRow);
        _kopieren.Pressed += () => NamensFrage("Kopie anlegen", "Kopieren", false);
        _titel = VerwKnopf("Titel …", "Der Anzeigename der Karte — er steht im Gefechtsaufbau unter dem Kartenbild.", verwRow);
        _titel.Pressed += TitelFrage;
        _loeschen = VerwKnopf("Löschen …", "Löscht die gewählte Karte (alle vier Dateien) — nach Rückfrage.", verwRow);
        _loeschen.Pressed += LoeschFrage;
        _existing.ItemSelected += _ => VerwStand();
        FillExisting();

        var after = new HBoxContainer();
        after.AddThemeConstantOverride("separation", 8);
        leftCol.AddChild(after);

        _check = new Button
        {
            Text = "Karte prüfen",
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Liest die geschriebenen Dateien zurück: Raster, Gelände, Bauplätze, "
                        + "Erreichbarkeit von den Startplätzen und das Bild.",
        };
        _check.Pressed += OnCheck;
        after.AddChild(_check);

        _view = new Button
        {
            Text = "Karte ansehen",
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            // bug-739 (11.11.2026): der alte Text (»traegt keine Einheiten und
            // keine Gebaeude«) war seit dem 13.08. falsch — der Generator setzt
            // je Spieler Basis, Fabrik und Truppe.
            TooltipText = "Öffnet die Karte im Kartenbetrachter — nur ansehen, kein Gefecht, "
                        + "keine KI. ESC fuehrt zurück.",
        };
        _view.Pressed += OnViewPressed;
        after.AddChild(_view);

        _edit = new Button
        {
            Text = "Karte bearbeiten",
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            // bug-802 (11.11.2026): Text auf das neue Bedienfeld umgestellt.
            TooltipText = "Öffnet die Karte mit dem Bedienfeld rechts: Werkzeug wählen (Gelände, "
                        + "Gebäude, Einheiten, Wald/Fels, Gleise, Wegnehmen), Auswahl und Besitzer "
                        + "anklicken, LINKS setzt, RECHTS nimmt weg, F5 speichert, Esc fuehrt zurück.",
        };
        _edit.Pressed += OnEditPressed;
        after.AddChild(_edit);

        // ⭐ 11.11.2026 — bug-743 (E2, M3): der Weg ins Gefecht. Bis heute gab es
        // nur »Ansehen«; die Gefechts- und die Netzliste sind fest (NET-Karten).
        _play = new Button
        {
            Text = "Im Gefecht spielen",
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Startet ein Gefecht auf dieser Karte: du spielst Platz 1, jeden anderen "
                        + "besetzten Platz die KI. Gespielt wird, was auf der Platte steht — "
                        + "ungespeicherte Pinselstriche erst mit F5 sichern.",
        };
        _play.Pressed += OnPlayPressed;
        after.AddChild(_play);

        // ⭐ 12.11.2026 — P3/C11 (Fable-Pruefung): die vier Knoepfe waren grau ohne
        // Hinweis, warum. Der Hinweis steht da, solange sie grau sind.
        _grauHinweis = new Label
        {
            Text = "Erst eine Karte erzeugen oder öffnen — dann gehen diese vier Knöpfe.",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.95f, 0.8f, 0.45f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        leftCol.AddChild(_grauHinweis);

        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.75f, 0.8f, 0.86f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        leftCol.AddChild(_status);

        box.AddChild(new HSeparator());
        var back = new Button { Text = "ZURÜCK", CustomMinimumSize = new Vector2(0, 40) };
        back.Pressed += Close;
        box.AddChild(back);

        // ---- der Ordnerwaehler ----------------------------------------------
        var dlg = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Size = new Vector2I(820, 560),
            Title = "Ordner mit den Kachelsaetzen wählen",
        };
        AddChild(dlg);
        browse.Pressed += () => dlg.PopupCentered();
        dlg.DirSelected += d => { _from.Text = d; FillTilesets(); };
        // Bei Eingabetaste UND beim Verlassen des Feldes neu suchen — nicht bei
        // jedem Tastendruck: eine Suche sind bis zu 64 x 12 File.Exists.
        _from.TextSubmitted += _ => FillTilesets();
        _from.FocusExited += FillTilesets;

        FillTilesets();
        SetPlayers(2, true);

        // ---- der Harnisch ----------------------------------------------------
        //
        // `--map-editor-run[=pruefen|=ansehen]` drueckt »KARTE ERZEUGEN« von
        // selbst, sobald der Schirm steht, und danach wahlweise noch einen der
        // beiden Folgeknoepfe. Gebraucht wird das, weil ein Prueflauf nicht
        // klicken kann und ein Menue, das nur RICHTIG AUSSIEHT, nichts wert ist
        // — genau die Begruendung, aus der MainMenu schon `--menu-click=` hat.
        // Zusammen ergibt das eine Zeile, die den ganzen Weg durchmisst:
        //   … --menu-click=Karteneditor --map-editor-run=pruefen --shot=<datei> --shot-delay=<bilder>
        // Der dritte Wert ist `bearbeiten`; zusammen mit `--map-edit-check` ist
        // das der Prueflauf des Bearbeitungsmodus, und der laeuft kopflos:
        //   … --headless --path . -- --menu-click=Karteneditor \
        //       --map-editor-run=bearbeiten --map-editor-name=pinsel01 \
        //       --map-edit-check --quit-after=8
        // ⚠ Braucht ein FENSTER (kein --headless), sonst gibt es nichts zu
        // photographieren.
        // ⚠ `--map-editor-name=` gehoert dazu und ist kein Beiwerk: das
        // Benutzerverzeichnis (%APPDATA%/Godot/app_userdata/…/data) ist EINS,
        // und mehrere Laeufe nebeneinander schreiben sonst auf dieselbe
        // map_eigen01.*. Wer seinen Lauf benennt, tritt keinem anderen auf die
        // Datei. Ohne den Schalter bleibt es bei der Vorgabe des Feldes.
        //
        // ⚠ `--map-editor-open=<karte>` drueckt statt »KARTE ERZEUGEN« den
        // Knopf »VORHANDENE OEFFNEN«. Ohne ihn liefe der Pruefstand des
        // Bearbeitungsmodus nur auf einer ERZEUGTEN Karte — und die trug bis
        // heute keinen einzigen Gegenstand. Ein Lauf, der »Gegenstand
        // weggenommen: 0 von 0« meldet, prueft nichts:
        //   … --headless --path . -- --menu-click=Karteneditor \
        //       --map-editor-open=map_01 --map-editor-run=bearbeiten \
        //       --map-edit-check --quit-after=20
        bool run = false;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--map-editor-name=")) _name.Text = a["--map-editor-name=".Length..];
            // bug-800 (11.11.2026): Spielerzahl, Groesse (Index der Tafel oder BxH)
            // und Kachelsatz fuer Bild- und Prueflaeufe.
            else if (a.StartsWith("--map-editor-spieler=") &&
                     int.TryParse(a["--map-editor-spieler=".Length..], out int sp)) SetPlayers(sp, true);
            else if (a.StartsWith("--map-editor-groesse="))
            {
                string v = a["--map-editor-groesse=".Length..];
                var wh = v.Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out int ww) && int.TryParse(wh[1], out int hh))
                { _ownSize.ButtonPressed = true; _w.Value = ww; _h.Value = hh; }
                else if (int.TryParse(v, out int gi)) SetSize(gi);
            }
            else if (a.StartsWith("--map-editor-kachelsatz=") &&
                     int.TryParse(a["--map-editor-kachelsatz=".Length..], out int kt))
            {
                int ki = _tilesets.IndexOf(kt);
                if (ki >= 0) { _tileset.Selected = ki; ShowTilesetInfo(); }
            }
            // 12.11.2026: feste Zufallszahl fuer Prueflaeufe — ohne sie wuerfelt jeder Lauf
            // eine andere Karte, und ein Fehler, der nur auf manchen Karten auftritt, laesst
            // sich nicht wiederholen.
            else if (a.StartsWith("--map-editor-samen=")) _samen.Text = a["--map-editor-samen=".Length..];
            else if (a.StartsWith("--map-editor-open=")) _autoOpen = a["--map-editor-open=".Length..];
            else if (a == "--map-editor-run" || a.StartsWith("--map-editor-run="))
            {
                _autoRun = a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : "";
                run = true;
            }
        }
        // ⭐ 12.11.2026 — P3/C2 (Fable-Pruefung): Esc/Zurueck im Bearbeitungsmodus
        // fuehrt hierher zurueck, mit derselben Karte geoeffnet (vorher: Startmenue,
        // und alle Knoepfe waren wieder grau).
        // C9 (12.11.2026): eine RUECKKEHR geht den Befehlszeilenschaltern vor — sonst
        // erzeugte ein Prueflauf mit --map-editor-run bei jeder Rueckkehr neu.
        if (ZurueckMit.Length > 0)
        {
            _autoOpen = ZurueckMit;
            run = true;
            _autoRun = ZurueckAktion;     // »spielenfrage« = Fenster »Im Gefecht spielen«, sonst nichts
        }
        ZurueckMit = "";
        ZurueckAktion = "";
        if (run && _autoOpen.Length > 0)
        {
            for (int i = 0; i < _existingNames.Count; i++)
                if (_existingNames[i] == _autoOpen || _existingNames[i] == "map_" + _autoOpen)
                    _existing.Selected = i;
            CallDeferred(nameof(OnOpen));
        }
        else if (run) CallDeferred(nameof(OnCreate));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Close() => QueueFree();

    // ---- die Kachelsaetze ---------------------------------------------------

    /// <summary>
    /// Das Auswahlfeld mit den Kachelsaetzen fuellen, die JETZT auffindbar sind
    /// — gefragt wird <see cref="MapGenerator.FindTileset"/>, also genau die
    /// Stelle, an der der Lauf selbst sucht.
    ///
    /// <para>⚠ Das ist keine Zierde. Der Einspieler legt <b>kein DATA-Verzeichnis</b>
    /// unter <c>user://data</c> an (nachgesehen am 12.08.2026: dort liegen
    /// Buildings, Effects, Maps, Sound, UI, Units — und sonst nichts), und die
    /// Engine oeffnet im Spiel nie eine <c>.CWP</c>. Auf einem Rechner ohne
    /// Entwicklungsbaum und ohne eingelegte CD findet der Editor also gar
    /// keinen Kachelsatz. Dann ist die Liste leer, der Knopf gesperrt und der
    /// Grund steht da — statt dass ein Lauf mit »Kachelsatz 47 nicht gefunden«
    /// endet.</para>
    /// </summary>
    private void FillTilesets()
    {
        string? hint = _from.Text.Trim().Length > 0 ? _from.Text.Trim() : null;
        _tilesets.Clear();
        _tileset.Clear();
        // 0..63: die gelieferten Saetze reichen von 01 bis 47, die Grenze ist
        // gross genug fuer einen nachgelegten und billig — je Nummer sind es
        // zwei File.Exists je Ordner.
        for (int t = 0; t <= 63; t++)
            if (MapGenerator.FindTileset(t, hint) != null) _tilesets.Add(t);

        // bug-801 (11.11.2026): mit Namen, Mehrspielersaetze zuerst.
        _tilesets.Sort((a, b) => KachelsatzInfo.Rang(a).CompareTo(KachelsatzInfo.Rang(b)));
        foreach (int t in _tilesets) _tileset.AddItem(KachelsatzInfo.Zeile(t));
        int at = _tilesets.IndexOf(PreferredTileset);
        _tileset.Selected = at >= 0 ? at : 0;
        ShowTilesetInfo();

        bool any = _tilesets.Count > 0;
        _tileset.Disabled = !any;
        _create.Disabled = !any;
        _status.Text = any
            ? $"{_tilesets.Count} Landschaften (Kachelsätze) gefunden."
            // bug-739 (11.11.2026): der Einspieler legt NN.CWP/NN.PAL seit
            // ContentBuilder.CopyTilesets sehr wohl ab — der alte Text sagte das Gegenteil.
            : "Kein Kachelsatz gefunden — weder im Entwicklungsbaum (Assets/Legacy/DATA) noch "
            + "unter user://data/DATA noch auf einer eingelegten Spiel-CD. Der Einspieler legt "
            + "die NN.CWP/NN.PAL unter user://data/DATA ab — fehlen sie, die Daten neu "
            + "einlesen, die CD einlegen oder oben den DATA-Ordner deiner Spielkopie wählen.";
    }

    // ---- 11.11.2026 bug-800: Spieler und Groesse --------------------------

    /// <summary>Spielerzahl setzen; <paramref name="suggest"/> schlaegt die
    /// passende Groesse vor (UNSERE SETZUNG, siehe <see cref="VorschlagFuer"/>).</summary>
    private void SetPlayers(int n, bool suggest)
    {
        _players = Math.Clamp(n, 2, MapGenerator.PlayersMax);
        for (int i = 0; i < _playerButtons.Count; i++) _playerButtons[i].SetPressedNoSignal(i + 2 == _players);
        if (suggest && !_ownSize.ButtonPressed) SetSize(VorschlagFuer(_players));
        else UpdateSizeHint();
    }

    private void SetSize(int i)
    {
        if (i < 0 || i >= Groessen.Length) return;
        _sizeAt = i;
        _w.Value = Groessen[i].W;
        _h.Value = Groessen[i].H;
        for (int k = 0; k < _sizeButtons.Count; k++) _sizeButtons[k].SetPressedNoSignal(k == i);
        UpdateSizeHint();
    }

    private void UpdateSizeHint()
    {
        if (_sizeHint == null) return;
        int w = (int)_w.Value, h = (int)_h.Value;
        if (_ownSize.ButtonPressed)
        {
            _sizeHint.Text = $"Eigene Größe {w}×{h} Zellen" +
                             (w * h < 150 * 150
                                 ? " — ⚠ kleiner als jede Mehrspielerkarte des Originals (kleinste: 150×150)."
                                 : ".");
            return;
        }
        var g = Groessen[_sizeAt];
        // B10 (12.11.2026): auch zu WENIGE Spieler fuer eine grosse Karte melden
        // (Groesse.MinP wurde nie gelesen).
        string warn = _players > g.MaxP
            ? $"  ⚠ Für {_players} Spieler eng — empfohlen: {Groessen[VorschlagFuer(_players)].Name}."
            : _players < g.MinP
                ? $"  ⚠ Für {_players} Spieler sehr weitläufig — empfohlen: {Groessen[VorschlagFuer(_players)].Name}."
                : "";
        _sizeHint.Text = $"{g.W}×{g.H} Zellen, {g.Spieler}. Vorbild: {g.Herkunft}.{warn}";
    }

    // ---- 11.11.2026 bug-801: der Kachelsatz beschrieben --------------------

    private void ShowTilesetInfo()
    {
        if (_tsName == null) return;
        foreach (var c in _tsStoffe.GetChildren()) c.QueueFree();
        if (_tileset.Selected < 0 || _tileset.Selected >= _tilesets.Count)
        {
            _tsName.Text = "keine Landschaft gefunden";
            _tsText.Text = _tsUsed.Text = _tsMore.Text = "";
            _preview.Texture = null;
            return;
        }
        int ts = _tilesets[_tileset.Selected];
        var s = KachelsatzInfo.Of(ts);
        _tsName.Text = $"{s.Name}  (Kachelsatz {ts:00})";
        _tsText.Text = s.Text;
        _tsUsed.Text = $"Im Original benutzt von: {s.Belege}. (Name und Beschreibung sind UNSERE "
                     + "Zusammenfassung — das Original kennt nur die Nummer.)";
        var m = KachelsatzInfo.Hole(ts, done =>
        {
            if (!IsInstanceValid(this)) return;
            if (_tileset.Selected >= 0 && _tileset.Selected < _tilesets.Count && _tilesets[_tileset.Selected] == done)
                ShowTilesetInfo();
        });
        if (m == null)
        {
            _preview.Texture = null;
            _tsMore.Text = "Vorschau wird aus der Originalkarte geschnitten …";
            return;
        }
        _preview.Texture = m.Bild != null ? ImageTexture.CreateFromImage(m.Bild) : null;
        if (m.Stoffe.Count > 0)
        {
            _tsStoffe.AddChild(new Label { Text = "Bodenstoffe:", Modulate = new Color(0.6f, 0.65f, 0.72f) });
            foreach (var (farbe, anteil, wasser) in m.Stoffe)
                _tsStoffe.AddChild(new ColorRect
                {
                    Color = farbe,
                    CustomMinimumSize = new Vector2(Math.Max(18f, (float)(anteil * 200)), 20),
                    TooltipText = (wasser ? "Wasser — " : "") +
                                  $"{100 * anteil:0} % der Kachelecken in den Originalkarten (gemessen, TileCorners)",
                });
        }
        string geb = m.FehlendeGebaeude.Count == 0 ? "alle 16 Gebäudearten"
            : $"{m.Gebaeudearten} von 16 Gebäudearten (es fehlen: {string.Join(", ", m.FehlendeGebaeude)})";
        _tsMore.Text = m.Fehler.Length > 0 ? $"Messung unvollstaendig: {m.Fehler}"
            : $"Bietet {geb} und {m.Gegenstaende} Objektbilder (Gebäudeteile, Bäume, Felsen …).";
    }

    // ---- erzeugen -----------------------------------------------------------

    /// <summary>
    /// Der Knopf. Er baut nur die Zeichenkette der Befehlszeile zusammen und
    /// reicht sie an <c>MainMenu.MapNew</c> weiter — es gibt keinen zweiten
    /// Schreibweg, den man getrennt pruefen muesste.
    ///
    /// <para><c>async</c>, damit »arbeitet …« auch wirklich im Bild steht: der
    /// Lauf haelt den Faden fest (er backt das ganze Kartenbild), und ohne das
    /// eine gewartete Einzelbild wuerde die Zeile erst NACH dem Lauf
    /// erscheinen.</para></summary>
    private LineEdit _samen = null!;
    private double _wasser = MapTerrain.WaterShareDefault, _neutral = 1.0;
    private bool _symmetrisch;
    /// <summary>P4: die Karte, die in diesem Schirm eben erzeugt (und nicht bearbeitet) wurde.</summary>
    private string _frischErzeugt = "";

    private static uint NeuerSamen() => (uint)GD.RandRange(1, 999999);

    /// <summary>A1: der Name, dessen Ueberschreiben der Spieler bestaetigt hat.</summary>
    private string _ueberschreibenOk = "";

    /// <summary>A1: erster freier Vorgabename eigen01, eigen02 …</summary>
    private static string FreierName()
    {
        string maps = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/Maps";
        for (int n = 1; n < 1000; n++)
        {
            string k = $"eigen{n:00}";
            if (!File.Exists($"{maps}/map_{k}.json") && !File.Exists($"{maps}/map_{k}.entities.json"))
                return k;
        }
        return "eigen01";
    }

    /// <summary>A1: »Ueberschreiben?« als gewohntes Bestaetigungsfenster.</summary>
    private void FrageUeberschreiben(string outName)
    {
        var d = new ConfirmationDialog
        {
            Title = "Karte überschreiben?",
            DialogText = $"Die Karte »{outName["map_".Length..]}« gibt es schon.\n" +
                         "Erzeugen ersetzt sie — alles, was darauf gemalt wurde, ist dann weg.",
            OkButtonText = "Überschreiben",
            CancelButtonText = "Abbrechen",
        };
        d.Confirmed += () => { _ueberschreibenOk = outName; d.QueueFree(); OnCreate(); };
        d.Canceled += () =>
        {
            d.QueueFree();
            _status.Text = "Nicht erzeugt — einen anderen Namen wählen oder das Überschreiben bestätigen.";
        };
        AddChild(d);
        d.PopupCentered();
    }

    private async void OnCreate()
    {
        string name = Sanitise(_name.Text);
        if (name.Length == 0) { _status.Text = "Die Karte braucht einen Namen."; return; }
        if (_tileset.Selected < 0 || _tileset.Selected >= _tilesets.Count)
        { _status.Text = "Kein Kachelsatz gewählt."; return; }
        int ts = _tilesets[_tileset.Selected];

        string outName = name.StartsWith("map_") ? name : "map_" + name;
        var lines = new List<string>();
        string maps = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/Maps";
        // ⭐ 12.11.2026 — A1 (Fable-Pruefung): ERZEUGEN UEBERSCHRIEB OHNE RUECKFRAGE,
        // auch gelieferte Karten (»NET07«, »01«). Gelieferte Namen werden jetzt
        // abgewiesen; eine vorhandene eigene Karte erst nach Bestaetigung (der
        // Prueflauf --map-editor-auto bestaetigt selbst, er arbeitet mit eigenem Namen).
        if (MapOpen.IsDelivered(outName))
        {
            _status.Text = $"»{name}« ist der Name einer mitgelieferten Karte — bitte einen anderen wählen.";
            return;
        }
        // P4: eine eben erst ERZEUGTE (nicht bearbeitete) Karte darf »Neu wuerfeln«
        // ohne Rueckfrage ersetzen — sonst fragte jeder zweite Wurf.
        if (File.Exists($"{maps}/{outName}.json") && _ueberschreibenOk != outName && _autoRun.Length == 0
            && _frischErzeugt != outName)
        {
            FrageUeberschreiben(outName);
            return;
        }
        _ueberschreibenOk = "";
        if (File.Exists($"{maps}/{outName}.json"))
            lines.Add($"⚠ {outName} gab es schon — die Dateien werden ueberschrieben (bestaetigt).");

        // ⭐ 13.11.2026 — F: der Auftrag geht als Parametersatz, nicht als Kommazeile — ein
        // Ordner mit Komma ist damit kein Sonderfall mehr (vorher abgewiesen).
        string from = _from.Text.Trim();
        var auftrag = new MainMenu.MapNewAuftrag
        {
            Name = name, W = (int)_w.Value, H = (int)_h.Value, Ts = ts,
            Players = _players,                                       // bug-800
            Flat = _flat.ButtonPressed,
            Pick = (int)_pick.Value,
            From = from.Length > 0 ? from : null,
            // P4/D1 (12.11.2026): Zufallszahl, Wasser, Neutrale
            Seed = uint.TryParse(_samen.Text.Trim(), out uint sm) && sm > 0 ? sm : 1,
            WaterShare = _wasser > 1.0 ? _wasser / 100.0 : _wasser,
            Sym = _symmetrisch,
            Neutrals = _neutral <= 0 ? 0 : -1,
            NeutralFaktor = _neutral <= 0 ? 1.0 : _neutral,
        };

        _create.Disabled = _check.Disabled = _view.Disabled = _edit.Disabled = _play.Disabled = true;
        _status.Text = $"arbeitet … ({(int)_w.Value}x{(int)_h.Value}, {_players} Spieler, " +
                       $"{KachelsatzInfo.Of(ts).Name} {ts:00}) — grosse Karten brauchen einige Sekunden";
        _log.Text = string.Join("\n", lines);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        int rc = MainMenu.MapNew(auftrag, s => lines.Add(s));
        _create.Disabled = false;
        string ls = KachelsatzInfo.Of(ts).Name;
        Bericht(lines, rc == 0
            ? $"Karte »{outName["map_".Length..]}« erzeugt: {(int)_w.Value}×{(int)_h.Value} Felder, {_players} Spieler, Landschaft »{ls}«."
              + Hinweise(lines)
            : $"Erzeugen fehlgeschlagen (Rückgabe {rc}).");

        if (rc == 0)
        {
            _made = outName;
            _frischErzeugt = outName;
            // D8 (12.11.2026): die neue Karte gleich in der Liste waehlen (Vorschau, Verwaltung)
            FillExisting();
            for (int i = 0; i < _existingNames.Count; i++)
                if (_existingNames[i] == outName) { _existing.Selected = i; VerwStand(); break; }
            _check.Disabled = _view.Disabled = _edit.Disabled = _play.Disabled = false;
            // ⭐ 12.11.2026 — P4/B8: Warnungen gehoeren in die Statuszeile, nicht nur in
            // den Bericht (»nur 0 von 8 Startplaetzen« stand frueher nur dort).
            var warn = lines.FindAll(l => l.Contains("⚠ nur") || l.Contains("Vorkommen: ⚠"));   // die zaehlen fuers Spiel: fehlende Startplaetze/Vorkommen
            _status.Modulate = warn.Count > 0 ? new Color(1f, 0.82f, 0.35f) : new Color(0.75f, 0.8f, 0.86f);
            string fairZeile = lines.Find(l => l.Contains("Fairness:")) is { } fz ? "  " + fz[(fz.IndexOf("Fairness:"))..] : "";
            _status.Text = warn.Count == 0
                ? $"{outName["map_".Length..]} geschrieben.{fairZeile}"
                : $"{outName["map_".Length..]} geschrieben — aber {warn.Count} Warnung(en): " +
                  warn[0].Trim().TrimStart('⚠', ' ') + (warn.Count > 1 ? " … (siehe Bericht)" : "");
            if (_autoRun == "pruefen") OnCheck();
            else if (_autoRun == "ansehen") OnViewPressed();
            else if (_autoRun == "bearbeiten") OnEditPressed();
            else if (_autoRun is "spielen" or "spielenfrage") OnPlayPressed();   // bug-743, C9: »spielenfrage« zeigt das Fenster (Bildlauf)
        }
        else
        {
            _made = "";
            _status.Text = $"Fehlgeschlagen (Rueckgabe {rc}) — siehe Bericht.";
        }
    }

    // ---- die vorhandene Karte oeffnen ---------------------------------------

    /// <summary>Die Karten, die schon geschrieben sind — beide Dateien muessen
    /// da sein, denn ohne die <c>.entities.json</c> laesst sich nichts
    /// zusammensetzen.</summary>
    private Button _umbenennen = null!, _kopieren = null!, _loeschen = null!, _titel = null!;
    private TextureRect? _vorschau;
    private Label? _vorschauText;

    private void TitelFrage()
    {
        string alt = Gewaehlt();
        if (alt.Length == 0) return;
        var feld = new LineEdit { Text = MapVerwaltung.Titel(alt), CustomMinimumSize = new Vector2(360, 0), MaxLength = 60 };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = $"Anzeigename für »{alt["map_".Length..]}« (steht im Gefechtsaufbau unter dem Bild):" });
        box.AddChild(feld);
        var d = new ConfirmationDialog { Title = "Titel der Karte", OkButtonText = "Übernehmen", CancelButtonText = "Abbrechen" };
        d.AddChild(box);
        d.Confirmed += () =>
        {
            string t = feld.Text;
            d.QueueFree();
            VerwErgebnis(MapVerwaltung.TitelSetzen(alt, t), $"Titel von »{alt["map_".Length..]}« ist jetzt »{t.Trim()}«.", alt, alt);
        };
        d.Canceled += () => d.QueueFree();
        AddChild(d);
        d.PopupCentered();
        feld.GrabFocus();
        feld.SelectAll();
    }

    private Button VerwKnopf(string text, string tip, Control row)
    {
        var b = new Button
        {
            Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(0, 32),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(b);
        return b;
    }

    /// <summary>Der Name hinter der gewaehlten Zeile, oder "".</summary>
    private string Gewaehlt()
    {
        int at = _existing.Selected;
        return at >= 0 && at < _existingNames.Count ? _existingNames[at] : "";
    }

    /// <summary>D8: die drei Knoepfe nur fuer Karten, die man verwalten darf.</summary>
    private void VerwStand()
    {
        if (_umbenennen == null) return;
        string n = Gewaehlt();
        string? warum = n.Length == 0 ? "keine Karte gewählt" : MapVerwaltung.Gesperrt(n);
        foreach (var b in new[] { _umbenennen, _kopieren, _loeschen, _titel }) b.Disabled = warum != null;
        if (_vorschau != null && _vorschauText != null)
        {
            _vorschau.Texture = n.Length > 0 ? UI.MapPreview.Of(n) : null;
            _vorschauText.Text = n.Length > 0 ? UI.MapPreview.Caption(n) : "";
        }
        // eine mitgelieferte Karte darf man KOPIEREN (das ist der Weg, sie zu aendern)
        if (n.Length > 0 && MapOpen.IsDelivered(n)) _kopieren.Disabled = false;
        _loeschen.TooltipText = warum != null
            ? $"Geht hier nicht: {warum}."
            : "Löscht die gewählte Karte (alle vier Dateien) — nach Rückfrage.";
    }

    private void NamensFrage(string titel, string ok, bool umbenennen)
    {
        string alt = Gewaehlt();
        if (alt.Length == 0) return;
        var feld = new LineEdit { Text = alt["map_".Length..] + (umbenennen ? "" : "_kopie"), CustomMinimumSize = new Vector2(320, 0) };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = $"Neuer Name für »{alt["map_".Length..]}« (Buchstaben, Ziffern, _ und -):" });
        box.AddChild(feld);
        var d = new ConfirmationDialog { Title = titel, OkButtonText = ok, CancelButtonText = "Abbrechen" };
        d.AddChild(box);
        d.Confirmed += () =>
        {
            string neu = Sanitise(feld.Text);
            d.QueueFree();
            if (neu.Length == 0) { _status.Text = "Kein gültiger Name — nichts geändert."; return; }
            string? f = umbenennen ? MapVerwaltung.Umbenennen(alt, neu) : MapVerwaltung.Kopieren(alt, neu);
            VerwErgebnis(f, umbenennen ? $"»{alt["map_".Length..]}« heißt jetzt »{neu}«."
                                       : $"Kopie »{neu}« angelegt.", alt, "map_" + neu.TrimStart());
        };
        d.Canceled += () => d.QueueFree();
        AddChild(d);
        d.PopupCentered();
        feld.GrabFocus();
        feld.SelectAll();
    }

    private void LoeschFrage()
    {
        string alt = Gewaehlt();
        if (alt.Length == 0) return;
        var d = new ConfirmationDialog
        {
            Title = "Karte löschen?",
            DialogText = $"Die Karte »{alt["map_".Length..]}« wird gelöscht (alle vier Dateien).\n" +
                         "Das laesst sich nicht rückgängig machen.",
            OkButtonText = "Löschen", CancelButtonText = "Abbrechen",
        };
        d.Confirmed += () =>
        {
            d.QueueFree();
            VerwErgebnis(MapVerwaltung.Loeschen(alt), $"»{alt["map_".Length..]}« gelöscht.", alt, "");
        };
        d.Canceled += () => d.QueueFree();
        AddChild(d);
        d.PopupCentered();
    }

    private void VerwErgebnis(string? fehler, string gut, string alt, string waehlen)
    {
        _status.Modulate = fehler == null ? new Color(0.6f, 0.95f, 0.6f) : new Color(1f, 0.85f, 0.35f);
        _status.Text = fehler ?? gut;
        if (fehler != null) return;
        // die gehaltene Karte gibt es so nicht mehr: Folgeknoepfe aus
        if (_made == alt && !MapVerwaltung.Gibt(alt))
        {
            _made = "";
            _check.Disabled = _view.Disabled = _edit.Disabled = _play.Disabled = true;
        }
        FillExisting();
        if (waehlen.Length > 0)
            for (int i = 0; i < _existingNames.Count; i++)
                if (_existingNames[i] == waehlen) { _existing.Selected = i; break; }
        VerwStand();
    }

    private void FillExisting()
    {
        _existing.Clear();
        _existingNames.Clear();
        string maps = ProjectSettings.GlobalizePath(Core.Content.UserRoot).TrimEnd('/', '\\') + "/Maps";
        if (!Directory.Exists(maps)) { _existing.AddItem("keine Karten gefunden"); _open.Disabled = true; return; }
        var found = new List<string>();
        foreach (string p in Directory.GetFiles(maps, "map_*.json"))
        {
            string f = Path.GetFileNameWithoutExtension(p);
            if (f.EndsWith(".entities")) continue;
            if (!File.Exists($"{maps}/{f}.entities.json")) continue;
            found.Add(f);
        }
        found.Sort(StringComparer.Ordinal);
        if (found.Count == 0) { _existing.AddItem("keine Karten gefunden"); _open.Disabled = true; return; }
        // ⭐ 12.11.2026 — P3/C3: eigene Karten ZUERST und vorgewaehlt, dann empfangene,
        // dann die mitgelieferten (vorher alphabetisch, Vorgabe Kampagnenkarte 01).
        var eigene = found.FindAll(f => !MapOpen.IsDelivered(f) && !f.StartsWith("map_netz_"));
        var netz = found.FindAll(f => f.StartsWith("map_netz_"));
        var geliefert = found.FindAll(f => MapOpen.IsDelivered(f));
        void Gruppe(string titel, List<string> liste)
        {
            if (liste.Count == 0) return;
            _existing.AddSeparator(titel);
            _existingNames.Add("");
            foreach (string f in liste) { _existing.AddItem(f["map_".Length..]); _existingNames.Add(f); }
        }
        Gruppe("Eigene Karten", eigene);
        Gruppe("Empfangene Karten (Netzspiel)", netz);
        Gruppe("Mitgelieferte Karten (werden als Kopie geöffnet)", geliefert);
        for (int i = 0; i < _existingNames.Count; i++)
            if (_existingNames[i].Length > 0) { _existing.Selected = i; break; }
        VerwStand();
    }

    /// <summary>P3/C2: die Karte, mit der der Bearbeitungsmodus per Esc/Zurueck
    /// verlassen wurde — der Editorschirm oeffnet sie gleich wieder.</summary>
    public static string ZurueckMit = "";

    /// <summary>C9: was nach der Rueckkehr gleich geschehen soll (»spielenfrage« = das
    /// Fenster »Im Gefecht spielen«).</summary>
    public static string ZurueckAktion = "";

    /// <summary>
    /// Der Knopf »VORHANDENE OEFFNEN«. Er geht denselben Weg wie
    /// »KARTE ERZEUGEN« — dieselbe Zeichenkette an dieselbe
    /// <c>MainMenu.MapNew</c>, nur mit <c>oeffne=</c> statt einer Groesse. Es
    /// gibt also auch hier keinen zweiten Weg, den man getrennt pruefen
    /// muesste.
    ///
    /// <para><s>⚠ Geoeffnet und gespeichert wird UNTER DEMSELBEN NAMEN: der Spieler
    /// will seine Karte weiterbearbeiten, nicht eine Kopie anlegen. Dass dabei
    /// nichts verlorengeht, ist gemessen — <c>--map-check=&lt;name&gt;,rundlauf</c>
    /// haelt die neu geschriebenen Dateien Feld fuer Feld gegen die alten.</s></para>
    /// <para>⭐ 11.11.2026 (bug-739): das »nichts geht verloren« galt nur bis zum
    /// 13.09. — seitdem traegt die Ausgabe Bruecken, Rampen, Zeichenlage und
    /// sec38, die der Rueckweg nicht liest. Eine GELIEFERTE Karte wird darum nur
    /// noch als Kopie geoeffnet, und das Oeffnen schreibt gar nichts mehr; eine
    /// eigene Karte speichert erst F5. Gegenschalter --editor-oeffnen-alt.</para>
    /// </summary>
    private async void OnOpen()
    {
        _frischErzeugt = "";   // P4: bearbeitet/geoeffnet = nicht mehr frisch
        _status.Modulate = new Color(0.75f, 0.8f, 0.86f);
        int at = _existing.Selected;
        if (at < 0 || at >= _existingNames.Count) { _status.Text = "Keine Karte gewählt."; return; }
        string outName = _existingNames[at];
        if (outName.Length == 0) { _status.Text = "Keine Karte gewählt."; return; }
        string stem = outName["map_".Length..];

        var lines = new List<string>();
        _create.Disabled = _open.Disabled = _check.Disabled = _view.Disabled = _edit.Disabled = _play.Disabled = true;
        _status.Text = $"öffnet {outName} …";
        _log.Text = "";
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        int rc = MainMenu.MapNew($"{stem},0,0,0,oeffne={outName}", s => lines.Add(s));
        Bericht(lines, rc == 0 ? $"»{stem}« geöffnet." : $"Öffnen fehlgeschlagen (Rückgabe {rc}).");
        _create.Disabled = _open.Disabled = false;

        if (rc == 0)
        {
            // bug-739 (11.11.2026): eine gelieferte Karte kommt als KOPIE zurueck
            // (map_<x>_kopie) — der wirklich gehaltene Name steht in der Sitzung.
            string held = MapEditSession.Name.Length > 0 ? MapEditSession.Name : outName;
            _made = held;
            _check.Disabled = _view.Disabled = _edit.Disabled = _play.Disabled = false;
            _name.Text = held["map_".Length..];
            _status.Text = held != outName
                ? $"{outName} ist eine gelieferte Karte — geöffnet als Kopie {held} (Brücken, "
                  + "Rampen, Waldtafel und Vorkommen fehlen der Kopie noch). F5 speichert die Kopie."
                : $"{outName} geöffnet, nichts geschrieben. F5 im Pinsel speichert an Ort und Stelle.";
            if (held != outName) FillExisting();
            if (_autoRun == "pruefen") OnCheck();
            else if (_autoRun == "ansehen") OnViewPressed();
            else if (_autoRun == "bearbeiten") OnEditPressed();
            else if (_autoRun is "spielen" or "spielenfrage") OnPlayPressed();   // bug-743, C9: »spielenfrage« zeigt das Fenster (Bildlauf)
        }
        else
        {
            _made = "";
            _status.Text = $"Öffnen fehlgeschlagen (Rueckgabe {rc}) — siehe Bericht.";
        }
    }

    /// <summary>Aus dem Bericht wird ein Dateiname; alles, was dort nichts zu
    /// suchen hat, faellt weg. Das Komma zuerst — daran trennt der Lauf seine
    /// Werte.</summary>
    private static string Sanitise(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.Trim())
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
        return sb.ToString();
    }

    // ---- pruefen und ansehen ------------------------------------------------

    private CheckButton? _technisch;
    private List<string> _berichtVoll = new();
    private string _berichtKurz = "";

    /// <summary>C10: Bericht ablegen — kurz für den Spieler, vollständig hinter dem Schalter.</summary>
    private void Bericht(List<string> voll, string kurz)
    {
        _berichtVoll = new List<string>(voll);
        _berichtKurz = kurz;
        BerichtZeigen();
    }

    private void BerichtZeigen()
    {
        if (_technisch?.ButtonPressed == true || _berichtKurz.Length == 0)
            _log.Text = string.Join("\n", _berichtVoll);
        else
            _log.Text = _berichtKurz + (_berichtVoll.Count > 0
                ? $"\n\n({_berichtVoll.Count} Zeilen technischer Bericht — »Technische Einzelheiten zeigen«)" : "");
    }

    /// <summary>C10: die Warnzeilen des Erzeugens, ohne Werkstattwörter.</summary>
    private static string Hinweise(List<string> lines)
    {
        var w = lines.FindAll(l => l.Contains("⚠ nur") || l.Contains("Vorkommen: ⚠") || l.Contains("Fairness:"));
        return w.Count == 0 ? "\nKeine Warnungen." : "\n• " + string.Join("\n• ", w.ConvertAll(Sauber));
    }

    /// <summary>Werkstattzeile kürzen: Vorsilben (»map-editor:«), Adressen und Klammerzusätze weg.</summary>
    private static string Sauber(string l)
    {
        string t = l.Trim();
        foreach (string vor in new[] { "map-editor:", "map-check:" }) if (t.StartsWith(vor)) t = t[vor.Length..].Trim();
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*@0x[0-9A-Fa-f]+", "");
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s*\[[^\]]*\]", "");
        t = t.Replace("can_build_here", "Bauprüfung").Replace("⚠", "").Replace("^ FEHLER:", "").Trim();
        return t.Length > 160 ? t[..157] + " …" : t;
    }

    private void OnCheck()
    {
        if (_made.Length == 0) return;
        var lines = new List<string>();
        int rc = MapCheck.Run(_made, s => lines.Add(s));
        var fehler = lines.FindAll(l => l.Contains("FEHLER") || l.Contains("DURCHGEFALLEN"));
        Bericht(lines, rc == 0
            ? $"Prüfung von »{_made["map_".Length..]}«: alles in Ordnung."
            : $"Prüfung von »{_made["map_".Length..]}«: {fehler.Count} Beanstandung(en)." +
              (fehler.Count > 0 ? "\n• " + string.Join("\n• ", fehler.ConvertAll(Sauber)) : ""));
        _status.Text = rc == 0
            ? $"{_made}: Prüfung ohne Beanstandung."
            : $"{_made}: die Prüfung hat etwas gefunden (Rueckgabe {rc}) — siehe Bericht.";
    }

    private void OnViewPressed()
    {
        if (_made.Length == 0 || OnView == null) return;
        OnView(_made);
    }

    /// <summary>
    /// ⭐ 12.11.2026 — C9 (Fable-Pruefung): »IM GEFECHT SPIELEN« MIT WAHL. Vorher nahm
    /// der Knopf immer Platz 1 gegen 7 KI auf der zuletzt im Gefechtsaufbau gewaehlten
    /// Stufe. Jetzt fragt ein Fenster nach dem eigenen Platz (nur die besetzten Plaetze
    /// der Karte), der Zahl der KI-Gegner und der Stufe; die uebrigen Plaetze bleiben
    /// unbeteiligt. Der Prueflauf (--map-editor-run=spielen) nimmt die Vorgabe ohne
    /// Fenster. Gegenschalter --editor-spielen-alt (alter Weg: Platz 1, 7 KI).
    /// </summary>
    private void OnPlayPressed()
    {
        if (_made.Length == 0 || OnPlay == null) return;
        var plaetze = Plaetze(_made);
        if (Array.IndexOf(Core.CommandLine.Args, "--editor-spielen-alt") >= 0 || plaetze.Count == 0)
        { OnPlay(_made, 0, 7, UI.SkirmishSetup.Level); return; }
        if (_autoRun == "spielen")
        { OnPlay(_made, plaetze[0], plaetze.Count - 1, UI.SkirmishSetup.Level); return; }

        var platz = new OptionButton();
        foreach (int p in plaetze) platz.AddItem($"Spieler {p + 1}", p);
        var gegner = new OptionButton();
        for (int n = 0; n < plaetze.Count; n++)
            gegner.AddItem(n == 0 ? "keine (Karte allein erkunden)" : n == 1 ? "1 Gegner" : $"{n} Gegner", n);
        gegner.Selected = plaetze.Count - 1;
        var stufe = new OptionButton();
        stufe.AddItem("Leicht", (int)Rendering.MapEntityLayer.AiLevel.Easy);
        stufe.AddItem("Normal", (int)Rendering.MapEntityLayer.AiLevel.Normal);
        stufe.AddItem("Schwer", (int)Rendering.MapEntityLayer.AiLevel.Hard);
        stufe.Selected = (int)UI.SkirmishSetup.Level;

        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        void Zeile(string t, Control c) { grid.AddChild(new Label { Text = t }); c.CustomMinimumSize = new Vector2(260, 0); grid.AddChild(c); }
        Zeile("Du spielst", platz);
        Zeile("Computergegner", gegner);
        Zeile("KI-Stufe", stufe);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(new Label
        {
            Text = $"»{_made["map_".Length..]}« hat {plaetze.Count} besetzte Plätze. Plätze ohne Mensch und " +
                   "ohne KI bleiben unbeteiligt stehen. Nach der Partie geht es zurück in den Editor.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0),
        });
        box.AddChild(grid);
        var d = new ConfirmationDialog { Title = "Im Gefecht spielen", OkButtonText = "Starten", CancelButtonText = "Abbrechen" };
        d.AddChild(box);
        d.Confirmed += () =>
        {
            int mensch = platz.GetSelectedId(), ki = gegner.GetSelectedId();
            var lv = (Rendering.MapEntityLayer.AiLevel)stufe.GetSelectedId();
            d.QueueFree();
            OnPlay(_made, mensch, ki, lv);
        };
        d.Canceled += () => d.QueueFree();
        AddChild(d);
        d.PopupCentered();
        GD.Print($"map-editor: Fenster »Im Gefecht spielen« offen — {_made}, {plaetze.Count} Plätze");
    }

    /// <summary>C9: die besetzten Plaetze einer Karte (Eigner 0..7 mit Gebaeude oder
    /// Einheit), aus ihrer .entities.json gelesen.</summary>
    private static List<int> Plaetze(string made)
    {
        var l = new List<int>();
        try
        {
            string pfad = $"{MapOpen.MapsDir}/{made}.entities.json";
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(pfad));
            var set = new SortedSet<int>();
            foreach (string feld in new[] { "buildings", "entities" })
                if (doc.RootElement.TryGetProperty(feld, out var arr))
                    foreach (var e in arr.EnumerateArray())
                        if (e.TryGetProperty("owner", out var o) && o.TryGetInt32(out int ow) && ow is >= 0 and <= 7)
                            set.Add(ow);
            l.AddRange(set);
        }
        catch (Exception ex) { GD.PrintErr("Plätze: " + ex.Message); }
        return l;
    }

    private void OnEditPressed()
    {
        _frischErzeugt = "";   // P4: bearbeitet/geoeffnet = nicht mehr frisch
        _status.Modulate = new Color(0.75f, 0.8f, 0.86f);
        if (_made.Length == 0 || OnEdit == null) return;
        OnEdit(_made);
    }

    // ---- Kleinteile, gebaut wie im Gefechtsschirm ---------------------------

    /// <summary>bug-800 (11.11.2026): ein Auswahlknopf mit sichtbarem Rahmen —
    /// im Spielthema sahen nicht gewaehlte Knoepfe wie blosser Text aus.
    /// UNSERE SETZUNG: Farben.</summary>
    private static void Kachelknopf(Button b)
    {
        StyleBoxFlat Box(Color bg, Color border, int w)
        {
            var s = new StyleBoxFlat { BgColor = bg, BorderColor = border,
                                       ContentMarginLeft = 6, ContentMarginRight = 6,
                                       ContentMarginTop = 4, ContentMarginBottom = 4 };
            s.SetBorderWidthAll(w);
            s.SetCornerRadiusAll(3);
            return s;
        }
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.12f, 0.14f, 0.17f), new Color(0.30f, 0.34f, 0.40f), 1));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.17f, 0.20f, 0.25f), new Color(0.55f, 0.62f, 0.72f), 1));
        b.AddThemeStyleboxOverride("pressed", Box(new Color(0.16f, 0.30f, 0.46f), new Color(0.95f, 0.80f, 0.40f), 2));
        b.AddThemeStyleboxOverride("hover_pressed", Box(new Color(0.18f, 0.33f, 0.50f), new Color(1f, 0.85f, 0.45f), 2));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.08f, 0.09f, 0.10f), new Color(0.18f, 0.2f, 0.22f), 1));
        b.AddThemeColorOverride("font_pressed_color", new Color(1f, 0.95f, 0.8f));
        b.AddThemeColorOverride("font_hover_pressed_color", new Color(1f, 0.95f, 0.8f));
    }

    private static Label Hint(string text) => new()
    {
        Text = text,
        Modulate = new Color(0.6f, 0.65f, 0.72f),
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    private static HBoxContainer Row(string label, Control field)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        h.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(200, 0) });
        field.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(field);
        return h;
    }

    /// <summary>Derselbe umrandete Abschnitt wie im Gefechtsaufbau
    /// (<c>MainMenu.Group</c>) — hier noch einmal, weil der dort privat und
    /// statisch ist und diese Datei ihn nur liest.</summary>
    private static PanelContainer Group(string title, Control content)
    {
        var p = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.10f, 0.12f),
            BorderColor = new Color(0.24f, 0.27f, 0.31f),
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 8, ContentMarginBottom = 10,
        };
        style.SetBorderWidthAll(1);
        p.AddThemeStyleboxOverride("panel", style);

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        p.AddChild(v);

        var h = new Label { Text = title, Modulate = new Color(0.62f, 0.72f, 0.85f) };
        h.AddThemeFontSizeOverride("font_size", 20);
        v.AddChild(h);
        content.SizeFlagsVertical = SizeFlags.ExpandFill;
        v.AddChild(content);
        return p;
    }
}
