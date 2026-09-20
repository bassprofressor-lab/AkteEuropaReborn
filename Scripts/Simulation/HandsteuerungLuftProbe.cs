using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b><c>--handsteuerung-luft-probe</c> — fliegt die Handsteuerung wirklich?</b>
///
/// <para>Die Probe <b>fliegt</b>, sie liest keine Zähler ab. Jeder Schritt geht
/// durch <see cref="MapEntityLayer.HandsteuerungLuftTakt"/> — denselben Weg,
/// den die Tastatur nimmt.</para>
///
/// <para>⚠⚠ <b>Die Do-Not-Repeat vom 19.09.:</b> ein Prüfstand mit eigener
/// Formel prüft sich selbst. Darum steht hier <b>keine</b> eigene Rechnung für
/// Winkel, Stufe oder Höhe — die Sollwerte kommen aus denselben Konstanten
/// (<see cref="MapEntityLayer.HandDrehSchritt"/>,
/// <see cref="MapEntityLayer.HandHoehenSchritt"/>,
/// <see cref="MapEntityLayer.HandHoeheDeckel"/>), und die Schritte werden
/// <b>gezählt</b>, nicht nachgerechnet.</para>
///
/// <para>⚠ <b>Und die zweite vom 19.09.:</b> ein Fall muss sich unterscheiden
/// können. Der Winkelfall ist darum so gewählt, dass er über <b>360 läuft</b>
/// (Start 300, 20 Takte à 6° = 420 → 60): auf einem Fall ohne Umlauf wäre die
/// Umlaufrechnung @0x4C2C37 nicht zu sehen. Der Höhenfall drückt unter
/// <b>0</b>, weil nur dort der Byte-Umlauf sichtbar wird, den das Original
/// hat.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Wie viele Takte je Abschnitt geflogen wird.</summary>
    private const int ProbeTakte = 20;

    public string HandsteuerungLuftProbe()
    {
        var sb = new System.Text.StringBuilder("handsteuerung-luft-probe\n");
        if (HandsteuerungLuftAus)
            return sb.Append("  --handsteuerung-luft-aus: nicht stellbar (das ist das "
                           + "Nullmodell, und dass hier NICHTS steht, ist sein Soll)")
                     .ToString();

        // Irgendeine Maschine, die fliegen kann — am liebsten eine im Hangar,
        // damit auch der Eintritt ueber den Knopf mitgemessen wird.
        int idx = _special.FindIndex(x => !x.Dead && !x.Absturz && !x.IsSupply);
        if (idx < 0) idx = ProbeFlugzeugStellen(sb);
        if (idx < 0) return sb.Append("  kein Flughafen auf der Karte — nicht stellbar").ToString();
        var a = _special[idx];
        bool ausDemHangar = a.Stored;

        bool alles = true;

        // --- 1. EINTRITT ----------------------------------------------------
        bool eingetreten = HandsteuerungLuftBeginnen(a.Slot);
        bool gewaehlt = HandLuftIdx == idx;
        sb.Append($"  1. Eintritt (Taste 9, Platz {a.Slot}"
                + (ausDemHangar ? ", aus dem Hangar" : ", schon in der Luft")
                + $"): uebernommen {HandJa(eingetreten)}, Maschine gewaehlt {HandJa(gewaehlt)}, "
                + $"Hoehe {a.Alt}\n");
        alles &= eingetreten && gewaehlt;
        if (!eingetreten) return sb.Append("  -> Abbruch").ToString();

        // --- 2. DIE DREHUNG, ueber 360 hinweg --------------------------------
        // ⚠ Der Fall, der TRENNT: Start 300, 20 Schritte a 6 = 420 -> 60.
        // Ohne Umlauf stuende hier 420, und 420 gibt es nicht.
        a.Dir = 300;
        int dirVor = a.Dir, gedrehtVor = HandLuftGedreht;
        for (int t = 0; t < ProbeTakte; t++) HandProbeTakt(rechts: true);
        int schritte = HandLuftGedreht - gedrehtVor;
        int sollDir = (dirVor + schritte * HandDrehSchritt) % 360;
        bool dirOk = a.Dir == sollDir && schritte == ProbeTakte;
        sb.Append($"  2. Drehung: {schritte} Schritte a {HandDrehSchritt}° von {dirVor}° "
                + $"-> {a.Dir}° (Soll {sollDir}°, ueber 360 gelaufen) {HandJa(dirOk)}\n");
        alles &= dirOk;

        // --- 3. DIE STUFE, nur in jedem ZWEITEN Takt -------------------------
        // Deckel sp_oben; darum wird von 0 aus hochgefahren und gegen den
        // Deckel geprueft, nicht gegen eine eigene Zahl.
        a.Stufe = 0;
        int aufVor = HandLuftStufeAuf;
        for (int t = 0; t < ProbeTakte; t++) HandProbeTakt(hoch: true);
        int auf = HandLuftStufeAuf - aufVor;
        // In 20 Takten ist die Haelfte ungerade -> hoechstens 10 Schritte, und
        // nie ueber sp_oben hinaus.
        int sollAuf = Mathf.Min(ProbeTakte / 2, a.Speed);
        bool stufeOk = auf == sollAuf && a.Stufe == sollAuf && a.Stufe <= a.Speed;
        sb.Append($"  3. Stufe: {auf} Schritte in {ProbeTakte} Takten (Soll {sollAuf} — "
                + $"nur jeder ZWEITE zaehlt), jetzt {a.Stufe} von sp_oben {a.Speed} "
                + $"{HandJa(stufeOk)}\n");
        alles &= stufeOk;

        // --- 4. DIE HOEHE: Deckel 135, KEIN Boden, Byte-Umlauf ---------------
        a.Alt = HandHoeheDeckel - 2 * HandHoehenSchritt;
        int deckelVor = HandLuftDeckel;
        for (int t = 0; t < ProbeTakte; t++) HandProbeTakt(steigen: true);
        bool deckelOk = a.Alt == HandHoeheDeckel && HandLuftDeckel > deckelVor;
        sb.Append($"  4. Hoehe hoch: jetzt {a.Alt} (Deckel {HandHoeheDeckel}), "
                + $"{HandLuftDeckel - deckelVor}x geklemmt {HandJa(deckelOk)}\n");
        alles &= deckelOk;

        // Nach UNTEN gibt es keinen Boden — und unter 0 springt sie auf 135
        // zurueck, weil das Original in einem BYTE rechnet (@0x4C2CB2) und
        // vorzeichenlos vergleicht (@0x4C2CBD jbe).
        a.Alt = HandHoehenSchritt;                 // zwei Schritte ueber 0
        int umlaufVor = HandLuftUmlauf;
        HandProbeTakt(sinken: true);               // -> 0
        int nullpunkt = a.Alt;
        HandProbeTakt(sinken: true);               // -> unter 0
        int umlauf = HandLuftUmlauf - umlaufVor;
        bool umlaufOk = HandhoeheOhneUmlauf
            ? umlauf == 0 && a.Alt == 0
            : umlauf == 1 && a.Alt == HandHoeheDeckel;
        sb.Append($"  5. Hoehe runter: {HandHoehenSchritt} -> {nullpunkt} -> {a.Alt}, "
                + $"{umlauf}x Byte-Umlauf "
                + (HandhoeheOhneUmlauf
                    ? "[--handhoehe-ohne-umlauf: Soll 0x Umlauf und Hoehe 0] "
                    : $"[Soll 1x Umlauf und Hoehe {HandHoeheDeckel} — das ist das "
                    + "ORIGINAL, kein Fehler von uns] ")
                + $"{HandJa(umlaufOk)}\n");
        alles &= umlaufOk;

        // --- 6. STRG SCHIESST ------------------------------------------------
        a.Alt = 100;
        a.Ammo = Mathf.Max(1, a.AmmoMax);
        a.Cooldown = 0f;
        // ⚠ Eine unbewaffnete Maschine kann diesen Abschnitt nicht stellen.
        // Das wird GESAGT und nicht stillschweigend weggebogen.
        bool bewaffnet = a.Attack > 0 || a.Waffenart != 0;
        if (!bewaffnet)
            sb.Append("  6. Strg: diese Maschine ist unbewaffnet — NICHT GEMESSEN\n");
        int schussVor = HandLuftSchuesse, munVor = a.Ammo;
        if (bewaffnet)
        {
            HandProbeTakt(schuss: true);
            bool schussOk = HandLuftSchuesse == schussVor + 1 && a.Ammo == munVor - 1;
            // Der zweite Druck im selben Augenblick darf NICHT schiessen — die
            // Abklingzeit steht.
            HandProbeTakt(schuss: true);
            bool sperreOk = HandLuftSchuesse == schussVor + 1;
            sb.Append($"  6. Strg: {HandLuftSchuesse - schussVor} Schuss (Soll 1), "
                    + $"Munition {munVor} -> {a.Ammo} {HandJa(schussOk)}, "
                    + $"zweiter Druck gesperrt (Abklingzeit) {HandJa(sperreOk)}\n");
            alles &= schussOk && sperreOk;
        }

        // --- 7. AUSTRITT -> air_back_to_airport ------------------------------
        int beendetVor = HandLuftBeendet;
        HandsteuerungLuftBeenden("Probe");
        bool ausOk = HandLuftIdx < 0 && HandLuftBeendet == beendetVor + 1
                     && (a.Heimkehr || a.Stored || a.Dead);
        sb.Append($"  7. Austritt: Handsteuerung aus {HandJa(HandLuftIdx < 0)}, "
                + $"Heimkehr gesetzt {HandJa(a.Heimkehr)} {HandJa(ausOk)}\n");
        alles &= ausOk;

        // --- Kontrollzahl ----------------------------------------------------
        int enden = HandLuftBeendet + (HandLuftIdx >= 0 ? 1 : 0);
        bool buch = enden == HandLuftBegonnen;
        sb.Append($"  Buchfuehrung: {HandLuftBegonnen}x begonnen = {enden}x geendet "
                + $"{HandJa(buch)}\n");
        alles &= buch;

        sb.Append(alles ? "  -> BESTANDEN" : "  -> DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>
    /// <b>Ein Flugzeug für die Probe</b>, wenn die Karte keines hat.
    ///
    /// <para>⚠ Das ist eine PRUEFSTANDSKULISSE und keine Messung: die Werte
    /// unten sind gewählt, nicht gelesen. Gebraucht wird sie, weil die
    /// Kampagnenkarten fast nie ein vorgesetztes Flugzeug tragen (in sec19
    /// aller 23 Dateien steht genau EINES) — der Spieler kauft sie. Ohne
    /// Kulisse wäre die Handsteuerung kopflos überhaupt nicht zu messen.</para>
    ///
    /// <para>Sie wird <b>angesagt</b>, damit niemand die Zahlen für die eines
    /// echten Flugzeugs hält.</para></summary>
    private int ProbeFlugzeugStellen(System.Text.StringBuilder sb)
    {
        var feld = _entities.Find(x => x.IsBuilding && !x.Dead && x.BType == 9);
        if (feld == null) return -1;
        int slot = 900;
        while (_special.Exists(x => x.Slot == slot)) slot++;
        var a = new Special
        {
            Slot = slot, Kind = 1, Name = "Probeflieger",
            Owner = feld.Owner, HomeSlot = feld.Slot,
            Col = feld.Col, Row = feld.Row, Pos = feld.Pos,
            Stored = true,
            Speed = 10,          // sp_oben
            StufeUnten = 2,      // sp_unten
            Stufe = 0,
            Hp = 100, HpMax = 100,
            Ammo = 4, AmmoMax = 4, Fuel = 800, FuelMax = 800,
            Attack = 10, Defence = 10, Sight = 6,
        };
        _special.Add(a);
        (feld.Hangar ??= new System.Collections.Generic.List<int>()).Add(slot);
        sb.Append($"  0. ⚠ KULISSE: die Karte hatte kein Flugzeug — die Probe hat "
                + $"eines in Flughafen {feld.Slot} gestellt (Art 1, sp_oben 10, "
                + $"sp_unten 2). Die Werte sind GEWAEHLT, nicht gelesen.\n");
        return _special.Count - 1;
    }

    /// <summary>Ein Probetakt. ⚠ Er zählt <see cref="DebugTicks"/> selbst
    /// weiter, weil der Stufenschritt daran hängt (@0x43361D) — eine Probe, die
    /// den Takt nicht mitzählt, sähe die Halbierung nie.</summary>
    private void HandProbeTakt(bool links = false, bool rechts = false,
                               bool hoch = false, bool runter = false,
                               bool steigen = false, bool sinken = false,
                               bool schuss = false)
    {
        // ⚠ Der Takt MUSS mitzaehlen: der Stufenschritt haengt an
        // (DebugTicks & 1) @0x43361D. Eine Probe, die ihn stehenlaesst, sieht
        // die Halbierung nie — und meldete dann 20 Schritte statt 10.
        DebugTicks++;
        HandsteuerungLuftTakt(links, rechts, hoch, runter, steigen, sinken, schuss);
    }

    private static string HandJa(bool b) => b ? "ja" : "NEIN";
}
