namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 30.10.2026, bug-683 — <b>DIE KI-UHREN IM SPIELSTAND</b>
/// (berichte/pruefung-rest-a-opus.md §2, Bau: berichte/bau-spielstand-ki-klon.md).
///
/// <para><b>Befund (gemessen):</b> nach dem Laden bildete die Gefechts-KI ihre nächste Gruppe
/// sofort — der neue <c>AiPlayer</c> aus <c>EnableSkirmishAi</c> (SkirmishAi.cs) begann mit
/// <c>AttackTimer = 0</c> und <c>Think = 0,5 + 0,13·p</c>, die Wellenpause fiel weg. Ebenso
/// verloren: die Sperre von »Der Feind naht« (<c>word[0x538BAC]</c>), der Simulationstakt
/// (<c>dword[0x4FA240]</c>, alle Takt-Plätze der KI-Runde), die Stufenuhr, Picks, Sweep/Block,
/// Einnehmer und Welle, und der eigene Nebel jeder Gefechts-KI.</para>
///
/// <para><b>Was Spielzustand ist (geklärt):</b> alles, was eine KÜNFTIGE Entscheidung ändert.</para>
/// <list type="bullet">
/// <item>je <c>AiPlayer</c>: <c>Think</c>, <c>AttackTimer</c>, <c>Sweep</c> (verlustfrei),
/// <c>Block</c>, <c>Picks</c> (Baumischung), <c>Taken</c>, <c>Built</c>, <c>Waves</c>,
/// <c>Zuege</c>, <c>Grabber</c>/<c>GrabTarget</c>/<c>TargetIdx</c>/<c>Wave</c> als STELLE
/// im Spielstand (wie die Gruppenmitglieder) — im Netz zeigten die alten Listenstellen nach
/// 990/980 sonst auf andere Einheiten.</item>
/// <item><c>_feindNahtFreiAb</c> als REST ab der Uhr (die Uhr selbst schreibt Writer.Num nur
/// auf 0,001 genau).</item>
/// <item><c>_taktNr</c> — die Phase aller <c>% 50</c>-Plätze (Helikauf 10, Bahnhof 14,
/// Strom 13, …).</item>
/// <item><c>_kiSpielzeit</c> — die Stufenleiter (KiStufen.cs).</item>
/// <item><c>_kiNebel</c> — als Lauflängen wie der Spielernebel (je KI ein paar hundert
/// Zahlen).</item>
/// </list>
/// <para>NICHT gespeichert, weil je Zug neu gerechnet: <c>_gefRest</c>, <c>_aiRaster</c>,
/// <c>_aiWege</c>, <c>_aiTuerzellen</c>; die übrigen Zähler der Statuszeile; der Plan
/// (<c>AiLoadPlan</c> lädt ihn aus der Mission, der Zähler sec58 steht schon im Stand).</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN:</b> <c>AttackTimer</c>, <c>Think</c>, <c>Picks</c>, der KI-Nebel
/// und die Stufenuhr gibt es im Original nicht — sie werden gespeichert, damit Laden
/// Weiterspielen heißt. Ob das Original <c>word[0x538BAC]</c> und seinen Taktzähler speichert,
/// ist NICHT gelesen (VERMUTET ja, globaler Block). Format bleibt <b>5</b>: zusätzliche
/// Schlüssel wie bei <c>ki_sec131</c> (bug-675); ein Stand ohne sie lädt wie bisher.</para>
///
/// <para><b>Netz:</b> 990 und der Neustart 980 setzen die Werte direkt in die laufenden
/// <c>AiPlayer</c> — der Neustart ist damit auch für die KI ein Neubeginn (»Feind naht« kommt
/// wieder). ⚠ SETZUNG bleibt: der Würfel läuft weiter (Neustart.cs).</para>
///
/// <para>Gegenschalter <c>--ki-uhren-spielstand-alt</c>: weder schreiben noch lesen (der
/// Fingerabdruck zeigt die Uhren trotzdem — so meldet <c>--spielstand-check</c> den Verlust).
/// Prüfstand <c>--ki-uhren-laden-check</c> (Rendering/KiUhrenLadenCheck.cs).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--ki-uhren-spielstand-alt</c> — Stand bis bug-683: die Uhren gehen nicht in den Stand.</summary>
    public static bool KiUhrenSpielstandAlt;

    /// <summary>Gelesene Werte eines Spielers, dessen <c>AiPlayer</c> beim Laden noch nicht
    /// stand (Einzelspiel: die KI entsteht erst in <c>EnableSkirmishAi</c>, NACH dem Laden).</summary>
    private sealed class KiUhrenSatz
    {
        public float Think, AttackTimer, Sweep;
        public int Block, Picks, Taken, Built, Waves, Zuege;
        public int Grabber = -1, GrabTarget = -1, TargetIdx = -1;
        public readonly List<int> Wave = new();
    }

    private readonly Dictionary<int, KiUhrenSatz> _kiUhrenGeladen = new();

    /// <summary>Wie viele Spieler das letzte Laden mit Uhren versorgt hat (−1 = Stand ohne Uhren).</summary>
    public static int KiUhrenGeladen = -1;

    /// <summary>Aus <see cref="KiZustandSchreiben"/>.</summary>
    private void KiUhrenSchreiben(Core.SaveGame.Writer w, Func<int, int> stelle)
    {
        if (KiUhrenSpielstandAlt) return;
        w.ArrayStart("ki_spieler");
        foreach (var a in _ai)
        {
            w.ItemStart();
            w.Num("p", a.Player)
             .Raw("think", Genau(a.Think)).Raw("attack_timer", Genau(a.AttackTimer)).Raw("sweep", Genau(a.Sweep))
             .Num("block", a.Block).Num("picks", a.Picks).Num("taken", a.Taken)
             .Num("built", a.Built).Num("waves", a.Waves).Num("zuege", a.Zuege)
             .Num("grabber", stelle(a.Grabber)).Num("grab_target", stelle(a.GrabTarget))
             .Num("target", stelle(a.TargetIdx));
            w.Raw("wave", "[" + string.Join(",", a.Wave.Select(stelle).Where(s => s >= 0)) + "]");
            w.ItemEnd();
        }
        w.ArrayEnd();
        w.Raw("feind_naht_rest", Genau(Mathf.Max(0f, _feindNahtFreiAb - _clock)));
        w.Num("takt_nr", _taktNr);
        w.Raw("ki_spielzeit", Genau(_kiSpielzeit));
        w.ArrayStart("ki_nebel");
        foreach (var kv in _kiNebel.OrderBy(k => k.Key))
        {
            w.ItemStart();
            w.Num("p", kv.Key).Num("w", kv.Value.Width).Num("h", kv.Value.Height).Raw("rle", NebelRle(kv.Value));
            w.ItemEnd();
        }
        w.ArrayEnd();
    }

    /// <summary>Aus <see cref="KiZustandLesen"/>, hinter dem KI-Teil (die Uhr ist schon geladen).</summary>
    private void KiUhrenLesen(Godot.Collections.Dictionary<string, Variant> root, List<int> listenstelle)
    {
        _kiUhrenGeladen.Clear();
        KiUhrenGeladen = -1;
        if (KiUhrenSpielstandAlt || !root.TryGetValue("ki_spieler", out var sv) || sv.VariantType != Variant.Type.Array) return;
        KiUhrenGeladen = 0;
        int I(Godot.Collections.Dictionary<string, Variant> d, string k, int dflt = 0)
            => d.TryGetValue(k, out var x) ? x.AsInt32() : dflt;
        float F(Godot.Collections.Dictionary<string, Variant> d, string k)
            => d.TryGetValue(k, out var x) ? (float)x.AsDouble() : 0f;
        int Stelle(int st) => st >= 0 && st < listenstelle.Count ? listenstelle[st] : -1;
        foreach (var item in sv.AsGodotArray())
        {
            if (item.VariantType != Variant.Type.Dictionary) continue;
            var d = item.AsGodotDictionary<string, Variant>();
            int p = I(d, "p", -1);
            if (p is < 0 or > 7) continue;
            var s = new KiUhrenSatz
            {
                Think = F(d, "think"), AttackTimer = F(d, "attack_timer"), Sweep = F(d, "sweep"),
                Block = I(d, "block"), Picks = I(d, "picks"), Taken = I(d, "taken"),
                Built = I(d, "built"), Waves = I(d, "waves"), Zuege = I(d, "zuege"),
                Grabber = Stelle(I(d, "grabber", -1)), GrabTarget = Stelle(I(d, "grab_target", -1)),
                TargetIdx = Stelle(I(d, "target", -1)),
            };
            if (d.TryGetValue("wave", out var wv) && wv.VariantType == Variant.Type.Array)
                foreach (var q in wv.AsGodotArray()) { int i = Stelle(q.AsInt32()); if (i >= 0) s.Wave.Add(i); }
            _kiUhrenGeladen[p] = s;
            KiUhrenGeladen++;
        }
        // Netz (990/980): die KI steht schon — sofort uebernehmen.
        foreach (var a in _ai) KiUhrenUebernehmen(a);
        if (root.TryGetValue("feind_naht_rest", out var fv))
            _feindNahtFreiAb = _clock + (float)fv.AsDouble();
        if (root.TryGetValue("takt_nr", out var tv)) _taktNr = tv.AsInt32();
        if (root.TryGetValue("ki_spielzeit", out var kz)) _kiSpielzeit = (float)kz.AsDouble();
        if (root.TryGetValue("ki_nebel", out var nv) && nv.VariantType == Variant.Type.Array)
        {
            _kiNebel.Clear();
            foreach (var item in nv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                int p = I(d, "p", -1), w = I(d, "w"), h = I(d, "h");
                if (p is < 0 or > 7 || w <= 0 || h <= 0 || !d.TryGetValue("rle", out var rv) || rv.VariantType != Variant.Type.Array) continue;
                if (_fog != null && (w != _fog.Width || h != _fog.Height)) continue;   // andere Karte: verwerfen
                var g = new Simulation.FogGrid(w, h);
                int at = 0;
                foreach (var pair in rv.AsGodotArray())
                {
                    if (pair.VariantType != Variant.Type.Array) continue;
                    var pa = pair.AsGodotArray();
                    if (pa.Count < 2) continue;
                    int v = pa[0].AsInt32(), n = pa[1].AsInt32();
                    for (int k = 0; k < n && at < g.CellCount; k++) g.SetCellAt(at++, v);
                }
                g.MarkChanged();
                _kiNebel[p] = g;
            }
        }
    }

    /// <summary>Aus <c>EnableSkirmishAi</c> (und beim Laden im Netz): die gelesenen Werte in
    /// den <c>AiPlayer</c> — genau einmal.</summary>
    private void KiUhrenUebernehmen(AiPlayer a)
    {
        if (!_kiUhrenGeladen.Remove(a.Player, out var s)) return;
        a.Think = s.Think; a.AttackTimer = s.AttackTimer; a.Sweep = s.Sweep;
        a.Block = s.Block; a.Picks = s.Picks; a.Taken = s.Taken;
        a.Built = s.Built; a.Waves = s.Waves; a.Zuege = s.Zuege;
        a.Grabber = s.Grabber; a.GrabTarget = s.GrabTarget; a.TargetIdx = s.TargetIdx;
        a.Wave.Clear(); a.Wave.AddRange(s.Wave);
    }

    private static string NebelRle(Simulation.FogGrid g)
    {
        var sb = new StringBuilder("[");
        int run = 0, last = -1;
        bool first = true;
        void Lauf() { if (!first) sb.Append(','); first = false; sb.Append('[').Append(last).Append(',').Append(run).Append(']'); }
        for (int i = 0; i < g.CellCount; i++)
        {
            int v = g.CellAt(i);
            if (v == last) { run++; continue; }
            if (last >= 0) Lauf();
            last = v; run = 1;
        }
        if (last >= 0) Lauf();
        return sb.Append(']').ToString();
    }

    /// <summary>Für den Fingerabdruck von <c>--spielstand-check</c>: die Uhren als Text.
    /// Einheiten über ihren Platz (die Listenstelle ändert sich beim Laden).</summary>
    private string KiUhrenAbdruck()
    {
        string Platz(int i) => i >= 0 && i < _entities.Count ? _entities[i].Slot.ToString() : "-";
        var sb = new StringBuilder();
        foreach (var a in _ai.OrderBy(x => x.Player))
            sb.Append($" P{a.Player}[{a.Think:0.0000}/{a.AttackTimer:0.0000}/{a.Sweep:0.0000}/{a.Block}/{a.Picks}/{a.Taken}/" +
                      $"{Platz(a.Grabber)}/{Platz(a.GrabTarget)}/{Platz(a.TargetIdx)}/{a.Wave.Count}]");
        long nebel = 0;
        foreach (var kv in _kiNebel)
            for (int i = 0; i < kv.Value.CellCount; i++) nebel += (kv.Key + 1) * (long)(kv.Value.CellAt(i) == 0 ? 0 : 1) * (i % 997 + 1);
        return $", Uhren{sb} FeindNaht {Mathf.Max(0f, _feindNahtFreiAb - _clock):0.000} Takt {_taktNr} " +
               $"Stufenuhr {_kiSpielzeit:0.000} Nebel {_kiNebel.Count}/{nebel}";
    }

    // ---- für den Prüfstand --ki-uhren-laden-check ----------------------------------

    /// <summary>Die Spieluhr in Sekunden.</summary>
    public float SpielUhrFuerProbe => _clock;
    /// <summary>AttackTimer des KI-Spielers <paramref name="p"/> (NaN = keine KI).</summary>
    public float KiAttackTimerFuerProbe(int p)
    {
        foreach (var a in _ai) if (a.Player == p) return a.AttackTimer;
        return float.NaN;
    }
    /// <summary>Denktakt der KI-Stufe in Sekunden.</summary>
    public float KiDenktaktFuerProbe => AiTuning(_aiStufe).think;
    /// <summary>Rest der »Feind naht«-Sperre in Sekunden.</summary>
    public float FeindNahtRestFuerProbe => Mathf.Max(0f, _feindNahtFreiAb - _clock);
    public int TaktNrUhrenProbe => _taktNr;
}
