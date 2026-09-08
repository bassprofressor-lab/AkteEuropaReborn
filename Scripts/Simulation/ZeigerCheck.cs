namespace AkteEuropaReborn.Rendering;

using Godot;
using System.Text;

/// <summary>
/// <b><c>--zeiger-check</c> — WELCHES ZEIGERBILD ueber welchem GEBAEUDE?</b>
/// (08.09.2026, zu seinen zwei Meldungen vom selben Tag.)
///
/// <para>»was mich stoert, ist ein angriff icon ueber dem nachschubposten …
/// das scheint mir ja eher eine neutrale einheit/gebaeude zu sein« und »es
/// gibt sogar ein 'Einnahme Icon' fuer Gebaeude. Bei uns ist auch auf
/// Gebaeuden wie Basis/Fabriken immer zuerst das Attack Icon.«</para>
///
/// <para>Der Pruefstand fragt <see cref="MapEntityLayer.CursorHintAt"/> — also
/// dieselbe Stelle, die auch der Mauszeiger fragt, und nicht eine nachgebaute
/// Regel (Arbeitsweise 24). Er waehlt dafuer ein eigenes FAHRZEUG an, weil der
/// Einnahmezweig des Originals (@0x4323F5) genau das verlangt: das Klassenbyte
/// <c>+0x0A</c> der gewaehlten Einheit muss 0 sein.</para>
///
/// <para><b>Drei Aussagen, jede kann scheitern:</b></para>
/// <list type="number">
///   <item>ueber der TUER eines fremden, nicht verbuendeten Gebaeudes steht
///   <c>Einnahme</c> (Zeigerart 6 → Bild 10);</item>
///   <item>ueber seiner MITTE steht weiter <c>Enemy</c> (Zeigerart 10, die in
///   der Tafel @0x4A9BEC mit 2 auf dasselbe Bild faellt);</item>
///   <item>ueber einem HERRENLOSEN Gebaeude steht nirgends das Angriffsbild —
///   das Original gibt ihm Zeigerart 1 (@0x43253A), und eine Zeile in der
///   Buendnistafel hat Besitzer 255 gar nicht.</item>
/// </list>
///
/// <para>⚠ Das Nullmodell ist <c>--gebaeudezeiger-alt</c>: damit muss dieselbe
/// Zeile DURCHFALLEN, sonst misst der Pruefstand nichts.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string ZeigerCheckLine()
    {
        var sb = new StringBuilder("zeiger-check\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();

        // ---- ein eigenes Fahrzeug anwaehlen, sonst gibt es keinen Zeiger ----
        int fahrzeug = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead) continue;
            if (u.Owner != ViewPlayer || u.GameUnitType != 0) continue;
            fahrzeug = i; break;
        }
        if (fahrzeug < 0)
            return sb.Append("  kein eigenes Fahrzeug auf dieser Karte — "
                           + "der Einnahmezweig verlangt Klassenbyte 0, ungeprueft").ToString();
        var merken = new System.Collections.Generic.List<int>(_sel);
        _sel.Clear(); _sel.Add(fahrzeug);
        // ⚠ Pick() laesst nichts durch den Nebel — und im kopflosen Lauf liegt
        // fast die ganze Karte darin. Ohne diese Zeile meldet der Pruefstand
        // ueberall »Ground« und behauptet damit einen Fehler, den er selbst
        // gemacht hat. Dieselbe Vorkehrung wie in FussvolkProbe.
        bool nebelVor = PickOhneNebel;
        PickOhneNebel = true;
        sb.AppendLine($"  gewaehlt: Platz {_entities[fahrzeug].Slot} "
                    + $"({_entities[fahrzeug].Name}, +0x0a 0)");

        int fremdTuer = 0, fremdTuerFalsch = 0, fremdMitte = 0, fremdMitteFalsch = 0;
        int herrenlos = 0, herrenlosFalsch = 0;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            var tuer = MitteVon(b.Col + b.DoorCol, b.Row + b.DoorRow);
            var mitte = MitteVon(b.Col + Mathf.Max(1, b.FootW) / 2,
                                 b.Row + Mathf.Max(1, b.FootH) / 2);
            var hTuer = CursorHintAt(tuer);
            var hMitte = CursorHintAt(mitte);
            string art = b.Owner == ViewPlayer ? "eigen"
                       : b.Owner is < 0 or > 7 ? "herrenlos" : "fremd";
            sb.AppendLine($"  Gebaeude {b.Slot,3} Art {b.BType,2} ({art,9}, Besitzer {b.Owner,3}): "
                        + $"Tuer {hTuer}, Mitte {hMitte}");
            if (art == "fremd")
            {
                fremdTuer++;
                if (hTuer != Hint.Einnahme) fremdTuerFalsch++;
                fremdMitte++;
                if (hMitte != Hint.Enemy) fremdMitteFalsch++;
            }
            else if (art == "herrenlos")
            {
                herrenlos++;
                if (hTuer == Hint.Enemy || hMitte == Hint.Enemy) herrenlosFalsch++;
            }
        }

        _sel.Clear(); foreach (int k in merken) _sel.Add(k);
        PickOhneNebel = nebelVor;

        sb.AppendLine($"  fremde Gebaeude: {fremdTuer}, davon ohne Einnahmezeiger auf der Tuer: "
                    + $"{fremdTuerFalsch}");
        sb.AppendLine($"  fremde Gebaeude: {fremdMitte}, davon ohne Angriffszeiger in der Mitte: "
                    + $"{fremdMitteFalsch}");
        sb.AppendLine($"  herrenlose Gebaeude: {herrenlos}, davon mit Angriffszeiger: "
                    + $"{herrenlosFalsch}");
        if (MapEntityLayer.GebaeudezeigerAlt)
            sb.AppendLine("  ⚠ NULLMODELL --gebaeudezeiger-alt: die Zeilen MUESSEN hier "
                        + "durchfallen, sonst misst der Pruefstand nichts");

        bool alles = fremdTuerFalsch == 0 && fremdMitteFalsch == 0 && herrenlosFalsch == 0
                  && (fremdTuer > 0 || herrenlos > 0);
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Die Bildmitte einer Zelle — der Zeiger wird in Kartenpunkten
    /// gefragt, nicht in Zellen.</summary>
    private Vector2 MitteVon(int col, int row)
        => new(_ox + (col + 0.5f) * TileW,
               _oy + row * TileH - ElevOf(col, row) * 15 + TileH * 0.5f);
}
