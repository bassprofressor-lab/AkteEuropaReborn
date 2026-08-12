namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;
using AkteEuropaReborn.Simulation;

/// <summary>
/// DER PRÜFSTAND: läuft dieselbe Partie zweimal gleich?
///
/// <para><b>Aufruf.</b> Der Prüfstand hängt sich an eine ganz normale Partie —
/// er startet keine eigene, sondern misst die, die die Befehlszeile ohnehin
/// startet:</para>
/// <code>
///   Godot_console.exe --headless --fixed-fps 60 --path . -- \
///       --skirmish=map_NET07,3,hard --no-briefing --determinism-check=30
/// </code>
/// <para>⚠ <c>--fixed-fps</c> ist ein GODOT-Schalter und muss VOR dem Trenner
/// stehen; alles hinter dem Trenner gehört dem Spiel. Ohne <c>--fixed-fps</c>
/// bekommt <c>_Process</c> die echte verstrichene Zeit, und dann ist der Lauf
/// naturgemäss nicht wiederholbar — das ist keine Schwäche des Prüfstands,
/// sondern genau der Befund, den er misst.</para>
///
/// <list type="bullet">
/// <item><c>--determinism-check=&lt;sekunden&gt;</c> — so viele SIMULIERTE
/// Sekunden laufen, dann beenden. Simulierte Sekunden, nicht Bilder: nur so
/// sind zwei Läufe mit verschiedener Bildrate überhaupt vergleichbar.</item>
/// <item><c>--determinism-every=&lt;sekunden&gt;</c> — wie oft gedruckt wird
/// (Vorgabe 1).</item>
/// <item><c>--determinism-seed=&lt;zahl&gt;</c> — Keim erzwingen.</item>
/// <item><c>--determinism-poison</c> — GEGENPROBE: ungekeimt würfeln. Zwei
/// Läufe damit MÜSSEN auseinandergehen, sonst sieht der Prüfstand nichts und
/// ist wertlos.</item>
/// <item><c>--determinism-rng-check</c> — misst den alten gegen den neuen
/// Würfel (siehe <see cref="Core.Math.DeterministicRng"/>).</item>
/// </list>
///
/// <para><b>Wo er einhängt.</b> In <c>UpdateAi</c> (SkirmishAi.cs), dem
/// letzten Aufruf in <c>MapEntityLayer._Process</c>. Damit misst er die Welt
/// NACH einem vollständigen Takt und liegt hinter allem, was der Takt anfasst
/// — Kampf, Produktion, Bahn, Flugzeuge, Missionsskript.</para>
/// </summary>
public partial class MapEntityLayer
{
    private bool _detArmed, _detDone, _detOff, _twinTried;
    private double _detSeconds, _detEvery = 1.0, _detClock, _detNext;
    private int _detFrames;
    private ulong _detLast;
    private FileAccess? _detFile;

    /// <summary>
    /// Jede Zeile geht in eine DATEI und wird sofort ausgeschrieben.
    ///
    /// <para>⚠ <b>Warum, gemessen am 12.08.2026.</b> Der erste Prüfstand druckte
    /// nur auf die Ausgabe, und von fünf gleichen Läufen kamen vier LEER
    /// zurück: dieser Godot-Bau beendet sich nach <c>GetTree().Quit()</c>
    /// gelegentlich mit »Fatal error. Internal CLR error. (0x80131506) at
    /// System.GC.RunFinalizers()« — und dabei geht der noch gepufferte Teil der
    /// Standardausgabe verloren. Der Lauf selbst war jedes Mal vollständig
    /// durchgelaufen; nur der Bericht fehlte. Eine Messung, die man nicht
    /// wiederholen kann, weil das Protokoll wegfällt, ist keine Messung.</para>
    /// </summary>
    private void DetWrite(string line)
    {
        GD.Print(line);
        if (_detFile == null) return;
        _detFile.StoreLine(line);
        _detFile.Flush();
    }

    /// <summary>Wird von <c>UpdateAi</c> gerufen — einmal je Takt, ganz am Ende
    /// von <c>_Process</c>.</summary>
    internal void DeterminismTick(float dt)
    {
        if (_detOff) return;

        // ⚠ DER ZWILLINGS-PRÜFSTAND kommt VOR allem anderen dran und übernimmt
        // dann den ganzen Lauf (Simulation/DeterminismTwin.cs). Er misst nicht
        // diese Partie, sondern setzt sich zwei eigene auf und legt diese hier
        // schlafen; deshalb steht danach ein `return` und kein `if`.
        //
        // Der Ort ist derselbe wie beim einläufigen Prüfstand — das Ende eines
        // vollen Takts —, weil es der früheste ist, an dem diese Sitzung ohne
        // die gesperrten Dateien an einen geladenen Zustand herankommt.
        if (!_twinTried)
        {
            _twinTried = true;
            if (DeterminismTwinRunner.TryStart(this)) { _detOff = true; return; }
        }

        if (!_detArmed && !DeterminismArm()) { _detOff = true; return; }

        _detFrames++;
        _detClock += dt;

        if (_detClock >= _detNext)
        {
            DeterminismReport(false);
            while (_detNext <= _detClock) _detNext += _detEvery;
        }

        if (_detClock >= _detSeconds && !_detDone)
        {
            _detDone = true;
            DeterminismReport(true);
            _detOff = true;
            GetTree().Quit(0);
        }
    }

    /// <summary>Schalter lesen, Zufall keimen. Gibt false zurück, wenn dieser
    /// Lauf gar kein Prüflauf ist — dann kostet der Einhänger ab jetzt ein
    /// einziges bool.</summary>
    private bool DeterminismArm()
    {
        _detArmed = true;
        double secs = -1;
        bool rngCheck = false;
        string outPath = "";
        foreach (string a in Core.CommandLine.Args)
        {
            if (a.StartsWith("--determinism-check="))
                double.TryParse(a["--determinism-check=".Length..],
                                System.Globalization.CultureInfo.InvariantCulture, out secs);
            else if (a.StartsWith("--determinism-every="))
                double.TryParse(a["--determinism-every=".Length..],
                                System.Globalization.CultureInfo.InvariantCulture, out _detEvery);
            else if (a.StartsWith("--determinism-out=")) outPath = a["--determinism-out=".Length..];
            else if (a == "--determinism-rng-check") rngCheck = true;
        }
        if (secs <= 0 && !rngCheck) return false;

        if (outPath.Length > 0)
        {
            _detFile = FileAccess.Open(outPath, FileAccess.ModeFlags.Write);
            if (_detFile == null) GD.PrintErr($"determinism: kann {outPath} nicht schreiben");
        }
        if (rngCheck) DeterminismRngCheck();
        if (secs <= 0) { _detFile?.Close(); GetTree().Quit(0); return false; }

        _detSeconds = secs;
        if (_detEvery <= 0) _detEvery = 1.0;
        _detNext = _detEvery;

        // Der Keim. Determinism.NewMap ist beim Kartenladen schon durch
        // (NavGrid.Build), hier kommt nur noch der eigene Würfel der Ebene
        // dazu — und die Meldung, damit im Protokoll steht, womit gerechnet
        // wurde.
        DeterminismSeed(_mission);

        DetWrite($"# Pruefstand: {_detSeconds:0.###} s simulierte Zeit, " +
                 $"Bericht alle {_detEvery:0.###} s, Karte {_mission}, " +
                 $"Einheiten {_entities.Count}, Flugzeuge {_special.Count}, " +
                 $"Keim {Determinism.Seed}, vergiftet={Determinism.Poisoned}");
        DeterminismReport(false);   // Takt 0: der Anfangszustand
        return true;
    }

    private void DeterminismReport(bool final)
    {
        ulong all = DeterminismChecksum();
        var t = DeterminismTotals();
        var sb = new StringBuilder();
        sb.Append(final ? "DET-ENDE" : "DET     ");
        sb.Append($" t={_detClock,8:0.0000} takte={_detFrames,6} sum={all:X16}");
        sb.Append($" boden={DeterminismGroundChecksum():X16} luft={DeterminismAircraftChecksum():X16}");
        sb.Append($" wuerfe={Determinism.Draws,6}");
        sb.Append($" hp={t.Hp,7} teile={t.Stock,6} mun={t.Ammo,6} leben={t.Alive,4}");
        sb.Append($" schuesse={DebugShots,5}");
        if (!final && _detLast != 0 && all == _detLast) sb.Append("  (unveraendert)");
        _detLast = all;
        DetWrite(sb.ToString());
        if (final)
        {
            DetWrite($"DET-KEIM seed={Determinism.Seed} vergiftet={Determinism.Poisoned} " +
                     $"bilder={_detFrames} mittleres_dt={(_detFrames > 0 ? _detClock / _detFrames : 0):0.000000}");
            _detFile?.Close();
            _detFile = null;
        }
    }

    // ---- Nachweis, dass der alte Würfel kaputt war --------------------------

    /// <summary>Die ALTE Fassung von <c>DeterministicRng.NextUInt</c>, Wort für
    /// Wort wie sie bis zum 12.08.2026 in <c>FixedMath.cs</c> stand. Sie steht
    /// hier nur, um sie zu MESSEN — nichts im Spiel ruft sie.</summary>
    private struct OldRng
    {
        public uint S0, S1;
        public OldRng(uint seed) { S0 = Mix(seed); S1 = Mix(S0); }
        private static uint Mix(uint x)
        {
            x += 0x9E3779B9;
            x = (x ^ (x >> 16)) * 0x85EBCA6B;
            x = (x ^ (x >> 13)) * 0xC2B2AE35;
            return x ^ (x >> 16);
        }
        public uint Next()
        {
            uint s0 = S0, s1 = S1, result = s0 + s1;
            s1 ^= s0;
            S0 = (s0 << 13) | (s0 >> 19) ^ s1 ^ (s1 << 5);
            S1 = (s1 << 7) | (s1 >> 25);
            return result;
        }
    }

    /// <summary>
    /// Misst den alten gegen den neuen Würfel. Zwei Dinge, die zählen:
    ///
    /// <list type="number">
    /// <item><b>Die PERIODE.</b> Ein Würfel mit 64 Bit Zustand sollte ungefähr
    /// 2^64 Schritte laufen, bevor er sich wiederholt. Der Zustand ist hier
    /// klein genug, um mit Brents Zyklussuche wenigstens eine UNTERE Schranke
    /// zu messen: findet sie in N Schritten keinen Kreis, ist die Periode
    /// grösser als N.</item>
    /// <item><b>Die BIT-DICHTE des Zustands.</b> Der alte Schritt schrieb
    /// <c>_s0 = (s0&lt;&lt;13) | (…)</c> — ein ODER, kein XOR. ODER kann Bits nur
    /// setzen, nie löschen; ein solcher Schritt zieht den Zustand zu lauter
    /// Einsen. Erwartet sind 32 gesetzte Bits von 64.</item>
    /// </list>
    /// </summary>
    private void DeterminismRngCheck()
    {
        const int N = 4000000;

        // Bit-Dichte des Zustands nach N Zügen (alt).
        var old = new OldRng(12345);
        long bits = 0;
        for (int i = 0; i < N; i++)
        {
            old.Next();
            if (i >= N - 1000) bits += System.Numerics.BitOperations.PopCount(old.S0) +
                                       System.Numerics.BitOperations.PopCount(old.S1);
        }
        double densOld = bits / 1000.0;

        // Brents Zyklussuche über den 64-Bit-Zustand des alten Würfels.
        long lam = CycleLenOld(12345, 1 << 26);

        // Verteilung über 16 Fächer.
        var rngB = new Core.Math.DeterministicRng(12345);
        var oldB = new OldRng(12345);
        int[] bn = new int[16], bo = new int[16];
        for (int i = 0; i < N; i++) { bn[rngB.NextUInt() & 15]++; bo[oldB.Next() & 15]++; }
        int mn = int.MaxValue, xn = 0, mo = int.MaxValue, xo = 0;
        for (int i = 0; i < 16; i++)
        {
            if (bn[i] < mn) mn = bn[i];
            if (bn[i] > xn) xn = bn[i];
            if (bo[i] < mo) mo = bo[i];
            if (bo[i] > xo) xo = bo[i];
        }

        long lamNew = CycleLenNew(12345, 1 << 26);
        DetWrite($"RNG-CHECK  {N} Zuege, Keim 12345, 16 Faecher, erwartet je {N / 16}");
        DetWrite($"RNG-CHECK  ALT  Faecher {mo}..{xo}   Zustandsbits {densOld:0.00}/64 " +
                 $"(erwartet 32)   Periode {(lam < 0 ? "> 2^26" : lam.ToString())}");
        DetWrite($"RNG-CHECK  NEU  Faecher {mn}..{xn}   Zustandsbits {DensityNew(12345, N):0.00}/64 " +
                 $"(erwartet 32)   Periode {(lamNew < 0 ? "> 2^26 (67 108 864)" : lamNew.ToString())}");
        DetWrite("RNG-CHECK  (xoroshiro64** hat bewiesene Periode 2^64-1; gemessen wird hier " +
                 "nur die untere Schranke, die in vertretbarer Zeit erreichbar ist)");
    }

    /// <summary>Der NEUE Schritt, nachgebaut — der Zustand von
    /// <c>DeterministicRng</c> ist privat, gemessen werden soll er trotzdem.
    /// Wort für Wort dieselben drei Zeilen wie in FixedMath.cs.</summary>
    private struct NewRng
    {
        public uint S0, S1;
        public NewRng(uint seed) { S0 = Mix(seed); S1 = Mix(S0); if ((S0 | S1) == 0) { S0 = 0x9E3779B9; S1 = 0x85EBCA6B; } }
        private static uint Mix(uint x)
        {
            x += 0x9E3779B9;
            x = (x ^ (x >> 16)) * 0x85EBCA6B;
            x = (x ^ (x >> 13)) * 0xC2B2AE35;
            return x ^ (x >> 16);
        }
        private static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));
        public uint Next()
        {
            uint s0 = S0, s1 = S1, result = Rotl(s0 * 0x9E3779BBu, 5) * 5u;
            s1 ^= s0;
            S0 = Rotl(s0, 26) ^ s1 ^ (s1 << 9);
            S1 = Rotl(s1, 13);
            return result;
        }
    }

    private static double DensityNew(uint seed, int n)
    {
        var r = new NewRng(seed);
        long bits = 0;
        for (int i = 0; i < n; i++)
        {
            r.Next();
            if (i >= n - 1000) bits += System.Numerics.BitOperations.PopCount(r.S0) +
                                       System.Numerics.BitOperations.PopCount(r.S1);
        }
        return bits / 1000.0;
    }

    private static long CycleLenNew(uint seed, long limit)
    {
        var tortoise = new NewRng(seed);
        var hare = new NewRng(seed);
        long power = 1, lam = 1;
        hare.Next();
        while ((tortoise.S0 != hare.S0 || tortoise.S1 != hare.S1) && lam < limit)
        {
            if (power == lam) { tortoise = hare; power *= 2; lam = 0; }
            hare.Next();
            lam++;
        }
        return lam >= limit ? -1 : lam;
    }

    /// <summary>Brent: die Kreislänge des alten Zustands, oder −1 wenn in
    /// <paramref name="limit"/> Schritten keiner gefunden wurde.</summary>
    private static long CycleLenOld(uint seed, long limit)
    {
        var tortoise = new OldRng(seed);
        var hare = new OldRng(seed);
        long power = 1, lam = 1;
        hare.Next();
        while ((tortoise.S0 != hare.S0 || tortoise.S1 != hare.S1) && lam < limit)
        {
            if (power == lam) { tortoise = hare; power *= 2; lam = 0; }
            hare.Next();
            lam++;
        }
        return lam >= limit ? -1 : lam;
    }
}
