namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--fussklangsatz-check</c> — <b>spricht ein ERZEUGTER Fusssoldat mit
/// seinem eigenen Klangsatz?</b> (10.09.2026, bug-159.)
///
/// <para>⚠⚠ Warum es diesen Stand braucht: <c>--fussschuss-probe</c> setzt auf
/// map_04 einen L-Infanteristen, und dessen Spodek ist <b>0</b> — der
/// Standardsatz ist fuer ihn RICHTIG. Die Probe zeigte darum vor und nach der
/// Behebung dieselbe Zeile und haette beides durchgewinkt. Eine Probe, die den
/// Fall nicht herstellt, misst ihn nicht (vgl. bug-105, bug-157).</para>
///
/// <para>Dieser Stand schickt <b>jede</b> Infanterieart durch den ECHTEN
/// Erzeugerweg (<c>SpawnReinforcement</c>, also <c>place_unit</c>
/// <c>@0x4D0810</c>) und vergleicht <c>Chassis</c> mit dem Spodek, das der
/// Aufsteller des Originals <c>@0x4B1ABE</c> aus der Waffenzeile rechnet.
/// Bestanden ist er, wenn alle Arten ihren eigenen Satz tragen.</para>
///
/// <para>Nullmodell: <c>--fussklangsatz-alt</c> muss genau die Arten mit
/// Spodek != 0 durchfallen lassen.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string FussklangsatzCheck()
    {
        LoadDesigns();
        LoadInfantryDesigns();
        var sb = new System.Text.StringBuilder("fussklangsatz-check\n");
        if (_infDesigns == null || _nav == null)
            return sb.Append("  keine Infanterietafel oder keine Karte - der Lauf sagt NICHTS").ToString();

        int gut = 0, schlecht = 0;
        foreach (var kv in _infDesigns)
        {
            // Nur der STEHENDE (gerade) Satz eines Paares - denselben nimmt
            // InfantryFor, und denselben fuehrt die Karte in +0x0B.
            if ((kv.Key & 1) != 0) continue;
            int zeile = kv.Value.WeaponRow;

            // ⚠ 10.09.2026 — DIE ENTWURFSNUMMER NICHT SELBST SUCHEN.
            // Der erste Wurf lief hier ueber _designBySlot und nahm den ersten
            // Entwurf mit passender Waffe — und erwischte FAHRZEUGE
            // (»Maschinengewehr«, »Mittelstreckenrakete«, »Roma-E4«), also
            // dreimal denselben falschen Satz. InfantryDesignOf gibt es
            // laengst, und es prueft zusaetzlich das FAHRWERK 148/149; genau
            // daran haengt die Eindeutigkeit.
            int entwurf = InfantryDesignOf(kv.Key);
            if (entwurf < 0)
            {
                sb.Append($"  Waffenzeile {zeile}: kein Entwurf gefunden - ungeprueft\n");
                continue;
            }

            var frei = FreieZelleUm(new Vector2I(_nav.Width / 2, _nav.Height / 2));
            if (frei == null) { sb.Append("  kein Platz auf der Karte\n"); break; }
            // ⚠⚠ SpawnReinforcement gibt den SATZ zurueck, NICHT den Index in
            // _entities — place_unit @0x4D0810 reicht dem Skript den Satzindex.
            // Der erste Wurf las hier _entities[rueckgabe] und bekam damit eine
            // voellig andere Einheit (»Maschinengewehr«, Chassis 2). Genau die
            // Sorte Fehler, die eine Messung LEISE falsch macht: die Zeilen
            // sahen plausibel aus.
            int satz = SpawnReinforcement(entwurf, frei.Value.X, frei.Value.Y, 0);
            if (satz < 0)
            {
                sb.Append($"  Entwurf {entwurf} (Zeile {zeile}): nicht erzeugbar - ungeprueft\n");
                continue;
            }
            Entity? u = null;
            foreach (var e in _entities)
                if (!e.IsBuilding && !e.Dead && e.Slot == satz) { u = e; break; }
            if (u == null)
            {
                sb.Append($"  Entwurf {entwurf}: Satz {satz} nicht gefunden - ungeprueft\n");
                continue;
            }
            int erwartet = kv.Key;
            bool ok = u.Chassis == erwartet;
            if (ok) gut++; else schlecht++;
            sb.Append($"  Entwurf {entwurf,3} Zeile {zeile}: Spodek erwartet {erwartet,2}, "
                    + $"Chassis {u.Chassis,2}, Satzindex {(u.Chassis >> 1) - 1,2} "
                    + (erwartet == 0 ? "(diese Art nimmt ohnehin den Standardsatz)"
                       : ok ? "eigener Satz" : "⚠ FAELLT auf den Standardsatz 189/193")
                    + "\n");
            u.Dead = true;                       // wieder von der Karte nehmen
        }
        sb.Append($"  {gut} Arten mit eigenem Satz, {schlecht} auf dem Standardsatz\n");
        sb.Append($"  Gegenschalter --fussklangsatz-alt: {FussklangsatzAlt}\n");
        sb.Append(schlecht == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
