namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// <b>TAKT 0 — »AI: test of life«</b> (C <c>0x4BAB40</c>, F <c>0x4BA640</c>),
/// gebaut 02.10.2026 (bug-396). Gelesen in berichte/ki-spaete-spieler-fable.md §3,
/// gezählt über alle 33 Karten und die acht NET-Karten im Abschnitt »Zählung 33
/// Missionen + Gefecht« desselben Berichts.
///
/// <code>
///   ai_tick 0x4BFB80, fuer jeden Spieler mit Kopf sec53[40p] == 1 und
///   sec106[p] == 0 (0x4BFBFA), Takt 0 (Bild % 50 == 0, ohne sec61-Tor):
///   0x4BAB69  ein Gebaeude mit Besitzer p und Art 1, 9 oder 11  -> LEBT
///   0x4BABB6  ein belegter Einheitensatz 1000p..1000p+999 (faze != 0xFF) -> LEBT
///   0x4BABED  ein sec19-Satz Art != 0, Besitzer p, Art nicht 13/14 -> LEBT
///   0x4BAC37  Kopf := 0xFF                                ; AUSGESCHIEDEN
///   0x4BAC3F  jedes Gebaeude mit Besitzer p: +0x05 := 11, +0x41 := 11
///   0x4BAC77  sec56[p] := 0
/// </code>
///
/// <para>⚠⚠ <b>NICHT endgültig — berichtigt am 05.10.2026 (bug-436).</b> Hier stand
/// »kein Weg innerhalb der Mission schreibt den Kopf zurück auf 1 (13 Schreiber C/F
/// gelesen)«. Die 13 waren nur die berechneten Adressen. Die MISSIONSSKRIPTE
/// schreiben den Kopf per direktem <c>mov byte [0x87B140 + 40p], 1</c> — 18 Stellen
/// in 11 Missionen, in C und F an denselben Regeln (M1, 11, 12, 13, 18, 20, 22, 24,
/// 27, 28, 31), fast immer unmittelbar vor <c>ai_mode</c>/<c>set_ai</c>: so WECKT
/// ein Skript eine ausgeschiedene KI, nachdem es ihr per <c>space_in</c>/
/// <c>place_unit</c> Einheiten gegeben hat. Eine Stelle schreibt 0 (M28 @0x4A2A77,
/// P5): Kopf »Mensch« — nicht mehr geprüft, kein ai_tick, und P5 sammelt wie der
/// Mensch neutrale Einheiten ein (Takeover @0x41135E). Wirkung <c>kopf</c> im
/// Missionsskript, <see cref="SkriptKopf"/>. Gegenschalter <c>--kopf-alt</c>:
/// der Stand 5b069b2 (die Wirkung tut nichts).</para>
///
/// <para>Ohne Skript bleibt es dabei: wer per <c>space_in</c>/<c>place_unit</c>
/// später Einheiten bekommt, bleibt draußen (<c>space_in</c> 0x4C17C0…0x4B34E0
/// fasst sec53 nie an) — seine Einheiten stehen und wehren sich nur über den
/// Selbstverteidiger (0x411770).</para>
///
/// <para>⭐ <b>Wer geprüft wird.</b> Kampagne: der Lader 0x41E070 macht P1…P7 zu
/// Rechnern (0xFF → 1) und P0 zum Menschen — also jeder außer dem Betrachter,
/// AUCH wer bei uns in Bereitschaft steht oder nicht in der KI-Liste ist. Gefecht:
/// die leeren Plätze hat <c>spieler_verteilen</c> 0x41B310 schon auf 0xFF gesetzt,
/// es bleiben die Rechner unserer KI-Liste. Die Prüfung steht im ai_tick, der
/// keine Kampagnenweiche hat (nur Mission 14 @0x4BFCAF, in einem späteren Takt) —
/// sie gilt darum <b>auch im Gefecht</b>.</para>
///
/// <para>⚠ <b>Zeitpunkt.</b> Bei uns <c>_taktNr % 50 == 0</c>, also erstmals im
/// Takt 50 (1 s) — dieselbe Zählweise wie Takt 48 der Teilespende. Ob das
/// Original schon Bild 0 prüft (Bildzähler := 0 im Lader 0x41EF8B, erhöht bei
/// 0x4160A5 vor der CPU-Station 0x41618C), ist V; beides liegt vor jeder
/// Verstärkung (früheste: M24 bei 15 s).</para>
///
/// <para>⚠ <b>Was wir anders zählen müssen.</b> »Einheitensatz« ist bei uns jedes
/// lebende Nicht-Gebäude mit Besitzer p (auch im Depot — dort liegen bei uns
/// Satzindizes); das Original sucht nach Block (Platz/1000), wir nach Besitzer —
/// change_owner verlegt den Satz im Original in den neuen Block, also dasselbe.
/// Flugzeug = <see cref="Special"/> mit Kind != 0 und nicht 13/14.</para>
///
/// <para>Gegenschalter <c>--lebenspruefung-aus</c>: der alte Stand (wer nichts
/// hat, pausiert in AiTick und läuft wieder an, sobald er etwas hat; Gebäude
/// bleiben beim Besitzer). Prüfstand <c>--lebenspruefung-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--lebenspruefung-aus</c> — keine Lebensprüfung (Stand vor dem
    /// 02.10.2026).</summary>
    public static bool LebenspruefungAus;

    /// <summary>sec53[40p] == 0xFF — der Spieler ist ausgeschieden. Wird beim
    /// Missionsstart (EnableSkirmishAi) geleert, wie der Lader 0xFF → 1 macht.</summary>
    private readonly bool[] _kiAusgeschieden = new bool[8];

    /// <summary>Wann (Takt) und wie viele Gebäude ein Ausgeschiedener an 11 verlor
    /// — für den Prüfstand.</summary>
    private readonly int[] _ausgeschiedenTakt = { -1, -1, -1, -1, -1, -1, -1, -1 };
    private readonly int[] _ausgeschiedenGebaeude = new int[8];

    public bool KiAusgeschieden(int p) => p is >= 0 and <= 7 && _kiAusgeschieden[p];

    private void LebenspruefungZuruecksetzen()
    {
        for (int p = 0; p < 8; p++)
        {
            _kiAusgeschieden[p] = false;
            _ausgeschiedenTakt[p] = -1;
            _ausgeschiedenGebaeude[p] = 0;
            _kopfMensch[p] = false;
            _kopfGeweckt[p] = 0;
        }
    }

    /// <summary>Hat der Kopf dieses Spielers die 1 (Rechner)? Siehe Kopfkommentar.</summary>
    private bool KopfRechner(int p)
    {
        if (p is < 0 or > 7 || _kiAusgeschieden[p] || _kopfMensch[p] || p == ViewPlayer) return false;
        return InCampaign || _ai.Any(a => a.Player == p);
    }

    // ================= bug-436: das Skript schreibt den Kopf =================

    /// <summary><c>--kopf-alt</c> — Stand 5b069b2: die Skriptwirkung <c>kopf</c>
    /// tut nichts, eine ausgeschiedene KI bleibt draußen.</summary>
    public static bool KopfAlt;

    /// <summary>sec53[40p] == 0, von einem Skript gesetzt (M28 @0x4A2A77).</summary>
    private readonly bool[] _kopfMensch = new bool[8];

    /// <summary>Wie oft ein Skript den Kopf auf 1 gesetzt hat — für den Prüfstand.</summary>
    private readonly int[] _kopfGeweckt = new int[8];

    /// <summary>Kopf == 0: der Mensch, oder ein Spieler, dem das Skript den Kopf 0
    /// gegeben hat. Gilt für das Einsammeln neutraler Einheiten (@0x41135E).</summary>
    private bool KopfMensch(int p)
        => p == ViewPlayer || (p is >= 0 and <= 7 && !KopfAlt && _kopfMensch[p]);

    /// <summary>Der Kopf, wie das Original ihn liest: 0 Mensch, 1 Rechner, 0xFF
    /// ausgeschieden. ⚠ Der neutrale P7 (sec106) hat im Original Kopf 1; er wird
    /// nur nicht geprüft — darum zählt <see cref="IsNeutralPlayer"/> hier nicht.</summary>
    private int KopfWert(int p)
    {
        if (p is < 0 or > 7) return 0xFF;
        if (p == ViewPlayer || (!KopfAlt && _kopfMensch[p])) return 0;
        return _kiAusgeschieden[p] ? 0xFF : 1;
    }

    /// <summary>
    /// <c>mov byte [0x87B140 + 40·p], wert</c> aus einem Missionsblock (bug-436).
    /// 1 = Rechner: ausgeschieden zurücknehmen und — wenn er noch nicht dabei ist —
    /// in die KI-Liste aufnehmen, mit denselben Ausschlüssen wie beim Missionsstart
    /// (<see cref="StartCampaign"/>: neutral/sec106, Bereitschaft). 0 = Mensch:
    /// aus der KI-Liste, nicht mehr geprüft, Gebäude bleiben beim Besitzer.
    /// <para>⚠ Der Kopf wird geschrieben, wie er ist — auch wenn der Spieler gar
    /// nicht ausgeschieden war; dann ändert die 1 nichts. Ob er danach lebt,
    /// entscheidet die nächste Lebensprüfung, genau wie im Original.</para>
    /// </summary>
    private void SkriptKopf(int p, int wert)
    {
        if (KopfAlt || p is < 0 or > 7 || p == ViewPlayer) return;
        if (wert == 0)
        {
            _kopfMensch[p] = true;
            int n = _ai.Count;
            _ai.RemoveAll(a => a.Player == p);
            if (n != _ai.Count)
                GD.Print($"KI: Skript setzt Kopf P{p} := 0 (Mensch) — aus der KI-Liste, nicht mehr geprueft");
            return;
        }
        if (wert != 1) return;                         // 0xFF schreibt kein Skript
        bool warAus = _kiAusgeschieden[p], warMensch = _kopfMensch[p];
        _kiAusgeschieden[p] = false;
        _kopfMensch[p] = false;
        _kopfGeweckt[p]++;
        if (_ai.Any(a => a.Player == p)) return;
        if (IsNeutralPlayer(p) || _standby[p]) return;
        if (VerbuendeteOhneKi && Allied(ViewPlayer, p)) return;
        var neu = new AiPlayer(p) { Level = _aiStufe, Think = 0.5f + p * 0.13f };
        AiLoadPlan(neu);
        _ai.Add(neu);
        _aiOn = true;
        GD.Print($"KI: Skript setzt Kopf P{p} := 1 — " +
                 (warAus ? "war ausgeschieden, " : warMensch ? "war Mensch-Kopf, " : "") +
                 $"in die KI-Liste aufgenommen (Takt {_taktNr})");
    }

    /// <summary>sec106[p] != 0. ⚠ Bei uns steht das in ZWEI Feldern: der Nachbau
    /// <c>_aiSec106</c> (SetAiSkriptsperre, heute ohne Rufer) und der neutrale Platz
    /// aus campaign_diplomacy.json — <c>set_neutral</c> 0x4D09F0 schreibt genau
    /// <c>byte[0xB38D38 + p]</c>, 13 der 14 Rufe für P7. Der neutrale P7 wird darum
    /// in keiner der 33 Missionen geprüft.</summary>
    private bool Sec106(int p) => AiGesperrt(p) || IsNeutralPlayer(p);

    /// <summary>Takt 0 der KI-Runde: die Lebensprüfung für jeden Rechner.</summary>
    private void LebenspruefungTakt()
    {
        if (LebenspruefungAus || _taktNr % 50 != 0) return;
        if (!InCampaign && !_aiOn) return;
        for (int p = 0; p < 8; p++)
        {
            if (!KopfRechner(p)) continue;
            if (Sec106(p)) continue;                        // 0x4BFBFA sec106: kein ai_tick
            if (LebtNachTestOfLife(p)) continue;
            SpielerAusscheiden(p);
        }
    }

    /// <summary>0x4BAB69…0x4BAC30, die drei Lebenszeichen.</summary>
    private bool LebtNachTestOfLife(int p)
    {
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead || e.Owner != p) continue;
            if (e.IsBuilding) { if (e.BType is 1 or 9 or 11) return true; }   // 0x4BAB8C
            else return true;                                                 // 0x4BABD9
        }
        foreach (var a in _special)                                           // 0x4BABFD
            if (!a.Dead && a.Kind != 0 && a.Owner == p && a.Kind is not (13 or 14)) return true;
        return false;
    }

    /// <summary>0x4BAC37…0x4BAC77: Kopf 0xFF, Gebäude an 11, sec56 leer.</summary>
    private void SpielerAusscheiden(int p)
    {
        _kiAusgeschieden[p] = true;
        _ausgeschiedenTakt[p] = _taktNr;
        int n = 0;
        var namen = new List<string>();
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Owner != p) continue;
            // +0x05 Besitzer und +0x41 — sonst nichts: keine Routen, keine
            // Insassen, kein Klang (anders als die Einnahme 0x43CD75ff.).
            b.Owner = b.Team = b.ShownOwner = NeutralOwner;
            n++;
            if (namen.Count < 6) namen.Add($"{(b.Name.Length > 0 ? b.Name : BuildingTypeName(b.BType))} (Platz {b.Slot}, Art {b.BType})");
        }
        _ausgeschiedenGebaeude[p] = n;
        _aiRaster[p] = new AiSektor[SektorZahl];               // 0x4BAC77 sec56[p] := 0
        int vorher = _ai.Count;
        _ai.RemoveAll(a => a.Player == p);
        GD.Print($"KI: Spieler {p} ausgeschieden (test of life 0x4BAB40, Takt {_taktNr})" +
                 (vorher != _ai.Count ? ", aus der KI-Liste genommen" : "") +
                 (n > 0 ? $"; {n} Gebaeude an Besitzer 11: {string.Join(", ", namen)}" : ""));
        if (n > 0) QueueRedraw();
    }

    // ================= der Prüfstand =========================================

    /// <summary>
    /// <c>--lebenspruefung-check[=sekunden]</c>: die Mission <paramref name="sekunden"/>
    /// laufen lassen und die Lebensprüfung gegen die Zählung vergleichen.
    /// Soll (Abschnitt »Zählung« im Bericht): K13 P4 ausgeschieden (P1, P2 nicht);
    /// K8 P3 ausgeschieden, seine Radarstellung Faro (Platz 4) an Besitzer 11.
    /// Nullmodell <c>--lebenspruefung-aus</c>: niemand ausgeschieden, Faro bleibt P3.
    /// Jede andere Mission: die Liste der Ausgeschiedenen wird gedruckt und gegen
    /// die Lebendliste beim Start geprüft (wer etwas Lebendes hat, darf nicht fallen).
    /// </summary>
    public string LebenspruefungCheck(int sekunden)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new System.Text.StringBuilder($"lebenspruefung-check K{m}, {sekunden} s, --lebenspruefung-aus {LebenspruefungAus}\n");
        if (!InCampaign) return sb.Append("  keine Kampagne — ungeprueft\n  DURCHGEFALLEN").ToString();
        MissionScriptTick(0.001f);

        // die Lebendliste beim Start, mit derselben Regel gerechnet
        var lebtStart = new bool[8];
        var gebStart = new int[8];
        for (int p = 0; p < 8; p++)
        {
            lebtStart[p] = LebtNachTestOfLife(p);
            foreach (var b in _entities) if (b.IsBuilding && !b.IsProp && !b.Dead && b.Owner == p) gebStart[p]++;
        }
        sb.AppendLine("  beim Start: " + string.Join(" ", Enumerable.Range(1, 7).Select(p =>
            $"P{p}={(lebtStart[p] ? "lebt" : "tot")}{(gebStart[p] > 0 ? $"/{gebStart[p]}Geb" : "")}" +
            $"{(Sec106(p) ? "/sec106" : "")}{(_ai.Any(a => a.Player == p) ? "/KI" : "")}")));
        var faro = _entities.Find(b => b.IsBuilding && !b.IsProp && b.Slot == 4);
        int faroVor = faro?.Owner ?? -99;

        int takte = sekunden * SimHz;
        for (int t = 0; t < takte; t++) SimTickFuerProbe();

        var aus = Enumerable.Range(0, 8).Where(KiAusgeschieden).ToList();
        sb.AppendLine("  ausgeschieden: " + (aus.Count == 0 ? "niemand" : string.Join(", ", aus.Select(p =>
            $"P{p} (Takt {_ausgeschiedenTakt[p]}, {_ausgeschiedenGebaeude[p]} Geb. an 11)"))));
        sb.AppendLine($"  KI-Liste danach: {string.Join(", ", _ai.Select(a => a.Player))}");

        var fehler = new List<string>();
        // Allgemein: niemand, der beim Start lebte, faellt in der ersten Sekunde
        foreach (int p in aus)
        {
            if (lebtStart[p] && _ausgeschiedenTakt[p] <= SimHz) fehler.Add($"P{p} lebte beim Start und fiel in Takt {_ausgeschiedenTakt[p]}");
            if (_ausgeschiedenTakt[p] > SimHz && !lebtStart[p]) fehler.Add($"P{p} fiel erst in Takt {_ausgeschiedenTakt[p]}, Soll Takt {SimHz}");
            if (Sec106(p)) fehler.Add($"P{p} hat sec106 und darf nicht geprueft werden");
            if (_ai.Any(a => a.Player == p)) fehler.Add($"P{p} steht noch in der KI-Liste");
        }
        int sollTot = 0;
        for (int p = 1; p < 8; p++)
            if (!lebtStart[p] && !Sec106(p) && p != ViewPlayer)
            {
                sollTot++;
                if (!LebenspruefungAus && !aus.Contains(p)) fehler.Add($"P{p} war beim Start tot und ist nicht ausgeschieden");
            }
        if (m == 13 && !LebenspruefungAus)
        {
            if (!aus.Contains(4)) fehler.Add("K13: P4 nicht ausgeschieden");
            if (aus.Contains(1) || aus.Contains(2)) fehler.Add("K13: P1/P2 ausgeschieden");
        }
        if (m == 8)
        {
            sb.AppendLine($"  K8 Faro (Platz 4, Radarstellung): Besitzer vorher {faroVor}, nachher {faro?.Owner ?? -99}");
            if (!LebenspruefungAus && (faro == null || faro.Owner != NeutralOwner || !aus.Contains(3)))
                fehler.Add("K8: P3 nicht ausgeschieden oder Faro nicht an 11");
        }
        if (LebenspruefungAus)
        {
            if (aus.Count > 0) fehler.Add("Nullmodell: trotz --lebenspruefung-aus ausgeschieden");
            if (m == 8 && (faro == null || faro.Owner != 3)) fehler.Add("Nullmodell K8: Faro nicht mehr bei P3");
        }
        sb.AppendLine($"  Soll ausgeschieden (beim Start tot, ohne sec106, nicht Betrachter): {sollTot}");
        foreach (var f in fehler) sb.AppendLine("  FEHLER: " + f);
        sb.Append(fehler.Count == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    // ================= bug-436: der Prüfstand zum Skript-Kopf ================

    /// <summary>
    /// <c>--kopf-check</c>: 60 Takte laufen lassen (die Lebensprüfung in Takt 50
    /// ist durch), dann JEDE Regel dieser Mission mit einer <c>kopf</c>-Wirkung
    /// über <see cref="Campaign.MissionScript.ErzwingeRegelFuerProbe"/> auslösen —
    /// also über die echte Abschrift, mit allen Wirkungen der Regel — und sofort
    /// prüfen: Kopf 1 → nicht ausgeschieden und in der KI-Liste (außer neutral/
    /// Bereitschaft); Kopf 0 → Kopf 0 und nicht in der KI-Liste. Danach 120 Takte:
    /// wer geweckt wurde und ein Lebenszeichen hat, muss noch in der Liste stehen
    /// und gezogen haben; wer keins hat, scheidet wieder aus (wie im Original).
    /// Nullmodell <c>--kopf-alt</c>: muss DURCHFALLEN, sobald ein Geweckter vorher
    /// ausgeschieden war.
    /// </summary>
    public string KopfCheck()
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new System.Text.StringBuilder($"kopf-check (bug-436) K{m}, --kopf-alt {KopfAlt}\n");
        if (InCampaign) MissionScriptTick(0.001f);                 // legt _mscript erst an
        if (!InCampaign || _mscript == null) return sb.Append("  keine Kampagne — ungeprueft\n  DURCHGEFALLEN").ToString();
        for (int t = 0; t < 60; t++) SimTickFuerProbe();
        sb.AppendLine("  Takt 60: ausgeschieden " + string.Join(",", Enumerable.Range(0, 8).Where(KiAusgeschieden).Select(p => "P" + p)) +
                      "; KI-Liste " + string.Join(",", _ai.Select(a => "P" + a.Player)));

        var regeln = _mscript.KopfRegelnFuerProbe();
        if (regeln.Count == 0) return sb.Append("  keine kopf-Regel in dieser Mission\n  BESTANDEN (nichts zu pruefen)").ToString();

        var fehler = new List<string>();
        var geweckt = new HashSet<int>();
        int warAusUndGeweckt = 0;
        foreach (var (at, p, w) in regeln)
        {
            int vor = KopfWert(p);
            bool vorKi = _ai.Any(a => a.Player == p);
            int n = _mscript.ErzwingeRegelFuerProbe(at);
            int nach = KopfWert(p);
            bool nachKi = _ai.Any(a => a.Player == p);
            bool ausgenommen = IsNeutralPlayer(p) || _standby[p] || (VerbuendeteOhneKi && Allied(ViewPlayer, p));
            sb.AppendLine($"  Regel 0x{at:X6} kopf(P{p}, {w}): {n} Wirkungen; Kopf {vor} -> {nach}, KI-Liste {(vorKi ? "ja" : "nein")} -> {(nachKi ? "ja" : "nein")}" +
                          (ausgenommen ? " (neutral/Bereitschaft)" : ""));
            if (w == 1)
            {
                if (vor == 0xFF) warAusUndGeweckt++;
                if (nach != 1) fehler.Add($"0x{at:X6}: Kopf P{p} ist {nach}, Soll 1");
                if (!nachKi && !ausgenommen) fehler.Add($"0x{at:X6}: P{p} nicht in der KI-Liste");
                if (nachKi) geweckt.Add(p);
            }
            else
            {
                geweckt.Remove(p);
                if (nach != 0) fehler.Add($"0x{at:X6}: Kopf P{p} ist {nach}, Soll 0");
                if (nachKi) fehler.Add($"0x{at:X6}: P{p} steht noch in der KI-Liste");
            }
        }

        var zuegeVor = _ai.ToDictionary(a => a.Player, a => a.Zuege);
        for (int t = 0; t < 120; t++) SimTickFuerProbe();
        foreach (int p in geweckt.OrderBy(x => x))
        {
            var a = _ai.Find(x => x.Player == p);
            bool lebt = LebtNachTestOfLife(p);
            int z = a == null ? -1 : a.Zuege - zuegeVor.GetValueOrDefault(p, 0);
            sb.AppendLine($"  nach 120 Takten: P{p} Kopf {KopfWert(p)}, {(lebt ? "lebt" : "ohne Lebenszeichen")}, " +
                          (a == null ? "nicht in der KI-Liste" : $"KI-Liste ja, {z} Zuege") +
                          (_ausgeschiedenTakt[p] > 60 ? $", erneut ausgeschieden in Takt {_ausgeschiedenTakt[p]} (wie Original)" : ""));
            if (lebt && a == null) fehler.Add($"P{p} lebt, ist aber aus der KI-Liste gefallen");
            if (lebt && a != null && z <= 0) fehler.Add($"P{p} hat nach dem Wecken nicht gezogen");
        }
        sb.AppendLine($"  {regeln.Count} kopf-Regeln, davon {warAusUndGeweckt} weckten einen Ausgeschiedenen");
        if (KopfAlt) sb.AppendLine("  Nullmodell (--kopf-alt): muss durchfallen, sobald ein Ausgeschiedener geweckt werden sollte");
        foreach (var f in fehler) sb.AppendLine("  FEHLER: " + f);
        sb.Append(fehler.Count == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
