using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>`--aufwertung-probe` — WÄCHST DER TANK, UND WÄCHST ER RICHTIG OFT?</b>
///
/// <para>Zu <see cref="MapEntityLayer.Aufwerten"/> (Simulation/Aufwertung.cs).
/// Er hatte gemeldet: »ich komm teilweise gar nicht bis zu dem Forscher
/// gefahren, da ist mein Sprit vorher alle«. Der Verbrauch war in Ordnung, der
/// Tank wuchs nur nie.</para>
///
/// <para><b>Zwei Fragen, und sie brauchen zwei verschiedene Messungen:</b></para>
///
/// <list type="number">
///   <item><b>Trägt die Kette?</b> Bauteil <c>+0x1A</c> → Entwurf → lebende
///   Einheit. Das ist mit einem Wurf nicht zu messen, weil der Tankarm nur in
///   einem von vier Fällen kommt — darum gibt es
///   <c>--aufwertung-immer-tank</c>, das den Wurf übergeht. ⚠ Dieser Schalter
///   ist KEINE Nachbildung des Originals, er ist ein Messwerkzeug.</item>
///   <item><b>Wird richtig gewürfelt?</b> Über viele Aufwertungen muss jeder
///   der vier Arme rund ein Viertel bekommen. ⭐ NULLMODELL: bei 400 Würfen
///   erwartet man je 100; die Probe meldet die vier Zahlen einzeln, damit ein
///   »immer derselbe Arm« sofort auffällt — genau der Fehlschluss, zu dem die
///   ausgelieferte Tafel verführt (dort steht der Wähler überall auf 0).</item>
/// </list>
///
/// <para>Aufruf:</para>
/// <code>
///   --campaign=3 --aufwertung-probe --quit-after=10
///   --campaign=3 --aufwertung-probe --aufwertung-immer-tank --quit-after=10
///   --campaign=3 --aufwertung-probe --aufwertung-alt --quit-after=10
/// </code>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private bool _aufProbeGelaufen;

    /// <summary>Der Prüflauf. Läuft einmal, wenn die Karte steht.</summary>
    public string AufwertungProbeLine()
    {
        if (_aufProbeGelaufen) return "";
        _aufProbeGelaufen = true;

        var sb = new System.Text.StringBuilder("aufwertung-probe:\n");

        // Ein Fahrwerk suchen, das auf der Karte wirklich vorkommt — eine
        // Messung an einem Bauteil, das niemand fährt, sagt nichts über das
        // Spiel aus.
        int fahrwerk = -1, beispiel = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.Dead || e.Owner != 0 || e.UnitType < 0 || e.FuelMax <= 0) continue;
            // ⚠ UnitType, nicht Chassis — siehe Aufwertung.EinheitenNachziehen.
            fahrwerk = e.UnitType; beispiel = i; break;
        }
        if (fahrwerk < 0)
            return sb + "   ⚠ keine eigene Einheit mit Fahrwerk und Tank — MISST NICHTS\n";

        var vorher = BauteilFuer(0, fahrwerk);
        if (vorher == null)
            return sb + $"   ⚠ Bauteil {fahrwerk} hat keine Grundzeile — MISST NICHTS\n";
        int tankVorher = vorher[0x1A] | (vorher[0x1A + 1] << 8);
        int stufeVorher = vorher[0x01];
        var eb = _entities[beispiel];
        sb.Append($"   Fahrwerk {fahrwerk}, Stufe {stufeVorher}, Tank {tankVorher}" +
                  $" | Beispieleinheit Platz {eb.Slot}: {eb.Fuel}/{eb.FuelMax}\n");

        // ---- Teil 1: trägt die Kette? -------------------------------------
        int wurf = Aufwerten(0, fahrwerk);
        var nachher = BauteilFuer(0, fahrwerk)!;
        int tankNachher = nachher[0x1A] | (nachher[0x1A + 1] << 8);
        sb.Append($"   1. eine Aufwertung: Wurf {(wurf < 0 ? "keiner (abgelehnt)" : wurf.ToString())}" +
                  $", Stufe {stufeVorher} -> {nachher[0x01]}" +
                  $", Tank {tankVorher} -> {tankNachher}" +
                  (wurf == 2 ? "   (Tankarm)" : wurf < 0 ? "" : "   (anderer Arm, Tank bleibt)") + "\n");
        sb.Append($"      Beispieleinheit danach: {eb.Fuel}/{eb.FuelMax}" +
                  (wurf == 2 && eb.FuelMax == tankNachher
                       ? "   ✔ nachgezogen"
                       : wurf == 2 ? "   ⚠⚠ NICHT nachgezogen — die Kette traegt nicht"
                                   : "   (kein Tankwurf, nichts zu erwarten)") + "\n");

        // ---- Teil 3: bekommt ein NEUBAU den neuen Tank — und NUR bei diesem
        // Spieler? ----------------------------------------------------------
        // Das ist 0x4B24B0 -> 0x4B1FB0 (Entwurf aus dem Spielerblock) und der
        // Aufsteller 0x4B1840, der @0x4B1BFE aus sec47[Entwurf + 200*Spieler]
        // liest. Gemessen wird am ECHTEN Bauweg (SpawnReinforcement), nicht an
        // der Rechnung allein — ein Pruefstand, der die geaenderte Stelle nicht
        // anfasst, belegt nichts.
        //
        // ⭐ NULLMODELL, drei Ausgaenge:
        //   Spieler 0 neu, Spieler 1 alt   -> je Spieler, wie das Original
        //   beide neu                      -> die Aufwertung wirkt GLOBAL
        //   beide alt                      -> sie wirkt auf Neubauten GAR NICHT
        //                                     (der Stand vor dem 03.09.2026,
        //                                      --entwuerfe-global-alt)
        //
        // ⚠ Ein Wurf ist kein Befund: der Tankarm kommt in einem von vier
        // Faellen. Bis er einmal getroffen hat, wird weiter aufgewertet (die
        // Stufe laesst hoechstens neun zu; 0.75^9 = 7.5 % Restwahrscheinlichkeit,
        // die Probe meldet den Fall). Mit --aufwertung-immer-tank ist es der
        // erste.
        int tankTreffer = wurf == 2 ? 1 : 0, versuche = 1;
        while (tankTreffer == 0 && versuche < 9 && !AufwertungAlt)
        {
            int w = Aufwerten(0, fahrwerk);
            versuche++;
            if (w == 2) tankTreffer++;
            if (w < 0) break;
        }
        var bauteil0 = BauteilFuer(0, fahrwerk)!;
        var bauteil1 = BauteilFuer(1, fahrwerk)!;
        int tank0 = bauteil0[0x1A] | (bauteil0[0x1A + 1] << 8);
        int tank1 = bauteil1[0x1A] | (bauteil1[0x1A + 1] << 8);
        sb.Append($"   3. Neubau: nach {versuche} Aufwertung(en) von Spieler 0, davon {tankTreffer} auf den Tank" +
                  $" — Bauteil {fahrwerk}: Spieler 0 = {tank0}, Spieler 1 = {tank1}\n");

        // Ein Entwurf mit diesem Fahrwerk, der in Block 0 UND Block 1 von sec47
        // steht (space_in nennt ihn `typ + 200*spieler`).
        int platz = -1; string entwurfName = "";
        foreach (var kv in _designBySlot)
        {
            if (kv.Key >= DesignsPerPlayer || kv.Value.Propulsion != fahrwerk) continue;
            if (!_designBySlot.ContainsKey(kv.Key + DesignsPerPlayer)) continue;
            platz = kv.Key; entwurfName = kv.Value.Name; break;
        }
        if (platz < 0)
        {
            sb.Append($"      ⚠ kein Entwurf mit Fahrwerk {fahrwerk} in Block 0 und 1 — Neubau NICHT gemessen\n");
            _neubauRc = 2;
        }
        else
        {
            // Erst die Rechnung, dann der Bau — damit sich »die Rechnung ist
            // falsch« von »der Bau liest die Rechnung nicht« trennen laesst.
            int rech0 = EntwurfFuer(0, _designBySlot[platz]).Fuel;
            int rech1 = EntwurfFuer(1, _designBySlot[platz + DesignsPerPlayer]).Fuel;
            int bau0 = -1, bau1 = -1;
            int s0 = SpawnReinforcement(platz, eb.Col, eb.Row, 0);
            int s1 = SpawnReinforcement(platz, eb.Col, eb.Row, 1);
            for (int i = _entities.Count - 1; i >= 0; i--)
            {
                var x = _entities[i];
                if (x.IsProp || x.Dead) continue;
                if (bau0 < 0 && s0 >= 0 && x.Owner == 0 && x.Slot == s0) bau0 = x.FuelMax;
                if (bau1 < 0 && s1 >= 0 && x.Owner == 1 && x.Slot == s1) bau1 = x.FuelMax;
            }
            sb.Append($"      Entwurf {platz} '{entwurfName}': gerechnet Spieler 0 = {rech0}, Spieler 1 = {rech1}" +
                      $" | GEBAUT Spieler 0 = {bau0}, Spieler 1 = {bau1}   (alt {tankVorher})\n");
            bool gemessen = bau0 >= 0 && bau1 >= 0;
            bool jeSpieler = bau0 == tank0 && bau1 == tank1 && tank0 != tank1;
            bool global = bau0 == tank0 && bau1 == tank0 && tank0 != tank1;
            bool wirkungslos = bau0 == tankVorher && bau1 == tankVorher && tank0 != tankVorher;
            if (!gemessen)
            {
                sb.Append("      ⚠⚠ Bau fehlgeschlagen (kein Platz?) — Neubau NICHT gemessen\n");
                _neubauRc = 2;
            }
            else if (tankTreffer == 0)
            {
                sb.Append($"      ⚠ in {versuche} Aufwertungen kein Tankarm — nichts zu erwarten, " +
                          (bau0 == bau1 && bau0 == tankVorher ? "und beide alt, stimmig" : "⚠⚠ und trotzdem verschieden") + "\n");
                _neubauRc = bau0 == bau1 && bau0 == tankVorher ? 0 : 1;
            }
            else if (Simulation.DesignMath.EntwuerfeGlobalAlt)
            {
                sb.Append(wirkungslos
                    ? "      (--entwuerfe-global-alt: beide alt — der Stand vor dem 03.09.2026, wie erwartet)\n"
                    : "      ⚠⚠ --entwuerfe-global-alt steht, aber der Neubau hat den neuen Tank — der Gegenschalter greift nicht\n");
                _neubauRc = wirkungslos ? 0 : 1;
            }
            else
            {
                sb.Append(jeSpieler
                    ? "      ✔ JE SPIELER: Spieler 0 baut mit dem neuen Tank, Spieler 1 mit dem alten\n"
                    : global
                        ? "      ⚠⚠ GLOBAL: auch Spieler 1 baut mit dem neuen Tank — die Aufwertung kennt keinen Spieler\n"
                        : wirkungslos
                            ? "      ⚠⚠ WIRKUNGSLOS: der Neubau bekommt den alten Tank — EntwuerfeNachziehen traegt nicht\n"
                            : "      ⚠⚠ UNERWARTET: passt zu keinem der drei Ausgaenge\n");
                _neubauRc = jeSpieler ? 0 : 1;
            }
            if (rech0 != bau0 || rech1 != bau1)
                sb.Append("      ⚠⚠ Rechnung und Bau weichen ab — der Bauweg liest den Spielerentwurf nicht\n");
        }
        sb.Append($"      Nachziehen: {EntwuerfeNachgezogen}x gelaufen, {EntwuerfeGerechnet} Entwuerfe gerechnet\n");

        // ---- Teil 2: wird richtig gewürfelt? ------------------------------
        // ⚠ Die Stufe ist bei 9 zu Ende, also lässt sich am selben Bauteil nicht
        // 400-mal aufwerten. Für die Wurfverteilung wird darum eine frische
        // Zeile je Runde genommen: der Zähler AufwertungWurf läuft über alle.
        int vorherAngewandt = AufwertungAngewandt;
        for (int runde = 0; runde < 400; runde++)
        {
            int b = 0xA0 + (runde % 16);
            var z = BauteilFuer(0, b);
            if (z == null) continue;
            if (z[0x01] >= 9) z[0x01] = 0;      // nur für die Wurfmessung zurückgestellt
            Aufwerten(0, b);
        }
        int n = AufwertungAngewandt - vorherAngewandt;
        sb.Append($"   2. Wurfverteilung ueber {n} Aufwertungen:");
        for (int k = 0; k < 4; k++)
            sb.Append($"  [{k}]={AufwertungWurf[k]}" + (k == 2 ? "(Tank)" : ""));
        sb.Append("\n");
        int erwartet = n / 4;
        int groessteAbweichung = 0;
        for (int k = 0; k < 4; k++)
            groessteAbweichung = System.Math.Max(groessteAbweichung,
                                                 System.Math.Abs(AufwertungWurf[k] - erwartet));
        sb.Append($"      ⭐ NULLMODELL: je Arm rund {erwartet}; groesste Abweichung " +
                  $"{groessteAbweichung}\n");
        sb.Append(AufwertungAlt
            ? "      (--aufwertung-alt: es wird gar nicht aufgewertet, 0 ist richtig)\n"
            : AufwertungImmerTank
                ? "      (--aufwertung-immer-tank: alles auf [2], das ist der Messschalter)\n"
                : n == 0
                    ? "      ⚠⚠ NICHTS angewandt — die Probe belegt nichts\n"
                    : groessteAbweichung > erwartet
                        ? "      ⚠⚠ EIN ARM UEBERWIEGT — es wird nicht gewuerfelt\n"
                        : "      bestanden — alle vier Arme kommen vor.\n");
        if (AufwertungOhneZeile > 0)
            sb.Append($"      ⚠ {AufwertungOhneZeile}x kein Tafeleintrag " +
                      "('for_vyv not found!')\n");

        // ---- Teil 4: der SATZ — nach allen Aufwertungen von Spieler 0 -------
        // Teil 3 war EIN Treffer. Hier, nach den 400 Aufwertungen aus Teil 2
        // (alle auf Spieler 0, ueber alle 16 Fahrwerke), muss Spieler 1 immer
        // noch beim alten Tank stehen, und der Entwurf von Spieler 0 muss dem
        // Bauteil folgen, das inzwischen viele Tankarme gesehen hat.
        if (platz >= 0 && !AufwertungAlt)
        {
            var b0 = BauteilFuer(0, fahrwerk)!;
            var b1 = BauteilFuer(1, fahrwerk)!;
            int t0 = b0[0x1A] | (b0[0x1A + 1] << 8);
            int t1 = b1[0x1A] | (b1[0x1A + 1] << 8);
            int r0 = EntwurfFuer(0, _designBySlot[platz]).Fuel;
            int r1 = EntwurfFuer(1, _designBySlot[platz + DesignsPerPlayer]).Fuel;
            bool global = Simulation.DesignMath.EntwuerfeGlobalAlt;
            sb.Append($"   4. nach {n} weiteren Aufwertungen von Spieler 0: Bauteil {fahrwerk} " +
                      $"Spieler 0 = {t0}, Spieler 1 = {t1}; Entwurf '{entwurfName}' " +
                      $"Spieler 0 = {r0}, Spieler 1 = {r1}\n");
            bool ok = global ? (r0 == tankVorher && r1 == tankVorher)
                             : (r0 == t0 && r1 == t1 && t1 == tankVorher);
            sb.Append(ok
                ? (global ? "      (--entwuerfe-global-alt: beide beim Grundwert, wie erwartet)\n"
                          : $"      ✔ 0 von {n + versuche} Aufwertungen sind nach Spieler 1 durchgeschlagen; Spieler 0 folgt seinem Bauteil\n")
                : "      ⚠⚠ der Satz stimmt nicht — siehe Zahlen\n");
            if (!ok && _neubauRc == 0) _neubauRc = 1;
        }
        return sb.ToString();
    }

    /// <summary>Ergebnis von Teil 3/4: 0 bestanden · 1 durchgefallen · 2 nicht
    /// gemessen. Vor dem 03.09.2026 gab es diesen Teil nicht.</summary>
    private int _neubauRc = 2;

    /// <summary>0 bestanden · 1 durchgefallen · 2 nichts gemessen.</summary>
    public int AufwertungProbeRc()
    {
        if (AufwertungAlt) return AufwertungAngewandt == 0 ? 0 : 1;
        if (AufwertungAngewandt == 0) return 2;
        if (_neubauRc != 0) return _neubauRc;          // Teil 3/4, seit 03.09.2026
        int erwartet = AufwertungAngewandt / 4;
        if (AufwertungImmerTank) return AufwertungWurf[2] == AufwertungAngewandt ? 0 : 1;
        for (int k = 0; k < 4; k++)
            if (System.Math.Abs(AufwertungWurf[k] - erwartet) > erwartet) return 1;
        return 0;
    }
}
