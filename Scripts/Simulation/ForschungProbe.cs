using Godot;
using System.Text;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>`--forschung-probe` — KOSTET SIE, WAS SIE KOSTEN SOLL, UND WIRKT SIE?</b>
///
/// <para>Zu <see cref="ForschungKaufen"/> (Simulation/Forschung.cs). Gebaut am
/// 05.09.2026 zusammen mit der Forschung selbst.</para>
///
/// <para><b>Drei Fragen, drei Messungen:</b></para>
///
/// <list type="number">
///   <item>⭐⭐ <b>Trifft die Preisformel die gelesenen Zahlen?</b> Das ist die
///   einzige Stelle, an der sich unsere Rechnung gegen etwas prüfen lässt, das
///   nicht von uns stammt: der Bericht rechnet aus der EXE-Scheibe 0 für
///   Missionsnummer 3 die <b>Kanone auf 199 $</b>, das <b>M-Gewehr auf 142 $</b>
///   und die <b>Reifen (0xA1) auf 142 $</b> aus. Trifft unsere Formel die drei,
///   stimmen Vorzeichen, Deckel und die Ganzzahldivision <c>wert/2</c> — verrechnet
///   man sich im Vorzeichen, kommen 2293 $ heraus, also das Zehnfache.</item>
///   <item><b>Ist der Preis die Dauer?</b> Gekauft wird an einer echten Basis;
///   danach zählt die Probe die Takte bis zum Abschluss. Erwartung: <b>genau der
///   Preis</b>, denn <c>0x4AB5A5</c> zählt einen Punkt je Takt.</item>
///   <item><b>Kommt beim Bauteil etwas an?</b> Stufe, Hauptwert <c>+0x0E</c> und
///   der Tank <c>+0x1A</c> vorher und nachher. ⚠ Der Tank wächst nur bei Wurf 2 —
///   die Probe sagt darum den Arm mit an, statt aus einem Lauf zu schliessen.</item>
/// </list>
///
/// <para>Dazu die vierte, kleine: <b>bricht eine zweite Forschung an derselben
/// Basis die erste ab?</b> (Löschlauf <c>0x4AAC11</c>/<c>0x4AB853</c>, ohne
/// Rückzahlung.)</para>
///
/// <para>Aufruf:</para>
/// <code>
///   --campaign=3 --forschung-probe --quit-after=10
///   --campaign=3 --forschung-probe --forschung-besitz-alt --quit-after=10
///   --campaign=3 --forschung-probe --forschung-alt --quit-after=10
/// </code>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _forschungProbeRc = 2;

    /// <summary>0 bestanden · 1 durchgefallen · 2 nichts gemessen.</summary>
    public int ForschungProbeRc() => _forschungProbeRc;

    public string ForschungProbeLine()
    {
        var sb = new StringBuilder("forschung-probe:\n");
        int mission = UI.SkirmishSetup.CampaignMission;
        int grund = ForschungGrundpreis();
        sb.Append($"   Mission {mission}, Grundpreis (LEITER) {grund}\n");
        if (ForschungAlt)
        {
            sb.Append("   --forschung-alt: die alte Forschung laeuft, nichts gemessen\n");
            return sb.ToString();
        }
        if (grund <= 0)
        {
            sb.Append("   ⚠ keine Preisleiter — Maps/research_ladder.json fehlt\n");
            _forschungProbeRc = 1;
            return sb.ToString();
        }

        int spieler = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        // ---- 1. die Preise gegen die Lesung ------------------------------------
        // Die Erwartungen gelten fuer Missionsnummer 3 und Stufe 0, aus
        // berichte/forschung-fable.md Abschnitt 5.
        bool preisOk = true;
        if (mission == 3)
        {
            (int Bauteil, int Erwartet)[] tafel =
                { (0x01, 199), (0x04, 142), (0xA1, 142), (0x02, 1093) };
            foreach (var (b, erwartet) in tafel)
            {
                int ist = ForschungPreis(spieler, b);
                bool ok = ist == erwartet;
                preisOk &= ok;
                sb.Append($"   Preis {ForschungBauteilName(b),-14} {ist,6} $  " +
                          $"(gelesen {erwartet}) {(ok ? "✓" : "⚠ WEICHT AB")}\n");
            }
        }
        else
        {
            sb.Append("   (Preisvergleich nur bei --campaign=3 — die gelesene " +
                      "Tafel gilt fuer Missionsnummer 3)\n");
        }

        var angebot = ForschungAngebote(spieler);
        sb.Append($"   Angebot: {angebot.Count} Zeilen");
        if (angebot.Count > 0)
            sb.Append($" — erste: {angebot[0].Name} ${angebot[0].Preis}, " +
                      $"letzte: {angebot[^1].Name} ${angebot[^1].Preis}");
        sb.Append('\n');
        // ⭐ Die Namen einzeln — sie sind der eigentliche Beleg. Der Leselauf
        // nennt fuer Missionsnummer 3 aus dem SKRIPTBLOCK der EXE Kanone,
        // M-Gewehr, Reifen und 6x6 Reifen; unsere Liste kommt aus dem
        // exportierten campaign.json. Stehen dieselben vier da, haben sich zwei
        // unabhaengige Lesungen derselben Sache getroffen.
        if (angebot.Count > 0 && angebot.Count <= 12)
        {
            sb.Append("   Bauteile:");
            foreach (var z in angebot) sb.Append($" {z.Name}({z.Bauteil})");
            sb.Append('\n');
        }
        if (angebot.Count == 0)
        {
            sb.Append("   ⚠ nichts angeboten — in Mission 1 ist das der ERWARTETE " +
                      "Befund: ihr Missionsblock hat keinen set_part-Ruf\n");
            _forschungProbeRc = mission == 1 ? 0 : 1;
            return sb.ToString();
        }

        // ---- 2. an einer echten Basis kaufen ------------------------------------
        Entity? basis = null;
        foreach (var e in _entities)
            if (e.IsBuilding && e.BType == 1 && !e.Dead && e.Owner == spieler) { basis = e; break; }
        if (basis == null)
        {
            sb.Append($"   ⚠ keine BASIS des Spielers {spieler} auf der Karte — " +
                      "ohne Basis wird nicht geforscht (das ist die Regel, nicht der Fehler)\n");
            _forschungProbeRc = 2;
            return sb.ToString();
        }

        // Die billigste Zeile, damit die Messung kurz bleibt.
        // ⚠ Mit --aufwertung-immer-tank die billigste FAHRWERKszeile: Arm 2 ist
        // nur dort der Tank (+0x1A). Bei einer Waffe trifft derselbe Arm die
        // Nachladezeit (+0x1E), und die Zeile »Tank 0→0« waere dann richtig und
        // trotzdem nichtssagend — genau die Sorte Messung, die nichts belegt.
        int wahl = -1;
        for (int i = 0; i < angebot.Count; i++)
        {
            if (AufwertungImmerTank && !angebot[i].Fahrwerk) continue;
            if (wahl < 0 || angebot[i].Preis < angebot[wahl].Preis) wahl = i;
        }
        if (wahl < 0) wahl = 0;
        var a = angebot[wahl];

        var c = BauteilFuer(spieler, a.Bauteil)!;
        int stufeVor = c[0x01], hauptVor = c[0x0E] | (c[0x0F] << 8),
            tankVor = c[0x1A] | (c[0x1B] << 8);

        int kontoVor = _money[spieler];
        if (kontoVor < a.Preis)
        {
            // ⚠ Das ist ein MESSWERKZEUG, keine Spielregel: ohne Geld gibt es
            // nichts zu messen. Es steht in der Ausgabe, damit niemand die Zahl
            // fuer einen Kontostand des Spiels haelt.
            _money[spieler] = a.Preis;
            sb.Append($"   (Konto {kontoVor} $ auf {a.Preis} $ gesetzt — Messwerkzeug)\n");
        }
        int kontoDavor = _money[spieler];
        bool gekauft = ForschungKaufen(basis, wahl);
        sb.Append($"   Kauf: {a.Name} Stufe {stufeVor} → {stufeVor + 1}, " +
                  $"${a.Preis}, Arm {a.Wurf}{(a.Wurf == 2 && a.Fahrwerk ? " (TANK)" : "")} " +
                  $"— {(gekauft ? "angenommen" : "⚠ ABGELEHNT")}\n");
        sb.Append($"   Konto {kontoDavor} → {_money[spieler]} " +
                  $"(erwartet {kontoDavor - a.Preis}) " +
                  $"{(_money[spieler] == kontoDavor - a.Preis ? "✓" : "⚠")}\n");

        // ---- 3. der Takt: der Preis IST die Dauer -------------------------------
        int takte = 0, deckel = a.Preis + 10;
        while (ForschungFertig == 0 && takte < deckel) { ForschungTick(); takte++; }
        bool dauerOk = takte == a.Preis;
        sb.Append($"   Dauer: {takte} Takte (erwartet {a.Preis}, " +
                  $"= {a.Preis / 50.0:0.0} s bei 50/s) {(dauerOk ? "✓" : "⚠ WEICHT AB")}\n");

        int stufeNach = c[0x01], hauptNach = c[0x0E] | (c[0x0F] << 8),
            tankNach = c[0x1A] | (c[0x1B] << 8);
        sb.Append($"   Bauteil {a.Bauteil}: Stufe {stufeVor}→{stufeNach}, " +
                  $"+0x0E {hauptVor}→{hauptNach}, Tank {tankVor}→{tankNach}\n");
        bool wirkungOk = stufeNach == stufeVor + 1 && hauptNach >= hauptVor;

        // ---- 4. der Loeschlauf --------------------------------------------------
        // Zwei Kaeufe hintereinander an DERSELBEN Basis: der erste muss fallen.
        int abgebrochenVor = ForschungAbgebrochen;
        var nachher = ForschungAngebote(spieler);
        bool loeschOk = true;
        if (nachher.Count > 0)
        {
            _money[spieler] = 60000;                       // Messwerkzeug
            ForschungKaufen(basis, 0);
            ForschungKaufen(basis, 0);
            loeschOk = ForschungAbgebrochen == abgebrochenVor + 1;
            sb.Append($"   Loeschlauf: zwei Kaeufe an derselben Basis → " +
                      $"{ForschungAbgebrochen - abgebrochenVor} Abbruch " +
                      $"(erwartet 1) {(loeschOk ? "✓" : "⚠")}\n");
        }

        bool alles = (mission != 3 || preisOk) && dauerOk && wirkungOk && loeschOk && gekauft;
        sb.Append($"   → {(alles ? "BESTANDEN" : "⚠ DURCHGEFALLEN")}\n");
        _forschungProbeRc = alles ? 0 : 1;
        return sb.ToString();
    }
}
