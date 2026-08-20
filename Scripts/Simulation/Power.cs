namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>DIE STROMABRECHNUNG</b> — <c>@0x440270</c>, und das Spiel nennt den Block
/// selbst: <c>'Power'</c> / <c>'Power end'</c> (0x4F7D0C / 0x4F7D00).
/// Antwort auf Fehler <b>C10</b>.
///
/// <para><b>Sie läuft jede Sekunde</b>: der Aufrufer @0x4161C4 prüft
/// <c>[0x4FA240] % 50 == 13</c> — also alle 50 Originaltakte, eine feste Phase
/// wie beim Dock-Auslauf.</para>
///
/// <para><b>Wer erzeugt, wer verbraucht</b> (Verteiler 0x440660 über
/// <c>typ − 2</c>, Bereich 2..15 — alles andere zählt gar nicht mit):</para>
/// <list type="bullet">
///   <item><b>Kraftwerk (13): +90, GLOBAL</b> und ohne Besitzer (@0x44038D
///   <c>add ebx, 0x5A</c>). Das passt zu den Daten: alle 262 Kraftwerke der
///   Karten tragen Besitzer 255.</item>
///   <item><b>Generator (7): +50, JE SPIELER</b> (@0x440344, nach
///   <c>0x879E60 + 2·Spieler</c>).</item>
///   <item><b>Fabriken (2,3,4) und Minen (10,15)</b> sind der Bedarf. Jede
///   trägt ihren <b>Nennwert</b> ein — <c>byte[Tafel[cis_typ] + 0x02]</c>, bei
///   der Fabrik aus 0x87A2C0 (Schrittweite 14), bei der Mine aus 0x878AD0
///   (Schrittweite 18) — und zwar zweimal: in den Bedarf des Spielers
///   (<c>0x87A582 + 4·Spieler</c>) und in den globalen.</item>
/// </list>
///
/// <para>⚠ Ein Gebäude mit <b>Zustand 1</b> wird übersprungen (@0x44031C,
/// @0x440369) — es ist beschäftigt und zählt weder als Verbraucher noch
/// bekommt es Strom.</para>
///
/// <para><b>Daraus zwei Prozentsätze:</b></para>
/// <code>
///   GlobalProzent  = 100 · KraftwerkeGesamt / BedarfGesamt      (@0x4403AC)
///   SpielerProzent = 100 · Generatoren[p]   / Bedarf[p]         (@0x4403E7)
///   — beide 100, wenn der Nenner 0 ist
/// </code>
///
/// <para><b>Und die Wirkung</b> (@0x440527, zweiter Durchgang über dieselben
/// Gebäude):</para>
/// <code>
///   wirksam = Nennwert · (SpielerProzent + GlobalProzent) / 100
///   Ist[Spieler] += wirksam                    ⚠ UNGEDECKELT
///   Tafel[cis].+0x01 = min(wirksam, Nennwert)  ⚠ GEDECKELT
/// </code>
/// <para>Die zwei Prozentsätze werden also <b>addiert</b>: eigene Generatoren
/// und die herrenlosen Kraftwerke helfen beide, und zusammen können sie über
/// 100 % kommen — gedeckelt wird erst beim Eintrag ins Gebäude.</para>
///
/// <para>⚠⚠ <b>DER FUND, an dem der ganze Einbau hängt:</b> die wirksame
/// Leistung ist im Original <b>keine Periodenskalierung</b>, sondern eine
/// <b>Würfelschwelle</b>. Der Fabriktakt @0x43DF33 rechnet
/// <c>Prozent = 100·wirksam/Nennwert</c> (bzw. 100, wenn wirksam ≥ Nennwert)
/// und verwirft den Fertigungsschritt bei <c>rand()%100 &gt; Prozent</c>
/// (@0x43DF82). <b>Das ist genau die Form, die unser <c>EffNum/EffDen</c>
/// schon hat</b> — und mehr noch: <c>EffNum</c> IST <c>wirksam</c> und
/// <c>EffDen</c> IST der Nennwert, beide aus sec24 +0x03/+0x04 (Fabrik) bzw.
/// sec28 +0x03/+0x04 (Mine), also aus denselben zwei Bytes.</para>
///
/// <para><b>Es fehlte also nie die Mechanik, sondern nur ihr Nachführen:</b>
/// <c>EffNum</c> stand seit dem Laden der Karte still. Strommangel bremst
/// damit <b>anteilig, er stoppt nicht</b> — genau wie im Original.</para>
///
/// <para>⚠ <b>Was UNSER ist:</b> dass die Mine überhaupt eine Förderchance
/// würfelt, war bei uns bisher nicht gebaut (<c>MineRate</c> lief flach); die
/// Chance selbst ist gelesen (@0x43E57F benutzt dieselben zwei Bytes). Und die
/// Förder<b>menge</b> von 5 je Takt bleibt unsere Setzung, wie bisher.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Ein Kraftwerk (Typ 13) liefert <b>90</b>, und zwar
    /// <b>global</b> — @0x44038D.</summary>
    private const int PowerPlantOutput = 90;

    /// <summary>Ein Generator (Typ 7) liefert <b>50</b> an SEINEN Spieler —
    /// @0x440340, <c>edi</c> steht dort auf 0x32.</summary>
    private const int PowerGeneratorOutput = 50;

    /// <summary>Die Abrechnung läuft bei <c>Takt % 50 == 13</c> (@0x4161C4),
    /// also einmal je Sekunde des Originals.</summary>
    private const int PowerPeriod = 50, PowerPhase = 13;

    /// <summary>Der Zustand, bei dem ein Gebäude übersprungen wird
    /// (@0x44031C).</summary>
    private const int PowerSkipState = 1;

    /// <summary>Was der laufende Betrieb erbringt bzw. verlangt, je Spieler —
    /// <c>word[0x87A580 + 4p]</c> und <c>word[0x87A582 + 4p]</c>.
    ///
    /// <para>⚠⚠ <b>DIE BENENNUNG IST BERICHTIGT.</b> Im Kopf von
    /// <c>UI/GameHud.cs</c> stand »+0x00 Stromverbrauch, +0x02
    /// Stromproduktion«, und das ist <b>vertauscht</b>: den Bedarf tragen die
    /// FABRIKEN nach <c>+0x02</c> ein (@0x44032E), und nach <c>+0x00</c>
    /// schreibt erst der zweite Durchgang die <b>erbrachte</b> Leistung
    /// (@0x440543). Die dortige Rechnung war richtig gelesen, nur die zwei
    /// Namen standen über Kreuz — und aus vertauschten Namen wäre hier eine
    /// vertauschte Mechanik geworden.</para></summary>
    private readonly int[] _powerDone = new int[8], _powerNeed = new int[8];

    /// <summary><c>dword[0x878AB0 + 4p]</c> — 100 · eigene Erzeugung / eigener
    /// Bedarf.</summary>
    private readonly int[] _powerOwn = new int[8];

    /// <summary>100 · Kraftwerke / Bedarf, für alle gleich.</summary>
    private int _powerGlobal = 100;

    /// <summary><c>dword[0x4FAD14]</c> — die Summe beider Prozentsätze des
    /// Sichtspielers beim LETZTEN Lauf. Nur dafür da, die zwei Meldungen
    /// flankengesteuert zu machen.</summary>
    private int _powerLast = 200;

    /// <summary>Wie oft die Abrechnung gelaufen ist. ⚠ Regel 33: ohne diese
    /// Zahl ist »kein Strommangel« nicht von »die Abrechnung lief nie« zu
    /// unterscheiden.</summary>
    public int PowerRuns;

    /// <summary>Die zwei Prozentsätze des Sichtspielers, für Leiste und
    /// Prüfstand.</summary>
    public (int Own, int Global, int Done, int Need) PowerOf(int player)
    {
        int p = player is >= 0 and <= 7 ? player : 0;
        return (_powerOwn[p], _powerGlobal, _powerDone[p], _powerNeed[p]);
    }

    /// <summary>Trägt dieses Gebäude zum Strombedarf bei? Fabriken und Minen —
    /// die Typen, die der Verteiler auf die zwei Bedarfszweige schickt.</summary>
    private static bool PowerConsumer(Entity b) => b.BType is 2 or 3 or 4 or 10 or 15;

    /// <summary>
    /// Ein Lauf der Abrechnung. Gehört in <see cref="OriginalTick"/>, bei
    /// <c>% 50 == 13</c>.
    /// </summary>
    private void PowerTick()
    {
        PowerRuns++;
        var gen = new int[8];
        int plants = 0, needAll = 0;
        for (int p = 0; p < 8; p++) { _powerDone[p] = 0; _powerNeed[p] = 0; }

        // ---- erster Durchgang: wer erzeugt, wer verlangt (@0x4402DA) ----
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (b.BType == 13) { plants += PowerPlantOutput; continue; }   // ohne Besitzer!
            if (b.Owner is < 0 or > 7) continue;
            if (b.State == PowerSkipState) continue;
            if (b.BType == 7) { gen[b.Owner] += PowerGeneratorOutput; continue; }
            if (!PowerConsumer(b)) continue;
            int nenn = Mathf.Max(0, b.EffDen);
            _powerNeed[b.Owner] += nenn;
            needAll += nenn;
        }

        _powerGlobal = needAll == 0 ? 100 : 100 * plants / needAll;
        for (int p = 0; p < 8; p++)
            _powerOwn[p] = _powerNeed[p] == 0 ? 100 : 100 * gen[p] / _powerNeed[p];

        // ---- zweiter Durchgang: die Wirkung (@0x4404C0) ----
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (b.Owner is < 0 or > 7) continue;
            if (b.State == PowerSkipState || !PowerConsumer(b)) continue;
            int nenn = Mathf.Max(0, b.EffDen);
            int wirksam = nenn * (_powerOwn[b.Owner] + _powerGlobal) / 100;
            _powerDone[b.Owner] += wirksam;                 // ⚠ ungedeckelt
            b.EffNum = Mathf.Min(wirksam, nenn);            // ⚠ gedeckelt
        }

        PowerMessages();
    }

    /// <summary>
    /// Die zwei Meldungen, <b>flankengesteuert</b> (@0x44043E / @0x44048B).
    ///
    /// <para><b>124</b> geht heraus, wenn die Summe der zwei Prozentsätze unter
    /// 100 fällt, <b>125</b>, wenn sie zurückkommt. Verglichen wird gegen den
    /// Wert des letzten Laufs (<c>dword[0x4FAD14]</c>).</para>
    ///
    /// <para>⚠ Und das Original ist dabei sparsam: solange es schon knapp ist,
    /// wiederholt es die Meldung nur in <b>einem von fünfzig</b> Läufen
    /// (@0x440427, <c>rand() % 50 == 1</c>) — sonst käme sie jede Sekunde. Vor
    /// Takt <b>300</b> schweigt es ganz (@0x440416).</para></summary>
    private void PowerMessages()
    {
        if (_origTicks <= 300) { _powerLast = PowerSum(); return; }
        int jetzt = PowerSum();
        // ⚠⚠ 20.08.2026 — HIER STAND `Simulation.Determinism.Roll(50)`, ALSO
        // DER GEMEINSAME WÜRFEL, UND DAS WAR EIN DESYNC.
        //
        // `||` verkürzt: der Wurf wird NUR gezogen, wenn `_powerLast < 100` —
        // und `_powerLast` ist `PowerSum()`, also `_powerOwn[ViewPlayer] +
        // _powerGlobal`. Zwei Maschinen im Lockstep haben verschiedene
        // Sichtspieler, kommen damit auf verschiedene `_powerLast` und ziehen
        // VERSCHIEDEN OFT aus dem gemeinsamen Strom. Ab dem ersten Lauf, in dem
        // der eine knapp ist und der andere nicht, würfeln beide Maschinen den
        // Rest der Partie versetzt — und das trifft dann Produktion,
        // Trefferstreuung und Markt, nicht bloss eine Ansage.
        //
        // Aufgefallen beim Anschliessen des Klangs, nicht beim Zwillingslauf:
        // der prüft mit gleichem Sichtspieler auf einer Maschine, und genau
        // dieser Fall ist der einzige, in dem es NICHT auffällt.
        //
        // Die Drosselung gehört ohnehin nicht in den Simulationsstrom: sie
        // entscheidet, wie oft der Mensch am Schirm eine Ansage hört. Das ist
        // Darstellung, also ein eigener, ungekeimter Würfel. ⚠ Damit weicht die
        // FOLGE der Wiederholungen vom Original ab — die Rate (eine von
        // fünfzig) ist dieselbe.
        bool wiederholen = _powerLast >= 100 || _meldeWurf.Next(50) == 1;
        if (jetzt < 100 && wiederholen) NotePowerShort(true);
        if (_powerLast < 100 && jetzt >= 100) NotePowerShort(false);
        _powerLast = jetzt;
    }

    /// <summary>Der Würfel für die MELDUNGSDROSSELUNG — bewusst NICHT der
    /// gekeimte aus <see cref="Simulation.Determinism"/>. Siehe
    /// <see cref="PowerMessages"/>.</summary>
    private readonly System.Random _meldeWurf = new();

    private int PowerSum()
    {
        int p = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        return _powerOwn[p] + _powerGlobal;
    }

    /// <summary>Meldung <b>124</b> (Strom knapp) bzw. <b>125</b> (wieder
    /// genug). ⚠ Die Nummern sind die des Originals; wie die Meldung
    /// AUSSIEHT, ist unsere Sache — bei uns ist es die Auftragszeile.</summary>
    private void NotePowerShort(bool knapp)
    {
        int p = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        _order = knapp
            ? $"Stromknappheit — die Anlagen laufen mit {Mathf.Min(100, PowerSum())} %"
            : "Die Stromversorgung ist wieder ausreichend.";
        // ⭐ 20.08.2026 — DER KLANG DAZU, und er ist erst seit heute setzbar.
        // Die Nummern standen seit dem 19.08. fest, ihre BEDEUTUNG nicht, und
        // ein Klang an der falschen Stelle ist hörbarer Unsinn. Gemessen ist
        // jetzt beides: 124 und 125 sind gesprochene Ansagen (Stimmhaftigkeit
        // 0,77 bzw. 0,79 gegen 0,00..0,08 bei jedem Effekt), und die Stelle,
        // an der das Original sie ruft, ist genau diese hier.
        Audio.GameSounds.Play(knapp ? Audio.GameSounds.PowerShort
                                    : Audio.GameSounds.PowerRestored);
        PowerShortMessages += knapp ? 1 : 0;
        PowerRestoredMessages += knapp ? 0 : 1;
        _ = p;
    }

    /// <summary>Wie oft »wieder genug Strom« gemeldet wurde. ⚠ Getrennt von
    /// <see cref="PowerShortMessages"/> gezählt: zwei Ansagen in einem Zähler
    /// wären zwei Wahrheiten, und die Aufwärtsflanke ist die seltenere.
    /// </summary>
    public int PowerRestoredMessages;

    /// <summary>Wie oft »Strom knapp« gemeldet wurde. ⚠ Ohne die Zahl ist
    /// nicht zu sehen, ob die Flanke je ausgelöst hat.</summary>
    public int PowerShortMessages;

    /// <summary>
    /// <b>Der Wurf gegen die wirksame Leistung</b> — @0x43DF33 (Fabrik) und
    /// @0x43E57F (Mine), beide gleich:
    /// <code>
    ///   Prozent = wirksam >= Nennwert ? 100 : 100·wirksam/Nennwert
    ///   if (rand()%100 > Prozent) -> dieser Schritt faellt aus
    /// </code>
    /// <para>⚠ <c>&gt;</c>, nicht <c>&gt;=</c>: bei Prozent 0 kommt der Wurf 0
    /// noch durch. Das Original hält eine Anlage ohne Strom also nicht ganz an
    /// — sie schafft es im Schnitt in einem von hundert Schritten. Nachgebaut
    /// wie es dasteht.</para>
    /// <para>Für die FABRIK steckt dieser Wurf schon in <c>Produce()</c>; hier
    /// steht er für die MINE, die ihn bisher gar nicht hatte.</para></summary>
    private static bool PowerRollPasses(Entity e)
    {
        int nenn = Mathf.Max(1, e.EffDen);
        int prozent = e.EffNum >= nenn ? 100 : 100 * e.EffNum / nenn;
        return Simulation.Determinism.Roll(100) <= prozent;
    }

    // ================= der Prüfstand ==========================================

    private int _powerCheck = -1, _powerCheckPlayer, _powerCheckParts, _powerCheckFull;
    private int _powerCheckPctFull, _powerCheckNeed0;
    private readonly System.Text.StringBuilder _powerLog = new();

    /// <summary><c>--power-check</c> anwerfen.</summary>
    public void PowerCheckStart() => _powerCheck = 0;

    /// <summary>Wieviele Teile die Fabriken eines Spielers zusammen haben.</summary>
    private int PartsOf(int player)
    {
        int n = 0;
        foreach (var b in _entities)
            if (b.IsBuilding && !b.Dead && b.Owner == player && b.BType is 2 or 3 or 4)
                n += b.StockW + b.StockF + b.StockS;
        return n;
    }

    /// <summary>Den Fabriken Rohstoff und Platz geben, damit der Wurf der
    /// einzige Engpass ist. ⚠ Eingriff des Prüfstands — er wird in der Ausgabe
    /// genannt.</summary>
    private int PowerCheckFeed(int player)
    {
        int n = 0;
        foreach (var b in _entities)
            if (b.IsBuilding && !b.Dead && b.Owner == player && b.BType is 2 or 3 or 4)
            { b.StockT = 9999; b.Capacity = 99999; b.State = StAktiv; n++; }
        return n;
    }

    /// <summary>Eine Anzahl Wirtschaftstakte fahren — dieselbe Stelle, die auch
    /// im Spiel läuft.</summary>
    private void PowerCheckRun(int takte)
    {
        for (int t = 0; t < takte; t++)
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (!e.IsBuilding || e.Dead) continue;
                e.EconTimer = 0f;
                UpdateEconomy(i, e, EconTick);
            }
    }

    /// <summary>
    /// <c>--power-check</c> — <b>bremst Strommangel die Fertigung, und zwar
    /// anteilig?</b>
    ///
    /// <para><b>Er stellt den Mangel HER</b> (Regel EE): erst wird mit der
    /// Anlage der Karte gemessen, dann werden dem Spieler <b>die Generatoren
    /// weggenommen</b> — und beides wird in der Ausgabe gesagt. Ohne den
    /// zweiten Teil wäre »die Fertigung läuft« kein Urteil über den Strom.</para>
    ///
    /// <para><b>Und er misst gegen eine VORHERSAGE, nicht gegen sich selbst</b>
    /// (Regel 16/N): die Abrechnung sagt einen Prozentsatz, und gezählt werden
    /// die wirklich gefertigten TEILE. Stimmt das Verhältnis der Teile mit dem
    /// Verhältnis der Prozentsätze überein, hat die Bremse wirklich
    /// gegriffen — eine Zahl, die aus derselben Rechnung käme wie der Sollwert,
    /// sagte darüber nichts.</para>
    ///
    /// <para>⚠ Der erste Teil wartet, bis die Abrechnung von SELBST gelaufen
    /// ist (<c>PowerRuns &gt; 0</c>) — damit ist mitgeprüft, dass die Phase
    /// <c>% 50 == 13</c> überhaupt zündet.</para></summary>
    private void PollPowerCheck()
    {
        if (_powerCheck < 0) return;

        switch (_powerCheck)
        {
            case 0:
            {
                if (PowerRuns == 0) return;              // erst von selbst laufen lassen
                _powerLog.AppendLine("power-check");
                _powerLog.AppendLine($"  Die Abrechnung ist von selbst gelaufen " +
                                     $"({PowerRuns}x, Phase % {PowerPeriod} == {PowerPhase}), " +
                                     $"Takt {_origTicks}");

                int best = -1, bestN = 0;
                for (int p = 0; p < 8; p++)
                {
                    int n = 0;
                    foreach (var b in _entities)
                        if (b.IsBuilding && !b.Dead && b.Owner == p && b.BType is 2 or 3 or 4) n++;
                    if (n > bestN) { bestN = n; best = p; }
                }
                if (best < 0)
                {
                    _powerLog.AppendLine("  KEIN URTEIL: keine Fabrik auf dieser Karte — " +
                                         "hier ist nichts zu bremsen");
                    GD.Print(_powerLog.ToString()); _powerCheck = -1; return;
                }
                _powerCheckPlayer = best;
                int gen = 0, kw = 0, minen = 0;
                foreach (var b in _entities)
                {
                    if (!b.IsBuilding || b.Dead) continue;
                    if (b.BType == 13) kw++;
                    else if (b.BType == 7 && b.Owner == best) gen++;
                    else if (b.BType is 10 or 15 && b.Owner == best) minen++;
                }
                var (own, glob, ist, soll) = PowerOf(best);
                _powerLog.AppendLine($"  Spieler {best}: {bestN} Fabriken, {minen} Minen, " +
                                     $"{gen} Generatoren (je {PowerGeneratorOutput}), " +
                                     $"{kw} Kraftwerke auf der Karte (je {PowerPlantOutput}, GLOBAL)");
                _powerLog.AppendLine($"    Bedarf {soll}, erbracht {ist}; " +
                                     $"eigen {own} % + global {glob} % = {own + glob} %");
                _powerCheckPctFull = own + glob;
                _powerCheckNeed0 = soll;

                int f = PowerCheckFeed(best);
                _powerLog.AppendLine($"  ⚠ EINGRIFF: {f} Fabriken bekommen Rohstoff und Lagerplatz, " +
                                     $"damit der STROMWURF der einzige Engpass ist");
                _powerCheckParts = PartsOf(best);
                PowerCheckRun(400);
                _powerCheckFull = PartsOf(best) - _powerCheckParts;
                _powerLog.AppendLine($"  MIT der Anlage der Karte: {_powerCheckFull} Teile " +
                                     $"in 400 Wirtschaftstakten");
                _powerCheck = 1;
                return;
            }

            case 1:
            {
                // ---- den MANGEL herstellen ----
                //
                // ⚠ Erst die Generatoren des Spielers, dann so viele KRAFTWERKE,
                // bis die Summe unter 100 % faellt. Der erste Anlauf schaltete
                // nur die Generatoren ab und meldete »KEIN URTEIL«: auf
                // map_DM_4 hat Spieler 1 gar keine, und neun herrenlose
                // Kraftwerke decken 165 %. Ein Prueflauf, der den Mangel nicht
                // herstellen KANN, ist keiner — also greift er dorthin, wo der
                // Strom wirklich herkommt, und sagt es.
                int weg = 0, kwWeg = 0;
                foreach (var b in _entities)
                    if (b.IsBuilding && !b.Dead && b.Owner == _powerCheckPlayer && b.BType == 7)
                    { b.Dead = true; weg++; }
                PowerTick();
                foreach (var b in _entities)
                {
                    var (o, g, _, _) = PowerOf(_powerCheckPlayer);
                    // ⚠ Nicht bis knapp unter 100, sondern deutlich darunter:
                    // bei 91 % ist der Unterschied von der Streuung des Wurfs
                    // kaum zu trennen. Ein Pruefstand, der seinen Fall nur
                    // knapp herstellt, misst hauptsaechlich Rauschen.
                    if (o + g < 60) break;
                    if (!b.IsBuilding || b.Dead || b.BType != 13) continue;
                    b.Dead = true; kwWeg++;
                    PowerTick();
                }
                _powerLog.AppendLine($"  ⚠ EINGRIFF: {weg} Generator(en) und {kwWeg} Kraftwerk(e) " +
                                     $"abgeschaltet — jetzt MUSS es knapp werden");
                PowerTick();
                var (own2, glob2, ist2, soll2) = PowerOf(_powerCheckPlayer);
                _powerLog.AppendLine($"    Bedarf {soll2}, erbracht {ist2}; " +
                                     $"eigen {own2} % + global {glob2} % = {own2 + glob2} %");

                if (own2 + glob2 >= _powerCheckPctFull)
                {
                    _powerLog.AppendLine("  KEIN URTEIL: der Prozentsatz ist NICHT gefallen — " +
                                         "auf dieser Karte tragen die Generatoren nichts bei " +
                                         "(vermutlich decken die herrenlosen Kraftwerke schon alles).");
                    GD.Print(_powerLog.ToString()); _powerCheck = -1; return;
                }

                int vor = PartsOf(_powerCheckPlayer);
                PowerCheckRun(400);
                int knapp = PartsOf(_powerCheckPlayer) - vor;
                _powerLog.AppendLine($"  OHNE Generatoren: {knapp} Teile in 400 Takten");

                // ---- die VORHERSAGE gegen die Zaehlung ----
                double vorher = Mathf.Min(100, _powerCheckPctFull);
                double nachher = Mathf.Min(100, own2 + glob2);
                double erwartet = vorher > 0 ? _powerCheckFull * nachher / vorher : 0;
                _powerLog.AppendLine($"  VORHERSAGE: {_powerCheckFull} · {nachher:0} % / {vorher:0} % " +
                                     $"= {erwartet:0} Teile; gezaehlt {knapp}");
                // ⚠ Der Vergleich braucht die STREUUNG, nicht eine Prozentzahl.
                // Jedes Teil haengt an einem eigenen Wurf, die Zaehlung ist also
                // binomialverteilt; bei ~120 erwarteten Teilen sind gut 10
                // Stueck Unterschied EIN Sigma. Eine feste 20-%-Schranke sagte
                // dazu nichts — sie waere bei kleinen Zahlen zu streng und bei
                // grossen zu lasch (Regel 27: ein Modell gegen eine Schwelle
                // erfindet Befunde).
                double sigma = System.Math.Sqrt(System.Math.Max(1, erwartet));
                double s = System.Math.Abs(knapp - erwartet) / sigma;
                _powerLog.AppendLine($"    Abweichung {knapp - erwartet:+0;-0} Teile = {s:0.0} Sigma " +
                                     $"(Sigma = √{erwartet:0} = {sigma:0.0}) " +
                                     $"-> {(s <= 3 ? "im Rahmen des Wurfs, RICHTIG" : "ZU GROSS — da bremst etwas anderes")}");
                if (soll2 != _powerCheckNeed0)
                    _powerLog.AppendLine($"    ⚠ der Bedarf hat sich zwischendurch geaendert " +
                                         $"({_powerCheckNeed0} -> {soll2}) — in den 400 Takten hat " +
                                         $"jemand ein Gebaeude verloren oder uebernommen. " +
                                         $"Gerechnet wird mit den Prozentsaetzen, nicht mit dem Bedarf.");
                _powerLog.AppendLine(knapp < _powerCheckFull
                    ? "  Gegenprobe: die Fertigung ist wirklich langsamer geworden"
                    : "  Gegenprobe FEHLGESCHLAGEN: der Mangel hat nichts gebremst");
                _powerLog.AppendLine(knapp > 0
                    ? "  Und sie steht NICHT still — Strommangel bremst anteilig, wie gelesen"
                    : "  ⚠ sie steht STILL — das waere zu streng, das Original bremst nur");
                GD.Print(_powerLog.ToString());
                _powerCheck = -1;
                return;
            }
        }
    }

    /// <summary>Eine Zeile über den Strom — für Prüfstand und Leiste.</summary>
    public string PowerLine()
    {
        int p = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        return $"Strom P{p}: erbracht {_powerDone[p]} / Bedarf {_powerNeed[p]}, " +
               $"eigen {_powerOwn[p]} % + global {_powerGlobal} % = {PowerSum()} %" +
               // ⚠ Beide Flanken einzeln, und die Klangnummern dazu: ohne die
               // Zahlen ist »es kam keine Ansage« nicht von »die Flanke hat nie
               // ausgeloest« zu unterscheiden, und genau daran haengt, ob die
               // seit heute angeschlossenen Klaenge 124/125 wirklich gehen.
               $" ({PowerRuns} Abrechnungen, {PowerShortMessages}x knapp" +
               $" [Ansage {Audio.GameSounds.PowerShort}], {PowerRestoredMessages}x" +
               $" wieder gedeckt [Ansage {Audio.GameSounds.PowerRestored}])";
    }
}
