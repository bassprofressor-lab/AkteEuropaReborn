using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>--fussschuss-probe — schiesst ein FUSSSOLDAT ueberhaupt?</b>
///
/// <para>Gebaut am 08.09.2026 fuer bug-105 (»infanterie schiesst aber immer
/// noch nicht«). Davor waren drei Vermutungen nacheinander widerlegt worden —
/// Waffe, Munition, halber Fahrbefehl —, ohne dass sich im Spiel etwas
/// aenderte. Diese Probe stellt den Fall, statt ihn zu vermuten.</para>
///
/// <para><b>Der Aufbau, in zwei Teilen und in dieser Reihenfolge:</b></para>
/// <list type="number">
///   <item><b>BEFOHLEN</b>: ein eigener Fusssoldat, der naechste feindliche
///   Einheit, Angriffsbefehl ueber den BUS (<see cref="PostAttack"/>) — also
///   genau der Weg des Rechtsklicks, nicht daran vorbei. Er muss hinlaufen,
///   anhalten und feuern.</item>
///   <item><b>VON SELBST</b>: danach wird der Auftrag weggenommen
///   (<c>Target = -1</c>, <c>Ordered = false</c>), der Soldat steht neben dem
///   Feind. <see cref="AutoAcquire"/> muss ihn von selbst wieder aufnehmen.</item>
/// </list>
///
/// <para>⚠ Beide Teile laufen im NORMALEN Takt weiter — die Probe ruft keine
/// Taktstufe selbst auf. Genau daran ist am 08.09.2026 schon einmal ein
/// Pruefstand vorbeigelaufen (die Absetzprobe rief nur die halbe Kette und war
/// gruen, waehrend im Spiel nichts ankam).</para>
///
/// <para><b>Die Messlatte steht vorher fest:</b> <c>Schuesse</c> des SCHUETZEN
/// steigt in Teil 1, und in Teil 2 steigt sie noch einmal. Bleibt eine der
/// beiden auf 0, sagt die laufende Zeile, WORAN es liegt
/// (<c>Entity.Schussgrund</c>, dieselbe Zeile wie <c>--schuss-log</c>).</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _fspStufe = -1, _fspSchuetze = -1, _fspOpfer = -1;
    private float _fspUhr, _fspTicker;
    private int _fspVor1, _fspVor2;

    /// <summary>Wie oft der SCHUETZE der Probe gefeuert hat — in
    /// <see cref="Fire"/> gezaehlt, also da, wo der Schuss wirklich faellt.
    /// ⚠ Die weltweite Zahl <c>DebugShots</c> taugt hier nicht: auf einer
    /// Kampagnenkarte schiessen nebenher Dutzende Fahrzeuge.</summary>
    public int FussSchuesse;

    /// <summary>Wie lange je Teil gemessen wird. Ein Fusssoldat laeuft langsam,
    /// und er muss erst auf zwei Zellen herankommen.</summary>
    private const float FspTeil1 = 45f, FspTeil2 = 15f;

    public void FussSchussProbeStart()
    {
        _fspStufe = 0; _fspUhr = 5f;
        Schussgruende = true;        // die Zeile soll den GRUND nennen
    }

    /// <summary><c>--schiffe-als-opfer</c> — die Gegenprobe: doch ein Boot
    /// nehmen.</summary>
    public static bool SchiffeAlsOpfer;

    /// <summary><c>--gegner-nicht-stellen</c> — den Feind NICHT heranstellen.
    /// Dann misst die Probe den zweiten Teil seiner Meldung: kommt der
    /// Ausgestiegene ueberhaupt zum Gegner hin?</summary>
    public static bool GegnerNichtStellen;

    /// <summary>Welcher Soldat der Schuetze sein MUSS, -1 = der naechstbeste.
    /// Gesetzt vom Entladeteil.</summary>
    private int _fspVorgabe = -1;

    /// <summary><c>--entladeschuss-probe</c> - derselbe Fall, aber mit einem
    /// Soldaten, der AUS EINEM SCHIFF kommt.
    ///
    /// <para>⚠⚠ 08.09.2026, seine Antwort auf die Rueckfrage zu bug-105:
    /// »das ist nur bei der Infanterie, die aus den Schiffen entladen wurden.
    /// In Kampagne 4, wo man mit dieser spawnt, war das Problem nicht.« - und
    /// beides, Rechtsklick UND von selbst, blieb still, obwohl der Gegner direkt
    /// vor ihr stand. Damit ist die Frage nicht mehr »schiesst Infanterie«,
    /// sondern <b>was an einem Ausgestiegenen anders ist</b>.</para>
    ///
    /// <para>Die Probe setzt einen ab (ueber <see cref="FrachtAbsetzen"/>, also
    /// den Weg des Spiels) und legt seine Felder <b>neben die eines Soldaten von
    /// der Karte</b>. Der Vergleich ist der ganze Zweck: eine Liste von Zahlen zu
    /// einem einzelnen Soldaten sagt nichts.</para></summary>
    public void EntladeSchussProbeStart()
    {
        _fspStufe = 10; _fspUhr = 5f;
        Schussgruende = true;
    }

    /// <summary>Alles, was ueber das Schiessen entscheidet, in einer Zeile.</summary>
    private string FspFelder(Entity e)
        => $"Platz {e.Slot} \"{LabelOf(e)}\" Sp{e.Owner} ({e.Col},{e.Row}) "
         + $"Infanterie {e.Infantry} Aufsatz {e.Weapon} Waffenfahne {e.Armed} "
         + $"Angriff {e.Attack} Reichweite {e.Range}/min {e.RangeMin} "
         + $"TP {e.Hp}/{e.HpMax} Munition {e.Ammo}/{e.AmmoMax} Ukol {e.Ukol} "
         + $"beweglich {e.Mobile} Klasse {e.Move} eingegraben {e.DugIn} "
         + $"tot {e.Dead} Kulisse {e.IsProp} Gebaeude {e.IsBuilding} "
         + $"| kampffaehig {CanFightFuerProbe(e)} RangeOf {RangeOf(e):0.0} "
         + $"RangeMinOf {RangeMinOf(e):0.0} Schussfenster@1 {InFiringWindow(e, 1f)}";

    /// <summary>Der naechste FEIND dieses Soldaten, der eine Einheit ist —
    /// ein Gebaeude nimmt die Zielaufnahme von selbst nie (siehe AutoAcquire).</summary>
    private int FspOpferSuchen(Entity e)
    {
        int best = -1; float bestD = float.MaxValue;
        for (int j = 0; j < _entities.Count; j++)
        {
            var t = _entities[j];
            if (t.Dead || t.IsProp || t.IsBuilding) continue;
            if (!IsHostile(e, t)) continue;
            // ⚠ KEIN SCHIFF als Opfer. Ein Fusssoldat kann eine Zelle im Wasser
            // nie erreichen; die Verfolgung gibt dann zu Recht auf
            // (»kein Zielfeld«), und die Probe haette einen Befund ueber das
            // WASSER gemessen und ihn fuer einen ueber das Schiessen gehalten.
            // Auf map_05 stehen an der Kueste fast nur Boote — genau die Falle.
            if (!SchiffeAlsOpfer && t.Move == Simulation.NavGrid.MoveClass.Ship) continue;
            float d = CellDistance(e, t);
            if (d < bestD) { bestD = d; best = j; }
        }
        return best;
    }

    private void PollFussSchussProbe(float dt)
    {
        if (_fspStufe < 0 || _nav == null) return;
        switch (_fspStufe)
        {
            case 0:
                _fspUhr -= dt;
                if (_fspUhr > 0f) return;
                _fspStufe = 1;
                return;

            case 10:
            {
                _fspUhr -= dt;
                if (_fspUhr > 0f) return;

                // Ein Traeger mit Fussvolk an Bord.
                int traeger = -1; Entity? fracht = null;
                for (int i = 0; i < _entities.Count && traeger < 0; i++)
                {
                    var q = _entities[i];
                    if (q.Dead || q.IsProp || q.IsBuilding) continue;
                    foreach (var f in FrachtAnBord(q.Slot))
                        if (f.Infantry >= 0) { traeger = i; fracht = f; break; }
                }
                if (traeger < 0)
                {
                    GD.Print("entladeschuss-probe: kein Traeger mit Fussvolk an Bord - "
                           + "auf dieser Karte nicht stellbar");
                    _fspStufe = -1; return;
                }
                var tr = _entities[traeger];

                // Der Soldat von der KARTE, gegen den verglichen wird - derselbe
                // Entwurf, wenn es ihn gibt.
                int karte = -1;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var q = _entities[i];
                    if (q.Dead || q.IsProp || q.IsBuilding || q.Infantry < 0) continue;
                    if (fracht != null && q.Infantry != fracht.Infantry) continue;
                    karte = i; break;
                }

                GD.Print($"entladeschuss-probe: Traeger Platz {tr.Slot} \"{LabelOf(tr)}\" "
                       + $"auf ({tr.Col},{tr.Row}), {FrachtAnBord(tr.Slot).Count} an Bord");
                if (fracht != null) GD.Print($"   AN BORD   {FspFelder(fracht)}");
                if (karte >= 0)     GD.Print($"   AUF KARTE {FspFelder(_entities[karte])}");
                else                GD.Print("   AUF KARTE: keiner zum Vergleich");

                // ⚠ NICHT die Zelle des Traegers nehmen: er schwimmt, und
                // FreieZelleUm sucht nur wenige Ringe weit. Im Spiel klickt der
                // Spieler eine RAMPE an; hier wird die naechste begehbare freie
                // Zelle gesucht, damit die Probe an Land absetzt.
                Vector2I? ab = null;
                for (int ring = 1; ring <= 12 && ab == null; ring++)
                    for (int dc = -ring; dc <= ring && ab == null; dc++)
                        for (int dr = -ring; dr <= ring && ab == null; dr++)
                        {
                            if (Mathf.Max(Mathf.Abs(dc), Mathf.Abs(dr)) != ring) continue;
                            int c = tr.Col + dc, w = tr.Row + dr;
                            if (!_nav.InBounds(c, w) || !_nav.IsWalkable(c, w)) continue;
                            if (_nav.OccupantAt(c, w) >= 0) continue;
                            ab = new Vector2I(c, w);
                        }
                if (ab == null)
                {
                    GD.Print("entladeschuss-probe: keine begehbare freie Zelle um den Traeger");
                    _fspStufe = -1; return;
                }
                int vorher = _entities.Count;
                int n = FrachtAbsetzen(tr.Slot, ab.Value, 1);
                if (n == 0 || _entities.Count <= vorher)
                {
                    GD.Print("entladeschuss-probe: das Absetzen hat nichts abgesetzt - "
                           + "kein freier Platz um den Traeger");
                    _fspStufe = -1; return;
                }
                _fspVorgabe = _entities.Count - 1;
                GD.Print($"   ENTLADEN  {FspFelder(_entities[_fspVorgabe])}");

                // ⚠ UND JETZT DER FALL, DEN ER BESCHREIBT: »der Gegner war
                // genau vor der Infanterie«. Auf map_05 steht der naechste
                // Feind neun Zellen weit; ohne ihn heranzustellen, misst die
                // Probe nur »kein Ziel im Ring« und nicht das Schiessen.
                // ⭐ Das Umsetzen geht ueber dieselben drei Schritte wie das
                // Absetzen selbst (Beleger loeschen, Lage setzen, neu stempeln)
                // — sonst zeigt das Bewegungsgitter auf eine leere Zelle.
                var sch = _entities[_fspVorgabe];
                int nah = FspOpferSuchen(sch);
                if (GegnerNichtStellen) nah = -1;   // --gegner-nicht-stellen
                if (nah >= 0)
                {
                    var o = _entities[nah];
                    Vector2I? platz = null;
                    for (int dc = -1; dc <= 1 && platz == null; dc++)
                        for (int dr = -1; dr <= 1 && platz == null; dr++)
                        {
                            if (dc == 0 && dr == 0) continue;
                            int c = sch.Col + dc, w = sch.Row + dr;
                            if (!_nav.InBounds(c, w) || !_nav.IsWalkable(c, w)) continue;
                            if (_nav.OccupantAt(c, w) >= 0) continue;
                            platz = new Vector2I(c, w);
                        }
                    if (platz != null)
                    {
                        _nav.ClearOccupant(o.Col, o.Row, nah);
                        o.Col = platz.Value.X; o.Row = platz.Value.Y;
                        o.Elev = ElevOf(o.Col, o.Row);
                        o.Pos = CellCenter(o.Col, o.Row);
                        o.Footprint = CellRect(_ox, _oy, o.Col, o.Row, o.Elev);
                        o.Path = null; o.Reserved = null; o.Target = -1;
                        _nav.SetOccupant(o.Col, o.Row, nah, o.Infantry >= 0);
                        GD.Print($"   GESTELLT: Feind Platz {o.Slot} \"{LabelOf(o)}\" "
                               + $"auf ({o.Col},{o.Row}), also {CellDistance(sch, o):0.0} "
                               + "Zellen vor dem Ausgestiegenen.");
                    }
                    else GD.Print("   ⚠ keine freie Nachbarzelle — der Feind bleibt, wo er ist");
                }
                GD.Print("   ERWARTET: AUF KARTE und ENTLADEN unterscheiden sich in KEINEM "
                       + "Feld, das ueber das Schiessen entscheidet.");
                _fspStufe = 1;
                return;
            }

            case 1:
            {
                // Das PAAR suchen, nicht den ersten Soldaten: einer ohne Feind
                // auf der Karte waere ein Befund ueber meine Auswahl, nicht
                // ueber das Schiessen.
                _fspSchuetze = -1; _fspOpfer = -1;
                if (_fspVorgabe >= 0)
                {
                    _fspSchuetze = _fspVorgabe;
                    _fspOpfer = FspOpferSuchen(_entities[_fspSchuetze]);
                    if (_fspOpfer < 0)
                    {
                        GD.Print("fussschuss-probe: der Ausgestiegene hat keinen Feind auf "
                               + "der Karte - nicht stellbar");
                        _fspStufe = -1; return;
                    }
                }
                float bestD = float.MaxValue;
                for (int k = 0; k < _entities.Count && _fspVorgabe < 0; k++)
                {
                    var s0 = _entities[k];
                    if (s0.Dead || s0.IsProp || s0.IsBuilding) continue;
                    if (s0.Infantry < 0 || s0.Owner != ViewPlayer) continue;
                    if (!CanFight(s0) || s0.DugIn) continue;
                    int o = FspOpferSuchen(s0);
                    if (o < 0) continue;
                    float d = CellDistance(s0, _entities[o]);
                    if (d < bestD) { bestD = d; _fspSchuetze = k; _fspOpfer = o; }
                }
                if (_fspSchuetze >= 0 && _fspOpfer >= 0 && bestD > 1e9f)
                    bestD = CellDistance(_entities[_fspSchuetze], _entities[_fspOpfer]);
                if (_fspSchuetze < 0)
                {
                    GD.Print("fussschuss-probe: kein eigener kampffaehiger Fusssoldat mit "
                           + "Feind auf dieser Karte — nicht stellbar");
                    _fspStufe = -1; return;
                }
                var s = _entities[_fspSchuetze]; var v = _entities[_fspOpfer];
                _sel.Clear(); _sel.Add(_fspSchuetze);
                _fspVor1 = FussSchuesse;
                // ⚠ NICHT ueber PostAttack(v.Pos): dessen erster Schritt ist
                // Pick(), also der MAUSTREFFER, und der scheitert im kopflosen
                // Lauf am NEBEL — ein Befund ueber die Sicht, nicht ueber das
                // Schiessen. Der Satz geht trotzdem durch den BUS und damit
                // durch den Behandler, in dem am 06.09.2026 die zweite Sperre
                // sass; nur die Zielsuche unter dem Zeiger faellt weg.
                var c = Simulation.Commands.CommandRecord.Make(
                            Simulation.Commands.CommandOp.Attack, (byte)ViewPlayer,
                            (short)_fspSchuetze, (short)v.Col, (short)v.Row,
                            (short)_fspOpfer, (short)v.Row, 0);
                AngriffAbgewiesen = "";
                bool ab = Emit(c);
                GD.Print($"fussschuss-probe AUFBAU: Platz {s.Slot} \"{LabelOf(s)}\" "
                       + $"auf ({s.Col},{s.Row}), Aufsatz {s.Weapon}, Waffenfahne {s.Armed}, "
                       + $"Reichweite {RangeOf(s):0.0} (min {RangeMinOf(s):0.0}), "
                       + $"Munition {s.Ammo}/{s.AmmoMax}, Nachladen {ReloadOf(s):0.00}s\n"
                       // ⚠ 10.09.2026 — DER KLANGSATZ EINES ERZEUGTEN
                       // FUSSSOLDATEN. Er haengt an `Chassis`, und das war fuer
                       // alles aus Depot/Markt/Dock/Skript 0 (siehe
                       // InfanterieAnlegen). Ohne diese Zeile ist der Fehler im
                       // Lauf unsichtbar: der Standardsatz KLINGT ja.
                       + $"   Klangsatz: Chassis {s.Chassis} (Spodek), Satzindex "
                       + $"{(s.Chassis >> 1) - 1} "
                       + (s.Chassis == 0
                          ? "= Standard 189/193   ⚠ bei einer Art mit eigenem Satz FALSCH"
                          : "= eigener Satz")
                       + (char)10
                       + $"   OPFER Platz {v.Slot} \"{LabelOf(v)}\" auf ({v.Col},{v.Row}), "
                       + $"Abstand {bestD:0.0} Zellen; Befehl abgesetzt: {(ab ? "ja" : "NEIN")}"
                       + (AngriffAbgewiesen.Length > 0 ? $" ({AngriffAbgewiesen})" : ""));
                GD.Print("   ERWARTET Teil 1: er laeuft heran, haelt an und feuert.");
                if (!ab) { _fspStufe = -1; return; }
                _fspUhr = FspTeil1; _fspTicker = 0f; _fspStufe = 2;
                return;
            }

            case 2:
            case 4:
            {
                _fspUhr -= dt; _fspTicker -= dt;
                var d0 = _entities[_fspSchuetze];
                if (d0.Dead)
                {
                    // ⚠ Eine Leiche misst nichts. Ohne diese Zeile lief die
                    // Probe 40 Sekunden weiter und meldete »nicht geschossen«,
                    // waehrend der Soldat laengst gefallen war.
                    GD.Print($"fussschuss-probe ABBRUCH: der Schuetze ist GEFALLEN "
                           + $"(Schuesse bis dahin {FussSchuesse - _fspVor1}).");
                    _fspStufe = -1; return;
                }
                if (_fspTicker <= 0f)
                {
                    _fspTicker = 3f;
                    var v0 = _entities[_fspOpfer];
                    // ⭐ 08.09.2026 — DIE BLICKRICHTUNG GEHOERT IN DIE ZEILE.
                    // Seine Meldung »schiesst nach links, der Gegner steht
                    // rechts« ist genau dieser Vergleich: die Richtung, in der
                    // der Soldat GEZEICHNET wird, gegen die Richtung zum Ziel.
                    var vz = _entities[_fspOpfer];
                    int soll = DirToFacing(vz.Pos - d0.Pos);
                    GD.Print($"   [b] Blick {d0.Facing}, Zielrichtung {soll}, Turm {d0.AimFacing}"
                           + $" -> {(d0.Facing == soll ? "passt" : "PASST NICHT")}");
                    GD.Print($"   [t] ({d0.Col},{d0.Row}) {(d0.Dead ? "GEFALLEN " : "")}"
                           + $"TP {d0.Hp}/{d0.HpMax} Ziel {d0.Target} befohlen {d0.Ordered} "
                           + $"Weg {(d0.Path == null ? "keiner" : $"{d0.PathIdx}/{d0.Path.Count}")} "
                           + $"Abstand {CellDistance(d0, v0):0.0} cd {d0.Cooldown:0.00} "
                           + $"Schuesse {FussSchuesse}: {d0.Schussgrund}");
                }
                if (_fspUhr > 0f) return;

                if (_fspStufe == 2)
                {
                    int t1 = FussSchuesse - _fspVor1;
                    GD.Print($"fussschuss-probe TEIL 1 (befohlen) nach {FspTeil1:0} s: "
                           + $"Schuesse +{t1} -> "
                           + (t1 > 0 ? "GESCHOSSEN" : "NICHT GESCHOSSEN")
                           + $"; Abstand jetzt {CellDistance(d0, _entities[_fspOpfer]):0.0}, "
                           + $"Grund: {d0.Schussgrund}");
                    // Teil 2: den Auftrag wegnehmen, stehen lassen, zusehen.
                    d0.Target = -1; d0.Ordered = false; d0.Path = null;
                    _fspVor2 = FussSchuesse;
                    GD.Print("   ERWARTET Teil 2: die Zielaufnahme holt ihn von selbst wieder.");
                    _fspUhr = FspTeil2; _fspTicker = 0f; _fspStufe = 4;
                    return;
                }

                int t2 = FussSchuesse - _fspVor2;
                GD.Print($"fussschuss-probe TEIL 2 (von selbst) nach {FspTeil2:0} s: "
                       + $"Schuesse +{t2} -> " + (t2 > 0 ? "GESCHOSSEN" : "NICHT GESCHOSSEN")
                       + $"; Grund: {d0.Schussgrund}");
                _fspStufe = -1;
                return;
            }
        }
    }
}
