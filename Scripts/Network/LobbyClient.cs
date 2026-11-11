#nullable enable
namespace AkteEuropaReborn.Network;

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;

/// <summary>
/// ⭐ 11.11.2026 — <b>DER DRAHT ZUM LOBBY-SERVER</b> (Online-Mehrspieler, Stufe 1).
///
/// <para>Spricht das Protokoll aus <c>docs/LOBBY_SERVER_AUFTRAG.md</c> (Fassung
/// <see cref="Proto"/>) mit dem Server unter <see cref="StandardUrl"/>: Text =
/// JSON-Nachrichten (Anmeldung, Räume, Chat, Start), Binär = die Spielpakete, die
/// der Server mit einer laufenden Nummer versehen an ALLE im Raum zurückschickt.</para>
///
/// <para>⚠ <b>UNSERE ZUTAT, vollständig.</b> Das Original kennt nur DirectPlay 1
/// (<c>berichte/mehrspieler-original-fable.md</c> §1); ein Server im Internet ersetzt
/// die Sitzungssuche (Vorlagen 129/130) und das Weiterreichen durch den Gastgeber.
/// Was darüber läuft — Lockstep, Taktpakete, Sitzung — bleibt unverändert
/// <see cref="NetLink"/>.</para>
///
/// <para><b>Faden:</b> keiner. <see cref="Poll"/> läuft auf Bildzeit aus
/// <see cref="NetworkManager"/>; die Simulation sieht davon nur, was
/// <see cref="NetLink.Pump"/> am Taktanfang aus <see cref="Frames"/> nimmt.</para>
/// </summary>
public sealed class LobbyClient
{
    public const string StandardUrl = "wss://ae.krynexlabs.de/ae/v1";
    public const int Proto = 1;

    public enum Phase { Aus, Verbinde, Angemeldet, Getrennt }

    private readonly WebSocketPeer _ws = new();
    private bool _helloSent;
    private int _rid;

    public string Url { get; private set; } = StandardUrl;
    public Phase State { get; private set; } = Phase.Aus;
    public string Fault { get; private set; } = "";

    // ---- aus »welcome« --------------------------------------------------------
    public string MyId { get; private set; } = "";
    public string MyName { get; private set; } = "";
    public string ResumeToken { get; private set; } = "";
    public string Motd { get; private set; } = "";
    public string ServerVersion { get; private set; } = "";
    public int Online { get; private set; }

    // ---- die Lobby ------------------------------------------------------------
    /// <summary>Die offenen Räume (Abo über <see cref="ListRooms"/>), nach id.</summary>
    public readonly Dictionary<string, JsonObject> Rooms = new();
    /// <summary>Der eigene Raum, wie ihn das letzte <c>room_state</c> zeigte; null = in keinem.</summary>
    public JsonObject? Room { get; private set; }
    /// <summary>Zählt jede Änderung an Räumen/Raum — die Oberfläche zeichnet neu, wenn er springt.</summary>
    public int Revision { get; private set; }

    public readonly List<(string Name, string Text, bool InRoom)> Chat = new();
    /// <summary>Server-Ereignisse in Klartext (Fehler, Spieler weg …) für Protokoll und Anzeige.</summary>
    public readonly List<string> Events = new();
    /// <summary>Der letzte Fehlercode einer Antwort (<c>room_full</c>, <c>not_host</c> …).</summary>
    public string LastError { get; private set; } = "";

    // ---- die Partie -----------------------------------------------------------
    public JsonObject? MatchStart { get; private set; }
    public string MatchId { get; private set; } = "";
    public uint Seed { get; private set; }
    public int YouSlot { get; private set; } = -1;
    /// <summary>Der Server hat »go« geschickt: ab jetzt dürfen Binärrahmen fliessen.</summary>
    public bool Go { get; private set; }
    public JsonObject? MatchOver { get; private set; }

    /// <summary>Die eingegangenen Spielrahmen in Serverreihenfolge.</summary>
    public readonly Queue<(uint Seq, int Slot, uint Ms, byte[] Data)> Frames = new();
    public uint LastSeq { get; private set; }
    public long FramesIn, FramesOut, BytesIn, BytesOut;

    // ================= Aufbauen =============================================

    public bool Connect(string url, string name, string gameVersion, string contentHash)
    {
        Url = string.IsNullOrWhiteSpace(url) ? StandardUrl : url;
        _ws.InboundBufferSize = 1 << 20;      // ⚠ ein resync kann viele Rahmen am Stück bringen
        _ws.OutboundBufferSize = 1 << 20;
        _ws.MaxQueuedPackets = 8192;
        Error e = _ws.ConnectToUrl(Url);
        if (e != Error.Ok) { Fault = $"Verbindung zu {Url} -> {e}"; State = Phase.Getrennt; return false; }
        State = Phase.Verbinde;
        _hello = new JsonObject
        {
            ["t"] = "hello", ["proto"] = Proto, ["name"] = name,
            ["game_version"] = gameVersion, ["content_hash"] = contentHash,
            ["resume_token"] = null,
        };
        GD.Print($"lobby: verbinde {Url} als »{name}« ({gameVersion})");
        return true;
    }

    private JsonObject? _hello;

    public void Close()
    {
        if (State is Phase.Verbinde or Phase.Angemeldet) _ws.Close(1000, "bye");
        State = Phase.Getrennt;
    }

    public bool Open => State == Phase.Angemeldet && _ws.GetReadyState() == WebSocketPeer.State.Open;

    // ================= Senden ===============================================

    private void Send(JsonObject o)
    {
        if (_ws.GetReadyState() != WebSocketPeer.State.Open) return;
        string s = o.ToJsonString();
        _ws.SendText(s);
        FramesOut++; BytesOut += s.Length;
    }

    private int Rid() => ++_rid;

    public void ListRooms() => Send(new JsonObject { ["t"] = "list_rooms" });
    public void UnlistRooms() => Send(new JsonObject { ["t"] = "unlist_rooms" });

    public void CreateRoom(string name, int max, string password, JsonObject settings)
        => Send(new JsonObject
        {
            ["t"] = "create_room", ["rid"] = Rid(), ["name"] = name, ["max"] = max,
            ["password"] = password, ["require_ready"] = false,   // Original: kein Bereit-Zustand
            ["settings"] = settings,
        });

    public void JoinRoom(string id, string password = "")
        => Send(new JsonObject { ["t"] = "join_room", ["rid"] = Rid(), ["id"] = id, ["password"] = password });

    public void LeaveRoom() { Send(new JsonObject { ["t"] = "leave_room" }); Room = null; Revision++; }
    public void SetSettings(JsonObject settings) => Send(new JsonObject { ["t"] = "set_settings", ["settings"] = settings });
    public void SetSlot(int team, int color) => Send(new JsonObject { ["t"] = "set_slot", ["team"] = team, ["color"] = color });
    public void MoveSlot(int to) => Send(new JsonObject { ["t"] = "move_slot", ["to"] = to });
    public void SetAi(int slot, string? ai, int team, int color)
        => Send(new JsonObject { ["t"] = "set_ai", ["slot"] = slot, ["ai"] = ai, ["team"] = team, ["color"] = color });
    public void Kick(string player) => Send(new JsonObject { ["t"] = "kick", ["player"] = player });
    public void SendChat(string text) => Send(new JsonObject { ["t"] = "chat", ["text"] = text });
    public void Start() => Send(new JsonObject { ["t"] = "start", ["rid"] = Rid() });
    public void Loaded() => Send(new JsonObject { ["t"] = "loaded" });
    public void Surrender() => Send(new JsonObject { ["t"] = "surrender" });
    public void MatchEnd(int winnerTeam, int durationS)
        => Send(new JsonObject
        {
            ["t"] = "match_end",
            ["result"] = new JsonObject { ["winner_team"] = winnerTeam, ["duration_s"] = durationS },
        });

    /// <summary>Ein Spielpaket an alle im Raum (über den Server, der es nummeriert).</summary>
    public bool SendBinary(byte[] payload)
    {
        if (!Go || _ws.GetReadyState() != WebSocketPeer.State.Open) return false;
        if (payload.Length == 0 || payload.Length > 4096)
        {
            GD.PrintErr($"lobby: Spielpaket mit {payload.Length} B passt nicht in einen Rahmen (1..4096)");
            return false;
        }
        if (_ws.Send(payload, WebSocketPeer.WriteMode.Binary) != Error.Ok) return false;
        FramesOut++; BytesOut += payload.Length;
        return true;
    }

    // ================= Abfragen =============================================

    public void Poll()
    {
        if (State is Phase.Aus or Phase.Getrennt) return;
        _ws.Poll();
        var st = _ws.GetReadyState();
        if (st == WebSocketPeer.State.Open && !_helloSent && _hello != null)
        {
            _helloSent = true;
            Send(_hello);
        }
        while (_ws.GetAvailablePacketCount() > 0)
        {
            byte[] b = _ws.GetPacket();
            bool text = _ws.WasStringPacket();
            BytesIn += b.Length;
            FramesIn++;
            if (text) OnText(System.Text.Encoding.UTF8.GetString(b));
            else OnBinary(b);
        }
        if (st == WebSocketPeer.State.Closed && State != Phase.Getrennt)
        {
            if (Fault.Length == 0)
                Fault = $"Verbindung zum Server getrennt ({_ws.GetCloseCode()} {_ws.GetCloseReason()})".Trim();
            State = Phase.Getrennt;
            GD.PrintErr("lobby: " + Fault);
        }
    }

    private void OnBinary(byte[] b)
    {
        if (b.Length < 9) return;
        uint seq = BitConverter.ToUInt32(b, 0);
        int slot = b[4];
        uint ms = BitConverter.ToUInt32(b, 5);
        if (LastSeq != 0 && seq != LastSeq + 1)
            Events.Add($"Lücke in der Reihenfolge: nach {LastSeq} kam {seq}");
        LastSeq = seq;
        var data = new byte[b.Length - 9];
        Buffer.BlockCopy(b, 9, data, 0, data.Length);
        Frames.Enqueue((seq, slot, ms, data));
    }

    private void OnText(string s)
    {
        JsonObject? m;
        try { m = JsonNode.Parse(s) as JsonObject; }
        catch (Exception) { Events.Add("unlesbare Nachricht vom Server"); return; }
        if (m == null) return;
        string t = (string?)m["t"] ?? "";
        switch (t)
        {
            case "welcome":
                MyId = (string?)m["you"]?["id"] ?? "";
                MyName = (string?)m["you"]?["name"] ?? "";
                ResumeToken = (string?)m["resume_token"] ?? "";
                Motd = (string?)m["motd"] ?? "";
                ServerVersion = (string?)m["server_version"] ?? "";
                Online = (int?)m["online"] ?? 0;
                State = Phase.Angemeldet;
                Revision++;                    // bug-763: sonst zeichnet das Lobbyfenster nach der Anmeldung nicht neu
                GD.Print($"lobby: angemeldet als »{MyName}« ({MyId}), Server {ServerVersion}, {Online} online");
                break;
            case "room_list":
                Rooms.Clear();
                if (m["rooms"] is JsonArray arr)
                    foreach (var r in arr) if (r is JsonObject o) Rooms[(string?)o["id"] ?? ""] = (JsonObject)o.DeepClone();
                Revision++;
                break;
            case "room_added":
            case "room_changed":
                if (m["room"] is JsonObject ro) Rooms[(string?)ro["id"] ?? ""] = (JsonObject)ro.DeepClone();
                Revision++;
                break;
            case "room_removed":
                Rooms.Remove((string?)m["id"] ?? "");
                Revision++;
                break;
            case "room_state":
                Room = m["room"] is JsonObject rs ? (JsonObject)rs.DeepClone() : null;
                Revision++;
                break;
            case "chat":
                Chat.Add(((string?)m["name"] ?? "?", (string?)m["text"] ?? "", (bool?)m["room"] ?? false));
                Revision++;
                break;
            case "match_start":
                MatchStart = (JsonObject)m.DeepClone();
                MatchId = (string?)m["match"] ?? "";
                Seed = (uint?)m["seed"] ?? 0;
                YouSlot = (int?)m["you_slot"] ?? -1;
                if (m["room"] is JsonObject mr) Room = (JsonObject)mr.DeepClone();
                Go = false; MatchOver = null; LastSeq = 0; Frames.Clear();
                Revision++;
                GD.Print($"lobby: Partie {MatchId} beginnt, Keim {Seed}, mein Platz {YouSlot}");
                break;
            case "go":
                Go = true;
                Revision++;
                GD.Print("lobby: go — Spielpakete frei");
                break;
            case "player_lost":
                Events.Add($"{NameOf((string?)m["player"])} hat die Verbindung verloren — Platz wird {(int?)m["hold_s"] ?? 60} s gehalten");
                Revision++;
                break;
            case "player_left":
                Events.Add($"{NameOf((string?)m["player"])} ist weg ({(string?)m["reason"] ?? "?"})");
                Revision++;
                break;
            case "match_over":
                MatchOver = (JsonObject)m.DeepClone();
                Go = false;
                Revision++;
                GD.Print("lobby: Partie vorbei — " + m.ToJsonString());
                break;
            case "pong":
                break;
            case "error":
                LastError = (string?)m["code"] ?? "";
                Events.Add($"Server: {LastError} — {(string?)m["msg"] ?? ""}");
                if (LastError == "proto") Fault = "Server spricht ein anderes Protokoll: " + ((string?)m["msg"] ?? "");
                Revision++;
                GD.PrintErr($"lobby: Fehler {LastError}: {(string?)m["msg"]}");
                break;
            default:
                break;                          // unbekannt = ignorieren (vorwärtsverträglich)
        }
    }

    // ================= Auskünfte ============================================

    public bool InRoom => Room != null;
    public string RoomId => (string?)Room?["id"] ?? "";
    public bool IAmHost => Room != null && (string?)Room["host"] == MyId;

    private string NameOf(string? id)
    {
        if (id != null && Room?["slots"] is JsonArray a)
            foreach (var s in a)
                if (s is JsonObject o && (string?)o["player"] == id) return (string?)o["name"] ?? id;
        return id ?? "?";
    }

    /// <summary>
    /// DIE SPIELERNUMMERN DER PARTIE aus dem Raum bei <c>match_start</c> —
    /// <b>auf jeder Maschine dieselbe Rechnung aus denselben Daten</b>: Spieler 0
    /// ist der Gastgeber, die weiteren Menschen folgen nach aufsteigendem
    /// Raumplatz. Gibt die Raumplätze der Menschen in Spielerreihenfolge zurück.
    /// <para>⚠ UNSERE SETZUNG: die Reihenfolge der ENet-Fassung (Vermittler = 0,
    /// dann nach Anschlussnummer) übertragen; das Original vergibt Platz 0 dem
    /// Gastgeber und die Gäste in Beitrittsfolge (§2.3).</para>
    /// </summary>
    public List<int> HumanRoomSlots()
    {
        var res = new List<int>();
        if (Room?["slots"] is not JsonArray a) return res;
        string host = (string?)Room["host"] ?? "";
        int hostSlot = -1;
        foreach (var s in a)
        {
            if (s is not JsonObject o || o["player"] == null) continue;
            int sl = (int?)o["slot"] ?? -1;
            if ((string?)o["player"] == host) hostSlot = sl; else res.Add(sl);
        }
        res.Sort();
        if (hostSlot >= 0) res.Insert(0, hostSlot);
        return res;
    }
}
