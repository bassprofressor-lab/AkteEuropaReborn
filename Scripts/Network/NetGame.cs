namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Network;
using AkteEuropaReborn.Simulation;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// DER TAKTGEBER DES NETZSPIELS — und der Prüfstand dafür ist derselbe Code.
///
/// <para><b>Was dieser Knoten tut.</b> Er nimmt der Simulation den Takt aus der
/// Hand (<c>SetProcess(false)</c>) und gibt ihn ihr einzeln zurück — aber nur,
/// wenn der Takt <b>frei</b> ist, also jeder Mitspieler sein Taktpaket für ihn
/// geschickt hat. Das ist die Sperre <c>[0x4F6F28]</c> des Originals
/// (@0x415039), nur dass wir sie selbst ausrechnen. Solange sie hält, steht die
/// Simulation still; das Bild läuft weiter.</para>
///
/// <para>⚠ <b>Warum der Taktgeber die Simulation übernimmt und nicht in
/// <c>MapEntityLayer._Process</c> eingreift.</b> Weil <c>_Process</c> einer
/// anderen Hand gehört — und weil es der Bauform des Hauses entspricht:
/// <c>DeterminismTwinRunner</c> und <c>CommandCheckRunner</c> takten ihre
/// Simulationen genauso von aussen (<c>_a._Process(_dt)</c>). Dass die
/// Übernahme wirklich einen Takt und genau einen auslöst, wird <b>gemessen</b>
/// und nicht angenommen: nach jedem Aufruf muss
/// <see cref="MapEntityLayer.CommandTicks"/> um genau 1 gestiegen sein, sonst
/// bricht der Lauf mit der Zahl ab (Regel 11 — nachsehen, ob der Schalter noch
/// angeschlossen ist).</para>
///
/// <para><b>Der Weg eines Klicks, vollständig.</b></para>
/// <code>
///   Maus ──► MapViewer._UnhandledInput ──► MapEntityLayer.PostMove
///                                            │ (der Absender: rechnet, liest,
///                                            │  fasst den Zustand NICHT an)
///                                            ▼
///                                     CommandSink  ─── UNSER AUSGANGSKORB
///                                            │
///        am Anfang von Takt t:               ▼
///        NetLink.TurnPacket(ich, t+Vorlauf, Korb)  ──► ENet ──► Vermittler ──► alle
///                                            │
///                                            └─► NetLink.Take(dieselben Bytes)
///                                                   (auch für uns selbst: der
///                                                    eigene Befehl nimmt den
///                                                    Weg des Empfangs, wie
///                                                    post() @0x4C1C50)
///        am Anfang von Takt t+Vorlauf:
///        NetLink.TurnRecords ──► MapEntityLayer.PostRaw ──► CommandRing
///                                            │
///                                     SimTick ──► CommandTick ──► ApplyDue(t')
///                                            │
///                                            ▼  ApplyCommand: DER BEHANDLER
///                                               (auf JEDER Maschine, gleicher
///                                                Takt, gleicher Satz)
/// </code>
///
/// <para><b>Die Schalter</b>:</para>
/// <list type="bullet">
///   <item><c>--net-check=&lt;takte&gt;</c> — so viele Takte spielen, dann mit
///     Urteil beenden. Ohne diesen Schalter läuft ein <b>gespieltes</b> Netzspiel
///     (Wanduhr statt Vollgas).</item>
///   <item><c>--net-abdruck=&lt;takte&gt;</c> — so oft die Prüfsumme
///     vergleichen, Vorgabe 60 (also je Sekunde).</item>
///   <item><c>--net-befehl=&lt;takte&gt;</c> — so oft der Prüfstand SELBST einen
///     Bewegungsbefehl absetzt, Vorgabe 60. ⚠ 0 schaltet ihn ab — dann geht
///     ausser den leeren Taktpaketen NICHTS über die Leitung, und der Lauf
///     belegt nur, dass zwei gleiche Simulationen gleich bleiben.</item>
///   <item><c>--net-schluck=&lt;takt&gt;</c> — <b>DIE GEGENPROBE</b>: in diesem
///     Takt wird ein Satz des ANDEREN verschluckt. Der Abdruckvergleich MUSS
///     anschlagen und die Stelle nennen.</item>
///   <item><c>--net-frist=&lt;ms&gt;</c> — so lange auf einen fehlenden Takt
///     warten, dann abbrechen. Vorgabe 15000. Das Original meldet nach
///     <b>5000 ms</b> »Warte auf Server« (0x1388 @0x41504E) und hält je
///     Spielerplatz eine Frist von <b>2000 ms</b> (0x7D0 @0x4C5B78) — beides
///     wird hier gemeldet, wenn es erreicht ist.</item>
///   <item><c>--net-out=&lt;pfad&gt;</c> — Protokoll auch in eine Datei.</item>
/// </list>
///
/// <para><b>Aufruf, zwei Prozesse</b> (⚠ <c>DOTNET_gcConcurrent=0</c> ist
/// Pflicht, Begründung in <c>DeterminismTwinRunner.WarnAboutGc</c>):</para>
/// <code>
///   A: … --headless --path . -- --skirmish=map_NET02,0,normal,1 --no-briefing \
///          --net-host=27015 --net-spieler=2 --net-check=600
///   B: … --headless --path . -- --skirmish=map_NET02,0,normal,2 --no-briefing \
///          --net-join=127.0.0.1:27015 --net-check=600
/// </code>
/// </summary>
public sealed partial class NetGameRunner : Node
{
    private MapEntityLayer _sim = null!;
    private NetLink _link = null!;
    private NetSession _s = null!;

    /// <summary>⚠ Einer je Prozess, aus demselben Grund wie bei den beiden
    /// anderen Prüfständen: der Einhänger sitzt in <c>DeterminismTick</c>, also
    /// in JEDER <see cref="MapEntityLayer"/>.</summary>
    internal static bool Active { get; private set; }

    private enum Phase { Vorlauf, Laufen, Melden, Ausklang, Ende }
    private Phase _phase = Phase.Vorlauf;

    /// <summary>
    /// ⚠ DER AUSKLANG — und er ist gemessen, nicht vorsorglich.
    ///
    /// <para>Im ersten Zwei-Prozess-Lauf (120 Takte, map_NET07) meldete der
    /// Mitspieler <c>DURCH</c> und der Vermittler blieb bei Takt 124 stehen:
    /// »es fehlt Spieler 1«, 15011 ms gewartet, ROT. Beide waren sich bis Takt
    /// 120 Zahl für Zahl einig (2F141FDAA8C723EA und F5354E15751C61D8 auf beiden
    /// Seiten) — der Fehler lag nicht im Spiel, sondern im Aufhören: wer zuerst
    /// fertig ist, hört auf zu senden, und der andere wartet dann auf ein
    /// Taktpaket, das nie mehr kommt. Der schnellere Prozess hatte 124 Pakete
    /// abgeschickt, der langsamere 121 empfangen; die restlichen gingen beim
    /// <c>Close()</c> verloren.</para>
    ///
    /// <para>Also: wer fertig ist, redet noch eine Weile weiter. Er simuliert
    /// nicht mehr, er meldet nur »ich habe für diesen Takt nichts« — was wahr
    /// ist — damit der andere seine letzten Takte fahren kann. Das Original hat
    /// dasselbe Problem und dieselbe Antwort: es hält je Spielerplatz eine Frist
    /// von 2000 ms (@0x4C5B78) und meldet erst nach 5000 ms »Warte auf
    /// Server«.</para>
    /// </summary>
    private const int AusklangMs = 2500;
    private ulong _ausklangSince;
    private bool _ausklangOk;
    private int _ausklangTick;

    private int _tick, _start, _limit, _every = 60, _order = 60, _swallow = -1;
    private int _fristMs = 15000;
    private bool _swallowed;
    private double _acc;
    private ulong _stallSince, _stallSaid;
    private int _desyncTick = -1;
    private ulong _ownDesyncHash, _otherDesyncHash;
    private int _otherDesyncPlayer = -1;
    private Godot.FileAccess? _log;

    /// <summary>Was hier hineingelegt wird, geht im nächsten Taktpaket über die
    /// Leitung. ⚠ Es geht NICHT in den Ring — das tut erst die Aufnahme.</summary>
    private readonly List<CommandRecord> _out = new();

    /// <summary>Die eigenen Abdrücke der letzten Prüftakte. Die Prüfsumme des
    /// Gegenübers kommt später an als die eigene entsteht, und für die STELLE
    /// braucht es den ganzen Auszug — also muss er ein paar Takte aufbewahrt
    /// werden. Vier reichen und kosten bei 8441 Zahlen etwa 270 KB.</summary>
    private readonly Dictionary<int, (ulong Hash, List<long> Snap)> _own = new();
    private const int KeepDigests = 4;

    /// <summary>⚠ Der Würfel des ABSENDERS, und er ist absichtlich <b>nicht</b>
    /// der von <see cref="Determinism"/>. Ein Absender darf würfeln, weil sein
    /// Ergebnis im Satz mitreist und danach auf allen Maschinen dieselbe Zahl
    /// ist (siehe <c>CommandBridge.PostMove</c>). Würde er den gekeimten Strom
    /// anfassen, zöge jede Maschine verschieden viele Zahlen daraus — und genau
    /// das wäre ein Auseinanderlaufen, das der Prüfstand sich selbst gebaut
    /// hätte.</summary>
    private readonly Random _senderDice = new();

    private int _ticksSinceOrder;

    // ================= Anwerfen =============================================

    internal static bool TryStart(MapEntityLayer host)
    {
        if (Active) return true;
        if (!NetworkManager.Active) return false;

        var r = new NetGameRunner();
        string outPath = "";
        foreach (string a in Core.CommandLine.Args)
        {
            if (a.StartsWith("--net-check=")) int.TryParse(a["--net-check=".Length..], out r._limit);
            else if (a.StartsWith("--net-abdruck=")) int.TryParse(a["--net-abdruck=".Length..], out r._every);
            else if (a.StartsWith("--net-befehl=")) int.TryParse(a["--net-befehl=".Length..], out r._order);
            else if (a.StartsWith("--net-schluck=")) int.TryParse(a["--net-schluck=".Length..], out r._swallow);
            else if (a.StartsWith("--net-frist=")) int.TryParse(a["--net-frist=".Length..], out r._fristMs);
            else if (a.StartsWith("--net-out=")) outPath = a["--net-out=".Length..];
        }
        if (r._every < 1) r._every = 60;
        if (r._fristMs < 1000) r._fristMs = 1000;
        if (outPath.Length > 0) r._log = Godot.FileAccess.Open(outPath, Godot.FileAccess.ModeFlags.Write);

        Active = true;
        host.GetTree().Root.AddChild(r);
        r.CallDeferred(nameof(Begin), host);
        return true;
    }

    private void Say(string s)
    {
        GD.Print(s);
        if (_log == null) return;
        _log.StoreLine(s);
        _log.Flush();
    }

    private void Begin(MapEntityLayer host)
    {
        _sim = host;
        _link = NetworkManager.Link!;
        if (_link.Session == null)
        {
            Say("NETZ-FEHLER: es steht keine Partie — der Taktgeber hat nichts zu tun. " +
                "Vermutlich hat das Menü ohne Netzschalter gestartet.");
            Quit(2);
            return;
        }
        _s = _link.Session;

        // ⚠ AB HIER GEHÖRT DER TAKT UNS.
        _sim.SetProcess(false);
        _sim.SetPhysicsProcess(false);

        // Der Ausgangskorb: ein Klick wird ab jetzt NICHT mehr sofort in den
        // Ring gelegt, sondern erst über die Leitung geschickt. Das ist der
        // Unterschied zwischen »Puffer« und »Netzspiel«.
        _sim.CommandSink = c => { _out.Add(c); return true; };

        Say($"# Netzspiel: {(_link.IsHost ? "VERMITTLER" : "MITSPIELER")}, {_s}");
        Say($"#   Vorlauf {_s.Lead} Takte = {_s.Lead * 1000.0 / 60.0:0.0} ms bei 60 Takten/s " +
            $"(UNSERE SETZUNG, Begründung im Quelltext von {nameof(NetGameRunner)})");
        Say($"#   Abdruck alle {_every} Takte, eigener Befehl alle " +
            $"{(_order > 0 ? _order + " Takte" : "— (KEIN Befehl: der Lauf belegt dann nur, " +
             "dass zwei gleiche Simulationen gleich bleiben)")}" +
            (_swallow >= 0 ? $", ⚠ GEGENPROBE: Satz verschlucken in Takt {_swallow}" : "") +
            (_limit > 0 ? $", Grenze {_limit} Takte" : ", gespielt (ohne Grenze)"));
        Say($"#   Prüfsumme des Anfangs {_sim.DeterminismChecksum():X16}, " +
            $"Keim {Determinism.Seed}, Einheiten in der Zahlenreihe {SnapOf().Count}");

        // ---- der gemeinsame Anfang -----------------------------------------
        //
        // ⚠ Beide Prozesse haben, wenn dieser Aufruf kommt, schon Takte hinter
        // sich: der Einhänger sitzt am ENDE des ersten _Process, und wie viele
        // Takte dieses erste Bild gefahren hat, hängt an der Bildzeit. Wer bei
        // Takt 1 anfängt, während der andere bei Takt 3 steht, hat nicht
        // dieselbe Partie — und der Unterschied sähe aus wie ein Rechenfehler.
        // Also: jeder sagt, wie weit er ist, alle nehmen das MAXIMUM, und die
        // Zurückgebliebenen holen auf. Aufholen ist erlaubt, weil in diesen
        // Takten keine Befehle liegen KÖNNEN — es gab noch keine Leitung.
        byte[] hs = NetLink.HeadStartPacket(_s.Me, _sim.CommandTicks);
        _link.SendAll(hs);
        _link.Take(hs);
        Say($"Vorlauf: meine Simulation ist bei Takt {_sim.CommandTicks}, warte auf die anderen");
        _stallSince = Time.GetTicksMsec();
    }

    private List<long> SnapOf()
    {
        var l = new List<long>();
        _sim.DeterminismSnapshot(l);
        return l;
    }

    // ================= Der Takt =============================================

    public override void _Process(double delta)
    {
        if (_phase == Phase.Ende || _sim == null) return;
        _link.Pump();

        foreach (string v in _link.Verdicts) Say("NETZ-URTEIL des Gegenübers: " + v);
        _link.Verdicts.Clear();

        switch (_phase)
        {
            case Phase.Vorlauf: Bootstrap(); break;
            case Phase.Laufen: Run(delta); break;
            case Phase.Melden: WaitForOtherSnapshot(); break;
            case Phase.Ausklang: Ausklang(); break;
        }
    }

    /// <summary>Nicht mehr rechnen, aber weiter melden — siehe
    /// <see cref="AusklangMs"/> für den Befund, der das erzwungen hat.</summary>
    private void Ausklang()
    {
        // Ist das Gegenüber schon weg, gibt es nichts mehr nachzuschicken.
        if (_link.Status != Godot.MultiplayerPeer.ConnectionStatus.Connected)
        {
            Say("Ausklang: die Gegenseite ist weg — es gibt nichts mehr nachzuschicken.");
            Finish(_ausklangOk);
            return;
        }
        // Weiter leere Taktpakete: der andere darf seine letzten Takte fahren.
        for (int i = 0; i < 4; i++) SendTurn(_ausklangTick++);
        if (Time.GetTicksMsec() - _ausklangSince < (ulong)AusklangMs) return;
        Say($"Ausklang: {AusklangMs} ms lang leere Taktpakete nachgeschickt " +
            $"(bis Takt {_ausklangTick}), damit das Gegenüber seine letzten Takte " +
            $"fahren konnte. {_link.Numbers()}");
        Finish(_ausklangOk);
    }

    /// <summary>Alle Vorläufe einsammeln, auf den gemeinsamen Takt aufholen, die
    /// ersten <c>Lead</c> Taktpakete (leer) abschicken — und dann läuft es.</summary>
    private void Bootstrap()
    {
        if (!_link.AllHeadStarts(_s.Players, out int common))
        {
            Stall($"Vorlauf, es fehlt noch jemand ({_link.HeadStartLine(_s.Players)})");
            return;
        }

        int mine = _sim.CommandTicks;
        Say($"Vorlauf: {_link.HeadStartLine(_s.Players)} -> gemeinsamer Anfang Takt {common}" +
            (mine < common ? $"; ich hole {common - mine} Takt(e) auf" : "; ich bin schon da"));
        for (int t = mine; t < common; t++)
            if (!OneTick(nothing: true)) return;

        _tick = _start = common;

        // Die ersten Lead Takte sind schon entschieden: für sie kann niemand
        // mehr einen Befehl haben, denn erst ab jetzt reden wir. Also gehen sie
        // leer hinaus — sonst wartete jeder auf jeden.
        for (int t = _tick; t < _tick + _s.Lead; t++) SendTurn(t);

        Say($"Anfang: Takt {_tick}, Prüfsumme {_sim.DeterminismChecksum():X16}, " +
            $"{_link.Numbers()}");
        _phase = Phase.Laufen;
        _stallSince = 0;
        _stallSaid = 0;
    }

    /// <summary>Das eigene Taktpaket für einen Takt: hinausschicken UND selbst
    /// aufnehmen, aus denselben Bytes.</summary>
    private void SendTurn(int tick)
    {
        byte[] b = _link.TurnPacket(_s.Me, tick, _out, tick);
        _link.SendAll(b);
        _link.Take(b);
        _out.Clear();
    }

    private void Run(double delta)
    {
        // Im Prüflauf mit Vollgas: der Lauf soll die Leitung messen, nicht die
        // Wanduhr. Im gespielten Spiel läuft die Simulation nach der Uhr, und
        // ein Rückstand wird zügig, aber nicht unbegrenzt aufgeholt.
        int budget;
        if (_limit > 0) budget = 256;
        else
        {
            _acc += delta;
            budget = 0;
            while (_acc >= SimSeconds && budget < 240) { budget++; _acc -= SimSeconds; }
            if (budget == 0) return;
        }

        for (int k = 0; k < budget; k++)
        {
            if (!_link.TurnReady(_tick, _s.Players))
            {
                Stall($"Takt {_tick} ist nicht frei — es fehlt Spieler " +
                      $"{_link.TurnMissing(_tick, _s.Players)}");
                return;
            }
            _stallSince = 0;
            _stallSaid = 0;

            // 1) Das eigene Taktpaket für t + Vorlauf — VOR dem Takt, damit die
            //    Leitung die ganze Vorlaufzeit hat.
            SendTurn(_tick + _s.Lead);

            // 2) Die Sätze DIESES Takts in den Ring, in der festgelegten
            //    Reihenfolge (Spielernummer, dann Paketreihenfolge).
            Ingest(_tick);

            // 3) Ein Takt, und genau einer.
            if (!OneTick(nothing: false)) return;

            // 4) Der Abdruck.
            if (_tick % _every == 0) Digest();
            CheckDigests();
            if (_phase != Phase.Laufen) return;

            _link.ForgetBefore(_tick - 8);

            // 5) Der Prüfstand setzt selbst Befehle ab — sonst reist nichts.
            if (_order > 0 && ++_ticksSinceOrder >= _order) { _ticksSinceOrder = 0; OrderSomething(); }

            if (_limit > 0 && _tick - _start >= _limit) { Verdict(); return; }
        }
    }

    /// <summary>⚠ Die Taktlänge. Sie steht in <c>MapEntityLayer</c> als
    /// <c>private const SimDt = 1f/60</c> und ist von hier nicht lesbar —
    /// deshalb wird sie nicht angenommen, sondern <b>nachgeprüft</b>: schlägt
    /// ein Aufruf mit diesem Wert nicht genau einen Takt an, bricht
    /// <see cref="OneTick"/> mit der Zahl ab.</summary>
    private const double SimSeconds = 1.0 / 60.0;

    private bool OneTick(bool nothing)
    {
        int before = _sim.CommandTicks;
        _sim._Process(SimSeconds);
        int ran = _sim.CommandTicks - before;
        if (ran != 1)
        {
            Say($"NETZ-ABBRUCH: ein Aufruf mit dt={SimSeconds:0.000000} s hat {ran} Takte " +
                "ausgelöst, nicht genau einen. Die Taktlänge dieses Taktgebers passt nicht " +
                "mehr zu MapEntityLayer.SimDt (SimHz geändert?). Ein Netzspiel mit einem " +
                "Taktgeber, der nicht weiss wie lang ein Takt ist, läuft garantiert " +
                "auseinander — deshalb wird hier abgebrochen und nicht geraten.");
            Quit(4);
            return false;
        }
        if (!nothing) _tick++;
        return true;
    }

    /// <summary>
    /// Die Sätze eines Takts in den Ring legen. <b>Die einzige Stelle, an der ein
    /// Befehl aus dem Netz in den Zustand gelangt</b> — und sie legt ihn nicht
    /// an, sie legt ihn HIN: gewirkt wird er von <c>CommandTick</c> am
    /// Taktanfang, wie jeder andere auch.
    /// </summary>
    private void Ingest(int tick)
    {
        int n = 0, ate = 0;
        foreach (var c in _link.TurnRecords(tick, _s.Players))
        {
            var r = c;
            r.Due = tick;
            // DIE GEGENPROBE: einen Satz des ANDEREN verschlucken. Kleiner kann
            // ein Netzfehler nicht sein — ein Paket kommt an, ein Satz darin
            // wirkt nicht. Wenn der Abdruckvergleich DAS findet, findet er jedes
            // Auseinanderlaufen.
            if (_swallow >= 0 && !_swallowed && tick == _swallow && r.Player != _s.MySlot)
            {
                _swallowed = true;
                ate++;
                Say($"⚠ GEGENPROBE in Takt {tick}: ein Satz des Gegenübers wird VERSCHLUCKT " +
                    $"— {r.Describe()}");
                continue;
            }
            if (_sim.PostRaw(r)) n++;
        }
        if (n > 0 || ate > 0)
            Say($"   Takt {tick}: {n} Satz/Sätze in den Ring{(ate > 0 ? $", {ate} verschluckt" : "")}");
    }

    // ================= Der Abdruck ==========================================

    private void Digest()
    {
        var snap = SnapOf();
        ulong h = _sim.DeterminismChecksum();
        _own[_tick] = (h, snap);
        if (_own.Count > KeepDigests)
        {
            int oldest = int.MaxValue;
            foreach (int k in _own.Keys) if (k < oldest) oldest = k;
            _own.Remove(oldest);
        }
        _link.SendAll(NetLink.DigestPacket(_s.Me, _tick, h));
        var rep = _sim.CommandReport();
        Say($"NETZ t={_tick,6} abdruck={h:X16} zahlen={snap.Count} " +
            $"befehle={rep.Applied} befehlsumme={rep.Digest:X16} | {_link.Numbers()}");
        SayAi();
    }

    /// <summary>
    /// WAS DIE COMPUTERSPIELER IN DIESEM LAUF GETAN HABEN — und warum das hier
    /// stehen muss.
    ///
    /// <para>Der erste Zwei-Prozess-Lauf mit <c>KI 2</c> war grün: 600 Takte,
    /// an jedem Prüftakt dieselbe Zahl, und die beiden Simulationen hatten
    /// dabei sogar verschiedene Sichtspieler (Platz 0 gegen Platz 1). Das sieht
    /// nach einem Beleg für »die KI ist im Netz unbedenklich« aus — und war
    /// keiner, weil im ganzen Protokoll <b>keine einzige Zeile über die KI</b>
    /// stand. Ob sie in diesen 600 Takten überhaupt etwas entschieden hat, war
    /// aus dem Lauf nicht zu erkennen; ein Lauf, in dem die KI nur dasteht,
    /// meldet dieselbe grüne Zahl (Regel 9: erst fragen, welche Fehlerklasse
    /// der Prüfstand sehen kann, dann das Grün zählen).</para>
    ///
    /// <para><see cref="MapEntityLayer.AiLine"/> gibt es längst; sie wurde hier
    /// nur nie gefragt. Ihre Zahlen (Armee, Welle, Gebaut, Angriffe, Greifer,
    /// Genommen) stammen aus dem KI-Zustand und sind damit zugleich die
    /// direkte Aussage, auf die es ankommt: <b>stehen auf beiden Maschinen
    /// dieselben Zahlen</b>, hat die KI dort dieselben Entscheidungen
    /// getroffen. Stehen überall Nullen, war der Lauf blind, und das steht dann
    /// im Protokoll statt in niemandes Kopf.</para>
    /// </summary>
    private void SayAi()
    {
        string ai = _sim.AiLine();
        Say(ai.Length > 0 ? "   " + ai : "   KI: kein Computerspieler in dieser Partie");
    }

    /// <summary>
    /// Fremde Prüfsummen gegen die eigenen halten. ⚠ Eine, für die wir unsere
    /// eigene noch nicht haben, bleibt LIEGEN — sonst würde sie verschluckt und
    /// der Vergleich schwiege, statt zu prüfen.
    /// </summary>
    private void CheckDigests()
    {
        for (int i = _link.Digests.Count - 1; i >= 0; i--)
        {
            var (t, p, h) = _link.Digests[i];
            if (!_own.TryGetValue(t, out var mine))
            {
                // schon vorbei und nicht mehr aufbewahrt: das ist selbst ein
                // Befund, denn dann sind wir mehr als KeepDigests Prüftakte
                // auseinander.
                if (t < _tick - KeepDigests * _every)
                {
                    Say($"⚠ Prüfsumme für Takt {t} von Spieler {p} kam zu spät (wir sind bei " +
                        $"{_tick}) — sie wird verworfen. Das heisst: die Leitung ist mehr als " +
                        $"{KeepDigests * _every} Takte im Rückstand.");
                    _link.Digests.RemoveAt(i);
                }
                continue;
            }
            _link.Digests.RemoveAt(i);
            if (mine.Hash == h) continue;

            _desyncTick = t;
            _ownDesyncHash = mine.Hash;
            _otherDesyncHash = h;
            _otherDesyncPlayer = p;
            Say($"AUSEINANDER bei Takt {t}: meine Prüfsumme {mine.Hash:X16}, " +
                $"Spieler {p} meldet {h:X16}");
            Say($"   ich frage nach der STELLE — der ganze Zahlenauszug geht hin und her " +
                $"({mine.Snap.Count} Zahlen). Eine Summe allein sagt nie, welches Glied es war.");
            _link.SendAll(NetLink.SnapshotPacket(_s.Me, t, mine.Snap));
            _phase = Phase.Melden;
            _stallSince = Time.GetTicksMsec();
            return;
        }
    }

    /// <summary>Auf den Zahlenauszug des Gegenübers warten und dann sagen, WO.
    /// Dieselbe Sprache wie <c>--determinism-twin</c> und
    /// <c>--befehl-poison</c>: Takt, Einheit, Feld, beide Werte.</summary>
    private void WaitForOtherSnapshot()
    {
        for (int i = 0; i < _link.Snaps.Count; i++)
        {
            if (_link.Snaps[i].Tick != _desyncTick) continue;
            var (t, p, other) = _link.Snaps[i];
            _link.Snaps.RemoveAt(i);
            var mine = _own[t].Snap;

            int first = DeterminismTwinRunner.FirstDiff(mine, other);
            if (first < 0)
            {
                Say($"⚠ RÄTSEL: die Prüfsummen für Takt {t} sind verschieden " +
                    $"({_ownDesyncHash:X16} vs {_otherDesyncHash:X16}), die Zahlenauszüge aber " +
                    $"gleich ({mine.Count} Zahlen). Dann liegt der Unterschied in etwas, das " +
                    "die Prüfsumme aufnimmt und der Auszug nicht — oder umgekehrt. Der " +
                    "Prüfstand kann in diesem Fall die Stelle NICHT nennen und sagt das.");
                FinishAfterAusklang(false);
                return;
            }

            Say($"DIE STELLE, Takt {t} (ich bin Spieler {_s.Me} auf Platz {_s.MySlot}, " +
                $"das Gegenüber Spieler {p}):");
            Say($"   {DeterminismTwinRunner.Describe(first, mine, other)}");
            int count = 0;
            var shown = new List<string>();
            int n = Math.Min(mine.Count, other.Count);
            for (int k = 0; k < n; k++)
            {
                if (mine[k] == other[k]) continue;
                count++;
                if (count <= 12) shown.Add(DeterminismTwinRunner.Describe(k, mine, other));
            }
            Say($"   {count} von {n} Zahlen verschieden{(count > 12 ? " (die ersten 12)" : "")}:");
            foreach (string s in shown) Say("     " + s);
            Say($"   zuletzt ausgeführter Befehl hier: {_sim.CommandReport().Last.Describe()}");
            Say($"   {_sim.CommandReport()}");
            _link.SendAll(NetLink.VerdictPacket(_s.Me, t,
                DeterminismTwinRunner.Describe(first, mine, other)));

            if (_swallow >= 0)
            {
                Say("⚠ DAS WAR DIE GEGENPROBE, und sie hat angeschlagen: der verschluckte " +
                    "Satz wurde gefunden, mit Takt, Einheit und Feld. Der Netzprüfstand ist " +
                    "damit nicht blind.");
                FinishAfterAusklang(true);
            }
            else FinishAfterAusklang(false);
            return;
        }

        if (Time.GetTicksMsec() - _stallSince > 5000)
        {
            Say($"⚠ der Zahlenauszug des Gegenübers für Takt {_desyncTick} ist in 5000 ms " +
                "nicht gekommen. Es steht damit fest, DASS wir auseinander sind " +
                $"({_ownDesyncHash:X16} vs {_otherDesyncHash:X16}), aber nicht WO — und das " +
                "ist der schlechtere von zwei Befunden.");
            FinishAfterAusklang(false);
        }
    }

    // ================= Der Prüfstand setzt selbst Befehle ab =================

    /// <summary>
    /// Einen echten Bewegungsbefehl für die EIGENEN Einheiten absetzen — auf
    /// demselben Weg, den ein Mausklick nimmt (<c>PostMove</c> über den
    /// Ausgangskorb).
    ///
    /// <para>⚠ <b>Warum das sein MUSS.</b> Zwei Simulationen, die nichts tun,
    /// bleiben gleich; ein Netzprüfstand ohne Befehle über der Leitung belegt
    /// nur das (Regel 7). Erst wenn Sätze reisen, prüft der Lauf die Leitung,
    /// die Reihenfolge, die Fälligkeit und den Behandler.</para>
    ///
    /// <para>Die Zielzelle wird vom ABSENDER gewürfelt, mit einem eigenen
    /// Würfel — das darf er (siehe <see cref="_senderDice"/>).</para>
    /// </summary>
    private void OrderSomething()
    {
        var units = _sim.CommandCheckMobileUnits(6);
        if (units.Count == 0)
        {
            if (_tick - _start < _order * 2)
                Say($"   ⚠ Takt {_tick}: ich habe auf Platz {_s.MySlot} keine fahrbereite " +
                    "Einheit — von MIR reist kein Befehl. Ein Lauf, in dem nur die andere " +
                    "Seite Befehle gibt, prüft die halbe Leitung.");
            return;
        }
        _sim.CommandCheckSelect(units);
        var cell = _sim.CommandCheckClickCell(units[0], 2 + _senderDice.Next(7));
        if (cell.X < 0) return;
        int n = _sim.PostMove(_sim.CellCenterFor(cell));
        Say($"   Takt {_tick}: {n} Satz/Sätze abgesetzt für Platz {_s.MySlot} " +
            $"({units.Count} Einheit(en) -> Zelle {cell.X},{cell.Y}), " +
            $"fällig in Takt {_tick + _s.Lead}");
    }

    // ================= Warten und Urteil ====================================

    /// <summary>Die Meldungen des Wartens — mit den Fristen des Originals.
    /// »Es hängt« ist keine Meldung.</summary>
    private void Stall(string what)
    {
        ulong now = Time.GetTicksMsec();
        if (_stallSince == 0) { _stallSince = now; return; }
        ulong waited = now - _stallSince;

        // 2000 ms: die Frist, die der Absender des Originals je Spielerplatz
        // setzt (GetTickCount()+0x7D0 @0x4C5B78).
        if (waited >= 2000 && _stallSaid < 2000)
        { _stallSaid = 2000; Say($"netz: warte {waited} ms — {what}"); }
        // 5000 ms: hier meldet das Original »Warte auf Server« (0x1388 @0x41504E).
        if (waited >= 5000 && _stallSaid < 5000)
        { _stallSaid = 5000; Say($"netz: »Warte auf Server« ({waited} ms) — {what}"); }

        if (waited >= (ulong)_fristMs)
        {
            Say($"NETZ-ABBRUCH: {waited} ms gewartet, Frist {_fristMs} ms. {what}");
            Say($"   {_link.Numbers()}, Verbindungszustand {_link.Status}");
            Finish(false);
        }
    }

    private void Verdict()
    {
        int ran = _tick - _start;
        var rep = _sim.CommandReport();
        Say($"NETZ-ENDE takte={ran} (Takt {_start}..{_tick}) abdruck={_sim.DeterminismChecksum():X16}");
        Say($"   {rep}");
        Say($"   {_link.Numbers()}");
        SayAi();

        if (_swallow >= 0)
        {
            if (!_swallowed)
                Say($"⚠ ABER: --net-schluck={_swallow} hat nie zugeschlagen — in diesem Takt " +
                    "kam kein Satz des Gegenübers an. Die Gegenprobe ging INS LEERE und " +
                    "belegt nichts.");
            else
                Say("⚠ ABER: ein Satz wurde verschluckt und der Abdruckvergleich hat es NICHT " +
                    "gefunden. Der Netzprüfstand ist blind — sein grünes Ergebnis würde " +
                    "nichts belegen.");
            FinishAfterAusklang(false);
            return;
        }

        if (rep.Applied == 0)
        {
            Say("⚠ ABER: es wurde in diesem ganzen Lauf KEIN Befehl ausgeführt. Dann belegt " +
                "das Ergebnis nur, dass zwei gleiche Simulationen gleich bleiben — nicht, " +
                "dass die Leitung trägt. Mit --net-befehl= und einer Karte, auf der beide " +
                "Plätze fahrbereite Einheiten haben, wiederholen.");
            FinishAfterAusklang(false);
            return;
        }

        Say($"DURCH: {ran} Takte im Netz, {rep.Applied} Befehle ausgeführt, " +
            "die Prüfsummen beider Seiten waren an jedem Prüftakt gleich.");
        FinishAfterAusklang(true);
    }

    /// <summary>Erst ausklingen lassen, dann urteilen — siehe
    /// <see cref="AusklangMs"/>.</summary>
    private void FinishAfterAusklang(bool ok)
    {
        _ausklangOk = ok;
        _ausklangSince = Time.GetTicksMsec();
        _ausklangTick = _tick + _s.Lead;
        _phase = Phase.Ausklang;
        Say(ok ? "NETZ-ERGEBNIS: grün. (Ausklang läuft noch — das Gegenüber " +
                 "braucht meine letzten Taktpakete)"
               : "NETZ-ERGEBNIS: ROT. (Ausklang läuft noch)");
    }

    private void Finish(bool ok)
    {
        _phase = Phase.Ende;
        Say(ok ? "NETZ-ERGEBNIS: grün." : "NETZ-ERGEBNIS: ROT.");
        Quit(ok ? 0 : 1);
    }

    private void Quit(int code)
    {
        _phase = Phase.Ende;
        _log?.Flush();
        _log?.Close();
        _log = null;
        NetworkManager.Link?.Close();
        GetTree().Quit(code);
    }
}
