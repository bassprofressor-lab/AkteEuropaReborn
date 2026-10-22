namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// ⭐ 22.10.2026 — die drei Prüfstände von Paket D (bug-592/593/594). Alle legen
/// ihre Entwürfe über den RINGWEG an und um (505/533/507 → <see cref="CommandTick"/>),
/// nie am Ring vorbei (bug-600), und urteilen erst, wenn der Ring leer ist.
/// Sie laufen nur unter Prüfschutz (kopflos Standard) — geschrieben wird in
/// <c>user://pruefstand/</c>, nie in die Dateien des Spielers.
/// </summary>
public partial class MapEntityLayer
{
    private int PruefRingLeeren()
    {
        int k = 0;
        for (; k < 8 && Commands.Pending > 0; k++) CommandTick();
        return k;
    }

    /// <summary>Die Basis des Betrachters wählen (Producer liest <c>_selected</c>).
    /// Gibt es keine eigene, wird eine fremde kurz dem Betrachter zugeschrieben.</summary>
    private Entity? PruefBasisWaehlen(StringBuilder sb, out int besitzer)
    {
        besitzer = -99;
        Entity? basis = null;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.BType != 1) continue;
            if (basis == null || e.Owner == ViewPlayer) { basis = e; _selected = i; }
            if (e.Owner == ViewPlayer) break;
        }
        if (basis != null && basis.Owner != ViewPlayer)
        {
            besitzer = basis.Owner; basis.Owner = ViewPlayer;
            sb.Append($"\n   ⚠ EINGRIFF: Basis Platz {basis.Slot} dem Betrachter zugeschrieben (Besitzer {besitzer})");
        }
        return basis;
    }

    /// <summary>Bauteile für einen Probeentwurf: die eines Vorgabe-Fahrzeugs.</summary>
    private (int P, int E, int W)? PruefTeile(int nte = 0)
    {
        if (_designs == null) return null;
        foreach (var d in _designs)
            if (d.Slot >= 0 && d.Propulsion is >= 160 and <= 175 && d.Weapon is > 0 and < 65 && nte-- <= 0)
                return (d.Propulsion, d.Equip, d.Weapon);
        return null;
    }

    /// <summary>Einen eigenen Entwurf über Befehl 505 anlegen; Index oder −1.</summary>
    private int PruefEntwurfUeberRing(string name, int nte = 0)
    {
        var t = PruefTeile(nte);
        if (t == null || _designs == null) return -1;
        PostEntwurf(t.Value.P, t.Value.E, t.Value.W, name);
        PruefRingLeeren();
        return _designs.FindIndex(d => d.Name == name && !d.Entfernt);
    }

    private static string Urteil(bool ok) => ok ? "ok  " : "FEHL";

    // =====================================================================
    // bug-592  --entwurf-verwalten-check
    // =====================================================================

    public string EntwurfVerwaltenCheck()
    {
        bool nullmodell = EntwurfKnoepfeAlt;
        var sb = new StringBuilder("entwurf-verwalten-check (bug-592)"
            + (nullmodell ? " (⚠ Nullmodell --entwurf-knoepfe-alt: (a)-(d) MUESSEN fallen)" : ""));
        if (!Core.Pruefschutz.Aktiv)
            return sb.Append("\n   ⚠ ABBRUCH: nur unter Pruefschutz\n   Ergebnis: DURCHGEFALLEN").ToString();
        LoadDesigns();
        UI.UnitStatBook.Load();
        if (_designs == null) return sb.Append("\n   keine Entwurfsliste\n   Ergebnis: DURCHGEFALLEN").ToString();
        int sel = _selected;
        var basis = PruefBasisWaehlen(sb, out int besitzer);
        if (basis == null) return sb.Append("\n   keine Basis — sagt NICHTS\n   Ergebnis: DURCHGEFALLEN").ToString();

        // Die Fenster: der Prüfstand »tippt« den Namen und sagt »Ja«.
        var merkN = OnNamensfenster; var merkA = OnAbfragefenster;
        string probeName = "", gefragt = "", abgefragt = ""; int gefragtMax = -1;
        OnNamensfenster = (f, m, dann) => { gefragt = f; gefragtMax = m; dann(probeName); };
        OnAbfragefenster = (f, ja) => { abgefragt = f; ja(); };
        int klicks0 = EntwurfKnopfKlicks;
        List<int> Menu() => BuildableFor(basis.BType, basis.Owner);
        bool InListe(string n) { foreach (var r in BuildPanelRows()) if (r.Name == n) return true; return false; }

        // (a) Vorgabeentwurf: beide Meldungen des Originals, Liste unverändert
        var menu = Menu();
        int zeileV = menu.FindIndex(i => _designs[i].Slot >= 0);
        bool okA = false;
        if (zeileV >= 0)
        {
            int nrV = menu[zeileV];
            string nameV = _designs[nrV].Name; int anzahl = _designs.Count;
            LetzteBasisMeldung = "";
            EntwurfUmbenennenKlick(zeileV);
            string m1 = LetzteBasisMeldung; LetzteBasisMeldung = "";
            EntwurfEntfernenKlick(zeileV);
            string m2 = LetzteBasisMeldung;
            PruefRingLeeren();
            bool unv = _designs.Count == anzahl && _designs[nrV].Name == nameV && !_designs[nrV].Entfernt;
            okA = m1 == TextNichtAendern && m2 == TextNichtLoeschen && unv;
            sb.Append($"\n   {Urteil(okA)} (a) Vorgabe »{nameV}«: Umbenennen meldet »{m1}«, Entfernen meldet »{m2}«, Liste {(unv ? "unveraendert" : "VERAENDERT")}");
        }
        else sb.Append("\n   FEHL (a) keine Vorgabezeile in der Liste");

        // (b) eigener Entwurf über 505, Umbenennen über 533
        string nameA = "Probe-A", neuA = "Probe Umbenannt";
        int nrA = PruefEntwurfUeberRing(nameA, 0);
        bool okB = false;
        if (nrA >= 0)
        {
            UI.UnitStatBook.Eintragen(nameA, _designs[nrA].Weapon, _designs[nrA].Propulsion, _designs[nrA].Equip);
            probeName = neuA;
            EntwurfUmbenennenKlick(Menu().IndexOf(nrA));
            int takte = PruefRingLeeren();
            bool name = _designs[nrA].Name == neuA;
            bool liste = InListe(neuA) && !InListe(nameA);
            bool werte = UI.BaseWindow.StatsFor(neuA).Length > 0 && UI.BaseWindow.StatsFor(nameA).Length == 0;
            bool fenster = gefragt == TextNamensfrage && gefragtMax == EntwurfNameMax;
            // 19 Zeichen: ein zu langer Name wird gekürzt (0x8B9242 = 0x13)
            probeName = "Abcdefghijklmnopqrstuvwxyz";
            EntwurfUmbenennenKlick(Menu().IndexOf(nrA));
            PruefRingLeeren();
            string lang = _designs[nrA].Name;
            bool kurz = lang == "Abcdefghijklmnopqrs";
            probeName = neuA;
            EntwurfUmbenennenKlick(Menu().IndexOf(nrA));
            PruefRingLeeren();
            okB = name && liste && werte && fenster && kurz;
            sb.Append($"\n   {Urteil(okB)} (b) »{nameA}« -> »{(name ? neuA : _designs[nrA].Name)}« ueber Befehl 533 ({takte} Takt): "
                      + $"Fenster »{gefragt}«/{gefragtMax} {(fenster ? "ja" : "NEIN")}, Bauliste {(liste ? "ja" : "NEIN")}, "
                      + $"Werteliste {(werte ? "ja" : "NEIN")}, 26 Zeichen -> »{lang}« ({lang.Length}) {(kurz ? "ja" : "NEIN")}");
        }
        else sb.Append("\n   FEHL (b) Probeentwurf nicht angelegt (505)");

        // (c) eine lebende Einheit dieses Entwurfs: Entfernen verweigert
        bool okC = false;
        Entity? einheit = null;
        if (nrA >= 0 && _nav != null)
        {
            int vor = _entities.Count;
            int at = SpawnAusEntwurf(_designs[nrA], -1, basis.Col, basis.Row + 3, ViewPlayer);
            if (at >= 0 && _entities.Count > vor) einheit = _entities[^1];
            LetzteBasisMeldung = "";
            EntwurfEntfernenKlick(Menu().IndexOf(nrA));
            string m = LetzteBasisMeldung;
            PostEntwurfEntfernen(nrA);                      // auch der Satz selbst wird abgewiesen
            PruefRingLeeren();
            okC = einheit != null && m == TextTypusBleibt + " / " + TextSchonEinheiten && !_designs[nrA].Entfernt;
            sb.Append($"\n   {Urteil(okC)} (c) Einheit »{einheit?.Name}« lebt (⚠ EINGRIFF: gesetzt neben die Basis): "
                      + $"Meldung »{m}«, Entwurf {(_designs[nrA].Entfernt ? "ENTFERNT" : "bleibt")}");
        }
        else sb.Append("\n   FEHL (c) keine Einheit setzbar");

        // (d) ohne Einheit: Entfernen; Grabstein, Indexprobe über eine Warteschlange
        bool okD = false;
        if (nrA >= 0)
        {
            if (einheit != null) einheit.Dead = true;       // ⚠ EINGRIFF
            int nrB = PruefEntwurfUeberRing("Probe-B", 1);
            if (nrB >= 0)
            {
                basis.BuildQueue.Add(nrB);                  // ⚠ EINGRIFF: B steht in der Warteschlange
                int anzahl = _designs.Count;
                abgefragt = "";
                EntwurfEntfernenKlick(Menu().IndexOf(nrA));
                int takte = PruefRingLeeren();
                bool grab = _designs[nrA].Entfernt && _designs.Count == anzahl;
                bool weg = !InListe(neuA);
                bool index = _designs[basis.BuildQueue[^1] % _designs.Count].Name == "Probe-B";
                basis.BuildQueue.RemoveAt(basis.BuildQueue.Count - 1);
                okD = grab && weg && index && abgefragt == TextLoeschFrage;
                sb.Append($"\n   {Urteil(okD)} (d) Abfrage »{abgefragt}«, Befehl 507 nach {takte} Takt: Grabstein {(grab ? "ja" : "NEIN")} "
                          + $"(Liste {_designs.Count} Saetze), Zeile weg {(weg ? "ja" : "NEIN")}, Warteschlange zeigt noch »"
                          + $"{_designs[nrB].Name}« {(index ? "ja" : "NEIN")}");
            }
            else sb.Append("\n   FEHL (d) Probe-B nicht angelegt");
        }
        sb.Append($"\n   Ankunft: {EntwurfKnopfKlicks - klicks0} Knopfklicks im Behandler");

        // aufraeumen
        OnNamensfenster = merkN; OnAbfragefenster = merkA;
        if (besitzer != -99) basis.Owner = besitzer;
        _selected = sel;

        bool alles = okA && okB && okC && okD;
        if (nullmodell)
            return sb.Append($"\n   Ergebnis: {(!okB && !okC && !okD ? "NULLMODELL ROT wie erwartet ((b)-(d) fallen)" : "⚠ NULLMODELL GRUEN — der Pruefstand misst nichts")}").ToString();
        return sb.Append($"\n   Ergebnis: {(alles ? "BESTANDEN" : "DURCHGEFALLEN")}").ToString();
    }

    // =====================================================================
    // bug-593  --eigene-zeile-check
    // =====================================================================

    public string EigeneZeileCheck()
    {
        bool nullZeile = EigeneZeileAlt, nullSchrift = UI.SchriftZweitfarbe.Alt;
        var sb = new StringBuilder("eigene-zeile-check (bug-593)"
            + (nullZeile ? " (⚠ Nullmodell --eigene-zeile-alt: Teil 1 MUSS fallen)" : "")
            + (nullSchrift ? " (⚠ Nullmodell --schrift-zweitfarbe-alt: Teil 2 MUSS fallen)" : ""));
        if (!Core.Pruefschutz.Aktiv)
            return sb.Append("\n   ⚠ ABBRUCH: nur unter Pruefschutz\n   Ergebnis: DURCHGEFALLEN").ToString();
        LoadDesigns();
        if (_designs == null) return sb.Append("\n   keine Entwurfsliste\n   Ergebnis: DURCHGEFALLEN").ToString();
        int sel = _selected;
        var basis = PruefBasisWaehlen(sb, out int besitzer);
        if (basis == null) return sb.Append("\n   keine Basis\n   Ergebnis: DURCHGEFALLEN").ToString();
        int nr = PruefEntwurfUeberRing("Probe-Ocker", 0);
        sb.Append($"\n   Probeentwurf ueber Befehl 505: {(nr >= 0 ? $"Satz {nr}" : "NICHT angelegt")}");

        // Teil 1: Zeilen mit Own = Zahl der eigenen Entwürfe in dieser Liste
        int soll = 0, ist = 0, vorgabeOwn = 0;
        var menu = BuildableFor(basis.BType, basis.Owner);
        foreach (int i in menu) if (_designs[i].Slot < 0) soll++;
        var rows = BuildPanelRows();
        for (int k = 0; k < rows.Count && k < menu.Count; k++)
        {
            if (!rows[k].Own) continue;
            if (_designs[menu[k]].Slot < 0) ist++; else vorgabeOwn++;
        }
        bool ok1 = soll >= 1 && ist == soll && vorgabeOwn == 0;
        sb.Append($"\n   {Urteil(ok1)} Teil 1: {ist} von {soll} eigenen Zeilen tragen »Own« (Platz 80), Vorgabezeilen mit Own: {vorgabeOwn}");

        // Teil 2: der gemalte Pixel — Glyphenteil 0x24 der eigenen Zeile = Platz 84
        bool ok2 = false;
        string path = Core.Content.Path("UI/akte_font.fnt");
        var f = new FontFile();
        if (FileAccess.FileExists(path) && f.LoadBitmapFont(path) == Error.Ok)
        {
            UI.WindowChrome.SchriftEinrichten(f);
            var rowFg = UI.BaseWindow.TextFg; var ownFg = UI.BaseWindow.EigeneZeileFg;
            var e24 = UI.SchriftZweitfarbe.GemalterPixel(f, true, 0x24, rowFg, ownFg);
            var eFe = UI.SchriftZweitfarbe.GemalterPixel(f, true, 0xFE, rowFg, ownFg);
            var v24 = UI.SchriftZweitfarbe.GemalterPixel(f, false, 0x24, rowFg, ownFg);
            var vFe = UI.SchriftZweitfarbe.GemalterPixel(f, false, 0xFE, rowFg, ownFg);
            static string Rgb(Color? c) => c is { } x ? $"({x.R8},{x.G8},{x.B8})" : "—";
            static bool Nah(Color? c, Color s) => c is { } x && Mathf.Abs(x.R8 - s.R8) <= 2
                                                   && Mathf.Abs(x.G8 - s.G8) <= 2 && Mathf.Abs(x.B8 - s.B8) <= 2;
            bool a = Nah(e24, UI.SchriftZweitfarbe.Platz84), b = Nah(eFe, UI.SchriftZweitfarbe.Platz80);
            ok2 = a && b;
            sb.Append($"\n   {Urteil(ok2)} Teil 2: eigene Zeile 0x24 -> {Rgb(e24)} (Soll Platz 84 (227,199,147)) {(a ? "ja" : "NEIN")}, "
                      + $"0xFE -> {Rgb(eFe)} (Soll Platz 80 (255,255,235)) {(b ? "ja" : "NEIN")}");
            sb.Append($"\n   --   Vorgabezeile zum Vergleich: 0x24 -> {Rgb(v24)}, 0xFE -> {Rgb(vFe)} "
                      + "(Original Platz 36 (187,179,179) / 254 (235,231,231) — 0.76-Naeherung, hier NICHT geurteilt)");
            sb.Append($"\n   --   umgefaerbt: {UI.SchriftZweitfarbe.PixelFe} Pixel 0xFE, {UI.SchriftZweitfarbe.Pixel24} Pixel 0x24");
        }
        else sb.Append($"\n   FEHL Teil 2: Schrift {path} nicht lesbar");

        if (nr >= 0) _designs.RemoveAt(nr);                  // Probe weg (Ende der Liste)
        if (besitzer != -99) basis.Owner = besitzer;
        _selected = sel;
        if (nullZeile || nullSchrift)
        {
            bool rot = (!nullZeile || !ok1) && (!nullSchrift || !ok2);
            return sb.Append($"\n   Ergebnis: {(rot ? "NULLMODELL ROT wie erwartet" : "⚠ NULLMODELL GRUEN — der Pruefstand misst nichts")}").ToString();
        }
        return sb.Append($"\n   Ergebnis: {(ok1 && ok2 ? "BESTANDEN" : "DURCHGEFALLEN")}").ToString();
    }

    // =====================================================================
    // bug-594  --entwurf-lebensdauer-check
    // =====================================================================

    public string EntwurfLebensdauerCheck()
    {
        bool nullmodell = EntwuerfeGlobal;
        var sb = new StringBuilder("entwurf-lebensdauer-check (bug-594)"
            + (nullmodell ? " (⚠ Nullmodell --entwuerfe-global: Gefecht- und Reset-Teil MUESSEN fallen)" : ""));
        // ⚠⚠ bug-548: NUR unter Prüfschutz — dieser Prüfstand schreibt campaign.cfg,
        // die Gefechtsbibliothek und setzt die Kampagne zurück.
        if (!Core.Pruefschutz.Aktiv)
            return sb.Append("\n   ⚠ ABBRUCH: nur unter Pruefschutz\n   Ergebnis: DURCHGEFALLEN").ToString();
        string cfgPfad = OwnDesignsPath;
        if (!cfgPfad.StartsWith(Core.Pruefschutz.Ordner) || !GefechtPfad.StartsWith(Core.Pruefschutz.Ordner)
            || !Campaign.CampaignManager.SavePath.StartsWith(Core.Pruefschutz.Ordner))
            return sb.Append("\n   ⚠ ABBRUCH: ein Pfad liegt ausserhalb von user://pruefstand\n   Ergebnis: DURCHGEFALLEN").ToString();
        sb.Append($"\n   Pfade: {cfgPfad}, {GefechtPfad}, {Campaign.CampaignManager.SavePath}");

        int mission = UI.SkirmishSetup.CampaignMission;
        bool aktiv = UI.SkirmishSetup.Active;
        if (mission <= 0) return sb.Append("\n   ⚠ ABBRUCH: braucht --campaign=N\n   Ergebnis: DURCHGEFALLEN").ToString();
        string md5Vor = FileAccess.FileExists(cfgPfad) ? FileAccess.GetMd5(cfgPfad) : "(keine)";
        var cfgNamen = new HashSet<string>();
        foreach (var t in CfgLesen(cfgPfad)) cfgNamen.Add(t.Item1);
        bool gefechtVorher = FileAccess.FileExists(GefechtPfad);
        int cfgGeschrieben0 = DesignsCfgGeschrieben;
        sb.Append($"\n   designs.cfg (Kopie): {cfgNamen.Count} Entwuerfe, md5 {md5Vor}; Gefechtsbibliothek vorher {(gefechtVorher ? "DA" : "nicht da")}");
        LoadDesigns();
        UI.UnitStatBook.Load();
        var kampagneNamen = new System.Func<HashSet<string>>(() =>
        {
            var h = new HashSet<string>();
            foreach (var t in Campaign.CampaignManager.Entwuerfe) h.Add(t.Name);
            return h;
        });
        bool InListe(string n) => _designs != null && _designs.Exists(d => d.Name == n && !d.Entfernt && d.Slot < 0);
        void Neu(int m) { UI.SkirmishSetup.CampaignMission = m; _designs = null; _designsMission = -1; LoadDesigns(); }

        // T1 Kampagne: Entwurf über 505 → im Kampagnenstand, nicht in der Gefechtsbibliothek
        int nrK = PruefEntwurfUeberRing("Probe-Kampagne", 0);
        bool t1 = nrK >= 0 && kampagneNamen().Contains("Probe-Kampagne");
        sb.Append($"\n   {Urteil(t1)} T1 Kampagne M{mission}: »Probe-Kampagne« ueber 505 angelegt {(nrK >= 0 ? "ja" : "NEIN")}, "
                  + $"steht in campaign.cfg [entwuerfe] {(kampagneNamen().Contains("Probe-Kampagne") ? "ja" : "NEIN")}");

        // T2 Missionswechsel: neu aufgebaute Liste von M+1 trägt ihn
        Neu(mission + 1);
        bool t2 = InListe("Probe-Kampagne");
        sb.Append($"\n   {Urteil(t2)} T2 Missionswechsel M{mission}->M{mission + 1}: Entwurf {(t2 ? "da" : "WEG")}");
        Neu(mission);

        // T3 Spielstand: Grabstein + Entwurf rein, Liste leeren, laden → beide an ihrem Index
        nrK = _designs!.FindIndex(d => d.Name == "Probe-Kampagne");
        int nrT = PruefEntwurfUeberRing("Probe-Grab", 1);
        if (nrT >= 0) { PostEntwurfEntfernen(nrT); PruefRingLeeren(); }
        int nrK2 = PruefEntwurfUeberRing("Probe-Danach", 2);
        bool grabVor = nrT >= 0 && nrT < _designs.Count && _designs[nrT].Entfernt;
        string json = SaveStateJson("probe", "entwurf-lebensdauer");
        var parse = new Json();
        bool t3 = false, t4 = false;
        if (parse.Parse(json) == Error.Ok && parse.Data.VariantType == Variant.Type.Dictionary)
        {
            var root = parse.Data.AsGodotDictionary<string, Variant>();
            bool abschnitt = root.ContainsKey("entwuerfe");
            int anzahl = _designs.Count;
            _designs.RemoveAll(d => d.Slot < 0);                                      // ⚠ EINGRIFF: anderer Stand
            Campaign.CampaignManager.Entwuerfe = new List<(string, int, int, int)>();
            ApplySaveState(root);
            string N(int i) => i >= 0 && i < _designs.Count ? _designs[i].Name : "-";
            bool Grab(int i) => i >= 0 && i < _designs.Count && _designs[i].Entfernt;
            t3 = abschnitt && grabVor && _designs.Count == anzahl
                 && N(nrK) == "Probe-Kampagne" && Grab(nrT) && N(nrT) == "Probe-Grab" && N(nrK2) == "Probe-Danach"
                 && kampagneNamen().Contains("Probe-Kampagne") && !kampagneNamen().Contains("Probe-Grab");
            sb.Append($"\n   {Urteil(t3)} T3 Spielstand: Abschnitt {(abschnitt ? "da" : "FEHLT")}, nach Laden {_designs.Count}/{anzahl} Saetze, "
                      + $"Satz {nrK} »{N(nrK)}«, Grabstein Satz {nrT} {(Grab(nrT) ? "bleibt Grabstein" : "NICHT")}, "
                      + $"Satz {nrK2} »{N(nrK2)}«,Kampagnenstand {(kampagneNamen().Contains("Probe-Kampagne") ? "zurueck" : "LEER")}");

            // T4 älterer Spielstand ohne Abschnitt: lädt, Liste bleibt
            root.Remove("entwuerfe");
            int alt0 = EntwuerfeAltstand, vor = _designs.Count;
            string fehler = "";
            try { ApplySaveState(root); } catch (System.Exception ex) { fehler = ex.GetType().Name + ": " + ex.Message; }
            t4 = fehler.Length == 0 && EntwuerfeAltstand == alt0 + 1 && _designs.Count == vor && InListe("Probe-Kampagne");
            sb.Append($"\n   {Urteil(t4)} T4 aelterer Spielstand ohne Abschnitt: {(fehler.Length == 0 ? "laedt" : "AUSNAHME " + fehler)}, "
                      + $"Liste {_designs.Count}/{vor}, Entwurf {(InListe("Probe-Kampagne") ? "da" : "WEG")}");
        }
        else sb.Append("\n   FEHL T3/T4: Spielstand nicht lesbar");

        // T5 Gefecht: Kampagnenentwurf NICHT da; Bibliothek eigene Datei; Entwurf im Gefecht nur dort
        UI.SkirmishSetup.Active = true;
        Neu(0);
        bool kWeg = !InListe("Probe-Kampagne");
        bool datei = FileAccess.FileExists(GefechtPfad);
        int nrG = PruefEntwurfUeberRing("Probe-Gefecht", 0);
        var bib = new HashSet<string>();
        foreach (var t in CfgLesen(GefechtPfad)) bib.Add(t.Item1);
        bool gInBib = nrG >= 0 && bib.Contains("Probe-Gefecht") && !kampagneNamen().Contains("Probe-Gefecht");
        bool t5 = kWeg && datei && gInBib;
        sb.Append($"\n   {Urteil(t5)} T5 Gefecht: Kampagnenentwurf {(kWeg ? "nicht da" : "DA")}, Bibliothek {(datei ? "da" : "FEHLT")}, "
                  + $"»Probe-Gefecht« nur in der Bibliothek {(gInBib ? "ja" : "NEIN")} ({bib.Count} Eintraege)");
        // zweites Gefecht: Bibliothek bleibt
        Neu(0);
        bool bleibt = InListe("Probe-Gefecht");
        sb.Append($"\n   {Urteil(bleibt)} T5b naechstes Gefecht: Bibliothekseintrag {(bleibt ? "bleibt" : "WEG")}");
        t5 &= bleibt;

        // T6 Übernahme: designs.cfg einmal übernommen, nie geschrieben
        string md5Nach = FileAccess.FileExists(cfgPfad) ? FileAccess.GetMd5(cfgPfad) : "(keine)";
        bool alleDa = true;
        foreach (var n in cfgNamen) if (!bib.Contains(n)) alleDa = false;
        bool t6 = md5Nach == md5Vor && DesignsCfgGeschrieben == cfgGeschrieben0
                  && (gefechtVorher || cfgNamen.Count == 0 || (GefechtUebernommen == 1 && alleDa));
        sb.Append($"\n   {Urteil(t6)} T6 designs.cfg: md5 {(md5Nach == md5Vor ? "unveraendert" : "VERAENDERT " + md5Nach)}, "
                  + $"geschrieben {DesignsCfgGeschrieben - cfgGeschrieben0}x, uebernommen {GefechtUebernommen}x, "
                  + $"alle {cfgNamen.Count} in der Bibliothek {(alleDa ? "ja" : "NEIN")}");

        // T7 Neue Kampagne: Reset leert
        UI.SkirmishSetup.Active = aktiv;
        Neu(mission);
        bool vorReset = InListe("Probe-Kampagne");
        Campaign.CampaignManager.Reset();
        EigeneEntwuerfeNeuLaden();
        int eigene = _designs!.FindAll(d => d.Slot < 0 && !d.Entfernt).Count;
        bool t7 = vorReset && eigene == 0 && Campaign.CampaignManager.Entwuerfe.Count == 0;
        sb.Append($"\n   {Urteil(t7)} T7 neue Kampagne (CampaignManager.Reset): vorher {(vorReset ? "da" : "WEG")}, danach {eigene} eigene Entwuerfe, "
                  + $"campaign.cfg [entwuerfe] {Campaign.CampaignManager.Entwuerfe.Count}");

        UI.SkirmishSetup.CampaignMission = mission;
        UI.SkirmishSetup.Active = aktiv;
        bool alles = t1 && t2 && t3 && t4 && t5 && t6 && t7;
        if (nullmodell)
            return sb.Append($"\n   Ergebnis: {(!t5 && !t7 ? "NULLMODELL ROT wie erwartet (Gefecht und Reset fallen)" : "⚠ NULLMODELL GRUEN — der Pruefstand misst nichts")}").ToString();
        return sb.Append($"\n   Ergebnis: {(alles ? "BESTANDEN" : "DURCHGEFALLEN")}").ToString();
    }
}
