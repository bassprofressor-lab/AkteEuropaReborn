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

    public int ClientCount => _clients.Count;

    public MultiplayerPeer.ConnectionStatus Status
        => _peer?.GetConnectionStatus() ?? MultiplayerPeer.ConnectionStatus.Disconnected;

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
        if (e != Error.Ok) { Fault = $"CreateServer({port}) -> {e}"; return false; }
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
        if (e != Error.Ok) { Fault = $"CreateClient({address}:{port}) -> {e}"; return false; }
        p.TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
        _peer = p;
        IsHost = false;
        GD.Print($"netz: Mitspieler, verbinde nach {address}:{port}");
        return true;
    }

    public void Close()
    {
        _peer?.Close();
        _peer = null;
        _clients.Clear();
    }

    private void OnPeerConnected(long id)
    {
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
        s.Players = 1 + _clients.Count;
        Session = s;
        SessionReady = true;

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
            .U8((byte)s.Slot.Length)
            .Also(w => { foreach (int sl in s.Slot) w.U8((byte)sl); })
            .Done();

    // ================= Der Takt =============================================

    /// <summary>⚠ Die Paketbauer geben BYTES zurück und schicken nicht selbst.
    /// Der Grund ist der Weg des eigenen Pakets: es muss <b>dieselben Bytes</b>
    /// durch <see cref="Take"/> in die Fächer legen, die auch hinausgehen. Wer
    /// zwei Wege baut — einen für sich, einen für die anderen — hat zwei Orte,
    /// an denen die Form auseinanderlaufen kann, und der Prüfstand sieht davon
    /// nichts.</summary>
    public static byte[] HeadStartPacket(int player, int ticks)
        => new NetWriter(NetPacket.Anfang).U8((byte)player).I32(ticks).Done();

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
    public void Pump()
    {
        if (_peer == null) return;
        _peer.Poll();
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
                if (!r.Bad) _headStart[p] = n;
                break;
            }
            case NetPacket.Bereit:
            {
                int p = r.U8(), t = r.I32(), n = r.U16();
                var list = new List<CommandRecord>(n);
                for (int i = 0; i < n; i++) list.Add(r.Record());
                if (r.Bad) { Dropped++; break; }
                Slots(t)[Clamp(p)] = list;
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
        int n = r.U8();
        var slots = new int[n];
        for (int i = 0; i < n; i++) slots[i] = r.U8();
        s.Slot = slots;
        if (r.Bad) { Dropped++; return; }
        if (proto != NetSession.Protokoll)
        {
            Fault = $"Protokoll {proto} des Gastgebers passt nicht zu unserem " +
                    $"{NetSession.Protokoll} — diese beiden Fassungen spielen nicht zusammen";
            GD.PrintErr("netz: " + Fault);
            return;
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
        if (!_turns.TryGetValue(tick, out var a)) return false;
        for (int p = 0; p < players; p++) if (a[p] == null) return false;
        return true;
    }

    /// <summary>Wer für diesen Takt noch fehlt — die Zahl, die in eine
    /// Warteschlange gehört. »Es hängt« ist keine Meldung, »Spieler 1 fehlt seit
    /// 2,4 s« ist eine.</summary>
    public string TurnMissing(int tick, int players)
    {
        if (!_turns.TryGetValue(tick, out var a)) return $"alle {players}";
        var miss = new List<int>();
        for (int p = 0; p < players; p++) if (a[p] == null) miss.Add(p);
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
    public IEnumerable<CommandRecord> TurnRecords(int tick, int players)
    {
        if (!_turns.TryGetValue(tick, out var a)) yield break;
        for (int p = 0; p < players; p++)
        {
            var l = a[p];
            if (l == null) continue;
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

    public bool AllHeadStarts(int players, out int max)
    {
        max = 0;
        for (int p = 0; p < players; p++)
        {
            if (!_headStart.TryGetValue(p, out int n)) return false;
            if (n > max) max = n;
        }
        return true;
    }

    public string HeadStartLine(int players)
    {
        var parts = new List<string>();
        for (int p = 0; p < players; p++)
            parts.Add(_headStart.TryGetValue(p, out int n) ? $"{p}:{n}" : $"{p}:—");
        return string.Join(" ", parts);
    }

    public string Numbers()
        => $"Pakete ein {PacketsIn} ({BytesIn} B), aus {PacketsOut} ({BytesOut} B), " +
           $"weitergeleitet {Relayed}, verworfen {Dropped}";
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
