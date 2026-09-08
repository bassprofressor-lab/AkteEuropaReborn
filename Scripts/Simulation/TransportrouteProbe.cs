using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>--transportroute-probe — pendelt der Wagen wirklich?</b>
///
/// <para>Zu <see cref="TransportrouteTakt"/> (Simulation/Transportroute.cs).
/// Der Fall ist im kopflosen Lauf sonst nicht zu bekommen: eine Route legt
/// entweder die KI an (nur fuer Computerspieler) oder der Spieler ueber ein
/// Fenster, das es noch nicht gibt. Also wird er gestellt — dieselbe Bauweise
/// wie <c>--bodenangriff-probe</c>.</para>
///
/// <para><b>Der Aufbau:</b> ein eigener Transporter, eine eigene Quelle
/// (Fabrik oder Mine mit Lager) und ein eigenes Ziel (Basis oder Flughafen).
/// Die Faecher werden ueber dieselben Felder gesetzt, die auch die KI setzt,
/// und mit <see cref="RouteStarten"/> gestartet.</para>
///
/// <para><b>Die Messlatte steht vorher fest</b> — und sie braucht ALLE drei
/// Zahlen, sonst ist »die Route laeuft« nicht von »der Wagen faehrt im Kreis«
/// zu unterscheiden:</para>
/// <list type="bullet">
///   <item><c>Fahrten</c> steigt — der Wagen bekommt ueberhaupt Auftraege.</item>
///   <item><c>geladen</c> steigt — an der Quelle wird wirklich aufgenommen,
///   und das Lager der Quelle SINKT um denselben Betrag.</item>
///   <item><c>abgeladen</c> steigt — am Ziel kommt es an, und dessen Lager
///   STEIGT. ⚠ Ohne diese letzte Zahl waere ein Wagen, der ewig laedt und nie
///   ankommt, ein »bestanden«.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _trpStufe = -1, _trpWagen = -1, _trpQuelle = -1, _trpZiel = -1;
    private float _trpUhr, _trpTicker;
    private int[] _trpQuelleVor = new int[4], _trpZielVor = new int[4];

    /// <summary>Wie lange gemessen wird. Eine volle Runde ist Fahrt + 100 Takte
    /// im Gebaeude + Rueckfahrt + 60 Takte an der Basis — das dauert.</summary>
    private const float TrpSekunden = 120f;

    /// <summary>Wie weit Wagen, Ziel und Quelle hoechstens auseinanderliegen
    /// duerfen, damit die Probe die ROUTE misst und nicht den Puffer der
    /// Wegsuche.</summary>
    private const int TrpNaehe = 40;

    public void TransportrouteProbeStart() { _trpStufe = 0; _trpUhr = 5f; }

    private static int[] LagerStand(Entity b)
        => new[] { b.StockW, b.StockF, b.StockS, b.StockT };

    private void PollTransportrouteProbe(float dt)
    {
        if (_trpStufe < 0 || _nav == null) return;
        switch (_trpStufe)
        {
            case 0:
                _trpUhr -= dt;
                if (_trpUhr > 0f) return;
                _trpStufe = 1;
                return;

            case 1:
            {
                // ⚠⚠ 08.09.2026, ZWEIMAL BERICHTIGT. Erst nahm die Probe das
                // erstbeste Trio, dann das naechste Paar aus Wagen und Basis —
                // beide Male lagen auf map_DM_8 zwischen Wagen (8,71) und der
                // einzigen eigenen Basis (129,14) 130 Zellen. Der Wagen lud,
                // fuhr los und kam in 90 Sekunden nicht an; gemeldet haette die
                // Probe »kommt nie am Ziel an«, gemessen haette sie die
                // KARTENGROESSE.
                //
                // Jetzt das ENGSTE Dreieck aus Wagen, Ziel und Quelle
                // DESSELBEN Besitzers — ueber alle Spieler, denn die Route ist
                // dieselbe Mechanik, egal wem der Wagen gehoert.
                _trpWagen = -1; _trpQuelle = -1; _trpZiel = -1;
                int bestesMass = int.MaxValue;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.Dead || e.IsBuilding || e.IsProp) continue;
                    if (!IstTransporter(e)) continue;
                    // ⚠ Nur ein Wagen, dessen Route STEHT. Der Lauf davor nahm
                    // einen, den die KI gerade fahren liess, setzte ihm mitten
                    // im Umlauf eine neue Route — und mass danach »nichts
                    // geladen«. Dieselbe Bedingung stellt auch die KI selbst
                    // (0x4BAEB0: UKOL 0 und satz+0x0E == 0).
                    if (e.Ukol != UkolFrei) continue;
                    if (RouteVon(e) is { Gestartet: true }) continue;
                    foreach (var zg in _entities)
                    {
                        if (!zg.IsBuilding || zg.Dead || zg.Owner != e.Owner) continue;
                        if (zg.BType is not (1 or 9)) continue;
                        foreach (var qg in _entities)
                        {
                            if (!qg.IsBuilding || qg.Dead || qg.Owner != e.Owner) continue;
                            if (qg.BType is not (2 or 3 or 4 or 10 or 15)) continue;
                            // ⚠⚠ 08.09.2026 — UND DIE QUELLE MUSS ETWAS HABEN,
                            // WAS DAS ZIEL NIMMT. Der Lauf davor waehlte eine
                            // Fahrwerkfabrik mit Lager 0/0/0/999 und eine BASIS
                            // als Ziel: die Basis nimmt Waffen, Fahrwerk und
                            // Spezial, aber KEIN Terranium (Tafel 0x4FACD0).
                            // Der Wagen fuhr also richtig hin, lud nichts — und
                            // die Probe meldete »nichts geladen«, obwohl genau
                            // das die Regel ist.
                            if (RouteVorratFuerProbe(qg, zg) <= 0) continue;
                            int hin = Mathf.Max(Mathf.Abs(zg.Col - e.Col), Mathf.Abs(zg.Row - e.Row));
                            int zurueck = Mathf.Max(Mathf.Abs(qg.Col - zg.Col), Mathf.Abs(qg.Row - zg.Row));
                            // ⚠⚠ UND EINE OBERGRENZE. Unsere Wegsuche schneidet
                            // bei rund 50 Zellen ab (»Weg am 50er-Puffer
                            // abgeschnitten«, --stuck-check). Ein Ziel, das
                            // weiter weg liegt, erreicht der Wagen in keiner
                            // Messzeit — die Probe wuerde den PUFFER messen und
                            // ihn fuer einen Fehler der Route halten. Genau das
                            // ist ihr zweimal passiert.
                            if (hin > TrpNaehe || zurueck > TrpNaehe) continue;
                            int mass = hin + zurueck;
                            if (mass >= bestesMass) continue;
                            bestesMass = mass; _trpWagen = i; _trpZiel = zg.Slot; _trpQuelle = qg.Slot;
                        }
                    }
                }
                if (_trpWagen < 0 || _trpQuelle < 0 || _trpZiel < 0)
                {
                    GD.Print($"transportroute-probe: nicht stellbar — Wagen "
                           + $"{(_trpWagen >= 0 ? "ja" : "NEIN")}, Quelle "
                           + $"{(_trpQuelle >= 0 ? "ja" : "NEIN")}, Ziel "
                           + $"{(_trpZiel >= 0 ? "ja" : "NEIN")}");
                    _trpStufe = -1; return;
                }

                var u = _entities[_trpWagen];
                var q = GebaeudePlatzFuerProbe(_trpQuelle)!;
                var z = GebaeudePlatzFuerProbe(_trpZiel)!;
                var r = RouteVon(u) ?? RouteAnlegen(u);
                if (r == null)
                {
                    GD.Print("transportroute-probe: der Wagen bekam keinen Umschlagsatz "
                           + "(--keine-transportrouten?)");
                    _trpStufe = -1; return;
                }
                r.Ziel = _trpZiel;
                r.Quelle[0] = _trpQuelle;
                r.Quelle[1] = r.Quelle[2] = r.Quelle[3] = -1;
                RouteStarten(u, r);

                _trpQuelleVor = LagerStand(q); _trpZielVor = LagerStand(z);
                GD.Print($"transportroute-probe AUFBAU: Wagen Platz {u.Slot} \"{LabelOf(u)}\" "
                       + $"auf ({u.Col},{u.Row}); QUELLE Platz {q.Slot} Art {q.BType} "
                       + $"({q.Col},{q.Row}) Lager {q.StockW}/{q.StockF}/{q.StockS}/{q.StockT}; "
                       + $"ZIEL Platz {z.Slot} Art {z.BType} ({z.Col},{z.Row}) Lager "
                       + $"{z.StockW}/{z.StockF}/{z.StockS}/{z.StockT}");
                GD.Print("   ERWARTET: Fahrten steigen, das Lager der Quelle SINKT und das "
                       + "des Ziels STEIGT um denselben Betrag.");
                _trpUhr = TrpSekunden; _trpTicker = 0f; _trpStufe = 2;
                return;
            }

            case 2:
            {
                _trpUhr -= dt; _trpTicker -= dt;
                var u = _entities[_trpWagen];
                if (_trpTicker <= 0f)
                {
                    _trpTicker = 10f;
                    var r = RouteVon(u);
                    GD.Print($"   [t] ({u.Col},{u.Row}) UKOL {u.Ukol} Fahrziel "
                           + $"{(r?.Fahrziel ?? -1)} Ladung {(r?.Gesamt ?? -1)}/"
                           + $"{(r?.Fassung ?? -1)} Warten {(r?.Warten ?? 0)} "
                           + $"Weg {(u.Path == null ? "keiner" : $"{u.PathIdx}/{u.Path.Count}")} "
                           + $"| Fahrten {RouteFahrten}");
                }
                if (_trpUhr > 0f) return;

                var q = GebaeudePlatzFuerProbe(_trpQuelle)!;
                var z = GebaeudePlatzFuerProbe(_trpZiel)!;
                var qn = LagerStand(q); var zn = LagerStand(z);
                int qd = 0, zd = 0;
                for (int k = 0; k < 4; k++) { qd += _trpQuelleVor[k] - qn[k]; zd += zn[k] - _trpZielVor[k]; }

                // ⚠⚠ 08.09.2026 — JE SATZ zaehlen, nicht weltweit. Der erste
                // Lauf verglich die Summe ALLER Wagen (die KI hatte sieben
                // eigene Routen angelegt) mit den zwei Lagern dieser Probe und
                // meldete »die Lager stimmen nicht« — ein Befund ueber meine
                // Rechnung, nicht ueber die Route.
                var rr = RouteVon(u);
                int geladen = rr?.GeladenGesamt ?? 0, abgeladen = rr?.AbgeladenGesamt ?? 0;

                GD.Print($"transportroute-probe ERGEBNIS nach {TrpSekunden:0} s: "
                       + $"Fahrten {RouteFahrten}, geladen {geladen}, abgeladen {abgeladen}; "
                       + $"Quelle {qd} weniger, Ziel {zd} mehr (beide Zahlen ENTHALTEN die "
                       + $"Wagen der KI) -> "
                       + (RouteFahrten == 0 ? "KEINE FAHRT (der Wagen bekam keinen Auftrag)"
                        : geladen == 0 ? "NICHTS GELADEN (er kam nie an der Quelle an "
                                       + "oder ihr Lager war leer)"
                        : abgeladen == 0 ? "GELADEN, ABER NICHTS ABGELADEN (er kam nie am Ziel an)"
                        : "WIE ERWARTET (dieser Wagen hat an der Quelle aufgenommen "
                          + "und am Ziel abgeliefert)"));
                GD.Print("   " + RouteWatchLine());
                _trpStufe = -1;
                return;
            }
        }
    }

    /// <summary>Dieselbe Suche wie im Takt, oeffentlich fuer die Probe.</summary>
    private Entity? GebaeudePlatzFuerProbe(int slot) => GebaeudePlatz(slot);

    /// <summary>Was diese Quelle fuer DIESES Ziel hergibt — dieselbe Rechnung
    /// wie <c>0x410510</c>, nur ohne Satz.</summary>
    private static int RouteVorratFuerProbe(Entity quelle, Entity ziel)
    {
        var f = WarenTafelFuerProbe(ziel.BType);
        int s2 = 0;
        for (int k = 0; k < 4; k++) if (f[k]) s2 += LagerStand(quelle)[k];
        return s2;
    }
}
