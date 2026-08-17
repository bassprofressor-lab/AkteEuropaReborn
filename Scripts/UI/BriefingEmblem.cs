namespace AkteEuropaReborn.UI;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>Das Emblem von Akte Europa auf dem Briefingschirm</b> — konzentrische
/// Ringe mit zwei geschwungenen Klingen. Es steht zweimal auf diesem Schirm,
/// und beides hat uns bis heute gefehlt:
///
/// <list type="number">
///   <item>als <b>Wasserzeichen hinter dem Briefingtext</b>, gross, dunkelgrau
///   auf Schwarz — und es <b>dreht sich einmal um die senkrechte Achse</b>,
///   wenn der Schirm aufgeht.</item>
///   <item>in den <b>zwei Nischen</b> unten, je 57×40 auf (285,432) und
///   (575,432), bläulich, mit einem <b>Schimmer, der von links nach rechts und
///   wieder zurück läuft</b> — und die zwei Nischen laufen gegeneinander.</item>
/// </list>
///
/// <para><b>WO DIE BILDER LAGEN — und warum sie so lange nicht zu finden
/// waren.</b> Der Schwanz von BRIEFG.DAT (43.302 Byte hinter dem 640×480-Bild)
/// galt hier als »Bank mit Kopfdaten«. <b>Er hat keine Kopfdaten.</b> Die
/// Gliederung steht im Maschinencode: der Lader schreibt bei
/// <c>0x45BE6C..0x45C04E</c> Breite und Höhe von <b>29</b> Bildern von Hand in
/// eine Tabelle, und ihre Flächen summieren sich auf <b>genau 43.302</b>. Die
/// Bilder sind verschieden breit und stehen ohne Trennzeichen aneinander —
/// darum zerfiel jede geratene Breite, und darum war Breite 106 nur ein
/// Zufallstreffer quer über zwei der 53×54-Wettertafeln.
/// Siehe <see cref="Import.BriefingExporter.Bank"/>.</para>
///
/// <para><b>Das Wasserzeichen war überhaupt nicht in BRIEFG.DAT</b> — es ist
/// eine eigene Datei, <b>SYMBOL.DAT</b>, 748.800 Byte = 9 × 320 × 260. Der
/// Lader <c>@0x45C110</c> liest von dort 260 Zeilen zu je 320 Byte auf
/// <b>(296,79)</b>, also Punkt für Punkt auf die Textplatte.
/// Siehe <see cref="Import.BriefingExporter.WriteWatermark"/>.</para>
///
/// <para>⚠ Eine frühere Deutung von mir — »das runde Zeichen ist ein
/// Wettersymbol« — bleibt <b>zurückgezogen</b>, und jetzt ist auch belegt,
/// woher sie kam: die Wettertafeln (SKY:CLOUDED, OVERCAST, DANGER, HIGH/LOW
/// TEMPERATURE) sind wirklich da, es sind die Bank-Einträge <b>13..16</b>.
/// Sie liegen nur acht Einträge vor dem Emblem und gehören zu etwas
/// anderem.</para>
/// </summary>
public partial class BriefingScreen
{
    // ---- was gezeichnet wird -------------------------------------------------

    private TextureRect? _mark;                       // das Wasserzeichen
    private TextureRect? _nicheL, _nicheR;            // die zwei Nischen
    private readonly List<Texture2D> _markFrames = new();
    private readonly List<Texture2D> _nicheFrames = new();

    /// <summary>
    /// <b>DER ZÄHLER DES ORIGINALS</b> — bei ihm ein Wort auf
    /// <c>[ebx+0x8C3CCC]</c>, im Lader <c>@0x45BD09</c> auf 0 gesetzt. Er treibt
    /// BEIDES: die Nischen über <c>zaehler &amp; 15</c> (endlos) und das
    /// Wasserzeichen über das Fenster 10..18 (einmal).
    ///
    /// <para>⚠ <b>SAUBERER NEGATIVBEFUND:</b> die Stelle, die ihn HOCHZÄHLT,
    /// steht nicht im Code. Ein roher Dword-Suchlauf über <c>.text</c> findet
    /// die Adresse 0x8C3CCC an genau sieben Stellen — einmal die Null im Lader,
    /// sechsmal Lesen im Zeichner. Kein einziges Schreiben ausser der Null. Der
    /// Zähler wird also über einen gerechneten Zeiger fortgeschaltet, den ein
    /// Adressuchlauf nicht sieht. <b>Sein TAKT ist damit nicht gelesen.</b></para>
    /// </summary>
    private int _emblemZaehler;

    private float _emblemZeit;

    /// <summary>⚠ <b>UNSERE SETZUNG.</b> Wie lange ein Zählerschritt dauert. Der
    /// Takt des Originals ist nicht gelesen (siehe <see cref="_emblemZaehler"/>),
    /// also nimmt er denselben Wert wie <see cref="RadarFrameSec"/> nebenan —
    /// ein Zehntel. Damit braucht der Schimmer 1,6 s für hin und zurück und die
    /// Drehung des Wasserzeichens 0,9 s. Zwei Zahlen aus derselben Quelle sind
    /// besser als zwei geratene aus zweien.</summary>
    private const float EmblemSchrittSec = 0.10f;

    /// <summary>Das zuletzt gesetzte Wasserzeichenbild, damit
    /// <see cref="TickEmblems"/> die Textur nicht jedes Bild neu zuweist — und
    /// damit es nach dem Fenster 10..18 auf Bild 8 <b>stehenbleibt</b>, so wie
    /// das Original den Puffer danach nicht mehr anfasst.</summary>
    private int _markBild = -1;

    // ---- bauen ---------------------------------------------------------------

    /// <summary>Das Emblem zeichnen — Wasserzeichen und die zwei Nischen. Ohne
    /// eingespielte Bilder tut es nichts, und die Nischen bleiben die dunklen
    /// Flächen, die <c>BuildBackdrop</c> dort hinlegt.</summary>
    private void BuildEmblems(Vector2 at, float scale)
    {
        Laden("UI/emblem/mark", Import.BriefingExporter.MarkFrames, _markFrames);
        Laden("UI/emblem/niche", Import.BriefingExporter.EmblemFrames, _nicheFrames);

        // Der Prüfstand steigt hier ein: er misst an den EINGESPIELTEN Bildern
        // und an der Phasenrechnung, die dieser Zeichner benutzt — nicht an
        // einer zweiten Abschrift davon.
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a == "--emblem-check")
            {
                int rc = Import.EmblemCheck.Run(GD.Print);
                GetTree().Quit(rc);
                return;
            }

        // ⚠ Das Wasserzeichen ist 320×260 und die Platte 240 hoch: die letzten
        // zwanzig Zeilen laufen UNTER die Platte. Das Original tut genau das
        // (260 Zeilen ab y=79), und nachgemessen ist der Hintergrund dort
        // ohnehin Palettenindex 47 wie der Grund der Bilder — man sieht es
        // nicht. Abschneiden wäre eine stillschweigende Abweichung.
        if (_markFrames.Count == Import.BriefingExporter.MarkFrames)
        {
            _mark = new TextureRect
            {
                Texture = null,                    // vor Zählerstand 10 steht dort nichts
                Position = at + new Vector2(Import.BriefingExporter.MarkX * scale,
                                            Import.BriefingExporter.MarkY * scale),
                Size = new Vector2(Import.BriefingExporter.MarkW * scale,
                                   Import.BriefingExporter.MarkH * scale),
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            AddChild(_mark);
        }

        if (_nicheFrames.Count == Import.BriefingExporter.EmblemFrames)
        {
            _nicheL = Nische(at, scale, Import.BriefingExporter.EmblemLX, right: false);
            _nicheR = Nische(at, scale, Import.BriefingExporter.EmblemRX, right: true);
            AddChild(_nicheL);
            AddChild(_nicheR);
        }

        if (_mark != null || _nicheL != null)
            GD.Print($"Briefing: Emblem — Wasserzeichen {_markFrames.Count} Bilder, " +
                     $"Nischen {_nicheFrames.Count} Bilder");
    }

    private TextureRect Nische(Vector2 at, float scale, int x, bool right) => new()
    {
        Texture = _nicheFrames[Import.BriefingExporter.NicheFrame(0, right)],
        Position = at + new Vector2(x * scale, Import.BriefingExporter.EmblemY * scale),
        Size = new Vector2(Import.BriefingExporter.EmblemW * scale,
                           Import.BriefingExporter.EmblemH * scale),
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        StretchMode = TextureRect.StretchModeEnum.Scale,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Die Bilder eines Satzes laden — erst als Godot-Ressource, dann
    /// als lose Datei, wie <see cref="BuildRadar"/> es auch tut: eingespielte
    /// Inhalte haben nie den Import von Godot gesehen. Bricht beim ersten
    /// fehlenden Bild ab, damit ein halber Satz nicht als ganzer durchgeht.</summary>
    private static void Laden(string dir, int count, List<Texture2D> into)
    {
        for (int f = 0; f < count; f++)
        {
            string p = Core.Content.Path($"{dir}/f{f}.png");
            Texture2D? t = ResourceLoader.Exists(p) ? ResourceLoader.Load<Texture2D>(p) : null;
            if (t == null && FileAccess.FileExists(p))
            {
                var im = Image.LoadFromFile(ProjectSettings.GlobalizePath(p));
                if (im != null) t = ImageTexture.CreateFromImage(im);
            }
            if (t == null) { into.Clear(); return; }
            into.Add(t);
        }
    }

    // ---- laufen lassen -------------------------------------------------------

    /// <summary>Ein Zählerschritt je <see cref="EmblemSchrittSec"/>, und daraus
    /// beides: der Schimmer in den Nischen (endlos, gegenläufig) und die eine
    /// Drehung des Wasserzeichens im Fenster 10..18.</summary>
    private void TickEmblems(double delta)
    {
        if (_mark == null && _nicheL == null) return;

        _emblemZeit += (float)delta;
        bool weiter = false;
        while (_emblemZeit >= EmblemSchrittSec)
        {
            _emblemZeit -= EmblemSchrittSec;
            _emblemZaehler++;
            weiter = true;
        }
        if (!weiter) return;

        if (_nicheL != null && _nicheR != null)
        {
            _nicheL.Texture = _nicheFrames[
                Import.BriefingExporter.NicheFrame(_emblemZaehler, right: false)];
            _nicheR.Texture = _nicheFrames[
                Import.BriefingExporter.NicheFrame(_emblemZaehler, right: true)];
        }

        // ⚠ Nur im Fenster. Danach bleibt Bild 8 stehen — das Original schreibt
        // das Wasserzeichen in den Hintergrundpuffer und rührt ihn danach nicht
        // mehr an, also steht dort für den Rest des Schirms das letzte Bild.
        int m = Import.BriefingExporter.MarkFrame(_emblemZaehler);
        if (_mark != null && m >= 0 && m != _markBild)
        {
            _markBild = m;
            _mark.Texture = _markFrames[m];
        }
    }
}
