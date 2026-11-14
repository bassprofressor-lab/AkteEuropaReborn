namespace AkteEuropaReborn.Network;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// DIE LEITUNG — ein <see cref="ENetMultiplayerPeer"/>, roh benutzt.
///
/// <para><b>⚠ Kein Godot-Hochsprachen-Mehrspieler.</b> Der Peer wird
/// <b>nicht</b> an <c>Multiplayer.MultiplayerPeer</c> gehängt, es gibt keine
/// <c>[Rpc]</c>-Methoden und keine <c>MultiplayerSynchronizer</c>. Gründe, in
/// dieser Reihenfolge:</para>
/// <list type="number">
///   <item><b>Wir schicken Zustand nicht, wir schicken Befehle.</b> Godots
///   Hochsprachenschicht ist für Zustandsabgleich gebaut; Lockstep braucht das
///   Gegenteil — ein Byteblock, der auf jeder Maschine dieselbe Rechnung
///   auslöst. Das Original schiebt <c>236</c> Byte in
///   <c>IDirectPlay::Send</c> (@0x4046EF), und genau diese 236 Byte gehen hier
///   über die Leitung.</item>
///   <item><b>Der Zeitpunkt muss uns gehören.</b> Ein an den Szenenbaum
///   gehängter Peer wird von der Hauptschleife abgefragt, also auf BILDZEIT.
///   Wann ein Satz in den Ring kommt, entscheidet bei uns der TAKT (Regel 8) —
///   das Abfragen der Steckdose darf jederzeit laufen, das Einlegen in den Ring
///   nur am Taktanfang. Diese Trennung ist mit einem selbst abgefragten Peer
///   sauber, mit einem eingehängten nicht.</item>
///   <item><b>Namenspfade sind keine Grundlage.</b> Godot-RPC bindet an
///   Knotenpfade; unsere Simulation hängt in Prüfständen an drei verschiedenen
///   Stellen im Baum. Ein Protokoll aus Zahlen hat dieses Problem nicht.</item>
/// </list>
///
/// <para><b>Der Vermittler.</b> Der Gastgeber ist ENet-Server und leitet jedes
/// Paket eines Mitspielers unverändert an alle anderen weiter — das ist die Rolle
/// <c>[0x538270] != 0</c> des Originals (post() @0x4C1C5B: der Satz des
/// Vermittlers geht in den eigenen Ring UND von dort weiter, der Satz des
/// Mitspielers geht nur hinaus). ⚠ Auch das EIGENE Taktpaket nimmt bei uns
/// denselben Weg wie ein fremdes: es wird zu Bytes gemacht und durch dieselbe
/// Aufnahme (<see cref="Take"/>) in den Ring gelegt. Nur der Draht wird für sich
/// selbst übersprungen — ein Rundlauf über den Vermittler wäre beim Gastgeber
/// gar keiner und beim Mitspieler eine ganze zusätzliche Runde Verzögerung,
/// ohne dass sich am Inhalt etwas ändert (das Original bezahlt sie: 1003 hin,
/// 978 zurück).</para>
/// </summary>
public sealed class NetLink
{
    private ENetMultiplayerPeer? _peer;
    private readonly List<int> _clients = new();

    /// <summary>Bin ich der Vermittler?</summary>
    public bool IsHost { get; private set; }

    public NetSession? Session { get; private set; }

    /// <summary>Steht die Partie fest — Karte, Keim, Plätze? Erst dann darf eine
    /// Karte geladen werden, denn der Keim muss VOR dem Laden gesetzt sein
    /// (<c>NavGrid.Build</c> ruft <c>Determinism.NewMap</c>).</summary>
    public bool SessionReady { get; private set; }

    public string Fault { get; private set; } = "";

    // ---- Zahlen, ohne die eine Netzmessung nichts wert ist ------------------

    public long PacketsIn, PacketsOut, BytesIn, BytesOut, Relayed, Dropped;

    public int ClientCount => _relay != null ? _relayHumans - 1 : _clients.Count;

    public MultiplayerPeer.ConnectionStatus Status
        => _relay != null
            ? (_relay.Open ? MultiplayerPeer.ConnectionStatus.Connected : MultiplayerPeer.ConnectionStatus.Disconnected)
            : _peer?.GetConnectionStatus() ?? MultiplayerPeer.ConnectionStatus.Disconnected;

    // ---- ⭐ 11.11.2026: Online über den Lobby-Server (Relay) -----------------

    /// <summary>Der Draht zum Lobby-Server, wenn diese Partie online läuft; sonst null (ENet/LAN).
    /// <para>⚠ UNSERE ZUTAT. Der Server reicht jedes Paket an ALLE im Raum weiter, auch an
    /// den Absender, und nummeriert sie. Der Gastgeber muss darum nichts weiterleiten,
    /// und das eigene Echo wird verworfen — das eigene Paket ist wie bei ENet schon
    /// über <see cref="Take"/> eingetragen (dieselben Bytes, ein Weg).</para></summary>
    private LobbyClient? _relay;
    private int _relayHumans;
    private int _relaySlot = -1;

    /// <summary>Die eigene Spielernummer, die ein Sitzungspaket tragen muss, damit es
    /// angenommen wird. Online gehen ALLE Sitzungspakete an alle — jeder nimmt nur
    /// das für sich (Feld »forPlayer« der ENet-Fassung). −1 = keine Prüfung (ENet).</summary>
    public int ExpectMe = -1;

    public bool IsRelay => _relay != null;

    /// <summary>Eine Partie, die auf ihre eigene Karte wartet (NetzKarte).</summary>
    public NetSession? WartendeSitzung { get; private set; }

    /// <summary>Die Pakete einer eigenen Karte an alle — vor dem Sitzungspaket.</summary>
    public void SendKarte(List<byte[]> pakete)
    {
        foreach (var p in pakete) SendAll(p);
        if (_relay != null) Flush();
    }

    // ---- ⭐ 11.11.2026 Online 2b: AUSSCHALTEN (Ctrl-K, Befehl 973 des Originals) ----------
    //
    // ⚠ Der Schnitt muss auf ALLEN Maschinen derselbe Takt sein. Er wird darum nicht vom
    // Absender bestimmt, sondern beim EMPFANG gerechnet: C = letzter Takt, für den von p
    // schon ein Taktpaket da war, + 1. Das ist überall gleich, weil alle die Pakete in
    // DERSELBEN Reihenfolge bekommen — online nummeriert sie der Server, im LAN reicht der
    // Vermittler sie über geordnete Kanäle weiter (und Ctrl-K drückt nur er). Keine Maschine
    // kann Takt C schon gerechnet haben, denn dafür hätte sie das Paket von p für C gebraucht.
    // Spätere Pakete von p werden überall verworfen. UNSERE SETZUNG in der Form; das Original
    // verlässt sich auf seine Gastgeber-Sperre (0x4C5F50), die wir nicht haben.

    private readonly int[] _maxTick = { -1, -1, -1, -1, -1, -1, -1, -1 };
    /// <summary>Ausgeschaltete Spieler → erster Takt ohne sie.</summary>
    public readonly Dictionary<int, int> Cut = new();
    /// <summary>Neue Ausschaltungen für den Taktgeber (Spieler, Schnitt-Takt).</summary>
    public readonly List<(int Player, int Tick)> NewCuts = new();
    /// <summary>Eingegangene LAN-Chatzeilen (Spieler, Text).</summary>
    public readonly List<(int Player, string Text)> ChatIn = new();

    public bool IsOut(int p, int tick) => Cut.TryGetValue(p, out int c) && tick >= c;

    /// <summary>LAN: Laufzeit zu Spieler p in ms, soweit ENet sie kennt (Vermittler zu allen,
    /// Mitspieler nur zum Vermittler); −1 = unbekannt.</summary>
    public int RttOf(int p)
    {
        if (_peer == null || _relay != null) return -1;
        int id = IsHost ? (p >= 1 && p - 1 < _clients.Count ? _clients[p - 1] : -1) : (p == 0 ? 1 : -1);
        if (id < 0) return -1;
        var pp = _peer.GetPeer(id);
        return pp == null ? -1 : (int)pp.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
    }

    public static byte[] AusschaltenPacket(int player, int by)
        => new NetWriter(NetPacket.Ausschalten).U8((byte)player).U8((byte)by).Done();

    /// <summary>Spieler p ausschalten. ENet: sofort selbst aufnehmen (der Vermittler hat dann
    /// schon alles von p weitergereicht, was er hatte). Online: erst das Echo des Servers
    /// zählt — dort steht es an seinem Platz in der gemeinsamen Reihenfolge.</summary>
    public void SendAusschalten(int player, int by)
    {
        var b = AusschaltenPacket(player, by);
        SendAll(b);
        if (_relay == null) Take(b); else Flush();
    }

    public void SendPlausch(int player, string text)
    {
        var b = new NetWriter(NetPacket.Plausch).U8((byte)player).Text(text).Done();
        SendAll(b);
        Take(b);
    }

    // ---- bug-762: BÜNDELN -----------------------------------------------------
    // Der Server lässt höchstens 60 Rahmen je Sekunde und Verbindung durch (Auftrag §1);
    // der Lockstep schickt je Takt ein Paket (50 Hz) plus Prüfsummen, ein kopfloser Lauf
    // noch mehr. Darum sammelt die Online-Leitung alles, was in 40 ms anfällt, zu EINEM
    // Rahmen: [0xFE][n:u16]{[len:u16][Paket]}*n. Höchstens 25 Rahmen/s, ≤ 40 ms mehr
    // Verzögerung — der Vorlauf (6 Takte = 120 ms) fängt das auf. UNSERE SETZUNG.
    public const byte Buendel = 0xFE;
    private const int BuendelMs = 40, BuendelMax = 4000;
    private readonly List<byte[]> _out = new();
    /// <summary>Fertige Rahmen, die noch hinaus müssen — höchstens einer je 40 ms
    /// (Servergrenze 60/s; eine eigene Karte sind ~100 Rahmen am Stück).</summary>
    private readonly Queue<byte[]> _rahmen = new();
    private ulong _lastSend;
    private int _outBytes;
    private ulong _lastFlush;
    public long Bundles;

    private void Queue(byte[] bytes)
    {
        if (_outBytes + bytes.Length + 2 + 3 > BuendelMax) Bundle();
        _out.Add(bytes);
        _outBytes += bytes.Length + 2;
        PacketsOut++;
        BytesOut += bytes.Length;
    }

    /// <summary>Gesammelte Pakete als einen Rahmen abschicken.</summary>
    private static IEnumerable<byte[]> Unbundle(byte[] data)
    {
        if (data.Length >= 3 && data[0] == Buendel)
        {
            int n = BitConverter.ToUInt16(data, 1), o = 3;
            for (int i = 0; i < n && o + 2 <= data.Length; i++)
            {
                int len = BitConverter.ToUInt16(data, o);
                if (o + 2 + len > data.Length) yield break;
                var b = new byte[len];
                Buffer.BlockCopy(data, o + 2, b, 0, len);
                o += 2 + len;
                yield return b;
            }
        }
        else yield return data;
    }

    /// <summary>Gesammelte Pakete als Rahmen in die Sendeschlange und die Schlange so weit
    /// leeren, wie die Rate es erlaubt (ein Rahmen je 40 ms).</summary>
    public void Flush()
    {
        _lastFlush = Time.GetTicksMsec();
        Bundle();
        SendePaced();
    }

    /// <summary>Beim Abschied: alles sofort hinaus, ohne Drossel (wenige Rahmen).</summary>
    public void FlushNow()
    {
        Bundle();
        while (_relay != null && _rahmen.Count > 0)
            if (_relay.SendBinary(_rahmen.Dequeue())) Bundles++; else Dropped++;
    }

    private void SendePaced()
    {
        if (_relay == null) return;
        ulong now = Time.GetTicksMsec();
        if (_rahmen.Count > 0 && now - _lastSend >= BuendelMs)
        {
            _lastSend = now;
            if (_relay.SendBinary(_rahmen.Dequeue())) Bundles++; else Dropped++;
        }
    }

    private void Bundle()
    {
        if (_relay == null || _out.Count == 0) return;
        var frame = new byte[3 + _outBytes];
        frame[0] = Buendel;
        BitConverter.TryWriteBytes(frame.AsSpan(1, 2), (ushort)_out.Count);
        int o = 3;
        foreach (var b in _out)
        {
            BitConverter.TryWriteBytes(frame.AsSpan(o, 2), (ushort)b.Length);
            b.CopyTo(frame, o + 2);
            o += 2 + b.Length;
        }
        _rahmen.Enqueue(frame);
        _out.Clear();
        _outBytes = 0;
    }

    private void TakeFrame(byte[] data, bool ownEcho = false)
    {
        // Vom eigenen Echo zählt nur das Ausschalten (siehe SendAusschalten).
        if (ownEcho)
        {
            foreach (var b in Unbundle(data))
                if (b.Length > 0 && b[0] == (byte)NetPacket.Ausschalten) Take(b);
            return;
        }
        if (data.Length >= 3 && data[0] == Buendel)
        {
            int n = BitConverter.ToUInt16(data, 1), o = 3;
            for (int i = 0; i < n && o + 2 <= data.Length; i++)
            {
                int len = BitConverter.ToUInt16(data, o);
                if (o + 2 + len > data.Length) { Dropped++; break; }
                var b = new byte[len];
                Buffer.BlockCopy(data, o + 2, b, 0, len);
                o += 2 + len;
                PacketsIn++;
                BytesIn += len;
                Take(b);
            }
            return;
        }
        PacketsIn++;
        BytesIn += data.Length;
        Take(data);
    }

    /// <summary>Online-Partie einrichten, sobald der Server <c>match_start</c> geschickt hat.</summary>
    public void RelayOn(LobbyClient lobby, bool isHost, int me, int humans)
    {
        _relay = lobby;
        _relaySlot = lobby.YouSlot;
        _relayHumans = humans;
        ExpectMe = me;
        IsHost = isHost;
        GD.Print($"netz: online über {lobby.Url} — {(isHost ? "Gastgeber" : "Mitspieler")}, " +
                 $"Spieler {me} von {humans}, Raumplatz {_relaySlot}");
    }

    // ---- Eingang ------------------------------------------------------------

    /// <summary>Die Taktpakete, nach Takt und Spielernummer. Der Schlüssel ist
    /// der Takt; der Wert je Spieler entweder <c>null</c> (noch nicht gemeldet)
    /// oder die Liste seiner Sätze (auch leer = »ich habe nichts«).</summary>
    private readonly Dictionary<int, List<CommandRecord>?[]> _turns = new();

    /// <summary>Fremde Prüfsummen, Takt → (Spieler, Zahl). ⚠ Offen und nicht
    /// gekapselt, und das mit Absicht: der Prüfstand muss sie einzeln ansehen
    /// und STEHEN LASSEN können, solange die eigene Zahl für diesen Takt noch
    /// nicht da ist. Ein »nimm die nächste« hätte sie in dem Fall verschluckt.</summary>
    public readonly List<(int Tick, int Player, ulong Hash)> Digests = new();

    /// <summary>Fremde Zahlenauszüge, nur nach einem Fehlbefund.</summary>
    public readonly List<(int Tick, int Player, List<long> Snap)> Snaps = new();

    /// <summary>Wie viele Takte die Simulation des Gegenübers schon hinter sich
    /// hatte, als es die Leitung aufnahm — für den gemeinsamen Anfang.</summary>
    private readonly Dictionary<int, int> _headStart = new();

    /// <summary>Fremde Meldungen »ich bin auseinandergelaufen«.</summary>
    public readonly List<string> Verdicts = new();

    // ================= Aufbauen =============================================

    public bool HostOn(int port, int maxClients)
    {
        var p = new ENetMultiplayerPeer();
        Error e = p.CreateServer(port, maxClients);
        // ⭐ 14.11.2026 — Fable C7: Spielerdeutsch statt »CreateServer(27033) -> CantCreate«
        if (e != Error.Ok)
        {
            Fault = e is Error.CantCreate or Error.AlreadyInUse
                ? $"Port {port} ist schon belegt — läuft das Spiel schon in einem zweiten Fenster als Gastgeber? " +
                  $"Sonst einen anderen Port eintragen, z. B. :{port + 1} (technisch: {e})"
                : $"Das Netzwerk ließ sich nicht öffnen (Port {port}, {e})";
            return false;
        }
        p.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        p.PeerConnected += OnPeerConnected;
        p.PeerDisconnected += OnPeerDisconnected;
        _peer = p;
        IsHost = true;
        GD.Print($"netz: Vermittler auf Port {port}, bis zu {maxClients} Mitspieler");
        return true;
    }

    public bool JoinTo(string address, int port)
    {
        var p = new ENetMultiplayerPeer();
        Error e = p.CreateClient(address, port);
        if (e != Error.Ok) { Fault = $"Mit »{address}:{port}« lässt sich keine Verbindung aufbauen — Adresse prüfen ({e})"; return false; }
        p.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        // ⭐ 14.11.2026 — Fable C7: WAEHREND DES VERBINDENS eine kurze Grenze (8 s) — mit der geduldigen
        // (bug-810, 30..60 s) stand »dort ist niemand« erst nach 30 s im Schirm. Die geduldige gilt
        // ab dem Verbinden (Pump), denn erst dann kann der Vermittler beim Kartenladen schweigen.
        if (Array.IndexOf(Core.CommandLine.Args, "--netz-grund-alt") >= 0) Geduldig(p, 1);
        else
        {
            try { p.GetPeer(1)?.SetTimeout(32, 4000, 8000); }
            catch (Exception ex) { GD.PrintErr($"netz: Verbindungsgrenze nicht gesetzt: {ex.Message}"); }
            _geduldigNachVerbinden = true;
        }
        _peer = p;
        IsHost = false;
        GD.Print($"netz: Mitspieler, verbinde nach {address}:{port}");
        return true;
    }

    /// <summary>
    /// ⭐ 14.11.2026 — SANFT TRENNEN (Mitspieler). <c>Close()</c> trennt sofort; laedt der Vermittler
    /// gerade die Karte (Hauptfaden mehrere Sekunden still), kommen Paket und Trennung bei ihm
    /// ZUSAMMEN an, und ENet meldet dann zuerst die Trennung und verwirft das Paket (Zombie-Zweig
    /// von dispatch_incoming_commands — gemessen: das Ausschalten eines Mitspielers ohne Karte kam
    /// nie an). <c>PeerDisconnectLater</c> trennt erst, wenn alles Zuverlaessige bestaetigt ist.
    /// Rueckgabe: true, sobald getrennt (oder nichts zu trennen ist) — so lange weiter rufen.
    /// </summary>
    public bool SanftTrennen()
    {
        if (_relay != null || _peer == null || IsHost) return true;
        if (_peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected) return true;
        if (!_sanftAngestossen) { _sanftAngestossen = true; _peer.GetPeer(1)?.PeerDisconnectLater(); }
        _peer.Poll();
        return _peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected;
    }
    private bool _sanftAngestossen;
    private bool _geduldigNachVerbinden;

    public void Close()
    {
        _relay = null;              // ⚠ der Lobby-Draht gehört dem NetworkManager, nicht der Partie
        _peer?.Close();
        _peer = null;
        _clients.Clear();
    }

    // ⭐ 12.11.2026 — bug-810: GEDULDIGERE LEITUNG. Backt ein Mitspieler eine
    // mitgereiste grosse Karte (254x125: mehrere Sekunden auf dem Hauptfaden), schweigt
    // er so lange. ENet trennt nach Vorgabe schon nach 5 s Schweigen (timeoutMin) —
    // die Partie stand dann still: »Vorlauf, es fehlt noch jemand«. Jetzt 30..60 s.
    // Gegenschalter --netz-zeitgrenze-alt.
    private static void Geduldig(ENetMultiplayerPeer p, int id)
    {
        if (Array.IndexOf(Core.CommandLine.Args, "--netz-zeitgrenze-alt") >= 0) return;
        try { p.GetPeer(id)?.SetTimeout(64, 30000, 60000); }
        catch (Exception e) { GD.PrintErr($"netz: Zeitgrenze fuer {id} nicht gesetzt: {e.Message}"); }
    }

    private void OnPeerConnected(long id)
    {
        if (_peer is ENetMultiplayerPeer ep) Geduldig(ep, (int)id);
        if (!_clients.Contains((int)id)) _clients.Add((int)id);
        _clients.Sort();          // ⚠ die Spielernummern hängen an dieser Ordnung
        GD.Print($"netz: Mitspieler {id} da ({_clients.Count} insgesamt)");
    }

    private void OnPeerDisconnected(long id)
    {
        _clients.Remove((int)id);
        GD.PrintErr($"netz: Mitspieler {id} ist weg ({_clients.Count} übrig)");
    }

    /// <summary>Die Spielernummer eines ENet-Teilnehmers. 0 ist immer der
    /// Vermittler; die Mitspieler zählen in der Reihenfolge ihrer ENet-Nummern
    /// weiter. ⚠ Diese Ordnung muss auf ALLEN Maschinen dieselbe sein, denn sie
    /// bestimmt die Reihenfolge der Befehle in einem Takt — deshalb schickt der
    /// Vermittler sie in <see cref="NetSession"/> mit und niemand rechnet sie
    /// selbst aus.</summary>
    public int PlayerOfPeer(int enetId) => enetId == 1 ? 0 : _clients.IndexOf(enetId) + 1;

    // ================= Senden ===============================================

    private void SendTo(int target, byte[] bytes)
    {
        if (_relay != null)
        {
            // Online gibt es kein »an einen«: der Server reicht an alle (Ziel wird übergangen).
            Queue(bytes);
            return;
        }
        if (_peer == null) return;
        // ⚠ Auf eine erloschene Steckdose NICHT schreiben. Godot meldet dort
        // »The multiplayer instance isn't currently active« mit vollem
        // Rückverfolgungsbaum, und zwar je Paket — im Ausklang des ersten
        // Gegenprobelaufs waren das vier Meldungen mit je zehn Zeilen, in denen
        // der eigentliche Befund unterging. Ein Protokoll, in dem der Befund
        // nicht mehr zu finden ist, ist keines.
        if (_peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
        { Dropped++; return; }
        _peer.SetTargetPeer(target);
        if (_peer.PutPacket(bytes) != Error.Ok) { Dropped++; return; }
        PacketsOut++;
        BytesOut += bytes.Length;
    }

    /// <summary>An alle anderen. Beim Vermittler heisst das »an alle
    /// Mitspieler«, beim Mitspieler »an den Vermittler« — der leitet weiter.</summary>
    public void SendAll(byte[] bytes) => SendTo(0, bytes);

    // ================= Die Partie ===========================================

    /// <summary>
    /// Der Vermittler legt die Partie fest und schickt sie herum. ⚠ <b>Der Keim
    /// kommt von hier</b> und nur von hier — <c>Determinism.Forced</c> wird bei
    /// jedem Teilnehmer daraus gesetzt, BEVOR die Karte lädt. Ohne das würfeln
    /// beide Maschinen aus dem Kartennamen, was zufällig auch stimmt, aber nur
    /// solange beide dieselbe Schreibweise erwischen — und es macht eine
    /// Neuauflage derselben Karte mit anderem Verlauf unmöglich.
    /// </summary>
    public void OfferSession(NetSession s)
    {
        s.Me = 0;
        s.Players = 1 + ClientCount;
        Session = s;
        SessionReady = true;

        if (_relay != null)
        {
            // Online: je Mitspieler ein Paket, alle an alle; jeder nimmt nur seins (ExpectMe).
            for (int i = 1; i < s.Players; i++) SendAll(SessionPacket(s, i));
            Flush();
        }
        else
        for (int i = 0; i < _clients.Count; i++)
        {
            var w = SessionPacket(s, i + 1);
            SendTo(_clients[i], w);
        }
        GD.Print($"netz: Partie verteilt — {s}");
    }

    private static byte[] SessionPacket(NetSession s, int forPlayer)
        => new NetWriter(NetPacket.Sitzung)
            .I32(NetSession.Protokoll)
            .Text(s.Map)
            .U32(s.Seed)
            .U8((byte)s.Players)
            .U8((byte)forPlayer)
            .U16(s.Lead)
            .U8((byte)s.AiCount)
            .U8((byte)s.Level)
            .U8((byte)s.Techstandard)
            .U8((byte)s.Resources)
            .Bool(s.AllUnits)
            .U16(s.StartMoney)                // bug-734, Protokoll 3
            .Bool(s.NoStartBase)
            .Bool(s.StartbasisAus)
            .Bool(s.EigeneKarte)              // Protokoll 4: eigene Karte + Prüfsumme
            .U64(s.KartenSumme)
            .U8((byte)s.Slot.Length)
            .Also(w => { foreach (int sl in s.Slot) w.U8((byte)sl); })
            .U8((byte)s.Tempo)                // Protokoll 5 (Fable D10): Starttempo
            .Bool(s.Aufstellung)              // Protokoll 5 (P7): Art + Team je Kartenplatz
            .Also(w => { for (int p = 0; p < 8; p++) w.U8((byte)s.Art[p]).U8((byte)s.Team[p]); })
            .Done();

    // ================= Der Takt =============================================

    /// <summary>⚠ Die Paketbauer geben BYTES zurück und schicken nicht selbst.
    /// Der Grund ist der Weg des eigenen Pakets: es muss <b>dieselben Bytes</b>
    /// durch <see cref="Take"/> in die Fächer legen, die auch hinausgehen. Wer
    /// zwei Wege baut — einen für sich, einen für die anderen — hat zwei Orte,
    /// an denen die Form auseinanderlaufen kann, und der Prüfstand sieht davon
    /// nichts.</summary>
    public static byte[] HeadStartPacket(int player, int ticks, ulong tafeln = 0, string name = "")
        => new NetWriter(NetPacket.Anfang).U8((byte)player).I32(ticks).U64(tafeln).Text(name).Done();

    /// <summary>⭐ 14.11.2026 — Fable C12/D10: die Spielernamen (aus dem Vorlaufpaket, Protokoll 5).
    /// Im LAN hiessen alle »Spieler N«; online kommen sie weiter aus dem Raum.</summary>
    public readonly Dictionary<int, string> Namen = new();

    /// <summary>Paket W4 (bug-641): die Prüfzahl der KI-Sektortafeln je Spieler, aus dem
    /// Vorlaufpaket (MapEntityLayer.KiTafelPruefzahl).</summary>
    public readonly Dictionary<int, ulong> TafelPruefzahl = new();

    /// <summary>
    /// DAS TAKTPAKET. »Ich bin für Takt <paramref name="tick"/> bereit, und das
    /// sind meine Sätze für diesen Takt.«
    ///
    /// <para>⚠ Es wird IMMER geschickt, auch leer. Ein Takt ohne Meldung ist ein
    /// Takt, in dem alle anderen stehen bleiben — das Schweigen ist hier keine
    /// Ersparnis, sondern ein Aufhänger. Das Original hält es genauso: 978 geht
    /// jede Runde hinaus, ob jemand geklickt hat oder nicht.</para>
    /// </summary>
    public byte[] TurnPacket(int player, int tick, List<CommandRecord> recs, int round)
    {
        var w = new NetWriter(NetPacket.Bereit).U8((byte)player).I32(tick).U16(recs.Count);
        foreach (var c in recs)
        {
            var r = c;
            // ⚠ Die Rundennummer in +0x22 — das Feld, das im Original
            // post() @0x4C1C7A aus [0x4F9248] füllt und das 978 in P9 mitführt.
            // Bei uns ist die Runde der Takt (siehe NetProtocol).
            r.Unknown22 = (short)round;
            r.Flag = 1;              // »weitergeleitet«, @0x4C1FFB
            w.Record(r);
        }
        return w.Done();
    }

    public static byte[] DigestPacket(int player, int tick, ulong hash)
        => new NetWriter(NetPacket.Abdruck).U8((byte)player).I32(tick).U64(hash).Done();

    public static byte[] SnapshotPacket(int player, int tick, List<long> snap)
    {
        var w = new NetWriter(NetPacket.AbdruckLang).U8((byte)player).I32(tick).I32(snap.Count);
        foreach (long v in snap) w.I64(v);
        return w.Done();
    }

    public static byte[] VerdictPacket(int player, int tick, string text)
        => new NetWriter(NetPacket.Auseinander).U8((byte)player).I32(tick).Text(text).Done();

    // ================= Abfragen =============================================

    /// <summary>
    /// Die Steckdose leeren: alles lesen, was da ist, weiterleiten was
    /// weiterzuleiten ist, und in die Fächer legen. ⚠ Das darf jederzeit
    /// laufen — es berührt den Zustand der Simulation nicht. Was den Zustand
    /// berührt, ist die Entnahme aus den Fächern, und die geschieht am
    /// Taktanfang.
    /// </summary>
    private bool _totGemeldet;
    private static readonly bool PumpAlt = Array.IndexOf(Core.CommandLine.Args, "--netz-pump-alt") >= 0;

    public void Pump()
    {
        if (_relay != null)
        {
            if (Time.GetTicksMsec() - _lastFlush >= BuendelMs) Flush();
            _relay.Poll();
            while (_relay.Frames.Count > 0)
            {
                var f = _relay.Frames.Dequeue();
                // eigenes Echo: schon über Take eingetragen — bis auf das Ausschalten
                TakeFrame(f.Data, ownEcho: f.Slot == _relaySlot);
            }
            return;
        }
        if (_peer == null) return;
        // ⭐ 13.11.2026 — Fable B11: nach einem Abriss rief Pump in JEDEM Bild Poll() auf der toten
        // Leitung, und Godot schrieb jedes Mal »The multiplayer instance isn't currently active«
        // samt Rueckverfolgung (gemessen 4548x, 7,9 MB in 40 s). Jetzt einmal melden, dann still.
        // Gegenschalter --netz-pump-alt.
        if (!PumpAlt && _peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Disconnected)
        {
            if (!_totGemeldet) { _totGemeldet = true; GD.PrintErr("netz: die Leitung ist getrennt — es wird nichts mehr empfangen"); }
            return;
        }
        _peer.Poll();
        if (_geduldigNachVerbinden && _peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
        { _geduldigNachVerbinden = false; Geduldig(_peer, 1); }   // ab jetzt die geduldige Grenze (bug-810)
        while (_peer.GetAvailablePacketCount() > 0)
        {
            int from = _peer.GetPacketPeer();
            byte[] bytes = _peer.GetPacket();
            PacketsIn++;
            BytesIn += bytes.Length;

            // Der Vermittler leitet weiter, bevor er selbst hineinsieht — ein
            // Paket, an dem er sich verschluckt, darf die anderen nicht
            // aufhalten.
            if (IsHost && bytes.Length > 0 && (NetPacket)bytes[0] != NetPacket.Hallo)
            {
                foreach (int c in _clients)
                    if (c != from) { SendTo(c, bytes); Relayed++; }
            }
            Take(bytes);
        }
    }

    /// <summary>
    /// EIN PAKET AUFNEHMEN — der einzige Weg herein, für fremde wie für eigene
    /// Pakete. Das ist der <c>Receive</c>-Zweig des Originals (@0x404460): auch
    /// der eigene Befehl kommt bei ihm über den Empfang zurück, und erst dann
    /// wirkt er. Bei uns wird für sich selbst der Draht übersprungen, aber nicht
    /// der Weg.
    /// </summary>
    public void Take(byte[] bytes)
    {
        var r = new NetReader(bytes).Begin();
        switch (r.Kind)
        {
            case NetPacket.Sitzung: TakeSession(r); break;
            case NetPacket.Anfang:
            {
                int p = r.U8(), n = r.I32();
                ulong tz = r.U64();
                string name = !r.Bad && r.Left > 0 ? r.Text() : "";
                if (!r.Bad) { _headStart[p] = n; TafelPruefzahl[p] = tz; if (name.Length > 0 && !Namen.ContainsKey(p)) { Namen[p] = name; GD.Print($"netz: Spieler {p} heisst »{name}«"); } }
                break;
            }
            case NetPacket.Bereit:
            {
                int p = r.U8(), t = r.I32(), n = r.U16();
                var list = new List<CommandRecord>(n);
                for (int i = 0; i < n; i++) list.Add(r.Record());
                if (r.Bad) { Dropped++; break; }
                if (IsOut(Clamp(p), t)) break;             // nach dem Schnitt: überall verworfen
                Slots(t)[Clamp(p)] = list;
                if (t > _maxTick[Clamp(p)]) _maxTick[Clamp(p)] = t;
                break;
            }
            case NetPacket.Abdruck:
            {
                int p = r.U8(), t = r.I32();
                ulong h = r.U64();
                if (!r.Bad) Digests.Add((t, p, h));
                break;
            }
            case NetPacket.AbdruckLang:
            {
                int p = r.U8(), t = r.I32();
                var s = r.Longs();
                if (!r.Bad) Snaps.Add((t, p, s));
                break;
            }
            case NetPacket.Auseinander:
            {
                int p = r.U8(), t = r.I32();
                string s = r.Text();
                if (!r.Bad) Verdicts.Add($"Spieler {p} meldet bei Takt {t}: {s}");
                break;
            }
            case NetPacket.Ausschalten:
            {
                int p = r.U8(), by = r.U8();
                if (r.Bad || p > 7 || Cut.ContainsKey(p)) break;
                int c = _maxTick[p] + 1;
                Cut[p] = c;
                NewCuts.Add((p, c));
                GD.Print($"netz: Spieler {p} ausgeschaltet (von Spieler {by}) ab Takt {c}");
                break;
            }
            case NetPacket.Karte:
                NetzKarte.Nimm(r, bytes, 0);
                if (WartendeSitzung is { } ws && NetzKarte.Bereitstellen(ws.KartenSumme))
                {
                    WartendeSitzung = null;
                    Session = ws;
                    SessionReady = true;
                    GD.Print($"netz: Karte da — Partie steht: {ws}");
                }
                break;
            case NetPacket.Plausch:
            {
                int p = r.U8();
                string text = r.Text();
                if (!r.Bad) ChatIn.Add((p, text));
                break;
            }
            default: Dropped++; break;
        }
    }

    private void TakeSession(NetReader r)
    {
        int proto = r.I32();
        var s = new NetSession { Map = r.Text(), Seed = r.U32() };
        s.Players = r.U8();
        s.Me = r.U8();
        s.Lead = r.U16();
        s.AiCount = r.U8();
        s.Level = r.U8();
        s.Techstandard = r.U8();
        s.Resources = r.U8();
        s.AllUnits = r.Bool();
        // ⚠ bug-734: ab Protokoll 3. Ein Gastgeber mit Protokoll 2 liefert diese Bytes
        // nicht — dann liest das hier die Platzzahl falsch, und die Pruefung unten
        // verwirft die Sitzung ohnehin wegen der Nummer.
        if (proto >= 3)
        {
            s.StartMoney = r.U16();
            s.NoStartBase = r.Bool();
            s.StartbasisAus = r.Bool();
        }
        if (proto >= 4)
        {
            s.EigeneKarte = r.Bool();
            s.KartenSumme = r.U64();
        }
        int n = r.U8();
        var slots = new int[n];
        for (int i = 0; i < n; i++) slots[i] = r.U8();
        s.Slot = slots;
        if (proto >= 5)
        {
            s.Tempo = r.U8();
            s.Aufstellung = r.Bool();
            for (int p = 0; p < 8; p++) { s.Art[p] = r.U8(); s.Team[p] = r.U8(); }
        }
        if (r.Bad) { Dropped++; return; }
        if (ExpectMe >= 0 && s.Me != ExpectMe) return;   // online: das Sitzungspaket eines anderen
        if (proto != NetSession.Protokoll)
        {
            Fault = $"Protokoll {proto} des Gastgebers passt nicht zu unserem " +
                    $"{NetSession.Protokoll} — diese beiden Fassungen spielen nicht zusammen";
            GD.PrintErr("netz: " + Fault);
            return;
        }
        // ⭐ 11.11.2026: eine EIGENE Karte des Gastgebers — erst spielen, wenn sie hier liegt.
        if (s.EigeneKarte)
        {
            if (!NetzKarte.ImmerEmpfangen && NetzKarte.Vorhanden(s.Map) && NetzKarte.Pruefsumme(s.Map) == s.KartenSumme) { /* dieselbe Karte unter demselben Namen */ }
            else
            {
                s.Map = NetzKarte.LokalerName(s.KartenSumme);
                if (!NetzKarte.Bereitstellen(s.KartenSumme))
                {
                    WartendeSitzung = s;
                    GD.Print($"netz: Partie empfangen, warte auf die Karte ({s.KartenSumme:X16})");
                    return;
                }
            }
        }
        Session = s;
        SessionReady = true;
        GD.Print($"netz: Partie empfangen — {s}");
    }

    private static int Clamp(int p) => p < 0 ? 0 : p > 7 ? 7 : p;

    private List<CommandRecord>?[] Slots(int tick)
    {
        if (_turns.TryGetValue(tick, out var a)) return a;
        a = new List<CommandRecord>?[8];
        _turns[tick] = a;
        return a;
    }

    /// <summary>Das eigene Taktpaket eintragen — über dieselben Bytes, die auch
    /// hinausgehen (siehe <see cref="Take"/>).</summary>
    public void TakeOwn(byte[] bytes) => Take(bytes);

    /// <summary>
    /// Ist Takt <paramref name="tick"/> frei? Frei heisst: <b>jeder</b> Spieler
    /// hat für ihn gemeldet. Das ist die Sperre <c>[0x4F6F28]</c> des Originals,
    /// nur dass wir sie selbst ausrechnen statt sie uns sagen zu lassen.
    /// </summary>
    public bool TurnReady(int tick, int players)
    {
        if (!_turns.TryGetValue(tick, out var a)) a = Slots(tick);
        for (int p = 0; p < players; p++) if (a[p] == null && !IsOut(p, tick)) return false;
        return true;
    }

    /// <summary>Wer für diesen Takt noch fehlt — die Zahl, die in eine
    /// Warteschlange gehört. »Es hängt« ist keine Meldung, »Spieler 1 fehlt seit
    /// 2,4 s« ist eine.</summary>
    public string TurnMissing(int tick, int players)
    {
        if (!_turns.TryGetValue(tick, out var a)) return $"alle {players}";
        var miss = new List<int>();
        for (int p = 0; p < players; p++) if (a[p] == null && !IsOut(p, tick)) miss.Add(p);
        return miss.Count == 0 ? "—" : string.Join(",", miss);
    }

    /// <summary>
    /// Die Sätze dieses Takts in der <b>festgelegten</b> Reihenfolge: nach
    /// Spielernummer, darin wie sie im Paket standen.
    ///
    /// <para>⚠ Das ist der Punkt, an dem wir vom Original abweichen und
    /// abweichen MÜSSEN. Dort ist die Reihenfolge die der Ankunft im Ring, und
    /// die hängt an der Leitung — auf zwei Maschinen also verschieden. Eine
    /// vertauschte Reihenfolge ist ein Auseinanderlaufen (zwei Befehle auf
    /// dieselbe Zelle: wer zuerst kommt, bekommt sie).</para>
    /// </summary>
    /// <summary>Wer für diesen Takt noch fehlt, als Liste (für das Wartefenster).</summary>
    public List<int> TurnMissingList(int tick, int players)
    {
        var miss = new List<int>();
        _turns.TryGetValue(tick, out var a);
        for (int p = 0; p < players; p++)
            if ((a == null || a[p] == null) && !IsOut(p, tick)) miss.Add(p);
        return miss;
    }

    public IEnumerable<CommandRecord> TurnRecords(int tick, int players)
    {
        if (!_turns.TryGetValue(tick, out var a)) yield break;
        for (int p = 0; p < players; p++)
        {
            var l = a[p];
            if (l == null || IsOut(p, tick)) continue;
            foreach (var c in l) yield return c;
        }
    }

    public int TurnCount(int tick, int players)
    {
        int n = 0;
        foreach (var _ in TurnRecords(tick, players)) n++;
        return n;
    }

    /// <summary>Alte Takte wegwerfen — der Puffer soll nicht mit der Partie
    /// wachsen.</summary>
    public void ForgetBefore(int tick)
    {
        if (_turns.Count < 256) return;
        var old = new List<int>();
        foreach (int k in _turns.Keys) if (k < tick) old.Add(k);
        foreach (int k in old) _turns.Remove(k);
    }

    public static bool KarteFehltAlt => Array.IndexOf(Core.CommandLine.Args, "--netz-karte-fehlt-alt") >= 0;

    public bool AllHeadStarts(int players, out int max)
    {
        max = 0;
        for (int p = 0; p < players; p++)
        {
            // ⭐ 14.11.2026 (Fable B12): wer sich VOR dem Anfang ausgeschaltet hat (Karte fehlt), schickt
            // nie einen Vorlauf — ohne diese Zeile warteten alle anderen auf ihn. Der Schnitt steht
            // bei Takt 0 auf jeder Maschine, also dieselbe Entscheidung ueberall. --netz-karte-fehlt-alt.
            if (Cut.ContainsKey(p) && !KarteFehltAlt) continue;
            if (!_headStart.TryGetValue(p, out int n)) return false;
            if (n > max) max = n;
        }
        return true;
    }

    public string HeadStartLine(int players)
    {
        var parts = new List<string>();
        for (int p = 0; p < players; p++)
            parts.Add(_headStart.TryGetValue(p, out int n) ? $"{p}:{n}" : Cut.ContainsKey(p) ? $"{p}:aus" : $"{p}:—");
        return string.Join(" ", parts);
    }

    public string Numbers()
        => $"Pakete ein {PacketsIn} ({BytesIn} B), aus {PacketsOut} ({BytesOut} B), " +
           $"weitergeleitet {Relayed}, verworfen {Dropped}" +
           (_relay != null ? $", online in {Bundles} Rahmen, Server-Nr. bis {_relay.LastSeq}" : "");
}

/// <summary>Ein Handgriff, damit <see cref="NetWriter"/> in einer Kette eine
/// Schleife vertragen kann — sonst müsste der Sitzungsaufbau in zwei Hälften
/// zerfallen und die Paketform stünde nicht mehr an einer Stelle.</summary>
internal static class NetWriterExt
{
    public static NetWriter Also(this NetWriter w, Action<NetWriter> more)
    {
        more(w);
        return w;
    }
}
