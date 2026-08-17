namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DAS FELD »NEUE TECHNOLOGIEN«</b> auf dem Briefingschirm — was diese
/// Mission NEU ankündigt: eine Überschrift auf schwarzem Streifen und darunter
/// ein Bild.
///
/// <para><b>Die Zuordnung ist gelesen</b>, 18.08.2026, und sie steht nicht dort,
/// wo dieser Kopf sie bis heute vermutet hat. Sie ist <b>weder</b> im
/// Missionssatz <b>noch</b> aus dem Freischalt-Fahrplan abgeleitet, sondern eine
/// eigene, redaktionelle Tafel in GAME.EXE: 224 Byte je Mission, zehn Einträge
/// zu 22 Byte (20 Byte Name in <b>Latin-1</b>, dann ein Wort =
/// Enzyklopädieseite). Wie sie gefunden wurde und warum die Form so ist, steht
/// bei <see cref="Import.MissionTechExporter"/>; gelesen wird sie hier über
/// <see cref="Import.MissionTechTable"/>.</para>
///
/// <para><b>Der Weg dorthin war der Zeichner, nicht die Datei.</b> Der
/// Briefingschirm ist Fensterart <b>43</b> (der Erbauer <c>0x45BC10</c> setzt
/// <c>byte[…] = 0x2B</c>), seine Zeichenroutine steht in der Zeichnertafel
/// <c>@0x487888</c> auf Platz 42 und ist <c>0x486480</c>. Deren letzter Block
/// malt genau drei Dinge, und die drei sind dieser Kasten:</para>
/// <list type="number">
/// <item>den <b>schwarzen Streifen</b> — <c>0x4021DA(0x32, 0x154, 0x96, 0x0F, 0, …)</c>
/// @0x486C4F, also <b>(50, 340) 150×15</b> in Palettenindex 0;</item>
/// <item>den <b>Namen</b> darauf — <c>0x401041(0x32, 0x154, &lt;Tafeleintrag&gt;, …)</c>
/// @0x486C8D;</item>
/// <item>das <b>Bild</b> darunter — 60 Zeilen zu 60 Byte nach
/// <c>x = 0x1E</c>, <c>y = 0x172</c> @0x486C12, also <b>(30, 370) 60×60</b>.</item>
/// </list>
///
/// <para>⚠⚠ <b>DAS BILD KOMMT NICHT AUS <see cref="PortraitBank"/>.</b> Der
/// Auftrag legte das nahe, und es wäre falsch gewesen: der Zeichner öffnet
/// <b>ENCYCLOG.PIC</b>, springt auf <c>3600·(Bild−1)</c> und liest 3.600 Byte —
/// das sind 60×60, und 345.600/3.600 sind <b>96 Bilder</b>. Damit ist neben-
/// bei die Frage beantwortet, die <see cref="Import.EncyclopediaExporter"/>
/// offengelassen hat (»bei 120×120 nur 24 Bilder — die Nummer zeigt nicht ohne
/// Weiteres dorthin«): sie sind 60×60, und der Weg führt über die
/// SEITENnummer.</para>
///
/// <para>⚠ <b>Nur EIN Eintrag steht zur Zeit im Kasten.</b> Das Original hält
/// den Blätterstand in <c>byte[0x8C3CC9]</c> und blättert mit den zwei
/// Pfeilknöpfen bei <b>(65, 452)</b> und <b>(107, 452)</b>
/// (<c>0x486A25</c>/<c>0x486A5C</c>) — dieselben zwei leeren Platten, die unser
/// Hintergrundbild links von START schon zeichnet. Sie sind hier angeschlossen,
/// denn sechs der dreissig Missionen haben mehr als drei Einträge und Mission 22
/// und 25 haben sechs; ohne Blättern wäre die Hälfte davon unerreichbar (siehe
/// Arbeitsweise: eine Mechanik, die nur auf einer Taste liegt, ist für den
/// Spieler nicht vorhanden).</para>
///
/// <para><b>Leer ist eine Antwort.</b> Die Missionen <b>1, 4 und 28</b> kündigen
/// im Original nichts an — der Kasten bleibt dort schwarz, genau wie in
/// <c>Bug Bilder/kampagnen preview original.png</c>. Wir bleiben dort ebenfalls
/// leer und sagen es in der Ausgabe, damit »leer, weil nichts« von »leer, weil
/// kaputt« zu unterscheiden ist.</para>
///
/// <para>⚠ <b>Warum die zwei Schalter hier hängen und nicht im Hauptmenü:</b>
/// an diesem Schirm arbeitet ein zweiter Agent, und <c>MainMenu.cs</c> und
/// <c>ContentBuilder.cs</c> gehören ihm so gut wie mir. Also nimmt die Tafel
/// denselben Notbehelf, den <see cref="PortraitBank"/> für die Bildbank nimmt:
/// <c>--tech-export=&lt;Installation&gt;</c> schreibt sie einmal,
/// <c>--tech-check</c> misst sie nach. <b>Offen bleibt der eine Haken im
/// regulären Einlesen</b> (<c>ContentBuilder</c>, neben BRIEFG.DAT) — der
/// gehört dem, dem die Datei gehört.</para>
/// </summary>
public partial class BriefingScreen
{
    // ---- die Lage, aus dem Zeichner abgezählt (640x480) ----------------------

    /// <summary>Der schwarze Streifen mit dem Namen — <c>0x486C4F</c>.</summary>
    private const int TechBarX = Import.MissionTechExporter.BarX,
                      TechBarY = Import.MissionTechExporter.BarY,
                      TechBarW = Import.MissionTechExporter.BarW,
                      TechBarH = Import.MissionTechExporter.BarH;

    /// <summary>Das 60×60-Bild darunter — <c>0x486C12</c>.</summary>
    private const int TechPicX = Import.MissionTechExporter.PicX,
                      TechPicY = Import.MissionTechExporter.PicY,
                      TechPicW = Import.MissionTechExporter.PicW,
                      TechPicH = Import.MissionTechExporter.PicH;

    /// <summary>Die zwei Blätterknöpfe — <c>0x486A25</c> und <c>0x486A5C</c>.
    /// Breite und Höhe sind am Hintergrundbild ausgemessen (die zwei dunklen
    /// Platten links von START liegen bei x 70..90 bzw. 110..130, y 453..477);
    /// die Flächen hier reichen einen Punkt weiter, aus demselben Grund wie bei
    /// den Monitorknöpfen.</summary>
    private const int TechPrevX = 65, TechNextX = 107, TechArrowY = 451,
                      TechArrowW = 28, TechArrowH = 28;

    /// <summary>Palettenindex 0 in DATA/01.PAL ist (0,0,0) — der Streifen ist
    /// wirklich schwarz und nicht bloss dunkel.</summary>
    private static readonly Color TechBarFarbe = Color.Color8(0, 0, 0);

    // ---- der Zustand des Kastens --------------------------------------------

    private List<Import.MissionTechTable.Entry>? _tech;
    private int _techAt;
    private Label? _techName;
    private TextureRect? _techPic;

    /// <summary>Das Feld »neue technologien« füllen.</summary>
    private void BuildTechPanel(Vector2 at, float scale)
    {
        TechFlags();                       // --tech-export= / --tech-check
        if (_mission <= 0) return;

        _tech = Import.MissionTechTable.Of(_mission);
        if (_tech == null)
        {
            GD.Print("Briefing: »neue technologien« leer — " +
                     Import.MissionTechTable.Trouble);
            return;
        }
        if (_tech.Count == 0)
        {
            // Original: Missionen 1, 4 und 28 kündigen nichts an.
            GD.Print($"Briefing: Mission {_mission} kuendigt keine Technik an — " +
                     "der Kasten bleibt leer wie im Original");
            return;
        }

        AddChild(new ColorRect
        {
            Color = TechBarFarbe,
            Position = at + new Vector2(TechBarX * scale, TechBarY * scale),
            Size = new Vector2(TechBarW * scale, TechBarH * scale),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });

        var font = LegacyFont();
        if (font is FontFile ff) ff.FixedSizeScaleMode = TextServer.FixedSizeScaleMode.Enabled;

        _techName = new Label
        {
            Position = at + new Vector2(TechBarX * scale, TechBarY * scale),
            Size = new Vector2(TechBarW * scale, TechBarH * scale),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,               // ein langer Name laeuft nicht aus dem Streifen
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Style(_techName, font, scale);
        AddChild(_techName);

        _techPic = new TextureRect
        {
            Position = at + new Vector2(TechPicX * scale, TechPicY * scale),
            Size = new Vector2(TechPicW * scale, TechPicH * scale),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_techPic);

        // ⚠ Die zwei Platten sind auch dann angeschlossen, wenn es nur einen
        // Eintrag gibt — das Original zeichnet sie ebenfalls immer; sie tun dann
        // schlicht nichts (0x486A9E/0x486AD8 klemmen den Stand bei 0 und
        // Anzahl−1 fest).
        AddChild(MonitorKnopf(at, scale, TechPrevX, TechArrowY, TechArrowW, TechArrowH,
                              "vorige Technologie", () => TechStep(-1)));
        AddChild(MonitorKnopf(at, scale, TechNextX, TechArrowY, TechArrowW, TechArrowH,
                              "naechste Technologie", () => TechStep(+1)));

        TechShow();
        GD.Print($"Briefing: Mission {_mission} kuendigt {_tech.Count} Technologie(n) an — " +
                 string.Join(", ", _tech.ConvertAll(e => $"{e.Name} (Seite {e.Page}, Bild {e.Picture})")));
    }

    /// <summary>Einen Eintrag weiter oder zurück, an den Enden festgeklemmt —
    /// so wie <c>0x486A9E</c> (zurück, nicht unter 0) und <c>0x486AD8</c>
    /// (vor, nicht über Anzahl−1).</summary>
    private void TechStep(int by)
    {
        if (_tech == null || _tech.Count == 0) return;
        _techAt = Mathf.Clamp(_techAt + by, 0, _tech.Count - 1);
        TechShow();
    }

    private void TechShow()
    {
        if (_tech == null || _techAt < 0 || _techAt >= _tech.Count) return;
        var e = _tech[_techAt];
        if (_techName != null) _techName.Text = e.Name.ToUpperInvariant();
        if (_techPic != null) _techPic.Texture = Import.MissionTechTable.Picture(e.Picture);
    }

    // ---- die zwei Schalter ---------------------------------------------------

    private static bool _techFlagsDone;

    /// <summary>
    /// <c>--tech-export=&lt;Installation oder GAME.EXE&gt;</c> schreibt die
    /// Zuordnung und die Bilder in den Benutzerordner;
    /// <c>--tech-check</c> misst sie nach und beendet den Lauf mit dem
    /// Rückgabewert der Messung.
    /// </summary>
    private void TechFlags()
    {
        if (_techFlagsDone) return;
        _techFlagsDone = true;
        string quelle = "", schuss = "";
        bool pruefen = false;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--tech-export=")) quelle = a["--tech-export=".Length..];
            else if (a.StartsWith("--tech-check=")) { pruefen = true; quelle = a["--tech-check=".Length..]; }
            else if (a == "--tech-check") pruefen = true;
            else if (a.StartsWith("--briefing-shot=")) schuss = a;
        }
        if (quelle.Length > 0)
        {
            int n = Import.MissionTechExporter.RunFromSource(
                quelle, ProjectSettings.GlobalizePath(Core.Content.UserRoot), s => GD.Print(s));
            Import.MissionTechTable.Forget();
            if (n < 0) GD.PrintErr($"tech-export: nichts geschrieben (Quelle »{quelle}«)");
        }
        if (!pruefen) return;
        int rc = TechCheck();
        // ⚠ Ein Bildschirmfoto schlaegt den Pruefstand: wer beides bestellt,
        // will das Bild — der Schirm darf dann nicht vorher weggehen.
        if (schuss.Length > 0) { GD.Print("tech-check: --briefing-shot= gesetzt, kein Abbruch"); return; }
        Callable.From(() => GetTree().Quit(rc)).CallDeferred();
    }

    /// <summary>
    /// <b>Der Prüfstand.</b> Er ruft die Tafel auf, er bildet sie nicht nach,
    /// und er kann scheitern — jede der fünf Messungen unten hat einen Ausgang,
    /// der einen Rückgabewert ungleich 0 erzeugt.
    ///
    /// <para>Die zwei Gegenproben sind BELEGT und nicht gesetzt: der Spieler hat
    /// zwei Bildschirmfotos des Originals beigelegt. In
    /// <c>Bug Bilder/kampagnen preview original.png</c> (Mission 1) ist der
    /// Kasten leer, in <c>Bug Bilder/kampagne2 original previewl.png</c>
    /// (Mission 2) steht »LEICHTE BORDKANONE« darin. Beides muss herauskommen,
    /// sonst steht die Tafel um eine Mission verschoben — der einzige Fehler,
    /// den diese Form überhaupt zulässt, und der teuerste.</para>
    /// </summary>
    private static int TechCheck()
    {
        GD.Print("tech-check: " + Import.MissionTechTable.WatchLine());
        if (!Import.MissionTechTable.Ready)
        {
            GD.PrintErr("tech-check: keine Zuordnung — " + Import.MissionTechTable.Trouble);
            return 2;
        }

        int rc = 0, mitTechnik = 0, eintraege = 0, ohneBild = 0, bildFehlt = 0;
        var known = new List<int>(Import.MissionTechTable.KnownMissions());
        known.Sort();
        foreach (int m in known)
        {
            var l = Import.MissionTechTable.Of(m);
            if (l == null || l.Count == 0) continue;
            mitTechnik++;
            eintraege += l.Count;
            foreach (var e in l)
            {
                if (e.Name.Length == 0 || e.Page <= 0)
                {
                    GD.PrintErr($"tech-check: Mission {m} hat einen Eintrag ohne Name oder Seite");
                    rc |= 1;
                }
                if (e.Picture <= 0) { ohneBild++; continue; }
                if (Import.MissionTechTable.Picture(e.Picture) == null) bildFehlt++;
            }
            GD.Print($"tech-check: Mission {m,2}: " +
                     string.Join(" · ", l.ConvertAll(e => $"{e.Name} [S{e.Page}/B{e.Picture}]")));
        }

        GD.Print($"tech-check: {known.Count} Missionen gefuehrt, {mitTechnik} mit Technik, " +
                 $"{eintraege} Eintraege, {ohneBild} ohne Bildnummer, {bildFehlt} Bild nicht ladbar");

        if (known.Count != Import.MissionTechExporter.Missions)
        {
            GD.PrintErr($"tech-check: {known.Count} Missionen statt " +
                        $"{Import.MissionTechExporter.Missions}");
            rc |= 1;
        }
        var m1 = Import.MissionTechTable.Of(1);
        if (m1 == null || m1.Count != 0)
        {
            GD.PrintErr("tech-check: Mission 1 hat einen Eintrag — im Original ist ihr " +
                        "Kasten LEER (Bug Bilder/kampagnen preview original.png)");
            rc |= 1;
        }
        var m2 = Import.MissionTechTable.Of(2);
        if (m2 == null || m2.Count == 0 || m2[0].Name != "Leichte Bordkanone")
        {
            GD.PrintErr("tech-check: Mission 2 fuehrt zuerst »" +
                        (m2 != null && m2.Count > 0 ? m2[0].Name : "nichts") +
                        "« statt »Leichte Bordkanone« (Bug Bilder/kampagne2 original previewl.png)");
            rc |= 1;
        }
        if (bildFehlt > 0)
        {
            GD.PrintErr($"tech-check: {bildFehlt} Bilder aus ENCYCLOG.PIC fehlen — " +
                        "»--tech-export=<Installation>« schreibt sie");
            rc |= 1;
        }
        GD.Print(rc == 0 ? "tech-check: in Ordnung" : $"tech-check: BEANSTANDET (rc={rc})");
        return rc;
    }
}
