namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using AkteEuropaReborn.Simulation;

/// <summary>
/// DER ZWILLINGS-PRÜFSTAND: <c>--determinism-twin=&lt;takte&gt;</c>
///
/// <para><b>Was er tut.</b> Er fährt <b>zwei vollständige Simulationen im
/// selben Prozess</b> — dieselbe Karte, derselbe Keim, derselbe Startzustand —
/// und vergleicht sie <b>nach jedem einzelnen Takt</b>. Beim ersten
/// Unterschied hält er an und sagt nicht nur DASS, sondern <b>WO</b>: welche
/// Einheit, welches Feld, welcher Wert hüben und drüben.</para>
///
/// <para><b>Warum im selben Prozess und nicht zwei Läufe hintereinander.</b>
/// Zwei Läufe hintereinander vergleichen zwei Protokolle; das kann nur sagen,
/// dass sich Zeile 400 unterscheidet. Zwei Simulationen nebeneinander lassen
/// sich <b>im Takt des Auseinanderlaufens</b> befragen, solange der Zustand
/// noch dasteht. Genau diese eine Zahl — der erste abweichende Takt — ist die,
/// nach der man den Fehler sucht. Ein zweiter Grund kommt dazu: im selben
/// Prozess teilen sich beide Simulationen alles, was <c>static</c> ist. Was
/// dabei auffliegt, ist kein Kunstfehler des Prüfstands, sondern die Liste der
/// Stellen, an denen die Simulation <b>heute nicht zweimal nebeneinander
/// existieren kann</b> — und die deckt sich mit der Liste der Stellen, die im
/// Netzspiel zwischen zwei Maschinen auseinanderlaufen.</para>
///
/// <para><b>Die Schalter.</b></para>
/// <list type="bullet">
///   <item><c>--determinism-twin=&lt;takte&gt;</c> — so viele Takte fahren.</item>
///   <item><c>--determinism-twin-dt=&lt;sekunden&gt;</c> — die Taktlänge, Vorgabe
///     1/60. Beide Simulationen bekommen <b>denselben</b> Wert; der Prüfstand
///     misst hier nicht die Bildrate, sondern den Rest.</item>
///   <item><c>--determinism-twin-skew=&lt;sekunden&gt;</c> — ⚠ und der bricht
///     die Gleichheit mit Absicht: Simulation B bekommt eine ANDERE Taktlänge.
///     Damit ist der Bildratenfehler im selben Prozess messbar, ohne zwei
///     Läufe zu vergleichen.</item>
///   <item><c>--determinism-twin-poison=&lt;takt&gt;</c> — <b>DIE GEGENPROBE.</b>
///     Bei genau diesem Takt bekommt in Simulation B eine einzige Einheit einen
///     Trefferpunkt weniger. Das ist die <b>kleinstmögliche</b> Abweichung im
///     ganzen Zustand; findet der Prüfstand die, findet er jede.</item>
///   <item><c>--determinism-twin-gdrand</c> — den globalen Godot-Würfel NICHT
///     je Takt gleichstellen. Siehe <see cref="SyncGlobalRng"/>; damit wird
///     sichtbar, was <c>GD.Randi()</c> in der Simulation anrichtet.</item>
/// </list>
///
/// <para><b>Aufruf</b> (⚠ die Umgebungsvariable ist nicht schmückend, siehe
/// <see cref="WarnAboutGc"/>):</para>
/// <code>
///   DOTNET_gcConcurrent=0 Godot_console.exe --headless --path . -- \
///       --skirmish=map_NET07,3,hard --no-briefing --determinism-twin=600
/// </code>
/// </summary>
public sealed partial class DeterminismTwinRunner : Node
{
    // ---- Aufbau -------------------------------------------------------------

    private MapEntityLayer _a = null!, _b = null!;
    private int _ticks, _limit, _poisonAt = -1;
    private double _dtA = 1.0 / 60.0, _dtB = 1.0 / 60.0;
    private bool _syncGd = true, _done;
    private Godot.FileAccess? _log;
    private ulong _gdSeed;

    /// <summary>Was in den Vergleich eingeht, in genau der Reihenfolge, in der
    /// <see cref="MapEntityLayer.DeterminismSnapshot"/> es ablegt. Der Prüfstand
    /// braucht die Namen, um beim ersten Unterschied sagen zu können, WELCHES
    /// Feld es war — eine Prüfsumme allein sagt das nie.</summary>
    internal static readonly string[] EntityFields =
    {
        "Slot", "Col", "Row", "Owner", "Hp", "Ammo", "Fuel",
        "StockW", "StockF", "StockS", "StockT", "State", "UpgradeStep",
        "ProdAccum", "CaptureProgress", "Dead", "Deposit", "Target", "Facing",
        "PathIdx", "PathLen", "GoalCol", "GoalRow",
    };

    internal static readonly string[] AircraftFields =
    { "Slot", "Col", "Row", "Owner", "Hp", "Ammo", "Fuel", "Cargo", "Stored", "Dead", "Target", "Customer" };

    /// <summary>
    /// Den Prüfstand anwerfen. Gerufen wird das aus
    /// <c>MapEntityLayer.DeterminismTick</c> beim ersten Takt der ganz normal
    /// geladenen Partie — dem einzigen Punkt, an den diese Sitzung ohne die
    /// gesperrten Dateien herankommt.
    ///
    /// <para>⚠ Die geladene Partie selbst wird dabei <b>stillgelegt und nicht
    /// mitgemessen</b>. Sie hat, wenn dieser Aufruf kommt, bereits einen Takt
    /// hinter sich; ein Zwilling, der einen Takt voraus ist, wäre kein
    /// Zwilling. Stattdessen werden zwei FRISCHE Simulationen aufgesetzt, die
    /// beide bei Takt 0 stehen. Das kostet den Speicher einer dritten Karte und
    /// ist den symmetrischen Anfang wert.</para>
    /// </summary>
    /// <summary>
    /// ⚠ EIN STATISCHES SPERRFLAG — und es ist gemessen, nicht vorsorglich.
    ///
    /// <para>Der erste Zwillingslauf am 12.08.2026 lief unendlich: der
    /// Einhänger sitzt in <c>UpdateAi</c>, also in JEDER
    /// <see cref="MapEntityLayer"/>. Die beiden Zwillinge sind aber selbst
    /// welche — sobald der Prüfstand ihnen den ersten Takt gab, riefen sie
    /// ihrerseits <c>TryStart</c> und legten zwei weitere Zwillinge an, und
    /// die wieder zwei. Im Protokoll stand die Kopfzeile »# Zwillings-
    /// Prüfstand« fünfmal in 90 Sekunden, jedes Mal mit frisch geladener
    /// Karte (je 1,9 s), und der Lauf kam nie über Takt 0 hinaus.</para>
    ///
    /// <para>Das Flag ist <b>statisch</b> und nicht je Instanz, weil genau das
    /// die Sperre ist: es darf in einem Prozess nur EINEN Prüfstand geben.</para>
    /// </summary>
    internal static bool Active { get; private set; }

    internal static bool TryStart(MapEntityLayer host)
    {
        if (Active) return true;      // schon einer da — dieser Einhänger schweigt
        int ticks = 0;
        double dt = 1.0 / 60.0, skew = 0;
        int poison = -1;
        bool gdRand = false;
        string outPath = "";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (string a in Core.CommandLine.Args)
        {
            if (a.StartsWith("--determinism-twin=")) int.TryParse(a["--determinism-twin=".Length..], out ticks);
            else if (a.StartsWith("--determinism-twin-dt=")) double.TryParse(a["--determinism-twin-dt=".Length..], inv, out dt);
            else if (a.StartsWith("--determinism-twin-skew=")) double.TryParse(a["--determinism-twin-skew=".Length..], inv, out skew);
            else if (a.StartsWith("--determinism-twin-poison=")) int.TryParse(a["--determinism-twin-poison=".Length..], out poison);
            else if (a.StartsWith("--determinism-twin-out=")) outPath = a["--determinism-twin-out=".Length..];
            else if (a == "--determinism-twin-gdrand") gdRand = true;
        }
        if (ticks <= 0) return false;

        var r = new DeterminismTwinRunner
        {
            _limit = ticks,
            _dtA = dt > 0 ? dt : 1.0 / 60.0,
            _dtB = skew > 0 ? skew : (dt > 0 ? dt : 1.0 / 60.0),
            _poisonAt = poison,
            _syncGd = !gdRand,
        };
        if (outPath.Length > 0) r._log = Godot.FileAccess.Open(outPath, Godot.FileAccess.ModeFlags.Write);
        Active = true;
        host.GetTree().Root.AddChild(r);
        r.CallDeferred(nameof(Build), host);
        return true;
    }

    // ---- der Aufbau der beiden Simulationen ---------------------------------

    private void Build(MapEntityLayer host)
    {
        var viewer = host.GetParent() as MapViewer;
        if (viewer == null)
        {
            Say("FEHLER: der Prüfstand hängt nicht unter einem MapViewer — abgebrochen");
            GetTree().Quit(2);
            return;
        }

        // Die ganz normal geladene Partie legt sich schlafen. Sie ist einen
        // Takt voraus und würde sonst weiter rechnen und weiter würfeln.
        host.SetProcess(false);
        host.SetPhysicsProcess(false);

        WarnAboutGc();

        string name = viewer.TwinMapName;
        if (name.Length == 0)
        {
            Say("FEHLER: kein Kartenname — abgebrochen");
            GetTree().Quit(2);
            return;
        }

        Say($"# Zwillings-Prüfstand: {_limit} Takte, dt(A)={_dtA:0.000000} s, dt(B)={_dtB:0.000000} s" +
            (Math.Abs(_dtA - _dtB) > 1e-12 ? "  ⚠ VERSCHIEDEN (skew)" : "") +
            $", Karte {name}, Gift bei Takt {(_poisonAt < 0 ? "—" : _poisonAt.ToString())}" +
            $", globaler Godot-Würfel {(_syncGd ? "je Takt gleichgestellt" : "NICHT gleichgestellt")}");

        _a = viewer.BuildTwin(name, out string errA);
        if (errA.Length > 0) { Say("FEHLER A: " + errA); GetTree().Quit(2); return; }
        _b = viewer.BuildTwin(name, out string errB);
        if (errB.Length > 0) { Say("FEHLER B: " + errB); GetTree().Quit(2); return; }

        // ⚠ NACH beiden Ladevorgängen: `NavGrid.Build` ruft `Determinism.NewMap`
        // und stellt die Ströme zurück — der zweite Ladevorgang macht also die
        // Würfe des ersten rückgängig. Erst hier stehen beide Simulationen
        // wirklich auf demselben Anfang.
        Determinism.ResetStreams();
        _gdSeed = Determinism.Seed;

        var sa = new List<long>();
        var sb = new List<long>();
        _a.DeterminismSnapshot(sa);
        _b.DeterminismSnapshot(sb);
        int first = FirstDiff(sa, sb);
        if (first >= 0)
        {
            Say($"⚠ SCHON DER ANFANGSZUSTAND ist verschieden — {Describe(first, sa, sb)}");
            Say("   (jede weitere Zahl dieses Laufs wäre wertlos)");
            Finish(false);
            return;
        }
        Say($"Anfangszustand gleich: {sa.Count} Zahlen, Prüfsumme {_a.DeterminismChecksum():X16}");
        Report(0, "gleich");
    }

    // ---- der Takt -----------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_done || _a == null || _b == null) return;

        _ticks++;

        // ⚠ Der globale Godot-Würfel (`GD.Randi()`) ist EINER für den ganzen
        // Prozess; zwei Simulationen darin teilen ihn sich unweigerlich. Wird er
        // nicht je Takt gleichgestellt, zieht A die Zahlen und B bekommt die
        // darauffolgenden — ein Auseinanderlaufen, das es im Netzspiel (eine
        // Simulation je Prozess) gar nicht gäbe.
        //
        // ⚠ UNSERE SETZUNG, und sie ist eine KRÜCKE: gleichgestellt wird, indem
        // vor jeder der beiden Simulationen derselbe Keim gesetzt wird. Das
        // heilt den Prüfstand, nicht das Spiel — im Spiel bleibt `GD.Randi()`
        // eine Quelle, die auch Klang und Anzeige anfassen, und die laufen auf
        // Bildzeit. Der Bericht nennt die vier Fundstellen, die umzustellen
        // sind. Mit `--determinism-twin-gdrand` bleibt die Krücke aus, und man
        // sieht, was ohne sie passiert.
        SyncGlobalRng();
        Determinism.UseStream(0);
        _a._Process(_dtA);

        SyncGlobalRng();
        Determinism.UseStream(1);
        if (_poisonAt >= 0 && _ticks == _poisonAt) Poison(_b);
        _b._Process(_dtB);

        var sa = new List<long>();
        var sb = new List<long>();
        _a.DeterminismSnapshot(sa);
        _b.DeterminismSnapshot(sb);

        int first = FirstDiff(sa, sb);
        if (first >= 0)
        {
            Say($"AUSEINANDER bei Takt {_ticks} (t={_ticks * _dtA:0.0000} s)");
            Say($"   {Describe(first, sa, sb)}");
            ReportAllDiffs(sa, sb);
            Report(_ticks, "AUSEINANDER");
            Finish(false);
            return;
        }

        if (_ticks % 60 == 0 || _ticks == _limit) Report(_ticks, "gleich");
        if (_ticks >= _limit) { Say($"DURCH: {_limit} Takte, kein Unterschied."); Finish(true); }
    }

    /// <summary>Denselben Keim vor beide Simulationen setzen — siehe die
    /// Begründung an der Aufrufstelle.</summary>
    private void SyncGlobalRng()
    {
        if (!_syncGd) return;
        // Der Keim wandert mit dem Takt, sonst würfelte jeder Takt dieselbe
        // Zahl und der Zufall wäre keiner mehr.
        GD.Seed(_gdSeed ^ (ulong)_ticks * 0x9E3779B97F4A7C15UL);
    }

    /// <summary>
    /// DIE GEGENPROBE: einen einzigen Trefferpunkt in B abziehen.
    ///
    /// <para>Kleiner geht eine Abweichung im ganzzahligen Zustand nicht. Ein
    /// Prüfstand, der diese eine Zahl übersieht, würde jede echte Abweichung
    /// ebenso übersehen — darum wird nicht mehr angefasst als nötig.</para>
    /// </summary>
    private void Poison(MapEntityLayer sim)
    {
        int slot = sim.DeterminismPoisonOneHp();
        if (slot >= 0) Say($"⚠ GIFT bei Takt {_ticks}: Simulation B, Einheit mit Slot {slot}, ein Trefferpunkt weniger");
        else Say($"⚠ GIFT bei Takt {_ticks} ging INS LEERE — keine lebende Einheit gefunden. Die Gegenprobe belegt nichts.");
    }

    // ---- Vergleich und Bericht ----------------------------------------------

    internal static int FirstDiff(List<long> a, List<long> b)
    {
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
        return a.Count != b.Count ? n : -1;
    }

    /// <summary>
    /// Aus der laufenden Nummer im Auszug wieder »welche Einheit, welches Feld«
    /// machen. Der Auszug ist eine flache Zahlenreihe; ohne diese Umrechnung
    /// stünde im Bericht »Zahl 4711 ist verschieden«, und das hilft niemandem.
    /// Der Aufbau des Auszugs steht in
    /// <see cref="MapEntityLayer.DeterminismSnapshot"/>: erst ein Kopf mit drei
    /// Zahlen, dann die Einheiten, dann die Flugzeuge, dann das Geld.
    /// </summary>
    internal static string Describe(int i, List<long> a, List<long> b)
    {
        long va = i < a.Count ? a[i] : long.MinValue;
        long vb = i < b.Count ? b[i] : long.MinValue;
        string what = Where(i, a);
        return $"{what}: A={va} B={vb} (Unterschied {vb - va})";
    }

    private static string Where(int i, List<long> s)
    {
        const int head = 3;
        if (i < head) return $"Kopf[{i}] ({(i == 0 ? "Einheiten" : i == 1 ? "Flugzeuge" : "Spieler")})";
        int nE = (int)s[0], nA = (int)s[1], nP = (int)s[2];
        int k = i - head;
        int eBlock = EntityFields.Length, aBlock = AircraftFields.Length;
        if (k < nE * eBlock) return $"Einheit #{k / eBlock}, Feld {EntityFields[k % eBlock]}";
        k -= nE * eBlock;
        if (k < nA * aBlock) return $"Flugzeug #{k / aBlock}, Feld {AircraftFields[k % aBlock]}";
        k -= nA * aBlock;
        if (k < nP) return $"Kontostand Spieler {k}";
        return $"Zahl {i} (ausserhalb des bekannten Aufbaus)";
    }

    /// <summary>Nicht nur die erste Abweichung, sondern alle — und wie viele es
    /// sind. Eine einzelne ist ein Rundungsfehler, zweihundert sind ein
    /// auseinandergelaufenes Spiel, und der Unterschied gehört in den
    /// Bericht.</summary>
    private void ReportAllDiffs(List<long> a, List<long> b)
    {
        int n = Math.Min(a.Count, b.Count), count = 0;
        var shown = new StringBuilder();
        for (int i = 0; i < n; i++)
        {
            if (a[i] == b[i]) continue;
            count++;
            if (count <= 12) shown.Append("\n     ").Append(Describe(i, a, b));
        }
        Say($"   {count} von {n} Zahlen verschieden{(count > 12 ? " (die ersten 12)" : "")}:{shown}");
    }

    private void Report(int tick, string state)
    {
        var ta = _a.DeterminismTotals();
        var tb = _b.DeterminismTotals();
        Say($"TWIN t={tick,6} {state,-12} A={_a.DeterminismChecksum():X16} B={_b.DeterminismChecksum():X16}" +
            $" | hp {ta.Hp}/{tb.Hp} leben {ta.Alive}/{tb.Alive} mun {ta.Ammo}/{tb.Ammo}" +
            $" | wuerfe A={Determinism.DrawsOf(0)} B={Determinism.DrawsOf(1)}");
    }

    private void Finish(bool ok)
    {
        _done = true;
        Say($"TWIN-ENDE takte={_ticks} keim={Determinism.Seed} " +
            $"wuerfe A={Determinism.DrawsOf(0)} B={Determinism.DrawsOf(1)} ergebnis={(ok ? "GLEICH" : "AUSEINANDER")}");
        _log?.Close();
        _log = null;
        GetTree().Quit(ok ? 0 : 1);
    }

    private void Say(string s)
    {
        GD.Print(s);
        if (_log == null) return;
        _log.StoreLine(s);
        _log.Flush();
    }

    /// <summary>
    /// ⚠ WARNEN, WENN DER HINTERGRUND-GC LÄUFT — und das ist gemessen, nicht
    /// vorsorglich.
    ///
    /// <para>Am 12.08.2026 stürzte derselbe Prüflauf <b>drei von drei Malen</b>
    /// mit »Fatal error. Internal CLR error. (0x80131506)« beim Kartenladen ab,
    /// sobald die Befehlszeile ein zusätzliches Wort trug — auch ein völlig
    /// wirkungsloses (<c>--zzzzzzzzzzzzzzzzzzz</c>: 3 von 3 Abstürzen; ohne es:
    /// 0 von 3). Es war also nie der Prüfstand, sondern der Zeitpunkt der
    /// Speicherbereinigung: der Kartenlader legt zehntausende
    /// <c>Godot.Collections.Dictionary</c> an, deren Finalisierer in Godots
    /// nativen Code greifen, und der verträgt das nicht nebenläufig.</para>
    ///
    /// <para>Mit <c>DOTNET_gcConcurrent=0</c>: <b>0 von 3 Abstürzen</b>.
    /// Dasselbe mit <c>DOTNET_GCgen0size=4000000</c>. Wer den Prüfstand ohne
    /// diese Einstellung fährt, misst nicht die Simulation, sondern ein
    /// Rennen — darum steht hier eine Warnung und keine stille Annahme.</para>
    /// </summary>
    private void WarnAboutGc()
    {
        // ⚠ GEFRAGT WIRD DIE UMGEBUNG, NICHT DIE LAUFZEIT — und das ist ein
        // Befund für sich. `GC.GetConfigurationVariables()` meldete am
        // 12.08.2026 auch dann »gcConcurrent = true«, wenn
        // DOTNET_gcConcurrent=0 gesetzt war UND nachweislich wirkte (0 von 3
        // Abstürzen statt 3 von 3). Godot startet die Laufzeit über hostfxr
        // mit eigenen Eigenschaften; was dabei aus der Umgebungsvariablen wird,
        // taucht in dieser Abfrage nicht auf. Eine Warnung, die immer angeht,
        // liest bald niemand mehr — darum wird die Umgebungsvariable selbst
        // gelesen, denn die ist es, die der Aufrufer setzt.
        // ⚠ System.Environment, nicht Godot.Environment — beide heissen gleich
        // und `using Godot;` steht oben.
        string env = System.Environment.GetEnvironmentVariable("DOTNET_gcConcurrent") ?? "";
        string gen0 = System.Environment.GetEnvironmentVariable("DOTNET_GCgen0size") ?? "";
        if (env is "0" or "false" || gen0.Length > 0)
        {
            Say($"GC gebändigt: DOTNET_gcConcurrent={(env.Length > 0 ? env : "—")} " +
                $"DOTNET_GCgen0size={(gen0.Length > 0 ? gen0 : "—")}");
            return;
        }
        Say("⚠ WARNUNG: weder DOTNET_gcConcurrent=0 noch DOTNET_GCgen0size gesetzt. Dieser Godot-Bau " +
            "stürzt dann beim Kartenladen unregelmässig mit »Internal CLR error (0x80131506)« ab — " +
            "gemessen am 12.08.2026 mit map_NET07: 3 von 3 Abstürzen mit einem beliebigen " +
            "zusätzlichen Wort auf der Befehlszeile, 0 von 3 mit DOTNET_gcConcurrent=0. Der " +
            "Prüfstand lädt DREI Karten statt einer und trifft das Rennen entsprechend öfter.");
        try
        {
            var cfg = GC.GetConfigurationVariables();
            if (cfg.TryGetValue("gcConcurrent", out object? v))
                Say($"   (die Laufzeit selbst meldet gcConcurrent={v} — siehe die Anmerkung im Quelltext, " +
                    "diese Zahl ist nicht verlässlich)");
        }
        catch (Exception e) { Say($"   (GC-Einstellung nicht lesbar: {e.Message})"); }
    }
}
