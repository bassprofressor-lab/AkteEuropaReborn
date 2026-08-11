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
    public static string ComponentLongLabel(int row)
    {
        Load();
        if (_comp == null || row <= 0 || !_comp.TryGetValue(row, out var r)) return "";
        string n = Import.Cp437.GetString(r, 0x25, 24);
        if (n.Length == 0) return ComponentLabel(row);
        return $"{n} ({r[0x01]})";
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
            var b = FromHex(kv.Value.AsString());
            if (b.Length >= Simulation.DesignMath.Stride) into[row] = b;
        }
        return into;
    }

    private static void ReadSec47()
    {
        var designs = Section("Maps/unit_designs.json", "designs");
        if (designs == null) return;
        foreach (var kv in designs)
        {
            if (kv.Value.VariantType != Variant.Type.Dictionary) continue;
            var d = kv.Value.AsGodotDictionary<string, Variant>();
            string name = d.TryGetValue("name", out var nv) ? nv.AsString() : "";
            if (name.Length == 0 || _byName!.ContainsKey(name)) continue;
            string raw = d.TryGetValue("raw", out var rv) ? rv.AsString() : "";
            if (raw.Length < 0x2e * 2) continue;
            _byName[name] = new Entry(
                name,
                d.TryGetValue("weapon", out var wv) ? wv.AsInt32() : 0,
                d.TryGetValue("propulsion", out var pv) ? pv.AsInt32() : 0,
                d.TryGetValue("body", out var bv) ? bv.AsInt32() : 0,
                Simulation.DesignMath.FromRecordHex(raw));
        }
    }

    /// <summary>Die selbst entworfenen Einheiten. Dort stehen nur die drei
    /// Bauteile — der Schwanz wird mit der Formel des Spiels gerechnet, genau
    /// wie <c>MapEntityLayer.LoadOwnDesigns</c> es tut.</summary>
    private static void ReadOwnDesigns()
    {
        var c = new ConfigFile();
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

    private static Godot.Collections.Dictionary<string, Variant>? Section(string rel, string key)
    {
        string path = Core.Content.Path(rel);
        if (!FileAccess.FileExists(path)) return null;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return null;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return null;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Dictionary)
            return null;
        return v.AsGodotDictionary<string, Variant>();
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
