namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ 29.10.2026 — <b>Paket P5 Tueren</b> der Testerrunde 0.6.12 (KayelGee R8 K6/K15/K18,
/// Spielerentscheidung 9). Lesung <c>berichte/tueren-einnahme-r8-opus.md</c>, Bau
/// <c>berichte/bau-p5-0-6-12.md</c>.
///
/// <para><b>1. Der Infanteriearm (bug-648 K6).</b> Der Gebaeudetakt nimmt Fussvolk auf
/// Tuer 0 einer Basis (<c>0x43D661…0x43D7E1</c>), eines Depots (<c>0x43D8E8…0x43D9DB</c>)
/// und eines Bahnhofs (<c>0x43DB0A…0x43DBFB</c>) ohne Anmeldung und ohne Tor auf: Zelle Z =
/// imap − 10000, Zaehler 9 (<c>@0x43D684</c>), je Mitgliedswort <c>0x74EC8C + 22·Z</c>
/// (<c>@0x43D692</c>): UKOL != 0 → naechstes (<c>@0x43D6C0</c>); Besitzer
/// <c>Gebaeude+0x05 == m/1000</c> (<c>@0x43D6DA…0x43D6F1</c>) → aus dem Trupp
/// (<c>0x4021D0</c>), andocken (<c>0x40259A</c>/<c>0x40236A</c>/<c>0x401BCC</c>), UKOL 0x32,
/// AKCE := Gebaeude. ALLE NEUN im selben Takt. Bei uns: jeder lebende Soldat mit
/// Col/Row == Tuer 0, ohne Weg und Auftrag, wird sofort <see cref="Einfahren"/> — nicht
/// nur der Stempeltraeger der Zelle (<c>NavGrid</c> fuehrt EINEN Stempel je Zelle).</para>
///
/// <para>Und: <b>Fussvolk wird nicht angemeldet.</b> Die Anmeldung 48 gilt nur fuer
/// <c>imap &lt; 0x1F40</c> (<c>@0x43D57E</c>), und der Torautomat schliesst bei 0x84 nur fuer
/// ein FAHRZEUG mit UKOL 48 (<c>@0x43D366 cmp cx,0x1F40 / jae</c>) — ein Trupp auf einer
/// Tuer haelt das Tor offen. Bei uns bekam der Stempeltraeger 48, auch Fussvolk.</para>
///
/// <para><b>2. Der Fremdzweig (Spielerentscheidung 9).</b> Ein Soldat mit UKOL 0, dessen
/// Besitzer NICHT der Gebaeudebesitzer ist (auch ein Verbuendeter, es gibt keine
/// Buendnisfrage), wird geloescht: Basis <c>@0x43D72A…0x43D7C4</c> mit Bild
/// <c>0x401D5C → 0x4357F0(RX, RY, POHYB, KOLIK, 310 + rand%6, +0x1D, +0x1E)</c> = EINE
/// Flamme (310..315, dieselbe Folge wie Schiffstod/Gebaeudebrand) an seiner Feinlage
/// mit seinem Streuversatz, dann <c>Zasah 0x401217(40050, imap[RX−1, RY])</c> = ein Treffer
/// OHNE Schuetzen mit Angriff 50 (Band 40000..40999, cerebrum [c-3eded4]) auf die Zelle
/// LINKS von ihm (<c>@0x43D776…0x43D795</c>: <c>(RX−1)·256 + RY</c>), dann
/// <c>0x4021D0</c> aus dem Trupp und <c>0x4011B3 → 0x410E60</c> loeschen (kein Sterbebild,
/// keine Leiche). Depot <c>@0x43D9AA…0x43D9BE</c> und Bahnhof <c>@0x43DBD0…0x43DBE4</c>:
/// nur loeschen, ohne Bild und ohne Treffer. Was links der Tuer steht, ist meist der
/// Grundriss des Gebaeudes selbst (60000+Platz → Gebaeudearm 0x40D269): der Fremde
/// beschaedigt die Basis um ~50.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN:</b> (a) UKOL 0 ist bei uns »Ukol 0 oder 48, kein Weg,
/// keine Befehle« (48 nur noch aus alten Spielstaenden); (b) volle Garage: der Soldat
/// bleibt draussen (wie bisher, <see cref="GarageVoll"/>), das Original loescht ihn
/// (<c>0x43BFC0 @0x43BFE6</c>); (c) nach dem Herausnehmen wird die Tuerzelle aus den
/// Ankern der Uebrigen neu gestempelt (das Original hat Trupps statt Stempel).</para>
///
/// <para>Gegenschalter: <c>--tuer-trupp-alt</c> (nur der Stempeltraeger, mit Torzyklus,
/// Fussvolk wird angemeldet; Stand 0.6.12-P4), <c>--tuer-fremdsoldat-alt</c> (fremdes
/// Fussvolk bleibt stehen). Pruefstand <c>--tuer-trupp-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--tuer-trupp-alt</c> — bug-648: der Stand vor P5.</summary>
    public static bool TuerTruppAlt;

    /// <summary><c>--tuer-fremdsoldat-alt</c> — fremdes Fussvolk auf Tuer 0 bleibt stehen.</summary>
    public static bool TuerFremdsoldatAlt;

    /// <summary>Zaehler fuer Pruefstand und Meldezeile.</summary>
    public int TuerTruppEingefahren, TuerFremdGeloescht, TuerFremdFlammen, TuerNeuGestempelt;

    /// <summary>
    /// Der Infanteriearm fuer Tuer 0 von <paramref name="b"/> (nur Garage-Arten, die
    /// Fabrik hat keinen Einfahrarm). Laeuft im <see cref="TorTakt"/> nach dem Torautomaten.
    /// </summary>
    private void TuerTruppArm(Entity b, int dc, int dr)
    {
        if (TuerTruppAlt || _nav == null || !GarageTyp(b.BType)) return;
        bool angefasst = false;
        // aufsteigend nach Listenplatz: der Trupp hat keine Reihenfolge, die wir haetten
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.Dead || u.IsBuilding || u.IsProp || u.Infantry < 0 || u.Verbraucht) continue;
            if (u.Col != dc || u.Row != dr || u.InGebaeude != null || Untergestellt(u)) continue;
            if (u.Ukol != UkolFrei && u.Ukol != UkolAngemeldet) continue;          // @0x43D6C0, Setzung (a)
            if (u.Path is { Count: > 0 } || u.Orders.Count > 0 || u.Reserved != null) continue;
            if (u.Owner == b.Owner)                                                // @0x43D6F1
            {
                int vor = Eingefahren;
                Einfahren(b, i, u);
                if (Eingefahren > vor) { TuerTruppEingefahren++; angefasst = true; }
                continue;
            }
            if (TuerFremdsoldatAlt) continue;
            if (b.BType == 1)
            {
                // @0x43D72A: eine Flamme 310+rand%6 an seiner Feinlage (steht = Zellmitte) + Streuversatz
                var (sx, sy) = StreuWirksam(u);
                int f = Simulation.Determinism.Roll(6);
                SchiffsBild("flamme" + f, dc, dr, Vector2.Zero, sx, sy);
                TuerFremdFlammen++;
                // @0x43D79E: Zasah(40050, imap[RX-1, RY]) — Angriff 50 ohne Schuetzen, Zelle links
                SkripttrefferZelle(dc - 1, dr, 50, "FREMDSOLDAT AUF DER TUER (0x43D79E)");
            }
            GD.Print($"tuer: fremder Soldat {LabelOf(u)} (Platz {u.Slot}, Spieler {u.Owner}) auf Tuer 0 von "
                   + $"{BuildingTypeName(b.BType)} {b.Slot} (Spieler {b.Owner}) GELOESCHT "
                   + (b.BType == 1 ? "(0x43D72A, Flamme + Treffer 50 auf links)" : "(ohne Bild, 0x43D9AA/0x43DBD0)"));
            RemoveSoldUnit(i, u);            // 0x4011B3 -> 0x410E60: weg, keine Leiche
            u.Verbraucht = true;
            TuerFremdGeloescht++;
            angefasst = true;
        }
        if (angefasst) TuerZelleNeuStempeln(dc, dr);
    }

    /// <summary>Setzung (c): traegt die Zelle keinen Stempel mehr, aber steht dort noch
    /// ein lebender Soldat, bekommt der mit dem kleinsten Listenplatz ihn zurueck.</summary>
    private void TuerZelleNeuStempeln(int c, int r)
    {
        if (_nav == null || _nav.OccupantAt(c, r) >= 0) return;
        for (int j = 0; j < _entities.Count; j++)
        {
            var o = _entities[j];
            if (o.Dead || o.IsBuilding || o.IsProp || o.Infantry < 0 || o.InGebaeude != null) continue;
            if (o.Col != c || o.Row != r) continue;
            _nav.SetOccupant(c, r, j, true);
            TuerNeuGestempelt++;
            return;
        }
    }

    // =====================================================================
    // 3. --torlog (bug-657 K15)
    // =====================================================================

    /// <summary><c>--torlog</c>: je Tor, das laenger als <see cref="TorlogSchwelle"/> Takte
    /// am Stueck offen (0x84) steht, EINE Zeile.</summary>
    public static bool Torlog;
    public const int TorlogSchwelle = 200;
    private readonly Dictionary<(Entity, int), int> _torOffenSeit = new();
    private readonly HashSet<(Entity, int)> _torGemeldet = new();
    public int TorlogZeilen;
    public readonly List<string> TorlogListe = new();

    private void TorlogNotieren(Entity b, int d, int z, int dc, int dr, int occ)
    {
        var key = (b, d);
        if (z != TorOffen) { _torOffenSeit.Remove(key); _torGemeldet.Remove(key); return; }
        if (!_torOffenSeit.TryGetValue(key, out int seit)) { _torOffenSeit[key] = _taktNr; return; }
        if (_taktNr - seit < TorlogSchwelle || _torGemeldet.Contains(key)) return;
        _torGemeldet.Add(key);
        var auf = occ >= 0 && occ < _entities.Count ? _entities[occ] : null;
        string was = auf == null ? "leer"
                   : auf.IsBuilding ? "GEBAEUDE " + LabelOf(auf)
                   : auf.IsProp ? "OBJEKT"
                   : $"{(auf.Dead ? "TOT " : "")}{(Untergestellt(auf) ? "UNTERGESTELLT " : "")}{(auf.Infantry >= 0 ? "Fussvolk" : "Fahrzeug")} "
                     + $"{LabelOf(auf)} Platz {auf.Slot} Spieler {auf.Owner} UKOL {auf.Ukol} "
                     + $"Weg {(auf.Path is { Count: > 0 } p ? p.Count : 0)} Befehle {auf.Orders.Count} Ziel {auf.Target}"
                     + $" Lage ({auf.Col},{auf.Row})";
        int fuss = 0;
        foreach (var o in _entities)
            if (!o.Dead && !o.IsBuilding && !o.IsProp && o.Infantry >= 0 && o.Col == dc && o.Row == dr && !Untergestellt(o)) fuss++;
        var typ = BuildingDoors(b.BType);
        string fallD = d < typ.Count
            ? (typ[d].X == b.DoorCells[d].Col && typ[d].Y == b.DoorCells[d].Row ? "gleich" : $"ABWEICHUNG Typtafel ({typ[d].X},{typ[d].Y})")
            : "Typtafel ohne diese Tuer";
        string zeile = $"torlog: Takt {_taktNr} {BuildingTypeName(b.BType)} {b.Slot} (Spieler {b.Owner}) Tuer {d} ({dc},{dr}) "
                     + $"offen seit Takt {seit}: Stempel {occ} = {was}; Fussvolk auf der Zelle {fuss}; "
                     + $"Kartensatz ({b.DoorCells[d].Col},{b.DoorCells[d].Row}) gegen Typtafel {fallD}";
        TorlogZeilen++;
        if (TorlogListe.Count < 200) TorlogListe.Add(zeile);
        GD.Print(zeile);
    }

    // =====================================================================
    // 4. K18 — Einnehmer verteilen, Vorplatz nur Fahrzeuge
    // =====================================================================

    /// <summary><c>--einnahme-ein-ziel-alt</c> — bug-660: alle Einnehmer fahren auf dieselbe
    /// Zelle unter Tuer 0 (Stand vor P5).</summary>
    public static bool EinnahmeEinZielAlt;

    /// <summary><c>--vorplatz-stempel-alt</c> — bug-660: die Vorplatz-Raeumung nimmt je Zelle
    /// nur den Stempeltraeger, auch Fussvolk (Stand vor P5).</summary>
    public static bool VorplatzStempelAlt;

    public int EinnehmerVerteilt;

    /// <summary>
    /// ⚠ <b>UNSERE SETZUNG</b> (bug-660 K18): das Original hat keinen Einnahmebefehl. Der
    /// erste Einnehmer bekommt die Einnahmezelle, jeder weitere die naechste freie Zelle
    /// im Ring darum (wie <c>NavGrid.NearestFree</c>, Radius 4), aber nur VOR dem Gebaeude
    /// (Zeile ≥ Einnahmezelle), nie eine Tuerzelle und nie eine schon vergebene. Damit
    /// liegen sie im 5×4-Feld der Vorplatz-Raeumung und werden nach dem Abschluss
    /// weggeschickt, statt sich um die frei gewordene Tuer zu draengen.
    /// </summary>
    private Vector2I? EinnehmerNachbarzelle(Entity b, Vector2I mitte, int ui, HashSet<Vector2I> vergeben)
    {
        if (_nav == null) return null;
        var u = _entities[ui];
        bool Tuer(int c, int r)
        {
            foreach (var (dc, dr) in b.DoorCells) if (b.Col + dc == c && b.Row + dr == r) return true;
            return false;
        }
        for (int rad = 1; rad <= 4; rad++)
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != rad || dy < 0) continue;
                    int c = mitte.X + dx, r = mitte.Y + dy;
                    if (c <= 0 || r <= 0 || c >= _nav.Width - 1 || r >= _nav.Height - 1) continue;
                    var z = new Vector2I(c, r);
                    if (vergeben.Contains(z) || Tuer(c, r)) continue;
                    if (_nav.IsFree(c, r, u.Move, ui)) return z;
                }
        return null;
    }

    /// <summary>Die Vorplatz-Raeumung nach <c>0x43D070…0x43D155</c>: jede FAHRZEUG-Einheit
    /// des Eindringlings im 5×4-Feld (alle, nicht nur der Stempeltraeger der Zelle).
    /// Fussvolk (Trupp, imap ≥ 10000, /1000 ≥ 10) trifft das Original nicht.</summary>
    private void VorplatzRaeumenAlle(Entity b, int who, int tx, int ty)
    {
        for (int x = tx - 2; x <= tx + 2; x++)
            for (int y = ty + 1; y <= ty + 4; y++)
            {
                if (!_nav!.InBounds(x, y)) continue;
                for (int g = 0; g < _entities.Count; g++)
                {
                    var u = _entities[g];
                    if (u.Col != x || u.Row != y) continue;
                    if (u.IsBuilding || u.IsProp || u.Dead || u.Owner != who || !u.Mobile || u.Infantry >= 0) continue;
                    if (Untergestellt(u)) continue;
                    VorplatzGefunden++;
                    int zy = Mathf.Min(u.Row + 6, _nav.Height - 1);
                    var weg = _nav.FindPath(new Vector2I(u.Col, u.Row), new Vector2I(u.Col, zy), u.Move, g);
                    if (weg == null || weg.Count == 0) continue;
                    u.Path = weg; u.PathIdx = 0; u.Goal = new Vector2I(u.Col, zy); u.StepCost = 0;
                    u.Orders.Clear(); u.Target = -1;
                    VorplatzGeschickt++;
                }
            }
    }
}
