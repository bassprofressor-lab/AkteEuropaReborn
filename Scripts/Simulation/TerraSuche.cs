namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>DER TERRANIUM-FINDER</b> (01.10.2026, bug-381,
/// berichte/terranium-finder-fable.md).
///
/// <para>Gemeldet in K23: »Terranium Finder kann nichts; Vorkommen sofort baubar
/// statt erst nach Suche«. Seine Entscheidung: »bau es so wie im Original«. Die
/// Kette, C-EXE (F-Adressen im Bericht):</para>
/// <code>
/// Menue   Turm 0x35 -> Zeile 0x15 »Terranium suchen«; Klick @0x448A79:
///         dword[0x502ACC] := 3 (Modus 3), Menue zu
/// Zeiger  0x4317C9: Modus 3 -> Zeigerart 0x0D OHNE Pruefung (Bild 19)
/// Klick   0x437994[13] = 0x4377D0 -> 0x438920(Einheit, X, Y) = BEFEHL 19, Modus := 0
/// Bef.19  0x4C3191: fahre(E, X, Y) ; Rawmat-Satz +1/+2 := X/Y ;
///         nur aus Zustand 1: Zustand := rand%3 - 0x7B  (0x85..0x87)
/// Takt    Leerlauf 0x407F38, Tafel 0x40A16C, Teil 0x4E -> 0x4083A4:
///   S == 0     »Wrong rawmat status«
///   S == 1     nichts (Ruhe, auch »nichts gefunden«)
///   S &gt; 0x80   BOHRSTOPP: Gelaendebyte (0x41D110) != 0 -> S += 0x80 (neu wuerfeln)
///                         sonst UKOL 17, AKCE 100, Klang 50, S += 0x7F
///   3 &lt; S &lt;= 0x80  ZUFALLSFAHRT nach Klick + (15 - rand%30) je Achse, S += 0x80
///   S == 3     SUCHE ueber sec78 0xBC6D40 (50 x 6 B) in Tafelreihenfolge:
///              Abstand^2 &lt; 1600 zur KLICKZELLE und imap (sp+1..sp+2, ze..ze+1)
///              alle 0xFFFE -> fahre(sp+1, ze+1), S := 2, +1 := Index; sonst S := 1
///   S == 2     ANKUNFT: genau auf (sp+1, ze+1), drei Nachbarn 0xFFFE ->
///              0x410E60 (Einheit weg), add_terra 0x420E20, sec78.belegt := 0, Klang 51
/// add_terra  sec38-Satz (+6+Spieler := 1), Kacheln 10240+3i+j, imap Spalte 0
///            0xFFFE, Spalten 1-2 0xFFFF
/// </code>
/// <para>⇒ 2, 3 oder 4 Bohrstopps à 100 Takte, der erste an der Klickzelle.</para>
///
/// <para>⚠ UNSERE SETZUNGEN:</para>
/// <list type="bullet">
/// <item>Der Rawmat-Satz wird im ersten Takt der Einheit zugeteilt, nicht beim
///   Aufstellen (Original: 0x4B1D9F/0x4B3952 hinter <c>+0x0C == 0x35</c>) — es
///   gibt bei uns zwei Aufstellwege mehr (Karte, Spielstand). Wirkung gleich.
///   Er wird beim Fund nicht zurueckgegeben (Felder +3/+6/+7 ungelesen).</item>
/// <item>Bleibt die Fahrt zur Mitte ohne Weg, wiederholen wir sie hoechstens alle
///   50 Takte statt jeden Takt (die Wegsuche ist bei uns teurer).</item>
/// <item>Die Zufallszelle wird in die Karte geklemmt (Original: Byte ohne Klemme).</item>
/// <item>Der Nebel um ein aufgeschlossenes Vorkommen (Radius 2 fuer den
///   Aufschliesser) und das Spielstandformat (sec78/sec79) fehlen noch.</item>
/// </list>
/// <para>Gegenschalter: <c>--terrasuche-aus</c> (Menue antwortet »noch nicht
/// gebaut«), <c>--terrasuche-ohne-bohren</c> (Diagnose: direkt Zustand 3),
/// <c>--terramarke-aus</c> (nur sec38-Satz, keine Kacheln/imap),
/// <c>--erz-sofort-baubar</c> (Simulation/Deposits.cs). Pruefstand
/// <c>--terrasuche-check</c> (K23).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Unser <c>dword[0x502ACC] = 3</c>.</summary>
    public const int OrderTerraSuche = 3;

    /// <summary>Aufsatz <c>+0x0E = 0x4E</c> — der Leerlauf-Automat haengt daran.</summary>
    public const int PartTerraFinder = 78;

    /// <summary>Turmgruppe <c>+0x0C = 0x35</c> — Menue und Rawmat-Zuteiler haengen daran.</summary>
    public const int TurmTerraFinder = 0x35;

    public static bool TerrasucheAus, TerrasucheOhneBohren, TerramarkeAus, TerrasucheCheckAn;

    /// <summary>Ein Satz der Tafel <b>sec79 »rawmat«</b> <c>0x834C38</c> (8 Byte).</summary>
    private sealed class RawmatSatz
    {
        public int Zustand;          // +0
        public int X, Y;             // +1/+2 Klickzelle
        public int Index;            // +1 nach der Suche: sec78-Index (bei uns eigenes Feld)
        public Entity? Einheit;      // +4
        public int BohrRest;         // UKOL 17 / AKCE der Einheit
        public int Wartet;           // UNSERE Drossel, siehe Kopf
        public bool Gemeldet;
        public int Bohr;             // Bohrstopps dieser Einheit (Pruefstand)
    }

    private readonly RawmatSatz?[] _rawmat = new RawmatSatz?[100];
    private readonly Dictionary<Entity, int> _rawmatPlatz = new();

    /// <summary>Die im Spiel gesetzten Markierungskacheln, Zelle -> Code.</summary>
    private readonly Dictionary<Vector2I, int> _terraKachel = new();

    /// <summary>Wer welches Vorkommen aufgeschlossen hat (sec38 +6+Spieler).</summary>
    private readonly List<(int Col, int Row, int Spieler)> _terraMarken = new();

    public int TerraBefehle, TerraBohrstopps, TerraNeugewuerfelt, TerraFunde,
               TerraFehlsuchen, TerraKlang50, TerraKlang51, TerraKachelnGezeichnet;

    private static bool IstTerraFinder(Entity e)
        => !e.Dead && !e.IsBuilding && !e.IsProp && e.Part == PartTerraFinder;

    /// <summary>Aus dem Getter von <c>_deposits</c>, wenn die Karte wechselt.</summary>
    private void TerraKartenwechsel()
    {
        System.Array.Clear(_rawmat);
        _rawmatPlatz.Clear();
        _terraKachel.Clear();
        _terraMarken.Clear();
    }

    // ---- der Menueweg ------------------------------------------------------

    /// <summary>Menuezeile 0x15 — @0x448A79: Modus 3.</summary>
    public string TerraSucheBeginnen()
    {
        int idx = MenueEinheit();
        if (idx < 0 || _entities[idx].Weapon != TurmTerraFinder)
            return "kein Terranium-Finder gewaehlt.";
        PlacementMode = OrderTerraSuche;
        PlacementUnit = idx;
        BuildOrderNote = $"{OrderWord(CodeTerraSuchen)}: Gegend anklicken (Esc bricht ab)";
        return "";
    }

    /// <summary>Klickarm 0x4377D0 — Befehl 19 fuer die gewaehlte Einheit.</summary>
    private bool TerraSucheKlick(int idx, int col, int row)
    {
        var e = _entities[idx];
        if (e.Dead || e.Owner != ViewPlayer)
        { BuildOrderNote = "die Einheit ist weg"; return false; }
        if (!Emit(CommandRecord.Make(CommandOp.TerraSuche, (byte)ViewPlayer,
                                     (short)idx, (short)col, (short)row)))
        { BuildOrderNote = "der Befehl liess sich nicht absetzen"; return false; }
        BuildOrderNote = $"{OrderWord(CodeTerraSuchen)}: unterwegs nach ({col},{row})";
        GD.Print($"terrasuche: Befehl 19 abgesetzt — Einheit {idx}, Zelle ({col},{row}) (0x4377D0 -> 0x438920)");
        return true;
    }

    /// <summary>Behandler 0x4C3191.</summary>
    private bool ApplyTerraSuche(in CommandRecord c)
    {
        int i = c.P1;
        if (i < 0 || i >= _entities.Count || _nav == null) return false;
        var e = _entities[i];
        if (e.Dead || e.IsBuilding || e.IsProp || !e.Mobile) return false;
        TerraFahre(i, c.P2, c.P3);                                   // 0x40B070
        var s = RawmatVon(e);
        if (s != null)
        {
            s.X = c.P2; s.Y = c.P3;                                  // 0x4C3200
            s.BohrRest = 0;                                          // UKOL := 2
            if (s.Zustand == 1)                                      // 0x4C3215
                s.Zustand = TerrasucheOhneBohren ? 3
                          : 0x85 + Simulation.Determinism.Roll(3);   // 0x4C3224
        }
        TerraBefehle++;
        GD.Print($"terrasuche: Befehl 19 angenommen — Einheit {i} faehrt nach ({c.P2},{c.P3}), " +
                 $"Rawmat-Zustand {(s == null ? "kein Satz" : $"0x{s.Zustand:X2}")}" +
                 (TerrasucheOhneBohren ? " (--terrasuche-ohne-bohren)" : ""));
        return true;
    }

    private RawmatSatz? RawmatVon(Entity e)
    {
        if (_rawmatPlatz.TryGetValue(e, out int p)) return _rawmat[p];
        if (e.Weapon != TurmTerraFinder) return null;
        // Zuteiler 0x438980: erster Satz mit +0 == 0, sonst »Cannot add new rawmat«.
        for (int k = 0; k < _rawmat.Length; k++)
        {
            if (_rawmat[k] != null) continue;
            _rawmat[k] = new RawmatSatz { Zustand = 1, Einheit = e };
            _rawmatPlatz[e] = k;
            return _rawmat[k];
        }
        GD.Print("terrasuche: Cannot add new rawmat (0x4FAB58)");
        return null;
    }

    /// <summary><c>fahre(E, x, y, 0)</c> — derselbe Fahrauftrag wie beim Bauauftrag.</summary>
    private bool TerraFahre(int i, int col, int row)
    {
        if (_nav == null) return false;
        var e = _entities[i];
        col = Mathf.Clamp(col, 0, _nav.Width - 1);
        row = Mathf.Clamp(row, 0, _nav.Height - 1);
        if (e.Col == col && e.Row == row) return true;
        var path = _nav.FindPath(new Vector2I(e.Col, e.Row), new Vector2I(col, row), e.Move, i);
        if (path == null || path.Count == 0) return false;
        e.Target = -1;
        e.Orders.Clear();
        AuftragLoeschen(e);   // bug-417: ein neuer Auftrag nimmt auch den Zellangriff
        e.Path = path; e.PathIdx = 0;
        e.Goal = new Vector2I(col, row);
        e.Reserved = null; e.WaitTime = 0;
        return true;
    }

    /// <summary><c>imap == 0xFFFE</c>: befahrbar, unbesetzt, nicht gesperrt.</summary>
    private bool ImapWortFrei(int c, int r)
        => _nav != null && _nav.InBounds(c, r)
        && _nav.GroundAt(c, r) == Simulation.NavGrid.Ground.Free
        && _nav.OccupantAt(c, r) < 0 && !_nav.IstTuerGesperrt(c, r);

    // ---- der Leerlauf-Automat 0x4083A4 -------------------------------------

    /// <summary>Jeden Originaltakt, aus OriginalTick neben BuildArrivalTick.</summary>
    private void TerraSucheTakt()
    {
        if (_nav == null) return;
        int n = _entities.Count;
        for (int i = 0; i < n && i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp) continue;
            if (e.Weapon == TurmTerraFinder && !_rawmatPlatz.ContainsKey(e)) RawmatVon(e);
            if (e.Part != PartTerraFinder) continue;
            if (!_rawmatPlatz.TryGetValue(e, out int p) || _rawmat[p] is not { } s) continue;
            // nur im Leerlauf (UKOL 0); eine Fahrt bricht das Bohren ab
            if (e.Path != null || e.Orders.Count > 0) { s.BohrRest = 0; continue; }
            if (s.BohrRest > 0) { s.BohrRest--; continue; }          // UKOL 17, 0x409449
            if (s.Wartet > 0) { s.Wartet--; continue; }
            TerraAutomat(i, e, s);
        }
    }

    private void TerraAutomat(int i, Entity e, RawmatSatz s)
    {
        int S = s.Zustand;
        if (S == 0)
        {
            if (!s.Gemeldet) { s.Gemeldet = true; GD.Print("terrasuche: Wrong rawmat status (0x4F69F4)"); }
            return;
        }
        if (S == 1) return;                                          // 0x408778
        if (S == 2) { TerraAnkunft(i, e, s); return; }               // 0x40862F
        if (S > 0x80)                                                // 0x4083DA
        {
            if (_nav!.FlagAt(e.Col, e.Row) != 0)
            {
                s.Zustand = (S + 0x80) & 0xFF;
                TerraNeugewuerfelt++;
                return;
            }
            s.BohrRest = 100;                                        // UKOL 17, AKCE 100
            s.Zustand = (S + 0x7F) & 0xFF;
            TerraBohrstopps++;
            s.Bohr++;
            TerraKlang50++;
            Audio.GameSounds.PlayAt(50, e.Col, e.Row);               // 0x4047E0(0x32,1,E,0)
            GD.Print($"terrasuche: Bohrstopp bei ({e.Col},{e.Row}) — 100 Takte, Klang 50, " +
                     $"Zustand jetzt 0x{s.Zustand:X2}");
            return;
        }
        if (S > 3)                                                   // 0x40843B
        {
            int zx = s.X + 15 - Simulation.Determinism.Roll(30);
            int zy = s.Y + 15 - Simulation.Determinism.Roll(30);
            TerraFahre(i, zx, zy);
            s.Zustand = S + 0x80;
            return;
        }
        // S == 3 — die Suche, 0x4084AB
        for (int k = 0; k < _erz.Count && k < 50; k++)
        {
            var z = _erz[k];
            if (!z.Belegt) continue;
            int dx = z.Col - s.X, dy = z.Row - s.Y;
            if (dx * dx + dy * dy >= 0x640) continue;
            if (!ImapWortFrei(z.Col + 1, z.Row) || !ImapWortFrei(z.Col + 2, z.Row) ||
                !ImapWortFrei(z.Col + 1, z.Row + 1) || !ImapWortFrei(z.Col + 2, z.Row + 1)) continue;
            TerraFahre(i, z.Col + 1, z.Row + 1);                     // 0x4085C7
            s.Zustand = 2; s.Index = k;
            GD.Print($"terrasuche: Suche um ({s.X},{s.Y}) — Erz {k} bei ({z.Col},{z.Row}) " +
                     $"gefunden, Fahrt zur Mitte ({z.Col + 1},{z.Row + 1})");
            return;
        }
        s.Zustand = 1;                                               // 0x4085BB
        TerraFehlsuchen++;
        GD.Print($"terrasuche: Suche um ({s.X},{s.Y}) — kein Erz im Umkreis 40, Einheit {i} wartet (Zustand 1)");
    }

    /// <summary>S == 2, 0x40862F…0x408746.</summary>
    private void TerraAnkunft(int i, Entity e, RawmatSatz s)
    {
        if (s.Index < 0 || s.Index >= _erz.Count) { s.Zustand = 1; return; }
        var z = _erz[s.Index];
        int tx = z.Col + 1, ty = z.Row + 1;
        if (e.Col != tx || e.Row != ty)
        {
            if (!TerraFahre(i, tx, ty)) s.Wartet = 50;               // UNSERE Drossel
            return;
        }
        if (!ImapWortFrei(tx, ty - 1) || !ImapWortFrei(tx + 1, ty - 1) || !ImapWortFrei(tx + 1, ty))
            return;
        // 0x410E60 — die Einheit wird entfernt (»transformiert sich«, HELPG #066)
        int owner = e.Owner;
        int ec = e.Col, er = e.Row;
        _sel.Remove(i);
        if (_selected == i) _selected = -1;
        _nav!.ClearOccupant(ec, er, i);
        e.Dead = true;
        e.Path = null; e.Orders.Clear();
        bool ok = AddTerra(z.Col, z.Row, z.Amount, owner);           // 0x408717
        z.Belegt = false;                                            // 0x408736
        TerraKlang51++;
        Audio.GameSounds.PlayAt(51, ec, er);                         // 0x40873E
        TerraFunde++;
        GD.Print($"terrasuche: FUND — Einheit {i} auf ({ec},{er}) entfernt, Vorkommen ({z.Col},{z.Row}) " +
                 $"{z.Amount} aufgeschlossen fuer Spieler {owner}{(ok ? "" : " — ⚠ sec38 voll")}; Klang 51" +
                 (TerramarkeAus ? " (--terramarke-aus: keine Kacheln, kein imap)" : ""));
        UpdatePanel();
        UpdateFog();
        QueueRedraw();
    }

    /// <summary><c>add_terra 0x420E20</c> (F 0x41FFE0).</summary>
    private bool AddTerra(int sp, int ze, int menge, int spieler)
    {
        var ds = _deposits;
        if (ds.Count >= 50) { GD.Print("terrasuche: Cannot place more terra (0x4F917C)"); return false; }
        ds.Add((sp, ze, menge));
        _terraMarken.Add((sp, ze, spieler));
        if (TerramarkeAus || _nav == null) return true;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                _terraKachel[new Vector2I(sp + c, ze + r)] = Import.MapBaker.TerraKachelBasis + 3 * r + c;
                _nav.TerraMarkeSetzen(sp + c, ze + r, frei: c == 0);
            }
        return true;
    }

    /// <summary>Die im Spiel gesetzten Markierungen — wie die Molen aus dem
    /// Streifen unten an <c>&lt;karte&gt;.objects.png</c>. Eine Karte aus einem
    /// Import vor dem 01.10.2026 hat die Kacheln nicht: dann unsichtbar.</summary>
    private void ZeichneTerramarken()
    {
        if (_terraKachel.Count == 0 || ObjektEbene is not { } tex) return;
        TerraKachelnGezeichnet = 0;
        foreach (var (z, code) in _terraKachel)
        {
            if (StreifenKachel(code) is not { } k) continue;
            var ziel = CellRect(_ox, _oy, z.X, z.Y, ElevOf(z.X, z.Y));
            DrawTextureRectRegion(tex,
                new Rect2(new Vector2(ziel.Position.X,
                                      ziel.Position.Y + Import.MapBaker.BlitAnchor + k.YOff),
                          k.Feld.Size), k.Feld);
            TerraKachelnGezeichnet++;
        }
    }

    // ---- der Pruefstand --terrasuche-check (K23) ---------------------------
    //
    //   Start:   Bauplaetze (sec38) 0, Erz (sec78) 11 — Nullmodell
    //            --erz-sofort-baubar: 11 Bauplaetze sofort.
    //   Finder 1 aus der VERSTAERKUNG (keine Kruecke) per Menue 0x15 und
    //            PlacementClick (= Befehl 19) auf eine Zelle nahe einem Erz:
    //            2..4 Bohrstopps, Fund, Einheit weg, 9 Kacheln, imap, Klang 51 1x.
    //   Finder 2 auf eine Zelle ohne Erz im Umkreis 40: Zustand 1, lebt.
    //   Danach ein echter Gebaeude-Techniker (Entwurf 70): »Mine bauen« auf dem
    //            anderen, versteckten Erz -> abgelehnt; auf dem gefundenen -> gebaut.
    private float _tsZeit;
    private int _tsSchritt;
    private int _tsStartBau = -1, _tsStartErz = -1, _tsStartErzBaubar = -1;
    private int _tsFinder = -1, _tsFinder2 = -1, _tsBauer = -1, _tsErz = -1, _tsAnders = -1;
    private Vector2I _tsKlick = new(-1, -1), _tsKlick2 = new(-1, -1);
    private string _tsModus = "", _tsZeiger = "", _tsMenue = "";
    private bool _tsMineAndersAbgelehnt, _tsMineGesetzt;
    private int _tsGebautVor = -1, _tsKacheln = -1, _tsImapOk = -1;
    private bool _tsDepositJa, _tsGrundrissJa, _tsAndersNein;
    private float _tsFundZeit = -1f;

    private void PollTerrasucheCheck(float dt)
    {
        _tsZeit += dt;
        if (_nav == null || _mscript == null) return;
        if (_tsSchritt == 0 && _tsZeit >= 1f)
        {
            _tsSchritt = 1;
            _tsStartBau = _deposits.Count;
            _tsStartErz = _erz.Count;
            int skriptBaubar = 0;
            if (_mscript.Terra != null)
                foreach (var (c, r, _) in _mscript.Terra) if (CellOnDeposit(c + 1, r + 1)) skriptBaubar++;
            _tsStartErzBaubar = skriptBaubar;
            GD.Print($"terrasuche-check: Start — {_tsStartBau} Bauplaetze (sec38), {_tsStartErz} Erz im " +
                     $"Boden (sec78), von {_mscript.Terra?.Count ?? 0} Skriptvorkommen sofort baubar: {skriptBaubar}" +
                     (ErzSofortBaubar ? " (--erz-sofort-baubar)" : ""));
            return;
        }
        if (_tsSchritt == 1)
        {
            // die Finder der Verstaerkung — erst wenn sie stehen
            var finder = new List<int>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (!IstTerraFinder(e) || e.Owner != ViewPlayer) continue;
                if (e.Path != null || e.Orders.Count > 0) continue;
                finder.Add(i);
            }
            if (finder.Count < 2 && _tsZeit < 60f) return;
            if (finder.Count == 0) { GD.Print("terrasuche-check: kein Finder angekommen"); _tsSchritt = 9; return; }
            _tsSchritt = 2;
            _tsFinder = finder[0];
            var f = _entities[_tsFinder];
            GD.Print($"terrasuche-check: {finder.Count} Finder stehen; Finder {_tsFinder} auf ({f.Col},{f.Row}), " +
                     $"Teil {f.Part}, Turm 0x{f.Weapon:X2}, Entwurf {f.Mark}");
            // das naechste Erz (aus der Skriptliste, damit es auch unter --erz-sofort-baubar geht)
            var liste = _mscript.Terra;
            int best = -1; int bestD = int.MaxValue;
            for (int k = 0; liste != null && k < liste.Count; k++)
            {
                int dx = liste[k].Col - f.Col, dy = liste[k].Row - f.Row;
                if (dx * dx + dy * dy < bestD) { bestD = dx * dx + dy * dy; best = k; }
            }
            if (best < 0) { GD.Print("terrasuche-check: kein Erz in dieser Mission"); _tsSchritt = 9; return; }
            _tsErz = best;
            var ez = liste![best];
            // ein anderes Erz fuer die Gegenprobe »anderswo nicht«
            for (int k = 0; k < liste.Count; k++) if (k != best) { _tsAnders = k; break; }
            // die Klickzelle: vier Zellen neben der Mitte, Gelaendebyte 0, befahrbar
            _tsKlick = new Vector2I(-1, -1);
            for (int ring = 4; ring <= 10 && _tsKlick.X < 0; ring++)
                for (int dy = -ring; dy <= ring && _tsKlick.X < 0; dy++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != ring) continue;
                        int c = ez.Col + 1 + dx, r = ez.Row + 1 + dy;
                        if (!_nav.InBounds(c, r) || _nav.FlagAt(c, r) != 0) continue;
                        if (!_nav.CanEnter(c, r, f.Move) || _nav.OccupantAt(c, r) >= 0) continue;
                        _tsKlick = new Vector2I(c, r); break;
                    }
            if (_tsKlick.X < 0) _tsKlick = new Vector2I(ez.Col + 1, ez.Row + 1);
            // ⭐ der SPIELERWEG: Auswahl, Menuezeile 0x15, Klick
            _sel.Clear(); _sel.Add(_tsFinder); _selected = _tsFinder;
            _tsMenue = MenueAktion(CodeTerraSuchen);
            _tsModus = PlacementMode.ToString();
            _tsZeiger = CursorHintAt(CellCenter(_tsKlick.X, _tsKlick.Y)).ToString();
            bool ok = PlacementClick(_tsKlick.X, _tsKlick.Y);
            GD.Print($"terrasuche-check: Finder {_tsFinder} — Menue »{(_tsMenue.Length == 0 ? "ok" : _tsMenue)}«, " +
                     $"Modus {_tsModus}, Zeiger {_tsZeiger}; Klick auf ({_tsKlick.X},{_tsKlick.Y}) nahe Erz " +
                     $"({ez.Col},{ez.Row}) -> {(ok ? "Befehl 19" : "KEIN Befehl")}");
            // Finder 2 auf eine Zelle ohne Erz im Umkreis 40
            for (int q = 1; q < finder.Count; q++)
            {
                var g = _entities[finder[q]];
                bool frei = true;
                foreach (var (c, r, _) in liste)
                    if ((c - g.Col) * (c - g.Col) + (r - g.Row) * (r - g.Row) < 0x640) { frei = false; break; }
                if (!frei) continue;
                _tsFinder2 = finder[q];
                _tsKlick2 = new Vector2I(g.Col, g.Row);
                _sel.Clear(); _sel.Add(_tsFinder2); _selected = _tsFinder2;
                MenueAktion(CodeTerraSuchen);
                bool ok2 = PlacementClick(g.Col, g.Row);
                GD.Print($"terrasuche-check: Finder {_tsFinder2} auf seine eigene Zelle ({g.Col},{g.Row}) " +
                         $"— kein Erz im Umkreis 40 -> {(ok2 ? "Befehl 19" : "KEIN Befehl")}");
                break;
            }
            return;
        }
        if (_tsSchritt == 2)
        {
            bool fund = TerraFunde > 0;
            if (!fund && _tsZeit < 150f) return;
            _tsSchritt = 3;
            _tsFundZeit = _tsZeit;
            var ez = _mscript.Terra![_tsErz];
            int sp = ez.Col, ze = ez.Row;
            _tsKacheln = 0;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    if (_terraKachel.TryGetValue(new Vector2I(sp + c, ze + r), out int code)
                        && code == Import.MapBaker.TerraKachelBasis + 3 * r + c) _tsKacheln++;
            _tsImapOk = 0;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    var g = _nav.GroundAt(sp + c, ze + r);
                    if (c == 0 ? g == Simulation.NavGrid.Ground.Free : g == Simulation.NavGrid.Ground.Blocked) _tsImapOk++;
                }
            _tsDepositJa = DepositIndexAt(sp + 1, ze + 1) >= 0;
            var off = BuildOffsetOfOrder(OrderFieldMine);
            _tsGrundrissJa = Patterns != null &&
                             CanBuild(Patterns, TypeFieldMine, sp + off.X, ze + off.Y, -1, null, skipDeposit: true);
            if (_tsAnders >= 0)
            {
                var a = _mscript.Terra[_tsAnders];
                _tsAndersNein = DepositIndexAt(a.Col + 1, a.Row + 1) < 0;
            }
            GD.Print($"terrasuche-check: nach {_tsZeit:F0} s — Funde {TerraFunde}, Bohrstopps {TerraBohrstopps}, " +
                     $"Kacheln {_tsKacheln}/9 (Streifen {(StreifenKachel(Import.MapBaker.TerraKachelBasis) != null ? "da" : "FEHLT — Karte vor dem 01.10. importiert")}), " +
                     $"imap {_tsImapOk}/9, Bauplatz dort {_tsDepositJa}, Grundriss traegt {_tsGrundrissJa}, " +
                     $"anderes Erz baubar {!_tsAndersNein}");
            // der Gebaeude-Techniker der Verstaerkung (Entwurf 70, Teil 72)
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer) continue;
                if (BuildPartOf(e) != PartBuildingTech || e.Path != null) continue;
                _tsBauer = i; break;
            }
            if (_tsBauer < 0) { GD.Print("terrasuche-check: kein Gebaeude-Techniker"); return; }
            _tsGebautVor = BuildingsRaised;
            if (_tsAnders >= 0)
            {
                var a = _mscript.Terra[_tsAnders];
                _sel.Clear(); _sel.Add(_tsBauer); _selected = _tsBauer;
                MenueAktion(CodeFeldmine);
                _tsMineAndersAbgelehnt = !PlacementClick(a.Col + 1, a.Row + 1);
                GD.Print($"terrasuche-check: »Mine bauen« auf dem versteckten Erz ({a.Col},{a.Row}) -> " +
                         $"{(_tsMineAndersAbgelehnt ? "abgelehnt" : "ANGENOMMEN")} ({BuildOrderNote})");
            }
            _sel.Clear(); _sel.Add(_tsBauer); _selected = _tsBauer;
            MenueAktion(CodeFeldmine);
            _tsMineGesetzt = PlacementClick(sp + 1, ze + 1);
            GD.Print($"terrasuche-check: »Mine bauen« auf dem gefundenen ({sp},{ze}) -> " +
                     $"{(_tsMineGesetzt ? "Befehl 20" : "abgelehnt")} ({BuildOrderNote})");
        }
    }

    public string TerrasucheCheckLine()
    {
        var sb = new StringBuilder("terrasuche-check");
        var schalter = new List<string>();
        if (ErzSofortBaubar) schalter.Add("--erz-sofort-baubar");
        if (TerrasucheAus) schalter.Add("--terrasuche-aus");
        if (TerrasucheOhneBohren) schalter.Add("--terrasuche-ohne-bohren");
        if (TerramarkeAus) schalter.Add("--terramarke-aus");
        if (schalter.Count > 0) sb.Append($" ({string.Join(" ", schalter)})");
        bool f1tot = _tsFinder >= 0 && _tsFinder < _entities.Count && _entities[_tsFinder].Dead;
        bool f2lebt = _tsFinder2 >= 0 && _tsFinder2 < _entities.Count && !_entities[_tsFinder2].Dead;
        int gebaut = _tsGebautVor >= 0 ? BuildingsRaised - _tsGebautVor : 0;
        int bohr1 = -1;
        if (_tsFinder >= 0 && _tsFinder < _entities.Count
            && _rawmatPlatz.TryGetValue(_entities[_tsFinder], out int pl) && _rawmat[pl] is { } s1) bohr1 = s1.Bohr;
        sb.Append($": Start {_tsStartBau} Bauplaetze / {_tsStartErz} Erz (sofort baubar {_tsStartErzBaubar}); ");
        sb.Append($"Befehl 19 {TerraBefehle}x, Bohrstopps {TerraBohrstopps} (Finder 1: {bohr1}; Klang 50 {TerraKlang50}x, neu gewuerfelt {TerraNeugewuerfelt}), ");
        sb.Append($"Funde {TerraFunde} (Klang 51 {TerraKlang51}x), Fehlsuchen {TerraFehlsuchen}; ");
        sb.Append($"Finder 1 {(f1tot ? "entfernt" : "lebt")}, Finder 2 {(_tsFinder2 < 0 ? "-" : f2lebt ? "lebt" : "weg")}; ");
        sb.Append($"Kacheln {_tsKacheln}/9, imap {_tsImapOk}/9, Bauplatz dort {_tsDepositJa}, Grundriss {_tsGrundrissJa}, ");
        sb.Append($"anderswo nicht {_tsAndersNein} (Mine dort abgelehnt {_tsMineAndersAbgelehnt}); ");
        sb.Append($"Mine bestellt {_tsMineGesetzt}, gebaut {gebaut}; Bauplaetze jetzt {_deposits.Count}");
        bool soll;
        string art;
        if (ErzSofortBaubar)
        {
            art = "Nullmodell"; soll = _tsStartBau == (_mscript?.Terra?.Count ?? -1) && _tsStartBau > 0 && TerraFunde == 0;
        }
        else if (TerrasucheAus)
        {
            art = "Nullmodell"; soll = _tsStartBau == 0 && TerraBefehle == 0 && TerraFunde == 0 && !f1tot;
        }
        else
        {
            bool basis = _tsStartBau == 0 && _tsStartErzBaubar == 0 && TerraBefehle >= 1 && TerraFunde == 1
                         && TerraKlang51 == 1 && f1tot && _tsDepositJa && _tsAndersNein && _tsMineAndersAbgelehnt
                         && (_tsFinder2 < 0 || (f2lebt && TerraFehlsuchen >= 1));
            bool bohr = TerrasucheOhneBohren ? TerraBohrstopps == 0 : bohr1 >= 2 && bohr1 <= 4;
            bool marke = TerramarkeAus ? _tsKacheln == 0 && _tsImapOk < 9 : _tsKacheln == 9 && _tsImapOk == 9;
            art = schalter.Count > 0 ? "Gegenschalter" : "";
            soll = basis && bohr && marke && _tsMineGesetzt;
        }
        sb.Append(soll ? $" — {art} BESTANDEN".Replace("  ", " ") : $" — {art} DURCHGEFALLEN".Replace("  ", " "));
        return sb.ToString();
    }
}
