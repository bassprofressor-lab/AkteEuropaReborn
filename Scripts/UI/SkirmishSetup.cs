namespace AkteEuropaReborn.UI;

using AkteEuropaReborn.Rendering;

/// <summary>
/// What the main menu hands to the game scene. Static because the two scenes
/// never exist at the same time — the menu frees itself before the map loads.
/// </summary>
public static class SkirmishSetup
{
    /// <summary>True while a game started from the menu is running; the game
    /// scene then goes back to the menu on Esc instead of quitting.</summary>
    public static bool Active;

    /// <summary>Die Mission, die das Hauptmenü <b>sofort nach dem Laden</b>
    /// starten soll — mit Vorschau, wie jeder andere Weg auch. 0 = keine.
    ///
    /// <para>⚠ Warum es diesen Umweg gibt: »Weiter« im Abschlussfenster lud
    /// die nächste Karte bisher DIREKT (<c>ReloadCurrentScene</c>) und sprang
    /// damit an der Vorschau vorbei. Gemeldet: »wenn man kampagne 1
    /// durchspielt und auf weiter drückt, kommt man wieder ins hauptmenu
    /// anstatt zu kampagne 2 preview«. Die Vorschau hängt aber am Menü
    /// (<c>MainMenu.StartMission</c> ruft <c>BriefingScreen.For</c>) — das ist
    /// die EINE Stelle, an der sich Menüknopf, Missionswähler und
    /// <c>--campaign</c> treffen. Also geht »Weiter« jetzt durch dieselbe Tür,
    /// statt eine zweite danebenzubauen.</para>
    ///
    /// <para>Nebenwirkung, und sie ist erwünscht: der Weg über das Menü läuft
    /// durch <c>Core.LeaveToMenu.Tidy()</c>, das die offenen Missionsfenster
    /// abräumt. Damit kann keine Meldung aus der alten Mission in die neue
    /// hinüberlecken.</para></summary>
    public static int PendingMission;

    public static string Map = "map_NET07";
    public static int Human;                       // which player the human is
    public static int AiCount = 3;                 // how many opponents
    public static MapEntityLayer.AiLevel Level = MapEntityLayer.AiLevel.Normal;

    /// <summary>The campaign mission being played, or 0 for a skirmish. Set so
    /// the game scene knows to record the mission as finished when it is won —
    /// a skirmish has nothing to record.</summary>
    public static int CampaignMission;

    /// <summary>"Rohstoffe": 0 keine · 1 wenige · 2 normal · 3 viele — the
    /// original's own option and its own order (see
    /// <see cref="Import.ExeTables.ResourceLevels"/>). It fills the buildings'
    /// stores at the start of a SKIRMISH; the routine behind it has one caller
    /// and that caller is the game-start message, so a campaign mission keeps
    /// what its level file gives it. Default 2, which is what the numbers call
    /// normal.</summary>
    public static int Resources = 2;

    /// <summary>⭐ 13.11.2026 — Spielerentscheidung (»ja bau das ins gefecht«): die fuenfte
    /// Rohstoffstufe »wie auf der Karte«. UNSERE ABWEICHUNG vom Original (Gefecht = Wettkampf-
    /// modus, darf abweichen): die Fuellroutine 0x41A002 laeuft dann NICHT, jedes Gebaeude
    /// behaelt das Lager aus der Kartendatei — so wirken die Lagerwerte des Editors. Index
    /// hinter den vier Stufen des Originals, damit deren Nummern (0..3, auch im Netz und auf
    /// der Befehlszeile) bleiben, wie sie sind.</summary>
    public const int ResourcesKarte = 4;
    public const string ResourcesKarteName = "wie auf der Karte";

    /// <summary>
    /// <b>»Techstandard« — die Technikstufe, mit der ein Gefecht anfängt.</b>
    ///
    /// <para>⚠ <b>NICHTS DAVON IST UNSERE SETZUNG</b>, gelesen am 13.08.2026 in
    /// beiden GAME.EXE: Bereich <b>1..8</b> ist der Bereich des Knopfs
    /// @0x44D3BC, <b>1</b> ist der Wert eines frischen Spiels (@0x4426F4), und
    /// die Beschriftung ist die des Originals — <c>Techstandard </c> (0x502630),
    /// aus derselben Reihe wie <c>Rohstoffe: </c> und <c>Konto </c>, die wir
    /// schon haben. Der Gastgeber stellt sie im Aufbaubild ein; Kommando 979
    /// trägt sie ein, Kommando 981 startet.</para>
    ///
    /// <para><b>Was daran hängt.</b> Die Torroutine @0x419F30 setzt aus diesem
    /// Wert die Freigaben der Flugzeugtabelle (0x51B020, 8 Blöcke × 20 Sätze ×
    /// 48 B) und lässt @0x4B2380 den Block von Spieler 0 in die anderen sieben
    /// kopieren — das Gegenstück zu @0x4B2330 bei den Schiffen. Gemessen ergibt
    /// das: Stufe <b>1–3</b> Treibstoff- und Munitionheli · <b>4</b> dazu den
    /// Kampfhubschrauber · <b>5</b> den Spionageflieger · <b>6–8</b> Jagdflieger
    /// und Bomber. <b>Die Liste ist auf keiner Stufe leer</b>, und genau darum
    /// braucht das Gefecht für die Luft keine Option von uns.</para>
    ///
    /// <para>⚠ Die Kampagne geht einen ANDEREN Weg (@0x4D03E0 →
    /// Rücksetzroutine @0x4B23C0, die alle Freigaben nullt) und schaltet einzeln
    /// per Skript frei. Zwei getrennte Quellen, keine Überschneidung — deshalb
    /// gilt dieser Wert nur, wo <see cref="CampaignMission"/> 0 ist.</para>
    ///
    /// <para>Derselbe Wert gehört unter den <b>Schiffs</b>-Rückfall, wo heute
    /// die Konstante <c>CampaignTechLevel = 1</c> steht: dann kommen beide
    /// Listen aus einer Quelle.</para>
    ///
    /// <para>⚠⚠ <b>DIE VORGABE IST SEIT DEM 17.08.2026 ACHT, UND DAS IST UNSERE
    /// SETZUNG — genauer: die Wettkampfentscheidung des Spielers.</b> Hier stand
    /// <c>1</c>, mit der Begründung »so startet ein frisches Original«
    /// (@0x4426F4). Gelesen ist daran nur, was das Original tut; <b>dass 1 auch
    /// für unser Gefecht richtig ist, war nie belegt</b> — und der Preis war
    /// sichtbar, gemeldet als Fehler C3: »Kann im Flughafen nur
    /// Versorgungshelikopter bauen, aber keine anderen Flugzeuge? (Liegt das am
    /// Techstandard 1?)«. Ja, lag es. Auf Stufe 1 gibt das Tor @0x419F30 genau
    /// die zwei Nachschubhelis frei, und ein Wettkampfmodus, der seine Luftwaffe
    /// hinter einer Startbedingung versteckt, die niemand gewählt hat, ist eine
    /// Falle und kein Spiel.</para>
    ///
    /// <para><b>Die gelesene Zahl bleibt gelesen:</b> 1 ist und bleibt der Wert
    /// eines frischen Originals, und wer ihn will, stellt ihn im Gefechtsschirm
    /// ein — der Bereich 1..8 ist unverändert der des Originals. Geändert ist
    /// allein, wo der Regler steht, wenn niemand ihn anfasst. Das ist genau die
    /// Trennung, die [[akte-europa-gefecht-competitive]] verlangt: gelesene
    /// Zahlen dokumentieren, Wettkampfentscheidungen benennen.</para></summary>
    public static int Techstandard = 8;

    /// <summary>
    /// <b>»Konto« — das Startgeld eines Gefechts.</b>
    ///
    /// <para>⚠ <b>NICHTS DAVON IST UNSERE SETZUNG</b>, gelesen 16.08.2026: der
    /// Gefechtsaufbau @0x41ABD6 schreibt <c>word[0x5407A0]</c> in einer Schleife
    /// an <b>alle acht</b> Spieler, der Menüknopf @0x44D425 zählt in
    /// <b>Tausenderschritten bis 10000 und springt dann auf 0</b>
    /// (<c>cmp 0x2710 / jae → 0, sonst += 0x3E8</c>), und die Beschriftung ist
    /// die des Originals — <c>Konto </c> (0x502628), gedruckt @0x480A8D. Ein
    /// frisches Spiel steht auf <b>0</b> (@0x4426DF, dieselbe Rücksetzroutine
    /// wie Techstandard und Rohstoffe).</para>
    ///
    /// <para>⚠⚠ <b>WARUM DAS FEHLTE UND WAS ES BEDEUTET:</b> im Gefecht wurde
    /// <c>_money</c> bisher <b>nur</b> aus sec73 der Karte gefüllt — und
    /// <b>keine</b> Gefechtskarte hat eines. Jeder Spieler stand also auf $0.
    /// Zusammen damit, dass der MARKT nicht gebaut ist, war Geld im Gefecht
    /// eine <b>tote Zahl</b>: es gab keine Quelle und keine Ausgabe, die es
    /// gebraucht hätte. Gelesen ist nämlich auch das Gegenstück — <b>eine
    /// laufende Einnahme gibt es im Original NICHT</b> (keine der sechs
    /// schreibenden Stellen an <c>0xA9C600</c> liegt in einer Taktschleife;
    /// Rohstoffe und Terranium füllen Bauteillager, kein Konto). Geld kommt aus
    /// dem Startkonto, aus VERKÄUFEN am Markt (@0x4C0628) und am Missionsende
    /// (»Berkewitz Corp. bezahlt Ihnen $«, @0x416B0A).</para>
    ///
    /// <para>⚠⚠ 18.08.2026 — <b>UNSERE VORGABE IST JETZT 5000, UND DAS IST EINE
    /// BEWUSSTE ABWEICHUNG.</b> Hier stand 0, »wie im Original«, mit dem
    /// Hinweis, wer handeln wolle, stelle es ein. Der Spieler hat es zweimal
    /// gemeldet — »Gefecht: sollten wir nicht echtes Startgeld haben? Aktuell
    /// ist Kontostand $ 0« und danach »im Gefecht nach wie vor Kontostand
    /// 0 $«.</para>
    ///
    /// <para>Er hat recht, und zwar aus einem Grund, der in dieser Fassung
    /// stärker wiegt als die Treue: <b>bei uns gibt es den Markt</b>, und ein
    /// Gefecht, das mit leerem Konto beginnt, hat ihn faktisch nicht — die
    /// erste Mark müsste erst aus einem Verkauf kommen. Nach der Trennachse
    /// des Projekts darf das Gefecht bewusst abweichen; die Kampagne bleibt
    /// unberührt, sie holt ihren Stand ohnehin aus dem Spielstand.</para>
    ///
    /// <para>⚠ 5000 ist <b>gewählt, nicht gemessen</b>: es ist die Mitte des
    /// Originalknopfs (0…10000 in Tausenderschritten, @0x44D425). Wer es anders
    /// will, stellt es im Menü unter »Konto« ein — der Regler bleibt, was er
    /// war, und 0 bleibt einstellbar.</para></summary>
    public static int StartMoney = 5000;

    /// <summary>Der Schritt des Originalknopfs: 1000 aufwärts, bei über 10000
    /// zurück auf 0 (@0x44D425).</summary>
    public static int NextStartMoney(int now) => now >= 10000 ? 0 : now + 1000;

    /// <summary>
    /// ⭐ <b>Der Namensfilter des Originals</b> (22.08.2026, OFFENE_FRAGEN
    /// <b>BN.12</b>): erlaubt sind nur die Zeichen <c>0x20 … 0x7A</c>, und
    /// davon NICHT <c>[</c>, <c>]</c> und <c>^</c>.
    ///
    /// <para>⚠ Der Grund ist die Spielschrift, nicht die Höflichkeit: die drei
    /// Ausnahmen und alles ab <c>0x7B</c> haben in <c>FONT.CWD</c> keine
    /// Kachel. Ein Name mit einem davon würde als Lücke oder als fremdes
    /// Zeichen erscheinen.</para>
    ///
    /// <para>⚠ Umlaute fallen damit ebenfalls weg — das ist keine Härte von
    /// uns, sondern die Schrift von 1997. Wer das ändert, weicht ab.</para>
    /// </summary>
    public static string FilterName(string roh)
    {
        if (string.IsNullOrEmpty(roh)) return "";
        var sb = new System.Text.StringBuilder(roh.Length);
        foreach (char c in roh)
        {
            if (c < 0x20 || c > 0x7A) continue;
            if (c is '[' or ']' or '^') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// <b>»Alle Einheiten« — und damit auch LUFTEINHEITEN.</b>
    ///
    /// <para>⚠ <b>UNSERE OPTION</b>, und sie steht hier, weil das Original für
    /// ein Gefecht keine hat, die wir gelesen hätten. Der Anlass ist eine
    /// Lücke in den Daten: die Gefechtskarten tragen in <b>sec120 NULL
    /// Flugzeugvorlagen</b> (nachgezählt auf NET02, NET05 und NET07) — der
    /// Flughafen hat dort schlicht nichts anzubieten, egal wieviel Geld man
    /// hat. Eine Kampagnenmission bekommt ihre Vorlagen aus dem Fahrplan
    /// (<c>CampaignManager.UnlocksFor</c>); ein Gefecht bekam gar nichts.</para>
    ///
    /// <para>Mit dieser Option werden die acht Vorlagen aus
    /// <c>aircraft.json</c> für <b>alle acht Spieler</b> angelegt und
    /// freigegeben — die KI eingeschlossen, sonst wäre es kein Gefecht, sondern
    /// ein Vorteil. Zugleich entfällt am Boden die Freigabe-Prüfung, so dass
    /// die ganze Entwurfsliste baubar ist.</para>
    ///
    /// <para>Aus bleibt sie in der Vorgabe: dann verhält sich ein Gefecht wie
    /// bisher, und niemand bekommt ungefragt eine Luftwaffe.</para></summary>
    public static bool AllUnits;

    /// <summary>Eine Karte, die nur ANGESEHEN und nicht gespielt werden soll —
    /// der Rueckweg des Karteneditors in den Kartenbetrachter.
    ///
    /// <para>⚠ Warum das nicht ueber <see cref="Map"/> und <see cref="Active"/>
    /// geht, und das ist gemessen: eine erzeugte Karte traegt <b>0 Einheiten und
    /// 0 Gebaeude</b> (MapGenerator setzt Gelaende und zwei Startmarken, sonst
    /// nichts). <c>SkirmishAi.StartSkirmish</c> findet dann keinen besetzten
    /// Platz und gibt -1 zurueck, und <c>MapEntityLayer.Verdict</c> meldet wegen
    /// <c>AssetsOf(me) == 0</c> sofort »MISSION GESCHEITERT« — nachgelaufen am
    /// 12.08.2026 mit <c>--skirmish=map_pruef01</c>: die Zeile »Gemetzel
    /// entschieden: MISSION GESCHEITERT« steht schon im ersten Takt da. Ein
    /// »Spielen«-Knopf haette dem Spieler also die Abrechnung ins Bild gelegt,
    /// bevor er die Karte gesehen hat. <c>CheckEnd</c> haengt an
    /// <see cref="Active"/>, darum bleibt der hier aus.</para>
    ///
    /// <para>Wird von <c>MapViewer._Ready</c> gelesen und dort sofort geleert,
    /// damit ein spaeterer Neustart der Szene nicht dieselbe Karte wieder
    /// aufzieht.</para></summary>
    public static string ViewMap = "";

    public const string MenuScene = "res://Scenes/Main/MainMenu.tscn";

    /// <summary>A save the main menu picked, applied by the game screen once
    /// the map is up. Empty means "start fresh". Cleared by whoever uses it, so
    /// a later restart does not silently reload the same game.</summary>
    public static string PendingSave = "";
    public const string GameScene = "res://Scenes/Gameplay/MapViewer.tscn";

    // ---- ⭐ 14.11.2026 — P7 (Spielerwunsch): Stärke und Team JE Startplatz ----------------
    // »ich kann also nicht beliebig sagen, ich nehme ein computergegner leicht, einen normal und
    // einen schweren … und team bildung wäre gut«. UNSERE ABWEICHUNG (Gefecht = Wettkampfmodus):
    // statt »Anzahl Gegner + eine Schwierigkeit« eine Zeile je Startplatz der Karte, wie in
    // klassischen Echtzeitstrategiespielen. Gilt NUR, wenn PlatzListe gesetzt ist (Gefechtsschirm,
    // --gefecht-plaetze=, Spielstand); Befehlszeile, Editor, Kampagne und Netz bleiben beim Alten.
    // Gegenschalter --gefecht-plaetze-alt.

    public const int ArtLeer = 0, ArtMensch = 1, ArtLeicht = 2, ArtNormal = 3, ArtSchwer = 4;
    /// <summary>Nur im Mehrspielerschirm (LAN-Gastgeber): hier startet ein Mitspieler. In der
    /// Sitzung wird daraus <see cref="ArtMensch"/> (besetzt) oder <see cref="ArtLeer"/>.</summary>
    public const int ArtMitspieler = 5;

    /// <summary>true: <see cref="PlatzArt"/>/<see cref="PlatzTeam"/> bestimmen Gegner, Stufen
    /// und Bündnisse; <see cref="AiCount"/>/<see cref="Level"/> sind dann nur noch Anzeige.</summary>
    public static bool PlatzListe;
    /// <summary>Je Kartenplatz 0..7: <see cref="ArtLeer"/> … <see cref="ArtSchwer"/>.</summary>
    public static readonly int[] PlatzArt = new int[8];
    /// <summary>Je Kartenplatz 0..7: 0 = kein Team (jeder gegen jeden), 1..4 = Team.</summary>
    public static readonly int[] PlatzTeam = new int[8];

    public static bool IstKi(int art) => art is ArtLeicht or ArtNormal or ArtSchwer;

    public static MapEntityLayer.AiLevel StufeVon(int art) => art switch
    {
        ArtLeicht => MapEntityLayer.AiLevel.Easy,
        ArtSchwer => MapEntityLayer.AiLevel.Hard,
        _ => MapEntityLayer.AiLevel.Normal,
    };

    public static int ArtVon(MapEntityLayer.AiLevel l) => l switch
    {
        MapEntityLayer.AiLevel.Easy => ArtLeicht,
        MapEntityLayer.AiLevel.Hard => ArtSchwer,
        _ => ArtNormal,
    };

    /// <summary>⭐ 14.11.2026 — Fable D10: das Spieltempo beim Start (1..3 wie Befehl 989; 1 =
    /// das Tempo des Originals, das im Netz fest auf 1 steht). Im Spiel aendern es +/- weiter fuer
    /// alle. UNSERE ABWEICHUNG nur fuer das Gefecht; Gegenschalter --tempo-alt (immer 1).</summary>
    public static int Tempo = 1;
    public static bool TempoAlt => System.Array.IndexOf(Core.CommandLine.Args, "--tempo-alt") >= 0;
    public static string TempoName(int v) => v switch { 2 => "schnell (×2)", 3 => "sehr schnell (×3)", _ => "normal" };

    public static bool PlaetzeAlt => System.Array.IndexOf(Core.CommandLine.Args, "--gefecht-plaetze-alt") >= 0;

    /// <summary>Die Aufstellung als Text »art:team« je Platz 0..7, z. B. »1:1,4:1,2:2,3:2,0:0,…«
    /// — für Spielstand und Einstellungen.</summary>
    public static string PlaetzeText()
    {
        var sb = new System.Text.StringBuilder();
        for (int p = 0; p < 8; p++) sb.Append(p > 0 ? "," : "").Append(PlatzArt[p]).Append(':').Append(PlatzTeam[p]);
        return sb.ToString();
    }

    /// <summary>Gegenstück zu <see cref="PlaetzeText"/>; false bei einem unlesbaren Text
    /// (dann bleibt alles, wie es war).</summary>
    public static bool PlaetzeAusText(string? text, int[] art, int[] team)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var teile = text.Split(',');
        if (teile.Length != 8) return false;
        var a = new int[8]; var t = new int[8];
        for (int p = 0; p < 8; p++)
        {
            var at = teile[p].Split(':');
            if (at.Length != 2 || !int.TryParse(at[0], out a[p]) || !int.TryParse(at[1], out t[p])) return false;
            if (a[p] is < ArtLeer or > ArtMitspieler || t[p] is < 0 or > 8) return false;
        }
        System.Array.Copy(a, art, 8); System.Array.Copy(t, team, 8);
        return true;
    }

    /// <summary>Wer mit wem verbündet ist, nach den Teams (jeder mit sich selbst).</summary>
    public static bool Verbuendet(int a, int b)
        => a == b || (a is >= 0 and < 8 && b is >= 0 and < 8 && PlatzTeam[a] > 0 && PlatzTeam[a] == PlatzTeam[b]);

    public static bool HatTeams()
    {
        if (!PlatzListe) return false;
        for (int p = 0; p < 8; p++) if (PlatzArt[p] != ArtLeer && PlatzTeam[p] > 0) return true;
        return false;
    }
}
