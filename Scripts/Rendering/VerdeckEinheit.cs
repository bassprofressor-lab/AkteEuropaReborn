namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐⭐ <c>--verdeck-einheit</c> (24.08.2026) — <b>WAS LIEGT ÜBER DIESER
/// EINHEIT?</b>
///
/// <para>Gemeldet, viermal in Folge und jedes Mal nach einer Behebung, die es
/// nicht war: »dort glitchen auch Cyborgs wie unter die Karte«, »war wieder kurz
/// im Boden verschwunden«, »ist immer noch so«.</para>
///
/// <para>⚠⚠ <b>Und das ist die Lehre aus diesen vier Anläufen.</b> Ich habe
/// nacheinander die Zeilenverzögerung der Flamme, die Nebelschicht, die
/// Geländehöhe im Sortierkriterium und den Boden ausgebrannter Zellen
/// verdächtigt — jedes Mal mit einer plausiblen Begründung, jedes Mal ohne zu
/// messen, WAS tatsächlich darüberliegt. Drei davon waren echte Fehler und
/// gehörten behoben; keiner war DIESER. <b>Eine plausible Ursache ist keine
/// gemessene.</b></para>
///
/// <para>Diese Zeile nennt sie beim Namen: sie rechnet für jede Einheit das
/// Rechteck ihres GEZEICHNETEN Bildes aus und listet alles auf, was danach an
/// dieselbe Stelle gemalt wird — mit Art, Zelle und Rechteck.</para>
///
/// <para><b>Die Reihenfolge, gegen die geprüft wird</b> (aus DrawRailAndBuildings,
/// je Zeile r):</para>
/// <code>
///   (1) Gleis   (2) EINHEITEN bis r   (3) Gebäude   (4) aufragende Kacheln bis r
///   (5) Flammen (um FlammenVerzug nachhängend)
/// </code>
/// <para>Eine aufragende Kachel der Zeile r wird also NACH einer Einheit
/// derselben Zeile gezeichnet — genau dort ist der Verdacht.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>⭐ Wie oft einem LEBENDEN Fusssoldaten das Bild fuer seine
    /// Richtung und seinen Block fehlte — und wie oft ausgewichen werden
    /// konnte. ⚠ Ohne diese Zahl sieht »er ist unsichtbar« genauso aus wie »er
    /// wird verdeckt«, und genau daran habe ich vier Anlaeufe verloren.</summary>
    public int InfBildFehlt, InfBildErsetzt;

    public readonly System.Collections.Generic.List<string> InfBildFaelle = new();

    /// <summary>Die Meldezeile dazu.</summary>
    public string InfBildLine()
        => InfBildFehlt == 0
            ? "inf-bild: kein fehlendes Fusssoldatenbild"
            : $"inf-bild: ⚠ {InfBildFehlt}x fehlte das Bild, {InfBildErsetzt}x ausgewichen, "
              + $"{InfBildFehlt - InfBildErsetzt}x UNSICHTBAR — "
              + string.Join("; ", InfBildFaelle);

    public string VerdeckEinheitLine()
    {
        var sb = new System.Text.StringBuilder("verdeck-einheit:\n");
        int geprueft = 0, betroffen = 0;

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            var tex = AuswahlBild(e);
            if (tex == null) continue;
            geprueft++;
            var oben = PictureAnchor(e) - ComposedAnchor;
            var rechteck = new Rect2(oben, tex.GetSize());

            var treffer = new System.Collections.Generic.List<string>();
            foreach (var o in ObjekteFuerVerdeck())
            {
                // Alles mit Zeile >= der Einheitenzeile wird SPAETER gezeichnet
                // (Schritt 4 nach Schritt 2 desselben Faches).
                if (o.Row < e.Row) continue;
                if (!o.Rechteck.Intersects(rechteck)) continue;
                var schnitt = o.Rechteck.Intersection(rechteck);
                treffer.Add($"{o.Art} ({o.Col},{o.Row}) Zeile {(o.Row == e.Row ? "GLEICH" : "+" + (o.Row - e.Row))}"
                          + $", ueberdeckt {schnitt.Size.X:0}x{schnitt.Size.Y:0} px");
            }
            if (treffer.Count == 0) continue;
            betroffen++;
            if (betroffen <= 6)
            {
                sb.Append($"  Einheit #{i} (P{e.Owner}) auf ({e.Col},{e.Row}), Bild {rechteck.Size.X:0}x{rechteck.Size.Y:0} "
                        + $"bei {rechteck.Position}\n");
                foreach (var t in treffer) sb.Append($"      ueberdeckt von {t}\n");
            }
        }

        sb.Append($"  {betroffen} von {geprueft} Einheiten mit Bild werden von spaeter "
                + "gezeichneten Kacheln ueberdeckt");
        if (betroffen == 0)
            sb.Append("   ⚠ keine — dann liegt es NICHT an den Kartenobjekten");
        return sb.ToString();
    }
}
