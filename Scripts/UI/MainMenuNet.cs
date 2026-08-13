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
    /// <para>⚠ <b>Zwei Entscheidungen, die am BILD getroffen sind</b>
    /// (<c>scratchpad/mp-lobby-zu.png</c>, <c>mp-lobby-auf.png</c>, 1600x900):</para>
    /// <list type="number">
    ///   <item><b>Eingeklappt, solange »Aus« steht.</b> Der Schirm ist an der
    ///   Grenze — die Zeile »Techstandard« hat am 13.08. schon die Bedienhilfe
    ///   aus dem Fenster geschoben.</item>
    ///   <item><b>In der Einstellungsspalte, nicht in voller Breite darunter.</b>
    ///   Der erste Anlauf setzte diesen Kasten unter den Haken »Luftwaffe …«, und
    ///   im Bild war »GEMETZEL STARTEN« danach nur noch zur Hälfte zu sehen. Der
    ///   Schirm sitzt in einem <c>ScrollContainer</c>, es ging also nichts
    ///   verloren — aber ein Startknopf, den man erst herunterrollen muss, ist
    ///   schlechter als einer, den man sieht. In der Spalte kostet die
    ///   eingeklappte Zeile gar keine Höhe: die Spalte ist kürzer als die
    ///   Kartenliste daneben.</item>
    /// </list>
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
        box.AddChild(Row("Netzwerk", _netMode));

        _netDetails = new VBoxContainer { Visible = false };
        _netDetails.AddThemeConstantOverride("separation", 4);
        _netAddr = new LineEdit { Text = "127.0.0.1:27015" };
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
            if (_netStatus != null)
                _netStatus.Text = _netMode.Selected switch
                {
                    1 => "Gastgeber: die Adresse gilt nur mit ihrem Port; die Mitspieler " +
                         "rufen dich an. Karte, Keim und Plätze verteilst DU.",
                    2 => "Beitreten: Karte, Keim, Startplatz und alle Einstellungen kommen " +
                         "vom Gastgeber — was hier links steht, wird überschrieben.",
                    _ => "Netzwerk aus",
                };
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
        if (!NetworkManager.Active && _netMode is { Selected: > 0 })
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
