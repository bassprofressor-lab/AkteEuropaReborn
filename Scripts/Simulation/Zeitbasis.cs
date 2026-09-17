namespace AkteEuropaReborn.Simulation;

/// <summary>
/// <b>DIE ZEITBASIS — wie viele Originaltakte auf eine Sekunde gehen.</b>
///
/// <para><b>⚠⚠ DIESE ZAHL IST UNSERE SETZUNG, NICHT GELESEN.</b> Das ist der wichtigste Satz
/// dieser Datei, und er ist das Ergebnis einer Gegenlesung (Fable, 17.09.2026,
/// <c>berichte/zeitbasis-fable.md</c>):</para>
///
/// <para><b>Das Original legt gar keine Taktfrequenz fest.</b> Ein Zeitgeber für 50 Hz ist da —
/// <c>SetTimer(hwnd, 1, 20 ms)</c>, C <c>0x415BC5</c> / F <c>0x415A05</c>, je genau einmal —,
/// aber WinMain überspringt ihn: es setzt <c>byte[0x53920C] := 1</c> von Hand, wenn
/// <c>byte[0x4F6FB0]</c> (F <c>0x4F5FAC</c>) ungleich 0 ist, und dieses Byte steht im
/// Dateiabbild BEIDER Fassungen auf <b>01</b> — Vollerhebung über die Relokationstafel:
/// <b>0 Schreiber, 1 Leser</b>. Der Timer ist toter Code. Die Hauptschleife läuft frei,
/// gebremst allein vom DirectDraw-<c>Flip</c> auf den Strahlrücklauf (C <c>0x415CBD</c> /
/// F <c>0x415AFD</c>), ohne Sleep und ohne Nachholen. Es gilt also:</para>
///
/// <para><b>Takte je Sekunde = Bilder je Sekunde × Geschwindigkeitsstufe</b> (Stufen 1/2/3,
/// Vorgabe 1; Tasten Ziffernblock <c>+</c>/<c>−</c> über die Befehle <c>0x3DD</c>/<c>0x3DC</c>).
/// Auf der Hardware von 1997 war das die Bildwiederholrate des Schirms.</para>
///
/// <para><b>Warum trotzdem 50.</b> Drei Anhaltspunkte, keiner davon ein Beweis:
/// <list type="number">
///   <item>50 ist der Entwurfswert des abgeschalteten Zeitgebers — die Zahl, mit der die
///         Entwickler gerechnet haben.</item>
///   <item>Die einzige vorhandene Messung stammt aus einer Spielaufnahme: 5–6 s je Spielminute
///         (250 Takte) ergeben <b>42–50 Takte/s</b>. Sie spricht gegen 60.</item>
///   <item>Alle bisherigen Lesungen des Projekts rechnen mit 50; <c>MissionScript</c> tat es
///         schon vorher.</item>
/// </list>
/// ⚠ Dagegen steht ein ungeklärter Punkt: zwei Originaltexte (ENCYCLOG, »Radarmast ungefähr
/// 6 Minuten« für 6375 Takte) passen nur zu einer rund dreimal langsameren Uhr. Vermutlich für
/// eine geplante Uhr geschrieben — belegen lässt sich das nicht.</para>
///
/// <para><b>Am laufenden Original nachmessen geht nicht</b> (seine Ansage 17.09.2026): das Spiel
/// läuft unter Windows 11 nicht sauber, und beim Fensterwechsel stürzt es ab. Das Werkzeug dafür
/// liegt trotzdem bereit — <c>aekernel-tools/takt_messen.py</c>, nur lesend, trennt nach
/// Geschwindigkeitsstufe —, falls sich das je ändert (VM, dosbox, wine).</para>
///
/// <para><b>Deshalb der Schalter.</b> <c>--originalhz=N</c> setzt die Zahl für einen Lauf, damit
/// sich zwei Werte im Spiel vergleichen lassen, statt sie zu erraten. <c>--zeitbasis-alt</c> ist
/// der Gegenschalter auf den Stand vor dem 17.09.2026 (drei verschiedene Zahlen: Nachladen
/// 18,2/s, Wirtschaft 16/s, Missionsskript 50/s).</para>
/// </summary>
public static class Zeitbasis
{
    /// <summary>Der Stand vor dem 17.09.2026: die Wirtschaft rechnete 16 Originaltakte je
    /// Sekunde. Er steht hier, damit <see cref="Alt"/> ihn wiederherstellen kann.</summary>
    public const int AlteWirtschaftsHz = 16;

    /// <summary>Die gesetzte Zahl. Vorgabe 50 — siehe den Kopf dieser Datei.</summary>
    public static int OriginalHz { get; private set; } = 50;

    /// <summary><c>--zeitbasis-alt</c> — DER GEGENSCHALTER: jede Uhr rechnet wieder mit ihrer
    /// eigenen alten Zahl, so wie bis zum 16.09.2026. Damit muss <c>--zeitbasis-check</c>
    /// durchfallen.</summary>
    public static bool Alt { get; private set; }

    /// <summary>Originaltakte je Sekunde für die WIRTSCHAFT (Reparatur, Produktion, Ausbau,
    /// Einnahme, Förderung, Bahn). Vor dem 17.09.2026 waren das fest 16.</summary>
    public static int WirtschaftsHz => Alt ? AlteWirtschaftsHz : OriginalHz;

    /// <summary>Sekunden je Einheit Nachladezeit — Einheit <c>+0x3D</c> zählt in
    /// Originaltakten, und <c>+0x32</c> sinkt genau einen je Takt (<c>0x4074F7</c>,
    /// F <c>0x407420</c>; 12 Schreiber / 19 Leser abgeklopft, kein zweiter Zähler).
    ///
    /// <para>Vor dem 17.09.2026 stand hier <c>1,1/20</c> = 0,055 s, also eine vierte,
    /// unbegründete Zahl (18,2 Takte/s). Sie kam daher, dass wir den Kartenwert 20 auf eine
    /// »sich richtig anfühlende« Sekundenzahl abgebildet hatten, statt ihn als Takte zu
    /// lesen.</para></summary>
    public static float NachladeTakt => Alt ? 1.1f / 20f : 1f / OriginalHz;

    /// <summary>Setzt die Zahl aus <c>--originalhz=N</c>. Werte außerhalb 1…1000 werden
    /// abgewiesen, damit ein Vertipper nicht still die halbe Simulation anhält.</summary>
    public static bool Setzen(int hz)
    {
        if (hz < 1 || hz > 1000) return false;
        OriginalHz = hz;
        return true;
    }

    /// <summary><c>--zeitbasis-alt</c>.</summary>
    public static void AufAltSetzen() => Alt = true;

    /// <summary>Die Zeile für den Prüfstand und für den Kopf jedes Laufs.</summary>
    public static string Zeile()
        => Alt
            ? $"zeitbasis: --zeitbasis-alt — Wirtschaft {AlteWirtschaftsHz}/s, Nachladen " +
              $"{1f / (1.1f / 20f):0.0}/s, Missionsskript {OriginalHz}/s (der alte, uneinheitliche Stand)"
            : $"zeitbasis: {OriginalHz} Originaltakte/s für alles — Nachladen, Reparatur, " +
              $"Produktion, Ausbau, Einnahme, Förderung, Bahn und Missionsskript (UNSERE SETZUNG, " +
              $"siehe Simulation/Zeitbasis.cs)";
}
