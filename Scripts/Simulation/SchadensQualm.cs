namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐⭐ <b>DER QUALM EINES BESCHÄDIGTEN FAHRZEUGS</b> (24.08.2026).
///
/// <para>Gemeldet: »Beschädigte Einheiten fangen an wie zu Qualmen, das
/// passiert bei uns nicht«. Auf Nachfrage: »nur Fahrzeuge«, »der Qualm wird
/// mehr, umso mehr Schaden«, »ich würde sagen dass Einheiten weiter qualmen bis
/// sie repariert sind«.</para>
///
/// <para><b>Alle drei Aussagen stehen in dreissig Byte des Einheitentakts</b>
/// (@0x4071C0, im Rumpf ab 0x406D20):</para>
///
/// <code>
///   0x4071C0  al = byte[edi + 0x0A]      ; Gattung
///   0x4071C5  jne  raus                  ; ⭐ NUR Gattung 0 = FAHRZEUG
///   0x4071C7  cl = byte[edi + 0x08]      ; Trefferpunkte
///   0x4071CC  je   raus                  ; hp != 0
///   0x4071CE  al = byte[edi + 0x29]      ; Hoechstwert
///   0x4071D3  div  cl                    ; si = hp_max / hp   (ganzzahlig)
///   0x4071DA  cmp  ax, 2
///   0x4071E1  jle  raus                  ; ⭐ erst UNTER der halben Energie
///   0x407202  rand() % 1000
///   0x407212  idiv si                    ; ⭐ geteilt durch das Schadensverhaeltnis
///   0x407214  cmp  eax, 50
///   0x407217  jge  raus                  ;    -> je kaputter, desto haeufiger
///   0x40722F  Ablage(Spalte, Zeile, +4, +6, MODUS 2, 0, 0)
/// </code>
///
/// <para>Modus 2 der Teilchenablage (@0x4AD83A) legt eine Wolke der Folge
/// <c>rand()%3 + 240</c> an, und ihr Takt (@0x4ADE26) treibt sie mit dem
/// <b>WIND</b>: <c>Tafel[0x5040E0][Richtung] · Staerke</c>, waagerecht halbiert,
/// senkrecht geviertelt — dieselbe isometrische Entzerrung wie ueberall —, dazu
/// ein Zittern von <c>(rand()%10 − 5)/4</c> je Achse.</para>
///
/// <para><b>Die Haeufigkeit, ausgerechnet</b> (je Takt):</para>
/// <list type="bullet">
/// <item>hp = ½·max → si = 2 → <b>gar nicht</b> (die Schwelle ist &gt; 2)</item>
/// <item>hp = ⅓·max → si = 3 → 15 %</item>
/// <item>hp = ⅕·max → si = 5 → 25 %</item>
/// <item>hp = 1/10·max → si = 10 → 50 %</item>
/// </list>
///
/// <para>⚠⚠ <b>WARUM ICH DAS ZWEIMAL NICHT GEFUNDEN HABE.</b> Der ganze erste
/// Anlauf hat die <b>Absolutform</b> abgesucht (<c>byte[0x6E26D0 + …]</c>) und
/// dabei sechs Effekt-Einsprünge mit 69 Aufrufstellen ausgeschlossen. Hier steht
/// aber die <b>Kurzform</b> <c>byte[edi + 0x08]</c> — der Satzzeiger. Das ist
/// derselbe blinde Fleck, an dem am 23.08. schon der Minenleger gescheitert ist,
/// und ich hatte ihn in derselben Sitzung sogar benannt und dann nur den
/// ZEICHENbereich danach abgesucht. <b>Beide Formen, immer, überall</b> —
/// sonst halbiert sich das Ergebnis.</para>
///
/// <para>⚠ Dazu ein zweiter eigener Fehler auf dem Weg: der erste Abtast lief
/// auf Feld <b>+0x06</b>, weil ich »KOLIK« für die Trefferpunkte hielt. Die
/// stehen in <b>+0x08</b> (Hoechstwert +0x29) — der halbe Abtast war fuer die
/// Katz. Der Beleg stand die ganze Zeit in unserem eigenen Ausleser:
/// <c>Hp = HexByte(raw, 0x08)</c>.</para>
///
/// <para>⚠ <b>UNSER bleibt eine Zahl:</b> die Taktrate. Das Original wuerfelt
/// je Einheitentakt; wie viele davon eine Sekunde hat, steht nirgends. Wir
/// nehmen <b>25</b> — dieselbe Rate, die dieses Projekt schon belegt hat
/// (<c>SkirmishAi.AiSweepSeconds</c>: 50 Takte je KI-Runde, gemessen als zwei
/// Sekunden). Eine bestehende Eichung, keine neue.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>⚠ UNSERE EICHUNG — siehe Klassenkopf. 50 Takte je KI-Runde bei
    /// zwei Sekunden Rundendauer.</summary>
    private const float QualmTakteJeSekunde = 25f;

    /// <summary>Die Windtafel 0x5040E0, acht Richtungen. Gelesen.</summary>
    private static readonly Vector2I[] WindTafel =
    {
        new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
        new(0, -1), new(1, -1), new(1, 0), new(1, 1),
    };

    /// <summary>⚠ UNSERE EICHUNG: wie stark die Wolke wirklich abtreibt. Bei 1
    /// (dem rohen Produkt aus gelesenem Taktversatz und unserer Taktrate) rutscht
    /// sie binnen ihrer Lebensdauer fast eine Kachel weg und sitzt sichtbar
    /// neben dem Fahrzeug. Bei 0,25 bleibt sie darauf und zieht trotzdem
    /// erkennbar in die Windrichtung.</summary>
    private const float QualmDriftBremse = 0.25f;

    private float _qualmRest;

    /// <summary>Wie viele Wolken seit dem Start aufgestiegen sind, und wie viele
    /// Fahrzeuge gerade unter der Schwelle stehen. ⚠ Ohne die zweite Zahl sieht
    /// »es qualmt nicht« genauso aus wie »nichts ist beschaedigt genug«
    /// (Arbeitsweise 30 und 33).</summary>
    public int QualmWolken, QualmKandidaten;

    /// <summary>Der Einheitentakt von @0x4071C0, ueber alle Fahrzeuge.</summary>
    private void SchadensQualmTakt(float dt)
    {
        _qualmRest -= dt;
        if (_qualmRest > 0f) return;
        _qualmRest += 1f / QualmTakteJeSekunde;

        QualmKandidaten = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding) continue;
            if (e.GameUnitType != 0) continue;          // nur Gattung 0
            if (e.Hp <= 0 || e.HpMax <= 0) continue;

            // si = hp_max / hp, GANZZAHLIG wie das `div cl` des Originals.
            int si = e.HpMax / e.Hp;
            if (si <= 2) continue;                      // erst unter der Haelfte
            QualmKandidaten++;

            if (Simulation.Determinism.Roll(1000) / si >= 50) continue;

            string folge = "rauch" + Simulation.Determinism.Roll(3);
            if (EffectFrames(folge).Count == 0) continue;

            // Modus 2: die Wolke treibt mit dem Wind. Waagerecht halb, senkrecht
            // ein Viertel — die isometrische Entzerrung des Originals.
            // ⚠⚠ ZWEITE BERICHTIGUNG DERSELBEN STELLE. Hier stand der
            // Taktversatz mal QualmTakteJeSekunde — und damit trieb die Wolke
            // mit rund 37 Bildpunkten je Sekunde davon. Bei elf Bildern à 0,06 s
            // rutscht sie in ihrer Lebensdauer fast eine ganze Kachel weg;
            // gemeldet wurde sie darum WIEDER »am Eck des Fahrzeugs«, obwohl
            // --auswahl-check den ENTSTEHUNGSPUNKT mit |dx| 1,6 / |dy| 0,3
            // entlastet. Sie entsteht mittig und faehrt sofort weg.
            //
            // ⚠ Der Fehler war, ZWEI Unsicherheiten zu multiplizieren: der
            // Versatz je Takt ist gelesen (Windtafel · Staerke, halb/viertel),
            // die Taktzahl je Sekunde ist UNSERE Eichung. Das Produkt erbt
            // beide. Der Bremsfaktor steht darum als eigene, benannte Zahl da
            // und nicht versteckt in einer Multiplikation.
            var w = WindDir >= 0 ? WindTafel[WindDir & 7] : Vector2I.Zero;
            var drift = new Vector2(w.X * WindStrength / 2f, w.Y * WindStrength / 4f)
                      * QualmTakteJeSekunde * QualmDriftBremse;

            // ⚠⚠ EINE BEWUSSTE ABWEICHUNG, und sie ist seine Entscheidung.
            //
            // Das Original uebergibt der Ablage `byte[edi+0x00]` und
            // `byte[edi+0x01]` (@0x407226/0x40722C), also die ANKERZELLE der
            // Einheit samt Feinlage. Der Anker ist die obere linke Ecke des
            // Fussabdrucks — bei einem mehrfeldrigen Fahrzeug steigt der Qualm
            // im Original also an dessen ECKE auf, nicht aus seiner Mitte.
            // Genau das hat er gesehen und gemeldet: »wie am Eck des
            // Fahrzeuges, nicht mittig«.
            //
            // Auf Nachfrage: er will ihn MITTIG.
            // ⚠ Das ist eine Abweichung vom Gelesenen und steht hier, damit
            // sie nicht spaeter als »Fehler« wieder wegrepariert wird.
            //
            // ⚠⚠ ZWEITER ANLAUF. Erst stand hier BodyCenter — und die Meldung
            // darauf war »jetzt ist der Qualm wie hinter dem Fahrzeug am Po,
            // nach wie vor nicht mittig«. Zu Recht: BodyCenter ist die Mitte
            // des FUSSABDRUCKS, also ein BODENPUNKT, und der Kommentar dort
            // sagt es selbst — »Mitte hier ist die Trefferflaeche und der
            // Standort, das Bild haengt an PictureAnchor«. Zwischen Pos und der
            // sichtbaren Bildflaeche liegen laut --stempel-check senkrecht im
            // Mittel 19,5 Bildpunkte; genau die sass der Qualm zu tief.
            //
            // ⚠⚠ DRITTER ANLAUF, und diesmal hat er gesagt WOHIN: »der muss da
            // sein, wo der Geschuetzturm ist«.
            //
            // Damit ist auch klar, warum die ersten beiden Punkte danebenlagen,
            // obwohl --auswahl-check den zweiten mit |dx| 1,6 / |dy| 0,3
            // entlastet: der Prueftstand misst die Mitte der FARBFLAECHE des
            // ganzen Fahrzeugs — Rumpf samt Turm. Der Turm sitzt darauf, also
            // hoeher und je nach Blickrichtung versetzt. »Mittig auf dem Bild«
            // und »am Turm« sind zwei verschiedene Punkte, und er meinte immer
            // den zweiten.
            //
            // ⭐ Den Punkt gibt es laengst: ShotOrigin ist die Mitte des
            // Turmbildes (die Muendung ist erst `+ dir · MuzzleReach` davon
            // entfernt) und wurde am 24.08. eigens gegen den Zeichner geeicht,
            // als die Rakete neben dem Schiff losflog. Ein Fahrzeug ohne Turm
            // faellt auf die Bildmitte zurueck.
            _effects.Add(new Effect
            {
                Pos = e.Weapon > 0 ? ShotOrigin(e) : AuswahlMitte(e),
                Kind = folge,
                FrameTime = 0.06f,
                Drift = drift,
            });
            QualmWolken++;
        }
    }

    /// <summary>Die Meldezeile.</summary>
    public string QualmWatchLine()
    {
        int fehlend = 0;
        for (int k = 0; k < 3; k++) if (EffectFrames("rauch" + k).Count == 0) fehlend++;
        return $"schadensqualm: {QualmWolken} Wolken, gerade {QualmKandidaten} Fahrzeug(e) "
             + $"unter der halben Energie, Wind {WindDir}/{WindStrength}"
             + (fehlend > 0 ? $"   ⚠ {fehlend} von 3 Rauchfolgen FEHLEN" : "")
             + (QualmKandidaten == 0
                 ? "   ⚠ ohne beschaedigtes Fahrzeug sagt die 0 nichts"
                 : "");
    }
}
