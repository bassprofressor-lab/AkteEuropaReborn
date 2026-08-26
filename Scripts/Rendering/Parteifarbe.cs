namespace AkteEuropaReborn.Rendering;

using Godot;
using System.Collections.Generic;

/// <summary>
/// ⭐⭐⭐ <b>DIE PARTEIFARBE — cyan für neutral, blau für den Spieler</b>
/// (24.08.2026). Gemeldet als: »neutrale einheiten haben erst einen cyan
/// ähnlichen farbton, wenn man sie einnimmt, erhalten sie das eigene blau. das
/// haben wir so noch nicht und ist aus dem original.«
///
/// <para><b>Die Regel, Wort für Wort aus dem fünften Blitter</b> @0x4AC450
/// (Thunk 0x401AE1):</para>
/// <code>
///   cl = byte[ebp+0x14]        ; der Besitzer
///   shl cl, 2                  ; mal VIER
///   je Pixel:
///     al = Quellindex
///     cmp al, 0xFF  -> durchsichtig, nichts tun
///     dec al
///     test al, 0xF8 -> nur wenn (al-1) in 0..7, also QUELLE 1..8
///     shr al, 1                ; acht Malstufen werden VIER Palettenstufen
///     add al, cl               ; + Besitzer*4
///     inc al
/// </code>
/// <para>Also: <c>Ziel = ((Quelle − 1) &gt;&gt; 1) + 4·Besitzer + 1</c>.</para>
///
/// <para>⚠⚠ <b>Es gibt FÜNF Blitter in dieser Familie, und ich habe erst den
/// falschen gelesen.</b> @0x4AC2F0 nimmt denselben Besitzerparameter, rechnet
/// aber <c>shl cl,3</c> auf das Band 0..7 — also Schritt ACHT ohne Halbierung.
/// Damit kommt man auf die Paare (blau,grün), (rot,gelb), (orange,weiss),
/// (magenta,cyan), und aus BLAU kann niemals CYAN werden: die beiden liegen 28
/// Plätze auseinander, und 28 ist kein Vielfaches von 8. Genau daran ist meine
/// erste Deutung gescheitert — und sein Bildschirmfoto war der Widerspruch, der
/// sie umgeworfen hat: <b>dieselbe Beinpartie</b> desselben Läufers, einmal
/// blau und einmal cyan.
/// (<c>Bug Bilder/neutraleeinheitenfarbeundwechsel.png</c>.)</para>
///
/// <para>⭐ <b>Was entscheidet, welcher Blitter läuft:</b> <c>byte[0x504034]</c>
/// (@0x4AC0A1 / @0x4AC5C1). Es steht in <c>.data</c> auf <b>1</b>, und der
/// einzige Schreibzugriff im ganzen Programm (@0x4C461B) schreibt wieder 1.
/// Der ×8-Blitter ist also toter Code; es läuft <b>immer</b> der ×4. Das ist
/// die Stelle, an der eine gelesene Zahl allein nicht reicht — man muss auch
/// nachsehen, ob der Zweig je betreten wird.</para>
///
/// <para><b>Die Tafel</b> (Palettenplätze 1…32; in allen 27 NN.PAL identisch —
/// nachgezählt, 0..63 stimmen Byte für Byte überein; nur 40/90.PAL sind um
/// genau einen Platz verschoben und tragen dieselben Leitern):</para>
/// <code>
///   Besitzer 0   1..4    #536fff #3b5fe3 #2b53cb #1b47b3   BLAU   (der Spieler)
///   Besitzer 1   5..8    #67d75f #4bbf3b #37a71f #2b8f0b   gruen
///   Besitzer 2   9..12   #ff2b27 #e71717 #d30b07 #bf0000   rot
///   Besitzer 3  13..16   #f7ff0f #f3df0f #e7bf0f #df9f0f   gelb
///   Besitzer 4  17..20   #ff9b00 #eb8700 #d77700 #c76700   orange
///   Besitzer 5  21..24   #dfdbdb #cfc7c7 #bfb3b3 #afa3a3   weiss
///   Besitzer 6  25..28   #cf00cb #af00ab #8f008b #6f006f   magenta
///   Besitzer 7  29..32   #00f7cf #00dfb3 #07c79b #07af87   CYAN   (neutral)
/// </code>
///
/// <para>⭐ <b>Die Gegenprobe aus den Kartendaten:</b> map_01 belegt genau die
/// Blöcke 0, 1, 2 und <b>7</b> — Block 0 ist der Spieler (ein Fahrzeug auf
/// 4,39), Block 7 sind die <b>zehn</b> Einheiten im Feld (35..39, 41..45), die
/// er einnehmen soll. Blau und cyan, wie im Bild. Und der Besitzer ist keine
/// eigene Zahl: er IST der Tausenderblock des Platzes im Einheitenfeld
/// (8000 Plätze, 78 Byte je Eintrag ab 0x6E26C8; die Zeichenschleife @0x429967
/// rechnet ihn mit <c>Platz / 1000</c> aus und reicht ihn als Farbe weiter).</para>
///
/// <para><b>Warum das ohne Neubacken geht:</b> die acht Quellfarben kommen in
/// der Palette <b>nur je einmal</b> vor (nachgezählt über alle 256 Plätze), und
/// unsere ausgegebenen PNG tragen genau diese Bytes. Ein Bildpunkt ist damit
/// auch ohne Indexkarte eindeutig als Bandpunkt erkennbar. ⚠ Wer sich darauf
/// verlässt, muss es nachzählen — bei 21/33 (<c>#dfdbdb</c>) und 24/37
/// (<c>#afa3a3</c>) wäre es schiefgegangen; die liegen aber beide ausserhalb
/// des Bandes 1..8.</para>
///
/// <para><b>Anteil im Bild</b> (Stichprobe je 40 Dateien): Einheiten 8,5 %,
/// Fusssoldaten 14 %, Türme 11 %, Gebäude 0,45 %. ⚠ Die <b>Wirkungen</b>
/// (2,4 %) bleiben ausdrücklich UNGEFÄRBT — eine Explosion gehört keinem
/// Spieler, und das Original reicht dort auch keinen Besitzer hinein.</para>
///
/// <para>⚠⚠ <b>WAS NOCH FEHLT: DIE GEBÄUDE.</b> Das Original färbt sie mit
/// derselben Regel — @0x42B688 holt den Besitzer aus <c>0xC06915</c> und reicht
/// ihn an denselben Zeichner weiter, mit einem gelesenen Sonderfall: Besitzer
/// <b>11</b> geht als Farbe <b>10</b> hinein (@0x42B6A0, <c>push 0xa</c>), und
/// 10 landet über <c>4·10</c> auf den Palettenplätzen 41..44, den dunklen
/// Grautönen. Bei uns geht das <b>nicht</b> mit dieser Umfärbung, und der Grund
/// ist unsere eigene Bauweise: <see cref="DrawBuildingBody"/> stempelt aus
/// einem <b>gemeinsamen Kachelatlas</b> (<c>DrawTextureRectRegion</c>), und der
/// hat rund 50 Millionen Bildpunkte. Ein Atlas je Besitzer wäre der falsche
/// Preis für 0,45 % Bandpunkte. Der Weg dorthin ist entweder ein Schattierer
/// oder ein Umfärben nur der Kachelausschnitte, die ein Gebäude wirklich
/// benutzt — beides ist eigene Arbeit und ist hier ausdrücklich NICHT
/// getan.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die acht Quellfarben — Palettenplätze 1…8 aus NN.PAL.</summary>
    private static readonly uint[] Bandquelle =
    {
        0x536fff, 0x3b5fe3, 0x2b53cb, 0x1b47b3,
        0x67d75f, 0x4bbf3b, 0x37a71f, 0x2b8f0b,
    };

    /// <summary>Die Palettenplätze 1…32, in Vierergruppen je Besitzer.</summary>
    private static readonly uint[] Bandziel =
    {
        0x536fff, 0x3b5fe3, 0x2b53cb, 0x1b47b3,   // 0 blau
        0x67d75f, 0x4bbf3b, 0x37a71f, 0x2b8f0b,   // 1 gruen
        0xff2b27, 0xe71717, 0xd30b07, 0xbf0000,   // 2 rot
        0xf7ff0f, 0xf3df0f, 0xe7bf0f, 0xdf9f0f,   // 3 gelb
        0xff9b00, 0xeb8700, 0xd77700, 0xc76700,   // 4 orange
        0xdfdbdb, 0xcfc7c7, 0xbfb3b3, 0xafa3a3,   // 5 weiss
        0xcf00cb, 0xaf00ab, 0x8f008b, 0x6f006f,   // 6 magenta
        0x00f7cf, 0x00dfb3, 0x07c79b, 0x07af87,   // 7 cyan
    };

    /// <summary>Wie viele Parteien die Tafel trägt — acht, wie das
    /// Einheitenfeld Tausenderblöcke hat.</summary>
    public const int Parteien = 8;

    private static readonly Dictionary<(ulong, int), Texture2D?> _parteiTex = new();

    /// <summary>Wie viele Bilder umgefärbt wurden, wie viele Bildpunkte dabei
    /// im Band lagen, und wie viele Bilder GAR KEINEN Bandpunkt hatten.
    /// ⚠ Die letzte Zahl ist die wichtige: ein Bild ohne Bandpunkt sieht bei
    /// jedem Besitzer gleich aus, und dann ist »die Farbe wirkt nicht« kein
    /// Fehler der Umfärbung, sondern eine Lücke in der Ausgabe.</summary>
    public static int FarbeBilder, FarbePunkte, FarbeOhneBand;

    /// <summary>Ob überhaupt gefärbt wird — <c>--keine-parteifarbe</c> stellt
    /// den Stand von vor dem 24.08. wieder her, damit ein Rückschritt
    /// nachweisbar bleibt.</summary>
    public static bool KeineParteifarbe;

    /// <summary>
    /// Das Bild in der Farbe eines Besitzers. Besitzer 0 gibt das Bild
    /// unverändert zurück — seine Vierergruppe IST die Quellgruppe, die
    /// Umrechnung wäre die Kennabbildung.
    /// </summary>
    private static Texture2D? Parteifarbe(Texture2D? tex, int besitzer)
    {
        if (tex == null || KeineParteifarbe) return tex;
        // ⚠⚠ 25.08.2026, von ihm gemeldet: »im original haben die Einheiten die
        // Blaue Farbe und nicht Gruen wie wir«. Hier stand `besitzer <= 0`, also
        // »Besitzer 0 braucht keine Umfaerbung« - eine Abkuerzung, die nur
        // stimmt, wenn die Rohbilder schon blau sind. GEMESSEN ueber zwoelf
        // Ruempfe (f0.png): 136 blaue gegen 336 GRUENE Bandpunkte, und die
        // Ruempfe 12, 144 und 145 haben gar keine blauen. Der Spieler blieb
        // damit gruen. Das Original nimmt Besitzer 0 nicht aus - seine Rechnung
        // (Quelle-1)>>1 + 4*Besitzer ergibt fuer 0 genau die blaue Vierergruppe.
        if (besitzer < 0 || besitzer >= Parteien) return tex;    // 255 = herrenlos

        var key = (tex.GetRid().Id, besitzer);
        if (_parteiTex.TryGetValue(key, out var fertig)) return fertig;

        var img = tex.GetImage();
        if (img == null) { _parteiTex[key] = tex; return tex; }
        if (img.GetFormat() != Image.Format.Rgba8)
        {
            img = Image.CreateFromData(img.GetWidth(), img.GetHeight(), false,
                                       img.GetFormat(), img.GetData());
            img.Convert(Image.Format.Rgba8);
        }

        byte[] d = img.GetData();
        int treffer = 0;
        for (int i = 0; i + 3 < d.Length; i += 4)
        {
            if (d[i + 3] < 128) continue;
            uint rgb = (uint)(d[i] << 16 | d[i + 1] << 8 | d[i + 2]);
            for (int q = 0; q < 8; q++)
            {
                if (Bandquelle[q] != rgb) continue;
                // ⭐ Genau die Rechnung des Blitters: (Quelle-1)>>1 + 4·Besitzer,
                // hier schon als Platz IN der Vierergruppe.
                uint z = Bandziel[besitzer * 4 + (q >> 1)];
                d[i] = (byte)(z >> 16); d[i + 1] = (byte)(z >> 8); d[i + 2] = (byte)z;
                treffer++;
                break;
            }
        }

        Texture2D? aus;
        if (treffer == 0) { FarbeOhneBand++; aus = tex; }
        else
        {
            FarbeBilder++; FarbePunkte += treffer;
            aus = ImageTexture.CreateFromImage(
                      Image.CreateFromData(img.GetWidth(), img.GetHeight(), false,
                                           Image.Format.Rgba8, d));
        }
        _parteiTex[key] = aus;
        return aus;
    }

    /// <summary><c>--sprit-check</c>: jede eigene fahrende Einheit bekommt einen
    /// echten Fahrbefehl ueber den Befehlsring (denselben Weg wie ein
    /// Missionsbefehl), quer ueber die Karte. Danach sagt <see cref="SpritLine"/>,
    /// wieviel Sprit sie unterwegs gelassen haben.</summary>
    public string SpritCheckStart()
    {
        int n = 0;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || e.Owner != 0) continue;
            // Quer ueber die Karte: weit genug, dass ein Tank von 5 sicher leer
            // wird, und in eine Richtung, die von der Startecke wegfuehrt.
            int zx = System.Math.Min(e.Col + 40, (_nav?.Width  ?? 70) - 2);
            int zy = System.Math.Min(e.Row + 40, (_nav?.Height ?? 80) - 2);
            sb.Append($" | Platz {e.Slot} ({e.Col},{e.Row}) Sprit {e.Fuel}/{e.FuelMax} -> ({zx},{zy})");
            MissionOrderAt(e.Slot, zx, zy, -1);
            n++;
        }
        return $"sprit-check: {n} eigene Einheiten losgeschickt" + sb;
    }

    /// <summary>Was der Spritabzug getan hat — die Zahl, die belegt, dass er
    /// ueberhaupt greift (25.08.2026, siehe den Abzug bei PathIdx++).</summary>
    public string SpritLine()
    {
        int leer = 0, faehrt = 0; long summe = 0, voll = 0;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || !e.Mobile || e.Dead) continue;
            faehrt++; summe += e.Fuel; voll += e.FuelMax;
            if (e.Fuel <= 0) leer++;
        }
        return $"sprit: {faehrt} fahrende Einheiten, {summe} von {voll} Sprit "
             + (voll > 0 ? $"({100.0 * summe / voll:F0}%)" : "")
             + $", {leer} stehen trocken; {OhneSpritGemeldet} sind waehrend des Laufs leergefahren"
             + (OhneSpritGemeldet == 0 ? "   ⚠ 0 = der Abzug greift nicht oder es fuhr niemand" : "");
    }

    /// <summary>Die Meldezeile für <c>--quit-after</c>.</summary>
    public string ParteifarbeLine()
    {
        var zaehl = new int[Parteien];
        foreach (var e in _entities)
            if (!e.IsProp && e.Owner >= 0 && e.Owner < Parteien) zaehl[e.Owner]++;
        string[] name = { "blau", "gruen", "rot", "gelb", "orange", "weiss", "magenta", "cyan" };
        var sb = new System.Text.StringBuilder("parteifarbe: ");
        for (int b = 0; b < Parteien; b++)
            if (zaehl[b] > 0) sb.Append($"{b}={name[b]}:{zaehl[b]}  ");
        sb.Append($"| {FarbeBilder} Bilder umgefaerbt, {FarbePunkte} Punkte, "
                + $"{FarbeOhneBand} Bilder ohne Bandpunkt");
        if (KeineParteifarbe) sb.Append("   (--keine-parteifarbe: AUS)");
        if (FarbeBilder == 0 && !KeineParteifarbe)
            sb.Append("\n  ⚠ nichts umgefaerbt — entweder gehoert alles dem Besitzer 0, "
                    + "oder die Umfaerbung wird gar nicht gerufen");
        return sb.ToString();
    }
}
