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
    private OptionButton? _netMode;
    private LineEdit? _netAddr;
    private SpinBox? _netPlayers;
    private Label? _netStatus;
    private VBoxContainer? _netDetails;

    /// <summary>Der Rahmen um den Netzkasten (<c>Accent("Netzwerk", …)</c>). Er
    /// wird IMMER gebaut und im Gefecht nur versteckt — siehe
    /// <see cref="ApplyNetEntry"/>. ⚠ Versteckt wird der RAHMEN, nicht sein
    /// Inhalt: ein sichtbarer Rahmen um einen unsichtbaren Inhalt ist ein leerer
    /// Kasten mit Titel.</summary>
    private Control? _netFrame;

    /// <summary>Ist der Aufbauschirm ueber »Multiplayer« aufgerufen worden?
    /// Gesetzt von <c>ShowSetup(net)</c>.</summary>
    private bool _netEntry;

    /// <summary>Die Statuszeile zu einem Modus. Steht hier und nicht im
    /// Signalhandler, weil <c>OptionButton.Selected</c> aus dem CODE zu setzen
    /// das Signal <c>item_selected</c> NICHT ausloest — der Text waere dann
    /// stehengeblieben und haette das Gegenteil behauptet.</summary>
    /// <remarks>⚠ Zweizeilig gehalten, und das ist am Bild gemessen: die dritte
    /// Zeile hat die Kartenliste daneben um einen Eintrag verkürzt (6 von 7 mit
    /// Rollbalken). Der Hinweis, dass die Adresse nur mit ihrem Port gilt, steht
    /// jetzt als Tooltip am Adressfeld — dort, wo man ihn braucht.</remarks>
    private static string NetStatusText(int sel) => sel switch
    {
        1 => "Gastgeber: die Mitspieler rufen dich an. Karte, Keim und " +
             "Plätze verteilst DU.",
        2 => "Beitreten: Karte, Keim und Startplatz kommen vom Gastgeber — " +
             "was hier steht, wird überschrieben.",
        _ => "Netzwerk aus",
    };

    /// <summary>
    /// Den Netzkasten auf den Einstieg einstellen.
    ///
    /// <para>⚠ 13.08.2026, gemeldet vom Spieler: »das Netzwerkspiel raus aus
    /// Gefecht«. Im Gefecht ist der Kasten unsichtbar UND sein Modus steht
    /// zwingend auf »Aus«. Beides zusammen, nicht nur das Verstecken: ein
    /// verborgenes Auswahlfeld, das noch »Gastgeber« sagt, wäre ein Netzspiel,
    /// von dem der Spieler nichts weiß — und <c>StartWhenNetIsReady</c> fragt
    /// genau dieses Feld.</para>
    ///
    /// <para>Im Multiplayer ist umgekehrt »Aus« keine Wahl: die Zeile heißt so,
    /// weil man gegen Menschen spielen will. Vorgabe ist <b>Gastgeber</b>.</para>
    ///
    /// <para>⚠ Hat die Befehlszeile die Steckdose schon geöffnet, wird gar nichts
    /// angetastet — <see cref="BuildNetRow"/> hat den Modus dann gesetzt und
    /// gesperrt, und der Kasten muss sichtbar bleiben, damit man den Zustand
    /// sieht (Regel: nachsehen, ob der Schalter noch angeschlossen ist).</para>
    /// </summary>
    private void ApplyNetEntry(bool net)
    {
        if (_netMode == null) return;

        if (NetworkManager.Active)
        {
            if (_netFrame != null) _netFrame.Visible = true;
            return;
        }

        if (_netFrame != null) _netFrame.Visible = net;
        _netMode.SetItemDisabled(0, net);
        _netMode.Selected = net ? (_netMode.Selected == 0 ? 1 : _netMode.Selected) : 0;
        if (_netDetails != null) _netDetails.Visible = _netMode.Selected != 0;
        if (_netStatus != null) _netStatus.Text = NetStatusText(_netMode.Selected);
    }

    /// <summary>
    /// DIE LOBBY — und sie ist absichtlich klein.
    ///
    /// <para>⚠ <b>Warum kein eigener Schirm.</b> Ein Netzspiel ist bei uns ein
    /// GEFECHT, und alles, was eine Partie ausmacht — Karte, Startplatz,
    /// Techstandard, Rohstoffe, »alle Einheiten« — steht schon im
    /// Gefechtsschirm. Ein zweiter Schirm daneben wäre ein zweiter Ort, an dem
    /// dieselben Einstellungen stehen, und damit ein zweiter Ort, an dem sie
    /// auseinanderlaufen können. Das Original macht es genauso: sein
    /// Aufbaubild ist eines, und Kommando 979 trägt die Einstellungen des
    /// Gastgebers ein, 981 startet.</para>
    ///
    /// <para>⚠ <b>Wo der Kasten steht, ist DREIMAL am Bild entschieden worden</b>
    /// — jeder Anlauf hat den Startknopf verschoben, und keiner davon war am
    /// Modell zu erkennen:</para>
    /// <list type="number">
    ///   <item>in voller Breite unter dem Haken »Luftwaffe …« → »GEMETZEL
    ///   STARTEN« nur noch zur Hälfte im Bild;</item>
    ///   <item>in der Einstellungsspalte rechts → im <b>Multiplayer</b>-Schirm
    ///   war der Startknopf <b>ganz</b> aus dem Fenster geschoben. Aufgeklappt
    ///   kostet der Kasten rund 180 px, und rechts stehen schon Vorschau,
    ///   Kartenname, Gegner, Startplatz, Schwierigkeit, Rohstoffe und
    ///   Techstandard. Solange er eingeklappt war, fiel das nicht auf — und
    ///   eingeklappt ist er nur im Gefecht, wo er jetzt gar nicht mehr steht;</item>
    ///   <item><b>links unter der Kartenliste</b>, wo deren sieben Einträge rund
    ///   200 px Luft lassen. Dort kostet er den Schirm KEINE Höhe.</item>
    /// </list>
    ///
    /// <para>Der Schirm sitzt in einem <c>ScrollContainer</c>, es ging also nie
    /// etwas verloren — aber ein Startknopf, den man erst herunterrollen muss,
    /// ist schlechter als einer, den man sieht. Der Rollbalken rettet die
    /// Bedienbarkeit, nicht die Gestaltung.</para>
    ///
    /// <para>Eingeklappt bleibt der Kasten, solange »Aus« steht: die Zeile
    /// »Techstandard« hat am 13.08. schon einmal die Bedienhilfe aus dem Fenster
    /// geschoben, der Schirm ist bei 1600x900 an der Grenze.</para>
    /// </summary>
    private Control BuildNetRow()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);

        _netMode = new OptionButton();
        _netMode.AddItem("Aus (gegen den Rechner)");
        _netMode.AddItem("Gastgeber");
        _netMode.AddItem("Beitreten");
        _netMode.Selected = 0;
        box.AddChild(Row("Modus", _netMode));

        _netDetails = new VBoxContainer { Visible = false };
        _netDetails.AddThemeConstantOverride("separation", 4);
        _netAddr = new LineEdit
        {
            Text = "127.0.0.1:27015",
            TooltipText = "Adresse und Port, durch Doppelpunkt getrennt. Ohne Port "
                        + "gilt 27015.\nBeim Gastgeber zählt nur der Port — er hört "
                        + "zu, er ruft nicht an.",
        };
        _netDetails.AddChild(Row("Adresse:Port", _netAddr));
        _netPlayers = new SpinBox { MinValue = 2, MaxValue = 8, Value = 2 };
        _netDetails.AddChild(Row("Menschen", _netPlayers));
        _netStatus = new Label
        {
            Text = "Netzwerk aus",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.7f, 0.75f, 0.8f),
        };
        _netDetails.AddChild(_netStatus);
        box.AddChild(_netDetails);

        _netMode.ItemSelected += _ =>
        {
            _netDetails.Visible = _netMode.Selected != 0;
            // Beim Gastgeber ist die Adresse gegenstandslos: er hört zu, er
            // ruft nicht an. Der Port darin gilt trotzdem.
            if (_netStatus != null) _netStatus.Text = NetStatusText(_netMode.Selected);
        };

        // Hat die Befehlszeile die Steckdose schon geöffnet, soll der Schirm das
        // ZEIGEN und nicht das Gegenteil behaupten (Regel 11).
        if (NetworkManager.Active)
        {
            _netMode.Selected = NetworkManager.Link!.IsHost ? 1 : 2;
            _netDetails.Visible = true;
            _netMode.Disabled = true;
            _netAddr.Editable = false;
            _netStatus.Text = "von der Befehlszeile geöffnet — " + NetworkManager.StatusLine();
        }
        // ⚠ Der Kasten selbst bleibt sichtbar — ein- und ausgeblendet wird sein
        // RAHMEN (_netFrame, gesetzt beim Aufbau des Schirms), damit im Gefecht
        // nicht ein leerer Kasten mit der Überschrift »Netzwerk« stehenbleibt.
        return box;
    }

    /// <summary>Adresse und Port aus einem Feld — <c>127.0.0.1:27015</c> oder nur
    /// eine Adresse.</summary>
    private (string Addr, int Port) NetTarget()
    {
        string v = (_netAddr?.Text ?? "").Trim();
        int c = v.LastIndexOf(':');
        if (c > 0 && int.TryParse(v[(c + 1)..], out int p) && p > 0) return (v[..c], p);
        return (v.Length > 0 ? v : "127.0.0.1", 27015);
    }

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
        // Der Schirm darf die Steckdose öffnen — bis heute ging das nur über die
        // Befehlszeile, und einen Schalter, den ein Spieler nicht hat, hat er
        // nicht (dieselbe Lehre wie beim Karteneditor).
        // ⚠ `_netEntry` ist die zweite Sperre, absichtlich doppelt: eine Leitung
        // wird nur geöffnet, wenn der Spieler über »Multiplayer« hereingekommen
        // ist. Im Gefecht steht der Modus schon auf »Aus« (ApplyNetEntry) — aber
        // ein Netzspiel, von dem der Spieler nichts weiß, ist der eine Fehler,
        // den ich hier nicht einmal über einen zweiten Weg zulassen will.
        if (!NetworkManager.Active && _netEntry && _netMode is { Selected: > 0 })
        {
            var (addr, port) = NetTarget();
            bool asHost = _netMode.Selected == 1;
            int players = (int)(_netPlayers?.Value ?? 2);
            if (!NetworkManager.StartFromMenu(asHost, addr, port, players))
            {
                if (_netStatus != null) _netStatus.Text = "FEHLER: " + NetworkManager.Fault;
                return;                     // ⚠ NICHT starten. Ein Netzspiel ohne
            }                               //   Leitung ist ein Einzelspieler, der
        }                                   //   sich für ein Netzspiel hält.

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
            // ⚠ Auch INS BILD, nicht nur ins Protokoll: ein Wartezustand, den man
            // nicht sieht, ist von einem Absturz nicht zu unterscheiden. Das
            // Original schreibt dafür »Warte auf Server« (0x4FA570) auf den
            // Schirm, und es tut das nach 5000 ms.
            if (_netStatus != null)
                _netStatus.Text = $"{NetworkManager.StatusLine()}  ({waited / 1000} s)" +
                                  (waited >= 5000 ? "  —  »Warte auf Server«" : "");
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
