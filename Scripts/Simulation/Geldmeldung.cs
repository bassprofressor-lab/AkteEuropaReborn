namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ 29.10.2026, bug-655 (KayelGee R8 K13): »Wenn eine Untermission mehrere
/// Zahlungen auszahlt, dann kommt normalerweise bei jeder eine Einblendung.«
///
/// <para>Der Bushandler 528, C <c>0x4C3A7C…0x4C3BED</c> (F <c>0x4C375C</c>),
/// gelesen in <c>berichte/missionsmeldungen-r8-opus.md</c> §3:</para>
/// <code>
///   geld[si] += Betrag ; &lt; 0 -> 0                     (Klemme, 0x4C3AA5)
///   si != eigener Spieler -> fertig                       (0x4C3AAF)
///   Betrag &gt; 0: "Einkommen: " (0x4FB6C8) + n, Klang 73 (0x49)
///   Betrag ≤ 0: "Verlust: "   (0x4FB72C) + (−n), Klang 74 (0x4A)
///   0x4C3B9A  Klang (nur wenn der Busabsender der eigene Spieler ist)
///   0x4C3BE5  0x4469A0(0, 0, Text, "", Standzeit 10, mittig 1)   ; Meldung Art 13
/// </code>
/// <para>Mittig heisst: jede neue Meldung schliesst die vorige (Schleife
/// <c>0x4469B1…0x446A12</c>), Standzeit 10 Zwanzig-Takt-Schritte (≈ 4 s). Bei uns
/// war es ein Hilfefenster links oben (0,0), ohne Standzeit, ohne Klang, nur bei
/// Betrag &gt; 0 — mehrere Zahlungen lagen deckungsgleich uebereinander.</para>
/// <para>⭐ Spielerentscheidung 29.10.2026: auch <b>0 $ ergibt »Verlust: 0«</b>,
/// wie das Original (<c>test dx,dx / jle</c>; M13 Regel 15 zahlt 0 $).</para>
/// <para>⚠ V: der Busabsender des Missionsskripts ist bei uns nicht nachgebildet —
/// der Klang spielt, wenn der EMPFAENGER der eigene Spieler ist. Ob zwei 528 im
/// selben Busdurchlauf im Original eine oder zwei Meldungen kurz zeigen, ist
/// ungelesen (die letzte bleibt stehen, beide Klaenge spielen). Gegenschalter
/// <c>--geldmeldung-alt</c> (Hilfefenster links oben, nur Einkommen, kein Klang).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--geldmeldung-alt</c> — bug-655: Stand 0.6.11.</summary>
    public static bool GeldmeldungAlt;

    /// <summary>Standzeit der Geldmeldung, <c>push 0xA</c> @0x4C3BD0.</summary>
    private const int GeldmeldungStandzeit = 10;

    /// <summary>Klang 73 »Einkommen«, 74 »Verlust« (<c>push 0x49</c> @0x4C3B33,
    /// <c>push 0x4A</c> @0x4C3B98).</summary>
    private const int KlangEinkommen = 73, KlangVerlust = 74;

    /// <summary>Pruefstand: gezeigte Geldmeldungen und gespielte Klaenge.</summary>
    public int GeldmeldungenGezeigt;
    public readonly List<int> GeldmeldungKlaenge = new();

    private void GeldmeldungZeigen(int betrag, int spieler)
    {
        if (spieler != ViewPlayer) return;                         // 0x4C3AAF
        bool plus = betrag > 0;                                    // 0x4C3AC2: jle -> Verlust
        string text = plus ? $"Einkommen: {betrag}" : $"Verlust: {-betrag}";
        int klang = plus ? KlangEinkommen : KlangVerlust;
        Audio.GameSounds.Play(klang);                              // 0x4C3B9A
        GeldmeldungKlaenge.Add(klang);
        GeldmeldungenGezeigt++;
        OnMeldung?.Invoke(text, "", GeldmeldungStandzeit, true);   // 0x4C3BE5, mittig
    }

    // ---- --geldmeldung-check ------------------------------------------------

    public static bool GeldmeldungCheckAn;

    /// <summary>
    /// Kampagne (M1 oder M7): dreimal +50 im Abstand von 60 Takten, dann dreimal +50
    /// im selben Takt, dann −20 und 0. Ueber das echte <c>_mscript.AddMoney</c>.
    /// Soll: 8 mittige Meldungen (6× »Einkommen: 50«, »Verlust: 20«, »Verlust: 0«),
    /// Klaenge 6×73 + 2×74, Standzeit 10, und das Hilfefenster wird nicht benutzt.
    /// Nullmodell <c>--geldmeldung-alt</c>: 0 Meldungen, 0 Klaenge → DURCHGEFALLEN.
    /// ⚠ Kopflos gibt es kein Fenster: die Meldungen werden am Haken
    /// <see cref="OnMeldung"/> mitgeschrieben, das Verdraengen der vorigen leistet
    /// <c>MapViewer.MeldungOeffnen</c> (mittig) und ist hier NICHT gemessen.
    /// </summary>
    public string GeldmeldungCheck()
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new System.Text.StringBuilder($"geldmeldung-check K{m}"
            + (GeldmeldungAlt ? " — NULLMODELL --geldmeldung-alt: die Meldungen MUESSEN fehlen" : "") + "\n");
        MissionScriptTick(0.001f);
        if (_mscript?.AddMoney is not { } geld) return sb.Append("  KEIN URTEIL: kein Missionsskript").ToString();
        var meldungen = new List<(string T, int St, bool Mi)>();
        var alt = OnMeldung;
        OnMeldung = (z1, z2, st, mi) => meldungen.Add((z1, st, mi));
        int view = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        int g0 = Money(view);
        int k0 = GeldmeldungKlaenge.Count;
        for (int k = 0; k < 3; k++) { geld(50, view); for (int t = 0; t < 60; t++) SimTickFuerProbe(); }
        for (int k = 0; k < 3; k++) geld(50, view);
        geld(-20, view);
        geld(0, view);
        OnMeldung = alt;
        int g1 = Money(view);
        int ein = meldungen.FindAll(x => x.T == "Einkommen: 50").Count;
        bool verl20 = meldungen.Exists(x => x.T == "Verlust: 20");
        bool verl0 = meldungen.Exists(x => x.T == "Verlust: 0");
        bool mittig = meldungen.TrueForAll(x => x.Mi && x.St == GeldmeldungStandzeit);
        var kl = GeldmeldungKlaenge.GetRange(k0, GeldmeldungKlaenge.Count - k0);
        int k73 = kl.FindAll(x => x == 73).Count, k74 = kl.FindAll(x => x == 74).Count;
        sb.AppendLine($"  Geld Spieler {view}: {g0} -> {g1} (Soll +{6 * 50 - 20})");
        sb.AppendLine($"  Meldungen {meldungen.Count} (Soll 8): »Einkommen: 50« {ein}× (Soll 6), »Verlust: 20« {verl20}, "
                    + $"»Verlust: 0« {verl0}, alle mittig mit Standzeit {GeldmeldungStandzeit}: {mittig}");
        sb.AppendLine($"  Klaenge 73: {k73} (Soll 6), 74: {k74} (Soll 2)");
        bool ok = meldungen.Count == 8 && ein == 6 && verl20 && verl0 && mittig && k73 == 6 && k74 == 2
                  && g1 == g0 + 280;
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
