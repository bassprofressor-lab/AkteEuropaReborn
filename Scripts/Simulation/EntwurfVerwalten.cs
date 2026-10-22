namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>EIGENE ENTWÜRFE VERWALTEN</b> — Testerrunde 0.6.11 Paket D (22.10.2026),
/// <c>berichte/eigene-entwuerfe-opus.md</c> R2, R2b, R3.
///
/// <para><b>bug-592 (Reaper R2) — Umbenennen und Entfernen.</b> Im Original sind
/// beide Knöpfe IMMER drückbar (kein Zweig im Zeichner <c>0x467C60…0x46B61C</c>
/// färbt sie); die Sperre ist allein die Meldung beim Klick:</para>
/// <code>
///   Umbenennen 0x44A440  +0x01 == 0 @0x44A46B → »Vordefinierte Einheiten können
///                        nicht geändert werden!« (0x4FBE34, F 0x44947B)
///                        sonst 0x442380: Eingabefenster Art 36, 300×60, höchstens
///                        19 Zeichen (+0x8B9242 = 0x13), Frage »Wählen Sie einen
///                        Namen für dieses Teil« (0x4FC53C); Bestätigen Modus 2
///                        0x44D0F2 → Befehl 533 (F 0x44C0D4)
///   Busarm 533 0x4C3CAE  strncpy(Name, 0x15) — keine weitere Prüfung
///   Entfernen  0x44A4BF  +0x01 == 0 → »Vordefinierte Einheiten können nicht
///                        gelöscht werden!« (0x4FBE98, F 0x449532); lebende Einheit
///                        mit diesem Entwurf (0x44A554…0x44A5A9) → »Einheitentypus
///                        kann nicht gelöscht werden« / »Sie haben bereits
///                        Einheiten diesen Typs erstellt.« (0x4FCF00 / 0x4FBEFC);
///                        sonst 0x446880: Abfragefenster Art 10, 300×100,
///                        »Einheitentyp löschen?« (0x4FC9EC); Knopf 1 (0x44B0EB)
///                        → Befehl 507 (0x44B109, F 0x44A0FC) → 0x4B2AE0 nullt
///                        +0x00/+0x18
/// </code>
///
/// <para>⚠ <b>UNSERE SETZUNGEN</b> (je einzeln):</para>
/// <list type="bullet">
/// <item>533 trägt den Namen in P3…P11 und P15 (20 Zeichen) statt ab +0x24 (bei uns
/// ist P15 ein Wort); 507/533 tragen als »Satz« den Index in <c>_designs</c>.</item>
/// <item>Gleichnamigkeit: das Original prüft bei 533 nicht; bei uns ist der Name der
/// Schlüssel (Einheiten, Wertebuch, Mitnahme hängen daran) — gleiche Regel wie 505.</item>
/// <item>Umbenennen zieht die Einheiten mit altem Namen mit: im Original behalten sie
/// die SATZNUMMER und zeigen dadurch den neuen Namen; bei uns hängen sie am Namen.</item>
/// <item>Entfernen wird auch abgewiesen, wenn der Entwurf in einer Warteschlange, im
/// Bau oder im Depot steht — das Original kennt keine Warteschlange von Entwürfen;
/// geprüft werden Einheiten ALLER Spieler (unsere Entwurfsliste ist nicht je Spieler).</item>
/// <item>Ein Grabstein (<see cref="Design.Entfernt"/>) statt <c>RemoveAt</c> — wie das
/// Original, das nur <c>+0x00</c> nullt; Indizes in Warteschlangen und Spielständen
/// bleiben stabil.</item>
/// <item>Die Knöpfe wirken nur im Reiter »Produktion« einer Basis.</item>
/// </list>
/// <para>Gegenschalter <c>--entwurf-knoepfe-alt</c> (Knöpfe gesperrt, Stand 0.6.10).</para>
///
/// <para><b>bug-594 (Reaper R3) — die Lebensdauer.</b> Original: sec47 Plätze 0…49 des
/// Spielers; Missionsstart <c>0x4B23C0</c> nullt nur 50…199 (überleben den
/// Missionswechsel), der Spielstand trägt sec47 (<c>0x41D6F1</c>/<c>0x41DB20</c>,
/// Lader <c>0x41E762</c>), »Neue Kampagne« nullt 0…49 (<c>0x44D0C6…0x44D0E8</c>),
/// Gefechtsstart nullt 0…49 aller Spieler (<c>0x41ACE2</c>).</para>
/// <list type="bullet">
/// <item>Kampagne: wie das Original — Abschnitt <c>[entwuerfe]</c> in
/// <c>campaign.cfg</c> (⚠ UNSERE SETZUNG: Ablageort), in jedem Spielstand
/// (<c>"entwuerfe"</c>), geleert in <c>CampaignManager.Reset</c>.</item>
/// <item>Gefecht: ⚠ <b>ABWEICHUNG auf Spielerwunsch</b> (22.10.2026,
/// Wettkampfmodus) — eine EIGENE bleibende Bibliothek
/// <c>user://designs-gefecht.cfg</c>; das Original beginnt jedes Gefecht leer.</item>
/// <item>Übergang: die alte <c>designs.cfg</c> wird EINMAL in die Gefechtsbibliothek
/// übernommen (wenn diese noch nicht existiert) und danach nie gelöscht und nie
/// überschrieben.</item>
/// <item>Netz: wie bisher ohne Dateientwürfe (<c>NetzStartDirektbefehle2</c>).</item>
/// </list>
/// <para>Gegenschalter <c>--entwuerfe-global</c> (Stand 0.6.10: eine
/// <c>designs.cfg</c> für alles, nichts im Spielstand).</para>
/// </summary>
public partial class MapEntityLayer
{
    // ================= SCHALTER ===============================================

    /// <summary><c>--entwurf-knoepfe-alt</c>: Umbenennen/Entfernen gesperrt (0.6.10).</summary>
    public static bool EntwurfKnoepfeAlt;

    /// <summary><c>--eigene-zeile-alt</c>: <see cref="Design.Selbst"/> wird für eigene
    /// Entwürfe nicht gesetzt (0.6.10) — die Bauliste zeigt sie wie Vorgaben.</summary>
    public static bool EigeneZeileAlt;

    /// <summary><c>--entwuerfe-global</c>: eine <c>designs.cfg</c> für Kampagne und
    /// Gefecht, nichts im Spielstand (Stand 0.6.10).</summary>
    public static bool EntwuerfeGlobal;

    // ================= TEXTE DES ORIGINALS ====================================

    public const string TextNichtAendern = "Vordefinierte Einheiten können nicht geändert werden!";   // 0x4FBE34
    public const string TextNichtLoeschen = "Vordefinierte Einheiten können nicht gelöscht werden!";  // 0x4FBE98
    public const string TextTypusBleibt = "Einheitentypus kann nicht gelöscht werden";               // 0x4FCF00
    public const string TextSchonEinheiten = "Sie haben bereits Einheiten diesen Typs erstellt.";    // 0x4FBEFC
    public const string TextLoeschFrage = "Einheitentyp löschen?";                                   // 0x4FC9EC
    public const string TextNamensfrage = "Wählen Sie einen Namen für dieses Teil";                  // 0x4FC53C
    /// <summary>Höchstlänge des Namens: <c>0x45B230(…, 0x13)</c> → <c>+0x8B9242 = 0x13</c>.</summary>
    public const int EntwurfNameMax = 19;

    // ================= ABLAGE (bug-594) =======================================

    public const string GefechtEchterPfad = "user://designs-gefecht.cfg";

    /// <summary>Die Gefechtsbibliothek — unter Prüfschutz die Kopie in
    /// <c>user://pruefstand/</c> (Core/Pruefschutz.cs).</summary>
    public static string GefechtPfad => Core.Pruefschutz.Pfad(GefechtEchterPfad);

    /// <summary>Gehört die laufende Partie zu einer Kampagne?</summary>
    private static bool EntwurfKampagne => UI.SkirmishSetup.CampaignMission > 0;

    /// <summary>Wie oft in diesem Lauf <c>designs.cfg</c> geschrieben wurde (nur noch
    /// unter <c>--entwuerfe-global</c>) bzw. die Gefechtsbibliothek / der
    /// Kampagnenabschnitt — für den Prüfstand.</summary>
    public static int DesignsCfgGeschrieben, GefechtGeschrieben, KampagneGeschrieben, GefechtUebernommen;

    /// <summary>Die eigenen, nicht entfernten Entwürfe der Liste, in Listenfolge.</summary>
    private static List<(string Name, int Prop, int Equip, int Weapon)> EigeneListe()
    {
        var l = new List<(string, int, int, int)>();
        if (_designs == null) return l;
        foreach (var d in _designs)
            if (d.Slot < 0 && !d.Entfernt) l.Add((d.Name, d.Propulsion, d.Equip, d.Weapon));
        return l;
    }

    /// <summary>Schreibt die eigenen Entwürfe dorthin, wo sie in dieser Partie hingehören.</summary>
    private static void SaveOwnDesigns()
    {
        if (_designs == null) return;
        if (EntwuerfeGlobal) { DesignsCfgGeschrieben++; SaveOwnDesignsGlobal(); return; }
        var liste = EigeneListe();
        if (EntwurfKampagne) { KampagneGeschrieben++; Campaign.CampaignManager.Entwuerfe = liste; }
        else GefechtSchreiben(liste);
    }

    /// <summary>Hängt die eigenen Entwürfe dieser Partie an die Liste.</summary>
    private static void LoadOwnDesigns()
    {
        if (_designs == null) return;
        if (EntwuerfeGlobal) { LoadOwnDesignsGlobal(); return; }
        var liste = EntwurfKampagne ? Campaign.CampaignManager.Entwuerfe : GefechtLesen();
        int n = EigeneAnhaengen(liste);
        if (n > 0) GD.Print($"designs: {n} eigene Entwuerfe aus " +
                            (EntwurfKampagne ? $"{Campaign.CampaignManager.SavePath} [entwuerfe]" : GefechtPfad));
    }

    private static int EigeneAnhaengen(List<(string Name, int Prop, int Equip, int Weapon)> liste)
    {
        var seen = new HashSet<string>();
        foreach (var d in _designs!) if (!d.Entfernt) seen.Add(d.Name);
        int n = 0;
        foreach (var (nm, p, e, w) in liste)
        {
            if (nm.Length == 0 || !seen.Add(nm)) continue;
            _designs.Add(new Design(nm, p, e, w, true, -1) { Selbst = !EigeneZeileAlt });
            n++;
        }
        return n;
    }

    /// <summary>Nach <c>CampaignManager.Reset</c> (neue Kampagne): die eigenen
    /// Entwürfe der schon aufgebauten Liste neu aus der (jetzt leeren) Ablage.</summary>
    public void EigeneEntwuerfeNeuLaden()
    {
        if (_designs == null) return;
        _designs.RemoveAll(d => d.Slot < 0);
        LoadOwnDesigns();
    }

    private static List<(string, int, int, int)> CfgLesen(string pfad)
    {
        var l = new List<(string, int, int, int)>();
        using var c = new ConfigFile();
        if (c.Load(pfad) != Error.Ok) return l;
        int n = (int)c.GetValue("designs", "count", 0);
        for (int i = 0; i < n; i++)
        {
            var v = c.GetValue("designs", $"d{i}", new Godot.Collections.Array());
            if (v.VariantType != Variant.Type.Array) continue;
            var a = v.AsGodotArray();
            if (a.Count < 4 || a[0].AsString().Length == 0) continue;
            l.Add((a[0].AsString(), a[1].AsInt32(), a[2].AsInt32(), a[3].AsInt32()));
        }
        return l;
    }

    /// <summary>Die Gefechtsbibliothek lesen — gibt es sie noch nicht, vorher EINMAL
    /// die alte <c>designs.cfg</c> übernehmen (die bleibt unangetastet liegen).</summary>
    private static List<(string, int, int, int)> GefechtLesen()
    {
        if (!FileAccess.FileExists(GefechtPfad) && FileAccess.FileExists(OwnDesignsPath))
        {
            var alt = CfgLesen(OwnDesignsPath);
            GefechtSchreiben(alt, "designs.cfg (einmalig uebernommen, bug-594)");
            GefechtUebernommen++;
            GD.Print($"designs: {alt.Count} Entwuerfe aus {OwnDesignsPath} EINMAL in die Gefechtsbibliothek " +
                     $"{GefechtPfad} uebernommen — designs.cfg bleibt unveraendert liegen");
        }
        return CfgLesen(GefechtPfad);
    }

    private static void GefechtSchreiben(List<(string Name, int Prop, int Equip, int Weapon)> liste, string herkunft = "")
    {
        using var c = new ConfigFile();
        // Herkunftsvermerk behalten, wenn es die Datei schon gibt.
        string alt = "";
        using (var vorher = new ConfigFile())
            if (vorher.Load(GefechtPfad) == Error.Ok) alt = (string)vorher.GetValue("designs", "herkunft", "");
        int n = 0;
        foreach (var (nm, p, e, w) in liste)
            c.SetValue("designs", $"d{n++}", new Godot.Collections.Array { nm, p, e, w });
        c.SetValue("designs", "count", n);
        string h = herkunft.Length > 0 ? herkunft : alt;
        if (h.Length > 0) c.SetValue("designs", "herkunft", h);
        GefechtGeschrieben++;
        Core.Pruefschutz.Gemeldet("designs-gefecht.cfg", "GefechtSchreiben");
        c.Save(GefechtPfad);
    }

    /// <summary>Für das Wertebuch (UI.UnitStatBook): die eigenen Entwürfe dieser
    /// Partie — aus der Liste, sonst aus der Ablage.</summary>
    public static List<(string Name, int Prop, int Equip, int Weapon)> EigeneEntwuerfeFuerBuch()
    {
        if (_designs != null) return EigeneListe();
        if (EntwuerfeGlobal) return CfgLesen(OwnDesignsPath);
        return EntwurfKampagne ? Campaign.CampaignManager.Entwuerfe : CfgLesen(GefechtPfad);
    }

    // ---- im Spielstand (entspricht sec47 in game.00N) ------------------------

    /// <summary>Spielstand schreiben: ALLE eigenen Sätze in Listenfolge, auch die
    /// Grabsteine — Warteschlangen und Depots halten Indizes.</summary>
    private void EntwuerfeInSpielstand(Core.SaveGame.Writer w)
    {
        if (EntwuerfeGlobal || _designs == null) return;
        w.ArrayStart("entwuerfe");
        foreach (var d in _designs)
        {
            if (d.Slot >= 0) continue;
            w.ItemStart();
            w.Str("name", d.Name);
            w.Num("fahrwerk", d.Propulsion);
            w.Num("ausruestung", d.Equip);
            w.Num("waffe", d.Weapon);
            w.Bool("entfernt", d.Entfernt);
            w.ItemEnd();
        }
        w.ArrayEnd();
    }

    /// <summary>Wie oft ein Spielstand OHNE Entwurfsabschnitt geladen wurde.</summary>
    public static int EntwuerfeAltstand;

    /// <summary>Spielstand laden — VOR den Einheiten (deren Warteschlangen sind Indizes).
    /// Ein älterer Spielstand ohne Abschnitt lässt die Liste, wie sie ist.</summary>
    private void EntwuerfeAusSpielstand(Godot.Collections.Dictionary<string, Variant> root)
    {
        if (EntwuerfeGlobal || _designs == null) return;
        if (!root.TryGetValue("entwuerfe", out var v) || v.VariantType != Variant.Type.Array)
        {
            EntwuerfeAltstand++;
            GD.Print("Spielstand: kein Entwurfsabschnitt (aelterer Stand) — eigene Entwuerfe bleiben, wie sie sind");
            return;
        }
        var vorher = EigeneListe();
        _designs.RemoveAll(d => d.Slot < 0);
        foreach (var it in v.AsGodotArray())
        {
            if (it.VariantType != Variant.Type.Dictionary) continue;
            var d = it.AsGodotDictionary<string, Variant>();
            string nm = d.TryGetValue("name", out var nv) ? nv.AsString() : "";
            bool weg = d.TryGetValue("entfernt", out var ev) && ev.AsBool();
            _designs.Add(new Design(nm, GetI(d, "fahrwerk", 0), GetI(d, "ausruestung", 0), GetI(d, "waffe", 0), true, -1)
                         { Selbst = !EigeneZeileAlt, Entfernt = weg || nm.Length == 0 });
        }
        // Kampagne: der Spielstand IST der Kampagnenstand (sec47 wird geladen).
        // Gefecht: die Bibliothek bleibt; was dort seither dazukam, hinten an.
        if (EntwurfKampagne) { KampagneGeschrieben++; Campaign.CampaignManager.Entwuerfe = EigeneListe(); }
        else EigeneAnhaengen(vorher);
        foreach (var (nm, p, e, w) in EigeneListe()) UI.UnitStatBook.Eintragen(nm, w, p, e);
        GD.Print($"Spielstand: {EigeneListe().Count} eigene Entwuerfe geladen");
    }

    // ================= KNÖPFE (bug-592) =======================================

    /// <summary>Das Eingabefenster Art 36 (MapViewer → UI/EntwurfDialog.cs):
    /// Frage, Höchstlänge, bei Bestätigen.</summary>
    public System.Action<string, int, System.Action<string>>? OnNamensfenster;

    /// <summary>Das Abfragefenster Art 10: Frage, bei »Ja«.</summary>
    public System.Action<string, System.Action>? OnAbfragefenster;

    /// <summary>Ankunft der Klicks (Messfalle 1 der Fensterwelt).</summary>
    public int EntwurfKnopfKlicks;

    /// <summary>Die letzte Meldung von <see cref="MeldungBasis"/> — Prüfstand.</summary>
    public string LetzteBasisMeldung = "";

    /// <summary>Welcher Entwurf steht in Zeile <paramref name="zeile"/> der
    /// Produktionsliste der gewählten Basis? −1 = keiner.</summary>
    private int EntwurfDerZeile(int zeile)
    {
        if (_designs == null) return -1;
        var e = Producer();
        if (e == null || !IsUnitPlant(e)) return -1;
        var menu = BuildableFor(e.BType, e.Owner);
        return zeile >= 0 && zeile < menu.Count ? menu[zeile] : -1;
    }

    /// <summary>Knopf »Umbenennen« (Art 6, Element 10, <c>0x44A440</c>).</summary>
    public void EntwurfUmbenennenKlick(int zeile)
    {
        if (EntwurfKnoepfeAlt) return;
        EntwurfKnopfKlicks++;
        int nr = EntwurfDerZeile(zeile);
        if (nr < 0) return;
        if (_designs![nr].Slot >= 0) { MeldungBasis(TextNichtAendern); return; }   // @0x44A46B
        if (OnNamensfenster == null) { _order = "Umbenennen: kein Eingabefenster"; return; }
        OnNamensfenster(TextNamensfrage, EntwurfNameMax, name => PostEntwurfUmbenennen(nr, name));
    }

    /// <summary>Knopf »Entfernen« (Art 6, Element 11, <c>0x44A4BF</c>).</summary>
    public void EntwurfEntfernenKlick(int zeile)
    {
        if (EntwurfKnoepfeAlt) return;
        EntwurfKnopfKlicks++;
        int nr = EntwurfDerZeile(zeile);
        if (nr < 0) return;
        if (_designs![nr].Slot >= 0) { MeldungBasis(TextNichtLoeschen); return; }  // @0x449532 (F)
        if (EntwurfInGebrauch(nr, out _)) { MeldungBasis(TextTypusBleibt, TextSchonEinheiten); return; }
        if (OnAbfragefenster == null) { _order = "Entfernen: kein Abfragefenster"; return; }
        OnAbfragefenster(TextLoeschFrage, () => PostEntwurfEntfernen(nr));
    }

    /// <summary>Die Zeichenannahme des Namensfeldes <c>0x4588E0</c>: 0x20…0x7A ohne
    /// <c>[ ] ^</c> (die Sinnbilder der Schrift), höchstens 19 Zeichen.</summary>
    public static string EntwurfNameFiltern(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in s)
        {
            if (sb.Length >= EntwurfNameMax) break;
            if (ch >= 0x20 && ch <= 0x7A && ch is not ('[' or ']' or '^')) sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Befehl 533 absetzen (Eingabefenster bestätigt).</summary>
    public void PostEntwurfUmbenennen(int nr, string name)
    {
        name = EntwurfNameFiltern(name);
        if (name.Length == 0) return;
        var c = CommandRecord.Make(CommandOp.EntwurfUmbenennen, (byte)ViewPlayer, (short)nr, (short)ViewPlayer);
        NamePacken(ref c, name, 3, 9);                      // Zeichen 0..17
        int lo = name.Length > 18 ? name[18] & 0xFF : 0;    // Zeichen 18..19 in P15
        int hi = name.Length > 19 ? name[19] & 0xFF : 0;
        c.P15 = (short)(lo | (hi << 8));
        Emit(c);
    }

    /// <summary>Befehl 507 absetzen (Abfragefenster »Ja«).</summary>
    public void PostEntwurfEntfernen(int nr)
        => Emit(CommandRecord.Make(CommandOp.EntwurfEntfernen, (byte)ViewPlayer, (short)ViewPlayer, (short)nr));

    private static string NameAus533(in CommandRecord c)
    {
        string s = NameEntpacken(c, 3, 9);
        if (s.Length < 18) return s;
        int w = (ushort)c.P15;
        if ((w & 0xFF) != 0) { s += (char)(w & 0xFF); if (((w >> 8) & 0xFF) != 0) s += (char)((w >> 8) & 0xFF); }
        return s;
    }

    /// <summary>Steht der Entwurf noch in Gebrauch? Lebende Einheit (Original
    /// <c>0x44A554…0x44A5A9</c>), dazu UNSERE SETZUNG: Depot, Warteschlange, im Bau.</summary>
    private bool EntwurfInGebrauch(int nr, out string wo)
    {
        wo = "";
        if (_designs == null || nr < 0 || nr >= _designs.Count) return false;
        string name = _designs[nr].Name;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead) continue;
            if (!e.IsBuilding)
            {
                if (e.Entwurf3E < 0 && e.Name == name) { wo = $"Einheit {e.Slot}"; return true; }
                continue;
            }
            if (e.Depot.Contains(nr)) { wo = $"Depot von Gebaeude {e.Slot}"; return true; }
            if (e.BuildQueue.Contains(nr) || (e.BuildTime > 0f && e.BuildIndex == nr))
            { wo = $"Warteschlange von Gebaeude {e.Slot}"; return true; }
        }
        return false;
    }

    private bool ApplyEntwurfUmbenennen(in CommandRecord c)
    {
        if (_designs == null || c.P2 != c.Player) return false;
        int nr = c.P1;
        if (nr < 0 || nr >= _designs.Count) return false;
        var d = _designs[nr];
        if (d.Slot >= 0 || d.Entfernt) return false;
        string neu = EntwurfNameFiltern(NameAus533(c));
        if (neu.Length == 0 || neu == d.Name) return false;
        for (int i = 0; i < _designs.Count; i++)
            if (i != nr && !_designs[i].Entfernt && _designs[i].Name == neu)
            { if (c.Player == ViewPlayer) _order = "Umbenennen: diesen Namen gibt es schon"; return false; }
        string alt = d.Name;
        _designs[nr] = d with { Name = neu };
        int mit = 0;
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && e.Entwurf3E < 0 && e.Name == alt) { e.Name = neu; mit++; }
        UI.UnitStatBook.Austragen(alt);
        UI.UnitStatBook.Eintragen(neu, d.Weapon, d.Propulsion, d.Equip);
        if (!ImNetz && !_entwurfProbe) SaveOwnDesigns();
        if (c.Player == ViewPlayer) _order = $"Entwurf »{alt}« heisst jetzt »{neu}«";
        GD.Print($"designer: '{alt}' -> '{neu}' (Befehl 533, Spieler {c.Player}, Satz {nr}, {mit} Einheiten mitbenannt)");
        return true;
    }

    private bool ApplyEntwurfEntfernen(in CommandRecord c)
    {
        if (_designs == null || c.P1 != c.Player) return false;
        int nr = c.P2;
        if (nr < 0 || nr >= _designs.Count) return false;
        var d = _designs[nr];
        if (d.Slot >= 0 || d.Entfernt) return false;
        if (EntwurfInGebrauch(nr, out string wo))
        {
            if (c.Player == ViewPlayer) MeldungBasis(TextTypusBleibt, TextSchonEinheiten);
            GD.Print($"designer: '{d.Name}' NICHT entfernt (Befehl 507) — in Gebrauch: {wo}");
            return false;
        }
        _designs[nr] = d with { Entfernt = true };
        UI.UnitStatBook.Austragen(d.Name);
        if (!ImNetz && !_entwurfProbe) SaveOwnDesigns();
        if (c.Player == ViewPlayer) _order = $"Entwurf »{d.Name}« entfernt";
        GD.Print($"designer: '{d.Name}' entfernt (Befehl 507, Spieler {c.Player}, Satz {nr} bleibt als Grabstein)");
        return true;
    }
}
