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
    private Control? _netAddrRow;
    private LineEdit? _netAddr;
    private ZahlWahl? _netPlayers;
    private Label? _netStatus;
    private VBoxContainer? _netDetails;

    /// <summary>
    /// »ABBRECHEN« — und dieser Knopf ist die Hälfte der Reparatur des
    /// Spielerbefunds vom 15.08.2026 (»drücke ich bei multiplayer auf partie
    /// starten, schließt sich das spiel«).
    ///
    /// <para>Die andere Hälfte ist, dass aus dem Menü heraus nicht mehr beendet
    /// wird (<see cref="NetworkManager.Frist"/>). Beides gehört zusammen: ein
    /// Gastgeber muss <b>unbegrenzt</b> warten dürfen, weil er auf einen MENSCHEN
    /// wartet — und wer unbegrenzt wartet, braucht einen Ausweg, sonst ist das
    /// Warten selbst der Absturz. Die Uhr war der falsche Ausweg.</para>
    ///
    /// <para>⚠ Er ist nur sichtbar, WÄHREND gewartet wird. Ein Abbrechen-Knopf,
    /// der immer dasteht, sagt nichts darüber, dass gerade etwas läuft.</para>
    /// </summary>
    private Button? _netCancel;

    /// <summary>»PARTIEN IM LAN SUCHEN« und die Liste darunter. ⚠ Nur beim
    /// BEITRETEN sichtbar — ein Gastgeber sucht nicht, er wird gesucht.</summary>
    private Button? _netSearch;
    private ItemList? _netList;

    /// <summary>Die gefundenen Partien in der Reihenfolge der Liste, damit ein
    /// Klick auf Zeile N zur richtigen Adresse führt. ⚠ Eigene Liste und nicht
    /// die von <see cref="NetDiscovery"/>: die wächst während der Suche weiter,
    /// und ein Index, der auf eine wachsende Liste zeigt, zeigt irgendwann
    /// daneben.</summary>
    private readonly System.Collections.Generic.List<NetOffer> _netShown = new();

    /// <summary>Vom Knopf gesetzt, von der Warteschleife gelesen. ⚠ Kein
    /// direkter Abbruch im Signalhandler: der läuft mitten im Bildlauf, die
    /// Schleife wartet auf <c>ProcessFrame</c>, und ein Aufräumen an zwei
    /// Stellen gleichzeitig ist genau die Sorte Fehler, die man erst im dritten
    /// Anlauf findet.</summary>
    private bool _netCancelWanted;

    /// <summary>Läuft gerade eine Warteschleife? Solange sie läuft, ist der
    /// Startknopf gesperrt — sonst legt ein zweiter Druck eine zweite
    /// Warteschleife auf dieselbe Leitung.</summary>
    private bool _netWaiting;

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
        1 => "Gastgeber: Karte und Einstellungen von hier gelten für alle.\n" + NetworkManager.HostHint(),
        3 => "Online über den Lobby-Server: »PARTIE STARTEN« öffnet die Lobby. " +
             "Karte und Einstellungen von hier gelten, wenn DU den Raum eröffnest.",
        2 => "Beitreten: Karte, Einstellungen und Startplatz bestimmt der Gastgeber.",
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

        if (NetworkManager.Active && !(NetworkManager.FromMenu && NetworkManager.Link == null))
        {
            if (_netFrame != null) _netFrame.Visible = true;
            return;
        }

        if (_netFrame != null) _netFrame.Visible = net;
        _netMode.SetItemDisabled(0, net);
        _netMode.Selected = net ? (_netMode.Selected == 0 ? 1 : _netMode.Selected) : 0;
        if (_netDetails != null) _netDetails.Visible = _netMode.Selected != 0;
        if (_netStatus != null) _netStatus.Text = NetStatusText(_netMode.Selected);
        ApplyNetSearchVisible();
    }

    /// <summary>Der Suchknopf und die Liste gehören zum BEITRETEN und zu nichts
    /// sonst. ⚠ Eigene Methode, weil <c>OptionButton.Selected</c> aus dem CODE zu
    /// setzen das Signal <c>item_selected</c> nicht auslöst — derselbe Grund, aus
    /// dem <see cref="NetStatusText"/> herausgezogen ist.</summary>
    /// <summary>⭐ 14.11.2026 — Fable C6: beim Beitreten waren Karte, Gegner, Rohstoffe … bedienbar,
    /// obwohl der Gastgeber sie ueberschreibt. Jetzt gesperrt und abgeblendet, solange »Beitreten«
    /// gewaehlt ist. Gegenschalter --netz-grund-alt.</summary>
    private void EinstellungenSperren(bool sperren)
    {
        if (System.Array.IndexOf(Core.CommandLine.Args, "--netz-grund-alt") >= 0) sperren = false;
        foreach (var o in new OptionButton?[] { _ai, _slot, _level, _res, _konto, _tech, _tempo })
            if (o != null) o.Disabled = sperren;
        if (_allUnits != null) _allUnits.Disabled = sperren;
        if (_mapList != null)
        {
            _mapList.MouseFilter = sperren ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
            _mapList.Modulate = sperren ? new Color(1, 1, 1, 0.45f) : Colors.White;
        }
    }

    private LineEdit? _netName;
    private Control? _netNameRow;

    private void ApplyNetSearchVisible()
    {
        if (_netNameRow != null) _netNameRow.Visible = _netMode is { Selected: 1 or 2 };   // online fragt die Lobby
        // Online gibt es keine Adresse — der Server steht fest (Settings.OnlineServer).
        if (_netAddrRow != null) _netAddrRow.Visible = _netMode is not { Selected: 3 };
        bool join = _netMode is { Selected: 2 };
        if (_netSearch != null) _netSearch.Visible = join;
        EinstellungenSperren(join);
        if (_netList != null && !join) _netList.Visible = false;
        PlaetzeSichtbar();   // P7: Platzliste beim LAN-Gastgeber, sonst die alten Zeilen
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
        _netMode.AddItem("Online (Internet)");     // ⭐ 11.11.2026, Online Stufe 2
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
        _netAddrRow = Row("Adresse:Port", _netAddr);
        _netDetails.AddChild(_netAddrRow);
        // ⭐ Fable C12/D10: der eigene Name auch im LAN (derselbe wie in der Online-Lobby)
        if (!NetworkManager.TastenAlt)
        {
            _netName = new LineEdit
            {
                Text = Settings.OnlineName, MaxLength = 20, PlaceholderText = "so sehen dich die anderen",
                TooltipText = "Dein Name in der Partie (Spielerliste, Nachrichten). Derselbe wie in der Online-Lobby.",
            };
            _netName.TextChanged += t => { if (!Core.Pruefschutz.Aktiv) Settings.OnlineName = t.Trim(); };
            _netNameRow = Row("Dein Name", _netName);
            _netDetails.AddChild(_netNameRow);
        }
        _netPlayers = new ZahlWahl { MinValue = 2, MaxValue = 8, Value = 2, Beschriftung = v => $"{v} Menschen" };   // Fable C2
        _netDetails.AddChild(Row("Menschen", _netPlayers));
        _netStatus = new Label
        {
            Text = "Netzwerk aus",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.7f, 0.75f, 0.8f),
        };
        // ---- die LAN-Suche ---------------------------------------------------
        //
        // ⚠ Nur beim BEITRETEN. Ein Gastgeber sucht nicht, er wird gesucht — und
        // ein Knopf, der in seinem Fall nichts tut, ist schlimmer als keiner.
        _netSearch = new Button { Text = "PARTIEN IM LAN SUCHEN", Visible = false };
        _netSearch.Pressed += SearchLanFromMenu;
        _netDetails.AddChild(_netSearch);

        // ⚠ Die Liste bleibt unsichtbar, bis eine Suche gelaufen ist. Eine leere
        // Liste, die immer dasteht, sagt nichts — und kostet 84 px Höhe auf einem
        // Schirm, der an der Grenze ist.
        _netList = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 84),
            Visible = false,
            AllowReselect = true,
        };
        _netList.ItemSelected += PickLanOffer;
        _netDetails.AddChild(_netList);

        _netDetails.AddChild(_netStatus);

        _netCancel = new Button { Text = "ABBRECHEN", Visible = false };
        _netCancel.Pressed += () => _netCancelWanted = true;
        _netDetails.AddChild(_netCancel);

        box.AddChild(_netDetails);

        _netMode.ItemSelected += _ =>
        {
            _netDetails.Visible = _netMode.Selected != 0;
            // Beim Gastgeber ist die Adresse gegenstandslos: er hört zu, er
            // ruft nicht an. Der Port darin gilt trotzdem.
            if (_netStatus != null) _netStatus.Text = NetStatusText(_netMode.Selected);
            ApplyNetSearchVisible();
        };

        // Hat die Befehlszeile die Steckdose schon geöffnet, soll der Schirm das
        // ZEIGEN und nicht das Gegenteil behaupten (Regel 11).
        // Fable C10: ein aus dem MENUE gehaltener Lobby-Draht (zurueck im Raum) ist das nicht.
        if (NetworkManager.Active && !(NetworkManager.FromMenu && NetworkManager.Link == null))
        {
            _netMode.Selected = (NetworkManager.Link?.IsHost ?? NetworkManager.WantHost) ? 1 : 2;   // bug-760: online gibt es vor match_start noch keine Leitung
            _netDetails.Visible = true;
            _netMode.Disabled = true;
            _netAddr.Editable = false;
            _netStatus.Text = "von der Befehlszeile geöffnet — " + NetworkManager.StatusLine();
        }
        // Der Prüfschalter für den Spielerweg (--net-probe=), verzögert, damit er
        // hinter ApplyNetEntry liegt.
        CallDeferred(nameof(NetProbeArm));
        CallDeferred(nameof(NachDerPartie));
        CallDeferred(nameof(NetzEndeProbe));

        // ⚠ Der Kasten selbst bleibt sichtbar — ein- und ausgeblendet wird sein
        // RAHMEN (_netFrame, gesetzt beim Aufbau des Schirms), damit im Gefecht
        // nicht ein leerer Kasten mit der Überschrift »Netzwerk« stehenbleibt.
        return box;
    }

    /// <summary>
    /// ZURÜCK IN DIE LOBBY — der Weg, den es vor dem 15.08.2026 nicht gab.
    ///
    /// <para>Statt das Programm zu beenden: die Leitung schliessen, den Grund in
    /// die Statuszeile schreiben, den Abbrechen-Knopf wegnehmen und den
    /// Startknopf wieder freigeben. <b>Der Spieler steht danach genau da, wo er
    /// vor dem Druck stand</b>, und kann es noch einmal versuchen oder etwas
    /// anderes einstellen.</para>
    ///
    /// <para>⚠ Die Leitung MUSS dabei zu — ein liegengebliebener Lauscher auf
    /// Port 27015 lässt den nächsten Versuch mit »CreateServer(27015) -&gt;
    /// ERR_CANT_CREATE« scheitern, und dann sähe die Reparatur schlechter aus als
    /// der Fehler. Deshalb <see cref="NetworkManager.Cancel"/> und nicht bloss
    /// eine Meldung.</para>
    /// </summary>
    private void BackToLobby(string why)
    {
        NetworkManager.Cancel(why);
        _netWaiting = false;
        _netCancelWanted = false;
        if (_netCancel != null) _netCancel.Visible = false;
        if (_startButton != null) _startButton.Disabled = false;
        if (_netMode != null)
        {
            // Die Felder waren gesperrt, solange die Befehlszeile die Leitung
            // hielt; jetzt hält sie niemand mehr.
            _netMode.Disabled = false;
            if (_netAddr != null) _netAddr.Editable = true;
        }
        if (_netStatus != null) _netStatus.Text = why;
        GD.Print("netz: zurück in der Lobby — " + why);
    }

    /// <summary>
    /// DIE LAN-SUCHE AUS DEM MENÜ — »zeig mir, wo was offen ist«.
    ///
    /// <para>Ein Rundruf, dann <see cref="NetDiscovery.SearchWindowMs"/>
    /// zuhören, dann die Liste füllen. Währenddessen ist der Knopf gesperrt und
    /// sagt, dass gesucht wird: eine Suche, die man nicht sieht, ist von einem
    /// tauben Knopf nicht zu unterscheiden.</para>
    ///
    /// <para>⚠ <b>Eine leere Liste MUSS reden.</b> »Nichts gefunden« und »die
    /// Firewall hat den Rundruf gefressen« sehen über UDP gleich aus — der Text
    /// aus <see cref="NetDiscovery.Verdict"/> sagt beides und nennt die Firewall
    /// als Verdacht, nicht als Befund.</para>
    /// </summary>
    private async void SearchLanFromMenu()
    {
        if (_netSearch == null || _netList == null) return;
        _netSearch.Disabled = true;
        string was = _netSearch.Text;
        _netSearch.Text = "suche …";
        if (_netStatus != null) _netStatus.Text = "Rundruf ins LAN abgeschickt, höre zu …";

        bool ok = NetworkManager.SearchLan();
        ulong t0 = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - t0 < (ulong)NetDiscovery.SearchWindowMs)
        {
            if (!IsInsideTree()) return;
            NetworkManager.CollectLan();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (!IsInsideTree()) return;
        NetworkManager.CollectLan();

        _netSearch.Disabled = false;
        _netSearch.Text = was;

        var d = NetworkManager.Discovery;
        _netShown.Clear();
        _netList.Clear();
        if (ok && d != null)
            foreach (var o in d.Found)
            {
                _netShown.Add(o);
                _netList.AddItem(o.ListLine());
                GD.Print($"netz: Listenzeile »{o.ListLine()}«");
                int row = _netList.ItemCount - 1;
                // Die lange Form als Tooltip: die Liste ist gekürzt, damit die
                // freien Plätze ins Bild passen (siehe NetOffer.ListLine), und
                // hier steht wieder alles.
                _netList.SetItemTooltip(row, o.Describe() + $"\n(über {o.Via})");
                // ⚠ Eine Partie ohne freien Platz bleibt SICHTBAR, aber nicht
                // wählbar. Wer sie ausblendete, liesse den Spieler glauben, es
                // gäbe nichts — und »voll« ist eine andere Nachricht als »nichts
                // da«.
                if (o.Free <= 0) _netList.SetItemDisabled(row, true);
                // Fable: eine andere Fassung kann nicht mitspielen — sichtbar, aber nicht waehlbar
                if (!o.Passt)
                {
                    _netList.SetItemDisabled(row, true);
                    _netList.SetItemTooltip(row, o.Describe() + "\nDiese Partie läuft mit einer anderen Fassung des Spiels — " +
                                                 "Beitreten geht nur mit derselben Fassung.");
                }
            }
        _netList.Visible = _netList.ItemCount > 0;
        if (_netStatus != null)
            _netStatus.Text = d?.Verdict(NetDiscovery.SearchWindowMs) ?? "Suche nicht möglich";
        GD.Print("netz: " + (d?.Verdict(NetDiscovery.SearchWindowMs) ?? "Suche nicht möglich"));
    }

    /// <summary>Ein Klick in die Liste trägt Adresse und Port ein — damit muss
    /// niemand mehr eine IP tippen. Das war die Frage des Spielers.</summary>
    private void PickLanOffer(long row)
    {
        int i = (int)row;
        if (i < 0 || i >= _netShown.Count || _netAddr == null) return;
        var o = _netShown[i];
        _netAddr.Text = o.Target;
        if (_netStatus != null)
            _netStatus.Text = $"gewählt: {o.Describe()} — »PARTIE STARTEN« tritt bei.";
        GD.Print($"netz: Partie gewählt — {o.Describe()}");
    }

    /// <summary>
    /// DER PRÜFSCHALTER FÜR DEN SPIELERWEG: <c>--net-probe=host</c> oder
    /// <c>--net-probe=join:&lt;adresse&gt;[:&lt;port&gt;]</c>.
    ///
    /// <para>Er stellt den Netzkasten ein und drückt den Startknopf — über
    /// dieselbe Methode, die der Knopf ruft, also nicht daneben herum. Zusammen
    /// mit <c>--setup=net</c> und <c>--shot=</c> ist damit der Spielerweg
    /// messbar.</para>
    ///
    /// <para>⚠ <b>Warum es diesen Schalter überhaupt braucht.</b> Der Fehler, den
    /// er prüft, ist genau der, den kein kopfloser Lauf zeigen kann: dort ist
    /// Beenden das gewünschte Verhalten. Ein Prüfstand, der nur den kopflosen Weg
    /// kennt, hätte »Programm beendet sich nach 20 s« als Erfolg gemeldet — und
    /// er hat es getan, bis der Spieler es am echten Weg fand. Also muss der
    /// Prüfstand an den Weg mit Fenster heran, und dafür muss er auf einen Knopf
    /// drücken können.</para>
    ///
    /// <para>⚠ Wartet 30 Bilder ab, damit er HINTER <see cref="ApplyNetEntry"/>
    /// liegt — das setzt den Modus, und wer davor stellt, stellt umsonst.</para>
    /// </summary>
    private async void NetProbeArm()
    {
        string want = "";
        foreach (string a in Core.CommandLine.Args)
            if (a.StartsWith("--net-probe=")) want = a["--net-probe=".Length..];
        if (want.Length == 0) return;

        for (int i = 0; i < 30; i++)
        {
            if (!IsInsideTree()) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (!IsInsideTree() || _netMode == null) return;

        // ⭐ 11.11.2026: `online:<raum>` — Modus »Online« und PARTIE STARTEN; den Rest
        // (anmelden, Raum eröffnen, START) fährt das Lobbyfenster selbst (OnlineLobbyScreen).
        if (want.StartsWith("online"))
        {
            _netMode.Selected = 3;
            if (_netDetails != null) _netDetails.Visible = true;
            if (_netStatus != null) _netStatus.Text = NetStatusText(3);
            ApplyNetSearchVisible();
            GD.Print("netz-probe: Modus 3 (Online), druecke den Startknopf");
            StartWhenNetIsReady();
            return;
        }

        if (want.StartsWith("join") || want.StartsWith("suche"))
        {
            _netMode.Selected = 2;
            int c = want.IndexOf(':');
            if (c > 0 && _netAddr != null) _netAddr.Text = want[(c + 1)..];
        }
        else _netMode.Selected = 1;
        if (_netDetails != null) _netDetails.Visible = true;
        if (_netStatus != null) _netStatus.Text = NetStatusText(_netMode.Selected);
        ApplyNetSearchVisible();

        // ⚠ `suche` drückt den SUCH-Knopf und nicht den Startknopf. Damit ist die
        // LAN-Liste im Bild belegbar, ohne dass danach eine Partie anläuft — eine
        // Messung, die den Gegenstand gleich weiterschiebt, misst ihn nicht.
        if (want.StartsWith("suche"))
        {
            GD.Print("netz-probe: Modus 2 (Beitreten), druecke »PARTIEN IM LAN SUCHEN«");
            SearchLanFromMenu();
            return;
        }

        GD.Print($"netz-probe: Modus {_netMode.Selected} " +
                 $"({(_netMode.Selected == 1 ? "Gastgeber" : "Beitreten")}), " +
                 $"Adresse {_netAddr?.Text}, druecke den Startknopf");
        StartWhenNetIsReady();
    }

    /// <summary>⭐ 11.11.2026 (Online 2b): nach einer Netzpartie zurück an die Stelle, von der
    /// sie ausging — online gleich wieder in die Lobby (angemeldet mit dem gespeicherten
    /// Namen), im LAN in den Netzkasten. Die Meldung des Endes steht in der Statuszeile.</summary>
    private async void NachDerPartie()
    {
        // Fable C10: nach einer Online-Partie ist der Lobby-Draht noch offen (zurueck in den Raum) —
        // nur eine offene SPIELLEITUNG heisst »noch im Spiel«.
        if (NetworkManager.Link != null || (NetworkManager.Lobby != null && !NetworkManager.ZurueckZurLobby)) return;
        string msg = NetworkManager.Fault;
        bool online = NetworkManager.ZurueckZurLobby;
        if (!online && msg.Length == 0) return;
        NetworkManager.ZurueckZurLobby = false;
        if (Rendering.NetGameRunner.EndeProbeGefeuert) GD.Print($"netz-ende-probe: NachDerPartie — {(online ? "zurueck in die Lobby" : "in den Netzkasten")}, Meldung »{msg}«");
        if (!online) { if (_netStatus != null) _netStatus.Text = msg; return; }
        for (int i = 0; i < 10; i++) { if (!IsInsideTree()) return; await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        ShowSetup(net: true);
        if (_netMode == null) return;
        _netMode.Selected = 3;
        if (_netDetails != null) _netDetails.Visible = true;
        if (_netStatus != null) _netStatus.Text = msg;
        ApplyNetSearchVisible();
        OpenOnlineLobby(autoConnect: true, meldung: msg);
    }

    /// <summary>Fable B4, --netz-ende-probe: beim Menueaufbau den Netzzustand drucken
    /// und beenden. Soll: keine offene Leitung, Netzkasten frei, Meldung in der Statuszeile (LAN)
    /// bzw. die Lobby offen (online).</summary>
    private async void NetzEndeProbe()
    {
        if (!Rendering.NetGameRunner.EndeProbeGefeuert) return;
        // gleich beim Menueaufbau (dort hat Fable gemessen) — 60 Bilder spaeter hat die Befehlszeile
        // (--skirmish) schon das naechste Gefecht gestartet
        await System.Threading.Tasks.Task.CompletedTask;
        bool lobby = false;
        foreach (var k in GetChildren()) if (k is OnlineLobbyScreen) lobby = true;
        bool runner = false;
        foreach (var k in GetTree().Root.GetChildren()) if (k is Rendering.NetGameRunner) runner = true;
        GD.Print($"netz-ende-probe: Active={NetworkManager.Active} Taktgeber={(runner ? "HAENGT NOCH" : "weg")} " +
                 $"Netzkasten={(_netMode?.Disabled == true ? "GESPERRT" : "frei")} Lobby={(lobby ? "offen" : "zu")} " +
                 $"ZurueckZurLobby={NetworkManager.ZurueckZurLobby} Status=»{_netStatus?.Text}« Lobbydraht={(NetworkManager.Lobby != null ? "offen" : "zu")}");
        if (NetworkManager.Lobby == null) GetTree().Quit(0);   // C10: sonst misst das Lobbyfenster weiter
    }

    /// <summary>Das Online-Lobbyfenster über den Gefechtsschirm legen.</summary>
    private void OpenOnlineLobby(bool autoConnect = false, string meldung = "")
    {
        foreach (var k in GetChildren()) if (k is OnlineLobbyScreen) return;
        var lobby = new OnlineLobbyScreen
        {
            OnMatchStart = StartWhenNetIsReady,
            CurrentMap = () => { ApplySetupFields(); return SkirmishSetup.Map; },
            EinstellungenLesen = RaumEinstellungenLesen,     // Fable P6 (C9/D3)
            EinstellungenSetzen = RaumEinstellungenSetzen,
            MaxHumans = (int)(_netPlayers?.Value ?? 2),
            AutoConnect = autoConnect && Settings.OnlineName.Length > 0,
            Meldung = meldung,
        };
        // bug-764: der Gefechtsschirm schien durch das Fenster — solange es offen ist, aus.
        if (_setup != null) _setup.Visible = false;
        lobby.TreeExiting += () =>
        {
            if (_setup != null && IsInsideTree()) _setup.Visible = true;
            // Fable C10: nach »ZURÜCK« aus einem gehaltenen Raum den Netzkasten wieder freigeben
            if (!NetworkManager.Active && _netMode != null) { _netMode.Disabled = false; if (_netAddr != null) _netAddr.Editable = true; }
        };
        AddChild(lobby);
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
    private string _letzteKartenZeile = "";

    private async void StartWhenNetIsReady()
    {
        // ⚠ Ein zweiter Druck während des Wartens legte eine ZWEITE
        // Warteschleife auf dieselbe Leitung — zwei Schleifen, die beide
        // aufräumen wollen. Der Startknopf ist währenddessen gesperrt; dieser
        // Riegel ist der zweite, weil ein gesperrter Knopf immer noch über
        // `CallDeferred` aus AutoStart erreicht werden kann.
        if (_netWaiting) return;

        // Der Schirm darf die Steckdose öffnen — bis heute ging das nur über die
        // Befehlszeile, und einen Schalter, den ein Spieler nicht hat, hat er
        // nicht (dieselbe Lehre wie beim Karteneditor).
        // ⚠ `_netEntry` ist die zweite Sperre, absichtlich doppelt: eine Leitung
        // wird nur geöffnet, wenn der Spieler über »Multiplayer« hereingekommen
        // ist. Im Gefecht steht der Modus schon auf »Aus« (ApplyNetEntry) — aber
        // ein Netzspiel, von dem der Spieler nichts weiß, ist der eine Fehler,
        // den ich hier nicht einmal über einen zweiten Weg zulassen will.
        // ⭐ 11.11.2026 Online Stufe 2: »Online« startet nicht, es öffnet die Lobby. Das Fenster
        // ruft diese Methode wieder, sobald der Server die Partie beginnt (match_start).
        if (_netEntry && _netMode is { Selected: 3 } && NetworkManager.Lobby?.MatchStart == null)
        {
            OpenOnlineLobby();
            return;
        }

        if (!NetworkManager.Active && _netEntry && _netMode is { Selected: > 0 and < 3 })
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
        _netWaiting = true;
        _netCancelWanted = false;
        if (_netCancel != null) _netCancel.Visible = true;
        if (_startButton != null) _startButton.Disabled = true;

        while (!NetworkManager.SessionReady)
        {
            if (!IsInsideTree()) return;

            // ⚠ DER AUSWEG. Spielerbefund vom 15.08.2026: »drücke ich bei
            // multiplayer auf partie starten, schließt sich das spiel.«
            if (_netCancelWanted)
            {
                BackToLobby("abgebrochen — die Leitung ist wieder zu.");
                return;
            }
            // ⭐ 14.11.2026 — Fable B6: ein Fehler der Leitung (»Protokoll x des Gastgebers passt nicht
            // zu unserem y«) stand nur im Protokoll; der Spieler sah 30 s »warte auf die Partie« und dann
            // »einfach noch einmal versuchen«. Jetzt sofort abbrechen und den wahren Grund zeigen.
            if (NetworkManager.Link is { Fault.Length: > 0 } lf && !NetworkManager.SessionReady
                && System.Array.IndexOf(Core.CommandLine.Args, "--netz-grund-alt") < 0)
            {
                if (NetworkManager.FromMenu)
                {
                    BackToLobby("Nicht verbunden: " + lf.Fault + ". Beide Rechner brauchen dieselbe Fassung des Spiels.");
                    return;
                }
                GD.PrintErr("netz: " + lf.Fault);
                GetTree().Quit(5);
                return;
            }
            if (NetworkManager.TimedOut)
            {
                // ⚠ HIER STAND `GetTree().Quit(5)`, und das war der Absturz.
                //
                // Der Rückgabewert 5 ist für einen kopflosen Prüflauf genau
                // richtig — ein Prüfstand, der ewig auf ein Gegenüber wartet,
                // hält die Bausperre für alle drei Agenten. Auf dem Spielerweg
                // ist dieselbe Zeile ein Programmende ohne Erklärung, nach
                // zwanzig Sekunden, in denen nichts weiter passiert ist als
                // dass niemand beigetreten ist.
                //
                // ⚠ Und deshalb konnte kein kopfloser Lauf ihn zeigen: dort IST
                // Beenden das gewünschte Verhalten. Belegt ist die Reparatur nur
                // mit FENSTER (scratchpad/mp-frist-*.png).
                if (NetworkManager.FromMenu)
                {
                    BackToLobby(NetworkManager.Fault + " — nichts verloren, " +
                                "einfach noch einmal versuchen.");
                    return;
                }
                GD.PrintErr("netz: die Partie kommt nicht zustande — " + NetworkManager.Fault);
                GetTree().Quit(5);
                return;
            }
            ulong waited = Time.GetTicksMsec() - t0;

            // ⚠ »Warte auf Server« gilt nur für den BEITRETENDEN — im Bild UND im
            // Protokoll. Beim Gastgeber wäre es falsch: er IST der Server, und
            // dass noch niemand da ist, ist kein Fehlerzustand, sondern der
            // Normalfall der ersten Minute. Das Original meint mit der Zeile
            // (0x4FA570) auch genau den Mitspieler — sie hängt an
            // [0x4F6F28] == 0xFF, dem Warten auf die Freigabe 978.
            bool joining = NetworkManager.Link is { IsHost: false };

            // Die Fristen des Originals, damit im Protokoll dieselben Zahlen
            // stehen: 2000 ms je Spielerplatz (@0x4C5B78), 5000 ms bis
            // »Warte auf Server« (0x1388 @0x41504E).
            if (waited >= 2000 && said < 2000)
            { said = 2000; GD.Print($"netz: warte {waited} ms auf die Partie"); }
            if (waited >= 5000 && said < 5000)
            {
                said = 5000;
                GD.Print(joining
                    ? $"netz: »Warte auf Server« ({waited} ms)"
                    : $"netz: der Gastgeber wartet weiter ({waited} ms) — {NetworkManager.HostHint()}");
            }
            // ⚠ Auch INS BILD, nicht nur ins Protokoll: ein Wartezustand, den man
            // nicht sieht, ist von einem Absturz nicht zu unterscheiden. Das
            // Original schreibt dafür »Warte auf Server« (0x4FA570) auf den
            // Schirm, und es tut das nach 5000 ms.
            string zeile = NetworkManager.StatusLine();
            if (zeile.StartsWith("Karte des Gastgebers") && zeile != _letzteKartenZeile)
            { _letzteKartenZeile = zeile; GD.Print("netz: " + zeile); }   // Fable D4, fuers Protokoll
            if (_netStatus != null)
                _netStatus.Text = $"{zeile}  ({waited / 1000} s)" +
                                  (joining && waited >= 5000 ? "  —  »Warte auf Server«" : "");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (!IsInsideTree()) return;
        _netWaiting = false;
        if (_netCancel != null) _netCancel.Visible = false;

        // ⚠ Die Karte kommt jetzt aus der Partie, nicht aus dem Auswahlfeld.
        // Sonst spielte der Mitspieler die Karte, die BEI IHM im Menü stand.
        var s = NetworkManager.Link!.Session!;
        int mi = KarteAufnehmen(s.Map);   // ⭐ 11.11.2026: auch eine empfangene eigene Karte
        // Pruefschalter (Fable B12): so tun, als fehle die Karte hier
        if (System.Array.IndexOf(Core.CommandLine.Args, "--netz-probe-karte-fehlt") >= 0) mi = -1;
        if (mi >= 0) SelectMap(mi);
        else if (System.Array.IndexOf(Core.CommandLine.Args, "--netz-karte-fehlt-alt") < 0)
        {
            // ⭐ 14.11.2026 — Fable B12: fehlt die Karte, startete der Mitspieler trotzdem — auf der
            // Karte, die BEI IHM im Menue stand, und lief sofort auseinander; gesagt wurde es nur im
            // Protokoll. Jetzt: nicht starten, sich selbst ausschalten (die anderen spielen ohne
            // diesen Platz weiter, ab Takt 0), kurz warten, damit das Paket hinausgeht, dann die
            // Leitung schliessen und es im Schirm sagen. Gegenschalter --netz-karte-fehlt-alt.
            GD.PrintErr($"netz: die Karte »{s.Map}« des Gastgebers gibt es hier nicht — nicht gestartet, ausgeschaltet");
            NetworkManager.Link!.SendAusschalten(s.Me, s.Me);
            NetworkManager.Link.FlushNow();
            // sanft trennen: erst wenn der Gastgeber das Ausschalten bestaetigt hat (er laedt gerade
            // die Karte und hoert ein paar Sekunden nicht zu). Hoechstens 20 s, nach Zeit gemessen.
            ulong ab = Time.GetTicksMsec();
            bool getrennt = false;
            while (Time.GetTicksMsec() - ab < 20000 && IsInsideTree() && !(getrennt = NetworkManager.Link?.SanftTrennen() ?? true))
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print($"netz: {(getrennt ? "sanft getrennt" : "NICHT sanft getrennt")} nach {Time.GetTicksMsec() - ab} ms");
            if (!IsInsideTree()) return;
            string titel = s.EigeneKarte ? "eine eigene Karte des Gastgebers" : $"die Karte »{s.Map}«";
            BackToLobby($"Nicht gestartet: {titel} fehlt auf diesem Rechner, und sie kam nicht an. " +
                        "Die anderen spielen ohne dich weiter. Bitte die Karte beim Gastgeber erneut speichern " +
                        "oder die gleiche Fassung des Spiels verwenden.");
            return;
        }
        else GD.PrintErr($"netz: die Karte »{s.Map}« des Gastgebers gibt es hier nicht — " +
                         "der Lauf wird auseinanderlaufen, und zwar sofort");

        GD.Print($"netz: Partie steht, Karte {s.Map}, mein Platz {s.MySlot} — losfahren");
        OnStart();
    }
}
