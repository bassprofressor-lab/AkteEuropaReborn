namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐⭐ <b>DER ABSTURZ EINES FLUGZEUGS — UND SEIN AUFPRALLSCHADEN</b>
/// (20.09.2026).
///
/// <para><b>Anlass, und die Auflösung ist die eigentliche Geschichte.</b>
/// Gemeldet zu Kampagne 17: »und im original wird man auch von
/// versorgungshelis wie angegriffen, als würden die kamikaze machen gezielt auf
/// gebäude«. Die Lesung (<c>berichte/flugzeug-nebel-fable.md</c> §2, Fable) hat
/// alle drei naheliegenden Erklärungen <b>widerlegt</b>:</para>
/// <list type="number">
/// <item><b>Kein Angriff.</b> Die Arten 13/14 tragen Angriff 0 und Munition 0;
/// der Abwurf verlangt <c>uk 7 &amp;&amp; m_uk 7 &amp;&amp; Munition</c>, das
/// Luft-gegen-Luft-Stück <c>+0x16 &gt; 0</c>. Ein Versorger, der über
/// <c>ai_air_attack</c> gestartet wird, dreht bei »air R« sofort um.</item>
/// <item><b>Kein fremder Kunde.</b> Die Kundensuche läuft nur über den eigenen
/// Block <c>Besitzer·1000 … +1000</c> (@0x4279DF).</item>
/// <item><b>Kein Schaden bei der Abgabe</b> (0x424845…0x424CB2): kein
/// <c>Zasah</c>, kein fremdes Feld.</item>
/// </list>
///
/// <para><b>Was er also gesehen hat, sind ZWEI Dinge zusammen.</b> Erstens
/// sucht ein Versorger bei <b>Ladung 0</b> den nächsten Nachschubposten (Art
/// 14) unter allen 255 Gebäuden <b>ohne Besitzer- oder Bündnisfrage</b>
/// (@0x427AD5…0x427B49, F 0x426CC5…) — der Posten ist herrenlos (255) und steht
/// oft mitten in der Basis des Spielers. Der Heli fliegt also wirklich auf ihn
/// zu. Und zweitens: wer ihn dabei abschießt, bekommt den <b>Aufprall</b>.</para>
///
/// <para><b>Der Absturz, wie gelesen:</b></para>
/// <code>
///   jeder Abschuss (Flak-Station, Luft-gegen-Luft, Sprit 0)
///     -> axplode air   C 0x425EA0 (F 0x425080)
///     -> uk := 100     C 0x425F5B (F 0x42513B) — der EINZIGE Schreiber der 100
///     -> Fall mit -2 je Takt, bei alt &lt;= 1 auf 0 (C 0x42395F / 0x423967)
///     -> Aufprall: Wrack, vier Bodenmarken, dann der Trefferverteiler
///        C 0x40C9A0 mit Code 0x9C7C auf die eigene Zelle (@0x423B12) und
///        Code 0x9C72 auf die acht Nachbarn (@0x423B5E, F 0x422CD2)
///     -> Codes 40000..41000: Staerke = Code - 40000 (@0x40CCA9, F 0x40CAE1)
///        0x9C7C = 40060 -> <b>60</b>,  0x9C72 = 40050 -> <b>50</b>
/// </code>
/// <para>⚠ Die zwei <c>push</c> habe ich selbst nachgelesen, nicht übernommen:
/// <c>push 0x9c7c</c> @0x423B12 und <c>push 0x9c72</c> @0x423B5E. Und
/// <c>uk := 100</c> stand schon in meinem eigenen Feldscan über 0x6DDF80
/// (<c>mov byte ptr [edi + 0x6ddf80], 0x64</c> @0x425F5B).</para>
///
/// <para><b>Es trifft auch GEBÄUDE</b>, und genau das macht den Eindruck eines
/// gezielten Angriffs: der Spieler sieht einen Heli auf seine Basis zufliegen,
/// schießt ihn ab — und die Basis nimmt Schaden. Die Ursache ist der Abschuss,
/// nicht der Heli.</para>
///
/// <para><b>UNSERE SETZUNGEN, ausdrücklich:</b></para>
/// <list type="bullet">
/// <item>Das Wrack ist bei uns eine Explosionswolke; die <b>vier Bodenmarken</b>
/// des Originals fehlen — benannte Lücke, kein stiller Verzicht.</item>
/// <item>Der Trefferverteiler wird mit <c>Schütze = -1</c> gerufen. Damit
/// entfällt in <see cref="ZellEinschlag"/> die Bündnisfrage, und das ist hier
/// richtig: ein Absturz hat keinen Schützen und fragt niemanden.</item>
/// <item>Die Reihenfolge Zelle-dann-Nachbarn ist die des Originals; dass ein
/// Nachbar zweimal getroffen würde, kann nicht vorkommen, weil die neun Zellen
/// verschieden sind.</item>
/// </list>
///
/// <para>Gegenschalter <c>--absturz-aus</c> — der Stand von vor dem 20.09.2026:
/// ein abgeschossenes Flugzeug ist sofort tot, ohne Sturz und ohne Aufprall.
/// </para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--absturz-aus</c>, siehe Klassenkopf.</summary>
    public static bool AbsturzAus;

    /// <summary>Wie viel die eigene Zelle abbekommt — <c>0x9C7C = 40060</c>,
    /// Stärke <c>Code − 40000</c>.</summary>
    private const int AbsturzSchadenZelle = 60;

    /// <summary>Und jeder der acht Nachbarn — <c>0x9C72 = 40050</c>.</summary>
    private const int AbsturzSchadenNachbar = 50;

    /// <summary>Der Fall: <b>−2 je Takt</b>, bei <c>alt ≤ 1</c> auf 0
    /// (C 0x42395F / 0x423967).</summary>
    private const int AbsturzSinken = 2;

    // Zahlen für den Prüfstand. ⚠ Drei, nicht eine: ein Sturz, der beginnt und
    // nie aufschlägt, sieht sonst genauso aus wie einer, der gar nicht beginnt.
    public int AbstuerzeBegonnen, AbstuerzeAufgeschlagen, AbsturzTrefferZellen;

    /// <summary>Wie viele Flugzeuge unter <c>--absturz-aus</c> SOFORT tot waren.
    /// ⚠ Ohne diesen Zaehler meldete das Nullmodell »kein Flugzeug
    /// abgeschossen«, obwohl zwei gefallen waren — eine Gegenprobe, die die
    /// falsche Auskunft gibt, ist schlimmer als keine.</summary>
    public int AbstuerzeSofortTot;

    /// <summary>
    /// <b><c>axplode air</c> @0x425EA0</b> — ein Flugzeug ist abgeschossen.
    ///
    /// <para>Der EINE Weg, den jeder Abschuss nimmt. Er macht das Flugzeug
    /// nicht tot, sondern <b>stürzend</b>: <c>uk := 100</c>, und ab da fällt es.
    /// </para>
    ///
    /// <para>⚠ Alles, was bei uns ein Flugzeug erledigt, muss hierher rufen und
    /// nicht selbst <c>Dead</c> setzen — sonst gibt es wieder zwei Wege in
    /// denselben Zustand, und nur einer richtet den Aufprall an. Genau diese
    /// Sorte Fehler hat am 18.08. den Hangar stehen lassen.</para></summary>
    public void FlugzeugAbschiessen(Special a, string grund)
    {
        if (a.Dead || a.Absturz) return;
        if (AbsturzAus)
        {
            a.Hp = 0; a.Dead = true; a.Target = -1; a.Goal = null;
            _effects.Add(new Effect { Pos = a.Pos, Kind = "explosion", FrameTime = 0.05f });
            AbstuerzeSofortTot++;
            GD.Print($"absturz-aus: {a.Name} (Platz {a.Slot}, Spieler {a.Owner}) " +
                     $"sofort tot — {grund}");
            return;
        }
        a.Absturz = true;
        a.Target = -1;
        a.Goal = null;
        a.PlayerGoal = null;
        a.TurnPoint = null;
        AbstuerzeBegonnen++;
        GD.Print($"absturz: {a.Name} (Platz {a.Slot}, Spieler {a.Owner}) auf " +
                 $"({a.Col},{a.Row}) Hoehe {a.Alt} stuerzt — {grund}");
    }

    /// <summary>Der Fall, je Spieltakt — und der Aufprall, wenn der Boden da
    /// ist.</summary>
    private void AbsturzTakt()
    {
        for (int i = 0; i < _special.Count; i++)
        {
            var a = _special[i];
            if (!a.Absturz || a.Dead) continue;
            a.Alt -= AbsturzSinken;
            if (a.Alt > 1) continue;
            a.Alt = 0;
            Aufprall(a);
        }
    }

    /// <summary>
    /// Der AUFPRALL: Wrack, dann <b>60</b> auf die eigene Zelle und <b>50</b>
    /// auf jeden der acht Nachbarn — Einheiten UND Gebäude.
    /// </summary>
    private void Aufprall(Special a)
    {
        a.Hp = 0;
        a.Dead = true;
        AbstuerzeAufgeschlagen++;
        _effects.Add(new Effect { Pos = a.Pos, Kind = "explosion", FrameTime = 0.05f });

        int treffer = Wirken(a.Col, a.Row, AbsturzSchadenZelle);
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dc == 0 && dr == 0) continue;
                treffer += Wirken(a.Col + dc, a.Row + dr, AbsturzSchadenNachbar);
            }
        AbsturzTrefferZellen += treffer;
        GD.Print($"aufprall: {a.Name} (Platz {a.Slot}, Spieler {a.Owner}) auf " +
                 $"({a.Col},{a.Row}) — {AbsturzSchadenZelle} auf die Zelle, " +
                 $"{AbsturzSchadenNachbar} auf acht Nachbarn, {treffer} Zellen getroffen");

        // ⚠ Die vier Bodenmarken des Originals fehlen — siehe Klassenkopf.
    }

    /// <summary>Eine Zelle des Aufpralls. ⚠ <c>Schütze = -1</c>: ein Absturz
    /// fragt niemanden nach dem Bündnis (siehe Klassenkopf).</summary>
    private int Wirken(int col, int row, int schaden)
    {
        if (_nav == null || col < 0 || row < 0 ||
            col >= _nav.Width || row >= _nav.Height) return 0;
        ZellEinschlag(-1, col, row, schaden, 0);
        ZellWirkung(col, row, schaden);
        return 1;
    }

    /// <summary>
    /// <c>--absturz-check</c> — die Zeile zum Absturz.
    ///
    /// <para>⚠ Sie nennt zuerst, ob überhaupt ein Abschuss vorkam. Ohne das ist
    /// jede 0 darunter keine Messung, sondern ein Lauf ohne Luftkampf — genau
    /// die Falle, die mich beim Flak-Bodenschuss und beim Einschlagklang schon
    /// zweimal erwischt hat.</para></summary>
    public string AbsturzLine()
    {
        if (AbstuerzeBegonnen == 0 && AbstuerzeAufgeschlagen == 0 && AbstuerzeSofortTot == 0)
            return "absturz: kein Flugzeug abgeschossen — dieser Lauf sagt darueber NICHTS";
        return $"absturz: {AbstuerzeBegonnen} begonnen, {AbstuerzeAufgeschlagen} aufgeschlagen, " +
               $"{AbsturzTrefferZellen} Zellen getroffen " +
               $"(Soll: 9 je Aufprall, {AbsturzSchadenZelle}/{AbsturzSchadenNachbar})" +
               (AbsturzAus
                   ? $"   ⚠ Nullmodell --absturz-aus: {AbstuerzeSofortTot} sofort tot, " +
                     "begonnen und aufgeschlagen muessen 0 sein"
                   : "");
    }
}
