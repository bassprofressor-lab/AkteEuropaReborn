namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// Die Brücke, über die <c>--campaign=&lt;n&gt;</c> und
/// <c>--skirmish=&lt;karte&gt;,…</c> auch in der ausgelieferten Fassung
/// ankommen — siehe <see cref="Core.CommandLine"/> für die Messung.
///
/// <para>⚠ <b>Warum das hier steht und nicht in MainMenu.cs.</b> Der Ort, an
/// dem es hingehörte, ist <c>MainMenu.AutoStart</c>: dort müsste
/// <c>OS.GetCmdlineUserArgs()</c> durch <c>Core.CommandLine.Args</c> ersetzt
/// werden, drei Zeilen in drei Dateien (MainMenu, BriefingScreen, PauseMenu).
/// An denen arbeitet gerade jemand anderes, und ein Zusammenstoss dort wäre
/// teurer als diese Datei. <c>MainMenu</c> ist eine <c>partial class</c>, also
/// darf dieses Stück von ihr woanders liegen — es kommt an dieselben privaten
/// Felder und ruft dieselbe <c>StartMission</c>, es gibt also KEINEN zweiten
/// Weg in eine Mission, nur einen zweiten Weg zu den Schaltern.</para>
///
/// <para>⚠ Diese Datei soll wieder verschwinden, sobald die drei
/// Oberflächendateien selbst <c>Core.CommandLine.Args</c> lesen.</para>
/// </summary>
public partial class MainMenu
{
    /// <summary>
    /// <c>_EnterTree</c> läuft vor <c>_Ready</c>; der eigentliche Anlauf wird
    /// darum verschoben, damit er hinter <c>AutoStart</c> liegt und nur dann
    /// etwas tut, wenn AutoStart nichts gesehen hat.
    /// </summary>
    public override void _EnterTree() => CallDeferred(nameof(StartFromFullCmdline));

    private void StartFromFullCmdline()
    {
        // Stand der Trenner da, hat AutoStart schon alles getan. Diese Brücke
        // darf dann NICHTS tun — sonst startete eine Mission zweimal.
        if (OS.GetCmdlineUserArgs().Length > 0) return;

        foreach (string a in Core.CommandLine.Args)
            if (a == "--no-briefing") _skipBriefing = true;

        foreach (string a in Core.CommandLine.Args)
        {
            if (!a.StartsWith("--campaign")) continue;
            string arg = a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : "";
            var m = int.TryParse(arg, out int no)
                ? Campaign.CampaignManager.ByIndex(no)
                : Campaign.CampaignManager.Next();
            if (m == null)
            {
                GD.PrintErr("campaign: keine solche Mission");
                GetTree().Quit(2);
                return;
            }
            GD.Print("cmdline: --campaign ohne »--« angekommen — Brücke springt ein");
            // DIESELBE Stelle, die auch der Menüknopf und AutoStart nehmen,
            // samt Briefing.
            StartMissionByIndex(m.Index);
            return;
        }
    }
}
