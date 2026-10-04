namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// ⭐⭐ <b>PAKET 1 BEDIENUNG — die Oberflächenseite</b> (04.10.2026, KayelGee-Meldungen
/// 1, 4 und 5, <c>berichte/maus-befehle-fable.md</c>):
/// <list type="bullet">
/// <item><b>bug-415/418 — Fahrt-, Formations- und Handsteuerungszeiger</b>
/// (<see cref="FahrtZeigerArt"/>), Gegenschalter <c>--fahrtzeiger-aus</c>; Prüfstand
/// <c>--zeigerbank-check</c> (Import in einen leeren Ordner, Nachzug, Bildtakt,
/// Zeiger 3).</item>
/// <item><b>bug-418 — Shift = Formation</b>, Prüfstand <c>--formation-check</c>
/// (geht durch <see cref="KartenBefehl"/>, also den echten Klickweg).</item>
/// <item><b>bug-419 — »Untermissionen«</b> (<see cref="UntermissionenZeigen"/>),
/// Gegenschalter <c>--untermissionen-knopf-aus</c>, Prüfstand
/// <c>--untermissionen-fenster-check=N</c>.</item>
/// </list>
/// </summary>
public partial class MapViewer
{
    /// <summary><c>--fahrtzeiger-aus</c> — der Stand vor dem 04.10.2026: über freiem
    /// Boden der Pfeil statt Zeiger 4/27, keine 3 bei der Handsteuerung.</summary>
    public static bool FahrtzeigerAus;

    /// <summary><c>--untermissionen-knopf-aus</c> — der Stand vor dem 04.10.2026:
    /// der Knopf »Untermissionen« im Haupt-Menü ist gedimmt und tut nichts.</summary>
    public static bool UntermissionenKnopfAus;

    private bool _formationCheck, _zeigerbankCheck;
    private int _untermissionenFensterCheck;

    /// <summary>
    /// Die Zeiger, die an der ZEIGERART hängen statt am Ziel unter der Maus
    /// (<c>0x4A9AB0</c>, selbst nachgelesen):
    /// <code>
    ///   0x4A9AF3  Zustand 1000 (Handsteuerung Boden)  -> 0x4A9BC9 mov dl,3    Bild 3
    ///   0x4A9B63  Zustand 3 (Fahrt): word[0x4FA0C8] == 10000 (GRUPPE)?
    ///   0x4A9B6E    al := [0x8B8068] (Einstellung 16), dl := [0xA182F8] (Shift)
    ///   0x4A9B79    cmp al,dl / je -> 0x4A9B81 mov dl,4     Bild 4  (Fahrt)
    ///   0x4A9B7D               sonst  mov dl,0x1B           Bild 27 (Formation)
    /// </code>
    /// Zustand 3 ist bei uns <see cref="MapEntityLayer.Hint.Ground"/> mit einer
    /// Auswahl — derselbe Fall, der beim Klick zu <c>PostMove</c> führt (freier
    /// Boden, Verbündeter, türloses fremdes Gebäude, bug-301). −1 = kein Fahrtzeiger.
    /// <para>⚠ UNSERE Eingrenzung: ohne Auswahl Pfeil (Zustand 0); welcher Zweig
    /// der Zeigerwahl ohne Auswahl Zustand 3 setzt, ist nicht nachgelesen.</para>
    /// </summary>
    public int FahrtZeigerArt(MapEntityLayer.Hint hint, bool shift)
    {
        if (FahrtzeigerAus) return -1;
        if (_entities.HandsteuerungIdx >= 0) return UI.GameCursors.Handsteuerung;
        if (hint != MapEntityLayer.Hint.Ground) return -1;
        if (!_entities.HasSelection && !_entities.HasAirSelection) return -1;
        bool gruppe = !_entities.HasAirSelection && _entities.AuswahlZahl > 1;
        // Bild und Klick sagen dasselbe: unter --formation-alt faehrt keine Gruppe in
        // Formation, also auch kein Zeiger 27.
        return gruppe && !MapEntityLayer.FormationAlt && MapEntityLayer.FormationGilt(shift)
            ? UI.GameCursors.Formation : UI.GameCursors.Fahrt;
    }

    /// <summary>
    /// ⭐⭐ 04.10.2026, bug-419 — <b>DER MENÜPUNKT »UNTERMISSIONEN«</b>,
    /// <c>0x451530</c> (selbst zerlegt):
    /// <code>
    ///   si := 101, di := 10
    ///   bis si &lt; 121:  1 &lt; v[si] &lt; 10 ?  -> 0x401A69(di, di, v[si+30], 0) = show_text,
    ///                                            di += 40   (Kaskade)
    ///   di == 10 -> 0x401CB7(0, 0, 0x4FC3AC »Keine Untermissionen im Moment«, "", 3, 1)
    /// </code>
    /// Die Textnummern v[131…150] setzt der Setup-Block jeder Mission (z. B.
    /// @0x4887C3 <c>v[131] := 110</c> in M1) — sie stehen bei uns längst im
    /// <c>init</c> von <c>Data/mission_scripts.json</c> (101 Paare über 32 Missionen,
    /// M5 hat keine). ⚠ Der Bericht meinte, sie fehlten; das war ein Lesefehler
    /// des Berichts. Fenster: <see cref="UI.HelpWindow.ShowOhneRiegel"/> (show_text
    /// hat keinen Riegel), Lage (di, di) im 640×480-Raster wie die Skripttexte.
    /// Gibt die Zahl der gezeigten Fenster zurück.
    /// </summary>
    public int UntermissionenZeigen()
    {
        int di = 10, n = 0;
        for (int si = 101; si < 121; si++)
        {
            int v = _entities.SkriptVar(si);
            GD.Print($"SUB: v[{si}] = {v}");
            if (v <= 1 || v >= 10) continue;
            int text = _entities.SkriptVar(si + 30);
            GD.Print($"text: {text}");
            UI.HelpWindow.ShowOhneRiegel(GetTree().Root, text, di, di);
            di += 40;
            n++;
        }
        if (di == 10) MeldungOeffnen("Keine Untermissionen im Moment", "", 3, true);
        return n;
    }

    // ======================= --untermissionen-fenster-check=N ====================

    private string UntermissionenFensterCheck(int soll)
    {
        var sb = new StringBuilder($"untermissionen-fenster-check K{soll} (bug-419)\n");
        int m = UI.SkirmishSetup.CampaignMission;
        if (m != soll) return sb.Append($"  KEIN URTEIL: --untermissionen-fenster-check={soll}, aber Mission {m}\n  DURCHGEFALLEN").ToString();
        if (UntermissionenKnopfAus) sb.AppendLine("  ⚠ NULLMODELL --untermissionen-knopf-aus: der Knopf MUSS tot sein");
        if (!_entities.SkriptBereitFuerProbe()) return sb.Append("  KEIN URTEIL: kein Missionsskript\n  DURCHGEFALLEN").ToString();
        int k0 = -1;
        for (int k = 0; k < 20 && k0 < 0; k++) if (_entities.SkriptVar(131 + k) > 0) k0 = k;
        if (k0 < 0) return sb.Append("  diese Mission hat keine Untermission mit Text (v[131..150] alle 0) — ungeprueft\n  DURCHGEFALLEN").ToString();
        int text = _entities.SkriptVar(131 + k0);
        var alt = new int[20];
        for (int k = 0; k < 20; k++) alt[k] = _entities.SkriptVar(101 + k);
        void Setze(int wert) { for (int k = 0; k < 20; k++) _entities.SkriptVarFuerProbe(101 + k, k == k0 ? wert : 0); }
        void Zu() { UI.HelpWindow.CloseAll(); UI.HelpWindow.CommitClose(); }
        bool alles = true;
        void Soll(bool ok, string was) { alles &= ok; sb.AppendLine($"  {(ok ? "ok  " : "FEHL")} {was}"); }
        sb.AppendLine($"  Untermission v[{101 + k0}] mit Text v[{131 + k0}] = {text}; ⚠ EINGRIFF: v[101..120] gesetzt");

        // 1. ueber den MENUEWEG: v = 2 (ausgeloest), Knopf 5 druecken
        Zu(); Setze(2);
        int meld0 = MeldungenGeoeffnet;
        OeffneHauptmenue();
        var menue = _hauptmenue;
        bool gedrueckt = menue != null && menue.Ausloesen(4);
        Soll(gedrueckt && UI.HelpWindow.IsOpen(text) && _hauptmenue == null && MeldungenGeoeffnet == meld0,
             $"v={2}: Knopf »Untermissionen« ueber das Haupt-Menue: gedrueckt {gedrueckt}, Fenster {text} offen {UI.HelpWindow.IsOpen(text)}, "
             + $"Menue zu {_hauptmenue == null}, Meldungen +{MeldungenGeoeffnet - meld0}");
        if (_hauptmenue != null) ClosePause();

        // 2. dasselbe noch einmal: show_text hat KEINEN Riegel
        Zu();
        int n2 = UntermissionenZeigen();
        Soll(n2 == 1 && UI.HelpWindow.IsOpen(text), $"v=2 zum zweiten Mal (kein Riegel): {n2} Fenster, offen {UI.HelpWindow.IsOpen(text)}");

        // 3. v = 1 (angelegt) und v = 10 (erledigt): nur die Meldung
        foreach (int w in new[] { 1, 10 })
        {
            Zu(); Setze(w);
            int me = MeldungenGeoeffnet;
            int n = UntermissionenZeigen();
            Soll(n == 0 && !UI.HelpWindow.IsOpen(text) && MeldungenGeoeffnet == me + 1,
                 $"v={w}: {n} Fenster, Meldung »Keine Untermissionen im Moment« +{MeldungenGeoeffnet - me}");
        }

        // 4. Kaskade: zwei aktive -> zwei Fenster
        int k1 = -1;
        for (int k = k0 + 1; k < 20 && k1 < 0; k++) if (_entities.SkriptVar(131 + k) > 0) k1 = k;
        if (k1 >= 0)
        {
            Zu();
            for (int k = 0; k < 20; k++) _entities.SkriptVarFuerProbe(101 + k, k == k0 || k == k1 ? 3 : 0);
            int n = UntermissionenZeigen();
            int t1 = _entities.SkriptVar(131 + k1);
            Soll(n == 2 && UI.HelpWindow.IsOpen(text) && UI.HelpWindow.IsOpen(t1), $"zwei aktive (v=3): {n} Fenster ({text}, {t1})");
        }
        else sb.AppendLine("  (nur eine Untermission mit Text — Kaskade nicht pruefbar)");

        Zu();
        for (int k = 0; k < 20; k++) _entities.SkriptVarFuerProbe(101 + k, alt[k]);
        return sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    // ======================= --formation-check ====================================

    private string FormationCheck()
    {
        var sb = new StringBuilder("formation-check (bug-418)\n");
        if (MapEntityLayer.FormationAlt) sb.AppendLine("  ⚠ NULLMODELL --formation-alt: die Formationsfaelle MUESSEN durchfallen");
        if (MapEntityLayer.WarteschlangeShift) sb.AppendLine("  ⚠ NULLMODELL --warteschlange-shift: der Warteschlangenfall MUSS durchfallen");
        var klick = _entities.FormationAufbau(sb);
        if (klick == null) return sb.Append("  DURCHGEFALLEN").ToString();
        var k = klick.Value;
        var klickPunkt = _entities.ZellMitteFuerProbe(k.X, k.Y);
        bool alles = true;
        void Soll(bool ok, string was) { alles &= ok; sb.AppendLine($"  {(ok ? "ok  " : "FEHL")} {was}"); }
        string Z(Vector2I[] z) => string.Join(" ", System.Array.ConvertAll(z, p => $"({p.X},{p.Y})"));

        foreach (var (einst, shift) in new[] { (0, false), (0, true), (1, false), (1, true) })
        {
            MapEntityLayer.Einstellung16Probe = einst;
            _entities.FormationZuruecksetzen();
            int zeiger = FahrtZeigerArt(MapEntityLayer.Hint.Ground, shift);
            _mausProbePunkt = klickPunkt;
            KartenBefehl(shift, ctrl: false);
            _mausProbePunkt = null;
            var (ziele, _) = _entities.FormationStand(3);
            bool formErw = (einst != 0) != shift;
            bool form = ziele[0] == k && ziele[1] == k + new Vector2I(2, 0) && ziele[2] == k + new Vector2I(0, 2);
            int weit = 0;
            foreach (var p in ziele) weit = System.Math.Max(weit, System.Math.Max(System.Math.Abs(p.X - k.X), System.Math.Abs(p.Y - k.Y)));
            bool gruppe = weit <= 1;
            int zeigerSoll = formErw && !MapEntityLayer.FormationAlt ? UI.GameCursors.Formation : UI.GameCursors.Fahrt;
            Soll((formErw ? form : gruppe) && zeiger == zeigerSoll,
                 $"Einstellung {einst} ({(einst == 0 ? "Gruppe" : "Formation")} Standard), Shift {(shift ? "an " : "aus")}: "
                 + $"Ziele {Z(ziele)} -> {(form ? "FORMATION" : gruppe ? "GRUPPE (alle um die Klickzelle)" : "weder noch")}, "
                 + $"erwartet {(formErw ? "FORMATION" : "GRUPPE")}; Zeiger {zeiger} (soll {zeigerSoll})");
        }

        // Warteschlange: zweiter Shift-Klick waehrend der Fahrt ersetzt, haengt nicht an
        MapEntityLayer.Einstellung16Probe = 0;
        _entities.FormationZuruecksetzen();
        _mausProbePunkt = klickPunkt;
        KartenBefehl(true, ctrl: false);
        _entities.FormationStand(3);
        var zweit = _entities.ZellMitteFuerProbe(k.X, k.Y + 2);
        _mausProbePunkt = zweit;
        KartenBefehl(true, ctrl: false);
        _mausProbePunkt = null;
        var (ziele2, reihe) = _entities.FormationStand(3);
        int inReihe = 0; foreach (int q in reihe) inReihe += q;
        Soll(inReihe == 0, $"zweiter Shift-Klick waehrend der Fahrt: {inReihe} Auftraege in der Warteschlange (soll 0 — keine Warteschlange im Original), Ziele {Z(ziele2)}");

        // ein einzelnes Fahrzeug: Shift ohne Wirkung (0x4378D9), Zeiger 4
        MapEntityLayer.Einstellung16Probe = 0;
        _entities.FormationZuruecksetzen();
        int einzel = _entities.AuswahlZahl > 0 ? 1 : 0;
        _entities.AuswahlNurErste();
        int zEinzel = FahrtZeigerArt(MapEntityLayer.Hint.Ground, true);
        Soll(einzel == 1 && zEinzel == UI.GameCursors.Fahrt, $"eine Einheit, Shift an: Zeiger {zEinzel} (soll 4, Shift wirkt nur bei Gruppen)");

        MapEntityLayer.Einstellung16Probe = -1;
        sb.AppendLine($"  Fahrten: Formation {_entities.FormationsFahrten}, Gruppe {_entities.GruppenFahrten}");
        return sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    // ======================= --zeigerbank-check ===================================

    private string ZeigerbankCheck()
    {
        var sb = new StringBuilder("zeigerbank-check (bug-415)\n");
        if (UI.GameCursors.OhneNachzug) sb.AppendLine("  ⚠ NULLMODELL --zeiger-ohne-nachzug: Import und Nachzug MUESSEN ohne Zeiger bleiben");
        if (UI.GameCursors.ZeigertaktSchnell) sb.AppendLine("  ⚠ NULLMODELL --zeigertakt-schnell: der Bildtakt MUSS durchfallen");
        bool alles = true;
        void Soll(bool ok, string was) { alles &= ok; sb.AppendLine($"  {(ok ? "ok  " : "FEHL")} {was}"); }
        int[] pflicht = { 0, 1, 2, 3, 4, 5, 10, 11, 16, 17, 19, 27, 28 };
        string Pruefe(string uiCursors, out bool gut)
        {
            var bank = UI.GameCursors.ProbeLesen(uiCursors);
            int fehlt = 0;
            foreach (int p in pflicht) if (!bank.TryGetValue(p, out int n) || n == 0) fehlt++;
            int b4 = bank.TryGetValue(4, out int x4) ? x4 : 0, b27 = bank.TryGetValue(27, out int x27) ? x27 : 0;
            gut = bank.Count == 28 && fehlt == 0 && b4 == 5 && b27 == 4;
            return $"{bank.Count} Arten, Pflichtzeiger fehlen {fehlt}, Bild 4 hat {b4} Bilder (soll 5), Bild 27 {b27} (soll 4)";
        }
        string Leer(string name)
        {
            string d = ProjectSettings.GlobalizePath("user://" + name).TrimEnd('/', '\\');
            if (System.IO.Directory.Exists(d)) System.IO.Directory.Delete(d, true);
            System.IO.Directory.CreateDirectory(d);
            return d;
        }

        // 1. der SPIELERIMPORT (Oberflaechenteil von ContentBuilder.Run) in einen leeren Ordner
        var src = Import.ContentBuilder.ProbeQuelle();
        if (src == null) return sb.Append("  kein Original auf diesem Rechner gefunden — ungeprueft\n  DURCHGEFALLEN").ToString();
        sb.AppendLine($"  Quelle: {src.Label}");
        string p1 = Leer("zeigerbank-probe");
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        new Import.ContentBuilder(src, p1).OberflaecheFuerProbe();
        string t1 = Pruefe(p1 + "/UI/cursors", out bool g1);
        Soll(g1, $"Import (Oberflaechenteil, {uhr.Elapsed.TotalSeconds:0.0} s) nach {p1}: {t1}");

        // 2. der NACHZUG beim Start: leerer Datenordner ohne Zeigerbank
        string p2 = Leer("zeigerbank-probe2");
        string? q = UI.GameCursors.NachzugWennNoetig(p2);
        string t2 = Pruefe(p2 + "/UI/cursors", out bool g2);
        Soll(g2 && !string.IsNullOrEmpty(q), $"Nachzug (Bank fehlt): Quelle »{q ?? "-"}«, {t2}");
        // ... und ein zweiter Start zieht NICHT noch einmal
        string? q2 = UI.GameCursors.NachzugWennNoetig(p2);
        Soll(q2 == "", $"zweiter Start mit Bank: kein Nachzug (Rueckgabe »{q2 ?? "null"}«)");

        // 3. Bildtakt: 0,02 s je Bild (0x4A9F90 je Bild, 50 Bilder/s)
        Soll(Mathf.IsEqualApprox(UI.GameCursors.FrameSeconds, 0.10f), $"Bildtakt {UI.GameCursors.FrameSeconds:0.00} s je Bild (soll 0,10 = Spielerwahl 04.10.)");

        // 4. die Zeiger an der Zeigerart: 4 (Fahrt), 3 (Handsteuerung)
        int e = _entities.ErsteEigeneFahrende();
        if (e >= 0)
        {
            _entities.AuswahlFuerProbe(e);
            int z4 = FahrtZeigerArt(MapEntityLayer.Hint.Ground, false);
            int alt = _entities.HandsteuerungIdx;
            _entities.HandsteuerungIdx = e;
            int z3 = FahrtZeigerArt(MapEntityLayer.Hint.Ground, false);
            _entities.HandsteuerungIdx = alt;
            _entities.AuswahlFuerProbe(-1);
            int z0 = FahrtZeigerArt(MapEntityLayer.Hint.Ground, false);
            Soll(z4 == UI.GameCursors.Fahrt && z3 == UI.GameCursors.Handsteuerung && z0 < 0,
                 $"Zeigerart: Auswahl ueber Boden {z4} (soll 4), Handsteuerung {z3} (soll 3), ohne Auswahl {z0} (soll Pfeil)");
        }
        else sb.AppendLine("  (keine eigene fahrende Einheit — Zeigerart nicht geprueft)");

        foreach (string d in new[] { p1, p2 })
            try { System.IO.Directory.Delete(d, true); } catch (System.Exception) { }
        return sb.Append(alles ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    /// <summary>Kommandozeile Paket 1 — gerufen aus dem Schalterleser. true =
    /// erkannt.</summary>
    private bool BedienungSchalter(string a)
    {
        switch (a)
        {
            case "--fahrtzeiger-aus": FahrtzeigerAus = true; return true;
            case "--untermissionen-knopf-aus": UntermissionenKnopfAus = true; return true;
            case "--formation-check": _formationCheck = true; return true;
            case "--zeigerbank-check": _zeigerbankCheck = true; return true;
            case "--bodenangriff-zielpruefung": MapEntityLayer.BodenangriffZielpruefung = true; return true;
            case "--bodenziel-haftet": MapEntityLayer.BodenzielHaftet = true; return true;
            case "--bodenangriff-leer-probe": _bodenangriffLeerProbe = true; return true;
            case "--bodenangriff-abbruch-probe": _bodenangriffAbbruchProbe = true; return true;
            case "--formation-alt": MapEntityLayer.FormationAlt = true; return true;
            case "--warteschlange-shift": MapEntityLayer.WarteschlangeShift = true; return true;
            case "--zeigertakt-alt": UI.GameCursors.ZeigertaktAlt = true; return true;
            case "--zeigertakt-schnell": UI.GameCursors.ZeigertaktSchnell = true; return true;
            case "--zeiger-ohne-nachzug": UI.GameCursors.OhneNachzug = true; return true;
        }
        if (a.StartsWith("--untermissionen-fenster-check=")
            && int.TryParse(a["--untermissionen-fenster-check=".Length..], out int n))
        { _untermissionenFensterCheck = n; return true; }
        return false;
    }

    private bool _bodenangriffLeerProbe, _bodenangriffAbbruchProbe;

    /// <summary>Die Prüfstände von Paket 1 nach dem Laden. true = einer lief (und
    /// hat beendet).</summary>
    private bool BedienungPruefstaende()
    {
        string? r = null;
        if (_bodenangriffLeerProbe) r = _entities.BodenangriffLeerProbe();
        else if (_bodenangriffAbbruchProbe) r = _entities.BodenangriffAbbruchProbe();
        else if (_formationCheck) r = FormationCheck();
        else if (_zeigerbankCheck) r = ZeigerbankCheck();
        else if (_untermissionenFensterCheck > 0) r = UntermissionenFensterCheck(_untermissionenFensterCheck);
        if (r == null) return false;
        GD.Print(r);
        GetTree().Quit(0);
        return true;
    }
}
