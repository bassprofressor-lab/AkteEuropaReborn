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
/// <para>⭐ <b>Endgültig.</b> Kein Weg innerhalb der Mission schreibt den Kopf
/// zurück auf 1 (13 Schreiber C/F gelesen; <c>space_in</c> 0x4C17C0…0x4B34E0 fasst
/// sec53 nie an). Ein Spieler, der per <c>space_in</c>/<c>place_unit</c> später
/// Einheiten bekommt, bleibt draußen — seine Einheiten stehen und wehren sich nur
/// über den Selbstverteidiger (0x411770).</para>
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
        }
    }

    /// <summary>Hat der Kopf dieses Spielers die 1 (Rechner)? Siehe Kopfkommentar.</summary>
    private bool KopfRechner(int p)
    {
        if (p is < 0 or > 7 || _kiAusgeschieden[p] || p == ViewPlayer) return false;
        return InCampaign || _ai.Any(a => a.Player == p);
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
}
