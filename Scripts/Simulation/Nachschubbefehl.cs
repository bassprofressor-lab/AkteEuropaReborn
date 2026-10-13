namespace AkteEuropaReborn.Rendering;

using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// <b>»Diese Einheit befuellen«</b> — bug-519 (KayelGee R5#7, berichte/kayelgee-runde5-heli.md §#7).
///
/// <para>Seine Meldung: ein Nachschubheli ist gewaehlt, ein Klick auf eine eigene
/// Einheit waehlt nur die Einheit an — der Heli fliegt nicht hin. Bei uns gab es
/// weder Zeiger noch Befehl: der Klick wurde zum Flugziel 2003, und das verwirft
/// die Kampagne.</para>
///
/// <para><b>Original (BELEGT, einladezeiger-fable §4 und Heli-Lesung):</b></para>
/// <list type="number">
/// <item>Zeiger <c>0x4320D1..0x43214A</c>: Flugzeugplatz gewaehlt, unter dem Zeiger
/// ein EIGENES Fahrzeug oder Schiff (Gattung 0/3/4/5, kein Fussvolk), Ladung
/// +0x31 != 0, Art 13 + Sprit max != 0 → Zustand 8, Bild 12 (Fass,
/// <c>0x4321BE</c>); Art 14 + Munition max != 0 → Zustand 9, Bild 13 (Patronen,
/// <c>0x4321FE</c>).</item>
/// <item>Klick, Tafel <c>0x437994</c>: [8] = <c>0x4376D3</c> Busbefehl <b>14</b>,
/// [9] = <c>0x43770A</c> Busbefehl <b>15</b>, P1 = Flugzeugplatz, P2 = Einheit.</item>
/// <item>Behandler <c>0x427E30</c> / <c>0x427EE0</c>: Flugziel := Zelle der
/// Einheit, uk := 10 bzw. 11, Sollhoehe := 135, Ziel +0x2E := Einheit. Danach der
/// gewoehnliche Nachschubflug. <b>Keine Kampagnensperre.</b></item>
/// </list>
///
/// <para>⚠ UNSERE SETZUNGEN: die Quittung <c>0x40162C(0x4B, …)</c> ist ungelesen und
/// fehlt; P2 ist bei uns der Einheitenindex (ob das Original ihn als Byte liest,
/// ist offen); ein voller Kunde wird wie bisher in <see cref="UpdateSupply"/>
/// sofort wieder losgelassen (die Abgabe 0x424845 ist ungelesen).</para>
///
/// <para>Gegenschalter <c>--nachschubbefehl-alt</c>, Pruefstand
/// <c>--nachschubbefehl-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--nachschubbefehl-alt</c> — Stand 0.6.7. bug-519.</summary>
    public static bool NachschubbefehlAlt;
    public static int NachschubBefehleAngenommen;

    /// <summary>Zustand 8 (Tanken), 9 (Munition) oder −1; dazu der Einheitenindex.</summary>
    private (int Zustand, int Einheit) NachschubzeigerAt(Vector2 mapPos)
    {
        if (NachschubbefehlAlt || _selAir < 0 || _selAir >= _special.Count) return (-1, -1);
        var a = _special[_selAir];
        if (a.Dead || a.Stored || a.Owner != ViewPlayer || !a.IsSupply || a.Cargo <= 0) return (-1, -1);
        int i = Pick(mapPos);
        if (i < 0 || i >= _entities.Count) return (-1, -1);
        var e = _entities[i];
        if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer || e.Infantry >= 0) return (-1, -1);
        if (e.GameUnitType is not (0 or 3 or 4 or 5)) return (-1, -1);
        if (a.Kind == 13 && e.FuelMax > 0) return (8, i);
        if (a.Kind == 14 && e.AmmoMax > 0) return (9, i);
        return (-1, -1);
    }

    /// <summary>Der Klick: Busbefehl 14/15 absetzen. true = der Klick ist verbraucht.</summary>
    public bool PostNachschubKlick(Vector2 mapPos)
    {
        var (zustand, ei) = NachschubzeigerAt(mapPos);
        if (zustand < 0) return false;
        var a = _special[_selAir];
        var c = CommandRecord.Make(zustand == 8 ? CommandOp.NachschubTanken : CommandOp.NachschubMunition,
                                   (byte)ViewPlayer, (short)a.Slot, (short)ei);
        if (!Emit(c)) { _order = "kein Befehl abgesetzt"; return false; }
        AddOrderMark(_entities[ei].Pos, attack: false);
        _order = $"Nachschub ({(zustand == 8 ? "Treibstoff" : "Munition")}) -> {LabelOf(_entities[ei])}";
        QueueRedraw();
        return true;
    }

    /// <summary>Behandler 0x427E30 (14) / 0x427EE0 (15).</summary>
    private bool ApplyNachschubBefehl(in CommandRecord c)
    {
        Special? a = null;
        foreach (var s in _special) if (s.Slot == c.P1) { a = s; break; }
        if (a == null || a.Dead || a.Stored || a.Owner != c.Player) return false;
        if (a.Kind != (c.Op == CommandOp.NachschubTanken ? 13 : 14)) return false;
        if (c.P2 < 0 || c.P2 >= _entities.Count) return false;
        var e = _entities[c.P2];
        if (!SupplyEligible(a, e) || e.Infantry >= 0) return false;
        a.Customer = c.P2;               // Ziel +0x2E := Einheit
        a.Goal = e.Pos;                  // Flugziel := Zelle der Einheit
        a.Sollhoehe = FlughoeheMax;      // +0x13 := 135
        a.PlayerGoal = null;
        a.HomePoint = null;
        a.DepotSlot = -1;
        NachschubBefehleAngenommen++;
        return true;
    }
}
