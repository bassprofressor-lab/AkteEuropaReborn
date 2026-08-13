namespace AkteEuropaReborn.Network;

using System.Collections.Generic;
using Godot;

/// <summary>
/// DIE LAN-SUCHE — »zeig mir, wo was offen ist«, ohne dass jemand eine Adresse
/// eintippen muss.
///
/// <para><b>Auftrag des Spielers, 15.08.2026:</b> »Lobbys, dass man sieht wo was
/// offen ist« — und dazu die Entscheidung »LAN erstmal machen, Internet später«.
/// Hier steht deshalb <b>nur</b> der LAN-Weg. Ein Vermittlungsserver im Internet
/// braucht jemanden, der ihn betreibt und bezahlt, und einen NAT-Durchstich;
/// beides ist ausdrücklich nicht gebaut. Es ist aber auch nichts verbaut: die
/// Lobby zeigt eine Liste von <see cref="NetOffer"/>, und wo die herkommen, ist
/// ihr gleich. Ein Server könnte sie später füllen, ohne dass die Anzeige sich
/// ändert.</para>
///
/// <para><b>⚠ FRAGE-UND-ANTWORT, nicht DAUERFUNK — und das ist eine Entscheidung
/// mit drei Gründen.</b> Der naheliegende Weg wäre ein Leuchtfeuer: der
/// Gastgeber ruft alle Sekunde »hier läuft eine Partie« ins Netz. Gebaut ist das
/// Gegenteil — der SUCHENDE fragt per Rundruf, und die Gastgeber antworten ihm
/// einzeln:</para>
/// <list type="number">
///   <item><b>Der Suchende weiss, wann seine Suche anfing.</b> Damit kann er
///   »nach 1500 ms nichts gefunden« sagen. Bei einem Leuchtfeuer weiss er nur,
///   dass er nichts gehört hat — und das ist derselbe Zustand wie »ich habe
///   nicht lange genug zugehört«. ⚠ Eine leere Liste sieht sonst aus wie
///   »funktioniert, es läuft nur nichts«.</item>
///   <item><b>Nur EIN Rundruf statt Dauerfunk.</b> Die Antwort geht als
///   Einzelsendung an den Fragenden zurück, nicht als zweiter Rundruf.</item>
///   <item><b>⚠ Zwei Prozesse auf EINER Maschine funktionieren damit ohne
///   Kunstgriffe.</b> Beim Leuchtfeuer müssten alle Beteiligten denselben Port
///   abhören, und zwei Sockets auf einem Port brauchen <c>SO_REUSEADDR</c> —
///   was Godots <c>PacketPeerUdp</c> nicht anbietet. Hier belegt nur der
///   GASTGEBER den festen Port <see cref="Port"/>; der Suchende bindet einen
///   beliebigen freien und bekommt die Antwort dorthin. Der Prüfstand mit zwei
///   Prozessen auf einer Maschine prüft damit denselben Code wie ein echtes
///   LAN — nur den Zustellweg nicht, siehe unten.</item>
/// </list>
///
/// <para><b>⚠ Rundruf UND Rückschleife, beides.</b> Die Frage geht an
/// <c>255.255.255.255</c> (das LAN) <b>und</b> an <c>127.0.0.1</c> (derselbe
/// Rechner). Zwei Gründe: der Ein-Maschinen-Fall ist für den Spieler echt (zwei
/// Fenster zum Ausprobieren), und ob ein Rundruf an die eigene Maschine
/// zurückkommt, hängt am Betriebssystem und an der Firewall. Jede Antwort merkt
/// sich, über welchen der beiden Wege die Frage kam
/// (<see cref="NetOffer.Via"/>) — damit sagt der Prüfstand, WAS er belegt hat,
/// statt den Normalfall zu behaupten.</para>
///
/// <para><b>⚠ Was diese Suche NICHT kann, und es ist keine Kleinigkeit.</b> Ein
/// Rundruf über UDP bekommt keine Fehlermeldung. Blockiert die Firewall ihn,
/// sieht das <b>genau so aus</b> wie »es ist kein Gastgeber da«: keine Antwort.
/// Unterscheiden lässt sich nur der Sonderfall, dass die Rückschleife antwortet
/// und das LAN nicht — dann ist auf dieser Maschine etwas offen und im Netz
/// nichts, und die Firewall ist ein begründeter Verdacht. Deshalb nennt die
/// Meldung sie als <b>Verdacht</b> und nicht als Befund. Scheitert schon das
/// Binden des Sockets, ist das dagegen ein handfester Fehler und wird als solcher
/// gemeldet.</para>
///
/// <para><b>Warum Rundruf und nicht Multicast.</b> <c>PacketPeerUdp</c> kann
/// beides. Multicast ist sauberer (es flutet das Netz nicht), aber in Heimnetzen
/// unzuverlässig: IGMP-Snooping in Switches und viele WLAN-Router lassen
/// Multicast liegen. Ein Rundruf ins eigene Subnetz ist ein Paket von etwa 20
/// Byte, einmal je Suche — die Ersparnis wäre nicht messbar, das Risiko
/// schon.</para>
/// </summary>
public sealed class NetDiscovery
{
    /// <summary>⚠ UNSERE SETZUNG: der Suchport. <see cref="NetworkManager"/>
    /// nimmt als Spielport 27015, also liegt der Suchport daneben. Das Original
    /// hatte für DirectPlay eine eigene Sitzungsaufzählung
    /// (<c>IDirectPlay::EnumSessions</c>) und brauchte keinen eigenen Port; ein
    /// Port von uns ist er deshalb ganz.</summary>
    public const int Port = 27016;

    /// <summary>
    /// ⚠ UNSERE SETZUNG: so lange wird nach einem Rundruf zugehört, 1200 ms.
    ///
    /// <para>Die Zahl ist nach unten begrenzt durch die Umlaufzeit im LAN
    /// (Millisekunden) plus die Bildzeit des Gastgebers — er antwortet aus seinem
    /// <c>_Process</c>, also erst im nächsten Bild, und wer bei 30 Bildern/s im
    /// Menü sitzt, braucht 33 ms dafür. Nach oben ist es die Geduld des Spielers,
    /// der auf einen Knopf gedrückt hat. 1200 ms ist mehr als das Zehnfache des
    /// Nötigen und immer noch keine Wartezeit, die man als solche empfindet.</para>
    ///
    /// <para>⚠ Es ist kein Fenster, in dem GENAU EINE Antwort erwartet wird: es
    /// können mehrere Gastgeber antworten, und jeder darf sich melden, solange es
    /// offen ist.</para>
    /// </summary>
    public const int SearchWindowMs = 1200;

    /// <summary>⚠ Wird bei jeder Änderung der Frage- oder Angebotsform erhöht.
    /// Ein Gastgeber mit anderer Nummer antwortet nicht — besser gar kein
    /// Eintrag als einer, dessen Zahlen etwas anderes bedeuten.</summary>
    public const int Protokoll = 1;

    private PacketPeerUdp? _listen;     // Gastgeberseite, fester Port
    private PacketPeerUdp? _ask;        // Sucherseite, beliebiger Port

    public string Fault { get; private set; } = "";

    // ---- Zahlen, ohne die eine leere Liste nichts aussagt -------------------

    public int Asked { get; private set; }
    public int Answered { get; private set; }
    public int Ignored { get; private set; }
    public bool SawLoopback { get; private set; }
    public bool SawBroadcast { get; private set; }

    // ================= Gastgeberseite ========================================

    /// <summary>
    /// Auf Fragen horchen. Gibt false zurück, wenn der Port nicht zu binden ist —
    /// das ist ein echter Fehler und wird gemeldet, nicht verschwiegen.
    ///
    /// <para>⚠ Häufigster Grund für ein Scheitern: ein <b>zweiter Gastgeber auf
    /// derselben Maschine</b>. Dann hat der erste den Port, und der zweite ist
    /// im LAN nicht zu finden — er ist aber sehr wohl spielbar, wenn man seine
    /// Adresse eintippt. Genau das sagt die Meldung.</para>
    /// </summary>
    public bool Listen()
    {
        if (_listen != null) return true;
        var u = new PacketPeerUdp();
        Error e = u.Bind(Port, "*", 8192);
        if (e != Error.Ok)
        {
            Fault = $"Suchport {Port} lässt sich nicht binden ({e}) — diese Partie ist im " +
                    "LAN nicht zu FINDEN. Spielbar bleibt sie: der Mitspieler kann die " +
                    "Adresse eintippen. Häufigster Grund: ein zweiter Gastgeber auf " +
                    "derselben Maschine hat den Port schon.";
            GD.PrintErr("suche: " + Fault);
            return false;
        }
        _listen = u;
        GD.Print($"suche: horche auf Port {Port} — diese Partie ist im LAN zu finden");
        return true;
    }

    /// <summary>
    /// Fragen beantworten. ⚠ Muss regelmässig gerufen werden, solange der
    /// Gastgeber wartet; er tut das aus <c>NetworkManager._Process</c>. Das
    /// Abfragen des Sockels berührt keinen Simulationszustand und darf deshalb
    /// auf Bildzeit laufen (Regel 8).
    /// </summary>
    public void Serve(NetOffer mine)
    {
        if (_listen == null) return;
        while (_listen.GetAvailablePacketCount() > 0)
        {
            byte[] q = _listen.GetPacket();
            string from = _listen.GetPacketIP();
            int fromPort = _listen.GetPacketPort();

            var r = new NetReader(q).Begin();
            if (r.Kind != NetPacket.Frage) { Ignored++; continue; }
            int proto = r.I32();
            if (r.Bad || proto != Protokoll) { Ignored++; continue; }

            // ⚠ Die Antwort geht als EINZELSENDUNG zurück, nicht als zweiter
            // Rundruf: der Fragende hat seinen Port mitgeschickt, indem er von
            // ihm aus gefragt hat.
            _listen.SetDestAddress(from, fromPort);
            _listen.PutPacket(mine.ToPacket());
            Answered++;
            GD.Print($"suche: Frage von {from}:{fromPort} beantwortet — {mine.Describe()}");
        }
    }

    public void StopListening()
    {
        _listen?.Close();
        _listen = null;
    }

    // ================= Sucherseite ===========================================

    /// <summary>
    /// Einen Rundruf abschicken. Bindet einen beliebigen freien Port, damit die
    /// Antwort ein Ziel hat, und schickt die Frage an das LAN <b>und</b> an die
    /// eigene Maschine.
    /// </summary>
    public bool Ask()
    {
        Close();
        var u = new PacketPeerUdp();
        // Port 0 = »irgendeinen freien«. ⚠ Das ist der Kunstgriff, der zwei
        // Prozesse auf einer Maschine möglich macht, ohne SO_REUSEADDR: nur der
        // Gastgeber belegt einen festen Port.
        Error e = u.Bind(0, "*", 8192);
        if (e != Error.Ok)
        {
            Fault = $"Suche: kein Port zu binden ({e})";
            GD.PrintErr("suche: " + Fault);
            return false;
        }
        u.SetBroadcastEnabled(true);
        _ask = u;
        Fault = "";
        Asked = Answered = Ignored = 0;
        SawLoopback = SawBroadcast = false;
        _found.Clear();

        byte[] q = new NetWriter(NetPacket.Frage).I32(Protokoll).Done();
        foreach (string dest in new[] { "255.255.255.255", "127.0.0.1" })
        {
            u.SetDestAddress(dest, Port);
            if (u.PutPacket(q) == Error.Ok) Asked++;
            else GD.PrintErr($"suche: Frage an {dest}:{Port} ging nicht hinaus");
        }
        GD.Print($"suche: Rundruf an 255.255.255.255:{Port} und 127.0.0.1:{Port} " +
                 $"({Asked} von 2 Wegen)");
        return Asked > 0;
    }

    private readonly List<NetOffer> _found = new();

    /// <summary>Die gefundenen Partien, in der Reihenfolge des Eintreffens.</summary>
    public IReadOnlyList<NetOffer> Found => _found;

    /// <summary>Antworten einsammeln. ⚠ Muss wiederholt gerufen werden — eine
    /// Suche ist keine Frage mit einer Antwort, sondern ein Fenster, in dem
    /// mehrere kommen können.</summary>
    public void Collect()
    {
        if (_ask == null) return;
        while (_ask.GetAvailablePacketCount() > 0)
        {
            byte[] a = _ask.GetPacket();
            string from = _ask.GetPacketIP();
            var o = NetOffer.FromPacket(a, from);
            if (o == null) { Ignored++; continue; }

            // ⚠ Über welchen Weg kam sie? 127.0.0.1 heisst: dieselbe Maschine.
            // Alles andere heisst: das LAN hat den Rundruf zugestellt. Der
            // Unterschied ist die Aussage des Prüfstands.
            if (from.StartsWith("127.")) { SawLoopback = true; o.Via = "127.0.0.1"; }
            else { SawBroadcast = true; o.Via = "LAN-Rundruf"; }

            // Dieselbe Partie kann über BEIDE Wege antworten — dann ist sie
            // einmal zu zeigen, nicht zweimal.
            bool dup = false;
            foreach (var have in _found)
                if (have.Port == o.Port && (have.Address == o.Address || from.StartsWith("127.")))
                { dup = true; break; }
            if (dup) { Ignored++; continue; }

            _found.Add(o);
            GD.Print($"suche: gefunden — {o.Describe()} (über {o.Via})");
        }
    }

    public void Close()
    {
        _ask?.Close();
        _ask = null;
    }

    /// <summary>
    /// DAS ERGEBNIS IN WORTEN — und es sagt auch, was es NICHT belegt.
    ///
    /// <para>⚠ Der ganze Sinn dieser Methode: eine leere Liste darf nicht
    /// schweigen. »Nichts gefunden« und »die Firewall hat den Rundruf gefressen«
    /// sehen über UDP gleich aus, und wer das nicht dazuschreibt, lässt den
    /// Spieler eine Stunde nach einem Fehler suchen, den es nicht gibt — oder
    /// umgekehrt.</para>
    /// </summary>
    public string Verdict(int ms)
    {
        if (Fault.Length > 0) return "Suche gescheitert: " + Fault;
        if (_found.Count == 0)
            return $"Keine offene Partie im LAN gefunden ({ms} ms gesucht, " +
                   $"{Asked} von 2 Wegen abgeschickt). Entweder läuft keine — oder der " +
                   "Rundruf kommt nicht durch (Firewall). ⚠ Über UDP ist das nicht zu " +
                   "unterscheiden: ein Rundruf, den niemand hört, meldet sich nicht.";
        string wege = SawBroadcast && SawLoopback ? "LAN-Rundruf und Rückschleife"
                    : SawBroadcast ? "LAN-Rundruf"
                    : "NUR die Rückschleife (127.0.0.1) — im LAN hat niemand geantwortet. " +
                      "Für zwei Fenster auf einem Rechner ist das richtig; sucht jemand " +
                      "einen anderen Rechner, ist die Firewall ein begründeter Verdacht";
        return $"{_found.Count} offene Partie(n) in {ms} ms gefunden, über {wege}.";
    }
}

/// <summary>
/// EINE OFFENE PARTIE, wie die Lobby sie zeigt: alles, was man zum Entscheiden
/// braucht, und nichts weiter.
///
/// <para>⚠ <b>Freie Plätze sind eine gerechnete Zahl, keine gemeldete.</b>
/// <c>Frei = Menschen − (Gastgeber + Beigetretene)</c>, und beim Gastgeber steht
/// sie auf 0, sobald die Partie verteilt ist. Eine Zahl, die nach dem Beitreten
/// gleich bliebe, wäre schlimmer als keine — sie sähe richtig aus.</para>
/// </summary>
public sealed class NetOffer
{
    public string Address = "";
    /// <summary>Der SPIELport (ENet), nicht der Suchport.</summary>
    public int Port;
    public string Map = "";
    public string Host = "";
    /// <summary>Wieviele Menschen die Partie haben soll.</summary>
    public int Players;
    /// <summary>Wieviele schon da sind, den Gastgeber eingeschlossen.</summary>
    public int Here;
    /// <summary>Läuft sie schon? Dann ist nichts mehr frei.</summary>
    public bool Running;
    /// <summary>Über welchen Weg die Antwort kam — nur für den Bericht.</summary>
    public string Via = "";

    public int Free => Running ? 0 : Players - Here < 0 ? 0 : Players - Here;

    public string Target => $"{Address}:{Port}";

    public byte[] ToPacket()
        => new NetWriter(NetPacket.Angebot)
            .I32(NetDiscovery.Protokoll)
            .U16(Port)
            .Text(Map)
            .Text(Host)
            .U8((byte)Players)
            .U8((byte)Here)
            .Bool(Running)
            .Done();

    /// <summary>⚠ Die Adresse kommt NICHT aus dem Paket, sondern aus dem
    /// Absender. Ein Gastgeber, der seine eigene Adresse mitschickt, schickt die,
    /// die er für seine hält — und das ist bei mehreren Netzadaptern die falsche.
    /// Wer geantwortet hat, weiss der Sockel besser.</summary>
    public static NetOffer? FromPacket(byte[] bytes, string from)
    {
        var r = new NetReader(bytes).Begin();
        if (r.Kind != NetPacket.Angebot) return null;
        int proto = r.I32();
        var o = new NetOffer
        {
            Address = from,
            Port = r.U16(),
            Map = r.Text(),
            Host = r.Text(),
            Players = r.U8(),
            Here = r.U8(),
            Running = r.Bool(),
        };
        if (r.Bad || proto != NetDiscovery.Protokoll || o.Port <= 0) return null;
        return o;
    }

    /// <summary>
    /// DIE KURZE FORM FÜR DIE LISTE IM BILD — und sie ist am Bild entschieden.
    ///
    /// <para>⚠ Zuerst stand hier <see cref="Describe"/>, dieselbe Zeile wie im
    /// Protokoll. Im Bildschirmfoto (<c>scratchpad/mp-lobby-liste.png</c>) stand
    /// davon »192.168.1.79:27015  map_NET05  1/3 Menschen, …« — der Netzkasten
    /// ist 400 px breit, und abgeschnitten war genau die Zahl, um die es geht:
    /// die freien Plätze. Eine Liste, die das Entscheidende wegkürzt, ist keine
    /// Entscheidungshilfe.</para>
    ///
    /// <para>Also kurz: das <c>map_</c> weg (es steht vor jedem Namen und
    /// unterscheidet nichts), der Rechnername weg — der steht im Tooltip, also
    /// in der langen Form, und dort geht nichts verloren.</para>
    /// </summary>
    public string ListLine()
    {
        string m = Map.StartsWith("map_") ? Map["map_".Length..] : Map;
        return $"{Target}  ·  {(m.Length > 0 ? m : "?")}  ·  " +
               (Running ? "läuft schon" : $"{Here}/{Players}, {Free} frei");
    }

    /// <summary>Die lange Form: fürs Protokoll und als Tooltip an der
    /// Listenzeile, damit die Kürzung im Bild nichts kostet.</summary>
    public string Describe()
        // ⚠ Ist die Adresse leer, ist das KEIN Fehler: der Gastgeber kennt sie
        // nicht und soll sie nicht kennen (siehe FromPacket). Er druckt sein
        // eigenes Angebot ins Protokoll, und dort stand vorher »:27015«.
        => $"{(Address.Length > 0 ? Target : $"(diese Maschine):{Port}")}  " +
           $"{(Map.Length > 0 ? Map : "?")}  " +
           (Running ? "läuft schon" : $"{Here}/{Players} Menschen, {Free} frei") +
           (Host.Length > 0 ? $"  »{Host}«" : "");
}
