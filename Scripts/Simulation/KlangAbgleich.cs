namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// ⭐ <b>PAKET K — DER KLANGABGLEICH 120..143</b> (Testerrunde 0.6.11, 22.10.2026,
/// <c>berichte/klangnamen-abgleich-opus.md</c> §4, K1..K8).
///
/// <para>Was hier steht: die zwei Auslöser in der Simulation, die keinen eigenen
/// Ort hatten (120 »Rakete gestartet«, 135 »Gebäude zerstört«), und der Prüfstand
/// <c>--klang-abgleich-check</c>. Die übrigen Punkte sitzen an ihrer Stelle:
/// K1 Namen <c>Audio/GameSounds.cs</c>; K2 Absagen <c>GameSounds.Absage</c>;
/// K3 Routenfenster <c>Direktbefehle2Sonst.cs</c>/<c>TransportrouteFenster.cs</c>;
/// K4 128 und K5 129/130 der Mine in <c>MapEntityLayer.UpdateEconomy</c>.</para>
///
/// <para>⚠ <b>Gleichschritt:</b> ein Klang ist Darstellung. Keiner der Auslöser
/// würfelt oder schreibt Spielzustand (nur örtliche Zähler für den Prüfstand).
/// »Für jeden hörbar« (120, 135) heißt: KEIN Besitzertor — jede Maschine spielt
/// ihn aus ihrer eigenen, gleichlaufenden Simulation. Die Besitzertore (128, 129,
/// 130) fragen <c>ViewPlayer</c>, also die örtliche Ausgabe.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Zähler für den Prüfstand (örtlich, nicht im Spielzustand).</summary>
    public int MinenlagerVollMeldungen, RaketenKlaengeOriginal, RaketenKlaengeSetzung,
               GebaeudeZerstoertKlaenge, GebaeudeZerstoertStummSkript;

    /// <summary>
    /// K6 — Klang 120 »Rakete gestartet« (C 0x40BB6E / F 0x40BA35), Modus 0, ohne
    /// Besitzertor.
    ///
    /// <para><paramref name="original"/> = der Weg des Originals: der Verteiler
    /// C 0x40C8C0 / F 0x40C780 (Busbefehl 9 @F 0x4C28B0, Skript-fire_at @F 0x4D069E)
    /// in die Schussroutine, dort NACH dem Munitionstor und VOR dem Nachladetor —
    /// ein Befehl auf eine ladende Rakete klingt also, schießt aber nicht.</para>
    ///
    /// <para>⚠ <b>UNSERE SETZUNG</b> (<paramref name="original"/> = false): unser
    /// Angriffsweg (die Gefechts-KI über AiSend, <c>--rakete-selbstziel</c>, der
    /// Zellangriff) feuert ZBRAN 8 auch ohne Befehl 9. Im Original gibt es diesen
    /// Weg nicht (die Schießuhr kehrt bei ZBRAN 8 um, F 0x40DC26); wir spielen 120
    /// dort je tatsächlich abgegebenem Schuss.</para>
    /// </summary>
    private void RaketenKlang(Entity s, bool original)
    {
        if (Audio.GameSounds.RaketenklangAus) return;
        if (original) RaketenKlaengeOriginal++; else RaketenKlaengeSetzung++;
        Audio.GameSounds.Play(Audio.GameSounds.RaketeGestartet);
    }

    /// <summary>
    /// K7 — Klang 135 »Gebäude zerstört« beim Tod eines Gebäudes, ohne Besitzertor.
    ///
    /// <para><b>Gelesen (F, selbst; C über die Thunks gegengeprüft):</b> der
    /// Stufenzeichner C 0x4C95E0 / F 0x4C9190 spielt 135, wenn die Schadensstufe
    /// <c>byte[Geb+0x0A]</c> gleich der Stufenzahl <c>byte[0xBB3200+10·Art]</c> ist
    /// (F 0x4C92E5 <c>cmp bl,cl / sete</c>, Klang F 0x4C94D4). Er hat drei Rufer:</para>
    /// <list type="number">
    /// <item><b>Der Zerstörer</b> C 0x4D5D60 / F 0x4D58F0 setzt die Stufe auf die
    /// Stufenzahl und ruft ihn (F 0x4D591D). Sein EINZIGER Rufer ist Zasah
    /// (C 0x40D3A1 / F 0x40D1DA), wenn der Treffer ≥ Trefferpunkte ist
    /// (F 0x40D17F <c>cmp ax,[esp+0x16] / jge</c>). → <b>gebaut</b>: der
    /// Gebäudetod in <c>Kill</c>.</item>
    /// <item><b>Der Stufenrechner</b> C 0x4CBBF0 / F 0x4CB7C0 ruft ihn bei jedem
    /// Stufenwechsel (F 0x4CB82C). Die Stufe (HpMax−Hp)/(HpMax/n) erreicht n bei
    /// Hp ≥ 1 nur, wenn HpMax nicht durch n teilbar ist und Hp ≤ HpMax mod n.
    /// Dann klänge 135 im Original schon vor dem Tod und beim Tod NOCH EINMAL.
    /// → <b>nicht gebaut, OFFEN</b> (unser Stufenrechner kennt die letzte Stufe
    /// nicht als Zeichenstufe).</item>
    /// <item><b>Der Gebäudetakt</b> F 0x43BBBC setzt nach dem Bauzustand die Stufe
    /// auf 1 und ruft ihn — 135 nur für eine Art mit Stufenzahl 1. → <b>nicht
    /// gebaut, OFFEN</b> (welche Art n = 1 hat, ist nicht nachgezählt).</item>
    /// </list>
    /// <para>⚠ <b>UNSERE SETZUNG:</b> das Missionsskript (remove_unit/sell_unit)
    /// geht im Original nicht über Zasah, also klingt es dort nicht; erkannt wird
    /// das an <paramref name="grund"/> (wie beim Todeslicht).</para>
    /// </summary>
    private void GebaeudeZerstoertKlang(Entity b, string grund)
    {
        if (Audio.GameSounds.GebaeudeZerstoertKlangAus) return;
        if (grund.StartsWith("Missionsskript")) { GebaeudeZerstoertStummSkript++; return; }
        GebaeudeZerstoertKlaenge++;
        Audio.GameSounds.Play(Audio.GameSounds.GebaeudeZerstoert);
    }

    // ================= --klang-abgleich-check ================================

    public static bool KlangAbgleichCheckAn;

    /// <summary>
    /// <c>--klang-abgleich-check</c> — misst über den <see cref="Audio.SoundBankPlayer.Horcher"/>,
    /// was wirklich ANKOMMT (vor jedem Riegel), nicht was gerufen wurde. Die
    /// Spielerauslöser gehen über den Absender und den Befehlsring
    /// (<see cref="RingLeeren"/>), geurteilt wird danach.
    ///
    /// <para><b>Teile und Nullmodelle</b> (jedes muss SEINEN Teil fallen lassen):</para>
    /// <list type="bullet">
    /// <item>A Absagen: Ausbau auf beschäftigter Mine (Ring), Marktkauf ohne Geld
    /// (Ring, falls ein Markt da ist), Beladen abgewiesen (direkt, melden:true gibt
    /// es nur in Prüfständen). Soll 140 × 0. Nullmodell <c>--ablehnklang-alt</c>.</item>
    /// <item>B Routenfenster: Öffnen, Von, Nach, Leeren, Start leer, Ziel, Quelle,
    /// Start. Soll 140,139,140,140,141. Nullmodell <c>--routenklang-aus</c>.</item>
    /// <item>C Minenlager: ⚠ Eingriff Lager = Platz − 1, dann Gebäudetakte bis voll
    /// und 30 weiter. Soll 128 × 1. Nullmodell <c>--minenlager-klang-aus</c>.</item>
    /// <item>D Minenausbau: Lagerausbau über den Ring (⚠ Eingriff Geld), Soll 130 × 0
    /// im Auftragstakt, × 1 beim Abschluss. Nullmodell <c>--minenausbau-klang-alt</c>.</item>
    /// <item>E Rakete: ⚠ Eingriff: eine eigene Einheit bekommt ZBRAN 8. Befehl 9 über
    /// den Ring dreimal: geladen, ladend, ohne Munition. Soll 120 × 2. Nullmodell
    /// <c>--raketenklang-aus</c>.</item>
    /// <item>F Gebäude zerstört: fremdes Gebäude per Treffer ≥ TP (Zasah-Weg), ein
    /// zweites per Skript-Entfernen. Soll 135 × 1. Nullmodell
    /// <c>--gebaeudezerstoert-klang-aus</c>.</item>
    /// <item>G über den ganzen Lauf: 121/126/137/142 × 0.</item>
    /// </list>
    /// </summary>
    public string KlangAbgleichCheck()
    {
        var sb = new StringBuilder("klang-abgleich-check (Paket K, K1-K7)\n");
        var G = Audio.GameSounds.NieGespielt;
        if (Audio.GameSounds.AblehnklangAlt) sb.Append("  ⚠ NULLMODELL --ablehnklang-alt: Teil A MUSS fallen\n");
        if (Audio.GameSounds.RoutenklangAus) sb.Append("  ⚠ NULLMODELL --routenklang-aus: Teil B MUSS fallen\n");
        if (Audio.GameSounds.MinenlagerKlangAus) sb.Append("  ⚠ NULLMODELL --minenlager-klang-aus: Teil C MUSS fallen\n");
        if (Audio.GameSounds.MinenausbauKlangAlt) sb.Append("  ⚠ NULLMODELL --minenausbau-klang-alt: Teil D MUSS fallen\n");
        if (Audio.GameSounds.RaketenklangAus) sb.Append("  ⚠ NULLMODELL --raketenklang-aus: Teil E MUSS fallen\n");
        if (Audio.GameSounds.GebaeudeZerstoertKlangAus) sb.Append("  ⚠ NULLMODELL --gebaeudezerstoert-klang-aus: Teil F MUSS fallen\n");

        var gehoert = new List<int>();
        var vorher = Audio.SoundBankPlayer.Horcher;
        Audio.SoundBankPlayer.Horcher = (slot, c, r) => gehoert.Add(slot);
        int Zahl(int ab, int slot) { int n = 0; for (int k = ab; k < gehoert.Count; k++) if (gehoert[k] == slot) n++; return n; }

        int gruen = 0, rot = 0, ohne = 0;
        var teile = new Dictionary<char, bool>();
        void Urteil(char teil, bool ok, string was)
        {
            sb.Append($"  {(ok ? "ok  " : "ROT ")} {teil} {was}\n");
            if (ok) gruen++; else rot++;
            teile[teil] = teile.TryGetValue(teil, out bool alt) ? alt && ok : ok;
        }
        void KeinUrteil(char teil, string was) { sb.Append($"  --   {teil} KEIN URTEIL: {was}\n"); ohne++; }

        int p = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
        int ring = 0;
        try
        {
            // ---- eine eigene Mine (C, D, A1) ---------------------------------
            int mi = -1;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (IsMine(e) && !e.Dead && e.Owner == ViewPlayer && e.Deposit > 0
                    && e.Bauzustand < BauzustandStart) { mi = i; break; }
            }
            if (mi < 0)
            {
                mi = MineProbeGeben();                              // ⚠ Eingriff wie --ring-nacharbeit-check
                if (mi >= 0)
                {
                    _entities[mi].Deposit = Mathf.Max(_entities[mi].Deposit, 500);
                    sb.Append($"  ⚠ EINGRIFF: Mine Platz {_entities[mi].Slot} dem Spieler gegeben, Vorkommen >= 500\n");
                }
            }

            // ---- C: Minenlager voll (128) --------------------------------------
            if (mi < 0) KeinUrteil('C', "keine eigene Mine mit Vorkommen");
            else
            {
                var m = _entities[mi];
                m.State = StAktiv;                                  // ⚠ Eingriff
                int platz = MineStoreCap(m);
                m.StockT = platz - 1;                               // ⚠ Eingriff
                m.Deposit = Mathf.Max(m.Deposit, 50);               // ⚠ Eingriff
                int ab = gehoert.Count, takte = 0, nachVoll = 0;
                for (; takte < 20000 && nachVoll < 30; takte++)
                {
                    m.EconTimer = 0f;
                    UpdateEconomy(mi, m, EconTick);
                    if (m.StockT >= platz) nachVoll++;
                    // Haul (Abtransport) darf das Lager nicht leeren — sonst misst der
                    // Lauf den Wagen. Bleibt es unter dem Platz, neu setzen.
                    if (m.StockT < platz - 1) m.StockT = platz - 1;
                }
                int n128 = Zahl(ab, Audio.GameSounds.MinenlagerVoll);
                Urteil('C', n128 == 1,
                    $"Minenlager Platz {m.Slot}: Lager {platz - 1} -> {m.StockT}/{platz} in {takte} Gebaeudetakten "
                  + $"(30 davon voll), Klang 128 x{n128} (Soll 1; >= statt == gaebe 30)");
            }

            // ---- D: Minenausbau (130 beim Abschluss) + A1 Absage --------------
            if (mi < 0) KeinUrteil('D', "keine eigene Mine");
            else
            {
                var m = _entities[mi];
                m.State = StAktiv;
                m.StockT = 0;                                       // ⚠ Eingriff: kein 128 dazwischen
                Money(p, Money(p) + m.CostStore * 4 + 10000);       // ⚠ Eingriff: Geld
                _sel.Clear(); _sel.Add(mi);
                int ab = gehoert.Count;
                int abgesetzt = PostBuildingJob(BuildingJob.ExpandStore);
                ring += RingLeeren();
                int imAuftrag = Zahl(ab, Audio.GameSounds.LagerVergroessert);
                bool laeuft = m.State == FaExpand;

                // A1: derselbe Mine eine Produktionserweiterung — sie ist beschaeftigt.
                int abA = gehoert.Count, stummVor = Audio.GameSounds.AbsageStumm + Audio.GameSounds.AbsageGespielt;
                PostBuildingJob(BuildingJob.ExpandProd);
                ring += RingLeeren();
                int a1 = Zahl(abA, Audio.GameSounds.ZielpunktWaehlen);
                int a1Weg = Audio.GameSounds.AbsageStumm + Audio.GameSounds.AbsageGespielt - stummVor;
                if (!laeuft || a1Weg == 0) KeinUrteil('A', $"Ausbau auf beschaeftigter Mine: Absage nicht erreicht (laeuft {laeuft}, Absagen {a1Weg})");
                else Urteil('A', a1 == 0, $"Ausbau auf beschaeftigter Mine (Ring): Absage erreicht {a1Weg}x, Klang 140 x{a1} (Soll 0)");

                int ab2 = gehoert.Count, takte = 0;
                for (; takte < 2000 && m.State != StAktiv; takte++)
                {
                    m.EconTimer = 0f;
                    UpdateEconomy(mi, m, EconTick);
                    m.StockT = 0;
                }
                int beimAbschluss = Zahl(ab2, Audio.GameSounds.LagerVergroessert);
                bool fertig = m.State == StAktiv;
                if (abgesetzt == 0 || !laeuft) KeinUrteil('D', $"Lagerausbau nicht angenommen (Saetze {abgesetzt}, Zustand {m.State})");
                else Urteil('D', imAuftrag == 0 && fertig && beimAbschluss == 1,
                    $"Mine Lagerausbau (Ring {abgesetzt} Satz): Klang 130 im Auftragstakt x{imAuftrag} (Soll 0), "
                  + $"Abschluss nach {takte} Gebaeudetakten {(fertig ? "ja" : "NEIN")}, Klang 130 dabei x{beimAbschluss} (Soll 1)");
            }

            // ---- A2: Marktkauf ohne Geld (Ring) ---------------------------------
            Entity? markt = _entities.FirstOrDefault(e => e.IsBuilding && e.BType == 17 && !e.Dead);
            var angebot = _market.FirstOrDefault(o => !o.Sold && o.Price > 0);
            if (markt == null || angebot == null) KeinUrteil('A', "Marktkauf ohne Geld: kein Markt/Angebot auf dieser Karte");
            else
            {
                int geld = Money(p);
                Money(p, 0);                                        // ⚠ Eingriff
                int abA = gehoert.Count, wegVor = Audio.GameSounds.AbsageStumm + Audio.GameSounds.AbsageGespielt;
                MarktBestellen(angebot.Nr, markt.Slot, basisfenster: true);
                ring += RingLeeren();
                int weg = Audio.GameSounds.AbsageStumm + Audio.GameSounds.AbsageGespielt - wegVor;
                Money(p, geld);
                int a2 = Zahl(abA, Audio.GameSounds.ZielpunktWaehlen);
                if (weg == 0) KeinUrteil('A', "Marktkauf ohne Geld: Absage nicht erreicht");
                else Urteil('A', a2 == 0, $"Marktkauf ohne Geld (Ring, Basisfensterweg): Absage {weg}x, Klang 140 x{a2} (Soll 0)");
            }

            // ---- A3: Beladen abgewiesen (direkt) -------------------------------
            {
                int gast = -1;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer || e.Infantry < 0) continue;
                    gast = i; break;
                }
                if (gast < 0)
                    for (int i = 0; i < _entities.Count; i++)
                    {
                        var e = _entities[i];
                        if (e.Dead || e.IsBuilding || e.IsProp || e.Infantry < 0) continue;
                        e.Owner = ViewPlayer; gast = i;                 // ⚠ Eingriff
                        sb.Append($"  ⚠ EINGRIFF: Fusssoldat Platz {e.Slot} dem Spieler gegeben\n");
                        break;
                    }
                if (gast < 0) KeinUrteil('A', "Beladen: kein eigener Fusssoldat");
                else
                {
                    int abA = gehoert.Count, wegVor = Audio.GameSounds.AbsageStumm + Audio.GameSounds.AbsageGespielt;
                    int rc = BeladeVersuch(gast, melden: true);
                    int weg = Audio.GameSounds.AbsageStumm + Audio.GameSounds.AbsageGespielt - wegVor;
                    int a3 = Zahl(abA, Audio.GameSounds.ZielpunktWaehlen);
                    if (rc >= 0 || weg == 0) KeinUrteil('A', $"Beladen: nicht abgewiesen (rc {rc}, »{_order}«)");
                    else Urteil('A', a3 == 0, $"Beladen abgewiesen (direkt, »{_order}«): Absage {weg}x, Klang 140 x{a3} (Soll 0)");
                }
            }

            // ---- B: Routenfenster -----------------------------------------------
            {
                int wagen = -1;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer || !IstTransporter(e)) continue;
                    wagen = i; break;
                }
                Entity? ziel = null, quelle = null;
                foreach (var b in _entities)
                {
                    if (!b.IsBuilding || b.Dead || b.Owner != ViewPlayer) continue;
                    if (ziel == null && b.BType is 1 or 9) ziel = b;
                    else if (quelle == null && b.BType is 2 or 3 or 4 or 10 or 15) quelle = b;
                }
                // ⚠ EINGRIFFE, wenn der Spieler auf dieser Karte keinen Wagen oder kein
                // Gebaeude hat: das Fremde wird ihm gegeben (Besitzerwechsel).
                if (wagen < 0)
                    for (int i = 0; i < _entities.Count; i++)
                    {
                        var e = _entities[i];
                        if (e.Dead || e.IsBuilding || e.IsProp || !IstTransporter(e)) continue;
                        e.Owner = ViewPlayer; wagen = i;
                        sb.Append($"  ⚠ EINGRIFF: Wagen Platz {e.Slot} dem Spieler gegeben\n");
                        break;
                    }
                foreach (var b in _entities)
                {
                    if (!b.IsBuilding || b.IsProp || b.Dead || b.Bauzustand >= BauzustandStart) continue;
                    if (ziel == null && b.BType is 1 or 9 && b != quelle)
                    { b.Owner = ViewPlayer; ziel = b; sb.Append($"  ⚠ EINGRIFF: Ziel {BuildingTypeName(b.BType)} dem Spieler gegeben\n"); }
                    else if (quelle == null && b.BType is 2 or 3 or 4 or 10 or 15 && b != ziel)
                    { b.Owner = ViewPlayer; quelle = b; sb.Append($"  ⚠ EINGRIFF: Quelle {BuildingTypeName(b.BType)} dem Spieler gegeben\n"); }
                }
                if (wagen < 0 || ziel == null || quelle == null)
                    KeinUrteil('B', $"Wagen {(wagen >= 0 ? "ja" : "NEIN")}, Ziel {(ziel != null ? "ja" : "NEIN")}, Quelle {(quelle != null ? "ja" : "NEIN")}");
                else
                {
                    _sel.Clear(); _sel.Add(wagen);
                    int ab = gehoert.Count;
                    var folge = new List<string>();
                    void Schritt(string was, System.Action tun)
                    {
                        int vor = gehoert.Count;
                        tun();
                        ring += RingLeeren();
                        var neu = gehoert.Skip(vor).Where(s => s is >= 139 and <= 141).ToList();
                        folge.Add($"{was}:{(neu.Count == 0 ? "-" : string.Join("/", neu))}");
                    }
                    PickOhneNebel = true;
                    Schritt("Oeffnen", () => PostRouteFensterOeffnen());
                    Schritt("Von", () => RouteModusSetzen(1));
                    Schritt("Nach", () => RouteModusSetzen(2));
                    Schritt("Leeren", () => PostRouteLeeren());
                    Schritt("StartLeer", () => PostRouteStartKnopf());
                    Schritt("Ziel", () => PostRouteKlickAufGebaeude(BodyRect(ziel).GetCenter()));
                    Schritt("Quelle", () => PostRouteKlickAufGebaeude(BodyRect(quelle).GetCenter()));
                    Schritt("Start", () => PostRouteStartKnopf());
                    PickOhneNebel = false;
                    var alle = gehoert.Skip(ab).Where(s => s is >= 139 and <= 141).ToList();
                    var r = RouteVon(_entities[wagen]);
                    bool laeuft = r != null && r.Gestartet;
                    string soll = "140,139,140,140,141";
                    string ist = string.Join(",", alle);
                    Urteil('B', ist == soll && laeuft,
                        $"Routenfenster (Ring): {string.Join(" ", folge)} — Folge {(ist.Length > 0 ? ist : "leer")} (Soll {soll}), Route laeuft {(laeuft ? "ja" : "NEIN")}");
                }
            }

            // ---- E: Rakete (120) -------------------------------------------------
            {
                int ri = -1;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer || e.Infantry >= 0 || !e.Mobile) continue;
                    if (IstTransporter(e) || i == mi) continue;
                    bool tuer = false;
                    var hier = CellCenter(e.Col, e.Row);
                    foreach (var b in _entities)
                        if (b.IsBuilding && !b.IsProp && !b.Dead && AufDerTuerzelle(b, hier)) { tuer = true; break; }
                    if (tuer) continue;
                    ri = i; break;
                }
                if (ri < 0) KeinUrteil('E', "kein eigenes Fahrzeug");
                else
                {
                    var s = _entities[ri];
                    int altWaffe = s.Weapon;
                    s.Weapon = 28;                                  // ⚠ Eingriff: ZBRAN 8
                    s.AmmoMax = 1; s.Ammo = 1; s.Cooldown = 0; s.RangeMin = 0;
                    int col = Mathf.Clamp(s.Col + 3, 0, (_nav?.Width ?? 4) - 1), row = s.Row;
                    void Befehl9() => Emit(Simulation.Commands.CommandRecord.Make(
                        Simulation.Commands.CommandOp.FireAt, (byte)ViewPlayer, (short)ri, (short)col, (short)row));
                    int ab = gehoert.Count;
                    Befehl9(); ring += RingLeeren();
                    int e1 = Zahl(ab, Audio.GameSounds.RaketeGestartet);
                    bool laedt = s.Cooldown > 0;
                    s.Cooldown = Mathf.Max(s.Cooldown, 5f);         // ⚠ Eingriff: sicher »laedt nach«
                    int ab2 = gehoert.Count;
                    Befehl9(); ring += RingLeeren();                // laedt nach: Klang ja, Schuss nein
                    int e2 = Zahl(ab2, Audio.GameSounds.RaketeGestartet);
                    s.Cooldown = 0; s.Ammo = 0;                     // ⚠ Eingriff: Munition leer
                    int ab3 = gehoert.Count;
                    Befehl9(); ring += RingLeeren();
                    int e3 = Zahl(ab3, Audio.GameSounds.RaketeGestartet);
                    Urteil('E', e1 == 1 && e2 == 1 && e3 == 0,
                        $"Rakete Platz {s.Slot} (Waffe {altWaffe} -> 28), Befehl 9 ueber den Ring: geladen x{e1} (Soll 1), "
                      + $"(Schuss {(laedt ? "abgegeben" : "nicht abgegeben")}), ladend x{e2} (Soll 1, Klang vor dem Nachladetor), ohne Munition x{e3} (Soll 0)");
                }
            }

            // ---- F: Gebaeude zerstoert (135) -------------------------------------
            {
                // Erst Gebaeude eines Spielers 0..7, sonst irgendein fremdes.
                int bi = -1, bi2 = -1;
                for (int runde = 0; runde < 2 && bi2 < 0; runde++)
                    for (int i = 0; i < _entities.Count; i++)
                    {
                        var b = _entities[i];
                        if (!b.IsBuilding || b.IsProp || b.Dead || b.Owner == ViewPlayer) continue;
                        if (b.Bauzustand >= BauzustandStart || i == mi || i == bi) continue;
                        if (runde == 0 && b.Owner is < 0 or > 7) continue;
                        if (bi < 0) bi = i; else if (bi2 < 0) { bi2 = i; break; }
                    }
                if (bi < 0) KeinUrteil('F', "kein fremdes Gebaeude");
                else
                {
                    var b = _entities[bi];
                    string name = $"{BuildingTypeName(b.BType)} (Art {b.BType}, Spieler {b.Owner})";
                    int ab = gehoert.Count;
                    ApplyHit(-1, bi, b, 1000000, "klang-abgleich-check");   // Zasah-Weg: Treffer >= TP
                    ApplyHit(-1, bi, b, 1000000, "klang-abgleich-check");   // auf die Ruine: nichts mehr
                    int f1 = Zahl(ab, Audio.GameSounds.GebaeudeZerstoert);
                    int f2 = 0; string zweit = "";
                    if (bi2 >= 0)
                    {
                        int ab2 = gehoert.Count;
                        Kill(bi2, _entities[bi2], -1, "Missionsskript remove_unit");   // ⚠ Eingriff
                        f2 = Zahl(ab2, Audio.GameSounds.GebaeudeZerstoert);
                        zweit = $", Skript-Entfernen x{f2} (Soll 0, SETZUNG)";
                    }
                    Urteil('F', b.Dead && f1 == 1 && f2 == 0,
                        $"Gebaeude {name} zerstoert {(b.Dead ? "ja" : "NEIN")}: "
                      + $"Klang 135 x{f1} (Soll 1, zwei Treffer){zweit}");
                }
            }
        }
        finally
        {
            Audio.SoundBankPlayer.Horcher = vorher;
        }

        // ---- G: nie gespielt -----------------------------------------------------
        int nie = gehoert.Count(s => G.Contains(s));
        Urteil('G', nie == 0, $"121/126/137/142 im ganzen Lauf x{nie} (Soll 0), Klaenge gesamt {gehoert.Count}");

        sb.Append($"  Ring {ring} Takt(e) geleert; Teile {string.Join(" ", teile.Select(kv => $"{kv.Key}={(kv.Value ? "gruen" : "ROT")}"))}\n");
        sb.Append($"  Ergebnis: {(rot == 0 && gruen > 0 ? "BESTANDEN" : "DURCHGEFALLEN")} ({gruen} gruen, {rot} rot, {ohne} ohne Urteil)");
        return sb.ToString();
    }
}
