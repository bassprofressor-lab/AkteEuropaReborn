namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// DIE REPARATURKETTE DER BAHN — ein Fahrzeug fährt zu einem zerschossenen
/// Gleisstück, arbeitet daran und sucht sich danach SELBST das nächste.
///
/// <para><b>Warum sie erst jetzt kommt.</b> Sie stand seit dem 12.08.2026 als
/// »vollständig gelesen, wartet auf die Befehlsschicht« im Handoff. Die
/// Befehlsschicht ist seit <c>185e98f</c> da; offen blieb dort nur eine Frage,
/// und die ist am 14.08. beantwortet (siehe <see cref="Entity.RailGoal"/>).</para>
///
/// <para><b>Der ganze Weg, aus der EXE gelesen — vier Stellen, jede geprüft:</b></para>
/// <code>
///   0x407F10   Auftragsverteiler: ukol = byte[+0x14], <= 0x38,
///              Indextafel 0x40A130, Sprungtafel 0x40A0D8 (22 Ziele, alle .text)
///              -> ukol 30 landet auf 0x4099B6
///   0x407F5A   Teileverteiler: (byte[+0x0E] - 71), Indextafel 0x40A188,
///              Sprungtafel 0x40A16C (7 Ziele, alle .text)
///              -> Spezialteil 73 landet auf 0x408267
///   0x408267   hat es einen Auftrag (word[+0x40] != 0)?  ist die Zielzelle
///              [+0x48]/[+0x49] die eigene?  wenn nein: hinfahren (0x4082AE).
///   0x408292   ANGEKOMMEN:  byte[+0x38] := 30   und   ukol := 30
///   0x4099B6   je Takt: byte[+0x38]--, und solange er ueber 10 steht,
///              geschieht NICHTS (cmp al,0xa / ja) -> 20 Takte Arbeit
///   0x4099E7   fertig: bild := bild % 10 (div 10), Effekt 0x2D an der Stelle
///              (0x409A04), rail_pylon_pass (0x409A0C), ukol := 0 (0x409A20)
///   0x409A2D   nächstes kaputtes Stück in der Nähe -> Zelle nach [+0x48/+0x49]
///   0x409A5E   sonst rail_find_broken(linie)      -> Zelle nach [+0x48/+0x49]
///   0x409A9A   gar nichts mehr kaputt: die Linie geht auf faze := 0 zurueck
/// </code>
///
/// <para>⚠ <b>Die Trefferpunkte werden NICHT zurückgesetzt.</b> Der Arm fasst
/// nur das Bild an; <see cref="MapEntityLayer.RailRepair"/> sagt das an seiner
/// Stelle schon. Ein oft repariertes Stück bleibt also dünn — das ist das
/// Original, nicht unsere Sparsamkeit.</para>
///
/// <para><b>⚠ UNSERE SETZUNGEN</b>, zwei, und beide hier benannt:</para>
/// <list type="number">
///   <item><b>Wie die Fahrt beginnt.</b> Im Original prüft 0x408267 einen
///   AUFTRAG (<c>word[+0x40] != 0</c>) und vergleicht die Zielzelle mit der
///   eigenen. Woher der erste Auftrag kommt, ist ungelesen — der Bus hat dafür
///   keinen gelesenen Opcode. Bei uns beginnt die Arbeit, wenn ein Fahrzeug mit
///   dem Aufsatz auf einem kaputten Stück <b>steht und nicht mehr fährt</b>.
///   Das ist dieselbe Bedingung, nur ohne den ungelesenen Auftragsweg: der
///   Spieler fährt hin, der Rest läuft von selbst.</item>
///   <item><b>Wie es zum nächsten Stück kommt.</b> Das Original schreibt die
///   Zelle nach +0x48/+0x49 und lässt seinen eigenen Fahrcode hinlaufen; wir
///   setzen einen Weg über <see cref="Simulation.NavGrid.FindPath"/>. Die
///   AUSWAHL des nächsten Stücks ist dagegen die gelesene: erst dasselbe
///   Liniennetz in der Nähe, dann irgendeines derselben Liniennummer.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Der Aufsatz, der Gleise repariert — Satzbyte +0x0E. GELESEN:
    /// die Teiletafel 0x40A188/0x40A16C schickt genau die <b>73</b> auf den
    /// Zweig 0x408267.</summary>
    public const int RailRepairPart = 73;

    /// <summary>Womit der Arbeitszähler anfängt (0x1E @0x408292) und ab wann
    /// das Stück heil ist (<c>ja</c> gegen 0xA @0x4099B9). Die Differenz ist
    /// die Arbeitszeit: <b>20 Takte</b>.</summary>
    public const int RailWorkStart = 30, RailWorkDone = 10;

    /// <summary>Wie oft eine Reparatur fertig geworden ist — für den
    /// Prüfstand.</summary>
    public int RailRepairsDone { get; private set; }

    /// <summary>Wie oft ein Fahrzeug sich selbst das nächste Stück gegeben
    /// hat. Das ist die eigentliche KETTE; ohne diese Zahl wäre nicht zu
    /// unterscheiden, ob eines fünfmal repariert hat oder fünf je einmal.</summary>
    public int RailRepairChained { get; private set; }

    /// <summary>Der Takt der Reparaturfahrzeuge. Gehört in den
    /// Simulationstakt, nicht ins Bild — der Zähler ist Zustand.</summary>
    private void RailRepairTick()
    {
        if (_nav == null) return;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp) continue;
            if (e.Equipment != RailRepairPart) continue;

            // ---- arbeitet gerade -------------------------------------------
            if (e.RailWork > 0)
            {
                e.RailWork--;                       // @0x4099BE
                if (e.RailWork > RailWorkDone) continue;   // @0x4099C1: noch nicht
                e.RailWork = 0;
                FinishRailRepair(i, e);
                continue;
            }

            // ---- hat es ein Ziel? -------------------------------------------
            // @0x408267 laeuft JEDEN Takt: Zielzelle gegen die eigene halten,
            // und wenn sie nicht stimmt, hinfahren (@0x4082AE).
            //
            // ⚠ Diese Wiederholung ist der Kern, und ihr Fehlen hat mich einen
            // Durchgang gekostet: der Schrittcode setzt `Path = null`, wenn ein
            // Umweg misslingt (eine fremde Einheit stand kurz im Weg). Ohne die
            // Wiederaufnahme blieb das Fahrzeug dann fuer immer stehen — mit
            // Ziel, ohne Weg, fahrbereit. Der Prueflauf meldete »1 repariert«
            // und die Diagnosezeile nannte genau diese Lage.
            if (e.RailGoal is { } g)
            {
                if (e.Col == g.X && e.Row == g.Y)
                {
                    if (RailBrokenAt(e.Col, e.Row))
                        e.RailWork = RailWorkStart;         // @0x408292: angekommen
                    else
                        FinishRailRepair(i, e, healed: false);   // schon heil: weiter
                }
                else if (e.Path == null && e.Reserved == null)
                {
                    DriveToRail(i, e, g);                   // @0x4082AE: hinfahren
                }
                continue;
            }

            // ---- kein Ziel: steht es zufaellig auf einem kaputten Stueck? ----
            // ⚠ UNSERE Fassung des ERSTEN Auftrags: woher das Original ihn
            // bekommt, ist ungelesen (der Bus hat dafuer keinen gelesenen
            // Opcode). »Steht drauf und faehrt nicht mehr« ist dieselbe Lage
            // ohne den ungelesenen Weg.
            if (e.Path != null || e.Reserved != null) continue;
            if (!RailBrokenAt(e.Col, e.Row)) continue;
            e.RailWork = RailWorkStart;             // @0x408292
            e.RailGoal = new Vector2I(e.Col, e.Row);
        }
    }

    /// <summary>Liegt auf dieser Zelle ein zerschossenes Gleisstueck?</summary>
    private bool RailBrokenAt(int col, int row)
    {
        foreach (var c in _railCells)
            if (c.Col == col && c.Row == row && c.Broken) return true;
        return false;
    }

    /// <summary>
    /// Das Stueck heilen und sich das naechste geben — @0x4099E7 bis 0x409A95.
    /// </summary>
    private void FinishRailRepair(int i, Entity e, bool healed = true)
    {
        int line = RailLineAt(e.Col, e.Row);
        if (healed)
        {
            if (!RailRepair(e.Col, e.Row)) { e.RailGoal = null; return; }
            RailRepairsDone++;
        }
        // Effekt 0x2D an der Stelle (@0x409A04). Unsere Effekte laufen ueber
        // Namen, nicht ueber die Nummer des Originals — der Kopfkommentar von
        // ANIM.CWA nennt 0x2D als Reparaturfunken.
        if (healed)
            _effects.Add(new Effect
            {
                Pos = CellCenter(e.Col, e.Row), Kind = "explosion", FrameTime = 0.06f,
            });

        // ---- das naechste Stueck, in der Reihenfolge des Originals ---------
        // erst in der Naehe auf derselben Linie (@0x409A2D), sonst irgendeines
        // mit derselben Liniennummer (@0x409A5E, rail_find_broken).
        // ⚠ UNSERE Fassung des Hinfahrens, und sie hat einen teuren Vorlaeufer.
        //
        // Zuerst stand hier: das NAECHSTE kaputte Stueck nehmen, einen Weg
        // dorthin suchen, und wenn keiner kommt, es dabei belassen. Der
        // Prueflauf sagte danach »1 repariert« und blieb dort stehen — 110
        // Sekunden lang. Die Diagnosezeile nannte das Glied: Ziel (164,118),
        // Fahrzeug auf (165,118), faehrt nicht. `NearestFree` darf naemlich die
        // EIGENE Zelle des Bewegers zurueckgeben (sie ist fuer ihn frei), und
        // dann ist Start gleich Ziel, der Weg leer und das Fahrzeug steht fuer
        // immer. ⚠ Nicht das Gelaende war schuld — von 52 kaputten Stuecken
        // waren 52 befahrbar; die Zielzelle war BELEGT.
        //
        // Jetzt werden die kaputten Stuecke der Reihe nach probiert, bis eines
        // wirklich erreichbar ist. Das Original braucht das nicht: es hat
        // keinen Wegsucher, es setzt die Zelle und faehrt los.
        foreach (var cand in BrokenOnLineByDistance(line, e.Col, e.Row))
        {
            if (cand.X == e.Col && cand.Y == e.Row) continue;
            e.RailGoal = cand;
            RailRepairChained++;
            DriveToRail(i, e, cand);
            return;
        }
        e.RailGoal = null;      // nichts mehr kaputt auf dieser Linie
    }

    /// <summary>Auf die Zielzelle zufahren — @0x4082AE. ⚠ Findet sich kein Weg,
    /// bleibt das ZIEL stehen: der naechste Takt versucht es wieder, so wie
    /// @0x408267 es jeden Takt tut. Ein einmal misslungener Weg darf einen
    /// Reparaturtrupp nicht fuer immer anhalten.</summary>
    private void DriveToRail(int i, Entity e, Vector2I cell)
    {
        var goal = _nav!.NearestFree(cell, e.Move, i);
        if (goal == null || goal.Value == new Vector2I(e.Col, e.Row)) return;
        var path = _nav.FindPath(new Vector2I(e.Col, e.Row), goal.Value, e.Move, i);
        if (path == null || path.Count == 0) return;
        e.Path = path;
        e.PathIdx = 0;
        e.Goal = goal.Value;
        e.Reserved = null;
        e.WaitTime = 0;
        e.Target = -1;
        e.Ordered = true;
    }

    /// <summary>Die kaputten Stuecke derselben Linie, nach Entfernung sortiert.
    /// ⚠ Die Reihenfolge ist die gelesene (nah zuerst, @0x409A2D vor
    /// @0x409A5E); dass es bei einem unerreichbaren WEITERSUCHT, ist unsere
    /// Zutat und der Grund steht bei <see cref="FinishRailRepair"/>.</summary>
    private System.Collections.Generic.List<Vector2I> BrokenOnLineByDistance(
        int line, int col, int row)
    {
        var list = new System.Collections.Generic.List<(long D, Vector2I At)>();
        foreach (var c in _railCells)
        {
            if (!c.Broken || (line >= 0 && c.Line != line)) continue;
            long dx = c.Col - col, dy = c.Row - row;
            list.Add((dx * dx + dy * dy, new Vector2I(c.Col, c.Row)));
        }
        list.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D)
                          : a.At.X != b.At.X ? a.At.X - b.At.X : a.At.Y - b.At.Y);
        var outp = new System.Collections.Generic.List<Vector2I>();
        foreach (var (_, at) in list) outp.Add(at);
        return outp;
    }

    /// <summary>Die Liniennummer des Stuecks auf dieser Zelle, oder −1.</summary>
    private int RailLineAt(int col, int row)
    {
        foreach (var c in _railCells)
            if (c.Col == col && c.Row == row) return c.Line;
        return -1;
    }

    /// <summary>Das naechste zerschossene Stueck derselben Linie. ⚠ Die
    /// Reihenfolge ist die gelesene: NAH zuerst. Das Original hat dafuer zwei
    /// getrennte Aufrufe (@0x409A2D und @0x409A5E) — beide suchen auf
    /// derselben Liniennummer, der zweite ohne Ruecksicht auf die
    /// Entfernung.</summary>
    private Vector2I? NearestBrokenOnLine(int line, int col, int row)
    {
        Vector2I? best = null;
        long bd = long.MaxValue;
        foreach (var c in _railCells)
        {
            if (!c.Broken || (line >= 0 && c.Line != line)) continue;
            long dx = c.Col - col, dy = c.Row - row;
            long d = dx * dx + dy * dy;
            if (d >= bd) continue;
            bd = d; best = new Vector2I(c.Col, c.Row);
        }
        return best;
    }

    /// <summary>
    /// <c>--rail-repair-check</c> — der Prueflauf, der die MECHANIK ausuebt.
    ///
    /// <para>Er schiesst selbst Gleisstuecke kaputt, stellt ein Fahrzeug mit
    /// dem Aufsatz auf eines davon und laesst laufen. Gezaehlt wird, was
    /// DANACH in der Karte steht — nicht, was vorhatte zu geschehen (Regel 11).</para>
    /// </summary>
    /// <summary>
    /// Die Lage herstellen: drei Gleisstuecke zerschiessen und ein Fahrzeug mit
    /// dem Aufsatz auf das erste stellen. Gibt die Stelle zum Hinsehen zurueck.
    ///
    /// <para>⚠ Eine Krücke des Prüfstands ist selbst eine Annahme (Regel 10):
    /// dass ein Fahrzeug den Aufsatz TRAEGT, wird hier GESETZT — auf den
    /// gelieferten Karten traegt ihn keines. Was NICHT gesetzt wird, ist
    /// irgendetwas an der Kette selbst: weder Ziel noch Zaehler noch Auftrag.
    /// Genau die soll ja geprueft werden.</para>
    /// </summary>
    public Vector2? RailRepairSetup()
    {
        if (_railCells.Count == 0) { GD.Print("rail-repair: kein Gleis auf dieser Karte"); return null; }

        // drei heile Stuecke derselben Linie zerschiessen
        int line = -1;
        var hit = new System.Collections.Generic.List<(int C, int R)>();
        foreach (var c in _railCells)
        {
            if (c.Frame == 255 || c.Broken || c.Pylon) continue;
            if (line < 0) line = c.Line;
            if (c.Line != line) continue;
            hit.Add((c.Col, c.Row));
            if (hit.Count >= 3) break;
        }
        foreach (var (c, r) in hit) RailHit(c, r, 999);
        int broken = 0;
        foreach (var c in _railCells) if (c.Broken) broken++;
        GD.Print($"rail-repair: Linie {line}, {hit.Count} Stuecke beschossen -> " +
                 $"{broken} kaputt (Nachbarn werden mitgerissen)");
        if (hit.Count == 0) return null;

        // ein Fahrzeug mit dem Aufsatz ausruesten und hinstellen
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || !e.Mobile) continue;
            if (e.Move != Simulation.NavGrid.MoveClass.Vehicle) continue;
            // ⚠ NUR EIN FAHRZEUG DES SICHTSPIELERS — und das ist kein
            // Schoenheitsfehler, es hat einen Lauf gekostet. Der erste Anlauf
            // nahm das erstbeste Fahrzeug; es gehoerte einem Computerspieler,
            // und der gab ihm jeden Denk-Takt einen eigenen Befehl. Der
            // Prueflauf meldete daraufhin »1 repariert« und ein Fahrzeug, das
            // mit Ziel, ohne Weg und fahrbereit dastand — die Kette sah kaputt
            // aus, obwohl sie nur ueberschrieben wurde.
            // Regel 10: die Kruecke des Pruefstands ist selbst eine Annahme.
            if (e.Owner != ViewPlayer) continue;
            e.Equipment = RailRepairPart;
            _nav?.ClearOccupant(e.Col, e.Row, i);
            e.Col = hit[0].C; e.Row = hit[0].R;
            e.Pos = CellCenter(e.Col, e.Row);
            e.Path = null; e.Reserved = null; e.Target = -1;
            e.Goal = new Vector2I(e.Col, e.Row);
            _nav?.SetOccupant(e.Col, e.Row, i);
            GD.Print($"rail-repair: Fahrzeug {i} (Platz {e.Slot}) bekommt Aufsatz " +
                     $"{RailRepairPart} und steht auf ({e.Col},{e.Row})");
            return e.Pos;
        }
        GD.Print("rail-repair: kein fahrbares Landfahrzeug zum Ausruesten gefunden");
        return null;
    }

    public string RailRepairLine()
    {
        int broken = 0;
        foreach (var c in _railCells) if (c.Broken) broken++;
        int crews = 0;
        foreach (var e in _entities)
            if (!e.Dead && !e.IsBuilding && !e.IsProp && e.Equipment == RailRepairPart) crews++;
        if (crews == 0)
            return "rail-repair: kein Fahrzeug mit dem Gleisaufsatz (Teil 73) auf dieser " +
                   "Karte — der Zaehler kann hier NICHTS aussagen";
        // ⚠ Die Frage hinter der stehengebliebenen Kette: kann ein Fahrzeug auf
        // einer Gleiszelle ueberhaupt STEHEN? Das Original verlangt genau das
        // (@0x408284 vergleicht die eigene Zelle mit der Zielzelle).
        int reach = 0, tot = 0;
        foreach (var c in _railCells)
        {
            if (!c.Broken) continue;
            tot++;
            if (_nav != null && _nav.CanEnter(c.Col, c.Row, Simulation.NavGrid.MoveClass.Vehicle))
                reach++;
        }
        var sb = new System.Text.StringBuilder();
        sb.Append($"rail-repair: von {tot} kaputten Stuecken sind {reach} fuer ein Fahrzeug " +
                  "BEFAHRBAR — das Original verlangt, dass es DARAUF steht (@0x408284)\n");
        sb.Append($"rail-repair: {crews} Fahrzeug(e) mit Aufsatz 73, noch {broken} Stuecke " +
                  $"kaputt, {RailRepairsDone} repariert, {RailRepairChained} mal hat sich " +
                  "eines selbst das naechste gegeben (das ist die KETTE)");
        // ⚠ Wenn die Kette steht, muss der Pruefstand sagen WELCHES GLIED —
        // sonst ist eine Kette, die nicht schliesst, nicht von einer zu
        // unterscheiden, die es fast tut (Regel 9).
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp || e.Equipment != RailRepairPart) continue;
            bool onBroken = RailBrokenAt(e.Col, e.Row);
            sb.Append($"\n   Fahrzeug {i} auf ({e.Col},{e.Row}): " +
                      $"Zaehler {e.RailWork}, Ziel " +
                      (e.RailGoal is { } g ? $"({g.X},{g.Y})" : "keines") +
                      $", faehrt {(e.Path != null ? "ja" : "nein")}" +
                      $", steht auf kaputtem Stueck {(onBroken ? "ja" : "NEIN")}");
            if (e.RailWork == 0 && e.Path == null && !onBroken && e.RailGoal is { } g2)
            {
                int occ = _nav?.OccupantAt(g2.X, g2.Y) ?? -1;
                bool enter = _nav?.CanEnter(g2.X, g2.Y, e.Move) ?? false;
                sb.Append($"\n     ⚠ HIER HAENGT ES: Ziel ({g2.X},{g2.Y}) — befahrbar " +
                          $"{(enter ? "ja" : "NEIN")}, belegt von " +
                          (occ < 0 ? "niemandem" :
                           occ == i ? "sich selbst" :
                           occ < _entities.Count
                               ? $"Einheit {occ} (Platz {_entities[occ].Slot}, " +
                                 $"{(_entities[occ].IsBuilding ? "Gebaeude" : "Fahrzeug")})"
                               : $"Handgriff {occ} (statisch)"));
                sb.Append($"\n     Zustand: beweglich {(e.Mobile ? "ja" : "NEIN")}, " +
                          $"eingegraben {(e.DugIn ? "JA" : "nein")}, " +
                          $"Sprit {e.Fuel}/{e.FuelMax}, Gattung {e.Move}, " +
                          $"Weg {(e.Path == null ? "keiner" : e.Path.Count.ToString())}, " +
                          $"vorgemerkt {(e.Reserved is { } rr ? $"({rr.X},{rr.Y})" : "keine")}");
            }
        }
        return sb.ToString();
    }
}
