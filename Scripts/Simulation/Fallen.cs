namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE FALLE</b> (30.10.2026, bug-697) — das zweite Feld von »Mines and traps«.
/// Gelesen in beiden GAME.EXE (berichte/fehlende-funktionen-opus.md §1 und
/// berichte/bau-fallen-legeauftrag.md), selbst nachgelesen:
///
/// <code>
/// Feld     C 0x688B58 / F 0x687BB8, 500 x 6 B, wie die Mine:
///          +0 Spalte, +1 Zeile, +2 rand%20+10, +3 rand%10+5, +4 belegt, +5 Leger
/// Legen    lay_trap C 0x421A40 / F 0x420C00 — dasselbe Tor wie lay_mine:
///          imap == 0xFFFE oder imap &lt;= 8000 (ja, 0x1F40); sonst »Too many traps«
///          @0x4F91AC. 11 Rufer: Fallenarm 0x4089A4 + 10 im SETUP (M20 4, M25 6)
/// Takt     C 0x4217C6..0x421882 / F 0x420986..0x420A42, NACH den Minen im selben
///          Block 0x4216F0, jeden Takt. Fuer jede belegte Falle:
///            imap &lt; 8000 (nur Fahrzeug) · T[opfer/1000, Leger] == 0 (feindlich)
///            · +0x0E != 0x45 (kein Fallenraeumer) · KOLIK +0x06 &lt; 0
///            · und −(+0x20) &lt; KOLIK  (der LETZTE Takt vor der Zellmitte)
///          -> Falle frei, word[+0x20] := 0. Kein Schaden, kein Klang, kein Effekt.
/// Raeumen  Zellankunft C 0x407A7F: +0x0E == 0x45 -> 0x421ED0(x, y, Spieler)
///          (F 0x421090): erste FEINDLICHE Falle der Zelle frei, Klang 41, Modus 2
/// Zeichnen Zeichenliste C 0x42F4A0 (F 0x42E650), Korb Zeile+2, Art 16, nur
///          SICHTBARE Zellen (sec50 0x678B58); Zeichner C 0x42CFAD: eigene/verb.
///          Folge 94 + Gelaendeklasse, sonst Schatten von Folge 61 (1/1000: 94)
/// Nebel    C 0x420945..0x4209A7: verbuendete Fallen decken ihre Zelle auf, Radius 0
/// </code>
///
/// <para>⭐ <b>Bleibt das Fahrzeug fuer immer stehen?</b> Ja, solange die Mission
/// laeuft. <c>+0x20</c> hat in C und F je <b>12</b> Schreiber (Suche nach dem
/// Satzfeld 0x6E26E8 / 0x6E1748): Aufbau (5), Tod, Plasmatreffer (:= max(2, v/2)
/// und := 2), die Falle, und drei Stellen, die den ENTWURFSWERT zurueckschreiben —
/// <c>0x43B190</c> = <c>place_carry</c> (Mitnahme in die naechste Mission),
/// <c>0x43C120</c> = Ausdocken aus der Basis, <c>0x43C6F0</c> = Ausdocken aus dem
/// Depot. Alle drei verlangen, dass die Einheit vorher in einem Stapel oder einer
/// Garage war; eine Einheit mit Tempo 0 kommt dort nicht hin. Im Bewegungstakt
/// schreibt niemand. ⇒ Sie steht, bis sie stirbt — es sei denn, ein PLASMAWERFER
/// trifft sie: dann faehrt sie mit Tempo 2 weiter (0x40CAA0, die Untergrenze).</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN:</b></para>
/// <list type="bullet">
/// <item>Tempo 0 ist bei uns »kein eigener Wert« (<c>RawSpeedOf</c> faellt auf das
///   Rumpftempo zurueck). Darum traegt die Einheit zusaetzlich
///   <see cref="Entity.Gefangen"/>; <c>RawSpeedOf</c> gibt bei
///   <c>Gefangen &amp;&amp; Speed == 0</c> eine 0. Schreibt jemand das Tempo zurueck
///   (Plasma, Ausdocken), faehrt sie von selbst wieder.</item>
/// <item>»−(+0x20) &lt; KOLIK &lt; 0« heisst bei uns: das Ziel des laufenden Schrittes
///   ist die Fallenzelle, die halbe Strecke ist gefahren (<see cref="MinenEinfahrt"/>),
///   und der Zuwachs DIESES Taktes wuerde den Schritt vollenden. Eine Einheit
///   Progress = 1/50 000 KOLIK, also exakt.</item>
/// <item>Bei uns wechselt Col/Row erst bei der Ankunft: die gefangene Einheit steht
///   auf ihrer alten Zelle und haelt die Fallenzelle vorgemerkt.</item>
/// <item>Das Aufflackern (1/1000) wuerfelt einen eigenen Bildwuerfel wie bei den
///   Minen (MinenBild.cs), nicht den Simulationszufall.</item>
/// </list>
/// <para>Gegenschalter <c>--fallen-aus</c>: keine Missionsfallen, kein Takt, kein
/// Raeumen. Pruefstand <c>--fallen-check</c> (K25).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>500 Plaetze — <c>cmp cx, 0x1F4</c> @0x42187D, @0x421A87.</summary>
    public const int UrFallen = 500;

    /// <summary><c>+0x0E</c> des Fallenraeumers — @0x42184B <c>cmp al, 0x45</c>,
    /// Zellankunft @0x407A7F.</summary>
    public const int FallenImmun = 0x45;

    /// <summary>Klang 41 »Falle geraeumt« — <c>push 0x29</c> @0x421F4B (F 0x42110B).</summary>
    public const int FallenRaeumKlang = 41;

    /// <summary><c>--fallen-aus</c> — der Stand bis 30.10.2026.</summary>
    public static bool FallenAus;

    private readonly Mine[] _fallen = new Mine[UrFallen];

    public int FallenGelegt, FallenKeinPlatz, FallenAusgeloest, FallenGeraeumt;
    public int FallenFreund, FallenImmunFall, FallenTorZu, FallenFussvolk;

    /// <summary>Wen die Fallen gefangen haben (Einheitenindex) — fuer den Pruefstand.</summary>
    public readonly List<int> FallenOpfer = new();

    public void FallenLeeren()
    {
        for (int i = 0; i < _fallen.Length; i++) _fallen[i] = default;
        FallenGelegt = FallenKeinPlatz = FallenAusgeloest = FallenGeraeumt = 0;
        FallenFreund = FallenImmunFall = FallenTorZu = FallenFussvolk = 0;
        FallenOpfer.Clear();
    }

    /// <summary><b>lay_trap</b> C 0x421A40 / F 0x420C00 — dasselbe Tor wie
    /// <see cref="MineLegen"/> (dort ausfuehrlich), dieselben zwei Wuerfe.
    /// <para>⚠ <paramref name="aufbau"/> (UNSERE SETZUNG, aus Zahlen): fuer die Fallen
    /// des Missionsaufbaus fragt das Tor NUR unsere Belegung (frei oder Fahrzeug), nicht
    /// Bodenklasse und Gebaeudegrundriss. Grund: in allen fuenf K25-Staenden des
    /// Originals (3/5/6/7/10.DM) liegen 6/6 Fallen, und die imap (sec6) traegt an allen
    /// sechs Zellen 0xFFFE — bei uns aber ist (168,96) »gesperrt« (Gelaende 3) und
    /// (35,59) liegt im Grundriss des Gebaeudes Init73 (Belegung frei). Mit dem vollen
    /// Tor lagen nur 4 von 6.</para></summary>
    public bool FalleLegen(int col, int row, int player, bool aufbau = false)
    {
        if (FallenAus || _nav == null || !_nav.InBounds(col, row)) return false;
        int drauf = _nav.BesetztVon(col, row);
        var ding = drauf >= 0 && drauf < _entities.Count ? _entities[drauf] : null;
        bool unterFahrzeug = ding != null && !ding.IsBuilding && !ding.IsProp && !ding.Dead
                             && ding.Infantry < 0;
        bool frei = ding == null && (aufbau || _nav.GroundAt(col, row) == Simulation.NavGrid.Ground.Free
                                     && !GrundrissSperrt(col, row));   // bug-706
        if (!frei && !unterFahrzeug) return false;
        for (int i = 0; i < _fallen.Length; i++)
        {
            if (_fallen[i].Aktiv) continue;
            // +0x02 = rand%20+10, +0x03 = rand%10+5 (@0x421AC7 / @0x421ADD)
            int lx = Simulation.Determinism.Roll(20) + 10;
            int ly = Simulation.Determinism.Roll(10) + 5;
            _fallen[i] = new Mine { Col = col, Row = row, Player = player, Aktiv = true, LageX = lx, LageY = ly };
            FallenGelegt++;
            return true;
        }
        FallenKeinPlatz++;                                   // @0x421A8D »Too many traps«
        return false;
    }

    /// <summary>Der Fallenteil von »Mines and traps« — aus <see cref="MinenTakt"/>,
    /// NACH der Minenschleife wie im Original (@0x4217C6).</summary>
    private void FallenTakt()
    {
        if (FallenAus || _nav == null) return;
        for (int i = 0; i < _fallen.Length; i++)
        {
            ref var f = ref _fallen[i];
            if (!f.Aktiv) continue;
            int vi = _nav.BesetztVon(f.Col, f.Row);                       // imap < 8000
            if (vi < 0 || vi >= _entities.Count) continue;
            var opfer = _entities[vi];
            if (opfer.Dead || opfer.IsProp || opfer.IsBuilding) continue;
            if (opfer.Infantry >= 0) { FallenFussvolk++; continue; }
            if (!MineFeindlich(f.Player, opfer.Owner)) { FallenFreund++; continue; }   // 0x87B155
            if (opfer.Part == FallenImmun) { FallenImmunFall++; continue; }           // @0x42184B
            if (!FallenLetzterTakt(opfer, f.Col, f.Row)) { FallenTorZu++; continue; } // @0x421856..0x421869
            f.Aktiv = false;                                                           // @0x42186B
            opfer.Speed = 0;                                                           // @0x421872
            opfer.Gefangen = true;                                                     // UNSERE Setzung, Kopf
            FallenAusgeloest++;
            FallenOpfer.Add(vi);
            GD.Print($"falle: Spieler {f.Player} auf ({f.Col},{f.Row}) faengt Platz {opfer.Slot} »{LabelOf(opfer)}« "
                   + $"(P{opfer.Owner}) — Tempo := 0 (C 0x421872)");
        }
    }

    /// <summary><c>KOLIK &lt; 0 &amp;&amp; −(+0x20) &lt; KOLIK</c>: die Einfahrt, und
    /// der Zuwachs dieses Taktes erreicht die Zellmitte. Siehe Kopf.</summary>
    private bool FallenLetzterTakt(Entity e, int col, int row)
    {
        if (!MinenEinfahrt(e, col, row)) return false;
        long voll = (long)e.StepCost * SimHz;
        long zuwachs = (long)RawSpeedOf(e) * Simulation.NavGrid.OriginalHz * 1000;
        return zuwachs > 0 && voll - e.Progress < zuwachs;
    }

    /// <summary><b>0x421ED0</b> (F 0x421090): die erste FEINDLICHE Falle auf
    /// (spalte, zeile) frei, Klang 41 an der Kartenstelle.</summary>
    private bool FallenRaeumen(int col, int row, int spieler)
    {
        for (int i = 0; i < _fallen.Length; i++)
        {
            ref var f = ref _fallen[i];
            if (!f.Aktiv || f.Col != col || f.Row != row) continue;
            if (!MineFeindlich(f.Player, spieler)) continue;
            f.Aktiv = false;
            FallenGeraeumt++;
            Audio.GameSounds.PlayAt(FallenRaeumKlang, col, row);       // C 0x421F55, Modus 2
            return true;
        }
        return false;
    }

    /// <summary>Die Fallen des SETUP-Blocks in Rufreihenfolge (M20 4, M25 6).
    /// Aus dem Skriptstart, NACH den Minenfeldern wie im Original
    /// (M25: lay_mine @0x48F416..0x48F49A, dann lay_trap @0x48F4BE..).</summary>
    public void MissionsFallenLegen(IReadOnlyList<(int Col, int Row, int Leger)> fallen)
    {
        if (_minenStandGeladen) { GD.Print($"Fallen: aus dem Spielstand ({FallenAktiv()} liegen) — der Missionsaufbau legt keine"); return; }
        FallenLeeren();
        if (FallenAus || fallen.Count == 0) return;
        foreach (var (c, r, p) in fallen)
            if (!FalleLegen(c, r, p, aufbau: true) && _nav != null && _nav.InBounds(c, r))
            {
                int drauf = _nav.BesetztVon(c, r);
                GD.Print($"Fallen: ({c},{r}) P{p} NICHT gelegt — Boden {_nav.GroundAt(c, r)} ({_nav.GroundWord(c, r)}), "
                       + $"belegt {(drauf >= 0 && drauf < _entities.Count ? $"{drauf} »{LabelOf(_entities[drauf])}« Fussvolk {_entities[drauf].Infantry >= 0}" : "nein")}, "
                       + $"Gebaeude {GebaeudeAufZelle(c, r)}");
            }
        GD.Print($"Fallen: {FallenGelegt} gelegt aus {fallen.Count} Rufen des Missionsaufbaus"
                 + (FallenKeinPlatz > 0 ? $", {FallenKeinPlatz}x kein Platz" : ""));
    }

    public int FallenAktiv()
    {
        int n = 0;
        foreach (var f in _fallen) if (f.Aktiv) n++;
        return n;
    }

    private int FallenAufZelle(int col, int row, int spieler = -1)
    {
        int n = 0;
        foreach (var f in _fallen)
            if (f.Aktiv && f.Col == col && f.Row == row && (spieler < 0 || f.Player == spieler)) n++;
        return n;
    }

    /// <summary><b>0x421D40</b> — liegt auf der Zelle eine Mine (0x552E18, 500), eine Falle
    /// (0x688B58, 500) oder ein RADARMAST (0x677F30, 200; @0x421DA4..0x421DD0), gleich
    /// welchen Spielers? ⭐ bug-722 (30.10.2026): hier stand »Gaswolke (0x677F30) nicht
    /// gebaut« — 0x677F30 ist die Radarmast-Tafel (RadarMast.cs), die Abfrage fehlte.
    /// Rufer im Original: Minenarm @0x408832, Fallenarm @0x40897E, Punktsuche @0x41193D
    /// (F @0x408756/0x4088A2/0x41170D) — bei uns Legeauftrag.cs (Arm, Punktsuche).
    /// Gegenschalter <c>--radarmast-zelle-alt</c>.</summary>
    private bool MineOderFalleAufZelle(int col, int row)
        => MinenAufZelle(col, row) > 0 || FallenAufZelle(col, row) > 0
           || (!RadarmastZelleAlt && RadarmastAufZelle(col, row));

    /// <summary><c>--radarmast-zelle-alt</c> — bug-722, der Stand bis 30.10.2026.</summary>
    public static bool RadarmastZelleAlt;

    /// <summary>Die dritte Schleife von 0x421D40: ein belegter Platz (+0x04 != 0) mit
    /// Spalte/Zeile der Zelle. Ein abgelaufener Mast ist aus der Liste genommen.</summary>
    private bool RadarmastAufZelle(int col, int row)
    {
        foreach (var m in _radarMasts)
            if (m.Col == col && m.Row == row) return true;
        return false;
    }

    /// <summary>Die Nebelrunde C 0x420945..0x4209A7: verbuendete Fallen, Radius 0.</summary>
    private IEnumerable<(int Col, int Row, int Sicht, int Hoehe)> FallenAufdecker(int betrachter)
    {
        if (FallenAus) yield break;
        foreach (var f in _fallen)
            if (f.Aktiv && DecktAuf(f.Player, betrachter))
                yield return (f.Col, f.Row, 1, 0);
    }

    // ---- das Bild ---------------------------------------------------------------

    /// <summary>Gezeichnet: volle Bilder (eigen/verbuendet), Punkte, Umrisse.</summary>
    public int FallenScheiben, FallenPunkte, FallenUmrisse;

    /// <summary>Zeichenliste C 0x42F4A0 — wie <see cref="MinenKorb"/>, Feld 0x688B58.</summary>
    private IEnumerable<(Vector2 Pos, MinenBildArt Art, int Klasse, int Leger)> FallenKorb(int zeile)
    {
        for (int i = 0; i < _fallen.Length; i++)
        {
            var f = _fallen[i];
            if (!f.Aktiv || f.Row + 2 != zeile) continue;
            if (FogActive && !Watched(f.Col, f.Row)) continue;                 // sec50 0x678B58
            int h = Simulation.Hang.Hub(ElevOf(f.Col, f.Row), HangArt(f.Col, f.Row), f.LageX, f.LageY);
            var pos = new Vector2(_ox + f.Col * TileW - 20 + f.LageX,
                                  _oy + f.Row * TileH - 20 + f.LageY - h);
            int klasse = SlopeClassOf(f.Col, f.Row);
            MinenBildArt art = Allied(ViewPlayer, f.Player) ? MinenBildArt.Scheibe
                             : _minenBildWurf.Next(1000) == 333 ? MinenBildArt.Umriss     // @0x42D046
                             : MinenBildArt.Punkt;
            yield return (pos, art, klasse, f.Player);
        }
    }

    /// <summary>Zeichner Art 16 C 0x42CFAD: Folge 94 (»falle«), Punkt Folge 61
    /// (»fallenpunkt«). Gegenschalter wie bei den Minen: <c>--minen-unsichtbar</c>.</summary>
    private void FallenZeichnen(int zeile)
    {
        if (MinenUnsichtbar || FallenAus) return;
        var scheibe = EffectFrames("falle");
        var punkt = EffectFrames("fallenpunkt");
        if (scheibe.Count == 0 || punkt.Count == 0) return;
        foreach (var (pos, art, klasse, _) in FallenKorb(zeile))
        {
            var bild = art == MinenBildArt.Punkt ? punkt[0] : scheibe[Mathf.Clamp(klasse, 0, scheibe.Count - 1)];
            if (art == MinenBildArt.Scheibe) { DrawTexture(bild, pos); FallenScheiben++; }
            else { DrawTexture(bild, pos, ShadowTint); if (art == MinenBildArt.Punkt) FallenPunkte++; else FallenUmrisse++; }
        }
    }

    // ---- --fallen-check -----------------------------------------------------------

    /// <summary>
    /// <c>--fallen-check</c>, Kampagne 25, der ECHTE Fahrweg (SimTick, Befehl ueber den Ring):
    /// <list type="number">
    /// <item>Der Aufbau hat 6 Fallen gelegt, je an der Zelle aus mission_fallen.py.</item>
    /// <item>A: ein eigenes Fahrzeug faehrt auf die P2-Falle (167,97): sie geht im
    /// letzten Takt vor der Zellmitte, Tempo 0, Falle frei; 300 Takte spaeter und nach
    /// einem neuen Fahrbefehl steht es noch an derselben Stelle.</item>
    /// <item>B: ein Fahrzeug mit Teil 0x45 (Fallenraeumer, ⚠ EINGRIFF: Teil gesetzt,
    /// falls keiner da ist) faehrt auf die Falle (161,65): Klang 41 ueber den Horcher,
    /// Falle frei, Tempo unveraendert.</item>
    /// <item>C: eine EIGENE Falle faengt das eigene Fahrzeug nicht.</item>
    /// </list>
    /// Nullmodell <c>--fallen-aus</c>: 0 Fallen, A und B MUESSEN scheitern.
    /// </summary>
    public string FallenCheck()
    {
        var sb = new System.Text.StringBuilder("fallen-check\n");
        EnsureMissionScript();
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (FallenAus) sb.AppendLine("  ⚠ NULLMODELL --fallen-aus: hier MUSS der Lauf durchfallen");
        var gehoert = new List<int>();
        var vorherHorcher = Audio.SoundBankPlayer.Horcher;
        Audio.SoundBankPlayer.Horcher = (s, c, r) => { gehoert.Add(s); vorherHorcher?.Invoke(s, c, r); };
        try
        {
            var soll = new (int C, int R, int P)[] { (168, 96, 2), (161, 65, 2), (167, 97, 2), (65, 106, 4), (47, 73, 4), (35, 59, 4) };
            int treffer = 0;
            foreach (var (c, r, p) in soll) if (FallenAufZelle(c, r, p) == 1) treffer++;
            Soll(FallenAktiv() == 6 && treffer == 6,
                 $"Aufbau K25: {FallenAktiv()} Fallen aktiv, {treffer}/6 an der gelesenen Zelle mit Leger (gelegt {FallenGelegt}, kein Platz {FallenKeinPlatz})");

            int Fahrzeug(bool raeumer, int ausser, int ausser2 = -1)
            {
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (i == ausser || i == ausser2 || e.Dead || e.IsProp || e.IsBuilding || !e.Mobile || e.Owner != ViewPlayer || Untergestellt(e)) continue;
                    if (e.Infantry >= 0 || e.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
                    if (raeumer != (e.Part == FallenImmun)) continue;
                    return i;
                }
                return -1;
            }
            void Ruhe(Entity e) { e.Path = null; e.Orders.Clear(); e.Target = -1; e.StepCost = 0; e.Progress = 0; }
            Vector2I? Nachbar(int c, int r, Entity e, int idx)
            {
                foreach (var (dx, dy) in new[] { (0, -1), (-1, 0), (1, 0), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    if (_nav.InBounds(c + dx, r + dy) && _nav.IsFree(c + dx, r + dy, e.Move, idx)
                        && FallenAufZelle(c + dx, r + dy) == 0 && MinenAufZelle(c + dx, r + dy) == 0)
                        return new Vector2I(c + dx, r + dy);
                return null;
            }

            // ---- A. ein gewoehnliches eigenes Fahrzeug auf die Falle (167,97) --------
            // ⚠ nicht (168,96): die Zelle ist bei uns gesperrt (Gelaende 3, Original 0xFFFE)
            var fa = new Vector2I(167, 97);
            int ai = Fahrzeug(raeumer: false, -1);
            if (ai < 0) Soll(false, "A: kein eigenes Landfahrzeug");
            else
            {
                var a = _entities[ai];
                var start = Nachbar(fa.X, fa.Y, a, ai);
                if (start == null) Soll(false, $"A: keine freie Nachbarzelle an ({fa.X},{fa.Y})");
                else
                {
                    ProbeVersetzen(ai, start.Value.X, start.Value.Y); Ruhe(a);
                    if (a.FuelMax > 0) a.Fuel = a.FuelMax;
                    int tempo0 = RawSpeedOf(a), aus0 = FallenAusgeloest;
                    sb.AppendLine($"  ⚠ EINGRIFF: Platz {a.Slot} »{LabelOf(a)}« (Tempo {tempo0}) nach ({start.Value.X},{start.Value.Y}) versetzt");
                    _sel.Clear(); _sel.Add(ai); _selected = ai;
                    bool posted = PostMoveOne(ai, CellCenter(fa.X, fa.Y), false);
                    string beim = "nie";
                    bool letzter = false;
                    int t = 0;
                    while (t < 600 && FallenAusgeloest == aus0 && !a.Dead && !(a.Col == fa.X && a.Row == fa.Y))
                    {
                        long voll = (long)a.StepCost * SimHz; int prog = a.Progress; var res = a.Reserved;
                        SimTickFuerProbe(); t++;
                        if (FallenAusgeloest != aus0)
                        {
                            long zuw = (long)tempo0 * Simulation.NavGrid.OriginalHz * 1000;
                            letzter = res == fa && voll > 0 && 2L * prog >= voll && voll - prog < zuw;
                            beim = $"Takt {t}, Schritt {prog}/{voll} ({(voll > 0 ? 100.0 * prog / voll : 0):0.0} %), Zuwachs {zuw}";
                        }
                    }
                    Soll(posted && FallenAusgeloest == aus0 + 1 && a.Gefangen && a.Speed == 0 && FallenAufZelle(fa.X, fa.Y) == 0,
                         $"A: gefangen {FallenAusgeloest - aus0}x, Tempo {a.Speed} (Gefangen {a.Gefangen}), Falle dort noch {FallenAufZelle(fa.X, fa.Y)}, steht ({a.Col},{a.Row})");
                    Soll(letzter, $"A: ausgeloest im letzten Takt vor der Zellmitte — {beim}");
                    var pos0 = a.Pos;
                    for (int k = 0; k < 300; k++) SimTickFuerProbe();
                    var ziel2 = new Vector2I(start.Value.X - 3, start.Value.Y - 3);
                    bool posted2 = PostMoveOne(ai, CellCenter(ziel2.X, ziel2.Y), false);
                    for (int k = 0; k < 300; k++) SimTickFuerProbe();
                    float weg = a.Pos.DistanceTo(pos0);
                    Soll(!a.Dead && weg < 0.01f && !(a.Col == fa.X && a.Row == fa.Y),
                         $"A: steht fuer immer — nach 600 Takten und einem neuen Fahrbefehl ({(posted2 ? "angenommen" : "abgelehnt")}) {weg:0.00} px bewegt, RawSpeedOf {RawSpeedOf(a)}");
                    _sel.Clear(); _selected = -1;
                }
            }

            // ---- B. der Fallenraeumer auf (161,65) -------------------------------------
            var fb = new Vector2I(161, 65);
            int bi = Fahrzeug(raeumer: true, ai);
            bool eingriff = false; int partVor = 0;
            if (bi < 0) { bi = Fahrzeug(raeumer: false, ai); eingriff = bi >= 0; }
            if (bi < 0) Soll(false, "B: kein zweites Landfahrzeug");
            else
            {
                var b = _entities[bi];
                if (eingriff) { partVor = b.Part; b.Part = FallenImmun; sb.AppendLine($"  ⚠ EINGRIFF: Platz {b.Slot} bekommt Teil 0x45 (kein Fallenraeumer auf der Karte)"); }
                var start = Nachbar(fb.X, fb.Y, b, bi);
                if (start == null) Soll(false, $"B: keine freie Nachbarzelle an ({fb.X},{fb.Y})");
                else
                {
                    ProbeVersetzen(bi, start.Value.X, start.Value.Y); Ruhe(b);
                    if (b.FuelMax > 0) b.Fuel = b.FuelMax;
                    int ger0 = FallenGeraeumt, aus0 = FallenAusgeloest, tempo0 = b.Speed, h0 = gehoert.Count;
                    bool posted = PostMoveOne(bi, CellCenter(fb.X, fb.Y), false);
                    int t = 0;
                    while (t < 600 && !(b.Col == fb.X && b.Row == fb.Y && b.Path == null) && !b.Dead) { SimTickFuerProbe(); t++; }
                    int k41 = gehoert.GetRange(h0, gehoert.Count - h0).FindAll(s => s == FallenRaeumKlang).Count;
                    Soll(posted && FallenGeraeumt == ger0 + 1 && FallenAusgeloest == aus0 && FallenAufZelle(fb.X, fb.Y) == 0
                         && !b.Gefangen && b.Speed == tempo0 && k41 == 1,
                         $"B: Raeumer Platz {b.Slot} in {t} Takten auf ({b.Col},{b.Row}): geraeumt {FallenGeraeumt - ger0}, gefangen {FallenAusgeloest - aus0}, "
                         + $"Klang 41 gehoert {k41}x, Tempo {tempo0} -> {b.Speed}, Falle dort noch {FallenAufZelle(fb.X, fb.Y)}");
                }
                if (eingriff) b.Part = partVor;
            }

            // ---- C. eine EIGENE Falle ------------------------------------------------
            int ci = Fahrzeug(raeumer: false, ai, bi);
            if (ci < 0) sb.AppendLine("  (C entfaellt: kein drittes Fahrzeug)");
            else
            {
                var e = _entities[ci];
                var z = Nachbar(e.Col, e.Row, e, ci);
                if (z != null && FalleLegen(z.Value.X, z.Value.Y, e.Owner))
                {
                    int fr0 = FallenFreund, aus0 = FallenAusgeloest;
                    Ruhe(e);
                    PostMoveOne(ci, CellCenter(z.Value.X, z.Value.Y), false);
                    for (int k = 0; k < 300 && !(e.Col == z.Value.X && e.Row == z.Value.Y); k++) SimTickFuerProbe();
                    Soll(FallenAusgeloest == aus0 && !e.Gefangen && FallenFreund > fr0 && e.Col == z.Value.X && e.Row == z.Value.Y,
                         $"C: eigene Falle auf ({z.Value.X},{z.Value.Y}): Platz {e.Slot} angekommen {(e.Col == z.Value.X && e.Row == z.Value.Y)}, gefangen {FallenAusgeloest - aus0}, als Freund {FallenFreund - fr0}x");
                }
                else sb.AppendLine("  (C entfaellt: keine Zelle fuer die eigene Falle)");
            }

        }
        finally { Audio.SoundBankPlayer.Horcher = vorherHorcher; }
        sb.AppendLine($"  Zaehler: gelegt {FallenGelegt}, gefangen {FallenAusgeloest}, geraeumt {FallenGeraeumt}, Freund {FallenFreund}, Raeumer {FallenImmunFall}, Tor zu {FallenTorZu}, Fussvolk {FallenFussvolk}; --fallen-aus {FallenAus}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    /// <summary><c>--minenhalt-check</c> (bug-707): eigenes Fussvolk mit Teil 0xC1 (⚠ EINGRIFF,
    /// falls kein Wissenschaftler da ist) laeuft ueber eine feindliche Falle und muss dort halten.
    /// Nullmodell <c>--minenhalt-alt</c>: er laeuft durch. Fussvolk loest Fallen nicht aus
    /// (imap &lt; 8000), darum ist die Falle das Hindernis, das sonst nichts tut.</summary>
    public string MinenhaltCheck()
    {
        var sb = new System.Text.StringBuilder("minenhalt-check\n");
        EnsureMissionScript();
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (MinenhaltAlt) sb.AppendLine("  ⚠ NULLMODELL --minenhalt-alt: hier MUSS der Lauf durchfallen");
        bool gefundenD = false;
        // ---- D. bug-707: der Wissenschaftler (Teil 0xC1) haelt vor einer feindlichen Falle ----
        int di = -1;
        for (int i = 0; i < _entities.Count && di < 0; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Mobile && e.Owner == ViewPlayer && e.Infantry >= 0 && !Untergestellt(e)) di = i;
        }
        if (di < 0) { sb.AppendLine("  (D entfaellt: kein eigenes Fussvolk)"); ok = false; }
        else
        {
            var d = _entities[di];
            int partVorD = d.Part;
            bool eingriffD = d.Part != MinenHaltTeil;
            if (eingriffD) d.Part = MinenHaltTeil;
            // eine Gerade aus drei freien Zellen: Start, Falle, Ziel
            Vector2I? st = null; Vector2I dir = default;
            foreach (var dv in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
            {
                var a1 = new Vector2I(d.Col, d.Row) + dv; var a2 = a1 + dv;
                if (_nav.InBounds(a2.X, a2.Y) && _nav.IsFree(a1.X, a1.Y, d.Move, di) && _nav.IsFree(a2.X, a2.Y, d.Move, di)
                    && _nav.GroundAt(a1.X, a1.Y) == Simulation.NavGrid.Ground.Free && FallenAufZelle(a1.X, a1.Y) == 0
                        && _nav.BesetztVon(a1.X, a1.Y) < 0 && _nav.BesetztVon(a2.X, a2.Y) < 0)
                { st = new Vector2I(d.Col, d.Row); dir = dv; break; }
            }
            if (st == null) sb.AppendLine("  (D entfaellt: keine freie Gerade am Fussvolk)");
            else
            {
                var fz = st.Value + dir; var ziel = fz + dir;
                int fremd = -1;
                for (int q = 0; q < 8 && fremd < 0; q++) if (MineFeindlich(q, d.Owner)) fremd = q;   // ein Spieler im Spiel
                bool gelegt = FalleLegen(fz.X, fz.Y, fremd);
                int h0 = MinenHalte;
                d.Path = null; d.Orders.Clear(); d.Target = -1;
                PostMoveOne(di, CellCenter(ziel.X, ziel.Y), false);
                for (int k = 0; k < 400 && !(d.Col == ziel.X && d.Row == ziel.Y) && !(MinenHalte > h0 && d.Path == null); k++) SimTickFuerProbe();
                for (int k = 0; k < 100; k++) SimTickFuerProbe();
                bool sollD = MinenHalte == h0 + 1 && d.Col == fz.X && d.Row == fz.Y; gefundenD = true;
                Soll(gelegt && sollD,
                     $"D: {(eingriffD ? "⚠ EINGRIFF Teil 0xC1, " : "")}Fussvolk Platz {d.Slot} »{LabelOf(d)}« von ({st.Value.X},{st.Value.Y}) ueber feindliche Falle ({fz.X},{fz.Y}) nach ({ziel.X},{ziel.Y}): "
                     + $"steht ({d.Col},{d.Row}), angehalten {MinenHalte - h0}x (--minenhalt-alt {MinenhaltAlt})");
            }
            if (eingriffD) d.Part = partVorD;
        }
        if (!gefundenD) ok = false;
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
