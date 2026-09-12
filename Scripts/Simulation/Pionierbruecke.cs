namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE BRÜCKE DES PIONIERS — sec17, »Brücke bauen« (Menüzeile 0x0D).</b>
/// Gebaut am 13.09.2026, bug-245.
///
/// <para><b>Seine Ansage</b> beim Erreichen von Kampagne 9: »jetzt brauch ich
/// bloss noch den brücken bau + brücke sichtbar, dann läuft die mission
/// relativ entspannt«. Sie war das letzte fehlende Stück des Pioniers — ihre
/// Geometrie stand in zwei früheren Berichten ausdrücklich als UNGELESEN.</para>
///
/// <para><b>GELESEN</b> (berichte/pionier-bruecke-fable.md, beide EXE):</para>
/// <list type="number">
/// <item><b>Die Länge.</b> Der Zähler startet bei 1 und bricht bei
///   <c>cmp cl,5</c> ab — das Gegenufer muss in <b>1…4 Schritten</b> liegen,
///   also höchstens <b>3 Wasserzellen</b>; die Brücke überspannt
///   <b>k + 2 Zellen</b>. ⭐ Nullmodell aus den Daten: alle <b>110 Brücken</b>
///   der Originalkarten haben k ∈ {1,2,3}, keine einzige 4. Ein breiterer
///   Fluss wird <b>still</b> verworfen — das Original hat dafür nicht einmal
///   einen Text.</item>
/// <item><b>Die Form:</b> <b>3 breit × (k+2) lang</b>. Nur der MITTELstreifen
///   wird befahrbar (<c>0xFFFE</c>), die beiden Randstreifen sind das Geländer
///   und werden <b>gesperrt</b> (<c>0xFFFF</c>). Alle 3·(k+2) Zellen bekommen
///   <c>sec20 = 100 + i</c> — und damit genau das, was unsere Lagentafel seit
///   jeher »Lage ≥ 100, praktisch das Brückengeländer« nennt
///   (<see cref="RampeBeladen"/>): <b>beladen ja, entladen nein</b>.</item>
/// <item><b>Beim Setzen stirbt alles auf den Kacheln</b> —
///   <c>Zasah(40200, …)</c> je Deckzelle, 18 Rufstellen aus <c>0x4CC280</c>.
///   Die 40200 ist kein Einheitengriff, sondern die Verursacherkennung »die
///   Brücke selbst«.</item>
/// <item><b>Die vier Richtungsfälle</b> (Eckcode 3/12/10/5 → Lauf
///   −y/+y/+x/−x): die Startzelle des Satzes ist immer die OBERE LINKE Ecke —
///   <c>(x−1, y−k−1)</c>, <c>(x−1, y)</c>, <c>(x, y−1)</c>,
///   <c>(x−k−1, y−1)</c>. Genau das leistet hier
///   <see cref="BrueckenZellen"/> mit Streifen −1…+1.</item>
/// <item><b>Die Kachel</b> ist
///   <c>10000 + 120·Bildsatz + 54·Variante + 18·Stufe + Lage</c>; Lage
///   waagerecht <c>3·Streifen + Pos</c> (0…8), senkrecht
///   <c>9 + 3·Streifen + Pos</c> (9…17), Pos 0/1/2 = Anfang/Mitte/Ende.
///   ⭐ Der <b>Bildsatz</b> ist das fünfte Argument von <c>0x4CC280</c>, und
///   der Pionier übergibt <b>1</b> — den <b>Holzsteg</b> 10120…10239.</item>
/// <item><b>Der Pionier steht am Ende AUF der Brücke:</b> seine Zelle wird die
///   Kopfzelle der Fahrbahn. Verbraucht wird er trotzdem — wie bei der Mole
///   verarbeitet er seine eigenen Bestandteile (kein Wrack, kein
///   Todesprotokoll, <c>Entity.Verbraucht</c>).</item>
/// <item><b>Trefferpunkte 500</b>, und <b>kein Bautakt</b> — die Brücke steht
///   im selben Takt. Die »Bauphasen« des Zeichners sind Schadensstufen
///   <c>(500 − TP)/167</c>.</item>
/// </list>
///
/// <para>⚠ <b>UNSERE SETZUNGEN, benannt:</b></para>
/// <list type="bullet">
/// <item><b>Die Uferkante statt der Eckhöhen.</b> Dieselbe Übersetzung wie bei
///   der Mole (<see cref="RampenRichtung"/>) und aus demselben Grund: das
///   Original liest ein Eckraster (sec2), das wir nicht führen; an den 90
///   echten Rampen gemessen traf die Eckregel <b>1 von 90</b>, die Uferkante
///   <b>69 von 71</b>. Die Brücke benutzt dieselbe Messung, aber IHRE
///   Richtungstafel — <c>{3,12,10,5}</c> statt <c>{5,3,10,12}</c>, also
///   <b>oben, unten, rechts, links</b>.</item>
/// <item><b>Wasser und Gegenufer</b> fragen wir am Geländeraster ab; das
///   Original erkennt beides über die Eckhöhen. Dieselbe Aussage, anderes
///   Raster.</item>
/// <item>Die <b>Zellhöhe +1</b> der Fahrbahn über dem Wasser lassen wir weg —
///   unsere Höhe steht je Zelle und wird vom Zeichner anders benutzt. Die
///   Brücke liegt dadurch flach auf; sichtbar ist das nur im Vergleich.</item>
/// </list>
///
/// <para>Gegenschalter <c>--bruecke-aus</c>. Prüfstand
/// <c>--bruecke-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Unsere Auftragsnummer, in der Reihe von <c>OrderMole</c> = 8,
    /// <c>OrderAusbessern</c> = 9.</summary>
    public const int OrderBruecke = 10;

    /// <summary>Die Bauart, die im Pionier steht, während er hinfährt —
    /// neben 2 (Mole) und 4 (Ausbessern).</summary>
    private const int BauartBruecke = 5;

    /// <summary><c>--bruecke-aus</c> — der Stand vor dem 13.09.2026: »Brücke
    /// bauen« sagt beim Druck nur, dass ihre Geometrie ungelesen ist. Das
    /// Nullmodell zu bug-245.</summary>
    public static bool BrueckeAus;

    /// <summary>Höchstens drei Wasserzellen — der Zähler des Originals bricht
    /// bei <c>cmp cl,5</c> ab, und alle 110 Kartenbrücken bleiben darunter.</summary>
    private const int BrueckeMaxWasser = 3;

    /// <summary>Die Trefferpunkte einer frischen Brücke (<c>+0x16</c>).
    /// ⚠ KEIN Bauzähler — siehe Kopf, Punkt 7.</summary>
    private const int BrueckeTp = 500;

    /// <summary>sec17 fasst 100 Brückensätze.</summary>
    private const int StegPlaetze = 100;

    /// <summary>Der Bildsatz, den der Pionier übergibt: 1 = Holzsteg.</summary>
    private const int BrueckeBildsatz = 1;

    /// <summary>Eine gebaute Brücke.</summary>
    public sealed class Steg
    {
        /// <summary>Die Kopfzelle auf der Seite des Pioniers.</summary>
        public int Col, Row;
        /// <summary>0 = oben, 1 = unten, 2 = rechts, 3 = links — die
        /// Laufrichtung über das Wasser.</summary>
        public int Richtung;
        /// <summary>Wie viele WASSERzellen überspannt werden (1…3). Die Brücke
        /// ist <c>k + 2</c> Zellen lang.</summary>
        public int Wasser;
        /// <summary>Die Zufallsvariante des Bildes, 0 oder 1.</summary>
        public int Variante;
        public int Tp = BrueckeTp;
        /// <summary>Alle Zellen des Satzes — Fahrbahn und Geländer, mit dem
        /// Untergrund, der vorher dort stand.</summary>
        public readonly List<(Vector2I Zelle, bool Mitte, Simulation.NavGrid.Ground Alt)>
            Zellen = new();
    }

    private readonly List<Steg> _stege = new();
    public int StegeZahl => _stege.Count;

    public int BrueckeGebaut, BrueckeVerworfen;
    public string BrueckeNote = "";

    /// <summary>Die Richtungstafel der BRÜCKE: <c>{3, 12, 10, 5}</c> → 0…3.
    /// ⚠ Die Rampe benutzt <c>{5, 3, 10, 12}</c> — wer beide über eine Tafel
    /// baut, dreht eine von beiden. Bei uns heisst das:
    /// 0 = oben, 1 = unten, 2 = rechts, 3 = links.</summary>
    private static readonly Vector2I[] BrueckenLauf =
        { new(0, -1), new(0, 1), new(1, 0), new(-1, 0) };

    /// <summary>Die Richtung, in die eine Brücke von dieser Zelle aus liefe —
    /// die Seite, an der das Wasser liegt. Siehe Kopf: dieselbe Übersetzung
    /// wie bei der Mole, nur mit der Richtungstafel der Brücke.</summary>
    private int BrueckenRichtung(int col, int row)
    {
        if (_nav == null) return -1;
        int seite = -1;
        for (int d = 0; d < 4; d++)
        {
            int c = col + BrueckenLauf[d].X, r = row + BrueckenLauf[d].Y;
            if (!_nav.InBounds(c, r)) continue;
            if (_nav.GroundAt(c, r) != Simulation.NavGrid.Ground.Water) continue;
            if (seite >= 0) return -1;        // zwei Ufer: keine eindeutige Richtung
            seite = d;
        }
        return seite;
    }

    /// <summary>
    /// <b>Wie weit ist das Gegenufer?</b> — der Zähler aus <c>0x4CD900</c>.
    ///
    /// <para>Gezählt werden die WASSERzellen in Laufrichtung. Steht nach
    /// höchstens <see cref="BrueckeMaxWasser"/> davon wieder Land, geht die
    /// Brücke; sonst nicht.</para>
    /// <returns>Die Zahl der Wasserzellen (1…3), oder −1.</returns></summary>
    private int BrueckenWeite(int col, int row, int richtung)
    {
        if (_nav == null || richtung is < 0 or > 3) return -1;
        var lauf = BrueckenLauf[richtung];
        for (int k = 1; k <= BrueckeMaxWasser + 1; k++)
        {
            int c = col + lauf.X * k, r = row + lauf.Y * k;
            if (!_nav.InBounds(c, r)) return -1;
            if (_nav.GroundAt(c, r) == Simulation.NavGrid.Ground.Water) continue;
            // Land erreicht: k−1 Wasserzellen lagen dazwischen.
            int wasser = k - 1;
            return wasser is >= 1 and <= BrueckeMaxWasser ? wasser : -1;
        }
        return -1;                            // zu breit — still verworfen
    }

    /// <summary>Der Steg auf dieser Zelle, oder <c>null</c>.</summary>
    public Steg? StegAn(int col, int row)
    {
        foreach (var s in _stege)
            foreach (var (z, _, _) in s.Zellen)
                if (z.X == col && z.Y == row) return s;
        return null;
    }

    /// <summary>Die Zellen, die eine Brücke von hier aus belegen würde — drei
    /// Streifen quer zur Laufrichtung, je <c>k + 2</c> lang. Der MITTLERE ist
    /// die Fahrbahn, die beiden anderen sind Geländer.</summary>
    private static List<(Vector2I Zelle, bool Mitte)> BrueckenZellen(
        int col, int row, int richtung, int wasser)
    {
        var aus = new List<(Vector2I, bool)>();
        var lauf = BrueckenLauf[richtung];
        var quer = new Vector2I(lauf.Y, lauf.X);        // 90 Grad gedreht
        for (int s = -1; s <= 1; s++)
            for (int p = 0; p < wasser + 2; p++)
                aus.Add((new Vector2I(col + lauf.X * p + quer.X * s,
                                      row + lauf.Y * p + quer.Y * s), s == 0));
        return aus;
    }

    /// <summary><b>Darf hier eine Brücke entstehen?</b> — Startzelle rau (oder
    /// nur ich darauf), eine eindeutige Uferrichtung, ein Gegenufer in
    /// Reichweite, jede Satzzelle auf der Karte und noch kein Steg darauf.
    ///
    /// <para>⚠ Wie bei der Mole gibt es zwei Fassungen der Frage: die VORSCHAU
    /// (<paramref name="idx"/> = −1, die Zelle muss leer sein) und die
    /// BAUPRÜFUNG (<paramref name="idx"/> = der Pionier, der selbst darauf
    /// steht). Genau diese Unterscheidung hat am 12.09. einen halben Abend
    /// gekostet — sie steht hier von Anfang an.</para></summary>
    public bool BrueckePlatzOk(int col, int row, int idx = -1)
    {
        if (BrueckeAus || _nav == null || !_nav.InBounds(col, row)) return false;
        if (_nav.GroundAt(col, row) != Simulation.NavGrid.Ground.Rough) return false;
        int wer = _nav.OccupantAt(col, row);
        if (wer >= 0 && wer != idx) return false;
        if (_stege.Count >= StegPlaetze) return false;
        int d = BrueckenRichtung(col, row);
        if (d < 0) return false;
        int k = BrueckenWeite(col, row, d);
        if (k < 0) return false;
        foreach (var (z, _) in BrueckenZellen(col, row, d, k))
        {
            if (!_nav.InBounds(z.X, z.Y)) return false;
            if (StegAn(z.X, z.Y) != null) return false;
        }
        return true;
    }

    // ---- der Menüweg --------------------------------------------------------

    /// <summary>»Brücke bauen« — Modus 1 (<c>dword[0x502ACC] = 1</c>).</summary>
    public string BrueckenbauBeginnen()
    {
        if (BrueckeAus)
            return "»Bruecke bauen« ist mit --bruecke-aus abgeschaltet.";
        int idx = GewaehlterPionier();
        if (idx < 0) return "kein Pionier gewaehlt — nur er kann eine Bruecke bauen.";
        PlacementMode = OrderBruecke;
        PlacementUnit = idx;
        BuildOrderNote = "Bruecke bauen: Uferzelle anklicken (Esc bricht ab)";
        return "";
    }

    /// <summary>Der Klick im Setzmodus — wie bei der Mole: er schickt den
    /// Pionier los, gebaut wird bei der ANKUNFT.</summary>
    private bool BrueckeKlick(int idx, int col, int row)
    {
        var e = _entities[idx];
        if (!BrueckePlatzOk(col, row, idx))
        {
            int d = BrueckenRichtung(col, row);
            BuildOrderNote = d < 0
                ? "dort geht keine Bruecke — sie braucht eine RAUE Uferzelle mit "
                + "Wasser an GENAU EINER Seite."
                : "dort geht keine Bruecke — das Gegenufer liegt weiter als drei "
                + "Wasserzellen entfernt.";
            BrueckeVerworfen++;
            return false;
        }
        // ⚠⚠ NICHT ueber AiWalkTo (NearestFree verlegt das Ziel) — die Bruecke
        // entsteht UNTER dem Pionier. Siehe MoleGehZu.
        if (!MoleGehZu(idx, col, row))
        {
            BuildOrderNote = "der Pionier kommt auf diese Zelle nicht hin.";
            GD.Print($"bruecke: kein Weg von ({e.Col},{e.Row}) nach ({col},{row})");
            BrueckeVerworfen++;
            return false;
        }
        e.Bauart = BauartBruecke;
        e.BauZelle = new Vector2I(col, row);
        e.BauTakte = 0;
        GD.Print($"bruecke: Pionier Platz {e.Slot} faehrt nach ({col},{row})");
        BuildOrderNote = "Bruecke bauen: unterwegs";
        return true;
    }

    /// <summary>Die Vorschau: der GANZE Steg, damit man sieht, wie weit er
    /// reicht und wie breit er ist. Das Bild selbst malt
    /// <see cref="ZeichneBrueckenVorschau"/> darüber.</summary>
    private void SetBrueckePreview(int col, int row, bool ok)
    {
        _previewType = 98;                    // Platzhaltertyp, siehe DrawBuildPreview
        _previewCol = col; _previewRow = row; _previewOk = ok;
        _previewCells.Clear();
        int d = BrueckenRichtung(col, row);
        int k = d < 0 ? -1 : BrueckenWeite(col, row, d);
        if (ok && d >= 0 && k > 0)
            foreach (var (z, mitte) in BrueckenZellen(col, row, d, k))
                _previewCells.Add(new SiteCell(z.X, z.Y, mitte));
        else
            _previewCells.Add(new SiteCell(col, row, false));
        QueueRedraw();
    }

    // ---- bauen --------------------------------------------------------------

    /// <summary>Der Arbeitstakt, wenn der Pionier auf seiner Bauzelle steht —
    /// gerufen aus <see cref="MoleArbeitTick"/>. ⚠ Die Brücke hat KEINE
    /// Bauzeit (Kopf, Punkt 7): sie steht im selben Takt.</summary>
    private void BrueckeFertigstellen(int idx, Entity e)
    {
        int col = e.Col, row = e.Row;
        e.Bauart = 0;
        // ⭐ DIE BAUPRUEFUNG, nicht die Vorschauregel: er selbst darf dort stehen.
        if (!BrueckePlatzOk(col, row, idx))
        {
            int d = BrueckenRichtung(col, row);
            BrueckeNote = $"Bauplatz ({col},{row}) faellt durch: Untergrund "
                        + $"{_nav?.GroundAt(col, row)}, Richtung {d}, Weite "
                        + $"{BrueckenWeite(col, row, d)}";
            GD.Print($"bruecke: {BrueckeNote}");
            Say($"Der Pionier kann auf ({col},{row}) keine Bruecke bauen.");
            BrueckeVerworfen++;
            return;
        }
        // ⭐ Der Pionier geht ZUERST — er steht am Ende auf der Kopfzelle der
        // Fahrbahn, und wer dort steht, wird von der Bruecke erschlagen.
        // ⚠ KEIN Todesprotokoll, KEIN Wrack: er wird verbraucht (bug-227).
        e.Verbraucht = true;
        e.Hp = 0; e.Dead = true; e.DeadTime = 0; e.Path = null; e.Target = -1;
        _nav?.ClearOccupant(col, row, idx);
        if (!BrueckeAnlegen(idx, col, row)) BrueckeVerworfen++;
    }

    /// <summary>Den Steg anlegen — <c>0x4CC280</c>.</summary>
    private bool BrueckeAnlegen(int idx, int col, int row)
    {
        if (_nav == null) return false;
        int d = BrueckenRichtung(col, row);
        int k = d < 0 ? -1 : BrueckenWeite(col, row, d);
        if (d < 0 || k < 0) return false;
        if (_stege.Count >= StegPlaetze)
        { BrueckeNote = "kein freier Brueckenplatz (100)"; return false; }

        var s = new Steg
        {
            Col = col, Row = row, Richtung = d, Wasser = k,
            Variante = (int)(GD.Randi() & 1), Tp = BrueckeTp,
        };
        int nr = _stege.Count;
        _stege.Add(s);

        int erschlagen = 0;
        foreach (var (z, mitte) in BrueckenZellen(col, row, d, k))
        {
            // ⚠ »Beim Setzen stirbt alles, was auf den Kacheln steht« —
            // Zasah(40200, …), 18 Rufstellen aus 0x4CC280.
            int drauf = _nav.OccupantAt(z.X, z.Y);
            if (drauf >= 0 && drauf != idx && drauf < _entities.Count)
            {
                var o = _entities[drauf];
                if (!o.IsBuilding && !o.IsProp && !o.Dead)
                {
                    o.Hp = 0; o.Dead = true; o.DeadTime = 0; o.Path = null; o.Target = -1;
                    _nav.ClearOccupant(z.X, z.Y, drauf);
                    erschlagen++;
                    GD.Print($"bruecke: {o.Name} auf ({z.X},{z.Y}) von der Bruecke erschlagen");
                }
            }
            // Mitte = Fahrbahn (0xFFFE), Rand = Gelaender (0xFFFF, gesperrt).
            var alt = _nav.BrueckeSetzen(z.X, z.Y, mitte);
            s.Zellen.Add((z, mitte, alt));
            // sec20 := 100 + i — unsere Lagentafel kennt diese Zahl schon:
            // »Lage >= 100, praktisch das Brueckengelaender« (MapObjects.cs:74),
            // also BELADEN ja, ENTLADEN nein (das faengt erst bei 200 an).
            _rampen[z.X * 1024 + z.Y] = 100 + nr;
        }

        BrueckeGebaut++;
        BrueckeNote = $"Bruecke ({col},{row}) Richtung {d}, {k} Wasserzellen, "
                    + $"{s.Zellen.Count} Kacheln, TP {s.Tp}"
                    + (erschlagen > 0 ? $", {erschlagen} erschlagen" : "");
        GD.Print($"bruecke: {BrueckeNote}");
        Audio.GameSounds.PlayAt(42, col, row);
        return true;
    }

    /// <summary>Die OBERE LINKE Ecke des Satzes — die vier Richtungsfälle des
    /// Originals, wörtlich: <c>(x−1, y−k−1)</c>, <c>(x−1, y)</c>,
    /// <c>(x, y−1)</c>, <c>(x−k−1, y−1)</c>.</summary>
    private static Vector2I BrueckenEcke(Steg s) => s.Richtung switch
    {
        0 => new Vector2I(s.Col - 1, s.Row - s.Wasser - 1),   // Lauf −y
        1 => new Vector2I(s.Col - 1, s.Row),                  // Lauf +y
        2 => new Vector2I(s.Col, s.Row - 1),                  // Lauf +x
        _ => new Vector2I(s.Col - s.Wasser - 1, s.Row - 1),   // Lauf −x
    };

    /// <summary>
    /// Wo liegt diese Zelle im Satz?
    ///
    /// <para>⚠⚠ <b>Beides zählt vom RECHTECK, nicht von der Laufrichtung.</b>
    /// Das steht in der Lesung: die Satzstartzelle ist in allen vier Fällen die
    /// <b>obere linke</b> Ecke (siehe <see cref="BrueckenEcke"/>), also läuft
    /// <c>Pos</c> immer nach rechts bzw. nach unten — auch dann, wenn der
    /// Pionier von dort aus nach oben oder nach links baut. Wer statt dessen
    /// vom Pionier aus zählt, dreht bei zwei der vier Richtungen Geländer und
    /// Brückenkopf um 180 Grad: dasselbe Bauwerk, spiegelverkehrt gemalt.</para>
    ///
    /// <para>Für eine senkrechte Brücke ist <c>Streifen</c> die Spalte (x−1,
    /// x, x+1), für eine waagerechte die Zeile (y−1, y, y+1).</para></summary>
    private static (int Pos, int Streifen) BrueckenLage(Steg s, Vector2I z)
    {
        var ecke = BrueckenEcke(s);
        bool senkrecht = BrueckenLauf[s.Richtung].Y != 0;
        return senkrecht ? (z.Y - ecke.Y, z.X - ecke.X)
                         : (z.X - ecke.X, z.Y - ecke.Y);
    }

    /// <summary>Die Kachel einer Brückenzelle —
    /// <c>10000 + 120·Bildsatz + 54·Variante + 18·Stufe + Lage</c> mit
    /// Bildsatz 1 (Holzsteg). <see cref="Import.MapBaker.BrueckenKachelBasis"/>
    /// ist bereits <c>10000 + 120·1</c>.</summary>
    private static int BrueckenKachel(Steg s, Vector2I z)
    {
        var (pos, streifen) = BrueckenLage(s, z);
        // Pos 0/1/2 = Anfang / Mitte / Ende — alles dazwischen ist »Mitte«.
        int p3 = pos <= 0 ? 0 : pos >= s.Wasser + 1 ? 2 : 1;
        int st = Mathf.Clamp(streifen, 0, 2);              // 0,1,2 ab der Ecke
        bool senkrecht = BrueckenLauf[s.Richtung].Y != 0;
        int lage = senkrecht ? 9 + 3 * st + p3 : 3 * st + p3;
        int stufe = Mathf.Clamp((BrueckeTp - s.Tp) / 167, 0, 2);
        return Import.MapBaker.BrueckenKachelBasis + 120 * (BrueckeBildsatz - 1)
             + 54 * s.Variante + 18 * stufe + lage;
    }

    /// <summary>Die Brücke fällt — der alte Untergrund kommt zurück.
    /// ⚠ Das Trümmerbild (<c>10114 + 120·Bildsatz + 0…5</c> an den beiden
    /// Köpfen) zeichnen wir nicht; es ist gelesen, aber im Spiel ist der
    /// Zerfall einer Brücke noch nie vorgekommen.</summary>
    public void BrueckeTrifft(int col, int row, int schaden)
    {
        var s = StegAn(col, row);
        if (s == null) return;
        s.Tp -= schaden;
        if (s.Tp > 0) return;
        foreach (var (z, _, alt) in s.Zellen)
        {
            _nav?.BrueckeLoesen(z.X, z.Y, alt);
            _rampen.Remove(z.X * 1024 + z.Y);
        }
        _stege.Remove(s);
        GD.Print($"bruecke: Bruecke ({s.Col},{s.Row}) zerstoert — "
               + $"{s.Zellen.Count} Zellen zurueck");
    }

    // ---- zeichnen -----------------------------------------------------------

    public int StegeGezeichnet;

    /// <summary><b>DIE GEBAUTE BRÜCKE ZEICHNEN.</b> Wie die Mole: der Importeur
    /// legt die 120 Kacheln <c>10120…10239</c> in den Streifen unten an
    /// <c>&lt;karte&gt;.objects.png</c>, hier wird daraus gemalt — gesucht wird
    /// nach dem CODE, denn auf eine im Spiel gebaute Brücke zeigt kein Eintrag.
    ///
    /// <para>⚠ Eine Karte aus einem ÄLTEREN Import hat den Streifen nicht. Dann
    /// bleibt die Brücke unsichtbar — sie WIRKT trotzdem, und Stufe 2 des
    /// Prüfstands sagt es. Kein Loch, keine erfundene Grafik.</para></summary>
    private void ZeichneStege()
    {
        if (ObjektEbene is not { } tex) return;
        ZeichneBrueckenVorschau(tex);
        if (_stege.Count == 0) return;
        StegeGezeichnet = 0;
        foreach (var s in _stege)
            foreach (var (z, _, _) in s.Zellen)
            {
                if (StreifenKachel(BrueckenKachel(s, z)) is not { } k) continue;
                var ziel = CellRect(_ox, _oy, z.X, z.Y, ElevOf(z.X, z.Y));
                // ⚠ Derselbe BlitAnchor wie beim Backen — ohne ihn sitzt das
                // Bild eine Zelle zu tief (das war bug-226 bei der Mole).
                DrawTextureRectRegion(tex,
                    new Rect2(new Vector2(ziel.Position.X,
                                          ziel.Position.Y + Import.MapBaker.BlitAnchor + k.YOff),
                              k.Feld.Size), k.Feld);
                StegeGezeichnet++;
            }
    }

    /// <summary>Die Vorschau zeigt die BRÜCKE SELBST, halb durchsichtig — wie
    /// bei der Mole seit bug-239. Nur über einer gültigen Zelle; sonst bleibt
    /// es beim roten Feld der gewöhnlichen Bauvorschau.</summary>
    private void ZeichneBrueckenVorschau(Texture2D tex)
    {
        if (PlacementMode != OrderBruecke || _previewCol < 0) return;
        if (!BrueckePlatzOk(_previewCol, _previewRow, PlacementUnit)) return;
        int d = BrueckenRichtung(_previewCol, _previewRow);
        int k = BrueckenWeite(_previewCol, _previewRow, d);
        if (d < 0 || k < 0) return;
        // ⚠ Die Zufallsvariante steht erst beim Bauen fest; die Vorschau nimmt
        // die 0 — Richtung und Ausdehnung sind es, worauf es ankommt.
        var s = new Steg { Col = _previewCol, Row = _previewRow, Richtung = d, Wasser = k };
        foreach (var (z, _) in BrueckenZellen(_previewCol, _previewRow, d, k))
        {
            if (StreifenKachel(BrueckenKachel(s, z)) is not { } kk) continue;
            var ziel = CellRect(_ox, _oy, z.X, z.Y, ElevOf(z.X, z.Y));
            DrawTextureRectRegion(tex,
                new Rect2(new Vector2(ziel.Position.X,
                                      ziel.Position.Y + Import.MapBaker.BlitAnchor + kk.YOff),
                          kk.Feld.Size), kk.Feld,
                new Color(1f, 1f, 1f, 0.55f));
        }
    }

    // ---- Prüfstand ----------------------------------------------------------

    /// <summary><c>--bruecke-check</c> — die ganze Kette, wirklich gefahren:
    /// zählen, wo eine Brücke hinkann, dann eine bauen und am Gitter messen,
    /// was sich geändert hat.</summary>
    public string BrueckeCheckLine()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("bruecke-check");
        sb.AppendLine($"  Gegenschalter --bruecke-aus: {BrueckeAus}");
        if (_nav == null) return sb.Append("  keine Karte").ToString();

        // 1) Wo geht eine Bruecke hin, und wie breit ist dort der Fluss?
        //    DAS NULLMODELL: alle 110 Originalbruecken haben k aus {1,2,3} —
        //    kommt hier eine 4 heraus, ist die Zaehlung falsch.
        int gut = 0;
        var weiten = new Dictionary<int, int>();
        var richtungen = new int[4];
        Vector2I platz = new(-1, -1);
        for (int r = 0; r < _nav.Height; r++)
            for (int c = 0; c < _nav.Width; c++)
            {
                if (!BrueckePlatzOk(c, r)) continue;
                gut++;
                int d = BrueckenRichtung(c, r);
                int k = BrueckenWeite(c, r, d);
                richtungen[d]++;
                weiten[k] = weiten.GetValueOrDefault(k) + 1;
                if (platz.X < 0) platz = new Vector2I(c, r);
            }
        var wtext = new List<string>();
        bool weitenOk = true;
        foreach (var kv in weiten)
        {
            wtext.Add($"{kv.Key}x Wasser: {kv.Value}");
            if (kv.Key is < 1 or > BrueckeMaxWasser) weitenOk = false;
        }
        sb.AppendLine($"  1. {gut} Uferzellen nehmen eine Bruecke"
                    + (wtext.Count > 0 ? " (" + string.Join(", ", wtext) + ")" : "")
                    + $"; Richtungen oben {richtungen[0]}, unten {richtungen[1]}, "
                    + $"rechts {richtungen[2]}, links {richtungen[3]}");
        if (!weitenOk)
            sb.AppendLine("     ⚠ eine Weite ausserhalb 1..3 — die Zaehlung stimmt nicht");
        if (platz.X < 0)
            // ⚠ Zwei sehr verschiedene Gruende fuer dieselbe Null, und wer sie
            // nicht auseinanderhaelt, liest den Gegenschalter als Befund:
            // ABGESCHALTET (dann ist die Null der erwartete Nullmodellbefund)
            // oder eine Karte ganz ohne Ufer (dann ist NICHTS gemessen).
            return sb.Append(BrueckeAus
                ? "  NULLMODELL WIE ERWARTET: mit --bruecke-aus nimmt keine "
                + "einzige Zelle eine Bruecke"
                : "  keine Uferzelle auf dieser Karte — NICHT GEMESSEN").ToString();

        // 2) Das Kachelbild — hat diese Karte den Streifen schon?
        var probe = StreifenKachel(Import.MapBaker.BrueckenKachelBasis);
        sb.AppendLine($"  2. Kachelbild: Streifenkachel {Import.MapBaker.BrueckenKachelBasis} "
                    + (probe == null
                       ? "FEHLT — Karte aus einem Import vor dem 13.09.2026; die "
                       + "Bruecke wirkt, ist aber unsichtbar (--reexport-maps)"
                       : $"da ({probe.Value.Feld.Size.X}x{probe.Value.Feld.Size.Y})"));

        // 3) Bauen — und danach messen, was sich am Gitter geaendert hat.
        bool ok = BrueckeAnlegen(-1, platz.X, platz.Y);
        var s2 = _stege.Count > 0 ? _stege[^1] : null;
        bool mitteFrei = s2 != null, randGesperrt = s2 != null, lagenOk = s2 != null;
        int kacheln = 0;
        var gesehen = new HashSet<int>();
        if (s2 != null)
            foreach (var (z, mitte, _) in s2.Zellen)
            {
                bool fahrbar = _nav.CanEnter(z.X, z.Y, Simulation.NavGrid.MoveClass.Vehicle);
                if (mitte && !fahrbar) mitteFrei = false;
                if (!mitte && fahrbar) randGesperrt = false;
                if (!RampeBeladen(z.X, z.Y) || RampeEntladen(z.X, z.Y)) lagenOk = false;
                int code = BrueckenKachel(s2, z);
                gesehen.Add(code);
                if (StreifenKachel(code) != null) kacheln++;
            }
        int erwartet = s2 == null ? 0 : 3 * (s2.Wasser + 2);
        sb.AppendLine($"  3. Bau bei ({platz.X},{platz.Y}): {(ok ? "angelegt" : "ABGELEHNT")}"
                    + (s2 != null
                       ? $", Richtung {s2.Richtung}, {s2.Wasser} Wasserzellen, "
                       + $"{s2.Zellen.Count} Zellen (erwartet {erwartet})"
                       : ""));
        if (s2 != null)
        {
            sb.AppendLine($"  4. Fahrbahn befahrbar {mitteFrei}, Gelaender gesperrt "
                        + $"{randGesperrt}, Lage 100+ (beladen ja, entladen nein) {lagenOk}");
            sb.AppendLine($"  5. {gesehen.Count} verschiedene Kachelcodes "
                        + $"(erwartet {erwartet}), davon {kacheln} im Streifen gefunden");
        }

        bool alles = ok && s2 != null && mitteFrei && randGesperrt && lagenOk && weitenOk
                  && s2.Zellen.Count == erwartet && gesehen.Count == erwartet
                  && probe != null && kacheln == erwartet;
        sb.Append(BrueckeAus
            ? (gut == 0 && !ok ? "  NULLMODELL WIE ERWARTET: keine Bruecke moeglich"
                               : "  ⚠ NULLMODELL OHNE BEFUND")
            : alles ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
