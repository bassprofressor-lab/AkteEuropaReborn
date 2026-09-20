namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>DAS FLUGTEMPO — die Stufe <c>sp</c> und ihre Regelung</b> (20.09.2026,
/// bug-348, beim Bau der Handsteuerung Luft aufgefallen).
///
/// <para><b>Wie es aufgefallen ist:</b> die Handsteuerung schreibt je Takt
/// <c>dir ±6</c> und <c>sp ±1</c>. Beim Nachlesen, WELCHE Satzfelder das sind,
/// stellte sich heraus, dass unser Lader die beiden <b>vertauscht</b> hatte:
/// <c>Dir</c> kam aus <b>+0x0C</b> — und das ist <c>sp</c>. Die Richtung steht
/// als <b>Wort</b> bei <b>+0x0A</b>. Siehe <see cref="Special.Dir"/> und
/// <see cref="Special.Stufe"/> für Lesung und Nullmodell.</para>
///
/// <para><b>Was daraus folgte</b> und hier steht: das Original fliegt mit
/// <c>sp</c>, nicht mit <c>sp_oben</c>, und <c>sp</c> wird <b>geregelt</b> —
/// ein startendes Flugzeug steht (<c>sp := 0</c>) und fährt hoch, ein
/// anfliegendes bremst. Bei uns flog bisher jede Maschine <b>sofort und immer
/// mit Höchsttempo</b>.</para>
///
/// <para><b>GELESEN</b> (C, byte-genau):</para>
/// <code>
///   air_takeoff @0x426095  mov word [0x6ddf7a], 0xb4   ; dir := 180
///               @0x42609E  mov byte [0x6ddf7c], bl     ; sp  := 0  (ebx=0 @0x426062)
///
///   Regelung im Flugtakt @0x424E27..0x424EF1
///     Ziel ist ein FLUGZEUG (+0x2E >= 20000, @0x424E31..0x424E4E):
///         soll := sp DES ZIELS  (idx = (+0x2E − 20000), idx·68 + 0x6ddf7c)
///         sp &lt; soll -> schneller   @0x424E5D
///         sp &gt; soll -> langsamer   @0x424E7A
///         gleich     -> nichts      @0x424E57
///     sonst (@0x424E9A):
///         Order2 (+0x11) == 3            -> langsamer
///         Entfernung zum Flugziel &gt; 6 -> schneller      @0x424EA8 (6.0f)
///         Entfernung &lt;= 0             -> langsamer      @0x424EB1
///         dazwischen: sp &gt;= 4 -> langsamer, sonst schneller  @0x424EB9
///
///     schneller:  sp_oben (+0x0D) &gt; sp  -> sp+1   @0x424EC9/@0x424ED1
///     langsamer:  sp_unten (+0x18) &lt; sp -> sp−1   @0x424EE0/@0x424EF1
/// </code>
///
/// <para>⚠ <b>Was hier UNSERE Setzung ist:</b> die <b>Einheit</b> der
/// Schwellen 6 und 8. Im Original ist es ein Gleitkommawert aus dem
/// Rahmen (<c>[esp+0x1c]</c>), gerechnet wird er nicht an dieser Stelle. Dass
/// es <b>Zellen</b> sind, ist die naheliegende Deutung (der Satz zählt in
/// Zellen plus Feinlage 0..39) — belegt ist sie <b>nicht</b>. Sie steht als
/// <see cref="AnflugZellen"/> beieinander, damit sie eine Stelle hat.</para>
///
/// <para>⚠ Der zweite Block @0x424EF6 mit der Schwelle <b>8.0</b> ist
/// dieselbe Regelung für einen anderen Fall (<c>al</c> ≠ 0 weiter oben) und
/// <b>nicht gebaut</b> — welcher Fall das ist, ist ungelesen. Wer ihn baut,
/// braucht nur <see cref="AnflugZellen"/> zu verzweigen.</para>
///
/// <para>Gegenschalter <c>--flugtempo-alt</c>: geflogen wird wieder mit
/// <c>Speed</c> (= <c>sp_oben</c>) und ohne jede Regelung — der Stand bis zum
/// 20.09.2026. Messzeile <c>--flugtempo-check</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--flugtempo-alt</c> — das Nullmodell: volles <c>sp_oben</c>
    /// ab dem ersten Takt, keine Regelung. Darunter MUSS
    /// <see cref="FlugtempoGeregelt"/> auf 0 fallen und die Zahl der Maschinen,
    /// die je mit Stufe 0 standen, ebenfalls.</summary>
    public static bool FlugtempoAlt;

    /// <summary>Die Schwelle des Anflugs, <c>0x40C00000 = 6.0f</c> @0x424EA8.
    /// ⚠ Die EINHEIT ist unsere Deutung (Zellen), siehe Klassenkopf.</summary>
    private const float AnflugZellen = 6f;

    /// <summary>Die Stufe, auf die das Original im Anflug einregelt
    /// (@0x424EB9 <c>cmp al, 4</c>).</summary>
    private const int AnflugStufe = 4;

    /// <summary>Die Richtung, die <c>air_takeoff</c> @0x426095 fest setzt.</summary>
    public const int StartRichtung = 180;

    /// <summary>Wie oft die Regelung eine Stufe verändert hat, und wie oft sie
    /// dabei hoch- bzw. heruntergegangen ist. Die Kontrollzahl ist die Summe.</summary>
    public int FlugtempoGeregelt, FlugtempoSchneller, FlugtempoLangsamer;

    /// <summary>Wieviele Maschinen seit dem Start je mit Stufe 0 gestanden
    /// haben — der Beleg, dass der Start bei 0 beginnt und nicht bei
    /// Höchsttempo.</summary>
    public int FlugtempoAusDemStand;

    /// <summary>
    /// <b>Womit geflogen wird.</b> Eine Grösse, eine Funktion — jeder, der ein
    /// Flugtempo braucht, fragt hier, auch der Prüfstand. (Die Do-Not-Repeat
    /// vom 19.09.: ein Prüfstand mit eigener Formel prüft sich selbst.)
    /// </summary>
    public static float FlugStufe(Special a)
        => FlugtempoAlt ? Mathf.Max(1, a.Speed) : a.Stufe;

    /// <summary>Was <c>air_takeoff</c> @0x426095/@0x42609E an Richtung und
    /// Stufe setzt. Wer ein Flugzeug aus dem Hangar holt, ruft das hier —
    /// sonst startet es mit den Werten von seinem letzten Flug.</summary>
    public void FlugtempoStart(Special a)
    {
        a.Dir = StartRichtung;                 // @0x426095  mov word [..], 0xb4
        if (FlugtempoAlt) { a.Stufe = a.Speed; return; }
        a.Stufe = 0;                           // @0x42609E  bl = 0
        FlugtempoAusDemStand++;
    }

    /// <summary>
    /// <b>Die Regelung, je Takt und Flugzeug</b> — @0x424E27..0x424EF1.
    /// </summary>
    /// <param name="a">die Maschine</param>
    /// <param name="zielFlugzeug">das verfolgte FLUGZEUG, oder null. Im
    /// Original ist das <c>+0x2E >= 20000</c>; bei uns ist
    /// <see cref="Special.Target"/> ein Eintrag der Einheitenliste, ein
    /// verfolgtes Flugzeug steht also woanders — darum reicht der Aufrufer es
    /// herein, statt dass diese Stelle raten muss.</param>
    public void FlugtempoTakt(Special a, Special? zielFlugzeug)
    {
        if (FlugtempoAlt) { a.Stufe = a.Speed; return; }

        bool schneller;
        if (zielFlugzeug != null)
        {
            // @0x424E31..0x424E57 — auf das Tempo des Verfolgten einregeln.
            int soll = zielFlugzeug.Stufe;
            if (a.Stufe == soll) return;                     // @0x424E57 je
            schneller = a.Stufe < soll;
        }
        else if (a.Order2 == 3)
        {
            schneller = false;                               // @0x424EA0/@0x424ED8
        }
        else
        {
            // @0x424EA4..0x424EBB — nach der Entfernung zum Flugziel.
            // ⚠ Kein Flugziel heisst »nichts anzufliegen«: das Original hat an
            // dieser Stelle IMMER einen Wert im Rahmen, wir nicht. Ohne Ziel
            // fliegt die Maschine geradeaus (AirDrift), und dafuer ist
            // »beschleunigen bis sp_oben« der Fall > 6.
            float dz = a.Goal is { } g ? a.Pos.DistanceTo(g) / TileW : float.MaxValue;
            if (dz > AnflugZellen) schneller = true;         // @0x424EAD jg
            else if (dz <= 0f) schneller = false;            // @0x424EB1 jle
            else schneller = a.Stufe < AnflugStufe;          // @0x424EBB jae
        }

        if (schneller)
        {
            if (a.Speed <= a.Stufe) return;                  // @0x424EC9 jbe
            a.Stufe++; FlugtempoSchneller++;
        }
        else
        {
            if (a.StufeUnten >= a.Stufe) return;             // @0x424EE9 jge
            a.Stufe--; FlugtempoLangsamer++;
        }
        FlugtempoGeregelt++;
    }

    /// <summary>Die Messzeile — <c>--flugtempo-check</c>.
    ///
    /// <para>⚠ Die KONTROLLZAHL ist <see cref="FlugtempoGeregelt"/> gegen
    /// <see cref="FlugtempoSchneller"/> + <see cref="FlugtempoLangsamer"/>:
    /// jede Regelung ist genau eines von beiden. Laufen sie auseinander, zählt
    /// irgendwo ein Zweig doppelt.</para></summary>
    public string FlugtempoAuskunft()
    {
        int summe = FlugtempoSchneller + FlugtempoLangsamer;
        int fliegen = 0, stehen = 0, voll = 0;
        foreach (var a in _special)
        {
            if (a.Dead || a.Stored) continue;
            fliegen++;
            if (a.Stufe == 0) stehen++;
            if (a.Stufe >= a.Speed && a.Speed > 0) voll++;
        }
        return $"flugtempo-check: {FlugtempoGeregelt}x geregelt "
             + $"({FlugtempoSchneller} schneller, {FlugtempoLangsamer} langsamer = {summe} "
             + (summe == FlugtempoGeregelt ? "stimmt" : "⚠ LAEUFT AUSEINANDER") + ")"
             + $" · {FlugtempoAusDemStand}x aus dem Stand gestartet (Stufe 0)"
             + $" · jetzt in der Luft: {fliegen}, davon {stehen} mit Stufe 0 und "
             + $"{voll} auf sp_oben"
             + (FlugtempoAlt
                 ? "   [--flugtempo-alt: 0x geregelt und 0x aus dem Stand ist das SOLL]"
                 : "");
    }
}
