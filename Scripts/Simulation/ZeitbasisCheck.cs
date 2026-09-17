namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <c>--zeitbasis-check</c> — der Prüfstand zu <see cref="Simulation.Zeitbasis"/>.
///
/// <para>Er misst nicht, ob die Zahl RICHTIG ist — das kann niemand, die EXE legt keine
/// Taktfrequenz fest (siehe Zeitbasis.cs). Er misst das, was prüfbar ist: <b>dass alle Uhren
/// des Spiels dieselbe Zahl benutzen</b>. Bis zum 16.09.2026 waren es drei verschiedene
/// (Missionsskript 50, Wirtschaft 16, Nachladen 18,2) — und genau das zeigt der Gegenschalter
/// <c>--zeitbasis-alt</c>, mit dem der Prüfstand durchfallen muss.</para>
///
/// <para>Dazu die Umrechnungstafel bekannter Originaldauern. Sie ist kein Bestehen-Kriterium,
/// sondern die Plausibilitätsprobe zum Ansehen: ein Radarmast, der sechs Stunden läuft, wäre
/// ein Zeichen, dass die Zahl nicht stimmen kann.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string ZeitbasisCheck()
    {
        var sb = new System.Text.StringBuilder("zeitbasis-check\n");
        int hz = Simulation.Zeitbasis.OriginalHz;
        sb.AppendLine("   " + Simulation.Zeitbasis.Zeile());

        int skript = Campaign.MissionScript.TicksPerSecond;
        int wirtschaft = TickScale;
        float nachladen = ReloadTick > 0 ? 1f / ReloadTick : 0f;

        sb.AppendLine($"   Missionsskript: {skript}/s");
        sb.AppendLine($"   Wirtschaft (TickScale): {wirtschaft}/s");
        sb.AppendLine($"   Nachladen (1/ReloadTick): {nachladen:0.0}/s");

        bool einig = skript == hz && wirtschaft == hz
                     && System.Math.Abs(nachladen - hz) < 0.05f;

        // Die Umrechnungstafel: bekannte Dauern des Originals, in Takten gelesen.
        sb.AppendLine("   bekannte Dauern des Originals mit dieser Uhr:");
        (string was, int takte)[] tafel =
        {
            ("Nachladen, leichte Waffe (+0x3D = 20)", 20),
            ("Nachladen, Schw.Raketenwerfer (120)", 120),
            ("Reparatur: ein Trefferpunkt (4)", 4),
            ("Ausbau: ein Schritt von 100 (5)", 5),
            ("Mechaniker: eine Runde (30)", 30),
            ("Spielminute (250)", 250),
            ("Gebaeude im Bau (300)", 300),
            ("Radarmast, ganze Laufzeit (6375)", 6375),
        };
        foreach (var (was, takte) in tafel)
        {
            double s = takte / (double)hz;
            string zeit = s < 1 ? $"{s:0.000} s"
                        : s < 90 ? $"{s:0.0} s"
                        : $"{s / 60:0.0} min";
            sb.AppendLine($"     {was,-40} {takte,5} Takte = {zeit}");
        }

        sb.AppendLine(einig
            ? $"   ✅ BESTANDEN — alle drei Uhren rechnen mit {hz} Takten/s."
            : "   ❌ DURCHGEFALLEN — die Uhren rechnen verschieden " +
              "(genau das muss --zeitbasis-alt zeigen).");
        return sb.ToString();
    }
}
