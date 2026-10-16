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
    public string SaveStateJson(string mapName, string label, Core.SaveGame.PlatzKopf? platz = null)
    {
        var w = new Core.SaveGame.Writer();
        w.Open();
        w.Num("format", Core.SaveGame.Format);
        w.Str("label", label);
        // bug-522: der Kopf des Originals (Schreiber 0x41D210) — +5 Name, +26
        // Mission, +55 Spielername; bei uns als JSON-Felder (Format bleibt UNSERES).
        if (platz is { } pk)
        {
            w.Num("platz", pk.Platz);
            w.Str("platz_name", pk.Name);
            w.Num("platz_mission", pk.Mission);
            w.Str("platz_spieler", pk.Spieler);
        }
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
        // ⚠ bug-506: VORAB, nicht im Schreiblauf — eine Garage-Einheit kann vor
        // ihrem Gebaeude in der Liste stehen und muss es trotzdem benennen.
        var stelleImStand = new int[_entities.Count];
        int geschrieben = 0;
        for (int q = 0; q < stelleImStand.Length; q++)
            stelleImStand[q] = _entities[q].IsProp ? -1 : geschrieben++;
        int Stelle(Entity? x)
        {
            if (x == null) return -1;
            int i = _entities.IndexOf(x);
            return i >= 0 ? stelleImStand[i] : -1;
        }
        static string Liste(IEnumerable<int> l) => "[" + string.Join(",", l) + "]";

        // Ein Satz Felder, EINMAL — die Ladung an Bord wird mit denselben
        // geschrieben. Zwei Schreibwege waeren zwei Wahrheiten darueber, was
        // eine Einheit ausmacht.
        void SchreibeEinheit(Entity e)
        {
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
            w.Num("faze", e.Faze);   // 02.10.2026, bug-400 (+0x09)
            w.Bool("building", e.IsBuilding).Bool("dead", e.Dead).Bool("dug_in", e.DugIn);
            w.Num("dead_time", e.DeadTime);
            w.Num("doors", e.Doors).Num("built", e.Built);
            w.Num("stock_w", e.StockW).Num("stock_f", e.StockF)
             .Num("stock_s", e.StockS).Num("stock_t", e.StockT);
            w.Num("deposit", e.Deposit).Num("grade", e.Grade);
            w.Num("build_time", e.BuildTime).Num("build_index", e.BuildIndex);
            w.Num("shown_owner", e.ShownOwner);
            if (e.Name.Length > 0) w.Str("name", e.Name);
            // ---- Format 2 (bug-506/507, KayelGee R5#16/#18) -----------------
            // Was das Spiel waehrend des Spielens AENDERT und bis heute beim
            // Laden auf den Standardwert fiel. Was der Kartenlader aus dem Inhalt
            // ableitet, kommt beim Laden aus der Vorlage (siehe ApplySaveState);
            // es steht trotzdem hier, damit auch ein SELBST GEBAUTES Gebaeude,
            // das keine Vorlage hat, vollstaendig zurueckkommt.
            w.Num("ukol", e.Ukol).Num("in_gebaeude", Stelle(e.InGebaeude));
            w.Num("bild_art", e.BildArt).Num("foot_w", e.FootW).Num("foot_h", e.FootH);
            w.Bool("no_structure", e.NoStructure).Bool("is_target", e.IsTarget).Num("mark", e.Mark);
            w.Num("entwurf3e", e.Entwurf3E).Num("reload", e.Reload).Num("range_min", e.RangeMin)
             .Num("rating28", e.Rating28).Num("erfahrung", e.Erfahrung).Num("part", e.Part)
             .Num("radar_charges", e.RadarCharges);
            w.Num("armor", e.Armor).Num("bauzustand", e.Bauzustand).Num("schadensstufe", e.Schadensstufe)
             .Num("capture_total", e.CaptureTotal).Num("capture_progress", e.CaptureProgress)
             .Num("intruder", e.Intruder).Num("upgrade_step", e.UpgradeStep)
             .Num("prod_speed", e.ProdSpeed).Num("capacity", e.Capacity)
             .Num("cost_store", e.CostStore).Num("cost_prod", e.CostProd)
             .Num("eff_num", e.EffNum).Num("eff_den", e.EffDen).Num("hangar_size", e.HangarSize)
             .Num("research_tech", e.ResearchTech).Num("research_done", e.ResearchDone)
             .Num("deposit_start", e.DepositStart);
            if (e.IsBuilding)
            {
                var tueren = new List<int>();
                foreach (var (c, r) in e.DoorCells) { tueren.Add(c); tueren.Add(r); }
                w.Raw("door_cells", Liste(tueren));
                w.Raw("depot", Liste(e.Depot));
                w.Raw("build_queue", Liste(e.BuildQueue));
                var gar = new List<int>();
                foreach (var u in e.Garage) gar.Add(Stelle(u));
                w.Raw("garage", Liste(gar));
                if (e.Hangar != null) w.Raw("hangar", Liste(e.Hangar));
            }
        }

        w.ArrayStart("entities");
        for (int ei = 0; ei < _entities.Count; ei++)
        {
            var e = _entities[ei];
            if (e.IsProp) continue;                 // scenery comes back from the map
            w.ItemStart();
            SchreibeEinheit(e);
            w.Num("unload_rest", e.UnloadRest)
             .Num("unload_col", e.UnloadCell.X).Num("unload_row", e.UnloadCell.Y);
            w.ItemEnd();
        }
        w.ArrayEnd();

        // ---- die Ladung AN BORD --------------------------------------------
        //
        // ⚠ Sie steht nicht in `entities` — sie ist ja nicht auf der Karte.
        // Ohne diese Zeilen waere die Ladung eines Frachters beim Speichern
        // weg, und das waere heute der DRITTE Fall derselben Art (Merkpunkte,
        // Bahnfahrten, Ladung). Wer etwas aus der Einheitenliste nimmt, muss
        // es im Spielstand wiederfinden.
        w.ArrayStart("cargo");
        foreach (var kv in _fracht)
            foreach (var u in kv.Value)
            {
                w.ItemStart();
                w.Num("carrier", kv.Key);
                SchreibeEinheit(u);
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

        // ---- die Einheiten, die gerade mit der Bahn unterwegs sind ---------
        //
        // ⚠ Ohne diese Zeilen wäre eine verlegte Einheit beim Speichern WEG:
        // aus dem Quelldepot ist sie heraus, im Zieldepot noch nicht. Genau
        // die Fehlerklasse, die die Merkpunkte oben hatten — nur dass hier
        // etwas verschwindet statt etwas Falsches stehenzubleiben.
        w.ArrayStart("rail_transfers");
        foreach (var t in RailTransfers)
        {
            w.ItemStart();
            w.Num("design", t.Design).Num("unit", t.Einheit != null ? _entities.IndexOf(t.Einheit) : -1)
             .Num("at", t.At).Num("dest", t.DestNode)
             .Num("owner", t.Owner).Num("line", t.Line).Bool("riding", t.Riding);
            var r = new System.Text.StringBuilder("[");
            for (int i = 0; i < t.Route.Length; i++)
            { if (i > 0) r.Append(','); r.Append(t.Route[i]); }
            w.Raw("route", r.Append(']').ToString());
            w.ItemEnd();
        }
        w.ArrayEnd();

        // ---- die WARENSCHALTER der Bahnlinien (22.09.2026) ------------------
        // ⚠ Seit sie von Hand gesetzt werden (Befehl 500), sind sie Zustand —
        // ohne diese Zeilen setzte das Laden sie auf die Matrix zurueck.
        w.ArrayStart("rail_modes");
        foreach (var (slot, mode, ownA, ownB) in RailModesForSave())
        {
            w.ItemStart();
            w.Num("line", slot).Num("m0", mode[0]).Num("m1", mode[1]).Num("m2", mode[2]).Num("m3", mode[3])
             .Num("own_a", ownA).Num("own_b", ownB);
            w.ItemEnd();
        }
        w.ArrayEnd();

        // ---- die schon gezeigte KONTEXTHILFE --------------------------------
        //
        // ⚠ Kampagnenweit, nicht je Mission: im Original liegen die Merker in
        // v[346…381], und v[300…499] gehen beim Missionswechsel MIT ueber
        // (0x4CFD80 nullt nur v[0…299]). Ohne diese Zeilen kaeme jedes
        // Hinweisfenster nach dem Laden wieder.
        var hin = new System.Text.StringBuilder("[");
        bool ersterHinweis = true;
        foreach (int v in Campaign.CampaignHints.Gezeigt)
        {
            if (!ersterHinweis) hin.Append(',');
            ersterHinweis = false;
            hin.Append(v);
        }
        w.Raw("hints_shown", hin.Append(']').ToString());

        // ⭐ sec71 — welche HILFETEXTE schon gezeigt wurden. Im Original ist das
        // ein eigener Abschnitt des Spielstands (C 0x87AE00, 500 B, ein Byte je
        // Textnummer), kein Laufzeitwert; er MUSS also mit in die Datei.
        // ⚠ Er ueberlebt keinen Missionswechsel — der Kartenlader nullt ihn —,
        // aber er ueberlebt ein Speichern und Laden, und das tat er bei uns
        // bisher nicht.
        var htx = new System.Text.StringBuilder("[");
        bool ersterText = true;
        foreach (int id in UI.HelpWindow.GezeigteTexte)
        {
            if (!ersterText) htx.Append(',');
            ersterText = false;
            htx.Append(id);
        }
        w.Raw("help_texts_shown", htx.Append(']').ToString());

        w.ArrayStart("marks");
        for (int i = 0; i < Marks.Length; i++)
        {
            w.ItemStart();
            w.Num("i", i).Num("col", Marks[i].Col).Num("row", Marks[i].Row);
            if (Marks[i].Name.Length > 0) w.Str("name", Marks[i].Name);
            w.ItemEnd();
        }
        w.ArrayEnd();

        // ---- die FLUGZEUGE (bug-507, KayelGee R5#18) --------------------------
        // ⚠ Sie standen in keiner Datei: nach dem Laden kam die Luftflotte des
        // Kartenstarts zurueck, gekaufte Nachschubhelis waren weg. Geschrieben
        // wird JEDES oeffentliche Feld von Special — ein Feld, das spaeter
        // hinzukommt, geht damit von selbst mit (die Lehre aus den Gebaeuden).
        w.ArrayStart("aircraft");
        foreach (var a in _special)
        {
            w.ItemStart();
            SchreibeLuft(w, a, stelleImStand);
            w.ItemEnd();
        }
        w.ArrayEnd();

        // ---- der Zustand des MISSIONSSKRIPTS (bug-508) ------------------------
        if (_mscript != null) w.Raw("script", _mscript.ZustandJson());

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

        // ⭐ bug-506 (KayelGee R5#16): DIE VORLAGEN AUS DER FRISCH GELADENEN KARTE.
        // Bis heute wurde jedes Gebaeude aus den ~40 Feldern der Datei neu
        // gebaut — ohne BildArt (unsichtbar, darunter Wasser und Fels), ohne
        // Fussabdruck (Klickflaeche eine Zelle), ohne Tueren, Marke, Siegziel.
        // Jetzt beginnt ein Satz, den es in der Karte gibt, als KOPIE seines
        // Kartensatzes, und die Datei legt nur ihren Zustand darueber. Was der
        // Kartenlader ableitet, kann so nie mehr fehlen. Zugeordnet wird ueber
        // den Platz — ein Gebaeude nur an DERSELBEN Zelle (ein Neubau kann den
        // Platz eines zerstoerten erben, bug-439), eine Einheit nur bei gleichem
        // Typ. --laden-vorlage-alt = der alte Leser.
        var vorlagen = new Dictionary<(int, bool), Entity>();
        if (!LadenVorlageAlt)
            foreach (var e in _entities)
                if (!e.IsProp && e.Slot >= 0) vorlagen.TryAdd((e.Slot, e.IsBuilding), e);
        LadenVorlagen = 0;

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

        Entity BaueEinheit(GDict d)
        {
            int col = GetI(d, "col"), row = GetI(d, "row");
            int el = ElevOf(col, row);
            int slot = GetI(d, "slot", -1);
            bool bau = GetB(d, "building");
            Entity? v = null;
            if (slot >= 0 && vorlagen.TryGetValue((slot, bau), out var t)
                && (bau ? t.Col == col && t.Row == row
                        : t.UnitType == GetI(d, "unit_type", -1)))
            {
                v = t;
                vorlagen.Remove((slot, bau));
            }
            var e = v?.Klon() ?? new Entity();
            if (v != null)
            {
                // die Laufzeitlisten gehoeren dem Spielstand, nicht der Karte
                e.Garage.Clear(); e.Depot.Clear(); e.BuildQueue.Clear(); e.Orders.Clear();
                e.TorZustand.Clear();
                e.Path = null; e.SpritWeg = null; e.Reserved = null; e.InGebaeude = null; e.Target = -1;
            }
            int I(string k, int dflt) => d.TryGetValue(k, out var x) ? x.AsInt32() : dflt;
            e.Slot = slot; e.Col = col; e.Row = row; e.Elev = el;
            e.Owner = GetI(d, "owner", -1); e.Team = GetI(d, "team", -1);
            e.UnitType = GetI(d, "unit_type", -1); e.BType = GetI(d, "btype");
            e.Hp = GetI(d, "hp"); e.HpMax = GetI(d, "hp_max");
            e.Facing = GetI(d, "facing"); e.AimFacing = GetI(d, "aim", -1);
            e.Weapon = GetI(d, "weapon"); e.Equipment = GetI(d, "equipment");
            e.Ammo = GetI(d, "ammo"); e.AmmoMax = GetI(d, "ammo_max");
            e.Fuel = GetI(d, "fuel"); e.FuelMax = GetI(d, "fuel_max");
            e.Speed = GetI(d, "speed"); e.Range = GetI(d, "range"); e.Sight = GetI(d, "sight");
            e.Attack = GetI(d, "attack"); e.Defence = GetI(d, "defence");
            e.Infantry = GetI(d, "infantry", -1); e.Chassis = GetI(d, "chassis", -1);
            e.GameUnitType = GetI(d, "subclass", -1); e.Pose = GetI(d, "pose");
            e.Faze = GetI(d, "faze");
            e.Condition = GetI(d, "condition", 100); e.State = GetI(d, "state");
            e.IsBuilding = bau; e.Dead = GetB(d, "dead");
            e.DugIn = GetB(d, "dug_in");
            e.DeadTime = d.TryGetValue("dead_time", out var dt) ? (float)dt.AsDouble() : 0f;
            e.Doors = GetI(d, "doors"); e.Built = GetI(d, "built");
            e.StockW = GetI(d, "stock_w"); e.StockF = GetI(d, "stock_f");
            e.StockS = GetI(d, "stock_s"); e.StockT = GetI(d, "stock_t");
            e.Deposit = GetI(d, "deposit"); e.Grade = GetI(d, "grade");
            e.BuildTime = d.TryGetValue("build_time", out var bt) ? (float)bt.AsDouble() : 0f;
            e.BuildIndex = GetI(d, "build_index", -1);
            e.ShownOwner = GetI(d, "shown_owner", -1);
            e.Name = d.TryGetValue("name", out var nv) ? nv.AsString() : "";
            e.Mobile = !bau;

            // ---- Format 2: was fehlt, bleibt wie in der Vorlage ------------
            // ⚠ Unter --laden-vorlage-alt gilt der GANZE alte Leser: ohne diese
            // Felder. Sonst retten sie, was die Vorlage nicht mehr liefert, und
            // das Nullmodell saehe so gut aus wie die Behebung (gemessen: beide
            // DECKUNGSGLEICH).
            if (LadenVorlageAlt) d = AlteFelder(d);
            e.Ukol = I("ukol", v?.Ukol ?? 0);
            e.BildArt = I("bild_art", v?.BildArt ?? 0);
            e.FootW = I("foot_w", e.FootW); e.FootH = I("foot_h", e.FootH);
            if (d.TryGetValue("no_structure", out var ns)) e.NoStructure = ns.AsBool();
            else if (v == null && bau) e.NoStructure = e.BType is < 1 or > 16;
            if (d.TryGetValue("is_target", out var it)) e.IsTarget = it.AsBool();
            e.Mark = I("mark", e.Mark); e.Entwurf3E = I("entwurf3e", e.Entwurf3E);
            e.Reload = I("reload", e.Reload); e.RangeMin = I("range_min", e.RangeMin);
            e.Rating28 = I("rating28", e.Rating28); e.Erfahrung = I("erfahrung", e.Erfahrung);
            e.Part = I("part", e.Part); e.RadarCharges = I("radar_charges", e.RadarCharges);
            e.Armor = I("armor", e.Armor); e.Bauzustand = I("bauzustand", e.Bauzustand);
            e.Schadensstufe = I("schadensstufe", e.Schadensstufe);
            e.CaptureTotal = I("capture_total", e.CaptureTotal);
            e.CaptureProgress = I("capture_progress", e.CaptureProgress);
            e.Intruder = I("intruder", e.Intruder); e.UpgradeStep = I("upgrade_step", e.UpgradeStep);
            e.ProdSpeed = I("prod_speed", e.ProdSpeed); e.Capacity = I("capacity", e.Capacity);
            e.CostStore = I("cost_store", e.CostStore); e.CostProd = I("cost_prod", e.CostProd);
            e.EffNum = I("eff_num", e.EffNum); e.EffDen = I("eff_den", e.EffDen);
            e.HangarSize = I("hangar_size", e.HangarSize);
            e.ResearchTech = I("research_tech", e.ResearchTech);
            e.ResearchDone = I("research_done", e.ResearchDone);
            e.DepositStart = I("deposit_start", e.DepositStart);
            if (bau)
            {
                if (d.TryGetValue("door_cells", out var dc) && dc.VariantType == Variant.Type.Array)
                {
                    var a = dc.AsGodotArray();
                    e.DoorCells.Clear();
                    for (int k = 0; k + 1 < a.Count; k += 2) e.DoorCells.Add((a[k].AsInt32(), a[k + 1].AsInt32()));
                }
                if (d.TryGetValue("depot", out var dp) && dp.VariantType == Variant.Type.Array)
                    foreach (var q in dp.AsGodotArray()) e.Depot.Add(q.AsInt32());
                if (d.TryGetValue("build_queue", out var bq) && bq.VariantType == Variant.Type.Array)
                    foreach (var q in bq.AsGodotArray()) e.BuildQueue.Add(q.AsInt32());
                if (d.TryGetValue("hangar", out var hg) && hg.VariantType == Variant.Type.Array)
                {
                    e.Hangar = new List<int>();
                    foreach (var q in hg.AsGodotArray()) e.Hangar.Add(q.AsInt32());
                }
            }
            // Ein Gebaeude mit Vorlage behaelt deren Flaeche (gleiche Zelle); alles
            // andere rechnet sie neu, wie der Neubau (Construction.PlaceBuilding).
            if (v == null || !bau) e.Footprint = CellRect(_ox, _oy, col, row, el);
            e.Pos = CellCenter(e.Col, e.Row);
            e.UnloadRest = GetI(d, "unload_rest");
            e.UnloadCell = new Vector2I(GetI(d, "unload_col"), GetI(d, "unload_row"));
            if (v != null) LadenVorlagen++;
            return e;
        }

        if (root.TryGetValue("entities", out var ev) && ev.VariantType == Variant.Type.Array)
            foreach (var item in ev.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                listenstelle.Add(_entities.Count);
                _entities.Add(BaueEinheit(item.AsGodotDictionary<string, Variant>()));
            }

        // Die Ladung an Bord — dieselbe Bauzeile, nur ein anderer Platz.
        FrachtZuruecksetzen();
        if (root.TryGetValue("cargo", out var cgv) && cgv.VariantType == Variant.Type.Array)
            foreach (var item in cgv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                FrachtAufnehmen(GetI(d, "carrier", -1), BaueEinheit(d));
            }

        // ---- Garage und »in welchem Gebaeude« (bug-506) ----------------------
        // Erst jetzt: die Stellen zeigen in beide Richtungen.
        if (!LadenVorlageAlt && root.TryGetValue("entities", out var ev2) && ev2.VariantType == Variant.Type.Array)
        {
            var arr = ev2.AsGodotArray();
            for (int stelle = 0; stelle < arr.Count && stelle < listenstelle.Count; stelle++)
            {
                if (arr[stelle].VariantType != Variant.Type.Dictionary) continue;
                var d = arr[stelle].AsGodotDictionary<string, Variant>();
                var e = _entities[listenstelle[stelle]];
                int ig = GetI(d, "in_gebaeude", -1);
                if (ig >= 0 && ig < listenstelle.Count) e.InGebaeude = _entities[listenstelle[ig]];
                if (d.TryGetValue("garage", out var gv2) && gv2.VariantType == Variant.Type.Array)
                    foreach (var q in gv2.AsGodotArray())
                    {
                        int st = q.AsInt32();
                        if (st >= 0 && st < listenstelle.Count) e.Garage.Add(_entities[listenstelle[st]]);
                    }
            }
        }

        // ---- die Flugzeuge (bug-507) — nur ein Format-2-Stand traegt sie ------
        if (!LadenVorlageAlt && root.TryGetValue("aircraft", out var av) && av.VariantType == Variant.Type.Array)
        {
            _special.Clear();
            _selAir = -1;
            foreach (var item in av.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                _special.Add(LeseLuft(item.AsGodotDictionary<string, Variant>(), listenstelle));
            }
        }

        // ---- der Skriptzustand (bug-508) ------------------------------------
        // Das Skript entsteht erst im ersten Takt (MissionScriptTick); bis dahin
        // wartet der Zustand hier. Steht es schon (--save-check), sofort.
        _skriptZustand = !LadenVorlageAlt && root.TryGetValue("script", out var sc) && sc.VariantType == Variant.Type.Dictionary
            ? sc.AsGodotDictionary<string, Variant>() : null;
        if (_skriptZustand != null && _mscript != null)
        {
            _mscript.ZustandSetzen(_skriptZustand);
            _skriptZustand = null;
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

        Campaign.CampaignHints.Vergiss();
        if (root.TryGetValue("hints_shown", out var hsv) && hsv.VariantType == Variant.Type.Array)
            foreach (var q in hsv.AsGodotArray()) Campaign.CampaignHints.Merke(q.AsInt32());

        // ⚠ KEIN Vergessen davor: HelpWindow.Forget() laeuft beim Missionsstart
        // und hat die Tafel schon geleert — genau wie der Kartenlader des
        // Originals. Hier wird sie aus der Datei wieder gefuellt.
        if (root.TryGetValue("help_texts_shown", out var htv) && htv.VariantType == Variant.Type.Array)
            foreach (var q in htv.AsGodotArray()) UI.HelpWindow.MerkeGezeigt(q.AsInt32());

        if (root.TryGetValue("rail_modes", out var rmv) && rmv.VariantType == Variant.Type.Array)
            foreach (var item in rmv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                RailModeRestore(GetI(d, "line", -1),
                    new[] { (byte)GetI(d, "m0", 2), (byte)GetI(d, "m1", 2), (byte)GetI(d, "m2", 2), (byte)GetI(d, "m3", 2) },
                    GetI(d, "own_a", -99), GetI(d, "own_b", -99));
            }

        RailTransfersClear();
        if (root.TryGetValue("rail_transfers", out var rt) && rt.VariantType == Variant.Type.Array)
            foreach (var item in rt.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                var route = new List<int>();
                if (d.TryGetValue("route", out var rv) && rv.VariantType == Variant.Type.Array)
                    foreach (var q in rv.AsGodotArray()) route.Add(q.AsInt32());
                if (route.Count == 0) continue;
                RailTransferRestore(GetI(d, "design"), route, GetI(d, "at"),
                                    GetB(d, "riding"), GetI(d, "dest", -1),
                                    GetI(d, "owner", -1), GetI(d, "line", -1),
                                    GetI(d, "unit", -1));
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

    /// <summary><c>--laden-vorlage-alt</c> — der GANZE Leser vor bug-506..508:
    /// jedes Gebaeude und jede Einheit aus den Format-1-Feldern allein, ohne
    /// Kartenvorlage, ohne Garage, Flugzeuge und Skriptzustand.</summary>
    public static bool LadenVorlageAlt;

    /// <summary>Die Format-1-Felder eines Satzes — fuer das Nullmodell.</summary>
    private static GDict AlteFelder(GDict d)
    {
        var alt = new GDict();
        foreach (var k in AltSchluessel)
            if (d.TryGetValue(k, out var v)) alt[k] = v;
        return alt;
    }

    private static readonly string[] AltSchluessel =
    {
        "slot", "col", "row", "owner", "team", "unit_type", "btype", "hp", "hp_max",
        "facing", "aim", "weapon", "equipment", "ammo", "ammo_max", "fuel", "fuel_max",
        "speed", "range", "sight", "attack", "defence", "infantry", "chassis", "subclass",
        "pose", "condition", "state", "faze", "building", "dead", "dug_in", "dead_time",
        "doors", "built", "stock_w", "stock_f", "stock_s", "stock_t", "deposit", "grade",
        "build_time", "build_index", "shown_owner", "name", "unload_rest", "unload_col", "unload_row",
    };

    /// <summary>Wie viele Saetze beim letzten Laden eine Kartenvorlage hatten.</summary>
    public static int LadenVorlagen;

    /// <summary>Wartet auf das Missionsskript, siehe ApplySaveState.</summary>
    private GDict? _skriptZustand;

    /// <summary>Fuer --spielstand-check: der Skriptzustand gleich nach dem
    /// Aufsetzen, als Text.</summary>
    public static string SkriptZustandGeladen = "";

    /// <summary>Felder eines Flugzeugs, die NICHT in die Datei gehen: der
    /// Zeichnerzustand (bug-505) wird nach dem Laden neu bestimmt.</summary>
    private static readonly HashSet<string> LuftOhne =
        new() { "HubPx", "HubLetztePos", "HuepfAlt", "HuepfHub", "Footprint" };

    private static readonly System.Reflection.FieldInfo[] LuftFelder =
        typeof(Special).GetFields(System.Reflection.BindingFlags.Public
                                  | System.Reflection.BindingFlags.Instance);

    private static string Zahl(float f)
        => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Jedes oeffentliche Feld eines Flugzeugs — ein Feld, das spaeter
    /// hinzukommt, geht damit von selbst mit. ⚠ Customer und Target sind
    /// LISTENSTELLEN in _entities und gehen als Stelle im Spielstand.</summary>
    private static void SchreibeLuft(Core.SaveGame.Writer w, Special a, int[] stelle)
    {
        foreach (var f in LuftFelder)
        {
            if (LuftOhne.Contains(f.Name)) continue;
            object? v = f.GetValue(a);
            string k = f.Name;
            if (k is "Customer" or "Target" && v is int ix)
            {
                w.Num(k, ix >= 0 && ix < stelle.Length ? stelle[ix] : -1);
                continue;
            }
            switch (v)
            {
                case null: break;                      // Vector2? ohne Wert: fehlt
                case int n: w.Num(k, n); break;
                case long n: w.Num(k, n); break;
                case float x: w.Num(k, x); break;
                case double x: w.Num(k, (float)x); break;
                case bool b: w.Bool(k, b); break;
                case string str: w.Str(k, str); break;
                case System.Enum en: w.Num(k, System.Convert.ToInt64(en)); break;
                case Vector2 p: w.Raw(k, $"[{Zahl(p.X)},{Zahl(p.Y)}]"); break;
                case Vector2I p: w.Raw(k, $"[{p.X},{p.Y}]"); break;
                case List<int> l: w.Raw(k, "[" + string.Join(",", l) + "]"); break;
            }
        }
    }

    private static Special LeseLuft(GDict d, List<int> listenstelle)
    {
        var a = new Special();
        foreach (var f in LuftFelder)
        {
            if (LuftOhne.Contains(f.Name)) continue;
            var t = f.FieldType;
            if (!d.TryGetValue(f.Name, out var v))
            {
                if (t == typeof(Vector2?) || t == typeof(Vector2I?)) f.SetValue(a, null);
                continue;
            }
            if (f.Name is "Customer" or "Target")
            {
                int st = v.AsInt32();
                f.SetValue(a, st >= 0 && st < listenstelle.Count ? listenstelle[st] : -1);
                continue;
            }
            if (t == typeof(int)) f.SetValue(a, v.AsInt32());
            else if (t == typeof(long)) f.SetValue(a, v.AsInt64());
            else if (t == typeof(float)) f.SetValue(a, (float)v.AsDouble());
            else if (t == typeof(double)) f.SetValue(a, v.AsDouble());
            else if (t == typeof(bool)) f.SetValue(a, v.AsBool());
            else if (t == typeof(string)) f.SetValue(a, v.AsString());
            else if (t.IsEnum) f.SetValue(a, System.Enum.ToObject(t, v.AsInt64()));
            else if ((t == typeof(Vector2) || t == typeof(Vector2?)) && v.VariantType == Variant.Type.Array)
            {
                var q = v.AsGodotArray();
                if (q.Count >= 2) f.SetValue(a, new Vector2((float)q[0].AsDouble(), (float)q[1].AsDouble()));
            }
            else if ((t == typeof(Vector2I) || t == typeof(Vector2I?)) && v.VariantType == Variant.Type.Array)
            {
                var q = v.AsGodotArray();
                if (q.Count >= 2) f.SetValue(a, new Vector2I(q[0].AsInt32(), q[1].AsInt32()));
            }
            else if (t == typeof(List<int>) && v.VariantType == Variant.Type.Array)
            {
                var l = new List<int>();
                foreach (var q in v.AsGodotArray()) l.Add(q.AsInt32());
                if (f.IsInitOnly && f.GetValue(a) is List<int> fest) { fest.Clear(); fest.AddRange(l); }
                else f.SetValue(a, l);
            }
        }
        return a;
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

            // Die Bahnfahrten gehen ueber ihren Inhalt ein, nicht ueber die
            // Anzahl: eine Fahrt, die mit falschem Wegindex zurueckkaeme,
            // waere sonst nicht von der richtigen zu unterscheiden.
            // Die Ladung an Bord geht ueber ihre Plaetze ein — sie steht nicht
            // in _entities und faellt oben durch jede Zaehlung.
            int anBord = 0, bpruef = 0;
            foreach (var kv in _fracht)
                foreach (var u in kv.Value)
                { anBord++; bpruef += (kv.Key + 1) * 31 + u.Slot * 3 + u.UnitType; }

            int hinweise = Campaign.CampaignHints.GezeigtCount, hpruef = 0;
            foreach (int v in Campaign.CampaignHints.Gezeigt) hpruef += v * 3 + 7;
            // ⚠ Die blosse ANZAHL wuerde eine vertauschte Menge nicht bemerken:
            // {48,72,19} und {1,2,3} sind beide "drei". Die Pruefzahl schon.
            int tpruef = 0;
            foreach (int id in UI.HelpWindow.GezeigteTexte) tpruef += id * 5 + 11;

            int fahrten = 0, fpruef = 0;
            foreach (var t in RailTransfers)
            {
                fahrten++;
                fpruef += (t.Design + 1) * 7 + (t.At + 1) * 13 + (t.DestNode + 1) * 17
                        + t.Route.Length * 23 + (t.Riding ? 29 : 0);
                foreach (int knoten in t.Route) fpruef += knoten;
            }

            return $"{n} Einheiten ({buildings} Gebaeude, {dead} tot), HP {hp}, " +
                   $"Besitzer {own}, Zellen {cells}, Lager {stock}, Geld {money}, Nebel {fog}, " +
                   $"{gruppen} Gruppen mit {mitglieder} Mitgliedern (Zellen {gzellen}, " +
                   $"Namen {gnamen}), {merk} Merkpunkte (Namen {mnamen}), " +
                   $"{fahrten} Bahnfahrten (Pruefzahl {fpruef}), " +
                   $"{anBord} an Bord (Pruefzahl {bpruef}), " +
                   $"{hinweise} Kontexthilfen (Pruefzahl {hpruef}), " +
                   $"{UI.HelpWindow.GezeigtCount} gezeigte Hilfetexte " +
                   $"(Pruefzahl {tpruef})";
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
        if (Campaign.CampaignHints.GezeigtCount == 0)
        {
            Campaign.CampaignHints.Merke(349);
            Campaign.CampaignHints.Merke(357);
            gelegt += " (zwei Kontexthilfen gelegt)";
        }
        // ⚠ Und dasselbe fuer sec71. "0 gezeigte Hilfetexte" vorher wie nachher
        // waere gruen, egal was der Schreiber tut — ein Prueflauf, der nichts zu
        // verlieren hat, kann nichts verlieren.
        if (UI.HelpWindow.GezeigtCount == 0)
        {
            UI.HelpWindow.MerkeGezeigt(48);
            UI.HelpWindow.MerkeGezeigt(72);
            UI.HelpWindow.MerkeGezeigt(19);
            gelegt += " (drei Hilfetexte gelegt)";
        }
        if (RailTransfers.Count == 0)
        {
            // Auch hier: ohne Gegenstand kann nichts verlorengehen.
            RailTransferStart(3, new List<int> { 5, 9, 12 }, 0);
            gelegt += " (eine Bahnfahrt gelegt)";
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
