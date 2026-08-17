namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--bau-check</c> — der Prüfstand zu den drei Bauaufträgen.
///
/// <para><b>Er misst drei Dinge, und das mittlere ist das eigentliche:</b></para>
/// <list type="number">
///   <item>der Knopfweg bietet dem Fahrzeug genau die Aufträge an, die sein
///   Bauteil hergibt — und einem Fahrzeug ohne das Bauteil keinen
///   (Gegenprobe);</item>
///   <item><b>nach dem Befehl steht noch KEIN Gebäude</b>, die Einheit fährt
///   erst. Das ist die Aussage, die die Kette von einem »Knopf baut sofort«
///   unterscheidet; ohne sie wäre nicht gesagt, dass wir die MECHANIK und nicht
///   nur ihr Ergebnis nachgebaut haben;</item>
///   <item>bei der Ankunft steht das Depot am gelesenen Versatz
///   <c>(Spalte−1, Zeile−1)</c> — und das Fahrzeug ist <b>weg</b>, weil es der
///   Preis war.</item>
/// </list>
///
/// <para>⚠ <b>Er sucht seinen Bauplatz, statt einen anzunehmen</b> — die Lehre
/// aus dem Radarprüfstand, der dreimal an einer Stelle mass, die die Wirkung
/// gar nicht zeigen konnte (Regel UU). Gesucht wird eine Zelle, die das Depot
/// TRÄGT und die nicht die Standzelle ist: sonst führe die Einheit nicht, und
/// die Fahrt wäre nicht Teil der Messung.</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _bauCheck = -1;
    private int _bauIdx = -1, _bauZielCol, _bauZielRow, _bauGebaeude0;
    private int _bauOrder = OrderDepot, _bauTyp = TypeDepot, _bauEckCol, _bauEckRow;
    private long _bauSim, _bauWartet;
    private System.Text.StringBuilder? _bauLog;

    /// <summary><c>--bau-check[=depot|mine|generator]</c> anwerfen. Ohne Angabe
    /// das Depot.
    ///
    /// <para>⚠ <b>Ein Lauf, ein Auftrag.</b> Drei Fälle hintereinander in einem
    /// Lauf hiesse, dass der zweite auf dem Zustand des ersten misst — und ein
    /// Prüfstand, der seinen Fall nicht sauber herstellt, ist keiner. Wer alle
    /// drei will, ruft dreimal.</para></summary>
    public void BauCheckStart(int order = OrderDepot)
    {
        _bauOrder = order;
        _bauTyp = BuildTypeOfOrder(order);
        _bauCheck = 0;
    }

    /// <summary>Wieviele lebende Gebäude gerade stehen — der Gegenstand.</summary>
    private int BuildingCount()
    {
        int n = 0;
        foreach (var e in _entities) if (e.IsBuilding && !e.IsProp && !e.Dead) n++;
        return n;
    }

    /// <summary>Stufe 1: Fahrzeug setzen, Leiste befragen, Befehl absetzen.</summary>
    private void PollBauCheck()
    {
        if (_bauCheck != 0) return;

        // ⚠⚠ DIE VORKOMMEN STEHEN IM ERSTEN TAKT NOCH NICHT DA. Sie kommen aus
        // dem MISSIONSAUFBAU (`add_terra_place`, siehe Simulation/Deposits.cs),
        // und der laeuft nach dem ersten SimTick: der erste Anlauf meldete
        // »KEIN URTEIL: diese Karte hat keine Vorkommen« — und zwei Zeilen
        // SPAETER stand im Protokoll »Vorkommen: 11 Rohstoffstellen aus dem
        // Missionsaufbau«. Das war kein Befund ueber die Karte, sondern ueber
        // die Reihenfolge. Deposits.cs kennt dieselbe Falle fuer die andere
        // Quelle; hier ist sie fuer das Skript.
        if (_bauOrder == OrderFieldMine && _deposits.Count == 0)
        {
            if (_bauWartet == 0) _bauWartet = DebugTicks + 1;
            if (DebugTicks - _bauWartet < 600) return;
        }
        _bauCheck = -1;
        var sb = new System.Text.StringBuilder(
            $"bau-check: \"{BuildOrderWord(_bauOrder)}\" (Modus {_bauOrder}, " +
            $"Gebaeudetyp {_bauTyp})\n");
        LoadDesigns();
        if (_designs == null || _nav == null || Patterns == null || !Patterns.HasBuildings)
        { sb.Append("  KEIN URTEIL: keine Entwuerfe/Karte/Muster"); GD.Print(sb); return; }

        // ---- den Entwurf mit dem passenden Bauteil suchen ----
        int teil = _bauOrder == OrderGenerator ? PartGeneratorTech : PartBuildingTech;
        int erwartet = _bauOrder == OrderGenerator ? 1 : 2;
        int slot = -1; string nm = "";
        for (int i = 0; i < _designs.Count; i++)
            if (_designs[i].Weapon == teil)
            { slot = _designs[i].Slot; nm = _designs[i].Name; break; }
        if (slot < 0)
        {
            // ⚠ Das ist KEIN stilles Nichtstun: steht das Bauteil in unseren
            // Entwuerfen nicht im Waffenfeld, dann ist die Zuordnung in
            // BuildPartOf falsch — und diese Zeile sagt es samt der Liste
            // dessen, was da IST.
            sb.Append($"  KEIN URTEIL: kein Entwurf mit Bauteil {teil} " +
                      "in dieser Entwurfsliste — ");
            var waffen = new SortedSet<int>();
            foreach (var d in _designs) if (d.Weapon > 0) waffen.Add(d.Weapon);
            sb.Append("vorhandene Bauteile/Waffen: " + string.Join(",", waffen));
            GD.Print(sb); return;
        }
        sb.AppendLine($"  Entwurf {slot} \"{nm}\" traegt Bauteil {teil}");

        int owner = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        // ---- eine freie Standzelle UND einen davon verschiedenen Bauplatz ----
        //
        // ⚠ Zwei Bedingungen, und beide muessen erfuellt sein, sonst misst der
        // Lauf nichts: die Einheit muss dort stehen koennen, und das Gebaeude
        // muss am Versatz passen. Eine Stelle, die nur eine erfuellt, gaebe ein
        // »FALSCH« ueber einen Fall, der nie eintreten kann (Regel UU).
        var off = BuildOffsetOfOrder(_bauOrder);
        var start = new Vector2I(-1, -1);
        var platz = new Vector2I(-1, -1);
        var ecke  = new Vector2I(-1, -1);

        if (_bauOrder == OrderFieldMine)
        {
            // ⚠ Die Mine haengt am VORKOMMEN, nicht an einer Zelle. Gesucht wird
            // ein Vorkommen, dessen Ecke traegt, und dazu eine freie Standzelle
            // dicht daneben — die Einheit faehrt ohnehin zur Vorkommenszelle.
            var ds = _deposits;
            if (ds.Count == 0)
            { sb.Append("  KEIN URTEIL: diese Karte hat keine Vorkommen"); GD.Print(sb); return; }
            for (int k = 0; k < ds.Count && platz.X < 0; k++)
            {
                var e2 = new Vector2I(ds[k].Col + off.X, ds[k].Row + off.Y);
                if (!CanBuild(Patterns, TypeFieldMine, e2.X, e2.Y, -1, null, true)) continue;
                if (!_nav.IsFree(ds[k].Col, ds[k].Row, Simulation.NavGrid.MoveClass.Vehicle)) continue;
                for (int dr = -4; dr <= 4 && start.X < 0; dr++)
                    for (int dc = -4; dc <= 4; dc++)
                    {
                        int c = ds[k].Col + dc, r = ds[k].Row + dr;
                        if (dc == 0 && dr == 0) continue;
                        if (!_nav.InBounds(c, r)) continue;
                        if (!_nav.IsFree(c, r, Simulation.NavGrid.MoveClass.Vehicle)) continue;
                        start = new Vector2I(c, r); break;
                    }
                if (start.X < 0) continue;
                platz = new Vector2I(ds[k].Col, ds[k].Row);       // wohin geklickt wird
                ecke = e2;
            }
            if (platz.X < 0)
            { sb.Append("  KEIN URTEIL: kein tragfaehiges Vorkommen mit freiem Standplatz"); GD.Print(sb); return; }
            sb.AppendLine($"  Vorkommen ({platz.X},{platz.Y}), Standplatz ({start.X},{start.Y}) — " +
                          $"die Mine kaeme auf ({ecke.X},{ecke.Y}), Versatz ({off.X},{off.Y})");
        }
        else
        {
            for (int r = 2; r < _nav.Height - 8 && platz.X < 0; r++)
                for (int c = 2; c < _nav.Width - 8; c++)
                {
                    if (!_nav.IsFree(c, r, Simulation.NavGrid.MoveClass.Vehicle)) continue;
                    if (!CanBuild(Patterns, _bauTyp, c + off.X, r + off.Y)) continue;
                    if (start.X < 0) { start = new Vector2I(c, r); continue; }
                    if (c == start.X && r == start.Y) continue;
                    // nah genug, damit die Fahrt kurz bleibt — aber nicht dieselbe
                    if (System.Math.Abs(c - start.X) + System.Math.Abs(r - start.Y) > 12) continue;
                    platz = new Vector2I(c, r); break;
                }
            if (start.X < 0 || platz.X < 0)
            {
                sb.Append("  KEIN URTEIL: keine zwei verschiedenen Bauplaetze " +
                          "dicht beieinander gefunden");
                GD.Print(sb); return;
            }
            ecke = new Vector2I(platz.X + off.X, platz.Y + off.Y);
            sb.AppendLine($"  Standplatz ({start.X},{start.Y}), Bauplatz ({platz.X},{platz.Y}) — " +
                          $"das Gebaeude kaeme auf ({ecke.X},{ecke.Y})");
        }
        _bauEckCol = ecke.X; _bauEckRow = ecke.Y;

        // ⚠ EINGRIFF, und er steht in der Ausgabe: einen Gebaeude-Techniker
        // bringt keine Karte von Haus aus mit.
        int satz = SpawnReinforcement(slot % 200, start.X, start.Y, owner);
        if (satz < 0)
        { sb.Append("  KEIN URTEIL: die Einheit liess sich nicht absetzen"); GD.Print(sb); return; }

        // ⚠⚠ DIE FALLE FF: SpawnReinforcement gibt den SATZ zurueck, nicht die
        // Listenstelle. Gesucht wird ueber Satz UND Besitzer — die Saetze laufen
        // je Spieler von vorn. Beim Radarpruefstand hat genau das einmal ein
        // fremdes Fahrzeug getroffen und ein »FALSCH« ueber nichts gemeldet.
        int idx = -1;
        for (int i = _entities.Count - 1; i >= 0; i--)
            if (_entities[i].Slot == satz && _entities[i].Owner == owner
                && !_entities[i].IsBuilding && !_entities[i].Dead) { idx = i; break; }
        if (idx < 0)
        { sb.Append($"  KEIN URTEIL: Satz {satz} nicht wiedergefunden"); GD.Print(sb); return; }
        var u = _entities[idx];
        sb.AppendLine($"  ⚠ EINGRIFF: \"{u.Name}\" fuer Spieler {owner} auf " +
                      $"({u.Col},{u.Row}) gesetzt");

        // ---- was der Knopfweg anbietet ----
        _sel.Clear(); _sel.Add(idx); _selected = idx;
        var ws = BuildChoicesOfSelection();
        var namen = new List<string>();
        foreach (var w in ws) namen.Add($"{w.Order}=\"{w.Word}\"");
        sb.AppendLine($"  Leiste bietet {ws.Count} Auftraege an: {string.Join(" ", namen)} " +
                      (ws.Count == erwartet ? "— richtig" : $"— FALSCH, erwartet waren {erwartet}"));

        // ---- die GEGENPROBE: ein Fahrzeug OHNE das Bauteil ----
        int ohne = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (i == idx || e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Owner != owner || e.Mark < 0) continue;
            ohne = i; break;
        }
        if (ohne < 0) sb.AppendLine("  Gegenprobe: keine zweite eigene Einheit — der Fall entfaellt");
        else
        {
            _sel.Clear(); _sel.Add(ohne); _selected = ohne;
            int k = BuildChoicesOfSelection().Count;
            bool abgelehnt = !PostPlaceBuilding(ohne, platz.X, platz.Y, _bauOrder, -1);
            sb.AppendLine($"  Gegenprobe: \"{_entities[ohne].Name}\" bietet {k} Auftraege an " +
                          $"und der Befehl wird {(abgelehnt ? "abgelehnt" : "ANGENOMMEN")} " +
                          $"{(k == 0 && abgelehnt ? "— richtig, sie traegt das Bauteil nicht" : "— FALSCH")}" +
                          $"  [{BuildOrderNote}]");
        }

        // ---- SETZEN, ueber den Knopfweg ----
        _sel.Clear(); _sel.Add(idx); _selected = idx;
        _bauGebaeude0 = BuildingCount();
        bool an = BeginPlacementFromPanel(_bauOrder);
        bool ab = an && PlacementClick(platz.X, platz.Y);
        sb.AppendLine($"  Setzmodus an: {(an ? "ja" : "NEIN")}; " +
                      $"Klick auf ({platz.X},{platz.Y}) abgesetzt: " +
                      $"{(ab ? "ja" : "NEIN — " + BuildOrderNote)}");
        sb.AppendLine($"  Gebaeude vorher: {_bauGebaeude0}");

        _bauIdx = idx;
        _bauZielCol = platz.X; _bauZielRow = platz.Y;
        _bauLog = sb; _bauSim = DebugTicks;
        _bauCheck = ab ? 1 : -1;
        if (!ab) GD.Print(sb.ToString());
    }

    /// <summary>Stufe 2, EINEN Takt später: der Befehl ist durch den Ring — und
    /// es steht <b>noch nichts</b>.
    ///
    /// <para>⚠ Der Takt muss WIRKLICH weiter sein. Der Radarprüfstand hat im
    /// ersten Anlauf im selben SimTick gelesen, in dem er abgesetzt hatte, und
    /// einen richtigen Einbau als kaputt gemeldet.</para></summary>
    private void PollBauCheck2()
    {
        if (_bauCheck != 1 || _bauLog == null) return;
        if (DebugTicks <= _bauSim) return;
        _bauCheck = 2;
        var sb = _bauLog;
        var u = _entities[_bauIdx];
        int jetzt = BuildingCount();
        // ⚠ Die Nutzlast bedeutet je Modus etwas anderes: gepackte Zelle bei
        // 5 und 7, VORKOMMENSNUMMER bei 6 (@0x4C3289 gegen @0x4C32A9).
        int soll = _bauOrder == OrderFieldMine
                 ? DepositIndexAt(_bauZielCol, _bauZielRow)
                 : (_bauZielRow << 8) | _bauZielCol;
        sb.AppendLine($"  nach dem Befehl: Auftrag {u.BuildOrder} (erwartet {_bauOrder}), " +
                      $"Nutzlast {u.BuildTarget} (erwartet {soll}" +
                      (_bauOrder == OrderFieldMine ? " = Vorkommensnummer" : " = gepackte Zelle") +
                      $"), faehrt: {(u.Path != null ? "ja" : "NEIN")}");
        // ⚠⚠ DIE ZEILE, UM DIE ES GEHT.
        sb.AppendLine($"  Gebaeude jetzt: {jetzt} " +
                      (jetzt == _bauGebaeude0
                        ? "— richtig, der BEFEHL baut nichts"
                        : "— FALSCH, es ist sofort etwas entstanden"));
        _bauSim = DebugTicks;
    }

    /// <summary>Stufe 3: warten, bis sie da ist — und nachsehen, was steht.</summary>
    private void PollBauCheck3()
    {
        if (_bauCheck != 2 || _bauLog == null) return;
        if (_bauIdx < 0 || _bauIdx >= _entities.Count) { _bauCheck = -1; return; }
        var u = _entities[_bauIdx];
        // ⚠ Die Wartebedingung steht VOR der Ausgabe. Zweimal (buy-check,
        // depot-flow) sass sie dahinter und schrieb den Kopf in jeden Takt.
        bool fertig = u.Dead || (u.BuildOrder == 0 && u.Path == null);
        if (!fertig && DebugTicks - _bauSim < 3000) return;
        _bauCheck = -1;
        var sb = _bauLog;
        if (!fertig)
        { sb.Append("  KEIN URTEIL: sie ist nach 3000 Takten nicht angekommen"); GD.Print(sb); return; }

        int jetzt = BuildingCount();
        sb.AppendLine($"  ANKUNFT nach {DebugTicks - _bauSim} Takten auf ({u.Col},{u.Row})");
        sb.AppendLine($"  Gebaeude: {_bauGebaeude0} -> {jetzt} " +
                      (jetzt == _bauGebaeude0 + 1 ? "— eines gebaut" : "— FALSCH"));

        var off = BuildOffsetOfOrder(_bauOrder);
        int wantC = _bauEckCol, wantR = _bauEckRow;
        Entity? neu = null;
        for (int i = _entities.Count - 1; i >= 0; i--)
            if (_entities[i].IsBuilding && !_entities[i].Dead &&
                _entities[i].BType == _bauTyp &&
                _entities[i].Col == wantC && _entities[i].Row == wantR)
            { neu = _entities[i]; break; }
        // ⚠ Der Versatz ist NICHT einheitlich: (−1,−1) fuer Depot und Generator
        // (@0x408126 / @0x40833E), (−1,−2) fuer die Mine (@0x408234 gegen
        // @0x408228). Wer daraus eine Regel machte, verschoebe die Mine.
        sb.AppendLine(neu == null
            ? $"  ⚠ FALSCH: auf ({wantC},{wantR}) steht kein {BuildingTypeName(_bauTyp)}"
            : $"  auf ({wantC},{wantR}) steht ein {BuildingTypeName(_bauTyp)}, " +
              $"Besitzer {neu.Owner}, Huelle {neu.Hp}/{neu.HpMax}" +
              (_bauTyp == TypeFieldMine ? $", im Boden {neu.Deposit}" : "") +
              $" — RICHTIG (Versatz {off.X},{off.Y})");

        // ⚠⚠ DER PREIS. Ohne diese Zeile waere nicht gemessen, dass das Fahrzeug
        // aufgeht — und genau das ist der Befund, der diese Mechanik von einer
        // mit Rohstoffkosten unterscheidet.
        sb.AppendLine("  das Fahrzeug: " +
                      (u.Dead ? "ist weg — RICHTIG, es war der Preis"
                              : "STEHT NOCH — FALSCH"));
        sb.AppendLine($"  {BuildOrderLine()}");
        GD.Print(sb.ToString());
    }
}
