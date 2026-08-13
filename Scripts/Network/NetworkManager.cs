namespace AkteEuropaReborn.Network;

using Godot;
using AkteEuropaReborn.Simulation;

/// <summary>
/// DER EINE ANSCHLUSS DES PROGRAMMS — und deshalb darf das hier ein Autoload
/// bleiben.
///
/// <para>⚠ <b>Warum diese Klasse ein Knoten ist und <c>CommandManager</c>
/// nicht.</b> Der Befehlsring ist ein Ding <b>je Simulation</b> — der
/// Zwillingsprüfstand fährt zwei im selben Prozess, der Befehlsprüfstand drei,
/// und ein geteilter Ring wäre genau der Fehler, den Stufe 1 an vier
/// Zufallsquellen gefunden hat. Eine <b>Steckdose</b> ist das Gegenteil: es gibt
/// genau eine je Prozess, sie muss den Szenenwechsel Menü → Karte überleben, und
/// sie muss abgefragt werden. Ein Autoload ist dafür die richtige Bauform, und
/// die Zeile stand ohnehin seit dem 19.07. in <c>project.godot</c> — sie wird
/// jetzt zum ersten Mal benutzt, ohne dass die Datei angefasst werden muss.</para>
///
/// <para><b>Was hier geschieht und was nicht.</b> Hier wird verbunden, die Partie
/// verteilt und die Steckdose abgefragt. Hier wird <b>nicht</b> getaktet und
/// <b>nichts</b> in den Befehlsring gelegt — das tut
/// <see cref="NetGameRunner"/> am Taktanfang. Die Trennung ist der Kern von
/// Regel 8: das Abfragen der Leitung läuft auf Bildzeit, das Einlegen in den Ring
/// auf Taktzeit.</para>
///
/// <para><b>Die Schalter</b> (alle UNSERE, das Original hat eine Oberfläche
/// dafür):</para>
/// <list type="bullet">
///   <item><c>--net-host[=&lt;port&gt;]</c> — Vermittler werden, Vorgabe 27015.</item>
///   <item><c>--net-join=&lt;adresse&gt;[:&lt;port&gt;]</c> — beitreten.</item>
///   <item><c>--net-spieler=&lt;n&gt;</c> — auf so viele Menschen warten, Vorgabe 2.</item>
///   <item><c>--net-lead=&lt;takte&gt;</c> — der Vorlauf, Vorgabe 6.</item>
///   <item><c>--net-keim=&lt;zahl&gt;</c> — den Keim der Partie erzwingen (nur
///     beim Vermittler; er verteilt ihn).</item>
///   <item><c>--net-warte=&lt;ms&gt;</c> — so lange auf die Partie warten, dann
///     abbrechen. Vorgabe 20000.</item>
/// </list>
/// </summary>
public partial class NetworkManager : Node
{
    /// <summary>Die Steckdose dieses Programms. <c>null</c>, solange niemand
    /// einen Netzschalter angegeben hat — dann kostet der ganze Netzcode ein
    /// <c>if</c> je Bild und sonst nichts.</summary>
    public static NetLink? Link { get; private set; }

    public static NetworkManager? Instance { get; private set; }

    /// <summary>Steht die Partie? Das Menü darf erst dann die Karte laden, denn
    /// der Keim gehört gesetzt, BEVOR <c>NavGrid.Build</c> ihn braucht.</summary>
    public static bool SessionReady => Link is { SessionReady: true };

    /// <summary>Läuft überhaupt ein Netzspiel?</summary>
    public static bool Active => Link != null;

    public static string Fault { get; private set; } = "";

    /// <summary>
    /// Die Kartenplätze, die von MENSCHEN geführt werden — auf jeder Maschine
    /// dieselbe Liste, denn sie kommt aus der Partie des Vermittlers.
    ///
    /// <para>⚠ Ohne Netzspiel leer, und das ist der Normalfall: dann bleibt in
    /// <c>StartSkirmish</c> alles wie bisher. Wozu sie gebraucht wird, steht
    /// dort — ohne sie steuert auf jeder Maschine ein ANDERER Platz vom Rechner,
    /// und das Netzspiel läuft im ersten Denk-Takt auseinander.</para>
    /// </summary>
    public static int[] HumanSlots()
        => Link?.Session?.Slot ?? System.Array.Empty<int>();

    // ---- die Schalter -------------------------------------------------------

    private bool _wantHost, _wantJoin;
    private int _port = 27015, _players = 2, _lead = 6, _waitMs = 20000;
    private string _address = "127.0.0.1";
    private uint _forcedSeed;
    /// <summary>⚠ <c>--net-ki=&lt;n&gt;</c>. Nötig, weil der Gefechtsschirm die
    /// Gegnerzahl auf <c>MinValue = 1</c> hält (MainMenu.cs:465) — über
    /// <c>--skirmish=karte,0,…</c> ist eine Partie OHNE Computerspieler also
    /// nicht erreichbar. Für den Netzprüfstand ist genau die aber die reine
    /// Messung: zwei Menschen, keine dritte Rechenquelle. Wo eine Lobby steht,
    /// gehört diese Zahl in sie.</summary>
    private int _netAi = -1;
    private ulong _t0;
    private bool _offered, _timedOut;

    public override void _Ready()
    {
        Instance = this;
        ReadSwitches();
        if (!_wantHost && !_wantJoin) { SetProcess(false); return; }

        Link = new NetLink();
        bool ok = _wantHost ? Link.HostOn(_port, 7) : Link.JoinTo(_address, _port);
        if (!ok)
        {
            Fault = Link.Fault;
            GD.PrintErr($"netz: Anschluss gescheitert — {Fault}");
            Link = null;
            SetProcess(false);
            return;
        }
        _t0 = Time.GetTicksMsec();
    }

    /// <summary>
    /// Die Steckdose aus dem MENÜ öffnen, nicht von der Befehlszeile.
    ///
    /// <para>⚠ Ruft der Gefechtsschirm, wenn dort »Gastgeber« oder »Beitreten«
    /// steht (UI/MainMenuNet.cs). Steht schon eine Verbindung — weil die
    /// Befehlszeile sie aufgebaut hat —, bleibt sie: eine zweite Steckdose je
    /// Prozess gibt es nicht, und das ist der ganze Grund, warum diese Klasse
    /// ein Autoload ist.</para>
    /// </summary>
    public static bool StartFromMenu(bool asHost, string address, int port, int players)
    {
        if (Instance == null) return false;
        if (Link != null) return true;
        var i = Instance;
        i._wantHost = asHost;
        i._wantJoin = !asHost;
        i._port = port > 0 ? port : 27015;
        i._players = players < 1 ? 1 : players;
        if (address.Length > 0) i._address = address;

        Link = new NetLink();
        bool ok = asHost ? Link.HostOn(i._port, 7) : Link.JoinTo(i._address, i._port);
        if (!ok)
        {
            Fault = Link.Fault;
            GD.PrintErr($"netz: Anschluss gescheitert — {Fault}");
            Link = null;
            return false;
        }
        i._t0 = Time.GetTicksMsec();
        i._timedOut = false;
        i.SetProcess(true);
        return true;
    }

    /// <summary>Was gerade zwischen den Rechnern los ist — eine Zeile für das
    /// Menü. ⚠ Ein Wartezustand, den man nicht sieht, ist von einem Absturz
    /// nicht zu unterscheiden.</summary>
    public static string StatusLine()
    {
        if (Link == null) return "Netzwerk aus";
        if (Link.Session is { } s)
            return $"Partie steht: {s.Map}, Keim {s.Seed}, mein Platz {s.MySlot} " +
                   $"von {s.Players} Menschen";
        if (Fault.Length > 0) return "FEHLER: " + Fault;
        return Link.IsHost
            ? $"Gastgeber: {Link.ClientCount + 1} von {Instance?._players ?? 2} da, " +
              $"warte auf Mitspieler"
            : $"Beitreten: {Link.Status}, warte auf die Partie des Gastgebers";
    }

    private void ReadSwitches()
    {
        foreach (string a in Core.CommandLine.Args)
        {
            if (a == "--net-host") _wantHost = true;
            else if (a.StartsWith("--net-host="))
            {
                _wantHost = true;
                if (int.TryParse(a["--net-host=".Length..], out int p) && p > 0) _port = p;
            }
            else if (a.StartsWith("--net-join="))
            {
                _wantJoin = true;
                string v = a["--net-join=".Length..];
                int c = v.LastIndexOf(':');
                if (c > 0 && int.TryParse(v[(c + 1)..], out int p) && p > 0)
                { _address = v[..c]; _port = p; }
                else if (v.Length > 0) _address = v;
            }
            else if (a.StartsWith("--net-spieler=")) int.TryParse(a["--net-spieler=".Length..], out _players);
            else if (a.StartsWith("--net-lead=")) int.TryParse(a["--net-lead=".Length..], out _lead);
            else if (a.StartsWith("--net-warte=")) int.TryParse(a["--net-warte=".Length..], out _waitMs);
            else if (a.StartsWith("--net-keim=")) uint.TryParse(a["--net-keim=".Length..], out _forcedSeed);
            else if (a.StartsWith("--net-ki=")) int.TryParse(a["--net-ki=".Length..], out _netAi);
        }
        if (_players < 1) _players = 1;
        if (_lead < 1) _lead = 1;
        if (_waitMs < 1000) _waitMs = 1000;
    }

    public override void _Process(double delta)
    {
        if (Link == null) return;
        Link.Pump();

        if (Link.SessionReady)
        {
            if (!_applied) ApplySession();
            return;
        }

        // ⚠ Erst anbieten, wenn das Menü seine Werte hingeschrieben hat
        // (Announce). Vorher wüsste der Vermittler nicht, welche Karte er
        // verteilt — er verteilte die Vorgabe, und der Mitspieler spielte
        // gehorsam die falsche.
        if (_wantHost && _announced && !_offered && Link.ClientCount + 1 >= _players)
            OfferSession();

        if (!_timedOut && Time.GetTicksMsec() - _t0 > (ulong)_waitMs)
        {
            _timedOut = true;
            Fault = _wantHost
                ? $"nach {_waitMs} ms sind nur {Link.ClientCount + 1} von {_players} " +
                  "Mitspielern da — die Partie kommt nicht zustande"
                : $"nach {_waitMs} ms keine Partie vom Gastgeber ({_address}:{_port}), " +
                  $"Verbindungszustand {Link.Status}";
            GD.PrintErr("netz: " + Fault);
        }
    }

    public static bool TimedOut => Instance is { _timedOut: true };

    private bool _announced;

    /// <summary>
    /// »Ich bin bereit, und das hier soll gespielt werden.« Ruft das Menü, wenn
    /// die Auswahlfelder in <see cref="UI.SkirmishSetup"/> stehen.
    ///
    /// <para>Beim Vermittler ist das der Anstoss zur Partie: von jetzt an darf er
    /// sie verteilen, sobald genug Mitspieler da sind. Beim Mitspieler setzt es
    /// nur die Wartefrist neu, damit sie ab dem Knopfdruck läuft und nicht ab dem
    /// Programmstart — ein Gastgeber, der noch im Menü sitzt, ist kein Fehler.</para>
    /// </summary>
    public static void Announce()
    {
        if (Instance == null || Link == null) return;
        Instance._announced = true;
        Instance._t0 = Time.GetTicksMsec();
        Instance._timedOut = false;
        GD.Print($"netz: angemeldet — {(Link.IsHost ? "Vermittler" : "Mitspieler")}, " +
                 $"Karte {UI.SkirmishSetup.Map}, " +
                 $"{(Link.IsHost ? $"warte auf {Instance._players - 1 - Link.ClientCount} " +
                                   "weitere(n) Mitspieler" : "warte auf die Partie")}");
    }

    // ---- die Partie ---------------------------------------------------------

    /// <summary>
    /// Der Vermittler legt fest, WAS gespielt wird. Die Karte und die
    /// Einstellungen kommen aus dem Gefechtsschirm bzw. von
    /// <c>--skirmish=</c> — der Keim aus <c>--net-keim=</c>,
    /// <c>--determinism-seed=</c> oder aus dem Kartennamen.
    ///
    /// <para>⚠ <b>Die Plätze.</b> Mensch Nr. 0 bekommt den Platz, den der
    /// Gastgeber gewählt hat; die weiteren bekommen aufsteigend die nächsten
    /// freien 0..7. Dass das eine dünne Regel ist, steht hier, damit es nicht
    /// aussieht wie eine gelesene: die Platzwahl ist im Original Sache des
    /// Aufbaubilds (Kommando 979 trägt die Einstellungen des Gastgebers ein,
    /// 981 startet), und ein richtiges Aufbaubild haben wir noch nicht.</para>
    /// </summary>
    private void OfferSession()
    {
        _offered = true;
        var s = new NetSession
        {
            Map = UI.SkirmishSetup.Map,
            Lead = _lead,
            AiCount = _netAi >= 0 ? _netAi : UI.SkirmishSetup.AiCount,
            Level = (int)UI.SkirmishSetup.Level,
            Techstandard = UI.SkirmishSetup.Techstandard,
            Resources = UI.SkirmishSetup.Resources,
            AllUnits = UI.SkirmishSetup.AllUnits,
        };

        Determinism.EnsureConfigured();
        uint seed = _forcedSeed != 0 ? _forcedSeed
                  : Determinism.Forced ?? Determinism.Fnv32(s.Map);
        if (seed == 0) seed = 0x9E3779B9;
        s.Seed = seed;

        int mine = UI.SkirmishSetup.Human;
        if (mine < 0) mine = 0;
        int n = 1 + Link!.ClientCount;
        var slots = new int[n];
        slots[0] = mine;
        int next = 0;
        for (int i = 1; i < n; i++)
        {
            while (next == mine) next++;
            slots[i] = next++;
        }
        s.Slot = slots;

        Link.OfferSession(s);
    }

    /// <summary>
    /// Die Partie noch einmal in die Einstellungen tragen — <b>nach</b> dem
    /// Gefechtsschirm, unmittelbar vor dem Szenenwechsel.
    ///
    /// <para>⚠ <b>Warum zweimal.</b> <c>MainMenu.OnStart</c> schreibt Karte,
    /// Platz, KI-Zahl, Stufe, Techstandard und Rohstoffe aus den Auswahlfeldern
    /// nach <see cref="UI.SkirmishSetup"/> — es MUSS das tun, sonst gäbe es keinen
    /// Einzelspieler. Im Netzspiel würde es damit aber die Partie des Vermittlers
    /// überschreiben, und zwar still: beide Rechner starteten, jeder auf seiner
    /// Karte und seinem Platz. Das ist kein Auseinanderlaufen, das man messen
    /// kann, sondern schlicht zwei verschiedene Partien.</para>
    ///
    /// <para>Ohne Netzschalter tut diese Methode nichts.</para>
    /// </summary>
    public static void OverrideSetup()
    {
        if (Instance == null || Link?.Session == null) return;
        Instance.ApplySession(again: true);
    }

    private bool _applied;

    /// <summary>
    /// Die Partie in die Einstellungen tragen — <b>vor</b> dem Kartenladen.
    ///
    /// <para>⚠ <c>Determinism.Forced</c> ist die Stelle, an der der Keim des
    /// Vermittlers wirkt: <c>Determinism.NewMap</c> (gerufen aus
    /// <c>NavGrid.Build</c>) nimmt <c>Forced ?? Fnv32(Kartenname)</c>. Wer nach
    /// dem Laden keimt, keimt zu spät — die Karte hat dann schon gewürfelt.</para>
    /// </summary>
    private void ApplySession(bool again = false)
    {
        _applied = true;
        var s = Link!.Session!;
        Determinism.EnsureConfigured();
        Determinism.Forced = s.Seed;

        // ⚠ Die Karte und die Einstellungen kommen bei JEDEM aus der Partie —
        // auch beim Vermittler. Bei ihm ist es dieselbe Zahl, die er selbst
        // geschickt hat; dass sie trotzdem aus einer Quelle kommt, ist der Sinn.
        UI.SkirmishSetup.Map = s.Map;
        UI.SkirmishSetup.AiCount = s.AiCount;
        UI.SkirmishSetup.Level = (Rendering.MapEntityLayer.AiLevel)s.Level;
        UI.SkirmishSetup.Techstandard = s.Techstandard;
        UI.SkirmishSetup.Resources = s.Resources;
        UI.SkirmishSetup.AllUnits = s.AllUnits;
        UI.SkirmishSetup.Human = s.MySlot;
        UI.SkirmishSetup.CampaignMission = 0;   // Netz ist immer Gefecht

        GD.Print($"netz: Partie {(again ? "noch einmal " : "")}angenommen, " +
                 $"Keim {s.Seed} erzwungen, Karte {s.Map}, mein Platz {s.MySlot}");
    }
}
