namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ 29.10.2026, bug-644 (KayelGee R8 K2, »nach Brueckenzerstoerung bleibt ein Gelaender
/// stehen«, Bild <c>Bug Bilder/2910292bug2.png</c>) — <b>DIE KARTENBRUECKE WIRD LEBEND
/// GEZEICHNET, NICHT MEHR INS BODENBILD GEBACKEN.</b> Spielerentscheidung 29.10.: »SAUBER«.
///
/// <para><b>Befund</b> (berichte/bruecken-r8-opus.md K2): die Fahrbahnkachel einer
/// Kartenbruecke (Lage 100…199, imap <c>0xFFFE</c>) lag im gebackenen <c>map_NN.png</c>.
/// Ihr Bild ragt ueber das Satzrechteck hinaus (M4 Platz 1: ab Weltzeile 697, die
/// Gelaenderzelle beginnt bei 705 → <b>8 px Ueberstand</b>). Der Abriss uebermalt nur die
/// 3·(k+2) Satzzellen — der Ueberstand blieb als heller Streifen ueber dem Wasser stehen.</para>
///
/// <para><b>Original:</b> es gibt kein Bodenbild. Der flache Durchgang malt jede Zelle je
/// Bild aus ihrem Kachelwort (<c>0x401410</c>, Bildbank-Weiche @<c>0x4B428F</c>);
/// <c>Erase bridge 0x4CB0A0</c> (F <c>0x4CAC70</c>) ersetzt die Kachelwoerter → die alte
/// Fahrbahnkachel wird gar nicht mehr gemalt, ein Ueberstand kann nicht stehen bleiben.</para>
///
/// <para><b>Gebaut:</b> (1) der Backofen (<c>Import/MapBaker.cs</c>) laesst die
/// Fahrbahnzellen der Lage 100…199 im Bodenbild weg (darunter bleibt die gewohnte
/// <c>basis</c>-Fuellung); (2) hier wird jede Fahrbahnzelle einer HEILEN Kartenbruecke im
/// Bodendurchgang (<see cref="ZeichneStege"/>, vor den Abrisskacheln) aus ihrer
/// KARTENkachel (<c>_rampenKachel</c>, das Kachelwort der Datei) gemalt — dieselbe Kachel,
/// dieselbe Hoehe, derselbe BlitAnchor wie beim Backen, also dasselbe Bild. Hat sie eine
/// Schadensstufe, malt sie wie bisher der Satz selbst (Feld + 18·Stufe); ist sie
/// abgerissen, malt hier nichts mehr. Die Gelaender liegen unveraendert in der Objektebene.
/// (3) Die Uebersichtskarte bekommt dieselben Kacheln (<see cref="KartenfahrbahnUebersicht"/>),
/// sonst fehlte ihr die Bruecke.</para>
///
/// <para>⚠ <b>Braucht einen Neuexport der Karten</b> (<c>--reexport-maps</c>); darum
/// <c>InhaltsStand.Noetig["karten"] = 3</c>. Mit Daten eines aelteren Imports steht die
/// Fahrbahn doppelt (gebacken + lebend, deckungsgleich) und der Streifen bleibt.</para>
///
/// <para>⚠ <b>UNSERE SETZUNG:</b> das Bodenbild ist ein einmal gebackenes Bild; was nach
/// einer Zelle in Zeilenfolge gebacken wurde und in die Fahrbahn hineinragt, liegt jetzt
/// UNTER ihr statt darueber. Am Bildvergleich vorher/nachher gemessen
/// (berichte/bau-p3-0-6-12.md).</para>
///
/// <para>Gegenschalter <c>--bruecke-gebacken-alt</c>: der alte Weg, soweit er mit den
/// neuen Daten geht — die Fahrbahn jeder Kartenbruecke wird IMMER als unterste Lage
/// gemalt (wie das gebackene Bild), auch beschaedigt und nach dem Abriss; Abrisskacheln
/// und Stufenbilder decken sie wie frueher. Damit steht der Streifen wieder da.
/// Pruefstand <c>--bruecke-streifen-check</c> (Rendering/PaketP3Lauf.cs).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--bruecke-gebacken-alt</c> — Nullmodell zu bug-644.</summary>
    public static bool BrueckeGebackenAlt;

    /// <summary>Die Fahrbahnzellen aller Kartenbruecken mit Platz und LADEhoehe.</summary>
    private readonly List<(Vector2I Zelle, int Slot, int Elev)> _kartenFahrbahn = new();

    public int KartenfahrbahnGezeichnet;

    /// <summary><c>--bruecke-clipuv-alt</c> — Messschalter: die lebende Fahrbahn mit Godots
    /// Vorgabe clip_uv = an zeichnen (Nahtzeilen anders als im gebackenen Bild).</summary>
    public static bool BrueckeClipUvAlt;

    /// <summary>Welche Fahrbahnkacheln jetzt zu malen sind: (Zelle, Code, Hoehe).</summary>
    private IEnumerable<(Vector2I Zelle, int Code, int Elev)> KartenfahrbahnKacheln()
    {
        foreach (var (z, slot, elev) in _kartenFahrbahn)
        {
            int key = z.X * 1024 + z.Y;
            var st = _stege.Find(x => x.Karte && x.Slot == slot);
            if (!BrueckeGebackenAlt && (st == null || st.Tp < BrueckeTp)) continue;
            int code;
            if (!_rampenKachel.TryGetValue(key, out code))
            {
                if (st == null) continue;
                code = 10000 + st.Feld[FeldIndex(1, Mathf.Clamp(BrueckenLage(st, z).Pos, 0, 4))];
            }
            yield return (z, code, BrueckeGebackenAlt ? elev : ElevOf(z.X, z.Y));
        }
    }

    /// <summary>Im Bodendurchgang, als UNTERSTE Lage der Bauwerke.</summary>
    private void ZeichneKartenfahrbahn(Texture2D tex)
    {
        KartenfahrbahnGezeichnet = 0;
        foreach (var (z, code, elev) in KartenfahrbahnKacheln())
        {
            if (StreifenKachel(code) is not { } k) continue;
            var ziel = CellRect(_ox, _oy, z.X, z.Y, elev);
            DrawTextureRectRegion(tex,
                new Rect2(new Vector2(ziel.Position.X, ziel.Position.Y + Import.MapBaker.BlitAnchor + k.YOff),
                          k.Feld.Size), k.Feld, null, false,
                // ⚠ clip_uv AUS: mit der Vorgabe (an) zieht Godot die Quelle um einen halben
                // Bildpunkt ein, und bei Zoom 2 zeigte die erste/letzte Zeile jeder Kachel die
                // Nachbarzeile — gemessen am Bildvergleich gegen das gebackene Bild (M13, M10).
                BrueckeClipUvAlt);
            KartenfahrbahnGezeichnet++;
        }
    }

    /// <summary>Fuer die Uebersichtskarte: Weltrechteck und Quellrechteck in der
    /// Objektebene je Fahrbahnkachel einer stehenden Kartenbruecke.</summary>
    public List<(Rect2 Ziel, Rect2 Quelle)> KartenfahrbahnUebersicht()
    {
        var l = new List<(Rect2, Rect2)>();
        foreach (var (z, code, elev) in KartenfahrbahnKacheln())
        {
            if (StreifenKachel(code) is not { } k) continue;
            var ziel = CellRect(_ox, _oy, z.X, z.Y, elev);
            l.Add((new Rect2(new Vector2(ziel.Position.X, ziel.Position.Y + Import.MapBaker.BlitAnchor + k.YOff),
                             k.Feld.Size), k.Feld));
        }
        l.AddRange(KartenrampenUebersicht());      // bug-685, KartenrampeLebend.cs
        return l;
    }

    /// <summary>Fuer den Pruefstand: die deckenden Punkte der Fahrbahnkacheln der ersten
    /// Kartenbruecke in Weltkoordinaten (aus der Objektebene gelesen).</summary>
    public List<Vector2> KartenfahrbahnPunkte()
    {
        var aus = new List<Vector2>();
        var st = _stege.Find(s => s.Karte);
        if (st == null || ObjektEbene is not { } tex) return aus;
        var bild = tex.GetImage();
        if (bild == null) return aus;
        foreach (var (z, slot, elev) in _kartenFahrbahn)
        {
            if (slot != st.Slot) continue;
            if (!_rampenKachel.TryGetValue(z.X * 1024 + z.Y, out int code)) continue;
            if (StreifenKachel(code) is not { } k) continue;
            var ziel = CellRect(_ox, _oy, z.X, z.Y, elev);
            for (int y = 0; y < (int)k.Feld.Size.Y; y++)
                for (int x = 0; x < (int)k.Feld.Size.X; x++)
                    if (bild.GetPixel((int)k.Feld.Position.X + x, (int)k.Feld.Position.Y + y).A > 0.5f)
                        aus.Add(new Vector2(ziel.Position.X + x + 0.5f,
                                            ziel.Position.Y + Import.MapBaker.BlitAnchor + k.YOff + y + 0.5f));
        }
        return aus;
    }
}
