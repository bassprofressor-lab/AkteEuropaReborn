using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b><c>--gruppenangriff-probe</c> — greift eine GRUPPE ein Boot an, oder nur
/// die vorderen?</b>
///
/// <para>Seine Meldung vom 08.09.2026: »gerade wenn ich einheiten angreife
/// wollen manche einfach nicht, gerade wenn ich eine gruppe kommandiere. dann
/// muss ich die einzeln auswaehlen, das er sich mal bemueht das boot
/// anzugreifen, sehr muehsam.«</para>
///
/// <para><b>Der Aufbau:</b> alle eigenen Schiffe anwaehlen und EINEN
/// Angriffsklick auf ein feindliches Boot absetzen — ueber
/// <see cref="PostAttack"/>, also den Weg des Rechtsklicks. Danach laeuft der
/// normale Takt.</para>
///
/// <para><b>Die Messlatte, und sie hat drei Stufen</b> — »der Befehl kam an«
/// ist nicht »sie fahren hin« und erst recht nicht »sie schiessen«:</para>
/// <list type="bullet">
///   <item>wie viele den Auftrag ANGENOMMEN haben (<c>Target</c> gesetzt),</item>
///   <item>wie viele ihn nach 30 Sekunden noch HABEN,</item>
///   <item>wie viele wirklich GESCHOSSEN haben.</item>
/// </list>
///
/// <para>⚠ Die mittlere Zahl ist der Kern: faellt sie ab, laesst die Verfolgung
/// die hinteren fallen — genau sein Bild. Gegenschalter
/// <c>--verfolgung-aufgeben-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _gapStufe = -1, _gapOpfer = -1, _gapBefohlen, _gapSchussVor;
    private float _gapUhr, _gapTicker;
    private readonly System.Collections.Generic.List<int> _gapFlotte = new();

    private const float GapSekunden = 30f;

    public void GruppenangriffProbeStart() { _gapStufe = 0; _gapUhr = 5f; Schussgruende = true; }

    private void PollGruppenangriffProbe(float dt)
    {
        if (_gapStufe < 0 || _nav == null) return;
        switch (_gapStufe)
        {
            case 0:
                _gapUhr -= dt;
                if (_gapUhr > 0f) return;
                _gapStufe = 1;
                return;

            case 1:
            {
                _gapFlotte.Clear(); _gapOpfer = -1;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.Dead || e.IsBuilding || e.IsProp || !e.Mobile) continue;
                    if (e.Move != Simulation.NavGrid.MoveClass.Ship) continue;
                    if (e.Owner != ViewPlayer || !CanFightFuerProbe(e)) continue;
                    _gapFlotte.Add(i);
                }
                // Das Opfer: das naechste feindliche SCHIFF zum ersten der Flotte.
                if (_gapFlotte.Count > 0)
                {
                    var a = _entities[_gapFlotte[0]];
                    float best = float.MaxValue;
                    for (int j = 0; j < _entities.Count; j++)
                    {
                        var t = _entities[j];
                        if (t.Dead || t.IsProp || t.IsBuilding) continue;
                        if (t.Move != Simulation.NavGrid.MoveClass.Ship) continue;
                        if (!IsHostile(a, t)) continue;
                        float d = CellDistance(a, t);
                        if (d < best) { best = d; _gapOpfer = j; }
                    }
                }
                if (_gapFlotte.Count < 2 || _gapOpfer < 0)
                {
                    GD.Print($"gruppenangriff-probe: nicht stellbar — eigene Schiffe "
                           + $"{_gapFlotte.Count}, feindliches Boot "
                           + (_gapOpfer >= 0 ? "ja" : "NEIN"));
                    _gapStufe = -1; return;
                }

                var v = _entities[_gapOpfer];
                _sel.Clear();
                foreach (int i in _gapFlotte) _sel.Add(i);
                _gapSchussVor = DebugShots;
                // ⚠ Ueber den ABSENDER, nicht am Bus vorbei: der Pruefstand soll
                // denselben Weg gehen wie der Rechtsklick. Pick() faende im
                // kopflosen Lauf nichts (Nebel), darum die Mitte des Koerpers.
                PickOhneNebel = true;
                bool ab = PostAttack(BodyRect(v).GetCenter());
                PickOhneNebel = false;

                // ⚠ Der Satz wirkt erst im NAECHSTEN Takt (der Bus haelt ihn
                // einen Takt lang). Direkt nach dem Klick zu zaehlen misst also
                // nichts — die Zahl kommt aus Stufe 2.
                _gapBefohlen = -1;

                GD.Print($"gruppenangriff-probe AUFBAU: {_gapFlotte.Count} eigene Schiffe auf "
                       + $"Platz {v.Slot} \"{LabelOf(v)}\" ({v.Col},{v.Row}); Klick "
                       + $"{(ab ? "angenommen" : "ABGEWIESEN")}; Verfolgung: "
                       + (VerfolgungAufgebenAlt ? "gibt auf (alter Stand)" : "bleibt dran"));
                GD.Print($"   ERWARTET nach {GapSekunden:0} s: FAST ALLE haben ihr Ziel noch, "
                       + "und mehrere haben geschossen.");
                if (!ab) { _gapStufe = -1; return; }
                _gapUhr = GapSekunden; _gapTicker = 0f; _gapStufe = 2;
                return;
            }

            case 2:
            {
                _gapUhr -= dt; _gapTicker -= dt;
                if (_gapTicker <= 0f)
                {
                    _gapTicker = 10f;
                    int hat = 0, faehrt = 0, wartet = 0, befohlen = 0;
                    foreach (int i in _gapFlotte)
                    {
                        var e = _entities[i];
                        if (e.Target >= 0) hat++;
                        // ⭐ DIE Zahl, um die es geht: ein BEFOHLENER Angriff auf
                        // GENAU dieses Opfer. Ein selbst aufgenommenes Ziel
                        // sieht in »hat ein Ziel« gleich aus und ist doch etwas
                        // ganz anderes — es wird nicht verfolgt.
                        if (e.Target == _gapOpfer && e.Ordered) befohlen++;
                        if (e.Path != null) faehrt++;
                        if (e.ChaseRetry > 0) wartet++;
                    }
                    if (_gapBefohlen < 0) _gapBefohlen = befohlen;
                    GD.Print($"   [t] {befohlen} mit BEFEHL auf das Opfer, {hat} mit "
                           + $"irgendeinem Ziel, {faehrt} unterwegs, {wartet} warten auf einen "
                           + $"neuen Weg; Schuesse {DebugShots - _gapSchussVor}");
                }
                if (_gapUhr > 0f) return;

                int nochZiel = 0, geschossen = 0, nochBefohlen = 0;
                foreach (int i in _gapFlotte)
                {
                    var e = _entities[i];
                    if (e.Target >= 0) nochZiel++;
                    if (e.Target == _gapOpfer && e.Ordered) nochBefohlen++;
                    if (e.FireUntil > 0f) geschossen++;
                }
                GD.Print($"gruppenangriff-probe ERGEBNIS nach {GapSekunden:0} s "
                       + $"({(VerfolgungAufgebenAlt ? "ALT: Verfolgung gibt auf" : "NEU: Auftrag bleibt")}): "
                       + $"Befehl angekommen bei {_gapBefohlen}/{_gapFlotte.Count}, am Ende "
                       + $"noch {nochBefohlen} mit BEFEHL und {nochZiel} mit irgendeinem Ziel, "
                       + $"{geschossen} haben geschossen; "
                       + $"Schuesse gesamt {DebugShots - _gapSchussVor}, "
                       + $"{ChaseGeduldet}x Auftrag behalten, {ChaseNoGoal}x kein Zielfeld, "
                       + $"{ChaseNoPath}x kein Weg");
                // ⚠ Und die ERSTEN ACHT Faelle im Klartext: »256x kein Weg« sagt
                // DASS, nicht WOHIN. Ohne die Zellen ist nicht zu trennen, ob
                // das Ziel unerreichbar ist oder die Suche zu frueh aufgibt.
                GD.Print("   " + ChaseWatchLine());
                var v2 = _entities[_gapOpfer];
                var nf = _nav.NearestFree(new Vector2I(v2.Col, v2.Row),
                                          Simulation.NavGrid.MoveClass.Ship, _gapFlotte[0]);
                GD.Print($"   Opfer Platz {v2.Slot} auf ({v2.Col},{v2.Row}), Rumpf "
                       + $"{Simulation.NavGrid.HullSide(v2.GameUnitType)}; naechstes freies "
                       + $"Schiffsfeld daneben: {(nf is { } q2 ? $"({q2.X},{q2.Y})" : "KEINES")}");
                foreach (int i in _gapFlotte)
                {
                    var e = _entities[i];
                    var weg = nf == null ? null
                            : _nav.FindPath(new Vector2I(e.Col, e.Row), nf.Value, e.Move, i);
                    GD.Print($"   Platz {e.Slot} auf ({e.Col},{e.Row}) -> "
                           + (nf == null ? "kein Zielfeld"
                              : weg == null || weg.Count == 0 ? "KEIN WEG"
                              : $"Weg ueber {weg.Count} Zellen"));
                }
                // Und der Ausschnitt der PLANUNGSKARTE zwischen dem naechsten
                // Schiff und dem Zielfeld — »kein Weg« ueber drei Zellen kann
                // nur an einer dieser Zellen liegen.
                if (nf is { } zf)
                {
                    var q3 = _entities[_gapFlotte[0]];
                    int x0 = Mathf.Min(zf.X, q3.Col) - 1, x1 = Mathf.Max(zf.X, q3.Col) + 1;
                    int y0 = Mathf.Min(zf.Y, q3.Row) - 1, y1 = Mathf.Max(zf.Y, q3.Row) + 1;
                    for (int r3 = Mathf.Max(0, y0); r3 <= y1 && r3 < _nav.Height; r3++)
                    {
                        string z = $"   Zeile {r3,3}: ";
                        for (int c3 = Mathf.Max(0, x0); c3 <= x1 && c3 < _nav.Width; c3++)
                            z += _nav.SchiffPfadOffen(c3, r3, _gapFlotte[0]) ? "." : "#";
                        GD.Print(z);
                    }
                    GD.Print($"   (Spalten {Mathf.Max(0, x0)}..{Mathf.Min(x1, _nav.Width - 1)}, "
                           + $". = fuer einen 2x2-Rumpf planbar, # = nicht)");
                }
                _gapStufe = -1;
                return;
            }
        }
    }
}
