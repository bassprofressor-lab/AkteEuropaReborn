namespace AkteEuropaReborn.Rendering;

using Godot;
using System.Text;

/// <summary>
/// <b><c>--schiffsentwurf-check</c> — STIMMEN DIE SCHIFFSZEILEN, UND TAUGT
/// <c>+0x43</c> ALS ENTWURFSNUMMER?</b> (19.09.2026.)
///
/// <para>Der Stand zu zwei Entscheidungen aus einem Tag: die Zeilenrechnung
/// (<see cref="SchiffszeilenRechnen"/>) wurde gebaut, der Schiffszweig des
/// Nachziehens ausdrücklich NICHT (Begründung im Kopf von
/// <c>Nachziehen.cs</c>). Er misst beides.</para>
///
/// <para><b>Aussage 1 — die gerechneten Zeilen.</b> Die zehn Entwürfe müssen die
/// Energien tragen, die die Gegenlesung <b>unabhängig</b> aus <c>PARTS.CWD</c>
/// gerechnet hat: <c>90 125 70 120 80 50 70 110 175 210</c>
/// (<c>berichte/schiffsentwurf-fable.md</c> §5/§7, gegengeprüft an 1040
/// Entwurfszeilen aus 13 Spielständen, 13 520 von 13 520 Feldwerten). Das ist
/// eine echte Gegenprobe und keine Tautologie: die Zahlen kommen aus einer
/// anderen Quelle als unsere Rechnung. ⚠ Und keine <b>freigegebene</b> Zeile
/// darf Energie 0 tragen — <c>LaunchShip</c> setzt <c>Hp = HpMax = d.Energie</c>
/// ohne Absicherung, eine Null wäre ein Schiff mit 0 Trefferpunkten.</para>
///
/// <para><b>Aussage 2 — <c>+0x43</c> auf KARTENGESETZTEN Schiffen.</b> ⚠⚠ Das ist
/// <b>keine</b> Bestehensfrage, sondern ein BEFUND, und er ist der Grund, warum
/// der Nachziehzweig nicht gebaut wird. Gemessen am 19.09. über alle 54 Karten:
/// <b>178</b> Schiffe tragen in <c>+0x43</c> eine Zahl, deren Entwurfszeile zu
/// ihrer Energie passt — <b>38</b> nicht, und die liegen ausnahmslos auf den
/// CD2-Karten 17, 20, 22, 25, 26 und 31. Auf Karte 22 steht dort eine
/// <b>103</b>, und das kann kein Entwurfsindex sein (die Tafel hat 10 Zeilen je
/// Spieler).</para>
///
/// <para><b>⚠ Meine erste Deutung war falsch und ist widerlegt.</b> Ich hatte
/// vermutet, <c>+0x43</c> sei doppelt belegt — Entwurf beim gebauten Schiff,
/// Skriptmarke beim gesetzten. Die Gegenprüfung an den ORIGINALDATEIEN beider
/// CDs (§8 des Berichts) sagt: <b>nein</b>. Alle drei Schreiber
/// (<c>0x4B1AB4</c>, <c>0x4B366E</c>, <c>0x4B2DF2</c>) schreiben die
/// Entwurfsnummer in <b>zwei</b> Felder — <c>+0x43</c> (Byte) und <c>+0x3E</c>
/// (Wort). Die »Marke« der Missionsskripte IST die Entwurfsnummer:
/// <c>unit_has_mark</c> hat 60 Rufstellen, gesucht werden 55, 57, 73, 86,
/// 190…194 — <b>keine sucht 0 oder 103</b>. Doppelt sind nur die LESER:
/// <c>0x4B3D70</c> und das Sinnbild <c>0x4508A0</c> nehmen <c>+0x43</c>, die
/// Namenszeilen <c>0x4701CF</c>/<c>0x477B4D</c>/<c>0x47B236</c> nehmen
/// <c>+0x3E</c> — mit derselben Rechnung.</para>
///
/// <para><b>Was wirklich dahintersteckt: ungepflegte KARTENDATEN.</b> Vollzählig
/// über beide CDs (54 Dateien, <b>210</b> Schiffe): <b>26</b> tragen in
/// <c>+0x43</c> etwas anderes als in <c>+0x3E</c>, und weitere <b>12</b> liegen
/// in beiden Feldern um eins unter dem, was Rumpfbild und Energie sagen. Das
/// Wort <c>+0x3E</c> passt dagegen bei <b>210 von 210</b>. Bei Landeinheiten
/// stimmen <b>4254 von 4254</b> überein — es sind ausschließlich die Schiffe der
/// CD2-Karten, die der Editor nicht gepflegt hat. Die <c>103</c> auf Karte 22 ist
/// ein Autorenrest.</para>
///
/// <para>⚠⚠ Und <c>0x4B3D70</c> hat <b>keinen Wächter und keine
/// Bereichsprüfung</b>. Liefe der Zweig, dann zöge er auf K17/K20/K22 zwölf bzw.
/// ein Schiff von 125 auf 90 (Angriff 6 statt 18), auf K25/K31 von 175 auf 110,
/// auf K26 von 210 auf 175 — und auf Karte 22 läse die <c>103</c> die Adresse
/// <c>0x53002A</c>, <b>1386 Byte hinter dem Tafelende</b>: 42 Nullbytes, also ein
/// Schiff mit <b>0 Trefferpunkten</b>. Das ist der Grund, warum der Zweig nicht
/// gebaut ist.</para>
///
/// <para>⚠ <b>Was hier noch offen ist</b> (seine Entscheidung, nicht meine):
/// baute man den Zweig, müsste man wählen — <b>originaltreu</b> <c>+0x43</c>
/// lesen und den Datenfehler mitsamt Hp 0 nachbilden, oder <b>datentreu</b>
/// <c>+0x3E</c> nehmen (210/210) und bewusst abweichen. Solange nichts gebaut
/// ist, muss die Frage nicht beantwortet werden.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die Energien der zehn Schiffsentwürfe, wie sie die Gegenlesung
    /// aus <c>PARTS.CWD</c> gerechnet hat — die UNABHÄNGIGE Quelle, gegen die
    /// unsere Rechnung geprüft wird. ⚠ Nicht anfassen, um einen Prüfstand grün
    /// zu bekommen: dann prüft er nichts mehr.</summary>
    private static readonly int[] SollEnergie = { 90, 125, 70, 120, 80, 50, 70, 110, 175, 210 };

    public string SchiffsentwurfCheckLine()
    {
        var sb = new StringBuilder("schiffsentwurf-check\n");
        if (_shipDesigns == null)
            return sb.Append("  keine Schiffsliste geladen (Maps/ships.json) — UNGEPRUEFT")
                     .ToString();

        sb.AppendLine($"  Entwurfszeilen aus [{_shipSource}], {_shipDesigns.Count} Zeilen"
                    + (SchiffszeilenAlt ? "   ⚠ NULLMODELL --schiffszeilen-alt: die Zeilen "
                                        + "MUESSEN hier durchfallen" : ""));

        // ---- Aussage 1a: die Zeilen gegen die Gegenlesung ------------------
        int gemessen = 0, falsch = 0;
        var abwZeile = new System.Collections.Generic.List<string>();
        foreach (var d in _shipDesigns)
        {
            if (d.Player != 0) continue;          // acht gleiche Bloecke, einer reicht
            if (d.Index is < 0 or > 9) continue;
            gemessen++;
            if (d.Energie == SollEnergie[d.Index]) continue;
            falsch++;
            abwZeile.Add($"Entwurf {d.Index} »{d.Name}«: {d.Energie}, "
                       + $"Gegenlesung {SollEnergie[d.Index]}");
        }
        sb.AppendLine($"  Zeilen gegen die Gegenlesung (PARTS.CWD): {gemessen} geprueft, "
                    + $"{falsch} abweichend");
        foreach (var a in abwZeile) sb.AppendLine($"      ⚠ {a}");

        // ---- Aussage 1b: keine freigegebene Zeile mit Energie 0 ------------
        int frei = 0, freiNull = 0;
        var nullen = new System.Collections.Generic.List<string>();
        foreach (var d in _shipDesigns)
        {
            if (!d.Enable) continue;
            frei++;
            if (d.Energie > 0) continue;
            freiNull++;
            string wer = $"Entwurf {d.Index} »{d.Name}«";
            if (!nullen.Contains(wer)) nullen.Add(wer);
        }
        sb.AppendLine($"  freigegebene Zeilen: {frei}, davon mit Energie 0: {freiNull}"
                    + (frei == 0 ? "   (diese Mission gibt noch keinen Schiffsentwurf frei)" : ""));
        foreach (var w in nullen)
            sb.AppendLine($"      ⚠ {w} — ein gebautes Schiff haette 0 Trefferpunkte");

        // ---- Aussage 2: der BEFUND zu +0x43, kein Bestehenskriterium -------
        int schiffe = 0, passend = 0, unpassend = 0, ausserhalb = 0;
        var abwSchiff = new System.Collections.Generic.List<string>();
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead || e.GameUnitType is not (4 or 5)) continue;
            schiffe++;
            if (e.Mark is < 0 or > 9) { ausserhalb++; }
            var d = ZeileFuerSchiff(e);
            if (d != null && d.Energie == e.HpMax) { passend++; continue; }
            unpassend++;
            if (abwSchiff.Count < 6)
                abwSchiff.Add($"Platz {e.Slot} (Spieler {e.Owner}): +0x43 = {e.Mark}, "
                            + $"Energie {e.HpMax}, Zeile dazu "
                            + $"{(d == null ? "gibt es nicht" : d.Energie.ToString())}");
        }
        sb.AppendLine($"  BEFUND +0x43 auf kartengesetzten Schiffen: {schiffe} Schiffe, "
                    + $"{passend} passen zu ihrer Zeile, {unpassend} nicht"
                    + (ausserhalb > 0 ? $", davon {ausserhalb} mit +0x43 ausserhalb 0…9" : ""));
        foreach (var a in abwSchiff) sb.AppendLine($"      · {a}");
        if (unpassend > 0)
            sb.AppendLine("      ⚠ KEIN Fehler unserer Karte, sondern UNGEPFLEGTE KARTENDATEN "
                        + "des Originals: an den Originaldateien beider CDs gemessen tragen 26 "
                        + "von 210 Schiffen in +0x43 etwas anderes als in +0x3E, 12 weitere "
                        + "liegen um eins daneben; +0x3E passt 210/210. Liefe der Nachziehzweig "
                        + "darueber, zoege er diese Schiffe herunter (Karte 22: bis auf Hp 0, "
                        + "1386 Byte hinter dem Tafelende). Darum ist er nicht gebaut — "
                        + "siehe Kopf von Nachziehen.cs.");
        if (schiffe == 0)
            sb.AppendLine("  ⚠ keine Schiffe auf dieser Karte — der Befund bleibt hier "
                        + "UNGEMESSEN; fuer ihn eine Seekarte nehmen (K1, K10, K17)");

        // ⚠ Bestanden wird NUR ueber Aussage 1. Aussage 2 ist ein Befund, und
        // ein Pruefstand, der an einem Befund scheitert, ist auf Dauer rot und
        // wird dann nicht mehr gelesen.
        bool alles = falsch == 0 && freiNull == 0 && gemessen == 10;
        if (falsch > 0)
            sb.AppendLine("  ⚠⚠ Die ZEILENRECHNUNG stimmt nicht (Simulation/Schiffszeilen.cs, "
                        + "0x4B25E0). Das ist NICHT durch den Nachziehzweig zu beheben.");
        sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Die Entwurfszeile, die <c>0x4B3D70</c> für dieses Schiff läse:
    /// <c>Spieler·10 + +0x43</c> in sec119 (<c>lea ebp,[edx+edx*4]; lea
    /// edx,[ebx+ebp*2]</c>). <c>e.Mark</c> ist bei uns das Byte <c>+0x43</c>.
    /// ⚠ Dass das bei einem kartengesetzten Schiff die RICHTIGE Zeile ist, ist
    /// genau die Annahme, die Aussage 2 widerlegt.</summary>
    private ShipDesign? ZeileFuerSchiff(Entity e)
    {
        if (_shipDesigns == null || e.Mark is < 0 or > 9) return null;
        foreach (var d in _shipDesigns)
            if (d.Index == e.Mark && d.Player == e.Owner) return d;
        // ⚠ Fällt die Karte auf die EXE-Vorgabe zurück, trägt die Liste nur den
        // Block von Spieler 0 — dann gilt er für alle, so wie 0x4B2330 ihn im
        // Original auf die sieben anderen kopiert.
        foreach (var d in _shipDesigns)
            if (d.Index == e.Mark && d.Player == 0) return d;
        return null;
    }
}
