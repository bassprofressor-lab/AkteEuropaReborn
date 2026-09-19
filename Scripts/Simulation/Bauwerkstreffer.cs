namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>BRÜCKEN UND RAMPEN ZERSTÖREN</b> — der Zellzweig am Ende von Zasah
/// (<c>0x40D72F…0x40D9A3</c>, F <c>0x40D56A…</c>), gebaut am 13.09.2026.
///
/// <para>Seine Frage aus Kampagne 10: »brücken scheinen bei uns noch nicht
/// "Zerstörbar" oder?« — richtig, <c>BrueckeTrifft</c> hatte keinen Rufer. Seine
/// Entscheidung: »im vollen original style«. Die Lesung steht in
/// <c>berichte/brueckenzerstoerung-fable.md</c>.</para>
///
/// <para><b>GELESEN:</b></para>
/// <list type="number">
/// <item><b>Kein eigener Brückentreffer.</b> Jeder Zasah, der bis ans Ende
///   kommt, liest <c>sec20</c> der Trefferzelle: 100…199 → Brücke n = sec20−100,
///   200…249 → Rampe n = sec20−200. Nicht dorthin kommen Treffer, an denen das
///   Ziel stirbt (Einheit, Gebäude).</item>
/// <item><b>Schaden</b> = <c>((Rang+128)·Angriff) &gt;&gt; 7 − rand%5 + rand%5</c>
///   mit den Werten des Verursachers (Einheit: <c>+0x28</c>,
///   <c>+0x26 + 2·Zellhöhe</c>; Kennung 40000+a: Rang 0, Angriff a; Druckwelle
///   200/(m+1)), <b>eigener Wurf</b>.</item>
/// <item><b>Brücke</b> 500 TP, Stufe <c>(500−hp)/167</c> → Bild +18 je Stufe;
///   <c>s ≥ hp</c> → Abriss <c>0x4CB0A0</c>. <b>Rampe</b> 200 TP, Stufe
///   <c>(200−hp)/67</c> → Bild +8; <c>s ≥ hp</c> → <c>0x4CBAB0</c>.</item>
/// <item><b>Abriss Brücke:</b> sec20 := 0; Einheiten auf der Fahrbahn werden
///   GELÖSCHT (Zasah 40500 → <c>0x410E60</c>, kein Wrack, keine Explosion);
///   Köpfe rau (<c>0xFFFD</c>), Wasserzeilen Wasser (<c>0xFFFC</c>); Wasser-
///   kacheln Zufall <c>|rand| &amp; 7</c> (senkrecht) bzw. <c>rand%6 + 1</c>
///   (waagerecht); Köpfe Trümmer <c>10114 + 120·B</c> (senkrecht) bzw.
///   <c>10108 + 120·B</c> (waagerecht), B = Feld[0][0]/120; Platz frei. Kein
///   Klang.</item>
/// <item><b>Abriss Rampe:</b> Kachel <c>10747 + v</c>, Platz frei, sec20 := 0,
///   Zasah 40500 auf die Zelle, <c>sec6 := 0xFFFD</c>.</item>
/// <item><b>Karten- und Pionierbrücken sind EINE Tafel</b> (100 Plätze,
///   <c>sec20 = 100 + Platz</c>), Rampen ebenso (50, <c>200 + Platz</c>).</item>
/// </list>
///
/// <para>⚠ <b>UNSERE SETZUNGEN, benannt:</b></para>
/// <list type="bullet">
/// <item>Die <b>Trefferzelle</b> ist die Zelle des getroffenen Ziels bzw. des
///   Einschlags. Das Original nimmt beim Infanterieschuss auf eine Einheit die
///   ZULETZT gesetzte Zelle (Nebenbefund 3.2) — nicht nachgebaut.</item>
/// <item>Beim Abriss NICHT gebaut: die Lichtquelle (4, 80 Takte), die
///   Bodenmarken Art 15 und die KI-Sektorkanten (<c>0x41CAD0</c>) — die
///   Bodenmarken sind ungelesen, Lichtquelle und Sektorkanten haben wir als
///   Mechanik noch nicht. (Die Zellhöhe der Fahrbahn wird zurückgesetzt.)</item>
/// <item>Das Geländer einer beschädigten KARTENbrücke zeichnen wir flach im
///   Bodendurchgang mit, statt im Zeilenfach — die Kacheln sind dieselben.</item>
/// </list>
///
/// <para>Gegenschalter <c>--bauwerke-unzerstoerbar</c>. Prüfstand
/// <c>--bruecke-treffer-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--bauwerke-unzerstoerbar</c> — der Stand vor dem 13.09.2026.</summary>
    public static bool BauwerkeUnzerstoerbar;

    /// <summary>Zellen, deren Kachel ein Abriss ersetzt hat (Wasser, Trümmer):
    /// Zellschlüssel → Kachelcode.</summary>
    private readonly Dictionary<int, int> _bauwerkKachel = new();

    /// <summary>Zellen einer KARTENbrücke, die nicht mehr im gebackenen Bild
    /// stehen dürfen (Schadensstufe &gt; 0 oder abgerissen) — ihr Geländer wird
    /// im Zeilenfach übersprungen.</summary>
    private readonly HashSet<int> _bauwerkUeberdeckt = new();

    public bool BauwerkUeberdeckt(int c, int r) => _bauwerkUeberdeckt.Contains(c * 1024 + r);

    /// <summary>
    /// ⭐⭐⭐ 20.09.2026 — <b>ZELLEN, DIE EIN GESETZTES GEBAEUDE ZUDECKT.</b>
    ///
    /// <para>Eine zweite Menge neben <see cref="_bauwerkUeberdeckt"/>, und mit
    /// Absicht getrennt: jene wird je Bruecke neu aufgebaut und geleert
    /// (siehe Zeile 106), diese waechst nur. Ein Gebaeude, das einmal gesetzt
    /// ist, deckt seine Musterzellen fuer immer zu — im Original, weil das
    /// Kachelwort ERSETZT wurde und es kein Zurueck gibt.</para>
    ///
    /// <para>⚠ Auch eine RUINE deckt weiter zu. Das Original ersetzt das
    /// Kachelwort beim Setzen, nicht beim Zerstoeren; was darunter lag, ist
    /// weg. Darum wird hier nie etwas entfernt.</para>
    ///
    /// <para>Gemeldet als »man sieht noch diese komische bodengrafik
    /// durchscheinen« beim Minenbau in K17. Gegenschalter
    /// <c>--vorkommen-unter-mine-alt</c>.</para></summary>
    private readonly HashSet<int> _gebaeudeUeberdeckt = new();

    /// <summary><c>--vorkommen-unter-mine-alt</c> — der Stand von vor dem
    /// 20.09.2026: die Objektebene malt auch ueber ein gesetztes Gebaeude.</summary>
    public static bool VorkommenUnterMineAlt;

    /// <summary>
    /// Wie viele Zellen WIRKSAM zugedeckt sind — die Zahl fuer den Pruefstand.
    ///
    /// <para>⚠ Es ist ausdruecklich nicht die Groesse der Menge. Der erste
    /// Anlauf meldete sie, und damit gaben beide A/B-Laeufe dieselbe 30: der
    /// Gegenschalter betrifft die ABFRAGE, nicht das Fuellen. Eine Zahl, die
    /// unter dem Nullmodell gleich bleibt, ist keine Messung — gezaehlt wird
    /// darum, was <see cref="GebaeudeUeberdeckt"/> wirklich antwortet.</para></summary>
    public int GebaeudeDeckzellen
    {
        get
        {
            int n = 0;
            foreach (int key in _gebaeudeUeberdeckt)
                if (GebaeudeUeberdeckt(key / 1024, key % 1024)) n++;
            return n;
        }
    }

    public void GebaeudeDecktZu(int c, int r) => _gebaeudeUeberdeckt.Add(c * 1024 + r);

    /// <summary>Deckt ein gesetztes Gebaeude diese Zelle zu?</summary>
    public bool GebaeudeUeberdeckt(int c, int r)
        => !VorkommenUnterMineAlt && _gebaeudeUeberdeckt.Contains(c * 1024 + r);

    public int BauwerkTreffer_, BrueckenAbgerissen, RampenAbgerissen;

    /// <summary>Der erste freie Brückenplatz über Karten- UND Pionierbrücken.</summary>
    private int FreierStegPlatz()
    {
        for (int p = 0; p < StegPlaetze; p++)
            if (!_stege.Exists(s => s.Slot == p)) return p;
        return -1;
    }

    private int FreierRampenPlatz()
    {
        for (int p = 0; p < RampenPlaetze; p++)
            if (!_moleSaetze.Exists(r => r.Slot == p)) return p;
        return -1;
    }

    /// <summary>Die Zellen einer Kartenbrücke aus ihrer Ecke, Achse und Länge —
    /// Streifen s = 0…2 quer, Position p = 0…k+1 längs.</summary>
    private static IEnumerable<(Vector2I Zelle, int S, int P)> SatzZellen(Steg st)
    {
        var e = BrueckenEcke(st);
        bool senk = IstSenkrecht(st);
        for (int s = 0; s < 3; s++)
            for (int p = 0; p < st.Wasser + 2; p++)
                yield return (senk ? new Vector2I(e.X + s, e.Y + p) : new Vector2I(e.X + p, e.Y + s), s, p);
    }

    /// <summary>sec17 der Karte als Stege in die gemeinsame Tafel.</summary>
    private void KartenStegeAnlegen(IEnumerable<(int Slot, int Col, int Row, int Dir, int Len, int Hp, int[] Feld)> saetze)
    {
        _stege.RemoveAll(s => s.Karte);
        _bauwerkKachel.Clear();
        _bauwerkUeberdeckt.Clear();
        foreach (var b in saetze)
        {
            var st = new Steg
            {
                Karte = true, Slot = b.Slot, Col = b.Col, Row = b.Row,
                KartenSenkrecht = b.Dir == 1, Wasser = Mathf.Clamp(b.Len, 1, 3),
                Tp = b.Hp, Feld = b.Feld,
            };
            foreach (var (z, s, _) in SatzZellen(st))
                st.Zellen.Add((z, s == 1, Simulation.NavGrid.Ground.Free));
            _stege.Add(st);
        }
    }

    /// <summary>
    /// <b>Der Zellzweig</b> — nach einem Treffer, der die Zelle erreicht.
    /// <paramref name="rang"/>/<paramref name="angriff"/> sind die Werte des
    /// Verursachers.
    /// </summary>
    private void BauwerkTreffer(int c, int r, int rang, int angriff)
    {
        if (BauwerkeUnzerstoerbar) return;
        if (!_rampen.TryGetValue(c * 1024 + r, out int lage) || lage < 100 || lage >= 250) return;
        // s = ((Rang+128)·Angriff) >> 7 − rand%5 + rand%5 — eigener Wurf (0x40D820).
        int s = ((rang + 128) * angriff >> 7) - Simulation.Determinism.Roll(5)
                                               + Simulation.Determinism.Roll(5);
        BauwerkTreffer_++;
        if (lage < 200)
        {
            var st = _stege.Find(x => x.Slot == lage - 100);
            if (st == null) return;
            if (s >= st.Tp) { BrueckeAbreissen(st); return; }
            st.Tp -= s;
            if (st.Karte && st.Tp < BrueckeTp)
                foreach (var (z, _, _) in st.Zellen) _bauwerkUeberdeckt.Add(z.X * 1024 + z.Y);
        }
        else
        {
            var rp = _moleSaetze.Find(x => x.Slot == lage - 200);
            if (rp == null) return;
            if (s >= rp.Tp) { RampeAbreissen(rp); return; }
            rp.Tp -= s;
        }
        QueueRedraw();
    }

    /// <summary>Einheiten einer Zelle löschen — Zasah 40500 nimmt den Sofortweg
    /// <c>0x410E60</c>: kein Wrack, keine Explosion, kein Todesprotokoll.</summary>
    private int ZelleLeeren(int c, int r)
    {
        int n = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (o.Dead || o.IsProp || o.IsBuilding) continue;
            bool drauf = o.Infantry >= 0 ? o.Col == c && o.Row == r : _nav?.OccupantAt(c, r) == i;
            if (!drauf) continue;
            // Gottmodus: eine eigene Einheit bleibt stehen (unsere Schummelzutat).
            if (GottModusFuer(o)) { GottModusTodVerhindert++; continue; }
            o.Verbraucht = true;
            o.Hp = 0; o.Dead = true; o.DeadTime = 0; o.Path = null; o.Target = -1;
            _nav?.ClearOccupant(o.Col, o.Row, i);
            n++;
            GD.Print($"bruecke: {LabelOf(o)} auf ({c},{r}) mit dem Bauwerk verschwunden");
        }
        return n;
    }

    /// <summary><c>0x4CB0A0</c>, Erase bridge — siehe Kopf, Punkt 4.</summary>
    private void BrueckeAbreissen(Steg st)
    {
        bool senk = IstSenkrecht(st);
        int bildsatz = (st.Karte ? st.Feld[0] : 120 * BrueckeBildsatz) / 120;
        int truemmer = (senk ? 10114 : 10108) + 120 * bildsatz;
        int weg = 0;
        var e = BrueckenEcke(st);
        // Höhe(Fahrbahn über Wasser) := Höhe(Kopfzelle derselben Fahrbahn) —
        // @0x4CB320/0x4CB333: das nimmt das +1 des Brückenbaus zurück. Ohne das
        // säße die Wasserkachel eine Höhenstufe (15 Punkte) zu hoch.
        var kopfZelle = senk ? new Vector2I(e.X + 1, e.Y) : new Vector2I(e.X, e.Y + 1);
        int kopfHoehe = ElevOf(kopfZelle.X, kopfZelle.Y);
        foreach (var (z, s, p) in SatzZellen(st))
        {
            if (s == 1 && p > 0 && p < st.Wasser + 1) _elevLookup[(z.X, z.Y)] = kopfHoehe;
            int key = z.X * 1024 + z.Y;
            _rampen.Remove(key);                                             // sec20 := 0
            if (s == 1) weg += ZelleLeeren(z.X, z.Y);                        // nur die Fahrbahn
            bool kopf = p == 0 || p == st.Wasser + 1;
            _nav?.BrueckeLoesen(z.X, z.Y, kopf ? Simulation.NavGrid.Ground.Rough
                                               : Simulation.NavGrid.Ground.Water);
            _bauwerkUeberdeckt.Add(key);
            if (kopf)
            {
                // senkrecht: Kopfzeile y0 -> T+s, y0+k+1 -> T+3+s;
                // waagerecht: Kopfspalte x0 -> T+s, x0+k+1 -> T+3+s.
                _bauwerkKachel[key] = truemmer + (p == 0 ? 0 : 3) + s;
            }
            else
            {
                int wurf = senk ? Mathf.Abs(Simulation.Determinism.Roll(0x8000)) & 7
                                : Simulation.Determinism.Roll(6) + 1;
                _bauwerkKachel[key] = wurf;
            }
        }
        _stege.Remove(st);                                                   // +0x12 := 0
        BrueckenAbgerissen++;
        GD.Print($"bruecke: Bruecke Platz {st.Slot} ({e.X},{e.Y}) {(senk ? "senkrecht" : "waagerecht")} "
               + $"eingestuerzt — {st.Zellen.Count} Zellen, {weg} Einheiten verschwunden, Truemmer {truemmer}");
        QueueRedraw();
    }

    /// <summary><c>0x4CBAB0</c>, Destroy ramp — siehe Kopf, Punkt 5.</summary>
    private void RampeAbreissen(Rampe rp)
    {
        int key = rp.Col * 1024 + rp.Row;
        _bauwerkKachel[key] = Import.MapBaker.RampenKachelBasis + 24 + rp.Bild;    // 10747 + v
        _moleSaetze.Remove(rp);
        _rampen.Remove(key);
        _rampenKachel.Remove(key);
        ZelleLeeren(rp.Col, rp.Row);                                              // Zasah 40500
        _nav?.BrueckeLoesen(rp.Col, rp.Row, Simulation.NavGrid.Ground.Rough);    // 0xFFFD
        RampenAbgerissen++;
        GD.Print($"mole: Rampe Platz {rp.Slot} ({rp.Col},{rp.Row}) zerstoert — Truemmer {10747 + rp.Bild}");
        QueueRedraw();
    }

    /// <summary>
    /// <c>--bruecke-treffer-check</c> — der Prüfstand aus §4.6 der Lesung, auf
    /// der ersten Kartenbrücke: (a) drei Einschläge à 200 auf eine leere
    /// Geländerzelle über den echten Einschlagweg (<c>ZellEinschlag</c>) → Stufe 1,
    /// Stufe 2, Abriss; (b) danach Köpfe rau, Wasser Wasser, sec20 leer,
    /// Trümmerkacheln; (c) eine Einheit auf der Fahrbahn ist WEG, ohne Wrack;
    /// (d) die Regel <c>bridge</c> sieht 0; (e) die Kacheln liegen im Streifen;
    /// (f) ein Treffer ohne Schützen (Sprengungsnachbar 15) auf eine zweite
    /// Brücke zieht ab. Nullmodell: <c>--bauwerke-unzerstoerbar</c>.
    /// </summary>
    public string BrueckeTrefferCheck(bool nurBisStufe1 = false)
    {
        var sb = new System.Text.StringBuilder("bruecke-treffer-check\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (BauwerkeUnzerstoerbar) sb.AppendLine("  ⚠ NULLMODELL --bauwerke-unzerstoerbar: hier MUSS alles stehen bleiben");
        var st = _stege.Find(s => s.Karte);
        if (st == null || _nav == null) return sb.Append("  keine Kartenbruecke — ungeprueft").ToString();
        int slot = st.Slot;
        bool senk = IstSenkrecht(st);
        var zellen = new List<(Vector2I Zelle, int S, int P)>(SatzZellen(st));
        var gelaender = zellen.Find(z => z.S == 0 && z.P == 1).Zelle;
        var fahrbahn = zellen.Find(z => z.S == 1 && z.P == 1).Zelle;
        sb.AppendLine($"  Kartenbruecke Platz {slot}: Ecke ({st.Col},{st.Row}), {(senk ? "senkrecht" : "waagerecht")}, "
                    + $"{st.Wasser} Wasserzellen, TP {st.Tp}, Bildsatz {st.Feld[0] / 120}; Gelaender {gelaender}, Fahrbahn {fahrbahn}");

        // (e) die Kacheln im Streifen
        int fehlt = 0;
        foreach (var (z, _, _) in zellen)
            for (int stufe = 0; stufe < 3; stufe++)
                if (StreifenKachel(10000 + st.Feld[FeldIndex(zellen.Find(q => q.Zelle == z).S, zellen.Find(q => q.Zelle == z).P)] + 18 * stufe) == null) fehlt++;
        int truemmer = (senk ? 10114 : 10108) + 120 * (st.Feld[0] / 120);
        for (int t = 0; t < 6; t++) if (StreifenKachel(truemmer + t) == null) fehlt++;
        for (int w = 0; w < 8; w++) if (StreifenKachel(w) == null) fehlt++;
        Soll(fehlt == 0, $"Stufen-, Truemmer- und Wasserkacheln im Streifen ({fehlt} fehlen — sonst Karten neu backen)");

        // (c) eine Einheit auf die Fahrbahn
        int opfer = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (o.IsBuilding || o.IsProp || o.Dead || !o.Mobile || o.Infantry >= 0) continue;
            if (o.Move != Simulation.NavGrid.MoveClass.Vehicle || Untergestellt(o)) continue;
            _nav.ClearOccupant(o.Col, o.Row, i);
            o.Col = fahrbahn.X; o.Row = fahrbahn.Y; o.Path = null;
            o.Pos = BodyCenterAt(o, o.Col, o.Row);
            _nav.SetOccupant(o.Col, o.Row, i);
            opfer = i;
            sb.AppendLine($"  ⚠ EINGRIFF: {LabelOf(o)} auf die Fahrbahn {fahrbahn} gestellt");
            break;
        }

        int Stufe() => Mathf.Clamp((BrueckeTp - st.Tp) / 167, 0, 2);
        ZellEinschlag(-1, gelaender.X, gelaender.Y, 200, 0);
        sb.AppendLine($"  Einschlag 1 (200) aufs Gelaender: TP {st.Tp}");
        Soll(BauwerkeUnzerstoerbar ? st.Tp == BrueckeTp : Stufe() == 1 && BauwerkUeberdeckt(gelaender.X, gelaender.Y),
             $"nach Einschlag 1 Stufe {Stufe()} (erwartet 1), Gelaender aus dem gebackenen Bild genommen");
        if (nurBisStufe1) return sb.ToString();
        ZellEinschlag(-1, gelaender.X, gelaender.Y, 200, 0);
        Soll(BauwerkeUnzerstoerbar || Stufe() == 2 && _stege.Contains(st), $"nach Einschlag 2 Stufe {Stufe()} (erwartet 2), TP {st.Tp}");
        ZellEinschlag(-1, gelaender.X, gelaender.Y, 200, 0);
        bool weg = !_stege.Contains(st);
        Soll(weg, $"nach Einschlag 3 eingestuerzt (Platz {slot} frei: {weg})");

        // (b) Gelaende, Lagentafel, Kacheln
        int rau = 0, wasser = 0, lage = 0, kopfTr = 0, wasserK = 0;
        foreach (var (z, s, p) in zellen)
        {
            bool kopf = p == 0 || p == st.Wasser + 1;
            var g = _nav.GroundAt(z.X, z.Y);
            if (kopf && g == Simulation.NavGrid.Ground.Rough) rau++;
            if (!kopf && g == Simulation.NavGrid.Ground.Water) wasser++;
            if (_rampen.ContainsKey(z.X * 1024 + z.Y)) lage++;
            if (_bauwerkKachel.TryGetValue(z.X * 1024 + z.Y, out int k))
            {
                if (kopf && k == truemmer + (p == 0 ? 0 : 3) + s) kopfTr++;
                if (!kopf && k is >= 0 and <= 7) wasserK++;
            }
        }
        Soll(rau == 6 && wasser == 3 * st.Wasser, $"Gelaende: {rau}/6 Kopfzellen rau, {wasser}/{3 * st.Wasser} Wasser");
        Soll(lage == 0, $"sec20 geraeumt ({lage} Zellen tragen noch 100+)");
        Soll(kopfTr == 6 && wasserK == 3 * st.Wasser,
             $"Kacheln: {kopfTr}/6 Truemmer ab {truemmer}, {wasserK}/{3 * st.Wasser} Wasserkacheln 0..{(senk ? 7 : 6)}");
        if (opfer >= 0)
        {
            var o = _entities[opfer];
            Soll(o.Dead && o.Verbraucht, $"Einheit auf der Fahrbahn verschwunden ohne Wrack (tot {o.Dead}, verbraucht {o.Verbraucht})");
        }
        Soll(BrueckeFeld0(slot) == 0, $"Regel bridge({slot}) sieht {BrueckeFeld0(slot)} (erwartet 0)");

        // (f) ein Treffer ohne Schuetzen — der Nachbarschaden einer sterbenden Einheit
        var st2 = _stege.Find(s => s.Karte);
        if (st2 != null)
        {
            var z2 = new List<(Vector2I Zelle, int S, int P)>(SatzZellen(st2)).Find(z => z.S == 2 && z.P == 1).Zelle;
            int tp0 = st2.Tp;
            SkripttrefferZelle(z2.X, z2.Y, 15, "bruecke-treffer-check");
            Soll(st2.Tp < tp0, $"Sprengungsnachbar (15) auf Bruecke Platz {st2.Slot}: TP {tp0} -> {st2.Tp}");
        }
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    /// <summary>Für das Bild: dieselbe Brücke mit zwei weiteren Einschlägen einstürzen lassen.</summary>
    public string BrueckeTrefferCheckRest()
    {
        var st = _stege.Find(s => s.Karte);
        if (st == null) return "bruecke-bild: keine Kartenbruecke mehr";
        var g = new List<(Vector2I Zelle, int S, int P)>(SatzZellen(st)).Find(z => z.S == 0 && z.P == 1).Zelle;
        int vor = BrueckenAbgerissen;
        ZellEinschlag(-1, g.X, g.Y, 200, 0);
        ZellEinschlag(-1, g.X, g.Y, 200, 0);
        return $"bruecke-bild: nach zwei weiteren Einschlaegen eingestuerzt: {BrueckenAbgerissen > vor}";
    }

    /// <summary>Für das Bild: die Zelle der ersten Kartenbrücke.</summary>
    public Vector2? ErsteKartenbrueckeMitte()
    {
        var st = _stege.Find(s => s.Karte);
        if (st == null) return null;
        var e = BrueckenEcke(st);
        return IstSenkrecht(st) ? CellCenter(e.X + 1, e.Y + 1) : CellCenter(e.X + 1, e.Y + 1);
    }

    /// <summary>Die Kacheln, die ein Abriss hinterlassen hat — im Bodendurchgang.</summary>
    private void ZeichneBauwerkKacheln(Texture2D tex)
    {
        foreach (var (key, code) in _bauwerkKachel)
        {
            if (StreifenKachel(code) is not { } k) continue;
            int c = key / 1024, r = key % 1024;
            var ziel = CellRect(_ox, _oy, c, r, ElevOf(c, r));
            DrawTextureRectRegion(tex,
                new Rect2(new Vector2(ziel.Position.X, ziel.Position.Y + Import.MapBaker.BlitAnchor + k.YOff),
                          k.Feld.Size), k.Feld);
        }
    }
}
