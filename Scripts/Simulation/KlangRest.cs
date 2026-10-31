namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// ⭐ <b>PAKET R — DER PRUEFSTAND <c>--klang-rest-check</c></b> (30.10.2026,
/// <c>berichte/klangabgleich-rest-opus.md</c> §4, Bau <c>berichte/bau-klang-rest.md</c>,
/// bug-677..bug-681). Kopflos, beendet sich selbst.
///
/// <para>Gemessen wird die ANKUNFT ueber <see cref="Audio.SoundBankPlayer.Horcher"/>
/// (vor jedem Riegel, seit bug-681 auch fuer <c>PlayVoice</c>). Jeder Teil hat
/// seinen Gegenschalter als Nullmodell, und der muss genau SEINEN Teil rot
/// machen. Die Teile, die die Oberflaeche brauchen (E Briefing, K Missionsende,
/// M Menuezeile), laufen in <c>MapViewer.KlangRestLauf</c>.</para>
///
/// <list type="bullet">
/// <item>A R1 Klangwuerfel: die sechs Klangstellen ziehen KEINEN Wurf aus
/// Determinism (Draws vorher = nachher). Nullmodell <c>--klangwuerfel-alt</c>.</item>
/// <item>B R2 Anwaehlen: 200 Anwahlen je eigener Gattung/Fahrwerk ⊆ Tafel 0x4331E0,
/// Fahrbefehlszeilen (152/189 …) ×0; Flugzeug 183. <c>--anwahlstimme-alt</c>.</item>
/// <item>C R3 Fusstod: je Infanterieart ein Soldat per Treffer getoetet: Ankunft ∈
/// 0x40B2E0, 77..79 ×0. <c>--fusstodstimme-alt</c>.</item>
/// <item>D R4 Hilfe: Regel n = 520/600 je Routine, dazu ein echtes show_text-Fenster.
/// <c>--hilfeklang-alt</c>.</item>
/// <item>F R6 Mechaniker, G R7 Gleis, H R8 Depot, I R9 Nachschub, J R10 Start,
/// L R12 Graben, N R13 Hand, O R14 Waldbrand, P R15 Truemmer — je <c>…-aus</c>.</item>
/// <item>Q bug-691 Aufsetzen 308 (Flugzeug ja, Heli nein) — <c>--landeklang-aus</c>.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    public static bool KlangRestCheckAn;

    private int _krGruen, _krRot, _krOhne;
    private readonly Dictionary<char, bool> _krTeile = new();

    /// <summary>Ein Urteil — oeffentlich, damit MapViewer seine Teile hier einreiht.</summary>
    public void KlangRestUrteil(StringBuilder sb, char teil, bool ok, string was)
    {
        sb.Append($"  {(ok ? "ok  " : "ROT ")} {teil} {was}\n");
        if (ok) _krGruen++; else _krRot++;
        _krTeile[teil] = _krTeile.TryGetValue(teil, out bool alt) ? alt && ok : ok;
    }

    public void KlangRestKeinUrteil(StringBuilder sb, char teil, string was)
    {
        sb.Append($"  --   {teil} KEIN URTEIL: {was}\n");
        _krOhne++;
    }

    public string KlangRestErgebnis()
        => $"  Teile {string.Join(" ", _krTeile.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={(kv.Value ? "gruen" : "ROT")}"))}\n"
         + $"  Ergebnis: {(_krRot == 0 && _krGruen > 0 ? "BESTANDEN" : "DURCHGEFALLEN")} ({_krGruen} gruen, {_krRot} rot, {_krOhne} ohne Urteil)";

    /// <summary>Alle Nummern, die 0x4331E0 liefern kann (Modus 1, siehe SelectVoice).</summary>
    private static readonly HashSet<int> AnwahlTafel = new()
    {
        150, 151, 156, 157, 158, 163, 164, 169, 170, 171, 176, 183,
        187, 188, 193, 194, 195, 199, 200, 205, 206, 207, 211, 212, 217, 218, 219,
        222, 223, 228, 229, 230, 233, 234, 239, 240, 241, 244, 245, 246, 247, 249, 250, 251, 252,
    };

    /// <summary>Die di-Zeilen der Fahrbefehlstafel 0x429290, die 0x4331E0 NIE liefert.</summary>
    private static readonly HashSet<int> NurFahrbefehl = new()
        { 152, 153, 165, 166, 177, 189, 190, 201, 202, 213, 214, 224, 225, 235, 236 };

    /// <summary>Was 0x40B2E0 liefern kann (296/297 sind leere Plaetze).</summary>
    private static readonly HashSet<int> TodesTafel = new() { 196, 197, 208, 209, 220, 231, 242, 252, 296, 297 };

    public string KlangRestCheck()
    {
        var G = Audio.GameSounds.KlangwuerfelAlt;
        var sb = new StringBuilder("klang-rest-check (Paket R, R1-R15)\n");
        void Null(bool an, string schalter, char teil) { if (an) sb.Append($"  ⚠ NULLMODELL {schalter}: Teil {teil} MUSS fallen\n"); }
        Null(Audio.GameSounds.KlangwuerfelAlt, "--klangwuerfel-alt", 'A');
        Null(Audio.GameSounds.AnwahlstimmeAlt, "--anwahlstimme-alt", 'B');
        Null(Audio.GameSounds.FusstodstimmeAlt, "--fusstodstimme-alt", 'C');
        Null(Audio.GameSounds.HilfeklangAlt, "--hilfeklang-alt", 'D');
        Null(Audio.GameSounds.BriefingklangAlt, "--briefingklang-alt", 'E');
        Null(Audio.GameSounds.MechanikerklangAus, "--mechanikerklang-aus", 'F');
        Null(Audio.GameSounds.GleisklangAus, "--gleisklang-aus", 'G');
        Null(Audio.GameSounds.DepotklangAus, "--depotklang-aus", 'H');
        Null(Audio.GameSounds.NachschubklangAus, "--nachschubklang-aus", 'I');
        Null(Audio.GameSounds.FlugklangAus, "--flugklang-aus", 'J');
        Null(Audio.GameSounds.EndklangAus, "--endklang-aus", 'K');
        Null(Audio.GameSounds.GrabklangAus, "--grabklang-aus", 'L');
        Null(Audio.GameSounds.MenuezeilenklangAus, "--menuezeilenklang-aus", 'M');
        Null(Audio.GameSounds.HandstimmeAus, "--handstimme-aus", 'N');
        Null(Audio.GameSounds.WaldbrandklangAus, "--waldbrandklang-aus", 'O');
        Null(Audio.GameSounds.TruemmerklangAus, "--truemmerklang-aus", 'P');
        Null(Audio.GameSounds.LandeklangAus, "--landeklang-aus", 'Q');
        _ = G;

        var gehoert = new List<(int Slot, float C, float R)>();
        var vorher = Audio.SoundBankPlayer.Horcher;
        Audio.SoundBankPlayer.Horcher = (slot, c, r) => gehoert.Add((slot, c, r));
        List<int> Ab(int ab) => gehoert.Skip(ab).Select(x => x.Slot).ToList();
        int Zahl(int ab, System.Func<int, bool> wer) => gehoert.Skip(ab).Count(x => wer(x.Slot));
        bool meldungen = UI.Settings.Announcements;
        int ring = 0;
        if (!meldungen) sb.Append("  ⚠ Meldungen (Einstellung) AUS — B, C, N ohne Urteil; die Einstellung wird NICHT angefasst\n");

        int Eigene(System.Func<Entity, bool> wer)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer) continue;
                if (wer(e)) return i;
            }
            return -1;
        }

        Special? probeHeli = null;
        try
        {
            // ---- A: R1 — kein Klang zieht aus Determinism ------------------------
            {
                int c = _nav != null ? _nav.Width / 2 : 10, r = _nav != null ? _nav.Height / 2 : 10;
                long d0 = Simulation.Determinism.Draws;
                int ab = gehoert.Count;
                for (int k = 0; k < 20; k++)
                {
                    Audio.GameSounds.InfantryDiesPick();                          // Fusstod 77..79
                    TodesKlang(new Entity { Col = c, Row = r, Infantry = -1 });   // 400..403
                    BrandKlang399(c, r);                                          // 399 -> 400..405
                    BrandFeinlage(c, r);                                          // Bildlage des Brandes
                    Audio.GameSounds.KlangRoll(10);                               // Druckwellenring
                }
                long dz = Simulation.Determinism.Draws - d0;
                KlangRestUrteil(sb, 'A', dz == 0,
                    $"Klangwuerfel (R1): 20 x (Fusstod, Fahrzeugtod, Brand 399, Brandfeinlage, Ring) — Determinism-Wuerfe {dz} (Soll 0), Klaenge {gehoert.Count - ab}");
            }

            // ---- B: R2 — Anwaehlen ----------------------------------------------
            if (!meldungen) KlangRestKeinUrteil(sb, 'B', "Meldungen aus");
            else
            {
                var gesehen = new HashSet<(int, int)>();
                int ab = gehoert.Count, sprecher = 0;
                for (int i = 0; i < _entities.Count; i++)
                {
                    var e = _entities[i];
                    if (e.Dead || e.IsBuilding || e.IsProp || e.Owner != ViewPlayer || e.Chassis < 0) continue;
                    if (!gesehen.Add((e.GameUnitType, e.GameUnitType == 1 ? e.Chassis : 0))) continue;
                    sprecher++;
                    for (int k = 0; k < 200; k++) SpeakSelected(i);
                }
                var alle = Ab(ab);
                int fremd = alle.Count(s => !AnwahlTafel.Contains(s));
                int fahr = alle.Count(s => NurFahrbefehl.Contains(s));
                if (sprecher == 0) KlangRestKeinUrteil(sb, 'B', "keine eigene Einheit");
                else KlangRestUrteil(sb, 'B', fremd == 0 && fahr == 0 && alle.Count == 200 * sprecher,
                    $"Anwaehlen (R2): {sprecher} Gattungen/Fahrwerke x 200, Ankuenfte {alle.Count}, ausserhalb 0x4331E0 {fremd} (Soll 0), "
                  + $"Fahrbefehlszeilen 152/189/... {fahr} (Soll 0), verschiedene {alle.Distinct().Count()}: {string.Join(",", alle.Distinct().OrderBy(x => x).Take(24))}");
            }

            // ---- I: R9 — Nachschubheli (75 Befehl, 72 Abgabe); dazu B2 Flugzeug ---
            {
                int kunde = Eigene(e => e.Mobile && e.Infantry < 0 && e.FuelMax > 0);
                int slot = 0; foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
                if (kunde < 0) KlangRestKeinUrteil(sb, 'I', "keine eigene Einheit mit Sprit");
                else
                {
                    var c = _entities[kunde];
                    probeHeli = new Special                                       // ⚠ Eingriff
                    {
                        Slot = slot, Kind = 13, Name = "Probeheli", TypeName = "Probeheli",
                        Owner = ViewPlayer, Stored = false, HomeSlot = -1,
                        Col = c.Col, Row = c.Row, Pos = FlugPunkt(c),   // bug-696
                        Hp = 10, HpMax = 10, Fuel = 999, FuelMax = 999, Speed = 5,
                        Alt = FlughoeheMax, Sollhoehe = FlughoeheMax, Cargo = SupplyCargoFull,
                    };
                    _special.Add(probeHeli);
                    sb.Append($"  ⚠ EINGRIFF: Probeheli Platz {slot} (Art 13) ueber {LabelOf(c)} gesetzt\n");
                    int ab = gehoert.Count;
                    Emit(Simulation.Commands.CommandRecord.Make(Simulation.Commands.CommandOp.NachschubTanken,
                        (byte)ViewPlayer, (short)probeHeli.Slot, (short)kunde));
                    ring += RingLeeren();
                    int n75 = Zahl(ab, s => s == Audio.GameSounds.NachschubBefehl);
                    int fuel = c.Fuel;
                    c.Fuel = c.FuelMax / 2;                                       // ⚠ Eingriff
                    probeHeli.Customer = kunde; probeHeli.Pos = FlugPunkt(c);   // bug-696
                    probeHeli.Alt = (ElevOf(c.Col, c.Row) + 2) * 15;
                    int ab2 = gehoert.Count, runs = SupplyRuns;
                    UpdateSupply(probeHeli);
                    int n72 = Zahl(ab2, s => s == Audio.GameSounds.NachschubAbgabe);
                    int abgaben = SupplyRuns - runs;
                    KlangRestUrteil(sb, 'I', n75 == 1 && abgaben == 1 && n72 == 1,
                        $"Nachschub (R9): Befehl 14 ueber den Ring -> 75 x{n75} (Soll 1); Abgabe x{abgaben}, 72 x{n72} (Soll 1), Sprit {c.FuelMax / 2} -> {c.Fuel}");
                    _ = fuel;
                    if (meldungen && !Audio.GameSounds.AnwahlstimmeAlt)
                    {
                        int ab3 = gehoert.Count;
                        SpeakSelectedAir(_special.IndexOf(probeHeli));
                        var a3 = Ab(ab3);
                        KlangRestUrteil(sb, 'B', a3.Count == 1 && a3[0] == Audio.GameSounds.AnwahlFlugzeug,
                            $"Anwaehlen Flugzeug (R2, C 0x433318): [{string.Join(",", a3)}] (Soll 183)");
                    }
                    else if (meldungen)
                    {
                        int ab3 = gehoert.Count;
                        SpeakSelectedAir(_special.IndexOf(probeHeli));
                        KlangRestUrteil(sb, 'B', gehoert.Count > ab3, $"Anwaehlen Flugzeug (R2): x{gehoert.Count - ab3} (Soll 1; alt: stumm)");
                    }
                }
            }

            // ---- D: R4 — Hilfestimme -------------------------------------------
            {
                int a1 = Audio.GameSounds.HilfeStimme(520, true), a2 = Audio.GameSounds.HilfeStimme(600, true);
                int b1 = Audio.GameSounds.HilfeStimme(520, false), b2 = Audio.GameSounds.HilfeStimme(600, false);
                bool regel = a1 == 520 && a2 == 1600 && (!meldungen || (b1 == 1520 && b2 == 1600));
                // ein echtes show_text-Fenster (0x443490): die erste Nummer mit Text
                string echt = "kein Fenster geoeffnet";
                bool echtOk = true;
                foreach (int id in new[] { 520, 510, 501, 1, 2, 3 })
                {
                    if (UI.HelpWindow.Suppressed) { echt = "Hilfefenster unterdrueckt"; break; }
                    int ab = gehoert.Count;
                    var w = UI.HelpWindow.ShowOhneRiegel(GetTree().Root, id, 120, 120);
                    if (w == null) continue;
                    var an = Ab(ab).Where(s => s >= 500).ToList();
                    int soll = Audio.GameSounds.HilfeStimme(id, true);
                    echtOk = an.Count == 1 && an[0] == soll;
                    echt = $"show_text {id} -> [{string.Join(",", an)}] (Soll {soll})";
                    UI.HelpWindow.CloseAll(); UI.HelpWindow.CommitClose();
                    break;
                }
                KlangRestUrteil(sb, 'D', regel && echtOk,
                    $"Hilfe (R4): show_text 520 -> {a1} (Soll 520), 600 -> {a2} (Soll 1600); show_text2 520 -> {b1}, 600 -> {b2} (Soll 1520/1600); {echt}");
            }

            // ---- F: R6 — Mechaniker --------------------------------------------
            {
                int ei = Eigene(e => e.HpMax > 3);
                Entity? geb = _entities.FirstOrDefault(b => b.IsBuilding && !b.IsProp && !b.Dead && b.HpMax > 20
                                                          && b.Bauzustand < BauzustandStart);
                if (ei < 0) KlangRestKeinUrteil(sb, 'F', "keine eigene Einheit");
                else
                {
                    var t = _entities[ei];
                    int hp = t.Hp; t.Hp = t.HpMax - 3;                               // ⚠ Eingriff
                    int ab = gehoert.Count, rep = MechanicRepairs;
                    for (int k = 0; k < 5; k++) RepairAt(t.Col, t.Row);
                    int n44 = Zahl(ab, s => s == Audio.GameSounds.MechanikerEinheit);
                    bool zelle = gehoert.Skip(ab).Where(x => x.Slot == 44).All(x => (int)x.C == t.Col && (int)x.R == t.Row);
                    int punkte = MechanicRepairs - rep;
                    string gtext = "kein Gebaeude";
                    int n43 = -1, gp = 0;
                    if (geb != null)
                    {
                        int ghp = geb.Hp; geb.Hp = geb.HpMax - 10;                   // ⚠ Eingriff
                        int abg = gehoert.Count, rg = MechanicRepairs;
                        for (int k = 0; k < 4; k++) RepairAt(geb.Col, geb.Row);
                        n43 = Zahl(abg, s => s == Audio.GameSounds.MechanikerGebaeude);
                        gp = MechanicRepairs - rg;
                        gtext = $"Gebaeude {BuildingTypeName(geb.BType)}: {gp} Reparaturen, 43 x{n43} (Soll {gp})";
                        geb.Hp = ghp;
                    }
                    t.Hp = hp;
                    KlangRestUrteil(sb, 'F', punkte == 3 && n44 == 3 && zelle && (n43 < 0 || (gp > 0 && n43 == gp)),
                        $"Mechaniker (R6): Einheit TP -3, 5 x repair_at -> {punkte} Punkte, 44 x{n44} (Soll 3), an der Zielzelle {(zelle ? "ja" : "NEIN")}; {gtext}");
                }
            }

            // ---- J: R10 — Startklaenge -------------------------------------------
            {
                int ab = gehoert.Count;
                var flz = new Special { Kind = 1, Order = 4, Order2 = 7, StartUhr = 0, Col = 5, Row = 5 };
                var heli = new Special { Kind = 11, Order = 4, Order2 = 7, StartUhr = 0, Col = 6, Row = 6 };
                var folge = new List<string>();
                for (int k = 0; k < 50; k++)
                {
                    int v = gehoert.Count;
                    StartuhrTakt(flz);
                    if (gehoert.Count > v) folge.Add($"Flugzeug Uhr {flz.StartUhr}:{gehoert[^1].Slot}");
                    v = gehoert.Count;
                    StartuhrTakt(heli);
                    if (gehoert.Count > v) folge.Add($"Heli Uhr {heli.StartUhr}:{gehoert[^1].Slot}");
                }
                int n303 = Zahl(ab, s => s == 303), n304 = Zahl(ab, s => s == 304);
                if (StartzustandAlt) KlangRestKeinUrteil(sb, 'J', "--startzustand-alt: die Startuhr laeuft nicht");
                else KlangRestUrteil(sb, 'J', n303 == 1 && n304 == 1 && folge.Count == 2,
                    $"Start (R10, uk 4): 303 x{n303} (Soll 1, Flugzeug Uhr 0), 304 x{n304} (Soll 1, Heli Uhr 39) — {string.Join("; ", folge)}");
            }

            // ---- Q: bug-691 — 308 beim Aufsetzen (C 0x42439F, m_uk 3 -> »air Da«) ----
            // Ein Probeflugzeug (Typ 1) und ein Probeheli (Typ 11) stehen schon ueber einem
            // Flughafen und landen ueber den echten Weg AirHeadHome. Soll: Flugzeug 308 x1 an
            // der Flughafenzelle, Heli 308 x0 (sein Landezweig 0x4241F5 spielt 309 = leer).
            {
                var feld = _entities.Find(x => x.IsBuilding && x.BType == 9 && !x.Dead && x.Owner is >= 0 and <= 7);
                // ⚠ EINGRIFF ohne Flughafen (NET07 hat keinen): ein eigenes Gebaeude als Heimat —
                // AirHeadHome nimmt die Heimat ueber HomeSlot ohne Gebaeudeart; der Hangar, den es
                // dabei anlegt, wird unten wieder zurueckgesetzt.
                bool ersatz = false;
                if (feld == null)
                {
                    feld = _entities.Find(x => x.IsBuilding && !x.Dead && x.Owner == ViewPlayer);
                    ersatz = feld != null;
                    if (ersatz) sb.Append($"  ⚠ EINGRIFF: kein Flughafen — Gebaeude Platz {feld!.Slot} ({BuildingTypeName(feld.BType)}) dient als Landeplatz\n");
                }
                var hangarVorher = feld?.Hangar;
                if (feld == null) KlangRestKeinUrteil(sb, 'Q', "weder Flughafen noch eigenes Gebaeude");
                else
                {
                    int slot = 0; foreach (var s in _special) slot = Mathf.Max(slot, s.Slot + 1);
                    (int N308, bool Gelandet, bool Zelle) Lande(int kind, int sl)
                    {
                        var p = new Special                                            // ⚠ Eingriff, nur lokal
                        {
                            Slot = sl, Kind = kind, Name = "Probelander", TypeName = "Probelander",
                            Owner = feld.Owner, Stored = false, HomeSlot = feld.Slot,
                            Col = feld.Col, Row = feld.Row, Pos = FlugPunkt(feld),   // bug-696
                            Hp = 10, HpMax = 10, Fuel = 999, FuelMax = 999, Speed = 5,
                            Alt = ElevOf(feld.Col, feld.Row) * 15,
                        };
                        int ab = gehoert.Count;
                        bool gelandet = AirHeadHome(p);
                        feld.Hangar?.Remove(sl);                                       // Eingriff zurueck
                        var n = gehoert.Skip(ab).Where(x => x.Slot == Audio.GameSounds.FlugzeugAufsetzen).ToList();
                        bool zelle = n.All(x => Mathf.RoundToInt(x.C) == feld.Col && Mathf.RoundToInt(x.R) == feld.Row);
                        return (n.Count, gelandet, zelle);
                    }
                    var flz = Lande(1, slot);
                    var hel = Lande(11, slot + 1);
                    if (ersatz && hangarVorher == null) feld.Hangar = null;           // Eingriff zurueck
                    KlangRestUrteil(sb, 'Q', flz.Gelandet && hel.Gelandet && flz.N308 == 1 && flz.Zelle && hel.N308 == 0,
                        $"Aufsetzen (bug-691, {(ersatz ? "Ersatz-Landeplatz" : "Flughafen")} Platz {feld.Slot} auf ({feld.Col},{feld.Row})): Flugzeug Typ 1 gelandet "
                      + $"{(flz.Gelandet ? "ja" : "NEIN")}, 308 x{flz.N308} (Soll 1){(flz.Zelle ? "" : " NICHT an der Flughafenzelle")}; "
                      + $"Heli Typ 11 gelandet {(hel.Gelandet ? "ja" : "NEIN")}, 308 x{hel.N308} (Soll 0)");
                }
            }

            // ---- L: R12 — Eingraben/Ausgraben (Ring) --------------------------
            {
                int ei = Eigene(e => e.Mobile && !e.DugIn);
                if (ei < 0) KlangRestKeinUrteil(sb, 'L', "keine eigene, nicht eingegrabene Einheit");
                else
                {
                    var e = _entities[ei];
                    int ab = gehoert.Count;
                    Emit(Simulation.Commands.CommandRecord.Make(Simulation.Commands.CommandOp.Eingraben, (byte)ViewPlayer, (short)ei, 1));
                    ring += RingLeeren();
                    var ein = Ab(ab).Where(s => s is 59 or 60).ToList();
                    bool drin = e.DugIn;
                    int ab2 = gehoert.Count;
                    Emit(Simulation.Commands.CommandRecord.Make(Simulation.Commands.CommandOp.Eingraben, (byte)ViewPlayer, (short)ei, 0));
                    ring += RingLeeren();
                    var aus = Ab(ab2).Where(s => s is 59 or 60).ToList();
                    KlangRestUrteil(sb, 'L', drin && !e.DugIn && string.Join(",", ein) == "59" && string.Join(",", aus) == "60",
                        $"Eingraben (R12, Befehl 26): ein [{string.Join(",", ein)}] (Soll 59), eingegraben {(drin ? "ja" : "NEIN")}, aus [{string.Join(",", aus)}] (Soll 60)");
                }
            }

            // ---- N: R13 — Handsteuerung uebernehmen --------------------------
            if (!meldungen) KlangRestKeinUrteil(sb, 'N', "Meldungen aus");
            else
            {
                int fz = Eigene(e => e.Mobile && e.Infantry < 0 && e.GameUnitType == 0);
                int fv = Eigene(e => e.Mobile && e.Infantry >= 0);
                if (fz < 0) KlangRestKeinUrteil(sb, 'N', "kein eigenes Fahrzeug (Gattung 0)");
                else
                {
                    string Probe(int i, out int n)
                    {
                        _sel.Clear(); _sel.Add(i); _selected = i; SetPrimary();
                        int ab = gehoert.Count;
                        PostHandsteuerungUmschalten();               // uebernehmen (4)
                        ring += RingLeeren();
                        var an = Ab(ab).Where(s => s is 161 or 174 or 181 or 260).ToList();
                        int ab2 = gehoert.Count;
                        PostHandsteuerungUmschalten();               // abgeben (5): still
                        ring += RingLeeren();
                        n = an.Count + Ab(ab2).Count(s => s is 161 or 174 or 181 or 260) * 100;
                        return string.Join(",", an);
                    }
                    string f1 = Probe(fz, out int nf);
                    string f2 = fv >= 0 ? Probe(fv, out int nv) : "(kein Fusssoldat)";
                    int nfv = fv >= 0 ? (f2.Length == 0 ? 0 : f2.Split(',').Length) : 0;
                    int soll = _entities[fz].Field28 > 50 ? 174 : 161;
                    KlangRestUrteil(sb, 'N', nf == 1 && f1 == soll.ToString() && nfv == 0,
                        $"Handsteuerung (R13): Fahrzeug [{f1}] (Soll {soll}, beim Abgeben still), Fusssoldat [{f2}] (Soll leer)");
                    _sel.Clear(); SetPrimary();
                }
            }

            // ---- O: R14 — Waldbrand --------------------------------------------
            {
                var baum = _objDraw.FirstOrDefault(o => o.IstWald);
                if (baum == null) KlangRestKeinUrteil(sb, 'O', "kein Wald auf dieser Karte");
                else
                {
                    int ab = gehoert.Count, w0 = WaldbrandWuerfe;
                    for (int k = 0; k < 8000; k++) WaldbrandKlang(baum);
                    int n300 = Zahl(ab, s => s == Audio.GameSounds.Waldbrand);
                    bool zelle = gehoert.Skip(ab).All(x => (int)x.C == baum.Col && (int)x.R == baum.Row);
                    // Erwartung 100, 3 sigma = 30
                    KlangRestUrteil(sb, 'O', n300 >= 70 && n300 <= 130 && zelle,
                        $"Waldbrand (R14): 8000 Bilder einer Waldzelle (als brenne sie) ({baum.Col},{baum.Row}), Wuerfe {WaldbrandWuerfe - w0}, 300 x{n300} "
                      + $"(Soll 100 +-30, rand%80), an der Zelle {(zelle ? "ja" : "NEIN")}. Nebeltor: Flamme() wie 0x42E706 (nicht hier gemessen)");
                }
            }

            // ---- P: R15 — Truemmerlandung --------------------------------------
            {
                int c = _nav != null ? _nav.Width / 2 : 10, r = _nav != null ? _nav.Height / 2 : 10;
                int ab = gehoert.Count, l0 = TruemmerLandeklaenge;
                int g0 = TruemmerGeworfen;
                for (int k = 0; k < 6; k++) EinTeil(new Entity { Col = c, Row = r }, sorte: 2, streuung: 12);
                int sorte2 = TruemmerGeworfen - g0;
                int g1 = TruemmerGeworfen;
                for (int k = 0; k < 6; k++) EinTeil(new Entity { Col = c, Row = r }, sorte: 1, streuung: 5);
                int sorte1 = TruemmerGeworfen - g1;
                int takte = 0;
                for (; takte < 3000 && _truemmer.Count > 0; takte++) TruemmerTakt(1f / EffektTakteJeSekunde);
                var an = Ab(ab).Where(s => s >= 400 && s <= 405).ToList();
                if (sorte2 == 0) KlangRestKeinUrteil(sb, 'P', "keine Gebaeudesplitter (Folgen 200..204 nicht ausgegeben)");
                else KlangRestUrteil(sb, 'P', an.Count == sorte2 && TruemmerLandeklaenge - l0 == sorte2 && _truemmer.Count == 0,
                    $"Truemmer (R15): {sorte2} Gebaeudesplitter (Folge >= 39) + {sorte1} Brocken geworfen, in {takte} Takten gelandet; "
                  + $"400..405 x{an.Count} (Soll {sorte2}, nur die Splitter): {string.Join(",", an)}");
            }

            // ---- C: R3 — Fusstod (toetet!) -------------------------------------
            if (!meldungen) KlangRestKeinUrteil(sb, 'C', "Meldungen aus");
            else
            {
                var arten = new HashSet<int>();
                var teile = new List<string>();
                int falsch = 0, alt = 0, n = 0, ohneStimme = 0;
                for (int i = 0; i < _entities.Count && n < 8; i++)
                {
                    var v = _entities[i];
                    if (v.Dead || v.IsProp || v.IsBuilding || v.Infantry < 0) continue;
                    if (!arten.Add(v.Chassis)) continue;
                    int ab = gehoert.Count;
                    Kill(i, v, -1, "klang-rest-check");                            // ⚠ Eingriff: Tod durch Treffer
                    var an = Ab(ab);
                    int tod = an.Count(s => TodesTafel.Contains(s));
                    int a77 = an.Count(s => s is 77 or 78 or 79);
                    alt += a77; n++;
                    if (tod != 1) { if (tod == 0) ohneStimme++; else falsch++; }
                    teile.Add($"Art {v.Chassis >> 1}:{string.Join("/", an.Where(s => TodesTafel.Contains(s) || s is 77 or 78 or 79))}");
                }
                if (n == 0) KlangRestKeinUrteil(sb, 'C', "kein Fusssoldat auf dieser Karte");
                else KlangRestUrteil(sb, 'C', falsch == 0 && ohneStimme == 0 && alt == 0,
                    $"Fusstod (R3): {n} Infanteriearten getoetet — {string.Join(" ", teile)}; 0x40B2E0 je Tod x1 (ohne {ohneStimme}, doppelt {falsch}), 77..79 x{alt} (Soll 0)");
            }

            // ---- H: R8 — Depotbau (setzt ein Fahrzeug ein!) --------------------
            {
                int ab = gehoert.Count, bau = 0, versuche = 0;
                string wo = "";
                int geb0 = _entities.Count;
                for (int i = 0; i < _entities.Count && bau == 0; i++)
                {
                    var e = _entities[i];
                    if (e.Dead || e.IsProp || e.IsBuilding || e.Owner != ViewPlayer || e.Infantry >= 0 || !e.Mobile) continue;
                    if (IstTransporter(e)) continue;
                    e.Path = null; e.Orders.Clear();
                    // ⚠ EINGRIFF: steht das Fahrzeug, wo kein Depot passt, wird es (nur
                    // der Zahl nach) auf die erste freie Zelle gesetzt, an der eines passt.
                    var off = BuildOffsetOfOrder(OrderDepot);
                    if (Patterns != null && _nav != null && !CanBuild(Patterns, TypeDepot, e.Col + off.X, e.Row + off.Y, i))
                    {
                        bool gefunden = false;
                        for (int rr = 4; rr < _nav.Height - 4 && !gefunden; rr += 3)
                            for (int cc = 4; cc < _nav.Width - 4 && !gefunden; cc += 3)
                            {
                                if (_nav.OccupantAt(cc, rr) >= 0) continue;
                                if (!CanBuild(Patterns, TypeDepot, cc + off.X, rr + off.Y, i)) continue;
                                _nav.ClearOccupant(e.Col, e.Row, i);
                                e.Col = cc; e.Row = rr; gefunden = true;
                            }
                        if (gefunden) sb.Append($"  ⚠ EINGRIFF: {LabelOf(e)} fuer den Depotbau nach ({e.Col},{e.Row}) gesetzt\n");
                    }
                    e.BuildOrder = OrderDepot; e.BuildTarget = (e.Row << 8) | e.Col;   // ⚠ Eingriff
                    int vorBau = _entities.Count;
                    versuche++;
                    var zelle = (e.Col, e.Row);
                    BuildArrivalTick();
                    if (e.Dead && _entities.Count > vorBau) { bau = 1; wo = $"({zelle.Col},{zelle.Row})"; }
                    else { e.BuildOrder = 0; e.BuildTarget = 0; }
                }
                var an = gehoert.Skip(ab).Where(x => x.Slot == 42).ToList();
                if (bau == 0) KlangRestKeinUrteil(sb, 'H', $"kein eigenes Fahrzeug, an dessen Platz ein Depot passt (Versuche {versuche}, letzte Notiz »{BuildOrderNote}«, Muster {(Patterns != null ? "ja" : "NEIN")})");
                else KlangRestUrteil(sb, 'H', an.Count == 1,
                    $"Depotbau (R8, C 0x408111): Depot gesetzt an der Zelle des Bauers {wo}, 42 x{an.Count} (Soll 1)"
                  + (an.Count == 1 ? $" bei ({(int)an[0].C},{(int)an[0].R})" : ""));
                _ = geb0;
            }

            // ---- G: R7 — Gleisreparatur (zuletzt: verschiebt ein Fahrzeug) ------
            {
                Vector2I? kaputt = null;
                foreach (var rc in _railCells)
                    if (rc.Frame != 255 && !rc.Pylon && !rc.Broken) { RailHit(rc.Col, rc.Row, 999); kaputt = new Vector2I(rc.Col, rc.Row); break; }
                int gleise = _railCells.Count;
                int ei = Eigene(e => e.Mobile && e.Infantry < 0 && e.Move == Simulation.NavGrid.MoveClass.Vehicle);
                RailCell? probeGleis = null;
                if (kaputt == null && ei >= 0)
                {
                    // ⚠ EINGRIFF: diese Karte hat keine Gleise — ein zerschossenes
                    // Probestueck (Bild 101, Linie -99) unter das Fahrzeug legen.
                    probeGleis = new RailCell { Index = -1, Col = _entities[ei].Col, Row = _entities[ei].Row, Frame = 101, Line = -99 };
                    _railCells.Add(probeGleis);
                    kaputt = new Vector2I(probeGleis.Col, probeGleis.Row);
                    sb.Append($"  ⚠ EINGRIFF: Probegleis (zerschossen) bei ({probeGleis.Col},{probeGleis.Row})\n");
                }
                if (kaputt == null || !RailBrokenAt(kaputt.Value.X, kaputt.Value.Y)) KlangRestKeinUrteil(sb, 'G', $"kein Gleis, das sich zerschiessen laesst ({gleise} Gleiszellen)");
                else if (ei < 0) KlangRestKeinUrteil(sb, 'G', "kein eigenes Fahrzeug");
                else
                {
                    var e = _entities[ei];
                    int oc = e.Col, or = e.Row;
                    e.Col = kaputt.Value.X; e.Row = kaputt.Value.Y;                  // ⚠ Eingriff (nur die Zahl)
                    int ab = gehoert.Count, done = RailRepairsDone;
                    FinishRailRepair(ei, e, healed: true);
                    int rep = RailRepairsDone - done;
                    var an = gehoert.Skip(ab).Where(x => x.Slot == Audio.GameSounds.GleisRepariert).ToList();
                    bool zelle = an.All(x => (int)x.C == kaputt.Value.X && (int)x.R == kaputt.Value.Y);
                    e.Col = oc; e.Row = or; e.Path = null; e.RailGoal = null;
                    if (probeGleis != null) { _railCells.Remove(probeGleis); _railTiles = null; }
                    KlangRestUrteil(sb, 'G', rep == 1 && an.Count == 1 && zelle,
                        $"Gleis (R7, C 0x409A04): Stueck ({kaputt.Value.X},{kaputt.Value.Y}) repariert x{rep}, 45 x{an.Count} (Soll {rep}), an der Zelle {(zelle ? "ja" : "NEIN")}");
                }
            }
        }
        catch (System.Exception ex)
        {
            KlangRestUrteil(sb, '!', false, $"AUSNAHME: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            if (probeHeli != null) _special.Remove(probeHeli);
            Audio.SoundBankPlayer.Horcher = vorher;
        }
        sb.Append($"  Ring {ring} Takt(e) geleert\n");
        return sb.ToString();
    }
}
