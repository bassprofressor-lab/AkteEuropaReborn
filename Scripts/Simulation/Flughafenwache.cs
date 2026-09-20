namespace AkteEuropaReborn.Rendering;

using Godot;
using System.Collections.Generic;

/// <summary>
/// <b>RECYCLE UND DIE FLUGHAFENWACHE</b> (20.09.2026, nach
/// <c>berichte/recycle-patrouille-opus.md</c> — die zwei letzten Punkte aus
/// Block A der K20-Lesung).
///
/// <para>Beide Knöpfe des Flughafenfensters waren gesperrt, weil von ihnen
/// <b>nur der Opcode</b> bekannt war. Jetzt sind beide Arme gelesen.</para>
///
/// <para>⚠ <b>Der Weg dahin war eine Thunk-Tafel.</b> Die Arme rufen über
/// <c>0x40xxxx</c> (<c>E9 rel32</c>, Schrittweite 5). Bevor die unbekannten
/// Ziele galten, liefen drei <b>bekannte</b> durch dasselbe Verfahren —
/// <c>0x402397 → 0x4513F0</c> (Bombenwechsler), <c>0x4020D6 → 0x427090</c>
/// (Air shoot), <c>0x401FFA → 0x444D90</c> (Kartenschirm). Alle drei stimmten
/// mit der bisherigen Lesung überein; <b>erst danach</b> wurden
/// <c>0x40163B → 0x4B27D0</c> und <c>0x401B95 → 0x428940</c> übernommen.</para>
///
/// <h3>RECYCLE — Befehl 535, <c>0x4B27D0(cis, idx)</c></h3>
/// <code>
///   0x4B27E4  0x43BF00(cis, idx)                  ; aus dem Hangar nehmen
///   0x4B2815  pct = 100 * HP(+0x19) / HP_MAX(+0x1A)
///   0x4B285B  Zeile = 0x51B020 + 48*(20*Eigner(+0x09) + Entwurf(+0x32))
///   0x4B286C  Gebaeude +0x2C += Zeile[+0x1F] * pct / 100
///   0x4B2881  Gebaeude +0x2E += Zeile[+0x20] * pct / 100
///   0x4B2896  Gebaeude +0x30 += Zeile[+0x21] * pct / 100
///   0x4B289E  Muster(+0x08) := 0                  ; die Maschine ist WEG
/// </code>
/// <para>Ein halb zerschossener Jagdflieger bringt also <b>25/25/0</b>, ein
/// heiler <b>50/50/0</b>. Es gibt <b>kein Geld</b> — nur die drei Teilelager,
/// und sie gehören dem <b>Flughafen</b>, nicht dem Spieler.</para>
///
/// <h3>DIE WACHE — die Patrouille-Flagge <c>+0x43</c></h3>
/// <para>Die Routine heisst im Original <b><c>guard:</c></b>
/// (<c>0x428940</c>, Zeichenkette <c>0x4F98C8</c>; die übrigen Protokollwörter
/// sind tschechisch — <c>vzd</c> = Abstand).</para>
/// <code>
///   Ausloeser im Gebaeudetakt @0x43EB11:
///     (Takt + Gebaeudeplatz) % 10 == 0   UND   Art(+0x04) == 9   UND   +0x43 != 0
///
///   guard(Flughafenplatz):
///     1. steht ein JAGDFLIEGER (Muster 1, uk 0) im Hangar?   sonst Ende   @0x4289F6
///     2. das NAECHSTE feindliche FLIEGENDE Flugzeug:                      @0x428A8E
///          Muster 1..12 (die Nachschubhelis 13/14 zaehlen NICHT)
///          uk != 0                                  (es fliegt)
///          byte[0x87B155 + 40*meinEigner + seinEigner] == 0   (Feind)
///          d2 = (dSpalte)^2 + (dZeile)^2
///     3. JEDEN bereiten Jaeger mit Modus 7 auf dessen Zelle starten       @0x428C52
///          -> launch_aircraft 0x426020, dieselbe Routine wie Befehl 502
/// </code>
///
/// <para>⭐⭐ <b>Es gibt KEINEN Kreisflug.</b> Unsere <c>AirPatrol</c> war eine
/// Setzung aus der Zeit, als diese Wirkung ungelesen war — und die Flagge tat
/// bis heute <b>gar nichts</b>, sie wurde nur angezeigt. Der Kreisflug bleibt,
/// aber als das, was er ist: das Verhalten eines Flugzeugs, das nichts zu tun
/// hat (seine Bitte vom 18.08.: »Eine Schleife um den Flughafen bis Gegner
/// auftauchen wäre praktisch«). Er ist <b>nicht</b> die Patrouille.</para>
///
/// <para>⚠⚠ <b>DIE ZWEI SCHWELLEN.</b> Das Original nimmt in der Suchschleife
/// <c>d² &lt; 3601</c> an (@0x428B4D, <c>ebp</c> startet auf <c>0xE11</c>) und
/// <b>verwirft danach</b> <c>min ≥ 3600</c> (@0x428B81). Ein Ziel bei genau
/// <c>d² = 3600</c> — also 60 Zellen auf einer Achse — wird damit gefunden und
/// sofort wieder weggeworfen. Wirksam ist <b><c>d² ≤ 3599</c></b>. Wir fassen
/// die zwei Konstanten zu einer zusammen (<see cref="WacheWeiteQuadrat"/> =
/// 3600, angenommen wird <c>d² &lt; 3600</c>) — <b>das ergibt dieselbe
/// Menge</b>: ein einziger Kandidat bei 3600 fällt so wie so durch, und gegen
/// einen näheren verliert er in beiden Fassungen. Wer »Radius 60« baut, hat auf
/// der Achse einen Fall zu viel.</para>
///
/// <para>Gegenschalter <c>--recycle-aus</c> und <c>--flughafenwache-aus</c>.
/// Prüfstand <c>--flughafenwache-probe</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--recycle-aus</c> — der Knopf bleibt gesperrt, wie bis zum
    /// 20.09. Darunter MUSS <see cref="RecycleVerwertet"/> auf 0 bleiben.</summary>
    public static bool RecycleAus;

    /// <summary><c>--flughafenwache-aus</c> — die Flagge tut wieder nichts (sie
    /// wird nur angezeigt), wie bis zum 20.09. Darunter MUSS
    /// <see cref="WacheGestartet"/> auf 0 bleiben.</summary>
    public static bool FlughafenwacheAus;

    /// <summary>Der Takt, in dem ein Flughafen an die Reihe kommt: <b>10</b>
    /// (@0x43EB18). ⚠ Phasenverschoben nach GEBÄUDEPLATZ — ohne das zucken
    /// zehn Flughäfen im selben Takt.</summary>
    public const int WachePeriode = 10;

    /// <summary>Der Suchradius als QUADRAT. Siehe den Klassenkopf: das Original
    /// hat hier zwei Konstanten (3601 und 3600), die zusammen <c>d² ≤ 3599</c>
    /// ergeben; eine reicht.</summary>
    public const int WacheWeiteQuadrat = 3600;

    /// <summary>Die Vorlagenart, die Wache fliegt: <b>1 = Jagdflieger</b>
    /// (@0x428BFB und @0x4289D8 prüfen beide auf 1).</summary>
    public const int WacheArt = 1;

    // ---- die Zahlen fuer den Pruefstand -----------------------------------
    public int RecycleVerwertet, RecycleTeileW, RecycleTeileF, RecycleTeileS;
    public int WacheLaeufe, WacheOhneJaeger, WacheOhneZiel, WacheGestartet, WacheAuftraege;

    // ====================== RECYCLE ========================================

    /// <summary>
    /// <b>Befehl 535</b> — die gewählte Hangarzeile verwerten.
    /// </summary>
    /// <returns>false, wenn nichts geschah.</returns>
    public bool RecycleAmFlughafen(int gebaeude, int slot)
    {
        if (RecycleAus) return false;
        var e = GebaeudeMitSlot(gebaeude);
        if (e == null || e.BType != 9) return false;
        int idx = _special.FindIndex(x => x.Slot == slot && !x.Dead);
        if (idx < 0) { _order = "Diese Maschine gibt es nicht mehr."; return false; }
        var a = _special[idx];
        // ⚠ Das Original prueft das NICHT — es nimmt die Zeile, die im Fenster
        // steht, und im Fenster stehen nur eingelagerte Maschinen. Bei uns
        // koennte der Aufrufer sich irren, also wird es gesagt.
        if (!a.Stored) { _order = "Nur eine Maschine im Hangar laesst sich verwerten."; return false; }

        // pct = 100 * HP / HP_MAX  (@0x4B2815)
        int pct = a.HpMax > 0 ? 100 * a.Hp / a.HpMax : 0;
        var d = EntwurfVon(a);
        int w = d == null ? 0 : d.CostW * pct / 100;
        int f = d == null ? 0 : d.CostF * pct / 100;
        int s = d == null ? 0 : d.CostS * pct / 100;
        e.StockW += w; e.StockF += f; e.StockS += s;
        RecycleTeileW += w; RecycleTeileF += f; RecycleTeileS += s;

        e.Hangar?.Remove(a.Slot);
        a.Dead = true;                       // Muster := 0 (@0x4B289E)
        a.Stored = false;
        RecycleVerwertet++;
        if (HandLuftIdx == idx) HandsteuerungLuftBeenden("verwertet");
        _order = d == null
            ? $"{a.Name} verwertet (kein Entwurf — nichts zurueck)"
            : $"{a.Name} verwertet bei {pct}% — zurueck: W {w}, F {f}, S {s}";
        UpdatePanel();
        QueueRedraw();
        return true;
    }

    /// <summary>Die Entwurfszeile einer Maschine — <c>20·Eigner + Entwurf</c>
    /// im Original (@0x4B285B). Bei uns steht die Art im Satz, und die
    /// Entwurfsliste trägt sie mit; gesucht wird über <b>Spieler und Art</b>.
    /// ⚠ Findet sich keine, gibt Recycle NICHTS zurück, statt zu raten.</summary>
    private AirDesign? EntwurfVon(Special a)
    {
        if (_airDesigns == null) return null;
        return _airDesigns.Find(d => d.Player == a.Owner && d.Kind == a.Kind)
            ?? _airDesigns.Find(d => d.Kind == a.Kind);
    }

    // ====================== DIE WACHE ======================================

    /// <summary>
    /// <b>Der Gebäudetakt der Wache</b> — @0x43EB11. Läuft je Spieltakt und
    /// nimmt sich die Flughäfen vor, die gerade an der Reihe sind.
    /// </summary>
    public void WacheTakt()
    {
        if (FlughafenwacheAus) return;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.BType != 9) continue;
            if (!e.Patrouille) continue;
            // ⭐ Die Phasenverschiebung nach dem PLATZ (@0x43EB15): sonst
            // zucken alle Flughaefen im selben Takt.
            if (((DebugTicks + e.Slot) % WachePeriode) != 0) continue;
            Wache(e);
        }
    }

    /// <summary><c>guard:</c> — <c>0x428940(Flughafenplatz)</c>.</summary>
    private void Wache(Entity e)
    {
        // ⚠⚠ 20.09.2026 — DER SCHALTER GEHOERT AN DIE WIRKUNG, NICHT NUR AN
        // DEN TAKT. Er stand allein in WacheTakt, und der Pruefstand ruft Wache
        // unmittelbar: unter --flughafenwache-aus stiegen die Jaeger trotzdem
        // auf, das Nullmodell war also blind. Derselbe Fehler wie beim
        // Kartenschirm-Zoom eine Stunde vorher. Jetzt hier.
        if (FlughafenwacheAus) return;
        WacheLaeufe++;

        // --- 1. steht ein bereiter Jaeger im Hangar? (@0x4289A5) -----------
        var bereit = new List<int>();
        if (e.Hangar != null)
            foreach (int slot in e.Hangar)
            {
                int k = _special.FindIndex(x => x.Slot == slot && !x.Dead);
                if (k < 0) continue;
                var m = _special[k];
                if (m.Kind != WacheArt) continue;      // nur Jagdflieger
                if (!m.Stored || m.Absturz) continue;  // uk 0 = im Hangar
                bereit.Add(k);
            }
        if (bereit.Count == 0) { WacheOhneJaeger++; return; }

        // --- 2. das naechste feindliche FLIEGENDE Flugzeug (@0x428A8E) -----
        int ziel = -1, min = WacheWeiteQuadrat;
        for (int k = 0; k < _special.Count; k++)
        {
            var a = _special[k];
            if (a.Dead) continue;
            // Muster 1..12 — die zwei Nachschubarten zaehlen NICHT (@0x428AAF).
            if (a.Kind <= 0 || a.Kind >= 13) continue;
            if (a.Stored) continue;                    // uk 0 -> es fliegt nicht
            if (e.Owner is < 0 or > 7 || a.Owner is < 0 or > 7) continue;
            // Diplomatie: 0 = Feind (@0x428ADD). ⚠ Die Deutung »0 = Feind« ist
            // weiterhin V — diese Stelle ist die ZWEITE, die sie so benutzt.
            if (Allied(e.Owner, a.Owner)) continue;
            int dc = a.Col - e.Col, dr = a.Row - e.Row;
            int d2 = dc * dc + dr * dr;
            if (d2 >= min) continue;                   // siehe Klassenkopf
            min = d2; ziel = k;
        }
        if (ziel < 0) { WacheOhneZiel++; return; }

        // --- 3. JEDEN bereiten Jaeger starten (@0x428BBE) ------------------
        var t = _special[ziel];
        var mitte = ZellMitte(t.Col, t.Row);
        int n = 0;
        foreach (int k in bereit)
        {
            var m = _special[k];
            if (!m.Stored) continue;
            m.Stored = false;
            m.Pos = e.Pos;
            m.Col = e.Col; m.Row = e.Row;
            m.Alt = ElevOf(m.Col, m.Row) * 15;         // air_takeoff @0x4260B9
            m.Sollhoehe = FlughoeheMax;
            FlugtempoStart(m);                         // dir 180, sp 0 (bug-348)
            e.Hangar?.Remove(m.Slot);
            // Modus 7 — ein ZIEL, genau wie beim ANGRIFF-Knopf.
            m.Target = ziel;
            m.Goal = mitte;
            m.PlayerGoal = mitte;
            m.Angriffsauftrag = true;
            n++;
            WacheGestartet++;
        }
        if (n > 0)
        {
            WacheAuftraege++;
            _order = $"Flughafen {e.Slot}: {n} Jaeger steigen auf "
                   + $"(Ziel {(t.Name.Length > 0 ? t.Name : "Flugzeug")} "
                   + $"auf {t.Col},{t.Row})";
        }
    }

    /// <summary>Die Messzeile — <c>--flughafenwache-check</c>.
    ///
    /// <para>⚠ KONTROLLZAHL: <see cref="WacheLaeufe"/> muss die Summe der drei
    /// Ausgänge sein (kein Jäger, kein Ziel, gestartet). Läuft das auseinander,
    /// verlässt ein Lauf die Routine auf einem vierten Weg.</para></summary>
    public string FlughafenwacheAuskunft()
    {
        int enden = WacheOhneJaeger + WacheOhneZiel + WacheAuftraege;
        string r = RecycleVerwertet > 0 || RecycleAus
            ? $"recycle: {RecycleVerwertet}x verwertet, zurueck W {RecycleTeileW} "
              + $"F {RecycleTeileF} S {RecycleTeileS}"
              + (RecycleAus ? "   [--recycle-aus: 0 ist das SOLL]" : "")
            : "recycle: nie benutzt — NICHT GEMESSEN";
        if (WacheLaeufe == 0 && !FlughafenwacheAus)
            return r + " · wache: kein Flughafen mit gesetzter Flagge — NICHT GEMESSEN";
        return r + $" · wache: {WacheLaeufe} Laeufe = {WacheOhneJaeger} ohne Jaeger + "
             + $"{WacheOhneZiel} ohne Ziel + {WacheAuftraege} Auftraege "
             + (enden == WacheLaeufe ? "(stimmt)" : "⚠ LAEUFT AUSEINANDER")
             + $", {WacheGestartet} Jaeger gestartet"
             + (FlughafenwacheAus ? "   [--flughafenwache-aus: alles 0 ist das SOLL]" : "");
    }
}
