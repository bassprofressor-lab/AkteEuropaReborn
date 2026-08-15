using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ÜBERNAHME — a neutral unit joins the player who drives up to it.
///
/// This closes a report that has been open for weeks: <i>"man muss die neutralen
/// Einheiten erst anfahren, damit sie meine eigenen werden"</i>. The remake did
/// not know the mechanic, so map_01's ten player-7 units just stood there — and
/// worse, our AI shot at them, because who was neutral was a guess of ours.
/// Both halves are answered now; see
/// <see cref="Import.ExeTables.CampaignDiplomacy"/> for where the neutral slot
/// comes from (the executable, not the map file).
///
/// <para><b>All of the following is the original's</b>, read off
/// <c>takeover_scan</c> @0x411270 and its two helpers.</para>
///
/// <list type="bullet">
/// <item><b>Only neutral units scan.</b> The entity main loop @0x406CD0 calls
/// it at @0x407280, and only when <c>byte[0xb38d38 + slot/1000] != 0</c>
/// (@0x407275) — the slot's player is a neutral one. A neutral unit looks
/// around itself; nobody looks for it.</item>
///
/// <item><b>How far it looks depends on the unit.</b> <c>byte[+0x0a]</c>, the
/// SPODEK subclass, decides: below 3 the ring is <b>3x3</b> (col-1..col+1,
/// @0x4112CC <c>add eax, 2</c>), from 3 up it is <b>4x4</b> (col-1..col+2,
/// @0x411435 <c>add eax, 3</c>) — one ring around a footprint that is two cells
/// wide. Column outer, row inner, both starting one before.</item>
///
/// <item><b>What it looks for.</b> Per cell: the bounds check @0x4018b1, then
/// the imap word <c>[0xbdea80 + (col*256 + row)*2]</c>. A value below 8000 is a
/// live slot and <b>the player is slot/1000</b> (@0x411344 <c>div 0x3e8</c>) —
/// the entity table is eight blocks of a thousand, one per player.</item>
///
/// <item><b>Two conditions on that player</b> (@0x411351, @0x41135E):
/// <c>byte[0xb38d38 + p] == 0</c>, so a neutral does not take a neutral; and
/// <c>byte[p*40 + 0x87b140] == 0</c>, the player record's +0x00 — and that byte
/// already has a name from the .DM files (<see cref="Import.CwmExtra"/>):
/// <b>0 = the human player</b>, 1 = an active CPU, 0xFF = beaten. <b>So in the
/// original only the human collects neutral units.</b> That is exactly how the
/// player described it, and it is why the computer never does it here.</item>
///
/// <item><b>One at a time.</b> On a hit the routine calls
/// <c>add_change_owner(slot, p)</c> (@0x410F40) and <b>returns immediately</b> —
/// no second cell is looked at. It also writes <c>word[0xbc5752] = p + 1</c>,
/// which the interface reads somewhere; that is not reproduced.</item>
///
/// <item><b>Through a queue.</b> <c>add_change_owner</c> parks the pair in 1000
/// slots of 4 bytes at 0x53c938 (u16 slot, u8 player, empty 0xFFFF) and says
/// "Too many change owners" (@0x4f6eb0) when they are full; it then cancels the
/// unit's current order — <c>+0x14 = 0</c>, <c>+0x15 = 0</c>, <c>+0x1a =
/// 0xFF</c> — unless <c>+0x04</c> is already 0xFF. The processor @0x411000 runs
/// every tick, finds a free slot in the NEW player's thousand-block, copies the
/// whole 78-byte record over, frees the old one and re-stamps the imap.</item>
/// </list>
///
/// <para><b>What is OURS, and it is three things.</b></para>
///
/// <para>(1) <b>No queue.</b> Our entities are one flat list with an owner
/// field, not eight blocks of a thousand, so a change of owner is a change of a
/// field and there is nothing to defer. The queue exists in the original
/// because the record has to MOVE; here it does not. "Too many change owners"
/// therefore cannot happen, which is a difference worth naming rather than
/// hiding.</para>
///
/// <para>(2) <b>Buildings do not count as a taker.</b> The original reads the
/// imap, and a building's cells are stamped in it as well, so standing next to
/// an enemy factory would hand a neutral unit over just as a tank would. That
/// reads like a side effect of the shared grid rather than an intention, and a
/// building cannot "drive up to" anything, so only mobile units count here. The
/// watch line counts both, so the question can be settled from a running game.
/// </para>
///
/// <para>(3) <b>The 3x3 is walked in the original's order</b> — column outer,
/// row inner, both from one before — so where the original would fire, this
/// fires on the same cell. That part is not ours; it is written down because it
/// is the sort of detail that is easy to get wrong twice.</para>
///
/// <para><b>Not built:</b> the second branch @0x411388. When the cell holds a
/// static object code (10000..13999) instead of a slot, the routine walks a
/// table of <b>11 u16 per code at 0x7847ec</b>, nine entries deep, and takes the
/// owner from there. What that table is has not been read, so nothing is
/// invented for it. Also not built: the script primitive <c>change_owner</c>
/// @0x4D0D30, which refuses with "Cannot change owner of unit of this type"
/// when +0x0a &gt; 1 and "…without weapon" when +0x0d == 0.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Seconds between scans. The original does it on the entity tick;
    /// running it every frame would cost the same work eight hundred times over
    /// for a result that cannot change that fast. OURS, and only a rate.</summary>
    private const float TakeoverEverySec = 0.35f;
    private float _takeoverTimer;

    // for the watch line, so the readings above can be checked against a game
    private int _tookOver, _tookByBuilding;

    /// <summary>The subclass at which the ring grows to 4x4 (@0x411430).</summary>
    private const int TakeoverWideSubclass = 3;

    /// <summary>Can this unit be taken? Neutral, alive, and not a structure —
    /// the scan runs over the entity table, where buildings sit too.</summary>
    private static bool Takeable(Entity e)
        => !e.Dead && !e.IsProp && !e.IsBuilding &&
           e.Owner is >= 0 and <= 7 && IsNeutralPlayer(e.Owner);

    /// <summary>The cells a neutral unit looks at, in the original's order:
    /// column outer, row inner, both starting one before its own.</summary>
    private static IEnumerable<Vector2I> TakeoverCells(Entity e)
    {
        int span = e.GameUnitType >= TakeoverWideSubclass ? 4 : 3;
        for (int c = e.Col - 1; c < e.Col - 1 + span; c++)
            for (int r = e.Row - 1; r < e.Row - 1 + span; r++)
                yield return new Vector2I(c, r);
    }

    /// <summary>
    /// One scan. Every neutral unit looks around itself and joins the human
    /// player if one of his units is standing there.
    /// </summary>
    private void TakeoverTick()
    {
        if (_nav == null || !HaveNeutralPlayers) return;

        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!Takeable(e)) continue;

            foreach (var cell in TakeoverCells(e))
            {
                int j = _nav.OccupantAt(cell.X, cell.Y);
                if (j < 0 || j == i || j >= _entities.Count) continue;
                var o = _entities[j];
                if (o.Dead || o.IsProp) continue;
                if (o.Owner is < 0 or > 7) continue;
                if (IsNeutralPlayer(o.Owner)) continue;      // @0x411351
                if (o.Owner != ViewPlayer) continue;         // @0x41135E: +0x00 == 0

                // ⚠ OURS: a building's cells are in the imap too, so the
                // original would let a factory do this. Counted, not obeyed.
                if (o.IsBuilding) { _tookByBuilding++; continue; }

                Join(i, e, o.Owner);
                break;                                       // @0x411380: one, then out
            }
        }
    }

    /// <summary>The unit changes hands. <c>add_change_owner</c> @0x410F40 drops
    /// whatever it was doing (+0x14, +0x15, +0x1a) before the record moves, so
    /// the order goes with the old owner.</summary>
    private void Join(int index, Entity e, int who)
    {
        e.Owner = who;
        e.Team = who;
        e.Path = null;
        e.PathIdx = 0;
        e.Goal = new Vector2I(e.Col, e.Row);
        e.Target = -1;
        e.WaitTime = 0;
        _tookOver++;

        string what = LabelOf(e.UnitType);
        string line = $"{what} uebergelaufen";
        _order = line;
        NoteEvent(e, line);
        QueueRedraw();
        if (_selected == index) UpdatePanel();
    }

    /// <summary>How many neutral units are left, how many have come over, and
    /// how often a building would have taken one — the number that settles
    /// reading (2).</summary>
    public string TakeoverWatchLine()
    {
        if (!HaveNeutralPlayers)
            return "takeover: kein neutraler Platz auf dieser Karte";
        int left = 0, near = 0;
        foreach (var e in _entities)
        {
            if (!Takeable(e)) continue;
            left++;
            foreach (var cell in TakeoverCells(e))
            {
                if (_nav == null) break;
                int j = _nav.OccupantAt(cell.X, cell.Y);
                if (j < 0 || j >= _entities.Count) continue;
                var o = _entities[j];
                if (!o.Dead && !o.IsProp && o.Owner is >= 0 and <= 7 &&
                    !IsNeutralPlayer(o.Owner)) { near++; break; }
            }
        }
        return $"takeover: neutrale Plaetze {string.Join(",", NeutralPlayerList())}; " +
               $"{left} neutrale Einheiten offen, {near} mit jemandem daneben, " +
               $"{_tookOver} uebergelaufen" +
               (_tookByBuilding > 0
                   ? $"; {_tookByBuilding} Takte, in denen ein GEBAEUDE danebenstand " +
                     "(das Original wuerde sie nehmen, wir nicht)"
                   : "");
    }

    /// <summary>
    /// <c>--demo-takeover</c>: send the nearest own unit to the nearest neutral
    /// one and look at it — the same shape as <c>--demo-capture</c>.
    /// </summary>
    public Vector2? DebugDemoTakeover()
    {
        if (_nav == null) return null;
        if (!HaveNeutralPlayers)
        {
            GD.Print("demo-takeover: kein neutraler Platz auf dieser Karte");
            return null;
        }

        int mine = -1, prize = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead || !u.Mobile) continue;
            if (u.Owner != ViewPlayer) continue;
            for (int k = 0; k < _entities.Count; k++)
            {
                if (!Takeable(_entities[k])) continue;
                var t = _entities[k];
                float d = new Vector2(t.Col - u.Col, t.Row - u.Row).Length();
                if (d >= bestD) continue;
                bestD = d; mine = i; prize = k;
            }
        }
        if (mine < 0 || prize < 0)
        {
            GD.Print("demo-takeover: keine eigene Einheit, die fahren kann, " +
                     "oder nichts Neutrales");
            return null;
        }

        var goal = _entities[prize];
        // one cell to the west of it — inside the 3x3 the neutral unit watches
        var cell = new Vector2I(goal.Col - 1, goal.Row);
        _sel.Clear();
        _sel.Add(mine);
        SetPrimary();
        IssueMove(CellCenter(cell.X, cell.Y));
        var u2 = _entities[mine];
        GD.Print($"demo-takeover: Platz {u2.Slot} (Spieler {u2.Owner}) faehrt {bestD:0.0} " +
                 $"Felder zu {LabelOf(goal.UnitType)} (Platz {goal.Slot}, Spieler " +
                 $"{goal.Owner}, GameUnitType {goal.GameUnitType} -> " +
                 $"{(goal.GameUnitType >= TakeoverWideSubclass ? "4x4" : "3x3")}) " +
                 $"bei ({goal.Col},{goal.Row}) — Ziel ({cell.X},{cell.Y})");
        return CellCenter(cell.X, cell.Y);
    }
}
