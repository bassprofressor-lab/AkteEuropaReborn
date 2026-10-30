namespace AkteEuropaReborn.Audio;

/// <summary>
/// ⭐ <b>PAKET R — DER KLANGABGLEICH AUSSERHALB 120..143</b> (30.10.2026,
/// <c>berichte/klangabgleich-rest-opus.md</c> §4 R1..R15, Bau
/// <c>berichte/bau-klang-rest.md</c>, bug-677..bug-681).
///
/// <para>Hier stehen die Nummern, Auswahlregeln und Gegenschalter; die Ausloeser
/// sitzen an ihrer Stelle (je Stelle die Adresse im Kommentar). Der Pruefstand
/// ist <c>--klang-rest-check</c> (Simulation/KlangRest.cs).</para>
///
/// <para>⚠ <b>Gleichschritt:</b> kein Klang dieses Pakets wuerfelt mit
/// <see cref="Simulation.Determinism"/>. Wer einen Klang auswaehlt, nimmt
/// <see cref="KlangRoll"/> — ausser unter dem Nullmodell
/// <c>--klangwuerfel-alt</c>, das den Stand bis 0.6.12 wiederherstellt.</para>
/// </summary>
public static partial class GameSounds
{
    // ================= R1 — der Klangwuerfel (bug-677) ========================

    /// <summary><c>--klangwuerfel-alt</c> — Stand bis 0.6.12: die sechs Klangstellen
    /// (TruemmerCheck 400..403, Gebaeudebrand 399/410..415 samt Feinlage,
    /// Druckwellenring 400, Fusstod 77..79) wuerfeln mit
    /// <see cref="Simulation.Determinism.Roll"/>, und <c>--brandklang-aus</c>
    /// laesst diese Wuerfe aus (aendert also das Spiel). Nullmodell zu R1: NET07
    /// muss damit wieder <c>C6FFB8FB0B11ADC8</c> geben.</summary>
    public static bool KlangwuerfelAlt;

    /// <summary>
    /// Ein Wurf fuer eine KLANG- oder BILDauswahl. Das Original zieht an allen
    /// diesen Stellen aus seinem einen <c>rand()</c> (z. B. C 0x4048F8 fuer
    /// 399 → 400+rand%6); wir nicht, weil ein Klangschalter das Spiel nicht
    /// aendern darf (cerebrum [c-cba258], [c-c70748]).
    /// ⚠ UNSERE SETZUNG: der eigene Wuerfel (<see cref="KlangWuerfel"/>) statt
    /// des gemeinsamen Stroms — die Verteilung bleibt, die Folge nicht.
    /// </summary>
    public static int KlangRoll(int n)
        => KlangwuerfelAlt ? Simulation.Determinism.Roll(n) : KlangWuerfel(n);

    // ================= R2 — die Anwaehlstimme 0x4331E0 (bug-678) =============

    /// <summary><c>--anwahlstimme-alt</c> — Stand bis 0.6.12: das Anwaehlen spricht
    /// mit der Fahrbefehlstafel <c>0x429290</c> (<see cref="Voice"/>, 2 daneben),
    /// ein Flugzeug schweigt.</summary>
    public static bool AnwahlstimmeAlt;

    /// <summary>183 — ein FLUGZEUG wird angewaehlt (Auswahl ≥ 20000): C
    /// <c>0x433318</c> <c>mov si,0xB7; xor bp,bp; bl := 1</c>.</summary>
    public const int AnwahlFlugzeug = 183;

    /// <summary>
    /// <b>Was eine Einheit sagt, wenn sie ANGEWAEHLT wird</b> — C <c>0x4331E0</c>
    /// (Klangruf F <c>0x4324D7</c>), Rufer im Klickverteiler <c>0x4373EB</c>/<c>0x437407</c>
    /// und in der Flugzeugliste <c>0x44BE29</c>/<c>0x44BE70</c>; Klang Modus 1 an
    /// der Einheit (C <c>0x433397</c>). Selbst gelesen:
    /// <code>
    ///   Gattung 0 (+0x0A):  +0x28 &gt; 50 ? si 163 bp 169 : si 150 bp 156, bl 2
    ///   Gattung 1:  (+0x0B &gt;&gt; 1) − 1, Tafel 0x4333AC (11 Eintraege, ohne Vorzeichen):
    ///               0,2 → 199/205   1 → 211/217   3 → 222/228   4..7, sonst → 187/193
    ///               8 → 233/239     9 → 244, bl 4, bp 0      10 → 249, bl 4, bp 0
    ///   Gattung 3:  163/169       sonst: si 176, bl 1, bp 0
    ///   Auswahl ≥ 20000 (Flugzeug): si 183, bl 1, bp 0
    ///   bp ≠ 0 und rand&amp;1 ≠ 0  → bp + rand%3,  sonst si + rand%bl   (0x433344..0x433393)
    /// </code>
    /// <para>⚠ Die Zahlen liegen 2 UNTER denen von <see cref="Voice"/>
    /// (0x429290, der Fahrbefehl): 150 statt 152, 187 statt 189 … — bei uns
    /// spielte das Anwaehlen bis 0.6.12 die Fahrbefehlstafel.</para>
    /// <para>⚠ Ein GEBAEUDE (8000..19999) nimmt im Original <c>si/bl/bp</c> aus dem
    /// Hochwort eines gesicherten Registers (C <c>0x43332F</c>, <c>[esp+0x12]</c> —
    /// liegt im gesicherten <c>ebx</c>, nicht im Argument). Das ist ungelesen;
    /// wir lassen Gebaeude stumm wie bisher (UNSERE SETZUNG).</para>
    /// </summary>
    /// <returns>Die Nummer (Modus 1 an der Einheit).</returns>
    public static int SelectVoice(int subclass, int chassis, int field28, bool flugzeug = false)
    {
        int si, bl = 2, bp;
        if (flugzeug) { si = AnwahlFlugzeug; bl = 1; bp = 0; }
        else switch (subclass)
        {
            case 0:
                if (field28 > 50) { si = 163; bp = 169; } else { si = 150; bp = 156; }
                break;
            case 1:
                uint i = (uint)((chassis >> 1) - 1);       // `dec eax; cmp eax,0xA; ja`
                switch (i)
                {
                    case 0: case 2: si = 199; bp = 205; break;
                    case 1: si = 211; bp = 217; break;
                    case 3: si = 222; bp = 228; break;
                    case 8: si = 233; bp = 239; break;
                    case 9: si = 244; bl = 4; bp = 0; break;
                    case 10: si = 249; bl = 4; bp = 0; break;
                    default: si = 187; bp = 193; break;   // 4..7 und ausserhalb (0x4332B7)
                }
                break;
            case 3: si = 163; bp = 169; break;
            default: si = 176; bl = 1; bp = 0; break;
        }
        if (bp != 0 && KlangWuerfel(2) != 0) return bp + KlangWuerfel(3);   // 0x433349..0x433375
        return si + KlangWuerfel(bl);                                       // 0x433382..0x433393
    }

    // ================= R3 — die Todesstimme des Fussvolks 0x40B2E0 ============

    /// <summary><c>--fusstodstimme-alt</c> — Stand bis 0.6.12: der erschossene
    /// Fusssoldat spielt 77..79 (den Klang des UEBERFAHRENEN am Ende des
    /// Haltens, 0x406E4D).</summary>
    public static bool FusstodstimmeAlt;

    /// <summary>
    /// <b>Die Todesstimme eines Fusssoldaten</b> — C <c>0x40B2E0</c> (Klangruf
    /// F <c>0x40B248</c>), gerufen aus der Todesroutine <c>0x40B530</c> bei Gattung 1;
    /// Klang Modus 1 an der Einheit (C <c>0x40B358</c>), kein Besitzertor. Selbst
    /// gelesen, Tafel <c>0x40B364</c>, Index <c>(+0x0B &gt;&gt; 1) − 1</c> ohne Vorzeichen:
    /// <code>
    ///   0, 2 → 208 + rand%2     1 → 220      3 → 231      8 → 242     10 → 252
    ///   9    → 296 + rand%2   (Plaetze 296/297 sind LEER: still im Original)
    ///   4..7 und sonst → 196 + rand%2       (0x40B318)
    /// </code>
    /// <para>⚠ Auch bei Teiler 1 zieht das Original <c>rand()</c> (0x40B349) — bei
    /// uns kein Belang, der Wurf ist der Klangwuerfel.</para>
    /// </summary>
    public static int InfantryDeathVoice(int chassis)
    {
        uint i = (uint)((chassis >> 1) - 1);
        (int di, int si) = i switch
        {
            0 or 2 => (208, 2),
            1 => (220, 1),
            3 => (231, 1),
            8 => (242, 1),
            9 => (296, 2),
            10 => (252, 1),
            _ => (196, 2),
        };
        return di + KlangWuerfel(si);
    }

    // ================= R4 — die Hilfestimme (bug-678) =========================

    /// <summary><c>--hilfeklang-alt</c> — Stand bis 0.6.12: jedes Hilfefenster
    /// mit Nummer &lt; 550 spricht 1000+n, darueber n selbst (die Grenze verkehrt
    /// herum), und <c>show_text</c> (0x443490) schweigt.</summary>
    public static bool HilfeklangAlt;

    /// <summary>
    /// Welcher Platz zu einem Hilfefenster spricht — und ob es ueberhaupt
    /// spricht (−1). Zwei Routinen, beide selbst gelesen:
    /// <code>
    ///   show_text2 C 0x4432E0 (Riegel 0x87AE00):  byte[0x8934C4] (Sprache) an? → 1000 + n
    ///                                              (C 0x44330C..0x443323)
    ///   show_text  C 0x443490 (ohne Riegel):     KEIN Schalter;
    ///                                              500 &lt; n &lt; 550 → n, sonst 1000 + n
    ///                                              (C 0x443587 cmp si,0x1F4 / jle,
    ///                                               0x44358E cmp si,0x226 / jge, Ruf 0x4435A9)
    /// </code>
    /// <para>Bis 0.6.12 stand hier nur EINE Regel <c>n &lt; 550 ? n+1000 : n</c> fuer
    /// show_text2 — die Grenze von show_text, verkehrt herum, an der falschen
    /// Routine. (Der Bericht §1e nennt die Routinen umgekehrt; massgeblich ist
    /// <c>aekernel-tools/mission_logic.py</c>: SHOW_TEXT = 0x443490.)</para>
    /// <para>⚠ Der Schalter <c>byte[0x8934C4]</c> ist bei uns
    /// <see cref="UI.Settings.Announcements"/> (UNSERE SETZUNG, wie bisher).</para>
    /// </summary>
    /// <param name="ohneRiegel">true = show_text (0x443490), false = show_text2.</param>
    public static int HilfeStimme(int n, bool ohneRiegel)
    {
        if (n < 0) return -1;
        if (HilfeklangAlt)
        {
            if (ohneRiegel || !UI.Settings.Announcements) return -1;
            return n < 550 ? n + HelpVoiceBase : n;
        }
        if (ohneRiegel) return n > 500 && n < 550 ? n : n + HelpVoiceBase;
        return UI.Settings.Announcements ? n + HelpVoiceBase : -1;
    }

    // ================= R5 — das Briefing (bug-679) ============================

    /// <summary><c>--briefingklang-alt</c> — Stand bis 0.6.12: 306 beim OEFFNEN,
    /// 350 beim Schliessen.</summary>
    public static bool BriefingklangAlt;

    // ================= R6..R15 — die fehlenden Klaenge (bug-680) =============

    /// <summary><c>--mechanikerklang-aus</c>: R6 stumm.</summary>
    public static bool MechanikerklangAus;
    /// <summary>44 — Mechaniker repariert eine EINHEIT (+1): C <c>0x411E9D</c>, Modus 2
    /// an der Zelle des Ziels. 43 — ein GEBAEUDE (+5): C <c>0x411EF1</c>.</summary>
    public const int MechanikerEinheit = 44, MechanikerGebaeude = 43;

    /// <summary><c>--gleisklang-aus</c>: R7 stumm.</summary>
    public static bool GleisklangAus;
    /// <summary>45 — Gleisstueck repariert: C <c>0x409A04</c> / F <c>0x40992D</c>,
    /// <c>push 0x2D; push 2</c>, Modus 2 an der Zelle der Einheit (x/y = +0/+1).
    /// ⚠ Bisher als »Effekt 0x2D« gelesen — es ist ein KLANG.</summary>
    public const int GleisRepariert = 45;

    /// <summary><c>--depotklang-aus</c>: R8 stumm.</summary>
    public static bool DepotklangAus;
    /// <summary>42 — Gebaeude aus dem Bauauftrag gesetzt (Depot C <c>0x408111</c>,
    /// Generator C <c>0x40837A</c>), Modus 2.</summary>
    public const int BauauftragGesetzt = 42;

    /// <summary><c>--nachschubklang-aus</c>: R9 stumm.</summary>
    public static bool NachschubklangAus;
    /// <summary>75 — Befehl 14/15 an den Nachschubheli, ERSTE Zeile der Behandler
    /// C <c>0x427E30</c>/<c>0x427EE0</c> (Ruf <c>0x427E50</c>/<c>0x427F00</c>),
    /// Modus 1 am Heli (20000 + Platz). 72 — je Abgabe (Tanken C <c>0x424952</c>,
    /// Munition C <c>0x424B8C</c>), Modus 1 am Heli.</summary>
    public const int NachschubBefehl = 75, NachschubAbgabe = 72;

    /// <summary><c>--flugklang-aus</c>: R10 stumm.</summary>
    public static bool FlugklangAus;
    /// <summary>303 — FLUGZEUG (+0x08 &lt; 10) erhaelt die Startbahn im Start (uk 4,
    /// Startuhr +0x12 == 0): C <c>0x423440..0x423467</c>. 304 — HELI (+0x08 ≥ 10) im
    /// Start bei Startuhr == 39: C <c>0x42361F..0x42363C</c>. ⚠ Der Bericht §1c las
    /// 304 als »Hoehe +0x0E erreicht 0x27«; verglichen wird aber <c>cl</c> = Startuhr
    /// +0x12 (0x423475), im Heli-Zweig (0x42347B <c>cmp al,0xA; jae 0x42356A</c>).</summary>
    public const int FlugzeugStartbahn = 303, HeliStartSteigen = 304;

    /// <summary><c>--endklang-aus</c>: R11 stumm.</summary>
    public static bool EndklangAus;
    /// <summary>Missionsende-Fenster C <c>0x441FD0</c>: <c>0x169 − Argument 3</c>
    /// (C 0x442023..0x442038), Modus 0. Rufer C <c>0x41697C</c> bei
    /// <c>word[0x4FA280] == 1</c> (SIEG, @0x41612C) mit Argument 1 → <b>360</b>;
    /// Rufer C <c>0x416C9D</c> bei <c>== 5000</c> (NIEDERLAGE, 0x4D0280) mit
    /// Argument 0 → <b>361</b>.</summary>
    public const int MissionGewonnen = 360, MissionVerloren = 361;

    /// <summary>Wie oft das Missionsende geklungen hat (Pruefstand).</summary>
    public static int EndklangGespielt;

    /// <summary>Der Klang des Missionsendefensters, oertlich (Modus 0).</summary>
    public static void Missionsende(bool gewonnen)
    {
        if (EndklangAus) return;
        EndklangGespielt++;
        Play(gewonnen ? MissionGewonnen : MissionVerloren);
    }

    /// <summary><c>--grabklang-aus</c>: R12 stumm.</summary>
    public static bool GrabklangAus;

    /// <summary><c>--handstimme-aus</c>: R13 stumm.</summary>
    public static bool HandstimmeAus;

    /// <summary>
    /// <b>Die Stimme beim UEBERNEHMEN der Handsteuerung</b> — C <c>0x429760</c>
    /// (Klangruf F <c>0x4289B8</c>), gerufen C <c>0x4337F7</c> im Tastenweg 0x433750 VOR dem
    /// Busbefehl 4 (0x433805); Modus 1 an der Einheit. Selbst gelesen:
    /// <code>
    ///   Auswahl ≥ 8000 (Gebaeude/Flugzeug) → 260
    ///   Gattung 0: +0x28 &gt; 50 ? 174 : 161   (cmp cl,1; sbb; and −13; add 0xAE)
    ///   Gattung 1 (Fussvolk): KEIN Klang (je 0x4297D0 = ret)
    ///   Gattung 3: 174      sonst: 181
    /// </code>
    /// </summary>
    /// <returns>Die Nummer, oder −1 fuer »schweigt«.</returns>
    public static int HandVoice(int subclass, int field28, bool objekt)
    {
        if (objekt) return 260;
        return subclass switch
        {
            0 => field28 > 50 ? 174 : 161,
            1 => -1,
            3 => 174,
            _ => 181,
        };
    }

    /// <summary><c>--waldbrandklang-aus</c>: R14 stumm.</summary>
    public static bool WaldbrandklangAus;
    /// <summary>300 — Waldbrand, im Flammen-Einreiher C <c>0x42E6D0</c>: je
    /// brennender Waldzelle (Zustand &gt; 1), die SICHTBAR ist (byte[0x678B58+…],
    /// 0x42E706) und im Bild liegt, <c>rand()%80 == 0</c> (0x42E81B..0x42E82A),
    /// dann Modus 2 an der Zelle (0x42E844).</summary>
    public const int Waldbrand = 300;

    /// <summary><c>--truemmerklang-aus</c>: R15 stumm.</summary>
    public static bool TruemmerklangAus;

    /// <summary><c>--menuezeilenklang-aus</c>: kein 306 an den Zeilen des
    /// Einheitenmenues (Stand bis 0.6.12).</summary>
    public static bool MenuezeilenklangAus;
}
