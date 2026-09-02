using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>`--befehlsklang-probe` — FÄLLT DER KLANG, WENN DER SPIELER EINEN BEFEHL
/// GIBT?</b>
///
/// <para>⚠ 03.09.2026, zum dritten Mal gemeldet (19.08., 02.09., 02.09. abends:
/// »hab immer noch nix gehoert«): beim FAHRBEFEHL kommt kein Einheitenklang,
/// beim Anwählen schon.</para>
///
/// <para><b>Warum `--befehl-check` das nicht sehen konnte:</b> der prüft je
/// Bauart, ob <c>OrderVoice</c> einen Platz liefert und ob die Bank dort einen
/// Klang hat — vier Glieder der Kette, und das ERSTE Glied (»SpeakOrdered
/// wird gerufen«) hat er nur behauptet, nie ausgelöst. Genau dort sass der
/// Fehler: alle drei Rufer von <see cref="SpeakOrdered"/> lagen in
/// <see cref="IssueMove"/> und <see cref="IssueAttack"/>, dem alten
/// Direktweg — und der Klick des Spielers läuft seit dem 22.08.2026 über
/// <c>PostMove</c>/<c>PostAttack</c> → Befehlsring → <c>ApplyMove</c>/
/// <c>ApplyAttack</c> (MapViewer.cs :4673–:4677), und keiner davon rief den
/// Klang. Dreimal »bestanden«, dreimal still.</para>
///
/// <para><b>Was diese Probe tut:</b> sie wählt alle eigenen fahrenden
/// Einheiten an und setzt einen ECHTEN Fahrbefehl über <c>PostMove</c> ab —
/// denselben Aufruf, den MapViewer beim Rechtsklick macht —, dreissig Takte
/// später einen ECHTEN Angriffsbefehl über <c>PostAttack</c> (Rückfall:
/// <c>PostAttackGround</c>, der Strg-Zellangriff). Danach liest sie die
/// Zähler, die im Klangweg selbst sitzen
/// (<see cref="BefehlsklangGerufen"/>, <see cref="BefehlsklangGespielt"/>,
/// <see cref="BefehlsklangStummGrund"/>), und daneben, ob der Behandler die
/// Sätze überhaupt angenommen hat (<see cref="BefehleAngewendet"/>).</para>
///
/// <para>⭐ <b>NULLMODELL:</b> ein Fahrbefehl auf eine Auswahl von N Einheiten
/// muss genau <b>1</b> Fahrklang ergeben — nicht 0 (der Weg ruft den Klang
/// nicht) und nicht N (jede Einheit spricht; das Original lässt mit
/// <c>0x429220</c> nur das wichtigste Mitglied sprechen). Ebenso genau 1
/// Angriffsklang für den Angriffsbefehl. Und »angewendet« muss ≥ 1 sein,
/// sonst kam der Befehl gar nicht an und der Klang belegt nichts.</para>
///
/// <para>Gegenprobe: <c>--befehlsklang-weg-alt</c> hängt den Klang wieder an
/// den Direktweg — dann muss diese Probe <b>0/0</b> melden, weil der
/// Spielerweg ihn nie erreicht. Das ist der Stand vor dem 03.09.2026.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private bool _bkProbeLaeuft;
    private int _bkTakt;
    private int _bkAuswahl;
    private int _bkSaetzeFahren = -1, _bkSaetzeAngriff = -1;
    private int _bkAngewendetVor0, _bkAngewendetVor1;
    private string _bkAngriffsweg = "noch nicht abgesetzt";
    private Vector2I _bkFahrziel;

    /// <summary>In diesem Takt (seit dem Start) wird der Angriffsbefehl
    /// abgesetzt — weit genug hinter dem Fahrbefehl, dass die beiden Sätze im
    /// Ring getrennt fällig werden.</summary>
    private const int BkAngriffTakt = 30;

    /// <summary>Wählt die eigenen fahrenden Einheiten an und setzt den
    /// Fahrbefehl ab — über <c>PostMove</c>, wie der Rechtsklick. Gibt die
    /// Startzeilen aus.</summary>
    public string BefehlsklangProbeStart()
    {
        if (_nav == null)
            return "befehlsklang-probe: ⚠ kein Navigationsgitter — die Probe misst NICHTS";

        // Die AUSWAHL wie im Spiel: _sel und SetPrimary, nicht eine eigene
        // Liste. SprecherDerAuswahl liest _sel, PostMove liest _sel — eine
        // Probe, die daran vorbei arbeitet, misst einen anderen Weg.
        _sel.Clear();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
            if (e.Owner != ViewPlayer) continue;
            if (e.DugIn || e.LeavingDock >= 0) continue;
            _sel.Add(i);
        }
        SetPrimary();
        _bkAuswahl = _sel.Count;
        if (_bkAuswahl == 0)
            return "befehlsklang-probe: ⚠ keine eigene fahrende Einheit — die Probe misst NICHTS";

        var e0 = _entities[_selected];
        // Sechs Zellen nach rechts: nah genug, dass PickGoalCell (Radius 8 um
        // den Klick) fuer jede Einheit eine freie Zelle findet, weit genug,
        // dass es ein Befehl und kein Stehenbleiben ist.
        _bkFahrziel = new Vector2I(Mathf.Clamp(e0.Col + 6, 0, _nav.Width - 1),
                                   Mathf.Clamp(e0.Row, 0, _nav.Height - 1));

        int gerufenVor = BefehlsklangGerufen[0], gespieltVor = BefehlsklangGespielt[0];
        _bkAngewendetVor0 = BefehleAngewendet[0];
        _bkAngewendetVor1 = BefehleAngewendet[1];
        _bkProbeLaeuft = true;
        _bkTakt = 0;

        // ⭐ DER WEG DES SPIELERS, Wort fuer Wort: MapViewer.cs :4677 ruft
        // `_entities.PostMove(GetGlobalMousePosition(), mb.ShiftPressed)`.
        _bkSaetzeFahren = PostMove(CellCenterFor(_bkFahrziel), queue: false);

        var sb = new System.Text.StringBuilder();
        sb.Append($"befehlsklang-probe: {_bkAuswahl} eigene Einheiten angewaehlt, " +
                  $"Sprecher nach Wichtigkeit: Platz {(SprecherDerAuswahl() is var sp && sp >= 0 ? _entities[sp].Slot.ToString() : "KEINER")}\n");
        sb.Append($"   Fahrbefehl ueber PostMove nach ({_bkFahrziel.X},{_bkFahrziel.Y}): " +
                  $"{_bkSaetzeFahren} Satz/Saetze abgesetzt | Fahrklang gerufen " +
                  $"{BefehlsklangGerufen[0] - gerufenVor}, gespielt {BefehlsklangGespielt[0] - gespieltVor}\n");
        sb.Append($"   Ansagen: {(UI.Settings.Announcements ? "an" : "AUS — dann kann nichts gespielt werden")}, " +
                  $"Weg: {(BefehlsklangWegAlt ? "ALT (Klang nur im Direktweg IssueMove/IssueAttack)" : "NEU (Klang im Absender PostMove/PostAttack)")}");
        return sb.ToString();
    }

    /// <summary>Je Takt: im Takt <see cref="BkAngriffTakt"/> den Angriffsbefehl
    /// absetzen. Erst ein echter Feind über <c>PostAttack</c> (mit
    /// <see cref="PickOhneNebel"/>, weil ein kopfloser Lauf nach einer Sekunde
    /// noch alles im Nebel hat und <c>Pick</c> sonst nichts trifft), sonst der
    /// Zellangriff <c>PostAttackGround</c> — beides Zeigerart 2 des Originals,
    /// beides der Angriffsklang.</summary>
    public void BefehlsklangProbeTick()
    {
        if (!_bkProbeLaeuft) return;
        _bkTakt++;
        if (_bkTakt != BkAngriffTakt) return;
        if (_selected < 0 || _selected >= _entities.Count) { _bkAngriffsweg = "keine Auswahl mehr"; return; }
        var e0 = _entities[_selected];

        int opfer = -1; float best = float.MaxValue;
        for (int i = 0; i < _entities.Count; i++)
        {
            var v = _entities[i];
            if (v.IsProp || v.Dead || v.Owner == ViewPlayer) continue;
            if (!IsHostile(e0, v)) continue;
            float d = new Vector2(v.Col - e0.Col, v.Row - e0.Row).LengthSquared();
            if (d < best) { best = d; opfer = i; }
        }

        int gerufenVor = BefehlsklangGerufen[1], gespieltVor = BefehlsklangGespielt[1];
        bool ok = false;
        if (opfer >= 0)
        {
            bool nebelVor = PickOhneNebel;
            PickOhneNebel = true;
            ok = PostAttack(_entities[opfer].Pos, queue: false);
            PickOhneNebel = nebelVor;
            _bkAngriffsweg = ok
                ? $"PostAttack auf Platz {_entities[opfer].Slot} (Spieler {_entities[opfer].Owner}, {Mathf.Sqrt(best):0} Zellen weit)"
                : $"PostAttack auf Platz {_entities[opfer].Slot} ABGELEHNT (niemand in der Auswahl kann schiessen?)";
        }
        if (!ok)
        {
            var z = new Vector2I(Mathf.Clamp(e0.Col + 3, 0, _nav!.Width - 1),
                                 Mathf.Clamp(e0.Row + 3, 0, _nav.Height - 1));
            ok = PostAttackGround(CellCenterFor(z), queue: false);
            _bkAngriffsweg += (opfer >= 0 ? "; Rueckfall " : "kein Feind auf der Karte; ") +
                              $"PostAttackGround auf ({z.X},{z.Y}): {(ok ? "abgesetzt" : "ABGELEHNT")}";
        }
        _bkSaetzeAngriff = ok ? 1 : 0;
        GD.Print($"befehlsklang-probe: Takt {_bkTakt}, Angriffsbefehl: {_bkAngriffsweg} | " +
                 $"Angriffsklang gerufen {BefehlsklangGerufen[1] - gerufenVor}, " +
                 $"gespielt {BefehlsklangGespielt[1] - gespieltVor}");
    }

    /// <summary>Die Tafel.</summary>
    public string BefehlsklangProbeLine()
    {
        if (!_bkProbeLaeuft) return "befehlsklang-probe: nicht gestartet";
        var sb = new System.Text.StringBuilder();
        int angF = BefehleAngewendet[0] - _bkAngewendetVor0;
        int angA = BefehleAngewendet[1] - _bkAngewendetVor1;
        sb.Append($"befehlsklang-probe: Auswahl {_bkAuswahl} Einheiten, {_bkTakt} Takte, " +
                  $"Weg {(BefehlsklangWegAlt ? "ALT (--befehlsklang-weg-alt)" : "NEU")}\n");
        // Ein KLICK ist ein Befehl; der Ring traegt je Einheit einen Satz.
        // Darum stehen hier Befehle (1) und angewendete SAETZE (N) nebeneinander
        // — und der Klang muss der Befehlszahl folgen, nicht der Satzzahl.
        sb.Append("   Befehl      Befehle abgesetzt   Saetze angewendet   Klang gerufen   Klang gespielt\n");
        sb.Append($"   FAHREN      {(_bkSaetzeFahren > 0 ? 1 : 0),17}   {angF,17}   {BefehlsklangGerufen[0],13}   {BefehlsklangGespielt[0],14}\n");
        sb.Append($"   ANGRIFF     {(_bkSaetzeAngriff < 0 ? "-" : _bkSaetzeAngriff.ToString()),17}   {angA,17}   {BefehlsklangGerufen[1],13}   {BefehlsklangGespielt[1],14}\n");
        sb.Append($"   Angriffsweg: {_bkAngriffsweg}\n");
        if (BefehlsklangStummGrund.Count > 0)
        {
            sb.Append("   nicht gespielt, weil:\n");
            foreach (var kv in BefehlsklangStummGrund)
                sb.Append($"      {kv.Value,3}x {kv.Key}\n");
        }
        // ⚠ »angewendet« zaehlt ALLE Fahrsaetze des Behandlers seit dem Start
        // der Probe — auch ein Missionsbefehl (order_at) des Skripts liefe
        // durch denselben Ring. Er stuende dann als Ueberschuss ueber
        // »abgesetzt«, nicht als Fehlbetrag.
        sb.Append($"   ⭐ NULLMODELL: {_bkAuswahl} Einheiten, EIN Fahrbefehl -> Fahrklang gespielt muss 1 sein " +
                  $"(nicht 0, nicht {_bkAuswahl}); ein Angriffsbefehl -> Angriffsklang 1.\n");
        int rc = BefehlsklangProbeRc();
        sb.Append(rc switch
        {
            0 => "   bestanden — je Befehl genau EIN Klang, und die Saetze sind angekommen.\n",
            2 => "   ⚠ NICHT GEMESSEN — kein Satz abgesetzt oder keiner angewendet; die Zahlen sagen nichts.\n",
            _ => $"   ⚠⚠ DURCHGEFALLEN — Fahrklang {BefehlsklangGespielt[0]} statt 1, " +
                 $"Angriffsklang {BefehlsklangGespielt[1]} statt {(_bkSaetzeAngriff > 0 ? 1 : 0)}.\n",
        });
        return sb.ToString();
    }

    /// <summary>0 = bestanden, 1 = durchgefallen, 2 = nichts gemessen.</summary>
    public int BefehlsklangProbeRc()
    {
        if (!_bkProbeLaeuft || _bkSaetzeFahren <= 0) return 2;
        if (BefehleAngewendet[0] - _bkAngewendetVor0 <= 0) return 2;
        bool fahren = BefehlsklangGespielt[0] == 1;
        // Der Angriff zaehlt nur, wenn er ueberhaupt abgesetzt werden konnte.
        bool angriff = _bkSaetzeAngriff <= 0 || BefehlsklangGespielt[1] == 1;
        return fahren && angriff ? 0 : 1;
    }
}
