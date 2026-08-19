namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Writes a map's decoded game state as <c>map_&lt;stem&gt;.entities.json</c> —
/// the file <see cref="Rendering.MapEntityLayer"/> loads to put units,
/// buildings, markers, rails and the zone grid on a map, and the very file
/// <c>Core.Content.Ready</c> looks for to decide whether content has been
/// imported at all.
///
/// The shape is the one <c>cwm_export_entities.py</c> produced, key for key and
/// in the same order, for two reasons: the runtime reads it unchanged, and it
/// is what makes the port checkable against the tooling it replaces
/// (<see cref="ImportSelfTest.RunEntities"/>). Nothing here is invented — where
/// a section is absent, the list is empty, exactly as the Python did for the 23
/// campaign levels that stop after section 38.
/// </summary>
public static class EntitiesJson
{
    /// <summary>Everything one map file yields, decoded once.</summary>
    public sealed class Doc
    {
        public CwmFile Map = null!;
        public List<CwmData.Entity> Entities = new();
        public List<CwmData.Marker> Markers = new();
        public List<CwmData.Building> Buildings = new();
        public List<CwmExtra.Target> Targets = new();
        public List<CwmExtra.Deposit> Deposits = new();
        public List<int> Money = new();
        public List<CwmExtra.Progress> Progress = new();
        public List<CwmExtra.Special> Special = new();
        /// <summary>Die Ware des MARKTES (Typ 17) — sec94 + sec95.
        /// Siehe CwmExtra.MarketOffers.</summary>
        public List<CwmExtra.MarketOffer> Market = new();
        public List<CwmExtra.RailNode> RailNodes = new();
        public List<CwmExtra.Link> Links = new();
        public List<CwmExtra.RailCell> RailCells = new();

        /// <summary>Die BELADENEN TRANSPORTER der Karte — sec37.
        /// Siehe CwmExtra.TransportLoads.</summary>
        public List<CwmExtra.TransportLoad> Transports = new();

        /// <summary>Die FREIEN Vorkommen der Karte — sec38.
        /// Nicht zu verwechseln mit Deposits (sec28).</summary>
        public List<CwmExtra.TerraPlace> TerraPlaces = new();
        public List<CwmExtra.Player> Players = new();
        public List<CwmExtra.AirDesign> AirDesigns = new();
        public List<CwmExtra.ShipDesign> ShipDesigns = new();
        public List<CwmExtra.Wagon> Trains = new();
        public CwmData.ZoneGrid Zones = new();
        public CwmData.SpatialGrid Spatial = new();
        public CwmData.TerrainGrid Terrain = new();
        public List<CwmData.InfantryCell> InfantryCells = new();
    }

    /// <summary>Decode every section this file carries. The order follows
    /// cwm_export_entities.py: the buildings are read first because the targets
    /// and the deposits name them and the rail lines end on them.</summary>
    public static Doc Decode(CwmFile m)
    {
        var d = new Doc
        {
            Map = m,
            Entities = CwmData.Entities(m),
            Markers = CwmData.Markers(m),
            Buildings = CwmData.Buildings(m),
            Targets = CwmExtra.Targets(m),
            Deposits = CwmExtra.Deposits(m),
            Money = CwmExtra.Money(m),
            Progress = CwmExtra.Progresses(m),
            Special = CwmExtra.Specials(m),
            Market = CwmExtra.MarketOffers(m),
            RailNodes = CwmExtra.RailNodes(m),
            RailCells = CwmExtra.RailCells(m),
            Transports = CwmExtra.TransportLoads(m),
            TerraPlaces = CwmExtra.TerraPlaces(m),
            Players = CwmExtra.Players(m),
            AirDesigns = CwmExtra.AirDesigns(m),
            ShipDesigns = CwmExtra.ShipDesigns(m),
            Trains = CwmExtra.Trains(m),
            Zones = CwmData.Zones(m),
            Spatial = CwmData.Spatial(m),
            InfantryCells = CwmData.InfantryCells(m),
        };
        d.Terrain = CwmData.Terrain(m, d.Entities);
        d.Links = CwmExtra.Links(m, d.Buildings);

        var bySlot = new Dictionary<int, CwmData.Building>();
        foreach (var b in d.Buildings) bySlot[b.Slot] = b;
        foreach (var t in d.Targets)
            if (bySlot.TryGetValue(t.Building, out var b)) { t.BuildingName = b.Name; t.BuildingOwner = b.Owner; }
        foreach (var x in d.Deposits)
            if (bySlot.TryGetValue(x.Building, out var b)) x.BuildingName = b.Name;
        return d;
    }

    public static string Write(CwmFile m) => Write(Decode(m));

    /// <summary><paramref name="name"/> is what the file is called without its
    /// `map_` prefix, which is not always the file stem: the three saved
    /// missions ship as DM_1, DM_3 and DM_4 so they cannot collide with the
    /// campaign levels 1, 3 and 4. Left out, the stem is used.</summary>
    public static string Write(Doc d, string? name = null)
    {
        var m = d.Map;
        var w = new J(1 << 20);
        w.Obj();
        w.Str("map", name ?? m.Stem).Str("mission", m.Mission);
        w.Num("tileset", m.Tileset).Num("width", m.Width).Num("height", m.Height);

        w.Key("counts").Obj();
        w.Num("entities", d.Entities.Count).Num("markers", d.Markers.Count);
        w.Num("buildings", d.Buildings.Count).Num("targets", d.Targets.Count);
        w.End();

        w.Key("entities").Arr();
        foreach (var e in d.Entities)
        {
            w.Obj();
            w.Num("slot", e.Slot).Num("player_block", e.PlayerBlock);
            w.Num("col", e.Col).Num("row", e.Row);
            w.Num("owner", e.Owner).Num("facing", e.Facing).Num("aim", e.Aim);
            w.Num("owner_old", e.Facing).Num("team", e.Owner);
            // ⚠ 16.08.2026 — auch hier heissen die Schluessel jetzt, was die
            // Felder sind. `+0x0A` ist das, was `Can_go` selbst als »unit type:«
            // druckt (Formatzeile @0x4F6734) — unser `unit_type` ist +0x0F, also
            // braucht es zwei verschiedene Namen. `+0x26` ist der ANGRIFF, den
            // das Einheitenfenster als »A/V « ausgibt.
            // Beide wurden aus dieser Datei nie gelesen, sie standen nur falsch
            // beschriftet darin.
            w.Num("kind", e.Kind).Num("game_unit_type", e.GameUnitType);
            w.Num("unit_type", e.UnitType).Num("attack", e.Attack);
            // ⚠ 16.08.2026 — die Schluessel heissen jetzt, was die Felder SIND.
            // +0x2e/+0x30 ist der TANK und +0x08/+0x29 das Leben (Korrektur vom
            // 26.07.2026). Bis heute standen die Tankwerte unter »hp«/»hp_max«,
            // und der Rueckfall in MapEntityLayer.Load zog daraus BEIDES.
            // `energie`/`energie_max` sind neu und standen vorher gar nicht in
            // der Datei — ohne `raw` war das Leben schlicht nicht ablesbar.
            w.Num("code_base", e.CodeBase);
            w.Num("fuel", e.Fuel).Num("fuel_max", e.FuelMax);
            w.Num("energie", e.Energie).Num("energie_max", e.EnergieMax);
            if (e.FootW > 0) w.Num("foot_w", e.FootW).Num("foot_h", e.FootH);
            w.Str("raw", Hex(e.Raw));
            w.End();
        }
        w.End();

        w.Key("markers").Arr();
        foreach (var k in d.Markers)
        {
            w.Obj();
            w.Num("slot", k.Slot).Num("col", k.Col).Num("row", k.Row).Num("type", k.Type);
            w.Key("extra").Arr();
            foreach (int x in k.Extra) w.Num(x);
            w.End();
            w.Str("raw", Hex(k.Raw));
            w.End();
        }
        w.End();

        w.Key("buildings").Arr();
        foreach (var b in d.Buildings)
        {
            w.Obj();
            w.Num("slot", b.Slot).Num("type", b.Type).Num("owner", b.Owner);
            w.Str("name", b.Name).Num("cis_typ", b.CisTyp).Num("rail", b.Rail);
            w.Num("doors", b.Doors).Num("ident", b.Ident);
            // where it is entered — Doors cells of three bytes each from +0x35,
            // door 0 first (that is the one the capture block uses) — and the
            // "is a real building" flag +0x18, the capture block's other gate
            w.Num("door_col", b.DoorCol).Num("door_row", b.DoorRow);
            w.Num("built", b.IsBuilt);
            if (b.DoorCells.Count > 0)
            {
                w.Key("door_cells").Arr();
                foreach (var (dc, dr) in b.DoorCells) { w.Obj(); w.Num("col", dc).Num("row", dr); w.End(); }
                w.End();
            }
            w.Num("w", b.StockW).Num("ch", b.StockF).Num("sp", b.StockS);
            w.Num("terranium", b.Terranium);
            w.Num("hp", b.Hp).Num("hp_max", b.HpMax);
            w.Num("col", b.Col).Num("row", b.Row);
            // the ground it covers, from the spatial grid — 0 where the grid
            // holds no handle for it (the sec3 list carries entries that are
            // not buildings on the map at all)
            if (b.FootW > 0) w.Num("foot_w", b.FootW).Num("foot_h", b.FootH);
            if (b.Shipyard.HasValue) w.Num("shipyard", b.Shipyard.Value);
            if (b.State.HasValue)
            {
                w.Num("state", b.State.Value).Str("state_name", b.StateName ?? "");
                if (b.EffNum.HasValue)
                {
                    w.Num("eff_num", b.EffNum.Value).Num("eff_den", b.EffDen!.Value);
                    w.Num("prod_speed", b.ProdSpeed!.Value).Num("upgrade_step", b.UpgradeStep!.Value);
                    w.Num("capacity", b.Capacity!.Value);
                    w.Num("cost_store", b.CostStore!.Value).Num("cost_prod", b.CostProd!.Value);
                }
            }
            if (b.HangarSize.HasValue)
            {
                w.Num("hangar_size", b.HangarSize.Value);
                w.Key("hangar").Arr();
                foreach (int v in b.Hangar!) w.Num(v);
                w.End();
            }
            w.End();
        }
        w.End();

        w.Key("targets").Arr();
        foreach (var t in d.Targets)
        {
            w.Obj();
            w.Num("player", t.Player).Num("slot", t.Slot).Num("type", t.Type);
            w.Num("importance", t.Importance).Num("building", t.Building);
            w.Num("destroyed", t.Destroyed);
            w.Str("building_name", t.BuildingName).Num("building_owner", t.BuildingOwner);
            w.End();
        }
        w.End();

        w.Key("deposits").Arr();
        foreach (var x in d.Deposits)
        {
            w.Obj();
            w.Num("slot", x.Slot).Num("building", x.Building).Num("capacity", x.Capacity);
            w.Num("grade", x.Grade).Num("terranium", x.Terranium);
            w.Str("building_name", x.BuildingName);
            w.End();
        }
        w.End();

        w.Key("money").Arr();
        foreach (int v in d.Money) w.Num(v);
        w.End();

        w.Key("progress").Arr();
        foreach (var p in d.Progress)
        {
            w.Obj();
            w.Num("slot", p.Slot).Num("total", p.Total).Num("done", p.Done);
            w.Num("percent", p.Percent).Str("raw", Hex(p.Raw));
            w.End();
        }
        w.End();

        // ⚠ Die Ware des MARKTES (Typ 17). Bis zum 16.08.2026 wurden sec94 und
        // sec95 gar nicht gelesen — der Markt fehlte vollstaendig, und der Typ
        // galt als »Deko des Editors«. Siehe CwmExtra.MarketOffers.
        w.Key("market").Arr();
        foreach (var o in d.Market)
        {
            w.Obj();
            w.Num("slot", o.Slot).Num("price", o.Price).Num("design", o.Design);
            w.Num("unit_type", o.UnitType).Num("game_unit_type", o.GameUnitType);
            w.Num("energie", o.Energie).Num("attack", o.Attack).Num("defence", o.Defence);
            w.Num("speed", o.Speed).Num("sight", o.Sight).Num("range", o.Range);
            // ⚠ +0x28, die Erfahrung — ohne sie ist der Ladenpreis nicht
            // nachrechenbar. Siehe CwmExtra.MarketOffer.Experience.
            w.Num("experience", o.Experience);
            w.End();
        }
        w.End();

        w.Key("special").Arr();
        foreach (var s in d.Special)
        {
            w.Obj();
            w.Num("slot", s.Slot).Num("col", s.Col).Num("row", s.Row).Num("kind", s.Kind);
            w.Str("name", s.Name).Bool("stored", s.Stored);
            w.Num("speed", s.Speed).Num("hp", s.Hp).Num("hp_max", s.HpMax);
            w.Num("ammo", s.Ammo).Num("ammo_max", s.AmmoMax);
            w.Num("fuel", s.Fuel).Num("fuel_max", s.FuelMax);
            w.Num("payload", s.Payload).Num("airframe", s.Airframe);
            w.Num("attack", s.Attack).Num("defence", s.Defence).Num("sight", s.Sight);
            w.Num("owner", s.Owner).Num("cargo", s.Cargo).Num("customer", s.Customer);
            // ⚠ Ohne diese fünf steht jedes Flugzeug still — siehe
            // CwmExtra.Special.FineX für die gelesene Flugbewegung (D6).
            w.Num("fine_x", s.FineX).Num("fine_y", s.FineY).Num("dir", s.Dir);
            w.Num("order", s.Order).Num("order2", s.Order2);
            w.End();
        }
        w.End();

        w.Key("rail_nodes").Arr();
        foreach (var n in d.RailNodes)
        {
            w.Obj();
            w.Num("node", n.Node).Num("building", n.Building);
            w.Key("links").Arr();
            foreach (int v in n.Links) w.Num(v);
            w.End();
            w.End();
        }
        w.End();

        w.Key("links").Arr();
        foreach (var l in d.Links)
        {
            w.Obj();
            w.Num("slot", l.Slot).Num("node1", l.Node1).Num("node2", l.Node2);
            w.Num("bud1", l.Bud1).Num("bud2", l.Bud2);
            w.Num("x1", l.X1).Num("x2", l.X2);
            w.NumOrNull("y1", l.Y1).NumOrNull("y2", l.Y2);
            w.Num("delka", l.Delka).Num("faze", l.Faze);
            if (l.End1.HasValue) w.Num("end1", l.End1.Value).Num("end2", l.End2!.Value);
            if (l.Route != null)
            {
                w.Key("route").Arr();
                foreach (var (x, y) in l.Route) { w.Arr(); w.Num(x); w.Real(y); w.End(); }
                w.End();
                w.Key("pieces").Arr();
                foreach (int v in l.Pieces!) w.Num(v);
                w.End();
            }
            w.End();
        }
        w.End();

        // sec22 — DAS GLEIS selbst, Zelle fuer Zelle mit seinem Bild. Kurz
        // geschrieben (fuenf Zahlen je Zelle statt fuenf Schluessel), weil eine
        // NET-Karte davon 1341 hat: Platz, Spalte, Zeile, Bild, Linie.
        // Die Trefferpunkte kommen mit, damit ein Spielstand seinen Schaden
        // behaelt (4.DM traegt 45 zerschossene Stuecke).
        w.Key("rail_cells").Arr();
        foreach (var c in d.RailCells)
        {
            w.Arr();
            w.Num(c.Index); w.Num(c.Col); w.Num(c.Row);
            w.Num(c.Frame); w.Num(c.Line); w.Num(c.Hp);
            w.End();
        }
        w.End();

        // Ein Transporter, den die Karte BELADEN ausliefert (sec37). Knapp
        // geschrieben: Satz, Traeger, Deckel, dann die Fracht. Sie ist selten
        // (30 Saetze auf 7 von 62 Dateien), aber ohne sie stehen auf 05.CWM
        // fuenfzehn Einheiten neben dem Schiff statt darin.
        // sec38 — die Stellen, auf die man eine Mine setzen darf. NICHT
        // dasselbe wie "deposits" (sec28, der Zustand vorhandener Minen).
        w.Key("terra_places").Arr();
        foreach (var tp in d.TerraPlaces)
        {
            w.Obj();
            w.Key("slot").Num(tp.Slot);
            w.Key("col").Num(tp.Col);
            w.Key("row").Num(tp.Row);
            w.Key("amount").Num(tp.Amount);
            w.Key("kind").Num(tp.Kind);
            w.End();
        }
        w.End();

        w.Key("transports").Arr();
        foreach (var t in d.Transports)
        {
            w.Obj();
            w.Key("slot").Num(t.Slot);
            w.Key("carrier").Num(t.Carrier);
            w.Key("cap").Num(t.Cap);
            w.Key("cargo").Arr();
            foreach (int g in t.Cargo) w.Num(g);
            w.End();
            w.End();
        }
        w.End();

        w.Key("players").Arr();
        foreach (var p in d.Players)
        {
            w.Obj();
            w.Num("player", p.Index).Num("flag", p.Flag);
            w.Str("name", p.Name).Str("comment", p.Comment);
            w.Key("allies").Arr();
            foreach (int v in p.Allies) w.Num(v);
            w.End();
            w.Num("kills", p.Kills).Num("losses", p.Losses);
            w.Bool("beaten", p.Beaten).Bool("human", p.Human);
            w.End();
        }
        w.End();

        w.Key("air_designs").Arr();
        foreach (var a in d.AirDesigns)
        {
            w.Obj();
            w.Num("slot", a.Slot).Num("player", a.Player).Num("index", a.Index);
            w.Num("enable", a.Enable).Str("name", a.Name).Str("short", a.Short);
            w.Num("speed", a.Speed).Num("hp", a.Hp).Num("payload", a.Payload);
            w.Num("airframe", a.Airframe).Num("attack", a.Attack).Num("defence", a.Defence);
            w.Num("sight", a.Sight).Num("ammo", a.Ammo).Num("fuel", a.Fuel);
            // Preis in Waffen-/Fahrwerk-/Spezialteilen (+0x1F/+0x20/+0x21) — die
            // drei Bytes, die `build_in_airport` @0x4BB3D0 gegen die Lager des
            // Flughafens haelt. Ohne sie musste die KI die Kosten fest im Code
            // fuehren, also fuer alle acht Spieler dieselben.
            w.Num("cost_w", a.CostW).Num("cost_f", a.CostF).Num("cost_s", a.CostS);
            w.End();
        }
        w.End();

        w.Key("ship_designs").Arr();
        foreach (var s in d.ShipDesigns)
        {
            w.Obj();
            w.Num("enable", s.Enable).Str("name", s.Name);
            w.Num("weapon", s.Weapon).Num("chassis", s.Chassis).Num("variant", s.Variant);
            w.Num("cost_w", s.CostW).Num("cost_ch", s.CostF).Num("cost_sp", s.CostS);
            w.Num("speed", s.Speed).Num("energie", s.Energie);
            w.Num("attack", s.Attack).Num("defence", s.Defence);
            w.Num("range1", s.Range1).Num("range2", s.Range2);
            w.Num("sight", s.Sight).Num("ammo", s.Ammo).Num("fuel", s.Fuel);
            w.Num("reload", s.Reload);
            w.Num("slot", s.Slot).Num("player", s.Player).Num("index", s.Index);
            w.End();
        }
        w.End();

        w.Key("trains").Arr();
        foreach (var t in d.Trains)
        {
            w.Obj();
            w.Num("slot", t.Slot).Num("train", t.Train).Num("wagon", t.WagonNo);
            w.Num("line", t.Line).Num("step", t.Step).Num("piece", t.Piece);
            w.Num("col", t.Col).Num("col2", t.Col2).Num("row", t.Row);
            w.Num("y_half", t.YHalf).Num("speed", t.Speed).Str("raw", Hex(t.Raw));
            w.End();
        }
        w.End();

        w.Key("zones").Obj();
        w.Num("stride", d.Zones.Stride).Num("width", d.Zones.Width).Num("height", d.Zones.Height);
        w.Key("hist").Obj();
        foreach (var (v, c) in d.Zones.Histogram) w.Num(v.ToString(CultureInfo.InvariantCulture), c);
        w.End();
        w.Key("grid").Arr();
        foreach (var row in d.Zones.Grid)
        {
            w.Arr();
            foreach (byte v in row) w.Num(v);
            w.End();
        }
        w.End();
        w.End();

        // The passability the game itself uses — Can_go @0x4055D0 over the imap
        // (sec6). Written run length encoded, row major: pairs of [class, run].
        // 0 free, 1 rough (foot and hover only), 2 water (ships and hover),
        // 3 blocked. See CwmData.Terrain for what is read and what is inferred.
        w.Key("terrain").Obj();
        w.Num("width", d.Terrain.Width).Num("height", d.Terrain.Height);
        w.Str("legend", "0=free 1=rough 2=water 3=blocked");
        w.Str("source", "imap sec6 (0xbdea80) via Can_go @0x4055D0");
        w.Num("inferred", d.Terrain.Inferred);
        w.Key("hist").Arr();
        foreach (int c in d.Terrain.Histogram) w.Num(c);
        w.End();
        w.Key("unknown").Arr();
        foreach (int v in d.Terrain.Unknown) w.Num(v);
        w.End();
        w.Key("rle").Arr();
        {
            var cells = d.Terrain.Cells;
            int i = 0;
            while (i < cells.Length)
            {
                byte v = cells[i];
                int run = 1;
                while (i + run < cells.Length && cells[i + run] == v) run++;
                w.Arr(); w.Num(v); w.Num(run); w.End();
                i += run;
            }
        }
        w.End();
        w.End();

        w.Key("infantry_cells").Arr();
        foreach (var c in d.InfantryCells)
        {
            w.Obj();
            w.Num("index", c.Index).Num("col", c.Col).Num("row", c.Row);
            w.Key("slots").Arr();
            foreach (int s in c.Slots) w.Num(s);
            w.End();
            w.End();
        }
        w.End();

        w.Key("spatial").Obj();
        w.Num("stride", d.Spatial.Stride).Str("order", d.Spatial.Order);
        w.Key("nonempty").Arr();
        foreach (var c in d.Spatial.NonEmpty)
        {
            w.Obj();
            w.Num("col", c.Col).Num("row", c.Row).Num("value", c.Value);
            w.Str("ref_kind", c.RefKind).Num("ref", c.Ref);
            w.End();
        }
        w.End();
        w.Key("top_values").Arr();
        foreach (var (v, c) in d.Spatial.TopValues) { w.Arr(); w.Num(v); w.Num(c); w.End(); }
        w.End();
        w.End();

        w.End();
        return w.ToString();
    }

    private static string Hex(byte[] b)
    {
        var sb = new StringBuilder(b.Length * 2);
        foreach (byte x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>A minimal JSON writer — the project has no serializer and the
    /// existing writers in <see cref="ContentBuilder"/> build their text the
    /// same way. It tracks nothing but whether a comma is due.</summary>
    private sealed class J
    {
        private readonly StringBuilder _sb;
        private readonly List<char> _open = new();
        private bool _comma;

        public J(int capacity) { _sb = new StringBuilder(capacity); }

        private void Sep() { if (_comma) _sb.Append(','); _comma = true; }

        public J Obj() { Sep(); _sb.Append('{'); _open.Add('}'); _comma = false; return this; }
        public J Arr() { Sep(); _sb.Append('['); _open.Add(']'); _comma = false; return this; }

        public J End()
        {
            _sb.Append(_open[^1]);
            _open.RemoveAt(_open.Count - 1);
            _comma = true;
            return this;
        }

        public J Key(string k) { Sep(); Quote(k); _sb.Append(':'); _comma = false; return this; }

        public J Str(string k, string v) { Key(k); Quote(v); _comma = true; return this; }
        public J Num(string k, long v) { Key(k); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); _comma = true; return this; }
        public J Bool(string k, bool v) { Key(k); _sb.Append(v ? "true" : "false"); _comma = true; return this; }
        public J NumOrNull(string k, int? v)
        {
            Key(k);
            _sb.Append(v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "null");
            _comma = true;
            return this;
        }

        public J Num(long v) { Sep(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public J Real(double v) { Sep(); _sb.Append(v.ToString("0.0##", CultureInfo.InvariantCulture)); return this; }

        private void Quote(string s)
        {
            _sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') _sb.Append('\\').Append(c);
                else if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4"));
                else _sb.Append(c);
            }
            _sb.Append('"');
        }

        public override string ToString() => _sb.ToString();
    }
}
