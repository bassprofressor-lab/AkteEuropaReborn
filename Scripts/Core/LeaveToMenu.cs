namespace AkteEuropaReborn.Core;

using Godot;

/// <summary>
/// AUFRAEUMEN BEIM VERLASSEN DER SPIELWELT — die fehlende Gegenstelle zu den
/// Knoten, die absichtlich an <c>SceneTree.Root</c> parken.
///
/// <para><b>Zwei Fehler, eine Ursache</b> (gemeldet vom Spieler, B9 und B10):
/// »sobald man im Editor war und dann in den Gefechtsmodus geht, ist das
/// Editorfeld immer noch da« und »geht man aus einer Kampagne raus, sieht man
/// die Popups im Hauptmenue«. Beide kommen aus derselben Bauform.</para>
///
/// <para><c>ChangeSceneToFile</c> ersetzt <b>nur</b> <c>CurrentScene</c>. Wer als
/// GESCHWISTER von <c>CurrentScene</c> unter der Wurzel haengt, ueberlebt jeden
/// Szenenwechsel — und genau das tun zwei Helfer <b>mit Absicht</b>:</para>
/// <list type="bullet">
/// <item><see cref="Editor.MapEditWatcher"/> haengt dort, damit sich der
/// Bearbeitungs-Ueberzug an die <c>MapEntityLayer</c> der NAECHSTEN Szene
/// haengen kann (siehe <see cref="Editor.MapEditSession.Watch"/>).</item>
/// <item>Die <c>CanvasLayer</c> »HelpLayer« der <see cref="UI.HelpWindow"/>
/// haengt dort, damit die Fenster nicht in der Weltleinwand liegen und von der
/// Kamera aus dem Bild getragen werden.</item>
/// </list>
///
/// <para><b>Der Fehler ist nicht die Bauform, sondern die fehlende zweite
/// Haelfte.</b> Beide Helfer hatten einen Einschalter und keinen Ausschalter:
/// <c>MapEditSession.Active</c> wurde gesetzt und nie zurueckgenommen
/// (<c>Drop()</c> stand da, wurde aber im ganzen Baum <b>kein einziges Mal</b>
/// gerufen), und <c>HelpWindow.Forget()</c> lief nur beim LADEN einer Karte —
/// ein Weg, den das Hauptmenue nie nimmt, weil dort keine
/// <c>MapEntityLayer</c> entsteht.</para>
///
/// <para><b>Warum hier und nicht an den Ausgaengen.</b> Es gibt keinen
/// Szenen-Manager; <c>ChangeSceneToFile</c> steht an neun Stellen verstreut
/// (Pausenmenue, <c>ToMenu</c>, Abschlussfenster, Editor, Menue selbst). Eine
/// Kur an den Ausgaengen muesste jeden davon treffen und jeden kuenftigen
/// dazu. Der EINGANG ist dagegen einer: wer im Hauptmenue steht, hat die
/// Spielwelt verlassen — egal, ueber welche Tuer. Darum ruft
/// <c>MainMenu._Ready</c> diese eine Zeile.</para>
///
/// <para><b>Und dazu eine Zahl</b>, weil ein Fehler ohne Zaehler wiederkommt
/// (Arbeitsweise 17): <c>--leave-check</c> geht den echten Weg —
/// Karte laden, Fenster oeffnen, Bearbeitungsmodus einschalten, ueber
/// <c>ChangeSceneToFile</c> ins Menue — und zaehlt drueben nach.
/// <c>--leave-check=alt</c> stellt ueber <see cref="Skip"/> die ALTE Fassung im
/// selben Programm nach und muss dabei durchfallen; ohne diese Gegenprobe
/// waere nicht zu sehen, ob der Zaehler ueberhaupt etwas sehen KANN
/// (Arbeitsweise 31).</para>
/// </summary>
public static class LeaveToMenu
{
    /// <summary>GEGENPROBE: nicht aufraeumen, also die Fassung vor dem
    /// 15.08.2026 nachstellen. Nur <c>--leave-check=alt</c> setzt das.</summary>
    public static bool Skip;

    /// <summary>Der Pruefstand will nach dem Aufraeumen gezaehlt haben. Wird
    /// beim Zaehlen selbst zurueckgesetzt, damit ein zweiter Menueeintritt
    /// nicht ein zweites Mal beendet.</summary>
    public static bool Report;

    /// <summary>Die eine Zeile, die <c>MainMenu._Ready</c> ruft.</summary>
    public static void Tidy()
    {
        if (Skip) return;
        // Die Missions-Popups: Fenster schliessen und das Weggeklickte
        // vergessen. Dieselbe Methode, die der Kartenlader beim Missionsstart
        // ruft — sie ist nicht neu, sie wurde auf diesem Weg nur nie erreicht.
        UI.HelpWindow.Forget();
        // Der Bearbeitungsmodus: Schalter aus und Waechter weg. Die Karte im
        // Speicher BLEIBT — wer im Menue erneut »bearbeiten« drueckt, soll sie
        // noch vorfinden.
        Editor.MapEditSession.Leave();
    }

    /// <summary>Was nach dem Aufraeumen noch an der Wurzel haengt. Gibt den
    /// Rueckgabewert des Laufs zurueck: 0 = sauber.</summary>
    public static int Count(SceneTree tree, out string report)
    {
        int windows = 0;
        bool layer = false, watcher = false;
        foreach (var c in tree.Root.GetChildren())
        {
            if (c.IsQueuedForDeletion()) continue;
            if (c is CanvasLayer cl && cl.Name == "HelpLayer")
            {
                layer = true;
                foreach (var w in cl.GetChildren())
                    if (w is UI.HelpWindow && !w.IsQueuedForDeletion()) windows++;
            }
            if (c is Editor.MapEditWatcher) watcher = true;
        }
        bool active = Editor.MapEditSession.Active;
        // ⚠ DAS ist die Frage, um die es geht, und sie wird ausgeschrieben statt
        // aus den Zahlen erschlossen: der Ueberzug haengt sich an die naechste
        // MapEntityLayer genau dann an, wenn der Waechter lebt UND der Schalter
        // steht (MapEditWatcher.OnAdded). Beides zusammen ist der Fehler B9.
        bool wouldOverlay = active && watcher;

        var sb = new System.Text.StringBuilder();
        sb.Append($"leave-check nachher: Popups {windows} offen (HelpLayer " +
                  $"{(layer ? "steht" : "weg")}), Waechter {(watcher ? "lebt" : "weg")}, " +
                  $"Bearbeitungsmodus {(active ? "AN" : "aus")}\n");
        sb.Append($"   ein neues Gefecht bekaeme das Editorfeld: " +
                  $"{(wouldOverlay ? "JA — B9 steht" : "nein")}\n");
        sb.Append($"   Popups im Hauptmenue: {(windows > 0 ? "JA — B10 steht" : "nein")}");
        report = sb.ToString();
        return (wouldOverlay || windows > 0) ? 1 : 0;
    }
}
