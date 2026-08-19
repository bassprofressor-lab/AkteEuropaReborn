namespace AkteEuropaReborn.Audio;

using System.Collections.Generic;
using System.Text.Json;
using Godot;

/// <summary>
/// Which sound belongs to which event — read out of GAME.EXE, not chosen by ear.
///
/// <para><b>How this was found.</b> Every sound in the game goes through one
/// routine, <b>@0x4047e0</b> (thunk 0x40162c): it is guarded by the sound switch
/// <c>byte[0x53c928]</c>, takes the sound number in the first argument and a mode
/// 0..3 in the second (jump table @0x404a18; mode 1 takes an entity slot and
/// plays at its position, which is why it compares against 0x1f40 = 8000).
/// There are <b>111 call sites</b>, and <b>85 of them push a fixed number</b>.
/// Naming them was then a matter of reading the debug line the game prints in
/// the same routine — the game names its own events.</para>
///
/// <para><b>The four modes</b>, read off the jump table @0x404a18 — this is the
/// whole sound API of the game:</para>
/// <code>
///   Sound(u16 number, u8 mode, ...)
///     mode 0  @0x40482b   no position at all       (interface, announcements)
///     mode 1  @0x404834   u16 entity slot          (cmp 0x1f40 = 8000 units)
///     mode 2  @0x404896   u16 x, u16 y             (a map position)
///     mode 3  @0x4048af   u16 index                (projectile array 0x884730,
///                                                   stride 32, x at +0, y at +1)
/// </code>
///
/// <para><b>The band the routine guards.</b> Numbers <b>150..253</b> need a
/// second switch, <c>byte[0x991708]</c> (@0x4047fa: <c>cmp si,0x95 / cmp si,0xfe</c>).
/// The bank's directory delimits exactly that block with holes on both sides:
/// 144..149 empty, <b>150..253 filled, all 104 of them</b>, 254..259 empty. Code
/// and data agree without either knowing about the other. It is the unit speech
/// the options screen switches with "Sprachausgabe der Einheiten
/// ein-/ausschalten", and <see cref="Voice"/> now picks from it the way the
/// game does — see there for the routine and for the check that every number
/// the rule can produce exists.</para>
///
/// <para><b>Four numbers the code names do not exist</b> in the bank: 40, 307,
/// 309 and 399. The loader skips empty slots, so those calls are silent in the
/// original too. They are kept in the list rather than quietly dropped.</para>
///
/// <para>Everything below carries the address it came from. Where no evidence
/// was found the constant is simply absent — no sound is better than the wrong
/// sound in the wrong place.</para>
/// </summary>
public static class GameSounds
{
    // ---- interface ---------------------------------------------------------

    // ⚠ WITHDRAWN: "600 and 601 are the interface click".
    //
    // They are not, and the game said so twice over. Both are played by one
    // four-line wrapper each — @0x487c00 and @0x487c20 — and BOTH wrappers are
    // reached only through a thunk (0x4017d0, 0x401d8e) whose callers all sit in
    // ONE contiguous stretch, 0x498932..0x4a55d1: 89 calls of the first and 62
    // of the second. That stretch is not the window code. It is the campaign's
    // MISSION SCRIPTS — 138 int3-separated blocks whose other calls are named by
    // the game itself ("Cannot add new 'vyroba'", g_robot_class_count,
    // g_buildings_count, "Too many units in 'Space in'", "Cannot add new
    // target", "Can't add built bridge", "SUB:"). And the length settles it:
    // sound 600 is 39,933 bytes at 22050/8/mono = 1.81 seconds. That is a spoken
    // line, not a click — the player heard a woman announcing a completed
    // sub-mission every time he touched a menu row.
    //
    // The original's start menu has NO click at all: its handler @0x447940, its
    // hit test @0x461101 and its draw @0x480430 contain not one call to the
    // sound routine (0x40162c / 0x4047e0), while all 111 call sites in the
    // program were counted. So the menu is silent here as well.

    /// <summary>The first weapon-class shot, 0.29 s — the shortest thing in the
    /// bank that is unambiguously an effect. Used only where a switch has to be
    /// audible while it is being dragged (the volume slider). OURS: the original
    /// plays nothing there.</summary>
    public const int VolumeProbe = 0;

    /// <summary>Refused: "Sie besitzen nicht genuegend Einzelteile." @0x44b6e9,
    /// and the same number again @0x448961 among the hangar and parts
    /// messages.</summary>
    public const int Refused = 140;

    /// <summary>Refused, the second kind: "Kann nicht starten." @0x44b8ad.</summary>
    public const int Refused2 = 141;

    /// <summary>Briefing opens — @0x44d8b9, immediately after the game prints
    /// "End of briefing starts".</summary>
    public const int BriefingStart = 306;

    /// <summary>Briefing closes — @0x44d976, after "End of briefing".</summary>
    public const int BriefingEnd = 350;

    // ---- combat ------------------------------------------------------------

    /// <summary>Infantry hit and destroyed — @0x40d37c, in the hit routine that
    /// prints "Zasah" (Czech for hit) and, right before this call,
    /// "Hit to exploding infantry!!!".</summary>
    public const int InfantryDies = 131;

    /// <summary>A target was acquired — @0x411ab0 in the seeker function
    /// @0x4119d0, whose own trace lines are "Check seeker" and
    /// "Check seeker: new target found".</summary>
    public const int TargetFound = 37;

    /// <summary>An aircraft explodes — @0x425f0f, right after "axplode air";
    /// the same routine plays <see cref="AircraftExplodes2"/> a few
    /// instructions later, so both belong to one explosion.</summary>
    public const int AircraftExplodes = 70;
    public const int AircraftExplodes2 = 138;

    /// <summary>A bomber fires — @0x427416, after "bomber shoot".</summary>
    public const int BomberShoots = 114;

    /// <summary>The explosion, and it is <b>two sounds at once</b>.
    ///
    /// <para>@0x454510 is a four-line routine that takes an x and a y and plays
    /// <b>410 then 400</b>, both in mode 2 (at a map position) with the same
    /// coordinates — not one or the other, both, one after the next. 410 runs
    /// 2.20 s and 400 runs 0.83 s, so it is a bang with a tail under it. The
    /// eight missile handlers @0x452a50..0x452fe4 all call it.</para>
    ///
    /// <para>⚠ Read as "missile away" at first, which was wrong: the routine
    /// takes a POSITION, and the missile branches call it where the missile
    /// arrives, not where it leaves. Each of those branches also plays sound 0
    /// in mode 3 — at the projectile's own place, out of the array at 0x884730
    /// (stride 32) — in the same breath.</para></summary>
    public const int ExplosionLow = 410, ExplosionHigh = 400;

    /// <summary>Both halves of it, the way the original fires them.</summary>
    public static void Explosion()
    {
        Play(ExplosionLow);
        Play(ExplosionHigh);
    }

    /// <summary>Dieselbe Explosion, aber an ihrer Stelle auf der Karte — siehe
    /// <see cref="SoundBankPlayer.DistanceDb"/>.</summary>
    public static void Explosion(float col, float row)
    {
        PlayAt(ExplosionLow, col, row);
        PlayAt(ExplosionHigh, col, row);
    }

    // ---- buildings and base ------------------------------------------------

    /// <summary>Your building is being taken — @0x43cc73, in the routine that
    /// prints "Ihre Basis wird besetzt", "Ihre Waffenfabrik wird besetzt" and
    /// the rest of that family.</summary>
    public const int BuildingCaptured = 132;

    /// <summary>Mining — @0x43e6cd, between the routine's own "mining 3" and
    /// "mining 4".</summary>
    public const int Mining = 128;

    /// <summary>A building is being enlarged — @0x43e794, immediately after
    /// "enlarging". Which of the remake's two jobs that is (Lagerausbau or
    /// Produktionserweiterung) is OURS: "enlarging" is read as the storage one
    /// and "upgrading" as the production one.</summary>
    public const int Enlarging = 130;

    /// <summary>A building is being upgraded — @0x43e837, immediately after
    /// "upgrading".</summary>
    public const int Upgrading = 129;

    /// <summary><b>Repair has no sound</b>, and that is a finding rather than a
    /// gap: @0x43e196 plays one after "mining 3", after "enlarging" and after
    /// "upgrading", and there is no call at all between its "repair" and its
    /// "enlarging". The original repairs silently.</summary>
    public const int RepairIsSilent = -1;

    /// <summary>Research reported — @0x4ab41b, right after "Nachricht des
    /// FORSCHUNGSLABORS:" and in the routine that prints "Neue Waffe
    /// erfunden".</summary>
    public const int ResearchDone = 136;

    /// <summary>Mirror switched on — @0x43a9b9, inside @0x43a978, the routine
    /// already known as the Spiegelung (it sets UKOL to 0x16).</summary>
    public const int MirrorOn = 59;

    // ---- the spoken word ----------------------------------------------------

    /// <summary>The narrated mission briefing: <b>500 + mission number</b>.
    ///
    /// <para><b>Proven against the text, not by ear.</b> The bank holds a block
    /// of exactly <b>33 sounds at 501..533</b>, and the campaign has 33
    /// missions. Holding each sound's length against the character count of the
    /// matching record in BRIEFG.TXT gives <b>r = 0.984 over all 33</b>, at
    /// <b>17.2 to 22.7 characters per second</b> — a narrow band around 19.8,
    /// which is what German read aloud sounds like. Nothing else in the file
    /// lines up that way.</para>
    ///
    /// <para>The call site agrees: @0x486603 does <c>mov eax,[0x8c34f4];
    /// add ax,0x1f4</c> — 500 plus a counter — gated by
    /// <c>cmp word[0x8c3ccc], 0x19</c>, a screen state, in the routine whose
    /// loader also reads MAP.DAT and SYMBOL.DAT, i.e. the mission screen.</para>
    /// </summary>
    public const int BriefingVoiceBase = 500;

    public static int BriefingVoice(int mission) => BriefingVoiceBase + mission;

    /// <summary>The spoken help texts: <b>1000 + the number of the text in
    /// HELPG.TXT</b>, switched by "Hilfe-Sprache" (<c>byte[0x8934c4]</c>,
    /// checked @0x44330c right before the call @0x443323 that adds 0x3e8).
    ///
    /// <para>The index correspondence is measured: over the 222 texts that have
    /// a sound the lengths correlate at <b>r = 0.60</b>, and shifting the
    /// pairing by one in either direction collapses it to <b>-0.08 / -0.04</b>.
    /// So the pairing is right. <b>It is not a reading of the text, though</b> —
    /// the median comes out at 31 characters per second against the briefings'
    /// 20, so the spoken help says a shorter version. That is stated rather than
    /// smoothed over.</para></summary>
    public const int HelpVoiceBase = 1000;

    /// <summary>The band 150..253, all 104 of them, which the sound routine only
    /// plays when <c>byte[0x991708]</c> is set (@0x4047fa:
    /// <c>cmp si,0x95 / cmp si,0xfe</c>) — the options screen's <b>"Meldungen"</b>
    /// switch.
    ///
    /// <para><b>They are the unit voices, keyed by chassis</b>, which also
    /// settles that "Meldungen" and the help line's "Sprachausgabe der
    /// Einheiten" are the same switch. The routine @0x429290 reads it out:</para>
    /// <code>
    ///   si = word[0x4fa0c8]                  ; what is selected, 0xFFFF = nothing
    ///   si &gt;= 0x1f40 (8000)                  ; not a unit -> di = 0xb8
    ///   entity +0x28 &gt; 0x32 (50)             ; the fuel tank: low fuel has its own set
    ///   switch (entity +0x0a)                ; the kind
    ///     0 -> di 0x98 bp 0x9c  (or the 0xa5/0xa9 set when the tank is low)
    ///     1 -> switch ((entity +0x0b &gt;&gt; 1) - 1)   ; SPODEK, the chassis, 11 ways
    ///     3 -> di 0xa5 bp 0xa9
    ///     else di 0xb1
    ///   bp += rand % 3 ; Sound(bp, 1, si)    ; three variants, played AT the unit
    /// </code>
    /// <para>The eleven chassis sets, as <c>di</c> / <c>bp</c>: default 189/193,
    /// then 201/205, 213/217, 224/228, 235/239, 246/—, 250/—, 165/169. Two
    /// groups of three or four per chassis, which is where the band's 104 go.
    /// </para>
    ///
    /// <para>The eleven-way jump table @0x4293ec maps the chassis index onto
    /// eight branches: 0 and 2 share one, 4..7 share the default, and 9 and 10
    /// have no <c>bp</c> group at all. Which of the two groups is heard is a
    /// throw: <b>one time in three</b> the <c>bp</c> line, otherwise
    /// <c>di</c> — @0x429393 draws `rand % 3` and only a zero takes the
    /// <c>bp</c> path.</para>
    /// </summary>
    public const int AnnounceFirst = 150, AnnounceLast = 253;

    /// <summary>The two groups a unit speaks from, and how many variants each
    /// holds. <c>Bp</c> 0 means the chassis has no second group.</summary>
    private readonly record struct VoiceSet(int Di, int DiCount, int Bp);

    /// <summary>The branches of @0x429290, in the order the jump table
    /// @0x4293ec reaches them. Index = (chassis &gt;&gt; 1) - 1.</summary>
    private static readonly VoiceSet[] ByChassis =
    {
        new(201, 2, 205),   // 0  -> 0x42934a
        new(213, 2, 217),   // 1  -> 0x429354
        new(201, 2, 205),   // 2  -> 0x42934a, same branch as 0
        new(224, 2, 228),   // 3  -> 0x42935e
        new(189, 2, 193),   // 4  -> the default @0x429340
        new(189, 2, 193),   // 5
        new(189, 2, 193),   // 6
        new(189, 2, 193),   // 7
        new(235, 2, 239),   // 8  -> 0x429368
        new(246, 2, 0),     // 9  -> 0x429372, bp cleared
        new(250, 3, 0),     // 10 -> 0x42937b, bl = 3
    };

    /// <summary>What a unit says when it is picked, straight off @0x429290.
    /// Returns -1 when the routine would say nothing.
    ///
    /// <para><b>Checked against the data:</b> run over the <b>2863 units on all
    /// the maps</b>, the rule can reach <b>36 different sound numbers, every one
    /// of them present in the bank and every one inside the band 150..253</b>.
    /// A wrong offset or a wrong shift would have produced numbers outside it or
    /// numbers the bank has not got.</para></summary>
    /// <param name="subclass">record +0x0a</param>
    /// <param name="chassis">record +0x0b</param>
    /// <param name="field28">record +0x28 — <b>not</b> the fuel tank (that is
    /// +0x2e): it is 0 on 2847 of the 2863 units the maps carry, so a runtime
    /// field rather than a stat, read beside +0x27 by the damage arithmetic
    /// @0x40cd90. The routine compares it with 50. Carried unnamed.</param>
    public static int Voice(int subclass, int chassis, int field28, System.Random? rng = null)
    {
        int Roll(int n) => n <= 1 ? 0 : (rng?.Next(n) ?? (int)(GD.Randi() % (uint)n));

        VoiceSet v;
        switch (subclass)
        {
            case 0:
                // `cmp al,0x32; seta cl` on +0x28, then the branch at 0x429319.
                // On a freshly loaded map that byte is 0 for all but sixteen
                // units, so in practice this is the 152/156 set.
                v = field28 > 50 ? new VoiceSet(165, 2, 169) : new VoiceSet(152, 2, 156);
                break;
            case 1:
                // `cmp eax,0xa; ja 0x429340` — and eax is UNSIGNED, so a chassis
                // of 0 or 1 (index -1) takes the default just as the ship hulls
                // 60..101 (index 29..49) do
                int i = (chassis >> 1) - 1;
                v = i >= 0 && i < ByChassis.Length ? ByChassis[i] : new VoiceSet(189, 2, 193);
                break;
            case 3:
                v = new VoiceSet(165, 2, 169);
                break;
            default:
                v = new VoiceSet(177, 1, 0);
                break;
        }

        // @0x42938e: no second group, or two throws in three, and it is a di line
        if (v.Bp == 0 || Roll(3) != 0) return v.Di + Roll(v.DiCount);
        return v.Bp + Roll(3);
    }

    /// <summary>Die Zweige von @0x429480, in der Reihenfolge der Sprungtafel
    /// @0x4295D0 mit der Bytetafel @0x4295EC. Index = (Fahrwerk &gt;&gt; 1) − 1.
    /// ⚠ Die Bytetafel ist [0,1,2,3,6,6,6,6,4,5,5] — Index 9 und 10 zeigen auf
    /// den Eintrag 5, und der ist das <c>ret</c> bei 0x4295C8: diese zwei
    /// Fahrwerke sagen beim Befehl <b>nichts</b>.</summary>
    private static readonly VoiceSet?[] OrderByChassis =
    {
        new VoiceSet(203, 2, 205),   // 0  -> 0x429542
        new VoiceSet(215, 2, 217),   // 1  -> 0x42954C
        new VoiceSet(203, 2, 205),   // 2  -> 0x429542, wie 0
        new VoiceSet(226, 2, 228),   // 3  -> 0x429556
        new VoiceSet(191, 2, 193),   // 4  -> 0x429538, der Standard
        new VoiceSet(191, 2, 193),   // 5
        new VoiceSet(191, 2, 193),   // 6
        new VoiceSet(191, 2, 193),   // 7
        new VoiceSet(237, 2, 239),   // 8  -> 0x429560
        null,                        // 9  -> 0x4295C8, das ret: schweigt
        null,                        // 10 -> ebenso
    };

    /// <summary>Was ein Gebäude oder ein Flugzeugplatz auf einen Befehl sagt:
    /// eine einzige Zeile, <c>mov si, 0xB9</c> @0x4294B9.</summary>
    public const int OrderVoiceBuilding = 185;

    /// <summary>
    /// <b>WAS EINE EINHEIT AUF EINEN BEFEHL SAGT</b> — Routine <b>@0x429480</b>.
    ///
    /// <para>Gemeldet: »alle einheiten haben sounds die abgespielt werden wenn
    /// man diese wohin bewegt, meistens aber jede klasse für sich einen sound,
    /// also schiffe haben alle einen gemeinsamen, fahrzeuge auch usw«. Genau so
    /// ist es gebaut, und es ist derselbe Bau wie beim Anwählen
    /// (<see cref="Voice"/>) — nur mit anderen Nummern.</para>
    ///
    /// <para><b>Wo sie hängt.</b> Die Eingaberoutine <c>0x437060</c> ruft sie
    /// VIERMAL (0x437458, 0x43746A, 0x437581, 0x4375A8), jedes Mal gleich
    /// nachdem der Befehl abgesetzt ist — bei 0x437477 steht der Busbefehl 11
    /// daneben. Den Anwählklang ruft dieselbe Routine nur EINMAL (0x437985).
    /// </para>
    ///
    /// <para><b>Die Zahlen liegen zwei über denen des Anwählens</b>: 152→154,
    /// 189→191, 201→203, 213→215, 224→226, 235→237, 165→167. Die zweite Gruppe
    /// (<c>bp</c>) ist dieselbe. Im Klangvorrat liegen also je Einheitenart
    /// zwei Anwählzeilen, zwei Befehlszeilen und drei gemeinsame.</para>
    ///
    /// <para>Der Würfel ist derselbe wie beim Anwählen (@0x429572): gibt es
    /// eine zweite Gruppe, kommt sie in einem von drei Fällen, sonst die
    /// erste.</para>
    ///
    /// <para>⚠ Ein <b>Gebäude oder Flugzeugplatz</b> (Auswahl ≥ 8000 und nicht
    /// die Gruppe) sagt <see cref="OrderVoiceBuilding"/>, eine einzige Zeile.
    /// Bei einer <b>Gruppe</b> holt sich das Original ein Mitglied
    /// (<c>call 0x4018BB</c> @0x4294A6) und lässt DIESES sprechen — nicht alle.
    /// </para>
    /// </summary>
    /// <param name="subclass">Satz +0x0a</param>
    /// <param name="chassis">Satz +0x0b</param>
    /// <param name="field28">Satz +0x28, dasselbe Feld wie bei
    /// <see cref="Voice"/></param>
    public static int OrderVoice(int subclass, int chassis, int field28,
                                 System.Random? rng = null)
    {
        int Roll(int n) => n <= 1 ? 0 : (rng?.Next(n) ?? (int)(GD.Randi() % (uint)n));

        VoiceSet v;
        switch (subclass)
        {
            case 0:
                // `cmp al,0x32; seta cl` @0x4294E3, geprueft bei 0x429509
                v = field28 > 50 ? new VoiceSet(167, 2, 169) : new VoiceSet(154, 2, 156);
                break;
            case 1:
                int i = (chassis >> 1) - 1;
                if (i >= 0 && i < OrderByChassis.Length)
                {
                    var w = OrderByChassis[i];
                    if (w == null) return -1;      // die zwei stummen Fahrwerke
                    v = w.Value;
                }
                else
                {
                    v = new VoiceSet(191, 2, 193);
                }
                break;
            case 3:
                v = new VoiceSet(167, 2, 169);
                break;
            default:
                // `mov si,0xB2; mov bl,1; xor bp,bp` @0x4294FE
                v = new VoiceSet(178, 1, 0);
                break;
        }

        if (v.Bp == 0 || Roll(3) != 0) return v.Di + Roll(v.DiCount);
        return v.Bp + Roll(3);
    }

    /// <summary>What a unit says when it is hit — routine <b>@0x4297f0</b>,
    /// called twice from the hit routine @0x40cc41, the second time right after
    /// it prints "Hit to exploding infantry!!!".
    ///
    /// <para>Same shape as <see cref="Voice"/> but a single line, no variants,
    /// and <b>mode 0</b> — no position at all, so it is heard wherever the
    /// camera is. Its chassis table @0x429894 is its own: 210, 221, 210, 232,
    /// 198 for 4..7, 243, 248, 253, and 198 for anything out of range.</para>
    ///
    /// <para><b>It is rate-limited by the game itself</b>, which is what keeps a
    /// battle from turning into a chorus: both call sites only speak when the
    /// clock at 0x4fa240 has passed 0x4f5aec, and then set that to
    /// <c>clock + 250 + rand % 100</c> (@0x40ce1c and @0x40d158). See
    /// <see cref="HitVoiceGapSec"/> for the one part of that which is ours.</para>
    /// </summary>
    public static int HitVoice(int subclass, int chassis, int field28)
    {
        switch (subclass)
        {
            case 0: return field28 > 50 ? 175 : 162;   // `sbb`/`and -13` on +0x28
            case 3: return 175;
            case 1:
                int i = (chassis >> 1) - 1;
                return i switch
                {
                    0 or 2 => 210, 1 => 221, 3 => 232,
                    8 => 243, 9 => 248, 10 => 253,
                    _ => 198,                           // 4..7 and out of range
                };
            default: return 182;
        }
    }

    /// <summary>How long the hit line stays quiet, in seconds. The COUNT is the
    /// game's — 250 plus a throw of 100 on the clock at 0x4fa240 — and only the
    /// length of a tick is ours: at the 25 frames a second the movies run at,
    /// that is 10 to 14 seconds, which is also what it sounds like.</summary>
    public static float HitVoiceGapSec(System.Random? rng = null)
        => (250 + (rng?.Next(100) ?? (int)(GD.Randi() % 100))) / 25f;

    // ---- the fire sound of a weapon ----------------------------------------

    private static int[]? _fire;
    private static bool _tried;

    /// <summary>Liest <c>Sound/weapon_sounds.json</c> einmal.</summary>
    private static void LoadWeaponSounds()
    {
        if (_tried) return;
        _tried = true;
        string path = Core.Content.Path("Sound/weapon_sounds.json");
        if (!FileAccess.FileExists(path)) return;
        try
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            using var doc = JsonDocument.Parse(f.GetAsText());
            _fire = Column(doc, "base");
            _tempo = Column(doc, "tempo");
            _flug = Column(doc, "flug");
            _einschlag = Column(doc, "einschlag");
            _lafette = Column(doc, "lafette");
            _zwilling = Column(doc, "zwilling");
        }
        catch (System.Exception e) { GD.PrintErr("Ton: weapon_sounds.json — " + e.Message); }

        static int[]? Column(JsonDocument doc, string name)
        {
            if (!doc.RootElement.TryGetProperty(name, out var arr)) return null;
            var list = new List<int>();
            foreach (var e in arr.EnumerateArray()) list.Add(e.GetInt32());
            return list.ToArray();
        }
    }


    /// <summary>The base sound for a weapon's sound class, or -1.
    ///
    /// The class is the component's stats byte at +0x1c, the base comes from the
    /// table at 0x4f98f2 (stride 22) — both exported to
    /// <c>Sound/weapon_sounds.json</c>. See
    /// <see cref="Import.ExeTables.FireSoundTable"/> for the disassembly.</summary>
    private static int[]? _tempo, _flug, _einschlag, _lafette, _zwilling;

    private static int Feld(int[]? sp, int art)
        => sp != null && art >= 0 && art < sp.Length ? sp[art] : -1;

    /// <summary>
    /// <b>Was ein Geschoss dieser Art ausmacht</b> — alles aus derselben Zeile
    /// wie der Schussklang, Tafel <c>0x4F98E8</c>. Siehe
    /// <see cref="Import.ExeTables.ProjectileTable"/>.
    ///
    /// <para>Die »Klangklasse« aus dem Statssatz (+0x1C) ist in Wahrheit die
    /// <b>Geschossart</b>; dass sie bisher nur einen Klang gewaehlt hat, lag
    /// daran, dass wir nur eine Spalte gelesen hatten.</para>
    /// </summary>
    public static int ProjectileSpeed(int art) { LoadWeaponSounds(); return Feld(_tempo, art); }

    /// <summary>Bildfolge des Fluges, oder −1 / 30000 wenn es keine gibt.</summary>
    public static int FlightSequence(int art) { LoadWeaponSounds(); return Feld(_flug, art); }

    /// <summary>Bildfolge des Einschlags.</summary>
    public static int ImpactSequence(int art) { LoadWeaponSounds(); return Feld(_einschlag, art); }

    /// <summary>⚠ Der <b>Aufschlag auf die Lafettensuche</b> (10..30), Feld
    /// +0x14. Was der Grundwert bedeutet, ist nicht gelesen — deshalb ist das
    /// hier KEINE »Hoehe« und schon gar keine Rohrlaenge. Wird bisher von
    /// nichts benutzt und steht nur da, damit die Zahl nicht wieder verloren
    /// geht. Belegstelle in <see cref="Import.ExeTables.ProjectileTable"/>.
    /// </summary>
    public static int MountBias(int art) { LoadWeaponSounds(); return Feld(_lafette, art); }

    /// <summary>Seitlicher Versatz der ZWILLINGSLAFETTE. Ist er ungleich 0,
    /// feuert das Original ZWEI Geschosse nebeneinander (0x40C35E und
    /// 0x40C449).</summary>
    public static int TwinOffset(int art) { LoadWeaponSounds(); return Feld(_zwilling, art); }

    public static int FireBase(int soundClass)
    {
        LoadWeaponSounds();
        if (_fire == null || soundClass < 0 || soundClass >= _fire.Length) return -1;
        return _fire[soundClass];
    }

    /// <summary>Plays the shot of a weapon component. The original picks the
    /// base or the base plus one at random, so two shots never sound the same;
    /// that is its behaviour, not our garnish.
    ///
    /// <para><paramref name="col"/>/<paramref name="row"/> sind die Zelle, in der
    /// geschossen wird — das Original übergibt seiner Klangroutine immer eine
    /// Stelle auf der Karte und dämpft danach, siehe
    /// <see cref="SoundBankPlayer.DistanceDb"/>. Ohne Zelle bleibt es beim alten
    /// Verhalten (volle Lautstärke), damit ein Prüfstand ohne Karte weiter
    /// klingt.</para></summary>
    public static void Fire(int weaponRow, System.Random? rng = null,
                            float col = float.NaN, float row = float.NaN)
    {
        int cls = Simulation.DesignMath.SoundClass(weaponRow);
        int b = FireBase(cls);
        if (b < 0) return;
        int pick = b + ((rng?.Next(2) ?? (int)(GD.Randi() & 1)));
        PlayAt(pick, col, row);
    }

    /// <summary>Shorthand for the events above.</summary>
    public static void Play(int slot) => SoundBankPlayer.Play(slot);

    /// <summary>Ein Klang, der auf der Karte entsteht — mit Zelle gedämpft, ohne
    /// Zelle wie bisher.</summary>
    public static void PlayAt(int slot, float col, float row)
    {
        if (float.IsNaN(col) || float.IsNaN(row)) SoundBankPlayer.Play(slot);
        else SoundBankPlayer.PlayAt(slot, col, row);
    }

    // ---- no click on every button -------------------------------------------
    //
    // There used to be a HookButtons(SceneTree) here that gave every BaseButton
    // in the game sound 600 on press. It rested on the reading withdrawn at the
    // top of this file, and what it actually did was announce a finished
    // sub-mission whenever the player clicked anything. It is gone rather than
    // repointed: the original's menu makes no sound, and inventing one would be
    // ours without saying so.
}
