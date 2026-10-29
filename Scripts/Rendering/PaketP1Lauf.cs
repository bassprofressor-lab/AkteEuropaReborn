namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 29.10.2026 — <b>Paket P1 der Testerrunde 0.6.12</b> (KayelGee R8, Reaper R5;
/// <c>berichte/testerrunde-0-6-12.md</c>, Bau: <c>berichte/bau-p1-0-6-12.md</c>):
/// die Schalter und die Pruefstaende an einer Stelle.
/// </summary>
public partial class MapViewer
{
    private bool _p1Fussbalken, _p1Material, _p1Zivil, _p1Geld, _p1Wiesel, _p1KiAuslass,
                 _p1FussEinnahme, _p1UkolCheck, _p1Cossarro, _p1WieselBild;

    /// <summary>Die Schalter von P1; true = verbraucht.</summary>
    private bool P1Schalter(string a)
    {
        switch (a)
        {
            // Gegenschalter (alter Stand)
            case "--fussbalken-dx-alt": MapEntityLayer.FussbalkenDxAlt = true; return true;          // bug-656
            case "--materialbalken-alt": MapEntityLayer.MaterialbalkenAlt = true; return true;       // bug-652
            case "--ladungsbalken-bild-alt": MapEntityLayer.LadungsbalkenBildAlt = true; return true; // bug-652 Schiff
            case "--cossarro-text-alt": Campaign.MissionScript.SetzungAus.Add(a); return true;       // bug-651
            case "--zivilstrafe-aus": MapEntityLayer.ZivilstrafeAus = true; return true;             // bug-654
            case "--geldmeldung-alt": MapEntityLayer.GeldmeldungAlt = true; return true;             // bug-655
            case "--besitz-grundtafel-alt": MapEntityLayer.BesitzGrundtafelAlt = true; return true;  // bug-659
            case "--fahrwerkliste-katalog-alt": MapEntityLayer.FahrwerklisteKatalogAlt = true; return true;
            case "--skript-entfernt-garage-alt": MapEntityLayer.SkriptEntferntGarageAlt = true; return true; // bug-649
            case "--ki-auslass-rueckfall-alt": MapEntityLayer.KiAuslassRueckfallAlt = true; return true;     // bug-647
            case "--einnahme-bau-weiter": MapEntityLayer.EinnahmeBauWeiter = true; return true;              // bug-647
            case "--startzustand-alt": MapEntityLayer.StartzustandAlt = true; return true;           // bug-664
            case "--flak-handtext-alt": MapEntityLayer.FlakHandtextAlt = true; return true;          // bug-664
            case "--fussvolk-einnahme-alt": MapEntityLayer.FussvolkEinnahmeAlt = true; return true;  // bug-653
            case "--ki-zielwahl-ukol-alt": MapEntityLayer.KiZielwahlUkolAlt = true; return true;     // bug-645 B2
            case "--filmton-alt": UI.MoviePlayer.FilmtonAlt = true; return true;                     // bug-663
            // Pruefstaende
            case "--fussbalken-lage-check": _p1Fussbalken = true; return true;
            case "--materialbalken-check": _p1Material = true; return true;
            case "--zivilstrafe-check": _p1Zivil = true; UI.HelpWindow.Suppressed = true; return true;
            case "--geldmeldung-check": _p1Geld = true; return true;
            case "--wiesel-check": _p1Wiesel = true; return true;
            // 29.10.2026 (Nachtrag bug-659): Gegenschalter der Rumpfbildprobe und der Bildlauf
            case "--wiesel-rumpfbild-res-alt": MapEntityLayer.WieselRumpfbildResAlt = true; return true;
            case "--wiesel-bild": _p1WieselBild = true; return true;
            case "--ki-einnahme-auslass-check": _p1KiAuslass = true; return true;
            case "--fussvolk-einnahme-check": _p1FussEinnahme = true; return true;
            case "--ki-zielwahl-ukol-check": _p1UkolCheck = true; return true;
            case "--cossarro-check": _p1Cossarro = true; UI.HelpWindow.Suppressed = true; return true;
            case "--flak-ki-start-check":
                MapEntityLayer.FlakKiStartCheckAn = true; _flakCheck = true; return true;
        }
        return false;
    }

    /// <summary>Die synchronen Pruefstaende von P1; true = einer lief (und beendet).</summary>
    private bool P1Pruefstaende()
    {
        string? zeile = null;
        // --wiesel-bild: zwei eigene Fahrzeuge umbauen (169/172) und, ohne eigenes --look,
        // die Kamera auf das erste; der Lauf geht danach normal weiter (--shot=).
        if (_p1WieselBild)
        {
            _p1WieselBild = false;
            var log = new System.Text.StringBuilder();
            var zelle = _entities.WieselUmbau(log);
            GD.Print(log.ToString().TrimEnd());
            if (zelle is { } z && _look.Length == 0) _look = $"{z.Col},{z.Row},4";
        }
        if (_p1Fussbalken) zeile = _entities.FussbalkenLageCheck();
        else if (_p1Material) zeile = _entities.MaterialbalkenCheck();
        else if (_p1Zivil) zeile = _entities.ZivilstrafeCheck();
        else if (_p1Geld) zeile = _entities.GeldmeldungCheck();
        else if (_p1Wiesel) zeile = _entities.WieselCheck();
        else if (_p1KiAuslass) zeile = _entities.KiEinnahmeAuslassCheck();
        else if (_p1FussEinnahme) zeile = _entities.FussvolkEinnahmeCheck();
        else if (_p1UkolCheck) zeile = _entities.KiZielwahlUkolCheck();
        else if (_p1Cossarro) zeile = _entities.CossarroCheck();
        if (zeile == null) return false;
        GD.Print(zeile);
        GetTree().Quit(0);
        return true;
    }
}

public partial class MapEntityLayer
{
    /// <summary>
    /// <b>--fussbalken-lage-check</b> (bug-656): je lebendem Fusssoldaten den Rahmen aus
    /// derselben Rechnung wie der Zeichner (<see cref="TabBalkenPunkt"/>) gegen eine
    /// EIGENE Zeile aus dem Blitpunkt rechnen: x = Blit.x + 12 − breite/2 (ganzzahlig),
    /// y = Blit.y + 10 (0x4B732D). Soll 0 Abweichungen; dazu, wie viele Soldaten
    /// dx ≠ 3 haben (bei dx = 3 faellt der Fehler nicht auf — sonst sagt die 0 nichts).
    /// Nullmodell <c>--fussbalken-dx-alt</c>: Abweichung 3 − dx bei fast allen.
    /// </summary>
    public string FussbalkenLageCheck()
    {
        var sb = new System.Text.StringBuilder("fussbalken-lage-check"
            + (FussbalkenDxAlt ? " — NULLMODELL --fussbalken-dx-alt" : "") + "\n");
        int n = 0, dxNicht3 = 0, abwX = 0, abwY = 0, maxX = 0;
        foreach (var e in _entities)
        {
            if (e.IsBuilding || e.IsProp || e.Dead || e.Infantry < 0 || e.HpMax <= 0 || Untergestellt(e)) continue;
            n++;
            int bw = (e.HpMax >> 2) + 2;
            var ist = TabBalkenPunkt(e, bw);
            var blit = FussvolkBlit(e);
            float sollX = Mathf.Floor(blit.X + 12) - bw / 2;
            float sollY = Mathf.Floor(blit.Y + 10);
            var (dx, _) = StreuWirksam(e);
            if (dx != 3) dxNicht3++;
            int ax = (int)Mathf.Abs(ist.X - sollX), ay = (int)Mathf.Abs(ist.Y - sollY);
            if (ax > 0) abwX++;
            if (ay > 0) abwY++;
            maxX = System.Math.Max(maxX, ax);
            if ((ax > 0 || ay > 0) && abwX + abwY <= 6)
                sb.AppendLine($"  Platz {e.Slot}: dx {dx}, Balken ({ist.X},{ist.Y}) gegen Blit+(12,10) ({sollX},{sollY})");
        }
        sb.AppendLine($"  Fusssoldaten {n}, davon dx != 3: {dxNicht3}; Abweichung waagerecht {abwX}, senkrecht {abwY}, "
                    + $"groesste waagerecht {maxX} px");
        bool urteil = n > 0 && dxNicht3 > 0;
        sb.Append(!urteil ? "  KEIN URTEIL (kein Fussvolk mit dx != 3)" : abwX == 0 && abwY == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>
    /// <b>--ki-zielwahl-ukol-check</b> (bug-645 B2): fuer einen KI-Spieler die Zielwahl
    /// <c>AiZielImSektor</c> in einem Sektor mit einem gegnerischen Kandidaten; dann den
    /// Kandidaten untergestellt melden (UKOL 0x32) und neu waehlen. Soll: ein ANDERES
    /// Ziel (das naechste) oder keines; Nullmodell <c>--ki-zielwahl-ukol-alt</c>:
    /// wieder das untergestellte. ⚠ EINGRIFF: UKOL des Kandidaten, danach zurueck.
    /// </summary>
    public string KiZielwahlUkolCheck()
    {
        var sb = new System.Text.StringBuilder("ki-zielwahl-ukol-check"
            + (KiZielwahlUkolAlt ? " — NULLMODELL --ki-zielwahl-ukol-alt" : "") + "\n");
        foreach (var a in _ai)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || AiVerbuendet(a.Player, e.Owner) || e.Ukol >= 45) continue;
                var (sx, sy) = AiSektorVon(e.Col, e.Row);
                int z1 = AiZielImSektor(a.Player, sx, sy);
                if (z1 < 0) continue;
                var ziel = _entities[z1];
                int merk = ziel.Ukol;
                ziel.Ukol = UkolUntergestellt;
                int z2 = AiZielImSektor(a.Player, sx, sy);
                ziel.Ukol = merk;
                bool ok = z2 != z1;
                sb.AppendLine($"  KI Spieler {a.Player}, Sektor ({sx},{sy}): Ziel Platz {ziel.Slot} (Spieler {ziel.Owner}); "
                            + $"untergestellt (UKOL 0x32) -> Ziel {(z2 < 0 ? "keines" : $"Platz {_entities[z2].Slot}")} "
                            + $"(Soll: nicht mehr Platz {ziel.Slot}), verworfen {KiZielUkolVerworfen}");
                sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
                return sb.ToString();
            }
        }
        return sb.Append("  KEIN URTEIL: kein KI-Spieler mit einem Ziel im Sektor").ToString();
    }

    /// <summary>
    /// <b>--cossarro-check</b> (bug-651, M7): Cossarro (Platz 0, Marke 190) toeten,
    /// 60 Takte. Soll: genau ein <c>Missionstext 175</c>, v16 = 1, v303 = 1, Mission
    /// laeuft. Nullmodell <c>--cossarro-text-alt</c>: 0 Texte, v303 = 0 → DURCHGEFALLEN.
    /// ⚠ Der Niederlagezweig (Spieler 0 ohne Einheiten und Gebaeude) ist NICHT gestellt.
    /// </summary>
    public string CossarroCheck()
    {
        int m = UI.SkirmishSetup.CampaignMission;
        bool alt = Campaign.MissionScript.SetzungAus.Contains("--cossarro-text-alt");
        var sb = new System.Text.StringBuilder($"cossarro-check K{m}" + (alt ? " — NULLMODELL --cossarro-text-alt" : "") + "\n");
        if (m != 7) return sb.Append("  KEIN URTEIL: nur in Mission 7 (--campaign=7)").ToString();
        MissionScriptTick(0.001f);
        if (_mscript == null) return sb.Append("  KEIN URTEIL: kein Missionsskript").ToString();
        var ms = _mscript;
        for (int t = 0; t < 100; t++) SimTickFuerProbe();          // 2 s anlaufen
        var texte = new System.Collections.Generic.List<int>();
        var vorher = ms.ShowText;
        ms.ShowText = (id, art, x, y) => { texte.Add(id); vorher?.Invoke(id, art, x, y); };
        int ci = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Owner == 0 && e.Mark == 190) { ci = i; break; }
        }
        if (ci < 0) { ms.ShowText = vorher; return sb.Append("  KEIN URTEIL: Cossarro (Marke 190) nicht gefunden").ToString(); }
        // Spieler 0 braucht ausser Cossarro noch etwas (sonst greift richtig der
        // Niederlagezweig units(0,0)==0 ∧ objects(1,0)==0 -> 0x4D0280, kein Text).
        int andere = 0;
        foreach (var e in _entities)
            if (!e.IsProp && !e.Dead && e.Owner == 0 && e.Mark != 190) andere++;
        if (andere == 0)
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner == 0 || !e.Mobile || e.Mark == 190) continue;
                sb.AppendLine($"  EINGRIFF: Platz {e.Slot} (Spieler {e.Owner}) -> Spieler 0, damit der Spieler nach Cossarros Tod noch Einheiten hat");
                e.Owner = 0; e.Team = 0; andere = 1;
                break;
            }
        int v16 = ms.Var(16), v303 = ms.Var(303);
        sb.AppendLine($"  Cossarro Platz {_entities[ci].Slot} auf ({_entities[ci].Col},{_entities[ci].Row}); vorher v16 {v16}, v303 {v303}");
        Kill(ci, _entities[ci], -1, "cossarro-check");
        for (int t = 0; t < 60; t++) SimTickFuerProbe();
        ms.ShowText = vorher;
        int n175 = texte.FindAll(x => x == 175).Count;
        sb.AppendLine($"  danach: Text 175 {n175}x (Soll 1), v16 {ms.Var(16)} (Soll 1), v303 {ms.Var(303)} (Soll 1), "
                    + $"Mission beendet {ms.Ended} (Soll False), alle Texte [{string.Join(",", texte)}]");
        bool ok = n175 == 1 && ms.Var(16) == 1 && ms.Var(303) == 1 && !ms.Ended;
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}

