namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐ 07.10.2026, bug-479 — <b>DER BAUTEILBESITZ IM GEFECHT</b>, gelesen in
/// <c>0x419CB0</c> (C) / <c>0x419AF0</c> (F, Tafel dort <c>0x5035E0</c>), einziger
/// Rufer <c>0x41AC68</c> in <c>gefecht_starten</c> <c>0x41A150</c> mit dem Argument
/// <c>byte[0x540EB8]</c> (Techstandard 1..8). Bericht:
/// <c>berichte/gefecht-besitz-pruefstaende-fable.md</c>.
///
/// <para><b>Die Routine, Zeile für Zeile:</b></para>
/// <code>
///   0x419CB7..0x419CD1  Spieler 0: +0x00 := 0 für ALLE 200 Zeilen
///   0x419CD9..0x419D42  Zeilen 0..99:    +0x00 := (+0x24 &lt;= Techstandard  UND  Name +0x02 != "")
///                       (strcmp gegen die leere Zeichenkette 0x4F5ABC; ja -> 1 @0x419D2D, sonst 0 @0x419D36)
///   0x419D44..0x419DAF  Zeilen 160..179: dieselbe Regel (cmp dl, 0xB4)
///   0x419DB1            byte[0x5057C0] := 1  — das ist Zeile 80 (+0x00), 0x5045A0 + 58·80
///   0x419DB8            call 0x40133E -> 0x4B2290: Block 0 (11 600 B) auf die Spieler 1..7 kopieren
/// </code>
///
/// <para>⭐ <b>Folgen, die man sehen kann:</b> (1) die Zeilen <b>100..159</b> und
/// <b>180..199</b> bekommen im Gefecht NIE Besitz — die Nutzlasten 100..107 des
/// Flughafens stehen in der EXE-Tafel mit 1, im Gefecht mit 0; genau das zeigen
/// auch die Spielstände 1/4/10.DM (sec46, Besitz 100..107 = 0). (2) Die
/// <b>sec46 der DM-Karte wird für den Besitz NICHT gebraucht</b>: die Routine
/// überschreibt +0x00 jeder Zeile, bevor jemand es liest; aus der Karte zählen nur
/// +0x24 (Techschwelle) und der Name, und beide sind in 1/4/10.DM byteweise die der
/// EXE-Tafel (geprüft mit cwm_sections.py: 0 Abweichungen außer +0x00 und den
/// englischen Namen; 4.DM hat zusätzlich die erfundene Zeile 20 »Hiff-64«,
/// Techstufe 10, die keine Stufe 1..8 je freigibt). (3) 65 Teleporter (Stufe 8),
/// 68/69 Minen-/Fallenräumer (Stufe 3) und 169..172 (W-Wiesel …) stehen in der
/// EXE-Tafel mit Besitz 0 und werden im Gefecht ab ihrer Stufe trotzdem besessen —
/// die Tafel ist nur der Rohstoff, die Stufe entscheidet.</para>
///
/// <para><b>Sollmengen aus der EXE-Tafel</b> (Zeilen, die die Regel bei Stufe T
/// freigibt, Zeile 80 mitgezählt): T1 = 12 Zeilen {1,4,67,71,72,73,74,78,80,161,167,169},
/// T4 = 35, T8 = 59 (alle benannten 1..19, 65..79, 81..88, 160..175). Bei der
/// Vorgabe 8 des Gefechtsschirms ändert sich im Erstellen-Fenster gegenüber
/// 0.6.6 also NICHTS; erst eine niedrigere Stufe nimmt Teile weg.</para>
///
/// <para>⚠ <b>UNSERE Setzungen:</b> wir schreiben den Besitz in unsere acht
/// Bauteilblöcke (<c>_bauteile</c>, Simulation/Aufwertung.cs) statt in eine
/// Tafel bei 0x5045A0; der Block-0-auf-1..7-Kopierer kopiert wie das Original die
/// GANZE Zeile (58 B). Die Technikstufe ist <see cref="AirProbeTechstandard"/>
/// (Gefechtsschirm, <c>--techstandard=</c> für Prüfläufe). Gegenschalter
/// <c>--gefecht-besitz-alt</c>: kein Verteilen, die EXE-Tafel gilt, und das
/// Erstellen-Fenster bietet im Gefecht wie bis 0.6.6 jede benannte Zeile an.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--gefecht-besitz-alt</c> — der Stand bis 0.6.6: im Gefecht kein
    /// Besitz nach Techstandard, das Erstellen-Fenster zeigt jede benannte Zeile.</summary>
    public static bool GefechtBesitzAlt;

    /// <summary><c>--alle-einheiten-bauteile-alt</c> — der Stand bis bug-489:
    /// »Alle Einheiten« gibt nur Flugzeuge/Schiffe frei, die Bauteile folgen
    /// weiter dem Techstandard.</summary>
    public static bool AlleEinheitenBauteileAlt;

    /// <summary>Ob <see cref="GefechtBesitzStart"/> auf dieser Ebene gelaufen ist —
    /// nur dann filtert <c>LoadDesignParts</c> im Gefecht nach Besitz. Eine Ebene
    /// ohne Gefechtsstart (<c>--map=</c> zum Ansehen) bleibt beim alten Stand.</summary>
    public bool GefechtBesitzVerteilt { get; private set; }

    /// <summary>Wie viele Zeilen der letzte Lauf Spieler 0 gegeben hat (mit Zeile 80).</summary>
    public int GefechtBesitzZeilen { get; private set; }

    /// <summary>Die erste Zeile hinter dem zweiten Schleifenblock — <c>cmp dl, 0xB4</c> @0x419DAC.</summary>
    private const int GefechtBesitzFahrwerkEnde = 0xB4;

    /// <summary>
    /// <c>0x419CB0(techstufe)</c>, siehe Dateikopf. Läuft beim Gefechtsstart
    /// (<c>StartSkirmish</c>) und aus <c>--entwurf-dunkel-check</c> je Stufe.
    /// Gibt die Zahl der Zeilen von Spieler 0 zurück, −1 unter dem Gegenschalter.
    /// </summary>
    public int GefechtBesitzStart(int techstufe)
    {
        if (GefechtBesitzAlt) return -1;
        // ⭐ 12.10.2026 (bug-489) — UNSERE OPTION »Alle Einheiten« (es gibt sie im
        // Original nicht) gibt auf seine Entscheidung auch ALLE Bauteile frei:
        // gerechnet wird mit Stufe 8, der hoechsten des Gefechtsschirms. Stufe 8
        // gibt schon jede benannte Zeile frei (Dateikopf: T8 = 59); die erfundene
        // Zeile 20 »Hiff-64« der 4.DM (Stufe 10) bleibt wie im Original draussen.
        if (UI.SkirmishSetup.AllUnits && !AlleEinheitenBauteileAlt && techstufe < 8)
        {
            GD.Print($"gefecht-besitz: »Alle Einheiten« an -> Stufe 8 statt {techstufe} (bug-489, unsere Option)");
            techstufe = 8;
        }
        BauteileVorbereiten();
        var b0 = _bauteile![0];
        int zeilen = 0;
        for (int r = 0; r < 200; r++)
        {
            var c = b0[r];
            if (c == null || c.Length < 58) continue;
            c[0x00] = 0;                                        // 0x419CCA
            bool imBlock = r < 100 || (r >= 0xA0 && r < GefechtBesitzFahrwerkEnde);
            if (!imBlock) continue;
            if (c[0x24] > techstufe) continue;                  // 0x419CF5 ja -> 0
            if (c[0x02] == 0) continue;                         // strcmp(name, "") == 0 -> 0
            c[0x00] = 1;                                        // 0x419D2D / 0x419D9A
        }
        if (b0[0x50] is { Length: >= 58 } z80) z80[0x00] = 1;  // 0x419DB1 byte[0x5057C0] := 1
        for (int r = 0; r < 200; r++) if (b0[r] != null && b0[r].Length >= 1 && b0[r][0] != 0) zeilen++;
        // 0x4B2290: Block 0 auf 1..7, ganze Zeilen
        for (int p = 1; p < 8; p++)
            for (int r = 0; r < 200; r++)
                _bauteile[p][r] = (byte[])b0[r].Clone();
        GefechtBesitzVerteilt = true;
        GefechtBesitzZeilen = zeilen;
        GD.Print($"gefecht-besitz: Techstandard {techstufe} -> {zeilen} Bauteilzeilen besessen " +
                 "(0x419CB0: Zeilen 0..99 und 160..179 mit +0x24 <= Stufe und Namen, Zeile 80; " +
                 "Block 0 auf Spieler 1..7 kopiert)");
        return zeilen;
    }
}
