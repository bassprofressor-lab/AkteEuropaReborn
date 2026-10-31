namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>DER LEGEAUFTRAG DES MINEN- UND DES FALLENLEGERS</b> (30.10.2026, bug-698).
/// Gelesen in beiden GAME.EXE (berichte/fehlende-funktionen-opus.md §2,
/// berichte/bau-fallen-legeauftrag.md), selbst nachgelesen in C:
///
/// <code>
/// Menue    Aufsatz 0x23 -> Zeile 0x0A »Mine legen«, 0x24 -> 0x0B »Falle legen«
///          Knopf @0x4488F7: dword[0x502ACC] := 8, Rahmenzustand 0xA32180 := 0
/// Zeiger   @0x431923 Zustand 16 (Bild 20); 0xA32180:
///            0: Taste losgelassen          -> 1
///            1: Taste gedrueckt            -> Anker := Maus, 2
///            2: gedrueckt: Vorschau 0x43A1B0 (je Zelle imap == 0xFFFE oder &lt; 8000);
///               losgelassen: Absender 0x43A110, Befehlsart 0; rechte Taste: Abbruch
///          also DRUECKEN — ZIEHEN — LOSLASSEN, ein Rahmen aus Anker- und Mauszelle
///          (0x43A020: min/max je Achse, beide Ecken einschliesslich)
/// Bef. 22  Absender 0x43A134: (Einheit 0x4FA0C8, X0, Y0, x1−X0+1, y1−Y0+1)
///          Behandler C 0x4C33D0: UKOL := 0, +0x1A := 0xFF,
///          word[+0x40] := (Y0&lt;&lt;8)+X0, +0x48 := B, +0x49 := H
/// Takt     Leerlauf UKOL 0 (0x407F38, Sprit &gt; 0), Aufsatzweiche ohne Fall ->
///          0x40877A: ZBRAN +0x0D == 15 Minenarm (0x408790), == 16 Fallenarm (0x4088DC):
///            +0x48 == 0 oder Munition +0x39 == 0      -> nichts
///            eigene Zelle im Rahmen und 0x421D40 leer (keine Mine/Falle/Gaswolke):
///              +0x32 != 0 -> warten; sonst lay_mine/lay_trap(Zelle, Spieler),
///              Klang 0x1C + rand&amp;1 (28/29) bzw. 0x1E + rand&amp;1 (30/31) an der
///              Einheit, +0x32 := +0x3D, --+0x39
///            sonst 0x4118C0: erste Zelle des Rahmens, Zeile aussen, mit imap ==
///              0xFFFE und 0x421D40 leer -> fahre (0x40B070); keine -> +0x48 := 0
/// Abbruch  +0x48 := 0 bei ZBRAN 15/16 in den Behandlern 3 (0x4C2896), 4 (0x4C299F),
///          10 (0x4C2DB9, ohne Absender), 11 (0x4C2E5F), 12 (0x4C2F1C), 17 (0x4C30A4)
/// </code>
///
/// <para>⚠ <b>UNSERE SETZUNGEN:</b></para>
/// <list type="bullet">
/// <item>ZBRAN ist <c>Comp0D</c> (+0x0D); fehlt es (0), nehmen wir die Tafelzeile
///   des Aufsatzes (<c>WeaponRowOf</c>, 35 -> 15, 36 -> 16).</item>
/// <item><c>+0x32 := +0x3D</c> ist bei uns <c>Cooldown := Reload · Nachladetakt</c> —
///   derselbe Zaehler, den <c>UpdateCombat</c> je Takt herunterzaehlt. Reload 0 heisst
///   wie im Original: sofort wieder.</item>
/// <item>Findet die Wegsuche zum naechsten Punkt keinen Weg, warten wir 50 Takte, bevor
///   wir es wieder versuchen (die Wegsuche ist bei uns teurer; wie TerraSuche.cs).</item>
/// <item>Der Abbruch steht hinter dem angenommenen Befehl (VERMUTET: im Original
///   vorn im Behandler — die Wirkung ist dieselbe).</item>
/// <item>Der Klang wuerfelt mit dem KLANGwuerfel (<c>GameSounds.KlangRoll</c>), nicht
///   mit dem Simulationszufall wie das Original — sonst verschoebe ein Klang die
///   Simulation.</item>
/// <item>Die Vorschau zeichnet unsere Bauvorschau (gruen/rot), nicht die Marken
///   0x90/0x9C (ihre Bilder sind ungelesen, Landungsbruecke.cs).</item>
/// </list>
/// <para>Gegenschalter <c>--legeauftrag-aus</c> (das Menue antwortet wieder »noch
/// nicht gebaut«, Befehl 22 wird verworfen, der Arm ruht). Pruefstand
/// <c>--legeauftrag-check</c> (K18).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Unser Setzmodus fuer <c>dword[0x502ACC] = 8</c> (unsere Nummern
    /// weichen ab, siehe Landungsbruecke.cs).</summary>
    public const int OrderLegen = 11;

    /// <summary>Die zwei Menuezeilen der Befehlsliste 0x4FD660.</summary>
    public const int CodeMineLegen = 0x0A, CodeFalleLegen = 0x0B;

    /// <summary>Zeigerzustand 16 -> Bild 20 (Tafel 0x4A9BEC).</summary>
    public const int ZeigerLegen = 20;

    /// <summary>ZBRAN +0x0D des Minen- bzw. Fallenlegers.</summary>
    public const int ZbranMine = 15, ZbranFalle = 16;

    /// <summary><c>--legeauftrag-aus</c> — der Stand bis 30.10.2026.</summary>
    public static bool LegeauftragAus;

    public int LegeBefehle, LegeMinen, LegeFallen, LegeFahrten, LegeFertig, LegeAbbrueche, LegeKeinWeg, LegeWarten;

    /// <summary>Gespielte Legeklaenge 28..31.</summary>
    public readonly int[] LegeKlaenge = new int[4];

    /// <summary>Die Ankerzelle des Rahmens (Zustand 2) — setzt der Kartenschirm,
    /// solange die linke Taste gedrueckt ist.</summary>
    public Vector2I? LegeAnker;

    private readonly Dictionary<Entity, int> _legeWarten = new();

    /// <summary>Welcher Arm: 15 Mine, 16 Falle, sonst 0.</summary>
    private static int LegerArt(Entity e)
    {
        if (e.Comp0D is ZbranMine or ZbranFalle) return e.Comp0D;
        int w = WeaponRowOf(e.Weapon);                       // UNSERE SETZUNG, Kopf
        return w is ZbranMine or ZbranFalle ? w : 0;
    }

    // ---- das Menue ----------------------------------------------------------------

    /// <summary>Menuezeile 0x0A/0x0B — @0x4488F7: Befehlsart 8, Rahmenzustand 0.</summary>
    public string LegeauftragBeginnen(int code)
    {
        int idx = MenueEinheit();
        if (idx < 0) return "keine Einheit gewaehlt.";
        var e = _entities[idx];
        int soll = code == CodeMineLegen ? MinenLegerTeil : FallenLegerTeil;
        if (e.Weapon != soll) return $"»{MenueWort(code)}«: die gewaehlte Einheit ist kein {(code == CodeMineLegen ? "Minen" : "Fallen")}leger.";
        PlacementMode = OrderLegen;
        PlacementUnit = idx;
        LegeAnker = null;
        BuildOrderNote = $"{MenueWort(code)}: Rahmen aufziehen (Esc bricht ab)";
        return "";
    }

    /// <summary>Die Vorschau 0x43A1B0: je Zelle des Rahmens ein Ja/Nein,
    /// <c>imap == 0xFFFE || imap &lt; 8000</c>.</summary>
    private void LegeVorschau(int col, int row)
    {
        var a = LegeAnker ?? new Vector2I(col, row);
        int x0 = Mathf.Min(a.X, col), x1 = Mathf.Max(a.X, col);
        int y0 = Mathf.Min(a.Y, row), y1 = Mathf.Max(a.Y, row);
        _previewType = 98;                       // Platzhaltertyp wie die Mole (99)
        _previewCol = x0; _previewRow = y0; _previewOk = true;
        _previewCells.Clear();
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (_nav != null && _nav.InBounds(x, y)) _previewCells.Add(new SiteCell(x, y, LegeVorschauZelle(x, y)));
        QueueRedraw();
    }

    private bool LegeVorschauZelle(int c, int r)
    {
        if (_nav == null || !_nav.InBounds(c, r)) return false;
        int drauf = _nav.BesetztVon(c, r);
        var ding = drauf >= 0 && drauf < _entities.Count ? _entities[drauf] : null;
        if (ding != null) return !ding.IsBuilding && !ding.IsProp && !ding.Dead && ding.Infantry < 0;
        return _nav.GroundAt(c, r) == Simulation.NavGrid.Ground.Free && !GrundrissSperrt(c, r);   // bug-706
    }

    /// <summary>Das Loslassen — Absender 0x43A110: Befehl 22 ueber den Ring, Modus 0.</summary>
    public bool LegeRahmenKlick(Vector2I a, Vector2I b)
    {
        int idx = PlacementUnit;
        CancelPlacement();
        LegeAnker = null;
        if (idx < 0 || idx >= _entities.Count) { BuildOrderNote = "die Einheit ist weg"; return false; }
        var e = _entities[idx];
        if (e.Dead || e.Owner != ViewPlayer) { BuildOrderNote = "die Einheit ist weg"; return false; }
        int x0 = Mathf.Min(a.X, b.X), x1 = Mathf.Max(a.X, b.X);
        int y0 = Mathf.Min(a.Y, b.Y), y1 = Mathf.Max(a.Y, b.Y);
        if (!Emit(CommandRecord.Make(CommandOp.Legeauftrag, (byte)ViewPlayer,
                                     (short)idx, (short)x0, (short)y0, (short)(x1 - x0 + 1), (short)(y1 - y0 + 1))))
        { BuildOrderNote = "der Befehl liess sich nicht absetzen"; return false; }
        BuildOrderNote = $"Legeauftrag: ({x0},{y0}) bis ({x1},{y1})";
        GD.Print($"legeauftrag: Befehl 22 abgesetzt — Einheit {idx}, Rahmen ({x0},{y0}) {x1 - x0 + 1}x{y1 - y0 + 1} (0x43A110)");
        return true;
    }

    // ---- der Befehl -----------------------------------------------------------------

    /// <summary>Behandler C 0x4C33D0.</summary>
    private bool ApplyLegeauftrag(in CommandRecord c)
    {
        if (LegeauftragAus) return false;
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (e.Dead || e.IsBuilding || e.IsProp) return false;
        // UKOL := 0, +0x1A := 0xFF — wie der Halt (Befehl 12), aber ohne Abbruch
        if (e.Reserved is { } r) _nav?.ClearOccupant(r.X, r.Y, i);
        e.Path = null; e.SpritWeg = null; e.Reserved = null;
        e.Orders.Clear(); e.Target = -1;
        AuftragLoeschen(e);
        e.LegeX = c.P2 & 0xFF; e.LegeY = c.P3 & 0xFF;                       // word[+0x40]
        e.LegeB = c.P4 & 0xFF; e.LegeH = c.P5 & 0xFF;                       // +0x48 / +0x49 (Bytes)
        _legeWarten.Remove(e);
        LegeBefehle++;
        GD.Print($"legeauftrag: Befehl 22 angenommen — Einheit {i} »{LabelOf(e)}« Rahmen ({e.LegeX},{e.LegeY}) {e.LegeB}x{e.LegeH}");
        return true;
    }

    /// <summary>Die Abbrueche der Behandler 3/4/11/12/17: ein anderer Befehl fuer
    /// einen Leger beendet den Auftrag (+0x48 := 0).</summary>
    private void LegeAbbruch(in CommandRecord c)
    {
        if (c.Op is not (CommandOp.Move or CommandOp.HandUebernehmen or CommandOp.Attack or CommandOp.OursAttack
                         or CommandOp.Stop or CommandOp.OursStop or CommandOp.Board)) return;
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return;
        var e = _entities[i];
        if (e.LegeB == 0 || LegerArt(e) == 0) return;
        e.LegeB = 0;
        LegeAbbrueche++;
    }

    // ---- der Arm ----------------------------------------------------------------------

    /// <summary>Jeden Originaltakt, im Leerlaufverteiler neben dem Terranium-Finder.</summary>
    private void LegeauftragTakt()
    {
        if (LegeauftragAus || _nav == null) return;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.LegeB == 0 || e.Dead || e.IsBuilding || e.IsProp) continue;
            int art = LegerArt(e);
            if (art == 0) continue;
            if (e.Path != null || e.Orders.Count > 0 || Untergestellt(e)) continue;    // UKOL 0
            if (e.FuelMax > 0 && e.Fuel <= 0) continue;                                // 0x407F09
            if (e.Ammo <= 0) continue;                                                 // +0x39
            if (_legeWarten.TryGetValue(e, out int w) && w > 0) { _legeWarten[e] = w - 1; continue; }
            int x0 = e.LegeX, y0 = e.LegeY;
            bool drin = e.Col >= x0 && e.Col < x0 + e.LegeB && e.Row >= y0 && e.Row < y0 + e.LegeH;
            if (drin && !MineOderFalleAufZelle(e.Col, e.Row))
            {
                if (e.Cooldown > 0) { LegeWarten++; continue; }                        // +0x32
                bool gelegt = art == ZbranMine ? MineLegen(e.Col, e.Row, e.Owner) : FalleLegen(e.Col, e.Row, e.Owner);
                if (gelegt) { if (art == ZbranMine) LegeMinen++; else LegeFallen++; }
                int klang = (art == ZbranMine ? MinenKlang : 30) + Audio.GameSounds.KlangRoll(2);
                LegeKlaenge[klang - 28]++;
                Audio.GameSounds.PlayAt(klang, e.Col, e.Row);                         // 0x4047E0(k, 1, E, 0)
                e.Cooldown = e.Reload * ReloadTick;                                    // +0x32 := +0x3D
                e.Ammo--;                                                              // --+0x39
                continue;
            }
            // 0x4118C0 — die erste freie Zelle des Rahmens, Zeile aussen
            Vector2I? ziel = null;
            for (int y = y0; y < y0 + e.LegeH && ziel == null; y++)
                for (int x = x0; x < x0 + e.LegeB; x++)
                    if (ImapWortFrei(x, y) && !GrundrissSperrt(x, y) && !MineOderFalleAufZelle(x, y))   // bug-706
                    { ziel = new Vector2I(x, y); break; }
            if (ziel == null)
            {
                e.LegeB = 0;                                                           // @0x4088D3
                LegeFertig++;
                GD.Print($"legeauftrag: Einheit {i} — kein freier Punkt mehr im Rahmen, Auftrag zu Ende (Munition {e.Ammo})");
                continue;
            }
            if (TerraFahre(i, ziel.Value.X, ziel.Value.Y)) LegeFahrten++;              // 0x40B070
            else { LegeKeinWeg++; _legeWarten[e] = 50; }                               // UNSERE Drossel
        }
    }

    // ---- --legeauftrag-check ---------------------------------------------------------

    /// <summary>
    /// <c>--legeauftrag-check</c>, Kampagne 18 (P0 hat 3 Minenleger, Entwurf 77, und 3
    /// Fallenleger, Entwurf 79). Der SPIELERWEG: Auswahl, Menuezeile 0x0A/0x0B, Rahmen
    /// aufziehen (<see cref="LegeRahmenKlick"/>, derselbe Weg wie der Kartenschirm),
    /// Befehl 22 ueber den Ring, dann echte Takte.
    /// <list type="bullet">
    /// <item>Minenleger: Rahmen 3×2 an freier Stelle -> genau min(6, Munition) Minen,
    /// alle im Rahmen, Leger = P0, Munition −n, Klang 28/29 n-mal, Auftrag danach zu Ende.</item>
    /// <item>Fallenleger: dasselbe mit Fallen, Klang 30/31.</item>
    /// <item>Abbruch: ein Fahrbefehl waehrend des Auftrags setzt +0x48 := 0.</item>
    /// </list>
    /// Nullmodell <c>--legeauftrag-aus</c>: 0 Minen, 0 Fallen.
    /// </summary>
    public string LegeauftragCheck()
    {
        var sb = new StringBuilder("legeauftrag-check\n");
        EnsureMissionScript();
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (LegeauftragAus) sb.AppendLine("  ⚠ NULLMODELL --legeauftrag-aus: hier MUSS der Lauf durchfallen");
        var gehoert = new List<int>();
        var vorherHorcher = Audio.SoundBankPlayer.Horcher;
        Audio.SoundBankPlayer.Horcher = (s, c, r) => { gehoert.Add(s); vorherHorcher?.Invoke(s, c, r); };
        var benutzt = new HashSet<Vector2I>();
        try
        {
            var minenleger = new List<int>(); var fallenleger = new List<int>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Owner != ViewPlayer) continue;
                if (e.Weapon == MinenLegerTeil) minenleger.Add(i);
                else if (e.Weapon == FallenLegerTeil) fallenleger.Add(i);
            }
            sb.AppendLine($"  P{ViewPlayer}: {minenleger.Count} Minenleger, {fallenleger.Count} Fallenleger");
            Soll(minenleger.Count == 3 && fallenleger.Count == 3, "K18: 3 Minen- und 3 Fallenleger beim Spieler");

            // ein freier Rahmen B x H nahe der Einheit, ohne Minen/Fallen, alles 0xFFFE
            Vector2I? Rahmen(Entity e, int bw, int bh)
            {
                for (int d = 2; d <= 14; d++)
                    for (int dy = -d; dy <= d; dy++)
                        for (int dx = -d; dx <= d; dx++)
                        {
                            if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != d) continue;
                            int x0 = e.Col + dx, y0 = e.Row + dy;
                            bool alle = true;
                            for (int y = y0 - 1; y <= y0 + bh && alle; y++)
                                for (int x = x0 - 1; x <= x0 + bw && alle; x++)
                                    alle = _nav.InBounds(x, y) && ImapWortFrei(x, y) && GebaeudeAufZelle(x, y) < 0
                                           && !MineOderFalleAufZelle(x, y) && !benutzt.Contains(new Vector2I(x, y));
                            if (!alle) continue;
                            if (_nav.FindPath(new Vector2I(e.Col, e.Row), new Vector2I(x0, y0), e.Move, -1) == null) continue;
                            for (int y = y0 - 1; y <= y0 + bh; y++)
                                for (int x = x0 - 1; x <= x0 + bw; x++) benutzt.Add(new Vector2I(x, y));
                            return new Vector2I(x0, y0);
                        }
                return null;
            }

            void Lauf(string wer, int idx, int code, bool mine)
            {
                var e = _entities[idx];
                if (e.FuelMax > 0) e.Fuel = e.FuelMax;
                if (e.AmmoMax > 0) e.Ammo = e.AmmoMax;
                const int bw = 3, bh = 2;
                var r0 = Rahmen(e, bw, bh);
                if (r0 == null) { Soll(false, $"{wer}: kein freier Rahmen nahe ({e.Col},{e.Row})"); return; }
                var r = r0.Value;
                int am0 = e.Ammo, h0 = gehoert.Count, gel0 = mine ? LegeMinen : LegeFallen, aus0 = mine ? MinenAusgeloest : FallenAusgeloest;
                int n0 = mine ? MinenAktiv() : FallenAktiv();
                _sel.Clear(); _sel.Add(idx); _selected = idx;
                string menue = MenueAktion(code);
                int modus = PlacementMode;
                int zeiger = BefehlsartZeigerbild(r.X, r.Y);
                LegeAnker = r;
                PlacementHover(r.X + bw - 1, r.Y + bh - 1);
                int vorschau = _previewCells.Count;
                bool posted = LegeRahmenKlick(r, new Vector2I(r.X + bw - 1, r.Y + bh - 1));
                sb.AppendLine($"  {wer}: Platz {e.Slot} Comp0D {e.Comp0D} Reload {e.Reload} Munition {e.Ammo}/{e.AmmoMax} auf ({e.Col},{e.Row}); "
                            + $"Menue »{(menue.Length == 0 ? "ok" : menue)}«, Modus {modus}, Zeiger {zeiger}, Vorschau {vorschau} Zellen, "
                            + $"Rahmen ({r.X},{r.Y}) {bw}x{bh} -> {(posted ? "Befehl 22" : "KEIN Befehl")}");
                int t = 0;
                int geheilt = 0;
                // ⚠ EINGRIFF: der Leger wird jeden Takt geheilt — auf K18 schiesst der Feind (seit dem
                // KI-Umbau vom 30.10. starb der Fallenleger im Lauf); gemessen wird das Legen.
                while (t < 3000 && (t < 3 || e.LegeB != 0) && !e.Dead)
                { if (e.Hp < e.HpMax) { e.Hp = e.HpMax; geheilt++; } SimTickFuerProbe(); t++; }
                if (geheilt > 0) sb.AppendLine($"  ⚠ EINGRIFF: {wer} {geheilt}x geheilt (Feindbeschuss)");
                int soll = System.Math.Min(bw * bh, am0);
                // ⚠ gezaehlt wird das GELEGTE: auf K18 laeuft der Feind auf die frischen Fallen/Minen
                // (im Lauf vom 30.10. abends fing eine Falle ein Feindfahrzeug) — die liegen dann nicht mehr
                int n = (mine ? LegeMinen : LegeFallen) - gel0;
                int weg = (mine ? MinenAusgeloest : FallenAusgeloest) - aus0;
                int imRahmen = 0, falscherLeger = 0;
                var feld = mine ? _minen : _fallen;
                foreach (var m in feld)
                {
                    if (!m.Aktiv || m.Col < r.X || m.Col >= r.X + bw || m.Row < r.Y || m.Row >= r.Y + bh) continue;
                    imRahmen++;
                    if (m.Player != e.Owner) falscherLeger++;
                }
                var neu = gehoert.GetRange(h0, gehoert.Count - h0);
                int ka = neu.FindAll(s => s == (mine ? 28 : 30)).Count, kb = neu.FindAll(s => s == (mine ? 29 : 31)).Count;
                Soll(posted && n == soll && imRahmen + weg >= soll && imRahmen <= soll && falscherLeger == 0 && e.Ammo == am0 - soll && ka + kb == soll && e.LegeB == 0,
                     $"{wer}: nach {t} Takten {n} {(mine ? "Minen" : "Fallen")} gelegt (Soll {soll}), im Rahmen liegend {imRahmen}, inzwischen ausgeloest {weg}, fremder Leger {falscherLeger}, "
                     + $"Munition {am0} -> {e.Ammo}, Klang {(mine ? "28/29" : "30/31")} {ka}/{kb}, Auftrag {(e.LegeB == 0 ? "zu Ende" : $"laeuft noch ({e.LegeB}x{e.LegeH})")}, steht ({e.Col},{e.Row}){(e.Dead ? ", ⚠ TOT (Feindbeschuss?)" : "")}, TP {e.Hp}/{e.HpMax}");
                _sel.Clear(); _selected = -1;
            }

            if (minenleger.Count > 0) Lauf("Minenleger", minenleger[0], CodeMineLegen, mine: true);
            if (fallenleger.Count > 0) Lauf("Fallenleger", fallenleger[0], CodeFalleLegen, mine: false);

            // Abbruch: Auftrag erteilen, dann ein Fahrbefehl
            int abIdx = -1;
            var kandidaten = new List<int>(minenleger.GetRange(1, System.Math.Max(0, minenleger.Count - 1)));
            kandidaten.AddRange(fallenleger.GetRange(1, System.Math.Max(0, fallenleger.Count - 1)));
            foreach (int k in kandidaten)
                if (!_entities[k].Dead) { abIdx = k; break; }
            if (abIdx < 0) Soll(false, "Abbruch: kein lebender Leger mehr (Feindbeschuss)");
            else
            {
                int idx = abIdx;
                var e = _entities[idx];
                if (e.AmmoMax > 0) e.Ammo = e.AmmoMax;
                var r0 = Rahmen(e, 2, 2);
                if (r0 == null) sb.AppendLine("  (Abbruch entfaellt: kein Rahmen)");
                else
                {
                    _sel.Clear(); _sel.Add(idx); _selected = idx;
                    MenueAktion(e.Weapon == MinenLegerTeil ? CodeMineLegen : CodeFalleLegen);
                    string m2 = MenueEinheit() == idx ? "" : "Menueeinheit falsch";
                    bool p2 = LegeRahmenKlick(r0.Value, r0.Value + new Vector2I(1, 1));
                    for (int k = 0; k < 3; k++) SimTickFuerProbe();
                    int b0 = e.LegeB, ab0 = LegeAbbrueche;
                    Emit(CommandRecord.Make(CommandOp.Stop, (byte)ViewPlayer, (short)idx));   // Befehl 12, Abbruch 0x4C2F1C
                    for (int k = 0; k < 3; k++) SimTickFuerProbe();
                    Soll(b0 == 2 && e.LegeB == 0 && LegeAbbrueche == ab0 + 1,
                         $"Abbruch: Platz {e.Slot}{(e.Dead ? " TOT" : "")} {m2}, Befehl 22 {(p2 ? "abgesetzt" : "NICHT abgesetzt")}, Auftrag 2x2 (+0x48 = {b0}), Halt (Befehl 12) -> +0x48 = {e.LegeB}, Abbrueche {LegeAbbrueche - ab0}");
                    _sel.Clear(); _selected = -1;
                }
            }
        }
        finally { Audio.SoundBankPlayer.Horcher = vorherHorcher; }
        sb.AppendLine($"  Zaehler: Befehle {LegeBefehle}, Minen {LegeMinen}, Fallen {LegeFallen}, Fahrten {LegeFahrten}, fertig {LegeFertig}, "
                    + $"Abbrueche {LegeAbbrueche}, kein Weg {LegeKeinWeg}, Nachladewarten {LegeWarten}, Klaenge 28..31 {string.Join("/", LegeKlaenge)}; "
                    + $"--legeauftrag-aus {LegeauftragAus}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
