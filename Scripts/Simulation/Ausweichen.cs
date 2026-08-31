using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>»GEH MIR AUS DEM WEG« — das dritte Stück der Wegplanung.</b>
///
/// <para>Gebaut am 30.08.2026. Es fehlte seit dem 23.08., und der Einchecker
/// von damals hat es beim Namen genannt: die Suchkarte des Originals ist
/// richtig gelesen — <i>»der Nachbau ist schlechter, solange ihm das dritte
/// Stück fehlt: ein wartender Fahrer muss ausweichen oder neu planen«</i>.
/// Gemessen war das eindeutig: mit der neuen Suchkarte fuhren 96 Einheiten auf
/// map_04 in 120 s nur noch <b>260 Zellen statt 2901</b>. Sie krochen, weil
/// jeder Weg sofort durch einen Nachbarn führte und dort auf eine Einheit
/// wartete, die selbst wartete.</para>
///
/// <para><b>Die Kette des Originals, jetzt ganz gelesen:</b></para>
/// <code>
///   1. Der Fahrer trifft auf eine besetzte Zelle -> Can_go @0x4055D0 gibt 1
///   2. Can_go ruft »geh mir aus dem Weg« @0x404D20 (21 der 22 Rufer)
///   3. Der Blockierer setzt UKOL := 3, AKCE := Richtung und meldet 1
///   4. Der ukol-3-Arm @0x408E45 faehrt ihn im naechsten Takt EINEN Schritt weg
///   5. Der Fahrer kommt durch
/// </code>
///
/// <para><b>Schritt 2 bis 4 gab es bei uns nicht.</b> Wir haben gewürfelt und
/// den FAHRER neu geplant; der Blockierer wurde nie gefragt.</para>
///
/// <para><b>@0x404D20 — wer weicht aus</b> (berichte/revier1.md §6, hier nur
/// der Fahrzeugzweig):</para>
/// <code>
///   +0x0F == 0xAB          -> nein   ; dieser Antrieb weicht nie aus
///   rand() % 50 == 13      -> nein   ; 2 % blosse Verweigerung, VOR allem anderen
///   Gattung (+0x0A) > 2    -> nein   ; Schiffe weichen nicht aus
///   POHYB (+0x04) != 0xFF  -> ja     ; faehrt schon
///   OTACIM (+0x16) != 0    -> ja     ; dreht schon
///   UKOL == 4 (Angriff)    -> nein   ; bleibt stehen
///   UKOL == 2 und AKCE == 0-> ja
///   UKOL != 0              -> nein   ; (und bei UKOL 3: UKOL := 0)
///   sonst: UKOL := 3 ; AKCE := Richtung ; ja
/// </code>
///
/// <para><b>@0x408E45 — der Ausweichschritt.</b> Drei Versuche; je Versuch wird
/// aus der Tafel <c>0x4F5B10</c> (drei Byte je Richtung) ein Kandidat gezogen,
/// der Index ist <c>(rand() + versuch) % 3</c>. Die Zelle muss auf der Karte
/// liegen, ihre imap muss <c>0xFFFE</c> sein und ihr Lagenbyte ungleich 99.
/// Dann ein Schritt dorthin.</para>
///
/// <para><b>Die Tafel selbst</b>, aus der EXE gelesen — je Fahrtrichtung des
/// Fahrers die drei Ausweichrichtungen, und das Muster ist SEITWÄRTS:</para>
/// <code>
///   akce 0 (Sued)  -> 2 6 0   (West, Ost, Sued)
///   akce 1         -> 2 0 0     akce 5         -> 4 6 6
///   akce 2 (West)  -> 4 0 2   (Nord, Sued, West)
///   akce 3         -> 2 4 4     akce 6 (Ost)   -> 0 4 6
///   akce 4 (Nord)  -> 2 6 4     akce 7         -> 6 0 6
/// </code>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>UKOL 3 — AUSWEICHEN. Der Arm dazu ist <c>0x408E45</c>;
    /// gesetzt wird er in »geh mir aus dem Weg« <c>@0x404D20</c>.</summary>
    public const int UkolAusweichen = 3;

    /// <summary>Die Ausweichtafel <c>0x4F5B10</c>, drei Byte je Richtung —
    /// Zeile für Zeile aus der EXE gelesen, nicht gerechnet.</summary>
    private static readonly int[,] AusweichTafel =
    {
        { 2, 6, 0 }, { 2, 0, 0 }, { 4, 0, 2 }, { 2, 4, 4 },
        { 2, 6, 4 }, { 4, 6, 6 }, { 0, 4, 6 }, { 6, 0, 6 },
    };

    /// <summary>Die 2-%-Verweigerung: <c>rand() % 50 == 13</c> @0x404D20.
    /// ⚠ Sie sitzt VOR allem anderen — auch eine ausweichbereite Einheit sagt
    /// in einer von fünfzig Anfragen nein.</summary>
    private const int AusweichVerweigerung = 50;

    /// <summary>
    /// <c>--ausweichen</c> — den Blockierer fragen, statt den Fahrer neu planen
    /// zu lassen.
    ///
    /// <para>⚠⚠ <b>STANDARDMAESSIG AUS — und seit dem 31.08.2026 ist das
    /// GEMESSEN, nicht mehr blosse Vorsicht.</b> Vier Keime auf map_DM_4, 60 s,
    /// auf dem trockengelegten Pruefstand (kein Absturz in 10 Laeufen):</para>
    /// <code>
    ///   Keim   ohne Ausweichen          mit Ausweichen
    ///    7     Ziel 21  Fort 41,7  1572   Ziel 19  Fort 40,8  1567
    ///   11     Ziel 19  Fort 40,5  1914   Ziel 18  Fort 39,3  1543
    ///   23     Ziel 17  Fort 39,3  1650   Ziel 19  Fort 40,5  1545
    ///   41     Ziel 20  Fort 41,7  1652   Ziel 21  Fort 41,0  1502
    ///   ---------------------------------------------------------
    ///   Mittel Ziel 19,25 Fort 40,8 1697   Ziel 19,25 Fort 40,4 1539
    /// </code>
    ///
    /// <para><b>Der Zielerfolg ist auf die Nachkommastelle gleich</b> (19,25 zu
    /// 19,25) — daran haette man es nicht entschieden, und die »18 statt 21«
    /// vom 30.08. waren tatsaechlich nur die Streuung der Schwellengroesse, vor
    /// der der Einchecker vom 23.08. gewarnt hat. Entschieden hat die
    /// <b>Fahrleistung: 1697 gegen 1539 Zellen, und in 4 von 4 Laeufen in
    /// dieselbe Richtung</b>. Vier von vier ist kein Rauschen.</para>
    ///
    /// <para>⭐ <b>Und die Lastzahl sagt, WARUM — sie ist der eigentliche
    /// Befund.</b> Im Lauf mit Keim 41: <b>26886 gefragt, 24340 zugesagt,
    /// 26 Schritte getan</b>. Auf jeden echten Ausweichschritt kommen
    /// <b>tausend Zusagen</b>. Die kommen fast alle aus dem Zweig »faehrt schon
    /// / dreht schon« weiter oben — der sagt dem Fahrer nicht <i>ich gehe dir
    /// aus dem Weg</i>, sondern <i>warte, ich bin gleich weg</i>. <b>Unser
    /// Ausweichen ist im Betrieb zu 99,9 % nicht Ausweichen, sondern Warten</b>
    /// — und Warten ist als eigene Konfiguration gemessen der schlechteste Bau
    /// von allen (Ziel 4, Fortschritt 20,2 gegen 21 und 41,7).</para>
    ///
    /// <para>⭐⭐⭐ <b>31.08.2026 — DER AUFRUFER IST GELESEN, UND ER HAT ZWEI
    /// AUSSTIEGE, DIE UNS BEIDE FEHLEN.</b> Alle Rufe auf <c>Can_go</c> laufen
    /// ueber den Linker-Thunk <c>0x4018FC</c> (eine Adressvollerhebung auf
    /// <c>0x4055D0</c> findet genau EINEN Rufer — den Thunk; die fuenf echten
    /// haengen an ihm). Der Fahrer ist <c>@0x408B88</c>, und er verzweigt
    /// <b>dreifach</b>, nicht zweifach:</para>
    /// <code>
    ///   0x408B90  test eax, eax
    ///   0x408B92  je   0x408BAB      ; 0
    ///   0x408B94  cmp  eax, 1
    ///   0x408B97  je   0x408D81      ; 1
    ///   0x408B9D  cmp  eax, 2
    ///   0x408BA0  je   0x408E1C      ; 2
    /// </code>
    ///
    /// <para><b>Was die drei Werte sind</b> — abgelesen am Rueckgabepunkt des
    /// Rad-/Kettenarms <c>@0x405B7C…0x405BBD</c> (Hover-Arm <c>@0x405897</c>
    /// gegengeprueft, gleiches Muster):</para>
    /// <code>
    ///   imap 0xFFFE oder 0xFFFD          -> mov eax, 2
    ///   sonst: 0x4054D0 gibt 0           -> xor eax, eax        (gar nicht erst gefragt)
    ///   sonst: 0x404D20 fragen, dann
    ///          cmp eax,1 / sbb eax,eax / inc eax
    ///                       NEIN(0) -> 0 ; JA(>=1) -> 1
    /// </code>
    ///
    /// <para><b>Und was der Fahrer damit tut:</b></para>
    /// <code>
    ///   2 = frei          @0x408E1C  POHYB := Richtung ; DALSI_SMER++ ; Schritt
    ///   1 = ZUGESAGT      @0x408D81  rand() % 60 != 0 -> raus (warten)
    ///                                sonst: Gattungstafel 0x40A220 -> NEU PLANEN
    ///   0 = NEIN/hart zu  @0x408BAB  Geduld(+0x1C)-- ; != 0 -> raus (warten)
    ///                                sonst Geduld := 40 + rand()%20
    ///                                dann Gattungstafel 0x40A208 -> NEU PLANEN
    /// </code>
    /// <para>(Beide Gattungstafeln fuehren ueber <c>0x401726</c> auf
    /// <c>0x4D32C0</c> — dasselbe Revier wie <c>0x4D1363</c> und
    /// <c>0x4D3810</c>, also die Wegsuche.)</para>
    ///
    /// <para>⭐ <b>Damit ist die Messung oben erklaert und die Lesung
    /// gerettet.</b> Das Original wartet an dieser Stelle <b>nie unbegrenzt</b>:
    /// bei einer Zusage wuerfelt es jeden Takt <b>1/60</b> auf Neuplanung, bei
    /// einer Absage laeuft ein <b>harter Zaehler von 40–59 Takten</b>. <b>Unser
    /// Nachbau hat keins von beidem</b> — er wartet ewig, und darum sind aus
    /// 24340 Zusagen 24340 Dauerblockaden geworden. Nicht die Mechanik ist
    /// falsch, sondern ihr <b>fehlender Ausstieg</b>.</para>
    ///
    /// <para>⚠ <b>Was noch fehlt, bevor der Schalter an darf</b> (Bauauftrag,
    /// nicht Forschung): (a) der 1/60-Ausstieg im Zugesagt-Fall, (b) der
    /// Geduldszaehler <c>+0x1C</c> im Absage-Fall statt unseres sofortigen
    /// Neuplanens, (c) <c>0x4054D0</c> ist noch nicht gelesen — es entscheidet,
    /// wann gar nicht erst gefragt wird. Bis das steht, bleibt der Schalter
    /// aus.</para></summary>
    public static bool AusweichenAn;

    /// <summary>Bequemlichkeit: der alte Name, damit die Abfragen unten lesbar
    /// bleiben.</summary>
    private static bool KeinAusweichen => !AusweichenAn;

    /// <summary>Wie oft gefragt, wie oft zugesagt, wie oft ein Schritt wirklich
    /// getan wurde. ⚠ Ohne die drei Zahlen ist »das Ausweichen tut nichts«
    /// nicht von »es wurde nie gefragt« zu unterscheiden.</summary>
    public int AusweichGefragt, AusweichZugesagt, AusweichSchritte, AusweichEng;

    /// <summary>
    /// <b>Den Blockierer fragen</b> — der Nachbau von <c>0x404D20</c>.
    /// </summary>
    /// <param name="blockIdx">Wer im Weg steht.</param>
    /// <param name="richtung">Die Fahrtrichtung des Fahrers, 0…7 nach der
    /// Richtungstafel <c>0x4F5AF0</c>. Sie wird zu <c>AKCE</c>.</param>
    /// <returns>true, wenn der Blockierer ausweicht oder ohnehin gleich
    /// weiterfährt.</returns>
    private bool AusweichenAnfragen(int blockIdx, int richtung)
    {
        if (KeinAusweichen) return false;
        if (blockIdx < 0 || blockIdx >= _entities.Count) return false;
        var b = _entities[blockIdx];
        if (b.Dead || b.IsBuilding || b.IsProp) return false;
        AusweichGefragt++;

        // ⚠ Die Verweigerung steht VOR allem anderen — genau wie im Original.
        if (Simulation.Determinism.Roll(AusweichVerweigerung) == 13) return false;

        // Gattung > 2 weicht nie aus (Schiffe). ⚠ Unsere Entsprechung ist
        // GameUnitType; die Gattung 3 kommt auf allen 54 Karten zehnmal vor.
        if (b.GameUnitType > 2) return false;
        if (!b.Mobile || b.DugIn) return false;

        // »faehrt schon« / »dreht schon« -> der Fahrer soll einfach warten.
        if (b.Path is { Count: > 0 } || b.Orders.Count > 0) { AusweichZugesagt++; return true; }

        if (b.Target >= 0) return false;                 // UKOL 4: greift an
        if (b.Ukol == UkolAusweichen) { b.Ukol = UkolFrei; return false; }
        if (b.Ukol != UkolFrei) return false;

        b.Ukol = UkolAusweichen;
        b.AusweichRichtung = richtung;
        AusweichZugesagt++;
        return true;
    }

    /// <summary>
    /// <b>Der Ausweichschritt</b> — der Nachbau des ukol-3-Arms
    /// <c>@0x408E45</c>. Einmal je Spieltakt, wie der Einheitentakt des
    /// Originals.
    /// </summary>
    private void AusweichTakt()
    {
        if (KeinAusweichen || _nav == null) return;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Ukol != UkolAusweichen) continue;
            if (e.Dead || !e.Mobile)
            {
                e.Ukol = UkolFrei;
                continue;
            }
            // Wer inzwischen wieder einen Befehl hat, weicht nicht mehr aus.
            if (e.Path is { Count: > 0 } || e.Orders.Count > 0)
            {
                e.Ukol = UkolFrei;
                continue;
            }

            int akce = e.AusweichRichtung & 7;
            bool getan = false;
            for (int versuch = 0; versuch < 3 && !getan; versuch++)
            {
                int r = (Simulation.Determinism.Roll(3) + versuch) % 3;
                int idx = AusweichTafel[akce, r] & 7;
                var d = Simulation.NavGrid.UrDirs[idx];
                int c = e.Col + d.X, w = e.Row + d.Y;
                if (!_nav.InBounds(c, w)) continue;
                // ⚠ Das Original prueft hier die imap auf 0xFFFE und das
                // Lagenbyte auf != 99 — also FREI und keine Bruecke. Unsere
                // Entsprechung ist `IsFree` fuer die eigene Bewegungsklasse;
                // sie nimmt zusaetzlich den Rumpf mit, was das Original an
                // dieser Stelle nicht tut (es kennt hier nur die eine Zelle).
                if (!_nav.IsFree(c, w, e.Move, i)) continue;

                _nav.ClearOccupant(e.Col, e.Row, i);
                if (e.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, i);
                e.Reserved = null;
                e.Col = c; e.Row = w;
                e.Elev = ElevOf(c, w);
                e.Pos = BodyCenterAt(e, c, w);
                e.Footprint = CellRect(_ox, _oy, c, w, e.Elev);
                e.Facing = idx;
                _nav.SetOccupant(c, w, i, e.Infantry >= 0);
                AusweichSchritte++;
                getan = true;
            }
            if (!getan) AusweichEng++;      // alle drei Kandidaten zu
            e.Ukol = UkolFrei;              // der Auftrag ist damit erledigt
        }
    }

    /// <summary>Die Meldezeile. Leer, solange niemand gefragt hat.</summary>
    public string AusweichLine()
        => AusweichGefragt == 0 ? ""
         : $"ausweichen: {AusweichGefragt} gefragt, {AusweichZugesagt} zugesagt, "
         + $"{AusweichSchritte} Schritte getan, {AusweichEng}x war es rundum zu; "
         + $"{GiveWayGewartet}x vor einer besetzten Zelle GEWARTET statt neu geplant";
}
