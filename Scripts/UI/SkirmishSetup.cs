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
}
