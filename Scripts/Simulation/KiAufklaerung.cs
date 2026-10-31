namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE AUFKLAERUNG DER GEFECHTS-KI — sie greift nur an, was sie gesehen hat</b>
/// (gebaut 11.09.2026, bug-198).
///
/// <para><b>Gemeldet:</b> »Die KI dort beschiesst mich relativ zu Beginn mit
/// Langstreckenraketen, die instant meine Fabriken wegballern. Ich bezweifle
/// aber, dass die mich sehen (die haben mich noch gar nicht aufgedeckt).«</para>
///
/// <para><b>Gelesen, nicht vermutet — die Kette im eigenen Code:</b></para>
/// <code>
///   AiFight        -> AiPickTarget(p)   naechstes feindliches Ding der GANZEN
///                                       Karte, Gebaeude zaehlen doppelt; kein Nebel
///   AiSend         -> Abstand &lt;= WeaponOf(Waffe).RangeTiles ? sofort Ziel + Ordered
///   Waffe 28       -> Mittelstreckenrakete. ⚠ BERICHTIGT 12.09.2026: hier stand
///                     »range_tiles 22 (weapons.json)«, und die Zahl gibt es
///                     nicht. Die Reichweite steht in component_stats.json bei
///                     +0x14 und ist 255 — zweimal belegt, siehe KiStufen.cs.
///   Sicht          -> Einheit elev + Sicht - 1 (meist 2..5), Gebaeude 10
/// </code>
/// <para>Eine Rakete der KI, die 22 Zellen vor der Fabrik des Spielers steht,
/// feuert also im ersten Denk-Takt nach der Wellenbildung — ohne dass eine
/// einzige Einheit der KI die Fabrik je gesehen hat. Es gab genau EINEN Nebel
/// im Spiel, den des Betrachters. Die Kopfzeile von SkirmishAi.cs behauptete
/// »schummelt nicht bei … Sicht«; das stimmte nie.</para>
///
/// <para><b>Aus seinem Protokoll</b> (godot.log vom 11.09., map_NET02, KI 1/2/3):
/// seine Schw.Artillerie auf (166,122) und sein MG auf (175,133) starben an
/// »DRUCKWELLE, ohne Schuetzen« — dem Art-7-Einschlag. Die Fabriken selbst
/// stehen nicht darin, weil das Todesprotokoll Gebaeude ausliess (jetzt
/// <c>gebaeudetod:</c>), und die Druckwelle nannte ihren Schuetzen nicht (jetzt
/// »DRUCKWELLE der Rakete von …«, samt der Angabe, ob die KI die Zielzelle
/// gesehen hatte).</para>
///
/// <para><b>Was jetzt gilt — NUR IM GEFECHT</b> (die Kampagne bleibt
/// unberuehrt, <see cref="InCampaign"/>): jede Computerpartei fuehrt ihren
/// EIGENEN Nebel, gerechnet wie der des Spielers — dieselben
/// <see cref="Watchers(int)"/>, dieselben Radien, dieselben Verbuendeten, im
/// selben Nebeltakt. Daraus:</para>
/// <list type="bullet">
/// <item>ein <b>Gebaeude</b> ist bekannt, sobald eine Zelle seines Grundrisses
///   einmal gesehen war (dieselbe Regel wie <see cref="GebaeudeAufgedeckt"/>
///   fuer den Spieler);</item>
/// <item>eine <b>Einheit</b> nur, solange ihre Zelle GERADE beobachtet wird —
///   ein Panzer faehrt weg, ein Gebaeude nicht;</item>
/// <item>daran haengen <see cref="AiPickTarget"/>, die Fortsetzung einer Welle in
///   <see cref="AiFight"/>, <see cref="AiRingTarget"/>, <see cref="AiGrab"/>
///   (nur fremde Gebaeude, die herrenlosen bleiben frei) und die Selbstaufnahme
///   <see cref="AutoAcquire"/> fuer Einheiten der KI.</item>
/// </list>
///
/// <para>⚠ <b>UNSERE SETZUNGEN, benannt</b> — Wettkampfmodus, das Original gibt
/// hier nichts vor (die Welle und der Greifer sind ohnehin unser Zusatz):</para>
/// <list type="bullet">
/// <item><b>Die Spaehfahrt.</b> Kennt die KI keinen Gegner, faehrt die Welle
///   zur naechsten feindlichen BASIS (Gebaeudeart 1) — der Startplatz, den auch
///   ein Mensch aus der Kartenwahl kennt. Gibt es keine, zur naechsten nie
///   gesehenen Zelle (Raster 8). Nur FAHREN, kein Ziel.</item>
/// <item><see cref="KiSpaehPause"/> Sekunden zwischen zwei Spaehfahrten (die
///   Angriffswelle hat 20).</item>
/// <item>Ist der Nebel aus (Einstellung »Nebel aus«), sieht auch die KI alles —
///   dieselbe Regel fuer beide Seiten.</item>
/// <item>Der Nebel der KI steht NICHT im Spielstand: nach dem Laden spaeht sie
///   neu.</item>
/// <item>Die Selbstaufnahme der Einheiten des SPIELERS ist nicht angefasst.</item>
/// </list>
///
/// <para>Gegenschalter <c>--ki-sieht-alles</c> (der Stand vor dem 11.09.: Zielwahl
/// ueber die ganze Karte). Pruefstand <c>--ki-sicht-check</c>: zaehlt jeden
/// Angriffsbefehl der Gefechts-KI danach, ob sie das Ziel gesehen hatte — unter
/// dem Gegenschalter ist das das Nullmodell.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--ki-sieht-alles</c></summary>
    public static bool KiSiehtAlles;

    /// <summary>Der Nebel je Computerspieler, Schluessel = Spielernummer.</summary>
    private readonly Dictionary<int, Simulation.FogGrid> _kiNebel = new();

    /// <summary>Unsere Setzung, siehe Kopf.</summary>
    private const float KiSpaehPause = 8f;

    /// <summary><c>--spaehen-sperre-alt</c> (bug-729): eine angefahrene und ungesehen
    /// gebliebene Rasterzelle wird NICHT gesperrt — der Stand bis zum 30.10.2026.</summary>
    public static bool SpaehenSperreAlt;

    /// <summary>bug-729: die gesperrten Rasterzellen je Computerspieler. ⚠ Wie der Nebel
    /// der KI NICHT im Spielstand — nach dem Laden spaeht sie neu.</summary>
    private readonly Dictionary<int, HashSet<Vector2I>> _kiSpaehSperre = new();

    /// <summary>bug-729: die letzte Spaehfahrt zu einer Rasterzelle je Computerspieler —
    /// Ziel und je Spaeher die Zelle, die <c>NearestFree</c> ihm gab.</summary>
    private readonly Dictionary<int, (Vector2I Ziel, List<(int Idx, Vector2I Goal)> Spaeher)> _kiSpaehLetzte = new();

    // ---- Messpunkte bug-729 (laufen auch unter dem Gegenschalter) ----
    /// <summary>Fahrten zu einer Zelle, an der schon ein Spaeher angekommen war und die
    /// trotzdem ungesehen blieb — die Spaehschleife selbst.</summary>
    public int KiSpaehLeerfahrten;
    /// <summary>Gesperrte Zellen (nur ohne Gegenschalter).</summary>
    public int KiSpaehGesperrt;
    private readonly int[] _kiSpaehSerie = new int[8], _kiSpaehSerieMax = new int[8];
    private readonly Vector2I[] _kiSpaehSerieZiel = new Vector2I[8], _kiSpaehSerieMaxZiel = new Vector2I[8];
    private readonly int[] _kiSpaehFahrtenJe = new int[8];
    private readonly HashSet<(int P, Vector2I Z)> _kiSpaehAngekommen = new();
    /// <summary>Wahlen einer Zelle, die schon einmal fuer keinen Spaeher erreichbar war.</summary>
    public int KiSpaehLeerwahlen;
    private readonly HashSet<(int P, Vector2I Z)> _kiSpaehUnerreichbar = new();

    /// <summary>Gilt die Regel gerade? Gefecht, eine KI, Nebel an, kein Gegenschalter.</summary>
    private bool KiAufklaerungAn => !KiSiehtAlles && !InCampaign && _aiOn && FogActive;

    // ---- die Zaehler des Pruefstands; sie laufen auch unter dem Gegenschalter ----
    public int KiBefehleGesehen, KiBefehleUngesehen, KiRaketenUngesehen;
    public int KiWahlUngesehen, KiRingVerworfen, KiAutoVerworfen, KiGreiferVerworfen, KiSpaehfahrten;
    /// <summary>Der ZWEITE Weg, den der erste Messlauf aufdeckte: ein Ziel wird in
    /// UpdateCombat nie wegen der Entfernung fallengelassen. Gezaehlt werden die
    /// verlorenen Ziele und die Raketeneinschlaege der KI auf nie gesehene Zellen.</summary>
    public int KiZieleVerloren, KiRaketenEinschlagUngesehen, KiRaketenEinschlagGesehen;

    private bool IstKi(int spieler)
    {
        foreach (var a in _ai) if (a.Player == spieler) return true;
        return false;
    }

    /// <summary><c>--ki-grundriss-nicht-merken</c> — der Stand des ersten Tages:
    /// der Nebel der KI schreibt beim Sehen eines Gebaeudes NICHT den ganzen
    /// Grundriss ins Gedaechtnis.</summary>
    public static bool KiGrundrissNichtMerken;

    /// <summary>Im Nebeltakt gerufen, direkt nach dem Nebel des Spielers.</summary>
    private void KiNebelAktualisieren()
    {
        if (InCampaign || !_aiOn || _fog == null) return;
        foreach (var a in _ai)
        {
            if (!_kiNebel.TryGetValue(a.Player, out var g))
                _kiNebel[a.Player] = g = new Simulation.FogGrid(_fog.Width, _fog.Height);
            g.Update(Watchers(a.Player));
            // ⭐ 11.09.2026, zweiter Messlauf — DER GANZE GRUNDRISS KOMMT INS
            // GEDAECHTNIS, wie beim Spieler (0x41FE20, gerufen aus dem Stempler
            // @0x420272 und dem Saum @0x41FFEF; bei uns GebaeudeAufgedeckt).
            // Gemessen: 5 Raketeneinschlaege der KI auf »nie gesehene« Zellen,
            // alle an Gebaeuden — die KI kannte das Gebaeude ueber EINE Zelle, die
            // Rakete zielt auf seine Bildmitte. Gegenschalter --ki-grundriss-nicht-merken.
            if (KiGrundrissNichtMerken) continue;
            foreach (var b in _entities)
            {
                if (!b.IsBuilding || b.IsProp || b.Dead) continue;
                int w = Mathf.Max(1, b.FootW), h = Mathf.Max(1, b.FootH);
                bool sieht = false;
                for (int dx = 0; dx < w && !sieht; dx++)
                    for (int dy = 0; dy < h && !sieht; dy++)
                        sieht = g.IsWatched(b.Col + dx, b.Row + dy);
                if (!sieht) continue;
                for (int dx = 0; dx < w; dx++)
                    for (int dy = 0; dy < h; dy++)
                        g.Merken(b.Col + dx, b.Row + dy);
            }
        }
    }

    /// <summary>Was ein Raketeneinschlag der KI auf eine nie gesehene Zelle
    /// eigentlich treffen wollte — damit der Pruefstand die Ursache NENNT statt
    /// sie mir zu ueberlassen. Hoechstens 12 Zeilen.</summary>
    private readonly List<string> _kiEinschlagNotizen = new();

    private void KiEinschlagNotieren(Entity s, int c, int r, int ziel)
    {
        if (_kiEinschlagNotizen.Count >= 12) return;
        string z = "ohne Zielgriff (Bodenziel?)";
        if (ziel >= 0 && ziel < _entities.Count)
        {
            var t = _entities[ziel];
            int w = Mathf.Max(1, t.FootW), h = Mathf.Max(1, t.FootH);
            bool imGrundriss = c >= t.Col && r >= t.Row && c < t.Col + w && r < t.Row + h;
            z = $"Ziel Platz {t.Slot} Spieler {t.Owner} {(t.IsBuilding ? $"GEBAEUDE Art {t.BType}" : "Einheit")} "
              + $"Anker ({t.Col},{t.Row}) Grundriss {w}x{h}, KI kennt es: {KiKenntRoh(s.Owner, t)}, "
              + $"Einschlagzelle im Grundriss: {imGrundriss}";
        }
        _kiEinschlagNotizen.Add($"P{s.Owner} Platz {s.Slot} auf ({s.Col},{s.Row}) -> Zelle ({c},{r}): {z}");
    }

    /// <summary>Kennt Spieler <paramref name="spieler"/> dieses Ding? OHNE den
    /// Gegenschalter zu fragen — so zaehlen die Messpunkte auch im Nullmodell.</summary>
    private bool KiKenntRoh(int spieler, Entity t)
    {
        if (_fog == null || !FogActive) return true;
        if (!_kiNebel.TryGetValue(spieler, out var g)) return false;   // vor der ersten Runde: nichts
        if (!t.IsBuilding) return g.IsWatched(t.Col, t.Row);
        int w = Mathf.Max(1, t.FootW), h = Mathf.Max(1, t.FootH);
        for (int dx = 0; dx < w; dx++)
            for (int dy = 0; dy < h; dy++)
                if (g.IsSeen(t.Col + dx, t.Row + dy)) return true;
        return false;
    }

    /// <summary>Die Regel selbst: ausserhalb des Gefechts und unter dem
    /// Gegenschalter kennt jeder alles.</summary>
    private bool KiKennt(int spieler, Entity t) => !KiAufklaerungAn || KiKenntRoh(spieler, t);

    /// <summary>Fuer das Todesprotokoll: hatte die KI diese Zelle je gesehen?
    /// null, wenn der Spieler keine Gefechts-KI ist oder kein Nebel laeuft.</summary>
    private bool? KiZelleGesehen(int spieler, int c, int r)
    {
        if (InCampaign || _fog == null || !FogActive || !IstKi(spieler)) return null;
        if (!_kiNebel.TryGetValue(spieler, out var g)) return false;
        return g.IsSeen(c, r);
    }

    /// <summary>Die Spaehfahrt — siehe Kopf. true, wenn eine Welle losfuhr.</summary>
    private bool KiSpaehen(AiPlayer a, List<int> free, int guard, Vector2 von)
    {
        if (!KiAufklaerungAn || _nav == null) return false;

        Vector2I? ziel = null;
        float best = float.MaxValue;
        string wohin = "feindlichen Basis";
        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.IsProp || e.Dead || e.BType != 1) continue;
            if (e.Owner is < 0 or > 7 || e.Owner == a.Player || !AiHostile(a.Player, e.Owner)) continue;
            float d = von.DistanceTo(new Vector2(e.Col, e.Row));
            if (d < best) { best = d; ziel = new Vector2I(e.Col, e.Row); }
        }
        bool rasterZiel = false;
        if (ziel == null && _kiNebel.TryGetValue(a.Player, out var g))
        {
            wohin = "naechsten nie gesehenen Zelle";
            rasterZiel = true;
            // bug-729: erst die letzte Fahrt pruefen — angekommen und doch ungesehen?
            KiSpaehAnkunftPruefen(a.Player, g);
            _kiSpaehSperre.TryGetValue(a.Player, out var sperre);
            for (int r = 4; r < g.Height; r += 8)
                for (int c = 4; c < g.Width; c += 8)
                {
                    if (g.IsSeen(c, r)) continue;
                    if (!SpaehenSperreAlt && sperre != null && sperre.Contains(new Vector2I(c, r))) continue;   // bug-729
                    float d = von.DistanceTo(new Vector2(c, r));
                    if (d < best) { best = d; ziel = new Vector2I(c, r); }
                }
        }
        if (ziel == null) { GefechtGrund(a.Player, "Spaehen: kein Spaehziel"); return false; }   // bug-730 Messpunkt

        var z = ziel.Value;
        free.Sort((x, y) => new Vector2(_entities[x].Col - z.X, _entities[x].Row - z.Y).Length()
                     .CompareTo(new Vector2(_entities[y].Col - z.X, _entities[y].Row - z.Y).Length()));
        // ⭐ 12.09.2026 — wie viele spaehen fahren, haengt an der Stufe
        // (Simulation/KiStufen.cs): leicht ein Kundschafter, normal zwei,
        // schwer alles, was nicht Wache ist (der bisherige Stand).
        int trupp = KiStufenAn ? ProfilOf(a.Level).SpaehTrupp : int.MaxValue;
        int los = 0;
        var spaeher = new List<(int Idx, Vector2I Goal)>();
        // ⚠ bug-730, VERWORFEN 31.10.2026: hier stand kurz eine Suche ueber die ganze Liste
        // (statt nur der ersten free.Count − guard). Gemessen in vier Welten mit und ohne:
        // in keinem Lauf ein Unterschied — die Ursache von bug-730 war eine andere
        // (GefechtNachsetzenVorher). Darum wieder der alte Stand.
        for (int k = 0; k < free.Count - guard && los < trupp; k++)
        {
            int i = free[k];
            var u = _entities[i];
            // bug-730 Messpunkt: warum ein Kandidat NICHT faehrt (reine Abfragen, ohne Wirkung)
            string grund = u.Dead || !u.Mobile ? "tot/unbeweglich" : u.DugIn ? "eingegraben"
                         : u.FuelMax > 0 && u.Fuel <= 0 ? "Sprit leer" : "kein Weg";
            if (AiWalkTo(i, z)) { a.Wave.Add(i); los++; spaeher.Add((i, _entities[i].Goal)); }
            else GefechtGrund(a.Player, $"Spaehen-Kandidat {grund}");
        }
        if (los == 0)
        {
            // bug-730 Messpunkt: standen mehr fahrbereite Einheiten frei als die Wache?
            int bereit = 0;
            foreach (int i in free)
            {
                var u = _entities[i];
                if (!u.Dead && u.Mobile && !u.DugIn && !(u.FuelMax > 0 && u.Fuel <= 0)) bereit++;
            }
            if (bereit > guard) KiSpaehAusfallMitFahrbereiten++;
            GefechtGrund(a.Player, $"Spaehen: niemand faehrt ({bereit} fahrbereit)");
            // ⭐ bug-729, zweiter Fall (gemessen 31.10. mit Startbasis, bug-733): hat KEIN
            // fahrbereiter Kandidat einen Weg zur Zelle (NearestFree/FindPath leer), kommt nie
            // ein Spaeher an — dieselbe Zelle wurde 528-mal vergeblich gewaehlt. Dann gilt sie
            // sofort als unerreichbar und wird gesperrt. UNSERE SETZUNG, --spaehen-sperre-alt.
            if (rasterZiel && bereit > 0)
            {
                if (!_kiSpaehUnerreichbar.Add((a.Player, z))) KiSpaehLeerwahlen++;   // Messpunkt, auch unter dem Gegenschalter
                KiSpaehSperren(a.Player, z, "fuer keinen Spaeher erreichbar");
            }
            return false;
        }
        if (rasterZiel) KiSpaehFahrtBuchen(a.Player, z, spaeher);                             // bug-729

        a.TargetIdx = -1;
        // dieselbe Weiche fuer die Pause: leicht 20 s, normal 8 s (bisher),
        // schwer 4 s.
        a.AttackTimer = KiStufenAn ? ProfilOf(a.Level).SpaehPause : KiSpaehPause;
        a.Waves++;
        KiSpaehfahrten++;
        KiBuch(a).Spaehfahrten++;                  // --ki-stufen-check
        GD.Print($"KI P{a.Player}: kennt keinen Gegner — Spaehfahrt mit {los} Einheiten "
               + $"zur {wohin} bei ({z.X},{z.Y})");
        return true;
    }

    /// <summary>
    /// ⭐ bug-729 (30.10.2026) — <b>DIE SPAEHSCHLEIFE</b>. Gemessen auf NET07
    /// (<c>berichte/gefecht-ki-stillstand-opus.md</c> 3b): <see cref="KiSpaehen"/> waehlt die
    /// naechste nie gesehene Rasterzelle, etwa (140,108). Ist sie unerreichbar, gibt
    /// <c>NearestFree</c> den Spaehern eine Ersatzzelle (134,102), von der aus sie das Ziel
    /// nicht sehen; alle 8 s kam derselbe Befehl (0.6.12: 152 Fahrten nach (156,100), heute
    /// 81 nach (140,108)), und P1 erreichte den Westen nie.
    ///
    /// <para>⚠ <b>UNSERE SETZUNG</b> — das Original kennt keine Spaehfahrt (Kopf dieser
    /// Datei, ai_units 0x4BF4E0 befiehlt nur innerhalb des eigenen Sichtrings). Regel: steht
    /// bei der naechsten Wahl ein Spaeher der letzten Fahrt an seiner Ersatzzelle (Abstand
    /// ≤ 1, kein Weg mehr) und ist die Zielzelle trotzdem ungesehen, wird sie fuer die Wahl
    /// GESPERRT. Gegenschalter <c>--spaehen-sperre-alt</c>.</para>
    /// </summary>
    private void KiSpaehAnkunftPruefen(int p, Simulation.FogGrid g)
    {
        if (!_kiSpaehLetzte.TryGetValue(p, out var l)) return;
        if (g.IsSeen(l.Ziel.X, l.Ziel.Y)) { _kiSpaehLetzte.Remove(p); return; }
        bool angekommen = false;
        foreach (var (i, goal) in l.Spaeher)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var u = _entities[i];
            if (u.Dead || u.Owner != p || u.Path != null) continue;
            if (Mathf.Max(Mathf.Abs(u.Col - goal.X), Mathf.Abs(u.Row - goal.Y)) > 1) continue;
            angekommen = true;
            break;
        }
        if (!angekommen) return;
        _kiSpaehAngekommen.Add((p, l.Ziel));          // Messpunkt, auch unter dem Gegenschalter
        if (SpaehenSperreAlt) return;
        _kiSpaehLetzte.Remove(p);
        KiSpaehSperren(p, l.Ziel, "angefahren und ungesehen");
    }

    /// <summary>bug-729: eine Rasterzelle fuer die Wahl sperren (nie unter dem Gegenschalter).</summary>
    private void KiSpaehSperren(int p, Vector2I z, string warum)
    {
        if (SpaehenSperreAlt) return;
        if (!_kiSpaehSperre.TryGetValue(p, out var s)) _kiSpaehSperre[p] = s = new HashSet<Vector2I>();
        if (!s.Add(z)) return;
        KiSpaehGesperrt++;
        GD.Print($"KI P{p}: Spaehziel ({z.X},{z.Y}) {warum} — gesperrt (bug-729)");
    }

    /// <summary>bug-729: die Fahrt merken und die Messpunkte zaehlen.</summary>
    private void KiSpaehFahrtBuchen(int p, Vector2I z, List<(int Idx, Vector2I Goal)> spaeher)
    {
        _kiSpaehLetzte[p] = (z, spaeher);
        if (p is < 0 or > 7) return;
        _kiSpaehFahrtenJe[p]++;
        if (_kiSpaehAngekommen.Contains((p, z))) KiSpaehLeerfahrten++;
        if (_kiSpaehSerie[p] > 0 && _kiSpaehSerieZiel[p] == z) _kiSpaehSerie[p]++;
        else { _kiSpaehSerie[p] = 1; _kiSpaehSerieZiel[p] = z; }
        if (_kiSpaehSerie[p] > _kiSpaehSerieMax[p]) { _kiSpaehSerieMax[p] = _kiSpaehSerie[p]; _kiSpaehSerieMaxZiel[p] = z; }
    }

    /// <summary>bug-729/730: eine Zeile fuer Pruefstand und DET-Ende.</summary>
    public string KiSpaehZeile()
    {
        var sb = new System.Text.StringBuilder(
            $"Spaehen: Rasterfahrten {string.Join("/", System.Linq.Enumerable.Select(_ai, a => $"P{a.Player} {(a.Player is >= 0 and <= 7 ? _kiSpaehFahrtenJe[a.Player] : 0)}"))}, " +
            $"Leerfahrten (Ziel schon angefahren, ungesehen) {KiSpaehLeerfahrten}, Leerwahlen (Ziel schon unerreichbar) {KiSpaehLeerwahlen}, gesperrt {KiSpaehGesperrt}, " +
            $"--spaehen-sperre-alt {SpaehenSperreAlt}; laengste Serie");
        foreach (var a in _ai)
            if (a.Player is >= 0 and <= 7 && _kiSpaehSerieMax[a.Player] > 0)
                sb.Append($" P{a.Player} {_kiSpaehSerieMax[a.Player]}x ({_kiSpaehSerieMaxZiel[a.Player].X},{_kiSpaehSerieMaxZiel[a.Player].Y})");
        return sb.ToString();
    }

    /// <summary><c>--ki-sicht-check</c> — die Zeile am Laufende.</summary>
    public string KiAufklaerungLine()
    {
        var sb = new System.Text.StringBuilder("ki-sicht-check\n");
        sb.Append($"  Gegenschalter --ki-sieht-alles: {KiSiehtAlles}, Nebel aktiv: {FogActive}, "
                + $"Kampagne: {InCampaign}, KI: {_ai.Count}\n");
        foreach (var a in _ai)
        {
            if (!_kiNebel.TryGetValue(a.Player, out var g)) { sb.Append($"  P{a.Player}: kein Nebel gerechnet\n"); continue; }
            int beob = 0, ges = 0;
            for (int i = 0; i < g.CellCount; i++)
            {
                int v = g.CellAt(i);
                if (v != Simulation.FogGrid.Unseen) ges++;
                if (v is Simulation.FogGrid.Watched or Simulation.FogGrid.Saum) beob++;
            }
            sb.Append($"  P{a.Player}: beobachtet {beob} Zellen, je gesehen {ges} von {g.CellCount}\n");
        }
        int befehle = KiBefehleGesehen + KiBefehleUngesehen;
        sb.Append($"  Angriffsbefehle der KI: {befehle} — auf GESEHENE Ziele {KiBefehleGesehen}, "
                + $"auf UNGESEHENE {KiBefehleUngesehen} (davon Mittelstreckenrakete {KiRaketenUngesehen})\n");
        sb.Append($"  Zielwahl: {KiWahlUngesehen} mal haette die Wahl ueber die ganze Karte ein ungesehenes Ziel genommen\n");
        sb.Append($"  verworfen: Ring {KiRingVerworfen}, Selbstaufnahme {KiAutoVerworfen}, Greifer {KiGreiferVerworfen}; "
                + $"Spaehfahrten {KiSpaehfahrten}; Ziele aus der Sicht verloren {KiZieleVerloren}\n");
        sb.Append($"  Raketeneinschlaege der KI (Druckwelle): auf gesehene Zellen {KiRaketenEinschlagGesehen}, "
                + $"auf NIE gesehene {KiRaketenEinschlagUngesehen}; Gegenschalter --ki-grundriss-nicht-merken: {KiGrundrissNichtMerken}\n");
        foreach (var n in _kiEinschlagNotizen) sb.Append($"    ungesehen: {n}\n");
        int ungesehen = KiBefehleUngesehen + KiRaketenEinschlagUngesehen;
        if (InCampaign || !FogActive || _ai.Count == 0 || befehle == 0)
            sb.Append("  KEIN URTEIL — kein Gefecht mit Nebel, oder die KI hat keinen einzigen Angriff befohlen");
        else if (KiSiehtAlles)
            sb.Append(ungesehen > 0
                ? $"  NULLMODELL: {KiBefehleUngesehen} Befehle und {KiRaketenEinschlagUngesehen} Einschlaege auf Ungesehenes — der Fehler ist herstellbar"
                : "  NULLMODELL OHNE BEFUND: nichts Ungesehenes — der Lauf zeigt den Fehler nicht, das Urteil sagt NICHTS");
        else
            sb.Append(ungesehen == 0
                ? (KiRaketenEinschlagGesehen == 0 ? "  BESTANDEN (⚠ ohne einen einzigen Raketeneinschlag der KI — der zweite Weg ist so NICHT gemessen)" : "  BESTANDEN")
                : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
