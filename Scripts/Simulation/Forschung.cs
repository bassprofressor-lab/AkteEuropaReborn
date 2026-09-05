using Godot;
using System;
using System.Collections.Generic;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐⭐ <b>DIE FORSCHUNG — der Auslöser, der der Aufwertung gefehlt hat.</b>
///
/// <para>Gebaut am 05.09.2026 nach <c>berichte/forschung-fable.md</c>. Der Anlass
/// steht in <c>STATUS.md</c> unter »Die Aufwertung fertig machen«: seit dem
/// 03.09. wächst der Tank (<see cref="Aufwerten"/>), aber <b>niemand rief es</b>
/// — <c>--aufwertung-probe</c> war der einzige Aufrufer im ganzen Programm.</para>
///
/// <para><b>Die alte Forschung stand auf einer falschen Prämisse</b>
/// (<c>OFFENE_FRAGEN.md</c> AN.7): sie SCHALTETE Zeilen 65…88 FREI, hing am
/// Gebäude in beliebiger Zahl, und Preis (2000) wie Dauer (5000/60) waren
/// erfunden. Das Original tut etwas anderes:</para>
///
/// <code>
///   Angebot   0x4AA950: 3 Erfindungen + JEDES besessene Bauteil unter Stufe 9
///   Kaufen    0x44A87D: Zeile wählen (Knopf 1000+i), dann Knopf 15
///                       -> Löschlauf, freier Laufsatz, Geld SOFORT und GANZ
///   Takt      0x4AB580: +1 je Simulationstakt, ⭐ DER PREIS IST DIE DAUER
///   Abschluss 0x4AAA80: die Bauteilzeile des Spielers wächst, Stufe +1
/// </code>
///
/// <para>⭐⭐ <b>Der Laufsatz hängt an der BASIS, nicht am Spieler</b>
/// (<c>0x44A8AF mov ax, word[edx+0x8B9044]</c> — das Fensterfeld mit dem
/// angeklickten Objekt, als ERSTES Argument an <c>0x4AB830</c>/<c>0x4AABD0</c>;
/// selbst nachgeschlagen). Der Spieler steht getrennt in <c>+0x0D</c>. Damit
/// berichtigt sich AN.3: nicht »eine Forschung je Spieler«, sondern <b>eine je
/// Basis</b>, zehn im ganzen Spiel. Wer zwei Basen hat, forscht zweimal.</para>
///
/// <para>⚠ <b>Eine Frage ist offen und steht als Schalter im Code</b>: welche
/// Bauteile ein Spieler überhaupt BESITZT (<c>+0x00</c> der Bauteilzeile). Der
/// Missionsstart <c>0x4B23C0</c> nullt den Merker für jede Zeile mit
/// <c>+0x24 != 10</c> — und <b>keine</b> Zeile der Auslieferung hat Techstufe
/// 10 (ausgezählt: die Stufen laufen 1…8; nur ERFUNDENE Waffen bekommen 10,
/// <c>0x4AB1B6</c>). Ein echter Spielstand bestätigt das: <c>game.007</c>
/// (Mission 1) trägt bei Kanone, S.Kanone und M-Gewehr <c>+0x00 = 0</c>. Der
/// einzige Verteiler, den ich finde, ist <c>0x419CB0</c> mit der Schranke
/// <c>byte[0x540EB8]</c>; woher die in der KAMPAGNE kommt, ist ungelesen.
/// Solange das so ist, bleibt der Besitz bei uns so, wie <c>PARTS.CWD</c> ihn
/// liefert (jede benannte Zeile besessen), und
/// <c>--forschung-missionsstart-treu</c> baut den Nulllauf des Originals nach —
/// dann ist die Aufwertung in der Kampagne <b>gar nicht</b> erreichbar. Beide
/// Richtungen sind messbar; die Vorgabe ist die, die etwas zu sehen gibt.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    // ---- die Schalter ---------------------------------------------------------

    /// <summary><c>--forschung-alt</c>: die alte Forschung (Freischalten von
    /// Zeilen 65…88, Preis 2000, Dauer 5000) statt der gelesenen. Der
    /// Gegenschalter zu dieser ganzen Datei.</summary>
    public static bool ForschungAlt;

    /// <summary><c>--forschung-besitz-alt</c>: den Besitzmerker so nehmen, wie
    /// <c>PARTS.CWD</c> ihn liefert (jede benannte Zeile besessen) statt aus dem
    /// Kampagnenfahrplan. Das ist der Stand vom Vormittag des 05.09.2026 —
    /// ⚠ und er bietet in Mission 3 <b>31</b> Bauteile an, wo das Original
    /// <b>vier</b> hergibt.</summary>
    public static bool ForschungBesitzAlt;

    // ---- die Grenzwerte, alle aus dem Bericht ---------------------------------

    /// <summary>Zehn Laufsätze — <c>cmp ax, 0xA</c> in allen fünf Schleifen
    /// (<c>0x4AB85C</c>, <c>0x4AAC1A</c>, …). Der elfte löst
    /// »Too many researches« aus.</summary>
    public const int ForschungPlaetze = 10;

    /// <summary>Stufe 9 ist das Ende; das Angebot zeigt nur <c>&lt; 9</c>
    /// (<c>0x4AA9DC cmp cl, 9 / jae</c>).</summary>
    public const int ForschungHoechstStufe = 9;

    /// <summary>Preisdeckel 30 000 (<c>0x4AA71F</c>) und Preisboden 1
    /// (<c>0x4AA74E</c>).</summary>
    public const int ForschungPreisDeckel = 30000;

    // ---- der Laufsatz (sec96, 10 × 16 B) --------------------------------------

    /// <summary>
    /// Ein Laufsatz des Originals, Feld für Feld (<c>0xA3A9D0</c>, 16 B).
    ///
    /// <para><c>Basis</c> ist <c>+0x00</c> — der GEBÄUDEindex, nicht der
    /// Spieler; <c>Marke</c> ist <c>+0x01</c> (0 = frei); <c>Preis</c>
    /// <c>+0x02</c>; <c>Fortschritt</c> <c>+0x04</c>; <c>+0x06…+0x0E</c> sind
    /// wortwörtlich die neun Argumente des Busbefehls 531, die beim KAUF
    /// gerechnet und bis zum Abschluss mitgetragen werden.</para></summary>
    public struct ForschungLauf
    {
        public int Basis;                    // +0x00  Gebäudeindex (unser Entity.Slot)
        public bool Belegt;                  // +0x01 != 0
        public int Preis;                    // +0x02  ⭐ ist zugleich die Dauer in Takten
        public int Fortschritt;              // +0x04
        public int Spieler;                  // +0x0D
        public AufwertungWerte Werte;        // +0x06…+0x0C, +0x0E
    }

    private readonly ForschungLauf[] _forschung = new ForschungLauf[ForschungPlaetze];

    /// <summary>Für die Probe: wie oft gekauft, abgeschlossen, abgebrochen, und
    /// wie oft »Too many researches« fiel.</summary>
    public int ForschungGekauft, ForschungFertig, ForschungAbgebrochen, ForschungKeinPlatz;

    // ---- das Angebot ----------------------------------------------------------

    /// <summary>Eine Zeile der Angebotstafel (<c>0xA39640</c>, 100 × 50 B). Wir
    /// halten sie als Liste statt als Tafel: sie ist reiner Laufzeitzustand,
    /// wird nach jedem Klick neu gebaut und <b>nicht</b> gesichert.</summary>
    public readonly record struct ForschungAngebot(int Bauteil, string Name, int Preis,
                                                   int Stufe, bool Fahrwerk, int Wurf,
                                                   AufwertungWerte Werte);

    /// <summary>⭐ <b>Der vorgewürfelte Arm der NÄCHSTEN Stufe</b>, je
    /// Aufwertungszeile — im Original das Laufzeitfeld <c>Zeile+0x02</c>, das
    /// <c>for_vyv</c> (<c>0x4AAA20</c>) beim Anwenden neu würfelt.
    ///
    /// <para>Er steht hier und nicht in der Zeile selbst, weil unsere
    /// Aufwertungstafel aus der JSON-Ausfuhr kommt und dort <b>überall 0</b>
    /// ist — genau wie in der EXE, und genau darum ist die 0 kein Wert,
    /// sondern der Anfangszustand (AN.4). Dass er anfangs 0 ist, ist
    /// originalgetreu: die erste Aufwertung jedes Bauteils trifft Arm 0.</para>
    /// </summary>
    private readonly Dictionary<int, int> _forschungWurf = new();

    /// <summary>
    /// ⭐⭐ <b>Die Angebotstafel bauen — <c>0x4AA950</c>.</b>
    ///
    /// <para>Kein Zufall, keine Auswahl: angeboten wird <b>alles</b>, was der
    /// Spieler besitzt und was unter Stufe 9 steht, in der Reihenfolge der
    /// Aufwertungstafel (Waffen <c>0x01…0x13</c>, dann Fahrwerke
    /// <c>0xA0…0xAF</c>). Höchstens 35 Zeilen; die 100er-Tafel des Originals
    /// wird nie voll, sein »Too many researches« an dieser Stelle ist tot.</para>
    ///
    /// <para>⚠ <b>Die drei ERFINDUNGEN fehlen</b> (Sätze 0–2 des Originals,
    /// feste Preise 500/2000/5000). Sie sind gelesen (AN.5: 40 Rezepte bei
    /// <c>0x502B00</c>), aber nicht gebaut — und eine Zeile anzubieten, die
    /// nichts täte, wäre schlimmer als sie wegzulassen. Sie steht als offene
    /// Arbeit im Bericht, Schritt 6.</para></summary>
    public List<ForschungAngebot> ForschungAngebote(int spieler)
    {
        var raus = new List<ForschungAngebot>();
        if (ForschungAlt || spieler is < 0 or > 7) return raus;
        AufwertungTafelLaden();
        if (_aufwertungTafel == null) return raus;
        var bauteile = new List<int>(_aufwertungTafel.Keys);
        bauteile.Sort();
        foreach (int b in bauteile)
        {
            if (!ForschungBesitzt(spieler, b)) continue;      // 0x4AA9D2
            var c = BauteilFuer(spieler, b);
            if (c == null || c.Length < 58) continue;
            if (c[0x01] >= ForschungHoechstStufe) continue;   // 0x4AA9DC
            // ⚠ --aufwertung-immer-tank ist ein MESSWERKZEUG und muss darum
            // schon in der VORSCHAU greifen: der Arm, den das Angebot zeigt,
            // ist der, der angewendet wird. Ohne das waere der Tankarm ueber
            // die Forschung erst bei der zweiten Aufwertung eines Bauteils zu
            // erreichen, weil der Anfangsarm 0 ist.
            int wurf = AufwertungImmerTank ? 2
                     : _forschungWurf.TryGetValue(b, out int w) ? w : 0;
            var werte = AngebotRechnen(spieler, b, wurf);
            if (!werte.Gueltig) continue;
            int preis = ForschungPreis(spieler, b);
            if (preis <= 0) continue;
            raus.Add(new ForschungAngebot(b, ForschungBauteilName(b), preis,
                                          c[0x01], werte.Fahrwerk, wurf,
                                          werte));
        }
        return raus;
    }

    /// <summary>
    /// ⭐⭐ <b>Besitzt dieser Spieler das Bauteil? — und das ist die Frage, an der
    /// die ganze Forschung hängt.</b> Das Original fragt
    /// <c>sec46[spieler][b] + 0x00</c> (<c>0x4AA9D2</c>); wir fragen dasselbe
    /// Byte. Die Frage war, <b>wer es setzt</b>, und die ist seit dem Nachtrag
    /// zum Leselauf beantwortet (<c>berichte/forschung-fable.md</c> 11.c/11.e):
    ///
    /// <code>
    ///   0x4D0290 Missionsstart:  Schatten → live, dann
    ///   0x4B23C0                 Besitz := 0 für ALLE 200 Zeilen (ausser Tech 10),
    ///                            Zeile 0x50 := 1 (0x4B23F9)
    ///   0x41F1E6 Kampagnenskript: der MISSIONSBLOCK setzt mit `set_part`
    ///                            (0x4D0520, 1037 Rufe) — ⭐ der letzte Schreiber
    ///   0x437F10 mission_init:   Spieler 0 → Spieler 1…7
    /// </code>
    ///
    /// <para>⭐ <b>Damit ist der Kampagnenfahrplan die Quelle</b>, und den haben
    /// wir seit dem 10.08.2026 exportiert (<c>campaign.json</c>, »components« =
    /// genau diese <c>set_part</c>-Rufe). Zwei unabhängige Lesungen derselben
    /// Sache treffen sich hier: der Leselauf nennt für Mission 2 Kanone,
    /// M-Gewehr und Reifen, für Mission 3 zusätzlich die 6x6-Reifen — und
    /// dasselbe steht in unserer Tafel.</para>
    ///
    /// <para>⚠ <b>Mission 1 gibt nichts</b>, und das ist kein Fehler: ihr Block
    /// hat null <c>set_part</c>-Rufe. Wer dort forschen will, kann nur
    /// ERFINDEN — und die Erfindung ist bei uns noch nicht gebaut.</para>
    ///
    /// <para>⚠ Im GEFECHT verteilt das Original nach der Technikstufe
    /// (<c>0x419CB0</c> mit <c>byte[0x540EB8]</c>), und die haben wir im
    /// Einstellschirm nicht. Dort bleibt es bei »jede benannte Zeile« —
    /// <b>[Setzung]</b>, dieselbe wie vor heute.</para></summary>
    private bool ForschungBesitzt(int spieler, int bauteil)
    {
        var c = BauteilFuer(spieler, bauteil);
        if (c == null || c.Length < 58) return false;
        if (c[0x00] == 0) return false;              // 0x4AA9D2, unsere Grundtafel

        int mission = UI.SkirmishSetup.CampaignMission;
        if (ForschungBesitzAlt || mission <= 0) return true;
        var u = Campaign.CampaignManager.UnlocksFor(mission);
        if (!u.Known) return true;                   // ohne Fahrplan der alte Stand

        // Die eine Zeile, die der Missionsstart selbst setzt (0x4B23F9).
        if (bauteil == 0x50) return true;
        if (u.Parts.Contains((spieler, bauteil))) return true;
        // mission_init kopiert Spieler 0 auf 1…7 (0x437F10) — ein KI-Spieler
        // besitzt, was der Mensch besitzt.
        return spieler != 0 && u.Parts.Contains((0, bauteil));
    }

    private string ForschungBauteilName(int bauteil)
    {
        var c = BauteilFuer(0, bauteil);
        if (c == null || c.Length < 0x14) return $"Bauteil {bauteil}";
        string n = Import.Cp437.GetString(c, 0x02, 18).Trim();
        return n.Length > 0 ? n : $"Bauteil {bauteil}";
    }

    // ---- der Preis ------------------------------------------------------------

    /// <summary>Die Missionsleiter aus <c>research_ladder.json</c> — 35 Wörter,
    /// 100…1500, Index ist die Missionsnummer.</summary>
    private static int[]? _forschungLeiter;

    private static void ForschungLeiterLaden()
    {
        if (_forschungLeiter != null) return;
        _forschungLeiter = Array.Empty<int>();
        string pfad = Core.Content.Path("Maps/research_ladder.json");
        if (!FileAccess.FileExists(pfad))
        {
            GD.PrintErr("forschung: Maps/research_ladder.json fehlt — ohne die " +
                        "Preisleiter wird nichts angeboten (einen Preis zu erfinden " +
                        "waere der Fehler, den AN.7 schon einmal aufgeschrieben hat)");
            return;
        }
        using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var json = new Json();
        if (json.Parse(f.GetAsText()) != Error.Ok ||
            json.Data.VariantType != Variant.Type.Dictionary) return;
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue("ladder", out var lv) ||
            lv.VariantType != Variant.Type.Array) return;
        var arr = lv.AsGodotArray();
        var v = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++) v[i] = arr[i].AsInt32();
        _forschungLeiter = v;
    }

    /// <summary>
    /// ⭐ <b>Der Grundpreis — die einzige Stelle, an der die MISSION mitredet.</b>
    /// Kampagne: <c>LEITER[Missionsnummer]</c>. Gefecht: <c>Technikstufe · 100</c>
    /// (<c>byte[0x540EB8]</c>).
    ///
    /// <para>⚠ <b>[Setzung]</b> im Gefecht: wir haben keine Technikstufe im
    /// Einstellschirm, darum 1 — dieselbe Zahl, die <c>LEITER[0]</c> gibt. Wenn
    /// der Schirm sie einmal bekommt, ist das hier die eine Zeile, die
    /// mitwandert.</para></summary>
    private static int ForschungGrundpreis()
    {
        ForschungLeiterLaden();
        int mission = UI.SkirmishSetup.CampaignMission;
        if (mission <= 0) return 100;                       // Gefecht: Technikstufe 1
        if (_forschungLeiter == null || _forschungLeiter.Length == 0) return 0;
        int i = Mathf.Clamp(mission, 0, _forschungLeiter.Length - 1);
        return _forschungLeiter[i];
    }

    /// <summary>
    /// ⭐⭐ <b>Der Preis einer Aufwertung — <c>0x4AA5F8…0x4AA752</c>.</b>
    ///
    /// <code>
    ///   p = 3.2^(Techstufe − Grund·0.01 + 1) · 2.3^Stufe · (Wert/2) · 10
    ///   p = min(p, 30000);  Preis = (int)p;  wenn 0 dann 1
    /// </code>
    ///
    /// <para>⭐ <b>Und der Preis IST die Dauer</b>: der Laufsatz zählt einen
    /// Punkt je Simulationstakt (<c>0x4AB5A5</c>), 50 in der Sekunde. Die Kanone
    /// in Missionsnummer 3 kostet 199 $ und braucht 199 Takte = 4,0 s.</para>
    ///
    /// <para>⚠ <c>Wert</c> ist VORZEICHENLOS (<c>and ebx, 0xFFFF00FF</c>), und
    /// <c>Wert/2</c> ist eine Ganzzahldivision. Beides steht so im Bericht,
    /// Abschnitt 5, mit dem Nullmodell dazu: mit umgedrehtem Vorzeichen wäre die
    /// Kanone teurer als der Laser.</para></summary>
    public int ForschungPreis(int spieler, int bauteil)
    {
        var c = BauteilFuer(spieler, bauteil);
        if (c == null || c.Length < 58) return 0;
        int grund = ForschungGrundpreis();
        if (grund <= 0) return 0;                    // keine Leiter, kein Preis
        int wert = bauteil >= 0xA0 ? c[0x21] : c[0x20];
        double p = Math.Pow(3.2, c[0x24] - grund * 0.01 + 1.0)
                 * Math.Pow(2.3, c[0x01])
                 * (wert / 2) * 10.0;
        if (p > ForschungPreisDeckel) p = ForschungPreisDeckel;
        int preis = (int)p;                          // ftol, zur Null
        return preis == 0 ? 1 : preis;
    }

    // ---- kaufen (0x44A87D) ----------------------------------------------------

    /// <summary>
    /// ⭐⭐ <b>Eine Forschung kaufen — der Bezahlknopf <c>0x44A87D</c>.</b>
    /// <paramref name="zeile"/> ist die gewählte Angebotszeile (im Original
    /// Knopf 1000+i), <paramref name="basis"/> das Gebäude, dessen Fenster
    /// offen ist.
    ///
    /// <para>Reihenfolge wie im Original: Geld prüfen → alle Laufsätze DIESER
    /// Basis löschen (ohne Rückzahlung, <c>0x4AAC11</c>/<c>0x4AB853</c>) →
    /// ersten freien Satz füllen → Geld ganz abziehen.</para>
    ///
    /// <para>⚠ <b>[Setzung] an genau einer Stelle:</b> findet das Original
    /// keinen freien Satz, zieht es das Geld trotzdem ab (Befehl 528 steht nach
    /// dem Ruf). Wir ziehen dann nichts ab — einen Fehler nachzubauen, der dem
    /// Spieler Geld nimmt, wäre keine Treue.</para></summary>
    public bool ForschungKaufen(Entity? basis, int zeile)
    {
        if (ForschungAlt || basis == null) return false;
        if (!basis.IsBuilding || basis.BType != 1 || basis.Dead)
        {
            _order = "Forschung — nur in der BASIS.";
            return false;
        }
        int spieler = Mathf.Clamp(basis.Owner, 0, 7);
        var angebot = ForschungAngebote(spieler);
        if (zeile < 0 || zeile >= angebot.Count)
        {
            _order = "Forschung — zuerst eine Zeile waehlen.";
            return false;
        }
        var a = angebot[zeile];
        if (_money[spieler] < a.Preis)
        {
            // Der Wortlaut des Originals, 0x44A8AA.
            _order = "Sie haben nicht genug Geld, um dieses Teil zu verbessern";
            return false;
        }

        // Der Löschlauf: alles, was an DIESER Basis läuft, fällt weg.
        for (int i = 0; i < ForschungPlaetze; i++)
            if (_forschung[i].Belegt && _forschung[i].Basis == basis.Slot)
            {
                _forschung[i].Belegt = false;
                ForschungAbgebrochen++;
            }

        int platz = -1;
        for (int i = 0; i < ForschungPlaetze && platz < 0; i++)
            if (!_forschung[i].Belegt) platz = i;
        if (platz < 0)
        {
            ForschungKeinPlatz++;
            _order = "Too many researches";          // 0x503F2C, der Text des Originals
            return false;
        }

        _forschung[platz] = new ForschungLauf
        {
            Basis = basis.Slot,
            Belegt = true,
            Preis = a.Preis,
            Fortschritt = 0,
            Spieler = spieler,
            Werte = a.Werte,
        };
        _money[spieler] -= a.Preis;                  // Befehl 528: sofort und ganz
        ForschungGekauft++;
        basis.State = StResearch;                    // »Status : forschen«
        _order = $"Forschung: {a.Name} auf Stufe {a.Stufe + 1} — ${a.Preis}";
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    // ---- der Takt (0x4AB580) --------------------------------------------------

    /// <summary>
    /// ⭐ <b>Ein Takt der Forschung — <c>0x4AB580</c>, gerufen aus dem
    /// Originaltakt (50/s), ohne Bedingung.</b>
    ///
    /// <para><c>Fortschritt += 1</c>, und beim Erreichen des Preises fällt der
    /// Abschluss. Das Original vergleicht mit <c>!=</c> (<c>0x4AB5AE</c>); wir
    /// nehmen <c>&gt;=</c>, damit ein einmal verpasster Gleichstand die
    /// Forschung nicht ewig laufen lässt — dieselbe Zahl, ein Riegel mehr.</para>
    /// </summary>
    private void ForschungTick()
    {
        if (ForschungAlt) return;
        for (int i = 0; i < ForschungPlaetze; i++)
        {
            if (!_forschung[i].Belegt) continue;
            _forschung[i].Fortschritt++;
            if (_forschung[i].Fortschritt < _forschung[i].Preis) continue;
            ForschungAbschluss(i, melden: true);
        }
    }

    /// <summary>Der Abschluss — Busbefehl 531, <c>0x4AAA80</c>. Meldung und
    /// Klang fallen nur beim Auftraggeber (<c>0x4AB674</c>: der Laufsatz
    /// vergleicht <c>+0x0D</c> mit dem eigenen Spieler).</summary>
    private void ForschungAbschluss(int i, bool melden)
    {
        var l = _forschung[i];
        _forschung[i].Belegt = false;
        if (!AufwertungSchreiben(l.Spieler, l.Werte)) return;
        ForschungFertig++;

        // ⭐ for_vyv (0x4AAA20): den Arm der NÄCHSTEN Stufe würfeln — erst
        // JETZT, damit das nächste Angebot eine ehrliche Vorschau zeigt.
        _forschungWurf[l.Werte.Bauteil] =
            AufwertungImmerTank ? 2 : Simulation.Determinism.Roll(4);
        AufwertungWurf[l.Werte.Wurf]++;
        if (l.Werte.Wurf == 2 && l.Werte.Fahrwerk) AufwertungTank++;

        var basis = ForschungBasis(l.Basis);
        if (basis != null && basis.State == StResearch) basis.State = StAktiv;
        if (!melden || l.Spieler != ViewPlayer) return;

        string name = ForschungBauteilName(l.Werte.Bauteil);
        _order = $"Nachricht des FORSCHUNGSLABORS: Verbesserung von {name} beendet";
        Audio.GameSounds.Play(Audio.GameSounds.ResearchDone);      // 136, 0x4AB750
        if (basis != null) NoteEvent(basis, $"{name} verbessert (Stufe {l.Werte.Stufe + 1})");
    }

    private Entity? ForschungBasis(int slot)
    {
        foreach (var e in _entities)
            if (e.Slot == slot && e.IsBuilding && !e.Dead) return e;
        return null;
    }

    /// <summary>
    /// <b>Missionsende — <c>0x4AB950</c>, »alle abfeuern«.</b> Jeder belegte
    /// Laufsatz wird sofort abgeschlossen, ohne Meldung und ohne Klang. Der
    /// Spieler hat bezahlt; das Original verschenkt den Rest der Dauer.</summary>
    public void ForschungMissionsende()
    {
        if (ForschungAlt) return;
        for (int i = 0; i < ForschungPlaetze; i++)
            if (_forschung[i].Belegt) ForschungAbschluss(i, melden: false);
    }

    // ---- was das Fenster zeigt ------------------------------------------------

    /// <summary>Die Angebotsliste für den Reiter »Forschung« des
    /// Basisfensters — dieselbe Liste, die <see cref="ForschungKaufen"/> nach
    /// Zeilennummer bedient.</summary>
    public List<UI.BuildPanel.Row> ForschungZeilen()
    {
        var raus = new List<UI.BuildPanel.Row>();
        var e = Producer();
        if (ForschungAlt || e == null || e.BType != 1) return raus;
        int spieler = Mathf.Clamp(e.Owner, 0, 7);
        if (ForschungLaeuft(e) != null) return raus;     // 0x46B575: statt der Liste der Fortschritt
        foreach (var a in ForschungAngebote(spieler))
        {
            // Der Arm ist die ehrliche Vorschau des Originals: was diese Stufe
            // ausser dem Hauptwert verbessert, steht schon fest.
            string was = a.Fahrwerk
                ? a.Wurf switch { 0 => "Panzerung", 1 => "Sicht", 2 => "TANK", _ => "Gewicht" }
                : a.Wurf switch { 0 => "Schaden", 1 => "Reichweite", 2 => "Nachladen", _ => "Gewicht" };
            raus.Add(new UI.BuildPanel.Row(
                $"{a.Name} — Stufe {a.Stufe} auf {a.Stufe + 1} (+{was})",
                $"${a.Preis}", _money[spieler] >= a.Preis, false));
        }
        return raus;
    }

    /// <summary>Der Laufsatz dieser Basis, oder null.</summary>
    private ForschungLauf? ForschungLaeuft(Entity basis)
    {
        for (int i = 0; i < ForschungPlaetze; i++)
            if (_forschung[i].Belegt && _forschung[i].Basis == basis.Slot)
                return _forschung[i];
        return null;
    }

    /// <summary>Was auf dem Reiter »Forschung« steht.</summary>
    public string ForschungNote()
    {
        if (ForschungAlt) return ResearchNoteAlt();
        var e = Producer();
        if (e == null) return "Forschung — kein Gebäude gewählt.";
        if (e.BType != 1)
            return "Forschung — nur in der BASIS. Dieses Gebäude kann nicht forschen.";
        int spieler = Mathf.Clamp(e.Owner, 0, 7);
        if (ForschungLaeuft(e) is { } l)
        {
            int prozent = l.Preis > 0 ? l.Fortschritt * 100 / l.Preis : 0;
            // Der Satz des Originals, 0x46B575: »done·100/total % fertig.«
            return $"{ForschungBauteilName(l.Werte.Bauteil)} auf Stufe " +
                   $"{l.Werte.Stufe + 1}\n{prozent} % fertig.\n\n" +
                   "Eine neue Forschung an dieser Basis bricht sie ab —\n" +
                   "ohne Rückzahlung.";
        }
        var angebot = ForschungAngebote(spieler);
        if (angebot.Count == 0)
            return "Forschung — kein Bauteil steht zur Verbesserung an.\n" +
                   "Die Mission hat noch keines freigegeben (Fahrplan set_part),\n" +
                   "oder alles steht auf Stufe 9.";
        return $"Zeile wählen, dann »Forschen«. Kontostand ${_money[spieler]}.\n" +
               "Der Preis ist zugleich die Dauer: 50 Punkte je Sekunde.";
    }

    // ---- Anschluss an den Rest -------------------------------------------------

    /// <summary>Taste O und der Knopf des Basisfensters: die gewählte Zeile
    /// kaufen.</summary>
    public void ForschungAusPanel(int zeile)
    {
        if (ForschungAlt) { ResearchFromPanel(); return; }
        AimAtPanelBuilding();
        foreach (int i in _sel)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.BType != 1 || e.Dead) continue;
            ForschungKaufen(e, zeile);
            return;
        }
        _order = "Forschung — keine Basis gewählt.";
    }
}
