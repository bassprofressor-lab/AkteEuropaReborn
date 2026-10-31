namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 29.10.2026 — <b>Paket P3 der Testerrunde 0.6.12: die Bruecken</b> (KayelGee R8 K1 = bug-643,
/// K2 = bug-644, K19 = bug-661, K20 = bug-662; Lesung <c>berichte/bruecken-r8-opus.md</c>, Bau
/// <c>berichte/bau-p3-0-6-12.md</c>): die Schalter und die drei Pruefstaende.
///
/// <list type="bullet">
/// <item><b><c>--bruecken-nebel-check</c></b> (K1, kopflos, z. B. <c>-- --campaign=4</c>): je
///   Kartenbruecke, alles als UNERKUNDET gerechnet (B = 0, Hangart aus dem Byte +3), die Zeilen
///   jeder Brueckenspalte zwischen Oberkante und Unterkante der Satzzellen, die KEINE Nebelmaske
///   deckt. Soll 0. Nullmodell <c>--gelaender-stempel-alt</c>: 15 Zeilen je Spalte jeder
///   waagerechten Bruecke mit gehobener Fahrbahn.</item>
/// <item><b><c>--bruecke-streifen-check --shot=…</c></b> (K2, mit Fenster, <c>-- --campaign=4</c>):
///   Bild vor und nach dem Abriss der ersten Kartenbruecke; gezaehlt werden die deckenden Punkte
///   der Fahrbahnkacheln OBERHALB des Satzrechtecks, die nach dem Abriss unveraendert dastehen
///   (= der Streifen). Soll 0. Nullmodell <c>--bruecke-gebacken-alt</c>.</item>
/// <item><b><c>--bruecke-baustelle-check</c></b> (K19/K20, kopflos, <c>-- --campaign=9</c>): ein
///   zweiter Soldat laeuft durch den Startpunkt, der Pionier kommt an, waehrend der Soldat mitten
///   im Schritt Flanke → Startzelle steht. Soll: niemand stirbt, kein Phantom, die Bruecke
///   entsteht beim zweiten Auftrag (zweite Probe nach den Arbeitsanimationen einmal mit Absicht
///   blockiert), ein Fahrzeug kommt danach hinueber. Nullmodell
///   <c>--bruecke-sofort --bruecke-flanken-alt --bruecke-erschlaegt-alt</c>: der Soldat stirbt,
///   ein toter Stempel bleibt auf dem Brueckenkopf, das Fahrzeug kommt nicht an.</item>
/// <item><b><c>--rampen-streifen-check</c></b> (bug-685, 30.10.2026, kopflos, z. B.
///   <c>-- --campaign=5</c>): je heiler Kartenrampe die Punkte, die nach dem Abriss noch im
///   Bild stehen und die Truemmerkachel nicht deckt (gerechnet aus Bodenbild und Streifen,
///   Simulation/KartenrampeLebend.cs). Soll 0. Nullmodell <c>--rampen-gebacken-alt</c>.</item>
/// </list>
/// </summary>
public partial class MapViewer
{
    private static bool _p3Nebel, _p3Baustelle, _p3Streifen, _p3Rampen, _p3Pionier;

    /// <summary>Die Schalter von P3; true = verbraucht.</summary>
    private bool P3Schalter(string a)
    {
        switch (a)
        {
            case "--gelaender-stempel-alt": Simulation.NavGrid.GelaenderStempelAlt = true; return true;   // bug-643
            case "--bruecke-gebacken-alt": MapEntityLayer.BrueckeGebackenAlt = true; return true;          // bug-644
            case "--bruecke-erschlaegt-alt":                                                               // bug-661
                MapEntityLayer.BrueckeErschlaegtAlt = true; Simulation.NavGrid.TotNeuStempelnAlt = true; return true;
            case "--bruecke-flanken-alt": MapEntityLayer.BrueckeFlankenAlt = true; return true;            // bug-662
            case "--bruecke-clipuv-alt": MapEntityLayer.BrueckeClipUvAlt = true; return true;              // Messschalter
            case "--bruecke-sofort": MapEntityLayer.BrueckeSofort = true; return true;                     // bug-662
            case "--bruecken-nebel-check": _p3Nebel = true; return true;
            case "--bruecke-baustelle-check": _p3Baustelle = true; UI.HelpWindow.Suppressed = true; return true;
            case "--bruecke-streifen-check": _p3Streifen = true; UI.HelpWindow.Suppressed = true; return true;
            // ⭐ 30.10.2026, bug-685 — die Kartenrampen wie K2 (Simulation/KartenrampeLebend.cs).
            case "--rampen-gebacken-alt": MapEntityLayer.RampenGebackenAlt = true; return true;
            case "--rampen-streifen-check": _p3Rampen = true; UI.HelpWindow.Suppressed = true; return true;
            // ⭐ 30.10.2026, bug-695 — die Pionierbruecke erhoeht (Simulation/PionierbrueckeHoch.cs).
            case "--pionierbruecke-flach-alt": MapEntityLayer.PionierbrueckeFlachAlt = true; return true;
            case "--pionierbruecke-nur-stempel": MapEntityLayer.PionierbrueckeNurStempel = true; return true;
            case "--pionierbruecke-abriss-alt": MapEntityLayer.PionierbrueckeAbrissAlt = true; return true;
            case "--pionierbruecke-check": _p3Pionier = true; UI.HelpWindow.Suppressed = true; return true;
            case "--kartenbruecke-abriss-alt": MapEntityLayer.KartenbrueckeAbrissAlt = true; return true;   // bug-700
            case "--bruecken-spielstand-alt": MapEntityLayer.BrueckenSpielstandAlt = true; return true;     // bug-701
            case "--rampen-spielstand-alt": MapEntityLayer.RampenSpielstandAlt = true; return true;         // bug-702
        }
        return false;
    }

    /// <summary>Die Pruefstaende von P3; true = einer lief (und beendet bzw. laeuft weiter).</summary>
    private bool P3Pruefstaende()
    {
        if (_p3Streifen)
        {
            if (_shotPath.Length == 0) { GD.Print("bruecke-streifen-check: braucht --shot=<datei.png> (Fenster)"); GetTree().Quit(0); return true; }
            _ = BrueckeStreifenLauf();
            return true;
        }
        string? zeile = null;
        if (_p3Nebel) zeile = _entities.BrueckenNebelCheck();
        else if (_p3Baustelle) zeile = _entities.BrueckeBaustelleCheck();
        else if (_p3Pionier) zeile = _entities.PionierbrueckeCheck();      // bug-695
        else if (_p3Rampen)
        {
            // bug-685: das Bodenbild und die Objektebene so, wie sie beim Spieler liegen.
            string name = MapNames[_mapIndex];
            Image? Lies(string datei)
            {
                string pfad = ProjectSettings.GlobalizePath(MapFile(datei));
                return FileAccess.FileExists(MapFile(datei)) ? Image.LoadFromFile(pfad) : null;
            }
            zeile = $"rampen-streifen-check: Karte {name}\n" + _entities.RampenStreifenCheck(Lies(name + ".png"), Lies(name + ".objects.png"));
        }
        if (zeile == null) return false;
        GD.Print(zeile);
        GetTree().Quit(0);
        return true;
    }

    /// <summary>K2: Bild vor/nach dem Abriss, Streifenpunkte zaehlen.</summary>
    private async System.Threading.Tasks.Task BrueckeStreifenLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var p = _entities.ErsteKartenbrueckeMitte();
        if (p != null) _camera.Position = p.Value;
        var punkte = _entities.KartenfahrbahnPunkte();
        float oberkante = _entities.ErsteKartenbrueckeOberkante();
        async System.Threading.Tasks.Task<Image> Bild(int n)
        {
            for (int i = 0; i < 4; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var img = GetViewport().GetTexture().GetImage();
            string pfad = _shotPath.Replace(".png", $"_{n}.png");
            img.SavePng(pfad);
            GD.Print($"bruecke-streifen: {pfad}");
            return img;
        }
        var vorher = await Bild(0);
        var xf = _entities.GetGlobalTransformWithCanvas();
        GD.Print(_entities.BrueckeTrefferCheck(nurBisStufe1: true));
        GD.Print(_entities.BrueckeTrefferCheckRest());
        var nachher = await Bild(2);
        var sicht = GetViewport().GetVisibleRect().Size;
        float sx = vorher.GetWidth() / sicht.X, sy = vorher.GetHeight() / sicht.Y;
        int gesehen = 0, gleich = 0, ueber = 0, ueberGleich = 0;
        // Ein STREIFEN ist eine Weltzeile des Ueberstands, deren Punkte nach dem Abriss zur
        // Haelfte oder mehr unveraendert dastehen (ein einzelner Punkt gleicher Farbe ist Zufall).
        var zeilen = new Dictionary<int, (int N, int G)>();
        foreach (var w in punkte)
        {
            var s = xf * w;
            int x = (int)(s.X * sx), y = (int)(s.Y * sy);
            if (x < 0 || y < 0 || x >= vorher.GetWidth() || y >= vorher.GetHeight()) continue;
            gesehen++;
            var a = vorher.GetPixel(x, y); var b = nachher.GetPixel(x, y);
            bool g = Mathf.Abs(a.R - b.R) < 0.012f && Mathf.Abs(a.G - b.G) < 0.012f && Mathf.Abs(a.B - b.B) < 0.012f;
            if (g) gleich++;
            if (w.Y < oberkante)
            {
                ueber++; if (g) ueberGleich++;
                int zeile = (int)w.Y;
                var (zn, zg) = zeilen.GetValueOrDefault(zeile);
                zeilen[zeile] = (zn + 1, zg + (g ? 1 : 0));
            }
        }
        var sb = new StringBuilder("bruecke-streifen-check"
            + (MapEntityLayer.BrueckeGebackenAlt ? " — NULLMODELL --bruecke-gebacken-alt" : "") + "\n");
        sb.AppendLine($"  {punkte.Count} deckende Fahrbahnpunkte, {gesehen} im Bild; davon {ueber} OBERHALB des "
                    + $"Satzrechtecks (Weltzeile < {oberkante})");
        int streifen = 0;
        foreach (var kv in zeilen) if (kv.Value.G * 2 >= kv.Value.N) streifen++;
        sb.AppendLine($"  nach dem Abriss unveraendert: {gleich} Punkte insgesamt, davon ueber dem Satzrechteck {ueberGleich}; "
                    + $"Streifenzeilen (>= 50 % unveraendert): {streifen} von {zeilen.Count}");
        bool urteil = ueber > 0;
        sb.Append(!urteil ? "  KEIN URTEIL (kein Ueberstand im Bild)" : streifen == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        GD.Print(sb.ToString());
        GetTree().Quit(0);
    }
}

public partial class MapEntityLayer
{
    /// <summary>Fuer den Streifen-Pruefstand: die oberste Weltzeile der Satzzellen der ersten
    /// Kartenbruecke.</summary>
    public float ErsteKartenbrueckeOberkante()
    {
        var st = _stege.Find(s => s.Karte);
        if (st == null) return 0;
        float top = float.MaxValue;
        foreach (var (z, _, _) in SatzZellen(st))
            top = Mathf.Min(top, CellRect(_ox, _oy, z.X, z.Y, ElevOf(z.X, z.Y)).Position.Y);
        return top;
    }

    // ---- K1: --bruecken-nebel-check ----------------------------------------------

    /// <summary>Deckt die UNERKUNDET-Maske (B = 0, Hangart = Byte +3) einer Zelle der Spalte
    /// c den Weltpunkt (x, y)? Dieselbe Rechnung wie <c>MaskeDeckt</c>, ohne Nebelzustand.</summary>
    private bool MaskeDecktUnerkundet(int x, int y, int c, int rLo, int rHi)
    {
        if (_nebelDeckung == null || _nav == null) return false;
        int lx = x - (_ox + c * TileW);
        if (lx < 0 || lx >= TileW) return false;
        for (int r = System.Math.Max(0, rLo); r <= System.Math.Min(_nav.Height - 1, rHi); r++)
        {
            int hang = _nav.FlagAt(c, r);
            if (hang < 0 || hang >= Import.NebelMasken.Hangformen) continue;
            int elev = _fog != null ? NebelElev(c, r) : ElevOf(c, r);
            int ly = y - (_oy + r * TileH - elev * Import.MapBaker.ElevStep - Import.NebelMasken.Hub);
            if (ly < 0 || ly >= Import.NebelMasken.ZelleH) continue;
            if (_nebelDeckung[ly * Import.NebelMasken.AtlasB + hang * TileW + lx]) return true;
        }
        return false;
    }

    /// <summary><c>--bruecken-nebel-check</c> — siehe Kopf von Rendering/PaketP3Lauf.cs.</summary>
    public string BrueckenNebelCheck()
    {
        var sb = new StringBuilder("bruecken-nebel-check"
            + (Simulation.NavGrid.GelaenderStempelAlt ? " — NULLMODELL --gelaender-stempel-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();
        if (_nebelDeckung == null) return sb.Append("  keine Nebelmasken geladen — NICHT GEMESSEN").ToString();
        sb.AppendLine($"  Gelaenderstempel beim Laden: {GelaenderGestempelt} Zellen; Ecken 9..12 schon in der Datei: "
                    + $"{BrueckenMitEckenAusDatei} Bruecken");
        int gesamt = 0, mitLuecke = 0, n = 0, mitEcken = 0;
        foreach (var st in _stege)
        {
            if (!st.Karte) continue;
            n++;
            var e = BrueckenEcke(st);
            bool senk = IstSenkrecht(st);
            if (_nav.FlagAt(e.X, e.Y) == 9) mitEcken++;
            int luecke = NebelLueckeZeilen(st, out int spaltenMit, out int doppelt);
            gesamt += luecke;
            if (luecke > 0) mitLuecke++;
            sb.AppendLine($"  Platz {st.Slot} ({e.X},{e.Y}) {(senk ? "senkrecht " : "waagerecht")} k={st.Wasser}: "
                        + $"{luecke} unverdeckte Zeilen in {spaltenMit} Spalten; doppelt gedeckte Punkte {doppelt}");
        }
        sb.AppendLine($"  {n} Kartenbruecken, {mitEcken} tragen jetzt die Ecke 9; {mitLuecke} mit Luecke, "
                    + $"zusammen {gesamt} unverdeckte Zeilen (Soll 0)");

        // ⭐ 30.10.2026, bug-695 — auch die Pionierbruecke. ⚠ EINGRIFF: auf der Karte steht keine;
        // je Achse wird die erste gueltige Stelle direkt angelegt (wie --bruecke-check), NACH der
        // Zaehlung der Kartenbruecken, damit deren Zahlen gleich bleiben.
        int pn = 0, pGesamt = 0;
        foreach (bool willSenk in new[] { false, true })
        {
            Vector2I platz = new(-1, -1);
            for (int r = 0; r < _nav.Height && platz.X < 0; r++)
                for (int c = 0; c < _nav.Width && platz.X < 0; c++)
                    if (BrueckePlatzOk(c, r) && (BrueckenLauf[BrueckenRichtung(c, r)].Y != 0) == willSenk) platz = new Vector2I(c, r);
            if (platz.X < 0) { sb.AppendLine($"  Pionierbruecke {(willSenk ? "senkrecht" : "waagerecht")}: keine gueltige Stelle — nicht gemessen"); continue; }
            if (!BrueckeAnlegen(-1, platz.X, platz.Y)) { sb.AppendLine($"  Pionierbruecke bei ({platz.X},{platz.Y}) abgelehnt: {BrueckeNote}"); continue; }
            var st = _stege[^1];
            var e = BrueckenEcke(st);
            pn++;
            int luecke = NebelLueckeZeilen(st, out int spaltenMit, out int doppelt);
            pGesamt += luecke;
            sb.AppendLine($"  ⚠ EINGRIFF Pionierbruecke ({platz.X},{platz.Y}) Ecke ({e.X},{e.Y}) {(willSenk ? "senkrecht " : "waagerecht")} k={st.Wasser}, "
                        + $"angehoben {st.Vorher != null && !PionierbrueckeNurStempel}, gestempelt {st.Gestempelt}: "
                        + $"{luecke} unverdeckte Zeilen in {spaltenMit} Spalten; doppelt gedeckte Punkte {doppelt}");
        }
        if (pn > 0)
            sb.AppendLine($"  {pn} Pionierbruecke(n), zusammen {pGesamt} unverdeckte Zeilen (Soll 0)"
                        + (PionierbrueckeFlachAlt ? " — NULLMODELL --pionierbruecke-flach-alt" : "")
                        + (PionierbrueckeNurStempel ? " — MESSSCHALTER --pionierbruecke-nur-stempel" : ""));
        sb.Append(n == 0 && pn == 0 ? "  KEINE BRUECKE — NICHT GEMESSEN" : gesamt + pGesamt == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>bug-695: die Zaehlung je Bruecke (vorher im Rumpf von <see cref="BrueckenNebelCheck"/>):
    /// Zeilen je Brueckenspalte zwischen Ober- und Unterkante der Satzzellen, die KEINE
    /// Unerkundet-Maske deckt. Dazu als Mass der »doppelten Abdunklung« die Punkte dieses
    /// Bandes, die zwei oder mehr Masken decken.</summary>
    private int NebelLueckeZeilen(Steg st, out int spaltenMit, out int doppelt)
    {
        spaltenMit = 0; doppelt = 0;
        if (_nav == null) return 0;
        var e = BrueckenEcke(st);
        bool senk = IstSenkrecht(st);
        int k = st.Wasser;
        int c0 = e.X, c1 = senk ? e.X + 2 : e.X + k + 1;
        int r0 = e.Y, r1 = senk ? e.Y + k + 1 : e.Y + 2;
        int luecke = 0;
        for (int c = c0; c <= c1; c++)
        {
            int yTop = int.MaxValue, yBot = int.MinValue;
            for (int r = r0; r <= r1; r++)
            {
                int top = _oy + r * TileH - ElevOf(c, r) * Import.MapBaker.ElevStep;
                yTop = System.Math.Min(yTop, top); yBot = System.Math.Max(yBot, top + TileH);
            }
            int hier = 0;
            for (int y = yTop; y < yBot; y++)
            {
                bool gedeckt = false;
                for (int x = 0; x < TileW; x++)
                {
                    int m = MaskenUnerkundet(_ox + c * TileW + x, y, c, r0 - 4, r1 + 8);
                    if (m > 0) gedeckt = true;
                    if (m > 1) doppelt++;
                }
                if (!gedeckt) hier++;
            }
            if (hier > 0) spaltenMit++;
            luecke += hier;
        }
        return luecke;
    }

    /// <summary>bug-695: wie <see cref="MaskeDecktUnerkundet"/>, aber die ZAHL der deckenden Masken.</summary>
    private int MaskenUnerkundet(int x, int y, int c, int rLo, int rHi)
    {
        if (_nebelDeckung == null || _nav == null) return 0;
        int lx = x - (_ox + c * TileW);
        if (lx < 0 || lx >= TileW) return 0;
        int n = 0;
        for (int r = System.Math.Max(0, rLo); r <= System.Math.Min(_nav.Height - 1, rHi); r++)
        {
            int hang = _nav.FlagAt(c, r);
            if (hang < 0 || hang >= Import.NebelMasken.Hangformen) continue;
            int elev = _fog != null ? NebelElev(c, r) : ElevOf(c, r);
            int ly = y - (_oy + r * TileH - elev * Import.MapBaker.ElevStep - Import.NebelMasken.Hub);
            if (ly < 0 || ly >= Import.NebelMasken.ZelleH) continue;
            if (_nebelDeckung[ly * Import.NebelMasken.AtlasB + hang * TileW + lx]) n++;
        }
        return n;
    }

    // ---- K19/K20: --bruecke-baustelle-check --------------------------------------

    /// <summary>⚠ EINGRIFF: Einheit auf eine Zelle versetzen (alle Anker raeumen, neu stempeln).</summary>
    private void P3Versetze(int i, Vector2I z)
    {
        var o = _entities[i];
        _nav!.AlleAnkerLoesen(i);
        _nav.ClearOccupant(o.Col, o.Row, i);
        o.Col = z.X; o.Row = z.Y; o.Path = null; o.Reserved = null; o.Target = -1;
        o.Pos = BodyCenterAt(o, z.X, z.Y);
        _nav.SetOccupant(z.X, z.Y, i, o.Infantry >= 0);
    }

    private bool P3AufZelle(int i, Vector2I z)
    {
        var o = _entities[i];
        if (o.Col == z.X && o.Row == z.Y) return true;
        if (o.Reserved is { } rv && rv == z) return true;
        foreach (var a in _nav!.AnkerVon(i)) if (a == z) return true;
        return false;
    }

    private int P3ToteStempel()
    {
        int n = 0;
        foreach (var (_, _, ent, fest) in _nav!.BelegteZellen())
            if (!fest && ent >= 0 && ent < _entities.Count && _entities[ent].Dead) n++;
        return n;
    }

    /// <summary><c>--bruecke-baustelle-check</c> — siehe Kopf von Rendering/PaketP3Lauf.cs.</summary>
    public string BrueckeBaustelleCheck()
    {
        bool nullmodell = BrueckeSofort || BrueckeFlankenAlt || BrueckeErschlaegtAlt;
        var sb = new StringBuilder("bruecke-baustelle-check"
            + (nullmodell ? $" — NULLMODELL (sofort {BrueckeSofort}, flanken-alt {BrueckeFlankenAlt}, erschlaegt-alt {BrueckeErschlaegtAlt})" : "") + "\n");
        if (_nav == null) return sb.Append("  keine Karte").ToString();
        int a = -1, b = -1, v = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var o = _entities[i];
            if (o.Dead || o.IsBuilding || o.IsProp || !o.Mobile || Untergestellt(o) || o.Owner != ViewPlayer) continue;
            if (a < 0 && IstPionier(o)) a = i;
            else if (b < 0 && o.Infantry >= 0 && !IstPionier(o)) b = i;
            else if (v < 0 && o.Infantry < 0 && (o.Move == Simulation.NavGrid.MoveClass.Vehicle || o.Move == Simulation.NavGrid.MoveClass.Walker) && _nav.HullOf(i) == 1) v = i;
        }
        if (b < 0)   // zur Not ein zweiter Pionier
            for (int i = 0; i < _entities.Count; i++)
            {
                var o = _entities[i];
                if (i != a && !o.Dead && !o.IsBuilding && !o.IsProp && o.Infantry >= 0 && o.Owner == ViewPlayer && !Untergestellt(o)) { b = i; break; }
            }
        // ⚠ EINGRIFF: Kampagne 9 hat beim Start keinen Pionier (er wird erst gebaut). Der
        // Arbeitstakt fragt die Gattung nicht — ein zweiter Fusssoldat spielt dann den Pionier.
        bool ersatzPionier = false;
        if (a < 0)
            for (int i = 0; i < _entities.Count; i++)
            {
                var o = _entities[i];
                if (i != b && !o.Dead && !o.IsBuilding && !o.IsProp && o.Infantry >= 0 && o.Owner == ViewPlayer && !Untergestellt(o))
                { a = i; ersatzPionier = true; break; }
            }
        if (a < 0 || b < 0 || v < 0)
        {
            var arten = new System.Collections.Generic.SortedDictionary<string, int>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var o = _entities[i];
                if (o.Dead || o.IsBuilding || o.IsProp || o.Owner != ViewPlayer) continue;
                string k = $"{(o.Infantry >= 0 ? "Fuss" : o.Move.ToString())} Rumpf {_nav.HullOf(i)} Pionier {IstPionier(o)} unter {Untergestellt(o)}";
                arten[k] = arten.GetValueOrDefault(k) + 1;
            }
            foreach (var kv in arten) sb.AppendLine($"    {kv.Key}: {kv.Value}");
            return sb.Append($"  Pionier {a}, zweiter Soldat {b}, Fahrzeug {v} — NICHT GEMESSEN").ToString();
        }
        var A = _entities[a]; var B = _entities[b]; var V = _entities[v];

        // Bauplatz: gueltig, die Laufreihe des Soldaten begehbar und frei, ein Anfahrtsfeld
        // fuer das Fahrzeug davor und ein Zielfeld hinter dem Gegenufer. Der naechste zum Pionier.
        Vector2I P = new(-1, -1), q = default, lauf = default, T0 = default, Z = default;
        int kW = 0, best = int.MaxValue, feindAbstand = 0;
        var feinde = new List<Vector2I>();
        foreach (var f in _entities)
            if (!f.Dead && !f.IsProp && f.Owner != ViewPlayer && f.Weapon != 0) feinde.Add(new Vector2I(f.Col, f.Row));
        bool Frei(Vector2I z, Simulation.NavGrid.MoveClass m) => _nav.InBounds(z.X, z.Y) && _nav.CanEnter(z.X, z.Y, m) && _nav.OccupantAt(z.X, z.Y) < 0;
        for (int r = 0; r < _nav.Height; r++)
            for (int c = 0; c < _nav.Width; c++)
            {
                if (!BrueckePlatzOk(c, r)) continue;
                int d = BrueckenRichtung(c, r); int k = BrueckenWeite(c, r, d);
                var l = BrueckenLauf[d]; var qq = new Vector2I(l.Y, l.X); var pz = new Vector2I(c, r);
                bool ok = true;
                for (int s = -3; s <= 3 && ok; s++) if (s != 0) ok = Frei(pz + qq * s, B.Move);
                if (!ok) continue;
                var t0 = pz - l * 2;
                if (!Frei(t0, V.Move) || !Frei(pz - l, V.Move)) continue;
                var zz = pz + l * (k + 2);
                if (!Frei(zz, V.Move)) continue;
                // Weit weg von jeder fremden Waffe (sonst schiesst der Gegner das Szenario um),
                // dann so nah wie moeglich am Fahrzeug.
                int feind = int.MaxValue;
                foreach (var f in feinde) feind = System.Math.Min(feind, System.Math.Abs(c - f.X) + System.Math.Abs(r - f.Y));
                int dist = (feind >= 20 ? 0 : 100000 - feind * 1000) + System.Math.Abs(c - V.Col) + System.Math.Abs(r - V.Row);
                if (dist < best) { best = dist; P = pz; q = qq; lauf = l; T0 = t0; Z = zz; kW = k; feindAbstand = feind; }
            }
        if (P.X < 0) return sb.Append("  kein geeigneter Bauplatz — NICHT GEMESSEN").ToString();
        var F1 = P - q; var F2 = P + q;
        sb.AppendLine($"  Bauplatz ({P.X},{P.Y}), naechste fremde Waffe {feindAbstand} Zellen, Lauf ({lauf.X},{lauf.Y}), {kW} Wasserzellen; Flanken ({F1.X},{F1.Y}) / ({F2.X},{F2.Y})");
        if (ersatzPionier) sb.AppendLine("  ⚠ EINGRIFF: kein Pionier auf der Karte — ein Fusssoldat spielt ihn (der Arbeitstakt fragt die Gattung nicht)");
        sb.AppendLine($"  ⚠ EINGRIFF: Pionier Platz {A.Slot}, Soldat Platz {B.Slot} ({LabelOf(B)}), Fahrzeug Platz {V.Slot} ({LabelOf(V)}) versetzt");

        // 1) Der Soldat laeuft quer durch die Startreihe.
        P3Versetze(b, P - q * 3);
        if (!MoleGehZu(b, (P + q * 3).X, (P + q * 3).Y)) return sb.Append("  der Soldat findet keinen Weg — NICHT GEMESSEN").ToString();
        int t = 0;
        while (t < 400 && !(B.Col == F1.X && B.Row == F1.Y && B.Reserved is { } rr && rr == P)) { SimTickFuerProbe(); t++; }
        if (t >= 400) return sb.Append($"  der Soldat kam nie in den Schritt Flanke -> Start (steht auf ({B.Col},{B.Row})) — NICHT GEMESSEN").ToString();
        sb.AppendLine($"  1. nach {t} Takten: Soldat mitten im Schritt ({F1.X},{F1.Y}) -> ({P.X},{P.Y}), Anker {_nav.AnkerVon(b).Count}");

        // 2) Der Pionier kommt an (Ankunftstakt direkt gerufen).
        int gebautVor = BrueckeGebaut, stegeVor = _stege.Count, ersteFehl = BrueckeErsteProbeFehl, zweiteFehl = BrueckeZweiteProbeFehl;
        P3Versetze(a, P);
        A.Bauart = BauartBruecke; A.BauZelle = P; A.BauTakte = 0; A.BauAnlauf = 0;
        MoleArbeitTick(a, A);
        bool gebaut1 = BrueckeGebaut > gebautVor;
        sb.AppendLine($"  2. Ankunftstakt: Bruecke gebaut {gebaut1}, Auftrag {(A.Bauart != 0 ? "laeuft" : "weg")}, Soldat {(B.Dead ? "TOT" : "lebt")}, "
                    + $"tote Stempel {P3ToteStempel()}");

        // 3) Weiterlaufen lassen, bis die Startreihe frei ist.
        t = 0;
        while (t < 400 && !B.Dead && (P3AufZelle(b, F1) || P3AufZelle(b, P) || P3AufZelle(b, F2))) { SimTickFuerProbe(); t++; }
        for (int i = 0; i < 25; i++) SimTickFuerProbe();
        sb.AppendLine($"  3. Soldat {(B.Dead ? "tot" : $"lebt, auf ({B.Col},{B.Row})")} nach {t} Takten");

        bool zweiteProbeGesehen = false;
        if (BrueckeGebaut == gebautVor && !A.Dead)
        {
            // 4) Der Spieler gibt den Auftrag noch einmal (der Pionier steht noch auf P).
            if (A.Col != P.X || A.Row != P.Y) P3Versetze(a, P);
            A.Bauart = BauartBruecke; A.BauZelle = P; A.BauTakte = 0; A.BauAnlauf = 0;
            t = 0;
            bool gestellt = false, weggenommen = false;
            while (t < 600 && BrueckeGebaut == gebautVor && !A.Dead && A.Bauart != 0)
            {
                // kurz vor der zweiten Probe den Soldaten auf die andere Flanke stellen …
                if (!gestellt && !B.Dead && A.BauTakte >= MoleArbeitTakte - 3) { P3Versetze(b, F2); gestellt = true; }
                SimTickFuerProbe(); t++;
                // … und gleich nach der durchgefallenen zweiten Probe wieder weg.
                if (gestellt && !weggenommen && BrueckeZweiteProbeFehl > zweiteFehl)
                { zweiteProbeGesehen = true; if (!B.Dead) P3Versetze(b, P + q * 3); weggenommen = true; }
            }
            sb.AppendLine($"  4. zweiter Auftrag: nach {t} Takten Bruecke {(BrueckeGebaut > gebautVor ? "gebaut" : "NICHT gebaut")}; "
                        + $"zweite Probe mit Soldat auf der Flanke durchgefallen: {zweiteProbeGesehen}; Soldat {(B.Dead ? "TOT" : "lebt")}");
        }
        bool gebaut = BrueckeGebaut > gebautVor;
        int tote = P3ToteStempel();
        sb.AppendLine($"  Proben durchgefallen: erste {BrueckeErsteProbeFehl - ersteFehl}x, zweite {BrueckeZweiteProbeFehl - zweiteFehl}x; "
                    + $"tote Stempel nach dem Bau: {tote}");

        // 5) Das Fahrzeug faehrt hinueber.
        bool angekommen = false; int fahrt = 0;
        if (gebaut)
        {
            P3Versetze(v, T0);
            if (MoleGehZu(v, Z.X, Z.Y))
                while (fahrt < 1500 && !V.Dead && !(V.Col == Z.X && V.Row == Z.Y)) { SimTickFuerProbe(); fahrt++; }
            angekommen = V.Col == Z.X && V.Row == Z.Y;
        }
        sb.AppendLine($"  5. Fahrzeug ({T0.X},{T0.Y}) -> ({Z.X},{Z.Y}) ueber die Bruecke: "
                    + (angekommen ? $"angekommen nach {fahrt} Takten" : $"NICHT angekommen ({fahrt} Takte, steht auf ({V.Col},{V.Row}))"));
        bool ok2 = gebaut && !B.Dead && tote == 0 && angekommen && !gebaut1 && zweiteProbeGesehen;
        sb.Append(ok2 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
