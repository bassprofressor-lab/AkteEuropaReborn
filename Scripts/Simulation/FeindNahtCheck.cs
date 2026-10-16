namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// <c>--feind-naht-check=SZENARIO</c> (KayelGee 14.10.2026, Punkte 7 und 14;
/// bug-575/576). Misst, WANN die Computerspieler über den Sektorangriff
/// losfahren und ob dabei Klang 122 »Der Feind naht« fällt.
///
/// <list type="bullet">
///   <item><c>m3-mitte</c> (mit <c>--campaign=3</c>): die Lage des Testers —
///   Mittelbasis Dolni (Gebäude 1) gehört Spieler 0, Spieler 1 ist geschlagen,
///   die eigenen Einheiten stehen bei Dolni. <b>Soll:</b> Weiß (Spieler 5)
///   schickt NIEMANDEN (Weg über die letzte Brücke &gt;= 50).</item>
///   <item><c>m3-bruecke</c>: dieselbe Lage, die eigenen Einheiten aber jenseits
///   der letzten Brücke (81,73). <b>Soll:</b> Weiß greift an, und Klang 122
///   fällt mindestens einmal.</item>
///   <item><c>m2</c> (mit <c>--campaign=2</c>): die eigenen Einheiten stehen
///   neben der nächsten KI-Einheit. <b>Soll:</b> ein Sektorangriff auf uns und
///   mindestens einmal Klang 122.</item>
/// </list>
///
/// <para><b>Nullmodelle:</b> <c>--ki-wegtor-alt</c> muss <c>m3-mitte</c> DURCHFALLEN
/// lassen (Weiß fährt los — die Meldung), <c>--feind-naht-alt</c> muss
/// <c>m2</c>/<c>m3-bruecke</c> DURCHFALLEN lassen (0 Ansagen).</para>
///
/// <para>⚠ EINGRIFFE, gesagt: die eigenen Einheiten werden HINGESETZT (die
/// Anfahrt misst der Lauf nicht); in M3 werden die Einheiten von Spieler 1
/// entfernt und Dolni per Besitzerwechsel übergeben (statt Einnahme).
/// Gemessen werden 120 s Spielzeit ab dem Setzen.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static string FeindNahtCheckSzenario = "";
    private bool _fnGesetzt, _fnFertig;
    private float _fnAb = -1;
    private string _fnNotiz = "";
    private const float FnDauer = 120f;

    /// <summary>Wer über den Sektorangriff losgeschickt wurde: [Spieler] und
    /// davon auf den Betrachter.</summary>
    public readonly int[] SektorLosJeSpieler = new int[8];
    public readonly int[] SektorLosAufMich = new int[8];
    /// <summary>Kleinster Abstand (Schachbrett) eines losgeschickten Spieler-5-
    /// Fahrzeugs zu seinem Ziel beim Losschicken — für die Lesbarkeit.</summary>
    private readonly List<string> _fnLos = new();

    private void FeindNahtCheckLos(int p, int einheit, int ziel)
    {
        if (FeindNahtCheckSzenario.Length == 0) return;
        if (_fnLos.Count >= 40) return;
        var e = _entities[einheit]; var z = _entities[ziel];
        _fnLos.Add($"t={_clock:0.0}s P{p} Platz {e.Slot} ({e.Col},{e.Row}) -> {z.Name} P{z.Owner} ({z.Col},{z.Row})");
    }

    private void FnSetzen(int idx, int c, int r)
    {
        var e = _entities[idx];
        var z = M2uFrei(e, c, r);
        M2uSetzen(idx, z.X, z.Y);
        e.Orders.Clear(); e.Target = -1;
    }

    public string FeindNahtCheckStart()
    {
        EnsureMissionScript();
        return $"Szenario {FeindNahtCheckSzenario}, Mission {UI.SkirmishSetup.CampaignMission}, Betrachter {ViewPlayer}, " +
               $"Wegtor {(KiWegtorAlt ? "AUS (--ki-wegtor-alt)" : "an")}, Klang 122 {(FeindNahtAlt ? "AUS (--feind-naht-alt)" : "an")}";
    }

    public bool FeindNahtCheckMessen()
    {
        if (_fnFertig) return true;
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        if (!_fnGesetzt && _clock >= 1f)
        {
            _fnGesetzt = true;
            var meine = new List<int>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == me && e.Mobile) meine.Add(i);
            }
            Vector2I ort;
            string sz = FeindNahtCheckSzenario;
            if (sz.StartsWith("m3"))
            {
                // Spieler 1 geschlagen, Dolni (Gebaeude 1) uns
                int weg = 0;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var x = _entities[i];
                    if (x.IsBuilding || x.IsProp || x.Dead || x.Owner != 1) continue;
                    Kill(i, x, -1, "feind-naht-check: Spieler 1 geschlagen"); weg++;
                }
                Entity? dolni = _entities.FirstOrDefault(b => b.IsBuilding && !b.Dead && b.Slot == 1 && b.BType == 1);
                if (dolni != null) Hand(dolni, me);
                ort = sz == "m3-bruecke" ? new Vector2I(86, 73) : new Vector2I(78, 36);
                _fnNotiz = $"{weg} Einheiten von Spieler 1 entfernt, Dolni {(dolni == null ? "NICHT GEFUNDEN" : $"({dolni.Col},{dolni.Row}) jetzt Spieler {dolni.Owner}")}; ";
            }
            else
            {
                // neben die naechste KI-Einheit (bewaffnet, beweglich)
                Entity? erster = meine.Count > 0 ? _entities[meine[0]] : null;
                int best = int.MaxValue; ort = default;
                if (erster != null)
                    foreach (var x in _entities)
                    {
                        if (x.IsBuilding || x.IsProp || x.Dead || !x.Mobile || x.Owner == me || x.Owner is < 0 or > 6) continue;
                        if (!CanFight(x) || !AiHostile(x.Owner, me)) continue;
                        int d = Mathf.Max(Mathf.Abs(x.Col - erster.Col), Mathf.Abs(x.Row - erster.Row));
                        if (d < best) { best = d; ort = new Vector2I(x.Col, x.Row + 8); }
                    }
                _fnNotiz = $"naechste KI-Einheit {best} Zellen vom Start; ";
            }
            foreach (int i in meine) FnSetzen(i, ort.X, ort.Y);
            _fnNotiz += $"{meine.Count} eigene Einheiten nach ({ort.X},{ort.Y}) gesetzt bei t={_clock:0.0}s";
            _fnAb = _clock;
        }
        if (_fnGesetzt && _clock >= _fnAb + FnDauer) _fnFertig = true;
        return _fnFertig;
    }

    public string FeindNahtCheckBericht()
    {
        var sb = new StringBuilder($"feind-naht-check[{FeindNahtCheckSzenario}]:\n");
        sb.AppendLine("  " + _fnNotiz);
        sb.AppendLine($"  Klang 122 gespielt: Stelle 1 (Sektorangriff 0x4BC79D) {FeindNahtGespielt[1]}, Stelle 2 (Gruppenlauf 0x4BD1C5) {FeindNahtGespielt[2]}; " +
                      $"von der Sperre geschluckt {FeindNahtGesperrt[1]}/{FeindNahtGesperrt[2]}, Buendnistor {FeindNahtFreund[1]}/{FeindNahtFreund[2]}, Mission 14 {FeindNahtM14}");
        foreach (var z in FeindNahtProtokoll) sb.AppendLine("    " + z);
        sb.AppendLine($"  Wegtor: {WegtorOffen} offen, {WegtorZuLang} zu (Weg >= {WegtorGrenze}), {WegtorKeinWeg} ohne Weg; laengster Weg {WegtorLaengste}");
        // die Wegtor-Zeilen je Spieler zusammengefasst (erste 3 je Spieler)
        foreach (var g in WegtorProtokoll.GroupBy(z => z.Substring(0, 2)))
            foreach (var z in g.Take(3)) sb.AppendLine("    " + z);
        sb.Append("  Sektorangriff losgeschickt je Spieler (davon auf uns):");
        for (int p = 0; p < 8; p++)
            if (SektorLosJeSpieler[p] > 0) sb.Append($" P{p} {SektorLosJeSpieler[p]} ({SektorLosAufMich[p]})");
        sb.AppendLine();
        foreach (var z in _fnLos.Take(12)) sb.AppendLine("    " + z);
        int lebend = _entities.Count(e => !e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == ViewPlayer && e.Mobile);
        sb.AppendLine($"  eigene Einheiten am Ende lebend: {lebend}");

        int klang = FeindNahtGespielt[1] + FeindNahtGespielt[2];
        bool ok; string soll;
        switch (FeindNahtCheckSzenario)
        {
            case "m3-mitte":
                ok = SektorLosAufMich[5] == 0;
                soll = "Weiss (P5) schickt niemanden auf uns, solange wir die letzte Bruecke nicht ueberquert haben";
                break;
            case "m3-bruecke":
                ok = SektorLosAufMich[5] > 0 && klang > 0;
                soll = "Weiss (P5) greift jenseits der letzten Bruecke an UND Klang 122 faellt";
                break;
            default:
                ok = SektorLosAufMich.Sum() > 0 && klang > 0;
                soll = "ein Sektorangriff auf uns UND Klang 122 faellt";
                break;
        }
        sb.Append($"  Soll: {soll} -> {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}");
        return sb.ToString();
    }
}
