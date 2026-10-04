namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE ZEIGER DER BEFEHLSARTEN</b> (04.10.2026, bug-431) — was bug-415 nicht
/// angeschlossen hatte. Selbst gelesen an <c>GAME.EXE</c> (C), Vorlage
/// <c>berichte/maus-befehle-fable.md</c> §1.2.
///
/// <para><b>1. Wer die Befehlsart setzt.</b> Der Knopfverteiler <c>0x4485D0</c>,
/// Fensterart 1 (Einheitenmenue, Arm <c>0x448746</c>), schaltet ueber
/// <c>byte[0x44DDEC + Zeile]</c> / Tafel <c>0x44DD64</c> nach der Zeile der
/// Befehlsliste <c>0x4FD660 + 30·n</c> (Namen selbst ausgelesen):</para>
/// <code>
///   Zeile 0x0A »Mine legen«, 0x0B »Falle legen"  -> @0x4488F7  Befehlsart 8
///   Zeile 0x0C »Bruecke/Mole reparieren«         -> @0x448920  Befehlsart 4
///   Zeile 0x0D »Bruecke bauen«                    -> @0x44893F  Befehlsart 1
///   Zeile 0x0E »Mole bauen«                       -> @0x44895E  Befehlsart 2
///   Zeile 0x11 »Depot bauen«                      -> @0x4489ED  Befehlsart 5
///   Zeile 0x12 »Mine bauen«                       -> @0x448A0C  Befehlsart 6
///   Zeile 0x13 »Generator bauen«                  -> @0x448A2B  Befehlsart 7
///   Zeile 0x15 »Terranium suchen«                 -> @0x448A79  Befehlsart 3
///   Zeile 0x26 »-Dig up Bunker«                   -> @0x448BF2  Befehlsart 9
/// </code>
/// <para><b>2. Befehlsart -> Zeigerzustand</b> in der Zeigerwahl <c>0x4315D0</c>
/// @<c>0x4316F5</c> (nur wenn nicht gerollt wird, <c>word[0x502AC0] == 0</c>),
/// Tafel <c>0x432A10[Befehlsart − 1]</c>, jeweils an der MAUSZELLE
/// <c>(0x5387D0, 0x5387D4)</c>:</para>
/// <code>
///   1 @0x431725  0x40146F(x,y,Einheit)       ? 20 : 21     Bruecke
///   2 @0x431774  0x401B90(x,y) != 0xFF       ? 20 : 21     Mole
///   3 @0x4317C9  ohne Pruefung                 13          Terranium
///   4 @0x4317E0  0x4023C9(x,y)               ? 14 : 15     Ausbessern
///   5 @0x431827  0x401A46(E,x,y,5,1)         ? 20 : 21     Depot
///   6 @0x43187A  0x4014CE(E,x,y,1)           ? 20 : 21     Mine (auf Vorkommen)
///   7 @0x4318D0  0x401A46(E,x,y,7,1)         ? 20 : 21     Generator
///   8 @0x431923  16 (+ Rechteckzustand 0xA32180)             Minen/Falle legen
///   9 @0x4319DA  E+0x32 != 0 -> 25; Abstand² > 26 -> 19;
///                eigenes Fahrzeug (Klasse 0, nicht E, +0x09 == 0) oder
///                Gebaeude 8000..13999 -> 17; sonst 18
/// </code>
/// <para><b>3. Zustand -> Bild</b> in <c>0x4A9AB0</c>, Tafel <c>0x4A9BEC</c>
/// (roh gelesen): 13→19, 14→17, 15→18, 16→20, 17→21, 18→22, 19→23, 20→14,
/// 21→15, 22→17, 25→24; Zustand 100 (@0x4A9ADD) → <c>0x4A9BC5 mov dl,0x1A</c>
/// = Bild 26.</para>
/// <para><b>Angeschlossen</b> sind die Befehlsarten, die wir haben: 1 (unser
/// <see cref="OrderBruecke"/>), 2 (<see cref="OrderMole"/>), 3, 4
/// (<see cref="OrderAusbessern"/>), 5, 6, 7 — und Bild 26 ueber dem Koerper des
/// Kartenschirms (siehe <c>MapViewer.KartenschirmZeiger</c>).
/// <b>Nicht</b> angeschlossen: Befehlsart 8 (Minen/Falle legen — der
/// Minenleger-Befehl ist nicht gebaut) und 9 (»-Dig up Bunker«, Zeiger 21…24 —
/// die Befehlsart haben wir nicht).</para>
/// <para>⚠ (V) Die Gueltigkeit fragt UNSERE vorhandenen Pruefungen derselben
/// Befehle (dieselben, die Vorschau und Klick fragen), nicht eine Abschrift von
/// <c>0x40146F</c>/<c>0x401A46</c>/<c>0x4014CE</c>/<c>0x4023C9</c> — so sagen
/// Zeiger, Vorschau und Klick dasselbe. Fuer Depot/Generator gilt die Zelle mit
/// unserem Eckversatz (<see cref="BuildOffsetOfOrder"/>), wie beim Klick.</para>
/// <para>Gegenschalter <c>--befehlszeiger-aus</c> (Stand vor dem 04.10.: im
/// Setzmodus der Zeiger nach dem Ziel unter der Maus, Terranium 19 ueber den
/// alten Weg).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--befehlszeiger-aus</c> — siehe Kopf.</summary>
    public static bool BefehlszeigerAus;

    /// <summary>Bild 14/15: Befehlsart 1, 2, 5, 6, 7 gueltig / ungueltig
    /// (Zustand 20/21). Bild 17/18: Befehlsart 4 (Zustand 14/15). Bild 19:
    /// Befehlsart 3 (Zustand 13). Bild 26: Kartenschirm (Zustand 100).</summary>
    public const int ZeigerZielGut = 14, ZeigerZielSchlecht = 15,
                     ZeigerAusbessernGut = 17, ZeigerAusbessernSchlecht = 18,
                     ZeigerTerranium = 19, ZeigerKartenschirm = 26;

    private readonly List<SiteCell> _befehlsZellen = new();

    /// <summary>Ist die Befehlsart an dieser Zelle gueltig? Dieselben Pruefungen
    /// wie <see cref="PlacementHover"/> und <see cref="PlacementClick"/>.
    /// null = diese Befehlsart pruefen wir nicht (Terranium: ohne Pruefung).</summary>
    public bool? BefehlsartGueltig(int modus, int col, int row)
    {
        switch (modus)
        {
            case OrderTerraSuche: return null;
            case OrderMole: return MolePlatzOk(col, row);
            case OrderAusbessern: return RampeAn(col, row) != null || StegAn(col, row) != null;
            case OrderBruecke: return BrueckePlatzOk(col, row, PlacementUnit);
        }
        if (Patterns == null) return false;
        var off = BuildOffsetOfOrder(modus);
        if (modus == OrderFieldMine)
        {
            int k = DepositIndexAt(col, row);
            if (k < 0) return false;
            var d = _deposits[k];
            return CanBuild(Patterns, TypeFieldMine, d.Col + off.X, d.Row + off.Y, -1,
                            _befehlsZellen, skipDeposit: true);
        }
        int typ = BuildTypeOfOrder(modus);
        if (typ <= 0) return false;
        return CanBuild(Patterns, typ, col + off.X, row + off.Y, -1, _befehlsZellen);
    }

    /// <summary>Das Zeigerbild der laufenden Befehlsart an dieser Zelle, −1 =
    /// keine Befehlsart (oder <c>--befehlszeiger-aus</c>).</summary>
    public int BefehlsartZeigerbild(int col, int row)
    {
        if (BefehlszeigerAus || PlacementMode == 0) return -1;
        return BefehlsartZeigerbild(PlacementMode, col, row);
    }

    /// <summary>Dasselbe fuer eine vorgegebene Befehlsart (Pruefstand).</summary>
    public int BefehlsartZeigerbild(int modus, int col, int row)
    {
        if (BefehlszeigerAus) return -1;
        if (modus == OrderTerraSuche) return ZeigerTerranium;              // Zustand 13
        if (modus is not (OrderMole or OrderAusbessern or OrderBruecke
                          or OrderDepot or OrderFieldMine or OrderGenerator)) return -1;
        bool gut = BefehlsartGueltig(modus, col, row) == true;
        if (modus == OrderAusbessern)                                      // Zustand 14/15
            return gut ? ZeigerAusbessernGut : ZeigerAusbessernSchlecht;
        return gut ? ZeigerZielGut : ZeigerZielSchlecht;                   // Zustand 20/21
    }

    /// <summary>Pruefstand: Kartengroesse in Zellen.</summary>
    public (int W, int H) KartenMassFuerProbe() => _nav == null ? (0, 0) : (_nav.Width, _nav.Height);

    /// <summary>Pruefstand: die Befehlsart setzen, ohne den Knopfweg (der je
    /// Befehlsart ein anderes Fahrzeug verlangt). 0 = aus.</summary>
    public void BefehlsartFuerProbe(int modus, int einheit)
    {
        PlacementMode = modus;
        PlacementUnit = modus == 0 ? -1 : einheit;
        if (modus == 0) ClearBuildPreview();
    }
}
