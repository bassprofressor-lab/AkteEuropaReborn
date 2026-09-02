using System;
using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>Das Sektorenraster der KI — sec55 / sec56 / sec62, aus dem Original.</b>
///
/// <para>Bis zum 21.08.2026 hatte unser Gegner GAR KEIN Raster: seine Einheiten
/// waren eine globale Liste, und die Auftragswahl nahm schlicht das Ziel mit dem
/// höchsten <c>Priority</c>. Das Original arbeitet vollkommen anders, und die
/// ganze Kette ist inzwischen gelesen (`OFFENE_FRAGEN.md`, Abschnitt AU):</para>
///
/// <list type="number">
///   <item><b>Takt 1</b> — `0x4BA710` / F `0x4BA210` baut die Stärkekarte
///   <b>sec55</b>, und `0x4BA7D0` / F `0x4BA2D0` verdichtet sie zu den
///   Sektorwerten <b>sec56</b> (`+0` eigene, `+2` verbündete, `+4` feindliche
///   Stärke).</item>
///   <item><b>Takt 7</b> — »Set imp cpu:« `0x4BBB80` / F `0x4BB640` summiert je
///   Sektor die Verteidigungswichtigkeit der eigenen Gebäude aus <b>sec62</b>
///   nach `+6`, kopiert sie nach `+7` (»DEF:«) und rechnet
///   <b>`+8 = min(100, 100·(+7) / pro_style[sec61])`</b> (»DEF_robots:«).
///   Danach zählt ein Durchlauf über die eigenen Einheiten `+0x0A` hoch.</item>
///   <item><b>Takt 8</b> — »Not free attacker:« `0x4BE790` / F `0x4BE250` bildet
///   daraus die <b>freien Angreifer</b>, und `target:` `0x4BECF0` / F `0x4BE7A0`
///   wählt den Auftrag mit dem kleinsten <b>`po = Wegkosten / Wichtigkeit`</b>.
///   </item>
/// </list>
///
/// <para>⭐ Die Formel für `+8` ist nicht gedeutet, sondern nachgerechnet:
/// <b>0 Abweichungen von 13 552 Zellen</b> über 14 Dateien.</para>
///
/// <para>⚠ <b>Was hier NICHT aus dem Original stammt, ist einzeln vermerkt.</b>
/// Das betrifft vor allem die Stärkeformel von sec55 (`0x4BA710` ist nicht
/// gelesen) und die Sektor-Wegesuche (`0x4BEA30` ist als Ein-/Ausgang bekannt,
/// nicht als Verfahren).</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    // ---- Die Masse des Rasters ---------------------------------------------
    //
    // Ein Sektor ist 24 × 24 Felder gross, das Raster 11 × 11 = 121. Belegt aus
    // `0x4BAFE0` und `0x4BAD20`, die beide durch 0x18 teilen, und gegengeprüft:
    // 540 von 540 Gebäuden aus 13 `.DM` liegen nach EINER Division im Raster.
    // (Nach zwei Divisionen landen alle bei (0,0) — genau das ist der Fehler
    // des Originals, siehe AU.10 und `AiSektorVon`.)
    private const int SektorFeld = 24;
    private const int SektorKante = 11;
    private const int SektorZahl = SektorKante * SektorKante;

    /// <summary>Der Index, den das Original benutzt: <c>121·Spieler + 11·sx + sy</c>
    /// — <b>spielerdur</b>. sec55 ist dagegen zellendur indiziert; das spielt hier
    /// keine Rolle, weil wir sec55 nicht als Datei halten.</summary>
    private static int SektorIndex(int sx, int sy) => sx * SektorKante + sy;

    /// <summary>Feldkoordinaten → Sektor. Beide Werte werden auf 0…10 geklemmt;
    /// da keine Karte breiter oder höher als 254 sein kann (sec6 fasst 65 536
    /// Wörter bei Index <c>Spalte·256 + Zeile</c>), greift die Klemme nur an
    /// den letzten beiden Spalten und Zeilen.</summary>
    private static (int sx, int sy) AiSektorVon(int col, int row)
        => (Math.Clamp(col / SektorFeld, 0, SektorKante - 1),
            Math.Clamp(row / SektorFeld, 0, SektorKante - 1));

    /// <summary>
    /// <b>Ein Satz von sec56 — 12 Byte im Original.</b>
    /// <c>+0</c> eigene Stärke · <c>+2</c> verbündete · <c>+4</c> feindliche ·
    /// <c>+6</c> Gebäude-Wichtigkeit · <c>+7</c> Kopie davon (»DEF:«) ·
    /// <c>+8</c> »DEF_robots:« · <c>+9</c> <b>tot</b> (0 Fundstellen, in allen
    /// 14 Dateien durchgehend 0 — ein Ausrichtungsloch) · <c>+0xA</c> die Zahl
    /// der Einheiten, die diesem Sektor zugeordnet sind.
    /// </summary>
    private struct AiSektor
    {
        public int Eigen;      // +0x00
        public int Verbuendet; // +0x02
        public int Feind;      // +0x04
        public int Imp;        // +0x06
        public int Def;        // +0x07
        public int DefRobots;  // +0x08
        public int Belegt;     // +0x0A
    }

    private readonly AiSektor[][] _aiRaster = ErzeugeRaster();

    private static AiSektor[][] ErzeugeRaster()
    {
        var r = new AiSektor[8][];
        for (int p = 0; p < 8; p++) r[p] = new AiSektor[SektorZahl];
        return r;
    }

    /// <summary>Die Summe aller <c>imp</c> der eigenen Gebäude — <c>sec110</c>.
    /// Sie entscheidet mit, ob eine Angriffsgruppe »alles mitnimmt«
    /// (<c>sec110 != 0</c> und <c>sec61 != 5</c> → Gruppengrösse 99).</summary>
    private readonly int[] _aiSec110 = new int[8];

    // ---- pro_style: der EINZIGE Zahlenhebel der Schwierigkeit ---------------
    //
    // ⭐⭐ Und zugleich der sechste belegte Unterschied der zwei Auslieferungen:
    //
    //   C 0x4BBD11   movsx ecx, word ptr [ecx*2 + 0x538BC8]   ; 8 WOERTER
    //   F 0x4BB7D6   mov   cl,  byte ptr [edx   + 0x537C08]   ; 8 BYTES
    //
    //   C  1  30  50  100  400  255  0  0
    //   F  1  30  50  100  200  255  0  0
    //
    // In Betriebsart 4 haelt F doppelt so viele Verteidiger zurueck wie C. Wir
    // folgen C, weil C die spaetere Fassung ist (22.01.1998 gegen 16.09.1997).
    //
    // ⚠ Der Formatwechsel erklaert sich selbst: 400 passt nicht in ein Byte,
    // und der C−F-Abstand springt genau an dieser Tafel von 0xFC0 auf 0xFC8 —
    // die acht Byte, um die die Tafel breiter geworden ist.
    //
    // ⚠ EINE MINE: pro_style[6] und [7] sind 0. Waere sec61 sechs oder sieben,
    // teilt »Set imp cpu:« im Original durch Null. Es geht dort nur gut, weil
    // das Kampagnenskript diese Werte nie setzt — wir fangen es unten ab, statt
    // den Absturz nachzubauen.
    private static readonly int[] ProStyleC = { 1, 30, 50, 100, 400, 255, 0, 0 };

    /// <summary>Die Betriebsart je Spieler — <c>sec61</c>. Im Original schreibt
    /// sie <b>genau eine</b> Stelle (C <c>0x4D1050</c>), 72× gerufen, davon 71×
    /// aus dem Kampagnenskript <c>0x487C40</c>: eine reine Missionsvorgabe.
    /// Vorkommende Werte im Skript 2, 3, 4, 5, 10; in den 13 <c>.DM</c> als
    /// Anfangswert 2 (92×), 3 (11×), 5 (1×).</summary>
    private readonly int[] _aiSec61 = { 2, 2, 2, 2, 2, 2, 2, 2 };

    /// <summary>Die Skriptsperre — <c>sec106</c>. <c>!= 0</c> heisst NICHT
    /// »ausgeschieden«, sondern <b>»dieser Spieler wird vom Missionsskript
    /// geführt«</b>: sein ganzer KI-Zug fällt aus (<c>0x4BFBFA</c>), seine
    /// Einheiten können nicht übernommen werden (<c>0x411351</c>), und
    /// Verbündete übergehen ihn (<c>0x42072C</c>). Ausgeschieden ist dagegen
    /// <c>sec53[40p] == 0xFF</c>.</summary>
    private readonly int[] _aiSec106 = new int[8];

    /// <summary>Die Betriebsart eines Spielers setzen — der Nachbau von
    /// <c>0x4D1050</c>. Für das Gefecht ist sie der Schwierigkeitsregler.</summary>
    public void SetAiBetriebsart(int player, int art)
    {
        if (player is < 0 or > 7) return;
        _aiSec61[player] = Math.Clamp(art, 0, 7);
    }

    /// <summary>Die Skriptsperre setzen — der Nachbau von <c>0x4D09F0</c>.</summary>
    public void SetAiSkriptsperre(int player, int wert)
    {
        if (player is < 0 or > 7) return;
        _aiSec106[player] = wert;
    }

    /// <summary>Ist dieser Spieler für den KI-Takt gesperrt? <c>0x4BFBFA</c>.</summary>
    private bool AiGesperrt(int player)
        => player is >= 0 and <= 7 && _aiSec106[player] != 0;

    // ---- Takt 1: die Stärkekarte -------------------------------------------

    /// <summary>
    /// Die Sektorwerte <c>+0</c> / <c>+2</c> / <c>+4</c> neu bilden —
    /// <c>0x4BA710</c> + <c>0x4BA7D0</c>.
    ///
    /// <para>⭐⭐ <b>01.09.2026 — DIE STÄRKEFORMEL IST GELESEN, sie war eine
    /// Setzung.</b> Hier stand <c>wert = 1</c> (»wir zählen die kampffähigen
    /// Einheiten«) mit dem Eingeständnis, <c>0x4BA710</c> sei nur als Ein- und
    /// Ausgang bekannt. Jetzt ist die Funktion Befehl für Befehl gelesen:</para>
    ///
    /// <code>
    ///   0x4BA71B  rep stosd  0xB461C0, 0x1E4 dwords     ; das Rohraster nullen
    ///   0x4BA725  fuer alle 8000 Einheitenplaetze cx:
    ///   0x4BA732    dl = byte[+0x09]  (faze)   == 0xFF -> weiter
    ///   0x4BA741    al = byte[+0x0D]  (Waffe)  == 0    -> weiter
    ///   0x4BA751    sx = byte[+0x00] / 24 ,  sy = byte[+0x01] / 24
    ///   0x4BA77E    spieler = cx / 1000
    ///   0x4BA787    ax = byte[+0x08]                    ; ENERGIE = Trefferpunkte
    ///   0x4BA792    word[0xB461C0 + 2·(8·(11·sx + sy) + spieler)] += ax
    /// </code>
    ///
    /// <para><b>Stärke ist also die Summe der TREFFERPUNKTE aller bewaffneten
    /// Einheiten im Sektor</b> — nicht ihre Zahl. Der Feldname stammt aus dem
    /// Spiel selbst (der Dump <c>@0x413743</c> druckt <c>energie:</c> für
    /// <c>+0x08</c>). <c>0x4BA7D0</c> verteilt das Rohraster danach nur noch
    /// nach der Bündnisspalte auf <c>+0</c> / <c>+2</c> / <c>+4</c>.</para>
    ///
    /// <para><b>Vollerhebung</b> über die Relokationstafel (Fenster
    /// <c>0xB461C0 + 3872</c>): <b>genau EIN Schreiber</b> (<c>0x4BA792</c>),
    /// 14 Leser, 0 unklar. Es gibt keine zweite Quelle.</para>
    ///
    /// <para>⚠ Zwei Unterschiede bleiben, beide benannt: das Original prüft
    /// <b>nicht</b> auf Beweglichkeit (ein Geschützturm ist dort ein GEBÄUDE und
    /// steht in einer anderen Tafel, bei uns nicht), und es zählt über alle acht
    /// Spielerblöcke, auch über tote Plätze — <c>faze == 0xFF</c> ist unser
    /// <c>Dead</c>. <c>--staerke-alt</c> stellt die alte Zählung wieder her.</para>
    /// </summary>
    private void AiStaerkeraster(int p)
    {
        var r = _aiRaster[p];
        for (int s = 0; s < SektorZahl; s++)
        {
            r[s].Eigen = 0;
            r[s].Verbuendet = 0;
            r[s].Feind = 0;
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (StaerkeAlt && !e.Mobile) continue;
            if (!CanFight(e)) continue;                 // byte[+0x0D] != 0

            var (sx, sy) = AiSektorVon(e.Col, e.Row);
            int s = SektorIndex(sx, sy);
            int wert = StaerkeAlt ? 1 : e.Hp;           // byte[+0x08], »energie«

            if (e.Owner == p) r[s].Eigen += wert;
            else if (AiVerbuendet(p, e.Owner)) r[s].Verbuendet += wert;
            else r[s].Feind += wert;
        }
    }

    /// <summary><c>--staerke-alt</c> — die Gegenprobe zur gelesenen
    /// Stärkeformel: eine Einheit zählt wieder als 1 statt mit ihren
    /// Trefferpunkten. Nur damit ist zu messen, ob die Formel überhaupt etwas
    /// ändert (sie ändert die SCHWELLE nicht — <c>&gt; 0</c> bleibt
    /// <c>&gt; 0</c> —, wohl aber jede Rechnung, die die Zahl weiterverwendet).
    /// </summary>
    public static bool StaerkeAlt;

    /// <summary>Die Bündniszeile <c>sec53[40·p + 0x15 + q]</c>. Ein Spieler ist
    /// sich immer selbst freund; alles andere kommt aus der Diplomatie.
    ///
    /// <para>⚠⚠ <b>02.09.2026 — HIER STAND EINE MANNSCHAFT STATT DER
    /// DIPLOMATIE, UND DAS WAR DER FEHLER.</b> Gemeldet aus einem Spiellauf
    /// der Kampagne 3: »ich höre fremde Einheiten im Fog of War kämpfen, als
    /// würde sich die KI selber beballern« — und auf Nachfrage: »doch, habe es
    /// live gesehen wie die sich beschießen«.</para>
    ///
    /// <para>Und genau so war es. Die Zeile lautete
    /// <c>p == q || AiMannschaft(p) == AiMannschaft(q)</c>, und
    /// <c>AiMannschaft(p)</c> gab <c>p</c> zurück — jeder war seine eigene
    /// Mannschaft, also war JEDER FREMDE EIN FEIND. In Mission 3 sind die
    /// Spieler 1, 2, 5 und 6 aber <b>alle untereinander verbündet</b>
    /// (<c>campaign_diplomacy.json</c> aus <c>mission_init</c> @0x487c40).
    /// Der Sektorlauf zählte die Einheiten des Verbündeten also als
    /// <c>Feind</c>, <see cref="AiZielImSektor"/> gab eine davon als Ziel
    /// heraus, und <c>AiSend</c> setzte <c>Ordered = true</c> — ein BEFOHLENER
    /// Angriff, der die Bündnisprüfung von <c>IsHostile</c> gar nicht mehr
    /// passiert.</para>
    ///
    /// <para><b>Gemessen</b> (<c>--beschuss-check</c>, Kampagne 3, 120 s,
    /// Keim 7): <b>188 von 468 Schuss</b> gingen auf einen Verbündeten, davon
    /// 177 von Spieler 5 auf Spieler 1. Nullmodell: bei intakter Prüfung darf
    /// dort <b>0</b> stehen — die Diplomatie erlaubt nur die Paare mit
    /// Spieler 0.</para>
    ///
    /// <para>⭐ Die Behebung bringt uns NÄHER ans Original, nicht weiter weg:
    /// das Original liest an dieser Stelle die Bündniszeile
    /// <c>sec53[40·p + 0x15 + q]</c> und überspringt ganze Spielerblöcke
    /// (<c>si += 1000</c>). <see cref="AiHostile"/> liest genau diese Matrix.
    /// Im GEFECHT ohne Bündnisse ändert sich nichts: dort ist
    /// <c>_haveAllies</c> falsch und <c>AiHostile</c> fällt selbst auf
    /// <c>q != p</c> zurück — die Voreinstellung des Originals für ein Gefecht
    /// ohne Bündnisse (<c>netzstart</c> setzt nur die Diagonale,
    /// <c>0x41952E</c>).</para>
    ///
    /// <para>Gegenprobe <c>--sektor-buendnis-alt</c> stellt den Stand vom
    /// 01.09.2026 wieder her.</para></summary>
    private bool AiVerbuendet(int p, int q)
        => SektorBuendnisAlt
            ? p == q || AiMannschaft(p) == AiMannschaft(q)
            : p == q || !AiHostile(p, q);

    /// <summary><c>--sektor-buendnis-alt</c> — die Gegenprobe: der Sektorlauf
    /// fragt wieder die Mannschaft statt der Diplomatie, und damit ist in der
    /// Kampagne wieder jeder Fremde ein Feind (Stand bis zum 02.09.2026).
    /// </summary>
    public static bool SektorBuendnisAlt;

    /// <summary>Die Mannschaft eines Platzes. Ohne Bündnissystem ist jeder seine
    /// eigene — dann fällt <see cref="AiVerbuendet"/> auf <c>p == q</c> zurück.
    /// ⚠ Nur noch unter <see cref="SektorBuendnisAlt"/> in Gebrauch.</summary>
    private static int AiMannschaft(int p) => p;

    // ---- Takt 7: »Set imp cpu:« --------------------------------------------

    /// <summary>
    /// <c>Set imp cpu:</c> — C <c>0x4BBB80</c> / F <c>0x4BB640</c>.
    ///
    /// <para>Vier Schritte, genau in dieser Reihenfolge:</para>
    /// <list type="number">
    ///   <item><c>sec110[p] = 0</c>, und je Sektor <c>+6 = +7 = +8 = +0xA = 0</c>.</item>
    ///   <item>Über alle 255 Gebäudeplätze: <c>imp = sec62[…]</c>; ist es ≠ 0,
    ///   dann <c>+6 += imp</c> im Sektor des Gebäudes und <c>sec110[p] += imp</c>.</item>
    ///   <item>Je Sektor <c>+7 = +6</c> und
    ///   <c>+8 = min(100, 100·(+7) / pro_style[sec61[p]])</c>.</item>
    ///   <item>Über die eigenen Einheiten: <c>CPU0 == 0 &amp;&amp; UKOL == 0</c> →
    ///   in die Freiliste; <c>CPU0 == 1</c> oder <c>2</c> → <c>+0xA++</c> im
    ///   Sektor, den <c>CPU1</c> nennt.</item>
    /// </list>
    /// </summary>
    private void AiSetImpCpu(int p)
    {
        var r = _aiRaster[p];
        _aiSec110[p] = 0;
        for (int s = 0; s < SektorZahl; s++)
        {
            r[s].Imp = 0;
            r[s].Def = 0;
            r[s].DefRobots = 0;
            r[s].Belegt = 0;
        }

        // Schritt 2 — die Verteidigungswichtigkeit der eigenen Gebaeude.
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.Owner != p) continue;
            int imp = AiImpVon(e);
            if (imp == 0) continue;
            var (sx, sy) = AiSektorVon(e.Col, e.Row);
            r[SektorIndex(sx, sy)].Imp += imp;
            _aiSec110[p] += imp;
        }

        // Schritt 3 — »DEF:« und »DEF_robots:«.
        int stil = ProStyleC[Math.Clamp(_aiSec61[p], 0, 7)];
        for (int s = 0; s < SektorZahl; s++)
        {
            r[s].Def = r[s].Imp;
            // ⚠ Das Original teilt hier ungeprueft. Bei pro_style 0 (die
            // Betriebsarten 6 und 7) waere das eine Division durch Null; das
            // Skript setzt sie nie, also KANN es dort nicht auffallen. Wir
            // lassen den Sektor stattdessen unverteidigt stehen, statt einen
            // Absturz nachzubauen, den kein Spieler je gesehen hat.
            r[s].DefRobots = stil <= 0 ? 0 : Math.Min(100, 100 * r[s].Def / stil);
        }

        // Schritt 4 — die zugeordneten Einheiten. ⭐ Seit dem 01.09.2026 macht
        // das die ZUSTANDSMASCHINE selbst, in demselben Durchlauf wie das
        // Original (die Modi 1 und 2 zaehlen sich dort an ZWEI verschiedenen
        // Sektoren: 1 am zugewiesenen, 2 am eigenen Standort). Nur die alte
        // Bruecke braucht den getrennten Zaehllauf.
        if (SektormaschineAlt) { AiZustandVorlaeufig(p); AiBelegtZaehlen(p); }
    }

    /// <summary>Der alte Schritt 4 — <b>ohne</b> die Unterscheidung zwischen
    /// zugewiesenem Sektor (Modus 1) und Standortsektor (Modus 2). Läuft nur
    /// noch unter <c>--sektormaschine-alt</c>.</summary>
    private void AiBelegtZaehlen(int p)
    {
        var r = _aiRaster[p];
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p || !e.Mobile) continue;
            if (e.AiCpu0 is 1 or 2)
                r[AiSektorAus(e.AiCpu1)].Belegt++;
        }
    }

    /// <summary>Das Halbbytepaar <c>CPU1</c> als Sektorplatz — <c>low = sx</c>,
    /// <c>high = sy</c>. Das Original packt es an <c>0x4BC133</c> gleich beim
    /// Suchen (<c>cx += 0x10</c> je Zeile), und darum passen 11 Sektoren in ein
    /// Halbbyte.</summary>
    private static int AiSektorAus(int cpu1)
        => SektorIndex(Math.Clamp(cpu1 & 0x0F, 0, SektorKante - 1),
                       Math.Clamp((cpu1 >> 4) & 0x0F, 0, SektorKante - 1));

    /// <summary>
    /// Die <c>imp</c>-Zahl eines Gebäudes — <c>sec62</c>, 8 × 255 × 2 B.
    ///
    /// <para>Gemessen an 13 <c>.DM</c>: 324 gesetzte Einträge, davon
    /// <b>322 (99,4 %)</b> auf ein Gebäude mit Typ ≠ 0 und <b>316 (97,5 %)</b>
    /// auf ein <b>eigenes</b>. Die Werte sind 6 (311×), 4 (8×), 9 (3×), 2 (2×).
    /// </para>
    ///
    /// <para>⚠ <b>Im Gefecht gibt es keine <c>sec62</c>.</b> Sie kommt aus dem
    /// Kartenbauer, und die 23 <c>.CWM</c> tragen die KI-Abschnitte gar nicht
    /// (nur sec1…38). Wir setzen darum den Wert, der im Original 311 von 324
    /// Fällen ausmacht — <b>6</b> — für jedes eigene Gebäude, und lassen
    /// <see cref="AiImpTafel"/> ihn überschreiben, sobald eine Mission ihn
    /// mitbringt.</para>
    ///
    /// <para>⭐⭐ <b>01.09.2026 — die 6 ist nicht mehr geraten, das SPIEL schreibt
    /// sie.</b> Vollerhebung über das Fenster <c>0xBC41E0 + 4080</c>: 74
    /// Relokationen, und unter den Schreibern stehen zwei ausserhalb jedes
    /// Missionsblocks —</para>
    ///
    /// <code>
    ///   0x43CF75  byte[0xBC41E1 + 2·(255·ALTbesitzer + platz)] := 0
    ///   0x43CF8D  byte[0xBC41E1 + 2·(255·NEUbesitzer + platz)] := 6   ; Besitzwechsel
    ///   0x43D177  byte[0xBC41E1 + 2·(255·besitzer   + platz)] := 6   ; Anlage
    /// </code>
    ///
    /// <para>Ein Gebäude bekommt seine <c>imp</c> also beim Aufstellen und beim
    /// Übernehmen — <b>6</b>, ohne Umweg über die Karte. Die Missionsblöcke
    /// (<c>0x488ADD</c>, <c>0x48934C</c>, <c>0x4896CA</c>, …) schreiben danach
    /// nur einzelne Plätze um. <b>Damit gilt die 6 auch in der Kampagne</b>, und
    /// die Zeile <c>InCampaign ? 0 : 6</c> war falsch: mit ihr hat in einer
    /// Mission KEIN Sektor Bedarf, und die Zuweisung aus
    /// <see cref="AiZustandsmaschine"/> findet nie einen Zielsektor.</para>
    ///
    /// <para><c>--imp-alt</c> stellt die alte Zeile wieder her.</para>
    /// </summary>
    private int AiImpVon(Entity e)
    {
        if (e.Owner is < 0 or > 7) return 0;
        var tafel = _aiImp[e.Owner];
        if (tafel != null && e.Slot >= 0 && e.Slot < tafel.Length && tafel[e.Slot] != 0)
            return tafel[e.Slot];
        return ImpAlt && InCampaign ? 0 : 6;
    }

    /// <summary><c>--imp-alt</c> — die Gegenprobe: in der Kampagne wieder gar
    /// kein Bedarf, wie bis zum 31.08.2026.</summary>
    public static bool ImpAlt;

    private readonly int[]?[] _aiImp = new int[8][];

    /// <summary>Die <c>imp</c>-Tafel eines Spielers aus einem Spielstand
    /// übernehmen — 255 Plätze.</summary>
    public void AiImpTafel(int player, int[] werte)
    {
        if (player is < 0 or > 7) return;
        _aiImp[player] = werte;
    }

    // ---- Takt 8: »Not free attacker:« und »target:« -------------------------

    /// <summary>
    /// <c>Not free attacker:</c> — C <c>0x4BE790</c> / F <c>0x4BE250</c>.
    ///
    /// <para>Über alle 121 Sektoren, in denen <c>+8 &lt; +0xA</c> gilt:
    /// <c>Überschuss = +0xA − +8</c>. Die Summe ist der Rückgabewert, und der
    /// grösste Einzelüberschuss liefert den Ausgangssektor. Null heisst
    /// »Not free attacker:« — <c>target:</c> bricht dann ab.</para>
    ///
    /// <para>Also wörtlich: <b>zugeordnete Einheiten minus benötigte
    /// Verteidiger</b>, sektorweise, nicht global.</para>
    /// </summary>
    private int AiFreieAngreifer(int p, out int sx, out int sy)
    {
        var r = _aiRaster[p];
        int summe = 0, best = 0;
        sx = sy = 0;
        for (int x = 0; x < SektorKante; x++)
            for (int y = 0; y < SektorKante; y++)
            {
                var s = r[SektorIndex(x, y)];
                if (s.DefRobots >= s.Belegt) continue;
                int ueber = s.Belegt - s.DefRobots;
                summe += ueber;
                if (ueber > best) { best = ueber; sx = x; sy = y; }
            }
        return summe;
    }

    /// <summary>
    /// Die <b>Sektor-Kostenkarte</b> — <c>0x4BEA30</c> / F <c>0x4BE4E0</c>,
    /// Puffer <c>0xB45FB0</c> (121 × 4 B) mit Vorgängerkarte <c>0xB36AA0</c>
    /// und Warteschlange <c>0xB38D50</c>.
    ///
    /// <para>⚠ <b>Das VERFAHREN ist nicht gelesen</b>, nur Ein- und Ausgang: aus
    /// einem Startsektor entsteht ein Kostenfeld über alle 121, aus dem
    /// <c>target:</c> den Wert <c>pway</c> zieht. Eine Warteschlange mit
    /// Vorgängerkarte über ein 11 × 11-Gitter ist eine Breitensuche; dass sie
    /// GENAU so gewichtet wie das Original, ist damit nicht gesagt.</para>
    ///
    /// <para>Die Nachbarreihenfolge ist dagegen belegt (Tafel C <c>0x538C10</c>,
    /// 9 × (dx,dy)): (0,0), (0,+1), (+1,0), (−1,0), (0,−1), (+1,+1), (−1,+1),
    /// (+1,−1), (−1,−1) — also die vier Geraden vor den vier Schrägen.</para>
    /// </summary>
    private static readonly (int dx, int dy)[] AiNachbarn =
    {
        (0, 0), (0, 1), (1, 0), (-1, 0), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1),
    };

    private readonly int[] _aiWege = new int[SektorZahl];

    private void AiWegekarte(int sx, int sy)
    {
        for (int s = 0; s < SektorZahl; s++) _aiWege[s] = int.MaxValue;
        _aiWege[SektorIndex(sx, sy)] = 0;

        var schlange = new Queue<(int x, int y)>();
        schlange.Enqueue((sx, sy));
        while (schlange.Count > 0)
        {
            var (x, y) = schlange.Dequeue();
            int hier = _aiWege[SektorIndex(x, y)];
            for (int n = 1; n < AiNachbarn.Length; n++)   // n = 0 ist (0,0)
            {
                int nx = x + AiNachbarn[n].dx, ny = y + AiNachbarn[n].dy;
                if (nx < 0 || ny < 0 || nx >= SektorKante || ny >= SektorKante) continue;
                int j = SektorIndex(nx, ny);
                if (_aiWege[j] <= hier + 1) continue;
                _aiWege[j] = hier + 1;
                schlange.Enqueue((nx, ny));
            }
        }
    }

    /// <summary>Der Wegewert eines Sektors nach dem letzten
    /// <see cref="AiWegekarte"/>-Lauf. Unerreichbar → <c>int.MaxValue</c>.</summary>
    private int AiWegewert(int sx, int sy) => _aiWege[SektorIndex(sx, sy)];

    /// <summary>
    /// <b><c>target:</c> — C <c>0x4BECF0</c> / F <c>0x4BE7A0</c>. Die
    /// Auftragswahl, und sie ist eine DIVISION.</b>
    ///
    /// <para>Das Original protokolliert je Kandidat <c>target:</c> <c>cx:</c>
    /// <c>cy:</c> <c>imp:</c> <c>pway:</c> <c>po:</c> <c>min:</c> und wählt
    /// am Ende <c>r_best:</c> mit dem kleinsten</para>
    ///
    /// <code>po = pway / imp</code>
    ///
    /// <para>— also <b>Wegkosten geteilt durch Wichtigkeit</b>. Ein doppelt so
    /// wichtiges Ziel darf doppelt so weit weg liegen. Das ist etwas anderes als
    /// »nimm das wichtigste« und etwas anderes als »nimm das nächste«.</para>
    ///
    /// <para>⚠ Bis zum 21.08.2026 nahm <c>AiMissionAttack</c> hier schlicht das
    /// Maximum von <c>Priority</c> und kannte gar keinen Wegewert. Das war der
    /// grösste einzelne Abstand unseres Gegners zum Original.</para>
    ///
    /// <para>Losgeschickt wird nur, wenn ein Auftrag gefunden ist <b>und</b>
    /// (<c>freie &gt; po</c> <b>oder</b> <c>sec61 == 5</c>). Ein weit entferntes
    /// oder unwichtiges Ziel verlangt also mehr Überschuss.</para>
    /// </summary>
    /// <returns>Der Listenplatz des gewählten Auftrags und sein <c>po</c>, oder
    /// (-1, 0), wenn keiner in Frage kommt.</returns>
    private (int platz, int po) AiZielwahl(int p, List<MissionTarget> liste,
                                           int startX, int startY)
    {
        AiWegekarte(startX, startY);

        int best = -1, min = int.MaxValue;
        for (int k = liste.Count - 1; k >= 0; k--)
        {
            int idx = ResolveTarget(p, liste[k]);
            if (idx < 0) { liste.RemoveAt(k); continue; }   // erledigt — streichen

            // »IMP is 0!!!« — im Original ein Abbruchfenster. Ein Auftrag mit
            // Wichtigkeit 0 kann nicht geteilt werden und darf gar nicht
            // entstehen; wir uebergehen ihn, statt zu teilen.
            int imp = liste[k].Priority;
            if (imp <= 0) continue;

            var e = _entities[idx];
            var (tx, ty) = AiSektorVon(e.Col, e.Row);
            int pway = AiWegewert(tx, ty);
            if (pway == int.MaxValue) continue;             // unerreichbar

            int po = pway / imp;
            if (po >= min) continue;
            min = po;
            best = k;
        }
        return best < 0 ? (-1, 0) : (best, min);
    }

    // ---- Die Angriffsgruppen: sec68, 4 × 100 je Spieler ---------------------

    /// <summary>
    /// <b>Eine Angriffsgruppe — sec68.</b> Die Tafel ist
    /// <c>6464 B = 8 Spieler × 4 Gruppen × 202 B</c>, Index
    /// <c>202·(4p+g)</c>: <c>+0</c> Anzahl, <c>+1</c> die Auftragsnummer,
    /// <c>+2…+0xC9</c> <b>100 Einheitennummern als Wörter</b>.
    ///
    /// <para>⚠ Das heisst hart: <b>höchstens vier Gruppen je Spieler, höchstens
    /// 100 Einheiten je Gruppe.</b> Sind alle vier belegt, meldet das Original
    /// »Attack group not available« und kehrt <b>ohne Wirkung</b> zurück — es
    /// wird also keine fünfte Welle gebildet, sie fällt ersatzlos aus.</para>
    ///
    /// <para>⚠ <b>Berichtigung vom 21.08.2026:</b> Diese Tafel ist sec68, nicht
    /// sec108. sec108 (1984 B = 32 × 62) trägt die <b>Wegpunkte</b> der Gruppen
    /// (<c>0x4BCF30</c>) und ist noch nicht gelesen.</para>
    ///
    /// <para>Gemessen: in allen 13 <c>.DM</c> sind alle 416 Gruppenzähler 0, und
    /// <c>CPU0 == 10</c> kommt in keiner Prüfdatei vor. Die Gruppen sind
    /// <b>reiner Laufzeitzustand</b> — der Kartenbauer setzt sie nicht.</para>
    /// </summary>
    private sealed class AiGruppe
    {
        public const int MaxEinheiten = 100;

        /// <summary>
        /// ⚠⚠ <b>HIER STAND EIN LISTENPLATZ, UND DAS WAR FALSCH.</b>
        ///
        /// <para>Im Original ist sec69 eine <b>feste Tafel mit 100 Plätzen</b>;
        /// ein erledigter Auftrag wird <b>an Ort und Stelle geleert</b>, die
        /// übrigen rücken NICHT nach. Ein gespeicherter Index bleibt darum
        /// gültig.</para>
        ///
        /// <para>Unsere Zielliste ist dagegen eine <c>List&lt;&gt;</c>, aus der
        /// <see cref="AiZielwahl"/> erledigte Einträge <b>herausnimmt</b> —
        /// dabei rutschen alle dahinterliegenden eine Stelle vor. Ein
        /// gemerkter Platz zeigte danach auf einen <b>fremden</b> Auftrag.
        /// Wir merken uns darum den Auftrag selbst.</para>
        /// </summary>
        public MissionTarget? Auftrag;
        public readonly List<int> Einheiten = new();
    }

    private readonly AiGruppe[][] _aiGruppen = ErzeugeGruppen();

    private static AiGruppe[][] ErzeugeGruppen()
    {
        var g = new AiGruppe[8][];
        for (int p = 0; p < 8; p++)
        {
            g[p] = new AiGruppe[4];
            for (int k = 0; k < 4; k++) g[p][k] = new AiGruppe();
        }
        return g;
    }

    /// <summary>
    /// <c>Create group cpu:</c> / <c>Attack group not available</c> —
    /// C <c>0x4BC920</c> / F <c>0x4BC3E0</c>.
    ///
    /// <para>Die Gruppengrösse kommt als <b><c>(3·po) / 2</c></b> herein und
    /// wird auf <b>3 … 99</b> geklemmt. Ist <c>sec110[p] != 0</c> <b>und</b>
    /// <c>sec61[p] != 5</c>, wird sie auf <b>99</b> hochgesetzt — »alles, was
    /// geht«.</para>
    ///
    /// <para>Aufnahmeregel: <c>faze == 0</c>, <c>CPU0</c> ist 1 oder 2, Antrieb
    /// ≠ <c>0xAB</c>, und im Sektor aus <c>CPU1</c> muss
    /// <c>+7 &lt; +0xA</c> gelten. Dann <c>CPU0 = 10</c>,
    /// <c>CPU1 = Gruppennummer</c>, Eintrag anhängen, <c>+0xA--</c>.</para>
    ///
    /// <para><b>»Take all«</b> (<c>sec110[p] == 0</c>): ohne Sektorprüfung, jede
    /// Einheit mit <c>faze == 0</c>, <c>UKOL &lt; 45</c>, <c>CPU0 &lt; 5</c>.</para>
    /// </summary>
    /// <returns>Die Gruppennummer 0…3, oder -1 für »Attack group not available«.</returns>
    private int AiGruppeBilden(int p, MissionTarget auftrag, int po)
    {
        int frei = -1;
        for (int g = 0; g < 4; g++)
            if (_aiGruppen[p][g].Einheiten.Count == 0) { frei = g; break; }
        if (frei < 0)
        {
            // »Attack group not available« — im Auslieferungszustand STUMM
            // (die Meldung laeuft durch `meldung()` @0x41CDB0, die zuerst den
            // Entwicklerschalter byte[0x4FA0C0] prueft). Der Code kehrt danach
            // ohne Wirkung zurueck: die Welle faellt ersatzlos aus.
            return -1;
        }

        int groesse = Math.Clamp(3 * po / 2, 3, 99);
        bool alles = _aiSec110[p] != 0 && _aiSec61[p] != 5;
        if (alles) groesse = 99;

        var gruppe = _aiGruppen[p][frei];
        gruppe.Auftrag = auftrag;
        gruppe.Einheiten.Clear();

        var r = _aiRaster[p];
        for (int i = 0; i < _entities.Count && gruppe.Einheiten.Count < groesse; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p || !e.Mobile) continue;
            if (!CanFight(e)) continue;

            int s;
            if (_aiSec110[p] == 0)
            {
                // »Take all« — ohne Sektorpruefung.
                if (e.AiCpu0 >= 5) continue;
                s = -1;
            }
            else
            {
                if (e.AiCpu0 is not (1 or 2)) continue;
                s = SektorIndex(Math.Clamp(e.AiCpu1 & 0x0F, 0, SektorKante - 1),
                                Math.Clamp((e.AiCpu1 >> 4) & 0x0F, 0, SektorKante - 1));
                if (r[s].Def >= r[s].Belegt) continue;   // der Sektor braucht sie
            }

            if (gruppe.Einheiten.Count >= AiGruppe.MaxEinheiten) break;
            gruppe.Einheiten.Add(i);
            e.AiCpu0 = 10;                 // »in einer Gruppe«
            e.AiCpu1 = frei;
            if (s >= 0) r[s].Belegt--;
        }

        if (gruppe.Einheiten.Count == 0) { AiGruppeAufloesen(p, frei); return -1; }
        return frei;
    }

    /// <summary>Tote und fremd gewordene Einheiten aus den Gruppen streichen und
    /// leere Gruppen freigeben. Im Original erledigt das der Gruppenlauf
    /// <c>0x4BCF30</c> nebenbei; wir tun es ausdrücklich, damit der einzige
    /// Weg, wieder einen Gruppenplatz zu bekommen, sichtbar bleibt.</summary>
    private void AiGruppenPflegen(int p)
    {
        for (int g = 0; g < 4; g++)
        {
            var gruppe = _aiGruppen[p][g];
            if (gruppe.Einheiten.Count == 0) continue;

            // Schritt 1 des Originals: aufraeumen. Ein Mitglied fliegt raus,
            // wenn es tot ist oder den Besitzer gewechselt hat.
            gruppe.Einheiten.RemoveAll(i =>
            {
                bool weg = i >= _entities.Count || _entities[i].Dead ||
                           _entities[i].Owner != p;
                if (weg && i < _entities.Count) AiFreigeben(_entities[i]);
                return weg;
            });

            // Schritt 2: ist das Ziel noch da? @0x4BCF30 mit der Tafel
            // 0x4BD7BC. Art 1 loest die Gruppe auch dann auf, wenn das
            // Gebaeude inzwischen UNS gehoert — nicht nur, wenn es weg ist.
            if (gruppe.Auftrag == null || ResolveTarget(p, gruppe.Auftrag) < 0)
            {
                AiGruppeAufloesen(p, g);
                continue;
            }
            if (gruppe.Einheiten.Count == 0) AiGruppeAufloesen(p, g);
        }
    }

    /// <summary>
    /// <b>Eine Gruppe auflösen — <c>0x4BCEA0</c> / F <c>0x4BC960</c>.</b>
    ///
    /// <para>⚠⚠ <b>Diese Funktion fehlte, und ihr Fehlen liess den Gegner nach
    /// wenigen Wellen erstarren.</b> <see cref="AiGruppeBilden"/> setzt jedem
    /// Mitglied <c>CPU0 = 10</c>; gezählt werden in <see cref="AiSetImpCpu"/>
    /// aber nur die Einheiten mit <c>CPU0</c> 1 oder 2. Wer nie zurückgesetzt
    /// wird, fällt also dauerhaft aus der Rechnung — die Zahl der freien
    /// Angreifer sank mit jeder Welle, bis gar nichts mehr losfuhr.</para>
    ///
    /// <para>Das Original macht es an drei Stellen: wenn das Ziel weg ist,
    /// wenn es <b>uns</b> gehört, und wenn die Gruppe leer läuft. Es setzt dann
    /// <c>sec60[u]+0 = 0</c> und <c>+1 = 0</c> für alle Mitglieder und
    /// <c>sec68[+0] = 0</c>.</para>
    /// </summary>
    private void AiGruppeAufloesen(int p, int g)
    {
        var gruppe = _aiGruppen[p][g];
        foreach (int i in gruppe.Einheiten)
            if (i < _entities.Count) AiFreigeben(_entities[i]);
        gruppe.Einheiten.Clear();
        gruppe.Auftrag = null;
    }

    /// <summary>Eine Einheit aus ihrer Gruppe entlassen: <c>CPU0 = 0</c>,
    /// <c>CPU1 = 0</c>. Danach nimmt <see cref="AiZustandVorlaeufig"/> sie beim
    /// nächsten Zug wieder auf.</summary>
    private static void AiFreigeben(Entity e)
    {
        if (e.AiCpu0 != 10) return;      // 3/5/20 bleiben, wie sie sind
        e.AiCpu0 = 0;
        e.AiCpu1 = 0;
    }

    /// <summary>Wieviele Gruppenplätze dieser Spieler noch frei hat — für den
    /// Prüfstand und die Statuszeile.</summary>
    public int AiFreieGruppen(int player)
    {
        if (player is < 0 or > 7) return 0;
        int n = 0;
        for (int g = 0; g < 4; g++) if (_aiGruppen[player][g].Einheiten.Count == 0) n++;
        return n;
    }

    // ---- Takt 7, zweite Haelfte: die Zustandsmaschine je Einheit ------------

    /// <summary>
    /// ⭐⭐⭐ <b>DIE ZUSTANDSMASCHINE JE EINHEIT</b> — die zweite Hälfte von
    /// <c>0x4BBB80</c> (Schleife <c>0x4BBDD2…0x4BC0BA</c>, Sprungtafel
    /// <c>0x4BC214</c> = <c>0x4BBE10</c> / <c>0x4BBE3A</c> / <c>0x4BBFBA</c> /
    /// <c>0x4BC029</c>, aus der Datei gelesen), samt der Zuweisung
    /// <c>0x4BC0C0…0x4BC208</c>. Nachgelesen am 01.09.2026, Befehl für Befehl.
    ///
    /// <para><b>⚠⚠ BERICHTIGUNG von BV.1/BV.3:</b> <c>0xB400F0</c> ist keine
    /// Sektortafel, sondern eine <b>Einheitentafel</b> — <c>3 Byte je
    /// Einheitenplatz</c>, adressiert <c>lea ecx,[ecx+ecx*2+0xB400F0]</c>
    /// (<c>@0x4BC691</c>). <c>+0</c> ist der MODUS, <c>+1</c> das Halbbytepaar
    /// des zugewiesenen Sektors. Das »Tor Sektorzustand 1..2« aus BV.3 ist in
    /// Wahrheit »Einheiten-Modus ∈ {1, 2}«.</para>
    ///
    /// <para>Die vier Arme, wörtlich:</para>
    /// <list type="bullet">
    ///   <item><b>0 — frei</b> (<c>0x4BBE10</c>): steht die Einheit
    ///   (<c>UKOL == 0</c>), kommt ihr Platz in die Kandidatenliste
    ///   <c>0xB38530</c>.</item>
    ///   <item><b>1 — zugewiesen, marschiert</b> (<c>0x4BBE3A</c>): zählt sich
    ///   in <c>+0x0A</c> ihres <b>zugewiesenen</b> Sektors. Steht sie, wird das
    ///   5×5-Feld um sie nach einer Zelle mit <b>Lage 99</b> abgesucht; gefunden
    ///   → nochmals <c>fahre</c> auf eine Zufallszelle des Sektors, sonst
    ///   <b>Modus := 2</b>.</item>
    ///   <item><b>2 — abrufbereit</b> (<c>0x4BBFBA</c>): zählt sich in
    ///   <c>+0x0A</c> ihres <b>Standort</b>sektors; mit
    ///   <c>rand() % 200 == 111</c> fällt sie auf Modus 0 zurück.</item>
    ///   <item><b>3 — im Angriff</b> (<c>0x4BC029</c>): <c>UKOL == 0</c> →
    ///   Modus 0. Sonst die <b>Leine</b>: mehr als EIN Sektor je Achse vom
    ///   zugewiesenen weg → <c>UKOL := 0</c> UND Modus := 0, der Angriff bricht
    ///   ab (<c>0x4BC093…0x4BC09E</c>).</item>
    /// </list>
    ///
    /// <para><b>Die Zuweisung:</b> höchstens <b>5</b> Kandidaten je Durchlauf
    /// (<c>cmp word[esp+0x10], 5</c> @<c>0x4BC0C0</c>); bester Sektor ist das
    /// <b>Minimum von <c>100·belegt / bedarf</c></b> über alle Sektoren mit
    /// Bedarf &gt; 0 (Startwert 9999, <b>strikt</b> kleiner gewinnt, sonst der
    /// erste); jeder Kandidat bekommt Modus 1, das Halbbytepaar und ein
    /// <c>fahre</c> auf <c>(sx·24 + rand%14 + 5, sy·24 + rand%14 + 5)</c>.</para>
    ///
    /// <para><b>⭐ Das ist die gemeldete Lage</b> (bug-012): die Einheiten laufen
    /// zu den Sektoren ihrer eigenen Gebäude, stellen sich dort auf
    /// Zufallszellen und bleiben in Modus 2 stehen — bis
    /// <see cref="AiGruppenangriff"/> in einem der neun Nachbarsektoren
    /// Feindstärke sieht. Das »Stehen vor der Basis« ist kein Fehler, es ist
    /// der Wartezustand des Originals.</para>
    ///
    /// <para><b>⚠ UNSERE SETZUNGEN, benannt:</b></para>
    /// <list type="number">
    ///   <item><b><c>UKOL == 0</c></b> heisst bei uns »kein Weg, kein Ziel,
    ///   keine Befehlsliste« — unser <see cref="Entity.Ukol"/> führt nur die
    ///   drei Werte der Einfahrt (0/48/50) und taugt dafür nicht.</item>
    ///   <item><b>Der 5×5-Griff nach Lage 99 ist NICHT gebaut</b>: die
    ///   Lagenkarte (<c>0x542E18</c>, .CWM-Sektion 20) liegt zur Laufzeit gar
    ///   nicht vor. Wir nehmen immer den anderen Ausgang (Modus := 2). Wirkung:
    ///   eine Einheit, die neben einer Lage-99-Zelle steht, fährt nicht noch
    ///   einmal weiter. <b>Was Lage 99 markiert, ist inzwischen halb gelesen</b>
    ///   — genau EIN Schreiber (<c>0x43CB12</c>) setzt sie, in der Gebäudeuhr,
    ///   zusammen mit <c>imap := 0xFFFE</c> auf der Zelle
    ///   <c>(spalte + b[+0x35], zeile + b[+0x36])</c> und <c>b[+0x0A] := 1</c>:
    ///   das sieht nach der TORZELLE eines Gebäudes aus, und der Arm hiesse
    ///   dann »geh dem Tor aus dem Weg«. ⚠ Deutung, nicht belegt.</item>
    /// </list>
    ///
    /// <para><c>--sektormaschine-alt</c> stellt den Stand vom 31.08.2026 wieder
    /// her (die Brücke <see cref="AiZustandVorlaeufig"/> plus der
    /// Ein-Sektor-Angriff).</para>
    /// </summary>
    private void AiZustandsmaschine(AiPlayer a)
    {
        int p = a.Player;
        var r = _aiRaster[p];
        _aiKandidaten.Clear();

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p) continue;

            switch (e.AiCpu0)
            {
                // ---- 0: frei ------------------------------------- 0x4BBE10 --
                case 0:
                    if (AiSteht(e)) _aiKandidaten.Add(i);
                    break;

                // ---- 1: zugewiesen, marschiert ------------------- 0x4BBE3A --
                case 1:
                    r[AiSektorAus(e.AiCpu1)].Belegt++;
                    if (!AiSteht(e)) break;
                    // ⚠ hier faellt der Lage-99-Griff aus, siehe oben.
                    e.AiCpu0 = 2;
                    a.SmBereit++;
                    break;

                // ---- 2: abrufbereit ------------------------------ 0x4BBFBA --
                case 2:
                {
                    var (cx, cy) = AiSektorVon(e.Col, e.Row);
                    r[SektorIndex(cx, cy)].Belegt++;
                    if (a.Roll(200) == 111)          // rand() % 200 == 0x6F
                    {
                        e.AiCpu0 = 0;
                        e.AiCpu1 = 0;
                        a.SmAbkuehlung++;
                    }
                    break;
                }

                // ---- 3: im Angriff ------------------------------- 0x4BC029 --
                case 3:
                {
                    if (AiSteht(e)) { e.AiCpu0 = 0; e.AiCpu1 = 0; a.SmFertig++; break; }
                    var (cx, cy) = AiSektorVon(e.Col, e.Row);
                    int ax = e.AiCpu1 & 0x0F, ay = (e.AiCpu1 >> 4) & 0x0F;
                    if (Math.Abs(ax - cx) > 1 || Math.Abs(ay - cy) > 1)
                    {
                        AiAnhalten(i, e);            // UKOL := 0
                        e.AiCpu0 = 0;
                        e.AiCpu1 = 0;
                        a.SmLeine++;
                    }
                    break;
                }
            }
        }

        // ---- die Zuweisung ------------------------------- 0x4BC0C0..0x4BC208
        int n = Math.Min(_aiKandidaten.Count, 5);
        if (n == 0) return;

        int paar = -1, min = 9999;                    // di := 0x270F
        for (int sx = 0; sx < SektorKante; sx++)
            for (int sy = 0; sy < SektorKante; sy++)
            {
                var s = r[SektorIndex(sx, sy)];
                if (s.Def == 0) continue;             // Bedarf 0 -> kein Sektor
                int po = 100 * s.Belegt / s.Def;
                if (po >= min) continue;              // cmp ax, di / jge
                min = po;
                paar = (sy << 4) | sx;
            }
        if (paar < 0) { a.SmKeinSektor++; return; }

        int zx = paar & 0x0F, zy = (paar >> 4) & 0x0F;
        for (int k = 0; k < n; k++)
        {
            var e = _entities[_aiKandidaten[k]];
            e.AiCpu0 = 1;
            e.AiCpu1 = paar;
            a.SmZuweisungen++;
            AiWalkTo(_aiKandidaten[k],
                     new Vector2I(zx * SektorFeld + a.Roll(14) + 5,
                                  zy * SektorFeld + a.Roll(14) + 5));
        }
    }

    private readonly List<int> _aiKandidaten = new();

    /// <summary><b>UNSERE Lesart von <c>UKOL == 0</c></b> — »die Einheit steht
    /// und hat nichts vor«. Dieselbe Prüfung, die der Sektorangriff seit dem
    /// 30.08.2026 benutzt; sie steht hier EINMAL, damit eine spätere, echte
    /// UKOL-Führung nur diese Zeile trifft.
    ///
    /// <para>⚠⚠ <b>01.09.2026 — UND <c>Ukol</c> SELBST GEHÖRT DAZU.</b>
    /// Gemeldet: »Einheiten die im Depot sind, sind am Anfang wie
    /// Geistereinheiten, die ich dann aus der Basis rausfahren sehe — also
    /// diese Wegpunkte, aber da ist gar keine Einheit«. Und genau so war es:
    /// eine untergestellte Einheit hat weder Weg noch Ziel noch Befehlsliste,
    /// stand also in der Kandidatenliste, bekam einen Sektor zugewiesen und
    /// fuhr los — unsichtbar, denn das Zeichnertor hängt an
    /// <c>UKOL == 0x32</c> (@0x4300E2).</para>
    ///
    /// <para><b>Das Original hat diesen Fehler nicht</b>, und zwar mit genau
    /// der Bedingung, die hier fehlte: der Modus-0-Arm fragt <c>UKOL == 0</c>,
    /// und eine untergestellte Einheit trägt dort <b>0x32</b> (@0x43D657), eine
    /// an der Tür angemeldete 0x30, eine ausfahrende 0x33. Unsere drei
    /// Einfahrt-Werte sind damit dieselbe Sperre wie im Original.</para></summary>
    private static bool AiSteht(Entity e)
        => e.Ukol == 0 && e.Path == null && e.Target < 0 && e.Orders.Count == 0;

    /// <summary><c>UKOL := 0</c> — die Leine des Modus 3 bricht den Auftrag ab
    /// (<c>0x4BC097</c>), sie schickt die Einheit NICHT zurück.</summary>
    private void AiAnhalten(int idx, Entity e)
    {
        e.Path = null;
        e.Target = -1;
        e.Orders.Clear();
        e.Ordered = false;
        // ⚠⚠ Und die Vormerkung MIT — sonst bleibt die vorgemerkte Zelle fuer
        // immer belegt. Siehe AiVormerkungLoesen; die Leine schlug in 60 s
        // Kampagne 3 zehnmal zu, das waeren zehn Phantome je Minute.
        AiVormerkungLoesen(idx, e);
    }

    // ---- Takt 7, dritte Haelfte: der Gruppenangriff -------------------------

    /// <summary>
    /// ⭐⭐⭐ <b>DER GRUPPENANGRIFF</b> — <c>0x4BC540</c> (F <c>0x4BC000</c>),
    /// vollständig zerlegt am 01.09.2026. <c>0x4BC900</c> (Takt 7) ist nur der
    /// Rahmen: erst <c>0x4BBB80</c> (Bedarf + Zustandsmaschine), dann diese
    /// Funktion.
    ///
    /// <code>
    ///   fuer sx = 0…10, sy = 0…10:
    ///       wenn belegt[sx,sy] &lt;= 0: naechster Sektor        ; word +0x0A @0x4BC5B1
    ///       fuer n = 0…8:                                      ; Tafel 0x538C10
    ///           (nx,ny) = (sx,sy) + Δ[n];  ausserhalb 0…10 -> weiter
    ///           wenn feind[nx,ny] == 0: weiter                 ; word +0x04 @0x4BC629
    ///           ziel = get_target_in_sector(p, nx, ny)         ; 0x4BC3D0
    ///           wenn ziel == 0xFFFF: weiter
    ///           fuer jede EIGENE Einheit im Sektor (sx,sy) mit
    ///               faze != 0xFF, Waffe (+0x0D) != 0, +0x0F != 0xAB,
    ///               Modus ∈ {1,2}, x/24 == sx, y/24 == sy:
    ///                   order(einheit, ziel)                   ; UKOL 4
    ///                   Modus := 3                             ; @0x4BC7EB
    /// </code>
    ///
    /// <para><b>⭐ Das ist die Antwort auf die Streifenfrage</b> und auf
    /// bug-013: es gibt keine Streife. Es gibt den Sichtring (3…5 Zellen) und
    /// dieses Raster — <b>Sektorkante 24 Zellen, neun Nachbarn, also bis rund 48
    /// Zellen weit</b>. Und es erklärt »vor der Brücke greifen nicht alle an«:
    /// losgeschickt werden nur die abrufbereiten Einheiten <b>DES EINEN
    /// Sektors</b>, in dem der Feind gesehen wird. Einheiten des Nachbarsektors
    /// bleiben stehen, bis IHR Sektor Feindstärke sieht — das kommt also auch im
    /// Original vor.</para>
    ///
    /// <para>⚠ Das Ziel wird <b>einmal je (Sektor, Nachbar)</b> geholt, nicht je
    /// Einheit (Merker <c>byte[esp+0x13]</c>, gesetzt @<c>0x4BC638</c>, gelöscht
    /// @<c>0x4BC7BE</c>) — die ganze Gruppe greift also DASSELBE Ziel an.</para>
    ///
    /// <para>⚠ <b>NICHT gebaut:</b> der Alarmklang 0x7A für den Menschen
    /// (<c>0x4BC753…0x4BC7B7</c>: nur wenn <c>word[0xBCA0E0] &lt; 50</c>, der
    /// Abklingzähler <c>word[0x538BAC]</c> auf 0 steht, <c>word[0x539934] !=
    /// 14</c> und die Bündnisspalte 0 ist; danach Abklingzeit
    /// <c>rand%3000 + 4000</c>), und das <c>+0x0F != 0xAB</c>-Tor, dessen
    /// Bedeutung ungelesen ist.</para>
    /// </summary>
    private void AiGruppenangriff(AiPlayer a)
    {
        int p = a.Player;
        var r = _aiRaster[p];

        for (int sx = 0; sx < SektorKante; sx++)
            for (int sy = 0; sy < SektorKante; sy++)
            {
                if (r[SektorIndex(sx, sy)].Belegt <= 0) continue;

                for (int n = 0; n < AiNachbarn.Length; n++)
                {
                    int nx = sx + AiNachbarn[n].dx, ny = sy + AiNachbarn[n].dy;
                    if (nx < 0 || nx >= SektorKante || ny < 0 || ny >= SektorKante) continue;
                    if (r[SektorIndex(nx, ny)].Feind == 0) continue;

                    a.SektorVersuche++;
                    int ziel = AiZielImSektor(p, nx, ny);
                    if (ziel < 0) { a.SektorKeinZiel++; continue; }

                    int los = 0;
                    for (int i = 0; i < _entities.Count; i++)
                    {
                        var e = _entities[i];
                        if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p) continue;
                        if (!CanFight(e)) continue;              // +0x0D != 0
                        if (e.AiCpu0 is not (1 or 2)) continue;  // das Modus-Tor
                        var (ex, ey) = AiSektorVon(e.Col, e.Row);
                        if (ex != sx || ey != sy) continue;

                        AiSend(i, ziel);
                        e.AiCpu0 = 3;
                        a.Wave.Add(i);
                        los++;
                    }
                    if (los > 0) { a.Waves++; a.SektorAngriffe++; a.TargetIdx = ziel; }
                }
            }
    }

    // ---- get target in sector ----------------------------------------------

    /// <summary>
    /// <c>get target in sector</c> — C <c>0x4BC3D0</c> / F <c>0x4BBE90</c>.
    ///
    /// <para>⭐ <b>Die Ordnung ist die Einheitennummer, aufsteigend.</b> Nicht
    /// der nächste, nicht der schwächste, nicht der wertvollste Gegner — der
    /// erste, der die Prüfung besteht. Da die Nummer der Reihenfolge der
    /// Kartenanlage entspricht, greift die KI faktisch die Einheit an, die der
    /// <b>Kartenbauer zuerst gesetzt hat</b>.</para>
    ///
    /// <para>Die einzige Auslese davor ist der Sprung über ganze Spielerblöcke
    /// (<c>si += 1000</c>), für die die Bündniszeile ≠ 0 ist.</para>
    ///
    /// <para>Geprüft wird je Einheit: <c>faze != 0xFF</c> (lebt),
    /// <c>RX/24 == sx</c>, <c>RY/24 == sy</c>, <c>+0x0A &lt; 4</c> (Landeinheit,
    /// <b>kein Schiff</b>) und <c>UKOL &lt; 45</c>.</para>
    /// </summary>
    private int AiZielImSektor(int p, int sx, int sy)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead) continue;
            if (AiVerbuendet(p, e.Owner)) continue;      // der Spielerblock-Sprung
            var (ex, ey) = AiSektorVon(e.Col, e.Row);
            if (ex != sx || ey != sy) continue;
            if (!e.Mobile) continue;                     // Landeinheit, kein Schiff
            return i;
        }
        return -1;
    }

    /// <summary>Wieviele Einheiten dem Sektor zugeordnet sind bzw. wieviele er
    /// braucht — für die Statuszeile und den Prüfstand.</summary>
    public (int belegt, int def, int imp) AiSektorStand(int player, int sx, int sy)
    {
        if (player is < 0 or > 7) return (0, 0, 0);
        var s = _aiRaster[player][SektorIndex(
            Math.Clamp(sx, 0, SektorKante - 1), Math.Clamp(sy, 0, SektorKante - 1))];
        return (s.Belegt, s.DefRobots, s.Imp);
    }

    /// <summary>Die Zahl der freien Angreifer eines Spielers — für den Prüfstand.</summary>
    public int AiFreieAngreiferStand(int player)
        => player is >= 0 and <= 7 ? AiFreieAngreifer(player, out _, out _) : 0;

    // ---- Der Pruefstand ----------------------------------------------------

    /// <summary>Die Auswahlregel als reine Rechnung, damit sie ohne Karte
    /// messbar ist: der kleinste <c>pway / imp</c> gewinnt.</summary>
    private static int PoWahl((int imp, int pway)[] k)
    {
        int best = -1, min = int.MaxValue;
        for (int i = 0; i < k.Length; i++)
        {
            if (k[i].imp <= 0) continue;
            int po = k[i].pway / k[i].imp;
            if (po >= min) continue;
            min = po; best = i;
        }
        return best;
    }

    /// <summary>Die ALTE Regel, die bis zum 21.08.2026 in Kraft war: nimm den
    /// grössten <c>Priority</c>. Sie steht hier als <b>Nullmodell</b> — ohne sie
    /// wäre nicht zu sehen, ob die Änderung überhaupt etwas bewirkt.</summary>
    private static int MaxWahl((int imp, int pway)[] k)
    {
        int best = -1, max = int.MinValue;
        for (int i = 0; i < k.Length; i++)
            if (k[i].imp > max) { max = k[i].imp; best = i; }
        return best;
    }

    /// <summary>
    /// <c>--sektor-check</c> — <b>rechnet der Gegner jetzt wie das Original?</b>
    ///
    /// <para>Fünf Messlatten, jede mit ihrem Nullmodell. Gemessen wird nicht,
    /// DASS etwas passiert, sondern <b>wie oft es anders ausgeht als vorher</b> —
    /// eine Umstellung, die in keiner Probe eine andere Entscheidung trifft, hat
    /// nichts umgestellt.</para>
    /// </summary>
    public string SektorCheck()
    {
        var sb = new System.Text.StringBuilder("sektor-check\n");
        bool alles = true;

        // ---- 1. po = pway / imp gegen die alte Regel »groesstes imp« --------
        const int Proben = 20000;
        int anders = 0, gleich = 0;
        var zufall = new Random(4711);
        for (int n = 0; n < Proben; n++)
        {
            int m = 2 + zufall.Next(6);
            var k = new (int imp, int pway)[m];
            for (int i = 0; i < m; i++) k[i] = (1 + zufall.Next(9), zufall.Next(21));
            if (PoWahl(k) == MaxWahl(k)) gleich++; else anders++;
        }
        double quote = (double)anders / Proben;
        // Die Regeln muessen sich unterscheiden, aber nicht in JEDEM Fall: bei
        // gleichem Weg entscheidet auch po nach der Wichtigkeit.
        bool ok1 = quote is > 0.25 and < 0.95;
        alles &= ok1;
        sb.Append($"  1. po=pway/imp gegen »groesstes imp«: {anders}/{Proben} " +
                  $"({quote:P1}) andere Wahl, {gleich} gleiche  {(ok1 ? "ok" : "FEHLT")}\n");
        sb.Append($"     Nullmodell: waere die alte Regel noch in Kraft, stuende hier 0/{Proben}.\n");

        // ---- 2. Ein Fall von Hand, damit die Richtung stimmt ---------------
        // Ziel A: Wichtigkeit 9, Weg 18  -> po = 2
        // Ziel B: Wichtigkeit 2, Weg  2  -> po = 1   <- gewinnt, obwohl unwichtiger
        var fall = new[] { (9, 18), (2, 2) };
        bool ok2 = PoWahl(fall) == 1 && MaxWahl(fall) == 0;
        alles &= ok2;
        sb.Append($"  2. Naeher schlaegt wichtiger: po waehlt {PoWahl(fall)}, " +
                  $"alte Regel waehlte {MaxWahl(fall)}  {(ok2 ? "ok" : "FEHLT")}\n");

        // ---- 3. pro_style: der sechste Auslieferungsunterschied -------------
        int[] proF = { 1, 30, 50, 100, 200, 255, 0, 0 };
        int defC = Math.Min(100, 100 * 100 / ProStyleC[4]);
        int defF = Math.Min(100, 100 * 100 / proF[4]);
        bool ok3 = defC == 25 && defF == 50;
        alles &= ok3;
        sb.Append($"  3. pro_style Betriebsart 4 bei DEF=100: C haelt {defC} zurueck, " +
                  $"F {defF}  {(ok3 ? "ok" : "FEHLT")}\n");
        sb.Append($"     Wir folgen C (22.01.1998), der spaeteren Fassung.\n");

        // ---- 4. Die Gruppengrenze: 4 Plaetze, 100 Einheiten -----------------
        for (int g = 0; g < 4; g++) { _aiGruppen[0][g].Einheiten.Clear(); _aiGruppen[0][g].Auftrag = null; }
        int belegt = 0;
        for (int v = 0; v < 6; v++)
        {
            // von Hand belegen, damit der Pruefstand keine Karte braucht
            int frei = -1;
            for (int g = 0; g < 4; g++) if (_aiGruppen[0][g].Einheiten.Count == 0) { frei = g; break; }
            if (frei < 0) continue;
            _aiGruppen[0][frei].Einheiten.Add(v);
            belegt++;
        }
        bool ok4 = belegt == 4 && AiFreieGruppen(0) == 0;
        alles &= ok4;
        sb.Append($"  4. Gruppenplaetze: {belegt} von 6 Versuchen belegt, " +
                  $"{AiFreieGruppen(0)} frei  {(ok4 ? "ok" : "FEHLT")}\n");
        sb.Append($"     »Attack group not available« ist STUMM und kehrt ohne Wirkung " +
                  $"zurueck — die 5. Welle faellt ersatzlos aus.\n");
        for (int g = 0; g < 4; g++) { _aiGruppen[0][g].Einheiten.Clear(); _aiGruppen[0][g].Auftrag = null; }

        // ---- 5. Der Sektorindex klemmt auf echten Karten nie ----------------
        int geklemmt = 0;
        for (int c = 0; c <= 253; c++)
        {
            var (sx, _) = AiSektorVon(c, 0);
            if (sx != c / SektorFeld) geklemmt++;
        }
        bool ok5 = geklemmt == 0;
        alles &= ok5;
        sb.Append($"  5. Sektorindex 0..253: {geklemmt} geklemmt  {(ok5 ? "ok" : "FEHLT")}\n");
        sb.Append($"     11 x 24 = 264 >= 254, also greift die Klemme auf keiner " +
                  $"moeglichen Karte.\n");

        // ---- 6. Was auf DIESER Karte im Raster steht -----------------------
        if (_entities.Count > 0)
        {
            for (int p = 0; p < 8; p++)
            {
                AiZustandVorlaeufig(p);
                AiStaerkeraster(p);
                AiSetImpCpu(p);
                int freie = AiFreieAngreifer(p, out int sx, out int sy);
                if (_aiSec110[p] == 0 && freie == 0) continue;
                sb.Append($"     P{p}: sec110={_aiSec110[p]}, freie Angreifer={freie}, " +
                          $"groesster Ueberschuss in Sektor ({sx},{sy}), " +
                          $"Betriebsart={_aiSec61[p]}\n");
            }
        }

        // ---- 7. ⭐ GIBT DIE GRUPPE IHRE EINHEITEN WIEDER FREI? -------------
        //
        // ⚠⚠ Die Messlatte, die am 21.08.2026 GEFEHLT HAT — und deren Fehlen
        // den Gegner nach wenigen Wellen erstarren liess. AiGruppeBilden setzt
        // jedem Mitglied CPU0 = 10; gezaehlt werden in Set imp cpu: aber nur
        // die mit CPU0 1 oder 2. Ohne Aufloesung fallen sie DAUERHAFT aus der
        // Rechnung, die Zahl der freien Angreifer sinkt mit jeder Welle, und
        // irgendwann faehrt gar nichts mehr los. Genau das sieht ein Spieler
        // als »die Kampagne tut nichts mehr« — und KEINE der sechs Messlatten
        // darueber haette es bemerkt.
        int mitP = -1;
        for (int q = 0; q < 8 && mitP < 0; q++)
        {
            AiZustandVorlaeufig(q);
            AiStaerkeraster(q);
            AiSetImpCpu(q);
            if (AiFreieAngreifer(q, out _, out _) >= 3) mitP = q;
        }
        if (mitP >= 0)
        {
            int vorher = AiFreieAngreiferStand(mitP);
            var ziel = new MissionTarget { Kind = 1, Priority = 6, Word = 0, Second = 0 };
            int g = AiGruppeBilden(mitP, ziel, 2);
            int inGruppe = g >= 0 ? _aiGruppen[mitP][g].Einheiten.Count : 0;
            int auf10 = 0;
            foreach (var e in _entities) if (e.Owner == mitP && e.AiCpu0 == 10) auf10++;

            if (g >= 0) AiGruppeAufloesen(mitP, g);
            int auf10danach = 0;
            foreach (var e in _entities) if (e.Owner == mitP && e.AiCpu0 == 10) auf10danach++;
            AiZustandVorlaeufig(mitP);
            AiStaerkeraster(mitP);
            AiSetImpCpu(mitP);
            int nachher = AiFreieAngreiferStand(mitP);

            bool ok7 = inGruppe > 0 && auf10 == inGruppe && auf10danach == 0 &&
                       nachher == vorher;
            alles &= ok7;
            sb.Append($"  7. Gruppe bilden und aufloesen (P{mitP}): {inGruppe} Einheiten, " +
                      $"{auf10} auf CPU0=10, danach {auf10danach}  {(ok7 ? "ok" : "FEHLT")}\n");
            sb.Append($"     freie Angreifer {vorher} -> {nachher} (muss gleich sein)" +
                      (nachher == vorher ? "" : "  ⚠ VERSICKERT") + "\n");
            sb.Append($"     Nullmodell: OHNE Aufloesung stuenden hier {inGruppe} auf CPU0=10 " +
                      $"und die freien Angreifer waeren um {inGruppe} gefallen.\n");
        }
        else
        {
            sb.Append("  7. Gruppenaufloesung: kein Spieler mit >= 3 freien Angreifern " +
                      "auf dieser Karte — nicht gemessen\n");
        }

        sb.Append(alles ? "  ALLE MESSLATTEN GETROFFEN\n" : "  ⚠ MINDESTENS EINE MESSLATTE VERFEHLT\n");
        return sb.ToString();
    }
}
