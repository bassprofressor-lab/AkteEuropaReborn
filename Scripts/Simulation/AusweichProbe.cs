using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>--ausweich-probe — WIRD EIN FEIND GEFRAGT ODER NICHT?</b>
///
/// <para>Gebaut am 31.08.2026, weil die Buendnispruefung aus <c>0x4054D0</c>
/// gebaut, belegt und <b>ungemessen</b> war: auf map_DM_4 meldete der Zaehler
/// in elf Laeufen <c>feind 0</c>, und Kampagne 2 kam mit 6 von 7 Einheiten
/// nicht einmal aus dem Sprit heraus. <b>Der Pruefstand enthielt den
/// Gegenstand nicht</b> — dieselbe Lage wie seinerzeit bei
/// <c>--ki-probe</c>.</para>
///
/// <para><b>Was gemessen wird.</b> <c>Can_go</c> @0x4055D0 macht VOR der
/// Anfrage den Test <c>0x4054D0</c>:</para>
/// <code>
///   imap &lt; 8000    -> neutok[fahrer/1000][ziel/1000]   Matrix 0x87B155
///   imap &lt; 14000   -> pratelska_infa 0x433FE0
///   sonst          -> 0
/// </code>
/// <para>Gibt er 0, kehrt <c>Can_go</c> mit 0 zurueck, <b>ohne 0x404D20 auch
/// nur zu rufen</b>. Einen Feind bittet man nicht zur Seite; um ihn plant man
/// herum.</para>
///
/// <para><b>Die Messlatte steht vorher fest, und sie ist gelesen.</b> Die
/// Vorbelegung der Matrix @0x419529 setzt genau die Diagonale
/// (<c>neutok[p][p] = 1</c>, Schrittweite 41 ueber eine Spielerbreite von 40):
/// jeder ist jedem feindlich, sich selbst aber nicht. Daraus folgt:</para>
/// <list type="bullet">
/// <item><b>Feind im Weg:</b> <c>AusweichFeind</c> steigt.</item>
/// <item><b>Eigener im Weg:</b> <c>AusweichFeind</c> bleibt bei NULL, obwohl
/// gefragt wird.</item>
/// </list>
///
/// <para>⚠ <b>Eine erste Messlatte war unbrauchbar, und das gehoert
/// hierher.</b> Sie lautete zusaetzlich »<c>AusweichGefragt</c> bleibt im
/// Feindfall stehen«. Das ist nicht messbar: die Zaehler sind GLOBAL, und der
/// Aufbau laesst achtundvierzig Einheiten gleichzeitig fahren, die einander
/// dauernd blockieren — im Feindfall liefen 443 bis 700 Anfragen mit, die mit
/// dem gestellten Feind nichts zu tun hatten. <b>Nicht die Messlatte
/// nachtraeglich passend machen, sondern sagen, warum sie falsch war:</b> sie
/// verlangte von einer globalen Zahl eine oertliche Aussage. Tragfaehig ist
/// allein der KONTRAST in <c>AusweichFeind</c> — und der ist eindeutig, weil
/// im Freundfall bei 339 bis 430 Anfragen exakt NULL Ablehnungen auftreten.</para>
///
/// <para><b>Gemessen am 31.08.2026</b>, map_DM_4, drei Keime:</para>
/// <code>
///   Keim    Feind im Weg        Eigener im Weg
///     7     feind +20           feind +0  (bei 339 Anfragen)
///    23     feind +28           feind +0  (bei 430 Anfragen)
///    41     feind +27           feind +0  (bei 351 Anfragen)
/// </code>
/// <para>Das Nullmodell lag schon vor: in elf Laeufen OHNE gestellten Feind
/// meldete der Zaehler <c>feind 0</c>. Und die Diagnosezeile zeigt den zweiten
/// Teil der Lesung mit: <c>Block 19 -> 15 -> 26</c>, der Geduldszaehler laeuft,
/// der Feindfall geht also wirklich in den Geduldszweig @0x408BAB und nicht in
/// den 1/60-Zweig.</para>
///
/// <para>⚠ <b>Die Reihenfolge ist der ganze Trick.</b> Wer den Blockierer
/// zuerst hinstellt und dann das Ziel setzt, misst nichts: die Wegsuche plant
/// um besetzte Zellen herum, und es kommt nie zu einer Blockade. Der Weg muss
/// ZUERST stehen, und der Blockierer kommt DANN auf dessen naechste Zelle —
/// genau so, wie es im Spiel passiert.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _awProbe = -1, _awFahrer = -1, _awBlock = -1;
    private float _awUhr;
    private float _awTicker;
    private Vector2I _awZiel, _awStart;
    private int _awFeindVor, _awGefragtVor;
    private string _awFeindfall = "", _awFreundfall = "";

    /// <summary>Wie lange je Fall gefahren wird. Drei Sekunden sind reichlich —
    /// der Fahrer probiert seinen Schritt in jedem Takt neu.</summary>
    private const float AwProbeSekunden = 3f;

    /// <summary><c>--ausweich-probe</c> starten.</summary>
    public void AusweichProbeStart() { _awProbe = 0; }

    /// <summary>Einen Blockierer auf eine Zelle stellen, sauber ausgebucht.</summary>
    private void AwStelle(int idx, Vector2I z)
    {
        var b = _entities[idx];
        _nav.ClearOccupant(b.Col, b.Row, idx);
        if (b.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, idx);
        b.Reserved = null; b.Path = null; b.Orders.Clear(); b.Target = -1;
        b.Col = z.X; b.Row = z.Y;
        b.Elev = ElevOf(z.X, z.Y);
        b.Pos = BodyCenterAt(b, z.X, z.Y);
        b.Footprint = CellRect(_ox, _oy, z.X, z.Y, b.Elev);
        _nav.SetOccupant(z.X, z.Y, idx, b.Infantry >= 0);
    }

    /// <summary>Die naechste Zelle des Weges — dorthin kommt der Blockierer.</summary>
    private Vector2I? AwNaechste(Entity e)
        => e.Path is { Count: > 0 } && e.PathIdx < e.Path.Count
           ? e.Path[e.PathIdx] : null;

    private void PollAusweichProbe(float dt)
    {
        if (_awProbe < 0 || _nav == null) return;
        switch (_awProbe)
        {
            case 0:
            {
                // ⚠⚠ ALLE BEFEHLIGEN, NICHT EINEN — zwei Fehlversuche haben
                // dahin gefuehrt, und beide sahen wie ein Befund aus:
                //
                //   1. `Goal`/`Path`/`PathIdx` von Hand gesetzt -> der Fahrer
                //      stand 3 s auf PathIdx 0/16. Ein selbstgesetzter Weg
                //      macht eine Einheit nicht fahrend.
                //   2. EINE Einheit per IssueMove befehligt -> Weg 12, dann
                //      Weg 0 und keinen Schritt. Auf map_DM_4 steht der eigene
                //      Pulk so dicht, dass eine einzelne Einheit gar nicht
                //      herauskommt; im Spiel faehrt sie los, sobald die
                //      anderen Platz machen (StuckCheckStart, 16.08.2026).
                //
                // Also derselbe Aufbau wie --stuck-check: die ganze Gruppe
                // faehrt, und beobachtet wird eine, die WIRKLICH unterwegs ist.
                GD.Print("ausweich-probe: " + StuckCheckStart());
                _awUhr = 12f;
                _awProbe = 1;
                return;
            }

            case 1:
            {
                // Warten, bis eine eigene Einheit tatsaechlich faehrt — erst
                // dann ist ein Blockierer vor ihr eine echte Blockade.
                _awUhr -= dt;
                if (_awFahrer < 0)
                    for (int i = 0; i < _entities.Count && _awFahrer < 0; i++)
                    {
                        var e = _entities[i];
                        if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
                        if (e.Owner != ViewPlayer) continue;
                        if (e.PathIdx <= 0) continue;                  // noch nie gefahren
                        if (e.Path is not { Count: > 0 }) continue;
                        if (e.PathIdx >= e.Path.Count) continue;       // gleich fertig
                        _awFahrer = i;
                    }
                if (_awFahrer < 0)
                {
                    if (_awUhr > 0f) return;
                    GD.Print("ausweich-probe: in 12 s ist keine eigene Einheit "
                           + "wirklich losgefahren — hier ist nichts zu messen");
                    _awProbe = -1; return;
                }

                // FALL 1 — einen FEIND auf die naechste Wegzelle stellen.
                var f = _entities[_awFahrer];
                var z = AwNaechste(f);
                if (z == null) { _awFahrer = -1; return; }
                for (int i = 0; i < _entities.Count && _awBlock < 0; i++)
                {
                    var e = _entities[i];
                    if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
                    if (i == _awFahrer || !IsHostile(f, e)) continue;
                    _awBlock = i;
                }
                if (_awBlock < 0)
                {
                    GD.Print("ausweich-probe: kein Feind auf dieser Karte — "
                           + "die Frage ist hier nicht stellbar");
                    _awProbe = -1; return;
                }
                AwStelle(_awBlock, z.Value);
                var b = _entities[_awBlock];
                _awFeindVor = AusweichFeind; _awGefragtVor = AusweichGefragt;
                GD.Print($"ausweich-probe FALL 1: Fahrer Platz {f.Slot} auf "
                       + $"({f.Col},{f.Row}) faehrt; Feind Platz {b.Slot} (P{b.Owner}) "
                       + $"auf ({z.Value.X},{z.Value.Y}) gestellt — genau in den Weg. "
                       + "ERWARTET: feind steigt.");
                _awUhr = AwProbeSekunden; _awProbe = 2;
                return;
            }

            case 2:
            {
                _awUhr -= dt;
                // ⚠ Diagnose: ohne sie ist »+0« nicht von »der Fahrer stand«
                // zu unterscheiden — genau der Fehler, den Regel 33 meint.
                _awTicker -= dt;
                if (_awTicker <= 0f)
                {
                    _awTicker = 1f;
                    var d = _entities[_awFahrer];
                    var nz = AwNaechste(d);
                    GD.Print($"   ausweich-probe [t] Fahrer ({d.Col},{d.Row}) "
                           + $"PathIdx {d.PathIdx}/{d.Path?.Count ?? 0} "
                           + $"naechste {(nz is { } q ? $"({q.X},{q.Y})" : "-")} "
                           + $"Block {d.Block} | gefragt {AusweichGefragt} "
                           + $"feind {AusweichFeind} gewartet {GiveWayGewartet}");
                }
                if (_awUhr > 0f) return;
                int dFeind = AusweichFeind - _awFeindVor;
                int dFrag = AusweichGefragt - _awGefragtVor;
                _awFeindfall = $"feind +{dFeind}, gefragt +{dFrag}";
                GD.Print($"ausweich-probe FALL 1 nach {AwProbeSekunden:0} s: {_awFeindfall}"
                       + $"  -> {(dFeind > 0 ? "WIE ERWARTET (ein Feind wird NICHT gefragt)" : "NICHT wie erwartet")}");

                // FALL 2 — derselbe Platz, aber ein EIGENER Blockierer.
                var f = _entities[_awFahrer];
                // den Feind fortraeumen, damit er nicht mitzaehlt
                var alt = _entities[_awBlock];
                _nav.ClearOccupant(alt.Col, alt.Row, _awBlock);
                alt.Dead = true;

                if (f.Path == null || f.Path.Count == 0)
                {
                    var neu = _nav.FindPath(new Vector2I(f.Col, f.Row), f.Goal, f.Move, _awFahrer);
                    if (neu != null && neu.Count > 0) { f.Path = neu; f.PathIdx = 0; }
                }
                var z = AwNaechste(f);
                if (z == null)
                {
                    GD.Print("ausweich-probe: Fahrer hat keinen Weg mehr — Fall 2 entfaellt");
                    _awProbe = -1; return;
                }

                int eigen = -1;
                for (int i = 0; i < _entities.Count && eigen < 0; i++)
                {
                    var e = _entities[i];
                    if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
                    if (i == _awFahrer || e.Owner != f.Owner) continue;
                    eigen = i;
                }
                if (eigen < 0)
                {
                    GD.Print("ausweich-probe: keine zweite eigene Einheit — Fall 2 entfaellt");
                    _awProbe = -1; return;
                }
                _awBlock = eigen;
                AwStelle(eigen, z.Value);
                _awFeindVor = AusweichFeind; _awGefragtVor = AusweichGefragt;
                GD.Print($"ausweich-probe FALL 2: eigener Platz {_entities[eigen].Slot} "
                       + $"(P{_entities[eigen].Owner}) auf ({z.Value.X},{z.Value.Y}) — "
                       + "ERWARTET: gefragt steigt, feind bleibt NULL.");
                _awUhr = AwProbeSekunden; _awProbe = 3;
                return;
            }

            case 3:
            {
                _awUhr -= dt;
                if (_awUhr > 0f) return;
                int dFeind = AusweichFeind - _awFeindVor;
                int dFrag = AusweichGefragt - _awGefragtVor;
                _awFreundfall = $"feind +{dFeind}, gefragt +{dFrag}";
                GD.Print($"ausweich-probe FALL 2 nach {AwProbeSekunden:0} s: {_awFreundfall}"
                       + $"  -> {(dFrag > 0 && dFeind == 0 ? "WIE ERWARTET (ein Eigener WIRD gefragt)" : "NICHT wie erwartet")}");
                GD.Print($"ausweich-probe ERGEBNIS: Feind [{_awFeindfall}] | "
                       + $"Eigener [{_awFreundfall}]");
                _awProbe = -1;
                return;
            }
        }
    }
}
