using System.Collections.Generic;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// The live state of a game, written out and read back.
///
/// <para>See <see cref="Core.SaveGame"/> for why this is our own format and not
/// the original's .DM.</para>
///
/// <para><b>What is saved and what is not.</b> Everything that changes while
/// playing is saved: where each entity stands, what it is, what it owns, what
/// it is doing, plus the money and the fog. Everything that comes back from the
/// content is not: terrain, the baked picture, the tileset patterns, the design
/// list. A load therefore reloads the map first and then puts this state on top
/// — the same order a fresh start uses, so a save cannot drift from the content
/// it was made against.</para>
///
/// <para><b>Deliberately not saved:</b> paths and reservations. A loaded unit
/// stands still instead of continuing a half-walked route; the alternative is
/// storing a path that may no longer fit the grid. Orders queued behind the
/// current one go the same way. This is a decision, not an oversight, and it is
/// what makes the save robust against any later change to the pathfinder.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>The whole state as JSON. `mapName` is what a load needs to put
    /// the map back before this is applied.</summary>
    public string SaveStateJson(string mapName, string label)
    {
        var w = new Core.SaveGame.Writer();
        w.Open();
        w.Num("format", Core.SaveGame.Format);
        w.Str("label", label);
        w.Num("when", (long)Time.GetUnixTimeFromSystem());
        w.Str("map", mapName);
        w.Str("mission", _mission);
        w.Num("campaign_mission", UI.SkirmishSetup.CampaignMission);
        w.Bool("skirmish", UI.SkirmishSetup.Active);
        w.Num("view_player", ViewPlayer);
        w.Num("clock", _clock);

        w.ArrayStart("money");
        for (int i = 0; i < _money.Length; i++) { w.ItemStart(); w.Num("v", _money[i]); w.ItemEnd(); }
        w.ArrayEnd();

        w.ArrayStart("entities");
        foreach (var e in _entities)
        {
            if (e.IsProp) continue;                 // scenery comes back from the map
            w.ItemStart();
            w.Num("slot", e.Slot).Num("col", e.Col).Num("row", e.Row);
            w.Num("owner", e.Owner).Num("team", e.Team);
            w.Num("unit_type", e.UnitType).Num("btype", e.BType);
            w.Num("hp", e.Hp).Num("hp_max", e.HpMax);
            w.Num("facing", e.Facing).Num("aim", e.AimFacing);
            w.Num("weapon", e.Weapon).Num("equipment", e.Equipment);
            w.Num("ammo", e.Ammo).Num("ammo_max", e.AmmoMax);
            w.Num("fuel", e.Fuel).Num("fuel_max", e.FuelMax);
            w.Num("speed", e.Speed).Num("range", e.Range).Num("sight", e.Sight);
            w.Num("attack", e.Attack).Num("defence", e.Defence);
            w.Num("infantry", e.Infantry).Num("chassis", e.Chassis).Num("subclass", e.GameUnitType);
            w.Num("pose", e.Pose).Num("condition", e.Condition).Num("state", e.State);
            w.Bool("building", e.IsBuilding).Bool("dead", e.Dead).Bool("dug_in", e.DugIn);
            w.Num("dead_time", e.DeadTime);
            w.Num("doors", e.Doors).Num("built", e.Built);
            w.Num("stock_w", e.StockW).Num("stock_f", e.StockF)
             .Num("stock_s", e.StockS).Num("stock_t", e.StockT);
            w.Num("deposit", e.Deposit).Num("grade", e.Grade);
            w.Num("build_time", e.BuildTime).Num("build_index", e.BuildIndex);
            w.Num("shown_owner", e.ShownOwner);
            if (e.Name.Length > 0) w.Str("name", e.Name);
            w.ItemEnd();
        }
        w.ArrayEnd();

        w.Raw("fog", FogRle());
        w.Close();
        return w.ToString();
    }

    /// <summary>The fog as run lengths — 40,000 cells of mostly the same value
    /// compress to a few hundred numbers.</summary>
    private string FogRle()
    {
        if (_fog == null) return "[]";
        var sb = new System.Text.StringBuilder("[");
        int run = 0, last = -1;
        bool first = true;
        for (int i = 0; i < _fog.CellCount; i++)
        {
            int v = _fog.CellAt(i);
            if (v == last) { run++; continue; }
            if (last >= 0)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('[').Append(last).Append(',').Append(run).Append(']');
            }
            last = v; run = 1;
        }
        if (last >= 0)
        {
            if (!first) sb.Append(',');
            sb.Append('[').Append(last).Append(',').Append(run).Append(']');
        }
        return sb.Append(']').ToString();
    }

    /// <summary>Puts a saved state back. The map must already be loaded — the
    /// caller does that first, exactly as a fresh start would.</summary>
    public void ApplySaveState(GDict root)
    {
        ViewPlayer = GetI(root, "view_player");
        _clock = root.TryGetValue("clock", out var cv) ? (float)cv.AsDouble() : 0f;

        if (root.TryGetValue("money", out var mv) && mv.VariantType == Variant.Type.Array)
        {
            var ma = mv.AsGodotArray();
            for (int i = 0; i < _money.Length && i < ma.Count; i++)
                _money[i] = ma[i].VariantType == Variant.Type.Dictionary
                    ? GetI(ma[i].AsGodotDictionary<string, Variant>(), "v") : ma[i].AsInt32();
        }

        // The props stay; everything else is replaced wholesale. Rebuilding
        // rather than patching is what keeps a save honest: an entity that is
        // not in the file is not on the map, full stop.
        _entities.RemoveAll(e => !e.IsProp);
        _sel.Clear();
        _selected = -1;
        _groups.Clear();

        if (root.TryGetValue("entities", out var ev) && ev.VariantType == Variant.Type.Array)
            foreach (var item in ev.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                int col = GetI(d, "col"), row = GetI(d, "row");
                int el = ElevOf(col, row);
                var e = new Entity
                {
                    Slot = GetI(d, "slot", -1), Col = col, Row = row, Elev = el,
                    Owner = GetI(d, "owner", -1), Team = GetI(d, "team", -1),
                    UnitType = GetI(d, "unit_type", -1), BType = GetI(d, "btype"),
                    Hp = GetI(d, "hp"), HpMax = GetI(d, "hp_max"),
                    Facing = GetI(d, "facing"), AimFacing = GetI(d, "aim", -1),
                    Weapon = GetI(d, "weapon"), Equipment = GetI(d, "equipment"),
                    Ammo = GetI(d, "ammo"), AmmoMax = GetI(d, "ammo_max"),
                    Fuel = GetI(d, "fuel"), FuelMax = GetI(d, "fuel_max"),
                    Speed = GetI(d, "speed"), Range = GetI(d, "range"), Sight = GetI(d, "sight"),
                    Attack = GetI(d, "attack"), Defence = GetI(d, "defence"),
                    Infantry = GetI(d, "infantry", -1), Chassis = GetI(d, "chassis", -1),
                    GameUnitType = GetI(d, "subclass", -1), Pose = GetI(d, "pose"),
                    Condition = GetI(d, "condition", 100), State = GetI(d, "state"),
                    IsBuilding = GetB(d, "building"), Dead = GetB(d, "dead"),
                    DugIn = GetB(d, "dug_in"),
                    DeadTime = d.TryGetValue("dead_time", out var dt) ? (float)dt.AsDouble() : 0f,
                    Doors = GetI(d, "doors"), Built = GetI(d, "built"),
                    StockW = GetI(d, "stock_w"), StockF = GetI(d, "stock_f"),
                    StockS = GetI(d, "stock_s"), StockT = GetI(d, "stock_t"),
                    Deposit = GetI(d, "deposit"), Grade = GetI(d, "grade"),
                    BuildTime = d.TryGetValue("build_time", out var bt) ? (float)bt.AsDouble() : 0f,
                    BuildIndex = GetI(d, "build_index", -1),
                    ShownOwner = GetI(d, "shown_owner", -1),
                    Name = d.TryGetValue("name", out var nv) ? nv.AsString() : "",
                    Mobile = !GetB(d, "building"),
                    Footprint = CellRect(_ox, _oy, col, row, el),
                };
                e.Pos = CellCenter(e.Col, e.Row);
                _entities.Add(e);
            }

        // the grid has to agree with the list again — the same routine a
        // fresh map load uses, so there is only one place that does it
        InitEntityMovement();

        if (root.TryGetValue("fog", out var fv) && fv.VariantType == Variant.Type.Array)
            ApplyFogRle(fv.AsGodotArray());

        UpdatePanel();
        QueueRedraw();
    }

    private void ApplyFogRle(Godot.Collections.Array runs)
    {
        if (_fog == null) return;
        int at = 0;
        foreach (var pair in runs)
        {
            if (pair.VariantType != Variant.Type.Array) continue;
            var p = pair.AsGodotArray();
            if (p.Count < 2) continue;
            int v = p[0].AsInt32(), n = p[1].AsInt32();
            for (int k = 0; k < n && at < _fog.CellCount; k++, at++) _fog.SetCellAt(at, v);
        }
        _fog.MarkChanged();
    }

    private static bool GetB(GDict d, string k)
        => d.TryGetValue(k, out var v) && v.VariantType != Variant.Type.Nil && v.AsBool();

    /// <summary>
    /// `--save-check` — write the state, read it back, apply it, and compare.
    ///
    /// <para>This is the whole point of a save that can be reloaded: what comes
    /// back has to be what went in. The check counts entities and sums the
    /// fields that matter, before and after, so a lost field shows up as a
    /// number instead of as a strange game three moves later.</para>
    /// </summary>
    public string SaveRoundTripCheck(string mapName)
    {
        string Fingerprint()
        {
            int n = 0, hp = 0, own = 0, cells = 0, buildings = 0, dead = 0, stock = 0;
            foreach (var e in _entities)
            {
                if (e.IsProp) continue;
                n++;
                hp += e.Hp; own += e.Owner + 1; cells += e.Col * 7 + e.Row * 13;
                if (e.IsBuilding) buildings++;
                if (e.Dead) dead++;
                stock += e.StockW + e.StockF + e.StockS + e.StockT;
            }
            int money = 0;
            foreach (int m in _money) money += m;
            int fog = 0;
            if (_fog != null)
                for (int i = 0; i < _fog.CellCount; i++) fog += _fog.CellAt(i);
            return $"{n} Einheiten ({buildings} Gebaeude, {dead} tot), HP {hp}, " +
                   $"Besitzer {own}, Zellen {cells}, Lager {stock}, Geld {money}, Nebel {fog}";
        }

        string before = Fingerprint();
        string json = SaveStateJson(mapName, "roundtrip");
        if (!Core.SaveGame.Write("__roundtrip", json, out string err))
            return $"save-check: Schreiben fehlgeschlagen — {err}";
        var root = Core.SaveGame.Read("__roundtrip", out err);
        if (root == null) return $"save-check: Lesen fehlgeschlagen — {err}";
        ApplySaveState(root);
        string after = Fingerprint();

        return "save-check:"
             + $"\n   vorher : {before}"
             + $"\n   nachher: {after}"
             + $"\n   {(before == after ? "DECKUNGSGLEICH" : "WEICHT AB")}, "
             + $"Datei {json.Length} Zeichen";
    }
}
