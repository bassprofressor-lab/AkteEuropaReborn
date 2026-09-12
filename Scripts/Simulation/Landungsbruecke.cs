namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE LANDUNGSBRÜCKE — »Mole bauen«, die Rampe aus sec21</b>
/// (gebaut 12.09.2026, bug-219).
///
/// <para><b>Gemeldet</b> aus Kampagne 8: »ich muss mit meinem pionieren eine
/// ladungsbrücke/rampe bauen, das geht bei uns noch nicht«. Das Missionsziel 8
/// lautet wörtlich »Bauen Sie Landungsbrücken«.</para>
///
/// <para><b>Das Original erklärt es selbst</b> (Hilfetext 45): »Um eine
/// Landungsbrücke oder Brücke zu bauen, aktivieren Sie eine Pioniereinheit und
/// bringen das Einheitenmenü auf den Schirm. Wählen Sie jetzt @LANDUNG oder
/// @BRÜCKE. Jetzt wählen Sie den Platz … Sehen Sie einen komplett weißen Umriß
/// des Objektes, kann es durch einen Klick dorthin gebaut werden.« Und das
/// Lexikon (Seite 99): »… wobei der jeweilige Droid jedoch auch seine eigenen
/// Bestandteile verarbeitet und so unbenutzbar wird.«</para>
///
/// <para><b>GELESEN</b> — zwei unabhängige Leser, beide EXE, Berichte
/// <c>berichte/pionier-menue-fable.md</c> und
/// <c>berichte/landungsbruecke-fable.md</c>:</para>
/// <list type="number">
/// <item><b>Die Zeile heißt »Mole bauen«</b>, Befehlsliste 0x4FD660 Zeile 0x0E;
///   0x0D ist »Brücke bauen«, 0x0C »Brücke/Mole reparieren«. ⚠ Die Wörter
///   LANDUNG/BRÜCKE des Hilfetextes stehen so NICHT in der EXE — dass »Landung«
///   diese Zeile meint, ist die einzige Vermutung der ganzen Kette.</item>
/// <item><b>Der Weg:</b> Menüklick setzt <c>dword[0x502ACC]</c> (1 Brücke,
///   2 Mole, 4 Ausbessern) → der Zeigerautomat 0x4315D0 prüft je Mauszelle →
///   Linksklick schickt <b>Befehl 16</b> (Einheit, Zelle, Bauart) → die Einheit
///   FÄHRT hin (UKOL 2) und trägt die Bauart in +0x40 → bei Ankunft schaltet
///   der UKOL-0-Arm auf <b>UKOL 20</b> → zwei Arbeitszyklen → Bauwerk.</item>
/// <item><b>Die Vorbedingung</b> (0x4CBC90): die Zelle muss <c>0xFFFD</c> sein
///   — <b>rau und leer</b>. Dazu die vier Eckhöhen: alle ≤ 1, und der Eckcode
///   muss in <c>{5, 3, 10, 12}</c> liegen = genau <b>zwei benachbarte Ecken
///   auf 1</b>, also eine Böschung um EINE Stufe zwischen Höhe 0 und 1. Eine
///   Böschung von 3 auf 4 bekommt keine Rampe.</item>
/// <item><b>Der Bauplatz ist die EIGENE Zelle</b> (UKOL 20 nimmt x = e[0],
///   y = e[1]) — die Rampe entsteht unter dem Pionier.</item>
/// <item><b>Der Pionier wird bei der FERTIGSTELLUNG gelöscht</b> (0x433C20 aus
///   dem Trupp, dann 0x410E60), im selben Takt VOR dem Anlegen. Kein Wrack,
///   kein Merker.</item>
/// <item><b>Die Rampe ist EINE Zelle</b>, Satz {x, y, Bild, TP}: Bild
///   <c>2·Richtung + Zufall&amp;1</c>, Kachel <c>10723 + Bild</c>,
///   <c>sec6 := 0xFFFE</c> (befahrbar) und <c>sec20 := 200 + i</c>.</item>
/// <item>⭐ <b>Warum sie »Landungsbrücke« heißt:</b> der Einstieg für
///   FAHRZEUGE verlangt <c>sec20 &gt;= 200</c> (@0x438440). Fußvolk braucht
///   keine. Die Rampe ist der Fahrzeug-Landeplatz am Ufer.</item>
/// </list>
///
/// <para>⚠⚠ <b>BERICHTIGUNG, und sie ist die wichtigste des Tages:</b> es gibt
/// <b>keinen Bautakt</b>. <c>Rampe+3</c> (200) und <c>Brücke+0x16</c> (500) sind
/// <b>TREFFERPUNKTE</b>, kein Bauzähler — über den ganzen Code gibt es keinen
/// Herunterzähler, der einzige Abzug sitzt im Trefferarm. Die »drei Bauphasen«
/// der Zeichner sind <b>Schadensstufen</b>. Brücke und Mole stehen sofort und
/// sind sofort begehbar; was Zeit kostet, ist der Weg des Pioniers und zwei
/// Arbeitszyklen.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN, benannt:</b></para>
/// <list type="bullet">
/// <item><b>Die Uferkante statt der Eckhöhen.</b> Das Original prüft die vier
///   Ecken der Zelle gegen <c>{5,3,10,12}</c> — aber sein Eckraster (sec2)
///   führen wir nicht, unsere Höhe steht je ZELLE und ist fast überall gleich.
///   Wir fragen darum direkt: genau EIN orthogonaler Nachbar ist Wasser, und
///   der gibt die Richtung. <b>An den 90 echten Rampen des Originals
///   gemessen</b>, siehe <see cref="RampenRichtung"/> — die alte Eckenregel
///   hätte 1 von 90 bestanden, die neue trifft 69 von 71 Richtungen.</item>
/// <item><b>Zwei Arbeitszyklen à 24 Takte</b> = <see cref="MoleArbeitTakte"/>.
///   Die 24 sind aus dem Bericht gerechnet, nicht gemessen.</item>
/// <item>Das <b>Kachelbild</b> der Rampe zeichnen wir nicht neu — uns fehlt der
///   Kachelwechsel zur Laufzeit für dieses Band. Die Zelle WIRKT richtig
///   (befahrbar), sie sieht nur aus wie vorher. Das ist eine Lücke und keine
///   Deutung; sie steht in der Prüfstandszeile.</item>
/// </list>
///
/// <para>Gegenschalter <c>--rampe-bauzeit</c> (die widerlegte Deutung: 200
/// Takte herunterzählen, erst dann befahrbar) und <c>--fussvolkmenue-alt</c>
/// (das Menü des Pioniers gibt es gar nicht erst). Prüfstand
/// <c>--mole-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Unsere Auftragsnummern, in der Reihe von <c>OrderDepot</c> = 5,
    /// <c>OrderFieldMine</c> = 6, <c>OrderGenerator</c> = 7.</summary>
    public const int OrderMole = 8, OrderAusbessern = 9;

    /// <summary><c>--rampe-bauzeit</c> — siehe Kopf: das Nullmodell zur
    /// Berichtigung. Mit ihm dauert der Bau 200 Takte, und die Zelle wird erst
    /// am Ende befahrbar.</summary>
    public static bool RampeBauzeit;

    /// <summary>Zwei Arbeitszyklen à 24 Takte (+0x47 = 11, Infanterie-
    /// Arbeitsanimation). ⚠ Gerechnet, nicht gemessen.</summary>
    private const int MoleArbeitTakte = 48;

    /// <summary>Was <c>--rampe-bauzeit</c> daraus macht: die widerlegte
    /// Deutung, 200 Takte wie ein Bauzähler.</summary>
    private const int MoleBauzeitAlt = 200;

    /// <summary>Eine Rampe der Karte — der 4-Byte-Satz aus sec21.</summary>
    public sealed class Rampe
    {
        public int Col, Row;
        /// <summary><c>+2</c>: <c>2·Richtung + Zufall&amp;1</c>, Kachel
        /// <c>10723 + Bild</c>.</summary>
        public int Bild;
        /// <summary><c>+3</c>: TREFFERPUNKTE, 200 — kein Bauzähler.</summary>
        public int Tp = 200;
    }

    /// <summary>sec21 zur Laufzeit — höchstens 50, wie im Original.</summary>
    private readonly List<Rampe> _moleSaetze = new();
    public int RampenZahl => _moleSaetze.Count;
    private const int RampenPlaetze = 50;

    // ---- Zähler des Prüfstands ---------------------------------------------
    public int MoleGebaut, MoleVerworfen, MoleAusgebessert;
    public string MoleNote = "";

    /// <summary>Die Waffenzeile des Pioniers — Entwurfssatz 53, Fall 8 der
    /// Fussvolkweiche. Unsere Fusssoldaten führen sie als
    /// <c>InfCompBase + Zeile</c>.</summary>
    public const int PionierWaffenzeile = 198;

    /// <summary>Ist das ein Pionier? ⚠ Über die WAFFENZEILE, nicht über den
    /// Namen: der Name kommt aus der Entwurfstafel und kann übersetzt sein.</summary>
    public static bool IstPionier(Entity e)
        => e.GameUnitType == 1 && WaffenUntertyp(e) == 2 * (PionierWaffenzeile - 190);

    /// <summary>Der erste eigene Pionier der Karte, oder −1 — fuer die
    /// Pruefstaende, die keinen Mausklick haben.</summary>
    public int ErsterPionier()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp) continue;
            if (e.Owner != ViewPlayer || !IstPionier(e)) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Der gewählte Pionier, oder −1.</summary>
    private int GewaehlterPionier()
    {
        int idx = MenueEinheit();
        if (idx < 0) return -1;
        return IstPionier(_entities[idx]) ? idx : -1;
    }

    // ---- der Eckcode --------------------------------------------------------

    /// <summary>Die vier orthogonalen Nachbarn in der Reihenfolge der
    /// Richtungen: 0 = links, 1 = oben, 2 = rechts, 3 = unten.</summary>
    private static readonly Vector2I[] RampenSeiten =
        { new(-1, 0), new(0, -1), new(1, 0), new(0, 1) };

    /// <summary>
    /// <b>DIE RICHTUNG EINER MOLE — und die Geschichte einer Berichtigung.</b>
    ///
    /// <para>⚠⚠ <b>Was hier zuerst stand und falsch war:</b> die vier EckHÖHEN
    /// der Zelle als 4-Bit-Code, nachgeschlagen in <c>{5,3,10,12}</c>. Genau so
    /// steht es im Original (2×2-Fenster in sec2, Schrittweite 257) — aber
    /// <b>sec2 ist ein Eckraster, das wir gar nicht ausführen</b>. Unsere
    /// Höhenkarte hält EINE Zahl je ZELLE, und die ist fast überall gleich (auf
    /// map_05 tragen 3292 von 3500 Zellen die 1). Eine Böschung INNERHALB einer
    /// Zelle kann sie nicht ausdrücken.</para>
    ///
    /// <para><b>Gemessen an den 90 echten Rampen des Originals</b> (sec21 über
    /// 11 Karten) — das ist das Nullmodell, und es hat die alte Regel
    /// erschlagen: <b>1 von 90</b> hätte sie bestanden. Die Codes kamen als
    /// 0 (43×) und 15 (43×) heraus, also »alle vier Ecken gleich« — flach.</para>
    ///
    /// <para>⭐ <b>Was dieselbe Messung dafür hergab:</b> 71 der 90 Rampen haben
    /// GENAU EINEN orthogonalen Wassernachbarn, und die Richtung folgt ihm:</para>
    /// <code>
    ///   Wasserseite links  -> Richtung 0   13 mal
    ///               oben   -> 1             3 mal
    ///               rechts -> 2            18 mal
    ///               unten  -> 3            35 mal
    ///               unten  -> 0             2 mal   (die einzigen Ausreisser)
    /// </code>
    /// <para><b>69 von 71 auf der Diagonale.</b> Nullmodell: bei blinder Wahl
    /// aus vier Richtungen wären rund 18 zu erwarten. ⭐ Und die Zuordnung ist
    /// dieselbe, die die Bitfolge-Annahme vorhergesagt hatte (5 = linke Kante,
    /// 3 = obere, 10 = rechte, 12 = untere) — die RICHTUNGSTAFEL war richtig,
    /// das RASTER war falsch.</para>
    ///
    /// <para>⚠ <b>UNSERE SETZUNG, benannt:</b> »zwei benachbarte Ecken hoch«
    /// heisst im Original »die Uferlinie läuft an einer Kante dieser Zelle
    /// entlang«. Wir fragen das direkt am Geländeraster: genau ein Nachbar ist
    /// Wasser. Das ist eine ÜBERSETZUNG in unser Raster, keine Lesung — aber
    /// eine an 71 Originalrampen geprüfte. Wer sec2 nachrüstet, ersetzt hier
    /// vier Zeilen.</para>
    /// </summary>
    /// <returns>0…3, oder −1 wenn hier keine Mole hingehört.</returns>
    private int RampenRichtung(int col, int row)
    {
        if (_nav == null) return -1;
        int seite = -1;
        for (int d = 0; d < 4; d++)
        {
            int c = col + RampenSeiten[d].X, r = row + RampenSeiten[d].Y;
            if (!_nav.InBounds(c, r)) continue;
            if (_nav.GroundAt(c, r) != Simulation.NavGrid.Ground.Water) continue;
            if (seite >= 0) return -1;          // zwei Ufer: keine eindeutige Richtung
            seite = d;
        }
        return seite;
    }

    /// <summary><b>Darf hier eine Mole entstehen?</b> — <c>0x4CBC90</c>: die
    /// Zelle muss rau UND leer sein, und ihr Eckcode muss eine Uferböschung
    /// sein. ⚠ NICHT geprüft (die Prüfer des Originals fragen es nicht):
    /// Wasser daneben, Feind daneben, Besitzer, Tiefe.</summary>
    public bool MolePlatzOk(int col, int row)
    {
        if (_nav == null || !_nav.InBounds(col, row)) return false;
        if (_nav.GroundAt(col, row) != Simulation.NavGrid.Ground.Rough) return false;
        if (_nav.OccupantAt(col, row) >= 0) return false;
        if (RampeAn(col, row) != null) return false;      // schon eine da -> stilles Nichts
        return RampenRichtung(col, row) >= 0;
    }

    /// <summary>
    /// <b>Darf hier GEBAUT werden?</b> — <c>0x4CBD90</c>, und das ist eine
    /// ANDERE Frage als <see cref="MolePlatzOk"/>.
    ///
    /// <para>⚠⚠ 12.09.2026, seine Meldung: »ich sehe keine rampe bzw. baut er
    /// keine«. Genau hier lag es: die Bauprüfung benutzte die Regel der
    /// VORSCHAU, und die verlangt eine LEERE Zelle. Sobald der Pionier
    /// ankommt, steht er selbst darauf — die Zelle galt als belegt, und der
    /// Auftrag wurde still verworfen. Der Pionier lief also hin und tat
    /// nichts.</para>
    ///
    /// <para>Das Original trennt die zwei Fragen sauber, und beide Leser haben
    /// es gemeldet (berichte/landungsbruecke-fable.md §2.4):</para>
    /// <list type="bullet">
    ///   <item><c>0x4CBC90</c> — <b>»darf ich hier planen?«</b>: die Zelle muss
    ///     <c>0xFFFD</c> sein, also rau und LEER.</item>
    ///   <item><c>0x4CBD90</c> — <b>»darf ich hier weiterbauen?«</b>: rau
    ///     <b>oder</b> eine Infanteriezelle mit <b>genau einem Mann, und der
    ///     bin ich</b> (@0x4CBDFA + 0x433B50). Ein zweiter Mann in derselben
    ///     Zelle sperrt.</item>
    /// </list>
    /// <para>Es ist dieselbe Unterscheidung, die auch der Bauplatz braucht: die
    /// Rampe entsteht UNTER dem Pionier.</para></summary>
    private bool MoleBauOk(int col, int row, int idx)
    {
        if (_nav == null || !_nav.InBounds(col, row)) return false;
        if (_nav.GroundAt(col, row) != Simulation.NavGrid.Ground.Rough) return false;
        if (RampeAn(col, row) != null) return false;
        // »genau ein Mann, und der bin ich«: frei ist auch gut, meine eigene
        // Zelle ebenso — ein FREMDER darauf sperrt.
        int wer = _nav.OccupantAt(col, row);
        if (wer >= 0 && wer != idx) return false;
        return RampenRichtung(col, row) >= 0;
    }

    /// <summary>Die Rampe auf dieser Zelle, oder <c>null</c>.</summary>
    public Rampe? RampeAn(int col, int row)
    {
        foreach (var r in _moleSaetze) if (r.Col == col && r.Row == row) return r;
        return null;
    }

    // ---- die zwei Menüwege --------------------------------------------------

    /// <summary>»Mole bauen« — Modus 2 (<c>dword[0x502ACC] = 2</c>).</summary>
    public string MolenbauBeginnen()
    {
        int idx = GewaehlterPionier();
        if (idx < 0) return "kein Pionier gewaehlt — nur er kann eine Mole bauen.";
        PlacementMode = OrderMole;
        PlacementUnit = idx;
        BuildOrderNote = "Mole bauen: Uferboeschung anklicken (Esc bricht ab)";
        return "";
    }

    /// <summary>»Brücke/Mole reparieren« — Modus 4. Der Zeiger prüft
    /// <c>sec20 &gt; 99</c>, bei uns: steht dort eine Rampe?</summary>
    public string AusbessernBeginnen()
    {
        int idx = GewaehlterPionier();
        if (idx < 0) return "kein Pionier gewaehlt.";
        if (_moleSaetze.Count == 0)
            return "es gibt keine Mole und keine Bruecke zum Ausbessern.";
        PlacementMode = OrderAusbessern;
        PlacementUnit = idx;
        BuildOrderNote = "Ausbessern: Mole anklicken (Esc bricht ab)";
        return "";
    }

    /// <summary>Die Vorschau der Mole: EINE Zelle, grün oder rot.
    ///
    /// <para>Im Original schreibt der Zeigerautomat jede geprüfte Zelle mit
    /// ihrem Ja/Nein in die Liste <c>0xA32188</c>, und der Kartenmaler zeichnet
    /// je Zelle die Marke <b>0x90 (baubar)</b> oder <b>0x9C (nicht)</b> — das
    /// ist der »weiße Umriß« des Hilfetextes. ⚠ Die Marken sind als NUMMERN
    /// gelesen, ihre Bilder nicht; wir nehmen darum unsere vorhandene
    /// Bauvorschau (grün/rot) und nicht eine geratene weiße.</para></summary>
    private void SetMolePreview(int col, int row, bool ok)
    {
        _previewType = 99;                       // Platzhaltertyp, siehe DrawBuildPreview
        _previewCol = col; _previewRow = row; _previewOk = ok;
        _previewCells.Clear();
        _previewCells.Add(new SiteCell(col, row, ok));
        QueueRedraw();
    }

    /// <summary>Der Klick im Setzmodus — das Gegenstück zu
    /// <see cref="PlacementClick"/> für die zwei Pionierbefehle. Er schickt den
    /// Pionier los; gebaut wird erst bei der ANKUNFT.</summary>
    private bool MoleKlick(int idx, int col, int row, int order)
    {
        var e = _entities[idx];
        if (order == OrderMole && !MolePlatzOk(col, row))
        {
            BuildOrderNote = "dort geht keine Mole — sie braucht eine RAUE, leere "
                           + "Uferboeschung um eine Hoehenstufe.";
            MoleVerworfen++;
            return false;
        }
        if (order == OrderAusbessern && RampeAn(col, row) == null)
        {
            BuildOrderNote = "dort steht keine Mole.";
            MoleVerworfen++;
            return false;
        }
        // Befehl 16: fahren, und die Bauart in +0x40 merken.
        //
        // ⚠⚠ NICHT ueber AiWalkTo: das schickt die Einheit ueber
        // `NearestFree` zur naechstgelegenen FREIEN Zelle, und der Kommentar
        // am Wegende sagt es selbst — »das Ziel kann von NearestFree verlegt
        // worden sein, und dann erreicht die Einheit es nie genau«. Die Rampe
        // entsteht aber UNTER dem Pionier: er muss auf GENAU diese Zelle.
        if (!MoleGehZu(idx, col, row))
        {
            BuildOrderNote = "der Pionier kommt auf diese Zelle nicht hin.";
            GD.Print($"mole: kein Weg von ({e.Col},{e.Row}) nach ({col},{row})");
            MoleVerworfen++;
            return false;
        }
        GD.Print($"mole: Pionier Platz {e.Slot} faehrt nach ({col},{row}), "
               + $"Bauart {(order == OrderMole ? 2 : 4)}");
        e.Bauart = order == OrderMole ? 2 : 4;
        e.BauZelle = new Vector2I(col, row);
        e.BauTakte = 0;
        BuildOrderNote = order == OrderMole ? "Mole bauen: unterwegs" : "Ausbessern: unterwegs";
        return true;
    }

    /// <summary>Den Pionier auf GENAU diese Zelle schicken — wie
    /// <c>AiWalkTo</c>, aber ohne die Ersatzwahl <c>NearestFree</c>. Steht er
    /// schon darauf, ist nichts zu fahren.</summary>
    private bool MoleGehZu(int idx, int col, int row)
    {
        if (_nav == null || idx < 0 || idx >= _entities.Count) return false;
        var e = _entities[idx];
        if (e.Dead || !e.Mobile || e.DugIn) return false;
        if (e.Col == col && e.Row == row) { e.Path = null; return true; }
        var weg = _nav.FindPath(new Vector2I(e.Col, e.Row), new Vector2I(col, row),
                                e.Move, idx);
        if (weg == null || weg.Count == 0) return false;
        e.Path = weg;
        e.PathIdx = 0;
        e.Goal = new Vector2I(col, row);
        AiVormerkungLoesen(idx, e);
        e.WaitTime = 0;
        e.Target = -1;
        e.Ordered = true;
        return true;
    }

    /// <summary><b>UKOL 20</b> — der Arbeitstakt des Pioniers. Im Einheitentakt
    /// gerufen, gleich hinter der Reparatur.
    ///
    /// <para>⚠ Geprüft wird die Zelle, auf der er WIRKLICH steht. Kommt er
    /// woanders an, fällt die Probe durch und der Auftrag wird still verworfen
    /// — genau wie im Original.</para></summary>
    private void MoleArbeitTick(int idx, Entity e)
    {
        if (e.Bauart == 0 || e.Dead) return;
        if (e.Path != null && e.PathIdx < e.Path.Count) return;      // noch unterwegs
        if (e.Col != e.BauZelle.X || e.Row != e.BauZelle.Y)
        {
            // ⚠ Das Original verwirft hier STILL. Wir schreiben eine Zeile:
            // »der Pionier laeuft hin und tut nichts« war der halbe Tag.
            MoleNote = $"Bauplatz nicht erreicht — Pionier steht auf ({e.Col},{e.Row}), "
                     + $"gewollt war ({e.BauZelle.X},{e.BauZelle.Y})";
            GD.Print($"mole: {MoleNote}");
            e.Bauart = 0;
            MoleVerworfen++;
            return;
        }
        bool ausbessern = e.Bauart == 4;
        // ⭐ DIE BAUPRUEFUNG, nicht die Vorschauregel — siehe MoleBauOk.
        if (!ausbessern && !MoleBauOk(e.Col, e.Row, idx))
        {
            MoleNote = $"Bauplatz ({e.Col},{e.Row}) faellt durch: rau "
                     + $"{_nav?.GroundAt(e.Col, e.Row)}, Richtung "
                     + $"{RampenRichtung(e.Col, e.Row)}, schon eine Rampe "
                     + $"{RampeAn(e.Col, e.Row) != null}";
            GD.Print($"mole: {MoleNote}");
            e.Bauart = 0; MoleVerworfen++; return;
        }

        e.BauTakte++;
        int noetig = RampeBauzeit ? MoleBauzeitAlt : MoleArbeitTakte;
        if (ausbessern) noetig = Mathf.Max(1, noetig / 2);    // AKCE 2 statt 3
        if (e.BauTakte < noetig) return;

        // ---- fertig: erst der Pionier, dann das Bauwerk --------------------
        int col = e.Col, row = e.Row;
        e.Bauart = 0;
        var vorhanden = RampeAn(col, row);
        if (ausbessern)
        {
            if (vorhanden != null) { vorhanden.Tp = 200; MoleAusgebessert++; }
            MoleNote = $"Mole ({col},{row}) ausgebessert, Pionier verbraucht";
            Audio.GameSounds.PlayAt(43, col, row);
        }
        else
        {
            if (_moleSaetze.Count >= RampenPlaetze)
            { MoleNote = "kein freier Rampenplatz (50)"; MoleVerworfen++; return; }
            int d = RampenRichtung(col, row);
            var r = new Rampe
            {
                Col = col, Row = row,
                Bild = 2 * Mathf.Max(0, d) + (int)(GD.Randi() & 1),
                Tp = 200,
            };
            _moleSaetze.Add(r);
            // sec6 := 0xFFFE — die Zelle ist ab sofort BEFAHRBAR. Das ist der
            // ganze Zweck, und es geschieht im selben Takt (siehe Kopf).
            _nav?.RampeSetzen(col, row, true);
            // ⭐⭐ sec20 := 200 + i — UND DAS IST DIE HALBE MIETE: die Lagentafel
            // fuehren wir laengst (MapObjects._rampen, »200 + n = Rampe Nr. n
            // aus sec21«, 85 von 85 gemessen), und an ihr haengt schon das
            // ENTLADEN eines Schiffes (RampeEntladen, >= 200, @0x409383).
            // Ohne diese Zeile waere die neue Mole die einzige Rampe der Karte,
            // auf der kein Schiff abladen darf.
            _rampen[col * 1024 + row] = 200 + (_moleSaetze.Count - 1);
            _rampenKachel[col * 1024 + row] = 10723 + r.Bild;
            MoleGebaut++;
            MoleNote = $"Mole ({col},{row}) Richtung {d}, Bild {r.Bild} "
                     + $"(Kachel {10723 + r.Bild}), TP {r.Tp} — Zelle befahrbar";
            Audio.GameSounds.PlayAt(42, col, row);
        }
        GD.Print($"mole: {MoleNote}");
        // ⭐ Der Pionier verarbeitet seine eigenen Bestandteile — gelöscht, kein
        // Wrack, kein Merker (Lexikon Seite 99, 0x433C20 + 0x410E60). ⚠ KEIN
        // Todesprotokoll und KEIN Zasah: er stirbt nicht, er wird verbraucht.
        // ⚠⚠ 12.09.2026, seine Meldung: »er stirbt/faellt wieder um anstatt
        // einfach zu verschwinden wie im original«. `Dead = true` allein macht
        // eine LEICHE — der Zeichner spielt dann die Sterbebilder 12..14. Das
        // Original ruft hier aber 0x410E60, das die Einheit ENTFERNT: kein
        // Umfallen, kein Wrack, kein Todesprotokoll. Dafuer gibt es jetzt
        // Entity.Verbraucht.
        e.Verbraucht = true;
        e.Hp = 0; e.Dead = true; e.DeadTime = 0; e.Path = null; e.Target = -1;
        _nav?.ClearOccupant(col, row, idx);
    }

    /// <summary>Die Rampe fällt — <c>Destroy ramp</c> 0x4CBAB0: die Zelle wird
    /// wieder rau. ⚠ Das Trümmerbild (10747 + Bild) zeichnen wir nicht.</summary>
    public void RampeTrifft(int col, int row, int schaden)
    {
        var r = RampeAn(col, row);
        if (r == null) return;
        r.Tp -= schaden;
        if (r.Tp > 0) return;
        _moleSaetze.Remove(r);
        _nav?.RampeSetzen(col, row, false);
        _rampen.Remove(col * 1024 + row);
        _rampenKachel.Remove(col * 1024 + row);
        GD.Print($"mole: Mole ({col},{row}) zerstoert — Zelle wieder rau");
    }

    /// <summary>
    /// <b>DIE GEBAUTE MOLE ZEICHNEN</b> (12.09.2026, bug-219).
    ///
    /// <para>⚠⚠ Seine Meldung, nachdem der Bau schon lief: »ich habe die
    /// landungsrampen aber nicht gesehen«. Und er hat recht — eine Mole, die
    /// erst im SPIEL entsteht, hat im gebackenen Kartenbild keine Pixel: das
    /// Bild der Karte entsteht beim Import.</para>
    ///
    /// <para>Gelöst wie bei den verkohlten Bäumen: der Importeur legt die zwölf
    /// Kacheln <c>10723…10734</c> in den Streifen unten an
    /// <c>&lt;karte&gt;.objects.png</c>, und hier wird daraus gemalt. Gesucht
    /// wird nach dem CODE, denn auf eine im Spiel gebaute Mole zeigt kein
    /// Eintrag.</para>
    ///
    /// <para>⚠ Eine Karte aus einem ÄLTEREN Import hat den Streifen nicht. Dann
    /// bleibt die Mole unsichtbar — sie WIRKT trotzdem, und Stufe 5 des
    /// Prüfstands sagt es. Kein Loch, keine erfundene Grafik.</para></summary>
    public int MolenGezeichnet;

    private void ZeichneMolen()
    {
        if (_moleSaetze.Count == 0 || ObjektEbene is not { } tex) return;
        MolenGezeichnet = 0;
        foreach (var r in _moleSaetze)
        {
            // Die Schadensstufe steckt im Bild: 10723 + Richtung + 8*Stufe.
            int stufe = Mathf.Clamp((200 - r.Tp) / 67, 0, 2);
            if (StreifenKachel(Import.MapBaker.RampenKachelBasis + r.Bild + 8 * stufe)
                is not { } k) continue;
            var ziel = CellRect(_ox, _oy, r.Col, r.Row, ElevOf(r.Col, r.Row));
            // ⚠⚠ 12.09.2026 — HIER FEHLTE DER BLITANCHOR, und seine Meldung war
            // »die rampe wird mitten im wasser gebaut«. Der Bauplatz stimmte,
            // das BILD sass 50 Punkte zu tief — gut eine Zelle, also genau ins
            // Wasser daneben. Das Backen rechnet
            //     y = OriginY + row·TileH − elev·ElevStep + BlitAnchor + YOff
            // (Import/MapBaker.cs:283, BlitAnchor = −50), und wer die Kachel
            // zur Laufzeit setzt, muss dieselbe Rechnung nehmen. Mit YOff 45
            // bleibt netto −5, nicht +45.
            DrawTextureRectRegion(tex,
                new Rect2(new Vector2(ziel.Position.X,
                                      ziel.Position.Y + Import.MapBaker.BlitAnchor + k.YOff),
                          k.Feld.Size), k.Feld);
            MolenGezeichnet++;
        }
    }

    /// <summary>
    /// <c>--mole-check</c> — <b>DIE GANZE KETTE, wirklich gefahren.</b>
    ///
    /// <para>Gemessen wird in vier Stufen, und jede kann für sich scheitern:</para>
    /// <list type="number">
    ///   <item><b>das Menü</b> — trägt ein Pionier die vier Zeilen
    ///     <c>0x0C, 0x20, 0x0D, 0x0E</c>, und NICHT Handsteuerung/Verkaufen/
    ///     Eingraben? (Das ist die Gattungsweiche, bug-219.)</item>
    ///   <item><b>die Gültigkeit</b> — wie viele Zellen der Karte nehmen eine
    ///     Mole, und weist die Probe raue Zellen ohne Uferböschung ab?</item>
    ///   <item><b>der Bau</b> — Pionier auf eine gültige Zelle, Auftrag 2, Takte
    ///     laufen lassen: wird die Zelle BEFAHRBAR, steht sie in der Lagentafel
    ///     als <c>≥ 200</c> (Schiffe dürfen abladen), und ist der Pionier
    ///     verbraucht?</item>
    ///   <item><b>der Abriss</b> — 200 Schaden, und die Zelle muss wieder rau
    ///     sein.</item>
    /// </list>
    /// <para>⚠ Der Lauf GREIFT EIN: er versetzt einen Pionier auf eine gültige
    /// Zelle, weil er sonst erst hinlaufen müsste. Das steht in der Ausgabe.
    /// Nullmodell ist <c>--fussvolkmenue-alt</c>: dann ist schon Stufe 1
    /// leer.</para></summary>
    public string MoleCheckLine()
    {
        var sb = new System.Text.StringBuilder("mole-check\n");
        sb.Append($"  Gegenschalter --rampe-bauzeit: {RampeBauzeit}, "
                + $"--fussvolkmenue-alt: {FussvolkmenueAlt}\n");

        // ---- 1) das Menue -------------------------------------------------
        int pion = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsBuilding && !e.IsProp && IstPionier(e)) { pion = i; break; }
        }
        if (pion < 0) return sb.Append("  kein Pionier auf dieser Karte — NICHT GEMESSEN").ToString();
        var codes = MenueCodes(pion);
        bool menueOk = codes[0] == CodeAusbessern && codes[1] == CodeFussLaufen
                    && codes[2] == CodeBruecke && codes[3] == CodeMole
                    && codes[6] == -1 && codes[7] == -1;
        sb.Append($"  1. Menue des Pioniers (Platz {_entities[pion].Slot}, Bildsatz {_entities[pion].Infantry}, +0x0B "
                + $"{WaffenUntertyp(_entities[pion])}): [{string.Join(", ", codes)}] — "
                + $"{(menueOk ? "0x0C/0x20/0x0D/0x0E, ohne Handsteuerung und Verkaufen (richtig)" : "FALSCH")}\n");

        // ---- 2) die Gueltigkeit -------------------------------------------
        int gut = 0, rauOhneBoeschung = 0;
        Vector2I platz = new(-1, -1);
        if (_nav != null)
            for (int r = 0; r < _nav.Height; r++)
                for (int c = 0; c < _nav.Width; c++)
                {
                    if (_nav.GroundAt(c, r) != Simulation.NavGrid.Ground.Rough) continue;
                    if (MolePlatzOk(c, r)) { gut++; if (platz.X < 0) platz = new Vector2I(c, r); }
                    else rauOhneBoeschung++;
                }
        sb.Append($"  2. Gueltigkeit: {gut} Zellen nehmen eine Mole, {rauOhneBoeschung} raue "
                + $"Zellen weist die Probe ab (Uferboeschung fehlt oder belegt)\n");
        if (platz.X < 0)
            return sb.Append("  keine gueltige Zelle auf dieser Karte — NICHT GEMESSEN").ToString();

        // ---- 3) der Bau ----------------------------------------------------
        var p = _entities[pion];
        int altC = p.Col, altR = p.Row;
        _nav?.ClearOccupant(altC, altR, pion);
        p.Col = platz.X; p.Row = platz.Y; p.Path = null;
        // ⚠⚠ 12.09.2026 — DER PIONIER MUSS AUF DER ZELLE WIRKLICH DRAUFSTEHEN.
        // Seine Meldung »ich sehe keine rampe bzw. baut er keine«: die
        // Bauprüfung benutzte die Vorschauregel (»Zelle muss LEER sein«), und
        // der Pionier belegt sie ja selbst. Der Pruefstand hat das NICHT
        // gesehen, weil er die Zelle vorher geraeumt und den Belegungseintrag
        // nie wieder gesetzt hat — er prüfte einen Fall, den es im Spiel nicht
        // gibt. Jetzt steht er wirklich darauf.
        _nav?.SetOccupant(platz.X, platz.Y, pion);
        p.Bauart = 2; p.BauZelle = platz; p.BauTakte = 0;
        sb.Append($"  ⚠ Eingriff: Pionier von ({altC},{altR}) auf die gueltige Zelle "
                + $"({platz.X},{platz.Y}) versetzt\n");
        // ⚠ Der Eingriff, benannt: die Uhr laeuft im Pruefstand nicht von
        // selbst, also drehen wir sie je Takt um ein Bild weiter — sonst zeigte
        // die Arbeitsanimation immer dasselbe Bild, und Stufe 6 waere blind.
        float uhrVor = _clock;
        var bilder = new System.Collections.Generic.HashSet<int>();
        int takte = 0;
        while (p.Bauart != 0 && !p.Dead && takte < 400)
        {
            MoleArbeitTick(pion, p);
            if (!p.Dead && p.Bauart != 0) bilder.Add(InfBlock(p));
            _clock += 0.05f;
            takte++;
        }
        _clock = uhrVor;
        bool befahrbar = _nav?.CanEnter(platz.X, platz.Y, Simulation.NavGrid.MoveClass.Vehicle) == true;
        var bauwerk = BauwerkAn(platz.X, platz.Y);
        bool entladen = RampeEntladen(platz.X, platz.Y);
        bool baOk = MoleGebaut > 0 && befahrbar && p.Dead && bauwerk.Art == 2 && entladen;
        sb.Append($"  3. Bau nach {takte} Takten: Molen {MoleGebaut}, Zelle befahrbar "
                + $"{befahrbar}, Lagentafel Art {bauwerk.Art} Nr {bauwerk.Nr} "
                + $"(Schiff darf abladen: {entladen}), Pionier verbraucht {p.Dead} — "
                + $"{(baOk ? "richtig" : "FALSCH")}\n  {MoleNote}\n");

        // ---- 4) der Abriss --------------------------------------------------
        RampeTrifft(platz.X, platz.Y, 200);
        bool wiederRau = _nav?.GroundAt(platz.X, platz.Y) == Simulation.NavGrid.Ground.Rough;
        sb.Append($"  4. Abriss (200 Schaden): Zelle wieder rau {wiederRau}, "
                + $"Rampen jetzt {_moleSaetze.Count} — {(wiederRau ? "richtig" : "FALSCH")}\n");

        var probe = StreifenKachel(Import.MapBaker.RampenKachelBasis);
        string bild = probe == null
            ? "FEHLT — diese Karte kommt aus einem Import VOR dem 12.09.2026; die Mole "
              + "wirkt, ist aber unsichtbar. Karten neu einlesen."
            : $"da ({probe.Value.Feld.Size.X}x{probe.Value.Feld.Size.Y}, Anschlag "
              + $"{probe.Value.YOff}) — die Mole wird gezeichnet";
        bool animOk = bilder.Count > 1;
        foreach (int b in bilder) if (System.Array.IndexOf(InfWorkBlocks, b) < 0) animOk = false;
        sb.AppendLine($"  6. Bauanimation: Bloecke [{string.Join(",", bilder)}] "
                    + (BauanimationAus ? "(--bauanimation-aus)"
                       : animOk ? "— der Pionier arbeitet sichtbar (richtig)"
                       : "— FALSCH, erwartet mehrere aus {8,9,10}"));
        sb.AppendLine("  5. Kachelbild: Streifenkachel "
                    + Import.MapBaker.RampenKachelBasis + " " + bild);
        if (FussvolkmenueAlt)
            sb.Append(menueOk
                ? "  ⚠ NULLMODELL OHNE BEFUND — mit --fussvolkmenue-alt duerfte das Menue "
                + "die vier Zeilen NICHT tragen"
                : "  NULLMODELL WIE ERWARTET: ohne die Gattungsweiche hat der Pionier "
                + "die vier Zeilen nicht");
        else
            sb.Append(menueOk && baOk && wiederRau && (animOk || BauanimationAus)
                      ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
