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
    /// <para>⚠ <b>UNSERE Stärkeformel.</b> Dass es drei Eimer sind (eigene,
    /// verbündete, feindliche Stärke) steht fest; <b>womit</b> das Original eine
    /// Einheit gewichtet, ist nicht gelesen — <c>0x4BA710</c> haben wir nur als
    /// Ein- und Ausgang. Wir zählen darum schlicht die kampffähigen Einheiten.
    /// Das ist eine Setzung, keine Wiederherstellung, und sie ist hier so
    /// gekapselt, dass eine spätere Lesung nur diese eine Zeile ändert.</para>
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
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
            if (!CanFight(e)) continue;

            var (sx, sy) = AiSektorVon(e.Col, e.Row);
            int s = SektorIndex(sx, sy);
            int wert = 1;                       // ⚠ UNSERE Setzung, siehe oben

            if (e.Owner == p) r[s].Eigen += wert;
            else if (AiVerbuendet(p, e.Owner)) r[s].Verbuendet += wert;
            else r[s].Feind += wert;
        }
    }

    /// <summary>Die Bündniszeile <c>sec53[40·p + 0x15 + q]</c>. Ein Spieler ist
    /// sich immer selbst freund; alles andere kommt aus der Diplomatie, die wir
    /// im Gefecht noch nicht führen — bis dahin ist jeder Fremde ein Feind, und
    /// das ist die Voreinstellung des Originals für ein Gefecht ohne Bündnisse
    /// (<c>netzstart</c> setzt nur die Diagonale, <c>0x41952E</c>).</summary>
    private bool AiVerbuendet(int p, int q) => p == q || AiMannschaft(p) == AiMannschaft(q);

    /// <summary>Die Mannschaft eines Platzes. Ohne Bündnissystem ist jeder seine
    /// eigene — dann fällt <see cref="AiVerbuendet"/> auf <c>p == q</c> zurück.</summary>
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

        // Schritt 4 — die zugeordneten Einheiten.
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p || !e.Mobile) continue;
            if (e.AiCpu0 is 1 or 2)
            {
                int s = SektorIndex(Math.Clamp(e.AiCpu1 & 0x0F, 0, SektorKante - 1),
                                    Math.Clamp((e.AiCpu1 >> 4) & 0x0F, 0, SektorKante - 1));
                r[s].Belegt++;
            }
        }
    }

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
    /// </summary>
    private int AiImpVon(Entity e)
    {
        if (e.Owner is < 0 or > 7) return 0;
        var tafel = _aiImp[e.Owner];
        if (tafel != null && e.Slot >= 0 && e.Slot < tafel.Length && tafel[e.Slot] != 0)
            return tafel[e.Slot];
        return InCampaign ? 0 : 6;
    }

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
        public int Auftrag = -1;
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
    private int AiGruppeBilden(int p, int auftrag, int po)
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

        if (gruppe.Einheiten.Count == 0) { gruppe.Auftrag = -1; return -1; }
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
            gruppe.Einheiten.RemoveAll(i =>
                i >= _entities.Count || _entities[i].Dead || _entities[i].Owner != p);
            if (gruppe.Einheiten.Count == 0) gruppe.Auftrag = -1;
        }
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
        for (int g = 0; g < 4; g++) { _aiGruppen[0][g].Einheiten.Clear(); _aiGruppen[0][g].Auftrag = -1; }
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
        for (int g = 0; g < 4; g++) { _aiGruppen[0][g].Einheiten.Clear(); _aiGruppen[0][g].Auftrag = -1; }

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

        sb.Append(alles ? "  ALLE MESSLATTEN GETROFFEN\n" : "  ⚠ MINDESTENS EINE MESSLATTE VERFEHLT\n");
        return sb.ToString();
    }
}
