namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// DIE EINHÄNGUNG DER BEFEHLSSCHICHT in die Spielwelt.
///
/// <para>Eine eigene Teildatei von <see cref="MapEntityLayer"/>, weil die
/// Befehlsschicht und die Zeichen-/Weltdatei getrennten Händen gehören. Hier
/// stehen drei Dinge und nichts sonst:</para>
/// <list type="number">
///   <item><b>Der Absender</b> — <see cref="PostMove"/>,
///   <see cref="PostAttack"/>, <see cref="PostStop"/>. Sie rechnen aus, was der
///   Satz tragen muss, und legen ihn hin. Sie fassen den Zustand NICHT an.</item>
///   <item><b>Der Behandler</b> — <see cref="ApplyCommand"/> und was daran
///   hängt. Er läuft am Taktanfang und ist die einzige Stelle, die aus einem
///   Befehl Zustand macht.</item>
///   <item><b>Der Taktanfang</b> — <see cref="CommandTick"/>. Diese eine Zeile
///   gehört an den Anfang von <c>SimTick</c>.</item>
/// </list>
///
/// <para>⚠ <b>Warum der Behandler den Rumpf von <c>IssueMove</c> nachbaut und
/// ihn nicht aufruft.</b> <c>IssueMove</c> tut beides in einem: es entscheidet
/// über die Gruppe UND schreibt den Zustand. Für Lockstep muss genau dieser
/// Schnitt gemacht werden. Der Rumpf ist deshalb hier nachgebaut — und weil
/// eine Abschrift auseinanderlaufen kann, prüft <c>--befehl-check</c> nicht auf
/// Zusehen, sondern <b>vergleicht die beiden Wege auf derselben Karte Zahl für
/// Zahl</b>. Solange dieser Vergleich grün ist, ist die Abschrift belegt und
/// nicht behauptet; wird er rot, sagt er, welche Einheit welches Feld
/// unterscheidet.</para>
/// </summary>
public partial class MapEntityLayer
{
    private CommandRing? _cmdRing;
    private int _cmdTick;
    private CommandRecord _cmdLast;
    private int _cmdLastTick = -1;

    /// <summary>
    /// WOHIN EIN FRISCH ABGESETZTER SATZ GEHT. <c>null</c> = geradewegs in den
    /// eigenen Ring; das ist der Einzelspieler.
    ///
    /// <para>Im Netzspiel hängt hier der Ausgangskorb des Taktgebers
    /// (<c>Network/NetGame.cs</c>): der Satz geht dann <b>erst über die
    /// Leitung</b> und kommt über <see cref="PostRaw"/> in den Ring zurück — auch
    /// der eigene. Genau das tut <c>post()</c> @0x4C1C50: bei
    /// <c>[0x538270] == 0</c> (Mitspieler) geht der Satz <b>nur</b> in
    /// <c>IDirectPlay::Send</c> und wird erst ausgeführt, wenn er über
    /// <c>Receive</c> im Ring wieder auftaucht.</para>
    ///
    /// <para>⚠ <b>Warum eine Weiche und nicht zwei Absender.</b> Ein zweiter
    /// Absender für den Netzfall wäre ein zweiter Ort, an dem entschieden wird,
    /// welche Einheit welche Zelle bekommt — und damit ein zweiter Ort, an dem
    /// sich Einzel- und Mehrspieler unterscheiden können, ohne dass ein Prüfstand
    /// es sieht. So ist es EIN Absender, und nur das Rohr dahinter wechselt.</para>
    ///
    /// <para>⚠ Je Instanz, nicht statisch — aus demselben Grund wie der Ring
    /// selbst (siehe <see cref="Commands"/>).</para>
    /// </summary>
    public System.Func<CommandRecord, bool>? CommandSink;

    /// <summary>Den Satz auf den Weg bringen: in den Ring, oder ins Netz, je
    /// nachdem, ob <see cref="CommandSink"/> gesetzt ist.</summary>
    private bool Emit(in CommandRecord c)
    {
        var sink = CommandSink;
        return sink != null ? sink(c) : Commands.Post(c);
    }

    /// <summary>Die Mitte einer Zelle in Weltkoordinaten — <c>CellCenter</c> ist
    /// privat und liegt in einer fremden Datei; diese Zeile macht sie für den
    /// Netztaktgeber und die Prüfstände erreichbar, ohne dort etwas zu
    /// ändern.</summary>
    public Vector2 CellCenterFor(Vector2I cell) => CellCenter(cell.X, cell.Y);

    /// <summary>Der Takt, in dem die Befehlsschicht dieser Simulation steht —
    /// die Zahl, gegen die die Fälligkeit geprüft wird. Der Netztaktgeber
    /// braucht sie, um seine Taktnummern an die der Simulation zu binden statt
    /// eine zweite Zählung daneben zu führen.</summary>
    public int CommandClock => _cmdTick;

    /// <summary>Der Ring dieser Simulation, beim ersten Zugriff angelegt.
    /// ⚠ Je Simulation einer — der Zwillings-Prüfstand hat zwei
    /// <c>MapEntityLayer</c> im selben Prozess, und ein gemeinsamer Ring wäre
    /// genau der Fehler, den Stufe 1 an vier Zufallsquellen gefunden hat.</summary>
    public CommandRing Commands => _cmdRing ??= CommandManager.NewRing();

    /// <summary>Wie viele Takte diese Simulation Befehle verarbeitet hat.
    /// Zählt unabhängig von <c>DebugTicks</c>, damit ein Prüfstand sehen kann,
    /// ob der Taktanfang überhaupt angeschlossen ist (Regel 11).</summary>
    public int CommandTicks { get; private set; }

    /// <summary>
    /// <b>DER TAKTANFANG.</b> Gehört als erste Zeile in <c>SimTick(dt)</c>.
    ///
    /// <para>Vorher, nicht nachher: ein Befehl, der nach der Bewegung wirkt,
    /// verschiebt eine Einheit um einen Takt gegenüber dem, was der Spieler
    /// gesehen hat — und, schlimmer, gegenüber der anderen Maschine, wenn dort
    /// die Reihenfolge anders herum steht. Die Reihenfolge im Takt ist Teil
    /// dessen, was geprüft wird (Regel 7).</para>
    /// </summary>
    public void CommandTick()
    {
        CommandTicks++;
        Commands.ApplyDue(_cmdTick, c =>
        {
            _cmdLast = c;
            _cmdLastTick = _cmdTick;
            return ApplyCommand(c);
        });
        _cmdTick++;
    }

    public CommandManager.Report CommandReport() => new(Commands, _cmdLast, _cmdLastTick);

    // ================= DER ABSENDER ==========================================

    /// <summary>
    /// Ein Bewegungsklick wird zu <b>je einem Satz für jede gemeinte
    /// Einheit</b> — so wie das Original es tut.
    ///
    /// <para><b>Belegt:</b> der Absender des Originals @0x4342E9..0x43433E
    /// schreibt Opcode 3 mit <c>P1 = Einheitsnummer</c> aus der Auswahlliste
    /// (0x8320F8, je Eintrag ein Wort, nur wenn <c>&lt; 8000</c> = Länge der
    /// Einheitentafel) und <c>P2/P3 = die Zielzelle DIESER Einheit</c>. Ein
    /// Gruppenklick auf zehn Einheiten sind also zehn Sätze, jeder mit seiner
    /// eigenen Zielzelle. Genau das steht hier.</para>
    ///
    /// <para>⚠ <b>GELESEN, ABER NICHT GEBAUT</b> (Regel 12 — der Grund des
    /// Nichteinbaus gehört belegt): das Original berechnet die Zielzelle als
    /// <c>Einheit.x - Mittelwert.x + Klick.x</c> (dieselbe Rechnung für y; der
    /// Mittelwert kommt aus einer <c>idiv</c>-Summe über die Auswahl,
    /// @0x4342D0). Das ist eine <b>Formationsverschiebung</b>: die Gruppe behält
    /// ihre Aufstellung. Unser Spiel sucht stattdessen freie Zellen im Ring um
    /// den Klick. Auf die Formationsregel umzustellen würde das
    /// Gruppenverhalten ändern — das ist eine Verhaltensfrage und gehört dem
    /// Eigentümer der Befehlslogik, nicht der Befehlsschicht. Der Absender
    /// bleibt daher unser bisheriger; er steckt sein Ergebnis nur nicht mehr in
    /// den Zustand, sondern in den Satz.</para>
    ///
    /// <para>Der Absender DARF den Zustand lesen (freie Zelle, Sprit): er läuft
    /// nur auf der Maschine mit dem Klick, und alles, was er ausrechnet, steht
    /// danach als Zahl im Satz. Die Auswahl <c>_sel</c> ist ein
    /// <c>HashSet&lt;int&gt;</c>, dessen Durchlaufreihenfolge nicht zugesagt ist
    /// — das ist hier <b>unschädlich</b>, weil sie nur bestimmt, welche Einheit
    /// welche Zelle bekommt, und dieses Ergebnis mitgeschickt wird. Liefe der
    /// Absender auf beiden Maschinen, wäre es eine Fehlerquelle.</para>
    /// </summary>
    /// <returns>Wie viele Sätze abgesetzt wurden.</returns>
    public int PostMove(Vector2 mapPos, bool queue = false)
    {
        if (_nav == null) return 0;
        var cell = CellAt(mapPos);
        if (cell == null) { _order = "outside the map"; return 0; }

        int n = 0;
        var taken = new HashSet<Vector2I>();
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (!e.Mobile || e.Dead || e.DugIn) continue;
            if (e.FuelMax > 0 && e.Fuel <= 0) continue;   // ein trockener Panzer fährt nicht

            Vector2I goal = PickGoalCell(i, e, cell.Value, taken);
            if (goal.X < 0) continue;
            taken.Add(goal);

            var c = CommandRecord.Make(CommandOp.Move, (byte)ViewPlayer,
                                 (short)i, (short)goal.X, (short)goal.Y,
                                 (short)(queue ? 1 : 0));
            if (Emit(c)) n++;
        }

        // Die Rückmeldung an den Spieler ist ANZEIGE und darf sofort kommen —
        // sie berührt den Zustand nicht. Der Befehl selbst wirkt erst im
        // nächsten Takt; genau diese Trennung ist der ganze Umbau.
        if (n > 0) AddOrderMark(CellCenter(cell.Value.X, cell.Value.Y), attack: false);
        _order = n > 0
            ? $"Befehl abgesetzt -> ({cell.Value.X},{cell.Value.Y}): {n} Satz/Sätze"
            : "kein Befehl abgesetzt";
        UpdatePanel();
        QueueRedraw();
        return n;
    }

    /// <summary>
    /// <b>FLUGZIEL SETZEN</b> — der Rechtsklick für ein angewähltes Flugzeug.
    ///
    /// <para>Gemeldet: »im Gefecht wäre es doch sinnvoll die Einheiten
    /// eigenständig zu steuern oder nicht?«. Ja — aber es ist eine
    /// <b>Abweichung</b>, und sie steht bei <see cref="CommandOp.OursAirMove"/>
    /// mit dem Negativbefund, auf den sie sich stützt: kein Befehlsbehandler
    /// des Originals schreibt das Zielfeld eines Flugzeugs.</para>
    ///
    /// <para>Der Weg ist derselbe wie bei jedem anderen Befehl — über den Ring,
    /// wirksam am nächsten Taktanfang. Ein zweiter, direkter Draht für
    /// Flugzeuge wäre genau die Sorte Ausnahme, die ein Netzspiel später
    /// auseinanderlaufen lässt.</para>
    ///
    /// <para>⚠ P1 ist der <b>Steckplatz</b> des Flugzeugs, nicht ein
    /// Einheitenindex. Deshalb darf dieser Befehl nicht durch
    /// <see cref="Owns"/> laufen — das prüft <c>_entities[P1]</c> und würde hier
    /// eine fremde Einheit befragen.</para></summary>
    /// <returns>Wie viele Sätze abgesetzt wurden (0 oder 1).</returns>
    public int PostAirMove(Vector2 mapPos, bool queue = false)
    {
        if (_nav == null) return 0;
        if (_selAir < 0 || _selAir >= _special.Count) return 0;
        var a = _special[_selAir];
        if (a.Dead || a.Stored) { _order = "das Flugzeug steht im Hangar"; return 0; }
        if (a.Owner != ViewPlayer) { _order = "nicht Ihr Flugzeug"; return 0; }

        var cell = CellAt(mapPos);
        if (cell == null) { _order = "outside the map"; return 0; }

        var c = CommandRecord.Make(CommandOp.OursAirMove, (byte)ViewPlayer,
                                   (short)a.Slot, (short)cell.Value.X, (short)cell.Value.Y);
        if (!Emit(c)) { _order = "kein Befehl abgesetzt"; return 0; }

        AddOrderMark(CellCenter(cell.Value.X, cell.Value.Y), attack: false);
        _order = $"Flugziel abgesetzt -> ({cell.Value.X},{cell.Value.Y})";
        UpdatePanel();
        QueueRedraw();
        return 1;
    }

    /// <summary>Einen Bewegungsbefehl für EINE Einheit auf EINE Zelle absetzen —
    /// ohne die Streuung von <see cref="PostMove"/>.
    ///
    /// <para>⚠ Der Unterschied ist der ganze Zweck: <c>PostMove</c> gibt jeder
    /// Einheit ein eigenes Ziel im Umkreis von acht Zellen, damit eine Gruppe
    /// nicht auf einen Punkt fährt. Beim Einnehmen ist genau diese Streuung
    /// falsch — die Einheit muss auf DIE Türzelle. Steht sie schon jemandem
    /// zu, wird der nächste freie Platz genommen, aber eng: Radius 1 statt
    /// 8.</para></summary>
    private bool PostMoveOne(int index, Vector2 mapPos, bool queue)
    {
        if (_nav == null) return false;
        var cell = CellAt(mapPos);
        if (cell == null) return false;
        var e = _entities[index];
        if (!e.Mobile || e.Dead || e.DugIn) return false;
        if (e.FuelMax > 0 && e.Fuel <= 0) return false;

        var goal = cell.Value;
        if (!_nav.IsFree(goal.X, goal.Y, e.Move, index))
        {
            bool found = false;
            for (int rad = 1; rad <= 1 && !found; rad++)
                for (int dy = -rad; dy <= rad && !found; dy++)
                    for (int dx = -rad; dx <= rad && !found; dx++)
                    {
                        var c = new Vector2I(goal.X + dx, goal.Y + dy);
                        if (_nav.IsFree(c.X, c.Y, e.Move, index)) { goal = c; found = true; }
                    }
            if (!found) return false;
        }
        return Emit(CommandRecord.Make(CommandOp.Move, (byte)ViewPlayer,
                                       (short)index, (short)goal.X, (short)goal.Y,
                                       (short)(queue ? 1 : 0)));
    }

    /// <summary>Die Zielzellensuche des heutigen <c>IssueMove</c>, wörtlich
    /// übernommen, damit der Vergleich der beiden Wege überhaupt eine Aussage
    /// hat. Liefert (-1,-1), wenn nichts frei ist.</summary>
    private Vector2I PickGoalCell(int i, Entity e, Vector2I click, HashSet<Vector2I> taken)
    {
        for (int rad = 0; rad <= 8; rad++)
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != rad) continue;
                    var c = new Vector2I(click.X + dx, click.Y + dy);
                    if (taken.Contains(c)) continue;
                    if (_nav!.IsFree(c.X, c.Y, e.Move, i)) return c;
                }
        return new Vector2I(-1, -1);
    }

    /// <summary>
    /// Ein Angriffsklick wird zu je einem Satz je Einheit —
    /// <b>Busbefehl 11</b>, der Angriffsbefehl des Originals.
    ///
    /// <para>⭐⭐ <b>Umgebaut am 22.08.2026.</b> Hier stand bis dahin die eigene
    /// Nummer 2001 mit der Begründung, für einen Angriff mit ZIELEINHEIT sei
    /// im Original kein Behandler belegt. Das ist widerlegt: Absender
    /// <c>0x4353F0</c>, Behandler <c>0x4C2DDD</c> → <c>order()</c>. Die
    /// Herleitung samt Griffraum steht bei <see cref="CommandOp.Attack"/>.</para>
    ///
    /// <para>⭐ <b>Und der Angriff formiert sich um das Ziel</b> (0x4353F0
    /// Teil 2/3). Wir haben bisher alle Einheiten auf dieselbe Zelle geschickt;
    /// das Original rechnet je Einheit
    /// <c>eigenePosition + Zielposition − Schwerpunkt</c> — derselbe Ausdruck
    /// wie beim Bewegungsbefehl (<c>0x4342D0</c>), nur mit der Zielzelle statt
    /// der Klickzelle. Die Gruppe behält also ihre Aufstellung, statt sich auf
    /// einem Punkt zu stapeln.</para>
    /// </summary>
    /// <returns>false, wenn der Klick kein Ziel getroffen hat — dann darf der
    /// Aufrufer daraus einen Bewegungsbefehl machen, genau wie heute.</returns>
    public bool PostAttack(Vector2 mapPos, bool queue = false)
    {
        int hit = Pick(mapPos);
        if (hit < 0) return false;
        var victim = _entities[hit];
        if (victim.IsProp || victim.Dead) return false;

        // ---- Teil 1: das Ziel in UTOK_NA uebersetzen (0x4353F0 @0x435403 ff.)
        // Ein Gebaeude bekommt 60000 + Platz, eine Einheit ihre eigene Nummer.
        // ⚠ Die zwei anderen Baender des Griffraums (30000 + Spalte fuer eine
        // blosse Bodenzelle, 40100..40249 fuer Bruecke/Rampe) sind hier NICHT
        // erreichbar, weil Pick() nur Einheiten und Gebaeude trifft. Sie sind
        // bei CommandOp.Attack dokumentiert und gehoeren zu dem Tag, an dem der
        // Angriff auch auf leeres Gelaende gehen darf.
        int utok = victim.IsBuilding ? 60000 + victim.Slot : hit;

        // ---- Teil 2: der Schwerpunkt der Auswahl (0x4353F0, idiv-Summe) ----
        int sx = 0, sy = 0, cnt = 0;
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (i == hit || !CanFight(e) || !IsHostile(e, victim)) continue;
            sx += e.Col; sy += e.Row; cnt++;
        }
        if (cnt == 0) return false;
        int mx = sx / cnt, my = sy / cnt;      // ganzzahlig, wie das Original

        // ---- Teil 3: je Ausgewaehltem ein Satz -----------------------------
        int n = 0;
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (i == hit || !CanFight(e) || !IsHostile(e, victim)) continue;
            int px = e.Col + victim.Col - mx;
            int py = e.Row + victim.Row - my;
            var c = CommandRecord.Make(CommandOp.Attack, (byte)ViewPlayer,
                                 (short)i, (short)px, (short)py,
                                 (short)utok, (short)victim.Row,
                                 (short)(queue ? 1 : 0));
            if (Emit(c)) n++;
        }
        if (n == 0) return false;
        AddOrderMark(victim.Pos, attack: true);
        _order = $"Angriffsbefehl abgesetzt -> Slot {victim.Slot}: {n} Satz/Sätze";
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    /// <summary>
    /// <b>»EINNEHMEN« — Strg+Rechtsklick auf ein Gebäude.</b> Die Antwort auf
    /// die Fehler C9 und C11 (17.08.2026).
    ///
    /// <para><b>Was gemeldet war:</b> »Werft und Seedock lassen sich nicht
    /// einnehmen, nur Angreifen kann man sie« und »Von KI eingenommene Gebäude
    /// kann man nicht einnehmen, nur zerstören«.</para>
    ///
    /// <para><b>Was gemessen wurde</b> (<c>--door-check</c>, NET02 und DM_4):
    /// die Türen sind erreichbar — Werft-Station <b>1 von 1</b> bzw. <b>2 von
    /// 2</b>, Basis, Fabriken, Flughafen, Mine, Bahnhöfe alle vollständig. Es
    /// lag also NICHT an der Einnahme und nicht am Gelände.</para>
    ///
    /// <para><b>Die Ursache ist der KLICKWEG</b> (MapViewer, rechte Maustaste):
    /// er versucht zuerst <see cref="PostAttack"/> und macht nur daraus einen
    /// Bewegungsbefehl, wenn der Klick <i>kein</i> Ziel getroffen hat. Ein
    /// FEINDLICHES Gebäude ist aber ein Ziel — der Befehl wurde damit immer zum
    /// Angriff, und die Einheit konnte die Türzelle gar nicht erreichen. Bei
    /// einem NEUTRALEN Gebäude (Besitzer 11) greift niemand an, deshalb ging es
    /// dort und nur dort. Genau das beschreiben beide Meldungen.</para>
    ///
    /// <para>⚠ <b>Warum eine eigene Taste und nicht »Rechtsklick nimmt ein«:</b>
    /// beides ist gewollt. Wer eine Werft nicht braucht, schiesst sie kaputt;
    /// wer sie will, nimmt sie ein. Eine Weiche, die das für den Spieler
    /// entscheidet, läge in der Hälfte der Fälle falsch — und sie wäre
    /// gefährlich: eine Einheit, die statt zu schiessen an die Tür fährt,
    /// verliert ein Gefecht.</para>
    ///
    /// <para>⚠ <b>Es ist KEIN neuer Befehlssatz.</b> Einnehmen heisst im
    /// Original wie bei uns nur »auf der Türzelle stehen«; der Behandler dafür
    /// ist die Einnahme selbst (Capture.cs), nicht ein Opcode. Deshalb rechnet
    /// diese Stelle nur die ZELLE aus und setzt einen gewöhnlichen
    /// <see cref="PostMove"/> ab. Damit läuft sie durch denselben Ring, ist auf
    /// zwei Maschinen derselbe Satz, und es gibt keine zweite Wahrheit über
    /// Wege.</para>
    ///
    /// <para>⚠ UNSERE Zutat, und im Gefecht ausdrücklich erlaubt: das Original
    /// hat keine solche Taste, weil dort niemand auf die Idee kam, sie zu
    /// brauchen — es kennt die Weiche »erst angreifen« gar nicht.</para>
    /// </summary>
    /// <returns>false, wenn dort kein einnehmbares fremdes Gebäude steht —
    /// dann darf der Aufrufer weitermachen wie bisher.</returns>
    public bool PostCapture(Vector2 mapPos, bool queue = false)
    {
        int hit = Pick(mapPos);
        if (hit < 0) return false;
        var b = _entities[hit];
        if (!b.IsBuilding || b.IsProp || b.Dead) return false;
        if (b.Owner == ViewPlayer)
        { _order = "Das gehoert bereits Ihnen."; return true; }
        if (!Capturable(b))
        {
            // ⚠ Das ist eine AUSKUNFT und keine Panne: Seedock (0 Türen in 39
            // von 39), Kraftwerk (0 in 262) und Radarstellung haben im ORIGINAL
            // keine Tür. Der Spieler soll das erfahren, statt auf eine Einheit
            // zu warten, die nie ankommt.
            _order = $"{BuildingName(b)} hat keine Tuer — nicht einnehmbar" +
                     (b.BType == 11 ? " (der Hafen wechselt MIT seiner Werft-Station)" : "");
            return true;
        }

        // Die nächstgelegene Türzelle zu der Einheit, die am nächsten steht.
        // ⚠ Nicht die erste Tür: eine Fabrik hat zwei, und die falsche kann um
        // das halbe Gebäude herumführen.
        var cells = new List<Vector2I>();
        foreach (var c in CaptureWatchCells(b)) cells.Add(c);
        if (cells.Count == 0) return false;

        int n = 0;
        foreach (int i in _sel)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead || !u.Mobile) continue;
            var best = cells[0];
            float bd = float.MaxValue;
            foreach (var c in cells)
            {
                float d = new Vector2(c.X - u.Col, c.Y - u.Row).LengthSquared();
                if (d < bd) { bd = d; best = c; }
            }
            // ⚠ Ueber den gewoehnlichen Bewegungsbefehl, EINE Einheit je Aufruf:
            // PostMove nimmt sonst die ganze Auswahl und streut sie im Umkreis
            // von acht Zellen — auf einer Tuerzelle ist Streuung genau falsch.
            if (PostMoveOne(i, CellCenter(best.X, best.Y), queue)) n++;
        }
        if (n == 0) { _order = "keine Einheit gewaehlt, die fahren kann"; return true; }
        AddOrderMark(b.Pos, attack: false);
        _order = $"Einnehmen: {n} Einheit(en) faehrt zur Tuer von " +
                 $"{BuildingName(b)} (Besitzer {b.Owner})";
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    /// <summary>»Anhalten« für die Auswahl. ⚠ UNSERE SETZUNG, Nummer 2002 —
    /// das Original hat den Befehl in seiner Liste (0x4FC698, Eintrag 27), aber
    /// welcher Opcode ihn ausführt, ist nicht gelesen.</summary>
    public int PostStop()
    {
        int n = 0;
        foreach (int i in _sel)
            if (Emit(CommandRecord.Make(CommandOp.OursStop, (byte)ViewPlayer, (short)i))) n++;
        return n;
    }

    /// <summary>
    /// <b>»Verkaufen« für EINE Einheit</b> — Kommando 529.
    ///
    /// <para>Der Preis wird <b>hier</b> gerechnet und in den Satz geschrieben,
    /// nicht im Behandler, und das ist die Ordnung des Originals: der Dialog
    /// @0x446470 rechnet <c>3·Wert/10</c> und legt ihn in seinen Merksatz, das
    /// Ja @0x44B138 nimmt ihn von dort als <b>P2</b>. Der Behandler @0x4BFFF0
    /// trägt den Preis nur noch ein.</para>
    ///
    /// <para>⚠ <b>Und das ist mehr als Buchhaltung.</b> Der Wert hängt an der
    /// HÜLLE JETZT (@0x450F94) — er sinkt also, während die Einheit beschossen
    /// wird. Stünde die Rechnung im Behandler, käme im Netzspiel je nach
    /// Ankunftstakt ein anderer Preis heraus, und beim Zusehen sähe alles
    /// richtig aus. Im Satz steht die Zahl fest, sobald der Spieler zugestimmt
    /// hat.</para>
    ///
    /// <para>⚠ <b>Keine Marktprüfung</b>, weil das Original keine hat: Dialog
    /// und Behandler sind ganz gelesen, keiner von beiden fragt nach einem
    /// Gebäude. Wer eine hinzufügte, erfände eine Regel.</para>
    /// </summary>
    /// <returns>Der Preis, zu dem abgesetzt wurde, oder −1 wenn nichts
    /// abgesetzt wurde. ⚠ Der Grund steht dann in <see cref="SellNote"/> —
    /// ein Aufrufer, der nur »ging nicht« erfährt, kann dem Spieler nicht
    /// sagen, welches Glied fehlt.</returns>
    public int PostSell(int idx)
    {
        if (idx < 0 || idx >= _entities.Count)
        { SellNote = "keine Einheit gewaehlt"; return -1; }
        var e = _entities[idx];
        if (e.IsBuilding || e.IsProp)
        { SellNote = "ein Gebaeude laesst sich nicht verkaufen"; return -1; }
        if (e.Dead)
        { SellNote = "die Einheit ist zerstoert"; return -1; }
        if (e.Owner != ViewPlayer)
        { SellNote = "das ist nicht Ihre Einheit"; return -1; }
        foreach (var o in _sellOffers)
            if (o.Unit == idx)
            {
                // ⚠ Das Original tut es TROTZDEM und schreibt nur »Robot
                // already sold.« (0x539050) ins Protokoll — es zahlt dann
                // zweimal. Wir halten hier an, und das ist eine bewusste
                // Abweichung: der Doppelverkauf ist kein Verhalten, das ein
                // Spieler will, sondern eine offene Kasse. Die gelesene
                // Fassung steht in CommandOp.Sell, damit sie nicht verloren ist.
                SellNote = "diese Einheit ist bereits verkauft";
                return -1;
            }

        int price = SellPriceOf(idx);
        if (price < 0)
        { SellNote = $"der Entwurf dieser Einheit (Marke {e.Mark}) ist nicht auffindbar"; return -1; }

        var c = CommandRecord.Make(CommandOp.Sell, (byte)ViewPlayer,
                                   (short)idx, (short)price);
        if (!Emit(c)) { SellNote = "der Befehl liess sich nicht absetzen"; return -1; }
        return price;
    }

    /// <summary>
    /// 529, Verkaufen. P1 = Einheit, P2 = Preis — @0x4BFFF0.
    ///
    /// <para>Kampagne: eintragen mit Zustand <c>0xFF</c> und den Markttick
    /// machen lassen. Gefecht: sofort abrechnen. Beides läuft durch DIESEN
    /// Satz, also über den Ring — im Netzspiel sehen damit beide Maschinen
    /// dasselbe Geschäft im selben Takt.</para>
    /// </summary>
    private bool ApplySell(in CommandRecord c)
    {
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (e.IsBuilding || e.IsProp || e.Dead) return false;
        int price = c.P2;
        if (price < 0) return false;

        if (!TradeLikeOriginal) { SellAtOnce(i, price); return true; }

        // byte[ent+0x14] = 0 — der laufende Auftrag geht weg (@0x4C000B).
        e.Path = null;
        e.Orders.Clear();
        e.Target = -1;
        if (e.Reserved is { } rc) { _nav?.ClearOccupant(rc.X, rc.Y, i); e.Reserved = null; }

        _sellOffers.Add(new SellOffer { Unit = i, Price = price, State = 0xFF });
        SellNote = $"verkauft fuer ${price} — der Abholer kommt";
        return true;
    }

    /// <summary>
    /// <b>»Radar setzen«</b> — Kommando 27. P1 = die Einheit, sonst nichts.
    ///
    /// <para>⚠ Die Zelle steht NICHT im Satz: das Original nimmt sie im
    /// Behandler aus der Einheit selbst (@0x4221C3). Das ist die Ordnung, die
    /// es überall hat — geprüft und gelesen wird im Behandler, nicht beim
    /// Absenden. Ein Satz, der die Zelle mitbrächte, könnte im Netzspiel eine
    /// andere nennen als die, auf der die Einheit steht.</para></summary>
    /// <returns>false, wenn nichts abgesetzt wurde; der Grund steht dann in
    /// <see cref="RadarNote"/>.</returns>
    public bool PostPlaceRadar(int idx)
    {
        if (idx < 0 || idx >= _entities.Count)
        { RadarNote = "keine Einheit gewaehlt"; return false; }
        var e = _entities[idx];
        if (e.IsBuilding || e.IsProp || e.Dead)
        { RadarNote = "das kann keinen Mast setzen"; return false; }
        if (e.Owner != ViewPlayer)
        { RadarNote = "das ist nicht Ihre Einheit"; return false; }
        if (RadarChargesOf(idx) <= 0)
        { RadarNote = "kein Radarstab mehr an Bord"; return false; }
        if (!Emit(CommandRecord.Make(CommandOp.PlaceRadar, (byte)ViewPlayer, (short)idx)))
        { RadarNote = "der Befehl liess sich nicht absetzen"; return false; }
        return true;
    }

    /// <summary>
    /// 27, Radar setzen — @0x422180.
    ///
    /// <para>⚠ Die Reihenfolge des Originals: <b>erst den Vorrat prüfen und
    /// abziehen, dann setzen</b>. Schlägt das Setzen fehl (Tafel voll, Zelle
    /// belegt), ist der Mast trotzdem verbraucht — das steht so da und wird so
    /// nachgebaut. Der Prüfstand weist die Ablehnungen deshalb getrennt
    /// aus.</para></summary>
    private bool ApplyPlaceRadar(in CommandRecord c)
    {
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (e.IsBuilding || e.IsProp || e.Dead) return false;
        if (RadarChargesOf(i) <= 0) return false;

        e.RadarCharges--;                                  // @0x4221AF
        int owner = e.Owner is >= 0 and <= 7 ? e.Owner : 0;
        if (!PlaceRadarMast(e.Col, e.Row, owner))
        {
            RadarMastsRefused++;
            if (RadarNote.Length == 0) RadarNote = "die Zelle nimmt keinen Mast";
            return true;                                   // verbraucht ist er trotzdem
        }
        RadarNote = $"Radarmast auf ({e.Col},{e.Row}) — noch {e.RadarCharges} an Bord";
        UpdateFog();
        QueueRedraw();
        return true;
    }

    /// <summary>Einen fertigen Satz absetzen — für Prüfstand, Wiederholung und
    /// später den Netzempfang. Der Weg, auf dem ein Befehl von aussen
    /// hereinkommt, ist derselbe wie der von innen; nur so ist gesagt, dass
    /// beide dasselbe bewirken.</summary>
    public bool PostRaw(CommandRecord c) => Commands.Post(c);

    // ================= DER BEHANDLER =========================================

    /// <summary>
    /// Aus einem Satz Zustand machen — <b>die einzige Stelle</b>, die das darf,
    /// und sie läuft auf jeder Maschine am selben Taktanfang mit demselben Satz.
    ///
    /// <para>Ein unbekannter Opcode wird still verworfen, wie im Original: der
    /// Verteiler springt für jeden Wert ausserhalb der drei Bereiche auf
    /// dieselbe Fehlermarke (@0x4C4847), und die schaltet nur weiter.</para>
    /// </summary>
    public bool ApplyCommand(in CommandRecord c) => c.Op switch
    {
        // ⚠ VOR der Owns-Schranke: bei diesem Befehl ist P1 ein
        // FLUGZEUG-Steckplatz, keine Einheitennummer. Owns() würde
        // _entities[P1] befragen — eine ganz andere Einheit.
        CommandOp.OursAirMove => ApplyAirMove(c),
        _ when !Owns(c) => false,
        CommandOp.Move => ApplyMove(c),
        CommandOp.Attack => ApplyAttack(c),
        CommandOp.OursAttack => ApplyAttack(c),   // ⚠ nur noch fuer alte Staende
        CommandOp.OursStop => ApplyStop(c),
        CommandOp.Sell => ApplySell(c),
        CommandOp.PlaceRadar => ApplyPlaceRadar(c),
        CommandOp.PlaceBuilding => ApplyPlaceBuilding(c),
        CommandOp.PlaceGenerator => ApplyPlaceBuilding(c),
        CommandOp.Unload => ApplyUnload(c),

        // Die fünfzehn Gebäudebefehle — vier Tafeln, eine je Gebäudeart.
        // Siehe CommandOp und ApplyBuildingJob.
        CommandOp.FactoryExpandStore or CommandOp.MineExpandStore
            => ApplyBuildingJob(c, BuildingJob.ExpandStore),
        CommandOp.FactoryExpandProd or CommandOp.MineExpandProd
            => ApplyBuildingJob(c, BuildingJob.ExpandProd),
        CommandOp.FactoryRepair or CommandOp.MineRepair
            or CommandOp.AirportHalt or CommandOp.BaseRepair
            => ApplyBuildingJob(c, BuildingJob.Repair),
        CommandOp.FactoryIdle or CommandOp.MineIdle
            or CommandOp.AirportIdle or CommandOp.BaseIdle
            => ApplyBuildingJob(c, BuildingJob.Idle),

        _ => false,
    };

    /// <summary>
    /// <c>--entladen-check</c> — <b>kommt die Ladung der Karte wirklich
    /// heraus?</b>
    ///
    /// <para>Bis zum 21.08.2026 war das unmöglich: die Ladung wurde beim Laden
    /// mit <c>continue</c> übersprungen. Geprüft wird darum von unten nach
    /// oben:</para>
    /// <list type="number">
    /// <item>Die Karte hat Ladung, und sie ist als Einheit da (nicht nur als
    /// Platznummer).</item>
    /// <item>Die Rampe rechnet eine Zielzelle aus.</item>
    /// <item>Der Satz wirkt <b>erst im nächsten Takt</b> und stellt die Ladung
    /// dann auf die Karte.</item>
    /// <item>⚠ Gegenprobe: derselbe Satz mit einer FREMDEN Zielzelle muss
    /// verworfen werden — sonst wäre ein Satz aus dem Netz ein Weg, Ladung
    /// irgendwohin zu stellen.</item>
    /// </list></summary>
    public string EntladenCheck()
    {
        var sb = new System.Text.StringBuilder("entladen-check\n");
        bool alles = true;

        int Leerlaufen()
        {
            for (int t = 0; t < 8; t++)
            {
                if (Commands.Pending == 0) return t;
                CommandTick();
            }
            return 8;
        }

        // Einen beladenen Traeger suchen.
        int traeger = -1, anBord = 0, tot = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            int n = FrachtAnBord(_entities[i].Slot).Count;
            if (n == 0) continue;
            if (_entities[i].Dead) { tot++; continue; }
            traeger = i; anBord = n; break;
        }
        // ⚠ ERKLAERT, damit niemand daran haengenbleibt: mit --skirmish duennt
        // SkirmishAi die Karte aus (es behaelt nur wenige Einheiten je Spieler
        // und setzt den Rest auf tot). Auf map_05 traf das GENAU die vier
        // beladenen Frachter, und der Prueflauf sah dann ein Gebaeude mit
        // demselben Platz fuer einen Traeger an. Ueber --campaign=N steht die
        // Karte, wie sie ist. Das ist keine Eigenheit dieses Pruefstands,
        // sondern der zweiten Falle aus der Arbeitsweise in neuem Gewand.
        if (tot > 0)
            sb.Append($"  ⚠ {tot} beladene Traeger sind als ZERSTOERT geladen — uebergangen. ")
              .Append("Mit --skirmish duennt SkirmishAi die Karte aus; ")
              .Append("fuer diesen Prueflauf --campaign=N nehmen.\n");
        if (traeger < 0)
            return sb.Append($"  ⚠ ABBRUCH: kein beladener Traeger auf dieser Karte ")
                     .Append($"(Rampenzellen: {RampenZellen}).\n  DURCHGEFALLEN").ToString();

        var t0 = _entities[traeger];
        sb.Append($"  Traeger Platz {t0.Slot} auf ({t0.Col},{t0.Row}): {anBord} Stueck an Bord ")
          .Append("— als Einheit gebaut, nicht nur als Platznummer ✔\n");

        // Eine Rampenzelle mit gueltiger Kachel suchen.
        int rc = -1, rr = -1;
        Vector2I? ziel = null;
        for (int c = 0; c < 400 && ziel == null; c++)
            for (int r = 0; r < 400; r++)
            {
                var z = RampenAbsetzZelle(c, r);
                if (z == null) continue;
                rc = c; rr = r; ziel = z; break;
            }
        if (ziel == null)
            return sb.Append($"  ⚠ ABBRUCH: keine Rampenzelle mit einer der acht Kacheln ")
                     .Append($"{MapObjectsRampBasis()}..{MapObjectsRampBasis() + 7} ")
                     .Append($"(Rampenzellen insgesamt: {RampenZellen}).\n  DURCHGEFALLEN").ToString();
        sb.Append($"  Rampe ({rc},{rr}) setzt ab auf ({ziel.Value.X},{ziel.Value.Y})\n");

        // --- absetzen ------------------------------------------------------
        ViewPlayer = t0.Owner;
        int vorher = _entities.Count, fertigVor = Unloaded;
        int gemeldet = PostUnload(traeger, rc, rr);
        bool sofortNichts = _entities.Count == vorher;
        sb.Append($"  abgesetzt: {gemeldet} gemeldet" + (gemeldet < 0 ? $" [{UnloadNote}]" : "") +
                  "; beim Absenden noch nichts auf der Karte: ")
          .Append(sofortNichts ? "ja ✔" : "NEIN ✘").Append('\n');
        alles &= gemeldet == anBord && sofortNichts;

        // ⚠ SimTick, nicht CommandTick: abgesetzt wird EINES je Takt, und der
        // Zaehler laeuft im Takt der Simulation. Ein Prueflauf, der nur den
        // Befehlsring leerlaufen laesst, sieht genau eine Einheit.
        int takte = 0;
        while (takte < 400 && FrachtAnBord(t0.Slot).Count > 0) { SimTick(SimDt); takte++; }
        int neu = _entities.Count - vorher;
        bool draussen = neu == anBord && Unloaded == fertigVor + anBord;
        sb.Append($"  nach {takte} Takten: {neu} Einheiten mehr auf der Karte ")
          .Append($"(erwartet {anBord}), Ladung an Bord jetzt {FrachtAnBord(t0.Slot).Count} ")
          .Append(draussen ? "✔" : "✘").Append('\n');
        alles &= draussen;

        // --- Gegenprobe: fremde Zielzelle ----------------------------------
        int vor2 = _entities.Count;
        PostRaw(CommandRecord.Make(CommandOp.Unload, (byte)ViewPlayer, (short)traeger,
                                   (short)(ziel.Value.X + 5), (short)ziel.Value.Y,
                                   (short)(rc * 256 + rr), 0));
        Leerlaufen();
        bool verworfen = _entities.Count == vor2;
        sb.Append("  Gegenprobe — Satz mit fremder Zielzelle: ")
          .Append(verworfen ? "verworfen ✔" : "AUSGEFUEHRT ✘").Append('\n');
        alles &= verworfen;

        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    private static int MapObjectsRampBasis() => RampenKachelBasis;

    /// <summary>
    /// <c>--beladen-check</c> — <b>die zwei Schranken des Originals, an
    /// echten Einheiten.</b>
    ///
    /// <para>Gemessen wird genau das, was gelesen ist, und beides in seiner
    /// eigenen Zahl:</para>
    /// <list type="number">
    /// <item>Ein <b>Fahrzeug</b> steigt auf, solange das Gewicht ≤ 10 ist —
    /// also <b>drei</b>, und das vierte wird abgewiesen.</item>
    /// <item>Ein <b>Infanterist</b>, solange es ≤ 14 ist — also
    /// <b>fünfzehn</b>, und der sechzehnte wird abgewiesen.</item>
    /// <item>⚠ Ein <b>Schiff</b> wird mit dem Wortlaut des Originals
    /// abgewiesen. Ohne diesen Punkt bestünde auch eine Fassung, die alles
    /// aufnimmt.</item>
    /// <item>⚠ Und eine Einheit, die <b>nicht auf einer Ladezelle</b> steht,
    /// darf gar nicht erst einsteigen.</item>
    /// </list></summary>
    public string BeladenCheck()
    {
        var sb = new System.Text.StringBuilder("beladen-check\n");
        bool alles = true;

        // Ein Traeger mit Transportsatz.
        int traeger = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var q = _entities[i];
            if (q.Dead || q.IsBuilding || q.IsProp) continue;
            if (!_bordDeckel.ContainsKey(q.Slot) && FrachtAnBord(q.Slot).Count == 0) continue;
            traeger = i; break;
        }
        if (traeger < 0)
            return sb.Append("  ⚠ ABBRUCH: kein Traeger mit Transportsatz auf dieser Karte.\n")
                     .Append("  DURCHGEFALLEN").ToString();

        var t0 = _entities[traeger];
        // Fuer die Messung faehrt er leer.
        FrachtLeeren(t0.Slot);
        sb.Append($"  Traeger Platz {t0.Slot} auf ({t0.Col},{t0.Row}), leergefahren; ")
          .Append($"Gewicht {BordGewicht(t0.Slot)}\n");

        // Irgendeine Ladezelle der Karte.
        int lc = -1, lr = -1;
        for (int c = 0; c < 400 && lc < 0; c++)
            for (int r = 0; r < 400; r++)
                if (RampeBeladen(c, r)) { lc = c; lr = r; break; }
        if (lc < 0)
            return sb.Append($"  ⚠ ABBRUCH: keine Ladezelle (Lage >= 100) auf dieser Karte ")
                     .Append($"(Rampenzellen insgesamt: {RampenZellen}).\n")
                     .Append("  DURCHGEFALLEN").ToString();

        // ⚠ EINGRIFF, und er steht in der Ausgabe: der Traeger wird an die
        // Ladestelle gestellt. Gemessen werden sollen die zwei GEWICHTS-
        // schranken, nicht die Frage, wo dieser Frachter zufaellig liegt.
        int altC = t0.Col, altR = t0.Row;
        t0.Col = lc; t0.Row = lr + 1;
        sb.Append($"  Ladezelle ({lc},{lr}); ⚠ EINGRIFF: Traeger von ({altC},{altR}) ")
          .Append($"nach ({t0.Col},{t0.Row}) gestellt — gemessen werden die Schranken\n");

        // Ein Probestueck bauen und immer wieder hinstellen.
        int Probe(int gattung)
        {
            var u = new Entity
            {
                Slot = 9000 + _entities.Count, Col = lc, Row = lr,
                Owner = t0.Owner, Team = t0.Owner,
                UnitType = 1, GameUnitType = gattung, Infantry = gattung == 1 ? 1 : -1,
                Hp = 100, HpMax = 100, Mobile = true,
                Footprint = CellRect(_ox, _oy, lc, lr, ElevOf(lc, lr)),
            };
            _entities.Add(u);
            return _entities.Count - 1;
        }

        // --- 1. Fahrzeuge: drei gehen, das vierte nicht ---------------------
        int drin = 0;
        for (int k = 0; k < 5; k++)
            if (BeladeVersuch(Probe(0)) >= 0) drin++;
        bool fzOk = drin == 3;
        sb.Append($"  Fahrzeuge (Gewicht 5, Schranke 10): {drin} von 5 Versuchen an Bord ")
          .Append($"(erwartet 3), Gewicht jetzt {BordGewicht(t0.Slot)} ")
          .Append(fzOk ? "✔" : "✘").Append('\n');
        alles &= fzOk;

        // --- 2. Infanterie: fuenfzehn ---------------------------------------
        FrachtLeeren(t0.Slot);
        drin = 0;
        for (int k = 0; k < 17; k++)
            if (BeladeVersuch(Probe(1)) >= 0) drin++;
        bool infOk = drin == 15;
        sb.Append($"  Infanterie (Gewicht 1, Schranke 14): {drin} von 17 Versuchen an Bord ")
          .Append($"(erwartet 15), Gewicht jetzt {BordGewicht(t0.Slot)} ")
          .Append(infOk ? "✔" : "✘").Append('\n');
        alles &= infOk;

        // --- 3. ein Schiff wird abgewiesen ----------------------------------
        FrachtLeeren(t0.Slot);
        int schiff = Probe(4);
        bool weg = BeladeVersuch(schiff) < 0;
        sb.Append($"  ein Schiff (Gattung 4): {(weg ? "abgewiesen ✔" : "AUFGENOMMEN ✘")} ")
          .Append($"[{_order}]\n");
        alles &= weg;

        // --- 4. abseits der Ladezelle geht gar nichts -----------------------
        int abseits = Probe(1);
        _entities[abseits].Col = lc + 20;
        _entities[abseits].Row = lr + 20;
        bool nein = BeladeVersuch(abseits) < 0;
        sb.Append($"  20 Zellen abseits: {(nein ? "abgewiesen ✔" : "AUFGENOMMEN ✘")} ")
          .Append($"[{_order}]\n");
        alles &= nein;

        // --- 5. und der TAKT loest es von selbst aus ------------------------
        //
        // ⚠ Ohne diesen Punkt waere BeladeVersuch eine Methode, die niemand
        // ruft — ein Auftrag, den es im Spiel gar nicht gibt.
        FrachtLeeren(t0.Slot);
        // ⚠ ERST AUFRAEUMEN. Die abgewiesenen Probestuecke der Punkte 1 bis 4
        // stehen noch auf der Ladezelle — im ersten Anlauf stiegen sie hier
        // mit ein und der Prueflauf mass seinen eigenen Abfall (»4
        // eingestiegen« statt einem).
        foreach (var q in _entities)
            if (!q.IsBuilding && q.Col == lc && q.Row == lr) { q.Col = 1; q.Row = 1; }
        int probe = Probe(1);
        _entities[probe].Col = lc; _entities[probe].Row = lr;
        int vorTakt = FrachtAnBord(t0.Slot).Count;
        for (int k = 0; k < 4; k++) SimTick(SimDt);
        bool vonSelbst = FrachtAnBord(t0.Slot).Count == vorTakt + 1;
        sb.Append($"  im Takt, ohne Zutun: {FrachtAnBord(t0.Slot).Count - vorTakt} eingestiegen ")
          .Append(vonSelbst ? "✔" : "✘ (niemand loest das Einsteigen aus)").Append('\n');
        alles &= vonSelbst;

        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    // ==== ABSETZEN — Befehl 18 ==============================================

    /// <summary>
    /// <b>Die Ladung eines Trägers auf einer Rampe absetzen</b> — den Satz auf
    /// den Weg bringen.
    ///
    /// <para>Gerechnet wird hier nur die Zielzelle (die Rampenprüfung des
    /// Originals @0x4CF100 tut dasselbe im Absender); GEPRÜFT wird im
    /// Behandler, denn was vom Absender kommt, ist keine Aussage über die
    /// Wahrheit.</para></summary>
    /// <returns>−1 wenn nichts abgesetzt wurde; der Grund steht dann in
    /// <see cref="UnloadNote"/>.</returns>
    public int PostUnload(int carrier, int rampCol, int rampRow)
    {
        UnloadNote = "";
        if (carrier < 0 || carrier >= _entities.Count)
        { UnloadNote = "kein Traeger gewaehlt"; return -1; }
        var e = _entities[carrier];
        if (e.Dead) { UnloadNote = "der Traeger ist zerstoert"; return -1; }
        var ladung = FrachtAnBord(e.Slot);
        if (ladung.Count == 0) { UnloadNote = "der Traeger ist leer"; return -1; }

        var ziel = RampenAbsetzZelle(rampCol, rampRow);
        if (ziel == null)
        {
            // Der Wortlaut des Originals, 0x4FAB24.
            UnloadNote = "Very unique error before unloading units";
            return -1;
        }
        short gepackt = (short)(rampCol * 256 + rampRow);
        if (!Emit(CommandRecord.Make(CommandOp.Unload, (byte)ViewPlayer, (short)carrier,
                                     (short)ziel.Value.X, (short)ziel.Value.Y,
                                     gepackt, (short)(ladung.Count - 1))))
        { UnloadNote = "der Befehl liess sich nicht absetzen"; return -1; }
        return ladung.Count;
    }

    /// <summary>Warum das Absetzen nicht ging — ⚠ ein Aufrufer, der nur »ging
    /// nicht« erfährt, kann dem Spieler nicht sagen, welches Glied fehlt.
    /// </summary>
    public string UnloadNote = "";

    /// <summary>Wieviele Einheiten insgesamt abgesetzt wurden.</summary>
    public int Unloaded;

    /// <summary>
    /// Aus einem Absetz-Satz Zustand machen: die Ladung steht auf der Karte.
    ///
    /// <para>⚠ <b>Der Behandler rechnet die Zielzelle NEU</b>, statt P2/P3 zu
    /// glauben. Das Original prüft an dieser Stelle, dass der Träger wirklich
    /// dort steht, wo der Absender ihn wähnte (@0x4C30E7 ff); bei uns käme über
    /// das Netz sonst eine beliebige Zelle herein, und die Ladung stünde
    /// irgendwo. Weicht die gerechnete von der gesendeten ab, wird der Satz
    /// verworfen — nicht stillschweigend zurechtgebogen.</para>
    ///
    /// <para>⚠ <b>Was UNSER ist:</b> die Ladung kommt auf die EINE gerechnete
    /// Zelle, und wenn dort schon jemand steht, wird eine freie Nachbarzelle
    /// genommen. Das Original hat dafür eigene Zweige (»Wrong square to unload
    /// infantry« gegen »… robot«, 0x4CF240) — die sind gelesen als vorhanden,
    /// aber nicht in ihrer Regel. Hier steht darum die einfachste Fassung, und
    /// sie steht als unsere da.</para></summary>
    private bool ApplyUnload(in CommandRecord c)
    {
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (e.Dead) return false;
        if (e.Owner != c.Player) return false;

        int rampCol = (c.P4 >> 8) & 0xFF, rampRow = c.P4 & 0xFF;
        var ziel = RampenAbsetzZelle(rampCol, rampRow);
        if (ziel == null) return false;
        if (ziel.Value.X != c.P2 || ziel.Value.Y != c.P3) return false;   // s.o.

        var ladung = FrachtAnBord(e.Slot);
        if (ladung.Count == 0) return false;

        // ⚠ NICHT auf einmal: der Satz traegt eine Stueckzahl, und die braucht
        // nur, wer nacheinander absetzt. Siehe FrachtAbsetzenTakt.
        e.UnloadCell = ziel.Value;
        e.UnloadRest = ladung.Count;
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    // ==== die fünfzehn GEBÄUDEBEFEHLE ========================================

    /// <summary>
    /// Welche Befehlsnummer dieses Gebäude für diesen Auftrag trägt, oder 0.
    ///
    /// <para>Das Original hat je Gebäudeart eine eigene Tafel und darum je Art
    /// eigene Nummern — dieselbe Handlung heisst bei der Fabrik 509 und bei der
    /// Mine 515. Diese Zeile ist die einzige Stelle, die das weiss.</para>
    ///
    /// <para>⚠ Die Basis kennt <b>keinen</b> Ausbaubefehl: in ihre Tafel
    /// (0x878E58) schreibt kein einziger Befehl eine 2 oder 3, obwohl ihr
    /// Fenster »vergrössern« und »forschen« anzeigt. Das ist gelesen, nicht
    /// vergessen — wer dort eine Nummer einträgt, erfindet sie.</para></summary>
    public static short BuildingOpFor(Entity e, BuildingJob job)
    {
        bool fabrik = e.BType is 2 or 3 or 4;
        bool mine = e.BType is 10 or 15;
        bool flug = e.BType == 5;
        return job switch
        {
            BuildingJob.ExpandStore => fabrik ? CommandOp.FactoryExpandStore
                                     : mine ? CommandOp.MineExpandStore : (short)0,
            BuildingJob.ExpandProd => fabrik ? CommandOp.FactoryExpandProd
                                    : mine ? CommandOp.MineExpandProd : (short)0,
            BuildingJob.Repair => fabrik ? CommandOp.FactoryRepair
                                : mine ? CommandOp.MineRepair
                                : flug ? CommandOp.AirportHalt : CommandOp.BaseRepair,
            BuildingJob.Idle => fabrik ? CommandOp.FactoryIdle
                              : mine ? CommandOp.MineIdle
                              : flug ? CommandOp.AirportIdle : CommandOp.BaseIdle,
            _ => 0,
        };
    }

    /// <summary>
    /// Einen Gebäudeauftrag <b>absetzen</b> — je gewähltem Gebäude einen Satz,
    /// wie das Original (sein P1 ist die laufende Nummer INNERHALB der Art,
    /// also nennt auch dort jeder Satz genau ein Gebäude).
    /// </summary>
    /// <returns>wie viele Sätze auf den Weg gingen.</returns>
    public int PostBuildingJob(BuildingJob job)
    {
        int n = 0;
        foreach (int i in new List<int>(Selection))
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead) continue;
            short op = BuildingOpFor(e, job);
            if (op == 0) continue;
            if (Emit(CommandRecord.Make(op, (byte)ViewPlayer, (short)i))) n++;
        }
        return n;
    }

    /// <summary>
    /// <c>--gebaeude-check</c> — <b>gehen die Gebäudebefehle wirklich durch den
    /// Ring, und weist der Behandler die falsche Tafel ab?</b>
    ///
    /// <para>Geprüft wird, was diesem Umbau eigen ist und was ein
    /// Zustandsvergleich allein nicht sieht:</para>
    /// <list type="number">
    /// <item>Ein abgesetzter Auftrag wirkt <b>erst im nächsten Takt</b> — beim
    /// Absenden darf sich nichts ändern. Ein Behandler, der schon beim Post
    /// wirkt, wäre der alte Direktweg mit neuem Namen.</item>
    /// <item>Danach steht der <b>Zustand der Gebäudeart</b> da, nicht
    /// irgendeine 2.</item>
    /// <item>Der Preis ist <b>genau einmal</b> abgebucht.</item>
    /// <item>⚠ Die <b>Gegenprobe</b>: derselbe Satz mit der Nummer einer
    /// FREMDEN Gebäudeart muss abgewiesen werden. Ohne diese Zeile wäre ein
    /// Satz aus dem Netz ein Weg, in eine fremde Tafel zu schreiben.</item>
    /// </list></summary>
    public string GebaeudeCheck()
    {
        var sb = new System.Text.StringBuilder("gebaeude-check\n");
        bool alles = true;

        // Den Ring leerlaufen lassen und sagen, wie viele Taktanfänge es
        // brauchte. ⚠ EINER reicht NICHT: ein Satz wird auf `Tick + 1 + Lead`
        // fällig (CommandRing.Post), und genau daran ist dieser Prüfstand beim
        // ersten Anlauf gescheitert — er las den Zustand, bevor der Satz dran
        // war, und schrieb den Fehlschlag dem Behandler zu.
        int Leerlaufen()
        {
            for (int t = 0; t < 8; t++)
            {
                if (Commands.Pending == 0) return t;
                CommandTick();
            }
            return 8;
        }

        int fabrik = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var q = _entities[i];
            if (!q.IsBuilding || q.Dead || q.State != MapEntityLayer.StAktiv) continue;
            if (q.BType is 2 or 3 or 4) { fabrik = i; break; }
        }
        if (fabrik < 0)
        {
            // ⚠ Ein Prüfstand ohne Gegenstand meldet keinen Erfolg — und er
            // sagt, WAS er statt dessen gefunden hat. Ein blosses »kein
            // Gegenstand« schickt den nächsten auf dieselbe Suche.
            var arten = new System.Collections.Generic.SortedDictionary<int, int>();
            int geb = 0;
            foreach (var q in _entities)
            {
                if (!q.IsBuilding || q.Dead) continue;
                geb++;
                arten.TryGetValue(q.BType, out int k); arten[q.BType] = k + 1;
            }
            sb.Append("  ⚠ ABBRUCH: keine Fabrik (Art 2,3,4) im Zustand »aktiv«. ")
              .Append($"Auf der Karte: {geb} Gebaeude, Arten");
            foreach (var kv in arten) sb.Append($" {kv.Key}x{kv.Value}");
            sb.Append($"; Sichtspieler {ViewPlayer}\n  DURCHGEFALLEN");
            return sb.ToString();
        }

        var f = _entities[fabrik];
        int owner = Mathf.Clamp(f.Owner, 0, 7);
        ViewPlayer = f.Owner;
        _money[owner] = 999999;
        int kasse = _money[owner], preis = f.CostStore;
        int erwartet = MapEntityLayer.JobState(f, BuildingJob.ExpandStore);

        // --- 1. absetzen wirkt NICHT sofort ---------------------------------
        _sel.Clear(); _sel.Add(fabrik);
        int n = PostBuildingJob(BuildingJob.ExpandStore);
        bool nochAktiv = f.State == MapEntityLayer.StAktiv && _money[owner] == kasse;
        sb.Append($"  Fabrik {fabrik} (Art {f.BType}, Spieler {f.Owner}): {n} Satz abgesetzt; ")
          .Append("beim Absenden noch unveraendert: ")
          .Append(nochAktiv ? "ja ✔" : "NEIN ✘ — der Ring wird umgangen").Append('\n');
        alles &= n == 1 && nochAktiv;

        // --- 2. nach dem Taktanfang steht der Zustand der ART da -------------
        int takte = Leerlaufen();
        bool zustandOk = f.State == erwartet;
        bool geldOk = _money[owner] == kasse - preis;
        sb.Append($"  nach {takte} Taktanfaengen: Zustand {f.State} (erwartet {erwartet}) ")
          .Append(zustandOk ? "✔" : "✘")
          .Append($", Konto {kasse} − {preis} = {_money[owner]} ")
          .Append(geldOk ? "✔" : "✘").Append('\n');
        alles &= zustandOk && geldOk;

        // --- 3. Gegenprobe: die Nummer einer FREMDEN Gebaeudeart -------------
        f.State = MapEntityLayer.StAktiv;
        kasse = _money[owner];
        PostRaw(CommandRecord.Make(CommandOp.MineExpandStore, (byte)f.Owner, (short)fabrik));
        Leerlaufen();
        bool abgewiesen = f.State == MapEntityLayer.StAktiv && _money[owner] == kasse;
        sb.Append("  Gegenprobe — Minenbefehl 515 auf eine Fabrik: ")
          .Append(abgewiesen ? "abgewiesen ✔" : "AUSGEFUEHRT ✘ (fremde Tafel beschreibbar)")
          .Append('\n');
        alles &= abgewiesen;

        // --- 4. und die richtige Nummer derselben Art geht durch -------------
        PostRaw(CommandRecord.Make(CommandOp.FactoryExpandStore, (byte)f.Owner, (short)fabrik));
        Leerlaufen();
        bool durch = f.State == erwartet && _money[owner] == kasse - preis;
        sb.Append($"  dieselbe Handlung mit 509: Zustand {f.State}, ")
          .Append($"Konto {_money[owner]} ").Append(durch ? "✔" : "✘").Append('\n');
        alles &= durch;

        // --- 5. die Nummernwahl je Art --------------------------------------
        sb.Append("  Nummern je Art (reparieren/aktiv):");
        var proben = new[] { ("Fabrik", 2, 519, 511), ("Mine", 10, 522, 517),
                             ("Flughafen", 5, 520, 524), ("Basis", 1, 521, 525) };
        foreach (var (name, bt, wantR, wantI) in proben)
        {
            var probe = new Entity { IsBuilding = true, BType = bt };
            short r = BuildingOpFor(probe, BuildingJob.Repair);
            short id = BuildingOpFor(probe, BuildingJob.Idle);
            bool ok = r == wantR && id == wantI;
            alles &= ok;
            sb.Append($" {name} {r}/{id}{(ok ? "" : $" ✘ (erwartet {wantR}/{wantI})")}");
        }
        // ⚠ Die Basis kennt keinen Ausbaubefehl — kein Befehl schreibt in ihre
        // Tafel eine 2 oder 3. Wer dort eine Nummer einträgt, erfindet sie.
        var basis = new Entity { IsBuilding = true, BType = 1 };
        bool basisOhne = BuildingOpFor(basis, BuildingJob.ExpandStore) == 0;
        sb.Append($"; Basis ohne Ausbaubefehl: {(basisOhne ? "ja ✔" : "NEIN ✘")}\n");
        alles &= basisOhne;

        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Die drei Fensterknöpfe, jetzt über den Ring. <b>Das ist der
    /// Umbau</b>: bis zum 21.08.2026 riefen sie <c>StartRepair</c>,
    /// <c>StartUpgrade</c> und <c>StopRepairFromPanel</c> unmittelbar, also am
    /// Befehlsbus vorbei — im Netzspiel hätte nur die klickende Maschine den
    /// Ausbau gesehen, und zwei Maschinen wären mit verschiedenem Kontostand
    /// weitergelaufen.
    ///
    /// <para><c>AimAtPanelBuilding</c> steht davor, weil das Fenster immer das
    /// Gebäude meint, das es zeigt — dieselbe Zeile, die der Direktweg
    /// hatte.</para></summary>
    public int PostRepairFromPanel()
    { AimAtPanelBuilding(); return PostBuildingJob(BuildingJob.Repair); }

    public int PostStopRepairFromPanel()
    { AimAtPanelBuilding(); return PostBuildingJob(BuildingJob.Idle); }

    public int PostUpgradeFromPanel(bool storage)
    { AimAtPanelBuilding(); return PostBuildingJob(storage ? BuildingJob.ExpandStore
                                                          : BuildingJob.ExpandProd); }

    /// <summary>
    /// Aus einem Gebäudesatz Zustand machen.
    ///
    /// <para>⚠ <b>Die Nummer muss zur Gebäudeart passen.</b> Ein Satz mit 509
    /// (Fabriktafel), der auf eine Mine zeigt, wird verworfen — im Original
    /// KANN das nicht vorkommen, weil 509 in die Fabriktafel schreibt und dort
    /// gar keine Mine steht. Bei uns zeigt P1 auf die gemeinsame
    /// Einheitenliste, also muss diese Zeile die Trennung nachholen. Ohne sie
    /// wäre ein Satz aus dem Netz ein Weg, fremde Tafeln zu beschreiben.</para>
    /// </summary>
    private bool ApplyBuildingJob(in CommandRecord c, BuildingJob job)
    {
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (!e.IsBuilding || e.Dead) return false;
        if (BuildingOpFor(e, job) != c.Op) return false;      // s.o.
        bool ok = GiveBuildingJob(i, job);
        if (ok) { UpdatePanel(); QueueRedraw(); }
        return ok;
    }

    /// <summary>
    /// GEHÖRT DIE EINHEIT DEM, DER DEN BEFEHL GEGEBEN HAT?
    ///
    /// <para>⚠ Die Prüfung steht im <b>Behandler</b>, nicht beim Absenden — das
    /// ist die Regel, die das Original vorgibt (Opcode 3 klemmt P2/P3 erst
    /// @0x4C2324, also im Behandler). Der Grund ist im Netzspiel handfest: der
    /// Absender läuft auf der Maschine des Klickenden, und was von dort kommt,
    /// ist keine Aussage über die Wahrheit. Ohne diese Zeile könnte ein
    /// Teilnehmer die Einheiten seines Gegners fahren, und zwar völlig
    /// synchron — beide Maschinen würden es gehorsam gleich rechnen.</para>
    ///
    /// <para>Für den Einzelspieler ändert sich nichts: <c>_sel</c> nimmt nur
    /// eigene Einheiten auf (<c>Commandable</c> prüft <c>Owner == ViewPlayer</c>),
    /// also war <c>Owner == Player</c> dort ohnehin immer erfüllt. Genau
    /// deshalb bleibt <c>--befehl-check</c> (A gegen den Direktweg B) grün — und
    /// wäre es das nicht, hätte die Zeile eine Lücke aufgedeckt statt eine
    /// geschlossen.</para>
    ///
    /// <para>⚠ <b>UNSERE SETZUNG</b> insofern, als das Original für den Absender
    /// gar kein Satzfeld hat (es nimmt ihn aus dem Transport, <c>Receive</c> gibt
    /// <c>idFrom</c> @0x404490). Wir führen ihn auf +0x03 mit; ob das Original
    /// die Eigentümerfrage im Behandler stellt, ist NICHT gelesen.</para>
    /// </summary>
    private bool Owns(in CommandRecord c)
    {
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        return _entities[i].Owner == c.Player;
    }

    /// <summary>
    /// <b>Flugziel, unsere Nummer 2003.</b> P1 = Steckplatz, P2/P3 = Zielzelle.
    ///
    /// <para>Getan wird genau das, was <c>air_back_to_airport</c> @0x42646D tut:
    /// Zielspalte und Zielzeile in den Satz, Auftrag auf 1. Bei uns heisst das
    /// <c>Goal</c> und <c>Order = 1</c>.</para>
    ///
    /// <para>⚠⚠ <b>NUR AUSSERHALB DER KAMPAGNE.</b> Nach der Trennachse des
    /// Projekts (<c>CampaignMission &gt; 0</c>) bleibt die Kampagne
    /// originaltreu; das Gefecht darf bewusst abweichen. Ein Flugbefehl in
    /// Mission 7 wäre eine stille Änderung am Original — hier wird er
    /// verworfen, und der Ring bleibt trotzdem für beide Seiten gleich, weil
    /// die Verwerfung aus dem MISSIONSSTAND folgt und nicht aus dem Zufall
    /// einer Maschine.</para>
    ///
    /// <para>⚠ Der Eigentümer wird auch hier im BEHANDLER geprüft, aus demselben
    /// Grund wie bei <see cref="Owns"/>: was vom Absender kommt, ist keine
    /// Aussage über die Wahrheit.</para></summary>
    private bool ApplyAirMove(in CommandRecord c)
    {
        if (_nav == null) return false;
        if (UI.SkirmishSetup.CampaignMission > 0) return false;   // s.o.

        Special? a = null;
        foreach (var s in _special) if (s.Slot == c.P1) { a = s; break; }
        if (a == null || a.Dead || a.Stored) return false;
        if (a.Owner != c.Player) return false;

        // geklemmt wie bei Opcode 3 (@0x4C2324): ein Satz aus dem Netz oder
        // einer Wiederholung darf nichts umwerfen.
        int col = Mathf.Clamp(c.P2, 0, _nav.Width - 1);
        int row = Mathf.Clamp(c.P3, 0, _nav.Height - 1);

        a.PlayerGoal = CellCenter(col, row);
        a.Goal = a.PlayerGoal;
        a.Order = 1;                     // gelesen: 1 = »flieg nach (x,y)«
        a.Customer = -1;                 // ein Versorgungsheli lässt seine Kundschaft
        a.HomePoint = null;              // und den gewürfelten Heimatpunkt
        AirOrdersGiven++;
        return true;
    }

    /// <summary>Wie viele Flugbefehle angekommen sind — für den Prüfstand.</summary>
    public int AirOrdersGiven { get; private set; }

    /// <summary>
    /// Opcode 3, Bewegen. P1 = Einheit, P2/P3 = Zielzelle, P4 = angereiht.
    ///
    /// <para><b>Zuerst geklemmt, wie im Original.</b> @0x4C2324 klemmt P2 und
    /// P3 auf die Karte, bevor irgendetwas passiert — im BEHANDLER, nicht beim
    /// Absenden. Wir tun dasselbe mit unseren Kartengrenzen. Ein Satz aus einer
    /// Wiederholung, aus dem Netz oder von einer anderen Kartengrösse kann
    /// damit nichts umwerfen.</para>
    /// </summary>
    private bool ApplyMove(in CommandRecord c)
    {
        if (_nav == null) return false;
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;      // P1 &lt; 8000 im Original
        var e = _entities[i];
        if (!e.Mobile || e.Dead || e.DugIn) return false;
        // ⚠ Ein Schiff, das noch IM DOCK steht (Auftrag 52), nimmt keinen
        // Fahrbefehl an. Es ist im Belegungsraster gar nicht vorhanden — eine
        // Wegsuche von dort aus hat keinen Anfang, und ein Ziel, das es nie
        // erreicht, saehe wie ein Fehler der Wegsuche aus. Der Auslauf ist ein
        // eigener Schritt (ShipLeaveDockTick) und laeuft weiter.
        if (e.LeavingDock >= 0) return false;

        int x = Mathf.Clamp((int)c.P2, 0, _nav.Width - 1);
        int y = Mathf.Clamp((int)c.P3, 0, _nav.Height - 1);
        var goal = new Vector2I(x, y);
        bool queue = c.P4 != 0;

        if (queue && (e.Path != null || e.Orders.Count > 0))
        {
            if (e.Orders.Count >= MaxOrders) return false;
            e.Orders.Add(Order.Move(goal));
            return true;
        }

        e.Target = -1;                 // ein Fahrbefehl bricht den Angriff ab
        e.Orders.Clear();
        if (e.FuelMax > 0 && e.Fuel <= 0) return false;

        var path = _nav.FindPath(new Vector2I(e.Col, e.Row), goal, e.Move, i);
        if (path == null || path.Count == 0)
        {
            // ⚠⚠ 18.08.2026 — HIER STAND NUR `return false`, UND DAS WAR EIN
            // ECHTER RÜCKSCHRITT, kein bloßer Prüfstandsfehler.
            //
            // <c>IssueMove</c> — der alte Direktweg — behält in diesem Fall das
            // Ziel und setzt <see cref="Entity.RetryIn"/>: wer von den eigenen
            // Leuten eingekeilt steht, fährt los, sobald sie Platz machen. Genau
            // das war die Reparatur vom 16.08.2026 (»NICHT MEHR VERGESSEN«).
            // Beim Umbau auf den Befehlsring ist sie nicht mitgekommen — und
            // seither geht JEDER Klick des Spielers durch DIESEN Weg. Eine
            // eingekeilte Einheit stand damit bis zum Missionsende.
            e.Goal = goal;
            e.RetryIn = RetryOff ? 0 : RetryTicks;
            return false;
        }
        e.Path = path;
        e.PathIdx = 0;
        e.Goal = goal;
        e.RetryIn = 0;
        e.Reserved = null;
        e.WaitTime = 0;
        // ⚠ UND DIE GEDULD. Ohne diese Zeile fängt ein frisch befohlener Wagen
        // mit <c>Block = 0</c> an und gibt beim ERSTEN versperrten Takt auf
        // (siehe BlockedStep) — der alte Weg setzt sie, dieser tat es nicht.
        //
        // ⚠ Der Wurf ist zugleich der Grund, warum <c>--befehl-check</c> ROT
        // meldete: <c>Determinism.Roll</c> zieht eine Zahl aus dem Strom. Ein
        // Weg, der sie zieht, und einer, der es nicht tut, laufen danach
        // auseinander — nicht wegen des Befehls, sondern wegen des Stroms.
        e.Block = BlockEnter + Determinism.Roll(BlockEnterSpread);
        return true;
    }

    /// <summary>2001, Angreifen (UNSERE SETZUNG). P1 = Einheit, P2 = Ziel,
    /// P3 = angereiht, P4/P5 = Zelle des Ziels beim Anreihen.</summary>
    /// <summary>
    /// <b>Busbefehl 11 ausführen.</b> Entspricht <c>0x4C2DDD</c> → <c>order()</c>.
    ///
    /// <para>⚠ Nimmt AUCH den ausgedienten Satz <see cref="CommandOp.OursAttack"/>
    /// = 2001 an, damit vor dem 22.08.2026 geschriebene Spielstände und
    /// Wiederholungen weiter laufen. Die zwei Sätze sind verschieden gebaut,
    /// darum wird zuerst der Opcode gefragt und dann gedeutet — nicht
    /// umgekehrt.</para>
    /// </summary>
    private bool ApplyAttack(in CommandRecord c)
    {
        bool alt = c.Op == CommandOp.OursAttack;

        int i = c.P1;
        // Der neue Satz trägt das Ziel in P4 (UTOK_NA), der alte in P2.
        int utok = alt ? c.P2 : c.P4;
        bool queue = alt ? c.P3 != 0 : c[6] != 0;   // P6 hat keinen Namen, nur den Index

        // ⚠ UTOK_NA entschlüsseln. 60000..60299 ist ein GEBÄUDE und wird über
        // seinen Platz gesucht; alles unter 8000 ist eine Einheitennummer und
        // bei uns unmittelbar der Listenplatz. Die zwei anderen Bänder
        // (30000 + Spalte = Bodenzelle, 40100..40249 = Brücke/Rampe) setzt
        // unser Absender nicht ab — siehe PostAttack —, also werden sie hier
        // ausdrücklich abgewiesen statt stillschweigend als Index gedeutet.
        int hit;
        if (utok >= 60000 && utok < 60300)
        {
            hit = -1;
            for (int k = 0; k < _entities.Count; k++)
                if (_entities[k].IsBuilding && _entities[k].Slot == utok - 60000)
                { hit = k; break; }
        }
        else if (utok >= 8000) return false;      // Bodenzelle/Brücke: noch nicht gebaut
        else hit = utok;

        if (i < 0 || i >= _entities.Count || hit < 0 || hit >= _entities.Count) return false;
        var e = _entities[i];
        var victim = _entities[hit];
        if (i == hit || e.Dead || victim.Dead || victim.IsProp) return false;
        if (!CanFight(e) || !IsHostile(e, victim)) return false;

        // ⚠ Die zwei Wächter des Originals (@0x4C2DF5, @0x4C2E04): eine Einheit
        // in UKOL 22 oder 23 — die Abwehrstellung im Auf- oder Abbau — nimmt
        // KEINEN Angriffsbefehl an. Bei uns ist das DugIn.
        if (e.DugIn) return false;

        if (queue && (e.Path != null || e.Orders.Count > 0))
        {
            if (e.Orders.Count >= MaxOrders) return false;
            // Der neue Satz führt in P2/P3 die Zelle DIESER Einheit (Formation
            // um das Ziel), der alte trug dort die Zielzelle in P4/P5.
            var ziel = alt ? new Vector2I(c.P4, c.P5) : new Vector2I(c.P2, c.P3);
            e.Orders.Add(Order.Attack(ziel, hit));
            return true;
        }
        e.Target = hit;
        e.Ordered = true;
        e.Path = null;
        e.Reserved = null;
        e.Orders.Clear();
        return true;
    }

    /// <summary>2002, Anhalten (UNSERE SETZUNG). P1 = Einheit.</summary>
    private bool ApplyStop(in CommandRecord c)
    {
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (e.Reserved is { } r) _nav?.ClearOccupant(r.X, r.Y, i);
        e.Path = null;
        e.Reserved = null;
        e.Orders.Clear();
        e.Target = -1;
        return true;
    }
}
