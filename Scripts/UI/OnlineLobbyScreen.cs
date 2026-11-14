#nullable enable
namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using AkteEuropaReborn.Network;

/// <summary>
/// ⭐ 11.11.2026 — <b>DIE ONLINE-LOBBY</b> (Online-Mehrspieler, Stufe 2).
///
/// <para>Ein Fenster über dem Gefechtsschirm, gleicher schlichter Stil wie
/// <see cref="SettingsScreen"/>: anmelden, Raumliste, Raum eröffnen/beitreten,
/// Mitspieler, Chat, Start. Sobald der Server <c>match_start</c> schickt, schließt
/// es sich und ruft den bewährten Startweg des Menüs (<c>StartWhenNetIsReady</c>):
/// Einstellungen anwenden, anmelden, auf die Partie warten, Karte laden.</para>
///
/// <para>⚠ <b>UNSERE ZUTAT.</b> Das Original hat keine Raumliste — es sucht
/// Sitzungen über DirectPlay (Vorlagen 129/130) und richtet die Partie in Fenster
/// Art 37 ein (600 × 420, »Akte Europa &lt;Name&gt;'s Spiel«). Von dort übernommen:
/// der Raumname »Akte Europa &lt;Name&gt;s Spiel«, <b>kein Bereit-Zustand</b> (der
/// Gastgeber startet, wann er will), Chat-Zeilen »&lt;Name&gt;: &lt;Text&gt;«.
/// Die Spieleinstellungen (Karte, Techstandard, Konto, Rohstoffe …) bleiben im
/// Gefechtsschirm — ein zweiter Ort für dieselben Werte wäre ein zweiter Ort, an
/// dem sie auseinanderlaufen (Begründung bei <c>MainMenu.BuildNetRow</c>).</para>
/// </summary>
public partial class OnlineLobbyScreen : Control
{
    /// <summary>Der Startweg des Menüs, gerufen bei <c>match_start</c>.</summary>
    public Action? OnMatchStart { get; init; }
    /// <summary>Die Karte, die der Gefechtsschirm gerade gewählt hat (für Raumliste/Einstellungen).</summary>
    public Func<string>? CurrentMap { get; init; }
    /// <summary>Fable P6: die Einstellungen des Mehrspielerschirms als Raumeinstellungen lesen …</summary>
    public Func<JsonObject>? EinstellungenLesen { get; init; }
    /// <summary>… und dorthin zurueckschreiben (Gastgeber aendert im Raum, neuer Gastgeber uebernimmt).</summary>
    public Action<JsonObject>? EinstellungenSetzen { get; init; }

    /// <summary>Höchstzahl Menschen für einen neuen Raum (Feld »Menschen« im Netzkasten).</summary>
    public int MaxHumans { get; init; } = 4;
    /// <summary>Nach einer Partie: gleich mit dem gespeicherten Namen anmelden.</summary>
    public bool AutoConnect { get; init; }
    /// <summary>Eine Zeile, die zuerst im Chatfeld steht (wie die Partie endete).</summary>
    public string Meldung { get; init; } = "";

    private Label _status = null!;
    private VBoxContainer _login = null!, _rooms = null!, _room = null!;
    private LineEdit _name = null!, _roomName = null!, _password = null!, _newPassword = null!, _chatIn = null!;
    private Label _nameHint = null!;
    private ZahlWahl _raumGroesse = null!;   // Fable C6
    private string _pendingName = "";   // Fable B9: erst nach »welcome« speichern
    private int _seenErrors;
    private ulong _verbindeSeit;
    private ItemList _roomList = null!, _members = null!;
    private RichTextLabel _chat = null!;
    private Label _roomHead = null!;
    private Button _start = null!, _join = null!;
    private readonly List<string> _roomIds = new();
    private int _seenRev = -1, _seenChat, _seenEvents;
    private bool _handedOver;

    /// <summary>14.11.2026: der Name, mit dem die Lobby anmeldet — der Pruefschalter
    /// <c>--net-name=</c> (bisher nur im LAN) geht dem gespeicherten vor, damit zwei Pruef- oder
    /// Aufnahmelaeufe auf einem Rechner unter verschiedenen Namen auftreten.</summary>
    private static string StartName()
    {
        foreach (string a in Core.CommandLine.Args)
            if (a.StartsWith("--net-name=")) return a["--net-name=".Length..];
        return Settings.OnlineName;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var backdrop = new ColorRect { Color = new Color(0.04f, 0.05f, 0.07f, 0.96f) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(scroll);
        var middle = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        scroll.AddChild(middle);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
        box.AddThemeConstantOverride("separation", 8);
        middle.AddChild(box);

        box.AddChild(Head("ONLINE-MEHRSPIELER"));
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.7f, 0.75f, 0.8f) };
        box.AddChild(_status);
        box.AddChild(new HSeparator());

        // ---- 1. anmelden ------------------------------------------------------
        _login = new VBoxContainer();
        _name = new LineEdit { Text = StartName(), MaxLength = 20, PlaceholderText = "dein Name" };
        _login.AddChild(Row("Name", _name));
        _nameHint = new Label
        {
            Text = NameRegel,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.6f, 0.64f, 0.7f),
        };
        _login.AddChild(_nameHint);
        var connect = new Button { Text = "VERBINDEN", CustomMinimumSize = new Vector2(0, 36) };
        connect.Pressed += Connect;
        _name.TextSubmitted += _ => Connect();
        _login.AddChild(connect);
        box.AddChild(_login);

        // ---- 2. Raumliste -----------------------------------------------------
        _rooms = new VBoxContainer { Visible = false };
        _rooms.AddThemeConstantOverride("separation", 6);
        _rooms.AddChild(Sub("Offene Spiele"));
        _roomList = new ItemList { CustomMinimumSize = new Vector2(0, 130), AllowReselect = true };   // Fable C1: 170 -> 130, sonst ZURUECK unter dem Rand
        _roomList.ItemSelected += _ => UpdateJoinButton();
        _roomList.ItemActivated += _ => JoinSelected();
        _rooms.AddChild(_roomList);
        _password = new LineEdit { PlaceholderText = "Passwort zum Beitreten (nur für Räume mit 🔒)", Secret = true };
        _rooms.AddChild(_password);
        _join = new Button { Text = "BEITRETEN", Disabled = true };
        _join.Pressed += JoinSelected;
        if (DiscordAlt) _rooms.AddChild(_join);
        else
        {
            // ⭐ Fable D10: »Mitspieler finden« — der Discord-Knopf des Hauptmenues auch hier, wo man
            // auf Mitspieler wartet (dieselbe Einladung, MainMenu.DiscordEinladung)
            var jz = new HBoxContainer();
            jz.AddThemeConstantOverride("separation", 6);
            _join.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            jz.AddChild(_join);
            jz.AddChild(DiscordKnopf("MITSPIELER FINDEN (Discord)"));
            _rooms.AddChild(jz);
        }
        _rooms.AddChild(new HSeparator());
        _rooms.AddChild(Sub("Eigenes Spiel eröffnen"));
        _roomName = new LineEdit { MaxLength = 40 };
        _rooms.AddChild(Row("Raumname", _roomName));
        // ⭐ 14.11.2026 — Fable B7: eigenes Feld. Bisher wurde das Beitrittsfeld still zum Passwort
        // des eigenen Raums, und wer absichtlich einen verschlossenen Raum wollte, fand kein Feld.
        _newPassword = new LineEdit { PlaceholderText = "leer = offen für alle", Secret = true, MaxLength = 40 };
        _rooms.AddChild(Row("Passwort", _newPassword));
        // ⭐ 14.11.2026 — Fable C6: die Raumgroesse stand nur als Fussnote (»Feld Menschen im
        // Netzkasten«), und man musste dafuer zurueck. Jetzt hier, vorbelegt mit dem Netzkasten.
        _raumGroesse = new ZahlWahl { MinValue = 2, MaxValue = 8, Value = Math.Clamp(MaxHumans, 2, 8), Beschriftung = v => $"{v} Menschen" };
        _rooms.AddChild(Row("Plätze", _raumGroesse));
        var create = new Button { Text = "RAUM ERÖFFNEN" };
        create.Pressed += CreateRoom;
        _rooms.AddChild(create);
        var rename = new Button { Text = "NAME ÄNDERN", Flat = true };
        rename.Pressed += () =>
        {
            NetworkManager.Cancel("Name ändern");
            _rooms.Visible = _room.Visible = false;
            _login.Visible = true;
            _name.GrabFocus();
        };
        _rooms.AddChild(rename);
        _rooms.AddChild(new Label
        {
            Text = "Karte, Techstandard, Konto und Rohstoffe stellst du vorher im Mehrspielerschirm ein.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.6f, 0.64f, 0.7f),
        });
        box.AddChild(_rooms);

        // ---- 3. im Raum -------------------------------------------------------
        _room = new VBoxContainer { Visible = false };
        _room.AddThemeConstantOverride("separation", 6);
        _roomHead = Sub("");
        _room.AddChild(_roomHead);
        if (!LobbyAlt) RaumEinstellungenBauen(_room);   // Fable P6 (C9/D3)
        _members = new ItemList { CustomMinimumSize = new Vector2(0, 130) };
        _room.AddChild(_members);
        if (!LobbyAlt) PlatzKnoepfeBauen(_room);         // Fable P6 (D2/D4)
        var rb = new HBoxContainer();
        _start = new Button { Text = "START", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 36) };
        _start.Pressed += () => NetworkManager.Lobby?.Start();
        var leave = new Button { Text = "RAUM VERLASSEN", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        leave.Pressed += () => { NetworkManager.Lobby?.LeaveRoom(); NetworkManager.Lobby?.ListRooms(); };
        rb.AddChild(_start);
        rb.AddChild(leave);
        if (!DiscordAlt) rb.AddChild(DiscordKnopf("EINLADEN (Discord)"));
        _room.AddChild(rb);
        box.AddChild(_room);

        // ---- Chat (Lobby und Raum) ---------------------------------------------
        box.AddChild(new HSeparator());
        _chat = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(0, 90),   // Fable C1: 120 -> 90
            ScrollFollowing = true,
            BbcodeEnabled = false,
            FitContent = false,
        };
        // Fable C8: der Chat hatte keinen Rahmen und sah aus wie leerer Hintergrund
        var chatRahmen = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.12f), BorderColor = new Color(0.3f, 0.34f, 0.42f),
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        chatRahmen.SetBorderWidthAll(1);
        _chat.AddThemeStyleboxOverride("normal", chatRahmen);
        box.AddChild(Sub("Nachrichten"));
        box.AddChild(_chat);
        _chatIn = new LineEdit { MaxLength = 200, PlaceholderText = "Nachricht … (Enter schickt)" };
        _chatIn.TextSubmitted += SendChat;
        box.AddChild(_chatIn);

        box.AddChild(new HSeparator());
        var back = new Button { Text = "ZURÜCK", CustomMinimumSize = new Vector2(0, 40) };
        back.Pressed += Close;
        box.AddChild(back);

        ProcessMode = ProcessModeEnum.Always;
        if (NetworkManager.Lobby != null) _login.Visible = false;
        Refresh();

        foreach (string a in Core.CommandLine.Args)
        {
            if (a.StartsWith("--net-probe=online:")) _probeRoom = a["--net-probe=online:".Length..];
            else if (a.StartsWith("--lobby-shot=")) _shotPrefix = a["--lobby-shot=".Length..];
        }
        if (Meldung.Length > 0) _chat.AddText($"— {Meldung}\n");
        // ⭐ 11.11.2026 (Spielerwunsch): mit gespeichertem Namen gleich verbinden — die
        // Namensabfrage kommt nur beim ersten Mal oder wenn die Verbindung scheitert.
        if (NetworkManager.Lobby == null && StartName().Length > 0)
        {
            _login.Visible = false;
            CallDeferred(nameof(Connect));
        }
        if (AutoConnect) _probeRoom = "";        // nach der Partie: nicht noch einmal eröffnen
        if (_probeRoom.Length > 0)
        {
            _name.Text = "ProbeHost";
            CallDeferred(nameof(Connect));
        }
        // Pruefschalter Fable B9: --lobby-probe-name=<name> tippt den Namen und drueckt VERBINDEN;
        // B7: --lobby-probe-pass=<pw> (Passwort des eigenen Raums); B8: --lobby-probe-beitritt=<raum>:<pw>
        foreach (string a in Core.CommandLine.Args)
        {
            if (a.StartsWith("--lobby-probe-pass=")) _newPassword.Text = a["--lobby-probe-pass=".Length..];
            if (a.StartsWith("--lobby-probe-beitritt=")) _probeBeitritt = a["--lobby-probe-beitritt=".Length..];
            if (a.StartsWith("--lobby-probe-plaetze=")) _raumGroesse.Value = int.Parse(a["--lobby-probe-plaetze=".Length..]);
            if (a.StartsWith("--lobby-probe-ki=")) _probeKi = int.Parse(a["--lobby-probe-ki=".Length..]);
            if (a == "--lobby-probe-hinaus") { _hinausSchritt = 0; _probeKi = 0; _probeSchritt = 3; }
            if (a.StartsWith("--lobby-probe-karte=")) _probeKarte = a["--lobby-probe-karte=".Length..];
            // P7 Teil 2: --lobby-probe-staerke=leicht|normal|schwer (fuer die gesetzten KI),
            // --lobby-probe-team=<eigenes Team>,<Team der KI>
            if (a.StartsWith("--lobby-probe-staerke=")) _probeStaerke = Array.IndexOf(Stufen, a["--lobby-probe-staerke=".Length..]);
            if (a.StartsWith("--lobby-probe-team=")) _probeTeam = a["--lobby-probe-team=".Length..].Split(',');
        }
        foreach (string a in Core.CommandLine.Args)
            if (a.StartsWith("--lobby-probe-name="))
            {
                _name.Text = a["--lobby-probe-name=".Length..];
                _login.Visible = true;
                CallDeferred(nameof(Connect));
            }
    }

    // ---- der Prüfschalter des Spielerwegs: --net-probe=online:<raum> [--lobby-shot=<präfix>] ----
    // Fährt das Fenster wie ein Spieler: anmelden, Raum eröffnen, warten bis ein zweiter
    // Mensch da ist, Bild, START. Der Mitspieler kommt kopflos über --net-online --net-join.
    private string _probeRoom = "";
    private bool _zurueckBild;
    private ulong _zurueckT;
    private string? _shotPrefix;
    private int _probeStep;
    private ulong _probeT;

    private void ProbeTick(LobbyClient l)
    {
        if (_probeRoom.Length == 0) return;
        switch (_probeStep)
        {
            case 0 when l.Open && !l.InRoom:
                _probeT = Time.GetTicksMsec();
                _probeStep = 1;
                break;
            case 1 when Time.GetTicksMsec() - _probeT > 1200:
                Shot("liste");
                _roomName.Text = _probeRoom;
                CreateRoom();
                _probeStep = 2;
                break;
            case 2 when l.InRoom && l.HumanRoomSlots().Count >= 2 && (_probeSchritt >= 3 || (_probeKi < 0 && _probeKarte.Length == 0)):
                _probeT = Time.GetTicksMsec();
                l.SendChat("Probe: gleich gehts los");
                _probeStep = 3;
                break;
            case 3 when Time.GetTicksMsec() - _probeT > 1500:
                Shot("raum");
                GD.Print("lobby-probe: START gedrückt");
                l.Start();
                _probeStep = 4;
                break;
        }
    }

    // P6-Pruefschalter: --lobby-probe-plaetze=N, --lobby-probe-karte=<datei>, --lobby-probe-ki=N
    private int _probeKi = -1, _probeSchritt;
    private int _probeStaerke = -1;
    private string[]? _probeTeam;
    private bool _probeTeamGesetzt;
    private string _probeKarte = "";
    private ulong _probeRaumT;

    private static int _runde;
    private ulong _nachPartieT;
    private string _nachPartieStand = "";

    /// <summary>Pruefweg Fable C10 (mit --netz-ende-probe): nach der Partie im Raum? Druckt jeden
    /// Raumzustand und beendet, sobald der Raum wieder »lobby« ist (oder nach 30 s).</summary>
    private void RaumNachPartieProbe(LobbyClient l)
    {
        if (!Rendering.NetGameRunner.EndeProbeGefeuert) return;
        if (_nachPartieT == 0) _nachPartieT = Time.GetTicksMsec();
        string stand = l.InRoom ? (string?)l.Room?["state"] ?? "?" : "kein Raum";
        if (stand != _nachPartieStand)
        {
            _nachPartieStand = stand;
            GD.Print($"lobby-probe: nach der Partie — Raum {(l.InRoom ? (string?)l.Room?["name"] : "-")}, Zustand {stand}, " +
                     $"{l.HumanRoomSlots().Count} Menschen, START {(_start.Visible ? (_start.Disabled ? "gesperrt" : "frei") : "unsichtbar")} »{_start.Text}«");
        }
        // --lobby-probe-nochmal: beim ersten Mal als Gastgeber die Revanche starten statt zu beenden
        if (stand == "lobby" && _runde == 0 && l.IAmHost && l.HumanRoomSlots().Count >= 2
            && Array.IndexOf(Core.CommandLine.Args, "--lobby-probe-nochmal") >= 0)
        {
            _runde = 1;
            Rendering.NetGameRunner.EndeProbeGefeuert = false;
            GD.Print("lobby-probe: Revanche — START im selben Raum");
            l.Start();
            return;
        }
        if (stand == "lobby" && _runde == 0 && !l.IAmHost && Array.IndexOf(Core.CommandLine.Args, "--lobby-probe-nochmal") >= 0)
        { _runde = 1; Rendering.NetGameRunner.EndeProbeGefeuert = false; return; }   // Mitspieler wartet auf die Revanche
        if (stand == "lobby" || Time.GetTicksMsec() - _nachPartieT > 30000)
        {
            GD.Print("lobby-probe: Ende der Raumprobe");
            Rendering.NetGameRunner.EndeProbeGefeuert = false;
            GetTree().CreateTimer(0.5).Timeout += () => GetTree().Quit(0);
        }
    }

    private int _hinausSchritt = -1;
    private ulong _hinausT;

    private void ProbeHinaus(LobbyClient l)
    {
        if (_hinausSchritt < 0 || !l.InRoom || !l.IAmHost) return;
        if (_hinausSchritt == 0 && l.HumanRoomSlots().Count >= 2)
        {
            for (int i = 0; i < _platzInfo.Count; i++)
                if (_platzInfo[i].Art == 'M') { _members.Select(i); Hinauswerfen(); break; }
            _hinausSchritt = 1; _hinausT = Time.GetTicksMsec();
        }
        else if (_hinausSchritt == 1 && Time.GetTicksMsec() - _hinausT > 2000)
        {
            GD.Print($"lobby-probe: nach dem Hinauswerfen {l.HumanRoomSlots().Count} Menschen im Raum");
            _hinausSchritt = 2;
            GetTree().Quit(0);
        }
    }

    private void ProbeRaum(LobbyClient l)
    {
        ProbeHinaus(l);
        if (_probeSchritt >= 3 || !l.InRoom || !l.IAmHost || (_probeKi < 0 && _probeKarte.Length == 0)) return;
        if (_probeSchritt == 0)
        {
            int mi = _karten.FindIndex(k => k.File == _probeKarte);
            if (mi >= 0) { _raumKarte.Select(mi); Geaendert(); GD.Print($"lobby-probe: Karte im Raum -> {_probeKarte}"); }
            _probeSchritt = 1; _probeRaumT = Time.GetTicksMsec();
            return;
        }
        if (_probeSchritt == 1 && Time.GetTicksMsec() - _probeRaumT > 800)
        {
            int n = 0;
            if (_probeStaerke >= 0 && !P7Alt) _kiStaerke.Select(_probeStaerke);
            for (int i = 0; i < _platzInfo.Count && n < Math.Max(0, _probeKi); i++)
                if (_platzInfo[i].Art == 'F') { _members.Select(i); PlatzSetzen(true); n++; }
            _probeSchritt = 2; _probeRaumT = Time.GetTicksMsec();
            return;
        }
        if (_probeSchritt == 2 && Time.GetTicksMsec() - _probeRaumT > 1500 && _probeTeam != null && !_probeTeamGesetzt && !P7Alt)
        {
            _probeTeamGesetzt = true;
            for (int i = 0; i < _platzInfo.Count; i++)
            {
                int idx = _platzInfo[i].Art == 'I' ? 0 : _platzInfo[i].Art == 'K' ? 1 : -1;
                if (idx < 0 || idx >= _probeTeam.Length || !int.TryParse(_probeTeam[idx], out int t)) continue;
                _members.Select(i); PlatzKnoepfeZeigen(l);
                _teamWahl.Select(Math.Clamp(t, 0, 4)); TeamGewaehlt();
            }
            _probeRaumT = Time.GetTicksMsec();
            return;
        }
        if (_probeSchritt == 2 && Time.GetTicksMsec() - _probeRaumT > 1500)
        {
            var s = l.Room?["settings"] as JsonObject;
            GD.Print($"lobby-probe: Raum jetzt {s?.ToJsonString()}, Plätze {string.Join(" ", _platzInfo.ConvertAll(p => $"{p.Slot + 1}{p.Art}"))}");
            if (!P7Alt) for (int i = 0; i < _members.ItemCount; i++) GD.Print($"lobby-probe: Liste {_members.GetItemText(i)}");
            _probeSchritt = 3;
        }
    }

    private string _probeBeitritt = "";
    private int _probeRaeume = -1;
    private bool _probeBeigetreten;

    private void BeitrittProbe(LobbyClient l)
    {
        if (_probeBeitritt.Length == 0 || _probeBeigetreten || !l.Open || l.InRoom) return;
        int c = _probeBeitritt.LastIndexOf(':');
        string raum = c > 0 ? _probeBeitritt[..c] : _probeBeitritt, pw = c > 0 ? _probeBeitritt[(c + 1)..] : "";
        for (int i = 0; i < _roomIds.Count; i++)
            if (l.Rooms.TryGetValue(_roomIds[i], out var r) && (string?)r["name"] == raum)
            {
                _probeBeigetreten = true;
                _roomList.Select(i);
                _password.Text = pw;
                GD.Print($"lobby-probe: trete »{raum}« bei (Passwort »{pw}«, verschlossen: {(bool?)r["password"] == true})");
                JoinSelected();
                return;
            }
    }

    private void Shot(string what)
    {
        if (_shotPrefix == null) return;
        string p = $"{_shotPrefix}_{what}.png";
        var err = GetViewport().GetTexture().GetImage().SavePng(p);
        GD.Print($"lobby-probe: Bild {p} ({err})");
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { Close(); GetViewport().SetInputAsHandled(); }
    }

    private void Connect()
    {
        string n = _name.Text.Trim();
        if (n.Length == 0) { _login.Visible = true; _status.Text = "Bitte einen Namen eingeben."; return; }
        // ⭐ 14.11.2026 — Fable B9: die Regel des Servers schon hier pruefen, und den Namen erst
        // speichern, wenn der Server ihn angenommen hat. Bisher wurde ein ungueltiger Name
        // gespeichert, das Fenster verband beim naechsten Oeffnen von selbst damit und blendete
        // das Namensfeld aus — dauerhaft ausgesperrt, Ausweg nur settings.cfg von Hand.
        if (!B9Alt && !NameGueltig(n) && Array.IndexOf(Core.CommandLine.Args, "--lobby-probe-roh") < 0)   // Pruefschalter: Serverweg
        {
            _login.Visible = true;
            _status.Text = $"Der Name »{n}« geht nicht. {NameRegel}";
            GD.Print("lobby: " + _status.Text);
            return;
        }
        if (B9Alt) Settings.OnlineName = n; else _pendingName = n;
        if (!NetworkManager.OpenLobbyFromMenu(Settings.OnlineServer, n))
        {
            _login.Visible = true;
            _status.Text = "Verbindung gescheitert: " + NetworkManager.Fault;
            return;
        }
        _verbindeSeit = Time.GetTicksMsec();
        _login.Visible = false;
        if (_roomName.Text.Length == 0) _roomName.Text = $"Akte Europa {n}s Spiel";
    }

    private void CreateRoom()
    {
        var l = NetworkManager.Lobby;
        if (l is not { Open: true }) return;
        string name = _roomName.Text.Trim();
        if (name.Length == 0) name = $"Akte Europa {l.MyName}s Spiel";
        var settings = !LobbyAlt && EinstellungenLesen != null
            ? EinstellungenLesen()
            : new JsonObject { ["map"] = CurrentMap?.Invoke() ?? SkirmishSetup.Map };
        if (!LobbyAlt) settings["ai"] = 0;   // P6: Computergegner kommen ueber KI-Plaetze im Raum
        if (B8Alt) l.UnlistRooms();   // Fable B8: erst abbestellen, wenn der Raum da ist (Refresh)
        int groesse = LobbyAlt ? Math.Clamp(MaxHumans, 2, 8) : (int)_raumGroesse.Value;
        l.CreateRoom(name, groesse, B7Alt ? _password.Text : _newPassword.Text, settings);
    }

    private void JoinSelected()
    {
        var l = NetworkManager.Lobby;
        if (l is not { Open: true }) return;
        var sel = _roomList.GetSelectedItems();
        if (sel.Length == 0 || sel[0] >= _roomIds.Count) return;
        if (B8Alt) l.UnlistRooms();
        l.JoinRoom(_roomIds[sel[0]], _password.Text);
    }

    private void SendChat(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        NetworkManager.Lobby?.SendChat(text);
        _chatIn.Text = "";
    }

    private void Close()
    {
        // Verlässt den Server ganz — der Netzkasten ist danach wieder frei.
        if (!_handedOver) NetworkManager.Cancel("Online-Lobby verlassen");
        QueueFree();
    }

    private void UpdateJoinButton()
    {
        var sel = _roomList.GetSelectedItems();
        _join.Disabled = sel.Length == 0 || _roomList.IsItemDisabled(sel[0]);
    }

    public override void _Process(double delta)
    {
        var l = NetworkManager.Lobby;
        if (l == null) { if (!_login.Visible) { _login.Visible = true; _rooms.Visible = _room.Visible = false; } Status(); return; }

        // ⚠ Die Übergabe an den Startweg: einmal, sobald der Server die Partie beginnt.
        if (l.MatchStart != null && !_handedOver)
        {
            _handedOver = true;
            GD.Print("lobby: Partie beginnt — übergebe an den Startweg des Menüs");
            OnMatchStart?.Invoke();
            QueueFree();
            return;
        }
        if (!B9Alt && LobbyFehler(l)) return;
        if (l.Revision != _seenRev)
        {
            _seenRev = l.Revision; Refresh();
            if (_probeBeitritt.Length > 0 && l.Rooms.Count != _probeRaeume)
            { _probeRaeume = l.Rooms.Count; GD.Print($"lobby-probe: Räume in der Liste {_probeRaeume}"); }
        }
        Status();
        RaumNachPartieProbe(l);
        ProbeRaum(l);
        ProbeTick(l);
        BeitrittProbe(l);
        if (AutoConnect && _shotPrefix != null && l.Open && !_zurueckBild)
        {
            if (_zurueckT == 0) _zurueckT = Time.GetTicksMsec();
            else if (Time.GetTicksMsec() - _zurueckT > 2000)
            {
                _zurueckBild = true;
                Shot("zurueck");
                GetTree().CreateTimer(1).Timeout += () => GetTree().Quit(0);
            }
        }
    }

    private void Status()
    {
        var l = NetworkManager.Lobby;
        if (l != null && _fehlerBis > Time.GetTicksMsec()) { _status.Text = _fehlerZeile; return; }
        _status.Text = l == null
            ? (NetworkManager.Fault.Length > 0 ? NetworkManager.Fault : "Nicht verbunden. Server: " + Settings.OnlineServer)
            : l.State switch
            {
                LobbyClient.Phase.Verbinde => $"Verbinde mit {l.Url} …",
                LobbyClient.Phase.Getrennt => "Getrennt: " + l.Fault,
                // Fable B6/B12: die Fassung nennen (nur gleiche sehen einander); die »online«-Zahl
                // stammte aus der Anmeldung und wurde nie erneuert — weg damit.
                _ => LobbyAlt
                     ? $"Angemeldet als »{l.MyName}« · {l.Online} online · Server {l.ServerVersion}" + (l.Motd.Length > 0 ? $"\n{l.Motd}" : "")
                     : $"Angemeldet als »{l.MyName}« · Fassung {NetworkManager.GameVersion().Replace("/n", ", Netzprotokoll ")} — " +
                       "nur Spieler mit derselben Fassung sehen einander" + (l.Motd.Length > 0 ? $"\n{l.Motd}" : ""),
            };
    }

    private void Refresh()
    {
        var l = NetworkManager.Lobby;
        if (l == null) return;
        bool open = l.Open;
        _rooms.Visible = open && !l.InRoom;
        _room.Visible = open && l.InRoom;

        if (open && !l.InRoom && !_listing) { _listing = true; l.ListRooms(); }
        if (l.InRoom && _listing) { if (!B8Alt) l.UnlistRooms(); _listing = false; }

        // Raumliste
        string keep = SelectedRoomId();
        _roomList.Clear();
        _roomIds.Clear();
        foreach (var r in l.Rooms.Values)
        {
            string id = (string?)r["id"] ?? "";
            bool running = (string?)r["state"] == "in_game";
            int pl = (int?)r["players"] ?? 0, max = (int?)r["max"] ?? 0;
            bool full = pl >= max;
            string map = KartenName((string?)r["map"] ?? "");
            string line = $"{(string?)r["name"]}  —  {(string?)r["host"]}  ·  {map}  ·  {pl}/{max}" +
                          ((bool?)r["password"] == true ? "  · 🔒" : "") +
                          (running ? "  · läuft" : full ? "  · voll" : "");
            _roomList.AddItem(line);
            _roomIds.Add(id);
            if (running || full) _roomList.SetItemDisabled(_roomList.ItemCount - 1, true);
            if (id == keep) _roomList.Select(_roomList.ItemCount - 1);
        }
        if (_roomList.ItemCount == 0) { _roomList.AddItem("(noch keine offenen Spiele — eröffne eins)"); _roomList.SetItemDisabled(0, true); _roomIds.Add(""); }
        UpdateJoinButton();

        // Raum
        if (l.Room is { } room)
        {
            var settings = room["settings"] as JsonObject;
            _roomHead.Text = $"{(string?)room["name"]}  ·  Karte {KartenName((string?)settings?["map"] ?? "?")}";
            int keepSel = _members.GetSelectedItems() is { Length: > 0 } ms ? ms[0] : -1;
            _members.Clear();
            _platzInfo.Clear();
            _platzTeam.Clear(); _platzKi.Clear();
            int humans = 0, kis = 0, andereDaten = 0;
            // ⭐ P7 Teil 2: Raumplatz n = Startplatz n der Karte (Network/NetzAufstellung.cs)
            var plRaum = MapPreview.Startplaetze((string?)settings?["map"] ?? "");
            string Zusatz(int sl, int team) => P7Alt ? "" :
                team == SkirmishSetup.ZuschauerTeam && !SkirmishSetup.ZuschauerAlt ? "  ·  Zuschauer" :
                (sl < plRaum.Count ? $"  ·  Startplatz {plRaum[sl] + 1}" : "  ·  ⚠ kein Startplatz auf dieser Karte") +
                (team > 0 ? $"  ·  Team {team}" : "");
            if (room["slots"] is JsonArray slots)
                foreach (var s in slots)
                {
                    if (s is not JsonObject o) continue;
                    int sl = (int?)o["slot"] ?? 0;
                    int team = (int?)o["team"] ?? 0;
                    _platzTeam[sl] = team;
                    if (o["player"] != null)
                    {
                        humans++;
                        bool host = (string?)o["player"] == (string?)room["host"];
                        bool ich = (string?)o["player"] == l.MyId;
                        int ping = (int?)o["ping_ms"] ?? -1;
                        // ⭐ Fable D8: andere Spieldaten (content_hash) sichtbar machen
                        string hash = (string?)o["content_hash"] ?? "";
                        bool anders = !Inhaltssumme.Alt && hash.Length > 0 && hash != "00000000" && hash != Inhaltssumme.Wert();
                        if (anders) andereDaten++;
                        _members.AddItem($"{(LobbyAlt ? "Platz " : "")}{sl + 1}.  {(string?)o["name"]}" + (host ? "  (Gastgeber)" : "") +
                                         (ich ? "  — du" : "") + Zusatz(sl, team) + (ping >= 0 ? $"  ·  {ping} ms" : "") +
                                         (anders ? "  ·  ⚠ andere Spieldaten" : ""));
                        if (anders) _members.SetItemTooltip(_members.ItemCount - 1,
                            "Dieser Spieler hat andere eingelesene Spieldaten als du (Prüfsumme " + hash + " statt " + Inhaltssumme.Wert() +
                            ").\nDie Partie kann dann auseinanderlaufen. Abhilfe: beide lesen die Original-CDs mit derselben Fassung neu ein.");
                        _platzInfo.Add((sl, ich ? 'I' : 'M', (string?)o["player"] ?? ""));
                    }
                    else if (!LobbyAlt && (string?)o["ai"] is { Length: > 0 } ai)
                    {
                        kis++;
                        _members.AddItem($"{sl + 1}.  Computergegner ({ai})" + Zusatz(sl, team));
                        _platzInfo.Add((sl, 'K', ""));
                        _platzKi[sl] = ai;
                    }
                    else { _members.AddItem(LobbyAlt ? $"Platz {sl + 1}:  frei" : $"{sl + 1}.  frei"); _platzInfo.Add((sl, 'F', "")); }
                }
            if (keepSel >= 0 && keepSel < _members.ItemCount) _members.Select(keepSel);
            _start.Visible = l.IAmHost;
            _start.Disabled = humans < 2;
            // Fable C10: zurueck im Raum, waehrend die anderen noch spielen
            if ((string?)room["state"] == "in_game")
            {
                _start.Visible = true;
                _start.Disabled = true;
                _start.Text = "PARTIE LÄUFT NOCH — warte, bis sie zu Ende ist";
            }
            else
            _start.Text = humans < 2 ? "START (wartet auf Mitspieler)"
                        : kis > 0 ? $"START ({humans} Menschen, {kis} Computergegner)" : $"START ({humans} Menschen)";
            // P7: ohne Startplatz oder ohne Gegner kein Start — mit Grund auf dem Knopf
            if (!P7Alt && (string?)room["state"] != "in_game" && humans >= 2)
            {
                bool Zusch((int Slot, char Art, string Player) p) => p.Art is 'I' or 'M' && !SkirmishSetup.ZuschauerAlt
                                                                   && _platzTeam.GetValueOrDefault(p.Slot) == SkirmishSetup.ZuschauerTeam;
                int zuschauer = _platzInfo.Count(Zusch);
                var besetzt = _platzInfo.Where(p => p.Art != 'F' && !Zusch(p)).ToList();
                int leerePl = MapPreview.LeerePlaetze((string?)settings?["map"] ?? "").Count;
                if (zuschauer > 0) _start.Text = _start.Text.Replace(" Menschen", $" Menschen, davon {zuschauer} Zuschauer");
                int ohne = besetzt.Where(p => plRaum.Count > 0 && p.Slot >= plRaum.Count).Select(p => p.Slot).DefaultIfEmpty(-1).First();
                bool feind = besetzt.Any(a => besetzt.Any(b => a.Slot != b.Slot &&
                                 !(_platzTeam.GetValueOrDefault(a.Slot) > 0 && _platzTeam.GetValueOrDefault(a.Slot) == _platzTeam.GetValueOrDefault(b.Slot))));
                if (ohne >= 0) { _start.Disabled = true; _start.Text = $"START — Platz {ohne + 1} hat auf dieser Karte keinen Startplatz"; }
                else if (andereDaten > 0 && !_start.Disabled) _start.Text += "  ⚠ andere Spieldaten im Raum";
                else if (!feind) { _start.Disabled = true; _start.Text = "START — alle im selben Team, kein Gegner"; }
                else if (zuschauer > leerePl) { _start.Disabled = true; _start.Text = "START — diese Karte hat keinen freien Platz für Zuschauer"; }
            }
            if (!LobbyAlt) { RaumEinstellungenZeigen(l, room, settings, kis); PlatzKnoepfeZeigen(l); }
        }

        // Chat und Ereignisse
        for (; _seenChat < l.Chat.Count; _seenChat++)
        {
            var c = l.Chat[_seenChat];
            // Original: »<Name>: <Text>« (Handler 994 @0x4C4688); Fable C11: Raum und Lobby kennzeichnen
            _chat.AddText(LobbyAlt ? $"{c.Name}: {c.Text}\n" : $"{(c.InRoom ? "[Raum]" : "[Lobby]")} {c.Name}: {c.Text}\n");
        }
        for (; _seenEvents < l.Events.Count; _seenEvents++)
            _chat.AddText($"— {l.Events[_seenEvents]}\n");
    }

    private bool _listing;

    // ---- ⭐ 14.11.2026 — Fable P6 (C9/D3, D2, D4, B12): DER RAUM WIE IN KLASSISCHEN RTS --------
    // Die Einstellungen der Partie stehen im Raum, mit Kartenbild, fuer alle sichtbar; aendern kann
    // sie nur der Gastgeber (set_settings, der Server reicht das JSON weiter). Die Platzliste zeigt
    // Menschen, Computergegner und freie Plaetze; der Gastgeber setzt Computergegner auf freie Plaetze
    // (set_ai) und kann Mitspieler hinauswerfen (kick). Die Zahl der KI-Plaetze IST die Zahl der
    // Computergegner. UNSERE SETZUNG: eine Staerke fuer alle KI (die Schwierigkeit des Raums) —
    // eine eigene je KI und Teams brauchen die Simulation (P7). Gegenschalter --lobby-alt.

    private TextureRect _raumBild = null!;
    private Label _raumKarteText = null!;
    private OptionButton _raumKarte = null!, _raumLevel = null!, _raumRes = null!;
    private ZahlWahl _raumKonto = null!, _raumTech = null!, _raumTempo = null!;
    private CheckBox _raumAlle = null!;
    private Button _kiDazu = null!, _platzFrei = null!, _hinaus = null!;
    private Label _platzHinweis = null!;
    private readonly List<(int Slot, char Art, string Player)> _platzInfo = new();
    private List<(string File, string Title)> _karten = new();
    private bool _fuelle, _warHost;
    private string _zuletztGesetzt = "";

    private static readonly string[] Stufen = { "leicht", "normal", "schwer" };

    private void RaumEinstellungenBauen(VBoxContainer into)
    {
        _karten = MainMenu.KartenAuswahl();
        var zeile = new HBoxContainer();
        zeile.AddThemeConstantOverride("separation", 12);
        into.AddChild(zeile);
        var bildSpalte = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
        _raumBild = new TextureRect
        {
            CustomMinimumSize = new Vector2(220, 110),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        bildSpalte.AddChild(_raumBild);
        _raumKarteText = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.7f, 0.75f, 0.8f),
        };
        _raumKarteText.AddThemeFontSizeOverride("font_size", 13);
        bildSpalte.AddChild(_raumKarteText);
        zeile.AddChild(bildSpalte);

        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 4);
        zeile.AddChild(grid);
        void Feld(string name, Control c) { grid.AddChild(new Label { Text = name }); c.SizeFlagsHorizontal = SizeFlags.ExpandFill; grid.AddChild(c); }

        _raumKarte = new OptionButton { ClipText = true };
        foreach (var (_, title) in _karten) _raumKarte.AddItem(title);
        _raumKarte.ItemSelected += _ => Geaendert();
        Feld("Karte", _raumKarte);
        _raumLevel = new OptionButton();
        foreach (string s in new[] { "Leicht", "Normal", "Schwer" }) _raumLevel.AddItem(s);
        _raumLevel.ItemSelected += _ => Geaendert();
        Feld("Schwierigkeit", _raumLevel);
        // P7 Teil 2: die Staerke gilt je Platz (Feld »Stärke« unter der Liste) — die eine
        // Schwierigkeit des Raums hat keine Wirkung mehr und verschwindet
        if (!P7Alt) { _raumLevel.Visible = false; grid.GetChild<Control>(grid.GetChildCount() - 2).Visible = false; }
        _raumRes = new OptionButton();
        foreach (string n in Rendering.MapEntityLayer.ResourceLevelNames()) _raumRes.AddItem(n);
        _raumRes.ItemSelected += _ => Geaendert();
        Feld("Rohstoffe", _raumRes);
        _raumKonto = new ZahlWahl
        {
            Werte = new[] { 0, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000 },
            Beschriftung = v => $"$ {v}",
        };
        _raumKonto.ValueChanged += _ => Geaendert();
        Feld("Konto", _raumKonto);
        _raumTech = new ZahlWahl { MinValue = 1, MaxValue = 8, Beschriftung = v => $"Stufe {v} von 8" };
        _raumTech.ValueChanged += _ => Geaendert();
        Feld("Techstandard", _raumTech);
        _raumTempo = new ZahlWahl { Werte = new[] { 1, 2, 3 }, Value = 1, Beschriftung = SkirmishSetup.TempoName };
        _raumTempo.ValueChanged += _ => Geaendert();
        if (!SkirmishSetup.TempoAlt) Feld("Spieltempo", _raumTempo);
        _raumAlle = new CheckBox { Text = "Alle Einheiten kaufbar" };
        _raumAlle.Toggled += _ => Geaendert();
        Feld("", _raumAlle);
    }

    private void PlatzKnoepfeBauen(VBoxContainer into)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);
        _kiDazu = new Button { Text = "COMPUTERGEGNER HIER", SizeFlagsHorizontal = SizeFlags.ExpandFill,
                               TooltipText = "Setzt auf den gewählten freien Platz einen Computergegner (Stärke = Schwierigkeit)." };
        _kiDazu.Pressed += () => PlatzSetzen(true);
        _platzFrei = new Button { Text = "PLATZ FREIGEBEN", SizeFlagsHorizontal = SizeFlags.ExpandFill,
                                  TooltipText = "Nimmt den Computergegner vom gewählten Platz — dort kann wieder ein Mensch beitreten." };
        _platzFrei.Pressed += () => PlatzSetzen(false);
        _hinaus = new Button { Text = "HINAUSWERFEN", SizeFlagsHorizontal = SizeFlags.ExpandFill,
                               TooltipText = "Wirft den gewählten Mitspieler aus dem Raum." };
        _hinaus.Pressed += Hinauswerfen;
        h.AddChild(_kiDazu); h.AddChild(_platzFrei); h.AddChild(_hinaus);
        // ⭐ Fable D2/F: den eigenen Platz (= Startplatz) waehlen — move_slot war geschrieben und ungenutzt
        _wechseln = new Button { Text = "HIERHIN WECHSELN", SizeFlagsHorizontal = SizeFlags.ExpandFill, Visible = false,
                                 TooltipText = "Setzt dich auf den gewählten freien Platz — damit wählst du deinen Startplatz auf der Karte." };
        _wechseln.Pressed += () =>
        {
            if (NetworkManager.Lobby is { } lb && GewaehlterPlatz() is { Art: 'F' } f)
            { lb.MoveSlot(f.Slot); GD.Print($"lobby: wechsle auf Platz {f.Slot + 1}"); }
        };
        if (!P7Alt) h.AddChild(_wechseln);
        into.AddChild(h);
        // ⭐ P7 Teil 2: Staerke und Team des gewaehlten Platzes
        if (!P7Alt)
        {
            var h2 = new HBoxContainer();
            h2.AddThemeConstantOverride("separation", 6);
            h2.AddChild(new Label { Text = "Stärke" });
            _kiStaerke = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = "Wie stark der Computergegner auf dem gewählten Platz spielt (nur Gastgeber). " +
                              "Gilt auch für »COMPUTERGEGNER HIER«." };
            foreach (string s in new[] { "Computer leicht", "Computer normal", "Computer schwer" }) _kiStaerke.AddItem(s);
            _kiStaerke.Select(1);
            _kiStaerke.ItemSelected += _ => StaerkeGewaehlt();
            h2.AddChild(_kiStaerke);
            h2.AddChild(new Label { Text = "Team" });
            _teamWahl = new OptionButton { CustomMinimumSize = new Vector2(120, 0),
                TooltipText = "Gleiches Team = verbündet: greifen sich nicht an und sehen, was der andere sieht. " +
                              "»–« = jeder gegen jeden. Dein eigenes Team stellst du selbst ein, das der " +
                              "Computergegner der Gastgeber." };
            _teamWahl.AddItem("–");
            for (int t = 1; t <= 4; t++) _teamWahl.AddItem($"Team {t}");
            // Fable D10: Zuschauer — sieht alles, spielt nicht mit (Serverteam 8)
            if (!SkirmishSetup.ZuschauerAlt) _teamWahl.AddItem("Zuschauer");
            _teamWahl.ItemSelected += _ => TeamGewaehlt();
            h2.AddChild(_teamWahl);
            into.AddChild(h2);
        }
        _platzHinweis = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.6f, 0.64f, 0.7f) };
        _platzHinweis.AddThemeFontSizeOverride("font_size", 13);
        into.AddChild(_platzHinweis);
        _members.ItemSelected += _ => { if (NetworkManager.Lobby is { } l) PlatzKnoepfeZeigen(l); };
    }

    private (int Slot, char Art, string Player)? GewaehlterPlatz()
    {
        var sel = _members.GetSelectedItems();
        return sel.Length > 0 && sel[0] < _platzInfo.Count ? _platzInfo[sel[0]] : null;
    }

    private void PlatzKnoepfeZeigen(LobbyClient l)
    {
        bool host = l.IAmHost;
        _kiDazu.Visible = _platzFrei.Visible = _hinaus.Visible = host;
        var p = GewaehlterPlatz();
        _kiDazu.Disabled = p is not { Art: 'F' };
        _platzFrei.Disabled = p is not { Art: 'K' };
        _hinaus.Disabled = p is not { Art: 'M' };
        if (!P7Alt) { _wechseln.Visible = true; _wechseln.Disabled = p is not { Art: 'F' }; }
        if (P7Alt)
        {
            _platzHinweis.Text = host
                ? "Eintrag anklicken: auf freie Plätze Computergegner setzen, Mitspieler hinauswerfen. Die Startplätze auf der Karte verteilt das Spiel beim Start."
                : "Die Einstellungen und Plätze bestimmt der Gastgeber. Die Startplätze auf der Karte verteilt das Spiel beim Start.";
            return;
        }
        _plWaehlen = true;
        if (p is { } q)
        {
            if (_platzKi.TryGetValue(q.Slot, out var ai)) _kiStaerke.Select(Math.Max(0, Array.IndexOf(Stufen, ai)));
            int tq = _platzTeam.GetValueOrDefault(q.Slot);
            _teamWahl.Select(tq == SkirmishSetup.ZuschauerTeam && _teamWahl.ItemCount > 5 ? 5 : Math.Clamp(tq, 0, 4));
        }
        _plWaehlen = false;
        _kiStaerke.Disabled = !host || p is not { Art: 'K' or 'F' };
        _teamWahl.Disabled = p is not { Art: 'I' } && !(host && p is { Art: 'K' });
        _platzHinweis.Text = (host
            ? "Eintrag anklicken: Computergegner setzen (mit Stärke), Teams festlegen, Mitspieler hinauswerfen. "
            : "Dein Team stellst du selbst ein (deinen Eintrag anklicken); den Rest bestimmt der Gastgeber. ") +
            "Der Platz in der Liste ist der Startplatz auf der Karte — »HIERHIN WECHSELN« setzt dich auf einen freien.";
    }

    private void PlatzSetzen(bool ki)
    {
        if (NetworkManager.Lobby is not { IAmHost: true } l || GewaehlterPlatz() is not { } p) return;
        string staerke = Stufen[Math.Clamp(P7Alt ? _raumLevel.Selected : _kiStaerke.Selected, 0, 2)];
        l.SetAi(p.Slot, ki ? staerke : null, P7Alt || !ki ? 0 : _platzTeam.GetValueOrDefault(p.Slot), p.Slot);
        GD.Print($"lobby: Platz {p.Slot + 1} {(ki ? "Computergegner (" + staerke + ")" : "frei")}");
    }

    // ---- ⭐ P7 Teil 2 (14.11.2026): Staerke und Team je Platz — Gegenschalter --gefecht-plaetze-alt
    private static bool P7Alt => SkirmishSetup.PlaetzeAlt || LobbyAlt;
    private OptionButton _kiStaerke = null!, _teamWahl = null!;
    private Button _wechseln = null!;
    private readonly Dictionary<int, int> _platzTeam = new();
    private readonly Dictionary<int, string> _platzKi = new();
    private bool _plWaehlen;

    private void StaerkeGewaehlt()
    {
        if (_plWaehlen || NetworkManager.Lobby is not { IAmHost: true } l || GewaehlterPlatz() is not { Art: 'K' } p) return;
        string staerke = Stufen[Math.Clamp(_kiStaerke.Selected, 0, 2)];
        l.SetAi(p.Slot, staerke, _platzTeam.GetValueOrDefault(p.Slot), p.Slot);
        GD.Print($"lobby: Platz {p.Slot + 1} Computergegner {staerke}");
    }

    private void TeamGewaehlt()
    {
        if (_plWaehlen || NetworkManager.Lobby is not { } l || GewaehlterPlatz() is not { } p) return;
        int team = _teamWahl.Selected == 5 ? SkirmishSetup.ZuschauerTeam : _teamWahl.Selected;
        if (team == SkirmishSetup.ZuschauerTeam && p.Art != 'I') { PlatzKnoepfeZeigen(l); return; }   // nur Menschen schauen zu
        if (p.Art == 'I') l.SetSlot(team, p.Slot);
        else if (p.Art == 'K' && l.IAmHost && _platzKi.TryGetValue(p.Slot, out var ai)) l.SetAi(p.Slot, ai, team, p.Slot);
        else return;
        GD.Print($"lobby: Platz {p.Slot + 1} Team {team}");
    }

    private void Hinauswerfen()
    {
        if (NetworkManager.Lobby is not { IAmHost: true } l || GewaehlterPlatz() is not { Art: 'M' } p) return;
        l.Kick(p.Player);
        GD.Print($"lobby: Platz {p.Slot + 1} hinausgeworfen");
    }

    /// <summary>Raumeinstellungen in die Felder; der Gastgeber darf aendern, die anderen sehen nur.</summary>
    private void RaumEinstellungenZeigen(LobbyClient l, JsonObject room, JsonObject? s, int kis)
    {
        bool host = l.IAmHost;
        // B12: wird man Gastgeber (der alte ist gegangen), uebernimmt man die Einstellungen des RAUMS —
        // sonst spielte man die Karte, die im eigenen Schirm stand, waehrend der Raumkopf eine andere zeigte.
        if (host && !_warHost && s != null && EinstellungenSetzen != null)
        {
            var kopie = (JsonObject)s.DeepClone();
            kopie["ai"] = kis;
            EinstellungenSetzen(kopie);
            GD.Print("lobby: du bist Gastgeber — Raumeinstellungen übernommen");
        }
        _warHost = host;
        _fuelle = true;
        if (s != null)
        {
            string map = (string?)s["map"] ?? "";
            int mi = _karten.FindIndex(k => k.File == map);
            if (mi >= 0) _raumKarte.Select(mi);
            _raumBild.Texture = MapPreview.Of(map);
            var pl = MapPreview.Startplaetze(map);
            var (w, h) = MapPreview.Groesse(map);
            _raumKarteText.Text = (mi >= 0 ? "" : KartenName(map) + "\n") +
                                  (pl.Count > 0 ? $"{pl.Count} Startplätze" : "") + (w > 0 ? $" · {w}×{h}" : "");
            if ((int?)s["level"] is int lv) _raumLevel.Select(Math.Clamp(lv, 0, 2));
            if ((int?)s["res"] is int rs && rs >= 0 && rs < _raumRes.ItemCount) _raumRes.Select(rs);
            if ((int?)s["money"] is int mo) _raumKonto.Value = mo;
            if ((int?)s["tech"] is int te) _raumTech.Value = te;
            if ((bool?)s["all"] is bool al) _raumAlle.ButtonPressed = al;
            _raumTempo.Value = (int?)s["tempo"] ?? 1;
        }
        foreach (var c in new Control[] { _raumKarte, _raumLevel, _raumRes, _raumKonto, _raumTech, _raumTempo })
            if (c is BaseButton b) b.Disabled = !host;
        _raumAlle.Disabled = !host;
        _fuelle = false;
        // die Zahl der Computergegner folgt den KI-Plaetzen — beim Gastgeber in den Schirm
        if (host && EinstellungenSetzen != null && s != null && (int?)s["ai"] != kis)
        {
            var neu = (JsonObject)s.DeepClone();
            neu["ai"] = kis;
            Senden(l, neu);
        }
    }

    /// <summary>Ein Feld im Raum hat sich geaendert (nur Gastgeber): in den Schirm und an alle.</summary>
    private void Geaendert()
    {
        if (_fuelle || NetworkManager.Lobby is not { IAmHost: true } l) return;
        int mi = _raumKarte.Selected;
        int kis = 0;
        foreach (var p in _platzInfo) if (p.Art == 'K') kis++;
        var s = new JsonObject
        {
            ["map"] = mi >= 0 && mi < _karten.Count ? _karten[mi].File : SkirmishSetup.Map,
            ["ai"] = kis,
            ["level"] = _raumLevel.Selected,
            ["res"] = _raumRes.Selected,
            ["money"] = (int)_raumKonto.Value,
            ["tech"] = (int)_raumTech.Value,
            ["all"] = _raumAlle.ButtonPressed,
            ["tempo"] = (int)_raumTempo.Value,
        };
        Senden(l, s);
    }

    private void Senden(LobbyClient l, JsonObject s)
    {
        string text = s.ToJsonString();
        if (text == _zuletztGesetzt) return;
        _zuletztGesetzt = text;
        EinstellungenSetzen?.Invoke((JsonObject)s.DeepClone());
        l.SetSettings(s);
        GD.Print("lobby: Raumeinstellungen gesetzt " + text);
    }

    // ---- ⭐ 14.11.2026 — Fable P4 (B7, B8, B9, C8, C11) — Gegenschalter --lobby-alt -----------
    // ⭐ Fable D10: Discord in der Lobby — Gegenschalter --lobby-discord-alt
    private static bool DiscordAlt => Array.IndexOf(Core.CommandLine.Args, "--lobby-discord-alt") >= 0;

    private static Button DiscordKnopf(string text)
    {
        var b = new Button
        {
            Text = text,
            TooltipText = "Öffnet den Discord-Server von Akte Europa Reborn im Browser — dort findest du Mitspieler " +
                          "und kannst sie in deinen Raum einladen (Raumname nennen).",
        };
        b.Pressed += () => OS.ShellOpen(MainMenu.DiscordEinladung);
        return b;
    }

    private static bool LobbyAlt => Array.IndexOf(Core.CommandLine.Args, "--lobby-alt") >= 0;
    private static bool B7Alt => LobbyAlt;
    private static bool B8Alt => LobbyAlt;
    private static bool B9Alt => LobbyAlt;

    /// <summary>Die Regel des Servers (gemessen, Fable B9): 1 bis 20 Zeichen, Buchstaben (auch
    /// Umlaute), Ziffern, Leerzeichen und - _ .</summary>
    public const string NameRegel = "Erlaubt: 1 bis 20 Zeichen — Buchstaben (auch Umlaute), Ziffern, Leerzeichen und - _ .";

    public static bool NameGueltig(string n)
    {
        if (n.Length is < 1 or > 20 || n.Trim().Length == 0) return false;
        foreach (char ch in n)
            if (!(char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '.')) return false;
        return true;
    }

    /// <summary>Fehler des Servers und Trennung behandeln. true: das Fenster ist auf die Anmeldung
    /// zurueckgefallen (Leitung zu, Namensfeld mit Grund).</summary>
    private bool LobbyFehler(LobbyClient l)
    {
        if (l.State == LobbyClient.Phase.Angemeldet && _pendingName.Length > 0)
        {
            Settings.OnlineName = _pendingName;   // erst jetzt speichern
            GD.Print($"lobby: Name »{_pendingName}« vom Server angenommen und gespeichert");
            _pendingName = "";
        }
        if (l.ErrorCount != _seenErrors)
        {
            _seenErrors = l.ErrorCount;
            switch (l.LastError)
            {
                case "name":
                    ZurAnmeldung($"Der Server nimmt den Namen »{_name.Text.Trim()}« nicht an — {l.LastErrorText}");
                    return true;
                case "kicked":
                    l.LeaveRoom();
                    _listing = false;
                    Refresh();
                    _fehlerZeile = "Der Gastgeber hat dich aus dem Raum genommen.";
                    _fehlerBis = Time.GetTicksMsec() + 8000;
                    return false;
                case "proto":
                    ZurAnmeldung("Der Server spricht ein anderes Protokoll — bitte die neueste Fassung des Spiels holen. " + l.LastErrorText);
                    return true;
                default:
                    // B8: nach einem gescheiterten Beitritt/Eroeffnen (bad_password, room_full,
                    // no_such_room …) die Raumliste wieder abonnieren und den Grund oben zeigen
                    if (!l.InRoom) { _listing = false; Refresh(); }
                    _fehlerZeile = l.LastErrorText.Length > 0 ? l.LastErrorText : "Das hat nicht geklappt.";   // C7: ohne Fehlercode
                    GD.Print($"lobby: Fehler {l.LastError}: {l.LastErrorText}");
                    _fehlerBis = Time.GetTicksMsec() + 8000;
                    break;
            }
        }
        if (l.State == LobbyClient.Phase.Getrennt)
        {
            ZurAnmeldung("Die Verbindung zum Server ist getrennt: " + l.Fault + " — »VERBINDEN« versucht es noch einmal.");
            return true;
        }
        if (l.State == LobbyClient.Phase.Verbinde && _verbindeSeit != 0 && Time.GetTicksMsec() - _verbindeSeit > 10000)
        {
            ZurAnmeldung($"Der Server ({Settings.OnlineServer}) antwortet nicht. Internetverbindung prüfen, dann »VERBINDEN«.");
            return true;
        }
        return false;
    }

    private string _fehlerZeile = "";
    private ulong _fehlerBis;

    private void ZurAnmeldung(string grund)
    {
        NetworkManager.Cancel(grund);
        _pendingName = "";
        _verbindeSeit = 0;
        _listing = false;
        _login.Visible = true;
        _rooms.Visible = _room.Visible = false;
        _status.Text = grund;
        _chat.AddText($"— {grund}\n");
        GD.Print("lobby: zurück zur Anmeldung — " + grund);
    }

    private string SelectedRoomId()
    {
        var sel = _roomList.GetSelectedItems();
        return sel.Length > 0 && sel[0] < _roomIds.Count ? _roomIds[sel[0]] : "";
    }

    private static string KartenName(string map) => MainMenu.KartenTitel(map);   // Fable C14

    private static Label Head(string t)
    {
        var l = new Label { Text = t, Modulate = new Color(0.75f, 0.8f, 0.86f) };
        l.AddThemeFontSizeOverride("font_size", 22);
        return l;
    }

    private static Label Sub(string t)
    {
        var l = new Label { Text = t, Modulate = new Color(0.75f, 0.8f, 0.86f) };
        l.AddThemeFontSizeOverride("font_size", 17);
        return l;
    }

    private static HBoxContainer Row(string label, Control c)
    {
        var h = new HBoxContainer();
        h.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(140, 0) });
        c.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        h.AddChild(c);
        return h;
    }
}
