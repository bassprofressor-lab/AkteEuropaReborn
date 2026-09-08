using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE CODES DES EINHEITENMENÜS</b> — <c>0x441810(einheit, &amp;c1..&amp;c8)</c>,
/// die Zusammenstellung, aus der <c>0x4573C0</c> das Fenster der Art 1 baut.
/// Gebaut am 08.09.2026 auf seine Meldung »das will ich alles haben«.
///
/// <para>Die Tafel ist die des Fable-Berichts
/// (<c>berichte/transporterroute-fable.md</c>, 2.2), Zeile für Zeile:</para>
/// <code>
///   c1  nach dem Turmaufsatz +0x0C:  0x1C->0x30 · 0x23->0x0A · 0x24->0x0B
///       0x27->0x23 · 0x2C->0x26 · 0x2E->0x10 (TRANSPORTZYKLUS) · 0x2F->0x11
///       0x31->0x13 · 0x32->0x14 · 0x35->0x15 ; sonst 0 (Angreifen), wenn +0x0D != 0
///   c2  1 Bewegen
///   c3  2 Beschuetzen · 0x1A Anhalten, wenn +0x0D == 0 · 7/8 bei +0x0F == 0xAB
///   c5  3 Selbstzerstoerung, wenn +0x08*100 / +0x29 > 15
///   c6  6 Einheiteninformation, immer
///   c7  5 Handsteuerung, wenn +0x0A != 1
///   c8  4 Verkaufen (im Original hinter einer Globalen)
/// </code>
///
/// <para>⚠ <b>Was davon WIRKT</b>, und das steht auch im Menü: Transportzyklus,
/// Anhalten, Verkaufen, Ein-/Ausgraben. Die anderen vier (Angreifen, Bewegen,
/// Beschützen, Selbstzerstörung, Handsteuerung) sind im Original
/// <b>Zeigermerker</b> — der Klick schaltet einen Modus, und erst der nächste
/// Klick auf die Karte führt ihn aus (<c>byte[0xA31A9C]</c>,
/// <c>byte[0xA1833B]</c>, <c>byte[0xA18330]</c>). Diesen Modus haben wir nicht;
/// die Symbole sagen es beim Druck, statt still nichts zu tun.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Die erste eigene Einheit der Auswahl — im Original
    /// <c>[0x4FA0C8]</c>, die »gewaehlte Einheit«, auf die das Menue sich
    /// bezieht.</summary>
    public int MenueEinheit()
    {
        foreach (int i in _sel)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp) continue;
            if (e.Owner != ViewPlayer) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Die acht Codes für diese Einheit, −1 = leerer Platz.</summary>
    public int[] MenueCodes(int idx)
    {
        var c = new[] { -1, -1, -1, -1, -1, -1, -1, -1 };
        if (idx < 0 || idx >= _entities.Count) return c;
        var e = _entities[idx];

        // c1 — nach dem Turmaufsatz. Die Tafel roh, ohne Deutung.
        c[0] = e.Weapon switch
        {
            0x1C => 0x30, 0x1D => -1, 0x23 => 0x0A, 0x24 => 0x0B, 0x27 => 0x23,
            0x2C => 0x26, 0x2E => 0x10, 0x2F => 0x11, 0x31 => 0x13, 0x32 => 0x14,
            0x35 => 0x15,
            _ => e.Comp0D != 0 ? 0 : -1,
        };
        c[1] = 1;                                       // Bewegen
        // c3: Anhalten statt Beschuetzen, wenn die Einheit unbewaffnet ist;
        // Ein-/Ausgraben, wenn +0x0F == 0xAB (das Fussvolk).
        c[2] = e.Comp0F == 0xAB ? (e.DugIn ? 8 : 7)
             : e.Comp0D == 0 ? 0x1A : 2;
        // c5: Selbstzerstoerung nur oberhalb von 15 % Huelle.
        if (e.HpMax > 0 && e.Hp * 100 / e.HpMax > 15) c[4] = 3;
        c[5] = 6;                                       // Einheiteninformation
        if (e.GameUnitType != 1) c[6] = 5;              // Handsteuerung
        // c8: Verkaufen — im Original hinter byte[0x504598]; bei uns hinter der
        // Frage, ob es hier ueberhaupt etwas zu verkaufen GIBT.
        if (SellChoiceOfSelection() != null) c[7] = 4;
        return c;
    }

    /// <summary>Was der Menüdruck bewirkt. Der Rückgabewert ist die Zeile für
    /// den Spieler — leer heisst »hat gewirkt, es gibt nichts zu sagen«.</summary>
    public string MenueAktion(int code)
    {
        switch (code)
        {
            case 0x10:                                   // Transportzyklus
                return RouteFensterOeffnen() ? "" : (RouteNote.Length > 0
                     ? RouteNote : "kein Transporter gewaehlt");
            case 0x1A:                                   // Anhalten
                StopSelected();
                return "";
            case 4:                                      // Verkaufen
                SellFromPanel();
                return SellNote;
            case 7:
            case 8:                                      // Ein-/Ausgraben
                ToggleDigIn();
                return "";
            case 6:                                      // Einheiteninformation
                return "Die Werte stehen im Bedienblock links unten.";
            default:
                // ⚠ Ehrlich statt still: diese fuenf sind Zeigermerker des
                // Originals, und den Modus gibt es bei uns nicht.
                return $"»{UI.UnitMenuWindow.CodeWort(code)}« ist noch nicht gebaut "
                     + "(im Original ein Zeigermodus).";
        }
    }
}
