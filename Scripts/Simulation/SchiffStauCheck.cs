namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐⭐ <c>--schiffstau-check</c> und der <b>BELEGUNGSABGLEICH</b> (24.08.2026).
///
/// <para>⚠⚠ Gemeldet: »die grossen 2 Boote (Kreuzer und Schlachtschiff) lassen
/// sich nicht nach unten fahren, ich bekomme sie da oben nicht aus der Ecke
/// raus. Was komisch ist, der kleine leichte Kreuzer der unterhalb der 2 grossen
/// Schiffe ist, kann nicht mehr unterhalb vom Hafen langfahren, als würde da was
/// blockieren.«</para>
///
/// <para><b>Warum ZWEI Messungen und nicht eine.</b> Die Meldung nennt zwei
/// Symptome, die verschiedene Ursachen haben können: »kommt nicht aus der Ecke«
/// kann Gelände sein (ein 4×4-Rumpf braucht sechzehn Wasserzellen und findet in
/// einer Bucht keine), »als würde da was blockieren« klingt nach einem
/// Stempel, der liegengeblieben ist. Das erste misst
/// <see cref="SchiffStauStart"/>, das zweite <see cref="BelegungAbgleich"/> —
/// und der Abgleich braucht kein Schiff und keine Meldung, er gilt für jede
/// Karte und jeden Takt.</para>
///
/// <para>⚠ <b>Was hier GESETZT und nicht gebaut wird:</b> die Schiffe werden auf
/// die Zellen gestellt, die das Original als Dockausfahrt vorsieht — Gattung 4
/// nach <c>(Spalte−2, Zeile+2)</c> / <c>(Spalte+5, Zeile+2)</c>, Gattung 5 nach
/// <c>(Spalte−4, Zeile+1)</c> / <c>(Spalte+5, Zeile+1)</c> (@0x43F730, siehe
/// <c>LaunchShip</c>). Geprüft wird damit das FAHREN, nicht das Auslaufen; wer
/// das Auslaufen prüfen will, nimmt <c>--demo-ship</c>. Das steht hier, weil
/// eine gesetzte Lage eine Annahme ist und als solche in der Ausgabe gehört.
/// </para>
/// </summary>
public partial class MapEntityLayer
{
    private bool _stauOn;
    private readonly List<int> _stauSchiffe = new();
    private readonly Dictionary<int, (Vector2I Start, Vector2I Ziel, int Seite, int Gattung)> _stauPlan = new();
    private string _stauKopf = "";

    /// <summary>Was der letzte <see cref="BelegungAbgleich"/> gefunden hat —
    /// Phantome + Loecher + Ueberdeckungen. Soll 0 sein.</summary>
    private int _belegungFehler;

    /// <summary>
    /// <c>--belegung-check[=sekunden]</c> — der Abgleich ALLEIN, ohne
    /// Schiffsszenario.
    ///
    /// <para>⭐ Er braucht keine Meldung und kein Schiff: er gilt auf jeder
    /// Karte und in jedem Takt, und er ist damit der Wächter für eine ganze
    /// Fehlerklasse statt für einen Fall. Genau das hat am 24.08. gefehlt — der
    /// Fehler war vier Tage alt und keine der 104 Messungen konnte ihn sehen.
    /// </para>
    /// </summary>
    public string BelegungCheckLine()
    {
        var s = BelegungAbgleich("am Ende des Laufs");
        return "belegung-check\n" + s
             + (_belegungFehler == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
    }

    /// <summary>Die zwei Ausfahrten je Gattung, aus <c>LaunchShip</c> — dort
    /// sind sie gelesen (@0x43F730) und über 39 Seedocks nachgezählt.</summary>
    private static IEnumerable<Vector2I> DockAusfahrten(Entity dock, int gattung)
    {
        if (gattung == 5)
        {
            yield return new Vector2I(dock.Col - 4, dock.Row + 1);
            yield return new Vector2I(dock.Col + 5, dock.Row + 1);
        }
        else
        {
            yield return new Vector2I(dock.Col - 2, dock.Row + 2);
            yield return new Vector2I(dock.Col + 5, dock.Row + 2);
        }
    }

    public string SchiffStauStart()
    {
        if (_nav == null) return "schiffstau-check: kein Gitter";
        _stauSchiffe.Clear(); _stauPlan.Clear();

        // Der Hafen. ⚠ Nicht der erste beliebige: er muss eine Ausfahrt haben,
        // sonst misst der Pruefstand das Dock und nicht das Fahren.
        int di = -1;
        for (int i = 0; i < _entities.Count; i++)
            if (IsDock(_entities[i]) && !_entities[i].Dead) { di = i; break; }
        if (di < 0) return "schiffstau-check: kein Hafen auf dieser Karte";
        var dock = _entities[di];

        var sb = new System.Text.StringBuilder();
        sb.Append($"schiffstau-check: Hafen '{dock.Name}' P{dock.Owner} ({dock.Col},{dock.Row})\n");
        // ⚠⚠ 24.08.2026 — DIE HAFENFRAGE GEHOERT HIERHIN, VOR DAS AUFSTELLEN.
        // Sie stand am Ende, und dort standen auch die drei Schiffe dieses
        // Pruefstands: das Ziel (159,107) lag MITTEN IN Schiff Nr. 76, und die
        // Wegsuche meldete »kein Weg« ueber fuenf Zellen offenes Wasser.
        // ⭐ Das war der DRITTE Fall an einem Tag, in dem dieser Pruefstand sich
        // selbst gemessen hat. Die Reihenfolge IST die Behebung: was die KARTE
        // hergibt, wird gefragt, solange nichts darauf steht.
        sb.Append(HafenDurchfahrt()).Append('\n');
        sb.Append("  ⚠ die Schiffe werden auf die GELESENEN Dockausfahrten GESETZT, "
                + "nicht gebaut — geprueft wird das Fahren\n");

        // Zwei Gattung 5 (Kreuzer 158, Schlachtschiff 157) und ein Gattung 4
        // (L.Kreuzer 151) — genau die drei aus der Meldung.
        foreach (int chassis in new[] { 158, 157, 151 })
        {
            int gattung = TypeOfChassis(chassis);
            int seite = Simulation.NavGrid.HullSide(gattung);
            Vector2I? platz = null;
            foreach (var p in DockAusfahrten(dock, gattung))
            {
                if (!HullPasst(p, seite)) continue;
                platz = p; break;
            }
            // Beide Ausfahrten verstellt? Dann die naechste Wasserflaeche, die
            // den Rumpf traegt — und das gehoert gesagt.
            string wie = platz != null ? "Ausfahrt" : "Ersatzplatz";
            platz ??= NaechsterRumpfplatz(new Vector2I(dock.Col, dock.Row + 1), seite);
            if (platz == null)
            {
                sb.Append($"  Rumpf {chassis} (Gattung {gattung}, {seite}x{seite}): "
                        + "KEIN Platz, der den Rumpf traegt — nicht gesetzt\n");
                continue;
            }

            int idx = StauSchiffSetzen(chassis, gattung, seite, platz.Value, dock);
            _stauSchiffe.Add(idx);
            sb.Append($"  Rumpf {chassis} (Gattung {gattung}, {seite}x{seite}) auf "
                    + $"({platz.Value.X},{platz.Value.Y}) [{wie}] als Nr. {idx}\n");
        }
        if (_stauSchiffe.Count == 0) return sb.Append("  nichts gesetzt — nicht gemessen").ToString();

        // ⭐⭐ WO VERLAESST EIN SCHUSS DIESE ZWEI SCHIFFE? Gemeldet am
        // 24.08.2026: »der Abschuss der Rakete kommt von ausserhalb des
        // Schiffs, anstatt von der Mitte wo die Waffe sitzt«, und dazu »die
        // Rakete ist da nicht drauf, die er abschiesst«. Beides ist hier
        // messbar, und nur hier — auf keiner Karte steht ein 157 oder 158.
        foreach (int idx in _stauSchiffe) sb.Append(MuendungsZeile(idx));

        // ⭐ Der Abgleich VOR der Fahrt: eine ueberlappende Aufstellung ist
        // selbst schon der Fehler, und dann sagt das Fahren nichts mehr.
        sb.Append(BelegungAbgleich("nach dem Setzen, vor der Fahrt"));
        sb.Append(SeeKarte(new Vector2I(dock.Col, dock.Row + 10), 76, 34));

        // Und jetzt NACH UNTEN — die Richtung aus der Meldung.
        foreach (int idx in _stauSchiffe)
        {
            var s = _entities[idx];
            int seite = _nav.HullOf(idx);
            var start = new Vector2I(s.Col, s.Row);
            var ziel = WeitesteZelleNachUnten(start, seite, idx);
            _stauPlan[idx] = (start, ziel, seite, s.GameUnitType);
            if (ziel == start)
            {
                sb.Append($"  Nr. {idx}: schon der ERSTE Schritt nach unten geht nicht — "
                        + $"{_nav.WarumGesperrt(start.X, start.Y + 1, s.Move, idx)}\n");
                continue;
            }
            var weg = _nav.FindPath(start, ziel, s.Move, idx);
            if (weg == null || weg.Count == 0)
            {
                sb.Append($"  Nr. {idx}: kein WEG von ({start.X},{start.Y}) nach "
                        + $"({ziel.X},{ziel.Y}), obwohl das Ziel den Rumpf traegt\n");
                continue;
            }
            s.Path = weg; s.PathIdx = 0; s.Goal = ziel; s.Reserved = null;
            s.Block = BlockEnter + Simulation.Determinism.Roll(BlockEnterSpread);
            sb.Append($"  Nr. {idx}: Auftrag ({start.X},{start.Y}) -> ({ziel.X},{ziel.Y}), "
                    + $"{weg.Count} Zellen\n");
        }
        _stauOn = true;
        _stauKopf = sb.ToString();
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// <b>Liegt die Muendung auf dem Rumpfbild, und hat das Geschoss ein
    /// Flugbild?</b> — dieselben zwei Fragen wie <c>--schiff-waffe-check</c>,
    /// aber fuer ein Schiff, das hier gerade GESETZT wurde.
    /// <para>⚠ Gemessen wird an den PIXELN des Rumpfbildes und mit demselben
    /// Anker, mit dem der Zeichner es absetzt — nicht an einer Rechnung
    /// daneben.</para></summary>
    private string MuendungsZeile(int idx)
    {
        if (idx >= _entities.Count) return "";
        var e = _entities[idx];
        int art = e.Weapon > 0 ? Simulation.DesignMath.SoundClass(WeaponRowOf(e.Weapon)) : -1;
        string flug = art < 0 ? "keine Waffe"
                    : FlightKind(art) is { } f
                        ? $"Art {art}, Flugbild \"{f}\""
                        : $"Art {art}, ⚠⚠ OHNE FLUGBILD — das Geschoss ist unsichtbar";
        var sb = new System.Text.StringBuilder(
            $"  Nr. {idx} Rumpf {e.UnitType}, Waffe {e.Weapon}: {flug}\n");

        var hull = GetHullTexture(e.UnitType, e.Facing, PoseOf(e), SlopeClassOf(e.Col, e.Row));
        if (hull == null) return sb.Append("    (kein Rumpfbild — nicht messbar)\n").ToString();
        var img = hull.GetImage();
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int yy = 0; yy < img.GetHeight(); yy++)
            for (int xx = 0; xx < img.GetWidth(); xx++)
            {
                if (img.GetPixel(xx, yy).A <= 0.5f) continue;
                if (xx < x0) x0 = xx;
                if (xx > x1) x1 = xx;
                if (yy < y0) y0 = yy;
                if (yy > y1) y1 = yy;
            }
        if (x1 < 0) return sb.Append("    (Rumpfbild ganz durchsichtig)\n").ToString();
        var ecke = PictureAnchor(e) - new Vector2(30, 55);      // ComposedAnchor
        var sicht = new Rect2(ecke + new Vector2(x0, y0), new Vector2(x1 - x0 + 1, y1 - y0 + 1));
        var muendung = ShotOrigin(e) + new Vector2(1, 0) * MuzzleReach;
        bool drauf = sicht.HasPoint(muendung);
        float weit = drauf ? 0f
            : Mathf.Max(Mathf.Max(sicht.Position.X - muendung.X, muendung.X - sicht.End.X),
                        Mathf.Max(sicht.Position.Y - muendung.Y, muendung.Y - sicht.End.Y));
        sb.Append($"    Muendung ({muendung.X:0},{muendung.Y:0}), sichtbarer Rumpf "
                + $"({sicht.Position.X:0},{sicht.Position.Y:0})..({sicht.End.X:0},{sicht.End.Y:0}): "
                + (drauf ? "✔ AUF dem Schiff"
                         : $"⚠⚠ {weit:0} px DANEBEN ({weit / TileW:0.00} Felder)") + "\n");
        if (!drauf) _belegungFehler++;
        return sb.ToString();
    }

    /// <summary>Traegt diese Zelle einen Rumpf der Kantenlaenge
    /// <paramref name="seite"/> — GANZ, wie <c>Can_go</c> es prueft?
    ///
    /// <para>⚠⚠ 24.08.2026 — HIER STAND KEIN <paramref name="mover"/>, und das
    /// war der erste Fehler dieses Pruefstands. <c>IsFree</c> ohne Beweger
    /// rechnet mit Rumpfbreite 1 <b>und ohne Selbstausnahme</b>: die eigenen
    /// Stempel des Schiffes zaehlten als fremde Belegung, also war jede Zelle
    /// unter dem Schiff »besetzt«. Der Pruefstand meldete darum »kommt keinen
    /// Schritt nach unten«, waehrend <c>WarumGesperrt</c> zwei Zeilen weiter
    /// »frei« sagte.
    ///
    /// ⭐ Genau dieser Widerspruch hat den Fehler verraten — zwei Wege zur
    /// selben Frage, und sie waren verschieden gebaut. Ein Pruefstand mit nur
    /// EINEM Weg haette hier eine falsche Ursache belegt und mich in den
    /// Wegcode geschickt.</para></summary>
    private bool HullPasst(Vector2I p, int seite, int mover = -1)
        => _nav != null
           && _nav.AskRumpf(p.X, p.Y, Simulation.NavGrid.MoveClass.Ship, mover, seite)
              == Simulation.NavGrid.Step.Free;

    /// <summary>
    /// Dieselbe Frage, aber <b>nur ans GELAENDE</b>: traegt diese Stelle den
    /// Rumpf, wenn keine einzige Einheit im Weg stuende?
    ///
    /// <para>⚠⚠ 24.08.2026 — der dritte Fehler dieses Pruefstands, und ein
    /// lehrreicher: <see cref="HafenDurchfahrt"/> behauptete im eigenen
    /// Kommentar, ohne fremde Schiffe zu messen (»Beweger −1«), und tat es
    /// nicht — <c>Ask</c> sieht mit Beweger −1 sehr wohl jeden Stempel. Auf
    /// map_NET02 parkten die DREI SCHIFFE DIESES PRUEFSTANDS in der Fahrrinne,
    /// und er meldete »gar kein Rumpf kommt durch«. Er hat sich selbst
    /// gemessen.</para>
    ///
    /// <para>⭐ Die Lehre: ein Kommentar ist keine Zusicherung. Wenn eine
    /// Messung »nur Gelaende« behauptet, muss sie einen Weg nehmen, der
    /// Einheiten gar nicht sehen KANN — nicht einen, von dem man annimmt, dass
    /// er keine sieht.</para></summary>
    private bool HullPasstGelaende(Vector2I p, int seite)
    {
        if (_nav == null) return false;
        for (int dy = 0; dy < seite; dy++)
            for (int dx = 0; dx < seite; dx++)
                if (!_nav.CanEnter(p.X + dx, p.Y + dy, Simulation.NavGrid.MoveClass.Ship))
                    return false;
        return true;
    }

    /// <summary>
    /// ⭐ <b>DIE SEEKARTE, wie das Gitter sie sieht</b> — ein Bild in Zeichen.
    ///
    /// <para>⚠ Aus der Meldung »als würde da was blockieren«: die entscheidende
    /// Frage ist, ob die GESPERRTE Fläche mit dem übereinstimmt, was am
    /// Bildschirm zu sehen ist. Eine Zahl kann das nicht sagen, ein Bild schon.
    /// <c>~</c> Wasser · <c>,</c> Land · <c>#</c> gesperrt · <c>D</c> das Dock ·
    /// Ziffern die Rümpfe der gesetzten Schiffe.</para>
    /// </summary>
    private string SeeKarte(Vector2I mitte, int breite, int hoehe)
    {
        if (_nav == null) return "";
        var marke = new Dictionary<(int, int), char>();
        for (int k = 0; k < _stauSchiffe.Count; k++)
        {
            int idx = _stauSchiffe[k];
            if (idx >= _entities.Count) continue;
            var s = _entities[idx];
            int seite = _nav.HullOf(idx);
            for (int dy = 0; dy < seite; dy++)
                for (int dx = 0; dx < seite; dx++)
                    marke[(s.Col + dx, s.Row + dy)] = (char)('1' + k);
        }
        int x0 = mitte.X - breite / 2, y0 = mitte.Y - hoehe / 2;
        var sb = new System.Text.StringBuilder();
        sb.Append($"    Seekarte um ({mitte.X},{mitte.Y}), links oben ({x0},{y0}); "
                + "~ Wasser  , Land  # gesperrt  1..n Ruempfe\n");
        for (int r = y0; r < y0 + hoehe; r++)
        {
            sb.Append("      ");
            for (int c = x0; c < x0 + breite; c++)
            {
                if (marke.TryGetValue((c, r), out char m)) { sb.Append(m); continue; }
                string g = _nav.GroundWord(c, r);
                sb.Append(g switch
                {
                    "Wasser" => '~',
                    "Land" => ',',
                    "rau" => ':',
                    "ausserhalb" => ' ',
                    _ => '#',
                });
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Die naechstgelegene Zelle, die den ganzen Rumpf traegt — eine
    /// Ringsuche um <paramref name="um"/>, damit ein Ersatzplatz nicht
    /// irgendwo auf der Karte landet.</summary>
    private Vector2I? NaechsterRumpfplatz(Vector2I um, int seite)
    {
        if (_nav == null) return null;
        for (int rad = 0; rad < 40; rad++)
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != rad) continue;
                    var p = new Vector2I(um.X + dx, um.Y + dy);
                    if (HullPasst(p, seite)) return p;
                }
        return null;
    }

    /// <summary>Wie weit kommt dieser Rumpf GERADE NACH UNTEN, wenn nur das
    /// Gelaende und die Stempel zaehlen? Die Zelle, ab der es nicht weitergeht,
    /// minus eins — und das ist zugleich die Antwort auf »aus der Ecke raus«.
    /// </summary>
    private Vector2I WeitesteZelleNachUnten(Vector2I start, int seite, int mover)
    {
        var letzte = start;
        for (int k = 1; k < 60; k++)
        {
            var p = new Vector2I(start.X, start.Y + k);
            if (!HullPasst(p, seite, mover)) break;
            letzte = p;
        }
        return letzte;
    }

    /// <summary>Ein Schiff setzen — dieselben Felder, die <c>LaunchShip</c>
    /// fuellt, und <b>Rumpf vor Stempel</b> (siehe <c>NavGrid.SetHull</c>).
    /// </summary>
    private int StauSchiffSetzen(int chassis, int gattung, int seite, Vector2I p, Entity dock)
    {
        var d = _shipDesigns?.FirstOrDefault(x => x.Chassis == chassis);
        int hp = d != null && d.Energie > 0 ? d.Energie : HpOfType(chassis);
        var u = new Entity
        {
            Slot = -1, Col = p.X, Row = p.Y, Owner = dock.Owner, Team = dock.Team,
            UnitType = chassis, GameUnitType = gattung, Mobile = true,
            // ⚠ Die Waffe gehoert dazu: Rumpf 157 und 158 stehen auf KEINER
            // Karte, also kann --schiff-waffe-check sie nie sehen. Ohne sie
            // waere die Muendungsfrage fuer genau die zwei Schiffe, um die es
            // geht, nicht messbar. Dieselbe Zeile wie LaunchShip.
            Weapon = d != null ? (d.WeaponComp > 0 ? d.WeaponComp : ShipTurretOf(d.Weapon)) : 0,
            Ammo = 60, AmmoMax = 60,
            // ⚠⚠ 24.08.2026 — DIE HIER FEHLTEN, und das war der zweite Fehler
            // dieses Pruefstands. `FootW/FootH` entscheiden ueber BodyCenterAt,
            // also darueber, WO das Schiff gezeichnet wird; `SetHull` darueber,
            // WELCHE Zellen es belegt. Ein Schiff mit 1x1-Grundriss und
            // 4x4-Rumpf wird eine Zelle weit oben links gezeichnet und sperrt
            // unten rechts — genau das Bild, das die Meldung beschreibt.
            // ⭐ `LaunchShip` setzt beide (Zeile »FootW = seite, FootH = seite«)
            // und prueft es danach sogar nach (ShipExitReport). Der Pruefstand
            // tat es nicht — er haette also einen Fehler VORGETAEUSCHT, den das
            // Spiel nicht hat.
            FootW = seite, FootH = seite,
            Hp = hp, HpMax = hp, Elev = ElevOf(p.X, p.Y),
            Name = d?.Name ?? $"Rumpf {chassis}",
            Speed = d != null && d.Speed > 0 ? d.Speed : 10,
            Facing = DefaultFacing * (FacingsOf(chassis) / 8),
            Move = Simulation.NavGrid.MoveClass.Ship,
            Footprint = CellRect(_ox, _oy, p.X, p.Y, ElevOf(p.X, p.Y)),
        };
        u.Pos = BodyCenterAt(u, u.Col, u.Row);
        _entities.Add(u);
        int idx = _entities.Count - 1;
        _nav!.SetHull(idx, seite);
        _nav.SetOccupant(p.X, p.Y, idx);
        return idx;
    }

    /// <summary>
    /// ⭐⭐ <b>DER BELEGUNGSABGLEICH</b> — stimmt das Stempelgitter mit der
    /// Einheitenliste überein?
    ///
    /// <para>Drei Fragen, und jede hat ihre eigene Fehlerklasse:</para>
    /// <list type="number">
    /// <item><b>Phantom</b> — eine Zelle ist gestempelt, aber keine lebende
    /// Einheit deckt sie mit ihrem Rumpf. Das ist der »als würde da was
    /// blockieren«-Fall: nichts zu sehen, und trotzdem gesperrt.</item>
    /// <item><b>Loch</b> — eine Einheit deckt eine Zelle, in der ihr Stempel
    /// NICHT steht. Das ist der umgekehrte Fehler und genauso schlimm: ein
    /// anderes Schiff fährt hinein.</item>
    /// <item><b>Ueberdeckung</b> — zwei Einheiten decken dieselbe Zelle. Dann
    /// stehen sie ineinander, und der Stempel kann nur einem gehören.</item>
    /// </list>
    ///
    /// <para>⚠ Ein Schritt ist ein Zustand mit ZWEI Ankern: die Einheit hält
    /// ihre Zelle und die vorgemerkte. Beide zählen darum als gedeckt — wer das
    /// vergisst, meldet jede fahrende Einheit als Fehler.</para>
    ///
    /// <para>⚠ Gebäude und Festes bleiben ausserhalb: sie stempeln über einen
    /// anderen Weg (Gebäudekacheln), und ihre Fläche steht nicht in
    /// <c>HullOf</c>. Ihre Zellen werden gezählt und genannt, aber nicht
    /// bewertet — eine Messung, die ihre eigene Grenze nicht nennt, behauptet
    /// mehr als sie weiss.</para>
    /// </summary>
    public string BelegungAbgleich(string wann)
    {
        if (_nav == null) return "  belegung: kein Gitter\n";

        // Was jede lebende, bewegliche Einheit DECKT — Zelle und Vormerkung.
        var deckung = new Dictionary<(int, int), List<int>>();
        int mitRumpf = 0, fussvolk = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.Dead || e.IsBuilding) continue;
            // ⚠⚠ 24.08.2026 — DIE FUSSSOLDATEN GEHOEREN NICHT IN DIESE MESSUNG,
            // und das ist gelesen, nicht bequem.
            //
            // Der Abgleich meldete auf Kampagne 1 eine Ueberdeckung: zwei
            // Einheiten vom Typ 148 auf (39,24). 148 und 149 sind die
            // FUSSSOLDATEN (sie tragen kein Fahrwerk, siehe ImmobileTypes), und
            // das Original stellt sie ausdruecklich uebereinander: `pratelska_infa`
            // @0x433fe0 laesst eine Einheit durch eine Infanteriezelle, wenn
            // alle NEUN Mann darin befreundet sind — neun Mann auf einer Zelle
            // sind dort der Normalfall, kein Fehler.
            //
            // ⭐ Ein Waechter, der den Normalfall anzeigt, wird weggeklickt.
            // Darum fliegen sie raus — aber sie werden GEZAEHLT und genannt,
            // damit die Ausnahme sichtbar bleibt und nicht zur stillen Luecke
            // wird.
            if (e.Infantry >= 0) { fussvolk++; continue; }
            int seite = _nav.HullOf(i);
            if (seite > 1) mitRumpf++;
            var anker = new List<Vector2I> { new(e.Col, e.Row) };
            if (e.Reserved is { } rv) anker.Add(rv);
            foreach (var a in anker)
                for (int dy = 0; dy < seite; dy++)
                    for (int dx = 0; dx < seite; dx++)
                    {
                        var k = (a.X + dx, a.Y + dy);
                        if (!deckung.TryGetValue(k, out var l)) deckung[k] = l = new List<int>();
                        if (!l.Contains(i)) l.Add(i);
                    }
        }

        var phantome = new List<string>();
        var fest = 0;
        var gestempelt = new HashSet<(int, int)>();
        foreach (var (c, r, ent, istFest) in _nav.BelegteZellen())
        {
            if (istFest) { fest++; continue; }
            // dieselbe Ausnahme wie oben, sonst waere jeder Soldat ein Phantom
            if (ent >= 0 && ent < _entities.Count && _entities[ent].Infantry >= 0) continue;
            gestempelt.Add((c, r));
            bool gedeckt = deckung.TryGetValue((c, r), out var l) && l.Contains(ent);
            if (gedeckt) continue;
            bool lebt = ent >= 0 && ent < _entities.Count && !_entities[ent].Dead;
            phantome.Add($"({c},{r}) Nr. {ent}"
                       + (lebt ? $" steht aber auf ({_entities[ent].Col},{_entities[ent].Row})"
                               : " — die Einheit ist tot oder gibt es nicht"));
        }

        var loecher = new List<string>();
        var ueber = new List<string>();
        foreach (var kv in deckung)
        {
            if (kv.Value.Count > 1)
                // ⚠ Nicht nur die Nummern: eine Ueberdeckung kann harmlos sein
                // (ein Fahrgast sitzt auf der Zelle seines Transporters), und
                // das sieht man nur, wenn dabeisteht, WAS da steht.
                ueber.Add($"({kv.Key.Item1},{kv.Key.Item2}) "
                        + string.Join(" + ", kv.Value.Select(x =>
                            $"Nr.{x} ut{_entities[x].UnitType} {_nav.HullOf(x)}x{_nav.HullOf(x)} "
                          + $"auf ({_entities[x].Col},{_entities[x].Row})"
                          + (_entities[x].Reserved is { } rr ? $" vorgemerkt ({rr.X},{rr.Y})" : ""))));
            if (!gestempelt.Contains(kv.Key))
                loecher.Add($"({kv.Key.Item1},{kv.Key.Item2}) von Nr. {kv.Value[0]}");
        }

        _belegungFehler = phantome.Count + loecher.Count + ueber.Count;
        var sb = new System.Text.StringBuilder($"  belegungsabgleich ({wann})\n");
        sb.Append($"    {deckung.Count} gedeckte Zellen von {mitRumpf} Einheiten mit "
                + $"Rumpf > 1, {fest} Zellen von Festem und {fussvolk} Fusssoldaten "
                + "(beides nicht bewertet)\n");
        sb.Append($"    Phantome (gestempelt, niemand deckt): {phantome.Count}"
                + (phantome.Count > 0 ? "  ⚠⚠ " + Kurz(phantome) : "  ✔") + "\n");
        sb.Append($"    Loecher (gedeckt, kein Stempel): {loecher.Count}"
                + (loecher.Count > 0 ? "  ⚠⚠ " + Kurz(loecher) : "  ✔") + "\n");
        sb.Append($"    Ueberdeckungen (zwei Einheiten auf einer Zelle): {ueber.Count}"
                + (ueber.Count > 0 ? "  ⚠⚠ " + Kurz(ueber) : "  ✔") + "\n");
        return sb.ToString();
    }

    private static string Kurz(List<string> l)
        => string.Join(", ", l.Take(10)) + (l.Count > 10 ? $" … (+{l.Count - 10})" : "");

    /// <summary>
    /// ⭐⭐ <b>KOMMT EIN SCHIFF UNTERHALB DES HAFENS DURCH?</b> — für JEDEN Hafen
    /// der Karte, für 2×2 und 4×4 getrennt.
    ///
    /// <para>⚠ Gemeldet: »der kleine leichte Kreuzer kann nicht mehr unterhalb
    /// vom Hafen langfahren, als würde da was blockieren«. Diese Frage lässt
    /// sich ohne seinen Spielstand beantworten, und darum wird sie hier
    /// beantwortet statt vermutet: die gesperrte Fläche des Hafens kommt aus der
    /// imap der Karte, und ob links und rechts davon ein Rumpf durchpasst, ist
    /// eine Flutfüllung.</para>
    ///
    /// <para>⚠ Die Füllung bleibt UNTERHALB der gesperrten Fläche (Zeilen ab
    /// deren Unterkante). Sonst zählte auch der Weg rings um die halbe Karte als
    /// »kommt durch«, und genau das ist nicht gefragt.</para>
    ///
    /// <para>⚠ Gemessen wird ohne fremde Schiffe im Weg (Beweger −1, nur
    /// Gelände und Festes). Was ein zweites Schiff sperrt, sagt der
    /// Belegungsabgleich — das sind zwei Fragen.</para>
    /// </summary>
    public string HafenDurchfahrt()
    {
        if (_nav == null) return "hafendurchfahrt: kein Gitter";
        var sb = new System.Text.StringBuilder("hafendurchfahrt\n");
        int haefen = 0, engstellen = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var dock = _entities[i];
            if (!IsDock(dock) || dock.Dead) continue;
            haefen++;
            var (c0, r0, c1, r1) = GesperrteFlaeche(dock.Col, dock.Row);
            sb.Append($"  Hafen Nr. {i} P{dock.Owner} Zelle ({dock.Col},{dock.Row}), "
                    + $"gesperrt ({c0},{r0})..({c1},{r1}) = {c1 - c0 + 1}x{r1 - r0 + 1} Zellen\n");
            // ⭐⭐ 24.08.2026 — STATT »ja/nein« DIE ZAHL. Gemeldet: »den Kreuzer
            // bekomme ich nicht am Hafen vorbei«. Kreuzer und Schlachtschiff
            // sind BEIDE 4x4 (Rumpf 158 und 157), also kann die Rumpfgroesse
            // allein den Unterschied nicht erklaeren — was hilft, ist die
            // Breite der Fahrrinne, und die ist eine Zahl.
            //
            // ⚠ Wieviele Wasserzeilen liegen ueberhaupt unter dem Hafen?
            int tiefe = 0;
            for (int r = r1 + 1; r < r1 + 30; r++)
            {
                bool zeileWasser = false;
                for (int c = c0; c <= c1 && !zeileWasser; c++)
                    if (_nav.GroundWord(c, r) == "Wasser") zeileWasser = true;
                if (!zeileWasser) break;
                tiefe++;
            }
            sb.Append($"    Wasser unter dem Hafen: {tiefe} Zeilen tief\n");

            // ⚠⚠ 24.08.2026 — DER ZWEITE WEG ZUR SELBEN FRAGE. Die Zeile
            // »groesster Rumpf« kommt aus einer Flutfuellung, und Flutfuellungen
            // dieses Pruefstands haben sich heute zweimal geirrt. Daneben steht
            // darum eine Zaehlung, die gar nicht fliesst: wieviele gueltige
            // ANKER gibt es unterhalb des Hafens ueberhaupt? Wenn es viele
            // gibt und die Flutfuellung trotzdem »kommt nicht durch« sagt, ist
            // die Flutfuellung schuld und nicht die Karte.
            for (int seite = 2; seite <= 7; seite++)
            {
                int anker = 0;
                for (int r = r1 + 1; r <= r1 + 20; r++)
                    for (int c = c0 - 35; c <= c1 + 35; c++)
                        if (HullPasstGelaende(new Vector2I(c, r), seite)) anker++;
                sb.Append($"      {seite}x{seite}: {anker} gueltige Anker unterhalb\n");
            }

            // ⭐⭐⭐ 24.08.2026 — HIER STAND EINE EIGENE FLUTFUELLUNG, UND SIE
            // WAR FALSCH. Sie meldete auf map_NET02 »groesster Rumpf, der
            // unterhalb durchkommt: 4x4«, waehrend die Zaehlung zwei Zeilen
            // darueber **495 gueltige 7x7-Anker** im selben Bereich fand. Zwei
            // Wege, eine Frage, zwei Antworten — und diesmal log der neue Weg.
            //
            // ⭐ Der Ersatz ist kein dritter Eigenbau, sondern **die Wegsuche
            // des Spiels selbst**. Sie ist ohnehin die einzige, deren Antwort
            // zaehlt: was sie nicht findet, faehrt der Spieler nicht.
            //
            // ⚠ Der Rumpf des Bewegers wird dafuer kurz umgestellt und danach
            // zurueckgesetzt — ein Pruefstand, der den Zustand veraendert und
            // nicht aufraeumt, verfaelscht alles, was nach ihm kommt.
            {
                // ⚠ Ein Beweger, der NIE eine Einheit ist: die Wegsuche
                // vergleicht nur `_occupant[i] == mover`, und diese Nummer steht
                // in keiner Zelle. Sie sieht damit jede fremde Belegung — hier
                // steht aber noch nichts, also ist es die reine Kartenfrage.
                const int Sonde = 1_000_000;
                int groesster = 0;
                for (int seite = 2; seite <= 6; seite++)
                {
                    var links = BandPlatz(c0 - 3 - seite, c0 - 1, r1 + 1, r1 + 14, seite, -1);
                    var rechts = BandPlatz(c1 + 1, c1 + seite + 4, r1 + 1, r1 + 14, seite, +1);
                    if (links == null || rechts == null)
                    {
                        sb.Append($"      {seite}x{seite}: kein Anlauf "
                                + (links == null ? "links" : "rechts") + "\n");
                        continue;
                    }
                    _nav.SetHull(Sonde, seite);
                    var weg = _nav.FindPath(links.Value, rechts.Value,
                                            Simulation.NavGrid.MoveClass.Ship, Sonde);
                    bool durch = weg != null && weg.Count > 0;
                    if (durch) groesster = seite;
                    sb.Append($"      {seite}x{seite}: Wegsuche ({links.Value.X},{links.Value.Y})"
                            + $" -> ({rechts.Value.X},{rechts.Value.Y}): "
                            + (durch ? $"{weg!.Count} Zellen" : "KEIN WEG  ⚠⚠") + "\n");
                }
                _nav.SetHull(Sonde, 1);   // aufraeumen
                sb.Append($"    groesster Rumpf, den die WEGSUCHE unterhalb durchbringt: "
                        + $"{groesster}x{groesster}"
                        + (groesster >= 4 ? "  ✔ auch die zwei grossen Schiffe"
                           : "  ⚠⚠ die grossen (4x4) kommen hier nicht durch") + "\n");
                if (groesster < 4) engstellen++;
            }
        }
        if (haefen == 0) return "hafendurchfahrt: kein Hafen auf dieser Karte — nicht gemessen";
        sb.Append($"  {haefen} Haefen, {engstellen} Durchfahrten gesperrt");
        return sb.ToString();
    }

    /// <summary>Die zusammenhaengende GESPERRTE Flaeche, in der diese Zelle
    /// liegt, als Rechteck. ⚠ Liegt die Zelle selbst nicht auf Gesperrtem, wird
    /// sie zurueckgegeben — dann steht das Dock nicht auf seiner eigenen
    /// Sperrflaeche, und das ist selbst eine Auskunft.</summary>
    private (int, int, int, int) GesperrteFlaeche(int col, int row)
    {
        if (_nav == null || _nav.GroundWord(col, row) != "gesperrt") return (col, row, col, row);
        int c0 = col, r0 = row, c1 = col, r1 = row;
        var gesehen = new HashSet<(int, int)> { (col, row) };
        var stapel = new Stack<(int, int)>();
        stapel.Push((col, row));
        while (stapel.Count > 0)
        {
            var (c, r) = stapel.Pop();
            c0 = Mathf.Min(c0, c); c1 = Mathf.Max(c1, c);
            r0 = Mathf.Min(r0, r); r1 = Mathf.Max(r1, r);
            foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var n = (c + d.Item1, r + d.Item2);
                if (gesehen.Contains(n)) continue;
                if (_nav.GroundWord(n.Item1, n.Item2) != "gesperrt") continue;
                gesehen.Add(n);
                stapel.Push(n);
            }
        }
        return (c0, r0, c1, r1);
    }

    /// <summary>Der erste Anker in einem Streifen, der den Rumpf traegt — von
    /// aussen nach innen gesucht (<paramref name="richtung"/>), damit der
    /// Anlaufpunkt neben dem Hafen liegt und nicht irgendwo.</summary>
    private Vector2I? BandPlatz(int cVon, int cBis, int rVon, int rBis, int seite, int richtung)
    {
        for (int r = rVon; r <= rBis; r++)
            for (int k = 0; k <= Mathf.Abs(cBis - cVon); k++)
            {
                int c = richtung < 0 ? cBis - k : cVon + k;
                if (HullPasstGelaende(new Vector2I(c, r), seite)) return new Vector2I(c, r);
            }
        return null;
    }

    /// <summary>Flutfuellung ueber ANKER, nicht ueber Zellen: von
    /// <paramref name="von"/> nach <paramref name="nach"/>, wobei jeder Anker
    /// den ganzen Rumpf tragen muss. Der Kasten haelt die Suche unterhalb des
    /// Hafens.</summary>
    private bool RumpfKommtDurch(Vector2I von, Vector2I nach, int seite,
                                 int rMin, int rMax, int cMin, int cMax)
    {
        if (_nav == null) return false;
        var gesehen = new HashSet<(int, int)> { (von.X, von.Y) };
        var welle = new List<Vector2I> { von };
        for (int i = 0; i < welle.Count; i++)
        {
            var a = welle[i];
            if (a == nach) return true;
            foreach (var d in Simulation.NavGrid.UrDirs)
            {
                var n = new Vector2I(a.X + d.X, a.Y + d.Y);
                if (n.Y < rMin || n.Y > rMax || n.X < cMin || n.X > cMax) continue;
                if (gesehen.Contains((n.X, n.Y))) continue;
                gesehen.Add((n.X, n.Y));
                if (!HullPasstGelaende(n, seite)) continue;
                welle.Add(n);
            }
        }
        return false;
    }

    public string SchiffStauLine()
    {
        if (!_stauOn || _nav == null) return "schiffstau-check: nicht gestartet";
        var sb = new System.Text.StringBuilder(_stauKopf);
        sb.Append("  --- nach dem Lauf ---\n");
        int fuhren = 0, gelaende = 0, haengen = 0;
        foreach (int idx in _stauSchiffe)
        {
            if (idx >= _entities.Count) continue;
            var s = _entities[idx];
            if (!_stauPlan.TryGetValue(idx, out var p)) continue;
            int gefahren = Mathf.Abs(s.Row - p.Start.Y) + Mathf.Abs(s.Col - p.Start.X);
            int soll = Mathf.Abs(p.Ziel.Y - p.Start.Y);
            sb.Append($"    Nr. {idx} {p.Seite}x{p.Seite} (Gattung {p.Gattung}): "
                    + $"({p.Start.X},{p.Start.Y}) -> ({s.Col},{s.Row}), "
                    + $"{gefahren} Zellen von {soll} gewollt");
            if (gefahren > 0) { fuhren++; sb.Append('\n'); continue; }

            // ⚠⚠ 24.08.2026 — HIER STAND »gefahren == 0 ist DURCHGEFALLEN«, und
            // das war eine falsche Auskunft. Der Ersatzplatz des 2x2 lag ueber
            // dem Hafen, und unter dem Hafen ist die Sperrflaeche des Hafens.
            // Ein Schiff, das nicht fahren KANN, weil dort Land steht, ist eine
            // Auskunft ueber die KARTE. Der Fehler waere ein Schiff, das steht,
            // obwohl der naechste Schritt frei ist.
            // ⭐ Ein Pruefstand, der beides gleich nennt, meldet auf jeder
            // Karte mit einer Bucht »DURCHGEFALLEN« — und dann sieht man das
            // eine Mal nicht hin, an dem er recht hat.
            var next = s.Path != null && s.PathIdx < s.Path.Count
                ? s.Path[s.PathIdx] : new Vector2I(s.Col, s.Row + 1);
            string grund = _nav.WarumGesperrt(next.X, next.Y, s.Move, idx);
            bool istGelaende = grund.Contains("darf da nicht hin")
                            || grund.Contains("FESTES") || grund.Contains("ausserhalb");
            if (istGelaende) gelaende++; else haengen++;
            sb.Append($"\n      {(istGelaende ? "steht — GELAENDE, kein Fehler" : "⚠⚠ STEHT, obwohl frei")}"
                    + $". Naechste Zelle ({next.X},{next.Y}): {grund}\n");
        }
        sb.Append(BelegungAbgleich("nach dem Lauf"));

        // ⭐⭐ DIE FOLGE, DIREKT GEFRAGT — und nicht erschlossen.
        //
        // Ein Schiff, dessen Stempel beim Fahren weggeradiert wurde, belegt
        // nichts mehr. Dann darf ein ZWEITES Schiff auf genau seine Zellen
        // fahren, und das ist das Bild der Meldung: zwei grosse Boote, die
        // ineinander stehen und sich nicht mehr vom Fleck bewegen (jedes sieht
        // im anderen eine Einheit, die »gleich weiterfaehrt«, und wartet).
        //
        // ⚠⚠ HIER STAND EINE FRAGE AN `Ask`: »darf das zweite Schiff auf die
        // Zelle des ersten?« Sie antwortete in BEIDEN Faellen »GiveWay«, also
        // ✔ — denn in den sechzehn Zellen lag noch der Stempel einer DRITTEN
        // Einheit, und den sieht `Ask` genauso. Die Frage war richtig gemeint
        // und konnte den Unterschied nicht sehen.
        // ⭐ Was ihn sieht, ist die Zahl je Schiff: wieviele seiner EIGENEN
        // Zellen tragen seinen EIGENEN Stempel? Nullmodell 0 von 16, behoben
        // 16 von 16.
        foreach (int idx in _stauSchiffe)
        {
            if (idx >= _entities.Count) continue;
            var s2 = _entities[idx];
            int seite = _nav.HullOf(idx);
            if (seite <= 1) continue;
            int hat = 0;
            for (int dy = 0; dy < seite; dy++)
                for (int dx = 0; dx < seite; dx++)
                    if (_nav.BesetztVon(s2.Col + dx, s2.Row + dy) == idx) hat++;
            int soll2 = seite * seite;
            sb.Append($"  Nr. {idx} haelt {hat} von {soll2} eigenen Zellen"
                    + (hat == soll2 ? "  ✔" : "  ⚠⚠ so faehrt ein anderes Schiff hinein") + "\n");
        }

        // Die Messlatte: kein Schiff darf auf FREIEM Wasser haengen, und der
        // Abgleich muss glatt sein. Gelaende zaehlt nicht als Fehler, wird aber
        // genannt — sonst waere die Zahl geschoent.
        int fehler = haengen + _belegungFehler;
        sb.Append($"  {fuhren} gefahren, {gelaende} vom Gelaende gehalten, {haengen} haengen frei; "
                + $"Abgleichfehler {_belegungFehler}\n");
        sb.Append(fehler == 0
            ? "  BESTANDEN — kein Schiff haengt auf freiem Wasser, die Belegung stimmt"
            : $"  DURCHGEFALLEN — {haengen} Schiff(e) haengen frei, {_belegungFehler} Abgleichfehler");
        return sb.ToString();
    }
}
