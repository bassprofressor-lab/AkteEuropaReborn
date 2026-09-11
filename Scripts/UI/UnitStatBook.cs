namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Die Werteliste, die im Original rechts im Basis- und im Erstellungsfenster
/// steht — nachgeschlagen zu einem Entwurfsnamen.
///
/// <para><b>Warum es dieses Buch gibt.</b> Das Baumenü zeigt nicht nur Namen und
/// Preis, sondern zu der markierten Zeile zehn Werte. Die stehen alle im
/// abgeleiteten Schwanz des sec47-Satzes (ab +0x1a), den
/// <see cref="Simulation.DesignMath"/> längst liest — nur reicht
/// <c>MapEntityLayer.BuildPanelRows()</c> davon nichts nach aussen. Statt dort
/// einzugreifen schlägt das Fenster den Entwurf über seinen NAMEN in denselben
/// ausgeführten Tabellen nach, aus denen die Bauliste selbst entsteht:
/// <c>Maps/unit_designs.json</c> (die Sätze aus sec47) und
/// <c>user://designs.cfg</c> (was der Spieler selbst entworfen hat, dort stehen
/// nur die drei Bauteile, der Rest wird mit der Formel des Spiels gerechnet).
/// </para>
///
/// <para><b>Die Beschriftungen sind die des Originals</b>, alle aus GAME.EXE
/// (Datenteil, VA = Versatz + 0x402200):</para>
/// <code>
///   0x501790 "Energie : "   0x501a10 "Nachladen "   0x5017d8 "A/V "
///   0x5017cc "Geschw. "     0x5017c4 "Sicht "       0x501a00 "Reichw. "
///   0x501980 "Min Reichw. " 0x5019f8 "Sprit "       0x5019ec "Munition "
///   0x5019c0 " ("
/// </code>
/// <para>Die REIHENFOLGE ist ebenfalls gelesen und nicht geraten: im
/// Erstellungsfenster stehen die Verweise auf diese Zeichenketten der Reihe nach
/// bei 0x46d845 (Energie), 0x46d95d (A/V), 0x46dbf3 (Nachladen), 0x46dd24
/// (Geschw.), 0x46de33 (Sicht), 0x46df4b (Reichw.), 0x46e1e0 (Sprit), 0x46e2ef
/// (Munition) — genau die Folge, die auch auf dem Bildschirmfoto steht.</para>
///
/// <para><b>Das »(0)« hinter einem Bauteilnamen</b> ist gelesen, nicht erfunden.
/// Das Erstellungsfenster hat drei Bauteillisten und für jede einen eigenen
/// Drucker. Zwei davon — @0x46cc53 (Fahrwerk) und @0x46d050 (Verbesserung) —
/// hängen OHNE Bedingung <c>" ("</c> (0x5019c0) und
/// <c>byte [row*58 + 0x5045a1]</c> in Zehnerschreibweise an den Namen; das ist,
/// bezogen auf den Satzanfang, <b>+0x01</b>. Der dritte (@0x46d385, Aufbauteil)
/// prüft davor noch <c>byte [row*58 + 0x5045c2]</c>, also +0x22 — den
/// Spezialteilpreis. Wir hängen die Zahl <b>immer</b> an: auf dem
/// Bildschirmfoto steht sie hinter jedem Eintrag jeder Liste, auch hinter
/// »Maschinengewehr«, dessen +0x22 null ist.</para>
///
/// <para>Der kurze Bauteilname steht auf <b>+0x02</b> desselben Satzes (elf
/// Byte, danach beginnt bei +0x0d die Zahlenreihe): Zeile 4 heisst dort
/// »M-Gewehr«, Zeile 161 »Reifen« — dieselben zwei Wörter, die im
/// Bildschirmfoto des Originals in der Werteliste stehen. Der LANGE Name liegt
/// auf +0x25 (»Leichte Bordkanone« in Zeile 1) und ist der, den die drei
/// Bauteillisten des Erstellungsfensters zeigen.</para>
///
/// <para>⚠ <b>UNSERE Deutung</b> ist einzig die Aufteilung von »Reichw. a/b«:
/// a ist das gelesene +0x24 (<see cref="Simulation.DesignMath.Derived.Range"/>),
/// b nehmen wir als +0x22, weil das Original daneben die eigene Beschriftung
/// »Min Reichw. « führt (0x501980) und +0x22 über alle 586 Sätze fast immer 0
/// ist — passend zu dem »4/0« auf dem Bildschirmfoto. Belegt ist das nicht.
/// </para>
/// </summary>
public static class UnitStatBook
{
    /// <summary>Alles, was rechts im Fenster steht — zu einem Entwurf.</summary>
    public readonly struct Entry
    {
        public Entry(string name, int weapon, int prop, int equip,
                     Simulation.DesignMath.Derived d)
        {
            Name = name; Weapon = weapon; Propulsion = prop; Equip = equip;
            Derived = d;
        }

        public string Name { get; }
        public int Weapon { get; }
        public int Propulsion { get; }
        public int Equip { get; }
        public Simulation.DesignMath.Derived Derived { get; }

        public int Hp => Derived.Hp;
        public int Attack => Derived.Attack;
        public int Defence => Derived.Defence;
        public int Speed => Derived.Speed;
        public int Sight => Derived.Sight;
        public int Range => Derived.Range;
        public int Reload => Derived.Reload;
        public int Ammo => Derived.Ammo;
        public int Fuel => Derived.Fuel;
        public int CostW => Derived.CostW;
        public int CostF => Derived.CostF;
        public int CostS => Derived.CostS;

        /// <summary>⚠ Unsere Deutung, siehe Klassenkopf: Satz +0x22.</summary>
        public int MinRange
        {
            get
            {
                var t = Derived.Tail;
                int i = 0x22 - 0x1a;
                return t != null && t.Length > i + 1 ? t[i] | (t[i + 1] << 8) : 0;
            }
        }
    }

    private static Dictionary<string, Entry>? _byName;
    private static Dictionary<int, byte[]>? _comp;

    /// <summary>True, sobald etwas nachzuschlagen ist.</summary>
    public static bool Ready => _byName is { Count: > 0 };

    /// <summary>Die Sätze einlesen. Mehrfach aufrufen ist umsonst; nur
    /// <see cref="Forget"/> macht es wieder scharf (der Spieler kann zwischen
    /// zwei Missionen einen Entwurf angelegt haben).</summary>
    public static void Load()
    {
        if (_byName != null) return;
        _byName = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        _comp = ReadComponentRows();
        Simulation.DesignMath.Load();
        ReadSec47();
        ReadOwnDesigns();
    }

    /// <summary>Nach einem Missionswechsel neu einlesen.</summary>
    public static void Forget() { _byName = null; _comp = null; }

    public static bool TryGet(string name, out Entry e)
    {
        Load();
        if (_byName != null && _byName.TryGetValue(name, out e)) return true;
        e = default;
        return false;
    }

    /// <summary>Alle Entwürfe, für den Wiki-Export. Die Werte kommen damit aus
    /// DERSELBEN Rechnung, die auch das Erstellungsfenster zeigt — eine zweite
    /// Nachbildung in einem Ausgabewerkzeug würde früher oder später
    /// abweichen.</summary>
    public static IEnumerable<Entry> All()
    {
        Load();
        if (_byName == null) yield break;
        foreach (var e in _byName.Values) yield return e;
    }

    /// <summary>Der kurze Bauteilname (Satz +0x02) mit dem »(n)« des Originals
    /// dahinter — siehe Klassenkopf. Leer, wenn es die Zeile nicht gibt.</summary>
    public static string ComponentLabel(int row)
    {
        Load();
        if (_comp == null || row <= 0 || !_comp.TryGetValue(row, out var r)) return "";
        string n = Import.Cp437.GetString(r, 0x02, 11);
        if (n.Length == 0) return "";
        return $"{n} ({r[0x01]})";
    }

    /// <summary>Der LANGE Bauteilname (Satz +0x25) mit derselben Klammer — das
    /// ist die Schreibweise der drei Listen im Erstellungsfenster.</summary>
    /// <summary>
    /// Der kurze Bauteilname (+0x02) <b>OHNE die Klammer</b>.
    ///
    /// <para>⚠ 09.09.2026, beim Bau der Einheitenliste (Fensterart 22) gelesen:
    /// die Klammer <c>" (n)"</c> gehört den drei BAUTEILLISTEN des
    /// Erstellungsfensters, nicht dem Namen. Die Einheitenliste kopiert bei
    /// <c>0x4773FF</c> nur <c>[Zeile·58 + 0x5045A2]</c> und hängt nichts an —
    /// dort steht »Ketten«, nicht »Ketten (0)«.</para></summary>
    public static string ComponentPlain(int row)
    {
        Load();
        if (_comp == null || row <= 0 || !_comp.TryGetValue(row, out var r)) return "";
        return Import.Cp437.GetString(r, 0x02, 11);
    }

    public static string ComponentLongLabel(int row)
    {
        Load();
        if (_comp == null || row <= 0 || !_comp.TryGetValue(row, out var r)) return "";
        string n = Import.Cp437.GetString(r, 0x25, 24);
        if (n.Length == 0) return ComponentLabel(row);
        return $"{n} ({r[0x01]})";
    }

    /// <summary>Was ein einzelnes Bauteil kostet — die drei Bytes, aus denen
    /// die Preisformel @0x4b1fb0 den Preis eines Entwurfs zusammensetzt:
    /// <c>+0x1a = stats[Waffe][0x20]</c>, <c>+0x1b = stats[Fahrwerk][0x21]</c>,
    /// <c>+0x1c = stats[Ausrüstung][0x22] + stats[Waffe][0x22]</c> (siehe
    /// <see cref="Simulation.DesignMath"/>). Genau diese Zahlen stehen im
    /// Original rechts neben den Einträgen der drei Bauteillisten: »Leichte
    /// Bordkanone (0) ]15« ist Zeile 1 mit +0x20 = 15, »Reifen (0) [10« ist
    /// Zeile 161 mit +0x21 = 10.</summary>
    public static int WeaponPrice(int row) => Stat(row, 0x20);
    public static int ChassisPrice(int row) => Stat(row, 0x21);
    public static int EquipPrice(int row) => Stat(row, 0x22);

    /// <summary>Die BILDNUMMER des Bauteils in der Bank aus ANIM.CWA 400…403 —
    /// das Byte <c>+0x0D</c> desselben 58-Byte-Satzes. Es IST die Bildnummer:
    /// der Zeichner <c>0x4508A0</c> rechnet in seinem Fall 5
    /// <c>Bild = word[0x7A468A] + icon</c>, und <c>0x7A468A</c> ist das Feld
    /// <c>start_frame</c> von ANIM-Folge 400. 0 heisst »kein Bild« — so führt
    /// das Original die Verbesserungen 80…88. Siehe
    /// <see cref="PortraitBank"/>.
    ///
    /// <para>Gemessen an dieser Tabelle: Fahrwerk 160 (Spinne) → 1, 161
    /// (Reifen) → 2, 175 (Schweber) → 18; Aufbauteil 1 (Leichte Bordkanone)
    /// → 21, 4 (Maschinengewehr) → 24; Verbesserung 65 (Teleporter) → 44.
    /// ⚠ Die Spalte 1…18 der Fahrwerke ist Zeichen für Zeichen die
    /// <c>comp_id</c>-Spalte aus UNIT_SPRITES_RE.md §4 — Bildnummer und
    /// ROBO-Bauteilnummer sind dasselbe Byte.</para></summary>
    public static int IconOf(int row) => Stat(row, 0x0D);

    private static int Stat(int row, int at)
    {
        Load();
        if (_comp == null || row <= 0 || !_comp.TryGetValue(row, out var r)) return 0;
        return at < r.Length ? r[at] : 0;
    }

    /// <summary>Nur der kurze Name, ohne die Klammer.</summary>
    public static string ComponentName(int row)
    {
        Load();
        if (_comp == null || row <= 0 || !_comp.TryGetValue(row, out var r)) return "";
        return Import.Cp437.GetString(r, 0x02, 11);
    }

    // ---- Einlesen -----------------------------------------------------------

    private static Dictionary<int, byte[]> ReadComponentRows()
    {
        var into = new Dictionary<int, byte[]>();
        var rows = Section("Maps/component_stats.json", "rows");
        if (rows == null) return into;
        foreach (var kv in rows)
        {
            if (!int.TryParse(kv.Key, out int row)) continue;
            var b = FromHex(Core.JsonMeta.AsS(kv.Value));
            if (b.Length >= Simulation.DesignMath.Stride) into[row] = b;
        }
        return into;
    }

    /// <summary><c>--werteliste-nur-roh</c> — der Stand vor dem 10.09.2026: die
    /// Werteliste zeigt nur Entwuerfe mit rohem Satz, und das sind seit bug-057
    /// keine.</summary>
    public static bool NurRoheWerteliste;

    private static void ReadSec47()
    {
        var designs = Section("Maps/unit_designs.json", "designs");
        if (designs == null) return;
        foreach (var kv in designs)
        {
            if (kv.Value is not System.Text.Json.Nodes.JsonObject d) continue;
            string name = Core.JsonMeta.GetS(d, "name");
            if (name.Length == 0 || _byName!.ContainsKey(name)) continue;
            string raw = Core.JsonMeta.GetS(d, "raw");
            int waffe = Core.JsonMeta.GetI(d, "weapon");
            int fahrwerk = Core.JsonMeta.GetI(d, "propulsion");
            int rumpf = Core.JsonMeta.GetI(d, "body");
            // ⚠⚠ 10.09.2026 — HIER STAND NUR `if (raw.Length < 0x2e*2) continue;`,
            // UND DAMIT WAR DIE WERTELISTE FUER JEDEN ORIGINAL-ENTWURF LEER.
            //
            // Seit bug-057 (05.09.) schreibt WriteDesignsFromExe die Datei
            // `unit_designs.json` OHNE `raw` — ihre eigene Kopfnote sagt es:
            // »no raw record is written here«. Der Schwanz war also nicht
            // fehlerhaft, sondern schlicht nicht mehr da, und diese Zeile sprang
            // seither ueber ALLE 74 belegten EXE-Saetze hinweg, davon zwoelf
            // Fusssoldaten. Im Basisfenster stand darum bei jedem
            // Original-Entwurf weder Energie noch A/V noch Reichweite.
            //
            // ⭐ Der Ausweg lag daneben: ReadOwnDesigns und
            // MapEntityLayer.LoadDesigns rechnen den Schwanz laengst mit
            // DesignMath.Compute, wenn kein Satz da ist. Genau das hier auch.
            // Gegenschalter --werteliste-nur-roh.
            if (raw.Length < 0x2e * 2 && NurRoheWerteliste) continue;
            var schwanz = raw.Length >= 0x2e * 2
                        ? Simulation.DesignMath.FromRecordHex(raw)
                        : Simulation.DesignMath.Compute(waffe, fahrwerk, rumpf);
            _byName[name] = new Entry(name, waffe, fahrwerk, rumpf, schwanz);
        }
    }

    /// <summary>Die selbst entworfenen Einheiten. Dort stehen nur die drei
    /// Bauteile — der Schwanz wird mit der Formel des Spiels gerechnet, genau
    /// wie <c>MapEntityLayer.LoadOwnDesigns</c> es tut.</summary>
    private static void ReadOwnDesigns()
    {
        // ⚠ `using`, nicht bloss `new` — ConfigFile ist ein RefCounted, und ein
        // nicht freigegebenes stirbt beim Herunterfahren im Finalizer. In
        // Settings.cs hat genau dieses Muster am 13.08.2026 zu
        // »Leaked unsafe reference to object: <ConfigFile#…>« in Serie und
        // danach zu 0xC0000005 in GC.RunFinalizers geführt (Rückgabewerte
        // 139/132 statt 0). Hier ist es je Aufruf eines statt je Bild, also
        // harmlos genug für `using` statt eines gehaltenen Abbilds — aber es
        // gehört freigegeben, zumal die Zeile darunter mitten heraus
        // zurückspringt. Dieselbe Datei macht es bei FileAccess schon so.
        using var c = new ConfigFile();
        if (c.Load(Rendering.MapEntityLayer.OwnDesignsPath) != Error.Ok) return;
        int n = (int)c.GetValue("designs", "count", 0);
        for (int i = 0; i < n; i++)
        {
            var v = c.GetValue("designs", $"d{i}", new Godot.Collections.Array());
            if (v.VariantType != Variant.Type.Array) continue;
            var a = v.AsGodotArray();
            if (a.Count < 4) continue;
            string nm = a[0].AsString();
            if (nm.Length == 0 || _byName!.ContainsKey(nm)) continue;
            int prop = a[1].AsInt32(), equip = a[2].AsInt32(), weapon = a[3].AsInt32();
            _byName[nm] = new Entry(nm, weapon, prop, equip,
                                    Simulation.DesignMath.Compute(weapon, prop, equip));
        }
    }

    /// <summary>
    /// Ein Abschnitt einer JSON-Datei — mit <c>System.Text.Json</c>, ohne
    /// Godot-Variants.
    ///
    /// <para>⚠⚠ 11.09.2026 — bug-130 KAM HIER WIEDER. Bis heute las diese
    /// Funktion ueber <c>Json.Parse</c> und lieferte ein
    /// <c>Godot.Collections.Dictionary</c>; <c>ReadSec47</c> lief dann ueber rund
    /// 1600 Variant-Paare. Im Abschlussbericht von <c>--fireat-check</c> starb
    /// der Lauf zweimal von zwei mit »Internal CLR error (0x80131506)« in
    /// <c>UTF32Encoding.GetChars ← Dictionary.GetKeyValuePair ← ReadSec47</c> —
    /// dasselbe Rennen im <c>DisposablesTracker</c>, das der Kopf von
    /// <see cref="Core.JsonMeta"/> beschreibt und das dort am 31.08. fuer den
    /// Kartenlader behoben wurde. Dieselbe Kur hier.</para>
    /// </summary>
    private static System.Text.Json.Nodes.JsonObject? Section(string rel, string key)
    {
        var root = Core.JsonMeta.Lies(Core.Content.Path(rel));
        return root.TryGetPropertyValue(key, out var v) && v is System.Text.Json.Nodes.JsonObject o ? o : null;
    }

    private static byte[] FromHex(string s)
    {
        if (s.Length % 2 != 0) return Array.Empty<byte>();
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++)
            b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
        return b;
    }
}
