namespace AkteEuropaReborn.Simulation;

using System;
using AkteEuropaReborn.Core.Math;
using Godot;

/// <summary>
/// DIE GRUNDLAGE FÜR DAS NETZSPIEL: eine Partie muss zweimal gleich laufen.
///
/// <para>Das Original von 1997 ist ein <b>Lockstep-Netzwerkspiel</b>:
/// <c>SendGameMessage</c> @0x404700 schickt je Befehl 236 Byte über
/// <c>IDirectPlay::Send</c>, die Weiche Netz/Einzelspieler sitzt in
/// <c>command_submit</c> @0x4C2190, und die Hauptschleife wartet mit einer
/// 5-Sekunden-Schranke auf den Server (»Warte auf Server« @0x415221). Über die
/// Leitung gehen also <b>Befehle</b>, nicht Zustände — und das geht nur, wenn
/// jede Maschine aus denselben Befehlen denselben Zustand rechnet.</para>
///
/// <para>Diese Datei liefert die zwei Werkzeuge, die dafür VOR jedem Netzcode
/// gebraucht werden:</para>
/// <list type="number">
/// <item><b>Eine Prüfsumme über den ganzzahligen Zustand</b>
/// (<see cref="Hasher"/>). Sie nimmt bewusst <b>nichts Fliesskommiges</b> auf —
/// weder die sieben Uhren noch die Zwischenposition zwischen zwei Zellen.
/// Nähme sie die auf, misse sie genau das Rauschen, gegen das sie beweisen
/// soll. Der diskrete Zustand des Spiels ist durchweg ganzzahlig: Zelle, HP,
/// Munition, Sprit, Lager, Geld.</item>
/// <item><b>Eine gekeimte Zufallsquelle</b> aus einem Kartenkeim
/// (<see cref="Roll"/> …), damit zwei Maschinen dieselben Würfel werfen.</item>
/// </list>
///
/// <para>⚠ <b>Was diese Datei NICHT heilt.</b> Die Spielwelt läuft heute in
/// <c>MapEntityLayer._Process(double delta)</c> auf BILDZEIT mit einem
/// <c>float dt</c>. Solange das so ist, hängt die ANZAHL der Takte — und damit
/// die Anzahl der Würfe — an der Bildrate des Rechners. Ein gekeimter Würfel
/// hilft dagegen nichts: beide Maschinen würfeln dieselbe Folge, aber
/// verschieden oft. Der Prüfstand in <c>DeterminismHarness.cs</c> misst genau
/// diesen Fehler; siehe die Zahlen dort.</para>
/// </summary>
public static class Determinism
{
    // ---- Kartenkeim ---------------------------------------------------------

    /// <summary>Der Keim dieser Partie. Im Netzspiel schickt ihn der Vermittler
    /// mit; bis dahin kommt er aus dem Kartennamen (siehe
    /// <see cref="NewMap"/>) oder aus <c>--determinism-seed=</c>.</summary>
    public static uint Seed { get; private set; } = 1;

    /// <summary>Wie oft seit dem letzten <see cref="NewMap"/> gewürfelt wurde.
    /// Das ist die schärfste Einzelzahl des Prüfstands: laufen zwei Läufe mit
    /// verschiedener Bildrate, geht diese Zahl auseinander, BEVOR die Prüfsumme
    /// es tut — sie zeigt, dass die Takte gezählt und nicht die Würfel schuld
    /// sind.</summary>
    public static long Draws { get; private set; }

    /// <summary>Gegenprobe: ist das gesetzt, kommen die Zahlen aus einer
    /// UNGEKEIMTEN Quelle. Der Prüfstand schaltet das mit
    /// <c>--determinism-poison</c> ein, damit belegt ist, dass er den Fehler
    /// überhaupt sehen KANN. Ein Prüfstand, der den Fehler nicht sieht, ist
    /// wertlos.</summary>
    public static bool Poisoned { get; set; }

    /// <summary>Ein von <c>--determinism-seed=</c> erzwungener Keim; er
    /// überstimmt den Kartennamen.</summary>
    public static uint? Forced { get; set; }

    // ---- die Ströme ---------------------------------------------------------
    //
    // ⚠ WARUM ES MEHR ALS EINEN WÜRFEL GIBT — und das ist keine Vorsichts-
    // massnahme, sondern gemessen.
    //
    // Der Zwillings-Prüfstand (DeterminismTwin.cs) fährt ZWEI vollständige
    // Simulationen im SELBEN PROZESS. Sie teilen sich alles, was `static` ist —
    // und dieser Würfel war `static`. Instanz A zieht in ihrem Takt drei
    // Zahlen, dann zieht Instanz B im selben Takt die DREI DARAUF FOLGENDEN.
    // Beide rechnen dann korrekt, aber mit verschiedenen Zahlen, und der
    // Prüfstand meldete ein Auseinanderlaufen, das im Netzspiel gar nicht
    // aufträte (dort hat jede Maschine ihren eigenen Prozess).
    //
    // Ein Prüfstand, der einen Fehler meldet, den es nicht gibt, ist genauso
    // wertlos wie einer, der einen echten übersieht. Darum: je Simulation ein
    // eigener Strom, und der Prüfstand schaltet vor jedem Takt um.
    //
    // ⚠ UNSERE SETZUNG: DREI Ströme. Zwei waren es, solange nur der Zwillings-
    // Prüfstand zwei Simulationen fuhr. Der Befehlsprüfstand
    // (Simulation/Commands/CommandCheck.cs) braucht einen dritten, weil er drei
    // Läufe vergleicht: Befehl über den Puffer, Befehl auf dem Direktweg, und
    // KEIN Befehl. Ohne den dritten könnte er nicht belegen, dass der Befehl
    // überhaupt etwas bewirkt hat — und ein Vergleich zweier gleicher Läufe,
    // die beide nichts tun, ist grün und wertlos (Regel 7).
    //
    // Strom 0 ist der des normalen Spiels — wer nie umschaltet, merkt von der
    // ganzen Einrichtung nichts.
    private static DeterministicRng[] _streams =
        { new DeterministicRng(1), new DeterministicRng(1), new DeterministicRng(1) };
    private static long[] _streamDraws = new long[3];
    private static int _cur;

    /// <summary>
    /// Auf den Würfelstrom dieser Simulation umschalten. Ruft der Zwillings-
    /// Prüfstand vor jedem Takt; sonst ruft es niemand und alles läuft auf
    /// Strom 0 wie bisher.
    /// </summary>
    public static void UseStream(int i)
    {
        if (i < 0 || i >= _streams.Length) return;
        _streamDraws[_cur] = Draws;      // den Zähler des alten Stroms sichern
        _cur = i;
        Draws = _streamDraws[i];
    }

    /// <summary>Wie viele Würfe der Strom <paramref name="i"/> hinter sich hat.
    /// Der Prüfstand druckt beide: gehen sie auseinander, hat eine der beiden
    /// Simulationen ÖFTER gewürfelt — das ist ein anderer Fehler als
    /// »dieselbe Anzahl, andere Zahlen« und muss unterscheidbar sein.</summary>
    public static long DrawsOf(int i)
        => i == _cur ? Draws : (i >= 0 && i < _streamDraws.Length ? _streamDraws[i] : 0);

    /// <summary>Alle Ströme auf denselben Keim zurückstellen — der Anfangs-
    /// zustand einer Partie. <see cref="NewMap"/> tut das ohnehin; der
    /// Prüfstand ruft es zusätzlich, nachdem BEIDE Simulationen geladen sind
    /// (das Laden selbst würfelt, siehe die 15 Würfe in Takt 0).</summary>
    public static void ResetStreams()
    {
        for (int i = 0; i < _streams.Length; i++)
        {
            _streams[i] = new DeterministicRng(Seed);
            _streamDraws[i] = 0;
        }
        Draws = 0;
    }

    private static ref DeterministicRng Rng => ref _streams[_cur];

    /// <summary>
    /// Eine neue Partie auf dieser Karte: Keim setzen, Würfel zurückstellen.
    ///
    /// <para>Der Keim aus dem KARTENNAMEN ist eine Setzung von uns — er hält
    /// den Prüfstand ohne weiteren Schalter reproduzierbar. Im echten Netzspiel
    /// muss er vom Vermittler kommen, sonst spielt jeder eine andere Partie
    /// derselben Karte.</para>
    /// </summary>
    public static void NewMap(string mapName)
    {
        EnsureConfigured();
        uint s = Forced ?? Fnv32(mapName ?? "");
        if (s == 0) s = 0x9E3779B9;
        Seed = s;
        ResetStreams();

        // Godots GLOBALER Würfel wird mitgekeimt.
        //
        // ⚠ HIER STANDEN DREI FUNDSTELLEN, DIE ES NICHT MEHR GIBT
        // (»MapEntityLayer 4215/4217 Schadensrechnung, RailFreight 1083
        // Gleisbruch«) — nachgezählt am 21.08.2026. Eine Zeilenangabe in einem
        // Kommentar wandert mit jeder Einfügung; wer ihr folgt, landet
        // irgendwo. Darum steht hier jetzt eine PRÜFBARE Aussage statt einer
        // Adresse:
        //
        // Stand 21.08.2026 ruft KEINE simulationsrelevante Stelle mehr
        // GD.Randi. Übrig sind allein die vier Würfe in Audio/GameSounds.cs
        // (welche Stimmprobe, welche Tonhöhe) — die dürfen dort bleiben, weil
        // ein Klang den Spielzustand nicht verändert. Nachzählen mit:
        //   grep -rn "GD.Randi()" Scripts --include=*.cs
        // ⚠ 16.10.2026 nachgezählt (bug-529): es waren inzwischen sieben —
        // drei neue im Takt (Landungsbruecke, Pionierbruecke, Todeslicht), alle
        // mit reiner Bildwirkung. Sie stehen jetzt auf KlangWuerfel; übrig
        // bleiben die vier in GameSounds (und die drei hinter
        // --systemzufall-alt).
        //
        // Das Keimen macht auch die REPRODUZIERBAR, aber nicht netztauglich:
        // der globale Würfel wird von Klang und Anzeige angefasst, und die
        // laufen auf Bildzeit — zwei Maschinen ziehen daraus verschieden oft.
        GD.Seed(s);
    }

    private static bool _configured;

    /// <summary>
    /// <c>--systemzufall-alt</c> (bug-529, 16.10.2026) — der Stand vor dem
    /// Umbau: <c>GD.Randi</c> bzw. ein ungekeimtes <c>System.Random</c> an den
    /// fünf Bildstellen (Molenbild Landungsbruecke, Brückenvariante
    /// Pionierbruecke, Lichtplatz Todeslicht, Fahrzittern, Meldewurf Power)
    /// und — der eigentliche Netzfehler — <see cref="Roll"/> in
    /// <c>SpeakHit</c>, das nur auf der Maschine würfelte, deren Sichtspieler
    /// das getroffene Gebäude gehört.
    ///
    /// <para>Heute nehmen alle sechs den Bildwürfel
    /// <c>Audio.GameSounds.KlangWuerfel</c> (fester Keim 20260920, eigener
    /// Strom): sie wirken nur auf Bild, Klang oder Meldung, also dürfen sie den
    /// Simulationsstrom NICHT verschieben, und sie sind gekeimt, also
    /// wiederholbar. Determinism.cs hatte keinen eigenen Zierstrom; die Regel
    /// des Hauses steht seit dem 20.09.2026 an <c>KlangWuerfel</c> (»wer einen
    /// Klang oder ein Bild auswürfelt, nimmt DIESEN Würfel«).</para>
    /// </summary>
    public static bool SystemzufallAlt;

    /// <summary>
    /// <c>--pruefsumme-gleitkomma</c> (16.10.2026, Bewertung P5 in
    /// berichte/desync-opus.md): die Prüfsumme nimmt ZUSÄTZLICH die
    /// Gleitkommapositionen auf — Einheiten <c>Pos</c>, Flugzeuge <c>Pos</c>
    /// und <c>Alt</c> —, gerundet auf 1/256 Bildpunkt.
    ///
    /// <para>⚠ Ohne den Schalter bleibt die Summe, wie sie ist (»nichts
    /// Fliesskommiges«, siehe Kopf): ein Gleitkomma-Auseinanderlaufen zwischen
    /// zwei Rechnern zeigte sich dann erst, wenn es eine Zelle, eine Richtung
    /// oder einen Treffer kippt — Takte bis Minuten später, und an einer
    /// Stelle, die nichts mit der Ursache zu tun hat. Mit dem Schalter sieht
    /// man es im ersten Prüftakt. Der Zahlenauszug (<c>DeterminismSnapshot</c>)
    /// nimmt die Positionen NICHT auf; meldet der Netzprüfstand »Prüfsummen
    /// verschieden, Auszüge gleich«, ist das unter diesem Schalter genau der
    /// Befund »Gleitkomma«.</para>
    /// </summary>
    public static bool PruefsummeGleitkomma;

    /// <summary><c>--pruefsumme-voll</c> (17.10.2026, Direktbefehle2): die
    /// Prüfsumme nimmt zusätzlich den Zustand auf, den die Befehle des zweiten
    /// Pakets schreiben — siehe <c>DeterminismVollAnhaengen</c>. Ohne Schalter
    /// unverändert.</summary>
    public static bool PruefsummeVoll;

    /// <summary>Eine Gleitkommazahl für die Prüfsumme: auf 1/256 gerundet.</summary>
    public static int Gerundet(float v) => (int)System.MathF.Round(v * 256f);

    /// <summary>
    /// Die Schalter, die den Keim betreffen, EINMAL lesen — und zwar früh
    /// genug: <see cref="NewMap"/> läuft beim Kartenladen, also bevor der
    /// Prüfstand selbst zum ersten Mal drankommt.
    ///
    /// <list type="bullet">
    /// <item><c>--determinism-seed=&lt;zahl&gt;</c> — Keim erzwingen</item>
    /// <item><c>--determinism-seed=0</c> — <b>die Gegenprobe</b>: KEIN Keim,
    /// ungekeimt würfeln</item>
    /// <item><c>--determinism-poison</c> — dasselbe, ausgeschrieben</item>
    /// </list>
    ///
    /// <para>⚠ <b>Warum es die Schreibweise <c>--determinism-seed=0</c>
    /// gibt.</b> Dieser Godot-Bau stürzt beim Kartenladen gelegentlich mit
    /// »Internal CLR error« in seinem eigenen <c>DisposablesTracker</c> ab
    /// (Kachelschleife in NavGrid.Build, siehe dort), und ob er das tut, hängt
    /// an der Speicherbereinigung — die wiederum an der LÄNGE DER BEFEHLSZEILE.
    /// Gemessen am 12.08.2026: derselbe Lauf mit einem zusätzlichen,
    /// wirkungslosen <c>--zz</c> stürzte dreimal von dreimal ab, ohne ihn
    /// dreimal von dreimal nicht. Damit die Gegenprobe nicht an so etwas
    /// scheitert, muss sie dieselbe FORM der Befehlszeile haben wie der
    /// Vergleichslauf — nur eine andere Zahl darin.</para>
    /// </summary>
    public static void EnsureConfigured()
    {
        if (_configured) return;
        _configured = true;
        foreach (string a in Core.CommandLine.Args)
        {
            if (a == "--determinism-poison") Poisoned = true;
            else if (a == "--systemzufall-alt") SystemzufallAlt = true;     // bug-529
            else if (a == "--pruefsumme-gleitkomma") PruefsummeGleitkomma = true;
            else if (a == "--pruefsumme-voll") PruefsummeVoll = true;
            else if (a.StartsWith("--determinism-seed="))
            {
                string v = a["--determinism-seed=".Length..];
                if (!uint.TryParse(v, out uint s)) continue;
                if (s == 0) Poisoned = true;     // Keim 0 = kein Keim
                else Forced = s;
            }
        }
    }

    /// <summary>FNV-1a über einen Namen — der Kartenkeim.</summary>
    public static uint Fnv32(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s) { h ^= c; h *= 16777619u; }
        return h;
    }

    // ---- die Würfel ---------------------------------------------------------

    /// <summary>Ein Wurf 0 … <paramref name="n"/>-1, wie es
    /// <c>GD.Randi() % n</c> tat — nur gekeimt.</summary>
    public static int Roll(int n)
    {
        if (n <= 1) return 0;
        Draws++;
        if (Spur != null) SpurEintragen();
        if (Poisoned) return System.Random.Shared.Next(n);   // Gegenprobe
        return (int)(Rng.NextUInt() % (uint)n);
    }

    /// <summary>Ein Wurf <paramref name="lo"/> … <paramref name="hi"/>,
    /// EINSCHLIESSLICH beider Enden — wie
    /// <c>RandomNumberGenerator.RandiRange</c>.</summary>
    public static int Range(int lo, int hi) => hi <= lo ? lo : lo + Roll(hi - lo + 1);

    /// <summary>Trifft mit <paramref name="num"/>/<paramref name="den"/>
    /// Wahrscheinlichkeit — ganzzahlig, ohne Fliesskomma. Das ist die Form, die
    /// die Produktionschance (sec24 +0x03/+0x04) braucht.</summary>
    public static bool Chance(int num, int den)
    {
        if (den <= 0) return false;
        if (num >= den) { Draws++; return true; }
        return Roll(den) < num;
    }

    /// <summary>Die rohe Zahl, wenn ein Aufrufer selbst rechnen will.</summary>
    public static uint NextUInt() { Draws++; if (Spur != null) SpurEintragen(); return Poisoned ? (uint)System.Random.Shared.Next() : Rng.NextUInt(); }

    // ---- die Wurfspur (16.10.2026, Netzprüfstand) ----------------------------

    /// <summary>
    /// ⚠ NUR FÜR DEN PRÜFSTAND (<c>--wurf-spur=von-bis</c> in
    /// <c>Network/NetGame.cs</c>): ist die Liste gesetzt, trägt jeder Wurf den
    /// Namen seines Rufers ein. Gemessen hat sie den ersten Fund des
    /// 16.10.2026: zwei Maschinen mit gleicher Prüfsumme, aber zwischen Takt
    /// 2010 und 2020 schon 32907 gegen 32906 Würfe — ein Wurf, der an etwas
    /// Örtlichem hing. Eine Summe sagt nie, WER würfelt; die Spur sagt es.
    /// Ohne den Schalter kostet sie einen Vergleich auf null.
    /// </summary>
    public static System.Collections.Generic.List<string>? Spur;

    private static void SpurEintragen()
    {
        var st = new System.Diagnostics.StackTrace(1, false);
        for (int i = 0; i < st.FrameCount; i++)
        {
            var m = st.GetFrame(i)?.GetMethod();
            if (m == null || m.DeclaringType == typeof(Determinism)) continue;
            // zwei Rufer tief, damit ein gemeinsamer Helfer nicht alles verdeckt
            var m2 = i + 1 < st.FrameCount ? st.GetFrame(i + 1)?.GetMethod() : null;
            Spur!.Add($"{m.Name} <- {m2?.Name}");
            return;
        }
        Spur!.Add("?");
    }

    // ---- die Prüfsumme ------------------------------------------------------

    /// <summary>
    /// FNV-1a über 64 Bit, gefüttert mit GANZEN ZAHLEN.
    ///
    /// <para>Warum FNV und nicht CRC: es braucht keine Tabelle, ist in drei
    /// Zeilen nachprüfbar, und die Reihenfolge geht ein — genau das soll es,
    /// denn eine vertauschte Einheitenliste IST ein Auseinanderlaufen.</para>
    /// </summary>
    public struct Hasher
    {
        private ulong _h;

        public static Hasher Start() => new Hasher { _h = 14695981039346656037UL };

        public void Add(int v)
        {
            _h ^= (uint)v;
            _h *= 1099511628211UL;
        }

        public void Add(bool v) => Add(v ? 1 : 0);

        public void Add(long v) { Add((int)v); Add((int)(v >> 32)); }

        public ulong Value => _h;
    }
}
