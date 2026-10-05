namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE LICHTQUELLE — die Sicht bleibt am Ort eines Todes kurz offen</b>
/// (04.10.2026, bug-434). Bei uns schloss sich der Nebel im selben Nebelschritt,
/// in dem eine eigene Einheit starb; im Original bleibt ihre Todesstelle noch
/// 30 (Fussvolk), 80 (Fahrzeug, Schiff) bzw. 130 Takte (Gattung 3) offen, und
/// der Kreis zieht sich am Ende zusammen.
///
/// <para><b>Gelesen (EXE C, selbst zerlegt, 04.10.2026).</b></para>
/// <code>
///   0x4222C0(x, y, Radius, Dauer)       ; Sprungbrett 0x401253, 15 Rufer
///     Tafel 0x6786A8, 200 Plaetze zu 6 Byte:
///       +0 x (Byte)  +1 y (Byte)  +2 Radius (Byte)  +3 —  +4 Dauer (Wort)
///     frei = Dauer == 0 (@0x4222CC), erster freier Platz;
///     KEINER frei: Platz rand() % 200 wird UEBERSCHRIEBEN (@0x4222DD..0x4222E8)
///   0x422340  »Check spot« (Spur 0x4F7BA4), JEDEN Spieltakt im Taktblock
///             (@0x416477, Schleife 0x416077..0x4168B4, je Takt einmal, Pause aus):
///       fuer jeden Platz mit Dauer > 0:  Dauer -= 1;
///                                         wenn Radius > Dauer: Radius -= 1
///     => Radius(t) = min(Radius0, Dauer0 - t), nach Dauer0 Takten zu.
///   Nebelrunde 0x4205B0 (jeder 5. Takt, @0x41678C): nach Einheiten, Gebaeuden,
///     Flugzeugen, Minen und Radarmasten (@0x420A14..0x420A5A)
///       fuer jeden Platz mit Dauer != 0:  0x4200C0(x, y, Radius)
///     = derselbe STEMPLER wie bei einer Einheit, aber ohne Hoehe und ohne -1.
///     Er setzt sec50 (sichtbar) UND das Gedaechtnis — das Licht ERKUNDET also
///     auch. Danach erst der Antiradar (@0x420A5C): er schliesst auch Licht,
///     und sein Wiederoeffnen kennt keine Lichter.
///   Spielstand: die ganze Tafel (1200 B) wird gesichert/geladen (@0x41DA33 /
///     @0x41EAB8) und beim Missionsbeginn geloescht (@0x41F146, @0x41F1AE).
/// </code>
///
/// <para><b>Die 15 Rufer</b> (Bytesuche auf <c>E8/E9 → 0x401253</c>):</para>
/// <code>
///   @0x40B483  Todesroutine 0x40B3C0, NUR eigener Spieler (Platz/1000 == 0x4FA284),
///              Radius = +0x2C Sicht, Dauer nach Gattung +0x0A, Tafel 0x40B840:
///              0 → 80, 1 → 30, 2 → keine, 3 → 130, 4 → 80, 5 → 80, &gt;5 → keine
///   @0x4C995C  Gebaeudetod 0x4C95E0: (X+dx, Y+dy) aus Tafel 0x539D90, 10, 80 — JEDER Besitzer
///   @0x45458E  Druckwelle 0x454560: (x, y, 8, 150) — jeder Besitzer
///   @0x423AE8  Flugzeugabsturz, Aufprall: (X, Y, 6, 140) — nur eigener Spieler (+0x09)
///   @0x40959D  Pionier UKOL 20, Bruecke fertig: (x, y, 6, 120) — nur eigener Spieler
///   @0x4CC2D5  Bruecke setzen 0x4CC280: (x0+1, y0+1, 4, 40) — jeder
///   @0x4CB118  Bruecke abreissen 0x4CB0A0: (x0+1, y0+1, 4, 80) — jeder
///   Missionsskripte (Block = Missionsnummer, Tafel 0x4A5B0C):
///     M4  @0x499D07 (8, 56, 12, 2500) wenn v5 == 1
///     M7  @0x49AC74 / @0x49ACB7 (14, 16, 5, 300) — Wiffers Zelle
///     M17 @0x49E1C2 (5, 106, 5, 100), @0x49E2C4 (73, 89, 5, 1000)
///     M24 @0x4A1075 (107, 103, 8, 500)       ⚠ Regel fehlt bei uns ganz — OFFEN
///     M29 @0x4A38CC / @0x4A3C43 (5, 5, 10, 500)  (die zweite ist im Original tot, bug-388)
/// </code>
///
/// <para>⚠ Die Missionsskripte rufen die Lichtquelle OHNE Spielerpruefung — die
/// Tafel gehoert ohnehin nur dem Betrachter. Die Todesroutine wird von Zasah
/// (@0x40CFE2, @0x40D24B), vom Ausloeschen eines Spielers (0x41B0D0) und vom
/// Befehlsbus (@0x4C2F48) gerufen; <c>remove_unit</c>/<c>sell_unit</c> der
/// Skripte gehen NICHT hindurch (M7 legt sein Licht darum selbst).</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN (V):</b> (1) der Ort einer fahrenden Einheit ist
/// <c>Col/Row</c> (bei uns schon die Zielzelle, das Original wechselt RX/RY auf
/// halbem Weg); (2) bei voller Tafel ein lokaler Wurf (<c>GD.Randi</c>) statt
/// <c>rand()</c> — die Tafel gehoert nur dem Betrachter, ein gemeinsamer Wurf
/// wuerde den Gleichlauf zweier Rechner brechen; (3) die Tafel wird NICHT im
/// Spielstand gesichert (bei uns auch die Druckwellen nicht); (4) nur der Nebel
/// des Betrachters, nicht die KI-Nebel des Gefechts (die das Original nicht
/// hat); (5) Kill aus Einnahme/Tuersperre/Pruefstaenden zaehlt wie Zasah.</para>
///
/// <para>Gegenschalter <c>--todeslicht-aus</c> (alle Rufer, Stand e45a9af).
/// Pruefstand <c>--todeslicht-check</c>, mit <c>--shot=…png</c> ein Bildlauf.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--todeslicht-aus</c> — Stand e45a9af: keine Lichtquelle.</summary>
    public static bool TodeslichtAus;

    /// <summary>200 Plaetze — <c>cmp dl, 0xC8</c> @0x4222D8.</summary>
    public const int LichtPlaetze = 200;

    private struct Licht
    {
        public int Col, Row, Radius, Dauer;
        public string Quelle;
    }

    private readonly Licht[] _lichter = new Licht[LichtPlaetze];

    /// <summary>Zaehler fuer Pruefstand und Schlusszeile.</summary>
    public int LichterAngelegt, LichterUeberschrieben;
    private readonly SortedDictionary<string, int> _lichtArten = new();

    /// <summary><c>0x4222C0</c> — eine Lichtquelle anlegen.</summary>
    private void LichtAnlegen(int col, int row, int radius, int dauer, string quelle)
    {
        if (TodeslichtAus || dauer <= 0) return;
        int k = System.Array.FindIndex(_lichter, l => l.Dauer == 0);          // @0x4222CC
        if (k < 0)
        {
            // @0x4222DD: rand() % 200 — hier ein LOKALER Wurf, siehe Kopf (V 2).
            k = (int)(GD.Randi() % LichtPlaetze);
            LichterUeberschrieben++;
        }
        // x, y und Radius sind Bytes, die Dauer ein Wort (@0x4222F9..0x42231D).
        _lichter[k] = new Licht
        {
            Col = col & 0xFF, Row = row & 0xFF, Radius = radius & 0xFF, Dauer = dauer & 0xFFFF,
            Quelle = quelle,
        };
        LichterAngelegt++;
        string art = quelle.Split(' ')[0];
        _lichtArten[art] = _lichtArten.GetValueOrDefault(art) + 1;
    }

    /// <summary><c>0x422340</c> »Check spot« — jeden Spieltakt.</summary>
    private void LichtTakt()
    {
        for (int k = 0; k < LichtPlaetze; k++)
        {
            ref var l = ref _lichter[k];
            if (l.Dauer <= 0) continue;                                        // @0x42235D jle
            l.Dauer--;
            if (l.Radius > l.Dauer) l.Radius--;                                // @0x422375
        }
    }

    /// <summary>Fuer die Nebelrunde (@0x420A14): jeder Platz mit Dauer != 0
    /// stempelt mit seinem Radius. Als Aufdecker mit Hoehe 0 und Sicht
    /// Radius+1, weil <see cref="Simulation.FogGrid.UnitRadius"/> die −1 der
    /// Bodeneinheiten abzieht — das Licht stempelt den Radius selbst.</summary>
    private IEnumerable<(int Col, int Row, int Sight, int Elev)> LichtAufdecker()
    {
        if (TodeslichtAus) yield break;
        for (int k = 0; k < LichtPlaetze; k++)
            if (_lichter[k].Dauer != 0)
                yield return (_lichter[k].Col, _lichter[k].Row, _lichter[k].Radius + 1, 0);
    }

    /// <summary>Gattung (+0x0A) → Dauer, Tafel <c>0x40B840</c>; 0 = kein Licht.</summary>
    public static int TodeslichtDauer(int gattung) => gattung switch
    {
        0 or 4 or 5 => 80,    // 0x40B46C push 0x50
        1 => 30,              // 0x40B461 push 0x1E
        3 => 130,             // 0x40B465 push 0x82
        _ => 0,               // 2 → 0x40B48B, > 5 → ja 0x40B48B
    };

    /// <summary>@0x40B442..0x40B488 der Todesroutine: nur eine EIGENE Einheit.</summary>
    private void TodeslichtTod(Entity v)
    {
        if (v.IsBuilding || v.IsProp || v.Owner != ViewPlayer) return;
        int dauer = TodeslichtDauer(v.GameUnitType);
        if (dauer > 0) LichtAnlegen(v.Col, v.Row, v.Sight, dauer, "Tod " + LabelOf(v));
    }

    /// <summary>@0x4C9933..0x4C995C des Gebaeudetods: Art-Mittelpunkt (Tafel
    /// 0x539D90, dieselbe wie im Nebel), Radius 10, 80 Takte, jeder Besitzer.</summary>
    private void GebaeudeLicht(Entity b)
    {
        if (!b.IsBuilding || b.IsProp) return;
        var half = BuildingHalfSpan(b.BType);
        LichtAnlegen(b.Col + half.X, b.Row + half.Y, 10, 80, "Gebaeudetod");
    }

    /// <summary>Fuer das Missionsskript: <c>licht(a=x, b=y, c=Radius, d=Dauer)</c>.</summary>
    private void SkriptLicht(int x, int y, int radius, int dauer)
        => LichtAnlegen(x, y, radius, dauer, $"Skript ({x},{y})");

    /// <summary>Schlusszeile.</summary>
    public string TodeslichtZeile()
        => TodeslichtAus ? "todeslicht: AUS (--todeslicht-aus)"
         : $"todeslicht: {LichterAngelegt} Lichtquellen angelegt, {_lichter.Count(l => l.Dauer > 0)} offen"
           + (_lichtArten.Count > 0 ? " (" + string.Join(", ", _lichtArten.Select(kv => $"{kv.Key} {kv.Value}")) + ")" : "")
           + (LichterUeberschrieben > 0 ? $", {LichterUeberschrieben} bei voller Tafel ueberschrieben" : "");

    // ---- Pruefstand --------------------------------------------------------------

    /// <summary>Wie viele Zellen ein Stempel des Radius r auf DIESER Karte an
    /// (c, r) oeffnet — mit einem frischen Gitter gerechnet, unabhaengig vom
    /// Lichtcode.</summary>
    private int KreisZellen(int col, int row, int radius)
    {
        if (_fog == null) return 0;
        var g = new Simulation.FogGrid(_fog.Width, _fog.Height);
        g.Update(new[] { (col, row, radius + 1, 0) });
        return g.Counts().Watched;
    }

    /// <summary>Gestempelte Zellen im Kasten ±k um (c, r) — k = Lichtradius + 1.</summary>
    private int OffenUm(int col, int row, int k = 20)
    {
        int n = 0;
        for (int dy = -k; dy <= k; dy++)
            for (int dx = -k; dx <= k; dx++)
                if (_fog!.IsStamped(col + dx, row + dy)) n++;
        return n;
    }

    /// <summary>⚠ PRUEFSTAND-EINGRIFF: eine Zelle suchen, um die im Kasten
    /// ±(Radius+1) niemand sonst etwas sieht und kein feindlicher Antiradar
    /// (Radius 8) hineinreicht, und die Einheit dorthin setzen — sonst haelt
    /// die Sicht der Nachbarn die Todesstelle offen bzw. schliesst der Stoerer
    /// sie, und das Licht ist nicht zu messen. Gesucht wird ringweise um die
    /// Einheit.</summary>
    private Vector2I? Abseits(int vi, int k)
    {
        var v = _entities[vi];
        bool tot = v.Dead;
        v.Dead = true;                         // sie selbst sieht nicht mit
        UpdateFog();
        v.Dead = tot;
        var stoerer = new List<Vector2I>();
        foreach (var e in _entities)
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Part == AntiradarPart
                && e.Owner is >= 0 and <= 7 && !DecktAuf(e.Owner, ViewPlayer))
                stoerer.Add(new Vector2I(e.Col, e.Row));
        int w = _fog!.Width, h = _fog.Height;
        for (int ring = 0; ring < Mathf.Max(w, h); ring++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring) continue;
                    int c = v.Col + dx, r = v.Row + dy;
                    if (c < 0 || r < 0 || c >= w || r >= h) continue;   // der Kreis darf am Rand abgeschnitten sein — KreisZellen schneidet gleich
                    if (stoerer.Any(s => Mathf.Max(Mathf.Abs(s.X - c), Mathf.Abs(s.Y - r)) <= k + Simulation.FogGrid.AntiradarRadius + 1)) continue;
                    if (OffenUm(c, r, k) == 0) return new Vector2I(c, r);
                }
        return null;
    }

    private void Versetzen(Entity v, Vector2I z)
    {
        v.Col = z.X; v.Row = z.Y;
        v.Pos = CellCenter(z.X, z.Y);
        v.Path = null;
    }

    /// <summary>
    /// <c>--todeslicht-check</c> (bug-434). Je Gattung (1 Fussvolk, 0 Fahrzeug,
    /// 3, 4/5) die erste EIGENE Einheit abseits setzen, toeten, dann den Takt
    /// (<see cref="LichtTakt"/>) laufen lassen und zu ausgewaehlten Takten die
    /// Nebelrunde rechnen: gestempelte Zellen um die Todesstelle muessen genau
    /// ein Kreis des Radius <c>min(Sicht, Dauer − t)</c> sein, bei t = Dauer null.
    /// Dazu eine FREMDE Einheit (kein Licht) und ein fremdes Gebaeude (Licht
    /// 10/80 am Art-Mittelpunkt). Nullmodell <c>--todeslicht-aus</c> muss
    /// durchfallen.
    /// </summary>
    public string TodeslichtCheck()
    {
        var sb = new System.Text.StringBuilder("todeslicht-check (bug-434)\n");
        ForceFog = true;
        sb.Append($"  Schalter: todeslicht-aus {TodeslichtAus}; Betrachter Spieler {ViewPlayer}; Nebel {(FogActive ? "an" : "AUS")}\n");
        if (_fog == null || !FogActive) { sb.Append("  kein Nebelgitter — der Lauf sagt NICHTS\n  DURCHGEFALLEN"); return sb.ToString(); }
        System.Array.Clear(_lichter);
        bool ok = true;
        int geprueft = 0;

        foreach (int g in new[] { 1, 0, 3, 4, 5 })
        {
            int vi = -1;
            for (int i = 0; i < _entities.Count && vi < 0; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Owner != ViewPlayer || e.GameUnitType != g) continue;
                vi = i;
            }
            // ⚠ PRUEFSTAND-EINGRIFF: gibt es keine eigene, nimmt er eine fremde und
            // macht ihren Spieler fuer DIESE Zeile zum Betrachter (Gattung 3 gehoert
            // auf allen Karten Spieler 2). Die Regel bleibt dieselbe: Besitzer == Betrachter.
            int betrachterAlt = _viewPlayer;
            string eingriff = "";
            for (int i = 0; i < _entities.Count && vi < 0; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Owner is < 0 or > 7 || e.GameUnitType != g) continue;
                vi = i;
            }
            if (vi < 0) { sb.Append($"  Gattung {g}: keine auf dieser Karte\n"); continue; }
            if (_entities[vi].Owner != ViewPlayer)
            {
                eingriff = $" [Betrachter fuer diese Zeile Spieler {_entities[vi].Owner} statt {ViewPlayer}]";
                _viewPlayer = _entities[vi].Owner;
            }
            var v = _entities[vi];
            int kasten = Mathf.Min(_entities[vi].Sight, 19) + 1;
            var z = Abseits(vi, kasten);
            if (z == null)
            {
                var n = _fog.Counts();
                sb.Append($"  Gattung {g}: keine abseitige Zelle gefunden (Karte {_fog.Width}x{_fog.Height}, gestempelt {n.Watched}, Saum {n.Saum}, gesehen {n.Seen}, nie {n.Unseen})\n");
                _viewPlayer = betrachterAlt;
                ok = false; continue;
            }
            var alt = new Vector2I(v.Col, v.Row);
            Versetzen(v, z.Value);
            int sicht = v.Sight, dauerSoll = TodeslichtDauer(g);
            string name = $"{LabelOf(v)} Platz {v.Slot}, Sicht {sicht}";
            Kill(vi, v, -1, "todeslicht-check");
            int c = z.Value.X, r = z.Value.Y;

            var proben = new SortedSet<int> { 0, dauerSoll / 2, dauerSoll - Mathf.Min(sicht, 19) + 1,
                                              dauerSoll - 2, dauerSoll - 1, dauerSoll };
            proben.RemoveWhere(t => t < 0);
            var zeilen = new List<string>();
            bool gut = true;
            int stand = 0;
            foreach (int t in proben)
            {
                for (; stand < t; stand++) LichtTakt();
                UpdateFog();
                int offen = OffenUm(c, r, kasten);
                int rSoll = Mathf.Min(Mathf.Min(sicht, 19), dauerSoll - t);
                int soll = t >= dauerSoll ? 0 : KreisZellen(c, r, rSoll);
                bool mitte = _fog.IsStamped(c, r);
                bool passt = offen == soll && mitte == (t < dauerSoll);
                gut &= passt;
                zeilen.Add($"t={t}: {offen} Zellen (Soll {soll}{(t < dauerSoll ? $", Radius {rSoll}" : ", zu")}), Mitte {(mitte ? "offen" : "zu")}{(passt ? "" : " ✗")}");
            }
            // die Todesstelle ist danach ERKUNDET (Gedaechtnis), auch wenn sie zu ist
            bool erkundet = _fog.IsSeen(c, r);
            gut &= erkundet;
            ok &= gut;
            geprueft++;
            _viewPlayer = betrachterAlt;
            UpdateFog();
            sb.Append($"  Gattung {g} {name} von ({alt.X},{alt.Y}) nach ({c},{r}) versetzt, Dauer Soll {dauerSoll}{eingriff}:\n");
            sb.Append("    " + string.Join("\n    ", zeilen) + "\n");
            sb.Append($"    erkundet danach: {(erkundet ? "ja" : "NEIN")}  {(gut ? "ja" : "NEIN")}\n");
        }

        // die FREMDE Einheit: kein Licht
        {
            int vi = -1;
            for (int i = 0; i < _entities.Count && vi < 0; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Owner is < 0 or > 7 || DecktAuf(e.Owner, ViewPlayer)) continue;
                if (TodeslichtDauer(e.GameUnitType) == 0) continue;
                vi = i;
            }
            if (vi < 0) sb.Append("  fremde Einheit: keine auf dieser Karte\n");
            else
            {
                var v = _entities[vi];
                int kasten = Mathf.Min(v.Sight, 19) + 1;
                var z = Abseits(vi, kasten);
                if (z != null) Versetzen(v, z.Value);
                int vorher = LichterAngelegt;
                Kill(vi, v, -1, "todeslicht-check fremd");
                UpdateFog();
                int offen = z != null ? OffenUm(z.Value.X, z.Value.Y, kasten) : -1;
                bool gut = LichterAngelegt == vorher && offen <= 0;
                ok &= gut;
                sb.Append($"  fremde Einheit {LabelOf(v)} (Spieler {v.Owner}, Gattung {v.GameUnitType}) bei ({v.Col},{v.Row}): "
                        + $"Lichter +{LichterAngelegt - vorher} (Soll 0), offen {offen} (Soll 0)  {(gut ? "ja" : "NEIN")}\n");
            }
        }

        // ein Gebaeude: Licht 10/80 am Art-Mittelpunkt, jeder Besitzer
        {
            int bi = -1;
            for (int i = 0; i < _entities.Count && bi < 0; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || !e.IsBuilding || e.Owner is < 0 or > 7 || DecktAuf(e.Owner, ViewPlayer)) continue;
                bi = i;
            }
            if (bi < 0) sb.Append("  fremdes Gebaeude: keines auf dieser Karte\n");
            else
            {
                var b = _entities[bi];
                var half = BuildingHalfSpan(b.BType);
                var m = new Vector2I(b.Col + half.X, b.Row + half.Y);
                int vorher = LichterAngelegt;
                Kill(bi, b, -1, "todeslicht-check Gebaeude");
                var l = _lichter.FirstOrDefault(x => x.Dauer > 0 && x.Col == (m.X & 0xFF) && x.Row == (m.Y & 0xFF));
                bool gut = LichterAngelegt == vorher + 1 && l.Radius == 10 && l.Dauer == 80;
                ok &= gut;
                sb.Append($"  fremdes Gebaeude Art {b.BildArt} (Spieler {b.Owner}) Mittelpunkt ({m.X},{m.Y}): "
                        + $"Licht Radius {l.Radius} Dauer {l.Dauer} (Soll 10/80)  {(gut ? "ja" : "NEIN")}\n");
            }
        }
        if (geprueft == 0) { sb.Append("  keine eigene Einheit geprueft — der Lauf sagt NICHTS\n"); ok = false; }
        sb.Append(ok ? $"  BESTANDEN ({geprueft} Gattungen)" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    // ---- Bildlauf ---------------------------------------------------------------

    /// <summary>Fuer den Bildlauf: das erste eigene Fahrzeug (Gattung 0) abseits
    /// setzen; Kamerapunkt.</summary>
    public Vector2? TodeslichtBildVorbereiten(out int vi)
    {
        vi = -1;
        ForceFog = true;
        for (int i = 0; i < _entities.Count && vi < 0; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Owner == ViewPlayer && e.GameUnitType == 0) vi = i;
        }
        if (vi < 0) return null;
        var z = Abseits(vi, Mathf.Min(_entities[vi].Sight, 19) + 1);
        if (z == null) { vi = -1; return null; }
        Versetzen(_entities[vi], z.Value);
        UpdateFog();
        QueueRedraw();
        return _entities[vi].Pos;
    }

    public string TodeslichtBildName(int vi)
        => vi < 0 ? "" : $"{LabelOf(_entities[vi])} Platz {_entities[vi].Slot} ({_entities[vi].Col},{_entities[vi].Row}), Sicht {_entities[vi].Sight}";

    public void TodeslichtBildToeten(int vi)
    {
        if (vi >= 0) Kill(vi, _entities[vi], -1, "todeslicht-check Bildlauf");
        UpdateFog();
        QueueRedraw();
    }

    /// <summary>n Spieltakte: Lichttakt und Effekte, dann eine Nebelrunde.</summary>
    public void TodeslichtBildTakte(int n)
    {
        for (int i = 0; i < n; i++) { LichtTakt(); UpdateEffects(SimDt); _clock += SimDt; }
        UpdateFog();
        QueueRedraw();
    }
}
