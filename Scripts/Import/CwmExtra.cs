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
        public bool Stored;
        public string Name = "";
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
                int fits = 0, chosen = by;
                for (int add = 0; add <= 512; add += 256)
                {
                    int cand = by + add;
                    if (cand + lo < 0 || cand + hi > limit) continue;
                    if (fits++ == 0) chosen = cand;
                }
                d.Y1 = fits == 1 ? chosen : by;
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
    public sealed class Wagon
    {
        public int Slot, Train, WagonNo, Line, Step, Piece, Col, Col2, Row, YHalf, Speed;
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
                Raw = Slice(s44, o, TrainStride),
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
