using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// BESETZEN — taking a building by standing at its door.
///
/// This is the mechanic the NET maps are built around, and the reason the
/// 0.4.0 play test found the shipped troops useless: those maps carry 12 to 42
/// factories and 4 to 8 bases, <b>all of them neutral (owner 11)</b>, and the
/// units they hand you are the tool for taking them. The remake did not know
/// the mechanic, so there was nothing to do with either.
///
/// <para><b>All of the following is the original's, read off the building tick
/// @0x43CA50.</b> The record base is 0xc06910, which is the sec3 record's own
/// +0x00, so the addresses below translate to file offsets one for one.</para>
///
/// <list type="bullet">
/// <item><b>Two gates</b> (@0x43CB29..0x43CB3F): <c>byte[+0x18]</c> — "this
/// entry is a real building", 1 on all 684 entries of type 1..16 and 0 on every
/// type from 17 up, no counterexample over the 893 entries of the 23 levels —
/// and <c>byte[+0x34]</c>, the door count. 519 buildings can be taken; types 8,
/// 11 (Hafen), 13 and 14 cannot.</item>
///
/// <item><b>The duration is the building's hit points.</b> @0x43CB45 reads
/// <c>dx = word[+0x06]</c> (hp) against <c>di = word[+0x38]</c> (the stored
/// total); where they differ the progress at +0x3a is rescaled
/// <c>progress * hp / oldTotal</c> and the total is set to hp. So a damaged
/// building falls faster, and nothing here is a rate of ours — which retires
/// the open question the handoff carried ("the duration is set by a routine
/// that has not been read").</item>
///
/// <item><b>The door</b> (@0x43CB4C..0x43CB67): <c>col + byte[+0x35]</c>,
/// <c>row + byte[+0x36]</c>. Constant per type over all 798 doors — Basis
/// (4,2) 73 of 73, the three factories two each at (2,3) and (5,3), Kaserne
/// (2,3) 12 of 12, Flughafen (5,4) 39 of 39, Mine (5,3) 49 of 49, type 12
/// (1,3) 36 of 36, type 15 (2,4) 10 of 10, Werft (2,3) 13 of 13. Doors are
/// <b>three bytes each</b> from +0x35 and the capture uses door 0 only.</item>
///
/// <item><b>And the door is a door.</b> Its third byte is a state, 0 on all 798
/// records because it is a runtime field, and @0x43D2EC runs it: at 0 (shut)
/// the door starts opening the moment its imap cell holds anything below 14000
/// — a unit standing in it — and from 0x84 (open) it starts shutting again once
/// the cell is empty. The building draw @0x42B20D..0x42B28A picks the frame
/// from it, and a building with a capture running is blitted through a
/// different call (0x4012f8 instead of 0x401348). That is why a unit belongs ON
/// the door tile: production puts a new unit exactly there
/// (@0x410441..@0x41047C writes the spawned entity's cell as
/// <c>col + door_col</c>, <c>row + door_row</c>), and @0x409B50 tests that same
/// tile when a unit is sent to a building. ⚠ The note that used to stand here —
/// "the remake bakes its buildings into the map picture and has no door frames,
/// so the animation is not reproduced" — is out of date on both counts: the door
/// frames come out of ANIM.CWA and the buildings are no longer baked, they are
/// drawn from their pattern (MapEntityLayer.DrawBuildingBody).</item>
///
/// <item><b>The intruder</b> (@0x43CBEF..0x43CC23): the imap cell holds a slot
/// below 8000, and the player is <c>slot / 1000</c>. Same owner as the
/// building, or nobody: the routine leaves through @0x43D265, and <b>that
/// branch is not empty</b> — it forgets the intruder (+0x3c back to 0xFF),
/// opens the door again (imap back to 0xFFFE), puts the shown owner back to the
/// real one, and <b>counts the progress DOWN by one</b> (@0x43D29E). Filling is
/// +1 per tick and draining is −1 per tick, so a besieger who walks away loses
/// his ground at exactly the rate he won it.
/// <para>⚠ This corrects the first reading of the same day, which said "the
/// progress is not cleared — the routine simply leaves". That was read off a
/// jump into code I had not disassembled, and it was wrong. It was found by
/// <c>shipre.py xref</c>, the raw dword scan: <c>disx2.py xref</c> reported one
/// single reference to +0x3a and the raw scan found twelve. Rule 7, again.</para></item>
///
/// <item><b>An ALLY takes too — and that is the original.</b> Read
/// 10.08.2026, after the engine was seen letting player 1 take back and then
/// destroy the factories player 0 had just handed him while the two were
/// allied. The suspicion was that the test above is too narrow. It is not.
/// <para>The alliance matrix is <c>byte[BASE + a*40 + b]</c> — the alliance ROW
/// sits at +0x15 of the 40-byte player plate, so the stride is 40, not 8; the
/// writer <c>set_relation</c> shows it in one breath: <c>lea eax,[edx+edx*4]</c>
/// then <c>mov byte [ebx+eax*8+BASE], cl</c> and the mirrored pair. BASE is
/// 0x87b155 in the aekernel EXE and 0x87a1b5 in the F: one, and neither address
/// is assumed here: <c>capture_re.py</c> finds them by that FORM (opcode 0x88,
/// modrm mod=10/rm=100, SIB scale *8) and exactly one displacement in the data
/// range carries it in each file.</para>
/// <para><b>The building tick never reads that matrix.</b> A raw dword scan
/// over the whole 8×40 plate range finds <b>207</b> occurrences in .text of the
/// aekernel EXE and <b>205</b> in the F: one — and <b>0 of them, in either
/// file, lie inside the capture routine</b> (0x43CA50..0x43ED34 there,
/// 0x43BAF0..0x43DD4C here; the F: address comes from the fingerprint, not from
/// arithmetic). The only two touches of a player plate anywhere in the routine
/// are <c>byte[plate+0x00]</c> at @0x43CFF8 / @0x43DB7F, which is the parts
/// bonus gate below, not a relation.</para>
/// <para>And the test itself is plain equality, the same instructions in both
/// files: aekernel @0x43CC15 <c>mov al, byte[esi+0xc06915]</c> (the owner) /
/// @0x43CC21 <c>cmp eax, edi</c> / @0x43CC23 <c>je 0x43d265</c>; F:
/// @0x43BCB5 / @0x43BCC1 / @0x43BCC3 <c>je 0x43c28c</c>. The gate before it
/// (<c>cmp ax, 0x1f40</c>) only asks that the imap cell hold a UNIT slot, and
/// all 8 players' slots are below 8000. So there is no step before that sorts
/// allied units out either — nobody issues a "capture" order at all; a unit
/// simply stands on a tile and the building's own tick notices.</para>
/// <para><b>So the observation is withdrawn, and the narrow test stays.</b>
/// Widening it to <c>Allied(u.Owner, b.Owner)</c> would be OUR rule, not the
/// game's. <c>--capture-check</c> now drives the allied case on purpose and
/// prints what happened, so this does not have to be argued again.</para>
/// <para>⚠ What this does NOT settle, and it is where the reported game went
/// wrong: whether the computer player should <i>choose</i> an ally's building.
/// <c>SkirmishAi.AiGrab</c> picks its prize with the same equality test
/// (<c>b.Owner == a.Player</c>), so it walks to an ally's door of its own
/// accord. The original's AI round has 21 sub-tasks and none of them names
/// itself for taking buildings; its unit scan <c>ai_units</c> does skip allies
/// explicitly (<c>byte[BASE + 40*player + slot/1000] != 0 -> next</c>), which
/// shows the AI asks the matrix where the tick does not. Deciding that is
/// SkirmishAi.cs's business, not this file's.</item>
///
/// <item><b>While it runs:</b> sound 132 and "Ihre Basis wird besetzt" (the
/// string family 0x4faef8 + n·0x64, picked by type through 0x43ebec/0x43ebd0 —
/// only types 1, 2, 3, 4, 9 and 16 say anything), and only to the owner
/// (@0x43CC32, @0x43CC68) and only on the first tick or when the intruder
/// changes (@0x43CC3E). The displayed owner at +0x3d flickers between old and
/// new: <c>(progress*10)/total</c> against <c>clock % 11</c>
/// (@0x43CD12..0x43CD51).</item>
///
/// <item><b>Done</b> when the progress reaches the total (@0x43CD57): sound 134
/// to the old owner (@0x43CED6), <c>byte[+0x01] := intruder</c> (@0x43CF83),
/// sound 133 to the new one (@0x43D068). A Werft-Station (type 16) drags its
/// Hafen along — the routine looks up the type-11 dock whose sec29 +0x02 names
/// this Werft and changes its owner too, printing "Error in shipyard" when
/// there is none (@0x43CEEC..0x43CF99).</item>
/// </list>
///
/// <para><b>What is OURS, and it is three things.</b></para>
///
/// <para>(1) <b>Both cells of every door count.</b> The original reads the imap
/// one element PAST door 0 — <c>[ecx+0xbdea82]</c> @0x43CBEF, which in the
/// column-major imap is (col, row+1) — while it stamps the door itself
/// (<c>[ecx+0xbdea80]</c> @0x43CC29). Everything else about the door says the
/// unit belongs IN it, so the +1 reads like an off-by-one in the original; but
/// I cannot prove that, so both cells count here. And a factory has two doors
/// while the capture block only ever looks at the first, which a player driving
/// to the nearer one would experience as a dead end — so both doors count as
/// well. Door 0's front cell is tested first, so wherever the original would
/// fire, this fires on the same cell.
/// <see cref="CaptureWatchLine"/> counts the ticks per cell kind, which is how
/// the question can be settled from a real game rather than argued about.</para>
///
/// <para>(2) <b>The door tile is not stamped.</b> The original writes 0xFFFF
/// into it while the capture runs and frees it again from a second counter
/// (<c>byte[+0x0a]</c> counts to 250 @0x43CAC5 and then writes 0xFFFE back).
/// That counter is not modelled here, and stamping without the release would
/// block the doorway for good — so the cell is left alone. An honest gap.</para>
///
/// <para>(3) <b>The parts bonus is not paid.</b> @0x43CFF5 gives every Basis of
/// the capturing player +100 at +0x2e and +0x30 (the Fahrwerk and Spezial
/// stores), gated on <c>dword[0x539234]</c> and on the player table's +0x00 —
/// neither of which is identified. Documented, not implemented.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Building types that announce being taken. The index table
    /// @0x43ebec sends every other type to the no-message case @0x43CD02.</summary>
    private static string? CaptureMessage(int bType) => bType switch
    {
        1 => "Ihre Basis wird besetzt",            // 0x4faef8
        2 => "Ihre Waffenfabrik wird besetzt",     // 0x4faf5c
        3 => "Ihre Fahrwerkfabrik wird besetzt",   // 0x4fafc0
        4 => "Ihre Spezialfabrik wird besetzt",    // 0x4fb024
        9 => "Ihr Flughafen wird besetzt",         // 0x4fb088
        16 => "Ihre Schiffswerft",                 // 0x4fb0ec, and that IS the
                                                   // whole string in the file
        _ => null,
    };

    /// <summary>Taken while it was being taken — sound 134 @0x43CED6 goes to the
    /// old owner, 133 @0x43D068 to the new one.</summary>
    private const int SoundTakenFrom = 134, SoundTakenBy = 133;

    // counters for the watch line, so the reading above can be checked against
    // a running game rather than argued about
    private int _capDoors, _capOnDoor, _capBelowDoor, _capDone;
    /// <summary>Ticks in which a progress counted DOWN again — the drain at
    /// @0x43D29E, which the first reading of it had missed.</summary>
    private int _capDrain;

    /// <summary>What the panel calls an owner. 11 is the neutral slot the map
    /// files use for the civilian structures.</summary>
    private static string OwnerWord(int owner)
        => owner == 11 ? "NEUTRAL" : owner < 0 ? "-" : "SPIELER " + owner;

    /// <summary>Can this building be taken at all — the original's two gates.</summary>
    private static bool Capturable(Entity b)
        => b.IsBuilding && !b.IsProp && !b.Dead && b.Built != 0 && b.Doors != 0;

    /// <summary>Door 0 — the one the capture block uses — and the tile the
    /// original actually looks at, one row further south.</summary>
    private static (Vector2I Door, Vector2I Front) CaptureCells(Entity b)
    {
        var door = new Vector2I(b.Col + b.DoorCol, b.Row + b.DoorRow);
        return (door, new Vector2I(door.X, door.Y + 1));
    }

    /// <summary>Every cell that counts as "at a door" here: for each of the
    /// building's doors its own tile and the tile in front of it.
    ///
    /// OURS in two ways, both deliberate and both counted in
    /// <see cref="CaptureWatchLine"/>. The original looks at exactly ONE cell —
    /// door 0's, plus one row (see the class note) — but a factory has two
    /// doors and a player will drive to whichever is nearer, so ignoring the
    /// second one would be a dead end he could not see. Door 0 stays first in
    /// the list, so where the original would fire, we fire on the same cell.</summary>
    private static IEnumerable<Vector2I> CaptureWatchCells(Entity b)
    {
        var cells = b.DoorCells;
        if (cells.Count == 0)
        {
            var (d, f) = CaptureCells(b);
            yield return f; yield return d;
            yield break;
        }
        foreach (var (dc, dr) in cells)
        {
            yield return new Vector2I(b.Col + dc, b.Row + dr + 1);   // the game's cell
            yield return new Vector2I(b.Col + dc, b.Row + dr);       // the doorway
        }
    }

    /// <summary>The player of the unit standing on a cell, or -1. Buildings and
    /// props do not count, and neither does a dead one.</summary>
    private int StanderAt(Vector2I cell)
    {
        if (_nav == null) return -1;
        int i = _nav.OccupantAt(cell.X, cell.Y);
        if (i < 0 || i >= _entities.Count) return -1;
        var e = _entities[i];
        if (e.IsBuilding || e.IsProp || e.Dead) return -1;
        return e.Owner is >= 0 and <= 7 ? e.Owner : -1;
    }

    /// <summary>
    /// One tick of the capture, called from the building tick in
    /// <c>UpdateEconomy</c>. <paramref name="ticks"/> is how many of the
    /// original's ticks this one stands for (<c>TickScale</c>), which is what
    /// keeps the duration in the game's own units: a 440 hp base takes 440
    /// original ticks.
    /// </summary>
    private void CaptureTick(int index, Entity b, int ticks)
    {
        if (!Capturable(b)) return;

        // the total is the building's hit points, and a change to them rescales
        // what has been achieved so far (@0x43CB70..0x43CBA1)
        if (b.CaptureTotal != b.Hp)
        {
            b.CaptureProgress = b.CaptureTotal <= 0
                ? 0 : b.CaptureProgress * b.Hp / b.CaptureTotal;
            b.CaptureTotal = b.Hp;
        }
        if (b.CaptureTotal <= 0) return;

        int who = -1;
        bool onFront = false;
        int n = 0;
        foreach (var cell in CaptureWatchCells(b))
        {
            int p = StanderAt(cell);
            // "meins" — NICHT "befreundet". Das ist das Original und keine
            // Nachlaessigkeit: die Einnahme-Routine fragt die Buendnismatrix
            // ueberhaupt nicht (0 von 207 bzw. 0 von 205 Vorkommen liegen in
            // ihr, siehe Klassenkopf), und ihr einziger Eignertest ist derselbe
            // Gleichheitsvergleich @0x43CC21 / F: @0x43BCC1. Ein Verbuendeter
            // nimmt also auch ein. `--capture-check` faehrt den Fall vor.
            if (p >= 0 && p != b.Owner) { who = p; onFront = (n % 2) == 0; break; }
            n++;
        }
        if (who < 0)
        {
            // Nobody, or only the owner's own units. @0x43D265: the intruder is
            // forgotten, the door is opened again, the shown owner goes back to
            // the real one — and the progress COUNTS DOWN, one per tick.
            // ⚠ This corrects what shipped a few hours ago: "the routine leaves
            // without clearing the progress" was read off a jump into unread
            // code. It is unread no longer — a besieger who walks away loses
            // his ground again at the same rate he won it.
            if (b.Intruder >= 0) { b.Intruder = -1; b.ShownOwner = b.Owner; }
            if (b.CaptureProgress > 0)
            {
                b.CaptureProgress = Mathf.Max(0, b.CaptureProgress - ticks);
                _capDrain++;
            }
            return;
        }
        if (onFront) _capBelowDoor++; else _capOnDoor++;

        // the announcement: to the owner, on the first tick or when a different
        // player takes over the doorway (@0x43CC3E..0x43CC78)
        if (b.CaptureProgress == 0 || b.Intruder != who)
        {
            if (b.Owner == ViewPlayer)
            {
                Audio.GameSounds.Play(Audio.GameSounds.BuildingCaptured);
                string? msg = CaptureMessage(b.BType);
                if (msg != null) { _order = msg; NoteEvent(b, msg); }
            }
            b.Intruder = who;
        }

        b.CaptureProgress += ticks;
        if (b.CaptureProgress < b.CaptureTotal)
        {
            // the displayed owner flickers between the two while it runs
            // @0x43CD3F: at or above the beat it shows the intruder (+0x3c),
            // below it the owner (+0x01). The beat is the original's frame
            // counter word[0x4fa248] % 11; ours is the simulation clock, which
            // is the one substitution here and only changes how fast it blinks.
            int tenths = b.CaptureProgress * 10 / b.CaptureTotal;
            int beat = (int)(DebugClock * 10) % 11;
            b.ShownOwner = tenths >= beat ? who : b.Owner;
            QueueRedraw();
            return;
        }

        CaptureDone(index, b, who);
    }

    /// <summary>The building changes hands.</summary>
    private void CaptureDone(int index, Entity b, int who)
    {
        int old = b.Owner;
        if (old == ViewPlayer) Audio.GameSounds.Play(SoundTakenFrom);

        Hand(b, who);
        // a Werft-Station takes its Hafen with it: the dock whose sec29 +0x02
        // names this Werft (@0x43CEEC..0x43CF99, "Error in shipyard" when there
        // is none). Our dock keeps that link in Shipyard, as a building slot.
        if (b.BType == 16)
        {
            var dock = _entities.Find(x => x.IsBuilding && !x.Dead &&
                                           x.BType == 11 && x.Shipyard == b.Slot);
            if (dock != null) Hand(dock, who);
        }

        if (who == ViewPlayer) Audio.GameSounds.Play(SoundTakenBy);
        _capDone++;

        string what = BuildingTypeName(b.BType);
        string line = b.Name.Length > 0 ? $"{b.Name}: {what} besetzt" : $"{what} besetzt";
        _order = line;
        NoteEvent(b, line);
        // the loser hears about it too, and NoteEvent only files the viewer's own
        if (old == ViewPlayer) _order = $"{what} verloren";
        QueueRedraw();
        if (_selected == index) UpdatePanel();
    }

    private static void Hand(Entity b, int who)
    {
        b.Owner = who;
        b.Team = who;
        b.ShownOwner = who;
        b.CaptureProgress = 0;
        b.Intruder = -1;
    }

    /// <summary>A bar over every building that is being taken, in the colour of
    /// the player taking it, plus a mark on the door tile. Called at the end of
    /// <c>_Draw</c>.</summary>
    private void DrawCaptureBars()
    {
        foreach (var b in _entities)
        {
            if (!Capturable(b) || b.CaptureProgress <= 0 || b.CaptureTotal <= 0) continue;
            float fr = Mathf.Clamp((float)b.CaptureProgress / b.CaptureTotal, 0, 1);
            var at = b.Pos + new Vector2(-TileW / 2f, -TileH / 2f - 16);
            DrawRect(new Rect2(at - new Vector2(1, 1), new Vector2(TileW + 2, 6)),
                     new Color(0, 0, 0, 0.8f));
            var c = OwnerColor(b.Intruder);
            DrawRect(new Rect2(at, new Vector2(TileW * fr, 4)), c);

            var (door, front) = CaptureCells(b);
            DrawRect(new Rect2(CellCenter(door.X, door.Y) - new Vector2(3, 2),
                               new Vector2(6, 4)), new Color(c.R, c.G, c.B, 0.55f));
            DrawRect(new Rect2(CellCenter(front.X, front.Y) - new Vector2(3, 2),
                               new Vector2(6, 4)), new Color(c.R, c.G, c.B, 0.35f));
        }
    }

    /// <summary>Where the doors are, for the harness and the overlay.</summary>
    public List<(Entity B, Vector2I Door, Vector2I Front)> CaptureDoors()
    {
        var list = new List<(Entity, Vector2I, Vector2I)>();
        foreach (var e in _entities)
        {
            if (!Capturable(e)) continue;
            var (d, f) = CaptureCells(e);
            list.Add((e, d, f));
        }
        return list;
    }

    /// <summary>
    /// <c>--door-check</c> — <b>kommt man an die Tür überhaupt heran?</b>
    ///
    /// <para>Zwei Meldungen des Spielers zeigen in dieselbe Richtung und wurden
    /// bisher nur am Regelwerk beantwortet: <b>C9</b> »Werft und Seedock lassen
    /// sich nicht einnehmen, nur Angreifen kann man sie« und <b>C11</b> »Von KI
    /// eingenommene Gebäude kann man nicht einnehmen, nur zerstören«. Die
    /// Einnahme selbst ist gelesen und gebaut; sie verlangt nur eines, nämlich
    /// dass eine fremde Einheit auf einer Türzelle STEHT. Ob das auf diesen
    /// Karten überhaupt möglich ist, hat noch niemand gefragt.</para>
    ///
    /// <para>Gefragt wird mit dem Test des Gegenstands, nicht mit einem
    /// ähnlichen: <see cref="Simulation.NavGrid.Ask"/> — dieselbe Auskunft, die
    /// auch die Wegsuche benutzt. Ein Prüfstand, der mit <c>IsFree</c> flutet
    /// und mit <c>CanStep</c> läuft, misst etwas anderes als die Sache
    /// (Arbeitsweise O).</para>
    ///
    /// <para>Aufgeschlüsselt nach GEBÄUDEART, weil die Meldung eine über Arten
    /// ist. Eine Gesamtquote würde die Werft in 200 Fabriken verstecken.</para>
    /// </summary>
    public string DoorCheckLine()
    {
        if (_nav == null) return "door-check: kein Gitter";
        // je Bauart: wieviele Gebaeude, wieviele mit erreichbarer Tuer, und
        // woran es sonst liegt
        var total = new Dictionary<int, int>();
        var ok = new Dictionary<int, int>();
        var blocked = new Dictionary<int, int>();
        var noDoor = new Dictionary<int, int>();
        var examples = new List<string>();

        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            int t = b.BType;
            total[t] = total.GetValueOrDefault(t) + 1;
            if (!Capturable(b))
            {
                // Nicht einnehmbar heisst hier: keine Tuer (Doors == 0) oder
                // nicht fertig gebaut. Beides ist eine Aussage des ORIGINALS
                // ueber diese Bauart, kein Fehler von uns.
                noDoor[t] = noDoor.GetValueOrDefault(t) + 1;
                continue;
            }
            bool reach = false;
            string why = "";
            foreach (var cell in CaptureWatchCells(b))
            {
                var ans = _nav.Ask(cell.X, cell.Y, Simulation.NavGrid.MoveClass.Vehicle, -1);
                if (ans != Simulation.NavGrid.Step.Blocked) { reach = true; break; }
                if (why.Length == 0) why = $"({cell.X},{cell.Y}) gesperrt";
            }
            if (reach) ok[t] = ok.GetValueOrDefault(t) + 1;
            else
            {
                blocked[t] = blocked.GetValueOrDefault(t) + 1;
                if (examples.Count < 6)
                    examples.Add($"   {BuildingTypeName(t)} slot {b.Slot} auf " +
                                 $"({b.Col},{b.Row}) Besitzer {b.Owner}: {why}");
            }
        }

        var sb = new System.Text.StringBuilder("door-check: je Bauart — " +
            "»Tuer erreichbar« heisst, eine Fahrzeugklasse darf auf mindestens " +
            "eine Tuerzelle treten\n");
        foreach (int t in new SortedSet<int>(total.Keys))
        {
            if (t > 17) continue;
            int n = total[t], o = ok.GetValueOrDefault(t),
                bl = blocked.GetValueOrDefault(t), nd = noDoor.GetValueOrDefault(t);
            sb.Append($"   Typ {t,2} {BuildingTypeName(t),-18} {n,3} Stueck: " +
                      $"{o,3} erreichbar, {bl,3} zugestellt, {nd,3} ohne Tuer\n");
        }
        foreach (string e in examples) sb.Append(e).Append('\n');
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// <c>--capture-enemy-check</c> — <b>lässt sich ein FEINDLICHES Gebäude
    /// einnehmen?</b> Die Gegenprobe zu C9 und C11.
    ///
    /// <para>⚠ Warum es diesen Prüfstand braucht und <c>--capture-check</c>
    /// nicht reicht: der prüft die Einnahme an einem NEUTRALEN Gebäude, und
    /// genau dort hat sie immer funktioniert. Die Meldung war eine über
    /// FEINDLICHE — ein Prüfstand, der den Fall nicht herstellt, um den es
    /// geht, kann ihn nicht sehen (Arbeitsweise 9).</para>
    ///
    /// <para>Er stellt ihn her: ein einnehmbares Gebäude eines ANDEREN Spielers,
    /// eine eigene fahrende Einheit direkt daneben gesetzt, dann der
    /// Einnahmebefehl über den KLICKWEG (<c>PostCapture</c>), nicht daran
    /// vorbei.</para></summary>
    public string CaptureEnemyOrder()
    {
        if (_nav == null) return "capture-enemy-check: kein Gitter";
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        // ein einnehmbares Gebaeude, das einem ANDEREN SPIELER gehoert (nicht
        // neutral, nicht uns) — das ist der gemeldete Fall
        Entity? target = null;
        foreach (var b in _entities)
        {
            if (!Capturable(b) || b.Owner == me) continue;
            if (b.Owner is < 0 or > 7) continue;          // 11 = neutral, zaehlt nicht
            target = b; break;
        }
        if (target == null)
        {
            // Keins da? Dann eins herstellen UND ES SAGEN — sonst misst der Lauf
            // die Kartenlage statt die Sache.
            foreach (var b in _entities)
                if (Capturable(b) && b.Owner != me)
                {
                    target = b;
                    b.Owner = b.Team = b.ShownOwner = me == 0 ? 1 : 0;
                    _capEnemyNote = $"(neutrales {BuildingTypeName(b.BType)} slot {b.Slot} " +
                                    $"auf Spieler {b.Owner} gesetzt, damit es FEINDLICH ist) ";
                    break;
                }
        }
        if (target == null) return "capture-enemy-check: kein einnehmbares Gebaeude";

        // eine eigene fahrende Einheit — notfalls eine fremde uebernehmen
        int ui = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead || !u.Mobile) continue;
            if (u.Owner == me) { ui = i; break; }
        }
        if (ui < 0)
            for (int i = 0; i < _entities.Count; i++)
            {
                var u = _entities[i];
                if (u.IsBuilding || u.IsProp || u.Dead || !u.Mobile) continue;
                ui = i; u.Owner = u.Team = me;
                _capEnemyNote += $"(Einheit slot {u.Slot} fuer die Probe uebernommen) ";
                break;
            }
        if (ui < 0) return $"capture-enemy-check: {_capEnemyNote}keine fahrende Einheit";

        // ⚠⚠ 17.08.2026 — HIER STAND `FindFreeNear(door, 2)`, UND DER PRÜFSTAND
        // HAT SICH DAMIT SELBST BELOGEN. Zwei Zellen um die Tür herum sind zum
        // Teil SELBST Einnahmezellen (CaptureWatchCells gibt je Tür die Türzelle
        // UND die davor). Die Einheit stand also schon auf dem Ziel, bevor
        // irgendein Befehl abgesetzt war — und die Gegenprobe mit dem ALTEN
        // Angriffsweg meldete prompt ebenfalls »EINGENOMMEN«. Ein Prüfstand, der
        // seine Voraussetzung herstellt, prüft nicht die Mechanik, sondern seine
        // eigene Vorbereitung (Arbeitsweise 11).
        //
        // Jetzt steht sie WEIT weg und muss fahren, und es wird nachgesehen,
        // dass sie zu Beginn auf KEINER Einnahmezelle steht — wer eine Bedingung
        // stellt, muss sie danach lesen (Arbeitsweise L).
        var watch = new HashSet<Vector2I>();
        foreach (var c in CaptureWatchCells(target)) watch.Add(c);
        Vector2I door = new(-1, -1);
        foreach (var c in CaptureWatchCells(target)) { door = c; break; }

        var far = FindFreeRing(door, 6, 10, watch);
        if (far.X < 0)
            return $"capture-enemy-check: {_capEnemyNote}keine freie Zelle 6..10 Felder " +
                   "von der Tuer — auf dieser Karte nicht messbar";
        {
            var u = _entities[ui];
            _nav.SetOccupant(u.Col, u.Row, -1);
            u.Col = far.X; u.Row = far.Y;
            u.Pos = CellCenter(u.Col, u.Row);
            u.Path = null; u.Target = -1;
            _nav.SetOccupant(u.Col, u.Row, ui);
        }
        {
            var u = _entities[ui];
            if (watch.Contains(new Vector2I(u.Col, u.Row)))
                return "capture-enemy-check: Einheit steht schon auf einer Einnahmezelle " +
                       "— der Lauf wuerde seine eigene Vorbereitung messen";
        }

        // ⚠⚠ UNVERWUNDBAR FÜR DIE PROBE, und das ist kein Schummeln, sondern das
        // Wegnehmen eines FREMDEN Störfaktors: eine Basis wehrt sich, und die
        // Einnahme dauert so viele Takte, wie sie Trefferpunkte hat. In den
        // ersten Läufen starb die Einheit beide Male — auf DM_4 schon unterwegs,
        // auf NET02 nach der Ankunft —, und »NICHT eingenommen« hätte damit
        // nichts über den BEFEHL gesagt.
        // ⚠ Es steht in der Ausgabe, weil ein Prüfstand sagen muss, was er an
        // seinem Gegenstand verändert hat.
        MapEntityLayer.CheatGodMode = true;
        _capEnemyNote += "(Einheit unverwundbar gesetzt, sonst erschiesst das " +
                         "Gebaeude sie vor Ablauf der Einnahme) ";

        _capEnemyTarget = _entities.IndexOf(target);
        _capEnemyOwner0 = target.Owner;
        _capEnemyUnit = ui;
        _capEnemyDoor = door;
        _capEnemyStart = new Vector2I(_entities[ui].Col, _entities[ui].Row);
        _sel.Clear(); _sel.Add(ui); SetPrimary();
        // ⚠ GEGENPROBE `--capture-by-attack`: derselbe Lauf, aber über den Weg
        // von VORHER (Rechtsklick ohne Strg = Angriff). Ohne sie ist »jetzt geht
        // es« nicht von »ging schon immer« zu unterscheiden.
        bool posted = CaptureByAttack ? PostAttack(target.Pos) : PostCapture(target.Pos);
        return $"capture-enemy-check: {_capEnemyNote}{BuildingTypeName(target.BType)} " +
               $"slot {target.Slot} gehoert Spieler {target.Owner}, " +
               $"Einheit slot {_entities[ui].Slot} steht auf ({_entities[ui].Col}," +
               $"{_entities[ui].Row}), Tuer ({door.X},{door.Y}) — " +
               $"Einnahmebefehl {(posted ? "abgesetzt" : "ABGELEHNT")}: {_order}";
    }

    /// <summary><c>--capture-by-attack</c> — der Weg von vor dem 17.08.2026.</summary>
    public static bool CaptureByAttack;

    private int _capEnemyTarget = -1, _capEnemyOwner0 = -1;
    private string _capEnemyNote = "";

    /// <summary>Die Abrechnung dazu.
    ///
    /// <para>⚠ Sie sagt, WELCHES GLIED nicht schliesst, und das ist der ganze
    /// Unterschied zu einer Zeile, die nur »nicht eingenommen« meldet: eine
    /// Kette, die nicht schliesst, ist sonst nicht von einer zu unterscheiden,
    /// die es fast tut (Arbeitsweise 9). Berichtet wird deshalb, wo die Einheit
    /// steht, ob sie noch einen Weg hat, ob sie überhaupt losgekommen ist und
    /// wie weit sie noch von der Tür weg ist.</para></summary>
    public string CaptureEnemyResult()
    {
        if (_capEnemyTarget < 0) return "capture-enemy-check: nicht gemessen";
        var b = _entities[_capEnemyTarget];
        bool won = b.Owner == (ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0);
        string wo = "Einheit weg";
        if (_capEnemyUnit >= 0 && _capEnemyUnit < _entities.Count)
        {
            var u = _entities[_capEnemyUnit];
            var watch = new HashSet<Vector2I>();
            foreach (var c in CaptureWatchCells(b)) watch.Add(c);
            float d = new Vector2(_capEnemyDoor.X - u.Col, _capEnemyDoor.Y - u.Row).Length();
            wo = $"Einheit slot {u.Slot} startete auf ({_capEnemyStart.X},{_capEnemyStart.Y}), " +
                 $"steht auf ({u.Col},{u.Row}), " +
                 $"{(u.Dead ? "TOT" : u.Path != null ? $"faehrt noch (Weg {u.Path.Count})" : "steht")}, " +
                 $"{d:0.0} Felder von der Tuer, " +
                 $"{(watch.Contains(new Vector2I(u.Col, u.Row)) ? "AUF einer Einnahmezelle" : "nicht auf der Einnahmezelle")}" +
                 $", Sprit {u.Fuel}/{u.FuelMax}, Ziel {u.Target}" +
                 $", bewegt hat sie sich {(u.Col != _capEnemyStart.X || u.Row != _capEnemyStart.Y ? "JA" : "NEIN")}";
        }
        return $"capture-enemy-check: {BuildingTypeName(b.BType)} slot {b.Slot} " +
               $"gehoerte Spieler {_capEnemyOwner0}, gehoert jetzt Spieler {b.Owner} " +
               $"(Fortschritt {b.CaptureProgress}/{b.CaptureTotal}, Trefferpunkte " +
               $"{b.Hp}/{b.HpMax}) — {(won ? "EINGENOMMEN" : "NICHT eingenommen")}\n" +
               $"   {wo}";
    }

    private int _capEnemyUnit = -1;
    private Vector2I _capEnemyStart, _capEnemyDoor;

    /// <summary>Eine freie Zelle zwischen <paramref name="min"/> und
    /// <paramref name="max"/> Feldern, die keine Einnahmezelle ist — und von der
    /// aus die Tür auch WIRKLICH erreichbar ist.
    ///
    /// <para>⚠ Die Wegprüfung gehört dazu: eine Zelle jenseits eines Wassers
    /// wäre »weit weg« und würde einen Fehlschlag melden, der nichts mit dem
    /// Einnahmebefehl zu tun hat. Gefragt wird mit <c>FindPath</c>, also mit dem
    /// Test des Gegenstands.</para></summary>
    private Vector2I FindFreeRing(Vector2I at, int min, int max, HashSet<Vector2I> avoid)
    {
        if (_nav == null) return new Vector2I(-1, -1);
        for (int r = min; r <= max; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var c = new Vector2I(at.X + dx, at.Y + dy);
                    if (avoid.Contains(c) || !_nav.IsFree(c.X, c.Y)) continue;
                    var p = _nav.FindPath(c, at, Simulation.NavGrid.MoveClass.Vehicle, -1);
                    if (p != null && p.Count > 0) return c;
                }
        return new Vector2I(-1, -1);
    }

    /// <summary>
    /// <c>--demo-capture</c>: send the nearest own unit to the door of the
    /// nearest building it does not own, and look at it.
    /// </summary>
    public Vector2? DebugDemoCapture()
    {
        if (_nav == null) return null;
        var doors = CaptureDoors();
        _capDoors = doors.Count;
        if (doors.Count == 0) { GD.Print("demo-capture: keine Tuer auf dieser Karte"); return null; }

        int best = -1, bestDoor = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead || !u.Mobile) continue;
            if (u.Owner != ViewPlayer) continue;
            for (int k = 0; k < doors.Count; k++)
            {
                var (b, _, front) = doors[k];
                if (b.Owner == u.Owner) continue;
                float d = new Vector2(front.X - u.Col, front.Y - u.Row).Length();
                if (d >= bestD) continue;
                bestD = d; best = i; bestDoor = k;
            }
        }
        if (best < 0) { GD.Print("demo-capture: keine eigene Einheit, die fahren kann"); return null; }

        var (bld, door, cell) = doors[bestDoor];
        _demoUnit = best;
        _demoBuilding = _entities.IndexOf(bld);
        _demoFrom = new Vector2I(_entities[best].Col, _entities[best].Row);
        _sel.Clear();
        _sel.Add(best);
        SetPrimary();
        IssueMove(CellCenter(cell.X, cell.Y));
        var unit = _entities[best];
        GD.Print($"demo-capture: {_capDoors} Tueren; Platz {unit.Slot} (Spieler {unit.Owner}) " +
                 $"faehrt {bestD:0.0} Felder zu {BuildingTypeName(bld.BType)} " +
                 $"\"{bld.Name}\" (Besitzer {bld.Owner}, {bld.Hp} Takte) " +
                 $"— Tuer {door.X},{door.Y}, davor {cell.X},{cell.Y}");
        return CellCenter(cell.X, cell.Y);
    }

    /// <summary>Where <c>--demo-capture</c> sent its unit from, so
    /// <c>--demo-leave</c> can send it back.</summary>
    private int _demoUnit = -1, _demoBuilding = -1;
    private Vector2I _demoFrom;

    /// <summary>
    /// <c>--demo-leave=&lt;s&gt;</c>: order the demo's unit back where it started.
    ///
    /// This exists for one reason. The drain at @0x43D29E — the branch that
    /// counts a capture DOWN when the besieger leaves — is the correction this
    /// session's predecessor made after reading a jump it had not disassembled,
    /// and in a running game nothing ever exercises it: a unit sent to a door
    /// stays at the door. So the harness has to walk away on purpose.
    /// </summary>
    public string DebugDemoLeave()
    {
        if (_demoUnit < 0 || _demoUnit >= _entities.Count) return "keine Demo-Einheit";
        var u = _entities[_demoUnit];
        if (u.Dead) return $"Platz {u.Slot} ist gefallen";
        int before = _demoBuilding >= 0 ? _entities[_demoBuilding].CaptureProgress : -1;
        _sel.Clear();
        _sel.Add(_demoUnit);
        SetPrimary();
        IssueMove(CellCenter(_demoFrom.X, _demoFrom.Y));
        return $"Platz {u.Slot} faehrt von ({u.Col},{u.Row}) zurueck nach " +
               $"({_demoFrom.X},{_demoFrom.Y}); Fortschritt am Demo-Gebaeude {before}";
    }

    /// <summary>The report line: how many doors, who is standing at one, and on
    /// which of the two cells — the number that settles reading (1).</summary>
    public string CaptureWatchLine()
    {
        var doors = CaptureDoors();
        int running = 0, neutral = 0;
        string first = "";
        foreach (var (b, _, _) in doors)
        {
            if (b.Owner == 11) neutral++;
            if (b.CaptureProgress <= 0) continue;
            running++;
            if (first.Length == 0)
                first = $" {BuildingTypeName(b.BType)} \"{b.Name}\" " +
                        $"{b.CaptureProgress}/{b.CaptureTotal} durch Spieler {b.Intruder}";
        }
        // content exported before the door fields existed has built == 0 on
        // every record; that is a gap in the DATA, not in the map, and it is
        // counted rather than hidden
        int stale = 0;
        foreach (var e in _entities)
            if (e.IsBuilding && !e.IsProp && e.BType is >= 1 and <= 16 && e.Built == 0) stale++;

        return $"capture: {doors.Count} Tueren ({neutral} neutral), {running} laufend, " +
               $"{_capDone} besetzt; Takte auf der Tuer {_capOnDoor}, davor {_capBelowDoor}, " +
               $"{_capDrain} zurueckgelaufen" +
               (_demoBuilding >= 0
                   ? $"; Demo-Gebaeude {BuildingTypeName(_entities[_demoBuilding].BType)} " +
                     $"\"{_entities[_demoBuilding].Name}\" {_entities[_demoBuilding].CaptureProgress}/" +
                     $"{_entities[_demoBuilding].CaptureTotal}, Besitzer {_entities[_demoBuilding].Owner}"
                   : "") +
               (stale > 0 ? $"; {stale} Gebaeude OHNE Tuerfeld — Karte neu einspielen" : "") +
               first;
    }

    /// <summary>
    /// `--capture-check` — drive the reported scenario and print every stage.
    ///
    /// <para>The report: an enemy starts taking a building, you stop him, and
    /// afterwards you cannot take or use it yourself. This puts an enemy on the
    /// doorway, removes him, then puts one of ours there, and prints progress,
    /// intruder and owner at each stage — so the answer is a table and not an
    /// impression.</para>
    /// </summary>
    public string CaptureCheck()
    {
        if (_nav == null) return "capture-check: kein Gitter";

        // The scenario needs a building that belongs to NEITHER of the two: a
        // player cannot take his own, so picking one of ours would only prove
        // that. Prefer a neutral one, fall back to any that is not ours.
        int bi = -1;
        for (int pass = 0; pass < 2 && bi < 0; pass++)
            for (int i = 0; i < _entities.Count; i++)
            {
                var c = _entities[i];
                if (!c.IsBuilding || c.Dead || c.Doors <= 0 || c.Built == 0) continue;
                if (c.Owner == ViewPlayer) continue;
                if (pass == 0 && !IsNeutralPlayer(c.Owner)) continue;
                bi = i; break;
            }
        if (bi < 0) return "capture-check: kein fremdes Gebaeude mit Tuer";

        var b = _entities[bi];
        var cells = new List<Vector2I>(CaptureWatchCells(b));
        if (cells.Count == 0) return "capture-check: keine Tuerzellen";
        var door = cells[0];

        var sb = new System.Text.StringBuilder();
        void Stage(string what) => sb.AppendLine(
            $"   {what,-30} Fortschritt {b.CaptureProgress,4}/{b.CaptureTotal,-5}" +
            $" Eindringling {b.Intruder,2}  Besitzer {b.Owner}  gezeigt {b.ShownOwner}");

        sb.AppendLine($"capture-check: Gebaeude slot {b.Slot} typ {b.BType} bei " +
                      $"({b.Col},{b.Row}), Tuerzelle ({door.X},{door.Y}), Besitzer {b.Owner}");
        Stage("Ausgangslage");

        // an attacker who is neither the owner nor us
        int foe = -1;
        for (int p = 0; p <= 7; p++)
            if (p != b.Owner && p != ViewPlayer) { foe = p; break; }
        if (foe < 0) return "capture-check: kein dritter Spieler moeglich";
        int ai = Park(door, foe, 9001);
        for (int t = 0; t < 8; t++) CaptureTick(bi, b, 1);
        Stage($"Gegner {foe} steht 8 Takte");

        _nav.ClearOccupant(door.X, door.Y, ai);
        _entities[ai].Dead = true;
        for (int t = 0; t < 3; t++) CaptureTick(bi, b, 1);
        Stage("Gegner vertrieben, 3 Takte");

        int mi = Park(door, ViewPlayer, 9002);
        int ownerBefore = b.Owner;
        int progBefore = b.CaptureProgress;
        for (int t = 0; t < 8; t++) CaptureTick(bi, b, 1);
        Stage($"eigene Einheit ({ViewPlayer}) 8 Takte");

        sb.Append($"   -> Besitzer {(b.Owner != ownerBefore ? "GEWECHSELT" : "unveraendert")}, " +
                  $"eigener Fortschritt {(b.CaptureProgress > progBefore ? "steigt" : "STEIGT NICHT")}");
        _nav.ClearOccupant(door.X, door.Y, mi);
        _entities[mi].Dead = true;

        sb.AppendLine();
        sb.Append(AlliedStage(bi));
        return sb.ToString();
    }

    /// <summary>
    /// The second half of <c>--capture-check</c>: <b>does an ALLY take?</b>
    ///
    /// <para>Reported on 10.08.2026 as a defect — player 1 took back and then
    /// destroyed the factories player 0 had handed him while the two were
    /// allied. It is not a defect: the original's building tick never asks the
    /// alliance matrix (0 of 207 raw occurrences in the aekernel EXE, 0 of 205
    /// in the F: one, see the class note), and its only owner test is the same
    /// equality compare. This stage makes that visible from a running game
    /// instead of from a disassembly: it allies two players ON PURPOSE, puts one
    /// player's unit on the other's doorway, and prints whether the progress
    /// moves. "Fortschritt STEIGT" is the CORRECT outcome here.</para>
    ///
    /// <para>The alliance is set directly on <c>_allied</c>/<c>_haveAllies</c>
    /// and put back afterwards, because the point is to test the capture rule
    /// under an alliance, not to test how the alliance got there. ⚠ Lesson of
    /// the same day: <c>_standby</c> in MapEntityLayer looks like a switch and
    /// is a retired stopgap that maps with an alliance matrix never consult —
    /// so this touches the matrix itself and nothing that stands next to it.</para>
    /// </summary>
    private string AlliedStage(int skip)
    {
        if (_nav == null) return "   Buendnis-Stufe: kein Gitter";

        // a building that belongs to a real player (0..7), so an alliance
        // between its owner and the intruder is a meaningful thing at all
        int bi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            if (i == skip) continue;
            var c = _entities[i];
            if (!c.IsBuilding || c.IsProp || c.Dead || c.Doors <= 0 || c.Built == 0) continue;
            if (c.Owner is < 0 or > 7 || IsNeutralPlayer(c.Owner)) continue;
            bi = i; break;
        }
        if (bi < 0) return "   Buendnis-Stufe: kein Gebaeude mit Tuer und echtem Besitzer";

        var b = _entities[bi];
        int mate = -1;
        for (int p = 0; p <= 7; p++)
            if (p != b.Owner && !IsNeutralPlayer(p)) { mate = p; break; }
        if (mate < 0) return "   Buendnis-Stufe: kein zweiter echter Spieler";

        var cells = new List<Vector2I>(CaptureWatchCells(b));
        if (cells.Count == 0) return "   Buendnis-Stufe: keine Tuerzellen";
        var door = cells[0];

        bool hadAllies = _haveAllies;
        bool ab = _allied[mate, b.Owner], ba = _allied[b.Owner, mate];
        _allied[mate, b.Owner] = _allied[b.Owner, mate] = true;
        _haveAllies = true;

        int progBefore = b.CaptureProgress, ownerBefore = b.Owner;
        int ui = Park(door, mate, 9003);
        for (int t = 0; t < 8; t++) CaptureTick(bi, b, 1);
        int progAfter = b.CaptureProgress;
        bool allied = Allied(mate, b.Owner);
        _nav.ClearOccupant(door.X, door.Y, ui);
        _entities[ui].Dead = true;

        _allied[mate, b.Owner] = ab;
        _allied[b.Owner, mate] = ba;
        _haveAllies = hadAllies;

        return $"   Buendnis-Stufe: Gebaeude slot {b.Slot} typ {b.BType} von Spieler " +
               $"{ownerBefore}, Tuerzelle ({door.X},{door.Y}); Spieler {mate} verbuendet " +
               $"= {allied}\n" +
               $"   {"verbuendete Einheit 8 Takte",-30} Fortschritt {progAfter,4}/{b.CaptureTotal,-5}" +
               $" Eindringling {b.Intruder,2}  Besitzer {b.Owner}  gezeigt {b.ShownOwner}\n" +
               $"   -> {(progAfter > progBefore ? "Fortschritt STEIGT" : "Fortschritt steht")} " +
               $"— das Original fragt hier keine Diplomatie, ein Verbuendeter nimmt ein " +
               $"(erwartet: STEIGT)";
    }

    /// <summary>Puts a throwaway unit on a cell for the check above.</summary>
    private int Park(Vector2I cell, int owner, int slot)
    {
        var u = new Entity
        {
            Slot = slot, Col = cell.X, Row = cell.Y, Owner = owner, Team = owner,
            UnitType = 160, Hp = 10, HpMax = 10, Mobile = true,
            Footprint = CellRect(_ox, _oy, cell.X, cell.Y, 0),
        };
        u.Pos = CellCenter(cell.X, cell.Y);
        _entities.Add(u);
        int i = _entities.Count - 1;
        _nav!.SetOccupant(cell.X, cell.Y, i);
        return i;
    }

    /// <summary>`--pick-check` — how big the click area of each building is,
    /// and whether a click in its middle actually finds it.</summary>
    public string PickCheck()
    {
        var sb = new System.Text.StringBuilder();
        int n = 0, hit = 0, oneByOne = 0;
        var seen = new Dictionary<int, Vector2I>();
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp) continue;
            n++;
            if (e.FootW <= 1 && e.FootH <= 1) oneByOne++;
            seen[e.BType] = new Vector2I(e.FootW, e.FootH);
            // click the middle of the body and see whether Pick finds this one
            var r = BodyRect(e);
            if (_entities.IndexOf(e) == Pick(r.Position + r.Size / 2f)) hit++;
        }
        sb.AppendLine($"pick-check: {n} Gebaeude, {hit} finden sich beim Klick in ihre Mitte, " +
                      $"{oneByOne} haben noch 1x1");
        foreach (var kv in seen)
            sb.AppendLine($"   typ {kv.Key,2}: Trefferflaeche {kv.Value.X} x {kv.Value.Y} Zellen " +
                          $"= {kv.Value.X * TileW} x {kv.Value.Y * TileH} px");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// `--ruin-check` — destroys every building and reports what is now drawn.
    ///
    /// <para>Answers the reported "destroyed buildings are graphically still
    /// there and can still be selected" with numbers: how many types have a ruin
    /// picture at all, how many tiles the ruin draws against the standing one,
    /// and whether the wreck can still be clicked.</para>
    /// </summary>
    public string RuinCheck()
    {
        if (Patterns == null) return "ruin-check: keine Muster geladen";
        var sb = new System.Text.StringBuilder();
        var seen = new Dictionary<int, (int Stand, int Ruin)>();
        int n = 0, withRuin = 0;

        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp) continue;
            n++;
            int stand = Patterns.GetBuildingType(e.BType).FirstPattern;
            int ruin = Import.BuildingPatterns.RuinPattern(Patterns, e.BType);
            if (ruin >= 0) withRuin++;
            if (!seen.ContainsKey(e.BType))
                seen[e.BType] = (stand, ruin);
        }

        var victims = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].IsBuilding && !_entities[i].IsProp) victims.Add(i);
        foreach (int i in victims) Kill(i, _entities[i]);

        int stillPicked = 0;
        foreach (int i in victims)
        {
            var r = BodyRect(_entities[i]);
            if (Pick(r.Position + r.Size / 2f) == i) stillPicked++;
        }

        sb.AppendLine($"ruin-check: {n} Gebaeude, {withRuin} haben ein Ruinenbild, " +
                      $"{n - withRuin} nicht (Typ mit nur einem Muster)");
        sb.AppendLine($"   nach der Zerstoerung noch anklickbar: {stillPicked} von {n}");
        foreach (var kv in seen)
            sb.AppendLine($"   typ {kv.Key,2}: Anzahl {Patterns.GetBuildingType(kv.Key).PatternCount,2}, " +
                          $"stehend Muster {kv.Value.Stand,3} ({TilesOf(kv.Value.Stand),2} Kacheln)" +
                          $" -> Ruine Muster {kv.Value.Ruin,3} " +
                          $"({(kv.Value.Ruin >= 0 ? TilesOf(kv.Value.Ruin) : 0),2} Kacheln)");
        sb.Append(stillPicked == 0 && withRuin == n
                  ? "   ALLE ZEIGEN IHRE RUINE UND SIND NICHT MEHR ANKLICKBAR"
                  : "   siehe oben");
        return sb.ToString();
    }

    private int TilesOf(int pattern)
    {
        int k = 0;
        for (int x = 0; x < Import.CwpFile.PatternWidth; x++)
            for (int y = 0; y < Import.CwpFile.PatternHeight; y++)
                if (Patterns!.PatternTile(pattern, x, y) != 0) k++;
        return k;
    }

    /// <summary>`--ruin-demo` — destroy every building so a screenshot shows the
    /// ruins instead of an argument about them.</summary>
    public void RuinDemo()
    {
        for (int i = 0; i < _entities.Count; i++)
            if (_entities[i].IsBuilding && !_entities[i].IsProp) Kill(i, _entities[i]);
        QueueRedraw();
    }

    /// <summary>
    /// `--corpse-check` — the two halves of the "dead units are black patches and
    /// still selectable" report, measured instead of eyeballed.
    ///
    /// <para>Kills a sample of units and then asks, for each: does a click on the
    /// body still find it, and does a live unit standing on the same spot win the
    /// click? The second question is the one that matters — a corpse used to be
    /// picked ahead of the living and swallow the order.</para>
    /// </summary>
    public string CorpseCheck()
    {
        var sb = new System.Text.StringBuilder();
        var victims = new List<int>();
        for (int i = 0; i < _entities.Count && victims.Count < 12; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.HpMax <= 0) continue;
            victims.Add(i);
        }
        if (victims.Count == 0) return "corpse-check: keine Einheiten auf dieser Karte";

        int foot = 0;
        foreach (int i in victims) if (_entities[i].Infantry >= 0) foot++;
        foreach (int i in victims) Kill(i, _entities[i]);

        int stillPicked = 0, takenByDead = 0, takenByOther = 0, tested = 0;
        foreach (int i in victims)
        {
            var dead = _entities[i];
            var mid = BodyRect(dead).Position + BodyRect(dead).Size / 2f;
            if (Pick(mid) == i) stillPicked++;

            // put a live stand-in on the very same spot and see who wins
            var live = new Entity
            {
                Col = dead.Col, Row = dead.Row, Elev = dead.Elev, Pos = dead.Pos,
                Owner = dead.Owner, HpMax = 100, Hp = 100, Mobile = true,
                UnitType = dead.UnitType, Infantry = dead.Infantry,
                FootW = dead.FootW, FootH = dead.FootH,
            };
            _entities.Add(live);
            tested++;
            int won = Pick(mid);
            // Losing to ANOTHER LIVING unit standing on the same spot is normal
            // and not what was reported — only a corpse winning the click is the
            // defect. The first version of this check counted both and called a
            // healthy map broken.
            if (won != _entities.Count - 1)
            {
                if (won >= 0 && _entities[won].Dead) takenByDead++;
                else takenByOther++;
            }
            _entities.RemoveAt(_entities.Count - 1);
        }

        int wreckFx = 0;
        foreach (var fx in _effects) if (fx.Kind == "wreck") wreckFx++;

        sb.AppendLine($"corpse-check: {victims.Count} Einheiten gefallen " +
                      $"({foot} zu Fuss, {victims.Count - foot} Fahrzeuge)");
        sb.AppendLine($"   noch anklickbar             : {stillPicked} von {victims.Count}");
        sb.AppendLine($"   Leiche schnappt den Klick   : {takenByDead} von {tested}");
        sb.AppendLine($"   (Lebender davor, normal)    : {takenByOther} von {tested}");
        sb.AppendLine($"   Wrackreste auf dem Boden    : {wreckFx} " +
                      $"(erwartet {victims.Count - foot}, Infanterie hinterlaesst keine)");
        sb.Append(stillPicked == 0 && takenByDead == 0
                  ? "   BEIDES BEHOBEN" : "   NOCH FEHLERHAFT");
        return sb.ToString();
    }
}
