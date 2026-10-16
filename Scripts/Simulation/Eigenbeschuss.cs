namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <b>Eigenbeschuss (»Friendly Fire«)</b> — bug-552/553, KayelGee 14.10.: »Friendly Fire
/// scheint aktuell nicht möglich zu sein. Im Original ist das möglich.« Schalter und der
/// Pruefstand <c>--eigenbeschuss-check</c>; die Regeln selbst stehen an ihren Stellen
/// (IstAngriffsziel, StrgAngriffVerweigert, Infanterieuhr, Geschossflug).
/// </summary>
public partial class MapEntityLayer
{
    public static bool EigenbeschussCheck;

    /// <summary><c>--eigenangriff-alt</c> — Stand 0.6.9: eigene Einheiten sind auch mit
    /// Strg kein Ziel, und die Strg-Sperre trifft den einzeln GEWAEHLTEN Fusssoldaten
    /// (die Lesart vom 15.09.) statt den Fusssoldaten als ZIEL. bug-552.</summary>
    public static bool EigenangriffAlt;

    /// <summary><c>--gebaeude-durchflug-alt</c> — Stand 0.6.9: Geschosse fliegen durch
    /// eigene und verbuendete Gebaeude hindurch. bug-553.</summary>
    public static bool GebaeudeDurchflugAlt;

    /// <summary>Wie oft ein Geschoss an etwas NICHT Feindlichem haltmachte.</summary>
    public static int EigenbeschussHalt, EigenTreffer;

    // =====================================================================
    //  --eigenbeschuss-check (bug-552/553)
    // =====================================================================
    public string EigenbeschussPruefung()
    {
        var sb = new StringBuilder("eigenbeschuss-check"
            + (EigenangriffAlt ? "  ⚠ NULLMODELL --eigenangriff-alt (Aussagen 1 und 2 MUESSEN scheitern)" : "")
            + (GebaeudeDurchflugAlt ? "  ⚠ NULLMODELL --gebaeude-durchflug-alt (Aussage 3 MUSS scheitern)" : "") + "\n");
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        bool nebelVor = PickOhneNebel; PickOhneNebel = true;
        Schussgruende = true;

        int me = ViewPlayer;
        var fz = new List<int>(); int fi = -1, gb = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.Owner != me || Untergestellt(e)) continue;
            if (e.IsBuilding) { if (gb < 0 && e.HpMax > 0 && e.Bauzustand < BauzustandStart) gb = i; continue; }
            if (e.Infantry >= 0) { if (fi < 0) fi = i; continue; }
            if (e.Mobile && e.GameUnitType == 0) fz.Add(i);
        }
        int vi = fz.Find(i => CanFight(_entities[i]) && _entities[i].Weapon > 0 && _entities[i].Range >= 3);
        int wi = fz.Find(i => i != vi && CanFight(_entities[i]));
        if (wi < 0) wi = fz.Find(i => i != vi);
        if (vi < 0 || wi < 0) return sb.Append($"  zu wenig eigene Fahrzeuge (Schuetze {vi}, Ziel {wi})\n  DURCHGEFALLEN").ToString();
        var v = _entities[vi]; var w = _entities[wi];
        sb.AppendLine($"  Spieler {me}: Schuetze Platz {v.Slot} (Waffe {v.Weapon}, Reichweite {v.Range}), eigenes Ziel Platz {w.Slot}"
                    + (fi >= 0 ? $", eigener Fusssoldat Platz {_entities[fi].Slot}" : "") + (gb >= 0 ? $", eigenes Gebaeude {BuildingName(_entities[gb])}" : ""));

        // ---- 1. Strg auf das eigene Fahrzeug: Ziel und Treffer -----------------------
        var pw = ProbeFreiNahe(v.Col, v.Row, 2, 3, wi);
        if (pw == null) return sb.Append("  kein Platz neben dem Schuetzen\n  DURCHGEFALLEN").ToString();
        ProbeVersetzen(wi, pw.Value.X, pw.Value.Y);
        ProbeRuhe(v); ProbeRuhe(w);
        _sel.Clear(); _sel.Add(vi); _selected = vi;
        int hp0 = w.Hp, et0 = EigenTreffer;
        StrgRechtsklick(w.Pos, false);
        int t1 = 0;
        while (t1 < 60 && v.Target != wi) { SimTickFuerProbe(); t1++; }
        int t2 = 0;
        while (v.Target == wi && t2 < 3000 && w.Hp >= hp0 && !w.Dead) { SimTickFuerProbe(); ProbeRuhe(w); t2++; }
        Soll(v.Target == wi || w.Hp < hp0 || w.Dead,
             $"Fahrzeug, Strg auf das EIGENE Fahrzeug: Ziel {v.Target} (Soll {wi}) nach {t1} Takten, »{_order}«");
        Soll(EigenTreffer > et0 || w.Hp < hp0 || w.Dead, $"  ... und trifft: {EigenTreffer - et0} Treffer (ApplyHit), TP {hp0} -> {w.Hp} nach {t2} Takten (Schussgrund »{v.Schussgrund}«)");
        w.Hp = System.Math.Max(w.Hp, 1);

        // ---- 2. Strg auf den eigenen Fusssoldaten: nichts --------------------------------
        if (fi < 0) sb.AppendLine("  ⚠ kein eigener Fusssoldat — Aussage 2 ungeprueft");
        else
        {
            var f = _entities[fi];
            var pf = ProbeFreiNahe(v.Col, v.Row, 2, 4, fi);
            if (pf != null) ProbeVersetzen(fi, pf.Value.X, pf.Value.Y);
            ProbeRuhe(v); ProbeRuhe(f);
            int verw0 = StrgFusssoldatVerweigert, fhp = f.Hp;
            StrgRechtsklick(f.Pos, false);
            for (int t = 0; t < 100; t++) SimTickFuerProbe();
            Soll(StrgFusssoldatVerweigert == verw0 + 1 && v.Target < 0 && v.AngriffsZelle == null && f.Hp >= fhp,
                 $"Fahrzeug, Strg auf den EIGENEN Fusssoldaten: verweigert {StrgFusssoldatVerweigert - verw0}x (Soll 1, 0x4374B4), "
                 + $"Ziel {v.Target}, Bodenziel {(v.AngriffsZelle?.ToString() ?? "-")}, TP {fhp} -> {f.Hp}");
        }
        ProbeRuhe(v);

        // ---- 3. ein Geschoss ueber ein eigenes Gebaeude: es schlaegt ein ------------------
        // ---- 4. Gegenprobe: ueber ein eigenes Fahrzeug fliegt es hinweg (0x452944) --------
        foreach (var (wer, idx) in new[] { ("Gebaeude", gb), ("Fahrzeug", wi) })
        {
            if (idx < 0) { sb.AppendLine($"  ⚠ kein eigenes {wer} — Aussage ungeprueft"); continue; }
            var z = _entities[idx];
            int zc = -1, zr = -1;
            for (int c = z.Col - 6; c <= z.Col + 6 && zc < 0; c++)
                for (int r = z.Row - 6; r <= z.Row + 6 && zc < 0; r++)
                    if (_nav.OccupantAt(c, r) == idx
                        && _nav.OccupantAt(c - 3, r) != idx && _nav.OccupantAt(c + 3, r) != idx) { zc = c; zr = r; }
            if (zc < 0) { sb.AppendLine($"  ⚠ keine Zelle des {wer}s gefunden"); continue; }
            int hpZ = z.Hp, halt0 = EigenbeschussHalt;
            float hoehe = ElevOf(zc, zr) * 15f + 10f;          // tief: unter Boden + Schwelle (15 bzw. 60)
            var start = CellCenter(zc - 3, zr); var ziel = CellCenter(zc + 3, zr);
            _shots.Add(new Projectile
            {
                Pos = start, Aim = ziel, Target = -1, Shooter = vi, Damage = 40,
                Facing = DirToFacing(ziel - start), Kind = null, Art = 1,
                Speed = 3 * PxPerProjectileSpeed, Weite = start.DistanceTo(ziel), Scheitel = 0f,
                HoeheStart = hoehe, HoeheZiel = hoehe,
            });
            int n0 = _shots.Count;
            for (int t = 0; t < 200 && _shots.Count >= n0; t++) { SimTickFuerProbe(); if (!z.IsBuilding) ProbeRuhe(z); }
            bool getroffen = EigenbeschussHalt > halt0;   // TP allein ist kein Beleg (Reparatur, Fremdtreffer)
            if (wer == "Gebaeude")
                Soll(getroffen, $"Geschoss in Hoehe {hoehe:0} ueber das eigene Gebaeude (Zelle {zc},{zr}): "
                              + $"Halt {EigenbeschussHalt - halt0}x, TP {hpZ} -> {z.Hp} (Soll getroffen, 0x452D1E ohne Buendnisfrage)");
            else
                Soll(!getroffen, $"Gegenprobe: Geschoss ueber das eigene Fahrzeug (Zelle {zc},{zr}): "
                               + $"Halt {EigenbeschussHalt - halt0}x, TP {hpZ} -> {z.Hp} (Soll durchgeflogen, 0x452944)");
        }

        _sel.Clear(); _selected = -1;
        PickOhneNebel = nebelVor;
        sb.AppendLine("  ⚠ EINGRIFFE: ein eigenes Fahrzeug und ein Fusssoldat werden neben den Schuetzen versetzt; "
                    + "Aussagen 3/4 legen ein Probegeschoss an (Art 1, Schaden 40, tief)");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
