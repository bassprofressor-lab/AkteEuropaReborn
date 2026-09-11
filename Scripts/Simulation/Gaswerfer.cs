namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DER GASWERFER — eine Wolke, die den TANK leert</b> (gebaut 11.09.2026 nach
/// <c>berichte/art7-gaswerfer.md</c> 3, seine Ansage »bau die offenen punkte auch
/// wie im original«; der Gassauger 0x439D20 selbst nachgelesen).
///
/// <para><b>Gelesen:</b></para>
/// <code>
///   0x40C8C0  ZBRAN 9 -> 0x439B30(einheit, x, y) — einziger Rufer (fire_at, Busbefehl 9)
///   0x439B30  x und y werden NICHT gelesen; Munition == 0 -> nichts; NABYTO != 0 -> nichts;
///             Munition −1, NABYTO := 120; Tafel 0x833870 (50): Zaehler 7,
///             X = Spalte·120 + 60, Y = (Zeile − 1)·120, Richtung = Tafel 0x4FA560[Aim]
///   0x439C50  je Takt je Werfer zehnmal Teilchen(X, Y, dx + 20 − rand%40, dy + 20 − rand%40)
///   0x439410  Tafel 0x77CAE8 (4000): freien Platz, keiner -> »too many gas«, Zufallsplatz
///   0x4396C0  Dichtekarte; je Teilchen: Takt % 25 == 23 -> 0x4395D0, 1 -> weg;
///             X += vx/10, vx = vx·19/20 (y ebenso); Wind; Hang; Dichte > 20 -> v += 20 − rand%40;
///             X &lt;= 0, Y &lt;= 0, ausserhalb -> weg; 1/10: Dichte Zelle + 4 Nachbarn &lt; 3 -> weg
///   0x4395D0  Zellwort an (cx, cy + 1): Einheit, nicht Gattung 1, Turm nicht 29/40,
///             Sprit > 4, Zusatz +0x10 != 87 -> Klang 310, Sprit := 1 + rand&amp;3, 1;
///             Gebaeude -> 1
///   0x439D20  (Zusatz +0x0E == 66, @0x40750E) nur wenn (Takt + einheit) &amp; 3 == 0:
///             je Teilchen mit (Spalte − X/120)² + (Zeile − Y/120 − 1)² &lt; 9:
///             fx = Spalte·120 − X + 60, fy = Zeile·120 − Y − 100;
///             |fx| &lt; 60 und |fy| &lt; 60 -> weg; sonst |f| &lt; 120 -> ±120;
///             v = 12000 / f (als Byte)
///   0x40B785  Tod eines Gaswerfers: 50·Munition Teilchen mit v = (20, 10)
/// </code>
///
/// <para>⚠ UNSERE SETZUNGEN, benannt: WIND und HANG fehlen (Windzustand
/// 0x4F8D68/0x4F8D6C und Hangtafel 0x4FA580 nicht ausgelesen); die Teilchen
/// werden mit dem Rauchbild <c>smoke0..2</c> halbdurchsichtig gezeichnet (der
/// Zeichner des Originals ist nicht gelesen); der Gassauger dreht die Einheit
/// nicht zur Wolke (+0x17 ueber 0x401951) und setzt kein +0x40; das 13×13-Fenster
/// @0x439DA3 prueft die ADRESSE der Dichtekarte und laeuft darum immer — so
/// gebaut; der Todeswolke fehlt ein gelesener Ort, sie sitzt wie der Werfer
/// (Spalte·120 + 60, (Zeile − 1)·120); im regulaeren Gefecht schiesst ein
/// Gaswerfer bei uns NIE — das Original schickt ihn ueber die Schiessuhr nicht
/// durch 0x40C8C0, und alle Kartensaetze tragen Reichweite 0.</para>
///
/// <para>Gegenschalter <c>--gaswerfer-als-schuss</c> (der Stand vor dem 11.09.:
/// die Schussroutine mit dem Rueckfall »BAUTEIL 29«, 10 Schaden). Pruefstand
/// <c>--gaswerfer-check</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--gaswerfer-als-schuss</c></summary>
    public static bool GaswerferAlsSchuss;

    private struct GasWerferPlatz { public int Zaehler, X, Y, Dx, Dy; }
    private struct GasTeilchen { public bool Belegt; public int X, Y; public sbyte Vx, Vy; }

    private const int GasWerferPlaetze = 50, GasPlaetze = 4000, GasFein = 120;
    private readonly GasWerferPlatz[] _gasWerfer = new GasWerferPlatz[GasWerferPlaetze];
    private readonly GasTeilchen[] _gas = new GasTeilchen[GasPlaetze];
    private readonly byte[] _gasDichte = new byte[256 * 256];
    private int _gasAnzahl;                                   // word[0x8106B8]
    public int GasAngelegt, GasTreffer, GasGesaugt, GasVoll, GasWerferVoll;

    /// <summary>Richtungstafel 0x4FA560 — Einheitsvektoren ×100, Index = Richtung.</summary>
    private static readonly (int Dx, int Dy)[] UrRichtung =
        { (0, 100), (-71, 71), (-100, 0), (-71, -71), (0, -100), (71, -71), (100, 0), (71, 71) };

    /// <summary>Schiesst diese Einheit im regulaeren Gefecht nicht, weil sie ein
    /// Gaswerfer ist? Siehe Kopf.</summary>
    private static bool GaswerferSchweigt(Entity e)
        => !GaswerferAlsSchuss && WeaponRowOf(e.Weapon) == 9;

    /// <summary>0x439B30 — gibt false zurueck, wenn nichts geschah.</summary>
    private bool GaswerferAbfeuern(Entity s, out string grund)
    {
        grund = "";
        bool cheat = CheatAmmo && Cheated(s);
        if (s.Ammo <= 0 && !cheat) { grund = "keine Munition (@0x439B4D)"; return false; }
        if (s.Cooldown > 0) { grund = "laedt nach (NABYTO, @0x439B58)"; return false; }
        if (!cheat) s.Ammo--;
        s.Cooldown = 120f / OriginalTicksPerSecond;                              // @0x439B61
        int k = System.Array.FindIndex(_gasWerfer, w => w.Zaehler == 0);
        if (k < 0)
        {
            GasWerferVoll++;
            GD.Print("can't add gas-thr (@0x439B8E)");
            return true;
        }
        int aim = s.AimFacing >= 0 ? s.AimFacing : s.Facing;
        var (dx, dy) = UrRichtung[((aim % 8) + 8) % 8];
        _gasWerfer[k] = new GasWerferPlatz
        {
            Zaehler = 7, X = s.Col * GasFein + 60, Y = (s.Row - 1) * GasFein, Dx = dx, Dy = dy,
        };
        return true;
    }

    /// <summary>0x439410</summary>
    private void GasAnlegen(int x, int y, int vx, int vy)
    {
        int k = System.Array.FindIndex(_gas, g => !g.Belegt);
        if (k < 0)
        {
            if (GasVoll++ == 0) GD.Print("Error: too many gas (@0x439410)");
            k = Simulation.Determinism.Roll(GasPlaetze);
        }
        if (!_gas[k].Belegt) _gasAnzahl++;
        _gas[k] = new GasTeilchen { Belegt = true, X = x, Y = y, Vx = unchecked((sbyte)vx), Vy = unchecked((sbyte)vy) };
        GasAngelegt++;
    }

    private void GasWeg(int k)
    {
        if (!_gas[k].Belegt) return;
        _gas[k].Belegt = false;
        _gasAnzahl--;
    }

    /// <summary>Stationen 46 und 47 und der Gassauger — jeden Takt.</summary>
    private void GasTakt()
    {
        // 0x439C50
        for (int k = 0; k < GasWerferPlaetze; k++)
        {
            ref var w = ref _gasWerfer[k];
            if (w.Zaehler == 0) continue;
            for (int n = 0; n < 10; n++)
                GasAnlegen(w.X, w.Y, w.Dx + 20 - Simulation.Determinism.Roll(40),
                           w.Dy + 20 - Simulation.Determinism.Roll(40));
            w.Zaehler--;
        }
        GasPruefen();
        if (_gasAnzahl == 0) return;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Part == 66 && !e.Dead && !e.IsProp && !e.IsBuilding && !Untergestellt(e))
                GasSauger(e.Slot >= 0 ? e.Slot : i, e);
        }
    }

    private int Dichte(int cx, int cy)
        => cx < 0 || cy < 0 || cx > 255 || cy > 255 ? 0 : _gasDichte[cx * 256 + cy];

    /// <summary>0x4396C0 »Check gas«</summary>
    private void GasPruefen()
    {
        if (_gasAnzahl == 0 || _nav == null) return;
        System.Array.Clear(_gasDichte);
        for (int k = 0; k < GasPlaetze; k++)
        {
            if (!_gas[k].Belegt) continue;
            int cx = _gas[k].X / GasFein, cy = _gas[k].Y / GasFein;
            if (cx is < 0 or > 255 || cy is < 0 or > 255) continue;
            ref byte d = ref _gasDichte[cx * 256 + cy];
            if (d < 254) d++;
        }
        bool trefferTakt = _taktNr % 25 == 23;
        for (int k = 0; k < GasPlaetze; k++)
        {
            if (!_gas[k].Belegt) continue;
            ref var g = ref _gas[k];
            int cx = g.X / GasFein, cy = g.Y / GasFein;
            if (trefferTakt && GasTrifft(cx, cy)) { GasWeg(k); continue; }
            g.X += g.Vx / 10; g.Vx = unchecked((sbyte)(g.Vx * 19 / 20));
            g.Y += g.Vy / 10; g.Vy = unchecked((sbyte)(g.Vy * 19 / 20));
            // Wind und Hang: NICHT gebaut, siehe Kopf
            if (Dichte(cx, cy) > 20)
            {
                g.Vx = unchecked((sbyte)(g.Vx + 20 - Simulation.Determinism.Roll(40)));
                g.Vy = unchecked((sbyte)(g.Vy + 20 - Simulation.Determinism.Roll(40)));
            }
            cx = g.X / GasFein; cy = g.Y / GasFein;
            if (g.X <= 0 || g.Y <= 0 || cx >= _nav.Width || cy >= _nav.Height) { GasWeg(k); continue; }
            if (Simulation.Determinism.Roll(10) == 0
                && Dichte(cx, cy) + Dichte(cx - 1, cy) + Dichte(cx + 1, cy) + Dichte(cx, cy - 1) + Dichte(cx, cy + 1) < 3)
                GasWeg(k);
        }
    }

    /// <summary>0x4395D0 — das Zellwort eine Zeile tiefer.</summary>
    private bool GasTrifft(int cx, int cy)
    {
        int r = cy + 1;
        if (_nav == null || cx < 0 || r < 0 || cx >= _nav.Width || r >= _nav.Height) return false;
        int o = _nav.OccupantAt(cx, r);
        if (o >= 0 && o < _entities.Count)
        {
            var e = _entities[o];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Infantry < 0)
            {
                if (e.GameUnitType == 1) return false;                           // @0x43961B
                if (e.Weapon is 29 or 40) return false;                          // @0x439628
                if (e.Fuel <= 4) return false;                                   // @0x439632
                if (e.Equipment == 87) return false;                             // @0x439642 Gashield
                Audio.GameSounds.PlayAt(310, e.Col, e.Row);                      // @0x43964C
                e.Fuel = 1 + (Simulation.Determinism.Roll(4));
                GasTreffer++;
                if (_gwCheckAn) _gwTreffer.Add((o, _taktNr));
                return true;
            }
        }
        return GebaeudeAufZelle(cx, r) >= 0;                                     // @0x439673
    }

    /// <summary>0x439D20</summary>
    private void GasSauger(int nr, Entity e)
    {
        if (((_taktNr + nr) & 3) != 0) return;
        for (int k = 0; k < GasPlaetze; k++)
        {
            if (!_gas[k].Belegt) continue;
            ref var g = ref _gas[k];
            int ddx = e.Col + g.X / -GasFein;
            int ddy = e.Row + g.Y / -GasFein - 1;
            if (ddx * ddx + ddy * ddy >= 9) continue;
            int fx = e.Col * GasFein - g.X + 60;
            int fy = e.Row * GasFein - g.Y - 100;
            if (System.Math.Abs(fx) < 60 && System.Math.Abs(fy) < 60) { GasWeg(k); GasGesaugt++; continue; }
            if (System.Math.Abs(fx) < 120) fx = fx > 0 ? 120 : -120;
            if (System.Math.Abs(fy) < 120) fy = fy > 0 ? 120 : -120;
            g.Vx = unchecked((sbyte)(12000 / fx));
            g.Vy = unchecked((sbyte)(12000 / fy));
        }
    }

    /// <summary>@0x40B785</summary>
    private void TodDesGaswerfers(Entity v)
    {
        if (GaswerferAlsSchuss || WeaponRowOf(v.Weapon) != 9) return;
        for (int n = 0; n < 50 * System.Math.Max(0, v.Ammo); n++)
            GasAnlegen(v.Col * GasFein + 60, (v.Row - 1) * GasFein, 20, 10);
    }

    /// <summary>Die Teilchen — Rauchbild, unsere Setzung.</summary>
    private void GasZeichnen()
    {
        if (_gasAnzahl == 0) return;
        for (int k = 0; k < GasPlaetze; k++)
        {
            if (!_gas[k].Belegt) continue;
            var frames = EffectFrames("smoke" + (k % 3));
            if (frames.Count == 0) continue;
            var g = _gas[k];
            int cx = g.X / GasFein, cy = g.Y / GasFein + 1;
            float px = _ox + g.X / (float)GasFein * TileW;
            float py = _oy + (g.Y / (float)GasFein + 1f) * TileH + TileH / 2f - HubOf(cx, cy);
            var tex = frames[(k + _taktNr / 6) % frames.Count];
            DrawTexture(tex, new Vector2(px, py) - _fxAnchor["smoke" + (k % 3)], new Color(1, 1, 1, 0.55f));
        }
    }

    // ================= der Pruefstand ==========================================

    private bool _gwCheckAn;
    private int _gwStartTakt, _gwAngelegtVorher;
    private readonly List<(int Idx, int Takt)> _gwTreffer = new();

    private sealed class GwFall
    {
        public char Name;
        public string Titel = "", Notiz = "", Grund = "", Grund2 = "";
        public int Werfer = -1, Opfer = -1, AmmoVorher, AmmoNachher, HpVorher, TeilchenTod = -1;
        public bool Geschossen, Zweiter;
        public float Nachladen;
    }

    private readonly List<GwFall> _gwFaelle = new();

    /// <summary>
    /// <c>--gaswerfer-check</c> — je Fall ein freier Streifen, Werfer mit Waffe 29,
    /// 4 Schuss, Rohr nach Osten; fire_at auf eine Zelle FUENF ZEILEN UEBER dem
    /// Werfer (Beleg, dass x/y nicht zaehlen):
    /// G Fahrzeug mit Sprit 400 eine Zelle oestlich (Soll: Sprit ≤ 4, TP gleich,
    /// Munition −1, Nachladen 2,4 s, zweiter Befehl »laedt nach«);
    /// S dasselbe mit Gashield 87 (Soll: Sprit 400);
    /// Z ein Gassauger (+0x0E = 66) zwei Zellen oestlich (Soll: Teilchen gesaugt);
    /// T ein Gaswerfer mit 3 Schuss stirbt (Soll: 150 Teilchen).
    /// <para>⚠ EINGRIFFE: Waffe, Munition, Aim, Besitzer (alle beim Spieler — Gas
    /// fragt kein Buendnis), Sprit, Ausruestung, +0x0E, Versetzen, Tod von Hand.
    /// Nullmodell <c>--gaswerfer-als-schuss</c>.</para>
    /// </summary>
    public void GaswerferCheckStart()
    {
        if (_nav == null) return;
        _gwCheckAn = true;
        _gwStartTakt = _taktNr;
        _gwAngelegtVorher = GasAngelegt;
        var benutzt = new HashSet<int>();
        bool Frei(int k)
        {
            var x = _entities[k];
            return !benutzt.Contains(k) && !x.Dead && !x.IsProp && !x.IsBuilding && x.Mobile
                   && x.Ukol < 50 && !Untergestellt(x) && x.Infantry < 0 && x.GameUnitType == 0;
        }
        int Suche()
        {
            for (int k = 0; k < _entities.Count; k++)
                if (Frei(k)) { benutzt.Add(k); return k; }
            return -1;
        }
        void Stellen(int idx, int c, int r)
        {
            var e = _entities[idx];
            e.Owner = ViewPlayer; e.Target = -1; e.Path = null; e.Ordered = false; e.Orders.Clear();
            ProbeVersetzen(idx, c, r);
        }
        void Werfer(GwFall f, int c, int r, int munition)
        {
            var w = _entities[f.Werfer];
            w.Weapon = 29; w.AmmoMax = System.Math.Max(w.AmmoMax, 4); w.Ammo = munition;
            w.Cooldown = 0; w.AimFacing = 6; w.Facing = 6;
            Stellen(f.Werfer, c, r);
        }

        GwFall Fall(char name, string titel, int opferDx, System.Action<Entity> opfer)
        {
            var f = new GwFall { Name = name, Titel = titel };
            _gwFaelle.Add(f);
            f.Werfer = Suche();
            if (f.Werfer < 0) { f.Notiz = "kein Fahrzeug fuer den Werfer"; return f; }
            if (!FreierStreifen(out int c, out int r)) { f.Notiz = "kein freier Streifen"; f.Werfer = -1; return f; }
            Werfer(f, c, r, name == 'T' ? 3 : 4);
            if (opfer != null)
            {
                f.Opfer = Suche();
                if (f.Opfer < 0) { f.Notiz = "kein Opferfahrzeug"; f.Werfer = -1; return f; }
                var o = _entities[f.Opfer];
                o.Weapon = 0;
                opfer(o);
                Stellen(f.Opfer, c + opferDx, r);
                f.HpVorher = o.Hp;
            }
            if (name == 'T')
            {
                int vor = GasAngelegt;
                var w = _entities[f.Werfer];
                Kill(f.Werfer, w, -1, "PRUEFSTAND gaswerfer-check");
                f.TeilchenTod = GasAngelegt - vor;
                return f;
            }
            var s = _entities[f.Werfer];
            f.AmmoVorher = s.Ammo;
            f.Geschossen = FireAtAusfuehren(f.Werfer, s.Col, System.Math.Max(0, s.Row - 5));
            f.Grund = FireAtGrund;
            f.AmmoNachher = s.Ammo;
            f.Nachladen = s.Cooldown;
            f.Zweiter = FireAtAusfuehren(f.Werfer, s.Col, System.Math.Max(0, s.Row - 5));
            f.Grund2 = FireAtGrund;
            return f;
        }

        void Tank(Entity o) { o.FuelMax = System.Math.Max(o.FuelMax, 400); o.Fuel = 400; o.Equipment = 0; }
        Fall('G', "Fahrzeug Sprit 400 oestlich", 1, Tank);
        Fall('S', "dasselbe mit Gashield 87   ", 1, o => { Tank(o); o.Equipment = 87; });
        Fall('Z', "Gassauger zwei Zellen oestl", 2, o => { Tank(o); o.Part = 66; });
        Fall('T', "Gaswerfer mit 3 Schuss stirbt", 0, null);
        foreach (var f in _gwFaelle)
            GD.Print($"gaswerfer-check: {f.Name} {f.Titel.Trim()} — Werfer {f.Werfer}, Opfer {f.Opfer}, "
                   + $"{(f.Geschossen ? "gesprueht" : "kein Spruehen: " + f.Grund)} {f.Notiz}");
    }

    public string GaswerferCheckLine()
    {
        var sb = new System.Text.StringBuilder("gaswerfer-check\n");
        if (!_gwCheckAn) return sb.Append("  nicht gestartet — der Lauf sagt NICHTS").ToString();
        int takte = _taktNr - _gwStartTakt;
        bool ok = _gwFaelle.Count > 0 && takte >= 60;
        if (takte < 60) sb.Append($"  ⚠ nur {takte} Takte gelaufen (Soll >= 60) — --quit-after erhoehen\n");
        float sollLaden = 120f / OriginalTicksPerSecond;
        int spruehende = 0;
        foreach (var f in _gwFaelle)
        {
            if (f.Werfer < 0)
            {
                ok = false;
                sb.Append($"  {f.Name} {f.Titel}: ⚠ nicht herstellbar ({f.Notiz}) — sagt NICHTS\n");
                continue;
            }
            if (f.Name == 'T')
            {
                bool sollT = f.TeilchenTod == 150;
                ok &= sollT;
                sb.Append($"  T {f.Titel}: {f.TeilchenTod} Teilchen beim Tod (Soll 150)  {(sollT ? "ja" : "NEIN")}\n");
                continue;
            }
            if (f.Geschossen) spruehende++;
            var o = _entities[f.Opfer];
            bool abfeuern = f.Geschossen && f.AmmoNachher == f.AmmoVorher - 1
                            && System.Math.Abs(f.Nachladen - sollLaden) < 0.01f
                            && !f.Zweiter && f.Grund2.Contains("laedt");
            bool soll = f.Name switch
            {
                'G' => abfeuern && o.Fuel <= 4 && o.Hp == f.HpVorher,
                'S' => abfeuern && o.Fuel == 400 && o.Hp == f.HpVorher,
                _ => abfeuern && GasGesaugt > 0,
            };
            ok &= soll;
            int treffer = _gwTreffer.FindAll(t => t.Idx == f.Opfer).Count;
            sb.Append($"  {f.Name} {f.Titel}: {(f.Geschossen ? "gesprueht" : "kein Spruehen — " + f.Grund)}, "
                    + $"Munition {f.AmmoVorher}->{f.AmmoNachher}, Nachladen {f.Nachladen:0.00} s (Soll {sollLaden:0.00}), "
                    + $"zweiter Befehl {(f.Zweiter ? "GESPRUEHT" : "— " + f.Grund2)}; "
                    + $"Opfer Sprit {o.Fuel}, TP {f.HpVorher}->{o.Hp}, Gastreffer {treffer}"
                    + (f.Name == 'Z' ? $", gesaugt {GasGesaugt}" : "")
                    + $"  {(soll ? "ja" : "NEIN")}\n");
        }
        int angelegt = GasAngelegt - _gwAngelegtVorher;
        int sollTeilchen = 70 * spruehende + 150;
        bool teilchen = angelegt == sollTeilchen;
        ok &= teilchen;
        sb.Append($"  Teilchen angelegt {angelegt} (Soll 70 je Spruehen + 150 = {sollTeilchen})  {(teilchen ? "ja" : "NEIN")}\n");
        sb.Append($"  nach {takte} Takten: {_gasAnzahl} Teilchen leben, {GasTreffer} Gastreffer, {GasGesaugt} gesaugt, "
                + $"{GasVoll}x voll; Gegenschalter --gaswerfer-als-schuss: {GaswerferAlsSchuss}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
