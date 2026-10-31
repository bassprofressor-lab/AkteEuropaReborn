namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;

/// <summary>
/// ⭐ <b>DIE GASWOLKE WIE IM ORIGINAL</b> (30.10.2026, bug-708 und bug-715,
/// berichte/bau-gaswolke.md). Ergaenzt <c>Simulation/Gaswerfer.cs</c> (11.09.2026) um
/// das, was dort als Setzung oder Luecke stand.
///
/// <para><b>Gelesen, C und F gleich</b> (Befehlsfolge je Funktion verglichen; der
/// Gassauger weicht nur in der Registerwahl ab):</para>
/// <code>
///   0x439410 / F 0x438570  add_gas(x, y, vx, vy, alter) — Rufer ueber Thunk 0x4018D9:
///            Werfertakt 0x439C50, Tod 0x40B7BE, Gasbombe 0x45569F, Entwicklertaste
///            0x43AB44 (50 am Zeiger, nicht gebaut), Aufbau M20 6 / M21 12 / M25 17,
///            Takt M21 @0x49F62B..0x49F71E (10, Regel @0x49F618: Objekt 13 ohne Besitzer)
///   0x4396C0 / F 0x438820  »Check gas«, je Teilchen nach der Reibung:
///     Wind  wenn byte[0x4F8D6C] > 1: X += ((rand%3 + Staerke)·Wind[Richtung].dx)/150,
///           dann Y ebenso (zweiter Wurf); Windtafel = Richtungstafel 0x4FA560
///     Hang  a = byte +3 der ALTEN Zelle (0x41D110), Tafel 0x4FA580 (F 0x4F9588, gleich):
///           X += dx/50 − rand%3 + 1, dann Y += dy/50 − rand%3 + 1
///     Ausduennen 0x439500 mit der ALTEN Zelle (@0x4399F1 [esp+0x12/0x14])
///   0x4395D0  Gebaeude = imap 60000..60299, also nur die echten Gebaeudezellen
///   0x42FAA0  Zeichenliste Art 14: nur sichtbare Zellen (sec50 an (cx, cy+1)),
///             feinX = (X%120)/3, feinY = 20 + (Y%120)/(−6), Hub(cx, cy+1, feinX, feinY)
///   0x42B6E4  Zeichner Art 14: Bild = Folge 69 (word[0x7A415E]) + (Takt + Platz) % 20,
///             Blitter 0x4AC830 — kopiert KEINE Bildpunkte, schickt den Untergrund durch
///             die Tafel 0xB136B8 = NN.CWG (@0x4B5829, Name ».cwg« @0x4B54A8)
/// </code>
///
/// <para><b>Keine Wirkung auf Sicht und Nebel</b>: 0x77CAE8 und die Dichtekarte
/// 0x81AA30 haben ausser den Gasroutinen, dem Zeichner und dem Spielstand (0x41D9A1,
/// 0x41EA26, 0x41F0F1) keinen Leser (Rohsuche, 12 bzw. 6 Stellen). Fussvolk (Klasse 1)
/// bleibt unberuehrt, Flieger stehen nicht in der imap.</para>
///
/// <para>⚠ UNSERE SETZUNGEN: der Untergrund ist bei uns RGB, eine Palettentafel laesst sich
/// nicht anwenden — das Bild der Folge 69 wird mit dem Mittel der CWG-Zielfarben
/// (143,112,163) zu 60 % gemischt (aus 01.CWG/01.PAL, Mischungsanpassung, Restfehler 59 von
/// 255 — schlecht, ehrlich benannt); gezeichnet wird das Bild oben links wie bei den Minen.
/// Ohne <c>Effects/gas</c> (alter Datenstand) bleibt das Rauchbild.</para>
///
/// <para>Gegenschalter <c>--gaswolke-aus</c> (der Stand bis 30.10.: kein Aufbaugas, kein
/// Skriptgas, kein Wind, kein Hang, Ausduennen an der neuen Zelle, Gebaeude als Rechteck,
/// Rauchbild) und <c>--gas-spielstand-alt</c> (bug-715: die Teilchen nicht im
/// Spielstand). Pruefstand <c>--gaswolke-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--gaswolke-aus</c> — bug-708, der Stand bis 30.10.2026.</summary>
    public static bool GaswolkeAus;

    /// <summary><c>--gas-spielstand-alt</c> — bug-715, der Stand bis 30.10.2026.</summary>
    public static bool GasSpielstandAlt;

    /// <summary>Die Hangtafel 0x4FA580 (F 0x4F9588), 19 Arten × (dx, dy) — ausgelesen,
    /// C = F.</summary>
    private static readonly (int Dx, int Dy)[] GasHangTafel =
    {
        (0, 0), (-100, 0), (0, -100), (100, 0), (0, 100), (-71, -71), (71, -71), (71, 71), (-71, 71),
        (-71, -71), (71, -71), (71, 71), (-71, 71), (0, 0), (0, 0), (142, 142), (-142, 142), (-142, -142), (142, -142),
    };

    /// <summary>@0x43983E..0x43994C — Wind, dann Hang; <paramref name="cx"/>/<paramref name="cy"/>
    /// ist die Zelle VOR der Bewegung.</summary>
    private void GasWindUndHang(ref GasTeilchen g, int cx, int cy)
    {
        if (WindDir >= 0 && WindStrength > 1)                                   // @0x439843 jbe
        {
            var (wx, wy) = UrRichtung[WindDir & 7];
            g.X += (Simulation.Determinism.Roll(3) + WindStrength) * wx / 150;
            g.Y += (Simulation.Determinism.Roll(3) + WindStrength) * wy / 150;
        }
        int a = HangArt(cx, cy);
        var (hx, hy) = a >= 0 && a < GasHangTafel.Length ? GasHangTafel[a] : (0, 0);
        g.X += hx / 50 - Simulation.Determinism.Roll(3) + 1;
        g.Y += hy / 50 - Simulation.Determinism.Roll(3) + 1;
    }

    // ---- die Aufbauwolken und das Skriptgas ----------------------------------------

    /// <summary>Der Stand hat die Teilchen gesetzt — der Aufbau legt keine (bug-715).</summary>
    private bool _gasStandGeladen;

    /// <summary>Wie viele der Aufbau gelegt hat, und das Bild direkt danach (Pruefstand).</summary>
    public int GasAufbau = -1, GasSkript;
    private readonly List<(int Platz, int X, int Y, int Vx, int Vy, int Alter)> _gasAufbauBild = new();

    /// <summary>Aus dem Skriptstart, nach den Fallen (M25: lay_trap @0x48F4BE.., add_gas
    /// @0x48F525..).</summary>
    public void MissionsGasLegen(IReadOnlyList<int[]> gas)
    {
        _gasAufbauBild.Clear();
        if (_gasStandGeladen) { GD.Print($"Gas: aus dem Spielstand ({_gasAnzahl} Teilchen) — der Missionsaufbau legt keines"); return; }
        GasAufbau = 0;
        if (GaswolkeAus || gas.Count == 0) return;
        foreach (var q in gas)
        {
            if (q.Length < 5) continue;
            int vor = GasAngelegt;
            GasAnlegen(q[0], q[1], q[2], q[3], q[4]);
            if (GasAngelegt > vor) GasAufbau++;
        }
        for (int k = 0; k < GasPlaetze; k++)
            if (_gas[k].Belegt) _gasAufbauBild.Add((k, _gas[k].X, _gas[k].Y, _gas[k].Vx, _gas[k].Vy, _gas[k].Alter));
        GD.Print($"Gas: {GasAufbau} Teilchen aus {gas.Count} Rufen des Missionsaufbaus (add_gas 0x439410, bug-708)");
    }

    /// <summary>Die Wirkung <c>gas</c> einer Skriptregel (M21 @0x49F618).</summary>
    private void SkriptGas(int[][] gas)
    {
        if (GaswolkeAus) return;
        foreach (var q in gas)
            if (q.Length >= 5) { GasAnlegen(q[0], q[1], q[2], q[3], q[4]); GasSkript++; }
        GD.Print($"Gas: {gas.Length} Teilchen aus einer Skriptregel (bug-708)");
    }

    // ---- das Bild -------------------------------------------------------------------

    public int GasGezeichnet;

    /// <summary>0x42FAA0 + 0x42B6E4. false = nicht zustaendig (dann das alte Rauchbild).</summary>
    private bool GaswolkeZeichnen()
    {
        if (GaswolkeAus) return false;
        var frames = EffectFrames("gas");
        if (frames.Count == 0) return false;
        // ⭐ bug-723: die CWG-Tafel wie der Blitter 0x4AC830 (Rendering/GasBild.cs); die
        // Mischung darunter nur noch mit --gasbild-mischung oder ohne NN.CWG.
        if (GasbildCwg()) return true;
        if (GasbildVerbergen) return true;                                     // Bildprobe: Rahmen ohne Gas
        var haze = new Color(143 / 255f, 112 / 255f, 163 / 255f, 0.6f);   // UNSERE SETZUNG, siehe Kopf
        for (int k = 0; k < GasPlaetze; k++)
        {
            if (!_gas[k].Belegt) continue;
            var g = _gas[k];
            int cx = g.X / GasFein, cy = g.Y / GasFein + 1;
            if (FogActive && !Watched(cx, cy)) continue;                        // sec50 an (cx, cy+1)
            int feinX = (g.X % GasFein) / 3, feinY = 20 + (g.Y % GasFein) / -6;
            int hub = HubOf(cx, cy, feinX, feinY);
            float px = _ox + cx * TileW + feinX - 20;
            float py = _oy + cy * TileH - hub - feinY;
            int f = (_taktNr + k) % 20;                                         // @0x42B703
            if (f >= frames.Count) continue;
            DrawTexture(frames[f], new Vector2(px, py), haze);
            GasGezeichnet++;
        }
        return true;
    }

    // ---- der Spielstand (bug-715) ---------------------------------------------------

    /// <summary>Aus <c>SaveStateJson</c>: <c>gas</c> = [[platz, x, y, vx, vy, alter], ...]
    /// (sec82), <c>gaswerfer</c> = [[platz, zaehler, x, y, dx, dy], ...] (sec83).</summary>
    private void GasInSpielstand(Core.SaveGame.Writer w)
    {
        if (GasSpielstandAlt) return;
        var sb = new StringBuilder("[");
        bool erst = true;
        for (int k = 0; k < GasPlaetze; k++)
        {
            var g = _gas[k];
            if (!g.Belegt) continue;
            if (!erst) sb.Append(',');
            erst = false;
            sb.Append('[').Append(k).Append(',').Append(g.X).Append(',').Append(g.Y).Append(',')
              .Append(g.Vx).Append(',').Append(g.Vy).Append(',').Append(g.Alter).Append(']');
        }
        w.Raw("gas", sb.Append(']').ToString());
        sb.Clear().Append('[');
        erst = true;
        for (int k = 0; k < GasWerferPlaetze; k++)
        {
            var e = _gasWerfer[k];
            if (e.Zaehler == 0) continue;
            if (!erst) sb.Append(',');
            erst = false;
            sb.Append('[').Append(k).Append(',').Append(e.Zaehler).Append(',').Append(e.X).Append(',')
              .Append(e.Y).Append(',').Append(e.Dx).Append(',').Append(e.Dy).Append(']');
        }
        w.Raw("gaswerfer", sb.Append(']').ToString());
    }

    public static int GasGeladen = -1;

    /// <summary>Aus <c>ApplySaveState</c>. Ohne Schluessel (alter Stand) bleibt alles wie
    /// bisher — der Aufbau legt neu.</summary>
    private void GasAusSpielstand(GDict root)
    {
        GasGeladen = -1;
        if (GasSpielstandAlt) return;
        if (!root.TryGetValue("gas", out var v) || v.VariantType != Variant.Type.Array) return;
        System.Array.Clear(_gas);
        System.Array.Clear(_gasWerfer);
        _gasAnzahl = 0;
        foreach (var e in v.AsGodotArray())
        {
            if (e.VariantType != Variant.Type.Array) continue;
            var q = e.AsGodotArray();
            if (q.Count < 6) continue;
            int k = q[0].AsInt32();
            if (k < 0 || k >= GasPlaetze || _gas[k].Belegt) continue;
            _gas[k] = new GasTeilchen
            {
                Belegt = true, X = q[1].AsInt32(), Y = q[2].AsInt32(),
                Vx = unchecked((sbyte)q[3].AsInt32()), Vy = unchecked((sbyte)q[4].AsInt32()),
                Alter = (byte)System.Math.Clamp(q[5].AsInt32(), 1, 255),
            };
            _gasAnzahl++;
        }
        if (root.TryGetValue("gaswerfer", out var wv) && wv.VariantType == Variant.Type.Array)
            foreach (var e in wv.AsGodotArray())
            {
                if (e.VariantType != Variant.Type.Array) continue;
                var q = e.AsGodotArray();
                if (q.Count < 6) continue;
                int k = q[0].AsInt32();
                if (k < 0 || k >= GasWerferPlaetze) continue;
                _gasWerfer[k] = new GasWerferPlatz
                {
                    Zaehler = q[1].AsInt32(), X = q[2].AsInt32(), Y = q[3].AsInt32(), Dx = q[4].AsInt32(), Dy = q[5].AsInt32(),
                };
            }
        GasGeladen = _gasAnzahl;
        _gasStandGeladen = true;
        GD.Print($"Spielstand: {_gasAnzahl} Gasteilchen gelesen (bug-715)");
    }

    /// <summary>Ein Teil fuer <c>SpielstandFingerabdruck</c>.</summary>
    public string GasFingerabdruck()
    {
        long p = 0, pw = 0; int n = 0, nw = 0;
        for (int k = 0; k < GasPlaetze; k++)
        {
            var g = _gas[k];
            if (!g.Belegt) continue;
            n++;
            p += (k + 1) * 7L + g.X * 31L + g.Y * 37L + g.Vx * 41L + g.Vy * 43L + g.Alter * 3L;
        }
        for (int k = 0; k < GasWerferPlaetze; k++)
        {
            var w = _gasWerfer[k];
            if (w.Zaehler == 0) continue;
            nw++;
            pw += (k + 1) * 7L + w.Zaehler * 11L + w.X * 31L + w.Y * 37L + w.Dx * 41L + w.Dy * 43L;
        }
        return $"Gas {n} (Pruefzahl {p}), Gaswerfer-Wolken {nw} (Pruefzahl {pw}), Zaehler {_gasAnzahl}";
    }

    /// <summary>Fuer <c>SpielstandProbeVorbereiten</c>: eine Wolke, die verloren gehen KANN —
    /// drei Teilchen mit Tempo und eine laufende Werferwolke. ⚠ EINGRIFF.</summary>
    public string SpielstandProbeGas()
    {
        if (_nav == null) return "Gas: keine Karte";
        int x = (_nav.Width / 2) * GasFein + 37, y = (_nav.Height / 2) * GasFein + 53;
        GasAnlegen(x, y, 31, -17, 255);
        GasAnlegen(x + 5, y + 3, -12, 9, 250);
        GasAnlegen(x + 9, y - 4, 0, 0, 250);
        int k = System.Array.FindIndex(_gasWerfer, w => w.Zaehler == 0);
        if (k >= 0) _gasWerfer[k] = new GasWerferPlatz { Zaehler = 5, X = x, Y = y, Dx = 71, Dy = -71 };
        return $"Gas: 3 Teilchen bei ({x},{y}) und eine Werferwolke (Platz {k}) gelegt";
    }

    // ---- der Pruefstand ---------------------------------------------------------------

    /// <summary>
    /// <c>--gaswolke-check</c> — drei Teile:
    /// <list type="number">
    /// <item><b>Aufbau</b> (nur Kampagne): Zahl und Lage der Teilchen direkt nach dem Aufbau
    /// gegen das Missionsskript (M20 6, M21 12, M25 17; v = 0, Alter 255). M21: die
    /// Skriptregel traegt ihre zehn Teilchen. Gefecht: die Gaswerfer der Karte (NET07: 9).</item>
    /// <item><b>Formel</b>: zehn Teilchen in einer Zelle (Dichte 10: weder Ausbreitung noch
    /// Ausduennen), ein Schritt von »Check gas«. Soll je Teilchen und Achse:
    /// vx' = vx·19/20 genau, X' − X − vx/10 liegt in der Menge Wind + Hang, die aus Wind,
    /// Staerke und Hangart der Zelle folgt; und nicht ALLE Abweichungen sind 0 (das waere
    /// der Stand ohne Wind und Hang).</item>
    /// <item><b>Sprit</b>: ein Fahrzeug (Sprit 400) eine Zeile unter einer dichten Wolke,
    /// 60 Takte: Soll Sprit 1..4, Klang 310, TP gleich.</item>
    /// </list>
    /// ⚠ EINGRIFFE: die Gastafel wird fuer Teil 2 beiseitegelegt und danach zurueckgestellt;
    /// Teil 3 versetzt ein Fahrzeug und setzt Sprit/Ausruestung. Nullmodell
    /// <c>--gaswolke-aus</c>: Teil 1 (Kampagne) und Teil 2 MUESSEN scheitern.
    /// </summary>
    public string GaswolkeCheck(int mission)
    {
        var sb = new StringBuilder($"gaswolke-check {(mission > 0 ? $"K{mission}" : "Gefecht")}\n");
        EnsureMissionScript();
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (GaswolkeAus) sb.AppendLine("  ⚠ NULLMODELL --gaswolke-aus: hier MUSS der Lauf durchfallen");

        // 1. Aufbau
        if (mission > 0 && _mscript != null)
        {
            var soll = _mscript.Gas;
            int treffer = 0;
            for (int i = 0; i < soll.Count && i < _gasAufbauBild.Count; i++)
            {
                var q = soll[i]; var b = _gasAufbauBild[i];
                if (b.X == q[0] && b.Y == q[1] && b.Vx == q[2] && b.Vy == q[3] && b.Alter == q[4]) treffer++;
            }
            Soll(soll.Count > 0 && GasAufbau == soll.Count && treffer == soll.Count,
                 $"Aufbau: {GasAufbau} Teilchen gelegt, Skript {soll.Count}, Lage/Tempo/Alter gleich {treffer}/{soll.Count}");
            int unter = 0;
            foreach (var b in _gasAufbauBild)
            {
                int o = _nav.OccupantAt(b.X / GasFein, b.Y / GasFein + 1);
                if (o >= 0 && o < _entities.Count && !_entities[o].IsBuilding && !_entities[o].IsProp && _entities[o].Infantry < 0) unter++;
            }
            sb.AppendLine($"       (Zelle eine Zeile tiefer traegt bei {unter} Aufbauteilchen ein Fahrzeug; jetzt leben {_gasAnzahl} Teilchen)");
            int regelGas = _mscript.SkriptGasZahl();
            if (mission == 21)
                Soll(regelGas == 10 && _mscript.GasGehakt, $"M21: Skriptregel @0x49F618 traegt {regelGas} Teilchen (Soll 10), Haken {(_mscript.GasGehakt ? "da" : "FEHLT")}");
        }
        else
        {
            int werfer = 0;
            foreach (var e in _entities)
                if (!e.Dead && !e.IsProp && !e.IsBuilding && WeaponRowOf(e.Weapon) == 9) werfer++;
            sb.AppendLine($"       Gaswerfer auf der Karte: {werfer} (NET07: 9 — schiessen nur auf Klick, bei Tod 50·Munition Teilchen)");
        }

        // 2. die Formel — ein Schritt. Der Wind steht erst nach dem ersten Takt (TickWind).
        if (WindDir < 0) SimTickFuerProbe();
        var stash = (GasTeilchen[])_gas.Clone();
        int stashN = _gasAnzahl;
        try
        {
            System.Array.Clear(_gas); _gasAnzahl = 0;
            // eine freie Zelle, am liebsten mit Hangschub (Hangtafel != 0), sonst irgendeine
            int zc = -1, zr = -1;
            for (int pass = 0; pass < 2 && zc < 0; pass++)
                for (int r = 8; r < _nav.Height - 8 && zc < 0; r += 1)
                    for (int c = 8; c < _nav.Width - 8 && zc < 0; c += 1)
                    {
                        int ha = HangArt(c, r);
                        bool schub = ha > 0 && ha < GasHangTafel.Length && GasHangTafel[ha] != (0, 0);
                        if ((pass == 1 || schub) && _nav.OccupantAt(c, r + 1) < 0 && _nav.OccupantAt(c, r) < 0) { zc = c; zr = r; }
                    }
            if (zc < 0) Soll(false, "Formel: keine freie Zelle");
            else
            {
                int a = HangArt(zc, zr);
                var (hx, hy) = a >= 0 && a < GasHangTafel.Length ? GasHangTafel[a] : (0, 0);
                bool wind = WindDir >= 0 && WindStrength > 1;
                var (wx, wy) = wind ? UrRichtung[WindDir & 7] : (0, 0);
                HashSet<int> Menge(int w, int h)
                {
                    var m = new HashSet<int>();
                    for (int r1 = 0; r1 < 3; r1++)
                        for (int r2 = 0; r2 < 3; r2++)
                            m.Add((wind ? (r1 + WindStrength) * w / 150 : 0) + h / 50 - r2 + 1);
                    return m;
                }
                var mx = Menge(wx, hx); var my = Menge(wy, hy);
                var start = new List<(int X, int Y, int Vx, int Vy)>();
                for (int i = 0; i < 10; i++)
                {
                    int x = zc * GasFein + 30 + 6 * i, y = zr * GasFein + 30 + 5 * i;
                    int vx = 40 - 9 * i, vy = -25 + 7 * i;
                    GasAnlegen(x, y, vx, vy, 250);
                    start.Add((x, y, vx, vy));
                }
                int sTakt = _taktNr;
                if (_taktNr % 25 == 23) _taktNr++;                              // kein Treffertakt (EINGRIFF)
                GasPruefen();
                _taktNr = sTakt;
                int gut = 0, nichtNull = 0, n = 0;
                for (int k = 0; k < 10; k++)
                {
                    var g = _gas[k]; var s = start[k];
                    if (!g.Belegt) continue;
                    n++;
                    int ex = g.X - s.X - s.Vx / 10, ey = g.Y - s.Y - s.Vy / 10;
                    bool v = g.Vx == s.Vx * 19 / 20 && g.Vy == s.Vy * 19 / 20;
                    if (v && mx.Contains(ex) && my.Contains(ey)) gut++;
                    if (ex != 0 || ey != 0) nichtNull++;
                }
                Soll(n == 10 && gut == 10, $"Formel: Zelle ({zc},{zr}) Hangart {a}, Wind {(wind ? $"{WindDir}/{WindStrength}" : "zu schwach")}: "
                     + $"{gut}/{n} Teilchen in der Menge (dx {{{string.Join(",", mx)}}}, dy {{{string.Join(",", my)}}}), Tempo ·19/20");
                Soll(nichtNull > 0, $"Formel: {nichtNull}/{n} Teilchen weichen von der reinen Reibungsbahn ab (ohne Wind und Hang: 0)");
            }
        }
        finally
        {
            stash.CopyTo(_gas, 0);
            _gasAnzahl = stashN;
        }

        // 3. Sprit — echte Takte
        Entity? opfer = null; int oi = -1;
        for (int i = 0; i < _entities.Count && opfer == null; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Mobile && e.Infantry < 0 && e.GameUnitType == 0
                && !Untergestellt(e) && WeaponRowOf(e.Weapon) != 9 && e.Weapon is not (29 or 40)) { opfer = e; oi = i; }
        }
        if (opfer == null || !FreierStreifen(out int sc, out int sr)) sb.AppendLine("       Sprit: kein Fahrzeug/Streifen — Teil 3 entfaellt");
        else
        {
            opfer.Owner = ViewPlayer; opfer.Target = -1; opfer.Path = null; opfer.Ordered = false; opfer.Orders.Clear();
            ProbeVersetzen(oi, sc, sr);
            opfer.FuelMax = System.Math.Max(opfer.FuelMax, 400); opfer.Fuel = 400; opfer.Equipment = 0;
            int hp = opfer.Hp, treffer0 = GasTreffer;
            var gehoert = new List<int>();
            var vorher = Audio.SoundBankPlayer.Horcher;
            Audio.SoundBankPlayer.Horcher = (s, c, r) => { gehoert.Add(s); vorher?.Invoke(s, c, r); };
            try
            {
                // je Takt drei frische Teilchen in die Zelle ueber dem Fahrzeug — die Wolke
                // treibt mit Wind und Hang weiter, der Treffer kommt nur alle 25 Takte
                for (int t = 0; t < 60 && GasTreffer == treffer0; t++)
                {
                    for (int i = 0; i < 3; i++) GasAnlegen(opfer.Col * GasFein + 50 + 10 * i, (opfer.Row - 1) * GasFein + 60, 0, 0, 250);
                    SimTickFuerProbe();
                }
            }
            finally { Audio.SoundBankPlayer.Horcher = vorher; }
            // TP nur gemeldet: auf K25 schiesst der Feind mit (gaswerfer-check prueft TP auf freiem Streifen)
            Soll(opfer.Fuel is >= 1 and <= 4 && GasTreffer > treffer0 && gehoert.Contains(310),
                 $"Sprit: Fahrzeug ({opfer.Col},{opfer.Row}) unter frischem Gas (je Takt 3, hoechstens 60 Takte): Sprit {opfer.Fuel} (Soll 1..4), TP {hp}->{opfer.Hp} (nur gemeldet), "
                 + $"Gastreffer +{GasTreffer - treffer0}, Klang 310 {(gehoert.Contains(310) ? "gehoert" : "NICHT gehoert")}");
        }
        sb.AppendLine($"  --gaswolke-aus {GaswolkeAus}, --gas-spielstand-alt {GasSpielstandAlt}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
