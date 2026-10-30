namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE SETUP-ZIELE</b> (22.10.2026, Paket W4, bug-636) — die
/// <c>add_target</c>-Rufe des Missions-SETUPS.
///
/// <para>Der Missionsaufbau des Kampagnenskripts leert die Zieltafel sec69
/// (C <c>0x4883D4</c>), setzt alle acht Betriebsarten auf 2 (<c>0x48843F</c>) und
/// springt dann nach der Missionsnummer in den Setup-Fall der Mission
/// (C <c>0x4884BE jmp [ecx*4+0x494274]</c>, F <c>0x487110 jmp [ecx*4+0x4937D4]</c>).
/// Darin stehen <b>336 Rufe auf add_target</b> (C <c>0x4CF700</c>, Thunk
/// <c>0x4023E7</c>; F Thunk <c>0x4023D8</c>) in 30 Missionen, C und F gleich —
/// 16 davon sind Schleifen. Bei uns las sie niemand: Data/mission_scripts.json
/// trug 0 Setup-Ziele, und ein Rechnerspieler ohne Liste fiel auf
/// <c>AiSektorAngriff</c> zurück (SkirmishAi.cs). Belegt an sec69 der
/// Originalstände: 5/3/6/7/10.DM (M25) und 1.DM (M26) tragen genau diese Rufe,
/// Eintrag für Eintrag (berichte/ki-eingaben-k25-k26-opus.md §2).</para>
///
/// <para><b>Die vier Schleifenformen</b>, alle in C gelesen (je Fassung gegen eine
/// Mustervorlage gehalten von aekernel-tools/mission_setupziele.py):</para>
/// <code>
///  eigene  M25 0x48EE58..0x48EEC2 (auch M21 M22 M27 M29..M33):
///          for bl = 0..7: for g = 0..254:
///            if byte[0xC06914+76g] != 0 (Typ) and byte[0xC06915+76g] == bl (Besitzer):
///              add_target(bl, 1, 6, g, 0)
///  gebaut  M24 0x48E69B..0x48E6FD: dasselbe, dazu byte[0xC06928+76g] (+0x18) != 0
///  fremde  M29 0x490EAA..0x490F0A (auch M30..M33):
///          for q = 1..6: for g = 0..254:
///            if Typ != 0 and (Besitzer == 0 or Besitzer == 11): add_target(q, 1, 6, g, 0)
///  einheit M24 0x48E737..0x48E754: for bl = 0..19: add_target(1, 2, 4, bl, 0)
/// </code>
/// <para>add_target selbst (C <c>0x4CF700</c>) schreibt in den ersten freien Satz
/// (Art 0) der 100er-Liste des Spielers, ohne Dublettenprüfung — beim
/// Missionsstart also in Rufreihenfolge hintereinander.</para>
///
/// <para><b>set_ai im Setup:</b> Jeder Setup-Block ruft zusätzlich
/// <c>set_ai(p, 1)</c> (<c>0x4D0EE0</c>, <c>byte[0x538BB8+p]</c> = sec131) und
/// <c>0x4D0970</c> (<c>byte[0xB46198+arg1]</c> = sec77). Beide kamen bei uns NICHT
/// an. sec131 hat genau zwei Leser: die KI-Runde Takt 10 (C <c>0x4BFD59</c>: nur bei
/// sec131 != 0 kauft <c>0x4BE330</c> Versorgungshelis) und einen Zeichner
/// (<c>0x484DE3</c>). sec77 hat außer dem Lader keinen Leser. Den KI-Helikauf
/// <c>0x4BE330</c> haben wir nicht gebaut — die Werte werden darum nur in
/// <see cref="_aiSec131"/> mitgeschrieben und gemeldet, ohne Wirkung.</para>
///
/// <para>Gegenschalter <c>--setup-ziele-aus</c> (Stand vor dem 22.10.2026).
/// Prüfstand <c>--setup-ziele-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--setup-ziele-aus</c> — der Stand vor Paket W4: keine Setup-Ziele.</summary>
    public static bool SetupZieleAus;

    /// <summary>sec131 je Spieler (<c>byte[0x538BB8+p]</c>), geschrieben von
    /// <c>set_ai</c> aus Setup und Skript. ⚠ Ohne Leser bei uns — siehe Klassenkopf.</summary>
    private readonly int[] _aiSec131 = new int[8];

    /// <summary>Wurde dieser Stand aus einem Spielstand geladen? Dann trägt der
    /// Spielstand die Zieltafel selbst (KiZustandLesen), und das Setup läuft nicht
    /// noch einmal darüber — es wäre eine Verdopplung.</summary>
    private bool _standGeladen;

    /// <summary>Wie viele Einträge das Setup anlegte, je Spieler — für Prüfstand und Zeile.</summary>
    public readonly int[] SetupZieleAngelegt = new int[8];
    public bool SetupZieleGelaufen { get; private set; }

    /// <summary>set_ai — <c>byte[0x538BB8+p] = wert</c> (<c>0x4D0EE0</c>).</summary>
    public void SetAiSec131(int spieler, int wert, string herkunft)
    {
        if (spieler is < 0 or > 7) return;
        _aiSec131[spieler] = wert & 0xFF;
        GD.Print($"set_ai P{spieler} = {wert} ({herkunft}; sec131, Leser KI-Takt 10 0x4BE330" +
                 (KiHelikaufAus ? ", --ki-helikauf-aus: ohne Wirkung)" : ", Simulation/KiHelikauf.cs)"));
    }

    /// <summary>
    /// Die Setup-Ziele beim Missionsstart anwenden — nach dem Vorspann der
    /// Betriebsart, vor dem ersten Skripttakt, in Rufreihenfolge.
    /// </summary>
    public void SetupZieleAnwenden(Campaign.MissionScript ms, int mission)
    {
        // bug-675: nach dem Laden eines Stands MIT sec131 traegt der Stand den Wert
        // (auch was das Missionsskript spaeter per set_ai gesetzt hat).
        if (_standGeladen && KiZustandGeladen && _sec131Geladen)
            GD.Print($"set_ai M{mission}: sec131 aus dem Spielstand ({string.Join(",", _aiSec131)}), Setup nicht erneut");
        else
            foreach (var (kind, a, b, at) in ms.SetupSetAi)
                if (kind == "set_ai") SetAiSec131(a, b, $"Setup M{mission} @{at}");
        if (SetupZieleAus)
        {
            if (ms.SetupZiele.Count > 0)
                GD.Print($"Setup-Ziele M{mission}: {ms.SetupZiele.Count} Rufe NICHT angewandt (--setup-ziele-aus)");
            return;
        }
        if (_standGeladen && KiZustandGeladen)
        {
            GD.Print($"Setup-Ziele M{mission}: aus dem Spielstand (KI-Zustand geladen), nicht neu angelegt");
            return;
        }
        Array.Clear(SetupZieleAngelegt);
        // die Gebaeudesaetze nach Platz — der Platz ist der Satzindex des Originals
        var platz = new SortedDictionary<int, Entity>();
        foreach (var e in _entities)
            if (e.IsBuilding && e.Slot is >= 0 and < 255 && !platz.ContainsKey(e.Slot)) platz[e.Slot] = e;
        // Typ != 0: ein zerstoerter Satz traegt Typ 0 (@0x4C9A8E), bei uns Dead.
        bool TypDa(Entity e) => e.BType != 0 && !e.Dead;
        // Besitzer als Byte: herrenlos ist 255, bei uns -1.
        int BesitzerByte(Entity e) => e.Owner is >= 0 and <= 254 ? e.Owner : 255;
        bool m26ohneTuer = mission == 26 && Campaign.MissionScript.SetzungAus.Contains("--m26-tuergriff-aus");
        int n = 0;
        foreach (var z in ms.SetupZiele)
        {
            switch (z.Schleife)
            {
                case "":
                    // ⚠ --m26-tuergriff-aus: frueher hob es UNSERE Setzung auf; seit W4 ist
                    // der Eintrag das Original (C 0x48F9A5), der Schalter bleibt als Nullmodell.
                    if (m26ohneTuer && z.At.Equals("0x48F9A5", StringComparison.OrdinalIgnoreCase)) continue;
                    AddMissionTarget(z.A, z.B, z.C, z.D, z.E);
                    if (z.A is >= 0 and <= 7) SetupZieleAngelegt[z.A]++;
                    n++;
                    break;
                case "einheiten":
                    for (int w = z.Von; w <= z.Bis; w++)
                    {
                        AddMissionTarget(z.A, z.B, z.C, w, z.E);
                        if (z.A is >= 0 and <= 7) SetupZieleAngelegt[z.A]++;
                        n++;
                    }
                    break;
                case "gebaeude":
                    for (int p = z.SpielerVon; p <= z.SpielerBis; p++)
                        foreach (var (g, e) in platz)
                        {
                            if (!TypDa(e)) continue;
                            if (z.Gebaut && e.Built == 0) continue;
                            int bes = BesitzerByte(e);
                            if (z.BesitzerIstSpieler ? bes != p : !z.Besitzer.Contains(bes)) continue;
                            AddMissionTarget(p, z.B, z.C, g, z.E);
                            if (p is >= 0 and <= 7) SetupZieleAngelegt[p]++;
                            n++;
                        }
                    break;
                default:
                    GD.PrintErr($"Setup-Ziele M{mission}: unbekannte Schleife »{z.Schleife}« @{z.At}");
                    break;
            }
        }
        SetupZieleGelaufen = true;
        GD.Print($"Setup-Ziele M{mission}: {n} Eintraege aus {ms.SetupZiele.Count} Rufen — " +
                 string.Join(" ", Enumerable.Range(0, 8).Where(p => SetupZieleAngelegt[p] > 0)
                                            .Select(p => $"P{p}:{SetupZieleAngelegt[p]}")));
    }

    // ================= der Pruefstand ==========================================

    /// <summary><c>--setup-ziele-soll=PFAD</c> — die Sollwerte (JSON) des Prüfstands.</summary>
    public static string SetupZieleSoll = "";

    /// <summary>
    /// <c>--setup-ziele-check</c> — die Zieltafel jedes Spielers nach dem Setup
    /// gegen zwei unabhängige Sollwerte (Datei <c>--setup-ziele-soll</c>, gebaut von
    /// scratchpad/w4/soll.py):
    /// <list type="number">
    /// <item><b>capstone</b>: die feste Rufliste aus der zweiten Lesung
    /// (scratchpad/k26e/setup_C.txt, ein anderer Leser als mission_setupziele.py),
    /// die Schleifen über sec3 der ORIGINALKARTE (D:/E:\LEVELS\NN.CWM) ausgerechnet
    /// — nicht über unsere Entitäten.</item>
    /// <item><b>dm</b>: sec69 der Originalstände (M25: 5/3/6/7/10.DM, M26: 1.DM),
    /// Eintrag für Eintrag (Art, Vorrang, Wort, c).</item>
    /// </list>
    /// Nullmodell <c>--setup-ziele-aus</c>: M25 P2 0/20 → DURCHGEFALLEN.
    /// </summary>
    public string SetupZieleCheck()
    {
        int m = UI.SkirmishSetup.CampaignMission;
        var sb = new StringBuilder($"setup-ziele-check K{m}, --setup-ziele-aus {SetupZieleAus}\n");
        if (!InCampaign || m <= 0) return sb.Append("setup-ziele-check: KEIN URTEIL (nur mit -- --campaign=N)").ToString();
        MissionScriptTick(0.001f);      // das Setup laeuft im ersten Skripttakt
        var ist = new List<(int K, int V, int W, int C)>[8];
        for (int p = 0; p < 8; p++)
            ist[p] = _missionTargets[p].Select(t => (t.Kind, t.Priority, t.Word, t.Second)).ToList();
        sb.AppendLine("  Ist: " + string.Join(" ", Enumerable.Range(0, 8).Select(p => $"P{p}:{ist[p].Count}")));

        if (SetupZieleSoll.Length == 0 || !System.IO.File.Exists(SetupZieleSoll))
            return sb.Append($"setup-ziele-check K{m}: KEIN URTEIL (Solldatei fehlt: »{SetupZieleSoll}«)").ToString();
        var json = new Json();
        if (json.Parse(System.IO.File.ReadAllText(SetupZieleSoll)) != Error.Ok)
            return sb.Append("setup-ziele-check: KEIN URTEIL (Solldatei unlesbar)").ToString();
        var root = json.Data.AsGodotDictionary<string, Variant>();
        if (!root.TryGetValue(m.ToString(), out var mv))
            return sb.Append($"setup-ziele-check K{m}: KEIN URTEIL (keine Sollwerte fuer M{m})").ToString();
        var md = mv.AsGodotDictionary<string, Variant>();

        List<(int K, int V, int W, int C)>[] Lies(Godot.Collections.Dictionary<string, Variant> d)
        {
            var r = new List<(int, int, int, int)>[8];
            for (int p = 0; p < 8; p++)
            {
                r[p] = new();
                if (!d.TryGetValue(p.ToString(), out var lv)) continue;
                foreach (var q in lv.AsGodotArray())
                {
                    var t = q.AsGodotArray();
                    r[p].Add((t[0].AsInt32(), t[1].AsInt32(), t[2].AsInt32(), t[3].AsInt32()));
                }
            }
            return r;
        }

        int quellen = 0, gleich = 0;
        var zeilen = new List<string>();
        void Vergleiche(string name, List<(int K, int V, int W, int C)>[] soll)
        {
            quellen++;
            int ok = 0, abw = 0;
            var teile = new List<string>();
            for (int p = 0; p < 8; p++)
            {
                if (soll[p].Count == 0 && ist[p].Count == 0) continue;
                bool same = soll[p].SequenceEqual(ist[p]);
                int treffer = 0;
                for (int i = 0; i < Math.Min(soll[p].Count, ist[p].Count); i++)
                    if (soll[p][i] == ist[p][i]) treffer++;
                teile.Add($"P{p} {treffer}/{soll[p].Count}{(same ? "" : $" (ist {ist[p].Count})")}");
                if (same) ok++; else abw++;
                if (!same)
                {
                    int i0 = Enumerable.Range(0, Math.Max(soll[p].Count, ist[p].Count))
                        .FirstOrDefault(i => i >= soll[p].Count || i >= ist[p].Count || soll[p][i] != ist[p][i]);
                    string S(List<(int K, int V, int W, int C)> l, int i) => i < l.Count ? $"({l[i].K},{l[i].V},{l[i].W},{l[i].C})" : "—";
                    zeilen.Add($"    {name} P{p}: erste Abweichung an Stelle {i0}: soll {S(soll[p], i0)}, ist {S(ist[p], i0)}");
                }
            }
            if (abw == 0) gleich++;
            sb.AppendLine($"  {name}: {(abw == 0 ? "GLEICH" : "ABWEICHUNG")} — {string.Join(", ", teile)}");
        }

        if (md.TryGetValue("capstone", out var cv)) Vergleiche("capstone", Lies(cv.AsGodotDictionary<string, Variant>()));
        if (md.TryGetValue("dm", out var dv))
            foreach (var kv in dv.AsGodotDictionary<string, Variant>())
                Vergleiche(kv.Key, Lies(kv.Value.AsGodotDictionary<string, Variant>()));
        foreach (var z in zeilen) sb.AppendLine(z);
        string urteil = quellen == 0 ? "KEIN URTEIL (keine Quelle)" : gleich == quellen ? "BESTANDEN" : "DURCHGEFALLEN";
        string p2 = m == 25 ? $", M25 P2 {ist[2].Count}/20" : "";
        sb.Append($"setup-ziele-check K{m}: {gleich}/{quellen} Quellen gleich, Ist " +
                  string.Join(" ", Enumerable.Range(0, 8).Where(p => ist[p].Count > 0).Select(p => $"P{p}:{ist[p].Count}")) +
                  $"{p2} — {urteil}");
        return sb.ToString();
    }
}
