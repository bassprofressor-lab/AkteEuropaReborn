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
///
/// <para>⭐⭐ <b>07.10.2026, bug-473 — WANN DER BODEN DA IST</b>
/// (<c>berichte/absturz-aufprall-fable.md</c>, Fable, C und F gelesen).
/// Der Aufpralltest ist KEIN <c>alt ≤ 1</c>: <c>0x423A04</c> ruft über den Thunk
/// <c>0x40232E</c> die Bodenprüfung <c>0x425CB0</c> (F <c>0x402324 → 0x424E90</c>),
/// und die liest die ZELLE unter dem Flugzeug — Höhe (<c>0x41D0E0</c>), Belegung
/// (imap <c>0xBDEA80</c>) und Lagenbyte (<c>0x542E18</c>) — und vergleicht
/// <c>alt</c> gegen <b>Gelände·15 + Schwelle</b>:</para>
/// <code>
///   Lage 1..59                         alt &lt;= Boden + 50   (0x425D0B)  kommt in den Karten nicht vor
///   imap FFFC/FFFD/FFFE  Wasser/rau/frei alt &lt;= Boden + 2    (0x425D27)
///   imap &lt; 8000          Einheit         alt &lt;  Boden + 10   (0x425D48)
///   imap &lt; 14000         Infanterie      alt &lt;  Boden + 5    (0x425D62)
///   Lage 98 + imap 60000..60299 Gebäude  alt &lt;= Boden + 2    (0x425D7C)
///   Lage 98 ohne Gebäude                 NIE                 (0x425DA2)  bei uns NICHT nachgebaut (kein Lagenbyte)
///   sonst (gesperrt, Lage != 98)         alt &lt;  Boden + 50   (0x425DA7)
/// </code>
/// <para>Das <c>alt ≤ 1 → 0</c> bei <c>0x423959</c> ist nur die KLEMME des
/// Byte-Abzugs. Und der Fallende FLIEGT WEITER: <c>je 0x425047</c> führt in den
/// gemeinsamen Bewegungsblock (<c>sin/cos(dir)·sp</c>), Richtung und Stufe
/// des Abschussmoments bleiben bis zum Aufprall. Die Fahne sitzt
/// <c>alt − rand%20 − FeinY/2 − 50</c> (<c>0x4239AD</c>), also 50 px UNTER der
/// Flughöhe. Gegenschalter <c>--absturz-alt</c> (Boden fest bei 1, Fahne auf
/// Flughöhe) und <c>--absturz-drift-aus</c> (der Fallende steht).
/// Prüfstand <c>--absturz-check</c>, Simulation/AbsturzCheck.cs.</para>
///
/// <para>⭐⭐ <b>07.10.2026, bug-475 — DER AUFPRALL WIE GELESEN (E)</b>,
/// <c>0x423A14…0x423B78</c> (F <c>0x422BD4…0x422D38</c>), in dieser
/// Reihenfolge:</para>
/// <code>
///   typ := 0                                   Platz sofort leer
///   6 × 0x4A97C0(X, Y+1, 0, 0, rand%40, rand%40, rand&amp;0xF)   Wracktafel: Feinlage, Variante
///   0x4AF000(i) an (X, Y+1):  2 × ANIM 510+rand%9 (Fein rand%40, rand%20)
///                             5 × ANIM 310+rand%6 (rand%40, rand%60)
///                             5 × ANIM 230+(rand&amp;3) (rand%40, rand%60)   ⚠ 230..233, nicht 227..233:
///                                 @0x4AF0DE..0x4AF0EE ist |rand|&amp;3 mit dem Vorzeichen von rand, rand() ist nie negativ
///                             10+rand%10 × 0x4AD7B0(…, POHYB 1, KOLIK 8, ART 1)  = Brocken 29..38, Streuung 8
///                             20 ×          0x4AD7B0(…, 1, 6, 0)                 = Splitter 19..24, Streuung 6
///                                 (0x4AD807 reicht ART als 5. und KOLIK als 6. Argument an 0x4AD520 —
///                                  dieselbe Reihung wie der Fahrzeugtod 0x40B63A: push 0, push 3 = Sorte 0, Streuung 3)
///   4 × 0x4AE4C0(x, y, 0)  (X,Y) (X+1,Y) (X,Y+1) (X+1,Y+1)     Brandherde, Art 0 → Lebensdauer 50 (@0x4AE549),
///                                                              je Herd Explosion 510..518 + Splitter 200..204 (Schwanz 0x4AE625)
///   Besitzer == Betrachter → Licht (6, 140)
///   60 auf (X,Y), 50 auf acht Nachbarn
/// </code>
/// <para>Die Wrackteile und die Wrack-Explosion sitzen auf <b>(X, Y+1)</b>, die
/// Brandherde auf dem 2×2 ab (X, Y), der Schaden auf (X, Y) — so steht es da,
/// der Grund ist ungelesen (vermutlich der Zeilenversatz des Flugzeugbilds).
/// Die Brandherde laufen im Brandticker <c>0x4AE760</c>
/// (Simulation/Gebaeudebrand.cs) und erlöschen mit ihm. Gegenschalter
/// <c>--absturz-wrack-alt</c> (= die eine <c>"explosion"</c> von vor dem
/// 07.10.). <c>--absturz-check</c> zählt je Aufprall Wrackteile, Brandherde,
/// Explosionsbilder und Teilchen.</para>
///
/// <para>⭐⭐ <b>07.10.2026, bug-482 — KLANG UND WRACK-FEINLAGE GELESEN</b>
/// (<c>berichte/aufprall-klang-wrack-fable.md</c>, Fable, C und F):</para>
/// <list type="bullet">
/// <item><b>Abschuss:</b> <c>axplode air</c> ruft bedingungslos <b>Klang 70,
/// Modus 1 am Flugzeug</b> (20000+i → X/Y der Flugzeugtafel) @0x425F0F
/// (F 0x4250EF); bei Typ ≥ 13 (Nachschubheli) zusätzlich <b>138, Modus 0</b>
/// (ohne Ort, Sprachregler) @0x425F53 (F 0x425133). Der Fall ist still.
/// Gegenschalter <c>--abschussklang-aus</c>. ⚠ UNSERE Setzung: 138 läuft bei
/// uns über <c>GameSounds.Play</c> mit dem Effekteregler — wir haben nur
/// einen Regler (Settings.MixDb).</item>
/// <item><b>Aufprall:</b> kein eigener Klang im Zweig — aber jeder der vier
/// Zünder <c>0x4AE4C0</c> ruft im Schwanz @0x4AE664 (F 0x4ADF94) Klang 399
/// Modus 2 an der Zelle, und <b>399 ist ein Platzhalter, den die Klangroutine
/// @0x4048F8 (F 0x4048D8) in <c>400 + rand%6</c> umsetzt</b>. Vier Zünder ⇒
/// vier Explosionsklänge 400..405 im Aufpralltakt; danach der Ticker mit
/// 20 % Klang je Zelle und Takt (Alter 1..10). Gebaut in Gebaeudebrand.cs,
/// Gegenschalter <c>--brandklang-aus</c>.</item>
/// <item><b>Wrackteil-Feinlage:</b> der Listenbauer <c>0x42EE10</c> (F 0x42DFE0)
/// holt die Ellipse <c>0x431090</c> (POHYB 0/KOLIK 0 → Zellmitte (20,10) von
/// (X, Y+1)) und addiert <b>FeinX in Bildpunkten nach RECHTS</b> (1/40 Zelle,
/// Umbruch bei 40 @0x42EF03) und <b>FeinY in Bildpunkten nach OBEN</b> (1/20
/// Zelle, Umbruch bei 20 → Zeile −1 @0x42EF35; F 0x42E0C6/0x42E0FB). Die sechs
/// Teile liegen also einseitig rechts und oberhalb der Mitte von (X, Y+1):
/// 0..39 px rechts, 0..39 px hoch. <see cref="WrackFeinlageAuf"/>;
/// Gegenschalter <c>--wrackteil-lage-alt</c> (= die alte mittige Setzung
/// ±½ Zelle). ⚠ Offen bleibt der Sprite-Anker (Oberkante + Kopfbyte +1 des
/// Blitters 0x4AC1B0) — dieselbe Unbekannte wie beim Fahrzeugwrack (bug-426).
/// Die Bilder der Wrack-EXPLOSION (0x4AF000 → Effektleger 0x4357F0) bleiben
/// bei der alten Näherung, ihr Zeichner ist ungelesen.</item>
/// </list>
///
/// <para>⭐ <b>bug-476 — Fall 5b (Lage 98 ohne Gebäude → nie) wird NICHT
/// nachgebaut, und zwar begründet:</b> das Lagenbyte 98 tragen in den Karten
/// nur Gebäudezellen (216 Zellen auf 7 NET-Karten, alle genau 98 —
/// <c>[n-0a2e4b]</c>; Schreiber der 98 ist der Gebäudebau <c>0x43DE88</c>), und
/// der <b>Gebäudetod löscht es</b>: <c>0x4C9789 mov byte [edx+esi+0x542E18], 0</c>
/// (F <c>0x4C9339</c>, Tafel <c>0x541E78</c>, per Bytemuster in beiden Bauten
/// gefunden) in der Zellschleife von <c>0x4C95E0</c>, direkt danach wird die
/// Belegung <c>0xBDEA80</c> neu gesetzt (@0x4C97E7). Eine Zelle mit Lage 98
/// und ohne Gebäude in der Belegung gibt es damit nur, solange ein Gebäude
/// steht und auf seiner Zelle etwas anderes in der Belegung liegt — und das
/// fängt vorher Fall 3/4 (Einheit/Infanterie, imap &lt; 14000). Das »nie«-Loch
/// ist im Spiel nicht erreichbar; bei uns ist ein Gebäude über den Fußabdruck
/// Fall 5a. Kein Prüfstandsfall, weil keine Karte so eine Zelle hat.
/// Außerhalb der Karte (X/Y laufen im Original schrankenlos) wird bei uns
/// geklemmt.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--absturz-aus</c>, siehe Klassenkopf.</summary>
    public static bool AbsturzAus;

    /// <summary><c>--absturz-alt</c> — Stand bis 0.6.6: Aufprall bei
    /// <c>Alt ≤ 1</c> ohne Blick auf Gelände und Zelle, Fahne auf Flughöhe.
    /// Nullmodell zu bug-473 (A und C).</summary>
    public static bool AbsturzAlt;

    /// <summary><c>--absturz-drift-aus</c> — Stand bis 0.6.6: der Fallende
    /// steht in der Luft still. Nullmodell zu bug-473 (B).</summary>
    public static bool AbsturzDriftAus;

    /// <summary><c>--absturz-wrack-alt</c> — Stand bis 0.6.6: der Aufprall ist
    /// EINE <c>"explosion"</c>, ohne Wrackteile, Wrack-Explosion und Brandherde.
    /// Nullmodell zu bug-475 (E).</summary>
    public static bool AbsturzWrackAlt;

    /// <summary><c>--abschussklang-aus</c> — Stand bis 0.6.7: der Abschuss ist
    /// stumm (70/138 waren definiert, aber nie gerufen). Nullmodell zu bug-482 (A).</summary>
    public static bool AbschussklangAus;

    /// <summary><c>--wrackteil-lage-alt</c> — Stand bis 0.6.7: die sechs
    /// Wrackteile mittig um (X, Y+1), ±½ Zelle, Y-Spanne halbiert, nach unten.
    /// Nullmodell zu bug-482 (D).</summary>
    public static bool WrackteilLageAlt;

    /// <summary>Wie oft der Abschuss geklungen hat (70) und wie oft die
    /// Sprachzeile 138 dazukam — für den Prüfstand.</summary>
    public int AbschussKlaenge, AbschussKlaenge138;

    /// <summary>bug-475: die Zahlen des Aufpralls für den Prüfstand — über alle
    /// Aufpralle summiert. Soll je Aufprall: 6 / 4 / 12 / 30..39.</summary>
    public int AbsturzWrackteile, AbsturzBrandherde, AbsturzWrackBilder, AbsturzWrackTeilchen;

    /// <summary>Was der letzte Aufprall gelegt hat (für die Zeile und den Prüfstand).</summary>
    private (int Wrack, int Brand, int Bilder, int Teilchen) _aufprallZuletzt;

    /// <summary>Der Boden in <c>alt</c>-Einheiten: <b>15 je Höhenstufe</b>
    /// (<c>0x425D01</c>, dieselbe 15 wie beim Nebel <c>0x4B4791</c> und beim
    /// Sollhöhen-Setzer).</summary>
    public const int AltJeHoehenstufe = 15;

    /// <summary>Die Schwellen der Bodenprüfung <c>0x425CB0</c>.</summary>
    public const int SchwelleFrei = 2, SchwelleInfanterie = 5, SchwelleEinheit = 10, SchwelleGesperrt = 50;

    /// <summary>Was der Fall bisher getan hat — für die Aufprallzeile und den
    /// Prüfstand. Angelegt in <see cref="FlugzeugAbschiessen"/>, fortgeschrieben
    /// in <see cref="AbsturzTakt"/>.</summary>
    private sealed class AbsturzBeginn
    {
        public int Tick, Alt, Col, Row, Takte;
        public Vector2 Pos;
    }
    private readonly System.Collections.Generic.Dictionary<Special, AbsturzBeginn> _absturzBeginn = new();

    /// <summary>
    /// <b>Die Schwelle der Zelle (c, r)</b> — die Weichen von <c>0x425D0B…0x425DB9</c>.
    /// </summary>
    /// <param name="kleinerGleich">true = Vergleich <c>alt ≤ Boden + Schwelle</c>
    /// (Fall 2 und 5a), false = <c>alt &lt; Boden + Schwelle</c> (Fall 3/4/5c).</param>
    /// <param name="zellart">für Zeilen und Prüfstand.</param>
    public int AbsturzSchwelle(int c, int r, out bool kleinerGleich, out string zellart)
    {
        kleinerGleich = true;
        if (_nav == null || !_nav.InBounds(c, r)) { zellart = "ausserhalb"; return SchwelleFrei; }

        // Gebäude über den FUSSABDRUCK (Fall 5a: Lage 98 + imap 60000..60299).
        // ⚠ Die Belegungstafel trägt nur den Anker eines Gebäudes, darum der
        // Grundriss — ein Gebäude findet man über den Fußabdruck, nicht den Anker.
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead) continue;
            if (c >= e.Col && c < e.Col + Mathf.Max(1, e.FootW) &&
                r >= e.Row && r < e.Row + Mathf.Max(1, e.FootH))
            { zellart = $"Gebaeude {e.Slot}"; return SchwelleFrei; }
        }

        int occ = _nav.OccupantAt(c, r);
        var u = occ >= 0 && occ < _entities.Count ? _entities[occ] : null;
        if (u != null && !u.Dead && !u.IsBuilding && !u.IsProp)
        {
            kleinerGleich = false;
            if (u.Infantry >= 0) { zellart = $"Fussvolk {u.Slot}"; return SchwelleInfanterie; }   // Fall 4
            zellart = $"Einheit {u.Slot}"; return SchwelleEinheit;                                 // Fall 3
        }
        // Fall 5c: fest und kein Gebäude (Gegenstand, Fels — imap >= 14000 bzw. 0xFFFF).
        if ((u != null && u.IsProp) || _nav.GroundAt(c, r) == Simulation.NavGrid.Ground.Blocked)
        { kleinerGleich = false; zellart = "gesperrt"; return SchwelleGesperrt; }
        // Fall 2: Wasser / rau / frei.
        zellart = _nav.GroundAt(c, r) switch
        {
            Simulation.NavGrid.Ground.Water => "Wasser",
            Simulation.NavGrid.Ground.Rough => "rau",
            _ => "frei",
        };
        return SchwelleFrei;
    }

    /// <summary><b>»Boden erreicht?«</b> — <c>0x425CB0</c> (F <c>0x424E90</c>),
    /// siehe Klassenkopf. <c>a.Alt</c> ist bei uns absolut (Gelände·15 + Höhe),
    /// dieselbe Einheit wie <c>alt</c> im Original. Unter <c>--absturz-alt</c>
    /// die alte Regel <c>Alt ≤ 1</c>.</summary>
    public bool BodenErreicht(Special a)
    {
        if (AbsturzAlt) return a.Alt <= 1;
        int boden = ElevOf(a.Col, a.Row) * AltJeHoehenstufe;
        int s = AbsturzSchwelle(a.Col, a.Row, out bool kleinerGleich, out _);
        return kleinerGleich ? a.Alt <= boden + s : a.Alt < boden + s;
    }

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
            AbschussKlang(a);                        // derselbe Abschuss, nur ohne Fall
            GD.Print($"absturz-aus: {a.Name} (Platz {a.Slot}, Spieler {a.Owner}) " +
                     $"sofort tot — {grund}");
            return;
        }
        a.Absturz = true;
        AbschussKlang(a);
        a.Target = -1;
        a.Goal = null;
        a.PlayerGoal = null;
        a.TurnPoint = null;
        AbstuerzeBegonnen++;
        _absturzBeginn[a] = new AbsturzBeginn { Tick = DebugTicks, Alt = a.Alt, Col = a.Col, Row = a.Row, Pos = a.Pos };
        GD.Print($"absturz: {a.Name} (Platz {a.Slot}, Spieler {a.Owner}) auf " +
                 $"({a.Col},{a.Row}) Hoehe {a.Alt} (im Bild {AirHoehePx(a):0} px ueber dem Schatten) stuerzt — {grund}");
    }

    /// <summary>bug-482 (A): <b>Klang 70, Modus 1 am Flugzeug</b> @0x425F0F
    /// (F 0x4250EF), und bei Typ ≥ 13 (<c>cmp byte [+0x08], 13</c> @0x425F17)
    /// zusätzlich <b>138, Modus 0</b> @0x425F53 (F 0x425133). Beide VOR
    /// <c>uk := 100</c>, also im Abschusstakt, nicht beim Aufprall.</summary>
    private void AbschussKlang(Special a)
    {
        if (AbschussklangAus) return;
        Audio.GameSounds.PlayAt(Audio.GameSounds.AircraftExplodes, a.Col, a.Row);
        AbschussKlaenge++;
        if (a.Kind >= AbschussSprachzeileAbTyp)
        {
            Audio.GameSounds.Play(Audio.GameSounds.AircraftExplodes2);
            AbschussKlaenge138++;
        }
    }

    /// <summary>Ab welchem Typ (+0x08) die Sprachzeile 138 kommt — die 13 von
    /// @0x425F17 (<c>cmp byte [edi+0x6DDF78], 0xD</c>): Nachschubhelis 13/14.</summary>
    public const int AbschussSprachzeileAbTyp = 13;

    /// <summary>Der Fall, je Spieltakt — und der Aufprall, wenn der Boden da
    /// ist.</summary>
    /// <summary><c>--absturz-rauch-aus</c> — Stand bis 0.6.6: ein Absturz ohne
    /// Feuer- und Rauchfahne. Nullmodell zu bug-464.</summary>
    public static bool AbsturzRauchAus;
    public int AbsturzFahnen;

    private void AbsturzTakt()
    {
        for (int i = 0; i < _special.Count; i++)
        {
            var a = _special[i];
            if (!a.Absturz || a.Dead) continue;
            if (_absturzBeginn.TryGetValue(a, out var b)) b.Takte++;
            // ⭐ bug-473: die KLEMME @0x423959 — alt > 1 ? alt -= 2 : alt := 0.
            // Sie ist NICHT der Aufpralltest, der kommt unten (0x423A04).
            a.Alt = a.Alt > 1 ? a.Alt - AbsturzSinken : 0;
            // ⭐ 06.10.2026, bug-464 (KayelGee R4, Meldung 12): »Nachschubheli
            // abgeschossen, aber nicht abgestuerzt«. Im Original zieht der
            // Fallende eine Fahne: je Takt rand&3 — 1 → Feuerstoss 210+rand%3,
            // 2 → Rauch 240+rand%3, 0/3 nichts (@0x42396E), in der Hoehe
            // alt − rand%20 − FeinY/2 − 50 (@0x4239AD..0x4239DA; bug-473: der
            // Abzug 50 + FeinY/2 ist jetzt gelesen, FeinY = word[+0x06] 0..39).
            // ⚠ Mindestwert 0 ist UNSERE Setzung (das Original zieht auch unter
            // den Bodenpunkt). --absturz-alt laesst die Fahne auf Flughoehe.
            if (!AbsturzRauchAus)
            {
                int w = Simulation.Determinism.Roll(4);
                if (w is 1 or 2)
                {
                    int feinY = AbsturzAlt ? 0 : (int)Mathf.PosMod(a.Pos.Y, TileH) * 40 / TileH;
                    float h = Mathf.Max(0f, AirHoehePx(a) - Simulation.Determinism.Roll(20)
                                            - (AbsturzAlt ? 0 : AbsturzFahneAbzug + feinY / 2));
                    _effects.Add(new Effect
                    {
                        Pos = a.Pos - new Vector2(0, h),
                        Kind = (w == 1 ? "feuerstoss" : "rauch") + Simulation.Determinism.Roll(3),
                        FrameTime = 0.06f,
                    });
                    AbsturzFahnen++;
                }
            }
            // ⭐ bug-473 (A): »Boden erreicht?« 0x423A04 -> 0x425CB0, nach Zelle.
            if (!BodenErreicht(a)) continue;
            if (AbsturzAlt) a.Alt = 0;
            Aufprall(a);
        }
    }

    /// <summary>Die Fahne sitzt <b>50</b> unter der Flughöhe (<c>sub eax, 50</c>
    /// in <c>0x4239C8…0x4239DA</c>).</summary>
    private const int AbsturzFahneAbzug = 50;

    /// <summary>
    /// <b>Der Fallende fliegt weiter</b> (bug-473 B) — <c>je 0x425047</c> aus
    /// dem Absturzzweig führt in den gemeinsamen Bewegungsblock: FeinX/FeinY
    /// += cos/sin(dir)·sp, Zellwechsel beim Überlauf (<c>0x425085…0x42512C</c>).
    /// Richtung und Stufe fasst der Absturzzweig nicht an; die Stufenregelung
    /// <c>0x424E01</c> liegt VOR dem Sprungziel und wird übersprungen.
    ///
    /// <para>⚠ Wie <see cref="AirDrift"/>, aber OHNE Spritprüfung (der Block
    /// prüft keinen Sprit — ein mit Sprit 0 abgeschossenes Flugzeug fällt
    /// trotzdem schräg) und OHNE Heimkehr am Rand: nur Klemme auf die Karte,
    /// benannte Abweichung wie bei der Handsteuerung (das Original zählt
    /// X/Y schrankenlos weiter, <c>0x4250A4</c>). Der Spritabzug je Zellgrenze
    /// (<c>fuel−−</c>) bleibt, er ist wirkungslos.</para></summary>
    private void AbsturzDrift(Special a, float dt)
    {
        if (AbsturzDriftAus) return;
        float rad = Mathf.DegToRad(a.Dir);
        var step = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad))
                 * FlugStufe(a) * AirPxPerSpeed * dt;   // dieselbe Stufe wie AirDrift
        if (step.LengthSquared() <= 0.000001f) return;
        a.Pos += step;
        if (_nav != null)
        {
            float rand = TileW * 0.5f;
            a.Pos = new Vector2(Mathf.Clamp(a.Pos.X, _ox + rand, _ox + _nav.Width * TileW - rand),
                                Mathf.Clamp(a.Pos.Y, _oy + rand, _oy + _nav.Height * TileH - rand));
        }
        a.Facing = AirDirToFacing(step);
        int c = Mathf.Clamp((int)(a.Pos.X / TileW), 0, 255), r = Mathf.Clamp((int)(a.Pos.Y / TileH), 0, 255);
        if (c != a.Col || r != a.Row) { a.Col = c; a.Row = r; if (a.Fuel > 0) a.Fuel--; }
        AbsturzDriftSchritte++;
    }

    /// <summary>Wie viele Schritte Fallende gedriftet sind — unter
    /// <c>--absturz-drift-aus</c> MUSS das 0 sein.</summary>
    public int AbsturzDriftSchritte;


    /// <summary>
    /// Der AUFPRALL: Wrack, dann <b>60</b> auf die eigene Zelle und <b>50</b>
    /// auf jeden der acht Nachbarn — Einheiten UND Gebäude.
    /// </summary>
    private void Aufprall(Special a)
    {
        // bug-473: Zellart und Schwelle VOR dem Schaden lesen — Wirken kann
        // die Einheit darunter toeten, und dann stimmt die Zeile nicht mehr.
        int schwelle = AbsturzSchwelle(a.Col, a.Row, out bool kleinerGleich, out string zellart);
        int elev = ElevOf(a.Col, a.Row);
        _absturzBeginn.TryGetValue(a, out var beginn);
        string wie = beginn == null ? "" :
            $" nach {beginn.Takte} Takten aus Hoehe {beginn.Alt} ueber ({beginn.Col},{beginn.Row}), " +
            $"Weg {a.Pos.DistanceTo(beginn.Pos):0} px";
        GD.Print($"absturz-boden: {a.Name} (Platz {a.Slot}) Zelle ({a.Col},{a.Row}) Hoehe {elev} " +
                 $"[{zellart}] Schwelle {(kleinerGleich ? "<=" : "<")} {elev * AltJeHoehenstufe}+{schwelle}, " +
                 $"Alt {a.Alt}{wie}" + (AbsturzAlt ? "   [--absturz-alt]" : ""));
        _absturzBeginn.Remove(a);

        a.Hp = 0;
        a.Dead = true;                                   // typ := 0 @0x423A25
        AbstuerzeAufgeschlagen++;
        // ⭐ 07.10.2026 (bug-475) — Wrackteile, Wrack-Explosion, Brandherde,
        // in der Reihenfolge von 0x423A2F..0x423AC9 (siehe Klassenkopf).
        if (AbsturzWrackAlt)
        {
            _effects.Add(new Effect { Pos = a.Pos, Kind = "explosion", FrameTime = 0.05f });
            _aufprallZuletzt = (0, 0, 0, 0);
        }
        else
        {
            int wrack = AbsturzWrackteileLegen(a.Col, a.Row + 1);
            var (bilder, teilchen) = AbsturzWrackExplosion(a.Col, a.Row + 1);
            int brand = AbsturzBrandherde2x2(a.Col, a.Row);
            _aufprallZuletzt = (wrack, brand, bilder, teilchen);
            AbsturzWrackteile += wrack; AbsturzBrandherde += brand;
            AbsturzWrackBilder += bilder; AbsturzWrackTeilchen += teilchen;
        }
        AbsturzCheckNotieren(a, beginn, elev, schwelle, kleinerGleich, zellart);
        // ⭐ 04.10.2026 (bug-434) — Lichtquelle (X, Y, 6, 140), nur wenn der
        // Besitzer (+0x09) der Betrachter ist (@0x423AD1..0x423AE8).
        // Simulation/Todeslicht.cs.
        if (a.Owner == ViewPlayer) LichtAnlegen(a.Col, a.Row, 6, 140, "Absturz " + a.Name);

        int treffer = Wirken(a.Col, a.Row, AbsturzSchadenZelle);
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dc == 0 && dr == 0) continue;
                treffer += Wirken(a.Col + dc, a.Row + dr, AbsturzSchadenNachbar);
            }
        AbsturzTrefferZellen += treffer;
        AbsturzCheckNachSchaden(a);                      // bug-482: Fremdzündungen durch den Schaden
        GD.Print($"aufprall: {a.Name} (Platz {a.Slot}, Spieler {a.Owner}) auf " +
                 $"({a.Col},{a.Row}) — {AbsturzSchadenZelle} auf die Zelle, " +
                 $"{AbsturzSchadenNachbar} auf acht Nachbarn, {treffer} Zellen getroffen; " +
                 $"Wrackteile {_aufprallZuletzt.Wrack}, Brandherde {_aufprallZuletzt.Brand}, " +
                 $"Explosionsbilder {_aufprallZuletzt.Bilder}, Teilchen {_aufprallZuletzt.Teilchen}" +
                 (AbsturzWrackAlt ? " [--absturz-wrack-alt]" : "") +
                 $"; Fahnen bisher {AbsturzFahnen}, im Bild jetzt {AirHoehePx(a):0} px");
    }

    /// <summary>Eine Feinlage <c>0..39</c> je Zelle (Y darf bis 59 laufen) auf
    /// eine Bildstelle — an die Zellmitte MIT Höhe gelegt, mittig. ⚠ UNSERE
    /// Näherung für die Bilder der Wrack-Explosion (Effektleger 0x4357F0,
    /// Zeichner ungelesen) — und das Nullmodell <c>--wrackteil-lage-alt</c>
    /// für die Wrackteile.</summary>
    private Vector2 FeinlageAuf(int c, int r, int feinX, int feinY)
        => CellCenter(c, r) + new Vector2((feinX - 20) * TileW / 40f, (feinY - 20) * TileH / 40f);

    /// <summary>bug-482 (D): <b>die Feinlage eines WRACKTEILS, wie gelesen</b>
    /// (Listenbauer 0x42EE10, F 0x42DFE0): Bezug ist die Zellmitte von (X, Y+1)
    /// (Ellipse 0x431090 bei POHYB 0/KOLIK 0 → (20, 10)), FeinX in Bildpunkten
    /// nach rechts (1/40 Zelle, @0x42EEF6), FeinY in Bildpunkten nach OBEN
    /// (1/20 Zelle, @0x42EF2B; bei ≥ 20 wandert der Eintrag in die Zeile
    /// darüber, @0x42EF35). Nicht zentriert: 0..39 rechts, 0..39 hoch.</summary>
    private Vector2 WrackFeinlageAuf(int c, int r, int feinX, int feinY)
        => WrackteilLageAlt
            ? FeinlageAuf(c, r, feinX, feinY)
            : CellCenter(c, r) + new Vector2(feinX * TileW / 40f, -feinY * TileH / 20f);

    /// <summary>Die Versätze der zuletzt gelegten Wrackteile gegen die Zellmitte
    /// von (X, Y+1) — für den Prüfstand (Soll dx ∈ [0, TileW), dy ∈ (−2·TileH, 0]).</summary>
    private readonly System.Collections.Generic.List<Vector2> _wrackteilVersatz = new();

    /// <summary><b>6 × <c>0x4A97C0</c>(X, Y+1, 0, 0, rand%40, rand%40, rand&amp;0xF)</b>
    /// (<c>0x423A2F…0x423A77</c>, <c>bl = 6; dec bl</c>). Die Würfe in der
    /// Reihenfolge des Originals: Feinlage X, Feinlage Y, Variante.</summary>
    private int AbsturzWrackteileLegen(int c, int r)
    {
        int n = 0;
        _wrackteilVersatz.Clear();
        var mitte = CellCenter(c, r);
        for (int k = 0; k < 6; k++)
        {
            int fx = Simulation.Determinism.Roll(40), fy = Simulation.Determinism.Roll(40);
            int variante = Simulation.Determinism.Roll(16);
            var pos = WrackFeinlageAuf(c, r, fx, fy);
            _wrackteilVersatz.Add(pos - mitte);
            WrackTeilAnlegen(pos, variante, c * 3 + r + k);
            n++;
        }
        return n;
    }

    /// <summary><b>Die Wrack-Explosion <c>0x4AF000</c></b> an (X, Y+1): 2 × 510..518,
    /// 5 × 310..315, 5 × 230..233 über <c>0x4357F0</c> (Effekt auf Feinlage), dann
    /// 10+rand%10 Brocken (Sorte 1, Streuung 8) und 20 Splitter (Sorte 0,
    /// Streuung 6) über <c>0x4AD7B0 → 0x4AD520</c>. Rückgabe (Bilder, Teilchen).</summary>
    private (int Bilder, int Teilchen) AbsturzWrackExplosion(int c, int r)
    {
        int bilder = 0;
        void Bild(string kind, int feinYMod, float frame)
        {
            int fx = Simulation.Determinism.Roll(40), fy = Simulation.Determinism.Roll(feinYMod);
            _effects.Add(new Effect { Pos = FeinlageAuf(c, r, fx, fy), Kind = kind, FrameTime = frame });
            bilder++;
        }
        for (int k = 0; k < 2; k++) Bild("sprengung" + Simulation.Determinism.Roll(9), 20, 0.04f);   // 0x4AF02B..0x4AF06E
        for (int k = 0; k < 5; k++) Bild("flamme" + Simulation.Determinism.Roll(6), 60, 0.05f);      // 0x4AF074..0x4AF0B7
        for (int k = 0; k < 5; k++) Bild("glut" + Simulation.Determinism.Roll(4), 60, 0.05f);        // 0x4AF0BD..0x4AF103
        var quelle = new Entity { Col = c, Row = r };
        int teilchen = 0;
        int brocken = 10 + Simulation.Determinism.Roll(10);                                         // 0x4AF105..0x4AF134
        for (int k = 0; k < brocken; k++) { EinTeil(quelle, sorte: 1, streuung: 8); teilchen++; }
        for (int k = 0; k < 20; k++) { EinTeil(quelle, sorte: 0, streuung: 6); teilchen++; }         // 0x4AF136..0x4AF154
        return (bilder, teilchen);
    }

    /// <summary><b>4 × <c>0x4AE4C0</c>(x, y, 0)</b> auf (X,Y), (X+1,Y), (X,Y+1),
    /// (X+1,Y+1) (<c>0x423A98…0x423AC9</c>) — Art 0, Lebensdauer 50. Zellen
    /// außerhalb der Karte werden übergangen (UNSERE Klemme).</summary>
    private int AbsturzBrandherde2x2(int c, int r)
    {
        if (GebaeudebrandAus) return 0;      // ohne Brandticker kein Herd (sonst bliebe er ewig)
        int n = 0;
        foreach (var (dc, dr) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            int x = c + dc, y = r + dr;
            if (_nav != null && !_nav.InBounds(x, y)) continue;
            if (BrandZuenden(x, y, BrandLebenArt0)) n++;
        }
        return n;
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
               $", {AbsturzDriftSchritte} Driftschritte im Fall" +
               $", Wrackteile {AbsturzWrackteile} / Brandherde {AbsturzBrandherde} / Explosionsbilder {AbsturzWrackBilder} / " +
               $"Teilchen {AbsturzWrackTeilchen} (Soll je Aufprall 6 / 4 / 12 / 30..39)" +
               (AbsturzWrackAlt ? "   [--absturz-wrack-alt: alles 0]" : "") +
               $", Abschussklang 70 {AbschussKlaenge}x / 138 {AbschussKlaenge138}x" +
               (AbschussklangAus ? " [--abschussklang-aus: MUESSEN 0 sein]" : "") +
               (WrackteilLageAlt ? "   [--wrackteil-lage-alt: Wrackteile mittig]" : "") +
               (AbsturzAus
                   ? $"   ⚠ Nullmodell --absturz-aus: {AbstuerzeSofortTot} sofort tot, " +
                     "begonnen und aufgeschlagen muessen 0 sein"
                   : "") +
               (AbsturzAlt ? "   [--absturz-alt: Boden fest bei Alt <= 1]" : "") +
               (AbsturzDriftAus ? "   [--absturz-drift-aus: Driftschritte MUESSEN 0 sein]" : "");
    }
}
