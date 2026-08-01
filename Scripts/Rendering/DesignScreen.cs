namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// The design screen: a unit is a PROPULSION plus a WEAPON plus an EQUIPMENT
/// slot, and the player draws it up rather than picking from a fixed roster.
///
/// This is the half of the game the campaign schedule made visible. The script
/// pre-makes nine designs over thirty-three missions, seven of them infantry —
/// everything else in a saved game was drawn up by whoever played it, which is
/// why 4.DM carries slots named `H-Cannon-81-165`: weapon, equipment and
/// propulsion spelled out in the name.
///
/// The record it produces is the one the game uses (sec47, 46 bytes): name at
/// +0x02, weapon row at +0x17, propulsion unit_type at +0x18, equipment row at
/// +0x19. Which factory may build it follows from the weapon, exactly as
/// <c>MapEntityLayer.FitsFactory</c> already decides it — 1..19 to the
/// Waffen-Fabrik, 65..79 to the Spezial-Fabrik, the rest to the Fahrwerk-Fabrik.
///
/// OURS, and marked as such: the generated name follows the pattern the saved
/// designs use (`weapon-equipment-propulsion`), because the original lets the
/// player type one and there is no text entry here yet.
/// </summary>
public sealed class DesignScreen
{
    public sealed class Part
    {
        public int Id;
        public string Name = "";
        public int Damage, Range;
    }

    /// <summary>The three rows the screen offers.</summary>
    public enum Row { Propulsion, Weapon, Equipment }

    public bool Active { get; private set; }
    public Row Cursor { get; private set; } = Row.Propulsion;

    public readonly List<Part> Propulsions = new();
    public readonly List<Part> Weapons = new();
    public readonly List<Part> Equipment = new();

    private int _prop, _weap, _equip;
    public string Message { get; private set; } = "";

    public bool Ready => Propulsions.Count > 0 && Weapons.Count > 0;

    public void Toggle()
    {
        Active = !Active;
        Message = "";
    }

    public void Close() => Active = false;

    public void Move(int d)
    {
        int n = System.Enum.GetValues<Row>().Length;
        Cursor = (Row)(((int)Cursor + d % n + n) % n);
    }

    public void Change(int d)
    {
        switch (Cursor)
        {
            case Row.Propulsion: _prop = Wrap(_prop + d, Propulsions.Count); break;
            case Row.Weapon: _weap = Wrap(_weap + d, Weapons.Count); break;
            default: _equip = Wrap(_equip + d, Equipment.Count + 1); break;  // +1 = none
        }
        Message = "";
    }

    private static int Wrap(int i, int n) => n <= 0 ? 0 : (i % n + n) % n;

    public Part? CurrentPropulsion => Pick(Propulsions, _prop);
    public Part? CurrentWeapon => Pick(Weapons, _weap);

    /// <summary>The equipment row, or null for "none" — the original allows a
    /// design without one and most of the ready-made ones have none.</summary>
    public Part? CurrentEquipment => _equip == 0 ? null : Pick(Equipment, _equip - 1);

    private static Part? Pick(List<Part> l, int i) => l.Count == 0 ? null : l[Wrap(i, l.Count)];

    /// <summary>The name a new design gets, in the shape the saved ones use.
    /// </summary>
    public string ProposedName()
    {
        var w = CurrentWeapon;
        var p = CurrentPropulsion;
        var e = CurrentEquipment;
        if (w == null || p == null) return "";
        string wn = Short(w.Name);
        return e == null ? $"{wn}-{p.Id}" : $"{wn}-{e.Id}-{p.Id}";
    }

    /// <summary>Trim a weapon name down to something that fits a design name —
    /// the game's own designs use short forms ("H-Cannon" for the Schwere
    /// Bordkanone). Ours is a plain first word rather than a translation.</summary>
    private static string Short(string s)
    {
        int sp = s.IndexOf(' ');
        string t = sp > 0 ? s[..sp] : s;
        return t.Length > 12 ? t[..12] : t;
    }

    /// <summary>Three lines for the HUD, the cursor marked.</summary>
    public string Text()
    {
        if (!Ready) return "KONSTRUKTION — keine Bauteildaten importiert";
        string Mark(Row r) => Cursor == r ? ">" : " ";
        var p = CurrentPropulsion;
        var w = CurrentWeapon;
        var e = CurrentEquipment;
        return
            "KONSTRUKTION   ↑↓ Zeile   ←→ Teil   Enter uebernehmen   M schliessen\n" +
            $"{Mark(Row.Propulsion)} Fahrwerk   {p?.Name ?? "-"} ({p?.Id})\n" +
            $"{Mark(Row.Weapon)} Waffe      {w?.Name ?? "-"} ({w?.Id})" +
            (w != null && w.Damage > 0 ? $"  Schaden {w.Damage}  Reichw. {w.Range / 10.0:0.#}" : "") + "\n" +
            $"{Mark(Row.Equipment)} Ausruestung {e?.Name ?? "keine"}" + (e != null ? $" ({e.Id})" : "") + "\n" +
            $"  -> {ProposedName()}" + (Message.Length > 0 ? "   " + Message : "");
    }

    public void Say(string s) => Message = s;
}
