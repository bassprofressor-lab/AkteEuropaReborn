namespace AkteEuropaReborn.Campaign;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// The campaign's mission scripts.
///
/// In the original these are not data but CODE: the mission number out of the
/// map header indexes a jump table (@0x4A5B0C, 35 entries), and each mission
/// gets its own straight-line block of 542..2660 bytes that runs every tick.
/// Read with `aekernel-tools/mission_logic.py --decode`, every block turns out
/// to be the same shape — a state machine over its own word variables:
///
///     if v[n] == 0 and &lt;condition&gt;:   &lt;effects&gt;;  v[n]++
///
/// with a small vocabulary the game names itself: `game_time()` in minutes
/// (@0x4CF570), `obj_owner(n)` (@0x4D0780), `g_robot_class_count(class, player)`
/// (@0x4CF980) and `g_buildings_count(class, player)` (@0x4CFB10), a one-shot
/// `take_flag(n)` out of the map (@0x4D0700), text, sound, and `mission_end`.
///
/// ⚠ <b>This file is a RUNTIME, not a decompiler.</b> The rules it executes are
/// carried in Data/mission_scripts.json, and each one records the address of
/// the block it was translated from, so the translation can be checked against
/// `--decode`. Where a block uses something not in the vocabulary below, the
/// rule is left out rather than approximated — an absent rule shows up as a
/// mission that never ends, an invented one would quietly play differently.
/// </summary>
public sealed class MissionScript
{
    public const string Path = "res://Data/mission_scripts.json";

    /// <summary>What a rule asks. Everything here is measured; the class
    /// numbers are the game's own (unit class 0 is the sum of classes 1..4,
    /// building class 0 is every building).</summary>
    public sealed class Cond
    {
        public string Kind = "";      // var | time_gt | time_after | obj_owner | units | buildings
        public int A, B, C;           // meaning depends on Kind
        public string Op = "==";
    }

    public sealed class Act
    {
        public string Kind = "";      // inc | set | text | end | find_unit | set_time | space_in
        //                               | money | sound | close_texts | order | add_target
        //                               | remove_unit | sell_unit | change_owner
        //                               | set_relation | stop_transport
        //                               | place_unit | order_at
        /// <summary>D gibt es nur fuer `order` — der Befehlsbus fuehrt dort
        /// vier Felder (Einheit, ukol, x, y).</summary>
        public int A, B, C, D, E;

        /// <summary>Nur `stock`: der quadratische Anteil. Siehe
        /// <see cref="StockWert"/>. 0 heisst »kein Quadratterm«.</summary>
        public int F;

        /// <summary>Nur `place_unit`: die Missionsvariable, die den Satzindex
        /// der gesetzten Einheit aufnimmt — <c>v[V] = place_unit(...)</c>.
        /// −1 heisst »der Rueckgabewert wird weggeworfen«, und das ist der
        /// Normalfall; nur Mission 28 braucht ihn.</summary>
        public int V = -1;

        /// <summary>`space_in` only: the design numbers to drop, in order. They
        /// index sec47 as <c>typ + 200*player</c> — the same table the design
        /// screen and the factories use, which is why mission 14's single byte
        /// 191 resolves to a design the game itself names "Col.Hullman".
        /// </summary>
        public int[] Types = Array.Empty<int>();
    }

    public sealed class Rule
    {
        public int Once = -1;         // the variable that latches this rule, -1 = every tick
        public readonly List<Cond> When = new();

        /// <summary>
        /// Die ODER-Glieder (`any`): mindestens EINES muss zutreffen, zusätzlich
        /// zu allem in <see cref="When"/>. Leer heisst: kein ODER.
        ///
        /// <para>⚠ 13.08.2026 — bis heute kannte diese Datei nur UND, und
        /// deshalb fehlten drei Regeln ganz. Das Original schreibt sein ODER als
        /// zwei Abfragen, von denen die erste in den Rumpf SPRINGT und die
        /// zweite nur bei Misserfolg hinausspringt (Kampagne 2 @0x498EAF):</para>
        /// <code>
        ///     obj_owner(1); test al,al; je  0x498EEC   ; trifft zu -> tu es
        ///     obj_owner(2); test al,al; jne 0x49900B   ; trifft nicht zu -> weg
        ///     0x498EEC: &lt;Wirkung&gt; ... inc v[70]
        /// </code>
        /// <para>Gelesen wird die Form von <c>mission_logic.set_rules</c>; sie
        /// steht in beiden GAME.EXE dieses Rechners dreimal (M2 @0x498EEC,
        /// M3 @0x4996B7, M15 @0x49D89D) und liefert dort dieselben Glieder.
        /// </para>
        /// <para>⚠ Eine Regel mit leerem <see cref="When"/> und gefülltem
        /// <c>Any</c> ist NICHT bedingungslos — M3s Regel ist genau so gebaut.
        /// Wer das ODER übersieht, hat wieder eine zu schwache Bedingung.</para>
        /// </summary>
        public readonly List<Cond> Any = new();

        /// <summary>
        /// <b>Das ODER über GRUPPEN</b> — mindestens EINE Gruppe muss
        /// vollständig zutreffen. Leer heisst: kein Gruppen-ODER.
        ///
        /// <para>⚠ <b>Nicht dasselbe wie <see cref="Any"/>.</b> Dort verbindet
        /// das ODER einzelne GLIEDER, hier ganze UND-GRUPPEN. Mission 28 ist
        /// der Fall, für den es das gibt (@0x4A3698, in beiden GAME.EXE
        /// gleich): drei Wissenschaftler, und <b>einer</b> von ihnen muss lebend
        /// auf (11, 243) stehen —</para>
        /// <code>
        ///   (v[51]==2 &amp;&amp; v[54] lebt als Entwurf 193 &amp;&amp; Spalte==11 &amp;&amp; Zeile==243)
        ///   ODER (v[52]==2 &amp;&amp; v[55] …)
        ///   ODER (v[51]==2 &amp;&amp; v[56] …)
        /// </code>
        ///
        /// <para>⚠⚠ <b>Eine Regel mit leerem <see cref="When"/> und gefülltem
        /// AnyGroups ist NICHT bedingungslos</b> — genau so ist M28 gebaut, und
        /// wer das übersieht, gewinnt die Mission in der Sekunde, in der sie
        /// anfängt. Deshalb prüft <see cref="HasOr"/> beide Listen, und jede
        /// Stelle, die früher <c>Any.Count &gt; 0</c> fragte, fragt jetzt
        /// dort.</para></summary>
        public readonly List<List<Cond>> AnyGroups = new();

        public readonly List<Act> Then = new();

        /// <summary>Die Adresse, aus der die Regel uebersetzt wurde (`_at`) —
        /// und damit ihr TAKT: alles vor dem Tor des Blocks laeuft jeden Takt,
        /// alles dahinter jeden hundertsten. Siehe <see cref="Script.Gate"/>.
        /// </summary>
        public int At;

        /// <summary>Steht diese Regel VOR dem Tor? Wird beim Laden aus
        /// <see cref="At"/> und <see cref="Script.Gate"/> bestimmt.</summary>
        public bool EveryTick;
    }

    public sealed class Script
    {
        public int Mission;
        public string Block = "";     // where it was translated from

        /// <summary>
        /// Das TOR des Missionsblocks (`gate`), gemessen mit
        /// <c>aekernel-tools/mission_tick.py</c> auf beiden GAME.EXE:
        /// 33 von 33 Bloecken tragen genau eines, an derselben Stelle im Rumpf.
        ///
        /// <para>Jeder Block hat die Form</para>
        /// <code>
        ///     &lt;Teil A&gt;                       // jeden Takt, 50 Hz
        ///     mov eax, [Takt mod 100]        // das Tor
        ///     test eax, eax
        ///     jne  &lt;Blockende&gt;
        ///     &lt;Teil B&gt;                       // jeden 100. Takt, alle 2 s
        /// </code>
        /// <para>0 heisst: kein Tor bekannt — dann laeuft die ganze Regelliste
        /// im langsamen Takt, denn das ist die Haelfte, in der die Endregeln
        /// stehen.</para>
        /// </summary>
        public int Gate;

        public readonly List<Rule> Rules = new();
        /// <summary>The state the mission STARTS in, out of the setup block
        /// (`aekernel-tools/mission_initvars.py`). Without it half of every
        /// chain over a block variable is missing: mission 7 wants
        /// `v[102] == 2` and its block raises v[102] exactly once — because the
        /// setup starts it at 1. v[101+k] is the k-th objective's state
        /// (1 = open, 10 = done), v[131+k] its text number.</summary>
        public readonly Dictionary<int, int> Init = new();

        /// <summary>
        /// Die ROHSTOFFVORKOMMEN dieser Mission — `[spalte, zeile, menge]` je
        /// Eintrag, aus <c>add_terra_place(spalte, zeile, menge)</c> im
        /// SETUP-Block (C: 0x4D0A10, F: 0x4D05C0).
        ///
        /// <para>Warum das hier steht und nicht auf der Karte: die Vorkommen
        /// gehören zum MISSIONSAUFBAU, nicht zum Gelände — 50 Aufrufe in acht
        /// Missionen, alle mit festen Zahlen, auf beiden GAME.EXE dieselben.</para>
        ///
        /// <para>Woran es hängt: die <b>Feld-Rohstoffmine</b> (Gebäudetyp 15)
        /// darf nur auf einem Vorkommen stehen (3x3-Fenster, @0x4205C0). Ohne
        /// diese Liste hat sie auf jeder Karte <b>0 Bauplätze</b> — gemessen auf
        /// map_23, deren Ziel »Bauen Sie fünf Rohstoffminen« lautet: Depot 329
        /// Bauplätze, Generator 1411, Feld-Rohstoffmine 0. Mission 23 war damit
        /// unlösbar, und zwar nicht wegen der Mission.</para>
        /// </summary>
        public readonly List<(int Col, int Row, int Amount)> Terra = new();

        /// <summary>
        /// <b>ZELLEN, DIE BEIM MISSIONSSTART SCHON GETROFFEN SIND.</b>
        ///
        /// <para>⚠ 18.08.2026, gemeldet als »in Original Kampagne 1 gibt es
        /// z. B. von Haus aus ein paar brennende Bäume, die haben wir
        /// garnicht«.</para>
        ///
        /// <para>Sie stehen <b>nicht in der Kartendatei</b>. Der SETUP-Block
        /// fährt eine Schleife über eine Zellenliste, schlägt in der
        /// Belegungskarte nach, was dort steht, und schickt es durch
        /// <c>Zasah</c> (@0x40C9A0 — die Zeichenkette steht in seinem eigenen
        /// Protokollaufruf). Was dabei zerstört wird, brennt.</para>
        ///
        /// <para>⚠ Die Liste wird <b>Byte für Byte auf dem Stack</b> gebaut, und
        /// zwar im gemeinsamen Vorspann der Setup-Funktion — dieselbe Klasse
        /// Problem wie bei <c>space_in</c>. Gelesen mit
        /// <c>aekernel-tools/mission_hits.py</c>, das nach der FORM sucht;
        /// <b>beide GAME.EXE liefern für Mission 1 dieselben fünf Zellen</b>
        /// (1,39) (9,40) (7,41) (19,52) (36,68), und vier davon tragen auf
        /// map_01 ein Objekt.</para></summary>
        public readonly List<(int Col, int Row)> Treffer = new();
    }

    // ---- state ------------------------------------------------------------

    private readonly Script _script;
    private readonly int[] _var = new int[512];      // v[n], the block's own words
    private bool _ended;

    // ---- der Takt des Originals --------------------------------------------
    //
    // ⚠ 11.08.2026 — bis heute lief die Regelliste einmal je BILD. Wie oft das
    // Original sie laufen laesst, ist jetzt gemessen; die vollstaendige Kette
    // mit allen Adressen steht im Kopf von `aekernel-tools/mission_tick.py`,
    // hier nur die vier Zahlen und woher sie kommen.

    /// <summary>50 Takte je Sekunde. Der Taktgeber ist eine Fensteruhr:
    /// <c>SetTimer(fenster, 1, 0x14, NULL)</c> — 0x14 = <b>20 ms</b> —, in der
    /// C:-Fassung bei 0x415BC5 (Datei 0x14FC0), in der F:-Fassung Datei
    /// 0x14E00, in beiden genau einmal. Die Fensterprozedur (0x412E30) traegt
    /// WM_TIMER (<c>cmp eax,0x113</c> @0x412ED6) als OFFENEN TAKT ein
    /// (<c>inc byte [0x53920C]</c> @0x414010), die Hauptschleife arbeitet je
    /// Durchlauf genau einen ab (<c>dec</c> @0x415F0E) und deckelt den Vorrat
    /// bei vier (@0x415EFC) — sie kann also aufholen, aber nicht davonlaufen.
    /// </summary>
    public const int TicksPerSecond = 50;

    /// <summary>Alle 100 Takte laeuft der zweite Teil des Missionsblocks: die
    /// Kampagnenfunktion rechnet <c>[0x502988] = Taktzaehler mod 100</c>
    /// (@0x497636), und jeder der 33 Bloecke prueft das genau einmal.
    /// 100 Takte sind bei 50 Hz <b>zwei Sekunden</b>.</summary>
    public const int BlockPeriod = 100;

    /// <summary>250 Takte sind eine Spielminute — also fuenf reale Sekunden,
    /// eine Spielstunde in fuenf realen Minuten. Die Uhrenkette steht direkt
    /// hinter dem Taktzaehler: <c>inc byte [0x81AA28]; cmp al,0xFA</c>
    /// (@0x4160B3) → <c>inc byte [0x81AA2C]; cmp 0x3C</c> (@0x416135) →
    /// <c>inc byte [0x8154E4]; cmp 0x18</c> → <c>inc dword [0x833868]</c>.
    /// Und <c>game_time()</c> @0x4CF570 liest genau diese 0x81AA2C.
    /// <para>⚠ Vorher rechnete diese Datei eine Spielminute als eine REALE
    /// Minute, also zwoelfmal zu langsam. Keine uebersetzte Regel fragt bisher
    /// nach der Zeit, die Aenderung bewegt darum nur die Anzeige — aber sie
    /// bewegt sie in die richtige Richtung.</para></summary>
    public const int TicksPerGameMinute = 250;

    /// <summary>Fuenf reale Sekunden auf eine Spielminute — dieselbe Zahl wie
    /// oben, nur in der Einheit, die eine Anzeige ohne Skript braucht
    /// (<c>250 / 50</c>).</summary>
    public const double RealSecondsPerGameMinute =
        TicksPerGameMinute / (double)TicksPerSecond;

    /// <summary>Takte seit Missionsbeginn. Der Taktzaehler des Originals
    /// (0x4FA240) wird beim Missionsstart auf 0 gestellt (@0x442728).</summary>
    private long _ticks;

    /// <summary>Der angebrochene Takt. Ohne ihn verschluckt eine Bildrate ueber
    /// 50 Hz Takte und eine darunter macht sie nicht auf.</summary>
    private double _rest;

    /// <summary>Fuer den Pruefstand: wieviele Takte gelaufen sind und wieviele
    /// Durchlaeufe der langsame Teil hatte.</summary>
    public long Ticks => _ticks;
    public long BlockRuns { get; private set; }
    public double Seconds => _ticks / (double)TicksPerSecond;

    public bool Ended => _ended;

    /// <summary>
    /// Does this script decide the mission at all?
    ///
    /// ⚠ Most of the 33 are translated only in part, and a partial script must
    /// NOT take the decision away from the fallback: a mission whose script has
    /// no `end` rule would otherwise be unwinnable. So the script is
    /// authoritative only once it can actually finish.
    ///
    /// ⚠ And an `end` rule alone is not enough. Several of the original's end
    /// conditions are chains over the block's OWN variables — mission 7 wants
    /// `v[102] == 2 and v[12] == 2` on top of its two destroyed objects, and
    /// v[102] is a stage counter that only the untranslated part of the block
    /// ever raises. Such a rule is read correctly and can still never fire, so
    /// letting it decide would make the mission unwinnable — the exact opposite
    /// of the truncated-chain bug it fixes. So an end rule counts only when
    /// every variable it tests is one that some translated rule writes.
    /// </summary>
    /// <summary>
    /// Kennt dieses Skript überhaupt eine NIEDERLAGE?
    ///
    /// <para>Bis zum 18.08.2026 kannte kein einziges eine: die Vokabel
    /// <c>end(a=0)</c> stand im Wortverzeichnis und wurde nie benutzt, weil der
    /// Leser nur die eine Endfunktion (<c>mission_end</c>) kannte und nicht die
    /// zweite (0x4D0280, setzt den Missionszustand auf 5000 statt auf 1). Siehe
    /// <c>aekernel-tools/mission_loserules.py</c>.</para>
    ///
    /// <para>Jetzt haben 31 der 33 Missionen mindestens eine. Für die drei
    /// übrigen — M10 (eine imap-Zelle, nur auf einer Fassung lesbar), M14
    /// (auf keiner) und M28 (die Kette ist unvollständig, ein
    /// <c>unit_alive</c>-Glied fehlt) — braucht <c>Verdict()</c> genau diese
    /// Auskunft, um seine Notregel nur dort einspringen zu lassen, wo wirklich
    /// nichts gelesen ist.</para>
    /// </summary>
    public bool HasDefeat
    {
        get
        {
            foreach (var r in _script.Rules)
                foreach (var a in r.Then)
                    if (a.Kind == "end" && a.A == 0) return true;
            return false;
        }
    }

    public bool Decides
    {
        get
        {
            foreach (var r in _script.Rules)
            {
                if (!Ends(r)) continue;
                bool reachable = true;
                foreach (var c in r.When)
                    if (c.Kind == "var" && !Cmp(Start(c.A), c.Op, c.B) && !Writes(c.A))
                    { reachable = false; break; }
                // Ein ODER lebt, solange EIN Glied lebt.
                if (reachable && HasOr(r) && !AnyReachable(r, new HashSet<int>()))
                    reachable = false;
                if (reachable) return true;
            }
            return false;
        }
    }

    private static bool Ends(Rule r)
    {
        foreach (var a in r.Then) if (a.Kind == "end") return true;
        return false;
    }

    /// <summary>Trifft mindestens ein ODER-Glied zu? Nur zu rufen, wenn es
    /// welche gibt — eine leere Liste heisst »kein ODER«, nicht »wahr«.
    /// </summary>
    private bool AnyTrue(Rule r)
    {
        foreach (var c in r.Any) if (Test(c)) return true;
        // ⚠ Eine GRUPPE muss GANZ zutreffen — sie ist ein UND. Wer hier
        // dieselbe Schleife wie oben schriebe, machte aus dem Gruppen-ODER ein
        // ODER ueber alle Glieder und liesse M28 gewinnen, sobald EIN
        // Wissenschaftler auch nur den richtigen Zustand haette.
        foreach (var g in r.AnyGroups)
        {
            bool alle = true;
            foreach (var c in g) if (!Test(c)) { alle = false; break; }
            if (alle) return true;
        }
        return false;
    }

    /// <summary>Hat diese Regel überhaupt ein ODER — als Glieder oder als
    /// Gruppen? ⚠ Jede Stelle, die früher <c>Any.Count &gt; 0</c> fragte, muss
    /// das hier fragen: sonst gilt eine Regel mit leerem <c>When</c> und
    /// gefüllten Gruppen als bedingungslos.</summary>
    private static bool HasOr(Rule r) => r.Any.Count > 0 || r.AnyGroups.Count > 0;

    /// <summary>
    /// PRÜFSTAND `--oder-check`: das ODER an seiner MECHANIK, nicht an seinem
    /// Ergebnis.
    ///
    /// <para>⚠ Regel 8 — ein Prüfstand, der nur das Ergebnis hinschreibt, prüft
    /// nichts. Also wird das Gatter jeder ODER-Regel durch seine <b>ganze
    /// Wahrheitstafel</b> gefahren: bei k Armen 2^k Belegungen, und gefeuert
    /// werden darf genau dann, wenn mindestens ein Arm wahr ist. Die Antworten
    /// werden dem Glied vorgegeben (<see cref="_vorgabe"/>), die Welt bleibt
    /// unberührt — sonst hinge das Ergebnis an der Karte statt am Gatter.</para>
    ///
    /// <para>Was das NICHT prüft: ob die Glieder richtig GELESEN sind. Das
    /// entscheidet <c>mission_logic.set_rules</c> gegen beide GAME.EXE.</para>
    /// </summary>
    public string OrCheck()
    {
        var sb = new System.Text.StringBuilder();
        int regeln = 0, zeilen = 0, falsch = 0;
        for (int ri = 0; ri < _script.Rules.Count; ri++)
        {
            var r = _script.Rules[ri];
            if (r.Any.Count == 0) continue;
            regeln++;
            int k = r.Any.Count;
            var tafel = new List<string>();
            for (int maske = 0; maske < (1 << k); maske++)
            {
                _vorgabe = new Dictionary<Cond, bool>();
                for (int b = 0; b < k; b++) _vorgabe[r.Any[b]] = (maske & (1 << b)) != 0;
                bool ist = AnyTrue(r);
                bool soll = maske != 0;
                _vorgabe = null;
                zeilen++;
                if (ist != soll) falsch++;
                var arme = new List<string>();
                for (int b = 0; b < k; b++) arme.Add((maske & (1 << b)) != 0 ? "wahr" : "falsch");
                tafel.Add($"{string.Join("/", arme)}->{(ist ? "feuert" : "still")}" +
                          (ist == soll ? "" : " FALSCH"));
            }
            var namen = new List<string>();
            foreach (var c in r.Any) namen.Add(Show(c));
            sb.Append($"   Regel {ri} @0x{r.At:X}: ODER({string.Join(" | ", namen)})\n" +
                      $"      {string.Join("  ", tafel)}\n");
        }
        return $"oder-check: M{Mission} {regeln} ODER-Regeln, {zeilen} Belegungen, " +
               $"{falsch} falsch\n" + sb.ToString().TrimEnd();
    }

    /// <summary>
    /// PRÜFSTAND `--rule-check`: jede Regel des laufenden Skripts mit ihren
    /// Gliedern und deren WERTEN — nicht nur die Endregel.
    ///
    /// <para>Warum es das braucht: eine Regel, die nicht feuert, ist im Spiel
    /// von einer, die es gleich täte, nicht zu unterscheiden. <c>WhyNot</c>
    /// verfolgt nur die Kette zur Endregel; die halbe Kampagne besteht aber aus
    /// Regeln, die einen Text zeigen oder einen Zähler anstossen, und die
    /// tauchen dort gar nicht auf.</para>
    /// </summary>
    public string RuleCheck()
    {
        var sb = new System.Text.StringBuilder();
        int offen = 0;
        for (int ri = 0; ri < _script.Rules.Count; ri++)
        {
            var r = _script.Rules[ri];
            bool zu = r.Once >= 0 && r.Once < _var.Length && _var[r.Once] != 0;
            bool alle = true;
            var teile = new List<string>();
            foreach (var c in r.When)
            {
                bool ok = Test(c);
                if (!ok) alle = false;
                teile.Add(Show(c) + (ok ? "=ja" : "=NEIN"));
            }
            bool oder = !HasOr(r) || AnyTrue(r);
            foreach (var c in r.Any)
                teile.Add("ODER " + Show(c) + (Test(c) ? "=ja" : "=NEIN"));
            bool feuert = !zu && alle && oder;
            if (!feuert && !zu) offen++;
            var wirkt = new List<string>();
            foreach (var a in r.Then) wirkt.Add(a.Kind);
            sb.Append($"   {ri,2} @0x{r.At:X} {(zu ? "RIEGEL" : feuert ? "FEUERT" : "wartet")}" +
                      $" once={r.Once,3} -> {(wirkt.Count == 0 ? "(nichts uebersetzt)" : string.Join("+", wirkt))}\n" +
                      $"        {(teile.Count == 0 ? "(ohne Bedingung)" : string.Join("  ", teile))}\n");
        }
        return $"rule-check: M{Mission} {_script.Rules.Count} Regeln, {offen} warten\n" +
               sb.ToString().TrimEnd();
    }

    /// <summary>
    /// PRÜFSTAND <c>--hilfe-check</c>: <b>DIE HILFEFENSTER DIESER MISSION —
    /// wieviele koennen ueberhaupt aufgehen?</b>
    ///
    /// <para>⚠ 18.08.2026. Der Anlass ist eine Zahl, die seit dem 11.08. im
    /// Quelltext stand und nie nachgemessen wurde: »Mission 1 ruft daraus
    /// <b>siebzehn</b> Fenster auf.« In <c>Data/mission_scripts.json</c>
    /// standen elf. Die sechs fehlenden hingen an vier Abfragen, die der
    /// Regelleser nicht las — und eine fehlende Regel sieht im Spiel aus wie
    /// ein Tutorial, das nach dem ersten Fenster stehenbleibt, nicht wie ein
    /// Fehler.</para>
    ///
    /// <para>Gemessen wird darum die Zahl, die genau das trennt, und zwar in
    /// drei Stufen, die alle scheitern koennen:</para>
    /// <list type="number">
    /// <item><b>Text da?</b> — <c>HelpWindow.TextOf(id)</c>. Ohne
    /// <c>help.json</c> faellt jedes Fenster aus, und das darf nicht wie ein
    /// Erfolg aussehen.</item>
    /// <item><b>Haken da?</b> — jedes Glied der Bedingung muss beantwortet
    /// werden koennen (<see cref="Hooked(Cond)"/>). Ein fehlender Haken macht
    /// die Bedingung FALSCH und das Fenster still.</item>
    /// <item><b>Erzeuger da?</b> — jede Variablenbedingung, die zu Beginn nicht
    /// schon wahr ist, braucht eine Regel, die diese Variable setzt und selbst
    /// feuern kann (<see cref="Writes(int)"/>, transitiv, mit Kreiserkennung).
    /// Dasselbe gilt fuer den <c>once</c>-Riegel.</item>
    /// </list>
    ///
    /// <para>Und danach der <b>Trockenlauf</b>: jede Fensterregel wird EINMAL
    /// durch denselben <see cref="Do"/>-Weg geschickt wie im Spiel, mit
    /// vorgegebenen Bedingungen (<c>_vorgabe</c>, dieselbe Mechanik wie bei
    /// <c>--oder-check</c>). Gezaehlt wird, was am Haken
    /// <see cref="ShowText"/> ankommt — Nummer, Fensterart und Stelle. Damit
    /// misst der Prüfstand den ganzen Weg von der JSON bis zum Fenster und
    /// nicht nur die Datei.</para>
    ///
    /// <para>⚠ Der Trockenlauf laesst den Haken unangetastet: er zaehlt nur
    /// mit. Wer ihn im echten Spiel laufen laesst, sieht die Fenster wirklich
    /// aufgehen — das ist Absicht, denn ein Prüfstand, der seinen Gegenstand
    /// nachbildet statt ihn aufzurufen, misst ihn nicht.</para>
    /// </summary>
    public string TutorialCheck()
    {
        var sb = new System.Text.StringBuilder();
        int fenster = 0, mitText = 0, kannFeuern = 0;

        foreach (var r in _script.Rules)
        {
            foreach (var a in r.Then)
            {
                if (a.Kind != "text") continue;
                fenster++;
                bool text = UI.HelpWindow.TextOf(a.A) is { Count: > 0 };
                if (text) mitText++;

                var fehlt = new List<string>();
                foreach (var c in r.When)
                {
                    if (!Hooked(c)) { fehlt.Add("kein Haken: " + c.Kind); continue; }
                    if (c.Kind == "var" && !Cmp(Start(c.A), c.Op, c.B) && !Writes(c.A))
                        fehlt.Add($"ohne Erzeuger: v[{c.A}]");
                }
                foreach (var c in r.Any)
                    if (!Hooked(c)) fehlt.Add("kein Haken (ODER): " + c.Kind);
                if (r.Once >= 0 && Start(r.Once) != 0 && !Writes(r.Once))
                    fehlt.Add($"Riegel v[{r.Once}] startet auf {Start(r.Once)}");
                if (!text) fehlt.Add("kein Text in help.json");

                if (fehlt.Count == 0) kannFeuern++;
                sb.Append($"   @0x{r.At:X} #{a.A,-4} Art{a.B} bei ({a.C,3},{a.D,3}) " +
                          $"once={r.Once,3}  " +
                          (fehlt.Count == 0 ? "kann feuern" : "BLOCKIERT: " +
                              string.Join(", ", fehlt)) + "\n" +
                          "        " +
                          Glieder(r) + "\n");
            }
        }

        // ---- Trockenlauf: jede Fensterregel einmal wirklich ausloesen ------
        var gesehen = new List<string>();
        var vorher = ShowText;
        ShowText = (id, art, x, y) =>
        {
            gesehen.Add($"#{id}");
            vorher?.Invoke(id, art, x, y);
        };
        _vorgabe ??= new Dictionary<Cond, bool>();
        try
        {
            foreach (var r in _script.Rules)
            {
                bool hatText = false;
                foreach (var a in r.Then) if (a.Kind == "text") hatText = true;
                if (!hatText) continue;
                foreach (var c in r.When) _vorgabe[c] = true;
                foreach (var c in r.Any) _vorgabe[c] = true;
                bool alle = true;
                foreach (var c in r.When) if (!Test(c)) alle = false;
                if (!alle) continue;              // die Vorgabe hat nicht gegriffen
                foreach (var a in r.Then) if (a.Kind == "text") Do(a);
            }
        }
        finally
        {
            _vorgabe.Clear();
            ShowText = vorher;
        }

        // ---- Das TOR der Kette an seiner Wahrheitstafel ---------------------
        //
        // ⚠ Nicht an seinem Ergebnis, sondern an seiner Mechanik (dieselbe
        // Ueberlegung wie bei --oder-check): `window_open(n)` wird einmal auf
        // »offen« und einmal auf »zu« gestellt, und die Regel MUSS im ersten
        // Fall schweigen und im zweiten durchlassen. Eine Regel, die in beiden
        // Faellen dasselbe sagt, haette kein Tor — und genau das war der
        // Zustand vor dem 18.08.: die Regel gab es gar nicht.
        var tor = new List<string>();
        var vorherTor = TextOpen;
        try
        {
            foreach (var r in _script.Rules)
            {
                Cond? gate = null;
                foreach (var c in r.When) if (c.Kind == "text_open") gate = c;
                if (gate == null) continue;
                int wartet = 0;
                foreach (var a in r.Then) if (a.Kind == "text") wartet = a.A;
                TextOpen = _ => true;
                bool zu = Test(gate);
                TextOpen = _ => false;
                bool auf = Test(gate);
                tor.Add($"#{wartet}<-Fenster#{gate.A}: offen={(zu ? "durch" : "haelt")}" +
                        $", zu={(auf ? "durch" : "haelt")}" +
                        (!zu && auf ? " ok" : " ⚠ KEIN TOR"));
            }
        }
        finally { TextOpen = vorherTor; }

        return $"hilfe-check: M{Mission} {fenster} Hilfefenster, {mitText} mit Text, " +
               $"{kannFeuern} koennen feuern, {fenster - kannFeuern} blockiert; " +
               $"Trockenlauf {gesehen.Count}/{fenster} am Haken " +
               $"[{string.Join(" ", gesehen)}]\n" +
               (tor.Count == 0 ? "" : "   Tor der Kette: " + string.Join("  ", tor) + "\n") +
               sb.ToString().TrimEnd();
    }

    /// <summary>Die Glieder einer Regel mit ihrem jetzigen Wahrheitswert — wie
    /// <see cref="Links"/>, aber ohne die Suche nach Erzeugern.</summary>
    private string Glieder(Rule r)
    {
        var parts = new List<string>();
        foreach (var c in r.When) parts.Add(Show(c) + (Test(c) ? "=ja" : "=NEIN"));
        foreach (var c in r.Any) parts.Add("ODER " + Show(c) + (Test(c) ? "=ja" : "=NEIN"));
        return parts.Count == 0 ? "(ohne Bedingung)" : string.Join("  ", parts);
    }

    /// <summary>Die Prüfstände, die sich selbst aus der Befehlszeile lesen —
    /// die zentrale Schalterschleife gehört einer anderen Datei, und das Muster
    /// ist schon da (MapEntityLayer liest seine eigenen ebenso). Läuft genau
    /// einmal, beim ersten Takt: da hängen die Haken, vorher nicht.</summary>
    private bool _bankGelaufen;

    private void BankVielleicht()
    {
        if (_bankGelaufen) return;
        _bankGelaufen = true;
        bool oder = false, regeln = false, ziele = false, tut = false;
        foreach (string a in Core.CommandLine.Args)
        {
            if (a == "--oder-check") oder = true;
            else if (a == "--rule-check") regeln = true;
            else if (a == "--ziel-check") ziele = true;
            else if (a == "--hilfe-check") tut = true;
        }
        if (oder) GD.Print(OrCheck());
        if (regeln) GD.Print(RuleCheck());
        if (ziele) GD.Print(GoalCheck());
        if (tut) GD.Print(TutorialCheck());
    }

    /// <summary>
    /// PRÜFSTAND `--ziel-check`: WILL die Endregel MEHR oder WENIGER?
    ///
    /// <para>⚠ Der Anlass ist Mission 23, und sie stand deswegen Tage auf der
    /// offenen Liste. Ihre Siegbedingung ist <c>buildings(Klasse 2, Spieler 0)
    /// == 5</c> (@0x4A0DBA, in beiden GAME.EXE gleich) — »Bauen Sie fünf
    /// Rohstoffminen«. <c>--script-check</c> kennt genau zwei Hebel,
    /// AUSSCHLAGEN und ÜBERGEBEN, und beide machen eine Zahl nur kleiner oder
    /// verschieben sie zwischen Spielern. Eine Zahl, die WACHSEN muss, kann er
    /// nicht herstellen; er meldet dann »nicht erzwingbar«, und das liest sich
    /// wie ein Fehler der Mission. Es ist aber ein Fehler der FRAGE.</para>
    ///
    /// <para>Diese Zeile trennt die beiden Arten und sagt bei jeder, welcher
    /// Prüfstand sie herstellen kann. Ein BAUZIEL gehört zu
    /// <c>--terra-check</c> bzw. <c>--produce-check</c>, die über den echten
    /// Bauweg gehen; nur ein ZERSTÖRUNGSZIEL gehört zu <c>--script-check</c>.
    /// </para>
    ///
    /// <para>⚠ Was sie NICHT sagt: ob das Ziel erreichbar IST. Das entscheidet
    /// die Karte, und dafür gibt es <c>--terra-check</c> und
    /// <c>--build-check</c>.</para>
    /// </summary>
    public string GoalCheck()
    {
        var sb = new System.Text.StringBuilder();
        int bau = 0, weg = 0, offen = 0;
        foreach (var c in EndConds())
        {
            int ist = Jetzt(c);
            int soll = TargetCount(c);
            if (ist < 0 || soll < 0)
            {
                // ⚠ Das heisst NICHT »unerreichbar«. Ein Glied ueber eine
                // Blockvariable ist ueberhaupt keine Zaehlbedingung: was der
                // Pruefstand herstellen muss, steht bei ihrem ERZEUGER, und den
                // loest `ChainConds` auf. Wer hier »kann keiner« schriebe, haette
                // wieder eine Zeile, die einen Erfolg wie einen Fehler aussehen
                // laesst — Mission 5 haengt genau so an v[33]/v[34].
                offen++;
                sb.Append($"   {Show(c)}: keine Zaehlbedingung — sie haengt an einer " +
                          "Kette; was dafuer herzustellen ist, sagt WhyNot " +
                          "(--script-check) bzw. --rule-check\n");
                continue;
            }
            bool baut = soll > ist;
            if (baut) bau++; else weg++;
            sb.Append($"   {Show(c)}: ist {ist}, soll {soll} — " +
                      (baut
                          ? $"BAUZIEL, es fehlen {soll - ist}. --script-check kann das " +
                            "NICHT (er kann nur ausschlagen und uebergeben); " +
                            "--terra-check / --produce-check gehen den echten Bauweg"
                          : "ZERSTOERUNGSZIEL — das kann --script-check herstellen") +
                      "\n");
        }
        return $"ziel-check: M{Mission} {bau} Bauziel(e), {weg} Zerstoerungsziel(e)" +
               (offen > 0 ? $", {offen} ueber eine Kette" : "") +
               "\n" + sb.ToString().TrimEnd();
    }

    /// <summary>Der Stand, den ein zaehlendes Glied gerade vorfindet, oder -1,
    /// wenn es nicht zaehlt (oder sein Haken fehlt — dann ist die Zahl
    /// unbekannt, und unbekannt darf hier nicht 0 heissen).</summary>
    private int Jetzt(Cond c) => c.Kind switch
    {
        "units" => UnitCount != null ? UnitCount(c.A, c.B) : -1,
        "buildings" => BuildingCount != null ? BuildingCount(c.A, c.B) : -1,
        "objects" => ObjectCount != null ? ObjectCount(c.A, c.B) : -1,
        _ => -1,
    };

    /// <summary>
    /// Kann v[n] jemals von seinem Anfangswert abweichen?
    ///
    /// <para>⚠ 11.08.2026 — das hiess bis heute nur »schreibt irgendeine Regel
    /// v[n]?«, und das war zu wenig. Mission 7 zeigt warum: v[102] wird von
    /// genau einer Regel gehoben, und die verlangt <c>v[1] == 40</c>; v[1]
    /// wiederum wird nur von einem Zaehler gehoben, der selbst
    /// <c>v[1] != 0</c> verlangt — er kann sich also nicht selbst anstossen
    /// (der Anstoss steht im Original in einer Regel, die wir nicht uebersetzen
    /// koennen, siehe CAMPAIGN_RE.md). Flach gelesen sah die Kette bewohnt aus,
    /// und <see cref="Decides"/> nahm der Notregel die Entscheidung ab — genau
    /// die Falle, vor der der Kommentar dort warnt: die Mission wurde
    /// unloesbar.</para>
    ///
    /// <para>Darum jetzt DURCHGEHEND: eine Regel zaehlt nur als Erzeuger, wenn
    /// sie selbst feuern kann. Ihre Weltbedingungen zaehlen als erfuellbar (der
    /// Spieler kann sie herstellen), ihre Variablenbedingungen nur, wenn sie
    /// schon im Anfangszustand wahr sind oder die Variable ihrerseits erreichbar
    /// ist. Ein Kreis — eine Variable, die nur sich selbst treibt — ist damit
    /// unerreichbar, und genau das soll er sein.</para>
    /// </summary>
    private bool Writes(int n) => Writes(n, new HashSet<int>());

    private bool Writes(int n, HashSet<int> onPath)
    {
        if (!onPath.Add(n)) return false;         // Kreis
        try
        {
            foreach (var r in _script.Rules)
            {
                if (!SetsVar(r, n)) continue;
                bool can = true;
                foreach (var c in r.When)
                {
                    if (c.Kind != "var") continue;              // Weltbedingung: herstellbar
                    if (Cmp(Start(c.A), c.Op, c.B)) continue;   // schon zu Beginn wahr
                    if (!Writes(c.A, onPath)) { can = false; break; }
                }
                // Ein ODER lebt, solange EIN Glied lebt — sonst kann die Regel
                // nie feuern und ist als Erzeuger nichts wert.
                if (can && HasOr(r) && !AnyReachable(r, onPath)) can = false;
                // Der `once`-Riegel verlangt v[once] == 0; das ist eine
                // Bedingung wie jede andere.
                if (can && r.Once >= 0 && Start(r.Once) != 0) can = false;
                if (can) return true;
            }
            return false;
        }
        finally { onPath.Remove(n); }
    }

    /// <summary>Kann mindestens ein ODER-Glied dieser Regel je zutreffen?
    /// Weltbedingungen gelten als herstellbar, Variablenbedingungen nur, wenn
    /// sie schon im Anfangszustand wahr sind oder die Variable einen Erzeuger
    /// hat. Dieselbe Rechnung wie in <see cref="Writes(int, HashSet{int})"/>,
    /// nur mit ODER statt UND.</summary>
    private bool AnyReachable(Rule r, HashSet<int> onPath)
    {
        foreach (var c in r.Any)
        {
            if (c.Kind != "var") return true;                 // Weltbedingung
            if (Cmp(Start(c.A), c.Op, c.B)) return true;       // schon wahr
            if (Writes(c.A, onPath)) return true;
        }
        // ⚠ Eine GRUPPE ist erreichbar, wenn ALLE ihre Glieder es sind — sie
        // ist ein UND. Dieselbe Rechnung wie in `Decides` fuer `When`, nur
        // gruppenweise; ein einzelnes erreichbares Glied genuegt hier NICHT.
        foreach (var g in r.AnyGroups)
        {
            bool alle = true;
            foreach (var c in g)
            {
                if (c.Kind != "var") continue;                // Weltbedingung
                if (Cmp(Start(c.A), c.Op, c.B)) continue;      // schon wahr
                if (Writes(c.A, onPath)) continue;
                alle = false; break;
            }
            if (alle) return true;
        }
        return false;
    }

    /// <summary>Schreibt diese eine Regel v[n]? Der `once`-Riegel zaehlt mit —
    /// so hebt das Original die meisten seiner Merker.</summary>
    private static bool SetsVar(Rule r, int n)
    {
        if (r.Once == n) return true;
        foreach (var a in r.Then)
            if ((a.Kind == "inc" || a.Kind == "set" ||
                 a.Kind == "find_unit" || a.Kind == "set_time" ||
                 a.Kind == "take_var" || a.Kind == "set_units" ||
                 a.Kind == "set_store") && a.A == n) return true;
        return false;
    }

    /// <summary>Der Wert, mit dem v[n] in die Mission geht.</summary>
    private int Start(int n) =>
        n >= 0 && _script.Init.TryGetValue(n, out int v) ? v : 0;

    /// <summary>Which end rules cannot fire because a variable they test is
    /// never written — for the harness, so the gap is visible instead of just
    /// showing up as a mission that runs forever.</summary>
    public List<int> UnreachableVars()
    {
        var list = new List<int>();
        foreach (var r in _script.Rules)
        {
            if (!Ends(r)) continue;
            foreach (var c in r.When)
                if (c.Kind == "var" && !Cmp(Start(c.A), c.Op, c.B) && !Writes(c.A) &&
                    !list.Contains(c.A)) list.Add(c.A);
        }
        list.Sort();
        return list;
    }
    public bool Success { get; private set; }
    public int Mission => _script.Mission;
    public int RulesFired { get; private set; }

    /// <summary>Wieviele Einsetzungen (<c>place_unit</c>) dieses Skript
    /// AUSGELÖST hat, und wieviele Befehle (<c>order_at</c>). Die MESSLATTE ist
    /// die gelesene Zahl je Mission — kommt Kampagne 2 auf sieben, ist das ein
    /// Beleg; kommt sie auf neunzehn, feuert etwas zu oft.</summary>
    public int Placements { get; private set; }

    /// <summary>Der Satzindex der zuletzt gesetzten Einheit, −1 wenn keine
    /// entstanden ist. Siehe <see cref="PlaceUnit"/> — M28 braucht ihn.</summary>
    public int LastPlaced { get; private set; } = -1;

    public int OrdersGiven { get; private set; }

    /// <summary>Wieviele Einsetzungen und Befehle dieses Skript überhaupt
    /// TRÄGT — die statische Zahl, gegen die die ausgelöste gehalten wird.
    /// </summary>
    public (int Place, int Order) Carried()
    {
        int p = 0, o = 0;
        foreach (var r in _script.Rules)
            foreach (var a in r.Then)
            {
                if (a.Kind == "place_unit") p++;
                else if (a.Kind == "order_at") o++;
            }
        return (p, o);
    }

    /// <summary>
    /// Die Bedingungen der Regeln, die eine EINSETZUNG oder einen BEFEHL tragen
    /// — damit der Prüfstand sie herstellen kann.
    ///
    /// <para>⚠ Warum das nötig ist: <see cref="ChainConds"/> geht von der
    /// ENDregel aus, und die Einsetzungen hängen nicht an ihr. Kampagne 2s
    /// sieben Einheiten kommen aus einer Regel, die v[70] anstösst; die Endregel
    /// zählt Objekte. Ohne diesen zweiten Einstieg liesse sich die Einsetzung
    /// nur durch Spielen auslösen — und ein Prüflauf, der sie nie auslöst, sieht
    /// genauso aus wie einer, in dem sie kaputt ist.</para>
    ///
    /// <para>Die ODER-Glieder kommen mit. Alle wahr zu machen kann das ODER nur
    /// wahr machen; was der Prüfstand nicht stellen kann, meldet er.</para>
    /// </summary>
    /// <summary>Die Rohstoffvorkommen dieser Mission — siehe
    /// <see cref="Script.Terra"/>. Ohne sie hat die Feld-Rohstoffmine nirgends
    /// einen Bauplatz.</summary>
    public IReadOnlyList<(int Col, int Row, int Amount)> Terra => _script.Terra;

    /// <summary>Siehe <see cref="Script.Treffer"/>.</summary>
    public IReadOnlyList<(int Col, int Row)> Treffer => _script.Treffer;

    public List<Cond> PlaceConds()
    {
        var list = new List<Cond>();
        foreach (var r in _script.Rules)
        {
            bool setzt = false;
            foreach (var a in r.Then)
                if (a.Kind is "place_unit" or "order_at") { setzt = true; break; }
            if (!setzt) continue;
            foreach (var c in r.When) list.Add(c);
            foreach (var c in r.Any) list.Add(c);
            foreach (var g in r.AnyGroups) foreach (var c in g) list.Add(c);
        }
        return list;
    }

    /// <summary>Die Bedingungen der Regeln, die ein Lager BESTUECKEN — damit
    /// <c>--stock-check</c> sie stellen kann, statt die Mission zu spielen.
    /// Siehe <see cref="SetStoreField"/>.</summary>
    public List<Cond> StockConds()
    {
        var list = new List<Cond>();
        foreach (var r in _script.Rules)
        {
            bool stockt = false;
            foreach (var a in r.Then) if (a.Kind == "stock") { stockt = true; break; }
            if (!stockt) continue;
            foreach (var c in r.When) list.Add(c);
            foreach (var c in r.Any) list.Add(c);
            foreach (var g in r.AnyGroups) foreach (var c in g) list.Add(c);
        }
        return list;
    }

    /// <summary>Jede Bestueckung dieses Skripts als Zahlenreihe: Gebaeudeplatz,
    /// Feld, Variable (oder −1), Aufschlag — die SOLLwerte, gegen die
    /// <c>--stock-check</c> das Ergebnis haelt.
    ///
    /// <para><c>Bereit</c> sagt, ob die Bedingungen der Regel gerade zutreffen,
    /// die Sperre ausgeklammert. ⚠ Das trennt zwei Dinge, die sonst in denselben
    /// Zaehler liefen (Arbeitsweise I): eine Regel, die feuern KONNTE und die
    /// falsche Zahl schrieb, ist ein Fehler von uns — eine, deren Bedingung der
    /// Pruefstand gar nicht herstellen kann (Mission 2 haengt an einem
    /// Fortschrittszaehler v[20]), ist eine Grenze des Pruefstands.</para>
    /// </summary>
    public List<(int Slot, int Off, int Var, int Add, int Soll, int At, bool Bereit)> StockSites()
    {
        var list = new List<(int, int, int, int, int, int, bool)>();
        foreach (var r in _script.Rules)
        {
            bool bereit = true;
            foreach (var c in r.When) if (!TestReal(c)) { bereit = false; break; }
            if (bereit && HasOr(r))
            {
                bereit = false;
                foreach (var c in r.Any) if (TestReal(c)) { bereit = true; break; }
                // ⚠ Eine GRUPPE zaehlt nur GANZ — dasselbe wie in AnyTrue.
                foreach (var g in r.AnyGroups)
                {
                    bool alleG = true;
                    foreach (var c in g) if (!TestReal(c)) { alleG = false; break; }
                    if (alleG) { bereit = true; break; }
                }
            }
            foreach (var a in r.Then)
                if (a.Kind == "stock")
                    list.Add((a.A, a.B, a.C, a.D, StockWert(a), r.At, bereit));
        }
        return list;
    }

    /// <summary>
    /// Der Betrag einer Bestueckung: <c>d + (e + f·(v[c] / 2))·v[c]</c>.
    ///
    /// <para>Drei Formen kommen im Spiel vor, und alle drei passen hier hinein:
    /// M2/M3/M6 schreiben <c>v + konstante</c> (e = 1, f = 0), M25 rechnet mit
    /// einem Faktor (<c>3v + 80</c> und <c>194 − 3v</c>), und M33 rechnet
    /// QUADRATISCH — <c>(19 − v/2)·v + 50</c> fuer die Waffen und
    /// <c>(v/2)·v + 70</c> fuer das Fahrwerk, ueber sechs Gebaeude
    /// (@0x4A55EE ff.).</para>
    ///
    /// <para>⚠ <c>v / 2</c> und nicht <c>v &gt;&gt; 1</c>: das Original rundet mit
    /// <c>cdq; sub eax,edx; sar eax,1</c> zur Null hin, und genau das tut die
    /// ganzzahlige Division in C#. Bei v[88] = 0..19 macht es keinen
    /// Unterschied, aber die Herleitung soll stimmen und nicht nur das
    /// Ergebnis.</para>
    ///
    /// <para>⚠ M33 stand einen halben Tag lang als <c>v[88] + 70</c> in der
    /// Datei, weil der Ausleser ein <c>imul</c> uebersah — und der Pruefstand
    /// merkte es nicht, weil er seinen Sollwert aus derselben Lesung zog. Siehe
    /// die Zurueckziehung in GAMESTATE_RE 3.87.</para>
    /// </summary>
    public int StockWert(Act a)
    {
        if (a.C < 0 || a.C >= _var.Length) return a.D;
        int v = _var[a.C];
        return a.D + (a.E + a.F * (v / 2)) * v;
    }

    /// <summary>Der Wert einer Missionsvariablen — für den Prüfstand, der den
    /// Sollwert nachrechnen muss.</summary>
    public int VarAt(int n) => n >= 0 && n < _var.Length ? _var[n] : 0;

    /// <summary>Jede bestueckende Regel mit ihrem Zustand, Glied fuer Glied.
    /// ⚠ Ein Pruefstand, der schweigt, muss sagen, WELCHES Glied nicht
    /// schliesst — sonst ist eine Kette, die nicht zumacht, nicht von einer zu
    /// unterscheiden, die es fast tut (Arbeitsweise 9).</summary>
    public string WhyNotStock()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var r in _script.Rules)
        {
            bool stockt = false;
            foreach (var a in r.Then) if (a.Kind == "stock") { stockt = true; break; }
            if (!stockt) continue;
            sb.Append($"\n      Regel @0x{r.At:X}: ");
            sb.Append(r.Once >= 0
                      ? $"Sperre v[{r.Once}]={VarAt(r.Once)}{(VarAt(r.Once) != 0 ? " ZU" : " offen")} | "
                      : "keine Sperre | ");
            foreach (var c in r.When)
                sb.Append($"{Show(c)} {(TestReal(c) ? "JA" : "NEIN")} · ");
            foreach (var c in r.Any)
                sb.Append($"ODER {Show(c)} {(TestReal(c) ? "JA" : "NEIN")} · ");
        }
        return sb.ToString();
    }

    /// <summary>Jede Einsetzung, die dieses Skript trägt, als Zahlenreihe —
    /// für den Prüfstand, der sie gegen den Kartenrahmen hält. Reihenfolge wie
    /// in der Datei, also wie im Block.</summary>
    public List<(int Design, int Col, int Row, int Player, int At)> PlaceSites()
    {
        var list = new List<(int, int, int, int, int)>();
        foreach (var r in _script.Rules)
            foreach (var a in r.Then)
                if (a.Kind == "place_unit") list.Add((a.A, a.B, a.C, a.D, r.At));
        return list;
    }

    private MissionScript(Script s)
    {
        _script = s;
        foreach (var kv in s.Init)
            if (kv.Key >= 0 && kv.Key < _var.Length) _var[kv.Key] = kv.Value;
    }

    /// <summary>The script of a mission, or null when none is carried for it.
    /// A missing script is not an error — most of the 33 are not translated
    /// yet, and a mission without one simply never ends by itself.
    ///
    /// <para>⚠ 14.08.2026 — <b>eine fehlende Mission schweigt nicht mehr.</b>
    /// Bis heute gab <c>For</c> stillschweigend null zurueck, und im Protokoll
    /// stand nur »31 geladen«. Damit war »fuer diese Mission ist noch keine
    /// Regel uebersetzt« von »die Datei ist kaputt« nicht zu unterscheiden —
    /// genau die Falle, vor der der Kopf dieser Datei warnt. Siehe
    /// <see cref="WhyNoScript"/>.</para></summary>
    public static MissionScript? For(int mission)
    {
        var all = Load();
        if (!all.TryGetValue(mission, out var s))
        {
            Current = null;
            Sagen(mission);
            return null;
        }
        Current = new MissionScript(s);
        return Current;
    }

    /// <summary>Wovon schon berichtet wurde — eine Mission ohne Skript sagt
    /// ihren Grund EINMAL je Sitzung, nicht bei jedem <see cref="For"/>.
    /// </summary>
    private static readonly HashSet<int> _gesagt = new();

    /// <summary>
    /// Die Zeile, mit der eine Mission OHNE Skript sich meldet.
    ///
    /// <para>Sie muss zwei Faelle auseinanderhalten, die sonst gleich
    /// aussehen:</para>
    /// <list type="bullet">
    /// <item><b>Gelesen, aber nicht ausdrueckbar.</b> Das Original HAT einen
    /// Block — die Endregel steht unter <c>_endregel_aus</c> —, und was fehlt,
    /// ist eine Vokabel dieser Datei. Dann ist die Mission ohne Skript
    /// RICHTIG, und die Zeile nennt das fehlende Glied.</item>
    /// <item><b>Gar nichts vermerkt.</b> Dann ist es eine Luecke, und die Zeile
    /// geht als Fehler heraus.</item>
    /// </list>
    /// </summary>
    private static void Sagen(int mission)
    {
        if (!_gesagt.Add(mission)) return;
        var u = WhyNoScript(mission);
        if (u == null)
        {
            GD.PrintErr($"Missionsskript M{mission}: KEIN SKRIPT und KEIN GRUND vermerkt " +
                        "— weder in `missions` noch in `_gelesen_ungebaut` von " +
                        $"{Path}. Die Notregel entscheidet die Mission.");
            return;
        }
        GD.Print($"Missionsskript M{mission}: kein Skript — und das ist RICHTIG, " +
                 "nicht kaputt. Das Original hat sehr wohl einen Block " +
                 $"(Endregel @{u.EndAt}); was fehlt, ist eine VOKABEL dieser Datei.\n" +
                 $"   fehlt:   {u.Fehlt}\n" +
                 $"   gelesen: {u.Gelesen}\n" +
                 (u.Objectg.Length > 0 ? $"   Ziel:    {u.Objectg}\n" : "") +
                 "   Bis dahin entscheidet die Notregel (MapEntityLayer.Verdict).");
    }

    /// <summary>
    /// Warum diese Mission kein Skript traegt — aus <c>_gelesen_ungebaut</c>
    /// der Datei, nicht aus dieser Klasse.
    ///
    /// <para>⚠ <b>Warum das nicht hier im Code steht.</b> Der Text ist ein
    /// LESEERGEBNIS aus GAME.EXE und gehoert neben die Regeln, die aus derselben
    /// Quelle kommen. Eine zweite Abschrift im Code waere eine zweite Wahrheit,
    /// und die veraltet still.</para>
    ///
    /// <para>⚠ <b>Stand 14.08.2026, gemessen auf BEIDEN GAME.EXE dieses
    /// Rechners.</b> Es fehlen genau zwei Missionen, <b>21</b> und <b>28</b>,
    /// und beide haben im Original einen Block — es sind sogar die <b>zwei
    /// groessten</b> (M28 3600 B / 115 Aufrufe, M21 3447 / 123, je ein
    /// <c>mission_end</c>). Uebergangen werden sie nicht beim Laden, sondern
    /// schon beim SCHREIBEN: <c>mission_endrules.agreed()</c> behaelt nur, was
    /// eine nichtleere Bedingungsliste hergibt, und
    /// <c>mission_logic._chain</c> gibt fuer beide <c>None</c> zurueck:</para>
    /// <list type="bullet">
    /// <item><b>M21</b> (C: 0x4A02C0, F: 0x49FBCC) ist eine SCHLEIFE ueber neun
    /// feste Bahnverbindungen — die Nummern stehen im Klartext im Rumpf
    /// (C: 0x4A01F5.., F: 0x49FB01..): 4, 7, 9, 12, 14, 17, 15, 16, 18, in
    /// beiden Fassungen dieselben. Der Leser bricht bei einem RUECKWAERTSsprung
    /// ab, statt etwas Falsches zu liefern. Ohne SPOJ als Spielobjekt ist die
    /// Regel nicht zu bauen.</item>
    /// <item><b>M28</b> (C: 0x4A3786, F: 0x4A3092) ist KEINE Schleife, sondern
    /// ein <b>ODER aus DREI UND-Gruppen</b> — der Leser sieht drei verschiedene
    /// Ausstiegsziele (C: 0x4A36EA / 0x4A3738 / 0x4A378B) und gibt darum auf.
    /// Siehe <see cref="Rule.Any"/>: unser ODER verbindet einzelne GLIEDER, das
    /// hier verbindet GRUPPEN.</item>
    /// </list>
    ///
    /// <para>⚠ <b>ZURUECKGEZOGEN — der Eintrag »28« der Datei liest die Regel zu
    /// kurz.</b> Dort steht <c>unit_alive(v[56], 0xC1)</c> und die Zelle
    /// (11, 243), und das ist nur der DRITTE der drei Arme. Vollstaendig, auf
    /// beiden Fassungen Zeichen fuer Zeichen gleich:</para>
    /// <code>
    ///   (v[51]==2 &amp;&amp; alive(v[54],193) &amp;&amp; v[54].col==11 &amp;&amp; v[54].row==243)   ; C 0x4A3698
    /// ODER (v[52]==2 &amp;&amp; alive(v[55],193) &amp;&amp; v[55].col==11 &amp;&amp; v[55].row==243)   ; C 0x4A36EA
    /// ODER (v[51]==2 &amp;&amp; alive(v[56],193) &amp;&amp; v[56].col==11 &amp;&amp; v[56].row==243)   ; C 0x4A3738
    /// </code>
    /// <para>Entwurf 193 ist der »Scientist«, und drei Arme sind DREI
    /// WISSENSCHAFTLER — genau das, was Ziel #028 verlangt (»Bringen Sie die
    /// Wissenschaftler mit den Informationen zurueck«). Der Block holt sie sich
    /// selbst: eine Schleife ueber die Einheiten 0..2 (C: 0x4A30A8..0x4A3130)
    /// prueft <c>unit_has_mark(i, 193)</c> und ob die Einheit im Fenster
    /// Spalte 35..38 / Zeile 9..14 steht — dem WRACK —, entfernt sie dort
    /// (<c>remove_unit</c> @0x4A310F), merkt sich <c>v[61+i] = game_time()</c>,
    /// und eine Spielminute spaeter setzt sie
    /// <c>v[54+i] = place_unit(193, 36, 12, 0)</c> (C: 0x4A3163, 0x4A31AF,
    /// 0x4A31FB). (11, 243) liegt in map_28 (80 x 254) — bei vertauschter
    /// Lesung laege es ausserhalb, das Kartenmass belegt also »Spalte, Zeile«
    /// mit 1 von 1.</para>
    ///
    /// <para>⚠ Und eine Ungereimtheit des ORIGINALS, wie die <c>jge</c> in
    /// Mission 5: der dritte Arm prueft wieder <c>v[51]</c> statt <c>v[53]</c>
    /// (C: 0x4A3738 liest 0xBC56F6, F: 0x4A3044 liest 0xBC4756 — beide Male
    /// v[51], waehrend Arm 2 v[52] nimmt). v[53] wird nur in der Stufenmaschine
    /// hochgezaehlt und nirgends als Waechter benutzt. Nachgebaut wird, was
    /// dasteht.</para>
    ///
    /// <para><b>Was M28 also braucht</b>, wenn es einmal gebaut wird — drei
    /// Dinge, keins davon eine Deutung: (1) ein ODER ueber GRUPPEN neben dem
    /// heutigen ODER ueber Glieder, (2) <c>v[a] = place_unit(...)</c>, denn das
    /// Original merkt sich den zurueckgegebenen Satzindex, unser Haken
    /// <see cref="PlaceUnit"/> gibt nichts zurueck, und (3) drei entrollte
    /// Fassungen der Einheitenschleife 0..2. <c>unit_is_var</c> und
    /// <c>unit_field</c> gibt es seit dem 13.08. schon.</para>
    /// </summary>
    public static Ungebaut? WhyNoScript(int mission)
    {
        Load();
        return _ungebaut != null && _ungebaut.TryGetValue(mission, out var u) ? u : null;
    }

    /// <summary>Ein Eintrag aus <c>_gelesen_ungebaut</c>: die Siegbedingung ist
    /// GELESEN, aber mit dem Vokabular dieser Datei nicht zu schreiben.</summary>
    public sealed class Ungebaut
    {
        public int Mission;
        public string EndAt = "", Gelesen = "", Fehlt = "", Objectg = "";
    }

    private static Dictionary<int, Ungebaut>? _ungebaut;

    /// <summary>Das Skript der laufenden Mission — nur fuer den PRÜFSTAND, der
    /// sonst keinen Weg an die Instanz haette (sie haengt privat in
    /// Rendering/MapEntityLayer). Im Spiel benutzt das niemand.</summary>
    public static MissionScript? Current { get; private set; }

    private static Dictionary<int, Script>? _cache;

    private static Dictionary<int, Script> Load()
    {
        if (_cache != null) return _cache;
        _cache = new Dictionary<int, Script>();
        if (!FileAccess.FileExists(Path)) return _cache;
        using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (f == null) return _cache;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return _cache;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        // ZUERST die Begruendungen, denn sie muessen auch dann dastehen, wenn
        // `missions` selbst leer ist — sonst schweigt genau der Fall, der
        // erklaert werden soll.
        _ungebaut = LeseUngebaut(root);
        if (!root.TryGetValue("missions", out var mv) ||
            mv.VariantType != Variant.Type.Dictionary) return _cache;

        foreach (var kv in mv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int m)) continue;
            var body = kv.Value.AsGodotDictionary<string, Variant>();
            var s = new Script { Mission = m };
            if (body.TryGetValue("block", out var bv)) s.Block = bv.AsString();
            if (body.TryGetValue("gate", out var gv)) s.Gate = Hex(gv.AsString());
            if (body.TryGetValue("init", out var iv) &&
                iv.VariantType == Variant.Type.Dictionary)
                foreach (var e in iv.AsGodotDictionary<string, Variant>())
                    if (int.TryParse(e.Key, out int n)) s.Init[n] = e.Value.AsInt32();
            // Die Rohstoffvorkommen. Siehe Script.Terra — sie kommen aus dem
            // Setup-Block, nicht aus der Karte.
            if (body.TryGetValue("terra", out var tev) &&
                tev.VariantType == Variant.Type.Array)
                foreach (var e in tev.AsGodotArray())
                {
                    if (e.VariantType != Variant.Type.Array) continue;
                    var q = e.AsGodotArray();
                    if (q.Count < 3) continue;
                    s.Terra.Add((q[0].AsInt32(), q[1].AsInt32(), q[2].AsInt32()));
                }
            // Die Zellen, die beim Missionsstart einen TREFFER bekommen — siehe
            // Script.Treffer.
            if (body.TryGetValue("treffer", out var hv) &&
                hv.VariantType == Variant.Type.Array)
                foreach (var e in hv.AsGodotArray())
                {
                    if (e.VariantType != Variant.Type.Array) continue;
                    var q = e.AsGodotArray();
                    if (q.Count < 2) continue;
                    s.Treffer.Add((q[0].AsInt32(), q[1].AsInt32()));
                }
            if (!body.TryGetValue("rules", out var rv) ||
                rv.VariantType != Variant.Type.Array) continue;
            foreach (var r in rv.AsGodotArray())
            {
                var rd = r.AsGodotDictionary<string, Variant>();
                var rule = new Rule();
                if (rd.TryGetValue("once", out var ov)) rule.Once = ov.AsInt32();
                if (rd.TryGetValue("_at", out var av)) rule.At = Hex(av.AsString());
                // Vor dem Tor: jeden Takt. Dahinter — und ebenso jede Regel
                // ohne Adresse — im langsamen Takt. Ohne Adresse sind genau die
                // 31 ENDregeln, und die stehen im Original am Blockende, also
                // hinter dem Tor.
                rule.EveryTick = s.Gate > 0 && rule.At > 0 && rule.At < s.Gate;
                if (rd.TryGetValue("when", out var wv) && wv.VariantType == Variant.Type.Array)
                    foreach (var c in wv.AsGodotArray())
                    {
                        var cd = c.AsGodotDictionary<string, Variant>();
                        rule.When.Add(new Cond
                        {
                            Kind = cd.TryGetValue("kind", out var k) ? k.AsString() : "",
                            A = cd.TryGetValue("a", out var a) ? a.AsInt32() : 0,
                            B = cd.TryGetValue("b", out var b) ? b.AsInt32() : 0,
                            C = cd.TryGetValue("c", out var c2) ? c2.AsInt32() : 0,
                            Op = cd.TryGetValue("op", out var o) ? o.AsString() : "==",
                        });
                    }
                // Die ODER-Glieder. Siehe Rule.Any — ohne sie fehlten drei
                // Regeln ganz, und M3s Regel sähe bedingungslos aus.
                if (rd.TryGetValue("any", out var yv) && yv.VariantType == Variant.Type.Array)
                    foreach (var c in yv.AsGodotArray())
                    {
                        var cd = c.AsGodotDictionary<string, Variant>();
                        rule.Any.Add(new Cond
                        {
                            Kind = cd.TryGetValue("kind", out var k) ? k.AsString() : "",
                            A = cd.TryGetValue("a", out var a) ? a.AsInt32() : 0,
                            B = cd.TryGetValue("b", out var b) ? b.AsInt32() : 0,
                            C = cd.TryGetValue("c", out var c2) ? c2.AsInt32() : 0,
                            Op = cd.TryGetValue("op", out var o) ? o.AsString() : "==",
                        });
                    }
                // Das ODER über GRUPPEN — siehe Rule.AnyGroups. ⚠ Eine Gruppe
                // ohne Glieder wird NICHT übernommen: sie wäre immer wahr und
                // machte die ganze Regel bedingungslos.
                if (rd.TryGetValue("any_groups", out var ggv) && ggv.VariantType == Variant.Type.Array)
                    foreach (var g in ggv.AsGodotArray())
                    {
                        if (g.VariantType != Variant.Type.Array) continue;
                        var gruppe = new List<Cond>();
                        foreach (var c in g.AsGodotArray())
                        {
                            var cd = c.AsGodotDictionary<string, Variant>();
                            gruppe.Add(new Cond
                            {
                                Kind = cd.TryGetValue("kind", out var k) ? k.AsString() : "",
                                A = cd.TryGetValue("a", out var a) ? a.AsInt32() : 0,
                                B = cd.TryGetValue("b", out var b) ? b.AsInt32() : 0,
                                C = cd.TryGetValue("c", out var c2) ? c2.AsInt32() : 0,
                                Op = cd.TryGetValue("op", out var o) ? o.AsString() : "==",
                            });
                        }
                        if (gruppe.Count > 0) rule.AnyGroups.Add(gruppe);
                    }
                if (rd.TryGetValue("then", out var tv) && tv.VariantType == Variant.Type.Array)
                    foreach (var a in tv.AsGodotArray())
                    {
                        var ad = a.AsGodotDictionary<string, Variant>();
                        var act = new Act
                        {
                            Kind = ad.TryGetValue("kind", out var k) ? k.AsString() : "",
                            A = ad.TryGetValue("a", out var x) ? x.AsInt32() : 0,
                            B = ad.TryGetValue("b", out var y) ? y.AsInt32() : 0,
                            C = ad.TryGetValue("c", out var z) ? z.AsInt32() : 0,
                            D = ad.TryGetValue("d", out var w) ? w.AsInt32() : 0,
                            E = ad.TryGetValue("e", out var e5) ? e5.AsInt32() : 0,
                            F = ad.TryGetValue("f", out var f5) ? f5.AsInt32() : 0,
                            V = ad.TryGetValue("v", out var v5) ? v5.AsInt32() : -1,
                        };
                        if (ad.TryGetValue("typen", out var ty) &&
                            ty.VariantType == Variant.Type.Array)
                        {
                            var arr = ty.AsGodotArray();
                            var list = new int[arr.Count];
                            for (int n = 0; n < arr.Count; n++) list[n] = arr[n].AsInt32();
                            act.Types = list;
                        }
                        rule.Then.Add(act);
                    }
                s.Rules.Add(rule);
            }
            _cache[m] = s;
        }
        // ⚠ Die Zeile nennt jetzt den NENNER und die Luecken. »31 geladen« sagte
        // nicht, ob zwei fehlen oder zwei zuviel da sind, und wer die Zahl
        // nicht auswendig kann, liest daraus gar nichts.
        var fehlen = new List<int>();
        for (int m = 1; m <= Import.ExeTables.CampaignMissions; m++)
            if (!_cache.ContainsKey(m)) fehlen.Add(m);
        GD.Print($"Missionsskripte: {_cache.Count} von {Import.ExeTables.CampaignMissions} geladen" +
                 (fehlen.Count == 0
                     ? ""
                     : $" — ohne Skript: M{string.Join(", M", fehlen)} " +
                       $"({Begruendet(fehlen)} davon mit vermerktem Grund, " +
                       "siehe MissionScript.WhyNoScript)"));
        return _cache;
    }

    /// <summary>Wieviele der fehlenden Missionen einen vermerkten Grund haben.
    /// Steht in der Ladezeile, damit »kein Skript« und »keine Ahnung warum«
    /// schon dort auseinanderfallen.</summary>
    private static int Begruendet(List<int> fehlen)
    {
        int n = 0;
        foreach (int m in fehlen)
            if (_ungebaut != null && _ungebaut.ContainsKey(m)) n++;
        return n;
    }

    /// <summary>
    /// <c>_gelesen_ungebaut</c> aus der Datei — die Missionen, deren
    /// Siegbedingung GELESEN ist, aber mit dem Vokabular dieser Datei nicht
    /// geschrieben werden kann.
    ///
    /// <para>Schluessel, die keine Missionsnummer sind (<c>_note</c>), fallen
    /// heraus; ein Eintrag ohne Zahl ist keine Mission.</para>
    /// </summary>
    private static Dictionary<int, Ungebaut> LeseUngebaut(
        Godot.Collections.Dictionary<string, Variant> root)
    {
        var d = new Dictionary<int, Ungebaut>();
        if (!root.TryGetValue("_gelesen_ungebaut", out var gv) ||
            gv.VariantType != Variant.Type.Dictionary) return d;
        foreach (var kv in gv.AsGodotDictionary<string, Variant>())
        {
            if (!int.TryParse(kv.Key, out int m)) continue;
            if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
            var b = kv.Value.AsGodotDictionary<string, Variant>();
            d[m] = new Ungebaut
            {
                Mission = m,
                EndAt = Text(b, "_endregel_aus"),
                Gelesen = Text(b, "gelesen"),
                Fehlt = Text(b, "fehlt"),
                Objectg = Text(b, "objectg"),
            };
        }
        return d;
    }

    private static string Text(Godot.Collections.Dictionary<string, Variant> d, string k)
        => d.TryGetValue(k, out var v) ? v.AsString() : "";

    /// <summary>"0x499197" -> 0x499197. Was sich nicht lesen laesst, wird 0 —
    /// und 0 heisst ueberall in dieser Datei »unbekannt«, nicht »Adresse 0«.
    /// </summary>
    private static int Hex(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.Trim();
        if (s.StartsWith("0x") || s.StartsWith("0X")) s = s[2..];
        return int.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out int v)
            ? v : 0;
    }

    // ---- what the world has to answer -------------------------------------

    /// <summary>The four questions a rule can ask of the running game. Supplied
    /// by the layer that owns the entities, so this file stays free of them.
    /// </summary>
    public Func<int, int>? ObjOwner;                 // building slot -> owner
    public Func<int, int, int>? UnitCount;           // class, player -> count
    public Func<int, int, int>? BuildingCount;       // class, player -> count
    /// <summary>`count_objects(typ, besitzer)` @0x4CFA70 — laeuft ueber 255
    /// Saetze der Objekttabelle 0xc06910 und zaehlt die mit passendem Typ in
    /// `+4` und Besitzer in `+5`. Die haeufigste Endbedingung der Kampagne.
    /// </summary>
    public Func<int, int, int>? ObjectCount;         // type, owner -> count
    public Action<int, int, int, int>? ShowText;     // id, art, x, y (640x480)

    /// <summary>`find_unit(spieler, marke)` @0x4D0F20 — the index of that
    /// player's first unit carrying <b>marke</b> in record byte +0x43, or
    /// 0xFFFF. That byte is how the campaign points at ONE named unit: map_03
    /// carries exactly one with 193 and mission 3 asks for 193, map_06 one with
    /// 194 and mission 6 asks for 194, and so on through fifteen missions.
    /// Empty records (+0x09 == 0xFF) and busy ones (ukol +0x14 >= 100) are
    /// skipped.</summary>
    public Func<int, int, int>? FindUnit;            // player, mark -> index

    /// <summary>A byte of one unit's record, by the index `find_unit` returned.
    /// Only +0x00 (column) and +0x01 (row) are answered — those are the two the
    /// campaign asks about, and they are the ones the engine holds. Anything
    /// else comes back -1 rather than a guess.</summary>
    public Func<int, int, int>? UnitField;           // index, offset -> byte

    /// <summary>A word out of one building record (255 x 76 @0xc06914). Only
    /// the four stores are answered: +0x28 Waffen, +0x2a Fahrwerk, +0x2c
    /// Spezial parts and +0x2e raw Terranium (GAMESTATE_RE 3.82). Mission 5
    /// marks two of them at its start and wins when BOTH have grown — which is
    /// objective #005 word for word, "Wiederaufnahme der Produktion".</summary>
    public Func<int, int, int>? StoreField;          // building slot, offset -> value

    /// <summary>
    /// <b>Ein Lager eines Gebaeudes SETZEN</b> — die Wirkung, die B6 gefehlt hat
    /// (gelesen am 16.08.2026).
    ///
    /// <para>Mission 3, Regel 11 @0x4997FD: sobald der Spieler Gebaeudeplatz 0
    /// (Horni) besitzt UND eine seiner Einheiten auf Zelle (7,36) steht — der
    /// Tuer —, schreibt das Original zwei Worte in den Gebaeudesatz:
    /// <c>word[0xc0693c] = v[88] + 180</c> und
    /// <c>word[0xc0693e] = v[88] + 127</c>. Das sind die Teilelager Waffen und
    /// Fahrwerk. Regel 12 tut dasselbe fuer Dolni mit 330 und 237.</para>
    ///
    /// <para>Dieselbe Form steht 18-mal im Spiel: M2 (zwei Gebaeude), M3 (zwei),
    /// M6 (zwei) und M33 (sechs, je 70 Fahrwerkteile). Es ist also keine
    /// Sonderregel von Mission 3, sondern die uebliche Art, ein eingenommenes
    /// Werk mit einem Anfangsbestand zu versehen.</para>
    ///
    /// <para>⚠ <b>v[88] ist der umlaufende Taktzaehler</b>, den jeder dieser
    /// Bloecke als erstes hochzaehlt (0..19, @0x49940B). Der Betrag schwankt
    /// deshalb um bis zu 19 — das ist im Original so und keine Nachlaessigkeit
    /// von uns; er wird als <c>v[..] + Konstante</c> uebernommen und nicht zu
    /// einer festen Zahl geglaettet.</para>
    ///
    /// <para>⚠ OFFEN und ausgeschildert: das <c>inc v[88]</c> steht im
    /// BLOCKVORSPANN, nicht in einer Regel, und der Regelleser faengt erst bei
    /// der ersten Bedingung an. Bei uns bleibt v[88] deshalb auf 0, und der
    /// Betrag ist immer das untere Ende des Fensters (180 statt 180..199).
    /// Gemessen, nicht geschaetzt — <c>--stock-check</c> druckt v[88] mit. Das
    /// zu heilen heisst, dem Leser beizubringen, wo ein Block anfaengt, und das
    /// ist ein eigener Durchgang.</para>
    ///
    /// <para>⚠⚠ <b>Die Feldnummer ist gegen 0xc06914 gerechnet, der Satz beginnt
    /// aber bei 0xc06910.</b> +0x28 hier ist also in Wahrheit Satz +0x2c. Die
    /// Zaehlung ist alt und durchgaengig — Ausleser wie Laufzeit rechnen gleich,
    /// es ist kein Fehler —, aber genau sie hat B6 eine Sitzung lang wie ein
    /// ungelesenes Feld aussehen lassen.</para>
    /// </summary>
    public Action<int, int, int>? SetStoreField;     // building slot, offset, value

    /// <summary>GEGENPROBE <c>--no-stock</c>: den Stand vor dem 16.08.2026
    /// nachstellen — die Regel feuert, ihre Wirkung ist leer. Genau das war B6,
    /// und der Prueflauf MUSS damit durchfallen (Arbeitsweise 31).</summary>
    public static bool StockOld;

    /// <summary>`space_in` puts one unit of design <b>typ</b> on the map for
    /// <b>player</b>, at or next to (col, row) — @0x4C1600 asks @0x4012AD for a
    /// free place beside the cell and gives up with "Incredible error ...no free
    /// place for new robot" when there is none. The design number is the sec47
    /// row <c>typ + 200*player</c>.</summary>
    public Action<int, int, int, int>? SpaceInSpawn;   // typ, col, row, player

    // ---- 11.08.2026: was der tutorialartige Ablauf zusätzlich braucht -------
    // Jeder Haken darf fehlen; dann ist die Bedingung falsch bzw. die Wirkung
    // bleibt aus, und `Line()` sagt es. Lieber eine sichtbare Lücke als eine
    // Regel, die etwas anderes tut als das Original.
    public Func<int>? Selection;                     // -> angewähltes Objekt
    public Func<int, int, int>? MarkCount;           // marke, spieler -> Anzahl
    public Func<int, int, bool>? UnitHasMark;        // einheit, marke
    public Func<int, int>? MoneyOf;                  // spieler -> Kontostand

    /// <summary>AUSGESCHALTET und VERLUSTE des Spielers — die zwei Zahlen aus
    /// dem Original-HUD (»AUSGESCHALTET 17 / VERLUSTE 0«).
    /// <para>Im Original zwei Zähler in einem Satz von 40 Byte je Spieler
    /// (C: 0x87B160 / 0x87B164). AUSGESCHALTET erhöht die Trefferroutine beim
    /// <b>Angreifer</b>, sobald das Ziel stirbt (@0x40D20E, ohne Angreifer —
    /// 0xFF — wird nicht gezählt); VERLUSTE erhöht der Tod der Einheit selbst
    /// beim <b>Besitzer</b> (@0x40B4D7, Besitzer = Satzindex / 1000).
    /// Dieselbe Lage steht unabhängig davon in der Kartendatei, siehe
    /// <c>CwmExtra</c> (+0x20 / +0x24 bei Satzweite 40).</para></summary>
    /// <summary>Ist der GEBAUTE-BRÜCKEN-Platz k belegt? (1 ja, 0 frei)
    /// <para>Im Original eine Tafel von 100 Sätzen zu 24 Byte (C: 0xBFEA80,
    /// F: 0xBFDAE0, Abschnitt 17 der Kartendatei); +0x12 ist das Kennzeichen,
    /// und der Vergeber nimmt den ersten Platz mit +0x12 == 0. Den Namen gibt
    /// das Spiel selbst: die Routine, die gleich danach in dieser Tafel
    /// nachschlägt, protokolliert sich als »Erase bridge«.</para></summary>
    public Func<int, int>? BridgeUsed;               // platz -> belegt?

    /// <summary>Steht auf Einheitenplatz <paramref name="a"/> eine lebende
    /// Einheit? Im Original ist das Feld <c>+0x09</c> des Satzes: <b>0xFF
    /// heisst leer</b>, und genau daran überspringt <c>find_unit</c> die
    /// leeren Sätze. Mission 28 fragt zweimal so (@0x4A2BBF, @0x4A2C05).</summary>
    public Func<int, bool>? UnitAlive;               // satz -> lebt?

    /// <summary>BEWEGEN — Befehl 3 des Originals (<c>0x4D0860</c>, 46
    /// Aufrufstellen in den Missionsblöcken). P1 = Einheitsnummer, P2/P3 =
    /// Zielzelle; siehe <see cref="Simulation.Commands.CommandOp.Move"/>,
    /// wo die Deutung von zwei Seiten belegt ist.</summary>
    public Action<int, int, int>? MoveUnit;          // einheit, x, y

    /// <summary>Den KI-Modus eines Spielers setzen — <c>byte[0x538BB8 +
    /// spieler] = wert</c> (<c>0x4D0EE0</c>). Dass es die KI ist, sagt das
    /// Spiel selbst: die Funktion, die diese Tafel liest, protokolliert sich
    /// als »AI« / »AI end« (@0x4BFB8C, @0x4BFE3A).</summary>
    public Action<int, int>? SetAi;                  // spieler, wert

    /// <summary>FEUERN AUF EINE ZELLE — <c>0x4D0AD0</c>, 7 Aufrufstellen.
    /// Siehe <c>MissionFireAt</c> für die Beweiskette und für das, was dabei
    /// unsere Setzung bleibt.</summary>
    public Action<int, int, int>? FireAt;            // einheit, x, y

    public Func<int, int>? KillCount;                // spieler -> ausgeschaltet
    public Func<int, int>? LossCount;                // spieler -> Verluste

    /// <summary>
    /// <b>Hält Spieler <i>p</i> die Bahnverbindung <i>n</i>?</b> — der Haken,
    /// an dem Mission 21 hängt. <c>(Linie, Spieler)</c>.
    ///
    /// <para>Die Frage ist die des Originals, Befehl für Befehl (@0x4A0224,
    /// F: @0x49FB30): der Satz der Linie liegt in einer Tafel mit
    /// <b>Schrittweite 214</b>, und geprüft werden <b>drei</b> Dinge:</para>
    /// <code>
    ///   byte[Satz+0xD5] == 3   -> NEIN      die »faze«, siehe RailFreight
    ///   je Endknoten:  Gebäude = word[Knotentafel + 8·Knoten]
    ///                  byte[Gebäude+0x05] != Spieler -> NEIN   (Besitzer)
    ///                  byte[Gebäude+0x04] == 0       -> NEIN   (gibt es nicht)
    /// </code>
    /// <para>⚠ Der Spieler ist im Original <b>fest 0</b> (<c>test al,al</c>) —
    /// er steht hier trotzdem als Parameter, damit die Bedingung nicht
    /// heimlich vom Menschen ausgeht.</para></summary>
    public Func<int, int, bool>? RailLinkHeld;
    public Func<int, int, int>? TerrainAt;           // x, y -> Geländebyte
    public Func<int, int, int>? ImapAt;              // spalte, zeile -> Belegung
    public Action<int, int>? AddMoney;               // betrag, spieler
    public Action<int>? PlaySound;                   // 600 / 601
    public Action? CloseTexts;                       // close_message_windows()

    /// <summary><c>window_open(n)</c> @0x4D0600 — <b>IST HILFEFENSTER n NOCH
    /// OFFEN?</b>
    ///
    /// <para>⚠ 18.08.2026. Das ist der TAKTGEBER des Tutorials, und er hat
    /// gefehlt. Der Rumpf (0x4D0600 → 0x4475B0) läuft das Fensterfeld ab —
    /// Basis 0x8B9038, Schrittweite 0xAD24, zwölf Plätze — und gibt 1 zurück,
    /// wenn ein Fenster der Art 0x1E (30) mit +0xACA0 == n dabei ist. Dieselbe
    /// Art 30, die <c>close_message_windows()</c> @0x447560 wegräumt.</para>
    ///
    /// <para>Mission 1 schaltet daran ihre Kette weiter: #002 kommt erst, wenn
    /// der Spieler #001 weggeklickt hat, #012 erst nach #011. Ohne diese Frage
    /// fielen beide Regeln beim Lesen weg — im Spiel sah das aus, als bliebe
    /// das Tutorial nach dem ersten Fenster einfach stehen.</para>
    ///
    /// <para>Der Haken hängt <b>von Haus aus</b> an <see cref="UI.HelpWindow"/>
    /// — anders als die übrigen, die die Kartenschicht einträgt. Grund: die
    /// Antwort steht vollständig in unserem eigenen Fensterverwalter, es gibt
    /// nichts, was die Welt dazu beitragen könnte. Ein Prüfstand darf ihn
    /// trotzdem überschreiben.</para></summary>
    public Func<int, bool>? TextOpen = UI.HelpWindow.IsOpen;

    public Action<int, int, int, int>? OrderUnit;    // einheit, ukol, x, y

    /// <summary>
    /// `place_unit(entwurf, spalte, zeile, spieler)` @0x4D0810 — <b>60 Aufrufe
    /// in 14 Missionen</b>, die grösste Lücke im Vokabular der Kampagne, bis
    /// zum 13.08.2026 ungebaut.
    ///
    /// <para>Der Rumpf ruft genau zwei Unterprogramme: 0x4C13E0 sucht eine freie
    /// Zelle neben (spalte, zeile), 0x4B34E0 legt den Satz an. Letzteres ist
    /// dasselbe `create_unit`, in das auch `space_in` am Ende seines Anflugs
    /// mündet (@0x4C0260 → @0x4C1600 → @0x4D0810 → @0x4B34E0) — der Unterschied
    /// ist allein die Warteschlange: <b>place_unit setzt SOFORT</b>.</para>
    ///
    /// <para>Die Argumentreihenfolge ist gemessen, nicht geraten: alle 60
    /// Aufrufstellen tragen vier Konstanten, auf beiden GAME.EXE dieselben.
    /// Argument 3 liegt bei allen 60 zwischen 0 und 7 (Spieler), Argument 0
    /// zwischen 50 und 194 (Entwurf), und von 40 nachgerechneten Zellen liegen
    /// 40 im Kartenrahmen, wenn Argument 1 die SPALTE ist — gegen 34, wenn es
    /// die Zeile wäre.</para>
    /// </summary>
    /// <summary>⚠ Gibt den SATZINDEX zurück, −1 wenn nichts entstanden ist.
    /// Das Original reicht den Rückgabewert von <c>create_unit</c> @0x4B34E0
    /// durch, und M28 merkt ihn sich (<c>v[54+i] = place_unit(193, 36, 12, 0)</c>
    /// @0x4A3163) — ohne ihn ist die Mission nicht baubar. Bis zum 14.08.2026
    /// war das ein <c>Action</c> und warf den Wert weg.</summary>
    public Func<int, int, int, int, int>? PlaceUnit;    // entwurf, spalte, zeile, spieler

    /// <summary>
    /// `order(einheit, cx, cy, utok_na, extra)` @0x410220 — <b>7 Aufrufe in
    /// EINER Mission</b> (Kampagne 2), und etwas anderes als
    /// <see cref="OrderUnit"/>: es geht nicht über den Befehlsbus, sondern
    /// schreibt den Einheitensatz direkt.
    ///
    /// <para>⚠ <b>Die Feldnamen sind die des SPIELS.</b> Der Debug-Dump @0x416F00
    /// druckt jedes Feld mit seiner Beschriftung, und daran hängt die ganze
    /// Deutung: <c>UKOL:</c> +0x14, <c>AKCE:</c> +0x15, <c>CX:</c> +0x18,
    /// <c>CY:</c> +0x19, <c>DALSI_SMER:</c> +0x1A, <c>STRILI_NA:</c> +0x34,
    /// <c>UTOK_NA:</c> +0x36, <c>RX:</c>/<c>RY:</c> +0x00/+0x01. »x, y« ist damit
    /// kein geratener Name mehr, sondern die <b>ZIELZELLE</b> — und die alte
    /// Notiz, 39/0 sei »keine sinnvolle Zelle«, war eine Vermutung über ein
    /// Feld, das das Spiel selbst benennt.</para>
    ///
    /// <para>Geschrieben wird: <c>UKOL = 4</c> (Angriff), <c>CX = cx</c>,
    /// <c>CY = cy</c>, <c>DALSI_SMER = 0xFF</c>, <c>AKCE = 0</c>,
    /// <c>UTOK_NA = utok_na</c>; <c>STRILI_NA</c> wird auf 0xFFFF geräumt, wenn
    /// es über 8000 stand.</para>
    ///
    /// <para>⚠ <b>Ungelesen bleibt UTOK_NA.</b> Kampagne 2 gibt allen sieben
    /// Aufrufen dieselbe Zahl (0xEA60 = 60000), es gibt also kein Gegenbeispiel,
    /// an dem sich eine Deutung prüfen liesse. Die Griffräume sind < 8000
    /// Einheit, 10000..13999 Infanteriezelle, ab 50000 Kulissenobjekt. Der Wert
    /// wird mitgeführt und nicht ausgewertet.</para>
    /// </summary>
    public Action<int, int, int, int>? OrderUnitAt;  // einheit, cx, cy, utok_na
    public Action<int, int, int, int, int>? AddTarget;  // spieler, art, vorrang, wort, c
    public Action<int>? RemoveUnit;                  // einheit
    public Action<int>? SellUnit;                    // einheit
    public Action<int, int>? ChangeOwner;            // einheit, spieler
    public Action<int, int, int>? SetRelation;       // a, b, wert
    public Action<int>? StopTransport;               // einheit

    /// <summary>Minutes since the mission started — the original's clock
    /// (`game_time()` counts 60·(hour + 24·day) + minute), und eine Spielminute
    /// sind <see cref="TicksPerGameMinute"/> Takte, nicht sechzig Sekunden.
    /// </summary>
    public int Minutes => (int)(_ticks / TicksPerGameMinute);

    // ---- reinforcements ----------------------------------------------------

    /// <summary>
    /// One flight of reinforcements on its way in.
    ///
    /// `space_in(player, x, y, &amp;types[], count)` @0x4C17C0 does NOT put units
    /// on the map. It takes one of <b>twenty</b> slots of a queue of 32-byte
    /// records at 0xB49E50 (@0x4C01D0, "More mer_ships needed" when full) and
    /// fills it with x = <b>-10</b> — off the map — y, the target x, the type
    /// bytes and the player. The queue is stepped once per game tick by
    /// @0x4C0260, and only when x has crawled all the way to the target does
    /// kind 3 hand each type to @0x4C1600.
    ///
    /// So a reinforcement ARRIVES LATE, and how late depends on how far into
    /// the map it is going. That is the whole reason mission 14 has a stage
    /// counter between "order Hullman" and "look for Hullman".
    /// </summary>
    private sealed class Incoming
    {
        public int X = -10, Y, Target, Rest, Player;
        public int[] Types = Array.Empty<int>();
    }

    private readonly List<Incoming> _incoming = new();

    /// <summary>The queue @0xB49E50 has twenty slots and the original refuses
    /// the twenty-first out loud.</summary>
    public const int SpaceInSlots = 20;

    /// <summary>How many flights are still on their way — for the harness.</summary>
    public int Incomings => _incoming.Count;

    /// <summary>
    /// One tick of the queue, in the original's own integer arithmetic
    /// (@0x4C04EF..0x4C0580):
    ///
    ///     d = target - x
    ///     d &gt; 10          ->  x++                       (full speed)
    ///     otherwise       ->  rest += max(1, 4*d) as a byte
    ///                         x += rest/40, rest %= 40   (braking)
    ///     x == target     ->  drop, slot freed
    ///
    /// ⚠ The TICK RATE is ours, not the original's — this runs per frame, the
    /// original per game tick, and that has never been measured (handoff
    /// 09.08.). It changes WHEN reinforcements land, not WHETHER.
    /// </summary>
    private void TickIncoming()
    {
        for (int i = _incoming.Count - 1; i >= 0; i--)
        {
            var r = _incoming[i];
            int d = r.Target - r.X;
            if (d > 10) { r.X++; continue; }
            int step = d * 4;
            if (step < 1) step = 1;
            r.Rest = (r.Rest + step) & 0xFF;
            if (r.Rest > 0x27) { r.X += r.Rest / 0x28; r.Rest %= 0x28; }
            if (r.X != r.Target) continue;
            Drop(r);
            _incoming.RemoveAt(i);
        }
    }

    private void Drop(Incoming r)
    {
        foreach (int t in r.Types) SpaceInSpawn?.Invoke(t, r.X, r.Y, r.Player);
        GD.Print($"Verstaerkung eingetroffen: {r.Types.Length} Einheiten " +
                 $"fuer Spieler {r.Player} auf ({r.X}, {r.Y})");
    }

    /// <summary>Land everything still in the air at once. For the harness only:
    /// the flight takes tens of ticks, and a check that asks "is Hullman there"
    /// after one tick would answer no for a reason that has nothing to do with
    /// the condition it is testing.</summary>
    public int FlushIncoming()
    {
        int n = _incoming.Count;
        foreach (var r in _incoming) { r.X = r.Target; Drop(r); }
        _incoming.Clear();
        return n;
    }

    /// <summary>
    /// Soviel Spielzeit, wie <paramref name="dt"/> Sekunden hergeben — in
    /// FESTEN Takten, nicht je Bild.
    ///
    /// <para>⚠ Bis zum 11.08.2026 wertete diese Schleife die ganze Regelliste
    /// einmal je Bild aus. Damit hing das Spiel an der Bildrate: derselbe
    /// Zaehler war auf einem schnellen Rechner frueher voll als auf einem
    /// langsamen. Jetzt zaehlt sie Takte zu 20 ms und laesst den langsamen Teil
    /// des Blocks alle 100 davon laufen — beides gemessen, siehe
    /// <see cref="TicksPerSecond"/> und <see cref="BlockPeriod"/>.</para>
    ///
    /// <para>Der DECKEL ist auch gemessen: das Original haelt hoechstens vier
    /// offene Takte vor (<c>cmp al,4; jbe</c> @0x415EFC, danach
    /// <c>mov byte [0x53920C],1</c>) und laesst alles darueber verfallen. Auf
    /// einem zu langsamen Rechner geht die Spieluhr im Original also NACH; das
    /// tut sie hier genauso. Bei 60 Bildern je Sekunde kommen 0,83 Takte auf
    /// ein Bild, der Deckel greift erst unter etwa 13 Bildern.</para>
    /// </summary>
    public void Tick(double dt)
    {
        if (_ended || dt <= 0) return;
        _rest += dt * TicksPerSecond;
        int n = (int)_rest;
        _rest -= n;
        if (n > MaxPending) { n = MaxPending; _rest = 0; }
        Advance(n);
    }

    /// <summary>Der Vorrat offener Takte im Original: vier (@0x415EFC).</summary>
    public const int MaxPending = 4;

    /// <summary>
    /// Genau <paramref name="n"/> Takte, ohne Deckel — fuer den Pruefstand,
    /// der Spielzeit vergehen lassen will, ohne von der Bildrate abzuhaengen.
    /// </summary>
    public void Advance(int n)
    {
        for (int i = 0; i < n && !_ended; i++)
        {
            // Reihenfolge wie im Original: die Verstaerkungswarteschlange
            // (@0x4C0260, gerufen bei 0x4163E7) laeuft VOR dem Missionsblock
            // (0x497540, gerufen bei 0x416884).
            TickIncoming();
            Step(_ticks % BlockPeriod == 0);
            _ticks++;
            // Die Nachfrist NACH dem Block: das Original prueft sie im
            // Hauptlauf (0x4160FC), also nachdem der Missionsblock gelaufen
            // ist und eine Untermission gerade noch erfuellt haben kann.
            TickGrace();
        }
    }

    /// <summary>
    /// Ein Durchlauf der Regelliste. <paramref name="full"/> heisst: auch der
    /// Teil hinter dem Tor kommt dran.
    /// </summary>
    private void Step(bool full)
    {
        BankVielleicht();
        if (full) BlockRuns++;
        for (int ri = 0; ri < _script.Rules.Count; ri++)
        {
            var r = _script.Rules[ri];
            if (!full && !r.EveryTick) continue;
            // ⚠ nur der Pruefstand: was --pay-check schon von Hand ausgeloest
            // hat, darf nicht ein zweites Mal von selbst feuern. Ohne diese
            // Sperre zahlte Mission 1 im Prüflauf VIER mal 50 statt drei mal:
            // das Erzwingen zaehlt die Kettenvariable 15 auf 3 hoch, und Regel
            // 13 (var15==3 und Spieler 1 ohne Schiffe) griff danach erneut.
            if (_forced != null && _forced.Contains(ri)) continue;
            if (r.Once >= 0 && r.Once < _var.Length && _var[r.Once] != 0) continue;
            bool all = true;
            foreach (var c in r.When)
                if (!Test(c)) { all = false; break; }
            if (!all) continue;
            // Und das ODER: mindestens ein Glied aus `any`. Leer heisst »kein
            // ODER«, NICHT »immer wahr« — siehe Rule.Any.
            if (HasOr(r) && !AnyTrue(r)) continue;

            _ruleNo = ri;
            if (Fired.Count < FiredMax)
            {
                var what = new List<string>();
                foreach (var a in r.Then) what.Add(a.Kind);
                Fired.Add((_ticks, ri, string.Join("+", what)));
            }
            foreach (var a in r.Then) Do(a);
            if (r.Once >= 0 && r.Once < _var.Length) _var[r.Once]++;
            RulesFired++;
            if (_ended) return;
        }
    }

    /// <summary>Fuer den Pruefstand: welche Regel bei welchem Takt gefeuert hat
    /// und womit. Gedeckelt, damit ein Zaehler, der jede Sekunde feuert, den
    /// Speicher nicht vollschreibt — die ersten <see cref="FiredMax"/> reichen,
    /// denn gesucht wird, was ZU FRUEH kommt.</summary>
    public readonly List<(long Tick, int Rule, string What)> Fired = new();

    private const int FiredMax = 64;

    /// <summary>
    /// Ein voller Durchlauf der Regelliste, OHNE die Uhr zu bewegen — fuer den
    /// Pruefstand.
    ///
    /// <para>⚠ Das war vorher <c>Tick(0.0)</c>, und mit festem Takt tut das
    /// nichts mehr: null Sekunden sind null Takte. Ein Prueflauf, der so fragt
    /// »gewinnt das Skript sofort?«, bekaeme immer nein — und zwar aus einem
    /// Grund, der mit der gepruefen Frage nichts zu tun hat.</para>
    /// </summary>
    public void Evaluate()
    {
        if (_ended) return;
        Step(true);
    }

    /// <summary>Welche Regel gerade laeuft — nur fuer die Protokollzeile beim
    /// Klang, damit ein gemeldetes Geraeusch eine Adresse bekommt.</summary>
    private int _ruleNo = -1;

    private bool Cmp(int lhs, string op, int rhs) => op switch
    {
        "==" => lhs == rhs,
        "!=" => lhs != rhs,
        ">" => lhs > rhs,
        ">=" => lhs >= rhs,
        "<" => lhs < rhs,
        "<=" => lhs <= rhs,
        _ => false,
    };

    /// <summary>NUR fuer den Pruefstand: eine vorgegebene Antwort auf ein
    /// einzelnes Glied. Bleibt null im Spiel und kostet dort einen Nullvergleich.
    /// Gebraucht wird es, um das ODER an seiner Mechanik zu pruefen und nicht an
    /// seinem Ergebnis (Regel 8): das Gatter wird durch seine ganze
    /// Wahrheitstafel gefahren, ohne die Welt anzufassen.</summary>
    private Dictionary<Cond, bool>? _vorgabe;

    private bool Test(Cond c)
    {
        if (_vorgabe != null && _vorgabe.TryGetValue(c, out bool vor)) return vor;
        return TestReal(c);
    }

    private bool TestReal(Cond c) => c.Kind switch
    {
        // v[a] <op> b
        "var" => c.A >= 0 && c.A < _var.Length && Cmp(_var[c.A], c.Op, c.B),
        // game_time() <op> a
        "time_gt" => Cmp(Minutes, c.Op == "==" ? ">" : c.Op, c.A),
        // game_time() > v[a] + b
        "time_after" => c.A >= 0 && c.A < _var.Length && Minutes > _var[c.A] + c.B,
        // DAS TOR DES BLOCKS: Takt mod 100 == 0.
        // ⚠ Das steht hier NICHT, weil die Engine es sonst nicht wüsste — sie
        // ordnet die Regeln längst über <see cref="Rule.EveryTick"/> ein
        // (At < Gate). Es steht hier, weil der Regelleser das Tor jetzt LIEST
        // und als Glied einträgt; eine Bedingung ohne Haken wäre FALSCH und
        // die Regel damit tot. Widersprechen können sich beide nie: es ist
        // dieselbe Rechnung, und alle 17 so gelesenen Regeln liegen
        // nachweislich hinter ihrem Tor.
        "block_gate" => _ticks % BlockPeriod == 0,
        // AUSGESCHALTET / VERLUSTE des Spielers a
        // Feld +b des Gebäudesatzes a <op> c
        "store" => StoreField != null && StoreField(c.A, c.B) >= 0 &&
                   Cmp(StoreField(c.A, c.B), c.Op, c.C),
        // Einnahme-FORTSCHRITT gegen BEZUGSENERGIE / b. Das Original hält den
        // Fortschritt proportional zur Energie nach (@0x43CB70) und zeichnet
        // danach abwechselnd den alten und den neuen Besitzer (@0x43CD27).
        "capture_frac" => StoreField != null && c.B > 0 &&
                          StoreField(c.A, 0x3A) >= 0 && StoreField(c.A, 0x38) > 0 &&
                          Cmp(StoreField(c.A, 0x3A), c.Op, StoreField(c.A, 0x38) / c.B),
        // ENERGIE(a) <op> ENERGIE VOLL(a) / b — dieselbe Frage wie
        // `store_energy`, nur fuer eine EINHEIT. M7 @0x49AC5A liest
        // +0x08 gegen +0x29 und laesst weiter, wenn voll groesser ist:
        // »die Einheit ist beschaedigt«.
        "unit_energy" => UnitField != null && c.B > 0 &&
                         UnitField(c.A, 0x08) >= 0 && UnitField(c.A, 0x29) > 0 &&
                         Cmp(UnitField(c.A, 0x08), c.Op, UnitField(c.A, 0x29) / c.B),
        // v[a] + c <op> AUSGESCHALTET(b)   (M24 @0x4A1625)
        "var_vs_kills" => KillCount != null && c.A >= 0 && c.A < _var.Length &&
                          Cmp(_var[c.A] + c.C, c.Op, KillCount(c.B)),
        // Lebt der Einheitensatz a? (+0x09 != 0xFF im Original)
        "unit_alive" => UnitAlive != null && Cmp(UnitAlive(c.A) ? 1 : 255, c.Op, 255),
        // Brückenplatz a belegt?
        "bridge" => BridgeUsed != null && Cmp(BridgeUsed(c.A), c.Op, c.B),
        "kills" => KillCount != null && Cmp(KillCount(c.A), c.Op, c.B),
        "losses" => LossCount != null && Cmp(LossCount(c.A), c.Op, c.B),
        // AUSGESCHALTET(a) <op> VERLUSTE(b) + c — das Original vergleicht die
        // zwei Zaehler gegeneinander, teils mit einem Vorsprung: M19 und M22
        // verlangen »hoechstens 30 Verluste mehr als Abschuesse«
        // (@0x49EB45 / @0x4A0A3B, `add eax, 0x1e`), M21 den blossen Vergleich.
        "kills_vs_losses" => KillCount != null && LossCount != null &&
                             Cmp(KillCount(c.A), c.Op, LossCount(c.B) + c.C),
        // ENERGIE(a) <op> ENERGIE VOLL(a) / b — »das Gebaeude ist beschaedigt«
        // (b = 1) bzw. »unter einem Fuenftel« (M19 @0x49EDF2, b = 5).
        // ⚠ b == 0 kaeme aus einem Lesefehler; dann lieber FALSCH als eine
        // Division durch null.
        "store_energy" => StoreField != null && c.B > 0 &&
                          StoreField(c.A, 0x02) >= 0 && StoreField(c.A, 0x12) >= 0 &&
                          Cmp(StoreField(c.A, 0x02), c.Op, StoreField(c.A, 0x12) / c.B),
        // v[a] + c <op> VERLUSTE(b)   (M24 @0x4A1611, M30 @0x4A4237)
        "var_vs_losses" => LossCount != null && c.A >= 0 && c.A < _var.Length &&
                           Cmp(_var[c.A] + c.C, c.Op, LossCount(c.B)),
        // obj_owner(a) <op> b
        "obj_owner" => ObjOwner != null && Cmp(ObjOwner(c.A), c.Op, c.B),
        // g_robot_class_count(a, b) <op> c
        "units" => UnitCount != null && Cmp(UnitCount(c.A, c.B), c.Op, c.C),
        // g_buildings_count(a, b) <op> c
        "buildings" => BuildingCount != null && Cmp(BuildingCount(c.A, c.B), c.Op, c.C),
        // count_objects(a, b) <op> c
        "objects" => ObjectCount != null && Cmp(ObjectCount(c.A, c.B), c.Op, c.C),
        // find_unit(a, b) <op> c   — c = 65535 heisst "es gibt sie nicht"
        "unit_index" => FindUnit != null && Cmp(FindUnit(c.A, c.B), c.Op, c.C),
        // Feld +b der Einheit, deren Index in v[a] steht, <op> c.
        // ⚠ Ein unbekanntes Feld (-1) macht die Bedingung FALSCH, nicht wahr:
        // eine Kette, die nicht beantwortet werden kann, darf nicht gewinnen.
        // v[a] <op> Feld +c des Gebaeudesatzes b
        "var_vs_store" => StoreField != null && c.A >= 0 && c.A < _var.Length &&
                          StoreField(c.B, c.C) >= 0 &&
                          Cmp(_var[c.A], c.Op, StoreField(c.B, c.C)),
        "unit_field" => UnitField != null && c.A >= 0 && c.A < _var.Length &&
                        UnitField(_var[c.A], c.B) >= 0 &&
                        Cmp(UnitField(_var[c.A], c.B), c.Op, c.C),

        // ---- 11.08.2026: die Glieder des tutorialartigen Ablaufs -----------
        //
        // ⚠ Alle fünf antworten mit FALSCH, wenn ihr Haken nicht hängt. Eine
        // Bedingung, die nicht beantwortet werden kann, darf nicht gewinnen —
        // dieselbe Regel wie oben bei `unit_field`.

        // Was ist angewählt? 0x1F40 (8000) ist die Zahl der Einheitenplätze,
        // »kleiner« heisst also »eine Einheit ist angewählt«; ab 0x2710
        // (10000) ist es eine GRUPPE. Mission 1 hängt daran ihre Fenster
        // #002..#004 und #011.
        "selected" => Selection != null && Cmp(Selection(), c.Op, c.B),
        // Feld +c des Einheitensatzes a. Mission 1 fragt so nach der ZEILE
        // (+0x01) ihres Startpanzers (Satz 0): < 30 heisst »auf dem Weg zur
        // Brücke«, < 20 »am Hafen«.
        "unit_pos" => UnitField != null && UnitField(c.A, c.C) >= 0 &&
                      Cmp(UnitField(c.A, c.C), c.Op, c.B),
        // Die Art des letzten Ereignisses. Der Block, der es liest, verbraucht
        // es — genau das tut `Do` unten auch.
        "event" => Cmp(LastEvent, c.Op, c.B),
        // count_units_with_mark(a, b): wieviele Einheiten des Spielers b tragen
        // die Entwurfsnummer a? Mission 1 zählt so die EINGENOMMENEN neutralen.
        "units_mark" => MarkCount != null && Cmp(MarkCount(c.A, c.B), c.Op, c.C),
        // unit_has_mark(a, b) -> 1/0
        // unit_has_mark(v[a], b) — DER INDEX KOMMT AUS EINER VARIABLEN.
        //
        // ⚠ 13.08.2026. Das Original uebergibt den Einheitenindex fast immer so:
        //     mov ax, word ptr [v[11]]  /  push 0xc1  /  push eax  /  call
        // Der Regelleser hielt `push eax` fuer ein unbekanntes Argument und warf
        // das ganze Glied weg — gezaehlt 31 Stellen in 10 Missionen, und daran
        // hingen 29 Regeln. `unit_is` mit einer festen Zahl kann das nicht: es
        // braucht die INDIREKTION.
        //
        // v[a] fuellt `find_unit`, und 0xFFFF heisst dort »gibt es nicht«.
        // UnitHasMark antwortet darauf FALSCH — eine Bedingung, die nicht
        // beantwortet werden kann, darf nicht gewinnen.
        "unit_is_var" => UnitHasMark != null && c.A >= 0 && c.A < _var.Length &&
                         Cmp(UnitHasMark(_var[c.A], c.B) ? 1 : 0, c.Op, c.C),
        "unit_is" => UnitHasMark != null &&
                     Cmp(UnitHasMark(c.A, c.B) ? 1 : 0, c.Op, c.C),
        // get_money(a)
        "money_of" => MoneyOf != null && Cmp(MoneyOf(c.A), c.Op, c.B),
        // Haelt Spieler b die Bahnverbindung a? Siehe RailLinkHeld — Mission 21
        // fragt neun davon, und ALLE muessen halten.
        "rail_link" => RailLinkHeld != null && RailLinkHeld(c.A, c.B),
        // terrain_at(a, b) — Mission 1 prüft damit, ob der Panzer auf der
        // Brücke steht (> 4)
        "terrain" => TerrainAt != null && Cmp(TerrainAt(c.A, c.B), c.Op, c.C),
        // ---- 18.08.2026: die vier letzten Glieder des Tutorials ------------

        // Takte seit Missionsbeginn (0x4FA240). ⚠ NICHT die Spieluhr: die zählt
        // Spielminuten zu je 250 Takten (`time_gt`). Mission 1 hält ihr
        // Willkommensfenster #001 damit zehn Takte zurück — 0,2 s bei 50 Hz.
        "ticks" => Cmp((int)Math.Min(_ticks, int.MaxValue), c.Op, c.B),
        // window_open(a) — siehe TextOpen. Kein Haken heisst FALSCH … und das
        // wäre hier die falsche Antwort: »kein Fenster offen« ist 0, und die
        // Regeln fragen auf `== 0`. Darum antwortet der Nullfall mit »nicht
        // offen«, nicht mit »Bedingung unerfüllt«.
        "text_open" => Cmp((TextOpen != null && TextOpen(c.A)) ? 1 : 0, c.Op, c.B),
        // terrain_at unter der EIGENEN EINHEIT a (Satz a, Felder +0x00 Spalte
        // und +0x01 Zeile). Mission 1 zeigt daran #020, »Ist eine Einheit auf
        // einem @Hügel …« — die Bedingung ist wörtlich die des Textes.
        "terrain_unit" => TerrainAt != null && UnitField != null &&
                          UnitField(c.A, 0) >= 0 && UnitField(c.A, 1) >= 0 &&
                          Cmp(TerrainAt(UnitField(c.A, 0), UnitField(c.A, 1)),
                              c.Op, c.B),
        // Feld +b der ANGEWÄHLTEN Einheit. +0x14 heisst im Spiel selbst UKOL
        // (Debug-Auszug @0x416F00); Mission 1 fragt damit auf UKOL == 1 und
        // zeigt #009, den Text über die @Handsteuerung.
        "sel_field" => Selection != null && UnitField != null &&
                       Selection() >= 0 && Selection() < 8000 &&
                       UnitField(Selection(), c.B) >= 0 &&
                       Cmp(UnitField(Selection(), c.B), c.Op, c.C),

        // Eine Zelle der BELEGUNGSKARTE (Spalte a, Zeile c). Das Original hält
        // dort je Zelle ein Wort: den Einheitenplatz, ab 8000 ein Gebäude, und
        // 0xFFFE für »frei«. Mission 1 startet daran ihren Zähler für die vier
        // Angreifer vor der Brücke — `imap(39,4) == 0xFFFE`.
        "imap" => ImapAt != null && Cmp(ImapAt(c.A, c.C), c.Op, c.B),
        _ => false,
    };

    /// <summary>Die Art des letzten Ereignisses (0x539930). Wird gesetzt, wo
    /// das Ereignis entsteht, und von der Regel, die es liest, wieder auf 0
    /// gestellt — das Original tut genau dasselbe (`mov byte [0x539930], 0`
    /// direkt hinter dem Fenster, das es auslöst).</summary>
    public int LastEvent;

    /// <summary>
    /// PRÜFSTAND (`--pay-check`): jede Regel, deren Wirkung eine Geldbuchung
    /// enthaelt, einmal ausloesen — ohne ihre Bedingungen zu pruefen.
    ///
    /// <para>Gebraucht wird das fuer die Frage »reicht das Geld fuer die
    /// naechste Mission, wenn ich die Nebenmission nicht mache?«. Der Fall OHNE
    /// laesst sich einfach fahren (nichts tun), der Fall MIT nicht: in
    /// Mission 1 haengen die drei Buchungen an einer Kette aus Zaehlvariable 15
    /// und der Zahl der noch schwimmenden Schiffe von Spieler 1, die im
    /// Prüfstand nicht von selbst zustande kommt.</para>
    ///
    /// <para>⚠ Das erfindet keinen Betrag. Es fuehrt die WIRKUNGEN aus, die in
    /// Data/mission_scripts.json stehen, durch denselben <see cref="Do"/>-Weg
    /// wie im Spiel — nur der Anlass wird vorgegeben. Die Regeln laufen in
    /// Dateireihenfolge, damit eine Kette wie 11/12/13 (die sich ueber
    /// Variable 15 selbst weiterschaltet) in ihrer Reihenfolge durchlaeuft.
    /// </para>
    /// </summary>
    /// <returns>Was ausgeloest wurde, als Zeile fuer das Protokoll.</returns>
    public string ForceMoneyRules()
    {
        int n = 0, summe = 0;
        _forced ??= new HashSet<int>();
        for (int ri = 0; ri < _script.Rules.Count; ri++)
        {
            var r = _script.Rules[ri];
            bool zahlt = false;
            foreach (var a in r.Then) if (a.Kind == "money") { zahlt = true; break; }
            if (!zahlt) continue;
            foreach (var a in r.Then)
            {
                if (a.Kind == "money") summe += a.A;
                Do(a);
            }
            _forced.Add(ri);
            n++;
        }
        // ⚠ 13.08.2026 — der VORGEFUNDENE Spielstand gehoert in die Zeile. Ohne
        // ihn misst diese Zahl die Erloese der vorigen Prueflaeufe mit: gemessen
        // wurden $47465 Startkontostand in Mission 23 und $970 statt $470 in
        // Mission 5. Wer eine reine Zahl will, nimmt --fresh-campaign.
        int stand = CampaignManager.Balance, fertig = CampaignManager.Completed;
        return $"pay-check: {n} Geldregeln von Mission {Mission} ausgeloest, " +
               $"Summe {(summe >= 0 ? "+" : "")}{summe} $" +
               (stand != 0 || fertig != 0
                   ? $"   ⚠ VORGEFUNDENER SPIELSTAND: {fertig} Missionen geschafft, " +
                     $"${stand} mitgebracht ({CampaignManager.SavePath}) — " +
                     "fuer eine reine Messung --fresh-campaign nehmen"
                   : "   (Spielstand leer)");
    }

    /// <summary>Die von <see cref="ForceMoneyRules"/> erzwungenen Regeln. Bleibt
    /// null, solange niemand den Pruefstand benutzt — im Spiel kostet das
    /// nichts.</summary>
    private HashSet<int>? _forced;

    private void Do(Act a)
    {
        switch (a.Kind)
        {
            case "inc":
                if (a.A >= 0 && a.A < _var.Length) _var[a.A]++;
                break;
            case "set":
                if (a.A >= 0 && a.A < _var.Length) _var[a.A] = a.B;
                break;
            case "text":
                ShowText?.Invoke(a.A, a.B, a.C, a.D);
                // Der Block, der ein Ereignis in ein Fenster verwandelt, nullt
                // es danach (`mov byte [0x539930], 0` @0x49867D). Ohne das
                // feuerte dieselbe Regel in jeder Runde neu.
                LastEvent = 0;
                break;

            // ---- 11.08.2026 ------------------------------------------------
            // geld(b) += a. Der Betrag ist ein WORT und darf negativ sein.
            // Mission 1 zahlt so dreimal 50 $ für die versenkten Schiffe.
            case "money":
                AddMoney?.Invoke(a.A, a.B);
                break;
            case "sound":
                // Welche Regel spricht, gehoert ins Protokoll: der Spieler hoerte
                // am 11.08.2026 beim START von Kampagne 2 »Nebenmission
                // beendet«, und ohne diese Zeile ist nicht zu sehen, welche der
                // 21 Regeln das war.
                // Mit dem TAKT dazu: der Spieler hoerte am 11.08.2026 beim
                // START von Kampagne 2 »Nebenmission beendet«, und ohne die
                // Zahl ist »zu frueh« nicht von »richtig« zu unterscheiden.
                GD.Print($"mission: Klang {a.A} aus Regel {_ruleNo} " +
                         $"bei Takt {_ticks} ({Seconds:0.00} s)");
                PlaySound?.Invoke(a.A);
                break;
            case "close_texts":
                CloseTexts?.Invoke();
                break;
            // bus_cmd(11, einheit, ukol, x, y) — der Befehl, mit dem eine
            // Mission ihre eigenen Einheiten losschickt. ukol 4 ist Angriff.
            // In Mission 1 sind das die Plätze 1000..1003, also die ersten vier
            // Einheiten von Spieler 1: drei Infanteristen und ein MG-Fahrzeug.
            case "order":
                OrderUnit?.Invoke(a.A, a.B, a.C, a.D);
                break;
            // BEWEGEN (Befehl 3). Dieselbe Wirkung wie `order_at`, nur ohne
            // dessen viertes, ungelesenes Argument.
            case "move":
                MoveUnit?.Invoke(a.A, a.B, a.C);
                break;
            case "set_ai":
                SetAi?.Invoke(a.A, a.B);
                break;
            case "fire_at":
                FireAt?.Invoke(a.A, a.B, a.C);
                break;
            // place_unit(entwurf, spalte, zeile, spieler) @0x4D0810 — siehe
            // PlaceUnit. Gezaehlt wird, WIEVIELE eine Mission auslöst: die
            // gelesene Zahl je Mission ist die Messlatte (M2 7, M3 4, M5 2,
            // M11 1, M14 3, M18 3 — 20 von 60 Aufrufstellen sind heute in
            // Regeln eintragbar).
            case "place_unit":
                Placements++;
                // ⚠ Der Rueckgabewert wird gehalten, auch wenn ihn heute noch
                // keine Regel abholt: sobald `v[a] = place_unit(...)` im
                // Vokabular steht (M28), ist er genau das, was dort hinein muss.
                LastPlaced = PlaceUnit?.Invoke(a.A, a.B, a.C, a.D) ?? -1;
                // ⚠ `v[e] = place_unit(...)` — das Original merkt sich den
                // zurueckgegebenen Satzindex, und Mission 28 spricht die Einheit
                // spaeter darueber an. `e` ist −1, wo das Skript den Rueckgabe-
                // wert wegwirft (das ist der Normalfall).
                if (a.V >= 0 && a.V < _var.Length) _var[a.V] = LastPlaced;
                break;
            // order(einheit, cx, cy, utok_na, extra) @0x410220 — siehe
            // OrderUnitAt. e (extra) wird NICHT weitergegeben: das Original
            // benutzt es nur, wenn utok_na in [30000, 30256) liegt, und
            // Kampagne 2 gibt dort 60000.
            case "order_at":
                OrdersGiven++;
                OrderUnitAt?.Invoke(a.A, a.B, a.C, a.D);
                break;
            // add_target(spieler, art, vorrang, wort, c) @0x4CF700 — ⚠ das ist
            // KEIN Missionsziel im Panel, sondern die ZIELLISTE DES
            // COMPUTERSPIELERS. Alle Leser der Tabelle 0xBC5A78 liegen im
            // KI-Bereich (0x4BCF30, 0x4BDCC0, 0x4BECF0), keiner in der
            // Oberflaeche; der alte Name kam aus dem String »Cannot add new
            // target« und war geraten. Siehe CAMPAIGN_RE.md.
            case "add_target":
                AddTarget?.Invoke(a.A, a.B, a.C, a.D, a.E);
                break;
            case "remove_unit":
                RemoveUnit?.Invoke(a.A);
                break;
            case "sell_unit":
                SellUnit?.Invoke(a.A);
                break;
            case "change_owner":
                ChangeOwner?.Invoke(a.A, a.B);
                break;
            case "set_relation":
                SetRelation?.Invoke(a.A, a.B, a.C);
                break;
            case "stop_transport":
                StopTransport?.Invoke(a.A);
                break;
            // v[a] = find_unit(b, c) — die Mission merkt sich ihre Einheit
            case "find_unit":
                if (a.A >= 0 && a.A < _var.Length)
                    _var[a.A] = FindUnit != null ? FindUnit(a.B, a.C) : 0xFFFF;
                break;
            // v[a] = Feld c des Gebaeudesatzes b — die Marke, gegen die spaeter
            // auf Wachstum geprueft wird
            case "set_store":
                if (a.A >= 0 && a.A < _var.Length && StoreField != null)
                    _var[a.A] = StoreField(a.B, a.C);
                break;
            // Lager b des Gebaeudesatzes a = e·v[c] + d — siehe SetStoreField.
            // c == -1 heisst »keine Variable«, dann ist d der ganze Betrag.
            // e ist der Faktor: 1 bei M2/M3/M6, +3 und −3 bei M25
            // (`3*v[88]+80` und `194−3*v[88]`, @0x4A1B50).
            case "stock":
                if (SetStoreField != null && !StockOld)
                    SetStoreField(a.A, a.B, StockWert(a));
                break;
            // space_in(a=spieler, b=x, c=y, typen) — die Verstaerkung startet
            // ausserhalb der Karte und braucht ihre Anflugzeit, wie im Original
            case "space_in":
                if (a.Types.Length == 0) break;
                if (_incoming.Count >= SpaceInSlots)
                {
                    GD.PrintErr("space_in: alle 20 Plaetze belegt " +
                                "(»More mer_ships needed«) — Verstaerkung faellt aus");
                    break;
                }
                _incoming.Add(new Incoming
                {
                    Y = a.C, Target = a.B, Player = a.A, Types = a.Types,
                });
                GD.Print($"Verstaerkung angefordert: {a.Types.Length} Einheiten " +
                         $"fuer Spieler {a.A} nach ({a.B}, {a.C})");
                break;
            // v[a] = g_robot_class_count(b, c) — die Mission MERKT sich einen
            // Bestand, statt ihn nur zu vergleichen. Mission 15 tut das im
            // ersten Takt mit `units(1, 7)`: den Bodenfahrzeugen des neutralen
            // Spielers, gegen die sie später zählt.
            // v[a] = welche Einheit auf (b, c) steht — die Belegungskarte als
            // GRIFF auf eine Einheit. Mission 28 liest so vier Zellen ab
            // (@0x4A2F06 ff., je eine entrollte Fassung derselben Schleife) und
            // spricht die gefundene Einheit danach ueber v[41] an.
            case "set_imap":
                if (a.A >= 0 && a.A < _var.Length)
                    _var[a.A] = ImapAt != null ? ImapAt(a.B, a.C) : 0xFFFE;
                break;
            case "set_units":
                if (a.A >= 0 && a.A < _var.Length)
                    _var[a.A] = UnitCount != null ? UnitCount(a.B, a.C) : 0;
                break;
            // v[a] += v[b]; v[b] = 0 — die VERBRAUCHSKORREKTUR.
            //
            // Mission 5 gewinnt, wenn zwei Teilelager über ihre Marke wachsen.
            // Damit eine GUTSCHRIFT dabei nicht als Produktion durchgeht, hebt
            // sie die Marke um genau den gutgeschriebenen Betrag: v[190..192]
            // halten die Waffen-, Fahrwerk- und Spezialteile, die @0x4B28E0
            // gerade einem Gebäude zurückgeschrieben hat — dieselbe Routine
            // erhöht dort +0x28/+0x2a/+0x2c um genau diese drei Zahlen. Auf
            // beiden GAME.EXE dieselben sechs Schreibstellen, Abstand 0xFA0.
            //
            // ⚠ Die Engine verrechnet noch keine Einheiten gegen Teile, also
            // bleiben v[190..192] hier auf 0 und die Regel feuert nie. Das ist
            // eine Lücke, keine Falle: ohne Gutschrift gibt es nichts zu
            // korrigieren.
            case "take_var":
                if (a.A >= 0 && a.A < _var.Length && a.B >= 0 && a.B < _var.Length)
                {
                    _var[a.A] += _var[a.B];
                    _var[a.B] = 0;
                }
                break;
            // v[a] = game_time() — der Zeitstempel, auf den `time_after` zeigt
            case "set_time":
                if (a.A >= 0 && a.A < _var.Length) _var[a.A] = Minutes;
                break;
            case "end":
                // ⚠ EIN SIEG ENDET NICHT SOFORT, wenn noch eine Untermission
                // offen ist — siehe TickGrace. Bis zum 18.08.2026 tat er das,
                // und damit fehlte genau das Fenster, das der Spieler
                // photographiert hat.
                if (a.A != 0 && Grace < 0 && OpenObjectives() > 0)
                {
                    Grace = GraceSteps;
                    _graceMinute = Minutes;
                    GD.Print($"Missionsskript {_script.Mission}: Ziel erreicht, " +
                             $"aber {OpenObjectives()} Untermissionen offen — " +
                             $"Nachfrist {Grace}");
                    break;
                }
                _ended = true;
                Success = a.A != 0;
                Grace = -1;
                GD.Print($"Missionsskript {_script.Mission}: " +
                         (Success ? "Mission erfolgreich beendet" : "Mission gescheitert") +
                         $" nach {Minutes} Minuten, {RulesFired + 1} Regeln");
                break;
        }
    }

    /// <summary>Every building slot the script watches, so the harness can show
    /// what those look like right now — the quickest way to see whether a
    /// translated condition is even asking about the right things.</summary>
    public List<int> WatchedSlots()
    {
        var list = new List<int>();
        foreach (var r in _script.Rules)
            foreach (var c in r.When)
                if (c.Kind == "obj_owner" && !list.Contains(c.A)) list.Add(c.A);
        list.Sort();
        return list;
    }

    /// <summary>Every building record the script reads a STORE out of, as
    /// (slot, offset). Mission 5 is the whole reason this exists: it marks the
    /// two part stores of building 0 and wins when both have GROWN, so the only
    /// way to tell "the engine cannot do it" from "the engine did not do it
    /// yet" is to watch those two numbers while the mission runs.</summary>
    public List<(int Slot, int Off)> WatchedStores()
    {
        var list = new List<(int, int)>();
        void Add(int slot, int off)
        {
            if (!list.Contains((slot, off))) list.Add((slot, off));
        }
        foreach (var r in _script.Rules)
        {
            foreach (var c in r.When) if (c.Kind == "var_vs_store") Add(c.B, c.C);
            foreach (var a in r.Then) if (a.Kind == "set_store") Add(a.B, a.C);
        }
        return list;
    }

    /// <summary>What the script currently holds in the variables it filled from
    /// a store — the other half of the comparison <see cref="WatchedStores"/>
    /// shows.</summary>
    public List<(int Var, int Slot, int Off, int Value)> StoreMarks()
    {
        var list = new List<(int, int, int, int)>();
        foreach (var r in _script.Rules)
            foreach (var a in r.Then)
                if (a.Kind == "set_store" && a.A >= 0 && a.A < _var.Length)
                    list.Add((a.A, a.B, a.C, _var[a.A]));
        return list;
    }

    /// <summary>Every condition the script's end rules ask about, so the
    /// harness can try to make them true and check the whole chain. The
    /// operator comes along: since the conditions are read out of the EXE with
    /// their real comparison, "gone" is no longer the only thing an end rule
    /// can want — mission 18 stops the advance at FEWER THAN THREE units left,
    /// and mission 3 wants a research complex that is still THERE.</summary>
    public List<Cond> EndConds()
    {
        var list = new List<Cond>();
        foreach (var r in _script.Rules)
        {
            if (!Ends(r)) continue;
            foreach (var c in r.When) list.Add(c);
            // ⚠ Die ODER-Glieder kommen MIT. Keine der 31 Endregeln hat heute
            // welche, aber ein Prüfstand, der sie stillschweigend fallen liesse,
            // hätte wieder eine zu schwache Bedingung vor sich.
            foreach (var c in r.Any) list.Add(c);
            foreach (var g in r.AnyGroups) foreach (var c in g) list.Add(c);
        }
        return list;
    }

    /// <summary>
    /// Everything the harness must arrange for an end rule to fire — the end
    /// conditions with their variable links RESOLVED.
    ///
    /// Since the setter rules are carried too, an end condition over a block
    /// variable is no longer a dead end: v[22] in mission 13 is raised by
    /// `objects(7, 0) != 0` ("build power generators"), so what the harness has
    /// to arrange is that, not the variable. Walking only `EndConds()` left the
    /// chain untested — the run reported "not forcible: var(22)!=0" and nothing
    /// ever fired.
    ///
    /// Variables whose writers are themselves conditioned on variables are
    /// followed too; each one only once, so a counter that raises itself
    /// (`v[1] < 40 -> inc v[1]`) cannot loop. Those resolve by time, not by the
    /// harness, and simply drop out.
    /// </summary>
    public List<Cond> ChainConds()
    {
        var list = new List<Cond>();
        var seen = new HashSet<int>();
        var queue = new Queue<Cond>();
        foreach (var c in EndConds()) queue.Enqueue(c);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            // `unit_field` fragt die Welt, hängt aber an der Variablen, in der
            // der Einheitenindex steht — also beides: die Bedingung sammeln UND
            // dem Erzeuger dieser Variablen nachgehen.
            if (c.Kind is "unit_field" or "var_vs_store" or "unit_is_var")
                queue.Enqueue(new Cond { Kind = "var", A = c.A, Op = "!=", B = 0 });
            if (c.Kind != "var") { list.Add(c); continue; }
            if (!seen.Add(c.A)) continue;
            foreach (var r in _script.Rules)
            {
                // ⚠ 13.08.2026 — hier stand eine ZWEITE, kürzere Liste der
                // schreibenden Wirkungen als in <see cref="SetsVar"/>: `take_var`
                // und `set_units` fehlten. Mission 5 hängt v[31]/v[32] an
                // `take_var`, dessen Erzeuger damit unsichtbar war. Eine Liste,
                // zwei Stellen — jetzt nur noch eine.
                if (!SetsVar(r, c.A)) continue;
                foreach (var w in r.When) queue.Enqueue(w);
                // ⚠ Beim ODER wird ALLES eingereiht. Jedes einzelne Glied wahr
                // zu machen kann das ODER nur wahr machen; und was der Prüfstand
                // nicht stellen kann, meldet er — eine Zeile zuviel ist besser
                // als ein Glied, das keiner nennt (Regel 10).
                foreach (var w in r.Any) queue.Enqueue(w);
            }
        }
        return list;
    }

    /// <summary>How many matching things the harness has to leave standing to
    /// make this condition true, or -1 when it cannot be arranged at all.
    ///
    /// Destroying is not the only lever, and treating it as the only one is how
    /// three inverted conditions stayed hidden: mission 3 wants a research
    /// complex that is still THERE, mission 23 exactly five raw-material mines,
    /// mission 2 exactly two objects. So the harness aims at a TARGET COUNT and
    /// destroys or hands over until it is met.</summary>
    public static int TargetCount(Cond c) => c.Op switch
    {
        "==" => c.C,
        "!=" => c.C == 0 ? 1 : -1,          // "nicht null" -> eines genuegt
        "<" => Math.Max(0, c.C - 1),
        "<=" => Math.Max(0, c.C),
        ">" => c.C + 1,
        ">=" => c.C,
        _ => -1,
    };

    /// <summary>
    /// PRÜFSTAND: warum die Mission nicht endet — die GANZE KETTE, Glied für
    /// Glied, mit dem Wert, den es gerade hat.
    ///
    /// <para>⚠ 13.08.2026 erweitert, und der Anlass ist Regel 10: »wenn er
    /// schweigt, muss er sagen, WELCHES GLIED falsch ist«. Bis heute standen
    /// hier nur die Bedingungen der ENDregel selbst. Für Mission 5 hiess das
    /// genau zwei Zeilen — <c>var(33)!=0=NEIN var(34)!=0=NEIN</c> —, und damit
    /// war eine Kette, die nicht schliesst, nicht von einer zu unterscheiden,
    /// die es fast tut: dass v[33] an <c>buildings(1,0)&gt;0</c> hängt und der
    /// Prüfstand dem Spieler eine Baracke statt einer FABRIK übergeben hatte,
    /// stand nirgends.</para>
    ///
    /// <para>Jetzt wird von jeder Endbedingung aus rückwärts gegangen: zu jeder
    /// falschen Variablenbedingung werden die Regeln genannt, die diese Variable
    /// schreiben, mit ihren eigenen Gliedern. Eine Variable, die keine Regel
    /// schreibt, wird ausdrücklich so gemeldet — das ist die Lücke, keine
    /// Fehlmessung.</para>
    /// </summary>
    public string WhyNot()
    {
        var lines = new List<string>();
        var todo = new Queue<int>();
        var seen = new HashSet<int>();

        foreach (var r in _script.Rules)
        {
            if (!Ends(r)) continue;
            lines.Add("Endregel: " + Links(r, todo));
        }
        while (todo.Count > 0)
        {
            int n = todo.Dequeue();
            if (!seen.Add(n)) continue;
            bool wer = false;
            for (int ri = 0; ri < _script.Rules.Count; ri++)
            {
                var r = _script.Rules[ri];
                if (!SetsVar(r, n)) continue;
                wer = true;
                string riegel = r.Once >= 0 && Var(r.Once) != 0
                    ? $" [Riegel v{r.Once}={Var(r.Once)} ZU]" : "";
                lines.Add($"   v[{n}]={Var(n)} <- Regel {ri} @0x{r.At:X}{riegel}: " +
                          Links(r, todo));
            }
            if (!wer) lines.Add($"   v[{n}]={Var(n)} <- KEINE Regel schreibt sie");
        }
        return string.Join("\n", lines);
    }

    /// <summary>Die Glieder einer Regel als Text, mit ihrem Wahrheitswert; jede
    /// FALSCHE Variablenbedingung wird für die Rückwärtssuche eingereiht.
    /// </summary>
    private string Links(Rule r, Queue<int> todo)
    {
        var parts = new List<string>();
        void Take(Cond c, string vor)
        {
            bool ok = Test(c);
            parts.Add(vor + Show(c) + "=" + (ok ? "ja" : "NEIN"));
            // Der Erzeuger wird auch bei WAHREM Glied gesucht, wenn die
            // Bedingung an einer Variablen hängt, die selbst erst gefüllt werden
            // muss — `var_vs_store` vergleicht v[a] mit einem Lager, und ob
            // v[a] überhaupt gesetzt wurde, ist die eigentliche Frage.
            if (c.Kind is "var_vs_store" or "unit_field" or "unit_is_var") todo.Enqueue(c.A);
            else if (!ok && c.Kind == "var") todo.Enqueue(c.A);
        }
        foreach (var c in r.When) Take(c, "");
        foreach (var c in r.Any) Take(c, "ODER ");
        foreach (var g in r.AnyGroups) foreach (var c in g) Take(c, "ODER-GRUPPE ");
        if (parts.Count == 0) return "(ohne Bedingung)";
        return string.Join(" ", parts);
    }

    /// <summary>Ein Glied als Text. Die Felder heissen je Art anders, darum
    /// nicht »a,b,c« für alle: eine Zeile, die man ohne die Quelle nicht lesen
    /// kann, hilft im Protokoll nicht.</summary>
    private string Show(Cond c) => c.Kind switch
    {
        "var" => $"v[{c.A}]{c.Op}{c.B}",
        "obj_owner" => $"obj_owner({c.A}){c.Op}{c.B}",
        "units" => $"units(Kl{c.A},P{c.B}){c.Op}{c.C}",
        "buildings" => $"buildings(Kl{c.A},P{c.B}){c.Op}{c.C}",
        "objects" => $"objects(Typ{c.A},P{c.B}){c.Op}{c.C}",
        "imap" => $"imap({c.A},{c.C})={(ImapAt != null ? ImapAt(c.A, c.C) : -1)}{c.Op}{c.B}",
        "var_vs_store" => $"v[{c.A}]={Var(c.A)}{c.Op}Lager({c.B},+0x{c.C:X})=" +
                          (StoreField != null ? StoreField(c.B, c.C) : -1),
        "unit_field" => $"Einheit v[{c.A}]={Var(c.A)} Feld+{c.B}{c.Op}{c.C}",
        "unit_index" => $"find_unit(P{c.A},Marke{c.B}){c.Op}{c.C}",
        "unit_is_var" => $"Einheit v[{c.A}]={Var(c.A)} hat Marke {c.B}{c.Op}{c.C}",
        "rail_link" => $"Bahnlinie {c.A} gehoert P{c.B}",
        "units_mark" => $"Marke({c.A},P{c.B}){c.Op}{c.C}",
        "time_gt" => $"Spielminute>{c.A}",
        "event" => $"Ereignis{c.Op}{c.B}",
        "selected" => $"Auswahl{c.Op}{c.B}",
        "ticks" => $"Takt {_ticks}{c.Op}{c.B}",
        "text_open" => $"Hilfefenster #{c.A} offen=" +
                       (TextOpen != null && TextOpen(c.A) ? 1 : 0) + $"{c.Op}{c.B}",
        "terrain_unit" => $"Gelaende unter Einheit {c.A}=" +
                          (TerrainAt != null && UnitField != null
                           ? TerrainAt(UnitField(c.A, 0), UnitField(c.A, 1)) : -1) +
                          $"{c.Op}{c.B}",
        "sel_field" => $"Auswahl={(Selection != null ? Selection() : -1)} " +
                       $"Feld+0x{c.B:X}{c.Op}{c.C}",
        _ => $"{c.Kind}({c.A},{c.B},{c.C}){c.Op}",
    };

    /// <summary>For the harness: the value of one of the block's variables.
    /// </summary>
    public int Var(int n) => n >= 0 && n < _var.Length ? _var[n] : -1;

    /// <summary>For the harness: what the script is doing right now.</summary>
    /// <summary>Die Missionsziele, wie der Block sie fuehrt.
    ///
    /// <para><c>v[101+k]</c> ist der Zustand des k-ten Ziels — <b>1 offen,
    /// 10 erfuellt</b> —, <c>v[131+k]</c> seine Textnummer aus HELPG.TXT. Das
    /// stand seit dem 09.08. im Vokabular und wurde nie angezeigt: Mission 1
    /// startet mit <c>v[101]=1, v[131]=110</c>, und #110 ist genau die
    /// Untermission mit den Schiffen. Wer sie erfuellte, bekam Geld und einen
    /// Klang, aber nirgends eine Bestaetigung — gemeldet als »die Nebenmission
    /// laesst sich nicht sauber abschliessen«.</para></summary>
    /// <summary>
    /// Wie viele Untermissionen noch OFFEN sind.
    ///
    /// <para>⚠ Nicht dasselbe wie <see cref="Objectives"/>: das überspringt
    /// Plätze ohne Textnummer, weil es eine ANZEIGE ist. Der Zähler des
    /// Originals fragt nur nach dem Zustand, und einen Platz ohne Text gibt es
    /// (M5 hat einen). Würde man hier dieselbe Liste nehmen, endete die
    /// Mission bei M5 zu früh.</para>
    ///
    /// <para><b>Gelesen</b> aus der Schleife @0x416D1E..0x416D42: sie läuft
    /// über 0xBC575A..0xBC5796 in Schritten von 2 — das sind bei VAR_BASE
    /// 0xBC5690 genau die <b>30 Wörter ab v[101]</b> — und zählt einen Platz
    /// als offen, wenn er weder <c>0</c> (unbenutzt) noch <c>&gt;= 10</c>
    /// (erfüllt) ist. Also <b>1..9</b>.</para>
    /// </summary>
    public int OpenObjectives()
    {
        int n = 0;
        for (int k = 0; k < 30; k++)
        {
            int st = 101 + k;
            if (st >= _var.Length) break;
            if (_var[st] >= 1 && _var[st] < 10) n++;
        }
        return n;
    }

    /// <summary>Wie viele Schritte die Nachfrist hat: <c>byte[0x4F6FA4] = 0xA</c>
    /// @0x416D11.</summary>
    public const int GraceSteps = 10;

    /// <summary>Der Rest der Nachfrist, oder −1, solange keine läuft. Das ist
    /// die Zahl, die im Fenster hinter »00:« steht — der Spieler hat sie als
    /// »ZEIT FÜR UNBEENDETE UNTERMISSIONEN 00:09« photographiert.</summary>
    public int Grace { get; private set; } = -1;

    private int _graceMinute = -1;

    /// <summary>
    /// <b>DIE NACHFRIST FÜR OFFENE UNTERMISSIONEN.</b>
    ///
    /// <para><c>mission_end</c> beendet die Mission <b>nicht</b>, solange eine
    /// Untermission offen ist — es setzt das Phasenbyte <c>0x4F6FA0</c> auf 1
    /// und kehrt zurück. Der Hauptlauf sieht die 1 (@0x416CF0), setzt sie auf
    /// 2, öffnet das Fenster (<c>push 0xA; call 0x401F32</c>) und setzt
    /// <c>byte[0x4F6FA4] = 0xA</c>. Danach gilt:
    /// <list type="bullet">
    /// <item>Jede Spielminute geht der Zähler um eins herunter (@0x4160FC).</item>
    /// <item>Steht er auf 0, endet die Mission trotzdem — als <b>SIEG</b>
    /// (<c>word[0x4FA280] = 1</c> @0x41612C).</item>
    /// <item>Sind vorher alle Untermissionen erledigt, endet sie sofort, ebenso
    /// als Sieg (@0x416D48).</item>
    /// </list></para>
    ///
    /// <para>⚠ <b>Der Takt.</b> Das Original zählt an einem Minutenmerker
    /// herunter, den der Hauptlauf alle 250 Takte setzt — bei
    /// <see cref="TicksPerSecond"/> ist das eine SPIELMINUTE. Wir hängen den
    /// Zähler darum an <see cref="Minutes"/> und rechnen die 250 nicht nach:
    /// dieselbe Uhr, ein Rechenweg weniger.</para>
    ///
    /// <para>⚠ Nur der SIEG kennt die Nachfrist. Die Niederlage (0x4D0280) ist
    /// ein Zweizeiler ohne jede Prüfung und wirkt sofort.</para>
    /// </summary>
    private void TickGrace()
    {
        if (Grace < 0 || _ended) return;
        if (OpenObjectives() == 0)
        {
            _ended = true;
            Success = true;
            Grace = -1;
            GD.Print($"Missionsskript {_script.Mission}: alle Untermissionen " +
                     "erledigt, Mission erfolgreich beendet");
            return;
        }
        if (Minutes == _graceMinute) return;
        _graceMinute = Minutes;
        Grace--;
        if (Grace > 0) return;
        _ended = true;
        Success = true;
        Grace = -1;
        GD.Print($"Missionsskript {_script.Mission}: Nachfrist abgelaufen, " +
                 $"{OpenObjectives()} Untermissionen offen, Mission beendet");
    }

    /// <summary>Die Nachfrist von Hand beenden — der Knopf »Beenden«.
    /// ⚠ UNSERE Deutung: das Original hat den Knopf, seine Wirkung ist
    /// ungelesen. Hier tut er dasselbe wie ein abgelaufener Zähler.</summary>
    public void EndGraceNow()
    {
        if (Grace < 0 || _ended) return;
        Grace = -1;
        _ended = true;
        Success = true;
        GD.Print($"Missionsskript {_script.Mission}: Nachfrist von Hand beendet");
    }

    public List<(int Text, int State)> Objectives()
    {
        var list = new List<(int, int)>();
        for (int k = 0; k < 30; k++)
        {
            int st = 101 + k, tx = 131 + k;
            if (tx >= _var.Length || _var[st] == 0 || _var[tx] == 0) continue;
            list.Add((_var[tx], _var[st]));
        }
        return list;
    }

    public string Line()
    {
        int fast = 0;
        foreach (var r in _script.Rules) if (r.EveryTick) fast++;
        return $"Skript M{_script.Mission} ({_script.Block}): {RulesFired} Regeln, " +
               $"Takt {_ticks} ({Seconds:0.00} s, {Minutes} Spielmin), " +
               $"{BlockRuns} Blockdurchlaeufe; Tor 0x{_script.Gate:X} — " +
               $"{fast} von {_script.Rules.Count} Regeln jeden Takt" +
               (_ended ? (Success ? ", GEWONNEN" : ", VERLOREN") : "");
    }

    // ---- Was die Runtime von diesem Skript ueberhaupt ausfuehren kann -------
    //
    // ⚠ 11.08.2026, und der Grund dafuer ist eine alte Lehre in neuer Gestalt:
    // ein fehlender Haken macht eine Bedingung FALSCH und eine Wirkung STILL.
    // Beides sieht im Spiel aus wie »die Mission tut nichts« — nicht wie ein
    // Fehler. Nach 270 gelesenen Regeln ist die Frage »welche davon koennen
    // hier ueberhaupt feuern« darum wichtiger als jede einzelne Mission, und
    // sie muss die Engine selbst beantworten, nicht das Auge.

    /// <summary>Bedingungsarten, für die ein Haken hängt.</summary>
    private bool Hooked(Cond c) => c.Kind switch
    {
        "var" or "time_gt" or "time_after" or "event" or "block_gate" => true,
        "obj_owner" => ObjOwner != null,
        "units" => UnitCount != null,
        "buildings" => BuildingCount != null,
        "objects" => ObjectCount != null,
        "unit_index" => FindUnit != null,
        "unit_field" or "unit_pos" => UnitField != null,
        "var_vs_store" => StoreField != null,
        "selected" => Selection != null,
        "units_mark" => MarkCount != null,
        "unit_is" or "unit_is_var" => UnitHasMark != null,
        "rail_link" => RailLinkHeld != null,
        "money_of" => MoneyOf != null,
        "kills" => KillCount != null,
        "losses" => LossCount != null,
        "kills_vs_losses" => KillCount != null && LossCount != null,
        "var_vs_losses" => LossCount != null,
        "store_energy" => StoreField != null,
        "bridge" => BridgeUsed != null,
        "store" or "capture_frac" => StoreField != null,
        "unit_alive" => UnitAlive != null,
        "unit_energy" => UnitField != null,
        "var_vs_kills" => KillCount != null,
        "terrain" => TerrainAt != null,
        "terrain_unit" => TerrainAt != null && UnitField != null,
        "sel_field" => Selection != null && UnitField != null,
        // `ticks` braucht nur die eigene Uhr, `text_open` haengt von Haus aus.
        "ticks" => true,
        "text_open" => TextOpen != null,
        "imap" => ImapAt != null,
        _ => false,
    };

    /// <summary>Wirkungsarten, für die ein Haken hängt.</summary>
    private bool Hooked(Act a) => a.Kind switch
    {
        "inc" or "set" or "set_time" or "take_var" or "end" => true,
        "text" => ShowText != null,
        "find_unit" => FindUnit != null,
        "set_store" => StoreField != null,
        "set_imap" => ImapAt != null,
        "stock" => SetStoreField != null,
        "set_units" => UnitCount != null,
        "space_in" => SpaceInSpawn != null,
        "place_unit" => PlaceUnit != null,
        "order_at" => OrderUnitAt != null,
        "money" => AddMoney != null,
        "sound" => PlaySound != null,
        "close_texts" => CloseTexts != null,
        "order" => OrderUnit != null,
        "move" => MoveUnit != null,
        "set_ai" => SetAi != null,
        "fire_at" => FireAt != null,
        "add_target" => AddTarget != null,
        "remove_unit" => RemoveUnit != null,
        "sell_unit" => SellUnit != null,
        "change_owner" => ChangeOwner != null,
        "set_relation" => SetRelation != null,
        "stop_transport" => StopTransport != null,
        _ => false,
    };

    /// <summary>Was dieses Skript nicht ausführen kann: je fehlender Art, wie
    /// oft sie vorkommt und in wievielen Regeln. Leer heisst: alles hängt.
    /// </summary>
    /// <summary>Was die Belegungskarte an den Stellen sagt, die dieses Skript
    /// abfragt — fuer den Pruefstand, damit eine Bedingung, die nicht wahr
    /// wird, ihren Wert nennt statt nur zu schweigen.</summary>
    public string ImapProbe()
    {
        if (ImapAt == null) return "imap: kein Haken";
        var seen = new List<string>();
        foreach (var r in _script.Rules)
        {
            foreach (var c in r.When)
                if (c.Kind == "imap")
                    seen.Add($"(Sp{c.A},Z{c.C}) ist {ImapAt(c.A, c.C)}, verlangt {c.Op} {c.B}");
            foreach (var c in r.Any)
                if (c.Kind == "imap")
                    seen.Add($"ODER (Sp{c.A},Z{c.C}) ist {ImapAt(c.A, c.C)}, verlangt {c.Op} {c.B}");
        }
        return seen.Count == 0 ? "" : "imap: " + string.Join(" | ", seen);
    }

    /// <summary>
    /// Der TAKT dieses Skripts in Zahlen — fuer <c>--tick-check</c>.
    ///
    /// <para>Drei Groessen, die alle nachrechenbar sind: die Zahl der Takte,
    /// die <paramref name="seconds"/> Sekunden ergeben (50 je Sekunde), die
    /// Zahl der Durchlaeufe des langsamen Blockteils (jeder 100.) und die
    /// Aufteilung der Regeln am Tor. Weicht eine davon ab, ist der Takt nicht
    /// der gemessene.</para>
    /// </summary>
    public string TickLine(double seconds)
    {
        int fast = 0;
        foreach (var r in _script.Rules) if (r.EveryTick) fast++;
        long wantTicks = (long)Math.Round(seconds * TicksPerSecond);
        // Gelaufen sind die Takte 0 .. wantTicks-1, und der Block kommt bei
        // jedem Vielfachen von 100 dran — Takt 0 zaehlt also mit.
        long wantRuns = (wantTicks + BlockPeriod - 1) / BlockPeriod;
        return $"Takt {_ticks}/{wantTicks}" + (_ticks == wantTicks ? "" : " ⚠") +
               $", Blockdurchlaeufe {BlockRuns}/{wantRuns}" +
               (BlockRuns == wantRuns ? "" : " ⚠") +
               $", Tor 0x{_script.Gate:X}" + (_script.Gate == 0 ? " ⚠ FEHLT" : "") +
               $", {fast}/{_script.Rules.Count} Regeln jeden Takt";
    }

    public string Coverage(out int rules, out int blocked)
    {
        var missing = new SortedDictionary<string, int>();
        rules = _script.Rules.Count;
        blocked = 0;
        foreach (var r in _script.Rules)
        {
            bool bad = false;
            foreach (var c in r.When)
                if (!Hooked(c)) { missing[c.Kind + "?"] = missing.GetValueOrDefault(c.Kind + "?") + 1; bad = true; }
            foreach (var c in r.Any)
                if (!Hooked(c)) { missing[c.Kind + "?"] = missing.GetValueOrDefault(c.Kind + "?") + 1; bad = true; }
            foreach (var a in r.Then)
                if (!Hooked(a)) { missing[a.Kind + "!"] = missing.GetValueOrDefault(a.Kind + "!") + 1; bad = true; }
            if (bad) blocked++;
        }
        var parts = new List<string>();
        foreach (var kv in missing) parts.Add($"{kv.Key}x{kv.Value}");
        return string.Join(" ", parts);
    }
}
