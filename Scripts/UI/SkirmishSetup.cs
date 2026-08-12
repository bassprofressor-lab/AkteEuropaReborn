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
