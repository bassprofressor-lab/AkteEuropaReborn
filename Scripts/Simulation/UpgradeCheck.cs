namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--ausbau-check</c> — <b>tut der Knopf »Lagerausbau« wirklich etwas?</b>
///
/// <para>Er geht über <b>denselben Weg wie der Knopf</b>
/// (<see cref="UpgradeChoiceOfSelection"/> → <see cref="StartUpgrade"/>), nicht
/// über die Felder. Bis zum 18.08.2026 lag die Mechanik nur auf den Tasten V
/// und C und war für den Spieler nicht vorhanden; ein Prüfstand, der nur die
/// Felder anfasst, hätte das nie gemerkt.</para>
///
/// <para><b>Gemessen wird gegen die gelesenen Zahlen:</b> der Lagerplatz steigt
/// um <b>10</b> (@0x43E0F1 <c>add word[+0x87A2C8], 0xA</c>) und der Preis
/// <b>dieses</b> Ausbaus auf das Anderthalbfache (@0x43E0FC, <c>×3/2</c>) —
/// ⚠ und der andere Preis bleibt, wie er war. Das ist der Teil, den man leicht
/// falsch baut: das Original führt <b>zwei getrennte Preisfelder</b>
/// (+0x0A und +0x0C), und jeder Ausbau verteuert nur seinen eigenen.</para>
///
/// <para>⚠ Auch das Geld wird nachgehalten. Ein Ausbau, der nichts kostet,
/// sähe an Platz und Preis genauso aus.</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _ausbauCheck = -1;
    private int _ausbauIdx = -1, _platz0, _preisL0, _preisP0, _geld0;
    private long _ausbauSim;
    private System.Text.StringBuilder? _ausbauLog;

    /// <summary><c>--ausbau-check</c> anwerfen.</summary>
    public void AusbauCheckStart() => _ausbauCheck = 0;

    private void PollAusbauCheck()
    {
        if (_ausbauCheck != 0) return;
        _ausbauCheck = -1;
        var sb = new System.Text.StringBuilder("ausbau-check\n");

        // eine eigene, gerade untätige Fabrik
        int idx = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!IsFactory(e) || e.Dead || e.Owner != ViewPlayer) continue;
            if (e.State != StAktiv) continue;
            idx = i; break;
        }
        if (idx < 0)
        { sb.Append("  KEIN URTEIL: keine eigene, untaetige Fabrik"); GD.Print(sb); return; }

        var f = _entities[idx];
        _sel.Clear(); _sel.Add(idx); _selected = idx;

        // ---- was der KNOPFWEG anbietet ----
        var w = UpgradeChoiceOfSelection();
        sb.AppendLine($"  {BuildingTypeName(f.BType)} \"{f.Name}\" auf ({f.Col},{f.Row}), " +
                      $"Zustand {StateName(f)}");
        sb.AppendLine(w == null
            ? "  ⚠ FALSCH: die Leiste bietet gar keinen Ausbau an"
            : $"  Knopfweg bietet an: Lagerausbau ${w.Value.CostStore}, " +
              $"Produktionserw. ${w.Value.CostProd}, bedienbar {w.Value.Ready}");
        if (w == null) { GD.Print(sb); return; }

        // ---- die GEGENPROBE: an einem Gebäude, das keine Fabrik ist ----
        int keine = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.IsProp || e.Dead || IsFactory(e)) continue;
            if (e.Owner != ViewPlayer) continue;
            keine = i; break;
        }
        if (keine < 0) sb.AppendLine("  Gegenprobe: kein anderes eigenes Gebaeude — entfaellt");
        else
        {
            _sel.Clear(); _sel.Add(keine); _selected = keine;
            bool leer = UpgradeChoiceOfSelection() == null;
            sb.AppendLine($"  Gegenprobe: {BuildingTypeName(_entities[keine].BType)} bietet " +
                          (leer ? "keinen Ausbau an — richtig, sie ist keine Fabrik"
                                : "einen Ausbau an — FALSCH"));
            _sel.Clear(); _sel.Add(idx); _selected = idx;
        }

        int owner = Mathf.Clamp(f.Owner, 0, 7);
        _platz0 = f.Capacity; _preisL0 = f.CostStore; _preisP0 = f.CostProd;
        _geld0 = _money[owner];
        sb.AppendLine($"  vorher: Platz {_platz0}, Preis Lager ${_preisL0}, " +
                      $"Preis Produktion ${_preisP0}, Konto ${_geld0}");

        // ---- DRÜCKEN, über den Knopfweg ----
        // bug-600 (Fortsetzung): der Fensterknopf schickt 509 in den Ring
        // (PostUpgradeFromPanel, wie BaseWindow.OnUpgrade); erst leeren, dann
        // Zustand und Konto lesen.
        PostUpgradeFromPanel(true);
        int ringA = RingLeeren();
        sb.AppendLine($"  Ring {ringA} Takt(e) geleert");
        sb.AppendLine($"  gedrueckt: Zustand jetzt {StateName(f)} " +
                      (f.State == FaExpand ? "— richtig, der Ausbau laeuft" : "— FALSCH"));
        sb.AppendLine($"  Konto ${_geld0} -> ${_money[owner]} " +
                      (_money[owner] == _geld0 - _preisL0
                       ? $"(−${_preisL0}, richtig)" : "— FALSCH"));

        _ausbauIdx = idx; _ausbauLog = sb; _ausbauSim = DebugTicks;
        _ausbauCheck = 1;
    }

    /// <summary>Stufe 2: warten, bis die 100 Schritte durch sind, und nachsehen.
    ///
    /// <para>⚠ Die Wartebedingung steht VOR der Ausgabe — sonst schriebe der
    /// Prüfstand seinen Kopf in jeden Takt (zweimal passiert, buy-check und
    /// depot-flow).</para></summary>
    private void PollAusbauCheck2()
    {
        if (_ausbauCheck != 1 || _ausbauLog == null) return;
        if (_ausbauIdx < 0 || _ausbauIdx >= _entities.Count) { _ausbauCheck = -1; return; }
        var f = _entities[_ausbauIdx];
        bool fertig = f.Dead || f.State != FaExpand;
        if (!fertig && DebugTicks - _ausbauSim < 60000) return;
        _ausbauCheck = -1;
        var sb = _ausbauLog;
        if (!fertig)
        {
            sb.Append($"  KEIN URTEIL: nach 60000 Takten immer noch bei " +
                      $"{PercentDone(f)} %");
            GD.Print(sb); return;
        }

        sb.AppendLine($"  FERTIG nach {DebugTicks - _ausbauSim} Takten, " +
                      $"Zustand {StateName(f)}");
        sb.AppendLine($"  Platz {_platz0} -> {f.Capacity} " +
                      (f.Capacity == _platz0 + 10
                       ? "(+10, richtig — @0x43E0F1)" : "— FALSCH, erwartet +10"));
        sb.AppendLine($"  Preis Lager ${_preisL0} -> ${f.CostStore} " +
                      (f.CostStore == _preisL0 * 3 / 2
                       ? "(x3/2, richtig — @0x43E0FC)" : "— FALSCH, erwartet x3/2"));
        // ⚠⚠ DIE ZEILE, DIE DEN HAEUFIGEN FEHLER FAENGT: die zwei Ausbauten
        // haben GETRENNTE Preisfelder. Wer beide aus einem bezahlen liesse,
        // machte den zweiten Ausbau zu teuer, und an Platz und Preis des ersten
        // waere davon nichts zu sehen.
        sb.AppendLine($"  Preis Produktion ${_preisP0} -> ${f.CostProd} " +
                      (f.CostProd == _preisP0
                       ? "(unveraendert, richtig — es sind zwei Felder)"
                       : "— FALSCH, der Lagerausbau hat den anderen Preis angefasst"));
        GD.Print(sb.ToString());
    }

    // ================= die zwei anderen neuen Knöpfe ==========================

    private int _knopfCheck = -1;
    private (int, int, int)? _eingriffTeile;

    /// <summary><c>--knopf-check</c> anwerfen.</summary>
    public void KnopfCheckStart() => _knopfCheck = 0;

    /// <summary>
    /// <c>--knopf-check</c> — <b>»Abbrechen« und »Starten«: stehen sie da, wenn
    /// es etwas zu tun gibt, und verschwinden sie, wenn nicht?</b>
    ///
    /// <para>Beide Mechaniken (<see cref="CancelBuild"/>,
    /// <see cref="LaunchAircraft"/>) gab es längst; neu ist, dass man sie
    /// FINDET. Geprüft wird deshalb genau das Neue: die Auskunft, an der die
    /// Knöpfe hängen — <b>und in beide Richtungen</b>, denn ein Knopf, der
    /// immer da ist, wäre so falsch wie einer, der nie da ist.</para>
    ///
    /// <para>⚠ Er stellt seinen Fall selbst her: er lässt eine eigene Fabrik
    /// über den Knopfweg (<see cref="ProduceFromSelection"/>) etwas bestellen.
    /// Auf eine Karte zu hoffen, auf der gerade gebaut wird, wäre kein
    /// Prüfstand.</para></summary>
    private void PollKnopfCheck()
    {
        if (_knopfCheck != 0) return;
        _knopfCheck = -1;
        var sb = new System.Text.StringBuilder("knopf-check\n");
        LoadDesigns();

        // ---- »Abbrechen« ----
        int werk = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!IsUnitPlant(e) || e.Dead || e.Owner != ViewPlayer) continue;
            werk = i; break;
        }
        if (werk < 0) sb.AppendLine("  Abbrechen: keine eigene Fertigung — der Fall entfaellt");
        else
        {
            var e = _entities[werk];
            _sel.Clear(); _sel.Add(werk); _selected = werk;
            string? vorher = CancelChoiceOfSelection();
            sb.AppendLine($"  {BuildingTypeName(e.BType)} \"{e.Name}\": vor der Bestellung " +
                          (vorher == null ? "nichts abzubrechen — richtig"
                                          : $"schon \"{vorher}\" — der Fall ist unsauber"));
            int owner = Mathf.Clamp(e.Owner, 0, 7);
            int geld = _money[owner];
            // bug-600 (Fortsetzung): Taste B / Knopf »Produzieren« (503) und
            // »Abbrechen« (2004) gehen ueber den Ring wie im Spiel; nach jedem
            // Druck leeren, erst dann die Knoepfe lesen.
            _order = "";
            int gelegtVor = NeubauInListeGelegt;
            PostProduzieren();
            int ringP = RingLeeren();
            int gelegt = NeubauInListeGelegt - gelegtVor;
            string? jetzt = CancelChoiceOfSelection();
            // ⚠ 22.10.2026 (bug-600 Fortsetzung): seit bug-462 (06.10.) hat der
            // Spielerbau KEINE Bauzeit mehr (0x4B1840 ohne Zeitfeld) — die Einheit
            // liegt sofort als Satz in der Basis, es bleibt nichts abzubrechen.
            // Bis heute verlangte dieser Punkt trotzdem einen Abbrechen-Knopf und
            // war seit dem 06.10. ROT, ohne dass am Spiel etwas falsch war.
            // Gemessen wird jetzt: der Bau WIRKT (ein Satz gelegt) und der Knopf
            // bleibt weg.
            sb.AppendLine($"  nach dem Knopf »Produzieren« (Ring {ringP} Takt): {gelegt} Satz gelegt, " +
                          (jetzt == null ? (gelegt > 0 ? "kein Abbrechen-Knopf — richtig (keine Bauzeit, bug-462)"
                                                       : "NICHTS gebaut — FALSCH oder nichts bestellbar")
                                         : $"»Abbrechen ({jetzt})« steht da — FALSCH, es gibt keine Bauzeit") +
                          $"  [Konto ${geld} -> ${_money[owner]}]" +
                          (_order.Length > 0 ? $"  Meldung »{_order}«" : ""));
            // Den Abbrechen-Satz 2004 trotzdem ueber den Ring pruefen: ⚠ EINGRIFF,
            // eine Bestellung mit bezahlten Teilen in die Warteschlange (wie sie die
            // KI-Wege mit Bauzeit noch anlegen).
            if (jetzt == null && _designs is { Count: > 0 })
            {
                var menu = BuildableFor(e.BType, e.Owner);
                if (menu.Count > 0)
                {
                    var d = _designs[menu[0] % _designs.Count];
                    int w0 = e.StockW, f0 = e.StockF, s0 = e.StockS;
                    e.StockW -= d.CostW; e.StockF -= d.CostF; e.StockS -= d.CostS;
                    e.BuildQueue.Add(menu[0]);
                    jetzt = CancelChoiceOfSelection();
                    sb.AppendLine($"  ⚠ EINGRIFF: \"{d.Name}\" mit bezahlten Teilen in die Warteschlange — " +
                                  (jetzt != null ? "»Abbrechen« steht da — richtig" : "KEIN Abbrechen-Knopf — FALSCH"));
                    geld = _money[owner];
                    _eingriffTeile = (w0, f0, s0);
                }
            }
            if (jetzt != null)
            {
                PostBauAbbrechen();
                int ringA = RingLeeren();
                string? danach = CancelChoiceOfSelection();
                if (_eingriffTeile is { } t)
                    sb.AppendLine($"  Abbrechen ueber den Ring ({ringA} Takt): Teile " +
                                  ((e.StockW, e.StockF, e.StockS) == t ? "zurueck — richtig" : "NICHT zurueck — FALSCH"));
                sb.AppendLine($"  nach dem Knopf »Abbrechen«: " +
                              (danach == null ? "der Knopf ist weg — richtig"
                                              : $"steht immer noch \"{danach}\" — FALSCH") +
                              $"  [Konto ${_money[owner]}" +
                              (_money[owner] == geld ? ", erstattet — richtig" : "") + "]");
            }
        }

        // ---- »Starten« ----
        int hafen = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding && !e.Dead && e.BType == 9 && e.Owner == ViewPlayer) { hafen = i; break; }
        }
        if (hafen < 0)
        {
            // ⚠ Das ist die halbe Auskunft, und sie steht als solche da: auf
            // dieser Karte gibt es keinen Flughafen, also ist der Knopf
            // »Starten« hier NICHT gemessen. Ein »richtig« an dieser Stelle
            // waere eine Behauptung ueber einen Fall, der nicht eintritt.
            sb.Append("  Starten: kein eigener Flughafen auf dieser Karte — " +
                      "NICHT GEMESSEN (nur die Gegenrichtung: der Knopf bleibt weg)");
            sb.AppendLine();
            _sel.Clear(); if (werk >= 0) { _sel.Add(werk); _selected = werk; }
            sb.AppendLine($"    Gegenprobe: HangarOfSelection() = " +
                          (HangarOfSelection() == null ? "null — richtig" : "eine Zahl — FALSCH"));
        }
        else
        {
            var h = _entities[hafen];
            _sel.Clear(); _sel.Add(hafen); _selected = hafen;
            int n = HangarOfSelection() ?? -1;
            if (n == 0)
            {
                // ⚠ KULISSE (22.10.2026, bug-600 Fortsetzung): ein leerer Hangar
                // misst »Alle starten« nicht. Zwei Probejaeger hinein, damit der
                // Knopf etwas zu tun hat; danach die Gegenrichtung wie bisher.
                sb.AppendLine($"  Flughafen \"{h.Name}\": Hangar 0, Knopf bleibt weg (leer) — richtig");
                ProbeMaschine(h, art: 1, stored: true, owner: h.Owner);
                ProbeMaschine(h, art: 1, stored: true, owner: h.Owner);
                n = HangarOfSelection() ?? -1;
                sb.AppendLine($"  ⚠ KULISSE: zwei Probejaeger in den Hangar gestellt");
            }
            sb.AppendLine($"  Flughafen \"{h.Name}\": Hangar {n}, Knopf " +
                          (n > 0 ? "steht da" : "bleibt weg (leer)"));
            if (n > 0)
            {
                // bug-600 (Fortsetzung): »Alle starten« ist Satz 2006. Der
                // Rueckgabewert von PostAlleStarten zaehlt SAETZE (0/1), nicht
                // Starts — gezaehlt wird darum am ZUSTAND: Hangar vorher − nachher.
                PostAlleStarten(ViewPlayer);
                RingLeeren();
                int rest = HangarOfSelection() ?? -1;
                int los = rest >= 0 ? n - rest : 0;
                // ⚠⚠ DIESE ZEILE HAT EINEN ECHTEN FEHLER GEFUNDEN: sie meldete
                // »gestartet: 6, Hangar jetzt 6«. `LaunchAircraft` hat das
                // Flugzeug fliegen lassen, es aber nicht aus der Hangarliste
                // genommen — anders als `SendOutFromPanel`. Zwei Wege in
                // denselben Zustand, und nur einer raeumte auf.
                sb.AppendLine($"    »Alle starten«: {los} gestartet, Hangar {n} -> {rest} " +
                              (rest == 0 ? "— richtig, er ist leer"
                                         : "— FALSCH, die Liste wurde nicht geraeumt"));
            }
        }
        GD.Print(sb.ToString());
    }
}
