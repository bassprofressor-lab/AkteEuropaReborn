namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐⭐ <b>DIE TRÜMMER EINES ZERSTÖRTEN FAHRZEUGS</b> (24.08.2026).
///
/// <para>Gemeldet: »wenn Einheiten zerstört werden, fliegen die Teile des
/// Fahrzeugs etwas herum, passiert bei uns auch nicht«.</para>
///
/// <para>⚠⚠ <b>Und danach: »da fliegt bisschen was weg, sieht nicht korrekt aus
/// gegenüber dem Original«.</b> Er hatte recht, und zwar dreifach — die erste
/// Fassung hatte eine <b>feste Flugdauer</b> (in Wahrheit Strecke/Tempo), eine
/// <b>gerade Bahn</b> (in Wahrheit ein Wurf mit Schwerkraft) und <b>keinen
/// Rauchschweif</b>. Alle drei standen im Original und waren ungelesen, weil
/// ich nach dem Anlegen aufgehört habe zu lesen statt bis zum Takt
/// weiterzugehen. Das ist die Lehre: ein Effekt ist nicht verstanden, solange
/// nur sein GEBURTSORT gelesen ist.</para>
///
/// <para><b>Geworfen wird in zwei Schleifen</b>, gleich hinter dem
/// Explosionsbild (<c>rand()%9 + 510</c>):</para>
///
/// <code>
///   0x40B61D   rand()%10 + 10  Stueck   ->  Sorte 0, Streuung 3
///   0x40B662   rand()%6  +  5  Stueck   ->  Sorte 1, Streuung 5
/// </code>
///
/// <para><b>Der Teilchenmacher</b> @0x4AD520 / @0x4AD99C:</para>
///
/// <code>
///   Sorte 0 -> Bildfolge rand()%6  + 19      (19..24)
///   Sorte 1 -> Bildfolge rand()%10 + 29      (29..38)
///   vx = rand()%(2·Streuung) − Streuung ; ebenso vy        @0x4AD5A7
///   |v| &lt; 3  ->  v = ±3                                    @0x4AD5C9
///   Zielzelle = Quelle + (vx,vy), auf die Karte geklemmt    @0x4AD67A
///   je Ende: Feinlage rand()%40 / rand()%20, Hoehe Gelaende·15
///   Tempo = rand()%15 + 4                                   @0x4AD99C
///   ⭐ Bildfolge &lt; 25  ->  KEIN Schweif  UND Tempo HALBIERT  @0x4AD9AC
///      Bildfolge ≥ 25  ->  Schweif Rauch  (240..242)
///      Bildfolge ≥ 39  ->  Schweif Feuer  (210..212)  — trifft die Truemmer nicht
/// </code>
///
/// <para><b>Der Takt</b> @0x4ADB80, und er ist der Teil, den ich zuerst
/// uebersprungen hatte:</para>
///
/// <code>
///   dx = (Zielspalte−Spalte)·40 + Feinlagendifferenz
///   dy = ((Zielzeile−Zeile)·20 + Feinlagendifferenz) · 2   ; isometrisch entzerrt
///   n  = sqrt(dx²+dy²) / Tempo                             ; TAKTE bis zur Ankunft
///   Grundhoehe += (Zielhoehe − Grundhoehe) / n             ; linear
///   vz  −= Schwerkraft ; Hoehe += vz                       ; JEDEN Takt -> Bogen
///   Bild = (Uhr + Platz) % Bildzahl                        ; laeuft mit der Uhr
/// </code>
///
/// <para>⭐ <b>Der Beleg fuer die Sorten steht in ANIM.CWA selbst:</b> es gibt
/// <b>genau sechs</b> Folgen 19..24 (je EIN Bild, 2..3 Bildpunkte hoch —
/// Splitter) und <b>genau zehn</b> Folgen 29..38 (je SECHS Bilder, 4..10 hoch —
/// taumelnde Brocken). Die Modulo-Zahlen treffen die vorhandenen Folgen auf den
/// Punkt.</para>
///
/// <para>⚠ <b>NUR FAHRZEUGE.</b> Die Sprungtafel @0x40B858 verzweigt nach der
/// Gattung (+0x0A); nur Zweig 0 laeuft durch die Schleifen, die Faelle bei
/// 0x40B6A9/0x40B6B1/0x40B6B9 springen daran vorbei.</para>
///
/// <para>⭐⭐ <b>04.10.2026 (bug-426) — DER BOGEN IST JETZT GANZ GELESEN</b>,
/// und er war 3- bis 8-mal zu flach. KayelGee: »Trümmer fliegen im Original in
/// einer Parabel aus der Explosion heraus.« Lesung
/// <c>berichte/nebel-explosionen-fable.md</c> §2.4/§2.5 (Konstanten
/// <c>0x4F2E74 = 2,0</c>, <c>0x4F2E78 = 1,0</c>, <c>0x4F2E7C = 0,5</c>):</para>
/// <code>
///   0x4ADA0D  d  = √(dx² + dy²)   dx = 40·Δsp + Δfein, dy = 40·Δze + Δfein
///             m  = d / (2·T)  ;  h0 = (T &gt;&gt; 3) + 2
///             g  = 2d / (h0·m·(m+1))  -> +0x28 ; vz0 = g·m -> +0x24
///   0x4ADCE9  Bogen += vz ; vz −= g          ; je TAKT
///   ⇒ Scheitel = g·m(m+1)/2 = d / h0  — ein Halbes bis ein Viertel der Strecke
///   0x4AE04A  Z = Grund + Bogen &gt; Bodenhoehe am Punkt (0x4B5CE0) ∧ nicht da
///             -> weiter, sonst LANDUNG; Brocken: Aufschlagbild 230 + rand&amp;3
/// </code>
/// <para>Und die Uhr: der Teilchentakt <c>0x4ADB80</c> laeuft im Taktblock
/// (@0x4165F1), wie das Explosionsbild (@0x4164A7) und das Wrackalter
/// (@0x4164FF) — <b>eine Uhr fuer Wolke, Teile, Schweif und Wrack</b>
/// (<see cref="EffektTakteJeSekunde"/>).</para>
///
/// <para>⚠⚠ <b>WAS UNSER BLEIBT:</b> nur noch <b>wie lang ein Takt ist</b>
/// (<see cref="EffektTakteJeSekunde"/> = 25, die Bildrate, mit der die Wolke
/// seit dem 24.08. laeuft) und die Lage des Aufschlagbilds (siehe
/// <see cref="Aufschlag"/>). Gegenschalter je Teil:
/// <c>--truemmer-bogen-alt</c> (Scheitel 3,5·h0 wie bisher, samt der alten
/// Endpunkte mit der DOPPELT abgezogenen Hoehe, s. <see cref="EinTeil"/>),
/// <c>--truemmer-uhr-alt</c> (85 Takte/s fuer den Flug, Schweif je 0,06 s),
/// <c>--truemmer-aufschlag-aus</c> (feste Dauer, keine Bodenlandung, kein
/// Aufschlagbild). Pruefstand <c>--truemmer-check</c>
/// (Simulation/TruemmerCheck.cs).</para>
///
/// <para>⚠ Nebenbefund derselben Lesung: der Schweif ist NICHT der Qualm
/// beschaedigter Fahrzeuge, den er getrennt gemeldet hat. Der haengt woanders
/// und ist weiter offen.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Ein fliegendes Teil.</summary>
    private struct Truemmer
    {
        public Vector2 Von, Nach;   // Bildpunkte, Bodenhoehe bereits eingerechnet
        public float Zeit, Dauer;   // Sekunden
        public float Scheitel;      // Bildpunkte ueber der Verbindungslinie
        public string Folge;
        public bool Schweif;        // Bildfolge >= 25 -> Rauch
        public float SchweifAb;     // naechster Rauchtakt
        // ⭐ 04.10.2026 (bug-426) — der gelesene Takt
        public Vector2 FlachVon, FlachNach;   // Bodenpunkt OHNE Hoehe (Zeile·20 + Fein)
        public float GrundVon, GrundNach;     // Gelaende·15 je Ende (@0x4AD6BB)
        public float N;                       // Takte bis zur Ankunft (@0x4ADBB6)
        public float G, M;                    // Schwerkraft, m (@0x4ADA0D..0x4ADAB2)
        public int Takt;                      // abgelaufene Takte
        public bool Brocken;                  // Stil 1 (Folge 25..38): Aufschlagbild
        public float Strecke, MaxBogen;       // fuer den Pruefstand
        public int Wurf;                      // welche Sprengung (Pruefstand)
    }

    private readonly List<Truemmer> _truemmer = new();

    /// <summary>Wie viele Teile gerade fliegen — fuer den Pruefstand.</summary>
    public int TruemmerZahl => _truemmer.Count;

    /// <summary>Die Hoehe eines Endes: <c>Gelaende · 15</c> @0x4AD6BB.</summary>
    private const int TruemmerHoehe = 15;

    /// <summary>⚠ UNSERE EICHUNG der Bogenhoehe — siehe Klassenkopf, Punkt 2.
    /// Der Scheitel ist <c>BogenFaktor · ((Tempo &gt;&gt; 3) + 2)</c>.</summary>
    private const float BogenFaktor = 3.5f;

    /// <summary>
    /// ⚠⚠ <b>UNSERE EICHUNG — wie viele Takte eine Sekunde hat.</b>
    ///
    /// <para>Gemeldet: »die Teile scheinen mir zu weit zu fliegen oder so«. Die
    /// STRECKE ist es nicht — die Streuung von 3 bzw. 5 ZELLEN ist gelesen
    /// (@0x4AD5FB) und der Takt bestaetigt die Einheit, weil er
    /// <c>(Zielspalte−Spalte)·40</c> rechnet, also mit der Kachelbreite. Es war
    /// die DAUER: hier stand zuerst <see cref="PxPerProjectileSpeed"/> (16
    /// Takte je Sekunde), und damit war ein Stueck bis zu VIER SEKUNDEN
    /// unterwegs. Es flog nicht zu weit, es zog zu lange.</para>
    ///
    /// <para>⚠ Sie steht jetzt als EIGENE Zahl da und nicht mehr geborgt: die
    /// 16 der Geschosse ist an deren Fluggeschwindigkeit geeicht, nicht am
    /// Takt des Originals, und zwei verschiedene Dinge an derselben Konstante
    /// aufzuhaengen hat schon einmal genau so einen Fehler getarnt.</para>
    ///
    /// <para>Womit geeicht: ein mittlerer Wurf (3 Zellen, Tempo 5) laeuft damit
    /// gut vier Zehntelsekunden, ein schneller Brocken knapp drei.</para>
    /// </summary>
    private const float TruemmerTakteJeSekunde = 85f;

    /// <summary>Wie oft der Takt einen Rauchball setzt: <b>jeder dritte</b>
    /// (@0x4ADD60, <c>rand()%3 == 0</c>). In Sekunden ueber denselben
    /// Taktbegriff wie das Tempo. ⚠ Nur noch fuer <c>--truemmer-uhr-alt</c>:
    /// seit dem 04.10.2026 wird je Effekttakt gewuerfelt, wie im Original.</summary>
    private const float SchweifAbstand = 0.06f;

    /// <summary>
    /// ⭐ 04.10.2026 (bug-426) — <b>EINE UHR</b> fuer Explosionsbild, Truemmer,
    /// Schweif und Wrack. Im Original laufen alle vier im Taktblock der
    /// Hauptschleife (<c>0x416077..0x4168B4</c>: Anims @0x4164A7, Wrackalter
    /// @0x4164FF, Teilchen @0x4165F1) — ein Bild, ein Teilchenschritt, ein
    /// Alterstakt je Spieltakt. Bei uns lief die Wolke mit 25 Bildern/s, die
    /// Teile mit 85 Takten/s: ein 3-Zellen-Wurf (24 Takte) war nach 0,28 s
    /// weg, die 22-Bild-Wolke stand 0,88 s.
    ///
    /// <para>⚠ <b>UNSERE SETZUNG ist die Zahl 25</b> — die Bildrate, mit der die
    /// Wolke seit dem 24.08.2026 laeuft (<c>FrameTime 0,04</c>). Gelesen ist nur,
    /// dass es EINE Uhr ist. Gegenschalter <c>--truemmer-uhr-alt</c>.</para>
    /// </summary>
    public const float EffektTakteJeSekunde = 25f;

    /// <summary><c>--truemmer-bogen-alt</c> — Scheitel 3,5·h0 und die alten
    /// Endpunkte (Stand feec479).</summary>
    public static bool TruemmerBogenAlt;
    /// <summary><c>--truemmer-uhr-alt</c> — Flug mit 85 Takten/s, Schweif je
    /// 0,06 s (Stand feec479).</summary>
    public static bool TruemmerUhrAlt;
    /// <summary><c>--truemmer-aufschlag-aus</c> — feste Flugdauer, keine
    /// Landung auf Bodenhoehe, kein Aufschlagbild (Stand feec479).</summary>
    public static bool TruemmerAufschlagAus;

    /// <summary>Wie viele Aufschlagbilder (Folge 230..233) die Brocken gesetzt
    /// haben — und wie viele Brocken geworfen wurden. Soll gleich.</summary>
    public int TruemmerAufschlaege, TruemmerBrockenGeworfen;

    /// <summary>Die laufende Nummer der Sprengung, fuer den Pruefstand.</summary>
    private int _truemmerWurf;

    /// <summary>Gelandete Teile (fuer den Pruefstand): Wurf, Brocken?,
    /// Flugdauer in Sekunden, Strecke d, gemessener Scheitel.</summary>
    internal readonly List<(int Wurf, bool Brocken, float Sekunden, float Strecke, float Scheitel)> TruemmerGelandet = new();

    /// <summary>Wie viele Teile seit dem Start geworfen wurden. ⚠ Ohne die Zahl
    /// sieht »ich sehe keine Truemmer« genauso aus wie »es wurden keine
    /// geworfen« (Arbeitsweise 30).</summary>
    public int TruemmerGeworfen;

    /// <summary>Die beiden Schleifen von @0x40B61D und @0x40B662.</summary>
    private void TruemmerWerfen(Entity opfer)
    {
        // Gattung 0 = Fahrzeug. Alles andere springt an den Schleifen vorbei.
        if (opfer.GameUnitType != 0 || opfer.Infantry >= 0 || opfer.IsBuilding) return;
        _truemmerWurf++;

        int n = Simulation.Determinism.Roll(10) + 10;
        for (int k = 0; k < n; k++) EinTeil(opfer, sorte: 0, streuung: 3);

        n = Simulation.Determinism.Roll(6) + 5;
        for (int k = 0; k < n; k++) EinTeil(opfer, sorte: 1, streuung: 5);
    }

    /// <summary>
    /// <b>DER TOD EINES GEBAEUDES</b> — die »massive Zerstoerung«, die er am
    /// 06.09.2026 vermisst hat: »im original ist die massive zerstoerung von
    /// gebaeuden drin (Explosionen, Splitter wie wenn wir einen Panzer
    /// zerstoeren), das fehlt noch«.
    ///
    /// <para><b>Gelesen</b> (Bericht <c>berichte/gebaeudetod-fable.md</c>, die
    /// zwei tragenden Stellen selbst nachgeschlagen): der Tod ist der
    /// Sonderfall »Stufe == letzte« in <c>0x4C95E0</c>, und ganz am Ende
    /// zuendet er <b>n/2 Braende</b> auf zufaelligen Zellen des Gebaeudebildes:
    /// <c>@0x4C9B8B mov al, [esp+0x20]; shr al, 1</c> — also die HAELFTE der
    /// belegten Musterzellen —, dann je Brand ein Ruf von <c>0x40136B</c> mit
    /// einer zufaellig gezogenen Zelle (<c>@0x4C9B8F..0x4C9BBE</c>).</para>
    ///
    /// <para>Jeder Brand (<c>0x4AE4C0</c>) wirft eine <b>Explosion</b>:
    /// <c>@0x4AE672 ecx = 9; idiv ecx; @0x4AE679 add dx, 0x1FE</c> — also
    /// <b>ANIM 510..518</b>, genau die Folge, die auch der Fahrzeugtod nimmt
    /// (<c>@0x40B5F6</c>). Ein Gebaeude bekommt davon also <b>n/2 statt einer</b>,
    /// verteilt ueber seinen Fussabdruck.</para>
    ///
    /// <para>⚠ <b>Drei benannte Luecken</b>, damit niemand mehr vermutet, als
    /// dasteht:</para>
    /// <list type="bullet">
    /// <item>Der <b>Splitter</b> je Brand ist im Original <c>ANIM 200..204</c>
    /// mit Streuung 12 (<c>@0x4AE6C9</c>) — eine andere Folge als die
    /// Fahrzeugsplitter 19..24/29..38, die wir ausgegeben haben. Wir werfen
    /// darum vorerst keinen.</item>
    /// <item>Das <b>Nachbrennen</b> (<c>0x4AE760</c>: Folgeexplosionen, Rauch,
    /// Flammen ueber rund 1470 Takte) ist nicht gebaut.</item>
    /// <item>Der Klang <b>135</b> beim Tod (<c>@0x4C9924</c>) ist laut
    /// Klangbank eine 1,36 s lange SPRACHZEILE, keine Explosion — er gehoert
    /// zur Meldung, nicht zum Bild, und ist hier nicht gesetzt.</item>
    /// </list>
    ///
    /// <para>⚠ <b>UNSERE Naeherung:</b> das Original zaehlt die belegten Zellen
    /// des MUSTERS, wir nehmen das Fussabdruckrechteck. Auf einem vollen
    /// Rechteck ist das dasselbe; bei einem Muster mit Loechern zuenden wir ein
    /// paar Braende zu viel.</para></summary>
    private void GebaeudeSprengen(Entity b)
    {
        if (!b.IsBuilding || GebaeudeSprengungAus) return;
        int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
        int n = w * h / 2;                       // @0x4C9B8B: shr al, 1
        for (int k = 0; k < n; k++)
        {
            int c = b.Col + Simulation.Determinism.Roll(w);
            int r = b.Row + Simulation.Determinism.Roll(h);
            _effects.Add(new Effect
            {
                Pos = CellCenter(c, r) - new Vector2(0, 6),
                Kind = "sprengung" + Simulation.Determinism.Roll(9),   // ANIM 510..518
                FrameTime = 0.04f,
            });

            // ⭐ 06.09.2026 — und je Brand EIN Splitter, `rand()%5 + 200` mit
            // Streuung 12 (@0x4AE6C9). Eine andere Sorte als die
            // Fahrzeugsplitter (19..24/29..38), darum eine eigene Folge.
            // ⚠ Wirft nichts, solange die fuenf Folgen 200..204 nicht
            // ausgegeben sind — `EinTeil` steigt bei einer leeren Bildfolge
            // aus. Das ist Absicht: lieber kein Splitter als ein falscher.
            EinTeil(new Entity { Col = c, Row = r }, sorte: 2, streuung: 12);
        }
        GebaeudeSprengungen++;
        GebaeudeSprengbilder += n;
        RuineSchneidetGleise(b);
    }

    /// <summary><c>--ruine-ohne-gleisschnitt</c> — der Stand von vor dem
    /// 20.09.2026: eine Ruine lässt die Gleise an ihrem Fußabdruck heil.</summary>
    public static bool RuineOhneGleisschnitt;

    /// <summary>Wie viele Gleiszellen eine Ruine gebrochen hat — die Zahl für
    /// den Prüfstand.</summary>
    public int RuineGleisbrueche;

    /// <summary>
    /// ⭐⭐⭐ <b>EINE RUINE BRICHT DIE GLEISE AN IHREM FUSSABDRUCK</b>
    /// (20.09.2026).
    ///
    /// <para><b>Anlass:</b> gemeldet als »im original ist bei k17 die
    /// bahnstrecke teils beschädigt, bei uns ist sie ganz«. Die Lesung
    /// (<c>berichte/bahnschaden-klang-fable.md</c> §1, Fable) hat zuerst seine
    /// Annahme berichtigt: bei Missionsstart ist sie im Original AUCH ganz.
    /// <c>17.CWM</c> sec22 trägt 72 Zellen, <b>0 zerschossen</b>, alle mit
    /// 150 Trefferpunkten; über alle 39 Leveldateien gibt es nur 49
    /// zerschossene Zellen, und die liegen in <c>4.DM</c>. Es gibt auch keinen
    /// Skriptsetzer.
    ///
    /// <para><b>Der Schaden entsteht im SPIEL</b>, und dies ist der Weg, der
    /// uns fehlte: der Gebäude-Stempler <c>0x4C95E0</c> ruft über
    /// <c>0x4B0820</c> die Routine <c>0x4B07C0</c>, und die bricht die Gleise
    /// <b>westlich, nördlich und östlich</b> des Fußabdrucks. In K17 grenzen
    /// <b>15 Gleiszellen an vier Gebäude des Computerspielers 1</b> (Basis,
    /// zwei Fabriken, Feldbahnhof) — dort entsteht im Kampf genau das Bild, das
    /// er im Let's Play gesehen hat. <b>Von ihm bestätigt:</b> »die bahnstrecke
    /// wird tatsächlich im lets play zerstört durch kampf«.</para>
    ///
    /// <para>⚠ <b>Drei Richtungen, nicht vier.</b> Der Süden fehlt, und das ist
    /// gelesen, nicht gewählt. Warum er fehlt, ist ungeklärt — eine Vermutung
    /// wäre, dass die Ruine dort ihr eigenes Bild trägt; sie steht hier
    /// ausdrücklich als Vermutung und nicht als Begründung.</para>
    ///
    /// <para>⚠ Der Bruch läuft über <see cref="RailHit(int,int,int)"/> mit
    /// vollem Schaden, also mit allem, was daran hängt: Trümmerbild,
    /// Stützendurchgang, Stilllegung der Linie und der Bruchlauf bis zum
    /// MAST.</para></summary>
    private void RuineSchneidetGleise(Entity b)
    {
        if (RuineOhneGleisschnitt) return;
        int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
        for (int dx = 0; dx < w; dx++)
            for (int dy = 0; dy < h; dy++)
            {
                int c = b.Col + dx, r = b.Row + dy;
                // West, Nord, Ost — der Süden fehlt, siehe oben
                if (RailHit(c - 1, r, int.MaxValue)) RuineGleisbrueche++;
                if (RailHit(c, r - 1, int.MaxValue)) RuineGleisbrueche++;
                if (RailHit(c + 1, r, int.MaxValue)) RuineGleisbrueche++;
            }
    }

    /// <summary>Wie viele Gebaeude in diesem Lauf gesprengt wurden — fuer den
    /// Pruefstand.</summary>
    public int GebaeudeSprengungen;

    /// <summary>Und wie viele Explosionsbilder dabei geworfen wurden. ⚠ Der
    /// Pruefstand muss DIESE Zahl lesen, nicht die Laenge der Effektliste:
    /// beim Tod entstehen auch andere Effekte, und der erste Anlauf zaehlte
    /// 17 statt 15, weil er alles mitzaehlte.</summary>
    public int GebaeudeSprengbilder;

    /// <summary><c>--gebaeudesprengung-aus</c> — der Stand vor dem 06.09.2026:
    /// ein Gebaeude verschwindet ohne Bild.</summary>
    public static bool GebaeudeSprengungAus;

    private void EinTeil(Entity opfer, int sorte, int streuung)
    {
        // Die Bildfolge, und mit ihr Schweif und Tempo (@0x4AD9AC).
        int seq = sorte == 0 ? 19 + Simulation.Determinism.Roll(6)
                : sorte == 2 ? 200 + Simulation.Determinism.Roll(5)      // Gebaeude
                             : 29 + Simulation.Determinism.Roll(10);
        string folge = sorte == 0 ? "splitter" + (seq - 19)
                     : sorte == 2 ? "bausplitter" + (seq - 200)
                     : "brocken" + (seq - 29);
        var bilder = EffectFrames(folge);
        if (bilder.Count == 0) return;      // nicht ausgegeben -> nichts werfen

        int tempo = Simulation.Determinism.Roll(15) + 4;
        bool schweif = seq >= 25;
        if (!schweif) tempo >>= 1;          // ⭐ Splitter fliegen halb so schnell
        if (tempo < 1) tempo = 1;

        int vx = Wurf(streuung), vy = Wurf(streuung);
        int zc = opfer.Col + vx, zr = opfer.Row + vy;
        if (_nav != null)
        {
            zc = Mathf.Clamp(zc, 0, _nav.Width - 1);
            zr = Mathf.Clamp(zr, 0, _nav.Height - 1);
        }

        // Start- UND Zielfeinlage gewuerfelt (@0x4AD6C0..0x4AD710), je Ende
        // rand%40 / rand%20 ab der linken oberen Zellecke.
        int fx0 = Simulation.Determinism.Roll(40), fy0 = Simulation.Determinism.Roll(20);
        int fx1 = Simulation.Determinism.Roll(40), fy1 = Simulation.Determinism.Roll(20);
        var flachVon = new Vector2(_ox + opfer.Col * TileW + fx0, _oy + opfer.Row * TileH + fy0);
        var flachNach = new Vector2(_ox + zc * TileW + fx1, _oy + zr * TileH + fy1);
        float grundVon = ElevOf(opfer.Col, opfer.Row) * TruemmerHoehe;    // @0x4AD6BB
        float grundNach = ElevOf(zc, zr) * TruemmerHoehe;

        // ⚠⚠ 04.10.2026 — DIE ALTEN ENDPUNKTE ZOGEN DIE HOEHE DOPPELT AB:
        // `CellCenter` enthaelt seit dem Hangbau schon `HubOf` (Hoehe·15 plus
        // Hang), und hier wurde noch einmal `ElevOf·15` abgezogen. Auf K1
        // (Hoehe 3) starteten die Teile 45 px ueber dem Boden. Nur noch mit
        // --truemmer-bogen-alt.
        Vector2 von, nach;
        if (TruemmerBogenAlt)
        {
            von = CellCenter(opfer.Col, opfer.Row) + new Vector2(fx0 - 20, fy0 - 10) - new Vector2(0, grundVon);
            nach = CellCenter(zc, zr) + new Vector2(fx1 - 20, fy1 - 10) - new Vector2(0, grundNach);
        }
        else
        {
            von = flachVon - new Vector2(0, grundVon);
            nach = flachNach - new Vector2(0, grundNach);
        }

        // n = Strecke / Tempo, und die SENKRECHTE Strecke zaehlt doppelt —
        // @0x4ADC0A `lea edi,[eax*2]`, die isometrische Entzerrung.
        float dx = flachNach.X - flachVon.X, dy = (flachNach.Y - flachVon.Y) * 2f;
        if (TruemmerBogenAlt) { dx = nach.X - von.X; dy = (nach.Y - von.Y) * 2f; }
        float takte = Mathf.Sqrt(dx * dx + dy * dy) / tempo;
        if (takte < 1f) takte = 1f;

        // ⭐ Der Bogen des Erzeugers (@0x4ADA0D..0x4ADAB2): beide Achsen ×40 je
        // Zelle, die Feinlage einfach — d = √(dx² + dy²).
        float gx = 40f * (zc - opfer.Col) + (fx1 - fx0);
        float gy = 40f * (zr - opfer.Row) + (fy1 - fy0);
        float d = Mathf.Sqrt(gx * gx + gy * gy);
        int h0 = (tempo >> 3) + 2;
        float m = d / (2f * tempo);
        float g = m > 0f ? 2f * d / (h0 * m * (m + 1f)) : 0f;

        bool brocken = seq >= 25 && seq < 39;            // Stil 1 (@0x4AD9AC)
        if (brocken) TruemmerBrockenGeworfen++;
        float rate = TruemmerUhrAlt ? TruemmerTakteJeSekunde : EffektTakteJeSekunde;
        _truemmer.Add(new Truemmer
        {
            Von = von,
            Nach = nach,
            Zeit = 0f,
            Dauer = takte / rate,
            Scheitel = TruemmerBogenAlt ? BogenFaktor * h0 : (h0 > 0 ? d / h0 : 0f),
            Folge = folge,
            Schweif = schweif,
            SchweifAb = 0f,
            FlachVon = flachVon, FlachNach = flachNach,
            GrundVon = grundVon, GrundNach = grundNach,
            N = takte, G = g, M = m,
            Brocken = brocken,
            Strecke = d,
            Wurf = _truemmerWurf,
        });
        TruemmerGeworfen++;
    }

    /// <summary>Der Bogen nach k Takten: <c>Σ (vz0 − (j−1)·g)</c> fuer
    /// j = 1..k = <c>g·(k·m − k(k−1)/2)</c> (@0x4ADCE9..0x4ADD45). Bei
    /// <c>--truemmer-bogen-alt</c> die alte Parabel <c>4·S·f(1−f)</c>.</summary>
    private static float TruemmerBogen(in Truemmer t, float k)
    {
        if (TruemmerBogenAlt)
        {
            float f = t.N <= 0f ? 1f : Mathf.Clamp(k / t.N, 0f, 1f);
            return 4f * t.Scheitel * f * (1f - f);
        }
        return t.G * (k * t.M - k * (k - 1f) / 2f);
    }

    /// <summary>
    /// ⭐ Das AUFSCHLAGBILD eines Brockens: <c>sec42 (230 + rand&amp;3)</c> bei
    /// der Landung (@0x4AE0DE). Bei uns die Folgen <c>glut0..3</c> (230..233,
    /// schon fuer den Gebaeudebrand ausgegeben).
    /// <para>⚠ UNSERE SETZUNG: die Lage. Das Original legt es bei
    /// <c>(feinX + 24, Hoehe·15 − feinY − 58)</c> an, die Wolke bei
    /// <c>fein − 50</c>; wie diese sec42-Koordinaten auf unseren Effektanker
    /// abbilden, ist nicht gelesen. Wir setzen es wie Explosion und Brand:
    /// Bodenpunkt − (0, 6).</para></summary>
    private void Aufschlag(Vector2 bodenPunkt)
    {
        string k = "glut" + Simulation.Determinism.Roll(4);
        if (EffectFrames(k).Count == 0) return;
        _effects.Add(new Effect
        {
            Pos = bodenPunkt - new Vector2(0, 6),
            Kind = k,
            FrameTime = 1f / EffektTakteJeSekunde,
        });
        TruemmerAufschlaege++;
    }

    /// <summary>Ein Geschwindigkeitsanteil: gleichverteilt in [−s, s), und was
    /// darunter kleiner als 3 ist, wird auf ±3 gesetzt (@0x4AD5C9). Damit bleibt
    /// kein Teil auf der Stelle liegen.</summary>
    private static int Wurf(int s)
    {
        int v = Simulation.Determinism.Roll(2 * s) - s;
        if (v > -3 && v < 3) v = Simulation.Determinism.Roll(2) * 6 - 3;
        return v;
    }

    /// <summary>Wo ein Teil gerade ist: gerade Verbindung (Grund linear,
    /// @0x4ADCC6) plus Bogen. Zwischen zwei Takten wird gleitend gezeichnet.</summary>
    private Vector2 TruemmerOrt(in Truemmer t)
    {
        float rate = TruemmerUhrAlt ? TruemmerTakteJeSekunde : EffektTakteJeSekunde;
        float k = t.Zeit * rate;
        float f = t.N <= 0f ? 1f : Mathf.Clamp(k / t.N, 0f, 1f);
        return t.Von.Lerp(t.Nach, f) - new Vector2(0, Mathf.Max(0f, TruemmerBogen(t, k)));
    }

    private void TruemmerTakt(float dt)
    {
        float rate = TruemmerUhrAlt ? TruemmerTakteJeSekunde : EffektTakteJeSekunde;
        for (int i = _truemmer.Count - 1; i >= 0; i--)
        {
            var t = _truemmer[i];
            t.Zeit += dt;
            bool gelandet = false;
            Vector2 boden = t.Nach;
            if (TruemmerAufschlagAus)
            {
                // Stand feec479: feste Dauer, kein Bodentest
                gelandet = t.Zeit >= t.Dauer;
                if (!gelandet) t.MaxBogen = Mathf.Max(t.MaxBogen, TruemmerBogen(t, t.Zeit * rate));
            }
            else
            {
                // ⭐ je ganzem Takt die Pruefung des Originals (@0x4AE04A):
                // weiter, solange Z ueber dem Boden am Punkt und nicht angekommen.
                while (!gelandet && t.Takt + 1 <= t.Zeit * rate)
                {
                    t.Takt++;
                    float f = t.N <= 0f ? 1f : Mathf.Min(1f, t.Takt / t.N);
                    var p = t.FlachVon.Lerp(t.FlachNach, f);
                    float grund = Mathf.Lerp(t.GrundVon, t.GrundNach, f);
                    float bogen = TruemmerBogen(t, t.Takt);
                    t.MaxBogen = Mathf.Max(t.MaxBogen, bogen);
                    int c = Mathf.FloorToInt((p.X - _ox) / TileW), r = Mathf.FloorToInt((p.Y - _oy) / TileH);
                    int fx = Mathf.PosMod(Mathf.FloorToInt(p.X - _ox), TileW);
                    int fy = Mathf.PosMod(Mathf.FloorToInt(p.Y - _oy), TileH);
                    float bodenHoehe = HubOf(c, r, fx, fy);              // 0x4B5CE0
                    bool angekommen = t.Takt >= t.N;
                    if (grund + (int)bogen > bodenHoehe && !angekommen) continue;
                    gelandet = true;
                    boden = p - new Vector2(0, bodenHoehe);
                    if (TruemmerBogenAlt && angekommen) boden = t.Nach;
                }
            }
            if (gelandet)
            {
                if (TruemmerMitschreiben)
                    TruemmerGelandet.Add((t.Wurf, t.Brocken, t.Zeit, t.Strecke, t.MaxBogen));
                if (t.Brocken && !TruemmerAufschlagAus) Aufschlag(boden);   // @0x4AE0DE
                _truemmer.RemoveAt(i);
                continue;
            }

            // Der Schweif: jeder dritte Takt ein Rauchball, aber nur bei den
            // Brocken (@0x4AD9AC/@0x4ADD60).
            if (t.Schweif && TruemmerUhrAlt)
            {
                t.SchweifAb -= dt;
                if (t.SchweifAb <= 0f)
                {
                    t.SchweifAb = SchweifAbstand;
                    string r = "rauch" + Simulation.Determinism.Roll(3);
                    if (EffectFrames(r).Count > 0)
                        _effects.Add(new Effect
                        {
                            Pos = TruemmerOrt(t), Kind = r, FrameTime = 0.05f,
                        });
                }
            }
            else if (t.Schweif)
            {
                // ⭐ 04.10.2026: je EFFEKTTAKT gewuerfelt, `rand()%3 == 0`
                // (@0x4ADD60), und die Wolke laeuft auf derselben Uhr.
                while (t.SchweifAb + 1f <= t.Zeit * rate)
                {
                    t.SchweifAb += 1f;
                    if (Simulation.Determinism.Roll(3) != 0) continue;
                    string r = "rauch" + Simulation.Determinism.Roll(3);
                    if (EffectFrames(r).Count > 0)
                        _effects.Add(new Effect
                        {
                            Pos = TruemmerOrt(t), Kind = r, FrameTime = 1f / EffektTakteJeSekunde,
                        });
                }
            }
            _truemmer[i] = t;
        }
        SterbendTakt(dt);
    }

    /// <summary>Nur im Pruefstand: gelandete Teile mitschreiben.</summary>
    internal bool TruemmerMitschreiben;

    private void TruemmerZeichnen()
    {
        // Das Bild laeuft mit der Uhr, nicht mit dem Alter des Teils
        // (@0x4ADCF5: `(Uhr + Platz) % Bildzahl`).
        int uhr = Mathf.FloorToInt(_clock * (TruemmerUhrAlt ? 20f : EffektTakteJeSekunde));
        for (int i = 0; i < _truemmer.Count; i++)
        {
            var t = _truemmer[i];
            var bilder = EffectFrames(t.Folge);
            if (bilder.Count == 0) continue;
            var tex = bilder[((uhr + i) % bilder.Count + bilder.Count) % bilder.Count];
            DrawTexture(tex, TruemmerOrt(t) - tex.GetSize() / 2f);
        }
    }

    /// <summary>Die Meldezeile — geworfen, unterwegs, und ob die Bilder
    /// ueberhaupt da sind.</summary>
    public string TruemmerWatchLine()
    {
        int fehlend = 0;
        for (int k = 0; k < 6; k++) if (EffectFrames("splitter" + k).Count == 0) fehlend++;
        for (int k = 0; k < 10; k++) if (EffectFrames("brocken" + k).Count == 0) fehlend++;
        for (int k = 0; k < 3; k++) if (EffectFrames("rauch" + k).Count == 0) fehlend++;
        return $"truemmer: {TruemmerGeworfen} geworfen, {_truemmer.Count} unterwegs"
             + (fehlend > 0
                 ? $"   ⚠ {fehlend} von 19 Bildfolgen FEHLEN — "
                 + "--reexport-effects=<Quelle> schreibt sie"
                 : "   alle 19 Bildfolgen da");
    }
}
