using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ROHSTOFFE — the skirmish option that says how much a map starts with.
///
/// The original's own option, down to its four words: <b>keine · wenige ·
/// normal · viele</b>. It is not a difficulty knob bolted on here; it is
/// <c>fill_resources</c> @0x419fe0, and everything below is read out of that
/// routine (see <see cref="Import.ExeTables.ResourceLevels"/> for the search
/// that finds it in either build, and <c>aekernel/resources_export.py</c> for
/// the reference the self-test compares against).
///
/// <list type="bullet">
/// <item><b>It is a SKIRMISH option and nothing else.</b> The routine has
/// <b>exactly one caller</b> — @0x41ACA0, inside the game-start message handler
/// @0x4c2280, right after the options packet has written the setting
/// (@0x4C40F8). A campaign mission never runs it and keeps the stores its level
/// file gives it. One caller is a count, not an impression.</item>
///
/// <item><b>Who gets what</b>, read back out of the routine's own jump table
/// rather than assigned here: <b>Basis (1), Flughafen (9), Werft-Station
/// (16)</b> get the three parts stores; <b>the three factories (2, 3, 4)</b>
/// get those emptied and Terranium instead; <b>Mine (10) and
/// Feld-Rohstoffmine (15)</b> get what is left in the ground; types 5-8 and
/// 11-14 are emptied, and type 0 is skipped.</item>
///
/// <item><b>The numbers</b>, a clean doubling ladder — which is what makes the
/// reading safe: Waffen/Fahrwerk/Spezial <b>0/0/0 · 150/200/100 · 300/400/200 ·
/// 600/800/400</b>, Terranium <b>0 · 500 · 1000 · 2000</b>, deposits <b>0 ·
/// 1000 · 3000 · 9000</b>.</item>
/// </list>
///
/// <para><b>What is OURS, and it is two things.</b></para>
///
/// <para>(1) <b>The default is "normal".</b> The original's default lives in
/// whatever the options packet was last set to, which is not a thing this
/// remake has; 2 is the middle of the ladder and the word the game itself uses
/// for it.</para>
///
/// <para>(2) <b>The deposit is written through the building, not through the
/// deposit array.</b> The original writes <c>word[0x878adc + 18*n]</c> with
/// <c>n</c> from the record's +0x19; we carry the same value on the mine that
/// owns it, because that is how the deposit reached the entity in the first
/// place (sec28, <c>Deposit</c>/<c>DepositStart</c>). Same number, one
/// indirection fewer — and <c>DepositStart</c> is set with it so the grade
/// arithmetic keeps working.</para>
///
/// <para><b>Not reproduced:</b> the routine walks <b>255</b> building slots
/// (<c>inc cl</c> against 0xFF @0x41A0C0) while a level carries 300 records, so
/// in the original a map with more than 255 buildings would leave the tail
/// untouched. Here every building is filled. The watch line prints both counts,
/// so the difference is visible rather than buried — on the maps in hand the
/// largest building list is well under 255, so it has never mattered.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>One setting, as it came out of GAME.EXE.</summary>
    public sealed class ResourceLevel
    {
        public int Level;
        public string Name = "";
        public int Weapons, Chassis, Special, Terranium, Deposit;
    }

    /// <summary>How a building type is filled — the routine's own five cases.</summary>
    public enum ResourceFill { None, Stores, Factory, Mine, Clear }

    private static List<ResourceLevel>? _resLevels;
    private static Dictionary<int, ResourceFill>? _resFill;
    private static bool _resTried;

    /// <summary>Reads <c>resources.json</c> once. Missing file means the option
    /// is simply not offered — nothing is invented in its place.</summary>
    private static void LoadResources()
    {
        if (_resTried) return;
        _resTried = true;
        string path = Core.Content.Path("Maps/resources.json");
        if (!FileAccess.FileExists(path)) return;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();

        if (root.TryGetValue("levels", out var lv) && lv.VariantType == Variant.Type.Array)
        {
            var list = new List<ResourceLevel>();
            foreach (var item in lv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                list.Add(new ResourceLevel
                {
                    Level = GetI(d, "level"),
                    Name = d.TryGetValue("name", out var nv) ? nv.AsString() : "",
                    Weapons = GetI(d, "weapons"),
                    Chassis = GetI(d, "chassis"),
                    Special = GetI(d, "special"),
                    Terranium = GetI(d, "terranium"),
                    Deposit = GetI(d, "deposit"),
                });
            }
            if (list.Count > 0) _resLevels = list;
        }

        if (root.TryGetValue("fill", out var fv) && fv.VariantType == Variant.Type.Dictionary)
        {
            var map = new Dictionary<int, ResourceFill>();
            foreach (var kv in fv.AsGodotDictionary<string, Variant>())
            {
                if (!int.TryParse(kv.Key, out int t)) continue;
                map[t] = kv.Value.AsString() switch
                {
                    "stores" => ResourceFill.Stores,
                    "factory" => ResourceFill.Factory,
                    "mine" => ResourceFill.Mine,
                    "clear" => ResourceFill.Clear,
                    _ => ResourceFill.None,
                };
            }
            if (map.Count > 0) _resFill = map;
        }
    }

    /// <summary>The four words the game uses, for the menu. Empty when the
    /// table could not be read — then the option is not offered at all.</summary>
    public static List<string> ResourceLevelNames()
    {
        LoadResources();
        var names = new List<string>();
        if (_resLevels == null) return names;
        foreach (var l in _resLevels) names.Add(l.Name.Length > 0 ? l.Name : $"Stufe {l.Level}");
        return names;
    }

    private static ResourceFill FillOf(int bType)
    {
        LoadResources();
        if (_resFill != null && _resFill.TryGetValue(bType, out var k)) return k;
        // the routine's first test is `cmp eax, 0x10 / ja` (@0x41A002): a type
        // above 16 goes to the branch that empties all four stores, so an
        // unknown type is cleared and not silently left alone
        return bType > 16 ? ResourceFill.Clear : ResourceFill.None;
    }

    // what the last fill did, for the watch line
    private int _resStores, _resFactories, _resMines, _resCleared, _resSkipped;
    private string _resName = "";

    /// <summary>
    /// Fill every building's stores for one setting. Called from
    /// <see cref="StartSkirmish"/> only — see the class note for why a campaign
    /// must not go through here.
    /// </summary>
    public bool ApplyResources(int level)
    {
        LoadResources();
        if (_resLevels == null || level < 0 || level >= _resLevels.Count) return false;
        var r = _resLevels[level];
        _resName = r.Name;
        _resStores = _resFactories = _resMines = _resCleared = _resSkipped = 0;

        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            switch (FillOf(e.BType))
            {
                case ResourceFill.Stores:            // Basis, Flughafen, Werft
                    e.StockW = r.Weapons;
                    e.StockF = r.Chassis;
                    e.StockS = r.Special;
                    _resStores++;
                    break;
                case ResourceFill.Factory:           // the three factories
                    e.StockW = e.StockF = e.StockS = 0;
                    e.StockT = r.Terranium;
                    _resFactories++;
                    break;
                case ResourceFill.Mine:              // Mine, Feld-Rohstoffmine
                    e.Deposit = r.Deposit;
                    e.DepositStart = Mathf.Max(1, r.Deposit);
                    _resMines++;
                    break;
                case ResourceFill.Clear:
                    e.StockW = e.StockF = e.StockS = e.StockT = 0;
                    _resCleared++;
                    break;
                default:
                    _resSkipped++;
                    break;
            }
        }
        QueueRedraw();
        return true;
    }

    /// <summary>What the fill did, with the numbers it used.</summary>
    public string ResourceWatchLine()
    {
        LoadResources();
        if (_resLevels == null) return "rohstoffe: keine Tabelle eingespielt";
        if (_resName.Length == 0 && _resStores + _resFactories + _resMines == 0)
            return "rohstoffe: nicht angewandt (Kampagne behaelt, was die Karte gibt)";
        // read the numbers back OFF THE BUILDINGS rather than reprinting the
        // table — that is what shows the fill actually happened
        int buildings = 0;
        long w = 0, f = 0, s = 0, tr = 0, dep = 0;
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            buildings++;
            switch (FillOf(e.BType))
            {
                case ResourceFill.Stores: w += e.StockW; f += e.StockF; s += e.StockS; break;
                case ResourceFill.Factory: tr += e.StockT; break;
                case ResourceFill.Mine: dep += Mathf.Max(0, e.Deposit); break;
            }
        }
        return $"rohstoffe: \"{_resName}\" — {_resStores} Lager, {_resFactories} Fabriken, " +
               $"{_resMines} Minen, {_resCleared} geleert, {_resSkipped} uebersprungen " +
               $"({buildings} Gebaeude; das Original faellt nach " +
               $"{Import.ExeTables.ResourceSlotsScanned} Plaetzen aus); " +
               $"in den Gebaeuden jetzt W{w} F{f} S{s}, Terranium {tr}, Vorkommen {dep}";
    }
}
