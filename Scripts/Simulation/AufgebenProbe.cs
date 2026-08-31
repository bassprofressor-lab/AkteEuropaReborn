using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>--aufgeben-probe — GIBT ER AUF, WENN DAS ZIEL BELEGT IST, UND NUR DANN?</b>
///
/// <para>Gebaut am 31.08.2026, unmittelbar nach dem Einbau. Der erste
/// <c>--stuck-check</c>-Lauf hat gezeigt, warum: <c>22x geprueft, 1x
/// aufgegeben</c>. Der Fall ist auf map_DM_4 so selten, dass die Tafel aus
/// CC.1 ihn nicht auflöst — <b>der Prüfstand enthält den Gegenstand kaum</b>,
/// dieselbe Lage wie bei der Bündnisprüfung (CF) und bei
/// <c>--ki-probe</c>.</para>
///
/// <para><b>Was gemessen wird, und warum GENAU das.</b> Die Bedingung, die
/// CD.3 verkehrt herum hatte (bug-004, siehe CG.3), ist die erste:</para>
/// <code>
///   0x408D19  cmp word [imap + Zielzelle*2], 0x1F40
///   0x408D21  jae -> NICHT aufgeben        ; Ziel FREI   -> weiterfahren
///             sonst                        ; Ziel BELEGT -> aufgeben
/// </code>
///
/// <para><b>Zwei Läufe, ein Aufbau.</b> <c>--aufgeben-probe=belegt</c> und
/// <c>--aufgeben-probe=frei</c> stellen <b>denselben</b> Fahrer an derselben
/// Stelle vor denselben Feind und setzen ihm dasselbe Zielfeld — der einzige
/// Unterschied ist, ob auf diesem Feld ein Fahrzeug steht.</para>
/// <list type="bullet">
/// <item><b>belegt:</b> <c>Aufgegeben</c> steigt, sein Weg ist danach weg.</item>
/// <item><b>frei:</b> <c>AufgebenGeprueft</c> steigt (die Prüfung läuft also
/// wirklich), <c>Aufgegeben</c> bleibt NULL, <c>AufgebenZielFrei</c> steigt —
/// und der Fahrer fährt zu seinem Ziel weiter.</item>
/// </list>
/// <para>⭐ <b>Unter CD.3s Lesung wäre es genau andersherum.</b> Die Probe
/// unterscheidet die beiden Lesungen also direkt, und nicht über eine
/// Summenzahl.</para>
///
/// <para>⚠ <b>Warum ZWEI Läufe und nicht zwei Fälle in einem.</b> Der erste
/// Anlauf hat beide Fälle nacheinander gemessen, und beide Male ging es
/// schief — jeder Fehlschlag hätte als Befund durchgehen können:</para>
/// <code>
///   1. Fall 2 bekam einen ZWEITEN Fahrer -> der war gar nicht blockiert
///      (`geprueft +0`). Damit war nichts gemessen, nicht einmal etwas
///      Falsches.
///   2. Nach Fall »frei« ist der Fahrer an sein Ziel GEFAHREN und hat keinen
///      Weg mehr — der zweite Fall kann ihn nicht mehr benutzen.
///   3. Das auf dem Zielfeld geparkte Fahrzeug FAEHRT WIEDER WEG: es gehört
///      zur Prüfgruppe und plant von selbst neu. Die Prüfung meldete dann
///      `zielfrei`, obwohl der Aufbau »belegt« hiess.
/// </code>
/// <para>Punkt 3 ist auch im Lauf »belegt« zu halten: der Blockierer wird
/// darum in JEDEM Takt auf sein Feld zurückgesetzt (<see cref="AgHalten"/>).
/// </para>
///
/// <para>⚠ <b>Der Feind im Weg ist kein Beiwerk, sondern Voraussetzung.</b>
/// Das Aufgeben hängt allein im Absage-Zweig (<c>Can_go = 0</c>) hinter dem
/// Geduldszähler. Ein EIGENER Blockierer sagt in der Regel zu, und eine Zusage
/// führt in den 1/60-Zweig, der das Aufgeben gar nicht kennt (CG.5). Ein Feind
/// wird nach <c>0x4054D0</c> nicht einmal gefragt — also Absage, also Geduld,
/// also die Prüfung. Das ist zugleich die Gegenprobe darauf, dass CF und CG
/// zusammenpassen.</para>
///
/// <para>⚠ <b>Die Reihenfolge ist derselbe Trick wie bei
/// <c>--ausweich-probe</c>:</b> erst muss ein Weg STEHEN und die Einheit
/// wirklich fahren (<c>PathIdx &gt; 0</c>), dann kommt der Blockierer auf
/// dessen nächste Zelle. Wer zuerst stellt und dann das Ziel setzt, plant um
/// den Blockierer herum und misst nichts.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _agProbe = -1, _agFahrer = -1, _agFeind = -1, _agZielBlock = -1;
    private float _agUhr, _agTicker;
    private int _agAufVor, _agPruefVor, _agFreiVor, _agZielVor, _agWeitVor;
    private Vector2I _agZielfeld;

    /// <summary>true = ein Fahrzeug steht auf dem Zielfeld
    /// (<c>--aufgeben-probe=belegt</c>, die Vorgabe).</summary>
    public static bool AgZielBelegt = true;

    /// <summary>Wie lange gemessen wird. Die Geduld steht beim ersten
    /// versperrten Takt auf 15…29 Takten (<c>BlockEnter</c>), läuft also in
    /// einer halben Sekunde ab; fünf Sekunden lassen mehrere Durchgänge zu.
    /// </summary>
    private const float AgProbeSekunden = 5f;

    /// <summary><c>--aufgeben-probe</c> starten.</summary>
    public void AufgebenProbeStart() { _agProbe = 0; }

    /// <summary>Eine freie Zelle in Chebyshev-Abstand 2 um den Fahrer — die
    /// Luftlinie liegt damit zwischen 2,0 und 2,83 und also unter der Schwelle
    /// 3 aus <c>cmp ax, 3</c>.</summary>
    private Vector2I? AgZielNah(Entity f)
    {
        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != 2) continue;
                int c = f.Col + dx, r = f.Row + dy;
                if (!_nav.InBounds(c, r)) continue;
                if (_nav.OccupantAt(c, r) >= 0) continue;
                return new Vector2I(c, r);
            }
        return null;
    }

    /// <summary>Einen fahrenden eigenen Fahrer suchen — <c>PathIdx &gt; 0</c>
    /// heisst: er ist wirklich schon losgefahren.</summary>
    private int AgFahrerSuchen()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
            if (e.Owner != ViewPlayer) continue;
            if (e.PathIdx <= 0) continue;
            if (e.Path is not { Count: > 0 }) continue;
            if (e.PathIdx >= e.Path.Count) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Einen Feind suchen, der als Blockierer taugt.</summary>
    private int AgFeindSuchen(Entity f)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
            if (!IsHostile(f, e)) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Den Aufbau stellen. false, wenn er sich hier nicht herstellen
    /// liess.</summary>
    private bool AgAufbau(out string was)
    {
        was = "";
        var f = _entities[_agFahrer];
        var vor = AwNaechste(f);
        if (vor == null) { was = "der Fahrer hat keine naechste Wegzelle"; return false; }

        var ziel = AgZielNah(f);
        if (ziel == null) { was = "keine freie Zelle in Abstand 2"; return false; }
        _agZielfeld = ziel.Value;

        // ⚠ ZUERST der Weg, DANN der Blockierer — sonst plant die Suche herum.
        _agFeind = AgFeindSuchen(f);
        if (_agFeind < 0) { was = "kein Feind auf dieser Karte"; return false; }
        AwStelle(_agFeind, vor.Value);

        _agZielBlock = -1;
        if (AgZielBelegt)
        {
            // ein FAHRZEUG auf das Zielfeld. Infanterie taugt nicht: sie steht
            // in der imap bei 10000…13999 und liegt damit ueber 8000.
            for (int i = 0; i < _entities.Count && _agZielBlock < 0; i++)
            {
                var e = _entities[i];
                if (i == _agFahrer || i == _agFeind) continue;
                if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile) continue;
                if (e.Infantry >= 0) continue;
                _agZielBlock = i;
            }
            if (_agZielBlock < 0) { was = "kein zweites Fahrzeug frei"; return false; }
            AwStelle(_agZielBlock, _agZielfeld);
        }

        // Das Zielfeld setzen. Der Weg bleibt, wie er ist — die Pruefung liest
        // NUR +0x18/+0x19 gegen +0x00/+0x01, nicht den Wegpuffer.
        f.Goal = _agZielfeld;
        f.Target = -1;                       // +0x36 := 0xFFFF, reiner Fahrbefehl
        was = $"Fahrer Platz {f.Slot} auf ({f.Col},{f.Row}), Ziel "
            + $"({_agZielfeld.X},{_agZielfeld.Y}) {(AgZielBelegt ? "BELEGT" : "FREI")}, "
            + $"Feind Platz {_entities[_agFeind].Slot} auf ({vor.Value.X},{vor.Value.Y})";
        return true;
    }

    /// <summary>⚠ Den Blockierer auf dem Zielfeld HALTEN. Ohne das faehrt er
    /// binnen einer Sekunde weg — er gehoert zur Pruefgruppe und plant von
    /// selbst neu —, und die Pruefung meldet dann `zielfrei`, obwohl der
    /// Aufbau »belegt« heisst. Genau daran ist der erste Anlauf gescheitert.
    /// </summary>
    private void AgHalten()
    {
        if (_agZielBlock < 0) return;
        var b = _entities[_agZielBlock];
        if (b.Dead) return;
        b.Path = null; b.Orders.Clear(); b.Target = -1; b.RetryIn = 0;
        if (b.Col != _agZielfeld.X || b.Row != _agZielfeld.Y)
            AwStelle(_agZielBlock, _agZielfeld);
    }

    /// <summary>Die Zaehlerstaende vor der Messung festhalten.</summary>
    private void AgMerken()
    {
        _agAufVor = Aufgegeben; _agPruefVor = AufgebenGeprueft;
        _agFreiVor = AufgebenZielFrei; _agZielVor = AufgebenHatZieleinheit;
        _agWeitVor = AufgebenZuWeit;
    }

    /// <summary>⚠ WORAN es lag, wenn nicht aufgegeben wurde. Ohne diese drei
    /// Zahlen sagt »aufgegeben +0« nur, DASS es nicht kam, nicht warum — und
    /// damit laesst sich ein Fehler im Aufbau nicht von einem Befund
    /// unterscheiden.</summary>
    private string AgWarum()
        => $"abgelehnt: zielfrei +{AufgebenZielFrei - _agFreiVor}, "
         + $"zieleinheit +{AufgebenHatZieleinheit - _agZielVor}, "
         + $"zuweit +{AufgebenZuWeit - _agWeitVor}";

    /// <summary>Die Diagnosezeile. ⚠ Ohne sie ist »+0« nicht von »der Fahrer
    /// war gar nicht blockiert« zu unterscheiden.</summary>
    private void AgTicker()
    {
        var d = _entities[_agFahrer];
        int auf = _agZielBlock >= 0 ? _nav.OccupantAt(_agZielfeld.X, _agZielfeld.Y) : -1;
        GD.Print($"   aufgeben-probe [t] Fahrer ({d.Col},{d.Row}) Ziel "
               + $"({d.Goal.X},{d.Goal.Y}) PathIdx {d.PathIdx}/{d.Path?.Count ?? 0} "
               + $"Geduld {d.Block} Zielfeld {(auf >= 0 ? "belegt" : "frei")} | "
               + $"geprueft {AufgebenGeprueft} aufgegeben {Aufgegeben} "
               + $"zielfrei {AufgebenZielFrei}");
    }

    private void PollAufgebenProbe(float dt)
    {
        if (_agProbe < 0 || _nav == null) return;
        switch (_agProbe)
        {
            case 0:
                // Dieselbe Falle wie bei --ausweich-probe: eine EINZELNE
                // Einheit kommt aus dem eigenen Pulk nicht heraus. Also alle.
                GD.Print("aufgeben-probe: " + StuckCheckStart());
                _agUhr = 12f; _agProbe = 1;
                return;

            case 1:
            {
                _agUhr -= dt;
                _agFahrer = AgFahrerSuchen();
                if (_agFahrer < 0)
                {
                    if (_agUhr > 0f) return;
                    GD.Print("aufgeben-probe: in 12 s ist keine eigene Einheit "
                           + "wirklich losgefahren — hier ist nichts zu messen");
                    _agProbe = -1; return;
                }
                if (!AgAufbau(out string was))
                { GD.Print($"aufgeben-probe entfaellt — {was}"); _agProbe = -1; return; }

                AgMerken();
                GD.Print($"aufgeben-probe AUFBAU: {was}");
                GD.Print(AgZielBelegt
                    ? "   ERWARTET: aufgegeben steigt, sein Weg ist danach weg."
                    : "   ERWARTET: geprueft steigt, aufgegeben bleibt NULL, zielfrei steigt.");
                _agUhr = AgProbeSekunden; _agTicker = 0f; _agProbe = 2;
                return;
            }

            case 2:
            {
                AgHalten();
                _agUhr -= dt; _agTicker -= dt;
                if (_agTicker <= 0f) { _agTicker = 1f; AgTicker(); }
                if (_agUhr > 0f) return;

                int dAuf = Aufgegeben - _agAufVor, dPr = AufgebenGeprueft - _agPruefVor;
                var f = _entities[_agFahrer];
                bool wegWeg = f.Path == null;
                string urteil = dPr == 0
                    ? "NICHTS GEMESSEN (der Fahrer war nicht blockiert)"
                    : AgZielBelegt
                        ? (dAuf > 0 ? "WIE ERWARTET (belegtes Ziel, nah genug -> aufgeben)"
                                    : "NICHT wie erwartet")
                        : (dAuf == 0 ? "WIE ERWARTET (freies Ziel -> weiterfahren)"
                                     : "NICHT wie erwartet");
                GD.Print($"aufgeben-probe ERGEBNIS ({(AgZielBelegt ? "Ziel BELEGT" : "Ziel FREI")}) "
                       + $"nach {AgProbeSekunden:0} s: geprueft +{dPr}, aufgegeben +{dAuf}, "
                       + $"{AgWarum()}, Weg danach {(wegWeg ? "WEG" : "noch da")}  -> {urteil}");
                _agProbe = -1;
                return;
            }
        }
    }
}
