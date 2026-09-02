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
        return sb.ToString();
    }

    /// <summary>0 bestanden · 1 durchgefallen · 2 nichts gemessen.</summary>
    public int AufwertungProbeRc()
    {
        if (AufwertungAlt) return AufwertungAngewandt == 0 ? 0 : 1;
        if (AufwertungAngewandt == 0) return 2;
        int erwartet = AufwertungAngewandt / 4;
        if (AufwertungImmerTank) return AufwertungWurf[2] == AufwertungAngewandt ? 0 : 1;
        for (int k = 0; k < 4; k++)
            if (System.Math.Abs(AufwertungWurf[k] - erwartet) > erwartet) return 1;
        return 0;
    }
}
