using Godot;
namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐⭐ <b>»DER FEIND NAHT« — KLANG 122</b>
/// (KayelGee 14.10.2026, Punkte 7 und 14; bug-575).
///
/// <para><b>Was der Satz ist.</b> Klang 122 der Bank (<c>s0122.wav</c>, 1,04 s)
/// sagt »Der Feind naht« — abgehört per Spracherkennung über den ganzen
/// Ansagenblock 120..143 (122: »der Feind naht«, 131: »Ein Gebäude wird
/// angegriffen«, 600/601: »Untermission beendet/gescheitert«). Kein anderer
/// Klang der Bank 120..299 sagt »Feind« oder »naht«.</para>
///
/// <para><b>Wer ihn spielt — NUR DIE KI, nie das Missionsskript.</b> Vollerhebung
/// der Bytefolge <c>push 0,0,0 / push 0x7A / call Klangroutine</c>: genau
/// <b>2</b> Stellen in C, <b>2</b> in F, beide in der KI-Runde:</para>
/// <code>
///   Stelle 1  C 0x4BC79D / F 0x4BC25D   Gruppenangriff 0x4BC540 (Sektorangriff)
///             nach Ziel (0x4BC3D0) UND Wegsuche (0x4D2580) fuer die ERSTE
///             abrufbereite Einheit des Sektors:
///               word[0xBCA0E0] (Weglaenge)   &lt; 50        ; sonst Nachbar ganz aus
///               word[0x538BAC] (Sperre)      == 0
///               word[0x539934] (Mission)     != 14
///               byte[0x87B155 + 40*p + ich]  == 0         ; p ist MEIN Feind
///             -> Klang 122 (Modus 0, ohne Ort), Sperre := rand%3000 + 4000
///   Stelle 2  C 0x4BD1C5 / F 0x4BCC85   Gruppenlauf 0x4BCF30, nach der
///             Gueltigkeit des Auftrags, wenn die Gruppe KEINE Wegpunkte mehr
///             hat (byte[sec108 + 62*(4p+g) + 1] == 0), also im Angriff:
///               Sperre == 0, Buendnisbyte == 0
///             -> Klang 122, Sperre := rand%2000 + 4000
///   Die Sperre zaehlt je Haupttakt eins herunter: C 0x4160AC
///   (dec word[0x538BAC], neben inc dword[0x4FA240]).
/// </code>
/// <para>Die Skripte spielen nur 600/601 (145 Stellen, v0.6.5 wie heute) —
/// »Der Feind naht« kommt also nie aus einer Missionsregel.</para>
///
/// <para><b>Bei uns war er NIE gebaut</b> — weder in 0.6.5/0.6.6 noch später
/// (<c>git log -G</c> über die ganze Geschichte: keine Abspielstelle für 122;
/// <c>SkirmishAiSectors.cs</c> nannte ihn seit dem 01.09.2026 ausdrücklich
/// »NICHT gebaut«). Ein Rückfall ist es also nicht.</para>
///
/// <para>Stelle 1 sitzt HINTER dem Wegtor (KiWegtor.cs): ein Sektorangriff, der
/// am Weg scheitert, sagt auch nichts an.</para>
///
/// <para>⚠ <b>SETZUNGEN, benannt:</b> (a) Die Sperre
/// würfelt mit <see cref="Audio.GameSounds.KlangWuerfel"/>, nicht mit dem
/// Spielwürfel: sie hängt am Betrachter und wäre sonst ein Gleichschritt-Bruch
/// (wie bug-529). (b) Ein Haupttakt sind <see cref="Simulation.Zeitbasis.OriginalHz"/>
/// je Sekunde (selbst eine Setzung, siehe dort): 4000..6999 Takte sind bei 50 Hz
/// 80..140 s.</para>
///
/// <para>Gegenschalter: <c>--feind-naht-alt</c> (stumm wie bis 0.6.9),
/// Prüfstand <c>--feind-naht-check</c>, siehe FeindNahtCheck.cs.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Klang 122, »Der Feind naht«.</summary>
    public const int KlangFeindNaht = 122;

    /// <summary><c>--feind-naht-alt</c>: Klang 122 bleibt stumm (Stand bis 0.6.9).</summary>
    public static bool FeindNahtAlt;

    /// <summary>Ab wann die Sperre <c>word[0x538BAC]</c> wieder 0 ist, in
    /// Sekunden auf <see cref="_clock"/>. EINE Sperre für das ganze Spiel, wie im
    /// Original (ein Wort, kein Feld je Spieler).</summary>
    private float _feindNahtFreiAb;

    /// <summary>Zähler für den Prüfstand: [Stelle 1/2] gespielt, von der Sperre
    /// geschluckt, vom Bündnis- oder Missionstor geschluckt.</summary>
    public readonly int[] FeindNahtGespielt = new int[3];
    public readonly int[] FeindNahtGesperrt = new int[3];
    public readonly int[] FeindNahtFreund = new int[3];
    public int FeindNahtM14;
    /// <summary>Das Protokoll jeder gespielten Ansage (Takt, Stelle, Spieler).</summary>
    public readonly System.Collections.Generic.List<string> FeindNahtProtokoll = new();

    /// <summary>
    /// Die Ansage, wie die beiden Stellen sie prüfen. <paramref name="stelle"/>
    /// 1 = Sektorangriff 0x4BC79D, 2 = Gruppenlauf 0x4BD1C5.
    /// </summary>
    private void FeindNahtMelden(int p, int stelle)
    {
        if (FeindNahtAlt) return;
        if (stelle is < 1 or > 2) return;
        // word[0x538BAC] == 0 — die Sperre zuerst, wie an beiden Stellen
        if (_clock < _feindNahtFreiAb) { FeindNahtGesperrt[stelle]++; return; }
        // nur Stelle 1: cmp word[0x539934], 0xE / je
        if (stelle == 1 && UI.SkirmishSetup.CampaignMission == 14) { FeindNahtM14++; return; }
        // byte[0x87B155 + 40*p + 0x4FA284] == 0: p ist kein Verbuendeter des Betrachters
        int ich = ViewPlayer;
        if (ich is < 0 or > 7 || !AiHostile(p, ich)) { FeindNahtFreund[stelle]++; return; }

        FeindNahtGespielt[stelle]++;
        int takte = 4000 + Audio.GameSounds.KlangWuerfel(stelle == 1 ? 3000 : 2000);
        _feindNahtFreiAb = _clock + takte / (float)Simulation.Zeitbasis.OriginalHz;
        string zeile = $"Feind naht: Klang {KlangFeindNaht} an Stelle {(stelle == 1 ? "0x4BC79D (Sektorangriff)" : "0x4BD1C5 (Gruppenlauf)")} " +
                       $"von Spieler {p}, Takt {_taktNr} ({_clock:0.0} s), Sperre {takte} Takte";
        FeindNahtProtokoll.Add(zeile);
        GD.Print(zeile);
        // Modus 0, ohne Ort — wie der Trefferklang des Gebaeudes (131).
        if (UI.Settings.Announcements) Audio.SoundBankPlayer.Play(KlangFeindNaht);
    }
}
