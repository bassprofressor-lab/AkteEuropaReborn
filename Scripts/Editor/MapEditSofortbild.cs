namespace AkteEuropaReborn.Editor;

using System;
using System.Threading.Tasks;
using Godot;
using AkteEuropaReborn.Import;
using AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐ 12.11.2026 — D4 (Fable-Pruefung): DAS SOFORTBILD. Bisher war jeder Pinselstrich
/// erst nach F5 im Bild (Speichern + Neuladen der ganzen Szene). Jetzt wird nach dem
/// Strich — mit kurzer Wartezeit, damit ein Zug nicht zwanzig Backvorgaenge ausloest —
/// eine KOPIE der Karte im Hintergrund gebacken, mit DEMSELBEN Backofen wie beim
/// Speichern (MapBaker, ContentBuilder.MapMetaText), und dann Bodenbild und
/// Objektebene im laufenden Betrachter ausgetauscht. Nichts wird geschrieben; F5 bleibt
/// das Speichern.
///
/// <para>⚠ Grenzen, benannt: Aendert sich die BILDGROESSE (eine Anhebung ueber die
/// bisher hoechste Stufe der Karte verschiebt den Ursprung), wird NICHT getauscht —
/// dann sagt die Leiste »F5 zeigt es«, denn Einheiten und Gebaeude haengen an diesem
/// Ursprung. Einheiten, Gebaeude und Gleise zeichnet die Entitaetenebene; die zeigt
/// ihre Aenderungen wie bisher selbst oder nach F5. Gegenschalter --editor-sofortbild-aus.</para>
/// </summary>
public partial class MapEditOverlay
{
    private static bool SofortAus => Array.IndexOf(Core.CommandLine.Args, "--editor-sofortbild-aus") >= 0;

    private int _sofortGen;          // jeder neue Auftrag macht die aelteren ungueltig
    private ulong _sofortStand;      // Abdruck der zuletzt gebackenen Karte
    private bool _sofortLaeuft;
    private bool _sofortNochmal;

    /// <summary>Wie viele Bilder schon getauscht wurden (fuer den Pruefstand).</summary>
    public int SofortGetauscht { get; private set; }
    public string SofortZeile { get; private set; } = "";
    /// <summary>Das zuletzt getauschte Bodenbild (fuer den Pruefstand: kopflos liefert
    /// <c>ImageTexture.GetImage()</c> nach <c>Update</c> weiter das ALTE Bild — der
    /// Dummy-Renderer behaelt nur das erste, bug-827).</summary>
    public Image? SofortLetzterBoden { get; private set; }

    /// <summary>Eine Kopie der bearbeiteten Karte (alle Sektionen geklont).</summary>
    private CwmFile KartenKopie()
    {
        var kopie = CwmFile.Create(_map.Width, _map.Height, _map.Tileset, _map.Stem, _map.Mission);
        for (int i = 0; i < _map.Sections.Count && i < kopie.Sections.Count; i++)
            kopie.Sections[i] = (byte[])_map.Sections[i].Clone();
        return kopie;
    }

    private static bool Bildwirksam(Brush b) => b is Brush.Gelaende or Brush.Hoehe or Brush.Gegenstand
                                                  or Brush.Gleis or Brush.Wegnehmen or Brush.Bruecke;

    /// <summary>Nach einem Strich (oder Rueckgaengig) aufrufen.</summary>
    private async void SofortbildPlanen()
    {
        if (SofortAus || MapEditSession.Cwp == null || MapEditSession.Pal == null) return;
        int gen = ++_sofortGen;
        await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        if (gen != _sofortGen || !IsInsideTree()) return;            // inzwischen weiter gemalt
        if (_sofortLaeuft) { _sofortNochmal = true; return; }
        SofortbildBacken();
    }

    private ulong KartenAbdruck()
    {
        ulong h = 14695981039346656037UL;
        foreach (var a in _map.Sections) foreach (byte b in a) { h ^= b; h *= 1099511628211UL; }
        return h;
    }

    private async void SofortbildBacken()
    {
        ulong stand = KartenAbdruck();
        if (stand == _sofortStand) return;                          // nichts Neues
        _sofortLaeuft = true;
        // die Kopie auf dem Hauptfaden — der Pinsel darf weitermalen, waehrend gebacken wird
        var kopie = KartenKopie();
        var cwp = MapEditSession.Cwp!; var pal = MapEditSession.Pal!;
        var t0 = DateTime.UtcNow;
        (Image Boden, Image? Objekte, string Meta)? fertig = null;
        string? fehler = null;
        await Task.Run(() =>
        {
            try
            {
                var baker = new MapBaker(kopie, cwp, pal);
                var img = baker.Bake();
                var ol = baker.ObjectLayer();
                fertig = (img, ol, ContentBuilder.MapMetaText(kopie, baker));
            }
            catch (Exception e) { fehler = e.Message; }
        });
        _sofortLaeuft = false;
        if (!IsInsideTree()) return;
        double ms = (DateTime.UtcNow - t0).TotalMilliseconds;
        if (fertig is { } f)
        {
            bool ok = _layer.EditorSofortbild(f.Boden, f.Objekte, f.Meta, out string warum);
            if (ok) { _sofortStand = stand; SofortGetauscht++; SofortLetzterBoden = f.Boden; }
            SofortZeile = ok ? $"Bild aktualisiert ({ms / 1000:0.0} s)" : $"Bild: {warum} — F5 zeigt es";
            _say = SofortZeile;
            GD.Print($"map-editor: Sofortbild {(ok ? "getauscht" : "NICHT getauscht: " + warum)} nach {ms:0} ms");
        }
        else
        {
            SofortZeile = "Bild: Backen fehlgeschlagen — F5 zeigt es";
            GD.PrintErr($"map-editor: Sofortbild fehlgeschlagen: {fehler}");
        }
        Refresh();
        if (_sofortNochmal) { _sofortNochmal = false; SofortbildPlanen(); }
    }
}
