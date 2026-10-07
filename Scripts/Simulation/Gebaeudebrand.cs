using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐⭐ <b>EIN BESCHÄDIGTES GEBÄUDE BRENNT</b> — gelesen und gebaut am
/// 07.09.2026.
///
/// <para><b>Seine Meldung:</b> »ich sehe auch noch kein rauch während des
/// beschusses an den kraftwerken«. ⚠ Das ist ein ANDERER Mechanismus als das
/// Nachbrennen nach dem Tod, das am selben Tag als »Rauch, kein Feuer«
/// zurückgestellt wurde: dieser hier hängt an der SCHADENSSTUFE und läuft,
/// während das Gebäude noch steht.</para>
///
/// <para><b>Die Kette, ganz gelesen:</b></para>
/// <code>
///   Zasah  @0x40D389   ruft den Stufenrechner mit Flag 1 (push 1)
///   0x4CBBF0           Stufe = (HpMax − Hp) / (HpMax / letzteStufe)
///          @0x4CBC0C     eax = word[+0x16]  HpMax
///          @0x4CBC1D     edx = word[+0x06]  Hp
///          @0x4CBC34     bl  = byte[0xBB41A0 + 10·Art]  letzte Stufe
///          @0x4CBC4C   ⚠ cmp/jne — ist die Stufe ANDERS (nicht: höher),
///          @0x4CBC50     dann +0x0A := Stufe und 0x4C95E0(Platz, Flag)
///   0x4C95E0 @0x4C9860 nur wenn `s == Stufe−1` und Flag ≠ 0:
///                      0x4AE4C0(X+dx, Y+dy, Platz+60000) je Zelle
///                      — ein Brand auf JEDER Zelle des neuen Musters
///   0x4AE4C0 @0x4AE593 Lebensdauer = (HpMax−Hp)·200/HpMax + rand%30, dann
///                      &lt;100 → v/4+11 · &lt;150 → v/3+11 · &lt;180 → v/2+11 · sonst v+11
///   0x4AE760           der Ticker, je Takt über alle Brände
/// </code>
///
/// <para><b>Zwei Tore, die das Ganze selten machen</b> und darum hierher
/// gehören: <c>0x4C960D</c> setzt Flag := 0 bei <b>Stufe 1</b> (das intakte
/// Gebäude), und <c>0x4C9742</c> tut dasselbe beim <b>Tod</b> (Stufe ==
/// letzte). Es brennt also nur, was beschädigt und noch am Leben ist — und nur
/// im Augenblick des Stufenwechsels, nicht bei jedem Treffer.</para>
///
/// <para><b>Der Ticker</b> (<c>0x4AE760</c>, Rufer <c>@0x4165C1</c> im
/// Haupttakt, ohne Bedingung), je Brand und Takt:</para>
/// <code>
///   Alter &lt; 11   rand%15 == 1     ANIM 510..518 + Splitter 200..204  @0x4AE843
///                        == 2,3    ANIM 310..315 + Splitter 29..38    @0x4AE931
///                        0,4,5,6   ANIM 230..233 + Splitter 19..24    @0x4AE7B5
///                        &gt;= 7      nichts
///   Alter &gt;= 11  rand%50 == 5      ANIM 210..212                     @0x4AEA08
///                       &lt;= 35      NICHTS (jle 0x4AEA63)
///                       sonst      ANIM 240..242, Höhe +50 (Rauch)   @0x4AEA01
///   Altern @0x4AEA63: Alter &gt; Lebensdauer ⇒ frei
/// </code>
///
/// <para>⚠ <b>Drei benannte Näherungen</b>, damit niemand mehr hineinliest als
/// dasteht:</para>
/// <list type="bullet">
///   <item>Das Original zündet auf den Zellen des <b>Schadensmusters</b>, wir
///   auf denen des <b>Fußabdrucks</b> — dieselbe Näherung wie beim Gebäudetod
///   (<see cref="MapEntityLayer.GebaeudeSprengen"/>). Auf einem vollen Rechteck
///   ist es dasselbe.</item>
///   <item>Der Rauch ist im Original ein <b>Geschossobjekt</b> mit Höhe +50, das
///   aufsteigt (<c>0x4AD8B0</c>); wir setzen einen stehenden Effekt etwas höher.
///   Unsere Effektliste kennt keine fliegenden Teilchen mit eigener Höhe.</item>
///   <item>⚠⚠ <b>FEHLLESUNG, berichtigt am 07.10.2026 (bug-482,
///   <c>berichte/aufprall-klang-wrack-fable.md</c> §3/§4):</b> hier stand bis
///   dahin »der Klang beim Zünden ist 399 und im Vorrat leer — er bleibt
///   stumm«. 399 kommt NIE beim Vorrat an: die Klangroutine <c>0x4047E0</c>
///   setzt @0x4048F8 (F 0x4048D8, <c>cmp si,0x18F / idiv 6 / lea
///   esi,[edx+0x190]</c>) <b>399 in 400 + rand%6</b> um, BEVOR sie den Platz
///   lädt. Der Zünder-Schwanz @0x4AE664 (F 0x4ADF94) ruft 399 Modus 2 an der
///   Zelle ohne Weiche — JEDER Brand zündet also hörbar (400..405), und der
///   Ticker klingt bei Alter 1..10 mit 20 % je Zelle und Takt (r == 1: 2/3
///   399→400..405, 1/3 410+rand%6 @0x4AE8C8; r == 2,3: 399 @0x4AE970).
///   Die Würfelfolge des Originals: rand%40, rand%20 (Feinlage des Bilds),
///   [rand%3], rand%6 (in der Klangroutine), dann rand%9 bzw. rand%6 fürs
///   Bild. Gegenschalter <c>--brandklang-aus</c> (= der stumme Stand bis 0.6.7,
///   auch ohne die Feinlage-Würfe).</item>
/// </list>
///
/// <para>Gegenschalter <c>--gebaeudebrand-aus</c>, Prüfstand
/// <c>--gebaeudebrand-probe</c>; der Klang wird in <c>--absturz-check</c>
/// (Simulation/AbsturzCheck.cs) mit dem Horcher gemessen.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Ein brennender Fleck auf einer Gebäudezelle — die Zeile der
    /// Tafel <c>0xA62A50</c> (400 × 6: x, y+2, Höhe, Alter, Lebensdauer).</summary>
    private struct Gebaeudebrand
    {
        public int Col, Row;
        public int Alter;              // +0x04, 0 = frei
        public int Lebensdauer;        // +0x05
    }

    /// <summary>⚠ 400 Plätze wie im Original (<c>cmp dx, 0x190</c> @0x4AE4DE).
    /// Ist keiner frei, überschreibt das Original einen zufälligen
    /// (<c>rand%400</c> @0x4AE4EB) — es scheitert nie.</summary>
    private const int BrandPlaetze = 400;

    /// <summary>Wie hoch über der Zelle der Rauch anfängt — die gelesenen
    /// <c>+50</c> aus <c>@0x4AEA24</c> (<c>add al, 0x32</c>).</summary>
    private const float RauchHoehe = 50f;

    /// <summary>Und wie schnell er steigt, in Punkten je Sekunde. ⚠ GESETZT,
    /// nicht gelesen: im Original ist der Rauch ein Geschossobjekt mit eigener
    /// Flugbahn (<c>0x4AD8B0</c>), und die haben wir nicht.</summary>
    private const float RauchSteigen = 22f;

    private readonly List<Gebaeudebrand> _gbrand = new();

    /// <summary><c>--gebaeudebrand-aus</c> — der Stand vor dem 07.09.2026: ein
    /// beschädigtes Gebäude qualmt nicht.</summary>
    public static bool GebaeudebrandAus;

    /// <summary>Wie oft ein Stufenwechsel Brände gezündet hat, und wie viele
    /// Brände daraus wurden. ⚠ Ohne diese zwei Zahlen ist »es qualmt nicht« nicht
    /// von »es hat nie gezündet« zu unterscheiden.</summary>
    public int BrandZuendungen, BrandStellen;

    /// <summary>Und wie viele Bilder der Ticker geworfen hat, nach Sorten
    /// getrennt — die Rauchzahl ist die, nach der er gefragt hat.</summary>
    public int BrandRauch, BrandFlammen, BrandExplosionen;

    /// <summary>
    /// Nach einem Treffer: hat sich die Schadensstufe geändert? Dann brennt es.
    ///
    /// <para>⚠ Der Vergleich ist <c>!=</c>, nicht <c>&gt;</c> (@0x4CBC4E
    /// <c>je</c>) — im Original zündet auch eine SINKENDE Stufe, also eine
    /// Reparatur. Das ist keine Nachlässigkeit von uns, sondern steht so da;
    /// wer es ändert, weicht ab.</para>
    /// </summary>
    private void GebaeudeStufeNachziehen(Entity b, bool zuenden = true)
    {
        if (GebaeudebrandAus || !b.IsBuilding || b.IsProp || b.Dead) return;
        if (b.HpMax <= 0) return;

        int stufe = DamageFrame(b);
        if (stufe == b.Schadensstufe) return;
        int vorher = b.Schadensstufe;
        b.Schadensstufe = stufe;
        // ⭐ 15.09.2026 — 0x4CBBF0(Platz, 0): mit Flag 0 wird nur die Stufe geschrieben,
        // nichts gezuendet (Reparaturbehandler und Reparaturtakt). Reparatur.cs.
        if (!zuenden) return;

        // Die zwei Tore: das intakte Gebaeude (Stufe 1, @0x4C960D) und der Tod
        // (@0x4C9742) setzen Flag := 0 und zuenden nichts. Der Tod hat seinen
        // eigenen Weg, GebaeudeSprengen.
        if (stufe <= 1) return;
        int letzte = Patterns == null ? 0 : Patterns.GetBuildingType(b.BType).PatternCount - 1;
        if (letzte >= 1 && stufe >= letzte) return;
        if (vorher <= 0) return;              // beim ersten Setzen nach dem Laden

        int gezuendet = 0;
        foreach (var (dc, dr) in Schadenszellen(b, stufe))
            if (BrandZuenden(b.Col + dc, b.Row + dr, b)) gezuendet++;
        if (gezuendet > 0) { BrandZuendungen++; BrandStellen += gezuendet; }
    }

    /// <summary>
    /// <b>DIE ZELLEN DES NEUEN SCHADENSMUSTERS</b> — und das ist der ganze
    /// Unterschied zwischen »ein Gebäude qualmt« und »ein Gebäude explodiert«.
    ///
    /// <para>⚠⚠ <b>Seine Meldung vom 07.09.2026:</b> »dein rauch (das sind
    /// eigentlich massive explosionen die kommen wenn das gebäude drauf geht)
    /// werden jetzt gezeigt anstatt rauch«. Er hatte recht, und der Fehler war
    /// nicht der Effekt, sondern die ANZAHL: der erste Bau zündete auf jeder
    /// Zelle des FUSSABDRUCKS, also dreißig Bränden mit je einem Feuerball.</para>
    ///
    /// <para><b>Das Original zündet auf den Zellen des MUSTERS</b>
    /// (<c>@0x4C9860 sub eax,ecx / cmp eax,1 / jne</c> — nur das zuletzt
    /// gestempelte Muster, und in der Zeichenschleife nur Zellen mit
    /// <c>Musterwort != 0</c>). Und ein Schadensmuster ist keine Kopie des
    /// Gebäudes, sondern ein FLECK, der über das Bild gestempelt wird.</para>
    ///
    /// <para>⭐ <b>GEMESSEN</b> an Art 13 in Tileset 04 (Kraftwerk, 18 Muster):
    /// das intakte Bild hat <b>26</b> belegte Zellen, die sechzehn
    /// Schadensmuster dazwischen haben <b>1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1,
    /// 2, 4, 1, 6</b> — im Mittel 1,4. Die Ruine (Muster 17) hat wieder 26.
    /// Ein Stufenwechsel wirft im Original also EINEN Brand, keine dreißig.
    /// Genau darum sieht man dort Qualm und hier eine Zerstörung.</para>
    ///
    /// <para>⚠ Rückfall: kennt der Baum die Muster nicht (kein
    /// <see cref="Patterns"/>), wird gar nichts gezündet — lieber kein Brand
    /// als wieder dreißig.</para>
    /// </summary>
    private IEnumerable<(int, int)> Schadenszellen(Entity b, int stufe)
    {
        if (Patterns == null) yield break;
        var bt = Patterns.GetBuildingType(b.BType);
        if (bt.PatternCount < 2) yield break;
        // Die Zeichenschleife stempelt s = 0..Stufe-1; gezuendet wird auf dem
        // ZULETZT gestempelten, also FirstPattern + Stufe - 1.
        int muster = bt.FirstPattern + Mathf.Clamp(stufe - 1, 0, bt.PatternCount - 1);
        for (int dc = 0; dc < Import.CwpFile.PatternWidth; dc++)
            for (int dr = 0; dr < Import.CwpFile.PatternHeight; dr++)
                if (Patterns.PatternTile(muster, dc, dr) != 0)
                    yield return (dc, dr);
    }

    /// <summary>
    /// <c>0x4AE4C0</c> — einen Brand entzünden.
    ///
    /// <para>Die Lebensdauer ist die des GEBÄUDEZWEIGS (<c>@0x4AE593</c>, Wert
    /// im Band 60000..60300): <b>je kaputter, desto länger</b>. Beim Zünden
    /// fällt eine Explosion (510..518) und ein Splitter (200..204, Streuung
    /// 12), dazu der Klang 399 → 400+rand%6 (bug-482, siehe Klassenkopf).</para>
    /// </summary>
    private bool BrandZuenden(int c, int r, Entity b)
    {
        int v = b.HpMax > 0 ? (b.HpMax - Mathf.Max(0, b.Hp)) * 200 / b.HpMax : 0;
        v += Simulation.Determinism.Roll(30);
        int leben = v < 100 ? v / 4 + 11
                  : v < 150 ? v / 3 + 11
                  : v < 180 ? v / 2 + 11
                            : v + 11;
        return BrandZuenden(c, r, leben);
    }

    /// <summary>bug-475: die Lebensdauer des Zweigs <b>Art 0</b> von <c>0x4AE4C0</c>
    /// (<c>@0x4AE549 mov byte [+5], 0x32</c>) — der Absturz zündet seine vier
    /// Brandherde mit <c>(x, y, 0)</c>.</summary>
    public const int BrandLebenArt0 = 50;

    /// <summary><c>--brandklang-aus</c> — Stand bis 0.6.7: Zünder und Ticker
    /// stumm, Bilder ohne Feinlage. Nullmodell zu bug-482 (B/C).</summary>
    public static bool BrandklangAus;

    /// <summary>Wie oft Zünder (399 → 400..405) und Ticker (400..405 / 410..415)
    /// einen Klang angestoßen haben — für Prüfstände.</summary>
    public int BrandZuendKlaenge, BrandTickerKlaenge, BrandTickerKlaenge410;

    /// <summary>bug-482: der Platzhalter <b>399</b> des Originals — die
    /// Klangroutine macht @0x4048F8 (F 0x4048D8) daraus <c>400 + rand%6</c>.
    /// Wir würfeln an derselben Stelle der Folge (der Ruf steht VOR den
    /// Bildwürfen rand%9 / rand%6).</summary>
    private void BrandKlang399(int c, int r)
        => Audio.GameSounds.PlayAt(400 + Simulation.Determinism.Roll(6), c, r);

    /// <summary>bug-482 (E): die Feinlage der Brandbilder — <c>rand%40</c>
    /// Bildpunkte nach rechts ab der linken Zellkante, <c>rand%20</c> auf die
    /// Höhe (Höhe·15 + rand%20 @0x4AE66C, also nach oben). ⚠ UNSERE Näherung
    /// für die Umrechnung der Höhe (1 Einheit = 1/20 Zelle senkrecht); der
    /// Effektleger 0x4357F0 ist ungelesen.</summary>
    private Vector2 BrandFeinlage(int c, int r)
    {
        int fx = Simulation.Determinism.Roll(40), fy = Simulation.Determinism.Roll(20);   // @0x4AE625
        return CellCenter(c, r) + new Vector2((fx - 20) * TileW / 40f, -6f - fy * TileH / 20f);
    }

    /// <summary><c>0x4AE4C0</c> mit fertiger Lebensdauer — der gemeinsame Schwanz
    /// ab <c>0x4AE625</c>: Eintrag, Feinlage rand%40/rand%20, <b>Klang 399 →
    /// 400+rand%6 an der Zelle (@0x4AE664, F 0x4ADF94)</b>, Explosion 510..518
    /// (@0x4AE679), Splitter 200..204 (@0x4AE6C9).</summary>
    private bool BrandZuenden(int c, int r, int leben)
    {
        if (_gbrand.Count >= BrandPlaetze) return false;   // ⚠ wir ueberschreiben nicht

        _gbrand.Add(new Gebaeudebrand { Col = c, Row = r, Alter = 1, Lebensdauer = leben });

        Vector2 pos;
        if (BrandklangAus) pos = CellCenter(c, r) - new Vector2(0, 6);
        else
        {
            pos = BrandFeinlage(c, r);                           // rand%40, rand%20 @0x4AE625
            BrandKlang399(c, r);                                 // rand%6  @0x4AE664 -> 0x4048F8
            BrandZuendKlaenge++;
        }
        _effects.Add(new Effect
        {
            Pos = pos,
            Kind = "sprengung" + Simulation.Determinism.Roll(9),   // @0x4AE679
            FrameTime = 0.04f,
        });
        EinTeil(new Entity { Col = c, Row = r }, sorte: 2, streuung: 12);  // @0x4AE6C9
        BrandExplosionen++;
        return true;
    }

    /// <summary>
    /// <c>0x4AE760</c> — der Brandticker, je Takt über alle Brände.
    ///
    /// <para>⚠ Er läuft im ORIGINALTAKT (50 Hz), nicht im Bildtakt: die
    /// Würfelbedingungen (<c>rand%15</c>, <c>rand%50</c>) und die Lebensdauern
    /// sind in Takten gezählt, und in einem anderen Takt gelesen ergäben sie
    /// eine andere Menge Rauch.</para>
    /// </summary>
    private void GebaeudebrandTakt()
    {
        if (GebaeudebrandAus || _gbrand.Count == 0) return;

        for (int i = _gbrand.Count - 1; i >= 0; i--)
        {
            var f = _gbrand[i];
            if (f.Alter == 0) { _gbrand.RemoveAt(i); continue; }
            var pos = CellCenter(f.Col, f.Row) - new Vector2(0, 6);

            if (f.Alter < 11)                                   // @0x4AE786
            {
                int w = Simulation.Determinism.Roll(15);
                if (w == 1)                                       // @0x4AE843
                {
                    // bug-482 (C): rand%40, rand%20, dann rand%3 != 0 -> 399
                    // (-> 400+rand%6) | == 0 -> 410+rand%6, beide Modus 2 an der
                    // Zelle (@0x4AE8C8); erst danach das Bild 510+rand%9.
                    if (!BrandklangAus)
                    {
                        pos = BrandFeinlage(f.Col, f.Row);
                        if (Simulation.Determinism.Roll(3) != 0) BrandKlang399(f.Col, f.Row);
                        else { Audio.GameSounds.PlayAt(410 + Simulation.Determinism.Roll(6), f.Col, f.Row); BrandTickerKlaenge410++; }
                        BrandTickerKlaenge++;
                    }
                    _effects.Add(new Effect { Pos = pos, FrameTime = 0.04f,
                                              Kind = "sprengung" + Simulation.Determinism.Roll(9) });
                    EinTeil(new Entity { Col = f.Col, Row = f.Row }, sorte: 2, streuung: 12);
                    BrandExplosionen++;
                }
                else if (w == 2 || w == 3)                        // @0x4AE931
                {
                    // bug-482 (C): rand%40, rand%20, Klang 399 (@0x4AE970), dann
                    // die Flamme 310+rand%6.
                    if (!BrandklangAus)
                    {
                        pos = BrandFeinlage(f.Col, f.Row);
                        BrandKlang399(f.Col, f.Row);
                        BrandTickerKlaenge++;
                    }
                    _effects.Add(new Effect { Pos = pos, FrameTime = 0.05f,
                                              Kind = "flamme" + Simulation.Determinism.Roll(6) });
                    BrandFlammen++;
                }
                else if (w is 0 or 4 or 5 or 6)
                {
                    _effects.Add(new Effect { Pos = pos, FrameTime = 0.05f,
                                              Kind = "glut" + Simulation.Determinism.Roll(4) });
                    BrandFlammen++;
                }
            }
            else                                                 // @0x4AE9D9
            {
                int w = Simulation.Determinism.Roll(50);
                if (w == 5)
                {
                    _effects.Add(new Effect { Pos = pos - new Vector2(0, 12), FrameTime = 0.06f,
                                              Kind = "feuerstoss" + Simulation.Determinism.Roll(3) });
                    BrandFlammen++;
                }
                else if (w > 35)
                {
                    // ⭐ 07.09.2026, auf seine Meldung »man sieht kein rauch«:
                    // im Original ist das ein GESCHOSSOBJEKT, das mit Hoehe +50
                    // angelegt wird (@0x4AEA24 `add al, 0x32`) und aufsteigt —
                    // kein stehendes Woelkchen. Das ist der Grund, warum man es
                    // dort sieht und hier nicht: das Bild ist 27x27 Punkte, die
                    // Explosion daneben 80x71.
                    //
                    // ⚠ UNSERE NAEHERUNG bleibt: wir haben keine fliegenden
                    // Teilchen mit eigener Hoehe, also nehmen wir den Startpunkt
                    // (die gelesenen +50) und die Drift, die unsere Effektliste
                    // schon kann. Die Steiggeschwindigkeit ist GESETZT, nicht
                    // gelesen.
                    _effects.Add(new Effect
                    {
                        Pos = pos - new Vector2(0, RauchHoehe),
                        FrameTime = 0.09f,
                        Kind = "rauch" + Simulation.Determinism.Roll(3),
                        Drift = new Vector2(0, -RauchSteigen),
                    });
                    BrandRauch++;
                }
            }

            // Altern (@0x4AEA63). Die Sonderregel fuer Lebensdauer 0xFF trifft
            // uns nicht: die hat nur der TOTE Bau (Art genullt), und der laeuft
            // hier nicht mit.
            f.Alter++;
            if (f.Alter > f.Lebensdauer) _gbrand.RemoveAt(i);
            else _gbrand[i] = f;
        }
    }

    /// <summary>
    /// <c>--gebaeudebrand-probe</c> — <b>qualmt ein beschädigtes Gebäude, und
    /// hört es wieder auf?</b>
    ///
    /// <para>⚠ Vier Fragen, nicht eine: zündet ein Stufenwechsel überhaupt,
    /// zündet er auf JEDER Zelle, kommt danach RAUCH (das war seine Meldung),
    /// und erlischt der Brand wieder. Ein Lauf, der nur die Zündung zählt,
    /// hätte den stummen Ticker nicht bemerkt.</para>
    /// </summary>
    public string GebaeudebrandProbe()
    {
        var sb = new System.Text.StringBuilder("gebaeudebrand-probe\n");
        int gi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var x = _entities[i];
            if (x.IsBuilding && !x.IsProp && !x.Dead && x.BType == 13) { gi = i; break; }
        }
        if (gi < 0) return sb.Append("  kein Kraftwerk auf dieser Karte").ToString();

        var b = _entities[gi];
        int letzte = Patterns == null ? 0 : Patterns.GetBuildingType(b.BType).PatternCount - 1;
        sb.AppendLine($"  Kraftwerk Platz {b.Slot}, {b.FootW}x{b.FootH}, {b.Hp}/{b.HpMax}, "
                    + $"Stufen 1..{letzte}, jetzt Stufe {DamageFrame(b)}");
        if (letzte < 2)
            return sb.Append("  diese Art hat keine Schadensstufen — MISST NICHTS").ToString();

        // 1. Der Stufenwechsel zuendet
        _gbrand.Clear();
        BrandZuendungen = BrandStellen = BrandRauch = BrandFlammen = BrandExplosionen = 0;
        BrandZuendKlaenge = BrandTickerKlaenge = BrandTickerKlaenge410 = 0;
        b.Schadensstufe = DamageFrame(b);
        // ⚠ Der Schritt muss mit DEM Teiler gerechnet werden, den DamageFrame
        // nimmt (HpMax / PatternCount) — mit `letzte` gerechnet blieb die Stufe
        // beim ersten Anlauf auf 1 stehen, und der Lauf meldete »FALSCH«,
        // obwohl der Bau stimmte. Ein Pruefstand, der seine eigene Vorbereitung
        // falsch rechnet, misst nichts.
        int count = Patterns!.GetBuildingType(b.BType).PatternCount;
        int schritt = Mathf.Max(1, b.HpMax / count);
        b.Hp = Mathf.Max(1, b.HpMax - 3 * schritt);               // sicher auf Stufe 3
        GebaeudeStufeNachziehen(b);
        // ⚠ Erwartet werden die Zellen des SCHADENSMUSTERS, nicht des
        // Fussabdrucks — gemessen 1..6 statt 30. Der erste Bau nahm den
        // Fussabdruck, und genau das sah im Spiel wie eine Zerstoerung aus.
        int erwartet = 0;
        foreach (var _ in Schadenszellen(b, b.Schadensstufe)) erwartet++;
        int fuss = Mathf.Max(1, b.FootW) * Mathf.Max(1, b.FootH);
        bool zuendOk = BrandZuendungen == 1 && BrandStellen == erwartet
                    && erwartet > 0 && erwartet < fuss;
        sb.AppendLine($"  Stufenwechsel auf {b.Schadensstufe}: {BrandZuendungen} Zuendung, "
                    + $"{BrandStellen} Brandstellen (erwartet {erwartet} = Zellen des "
                    + $"Schadensmusters; der Fussabdruck haette {fuss}): "
                    + (zuendOk ? "richtig" : "FALSCH"));

        // 2. ⚠ und NICHT nochmal beim naechsten Treffer ohne Stufenwechsel —
        // das Original zuendet nur am Wechsel (@0x4CBC4E je).
        int vorZuendungen = BrandZuendungen;
        b.Hp -= 1;
        GebaeudeStufeNachziehen(b);
        bool einmalOk = BrandZuendungen == vorZuendungen;
        sb.AppendLine($"  ein Treffer ohne Stufenwechsel: {BrandZuendungen - vorZuendungen} "
                    + $"weitere Zuendung (erwartet 0): {(einmalOk ? "richtig" : "FALSCH")}");

        // 3. Der Ticker wirft Rauch — seine eigentliche Meldung
        int takte = 0;
        while (_gbrand.Count > 0 && takte < 4000) { GebaeudebrandTakt(); takte++; }
        bool rauchOk = BrandRauch > 0;
        sb.AppendLine($"  {takte} Takte bis alles aus war: {BrandRauch} Rauchbilder, "
                    + $"{BrandFlammen} Flammen, {BrandExplosionen} Explosionen: "
                    + (rauchOk ? "es raucht, richtig" : "KEIN RAUCH — genau seine Meldung"));
        // bug-482: und es KLINGT — Zuender je Herd, Ticker 20 % je Zelle und Takt (Alter 1..10)
        sb.AppendLine($"  Klang: {BrandZuendKlaenge} Zuenderklaenge 400..405 (Soll = {BrandStellen} Brandstellen), "
                    + $"{BrandTickerKlaenge} Tickerklaenge (davon {BrandTickerKlaenge410} aus 410..415; "
                    + $"Soll ~0,2 x {BrandStellen} x 10 = {BrandStellen * 2})"
                    + (BrandklangAus ? " [--brandklang-aus: alles 0]" : ""));

        // 4. Und es hoert wieder auf. ⚠ Ohne diese Frage koennte ein Brand ewig
        // laufen und die Tafel verstopfen — im Original ist die Lebensdauer
        // beim beschaedigten Bau 11..240 Takte, nicht die 0xFF des toten.
        bool endeOk = _gbrand.Count == 0 && takte < 4000;
        sb.AppendLine($"  Brandliste danach: {_gbrand.Count} Eintraege "
                    + $"(erwartet 0, gelesene Lebensdauer 11..240 Takte): "
                    + (endeOk ? "richtig" : "FALSCH — er erlischt nicht"));

        // 5. ⭐ 07.09.2026, auf seine Meldung »die explosionen scheinen mir zu
        // viel und man sieht kein rauch«: die Zahlen fuer einen GANZEN
        // Beschuss, nicht fuer einen Stufenwechsel. Erst hier ist zu sehen,
        // was der Spieler wirklich sieht — ein Kraftwerk durchlaeuft alle
        // Stufen, und die Lebensdauer waechst mit dem Schaden.
        _gbrand.Clear();
        BrandZuendungen = BrandStellen = BrandRauch = BrandFlammen = BrandExplosionen = 0;
        BrandZuendKlaenge = BrandTickerKlaenge = BrandTickerKlaenge410 = 0;
        b.Hp = b.HpMax; b.Schadensstufe = 1;
        int gesamtTakte = 0;
        for (int stufe = 2; stufe < letzte; stufe++)
        {
            b.Hp = Mathf.Max(1, b.HpMax - stufe * schritt);
            GebaeudeStufeNachziehen(b);
            // zwischen zwei Stufen vergeht Zeit — beim Bombardement wenig,
            // beim Panzerbeschuss mehr. 25 Takte ist eine halbe Sekunde.
            for (int t = 0; t < 25; t++) { GebaeudebrandTakt(); gesamtTakte++; }
        }
        while (_gbrand.Count > 0 && gesamtTakte < 8000) { GebaeudebrandTakt(); gesamtTakte++; }
        sb.AppendLine($"  GANZER BESCHUSS (Stufe 2..{letzte - 1}, {gesamtTakte} Takte = "
                    + $"{gesamtTakte / 50.0:0.0} s): {BrandZuendungen} Zuendungen, "
                    + $"{BrandExplosionen} Explosionen (80x71 Punkte), {BrandFlammen} Flammen, "
                    + $"{BrandRauch} Rauchwolken (27x27, steigend)");
        sb.AppendLine($"      also je Sekunde {BrandExplosionen * 50.0 / Mathf.Max(1, gesamtTakte):0.0} "
                    + $"Explosionen und {BrandRauch * 50.0 / Mathf.Max(1, gesamtTakte):0.0} Rauchwolken. "
                    + $"⚠ Der Lauf URTEILT NICHT — ob das zu viel ist, sagt sein Auge.");

        bool alles = zuendOk && einmalOk && rauchOk && endeOk;
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Rückgabewert für den Prüflauf.</summary>
    public int GebaeudebrandProbeRc() => GebaeudebrandProbe().Contains("BESTANDEN") ? 0 : 1;
}
