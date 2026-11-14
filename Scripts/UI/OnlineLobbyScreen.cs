#nullable enable
namespace AkteEuropaReborn.UI;

using System;
using System.Collections.Generic;
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
        _name = new LineEdit { Text = Settings.OnlineName, MaxLength = 20, PlaceholderText = "dein Name" };
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
        _roomList = new ItemList { CustomMinimumSize = new Vector2(0, 170), AllowReselect = true };
        _roomList.ItemSelected += _ => UpdateJoinButton();
        _roomList.ItemActivated += _ => JoinSelected();
        _rooms.AddChild(_roomList);
        _password = new LineEdit { PlaceholderText = "Passwort zum Beitreten (nur für Räume mit 🔒)", Secret = true };
        _rooms.AddChild(_password);
        _join = new Button { Text = "BEITRETEN", Disabled = true };
        _join.Pressed += JoinSelected;
        _rooms.AddChild(_join);
        _rooms.AddChild(new HSeparator());
        _rooms.AddChild(Sub("Eigenes Spiel eröffnen"));
        _roomName = new LineEdit { MaxLength = 40 };
        _rooms.AddChild(Row("Raumname", _roomName));
        // ⭐ 14.11.2026 — Fable B7: eigenes Feld. Bisher wurde das Beitrittsfeld still zum Passwort
        // des eigenen Raums, und wer absichtlich einen verschlossenen Raum wollte, fand kein Feld.
        _newPassword = new LineEdit { PlaceholderText = "leer = offen für alle", Secret = true, MaxLength = 40 };
        _rooms.AddChild(Row("Passwort", _newPassword));
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
            Text = "Karte, Techstandard, Konto und Rohstoffe stellst du VORHER im Gefechtsschirm ein; " +
                   "die Zahl der Menschen steht im Netzkasten (Feld »Menschen«).",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(0.6f, 0.64f, 0.7f),
        });
        box.AddChild(_rooms);

        // ---- 3. im Raum -------------------------------------------------------
        _room = new VBoxContainer { Visible = false };
        _room.AddThemeConstantOverride("separation", 6);
        _roomHead = Sub("");
        _room.AddChild(_roomHead);
        _members = new ItemList { CustomMinimumSize = new Vector2(0, 130) };
        _room.AddChild(_members);
        var rb = new HBoxContainer();
        _start = new Button { Text = "START", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 36) };
        _start.Pressed += () => NetworkManager.Lobby?.Start();
        var leave = new Button { Text = "RAUM VERLASSEN", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        leave.Pressed += () => { NetworkManager.Lobby?.LeaveRoom(); NetworkManager.Lobby?.ListRooms(); };
        rb.AddChild(_start);
        rb.AddChild(leave);
        _room.AddChild(rb);
        box.AddChild(_room);

        // ---- Chat (Lobby und Raum) ---------------------------------------------
        box.AddChild(new HSeparator());
        _chat = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(0, 120),
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
        if (NetworkManager.Lobby == null && Settings.OnlineName.Length > 0)
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
            case 2 when l.InRoom && l.HumanRoomSlots().Count >= 2:
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
        var settings = new JsonObject { ["map"] = CurrentMap?.Invoke() ?? SkirmishSetup.Map };
        if (B8Alt) l.UnlistRooms();   // Fable B8: erst abbestellen, wenn der Raum da ist (Refresh)
        l.CreateRoom(name, Math.Clamp(MaxHumans, 2, 8), B7Alt ? _password.Text : _newPassword.Text, settings);
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
            _members.Clear();
            int humans = 0;
            if (room["slots"] is JsonArray slots)
                foreach (var s in slots)
                {
                    if (s is not JsonObject o) continue;
                    int sl = (int?)o["slot"] ?? 0;
                    if (o["player"] != null)
                    {
                        humans++;
                        bool host = (string?)o["player"] == (string?)room["host"];
                        int ping = (int?)o["ping_ms"] ?? -1;
                        _members.AddItem($"Platz {sl + 1}:  {(string?)o["name"]}" + (host ? "  (Gastgeber)" : "") +
                                         (ping >= 0 ? $"  ·  {ping} ms" : ""));
                    }
                    else _members.AddItem($"Platz {sl + 1}:  frei");
                }
            _start.Visible = l.IAmHost;
            _start.Disabled = humans < 2;
            _start.Text = humans < 2 ? "START (wartet auf Mitspieler)" : $"START ({humans} Menschen)";
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

    // ---- ⭐ 14.11.2026 — Fable P4 (B7, B8, B9, C8, C11) — Gegenschalter --lobby-alt -----------
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
                case "proto":
                    ZurAnmeldung("Der Server spricht ein anderes Protokoll — bitte die neueste Fassung des Spiels holen. " + l.LastErrorText);
                    return true;
                default:
                    // B8: nach einem gescheiterten Beitritt/Eroeffnen (bad_password, room_full,
                    // no_such_room …) die Raumliste wieder abonnieren und den Grund oben zeigen
                    if (!l.InRoom) { _listing = false; Refresh(); }
                    _fehlerZeile = $"Der Server sagt: {l.LastErrorText} ({l.LastError})";
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
