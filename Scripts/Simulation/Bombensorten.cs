namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>DIE DREI BOMBENSORTEN UND DER KNOPF, DER SIE DREHT</b> (19.09.2026, nach
/// <c>berichte/flughafenfenster-k20-fable.md</c> §4).
///
/// <para>Seine Frage war: »Bombe wechseln bei Bombern, keine ahnung was es da
/// für unterschied gibt am ende«. Der Unterschied ist erheblich — eine der drei
/// Sorten ist überhaupt keine Waffe.</para>
///
/// <para><b>GELESEN</b> (C, und in F byteweise gleich — Geschosstafel C
/// <c>0x4F98E8</c> / F <c>0x4F88F0</c>, Wechsler C <c>0x4513F0</c> / F
/// <c>0x4500A0</c>):</para>
/// <code>
///   Art 45  Gasbombe     Tempo 3  Flugbild 220  Einschlag 120  Klang 16
///                        Zasah normal  +  30 x »Add gas« (Stärke 250)
///   Art 46  Löschmittel  Tempo 3  Flugbild 220  Einschlag 121  Klang 16
///                        Zasah: 0x4CA600(x, y) und ENDE — KEIN Schaden (@0x40CC15)
///   Art 47  Bombe        Tempo 3  Flugbild 220  Einschlag 309 (leer)
///                        Einschlagklang 399 -> 400 + rand % 6 (@0x4048F8)
///                        Zasah normal  +  0x4A98A0 (UNGELESEN)
/// </code>
///
/// <para><b>Der Wechsler</b> (Befehl 534, Arm <c>0x4C3D32</c>): steht die
/// Staffel auf <c>0xFF</c>, dreht er <b>nur die gewählte Maschine</b>
/// 45→46→47→45 (@0x451413). Steht sie auf einer Nummer, dreht er
/// <b>jeden Bomber der Staffel</b> (@0x45144F) — die gewählte ist darunter,
/// weil der Knopf ohnehin nur bei einem Bomber erscheint.</para>
///
/// <para>⭐⭐ <b>Das Nullmodell aus den DATEN, und es ist das Wichtigste hier:</b>
/// in sec19 aller 23 Kampagnendateien des Projekts (01–15, NET01–08) steht
/// <b>ein einziges</b> vorgesetztes Flugzeug — und <b>kein Bomber</b>. Die
/// Schreiber von <c>+0x2C</c> sind <c>spawn_aircraft</c> (setzt 47) und dieser
/// Wechsler. <b>Die Sorten 45 und 46 entstehen ausschliesslich am Knopf.</b>
/// Wer sie in einer Kartendatei sucht, sucht umsonst; wer sie ohne den Knopf
/// baut, baut etwas, das im Spiel nie vorkommt.</para>
///
/// <para><b>UNSERE SETZUNGEN, benannt:</b></para>
/// <list type="bullet">
///   <item>Das fünfte Argument von <c>Add gas</c> (<b>250</b>) hat bei uns
///   keinen Platz: unser <c>GasAnlegen</c> nimmt vier (x, y, vx, vy), genau die
///   vier, die auch der Gaswerfer-Pfad <c>0x439C50</c> übergibt. Was die 250
///   im fünften Feld bewirkt, ist <b>UNGELESEN</b> — sie wird deshalb nicht
///   erfunden, sondern fortgelassen, und das steht hier.</item>
///   <item>Die Nebenwirkung von Art 47 (<c>0x4A98A0</c>, dieselbe wie bei
///   Raketen 5/6, Flak 17 und M-Bombe 20) ist <b>UNGELESEN</b> — sie prüft
///   <c>imap == 0xFFFE</c>, alles Weitere ist offen. Nicht gebaut.</item>
///   <item>Die Einschlagfolgen <b>120</b> und <b>121</b> (je 11 Bilder,
///   25×10 → 36×25) müssen ausgegeben werden. Fehlen sie, zeichnet der
///   Einschlag nichts — und das <b>sagt der Prüfstand auch</b>, statt es
///   stillschweigend gut aussehen zu lassen.</item>
///   <item>Beim Löschtrupp ist der Einzelobjekt-Zweig <c>+0x02 == 0</c> auf
///   unser <c>Klasse == 0</c> abgebildet. Dass <c>+0x02</c> wirklich die
///   Verhaltensklasse ist und nicht die Art, ist eine <b>Annahme</b> —
///   sec4-Satzlänge 6, und wir tragen nur Art und Klasse.</item>
/// </list>
///
/// <para>Gegenschalter <c>--bombe-fest-47</c> (der Knopf ist gesperrt, jeder
/// Bomber trägt 47 — der Stand vor dem 19.09.) und
/// <c>--bombenetikett-kaputt</c> (ahmt den Fehler des Originals nach, siehe
/// <see cref="MapEntityLayer.Special.Waffenart"/>). Prüfstand
/// <c>--bombe-check</c>.</para>
/// </summary>
public static class Bombensorten
{
    /// <summary>Art 45 — Einschlag 120, dazu die Gaswolken.</summary>
    public const int Gas = 45;

    /// <summary>Art 46 — Einschlag 121, KEIN Schaden, dafür der Löschtrupp.</summary>
    public const int Loeschmittel = 46;

    /// <summary>Art 47 — die normale Bombe, mit der ein Bomber aus dem Werk
    /// kommt.</summary>
    public const int Normal = 47;

    /// <summary>Die Drehung des Knopfes: 45 → 46 → 47 → 45. Alles andere bleibt
    /// liegen (@0x451413: drei <c>cmp</c>, kein <c>else</c>).</summary>
    public static int Weiter(int art) => art switch
    {
        Gas => Loeschmittel,
        Loeschmittel => Normal,
        Normal => Gas,
        _ => art,
    };

    /// <summary>Wie das Original die Sorte in der HANGARZEILE benennt
    /// (@0x465CE6, Vergleich gegen 0x2D/0x2E/0x2F). ⚠ Mit dem führenden
    /// Leerzeichen, das im Zeichner hinter dem Namen steht.</summary>
    public static string Wort(int art) => art switch
    {
        Gas => " Gasbombe",
        Loeschmittel => " Löschmittel",
        Normal => " Bombe",
        _ => "",
    };

    /// <summary>Die Einschlagfolge je Sorte — 120, 121, und bei 47 die leere
    /// 309, die das Original zeichnet, also nichts.</summary>
    public static int Einschlagfolge(int art) => art switch
    {
        Gas => 120,
        Loeschmittel => 121,
        _ => -1,
    };
}

public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--bombe-fest-47</c> — der Knopf ist gesperrt und jeder
    /// Bomber trägt die normale Bombe. Nullmodell für alles in dieser
    /// Datei: unter ihm muss <see cref="BombenGas"/> auf 0 stehen und
    /// <see cref="BombenLoesch"/> ebenso, und die Schadenssumme muss sich
    /// gegenüber dem Normalfall NICHT ändern.</summary>
    public static bool BombeFest47;

    /// <summary><c>--bombenetikett-kaputt</c> — ahmt den Fehler des Originals
    /// nach: das Etikett unter dem Flugzeugbild (@0x466690) vergleicht 0/1/2
    /// statt 45/46/47 und zeigt darum nie die richtige Sorte.</summary>
    public static bool BombenetikettKaputt;

    /// <summary>Wie oft der Knopf gedreht hat, wie viele Maschinen dabei
    /// umgestellt wurden, wie viele Gaswolken eine Gasbombe gelegt hat, wie oft
    /// ein Löschmittel gelöscht hat, und wie oft ein Löschmittel den Schaden
    /// unterdrückt hat.</summary>
    public int BombenDrehungen, BombenUmgestellt, BombenGas, BombenLoesch,
               BombenSchadenUnterdrueckt;

    /// <summary>Wie viele Zellen der Löschtrupp wirklich gelöscht hat — Wald
    /// und Einzelobjekt getrennt, weil das zwei Zweige des Originals sind.</summary>
    public int LoeschWald, LoeschObjekt;

    /// <summary>
    /// <b>Befehl 534 — »Bombe wechseln«</b>, der Wechsler <c>0x4513F0</c>.
    ///
    /// <para><paramref name="platz"/> ist der sec19-Platz der gewählten
    /// Maschine (<c>+0x0A</c> des Befehls), <paramref name="flughafen"/> ihr
    /// Gebäudeplatz (<c>+0x08</c> = die sec27-Nummer im Original).</para>
    /// </summary>
    public void BombeWechseln(int platz, int flughafen)
    {
        if (BombeFest47) return;
        var gewaehlt = FlugzeugAufPlatz(platz);
        if (gewaehlt == null || !gewaehlt.IstBomber) return;

        BombenDrehungen++;

        // Keine Staffel: nur diese eine Maschine (@0x451413).
        if (gewaehlt.Staffel == 0xFF)
        {
            gewaehlt.Waffenart = Bombensorten.Weiter(gewaehlt.Waffenart);
            BombenUmgestellt++;
            return;
        }

        // Staffel gesetzt: JEDER Bomber der Staffel an diesem Flughafen
        // (@0x45144F — die Schleife läuft über die Stellplätze des Flughafens
        // bis zum ersten 0xFF, nicht über alle Flugzeuge der Karte).
        foreach (var a in _special)
        {
            if (a.Dead || a.Kind != 2) continue;
            if (a.HomeSlot != flughafen) continue;
            if (a.Staffel != gewaehlt.Staffel) continue;
            a.Waffenart = Bombensorten.Weiter(a.Waffenart);
            BombenUmgestellt++;
        }
    }

    private Special? FlugzeugAufPlatz(int platz)
    {
        foreach (var a in _special) if (a.Slot == platz && !a.Dead) return a;
        return null;
    }

    /// <summary>
    /// <b><c>0x4554F0</c> — die Nebenwirkung des Aufschlags</b>, gerufen an
    /// BEIDEN Aufschlagwegen des Geschosstakts (<c>0x452F92</c>,
    /// <c>0x452FED</c>), nach dem Einschlagbild und dem Klang.
    ///
    /// <para>Die Weiche ist eine Tafel über die Geschossarten 1…47 (Index
    /// <c>0x4556DC</c>, Tafel <c>0x4556B0</c>). Hier stehen nur die beiden
    /// Arme, die gelesen sind; Art 47 führt auf <c>0x4A98A0</c>, und das ist
    /// ungelesen — deshalb steht hier kein Arm dafür und wird auch keiner
    /// erfunden.</para>
    /// </summary>
    private void BombenNebenwirkung(int art, int col, int row, int fx, int fy)
    {
        if (BombeFest47) return;

        if (art == Bombensorten.Gas)
        {
            // @0x455658: dreissigmal Add gas(x·120 + fx + 20, y·120 + fy, 0, 0, 250)
            // — dieselbe Form wie die Entwicklertaste, nur 30 statt 50 Wolken.
            // ⚠ Das fünfte Argument (250) hat bei uns keinen Platz, siehe
            // Klassenkopf.
            for (int i = 0; i < GasWolkenJeBombe; i++)
            {
                GasAnlegen(col * GasFein + fx + 20, row * GasFein + fy, 0, 0);
                BombenGas++;
            }
        }
        // Art 46 tut hier NICHTS (@0x4556AB) — sein Löschtrupp hängt am
        // Schadensweg, nicht an der Nebenwirkung. Siehe LoeschtruppAus.
    }

    /// <summary>Dreissig Wolken je Gasbombe — <c>bl = 0x1E</c> @0x455658.
    /// (Die Entwicklertaste legt 50, siehe Gaswerfer.cs.)</summary>
    private const int GasWolkenJeBombe = 30;

    /// <summary>
    /// <b><c>0x4CA600</c> — DER LÖSCHTRUPP</b>, der ganze Zweck von Art 46.
    ///
    /// <para>3×3 um die Einschlagzelle. Zwei Zweige, wie im Original:</para>
    /// <list type="bullet">
    ///   <item><b>Wald</b> (imap 50000…56000): <c>byte[0xBFF3E2 + 3·(v−50000)]
    ///   &gt; 1 → := 1</c> — der Zustand über 1 ist ein BRAND, die 1 ist »brennt
    ///   nicht«. Bei uns: <c>BrandVon := −1</c>. ⚠ Eine Zelle, die schon
    ///   durchgebrannt ist, bleibt durchgebrannt — das Original setzt nur den
    ///   Zustand zurück und macht keine Bäume wieder heil.</item>
    ///   <item><b>Einzelobjekt</b> (imap 61000…64000, sec4): <c>+0x02 == 0</c>
    ///   und <c>+0x03 ≠ 0</c> → <c>+0x03 := 0</c>. Siehe Klassenkopf zur
    ///   Abbildung von <c>+0x02</c>.</item>
    /// </list>
    /// </summary>
    private void LoeschtruppAus(int col, int row)
    {
        int geloescht = 0;
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                int c = col + dc, r = row + dr;
                foreach (var e in _objDraw)
                {
                    if (e.Col != c || e.Row != r) continue;
                    if (e.BrandVon < 0f) continue;              // brennt nicht
                    if (e.IstWald)
                    {
                        if (e.Abgebrannt) continue;             // durch ist durch
                        e.BrandVon = -1f;
                        LoeschWald++; geloescht++;
                    }
                    else if (e.IstObjekt && e.Klasse == 0)
                    {
                        e.BrandVon = -1f;
                        LoeschObjekt++; geloescht++;
                    }
                }
            }
        if (geloescht > 0) BombenLoesch++;
    }

    /// <summary>Die Messzeile — <c>--bombe-check</c>.
    ///
    /// <para>⚠ Die KONTROLLZAHL ist <see cref="BombenDrehungen"/>: sie muss
    /// unter <c>--bombe-fest-47</c> auf 0 fallen, während die Zahl der
    /// EINSCHLÄGE gleich bleiben muss. Ohne eine Zahl, die sich NICHT ändern
    /// darf, sieht jede A/B hier richtig aus — das ist am 20.09. dreimal an
    /// einem Tag passiert.</para></summary>
    public string BombeAuskunft()
    {
        if (BombenDrehungen == 0 && BombenGas == 0 && BombenLoesch == 0
            && BombenSchadenUnterdrueckt == 0) return "";
        return $"bombe-check: {BombenDrehungen}x gedreht ({BombenUmgestellt} Maschinen), "
             + $"{BombenGas} Gaswolken (Soll {GasWolkenJeBombe} je Gasbombe), "
             + $"{BombenLoesch}x geloescht ({LoeschWald} Wald, {LoeschObjekt} Objekte), "
             + $"{BombenSchadenUnterdrueckt}x Schaden unterdrueckt (Loeschmittel)"
             + (BombeFest47 ? "   [--bombe-fest-47: alles 0 ist das SOLL]" : "");
    }
}
