namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE GEFECHTS-KI BILDET GRUPPEN UND MARSCHIERT</b> (23.10.2026, Paket W3 der
/// Testerrunde 0.6.11, bug-631..634; Bericht berichte/gruppen-wegpunkte-sec108-opus.md,
/// Abschnitt »Bau W3«).
///
/// <para><b>Spielerentscheidung 22.10. (3):</b> »Gefechts-KI soll Gruppen bilden und
/// marschieren, sie soll ruhig aktiv werden im Gefecht.« Bis hierher nahm das Gefecht
/// unsere eigene KI <see cref="AiFight"/> (eine Welle, sofort mit <c>AiSend</c> los, ohne
/// Sammeln). Jetzt läuft im Gefecht dieselbe Kette wie in der Kampagne — <b>keine zweite
/// Kopie</b>, sondern dieselben Bausteine aus SkirmishAiSectors.cs und KiMarschwege.cs:</para>
/// <list type="number">
///   <item>Takt 7 <c>0x4BC900</c>: <see cref="AiGruppenPflegen"/> (Gruppenlauf
///   <c>0x4BCF30</c> samt Wegpunktfahrt <c>0x4BD5FC</c>, Sammeln über 2/3, Angriff erst bei
///   Endmarke 0), <see cref="AiStaerkeraster"/>, <see cref="AiSetImpCpu"/>,
///   <see cref="AiZustandsmaschine"/> (<c>0x4BBB80</c>) und
///   <see cref="AiGruppenangriff"/> (<c>0x4BC540</c>, die Heimverteidigung).</item>
///   <item>Takt 8 <c>0x4BE2E0</c>: <see cref="AiFreieAngreifer"/> (<c>0x4BE790</c>),
///   <see cref="AiZielwahl"/> (<c>0x4BECF0</c>, mit der Sektor-Wegsuche <c>0x4BEA30</c>),
///   <see cref="AiGruppeBilden"/> (<c>0x4BC920</c>, Aufnahmeregel und Grösse
///   <c>(3·po)/2</c> geklemmt 3..99, »Take all« ohne Gebäude) und
///   <see cref="KiNachGruppeBilden"/> (Weg schreiben, 4er-Nachbarn).</item>
///   <item>Sektortafeln wie in der Kampagne: aus <c>AIxx.CWI</c>, wenn die Karte eine hat
///   (NET01..08 → AI51..58), sonst zur Laufzeit gerechnet (<see cref="KiSektorTafelnBauen"/>).</item>
/// </list>
///
/// <para><b>⚠ UNSERE SETZUNGEN — das Gefecht ist Wettkampfmodus und weicht hier bewusst ab
/// (memory akte-europa-gefecht-competitive):</b></para>
/// <list type="bullet">
///   <item><b>Aufträge ohne Missionsprogramm.</b> Das Original füllt die Auftragstafel
///   sec69 nur über <c>add_target</c> aus dem Missionsskript; ein Gefecht hat keins. Wir
///   tragen jedes feindliche Gebäude und jede feindliche Einheit ein, die DIESE KI gesehen
///   hat (<see cref="KiKennt"/>), Art 1 mit <c>c = 1</c> (Angriff) bzw. Art 2, höchstens
///   100 wie die Tafel. Vorrang (imp): Basis 9, Gebäude mit Tür 6, sonstige Gebäude 4,
///   Einheit 3. Ungesehene Einheiten fallen wieder heraus.</item>
///   <item><b>Stufen</b> (<see cref="GefechtStufe"/>): Betriebsart (pro_style, also
///   Verteidiger je Gebäudesektor) und Mindestgrösse einer Gruppe; die Pause zwischen
///   zwei Gruppen ist die Wellenpause aus KiStufen.cs.</item>
///   <item><b>Aufnahme gegen DEF_robots (+8)</b> statt DEF (+7), siehe AiGruppeBilden.</item>
///   <item><b>Berichtigte Originalfehler</b> (<see cref="GefechtBerichtigt"/>): Zielsektor
///   des GEWÄHLTEN Auftrags, Wegende bei Start == Ziel / Nachbar, Sammelkasten ohne die
///   Konstante 6 und ohne Byteüberlauf. <c>--gefecht-ki-originalfehler</c> nimmt sie zurück.</item>
///   <item><b>Nach dem Marsch:</b> stirbt das Ziel oder verliert die KI es aus der Sicht,
///   sucht sich die Gruppe ein neues bekanntes Ziel in höchstens 48 Zellen
///   (<see cref="AiPickTarget"/>), sonst löst sie sich auf; stehende Mitglieder werden in
///   jedem Lauf neu angesetzt (das Original befiehlt ohnehin in jedem Gruppenlauf).</item>
///   <item><b>Auffüllen nach Verlusten:</b> eine angreifende Gruppe unter ihrer Sollstärke
///   nimmt je Lauf bis zu 3 freie Einheiten nach derselben Aufnahmeregel nach.</item>
///   <item><b>Nachzüglerfrist:</b> kommt eine marschierende Gruppe 60 Gruppenläufe lang
///   nicht weiter (ein Mitglied steckt fest), werden die Mitglieder ausserhalb des Kastens
///   entlassen — sonst hielte die 2/3-Regel die Gruppe für immer.</item>
///   <item>Eine Gruppe unter der Mindestgrösse wird gleich wieder aufgelöst (eine
///   1er-Gruppe käme nach <c>3·1/2 &gt; 1</c> nie weiter).</item>
///   <item>Ohne bekanntes Ziel fährt die KI spähen (<see cref="KiSpaehen"/>, wie bisher).</item>
/// </list>
///
/// <para>Gleichschritt: Würfe nur über <see cref="Simulation.Determinism.Roll"/> (in den
/// gelesenen Bausteinen), alle Schleifen über <c>_entities</c> in Indexfolge, die
/// Nachschlagetafeln unten nur für <c>ContainsKey</c>/<c>TryGetValue</c>.</para>
///
/// <para>Gegenschalter <c>--gefecht-ki-marsch-aus</c>: AiFight genau wie vorher (NET07
/// <c>E7AF8F62847C82F4</c>). Prüfstand <c>--gefecht-ki-aktiv-check[=takte]</c>
/// (Simulation/GefechtKiAktivCheck.cs).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--gefecht-ki-marsch-aus</c> — siehe Kopf.</summary>
    public static bool GefechtKiMarschAus;

    /// <summary><c>--gefecht-ki-originalfehler</c> — auch im Gefecht die drei
    /// Originalfehler des Marschwegs (wie in der Kampagne).</summary>
    public static bool GefechtKiOriginalfehler;

    /// <summary>Gilt die Gruppen-KI gerade? Gefecht (keine Kampagne), kein Gegenschalter.</summary>
    private bool GefechtKiMarschAn => !GefechtKiMarschAus && !InCampaign;

    /// <summary>Gelten im Marschweg die Berichtigungen? Nur im Gefecht mit Gruppen.</summary>
    private bool GefechtBerichtigt => GefechtKiMarschAn && !GefechtKiOriginalfehler;

    /// <summary>Sekunden ohne Weiterschalten, nach denen Nachzügler entlassen werden;
    /// gezählt in Gruppenläufen (ein Lauf je Denktakt der Stufe).</summary>
    private const float GefechtMarschFristSek = 120f;
    private static int GefechtMarschFrist(AiLevel l) => Math.Min(250, (int)Math.Ceiling(GefechtMarschFristSek / AiTuning(l).think));

    /// <summary>Höchstens so weit (Zellen) sucht eine Gruppe nach dem Marsch ein neues Ziel.</summary>
    private const float GefechtNachsetzWeite = 48f;

    /// <summary>
    /// Das Zahlenwerk einer Stufe. ⚠ UNSERE SETZUNG.
    /// <code>
    ///                          leicht   normal   schwer
    ///   Betriebsart (sec61)       3        4        5
    ///   pro_style (gelesen)      100      400      255    -> Verteidiger je Gebaeude (imp 6): 6 / 1 / 2
    ///   Gruppengroesse         (3po)/2  (3po)/2    99 = aller Ueberschuss (C 0x4BC920: sec61 == 5)
    ///   Mindestgroesse            4        5        6
    ///   Pause zwischen Gruppen   40 s     20 s     10 s   (KiStufen.cs, WellenPause)
    ///   Denktakt (AiTuning)      3,0 s    1,5 s    0,8 s
    /// </code>
    /// </summary>
    private static (int Betriebsart, int Mindest) GefechtStufe(AiLevel l) => l switch
    {
        AiLevel.Easy => (3, 4),
        AiLevel.Hard => (5, 6),
        _ => (4, 5),
    };

    private string GefechtStufeZeile(AiLevel l)
    {
        var (ba, mind) = GefechtStufe(l);
        int stil = ProStyleC[Math.Clamp(ba, 0, 7)];
        return $"Betriebsart {ba} (pro_style {stil}, {Math.Min(100, 100 * 6 / Math.Max(1, stil))} Verteidiger je Gebaeude), " +
               $"Mindestgroesse {mind}, Pause {(KiStufenAn ? ProfilOf(l).WellenPause : 20f):0} s" +
               (GefechtKiOriginalfehler ? ", Originalfehler des Marschwegs (--gefecht-ki-originalfehler)" : ", Marschweg berichtigt");
    }

    /// <summary>NN der Sektortafeln im Gefecht: <c>map_NETxx</c> → 50 + xx
    /// (W2.4: AI51..58 gehören zu NET01..08), sonst −1 (rechnen). ⚠ UNSERE SETZUNG,
    /// die Zuordnung ist nicht an der EXE nachgelesen.</summary>
    private int GefechtKiTafelNummer()
    {
        if (!GefechtKiMarschAn) return -1;
        string m = UI.SkirmishSetup.Map ?? "";
        int k = m.LastIndexOf("NET", StringComparison.OrdinalIgnoreCase);
        if (k < 0) return -1;
        return int.TryParse(m[(k + 3)..], out int n) && n is >= 1 and <= 49 ? 50 + n : -1;
    }

    // ---- Zähler (Statuszeile und Prüfstand) ----------------------------------

    public int GefechtGruppenGebildet, GefechtGruppenZuKlein, GefechtNeueZiele, GefechtAufgeloestOhneZiel,
               GefechtAufgefuellt, GefechtNachzuegler, GefechtNachgesetzt, GefechtSpaehen;
    private readonly Dictionary<string, int> _gefGruende = new();
    private void GefechtGrund(int p, string g)
    {
        string k = $"P{p} {g}";
        _gefGruende[k] = _gefGruende.GetValueOrDefault(k) + 1;
    }
    public string GefechtGruendeZeile()
    {
        var keys = new List<string>(_gefGruende.Keys);
        keys.Sort(StringComparer.Ordinal);
        var teile = new List<string>();
        foreach (var k in keys) teile.Add($"{k} {_gefGruende[k]}x");
        return string.Join(", ", teile);
    }

    /// <summary>Für den Prüfstand: jede gebildete Gruppe (Takt, Spieler, Platz, Grösse).</summary>
    public static readonly List<(int Takt, int P, int G, int Groesse, bool Marsch)> GefechtGruppenLog = new();

    // ---- der Zug ------------------------------------------------------------

    /// <summary>Der KI-Zug im Gefecht (statt <see cref="AiFight"/>), einmal je Denktakt.</summary>
    private void GefechtKiZug(AiPlayer a, int guard)
    {
        int p = a.Player;
        if (_nav == null) return;
        KiTafelnSicher();
        var stufe = GefechtStufe(a.Level);

        GefechtZieleAuffrischen(p);
        GefechtNachsetzenVorher(a);

        // Takt 7 — die gelesene Kette, wie AiSektorAngriff/AiMissionAttack.
        AiGruppenPflegen(p);
        AiStaerkeraster(p);
        AiSetImpCpu(p);
        AiZustandsmaschine(a);
        GefechtNachsetzenNachher(a);
        a.Wave.Clear();
        AiGruppenangriff(a);

        // Takt 8 — target: 0x4BECF0, mit unserer Pause dazwischen.
        if (a.AttackTimer > 0f) { GefechtGrund(p, "Pause"); return; }
        var list = _missionTargets[p];
        if (list.Count == 0)
        {
            var frei = new List<int>();
            foreach (int i in ArmyOf(p))
            {
                var e = _entities[i];
                if (e.DugIn || i == a.Grabber || e.AiCpu0 == 10) continue;
                frei.Add(i);
            }
            if (frei.Count > guard && KiSpaehen(a, frei, guard, AiCenter(p))) GefechtSpaehen++;
            GefechtGrund(p, "kein bekanntes Ziel");
            return;
        }
        int freie = AiFreieAngreifer(p, out int sx, out int sy);
        if (freie < stufe.Mindest) { GefechtGrund(p, "zu wenige freie"); return; }   // UNSERE Schwelle

        var (platz, po) = AiZielwahl(p, list, sx, sy);
        if (platz < 0) { GefechtGrund(p, "kein Ziel waehlbar"); return; }
        if (!(freie > po || _aiSec61[p] == 5)) { GefechtGrund(p, "freie <= po"); return; }   // C 0x4BEF.., sec61 == 5 haengt aus
        var auftrag = list[platz];
        int ziel = ResolveTarget(p, auftrag);
        if (ziel < 0) return;
        // UNSERE SETZUNG: erst zaehlen, ob die Aufnahmeregel ueberhaupt die Mindestgroesse
        // hergibt — eine gebildete und gleich wieder aufgeloeste Gruppe wuerfe ihre
        // Mitglieder aus Modus 1/2 und die Zustandsmaschine schickte sie neu los.
        if (GefechtAufnehmbar(a) < stufe.Mindest) { GefechtGrund(p, "zu wenige aufnehmbar"); return; }

        int g = AiGruppeBilden(p, auftrag, po, a);
        if (g < 0) { GefechtGrund(p, "keine Gruppe"); return; }
        var gr = _aiGruppen[p][g];
        if (gr.Einheiten.Count < stufe.Mindest)
        {
            // UNSERE SETZUNG: eine zu kleine Gruppe gleich wieder auflösen.
            GefechtGruppenZuKlein++;
            GefechtGrund(p, "Gruppe zu klein");
            AiGruppeAufloesen(p, g);
            return;
        }
        gr.Soll = gr.Einheiten.Count;
        gr.Frist = 0;
        GefechtGruppenGebildet++;
        a.AttackTimer = KiStufenAn ? ProfilOf(a.Level).WellenPause : 20f;   // ours
        a.Waves++;
        KiBuch(a).Wellen++;                                                 // --ki-stufen-check
        GefechtGrund(p, "Gruppe gebildet");

        bool marsch = KiNachGruppeBilden(p, g, sx, sy);
        if (GefechtGruppenLog.Count < 500) GefechtGruppenLog.Add((_taktNr, p, g, gr.Einheiten.Count, marsch));
        if (marsch)
        {
            a.Wave.Clear();
            foreach (int i in gr.Einheiten) a.Wave.Add(i);
            a.TargetIdx = ziel;
            GD.Print($"KI P{p} (Gefecht): Gruppe {g} mit {gr.Einheiten.Count} Einheiten MARSCHIERT " +
                     $"(Index {KiIndex(p, g)}, Ende {KiEnde(p, g)}) Richtung {_entities[ziel].Name} " +
                     $"bei ({_entities[ziel].Col},{_entities[ziel].Row}) — po={po}, freie={freie}, Start-Sektor ({sx},{sy})");
        }
        else AiGruppeLosschicken(p, g, ziel, $"Gefecht ohne Marschweg, po={po}, freie={freie}");
        NoteEvent(_entities[ziel], $"KI P{p} Gruppe {g}");
    }

    // ---- die Auftragstafel ohne Missionsprogramm ----------------------------

    private readonly Dictionary<int, int> _gefErsterBau = new(), _gefErsteEinheit = new();
    private readonly HashSet<long> _gefSchon = new();

    /// <summary>Vorrang eines Gebäudes/einer Einheit als Auftrag. UNSERE SETZUNG.</summary>
    private static int GefechtVorrang(Entity e) => !e.IsBuilding ? 3 : e.BType == 1 ? 9 : e.Doors > 0 ? 6 : 4;

    /// <summary>Platz → erster lebender Index (die Auflösung von ResolveTarget Art 1/2),
    /// damit ein Auftrag nur eingetragen wird, wenn er genau dieses Ding meint.</summary>
    private void GefechtPlaetzeTafeln()
    {
        _gefErsterBau.Clear(); _gefErsteEinheit.Clear();
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.Slot < 0) continue;
            var t = e.IsBuilding ? _gefErsterBau : _gefErsteEinheit;
            if (!t.ContainsKey(e.Slot)) t[e.Slot] = i;
        }
    }

    private void GefechtZieleAuffrischen(int p)
    {
        var list = _missionTargets[p];
        // 1. erledigte, nicht mehr feindliche und (Einheiten) ungesehene streichen —
        //    ausser ein Gruppenplatz haelt den Auftrag noch.
        for (int k = list.Count - 1; k >= 0; k--)
        {
            var t = list[k];
            if (AiAuftragHatGruppe(p, t)) continue;
            int i = ResolveTarget(p, t);
            bool weg = i < 0 || !AiHostile(p, _entities[i].Owner) || (t.Kind == 2 && !KiKennt(p, _entities[i]));
            if (weg) list.RemoveAt(k);
        }
        if (list.Count >= 100) return;

        // 2. neu Gesehenes anhaengen — erst Gebaeude, dann Einheiten, je in Indexfolge.
        GefechtPlaetzeTafeln();
        _gefSchon.Clear();
        foreach (var t in list) _gefSchon.Add(((long)t.Kind << 32) | (uint)t.Word);
        for (int pass = 0; pass < 2 && list.Count < 100; pass++)
            for (int i = 0; i < _entities.Count && list.Count < 100; i++)
            {
                var e = _entities[i];
                if (e.IsProp || e.Dead || e.Slot < 0) continue;
                if (e.IsBuilding != (pass == 0)) continue;
                if (e.Owner is < 0 or > 7 || !AiHostile(p, e.Owner)) continue;
                if (e.IsBuilding && (e.NoStructure || e.Built == 0)) continue;
                if (!e.IsBuilding && !e.Mobile) continue;
                var tafel = e.IsBuilding ? _gefErsterBau : _gefErsteEinheit;
                if (!tafel.TryGetValue(e.Slot, out int erster) || erster != i) continue;   // Platz doppelt
                int kind = e.IsBuilding ? 1 : 2;
                long key = ((long)kind << 32) | (uint)e.Slot;
                if (_gefSchon.Contains(key)) continue;
                if (!KiKennt(p, e)) continue;
                _gefSchon.Add(key);
                list.Add(new MissionTarget { Kind = kind, Priority = GefechtVorrang(e), Word = e.Slot, Second = 1 });
            }
    }

    // ---- nach dem Marsch, Nachzuegler, Auffuellen ---------------------------

    private Vector2 GefechtGruppenMitte(AiGruppe gr)
    {
        var sum = Vector2.Zero; int n = 0;
        foreach (int i in gr.Einheiten)
        {
            if (i < 0 || i >= _entities.Count || _entities[i].Dead) continue;
            sum += new Vector2(_entities[i].Col, _entities[i].Row); n++;
        }
        return n > 0 ? sum / n : Vector2.Zero;
    }

    /// <summary>Vor dem Gruppenlauf: Nachzüglerfrist im Marsch; nach dem Marsch ein
    /// gestorbenes, übergelaufenes oder aus der Sicht verlorenes Ziel ersetzen.</summary>
    private void GefechtNachsetzenVorher(AiPlayer a)
    {
        int p = a.Player;
        for (int g = 0; g < 4; g++)
        {
            var gr = _aiGruppen[p][g];
            if (gr.Einheiten.Count == 0) continue;

            if (KiEnde(p, g) != 0)
            {
                // UNSERE SETZUNG: stirbt das Ziel waehrend des Marsches (im Gefecht oft eine
                // Einheit), marschiert die Gruppe weiter und nimmt das bekannte Ziel, das dem
                // Endsektor ihres Weges am naechsten liegt. Im Original loest sie sich auf.
                int zm = gr.Auftrag == null ? -1 : ResolveTarget(p, gr.Auftrag);
                if (zm < 0 || !AiHostile(p, _entities[zm].Owner))
                {
                    var (ex, ey) = KiWegpunkt(p, g, 29);
                    var t2 = GefechtNeuesZiel(p, new Vector2(ex * SektorFeld + 12, ey * SektorFeld + 12), float.MaxValue, out _);
                    if (t2 == null) { GefechtAufgeloestOhneZiel++; AiGruppeAufloesen(p, g); continue; }
                    gr.Auftrag = t2;
                    GefechtNeueZiele++;
                }
                // im Marsch: Frist zaehlen, bei Weiterschalten (Index neu) zuruecksetzen —
                // KiWegpunktfahrt kennt die Frist nicht, darum hier ueber den Index.
                int idx = KiIndex(p, g);
                if (gr.Frist >> 8 != idx) gr.Frist = idx << 8;          // oberes Byte: Index
                gr.Frist++;
                if ((gr.Frist & 0xFF) < GefechtMarschFrist(a.Level)) continue;
                var (wx, wy) = KiWegpunkt(p, g, idx);
                var (loX, hiX, loY, hiY) = KiWegKasten(wx, wy);
                int raus = 0;
                for (int k = gr.Einheiten.Count - 1; k >= 0; k--)
                {
                    int i = gr.Einheiten[k];
                    if (i < 0 || i >= _entities.Count) continue;
                    var u = _entities[i];
                    if (u.Dead) continue;
                    if (loX < u.Col && u.Col < hiX && loY < u.Row && u.Row < hiY) continue;
                    AiFreigeben(u);
                    gr.Einheiten.RemoveAt(k);
                    raus++;
                }
                GefechtNachzuegler += raus;
                gr.Frist = idx << 8;
                if (raus > 0) GD.Print($"KI P{p} (Gefecht): Gruppe {g} laesst {raus} Nachzuegler zurueck (Frist {GefechtMarschFrist(a.Level)} Laeufe = {GefechtMarschFristSek:0} s)");
                continue;
            }

            int zi = gr.Auftrag == null ? -1 : ResolveTarget(p, gr.Auftrag);
            bool gut = zi >= 0 && AiHostile(p, _entities[zi].Owner) && KiKennt(p, _entities[zi]);
            if (gut) continue;
            var t = GefechtNeuesZiel(p, GefechtGruppenMitte(gr), GefechtNachsetzWeite, out int neu);
            if (t == null)
            {
                GefechtAufgeloestOhneZiel++;
                AiGruppeAufloesen(p, g);
                continue;
            }
            gr.Auftrag = t;
            GefechtNeueZiele++;
            // noch nicht losgeschickt: das tut der Gruppenlauf gleich (KiGruppeNachMarsch)
            if (gr.Losgeschickt) AiGruppeLosschicken(p, g, neu, "Gefecht: neues Ziel nach dem Marsch");
        }
    }

    /// <summary>Ein neues bekanntes Ziel nahe <paramref name="von"/> (<see cref="AiPickTarget"/>,
    /// Gebäude zählen doppelt) als Auftrag Art 1/2 mit <c>c = 1</c> — oder null.</summary>
    private MissionTarget? GefechtNeuesZiel(int p, Vector2 von, float weite, out int idx)
    {
        idx = AiPickTarget(p, von);
        if (idx < 0) return null;
        var e = _entities[idx];
        if (e.Slot < 0 || von.DistanceTo(new Vector2(e.Col, e.Row)) > weite) { idx = -1; return null; }
        var t = new MissionTarget { Kind = e.IsBuilding ? 1 : 2, Priority = GefechtVorrang(e), Word = e.Slot, Second = 1 };
        if (ResolveTarget(p, t) != idx) { idx = -1; return null; }            // Platz doppelt
        return t;
    }

    /// <summary>Nach der Zustandsmaschine (frisches Raster): angreifende Gruppen setzen
    /// stehende Mitglieder neu an und füllen nach Verlusten auf.</summary>
    private void GefechtNachsetzenNachher(AiPlayer a)
    {
        int p = a.Player;
        var r = _aiRaster[p];
        for (int g = 0; g < 4; g++)
        {
            var gr = _aiGruppen[p][g];
            if (gr.Einheiten.Count == 0 || !gr.Losgeschickt || KiEnde(p, g) != 0) continue;
            int zi = gr.Auftrag == null ? -1 : ResolveTarget(p, gr.Auftrag);
            if (zi < 0 || !AiHostile(p, _entities[zi].Owner) || !KiKennt(p, _entities[zi])) continue;
            bool tuer = IstTuergriff(gr.Auftrag);
            if (tuer) continue;

            foreach (int i in gr.Einheiten)
            {
                if (i < 0 || i >= _entities.Count) continue;
                var e = _entities[i];
                if (e.Dead || e.Target >= 0 || e.Path != null) continue;
                AiSend(i, zi);
                GefechtNachgesetzt++;
            }

            int fehlt = Math.Min(3, gr.Soll - gr.Einheiten.Count);
            for (int i = 0; i < _entities.Count && fehlt > 0 && gr.Einheiten.Count < AiGruppe.MaxEinheiten; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p || !e.Mobile) continue;
                if (!CanFight(e) || AiUnarmed(e) || e.DugIn || i == a.Grabber) continue;
                int s = -1;
                if (_aiSec110[p] == 0)
                {
                    if (e.AiCpu0 >= 5) continue;
                }
                else
                {
                    if (e.AiCpu0 is not (1 or 2)) continue;
                    s = AiSektorAus(e.AiCpu1);
                    if (r[s].DefRobots >= r[s].Belegt) continue;
                }
                gr.Einheiten.Add(i);
                e.AiCpu0 = 10;
                e.AiCpu1 = g;
                if (s >= 0) r[s].Belegt--;
                AiSend(i, zi);
                GefechtAufgefuellt++;
                fehlt--;
            }
        }
    }

    private readonly int[] _gefRest = new int[SektorZahl];

    /// <summary>Wie viele Einheiten <see cref="AiGruppeBilden"/> mit
    /// <c>gefecht != null</c> jetzt aufnehmen würde — dieselbe Regel, ohne etwas zu ändern.</summary>
    private int GefechtAufnehmbar(AiPlayer a)
    {
        int p = a.Player;
        var r = _aiRaster[p];
        for (int s = 0; s < SektorZahl; s++) _gefRest[s] = r[s].Belegt - r[s].DefRobots;
        int n = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != p || !e.Mobile) continue;
            if (!CanFight(e) || AiUnarmed(e) || e.DugIn || i == a.Grabber) continue;
            if (_aiSec110[p] == 0) { if (e.AiCpu0 < 5) n++; continue; }
            if (e.AiCpu0 is not (1 or 2)) continue;
            int s2 = AiSektorAus(e.AiCpu1);
            if (_gefRest[s2] <= 0) continue;
            _gefRest[s2]--;
            n++;
        }
        return n;
    }

    /// <summary>Statuszeile für Prüfstände.</summary>
    public string GefechtKiZeile()
        => $"Gefechts-KI: {(GefechtKiMarschAn ? "Gruppen/Marsch" : "AiFight (--gefecht-ki-marsch-aus)")}, " +
           $"Gruppen {GefechtGruppenGebildet} (zu klein {GefechtGruppenZuKlein}), neue Ziele nach dem Marsch {GefechtNeueZiele}, " +
           $"aufgeloest ohne Ziel {GefechtAufgeloestOhneZiel}, aufgefuellt {GefechtAufgefuellt}, nachgesetzt {GefechtNachgesetzt}, " +
           $"Nachzuegler {GefechtNachzuegler}, Spaehfahrten {GefechtSpaehen}";
}
