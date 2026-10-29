namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 29.10.2026, bug-649 (KayelGee R8 K7): »Einheiten fuer die Untermission
/// (Forscher M7) bleiben im Basisdepot und belegen sichtbar Plaetze.«
///
/// <para>Die Bezahlregel M7 <c>0x49B0CE</c> fragt <c>UKOL(v10) == 50</c> und ruft
/// <c>remove_unit_var</c> → <see cref="MissionRemove"/> → <see cref="Kill"/>. Kill nahm
/// die Einheit nicht aus <see cref="Entity.Garage"/>: <c>DepotRows</c> listete den
/// toten Satz weiter, <c>GarageBelegt</c> zaehlte ihn gegen die 6 Plaetze. Dasselbe
/// bei M3 Hemmerl (<c>0x499425</c>), M11 Agent (<c>0x49C4BD</c>), M16 Wissenschaftler
/// (<c>0x49DEF2</c>). Gelesen in <c>berichte/tueren-einnahme-r8-opus.md</c> §2.4:</para>
/// <code>
///   remove_unit 0x4D0B00 (C, Muster @0x4D0B27; F @0x4D06D7):
///     0x4D0B1E  UKOL in 0x32..0x33 -> Ausdocken:
///     0x4D0B4A    Gebaeude = AKCE (+0x15); Art -> Index 0x4D0CB4 = [0,4,4,4,1,2,4,4,4,4,4,3]
///     0x4D0B72    jmp [0x4D0CA0 + 4·Index]: Basis 0x43C120, Depot 0x43C6F0, Bahnhof 0x43C430
///     0x4D0C92  0x4011B3 -> 0x410E60 loeschen (ohne Klang, ohne Wrack)
/// </code>
/// <para>Bei uns: <see cref="SkriptEntferntAusGarage"/> (remove_unit eines
/// untergestellten Satzes: ausdocken und STILL loeschen, wie
/// <c>EinnahmeInsassenLoeschen</c>) und allgemein <see cref="TodAusGarage"/> (jeder
/// Tod eines Garage-Satzes nimmt ihn aus der Liste). ⚠ UNSERE SETZUNG: das stille
/// Loeschen ruft <c>SkriptvarTod</c>/<c>NoteKill</c> nicht — ob 0x410E60 Zaehler
/// fuehrt, ist ungelesen. ⚠ Das Original schreibt danach imap an der Lage des
/// Fahrzeugsatzes (Tuerzelle) 0xFFFE−…; wir schreiben das Gitter nicht (der Satz
/// steht nicht darin). <c>sell_unit</c> (SkriptVerkauf) eines untergestellten
/// Satzes ist NICHT geprueft (Bericht §6). Gegenschalter
/// <c>--skript-entfernt-garage-alt</c> (Stand 0.6.11: tote Zeile bleibt).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--skript-entfernt-garage-alt</c> — bug-649, Stand 0.6.11.</summary>
    public static bool SkriptEntferntGarageAlt;

    /// <summary>Pruefstand: wie oft ausgedockt wurde (Skript / Tod).</summary>
    public int SkriptAusgedockt, TodAusgedockt;

    /// <summary>remove_unit eines untergestellten Satzes. false = nicht untergestellt
    /// (dann der bisherige Weg ueber Kill).</summary>
    private bool SkriptEntferntAusGarage(int i, Entity e)
    {
        if (SkriptEntferntGarageAlt || e.InGebaeude is not { } b || !b.Garage.Contains(e)) return false;
        b.Garage.Remove(e);                                   // 0x43C120 / 0x43C6F0 / 0x43C430
        e.InGebaeude = null;
        SkriptAusgedockt++;
        GD.Print($"Missionsskript: Einheit {e.Slot} aus {BuildingTypeName(b.BType)} {b.Slot} ausgedockt "
               + "und geloescht (remove_unit 0x4D0B00)");
        if (TodesLog)
            GD.Print($"tod: {LabelOf(e)} (Platz {e.Slot}, Spieler {e.Owner}) im Depot von "
                   + $"{BuildingTypeName(b.BType)} {b.Slot} GELOESCHT, Grund: Missionsskript remove_unit (0x4D0B00)");
        _sel.Remove(i);
        foreach (var other in _entities)
            if (other.Target == i) other.Target = -1;
        if (_selected == i) SetPrimary();
        e.Hp = 0;
        e.Dead = true;
        e.DeadTime = 0;
        e.Path = null;
        e.Target = -1;
        e.Reserved = null;
        return true;
    }

    /// <summary>Jeder Tod eines untergestellten Satzes nimmt ihn aus der Liste.</summary>
    private void TodAusGarage(Entity e)
    {
        if (SkriptEntferntGarageAlt || e.InGebaeude is not { } b) return;
        if (!b.Garage.Remove(e)) return;
        TodAusgedockt++;
        e.InGebaeude = null;
    }
}
