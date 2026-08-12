namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// Der Teil des Zwillings-Prüfstands, der an den Kartenlader des Betrachters
/// heranmuss — siehe <see cref="DeterminismTwinRunner"/> für den Zweck.
///
/// <para>⚠ <b>Warum eine eigene Datei und nicht MapViewer.cs.</b> Dieselbe
/// Begründung wie bei <c>Core/MainMenuCommandLine.cs</c> und
/// <c>Simulation/DeterminismProbe.cs</c>: an den Oberflächendateien wird
/// gerade gearbeitet. <c>MapViewer</c> ist eine <c>partial class</c>, also darf
/// dieses Stück von ihr woanders liegen; es kommt an dieselben privaten
/// Mitglieder <c>_mapIndex</c>, <c>MapNames</c> und <c>LoadMeta</c> und nimmt
/// <b>denselben Weg in eine Karte</b>, den <c>LoadMap</c> nimmt — es gibt also
/// keinen zweiten Kartenlader, nur einen zweiten Aufrufer.</para>
/// </summary>
public partial class MapViewer
{
    /// <summary>Die Karte, die dieser Betrachter gerade zeigt. Der Prüfstand
    /// braucht den NAMEN, weil er sich seine Simulationen selbst lädt statt die
    /// vorhandene mitzumessen (die ist einen Takt voraus, siehe
    /// <c>DeterminismTwinRunner.TryStart</c>).</summary>
    internal string TwinMapName =>
        MapNames.Length == 0 ? "" : MapNames[Mathf.PosMod(_mapIndex, MapNames.Length)];

    /// <summary>
    /// Eine weitere, vollständige Simulation derselben Karte aufsetzen.
    ///
    /// <para>Sie hängt unter dem Betrachter wie die echte, wird aber
    /// <b>stillgelegt</b> (<c>SetProcess(false)</c>): getaktet wird sie
    /// ausschliesslich vom Prüfstand, mit einem festen dt. Das ist der ganze
    /// Punkt — die Godot-Hauptschleife liefert die verstrichene Bildzeit, und
    /// genau die soll hier nicht mitreden.</para>
    ///
    /// <para>⚠ Es wird KEINE Kamera gesetzt, kein Nebel neu gebaut, keine
    /// Minikarte gezeichnet. Alles, was der Prüfstand misst, ist der
    /// ganzzahlige Zustand; alles andere kostet nur Zeit und Speicher. Die
    /// Anfangslage kommt aus derselben <c>StartSkirmish</c>, die auch das
    /// gespielte Gefecht aufsetzt — ein zweiter Aufsetzweg wäre ein zweiter
    /// Ort für Abweichungen.</para>
    /// </summary>
    internal MapEntityLayer BuildTwin(string name, out string err)
    {
        err = "";
        var sim = new MapEntityLayer();
        AddChild(sim);                       // löst _Ready aus (Kataloge, Einheiten, Waffen)
        sim.SetProcess(false);
        sim.SetPhysicsProcess(false);
        sim.Visible = false;

        // ⚠ Zeitstempel um jeden Abschnitt. Sie stehen hier, weil der erste
        // Zwillingslauf am 12.08.2026 im ZWEITEN Ladevorgang stehenblieb (über
        // sechs Minuten ohne eine weitere Zeile, während ein vollständiger
        // normaler Lauf 10,9 s braucht) — und ohne Zwischenmeldung war nicht zu
        // sagen, ob er rechnet oder steht.
        ulong t0 = Time.GetTicksMsec();
        var meta = LoadMeta(name);
        if (meta.Count == 0) { err = $"{name}: keine Metadaten (map_*.json fehlt?)"; return sim; }
        GD.Print($"twin: meta gelesen nach {Time.GetTicksMsec() - t0} ms");

        sim.Load(name, meta);
        GD.Print($"twin: Load fertig nach {Time.GetTicksMsec() - t0} ms");

        // ⚠ DER WÜRFEL DER EBENE — und das ist der erste echte Determinismus-
        // fehler, den der Zwillings-Prüfstand GEFUNDEN hat, nicht einer, den
        // wir vermutet hätten.
        //
        // Am 12.08.2026 liefen die beiden Zwillinge auf map_NET02 bei Takt 123
        // (2,05 s) auseinander, in vierzehn Zahlen und ausschliesslich in den
        // Lagerbeständen: »Einheit #10, Feld StockS: A=0 B=1«, »Feld StockT:
        // A=1000 B=999«. Das ist die Produktion, und die hängt an genau einer
        // Zeile — MapEntityLayer.cs:7919, <c>_rng.RandiRange(0, 99)</c>, die
        // Produktionschance (sec24 +0x03/+0x04).
        //
        // <c>_rng</c> ist <c>private readonly RandomNumberGenerator _rng =
        // new()</c> (MapEntityLayer.cs:7802), und <b>Godot keimt einen frisch
        // angelegten RandomNumberGenerator ZUFÄLLIG</b>. Jede Instanz würfelt
        // also von Anfang an anders — im Prüfstand die beiden Zwillinge, im
        // Netzspiel die beiden Maschinen. Auf map_NET07 fiel es nicht auf, weil
        // die Karte laut ihrer eigenen Meldung »0 Lager, 0 Fabriken« hat und
        // die Zeile nie erreicht wird; auf map_NET02 fiel es nach zwei
        // Sekunden auf.
        //
        // <c>DeterminismSeed</c> (Simulation/DeterminismProbe.cs) setzt
        // <c>_rng.Seed</c> auf den Kartenkeim. Das ist die KRÜCKE der
        // Vorarbeit, nicht der Umbau: sie wirkt nur, weil der Prüfstand sie
        // ruft. Damit ein gespieltes Netzspiel dasselbe bekommt, muss die
        // Keimung an den Kartenladeweg selbst — der Bericht nennt die Stelle.
        sim.DeterminismSeed(name);

        // Dieselbe Weiche wie in _Ready: eine Kampagnenmission behält ihre
        // Armee, ein Gefecht wird aufgesetzt.
        if (UI.SkirmishSetup.Active)
        {
            if (UI.SkirmishSetup.CampaignMission > 0)
                sim.StartCampaign(UI.SkirmishSetup.Human, UI.SkirmishSetup.Level);
            else
                sim.StartSkirmish(UI.SkirmishSetup.Human, UI.SkirmishSetup.AiCount, UI.SkirmishSetup.Level);
        }
        GD.Print($"twin: Aufsatz fertig nach {Time.GetTicksMsec() - t0} ms");
        return sim;
    }
}
