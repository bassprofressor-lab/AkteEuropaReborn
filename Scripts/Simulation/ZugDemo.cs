namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using JObj = System.Text.Json.Nodes.JsonObject;

/// <summary>
/// ⭐⭐ 01.10.2026 — bug-377, die Züge der Hauptmenü-Demos (gemeldet von einem
/// externen Spieler: »Züge halten vor Gebäuden, falsche Farbe, Abschnitte
/// überlappen, Zug neben den Schienen«). Bauvorlage berichte/zuege-demo-fable.md
/// §5, V1–V6; die Schalter stehen in ZugSchalter.cs.
///
/// <code>
///   V1  Lebensbyte +0x00 == 0 nicht laden          @0x4C768F / @0x42E111   --geisterwaggons
///   V2  Waggon im Zeilenfach, je Fach Bild-Y       @0x42E100 / @0x430C50   --zugfach-alt
///   V3  Standzeit und Bandenden unsichtbar         @0x42E14A / @0x4C6C47   --zug-steht-am-bahnsteig
///   V4  tote Linie: einmal zu Ende, dann löschen   0 Lesungen 0xA892F5     --zug-pendelt
///   V5  nie roh, −1 → neutrale Gruppe 10           @0x42B6A0               --zugfarbe-roh
///   V6  keine Kupplung (ENTSCHEIDUNG des Spielers) @0x4C687C / @0x4C688C   --zug-gekuppelt
/// </code>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Wieviele Waggonsätze <c>LoadWagons</c> wegen Lebensbyte 0 übersprungen
    /// hat, und wieviele danach geladen waren.</summary>
    public int GeisterUebersprungen, WaggonsGeladen;

    /// <summary>Wieviele Waggons <see cref="ZugAuslauf"/> am Streckenende gelöscht hat.</summary>
    public int AuslaufGeloescht;

    /// <summary>bug-395 (F4): die Stelle eines auslaufenden Waggons auf dem VERLÄNGERTEN
    /// Weg (<c>ZugWegOf</c>); <c>w.LeadF</c> bleibt die Kettenstelle.</summary>
    private readonly Dictionary<Wagon, float> _auslaufWeg = new();

    /// <summary>Das Lebensbyte +0x00 eines Waggonsatzes (sec44/121, 24 B). Der Export
    /// trägt es nur im <c>raw</c>-String; ohne ihn gilt der Satz als lebend (−1),
    /// damit eine ältere Exportdatei nicht alle Waggons verliert.</summary>
    private static int WaggonLebensbyte(JObj w)
    {
        string raw = GetS(w, "raw");
        if (raw.Length < 2) return -1;
        return int.TryParse(raw[..2], System.Globalization.NumberStyles.HexNumber, null, out int b) ? b : -1;
    }

    // ---- V5: die Zeichenfarbe ----------------------------------------------

    /// <summary>Die Farbe, mit der ein Waggon WIRKLICH gezeichnet wird. Das Original
    /// hat keinen Rohpfad: jeder Waggon geht mit einem Farbparameter in den Blitter
    /// (@0x42B6A0 → 0x40135C). Liefert unsere Knotentafel keinen Besitzer (Linie
    /// ohne Satz, Knoten ohne Gebäude), nehmen wir die neutrale Gruppe 10.
    /// ⚠ UNSERE SETZUNG: das Original liest an dieser Stelle Zufallsinhalt der
    /// Knotentafel (Bericht §3.2) — welche Farbe es dann zeigt, ist offen; sicher
    /// ist nur »nie roh«. Gegenschalter <c>--zugfarbe-roh</c>.</summary>
    private int ZugZeichenFarbe(int linie)
    {
        int f = ZugBesitzerFarbe(linie);
        return f < 0 && !ZugfarbeRoh ? NeutralFarbe : f;
    }

    // ---- V4: der Auslauf auf toten Linien ------------------------------------

    /// <summary>
    /// Ersatz für das Pendeln in <see cref="UpdateTrains"/>. Der Waggontakt des
    /// Originals (0x4C69C0..0x4C73C7) liest die Linien-faze nicht, der Rufer
    /// 0x4C7680 taktet alle 240 Sätze mit +0x00 != 0 — ein Zug auf einer Linie, die
    /// faze 3/4 trägt, fährt also zu Ende, wird gelöscht (@0x4C6C47), und es kommt
    /// kein neuer (spoj_tick überspringt faze 3/4). Gefahren wird wie im
    /// Fahrplanweg gleitend auf dem gezeichneten Weg (<c>RailPathOf</c>) mit
    /// Ausmittelung und <c>Lift</c> — die drei Hüpfer des alten Zellensprungs
    /// (Rundung, Umkehr, Höhentreppe C17) gibt es hier nicht.
    ///
    /// <para>⚠ UNSERE NÄHERUNG: das Tempo ist eine Zelle Bogenlänge je
    /// <see cref="TrainStepSeconds"/> (gerader Schritt, 5 Takte); der Diagonalpreis
    /// 28 statt 40 (@0x4C6E53) ist wie im Fahrplanweg nicht abgebildet.</para>
    /// </summary>
    private void ZugAuslauf(float dt)
    {
        bool moved = false;
        for (int i = _wagons.Count - 1; i >= 0; i--)
        {
            var w = _wagons[i];
            if (w.Freight) continue;
            // ohne Kette steht der Waggon (nur noch mit --geisterwaggons denkbar)
            if (!_lineCell.TryGetValue(w.Line, out var cells) || cells.Count < 2) continue;
            var pd = RailPathOf(w.Line);
            var route = pd?.Pts ?? cells;
            int last = route.Count - 1;
            if (last < 1) continue;
            if (!w.Auslauf)
            {
                // Aufsetzen: der nächste Punkt des gezeichneten Wegs
                w.Auslauf = true;
                int best = 0;
                float bd = float.MaxValue;
                var at0 = new Vector2(w.Col, w.Row);
                for (int k = 0; k < route.Count; k++)
                {
                    float d = (route[k] - at0).LengthSquared();
                    if (d < bd) { bd = d; best = k; }
                }
                w.LeadF = best;
            }
            // ⭐ bug-395 (F4): auf dem VERLÄNGERTEN Weg bis Routenpunkt 1 / delka−2, wie der
            // Fahrplanwaggon seit bug-382 (E2) — sonst endet der Auslauf in der Mitte der
            // letzten Gleiszelle, 12–24 px vor dem Originalpunkt, mit dem Stück der
            // Annäherung statt des Andockschritts (Bericht zug-feinlage-fable.md §4.4).
            // Gelöscht wird beim Überfahren des Wegendes (@0x4C6C47 cursor+1 == delka,
            // gezeichnet nur bis delka−2 @0x42E14F). Gegenschalter --zug-einfahrt-alt.
            var weg = !ZugEinfahrtAlt && pd != null ? ZugWegOf(w.Line, pd.Pts) : null;
            if (weg != null && weg.Cum.Length == weg.Pts.Count && weg.Cum[^1] > 0f)
            {
                var wc = weg.Cum;
                int wl = wc.Length - 1;
                if (!_auslaufWeg.TryGetValue(w, out float wf)) wf = w.LeadF + weg.Vorn;
                int t0 = Mathf.Clamp(Mathf.FloorToInt(wf), 0, wl);
                int t1 = Mathf.Clamp(t0 + 1, 0, wl);
                float sw = Mathf.Lerp(wc[t0], wc[t1], wf - t0);
                sw += w.Dir * dt * TileW / TrainStepSeconds;
                if (sw >= wc[wl] || sw <= 0f) { _auslaufWeg.Remove(w); Loesche(i); moved = true; continue; }
                wf = RailArcToIndex(wc, sw);
                _auslaufWeg[w] = wf;
                w.LeadF = Mathf.Clamp(wf - weg.Vorn, 0f, last);
                int st = Mathf.Clamp(Mathf.FloorToInt(w.LeadF), 0, last);
                w.Step = st;
                if (_lineCellPiece.TryGetValue(w.Line, out var pcw) && st < pcw.Count)
                    w.Piece = w.Dir > 0 ? pcw[st] : (pcw[st] + 4) & 7;
                ZugWegSetzen(w, weg, wf, last, w.Dir);
                w.Hidden = false;
                moved = true;
                continue;
            }
            var cum = pd?.Cum;
            if (cum != null && cum.Length == route.Count && cum[last] > 0f)
            {
                int s0 = Mathf.Clamp(Mathf.FloorToInt(w.LeadF), 0, last);
                int s1 = Mathf.Clamp(s0 + 1, 0, last);
                float s = Mathf.Lerp(cum[s0], cum[s1], w.LeadF - s0);
                s += w.Dir * dt * TileW / TrainStepSeconds;
                if (s >= cum[last] || s <= 0f) { Loesche(i); moved = true; continue; }
                w.LeadF = RailArcToIndex(cum, s);
            }
            else
            {
                w.LeadF += w.Dir * dt / TrainStepSeconds;
                if (w.LeadF >= last || w.LeadF <= 0f) { Loesche(i); moved = true; continue; }
            }
            var pt = RailPathPoint(route, w.LeadF);
            w.Col = pt.X; w.Row = pt.Y;
            w.Lift = RailLiftAt(pd?.Lift, w.LeadF);
            int step = Mathf.Clamp(Mathf.FloorToInt(w.LeadF), 0, last);
            w.Step = step;
            // dieselbe Bildwahl wie RailPlaceWagons: Fahrtrichtung, nicht Kettenrichtung
            if (_lineCellPiece.TryGetValue(w.Line, out var pcs) && step < pcs.Count)
                w.Piece = w.Dir > 0 ? pcs[step] : (pcs[step] + 4) & 7;
            w.Hidden = false;
            moved = true;
        }
        if (moved) QueueRedraw();

        void Loesche(int i)
        {
            _wagons.RemoveAt(i);             // @0x4C6C47: Satz+0x00 := 0
            AuslaufGeloescht++;
        }
    }

    // ---- V2: das Zeilenfach --------------------------------------------------

    /// <summary>Das Fach eines Waggons im Maßstab des ORIGINALS (Fach i+2 wird vor den
    /// Kacheln der Zeile i gezeichnet, @0x4B43F3 — darum ruft die Zeilenschleife seit
    /// bug-382 <c>ZugfachZeichnen(r + 2)</c>; bis dahin <c>(r)</c>, zwei Zeilen zu spät,
    /// Gegenschalter --zug-einfahrt-alt): Einreiher @0x42E157..@0x42E1F2
    /// <c>yoff = ((Stück − 2) mod 4 != 0)</c>, also 0 für die waagerechten Stücke 2/6,
    /// sonst 1; <c>Fach = Zeile + yoff + 2</c>. Der yoff hebt sich in der Bildlage
    /// wieder auf (@0x42E214 <c>− 20·yoff</c>) — er wirkt NUR auf die Reihenfolge.</summary>
    private int WaggonFach(Wagon w) => WaggonFach(w, out _, out _);

    /// <summary>Wie oben, dazu der nächste Routenpunkt <paramref name="k"/> von
    /// <paramref name="n"/> (−1, wenn die Linie keine Route hat).</summary>
    private int WaggonFach(Wagon w, out int k, out int n)
    {
        k = -1; n = 0;
        // ⚠ Zeile und Stück NICHT aus unserer Zellenkette, sondern aus der ROUTE
        // (sec34/122, halbe Zeilen): das Original rechnet mit Satz+0x01 (Zeile des
        // Routenpunkts, ganzzahlig) und Satz+0x0B (Stück des Routenschritts,
        // 0x5393F0[code]). Unsere Kette (sec22) endet zwei Routenpunkte VOR dem
        // Linienende und traegt dort das Stueck der Annaeherung, nicht das
        // waagerechte des Andockschritts (294/294, Bericht §2.1).
        // ⚠ UNSERE NAEHERUNG: der Routenpunkt ist der dem Waggon naechste.
        int zeile = Mathf.RoundToInt(w.Row), stueck = w.Piece;
        if (_lineRoute.TryGetValue(w.Line, out var pts) && pts.Count > 0)
        {
            int best = 0;
            float bd = float.MaxValue;
            var at = new Vector2(w.Col, w.Row);
            for (int q = 0; q < pts.Count; q++)
            {
                // ⚠ bug-382: der Routenpunkt (x, y.5) liegt in UNSEREN Zellkoordinaten
                // bei (x − 0,5, y) — ohne die Umrechnung ist der Abstand zu zwei
                // Nachbarpunkten gleich, und der erste gewinnt (falsches Stück/yoff).
                var rq = ZugEinfahrtAlt ? pts[q] : ZugRoutenpunktZelle(pts[q]);
                float d = (rq - at).LengthSquared();
                if (d < bd) { bd = d; best = q; }
            }
            k = best; n = pts.Count;
            zeile = Mathf.FloorToInt(pts[best].Y);
            if (_linePiece.TryGetValue(w.Line, out var rp) && rp.Count > 0)
                stueck = rp[Mathf.Clamp(best, 0, rp.Count - 1)];
        }
        int yoff = Mathf.PosMod(stueck - 2, 4) != 0 ? 1 : 0;
        return zeile + yoff + 2;
    }

    /// <summary>Das Fach eines Gebäudes im Maßstab des Originals: Einreiher @0x42FCD0,
    /// <c>Zeile + Tür0.row</c> (türlos 3) <c>+ 2</c>.</summary>
    private static int GebaeudeFachOriginal(Entity b) => b.Row + BuildingDrawRowFor(b) + 2;

    private readonly List<(int Fach, float Y, Wagon W)> _zugfach = new();

    /// <summary>Wieviele Waggons der Zeilendurchgang im letzten Bild gezeichnet hat.</summary>
    public int WaggonsImFach;

    /// <summary>Alle sichtbaren Waggons nach Fach, im Fach nach Bildpunkt-Y aufsteigend —
    /// der Sortierer @0x430C50 (Blasensortierung über die Einträge Art 0x0D nach
    /// <c>word +0x08</c>): weiter unten im Bild = später gezeichnet = vorn. Gleiche Y
    /// behalten die Einfügefolge (Platz aufwärts), darum ein stabiler Vergleich.</summary>
    private void ZugfachVorbereiten()
    {
        _zugfach.Clear();
        WaggonsImFach = 0;
        ZugImNebelVerborgen = 0;
        int n = 0;
        foreach (var w in _wagons)
        {
            if (w.Hidden) continue;
            // ⭐ bug-395 (F2): die Sichtprobe des Einreihers @0x42E197, für JEDEN Waggon
            // (Simulation/ZugEinfahrt.cs ZugImNebel). Gegenschalter --zug-ohne-sichtprobe.
            if (ZugImNebel(w)) { ZugImNebelVerborgen++; continue; }
            var at = RailLifted(new Vector2(w.Col, w.Row), w.Lift);
            _zugfach.Add((WaggonFach(w), at.Y + n++ * 1e-5f, w));
        }
        _zugfach.Sort((a, b) => a.Fach != b.Fach ? a.Fach.CompareTo(b.Fach) : a.Y.CompareTo(b.Y));
    }

    /// <summary>Alle Waggons bis einschließlich Fach <paramref name="r"/> zeichnen.</summary>
    private void ZugfachZeichnen(int r, ref int wi)
    {
        for (; wi < _zugfach.Count && _zugfach[wi].Fach <= r; wi++)
        {
            DrawWagon(_zugfach[wi].W);
            WaggonsImFach++;
        }
    }

    // ---- der Prüfstand -------------------------------------------------------

    /// <summary>Wann die Schlusszeile kommt (Spielsekunden), <c>--zug-demo-check=N</c>.</summary>
    public static float ZugDemoCheckSekunden = 80f;

    private float _zdZeit, _zdMelde;
    private bool _zdFertig;
    private readonly Dictionary<Wagon, (Vector2 Pos, Vector2 Mv, float Prog, float DProg, int Starts)> _zdVorher = new();
    private int _zdWendenMitte, _zdWendenEnde, _zdWendenBild, _zdLift0, _zdRampenBilder;
    private int _zdStandSichtbar, _zdRoh, _zdGeistSichtbar, _zdKupplung;
    private int _zdHinter, _zdDavor, _zdVerdeckt, _zdEinfahrBilder, _zdImGrundriss;
    private readonly ZugAbstandStat _zdAbstand = new();
    private string _zdDavorWo = "", _zdWendeWo = "", _zdLiftWo = "";

    private RailLine? ZdLinie(int slot)
    {
        foreach (var l in _railLines) if (l.Slot == slot) return l;
        return null;
    }

    private void PollZugDemoCheck(float dt)
    {
        if (_zdFertig) return;
        _zdZeit += dt;
        ZdBild();
        _zdMelde += dt;
        if (_zdMelde >= 10f) { _zdMelde = 0f; GD.Print($"[{_zdZeit:0}s] " + ZugDemoZeile(false)); }
        if (_zdZeit >= ZugDemoCheckSekunden)
        {
            _zdFertig = true;
            GD.Print(ZugDemoZeile(true));
        }
    }

    /// <summary>Ein Bild vermessen: Wenden, Höhe, Standzeit, Farbe, Fach, Abstand.</summary>
    private void ZdBild()
    {
        var gesehen = new HashSet<Wagon>();
        foreach (var w in _wagons)
        {
            if (w.Hidden) { _zdVorher.Remove(w); continue; }
            gesehen.Add(w);
            var l = ZdLinie(w.Line);
            if (w.Geist) _zdGeistSichtbar++;
            if (ZugZeichenFarbe(w.Line) < 0 && !ZugfarbeAlt) _zdRoh++;
            // V3: ein Fahrplanwaggon, dessen Linie steht, darf nicht zu sehen sein.
            // ⚠ bug-382: ausser im NACHLAUF (Nachlaeufer 4/7/11 Takte nach Waggon 0,
            // @0x4C6C47 je Satz) — bis dahin belohnte V3 das gleichzeitige Verschwinden.
            if (w.Freight && l != null && !l.Rollt && (ZugEinfahrtAlt || l.Nachlauf < 0f)) _zdStandSichtbar++;
            // V6: hat die Kupplung den Waggon verschoben?
            if (w.Freight && Mathf.Abs(w.LeadF - w.RawLeadF) > 1e-4f) _zdKupplung++;

            // Wenden: der Waggon kehrt AUF SEINEM GLEIS um — gemessen an der
            // Kettenstelle (Fahrplan/Auslauf: LeadF, Pendeln: Step), getrennt nach
            // »an einem Fahrtende« (die Linie ist seit dem letzten Bild neu
            // abgefahren) und »mitten in der Fahrt« (das Pendeln). Daneben, nur
            // zur Auskunft, der Bildwinkel > 170° — der sieht auch eine Rampe, auf
            // der die Hoehe schneller steigt als die Zeile faellt.
            var pos = RailLifted(new Vector2(w.Col, w.Row), w.Lift);
            float prog = w.Freight || w.Auslauf ? w.LeadF : w.Step;
            // ⭐ bug-397 (F7): im Fahrmodell je Schritt fährt der Fahrplanwaggon auf der ROUTE;
            // seine Kettenstelle ist nur eine Projektion und läuft an Routenstücken ohne
            // Kette (DM_4 Linie 0, Spitzkehre bei (73,20)) scheinbar zurück. Gemessen wird
            // dann der Fahrtfortschritt (Takt/Fahrzeit).
            if (w.Freight && ZugFahrmodellNeu && _zugPw.TryGetValue(w, out float pwF)) prog = pwF;
            int starts = l?.Starts ?? 0;
            if (_zdVorher.TryGetValue(w, out var v))
            {
                var mv = pos - v.Pos;
                if (mv.Length() > 0.5f && v.Mv.Length() > 0.5f &&
                    mv.Normalized().Dot(v.Mv.Normalized()) < -0.985f) _zdWendenBild++;
                float dp = prog - v.Prog;
                if (Mathf.Abs(dp) > 1e-4f && Mathf.Abs(v.DProg) > 1e-4f && Mathf.Sign(dp) != Mathf.Sign(v.DProg))
                {
                    if (starts != v.Starts) _zdWendenEnde++;
                    else
                    {
                        _zdWendenMitte++;
                        if (_zdWendeWo.Length == 0)
                            _zdWendeWo = $"Linie {w.Line} Waggon {w.Index} bei ({w.Col:0.0},{w.Row:0.0}) " +
                                         $"Kettenstelle {v.Prog:0.00} -> {prog:0.00}, faze {l?.Faze}" +
                                         (w.Freight ? " (Fahrplan)" : " (ohne Fahrplan)");
                    }
                }
                _zdVorher[w] = (pos, mv.Length() > 0.5f ? mv : v.Mv, prog,
                                Mathf.Abs(dp) > 1e-4f ? dp : v.DProg, starts);
            }
            else _zdVorher[w] = (pos, Vector2.Zero, prog, 0f, starts);

            // Höhe: liegt der Waggon auf einem Rampenglied und hat trotzdem Lift 0?
            var pd = RailPathOf(w.Line);
            // ⭐ bug-397 (F7): die Fahrplanwaggons tragen die Höhe des ORIGINALS (Bezugszelle
            // 0x4C76C0 + Rampe 0x4C73C8), nicht mehr den Lift aus dem Gleisbild — die Probe
            // »Lift 0 auf Rampenglied« gilt dort nicht; der Vergleich steht im
            // --zug-einfahrt-check (»Hoehe gegen Gleisbild-Lift«).
            if (pd?.Lift != null && pd.Lift.Length > 0 && !(w.Freight && ZugFahrmodellNeu))
            {
                float f = w.Freight || w.Auslauf ? w.LeadF : w.Step;
                float soll = RailLiftAt(pd.Lift, f);
                if (Mathf.Abs(soll) > 0.5f)
                {
                    _zdRampenBilder++;
                    if (Mathf.Abs(w.Lift) < 0.01f)
                    {
                        _zdLift0++;
                        if (_zdLiftWo.Length == 0)
                            _zdLiftWo = $"Linie {w.Line} Waggon {w.Index} Glied {f:0.0} Soll {soll:0.0} px";
                    }
                }
            }

            // Einfahrt: steht der Waggon im Grundriss eines Endgebäudes seiner Linie?
            if (l != null)
                foreach (int slot in new[] { l.Bud1, l.Bud2 })
                {
                    var b = RailBuilding(slot);
                    if (b == null || b.Dead) continue;
                    int c = Mathf.RoundToInt(w.Col), rr = Mathf.RoundToInt(w.Row);
                    if (c < b.Col || c >= b.Col + Mathf.Max(1, b.FootW) ||
                        rr < b.Row || rr >= b.Row + Mathf.Max(1, b.FootH)) continue;
                    _zdImGrundriss++;
                    int wf0 = WaggonFach(w, out int rk, out int rn);
                    // ⚠⚠ bug-382: hier stand der Vergleich mit dem TUER-Fach
                    // (GebaeudeFachOriginal = Zeile + Tuer0 + 2) und »verdeckt« ab
                    // Koerperzeile >= Fach — die zwei falsch liegenden Zeilen waren per
                    // Konstruktion »darunter«, und der Pruefstand meldete BESTANDEN,
                    // waehrend der Waggon UEBER Dach und Halle lag. Gemessen wird jetzt,
                    // was gezeichnet wird: die Waggonpixel unter den KOERPERKACHELN, in
                    // unserer Ordnung gegen die des Originals (Fach i+2 vor Zeile i).
                    var (abUns, abOrig) = ZeAbZeile(w);
                    var dk = ZeDeckung(w, b, abUns, abOrig);
                    if (dk.Uns > 0) _zdVerdeckt++;
                    // ⚠ EINFAHREN heisst: auf den Andockschritten, also den letzten
                    // drei Routenpunkten am Ende DIESES Gebaeudes (die zwei ohne
                    // Gleisbild und der davor, Bericht §2.1). Mitten im Grundriss
                    // kann die Route senkrecht laufen (DM_4 Linie 7, Stueck 4/5) —
                    // dort liegt der Waggon auch im Original im SELBEN Fach wie das
                    // Gebaeude und wird danach eingereiht, also davor gezeichnet.
                    bool amEnde = rk >= 0 && (slot == l.Bud1 ? rk <= 2 : rk >= rn - 3);
                    if (!amEnde) continue;
                    _zdEinfahrBilder++;
                    if (dk.Orig > 0) _zdHinter++;          // im Original (teils) verdeckt
                    if (dk.Uns != dk.Orig)
                    {
                        _zdDavor++;                         // unsere Ordnung weicht ab
                        if (_zdDavorWo.Length == 0)
                            _zdDavorWo = $"Linie {w.Line} W{w.Index} Stueck {w.Piece} Zeile {rr} Fach {wf0}: " +
                                         $"verdeckt {100f * dk.Uns / Mathf.Max(1, dk.Gesamt):0} % statt " +
                                         $"{100f * dk.Orig / Mathf.Max(1, dk.Gesamt):0} % an Platz {b.Slot} Zeile {b.Row}" +
                                         (ZugfachAlt ? " (ueber allem gezeichnet)" : "");
                    }
                }
        }
        foreach (var k in new List<Wagon>(_zdVorher.Keys))
            if (!gesehen.Contains(k)) _zdVorher.Remove(k);

        // Abstand zweier aufeinanderfolgender Waggons derselben Fahrt (Original: >= 12 px).
        // ⭐ bug-395 (F3b): in der EBENE gemessen, mit Ort des kleinsten Paars und dem
        // Schirmmass zur Auskunft (Simulation/ZugEinfahrt.cs ZugAbstandBild).
        ZugAbstandBild(_zdAbstand);
    }

    private string ZugDemoZeile(bool schluss)
    {
        int fw = 0;
        foreach (var w in _wagons) if (w.Freight) fw++;
        var sb = new System.Text.StringBuilder("zug-demo-check: ");
        var schalter = new List<string>();
        if (Geisterwaggons) schalter.Add("--geisterwaggons");
        if (ZugfachAlt) schalter.Add("--zugfach-alt");
        if (ZugStehtAmBahnsteig) schalter.Add("--zug-steht-am-bahnsteig");
        if (ZugPendelt) schalter.Add("--zug-pendelt");
        if (ZugfarbeRoh) schalter.Add("--zugfarbe-roh");
        if (ZugGekuppelt) schalter.Add("--zug-gekuppelt");
        if (ZugEinfahrtAlt) schalter.Add("--zug-einfahrt-alt");
        if (GleisfachAlt) schalter.Add("--gleisfach-alt");
        if (ZugBogenSchirm) schalter.Add("--zug-bogen-schirm");
        if (ZugFeinlageAlt) schalter.Add("--zug-feinlage-alt");
        if (ZugOhneSichtprobe) schalter.Add("--zug-ohne-sichtprobe");
        if (schalter.Count > 0) sb.Append($"⚠ NULLMODELL {string.Join(" ", schalter)} | ");

        bool v1 = _zdGeistSichtbar == 0;
        // ⚠ bug-382: V2 verlangt jetzt, dass ueberhaupt etwas verdeckt wird (sonst prueft
        // es nichts) und dass unsere Ordnung Pixel fuer Pixel die des Originals ist;
        // V3, dass kein Nachlaeufer im Takt des Spitzenwaggons verschwindet.
        int gleichzeitig = ZeGleichzeitig();
        bool v2 = _zdDavor == 0 && _zdEinfahrBilder > 0 && _zdHinter > 0;
        bool v3 = _zdStandSichtbar == 0 && gleichzeitig == 0;
        bool v4 = fw == _wagons.Count && _zdWendenMitte == 0 && _zdLift0 == 0;
        bool v5 = _zdRoh == 0;
        // ⚠ bug-395: der Mindestabstand (Original 12 px, W1->W2 senkrecht) ist hier nur
        // AUSKUNFT — Kriterium ist er im --zug-einfahrt-check (F). Er haengt am Fahrmodell
        // (F7, nicht gebaut): unser Weg ist kuerzer als die Route bei gleicher Fahrzeit.
        bool v6 = _zdKupplung == 0;
        string Ok(bool b) => b ? "OK" : "ABWEICHUNG";

        sb.Append($"V1 geladen {WaggonsGeladen} (Geister uebersprungen {GeisterUebersprungen}), " +
                  $"Geister-Bilder sichtbar {_zdGeistSichtbar} {Ok(v1)}");
        sb.Append($" | V2 {_zdImGrundriss} Waggonbilder im Grundriss eines Endgebaeudes, davon " +
                  $"{_zdVerdeckt} von einer Koerperkachel (teils) verdeckt; EINFAHRT (letzte drei " +
                  $"Routenpunkte): {_zdEinfahrBilder} Bilder, im Original verdeckt {_zdHinter}, " +
                  $"unsere Ordnung abweichend {_zdDavor}" +
                  (_zdDavorWo.Length > 0 ? $" (erstes: {_zdDavorWo})" : "") +
                  $"; im Fach gezeichnet (letztes Bild) " +
                  $"{WaggonsImFach} {Ok(v2)}");
        sb.Append($" | V3 Fahrplanwaggons sichtbar, waehrend ihre Linie steht (ohne Nachlauf): {_zdStandSichtbar}, " +
                  $"Fahrten mit Nachlaeufer im Takt des Spitzenwaggons verschwunden: {gleichzeitig} {Ok(v3)}");
        sb.Append($" | V4 {fw} von {_wagons.Count} Waggons am Fahrplan, ausgelaufen und geloescht " +
                  $"{AuslaufGeloescht}, 180°-Wenden mitten in der Fahrt {_zdWendenMitte}" +
                  (_zdWendeWo.Length > 0 ? $" (erstes: {_zdWendeWo})" : "") +
                  $", an Fahrtenden {_zdWendenEnde} (Bildwinkel > 170° zur Auskunft: {_zdWendenBild}), Lift 0 auf Rampenglied {_zdLift0} von " +
                  $"{_zdRampenBilder} Rampenbildern" +
                  (_zdLiftWo.Length > 0 ? $" (erstes: {_zdLiftWo})" : "") + $" {Ok(v4)}");
        sb.Append($" | V5 roh gezeichnete Waggonbilder {_zdRoh} {Ok(v5)}");
        sb.Append($" | V6 von der Kupplung verschobene Waggonbilder {_zdKupplung}; Abstand " +
                  $"aufeinanderfolgender Waggons (Soll >= 12 px): {ZugAbstandText(_zdAbstand)} {Ok(v6)}");
        if (schluss)
            sb.Append($"\nzug-demo-check: bug-377 {(v1 && v2 && v3 && v4 && v5 && v6 ? "BESTANDEN" : "DURCHGEFALLEN")}" +
                      $" (V1 {Ok(v1)}, V2 {Ok(v2)}, V3 {Ok(v3)}, V4 {Ok(v4)}, V5 {Ok(v5)}, V6 {Ok(v6)})");
        return sb.ToString();
    }
}
