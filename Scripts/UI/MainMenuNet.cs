namespace AkteEuropaReborn.UI;

using Godot;
using AkteEuropaReborn.Network;

/// <summary>
/// DAS WARTEN VOR DEM START — die Stelle, an der ein Netzspiel entsteht.
///
/// <para>⚠ <b>Warum das Menü warten MUSS und nicht einfach starten kann.</b> Der
/// Keim der Partie kommt vom Vermittler, und er muss gesetzt sein, <b>bevor</b>
/// die Karte lädt: <c>NavGrid.Build</c> ruft <c>Determinism.NewMap</c>, und das
/// nimmt <c>Determinism.Forced ?? Fnv32(Kartenname)</c>. Wer nach dem Laden
/// keimt, keimt zu spät — die Karte hat dann schon gewürfelt (gemessen im
/// Zwillingslauf am 12.08.2026: ein zweiter Keim aus einer anderen Schreibweise
/// des Kartennamens gab 2819280086 statt 2524234454).</para>
///
/// <para>Dasselbe gilt für den <b>Spielerplatz</b>: welchen Platz ich führe,
/// entscheidet der Vermittler, und <c>MapEntityLayer._Ready</c> liest ihn aus
/// <see cref="SkirmishSetup.Human"/>. Auch das ist vor dem Szenenwechsel.</para>
///
/// <para>Das Original macht es in derselben Reihenfolge und es ist gelesen:
/// Kommando <b>979</b> trägt die Einstellungen des Gastgebers ein
/// (<c>Techstandard </c> 0x502630, <c>Konto </c>, <c>Rohstoffe: </c>,
/// <c>Wetter: </c>, <c>Start: </c>) und <b>981</b> startet danach
/// (@0x4C3C95 → 0x401E15). Erst einrichten, dann losfahren.</para>
///
/// <para>⚠ Eine eigene Teildatei, damit an <c>MainMenu.cs</c> genau eine Zeile
/// zu ändern war — dort arbeitet gerade jemand anderes an den Nachbarzeilen.</para>
/// </summary>
public partial class MainMenu
{
    /// <summary>
    /// Losfahren, aber erst wenn die Partie steht. Ohne Netzschalter ist das
    /// derselbe Aufruf wie vorher, nur einen Bildlauf später.
    ///
    /// <para>⚠ <c>await ToSignal(GetTree(), ProcessFrame)</c> statt eines
    /// eigenen <c>_Process</c>: <see cref="MainMenu"/> hat schon eines, und eine
    /// zweite Überschreibung derselben Methode ist in einer Teilklasse nicht
    /// möglich. Ein Warten, das sich in die Hauptschleife einhängt, statt sie zu
    /// blockieren — ein blockierendes Warten würde die Steckdose nicht mehr
    /// abfragen, und dann käme die Partie nie an.</para>
    /// </summary>
    private async void StartWhenNetIsReady()
    {
        if (!NetworkManager.Active) { OnStart(); return; }

        // ⚠ ZUERST die Auswahlfelder auswerten, DANN anmelden. Was der Vermittler
        // verteilt, ist genau das, was hier im Schirm steht — er kann es nicht
        // anbieten, bevor er es gelesen hat. Beim Mitspieler ist es wirkungslos:
        // seine Werte werden von der Partie überschrieben.
        ApplySetupFields();
        NetworkManager.Announce();

        GD.Print("netz: das Menü wartet auf die Partie, bevor die Karte lädt " +
                 "(der Keim des Vermittlers muss vor NavGrid.Build stehen)");

        ulong t0 = Time.GetTicksMsec();
        ulong said = 0;
        while (!NetworkManager.SessionReady)
        {
            if (!IsInsideTree()) return;
            if (NetworkManager.TimedOut)
            {
                GD.PrintErr("netz: die Partie kommt nicht zustande — " + NetworkManager.Fault);
                GetTree().Quit(5);
                return;
            }
            ulong waited = Time.GetTicksMsec() - t0;
            // Die Fristen des Originals, damit im Protokoll dieselben Zahlen
            // stehen: 2000 ms je Spielerplatz (@0x4C5B78), 5000 ms bis
            // »Warte auf Server« (0x1388 @0x41504E).
            if (waited >= 2000 && said < 2000) { said = 2000; GD.Print($"netz: warte {waited} ms auf die Partie"); }
            if (waited >= 5000 && said < 5000) { said = 5000; GD.Print($"netz: »Warte auf Server« ({waited} ms)"); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (!IsInsideTree()) return;

        // ⚠ Die Karte kommt jetzt aus der Partie, nicht aus dem Auswahlfeld.
        // Sonst spielte der Mitspieler die Karte, die BEI IHM im Menü stand.
        var s = NetworkManager.Link!.Session!;
        int mi = Maps.FindIndex(m => m.File == s.Map);
        if (mi >= 0) SelectMap(mi);
        else GD.PrintErr($"netz: die Karte »{s.Map}« des Gastgebers gibt es hier nicht — " +
                         "der Lauf wird auseinanderlaufen, und zwar sofort");

        GD.Print($"netz: Partie steht, Karte {s.Map}, mein Platz {s.MySlot} — losfahren");
        OnStart();
    }
}
