namespace AkteEuropaReborn.Core;

using Godot;
using AkteEuropaReborn.Simulation;

[GlobalClass]
public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    public override void _Notification(int what)
    {
        if (what == NotificationSceneInstantiated)
            Instance = this;
    }

    public override void _Ready()
    {
        GD.Print("GameManager ready");
        MuellsammlerOhneNebenlauf();
        // ⚠ Schadensbehebung, kein Nachbau: bis zum 21.08.2026 hat der
        // Musikregler die Lautstaerke des MIDI-Geraets SYSTEMWEIT verstellt,
        // und dieser Wert ueberlebt das Beenden des Spiels. Wer damals am
        // Regler war, hat sein Geraet womoeglich immer noch leise. Einmal beim
        // Start auf voll zuruecksetzen. Siehe MidiMusic.Volume.
        Audio.MidiMusic.GeraeteLautstaerkeZuruecksetzen();
        // There used to be a HookButtons(GetTree()) here. It gave every button
        // sound 600, which is a spoken sub-mission line, not a click — see the
        // note at the top of GameSounds. The original's menu is silent.
    }

    /// <summary><c>--gc-nebenlaeufig-alt</c> — Stand bis 0.6.7: der Muellsammler
    /// laeuft im Entwicklungslauf (<c>--path .</c>) NEBENLAEUFIG. Nullmodell zu
    /// bug-484.</summary>
    public static bool GcNebenlaeufigAlt;

    /// <summary>
    /// bug-484: den NEBENLAEUFIGEN Muellsammler (Hintergrund-GC) abschalten —
    /// zur Laufzeit, weil die Projekteinstellung im Entwicklungslauf nicht
    /// ankommt.
    ///
    /// <para>⚠⚠ <b>Was geschah.</b> Kopflose Gefechtslaeufe auf map_DM_3/DM_4
    /// starben beim Laden mit »Internal CLR error (0x80131506)«. Der Dump
    /// (12.10.2026, crash_2268.dmp) zeigt den Ausnahmeauftrag
    /// <c>System.ExecutionEngineException</c> auf einem Faden OHNE verwalteten
    /// Rahmen (verwaltete Nr. 4, 0x744c) — dem Hintergrund-Sammler —, waehrend
    /// der Hauptfaden selbst in einer Sammlung steht (Allokation in
    /// JsonObject.InitializeDictionary, MapEntityLayer.BuildElevLookup).
    /// <c>verifyheap</c> findet das Objekt 0x1e41f80ced0 (gen2) mit
    /// Methodentafel 0.</para>
    ///
    /// <para>⚠⚠ <b>Warum der Schalter vom 18.08. nicht griff.</b> Das
    /// <c>&lt;ConcurrentGarbageCollection&gt;false</c> im csproj landet in
    /// <c>Akte Europa Reborn.runtimeconfig.json</c> — die liest aber nur die
    /// AUSGELIEFERTE Fassung. Im Entwicklungslauf startet Godot .NET ueber
    /// <c>GodotSharp/Api/Debug/GodotPlugins.runtimeconfig.json</c>
    /// (rollForward LatestMajor, kein GC-Eintrag): gemessen .NET 10.0.11,
    /// <c>GCSettings.LatencyMode = Interactive</c>, also nebenlaeufig. Mit
    /// <c>DOTNET_gcConcurrent=0</c> meldet derselbe Lauf <c>Batch</c>.</para>
    ///
    /// <para><c>GCSettings.LatencyMode = Batch</c> schaltet die Nebenlaeufigkeit
    /// auch zur Laufzeit ab; in der ausgelieferten Fassung ist es ohnehin schon
    /// Batch, dort aendert die Zeile nichts.</para>
    /// </summary>
    private static void MuellsammlerOhneNebenlauf()
    {
        foreach (string a in CommandLine.Args)
            if (a == "--gc-nebenlaeufig-alt") GcNebenlaeufigAlt = true;
        var vorher = System.Runtime.GCSettings.LatencyMode;
        if (!GcNebenlaeufigAlt)
            System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.Batch;
        GD.Print($"gc: .NET {System.Environment.Version}, Modus {vorher} -> "
                 + $"{System.Runtime.GCSettings.LatencyMode}"
                 + (GcNebenlaeufigAlt ? " (--gc-nebenlaeufig-alt)" : "")
                 + " — bug-484");
    }

    public override void _ExitTree()
    {
        var bg = System.GC.GetGCMemoryInfo(System.GCKind.Background);
        GD.Print($"gc: {System.GC.CollectionCount(0)} Sammlungen, "
                 + $"letzte Hintergrundsammlung Nr. {bg.Index} (0 = keine)");
    }
}
