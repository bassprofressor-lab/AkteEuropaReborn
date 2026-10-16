namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 16.10.2026 — <b>DER ABSCHLUSSFILM</b> (Meldung des Spielers: »nach Ende K33
/// müsste ein Abschlussvideo kommen«). Lesung: <c>berichte/abschlussfilm-fable.md</c>.
///
/// <para>Im Original laufen nach dem Sieg in Mission 33 ZWEI Filme. Der
/// Missionswechsel <c>0x4CFD80</c> (nach <i>Weiter</i> im Abschlussfenster,
/// Zustand 100 → 200 »Succ200« <c>0x416B37</c>) spielt wie nach jeder Mission
/// den Film mit der Nummer der gespielten Mission — <c>movies\33.rpl</c>
/// (innen <c>ExtEsc.rpl</c>, 59,6 s) —, und hinter
/// <c>cmp word[0x539934],0x21</c> @<c>0x4D0012</c> (F <c>0x4CFBC2</c>) fest
/// verdrahtet <c>movies\34.rpl</c> (<c>0x4FE8B0</c>/F <c>0x4FD8E8</c>) = den
/// ABSPANN (innen <c>E:\Credits\AbspEsc.rpl</c>, 203 s), mit Fahne 0 im
/// 320×240-Fenster. Danach das Hauptmenü. Bei einer Niederlage kein Film
/// (Zustand 5000/5001 → <c>0x4D0290</c>).</para>
///
/// <para>Bei uns lief bisher nur der Film VOR jeder Mission (MainMenu.StartMission,
/// Film M−1) — 33.RPL und 34.RPL rief niemand, denn nach Mission 33 gibt es
/// keine Mission 34. Gegenschalter <c>--abschlussfilm-alt</c>, Prüfstand
/// <c>--abschlussfilm-check</c> (auf <c>--campaign=33</c>).</para>
///
/// <para>⚠ SETZUNG: das Original setzt danach die Demo-Fahne <c>0x4F6F9C := 1</c>
/// (Hauptmenü mit laufender Demo); wir gehen ins gewöhnliche Menü.</para>
/// </summary>
public partial class MapViewer
{
    /// <summary><c>--abschlussfilm-alt</c>: nach Mission 33 sofort ins Menü.</summary>
    public static bool AbschlussfilmAlt;
    private bool _abschlussfilmCheck;
    /// <summary><c>--abschlussfilm-zeigen</c>: derselbe nachgestellte Sieg, aber
    /// die Filme laufen wirklich und danach geht es ins Menue — zum Ansehen
    /// im Fenster.</summary>
    private bool _abschlussfilmZeigen;
    /// <summary>Wurde gerade Mission 33 gewonnen? Gesetzt in ShowEnd —
    /// <c>word[0x539934] == 0x21</c>.</summary>
    private bool _letzteMissionGewonnen;

    /// <summary>Statt <see cref="ToMenu"/> am Kampagnenende.</summary>
    private void KampagnenEnde()
    {
        if (!_letzteMissionGewonnen || AbschlussfilmAlt) { NachAbschlussfilm(); return; }
        // Die Abrechnung liegt auf Ebene 95, der Film auf 90 — weg damit.
        if (_endBanner != null) _endBanner.Visible = false;
        if (!UI.MoviePlayer.Play(this, 33, Abspann)) Abspann();
    }

    private void Abspann()
    {
        if (!UI.MoviePlayer.Play(this, 34, NachAbschlussfilm, fenster: true)) NachAbschlussfilm();
    }

    private void NachAbschlussfilm()
    {
        if (_abschlussfilmCheck && !_abschlussfilmZeigen)
        {
            var p = UI.MoviePlayer.Protokoll;
            // Mission 33 -> 33.RPL, 34.RPL; jede andere Mission -> keiner
            // (ihr Film laeuft VOR der naechsten Mission, MainMenu.StartMission).
            string soll = UI.SkirmishSetup.CampaignMission == 33 ? "33, 34" : "";
            bool ok = string.Join(", ", p) == soll;
            GD.Print($"abschlussfilm-check: Filme [{string.Join(", ", p)}], erwartet [{soll}] -> " +
                     (ok ? "BESTANDEN" : "DURCHGEFALLEN") +
                     (AbschlussfilmAlt ? "  (Nullmodell --abschlussfilm-alt)" : ""));
            GetTree().Quit(0);
            return;
        }
        // ⚠ Beim Ansehen beenden statt ins Menue: das Menue liest --campaign=33
        // erneut und startete die Mission wieder — 54 Runden bis zum CLR-Absturz.
        if (_abschlussfilmZeigen) { GetTree().Quit(0); return; }
        ToMenu();
    }

    /// <summary>Spielt den Sieg in Mission 33 nach, ohne ihn zu verbuchen
    /// (<c>record: false</c> — campaign.cfg bleibt unberührt), und drückt
    /// <i>Weiter</i>.</summary>
    private void AbschlussfilmLauf()
    {
        UI.MoviePlayer.NurProtokoll = !_abschlussfilmZeigen;
        // ⚠ --no-briefing schaltet im Menue auch die Filme ab (MainMenu:1932).
        if (_abschlussfilmZeigen) UI.MoviePlayer.Disabled = false;
        UI.MoviePlayer.Protokoll.Clear();
        GD.Print($"abschlussfilm-check: Mission {UI.SkirmishSetup.CampaignMission}, Sieg nachgestellt");
        ShowEnd(true, "abschlussfilm-check", record: false);
        // Keine Folgemission starten — gemessen wird nur, was am Ende laeuft.
        _nextMission = -1;
        ContinueAfterMission();
    }
}
