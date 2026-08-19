namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// What the campaign was missing: the mission's own briefing, shown before the
/// map instead of dropping the player straight into it.
///
/// The text is the game's, out of BRIEFG.TXT (see
/// <see cref="Import.BriefingExporter"/>); the typeface is the game's, the same
/// bitmap font the HUD uses. The layout is ours — the original drew its briefing
/// on a fixed 320x200 screen with a picture behind it, and BRIEFG.DAT, the
/// picture file, has not been decoded.
///
/// It sits in front of everything as its own CanvasLayer, the way the end-of-
/// mission banner does, and hands control on with a callback rather than
/// changing the scene itself — so the one place that starts a mission stays the
/// one place that starts a mission.
/// </summary>
public partial class BriefingScreen : CanvasLayer
{
    private readonly List<string> _paragraphs;
    private readonly string _title;
    private readonly System.Action _go;

    /// <summary>Which mission this is, so the narration can be found: the bank
    /// holds it at 500 + this number. 0 means "no narration".</summary>
    private readonly int _mission;

    public BriefingScreen(string title, List<string> paragraphs, System.Action onContinue,
                          int mission = 0)
    {
        _title = title;
        _paragraphs = paragraphs;
        _go = onContinue;
        _mission = mission;
        Layer = 10;
    }

    public override void _Ready()
    {
        // the original's own two sounds for this screen: @0x44d8b9 right after
        // it prints "End of briefing starts", @0x44d976 after "End of briefing"
        Audio.GameSounds.Play(Audio.GameSounds.BriefingStart);

        // and the mission read aloud — sound 500 + mission, see
        // GameSounds.BriefingVoice for how that was pinned down
        if (_mission > 0 &&
            Audio.SoundBankPlayer.PlayVoice(Audio.GameSounds.BriefingVoice(_mission)))
            GD.Print($"Briefing: Sprachausgabe {Audio.GameSounds.BriefingVoice(_mission)}");

        // fully opaque: the menu behind it must not read through the text
        AddChild(new ColorRect
        {
            Color = new Color(0.03f, 0.04f, 0.05f),
            AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        });

        // The original's own briefing screen, if it has been imported: BRIEFG.DAT
        // is a 640x480 picture with a white plate at (296,79) 320x240 that the
        // text is written on. Drawn at a whole multiple so the pixels stay square,
        // with the text laid into the plate. Without it, the plain layout below.
        if (BuildBackdrop()) return;

        // Fill the screen with a margin rather than centring a fixed block: a
        // briefing runs from two paragraphs to a page and a half, and a centred
        // box of a guessed height pushes the long ones off the top and bottom.
        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, side is "left" or "right" ? 60 : 34);
        AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        margin.AddChild(box);

        // ⚠⚠ 19.08.2026 — DER FLIESSTEXT LAEUFT AUF FONT.CWD, NICHT AUF FONT2.
        //
        // Der Vorschauschirm LAEDT zwar FONT2.CWD (@0x45BE02) — daran hing die
        // falsche Annahme, sie sei »die Schrift dieses Schirms«. Er tauscht sie
        // aber nur fuer den ZIELKASTEN unten rechts ein und gleich wieder
        // zurueck; dreimal `rep movsd` mit `mov ecx,0x1478`, und diese drei
        // Stellen sind in BEIDEN GAME.EXE die einzigen im ganzen Programm:
        //
        //   0x45C15A  FONT.CWD sichern  (nach 0x8BE224)
        //   0x45C174  FONT2 einsetzen   (nach 0xB26440)
        //   ... OBJECTG.TXT zeichnen ...
        //   0x45C31C  FONT.CWD zurueck
        //
        // Der BRIEFINGtext wird ganz woanders gezeichnet (@0x486814), also
        // ausserhalb dieses Tauschs — mit FONT.CWD.
        //
        // Gemessen: derselbe Text mit FONT2 statt FONT ist noch einmal 8,9 %
        // breiter. FONT2 ist die NIEDRIGERE Schrift (Versalhoehe 7 px statt 9),
        // aber die BREITERE im Vorschub — 92 von 160 Zeichen haben Breite 7.
        // Genau das liess den Satz aufgeblasen wirken: zu niedrig UND zu breit
        // zugleich.
        //
        // ⚠ BriefingTech bleibt bewusst auf FONT2: das IST der Zielkasten.
        var font = LegacyFont(second: false);

        var head = new Label
        {
            Text = _title.ToUpperInvariant(),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Style(head, font, 2, head: true);
        box.AddChild(head);
        box.AddChild(new HSeparator());

        // the text scrolls, so even the longest briefing is fully readable
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        box.AddChild(scroll);

        var body = new Label
        {
            Text = string.Join("\n\n", _paragraphs),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Style(body, font, 2);
        scroll.AddChild(body);

        box.AddChild(new HSeparator());
        var go = new Button { Text = "Auftrag annehmen  [Enter]" };
        go.Pressed += Continue;
        box.AddChild(go);
        go.CallDeferred(Control.MethodName.GrabFocus);
    }

    // ---- the original's screen ---------------------------------------------

    /// <summary>Where the text goes on BRIEFG.DAT, measured off the picture:
    /// every row and column of the plate is palette index 144, and it comes out
    /// at exactly 320x240 — see <see cref="Import.BriefingExporter"/>.</summary>
    private const int BgW = 640, BgH = 480, PlateX = 296, PlateY = 79, PlateW = 320, PlateH = 240;

    // ---- the radar monitor ---------------------------------------------------

    private TextureRect? _radar;
    private readonly List<Texture2D> _radarFrames = new();
    private float _radarTime;

    /// <summary>Welches der zehn Bilder gerade steht und welches gemeint ist.
    /// Beide 0 heisst: Europa, und es bewegt sich nichts. Siehe
    /// <see cref="BuildMonitorButtons"/>.</summary>
    private int _radarBild, _radarZiel;

    /// <summary>Der MISSION-Knopf, damit ein Prueflauf ihn DRUECKEN kann statt
    /// das Feld dahinter zu setzen — siehe <c>--briefing-mission</c>.</summary>
    private Button? _btnMission;
    private Button? _btnStart;
    private bool _missionGetippt;

    /// <summary>How long one of the ten frames stays up. OURS — the original's
    /// pace is not read; a tenth of a second makes the cross-hair walk in over
    /// a second, which is about as long as the screen takes to settle.</summary>
    private const float RadarFrameSec = 0.10f;

    /// <summary>The mission's own radar picture on the monitor the backdrop
    /// already draws a frame around: MAP.DAT group (mission-1), ten frames, at
    /// x=17 y=38 — the position the loader @0x45c093 reads them to. See
    /// <see cref="Import.BriefingExporter.WriteRadar"/>.</summary>
    private void BuildRadar(Vector2 at, float scale)
    {
        if (_mission <= 0) return;
        for (int f = 0; f < Import.BriefingExporter.RadarFrames; f++)
        {
            string p = Core.Content.Path($"UI/radar/{_mission}/f{f}.png");
            Texture2D? t = ResourceLoader.Exists(p) ? ResourceLoader.Load<Texture2D>(p) : null;
            if (t == null && FileAccess.FileExists(p))
            {
                var im = Image.LoadFromFile(ProjectSettings.GlobalizePath(p));
                if (im != null) t = ImageTexture.CreateFromImage(im);
            }
            if (t == null) break;
            _radarFrames.Add(t);
        }
        if (_radarFrames.Count == 0) return;

        _radar = new TextureRect
        {
            Texture = _radarFrames[0],
            Position = at + new Vector2(Import.BriefingExporter.RadarX * scale,
                                        Import.BriefingExporter.RadarY * scale),
            Size = new Vector2(Import.BriefingExporter.RadarW * scale,
                               Import.BriefingExporter.RadarH * scale),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        AddChild(_radar);
        GD.Print($"Briefing: Radarbild Mission {_mission}, {_radarFrames.Count} Bilder");
    }

    /// <summary>
    /// <b>DIE ZWEI KNÖPFE UNTER DEM MONITOR</b> — »EUROPE« und »MISSION«.
    ///
    /// <para>Sie stehen im Hintergrundbild seit jeher, waren bei uns aber
    /// Zierrat: der Zoom lief beim Aufschlagen von selbst durch. Gemeldet mit
    /// zwei Bildern nebeneinander, »die Minimap ging erst auf, wenn man auf
    /// Mission geklickt hat«.</para>
    ///
    /// <para><b>Die Lage ist am Bild ausgemessen</b>, nicht geschätzt: die zwei
    /// dunklen Platten mit der hellen Schrift liegen im 640×480-Bild bei
    /// <c>x = 66..116</c> und <c>x = 125..165</c>, beide im Band
    /// <c>y = 260..276</c>. Die Flächen hier sind ein wenig grösser als die
    /// Platten — ein Knopf, den man knapp verfehlt, ist schlimmer als einer,
    /// der einen Punkt zu weit reicht.</para>
    ///
    /// <para>⚠ <b>Was NICHT gelesen ist:</b> ob das Original den Zoom Bild für
    /// Bild abspielt oder umschaltet, und wie schnell. Wir fahren ihn in
    /// <see cref="RadarFrameSec"/> je Bild in beide Richtungen — das benutzt
    /// alle zehn Bilder, die die Datei hergibt, statt acht davon
    /// wegzuwerfen.</para></summary>
    private void BuildMonitorButtons(Vector2 at, float scale)
    {
        if (_radarFrames.Count < 2) return;
        AddChild(MonitorKnopf(at, scale, 64, 258, 54, 20, "EUROPA",
                              () => _radarZiel = 0));
        _btnMission = MonitorKnopf(at, scale, 123, 258, 44, 20, "MISSION",
                                   () => _radarZiel = _radarFrames.Count - 1);
        AddChild(_btnMission);
    }

    /// <summary>Eine durchsichtige Fläche über einem Knopf, den das
    /// Hintergrundbild schon zeichnet. ⚠ Kein eigenes Aussehen: der Knopf ist
    /// gemalt, und ein zweiter darüber wäre doppelt.</summary>
    private static Button MonitorKnopf(Vector2 at, float scale, int x, int y, int w, int h,
                                       string name, System.Action tap)
    {
        var b = new Button
        {
            Position = at + new Vector2(x * scale, y * scale),
            Size = new Vector2(w * scale, h * scale),
            Flat = true,
            TooltipText = name,
            FocusMode = Control.FocusModeEnum.None,
        };
        b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        b.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        b.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        b.Pressed += () => tap();
        return b;
    }

    /// <summary>Builds the faithful screen. False if the picture is not there,
    /// in which case the caller falls back to the plain layout.</summary>
    private bool BuildBackdrop()
    {
        string path = Core.Content.Path("UI/briefing_bg.png");
        Texture2D? tex = null;
        if (ResourceLoader.Exists(path)) tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null && FileAccess.FileExists(path))
        {
            // imported content has no Godot import step — read the file directly
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
            if (img != null) tex = ImageTexture.CreateFromImage(img);
        }
        if (tex == null) return false;

        var view = GetViewport().GetVisibleRect().Size;
        // ⚠⚠ 18.08.2026 — HIER STAND EIN GANZZAHLIGER MASSSTAB, und der war bei
        // 1600×900 gleich EINS: das 640×480-Bild sass als kleiner Kasten mitten
        // auf schwarzem Grund. Gemeldet als »das Hintergrundbild fehlt immer
        // noch« — es fehlte nicht, es war nur ein Viertel so gross wie der
        // Schirm. Der Grund fuer die ganze Zahl war »damit die Bildpunkte
        // quadratisch bleiben«; das stimmt, wiegt aber nicht auf, dass der
        // Bildschirm zu drei Vierteln leer bleibt. Das Original fuellte ihn.
        float scale = Mathf.Max(1f, Mathf.Min(view.X / BgW, view.Y / BgH));
        var size = new Vector2(BgW * scale, BgH * scale);
        var at = ((view - size) * 0.5f).Floor();

        var pic = new TextureRect
        {
            Texture = tex,
            Position = at,
            Size = size,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        AddChild(pic);

        // ⚠ Die Markierungsflaeche zudecken, BEVOR etwas darauf steht — siehe
        // PlatteGrund. Das Original zeichnet dort seinen eigenen Grund; unser
        // Bild traegt an der Stelle nur den Platzhalter.
        AddChild(new ColorRect
        {
            Color = PlatteGrund,
            Position = at + new Vector2(PlateX * scale, PlateY * scale),
            Size = new Vector2(PlateW * scale, PlateH * scale),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        // ⚠ Die zwei kleinen Markierungsflaechen unten ebenso zudecken.
        //
        // ⚠⚠ HIER STAND »WETTERSYMBOL«, UND DAS WAR FALSCH. Ich hatte im
        // Dateischwanz Tafeln mit CLOUDED, OVERCAST, SKY, DANGER, HIGH/LOW
        // TEMPERATURE gefunden und daraus geschlossen, das runde Zeichen in den
        // zwei Nischen sei ein Wirbelsturm. Der Spieler hat berichtigt: es ist
        // das EMBLEM von Akte Europa, und es ist ANIMIERT — ein Schimmer laeuft
        // von links nach rechts und wieder zurueck darueber. Dasselbe Emblem
        // steht als Wasserzeichen hinter dem Text (im Vergleichsbild sichtbar,
        // wenn man es aufhellt): konzentrische Ringe, bronzefarben.
        //
        // Die Tafeln im Schwanz gibt es trotzdem — der Schwanz haelt also
        // MEHRERES. Was fehlt, ist seine Gliederung: bei Breite 106 wird eine
        // Bahn lesbar, bei 53/55/202/208/318 zerfaellt alles. Es ist eine Bank
        // mit Kopfdaten, und die gehoert an der BLITSTELLE gelesen.
        //
        // Bis dahin steht dort dieselbe dunkle Nische wie im Original — ein
        // WEISSER Kasten sieht aus wie ein Fehler, eine leere Nische wie eine
        // Nische.
        foreach (int wx in new[] { WeatherLX, WeatherRX })
            AddChild(new ColorRect
            {
                Color = PlatteGrund,
                Position = at + new Vector2(wx * scale, WeatherY * scale),
                Size = new Vector2(WeatherW * scale, WeatherH * scale),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

        BuildRadar(at, scale);
        BuildMonitorButtons(at, scale);

        // ⚠ Zwei Anschlussstellen, jede in ihrer EIGENEN Teildatei, damit an
        // ihnen unabhaengig voneinander gearbeitet werden kann:
        //   BriefingEmblem.cs  — das Emblem (Wasserzeichen + die zwei Nischen)
        //   BriefingTech.cs    — das Feld »neue technologien«
        // Beide sind heute leer und zeichnen nichts; der Schirm sieht damit aus
        // wie vorher.
        BuildEmblems(at, scale);
        BuildTechPanel(at, scale);

        // ⚠⚠ 19.08.2026 — DER FLIESSTEXT LAEUFT AUF FONT.CWD, NICHT AUF FONT2.
        //
        // Der Vorschauschirm LAEDT zwar FONT2.CWD (@0x45BE02) — daran hing die
        // falsche Annahme, sie sei »die Schrift dieses Schirms«. Er tauscht sie
        // aber nur fuer den ZIELKASTEN unten rechts ein und gleich wieder
        // zurueck; dreimal `rep movsd` mit `mov ecx,0x1478`, und diese drei
        // Stellen sind in BEIDEN GAME.EXE die einzigen im ganzen Programm:
        //
        //   0x45C15A  FONT.CWD sichern  (nach 0x8BE224)
        //   0x45C174  FONT2 einsetzen   (nach 0xB26440)
        //   ... OBJECTG.TXT zeichnen ...
        //   0x45C31C  FONT.CWD zurueck
        //
        // Der BRIEFINGtext wird ganz woanders gezeichnet (@0x486814), also
        // ausserhalb dieses Tauschs — mit FONT.CWD.
        //
        // Gemessen: derselbe Text mit FONT2 statt FONT ist noch einmal 8,9 %
        // breiter. FONT2 ist die NIEDRIGERE Schrift (Versalhoehe 7 px statt 9),
        // aber die BREITERE im Vorschub — 92 von 160 Zeichen haben Breite 7.
        // Genau das liess den Satz aufgeblasen wirken: zu niedrig UND zu breit
        // zugleich.
        //
        // ⚠ BriefingTech bleibt bewusst auf FONT2: das IST der Zielkasten.
        var font = LegacyFont(second: false);

        // ⚠⚠ 18.08.2026 — DIE SCHRIFT SPRANG IN GANZEN VIELFACHEN. Gemeldet:
        // »unser Text ist noch etwas zu gross im Kampagnen-Missions-Preview«.
        //
        // `LegacyFont` setzt `FixedSizeScaleMode.IntegerOnly`, damit die 13 px
        // Bildpunkte scharf bleiben. Solange das Bild selbst in ganzen
        // Vielfachen sass, passte das zusammen. Seit es den Schirm FUELLT, steht
        // es bei 1600×900 auf 1,875 — und die Schrift sprang auf das naechste
        // ganze Vielfache, also 2×. Das sind **6,7 % zu gross**, und genau das
        // sieht man: der Text laeuft frueher um als im Original.
        //
        // Hier, und NUR hier, darf sie bruchteilig mitgehen. Die Schrift ist
        // eine Kopie (LegacyFont dupliziert), das Hauptmenue bleibt also
        // unberuehrt und behaelt seine scharfen ganzen Stufen.
        if (font is FontFile ff) ff.FixedSizeScaleMode = TextServer.FixedSizeScaleMode.Enabled;

        // ⚠⚠ 19.08.2026 — DER EIGENE TITELSTREIFEN IST WEG, und er war die
        // Ursache des gemeldeten Fehlers.
        //
        // Gemeldet: »im Kampagnen-Preview ist der Titel 01 - Airborne Ambush
        // überdeckt vom Text«. Das habe ich mir selbst eingebrockt: der Streifen
        // sass bei `PlateY − 26` = 53, und als der Textkasten kurz zuvor auf die
        // gelesene Stelle y = 50 wanderte, lagen beide uebereinander.
        //
        // Die Kur ist aber nicht, den Streifen zu verschieben — es gibt ihn im
        // Original gar nicht. Der Titel ist dort schlicht die ERSTE ZEILE
        // desselben Textes: linksbuendig bei x = 320, y = 50, in gemischter
        // Schreibung, in derselben Schrift und Farbe wie der Rest. Er laeuft
        // durch dieselbe Zeichenschleife (@0x486759) wie jede andere Zeile.
        //
        // Er steht deshalb jetzt als erste Zeile im Fliesstext (siehe unten) —
        // damit verschwindet auch die Grossschreibung, die von uns war.

        // ⚠⚠ 19.08.2026 — DER TEXTKASTEN IST NICHT DIE WEISSE PLATTE.
        //
        // Hier stand `PlateX+8 / PlateY+6`, Breite `PlateW-16` = 304 — abgeleitet
        // aus dem Rechteck 296,79,320,240. Das ist aber die WASSERZEICHENflaeche:
        // genau dorthin laedt der Lader @0x45C120 das Bild aus SYMBOL.DAT
        // (260 Zeilen zu 320 Byte auf (296,79)). Der Text steht darueber hinaus.
        //
        // Der Fliesstext des Originals, gelesen in der Zeichenschleife
        // @0x486759..0x48684C (F @0x484E29, gleiche Form):
        //
        //   0x486828  add esi, 0x140       ; linker Rand x = 320
        //   0x486835  add eax, 0x32        ; erste Zeile y = 50
        //   0x48682F/32  3*Zeile -> 15*Zeile ; Zeilenabstand 15 px
        //   0x4867CF  cmp eax, 0x118       ; SPALTENBREITE = 280 px
        //   0x4867B2  cmp ax, 0x12c        ; ein Wort > 300 px bricht ab
        //   0x486807  cmp cl, 0x5e         ; '^' = harter Zeilenwechsel
        //
        // Und das dunkle Feld, an BRIEFG.DAT gemessen (Palettenindex 47),
        // laeuft x 277..634, y 3..389 — der Text sitzt mit 43 px linkem und
        // 34 px rechtem Rand mittig darin und hat 339 px Hoehe.
        //
        // ⚠ Ueber alle 33 Briefings gemessen: der laengste Text hat 21 Zeilen
        // und endet bei y 363, das Feld bei 389. **Das Original rollt nie** —
        // es ist so gesetzt, dass alles hineinpasst. Unsere 228 px schnitten ab,
        // wo das Original 339 hat.
        const int TxtX = 320, TxtY = 50, TxtW = 280, TxtH = 339;
        var scroll = new ScrollContainer
        {
            Position = at + new Vector2(TxtX * scale, TxtY * scale),
            Size = new Vector2(TxtW * scale, TxtH * scale),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);

        // Der Titel als ERSTE ZEILE des Textes — siehe oben, wo der eigene
        // Streifen weggefallen ist. Eine Leerzeile dahinter, damit er sich vom
        // Fliesstext absetzt; das ist unsere Zutat, das Original trennt nur
        // durch den Zeilenwechsel.
        var body = new Label
        {
            Text = _title + "\n\n" + string.Join("\n\n", _paragraphs),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(TxtW * scale, 0),
        };
        Style(body, font, scale, dark: true);
        scroll.AddChild(body);

        // ⭐⭐ 19.08.2026 — DER ZIELKASTEN, und er ist der einzige Ort fuer
        // FONT2.CWD.
        //
        // Bis heute fehlte er ganz: `objectives.json` war exportiert, wurde auf
        // diesem Schirm aber nirgends gezeichnet. Das Original setzt ihn unten
        // rechts, und dafuer — und NUR dafuer — tauscht es die zweite Schrift
        // ein (0x45C15A sichern, 0x45C174 einsetzen, 0x45C31C zurueck; die drei
        // `mov ecx,0x1478` sind die einzigen im ganzen Programm).
        //
        // Gelesen, Zeichenschleife C 0x45C2A3..0x45C31A / F 0x45AF4E:
        //
        //   x            = 355     (`mov ax,0x163`)
        //   y erste Zeile= 390     (`mov di,0x186`)
        //   Zeilenabstand= 11      (`add di,0xb`)
        //   Umbruch      = wenn x > 490 UND das Zeichen ein Leerzeichen ist
        //   abgeschnitten ab x >= Bildbreite − 10
        //
        // ⚠ Der Umbruch ist im Original ein ZEICHEN-, kein Wortumbruch: er
        // schaut erst beim naechsten Leerzeichen, ob die Grenze ueberschritten
        // ist. Godots WordSmart bricht frueher — das ist unsere Abweichung, und
        // sie faellt nur bei sehr langen Wortketten auf.
        //
        // ⚠ Die Zeilenhoehe 11 gilt NUR hier. Unser .fnt traegt 15 (dieselbe
        // Zahl, die der Fliesstext braucht), deshalb wird sie ueberschrieben.
        var ziele = Campaign.MissionObjectives.For(_mission);
        if (ziele.Count > 0)
        {
            var zielFont = LegacyFont(second: true);
            const int ZX = 355, ZY = 390, ZW = 490 - 355, ZH = 11;
            var kasten = new Label
            {
                Text = string.Join("\n", ziele),
                Position = at + new Vector2(ZX * scale, ZY * scale),
                Size = new Vector2(ZW * scale, 80 * scale),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            Style(kasten, zielFont, scale, dark: true);
            // ⚠ Negativ: 11 Zeilenabstand bei 13 px Zeichenhoehe heisst, dass
            // die Zeilen ENGER stehen als die Zeichen hoch sind — im Original
            // ueberlappen sie leicht. Godot nimmt einen negativen Abstand an.
            kasten.AddThemeConstantOverride("line_spacing",
                                            Mathf.RoundToInt((ZH - 13) * scale));
            AddChild(kasten);
        }

        // ⚠⚠ 18.08.2026 — KEIN EIGENER »AUFTRAG ANNEHMEN«-KNOPF MEHR. Der
        // Spieler dazu: »kannst unser Auftrag annehmen [Enter] raus, weil man ja
        // die Mission über den Start-Knopf startet, der unten links ist«.
        //
        // Er hat recht, und der Knopf war doppelt: das Hintergrundbild zeichnet
        // unten links ein START, und das ist der Weg des Originals. Unser
        // eigener Knopf lag zusätzlich über der Textplatte und war das
        // deutlichste Stück Fremdkörper auf diesem Schirm.
        //
        // Die Lage ist am Bild ausgemessen: die Platte mit der Aufschrift
        // START liegt bei x 154..212, y 454..476. Die Fläche hier ist etwas
        // grösser gehalten, aus demselben Grund wie bei den Monitorknöpfen.
        //
        // ⚠ Enter und Leertaste bleiben — sie laufen über _UnhandledInput und
        // hingen nie an diesem Knopf.
        _btnStart = MonitorKnopf(at, scale, 152, 452, 62, 26, "START", Continue);
        AddChild(_btnStart);
        return true;
    }

    /// <summary>The game's own bitmap font, loaded the way MapViewer loads it —
    /// imported resource first, raw .fnt second, because content derived on the
    /// player's machine never went through Godot's import step.</summary>
    /// <param name="second">FONT2.CWD when true — this screen's own typeface,
    /// which the loader @0x45bddc reads immediately before the backdrop. The
    /// start menu wants the other one, FONT.CWD, which is the game's everyday
    /// face.</param>
    public static Font? LegacyFont(bool second = true)
    {
        // FONT.CWD stands in when FONT2 was not exported (content imported
        // before that reader existed), and the other way round.
        string path = Core.Content.Path(second ? "UI/akte_font2.fnt" : "UI/akte_font.fnt");
        if (!FileAccess.FileExists(path) && !ResourceLoader.Exists(path))
            path = Core.Content.Path(second ? "UI/akte_font.fnt" : "UI/akte_font2.fnt");
        FontFile? f = null;
        if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is FontFile res)
            f = (FontFile)res.Duplicate();       // ours to configure, not the HUD's
        else if (FileAccess.FileExists(path))
        {
            var bmp = new FontFile();
            if (bmp.LoadBitmapFont(path) == Error.Ok) f = bmp;
        }
        if (f == null) return null;

        // A bitmap font has one size, and Godot renders it at that size no matter
        // what font_size says — until scaling is allowed. Whole multiples only, so
        // the 13 px glyphs stay sharp instead of turning to mush.
        f.FixedSizeScaleMode = TextServer.FixedSizeScaleMode.IntegerOnly;

        // FONT.CWD holds 160 glyphs, cp437 0x20..0xBF — and the game's own
        // briefing text uses 0xE1, the sharp s ("in einem Fluß notwassern",
        // "außerdem"). There is no glyph for it, in FONT.CWD or in the
        // unused FONT2.CWD, so the original could not have drawn one either.
        // Rather than quietly rewriting the game's words to "ss", the missing
        // characters come from the default face; everything the original has,
        // the original draws.
        f.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
        return f;
    }

    /// <summary>The atlas holds the glyphs white and the shadow black, so the
    /// colour comes from the modulation rather than the pixels.</summary>
    private static void Style(Label l, Font? font, float scale, bool head = false, bool dark = false)
    {
        if (font != null)
        {
            l.AddThemeFontOverride("font", font);
            // the atlas cell is 13 px high; whole multiples keep it crisp, the
            // same rule MapViewer follows for the HUD
            l.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(13 * scale));
        }
        else l.AddThemeFontSizeOverride("font_size", scale > 1 ? 28 : 17);
        l.AddThemeColorOverride("font_color",
            dark ? PlatteSchrift                            // auf der Textplatte
                 : head ? new Color(1f, 0.86f, 0.45f)
                        : new Color(0.95f, 0.96f, 0.97f));
    }

    /// <summary>
    /// <b>DIE TEXTPLATTE IST NICHT WEISS — SIE IST EINE MARKIERUNG.</b>
    ///
    /// <para>⚠⚠ 18.08.2026, gemeldet als »das Textfeld ist nicht original«, mit
    /// dem Originalbild daneben: dort steht <b>grüne Schrift auf fast
    /// schwarzem Grund</b>, bei uns dunkle Schrift auf Weiss.</para>
    ///
    /// <para><b>Gemessen an BRIEFG.DAT selbst:</b> Palettenindex <b>144</b>
    /// (in 01.PAL 255,255,247, also weiss) kommt im ganzen 640×480-Bild an
    /// <b>genau zwei Stellen</b> vor — der grossen Platte
    /// (x 296..615, y 79..318) und dem unteren Streifen (x 285..629,
    /// y 432..469). <b>80.986 Punkte, und 320×240 + 2×55×38 sind 80.980.</b>
    /// Nirgends sonst im Bild. Ein Weiss, das nur dort auftritt, wo etwas
    /// hingezeichnet wird, ist keine Farbe, sondern ein <b>Platzhalter</b> —
    /// das Spiel überschreibt alle drei Flächen zur Laufzeit.</para>
    ///
    /// <para><b>Die beiden Farben sind aus dem Originalbild gemessen</b> und
    /// dann auf die Palette gelegt: der Grund kommt als (15,16,11) heraus, das
    /// ist <c>0x2f</c> = (19,19,15); die Schrift als (57,130,38) ≈ <c>0x08</c>
    /// = (43,143,11). ⚠ Die Abweichung geht auf die Videokompression des
    /// Vergleichsbildes — die Palettenwerte sind die belastbaren.</para></summary>
    /// <summary>Die zwei Wetterfelder, am Bild ausgemessen: Index 144 liegt
    /// dort in zwei Bloecken von genau <b>55×38</b> auf <b>(285,432)</b> und
    /// <b>(575,432)</b>. Siehe <see cref="PlatteGrund"/>.</summary>
    private const int WeatherLX = 285, WeatherRX = 575, WeatherY = 432,
                      WeatherW = 55, WeatherH = 38;

    private static readonly Color PlatteGrund = Color.Color8(19, 19, 15);
    private static readonly Color PlatteSchrift = Color.Color8(43, 143, 11);

    // ---- harness -----------------------------------------------------------

    private int _frames;

    /// <summary>`--briefing-shot=<path>` photographs the screen and quits, so
    /// what it actually looks like can be checked rather than assumed.</summary>
    public override void _Process(double delta)
    {
        // ⚠⚠ 18.08.2026 — DER MONITOR ZEIGT ERST EUROPA. Gemeldet mit zwei
        // Bildern nebeneinander: »die Minimap ging erst auf, wenn man auf
        // Mission geklickt hat«.
        //
        // Hier stand: die zehn Bilder laufen beim Aufschlagen von selbst durch
        // und bleiben auf dem letzten stehen. Damit lag die Missionskarte
        // sofort auf der Europakarte, und die zwei Knoepfe darunter waren
        // Zierrat. Bild 0 IST die Europaansicht (rotes Europa, gelbes
        // Fadenkreuz), Bild 9 die hineingezoomte Missionskarte — die zehn sind
        // ein Zoom, kein Einlaufen des Fadenkreuzes.
        //
        // Jetzt: stehen auf Bild 0, und die Knoepfe fahren den Zoom.
        if (_radar != null && _radarFrames.Count > 1 && _radarZiel != _radarBild)
        {
            _radarTime += (float)delta;
            while (_radarTime >= RadarFrameSec && _radarZiel != _radarBild)
            {
                _radarTime -= RadarFrameSec;
                _radarBild += _radarZiel > _radarBild ? 1 : -1;
            }
            _radarBild = Mathf.Clamp(_radarBild, 0, _radarFrames.Count - 1);
            if (_radar.Texture != _radarFrames[_radarBild])
                _radar.Texture = _radarFrames[_radarBild];
        }

        // ⚠ `--briefing-mission` DRUECKT den Knopf, statt _radarZiel zu setzen.
        // Ein Pruefstand, der das Feld dahinter anfasst, prueft den Knopf nicht
        // — und genau der war es, der fehlte.
        if (!_missionGetippt)
            foreach (string a in OS.GetCmdlineUserArgs())
                if (a == "--briefing-mission" && _btnMission != null)
                {
                    _missionGetippt = true;
                    _btnMission.EmitSignal(BaseButton.SignalName.Pressed);
                }
                else if (a == "--briefing-start" && _btnStart != null)
                {
                    _missionGetippt = true;
                    GD.Print("Briefing: START gedrueckt");
                    _btnStart.EmitSignal(BaseButton.SignalName.Pressed);
                }

        string want = "";
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--briefing-shot=")) want = a["--briefing-shot=".Length..];
        TickEmblems(delta);

        // ⚠ Der Monitor sagt, worauf er steht — sonst muesste man es aus dem
        // Bildschirmfoto raten, und ein Zoom, der gerade erst anlaeuft, sieht
        // dort fast aus wie einer, der gar nicht anlaeuft.
        if (want.Length > 0 && _frames == 20 && _radarFrames.Count > 1)
            GD.Print($"Briefing: Monitor auf Bild {_radarBild} von " +
                     $"{_radarFrames.Count - 1} (Ziel {_radarZiel}) — " +
                     (_radarBild == 0 ? "EUROPA" : _radarBild == _radarFrames.Count - 1
                        ? "MISSION" : "unterwegs"));
        if (want.Length == 0 || _frames++ < 90) return;
        RenderingServer.ForceDraw();
        GetViewport().GetTexture().GetImage().SavePng(want);
        GD.Print($"briefing-shot -> {want}");
        GetTree().Quit();
    }

    private bool _done;

    private void Continue()
    {
        if (_done) return;
        _done = true;
        Audio.SoundBankPlayer.StopVoice();       // the narration goes with the screen
        Audio.GameSounds.Play(Audio.GameSounds.BriefingEnd);
        QueueFree();
        _go();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } k) return;
        if (k.Keycode is Key.Enter or Key.KpEnter or Key.Space or Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            Continue();
        }
    }

    // ---- the text ----------------------------------------------------------

    /// <summary>The briefing for a mission, or null if there is none. Missing
    /// text is not an error: a skirmish has no briefing, and neither has a
    /// build whose content was imported before this table existed.</summary>
    public static (string Title, List<string> Paragraphs)? For(int mission)
    {
        string path = Core.Content.Path("Maps/briefings.json");
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return null;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return null;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("briefings", out var bv) ||
            bv.VariantType != Variant.Type.Dictionary) return null;
        var all = bv.AsGodotDictionary<string, Variant>();
        if (!all.TryGetValue(mission.ToString(), out var one) ||
            one.VariantType != Variant.Type.Dictionary) return null;
        var d = one.AsGodotDictionary<string, Variant>();

        var paras = new List<string>();
        if (d.TryGetValue("paragraphs", out var pv) && pv.VariantType == Variant.Type.Array)
            foreach (var p in pv.AsGodotArray()) paras.Add(p.AsString());
        if (paras.Count == 0) return null;
        return (d.TryGetValue("title", out var tv) ? tv.AsString() : $"Mission {mission}", paras);
    }
}
