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

        // ⚠ Die Gruppen unten zeigen auf die STELLE IM SPIELSTAND, nicht auf
        // die Listenstelle im Spiel und nicht auf den Platz. Der Grund steht
        // bei "groups"; hier wird die Umrechnung nebenbei mitgeschrieben.
        var stelleImStand = new int[_entities.Count];
        for (int q = 0; q < stelleImStand.Length; q++) stelleImStand[q] = -1;
        int geschrieben = 0;

        w.ArrayStart("entities");
        for (int ei = 0; ei < _entities.Count; ei++)
        {
            var e = _entities[ei];
            if (e.IsProp) continue;                 // scenery comes back from the map
            stelleImStand[ei] = geschrieben++;
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

        // ---- die zehn GRUPPEN und die vier MERKPUNKTE -----------------------
        //
        // Das Original speichert beide im Spielstand: sec81 traegt 10 Saetze zu
        // 422 Byte (22 B Name + 200 Mitglieder als u16), sec80 vier Saetze zu
        // 23 Byte (21 B Name + Spalte + Zeile). Wir taten es bis zum 21.08.2026
        // nicht — schlimmer noch, ApplySaveState LEERTE die Gruppen und liess
        // die Merkpunkte stehen, so dass sie nach dem Laden in die vorige Karte
        // zeigten.
        //
        // ⚠ WORAUF EIN MITGLIED ZEIGT. Das Original schreibt Platznummern; das
        // koennen wir nicht nachmachen, weil eine frisch GEBAUTE Einheit bei
        // uns Slot = -1 traegt (siehe die Erzeuger um 0x13869/0x15246) — zehn
        // gebaute Panzer haetten alle denselben "Platz". Darum steht hier die
        // Stelle im geschriebenen Feld "entities". Die traegt so weit wie der
        // Spielstand selbst: der Leser haengt die Einheiten in Dateireihenfolge
        // an, also ist die n-te geschriebene Einheit nach dem Laden eindeutig
        // wiederzufinden.
        w.ArrayStart("groups");
        foreach (var kv in _groups)
        {
            var mitglieder = new System.Text.StringBuilder("[");
            bool ersteZahl = true;
            foreach (int i in kv.Value)
            {
                if (i < 0 || i >= stelleImStand.Length || stelleImStand[i] < 0) continue;
                if (!ersteZahl) mitglieder.Append(',');
                ersteZahl = false;
                mitglieder.Append(stelleImStand[i]);
            }
            mitglieder.Append(']');
            w.ItemStart();
            w.Num("n", kv.Key);
            string gn = GroupName(kv.Key);
            if (gn.Length > 0) w.Str("name", gn);
            w.Raw("members", mitglieder.ToString());
            w.ItemEnd();
        }
        w.ArrayEnd();

        w.ArrayStart("marks");
        for (int i = 0; i < Marks.Length; i++)
        {
            w.ItemStart();
            w.Num("i", i).Num("col", Marks[i].Col).Num("row", Marks[i].Row);
            if (Marks[i].Name.Length > 0) w.Str("name", Marks[i].Name);
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
        _groupNames.Clear();
        // ⚠ Die Merkpunkte MUESSEN mit weg. Bis zum 21.08.2026 standen sie
        // hier nicht, ueberlebten also das Laden und zeigten auf Zellen der
        // vorigen Karte — auf einer kleineren Karte sogar ausserhalb.
        foreach (var mk in Marks) { mk.Col = mk.Row = -1; mk.Name = ""; }

        // Stelle im Spielstand -> Listenstelle im Spiel, fuer die Gruppen unten.
        var listenstelle = new List<int>();

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
                listenstelle.Add(_entities.Count);
                _entities.Add(e);
            }

        // ---- die Gruppen und die Merkpunkte zurueck --------------------------
        //
        // ⚠ Eine Stelle, die es nicht mehr gibt, wird UEBERGANGEN statt auf 0
        // abgebogen: ein Mitglied, das ins Leere zeigt, waere eine fremde
        // Einheit in der Gruppe. Bleibt davon nichts uebrig, gibt es die
        // Gruppe nicht — dieselbe Regel, die StoreGroup fuer die leere Auswahl
        // anwendet.
        if (root.TryGetValue("groups", out var gv) && gv.VariantType == Variant.Type.Array)
            foreach (var item in gv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                int n = GetI(d, "n", -1);
                if (n < 0) continue;
                var mit = new List<int>();
                if (d.TryGetValue("members", out var mvv) && mvv.VariantType == Variant.Type.Array)
                    foreach (var q in mvv.AsGodotArray())
                    {
                        int stelle = q.AsInt32();
                        if (stelle >= 0 && stelle < listenstelle.Count) mit.Add(listenstelle[stelle]);
                    }
                if (mit.Count == 0) continue;
                _groups[n] = mit;
                if (d.TryGetValue("name", out var gnv))
                {
                    string gn = gnv.AsString();
                    if (gn.Length > 0) _groupNames[n] = gn;
                }
            }

        if (root.TryGetValue("marks", out var kv2) && kv2.VariantType == Variant.Type.Array)
            foreach (var item in kv2.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                int i = GetI(d, "i", -1);
                if (i < 0 || i >= Marks.Length) continue;
                Marks[i].Col = GetI(d, "col", -1);
                Marks[i].Row = GetI(d, "row", -1);
                Marks[i].Name = d.TryGetValue("name", out var nv2) ? nv2.AsString() : "";
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

            // ⚠ Die Gruppen gehen ueber die ZELLEN ihrer Mitglieder ein, nicht
            // ueber deren Listenstellen: die Stellen duerfen sich beim Laden
            // verschieben (Requisiten bleiben stehen, alles andere wird neu
            // angehaengt), die Einheiten dahinter aber nicht.
            int gruppen = 0, mitglieder = 0, gzellen = 0, gnamen = 0;
            foreach (var kv in _groups)
            {
                gruppen++;
                gnamen += GroupName(kv.Key).Length * (kv.Key + 1);
                foreach (int i in kv.Value)
                {
                    if (i < 0 || i >= _entities.Count) continue;
                    mitglieder++;
                    gzellen += (_entities[i].Col * 7 + _entities[i].Row * 13) * (kv.Key + 1);
                }
            }
            int merk = 0, mnamen = 0;
            for (int i = 0; i < Marks.Length; i++)
            {
                if (Marks[i].Leer) continue;
                merk++;
                mnamen += Marks[i].Name.Length;
                gzellen += (Marks[i].Col * 3 + Marks[i].Row * 5) * (i + 1);
            }

            return $"{n} Einheiten ({buildings} Gebaeude, {dead} tot), HP {hp}, " +
                   $"Besitzer {own}, Zellen {cells}, Lager {stock}, Geld {money}, Nebel {fog}, " +
                   $"{gruppen} Gruppen mit {mitglieder} Mitgliedern (Zellen {gzellen}, " +
                   $"Namen {gnamen}), {merk} Merkpunkte (Namen {mnamen})";
        }

        // Ein Prueflauf, der nichts zu verlieren hat, kann nichts verlieren:
        // ohne Gruppe und ohne Merkpunkt waere die Zeile oben beidemal null
        // und der Vergleich gruen, egal was der Schreiber tut. Also erst
        // etwas hinlegen — aber nur, wenn nichts da ist.
        string gelegt = "";
        if (_groups.Count == 0)
        {
            var erste = new List<int>();
            for (int i = 0; i < _entities.Count && erste.Count < 3; i++)
                if (!_entities[i].IsProp && !_entities[i].Dead) erste.Add(i);
            if (erste.Count > 0)
            {
                _groups[2] = erste;
                _groupNames[2] = "Pruefgruppe";
                gelegt = $" (Gruppe 2 mit {erste.Count} Einheiten gelegt)";
            }
        }
        if (Marks[1].Leer)
        {
            Marks[1].Col = 12; Marks[1].Row = 34; Marks[1].Name = "Merk Zwei";
            gelegt += " (Merkpunkt 2 gelegt)";
        }

        string before = Fingerprint();
        string json = SaveStateJson(mapName, "roundtrip");
        if (!Core.SaveGame.Write("__roundtrip", json, out string err))
            return $"save-check: Schreiben fehlgeschlagen — {err}";
        var root = Core.SaveGame.Read("__roundtrip", out err);
        if (root == null) return $"save-check: Lesen fehlgeschlagen — {err}";
        ApplySaveState(root);
        string after = Fingerprint();

        return "save-check:" + gelegt
             + $"\n   vorher : {before}"
             + $"\n   nachher: {after}"
             + $"\n   {(before == after ? "DECKUNGSGLEICH" : "WEICHT AB")}, "
             + $"Datei {json.Length} Zeichen";
    }
}
