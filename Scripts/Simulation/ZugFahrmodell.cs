namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ 02.10.2026 — bug-397, F7: DAS FAHRMODELL JE GLEISSCHRITT (Waggontakt 0x4C69C0).
/// Entscheidung des Spielers: »Ja, wie im Original«. Bauvorlage berichte/zug-feinlage-fable.md
/// §2 und §5 F7; die Stellen unten sind für diesen Bau SELBST nachgelesen (EXE C, und die
/// 424 lebenden Waggonsätze der 13 .DM-Spielstände als Gegenprobe).
///
/// <para><b>Was vorher war:</b> der Fortschritt lief gleichmäßig über die BOGENLÄNGE unserer
/// Gleiskette (sec22), die Fahrzeit aus den Schrittpreisen. Wo die Kette kürzer ist als die
/// Route, fuhr der Zug bei gleicher Zeit langsamer — DM_4 Linie 6 senkrecht 2,71 statt 4 px
/// je Takt, kleinster Waggonabstand 9,5 px (bug-395, Kriterium F).</para>
///
/// <para><b>Was das Original tut</b> (O, Satz 24 B ab 0xB95F48, Halbzeile als Wort 0xB95D60):</para>
/// <code>
///   Takt   Zähler(+0x08) &gt; Abzug(+0x0C = 8)  →  Zähler −= 8 ;  fein = Basis(Parität)
///          + (Preis − Zähler)·Δ(Stück)/Preis        @0x4C6A62…0x4C6BE4, Δ-Tafel 0x539400
///          Preis 28 bei ungeradem Stück (@0x4C6B3B), sonst 40 (@0x4C6B9C)
///   Wechsel Zähler ≤ 8 →  vorwärts: Lage += Code[cursor], cursor++ (@0x4C6C21, @0x4C6DAF)
///          rückwärts: Lage −= Code[cursor], cursor−− (@0x4C71E3, @0x4C725A)
///          Stück := 0x5393F0[Code[neuer cursor]] (rückwärts +4, @0x4C72A0)
///          Zähler := 28 bei ungeradem Stück, sonst 40 (@0x4C6E53 / @0x4C6E5E)
///   Rampe  NUR auf geradem Stück: Hangart h der Bezugszelle (0x401FF5 → Byte +3), Tafel
///          0x4C73C8 (rückwärts 0x4C73DC, gleich gebaut): feinY += f·cx + di mit
///          (cx, di) = (+15, −15) »fällt« für (h,Stück) = (1,2) (2,≠0) (3,≠2) (4,0),
///          sonst (−15, 0) »steigt«; beim Wechsel einmal −15 (@0x4C6E6B…0x4C6ED9)
///   Höhe   Treppe der Bezugszelle 0x4C76C0 (Sprungtafel 0x4C7740 über Stück−1)
///   Anlage vorwärts cursor 0 auf Punkt 0, rückwärts cursor = delka (@0x4C678D), Zähler 20
///          (Tafeln 0x4C687C/0x4C688C: 20/40/25/40)
///   Ende   vorwärts neuer cursor == delka (@0x4C6C30), rückwärts cursor == 0 (@0x4C7089)
///   Bild   nur bei 1 ≤ cursor ≤ delka−2 (@0x42E11B, @0x42E141…0x42E14F)
/// </code>
///
/// <para><b>Gegenprobe an den Spielständen</b> (424 lebende Sätze, kein Gegenbeispiel):
/// vorwärts steht der Satz mit cursor c auf Routenpunkt P(c), rückwärts auf P(c+1); und aus
/// (cursor, Zähler) nach diesem Modell zurückgerechnet liegen die Waggons 4/3/4 Takte
/// auseinander — in BEIDEN Richtungen (vorwärts 46/51/52, rückwärts 53/52/56 Paare).</para>
///
/// <para><b>Daraus, in Fahrtreihenfolge j = 0…delka</b> (vorwärts Punkt j, rückwärts Punkt
/// delka−j), mit Kosten 5 Takte je geradem und 4 je ungeradem Schritt (Preis/8 aufgerundet):
/// W0 schaltet auf j = 1 im Takt 3 (vorwärts: Zähler 20 → 12 → 4 → Wechsel) bzw. 3 + Kosten
/// des ersten Schritts (rückwärts: der Satz beginnt eine Stelle hinter dem Linienende),
/// danach je Schritt seine Kosten. Gezeichnet für j = 1…delka−2 — beide Richtungen fahren
/// also sichtbar von P(1) bis kurz vor P(delka−1) bzw. umgekehrt. Ankunft (Entladen durch W0,
/// @0x4C6C62) = Takt T[delka]. Die Waggons 1…3 laufen 4/7/11 Takte hinterher.</para>
///
/// <para>Die Lage springt wie im Original nur im TAKT (1/TickScale s), mit der ganzzahligen
/// Teilung des idiv — kein Mitteln zwischen den Takten. Das letzte Bild eines Schritts liegt
/// damit bei f = 0,8 bzw. 24/28, und daran hängt, wie tief der Spitzenwaggon im letzten
/// Bild im Gebäude steckt. ⚠ Die Höhe am senkrechten Aufstieg (Stück 4, gerade Halbzeile:
/// Bezugszelle Zeile−2) liegt im Original eine Zelle »hinter« der Rampe — so gelesen und an
/// den Hangarten der Spielstände bestätigt (53/53 Sätze mit Zeile−2, 11 Fehler mit Zeile−1);
/// nachgebaut wie gelesen. Gegenschalter <c>--zug-fahrmodell-alt</c> (= Stand bug-395:
/// Bogenlänge der Kette, Höhe aus dem Gleisbild).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Je Linie das Fahrmodell: Routenpunkte, Stücke und Takte.</summary>
    private sealed class ZugSchrittPlan
    {
        public List<Vector2> Quelle = null!;
        public int Delka;
        /// <summary>Je Routenpunkt die Zellkoordinate unserer Karte (<c>ZugRoutenpunktZelle</c>).</summary>
        public Vector2[] Zelle = null!;
        /// <summary>Je Routenpunkt Spalte und Halbzeile des Originals.</summary>
        public int[] Spalte = null!, Halbzeile = null!;
        /// <summary>Stück des Schritts i → i+1 (vorwärts), = pieces[i+1].</summary>
        public int[] Stueck = null!;
        /// <summary>Je Richtung (0 vor, 1 rück): Takt, in dem W0 auf die Fahrstelle j schaltet.</summary>
        public float[][] T = null!;
        /// <summary>Je Routenpunkt die Stelle auf unserer Gleiskette (Projektion), für
        /// Gleisbruch, Zähler und Prüfstände, die <c>w.LeadF</c> lesen.</summary>
        public float[]? KettenStelle;
        public List<Vector2>? KetteQuelle;
    }

    private readonly Dictionary<int, ZugSchrittPlan?> _zugPlan = new();

    /// <summary>Gilt das neue Fahrmodell? Nicht unter den Nullmodellen, die auf der
    /// Bogenlänge aufsetzen (Einfahrt alt, Stehen am Bahnsteig, Kupplung).</summary>
    private static bool ZugFahrmodellNeu
        => !ZugFahrmodellAlt && !ZugEinfahrtAlt && !ZugStehtAmBahnsteig && !ZugGekuppelt;

    /// <summary>Tafel 0x539400: Stück → (dx, dy) in Bildpunkten, in der EBENE.</summary>
    private static readonly (int, int)[] ZugDelta =
        { (0, 20), (-20, 10), (-40, 0), (-20, -10), (0, -20), (20, -10), (40, 0), (20, 10) };

    /// <summary>Ganzzahlige Teilung gegen null wie <c>idiv</c>.</summary>
    private static int Tdiv(int a, int b) => a / b;

    private static int ZugSchrittTakte(int stueck) => (stueck & 1) != 0 ? 4 : 5;
    private static int ZugSchrittPreis(int stueck) => (stueck & 1) != 0 ? 28 : 40;

    private ZugSchrittPlan? ZugPlanOf(int line)
    {
        if (_zugPlan.TryGetValue(line, out var got)) return got;
        ZugSchrittPlan? plan = null;
        if (_lineRoute.TryGetValue(line, out var rt) && _linePiece.TryGetValue(line, out var pcs)
            && rt.Count >= 4 && pcs.Count == rt.Count)
        {
            int n = rt.Count, delka = n - 1;
            plan = new ZugSchrittPlan
            {
                Quelle = rt, Delka = delka, Zelle = new Vector2[n], Spalte = new int[n],
                Halbzeile = new int[n], Stueck = new int[n], T = new[] { new float[n], new float[n] },
            };
            for (int i = 0; i < n; i++)
            {
                plan.Zelle[i] = ZugRoutenpunktZelle(rt[i]);
                plan.Spalte[i] = Mathf.RoundToInt(rt[i].X);
                plan.Halbzeile[i] = Mathf.RoundToInt(rt[i].Y * 2f);
                plan.Stueck[i] = i + 1 < n ? pcs[i + 1] & 7 : pcs[i] & 7;
            }
            for (int dir = 0; dir < 2; dir++)
            {
                var t = plan.T[dir];
                // j = 1 im Takt 3 (Startzähler 20); rückwärts beginnt der Satz eine Stelle
                // hinter dem Linienende (cursor = delka, @0x4C678D) und braucht den ersten
                // Schritt dazu.
                t[1] = 3f + (dir == 1 ? ZugSchrittTakte(StueckJ(plan, 1, 0)) : 0f);
                t[0] = dir == 1 ? 3f : t[1] - ZugSchrittTakte(StueckJ(plan, 0, 0));
                for (int j = 1; j < delka; j++) t[j + 1] = t[j] + ZugSchrittTakte(StueckJ(plan, dir, j));
            }
        }
        _zugPlan[line] = plan;
        return plan;
    }

    /// <summary>Routenpunkt der Fahrstelle j.</summary>
    private static int PunktJ(ZugSchrittPlan p, int dir, int j) => dir == 0 ? j : p.Delka - j;

    /// <summary>Stück des Schritts j → j+1 in Fahrtrichtung (rückwärts gedreht, @0x4C72A0).</summary>
    private static int StueckJ(ZugSchrittPlan p, int dir, int j)
        => dir == 0 ? p.Stueck[Mathf.Clamp(j, 0, p.Delka - 1)]
                    : (p.Stueck[Mathf.Clamp(p.Delka - j - 1, 0, p.Delka - 1)] + 4) & 7;

    /// <summary>Fahrzeit von der Abfahrt bis zum Entladen (W0 löscht seinen Satz), in Takten.</summary>
    private float ZugPlanAnkunft(ZugSchrittPlan p, int dir) => p.T[dir][p.Delka];

    /// <summary>Die Fahrzeit der Linie im neuen Modell, Sekunden; −1 ohne Plan.</summary>
    private float ZugFahrmodellSekunden(RailLine l)
    {
        if (!ZugFahrmodellNeu) return -1f;
        var p = ZugPlanOf(l.Slot);
        return p == null ? -1f : ZugPlanAnkunft(p, l.Dir == 1 ? 1 : 0) / TickScale;
    }

    /// <summary>Bezugszelle der Höhe, 0x4C76C0: nach Stück und Halbzeilen-Parität
    /// (Sprungtafel 0x4C7740). Stück 1/3: Spalte−1 bei ungerader Halbzeile; 2: immer
    /// Spalte−1; 4: Zeile−1, bei gerader Halbzeile Zeile−2; 5: Zeile−1 bei gerader; sonst
    /// die eigene Zelle.</summary>
    private static (int Spalte, int Zeile) ZugBezugszelle(int spalte, int halbzeile, int stueck)
    {
        int zeile = halbzeile >> 1;
        bool ungerade = (halbzeile & 1) != 0;
        switch (stueck)
        {
            case 1: case 3: if (ungerade) spalte--; break;
            case 2: spalte--; break;
            case 4: zeile--; if (!ungerade) zeile--; break;
            case 5: if (!ungerade) zeile--; break;
        }
        return (spalte, zeile);
    }

    /// <summary>Die Anhebung im Takt in Bildpunkten (positiv = höher): Höhe der Bezugszelle
    /// ·15 plus die Rampe (nur gerades Stück, Hangart 1…4, @0x4C6AB5…0x4C6B17).</summary>
    private float ZugHub(int spalte, int halbzeile, int stueck, float f, out bool rampe)
    {
        var (bs, bz) = ZugBezugszelle(spalte, halbzeile, stueck);
        float hub = ElevOf(bs, bz) * 15f;
        int h = HangArt(bs, bz);
        rampe = false;
        // Hangart > 4 ruft im Original die Meldung @0x4C6ACD (Zusicherung) und rechnet ohne
        // Rampe weiter (cx aus dem Stapel) — V: kommt auf Gleis nicht vor.
        if ((stueck & 1) != 0 || h is < 1 or > 4) return hub;
        bool faellt = h switch
        {
            1 => stueck == 2,
            2 => stueck != 0,
            3 => stueck != 2,
            _ => stueck == 0,
        };
        rampe = true;
        return hub + (faellt ? 15f * (1f - f) : 15f * f);
    }

    /// <summary>Projiziert die Routenpunkte auf unsere Gleiskette (einmal je Kette).</summary>
    private void ZugPlanKette(ZugSchrittPlan p, List<Vector2> kette)
    {
        if (p.KettenStelle != null && ReferenceEquals(p.KetteQuelle, kette)) return;
        p.KetteQuelle = kette;
        p.KettenStelle = new float[p.Zelle.Length];
        for (int i = 0; i < p.Zelle.Length; i++)
        {
            float best = float.MaxValue, at = 0f;
            for (int s = 0; s + 1 < kette.Count; s++)
            {
                var a = kette[s]; var d = kette[s + 1] - a;
                float len2 = d.LengthSquared();
                float t = len2 < 1e-6f ? 0f : Mathf.Clamp((p.Zelle[i] - a).Dot(d) / len2, 0f, 1f);
                float dist = (a + d * t - p.Zelle[i]).LengthSquared();
                if (dist < best) { best = dist; at = s + t; }
            }
            if (kette.Count == 1) at = 0f;
            p.KettenStelle[i] = at;
        }
    }

    /// <summary>Zähler für den Prüfstand: wie oft stand ein Waggon auf einer Rampe, und
    /// wie weit lag unsere alte Höhe (Lift aus dem Gleisbild an der Kettenstelle) daneben.</summary>
    public int ZugRampenBilder;
    public float ZugRampenAbweichungMax;
    public string ZugRampenWo = "";

    /// <summary>F7: setzt alle Waggons der Linie nach dem Fahrmodell je Gleisschritt.
    /// <paramref name="kette"/>/<paramref name="lift"/> sind die gezeichnete Kette (nur für
    /// <c>w.LeadF</c>/<c>w.Step</c> und die Vergleichshöhe).</summary>
    private void ZugSchrittSetzen(RailLine l, List<Wagon> list, ZugSchrittPlan p,
                                  List<Vector2> kette, float[]? lift)
    {
        ZugPlanKette(p, kette);
        int dir = l.Dir == 1 ? 1 : 0;
        var T = p.T[dir];
        int last = kette.Count - 1;
        // Takt des Spitzenwaggons seit der Abfahrt. ⚠ Der Nachlauf zuerst: er läuft schon,
        // während Rollt noch gesetzt ist (Travel ≤ 0, die Ankunft meldet erst der Automat
        // in seiner Runde) — sonst stünden die Nachläufer bis dahin still (B: 4/9/11).
        float tau0;
        if (l.Nachlauf >= 0f) tau0 = (l.TravelFull + l.Nachlauf) * TickScale;
        else if (l.Rollt && l.TravelFull > 0f) tau0 = (l.TravelFull - Mathf.Max(l.Travel, 0f)) * TickScale;
        else tau0 = float.NegativeInfinity;                      // steht: kein Satz
        // Ganze Takte: das Original setzt die Lage nur im Takt (Zähler −8), und das LETZTE
        // Bild eines Schritts liegt bei f = 0,8 (gerade) bzw. 24/28 (schräg), nie auf dem
        // nächsten Punkt — daran hängt die Deckung am Linienende (Kriterium E).
        if (!float.IsNegativeInfinity(tau0)) tau0 = Mathf.Floor(tau0 + 1e-3f);
        float ende = T[p.Delka];
        foreach (var w in list)
        {
            int k = Mathf.Clamp(w.Index, 0, RailWagonLagTicks.Length - 1);
            float tau = tau0 - RailWagonLagTicks[k];
            _zugPw[w] = ende > 0f ? tau / ende : 0f;
            // gezeichnet nur bei 1 <= cursor <= delka−2 (@0x42E11B / @0x42E14F)
            w.Hidden = !(tau >= T[1] && tau < T[p.Delka - 1]);
            w.Dir = dir == 0 ? 1 : -1;
            if (float.IsNegativeInfinity(tau)) continue;
            float tc = Mathf.Clamp(tau, T[1], T[p.Delka - 1] - 1f);
            int j = 1;
            while (j + 1 < p.Delka - 1 && T[j + 1] <= tc) j++;
            int s = StueckJ(p, dir, j);
            // k Takte nach dem Wechsel: Zähler = Preis − 8k, fein = Basis + (Preis − Zähler)·Δ/Preis
            // mit GANZZAHLIGER Teilung wie idiv @0x4C6B4B/@0x4C6BAF (schräg 5/11/17 px statt 5,7/11,4/17,1)
            int kt = Mathf.Clamp((int)(tc - T[j]), 0, ZugSchrittTakte(s) - 1);
            int preis = ZugSchrittPreis(s);
            float f = 8f * kt / preis;
            int pa = PunktJ(p, dir, j), pb = PunktJ(p, dir, j + 1);
            var (dx, dy) = ZugDelta[s];
            var at = p.Zelle[pa] + new Vector2(Tdiv(8 * kt * dx, preis) / (float)TileW,
                                               Tdiv(8 * kt * dy, preis) / (float)TileH);
            w.Col = at.X; w.Row = at.Y;
            w.Piece = s;
            // Höhe wie das Original (Bezugszelle + Rampe); RailPoint zieht die Höhe der
            // GERUNDETEN Zelle ab, die gleicht Lift aus.
            float hub = ZugHub(p.Spalte[pa], p.Halbzeile[pa], s, f, out bool rampe);
            w.Lift = hub - ElevOf(Mathf.RoundToInt(at.X), Mathf.RoundToInt(at.Y)) * 15f;
            float leadF = Mathf.Clamp(Mathf.Lerp(p.KettenStelle![pa], p.KettenStelle[pb], f), 0f, last);
            w.RawLeadF = leadF;
            w.LeadF = leadF;
            w.Step = Mathf.Clamp(Mathf.FloorToInt(leadF), 0, last);
            if (rampe && !w.Hidden && lift != null && lift.Length == kette.Count)
            {
                // Nullmodell der Höhe: die Kur C17 (Lift aus dem Gleisbild) an derselben Stelle
                float alt = RailLiftAt(lift, leadF);
                var ka = RailPathPoint(kette, leadF);
                float yNeu = RailLifted(at, w.Lift).Y, yAlt = RailLifted(ka, alt).Y;
                float d = Mathf.Abs(yNeu - yAlt);
                ZugRampenBilder++;
                if (d > ZugRampenAbweichungMax)
                {
                    ZugRampenAbweichungMax = d;
                    ZugRampenWo = $"Linie {l.Slot} W{w.Index} Stueck {s} bei ({at.X:0.00},{at.Y:0.00}) f {f:0.00}: " +
                                  $"neu y {yNeu:0.0}, Kette y {yAlt:0.0}";
                }
            }
        }
    }

    // ---- der Bildauslöser --shot-when=senkrecht ---------------------------------------

    /// <summary><c>--shot-when=senkrecht</c> (bug-397): mindestens drei Waggons einer
    /// fahrenden Linie sichtbar und alle auf senkrechtem Stück (0/4) — dort fuhr der Zug vor
    /// F7 mit 2,71 statt 4 px je Takt. Kamera auf den mittleren Waggon.</summary>
    public bool ZugSenkrecht(out Vector2 at, out int linie, out string was)
    {
        at = default; linie = -1; was = "";
        foreach (var kv in _freightWagons)
        {
            int n = 0; bool alle = true;
            foreach (var w in kv.Value)
            {
                if (w.Hidden) continue;
                n++;
                if ((w.Piece & 3) != 0 || ZugImNebel(w)) { alle = false; break; }
            }
            if (!alle || n < 3) continue;
            linie = kv.Key;
            var sb = new System.Text.StringBuilder();
            Wagon? vor = null;
            foreach (var w in kv.Value)
            {
                if (w.Hidden) { sb.Append($" W{w.Index} —"); continue; }
                if (w.Index == 1) at = new Vector2(w.Col, w.Row);
                sb.Append($" W{w.Index} ({w.Col:0.00},{w.Row:0.00}) Stueck {w.Piece}");
                if (vor != null)
                    sb.Append($" Abstand {new Vector2((vor.Col - w.Col) * TileW, (vor.Row - w.Row) * TileH).Length():0.0} px");
                vor = w;
            }
            was = sb.ToString();
            return true;
        }
        return false;
    }

    // ---- die Fahrzeit-Tafel (--zug-fahrzeit-tafel) -----------------------------------

    /// <summary><c>--zug-fahrzeit-tafel</c>: einmal nach dem Laden je Linie Länge und
    /// Fahrzeit in Takten, alt (Summe der Schrittpreise, Stand bis bug-395) gegen neu
    /// (Fahrmodell je Schritt, je Richtung).</summary>
    public static bool ZugFahrzeitTafelAn;
    private bool _zugFahrzeitTafelGedruckt;

    private void ZugFahrzeitTafelTakt()
    {
        if (!ZugFahrzeitTafelAn || _zugFahrzeitTafelGedruckt || _railLines.Count == 0) return;
        _zugFahrzeitTafelGedruckt = true;
        var sb = new System.Text.StringBuilder("zug-fahrzeit-tafel: Linie | delka | Takte alt (Summe 5/4) | neu hin | neu rueck | Abweichung hin/rueck");
        double sAlt = 0, sNeu = 0; int n = 0; float maxAbw = 0f;
        var gesehen = new HashSet<int>();
        foreach (var l in _railLines)
        {
            if (!gesehen.Add(l.Slot)) continue;
            var p = ZugPlanOf(l.Slot);
            if (p == null) { sb.Append($"\n  L{l.Slot}: keine Route"); continue; }
            float alt = 0f;
            if (_linePiece.TryGetValue(l.Slot, out var pcs))
                for (int i = 1; i < pcs.Count; i++) alt += ZugSchrittTakte(pcs[i]);
            float hin = ZugPlanAnkunft(p, 0), rueck = ZugPlanAnkunft(p, 1);
            float ah = alt > 0f ? 100f * (hin - alt) / alt : 0f, ar = alt > 0f ? 100f * (rueck - alt) / alt : 0f;
            maxAbw = Mathf.Max(maxAbw, Mathf.Max(Mathf.Abs(ah), Mathf.Abs(ar)));
            sAlt += 2 * alt; sNeu += hin + rueck; n++;
            sb.Append($"\n  L{l.Slot} | {p.Delka} | {alt:0} | {hin:0} | {rueck:0} | {ah:+0.0;-0.0;0.0} % / {ar:+0.0;-0.0;0.0} %");
        }
        sb.Append($"\nzug-fahrzeit-tafel: {n} Linien, Summe hin+rueck alt {sAlt:0} / neu {sNeu:0} Takte " +
                  $"({(sAlt > 0 ? 100.0 * (sNeu - sAlt) / sAlt : 0):+0.00;-0.00} %), groesste Abweichung je Linie {maxAbw:0.0} %");
        GD.Print(sb.ToString());
    }
}
