namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐⭐ <b>DAS BRANDWESEN DER ZERSTÖRBAREN KARTENOBJEKTE</b> (24.08.2026).
///
/// <para>Gefunden über die Aufruferliste von <c>zapal_forestA</c> @0x4CA7E0:
/// sie hat DREI Aufrufer. Zwei gehören zum Waldbrand, der dritte (@0x4CA043)
/// läuft über eine eigene Liste — die der zerstörbaren Objekte. Die
/// vollständige Aufnahme steht in <c>BrennendeObjekte.cs</c>; hier ist der
/// Nachbau.</para>
///
/// <para><b>Die Daten.</b> Ein solches Objekt trägt im Belegungswert
/// <c>61000 + Index</c>, der Index zeigt in Sektion 4 der Kartendatei (6 Byte:
/// Spalte, Zeile, <b>Art</b>, Zustand — byteweise dieselbe Grösse wie das Feld
/// 0xC03A30 des Originals, 2000 × 6). Die Art zeigt in die Arttafel, Block 0x2b
/// der Kacheldatei (200 × 8): <c>+0</c> Verhaltensklasse 0/1/2, <c>+2</c>
/// Grundkachel. Alles davon wird beim Backen aufgelöst und liegt am
/// <see cref="Kartenobjekt"/>.</para>
///
/// <para>⭐ <b>Die Probe, die die ganze Kette bestätigt hat:</b> die Grundkachel
/// aus der Arttafel ist bei allen fünf Objekten von map_01 genau
/// <c>code − 10000</c> — zwei unabhängige Quellen, dieselbe Zahl. Damit sind
/// »brennt« (<c>+10001</c>) und »zerstört« (<c>+10002</c>) schlicht die beiden
/// nächsten Kachelcodes, und die liegen seit dem Neubacken als Ersatzbilder
/// vor: <b>1913 von 1913</b> Objekten haben beide.</para>
///
/// <para><b>Die Schadensbänder</b> @0x40D483…0x40D4C9:</para>
/// <code>
///   &gt; 80        ZERSTOEREN   (0x4CA750, Kachel + 10002)
///   41 … 80      ANZUENDEN    (0x4CA570, Kachel + 10001)
///   21 … 40      ANZUENDEN mit 1/3   (@0x40D4AB, rand()%3 == 0)
///   &lt;= 20       nichts
/// </code>
///
/// <para>⚠⚠ <b>UNSERE SETZUNG, und sie ist genau eine:</b> WAS in die Bänder
/// geht. Das Original rechnet @0x40D442 <c>((A + 128) · B) &gt;&gt; 7</c> aus zwei
/// Wörtern seines Zasah-Rahmens und würfelt <c>− rand()%5 + rand()%5</c> darauf.
/// Welche zwei Grössen A und B sind, habe ich <b>nicht</b> aufgelöst — dafür
/// müsste der ganze Rahmen von Zasah nachgezeichnet werden. Wir setzen den
/// Schaden selbst ein und behalten den Wurf. Die SCHWELLEN und die beiden
/// Handlungen sind gelesen; nur die Grösse, die man an ihnen misst, ist
/// ersetzt.</para>
///
/// <para><b>Der Takt</b> @0x4C9FC0 verzweigt nach der Verhaltensklasse. Gebaut
/// ist <b>Klasse 0</b> — sie ruft @0x4CA043 den Waldübergriff, ein brennendes
/// Objekt steckt also den Wald an. Über alle 36 Karten: Klasse 0 kommt
/// <b>1146</b> mal vor, Klasse 1 <b>756</b> mal, Klasse 2 <b>11</b> mal.
/// ⚠ Was 1 und 2 anders machen, ist NICHT gelesen (Sprungziele 0x4CA0FC und
/// 0x4CA17A) — sie brennen bei uns wie Klasse 0, nur ohne Übergriff. Das steht
/// hier, damit es nicht als »gebaut« durchgeht.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die Schwellen aus @0x40D483…0x40D4A9.</summary>
    private const int ObjZerstoeren = 80, ObjAnzuenden = 40, ObjWuerfeln = 20;

    /// <summary>Wie viele Objekte angezuendet bzw. zerstoert wurden, und wie oft
    /// eines den Wald angesteckt hat. ⚠ Ohne die Zahlen ist »es passiert nichts«
    /// nicht von »es wurde nie getroffen« zu unterscheiden.</summary>
    public int ObjAngezuendet, ObjZerstoert, ObjUebergriffe;

    /// <summary>Der Treffer auf ein zerstoerbares Kartenobjekt — die Bänder von
    /// @0x40D442. Gibt true, wenn etwas geschehen ist.</summary>
    private bool ObjektTreffer(int col, int row, int schaden)
    {
        var e = ObjektAn(col, row);
        if (e == null || e.Abgebrannt) return false;

        // ⚠ Der Wurf ist gelesen (@0x40D45F/@0x40D471), die Groesse darunter
        // ist unsere — siehe Klassenkopf.
        int wert = schaden - Simulation.Determinism.Roll(5) + Simulation.Determinism.Roll(5);

        if (wert > ObjZerstoeren) { ObjektZerstoeren(e); return true; }
        if (wert > ObjAnzuenden) return ObjektAnzuenden(e);
        if (wert > ObjWuerfeln)
            return Simulation.Determinism.Roll(3) == 0 && ObjektAnzuenden(e);
        return false;
    }

    private Kartenobjekt? ObjektAn(int col, int row)
    {
        foreach (var e in _objDraw)
            if (e.Col == col && e.Row == row && e.IstObjekt) return e;
        return null;
    }

    /// <summary>»hori« @0x4CA570 — die Kachel wird zu <c>Grundkachel + 10001</c>
    /// und das Objekt brennt.</summary>
    private bool ObjektAnzuenden(Kartenobjekt e)
    {
        if (e.BrandVon >= 0f || !e.HatKohle) return false;
        e.BrandVon = (float)DebugClock;
        // ⚠ Die Branddauer eines OBJEKTS ist nicht gelesen; wir nehmen die des
        // Waldes, weil sie aus demselben Zustandszaehler kommt und dieselbe
        // Groessenordnung haben muss. Ausdruecklich unsere Setzung.
        int zustand = Simulation.Determinism.Range(Import.MapForest.BrandZustandVon,
                                                   Import.MapForest.BrandZustandBis);
        e.BrandDauer = (Import.MapForest.BrandEnde - zustand) * Import.MapForest.BrandTakt
                       / OriginalTicksPerSecond;
        e.Steht = false;                 // danach kommt die ZERSTOERT-Kachel
        ObjectsBurning++;
        ObjAngezuendet++;
        GD.Print($"objekt: ({e.Col},{e.Row}) Art {e.Art} Klasse {e.Klasse} angezuendet, "
               + $"brennt {e.BrandDauer:0.0}s (Kachel {e.Basis + 10001})");
        return true;
    }

    /// <summary>@0x4CA750 — die Kachel wird zu <c>Grundkachel + 10002</c>.</summary>
    private void ObjektZerstoeren(Kartenobjekt e)
    {
        if (e.BrandVon >= 0f) ObjectsBurning--;
        e.BrandVon = -1f;
        e.Abgebrannt = true;
        e.Steht = false;
        ObjZerstoert++;
        GD.Print($"objekt: ({e.Col},{e.Row}) Art {e.Art} zerstoert (Kachel {e.Basis + 10002})");
    }

    /// <summary>
    /// Der Takt der brennenden Objekte — @0x4C9FC0. <b>Klasse 0</b> ruft den
    /// Waldübergriff (@0x4CA043); Klasse 1 und 2 sind ungelesen und brennen
    /// hier nur ab.
    /// </summary>
    private void ObjektBrandTakt()
    {
        foreach (var e in _objDraw)
        {
            if (!e.IstObjekt || e.BrandVon < 0f || e.Abgebrannt) continue;

            // Klasse 0: das brennende Objekt steckt einen Nachbarn an —
            // dieselbe Routine und dieselbe Windformel wie beim Wald.
            if (e.Klasse == 0 && WindDir >= 0)
            {
                int richtung = Simulation.Determinism.Roll(8);
                int ab = System.Math.Abs(richtung - WindDir);
                if (ab > 4) ab = 8 - ab;
                int nenner = 2 * (5 * ((9 - WindStrength) * ab) + 50);
                if (Simulation.Determinism.Roll(nenner) == 0)
                {
                    var (dc, dr) = Achtel[richtung];
                    if (Anzuenden(e.Col + dc, e.Row + dr)) ObjUebergriffe++;
                }
            }

            // Ausgebrannt -> die ZERSTOERT-Kachel, wie bei Zasah mit viel Schaden.
            if ((float)DebugClock - e.BrandVon >= e.BrandDauer)
            {
                ObjectsBurning--;
                e.BrandVon = -1f;
                e.Abgebrannt = true;
                e.Steht = false;
                GD.Print($"objekt: ({e.Col},{e.Row}) Art {e.Art} abgebrannt");
            }
        }
    }

    /// <summary>Die Meldezeile.</summary>
    public string ObjektBrandLine()
    {
        int n = 0, brennt = 0, hin = 0;
        foreach (var e in _objDraw)
        {
            if (!e.IstObjekt) continue;
            n++;
            if (e.Abgebrannt) hin++;
            else if (e.BrandVon >= 0f) brennt++;
        }
        return $"objektbrand: {n} zerstoerbare Objekte auf der Karte, {brennt} brennen, "
             + $"{hin} hin; {ObjAngezuendet} angezuendet, {ObjZerstoert} zerstoert, "
             + $"{ObjUebergriffe} Uebergriffe auf den Wald"
             + (n == 0 ? "   ⚠ keine auf dieser Karte — die Nullen sagen nichts" : "");
    }
}
