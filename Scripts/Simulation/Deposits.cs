using System.Collections.Generic;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// WOHER DIE VORKOMMEN KOMMEN — die eine Liste, aus der <c>CellOnDeposit</c>
/// (C <c>0x421400</c>, F <c>0x4205C0</c>) fragt, und ihre ZWEI Quellen.
///
/// <para>⚠⚠ 01.10.2026, bug-381 — <b>BERICHTIGT: ZWEI TAFELN, NICHT EINE.</b>
/// Seine Meldung: »K23: Terranium Finder kann nichts; Vorkommen sofort baubar
/// statt erst nach Suche«. Selbst gelesen (C-EXE): <c>add_terra_place
/// 0x4D0A10</c> schreibt NUR in <b>sec78</b> <c>0xBC6D40</c> (6 Byte, 50 Plätze:
/// +0 belegt, +1 Spalte, +2 Zeile, +4 Menge) — nie in sec38. sec78 lesen
/// ausserdem nur der Leerlauf-Automat des Finders (<c>0x4084C0…0x408751</c>),
/// das Leeren (<c>0x41F088</c>, <c>0x4D0AA0</c>) und Laden/Speichern
/// (<c>0x41D94F</c>, <c>0x41E9CA</c>) — keine Missionsweiche, kein Fahrplan.
/// Die Vorkommensfrage des Minenbaus <c>0x421400</c> läuft über <b>sec38</b>
/// <c>0x6783E8</c> (F <c>0x677448</c>). Also: was das Skript legt, ist ERZ IM
/// BODEN (<see cref="_erz"/>), und erst ein Terranium-Finder macht daraus einen
/// Bauplatz (Simulation/TerraSuche.cs). Gezählt über alle 33 Missionen
/// (berichte/terranium-finder-fable.md §7): Skriptvorkommen gibt es NUR in
/// M23, 25, 26, 29–33, und dort ist der Finder (Teil 78) ab Fahrplan-Zustand
/// 23 frei — keine Mission verliert ihren einzigen Bauplatz.
/// <c>--erz-sofort-baubar</c> = der alte Stand (Skript direkt in sec38).</para>
///
/// <para>⚠ Hier stand bis zum 01.10. <c>@0x4205C0</c> allein — das ist die
/// F-Fassung derselben Routine; in der C-Fassung liegt an 0x4205C0 eine
/// Nebelroutine, die Vorkommensfrage ist dort <c>0x421400</c> (beide
/// Anfänge Byte für Byte verglichen: <c>sub esp,0x10 … mov cl,[ebx*2+sec38]</c>).</para>
///
/// <para><b>1. Das Missionsskript</b>, und das ist die gelesene Quelle:
/// <c>add_terra_place(spalte, zeile, menge)</c> (C: <c>0x4D0A10</c>,
/// F: <c>0x4D05C0</c>) im SETUP-Block einer Mission — gemessen <b>50 Aufrufe in
/// 8 Missionen</b>, jeder mit drei Konstanten, in beiden Fassungen gleich
/// (<c>aekernel-tools/mission_terra.py</c>). Sie kommt über
/// <c>SetTerraPlaces(_mscript.Terra)</c> herein und hat VORRANG: das Skript
/// leert die Liste und schreibt seine eigene.</para>
///
/// <para><b>2. Die Karte selbst</b> — <see cref="Simulation.NavGrid.Deposits"/>,
/// und das ist <b>UNSERE ZUTAT für eine ERZEUGTE Karte</b>. Sie hat kein
/// Missionsskript, also hätte sie nach Quelle 1 gar keine Vorkommen; gemessen:
/// Feld-Rohstoffmine <b>0 Bauplätze</b>, Kontostand <b>0</b>. Der Karteneditor
/// legt sie darum selbst (<c>Editor.MapDeposits</c>, dort steht die Messlatte aus
/// den 50 Originalvorkommen), schreibt sie in die Karten-<c>.json</c>
/// (<c>Import.ContentBuilder.MapMeta</c>), und von dort holt sie
/// <c>NavGrid.Build</c> beim Laden. <b>Eine gelieferte Karte trägt hier nichts</b>
/// — für sie bleibt es bei Quelle 1.</para>
///
/// <para><b>Warum es eine Eigenschaft und kein Feld ist.</b> Die Liste wird an
/// vier Stellen gelesen (<c>CellOnDeposit</c>, <c>HasDeposits</c>,
/// <c>TerraCheckLine</c>, <c>SetTerraPlaces</c>), und die Karte ist beim ersten
/// Zugriff schon geladen, das Missionsskript aber vielleicht noch nicht. Das
/// Nachziehen hängt darum am Zugriff und nicht an einer Ladestelle: so kann kein
/// Prüfstand mehr eine leere Liste sehen, weil er früher dran war als der Füller
/// — genau der Fehler, an dem <c>--build-check</c> auf map_23 »0 Bauplaetze«
/// meldete, obwohl die Vorkommen eingetragen waren.</para>
///
/// <para>Der Wechsel der Karte wird an <see cref="_nav"/> erkannt:
/// <c>NavGrid.Build</c> läuft je Karte genau einmal, also ist eine andere
/// Gitterinstanz eine andere Karte.</para>
/// </summary>
public partial class MapEntityLayer
{
    private readonly List<(int Col, int Row, int Amount)> _depositList = new();

    /// <summary>
    /// <b>Die dritte Quelle: die KARTE selbst (sec38).</b>
    ///
    /// <para>Der Klassenkommentar oben sagte, »eine gelieferte Karte traegt hier
    /// nichts«. Das ist widerlegt: sec38 ist die Tafel der freien Stellen
    /// (Zuteiler @0x420E30, »Cannot place more terra«, 50 Plaetze zu 14 Byte).
    /// GEMESSEN: 9 Vorkommen auf 6 Karten. Auf den Missionen 14, 17, 20 und 22
    /// legt das Missionsskript KEINE an — dort stand der Spieler bei uns vor
    /// einer Karte ohne einen einzigen Bauplatz fuer eine Mine.</para>
    ///
    /// <para>Skript und Karte werden VEREINIGT, nicht gegeneinander gestellt:
    /// im Original laedt der Kartenlader sec38, und <c>add_terra_place</c> des
    /// Missionsaufbaus legt weitere DAZU.</para></summary>
    private readonly List<(int Col, int Row, int Amount)> _karteTerra = new();

    /// <summary><c>--erz-sofort-baubar</c> — der Stand vor dem 01.10.2026
    /// (bug-381): die Vorkommen des Missionsskripts (sec78) landen unmittelbar
    /// in der Bauplatzliste (sec38), als wären sie schon aufgeschlossen.</summary>
    public static bool ErzSofortBaubar;

    /// <summary>Ein Satz der Tafel <b>sec78</b> »terra_place« <c>0xBC6D40</c>
    /// (6 Byte): +0 belegt, +1 Spalte, +2 Zeile, +4 Menge. Das ist das Erz IM
    /// BODEN — unsichtbar und nicht baubar, bis ein Finder es aufschliesst
    /// (<c>0x408736</c> setzt <c>belegt := 0</c>).</summary>
    public sealed class ErzSatz
    {
        public int Col, Row, Amount;
        public bool Belegt;
    }

    /// <summary>sec78 — in TAFELREIHENFOLGE, denn die Suche des Finders nimmt
    /// den ERSTEN Treffer (<c>0x4084AB</c>), nicht den nächsten.</summary>
    private readonly List<ErzSatz> _erz = new();

    /// <summary>Wie viele Erzplätze noch im Boden liegen (für Leiste und
    /// Prüfstand).</summary>
    public int ErzImBoden
    {
        get { int n = 0; foreach (var z in _erz) if (z.Belegt) n++; return n; }
    }

    /// <summary>Das Gitter, aus dem <see cref="_depositList"/> gefüllt wurde —
    /// null, solange nichts nachgezogen wurde.</summary>
    private Simulation.NavGrid? _depositNav;

    /// <summary>Die Vorkommen. Siehe den Klassenkommentar für die zwei
    /// Quellen.</summary>
    private List<(int Col, int Row, int Amount)> _deposits
    {
        get
        {
            if (_nav != null && !ReferenceEquals(_depositNav, _nav))
            {
                _depositNav = _nav;
                _depositList.Clear();
                // ⭐ bug-381: mit der Karte wechseln auch Erz und Markierungen.
                _erz.Clear();
                TerraKartenwechsel();
                foreach (var d in _nav.Deposits) _depositList.Add(d);
                foreach (var d in _karteTerra) _depositList.Add(d);
                if (_depositList.Count > 0)
                    Godot.GD.Print($"Vorkommen: {_depositList.Count} Rohstoffstellen aus der " +
                                   $"KARTE — {_karteTerra.Count} davon aus sec38 (echte " +
                                   $"Kartendaten), {_depositList.Count - _karteTerra.Count} " +
                                   "aus dem Gitter einer erzeugten Karte (unsere Zutat)");
            }
            return _depositList;
        }
    }

    /// <summary>Wieviel Terranium im Boden liegt, wo diese Zelle steht — die
    /// <c>menge</c> des Vorkommens, in dessen 3x3-Fenster sie liegt, sonst 0.
    ///
    /// <para>Damit bekommt eine neu gebaute Feld-Rohstoffmine etwas zu fördern.
    /// Ohne das blieb <c>Entity.Deposit</c> auf seinem Anfangswert −1 stehen, der
    /// Förderschritt (<c>e.Deposit &gt; 0</c>) lief nie, und der Kontostand blieb
    /// 0 — eine baubare Mine, die nichts einbringt.</para></summary>
    public int DepositAmountAt(int col, int row)
    {
        foreach (var d in _deposits)
            if (col >= d.Col && col < d.Col + 3 && row >= d.Row && row < d.Row + 3)
                return d.Amount;
        return 0;
    }
}
