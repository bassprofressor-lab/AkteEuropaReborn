namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐⭐ <b>DIE FLUGABWEHR</b> — der Kegel und die Station, 19.09.2026.
///
/// <para><b>Anlass:</b> seine Meldung zu Kampagne 17, »und es greifen ganz viele
/// helikopter und flugeinheiten an«. Seit <c>ai_air_attack</c> gebaut ist,
/// kommen die Hubschrauber wirklich — und niemand schoss zurück. Der Grund war
/// nicht eine fehlende Waffe, sondern eine fehlende MECHANIK: die Flak trifft
/// ein Flugzeug überhaupt nicht über den Geschossflug.</para>
///
/// <para><b>Lesung:</b> <c>berichte/flak-moerser-fable.md</c> §3/§4 und
/// <c>berichte/flughoehe-fable.md</c>, beide von Fable; die tragenden Aussagen
/// habe ich selbst gegengelesen (§8 dort).</para>
///
/// <para><b>Der Negativbefund, der alles erklärt:</b> über 890 sec19-Verweise im
/// Takt greift <b>kein einziger</b> aus dem Geschossflug — ein Geschoss kann ein
/// Flugzeug gar nicht treffen. Die Flak schießt darum auch kein Geschoss: ihr
/// Kampftakt kehrt bei <c>cmp cl,0x26</c> (@<c>0x40DE04</c>) nach EINEM Ruf um,
/// und dieser Ruf ist die Luftraumprüfung <c>0x428350</c>. Siehe
/// <see cref="MapEntityLayer.FlakAufsatz"/>.</para>
///
/// <para><b>Die drei Stücke:</b></para>
/// <list type="number">
/// <item><b>Der Kegel</b> <c>0x428350</c> (F <c>0x427540</c>) — wer im
/// Luftraum über mir ist, wird angemeldet. KEINE Reichweitenprüfung, sondern ein
/// RING: <c>dz = |alt − 15·Gelände| / 13</c>, Treffer wenn
/// <c>dz/3 ≤ d ≤ dz</c>. Ein Flugzeug SENKRECHT darüber (<c>d = 0</c>) wird
/// also nie getroffen, und ein sehr hohes nur weit draußen — daher »Kegel«.</item>
/// <item><b>Der Zuteiler</b> <c>0x428580</c> (F <c>0x427770</c>) legt den
/// Auftrag an.</item>
/// <item><b>Die Station »Check AA«</b> <c>0x428600</c> (F <c>0x4277F0</c>,
/// <c>--diff</c>: <b>kein Unterschied</b>) arbeitet die Aufträge ab: vier Rohre,
/// vier Schüsse, halbe Wahrscheinlichkeit je Takt.</item>
/// </list>
///
/// <para>⚠⚠ <b>Warum das nicht am Geschossflug hängen darf.</b> Der naheliegende
/// Einbau wäre, die Flak ein Geschoss auf das Flugzeug schießen zu lassen. Genau
/// das tut das Original NICHT, und es ist auch nicht dasselbe: der Ring hat ein
/// LOCH in der Mitte, ein Geschoss hätte das nie. Wer es als Geschoss baut,
/// bekommt eine Flak, die senkrecht über sich trifft — und das ist der
/// auffälligste Unterschied von allen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Ein angemeldetes Ziel: wer schießt, auf was, und wie viel noch
    /// übrig ist. Das Original führt <b>200</b> solche Plätze
    /// (<c>0x6E1498</c>), je Satz Schütze, Flugzeug, vier Rohre und ein
    /// Schusszähler.</summary>
    private sealed class FlakAuftrag
    {
        public int Schuetze;             // Index in _entities
        public int Flugzeug;             // Slot des Special
        public int Schuesse = FlakRohre; // +0x03, zählt herunter
        public readonly bool[] Rohr = new bool[FlakRohre];
    }

    private readonly List<FlakAuftrag> _flak = new();

    /// <summary>Vier Rohre, vier Schüsse — <c>push 0x5B</c> @0x4286C3
    /// (F 0x4278B3) und der Satzaufbau des Zuteilers.</summary>
    private const int FlakRohre = 4;

    /// <summary>So viele Aufträge führt das Original (<c>0x6E1498</c>).</summary>
    private const int FlakPlaetze = 200;

    /// <summary>Der Teiler des Rings: <c>dz = |alt − 15·Gelände| / <b>13</b></c>.
    /// ⚠ Eine gelesene Zahl, keine gewählte.</summary>
    private const int FlakHoehenTeiler = 13;

    /// <summary><c>--flak-kegel-aus</c> — der Kegel meldet nichts an. Nullmodell:
    /// <c>Aufträge 0</c>, und die Station läuft leer.</summary>
    public static bool FlakKegelAus;

    /// <summary><c>--flak-abstand-alt</c> — der Stand vor dem 19.09.2026: der
    /// Abstand gerundet und aus der Feinlage statt abgeschnitten und aus
    /// Zellen. Nullmodell: die Zahl der RINGTREFFER muss sich ändern, die Zahl
    /// der KEGELPROBEN nicht.</summary>
    public static bool FlakAbstandAlt;

    /// <summary><c>--flak-uk-alt</c> — ohne den Ausschluss von <c>uk</c> 2 und
    /// 4 (Startvorgang).</summary>
    public static bool FlakUkAlt;

    /// <summary>⭐⭐ 20.09.2026 — <c>--flak-drehung-alt</c>: eine nachladende
    /// Flak dreht sich NICHT mit, wie bis zum 20.09. Darunter MUSS
    /// <see cref="FlakDrehungOhneSchuss"/> auf 0 fallen.</summary>
    public static bool FlakDrehungAlt;

    /// <summary><c>--flak-treffer-aus</c> — Aufträge werden angelegt, aber nie
    /// abgearbeitet. Damit ist der Kegel (Stück 1) OHNE die Station (Stück 3)
    /// messbar: <c>Aufträge &gt; 0</c> bei <c>Schüsse 0</c>.</summary>
    public static bool FlakTrefferAus;

    /// <summary>
    /// <c>--flak-schaden-klemme</c> — <b>eine bewusste ABWEICHUNG, standardmäßig
    /// AUS.</b>
    ///
    /// <para>Der Vergleich im Original ist VORZEICHENLOS: <c>sub eax,edx;
    /// sar eax,1</c> gibt den Schaden, dann <c>and edi,0xFFFF; cmp edx,edi; ja</c>
    /// (@<c>0x428820</c>, in C und F identisch). Ein NEGATIVER Schaden — eine
    /// gut gepanzerte Maschine gegen eine schwache Flak — wird damit zu ~65535,
    /// der Vergleich fällt durch, und das Flugzeug stürzt <b>sofort</b> ab.
    /// Das Luft-gegen-Luft-Stück <c>0x42811C</c> klemmt dagegen auf ≥ 0, was
    /// dafür spricht, dass hier ein Versehen des Originals steht (V).</para>
    ///
    /// <para><b>Es bleibt trotzdem die Voreinstellung</b>, weil die Kampagne
    /// originalgetreu ist. Dieser Schalter klemmt auf ≥ 0 und ist für den
    /// Gefechtsmodus gedacht, der bewusst abweichen darf.</para></summary>
    public static bool FlakSchadenKlemme;

    // Zahlen für den Prüfstand. ⚠ Getrennt, nicht eine Summe: »kein Treffer«
    // kann heissen, dass der Kegel nichts meldet, dass die Station nicht läuft,
    // oder dass gar keine Flak steht — und das sind drei verschiedene Befunde.
    public int FlakAuftraege, FlakSchuesse, FlakAbschuesse, FlakNegativSchaden;
    public int FlakKegelProben, FlakRingTreffer;

    /// <summary>Wie oft ein Rohr gedreht wurde, und wie oft davon OHNE einen
    /// Auftrag anzulegen — das ist der sichtbare Unterschied: die Flak zuckt
    /// dem Flieger nach, waehrend sie nachlaedt.</summary>
    public int FlakGedreht, FlakDrehungOhneSchuss;

    /// <summary>⭐⭐ 20.09.2026 — <b>DER ABGLEICH DER SCHUSSFOLGE</b> mit den drei
    /// gelesenen Zahlen. Bis heute stand im STATUS »unsere Kette liefert
    /// 8 Auftraege / 27 Schuesse / 2 Abschuesse — mit dem Original nicht
    /// abgeglichen«. Das waren ZAEHLER, keine Pruefung: ohne ein SOLL sagt eine
    /// Zahl nichts.
    ///
    /// <para>Geprueft wird jetzt gegen das, was gelesen ist:</para>
    /// <list type="bullet">
    ///   <item><b>Hoechstens 4 Schuesse je Auftrag</b> — der Zuteiler schreibt
    ///   <c>+0x07 = 4</c> und vier Rohre (@0x4285C1..0x4285D9).</item>
    ///   <item><b>Rund 2 Takte je Schuss</b> — <c>test al,1</c> @0x42862D ist
    ///   <c>P = ½</c>, der Erwartungswert der geometrischen Verteilung ist 2.</item>
    ///   <item><b>Nachladen 12…17, im Mittel 14,5</b> — <c>rand%6 + 12</c>
    ///   @0x4284EE.</item>
    /// </list></summary>
    public int FlakSchuesseMax, FlakAuftragTakte;
    public int FlakNachladeSumme, FlakNachladeZahl, FlakNachladeMin = int.MaxValue, FlakNachladeMax;

    /// <summary>⚠ 19.09.2026 — die SPANNEN von dz und d, und wie oft dz null war.
    /// Ein »im Ring 0« ohne diese Zahlen ist nicht auswertbar: es kann heissen,
    /// dass die Hoehe nicht stimmt (dz), dass der Abstand nicht stimmt (d), oder
    /// dass beide stimmen und der Ring einfach nicht getroffen wurde. Genau das
    /// war beim ersten Lauf die Frage.</summary>
    public int FlakDzMin = int.MaxValue, FlakDzMax, FlakDMin = int.MaxValue, FlakDMax, FlakDzNull;

    /// <summary>
    /// <b>Der Takt der Flugabwehr</b> — Kegel und Station, in dieser Reihenfolge,
    /// je Spieltakt.
    ///
    /// <para>⚠ Die Reihenfolge ist nicht gleichgültig: das Original ruft den
    /// Kegel aus dem KAMPFTAKT der Einheit (@0x40DE0A) und die Station aus dem
    /// Taktrumpf. Ein in diesem Takt angemeldeter Auftrag kann also im selben
    /// Takt schon schießen.</para></summary>
    private void FlakTakt()
    {
        FlakKegel();
        FlakStation();
    }

    /// <summary>
    /// <b><c>0x428350</c> — die Luftraumprüfung, der KEGEL.</b>
    ///
    /// <para>Für jede lebende Flak: über alle fliegenden, verfeindeten Flugzeuge,
    /// und der ERSTE Treffer beendet die Schleife (ein Schütze meldet je Takt
    /// höchstens ein Ziel an).</para>
    ///
    /// <para><b>UNSERE SETZUNGEN:</b> das Original schließt Flugzeuge mit
    /// <c>uk ∈ {0, 2, 4, 100}</c> aus — abgestellt, landend, startend,
    /// abstürzend. Von diesen vier Zuständen führen wir nur »abgestellt«
    /// (<see cref="Special.Stored"/>); startende und landende Maschinen sind bei
    /// uns also schon beschießbar, und einen Absturz gibt es nicht. Das ist eine
    /// benannte Abweichung, keine stille.</para></summary>
    /// <summary>
    /// <b>Der RING selbst</b>, als eigene Frage: <c>dz = |alt − g| / 13</c>, und
    /// Treffer bei <c>dz/3 ≤ d ≤ dz</c> (beides ganzzahlig).
    ///
    /// <para>⚠ Herausgezogen am 19.09.2026, damit der Prüfstand ihn OHNE die
    /// Kette messen kann. Der Grund steht in <see cref="FlakProbeTakt"/>: mit
    /// einer einzigen Flak kommt je Nachladezeit nur EIN Ziel dran, und ein
    /// Aufbau mit sechs Fällen misst dann fünfmal nichts. Die Regel ist reine
    /// Rechnung und damit für jeden Fall prüfbar; die Kette wird einmal
    /// gemessen.</para></summary>
    /// <summary>
    /// ⚠⚠ 19.09.2026 — <b>DIE ZEILE <c>if (dz &lt;= 0) return false;</c> WAR
    /// UNSERE ZUTAT UND IST WEG.</b>
    ///
    /// <para>Gegengelesen (<c>berichte/flak-zielwahl-spion-fable.md</c> A2): das
    /// Original prüft nur die zwei Ungleichungen, und bei <c>dz == 0</c> heisst
    /// das <c>0 ≤ d ≤ 0</c> — also <b>d = 0 trifft</b>. Ein Flugzeug in
    /// Bodennähe genau über der Flakzelle wird getroffen; bei uns war es
    /// unverwundbar. Das betrifft vor allem die Versorgungshelis, die tief
    /// fliegen.</para>
    ///
    /// <para><b>Und das »Loch in der Mitte« gilt erst ab dz ≥ 3</b>, weil
    /// <c>dz/3</c> ganzzahlig teilt: dz 1 und 2 geben <c>0 ≤ d ≤ dz</c>, also
    /// kein Loch. »Senkrecht darüber trifft sie nie« stimmt nur für
    /// <c>|alt − g| ≥ 39</c>. Das hatte ich zu breit aufgeschrieben.</para>
    /// </summary>
    /// <summary>
    /// <b>DER ABSTAND, EINMAL</b> — und zwar die Rechnung, die der Kegel
    /// benutzt UND die der Prüfstand fragt.
    ///
    /// <para>⚠⚠ 19.09.2026 — es gab davon <b>drei</b>: der Kegel rechnete auf
    /// Bildpunkten samt Feinlage und rundete, der Prüfstand nahm allein die
    /// SPALTENdifferenz, und das Original nimmt Zellen und schneidet ab. Die
    /// A/B zu <c>--flak-abstand-alt</c> meldete darum zweimal »alles ok«, auch
    /// für einen schrägen Fall, der sie hätte trennen müssen — der Prüfstand
    /// prüfte seine eigene Formel.</para>
    ///
    /// <para><b>Gelesen:</b> das Original nimmt das ZELLBYTE der Einheit
    /// (<c>+0x00/+0x01</c>) gegen das ZELLWORT des Flugzeugs
    /// (<c>+0x00/+0x02</c>) und schneidet die Wurzel ab — <c>_ftol</c>
    /// C <c>0x4D6C2B</c> / F <c>0x4D67BB</c> setzt <c>or ah,0xC</c>, also
    /// Rundungsmodus 11 (gegen Null). <b>Keine Feinlage.</b></para>
    ///
    /// <para>Gemessen: bei <c>dz 10</c> entscheiden <b>33 von 441 Zellen</b>
    /// anders als unsere alte Rechnung — der Ring des Originals sind die wahren
    /// Abstände <c>[3, 11)</c>, unser alter <c>[2.5, 10.5)</c>.</para></summary>
    private static int FlakAbstand(int dc, int dr, Vector2 flugzeug, Vector2 schuetze)
    {
        if (FlakAbstandAlt)
        {
            float dxz = (flugzeug.X - schuetze.X) / TileW;
            float dyz = (flugzeug.Y - schuetze.Y) / TileH;
            return Mathf.RoundToInt(Mathf.Sqrt(dxz * dxz + dyz * dyz));
        }
        return (int)Mathf.Sqrt(dc * dc + dr * dr);      // abgeschnitten, aus Zellen
    }

    private static bool FlakImRing(int alt, int g, int d)
    {
        int dz = Mathf.Abs(alt - g) / FlakHoehenTeiler;
        return d >= dz / 3 && d <= dz;
    }

    private void FlakKegel()
    {
        if (FlakKegelAus || _nav == null) return;

        for (int i = 0; i < _entities.Count; i++)
        {
            var s = _entities[i];
            if (s.IsProp || s.Dead || s.Weapon != FlakAufsatz) continue;
            if (s.Owner is < 0 or > 7) continue;
            if (_flak.Count >= FlakPlaetze) break;          // »Cannot add« des Originals
            // ⭐⭐ 20.09.2026 — DIE DREHUNG STEHT VOR DER NACHLADEPRUEFUNG.
            // Hier sprang der Takt beim Nachladen ganz aus der Schleife, und
            // damit drehte sich ein nachladendes Rohr nicht. Das Original
            // schreibt OTOC_HLAVEN (+0x17) @0x4284CB und PRUEFT ERST DANACH
            // NABYTO (+0x32) @0x4284D1 — es zuckt also jeden Takt dem ersten
            // Flugzeug im Ring nach, auch waehrend es nachlaedt. Selbst
            // nachgelesen am 20.09. (byte-genau, C).
            //
            // Gebaut ist es als Fahne: die Kegelschleife laeuft auch beim
            // Nachladen, dreht das Rohr und bricht dann ab, statt einen
            // Auftrag anzulegen. Gegenschalter --flak-drehung-alt.
            bool laedt = s.Reload > 0;
            if (laedt)
            {
                s.Reload--;
                if (FlakDrehungAlt) continue;
            }

            int g = ElevOf(s.Col, s.Row) * 15;

            foreach (var a in _special)
            {
                if (a.Dead || a.Stored) continue;            // uk 0 / 4
                // ⚠ 20.09.2026 — UND uk 100: ein STUERZENDER Flieger ist kein
                // Ziel mehr. Ohne diese Zeile beschiesst ihn die Station
                // weiter, und der Abschusszaehler meldete 12 bei sechs
                // Fliegern — die Kette zaehlte jeden Nachschuss als Abschuss.
                if (a.Absturz) continue;                     // uk 100
                // ⭐ 19.09.2026 — die Liste des Originals ist uk ∉ {0, 2, 4, 100}
                // (C 0x428405..0x428419 / F 0x4275F5..0x427609). Wir hatten nur
                // 0 (ueber `Stored`) und 100. Ein Flugzeug, das gerade startet,
                // ist im Original kein Ziel — bei uns war es eines.
                // Gegenschalter --flak-uk-alt.
                //
                // ⭐⭐ 20.09.2026 — **UND JETZT IST AUCH uk 2 GELESEN.** Es stand
                // hier als »ungelesen«; damit war die ganze Ausschlussliste eine
                // Abschrift ohne Bedeutung. Eingekreist wurde es so:
                //
                //   * uk (+0x10) hat 27 Schreiber. **22 schreiben LITERALE** —
                //     0, 1, 3, 4, 7, 10, 11, 100. **Keines ist 2.**
                //   * Von den fuenf Registerschreibern setzen 0x427E9E und
                //     0x427F4E feste 10 bzw. 11, 0x4B30EE rechnet `arg + 10`.
                //     Bleibt **0x4235D8: `uk := byte[+0x11]`, also uk := m_uk**,
                //     am Ende des Steigflugs (Startzweig uk 4).
                //   * m_uk (+0x11) hat 26 Schreiber; **genau EINER** schreibt
                //     das Literal 2: **0x426350**, und das ist der HELI-Zweig
                //     von `air_back_to_airport` (Sollhoehe Gelaende*15 + 8,
                //     also Muster 10..12).
                //   * `launch_aircraft` hat **sechs** Rufer; ihre Modi sind
                //     7 (guard 0x428C52), 10 und 11 (die zwei Nachschubhelis
                //     0x4B16EA/0x4B1721), 7 (0x4BDC2A), 1-oder-7 (0x4C2B02:
                //     `cmp ax,1; sbb cl,cl; and cl,0xFA; add cl,7`) und der
                //     Modus aus der Meldung (Befehl 502, 0x4C3773).
                //     **Keiner uebergibt 2.**
                //
                // ⇒ **uk 2 heisst »heimfliegen und landen«**, und es kann nur
                // einen KAMPF- oder TRANSPORTHUBSCHRAUBER treffen. Damit ist die
                // Liste {0, 2, 4, 100} eine Einheit: im Hangar, im Landeanflug,
                // im Startvorgang, im Absturz — **alles vier ist »nicht richtig
                // in der Luft«**. Die Flak schiesst darauf nicht.
                //
                // ⚠ OFFEN (?): welcher Takt aus `uk 1 + m_uk 2` das `uk 2`
                // macht. `air_back_to_airport` setzt fuer ALLE `uk := 1`
                // (@0x4261E0); die Uebergabe `uk := m_uk` steht nur im
                // Startzweig. Der Weg dazwischen ist nicht gelesen.
                if (!FlakUkAlt && (a.Order == 2 || a.Order == 4)) continue;
                if (a.Owner is < 0 or > 7 || Allied(s.Owner, a.Owner)) continue;

                FlakKegelProben++;

                // ⚠ dz und d BEIDE in Zellen, und dz ganzzahlig geteilt —
                // der Ring ist grob, und das ist er im Original auch.
                int dz = Mathf.Abs(a.Alt - g) / FlakHoehenTeiler;
                FlakDzMin = Mathf.Min(FlakDzMin, dz);
                FlakDzMax = Mathf.Max(FlakDzMax, dz);
                // ⚠ Kein Ausstieg bei dz == 0 mehr — siehe FlakImRing. Der
                // Zaehler bleibt, weil die Zahl interessant ist.
                if (dz <= 0) FlakDzNull++;

                // ⚠⚠ 19.09.2026 — DER ABSTAND WIRD ABGESCHNITTEN UND AUS
                // ZELLEN GERECHNET, nicht gerundet und nicht aus der Feinlage.
                //
                // Gelesen: das Original nimmt das ZELLBYTE der Einheit
                // (+0x00/+0x01) gegen das ZELLWORT des Flugzeugs (+0x00/+0x02)
                // und schneidet die Wurzel ab (`_ftol` C 0x4D6C2B / F 0x4D67BB
                // setzt `or ah,0xC`, Rundungsmodus 11 = gegen Null).
                //
                // Wir rechneten mit RoundToInt auf Bildpunkten samt Feinlage.
                // Der Unterschied ist NICHT kosmetisch: gemessen entscheiden
                // bei dz 10 **33 von 441 Zellen anders** — der Ring des
                // Originals sind die wahren Abstaende [3, 11), unser alter
                // [2.5, 10.5). Gegenschalter --flak-abstand-alt.
                int d = FlakAbstand(a.Col - s.Col, a.Row - s.Row, a.Pos, s.Pos);
                FlakDMin = Mathf.Min(FlakDMin, d);
                FlakDMax = Mathf.Max(FlakDMax, d);

                if (!FlakImRing(a.Alt, g, d)) continue;      // NICHT im Ring
                FlakRingTreffer++;

                // Der Schuetze dreht sich hin — IMMER, auch beim Nachladen
                // (@0x4284CB vor @0x4284D1).
                s.AimFacing = DirToFacing(a.Pos - s.Pos);
                FlakGedreht++;
                // …aber angelegt wird nur, wenn er geladen hat (@0x4284D9).
                if (laedt) { FlakDrehungOhneSchuss++; break; }
                _flak.Add(new FlakAuftrag { Schuetze = i, Flugzeug = a.Slot });
                FlakAuftraege++;
                // Nachladen: Roll(6) + 12 Takte, ein deterministischer Würfel
                s.Reload = Simulation.Determinism.Roll(6) + 12;
                FlakNachladeSumme += s.Reload; FlakNachladeZahl++;
                FlakNachladeMin = Mathf.Min(FlakNachladeMin, s.Reload);
                FlakNachladeMax = Mathf.Max(FlakNachladeMax, s.Reload);
                break;                                       // erster Treffer, fertig
            }
        }
    }

    /// <summary>
    /// <b><c>0x428600</c> »Check AA« — die STATION.</b>
    ///
    /// <para>Je Takt und Auftrag: mit halber Wahrscheinlichkeit gar nichts; sonst
    /// ein Rohr, eine Wolke, ein Klang, ein Schaden. Nach vier Schüssen ist der
    /// Auftrag zu Ende.</para>
    ///
    /// <para><b>Der Schaden</b> ist <c>((Roll(4) + Angriff) − (Roll(4) +
    /// Panzerung)) / 2</c>, abgeschnitten — und der Vergleich danach ist
    /// VORZEICHENLOS, siehe <see cref="FlakSchadenKlemme"/>.</para>
    ///
    /// <para>⚠ <b>UNSERE SETZUNGEN:</b> die Wolkenhöhe des Originals
    /// (<c>alt − (Y mod 40)/2 − 40</c>) rechnet in Feineinheiten eines Bildes,
    /// das wir anders aufbauen; wir setzen die Wolke an die Bildlage des
    /// Flugzeugs. Und die vier Rohre führen wir mit, weil das Original sie
    /// führt — sichtbar ist davon bei uns nichts.</para></summary>
    private void FlakStation()
    {
        if (FlakTrefferAus) return;

        for (int k = _flak.Count - 1; k >= 0; k--)
        {
            var auf = _flak[k];
            var ziel = _special.Find(x => x.Slot == auf.Flugzeug);
            // ⚠ Auftrag loeschen bei Hp == 0 ODER uk == 100 — so gelesen.
            if (ziel == null || ziel.Dead || ziel.Absturz || auf.Schuesse <= 0)
            { _flak.RemoveAt(k); continue; }
            if (auf.Schuetze < 0 || auf.Schuetze >= _entities.Count) { _flak.RemoveAt(k); continue; }
            var s = _entities[auf.Schuetze];
            if (s.Dead || s.IsProp) { _flak.RemoveAt(k); continue; }

            // Je Takt, in dem ein Auftrag OFFEN steht, eine Strichliste — daraus
            // wird unten »Takte je Schuss« (Soll ~2, weil P = ½).
            FlakAuftragTakte++;

            // »Roll(2) == 0 -> weiter«: die Haelfte der Takte fällt aus
            if (Simulation.Determinism.Roll(2) == 0) continue;

            // Rohrwahl: ab Roll(4) zyklisch das erste unverbrauchte
            int start = Simulation.Determinism.Roll(FlakRohre), rohr = -1;
            for (int t = 0; t < FlakRohre; t++)
            {
                int r = (start + t) % FlakRohre;
                if (!auf.Rohr[r]) { rohr = r; break; }
            }
            if (rohr < 0) { _flak.RemoveAt(k); continue; }
            auf.Rohr[rohr] = true;
            auf.Schuesse--;
            FlakSchuesse++;
            FlakSchuesseMax = Mathf.Max(FlakSchuesseMax, FlakRohre - auf.Schuesse);

            // die Sprengwolke am Flugzeug, verstreut um ±30 Feineinheiten
            var wolke = ziel.Pos + new Vector2(30 - Simulation.Determinism.Roll(60),
                                               30 - Simulation.Determinism.Roll(60)) * 0.5f;
            _effects.Add(new Effect { Pos = wolke, Kind = "schlag_91", FrameTime = 0.05f });
            Audio.GameSounds.PlayAt(2, s.Col, s.Row);

            int dmg = ((Simulation.Determinism.Roll(4) + s.Attack)
                     - (Simulation.Determinism.Roll(4) + ziel.Defence)) / 2;
            if (dmg < 0)
            {
                FlakNegativSchaden++;
                // ⚠⚠ HIER SITZT DER VORZEICHENFEHLER DES ORIGINALS. Ohne Klemme
                // ist der Vergleich `Hp > dmg` vorzeichenlos, ein negativer
                // Schaden wird zu ~65535 und das Flugzeug faellt sofort.
                if (FlakSchadenKlemme) { dmg = 0; }
                else { FlakAbschuss(ziel, s); continue; }
            }

            ziel.Hp -= dmg;
            if (ziel.Hp <= 0) FlakAbschuss(ziel, s);
        }
    }

    /// <summary>Ein Flugzeug ist abgeschossen. ⚠ Einen ABSTURZ (<c>uk</c> 100,
    /// −2 Höhe je Takt bis zum Boden) führen wir nicht — bei uns ist es sofort
    /// tot. Benannte Abweichung; der Absturz waere eine eigene Bauaufgabe.</summary>
    private void FlakAbschuss(Special ziel, Entity schuetze)
    {
        FlakAbschuesse++;
        // ⭐ 20.09.2026 — HIER STAND `Dead = true`, UND DAS WAR ZU FRUEH.
        // Ein Abschuss macht ein Flugzeug im Original nicht tot, sondern
        // STUERZEND (uk := 100): es faellt mit -2 je Takt, und erst der
        // Aufprall richtet Schaden an — 60 auf die Zelle, 50 auf acht
        // Nachbarn, auch gegen Gebaeude. DAS ist der »Kamikaze«, den er
        // gemeldet hat. Siehe Simulation/Absturz.cs; --absturz-aus.
        FlugzeugAbschiessen(ziel,
            $"Flak Platz {schuetze.Slot} (Spieler {schuetze.Owner}) " +
            $"auf ({schuetze.Col},{schuetze.Row})");
    }

    /// <summary>
    /// <c>--flak-check</c> — die Zeile zur Flugabwehr.
    ///
    /// <para>⚠ Sie nennt ZUERST, ob überhaupt eine Flak und ein Flugzeug da war.
    /// Ohne das ist jede 0 darunter keine Messung, sondern eine leere Karte —
    /// genau der Fehler, den ich am 19.09. bei der Bodensperre erst gemacht und
    /// dann behoben habe.</para></summary>
    public string FlakCheckLine()
    {
        int flak = 0, flugzeuge = 0;
        foreach (var e in _entities)
            if (!e.IsProp && !e.Dead && e.Weapon == FlakAufsatz) flak++;
        foreach (var a in _special) if (!a.Dead && !a.Stored) flugzeuge++;

        if (flak == 0 || flugzeuge == 0)
            return $"flak-check: {flak} Flak, {flugzeuge} Flugzeuge in der Luft — " +
                   "KEIN URTEIL, dieser Fall kann hier nicht eintreten";

        var sb = new System.Text.StringBuilder();
        sb.Append($"flak-check: {flak} Flak, {flugzeuge} in der Luft; ");
        sb.Append($"Kegelproben {FlakKegelProben}, im Ring {FlakRingTreffer}, ");
        sb.Append(FlakDzMin <= FlakDzMax
            ? $"dz {FlakDzMin}..{FlakDzMax} (davon {FlakDzNull}x null), "
            : "dz nie gerechnet, ");
        sb.Append(FlakDMin <= FlakDMax ? $"d {FlakDMin}..{FlakDMax} Zellen, " : "d nie gerechnet, ");
        sb.Append($"Auftraege {FlakAuftraege} (offen {_flak.Count}), ");
        // ⭐⭐ 20.09.2026 — die Drehung. Die ZWEITE Zahl ist die interessante:
        // sie zaehlt die Takte, in denen das Rohr nachzuckt, OHNE zu schiessen.
        // Genau die gab es bei uns bis heute nicht.
        sb.Append($"gedreht {FlakGedreht}x (davon {FlakDrehungOhneSchuss}x beim "
                + "NACHLADEN, also ohne Schuss)"
                + (FlakDrehungAlt ? " [--flak-drehung-alt: die zweite Zahl MUSS 0 sein]" : "")
                + ", ");
        // ⭐⭐ DER ABGLEICH: drei Zahlen, jede gegen ihr gelesenes SOLL.
        if (FlakSchuesse > 0)
        {
            float takte = (float)FlakAuftragTakte / FlakSchuesse;
            sb.Append($"ABGLEICH: hoechstens {FlakSchuesseMax} Schuesse je Auftrag "
                    + $"(Soll <= {FlakRohre}) {(FlakSchuesseMax <= FlakRohre ? "ok" : "⚠ ZU VIEL")}, "
                    + $"{takte:0.00} Takte je Schuss (Soll ~2,00 bei P=1/2) "
                    + $"{(takte > 1.5f && takte < 2.8f ? "ok" : "⚠ DANEBEN")}, ");
        }
        if (FlakNachladeZahl > 0)
        {
            float m = (float)FlakNachladeSumme / FlakNachladeZahl;
            bool spanne = FlakNachladeMin >= 12 && FlakNachladeMax <= 17;
            sb.Append($"Nachladen {FlakNachladeMin}..{FlakNachladeMax} Mittel {m:0.0} "
                    + $"(Soll 12..17, Mittel 14,5) {(spanne && m > 13f && m < 16f ? "ok" : "⚠ DANEBEN")}, ");
        }
        sb.Append($"Schuesse {FlakSchuesse}, Abschuesse {FlakAbschuesse}");
        if (FlakNegativSchaden > 0)
            sb.Append($", negativer Schaden {FlakNegativSchaden}x " +
                      (FlakSchadenKlemme ? "(geklemmt)" : "(= Sofortabschuss, wie das Original)"));
        sb.Append($" | Hoehe: {FlughoeheGestiegen}x gestiegen, {FlughoeheGeregelt}x geregelt");
        if (FlakKegelAus) sb.Append("   ⚠ Nullmodell --flak-kegel-aus: Auftraege muessen 0 sein");
        if (FlakTrefferAus) sb.Append("   ⚠ Nullmodell --flak-treffer-aus: Schuesse muessen 0 sein");
        if (FlughoeheAlt) sb.Append("   ⚠ Nullmodell --flughoehe-alt: ohne Hoehe kein Ring");
        return sb.ToString();
    }

    // ================= --flak-probe: der Ring mit festen Zahlen ==============
    //
    // ⚠⚠ WARUM ES DIESEN AUFBAU BRAUCHT. Der erste Lauf auf map_DM_3 (10 Flak,
    // 23 Flugzeuge, 120 s) gab »im Ring 0« — und erst die Spannen sagten,
    // warum: `dz 0..9`, aber `d 10..192 Zellen`. Auf dieser Karte kommt nie ein
    // Flugzeug naeher als zehn Zellen an eine Flak, der Ring KANN dort nicht
    // greifen. Das ist kein Befund ueber den Ring, sondern einer ueber die
    // Karte — und ohne einen gestellten Fall waere daraus beinahe »die Flak
    // trifft nicht« geworden.
    //
    // Der Aufbau kommt aus dem Vorschlag der Lesung: eine Flak auf Stufe 0,
    // sechs feindliche Flugzeuge an festen Plaetzen, und ein SOLL je Platz, das
    // sich aus dem gelesenen Ring ausrechnen laesst:
    //
    //   Hoehe 130, Gelaende 0 -> dz = 130/13 = 10, Ring 3 <= d <= 10
    //       d = 2  -> NICHT (unter dem Loch)      d = 5  -> im Ring
    //       d = 9  -> im Ring                      d = 12 -> NICHT (zu weit)
    //   Hoehe 39              -> dz = 3,  Ring 1 <= d <= 3
    //       d = 2  -> im Ring
    //   senkrecht darueber (d = 0, Hoehe 130) -> NIE

    private bool _flakProbe;
    private int _flakProbeTakte;
    private int _flakProbeSchuetzeCol, _flakProbeSchuetzeRow, _flakProbeGelaende;
    private Vector2 _flakProbeSchuetzePos;
    private readonly List<(string Titel, int Slot, bool Soll)> _flakProbeFaelle = new();

    /// <summary>
    /// ⚠⚠ 19.09.2026 — <b>DER GESTELLTE FALL MUSS STEHEN, UND ER STAND NICHT.</b>
    ///
    /// <para>Der erste Lauf der Probe meldete drei von sechs Faellen falsch — und
    /// die Ursache war nicht der Ring, sondern die Probe selbst: alle sechs
    /// Flieger standen am Ende auf <b>Hoehe 135</b> statt auf ihren gesetzten 130
    /// bzw. 39, und sie waren weggeflogen. <c>UpdateAircraft</c> gibt einem
    /// Flugzeug ohne Ziel ueber <c>AirPatrol</c> ein neues Flugziel; damit ist
    /// der Abstand dorthin groesser als sechs Zellen, der Steigflug greift, und
    /// die Lage wandert mit. Gemessen wurde danach ein Fall, den niemand
    /// gestellt hatte.</para>
    ///
    /// <para>Darum wird Lage UND Hoehe jedes Probefliegers <b>je Takt neu
    /// festgeheftet</b>, und zwar VOR dem Kegel. Das ist der Zweck eines
    /// gestellten Aufbaus: er soll eine Zahl messen, nicht ein Flugverhalten.</para></summary>
    private readonly List<(int Slot, Vector2 Pos, int Col, int Row, int Alt)> _flakProbeHalt = new();

    /// <summary><c>--flak-probe</c> anwerfen.</summary>
    public void FlakProbeStart() => _flakProbe = true;

    /// <summary>Der Aufbau, einmal — und dann N Takte laufen lassen.</summary>
    private void FlakProbeAufbauen()
    {
        _flakProbe = false;
        if (_nav == null) return;

        // Einen eigenen Schuetzen zur Flak machen. ⚠ UNSERE Zutat: eine Flak
        // aus dem Nichts zu setzen hiesse, den Aufstellweg mitzubauen; hier
        // wird nur der Aufsatz umgeschrieben, und das ist als Eingriff
        // ausdruecklich benannt.
        int si = -1;
        for (int i = 0; i < _entities.Count && si < 0; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer) continue;
            if (e.Attack <= 0) continue;
            si = i;
        }
        if (si < 0) { GD.Print("flak-probe: KEIN URTEIL — kein eigener Schuetze"); return; }
        var s = _entities[si];
        s.Weapon = FlakAufsatz;
        s.Reload = 0;
        int g = ElevOf(s.Col, s.Row) * 15;
        _flakProbeSchuetzeCol = s.Col;
        _flakProbeSchuetzeRow = s.Row;
        _flakProbeSchuetzePos = s.Pos;
        _flakProbeGelaende = g;

        int gegner = -1;
        for (int p = 0; p < 8; p++) if (!Allied(s.Owner, p)) { gegner = p; break; }
        if (gegner < 0) { GD.Print("flak-probe: KEIN URTEIL — kein verfeindeter Spieler"); return; }

        GD.Print($"flak-probe: Flak = Platz {s.Slot} auf ({s.Col},{s.Row}), Gelaende*15 = {g}; " +
                 $"Ziele gehoeren Spieler {gegner}");

        // (Titel, Spaltenabstand, Zeilenabstand, Hoehe, Soll)
        //
        // ⚠⚠ 19.09.2026 — DIE ERSTEN SECHS FAELLE KONNTEN DEN ABSTANDSFEHLER
        // NICHT SEHEN. Sie liegen alle auf GANZEN Zellabstaenden einer Achse,
        // und dort geben Runden und Abschneiden dasselbe: die A/B zu
        // --flak-abstand-alt lieferte zweimal »6 von 6 ok«, und genau das ist
        // die Falle, gegen die das Do-Not-Repeat vom 20.09. geschrieben ist.
        //
        // Der siebte Fall ist SCHRAEG und entscheidet:
        //     dc 10, dr 4  ->  sqrt(116) = 10.77
        //     Original  (abgeschnitten): 10  ->  IM Ring   (dz 10, Ring 3..10)
        //     unser alt (gerundet):      11  ->  NICHT im Ring
        // Ein Fall, der unter beiden Nullmodellen gleich ausfaellt, prueft
        // nichts.
        var faelle = new (string T, int Dc, int Dr, int Alt, bool Soll)[]
        {
            ("2 Zellen, Hoehe 130", 2, 0, 130, false),
            ("5 Zellen, Hoehe 130", 5, 0, 130, true),
            ("9 Zellen, Hoehe 130", 9, 0, 130, true),
            ("12 Zellen, Hoehe 130", 12, 0, 130, false),
            ("2 Zellen, Hoehe 39", 2, 0, 39, true),
            ("senkrecht darueber, Hoehe 130", 0, 0, 130, false),
            // ⭐ der Fall, der das Abschneiden von der Rundung trennt
            ("schraeg 10/4 = 10.77, Hoehe 130", 10, 4, 130, true),
            // ⭐ und der Gegenfall zur geloeschten Zeile `dz <= 0`: bei Hoehe
            // GLEICH dem Gelaende ist dz 0, und dann trifft nur d = 0 — das
            // Original TRIFFT hier, wir hielten es fuer unverwundbar.
            ("Bodenhoehe senkrecht darueber (dz 0)", 0, 0, 0, true),
        };
        int slot = 0;
        foreach (var x in _special) slot = Mathf.Max(slot, x.Slot + 1);
        foreach (var f in faelle)
        {
            var a = new Special
            {
                Slot = slot++, Kind = 4, Name = "Probeflieger", TypeName = "Probeflieger",
                Owner = gegner, Stored = false,
                Col = s.Col + f.Dc, Row = s.Row + f.Dr,
                Pos = s.Pos + new Vector2(f.Dc * TileW, f.Dr * TileH),
                Hp = FlakProbeHuelle, HpMax = FlakProbeHuelle, Defence = 0, Attack = 0,
                Ammo = 0, AmmoMax = 0, Fuel = 9999, FuelMax = 9999,
                Alt = f.Alt, Sollhoehe = f.Alt,
            };
            // ⚠ Goal auf die eigene Lage: sonst greift der Steigflug (Abstand
            // zum Flugziel > 6 Zellen -> +2 je Takt) und die Hoehe wandert
            // waehrend der Messung weg. Der Fall soll STEHEN.
            a.Goal = a.Pos;
            _special.Add(a);
            _flakProbeFaelle.Add((f.T, a.Slot, f.Soll));
            _flakProbeHalt.Add((a.Slot, a.Pos, a.Col, a.Row, f.Alt));
        }
        _flakProbeTakte = 1;
    }

    /// <summary>Die Zeile der Probe, nach <see cref="FlakProbeTakte"/> Takten.</summary>
    private const int FlakProbeTakte = 120;

    /// <summary>
    /// Die Huelle eines Probefliegers — <b>60</b>, und die Zahl ist mit Absicht
    /// klein.
    ///
    /// <para>⚠ Sie stand auf 1000, und damit hat die Probe den ABSCHUSS nie
    /// erreicht: 28 Schuesse nahmen 1000 auf 810 herunter, fuer einen Abschuss
    /// haette es rund 140 gebraucht. Der Aufprallschaden (Absturz.cs) war so
    /// nicht messbar — die Kette endete beim Beschuss. Mit 60 fallen innerhalb
    /// des Messfensters welche, und der ganze Weg vom Kegel bis zum Aufprall
    /// steht in einer Zeile.</para></summary>
    private const int FlakProbeHuelle = 60;

    private void FlakProbeTakt()
    {
        if (_flakProbe) { FlakProbeAufbauen(); return; }
        if (_flakProbeTakte <= 0) return;

        // festheften, siehe _flakProbeHalt
        foreach (var (pslot, pos, col, row, alt) in _flakProbeHalt)
        {
            var a = _special.Find(x => x.Slot == pslot);
            if (a == null || a.Dead) continue;
            // ⚠⚠ EIN STUERZENDER FLIEGER WIRD NICHT FESTGEHEFTET. Sonst setzt
            // die Probe seine Hoehe je Takt zurueck, er erreicht den Boden nie
            // und der Aufprall — der eigentliche Befund — tritt nicht ein.
            if (a.Absturz) continue;
            a.Pos = pos; a.Col = col; a.Row = row;
            a.Alt = alt; a.Sollhoehe = alt;
            a.Goal = pos; a.PlayerGoal = null; a.TurnPoint = null;
        }

        if (++_flakProbeTakte < FlakProbeTakte) return;
        _flakProbeTakte = -1;

        var sb = new System.Text.StringBuilder("flak-probe");
        sb.AppendLine();
        // (1) DIE REGEL, je Fall — reine Rechnung, fuer alle sechs pruefbar
        int ok = 0;
        for (int k = 0; k < _flakProbeFaelle.Count; k++)
        {
            var (titel, pslot, soll) = _flakProbeFaelle[k];
            var halt = _flakProbeHalt[k];
            var a = _special.Find(x => x.Slot == pslot);
            // ⚠ DIESELBE Rechnung wie der Kegel, nicht eine eigene — siehe
            // FlakAbstand. Hier stand `Abs(Col - SchuetzeCol)`, also allein die
            // SPALTENdifferenz: ein schraeger Fall war damit gar nicht
            // pruefbar.
            int d = FlakAbstand(halt.Col - _flakProbeSchuetzeCol,
                                halt.Row - _flakProbeSchuetzeRow,
                                halt.Pos, _flakProbeSchuetzePos);
            bool imRing = FlakImRing(halt.Alt, _flakProbeGelaende, d);
            bool passt = imRing == soll;
            if (passt) ok++;
            sb.AppendLine($"   {(passt ? "ok  " : "⚠ FALSCH")} {titel}: " +
                          $"dz {Mathf.Abs(halt.Alt - _flakProbeGelaende) / FlakHoehenTeiler}, " +
                          $"d {d} -> {(imRing ? "im Ring" : "nicht im Ring")} " +
                          $"(Soll {(soll ? "im Ring" : "nicht im Ring")})" +
                          (a != null ? $"   Huelle {a.Hp}/{FlakProbeHuelle}, Hoehe {a.Alt}" : "   WEG"));
        }
        sb.AppendLine($"   {ok} von {_flakProbeFaelle.Count} Faellen richtig (die REGEL)");
        // (2) DIE KETTE, einmal. ⚠ Mit EINER Flak kommt je Nachladezeit nur ein
        // Ziel dran — der erste Treffer beendet den Kegel, und danach laedt sie
        // 12..17 Takte. Dass hier nur ein Flieger Schaden hat, ist also richtig
        // und kein Fehlbefund.
        int getroffen = 0;
        foreach (var (_, pslot, _) in _flakProbeFaelle)
        {
            var a = _special.Find(x => x.Slot == pslot);
            if (a != null && (a.Dead || a.Absturz || a.Hp < FlakProbeHuelle)) getroffen++;
        }
        sb.Append($"   Kette: Auftraege {FlakAuftraege}, Schuesse {FlakSchuesse}, " +
                  $"Abschuesse {FlakAbschuesse}, beschossene Flieger {getroffen} " +
                  "(Soll >= 1 — eine Flak bedient je Nachladezeit genau eines)");
        // ⚠ 19.09.2026 — hier standen zwei irreführende Hinweise. Die REGEL ist
        // reine Rechnung; kein Schalter kann sie ändern, also bleibt die Zeile
        // »6 von 6« unter jedem Nullmodell stehen, und das ist richtig. Wirken
        // müssen die Schalter auf die KETTE — und nur das steht jetzt da.
        if (FlakKegelAus)
            sb.Append("   ⚠ Nullmodell --flak-kegel-aus: die KETTE muss 0/0/0 sein " +
                      "(die Regel bleibt 6 von 6, sie ist Rechnung)");
        if (FlakTrefferAus)
            sb.Append("   ⚠ Nullmodell --flak-treffer-aus: Auftraege > 0, aber Schuesse 0");
        if (FlughoeheAlt)
            sb.Append("   ⚠ --flughoehe-alt aendert HIER nichts: die Probe heftet die Hoehe " +
                      "fest. Der Schalter wirkt im echten Lauf, siehe --flak-check");
        GD.Print(sb);
    }
}
