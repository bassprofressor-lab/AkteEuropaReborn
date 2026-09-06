using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE TUERSPERRE</b> — warum niemand in ein Gebäude fährt, das ihm nicht
/// gehört. Gelesen und gebaut am 06.09.2026.
///
/// <para><b>Seine Meldung:</b> »Solange ein Gebäude noch im fremden Besitz ist,
/// kann man noch nicht in die Tür einfahren. Aktuell können wir das. Im
/// Original wird das Gebäude sozusagen genau davor eingenommen, bei uns fährt
/// eine Einheit schon rein, wobei es noch dem Gegner gehört.« Nachgefragt:
/// »Die Einheit fährt in die Tür rein, so dass man sie noch leicht sieht, weil
/// das Tor nicht ganz zu geht. Sie verschwindet also nicht von der Karte.«</para>
///
/// <para><b>Damit war die Ebene klar</b> — nicht das Unterstellen (dort hat
/// <c>Einfahrt.cs</c> längst ein Besitzertor), sondern der SCHRITT auf die
/// Türzelle.</para>
///
/// <para><b>Und die Sperre steht NICHT in der Wegsuche.</b> <c>Can_go
/// 0x4055D0</c> hat keinen Besitzerabgleich; ein Besitzertest im Schritt wäre
/// unsere Erfindung gewesen. Sie steht im <b>Einnahme-Arm des Gebäudetakts</b>,
/// und die vier tragenden Zeilen sind selbst nachgeschlagen:</para>
/// <code>
///   @0x43CBEF  ecx = 2*(Spalte*256 + Zeile)     ; die TUERzelle
///              ax  = word[0xBDEA82 + ecx]       ; = Zelle (Spalte, Zeile+1)
///              cmp ax, 0x1F40 / jae raus        ; nichts oder keine Einheit
///              ax /= 1000                       ; die 1000er-Stelle IST der Besitzer
///   @0x43CC15  al  = byte[0xC06915 + 76*id]     ; der Besitzer des GEBAEUDES
///   @0x43CC21  cmp eax, edi
///   @0x43CC23  je  raus                         ; gleicher Besitzer -> nichts
///   @0x43CC29  word[0xBDEA80 + ecx] := 0xFFFF   ; die TUER wird gesperrt
/// </code>
///
/// <para>⭐ <b>Die zwei Basen liegen zwei auseinander, und die Schrittweite je
/// Zelle ist ebenfalls zwei.</b> <c>+2</c> ist also keine zweite Ebene, sondern
/// die nächste ZEILE. Das ist die ganze Mechanik: sobald ein Fremder VOR der
/// Tür steht, ist die Tür dicht — er kann nie auf sie treten. <c>0xFFFF</c>
/// fällt über <c>0x4054D0</c> auf 0, eine harte Sperre.</para>
///
/// <para>⚠ <b>Ein reiner Bytevergleich, kein Bündnistest.</b> Ein Verbündeter
/// sperrt genauso. So steht es da, und so ist es gebaut.</para>
///
/// <para>⚠ <b>Die Taktordnung trägt die Sache:</b> der Gebäudetakt
/// (<c>@0x416690</c>) läuft VOR <c>move units</c> (<c>@0x4166BB</c>). Der
/// Stempel fällt also, bevor der Schritt geprüft wird — darum steht
/// <c>TuersperreTakt</c> vor allem, was Einheiten bewegt.</para>
///
/// <para>⚠ <b>UNSERES:</b> das Original stempelt in die imap, wir führen eine
/// eigene Menge in <see cref="Simulation.NavGrid"/>. Wirkung gleich, Weg
/// anders — unsere imap trägt Beleger und Gelände getrennt, und ein
/// <c>0xFFFF</c> hineinzuschreiben hiesse, einen Beleger zu erfinden.</para>
///
/// <para>Gegenschalter <c>--tuersperre-alt</c>, Prüfstand
/// <c>--tuersperre-probe</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Wie oft eine Tür in diesem Lauf gesperrt wurde (Flanke, nicht
    /// je Takt). ⚠ Ohne die Zahl ist »es ist gesperrt« nicht von »es wurde nie
    /// geprüft« zu unterscheiden.</summary>
    public int TuerGesperrtFlanken;

    /// <summary>Und wie oft wieder freigegeben — die Gegenzahl, damit ein
    /// Stempel, der nie zurückgenommen wird, auffällt.</summary>
    public int TuerFreiFlanken;

    /// <summary>
    /// Der Einnahme-Arm, soweit er die TUER betrifft (<c>@0x43CBEF..0x43CC29</c>).
    ///
    /// <para>Läuft je Takt über alle gebauten Gebäude mit Tür. Steht auf der
    /// Zelle vor der Tür eine lebende Einheit, die dem Gebäude nicht gehört,
    /// wird die Türzelle gesperrt; sonst wird sie freigegeben. Der Verfall des
    /// Stempels ist damit derselbe wie im Original (<c>@0x43D276</c> gibt frei,
    /// sobald kein Fremder mehr davorsteht).</para>
    /// </summary>
    private void TuersperreTakt()
    {
        if (_nav == null || Simulation.NavGrid.TuersperreAlt) return;

        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (b.Built == 0 || b.DoorCells.Count == 0) continue;

            int tc = b.Col + b.DoorCol, tr = b.Row + b.DoorRow;

            // Die Zelle VOR der Tuer: eine Zeile weiter (@0x43CBEF liest
            // 0xBDEA82, also den naechsten Zellplatz).
            int fremder = BelegerVorDerTuer(tc, tr + 1);
            bool zu = fremder >= 0 && _entities[fremder].Owner != b.Owner;

            if (_nav.TuerSperre(tc, tr, zu))
            {
                if (zu) TuerGesperrtFlanken++;
                else TuerFreiFlanken++;
            }
        }
    }

    /// <summary>Welche lebende, fahrende EINHEIT steht auf dieser Zelle?
    /// Listenplatz oder -1. Gebäude und Kulisse zählen nicht — das Original
    /// verwirft alles ab <c>0x1F40</c> (8000) an derselben Stelle.</summary>
    private int BelegerVorDerTuer(int c, int r)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead) continue;
            if (u.Col == c && u.Row == r) return i;
        }
        return -1;
    }

    /// <summary>
    /// <c>--tuersperre-probe</c> — <b>kommt eine fremde Einheit auf die
    /// Türzelle?</b>
    ///
    /// <para>⚠ Gemessen werden BEIDE Richtungen und der Gegenschalter. »Die
    /// Tür ist zu« allein wäre keine Messung: sie müsste auch wieder aufgehen,
    /// sobald der Fremde weg ist, und für den EIGENEN Besitzer darf sie nie
    /// zugehen. Sonst hätte die Sperre einfach immer gewonnen.</para>
    /// </summary>
    public string TuersperreProbe()
    {
        var sb = new System.Text.StringBuilder("tuersperre-probe\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();

        int bi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (b.Built == 0 || b.DoorCells.Count == 0) continue;
            bi = i; break;
        }
        if (bi < 0) return sb.Append("  kein gebautes Gebaeude mit Tuer auf dieser Karte").ToString();

        int ui = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead || !u.Mobile) continue;
            ui = i; break;
        }
        if (ui < 0) return sb.Append("  keine fahrende Einheit").ToString();

        var geb = _entities[bi];
        var eh = _entities[ui];
        int tc = geb.Col + geb.DoorCol, tr = geb.Row + geb.DoorRow;
        int merkC = eh.Col, merkR = eh.Row, merkO = eh.Owner, merkGO = geb.Owner;

        bool Zu()
        {
            TuersperreTakt();
            return _nav.IstTuerGesperrt(tc, tr);
        }

        // 1. FREMDE Einheit vor der Tuer -> die Tuer ist dicht
        geb.Owner = 1; eh.Owner = 0;
        eh.Col = tc; eh.Row = tr + 1;
        bool zuFremd = Zu();
        bool schrittFremd = _nav.CanEnter(tc, tr, Simulation.NavGrid.MoveClass.Vehicle);
        sb.AppendLine($"  fremde Einheit (P{eh.Owner}) vor der Tuer von P{geb.Owner}: "
                    + $"Tuer {(zuFremd ? "gesperrt" : "OFFEN")}, Schritt darauf "
                    + $"{(schrittFremd ? "MOEGLICH" : "verwehrt")}: "
                    + $"{(zuFremd && !schrittFremd ? "richtig" : "FALSCH")}");

        // 2. dieselbe Lage, aber das Gebaeude gehoert ihr -> die Tuer bleibt auf
        geb.Owner = 0;
        bool zuEigen = Zu();
        bool schrittEigen = _nav.CanEnter(tc, tr, Simulation.NavGrid.MoveClass.Vehicle);
        sb.AppendLine($"  dieselbe Einheit, Gebaeude jetzt EIGEN: "
                    + $"Tuer {(zuEigen ? "GESPERRT" : "offen")}, Schritt "
                    + $"{(schrittEigen ? "moeglich" : "VERWEHRT")}: "
                    + $"{(!zuEigen && schrittEigen ? "richtig" : "FALSCH")}");

        // 3. Verfall: der Fremde faehrt weg -> die Tuer geht wieder auf
        geb.Owner = 1;
        Zu();                                   // erst wieder sperren
        eh.Col = merkC; eh.Row = merkR;         // und dann wegfahren
        bool wiederAuf = !Zu();
        sb.AppendLine($"  der Fremde faehrt weg: Tuer {(wiederAuf ? "wieder offen" : "BLEIBT ZU")}: "
                    + $"{(wiederAuf ? "richtig" : "FALSCH — der Stempel verfaellt nicht")}");

        // 4. der Gegenschalter muss alles zurueckdrehen
        eh.Col = tc; eh.Row = tr + 1;
        Simulation.NavGrid.TuersperreAlt = true;
        TuersperreTakt();
        bool altOffen = _nav.CanEnter(tc, tr, Simulation.NavGrid.MoveClass.Vehicle);
        Simulation.NavGrid.TuersperreAlt = false;
        sb.AppendLine($"  mit --tuersperre-alt: Schritt auf die fremde Tuer "
                    + $"{(altOffen ? "moeglich" : "VERWEHRT")}: "
                    + $"{(altOffen ? "richtig" : "SCHALTER WIRKT NICHT")}");

        sb.AppendLine($"  Flanken: gesperrt {TuerGesperrtFlanken}x, freigegeben {TuerFreiFlanken}x");

        eh.Col = merkC; eh.Row = merkR; eh.Owner = merkO; geb.Owner = merkGO;
        _nav.TuerSperre(tc, tr, false);

        bool alles = zuFremd && !schrittFremd && !zuEigen && schrittEigen && wiederAuf && altOffen;
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>
    /// <c>--einfahrzeiger-probe</c> — <b>zeigt die Maus ueber der Tuer den
    /// Einfahrzeiger?</b> (06.09.2026, zu <c>@0x4323E6</c>.)
    ///
    /// <para>⚠ Und, weil mich das heute zweimal erwischt hat: der Lauf fragt
    /// AUCH, ob es das Zeigerbild ueberhaupt gibt. Ein Hinweis, der auf ein
    /// fehlendes Bild zeigt, faellt still auf den Systempfeil zurueck — das
    /// saehe genauso aus wie »nie gebaut«.</para></summary>
    public string EinfahrzeigerProbe()
    {
        var sb = new System.Text.StringBuilder("einfahrzeiger-probe\n");

        int bi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (b.Built == 0 || b.DoorCells.Count == 0 || !GarageTyp(b.BType)) continue;
            bi = i; break;
        }
        if (bi < 0) return sb.Append("  kein Gebaeude mit Tor (Art 1/5/6/12) auf dieser Karte").ToString();

        var geb = _entities[bi];
        int merkO = geb.Owner;
        geb.Owner = ViewPlayer;

        var tuer = CellCenter(geb.Col + geb.DoorCol, geb.Row + geb.DoorRow);
        var koerper = CellCenter(geb.Col, geb.Row);

        var aufTuer = CursorHintAt(tuer);
        bool tuerOk = aufTuer == Hint.Einfahrt;
        sb.AppendLine($"  eigenes Tor (Art {geb.BType}), Maus auf der Tuerzelle: "
                    + $"{aufTuer}: {(tuerOk ? "Einfahrzeiger, richtig" : "FALSCH")}");

        var aufKoerper = CursorHintAt(koerper);
        bool koerperOk = aufKoerper != Hint.Einfahrt;
        sb.AppendLine($"  dasselbe Gebaeude, Maus auf dem Koerper: {aufKoerper}: "
                    + $"{(koerperOk ? "kein Einfahrzeiger, richtig" : "FALSCH")}");

        geb.Owner = ViewPlayer == 0 ? 1 : 0;
        var fremd = CursorHintAt(tuer);
        bool fremdOk = fremd != Hint.Einfahrt;
        sb.AppendLine($"  dieselbe Tuer, Gebaeude jetzt FREMD: {fremd}: "
                    + $"{(fremdOk ? "kein Einfahrzeiger, richtig" : "FALSCH")}");

        geb.Owner = ViewPlayer;
        EinfahrzeigerAlt = true;
        var alt = CursorHintAt(tuer);
        EinfahrzeigerAlt = false;
        bool altOk = alt != Hint.Einfahrt;
        sb.AppendLine($"  mit --einfahrzeiger-alt: {alt}: "
                    + $"{(altOk ? "richtig" : "SCHALTER WIRKT NICHT")}");

        // ⚠ und ob das Bild ueberhaupt da ist
        bool bild = UI.GameCursors.HatBild(UI.GameCursors.Einfahrt);
        sb.AppendLine($"  Zeigerbild {UI.GameCursors.Einfahrt} in der Bank: "
                    + $"{(bild ? "da" : "FEHLT — es faellt still auf den Systempfeil zurueck")}");

        geb.Owner = merkO;
        bool alles = tuerOk && koerperOk && fremdOk && altOk && bild;
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>
    /// <c>--angriff-probe</c> — <b>laesst sich ein HERRENLOSES Gebaeude
    /// angreifen?</b> (06.09.2026, aus der Missionssperre in Kampagne 4.)
    ///
    /// <para>⚠ Beide Richtungen und der Gegenschalter: das herrenlose Ziel muss
    /// gehen, ein EIGENES muss weiter abgewiesen werden, und
    /// <c>--angriff-nur-feinde</c> muss den alten Stand zurueckbringen. Ohne
    /// die zweite Haelfte waere ein Bau gruen, der einfach jedes Ziel
    /// durchlaesst.</para></summary>
    public string AngriffProbe()
    {
        var sb = new System.Text.StringBuilder("angriff-probe\n");

        int gi = -1, ei = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (gi < 0 && (b.Owner is < 0 or > 7)) gi = i;
            if (ei < 0 && b.Owner == ViewPlayer) ei = i;
        }
        if (gi < 0) return sb.Append("  kein herrenloses Gebaeude auf dieser Karte").ToString();

        int ui = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead) continue;
            u.Owner = u.Team = ViewPlayer;
            if (CanFight(u)) { ui = i; break; }
        }
        if (ui < 0) return sb.Append("  keine eigene Einheit, die schiessen kann").ToString();

        _sel.Clear(); _sel.Add(ui); _selected = ui;
        // ⚠ OHNE NEBEL greifen: ein kopfloser Lauf hat nach acht Sekunden die
        // halbe Karte noch nicht erkundet, und im Nebel trifft `Pick` nichts.
        // Das ist eine Eigenschaft des LAUFS, nicht des Angriffs — derselbe
        // Hinweis steht schon beim Gebaeudefenster-Lauf.
        bool nebelVor = PickOhneNebel;
        PickOhneNebel = true;
        var ziel = _entities[gi];
        sb.AppendLine($"  Ziel: {BuildingTypeName(ziel.BType)} Platz {ziel.Slot}, Besitzer {ziel.Owner}");

        // ⚠ AUF DIE MITTE DES RUMPFRECHTECKS, nicht auf die Zellmitte: Pick
        // prueft `BodyRect(e).HasPoint(p)`, und der Ankerpunkt eines Gebaeudes
        // liegt nicht darin. Derselbe Hinweis steht schon beim
        // Gebaeudefenster-Lauf — ich bin trotzdem hineingelaufen, und der Lauf
        // meldete »ABGEWIESEN«, obwohl der Bau stimmte.
        // ⚠ Erst fragen, WO es scheitert, statt zu raten. Drei Tore, drei Zahlen.
        var punkt = BodyRect(ziel).GetCenter();
        int getroffen = Pick(punkt);
        var w = _entities[ui];
        sb.AppendLine($"  Tore: Pick bei ({punkt.X:0},{punkt.Y:0}) -> {getroffen} "
                    + $"(Ziel waere {gi}), CanFight {CanFight(w)}, "
                    + $"IstAngriffsziel {(getroffen >= 0 ? IstAngriffsziel(w, _entities[getroffen]).ToString() : "-")}, "
                    + $"Auswahl {_sel.Count}, Angreifer P{w.Owner}");
        sb.AppendLine($"  Ziel im einzelnen: IsProp {ziel.IsProp}, Dead {ziel.Dead}, "
                    + $"HpMax {ziel.HpMax}, Untergestellt {Untergestellt(ziel)}, "
                    + $"Ukol {ziel.Ukol}, Owner {ziel.Owner}, "
                    + $"AngriffNurFeinde {AngriffNurFeinde}");

        int vor = AngriffAufHerrenlos;
        bool herrenlos = PostAttack(BodyRect(ziel).GetCenter());
        bool herrenlosOk = herrenlos && AngriffAufHerrenlos == vor + 1;
        sb.AppendLine($"  Angriffsbefehl auf das herrenlose Gebaeude: "
                    + $"{(herrenlos ? "abgesetzt" : "ABGEWIESEN")}, gezaehlt "
                    + $"{AngriffAufHerrenlos - vor}x: {(herrenlosOk ? "richtig" : "FALSCH")}");

        // ⭐⭐ UND JETZT DURCH DEN BEHANDLER. Genau hier ist der erste Anlauf
        // gescheitert: der Absender liess das Ziel durch, der Behandler wies es
        // wieder ab, und der Lauf war trotzdem gruen, weil er nur den Absender
        // gefragt hatte. »Der Befehl ist abgesetzt« ist nicht »die Einheit hat
        // ein Ziel«.
        int offenVor = Commands.Pending, angewVor = BefehleAngewendet[1];
        int takte = 0;
        for (int t = 0; t < 8 && Commands.Pending > 0; t++) { CommandTick(); takte++; }
        sb.AppendLine($"  Ring: offen vor {offenVor}, nach {Commands.Pending}, "
                    + $"{takte} Takte, angewendet {BefehleAngewendet[1] - angewVor}"
                    + (AngriffAbgewiesen.Length > 0 ? $", abgewiesen weil: {AngriffAbgewiesen}" : ""));
        bool zielGesetzt = w.Target == gi && w.Ordered;
        sb.AppendLine($"  nach dem Behandler: Ziel der Einheit = {w.Target} "
                    + $"(erwartet {gi}), Ordered {w.Ordered}: "
                    + $"{(zielGesetzt ? "angekommen, richtig" : "NICHT ANGEKOMMEN — der Behandler wirft ihn weg")}");

        bool eigenOk = true;
        if (ei >= 0)
        {
            var mein = _entities[ei];
            bool eigen = PostAttack(BodyRect(mein).GetCenter());
            eigenOk = !eigen;
            sb.AppendLine($"  Gegenprobe, EIGENES Gebaeude (Platz {mein.Slot}, P{mein.Owner}): "
                        + $"{(eigen ? "ABGESETZT — falsch" : "abgewiesen, richtig")}");
        }
        else sb.AppendLine("  kein eigenes Gebaeude auf dieser Karte — Gegenprobe entfaellt");

        AngriffNurFeinde = true;
        bool alt = PostAttack(BodyRect(ziel).GetCenter());
        AngriffNurFeinde = false;
        bool altOk = !alt;
        sb.AppendLine($"  mit --angriff-nur-feinde: {(alt ? "geht trotzdem — SCHALTER WIRKT NICHT" : "wieder abgewiesen, richtig")}");

        // ⚠ Und die andere Seite: die PLATZHALTER muessen weiter ohne Energie
        // bleiben. Ohne diese Zeile waere ein Bau gruen, der einfach jedem
        // Besitzer-255-Satz Energie gibt — auch den 600 InitN-Platzhaltern.
        int echt = 0, leer = 0;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp) continue;
            if (b.Owner >= 0) continue;
            if (b.HpMax > 0) echt++; else leer++;
        }
        sb.AppendLine($"  herrenlose Gebaeude auf dieser Karte: {echt} mit Energie, "
                    + $"{leer} ohne (Platzhalter)");

        PickOhneNebel = nebelVor;
        bool alles = herrenlosOk && zielGesetzt && eigenOk && altOk;
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
