namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐⭐ 01.10.2026 — <b>DIE RAMPE DES RAKETENWERFERS</b> (bug-378). Gemeldet von
/// einem Spieler: »Wenn Raketenwerfer schießen, machen sie eine Animation, diese
/// fehlt. Der Raketenwerfer sollte hoch gehen vor dem Schießen.«
///
/// <para>Lesung: <c>berichte/raketenwerfer-animation-fable.md</c>. Der Zeichner
/// <c>0x429900</c> wählt die Turmgruppe (0 = flach, 1 = aufgerichtet bzw. Rakete
/// liegt auf) für genau fünf Bauteile — 26, 27, 28, 35, 36, dieselben fünf, die in
/// ROBO.CWR zwei Gruppen tragen — und zwar <b>allein aus dem Nachladezähler NABYTO
/// <c>+0x32</c> gegen die Nachladezeit RELOAD <c>+0x3D</c></b>:</para>
/// <code>
///   0x429D8F / 0x42A20C  (26, 27, 35, 36)   NABYTO > RELOAD−8  oder  NABYTO &lt; 5  -> dx = 0x30
///   0x429E37 / 0x42A2B4  (28)               NABYTO &lt; 5                          -> dx = 0x30
/// </code>
/// <para><c>0x30</c> = 48 Bilder = eine Gruppe. Kein Zwischenbild, kein eigener
/// Posenzustand, und der Schuss wartet auf nichts (Sperre @0x40BC78 prüft nur
/// NABYTO == 0) — die Rampe geht 4 Takte vor der Bereitschaft hoch und bleibt
/// oben, bis geschossen wird.</para>
///
/// <para>⚠ UNSERE Setzung bleibt: NABYTO ist bei uns <c>Cooldown</c> in Sekunden,
/// umgerechnet über <see cref="ReloadTick"/>; und ohne den Zufallsanteil
/// <c>+ zufall&amp;3</c> (@0x40C46C) dauert die erste Phase genau 8 statt 8…11 Takte.</para>
///
/// <para>Gegenschalter <c>--werferpose-alt</c>: die alte Munitionsregel
/// <see cref="TurmLadePose"/> (Stand bis 30.09.2026). Prüfstand
/// <c>--werferpose-check</c> (mit <c>--shot=…</c> zusätzlich zwei Bilder).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--werferpose-alt</c> — die Gegenprobe: Turmgruppe nach Munition
    /// (<see cref="TurmLadePose"/>), wie bis zum 30.09.2026.</summary>
    public static bool WerferPoseAlt;

    /// <summary><c>--werferpose-check</c> — siehe <see cref="WerferPoseTakt"/>.</summary>
    public static bool WerferPoseCheckAn;

    /// <summary>NABYTO in Originaltakten (0 = bereit), aus der Restzeit.
    /// ⚠ Der kleine Abzug fängt den Gleitkommarest von <c>Cooldown -= dt</c>
    /// ab — sonst stünde nach k Takten R−k+1 statt R−k da.</summary>
    private static int Nabyto(Entity e)
    {
        // ⭐ 02.10.2026, bug-385 — DIE MUNITIONSSPERRE (Opus-Gegenlesung,
        // berichte/werferpose-gegenlesung-opus.md, W1): ein Fahrzeug mit Waffe
        // und Munition +0x39 == 0 bekommt NABYTO jeden Takt fest auf 10
        // (C 0x4074EC, F 0x407415). Ein leer geschossener Werfer bleibt damit
        // FLACH und LEER — bei uns lief Cooldown auf 0 und zeigte Raketen.
        // Gegenschalter --werferpose-fable (Regel der ersten Lesung, ohne Sperre).
        if (!WerferPoseFable && e.Infantry < 0 && e.Weapon != 0 && e.AmmoMax > 0 && e.Ammo <= 0)
            return 10;
        return e.Cooldown <= 0 ? 0 : Mathf.Max(0, Mathf.CeilToInt(e.Cooldown / ReloadTick - 1e-3f));
    }

    /// <summary><c>--werferpose-fable</c>: Posenregel ohne Munitionssperre, Stand
    /// bug-378 (01.10.2026).</summary>
    public static bool WerferPoseFable;

    /// <summary>
    /// ⭐⭐ <b>Die Gruppe des Turmbilds wie 0x429D8F/0x429E37</b> — nur aus
    /// Nachladezähler und Nachladezeit, für beide Zeichenwege (gewöhnlicher Rumpf
    /// und Hangweg, befehlsgleich). Fehlt das Bild der Gruppe 1, fällt
    /// <see cref="GetTurretTexture"/> selbst auf Gruppe 0 zurück.
    /// </summary>
    private static int TurmGruppe(Entity e)
    {
        if (WerferPoseAlt) return TurmLadePose(e);                // Stand bis 30.09.2026
        int nab = Nabyto(e);
        switch (e.Weapon)
        {
            case 26 or 27 or 35 or 36:                            // 0x429D8F / 0x42A20C
                return (nab > e.Reload - 8 || nab < 5) ? 1 : 0;
            case 28:                                              // 0x429E37 / 0x42A2B4
                return nab < 5 ? 1 : 0;
            default: return 0;
        }
    }

    // ---- der Prüfstand -------------------------------------------------------

    private sealed class WerferProbe
    {
        public int Idx, Target = -1;
        public float LastCd = -1;
        public int Shots;
        public List<int> Lauf = new();          // Gruppen seit dem letzten Schuss
        public List<List<int>> Zyklen = new();  // vollständige Schuss-zu-Schuss-Folgen
        public int Reload;
        public int Mzf48Bei = -1;               // Effekte »mzf48« gleich nach dem Schuss
    }

    private List<WerferProbe>? _wpProben;
    /// <summary>Leerphase (bug-385): Takte seit Munition 0, und die Gruppen darin.</summary>
    private int _wpLeerTakt = -1;
    private readonly List<int> _wpLeer = new();
    private int _wpStart = -1;
    private bool _wpFertig;

    /// <summary>Für den Bildlauf in MapViewer: die erste Probe-Einheit, ihre
    /// aktuelle Gruppe und ob sie mindestens einmal geschossen hat.</summary>
    public (Vector2 pos, int gruppe, int schuesse, bool fertig)? WerferPoseStand()
    {
        if (_wpProben == null || _wpProben.Count == 0) return null;
        var p = _wpProben[0];
        var e = _entities[p.Idx];
        return (e.Pos, TurmGruppe(e), p.Shots, _wpFertig);
    }

    /// <summary>
    /// <c>--werferpose-check</c> — aus <c>SimTick</c>, einmal je Takt, VOR dem
    /// Gefecht des Takts (also der Zustand, den der Zeichner nach dem letzten Takt
    /// zeigt).
    ///
    /// <para>Nimmt echte Karteneinheiten mit Turm 26 oder 27 (und, falls auf der
    /// Karte, 28), gibt jeder einen befohlenen Angriff auf den nächsten Feind und
    /// lässt sie über den GEWÖHNLICHEN Waffenpfad (<c>UpdateCombat</c> →
    /// <c>Cooldown = ReloadOf(e)</c>) schiessen. ⚠ EINGRIFFE, und nur diese: der
    /// Angriffsbefehl, volle Munition, und ein neues Ziel, wenn das alte fällt —
    /// Cooldown und Gruppe fasst der Prüfstand nicht an.</para>
    ///
    /// <para><b>SOLL</b> je Schuss-zu-Schuss-Folge (R = RELOAD): 26/27 —
    /// <b>8 Takte Gruppe 1, dann R−12 Takte Gruppe 0, dann Gruppe 1 ab Takt R−4 bis
    /// zum Schuss</b>; 28 — Gruppe 0 bis Takt R−4, dann 1. Nullmodell
    /// <c>--werferpose-alt</c>: 26/27 durchgehend 0, 28 durchgehend 1 (Munition) —
    /// muss DURCHFALLEN. Dazu: <c>Effects/mzf48</c> hat 7 Bilder und wird beim
    /// Schuss eines 26/27 wirklich angelegt.</para>
    /// </summary>
    private void WerferPoseTakt()
    {
        if (!WerferPoseCheckAn || _wpFertig) return;
        if (_wpProben == null)
        {
            if (_taktNr < 20) return;
            _wpProben = new();
            var genommen = new HashSet<int>();
            // erst ein Werfer mit Feind im Schussfenster, sonst der erste mit irgendeinem Feind
            foreach (int[] satz in new[] { new[] { 26, 27 }, new[] { 28 } })
                foreach (bool fenster in new[] { true, false })
                {
                    if (_wpProben.Any(p => satz.Contains(_entities[p.Idx].Weapon))) break;
                    for (int i = 0; i < _entities.Count; i++)
                    {
                        var e = _entities[i];
                        if (e.Dead || e.IsProp || e.IsBuilding || !satz.Contains(e.Weapon)) continue;
                        int t = WerferZiel(e, fenster);
                        if (t < 0) continue;
                        _wpProben.Add(new WerferProbe { Idx = i, Target = t, Reload = e.Reload });
                        genommen.Add(i);
                        break;
                    }
                }
            _wpStart = _taktNr;
            if (_wpProben.Count == 0)
            {
                GD.Print("werferpose-check: keine Einheit mit Turm 26/27/28 und erreichbarem Feind — NICHT GEMESSEN");
                _wpFertig = true; WerferPoseEnde(); return;
            }
            foreach (var p in _wpProben)
            {
                var e = _entities[p.Idx];
                if (e.AmmoMax > 0) e.Ammo = e.AmmoMax;
                e.Target = p.Target; e.Ordered = true;
                GD.Print($"werferpose-check: Einheit {p.Idx} Turm {e.Weapon} Besitzer {e.Owner} ({e.Col},{e.Row}) "
                       + $"RELOAD {e.Reload} cd {e.Cooldown:0.000} -> Ziel {p.Target} "
                       + $"(Besitzer {_entities[p.Target].Owner}) bei Takt {_taktNr}"
                       + (WerferPoseAlt ? "   [--werferpose-alt: Nullmodell]" : ""));
            }
            return;
        }

        foreach (var p in _wpProben)
        {
            var e = _entities[p.Idx];
            if (e.Dead) continue;
            var tz = p.Target >= 0 ? _entities[p.Target] : null;
            if (tz == null || tz.Dead)
            {
                p.Target = WerferZiel(e);
                if (p.Target < 0) p.Target = WerferZiel(e, false);
                if (p.Target >= 0) { e.Target = p.Target; e.Ordered = true; }
            }
            if (e.AmmoMax > 0 && e.Ammo <= 0) e.Ammo = e.AmmoMax;
            bool schuss = p.LastCd >= 0 && e.Cooldown > p.LastCd + ReloadTick * 0.5f;
            p.LastCd = e.Cooldown;
            if (schuss)
            {
                if (p.Shots > 0) p.Zyklen.Add(p.Lauf);
                p.Lauf = new();
                p.Shots++;
                p.Reload = e.Reload;
                if (p.Mzf48Bei < 0 && e.Weapon is 26 or 27)
                    p.Mzf48Bei = _effects.Count(f => f.Kind == "mzf48");
            }
            if (p.Shots > 0) p.Lauf.Add(TurmGruppe(e));
            // Bildlauf: im gewünschten Zustand die Spielzeit anhalten (GameSpeed 0 =
            // Pause der Geschwindigkeitsschleife), damit das Bild GENAU diesen Takt zeigt.
            if (WerferPoseBildLaeuft && p == _wpProben[0] && p.Shots > 0
                && WerferPoseHaltGruppe >= 0 && TurmGruppe(e) == WerferPoseHaltGruppe)
            {
                WerferPoseHaltGruppe = -1;
                GameSpeed = 0;
            }
        }

        bool genug = _wpProben.All(p => p.Zyklen.Count >= 2 || _entities[p.Idx].Dead);
        if (genug || _taktNr - _wpStart > 3000) WerferPoseAuswerten();
    }

    /// <summary>bug-385: nach den Folgen 120 Takte mit Munition 0, ohne Nachfuellen.
    /// Soll durchgehend Gruppe 0 (Sperre 0x4074EC).</summary>
    private bool WerferLeerTakt()
    {
        var p = _wpProben!.FirstOrDefault(q => !_entities[q.Idx].Dead && _entities[q.Idx].AmmoMax > 0);
        if (p == null) return false;
        var e = _entities[p.Idx];
        if (_wpLeerTakt < 0) { _wpLeerTakt = 0; _wpLeer.Clear(); }
        e.Ammo = 0;
        _wpLeer.Add(TurmGruppe(e));
        return ++_wpLeerTakt < 120;
    }

    /// <summary>Der nächste Feind, der schon im Schussfenster liegt (sonst müsste
    /// die Einheit erst hinfahren, und ein weiter Weg misst die Wegfindung statt
    /// der Rampe — beim ersten Lauf in K21 schoss der Turm-27-Werfer so nie).
    /// <paramref name="nurImFenster"/> false: der nächste überhaupt.</summary>
    private int WerferZiel(Entity e, bool nurImFenster = true)
    {
        int best = -1; float bd = float.MaxValue;
        for (int j = 0; j < _entities.Count; j++)
        {
            var t = _entities[j];
            if (t.Dead || t.IsProp || t.Hp <= 0 || Allied(e.Owner, t.Owner)) continue;
            if (t.Owner < 0 || t.Owner > 7) continue;
            if (nurImFenster && !InFiringWindow(e, CellDistance(e, t))) continue;
            float d = e.Pos.DistanceSquaredTo(t.Pos);
            if (d < bd) { bd = d; best = j; }
        }
        return best;
    }

    private void WerferPoseAuswerten()
    {
        if (WerferLeerTakt()) return;                 // bug-385: erst die Leerphase
        _wpFertig = true;
        var sb = new System.Text.StringBuilder("werferpose-check\n");
        bool alle = true;
        int gemessen = 0;
        foreach (var p in _wpProben!)
        {
            var e = _entities[p.Idx];
            int r = p.Reload;
            bool werfer28 = e.Weapon == 28;
            sb.Append($"  Einheit {p.Idx} Turm {e.Weapon} RELOAD {r}: {p.Shots} Schuesse, {p.Zyklen.Count} volle Folgen\n");
            // ⚠ Ein Werfer, der in 3000 Takten nicht zweimal schoss (weiter Weg zum Feind), zählt als
            // UNGEMESSEN, nicht als bestanden — und ohne einen gemessenen fällt der Lauf durch.
            if (p.Zyklen.Count == 0) { sb.Append("    ⚠ keine volle Folge (kein zweiter Schuss) — UNGEMESSEN\n"); continue; }
            gemessen++;
            if (r <= 12) sb.Append("    ⚠ RELOAD ≤ 12 — die Dreiphasenfolge ist hier entartet\n");
            int zi = 0;
            foreach (var z in p.Zyklen)
            {
                // Lauflängen: Gruppe×Anzahl
                var runs = new List<(int g, int n)>();
                foreach (int g in z)
                    if (runs.Count > 0 && runs[^1].g == g) runs[^1] = (g, runs[^1].n + 1);
                    else runs.Add((g, 1));
                int erste1 = z.IndexOf(1);
                int abHoch = -1;                        // erster Takt der letzten 1er-Strecke
                for (int k = z.Count - 1; k >= 0 && z[k] == 1; k--) abHoch = k;
                bool ok;
                string soll;
                if (werfer28)
                {
                    soll = $"0×{r - 4}, dann 1 ab Takt {r - 4}";
                    ok = runs.Count == 2 && runs[0] == (0, r - 4) && runs[1].g == 1 && runs[1].n >= 4;
                }
                else
                {
                    soll = $"1×8, 0×{r - 12}, dann 1 ab Takt {r - 4}";
                    ok = runs.Count == 3 && runs[0] == (1, 8) && runs[1] == (0, r - 12)
                         && runs[2].g == 1 && abHoch == r - 4;
                }
                string folge = string.Join(" ", runs.Select(x => $"{x.g}×{x.n}"));
                sb.Append($"    Folge {++zi} ({z.Count} Takte): {folge}   SOLL {soll} -> {(ok ? "ok" : "⚠ abweichend")}"
                        + $"{(erste1 < 0 ? " (nie Gruppe 1)" : "")}\n");
                alle &= ok;
            }
            if (!werfer28)
            {
                int bilder = EffectFrames("mzf48").Count;
                sb.Append($"    Muendungsfeuer: Effects/mzf48 {bilder} Bilder (Soll 7), beim ersten Schuss "
                        + $"{Mathf.Max(0, p.Mzf48Bei)} mzf48-Effekte angelegt"
                        + (bilder == 7 && p.Mzf48Bei > 0 ? " -> ok" : " -> ⚠ fehlt (--reexport-effects?)") + "\n");
                alle &= bilder == 7 && p.Mzf48Bei > 0;
            }
            // Bild vorhanden? Gruppe 1 muss ein ANDERES Bild liefern als Gruppe 0.
            int aim = TurmRichtung(e);
            var t0 = GetTurretTexture(e.Weapon, aim, 0, 0);
            var t1 = GetTurretTexture(e.Weapon, aim, 0, 1);
            bool eigen = t1 != null && t0 != null && !ReferenceEquals(t0, t1);
            sb.Append($"    Turmbild Gruppe 1: {(eigen ? "eigenes Bild (turret/" + e.Weapon + "/g1)" : "⚠ FEHLT — Rueckfall auf Gruppe 0 (--reexport-units?)")}\n");
            alle &= eigen;
        }
        sb.Append(WerferPoseAlt ? "  [--werferpose-alt: Nullmodell, MUSS durchfallen]\n" : "");
        alle &= gemessen > 0;
        // bug-385: Leerphase — Munition 0, soll durchgehend flach und leer sein.
        int leerHoch = _wpLeer.Count(g => g == 1);
        bool leerOk = _wpLeer.Count > 0 && leerHoch == 0;
        sb.Append($"  Leerphase (Munition 0, {_wpLeer.Count} Takte): Gruppe 1 in {leerHoch} Takten, "
                + $"SOLL 0 (Munitionssperre 0x4074EC) -> {(leerOk ? "ok" : "⚠ abweichend")}"
                + (WerferPoseFable ? "   [--werferpose-fable: Nullmodell, MUSS abweichen]" : "") + "\n");
        alle &= leerOk;
        sb.Append($"  {gemessen} von {_wpProben!.Count} Werfern gemessen\n");
        sb.Append(alle ? "  BESTANDEN" : "  ⚠ DURCHGEFALLEN");
        GD.Print(sb.ToString());
        WerferPoseEnde();
    }

    /// <summary>Ohne Bildlauf endet der kopflose Lauf hier selbst; mit
    /// <c>--shot=</c> beendet ihn MapViewer.WerferPoseBildLauf.</summary>
    public static bool WerferPoseBildLaeuft;

    /// <summary>Bildlauf: bei welcher Gruppe der erste Probe-Werfer die Spielzeit
    /// anhält (−1 = nicht anhalten).</summary>
    public static int WerferPoseHaltGruppe = -1;

    private void WerferPoseEnde()
    {
        if (!WerferPoseBildLaeuft) GetTree().Quit(0);
    }
}
