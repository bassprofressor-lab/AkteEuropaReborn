using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>`--rampen-probe` — DREHT SICH EINE EINHEIT AUF DER BRÜCKENRAMPE IM KREIS?</b>
///
/// <para>⚠ 03.09.2026, aus seinem Spiellauf der Kampagne 3 gemeldet:
/// »Einheiten die eine Brücke überfahren und sozusagen dort sind wo die
/// Schräge/Steigung drinne ist machen dort kurz wie einen Threesixty, sieht
/// lustig aus, fahren aber normal über die Brücke, der Effekt ist also immer
/// nur beim Auffahren bzw. Abfahren der Brücke.«</para>
///
/// <para><b>Warum eine eigene Probe.</b> Die Blickrichtung ist bei uns kein
/// Zustand, den man ablesen könnte — sie wird in JEDEM Takt aus
/// <c>dest − Pos</c> neu gerechnet (<see cref="MapEntityLayer.DirToFacing(Vector2)"/>),
/// und der Fahrer dreht dann eine Stufe je Takt darauf zu. Ein »Threesixty«
/// heisst also: die GEWÜNSCHTE Richtung springt auf der Rampe, und zwar so
/// weit, dass der kürzeste Weg um den Kreis mehrere Stufen lang ist. Das ist
/// mit blossem Nachrechnen nicht zu entscheiden, weil zwei Rechnungen
/// ineinandergreifen: die Höhenanhebung der ZELLMITTE
/// (<see cref="MapEntityLayer.HubOf"/>, in <c>dest</c>) und der laufende
/// Feinversatz der FAHRT (<see cref="MapEntityLayer.FahrtY"/>, in <c>Pos</c>).
/// Auf ebenem Gelände sind beide gleich; auf einer Rampe nicht.</para>
///
/// <para><b>Was gemessen wird.</b> Eine eigene fahrende Einheit wird vor eine
/// Rampenzelle gestellt und quer darüber geschickt. Je Takt werden Zelle und
/// Blickrichtung mitgeschrieben; gezählt wird, wie oft die Richtung WECHSELT
/// und wie gross der grösste Sprung dabei war.</para>
///
/// <para><b>Nullmodell — und es ist ein scharfes.</b> Derselbe Lauf mit
/// <c>--kein-hang</c> schaltet die ganze Schrägenrechnung ab (Höhe wirkt dann
/// gar nicht mehr auf die Bildlage). Kommt der Dreher DORT auch, liegt es
/// nicht an der Rampe, und die Probe hat die Ursache widerlegt statt sie zu
/// bestätigen. Ohne diese Gegenprobe ist eine Zahl hier nichts wert: eine
/// Einheit wechselt die Blickrichtung auch auf ebenem Gelände, sobald der Weg
/// eine Ecke macht.</para>
///
/// <para>⚠ Die Probe SETZT die Einheit um (auf die Zelle vor der Rampe). Das
/// ist zulässig, weil sie nichts über Wegsuche aussagen soll, sondern nur über
/// die Blickrichtung auf einer Schräge — aber es ist eine Setzung, und darum
/// steht sie hier.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private bool _rampenProbeLaeuft;
    private int _rampenSlot = -1;
    private int _rampenVonX, _rampenVonY, _rampenNachX, _rampenNachY;
    private int _rampenZelleX, _rampenZelleY;
    private readonly System.Collections.Generic.List<(int Takt, int Col, int Row, int Facing, int Art)>
        _rampenSpur = new();
    private int _rampenTakt;

    /// <summary>Die Rampenzelle, über die gefahren wird — die erste Zelle mit
    /// einer Schrägenart, deren beide Nachbarn in Fahrtrichtung befahrbar sind.
    /// Gesucht wird zuerst WAAGERECHT (Osten/Westen), denn genau die
    /// waagerechte Brücke hat er gemeldet.</summary>
    private bool RampeSuchen(out int cx, out int cy, out int dx, out int dy)
    {
        cx = cy = dx = dy = 0;
        if (_nav == null) return false;
        // Zwei Durchgänge: erst die Auf-/Abfahrten nach OSTEN (Art 1/3), dann
        // die nach SÜDEN (Art 2/4). Die Artnummern stehen in Simulation.Hang.
        foreach (var (rx, ry) in new[] { (1, 0), (0, 1) })
        {
            foreach (var ((c, r), _) in _elevLookup)
            {
                if (HangArt(c, r) == 0) continue;
                // drei Zellen davor und drei dahinter müssen befahrbar sein —
                // sonst kommt die Einheit gar nicht erst in Fahrt.
                bool frei = true;
                for (int k = -3; k <= 3 && frei; k++)
                {
                    int x = c + rx * k, y = r + ry * k;
                    if (!_nav.IsWalkable(x, y, Simulation.NavGrid.MoveClass.Vehicle)) frei = false;
                }
                if (!frei) continue;
                cx = c; cy = r; dx = rx; dy = ry;
                return true;
            }
        }
        return false;
    }

    public string RampenProbeStart()
    {
        if (_nav == null) return "rampen-probe: ⚠ keine Karte — die Probe misst NICHTS";
        if (!RampeSuchen(out int cx, out int cy, out int dx, out int dy))
            return "rampen-probe: ⚠ keine Rampenzelle mit freier An- und Abfahrt auf " +
                   "dieser Karte — die Probe misst NICHTS (map_02 / Kampagne 2 hat welche)";

        int i = _entities.FindIndex(e => !e.IsBuilding && !e.IsProp && !e.Dead && e.Mobile
                                         && e.Owner == 0 && e.Infantry < 0);
        if (i < 0)
            return "rampen-probe: ⚠ keine eigene FAHRENDE Einheit — die Probe misst NICHTS";

        var u = _entities[i];
        _rampenZelleX = cx; _rampenZelleY = cy;
        _rampenVonX = cx - dx * 3; _rampenVonY = cy - dy * 3;
        _rampenNachX = cx + dx * 3; _rampenNachY = cy + dy * 3;

        // umsetzen — mit Buchführung im Gitter, sonst bleibt der alte Platz belegt
        _nav.ClearOccupant(u.Col, u.Row, i);
        u.Col = _rampenVonX; u.Row = _rampenVonY;
        u.Elev = ElevOf(u.Col, u.Row);
        u.Pos = BodyCenterAt(u, u.Col, u.Row);
        u.Footprint = CellRect(_ox, _oy, u.Col, u.Row, u.Elev);
        u.Path = null; u.PathIdx = 0; u.Reserved = null; u.StepCost = 0; u.Progress = 0;
        _nav.SetOccupant(u.Col, u.Row, i, false);
        if (u.FuelMax > 0) u.Fuel = u.FuelMax;

        _rampenSlot = u.Slot;
        _rampenProbeLaeuft = true;
        _rampenTakt = 0;
        _rampenSpur.Clear();

        MissionOrderAt(u.Slot, _rampenNachX, _rampenNachY, -1);

        return $"rampen-probe: Rampenzelle ({cx},{cy}) Art {HangArt(cx, cy)} " +
               $"Hoehe {ElevOf(cx, cy)}, Fahrtrichtung ({dx},{dy})\n" +
               $"   Platz {u.Slot} \"{LabelOf(u)}\" von ({_rampenVonX},{_rampenVonY}) " +
               $"nach ({_rampenNachX},{_rampenNachY})\n" +
               $"   Schrägenrechnung: {(Simulation.Hang.Aus ? "AUS (--kein-hang, NULLMODELL)" : "an")}\n";
    }

    public void RampenProbeTick()
    {
        if (!_rampenProbeLaeuft) return;
        _rampenTakt++;
        var u = _entities.Find(e => e.Slot == _rampenSlot);
        if (u == null) return;
        // Nur die WECHSEL aufschreiben — eine Zeile je Takt wäre ein Roman, und
        // die Frage ist ohnehin nur, wann und wie weit die Richtung springt.
        if (_rampenSpur.Count == 0 || _rampenSpur[^1].Facing != u.Facing
                                   || _rampenSpur[^1].Col != u.Col || _rampenSpur[^1].Row != u.Row)
            _rampenSpur.Add((_rampenTakt, u.Col, u.Row, u.Facing, HangArt(u.Col, u.Row)));
    }

    public string RampenProbeLine()
    {
        if (!_rampenProbeLaeuft) return "rampen-probe: nicht gestartet";
        var sb = new System.Text.StringBuilder(
            $"rampen-probe: Rampenzelle ({_rampenZelleX},{_rampenZelleY}), " +
            $"Platz {_rampenSlot}, Schräge {(Simulation.Hang.Aus ? "AUS" : "an")}\n" +
            "   Takt  Zelle       Art  Blick            Sprung\n");

        int wechsel = 0, groesster = 0, wechselAufRampe = 0;
        int vorher = -1;
        foreach (var (takt, c, r, f, art) in _rampenSpur)
        {
            int sprung = 0;
            if (vorher >= 0 && f != vorher)
            {
                int diff = ((f - vorher) % 8 + 8) % 8;
                sprung = System.Math.Min(diff, 8 - diff);
                wechsel++;
                if (sprung > groesster) groesster = sprung;
                if (art != 0) wechselAufRampe++;
            }
            sb.Append($"   {takt,4}  ({c,3},{r,3})  {art,3}  {f} {FacingWord(f),-13} " +
                      (sprung > 0 ? $"{sprung}" : "") + "\n");
            vorher = f;
        }

        sb.Append($"   {_rampenSpur.Count} Einträge, {wechsel} Richtungswechsel " +
                  $"({wechselAufRampe} davon auf einer Schrägenzelle), " +
                  $"grösster Sprung {groesster} Stufen\n");
        // ⭐⭐ 07.09.2026 — UND KANN MAN DORT ENTLADEN? Seine Meldung: »ich
        // kann garnicht meine Schiffe entladen an den Rampen«. Die Mechanik war
        // gebaut, nur loeste sie niemand aus. Gemessen wird der ECHTE Weg —
        // dieselbe Funktion, die der Mauszeiger fragt — und beide Richtungen:
        // ohne beladenen Traeger darf der Zeiger NICHT anspringen.
        int rc2 = -1, rr2 = -1;
        for (int c = 0; c < 400 && rc2 < 0; c++)
            for (int r = 0; r < 400; r++)
                if (RampenAbsetzZelle(c, r) != null) { rc2 = c; rr2 = r; break; }
        if (rc2 >= 0)
        {
            int tr = -1;
            for (int i = 0; i < _entities.Count; i++)
            {
                var t = _entities[i];
                if (!t.IsBuilding && !t.IsProp && !t.Dead && IstTraeger(t)
                    && FrachtAnBord(t.Slot).Count > 0) { tr = i; break; }
            }
            var mitte = CellCenter(rc2, rr2);
            _sel.Clear();
            var ohne = CursorHintAt(mitte);
            if (tr >= 0) { ViewPlayer = _entities[tr].Owner; _sel.Add(tr); }
            var mit = CursorHintAt(mitte);
            _sel.Clear();
            bool zeigerOk = tr < 0 || (mit == Hint.Entladen && ohne != Hint.Entladen);
            // ⭐⭐ 08.09.2026 — GIBT ES EINEN LIEGEPLATZ? Seine Meldung nach
            // dem ersten Bau: »entladen kann ich nicht mehr« und »die Boote tun
            // sich sehr schwer beim Fahren«. Der Klick schickte den Traeger auf
            // die RAMPE — die ist Land, dorthin kann ein Schiff nie, und die
            // Wegsuche rechnete endlos an einem unerreichbaren Ziel.
            if (tr >= 0)
            {
                var absetzZelle = RampenAbsetzZelle(rc2, rr2);
                var lp = absetzZelle == null ? null : LiegeplatzAnFuerProbe(_entities[tr], absetzZelle.Value);
                bool lpOk = lp != null;
                sb.Append($"   Liegeplatz zur Rampe ({rc2},{rr2}), Absetzzelle "
                        + $"({absetzZelle?.X},{absetzZelle?.Y}): "
                        + (lpOk ? $"({lp!.Value.X},{lp.Value.Y}) — dort traegt das Wasser "
                                  + $"das ganze Schiff, und von dort liegt es an: richtig"
                                : "KEINER GEFUNDEN — der Traeger kaeme nie an")
                        + System.Environment.NewLine);
            }

            // ⭐⭐ 08.09.2026 — DIE GANZE KETTE, so wie der Spieler sie geht:
            // Traeger an den Liegeplatz, Befehl absetzen, Takte laufen lassen.
            // Seine Meldung: »kann immer noch keine einheiten absetzen«, obwohl
            // die Boote genau neben der Rampe stehen. Ein Lauf, der nur
            // FrachtAbsetzen direkt ruft, haette das nie gefunden — er umgeht
            // Befehl und Takt.
            if (tr >= 0)
            {
                var azelle = RampenAbsetzZelle(rc2, rr2);
                var lp2 = azelle == null ? null : LiegeplatzAnFuerProbe(_entities[tr], azelle.Value);
                if (lp2 != null)
                {
                    var t2 = _entities[tr];
                    t2.Col = lp2.Value.X; t2.Row = lp2.Value.Y;
                    t2.Pos = CellCenter(t2.Col, t2.Row);
                    int vorLadung = FrachtAnBord(t2.Slot).Count;
                    int rc = PostUnload(tr, rc2, rr2);
                    // ⚠⚠ DER RING. Ein Befehl WIRKT erst, wenn der Behandler
                    // ihn aus dem Ring genommen hat — genau die Falle, die im
                    // cerebrum steht (»ein Pruefstand, der nur den ABSENDER
                    // fragt, ist kein Beleg«). Ohne diese Zeile stand hier
                    // »Auftrag 0«, und das war der Pruefstand, nicht das Spiel.
                    CommandTick();
                    // ⚠⚠ DER BELADE-TAKT MUSS MITLAUFEN. Ohne ihn ging dieser
                    // Lauf gruen durch, waehrend im Spiel gar nichts von Bord
                    // kam: der Ausgestiegene wurde sofort wieder eingeladen
                    // (--entlade-log, »1 Stueck, noch 14 an Bord«, hundertfach).
                    // Ein Pruefstand, der nur die halbe Kette laufen laesst,
                    // misst die andere Haelfte nicht.
                    int takte = 0;
                    while (takte < 60 && FrachtAnBord(t2.Slot).Count > vorLadung - 3)
                    { FrachtAbsetzenTaktFuerProbe(); BeladeTaktFuerProbe(); takte++; }
                    int nachLadung = FrachtAnBord(t2.Slot).Count;
                    bool ketteOk = rc >= 0 && nachLadung <= vorLadung - 3;
                    sb.Append($"   GANZE KETTE: Befehl {(rc >= 0 ? "angenommen" : "abgewiesen: " + UnloadNote)}, "
                            + $"Traeger auf ({t2.Col},{t2.Row}), Auftrag {t2.UnloadRest}, "
                            + $"Ladung {vorLadung} -> {nachLadung} nach {takte} Takten "
                            + $"(weggefahren {AbgesetztWeggeschickt}, gesperrt {AbgesetztGesperrt}): "
                            + (ketteOk ? "richtig" : "FALSCH — es kommt nichts von Bord")
                            + System.Environment.NewLine);
                }
            }

            // ⭐⭐ 07.09.2026 — UND WAS KANN DIE AUSGESETZTE EINHEIT? Seine
            // Meldung: »Die entladene Infanterie konnte nicht schiessen, als
            // haette sie keine Munition«, und »die entladenen Fahrzeuge wurden
            // erst ohne Waffenturm angezeigt«. Beides ist am Zustand des
            // Ausgestiegenen zu messen, nicht am Auge.
            if (tr >= 0)
            {
                var tE = _entities[tr];
                var warSchon = new System.Collections.Generic.HashSet<int>();
                foreach (var q in _entities) warSchon.Add(q.Slot);
                tE.Col = rc2; tE.Row = rr2; tE.Pos = CellCenter(rc2, rr2);
                int raus = FrachtAbsetzen(tE.Slot, new Vector2I(rc2, rr2), 2);
                int gemessen = 0;
                foreach (var q in _entities)
                {
                    if (warSchon.Contains(q.Slot) || gemessen >= 2) continue;
                    gemessen++;
                    sb.Append($"   ausgesetzt: Platz {q.Slot} \"{q.Name}\" Munition {q.Ammo}/{q.AmmoMax}, "
                            + $"Angriff {q.Attack}, Reichweite {q.Range}, Ziel {q.Target}, "
                            + $"Blick {q.Facing}/Turm {q.AimFacing}, beweglich {q.Mobile}, "
                            + $"Waffe {q.Weapon}, Waffenfahne {q.Armed}, kampffaehig {CanFightFuerProbe(q)}"
                            + (q.Ammo <= 0 && q.AmmoMax > 0 ? "  ⚠ OHNE MUNITION" : "")
                            + (q.AimFacing < 0 ? "  ⚠ KEINE TURMRICHTUNG" : "")
                            + (q.Path != null ? $"  faehrt noch ({q.Path.Count} Schritte)" : "  steht")
                            + System.Environment.NewLine);
                }
                // ⚠ Und die TRAGWEITE: gilt das nur fuer Ausgesetzte oder fuer
                // JEDE Infanterie? Ohne diese Zahl haelt man einen alten
                // Grundfehler fuer eine Folge des Ausladens.
                int inf = 0, infKampf = 0, fz = 0, fzKampf = 0;
                foreach (var q in _entities)
                {
                    if (q.IsBuilding || q.IsProp || q.Dead) continue;
                    if (q.Infantry >= 0) { inf++; if (CanFightFuerProbe(q)) infKampf++; }
                    else if (q.GameUnitType == 0) { fz++; if (CanFightFuerProbe(q)) fzKampf++; }
                }
                // ⚠⚠ 08.09.2026 — UND SCHIESST SIE AUCH? »kampffaehig« ist nur
                // die halbe Frage: ein Fusssoldat schiesst NUR IM STAND
                // (@0x40F0A0), und wer einen Weg hat, feuert nie. Nach dem
                // Absetzen faehrt er absichtlich vom Ufer weg — der Weg muss
                // also auch WIEDER ENDEN. Gemessen wird darum ueber Takte.
                // ⚠ Nur die AUSGESETZTEN zaehlen: die uebrige Infanterie der
                // Karte laeuft ohnehin herum, und ihre Wege sagen hier nichts.
                var frisch = new System.Collections.Generic.List<int>();
                foreach (var q in _entities)
                    if (!warSchon.Contains(q.Slot) && q.Infantry >= 0) frisch.Add(q.Slot);
                for (int t = 0; t < 200; t++) MoveTickFuerProbe(1f / 50f);
                int mitWeg = 0, ohneWeg = 0;
                foreach (var q in _entities)
                {
                    if (!frisch.Contains(q.Slot) || q.Dead) continue;
                    if (q.Path != null) mitWeg++; else ohneWeg++;
                }
                sb.Append($"   die AUSGESETZTEN nach 200 Takten (4 s): {ohneWeg} stehen "
                        + $"(koennen feuern), {mitWeg} faehrt noch"
                        + (mitWeg > 0 ? "  ⚠ wer ewig faehrt, schiesst nie" : "  — richtig")
                        + System.Environment.NewLine);

                sb.Append($"   kampffaehig auf der ganzen Karte: Fussvolk {infKampf}/{inf}, "
                        + $"Fahrzeuge {fzKampf}/{fz}"
                        + (inf > 0 && infKampf == 0 ? "  ⚠⚠ KEIN EINZIGER Fusssoldat kann schiessen"
                                                    : "")
                        + System.Environment.NewLine);

                if (raus == 0)
                    sb.Append("   ausgesetzt: NICHTS — die Absetzzelle nahm niemanden auf"
                            + System.Environment.NewLine);
            }

            sb.Append($"   Entladezeiger auf der Rampe ({rc2},{rr2}): ohne Auswahl {ohne}, "
                    + $"mit beladenem Traeger {mit}: "
                    + (tr < 0 ? "kein beladener Traeger — UNGEPRUEFT" 
                              : zeigerOk ? "richtig" : "FALSCH")
                    + System.Environment.NewLine);
        }

        sb.Append("   ⭐ Erwartet auf einer GERADEN Fahrt über eine Rampe: 0 Wechsel.\n" +
                  "     Ein »Threesixty« sind mindestens 4 Wechsel auf derselben Zelle.\n" +
                  "   ⚠ NULLMODELL: derselbe Lauf mit --kein-hang. Sind die Zahlen dort\n" +
                  "     gleich, liegt es NICHT an der Schrägenrechnung.\n");
        return sb.ToString();
    }

    /// <summary>0 = sauber (keine Wechsel), 1 = die Richtung springt.</summary>
    public int RampenProbeRc() => _rampenSpur.Count > 1 && RampenWechsel() > 0 ? 1 : 0;

    private int RampenWechsel()
    {
        int n = 0, vorher = -1;
        foreach (var (_, _, _, f, _) in _rampenSpur)
        {
            if (vorher >= 0 && f != vorher) n++;
            vorher = f;
        }
        return n;
    }
}
