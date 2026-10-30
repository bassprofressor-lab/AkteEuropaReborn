namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐ 22.10.2026, Paket N (Testerrunde 0.6.11, bug-615) — <b>Befehl 980 »Mission neu
/// starten«</b>, netzfest. Lesung: <c>berichte/neustart-980-netz-opus.md</c>.
///
/// <para><b>Das Original</b> (C / F, selbst gegengelesen in F):</para>
/// <list type="bullet">
/// <item>Haupt-Menü Art 17, Knopf 6 »Mission wiederholen« (C 0x44B9D3) → Abfrage Art 10
/// Unterart 3 »Möchten Sie die Mission erneut starten?« (C 0x446370/0x446416, Text C
/// 0x4FC7F8 / F 0x4FB830) → »Ja« (Element 1) → <c>word[0xB89438] := 0x3D4</c> = 980 (F
/// 0x44A17F, C 0x44B18C) → Bus <c>0x4012A8</c>.</item>
/// <item>Behandler (F 0x4C3C84, C 0x4C4113): <c>byte[0x4F5F9C] := 0</c>, dann der
/// Missions-Reset F 0x4CFE40 / C 0x4D0290 — auf JEDER Maschine, ohne Netztor. Der Reset
/// lädt <c>&lt;0&gt;N.cwm</c> aus der Missionsnummer; im Netz (Nummern 51..58, Dateien
/// NET01..08) VERMUTET kaputt.</item>
/// </list>
///
/// <para><b>Bei uns:</b></para>
/// <list type="bullet">
/// <item><b>Einzelspiel:</b> dieselbe Abfrage, »Ja« schickt 980 über den Ring, der
/// Behandler ruft <see cref="OnNeustartEinzel"/> = Szene neu laden (der Missions-Reset;
/// <c>campaign.cfg</c> schreibt erst das Missionsende).</item>
/// <item><b>Netz — ⚠ UNSERE SETZUNG (Spielerentscheidung 22.10., bewusste Verbesserung
/// gegenüber dem Original):</b> kein Szenenwechsel (der Taktgeber <c>NetGameRunner</c> und
/// die Sitzung lebten ihn nicht). Der Startstand der Partie wird im Takt des gemeinsamen
/// Anfangs im Speicher festgehalten (<see cref="NetzStartstandMerken"/>) und vom
/// Behandler auf allen Maschinen im selben Takt eingespielt — nach dem Vorbild des
/// netzfesten Ladens 990 (<see cref="NetzStandEinspielen"/>). Dazu wie der Reset
/// <c>0x44FE10(1)</c>: alle Fenster zu, die Pause (Art 40) aus.</item>
/// <item>⚠ SETZUNG: Würfel (<c>Determinism</c>) und Ring laufen weiter — beide
/// Maschinen haben ohnehin denselben Stand. ⭐ Seit bug-683 (30.10.2026) gehen der
/// Taktzähler und die KI-Uhren (AttackTimer, Think, Feind-naht-Sperre, KI-Nebel …) mit dem
/// Startstand zurück (Simulation/KiUhrenSpielstand.cs) — für die KI ein echter Neubeginn;
/// <c>--ki-uhren-spielstand-alt</c> = der Stand davor.</item>
/// </list>
///
/// <para>Gegenschalter: <c>--neustart-alt</c> (Stand bis 0.6.11: Knopf lädt sofort neu,
/// ohne Abfrage und ohne 980; im Netz dann gesperrt), <c>--neustart-oertlich</c> (NUR zum
/// Messen: der Behandler wirkt nur beim Absender — das Nullmodell von
/// <c>--net-neustart-check</c>).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Text des Originals, C 0x4FC7F8 / F 0x4FB830.</summary>
    public const string NeustartFrage = "Möchten Sie die Mission erneut starten?";

    private static bool Schalter(string s) => System.Array.IndexOf(Core.CommandLine.Args, s) >= 0;

    /// <summary><c>--neustart-alt</c>: der alte Knopf (sofort neu laden, keine Abfrage).</summary>
    public static bool NeustartAlt => Schalter("--neustart-alt");

    /// <summary><c>--neustart-oertlich</c>: Nullmodell, 980 wirkt nur beim Absender.</summary>
    public static bool NeustartOertlich => Schalter("--neustart-oertlich");

    /// <summary>Einzelspiel: der Missions-Reset (die Oberfläche lädt die Szene neu).</summary>
    public System.Action? OnNeustartEinzel;

    /// <summary>Netz: die Oberfläche schliesst ihre Fenster (0x44FE10(1)).</summary>
    public System.Action? OnNeustartNetz;

    /// <summary>Für die Prüfstände: wie oft 980 gewirkt hat, und der Abdruck danach.</summary>
    public int NeustartAngewandt;
    public ulong NeustartAbdruckNach, NetzStartAbdruck;
    public int NeustartTakt = -1;

    private string? _netzStartJson;
    private List<Entity>? _netzStartVorlagen;

    /// <summary>Hat die Partie einen festgehaltenen Startstand (Netz)?</summary>
    public bool NetzStartstandDa => _netzStartJson != null;

    /// <summary>Aus <c>NetGameRunner.Bootstrap</c>, im Takt des gemeinsamen Anfangs, auf
    /// JEDER Maschine mit demselben Stand. Liest nur.</summary>
    public void NetzStartstandMerken()
    {
        _netzStartJson = SaveStateJson("", "Netz-Startstand (Befehl 980)");
        _netzStartVorlagen = new List<Entity>();
        foreach (var e in _entities) if (!e.IsProp) _netzStartVorlagen.Add(e.Klon());
        NetzStartAbdruck = DeterminismChecksum();
        GD.Print($"neustart: Startstand festgehalten bei Takt {CommandTicks}, Abdruck {NetzStartAbdruck:X16}, " +
                 $"{_netzStartJson.Length} Zeichen");
    }

    /// <summary>Der Absender: »Ja« der Abfrage Art 10/3 (F 0x44A17F).</summary>
    public bool PostNeustart()
        => Emit(CommandRecord.Make(CommandOp.Neustart, (byte)ViewPlayer));

    /// <summary>Der Behandler (F 0x4C3C84 / C 0x4C4113), auf jeder Maschine im selben Takt.</summary>
    private bool ApplyNeustart(in CommandRecord c)
    {
        if (NeustartOertlich && c.Player != ViewPlayer) return false;   // Nullmodell
        if (ImNetz)
        {
            if (Alt2("neustart") || _netzStartJson == null)
            {
                GD.Print("neustart: im Netz ohne Startstand — 980 bleibt ohne Wirkung");
                return false;
            }
            var json = new Json();
            if (json.Parse(_netzStartJson) != Error.Ok || json.Data.VariantType != Variant.Type.Dictionary)
                return false;
            var root = json.Data.AsGodotDictionary<string, Variant>();
            // Die Vorlagen des Anfangs zurück in die Liste — ApplySaveState sucht seine
            // Kartenvorlagen (bug-506) in der laufenden Liste; Einheiten, die seitdem in
            // eine Garage gefahren sind, stünden dort sonst nicht mehr.
            if (_netzStartVorlagen != null)
            {
                _entities.RemoveAll(e => !e.IsProp);
                foreach (var v in _netzStartVorlagen) _entities.Add(v.Klon());
            }
            NetzStandEinspielen(root);
            // 0x44FE10(1) → Schliesser 0x4472F5: Art 40 zu, Pausenwort 0 — hier auf JEDER
            // Maschine, weil der Behandler überall läuft.
            bool warPause = NetzPause != 0;
            NetzPause = 0;
            if (warPause) OnNetzPause?.Invoke(false, c.Player);
            OnNeustartNetz?.Invoke();
            NeustartAngewandt++;
            NeustartTakt = CommandTicks;
            NeustartAbdruckNach = DeterminismChecksum();
            GD.Print($"neustart: Befehl 980 von Spieler {c.Player} in Takt {CommandTicks} — Startstand " +
                     $"eingespielt, Abdruck {NeustartAbdruckNach:X16} (Anfang {NetzStartAbdruck:X16}, " +
                     $"{(NeustartAbdruckNach == NetzStartAbdruck ? "GLEICH" : "UNGLEICH")})");
            return true;
        }
        NeustartAngewandt++;
        NeustartTakt = CommandTicks;
        GD.Print($"neustart: Befehl 980 in Takt {CommandTicks} — Missions-Reset (Szene neu)");
        OnNeustartEinzel?.Invoke();
        return true;
    }

    /// <summary>Der Fingerabdruck für <c>--neustart-check</c>: Takt, Uhr, Geld, Einheiten,
    /// Forschungsbesitz des Sichtspielers, Prüfsumme.</summary>
    public string NeustartFingerabdruck()
    {
        int lebend = 0;
        foreach (var e in _entities) if (!e.IsProp && !e.Dead) lebend++;
        int forsch = 0;
        for (int id = 0; id < 128; id++) if (ForschungBesitzt(ViewPlayer, id)) forsch++;
        var geld = new System.Text.StringBuilder();
        for (int p = 0; p < _money.Length; p++) geld.Append(p == 0 ? "" : "/").Append(_money[p]);
        return $"takt={_taktNr} uhr={_clock:0.000} geld={geld} einheiten={lebend} flieger={_special.Count} " +
               $"forschung={forsch} summe={DeterminismChecksum():X16}";
    }
}
