namespace AkteEuropaReborn.Simulation;

using System;
using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// Walkability grid + A* over a legacy CWM map.
///
/// THE TERRAIN IS THE GAME'S OWN. It comes out of <c>Can_go</c> @0x4055D0 — the
/// routine every movement question goes through, named by its own debug line
/// "can go, number:" @0x4f6754. It takes a unit slot and a direction (the 8-way
/// table @0x4f5af0), and returns <b>0 no, 1 yes but someone has to give way,
/// 2 free</b>.
///
/// It asks exactly one map, the <b>imap</b> at 0xbdea80 (the game's own name,
/// `imap[rx+px][ry+py]:` @0x4f66b4) = sec6 of the map file, 256x256 u16 column
/// major. Three of its values are ground:
///
///   0xFFFE  free      — everyone
///   0xFFFD  rough     — foot yes, hover only where the tile's flag byte is 0,
///                       vehicles never
///   0xFFFC  water     — ships and hover only
///   0xFFFF, >= 14000  — the static object handles; they walk into the routine's
///                       own `nothing` path @0x405586 and block everyone
///
/// It branches on entity <b>+0x0a</b> (jump table @0x40678c, six cases): case 0
/// is the ordinary unit and splits again on the chassis <b>+0x0b</b> — 7 hover
/// (@0x4056b9), 0x11 walker (@0x405973), anything else "normal chassis"
/// (@0x405bd7) — and <b>case 4 is the ship</b> (@0x406669), whose only terrain
/// test is 0xFFFC.
///
/// ⚠ TWO EARLIER READINGS WITHDRAWN.
/// (1) "sec2 is the passability". It is not: Can_go never touches sec2. The
///     zones stay zones (the Z overlay), the passability comes from the imap.
///     On NET07 sec2 class 0 covers 7115 cells where the real water is 6452 —
///     that difference is why land units used to drive into the sea.
/// (2) "a cell with a prop on it blocks". It does not. Most object cells are
///     0xFFFE, that is free (map_02: 498, map_14: 512, NET07: 312) — bridges
///     and roads among them. That is why bridges used to be impassable.
///
/// OURS, and marked as such where it shows: <see cref="TerrainCost"/> (the
/// original has no per-cell movement cost in this routine), the climb limit
/// <see cref="MaxClimb"/>, and the ground under an occupied cell, which the imap
/// cannot say — see CwmData.Terrain.
/// </summary>
public sealed class NavGrid
{
    /// <summary>How a unit moves. The numbers are ours; what selects them is
    /// the game's: entity +0x0a == 4 or 5 is a ship, otherwise the chassis at
    /// +0x0b picks hover (7), walker (0x11) or the ordinary vehicle.</summary>
    public enum MoveClass { Vehicle = 0, Walker = 1, Hover = 2, Ship = 3 }

    /// <summary>The four ground classes Can_go distinguishes.</summary>
    public enum Ground : byte { Free = 0, Rough = 1, Water = 2, Blocked = 3 }

    /// <summary>Chassis (+0x0b) that Can_go singles out by name.</summary>
    public const int ChassisHover = 7;
    public const int ChassisWalker = 0x11;

    /// <summary>Entity +0x0a values that take the ship branch (@0x406669,
    /// @0x40671b) — both test the imap for 0xFFFC and nothing else.</summary>
    public static bool ArtIsShip(int art) => art is 4 or 5;

    /// <summary>The move class of a unit, the way Can_go picks its branch.</summary>
    public static MoveClass ClassOf(int art, int chassis)
        => ArtIsShip(art) ? MoveClass.Ship
         : chassis == ChassisHover ? MoveClass.Hover
         : chassis == ChassisWalker ? MoveClass.Walker
         : MoveClass.Vehicle;

    /// <summary>Ground tile codes 0..7 are the animated water cycle. Only used
    /// when a map was exported before the terrain block existed.</summary>
    public const int WaterCodeMax = 7;

    /// <summary>Elevation step (in levels) a ground unit can no longer climb.
    /// OURS — the original has no such test in Can_go.</summary>
    public const int MaxClimb = 3;

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>True once the real imap terrain has been laid over the fallback.</summary>
    public bool HasTerrain { get; private set; }

    /// <summary>Cells whose ground the importer had to infer from their occupant.</summary>
    public int Inferred { get; private set; }

    private byte[] _ground = Array.Empty<byte>();     // <see cref="Ground"/>
    private byte[] _flag = Array.Empty<byte>();       // tile flag byte (slope 0..4)
    private byte[] _elev = Array.Empty<byte>();
    private int[] _occupant = Array.Empty<int>();     // entity index or -1
    private bool[] _crushable = Array.Empty<bool>();  // occupant is a foot soldier
    // ⚠ Der Belegende ist ein Gebäude oder ein Gegenstand, also etwas, das nie
    // weiterfährt. Ohne diese Unterscheidung wäre jede Wand ein »der fährt
    // gleich weiter« — und eine Einheit wartete davor bis zum Missionsende.
    // Siehe Ask(): das Original liest denselben Unterschied am imap-Wert
    // (10000..13999 Einheit, ab 14000 fest).
    private bool[] _immobile = Array.Empty<bool>();

    public bool InBounds(int c, int r) => c >= 0 && r >= 0 && c < Width && r < Height;

    private int Idx(int c, int r) => r * Width + c;

    public Ground GroundAt(int c, int r) => InBounds(c, r) ? (Ground)_ground[Idx(c, r)] : Ground.Blocked;

    public int FlagAt(int c, int r) => InBounds(c, r) ? _flag[Idx(c, r)] : 0;

    public int ElevAt(int c, int r) => InBounds(c, r) ? _elev[Idx(c, r)] : 0;

    public int OccupantAt(int c, int r) => InBounds(c, r) ? _occupant[Idx(c, r)] : -1;

    // ========================================================================
    //  DER RUMPF — ein Schiff ist mehr als eine Zelle
    // ========================================================================

    /// <summary>
    /// Wie viele Zellen je Kante der Rumpf dieser Gattung hat — <b>GELESEN</b>.
    ///
    /// <para><c>Can_go</c> @0x4055D0 verteilt auf <c>byte[+0x0A]</c> (die Zeile,
    /// die das Spiel selbst <c>»unit type:«</c> nennt, Formatzeile @0x4F6734)
    /// ueber die Sprungtafel @0x40678C. Zwei der sechs Faelle sind Schiffe, und
    /// sie pruefen NICHT eine Zelle:</para>
    /// <code>
    ///   Gattung 4  @0x406669   VIER Zellen:     (c,r) (c+1,r) (c,r+1) (c+1,r+1)
    ///   Gattung 5  @0x40671B   SECHZEHN Zellen: zwei Schleifen zu je vier
    /// </code>
    /// <para>Die Karten bestaetigen es unabhaengig: ueber die 29 gelieferten
    /// Karten tragen <b>163 von 163</b> aufloesbaren Einheiten der Gattung 4
    /// einen 2x2-Grundriss und <b>32 von 32</b> der Gattung 5 einen 4x4; und
    /// <b>193 von 210</b> Schiffen liegen mit dem GANZEN Rumpf auf Wasser.
    /// ⚠ Gattung <b>2</b> zeigt in derselben Tafel auf den Fehlerzweig
    /// @0x40569D und kommt in keiner Karte vor (0 von 4474).</para>
    ///
    /// <para>Alles andere ist 1 — das Original prueft dort genau eine Zelle.</para>
    /// </summary>
    public static int HullSide(int art) => art == 5 ? 4 : art == 4 ? 2 : 1;

    /// <summary>
    /// Die Kantenlaenge je Einheit. <b>Hier und nicht beim Aufrufer</b>, und das
    /// ist der Kern dieser Aenderung: es gibt elf Stempel- und fuenfzehn
    /// Loeschstellen. Reichte jede von ihnen den Rumpf selbst herein, waere die
    /// erste vergessene Stelle ein Schiff, das halb belegt bleibt — und der
    /// Fehler faende sich nie wieder. So kennt <see cref="SetOccupant"/> und
    /// <see cref="ClearOccupant"/> ihn von selbst, und beide stempeln
    /// zwangslaeufig dieselbe Flaeche.
    /// </summary>
    private readonly Dictionary<int, int> _hull = new();

    /// <summary>Den Rumpf einer Einheit hinterlegen. 1 loescht den Eintrag.</summary>
    public void SetHull(int entity, int side)
    {
        if (side <= 1) _hull.Remove(entity);
        else _hull[entity] = side;
    }

    /// <summary>Die hinterlegte Kantenlaenge, sonst 1.</summary>
    public int HullOf(int entity)
        => entity >= 0 && _hull.TryGetValue(entity, out int s) ? s : 1;

    /// <summary>Alle Ruempfe vergessen — gehoert zu <see cref="ClearOccupants"/>,
    /// sonst traegt ein neu geladenes Spiel die Ruempfe des alten.</summary>
    public void ClearHulls()
    {
        _hull.Clear();
        // ⚠ Die Ankerbuchhaltung geht MIT: sie fuehrt Einheitennummern, und die
        // sind auf der naechsten Karte andere. Ein stehengebliebener Anker
        // waere ein Phantom, das keiner Einheit mehr gehoert.
        _anker.Clear();
        _stempelArt.Clear();
    }

    /// <summary>Terrain a move class may stand on, ignoring occupants. This IS
    /// the table Can_go implements; the hover line's flag test is its own
    /// (@0x4057b5 calls the tile-flag accessor @0x41d110 and lets it pass only
    /// when the byte is 0).</summary>
    public bool CanEnter(int c, int r, MoveClass mc)
    {
        if (!InBounds(c, r)) return false;
        return (Ground)_ground[Idx(c, r)] switch
        {
            Ground.Free => mc != MoveClass.Ship,
            Ground.Rough => mc == MoveClass.Walker ||
                            (mc == MoveClass.Hover && _flag[Idx(c, r)] == 0),
            Ground.Water => mc is MoveClass.Ship or MoveClass.Hover,
            _ => false,
        };
    }

    /// <summary>Kept under its old name because half the caller set asks it this
    /// way; it is <see cref="CanEnter"/>.</summary>
    public bool IsWalkable(int c, int r, MoveClass mc = MoveClass.Vehicle) => CanEnter(c, r, mc);

    /// <summary>
    /// Free to move into: the terrain allows it and nothing blocks the cell.
    ///
    /// A foot soldier does NOT block a cell. The original says so twice over,
    /// and in both directions: `pratelska_infa` @0x433fe0 lets a unit through an
    /// infantry cell when all nine of its men are friendly, and `prejet` — the
    /// game's own word for running over — @0x412980 lets it through when none of
    /// them is, killing them on the way (@0x412a50 then stamps the cell 0xFFFE).
    /// Either way the cell is passable, which is why infantry could always be
    /// driven over and why it must not stop a tank here.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Prueft den GANZEN Rumpf des Bewegers</b>, wenn fuer ihn einer
    /// hinterlegt ist (<see cref="SetHull"/>). Genau das tut das Original:
    /// <c>Can_go</c> @0x4055D0 laeuft fuer Gattung 4 ueber vier und fuer
    /// Gattung 5 ueber sechzehn Zellen und laesst den Schritt nur durch, wenn
    /// ALLE tragen. Ohne diese Schleife passt ein Schlachtschiff durch eine
    /// Luecke von einer Zelle, und zwei Schiffe stehen ineinander.
    /// </remarks>
    public bool IsFree(int c, int r, MoveClass mc = MoveClass.Vehicle, int mover = -1)
        => Ask(c, r, mc, mover) == Step.Free;

    /// <summary>
    /// ⭐⭐ <b>WIE DIE WEGSUCHE DIE KARTE SIEHT</b> — und das ist NICHT
    /// <see cref="IsFree"/>.
    ///
    /// <para><b>Gelesen, Tafel BB.1, Bewegungsart 0</b> (<c>0x4D118D</c>, das
    /// 1×1-Bodenfahrzeug). Der Kartenbauer schreibt je Zelle:</para>
    /// <code>
    ///   0 (frei)     wenn 0xFFFE                    leeres Gelaende
    ///                oder &lt;8000 und Unterklasse 0    eine Infanteriezelle
    ///                oder 10000..13999 mit +1 == 0  EINE EINHEIT STEHT DA
    ///   1 (weich)    wenn 0xFFFD                    rau
    ///   2 (hart)     sonst — darunter >= 14000      Gebaeude, Festes
    /// </code>
    ///
    /// <para>⭐ <b>Die Zeile, um die es geht, ist die dritte:</b> eine Zelle mit
    /// einer EINHEIT darin ist für die Planung <b>frei</b>. Das Original plant
    /// mitten durch stehende Einheiten hindurch und wartet erst beim FAHREN —
    /// dort antwortet <c>Can_go</c> mit 1 (<see cref="Step.GiveWay"/>), und der
    /// Fahrer behält seinen Weg und lässt den anderen vorbei. Nur Festes sperrt
    /// schon die Planung.</para>
    ///
    /// <para>⚠⚠ <b>Warum das bis zum 23.08.2026 falsch war und was es kostete:</b>
    /// wir haben die Suchkarte aus <see cref="IsFree"/> gebaut, und das sperrt
    /// jede besetzte Zelle hart. Gemessen mit <c>--nav-flut</c> auf map_01:
    /// vom Startpanzer aus sind <b>921</b> Zellen erreichbar, aber nur
    /// <b>334</b>, wenn man die Karte so ansieht wie die Wegsuche. <b>587
    /// Zellen — zwei Drittel — gaben die stehenden Einheiten weg.</b> Bei einem
    /// Gruppenbefehl sperren sich die eigenen Einheiten dadurch gegenseitig die
    /// Wege.</para>
    ///
    /// <para>⚠⚠ <b>UND DAS HIER IST DER ZWEITE ANLAUF.</b> Am 16.08.2026 stand
    /// schon einmal <c>Ask(...) != Blocked</c> in <c>CanStep</c>, und es wurde
    /// am selben Tag zurückgezogen: gemessen auf map_NET07 kamen <b>17 statt
    /// 32</b> Einheiten an. Die Wege liefen durch die Pulks, und die Einheiten
    /// standen wartend darin. Zwei Dinge sind seither anders:
    /// <list type="number">
    ///   <item>Damals war es <b>erschlossen</b> (»das Original fährt hin und
    ///   wartet«), heute ist es <b>gelesen</b> — Tafel BB.1 oben, aus dem
    ///   Kartenbauer selbst.</item>
    ///   <item>Damals fehlte die andere Hälfte. Das Original plant nicht nur
    ///   durch, es plant auch alle <see cref="UrWegLaenge"/> Schritte NEU. Ohne
    ///   die Neuplanung bleibt ein Weg durch einen Pulk für immer ein Weg durch
    ///   einen Pulk. <b>Beides zusammen oder gar nicht.</b></item>
    /// </list></para>
    ///
    /// <para>⚠ <see cref="IsFree"/> bleibt, wo es hingehört: beim SCHRITT.
    /// Planen und fahren sind zwei verschiedene Fragen, und das war der ganze
    /// Denkfehler.</para>
    /// </summary>
    public bool PfadOffen(int c, int r, MoveClass mc = MoveClass.Vehicle, int mover = -1)
    {
        int side = HullOf(mover);
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                int cc = c + dx, rr = r + dy;
                if (!CanEnter(cc, rr, mc)) return false;
                int i = Idx(cc, rr);
                if (_occupant[i] < 0 || _occupant[i] == mover) continue;
                // ⭐ Nur Festes sperrt (>= 14000). Eine bewegliche Einheit ist
                // fuer die PLANUNG frei — sie faehrt weiter.
                if (_immobile[i]) return false;
            }
        return true;
    }

    /// <summary>
    /// DIE DREI ANTWORTEN DES ORIGINALS. <c>Can_go</c> gibt kein ja/nein zurück,
    /// sondern <b>0 nein · 1 ja, aber jemand muss ausweichen · 2 frei</b> — und
    /// der Unterschied zwischen 0 und 1 ist der ganze Fehler B2.
    /// </summary>
    public enum Step
    {
        /// <summary><c>Can_go</c> = 0. Das Gelände sagt nein, oder es steht
        /// etwas Unbewegliches darauf. Wartet man hier, wartet man ewig.</summary>
        Blocked = 0,

        /// <summary><c>Can_go</c> = 1. In der Zelle steht eine ANDERE EINHEIT.
        /// Sie fährt gleich weiter — das ist der Fall, in dem das Original
        /// wartet und den Weg behält.</summary>
        GiveWay = 1,

        /// <summary><c>Can_go</c> = 2. Frei.</summary>
        Free = 2,
    }

    /// <summary>
    /// WIE <c>Can_go</c> @0x4055D0 ANTWORTET — und woher die drei Fälle kommen.
    ///
    /// <para>Gelesen am 15.08.2026 aus Anlass von B2 (»Gruppenauswahl und
    /// hintereinander weg fahren wie über brücken lässt einheiten nicht mehr
    /// fahren, gerade wenn ein Fahrweg durch die brücke blockiert ist, weil
    /// gerade jemand anders drüber fährt«).</para>
    ///
    /// <para>⚠ <b>Der Aufrufer war über den rohen Byte-Scan zu finden, nicht
    /// über <c>call</c>:</b> auf <c>Can_go</c> zeigt genau EIN Sprung
    /// (@0x4018fc, ein Vermittler des Inkrementallinkers), und erst auf DEN
    /// zeigen fünf echte Aufrufe. <c>disx2.py call 0x4055D0</c> meldete null —
    /// Regel 7 in Reinform. Werkzeug: <c>aekernel-tools/move_re.py</c>.</para>
    ///
    /// <para><b>Wie das Original die 1 herstellt</b> (@0x40624b..@0x406275, in
    /// allen drei Fahrwerkszweigen wortgleich, @0x40582e und @0x405a8e): es
    /// liest das imap der Zielzelle. <c>0xFFFD</c>/<c>0xFFFE</c> sind Gelände
    /// und gehen weiter; alles andere ist ein HANDLE, aus dem
    /// <c>handle − 10000</c> den Platz der Einheit macht (@0x406264
    /// <c>sub ax, 0x2710</c>). Erst wenn die Prüfung @0x433df0 dazu nein sagt,
    /// wird der Merker gesetzt, und am Ende steht
    /// <c>eax = 2 − (merker != 0 ? 1 : 0)</c> (@0x405926..@0x405938,
    /// <c>sbb ecx,ecx</c> + <c>inc ecx</c>).</para>
    ///
    /// <para>⚠ <b>Was hier UNGELESEN bleibt</b> und darum grob nachgebildet ist:
    /// die Prüfung @0x433df0 selbst. Sie schlägt in einer eigenen Tafel
    /// (0x7847e8) nach und kennt einen Sonderfall 0x63. Wir setzen dafür
    /// schlicht »der Belegende ist eine bewegliche Einheit« — das trifft den
    /// Fall, um den es geht, aber es ist nicht dieselbe Frage. Wer sie liest,
    /// gehört hierher.</para>
    /// </summary>
    public Step Ask(int c, int r, MoveClass mc = MoveClass.Vehicle, int mover = -1)
        => AskRumpf(c, r, mc, mover, HullOf(mover));

    /// <summary>
    /// ⭐ Wie <see cref="Ask"/>, aber mit der Kantenlaenge als ARGUMENT.
    ///
    /// <para>⚠⚠ 24.08.2026 — herausgezogen, weil ein Pruefstand daran
    /// gescheitert ist. Er wollte wissen, ob eine Zelle einen 4x4-Rumpf traegt,
    /// und rief dafuer <see cref="IsFree"/> in einer eigenen 4x4-Schleife.
    /// <see cref="IsFree"/> prueft aber SELBST den ganzen Rumpf — die Frage lief
    /// damit ueber 7x7 Zellen und war fuer jedes Schiff »gesperrt«.
    /// ⭐ Die Lehre: wer den Rumpf nur INNEN kennt, verfuehrt jeden Aufrufer,
    /// ihn AUSSEN noch einmal zu nehmen. Also gibt es jetzt eine Stelle, die ihn
    /// entgegennimmt, und eine, die ihn nachschlaegt — und keinen Grund mehr,
    /// selbst zu schleifen.</para>
    /// </summary>
    public Step AskRumpf(int c, int r, MoveClass mc, int mover, int side)
    {
        if (side < 1) side = 1;
        bool giveWay = false;
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                int cc = c + dx, rr = r + dy;
                if (!CanEnter(cc, rr, mc)) return Step.Blocked;
                int i = Idx(cc, rr);
                if (_occupant[i] < 0 || _occupant[i] == mover) continue;
                if (_crushable[i]) continue;          // Infanterie hält niemanden auf
                // ⚠ Ein Gebäude weicht nicht aus. Das Original unterscheidet
                // hier nicht nach dem Satz, sondern am imap-Wert: 10000..13999
                // ist eine Einheit, ab 14000 steht dort etwas Festes. Bei uns
                // trägt der Stempel es mit (siehe SetOccupant).
                if (_immobile[i]) return Step.Blocked;
                giveWay = true;                        // eine Einheit — die fährt weiter
            }
        return giveWay ? Step.GiveWay : Step.Free;
    }

    /// <summary>
    /// ⭐ <b>WARUM geht dieser Schritt nicht?</b> — dieselbe Schleife wie
    /// <see cref="Ask"/>, aber sie nennt die ZELLE und den GRUND statt nur
    /// »gesperrt«.
    ///
    /// <para>⚠ 24.08.2026, aus der Meldung »als würde da was blockieren«. Genau
    /// das ist der Satz, den <see cref="Ask"/> nicht beantworten kann: es gibt
    /// drei Gründe (Gelände, ein festes Etwas, eine Einheit) und bei einem
    /// 4×4-Rumpf sechzehn Zellen, in denen sie stecken können. Ohne Zelle und
    /// Grund bleibt nur Raten.</para>
    ///
    /// <para>⚠ Diese Fassung MUSS Zeichen für Zeichen dieselbe Schleife sein
    /// wie <see cref="Ask"/>. Eine Diagnose, die anders prüft als der
    /// Entscheider, beschreibt einen anderen Fall.</para>
    /// </summary>
    public string WarumGesperrt(int c, int r, MoveClass mc, int mover)
        => WarumGesperrt(c, r, mc, mover, HullOf(mover));

    public string WarumGesperrt(int c, int r, MoveClass mc, int mover, int side)
    {
        if (side < 1) side = 1;
        var einheiten = new List<string>();
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                int cc = c + dx, rr = r + dy;
                if (!InBounds(cc, rr)) return $"({cc},{rr}) liegt ausserhalb der Karte";
                if (!CanEnter(cc, rr, mc))
                    return $"({cc},{rr}) ist {GroundWord(cc, rr)} — {mc} darf da nicht hin";
                int i = Idx(cc, rr);
                if (_occupant[i] < 0 || _occupant[i] == mover) continue;
                if (_crushable[i]) continue;
                if (_immobile[i])
                    return $"({cc},{rr}) haelt etwas FESTES (Nr. {_occupant[i]}) — das weicht nicht aus";
                einheiten.Add($"({cc},{rr}) Nr. {_occupant[i]}");
            }
        if (einheiten.Count > 0)
            return "eine EINHEIT steht im Weg (die faehrt weiter): " + string.Join(", ", einheiten);
        return "frei";
    }

    /// <summary>Das Bodenwort einer Zelle, fuer Diagnosezeilen.</summary>
    public string GroundWord(int c, int r) => !InBounds(c, r) ? "ausserhalb"
        : (Ground)_ground[Idx(c, r)] switch
        {
            Ground.Free => "Land",
            Ground.Rough => "rau",
            Ground.Water => "Wasser",
            _ => "gesperrt",
        };

    /// <summary>Jede Zelle, in der ein Stempel steht: <c>(Spalte, Zeile,
    /// Einheitennummer, ist_fest)</c>. Nur fuer Pruefstaende — siehe den
    /// Belegungsabgleich in <c>MapEntityLayer.BelegungCheck</c>.</summary>
    public IEnumerable<(int C, int R, int Ent, bool Fest)> BelegteZellen()
    {
        for (int r = 0; r < Height; r++)
            for (int c = 0; c < Width; c++)
            {
                int i = Idx(c, r);
                if (_occupant[i] >= 0) yield return (c, r, _occupant[i], _immobile[i]);
            }
    }

    /// <summary>Die Rohstoffvorkommen, die DIE KARTE mitbringt —
    /// <c>(spalte, zeile, menge)</c>. Bei jeder gelieferten Karte LEER (dort legt
    /// sie das Missionsskript an); gefüllt nur bei einer vom Karteneditor
    /// erzeugten Karte. Gelesen wird sie von
    /// <c>Rendering.MapEntityLayer._deposits</c> (siehe
    /// <c>Simulation/Deposits.cs</c>), aus der <c>CellOnDeposit</c> @0x4205C0
    /// fragt.</summary>
    public readonly List<(int Col, int Row, int Amount)> Deposits = new();

    /// <summary>The foot soldier standing in the way, or -1. The caller decides
    /// what happens to them — driven through if friendly, run over if not.</summary>
    public int CrushableAt(int c, int r, int mover = -1)
    {
        if (!InBounds(c, r)) return -1;
        int i = Idx(c, r);
        return _occupant[i] >= 0 && _occupant[i] != mover && _crushable[i] ? _occupant[i] : -1;
    }

    // ========================================================================
    //  WAS EIN SCHRITT KOSTET — gelesen am 16.08.2026 (B4, zweite Haelfte)
    // ========================================================================

    /// <summary>Takte je Sekunde im Original. GELESEN, nicht gesetzt:
    /// <c>SetTimer(fenster, 1, 0x14, 0)</c> — 20 ms — steht in beiden GAME.EXE
    /// genau einmal (C: bei 0x14FC0, F: bei 0x14E00, Form
    /// <c>6A 14 6A 01 50 FF 15</c>), und die Fensterprozedur @0x412E30 haengt
    /// WM_TIMER daran. Siehe CAMPAIGN_RE.md.</summary>
    public const int OriginalHz = 50;

    /// <summary>
    /// Was ein Schritt von einer Zelle in die naechste kostet, in
    /// TAUSENDSTELN eines Originaltakt-Schrittes.
    ///
    /// <para><b>Das Original zaehlt Zellen, nicht Bildpunkte.</b> Jede Einheit
    /// fuehrt in Satzfeld <b>+0x06</b> einen Zaehler (das Spiel nennt ihn
    /// selbst »kolik«, @0x4f6bbc). <c>move_units</c> @0x406cd0 erhoeht ihn
    /// einmal je Takt um Feld <b>+0x20</b>, die Geschwindigkeit
    /// (@0x40777b..@0x40778e: <c>ax = [edi+6]; si = [edi+0x20]; si += ax;
    /// [edi+6] = si</c>) — <b>ohne jeden weiteren Term</b>. Der Schritt ist
    /// fertig, sobald der Zaehler die Schwelle erreicht (@0x407817..@0x407839):
    /// <c>((richtung &amp; 1) * 5 + 10) * 8</c>, also <b>80</b> gerade und
    /// <b>120</b> schraeg. Danach wird Spalte/Zeile fortgeschrieben und der
    /// Zaehler auf <c>si − (2·kosten − 1)</c> gesetzt (@0x40799d
    /// <c>imul ax,ax,0xffb0</c> = ×(−80), @0x4079a4 <c>+ esi − 0x9f</c>) — von
    /// Zellmitte zu Zellmitte sind es also <b>2·kosten</b> = 160 bzw. 240.
    /// Die Richtungstafel @0x4f5af0 (8 × zwei i16) zeigt, welche das sind:
    /// die GERADEN Nummern 0/2/4/6 sind (0,±1)/(±1,0), die UNGERADEN 1/3/5/7
    /// die Gitterdiagonalen — <b>die Schraege kostet genau das Anderthalbfache</b>,
    /// die uebliche ganzzahlige Naeherung von √2.</para>
    ///
    /// <para>⚠ Die Zahl hier ist <b>2·kosten − 1</b> = 159 bzw. 239, nicht 160
    /// bzw. 240. Der Abzug von <c>2·kosten − 1</c> ist das, was das Original
    /// wirklich rechnet, und weil der UEBERSCHUSS dabei stehenbleibt, ist genau
    /// dieser Betrag der Zuwachs, den der Zaehler von einer Zelle zur naechsten
    /// braucht — unabhaengig davon, wie weit er beim Ankommen ueber die Schwelle
    /// geschossen ist. Das Verhaeltnis ist damit 239/159 = <b>1,5031</b> und
    /// nicht glatt 1,5; die Abweichung ist die des Originals, nicht unsere.</para>
    ///
    /// <para>⚠⚠ <b>Und das Gelaende kommt darin nicht vor.</b> Vier
    /// unabhaengige Proben, alle am selben Ergebnis:
    /// (1) <c>move_units</c> liest <c>+0x20</c> in seinen ~6000 Befehlen
    /// GENAU EINMAL und schreibt es nirgends;
    /// (2) es ruehrt weder das Gelaenderaster (Zeiger <c>0x677e20</c>) noch das
    /// Zonenraster sec2 (<c>0xa3aeb0</c>) an — kein einziger Zugriff, auch
    /// nicht ueber ein Register (Regel 7 gegengeprobt: es gibt EXE-weit nur
    /// zwei <c>mov reg, 0x6e26c8</c>, @0x406ce3 und @0x41f2f5);
    /// (3) der einzige Schreibzugriff auf <c>+0x20</c> im laufenden Spiel steht
    /// in der Trefferroutine (@0x40caae, »Zasah end1«: halbieren, mindestens 2)
    /// — Schaden, nicht Boden;
    /// (4) das einzige Ereignis je betretener Zelle (»on square« @0x407a62)
    /// tut genau zweierlei: Geduldszaehler +0x1c neu wuerfeln und ein Sprit von
    /// +0x2e abziehen.</para>
    ///
    /// <para>Damit ist unsere alte Setzung »Geroell ×1,45« <b>widerlegt</b> und
    /// zurueckgezogen (Arbeitsweise 4). Sie hatte einen zweiten, groesseren
    /// Fehler verdeckt: wir sind mit fester BILDPUNKT-Geschwindigkeit auf die
    /// naechste Zellmitte zugefahren, und im schraegen Raster sind die acht
    /// Nachbarn verschieden weit entfernt (Kachel 40×20: gerade 22,4 px, die
    /// eine Diagonale 40 px, die andere 20 px). Die Fahrzeit haette also je
    /// nach Himmelsrichtung um den Faktor <b>2</b> geschwankt, wo das Original
    /// 1,5 kennt.</para>
    /// </summary>
    public static int StepCostMilli(Vector2I from, Vector2I to)
        => (from.X != to.X && from.Y != to.Y) ? 239_000 : 159_000;

    /// <summary>GEGENPROBE <c>--old-move-cost</c>: den Stand vor dem 16.08.2026
    /// nachstellen — feste Bildpunktgeschwindigkeit auf die Zellmitte zu,
    /// Geroellaufschlag ×1,45 in Fahrt UND Wegsuche. Ein Schalter, der die alte
    /// Fassung im selben Programm nachstellt, entscheidet in einem Lauf
    /// (Arbeitsweise 31).</summary>
    public static bool MoveCostOld;

    /// <summary>
    /// Relative cost of crossing a cell. ⚠ ZURUECKGEZOGEN als Bremse — siehe
    /// <see cref="StepCostMilli"/>. Steht nur noch fuer die Gegenprobe
    /// <c>--old-move-cost</c> da, die den Stand vor dem 16.08.2026 im selben
    /// Programm nachstellt.
    /// </summary>
    public float TerrainCost(int c, int r, MoveClass mc) => TerrainCostMilli(c, r, mc) / 1000f;

    /// <summary>
    /// Dasselbe in TAUSENDSTELN, ganzzahlig — 1000 = Faktor 1,0.
    ///
    /// <para>⚠ Diese Fassung gibt es, weil das Netzspiel Lockstep ist: jede
    /// Maschine rechnet dieselben Befehle nach, und ein Weg, der auf einem
    /// Rechner eine Zelle anders läuft, ist ein auseinandergelaufenes Spiel.
    /// Die Wegsuche rechnete bis zum 12.08.2026 mit <c>float</c>-Kosten
    /// (1,4142 für die Diagonale, ×1,45 für Geröll, +0,5 je Höhenstufe). Das
    /// ist auf EINER Maschine reproduzierbar, aber es hängt an der
    /// Fliesskomma-Einstellung des Übersetzers (FMA-Zusammenziehung, x87
    /// gegen SSE) — genau die Klasse Unterschied, die zwischen zwei Rechnern
    /// auftritt. Ganze Zahlen tun das nicht.</para>
    ///
    /// <para>Die Zahlen selbst sind unverändert UNSERE (das Original kennt in
    /// <c>Can_go</c> keine Wegkosten), nur eben in Tausendsteln.</para>
    ///
    /// <para>⚠⚠ <b>ZURUECKGEZOGEN am 16.08.2026.</b> Die ×1,45 waren nicht nur
    /// unbelegt, sondern falsch: das Original bremst am Boden ueberhaupt nicht,
    /// siehe <see cref="StepCostMilli"/> mit vier Proben. Diese Rechnung wird
    /// nur noch aufgerufen, wenn <see cref="MoveCostOld"/> gesetzt ist — der
    /// Schalter <c>--old-move-cost</c>, der den alten Stand im selben Programm
    /// nachstellt (Arbeitsweise 31).</para>
    /// </summary>
    public int TerrainCostMilli(int c, int r, MoveClass mc)
    {
        if (mc == MoveClass.Ship) return 1000;
        return GroundAt(c, r) == Ground.Rough ? 1450 : 1000;
    }

    // ---- construction -------------------------------------------------------

    /// <summary>Sizes the grid and takes elevation and the tile flag byte off
    /// the baked map. The ground starts as a fallback read of the tile codes and
    /// is replaced by <see cref="ApplyTerrain"/> as soon as the map's own
    /// terrain block is there.</summary>
    public static NavGrid Build(GDict meta)
    {
        // ⚠ DER FRÜHESTE PUNKT, AN DEN DIESE SITZUNG HERANKOMMT.
        //
        // Der Zufall muss GEKEIMT sein, bevor der erste Takt läuft — sonst
        // würfelt Takt 1 aus der Uhrzeit und der ganze Lauf ist nicht mehr
        // wiederholbar. Build() wird beim Kartenladen genau einmal gerufen
        // (MapEntityLayer.cs:1001) und liegt vor jedem _Process dieser Karte.
        // Die Ebene selbst dürfte diese Sitzung nicht anfassen, deshalb steht
        // es hier; siehe Simulation/Determinism.cs.
        //
        // Es steht VOR der Kachelschleife und rührt bewusst keine Godot-
        // Sammlung an: die Schleife unten legt zehntausende
        // Godot.Collections.Dictionary an, und dieser Godot-Bau stürzt beim
        // Aufräumen davon gelegentlich ab (»Internal CLR error« in
        // DisposablesTracker.RegisterDisposable). Wer hier zusätzlich
        // alloziert, verschiebt nur den Zeitpunkt der Speicherbereinigung.
        Determinism.NewMap(GetS(meta, "mission", $"{GetI(meta, "width")}x{GetI(meta, "height")}"));

        var g = new NavGrid
        {
            Width = GetI(meta, "width"),
            Height = GetI(meta, "height"),
        };

        // Die ROHSTOFFVORKOMMEN, wenn die Karte selbst welche mitbringt.
        //
        // ⚠ Eine GELIEFERTE Karte bringt keine mit, und das ist richtig so: im
        // Original füllt das MISSIONSSKRIPT die Liste, über
        // `add_terra_place(spalte, zeile, menge)` (C: 0x4D0A10, F: 0x4D05C0) im
        // SETUP-Block — 50 Aufrufe in 8 Missionen. Der Block hier ist nur für
        // eine vom Editor ERZEUGTE Karte da, die kein Missionsskript hat und
        // deren Feld-Rohstoffmine sonst 0 Bauplätze hätte. Geschrieben von
        // Import.ContentBuilder.MapMeta aus Import.CwmFile.Terra, gelegt von
        // Editor.MapDeposits — und dort steht auch die Messlatte.
        if (meta.TryGetValue("terra", out var terra) && terra.VariantType == Variant.Type.Array)
            foreach (var e in terra.AsGodotArray())
            {
                if (e.VariantType != Variant.Type.Array) continue;
                var q = e.AsGodotArray();
                if (q.Count < 3) continue;
                g.Deposits.Add((q[0].AsInt32(), q[1].AsInt32(), q[2].AsInt32()));
            }

        if (g.Width <= 0 || g.Height <= 0) { g.Width = g.Height = 0; return g; }

        int n = g.Width * g.Height;
        g._ground = new byte[n];
        g._flag = new byte[n];
        g._elev = new byte[n];
        g._occupant = new int[n];
        g._crushable = new bool[n];
        g._immobile = new bool[n];
        Array.Fill(g._occupant, -1);

        if (!meta.TryGetValue("tiles", out var tv) || tv.VariantType != Variant.Type.Array)
            return g;

        // ⚠ `using` — GEMESSEN am 12.08.2026, und es ist kein Schoenheitsfehler.
        //
        // Jede dieser Kacheln legt ein Godot.Collections.Dictionary an, und
        // jedes davon traegt sich in Godots DisposablesTracker ein. Auf
        // map_NET07 sind das rund 32 000 Eintraege, die alle bis zur naechsten
        // Speicherbereinigung liegenbleiben — und dann raeumt der
        // Finalisierer-Faden sie ab, waehrend die Schleife noch neue eintraegt.
        // Der Tracker haelt das nicht aus: der Lauf endete mit »Fatal error.
        // Internal CLR error. (0x80131506) at
        // Godot.DisposablesTracker.RegisterDisposable«, mitten im Kartenladen.
        //
        // Das Tueckische daran war, dass es an der BEFEHLSZEILE haengt: mit
        // einem zusaetzlichen, voellig wirkungslosen Schalter (--zz) stuerzte
        // derselbe Lauf jedes Mal ab, ohne ihn nie — ein zusaetzliches Wort
        // verschiebt die Speicherbereinigung um genau so viel, dass das Rennen
        // anders ausgeht. `using` gibt jede Kachel sofort wieder frei; der
        // Tracker bleibt klein und das Rennen faellt aus.
        foreach (var item in tv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            // Die UNGETYPTE Sammlung, weil nur die IDisposable ist —
            // Dictionary<string,Variant> laesst sich nicht freigeben.
            using var t = item.AsGodotDictionary();
            int c = GetV(t, "col"), r = GetV(t, "row");
            if (!g.InBounds(c, r)) continue;
            int i = g.Idx(c, r);
            g._elev[i] = (byte)Mathf.Clamp(GetV(t, "elev"), 0, 255);
            g._flag[i] = (byte)Mathf.Clamp(GetV(t, "flag"), 0, 255);

            // fallback only, for content imported before the terrain block: the
            // tile code says water, an object cell is assumed to block. Both
            // are superseded the moment ApplyTerrain runs — and the second of
            // them is exactly the assumption that made bridges impassable.
            bool isObject = t.TryGetValue("object", out var ob) && ob.AsBool();
            g._ground[i] = isObject ? (byte)Ground.Blocked
                         : GetV(t, "code", 9999) <= WaterCodeMax ? (byte)Ground.Water
                         : (byte)Ground.Free;
        }
        return g;
    }

    /// <summary>Lay the map's own passability (entities.json `terrain`, run
    /// length encoded row major) over the fallback. This is the authority.</summary>
    public void ApplyTerrain(GDict terrain)
    {
        int w = GetI(terrain, "width"), h = GetI(terrain, "height");
        if (w <= 0 || h <= 0 ||
            !terrain.TryGetValue("rle", out var rv) || rv.VariantType != Variant.Type.Array)
            return;

        int at = 0, total = w * h;
        foreach (var pair in rv.AsGodotArray())
        {
            if (pair.VariantType != Variant.Type.Array) continue;
            var p = pair.AsGodotArray();
            if (p.Count < 2) continue;
            byte v = (byte)Mathf.Clamp(p[0].AsInt32(), 0, 3);
            int run = p[1].AsInt32();
            for (int k = 0; k < run && at < total; k++, at++)
            {
                int col = at % w, row = at / w;
                if (InBounds(col, row)) _ground[Idx(col, row)] = v;
            }
        }
        Inferred = GetI(terrain, "inferred");
        HasTerrain = at >= total;
    }

    private static int GetI(GDict d, string k, int def = 0)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsInt32() : def;

    /// <summary>Wie <see cref="GetI"/>, aber auf der ungetypten Sammlung —
    /// siehe das <c>using</c> in <see cref="Build"/>.</summary>
    private static int GetV(Godot.Collections.Dictionary d, string k, int def = 0)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsInt32() : def;

    private static string GetS(GDict d, string k, string def = "")
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil ? v.AsString() : def;

    // ---- dynamic occupancy --------------------------------------------------

    public void ClearOccupants()
    {
        if (_occupant.Length > 0) Array.Fill(_occupant, -1);
        if (_crushable.Length > 0) Array.Fill(_crushable, false);
        if (_immobile.Length > 0) Array.Fill(_immobile, false);
        // ⚠ Die Ruempfe gehen MIT. Wer die Belegung leert und die Ruempfe
        // stehen laesst, traegt beim naechsten Aufbau die Rumpfgroessen der
        // vorigen Karte — und die Einheitennummern sind dort andere.
        ClearHulls();
    }

    /// <summary>Take one cell out of the blocked class. Used for the
    /// Nachschub-Posten: its tick handler @0x43e872 services the unit standing
    /// ON the post, so that cell must not block.</summary>
    public void ClearStatic(int c, int r)
    {
        if (InBounds(c, r) && (Ground)_ground[Idx(c, r)] == Ground.Blocked)
            _ground[Idx(c, r)] = (byte)Ground.Free;
    }

    /// <summary><paramref name="crushable"/> marks a foot soldier: they are
    /// driven through or run over, never blocked against (see
    /// <see cref="IsFree"/>).</summary>
    /// <summary>⚠ Stempelt den GANZEN Rumpf ab (<paramref name="c"/>,
    /// <paramref name="r"/>) — <see cref="HullSide"/>. Fuer alles ausser
    /// Schiffen ist das genau die eine Zelle wie bisher.</summary>
    public void SetOccupant(int c, int r, int entity, bool crushable = false,
                             bool immobile = false)
    {
        if (entity >= 0)
        {
            _stempelArt[entity] = (crushable, immobile);
            if (!_anker.TryGetValue(entity, out var l)) _anker[entity] = l = new List<Vector2I>();
            var a = new Vector2I(c, r);
            if (!l.Contains(a)) l.Add(a);
        }
        Stempeln(c, r, entity, crushable, immobile);
    }

    /// <summary>Die reine Schreibbewegung — ohne Buchhaltung.</summary>
    private void Stempeln(int c, int r, int entity, bool crushable, bool immobile)
    {
        int side = HullOf(entity);
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                if (!InBounds(c + dx, r + dy)) continue;
                int i = Idx(c + dx, r + dy);
                _occupant[i] = entity;
                _crushable[i] = crushable;
                _immobile[i] = immobile;
            }
    }

    /// <summary>
    /// ⭐⭐ <b>WELCHE ANKER EINE EINHEIT GERADE HAELT</b> — und warum das Gitter
    /// das wissen muss (24.08.2026).
    ///
    /// <para>⚠⚠ Gemeldet: »die grossen 2 Boote lassen sich nicht nach unten
    /// fahren« und »der kleine Kreuzer kann nicht mehr unterhalb vom Hafen
    /// langfahren, als würde da was blockieren«. Gemessen mit dem
    /// Belegungsabgleich: ein 4×4-Schiff, das <b>19 Zellen gefahren</b> ist,
    /// belegte danach <b>keine einzige Zelle</b> — 24 Löcher, 0 Phantome.</para>
    ///
    /// <para><b>Die Ursache ist die Ueberlappung der zwei Anker.</b> Während
    /// eines Schrittes hält eine Einheit ihre Zelle UND die vorgemerkte. Bei
    /// einem 1×1 sind das zwei getrennte Zellen; bei einem 4×4, der einen
    /// Schritt weit auseinanderliegt, <b>teilen sich die beiden Anker zwölf von
    /// sechzehn Zellen</b>. Und <see cref="ClearOccupant"/> löschte alles, was
    /// die eigene Nummer trug — also auch die zwölf, die der ANDERE Anker
    /// gerade braucht. Nach dem ersten Schritt blieben vier Zellen, nach dem
    /// zweiten keine.</para>
    ///
    /// <para>⭐ <b>Warum die Buchhaltung hierher gehört und nicht zu den
    /// Aufrufern:</b> es gibt elf Stempel- und fünfzehn Löschstellen, und
    /// achtundzwanzig Stellen setzen eine Vormerkung zurück. Ein »nach dem
    /// Löschen bitte neu stempeln« an jeder davon wäre genau die Regel in
    /// achtundzwanzig Kopien, deren erste vergessene Kopie der nächste Fehler
    /// ist — dieselbe Falle wie bei <see cref="SetHull"/> selbst, und dieselbe
    /// wie bei den sechzehn Blickrichtungen der Schiffe zwei Tage vorher.</para>
    ///
    /// <para>⚠ Was das NICHT heilt: eine Stelle, die <c>Reserved = null</c>
    /// setzt, ohne den Stempel zu löschen. Dann bleibt ein Anker in dieser
    /// Liste stehen, den die Einheit nicht mehr anfahren will — ein Phantom.
    /// Das ist eine eigene Fehlerklasse, und der Belegungsabgleich zählt sie
    /// getrennt.</para>
    /// </summary>
    private readonly Dictionary<int, List<Vector2I>> _anker = new();

    /// <summary>⭐ <c>--alt-stempel</c>: das Neustempeln abschalten und damit den
    /// Stand vor dem 24.08.2026 wiederherstellen. <b>Die Gegenprobe zum
    /// Belegungsabgleich</b> — mit diesem Schalter MUSS er durchfallen, sonst
    /// misst er nicht, was er zu messen behauptet (Arbeitsweise: eine Messlatte,
    /// die nicht reissen kann, ist keine).</summary>
    public static bool AltStempel;

    /// <summary>Mit welchen Merkmalen eine Einheit gestempelt wurde, damit ein
    /// Neustempeln sie nicht verliert.</summary>
    private readonly Dictionary<int, (bool Crush, bool Immobile)> _stempelArt = new();

    /// <summary>Wer in dieser EINEN Zelle steht, ohne Rumpfschleife — fuer den
    /// Belegungsabgleich. −1 heisst leer.</summary>
    public int BesetztVon(int c, int r) => InBounds(c, r) ? _occupant[Idx(c, r)] : -1;

    /// <summary>Die Anker einer Einheit — fuer den Belegungsabgleich.</summary>
    public IReadOnlyList<Vector2I> AnkerVon(int entity)
        => _anker.TryGetValue(entity, out var l) ? l : System.Array.Empty<Vector2I>();

    /// <summary>Dieselbe Flaeche wieder freigeben. ⚠ Der Anker muss derselbe
    /// sein, mit dem gestempelt wurde — waehrend eines Schrittes haelt eine
    /// Einheit zwei Anker (den alten und den vorgemerkten), und beide werden
    /// einzeln gesetzt und einzeln geloescht.</summary>
    public void ClearOccupant(int c, int r, int entity)
    {
        int side = HullOf(entity);
        // Diesen Anker gibt die Einheit auf.
        if (_anker.TryGetValue(entity, out var l))
        {
            l.Remove(new Vector2I(c, r));
            if (l.Count == 0) _anker.Remove(entity);
        }
        for (int dy = 0; dy < side; dy++)
            for (int dx = 0; dx < side; dx++)
            {
                if (!InBounds(c + dx, r + dy)) continue;
                int i = Idx(c + dx, r + dy);
                if (_occupant[i] != entity) continue;
                _occupant[i] = -1;
                _crushable[i] = false;
                _immobile[i] = false;
            }
        // ⭐⭐ UND WIEDER STEMPELN, WAS SIE NOCH HAELT — siehe die Herleitung
        // bei _anker. Ohne diese drei Zeilen radiert der aufgegebene Anker die
        // Zellen des behaltenen mit weg, weil beide dieselbe Nummer tragen.
        if (AltStempel) return;
        if (!_anker.TryGetValue(entity, out var rest)) return;
        var art = _stempelArt.TryGetValue(entity, out var v) ? v : (false, false);
        foreach (var a in rest) Stempeln(a.X, a.Y, entity, art.Item1, art.Item2);
    }

    /// <summary>Cell counts per ground class, for the HUD / debug overlay.</summary>
    public (int Free, int Rough, int Water, int Blocked) Census()
    {
        int f = 0, ro = 0, wa = 0, bl = 0;
        foreach (byte v in _ground)
            switch ((Ground)v)
            {
                case Ground.Free: f++; break;
                case Ground.Rough: ro++; break;
                case Ground.Water: wa++; break;
                default: bl++; break;
            }
        return (f, ro, wa, bl);
    }

    /// <summary>Coarse debug texture: the four ground classes tinted.</summary>
    public ImageTexture? BuildDebugTexture()
    {
        if (Width <= 0 || Height <= 0) return null;
        var img = Image.CreateEmpty(Width, Height, false, Image.Format.Rgba8);
        var byGround = new[]
        {
            new Color(0.25f, 0.9f, 0.4f, 0.20f),   // 0 free
            new Color(1f, 0.85f, 0.25f, 0.35f),    // 1 rough
            new Color(0.15f, 0.45f, 1f, 0.45f),    // 2 water
            new Color(1f, 0.25f, 0.2f, 0.40f),     // 3 blocked
        };
        for (int r = 0; r < Height; r++)
            for (int c = 0; c < Width; c++)
                img.SetPixel(c, r, byGround[_ground[Idx(c, r)] & 3]);
        return ImageTexture.CreateFromImage(img);
    }

    // ---- pathfinding --------------------------------------------------------

    // =====================================================================
    //  DIE WEGSUCHE DES ORIGINALS
    // =====================================================================

    /// <summary>
    /// ⭐⭐ <b>Die Richtungstafel des Originals</b>, <c>0x4F5AF0</c> / F
    /// <c>0x4F4AF0</c> — <b>in beiden Fassungen bitgleich</b>.
    ///
    /// <code>
    ///  i        0    1    2    3    4    5    6    7
    ///  dSpalte  0   -1   -1   -1    0   +1   +1   +1
    ///  dZeile  +1   +1    0   -1   -1   -1    0   +1
    /// </code>
    ///
    /// <para>⭐ <b>Gerade Indizes sind die geraden Richtungen, ungerade die
    /// Diagonalen</b> — das ist keine Deutung, es fällt aus der Tafel heraus
    /// und trägt danach die ganze Suche: die Erweiterung nimmt erst die
    /// ungeraden, das Rückverfolgen erst die geraden.</para>
    ///
    /// <para>⚠ Sie ist NICHT dieselbe wie <see cref="Dirs"/>. Die Reihenfolge
    /// ist Teil des Verfahrens, nicht Geschmack: wer eine andere nimmt, bekommt
    /// bei gleich langen Wegen einen anderen — und im Netzspiel zwei
    /// verschiedene.</para>
    /// </summary>
    public static readonly Vector2I[] UrDirs =
    {
        new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
        new(0, -1), new(1, -1), new(1, 0), new(1, 1),
    };

    /// <summary>⭐ Die Erweiterungsreihenfolge: <b>erst die vier Diagonalen,
    /// dann die vier Geraden</b> (BB.2). Genau diese Reihenfolge.</summary>
    private static readonly int[] UrErweitern = { 1, 3, 5, 7, 0, 4, 6, 2 };

    /// <summary>⭐ Die Rückverfolgungsreihenfolge: <b>erst die Geraden, dann die
    /// Diagonalen</b> (BB.3). Andersherum als die Erweiterung — auch das steht
    /// so da.</summary>
    private static readonly int[] UrZurueck = { 0, 2, 4, 6, 1, 3, 5, 7 };

    /// <summary>⭐ Der Wegpuffer des Originals fasst <b>50</b> Schritte
    /// (<c>cmp …,0x32</c>). ⭐ Unabhängig bestätigt: <c>0x7AEC38</c> ist sec14
    /// mit <c>0x61A80 = 400 000 = 8000 × 50</c> — die Zahl aus dem Code und die
    /// aus dem Dateiformat stimmen überein, zwei völlig getrennte Quellen.</summary>
    public const int UrWegLaenge = 50;

    /// <summary>⭐ Die Ringlänge des 1×1-Pfades: <b>4096</b> Einträge
    /// (Bytemaske <c>and si,0x1FFF</c> = 8192 Byte zu zwei).
    /// ⚠ Die 2×2- und 4×4-Pfade nehmen <b>5000</b> auf demselben Puffer.
    /// <b>In sich ist jede Variante widerspruchsfrei — wer nachbaut, darf das
    /// nicht vereinheitlichen.</b></summary>
    public const int UrRing1x1 = 4096, UrRingGross = 5000;

    /// <summary>Wie viele Zellen die letzte Suche angefasst hat, wie viele
    /// Wellen sie brauchte, und ob sie am Ring hängengeblieben ist — für den
    /// Prüfstand.</summary>
    public static int UrBesucht, UrWellen;
    public static bool UrRingVoll;

    /// <summary>
    /// ⭐⭐ <b>Die Wegsuche des Originals</b> — eine reine 8-Nachbar-
    /// <b>Breitensuche mit Wellenmarken</b> (OFFENE_FRAGEN <b>BB</b>,
    /// <c>0x4D2580</c> für den 1×1-Rumpf).
    ///
    /// <para><b>Es gibt keine Kostenkarte, keine Heuristik, keine
    /// Prioritätswarteschlange.</b> Jeder Schritt kostet gleich viel, eine
    /// Diagonale genauso viel wie eine Gerade — die entstehende Metrik ist die
    /// <b>Chebyshev-Distanz</b>. Unsere <see cref="FindPath"/> ist ein A* mit
    /// Kosten und Schätzer und damit <b>komplett eigene Erfindung</b>.</para>
    ///
    /// <para><b>Die Entfernung wird nicht gespeichert, sondern in die Karte
    /// geschrieben:</b> jede erreichte Zelle bekommt die laufende Wellenmarke,
    /// die bei 9 beginnt, bei jedem Wellenwechsel um eins steigt und nach 255
    /// auf 8 zurückspringt — <b>248 unterscheidbare Wellen</b>. Den
    /// Wellenwechsel erkennt das Original daran, dass der Lesezeiger die
    /// gemerkte Wellengrenze erreicht; hier steht dafür die Zahl der Einträge
    /// der laufenden Welle, was dasselbe leistet.</para>
    ///
    /// <para>⭐⭐ <b>Ecken werden nicht geschnitten.</b> Jede Diagonale prüft
    /// genau die beiden anliegenden geraden Nachbarn — <b>8 von 8</b>
    /// Bedingungen haben diese Form, keine Ausnahme. Nullmodell (jede Bedingung
    /// eine von acht Nachbarzellen): <c>8^-8</c>, rund <c>6e-8</c>.</para>
    ///
    /// <para>⚠ <b>Was hier UNSER bleibt: die Karte.</b> Das Original baut je
    /// Suche eine eigene Passierbarkeitskarte aus sec6, mit <b>vierzehn</b>
    /// Bewegungsarten und einem dreiwertigen Feld (0 frei, 1 <i>weich</i>
    /// gesperrt, 2 hart). Wir nehmen <see cref="IsFree"/>, also zwei Werte.
    /// <b>Der Unterschied ist der Wert 1:</b> dort darf man nicht hinein, aber
    /// eine Diagonale daran vorbei ist erlaubt. Ohne ihn sind wir an solchen
    /// Stellen etwas strenger als das Original. <b>Das VERFAHREN ist das des
    /// Originals, die KARTE ist noch unsere.</b></para>
    ///
    /// <para>⭐⭐ <b>23.08.2026 — DER 50-SCHRITTE-PUFFER IST JETZT DA.</b> Hier
    /// stand »ebenfalls nicht hier«. Das Original fährt höchstens
    /// <see cref="UrWegLaenge"/> Ziffern ab (sec14, 8000 × 50) und plant dann
    /// mit DEMSELBEN Ziel neu — dadurch reagiert es unterwegs auf Änderungen.
    /// Der Weg wird darum abgeschnitten, und
    /// <c>MapEntityLayer</c> setzt am abgeschnittenen Ende die Neuplanung an
    /// statt »angekommen« zu melden.</para>
    ///
    /// <para>⭐ <b>Und er gehört zum zweiten Stück:</b> die Suchkarte sperrt
    /// keine beweglichen Einheiten mehr (<see cref="PfadOffen"/>). Beides
    /// einzeln ist schlechter als keines von beiden — genau daran ist der
    /// Versuch vom 16.08.2026 gescheitert.</para>
    /// </summary>
    public List<Vector2I>? FindPathUr(Vector2I start, Vector2I goal,
                                      MoveClass mc = MoveClass.Vehicle, int mover = -1)
    {
        UrBesucht = 0; UrWellen = 0; UrRingVoll = false;
        if (!InBounds(start.X, start.Y) || !InBounds(goal.X, goal.Y)) return null;
        if (!IsFree(goal.X, goal.Y, mc, mover))
        {
            var ausweich = NearestFree(goal, mc, mover);
            if (ausweich == null) return null;
            goal = ausweich.Value;
        }
        if (start == goal) return new List<Vector2I>();

        // ---- die Karte: 0 frei, 2 gesperrt ---------------------------------
        int w = Width, h = Height;
        var karte = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                // ⭐⭐ 23.08.2026 — PfadOffen statt IsFree: bewegliche Einheiten
                // sperren die PLANUNG nicht mehr, nur Festes tut es (BB.1,
                // Art 0). Siehe den Kopf von PfadOffen fuer die Messung und
                // fuer den zurueckgezogenen ersten Anlauf vom 16.08.2026.
                karte[y * w + x] = (NeuePfadkarte ? PfadOffen(x, y, mc, mover)
                                                 : IsFree(x, y, mc, mover)) ? (byte)0 : (byte)2;

        // ⭐ Der Kartenrand wird gesperrt (Zeile 0, Zeile H-1, Spalte 0,
        // Spalte B-1 auf 2). Damit braucht die innere Schleife KEINE
        // Randpruefung -- genau darum tut das Original es.
        for (int x = 0; x < w; x++) { karte[x] = 2; karte[(h - 1) * w + x] = 2; }
        for (int y = 0; y < h; y++) { karte[y * w] = 2; karte[y * w + w - 1] = 2; }

        int si = start.Y * w + start.X, zi = goal.Y * w + goal.X;
        if (si == zi) return new List<Vector2I>();
        if (karte[zi] == 2) return null;
        karte[si] = 8;                       // Startmarke des 1x1-Pfades

        var ring = new int[UrRing1x1];
        int kopf = 0, schwanz = 0, drin = 0;
        ring[schwanz++] = si; drin++;
        int marke = 9, welleRest = 1, naechsteWelle = 0, schritte = 0;
        bool gefunden = false;

        while (drin > 0)
        {
            if (welleRest == 0)
            {
                // ⭐ Wellenwechsel: Marke eins hoch, Umlauf 255 -> 8.
                marke = marke >= 255 ? 8 : marke + 1;
                schritte++;
                welleRest = naechsteWelle;
                naechsteWelle = 0;
                if (welleRest == 0) break;
            }
            int c = ring[kopf]; kopf = (kopf + 1) % UrRing1x1; drin--; welleRest--;
            UrBesucht++;

            if (c == zi) { karte[zi] = 5; gefunden = true; break; }

            int cx = c % w, cy = c / w;
            foreach (int i in UrErweitern)
            {
                var d = UrDirs[i];
                int nx = cx + d.X, ny = cy + d.Y;
                int n = ny * w + nx;
                if (karte[n] != 0) continue;
                if ((i & 1) != 0)
                {
                    // ⭐ Diagonale: BEIDE anliegenden Geraden muessen <= 1 sein.
                    // Das sind genau (i-1) und (i+1) modulo 8 -- an allen vier
                    // Diagonalen nachgerechnet, nicht angenommen.
                    var a = UrDirs[(i + 7) & 7];
                    var b = UrDirs[(i + 1) & 7];
                    if (karte[(cy + a.Y) * w + cx + a.X] > 1) continue;
                    if (karte[(cy + b.Y) * w + cx + b.X] > 1) continue;
                }
                karte[n] = (byte)marke;
                if (drin >= UrRing1x1) { UrRingVoll = true; break; }
                ring[schwanz] = n; schwanz = (schwanz + 1) % UrRing1x1;
                drin++; naechsteWelle++;
            }
            if (UrRingVoll) break;
        }

        UrWellen = schritte;
        if (!gefunden || karte[zi] != 5) return null;   // ⭐ nichts schreiben

        // ---- zurueckverfolgen ----------------------------------------------
        // Von der Zielzelle rueckwaerts den Nachbarn mit Marke-1 suchen,
        // ERST die Geraden (0,2,4,6), dann die Diagonalen (1,3,5,7).
        //
        // ⚠⚠ HIER SASS DER FEHLER (gemeldet 23.08.2026: »die Wegfuehrung ist
        // katastrophal« und »Einheiten springen ein paar Felder«).
        //
        // Die Wellenmarken laufen so: waehrend die Welle der Entfernung d
        // ABGERAEUMT wird, steht `marke` auf 9+d, und die Zellen dieser Welle
        // tragen 8+d (sie bekamen ihre Marke, als 9+(d-1) galt). Wird die
        // Zielzelle aufgesammelt, ist also
        //
        //      marke     = 9+D        (der Zaehler, eine Welle VORAUS)
        //      Zielwelle = 8+D        (die Zellen neben dem Ziel)
        //      Vorgaenger= 7+D        (die Welle, aus der das Ziel kam)
        //
        // Gesucht ist der VORGAENGER, also 7+D = marke-2. Hier stand marke-1,
        // und das ist die Marke der Zielwelle SELBST: der Rueckweg wechselte
        // damit auf eine Nachbarzelle GLEICHER Entfernung statt auf die
        // vorherige. Jeder weitere Schritt blieb um eine Welle verschoben.
        //
        // ⭐ Wie es sich zeigt: die Zellen des zurueckgegebenen Weges sind nicht
        // mehr benachbart. Gemessen auf map_01, (4,39) -> (4,35):
        //      falsch: 2,38  2,37     — (4,39) auf (2,38) ist ZWEI Spalten weit
        //      richtig: 3,38  2,37    — und genau das liefert auch der alte A*
        // Eine Einheit, die einer solchen Liste folgt, setzt sichtbar ueber
        // Felder hinweg. Das ist das gemeldete »Springen/Beamen«.
        var rueck = new List<Vector2I>();
        static int Runter(int v) => v == 8 ? 255 : v - 1;
        int cc = zi, m = Runter(Runter(marke));
        for (int k = schritte - 1; k >= 0; k--)
        {
            int gx = cc % w, gy = cc / w, gewaehlt = -1;
            foreach (int i in UrZurueck)
            {
                var d = UrDirs[i];
                int px = gx + d.X, py = gy + d.Y;
                if (px < 0 || py < 0 || px >= w || py >= h) continue;
                if (karte[py * w + px] != m) continue;
                gewaehlt = i; break;
            }
            if (gewaehlt < 0) return null;
            rueck.Add(new Vector2I(gx, gy));
            var dd = UrDirs[gewaehlt];
            cc = (gy + dd.Y) * w + gx + dd.X;
            m = Runter(m);
        }
        rueck.Reverse();

        // ⭐⭐ DER 50-SCHRITTE-PUFFER (sec14, 8000 x 50 Byte). Das Original
        // faehrt hoechstens so viele Ziffern ab und plant dann mit DEMSELBEN
        // Ziel neu. Genau daran haengt, dass ein Weg durch einen Pulk nicht
        // fuer immer ein Weg durch einen Pulk bleibt -- siehe PfadOffen.
        // ⚠ Die Neuplanung selbst sitzt nicht hier, sondern beim Verfolger:
        // MapEntityLayer erkennt einen ABGESCHNITTENEN Weg daran, dass er
        // genau UrWegLaenge lang ist, und setzt dann neu an, statt
        // »angekommen« zu melden.
        if (!KeinWegpuffer && rueck.Count > UrWegLaenge)
        {
            UrAbgeschnitten++;
            rueck.RemoveRange(UrWegLaenge, rueck.Count - UrWegLaenge);
        }
        return rueck;
    }

    /// <summary>Wie oft ein Weg am 50-Schritte-Puffer abgeschnitten wurde.
    /// ⚠ Ohne diese Zahl ist »die Neuplanung laeuft« nicht von »sie kam nie
    /// dran« zu unterscheiden (Arbeitsweise 33).</summary>
    public static int UrAbgeschnitten;

    /// <summary>
    /// <c>--wegsuche-check</c> — <b>ist es wirklich die Breitensuche des
    /// Originals?</b> (22.08.2026, OFFENE_FRAGEN <b>BB</b>.)
    ///
    /// <para>⚠ Gemessen wird nicht, DASS ein Weg herauskommt — das täte ein A*
    /// auch. Gemessen werden die drei Eigenschaften, in denen sich die beiden
    /// Verfahren <b>unterscheiden</b>: die Metrik, die Ecken und die
    /// Richtungstafel. Ein Lauf, der nur »Weg gefunden« prüft, hätte den alten
    /// Zustand genauso bestanden.</para>
    /// </summary>
    public string WegsucheCheck()
    {
        var sb = new System.Text.StringBuilder("wegsuche-check\n");
        bool alles = true;
        void Sag(string was, bool ok)
        {
            sb.Append($"  {was}: {(ok ? "richtig" : "FALSCH")}\n");
            alles &= ok;
        }

        // 1. ⭐ DIE RICHTUNGSTAFEL, Ziffer fuer Ziffer gegen 0x4F5AF0.
        int[] sollC = { 0, -1, -1, -1, 0, 1, 1, 1 };
        int[] sollZ = { 1, 1, 0, -1, -1, -1, 0, 1 };
        int tf = 0;
        for (int i = 0; i < 8; i++)
            if (UrDirs[i].X != sollC[i] || UrDirs[i].Y != sollZ[i]) tf++;
        Sag($"Richtungstafel 0x4F5AF0: {8 - tf} von 8 Eintraegen treffen", tf == 0);

        // ⭐ Gerade Indizes sind die Geraden, ungerade die Diagonalen -- die
        // Eigenschaft, auf der die ganze Reihenfolge beruht.
        bool parit = true;
        for (int i = 0; i < 8; i++)
        {
            bool diag = UrDirs[i].X != 0 && UrDirs[i].Y != 0;
            if (diag != ((i & 1) != 0)) parit = false;
        }
        Sag("gerade Indizes = gerade Richtungen, ungerade = Diagonalen", parit);

        // ⭐ Die Umkehrtafel ist i XOR 4 -- 8/8 im Original, Nullmodell 1/8!.
        bool xor4 = true;
        for (int i = 0; i < 8; i++)
        {
            var g = UrDirs[i];
            var r = UrDirs[i ^ 4];
            if (r.X != -g.X || r.Y != -g.Y) xor4 = false;
        }
        Sag("Umkehrtafel 0x539B20 = i XOR 4 (Gegenrichtung)", xor4);

        // 2. ⭐⭐ DIE METRIK IST CHEBYSHEV. Auf freier Flaeche kostet die
        // Diagonale so viel wie die Gerade: ein Ziel (n,n) ist n Schritte weit,
        // nicht 2n und nicht n*sqrt(2).
        var frei = FreieProbeflaeche(out int kante);
        if (frei == null)
        {
            sb.Append("  ⚠ keine freie Probeflaeche auf dieser Karte gefunden\n");
            // ⚠ »Nicht gemessen« ist NICHT gruen. Bis zum 23.08.2026 ging der
            // Lauf hier mit drei von zehn Messungen durch, und die drei pruefen
            // nur Tafeln — also gerade nicht die Suche.
            return sb.Append("  DURCHGEFALLEN (nicht gemessen ist nicht bestanden)").ToString();
        }
        sb.Append($"  Probeflaeche: {kante}x{kante} frei ab {frei.Value}\n");
        var a0 = frei.Value;
        var b0 = new Vector2I(a0.X + kante, a0.Y + kante);
        var diagWeg = InBounds(b0.X, b0.Y) ? FindPathUr(a0, b0) : null;
        var gerWeg = InBounds(a0.X + kante, a0.Y) ? FindPathUr(a0, new Vector2I(a0.X + kante, a0.Y)) : null;
        if (diagWeg != null && gerWeg != null)
            Sag($"Chebyshev: schraeg {kante} Zellen = {diagWeg.Count} Schritte, "
                + $"gerade {kante} Zellen = {gerWeg.Count} Schritte (erwartet gleich)",
                diagWeg.Count == gerWeg.Count);
        else
            Sag($"Metrik: schraeg {(diagWeg == null ? "KEIN WEG" : "ok")}, gerade "
                + $"{(gerWeg == null ? "KEIN WEG" : "ok")} - beide muessen liefern", false);

        // 3. ⭐⭐ ECKEN WERDEN NICHT GESCHNITTEN. Jeder Schritt des Weges muss
        // ein echter Nachbar sein, UND bei einer Diagonale muessen beide
        // anliegenden Geraden frei sein.
        int ecken = 0, schritte = 0;
        if (diagWeg is { Count: > 1 })
            for (int k = 1; k < diagWeg.Count; k++)
            {
                var v = diagWeg[k] - diagWeg[k - 1];
                schritte++;
                if (Math.Abs(v.X) > 1 || Math.Abs(v.Y) > 1) { ecken++; continue; }
                if (v.X != 0 && v.Y != 0)
                {
                    bool f1 = IsFree(diagWeg[k - 1].X + v.X, diagWeg[k - 1].Y, MoveClass.Vehicle, -1);
                    bool f2 = IsFree(diagWeg[k - 1].X, diagWeg[k - 1].Y + v.Y, MoveClass.Vehicle, -1);
                    if (!f1 || !f2) ecken++;
                }
            }
        Sag($"kein geschnittener Eck- oder Riesenschritt: {schritte} Schritte, "
            + $"{ecken} Verstoesse", ecken == 0);

        // 3b. ⭐⭐ DER ERSTE SCHRITT GEHT VOM START AUS — und das ist die Messung,
        // die am 23.08.2026 gefehlt hat.
        //
        // ⚠⚠ Der Rueckverfolger begann bei der falschen Wellenmarke (marke-1
        // statt marke-2, siehe FindPathUr). Der gelieferte Weg war dadurch in
        // sich SELBST noch stimmig — aufeinanderfolgende Zellen blieben
        // benachbart, Messung 3 oben blieb gruen —, aber er begann eine Welle
        // zu frueh und damit NEBEN dem Start. Gemessen auf map_01:
        //     (4,39) -> (4,35) lieferte »2,38 2,37«, und (4,39) auf (2,38)
        //     sind ZWEI Spalten.
        // Von aussen ist das der gemeldete Sprung: die Einheit setzt beim
        // Losfahren ueber ein paar Felder.
        //
        // ⭐ Die Lehre, und sie gilt ueber diesen Fall hinaus: ein Weg wurde nur
        // gegen SICH SELBST geprueft, nie gegen seine beiden ENDEN. Eine Kette,
        // die in sich stimmt, kann trotzdem woanders anfangen.
        if (diagWeg is { Count: > 0 })
        {
            var d0 = diagWeg[0] - a0;
            bool ersterSchritt = Math.Abs(d0.X) <= 1 && Math.Abs(d0.Y) <= 1 && d0 != Vector2I.Zero;
            bool letzterTrifft = diagWeg[^1] == b0;
            Sag($"erster Schritt liegt neben dem Start: {a0} -> {diagWeg[0]} "
                + $"(Versatz {d0.X},{d0.Y})", ersterSchritt);
            Sag($"letzte Zelle IST das Ziel: {diagWeg[^1]} gegen {b0}", letzterTrifft);
        }

        // 3c. ⭐ Und dasselbe ueber einen LANGEN Weg, quer ueber die Karte —
        // ein kurzer Weg kann eine Verschiebung um eine Welle zufaellig
        // ueberdecken, ein langer nicht.
        {
            var weit = WeitesteFreieZelle(a0);
            var langWeg = weit == null ? null : FindPathUr(a0, weit.Value);
            if (langWeg is not { Count: > 2 })
                Sag($"langer Weg von {a0}: weiteste Zelle "
                    + $"{(weit == null ? "KEINE" : weit.Value.ToString())}, Weg "
                    + $"{(langWeg == null ? "KEINER" : langWeg.Count + " Zellen")} "
                    + "- zu kurz zum Messen", false);
            else
            {
                int bruch = 0;
                var vor = a0;
                foreach (var z in langWeg)
                {
                    var v = z - vor;
                    if (Math.Abs(v.X) > 1 || Math.Abs(v.Y) > 1 || v == Vector2I.Zero) bruch++;
                    vor = z;
                }
                Sag($"langer Weg {a0} -> {weit!.Value}: {langWeg.Count} Zellen, "
                    + $"{bruch} Bruchstellen (Start mitgerechnet)", bruch == 0);
            }
        }

        // 4. Die Grenzen als ZAHLEN -- sie sollen nicht stillschweigend
        // vereinheitlicht werden.
        Sag($"Ringlaenge 1x1 {UrRing1x1} und gross {UrRingGross} sind VERSCHIEDEN "
            + "(im Original zwei Masse auf demselben Puffer)",
            UrRing1x1 == 4096 && UrRingGross == 5000 && UrRing1x1 != UrRingGross);
        Sag($"Wegpuffer {UrWegLaenge} Schritte (Code 0x32, sec14 = 8000 x 50)",
            UrWegLaenge == 50);

        // 5. Gegenprobe gegen unser A*: beide sollen dasselbe ERREICHEN, auch
        // wenn der Weg ein anderer ist.
        if (diagWeg != null)
        {
            var astern = FindPath(a0, b0);
            Sag($"A* findet denselben Zielpunkt: Breitensuche {diagWeg.Count} "
                + $"Schritte, A* {astern?.Count.ToString() ?? "KEINEN"}",
                astern != null);
        }

        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Eine Zelle, um die herum 7x7 alles frei ist — Probeflaeche für
    /// <see cref="WegsucheCheck"/>. ⚠ Ohne freie Flaeche misst der Lauf die
    /// Karte statt das Verfahren.</summary>
    private Vector2I? FreieProbezelle() => FreieProbeflaeche(out _);

    /// <summary>
    /// Die obere linke Ecke des groessten freien Quadrats, das diese Karte
    /// hergibt, und dessen Kantenlaenge in <paramref name="kante"/>.
    ///
    /// <para>⚠⚠ 23.08.2026 — VORHER STAND HIER EINE FESTE 7×7, UND DAS HAT DEN
    /// HALBEN PRUEFSTAND STILLGELEGT. Weder map_01 noch die Gefechtskarte haben
    /// irgendwo sieben mal sieben freie Zellen am Stueck; der Lauf meldete
    /// »keine freie Probeflaeche gefunden — NICHT GEMESSEN« und ging mit drei
    /// von acht Messungen durch. Die drei, die liefen, pruefen nur Tafeln —
    /// also gerade NICHT die Suche. Der Fehler im Rueckverfolger konnte hier
    /// gar nicht auffallen.</para>
    ///
    /// <para>⭐ Die Lehre: ein Pruefstand, der »nicht gemessen« sagt, ist nicht
    /// gruen. Er darf sich seine Probe suchen, aber er darf nicht schweigen —
    /// die benutzte Kantenlaenge steht jetzt in der Ausgabe.</para>
    /// </summary>
    private Vector2I? FreieProbeflaeche(out int kante)
    {
        for (kante = 6; kante >= 2; kante--)
            for (int y = 1; y < Height - kante - 1; y++)
                for (int x = 1; x < Width - kante - 1; x++)
                {
                    bool ok = true;
                    for (int dy = 0; dy <= kante && ok; dy++)
                        for (int dx = 0; dx <= kante && ok; dx++)
                            if (!IsFree(x + dx, y + dy, MoveClass.Vehicle, -1)) ok = false;
                    if (ok) return new Vector2I(x, y);
                }
        kante = 0;
        return null;
    }

    /// <summary>Die von <paramref name="von"/> aus am weitesten entfernte
    /// ERREICHBARE freie Zelle — der laengste Weg, den diese Karte hergibt.
    /// ⚠ Sie wird geflutet und nicht geschaetzt: die Zelle mit dem groessten
    /// Luftabstand ist oft gar nicht erreichbar, und dann misst man nichts.</summary>
    private Vector2I? WeitesteFreieZelle(Vector2I von)
    {
        var gesehen = new bool[Width * Height];
        var welle = new List<Vector2I> { von };
        gesehen[von.Y * Width + von.X] = true;
        Vector2I letzte = von;
        for (int i = 0; i < welle.Count; i++)
        {
            var c = welle[i];
            letzte = c;
            foreach (var d in UrDirs)
            {
                int nx = c.X + d.X, ny = c.Y + d.Y;
                if (!InBounds(nx, ny) || gesehen[ny * Width + nx]) continue;
                if (!IsFree(nx, ny, MoveClass.Vehicle, -1)) continue;
                // ⚠ DIESELBE ECKENREGEL WIE DIE SUCHE. Ohne sie erklaert die
                // Flut Zellen fuer erreichbar, an die die Suche nie kommt —
                // und die Messung wirft der Suche einen Fehler vor, den die
                // Messung selbst gemacht hat.
                if (d.X != 0 && d.Y != 0
                    && (!IsFree(c.X + d.X, c.Y, MoveClass.Vehicle, -1)
                     || !IsFree(c.X, c.Y + d.Y, MoveClass.Vehicle, -1))) continue;
                gesehen[ny * Width + nx] = true;
                welle.Add(new Vector2I(nx, ny));
            }
        }
        return letzte == von ? null : letzte;
    }

    private static readonly Vector2I[] Dirs =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// <summary>Can <paramref name="mover"/> step from a to b (adjacent cells)?</summary>
    /// <summary>GEGENPROBE <c>--no-climb-limit</c>: die Steiglimite abschalten.
    /// <see cref="MaxClimb"/> ist ausdruecklich UNSERE Setzung — »the original
    /// has no such test in Can_go« —, und am 16.08.2026 fiel auf, dass sie auf
    /// map_DM_4 eine Einheit einschliesst: 787 Zellen im Ring 8..30 sind
    /// <see cref="IsFree"/>, und zu keiner einzigen findet <see cref="FindPath"/>
    /// einen Weg. Ein Schalter, der die Setzung im selben Programm wegnimmt,
    /// entscheidet in einem Lauf, ob sie die Ursache ist (Arbeitsweise 31).</summary>
    public static bool ClimbOff;

    /// <summary>Derselbe Schritt-Test, den <see cref="FindPath"/> benutzt — fuer
    /// Pruefstaende, die fluten muessen. ⚠ Ein Pruefstand, der die Regel
    /// NACHBAUT, ist kein Zeuge (Arbeitsweise 24): er muss dieselbe Stelle
    /// fragen.</summary>
    public bool CanStepFor(Vector2I a, Vector2I b, MoveClass mc, int mover)
        => CanStep(a, b, mc, mover);

    private bool CanStep(Vector2I a, Vector2I b, MoveClass mc, int mover)
    {
        // ⚠⚠ ZURUECKGEZOGEN am 16.08.2026, noch am selben Tag: hier stand kurz
        // `Ask(...) != Blocked`, die Wegsuche fuehrte also auch durch Zellen mit
        // anderen Einheiten (`Can_go == 1`, »jemand muss ausweichen«). Die Idee
        // klang zwingend — das Original faehrt dorthin und wartet — war aber
        // ERSCHLOSSEN und nicht gelesen: dass `Can_go` drei Antworten hat, ist
        // gelesen, dass die WEGSUCHE des Originals durch belegte Zellen plant,
        // nicht.
        //
        // Und gemessen war sie schlechter als der Fehler, den sie heilen
        // sollte. map_NET07, gleicher Anker und gleiches Ziel, 60 s:
        // **32 angekommen vorher, 17 danach.** Die Wege liefen durch die Pulks,
        // und die Einheiten standen wartend darin, statt aussen herumzufahren.
        //
        // Der echte Fehler dahinter bleibt und ist eng: wer beim Befehl gar
        // keinen Weg bekommt, bekommt bei uns auch kein Ziel und versucht es nie
        // wieder. Geheilt wird das dort, wo es entsteht — siehe
        // `MapEntityLayer.IssueMove` und `RetryPath`.
        if (!IsFree(b.X, b.Y, mc, mover)) return false;
        if (!ClimbOff && mc != MoveClass.Ship &&
            Math.Abs(ElevAt(b.X, b.Y) - ElevAt(a.X, a.Y)) >= MaxClimb) return false;
        // no cutting a corner between two blocked cells
        if (a.X != b.X && a.Y != b.Y &&
            !(CanEnter(b.X, a.Y, mc) && CanEnter(a.X, b.Y, mc))) return false;
        return true;
    }

    /// <summary>
    /// A* from <paramref name="start"/> to <paramref name="goal"/> (8-way).
    /// Returns the waypoint cells WITHOUT the start cell, or null if unreachable.
    /// If the goal itself is blocked/occupied, the nearest free cell is used.
    /// </summary>
    /// <summary>
    /// ⚠ <b>GEGENPROBE: den alten A* benutzen</b> statt der Breitensuche des
    /// Originals. Nur <c>--alter-astern</c> setzt das.
    ///
    /// <para>Der A* war <b>komplett unsere Erfindung</b> — Kosten, Schätzer,
    /// Prioritätswarteschlange, Steigungsaufschlag. Das Original hat nichts
    /// davon (OFFENE_FRAGEN BB). Er bleibt erreichbar, weil ein Verfahren, das
    /// man nicht mehr gegen sein Vorgängerverfahren halten kann, nicht mehr
    /// nachprüfbar ist.</para>
    /// </summary>
    public static bool AlterAstern;

    /// <summary>
    /// <c>--neue-pfadkarte</c>: die Suchkarte aus <see cref="PfadOffen"/> statt
    /// aus <see cref="IsFree"/> bauen — stehende Einheiten sperren die PLANUNG
    /// dann nicht mehr, so wie Tafel BB.1 es fuer Art 0 beschreibt.
    ///
    /// <para>⚠⚠ <b>STANDARD IST AUS, und das ist eine GEMESSENE Entscheidung
    /// vom 23.08.2026 — zum zweiten Mal.</b> Die Lesung stimmt; der Nachbau ist
    /// trotzdem schlechter, solange ihm das Gegenstueck fehlt. Gemessen auf
    /// map_04 (96 Einheiten dicht gepackt auf 8x14 Zellen, Ziel (12,40),
    /// 120 s):</para>
    ///
    /// <code>
    ///                  Fortschritt  gefahrene Zellen  angekommen  tot
    ///   alte Karte          2,6           2901            5        43
    ///   NEUE Karte          1,6            260            0         3
    ///   nur 50er-Puffer     6,0           3055           13        35
    ///   beides zusammen     1,6            260            0         3
    /// </code>
    ///
    /// <para><b>260 Zellen statt 2901</b> — bei 44 fahrenden Einheiten sind das
    /// rund 6 Zellen in 120 s statt 62. Sie kriechen. Der Grund ist derselbe,
    /// den der Rueckzieher vom 16.08.2026 genannt hat: plant man durch einen
    /// Pulk hindurch, fuehrt fast jeder Weg sofort durch einen Nachbarn, und
    /// dort wartet die Einheit (<see cref="Step.GiveWay"/>) auf eine, die
    /// selbst wartet.</para>
    ///
    /// <para>⚠ <b>Und die niedrige Totenzahl ist KEIN Erfolg, sondern dieselbe
    /// Ursache:</b> wer sich nicht bewegt, kommt nicht in Reichweite. Sie war
    /// beim ersten Hinsehen als Beleg gelesen worden — die Fortschrittszahl hat
    /// das widerlegt.</para>
    ///
    /// <para>⭐ <b>Was fehlt, ist benannt:</b> im Original bleibt ein wartender
    /// Fahrer nicht ewig stehen. Solange bei uns weder ein AUSWEICHEN des
    /// Blockierers noch eine Neuplanung des Wartenden gebaut ist, ist »durch
    /// den Pulk planen« nur eine andere Art steckenzubleiben. Der Schalter
    /// bleibt, damit die naechste Haelfte dagegen gemessen werden kann.</para>
    /// </summary>
    public static bool NeuePfadkarte;

    /// <summary>GEGENPROBE <c>--kein-wegpuffer</c>: den 50-Schritte-Puffer
    /// abschalten und wieder den ganzen Weg zurueckgeben. Die beiden Haelften
    /// muessen sich EINZELN messen lassen — sonst ist nicht zu zeigen, dass
    /// erst ihr ZUSAMMENSPIEL traegt.</summary>
    public static bool KeinWegpuffer;

    /// <summary>Wie oft seit dem Kartenstart welches Verfahren gelaufen ist —
    /// für den Prüfstand.</summary>
    public static int LaeufeUr, LaeufeAstern;

    public List<Vector2I>? FindPath(Vector2I start, Vector2I goal, MoveClass mc = MoveClass.Vehicle,
                                    int mover = -1, int maxNodes = 60000)
    {
        // ⭐⭐ 22.08.2026 — DIE WEGSUCHE IST JETZT DIE DES ORIGINALS.
        // Eine reine 8-Nachbar-Breitensuche mit Wellenmarken, ohne Kosten und
        // ohne Schaetzer (BB). Was darunter liegt, war unsere Erfindung und ist
        // hier nur noch die Gegenprobe.
        if (!AlterAstern)
        {
            LaeufeUr++;
            return FindPathUr(start, goal, mc, mover);
        }
        LaeufeAstern++;
        if (!InBounds(start.X, start.Y) || !InBounds(goal.X, goal.Y)) return null;
        if (!IsFree(goal.X, goal.Y, mc, mover))
        {
            var alt = NearestFree(goal, mc, mover);
            if (alt == null) return null;
            goal = alt.Value;
        }
        if (start == goal) return new List<Vector2I>();

        // ⚠ GANZZAHLIG, in Tausendsteln einer Zelle. Siehe TerrainCostMilli:
        // die Wegsuche ist Teil des Lockstep-Zustands und darf nicht an der
        // Fliesskomma-Einstellung der Maschine hängen. Die Kosten sind
        // dieselben wie vorher, nur ×1000: Diagonale 1414 statt 1,4142,
        // Geröll ×1450/1000 statt ×1,45, eine Höhenstufe 500 statt 0,5.
        var came = new Dictionary<Vector2I, Vector2I>();
        var gScore = new Dictionary<Vector2I, int> { [start] = 0 };
        var open = new PriorityQueue<Vector2I, int>();
        open.Enqueue(start, Heuristic(start, goal));
        var closed = new HashSet<Vector2I>();
        int expanded = 0;

        while (open.Count > 0 && expanded < maxNodes)
        {
            var cur = open.Dequeue();
            if (!closed.Add(cur)) continue;
            expanded++;
            if (cur == goal) return Reconstruct(came, cur);

            foreach (var d in Dirs)
            {
                var nb = new Vector2I(cur.X + d.X, cur.Y + d.Y);
                if (closed.Contains(nb) || !CanStep(cur, nb, mc, mover)) continue;

                // ⚠ 1500, nicht 1414: die Schraege kostet im Original genau das
                // Anderthalbfache (120 gegen 80, siehe StepCostMilli). 1414 war
                // der Euklid und damit eine Schaetzung ueber eine Zahl, die
                // gelesen werden konnte. Der Geroellaufschlag ist HIER
                // MITGEFALLEN: er stand nur da, weil die Bewegung ihn hatte
                // ("rough is slow"), und die Bewegung hat ihn nicht (16.08.2026).
                // Ein Weg, der Geroell meidet, waere sonst ein Umweg ohne Gewinn.
                int step = (d.X != 0 && d.Y != 0) ? 1500 : 1000;
                if (MoveCostOld) step = step * TerrainCostMilli(nb.X, nb.Y, mc) / 1000;
                // ⚠ UNSERE Setzung, ungelesen: das Original ist nicht daraufhin
                // gelesen, ob es Steigungen bepreist. Sie bleibt, weil sie nur
                // die Wahl des Weges faerbt und keine Geschwindigkeit behauptet.
                step += Math.Abs(ElevAt(nb.X, nb.Y) - ElevAt(cur.X, cur.Y)) * 500;  // climbing costs
                int tentative = gScore[cur] + step;
                if (gScore.TryGetValue(nb, out int known) && tentative >= known) continue;
                gScore[nb] = tentative;
                came[nb] = cur;
                open.Enqueue(nb, tentative + Heuristic(nb, goal));
            }
        }
        return null;
    }

    /// <summary>Octile-Schätzung, in Tausendsteln — dieselbe Formel wie vorher,
    /// nur ganzzahlig: (dx+dy)·1000 + (1414−2000)·min(dx,dy).
    /// <para>⚠ Die 1414 steht hier mit Absicht STEHEN, obwohl der Schritt jetzt
    /// 1500 kostet: eine Schaetzung darf unterschaetzen, nie ueberschaetzen.
    /// Mit −586 bleibt sie in beiden Fassungen zulaessig (neu 1500, alt
    /// 1414×Gelaende ≥ 1414) und der gefundene Weg in beiden der kuerzeste;
    /// mit −500 waere sie in der Gegenprobe <c>--old-move-cost</c> zu
    /// optimistisch.</para></summary>
    private static int Heuristic(Vector2I a, Vector2I b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return (dx + dy) * 1000 - 586 * Math.Min(dx, dy);
    }

    private static List<Vector2I> Reconstruct(Dictionary<Vector2I, Vector2I> came, Vector2I cur)
    {
        var path = new List<Vector2I> { cur };
        while (came.TryGetValue(cur, out var prev)) { cur = prev; path.Add(cur); }
        path.RemoveAt(path.Count - 1);   // drop the start cell
        path.Reverse();
        return path;
    }

    /// <summary>Closest free cell to <paramref name="around"/> (spiral search).</summary>
    public Vector2I? NearestFree(Vector2I around, MoveClass mc = MoveClass.Vehicle,
                                 int mover = -1, int maxRadius = 12)
    {
        if (IsFree(around.X, around.Y, mc, mover)) return around;
        for (int rad = 1; rad <= maxRadius; rad++)
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != rad) continue;
                    int c = around.X + dx, r = around.Y + dy;
                    if (IsFree(c, r, mc, mover)) return new Vector2I(c, r);
                }
        return null;
    }
}
