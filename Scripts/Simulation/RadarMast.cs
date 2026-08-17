namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>»RADAR SETZEN« — der erste der vier Bauaufträge des Originals.</b>
/// Antwort auf <b>C7</b>, erste Scheibe.
///
/// <para><b>⚠⚠ Die Fehlerliste war falsch.</b> Zu C7 stand dort »Zerstörte
/// Gebäude wieder aufbauen — <i>gibt es nicht</i>«. Das ist zurückgezogen: das
/// Original hat sehr wohl Baufahrzeuge, und sie stehen in seiner eigenen
/// Befehlsliste (0x4FD660) — <b>17 »Depot bauen«, 18 »Mine bauen«,
/// 19 »Generator bauen«, 20 »Radar setzen«</b>. Was es <i>nicht</i> gibt, ist
/// das Wiederaufrichten einer Ruine.</para>
///
/// <para><b>Warum ausgerechnet »Radar setzen« zuerst:</b> es ist der einzige
/// der vier, der <b>keinen Platzierungsmodus</b> braucht. Die anderen drei
/// setzen <c>dword[0x502ACC]</c> und warten auf einen Klick; dieser hier
/// schickt @0x448A4A <b>sofort Kommando 27</b> auf die gewählte Einheit. Damit
/// ist die ganze Kette (Befehlsleiste → Kommando → es entsteht etwas) an einem
/// Stück prüfbar, ohne neue Oberfläche.</para>
///
/// <para><b>Kommando 27 @0x422180, Befehl für Befehl:</b></para>
/// <code>
///   if (byte[Einheit+0x45] == 0) return       ; der VORRAT, und ohne ihn nichts
///   byte[Einheit+0x45]--                      ; einer verbraucht
///   Spieler = Einheit / 1000                  ; wieder positionell!
///   place_radar(Spalte, Zeile, Spieler)       ; @0x421B40
///   word[Einheit+0x40] = 10                   ; Auftrag 10
///   Meldung 49
/// </code>
///
/// <para><b>⚠⚠ Und ein Radar ist KEIN GEBÄUDE.</b> <c>place_radar</c> @0x421B40
/// legt einen Satz in einer <b>eigenen Tafel</b> an — <c>0x677F30</c>,
/// <b>200 Plätze zu 6 Byte</b>: <c>+0x00</c> Spalte, <c>+0x01</c> Zeile,
/// <c>+0x02</c> und <c>+0x03</c> zwei Zufallsbytes (<c>rand()%20+10</c> und
/// <c>rand()%10+5</c>), <c>+0x04</c> die Belegtmarke 0xFF, <c>+0x05</c> der
/// Besitzer. Ist die Tafel voll, sagt es <c>»Too many radars«</c> (0x4F91C0).
/// Es gibt also weder einen Gebäudesatz noch einen Grundriss noch Trefferpunkte
/// — ein Mast ist ein Punkt mit einem Besitzer.</para>
///
/// <para><b>Was er tut</b> (@0x4209AE, im Nebeltakt): für jeden belegten Platz
/// öffnet er die Sicht bei <c>(Spalte, Zeile)</c> mit <b>Radius 10</b> — und
/// zwar genau dann, wenn <c>byte[0x87B155 + 40·Sichtspieler + Besitzer]</c>
/// gesetzt ist, also <b>über die Bündnismatrix</b>. Ein verbündeter Mast sieht
/// für mich mit.</para>
///
/// <para><b>Wer es kann</b> (@0x4B1C2D, im Einheitenbau): trägt der Entwurf
/// <b>Waffe 75</b> — das ist der <b>»Radarstab Ausleger«</b> der
/// Ausrüstungsliste —, bekommt die Einheit <c>+0x45 = 20</c> und
/// <c>+0x46 = 20</c>, also <b>zwanzig Masten</b>. Jede andere bekommt
/// <c>0xFF</c>. Der fertige Entwurf dazu ist <c>64 »Radar Installer«</c>.</para>
///
/// <para>⚠ <b>0xFF ist NICHT 0</b> — die Prüfung in Kommando 27 (<c>== 0</c>)
/// hält andere Einheiten also gar nicht auf. Das Tor ist das MENÜ: nur wer das
/// Bauteil trägt, bekommt den Eintrag überhaupt angeboten. Bei uns ist es
/// dasselbe, und zusätzlich prüft der Befehl es noch einmal — im Netzspiel ist
/// ein Satz von aussen keine Aussage über die Wahrheit.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Das Bauteil, das Radarmasten setzen kann — <b>75</b>, in der
    /// Ausrüstungsliste »Radarstab Ausleger«.</summary>
    public const int RadarKitWeapon = 75;

    /// <summary>Wieviele Masten ein solches Fahrzeug mitbringt: <c>0x14</c>
    /// @0x4B1C45.</summary>
    public const int RadarKitCharges = 20;

    /// <summary>Die Tafel hat <b>200</b> Plätze (@0x421B87, <c>cmp ax, 0xC8</c>);
    /// darüber sagt das Original <c>»Too many radars«</c>.</summary>
    public const int RadarMastSlots = 200;

    /// <summary>Sichtweite eines Mastes: <c>push 0xa</c> @0x4209E4 — dieselbe
    /// Zehn wie bei einem Gebäude.</summary>
    public const int RadarMastSight = 10;

    /// <summary>Ein Radarmast — der Satz aus <c>0x677F30</c>.</summary>
    public sealed class RadarMast
    {
        public int Col, Row, Owner;

        /// <summary>+0x02 und +0x03, die zwei ausgewürfelten Bytes
        /// (<c>rand()%20+10</c>, <c>rand()%10+5</c>). ⚠ <b>Wofür sie gut sind,
        /// ist NICHT gelesen.</b> Sie werden trotzdem mitgeführt und gewürfelt,
        /// damit der Zufallsstrom denselben Verlauf nimmt wie im Original —
        /// wer sie wegliesse, verschöbe jeden späteren Wurf.</summary>
        public int A, B;
    }

    private readonly List<RadarMast> _radarMasts = new();

    /// <summary>Die gesetzten Masten — für Nebel und Prüfstand.</summary>
    public IReadOnlyList<RadarMast> RadarMasts => _radarMasts;

    /// <summary>Wieviele gesetzt und wieviele abgelehnt wurden. ⚠ Regel 33:
    /// ohne die zweite Zahl ist »kein Mast« nicht von »der Befehl kam nie an«
    /// zu unterscheiden.</summary>
    public int RadarMastsPlaced, RadarMastsRefused;

    /// <summary>Was zuletzt beim Radarsetzen geschah.</summary>
    public string RadarNote = "";

    /// <summary>
    /// <b>Wieviele Masten hat diese Einheit noch?</b>
    ///
    /// <para>⚠ <b>Unsere Anordnung, und sie ist eine Vereinfachung mit
    /// Absicht:</b> das Original schreibt <c>+0x45</c> beim Bau der Einheit
    /// (@0x4B1C45). Wir lösen den Wert beim ersten Fragen aus dem Entwurf auf.
    /// Das Ergebnis ist dasselbe, und es gilt auch für Einheiten, die die KARTE
    /// mitbringt — die laufen bei uns durch keinen Bauweg und hätten sonst
    /// nie einen Vorrat bekommen.</para></summary>
    public int RadarChargesOf(int idx)
    {
        if (idx < 0 || idx >= _entities.Count) return 0;
        var e = _entities[idx];
        if (e.RadarCharges >= 0) return e.RadarCharges;
        e.RadarCharges = RadarKitOf(e) ? RadarKitCharges : 0;
        return e.RadarCharges;
    }

    /// <summary>Trägt diese Einheit den Radarstab-Ausleger?</summary>
    private bool RadarKitOf(Entity e)
    {
        if (e.IsBuilding || e.IsProp || e.Mark < 0) return false;
        LoadDesigns();
        int owner = e.Owner is >= 0 and <= 7 ? e.Owner : 0;
        var d = DesignBySlot(e.Mark + 200 * owner);
        return d is { } dd && dd.Weapon == RadarKitWeapon;
    }

    /// <summary>
    /// <b>Einen Mast setzen</b> — <c>place_radar</c> @0x421B40.
    ///
    /// <para>Die Zelle muss frei sein: <c>word[0xBDEA80 + 2·Zelle]</c> darf
    /// <c>0xFFFE</c> sein (leer) oder eine Einheitsnummer tragen
    /// (<c>&lt;= 0x1F40</c>); alles darüber ist Gelände oder Gebäude und
    /// blockiert. ⚠ Auf einer Zelle, auf der eine EINHEIT steht, darf also
    /// gesetzt werden — das ist nachgebaut, nicht geglättet.</para></summary>
    private bool PlaceRadarMast(int col, int row, int owner)
    {
        if (_nav == null) return false;
        if (col < 0 || row < 0 || col >= _nav.Width || row >= _nav.Height) return false;
        // Unsere Entsprechung der Zellprüfung: Gelände, auf dem etwas STEHEN
        // kann. Ein Gebäude blockiert, eine Einheit nicht.
        int occ = _nav.OccupantAt(col, row);
        if (occ >= 0 && occ < _entities.Count && _entities[occ].IsBuilding) return false;
        if (_radarMasts.Count >= RadarMastSlots)
        {
            RadarNote = "Too many radars";        // die Meldung des Originals
            return false;
        }
        _radarMasts.Add(new RadarMast
        {
            Col = col, Row = row, Owner = owner,
            // ⚠ Die zwei Würfe stehen hier, obwohl ihr Zweck ungelesen ist —
            // siehe RadarMast.A/B. Reihenfolge wie im Original.
            A = Simulation.Determinism.Roll(20) + 10,
            B = Simulation.Determinism.Roll(10) + 5,
        });
        RadarMastsPlaced++;
        return true;
    }

    /// <summary>
    /// Die Masten als Nebelspäher — sie kommen in <see cref="Watchers"/> mit.
    ///
    /// <para>Über die <b>Bündnismatrix</b>, wie das Original (@0x4209D9): ein
    /// Mast eines Verbündeten sieht für mich mit. Deshalb steht hier
    /// <see cref="Allied"/> und nicht <c>Owner == ViewPlayer</c> — der
    /// Unterschied ist genau der Fall, für den es die Matrix gibt.</para></summary>
    private IEnumerable<(int Col, int Row, int Sight, int Elev)> RadarWatchers()
    {
        foreach (var m in _radarMasts)
        {
            if (m.Owner is < 0 or > 7) continue;
            if (!Allied(m.Owner, ViewPlayer)) continue;
            // Elev 1 haelt UnitRadius bei genau der Zehn — dieselbe Zeile wie
            // beim Gebaeude, siehe Watchers().
            yield return (m.Col, m.Row, RadarMastSight, 1);
        }
    }

    // ================= der Prüfstand ==========================================

    private int _radarCheck = -1;

    /// <summary><c>--radar-check</c> anwerfen.</summary>
    public void RadarCheckStart() => _radarCheck = 0;

    /// <summary>Wieviele Zellen der Sichtspieler gerade BEOBACHTET. Das ist der
    /// eigentliche Gegenstand: ein Mast, der keine Sicht öffnet, ist keiner.</summary>
    private int WatchedCells()
    {
        if (_fog == null) return -1;
        int n = 0;
        for (int i = 0; i < _fog.CellCount; i++)
            if (_fog.CellAt(i) == Simulation.FogGrid.Watched) n++;
        return n;
    }

    /// <summary>
    /// <c>--radar-check</c> — <b>setzt eine Einheit mit Radarstab einen Mast,
    /// und öffnet der Mast wirklich Sicht?</b>
    ///
    /// <para><b>Er misst die WIRKUNG, nicht den Eintrag</b> (Regel 11): gezählt
    /// werden die beobachteten Zellen vor und nach dem Setzen. Ein Prüfstand,
    /// der nur »ein Mast steht in der Liste« sagt, hätte auch dann Recht, wenn
    /// die Masten im Nebeltakt gar nicht vorkämen — und genau diese Zeile
    /// (<c>RadarWatchers</c> in <c>Watchers()</c>) ist die, die man
    /// vergisst.</para>
    ///
    /// <para><b>⚠ Und er prüft die Gegenrichtung:</b> eine Einheit OHNE das
    /// Bauteil darf keinen Vorrat und keinen Knopf haben. Ohne das wäre »der
    /// Knopf steht da« kein Urteil über das Tor.</para>
    ///
    /// <para>⚠ Er SETZT die Einheit auf die Karte (Entwurf mit Waffe 75) und
    /// sagt es — auf keiner Karte steht von Haus aus ein »Radar
    /// Installer«.</para></summary>
    private void PollRadarCheck()
    {
        if (_radarCheck != 0) return;
        _radarCheck = -1;
        var sb = new System.Text.StringBuilder("radar-check\n");
        LoadDesigns();
        if (_designs == null || _nav == null || _fog == null)
        { sb.Append("  KEIN URTEIL: keine Entwuerfe/Karte"); GD.Print(sb); return; }

        // ---- den Entwurf mit dem Radarstab suchen ----
        int slot = -1;
        string nm = "";
        for (int i = 0; i < _designs.Count; i++)
            if (_designs[i].Weapon == RadarKitWeapon)
            { slot = _designs[i].Slot; nm = _designs[i].Name; break; }
        if (slot < 0)
        {
            sb.Append($"  KEIN URTEIL: kein Entwurf mit Waffe {RadarKitWeapon} " +
                      $"(»Radarstab Ausleger«) in dieser Entwurfsliste");
            GD.Print(sb); return;
        }
        sb.AppendLine($"  Entwurf {slot} \"{nm}\" traegt Waffe {RadarKitWeapon} " +
                      $"(»Radarstab Ausleger«) -> {RadarKitCharges} Masten erwartet");

        int owner = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        // ⚠⚠ DIE STELLE MUSS DUNKEL SEIN, sonst misst der Lauf nichts. Der
        // erste Anlauf setzte die Einheit in die Kartenmitte — mitten in das
        // Gebiet, das der Spieler ohnehin einsieht. Ergebnis: »455 -> 455,
        // +0 Zellen«, und die Zeile darunter nannte das FALSCH. Es war keiner:
        // die Gegend war schon offen, der Fall konnte die Wirkung gar nicht
        // zeigen (Regel EE). Gesucht wird jetzt eine Zelle, um die herum
        // moeglichst WENIG beobachtet ist.
        UpdateFog();
        var dunkel = new Vector2I(_nav.Width / 2, _nav.Height / 2);
        int dunkelWert = int.MaxValue;
        for (int y = 6; y < _nav.Height - 6; y += 7)
            for (int x = 6; x < _nav.Width - 6; x += 7)
            {
                if (!_nav.IsFree(x, y, Simulation.NavGrid.MoveClass.Vehicle)) continue;
                int w = WatchedAround(x, y, 12);
                if (w < dunkelWert) { dunkelWert = w; dunkel = new Vector2I(x, y); }
                if (w == 0) { y = _nav.Height; break; }
            }
        sb.AppendLine($"  dunkelste freie Stelle: ({dunkel.X},{dunkel.Y}), " +
                      $"{dunkelWert} von 625 Zellen im Umkreis 12 beobachtet");
        _radarCheckNah0 = dunkelWert;

        // ⚠ EINGRIFF: eine solche Einheit gibt es auf keiner Karte fertig.
        int satz = SpawnReinforcement(slot % 200, dunkel.X, dunkel.Y, owner);
        if (satz < 0)
        { sb.Append("  KEIN URTEIL: die Einheit liess sich nicht absetzen"); GD.Print(sb); return; }

        // ⚠⚠ DIE FALLE FF, und sie hat hier sofort zugeschlagen:
        // `SpawnReinforcement` gibt den SATZINDEX zurueck (Slot), NICHT die
        // Stelle in `_entities`. Als Listenstelle gelesen zeigte sie hier auf
        // eine fremde Einheit (Satz 48 -> Marke 103, Besitzer 1), und der
        // Pruefstand meldete »Vorrat 0 — FALSCH« ueber ein Fahrzeug, das mit
        // der Sache nichts zu tun hatte. Gesucht wird ueber Satz UND Besitzer:
        // die Saetze laufen je Spieler von vorn.
        int idx = -1;
        for (int i = _entities.Count - 1; i >= 0; i--)
            if (_entities[i].Slot == satz && _entities[i].Owner == owner
                && !_entities[i].IsBuilding && !_entities[i].Dead) { idx = i; break; }
        if (idx < 0)
        { sb.Append($"  KEIN URTEIL: Satz {satz} nicht in der Liste wiedergefunden"); GD.Print(sb); return; }
        var u = _entities[idx];
        sb.AppendLine($"  ⚠ EINGRIFF: \"{u.Name}\" fuer Spieler {owner} auf " +
                      $"({u.Col},{u.Row}) gesetzt");

        int vorrat = RadarChargesOf(idx);
        // ⚠ Wenn die Zahl nicht stimmt, muss die Zeile sagen, an WELCHEM Glied
        // es liegt — Marke, Besitzer, aufgeloester Entwurf, dessen Waffe.
        var dd = DesignBySlot(u.Mark + 200 * (u.Owner is >= 0 and <= 7 ? u.Owner : 0));
        sb.AppendLine($"  Vorrat: {vorrat} " +
                      $"{(vorrat == RadarKitCharges ? "— richtig" : "— FALSCH")}" +
                      $"  [Marke {u.Mark}, Besitzer {u.Owner}, sec47-Platz " +
                      $"{u.Mark + 200 * (u.Owner is >= 0 and <= 7 ? u.Owner : 0)} -> " +
                      $"{(dd == null ? "NICHT GEFUNDEN" : $"\"{dd.Value.Name}\" Waffe {dd.Value.Weapon}")}]");

        // ---- die GEGENPROBE: eine Einheit OHNE das Bauteil ----
        int ohne = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (i == idx || e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Owner != owner || e.Mark < 0) continue;
            ohne = i; break;
        }
        sb.AppendLine(ohne < 0
            ? "  Gegenprobe: keine zweite eigene Einheit da — der Fall entfaellt"
            : $"  Gegenprobe: Einheit {ohne} \"{_entities[ohne].Name}\" hat " +
              $"{RadarChargesOf(ohne)} Masten " +
              $"{(RadarChargesOf(ohne) == 0 ? "— richtig, sie traegt das Bauteil nicht" : "— FALSCH, sie duerfte keinen haben")}");

        // ---- SETZEN, ueber den Knopfweg ----
        //
        // ⚠⚠ Und DANN EINEN TAKT WARTEN. Der Befehl geht durch den Ring und
        // wirkt am naechsten Taktanfang; wer gleich danach liest, sieht
        // »Vorrat unveraendert, 0 Masten« und haelt einen richtigen Einbau fuer
        // kaputt. Genau das hat dieser Pruefstand im ersten Anlauf gemeldet.
        UpdateFog();
        _radarCheckSicht = WatchedCells();
        // ⚠ Die Umgebung der KUENFTIGEN Maststelle auch vorher zaehlen — die
        // Gesamtzahl ist auf einer bewegten Karte ein schlechter Zeuge (die KI
        // fährt, andere Späher öffnen und schliessen). Der Umkreis um genau
        // diese Zelle ist der Gegenstand.
        _radarCheckNah = WatchedAround(u.Col, u.Row, 12);
        _radarCheckIdx = idx;
        _radarCheckVorrat = vorrat;
        _sel.Clear(); _sel.Add(idx); _selected = idx;
        bool ok = PlaceRadarFromPanel();
        sb.AppendLine($"  Gesetzt ueber den Knopfweg: {(ok ? "ja" : "NEIN — " + RadarNote)}");
        _radarLog = sb;
        _radarCheckSim = DebugTicks;
        _radarCheck = ok ? 1 : -1;
        if (!ok) GD.Print(sb.ToString());
    }

    /// <summary>Wieviele Zellen im Umkreis von <paramref name="r"/> um
    /// (col,row) gerade beobachtet werden.</summary>
    private int WatchedAround(int col, int row, int r)
    {
        if (_fog == null) return 0;
        int n = 0;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (_fog.IsWatched(col + dx, row + dy)) n++;
        return n;
    }

    private int _radarCheckIdx = -1, _radarCheckVorrat, _radarCheckSicht;
    private int _radarCheckNah, _radarCheckNah0;
    private long _radarCheckSim;
    private System.Text.StringBuilder? _radarLog;

    /// <summary>Stufe 2: einen Takt nach dem Befehl nachsehen, was er bewirkt
    /// hat.
    ///
    /// <para>⚠ <b>Der Takt muss WIRKLICH weiter sein.</b> Der erste Anlauf rief
    /// diese Stufe im selben SimTick auf, in dem der Befehl abgesetzt wurde —
    /// und meldete »Vorrat unveraendert, 0 Masten«, also einen richtigen Einbau
    /// als kaputt. Der Ring wirkt am naechsten Taktanfang.</para></summary>
    private void PollRadarCheck2()
    {
        if (_radarCheck != 1 || _radarLog == null) return;
        if (DebugTicks <= _radarCheckSim) return;
        _radarCheck = -1;
        var sb = _radarLog;
        UpdateFog();
        int sichtNach = WatchedCells();
        int jetzt = RadarChargesOf(_radarCheckIdx);

        sb.AppendLine($"    {RadarNote}");
        sb.AppendLine($"  Vorrat {_radarCheckVorrat} -> {jetzt} " +
                      $"{(jetzt == _radarCheckVorrat - 1 ? "(einer weg, richtig)" : "(FALSCH)")}");
        sb.AppendLine($"  Masten in der Tafel: {_radarMasts.Count} " +
                      $"({RadarMastsPlaced} gesetzt, {RadarMastsRefused} abgelehnt)");

        // ---- die WIRKUNG ----
        //
        // ⚠ Die Einheit selbst sieht auch, und sie steht auf derselben Zelle —
        // der Zuwachs ist also die Sicht des MASTES ueber die der Einheit
        // hinaus. Bei Sichtweite 10 gegen die der Einheit (meist 4..8) bleibt
        // ein Rest; waere er 0, kaeme der Mast im Nebeltakt gar nicht vor.
        sb.AppendLine($"  SICHT: {_radarCheckSicht} -> {sichtNach} beobachtete Zellen " +
                      $"(+{sichtNach - _radarCheckSicht})");
        // ⚠ »Kein Zuwachs« hat ZWEI moegliche Ursachen, und ein Pruefstand, der
        // sie nicht trennt, zeigt auf den falschen Taeter (Regel CC). Also
        // werden beide gemessen: kommt der Mast ueberhaupt in Watchers() vor,
        // und wieweit sieht die Einheit selbst?
        int spaeher = 0;
        foreach (var _ in RadarWatchers()) spaeher++;
        int eigen = _radarCheckIdx >= 0 && _radarCheckIdx < _entities.Count
                  ? _entities[_radarCheckIdx].Sight : -1;
        if (_radarMasts.Count == 0 || _radarCheckIdx < 0)
        { sb.Append("  KEIN URTEIL: kein Mast in der Tafel"); GD.Print(sb.ToString()); return; }
        var mast = _radarMasts[0];
        var uu = _entities[_radarCheckIdx];
        int wirk = uu.Sight + ElevOf(uu.Col, uu.Row) - 1;      // FogGrid.UnitRadius
        sb.AppendLine($"    Masten in Watchers(): {spaeher}; die Einheit selbst sieht " +
                      $"{wirk} weit (Sicht {eigen} + Hoehe {ElevOf(uu.Col, uu.Row)} − 1), " +
                      $"der Mast {RadarMastSight}");

        // ⚠⚠ DIE EINHEIT MUSS WEG, sonst ist gar nichts zu messen. Zwei Anlaeufe
        // haben das gezeigt: erst stand sie mitten im eigenen Gebiet (455 -> 455),
        // dann auf einer Anhoehe, wo sie mit Sicht+Hoehe−1 = 12 selbst WEITER
        // sieht als der Mast (465 -> 465). Beide Male meldete die Zeile darunter
        // »FALSCH« ueber einen Fall, der die Wirkung nicht zeigen KONNTE.
        // Jetzt die scharfe Frage: bleibt es offen, wenn nur noch der Mast da
        // ist? Das kann nur der Mast beantworten.
        _nav?.ClearOccupant(uu.Col, uu.Row, _radarCheckIdx);
        uu.Col = 0; uu.Row = 0; uu.Pos = CellCenter(0, 0);
        uu.Dead = true;                       // ⚠ Eingriff, und er steht in der Ausgabe
        UpdateFog();
        int nurMast = WatchedAround(mast.Col, mast.Row, 12);
        sb.AppendLine($"  ⚠ EINGRIFF: die Einheit wird entfernt — jetzt haelt NUR der Mast");
        sb.AppendLine($"  UMKREIS 12 um den Mast ({mast.Col},{mast.Row}): " +
                      $"vorher (dunkel) {_radarCheckNah0}, mit Einheit+Mast {_radarCheckNah}, " +
                      $"NUR Mast {nurMast}");

        // Ein Kreis mit Radius 10 hat rund 317 Zellen; deutlich mehr als null
        // und weniger als die 465 der Einheit auf dem Berg ist genau das, was
        // ein Mast mit Radius 10 leisten kann.
        sb.AppendLine(nurMast > 0
            ? $"    der Mast haelt {nurMast} Zellen offen — RICHTIG (ohne ihn waeren es 0)"
            : "    ⚠ FALSCH: ohne die Einheit ist alles wieder dunkel — der Mast " +
              "kommt im Nebeltakt nicht vor");
        sb.AppendLine($"  SICHT gesamt: {_radarCheckSicht} -> {sichtNach} " +
                      "(auf einer bewegten Karte ein schlechter Zeuge — siehe den Umkreis)");
        GD.Print(sb.ToString());
    }

    // ================= was die Oberfläche davon braucht =======================

    /// <summary>Die Einheit der Auswahl, die einen Mast setzen könnte — mit dem
    /// Vorrat. <c>null</c>, wenn keine kann.</summary>
    public (int Index, string Name, int Charges)? RadarChoiceOfSelection()
    {
        foreach (int i in _sel)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer) continue;
            int n = RadarChargesOf(i);
            if (n <= 0) continue;
            return (i, e.Name.Length > 0 ? e.Name : "Einheit", n);
        }
        return null;
    }

    /// <summary>»Radar setzen« aus der Befehlsleiste.</summary>
    public bool PlaceRadarFromPanel()
    {
        var w = RadarChoiceOfSelection();
        if (w == null) { RadarNote = "keine Einheit mit Radarstab gewaehlt"; return false; }
        bool ok = PostPlaceRadar(w.Value.Index);
        UpdatePanel();
        QueueRedraw();
        return ok;
    }

    /// <summary>Eine Zeile über die Masten — für Prüfstand und Protokoll.</summary>
    public string RadarLine()
    {
        int mein = 0;
        foreach (var m in _radarMasts) if (m.Owner == ViewPlayer) mein++;
        return $"radar: {_radarMasts.Count} Masten ({mein} eigene), " +
               $"{RadarMastsPlaced} gesetzt, {RadarMastsRefused} abgelehnt" +
               (RadarNote.Length > 0 ? $"; {RadarNote}" : "");
    }
}
