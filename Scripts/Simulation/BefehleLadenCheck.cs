namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <b><c>--befehle-laden-check</c></b> (bug-650, KayelGee R8 K8) — die Ebenenseite: Befehle
/// verschiedener Art hinlegen, nach dem Laden feldweise vergleichen und die WIRKUNG messen
/// (faehrt die Einheit weiter ans selbe Ziel, pendelt der Wagen, kommt der gebaute Wagen
/// aus dem Werk?). Ablauf und Urteil: <c>Rendering/PaketP2Lauf.cs</c>. Nullmodell
/// <c>--laden-befehle-alt</c>: Feldvergleich ANDERS, Wirkung bleibt aus.
///
/// <para>⚠ EINGRIFFE (alle in der Ausgabe genannt): fremde Fahrzeuge koennen dem Spieler
/// gegeben werden, wenn er zu wenige hat; zwei Fahrzeuge werden zu Transportern
/// (<c>Part := 0x47</c>, wie <c>--einnahme-check</c>), einer davon gilt als GEBAUT
/// (<c>Slot := −1</c>, keine Kartenvorlage) und steht im Werk (UKOL 54); die Quelle bekommt
/// Lager 40, wenn sie leer ist; die Sonderauftraege werden als Feldwerte gesetzt und nach
/// dem Feldvergleich wieder geloescht, damit sie die Wirkungsprobe nicht stoeren.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Eine hingelegte Probe: Listenstelle, Art, und was die Wirkung braucht.</summary>
    public sealed class BefehlsProbe
    {
        public int Index;
        public string Art = "";
        public int Ordinal;
        public string Vorher = "";
        public Vector2I Ziel;
        public int ZielKennung = -1;
        public int ZielPlatz = -1;
    }

    /// <summary>Die wievielte Nicht-Requisite der Liste — so steht sie im Spielstand, und so
    /// findet man sie nach dem Laden wieder.</summary>
    public int Ordinal(int idx)
    {
        int n = 0;
        for (int i = 0; i < idx && i < _entities.Count; i++) if (!_entities[i].IsProp) n++;
        return n;
    }

    public int IndexVonOrdinal(int ord)
    {
        int n = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            if (_entities[i].IsProp) continue;
            if (n == ord) return i;
            n++;
        }
        return -1;
    }

    public Entity? EinheitAn(int idx) => idx >= 0 && idx < _entities.Count ? _entities[idx] : null;

    private static int Cheb(int c, int r, Vector2I z) => System.Math.Max(System.Math.Abs(c - z.X), System.Math.Abs(r - z.Y));

    /// <summary>Ein Ziel mindestens <paramref name="mindest"/> Zellen weit, das die Wegsuche
    /// erreicht — acht Richtungen, wachsender Abstand.</summary>
    private Vector2I? ProbeFahrziel(int i, int mindest)
    {
        if (_nav == null) return null;
        var e = _entities[i];
        var start = new Vector2I(e.Col, e.Row);
        int[] dx = { 1, -1, 0, 0, 1, -1, 1, -1 }, dy = { 0, 0, 1, -1, 1, 1, -1, -1 };
        for (int abst = mindest; abst <= mindest + 20; abst += 4)
            for (int k = 0; k < 8; k++)
            {
                var z = new Vector2I(Mathf.Clamp(e.Col + dx[k] * abst, 2, _nav.Width - 3),
                                     Mathf.Clamp(e.Row + dy[k] * abst, 2, _nav.Height - 3));
                if (Cheb(e.Col, e.Row, z) < mindest) continue;
                var frei = _nav.NearestFree(z, e.Move, i, 4);
                if (frei is not { } f) continue;
                var weg = _nav.FindPath(start, f, e.Move, i);
                if (weg != null && weg.Count >= 10) return f;
            }
        return null;
    }

    /// <summary>Legt die Befehle hin und gibt die Beschreibung zurueck.</summary>
    public string BefehlsProbeLegen(List<BefehlsProbe> proben)
    {
        var sb = new StringBuilder();
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        var benutzt = new HashSet<int>();
        bool Fahrzeug(Entity x) => !x.IsProp && !x.IsBuilding && !x.Dead && x.Mobile && x.Infantry < 0
                                   && x.Move != Simulation.NavGrid.MoveClass.Ship && x.Ukol < 50 && !Untergestellt(x)
                                   && x.LeavingDock < 0 && (x.FuelMax <= 0 || x.Fuel > 20);
        int Nimm(System.Func<Entity, bool> passt, bool eigenZuerst = true)
        {
            for (int runde = 0; runde < 2; runde++)
                for (int i = 0; i < _entities.Count; i++)
                {
                    var x = _entities[i];
                    if (benutzt.Contains(i) || !passt(x)) continue;
                    bool eigen = x.Owner == me;
                    if (runde == 0 && eigenZuerst && !eigen) continue;
                    if (!eigen)
                    {
                        sb.Append($"; EINGRIFF Platz {x.Slot} Spieler {x.Owner} -> {me}");
                        x.Owner = x.Team = me;
                    }
                    benutzt.Add(i);
                    return i;
                }
            return -1;
        }
        void Probe(int i, string art, Vector2I ziel = default, int zk = -1, int zp = -1)
            => proben.Add(new BefehlsProbe { Index = i, Art = art, Ziel = ziel, ZielKennung = zk, ZielPlatz = zp });

        // ---- 1) drei Fahrten, die erste mit einer Schlange aus zwei Befehlen ----
        int fahrten = 0;
        for (int k = 0; k < 3; k++)
        {
            int i = Nimm(Fahrzeug);
            if (i < 0) break;
            var z = ProbeFahrziel(i, 15);
            if (z == null) { sb.Append($"; Platz {_entities[i].Slot}: kein Ziel >= 15 Zellen"); continue; }
            _sel.Clear(); _sel.Add(i);
            IssueMove(CellCenter(z.Value.X, z.Value.Y));
            var e = _entities[i];
            if (e.Path == null) { sb.Append($"; Platz {e.Slot}: IssueMove ohne Weg"); continue; }
            fahrten++;
            string art = "Fahrt";
            if (k == 0)
            {
                var z2 = ProbeFahrziel(i, 25) ?? z.Value;
                IssueMove(CellCenter(z2.X, z2.Y), queue: true);
                IssueMove(CellCenter(e.Col, e.Row), queue: true);
                art = $"Fahrt+Schlange({e.Orders.Count})";
            }
            Probe(i, art, z.Value);
            sb.Append($"; {art} Platz {e.Slot} ({e.Col},{e.Row}) -> ({z.Value.X},{z.Value.Y}), Weg {e.Path.Count}");
        }

        // ---- 2) ein befohlener Angriff auf ein Ziel ausser Reichweite ----------
        int ai = Nimm(x => Fahrzeug(x) && CanFight(x));
        if (ai >= 0)
        {
            var a = _entities[ai];
            int beste = -1, bd = int.MaxValue;
            for (int t = 0; t < _entities.Count; t++)
            {
                var v = _entities[t];
                if (v.IsProp || v.Dead || v.IsBuilding || Untergestellt(v) || !IsHostile(a, v)) continue;
                int d = Cheb(a.Col, a.Row, new Vector2I(v.Col, v.Row));
                if (d <= a.Range + 2 || d >= bd) continue;
                bd = d; beste = t;
            }
            if (beste >= 0)
            {
                _sel.Clear(); _sel.Add(ai);
                bool ok = IssueAttack(_entities[beste].Pos);
                if (!ok || a.Target != beste)
                {
                    sb.Append($"; EINGRIFF Angriff direkt gesetzt (IssueAttack {(ok ? "traf anderes Ziel" : "abgelehnt")})");
                    a.Target = beste; a.Ordered = true; a.Path = null;
                }
                Probe(ai, "Angriff", new Vector2I(_entities[beste].Col, _entities[beste].Row), Kennung(beste), _entities[beste].Slot);
                sb.Append($"; Angriff Platz {a.Slot} auf Platz {_entities[beste].Slot}, Abstand {bd}, Reichweite {a.Range}");
            }
            else sb.Append("; kein feindliches Ziel ausser Reichweite");
        }

        // ---- 3)+4) zwei Materialrouten: eine Karteneinheit, ein gebauter Wagen im Werk
        Entity? basis = null, quelle = null;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.IsProp || b.Dead || b.DoorCells.Count == 0) continue;
            if (basis == null && b.BType == 1) basis = b;
            else if (quelle == null && b.BType is 2 or 3 or 4) quelle = b;
        }
        quelle ??= basis;
        if (basis != null && quelle != null && !KeineTransportrouten)
        {
            if (quelle.StockW + quelle.StockF + quelle.StockS == 0)
            {
                quelle.StockW = quelle.StockF = quelle.StockS = 40;
                sb.Append($"; EINGRIFF Lager der Quelle Platz {quelle.Slot} := 40");
            }
            for (int k = 0; k < 2; k++)
            {
                int i = Nimm(Fahrzeug);
                if (i < 0) break;
                var u = _entities[i];
                if (!IstTransporter(u)) { u.Part = TransporterTeil; u.Weapon = 0; sb.Append($"; EINGRIFF Platz {u.Slot} wird Transporter"); }
                u.FuelMax = System.Math.Max(u.FuelMax, 999); u.Fuel = u.FuelMax;
                if (k == 1)
                {
                    // ⚠ GEBAUT: keine Kartenvorlage (Slot −1, wie die Erzeuger 0x4B1840/0x4B3800)
                    RouteFreigeben(u);
                    sb.Append($"; EINGRIFF Platz {u.Slot} gilt als GEBAUT (Slot -1)");
                    u.Slot = -1;
                }
                var r = RouteVon(u) ?? RouteAnlegen(u);
                if (r == null) { sb.Append("; kein Umschlagsatz frei"); continue; }
                r.Ziel = basis.Slot;
                r.Quelle[0] = quelle.Slot; r.Quelle[1] = r.Quelle[2] = r.Quelle[3] = -1;
                RouteStarten(u, r);
                if (k == 1)
                {
                    // im Werk: UKOL 54, unsichtbar, nicht im Raster (wie Einfahren)
                    _nav?.ClearOccupant(u.Col, u.Row, i);
                    u.Ukol = UkolImGebaeude;
                    r.Fahrziel = quelle.Slot;
                    r.Warten = 40;
                    _sel.Remove(i);
                    Probe(i, "Route gebaut UKOL 54");
                }
                else Probe(i, "Route Karte");
                sb.Append($"; Route {(k == 1 ? "gebaut, im Werk" : "Karte")}: Wagen ({u.Col},{u.Row}), Quelle {quelle.Slot} (Typ {quelle.BType}), Ziel {basis.Slot}");
            }
        }
        else sb.Append("; keine Basis/Quelle mit Tuer — Routen nicht geprueft");
        _sel.Clear();

        // ---- anlaufen lassen: der Weg soll MITTEN im Lauf gespeichert werden ----
        for (int t = 0; t < 30; t++) SimTickFuerProbe();

        // ---- 5)+6) Sonderauftraege als Feldwerte (nach dem Anlaufen gesetzt) -----
        int s1 = Nimm(x => !x.IsProp && !x.IsBuilding && !x.Dead && !Untergestellt(x) && x.Ukol < 50);
        if (s1 >= 0)
        {
            var e = _entities[s1];
            e.Bauart = 1; e.BauZelle = new Vector2I(e.Col + 1, e.Row); e.BauTakte = 7; e.BauAnlauf = 1;
            e.EinsteigTraeger = 4711; e.UnloadVonRampe = false; e.AusweichRichtung = 3; e.Cooldown = 1.25f;
            Probe(s1, "Sonder A (Bruecke, Einsteigen, Rampe, Ausweichen, Nachladen)");
        }
        int s2 = Nimm(x => !x.IsProp && !x.IsBuilding && !x.Dead && !Untergestellt(x) && x.Ukol < 50);
        int dock = -1;
        for (int i = 0; i < _entities.Count; i++) if (_entities[i].IsBuilding && !_entities[i].IsProp && !_entities[i].Dead) { dock = i; break; }
        if (s2 >= 0)
        {
            var e = _entities[s2];
            e.BuildOrder = 5; e.BuildTarget = (e.Row << 8) | (e.Col + 2);
            e.RailGoal = new Vector2I(e.Col, e.Row + 2); e.RailWork = 25;
            e.LeavingDock = dock;
            e.AuslaufZellen = 2; e.AuslaufV = -8; e.AuslaufRichtung = 1; e.AuslaufEnde = new Vector2I(e.Col + 3, e.Row);
            Probe(s2, "Sonder B (Bauauftrag, Gleis, Dock, Auslauf)");
        }
        int flug = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (flug < 0 || b.BType == 9) flug = i;
            if (b.BType == 9) break;
        }
        if (flug >= 0)
        {
            _entities[flug].Patrouille = !_entities[flug].Patrouille;
            Probe(flug, $"Patrouille (Typ {_entities[flug].BType})");
        }
        foreach (var p in proben) { p.Ordinal = Ordinal(p.Index); p.Vorher = BefehlsFeldzeile(_entities[p.Index]); }
        return $"{fahrten} Fahrten, {proben.Count} Proben" + sb;
    }

    /// <summary>Nach dem Feldvergleich: die kuenstlichen Sonderauftraege loeschen, damit
    /// die Wirkungsprobe nur Fahrt, Angriff und Route misst.</summary>
    public void BefehlsProbeSonderLoeschen(List<BefehlsProbe> proben)
    {
        foreach (var p in proben)
        {
            if (!p.Art.StartsWith("Sonder")) continue;
            int i = IndexVonOrdinal(p.Ordinal);
            if (i < 0) continue;
            var e = _entities[i];
            e.Bauart = 0; e.BauZelle = new Vector2I(-1, -1); e.BauTakte = 0; e.BauAnlauf = 0;
            e.EinsteigTraeger = -1; e.UnloadVonRampe = true; e.AusweichRichtung = 0; e.Cooldown = 0;
            e.BuildOrder = 0; e.BuildTarget = 0; e.RailGoal = null; e.RailWork = 0; e.LeavingDock = -1;
            e.AuslaufZellen = 0; e.AuslaufV = 0; e.AuslaufRichtung = 0; e.AuslaufEnde = default;
        }
    }

    /// <summary>
    /// Die Wirkungsprobe: <paramref name="takte"/> Takte laufen lassen und je Probe sagen, ob
    /// sie WIRKT. Fahrt: dasselbe Ziel wie gespeichert und naeher dran (oder da). Angriff:
    /// dasselbe Ziel noch im Visier. Karten-Route: gestartet und in Bewegung. Gebauter Wagen
    /// im Werk: wieder draussen (UKOL != 54). Gibt (ok, gemessen) zurueck.
    /// </summary>
    public (int Ok, int Gemessen) BefehlsWirkung(List<BefehlsProbe> proben, int takte, StringBuilder sb)
    {
        var vor = new Dictionary<BefehlsProbe, (int D, int C, int R, int U)>();
        foreach (var p in proben)
        {
            int i = IndexVonOrdinal(p.Ordinal);
            if (i < 0) continue;
            var e = _entities[i];
            int d = p.Art.StartsWith("Fahrt") ? Cheb(e.Col, e.Row, p.Ziel)
                  : p.Art == "Angriff" ? Cheb(e.Col, e.Row, p.Ziel) : 0;
            vor[p] = (d, e.Col, e.Row, e.Ukol);
        }
        for (int t = 0; t < takte; t++) SimTickFuerProbe();
        int ok = 0, gem = 0;
        foreach (var p in proben)
        {
            if (!vor.TryGetValue(p, out var v)) continue;
            int i = IndexVonOrdinal(p.Ordinal);
            var e = _entities[i];
            bool? gut = null; string was = "";
            if (p.Art.StartsWith("Fahrt"))
            {
                int d = Cheb(e.Col, e.Row, p.Ziel);
                gut = e.Goal == p.Ziel && (d < v.D || d == 0);
                was = $"Ziel ({e.Goal.X},{e.Goal.Y}) gespeichert ({p.Ziel.X},{p.Ziel.Y}), Abstand {v.D} -> {d}";
            }
            else if (p.Art == "Angriff")
            {
                int d = Cheb(e.Col, e.Row, p.Ziel);
                // dasselbe Ziel (ueber den PLATZ — die Kennung enthaelt die Zelle, und das Ziel
                // faehrt), oder es ist gefallen und der Angreifer kam befohlen naeher.
                gut = (e.Target >= 0 && _entities[e.Target].Slot == p.ZielPlatz) || (e.Ordered && d < v.D);
                was = $"Ziel {(e.Target >= 0 ? $"Platz {_entities[e.Target].Slot}" : "keines")}, befohlen {e.Ordered}, Abstand zur Zielzelle {v.D} -> {d}";
            }
            else if (p.Art == "Route Karte")
            {
                var r = RouteVon(e);
                bool bewegt = e.Col != v.C || e.Row != v.R || e.Path != null || e.Ukol != v.U;
                gut = r != null && r.Gestartet && bewegt;
                was = $"Route {(r == null ? "KEINE" : r.Gestartet ? "gestartet" : "steht")}, ({v.C},{v.R}) -> ({e.Col},{e.Row}), UKOL {v.U} -> {e.Ukol}";
            }
            else if (p.Art.StartsWith("Route gebaut"))
            {
                gut = e.Ukol != UkolImGebaeude && !Untergestellt(e);
                was = $"UKOL {v.U} -> {e.Ukol}, sichtbar {!Untergestellt(e)}, Route {(RouteVon(e) == null ? "KEINE" : "da")}";
            }
            if (gut == null) continue;
            gem++; if (gut.Value) ok++;
            sb.Append($"\n   {(gut.Value ? "  wirkt " : "⚠ STEHT ")} {p.Art}: {was}");
        }
        return (ok, gem);
    }

    /// <summary>Fuer den Spielstand-Vergleich (<c>--spielstand-check</c>): einer eigenen
    /// Einheit eine Fahrt >= 12 Zellen geben, wenn noch keine eigene faehrt — sonst haette
    /// der Teil »Befehle« nichts zu verlieren.</summary>
    public string SpielstandProbeFahrt()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        foreach (var e in _entities)
            if (!e.IsProp && !e.Dead && e.Owner == me && e.Path != null && e.Path.Count - e.PathIdx > 2)
                return "eine eigene Einheit faehrt schon";
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.IsBuilding || e.Dead || e.Owner != me || !e.Mobile || Untergestellt(e) || e.Ukol >= 50) continue;
            if (e.FuelMax > 0 && e.Fuel < 15) continue;
            var z = ProbeFahrziel(i, 12);
            if (z == null) continue;
            _sel.Clear(); _sel.Add(i);
            IssueMove(CellCenter(z.Value.X, z.Value.Y));
            _sel.Clear();
            if (e.Path != null) return $"Fahrt Platz {e.Slot} ({e.Col},{e.Row}) -> ({z.Value.X},{z.Value.Y})";
        }
        return "keine Fahrt gelegt";
    }

    /// <summary>Fuer <c>--spielstand-check</c>: eine Aufwertung, damit der Teil »Bauteile«
    /// etwas zu verlieren hat (bug-665).</summary>
    public string SpielstandProbeAufwertung()
    {
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        foreach (int b in new[] { 163, 161, 164, 160, 1, 2, 3, 4, 5 })
            if (Aufwerten(me, b) >= 0) return $"Bauteil {b} auf Stufe {BauteilFuer(me, b)![1]} aufgewertet";
        return "keine Aufwertung moeglich";
    }
}
