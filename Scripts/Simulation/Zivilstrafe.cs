namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐ 29.10.2026, bug-654 (KayelGee R8 K12): »Wenn man in Mission 7 im Norden das
/// zivile Gebaeude zerstoert, dann sollte eine Nachricht kommen, dass man dazu nicht
/// befugt ist, und 10 $ Strafe abgezogen werden.«
///
/// <para>Kein Skriptbefehl, sondern die <b>Gebaeudetodroutine</b> selbst
/// (<c>berichte/missionsmeldungen-r8-opus.md</c> §2), C <c>0x4C9964…0x4C99BB</c>,
/// F <c>0x4C9514…0x4C956B</c>, beide gleich:</para>
/// <code>
///   dword[0x539234] == 0          ; kein Netz-/Gleichschrittspiel
///   byte [Satz+0x18] == 0         ; »built« — 0 bei allen Arten ab 17
///   word [Satz+0x32] != 0         ; das Terraniumwort, bei zivilen Bauten das Strafgeld
///   byte [0x4F6F9C] == 0          ; keine Demo
///   -> show_text(350, 220, 103, 0)               @0x4C999D (F 0x4C954D)
///   -> bus_cmd(528, −word[+0x32], Spieler 0)     @0x4C99B6 (F 0x4C9566)
/// </code>
/// <para>Keine Taeter- und keine Besitzerpruefung: wer das Gebaeude zerstoert, ist
/// gleich, das Geld geht immer an Spieler 0. 52 solche Saetze auf 9 Karten
/// (02, 07, 09, 10, 11, 12, 13, 14, NET05); M7: Platz 12, Art 39, (42,20), 10 $.</para>
///
/// <para>⚠ Spielerentscheidung 29.10.2026: <b>NUR in der Kampagne</b> (nicht im
/// Gefecht, nicht im Netz). Das Original schaltet nur das Netz ab
/// (<c>dword[0x539234]</c>); das Einzelspiel-Gefecht auf NET05 waere dort betroffen
/// — hier bewusst nicht (UNSERE SETZUNG nach seiner Entscheidung). »Spieler 0«
/// ist im Einzelspiel der Mensch. Der Geldweg geht ueber denselben Befehl 528 wie
/// das Missionsskript (<c>_mscript.AddMoney</c>), damit die Meldung »Verlust: 10«
/// mit Klang 74 mitkommt (bug-655). Der Text 103 geht ueber
/// <see cref="UI.HelpWindow.ShowOhneRiegel"/> (show_text: kein Riegel). Die
/// Demo-Fahne <c>byte[0x4F6F9C]</c> hat bei uns keine Entsprechung in der Kampagne.
/// Gegenschalter <c>--zivilstrafe-aus</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--zivilstrafe-aus</c> — bug-654: keine Strafe (Stand 0.6.11).</summary>
    public static bool ZivilstrafeAus;

    /// <summary>Pruefstand: wie oft gestraft, und mit wieviel.</summary>
    public int ZivilstrafenGezahlt, ZivilstrafeSumme;
    public readonly List<int> ZivilstrafeTexte = new();

    private const int ZivilstrafeText = 103;

    private void ZivilstrafeBeimTod(Entity b)
    {
        if (ZivilstrafeAus || UI.SkirmishSetup.CampaignMission <= 0) return;
        if (ImNetz) return;                                   // dword[0x539234]
        if (b.Built != 0 || b.StockT == 0) return;
        int wert = b.StockT;
        ZivilstrafenGezahlt++;
        ZivilstrafeSumme += wert;
        ZivilstrafeTexte.Add(ZivilstrafeText);
        GD.Print($"Zivilstrafe: Gebaeude Platz {b.Slot} (Art {b.BType}, Spieler {b.Owner}) auf ({b.Col},{b.Row}) "
               + $"zerstoert -> Text {ZivilstrafeText}, −{wert} $ fuer Spieler 0 (0x4C9964)");
        var root = GetTree()?.Root;
        if (root != null) UI.HelpWindow.ShowOhneRiegel(root, ZivilstrafeText, 350, 220);   // 0x4C999D
        // 0x4C99B6: bus_cmd(528, −wert, 0) — derselbe Weg wie das Missionsskript
        if (_mscript?.AddMoney is { } geld) geld(-wert, 0);
        else
        {
            Money(0, Mathf.Max(0, Money(0) - wert));
            if (!GeldmeldungAlt && OnMeldung != null && !MeldungsfensterAus) GeldmeldungZeigen(-wert, 0);
        }
    }

    // ---- --zivilstrafe-check ------------------------------------------------

    public static bool ZivilstrafeCheckAn;

    /// <summary>
    /// M7 (oder jede Karte mit einem Kandidaten): Geld 1000, Kandidat toeten →
    /// genau ein Text 103, Geld 1000 − Wert, eine Meldung »Verlust: Wert«.
    /// Gegenproben des Tors: ein Gebaeude mit built=1 und eines mit built=0 ∧ +0x32=0
    /// toeten → keine Strafe. Nullmodell <c>--zivilstrafe-aus</c>: Geld 1000.
    /// Dazu die Zahl der geladenen Kandidaten (Bericht: M7 = 1).
    /// </summary>
    public string ZivilstrafeCheck()
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new System.Text.StringBuilder($"zivilstrafe-check K{m}"
            + (ZivilstrafeAus ? " — NULLMODELL --zivilstrafe-aus: die Strafe MUSS fehlen" : "") + "\n");
        MissionScriptTick(0.001f);
        var kand = new List<int>();
        int built1 = -1, leer = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.IsProp || e.Dead) continue;
            if (e.Built == 0 && e.StockT != 0) kand.Add(i);
            else if (e.Built != 0 && built1 < 0 && e.BType != 1) built1 = i;
            else if (e.Built == 0 && e.StockT == 0 && leer < 0) leer = i;
        }
        sb.AppendLine($"  Kandidaten built==0 ∧ +0x32!=0 auf dieser Karte: {kand.Count}"
            + (m == 7 ? " (Soll 1, Bericht: Platz 12, Art 39, 10 $)" : ""));
        if (kand.Count == 0) return sb.Append("  KEIN URTEIL: kein Kandidat").ToString();

        // Meldungen mitschreiben (kopflos gibt es kein Fenster)
        var meldungen = new List<string>();
        var alt = OnMeldung;
        OnMeldung = (z1, z2, st, mi) => { meldungen.Add($"{z1}|{st}|{(mi ? "mittig" : "Maus")}"); };
        int fehler = 0;
        void Fall(string titel, int idx, bool strafeSoll)
        {
            var b = _entities[idx];
            Money(0, 1000);
            int n0 = ZivilstrafenGezahlt, mz = meldungen.Count;
            int wert = b.StockT, art = b.BType, built = b.Built;
            Kill(idx, b, 0, "zivilstrafe-check");
            int geld = Money(0);
            bool gestraft = ZivilstrafenGezahlt > n0;
            bool soll = strafeSoll;      // das Nullmodell behaelt die Erwartung — und faellt
            bool ok = soll
                ? gestraft && geld == 1000 - wert && meldungen.Count == mz + 1 && meldungen[^1].StartsWith($"Verlust: {wert}")
                : !gestraft && geld == 1000;
            if (!ok) fehler++;
            sb.AppendLine($"  {(ok ? "ok    " : "FEHLER")} {titel}: Platz {b.Slot} Art {art} built {built} +0x32 {wert} -> "
                + $"Text 103 {(gestraft ? "ja" : "nein")}, Geld 1000 -> {geld}, Meldung "
                + (meldungen.Count > mz ? $"»{meldungen[^1]}«" : "keine")
                + $" (Soll {(soll ? $"Strafe −{wert}" : "keine Strafe")})");
        }
        Fall("Kandidat", kand[0], true);
        if (built1 >= 0) Fall("Gegenprobe built=1", built1, false);
        else sb.AppendLine("  (kein Gebaeude mit built=1 ausser der Basis — Gegenprobe entfaellt)");
        if (leer >= 0) Fall("Gegenprobe built=0, +0x32=0", leer, false);
        else sb.AppendLine("  (kein Satz built=0 ∧ +0x32=0 — Gegenprobe entfaellt)");
        OnMeldung = alt;
        sb.Append(fehler == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
