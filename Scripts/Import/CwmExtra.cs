namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;

/// <summary>
/// The rest of what a map file carries: the tables the game's own debug dumps
/// named — mission targets, Terranium deposits, the bank, aircraft and supply
/// helicopters, the rail network, the player table, the buildable designs and
/// the trains.
///
/// Ported one to one from <c>cwm_extra.py</c>; every layout below was recovered
/// by disassembling GAME.EXE and the evidence addresses are kept with the
/// fields, because the address is what makes a reading checkable. What the
/// Python tooling produced is the reference this port is measured against
/// (<see cref="ImportSelfTest.RunEntities"/>).
///
/// The 23 campaign .CWM levels stop after section 38 and therefore carry none
/// of the sections past it — targets, players, designs, trains and the rail
/// lines come out empty for them, which is faithful, not a failure.
/// </summary>
public static class CwmExtra
{
    public const int SpojStride = 214, TargetStride = 6, DepositStride = 18;
    public const int SpecialStride = 68, TrainStride = 24, ShipStride = 42;
    public const int TargetsPerPlayer = 100;

    // ---- sec69: the mission targets ----------------------------------------

    /// <summary>sec69 — eight objective lists, one per player (8 x 100 x 6).
    ///
    /// The debug dump (@0x4134a4, format @0x4f71b8) walks section base + 0x258
    /// for 600 bytes, which is player 1's list alone; the section is 4800 bytes
    /// and every player owns 100 records of the same shape:
    /// +0x00 typ (1 = a building), +0x01 imp, +0x02 u16 building, +0x04 nici.
    /// `imp` is a weight and does not separate own from enemy targets.</summary>
    public sealed class Target
    {
        public int Player, Slot, Type, Importance, Building, Destroyed;
        public string BuildingName = "?";
        public int BuildingOwner = -1;
    }

    public static List<Target> Targets(CwmFile m)
    {
        var list = new List<Target>();
        var s = m.Sec(69);
        if (s == null) return list;
        for (int pl = 0; pl < 8; pl++)
        {
            int b = pl * TargetsPerPlayer * TargetStride;
            if (b + TargetsPerPlayer * TargetStride > s.Length) break;
            for (int i = 0; i < TargetsPerPlayer; i++)
            {
                int o = b + i * TargetStride;
                if (s[o] == 0) continue;
                list.Add(new Target
                {
                    Player = pl, Slot = i, Type = s[o], Importance = s[o + 1],
                    Building = BitConverter.ToUInt16(s, o + 2), Destroyed = s[o + 4],
                });
            }
        }
        return list;
    }

    // ---- sec28: the Terranium deposits --------------------------------------

    /// <summary>sec28 — 50 records of 18 (dest 0x878ad0). The mining panel
    /// @0x474500 indexes it with the building's `cis_typ`. +0x00 the building
    /// it belongs to, +0x08 capacity, +0x0a grade, +0x0c what is left.</summary>
    public sealed class Deposit
    {
        public int Slot, Building, Capacity, Grade, Terranium;
        public string BuildingName = "?";
    }

    public static List<Deposit> Deposits(CwmFile m)
    {
        var list = new List<Deposit>();
        var s = m.Sec(28);
        if (s == null) return list;
        for (int i = 0; i + DepositStride <= s.Length; i += DepositStride)
        {
            int r = BitConverter.ToUInt16(s, i);
            if (r == 0xFFFF || AllZero(s, i, DepositStride)) continue;
            list.Add(new Deposit
            {
                Slot = i / DepositStride, Building = r,
                Capacity = BitConverter.ToUInt16(s, i + 8),
                Grade = BitConverter.ToUInt16(s, i + 10),
                Terranium = BitConverter.ToUInt16(s, i + 12),
            });
        }
        return list;
    }

    // ---- sec73 / sec96: the bank and the progress slots ---------------------

    /// <summary>sec73 — the per-player balance, 8 x i32 (dest 0xa9c600). The
    /// base panel prints it as "Kontostand : $" @0x46b432.</summary>
    public static List<int> Money(CwmFile m)
    {
        var list = new List<int>();
        var s = m.Sec(73);
        if (s == null || s.Length < 32) return list;
        for (int i = 0; i < 8; i++) list.Add(BitConverter.ToInt32(s, i * 4));
        return list;
    }

    /// <summary>sec96 — 10 slots of 16 (dest 0xa3a9d0). The panel computes
    /// "% fertig" as done*100/total from +0x04 over +0x02 (@0x46b575).</summary>
    public sealed class Progress
    {
        public int Slot, Total, Done, Percent;
        public byte[] Raw = Array.Empty<byte>();
    }

    public static List<Progress> Progresses(CwmFile m)
    {
        var list = new List<Progress>();
        var s = m.Sec(96);
        if (s == null) return list;
        for (int i = 0; i + 16 <= s.Length; i += 16)
        {
            if (AllZero(s, i, 16)) continue;
            int total = BitConverter.ToInt16(s, i + 2);
            int done = BitConverter.ToInt16(s, i + 4);
            list.Add(new Progress
            {
                Slot = i / 16, Total = total, Done = done,
                // Python floor-divides; with a negative count that differs from
                // C#'s truncation, so it is spelled out rather than assumed
                Percent = total != 0 ? FloorDiv(done * 100, total) : 0,
                Raw = Slice(s, i, 16),
            });
        }
        return list;
    }

    // ---- sec19: aircraft and the supply helicopters -------------------------

    /// <summary>sec19 — 200 records of 68 (dest 0x6ddf70), the table a
    /// Flughafen's hangar list indexes (@0x4289bb).
    ///
    /// +0x00 u16 col, +0x02 u16 row, +0x08 kind, +0x3b name. Kinds seen: 1
    /// Shark, 2 Whale, 10 Fight, 13 Treibstoffheli, 14 Munitionheli. An
    /// aircraft sits at (0,0) exactly while it is inside a hangar.
    ///
    /// +0x09 owner — the customer search (@0x427990 / @0x427bd0) scans only the
    /// entity block owner*1000..+999. +0x2e the entity being served (>= 8000
    /// means a building), +0x31 what is left of the payload: a full top-up
    /// costs 50 (@0x424964), a Nachschub-Posten refills to 255 (@0x42499e).
    /// The rest are the instance values the spawn routine @0x4b1580 copies out
    /// of the template table.</summary>
    public sealed class Special
    {
        public int Slot, Col, Row, Kind, Speed, Hp, HpMax, Ammo, AmmoMax;
        public int Fuel, FuelMax, Payload, Airframe, Attack, Defence, Sight;
        public int Owner, Cargo, Customer;

        /// <summary>
        /// <b>Was ein Flugzeug fliegen lässt</b> — bis zum 16.08.2026 gar nicht
        /// gelesen, und deshalb standen die Flugzeuge der Menü-Demos still
        /// (Fehler D6: »In einer Demo sieht man Bomber auf einer Karte, die
        /// stehen aber nur in der Luft«).
        ///
        /// <para>Die Flugbewegung steht im gemeinsamen Abschluss von
        /// <c>move_airplanes</c> (@0x425050…0x425120), also HINTER dem Verteiler
        /// und damit für jedes Kind:</para>
        /// <code>
        ///   al = byte[+0x6ddf7c]        ; die RICHTUNG
        ///   fdiv [0x4f9208]             ; die Konstante ist 57,2958 = 180/pi,
        ///                               ;   der Winkel ist also in GRAD
        ///   fsin * Geschwindigkeit      ; dx
        ///   fcos * Geschwindigkeit      ; dy
        ///   feinX += dx  @0x42508C   bei &gt;= 40: feinX -= 40, col++, Sprit--
        ///   feinY += dy  @0x4250E1   bei &gt;= 40: feinY -= 40, row++, Sprit--
        /// </code>
        /// <para>Ein Flugzeug fliegt also nach seiner <b>Richtung</b>, nicht nach
        /// seinem Kunden — geradeaus, bis der Sprit alle ist.</para>
        ///
        /// <para><b>Die Umrechnung Laufzeit → Datei</b> steht auf zwei Ankern:
        /// <c>Customer</c> liegt in der Datei bei <c>+0x2e</c> und zur Laufzeit
        /// bei <c>0x6ddf9e</c> (Satzbasis <b>0x6ddf70</b>), und die Schrittweite
        /// ist auf beiden Seiten <b>68</b> (Datei <c>SpecialStride</c>, Laufzeit
        /// <c>ecx·17·4</c>).</para>
        ///
        /// <para>✅ <b>Belegt</b> an 190 Flugzeugsätzen aller Demo-Spielstände:
        /// <see cref="FineX"/> und <see cref="FineY"/> liegen <b>restlos in
        /// 0..39</b> — genau der Bereich, den der Code prüft
        /// (<c>cmp ax, 0x28</c>), kein einziger Ausreisser.</para>
        ///
        /// <para>⚠ <b><see cref="Dir"/> ist die schwächste der drei.</b> Sie ist
        /// über dasselbe Offset-Mapping hergeleitet, aber ihre Werte (0, 2..10,
        /// 25; 84 von 190 auf der 8) <b>stützen</b> die Deutung »Winkel in Grad«
        /// nicht — sie widersprechen ihr nur nicht. Wer sie anwendet, prüfe das
        /// Ergebnis im BILD.</para></summary>
        public int FineX, FineY, Dir;

        /// <summary>Der laufende und der geplante Auftrag (<c>+0x10</c> /
        /// <c>+0x11</c>, Laufzeit <c>0x6ddf80/81</c>). Gelesen sind 1 (»flieg
        /// nach x,y«, ohne Kunden), 3..5 (nicht überschreibbar), 7 (mit Kunden),
        /// 10 und 11 (die zwei Versorgungsaufträge). ✅ Über 190 Sätze kommt
        /// <b>kein Wert ausserhalb {0,1,4,6,7,10,11}</b> vor.</summary>
        public int Order, Order2;

        public bool Stored;
        public string Name = "";
    }

    /// <summary>
    /// <b>Ein Angebot des MARKTES</b> — eine fertige Einheit, die dort für Geld
    /// zu haben ist.
    /// </summary>
    public sealed class MarketOffer
    {
        /// <summary>Platz 0..49 in sec94/sec95.</summary>
        public int Slot;
        /// <summary>Der Preis aus sec95. <b>0 heisst: Platz frei</b> — so
        /// zählt das Original selbst (@0x4C0860 / @0x4C0D28).</summary>
        public int Price;
        /// <summary>Die Entwurfsnummer (+0x43) — damit löst sich das Angebot
        /// gegen sec47 zu einem Namen auf.</summary>
        public int Design;
        public int UnitType, GameUnitType, Attack, Defence, Energie, Speed, Sight, Range;

        /// <summary>Satz <b>+0x28</b> — die ERFAHRUNG der angebotenen Einheit,
        /// und der fehlende Schlüssel zum Preis.
        ///
        /// <para>⚠ Bis zum 18.08.2026 wurde dieses Byte nicht mitgelesen, und
        /// damit war der Ladenpreis nicht nachrechenbar: er ist
        /// <c>2,5 × Wert</c>, und in den Wert geht der Stufenfaktor ein
        /// (0,10 bis 7,00, @0x450FC5). Ohne <c>+0x28</c> kam für jedes Angebot
        /// der Preis einer frischen Einheit heraus — beim gestrigen Vergleich
        /// wäre das als »unsere Formel stimmt nicht« gelesen worden, obwohl die
        /// Formel stimmt und nur eine Eingabe fehlte.</para>
        ///
        /// <para>Der Nachschub des Originals würfelt sie aus (@0x4C0B97) und
        /// legt sie nach <c>+0x28</c> des Ladensatzes (@0x4C0FF6); die
        /// Ladenwertfunktion @0x451092 liest sie von dort wieder.</para></summary>
        public int Experience;
    }

    /// <summary>
    /// <b>Die Ware des Marktes</b> — Gebäudetyp 17, das »Geschäftszentrum«.
    ///
    /// <para><b>Gelesen.</b> Der Markt hält seinen Bestand in ZWEI Sektionen,
    /// und beide sind 50 Plätze lang:</para>
    /// <list type="bullet">
    /// <item><b>sec94</b> — <c>0xf3c</c> = 3900 B = <b>50 × 78</b> (Laufzeit
    /// <c>0x82AA30</c>). 78 ist die Einheitensatzbreite: das sind fünfzig
    /// FERTIGE Einheiten, keine Entwürfe.</item>
    /// <item><b>sec95</b> — <c>0x64</c> = 100 B = <b>50 × u16</b> (Laufzeit
    /// <c>0x81A3A8</c>): die Preise. <c>0</c> heisst »Platz frei«, und genau so
    /// zählt das Original (Zählschleife @0x4C0860, Suchschleife @0x4C0D28).</item>
    /// </list>
    ///
    /// <para>Der Preis ist NICHT fest: @0x451010 rechnet
    /// <c>30·(Bauteil₁+₂+₃)·Leben / Entwurf[+0x1e]</c> mal einem Prozentwert
    /// und legt <c>× 5/2</c> davon in sec95 ab. Dasselbe Modell kommt deshalb
    /// mit verschiedenen Preisen vor.</para>
    ///
    /// <para>⚠ Bis zum 16.08.2026 hat das Projekt diese zwei Sektionen gar
    /// nicht angefasst — der ganze Markt fehlte, und Typ 17 galt als »Deko des
    /// Editors«. Er ist keine: der Spieler fährt eine eigene Einheit auf eine
    /// der vier freien Eckzellen des 4×4-Grundrisses, dann öffnet das Fenster
    /// (@0x43E90C prüft genau diese vier Zellen im Belegungsraster).</para></summary>
    public static List<MarketOffer> MarketOffers(CwmFile m)
    {
        var list = new List<MarketOffer>();
        var s94 = m.Sec(94);
        var s95 = m.Sec(95);
        if (s94 == null || s95 == null) return list;
        int n = Math.Min(s94.Length / CwmData.EntityStride, s95.Length / 2);
        for (int i = 0; i < n; i++)
        {
            int price = BitConverter.ToUInt16(s95, i * 2);
            // ⚠ ZWEI verschiedene »kein Angebot«, beide gelesen:
            //   0      — nie belegter Platz (Zählschleife @0x4C0860)
            //   0xFFFF — GEKAUFT; @0x4C13A6 setzt den Preis darauf, und der
            //            Nachschub sucht @0x4C03C8 genau diese Plätze wieder
            //            (`cmp word[ecx*2+0x81a3a8], 0xFFFF`), um sie neu zu
            //            füllen.
            // ⚠ Über alle 225 Angebote der gelieferten Karten kommt 0xFFFF
            // NICHT vor — die Prüfung ist Vorsorge für Spielstände, die
            // mitten im Handel gespeichert wurden, kein beobachteter Fall.
            if (price == 0 || price == 0xFFFF) continue;
            int o = i * CwmData.EntityStride;
            list.Add(new MarketOffer
            {
                Slot = i, Price = price,
                Design = s94[o + 0x43],
                UnitType = s94[o + 0x0f], GameUnitType = s94[o + 0x0a],
                Energie = s94[o + 0x08], Attack = s94[o + 0x26],
                Defence = s94[o + 0x27], Speed = s94[o + 0x20],
                Sight = s94[o + 0x2c], Range = s94[o + 0x2b],
                Experience = s94[o + 0x28],
            });
        }
        return list;
    }

    public static List<Special> Specials(CwmFile m)
    {
        var list = new List<Special>();
        var s = m.Sec(19);
        if (s == null || s.Length < 200 * SpecialStride) return list;
        for (int i = 0; i < 200; i++)
        {
            int o = i * SpecialStride;
            if (AllZero(s, o, SpecialStride)) continue;
            int col = BitConverter.ToUInt16(s, o), row = BitConverter.ToUInt16(s, o + 2);
            list.Add(new Special
            {
                Slot = i, Col = col, Row = row, Kind = s[o + 0x08],
                Name = Cp437.GetString(s, o + 0x3b, SpecialStride - 0x3b),
                Stored = col == 0 && row == 0,
                Speed = s[o + 0x0d], Hp = s[o + 0x19], HpMax = s[o + 0x1a],
                Ammo = s[o + 0x16], AmmoMax = s[o + 0x17],
                Fuel = BitConverter.ToUInt16(s, o + 0x1c),
                FuelMax = BitConverter.ToUInt16(s, o + 0x1e),
                Payload = s[o + 0x20], Airframe = s[o + 0x21],
                Attack = s[o + 0x22], Defence = s[o + 0x23], Sight = s[o + 0x24],
                Owner = s[o + 0x09], Cargo = s[o + 0x31],
                Customer = BitConverter.ToUInt16(s, o + 0x2e),
                // ⚠ Siehe Special.FineX: ohne diese fünf steht jedes Flugzeug
                // still. Speed liegt bei +0x0d — die Richtung DIREKT DANEBEN
                // bei +0x0c, und genau die fehlte.
                FineX = BitConverter.ToUInt16(s, o + 0x04),
                FineY = BitConverter.ToUInt16(s, o + 0x06),
                Dir = s[o + 0x0c],
                Order = s[o + 0x10], Order2 = s[o + 0x11],
            });
        }
        return list;
    }

    // ---- sec53: the player table --------------------------------------------

    /// <summary>sec53 — 8 records of 40 (dest 0x87b140), found through the
    /// defeat check @0x4982a0.
    ///
    /// +0x00 0 = the human player, 1 = an active CPU, 0xFF = beaten;
    /// +0x01 name, +0x07 save comment, +0x15 the alliance row (1 = allied),
    /// +0x20 kills and +0x24 losses — the statistics screen prints the second
    /// after " / Verluste " (@0x48571b). The matrix is symmetric in every file
    /// checked. There is no trigger bytecode behind the missions: defeat is
    /// computed and the objective list is sec69.</summary>
    public sealed class Player
    {
        public int Index, Flag;
        public string Name = "", Comment = "";
        public List<int> Allies = new();
        public long Kills, Losses;
        public bool Beaten, Human;
    }

    public static List<Player> Players(CwmFile m)
    {
        var list = new List<Player>();
        var s = m.Sec(53);
        if (s == null || s.Length < 320) return list;
        for (int i = 0; i < 8; i++)
        {
            int o = i * 40;
            var p = new Player
            {
                Index = i, Flag = s[o],
                Name = Cp437.GetString(s, o + 1, 6),
                Comment = Cp437.GetString(s, o + 7, 0x15 - 7),
                Kills = BitConverter.ToUInt32(s, o + 0x20),
                Losses = BitConverter.ToUInt32(s, o + 0x24),
                Beaten = s[o] == 0xFF, Human = s[o] == 0,
            };
            for (int j = 0; j < 8; j++) if (s[o + 0x15 + j] == 1) p.Allies.Add(j);
            list.Add(p);
        }
        return list;
    }

    // ---- sec33 / sec34 / sec122: the rail network ---------------------------

    /// <summary>sec33 — the node table, 120 x 8 (dest 0xa8d508). A building's
    /// `rail` (+0x1a) is its node number and the node's +0x00 points back at
    /// the building — 76 of 76 agree on 1.DM and 10.DM. +0x02..+0x06 list up to
    /// five attached links, 0xFF meaning empty.</summary>
    public sealed class RailNode
    {
        public int Node, Building;
        public List<int> Links = new();
    }

    public static List<RailNode> RailNodes(CwmFile m)
    {
        var list = new List<RailNode>();
        var s = m.Sec(33);
        if (s == null) return list;
        for (int i = 0; i + 8 <= s.Length; i += 8)
        {
            if (AllZero(s, i, 8)) continue;
            var n = new RailNode { Node = i / 8, Building = BitConverter.ToUInt16(s, i) };
            for (int k = 2; k < 7; k++) if (s[i + k] != 0xFF) n.Links.Add(s[i + k]);
            list.Add(n);
        }
        return list;
    }

    /// <summary>The step a route code takes, verbatim from GAME.EXE VA
    /// 0x5043c0: the train tick does `wagon.x += byte [0x5043c0 + 2*c]` and
    /// `sec121[wagon] += (signed) byte [0x5043c1 + 2*c]` (@0x4c6da7), so
    /// <b>dx counts whole columns and dy half rows</b> — the asymmetry an
    /// earlier least-squares fit could not see, which had drawn 81 of 115 lines
    /// half a tile off in x. Walking this table hits the stored end point on
    /// 115 of 115 lines of the three .DM missions.</summary>
    public static readonly (int Dx, int Dy)[] SpojStep =
    {
        (0, 2), (0, -2), (1, 0), (-1, 0), (0, 1), (-1, -1), (-1, 1), (0, -1),
        (1, 1), (0, 1), (1, -1), (0, -1), (1, 0), (0, 0), (-1, -1), (0, 0),
    };

    /// <summary>The same code byte picks the rail piece the wagon is drawn on,
    /// through the second table at VA 0x5393f0; the tick stores the result in
    /// wagon +0x0b (@0x4c6de9).</summary>
    public static readonly int[] SpojPiece = { 0, 4, 6, 2, 7, 3, 1, 5, 7, 1, 5, 3, 0, 0, 0, 0 };

    /// <summary>sec34 — the SPOJ lines, 80 x 214 (dump @0x413400, format
    /// @0x4f7230). Bytes 0 and 1 are node numbers, the node's +0x00 a building.
    /// x1/x2 are +0x02/+0x04 and the matching y come from <b>sec122</b> (80 x 4,
    /// dest 0xa66dd8). The tail from +0x0d is the route: `delka` direction
    /// codes stepped through <see cref="SpojStep"/>.
    ///
    /// <b>⚠ REHABILITATED 2026-08-01.</b> The sec33 indirection was written off
    /// as untrustworthy — "its building is never the one the line ends on". It
    /// is trustworthy, and it always was: the node's +0x00 is a building slot,
    /// and checked against the route end points on every map that has lines it
    /// names the SAME building in <b>164 of 164</b> cases where both are known.
    ///
    /// What was wrong then were the buildings, not the nodes. Until the 4-byte
    /// correction of 30.07 every building carried its neighbour's coordinates,
    /// so the comparison could not have agreed. The old verdict was drawn from
    /// broken data and outlived it.
    ///
    /// It matters because the node covers far more ground: 542 node/end-point
    /// pairs exist and only 164 of them have a route end point that lands on a
    /// building. The node is therefore the primary link now, with the end point
    /// as the cross-check that earned it back.</summary>
    public sealed class Link
    {
        public int Slot, Node1, Node2, Bud1, Bud2, X1, X2, Delka, Faze;
        public int? Y1, Y2, End1, End2;
        public List<(int X, double Y)>? Route;
        public List<int>? Pieces;

        /// <summary>true = das Start-y stand NICHT in der Datei, sondern wurde
        /// aus den beiden Endgebäuden zurückgerechnet (siehe SolveStartY). Auf
        /// den drei Spielständen ist die Rechnung gegen den gespeicherten Wert
        /// geprüft; auf einer Leveldatei ist sie die einzige Quelle.</summary>
        public bool Rebuilt;
    }

    /// <param name="ignoreStoredY">Nur fuer den Pruefstand: tut so, als gaebe es
    /// sec122 nicht, damit die Rueckrechnung dort gemessen werden kann, wo die
    /// Antwort bekannt ist.</param>
    public static List<Link> Links(CwmFile m, List<CwmData.Building>? blds = null,
                                   bool ignoreStoredY = false)
    {
        var list = new List<Link>();
        var s33 = m.Sec(33);
        var s34 = m.Sec(34);
        var s122 = ignoreStoredY ? null : m.Sec(122);
        if (s33 == null || s34 == null) return list;

        // Every cell a building covers, pointing back at it. The footprint comes
        // from the spatial grid (see CwmData.Footprint), so a line ending on a
        // building's rail connection — which sits inside the building, not on
        // its anchor cell — finds the right structure.
        // Real footprint where the spatial grid gives one; the measured window
        // where it does not. Neither alone is enough: the footprints resolve
        // fewer end points than the window did, because a building the grid
        // holds no handle for drops out of the map entirely, and the window
        // alone is a guess at a shape the data actually knows.
        var at = new Dictionary<(int, int), int>();
        foreach (var b in blds ?? new List<CwmData.Building>())
        {
            bool real = b.FootW > 0 && b.FootH > 0;
            int w = real ? b.FootW : 6, h = real ? b.FootH : 2;
            for (int dx = 0; dx < w; dx++)
                for (int dy = 0; dy < h; dy++)
                    if (!at.ContainsKey((b.Col + dx, b.Row + dy)))
                        at[(b.Col + dx, b.Row + dy)] = b.Slot;
        }

        /// <summary>Which building a line ends on: the one whose ground the end
        /// point stands on.
        ///
        /// <b>Twice corrected.</b> Looking at the end cell and its eight
        /// neighbours found a building for 40 of 772 end points — 5%. The end
        /// point was never wrong: a building's stored cell is its top-left
        /// ANCHOR, and a line ends on the rail connection INSIDE the building.
        /// A hand-fitted window (dx 1..6, dy 1..2) got that to 328 of 460 with
        /// nothing ambiguous, and it was ours.
        ///
        /// It is no longer needed. The footprint is in the data after all — not
        /// in the 76-byte record but in the spatial grid, where every building
        /// carries a handle and the cells holding it are its ground (see
        /// <see cref="CwmData.Footprint"/>). The map above is built from the real
        /// footprints, so this is a plain lookup.</summary>
        int BuildingAt(int x, int y) => at.TryGetValue((x, y), out int v) ? v : -1;

        /// <summary>Wie weit dieser Punkt vom naechsten Gebaeudegrund entfernt
        /// ist — der Entscheider fuer das zurueckgeholte y. 0 heisst: er steht
        /// auf einem Gebaeude, und genau dort enden Bahnlinien.</summary>
        static int EndCost(Dictionary<(int, int), int> ground, int x, int y)
        {
            for (int r = 0; r <= 4; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) == r
                            && ground.ContainsKey((x + dx, y + dy)))
                            return r;
            return 99;
        }

        for (int i = 0; i + SpojStride <= s34.Length; i += SpojStride)
        {
            if (s34[i] == 0xFF || AllZero(s34, i, SpojStride)) continue;
            var d = new Link
            {
                Slot = i / SpojStride, Node1 = s34[i], Node2 = s34[i + 1],
                Bud1 = NodeBuilding(s33, s34[i]), Bud2 = NodeBuilding(s33, s34[i + 1]),
                X1 = s34[i + 2], X2 = s34[i + 4],
                Delka = s34[i + 0x0c], Faze = s34[i + 0xd5],
            };
            int rec = i / SpojStride;
            if (s122 != null && rec * 4 + 4 <= s122.Length)
            {
                d.Y1 = BitConverter.ToUInt16(s122, rec * 4);
                d.Y2 = BitConverter.ToUInt16(s122, rec * 4 + 2);
            }
            else
            {
                // ⭐ 11.08.2026 — DAS y STAND DIE GANZE ZEIT IN sec34.
                //
                // Der Kommentar oben sagte, die y kaemen aus sec122. Das stimmt
                // fuer einen SPIELSTAND — aber kein einziges der 49 Levelfiles
                // traegt sec122 (gezaehlt), und darum war auf jeder Kampagnen-
                // und NET-Karte weder Gleis noch Zug zu sehen.
                //
                // Gefunden ueber die Routine @0x4B0FE0, die sec34 fuellt: sie
                // liest `+0x03` und haelt `wert >> 1` gegen ein Feld des
                // Endgebaeudes — also ein y in HALBEN Zeilen, genau wie in
                // sec122. Die x liegen auf +0x02 und +0x04, die y schlicht
                // daneben auf +0x03 und +0x05.
                //
                // ⚠ BELEGT, nicht angenommen: `--selftest-rail` haelt beide
                // Quellen auf den drei Spielstaenden gegeneinander, wo es sec122
                // WIRKLICH gibt — **25 + 49 + 41 = 115 von 115 Linien stimmen
                // ueberein, in y1 wie in y2**. sec122 ist nur die Laufzeitkopie.
                //
                // ⚠ Und eine eigene Sackgasse davor, die der Pruefstand
                // abgeraeumt hat: ich hatte das y aus den beiden Endgebaeuden
                // zurueckrechnen wollen. Der Pruefstand meldete **0 von 115
                // eindeutig** — zu Recht, denn der Streckenendpunkt liegt meist
                // gar nicht auf einem Gebaeude (nur 164 von 542 Paaren). Ohne
                // ihn waere ein falsches Gleis eingebaut worden, das auf einer
                // Kampagnenkarte mit nichts vergleichbar gewesen waere.
                // ⚠ 11.08.2026, NACHGEBESSERT. +0x03 und +0x05 sind BYTES, das
                // y zaehlt aber HALBE Zeilen — auf einer Karte ueber 128 Zeilen
                // laeuft es ueber. Gemessen an den 609 Linien: der x-Lauf
                // landet 609 mal exakt auf dem gespeicherten x2, das y aber nur
                // 580 mal; 20 Linien liegen um +256 daneben und 9 um -256.
                //
                // (Mein eigener Pruefstand hatte das verdeckt, weil er mit
                // `& 0xFF` verglich — er konnte eine Abschneidung gar nicht
                // sehen. Die »115 von 115« waren in Wahrheit 115 modulo 256.)
                //
                // Zwei Schluesse daraus:
                //   * y2 wird NICHT mehr gelesen, sondern gelaufen: es ist
                //     y1 + Summe der Hoehenschritte, und der Lauf ist auf der
                //     x-Seite 609 von 609 exakt.
                //   * y1 wird aus dem Byte ZURUECKGEHOLT: der wahre Wert ist
                //     b, b+256 oder b+512, und genommen wird der, bei dem die
                //     GANZE Route innerhalb der Karte bleibt. Bleibt mehr als
                //     einer moeglich, gilt das Byte — lieber die alte Zahl als
                //     eine geratene.
                int by = s34[i + 3];
                int sum = SumDy(s34, i, d.Delka);
                int lo = 0, hi = 0, run = 0;
                for (int k = 0; k < d.Delka && i + 0x0d + k < i + SpojStride; k++)
                {
                    run += SpojStep[s34[i + 0x0d + k] & 15].Dy;
                    if (run < lo) lo = run;
                    if (run > hi) hi = run;
                }
                int limit = m.Height * 2;
                // ⚠ NACHGEBESSERT am selben Tag: »passt in die Karte« allein
                // reicht nicht. Auf einer 254 Zeilen hohen Karte passen b UND
                // b+256, und dann blieb es beim Byte — gemessen an 1218
                // Endpunkt/Gebaeude-Paaren lagen 411 um rund 126 Zeilen daneben,
                // also genau um die Sprungmarke. Der bessere Entscheider ist das
                // ENDGEBAEUDE: die Strecke faengt an einem an und hoert am
                // anderen auf. Genommen wird der Kandidat, dessen beide Enden
                // ihren Gebaeuden am naechsten liegen.
                int best = by, bestCost = int.MaxValue;
                for (int add = 0; add <= 512; add += 256)
                {
                    int cand = by + add;
                    if (cand + lo < 0 || cand + hi > limit) continue;
                    int cost = EndCost(at, d.X1, cand / 2) + EndCost(at, d.X2, (cand + sum) / 2);
                    if (cost < bestCost) { bestCost = cost; best = cand; }
                }
                d.Y1 = best;
                d.Y2 = d.Y1 + sum;
                d.Rebuilt = true;
            }
            if (d.Y1.HasValue && at.Count > 0)
            {
                d.End1 = BuildingAt(d.X1, d.Y1.Value / 2);
                d.End2 = BuildingAt(d.X2, d.Y2!.Value / 2);
            }
            if (d.Y1.HasValue)
            {
                // x is whole columns, y half rows, so a point may sit on a half
                // row but never on a half column
                int hx = d.X1, hy = d.Y1.Value;
                d.Route = new List<(int, double)> { (hx, hy / 2.0) };
                d.Pieces = new List<int> { d.Delka != 0 ? SpojPiece[s34[i + 0x0d] & 15] : 0 };
                for (int k = 0; k < d.Delka && i + 0x0d + k < i + SpojStride; k++)
                {
                    int c = s34[i + 0x0d + k] & 15;
                    hx += SpojStep[c].Dx;
                    hy += SpojStep[c].Dy;
                    d.Route.Add((hx, hy / 2.0));
                    d.Pieces.Add(SpojPiece[c]);
                }
            }
            list.Add(d);
        }
        return list;
    }

    private static int NodeBuilding(byte[] s33, int node)
        => node * 8 + 2 <= s33.Length ? BitConverter.ToUInt16(s33, node * 8) : 0;

    /// <summary>Die Summe der Höhenschritte einer Route, in HALBEN Zeilen.</summary>
    private static int SumDy(byte[] s34, int at, int delka)
    {
        int sum = 0;
        for (int k = 0; k < delka && at + 0x0d + k < at + SpojStride; k++)
            sum += SpojStep[s34[at + 0x0d + k] & 15].Dy;
        return sum;
    }

    /// <summary>Der x-Lauf einer Route — unabhängig von y, darum die Probe.</summary>
    private static int EndX(byte[] s34, int at, int delka, int x1)
    {
        int x = x1;
        for (int k = 0; k < delka && at + 0x0d + k < at + SpojStride; k++)
            x += SpojStep[s34[at + 0x0d + k] & 15].Dx;
        return x;
    }

    /// <summary>
    /// Das Start-y einer Bahnlinie, wenn sec122 fehlt — und ob es eindeutig ist.
    ///
    /// <para>Gesucht wird ein <c>y1</c> in halben Zeilen, sodass
    /// <c>(x1, y1/2)</c> auf dem Grund des Gebäudes von node1 steht und
    /// <c>(x2, (y1+Σdy)/2)</c> auf dem des Gebäudes von node2. Beide Fußabdrücke
    /// kommen aus dem Belegungsgitter, sind also gemessen und nicht geraten.</para>
    ///
    /// <para>⚠ <b>Eindeutig oder gar nicht.</b> Ein Gebäude ist zwei Zeilen hoch,
    /// also vier halbe — die Startbedingung allein lässt mehrere Werte zu. Erst
    /// zusammen mit dem starren Abstand zum Ende bleibt meist einer übrig. Bleibt
    /// mehr als einer, wird die Linie <b>nicht</b> gelegt: lieber eine sichtbare
    /// Lücke als ein Gleis, das anderswo verläuft als im Original.</para>
    ///
    /// <para>Der x-Lauf ist unabhängig von y und dient als Vorprobe: endet er
    /// nicht auf dem gespeicherten x2, sind die Richtungscodes nicht die, für
    /// die wir sie halten, und es wird gar nichts gelegt.</para>
    /// </summary>
    private static (int Y1, bool Sure) SolveStartY(
        byte[] s34, int at, Link d,
        Dictionary<(int, int), int> ground, Func<int, int, int> buildingAt)
    {
        if (d.Delka <= 0) return (0, false);
        if (EndX(s34, at, d.Delka, d.X1) != d.X2) return (0, false);   // Vorprobe
        int dy = SumDy(s34, at, d.Delka);

        int found = 0, first = 0;
        // halbe Zeilen; 254 Zeilen ist die größte Karte
        for (int y = 0; y <= 254 * 2; y++)
        {
            if (buildingAt(d.X1, y / 2) != d.Bud1) continue;
            int y2 = y + dy;
            if (y2 < 0) continue;
            if (buildingAt(d.X2, y2 / 2) != d.Bud2) continue;
            if (found++ == 0) first = y;
            if (found > 1) return (0, false);
        }
        return found == 1 ? (first, true) : (0, false);
    }

    // ---- sec44 + sec121: the trains -----------------------------------------

    /// <summary>sec44 — the wagons, 240 x 24 (dest 0xb95f48); sec121 — their y,
    /// one u16 each (dest 0xb95d60). The stride comes out of the tick, which
    /// indexes a wagon as `train*3*8` (@0x4c64ae); the 240 are 60 trains of 4
    /// wagons, wagon major, so wagon w of train t is record w*60 + t.
    ///
    /// The pair is a position split the way the rail lines are — x with the
    /// record, y in a section of its own. The spawn code writes the line's end
    /// point into a fresh wagon, +0x00 = +0x06 = x2 and sec121[i] = y2, then
    /// +0x01 = y2/2 (@0x4c674a). Verified over the three .DM missions:
    /// +0x01 == sec121/2 on 385 of 385 wagons.
    ///
    /// The tick splits the slot with `div 60` (@0x4c6c06) — the remainder is
    /// the train AND the SPOJ line it runs on, the quotient the wagon number
    /// (0 leads). There is no line field in the record; line == train.
    ///
    /// <para><b>The wagons that sit on no line, counted 2026-08-01.</b> Of the
    /// 770 wagons exported across the six maps that have any, 150 name a line
    /// that is not in the line list, and they split cleanly in two:</para>
    /// <list type="bullet">
    /// <item>72 are empty slots — position (0,0), all of them in 1.DM. They are
    /// records that were never filled, and only the reader ever called them
    /// wagons.</item>
    /// <item>80 are real wagons in 4.DM standing at real coordinates, on lines
    /// 24..33. Those lines exist as records but every header field is zero
    /// (node1, node2, x1, x2, delka) — they were cleared, and the wagons that
    /// ran on them were left behind. Read out of the file, not inferred.</item>
    /// </list>
    /// <para>So neither group is a reading error, and neither is drawn: a wagon
    /// without a route has nothing to be drawn along.</para></summary>
    /// <summary>
    /// <b>DIE FEINLAGE — +0x02 und +0x04, gelesen am 14.08.2026.</b>
    ///
    /// <para>Beide sind <b>vorzeichenbehaftete Worte</b> und zusammen der
    /// Versatz des Waggons INNERHALB seiner Zelle, in Bildpunkten. Sie waren
    /// die letzte unbenannte Stelle des Satzes.</para>
    ///
    /// <para><b>Wie sie gesetzt werden</b> (@0x4C6A64..0x4C6AAD, und wortgleich
    /// noch einmal @0x4C6E10..0x4C6E4D):</para>
    /// <code>
    ///   eax = (short) sec121[i]     ; die Y-Koordinate in HALBZEILEN
    ///   eax = eax &amp; 1           ; ihre PARITAET  (@0x4C6A71, mit Vorzeichen)
    ///   ungerade -> +0x02 := 0    +0x04 := 10
    ///   gerade   -> +0x02 := 20   +0x04 := 0
    /// </code>
    /// <para>20 ist die halbe Kachelbreite, 10 die halbe Kachelhoehe. Der Waggon
    /// sitzt also auf der Mitte einer ZELLKANTE, und welche Kante es ist,
    /// entscheidet allein die Parität der Halbzeile.</para>
    ///
    /// <para>⚠ <b>Was daran neu ist — und was nicht.</b> Die Paritaetsregel war
    /// schon einmal ERSCHLOSSEN: eine fruehere Sitzung hat sie aus den zwoelf
    /// Routencodes zurueckgerechnet (»zwoelf unabhaengige Gleichungen, alle
    /// erfuellt«). Neu ist, dass sie jetzt im Code STEHT statt zu passen, und
    /// dass beide Zahlen einen Namen haben — der Handoff fuehrte sie bis heute
    /// als »sec44 +0x02/+0x04 nicht entschluesselt«.</para>
    ///
    /// <para>⚠ <b>Und was das fuer die blockierten Randmitten heisst.</b> Die
    /// Sperre lautete: »Gleis und Zug laufen auf zwei Strukturen, die nur zu
    /// 84 % uebereinstimmen — jede sechste Zelle haette unbekannte
    /// Halbzeilenparitaet.« Das gilt fuer die Paritaet einer GLEISZELLE, die man
    /// aus der Kette ableiten muesste. Fuer den WAGGON gilt es nicht: er traegt
    /// seine eigene Halbzeile in sec121 bei sich, und genau die fragt der
    /// Fahrcode ab. Wer die Waggons auf die Randmitten legen will, muss die
    /// 84 %-Luecke also gar nicht schliessen.</para>
    ///
    /// <para>⚠ <b>NACHTRAG vom selben Tag, und er berichtigt mich:</b> hier
    /// stand »ungebaut, der Einbau ist die Entscheidung des Spielers«. Unsere
    /// Waggons standen zu diesem Zeitpunkt laengst auf den Randmitten —
    /// gefehlt hat der Zaehler, der es zeigt. Auf map_NET02/05/08 liegt
    /// <b>0 von 1193 / 1000 / 1305</b> Wegknoten auf der Zellmitte. Was diese
    /// Lesart wirklich beigetragen hat, ist die GEGENPROBE: die Regel des
    /// Originals laesst sich jetzt gegen unsere Ableitung aus der Nachbarzelle
    /// halten, und auf den Zellen, ueber die sie ueberhaupt etwas sagen kann,
    /// stimmen sie <b>1271 zu 0</b> ueberein.
    ///
    /// <para>⚠ <b>Hier stand »88 bis 93 Prozent« — zurueckgezogen.</b> Ich hatte
    /// den Rest der bekannten 84-%-Luecke zwischen Gleis- und Zugstruktur
    /// zugeschrieben. Falsch: nach Bildart getrennt sitzt JEDE Abweichung auf
    /// einem Eckstueck (gerade 402:0 / 473:0 / 396:0, Rampe 78:0 / 46:0 / 116:0,
    /// Ecke 192:88 / 155:66 / 353:54). Ein Eckstueck hat je einen Anschluss auf
    /// beiden Gittern, eine Tafel mit einer Paritaet je Zelle kann dort nicht
    /// beide treffen — es war die Buchfuehrung. Eine Quote, die man nicht nach
    /// der Ursache aufgeschluesselt hat, ist keine Messung.</para>
    ///
    /// <para>Siehe <c>MapEntityLayer._railHalfOfCell</c>.</para>
    ///
    /// <para><b>Gemessen</b> ueber alle 1199 Waggons mit Rohsatz: 162 stehen auf
    /// einem der zwei Ausgangspaare, und davon folgen <b>161</b> der Regel —
    /// ein Gegenbeispiel. Die uebrigen 1037 sind Zwischenstaende einer Fahrt:
    /// fast alle tragen <c>dy = 10</c> und ein <c>dx</c> in Achterschritten
    /// (−32, −24, −8, 0, 8, 32), also im Takt des Abzugs +0x0c = 8. Die zwoelf
    /// Schreibstellen teilen sich damit in »zuruecksetzen« (die zwei oben) und
    /// »fortschreiben« (der Rest, je nach Gleisstueck).</para>
    /// </summary>
    public sealed class Wagon
    {
        public int Slot, Train, WagonNo, Line, Step, Piece, Col, Col2, Row, YHalf, Speed;

        /// <summary>+0x02 / +0x04 — der Versatz in der Zelle, in Bildpunkten,
        /// vorzeichenbehaftet. Siehe den Kommentar ueber dieser Klasse.</summary>
        public int FineX, FineY;

        public byte[] Raw = Array.Empty<byte>();
    }

    public static List<Wagon> Trains(CwmFile m)
    {
        var list = new List<Wagon>();
        var s44 = m.Sec(44);
        var s121 = m.Sec(121);
        if (s44 == null || s121 == null) return list;
        int n = Math.Min(s44.Length / TrainStride, s121.Length / 2);
        for (int i = 0; i < n; i++)
        {
            int o = i * TrainStride;
            if (AllZero(s44, o, TrainStride)) continue;
            list.Add(new Wagon
            {
                Slot = i, Train = i % 60, WagonNo = i / 60, Line = i % 60,
                Step = s44[o + 0x0a],     // cursor into the code list (@0x4c6c30)
                Piece = s44[o + 0x0b],    // rail piece, SpojPiece[code] (@0x4c6de9)
                Col = s44[o + 0x06],      // live column, whole tiles
                Col2 = s44[o + 0x00],     // second copy, equal at spawn
                Row = s44[o + 0x01],      // == y_half / 2 on 385 of 385
                YHalf = BitConverter.ToUInt16(s121, i * 2),
                Speed = s44[o + 0x0c],    // decrement per tick (@0x4c6a62)
                // Die Feinlage in Bildpunkten, VORZEICHENBEHAFTET — Begruendung
                // und Messung im Kommentar ueber der Klasse Wagon.
                FineX = BitConverter.ToInt16(s44, o + 0x02),
                FineY = BitConverter.ToInt16(s44, o + 0x04),
                Raw = Slice(s44, o, TrainStride),
            });
        }
        return list;
    }

    // ---- sec22: DAS GLEIS ----------------------------------------------------

    /// <summary>
    /// <b>Ein Stück Gleis, wie die Karte es selbst führt.</b>
    ///
    /// <para>⚠ <b>13.08.2026 — und es war die ganze Zeit da.</b> Bis heute haben
    /// wir die Strecke aus den Streckencodes der Linie (sec34 ab +0x0D)
    /// NACHGEBAUT und ihre Form aus den Nachbarzellen erschlossen. Das war eine
    /// Konstruktion. Die Karte nennt jede Gleiszelle einzeln, mit ihrem Bild:
    /// <b>sec22, 15000 B = 3000 Sätze zu 5 Byte, Ziel 0xc2c220</b>
    /// (Zieltabelle des Laders, Eintrag 21).</para>
    ///
    /// <code>
    ///   +0x00  Spalte                     (byte)
    ///   +0x01  Zeile                      (byte)
    ///   +0x02  BILD                       0..9 heil, +20·k Stützenart,
    ///                                     &gt;=100 zerstört, 255 = leerer Platz
    ///   +0x03  Trefferpunkte              rail_add @0x4AFA90 setzt 0x96 = 150
    ///   +0x04  Liniennummer               (der SPOJ-Satz, zu dem das Stück gehört)
    /// </code>
    ///
    /// <para><b>Gelesen, nicht gedeutet:</b> <c>rail_add</c> @0x4AFA90 sucht den
    /// ersten Platz mit Bild 0xFF und schreibt genau diese fünf Byte;
    /// <c>rail_at</c> @0x4B0E70 nennt eine Zelle Gleis, wenn ihr Bild &lt; 20
    /// ist; <c>rail_broken</c> @0x4B0A00 gibt <c>bild &gt;= 100</c> zurück;
    /// <c>rail_find_broken</c> @0x4B0DF0 sucht das erste Stück mit
    /// <c>100 &lt;= bild &lt; 255</c> und passendem +0x04; alle vier laufen über
    /// <b>3000</b> Plätze (<c>cmp si, 0xbb8</c>).</para>
    ///
    /// <para><b>Was das Bild bedeutet</b> — an 9846 Gleiszellen aller Karten
    /// gegen die Nachbarzellen und gegen das Geländebyte +3 gehalten:</para>
    /// <code>
    ///   0  L–R  (1752 von 2069 haben genau links und rechts Gleis)
    ///   1  T–B  (2213 von 2477)
    ///   2  R–B  (850 von 1055)      3  R–T  (843 von 958)
    ///   4  L–B  (919 von 1161)      5  L–T  (824 von 1089)
    ///   6  L–R  auf Geländebyte 3   147 von 147   RAMPE, links höher
    ///   7  L–R  auf Geländebyte 1   170 von 170   RAMPE, rechts höher
    ///   8  T–B  auf Geländebyte 4   180 von 180   RAMPE, oben höher
    ///   9  T–B  auf Geländebyte 2   118 von 118   RAMPE, unten höher
    /// </code>
    /// <para>Die vier Rampenbilder sitzen also <b>ausnahmslos</b> auf einer
    /// Zelle, deren Geländebyte +3 die passende Stufe nennt — das ist die
    /// Antwort auf »wo werden Höhe und Winkel gesetzt«: im Gelände, und die
    /// Karte hat das Gleis danach ausgesucht.</para>
    ///
    /// <para><b>Zerstörbar: ja.</b> <c>rail_hit</c> @0x4B0460 lässt Rauch und
    /// Splitter los und rechnet <c>bild += (10 + zufall&amp;1)·10</c>, also +100
    /// oder +110 — genau die Werte, die <c>4.DM</c> gespeichert hat (Bilder
    /// 100..117 an 45 Zellen) neben Trefferpunkten von 1 bis 135. Ein
    /// Spielstand hält den Schaden also fest.</para>
    ///
    /// <para><b>IST DIE STRECKE ZERSTÖRBAR? JA, und zwar durch Beschuss.</b>
    /// Die Einschlagsroutine @0x40D799 läuft über alle 3000 Plätze, vergleicht
    /// Spalte und Zeile des Stücks mit dem Einschlagsort
    /// (<c>[0x53C930]</c>/<c>[0x53C934]</c>) und hält dann die
    /// <b>Trefferpunkte +0x03</b> gegen den Schaden: reichen sie nicht,
    /// <c>rail_hit(platz, 1)</c> @0x40D7E2 — sonst <c>tp -= schaden</c>
    /// (@0x40D7EC). Ein Gleisstück startet mit <b>150</b> Trefferpunkten
    /// (<c>mov byte …, 0x96</c> in <c>rail_add</c> @0x4AFADA). <b>Eine ganze
    /// Linie fällt damit nicht aus</b> — beschädigt wird immer nur die eine
    /// getroffene ZELLE; der Zug prüft sie beim Darüberfahren
    /// (<c>rail_broken</c> @0x4C6A12).</para>
    ///
    /// <para><b>KANN MAN SIE REPARIEREN? JA.</b> Ein Fahrzeug tut es, und der
    /// Weg steht ganz im Spezialteil-Verteiler: @0x4099B6 zählt seinen
    /// Arbeitszähler <c>byte[fahrzeug+0x38]</c> herunter und bleibt oberhalb
    /// von 10 unfertig (<c>cmp al,0xa / ja</c>). Unter 10 ist das Stück
    /// wiederhergestellt: <c>bild := bild % 10</c> (@0x4099E7, <c>div 10</c> —
    /// die Hunderter des Schadens fallen weg, das Grundbild bleibt), dazu ein
    /// Effekt 0x2d an der Stelle (@0x409A04) und ein Durchlauf von
    /// <c>rail_pylon_pass</c> @0x409A0C, damit die Stützen der Nachbarn wieder
    /// stimmen. Danach sucht das Fahrzeug SELBST weiter: erst das nächste
    /// kaputte Stück derselben Linie (@0x409A2D), sonst über
    /// <c>rail_find_broken</c> @0x409A5E irgendeines mit derselben
    /// Liniennummer (+0x04). <b>Die Trefferpunkte setzt diese Stelle NICHT
    /// zurück</b> — gelesen, nicht vermutet.</para>
    ///
    /// <para><b>NEU GEBAUT WERDEN KANN SIE NICHT.</b> <c>rail_add</c> @0x4AFA90
    /// hat im ganzen Programm <b>eine</b> Aufrufstelle (@0x4AF8F2), und die
    /// steht in einem Durchlauf über die ganze Karte (@0x4AF4C0, Schleife über
    /// <c>[0x542DF8]</c>×<c>[0x542DC4]</c> = Höhe×Breite), der seinerseits nur
    /// einmal gerufen wird. Es gibt keinen Befehl und keine Zeigerlogik, die
    /// ein einzelnes Stück Gleis setzt. <b>Die Strecke liegt fest; man kann sie
    /// kaputtschießen und wieder flicken, aber nicht verlängern.</b>
    /// (Die Meldung »Can't add built bridge« @0x539AF0 gehört nicht hierher:
    /// sie hängt an @0x4D1070 und an der Tabelle 0xB36B20, nicht am
    /// Gleisfeld 0xC2C220.)</para>
    ///
    /// <para>⚠ Was davon in unserem Spiel steht: <b>nichts.</b> Wir zeichnen
    /// den Schaden, den ein Spielstand mitbringt (indem wir das Stück
    /// weglassen), aber wir beschädigen und reparieren nicht. Das ist eine
    /// LÜCKE von uns, keine des Originals.</para>
    /// </summary>
    public sealed class RailCell
    {
        /// <summary>Platznummer im Feld — sie entscheidet über die Stütze:
        /// der Zeichner nimmt Teil 65 (mit Bock) genau dann, wenn
        /// <c>platz % 6 == 0</c> ist (@0x42D4B1..0x42D4C2), sonst Teil 64.</summary>
        public int Index;
        public int Col, Row, Frame, Hp, Line;

        /// <summary>Das Grundbild 0..9 ohne die Stützenart (<c>bild % 10</c>) —
        /// so rechnet auch das Original, wenn es die Stütze neu wählt
        /// (@0x4B037A: <c>div 10</c>, der REST ist das Grundbild).</summary>
        public int Base => Frame >= 100 ? -1 : Frame % 10;

        /// <summary>true = dieses Stück ist zerschossen (Bild &gt;= 100,
        /// <c>rail_broken</c> @0x4B0A3B).</summary>
        public bool Broken => Frame >= 100;
    }

    public const int RailStride = 5, RailSlots = 3000;

    /// <summary>sec22 auspacken. Ein Platz mit Bild 255 ist leer.</summary>
    public static List<RailCell> RailCells(CwmFile m)
    {
        var list = new List<RailCell>();
        var s = m.Sec(22);
        if (s == null) return list;
        int n = Math.Min(RailSlots, s.Length / RailStride);
        for (int i = 0; i < n; i++)
        {
            int o = i * RailStride;
            if (s[o + 2] == 0xFF) continue;
            list.Add(new RailCell
            {
                Index = i, Col = s[o], Row = s[o + 1],
                Frame = s[o + 2], Hp = s[o + 3], Line = s[o + 4],
            });
        }
        return list;
    }

    // ---- sec119 / sec120: what a player may build ---------------------------

    /// <summary>sec120 — the buildable aircraft per player (dest 0x51b020),
    /// 8 blocks of 20 records of 48. The record is the aircraft template the
    /// spawn routine copies from, shifted by one because the section starts a
    /// byte earlier — and that byte is the enable flag.</summary>
    public sealed class AirDesign
    {
        public int Slot, Player, Index, Enable;
        public string Name = "", Short = "";
        public int Speed, Hp, Payload, Airframe, Attack, Defence, Sight, Ammo, Fuel;

        /// <summary>Preis in Waffen- / Fahrwerk- / Spezialteilen, Satz
        /// +0x1F/+0x20/+0x21. GELESEN: `build_in_airport` @0x4BB3D0 haelt genau
        /// diese drei Bytes (`0x51B03F/40/41` gegen die Basis 0x51B020) gegen
        /// die drei Lager des Flughafens (Gebaeudesatz +0x28/+0x2a/+0x2c).
        /// Vorher standen sie in der KI fest im Code — das war eine Setzung.</summary>
        public int CostW, CostF, CostS;
    }

    public static List<AirDesign> AirDesigns(CwmFile m)
    {
        var list = new List<AirDesign>();
        var s = m.Sec(120);
        if (s == null) return list;
        for (int i = 0; i + 48 <= s.Length; i += 48)
        {
            string name = Cp437.GetString(s, i + 1, 0x15);
            if (name.Length == 0) continue;
            list.Add(new AirDesign
            {
                Slot = i / 48, Player = i / 48 / 20, Index = i / 48 % 20,
                Enable = s[i], Name = name, Short = Cp437.GetString(s, i + 0x16, 12),
                Speed = s[i + 0x22], Hp = s[i + 0x23], Payload = s[i + 0x24],
                Airframe = s[i + 0x25], Attack = s[i + 0x26], Defence = s[i + 0x27],
                Sight = s[i + 0x28], Ammo = s[i + 0x29],
                CostW = s[i + 0x1f], CostF = s[i + 0x20], CostS = s[i + 0x21],
                Fuel = BitConverter.ToUInt16(s, i + 0x2c),
            });
        }
        return list;
    }

    /// <summary>sec119 — the buildable ships per player (dest 0x52eda0), 8
    /// blocks of 10 records of 42: the SHIP_PROD table the game dumps under
    /// that very name (@0x4137b6). The 23 .CWM levels stop at sec39 and carry
    /// no table of their own — those maps run on the exe's default, which
    /// <see cref="ExeTables"/> reads.</summary>
    public sealed class ShipDesign
    {
        public int Enable, Weapon, Chassis, Variant, CostW, CostF, CostS;
        public int Speed, Energie, Attack, Defence, Range1, Range2, Sight, Ammo;
        public int Fuel, Reload, Slot, Player, Index;
        public string Name = "";
    }

    public static List<ShipDesign> ShipDesigns(CwmFile m)
    {
        var list = new List<ShipDesign>();
        var s = m.Sec(119);
        if (s == null) return list;
        for (int i = 0; i + ShipStride <= s.Length; i += ShipStride)
        {
            string name = Cp437.GetString(s, i + 0x01, 0x15);
            if (name.Length == 0) continue;
            int slot = i / ShipStride;
            list.Add(new ShipDesign
            {
                Enable = s[i], Name = name,
                Weapon = s[i + 0x16], Chassis = s[i + 0x17], Variant = s[i + 0x18],
                CostW = s[i + 0x19], CostF = s[i + 0x1a], CostS = s[i + 0x1b],
                Speed = s[i + 0x1c], Energie = s[i + 0x1d],
                Attack = s[i + 0x1e], Defence = s[i + 0x1f],
                Range1 = BitConverter.ToUInt16(s, i + 0x20),
                Range2 = BitConverter.ToInt16(s, i + 0x22),
                Sight = s[i + 0x24], Ammo = s[i + 0x25],
                Fuel = BitConverter.ToUInt16(s, i + 0x26),
                Reload = s[i + 0x28],
                Slot = slot, Player = slot / 10, Index = slot % 10,
            });
        }
        return list;
    }

    // ---- helpers ------------------------------------------------------------

    internal static bool AllZero(byte[] b, int at, int len)
    {
        for (int i = at; i < at + len && i < b.Length; i++) if (b[i] != 0) return false;
        return true;
    }

    internal static byte[] Slice(byte[] b, int at, int len)
    {
        var r = new byte[len];
        Array.Copy(b, at, r, 0, Math.Min(len, b.Length - at));
        return r;
    }

    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
    }
}
