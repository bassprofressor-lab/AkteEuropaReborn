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
    public static void Tidy(Node host = null!)
    {
        if (Skip) return;
        // ⚠⚠ 19.08.2026 — ZUERST DIE PAUSE, und sie war der eigentliche Fehler.
        //
        // Gemeldet, zum zweiten Mal: »nachdem ich eine Kampagne mittendrin
        // beende, tauchen die Popups im Menue auf, und ich muss sie erst
        // schliessen, um wieder die Maus benutzen zu koennen.«
        //
        // Ein Hilfefenster HAELT DAS SPIEL AN (HelpWindow.PauseWhileOpen, aus
        // den Optionen des Originals gelesen). Bricht man die Mission ab,
        // waehrend eines offen steht, wurden die Fenster hier zwar weggeraeumt
        // — die Pause blieb stehen. Im Menue friert damit alles ein, was auf
        // ProcessMode.Inherit steht, also der ganze Mausbetrieb. Nur das
        // Hilfefenster laeuft weiter (ProcessMode.Always, sonst koennte man es
        // nie wegklicken), und sein Wegklicken gibt die Pause frei.
        //
        // Die Popups waren also nicht die Ursache — sie waren das EINZIGE, was
        // noch reagierte. Deshalb las sich der Fehler wie »Popups fangen die
        // Maus«, und deshalb half Wegklicken.
        //
        // ⚠ Warum drei Prueflaeufe gruen meldeten: HelpWindow.PauseErlaubt
        // schaltet die Pause KOPFLOS ganz ab (mit gutem Grund — ein Fenster,
        // das niemand wegklicken kann, haengt jeden Prueflauf auf). Jeder
        // Prueflauf lief damit in einer Welt, in der es diesen Fehler nicht
        // geben KANN. Seit heute hebt `--help-pause` das auf.
        var tree = host?.GetTree();
        if (tree != null && (UI.HelpWindow.HaeltPause || tree.Paused))
        {
            GD.Print("LeaveToMenu: Baum war angehalten (Hilfefenster) — Pause freigegeben");
            tree.Paused = false;
        }
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
        sb.Append($"   Popups im Hauptmenue: {(windows > 0 ? "JA — B10 steht" : "nein")}\n");

        // ⚠ 19.08.2026 — DAS VOLLSTAENDIGE VERZEICHNIS, und zwar weil die
        // schmale Zaehlung gelogen hat, ohne unwahr zu sein.
        //
        // Der Spieler meldet zum zweiten Mal: »die Popups tauchen im Menue auf
        // und fangen die Maus«. Der Prueflauf sagte beide Male »nein« — er
        // zaehlt aber nur HelpWindow-Knoten unter der HelpLayer. Was ihn
        // faengt, muss kein HelpWindow sein: es reicht IRGENDEIN Control an
        // SceneTree.Root, das die Maus nicht durchlaesst.
        //
        // Deshalb steht hier jetzt alles, was an der Wurzel haengt, mit Art und
        // Mausdurchlass. Eine Zahl, die nur eine Bauart kennt, kann den Fehler
        // nicht finden, den eine andere Bauart macht.
        sb.Append("   was sonst an der Wurzel haengt:\n");
        int faenger = 0;
        foreach (var c in tree.Root.GetChildren())
        {
            if (c.IsQueuedForDeletion()) continue;
            string art = c.GetType().Name;
            string zusatz = "";
            if (c is Control ct)
            {
                bool faengt = ct.MouseFilter != Control.MouseFilterEnum.Ignore && ct.Visible;
                if (faengt) faenger++;
                zusatz = $" [Control, Maus {ct.MouseFilter}, {(ct.Visible ? "sichtbar" : "unsichtbar")}]";
            }
            else if (c is CanvasLayer cl2)
            {
                int kinder = 0, sichtbar = 0;
                foreach (var k in cl2.GetChildren())
                {
                    if (k.IsQueuedForDeletion()) continue;
                    kinder++;
                    if (k is Control kc && kc.Visible
                        && kc.MouseFilter != Control.MouseFilterEnum.Ignore) { sichtbar++; faenger++; }
                }
                zusatz = $" [CanvasLayer, {kinder} Kinder, davon {sichtbar} mausfangend]";
            }
            sb.Append($"      {c.Name} ({art}){zusatz}\n");
        }
        sb.Append($"   mausfangende Knoten ueber dem Menue: {faenger}\n");
        // ⚠ DIE ZAHL, DIE HIER GEFEHLT HAT. Ein angehaltener Baum friert im
        // Menue alles ein, was auf ProcessMode.Inherit steht — das sieht fuer
        // den Spieler genauso aus wie ein Fenster, das die Maus faengt, und war
        // in Wahrheit die Ursache. Sie steht jetzt im Bericht, damit der
        // Prueflauf beim naechsten Mal nicht wieder danebenschaut.
        sb.Append($"   Baum angehalten: {(tree.Paused ? "JA — DAS FRIERT DAS MENUE EIN" : "nein")}");
        if (tree.Paused) windows++;      // faellt damit auch durch
        report = sb.ToString();
        return (wouldOverlay || windows > 0) ? 1 : 0;
    }
}
