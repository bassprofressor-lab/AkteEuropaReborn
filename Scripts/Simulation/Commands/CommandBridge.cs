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
            if (Commands.Post(c)) n++;
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
    /// Ein Angriffsklick wird zu je einem Satz je Einheit.
    ///
    /// <para>⚠ <b>UNSERE SETZUNG:</b> Opcode <see cref="CommandOp.OursAttack"/>
    /// = 2001. Für einen Angriffsbefehl mit einer ZIELEINHEIT ist im Original
    /// kein Behandler belegt; Opcode 1 sieht danach aus, trägt aber in P2 einen
    /// Richtungswert aus der Tafel 0x4F4AF0 (@0x4326BD..0x4326F0) und ist damit
    /// etwas anderes. Eine Deutung ohne Zahl wird nicht eingebaut (Regel 6),
    /// also nimmt der Angriff bis auf Weiteres eine eigene Nummer über 1201.</para>
    /// </summary>
    /// <returns>false, wenn der Klick kein Ziel getroffen hat — dann darf der
    /// Aufrufer daraus einen Bewegungsbefehl machen, genau wie heute.</returns>
    public bool PostAttack(Vector2 mapPos, bool queue = false)
    {
        int hit = Pick(mapPos);
        if (hit < 0) return false;
        var victim = _entities[hit];
        if (victim.IsProp || victim.Dead) return false;

        int n = 0;
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (i == hit || !CanFight(e) || !IsHostile(e, victim)) continue;
            var c = CommandRecord.Make(CommandOp.OursAttack, (byte)ViewPlayer,
                                 (short)i, (short)hit, (short)(queue ? 1 : 0),
                                 (short)victim.Col, (short)victim.Row);
            if (Commands.Post(c)) n++;
        }
        if (n == 0) return false;
        AddOrderMark(victim.Pos, attack: true);
        _order = $"Angriffsbefehl abgesetzt -> Slot {victim.Slot}: {n} Satz/Sätze";
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
            if (Commands.Post(CommandRecord.Make(CommandOp.OursStop, (byte)ViewPlayer, (short)i))) n++;
        return n;
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
        CommandOp.Move => ApplyMove(c),
        CommandOp.OursAttack => ApplyAttack(c),
        CommandOp.OursStop => ApplyStop(c),
        _ => false,
    };

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
        if (path == null || path.Count == 0) return false;
        e.Path = path;
        e.PathIdx = 0;
        e.Goal = goal;
        e.Reserved = null;
        e.WaitTime = 0;
        return true;
    }

    /// <summary>2001, Angreifen (UNSERE SETZUNG). P1 = Einheit, P2 = Ziel,
    /// P3 = angereiht, P4/P5 = Zelle des Ziels beim Anreihen.</summary>
    private bool ApplyAttack(in CommandRecord c)
    {
        int i = c.P1, hit = c.P2;
        if (i < 0 || i >= _entities.Count || hit < 0 || hit >= _entities.Count) return false;
        var e = _entities[i];
        var victim = _entities[hit];
        if (i == hit || e.Dead || victim.Dead || victim.IsProp) return false;
        if (!CanFight(e) || !IsHostile(e, victim)) return false;

        if (c.P3 != 0 && (e.Path != null || e.Orders.Count > 0))
        {
            if (e.Orders.Count >= MaxOrders) return false;
            e.Orders.Add(Order.Attack(new Vector2I(c.P4, c.P5), hit));
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
