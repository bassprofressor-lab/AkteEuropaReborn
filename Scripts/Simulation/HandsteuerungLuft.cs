namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>DIE HANDSTEUERUNG LUFT</b> (20.09.2026, nach
/// <c>berichte/flughafenfenster-k20-fable.md</c> §3.4, Bauaufgabe 4).
///
/// <para>Seine Beschreibung von K20: der ANGRIFF-Knopf »bietet aber auch
/// Handsteuerung an«. Am Boden haben wir sie seit langem
/// (<see cref="Zeigermerker"/>, Menücode 5, Pfeiltasten → Befehl 1); in der
/// Luft fehlte sie ganz.</para>
///
/// <para><b>GELESEN</b> (C, F dazu benannt; alle Zahlen byte-genau aus
/// <c>adis.py</c>):</para>
/// <code>
///   Eintritt A — Taste 9 »Handsteuerung« @0x449C1B (F 0x448C27):
///       Befehl 502 mit +0x0A = 6  -> launch_aircraft mit m_uk 6
///       im Startzweig (uk 4, Uhr > 25, m_uk == 6, @0x4234B7..0x4234E3):
///           alt += 5 ; uk := 3 ; +0x0F := 1 (die Handmarke)
///           word[0x4FA0C8] := 20000 + idx      (die Maschine ist gewaehlt)
///           dword[0x502AD4] := 0x3EA           -> @0x4315D3: Zeiger AUS
///   Eintritt B — Taste H @0x433750: gewaehltes Flugzeug, m_uk nicht 3..5,
///       uk != 3  ->  Befehl 4  ->  0x4C28BA: typ &lt; 13  ->  uk := 3
///
///   Der Sender @0x433460 (F 0x4325A0), je SPIELTAKT, nur bei uk == 3:
///       +0x0A d_dir  = (Taste -> minus Taste &lt;-) * 6     VK 0x27 / 0x25
///       +0x0C d_sp   = (Taste hoch minus Taste runter),
///                      aber NUR in ungeraden Takten (dword[0x4FA240] &amp; 1)
///       +0x0E d_alt  = (Taste A minus Taste Z) * 2         VK 0x41 / 0x5A
///       +0x10 shoot  = byte[0xA182F9]                      VK 0x11 = Strg
///       Befehl 8
///
///   Der Arm — Befehl 8 @0x4C2B58 »Hand control« (F 0x4C282E):
///       @0x4C2C29  dir += d_dir ; &gt;= 360 -> −360 ; &lt; 0 -> +360   (dir ist ein WORT)
///       @0x4C2C62  sp: nur wenn sp_unten &lt; sp ODER d_sp nicht negativ;
///       @0x4C2C8F      sp += d_sp ; Deckel sp_oben (+0x0D)
///       @0x4C2CB2  alt += d_alt (BYTE-Rechnung!) ; Deckel 0x87 = 135, KEIN Boden
///       @0x4C2CC6  shoot != 0 -> Air shoot(idx, 0xFF)  (0x427090 prueft
///                  Waffenart +0x2C, +0x2B und Munition +0x16 selbst)
///
///   Austritt: H (Befehl 5) · Rechtsklick / jede Abwahl (Befehl 30)
///       -> beide rufen air_back_to_airport 0x426180 (F 0x425360):
///          uk := 1, m_uk := 2 (Landung), Sollhoehe nach Art, Zustand 0
/// </code>
///
/// <para><b>Was <c>uk 3</c> im Flugtakt tut: NICHTS.</b> AIR_RE 446 — »uk 3 hat
/// gar keinen Zweig«. Die Maschine fliegt mit <c>dir</c> und <c>sp</c>
/// geradeaus, sucht sich kein Ziel, kreist nicht und kehrt auch bei leerem Tank
/// nicht um (die Sprit-Heimkehr prüft ausdrücklich <c>uk != 3</c>, AIR_RE 88).
/// <b>Man kann sie leerfliegen.</b></para>
///
/// <para>⚠⚠ <b>ZWEI SACHEN, DIE WIE FEHLER AUSSEHEN UND KEINE SIND:</b></para>
/// <list type="number">
///   <item><b>Es gibt keinen Höhenboden.</b> Der Arm klemmt nur nach OBEN
///   (135). Wer mit Z sinkt, fliegt unter das Gelände — in den Hügel hinein.
///   Das ist gelesen, siehe @0x4C2CBA.</item>
///   <item><b>Unter 0 springt die Höhe auf 135.</b> Die Rechnung ist ein
///   BYTE (<c>add cl, dl</c> @0x4C2CB2), und der Deckelvergleich ist
///   <b>vorzeichenlos</b> (<c>jbe</c>): aus 0 − 2 wird 254, und 254 &gt; 135,
///   also 135. Wer ganz nach unten drückt, ist plötzlich ganz oben. Wir bilden
///   das nach; <see cref="HandLuftUmlauf"/> zählt es, damit es nicht für einen
///   Fehler von uns gehalten wird. Gegenschalter
///   <c>--handhoehe-ohne-umlauf</c>.</item>
/// </list>
///
/// <para>⚠ <b>Unsere Abweichungen, benannt:</b></para>
/// <list type="bullet">
///   <item>Der <b>Kartenrand</b>. Das Original hat an dieser Stelle keine
///   Sperre; bei uns dreht <c>AirDrift</c> sonst von selbst um oder schickt
///   heim — und das nähme dem Spieler mitten im Flug die Steuerung aus der
///   Hand. Unter Handsteuerung wird die Lage darum nur <b>geklemmt</b>, ohne
///   Umkehr.</item>
///   <item>Der <b>Schuss ohne Ziel</b>: <c>0x427090</c> liest bei leerem
///   Zielfeld die Richtung (@0x42723F) und schiesst geradeaus. Wir legen den
///   Zielpunkt auf die Sichtweite vor die Maschine und nehmen einen Feind, der
///   nahe genug daran liegt, als Trefferziel. <b>Der Rumpf von 0x427090 ist
///   nicht gelesen</b> — nur seine drei Eingangsprüfungen.</item>
/// </list>
///
/// <para>Gegenschalter <c>--handsteuerung-luft-aus</c> (Knopf und Taste H tun
/// nichts, wie bis zum 19.09.). Messzeile
/// <c>--handsteuerung-luft-check</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--handsteuerung-luft-aus</c> — das Nullmodell. Darunter MUSS
    /// <see cref="HandLuftBegonnen"/> auf 0 bleiben.</summary>
    public static bool HandsteuerungLuftAus;

    /// <summary><c>--handhoehe-ohne-umlauf</c> — das Nullmodell zum
    /// Byte-Umlauf: die Höhe wird bei 0 abgefangen statt auf 135 zu springen.
    /// Darunter MUSS <see cref="HandLuftUmlauf"/> auf 0 fallen.</summary>
    public static bool HandhoeheOhneUmlauf;

    /// <summary>Welche Maschine gerade von Hand fliegt — Platz in
    /// <c>_special</c>, −1 = keine. Im Original <c>uk == 3</c> plus die
    /// Handmarke <c>+0x0F</c>.</summary>
    public int HandLuftIdx { get; private set; } = -1;

    /// <summary>Der Drehschritt je Takt, @0x433626: <b>6°</b>. Die Daten
    /// stützen ihn unabhängig — alle 190 Flugrichtungen der Leveldateien sind
    /// Vielfache von 6 (siehe <see cref="Special.Dir"/>).</summary>
    public const int HandDrehSchritt = 6;

    /// <summary>Der Höhenschritt je Takt, @0x433665: <b>2</b>.</summary>
    public const int HandHoehenSchritt = 2;

    /// <summary>Der Höhendeckel, @0x4C2CBA: <b>0x87 = 135</b>.</summary>
    public const int HandHoeheDeckel = 135;

    /// <summary>Was der Startzweig @0x4234DA einmalig auf die Höhe legt, wenn
    /// er an die Handsteuerung übergibt: <b>+5</b>.</summary>
    public const int HandUebergabeHub = 5;

    // ---- die Zahlen fuer den Pruefstand -----------------------------------
    // ⚠ Nach WIRKUNG getrennt. »Die Handsteuerung lief« sagt nichts darueber,
    // ob sie gedreht, beschleunigt, gestiegen und geschossen hat.
    public int HandLuftBegonnen, HandLuftBeendet, HandLuftTakte,
               HandLuftGedreht, HandLuftStufeAuf, HandLuftStufeAb,
               HandLuftGehoben, HandLuftGesenkt, HandLuftDeckel, HandLuftUmlauf,
               HandLuftSchuesse, HandLuftPerTasteH;

    /// <summary>Was die Oberfläche beim Ein- und Austritt tun soll: Zeiger aus
    /// (<c>byte[0xA182D0] := 0xFF</c> @0x4315D3) und das Fenster wegnehmen
    /// (<c>0x44FE10(0)</c>). Null heisst: niemand hört zu.</summary>
    public System.Action<bool>? OnHandsteuerungLuft;

    /// <summary>
    /// <b>Der Tastenstand</b>, den die Oberfläche je Bild hineinlegt und den
    /// der SPIELTAKT abholt.
    ///
    /// <para>⚠ Warum getrennt: im Original steht der Sender <c>0x433460</c> im
    /// Taktrumpf (@0x41685C), nicht im Bildtakt. Der Stufenschritt hängt an
    /// <c>dword[0x4FA240] &amp; 1</c> — am TAKT. Wer ihn je Bild schickt,
    /// bekommt bei 60 Bildern eine andere Steuerung als bei 30, und das wäre
    /// eine Abweichung, die man erst auf einem anderen Rechner merkt.</para>
    /// </summary>
    public bool HandTasteLinks, HandTasteRechts, HandTasteHoch, HandTasteRunter,
                HandTasteSteigen, HandTasteSinken, HandTasteSchuss;

    /// <summary>Fliegt diese Maschine gerade von Hand?</summary>
    public bool IstHandgesteuert(Special a)
        => HandLuftIdx >= 0 && HandLuftIdx < _special.Count
           && ReferenceEquals(_special[HandLuftIdx], a);

    // ======================= EINTRITT =======================================

    /// <summary>
    /// <b>Eintritt A — der Knopf »Handsteuerung« am Flughafen</b> (Taste 9).
    ///
    /// <para>Das Original schickt Befehl 502 im <b>Modus 6</b>; das Flugzeug
    /// startet ganz gewöhnlich und geht erst im Startzweig (Uhr &gt; 25) auf
    /// <c>uk 3</c> über. Bei uns fällt der Startvorgang weg — er ist nicht
    /// gebaut (<c>m_uk 3</c>, das Rollen, steht als offener Punkt) —, also
    /// holen wir die Maschine heraus und übergeben sofort. <b>Das ist eine
    /// benannte Vereinfachung</b>: im Original vergehen zwischen Knopf und
    /// Steuerbarkeit 26 Takte.</para>
    ///
    /// <para>⚠ Die Handsteuerung wirkt auf <b>eine</b> Maschine, nicht auf die
    /// Staffel (Bericht §3, Bauaufgabe 3: »Angriff und Bombe wechseln wirken
    /// staffelweit, Handsteuerung nicht«).</para></summary>
    /// <param name="slot">der Satzplatz der gewählten Hangarzeile.</param>
    /// <returns>false, wenn nichts geschehen ist — dann bleibt alles, wie es
    /// war, und der Aufrufer darf melden.</returns>
    public bool HandsteuerungLuftBeginnen(int slot)
    {
        if (HandsteuerungLuftAus) return false;
        int idx = _special.FindIndex(x => x.Slot == slot && !x.Dead);
        if (idx < 0) { _order = "Diese Maschine gibt es nicht mehr."; return false; }
        var a = _special[idx];
        if (a.Absturz) { _order = "Diese Maschine stuerzt ab."; return false; }

        if (a.Stored)
        {
            var home = GebaeudeMitSlot(a.HomeSlot);
            if (home == null) { _order = "Kein Flughafen fuer diese Maschine."; return false; }
            a.Stored = false;
            a.Pos = home.Pos;
            a.Col = home.Col; a.Row = home.Row;
            a.Alt = ElevOf(a.Col, a.Row) * 15;      // air_takeoff @0x4260B9
            a.Sollhoehe = FlughoeheMax;
            FlugtempoStart(a);                      // dir := 180, sp := 0
            home.Hangar?.Remove(a.Slot);
        }
        // @0x4234DA — der EINMALWERT bei der Uebergabe.
        a.Alt = Mathf.Min(HandHoeheDeckel, a.Alt + HandUebergabeHub);
        return HandLuftUebernehmen(idx, "Handsteuerung: Pfeile fliegen, A/Z Hoehe, Strg schiesst");
    }

    /// <summary>
    /// <b>Eintritt B — die Taste H</b> für eine bereits fliegende, gewählte
    /// Maschine (@0x433750 → Befehl 4 → @0x4C28BA). Dieselbe Taste beendet sie
    /// auch wieder (Befehl 5, @0x433863) — im Original entscheidet
    /// <c>uk == 3</c> darüber, bei uns <see cref="HandLuftIdx"/>.
    /// </summary>
    public bool HandsteuerungLuftTasteH()
    {
        if (HandsteuerungLuftAus) return false;
        if (HandLuftIdx >= 0) { HandsteuerungLuftBeenden("H"); return true; }
        int idx = _selAir;
        if (idx < 0 || idx >= _special.Count) return false;
        var a = _special[idx];
        // @0x4C28BA: `typ < 13` — die zwei Nachschubarten (13/14) koennen es
        // nicht. Eingelagert ebensowenig: uk 3 setzt der Startzweig.
        if (a.Dead || a.Stored || a.Absturz || a.IsSupply) return false;
        HandLuftPerTasteH++;
        return HandLuftUebernehmen(idx, "Handsteuerung uebernommen (H beendet sie)");
    }

    private bool HandLuftUebernehmen(int idx, string meldung)
    {
        HandLuftIdx = idx;
        var a = _special[idx];
        // word[0x4FA0C8] := 20000 + idx — die Maschine ist damit GEWAEHLT.
        _sel.Clear();
        _selAir = idx;
        SetPrimary();
        // uk 3 kennt keinen Auftrag: was vorher anlag, ist weg.
        a.Target = -1; a.Goal = null; a.PlayerGoal = null;
        a.Angriffsauftrag = false; a.Heimkehr = false; a.TurnPoint = null;
        HandLuftBegonnen++;
        _order = meldung;
        OnHandsteuerungLuft?.Invoke(true);          // Zeiger aus, Fenster zu
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    // ======================= AUSTRITT =======================================

    /// <summary>
    /// <b>Austritt</b> — H, Rechtsklick, jede Abwahl, und der Absturz.
    ///
    /// <para>Beide Arme des Originals (Befehl 5 @0x433863 und Befehl 30
    /// @0x4330D4) enden in <c>air_back_to_airport</c>: die Maschine fliegt
    /// heim und LANDET (<c>m_uk := 2</c>). Bei uns ist das
    /// <see cref="Special.Heimkehr"/> — derselbe Zustand, den ein erledigter
    /// Angriffsauftrag setzt, und derselbe, der sie am Ende einlagert.</para>
    ///
    /// <para>⚠ Beim <b>Absturz</b> hebt das Original den Zustand auf
    /// (@0x425F62) und schickt NICHT heim — eine stürzende Maschine fliegt
    /// nirgendwohin mehr.</para></summary>
    public void HandsteuerungLuftBeenden(string grund)
    {
        if (HandLuftIdx < 0) return;
        int idx = HandLuftIdx;
        HandLuftIdx = -1;
        HandLuftBeendet++;
        if (idx < _special.Count)
        {
            var a = _special[idx];
            if (!a.Dead && !a.Stored && !a.Absturz)
            {
                a.Heimkehr = true;                  // air_back_to_airport
                _order = $"Handsteuerung beendet ({grund}) — die Maschine landet.";
            }
            else _order = $"Handsteuerung beendet ({grund}).";
        }
        OnHandsteuerungLuft?.Invoke(false);
        UpdatePanel();
        QueueRedraw();
    }

    // ======================= DER TAKT =======================================

    /// <summary>
    /// <b>Ein Takt der Handsteuerung</b> — der Sender @0x433460 und der Arm
    /// @0x4C2B58 in einem. Der Aufrufer liest nur die Tasten.
    /// </summary>
    /// <param name="links">VK 0x25</param><param name="rechts">VK 0x27</param>
    /// <param name="hoch">VK 0x26</param><param name="runter">VK 0x28</param>
    /// <param name="steigen">VK 0x41 = A</param><param name="sinken">VK 0x5A = Z</param>
    /// <param name="schuss">VK 0x11 = Strg</param>
    public void HandsteuerungLuftTakt(bool links, bool rechts, bool hoch, bool runter,
                                      bool steigen, bool sinken, bool schuss)
    {
        if (HandLuftIdx < 0 || HandLuftIdx >= _special.Count) return;
        var a = _special[HandLuftIdx];
        // Der Absturz hebt den Zustand auf (@0x425F62), Tod und Landung auch.
        if (a.Dead || a.Stored || a.Absturz) { HandsteuerungLuftBeenden("Absturz"); return; }
        HandLuftTakte++;

        // --- +0x0A: dir += (rechts − links) · 6, Umlauf gegen 360 -----------
        int dDir = ((rechts ? 1 : 0) - (links ? 1 : 0)) * HandDrehSchritt;
        if (dDir != 0)
        {
            int d = a.Dir + dDir;
            if (d >= 360) d -= 360;                 // @0x4C2C37
            if (d < 0) d += 360;                    // @0x4C2C51
            a.Dir = d;
            HandLuftGedreht++;
        }

        // --- +0x0C: sp ±1, aber nur in JEDEM ZWEITEN Takt -------------------
        // @0x43361D: dword[0x4FA240] & 1 — der Takt selbst ist die Bremse,
        // nicht ein eigener Zaehler.
        if ((DebugTicks & 1) != 0)
        {
            int dSp = (hoch ? 1 : 0) - (runter ? 1 : 0);
            // @0x4C2C62..0x4C2C7E: bremsen geht nur oberhalb von sp_unten.
            if (dSp != 0 && (a.StufeUnten < a.Stufe || dSp >= 0))
            {
                int sp = a.Stufe + dSp;
                if (sp > a.Speed) sp = a.Speed;     // @0x4C2C9B Deckel sp_oben
                if (sp < 0) sp = 0;
                if (sp != a.Stufe)
                {
                    if (sp > a.Stufe) HandLuftStufeAuf++; else HandLuftStufeAb++;
                    a.Stufe = sp;
                }
            }
        }

        // --- +0x0E: alt += (A − Z)·2, Deckel 135, KEIN Boden ----------------
        int dAlt = ((steigen ? 1 : 0) - (sinken ? 1 : 0)) * HandHoehenSchritt;
        if (dAlt != 0)
        {
            int roh = a.Alt + dAlt;
            if (roh < 0)
            {
                // ⚠ DER BYTE-UMLAUF, siehe Klassenkopf. `add cl, dl` rechnet in
                // einem Byte, `jbe` vergleicht vorzeichenlos: aus −2 wird 254,
                // und 254 > 135 heisst 135.
                if (HandhoeheOhneUmlauf) roh = 0;
                else { roh = HandHoeheDeckel; HandLuftUmlauf++; }
            }
            else if (roh > HandHoeheDeckel) { roh = HandHoeheDeckel; HandLuftDeckel++; }
            if (roh > a.Alt) HandLuftGehoben++; else if (roh < a.Alt) HandLuftGesenkt++;
            a.Alt = roh;
            // ⚠ Die SOLLHOEHE zieht mit, sonst regelt FlughoeheTakt im naechsten
            // Takt alles wieder weg. Im Original stellt sich die Frage nicht:
            // uk 3 hat gar keinen Zweig, also laeuft dort keine Regelung.
            a.Sollhoehe = a.Alt;
        }

        // --- +0x10: Strg schiesst -------------------------------------------
        if (schuss) HandLuftSchuss(a);
    }

    /// <summary>
    /// <b><c>Air shoot(idx, 0xFF)</c></b> @0x4020D6 → @0x427090.
    ///
    /// <para>Gelesen sind die drei Eingangsprüfungen (@0x4270B7 Waffenart
    /// +0x2C, @0x4270CD +0x2B, @0x4270DB Munition +0x16) und dass der Rumpf bei
    /// leerem Zielfeld die <b>Richtung</b> heranzieht (@0x42723F liest +0x0A
    /// und rechnet sin/cos). ⚠ Der Rumpf selbst ist <b>nicht</b> gelesen — der
    /// Zielpunkt »Sichtweite geradeaus« ist unsere Setzung.</para></summary>
    private void HandLuftSchuss(Special a)
    {
        if (a.Cooldown > 0f) return;                 // die Abklingzeit
        if (a.Waffenart == 0 && a.Attack <= 0) return;
        if (a.Ammo <= 0 && !(CheatAmmo && Cheated(a))) return;

        float rad = Mathf.DegToRad(a.Dir);
        float weite = Mathf.Max(2, a.Sight) * TileW;
        var aim = a.Pos + new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * weite;

        // Wer nahe genug an der Schusslinie steht, ist das Trefferziel. ⚠ Die
        // halbe Zellbreite ist unsere Wahl; ohne Ziel fliegt das Geschoss auf
        // den Punkt und richtet dort an, was es anrichtet.
        Entity? getroffen = null;
        float best = TileW * 1.5f;
        for (int j = 0; j < _entities.Count; j++)
        {
            var e = _entities[j];
            if (e.IsProp || e.Dead || e.HpMax <= 0) continue;
            if (e.Owner is < 0 or > 7 || a.Owner is < 0 or > 7) continue;
            if (Allied(a.Owner, e.Owner)) continue;
            float dd = e.Pos.DistanceTo(aim);
            if (dd < best) { best = dd; getroffen = e; a.Target = j; }
        }
        if (getroffen != null) aim = getroffen.Pos;

        a.Cooldown = LuftnachladenAlt ? AirFireGap : NachladeTakte(a.Kind) * SimDt;
        if (!(CheatAmmo && Cheated(a))) a.Ammo--;
        LuftSalve(a, getroffen, aim);
        a.Target = -1;                               // uk 3 haelt kein Ziel
        HandLuftSchuesse++;
    }

    // ======================= DIE MESSZEILE ==================================

    /// <summary>Die Messzeile — <c>--handsteuerung-luft-check</c>.
    ///
    /// <para>⚠ Die KONTROLLZAHL ist <see cref="HandLuftBegonnen"/> gegen
    /// <see cref="HandLuftBeendet"/> plus die eine, die noch fliegt: jede
    /// begonnene Handsteuerung muss genau einmal enden. Läuft das auseinander,
    /// hängt eine — und eine hängende Handsteuerung frisst jeden Tastendruck.</para>
    ///
    /// <para>⚠ Und sie nennt jede WIRKUNG einzeln. »Die Handsteuerung lief
    /// 300 Takte« ist keine Messung, solange nicht dasteht, dass in diesen
    /// Takten gedreht, beschleunigt, gestiegen und geschossen wurde.</para></summary>
    public string HandsteuerungLuftAuskunft()
    {
        int offen = HandLuftIdx >= 0 ? 1 : 0;
        int enden = HandLuftBeendet + offen;
        if (HandLuftBegonnen == 0 && enden == 0) return "";
        string hoehe = HandLuftIdx >= 0 && HandLuftIdx < _special.Count
            ? $", jetzt Hoehe {_special[HandLuftIdx].Alt} Stufe {_special[HandLuftIdx].Stufe}"
              + $" Richtung {_special[HandLuftIdx].Dir}°"
            : "";
        return $"handsteuerung-luft-check: {HandLuftBegonnen}x begonnen "
             + $"({HandLuftPerTasteH}x per Taste H), {HandLuftBeendet}x beendet, "
             + $"{offen} laeuft noch = {enden} "
             + (enden == HandLuftBegonnen ? "(stimmt)" : "⚠ HAENGT")
             + $" · {HandLuftTakte} Takte · gedreht {HandLuftGedreht}x "
             + $"(je {HandDrehSchritt}°) · Stufe +{HandLuftStufeAuf}/−{HandLuftStufeAb} "
             + $"· Hoehe +{HandLuftGehoben}/−{HandLuftGesenkt}, {HandLuftDeckel}x am "
             + $"Deckel {HandHoeheDeckel}, {HandLuftUmlauf}x Byte-Umlauf unter 0 "
             + $"· {HandLuftSchuesse} Schuesse" + hoehe
             + (HandsteuerungLuftAus
                 ? "   [--handsteuerung-luft-aus: alles 0 ist das SOLL]" : "")
             + (HandhoeheOhneUmlauf
                 ? "   [--handhoehe-ohne-umlauf: Byte-Umlauf MUSS 0 sein]" : "");
    }
}
