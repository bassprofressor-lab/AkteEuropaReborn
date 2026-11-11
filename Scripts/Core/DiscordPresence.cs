#nullable enable
using Godot;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AkteEuropaReborn.Core;

/// <summary>
/// ⭐ 11.11.2026 — <b>DISCORD-ANZEIGE »spielt Akte Europa Reborn«</b> (Rich Presence).
///
/// <para>⚠ <b>UNSERE ZUTAT, vollständig</b> — das Original von 1997 kennt kein
/// Discord. Nichts hier berührt die Simulation: der Beobachter LIEST nur
/// statische Felder (Szene, Mission, Karte, Netzsitzung), würfelt nichts und
/// schreibt nichts zurück. NET07 und jeder Lockstep bleiben unberührt.</para>
///
/// <para><b>Wie:</b> ohne fremde Bibliothek, über die lokale Schnittstelle der
/// Discord-App — die benannte Leitung <c>\\.\pipe\discord-ipc-0..9</c>. Ein Rahmen
/// ist <c>Opcode (uint32 LE) · Länge (uint32 LE) · JSON</c>; Opcode 0 =
/// Handschlag <c>{v:1, client_id}</c>, Opcode 1 = Befehl <c>SET_ACTIVITY</c>.
/// Läuft Discord nicht, passiert nichts; die Verbindung wird alle 30 s neu
/// versucht.</para>
///
/// <para><b>Faden:</b> die ganze Leitung lebt in EINEM eigenen Hintergrundfaden.
/// Der Hauptfaden baut nur alle 5 s den Anzeigetext (Godot-Zugriffe nur dort)
/// und reicht ihn als fertigen Text hinüber. Das Spiel wartet nie auf Discord.</para>
///
/// <para>Aus: Einstellung »Discord-Anzeige« (<see cref="UI.Settings.DiscordPresence"/>),
/// Schalter <c>--discord-aus</c>, und von selbst in jedem kopflosen Lauf und
/// unter dem Prüfschutz (<see cref="Pruefschutz.Aktiv"/>) — ein Prüfstand soll
/// niemandem einen Status in Discord setzen.</para>
/// </summary>
public partial class DiscordPresence : Node
{
    /// <summary>Die Anwendung »Akte Europa Reborn« im Discord Developer Portal
    /// (angelegt vom Spieler am 11.11.2026).</summary>
    public const string ClientId = "1570153057352814642";

    /// <summary>Name des Bildes unter Rich Presence → Art Assets. ⚠ Discord nimmt
    /// beim Hochladen den DATEINAMEN als Namen — gewählt wurde Variante C.
    /// Nachsehen: GET discord.com/api/v10/oauth2/applications/&lt;ClientId&gt;/assets
    /// (mit ?x=… gegen den Zwischenspeicher, sonst kommt kurz [] zurück).</summary>
    public const string BildSchluessel = "presence_c_militaer";

    private const double Takt = 5.0;              // Sekunden zwischen zwei Blicken auf den Zustand
    private const int NeuVersuchMs = 30_000;      // Discord nicht da -> so lange warten

    private bool _gesperrt;                       // kopflos / Prüfschutz / --discord-aus
    private double _uhr = Takt;                   // erster Blick sofort
    private string _letzterSchluessel = "";       // was zuletzt angezeigt wurde (ohne Zeit)
    private long _seit;                           // Unix-Sekunden: Beginn des jetzigen Zustands
    private string _version = "";

    // --- an den Hintergrundfaden ---
    private readonly object _sperre = new();
    private string? _ausstehend;                  // JSON der Aktivität, "" = löschen, null = nichts neu
    private readonly AutoResetEvent _wecker = new(false);
    private Thread? _faden;
    private volatile bool _ende;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;     // auch in der Pause weiterzählen
        _version = ProjectSettings.GetSetting("application/config/version", "").AsString();

        _gesperrt = DisplayServer.GetName() == "headless"
                    || Pruefschutz.Aktiv
                    || !OS.HasFeature("windows")
                    || Array.IndexOf(OS.GetCmdlineUserArgs(), "--discord-aus") >= 0
                    || Array.IndexOf(OS.GetCmdlineArgs(), "--discord-aus") >= 0;
        if (_gesperrt) { SetProcess(false); return; }

        _faden = new Thread(Leitungsfaden) { IsBackground = true, Name = "DiscordPresence" };
        _faden.Start();
    }

    public override void _Process(double delta)
    {
        _uhr += delta;
        if (_uhr < Takt) return;
        _uhr = 0;

        if (!UI.Settings.DiscordPresence)
        {
            if (_letzterSchluessel != "") { _letzterSchluessel = ""; Uebergeben(""); }
            return;
        }

        var (details, state, party) = Zustand();
        string schluessel = details + "|" + state + "|" + party;
        if (schluessel == _letzterSchluessel) return;

        // Die Uhr in Discord läuft ab dem Wechsel von Modus/Mission/Karte, nicht
        // ab jeder Kleinigkeit (Spielerzahl in der Lobby u. ä.).
        if (details != _letzterSchluessel.Split('|')[0])
            _seit = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _letzterSchluessel = schluessel;

        Uebergeben(Aktivitaet(details, state, party));
    }

    /// <summary>Was gerade gespielt wird — nur lesen, nichts verändern.</summary>
    private (string Details, string State, (int, int)? Party) Zustand()
    {
        string szene = GetTree().CurrentScene?.SceneFilePath ?? "";
        bool imSpiel = szene == UI.SkirmishSetup.GameScene;
        bool netz = Network.NetworkManager.Active;

        if (Editor.MapEditSession.Active)
            return ("Im Karteneditor", "Baut eine Karte", null);

        if (!imSpiel)
        {
            if (netz)
                return ("Mehrspieler", "Wartet auf Mitspieler", null);
            return ("Im Hauptmenü", "", null);
        }

        int mission = UI.SkirmishSetup.CampaignMission;
        if (mission > 0)
        {
            var m = Campaign.CampaignManager.ByIndex(mission);
            string titel = m != null && m.Title.Length > 0 ? m.Title : $"Mission {mission}";
            int gesamt = Campaign.CampaignManager.Missions.Count;
            return ($"Kampagne · Mission {mission}" + (gesamt > 0 ? $" von {gesamt}" : ""), titel, null);
        }

        string karte = KartenName(UI.SkirmishSetup.Map);
        if (netz)
        {
            int menschen = Network.NetworkManager.HumanSlots().Length;
            return ($"Mehrspieler · {karte}", "Partie läuft", menschen > 0 ? (menschen, System.Math.Max(menschen, 8)) : null);
        }

        int ki = UI.SkirmishSetup.AiCount;
        return ($"Gefecht · {karte}", ki == 1 ? "gegen 1 KI-Gegner" : $"gegen {ki} KI-Gegner", null);
    }

    private static string KartenName(string map)
        => map.StartsWith("map_", StringComparison.OrdinalIgnoreCase) ? map[4..] : map;

    private string Aktivitaet(string details, string state, (int Size, int Max)? party)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("details", Kappen(details));
            if (state.Length > 0) w.WriteString("state", Kappen(state));
            w.WriteStartObject("timestamps"); w.WriteNumber("start", _seit); w.WriteEndObject();
            w.WriteStartObject("assets");
            w.WriteString("large_image", BildSchluessel);
            w.WriteString("large_text", _version.Length > 0 ? $"Akte Europa Reborn {_version}" : "Akte Europa Reborn");
            w.WriteEndObject();
            if (party is { } p)
            {
                w.WriteStartObject("party");
                w.WriteStartArray("size"); w.WriteNumberValue(p.Size); w.WriteNumberValue(p.Max); w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // Discord verlangt 2..128 Zeichen je Textfeld.
    private static string Kappen(string s) => s.Length > 128 ? s[..128] : s.Length < 2 ? s.PadRight(2) : s;

    private void Uebergeben(string aktivitaetJson)
    {
        lock (_sperre) _ausstehend = aktivitaetJson;
        _wecker.Set();
    }

    public override void _ExitTree()
    {
        if (_faden == null) return;
        Uebergeben("");                           // Anzeige beim Beenden löschen
        _ende = true;
        _wecker.Set();
        _faden.Join(1500);
    }

    // ===================== Hintergrundfaden (kein Godot-Zugriff!) =====================

    private NamedPipeClientStream? _leitung;
    private string? _zuletztGesendet;

    private void Leitungsfaden()
    {
        int pid = System.Environment.ProcessId;
        while (true)
        {
            _wecker.WaitOne(_leitung == null && _zuletztGesendet != null ? NeuVersuchMs : Timeout.Infinite);

            string? neu;
            lock (_sperre) { neu = _ausstehend; _ausstehend = null; }
            if (neu != null) _zuletztGesendet = neu;
            if (_zuletztGesendet == null) { if (_ende) break; continue; }

            try
            {
                if (_leitung == null && !Verbinden()) { if (_ende) break; continue; }
                SetzeAktivitaet(pid, _zuletztGesendet);
            }
            catch (Exception)
            {
                Schliessen();                     // Discord beendet o. ä. -> später neu
            }
            if (_ende) break;
        }
        Schliessen();
    }

    private bool Verbinden()
    {
        for (int i = 0; i < 10; i++)
        {
            var l = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                l.Connect(200);
                _leitung = l;
                Schreiben(0, $"{{\"v\":1,\"client_id\":\"{ClientId}\"}}");
                Lesen();                          // READY oder Fehler
                return true;
            }
            catch (Exception)
            {
                l.Dispose();
                _leitung = null;
            }
        }
        return false;
    }

    private void SetzeAktivitaet(int pid, string aktivitaet)
    {
        string args = aktivitaet.Length == 0
            ? $"{{\"pid\":{pid}}}"                                   // ohne activity = löschen
            : $"{{\"pid\":{pid},\"activity\":{aktivitaet}}}";
        Schreiben(1, $"{{\"cmd\":\"SET_ACTIVITY\",\"args\":{args},\"nonce\":\"{Guid.NewGuid():N}\"}}");
        Lesen();
    }

    private void Schreiben(int opcode, string json)
    {
        byte[] nutz = Encoding.UTF8.GetBytes(json);
        byte[] rahmen = new byte[8 + nutz.Length];
        BitConverter.TryWriteBytes(rahmen.AsSpan(0, 4), (uint)opcode);
        BitConverter.TryWriteBytes(rahmen.AsSpan(4, 4), (uint)nutz.Length);
        nutz.CopyTo(rahmen, 8);
        _leitung!.Write(rahmen, 0, rahmen.Length);
        _leitung.Flush();
    }

    /// <summary>Eine Antwort abholen (sonst läuft der Puffer der Leitung voll).
    /// Höchstens 3 s warten; Opcode 2 = Discord schließt.</summary>
    private void Lesen()
    {
        byte[] kopf = new byte[8];
        LeseGenau(kopf);
        uint op = BitConverter.ToUInt32(kopf, 0);
        int laenge = (int)BitConverter.ToUInt32(kopf, 4);
        if (laenge < 0 || laenge > 1 << 20) throw new IOException("Rahmen zu gross");
        if (laenge > 0) LeseGenau(new byte[laenge]);
        if (op == 2) throw new IOException("Discord hat geschlossen");
    }

    private void LeseGenau(byte[] puffer)
    {
        int gelesen = 0;
        while (gelesen < puffer.Length)
        {
            Task<int> t = _leitung!.ReadAsync(puffer, gelesen, puffer.Length - gelesen);
            if (!t.Wait(3000)) throw new IOException("Discord antwortet nicht");
            if (t.Result <= 0) throw new IOException("Leitung zu");
            gelesen += t.Result;
        }
    }

    private void Schliessen()
    {
        try { _leitung?.Dispose(); } catch (Exception) { }
        _leitung = null;
    }
}
