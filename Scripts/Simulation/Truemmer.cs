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
/// <para>⚠⚠ <b>WAS UNSER BLEIBT — genau zwei Zahlen, und beide sind
/// Umrechnungen, keine Mechanik:</b></para>
/// <list type="number">
/// <item><b>Takt → Sekunde.</b> Das Tempo steht in Bildpunkten je TAKT; wie
/// lang ein Takt ist, steht nirgends. Wir nehmen
/// <see cref="PxPerProjectileSpeed"/> — dieselbe Zahl, mit der schon die
/// Geschosse rechnen. EIN Unbekanntes, EINE Konstante, statt zwei.</item>
/// <item><b>Die Bogenhoehe.</b> Das Original rechnet Schwerkraft und
/// Anfangssteigen aus <c>(Tempo &gt;&gt; 3) + 2</c> und zwei Gleitkommazahlen
/// (@0x4ADA6C..0x4ADAAC); die Kette habe ich NICHT zu Ende gelesen.
/// <see cref="BogenFaktor"/> setzt darum den Scheitel auf ein Vielfaches
/// dieser gelesenen Zahl. Die FORM (Wurfparabel, Landung genau im Ziel) ist
/// gelesen, nur ihre Hoehe ist geeicht.</item>
/// </list>
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
    }

    private readonly List<Truemmer> _truemmer = new();

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
    /// Taktbegriff wie das Tempo.</summary>
    private const float SchweifAbstand = 0.06f;

    /// <summary>Wie viele Teile seit dem Start geworfen wurden. ⚠ Ohne die Zahl
    /// sieht »ich sehe keine Truemmer« genauso aus wie »es wurden keine
    /// geworfen« (Arbeitsweise 30).</summary>
    public int TruemmerGeworfen;

    /// <summary>Die beiden Schleifen von @0x40B61D und @0x40B662.</summary>
    private void TruemmerWerfen(Entity opfer)
    {
        // Gattung 0 = Fahrzeug. Alles andere springt an den Schleifen vorbei.
        if (opfer.GameUnitType != 0 || opfer.Infantry >= 0 || opfer.IsBuilding) return;

        int n = Simulation.Determinism.Roll(10) + 10;
        for (int k = 0; k < n; k++) EinTeil(opfer, sorte: 0, streuung: 3);

        n = Simulation.Determinism.Roll(6) + 5;
        for (int k = 0; k < n; k++) EinTeil(opfer, sorte: 1, streuung: 5);
    }

    private void EinTeil(Entity opfer, int sorte, int streuung)
    {
        // Die Bildfolge, und mit ihr Schweif und Tempo (@0x4AD9AC).
        int seq = sorte == 0 ? 19 + Simulation.Determinism.Roll(6)
                             : 29 + Simulation.Determinism.Roll(10);
        string folge = sorte == 0 ? "splitter" + (seq - 19) : "brocken" + (seq - 29);
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

        var von = Ende(opfer.Col, opfer.Row);
        var nach = Ende(zc, zr);

        // n = Strecke / Tempo, und die SENKRECHTE Strecke zaehlt doppelt —
        // @0x4ADC0A `lea edi,[eax*2]`, die isometrische Entzerrung.
        float dx = nach.X - von.X, dy = (nach.Y - von.Y) * 2f;
        float takte = Mathf.Sqrt(dx * dx + dy * dy) / tempo;
        if (takte < 1f) takte = 1f;

        _truemmer.Add(new Truemmer
        {
            Von = von,
            Nach = nach,
            Zeit = 0f,
            Dauer = takte / TruemmerTakteJeSekunde,
            Scheitel = BogenFaktor * ((tempo >> 3) + 2),
            Folge = folge,
            Schweif = schweif,
            SchweifAb = 0f,
        });
        TruemmerGeworfen++;

        // Feinlage im Feld (die Kachel ist 40x20 — genau die beiden Modulo des
        // Originals) und die Hoehe des Gelaendes darueber.
        Vector2 Ende(int c, int r)
            => CellCenter(c, r)
             + new Vector2(Simulation.Determinism.Roll(40) - 20,
                           Simulation.Determinism.Roll(20) - 10)
             - new Vector2(0, ElevOf(c, r) * TruemmerHoehe);
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

    /// <summary>Wo ein Teil gerade ist: gerade Verbindung plus Wurfparabel.</summary>
    private static Vector2 TruemmerOrt(in Truemmer t)
    {
        float f = t.Dauer <= 0f ? 1f : Mathf.Clamp(t.Zeit / t.Dauer, 0f, 1f);
        // vz −= g je Takt, Hoehe += vz, Landung genau im Ziel: das ist die
        // Parabel 4·h·f·(1−f). Die FORM ist gelesen, die Hoehe geeicht.
        return t.Von.Lerp(t.Nach, f) - new Vector2(0, 4f * t.Scheitel * f * (1f - f));
    }

    private void TruemmerTakt(float dt)
    {
        for (int i = _truemmer.Count - 1; i >= 0; i--)
        {
            var t = _truemmer[i];
            t.Zeit += dt;
            if (t.Zeit >= t.Dauer) { _truemmer.RemoveAt(i); continue; }

            // Der Schweif: jeder dritte Takt ein Rauchball, aber nur bei den
            // Brocken (@0x4AD9AC/@0x4ADD60).
            if (t.Schweif)
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
            _truemmer[i] = t;
        }
    }

    private void TruemmerZeichnen()
    {
        // Das Bild laeuft mit der Uhr, nicht mit dem Alter des Teils
        // (@0x4ADCF5: `(Uhr + Platz) % Bildzahl`).
        int uhr = Mathf.FloorToInt(_clock * 20f);
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
