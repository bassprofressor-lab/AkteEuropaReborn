namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐⭐ <b>WAS EIN ANGREIFENDES FLUGZEUG WIRKLICH SCHIESST</b> (20.09.2026).
///
/// <para><b>Anlass:</b> gemeldet zu Kampagne 17 — »ja die helikopter greifen
/// mich an, sehe aber keine schüsse von denen.« Die Wirkung war da (vier Tote
/// durch <c>LUFTANGRIFF</c> in seinem Lauf), das BILD fehlte: unser Angriff
/// setzte nur eine Explosion auf das Ziel und zog Lebenspunkte ab.</para>
///
/// <para>Lesung <c>berichte/luftschuss-fable.md</c> (Fable), Kern »Air shoot«
/// <c>0x427090</c> (F <c>0x426280</c>).</para>
///
/// <para><b>DIE DREI ARTEN SCHIESSEN DREI VERSCHIEDENE DINGE</b>, und das ist
/// der eigentliche Befund:</para>
/// <list type="bullet">
/// <item><b>Jagdflieger</b> (typ 1): <b>ZWEI</b> Geschosse der <b>Art 49</b> —
/// Tempo 35, <b>kein Flugbild</b>, Einschlag 83 (MG-Treffer), Klang
/// <c>8 + rand&amp;1</c>. Anleger @0x42731E/0x4273BE.</item>
/// <item><b>Bomber</b> (typ 2): <b>EIN</b> Geschoss der <b>Art 47</b> —
/// Tempo 3, Flugbild 220 (die Bombe in acht Richtungen), Einschlag 309 (in
/// ANIM.CWA leer), Klang 114 fest. Anleger @0x42752B.</item>
/// <item><b>Kampfhubschrauber</b> (typ 10): <b>GAR KEIN GESCHOSS</b> — zwei
/// STRAHLSÄTZE in der Tafel <c>0x87B448</c> (44 B × 200, Anleger
/// <c>0x455320</c> / F <c>0x453FC0</c>) aus zwei Mündungen. Siehe
/// <see cref="StrahlTakt"/>.</item>
/// </list>
///
/// <para>⭐ Die Waffenart steht fest in <c>spawn_aircraft</c>: Vorlage
/// <b>0 → 49, 1 → 47, 4 → 48</b>. Das deckt sich mit meiner EIGENEN Lesung vom
/// 19.09. (»entwurf 0/1/4 → +0x2C = 0x31/0x2F/0x30«) — zwei unabhängige Wege
/// auf dieselben drei Zahlen.</para>
///
/// <para><b>Die Bahn</b> ist für beide Geschossarten dieselbe: Start = Zelle des
/// Flugzeugs, Höhe = <c>alt</c>; Ziel = Lage + Richtung·(2·alt − 15·Gelände)
/// ± 20, und die <b>Zielhöhe ist 0</b>. Es ist also eine GERADE Bahn, die zum
/// Boden sinkt — kein Bogen und keine Zielverfolgung.</para>
///
/// <para>⚠ <b>KEIN Mündungsfeuer</b>, bei keiner der drei Arten. Und das
/// <b>Nachladen zählt TAKTE</b> (3/6/10 je Art, Satzfeld +0x2A), nicht Sekunden
/// wie unsere 1,4 s.</para>
///
/// <para>⚠⚠ <b>Was hier NICHT gebaut ist:</b> der SCHÜTZE. Im Original trägt
/// das Geschoss <c>20000 + Platz</c> in +0x0C und der Strahl dasselbe in +0x26;
/// <c>Zasah</c> @0x40CBC1 nimmt für 20000..20200 den Angriff aus +0x22 und den
/// Besitzer aus +0x09, und daran hängt die Zuschreibung des Abschusses. Unser
/// <see cref="ApplyHit"/> erwartet einen Eintrag der ENTITÄTENliste, und ein
/// Flugzeug steht dort nicht — darum steht in den Todeszeilen weiter »ohne
/// Schuetzen«. Der Schaden ist richtig, die Zuschreibung fehlt. Eine eigene
/// Aufgabe, benannt und nicht verschwiegen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--luftschuss-alt</c> — der Stand von vor dem 20.09.2026: der
    /// Angriff setzt eine Explosion auf das Ziel und zieht sofort ab, ohne
    /// Geschoss und ohne Strahl.</summary>
    public static bool LuftschussAlt;

    /// <summary><c>--heli-strahl-aus</c> — der Kampfhubschrauber trifft wie
    /// bisher sofort, ohne Strahl. Nullmodell zu <see cref="StrahlTakt"/>.</summary>
    public static bool HeliStrahlAus;

    /// <summary><c>--luftnachladen-alt</c> — Nachladen in Sekunden (1,4) statt
    /// in Takten (3/6/10).</summary>
    public static bool LuftnachladenAlt;

    /// <summary>Die Geschossarten der Luft, aus <c>spawn_aircraft</c>:
    /// Jagdflieger 49, Bomber 47, Kampfhubschrauber 48 (der aber keines
    /// anlegt).</summary>
    private const int ArtJaeger = 49, ArtBombe = 47;

    /// <summary>Nachladetakte je Flugzeugart — Satzfeld +0x2A, gesetzt von
    /// <c>spawn_aircraft</c>: <b>3</b> (Jagdflieger), <b>6</b> (Bomber),
    /// <b>10</b> (Kampfhubschrauber).</summary>
    private static int NachladeTakte(int kind) => kind switch
    {
        1 => 3,
        2 => 6,
        _ => 10,
    };

    /// <summary>Wie viele Geschosse eine Salve hat: der Jagdflieger zwei, der
    /// Bomber eines. ⚠ Verbraucht wird trotzdem nur EINE Munition je Salve.</summary>
    private static int SalvenZahl(int kind) => kind == 1 ? 2 : 1;

    // Zahlen für den Prüfstand. ⚠ Nach ART getrennt: »es wurde geschossen«
    // sagt nichts darüber, ob die richtige Art das Richtige tat.
    public int LuftGeschosse, LuftStrahlen, LuftSalven;

    /// <summary>
    /// <b>Eine Salve eines Flugzeugs</b> — der Verteiler nach Art.
    ///
    /// <para>Gibt zurück, ob geschossen wurde. Der Aufrufer zieht danach die
    /// Munition ab und setzt seinen Wendepunkt.</para></summary>
    /// <param name="ziel">das getroffene Ding — <b>darf null sein</b>. Die
    /// Handsteuerung schiesst geradeaus, und dann gibt es keines: das Geschoss
    /// fliegt auf den Punkt. Nur die zwei Zweige, die einen Griff brauchen
    /// (das Nullmodell und der Helistrahl), verlangen eines.</param>
    private bool LuftSalve(Special a, Entity? ziel, Vector2 aim) => LuftSalve(a, ziel, aim, false);

    private bool LuftSalve(Special a, Entity? ziel, Vector2 aim, bool abwurf)
    {
        LuftSalven++;
        if (LuftschussAlt && ziel != null)
        {
            _effects.Add(new Effect { Pos = aim - new Vector2(0, 8),
                                      Kind = "explosion", FrameTime = 0.05f });
            ApplyHit(-1, a.Target, ziel, a.Attack,
                     $"LUFTANGRIFF {a.Name} (Art {a.Kind}, Platz {a.Slot}, auf ({a.Col},{a.Row}))");
            return true;
        }

        // Der Kampfhubschrauber: ein STRAHL, kein Geschoss.
        if (a.Kind is >= 10 and <= 12 && !HeliStrahlAus && ziel != null)
        {
            StrahlAnlegen(a, ziel, aim);
            return true;
        }

        // ⭐ 19.09.2026 — DER BOMBER WIRFT, WAS IM SATZ STEHT (+0x2C), nicht
        // mehr fest die 47: 45 Gasbombe, 46 Loeschmittel, 47 Bombe. Das
        // Einschlagbild (schlag_120 / schlag_121 / leer) und der Einschlagklang
        // (16 / 16 / 400 + rand%6) kommen dann von selbst aus der
        // Geschosstafel, weil beide je ART daraus gelesen werden — hier ist
        // nichts nachzuziehen. Simulation/Bombensorten.cs, --bombe-fest-47.
        int art = a.Kind != 2 ? ArtJaeger
                : (BombeFest47 || a.Waffenart == 0 ? ArtBombe : a.Waffenart);
        string? flug = FlightKind(art);          // 47 -> flug_220, 49 -> keines
        int tempo = Audio.GameSounds.ProjectileSpeed(art);
        int n = SalvenZahl(a.Kind);
        var dir = (aim - a.Pos).Normalized();
        if (dir.LengthSquared() < 0.0001f) dir = Vector2.Down;

        if (abwurf)
        {
            BombeAnlegen(a, art, flug, tempo, aim);
            Audio.GameSounds.PlayAt(114, a.Col, a.Row);                 // 0x42740C, fest 0x72
            return true;
        }
        for (int k = 0; k < n; k++)
        {
            // ⚠ Die zwei Geschosse des Jaegers kommen aus zwei Muendungen; die
            // ±20 der Lesung sind der Seitenversatz des Ziels, nicht des Starts.
            float seit = n == 1 ? 0f : (k == 0 ? -20f : 20f);
            var quer = new Vector2(-dir.Y, dir.X) * seit;
            _shots.Add(new Projectile
            {
                Pos = a.Pos,
                Aim = aim + quer,
                Target = -1,                     // die ZELLE, nicht der Griff
                Shooter = -1,                    // ⚠ siehe Klassenkopf
                Damage = a.Attack,
                Facing = DirToFacing(dir),
                Kind = flug, Art = art,
                // bug-499 (KayelGee R5#21): das Tempo ist eine TEMPOEINHEIT wie
                // bei jedem Bodengeschoss (MapEntityLayer.Fire) und braucht den
                // Massstab px/s — ohne ihn flog die Bombe mit 3 statt 150 px/s
                // und »stand in der Luft«. --luftschuss-tempo-alt = alte Zahl.
                Speed = LuftTempoPx = (tempo > 0 ? tempo : 10)
                                      * (LuftschussTempoAlt ? 1f : PxPerProjectileSpeed),
                Weite = a.Pos.DistanceTo(aim + quer),
                Scheitel = 0f,                   // GERADE Bahn, kein Bogen
                HoeheStart = a.Alt,
                HoeheZiel = 0,                   // sinkt zu Boden
                AbwurfNr = AlterWurfSatz(a, aim),   // nur --bomber-abwurf-check (Nullmodell)
            });
            LuftGeschosse++;
        }
        // Klang: Jagdflieger 8 + rand&1, Bomber 114 fest.
        int klang = a.Kind == 2 ? 114 : 8 + Audio.GameSounds.KlangWuerfel(2);
        Audio.GameSounds.PlayAt(klang, a.Col, a.Row);
        // ⚠ KEIN Muendungsfeuer — bei keiner Art.
        return true;
    }

    // ================= der Strahl des Kampfhubschraubers =====================

    /// <summary>Ein laufender Strahl: wo er ist, wohin er will, und was er
    /// anrichtet. Das Original führt 200 solche Sätze zu 44 Byte
    /// (Tafel 0x87B448).</summary>
    private sealed class Strahl
    {
        public Vector2 Pos, Ziel;
        public int Zielgriff, Schaden, Owner;
        public string Name = "";
        /// <summary>bug-514: der Bodenschuetze (Fussvolk-Laser), sonst −1.</summary>
        public int Schuetze = -1;
        public string Punkt = "strahlpunkt";
        /// <summary>bug-658 (29.10.2026): der BODENZWEIG des Strahltakts 0x454CF0 —
        /// Startpunkt, Laenge L in Feineinheiten (= Bildpunkten), Taktzaehler n
        /// (+0x25, der Anleger 0x45542B setzt 1), Schritt 20 und Zuschlag 4
        /// (0x454E21). Nur fuer den Fussvolk-Laser; der Hubschrauber bleibt beim
        /// alten Weg (siehe <see cref="BodenstrahlAlt"/>).</summary>
        public bool Boden;
        public Vector2 Start;
        public float Laenge;
        public int N = 1, Punkte, Takte;
        /// <summary>bug-686 (30.10.2026): der HUBSCHRAUBERZWEIG desselben Strahltakts —
        /// Marke +0x25 &gt; 0x80 (der Anleger 0x455320 setzt sie fuer Schuetzen &gt; 20000):
        /// Schritt 40, Zuschlag 6, Art 73. <see cref="BildHoehe"/> = die Hoehe des
        /// Hubschrauberbildes ueber dem Boden beim Anlegen; der Strich laeuft von dort
        /// schraeg zur Zielhoehe (0x455048). Nicht gesetzt mit <c>--heli-strahl-alt</c>.</summary>
        public bool Heli;
        public float BildHoehe;
        /// <summary>Erster und letzter gesetzter Punkt (fuer den Pruefstand).</summary>
        public Vector2 ErsterPunkt, LetzterPunkt;
    }

    /// <summary><c>--heli-strahl-alt</c> — der Hubschrauberstrahl wieder wie bis 30.10.2026:
    /// ein Punkt je Takt, 40 px je Takt am Boden, Treffer nach ⌈L/40⌉ Takten. bug-686.</summary>
    public static bool HeliStrahlAlt;

    /// <summary>Schritt und Zuschlag des Hubschrauberzweigs (0x454DD1: <c>mov esi,0x28 /
    /// mov ebx,6 / Art 0x49</c>).</summary>
    private const int HeliSchritt = 40, HeliZuschlag = 6;

    /// <summary>Zaehler fuer den Pruefstand <c>--helistrahl-check</c>: jeder angekommene
    /// Hubschrauberstrahl mit Laenge, Punkten und Takten (neuer und alter Weg).</summary>
    public int HsAngekommen;
    public readonly List<(float Laenge, int Punkte, int Takte)> HsMessung = new();

    /// <summary><c>--bodenstrahl-alt</c> — der Fussvolk-Laser wieder wie bis 29.10.2026:
    /// ein 2×2-Punkt je Takt, 40 px je Takt (doppelt so schnell wie das Original).</summary>
    public static bool BodenstrahlAlt;

    /// <summary>Schritt und Zuschlag des Bodenzweigs (0x454E21: <c>mov esi,0x14 /
    /// mov ebx,4 / Art 0x4A</c>).</summary>
    private const int BodenSchritt = 20, BodenZuschlag = 4;

    /// <summary>Zaehler fuer den Pruefstand: Bodenstrahlen angekommen, ihre Punkte und
    /// Takte (Summe), und der letzte Strahl einzeln (Laenge, Punkte, Takte).</summary>
    public int BsAngekommen, BsPunkteSumme, BsTakteSumme;
    public (float Laenge, int Punkte, int Takte) BsLetzter;

    private readonly List<Strahl> _strahlen = new();

    /// <summary>So viele Strahlsätze führt das Original.</summary>
    private const int StrahlPlaetze = 200;

    /// <summary>Wie weit ein Strahl je Takt vorrückt — <b>40 Feineinheiten</b>,
    /// aus dem Strahltakt <c>0x454CF0</c> (F <c>0x4539A0</c>). Bei uns ist eine
    /// Zelle 40 Bildpunkte breit, ein Takt also etwa eine Zelle.</summary>
    private const float StrahlSchritt = 40f;

    /// <summary>
    /// Zwei Strahlen aus zwei Mündungen (Tafel 0x8832F0). ⚠ Die Mündungen
    /// selbst führen wir nicht — die zwei Strahlen starten bei uns beide an der
    /// Lage des Flugzeugs, mit dem Seitenversatz am ZIEL. Benannte Vereinfachung.
    /// </summary>
    private void StrahlAnlegen(Special a, Entity ziel, Vector2 aim)
    {
        for (int k = 0; k < 2; k++)
        {
            if (_strahlen.Count >= StrahlPlaetze) break;
            var dir = (aim - a.Pos).Normalized();
            var quer = new Vector2(-dir.Y, dir.X) * (k == 0 ? -20f : 20f);
            _strahlen.Add(new Strahl
            {
                Pos = a.Pos, Ziel = aim + quer,
                Zielgriff = a.Target, Schaden = a.Attack, Owner = a.Owner,
                Name = a.Name,
                // bug-686: der Hubschrauberzweig des Strahltakts (HeliStrahlTakt). L ist die
                // Laenge am BODEN wie im Original (dx, dy der Kartenlage, 0x454D72), die Hoehe
                // laeuft getrennt. Start/Laenge auch im alten Weg — nur fuer den Pruefstand.
                Heli = !HeliStrahlAlt, Start = a.Pos, Laenge = (aim + quer - a.Pos).Length(),
                BildHoehe = HeliBildHoehe(a),
            });
            LuftStrahlen++;
        }
        Audio.GameSounds.PlayAt(8 + Audio.GameSounds.KlangWuerfel(2), a.Col, a.Row);
    }

    /// <summary>
    /// <b>Der Strahltakt <c>0x454CF0</c></b> (F <c>0x4539A0</c>) — jeder Strahl
    /// rückt <b>40 Feineinheiten je Takt</b> vor und setzt je Einheit einen
    /// PUNKT. So entsteht die wachsende gelbe Punktlinie, die man im Original
    /// sieht und die bei uns fehlte.
    ///
    /// <para>Der Punkt ist ANIM-Folge <b>73</b>, ein gelber <b>2×2</b>-Fleck
    /// (74 ist der grüne der Boden-Laserhandwaffen). ⚠ Beide gab bis heute
    /// NIEMAND aus — siehe <c>Import/InterfaceExporter.Picked</c>; nachgemessen
    /// ist die Tinte 2×2 auf einer Leinwand von 21×20, verankert unten rechts.
    /// </para>
    ///
    /// <para>Bei Ankunft ruft das Original <c>Zasah</c> auf die ZIELZELLE
    /// (@0x454EDC, F 0x453B7F) — es trifft also, was dort steht, nicht
    /// zwingend das angepeilte Ziel.</para></summary>
    private void StrahlTakt()
    {
        for (int i = _strahlen.Count - 1; i >= 0; i--)
        {
            var s = _strahlen[i];
            if (s.Boden && !BodenstrahlAlt) { if (BodenstrahlTakt(s)) _strahlen.RemoveAt(i); continue; }
            // bug-686: der Hubschrauber durch dieselbe Punktschleife (40 Punkte je Takt).
            if (s.Heli) { if (BodenstrahlTakt(s)) _strahlen.RemoveAt(i); continue; }
            var weg = s.Ziel - s.Pos;
            float rest = weg.Length();
            s.Takte++;                       // bug-658: Zaehler auch im alten Weg (Nullmodell)
            if (rest <= StrahlSchritt)
            {
                // angekommen: der Treffer sitzt auf der ZIELZELLE
                _strahlen.RemoveAt(i);
                if (s.Boden)
                {
                    BsAngekommen++; BsPunkteSumme += s.Punkte; BsTakteSumme += s.Takte;
                    BsLetzter = (s.Laenge, s.Punkte, s.Takte);
                }
                else { HsAngekommen++; HsMessung.Add((s.Laenge, s.Punkte, s.Takte)); }   // bug-686, Nullmodell
                if (CellAt(s.Ziel) is { } zc)
                {
                    int c = Mathf.RoundToInt(zc.X), r = Mathf.RoundToInt(zc.Y);
                    ZellEinschlag(s.Schuetze, c, r, s.Schaden, 0);
                }
                else if (s.Zielgriff >= 0 && s.Zielgriff < _entities.Count)
                {
                    var t = _entities[s.Zielgriff];
                    if (!t.Dead)
                        ApplyHit(-1, s.Zielgriff, t, s.Schaden,
                                 $"LUFTSTRAHL {s.Name} (Spieler {s.Owner})");
                }
                continue;
            }
            s.Pos += weg.Normalized() * StrahlSchritt;
            _effects.Add(new Effect { Pos = s.Pos, Kind = s.Punkt, FrameTime = 0.08f });
            s.Punkte++;
        }
    }

    /// <summary>
    /// ⭐ 29.10.2026, bug-658 (KayelGee R8 K16: »schwere Infanterie schiesst keine
    /// sichtbaren Laser«) — <b>der Bodenzweig des Strahltakts</b> <c>0x454CF0</c>
    /// (F <c>0x4539A0</c>), selbst nachgelesen (C):
    /// <code>
    ///   0x454D72..0x454D89  L = sqrt(dx² + dy²) in Feineinheiten, mindestens 1
    ///   0x454DCC  Marke &gt; 0x80 (Hubschrauber)     -> Schritt 40, Zuschlag 6, Art 73
    ///   0x454E09  Zieleinheit +0x43 == 0xBF          -> Schritt 20, Zuschlag 6, Art 73
    ///   0x454E21  sonst (Bodenschuetze)              -> Schritt 20, Zuschlag 4, Art 74
    ///   0x454E32  vorher = (n−1)·Schritt + Zuschlag ; jetzt = n·Schritt + Zuschlag
    ///   0x454E48  jetzt &gt;= L -> jetzt := L, Ankunft (Zasah auf die Zielzelle), Satz frei
    ///   0x454FD3  sonst n++
    ///   0x454FDB  fuer t = vorher … jetzt−1: Punkt = Start + (Ziel − Start)·t/L  (0x45500A)
    /// </code>
    /// <para>Die Punktschleife steht HINTER allen drei Zweigen und laeuft auch im
    /// Ankunftstakt: je Takt ein Strich aus 20 Punkten (2×2-Flecken), zusammen eine
    /// geschlossene Linie aus L − 4 Punkten, Treffer nach ⌈(L − 4)/20⌉ Takten. Bei uns
    /// stand ein Punkt je 40 px und Ankunft nach ⌈L/40⌉ Takten — bei 4 Zellen 3 Punkte
    /// statt 156, und doppelt so schnell.</para>
    /// <para>⚠ UNSERE SETZUNGEN: die Lebensdauer eines Punktes (0,08 s, wie bisher —
    /// die eines sec42-Ein-Bild-Satzes ist ungelesen); die Hoehe des Strichs (das
    /// Original rechnet −40 Feineinheiten @0x455048, wir ziehen von der Muendung zum
    /// Bildpunkt des Ziels). Der gelbe Sonderfall <c>+0x43 == 0xBF</c> der Zieleinheit
    /// ist ungedeutet und nicht gebaut. ⚠ Der Hubschrauberstrahl laeuft durch dieselbe
    /// Punktschleife (40 Punkte je Takt) — seit 30.10.2026 auch bei uns (bug-686, siehe
    /// unten; <c>--heli-strahl-alt</c> = der Stand davor).</para>
    /// <para>⭐ 30.10.2026, bug-686 (pruefung-rest-b-opus.md §2) — <b>der HUBSCHRAUBERZWEIG</b>
    /// laeuft durch dieselbe Schleife: Schritt 40, Zuschlag 6 (0x454DD1), also L − 6 Punkte
    /// und Treffer nach ⌈(L − 6)/40⌉ Takten (bei uns stand ⌈L/40⌉ und ein Punkt je Takt).
    /// Die Hoehe: <c>Quellhoehe + (Zielhoehe − Quellhoehe)·t/L − 40</c> (0x455048), Quelle
    /// = Flughoehe +0x6DDF7E, Ziel = Gelaende·15 (0x4276xx, Anleger-Aufruf @0x4276B6) — der
    /// Strich laeuft also SCHRAEG vom Hubschrauber zum Boden. ⚠ UNSERE SETZUNG: wir ziehen
    /// vom Bildpunkt des Hubschraubers (Kartenlage − <see cref="BildHoehe"/>) linear zum
    /// Zielpunkt am Boden; die −40 von 0x455048 nehmen wir wie beim Bodenstrahl nicht.
    /// Beim Treffer ohne Zelle bleibt der alte Rueckfall auf das angepeilte Ziel.</para>
    /// <para>true = angekommen (der Satz ist frei).</para></summary>
    private bool BodenstrahlTakt(Strahl s)
    {
        int schritt = s.Heli ? HeliSchritt : BodenSchritt, zuschlag = s.Heli ? HeliZuschlag : BodenZuschlag;
        float L = Mathf.Max(1f, s.Laenge);
        int vorher = (s.N - 1) * schritt + zuschlag;
        int jetzt = s.N * schritt + zuschlag;
        bool an = jetzt >= L;
        if (an) jetzt = (int)L;
        else s.N++;
        s.Takte++;
        var d = s.Ziel - s.Start;
        for (int t = vorher; t < jetzt; t++)
        {
            float f = t / L;
            var hoch = s.Heli ? new Vector2(0f, s.BildHoehe * (1f - f)) : Vector2.Zero;
            var pos = s.Start + d * f - hoch;
            _effects.Add(new Effect { Pos = pos, Kind = s.Punkt, FrameTime = 0.08f });
            if (s.Punkte == 0) s.ErsterPunkt = pos;
            s.LetzterPunkt = pos;
            s.Punkte++;
        }
        if (!an) return false;
        if (s.Heli) { HsAngekommen++; HsMessung.Add((s.Laenge, s.Punkte, s.Takte)); }
        else
        {
            BsAngekommen++; BsPunkteSumme += s.Punkte; BsTakteSumme += s.Takte;
            BsLetzter = (s.Laenge, s.Punkte, s.Takte);
        }
        if (CellAt(s.Ziel) is { } zc)
            ZellEinschlag(s.Schuetze, Mathf.RoundToInt(zc.X), Mathf.RoundToInt(zc.Y), s.Schaden, 0);
        else if (s.Heli && s.Zielgriff >= 0 && s.Zielgriff < _entities.Count && !_entities[s.Zielgriff].Dead)
            ApplyHit(-1, s.Zielgriff, _entities[s.Zielgriff], s.Schaden,
                     $"LUFTSTRAHL {s.Name} (Spieler {s.Owner})");
        return true;
    }

    /// <summary>bug-686: wie hoch das Hubschrauberbild ueber dem Boden steht — dieselbe
    /// Rechnung wie <see cref="AirHoehePx"/>, aber ohne deren Nachfuehrung (die hat
    /// Zustand; ein Pruefstands- oder Schussaufruf darf ihn nicht verschieben).</summary>
    private float HeliBildHoehe(Special a)
    {
        if (FlughoeheBildAlt) return AirShadowDrop;
        float hub = !FlughuepfenAlt && !float.IsNaN(a.HubPx) ? a.HubPx : ElevOf(a.Col, a.Row) * 15f;
        return Mathf.Max(0f, a.Alt - hub);
    }

    /// <summary>
    /// <c>--luftschuss-check</c> — die Zeile zum Luftschuss.
    ///
    /// <para>⚠ Sie nennt zuerst, ob überhaupt geschossen wurde. Ohne das ist
    /// jede 0 darunter keine Messung, sondern ein Lauf ohne Luftkampf — die
    /// Falle, die mich heute schon dreimal erwischt hat.</para></summary>
    /// <summary><c>--luftschuss-tempo-alt</c> — Bomben und Jaegergeschosse fliegen
    /// wieder mit der rohen Tempozahl als px/s (50× zu langsam). bug-499.</summary>
    public static bool LuftschussTempoAlt;

    /// <summary>Das Tempo des letzten Luftgeschosses in px/s, fuer die Pruefzeile.</summary>
    private float LuftTempoPx;

    public string LuftschussLine()
    {
        if (LuftSalven == 0)
            return "luftschuss: keine Salve — dieser Lauf sagt darueber NICHTS";
        return $"luftschuss: {LuftSalven} Salven, {LuftGeschosse} Geschosse, " +
               $"{LuftStrahlen} Strahlen (offen {_strahlen.Count}), Tempo {LuftTempoPx:0} px/s" +
               (LuftschussTempoAlt ? "   ⚠ Nullmodell --luftschuss-tempo-alt" : "") +
               (LuftschussAlt ? "   ⚠ Nullmodell --luftschuss-alt: Geschosse und Strahlen 0"
                              : HeliStrahlAus ? "   ⚠ Nullmodell --heli-strahl-aus: Strahlen 0"
                                              : "");
    }
}
