namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// ⭐ 16.10.2026, bug-525 — <b>DIE MENÜZEILE »CREDITS« SPIELT DEN ABSPANN</b> wie im Original
/// (Entscheidung des Spielers: »alles wie im Original«). Lesung
/// <c>berichte/abschlussfilm-fable.md</c> §1.4 und <c>berichte/autosave-credits-demo-opus.md</c>.
///
/// <para>Hauptmenü Knopf 7 (Platz 155, <c>push 0x50250C</c> @0x480550) → <c>0x44CE88</c>:
/// <c>"X:\" + "movies\34.rpl"</c> (0x4FAC94 + 0x4FE8B0, Laufwerk über 0x4014E2 mit CD-Fahne 0),
/// Protokollzeile 0x4016A9, Abspieler <c>0x4016F4 → 0x4D49F0(Name, 0)</c> = <b>Fenster
/// 320×240</b>, danach Bildmodus zurück (0x4011A4) und Palette (<c>[0x540770]+0x7C</c>) —
/// <b>und sonst nichts</b>: weder die Demo-Fahne 0x4F6F9C noch die Menü-Fahne 0x4F6FC8
/// werden angefasst. Das Menü steht danach wie vorher; die Demo dahinter war für die Dauer
/// des Films eingefroren (der Abspieler hält die Meldeschleife an, <c>Main_funct</c> und damit
/// der Demozähler 0x540740 laufen nicht) und läuft dort weiter, wo sie war.</para>
///
/// <para>Bei uns: <see cref="MoviePlayer.Play"/> mit <c>fenster: true</c> wie nach Mission 33
/// (<see cref="Rendering.MapViewer"/>, Abschlussfilm.cs); die Kulisse steht derweil
/// (<c>ProcessMode.Disabled</c>). Fehlt 34.RPL (kein Import der Filme) → Rückfall auf die
/// Textliste <see cref="CreditsScreen"/>. Gegenschalter <c>--credits-text</c>: immer die
/// Textliste (Stand 0.6.9).</para>
/// <para>⚠ SETZUNG: nach dem Film legen wir die Menümusik wieder auf
/// (<see cref="Audio.MidiMusic.Resume"/>) — ob das Original sie nach 0x44CE88 fortsetzt, ist
/// nicht gelesen (es gibt vor dem Film die Objekte 0x540730/0x540770 frei, deren Art ungedeutet
/// ist). Ein stummes Menü nach dem Abspann sähe wie ein Fehler aus.</para>
/// </summary>
public partial class MainMenu
{
    /// <summary><c>--credits-text</c>: die Textliste statt des Abspanns (Stand 0.6.9).</summary>
    public static bool CreditsText;

    /// <summary>NUR ZUM MESSEN (<c>--credits-check</c>): so tun, als fehle 34.RPL.</summary>
    private static bool _creditsFilmFehltTest;

    private bool _creditsCheck;

    /// <summary>Der Abspann läuft gerade (zwischen Play und dem Rückruf).</summary>
    private bool _abspannLaeuft;

    /// <summary>Knopf 7 <c>0x44CE88</c>.</summary>
    private void Credits()
    {
        if (_abspannLaeuft) return;
        if (CreditsText || _creditsFilmFehltTest || MoviePlayer.Find(34) == null)
        {
            if (!CreditsText) GD.Print("Credits: 34.RPL fehlt — Rueckfall auf die Textliste");
            AddChild(new CreditsScreen());
            return;
        }
        // Die Demo friert ein wie im Original (der Abspieler hält die Schleife an).
        var kulisse = _backdrop;
        if (kulisse != null) kulisse.ProcessMode = ProcessModeEnum.Disabled;
        _abspannLaeuft = true;
        bool musik = Audio.MidiMusic.Track >= 0;
        GD.Print("Credits: Hauptmenue Knopf 7 (0x44CE88) — movies\\34.rpl im Fenster 320x240");
        void Zurueck()
        {
            _abspannLaeuft = false;
            if (kulisse != null && IsInstanceValid(kulisse)) kulisse.ProcessMode = ProcessModeEnum.Inherit;
            if (musik && !MoviePlayer.NurProtokoll) Audio.MidiMusic.Resume();   // SETZUNG, s. o.
            GD.Print("Credits: Abspann zu Ende — zurueck ins Menue, die Demo laeuft weiter");
        }
        // ⚠ --no-movie / movies.no schalten Filme ab: dann wie ein fehlender Film die Liste.
        if (!MoviePlayer.Play(this, 34, Zurueck, fenster: true))
        {
            Zurueck();
            AddChild(new CreditsScreen());
        }
    }

    /// <summary><c>--credits-check</c> (mit <c>--no-backdrop</c>): die drei Wege des Knopfes.</summary>
    private void CreditsCheckGo()
    {
        var sb = new System.Text.StringBuilder("credits-check:");
        bool ok = true;
        void Pruef(string was, bool b, string w = "")
        {
            ok &= b;
            sb.Append($"\n   {(b ? "  ok   " : "⚠ FEHLT")} {was}{(w.Length > 0 ? $"  [{w}]" : "")}");
        }
        int Listen()
        {
            int n = 0;
            foreach (var k in GetChildren())
                if (k is CreditsScreen && !((Node)k).IsQueuedForDeletion()) n++;
            return n;
        }
        bool alt = CreditsText;
        bool filmDa = MoviePlayer.Find(34) != null;
        Pruef("34.RPL gefunden (Import der Filme)", filmDa, MoviePlayer.Find(34) ?? MoviePlayer.LetzteSuche);

        MoviePlayer.NurProtokoll = true;
        MoviePlayer.Protokoll.Clear();
        Credits();
        string p1 = string.Join(", ", MoviePlayer.Protokoll);
        int l1 = Listen();
        Pruef("Knopf 7 spielt 34.RPL (Fenster) und zeigt KEINE Textliste", p1 == "34" && l1 == 0,
              $"Filme [{p1}], Listen {l1}");
        Pruef("danach zurueck im Menue (kein Film offen)", !_abspannLaeuft);
        foreach (var k in GetChildren()) if (k is CreditsScreen c) c.QueueFree();

        _creditsFilmFehltTest = true;
        MoviePlayer.Protokoll.Clear();
        Credits();
        Pruef("fehlt 34.RPL: Rueckfall auf die Textliste", MoviePlayer.Protokoll.Count == 0 && Listen() == 1,
              $"Filme [{string.Join(", ", MoviePlayer.Protokoll)}], Listen {Listen()}");
        _creditsFilmFehltTest = false;
        foreach (var k in GetChildren()) if (k is CreditsScreen c) c.QueueFree();
        MoviePlayer.NurProtokoll = false;

        GD.Print(sb.ToString());
        GD.Print($"credits-check: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}"
                 + (alt ? "  (Nullmodell --credits-text)" : ""));
        GetTree().Quit(ok ? 0 : 1);
    }
}
