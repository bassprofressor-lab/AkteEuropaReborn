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
            // ⚠⚠ 19.09.2026, bug-298: hier stand `break` beim ERSTEN Fahrzeug.
            // PostAttack wirft in Teil 2 jede Einheit ohne CanFight() aus der
            // Auswahl (cnt == 0 -> false). Faellt die Wahl auf ein BAUfahrzeug
            // (Aufsatz 40..54, Waffenfahne +0x0d = 0), misst Abschnitt 5 nicht
            // den Angriff, sondern die eigene Auswahl. Also: ein BEWAFFNETES
            // Fahrzeug bevorzugen, notfalls irgendeines (der Einnahmezweig
            // @0x4323F5 verlangt nur Klassenbyte 0, nicht die Waffe).
            if (fahrzeug < 0) fahrzeug = i;
            if (CanFight(u)) { fahrzeug = i; break; }
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
                    + $"({_entities[fahrzeug].Name}, +0x0a 0, Aufsatz {_entities[fahrzeug].Weapon}, "
                    + $"kann schiessen {(CanFight(_entities[fahrzeug]) ? "ja" : "NEIN")})");

        int fremdTuer = 0, fremdTuerFalsch = 0, fremdMitte = 0, fremdMitteFalsch = 0;
        int herrenlos = 0, herrenlosFalsch = 0;
        // ⭐ 12.09.2026, bug-209: das ZIVILE Gebaeude (Besitzer 11) ist der
        // Preis einer Eroberungskarte und war hier bisher mit der 255 in einem
        // Topf — auf map_NET02 sind das ALLE 52 Gebaeude mit Tuer.
        int zivilTuer = 0, zivilTuerFalsch = 0;
        // ⭐ 14.09.2026: das VERBUENDETE Gebaeude hat eine eigene Zeile — ueber ihm steht
        // im Original die Fahrt (Zeigerart 3), weder Einnahme noch Angriff
        // (berichte/verbuendete-fable.md §1). Bis heute zaehlte es als »fremd«.
        int verbuendet = 0, verbuendetFalsch = 0;
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
                       : b.Owner is < 0 or > 7 ? "herrenlos"
                       : Allied(ViewPlayer, b.Owner) ? "verbuendet" : "fremd";
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
                // ⭐⭐ 17.09.2026 RICHTIGGESTELLT (berichte/zeiger-klickfeld-fable.md):
                // hier stand `hMitte != Hint.Enemy` — der Prüfstand verlangte also den
                // ANGRIFFSZEIGER über der Mitte eines fremden Gebäudes. Das Original
                // zeigt dort NIE den Angriff ohne Strg: der Einnahmezweig I prüft jede
                // gemerkte Grundrisszelle (sec52), die Zone ist Körper ∪ Tür ∪ Zelle
                // über der Tür. Ein Gebäude MIT Tür trägt also auch auf dem Körper den
                // Einnahmezeiger, eines ohne Tür gar keinen Angriffszeiger.
                fremdMitte++;
                bool mitteFalsch = b.Doors != 0 && b.Built != 0
                                   ? hMitte != Hint.Einnahme
                                   : hMitte == Hint.Enemy;
                if (mitteFalsch) fremdMitteFalsch++;
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
            else if (art == "verbuendet")
            {
                verbuendet++;
                bool falsch = hTuer is Hint.Enemy or Hint.Einnahme || hMitte is Hint.Enemy or Hint.Einnahme;
                if (falsch) verbuendetFalsch++;
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
            if (b.Owner is >= 0 and <= 7 && Allied(ViewPlayer, b.Owner)) continue;   // 14.09.: kein Einnahmeziel
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
            // ⭐ 19.09.2026, bug-298: die Gruende, aus denen PostAttack `false`
            // geben kann, werden jetzt EINZELN genannt. Vorher stand nur
            // »NEIN«, und die Statuszeile trug noch den Satz des vorigen
            // Schrittes — darum wird `_order` hier zuerst geleert.
            int trefferA = Pick(anker);
            var waffe = _entities[fahrzeug];
            bool kannSchiessen = CanFight(waffe);
            bool istZiel = trefferA >= 0 && IstAngriffsziel(waffe, _entities[trefferA]);
            _order = "";
            bool zielt = !schluckt && PostAttack(anker);
            tuerlosOk = !zeigtEin && !schluckt && zielt;
            sb.AppendLine($"  tuerloses Gebaeude {ohneTuer.Slot} (Art {ohneTuer.BType}): "
                        + $"Einnahmezeiger {(zeigtEin ? "JA (falsch)" : "nein")}, "
                        + $"PostCapture schluckt den Klick "
                        + $"{(schluckt ? "JA (falsch — Strg kann dann nicht schiessen)" : "nein")}, "
                        + $"Angriffsbefehl nimmt an {(zielt ? "ja" : "NEIN")} — »{_order}«");
            if (!zielt)
                sb.AppendLine($"      Grund: Pick auf den Anker trifft "
                            + $"{(trefferA < 0 ? "NICHTS" : "Platz " + _entities[trefferA].Slot + " (Art " + _entities[trefferA].BType + ")")}, "
                            + $"gewaehltes Fahrzeug kann schiessen {(kannSchiessen ? "ja" : "NEIN")}, "
                            + $"IstAngriffsziel {(istZiel ? "ja" : "NEIN")}");
            if (!zielt && trefferA < 0)
            {
                var kasten = BodyRect(ohneTuer);
                sb.AppendLine($"      Geometrie: Zelle ({ohneTuer.Col},{ohneTuer.Row}) "
                            + $"Fuss {ohneTuer.FootW}x{ohneTuer.FootH} Hoehe {ElevOf(ohneTuer.Col, ohneTuer.Row)}, "
                            + $"Pos {ohneTuer.Pos}, Klickfeld {kasten}, gepruefter Punkt {anker}");
                sb.AppendLine($"      Anhebung HubOf {HubOf(ohneTuer.Col, ohneTuer.Row)} gegen flach "
                            + $"{ElevOf(ohneTuer.Col, ohneTuer.Row) * 15}, Hangart {HangArt(ohneTuer.Col, ohneTuer.Row)}, "
                            + $"Zellmitte {CellCenter(ohneTuer.Col, ohneTuer.Row)}, "
                            + $"Koerpermitte {BodyCenterAt(ohneTuer, ohneTuer.Col, ohneTuer.Row)}, "
                            + $"CellAt(Punkt) {(CellAt(anker) is { } za ? $"({za.X},{za.Y})" : "nichts")}, "
                            + $"CellAt(Kastenmitte) {(CellAt(kasten.GetCenter()) is { } zk ? $"({zk.X},{zk.Y})" : "nichts")}");
            }
        }

        _sel.Clear(); foreach (int k in merken) _sel.Add(k);
        PickOhneNebel = nebelVor;

        sb.AppendLine($"  fremde Gebaeude: {fremdTuer}, davon ohne Einnahmezeiger auf der Tuer: "
                    + $"{fremdTuerFalsch}");
        sb.AppendLine($"  fremde Gebaeude: {fremdMitte}, davon mit falschem Zeiger auf dem KOERPER (Soll: Einnahme, wo es eine Tuer gibt, sonst kein Angriff): "
                    + $"{fremdMitteFalsch}");
        sb.AppendLine($"  verbuendete Gebaeude: {verbuendet}, davon mit Angriffs- oder Einnahmezeiger: "
                    + $"{verbuendetFalsch}{(ZeigerVerbuendetAlt ? "  ⚠ NULLMODELL --zeiger-verbuendet-alt: MUSS hier > 0 sein und durchfallen" : "")}");
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
                  && zivilTuerFalsch == 0 && verbuendetFalsch == 0
                  && klickOk && tuerlosOk && (fremdTuer > 0 || herrenlos > 0 || zivilTuer > 0);
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Die Bildmitte einer Zelle — der Zeiger wird in Kartenpunkten
    /// gefragt, nicht in Zellen.</summary>
    /// <summary><c>--zeigerprobe-flach</c> — die NACHGEBAUTE, flache Hoehenformel
    /// von vor dem 19.09.2026. Nullmodell zu <see cref="MitteVon"/>: damit muss
    /// der Pruefstand auf K16 an Gebaeude 13 wieder DURCHFALLEN, sonst misst die
    /// Berichtigung nichts.</summary>
    public static bool ZeigerprobeFlach;

    /// <summary>
    /// <b>DIE BILDMITTE EINER ZELLE</b> — der Punkt, auf den der Pruefstand zeigt.
    ///
    /// <para>⚠⚠ 19.09.2026, bug-298. Hier stand eine EIGENE Formel:
    /// <c>_oy + row*TileH − ElevOf(col,row)*15 + TileH/2</c>. Sie nimmt an, dass
    /// eine Zelle der Hoehe h stets um <c>15·h</c> angehoben wird — flach, ohne
    /// die HANGART. Der Zeichner nimmt <see cref="CellCenter"/>, und das fragt
    /// <c>HubOf(col,row)</c>, also <c>Hang.Hub</c> mit der Hangart der Zelle.</para>
    ///
    /// <para>Auf ebenem Grund sind beide Zeichen fuer Zeichen gleich; am HANG
    /// gehen sie auseinander. Gemessen an K16, Gebaeude 13 (Zelle 76,30, Hoehe 6):
    /// flach <b>90</b>, <c>HubOf</c> <b>45</b> — <b>45 Bildpunkte</b> Unterschied.
    /// Der Pruefstand zeigte damit 35 px ueber das Klickfeld des Gebaeudes hinaus,
    /// <see cref="Pick"/> traf nichts, und Abschnitt 5 meldete »Angriffsbefehl
    /// nimmt an NEIN« — ein Fehler des Standes, nicht des Spiels.</para>
    ///
    /// <para>⚠ Arbeitsweise 24: ein Pruefstand fragt die Stelle, die auch das Spiel
    /// fragt, und baut die Regel nicht nach. Dieselbe Lehre wie bei
    /// <c>CellAt</c> am 18.09. (geneigte Klickecken statt flacher Kanten).</para>
    /// </summary>
    private Vector2 MitteVon(int col, int row)
        => ZeigerprobeFlach
             ? new(_ox + (col + 0.5f) * TileW,
                   _oy + row * TileH - ElevOf(col, row) * 15 + TileH * 0.5f)
             : CellCenter(col, row);
}
