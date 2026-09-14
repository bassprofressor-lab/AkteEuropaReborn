using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DER EINNAHMEABSCHLUSS</b> — was ausser dem Besitzerwechsel noch geschieht,
/// wenn ein Gebaeude die Hand wechselt (14.09.2026, Kampagne 13).
///
/// <para><b>Seine zwei Meldungen:</b> »Wenn der Gegner von mir eine Basis
/// einnimmt, aber noch 1 Einheit im Depot hat, sehe ich die Einheit, nachdem ich
/// die Basis wieder eingenommen habe, und kann diese Aussenden … dann zerstören«
/// und »Wenn ich eine gegnerische Fabrik einnehme, faehrt dort immer noch der
/// Transporter vom Gegner, kurz rein und wieder raus«. Bei uns wechselte
/// <c>CaptureDone</c> nur den Besitzer (<c>Hand</c>).</para>
///
/// <para><b>Gelesen</b> (beide EXE, selbst nachgeschlagen):</para>
/// <code>
///   Einnahmeabschluss (C 0x43CD57.., F 0x43BDF4..)
///     0x440190(Platz)  (F 0x43F1A0)   sec48 streichen — Rufer C 0x43CD75 / F 0x43BE0A,
///                                     zweiter Rufer: Gebaeudetod C 0x4C9A6C / F 0x4C961C
///     ... Besitzer := neuer ...
///     Art == 1 (C 0x43D190, F 0x43C1B7):
///        solange Kopf der Andockliste word[0x878E5C + 16·cis] != 0xFFFF:
///           0x43C120 ausdocken, 0x410E60 LOESCHEN (F-Ruf @0x43C208), »Error 2« bei 100
///     Art == 9: Flugzeuge mit Heimat == cis wechseln den Besitzer   (⚠ hier NICHT gebaut)
///     alle anderen Arten (Depot 5, Bahnhof, Fabriken …): Insassen bleiben
/// </code>
/// <para>Berichte: <c>berichte/einnahme-insassen-fable.md</c>,
/// <c>berichte/einnahme-transporter-fable.md</c>.</para>
///
/// <para>⚠ <b>Folge fuer das Art-5-Depot:</b> dort tut das Original NICHTS — ein
/// eingenommenes Depot zeigt die fremden Insassen, und wer sie aussendet, bekommt
/// eine Feindeinheit. Das ist originalgetreu und bleibt.</para>
///
/// <para>Gegenschalter <c>--einnahme-routen-alt</c> und
/// <c>--einnahme-insassen-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--einnahme-routen-alt</c> — der Stand bis 14.09.2026: keine
    /// Route wird beim Besitzerwechsel oder Gebaeudetod gestrichen, ein stehender
    /// Wagen friert in jedem Arm ein, und die Ankunft an Fabrik/Mine gilt schon
    /// eine Zelle vor der Tuer.</summary>
    public static bool EinnahmeRoutenAlt;

    /// <summary><c>--einnahme-insassen-alt</c> — der Stand bis 14.09.2026: die
    /// Insassen einer eingenommenen Basis bleiben im Depot.</summary>
    public static bool EinnahmeInsassenAlt;

    /// <summary>Wieviele Routensaetze gestrichen wurden (Ziel oder erste Quelle
    /// traf), wieviele davon dadurch stehen, und wieviele Insassen geloescht
    /// wurden. ⚠ Ohne die Zahlen ist »nichts passiert« nicht von »es gab nichts
    /// zu tun« zu unterscheiden.</summary>
    public int RoutenGestrichen, RoutenAngehalten, InsassenGeloescht, DepotEintraegeGeloescht;

    /// <summary>
    /// <b>Routen eines Gebaeudes streichen</b> — <c>0x440190(Platz)</c>, woertlich:
    /// <code>
    ///   fuer jeden der 400 Saetze mit Fassung != 0:
    ///     die ERSTE Quelle == Platz  -> 0xFF, geaendert
    ///     Ziel == Platz              -> 0xFF, +0x0E := 0, geaendert
    ///     geaendert und alle vier Quellen 0xFF -> +0x0E := 0
    /// </code>
    /// Fahrziel (+0x0F), Ladung und der Auftrag des Wagens bleiben unangetastet.
    /// </summary>
    public void RouteGebaeudeStreichen(int platz)
    {
        if (EinnahmeRoutenAlt || platz < 0) return;
        foreach (var r in _routen)
        {
            if (r.Fassung == 0) continue;                  // @0x4401AF
            bool geaendert = false;
            for (int k = 0; k < 4; k++)                    // @0x4401BA..0x4401D9: nur die erste Fundstelle
            {
                if (r.Quelle[k] != platz) continue;
                r.Quelle[k] = -1;
                geaendert = true;
                break;
            }
            if (r.Ziel == platz)                           // @0x4401E7
            {
                r.Ziel = -1;
                if (r.Gestartet) RoutenAngehalten++;
                r.Gestartet = false;
                geaendert = true;
            }
            if (!geaendert) continue;
            RoutenGestrichen++;
            bool leer = r.Quelle[0] < 0 && r.Quelle[1] < 0 && r.Quelle[2] < 0 && r.Quelle[3] < 0;
            if (leer)                                      // @0x44021F cmp bp,0x3FC
            {
                if (r.Gestartet) RoutenAngehalten++;
                r.Gestartet = false;
            }
        }
    }

    /// <summary>
    /// <b>Die Insassen einer eingenommenen BASIS loeschen</b> — C <c>0x43D19F…
    /// 0x43D20F</c> (F <c>0x43C1C6…0x43C236</c>): die ganze Andockliste, je Griff
    /// ausdocken und <c>0x410E60</c>. Kein Wrack, kein Tod, keine Statistik.
    ///
    /// <para>Bei uns ist die eine Liste des Originals zweigeteilt: <see
    /// cref="Entity.Garage"/> (eingefahrene, echte Saetze) und <see
    /// cref="Entity.Depot"/> (fertig gebaute Entwurfsnummern, die noch nicht
    /// draussen stehen — im Original waeren das schon Einheiten mit UKOL 0x33).
    /// Beide gehen.</para>
    ///
    /// <para>⚠ Die <see cref="Entity.BuildQueue"/> ist UNSERE Zutat und hat im
    /// Original keine Entsprechung — sie bleibt hier unberuehrt, bis er
    /// entschieden hat.</para>
    /// </summary>
    private void EinnahmeInsassenLoeschen(Entity b)
    {
        if (EinnahmeInsassenAlt || b.BType != 1) return;
        foreach (var u in b.Garage)
        {
            int ui = _entities.IndexOf(u);
            if (TodesLog)
                GD.Print($"tod: {LabelOf(u)} (Platz {u.Slot}, Spieler {u.Owner}) im Depot von "
                       + $"{BuildingTypeName(b.BType)} {b.Slot} GELOESCHT, Grund: Einnahme der Basis (0x43D19F)");
            if (ui >= 0)
            {
                _sel.Remove(ui);
                foreach (var other in _entities)
                    if (other.Target == ui) other.Target = -1;
                if (_selected == ui) SetPrimary();
            }
            u.InGebaeude = null;
            u.Hp = 0;
            u.Dead = true;
            u.DeadTime = 0;
            u.Path = null;
            u.Target = -1;
            u.Reserved = null;
            InsassenGeloescht++;
        }
        b.Garage.Clear();
        DepotEintraegeGeloescht += b.Depot.Count;
        b.Depot.Clear();
    }
}
