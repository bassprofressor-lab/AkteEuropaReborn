using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DER EINSTEIGEBEFEHL</b> — Klick mit gewaehlter Einheit auf den eigenen
/// Frachter (14.09.2026, Kampagne 14).
///
/// <para><b>Seine Meldung:</b> »Fahrzeuge gehen rein, nur keine Infanterie«. Der
/// Buglog von bug-240 behauptete einen Klickweg (»Klick auf den Traeger ->
/// BeladeVersuch«) — den gab es nie. Einsteigen lief nur ueber den Belade-Takt,
/// und der verlangt seit bug-240 eine LADEZELLE. Fahrzeuge stehen dafuer ohnehin
/// auf der Rampe, Fussvolk am gewoehnlichen Ufer nie.</para>
///
/// <para><b>Gelesen</b> (<c>berichte/transportladung-fable.md</c> §2,
/// <c>einladezeiger-fable.md</c>):</para>
/// <code>
///   Zeiger 11 -> Klick 0x437994[11] -> Sender 0x4380F0 (F 0x437250):
///     spiralfoermig ab dem Schiff eine Zelle suchen
///       Fahrzeug: Zellbyte >= 200 (0x438440)   Fussvolk: Uferzelle (0x43820C)
///     je Einheit Befehl 3 (fahre) und Befehl 17 (+6 := Schiff)
///   Behandler 17 (0x4C3021..0x4C30C3): fahre, +0x36 := Schiff (@0x4C309B)
///   Ankunft (0x411670): +0x36 < 8000 -> UKOL 15
///   Arm 15 (0x4091E3): Schiff an einer der vier Kanten, Passagier steht -> 0x4CEE80
/// </code>
///
/// <para>⚠ <b>UNSERE Setzungen:</b> die Uferpruefung (Wasser im 2x2-Muster) bauen
/// wir nicht nach — Fussvolk nimmt die naechste freie begehbare Zelle in
/// <see cref="BeladeReichweite"/> um das Schiff; eingestiegen wird wie bisher im
/// Abstand <see cref="BeladeReichweite"/> statt nur an den vier Kanten. Das ist
/// grosszuegiger, verhindert aber nichts, was das Original erlaubt.
/// Gegenschalter <c>--einsteigbefehl-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--einsteigbefehl-alt</c> — der Stand bis 14.09.2026: ein Klick auf
    /// den eigenen Frachter ist ein gewoehnlicher Fahrbefehl, eingestiegen wird nur
    /// von einer Ladezelle aus.</summary>
    public static bool EinsteigbefehlAlt;

    /// <summary>Wieviele Einsteigeauftraege abgesetzt und wieviele davon an Bord
    /// gingen.</summary>
    public int EinsteigAuftraege, EinsteigAngekommen;

    /// <summary>
    /// Der Absender <c>0x4380F0</c>: liegt unter der Maus ein eigener Traeger und
    /// ist Fahrzeug oder Fussvolk gewaehlt, geht je Einheit ein Fahrbefehl zu einer
    /// passenden Zelle und Befehl 17 hinaus. <c>false</c>, wenn der Klick nichts
    /// damit zu tun hat (dann faellt er in die uebrigen Weichen).
    /// </summary>
    public bool PostBoardKlick(Vector2 mapPos, bool queue = false)
    {
        if (EinsteigbefehlAlt || _nav == null || _sel.Count == 0) return false;
        int ti = Pick(mapPos);
        if (ti < 0) return false;
        var t = _entities[ti];
        if (t.IsBuilding || t.IsProp || t.Dead || t.Owner != ViewPlayer) return false;
        if (t.GameUnitType != 4 || !IstTraeger(t) || !AuswahlKannEinsteigen()) return false;

        var vergeben = new HashSet<Vector2I>();
        int n = 0;
        foreach (int i in new List<int>(_sel))
        {
            if (i == ti || i < 0 || i >= _entities.Count) continue;
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead || !u.Mobile || u.GameUnitType >= 2) continue;
            var z = EinsteigZelle(i, u, t, vergeben);
            if (z == null) continue;
            vergeben.Add(z.Value);
            if (!Emit(CommandRecord.Make(CommandOp.Move, (byte)ViewPlayer, (short)i,
                                         (short)z.Value.X, (short)z.Value.Y, (short)(queue ? 1 : 0))))
                continue;
            Emit(CommandRecord.Make(CommandOp.Board, (byte)ViewPlayer, (short)i,
                                    (short)z.Value.X, (short)z.Value.Y, (short)ti));
            n++;
        }
        _order = n > 0 ? $"Einsteigen: {n} Einheit(en) zum {t.Name}" : "keine Stelle zum Einsteigen gefunden";
        if (n > 0)
        {
            EinsteigAuftraege += n;
            AddOrderMark(t.Pos, attack: false);
            if (!BefehlsklangWegAlt) SpeakOrdered(angriff: false);
        }
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    /// <summary>Spiralfoermig ab dem Schiff die erste freie Zelle, auf der die Einheit
    /// einsteigen darf (Fahrzeug: Rampe >= 200, Fussvolk: begehbar).</summary>
    private Vector2I? EinsteigZelle(int i, Entity u, Entity t, HashSet<Vector2I> vergeben)
    {
        if (_nav == null) return null;
        for (int r = 1; r <= BeladeReichweite; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var c = new Vector2I(t.Col + dx, t.Row + dy);
                    if (!_nav.InBounds(c.X, c.Y) || vergeben.Contains(c)) continue;
                    if (!(c.X == u.Col && c.Y == u.Row) && !_nav.IsFree(c.X, c.Y, u.Move, i)) continue;
                    if (u.Infantry < 0 && !RampeEntladen(c.X, c.Y)) continue;     // 0x438440
                    return c;
                }
        return null;
    }

    /// <summary>Behandler 17 (<c>0x4C3021…0x4C30C3</c>): +0x36 := Traeger. Die Fahrt
    /// kam mit dem Befehl 3 davor.</summary>
    private bool ApplyBoard(in CommandRecord c)
    {
        int i = c.P1, ti = c.P4;
        if (i < 0 || i >= _entities.Count || ti < 0 || ti >= _entities.Count) return false;
        var u = _entities[i];
        var t = _entities[ti];
        if (u.Dead || t.Dead || t.Owner != c.Player || !IstTraeger(t)) return false;
        u.EinsteigTraeger = t.Slot;
        return true;
    }

    /// <summary>Liegt der befohlene Traeger in Reichweite? Vorab gefragt, damit ein
    /// wartender Passagier nicht jeden Takt die Statuszeile ueberschreibt.</summary>
    private bool EinsteigTraegerDa(Entity u)
    {
        foreach (var q in _entities)
            if (!q.Dead && !q.IsBuilding && !q.IsProp && q.Slot == u.EinsteigTraeger && IstTraeger(q))
                return Mathf.Max(Mathf.Abs(q.Col - u.Col), Mathf.Abs(q.Row - u.Row)) <= BeladeReichweite;
        u.EinsteigTraeger = -1;                       // Traeger weg: Auftrag verfaellt
        return false;
    }
}
