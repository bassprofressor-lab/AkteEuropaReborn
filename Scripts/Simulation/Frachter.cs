using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DER RAUMFRACHTER</b> (»mer_ship«) — 13.09.2026. Seine Meldung aus Kampagne
/// 11: »Wenn ein Geschäftszentrum eine Einheit bringt, kommt wie ein Raumschiff
/// und lädt es ab, bei uns spawnt die Einheit nur.« Lesung:
/// <c>berichte/geschaeftszentrum-lieferung-fable.md</c>.
///
/// <para>Die Fahrt war längst gebaut (<see cref="CollectorTick"/>,
/// <c>MissionScript.TickIncoming</c>), aber unsichtbar und am Ziel gelöscht.
/// Was hier dazukommt, ist alles aus beiden GAME.EXE:</para>
/// <list type="bullet">
/// <item><b>Abflug</b>: nach dem Abladen <c>+0x04 := 0xFF</c> (@0x4C0710), dann
/// eine Spalte je Takt nach rechts (@0x4C04D6), bis <c>x − 2 &gt; Breite</c>
/// (@0x4C04C6). Für Markt (Art 1/2) und <c>space_in</c> (Art 3) gleich.</item>
/// <item><b>Bild</b>: Zeichenlistenart 0x28, Bauer <c>0x42F830</c>, Zeichner
/// <c>0x42C7E0</c> — ANIM-Folge 910 Bild 0 (209 × 106, yoff 79),
/// <c>sx = 40·x + Rest − 20</c>, <c>sy = 20·y − 260</c>, zuerst der Schatten bei
/// <c>sy + 15·(9 − h)</c>, dann das Schiff; Korb Zeile + 2; sichtbar, sobald
/// eine von (x,y) (x,y−10) (x+4,y) (x+4,y−10) aufgedeckt ist.</item>
/// <item><b>Abladen</b>: Rumpf <c>rand()&amp;7</c> (Chassis 9: 0), Turm
/// <c>rand()&amp;7</c> (@0x4C1540/@0x4C1552) und der Lichtblitz ANIM-Folge 96
/// an der Zelle (@0x4C158D). Kein Klang in der ganzen Kette.</item>
/// </list>
///
/// <para>⚠ UNSERE Setzungen: der Schatten ist wie überall Schwarz mit
/// <see cref="ShadowAlpha"/> statt der NN.CWS-Tafel; der Lichtblitz sitzt auf
/// der Körpermitte der Einheit und läuft 20 ms je Bild — der Zeichner der
/// Effektliste sec42 ist nicht gelesen. Die Landezelle bleibt
/// <c>NearestFree</c> (ab Ring 2 andere Ordnung als die Ringtafel, auf allen 13
/// Kampagnenmärkten ist der Anker selbst frei).</para>
///
/// <para>Gegenschalter <c>--frachter-aus</c> (<see cref="Campaign.MissionScript.FrachterAus"/>).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Wie oft ein Frachter gezeichnet wurde — für den Prüfstand.</summary>
    public int FrachterGezeichnet;

    /// <summary>Wie viele Lichtblitze beim Abladen gesetzt wurden.</summary>
    public int Lichtblitze;

    /// <summary>Folge 910 hat EIN Bild (Rahmen 1512); der Zeichner addiert keine
    /// Phase.</summary>
    private const string FrachterBild = "frachter";

    /// <summary>ANIM-Folge 96, sieben Bilder, eines je Takt (0x435B20).</summary>
    private const string LichtblitzBild = "lichtblitz";

    /// <summary>Rumpf und Turm würfeln, Lichtblitz setzen — der Teil von
    /// <c>deliver_one</c> 0x4C1480 bzw. 0x4C1600 nach dem Stempeln.</summary>
    private void AbladenSchmuck(int idx)
    {
        if (Campaign.MissionScript.FrachterAus) return;
        if (idx < 0 || idx >= _entities.Count) return;
        var u = _entities[idx];
        int rumpf = Simulation.Determinism.Roll(8);           // 0x4C5B30 & 7
        u.Facing = u.Chassis == 9 ? 0 : rumpf;                // @0x4C1540
        u.AimFacing = Simulation.Determinism.Roll(8);         // @0x4C1552
        _effects.Add(new Effect { Pos = u.Pos, Kind = LichtblitzBild, FrameTime = 1f / SimHz });
        Lichtblitze++;
    }

    private void SpaceInAbladen(int slot)
    {
        if (slot < 0) return;
        for (int i = _entities.Count - 1; i >= 0; i--)
            if (!_entities[i].IsBuilding && _entities[i].Slot == slot) { AbladenSchmuck(i); return; }
    }

    /// <summary>Die Frachter, deren Korb diese Zeile ist (Zelle + 2) — im
    /// Zeilendurchgang neben den Radarmasten.</summary>
    private void FrachterZeichnen(int zeile)
    {
        if (Campaign.MissionScript.FrachterAus) return;
        bool markt = _collectors.Count > 0;
        bool skript = _mscript != null;
        if (!markt && !skript) return;
        var bilder = EffectFrames(FrachterBild);
        if (bilder.Count == 0) return;
        var bild = bilder[0];
        foreach (var s in _collectors) EinFrachter(bild, s.Col, s.Row, s.Frac, zeile);
        if (_mscript != null)
            foreach (var f in _mscript.FrachterInDerLuft()) EinFrachter(bild, f.X, f.Y, f.Fein, zeile);
    }

    private void EinFrachter(Texture2D bild, int x, int y, int rest, int zeile)
    {
        if (y + 2 != zeile) return;                                  // Korb @0x42F95F
        if (FogActive && _fog != null &&
            !_fog.IsSeen(x, y) && !_fog.IsSeen(x, y - 10) &&
            !_fog.IsSeen(x + 4, y) && !_fog.IsSeen(x + 4, y - 10)) return;   // @0x42F880..0x42F90F
        // Die PNG traegt den yoff (79) schon im Rahmen, siehe InterfaceExporter.Canvas.
        var oben = new Vector2(_ox + x * TileW + rest - 20, _oy + y * TileH - 260);
        int h = ElevOf(x, y);                                        // terrain_at 0x41D0E0
        DrawTexture(bild, oben + new Vector2(0, 15 * (9 - h)), ShadowTint);   // 0x4AC6D0
        DrawTexture(bild, oben);                                     // 0x4ACCD0
        FrachterGezeichnet++;
    }

    // ---- --lieferung-check ---------------------------------------------------

    /// <summary>
    /// <c>--lieferung-check</c> (K11): ein Kauf am Handelsposten, dann echte
    /// Takte. Gemessen: Start in der Phase 222, Ankunft nach der gerechneten
    /// Flugzeit, die Einheit erscheint ERST DANN, der Frachter ist danach noch
    /// in der Luft und fliegt nach rechts, bis <c>x − 2 &gt; Breite</c>; ein
    /// Lichtblitz je Stück. Nullmodell <c>--frachter-aus</c>: der Frachter ist im
    /// Ankunftstakt weg und es gibt keinen Blitz.
    /// </summary>
    public string LieferungCheck()
    {
        var sb = new System.Text.StringBuilder("lieferung-check\n");
        int fehler = 0;
        void Soll(bool b, string was) { sb.Append(b ? "  ok     " : "  FEHLER ").Append(was).Append('\n'); if (!b) fehler++; }
        bool aus = Campaign.MissionScript.FrachterAus;
        if (aus) sb.Append("  ⚠ NULLMODELL --frachter-aus: Abflug und Blitz MUESSEN fehlen\n");
        Soll(EffectFrames(FrachterBild).Count == 1, $"Effects/{FrachterBild}: {EffectFrames(FrachterBild).Count} Bild (1 — sonst --reexport-effects)");
        Soll(EffectFrames(LichtblitzBild).Count == 7, $"Effects/{LichtblitzBild}: {EffectFrames(LichtblitzBild).Count} Bilder (7)");

        Entity? markt = null;
        foreach (var e in _entities) if (e.IsBuilding && !e.Dead && e.BType == 17) { markt = e; break; }
        if (markt == null) return sb.Append("  KEIN URTEIL: kein Geschaeftszentrum").ToString();
        int owner = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        int t = 0;
        while (MarketShelf().Count == 0 && t < 2000) { SimTickFuerProbe(); t++; }
        if (MarketShelf().Count == 0) return sb.Append("  KEIN URTEIL: Regal leer").ToString();
        Money(owner, 1_000_000);                                     // EINGRIFF: Geld
        int einheiten0 = _entities.Count;
        MarketBuy(markt, 0);
        Soll(_entities.Count == einheiten0, "im Kauftakt entsteht keine Einheit");

        // bis zur Phase 222
        t = 0;
        while (_collectors.Count == 0 && t < 400) { SimTickFuerProbe(); t++; }
        Soll(_collectors.Count == 1 && _origTicks % 300 == 222,
             $"Frachter gestartet bei Originaltakt {_origTicks} (Phase {_origTicks % 300}, 222)");
        if (_collectors.Count == 0) return sb.Append("lieferung-check: FEHLER (kein Frachter)").ToString();
        var f = _collectors[0];
        int ziel = f.Target;
        // die Flugzeit nachrechnen, Ganzzahl fuer Ganzzahl (0x4C0508..0x4C0567)
        int soll = 0; { int x = f.Col, rest = f.Frac; while (x != ziel && soll < 10000) { int d = ziel - x; if (d > 10) x++; else { int st = Mathf.Max(1, 4 * d) + rest; if (st > 39) { x += st / 40; rest = st % 40; } else rest = st; } soll++; } }
        int blitze0 = Lichtblitze, flug = 0;
        while (_entities.Count == einheiten0 && flug < 2000) { SimTickFuerProbe(); flug++; }
        Soll(flug == soll, $"Ankunft nach {flug} Takten (gerechnet {soll}) an Spalte {ziel}, Zeile {f.Row}");
        Soll(_entities.Count == einheiten0 + 1, "die Einheit ist im Ankunftstakt da");
        if (_entities.Count == einheiten0 + 1)
        {
            var neu = _entities[^1];
            // 0x4C13E0: Ring 0 ist der Anker selbst, wenn er freies Land ist (auf allen 13 Kampagnenmaerkten)
            sb.Append($"  (Landezelle ({neu.Col},{neu.Row}), Anker ({markt.Col},{markt.Row}), Frachterziel ({ziel},{f.Row}); " +
                      $"Rumpf {neu.Facing}, Turm {neu.AimFacing})\n");
            if (_nav != null)
                sb.Append($"  (Anker bei uns nach der Landung: Boden {_nav.GroundAt(markt.Col, markt.Row)}, " +
                          $"Belegung {_nav.OccupantAt(markt.Col, markt.Row)}, " +
                          $"Gebaeude? {(_nav.OccupantAt(markt.Col, markt.Row) is int oi && oi >= 0 && oi < _entities.Count ? _entities[oi].IsBuilding.ToString() : "-")})\n");
            Soll(neu.Col == markt.Col && neu.Row == markt.Row, "gelandet auf dem Anker (Ringtafel-Eintrag 0)");
        }
        bool nochDa = _collectors.Contains(f);
        Soll(aus ? !nochDa : nochDa && f.Abflug, aus ? "Frachter im Ankunftstakt weg (Nullmodell)" : "Frachter nach dem Abladen noch in der Luft, Abflug gesetzt");
        Soll(aus ? Lichtblitze == blitze0 : Lichtblitze == blitze0 + 1, $"Lichtblitze {Lichtblitze - blitze0} ({(aus ? 0 : 1)})");
        if (!aus)
        {
            int breite = _nav?.Width ?? 256, weg = 0, x0 = f.Col;
            while (_collectors.Contains(f) && weg < 2000) { SimTickFuerProbe(); weg++; }
            int sollWeg = breite + 4 - x0;              // entfernt, sobald x = Breite + 3
            Soll(weg == sollWeg, $"Abflug: nach {weg} Takten hinter dem Rand (gerechnet {sollWeg}, Breite {breite})");
        }
        sb.Append(fehler == 0 ? "lieferung-check: IN ORDNUNG" : $"lieferung-check: {fehler} FEHLER");
        return sb.ToString();
    }

    /// <summary><c>--lieferung-bild</c>: kauft und laesst den Frachter bis
    /// <paramref name="vorZiel"/> Spalten vor das Ziel fliegen; gibt die
    /// Weltmitte zwischen Schiff und Landezelle fuer die Kamera.</summary>
    public Vector2? LieferungBildVorbereiten(int vorZiel)
    {
        Entity? markt = null;
        foreach (var e in _entities) if (e.IsBuilding && !e.Dead && e.BType == 17) { markt = e; break; }
        if (markt == null) return null;
        int t = 0;
        while (MarketShelf().Count == 0 && t < 2000) { SimTickFuerProbe(); t++; }
        Money(ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0, 1_000_000);
        MarketBuy(markt, 0);
        t = 0;
        while ((_collectors.Count == 0 || _collectors[0].Target - _collectors[0].Col > vorZiel) && t < 2000)
        { SimTickFuerProbe(); t++; }
        if (_collectors.Count == 0) return null;
        var f = _collectors[0];
        return new Vector2(_ox + f.Target * TileW, _oy + f.Row * TileH - 60);
    }

    public void LieferungBildTakte(int n) { for (int i = 0; i < n; i++) SimTickFuerProbe(); }

    public void LieferungBisAnkunft()
    {
        int t = 0;
        while (_collectors.Count > 0 && !_collectors[0].Abflug && t < 2000) { SimTickFuerProbe(); t++; }
    }
}
