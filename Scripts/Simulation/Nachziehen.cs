namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DAS NACHZIEHEN AUF DIE ENTWURFSWERTE</b> — <c>0x4B3AF0</c> (F <c>0x4B3420</c>),
/// gerufen über die Schleife <c>0x4B3CD0</c> (F <c>0x4B3600</c>).
///
/// <para><b>Warum es das gibt.</b> Seine Meldung aus Kampagne 15: »Die gegnerische KI schießt
/// mit ihrem Flammenwerfer viel schneller als meiner.« Gemessen: sein Flammenwerfer (von der
/// KARTE, <c>+0x3D</c> = 0) lud 1,10 s nach, der gebaute der KI (aus dem ENTWURF, <c>+0x3D</c>
/// = 1) 0,06 s. Wir haben den Rohwert der Kartendatei übernommen — das Original tut das nie.
/// </para>
///
/// <para><b>Was gelesen ist.</b> Zwei Lesungen, unabhängig voneinander:
/// <c>berichte/nachladezeit-opus.md</c> (15.09.2026) und die Gegenlesung
/// <c>berichte/nachladezeit-nachziehen-fable.md</c> (17.09.2026, alle neun Behauptungen
/// bestätigt, nichts widerlegt, größere Stichproben: <c>+0x3D</c> in den Spielständen
/// 1420/1420, Entwurf <c>+0x2B</c> = Σ Bauteile 1847/1847). Nach dem Laden einer <c>.CWM</c>
/// zieht das Original <b>jede lebende Einheit der Gattung 0/1 mit UKOL &lt; 100 bedingungslos
/// auf ihren Entwurf nach</b> — dreizehn Felder, ohne zu prüfen, ob in der Datei etwas steht.
/// Der Kartenwert wird nie gelesen.</para>
///
/// <para><b>⚠ Die Weiche Karte/Spielstand</b> ist das Kopfbyte 3 der Datei (<c>0x41E1D5</c> →
/// <c>0x41E6AC cmp 2</c>): <c>.CWM</c> 23/23 = 1, <c>.DM</c> 13/13 = 2, und Zweig 2 springt bei
/// <c>0x41EE04</c> (F <c>0x41DFC3</c>) hinter die Vorbereitung UND hinter mission_init. Ein
/// Spielstand trägt den schon gezogenen Wert und wird <b>nicht</b> angefasst — deshalb ruft
/// <see cref="MapViewer"/> das hier nur, wenn kein Spielstand aufgelegt wurde.</para>
///
/// <para><b>⚠ Es sind DREI Läufe beim Kartenstart, und der LETZTE zählt</b> (§5.1 der
/// Gegenlesung): (1) <c>0x488300</c> aus mission_init, (2) je KI-Aufwertung, (3) am Ende von
/// »Enemy upgrade« <c>0x437E6C</c> — also NACH place_unit und nach den mitgenommenen
/// Einheiten, die per 78-Byte-Kopie noch mit den Werten der VORMISSION aufgestellt werden.
/// Bei uns steht darum der eine Aufruf, den wir brauchen, hinter <c>PlaceCarriedUnits</c>.</para>
///
/// <para><b>⚠ Die Spieler rechnen aus VERSCHIEDENEN Bauteilblöcken</b> (§5.2/5.3):
/// <c>0x4B23C0</c> kopiert Block 0 auf alle sieben anderen, dann lädt »Enemy upgrade«
/// <c>PARTS.CWD</c> komplett neu und stellt nur Block 0 zurück. Endstand: KI = PARTS.CWD +
/// ihre Aufwertungen, Spieler = sein Block mit seiner Forschung. Genau das bildet
/// <c>DesignMath.Compute(waffe, fahrwerk, ausrüstung, SPIELER)</c> ab — der Entwurf wird hier
/// deshalb je Besitzer neu gerechnet und nicht aus der spielerlosen Bauliste genommen.</para>
///
/// <para><b>⭐⭐⭐ NICHT GEBAUT, UND DAS IST EINE ENTSCHEIDUNG — keine Lücke.</b> Die
/// Schiffsfassung <c>0x4B3D70</c> (F <c>0x4B36A0</c>, Gattung 4/5) bleibt aus. Bis zum
/// 19.09.2026 stand hier »ausdrücklich offen, ob <c>+0x43</c> beim Schiff der Entwurf 0…9 ist«.
/// Das ist seither <b>beantwortet</b> (<c>berichte/schiffsentwurf-fable.md</c>, Gegenlesung an
/// beiden EXE) — und die Antwort macht den ganzen Zweig <b>wirkungslos</b>. Wer ihn dennoch
/// nachträgt, gewinnt nichts und riskiert, die Energie fremder Schiffe zu verstellen. Die vier
/// Belege, damit das nicht in einem halben Jahr neu aufgerollt wird:</para>
///
/// <list type="number">
///   <item><b><c>+0x43</c> IST der Schiffsentwurf 0…9.</b> Einziger Schreiber für Gattung 4/5 ist
///   der Werft-Befehl <c>0x4B2B20</c> (@<c>0x4B2DF2</c>, F <c>0x4B2725</c>) — dasselbe Byte, mit
///   dem er vorher die sec119-Zeile <c>42·(Entwurf + 10·Spieler)</c> gebildet hat. Zweiter,
///   unabhängiger Leser mit derselben Rechnung: <c>0x4508A0</c> (@<c>0x450A48</c>). Nullmodell:
///   <b>137 von 137</b> Kartenschiffen tragen 0…9 (gleichverteilt wären ~5,4), die 3012
///   Landsätze tragen 47…194 und keiner ≤ 9.</item>
///
///   <item><b>Der Zweig LÄUFT in der Kampagne</b> — drei Rufer von <c>0x4B3CD0</c>, darunter
///   <c>0x437E6C</c> bei JEDEM Levelstart. Er ist also kein toter Code, sondern er tut nichts.</item>
///
///   <item><b>Für die meisten Schiffe kann er nichts tun, weil die Zeile schon stimmt.</b>
///   <c>0x4B23C0</c> rechnet die Schiffszeilen beim Missionsstart aus den Bauteilen
///   (<c>0x4B25E0</c>: Energie <c>+0x1D</c> = <c>Rumpf.+0x0E + Waffe.+0x0E</c>, nachgebaut in
///   Simulation/Schiffszeilen.cs). Das Nachziehen schriebe danach jedem unbeschädigten Schiff
///   genau die Zahl hinein, die es hat. Nullmodell: <b>1040 von 1040</b> sec119-Zeilen aller 13
///   Spielstände, und <b>172 von 210</b> Kartenschiffen.</item>
///
///   <item><b>⚠⚠ UND FÜR DIE ÜBRIGEN 38 WÄRE ER SCHÄDLICH</b> — der eigentliche Grund, und er
///   kam erst am 19.09. abends heraus (§8 des Berichts, an den Originaldateien BEIDER CDs
///   gemessen; die erste Lesung hatte nur CD1 und meldete darum fälschlich 137/137). Die
///   Kampagnenkarten der zweiten CD sind an dieser Stelle <b>ungepflegt</b>: von 210 Schiffen
///   tragen <b>26</b> in <c>+0x43</c> etwas anderes als im Wort <c>+0x3E</c> (17.CWM 12×0,
///   20.CWM 1×0, 22.CWM 12×0 und <b>1×103</b>), weitere <b>12</b> liegen in beiden Feldern um
///   eins daneben. Das Wort <c>+0x3E</c> passt bei <b>210 von 210</b>, bei Landeinheiten stimmen
///   ohnehin <b>4254 von 4254</b> überein. Und <c>0x4B3D70</c> hat <b>keinen Wächter</b>
///   (<c>cl = +0x43</c> @<c>0x4B3D96</c>, Zeile <c>(spieler·10 + cl)·42</c>): der Zweig zöge
///   K17/K20/K22 von 125 auf 90 herunter (Angriff 6 statt 18), K25/K31 von 175 auf 110, K26 von
///   210 auf 175 — und die <c>103</c> auf Karte 22 läse <c>0x53002A</c>, <b>1386 Byte hinter dem
///   Tafelende</b>, wo 42 Nullbytes liegen: ein Schiff mit <b>0 Trefferpunkten</b>.</item>
///
///   <item><b>Und ein Schiff wird nie besser.</b> Das Forschungsangebot umfasst nur die Bauteile
///   1…49 (<c>0x4AA9EC cmp si,0x32</c>), die KI-Aufwertung nur 1…19 und 160…174 — Schiffsrümpfe
///   liegen bei 150…158, Schiffswaffen bei 140…145, beide außerhalb. Und <c>0x4AAA80</c> rechnet
///   nach einer fertigen Forschung ausdrücklich nur die LANDzeilen neu (<c>0x4B24B0</c>), nie die
///   Schiffszeilen. Es gibt in diesem Spiel keinen Weg, der die Energie eines Schiffes
///   verändert — auch nicht über die Waffe.</item>
/// </list>
///
/// <para><b>⚠ Die Gegenprobe</b> (denn »passiert nie« ohne sie ist keine Aussage): herstellbar
/// ist der Fall nur von außen. Ändert man in <c>PARTS.CWD</c> die Energie eines Rumpfes, hebt die
/// nächste Mission jedes unbeschädigte Schiff dieses Typs an — auch die gegnerischen. In einer
/// unveränderten <c>.CWM</c> tritt er nicht ein.</para>
///
/// <para><b>Was daraus statt des Zweiges gebaut wurde:</b> die ZEILENRECHNUNG
/// (Simulation/Schiffszeilen.cs) — die war nämlich wirklich falsch und im Spiel spürbar — und
/// der Prüfstand <c>--schiffsentwurf-check</c> (Simulation/SchiffsentwurfCheck.cs). Er hält die
/// Zeilen gegen die unabhängig aus <c>PARTS.CWD</c> gerechneten Energien und meldet die Schiffe,
/// deren <c>+0x43</c> nicht zu ihrer Zeile passt — die 38 aus Beleg 4. Fällt er durch, ist nicht
/// dieser Zweig zu bauen, sondern die Zeilenrechnung zu berichtigen.</para>
///
/// <para><b>⚠ Wenn ihn doch einmal jemand baut</b>, ist vorher eine Entscheidung zu treffen, die
/// dem SPIELER gehört: <b>originaltreu</b> <c>+0x43</c> lesen und den Datenfehler mitsamt Hp 0
/// nachbilden, oder <b>datentreu</b> das Wort <c>+0x3E</c> nehmen (210/210) und bewusst
/// abweichen. Beides ist begründbar, keines ist selbstverständlich — und solange nichts gebaut
/// ist, muss die Frage nicht beantwortet werden.</para>
///
/// <para>⚠ Schiffe werden hier weiter <b>gezählt</b>, damit die Zeile sichtbar macht, dass sie
/// bewusst ausgelassen sind.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--nachziehen-aus</c> — DER GEGENSCHALTER: die Kartenwerte bleiben roh
    /// stehen, also unser Stand bis zum 17.09.2026. Das Nullmodell zu
    /// <c>--nachladezeit-check</c>: damit muss der Prüfstand DURCHFALLEN.</summary>
    public static bool NachziehenAus;

    /// <summary>Wie viele Sätze der letzte Lauf angefasst hat, und wie viele davon vorher
    /// einen anderen Wert trugen als der Entwurf. Der zweite Zähler ist der interessante:
    /// er ist das Maß dafür, wie weit die Kartendatei vom Entwurf abweicht.</summary>
    public int NachgezogenGesamt, NachgezogenAbweichend, NachziehenOhneEntwurf, NachziehenSchiffe;

    /// <summary>Die Abweichungen des letzten Laufs, Feld für Feld — für den Prüfstand und
    /// für die Zeile, die der Lauf ausgibt. Begrenzt, damit eine große Karte die Ausgabe
    /// nicht flutet.</summary>
    private readonly List<string> _nachziehProtokoll = new();

    /// <summary>
    /// Der Lauf über alle Plätze — <c>0x4B3CD0</c>. Auswahl wörtlich: Zustand
    /// <c>+0x09 != 0xFF</c> (lebt), <c>UKOL &lt; 100</c> (<c>cmp cl,0x64; jae</c>
    /// @<c>0x4B3CFB</c>), Gattung <c>&lt;= 5</c>, und dann die Sprungtafel
    /// <c>{00 00 02 02 01 01}</c>: Gattung 0/1 → <see cref="NachziehenEiner"/>,
    /// Gattung 4/5 → die Schiffsfassung (bei uns offen), Gattung 2/3 → nichts.
    /// </summary>
    public void NachziehenNachKarte(string anlass)
    {
        NachgezogenGesamt = NachgezogenAbweichend = NachziehenOhneEntwurf = NachziehenSchiffe = 0;
        _nachziehProtokoll.Clear();
        if (NachziehenAus)
        {
            GD.Print($"nachziehen: --nachziehen-aus — die Kartenwerte bleiben roh ({anlass})");
            return;
        }
        LoadDesigns();
        foreach (var e in _entities)
        {
            if (e.Hp <= 0) continue;                  // +0x09 == 0xFF, der tote Platz
            if (e.Ukol >= 100) continue;              // 0x4B3CFB
            // ⭐ 19.09.2026 — Gattung 4/5 bleibt bewusst aus, siehe Kopf: der Zweig laeuft im
            // Original, ist aber wirkungslos, weil die Schiffszeile schon stimmt und nie besser
            // wird. Geprueft von --schiffsentwurf-check.
            if (e.GameUnitType is 4 or 5) { NachziehenSchiffe++; continue; }
            if (e.GameUnitType is not (0 or 1)) continue;   // 2/3 zieht das Original nicht nach
            NachziehenEiner(e);
        }
        GD.Print($"nachziehen ({anlass}): {NachgezogenGesamt} Einheiten auf ihren Entwurf " +
                 $"gezogen, davon {NachgezogenAbweichend} mit abweichenden Kartenwerten" +
                 (NachziehenOhneEntwurf > 0 ? $"; {NachziehenOhneEntwurf} ohne Entwurfszeile" : "") +
                 (NachziehenSchiffe > 0
                     ? $"; {NachziehenSchiffe} Schiffe ausgelassen (wirkungslos, belegt — "
                     + "siehe Kopf von Nachziehen.cs und --schiffsentwurf-check)"
                     : ""));
    }

    /// <summary>
    /// <b>Die Feldtafel von <c>0x4B3AF0</c>, dreizehn Schreiber.</b> Reihenfolge und
    /// Bedingungen wörtlich aus §2 der Gegenlesung — sie ist hier wichtig: Energie, Tank und
    /// Munition vergleichen mit dem ALTEN Maximum, setzen dann das neue und klemmen erst
    /// danach.
    /// </summary>
    private void NachziehenEiner(Entity e)
    {
        int slot = e.Mark + 200 * e.Owner;            // Entwurfszeile 0x51CE20 + 46·(E + 200·S)
        if (e.Mark < 0 || !_designBySlot.TryGetValue(slot, out var d))
        {
            NachziehenOhneEntwurf++;
            return;
        }
        // ⚠ JE BESITZER neu gerechnet, nicht die Bauliste: siehe Kopf, §5.2/5.3.
        var w = Simulation.DesignMath.Compute(d.Weapon, d.Propulsion, d.Equip, e.Owner);

        int altReload = e.Reload, altAttack = e.Attack, altSpeed = e.Speed;

        // +0x08 Energie / +0x29 Energie max — 0x4B3B2E…0x4B3B89 (F 0x4B345E…0x4B34B9)
        bool energieVoll = e.Hp == e.HpMax;           // cmp bl,dl mit dem ALTEN Max
        if (energieVoll) e.Hp = w.Hp;
        e.HpMax = w.Hp;                               // 0x4B3B7B, immer
        if (e.Hp > e.HpMax) e.Hp = e.HpMax;           // unsigned, mit dem NEUEN Max

        // +0x20 Geschwindigkeit (word) — nur wenn der alte Wert > 2 ist (jle), 0x4B3B8F
        if (e.Speed > 2) e.Speed = w.Speed;

        e.Attack = w.Attack;                          // +0x26, 0x4B3BBD — immer
        e.Defence = w.Defence;                        // +0x27, 0x4B3BC9 — immer
        e.RangeMin = w.RangeMin;                      // +0x2A, 0x4B3BD5 — immer (Lowbyte +0x22)
        e.Range = w.Range;                            // +0x2B, 0x4B3BDB — immer (Lowbyte +0x24)
        e.Sight = w.Sight;                            // +0x2C, 0x4B3BE1 — immer

        // +0x2E Tank / +0x30 Tank max — 0x4B3BE7…0x4B3C1C, Vergleich VORZEICHENBEHAFTET
        bool tankVoll = e.Fuel == e.FuelMax;
        if (tankVoll) e.Fuel = w.Fuel;
        e.FuelMax = w.Fuel;                           // 0x4B3C0C, immer
        if (e.Fuel > e.FuelMax) e.Fuel = e.FuelMax;

        // +0x39 Munition / +0x3A Munition max — 0x4B3C23…0x4B3C53, unsigned
        bool munVoll = e.Ammo == e.AmmoMax;
        if (munVoll) e.Ammo = w.Ammo;
        e.AmmoMax = w.Ammo;                           // 0x4B3C45, immer
        if (e.Ammo > e.AmmoMax) e.Ammo = e.AmmoMax;

        // ⭐ +0x3D Nachladen — 0x4B3C61 (F 0x4B3591), IMMER, ohne jeden Vergleich.
        e.Reload = w.Reload;

        // NICHT angefasst (kommt in den 91 Befehlen nicht vor): +0x28 Rang, +0x4C Erfahrung,
        // +0x32 die laufende Nachladeuhr, +0x0B…+0x10 die Bauteilbytes, +0x14 UKOL, +0x43.

        NachgezogenGesamt++;
        if (altReload != e.Reload || altAttack != e.Attack || altSpeed != e.Speed)
        {
            NachgezogenAbweichend++;
            if (_nachziehProtokoll.Count < 8)
                _nachziehProtokoll.Add($"Platz {e.Slot} Sp{e.Owner} Entwurf {e.Mark} " +
                                       $"({d.Name}): Nachladen {altReload}→{e.Reload}, " +
                                       $"Angriff {altAttack}→{e.Attack}, Tempo {altSpeed}→{e.Speed}");
        }
    }

    /// <summary>
    /// <c>--nachladezeit-check</c> — der Prüfstand. Er misst nicht »sieht gut aus«, sondern:
    /// trägt nach dem Laden JEDE Einheit der Gattung 0/1 genau den Entwurfswert ihres
    /// Besitzers? Das Nullmodell ist <c>--nachziehen-aus</c>: damit muss er durchfallen.
    /// </summary>
    public string NachladezeitCheck()
    {
        var sb = new System.Text.StringBuilder("nachladezeit-check\n");
        LoadDesigns();
        int geprueft = 0, stimmt = 0, fehlt = 0;
        var schlecht = new List<string>();
        foreach (var e in _entities)
        {
            if (e.Hp <= 0 || e.Ukol >= 100) continue;
            if (e.GameUnitType is not (0 or 1)) continue;
            int slot = e.Mark + 200 * e.Owner;
            if (e.Mark < 0 || !_designBySlot.TryGetValue(slot, out var d)) { fehlt++; continue; }
            var w = Simulation.DesignMath.Compute(d.Weapon, d.Propulsion, d.Equip, e.Owner);
            geprueft++;
            if (e.Reload == w.Reload) stimmt++;
            else if (schlecht.Count < 8)
                schlecht.Add($"   Platz {e.Slot} Sp{e.Owner} Entwurf {e.Mark} ({d.Name}): " +
                             $"Einheit {e.Reload}, Entwurf {w.Reload}");
        }
        sb.AppendLine($"   Nachladezeit == Entwurf: {stimmt}/{geprueft} Einheiten " +
                      $"Gattung 0/1" + (fehlt > 0 ? $" ({fehlt} ohne Entwurfszeile)" : ""));
        foreach (var z in schlecht) sb.AppendLine(z);
        if (_nachziehProtokoll.Count > 0)
        {
            sb.AppendLine($"   Beim Laden gezogen: {NachgezogenGesamt} Einheiten, " +
                          $"{NachgezogenAbweichend} mit abweichenden Kartenwerten:");
            foreach (var z in _nachziehProtokoll) sb.AppendLine($"     {z}");
        }
        sb.AppendLine(geprueft > 0 && stimmt == geprueft
            ? "   ✅ BESTANDEN — jede Einheit trägt den Entwurfswert ihres Besitzers."
            : geprueft == 0
                ? "   ⚠ DURCHGEFALLEN — keine Einheit der Gattung 0/1 auf dieser Karte, " +
                  "der Prüfstand sagt hier nichts."
                : $"   ❌ DURCHGEFALLEN — {geprueft - stimmt} Einheiten weichen ab " +
                  "(Nullmodell --nachziehen-aus muss genau das zeigen).");
        return sb.ToString();
    }
}
