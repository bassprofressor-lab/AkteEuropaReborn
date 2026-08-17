namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// <b>Das Feld »neue technologien«</b> auf dem Briefingschirm — noch nicht
/// gebaut, aber die Anschlussstelle steht.
///
/// <para>Im Original zeigt der Kasten unten links, was diese Mission NEU
/// freischaltet: eine <b>Überschrift</b> und darunter ein <b>Bild</b> des
/// Bauteils. In Kampagne 2 steht dort »LEICHTE BORDKANONE« mit dem Bild der
/// Kanone — siehe <c>Bug Bilder/kampagne2 original previewl.png</c>. Bei
/// Mission 1 ist der Kasten leer, dort gibt es nichts Neues.</para>
///
/// <para><b>Was schon da ist:</b> <c>user://data/Maps/research.json</c> führt
/// <b>23 Techniken mit Namen</b> (Schlüssel = Bauteilzeile, z. B. 65
/// Teleporter … 79 Zielfokus). ⚠ <b>Was fehlt, ist die Zuordnung zur
/// MISSION</b> — welche Technik in welcher Mission dazukommt, steht dort
/// nicht. Die muss im Missionssatz oder in einer eigenen Tafel von GAME.EXE
/// stecken und ist noch zu suchen.</para>
///
/// <para>Die Bilder sollten über <see cref="PortraitBank"/> erreichbar sein —
/// dieselbe Bank, aus der der Entwurfsschirm seine Bauteilbilder holt.</para>
///
/// <para><b>Die Lage im 640×480-Bild</b>, am Hintergrundbild ausgemessen: der
/// Kasten »neue technologien« liegt bei x 20..215, y 330..455; die Überschrift
/// des Originals sitzt oben darin auf einem schwarzen Streifen, das Bild
/// darunter links.</para>
/// </summary>
public partial class BriefingScreen
{
    /// <summary>Das Feld »neue technologien« füllen. Solange die Zuordnung
    /// Mission→Technik nicht gelesen ist, bleibt der Kasten leer — genau wie
    /// bei einer Mission, die nichts freischaltet.</summary>
    private void BuildTechPanel(Vector2 at, float scale)
    {
        // noch nichts zu zeigen — siehe Kopf
    }
}
