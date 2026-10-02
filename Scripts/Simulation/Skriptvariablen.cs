using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE ENGINE-SCHREIBER DER VARIABLENTAFEL und der Prüfstand
/// <c>--untermission-check=N</c></b> — 02.10.2026, seine Freigabe »audit bauen«
/// zu berichte/skriptvariablen-audit-fable.md (Bauvorlage §5 A–E).
///
/// <para>Die Missionsblöcke lesen sieben Variablen, die NICHT der Block
/// schreibt, sondern das Spiel selbst (Relokationstafel über
/// <c>0xBC5690..0xBC5A70</c>, in C und F dieselben Befehle). Bis heute war davon
/// nur v0 gebaut (ZasahSonderfaelle.cs). Hier stehen die übrigen:</para>
/// <list type="bullet">
/// <item><b>v93</b> @<c>0x40B42A</c> (F 0x40B31A), Todesroutine: Opfer trägt
/// <c>+0x0F == 0xAB</c> (171, »Abwehrstellung«) und
/// <c>byte[0x87B155 + 40·Sicht + Besitzer] == 0</c> → <c>v93 := 1</c>.
/// ⚠ BERICHTIGUNG zum Bericht (»Schütze nicht verbündet«): der Index ist
/// <c>di/1000</c>, und <c>di</c> ist das OPFER — dieselbe Routine meldet
/// gleich danach @0x40B433 »eigene Einheit verloren«, wenn <c>di/1000</c> der
/// Sichtspieler ist, an der Stelle von <c>di</c>. Ein Schütze kommt in der
/// Bedingung nicht vor. Leser: M24 R2 (150 $), M31 R11 (250 $).
/// Gegenschalter <c>--v93-aus</c>.</item>
/// <item><b>v100</b> @<c>0x4C9AAE</c> (F 0x4C965E), Gebäudetod: nach
/// <c>typ := 0</c> (@0x4C9A8E) <c>0x87B155[40·Sicht + Besitzer] == 0</c> →
/// <c>v100 := 1</c>. Leser: M23 R2 (230 $ je Gebäude). <c>--v100-aus</c>.</item>
/// <item><b>v99</b> @<c>0x4C15A3</c> (F 0x4C1063), <c>deliver_one</c>: Käufer
/// <c>byte[0xB49C50+n]</c> == Sichtspieler → <c>inc v99</c>, je Stück. Leser:
/// M11 R2 (Text 210), M15 R2 (Text 252, danach 650 $). <c>--v99-aus</c>.</item>
/// <item><b>v190/191/192</b> @<c>0x4B2A3F/0x4B2A29/0x4B2A45</c>, Recycle
/// <c>0x4B28E0</c>: die erstatteten W/F/S. Leser: M5 R15/R16 (take_var — die
/// Siegmarke wächst mit, Recycling zählt NICHT als Lieferung).
/// <c>--v190-aus</c>.</item>
/// <item><b>v98</b> @<c>0x4C0670</c> (F 0x4C0130), Frachter-Arm 1:
/// <c>v98 := Einheit/1000 + 1</c> — erst wenn der Raumfrachter die verkaufte
/// Einheit ABGEHOLT hat. Dazu baut <see cref="SkriptVerkauf"/> den
/// <c>sell_unit</c> des Skripts so, wie das Original ihn baut: 0x4D0EC0 ruft
/// <c>0x4BFFF0(Einheit, 0)</c> — Eintrag in die Verkaufsliste mit
/// <b>Preis 0</b> (<c>push 0</c> @0x4D0EC4, damit ist die Frage »Erlös
/// ungelesen« aus Bericht §7 beantwortet), kein Tod. Leser: M19 R14/R15
/// (Spion-Flucht), M24 R20/R22, M28 R42, M29 R26 (Barry More).
/// <c>--verkauf-sofort</c> = altes Verhalten (sofortiger Tod, kein v98).</item>
/// </list>
///
/// <para><c>--skriptvar-alt</c> setzt alle fünf Gegenschalter UND lässt die vom
/// Audit nachgetragenen Regeln/Glieder/Wirkungen der JSON weg (Schlüssel
/// <c>_schalter</c>/<c>_nur_mit</c>, siehe MissionScript.GliedAus): Stand vor dem
/// 02.10.2026.</para>
///
/// <para>⚠ UNSERE SETZUNGEN: (1) ein Gebäude ohne Besitzer (−1, im Original
/// 0xFF) setzt v100 NICHT — das Original läse <c>0x87B155 + 40·Sicht + 255</c>,
/// also ein Byte einer fremden Zeile; ungelesen. (2) Ein zweites
/// <c>sell_unit</c> auf eine schon eingetragene Einheit trägt sie nicht noch
/// einmal ein — das Original meldet »already in list« (@0x4C0022) und trägt
/// trotzdem ein; die Doppelabholung hätte keine sichtbare Folge.</para>
/// </summary>
public partial class MapEntityLayer
{
    // ================= Gegenschalter =========================================

    public static bool V93Aus, V99Aus, V100Aus, V190Aus, VerkaufSofort;

    /// <summary><c>--skriptvar-alt</c>: alles wie vor dem Audit.</summary>
    public static void SkriptvarAltSetzen()
    {
        V93Aus = V99Aus = V100Aus = V190Aus = VerkaufSofort = true;
        Campaign.MissionScript.SetzungAus.Add(Campaign.MissionScript.SkriptvarAlt);
    }

    /// <summary>Wie oft jeder Schreiber gesetzt hat — für den Prüfstand.</summary>
    public int V93Gesetzt, V99Gesetzt, V100Gesetzt, V190Gesetzt, V98Gesetzt, SkriptVerkaeufe;

    // ================= die Schreiber =========================================

    /// <summary>v93 (@0x40B42A) und v100 (@0x4C9AAE) — aus <see cref="Kill"/>,
    /// bevor das Opfer als tot gilt.</summary>
    private void SkriptvarTod(Entity victim)
    {
        if (_mscript == null || victim.Dead || victim.IsProp) return;
        int sicht = ViewPlayer;
        if (victim.Owner is < 0 or > 7) return;           // UNSERE SETZUNG (1), siehe Kopf
        if (Allied(sicht, victim.Owner)) return;          // 0x87B155[40·Sicht + Besitzer] != 0
        if (!victim.IsBuilding)
        {
            if (V93Aus || victim.Comp0F != 171) return;    // +0x0F == 0xAB @0x40B401
            _mscript.SetVar(93, 1);
            V93Gesetzt++;
            GD.Print($"skriptvar: v93 := 1 — Abwehrstellung Platz {victim.Slot} (Spieler {victim.Owner}) zerstoert (@0x40B42A)");
        }
        else
        {
            if (V100Aus) return;
            _mscript.SetVar(100, 1);
            V100Gesetzt++;
            GD.Print($"skriptvar: v100 := 1 — Gebaeude Platz {victim.Slot} (Art {victim.BType}, Spieler {victim.Owner}) zerstoert (@0x4C9AAE)");
        }
    }

    /// <summary>inc v99 je geliefertem Stück, wenn der Käufer der Sichtspieler
    /// ist (@0x4C15A3).</summary>
    private void SkriptvarMarktlieferung(int kaeufer)
    {
        if (_mscript == null || V99Aus || kaeufer != ViewPlayer) return;
        _mscript.SetVar(99, _mscript.Var(99) + 1);
        V99Gesetzt++;
        GD.Print($"skriptvar: v99 := {_mscript.Var(99)} — Marktlieferung fuer Spieler {kaeufer} (@0x4C15A3)");
    }

    /// <summary>v190/191/192 := die Erstattung des Recyclings (@0x4B2A3F/29/45).
    /// ⚠ Gesetzt, nicht addiert: das Original schreibt <c>mov</c>.</summary>
    private void SkriptvarRecycling(int w, int f, int s)
    {
        if (_mscript == null || V190Aus) return;
        _mscript.SetVar(190, w);
        _mscript.SetVar(191, f);
        _mscript.SetVar(192, s);
        V190Gesetzt++;
        GD.Print($"skriptvar: v190/191/192 := {w}/{f}/{s} — Recycling-Erstattung (@0x4B2A29ff)");
    }

    /// <summary>
    /// <c>sell_unit</c> des Skripts (0x4D0EC0 → 0x4BFFF0(Einheit, 0)): in die
    /// Verkaufsliste mit Preis 0 und Zustand 0xFF — der Markttakt
    /// (MarketTradeTickOnce) wartet, bis sie steht, schickt bei Phase 111 den
    /// Abholer, und <see cref="PayForSale"/> nimmt sie bei seiner Ankunft vom
    /// Feld und setzt v98. Gibt false zurück, wenn der alte Sofortweg gilt.
    /// </summary>
    private bool SkriptVerkauf(int idx, Entity e)
    {
        if (VerkaufSofort || !TradeLikeOriginal) return false;
        foreach (var o in _sellOffers)
            if (o.Unit == idx)
            {
                GD.Print($"Missionsskript: Einheit {e.Slot} steht schon in der Verkaufsliste (UNSERE SETZUNG (2))");
                return true;
            }
        // byte[ent+0x14] = 0 (@0x4C000B) — wie ApplySell.
        e.Path = null;
        e.Orders.Clear();
        e.Target = -1;
        if (e.Reserved is { } rc) { _nav?.ClearOccupant(rc.X, rc.Y, idx); e.Reserved = null; }
        _sellOffers.Add(new SellOffer { Unit = idx, Price = 0, State = 0xFF });
        SkriptVerkaeufe++;
        GD.Print($"Missionsskript: Einheit {e.Slot} (Spieler {e.Owner}) zum Verkauf eingetragen, Preis 0 " +
                 "— der Raumfrachter holt sie ab (@0x4D0EC0 -> 0x4BFFF0)");
        return true;
    }

    /// <summary>v98 := Besitzer + 1 (@0x4C0670) — im Abholarm, für JEDEN
    /// vollzogenen Verkauf, auch den des Spielers.</summary>
    private void SkriptvarVerkaufVollzogen(int besitzer)
    {
        if (_mscript == null || VerkaufSofort || !TradeLikeOriginal) return;
        _mscript.SetVar(98, besitzer + 1);
        V98Gesetzt++;
        GD.Print($"skriptvar: v98 := {besitzer + 1} — Verkauf durch den Frachter vollzogen (@0x4C0670)");
    }

    /// <summary>Die zwei neuen Haken des Interpreters: Verkaufsliste
    /// (<c>in_sale_var</c>) und Flugzeugzähler (<c>set_air_count</c>).</summary>
    private void SkriptvarHakenSetzen()
    {
        if (_mscript == null) return;
        _mscript.InSale = platz =>
        {
            foreach (var o in _sellOffers)
                if (o.Unit >= 0 && o.Unit < _entities.Count && !_entities[o.Unit].Dead
                    && !_entities[o.Unit].IsBuilding && _entities[o.Unit].Slot == platz) return true;
            return false;
        };
        // Flugzeugtafel 0x6DDF78: +0 Typ (unser Kind), +1 Besitzer.
        _mscript.AirCount = (besitzer, typMin) =>
        {
            int n = 0;
            foreach (var a in _special)
                if (!a.Dead && a.Kind > typMin && a.Owner == besitzer) n++;
            return n;
        };
    }

    // ================= der Prüfstand =========================================

    /// <summary>Was die Wirkungsprobe kennt und warum es fehlen darf.</summary>
    private static readonly Dictionary<(int M, int At), string> WirkungsprobeBekannt = new()
    {
        [(3, 0x499651)] = "Text 25 in der UKOL-Schleife (Audit E, nicht gebaut)",
        [(7, 0x49AB90)] = "Text 175 Cossarro verwundet (Audit E, nicht gebaut)",
        [(11, 0x49BEA1)] = "Rest auf 250 Teile, gedeckt durch Handregel 0x49BEA9 (m11-hotelplaza-fable.md)",
        [(11, 0x49BF02)] = "Text 100, gedeckt durch Handregel 0x49BEA9",
        [(11, 0x49BF18)] = "430 $, gedeckt durch Handregel 0x49BEA9",
        [(14, 0x49D2B1)] = "Hinweis Text 240 (Audit E, nicht gebaut)",
        [(15, 0x49D9C2)] = "Text 255 neutrale Panzer (Audit E, nicht gebaut)",
        [(21, 0x49FDCF)] = "Text 317 Dan Simonski (Audit C9, nicht im Auftrag)",
        [(24, 0x4A10C7)] = "Text 343 Cossarro — im Original tot (v303)",
        [(28, 0x4A2D43)] = "Text 381 Expedition zieht ab (Audit E, nicht gebaut)",
    };

    /// <summary>Die Wirkungsprobe (Audit §1.3c) als Selbsttest: jede
    /// show_text-/Geld-Stelle der EXE (Data/mission_wirkungen.json, erzeugt von
    /// aekernel-tools/mission_wirkungsprobe.py) gegen die GELADENEN Wirkungen.
    /// Gibt die Zahl der unerklärten Lücken zurück.</summary>
    private int Wirkungsprobe(System.Text.StringBuilder sb, int nur)
    {
        static string Wert(Godot.Collections.Dictionary<string, Variant> d)
            => d["wert"].VariantType == Variant.Type.Nil ? "(Register)" : d["wert"].AsInt32().ToString();
        using var f = FileAccess.Open("res://Data/mission_wirkungen.json", FileAccess.ModeFlags.Read);
        if (f == null) { sb.AppendLine("  Wirkungsprobe: Data/mission_wirkungen.json fehlt"); return 1; }
        var root = Json.ParseString(f.GetAsText()).AsGodotDictionary<string, Variant>();
        var ms = root["missions"].AsGodotDictionary<string, Variant>();
        int stellen = 0, gedeckt = 0, bekannt = 0, offen = 0;
        var offenListe = new List<string>();
        for (int m = 1; m <= 33; m++)
        {
            if (!ms.ContainsKey(m.ToString())) continue;
            var da = m == UI.SkirmishSetup.CampaignMission && _mscript != null
                   ? _mscript.WirkungsStellen() : Campaign.MissionScript.WirkungsStellenVon(m);
            foreach (var s in ms[m.ToString()].AsGodotArray())
            {
                var d = s.AsGodotDictionary<string, Variant>();
                string ats = d["at"].AsString();
                int at = Convert.ToInt32(ats[2..], 16);
                stellen++;
                if (da.Contains(at)) { gedeckt++; continue; }
                if (WirkungsprobeBekannt.TryGetValue((m, at), out var grund))
                {
                    bekannt++;
                    if (m == nur) sb.AppendLine($"    bekannt offen M{m} {ats} {d["art"]} {Wert(d)}: {grund}");
                    continue;
                }
                offen++;
                offenListe.Add($"M{m} {ats} {d["art"]} {Wert(d)}");
            }
        }
        sb.AppendLine($"  Wirkungsprobe 33 Missionen: {stellen} Stellen, {gedeckt} gedeckt, " +
                      $"{bekannt} bekannt offen, {offen} UNERKLAERT");
        foreach (var o in offenListe.Take(40)) sb.AppendLine("    UNERKLAERT " + o);
        return offen;
    }

    public string UntermissionCheck(int soll)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        bool alt = Campaign.MissionScript.SetzungAus.Contains(Campaign.MissionScript.SkriptvarAlt);
        var sb = new System.Text.StringBuilder($"untermission-check K{m}" +
            (alt ? " — NULLMODELL --skriptvar-alt: jede neue Wirkung MUSS fehlen" : "") + "\n");
        sb.AppendLine($"  Schalter: --skriptvar-alt {alt}, --v93-aus {V93Aus}, --v99-aus {V99Aus}, " +
                      $"--v100-aus {V100Aus}, --v190-aus {V190Aus}, --verkauf-sofort {VerkaufSofort}");
        if (m != soll) return sb.Append($"  KEIN URTEIL: --untermission-check={soll}, aber --campaign={m}").ToString();
        MissionScriptTick(0.001f);
        if (_mscript == null) return sb.Append("  KEIN URTEIL: kein Missionsskript").ToString();
        var ms = _mscript;
        sb.AppendLine($"  Audit-Regeln geladen {Campaign.MissionScript.AuditGeladen}, ausgelassen " +
                      $"{Campaign.MissionScript.AuditAusgelassen}");
        int fehler = 0;
        int view = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        // Aufzeichner: Texte, Skriptgeld des Sichtspielers, KI-Ziele.
        var texte = new List<int>();
        int geld = 0, ziele = 0;
        var alterText = ms.ShowText; var alterGeld = ms.AddMoney; var alterZiel = ms.AddTarget;
        ms.ShowText = (id, art, x, y) => { texte.Add(id); alterText?.Invoke(id, art, x, y); };
        ms.AddMoney = (b, p) => { if (p == view) geld += b; alterGeld?.Invoke(b, p); };
        ms.AddTarget = (a, b, c, d, e) => { ziele++; alterZiel?.Invoke(a, b, c, d, e); };

        void Soll(bool neu, string was)
        {
            bool ok = alt ? !neu : neu;
            sb.Append(ok ? "  ok     " : "  FEHLER ").Append(alt ? "[muss fehlen] " : "").Append(was).Append('\n');
            if (!ok) fehler++;
        }
        void Immer(bool b, string was)
        {
            sb.Append(b ? "  ok     " : "  FEHLER ").Append(was).Append('\n');
            if (!b) fehler++;
        }
        void Eingriff(string was) => sb.AppendLine("  EINGRIFF: " + was);
        int V(int n) => ms.Var(n);
        void Setze(int n, int w) => ms.SetVarFuerProbe(n, w);
        int Takte(int n, Func<bool>? bis = null)
        {
            for (int t = 0; t < n; t++)
            {
                SimTickFuerProbe();
                if (bis != null && bis()) return t + 1;
            }
            return n;
        }
        Entity? Einheit(int slot)
        {
            foreach (var e in _entities)
                if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Slot == slot) return e;
            return null;
        }
        Entity? MitMarke(int spieler, int marke)
        {
            foreach (var e in _entities)
                if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == spieler && e.Mark == marke) return e;
            return null;
        }
        bool Stelle(Entity e, IEnumerable<(int C, int R)> zellen)
        {
            int i = _entities.IndexOf(e);
            if (_nav == null || i < 0) return false;
            foreach (var (c, r) in zellen)
            {
                if (!_nav.InBounds(c, r)) continue;
                if (!(e.Col == c && e.Row == r) && !_nav.IsFree(c, r, e.Move, i)) continue;
                _nav.ClearOccupant(e.Col, e.Row, i);
                if (e.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, i);
                e.Reserved = null; e.Path = null; e.Orders.Clear(); e.Target = -1;
                e.Col = c; e.Row = r;
                e.Elev = ElevOf(c, r);
                e.Pos = BodyCenterAt(e, c, r);
                e.Footprint = CellRect(_ox, _oy, c, r, e.Elev);
                _nav.SetOccupant(c, r, i, e.Infantry >= 0);
                sb.AppendLine($"    (Platz {e.Slot} steht jetzt auf ({c},{r}))");
                return true;
            }
            sb.AppendLine($"    ⚠ keine der Zielzellen frei fuer Platz {e.Slot}");
            return false;
        }
        IEnumerable<(int, int)> Ring(int c, int r)
        {
            yield return (c, r);
            for (int d = 1; d <= 2; d++)
                for (int dx = -d; dx <= d; dx++)
                    for (int dy = -d; dy <= d; dy++)
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == d) yield return (c + dx, r + dy);
        }
        bool Sieg() => ms.Grace >= 0 || (ms.Ended && ms.Success);
        int Sprenge(Func<Entity, bool> welche, string was)
        {
            int n = 0;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || !welche(e)) continue;
                Kill(i, e, view, "untermission-check: " + was);
                n++;
            }
            return n;
        }
        int Heli(int n, int besitzer)
        {
            int slot = 0;
            foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
            for (int k = 0; k < n; k++)
                _special.Add(new Special
                {
                    Slot = slot + k, Kind = 13, Name = "Pruefstand", TypeName = "Pruefstand",
                    Col = 10 + k, Row = 10, Stored = true, Owner = besitzer, HomeSlot = -1,
                    Pos = CellCenter(10 + k, 10), Speed = 20,
                    Hp = 100, HpMax = 100, Ammo = 10, AmmoMax = 10, Fuel = 999, FuelMax = 999,
                    Attack = 0, Defence = 1, Sight = 1,
                });
            return n;
        }
        bool Abholen(Entity e, int maxTakte)
        {
            Takte(maxTakte, () => e.Dead);
            Takte(30);
            return e.Dead;
        }

        Takte(60);   // Setup-Regeln anlaufen lassen

        switch (m)
        {
            case 28:
            {
                var e0 = Einheit(0);
                Immer(e0 != null && e0.Mark == 193 && e0.Owner == 0,
                      $"Experte auf Platz 0 (Marke {e0?.Mark}, Spieler {e0?.Owner}) bei ({e0?.Col},{e0?.Row})");
                if (e0 == null) break;
                Eingriff("Experte Platz 0 in die Zone 34<x<39, 8<y<15 gestellt");
                Stelle(e0, Ring(37, 11).Where(z => z.Item1 is > 34 and < 39 && z.Item2 is > 8 and < 15));
                Takte(200, () => V(51) != 0);
                Soll(V(51) == 1, $"0x4A30A8: v51 = {V(51)} (1)");
                Soll(Einheit(0) == null, "Experte 0 aus der Zone genommen (remove_unit)");
                Eingriff("v61 := Uhr − 2 (statt eine Spielminute zu warten)");
                Setze(61, ms.Minutes - 2);
                Takte(200, () => V(51) == 2);
                Soll(V(51) == 2, $"0x4A3151: v51 = {V(51)} (2), v54 = {V(54)}");
                var neu = V(51) == 2 ? Einheit(V(54)) : null;
                if (neu != null)
                {
                    Eingriff($"Experte v54 (Platz {neu.Slot}) nach (11,243) gestellt — das Ziel der Siegregel");
                    if (Stelle(neu, new[] { (11, 243) }))
                    {
                        Takte(200, Sieg);
                        Soll(Sieg(), $"Siegregel any_groups (v51==2 & v54 auf (11,243)) — Sieg {Sieg()}, " +
                                     $"Nachfrist {ms.Grace}, beendet {ms.Ended}");
                    }
                    else sb.AppendLine("  KEIN URTEIL Sieg: (11,243) besetzt");
                }
                else Soll(false, "kein neuer Experte — Siegregel nicht erreichbar");
                break;
            }
            case 19:
            {
                if (V(40) == 0) { Eingriff("v40 := 1 (Kette Agent Bingham)"); Setze(40, 1); }
                Entity? g21 = null;
                foreach (var e in _entities) if (e.IsBuilding && e.Slot == 21 && !e.Dead) g21 = e;
                if (g21 != null) { Eingriff($"Gebaeude 21 auf {g21.HpMax / 10} TP (store_energy < 1/5)"); g21.Hp = g21.HpMax / 10; }
                Takte(1200, () => V(40) >= 2);
                var spion = Einheit(4000);
                Immer(spion != null, $"Regel 0x49EE0A: Agent Bingham (4000) gesetzt, v40 = {V(40)}");
                if (spion == null) break;
                // ⚠ das Geld VOR der Wartezeit merken: im alten Sofortweg zahlt
                // 0x49EF0F im selben Takt, in dem der Spion verschwindet.
                int g0 = geld;
                Takte(60 * 90, () => V(40) >= 3 || spion.Dead);
                Immer(V(40) >= 3, $"Spion auf Zeile {spion.Row} < 101 -> Regel 0x49EEC7 verkauft ihn (v40 = {V(40)})");
                Soll(!spion.Dead && ms.InSale != null && ms.InSale(4000),
                     "nach sell_unit steht der Spion noch in der Verkaufsliste und wartet auf den Frachter");
                bool weg = Abholen(spion, 60 * 90);
                Immer(weg, $"Spion vom Feld (tot/abgeholt: {spion.Dead})");
                Soll(V(98) == 5, $"v98 = {V(98)} (5 = Spieler 4 + 1, @0x4C0670)");
                Soll(V(104) == 1, $"v104 = {V(104)} (1 = Untermission 293 gescheitert, Regel 0x49EF4B)");
                Soll(geld - g0 == 0, $"Skriptgeld seit dem Verkauf {geld - g0} $ (0 — die 1000 $ von 0x49EF0F gehoeren dem Abschuss, nicht der Flucht)");
                break;
            }
            case 29:
            {
                Eingriff("v40 := 1, v41 := Uhr − 11 (Barry More kommt per space_in)");
                Setze(40, 1); Setze(41, ms.Minutes - 11);
                Takte(60 * 120, () => MitMarke(0, 194) != null);
                var more = MitMarke(0, 194);
                Immer(more != null, $"Barry More (194) gelandet: Platz {more?.Slot}");
                if (more == null) break;
                Eingriff("v41 := Uhr − 4");
                Setze(41, ms.Minutes - 4);
                Takte(400, () => V(40) == 3);
                Immer(V(40) == 3 && V(42) == more.Slot, $"Regel 0x4A3D46: v40 = {V(40)}, v42 = {V(42)}");
                Eingriff("More an das Lufthandelszentrum gestellt (x 29/32, y 152/155)");
                Stelle(more, new[] { (29, 152), (29, 155), (32, 152), (32, 155) });
                int g0 = geld;
                Takte(200, () => V(40) == 4);
                Soll(V(40) == 4 && !more.Dead, $"0x4A3D9E: More verkauft, wartet auf den Frachter (v40 = {V(40)})");
                Abholen(more, 60 * 90);
                Soll(more.Dead, "More abgeholt");
                Soll(V(98) == 1, $"v98 = {V(98)} (1 = Spieler 0 + 1)");
                Soll(geld - g0 == 5000 && V(104) == 10, $"Regel 0x4A3E27: +{geld - g0} $ (5000), v104 = {V(104)} (10)");
                break;
            }
            case 24:
            {
                if (V(10) != 1) { Eingriff("v10 := 1 (Untermission 340 laeuft)"); Setze(10, 1); }
                int idx = -1;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Comp0F == 171 &&
                        e.Owner is >= 0 and <= 7 && !Allied(view, e.Owner)) { idx = i; break; }
                }
                Immer(idx >= 0, $"Abwehrstellung (+0x0F == 171) eines Nichtverbuendeten: Platz {(idx >= 0 ? _entities[idx].Slot : -1)}");
                if (idx >= 0)
                {
                    int g0 = geld, n0 = V(11);
                    Eingriff($"Abwehrstellung Platz {_entities[idx].Slot} getoetet");
                    Kill(idx, _entities[idx], view, "untermission-check: Abwehrstellung");
                    Takte(150);
                    Soll(geld - g0 == 150 && V(11) == n0 + 1, $"v93 -> Regel 0x4A0E46: +{geld - g0} $ (150), v11 {n0} -> {V(11)}");
                }
                // UM6
                var w = Einheit(V(52));
                if (w == null || w.Mark != 192)
                {
                    int s = ms.PlaceUnit?.Invoke(192, 108, 99, 0) ?? -1;
                    Setze(52, s);
                    w = Einheit(s);
                    Eingriff($"Wiffer (192) per place_unit(192,108,99,0) -> v52 = {s}");
                }
                Eingriff("v50 := 2, v51 := Uhr − 2");
                Setze(50, 2); Setze(51, ms.Minutes - 2);
                int z0 = ziele, u6 = V(106);
                Takte(20, () => V(50) == 3);
                Soll(V(50) == 3 && texte.Contains(345), $"0x4A1482: Text 345, v50 = {V(50)} (3), v106 {u6} -> {V(106)}");
                Soll(ziele - z0 > 0, $"add_target-Schleife: {ziele - z0} Ziele fuer Spieler 4");
                if (w == null) break;
                if (alt && V(50) == 2) { Eingriff("v50 := 3 (Nullmodell: Start fehlt, Verkaufsregel trotzdem pruefen)"); Setze(50, 3); }
                Eingriff("Wiffer an die Luftstation gestellt (x 130/133, y 130/127)");
                Stelle(w, new[] { (130, 130), (130, 127), (133, 130), (133, 127) });
                int g1 = geld;
                Takte(200, () => V(50) == 4);
                Soll(V(50) == 4, $"0x4A17D3: Wiffer verkauft (v50 = {V(50)})");
                Abholen(w, 60 * 90);
                Soll(V(98) == 1 && geld - g1 == 2500, $"v98 = {V(98)} (1), Regel 0x4A185C +{geld - g1} $ (2500), v106 = {V(106)}");
                break;
            }
            case 31:
            {
                int n = 0, g0 = geld, z0 = V(32);
                for (int i = 0; i < _entities.Count && n < 4; i++)
                {
                    var e = _entities[i];
                    if (e.IsBuilding || e.IsProp || e.Dead || e.Comp0F != 171 ||
                        e.Owner is < 0 or > 7 || Allied(view, e.Owner)) continue;
                    Kill(i, e, view, "untermission-check: Abwehrstellung");
                    n++;
                    Takte(5);
                }
                Eingriff($"{n} Abwehrstellungen getoetet");
                Takte(150);
                Soll(n > 0 && geld - g0 == 250 * n && V(32) == z0 + n, $"v93 -> Regel 0x4A4906: +{geld - g0} $ ({250 * n}), v32 {z0} -> {V(32)}");
                var r15 = ms.WirkungenDerRegel(0x4A4A2E);
                Soll(r15.Contains("set_if_owner") && r15.Contains("set_time"),
                     $"Regel 0x4A4A2E traegt den Schwanz 0x4A4A63..0x4A4ABA: {string.Join("+", r15)}");
                int u5 = V(105);
                Eingriff("v50 := 1, v51 := Uhr − 21 (Regel 0x4A4A2E gelaufen, 20 Minuten vorbei)");
                Setze(50, 1); Setze(51, ms.Minutes - 21);
                Takte(150, () => V(50) == 2);
                Soll(V(50) == 2 && texte.Contains(415), $"0x4A4AC9: Text 415, v50 = {V(50)}, v105 {u5} -> {V(105)}");
                break;
            }
            case 23:
            {
                if (V(10) != 1) { Eingriff("v10 := 1 (Untermission 330 laeuft)"); Setze(10, 1); }
                int g0 = geld, n0 = V(11);
                Entity? b = null; int bi = -1;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.IsBuilding && !e.IsProp && !e.Dead && e.BType != 0 &&
                        e.Owner is >= 0 and <= 7 && !Allied(view, e.Owner)) { b = e; bi = i; break; }
                }
                Immer(b != null, $"nicht verbuendetes Gebaeude: Platz {b?.Slot}, Art {b?.BType}, Spieler {b?.Owner}");
                if (b == null) break;
                Eingriff($"Gebaeude Platz {b.Slot} gesprengt");
                Kill(bi, b, view, "untermission-check: Gebaeude");
                Takte(150);
                Soll(geld - g0 == 230 && V(11) == n0 + 1, $"v100 -> Regel 0x4A0D47: +{geld - g0} $ (230), v11 {n0} -> {V(11)}");
                break;
            }
            case 22:
            {
                Eingriff("v61 := 2 (Endbonus Stufe 2), v291 := 0");
                Setze(61, 2); Setze(291, 0);
                int objs = ms.ObjectCount?.Invoke(1, 1) ?? -1;
                Takte(150);
                Soll(V(107) != 10, $"UM 327 NICHT sofort erfuellt, solange Spieler 1 noch {objs} Gebaeude Art 1 hat (v107 = {V(107)})");
                int g0 = geld;
                int n = Sprenge(e => e.IsBuilding && e.BType == 1 && e.Owner == 1, "Gebaeude Art 1 von Spieler 1");
                Eingriff($"{n} Gebaeude Art 1 von Spieler 1 gesprengt");
                Takte(150);
                Soll(geld - g0 == 2500 && V(107) == 10 && V(291) == 1,
                     $"0x4A0C1A: +{geld - g0} $ (2500, @0x4A0C3C), v107 = {V(107)}, v291 = {V(291)}");
                break;
            }
            case 9:
            {
                if (V(1) != 1) { Eingriff("v1 := 1 (Untermission 191 laeuft)"); Setze(1, 1); }
                Eingriff("4 Treibstoffhelis (Typ 13) fuer Spieler 0 gesetzt");
                Heli(4, 0);
                int g0 = geld;
                Takte(250);
                Soll(V(230) == 4, $"0x49B9F8: v230 = {V(230)} (4)");
                Soll(geld - g0 == 1300, $"Regel 0x49B640: +{geld - g0} $ (1300)");
                Eingriff("v3 := 1, v4 := Uhr − 9 (Untermission 193 startet, Regel 0x49B88E setzt v5 := 2000)");
                Setze(3, 1); Setze(4, ms.Minutes - 9);
                Takte(150, () => V(3) == 2);
                Immer(V(3) == 2 && V(5) == 2000 && texte.Contains(193), $"Regel 0x49B88E: Text 193, v3 = {V(3)}, v5 = {V(5)}");
                int v5 = V(5);
                Eingriff($"v4 := Uhr − v6 − 13 (v6 = {V(6)}, v5 = {v5})");
                Setze(4, ms.Minutes - V(6) - 13);
                Takte(20, () => V(3) == 3);
                Soll(V(3) == 3 && texte.Contains(194) && V(5) == v5 / 2, $"0x49B8D5: Text 194, v3 = {V(3)}, v5 {v5} -> {V(5)}");
                int g1 = geld, bonus = V(5);
                // objects(1,1)==0 & objects(1,2)==0: Art 1 bei Spieler 1 UND Spieler 2.
                int n = Sprenge(e => e.IsBuilding && e.BType == 1 && (e.Owner == 1 || e.Owner == 2), "Gebaeude Art 1 von Spieler 1/2");
                Eingriff($"{n} Gebaeude Art 1 von Spieler 1 und 2 gesprengt");
                Takte(250);
                Soll(bonus > 0 && geld - g1 == bonus, $"Regel 0x49BB28: +{geld - g1} $ (v5 = {bonus}), v104 = {V(104)}");
                break;
            }
            case 12:
            {
                if (V(2) != 1) { Eingriff("v2 := 1 (Untermission 222 laeuft)"); Setze(2, 1); }
                Eingriff("2 Treibstoffhelis (Typ 13) fuer Spieler 0 gesetzt");
                Heli(2, 0);
                int g0 = geld;
                Takte(250);
                Soll(V(230) == 2, $"0x49C96B: v230 = {V(230)} (2)");
                Soll(geld - g0 == 500, $"Regel 0x49C9C4: +{geld - g0} $ (500)");
                break;
            }
            case 16:
            {
                Entity? p0 = null;
                foreach (var e in _entities) if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == 0 && e.Infantry < 0) { p0 = e; break; }
                Immer(p0 != null, $"Einheit des Spielers fuer imap(48,123): Platz {p0?.Slot}");
                if (p0 == null) break;
                Eingriff("Einheit des Spielers nach (48,123) gestellt (Anstoss der Regel 0x49DA81)");
                Stelle(p0, new[] { (48, 123) });
                int u2 = V(102);
                Takte(250, () => V(20) == 1);
                Takte(5);
                Soll(texte.Contains(261) && V(102) == u2 + 1, $"0x49DA81: Text 261, v102 {u2} -> {V(102)}, v21/22/23 = {V(21)}/{V(22)}/{V(23)}");
                var w = Einheit(V(21));
                Immer(w != null && w.Mark == 193, $"Wissenschaftler v21 = {V(21)} gesetzt");
                if (w == null) break;
                Eingriff($"Wissenschaftler Platz {w.Slot} -> Spieler 0, UKOL 50 (als stuende er in der Basis)");
                w.Owner = 0; w.Ukol = 50;
                int g0 = geld;
                Takte(250);
                Soll(geld - g0 == 100 && V(29) == 1 && w.Dead, $"0x49DEF2: +{geld - g0} $ (100), v29 = {V(29)}, entfernt {w.Dead}");
                break;
            }
            case 7:
            {
                var e0 = Einheit(0);
                Immer(e0 != null, $"Platz 0 des Spielers: ({e0?.Col},{e0?.Row})");
                if (e0 == null) break;
                Eingriff("Platz 0 in den Kasten 54<x<106, y>102 gestellt");
                Stelle(e0, Ring(60, 104).Where(z => z.Item1 is > 54 and < 106 && z.Item2 > 102));
                Takte(60, () => V(8) == 3);
                Soll(V(8) == 3 && texte.Contains(172) && V(6) == 1, $"0x49AD83: Text 172, v8 = {V(8)} (3), v6 = {V(6)}");
                var w = Einheit(7001);
                Immer(w != null, "Wissenschaftler 7001 auf der Karte");
                if (w == null) break;
                if (alt && V(8) == 0) { Eingriff("v8 := 3 (Nullmodell: Start fehlt, Bezahlung trotzdem pruefen)"); Setze(8, 3); }
                Eingriff("7001 -> Spieler 0, UKOL 50");
                w.Owner = 0; w.Ukol = 50;
                int g0 = geld;
                Takte(250);
                Soll(geld - g0 == 350 && V(9) == 1 && w.Dead, $"0x49B0CE: +{geld - g0} $ (350), v9 = {V(9)}, v8 = {V(8)}, entfernt {w.Dead}");
                break;
            }
            case 3:
            {
                int g0 = geld;
                Takte(200);
                if (V(59) == 0)
                {
                    Eingriff("Regel 0x499A06 erzwungen (Bedingung bridge(0)==0 = Brueckenplatz 0 frei, im Lauf nicht von selbst)");
                    ms.ErzwingeRegelFuerProbe(0x499A06);
                }
                Soll(geld - g0 == -20, $"Brueckenbau: {geld - g0} $ (−20 @0x499A20)");
                var h = Einheit(7000);
                Immer(h != null && h.Mark == 193, "Dr. Hemmerl (7000, Marke 193) auf der Karte");
                if (h == null) break;
                Eingriff("v101 := 10 (Hemmerl gefunden), 7000 -> Spieler 0, UKOL 50 (in der Basis)");
                Setze(101, 10); h.Owner = 0; h.Ukol = 50;
                int g1 = geld;
                Takte(100);
                Soll(texte.Contains(132) && geld - g1 == 1000 && V(301) == 1 && h.Dead,
                     $"0x499470: Text 132, +{geld - g1} $ (1000), v301 = {V(301)}, entfernt {h.Dead}");
                break;
            }
            case 5:
            {
                // Die Siegkette haengt an Gebaeudeplatz 0: Regel 0x49A49D merkt sich
                // dessen Lager W/F (v31/v32), sobald der Spieler ein Gebaeude der
                // Klasse 1 hat; 0x49A3E7/0x49A416 setzen v33/v34, wenn sie wachsen.
                Entity? b = null; int bi = -1;
                for (int i = 0; i < _entities.Count; i++)
                    if (_entities[i].IsBuilding && !_entities[i].IsProp && !_entities[i].Dead && _entities[i].Slot == 0) { b = _entities[i]; bi = i; }
                if (b != null && b.Owner != view)
                {
                    Eingriff($"Gebaeudeplatz 0 (Art {b.BType}) an Spieler {view} gegeben (statt Einnahme)");
                    b.Owner = view;
                }
                foreach (var e in _entities)
                    if (e.IsBuilding && !e.Dead && !e.IsProp && e.BType is 2 or 3 or 4 && e.Owner != view)
                    {
                        Eingriff($"Fabrik Platz {e.Slot} (Art {e.BType}) an Spieler {view} gegeben — buildings(Kl1,P0) > 0");
                        e.Owner = view;
                        break;
                    }
                Takte(250, () => V(30) == 1);
                Immer(V(30) == 1, $"Regel 0x49A49D: Siegmarken v31 = {V(31)}, v32 = {V(32)} gesetzt (v30 = {V(30)})");
                LoadDesigns();
                int nr = -1;
                if (_designs != null)
                    for (int k = 0; k < _designs.Count; k++)
                        if (_designs[k].CostW > 0 && _designs[k].CostF > 0) { nr = k; break; }
                Immer(b != null && nr >= 0, $"Gebaeude des Spielers Platz {b?.Slot} (Art {b?.BType}), Entwurf {nr}");
                if (b == null || nr < 0) break;
                int w0 = V(31), f0 = V(32);
                var d = _designs![nr];
                Eingriff($"Entwurf {nr} ({d.Name}, W{d.CostW} F{d.CostF} S{d.CostS}) ins Depot von Platz {b.Slot} gelegt und verwertet");
                b.Depot.Add(nr);
                _selected = bi;
                RecycleFromPanel(b.Garage.Count + b.Depot.Count - 1);
                Takte(150);
                Soll(V(31) == w0 + d.CostW && V(32) == f0 + d.CostF && V(190) == 0,
                     $"Regeln 0x49A37F/0x49A3A9: Siegmarke v31 {w0} -> {V(31)} (+{d.CostW}), v32 {f0} -> {V(32)} (+{d.CostF}), v190 = {V(190)}");
                Soll(V(33) == 0 && V(34) == 0, $"Recycling zaehlt NICHT als Lieferung: v33 = {V(33)}, v34 = {V(34)} (0/0 — sonst Sieg ohne Lieferung)");
                break;
            }
            case 11:
            case 15:
            {
                Entity? markt = null;
                foreach (var e in _entities) if (e.IsBuilding && !e.Dead && e.BType == 17) { markt = e; break; }
                Immer(markt != null, "Geschaeftszentrum vorhanden");
                if (markt == null) break;
                int t = 0;
                while (MarketShelf().Count == 0 && t < 3000) { SimTickFuerProbe(); t++; }
                Immer(MarketShelf().Count > 0, "Regal gefuellt");
                if (MarketShelf().Count == 0) break;
                Eingriff("Kontostand 1 000 000 $, Kauf von Regalzeile 0");
                Money(view, 1_000_000);
                int d0 = MarketDelivered;
                MarketBuy(markt, 0);
                Takte(60 * 120, () => MarketDelivered > d0);
                Immer(MarketDelivered > d0, $"Lieferung angekommen ({MarketDelivered - d0} Stueck)");
                Takte(250);
                int text = m == 11 ? 210 : 252;
                Soll(V(99) >= 1 && texte.Contains(text), $"v99 = {V(99)}, Text {text} {(texte.Contains(text) ? "gezeigt" : "fehlt")}");
                break;
            }
            case 30:
            {
                Takte(60 * 45, () => texte.Contains(400) || ms.Minutes > 6);
                Soll(texte.Contains(400) && V(10) == 1, $"0x4A41DC: Text 400 nach {ms.Minutes} Spielminuten, v10 = {V(10)}");
                int verl = _lossCount.Length > 0 ? _lossCount[0] : -1;
                Soll(V(11) == verl && V(12) > 0, $"v11 = {V(11)} (Verluste Spieler 0: {verl}), v12 = {V(12)}");
                Eingriff("v12 := Uhr − 21 (statt 20 Spielminuten zu warten)");
                Setze(12, ms.Minutes - 21);
                int g0 = geld;
                Takte(250);
                Soll(geld - g0 == 5000, $"Regel 0x4A4278: +{geld - g0} $ (5000)");
                break;
            }
            default:
                sb.AppendLine("  (fuer diese Mission ist kein Ablauf gebaut — nur die Wirkungsprobe)");
                break;
        }

        // Die Wirkungsprobe als Selbsttest.
        int unerklaert = Wirkungsprobe(sb, m);
        if (!alt) Immer(unerklaert == 0, $"Wirkungsprobe: {unerklaert} unerklaerte Stellen (0)");
        else sb.AppendLine($"  (Nullmodell: {unerklaert} unerklaerte Stellen — die nachgetragenen Wirkungen fehlen)");
        sb.AppendLine($"  Schreiber: v93 {V93Gesetzt}x, v99 {V99Gesetzt}x, v100 {V100Gesetzt}x, v190 {V190Gesetzt}x, " +
                      $"v98 {V98Gesetzt}x, Skriptverkaeufe {SkriptVerkaeufe}; Skriptgeld gesamt {geld} $, Texte {string.Join(",", texte.Distinct())}");

        ms.ShowText = alterText; ms.AddMoney = alterGeld; ms.AddTarget = alterZiel;
        sb.Append(fehler == 0
            ? $"untermission-check K{m}: {(alt ? "NULLMODELL BESTANDEN (alles fehlt)" : "BESTANDEN")}"
            : $"untermission-check K{m}: {fehler} FEHLER{(alt ? " (Nullmodell)" : "")}");
        return sb.ToString();
    }
}
