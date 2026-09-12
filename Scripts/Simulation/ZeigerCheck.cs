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
        // ⭐ 12.09.2026, bug-209: das ZIVILE Gebaeude (Besitzer 11) ist der
        // Preis einer Eroberungskarte und war hier bisher mit der 255 in einem
        // Topf — auf map_NET02 sind das ALLE 52 Gebaeude mit Tuer.
        int zivilTuer = 0, zivilTuerFalsch = 0;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            var tuer = MitteVon(b.Col + b.DoorCol, b.Row + b.DoorRow);
            var mitte = MitteVon(b.Col + Mathf.Max(1, b.FootW) / 2,
                                 b.Row + Mathf.Max(1, b.FootH) / 2);
            var hTuer = CursorHintAt(tuer);
            var hMitte = CursorHintAt(mitte);
            string art = b.Owner == ViewPlayer ? "eigen"
                       : b.Owner == NeutralOwner ? "zivil"
                       : b.Owner is < 0 or > 7 ? "herrenlos" : "fremd";
            sb.AppendLine($"  Gebaeude {b.Slot,3} Art {b.BType,2} ({art,9}, Besitzer {b.Owner,3}): "
                        + $"Tuer {hTuer}, Mitte {hMitte}");
            if (art == "fremd")
            {
                // ⚠ Nur ein Gebaeude MIT Tuer traegt die Marke 0x63 — ein
                // tuerloses (Kraftwerk, Seedock, Radarstellung) darf den
                // Einnahmezeiger gar nicht bekommen, siehe EinnahmezeigerGilt.
                if (b.Doors != 0 && b.Built != 0)
                {
                    fremdTuer++;
                    if (hTuer != Hint.Einnahme) fremdTuerFalsch++;
                }
                fremdMitte++;
                if (hMitte != Hint.Enemy) fremdMitteFalsch++;
            }
            else if (art == "zivil")
            {
                // Die Tuer MUSS den Einnahmezeiger tragen — das ist der Preis
                // der Karte. Die Mitte bleibt neutral: ein einfacher Klick soll
                // nicht die Fabrik beschiessen, die man erobern will (Strg
                // greift weiter an).
                if (b.Doors != 0 && b.Built != 0)
                {
                    zivilTuer++;
                    if (hTuer != Hint.Einnahme) zivilTuerFalsch++;
                }
                if (hMitte == Hint.Enemy) zivilTuerFalsch++;
            }
            else if (art == "herrenlos")
            {
                herrenlos++;
                if (hTuer == Hint.Enemy || hMitte == Hint.Enemy) herrenlosFalsch++;
            }
        }

        // ---- 4) UND WAS DER KLICK DARAUS MACHT -------------------------
        // ⚠⚠ Seine Meldung vom 08.09.2026: »das einnahme icon fuehrt aber nicht
        // zur einnahme, sondern die sagen angriff, schiessen aber nicht«. Ein
        // Bild, das etwas verspricht, was der Klick nicht tut, ist schlimmer
        // als gar keins — also wird der KLICKWEG hier mitgemessen, nicht nur
        // das Bild. ⚠ Der Aufruf setzt wirklich einen Befehl ab; das ist der
        // Zweck (der Pruefstand darf die Frage nicht nachbauen), macht ihn aber
        // zu einem Eingriff und nicht zu einer blossen Ablesung.
        bool klickOk = true;
        Entity? probe = null;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (b.Owner == ViewPlayer) continue;
            // ⭐ 12.09.2026: das ZIVILE Gebaeude gehoert ausdruecklich dazu — auf
            // einer Eroberungskarte ist es der einzige Klickweg, den es gibt.
            if (b.Owner != NeutralOwner && b.Owner is < 0 or > 7) continue;
            if (b.Doors == 0 || b.Built == 0) continue;
            probe = b; break;
        }
        if (probe == null)
            sb.AppendLine("  kein fremdes Gebaeude mit Tuer — der Klickweg bleibt ungeprueft");
        else
        {
            var tuer = MitteVon(probe.Col + probe.DoorCol, probe.Row + probe.DoorRow);
            bool zeigt = EinnahmezeigerHier(tuer);
            bool nimmt = zeigt && PostCapture(tuer);
            klickOk = zeigt && nimmt;
            sb.AppendLine($"  Klick auf die Tuer von Gebaeude {probe.Slot}: "
                        + $"Zeiger sagt einnehmen {(zeigt ? "ja" : "NEIN")}, "
                        + $"PostCapture nimmt an {(nimmt ? "ja" : "NEIN")} "
                        + $"— »{_order}«");
        }

        // ---- 5) UND DAS TUERLOSE GEBAEUDE ------------------------------
        // ⚠⚠ Seine Meldung vom 08.09.2026: »wenn ich Strg druecke, was ja
        // attack bewirkt, kann ich nicht mehr auf Kraftwerke schiessen«. Der
        // Strg-Zweig lautet `if (!PostCapture && !PostAttackGround) PostMove`,
        // und PostCapture gab fuer ein tuerloses Gebaeude `true` zurueck — der
        // Bodenangriff kam nie dran. Gemessen wird genau das: PostCapture MUSS
        // hier `false` sagen, und der Einnahmezeiger darf nicht erscheinen.
        bool tuerlosOk = true;
        Entity? ohneTuer = null;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (b.Owner == ViewPlayer || b.Doors != 0) continue;
            // ⭐ Am liebsten das KRAFTWERK (Art 13) — das ist das Gebaeude aus
            // seiner Meldung. Sonst irgendeines ohne Tuer.
            if (ohneTuer == null || b.BType == 13) ohneTuer = b;
            if (b.BType == 13) break;
        }
        if (ohneTuer == null)
            sb.AppendLine("  kein fremdes Gebaeude OHNE Tuer — ungeprueft");
        else
        {
            var anker = MitteVon(ohneTuer.Col, ohneTuer.Row);
            bool zeigtEin = EinnahmezeigerHier(anker);
            bool schluckt = PostCapture(anker);
            // ⚠ Und die andere Haelfte der Frage: was macht der Strg-Zweig
            // danach? Seit dem 08.09.2026 versucht er ERST das Ziel
            // (PostAttack, wie die Zieluebersetzung @0x4353F0) und erst dann
            // die Zelle. Ein Bodenangriff tut einem Gebaeude naemlich nichts —
            // er kennt nur Wald, Objekte und Einheiten. Gemessen wird die neue
            // Reihenfolge, sonst misst der Pruefstand den alten Fehler.
            bool zielt = !schluckt && PostAttack(anker);
            tuerlosOk = !zeigtEin && !schluckt && zielt;
            sb.AppendLine($"  tuerloses Gebaeude {ohneTuer.Slot} (Art {ohneTuer.BType}): "
                        + $"Einnahmezeiger {(zeigtEin ? "JA (falsch)" : "nein")}, "
                        + $"PostCapture schluckt den Klick "
                        + $"{(schluckt ? "JA (falsch — Strg kann dann nicht schiessen)" : "nein")}, "
                        + $"Angriffsbefehl nimmt an {(zielt ? "ja" : "NEIN")} — »{_order}«");
        }

        _sel.Clear(); foreach (int k in merken) _sel.Add(k);
        PickOhneNebel = nebelVor;

        sb.AppendLine($"  fremde Gebaeude: {fremdTuer}, davon ohne Einnahmezeiger auf der Tuer: "
                    + $"{fremdTuerFalsch}");
        sb.AppendLine($"  fremde Gebaeude: {fremdMitte}, davon ohne Angriffszeiger in der Mitte: "
                    + $"{fremdMitteFalsch}");
        sb.AppendLine($"  herrenlose Gebaeude: {herrenlos}, davon mit Angriffszeiger: "
                    + $"{herrenlosFalsch}");
        sb.AppendLine($"  ZIVILE Gebaeude mit Tuer (Besitzer {NeutralOwner}, der Preis der "
                    + $"Eroberungskarte): {zivilTuer}, davon falsch: {zivilTuerFalsch}");
        if (MapEntityLayer.GebaeudezeigerAlt)
            sb.AppendLine("  ⚠ NULLMODELL --gebaeudezeiger-alt: die Zeilen MUESSEN hier "
                        + "durchfallen, sonst misst der Pruefstand nichts");
        if (MapEntityLayer.ZivilzeigerAlt)
            sb.AppendLine("  ⚠ NULLMODELL --zivilzeiger-alt: die zivile Zeile MUSS hier "
                        + "durchfallen (bug-209)");

        bool alles = fremdTuerFalsch == 0 && fremdMitteFalsch == 0 && herrenlosFalsch == 0
                  && zivilTuerFalsch == 0
                  && klickOk && tuerlosOk && (fremdTuer > 0 || herrenlos > 0 || zivilTuer > 0);
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Die Bildmitte einer Zelle — der Zeiger wird in Kartenpunkten
    /// gefragt, nicht in Zellen.</summary>
    private Vector2 MitteVon(int col, int row)
        => new(_ox + (col + 0.5f) * TileW,
               _oy + row * TileH - ElevOf(col, row) * 15 + TileH * 0.5f);
}
