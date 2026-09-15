namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE FAHRSPUREN</b> (15.09.2026, seine Beobachtung in K15: »wenn der Minenräumer
/// wo langfährt, findet eine leichte Bodengrafik-Veränderung statt« — und auf Rückfrage:
/// »die Fahrspur verschwindet nach kurzer Zeit«). Lesung berichte/minenraeumer-spur-opus.md
/// §2, beide GAME.EXE; Tafeln aus <c>aekernel-tools/spuren_tafeln.py</c> nach
/// <c>Data/spuren.json</c>.
///
/// <list type="bullet">
/// <item><b>Wer:</b> Gattung (+0x0A) == 0 und <c>Flagge[SPODEK +0x0B] != 0</c> — keine Spur
/// bei Spinne (1), Schneegleiter (8), Stahlsucher (16), Läufer (17). Sorte 1 bei SPODEK 7/9
/// (C 0x4B1C9D). Tafel sec39: 500 Gruppen × 40 Marken.</item>
/// <item><b>Wann:</b> eine Marke bei Schrittbeginn (Zelle alt, Richtung; C 0x40538F), eine
/// beim Zellwechsel auf halbem Weg (Zelle neu, Richtung | 0x80; C 0x4079DD); im Fahrtakt ein
/// Punkt, wenn <c>KOLIK / −19</c> (gerade) bzw. <c>/ −18</c> (schräg) den Quotienten wechselt
/// (C 0x40780F). Anlegen C 0x4A9590 (erster freie Platz, sonst rand%40; Leben 180; Punkt
/// rand%4 bzw. rand%8+10), Anhängen C 0x4A9680, Altern C 0x4A9790 je Takt.</item>
/// <item><b>Wie:</b> Zeichenliste C 0x42FF00, Korb Zeile + 2, nur sichtbare Zellen, VOR den
/// Minen; Zeichner Art 19 C 0x42D196: je Punkt 8 Bildpunkte aus MARK.CWK
/// [Neigungsbasis + Richtung] + Zittern, entlang des Weges (Richtungstafel 0x4312B0). Das
/// Lebensfeld liest der Zeichner nicht — kein Ausblenden, nach 180 Takten weg.</item>
/// </list>
///
/// <para>⚠ <b>UNSERE Übersetzungen/Setzungen:</b> (1) KOLIK aus <c>Progress</c>: bei uns läuft
/// ein Schritt 0 → StepCost (159/239 KOLIK), das Original wechselt die Zelle bei 80/120 und
/// zählt dann −79/−119 → 0 — hier umgerechnet. (2) Die Gruppe wird beim ersten Schritt
/// zugeteilt, nicht schon beim Laden. (3) Eigener Zufall statt <c>rand()</c> 0x43B750 — die
/// Spur ist reine Darstellung. (4) Abgedunkelt mit <see cref="ShadowTint"/> statt der
/// .CWS-Tafel. (5) Die Höhe an der Unterposition (0x4B62B0) ist durch
/// <c>Hang.Versatz</c> ersetzt (V). (6) Sorte 1 (Luftkissen, Kugelroller) wird simuliert,
/// aber nicht gezeichnet — die Zitterzeile ihres Sechsermusters ist nicht gelesen (V).
/// (7) Höchstens 8 Punkte je Marke (Tafelbreite) — das Original prüft das nicht.</para>
///
/// <para>Gegenschalter <c>--spuren-aus</c> (keine Spur), <c>--spuren-unsichtbar</c>
/// (simuliert, zeichnet nicht).</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool SpurenAus, SpurenUnsichtbar;

    /// <summary><c>--spuren-ueber-einheiten</c> — der Stand vom ersten Bau (15.09.2026):
    /// die Spur im Korb Zeile + 2, nach den Einheiten, also über ihnen.</summary>
    public static bool SpurenUeberEinheiten;

    /// <summary>Alle Spuren als Bodenschicht, vor den Einheiten.</summary>
    private void SpurenZeichnenAlle()
    {
        if (SpurenUnsichtbar || _spuren.Count == 0 || !SpurTafeln()) return;
        foreach (var g in _spuren.Values)
            foreach (var m in g.Marken)
            {
                if (m.Leben == 0) continue;
                if (FogActive && !Watched(m.Col, m.Row)) continue;
                foreach (var p in SpurBildpunkte(m))
                    DrawRect(new Rect2(p, Vector2.One), ShadowTint);
            }
    }

    private sealed class SpurMarke
    {
        public int Col, Row, Richtung, Anzahl, Leben;
        public readonly byte[] Punkte = new byte[8];
    }

    private sealed class SpurGruppe
    {
        public readonly SpurMarke[] Marken = new SpurMarke[40];
        public int Laufend = -1;
        public int Sorte;
        public int Angelegt, Punkte, Max;                           // fuer den Pruefstand
        public SpurGruppe() { for (int i = 0; i < 40; i++) Marken[i] = new SpurMarke(); }
    }

    private const int SpurLeben = 180, SpurGruppenMax = 500;
    private readonly Dictionary<Entity, SpurGruppe> _spuren = new();
    private readonly HashSet<Entity> _spurlos = new();
    private readonly System.Random _spurWurf = new(0x4A9590);

    /// <summary>Zähler für den Prüfstand.</summary>
    public int SpurMarkenAngelegt, SpurPunkteGesamt, SpurPunkteMax;

    // ---- die Tafeln ----------------------------------------------------------------
    private static int[]? _spFlaggen, _spBasis, _spZittern, _spMark;

    private static bool SpurTafeln()
    {
        if (_spMark != null) return true;
        const string pfad = "res://Data/spuren.json";
        if (!FileAccess.FileExists(pfad)) return false;
        using var f = FileAccess.Open(pfad, FileAccess.ModeFlags.Read);
        using var doc = System.Text.Json.JsonDocument.Parse(f.GetAsText());
        int[] Feld(string n)
        {
            var l = new List<int>();
            foreach (var x in doc.RootElement.GetProperty(n).EnumerateArray()) l.Add(x.GetInt32());
            return l.ToArray();
        }
        _spFlaggen = Feld("flaggen"); _spBasis = Feld("basis"); _spZittern = Feld("zittern");
        var mark = Feld("mark");
        if (mark.Length != 640 || _spBasis.Length != 5 || _spZittern.Length != 96) return false;
        _spMark = mark;
        return true;
    }

    private SpurGruppe? SpurGruppeVon(Entity e)
    {
        if (SpurenAus || e.Dead || e.IsBuilding || e.IsProp) return null;
        if (_spuren.TryGetValue(e, out var g)) return g;
        if (_spurlos.Contains(e)) return null;
        bool zieht = SpurTafeln() && e.GameUnitType == 0 && e.Infantry < 0
                     && e.Chassis >= 0 && e.Chassis < _spFlaggen!.Length && _spFlaggen[e.Chassis] != 0;
        if (!zieht || _spuren.Count >= SpurGruppenMax) { _spurlos.Add(e); return null; }
        g = new SpurGruppe { Sorte = e.Chassis is 7 or 9 ? 1 : 0 };
        _spuren[e] = g;
        return g;
    }

    private int SpurPunktWert(SpurGruppe g) => g.Sorte == 0 ? _spurWurf.Next(4) : _spurWurf.Next(8) + 10;

    /// <summary>0x4A9590 — eine Marke anlegen.</summary>
    private void SpurAnlegen(SpurGruppe g, int col, int row, int richtung)
    {
        int b = -1;
        for (int i = 0; i < 40; i++) if (g.Marken[i].Leben == 0) { b = i; break; }
        if (b < 0) b = _spurWurf.Next(40);
        var m = g.Marken[b];
        m.Col = col; m.Row = row; m.Richtung = richtung; m.Anzahl = 1; m.Leben = SpurLeben;
        m.Punkte[0] = (byte)SpurPunktWert(g);
        g.Laufend = b;
        g.Angelegt++; g.Punkte++; g.Max = System.Math.Max(g.Max, 1);
        SpurMarkenAngelegt++; SpurPunkteGesamt++;
        SpurPunkteMax = System.Math.Max(SpurPunkteMax, 1);
    }

    /// <summary>0x4A9680 — einen Punkt an die laufende Marke hängen.</summary>
    private void SpurPunktAnhaengen(SpurGruppe g)
    {
        if (g.Laufend < 0) return;
        var m = g.Marken[g.Laufend];
        if (m.Leben == 0 || m.Anzahl >= 8) return;                   // Setzung (7)
        m.Punkte[m.Anzahl++] = (byte)SpurPunktWert(g);
        g.Punkte++; g.Max = System.Math.Max(g.Max, m.Anzahl);
        SpurPunkteGesamt++;
        SpurPunkteMax = System.Math.Max(SpurPunkteMax, m.Anzahl);
    }

    /// <summary>Schrittbeginn (C 0x40538F): Marke auf der alten Zelle, Richtung des Schrittes.</summary>
    private void SpurSchrittbeginn(Entity e, Vector2I next)
    {
        var g = SpurGruppeVon(e);
        if (g == null) return;
        int richtung = DirToFacing(new Vector2(next.X - e.Col, next.Y - e.Row));   // POHYB: 0 unten, 2 links …
        SpurAnlegen(g, e.Col, e.Row, richtung);
    }

    /// <summary>KOLIK des Originals aus unserem Fortschritt: bis zur Hälfte positiv, danach
    /// negativ bis 0 (C 0x40799D: si − (2·kosten − 1)).</summary>
    private static int SpurKolik(int progress, int stepCost, bool gewickelt)
    {
        int voll = stepCost / 1000;                                   // 159 bzw. 239
        int halb = (voll + 1) / 2;                                    // 80 bzw. 120
        int k = progress / (SimHz * 1000);
        return gewickelt && k >= halb ? k - voll : k;
    }

    /// <summary>Der Fahrtakt (C 0x40780F) und der Zellwechsel auf halbem Weg (C 0x4079DD).</summary>
    private void SpurFahrtakt(Entity e, int progVor, Vector2I ziel)
    {
        if (e.StepCost <= 0) return;
        var g = SpurGruppeVon(e);
        if (g == null) return;
        int halb = (e.StepCost / 1000 + 1) / 2;
        int teiler = e.StepCost > 200_000 ? -18 : -19;
        int kVor = SpurKolik(progVor, e.StepCost, true);
        bool warVor = progVor / (SimHz * 1000) < halb;
        int kNeu = warVor ? SpurKolik(e.Progress, e.StepCost, false)   // noch in der ersten Hälfte gerechnet
                          : SpurKolik(e.Progress, e.StepCost, true);
        if (kVor / teiler != kNeu / teiler) SpurPunktAnhaengen(g);
        if (warVor && e.Progress / (SimHz * 1000) >= halb)
        {
            int richtung = DirToFacing(new Vector2(ziel.X - e.Col, ziel.Y - e.Row));
            SpurAnlegen(g, ziel.X, ziel.Y, richtung | 0x80);
        }
    }

    /// <summary>0x4A9790 — alle Marken altern, je Takt −1.</summary>
    private void SpurenAltern()
    {
        if (_spuren.Count == 0) return;
        List<Entity>? weg = null;
        foreach (var (e, g) in _spuren)
        {
            bool lebt = false;
            foreach (var m in g.Marken) if (m.Leben > 0) { m.Leben--; lebt |= m.Leben > 0; }
            if (!lebt && e.Dead) (weg ??= new List<Entity>()).Add(e);
        }
        if (weg != null) foreach (var e in weg) _spuren.Remove(e);
    }

    public int SpurMarkenLebend()
    {
        int n = 0;
        foreach (var g in _spuren.Values) foreach (var m in g.Marken) if (m.Leben > 0) n++;
        return n;
    }

    /// <summary>Richtungstafel 0x4312B0: Versatz entlang des Weges, jeweils + (20, 10).</summary>
    private static (int X, int Y) SpurRichtung(int r, int d) => r switch
    {
        0 => (20, 10 - d / 8), 1 => (20 - d / 6, 10 - d / 12), 2 => (20 - d / 4, 10),
        3 => (20 - d / 6, 10 + d / 12), 4 => (20, 10 + d / 8), 5 => (20 + d / 6, 10 + d / 12),
        6 => (20 + d / 4, 10), _ => (20 + d / 6, 10 - d / 12),
    };

    /// <summary>Die Bildpunkte einer Marke (Zeichner Art 19, C 0x42D196) in Kartenkoordinaten.</summary>
    private IEnumerable<Vector2> SpurBildpunkte(SpurMarke m)
    {
        int lage = Mathf.Clamp(SlopeClassOf(m.Col, m.Row), 0, 4);
        int basis = _spBasis![lage];
        int sx = (int)_ox + m.Col * TileW - 5;
        int sy = (int)_oy + m.Row * TileH - 14 - 15 * ElevOf(m.Col, m.Row);
        int richt = m.Richtung & 0x7F;
        for (int n = 0; n < m.Anzahl; n++)
        {
            int v = m.Punkte[n];
            if (v >= 10) continue;                                   // Sorte 1 — Setzung (6)
            int weg = 20 * n;
            if (m.Richtung >= 0x80) weg -= (richt & 1) != 0 ? 110 : 85;
            var (ox, oy) = SpurRichtung(richt, weg);
            int hc = Simulation.Hang.Versatz(HangArt(m.Col, m.Row), ox, oy);   // Setzung (5)
            if (hc > 20) hc = 10;
            int x0 = sx + ox - 20, y0 = sy - hc - oy + 10;
            for (int k = 0; k < 8; k++)
            {
                int mi = 2 * (8 * (basis + richt) + k), zi = 3 * (8 * v + k);
                yield return new Vector2(x0 + _spMark![mi] + _spZittern![zi],
                                         y0 + _spMark[mi + 1] + _spZittern[zi + 1]);
            }
        }
    }

    /// <summary>Korb Zeile + 2, vor den Minen (C 0x430E17 &lt; 0x430E21).</summary>
    private void SpurenZeichnen(int zeile)
    {
        if (SpurenUnsichtbar || _spuren.Count == 0 || !SpurTafeln()) return;
        foreach (var g in _spuren.Values)
            foreach (var m in g.Marken)
            {
                if (m.Leben == 0 || m.Row + 2 != zeile) continue;
                if (FogActive && !Watched(m.Col, m.Row)) continue;
                foreach (var p in SpurBildpunkte(m))
                    DrawRect(new Rect2(p, Vector2.One), ShadowTint);
            }
    }

    // ---- --spuren-check -------------------------------------------------------------

    /// <summary>
    /// <c>--spuren-check</c>, Kampagne 15: der Minenräumer (Reifen, Tempo 7) fährt 4 Zellen
    /// gerade → Soll 8 Marken, 40 Punkte, höchstens 5 je Marke; 180 Takte nach dem Halt 0
    /// lebende Marken. Gegenproben: eine Spinne (SPODEK 1) → 0; ein Kettenfahrzeug → dieselben
    /// Zahlen wie der Räumer, sofern gleich schnell (nichts hängt an 0x44).
    /// Nullmodell <c>--spuren-aus</c> → 0.
    /// </summary>
    /// <summary>Der Sollwert, unabhaengig von unserem Schrittmodell gerechnet — die Zaehler-
    /// arithmetik des Originals (§2.2): KOLIK += Tempo; Punkt, wenn KOLIK/−19 wechselt; bei
    /// KOLIK ≥ 80 Zellwechsel (−159) mit neuer Marke; Ankunft beim Uebergang auf ≥ 0; danach
    /// neue Marke zum Schrittbeginn. Gerade Fahrt, Tempo &lt; 18.</summary>
    private static (int Marken, int Punkte) SpurSoll(int tempo, int zellen)
    {
        const int voll = 159, halb = 80, teiler = -19;
        int kol = 0, marken = 0, punkte = 0, inMarke = 0;
        void Neu() { marken++; punkte++; inMarke = 1; }
        for (int z = 0; z < zellen; z++)
        {
            Neu();                                                   // Schrittbeginn
            while (true)
            {
                int alt = kol;
                kol += tempo;
                bool gewickelt = alt < 0;
                if (alt / teiler != kol / teiler && inMarke < 8) { punkte++; inMarke++; }
                if (!gewickelt && alt < halb && kol >= halb) { kol -= voll; Neu(); }
                else if (gewickelt && kol >= 0) break;               // on square
            }
        }
        return (marken, punkte);
    }

    /// <summary>Fuer den Bildlauf: den eigenen Minenraeumer auf eine freie Reihe stellen und
    /// 5 Zellen nach rechts schicken; gibt seinen Kartenpunkt zurueck.</summary>
    public Vector2? SpurBildProbe()
    {
        if (_nav == null) return null;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.Owner != ViewPlayer || e.Part != MinenImmun) continue;
            for (int r = 3; r < _nav.Height - 3; r++)
                for (int c = 3; c < _nav.Width - 9; c++)
                {
                    bool frei = true;
                    for (int dx = 0; dx <= 5 && frei; dx++)
                        frei = _nav.IsFree(c + dx, r, e.Move, i) && MinenAufZelle(c + dx, r) == 0
                               && _nav.GroundAt(c + dx, r) == Simulation.NavGrid.Ground.Free;
                    if (!frei) continue;
                    ProbeVersetzen(i, c, r);
                    e.Orders.Clear(); e.Target = -1; e.StepCost = 0; e.Progress = 0;
                    if (e.FuelMax > 0) e.Fuel = e.FuelMax;
                    e.Path = new List<Vector2I>();
                    for (int dx = 1; dx <= 5; dx++) e.Path.Add(new Vector2I(c + dx, r));
                    e.PathIdx = 0; e.Goal = new Vector2I(c + 5, r); e.Ordered = true;
                    return CellCenter(c + 3, r);
                }
        }
        return null;
    }

    public string SpurenCheck()
    {
        var sb = new System.Text.StringBuilder("spuren-check\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (SpurenAus) sb.AppendLine("  ⚠ NULLMODELL --spuren-aus: hier MUSS es durchfallen");
        Soll(SpurTafeln(), "Data/spuren.json geladen (MARK.CWK 640 B, Basis 5, Zittern 96)");

        (int marken, int punkte, int max, int lebend, string wo) Fahre(int idx, int zellen)
        {
            var e = _entities[idx];
            // eine gerade Reihe freier Zellen ohne Minen
            for (int r = 3; r < _nav.Height - 3; r++)
                for (int c = 3; c < _nav.Width - zellen - 3; c++)
                {
                    bool frei = true;
                    for (int dx = 0; dx <= zellen && frei; dx++)
                        frei = _nav.IsFree(c + dx, r, e.Move, idx) && MinenAufZelle(c + dx, r) == 0
                               && _nav.GroundAt(c + dx, r) == Simulation.NavGrid.Ground.Free;
                    if (!frei) continue;
                    ProbeVersetzen(idx, c, r);
                    e.Path = null; e.Orders.Clear(); e.Target = -1; e.StepCost = 0; e.Progress = 0;
                    if (e.FuelMax > 0) e.Fuel = e.FuelMax;
                    if (_spuren.TryGetValue(e, out var g0)) { g0.Angelegt = g0.Punkte = g0.Max = 0; }
                    e.Path = new List<Vector2I>();
                    for (int dx = 1; dx <= zellen; dx++) e.Path.Add(new Vector2I(c + dx, r));
                    e.PathIdx = 0; e.Goal = new Vector2I(c + zellen, r); e.Ordered = true;
                    int t = 0;
                    while (t < 2000 && !(e.Col == c + zellen && e.Path == null)) { SimTickFuerProbe(); t++; }
                    _spuren.TryGetValue(e, out var gf);
                    int marken = gf?.Angelegt ?? 0, punkte = gf?.Punkte ?? 0, max = gf?.Max ?? 0;
                    for (int k = 0; k < SpurLeben + 5; k++) SimTickFuerProbe();
                    int lebend = 0;
                    if (_spuren.TryGetValue(e, out var g)) foreach (var mk in g.Marken) if (mk.Leben > 0) lebend++;
                    return (marken, punkte, max, lebend, $"({c},{r})->({e.Col},{e.Row}) in {t} Takten, Tempo {RawSpeedOf(e)}");
                }
            return (-1, -1, -1, -1, "keine freie Reihe");
        }

        int Suche(System.Func<Entity, bool> f)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Mobile && e.Infantry < 0 && !Untergestellt(e)
                    && e.Move != Simulation.NavGrid.MoveClass.Ship && f(e)) return i;
            }
            return -1;
        }

        int ri = Suche(e => e.Part == MinenImmun && e.Owner == ViewPlayer);
        if (ri < 0) Soll(false, "kein eigener Minenraeumer");
        else
        {
            var (sm, sp) = SpurSoll(RawSpeedOf(_entities[ri]), 4);
            var (m, p, mx, l, wo) = Fahre(ri, 4);
            Soll(m == sm && System.Math.Abs(p - sp) <= 1 && mx <= 5 && l == 0,
                 $"Minenraeumer Platz {_entities[ri].Slot} (SPODEK {_entities[ri].Chassis}) 4 Zellen gerade {wo}: Marken {m} (Soll {sm}), Punkte {p} (Soll {sp} ±1, gerechnet), hoechstens {mx} je Marke, nach 180 Takten lebend {l} (Soll 0)");
        }
        int si = Suche(e => e.Chassis == 1);
        if (si < 0) sb.AppendLine("  (keine Spinne fuer die Gegenprobe)");
        else
        {
            var (m, p, _, _, wo) = Fahre(si, 4);
            Soll(m == 0 && p == 0, $"Gegenprobe Spinne Platz {_entities[si].Slot} (SPODEK 1) {wo}: Marken {m}, Punkte {p} (Soll 0)");
        }
        int ki = Suche(e => e.Chassis is 4 or 5 && e.Part != MinenImmun && RawSpeedOf(e) < 18);
        if (ki < 0) sb.AppendLine("  (kein langsames Kettenfahrzeug fuer die Gegenprobe)");
        else
        {
            var (sm, sp) = SpurSoll(RawSpeedOf(_entities[ki]), 4);
            var (m, p, mx, _, wo) = Fahre(ki, 4);
            Soll(m == sm && System.Math.Abs(p - sp) <= 1 && mx <= 5,
                 $"Gegenprobe Ketten Platz {_entities[ki].Slot} (SPODEK {_entities[ki].Chassis}, ohne 0x44) {wo}: Marken {m} (Soll {sm}), Punkte {p} (Soll {sp} ±1, gerechnet)");
        }
        sb.AppendLine($"  --spuren-aus {SpurenAus}, --spuren-unsichtbar {SpurenUnsichtbar}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
