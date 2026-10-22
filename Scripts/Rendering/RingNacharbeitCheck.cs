namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ <c>--ring-nacharbeit-check</c> (bug-600, 22.10.2026,
/// <c>berichte/ring-rueckfaelle-opus.md</c> §5 Schritt 3.4) — <b>liest ab, was das
/// FENSTER zeigt</b>, nachdem der Ring gewirkt hat, und ruft dabei NIE selbst
/// <c>Refresh()</c>. Genau diese Lücke hat bug-587 und bug-588 durchgelassen: kein
/// Prüfstand las den gezeigten Wert ohne eigenes Neumalen.
///
/// <para><b>Zwei Teile je Fenster, damit jeder Gegenschalter seinen eigenen
/// Durchfall hat:</b></para>
/// <list type="bullet">
/// <item><b>Teil A — Satznacharbeit.</b> Klick über den Spielerweg (PushInput bzw.
/// der Knopfweg des Spielers), Ring leerlaufen, sofort ablesen. Der Fenstertakt ist
/// für Teil A ANGEHALTEN (⚠ Eingriff), sonst könnte er zufällig im selben Bild neu
/// malen. Nullmodell <c>--satznacharbeit-alt</c> MUSS hier fallen.</item>
/// <item><b>Teil B — Fenstertakt.</b> Ein angezeigter Wert ändert sich OHNE Satz
/// (⚠ Eingriff: Konto/Lager um +7 verstellt, danach zurück), dann 35 Bilder warten
/// (Arm <c>0x45066C</c>: alle 30). Nullmodell <c>--fensterneumalen-alt</c> MUSS
/// hier fallen.</item>
/// </list>
///
/// <para>Was es misst, hängt an der Karte: K2 Nachschubposten (527, Kontostand),
/// eigene Mine (517 Start/Anhalten als UMSCHALTER, zweimal), K21 Bahnhof (518
/// Geisterzeile), überall wo Entwurfslisten stehen 505 (Werteliste des
/// Basisfensters, Nullmodell <c>--satznacharbeit-alt=katalog</c>). Was auf der Karte
/// fehlt, sagt »KEIN URTEIL« und zählt nicht.</para>
/// </summary>
public partial class MapViewer
{
    private bool _ringNacharbeitCheck;

    // ================= DER FENSTERTAKT (bug-587) ==============================

    /// <summary>⚠ UNSERE SETZUNG: der Fensterplatz des Originals (einer von 20
    /// Plätzen, Schritt <c>0xAD24</c>) versetzt die 30er-Phase je Fenster. Wir haben
    /// keine festen Plätze, und es gibt nur EIN Gebäudefenster — also Platz 0.</summary>
    private const int FensterplatzSetzung = 0;

    /// <summary>Welcher Bildzählerstand zuletzt geprüft wurde, und wie oft der Takt
    /// neu gemalt hat (Prüfstand).</summary>
    private int _neumalBild = -1;
    public int FenstertaktNeumalen;

    /// <summary>
    /// ⭐ bug-587 — <b>der Neuzeichnen-Arm des Fenstertakts</b>, Original
    /// <c>0x4505F0</c> Arm <c>0x45066C</c>: <c>if ((word[0x4FA248] + platz) % 30 == 0)
    /// call 0x4013E3(platz)</c> für die Fensterarten 2, 5, 8, 9, 11, 18–25, 27–29,
    /// 31, 33, 38 (Bytetafel <c>0x45070C</c>). <c>word[0x4FA248]</c> ist unser
    /// <see cref="MapEntityLayer.Bildzaehler"/> — er steigt je GEZEICHNETEM Bild,
    /// darum läuft der Arm hier im Bildlauf (<c>_Process</c>) und nicht im SimTick.
    /// Reine Anzeige: liest den Zustand, ändert nichts (Gleichschritt).
    /// Gegenschalter <c>--fensterneumalen-alt</c>.
    /// </summary>
    private void FensterNeumalenTakt()
    {
        if (UI.BuildingWindow.FensterneumalenAlt) return;
        var f = _gebaeudeFenster;
        if (f == null || !f.Visible) return;
        int bz = MapEntityLayer.Bildzaehler;
        if (bz == _neumalBild) return;                      // je Bild höchstens einmal
        _neumalBild = bz;
        if (!UI.BuildingWindow.ImFenstertakt((int)f.OffeneArt)) return;
        if ((bz + FensterplatzSetzung) % 30 != 0) return;
        FenstertaktNeumalen++;
        f.NeumalenBehutsam();
    }

    private async System.Threading.Tasks.Task RingNacharbeitLauf()
    {
        for (int i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var sb = new System.Text.StringBuilder("ring-nacharbeit-check (bug-587/588/600)\n");
        bool nullA = MapEntityLayer.NacharbeitAlt("fenster");
        bool nullB = UI.BuildingWindow.FensterneumalenAlt;
        bool nullK = MapEntityLayer.NacharbeitAlt("katalog");
        if (nullA) sb.Append("  ⚠ NULLMODELL --satznacharbeit-alt(=fenster): Teil A MUSS fallen\n");
        if (nullB) sb.Append("  ⚠ NULLMODELL --fensterneumalen-alt: Teil B MUSS fallen\n");
        if (nullK) sb.Append("  ⚠ NULLMODELL --satznacharbeit-alt(=katalog): 505 MUSS fallen\n");
        int gruenA = 0, rotA = 0, gruenB = 0, rotB = 0, gruenK = 0, rotK = 0;
        void A(bool b, string was) { sb.Append($"  {(b ? "ok  " : "ROT ")} A {was}\n"); if (b) gruenA++; else rotA++; }
        void B(bool b, string was) { sb.Append($"  {(b ? "ok  " : "ROT ")} B {was}\n"); if (b) gruenB++; else rotB++; }
        void K(bool b, string was) { sb.Append($"  {(b ? "ok  " : "ROT ")} K {was}\n"); if (b) gruenK++; else rotK++; }

        var f = _gebaeudeFenster;
        if (f == null) { GD.Print(sb.Append("  kein Gebaeudefenster gebaut\n  Ergebnis: KEIN URTEIL")); GetTree().Quit(0); return; }
        for (int i = 0; i < 400; i++) UI.HelpWindow.MerkeGezeigt(i);   // ⚠ Eingriff: Kontexthilfe deckt sonst zu
        var leinwand = GetViewport().GetVisibleRect().Size;
        var fenstermass = (Vector2)GetWindow().Size;
        var faktor = new Vector2(fenstermass.X / leinwand.X, fenstermass.Y / leinwand.Y);
        void Druck(Vector2 punkt)
        {
            var p = punkt * faktor;
            GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p, GlobalPosition = p });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p, GlobalPosition = p });
        }
        async System.Threading.Tasks.Task Oeffnen(int idx)
        {
            UI.WindowManager.Mausquelle = () => new Vector2(300, 200);
            _entities.PostenAnwaehlenWieKlick(idx);
            for (int t = 0; t <= UI.WindowManager.BilderAuf + 1; t++) UI.WindowManager.Takt();
            for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        async System.Threading.Tasks.Task Bilder(int n)
        {
            for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // Teil B: ⚠ EINGRIFF — das Spiel steht (Geschwindigkeit 0, wie die Pause),
        // nur der Bildzaehler laeuft. Sonst aendert der Takt (Mine foerdert) den Wert
        // zwischen Neumalen und Ablesen, und der Pruefstand misst den Foerderer.
        async System.Threading.Tasks.Task BilderAngehalten(int n)
        {
            int merk = MapEntityLayer.GameSpeed;
            MapEntityLayer.GameSpeed = 0;
            try { await Bilder(n); }
            finally { MapEntityLayer.GameSpeed = merk; }
        }
        // Teil A: Fenstertakt anhalten (Eingriff), damit nur die Nacharbeit malt.
        bool taktMerk = UI.BuildingWindow.FensterneumalenAlt;
        void TaktHalten(bool halten) => UI.BuildingWindow.FensterneumalenAlt = halten || taktMerk;

        // ---- 1. Nachschubposten, 527 Heli kaufen, Kontostand im Fenster -------
        int posten = _entities.PostenIndex();
        if (posten < 0) sb.Append("  -- 527 Nachschubposten: KEIN URTEIL, keiner auf dieser Karte\n");
        else
        {
            await Oeffnen(posten);
            var feld = f.KaufknopfAufDemSchirm(0);
            if (feld == null) sb.Append("  -- 527: KEIN URTEIL, kein Kaufknopf (WINDOWS.CWW?)\n");
            else
            {
                TaktHalten(true);
                int vor = _entities.GeldImFenster;
                Druck(feld.Value.Position + feld.Value.Size / 2f);
                int takte = _entities.RingLeeren();
                int spiel = _entities.GeldImFenster, gezeigt = f.AngezeigtesGeld;
                A(spiel < vor && gezeigt == spiel,
                  $"527 Heli-Kauf: Konto {vor} -> {spiel} (Ring {takte} Takt), Fenster zeigt {gezeigt}");
                TaktHalten(false);
                // Teil B
                _entities.GeldEingriff(+7);
                int soll = _entities.GeldImFenster;
                await BilderAngehalten(35);
                gezeigt = f.AngezeigtesGeld;
                B(gezeigt == soll, $"Posten: Konto ohne Satz +7 (Eingriff) -> {soll}, Fenster zeigt nach 35 Bildern {gezeigt}");
                _entities.GeldEingriff(-7);
            }
        }

        // ---- 2. eigene Mine, 517 Start/Anhalten (Umschalter, zweimal) ----------
        int mine = _entities.MinenIndex();
        if (mine < 0)
        {
            // Keine Kampagne beginnt mit eigener Mine (K3..K21 abgesucht) — eine wird
            // erst eingenommen. ⚠ EINGRIFF: die erste Mine der Karte dem Spieler geben.
            mine = _entities.MineProbeGeben();
            if (mine >= 0) sb.Append($"  ⚠ EINGRIFF: Mine Index {mine} dem Spieler gegeben (der Zustand nach der Einnahme)\n");
        }
        if (mine < 0) sb.Append("  -- 517 Mine: KEIN URTEIL, keine Mine auf dieser Karte\n");
        else
        {
            await Oeffnen(mine);
            var k = f.MinenknopfAufDemSchirm(2);
            if (k == null || !f.ZeigtOriginalMine) sb.Append("  -- 517: KEIN URTEIL, Minenfenster ohne Originalkacheln\n");
            else
            {
                TaktHalten(true);
                for (int runde = 1; runde <= 2; runde++)
                {
                    bool vor = _entities.MineLaeuft(mine);
                    Druck(k.Value.Position + k.Value.Size / 2f);
                    int takte = _entities.RingLeeren();
                    bool spiel = _entities.MineLaeuft(mine);
                    bool? gezeigt = f.AngezeigterStand?.Laeuft;
                    A(spiel != vor && gezeigt == spiel,
                      $"517 Mine Klick {runde}: laeuft {vor} -> {spiel} (Ring {takte} Takt), Fenster zeigt {(gezeigt?.ToString() ?? "-")}"
                      + (runde == 2 ? " (der zweite Klick setzt zurueck — der Spieler sah den ersten)" : ""));
                }
                TaktHalten(false);
                int t0 = _entities.LagerEingriff(mine, +7);
                await BilderAngehalten(35);
                int? tg = f.AngezeigterStand?.StockT;
                B(tg == t0, $"Mine: Terranium ohne Satz +7 (Eingriff) -> {t0}, Fenster zeigt nach 35 Bildern {(tg?.ToString() ?? "-")}");
                _entities.LagerEingriff(mine, -7);
            }
        }

        // ---- 3. K21 Bahnhof 55, 518 Bahnverlegung, Geisterzeile ---------------
        if (UI.SkirmishSetup.CampaignMission != 21 || _kartenschirm == null)
            sb.Append("  -- 518 Bahnhof: KEIN URTEIL, nur auf K21 (--campaign=21)\n");
        else
        {
            int gegeben = _entities.K21PlaetzeGeben();
            int bhf = _entities.GebaeudeIndexVonPlatz(55), basis = _entities.GebaeudeIndexVonPlatz(0);
            sb.Append($"  ⚠ EINGRIFF: {gegeben} Karvina-Gebaeude an den Spieler, ");
            int drin = bhf >= 0 ? _entities.DepotProbeBefuellen(bhf, 2) : 0;
            sb.Append($"{drin} Fahrzeuge in Bahnhof 55\n");
            if (bhf < 0 || basis < 0) sb.Append("  -- 518: KEIN URTEIL (Bahnhof 55 / Basis 0 fehlen)\n");
            else
            {
                await Oeffnen(bhf);
                var bv = f.BahnhofAnsicht;
                if (bv == null || !f.ZeigtOriginalBahnhof) sb.Append("  -- 518: KEIN URTEIL, Bahnhof ohne Originalkacheln\n");
                else
                {
                    TaktHalten(true);
                    Druck(bv.FeldAufDemSchirm(4));                  // Zeile 0 markieren
                    await Bilder(3);
                    Druck(bv.FeldAufDemSchirm(3));                  // »Transportieren« -> Karte Betriebsart 5
                    await Bilder(3);
                    int garageVor = _entities.GarageZahl(bhf);
                    var bz = _entities.GebaeudeZelle(basis);
                    KartenschirmKlickBahn(bz.X, bz.Y);              // der Kartenklick des Spielers
                    int takte = _entities.RingLeeren();
                    int garage = _entities.GarageZahl(bhf);
                    int zeilen = f.AngezeigterStand?.DepotZeilen.Count ?? -1;
                    A(garage < garageVor && zeilen == garage,
                      $"518 Bahnverlegung: Bahnhof {garageVor} -> {garage} Einheit(en) (Ring {takte} Takt), Fenster zeigt {zeilen} Zeile(n)"
                      + (zeilen > garage ? " — GEISTERZEILE" : ""));
                    TaktHalten(false);
                    if (!f.Visible) await Oeffnen(bhf);
                    int w0 = _entities.LagerEingriff(bhf, +7, waffen: true);
                    await BilderAngehalten(35);
                    int? wg = f.AngezeigterStand?.StockW;
                    B(wg == w0, $"Bahnhof: Waffenlager ohne Satz +7 (Eingriff) -> {w0}, Fenster zeigt nach 35 Bildern {(wg?.ToString() ?? "-")}");
                    _entities.LagerEingriff(bhf, -7, waffen: true);
                }
            }
        }

        // ---- 4. 505 Entwurf, Werteliste des Basisfensters ----------------------
        string probe = _entities.RingEntwurfProbe(out string zeile505);
        if (probe.Length == 0) sb.Append("  -- 505 Entwurf: KEIN URTEIL, " + zeile505 + "\n");
        else K(zeile505.StartsWith("ok"), "505 " + zeile505);

        // ---- 5. Meldungen (bug-599): 2005 Wächter, 530 Basisfenster, 502 Zielwahl --
        bool nullM = MapEntityLayer.NacharbeitAlt("meldung");
        if (nullM) sb.Append("  ⚠ NULLMODELL --satznacharbeit-alt(=meldung): Teil M MUSS fallen\n");
        int gruenM = 0, rotM = 0;
        foreach (var (urteil, text) in _entities.MeldungsProben())
        {
            if (urteil == null) { sb.Append($"  -- {text}\n"); continue; }
            sb.Append($"  {(urteil.Value ? "ok  " : "ROT ")} M {text}\n");
            if (urteil.Value) gruenM++; else rotM++;
        }

        TaktHalten(false);
        UI.BuildingWindow.FensterneumalenAlt = taktMerk;
        int geurteilt = gruenA + rotA + gruenB + rotB + gruenK + rotK + gruenM + rotM;
        sb.Append($"  Summe: A {gruenA} gruen / {rotA} rot, B {gruenB} gruen / {rotB} rot, K {gruenK} gruen / {rotK} rot, "
                + $"M {gruenM} gruen / {rotM} rot; "
                + $"Nacharbeit malte {_entities.NacharbeitNeumalen}x, Fenster behutsam {f.BehutsamGemalt}x\n");
        string ergebnis;
        if (geurteilt == 0) ergebnis = "KEIN URTEIL";
        else if (nullA || nullB || nullK || nullM)
        {
            // Jedes eingeschaltete Nullmodell muss SEINEN Teil rot machen (sofern
            // der Teil auf dieser Karte überhaupt urteilt).
            bool Rot(bool an, int gruen, int rot) => !an || rot > 0 || gruen + rot == 0;
            bool rotWieErwartet = Rot(nullA, gruenA, rotA) && Rot(nullB, gruenB, rotB)
                                  && Rot(nullK, gruenK, rotK) && Rot(nullM, gruenM, rotM);
            ergebnis = rotWieErwartet ? "NULLMODELL ROT wie erwartet" : "⚠ NULLMODELL GRUEN — der Pruefstand misst nichts";
        }
        else ergebnis = rotA + rotB + rotK + rotM == 0 ? "BESTANDEN" : "DURCHGEFALLEN";
        sb.Append($"  Ergebnis: {ergebnis}");
        GD.Print(sb.ToString());
        GetTree().Quit(0);
    }
}

public partial class MapEntityLayer
{
    /// <summary>Für Prüfstände (bug-600): den Ring leerlaufen lassen wie der Takt —
    /// gibt die Zahl der Takte zurück.</summary>
    public int RingLeeren()
    {
        int t = 0;
        for (; t < 8 && Commands.Pending > 0; t++) CommandTick();
        return t;
    }

    /// <summary>⚠ EINGRIFF für <c>--ring-nacharbeit-check</c> Teil B: das Konto des
    /// Betrachters ohne Satz verstellen. Gibt den neuen Stand zurück.</summary>
    public int GeldEingriff(int delta)
    {
        int p = Mathf.Clamp(ViewPlayer, 0, 7);
        Money(p, Money(p) + delta);
        return Money(p);
    }

    /// <summary>⚠ EINGRIFF für Teil B: Terranium- bzw. Waffenlager ohne Satz.</summary>
    public int LagerEingriff(int idx, int delta, bool waffen = false)
    {
        if (idx < 0 || idx >= _entities.Count) return -1;
        var e = _entities[idx];
        if (waffen) { e.StockW += delta; return e.StockW; }
        e.StockT += delta;
        return e.StockT;
    }

    /// <summary>⚠ EINGRIFF für <c>--ring-nacharbeit-check</c>: die erste lebende
    /// Terranium-Mine (Art 10/15) dem Betrachter geben. −1 = keine.</summary>
    public int MineProbeGeben()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead || b.NoStructure || b.BType is not (10 or 15)) continue;
            b.Owner = ViewPlayer;
            return i;
        }
        return -1;
    }

    /// <summary>
    /// bug-599 — die drei Meldungen, die mit dem Ring verstummt waren, je über den
    /// Absender des Spielers und Ring leerlaufen. Rückgabe je Probe (Urteil, Zeile);
    /// Urteil null = KEIN URTEIL auf dieser Karte.
    /// </summary>
    public System.Collections.Generic.List<(bool?, string)> MeldungsProben()
    {
        var r = new System.Collections.Generic.List<(bool?, string)>();
        int me = Mathf.Clamp(ViewPlayer, 0, 7);

        // 2005: der Wächter (Preis an der Zeile anders als beim Klick).
        int basis = -1;
        for (int i = 0; i < _entities.Count && basis < 0; i++)
            if (_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].BType == 1 && _entities[i].Owner == ViewPlayer) basis = i;
        var angebot = basis >= 0 && !ForschungAlt ? ForschungAngebote(me) : null;
        if (angebot == null || angebot.Count == 0)
            r.Add((null, "2005 Forschung: KEIN URTEIL, keine eigene Basis mit Angebot"));
        else
        {
            _order = "";
            int preis = angebot[0].Preis;
            // ⚠ der Satz, den der Absender PostForschung baut — mit dem Preis, den der
            // Spieler »gesehen« hat, als die Liste sich danach verschob (Preis + 1).
            PostRaw(Simulation.Commands.CommandRecord.Make(Simulation.Commands.CommandOp.OursResearch,
                    (byte)ViewPlayer, (short)basis, 0, (short)Mathf.Clamp(preis + 1, short.MinValue, short.MaxValue)));
            int t = RingLeeren();
            bool ok = _order.StartsWith("Forschung — das Angebot hat sich geaendert");
            r.Add((ok, $"2005 Forschung, Waechter schlaegt an (Ring {t} Takt): Auftragszeile »{_order}«"));
        }

        // 530 aus dem Basisfenster (Kampagne): ohne Geld die Absage, mit Geld die Lieferzeile.
        int markt = TradeLikeOriginal ? MarktIndex() : -1;
        if (markt >= 0) MarktRegalFuellen();                          // wie --marktfenster-check
        var regal = markt >= 0 ? MarketShelf() : null;
        if (!TradeLikeOriginal || regal == null || regal.Count == 0)
            r.Add((null, "530 Marktkauf Basisfenster: KEIN URTEIL (nur Kampagne mit Markt und Angebot)"));
        else
        {
            int merkGeld = Money(me);
            // ⚠ EINGRIFF wie --marktfenster-check: ein eigenes Fahrzeug auf die Platte,
            // sonst ist das Geschäftszentrum geschlossen.
            MarktFahrzeugSetzen(markt, true);
            PostenAnwaehlenWieKlick(markt);
            Money(me, 0);                                             // ⚠ EINGRIFF: kein Geld
            _order = "";
            PostBuildPanelPick(0);                                    // die Zeile des Basisfensters
            int t = RingLeeren();
            bool absage = _order == "Sie haben leider nicht genug Geld.";
            r.Add((absage, $"530 Basisfenster ohne Geld (Ring {t} Takt): »{_order}«"));
            Money(me, merkGeld + 100000);                             // ⚠ EINGRIFF: genug Geld
            _order = "";
            PostBuildPanelPick(0);
            t = RingLeeren();
            bool gekauft = _order.StartsWith("gekauft fuer $");
            r.Add((gekauft, $"530 Basisfenster mit Geld (Ring {t} Takt): »{_order}«"));
        }

        // 502 Zielwahl: gezählt werden GESTARTETE Maschinen, nicht Sätze.
        int fh = FlughafenIndex();
        if (fh < 0) { r.Add((null, "502 Zielwahl: KEIN URTEIL, kein Flughafen auf dieser Karte")); return r; }
        var e = _entities[fh];
        FlughafenUebergeben(fh, ViewPlayer);                          // ⚠ EINGRIFF (wie --flughafenfenster-bild)
        e.StockW += 500; e.StockF += 500; e.StockS += 500;            // ⚠ EINGRIFF: Teile für den Kauf
        PostenAnwaehlenWieKlick(fh);
        var st = BuildingWindowData();
        if (st == null || st.Angebote.Count == 0 || st.Angebote[0].Kaufen == null)
        { r.Add((null, "502 Zielwahl: KEIN URTEIL, Flughafen ohne Kaufzeile")); return r; }
        st.Angebote[0].Kaufen!();                                     // 501 über den Fensterknopf
        RingLeeren();
        if ((e.Hangar?.Count ?? 0) == 0) { r.Add((null, "502 Zielwahl: KEIN URTEIL, kein Flugzeug gekauft")); return r; }
        int col = Mathf.Clamp(e.Col + 4, 0, (_nav?.Width ?? 1) - 1), row = Mathf.Clamp(e.Row + 4, 0, (_nav?.Height ?? 1) - 1);

        // a) alle Sätze werden abgewiesen (⚠ EINGRIFF: Absturz zwischen Klick und Takt)
        int vor = ZielwahlMaschinen;
        bool beg = ZielwahlBeginnen(e.Slot, 0xFF);
        ZielwahlKlick(col, row);
        var maschinen = StaffelMaschinen(e.Slot, 0xFF);
        foreach (var a in maschinen) a.Absturz = true;
        RingLeeren();
        foreach (var a in maschinen) a.Absturz = false;
        int gezaehlt = ZielwahlMaschinen - vor;
        r.Add((beg && gezaehlt == 0 && _order == "keine Maschine konnte starten",
               $"502 Zielwahl, alle Saetze abgewiesen: gezaehlt {gezaehlt} (Soll 0), »{_order}«"));
        // b) normal
        vor = ZielwahlMaschinen;
        int imHangar = e.Hangar?.Count ?? 0;
        ZielwahlBeginnen(e.Slot, 0xFF);
        ZielwahlKlick(col, row);
        RingLeeren();
        int gestartet = imHangar - (e.Hangar?.Count ?? 0);
        gezaehlt = ZielwahlMaschinen - vor;
        r.Add((gestartet > 0 && gezaehlt == gestartet && _order.StartsWith($"{gestartet} Maschinen greifen"),
               $"502 Zielwahl normal: gestartet {gestartet}, gezaehlt {gezaehlt}, »{_order}«"));
        return r;
    }

    /// <summary>Läuft die Mine (Zustand aktiv)?</summary>
    public bool MineLaeuft(int idx) => idx >= 0 && idx < _entities.Count && _entities[idx].State == StAktiv;

    /// <summary>
    /// 505 über den Spielerweg (<c>DesignerInput(0, 0, true)</c> = Enter im
    /// Erstellungsfenster), Ring leerlaufen, dann die Werteliste des Basisfensters
    /// (<c>BaseWindow.StatsFor</c>) lesen. Gibt den Probenamen zurück (leer = kein
    /// Urteil); <paramref name="zeile"/> beginnt mit »ok« oder »ROT«. Der Entwurf
    /// wird danach wieder entfernt; <c>designs.cfg</c> bleibt unberührt
    /// (<c>_entwurfProbe</c>, das seit bug-600 NUR noch das Schreiben sperrt).
    /// </summary>
    public string RingEntwurfProbe(out string zeile)
    {
        LoadDesigns();
        LoadDesignParts();
        bool alleTeile = false;
        if (!Designer.Ready) { alleTeile = true; EntwurfAlleTeile = true; LoadDesignParts(); }
        if (_designs == null || !Designer.Ready)
        {
            if (alleTeile) { EntwurfAlleTeile = false; LoadDesignParts(); }
            zeile = "keine Entwurfslisten auf dieser Karte";
            return "";
        }
        UI.UnitStatBook.Load();
        bool aktiv = Designer.Active;
        if (!aktiv) Designer.Toggle();
        while (Designer.Cursor != DesignScreen.Row.Propulsion) Designer.Move(1);
        string name = Designer.ProposedName();
        for (int k = 0; k < 40 && _designs.Exists(d => d.Name == name); k++)
        {
            Designer.Change(1);
            name = Designer.ProposedName();
        }
        int vorher = _designs.Count;
        _entwurfProbe = true;
        DesignerInput(0, 0, true);
        int takte = RingLeeren();
        _entwurfProbe = false;
        if (!aktiv) Designer.Close();
        bool angelegt = _designs.Count == vorher + 1;
        string werte = UI.BaseWindow.StatsFor(name);
        bool da = angelegt && werte.Length > 0;
        zeile = $"{(da ? "ok" : "ROT")} Entwurf »{name}« {(angelegt ? "angelegt" : "NICHT angelegt")} "
              + $"({(Alt2("entwurf") ? "Direktweg" : $"Ring, {takte} Takt")}), Werteliste {(werte.Length > 0 ? "gefuellt" : "LEER")}"
              + (werte.Length > 0 ? ": " + werte.Replace("\n", " | ") : "");
        if (alleTeile) { EntwurfAlleTeile = false; LoadDesignParts(); }
        _designs.RemoveAll(d => d.Slot < 0 && d.Name == name);
        UI.UnitStatBook.Austragen(name);
        if (!angelegt) zeile += $" »{Designer.Message}«";
        return name;
    }
}
