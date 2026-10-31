namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using AkteEuropaReborn.Simulation.Commands;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;
using Godot;

/// <summary>
/// ⭐⭐ <b>DAS MAGNETBOMBEN-PAKET</b> (30.10.2026, bug-710..bug-713). Lesung
/// <c>berichte/fehlende-funktionen-opus.md</c> §3/§4 und <c>berichte/bau-heli-strahl-seeker.md</c>
/// §3, Bau <c>berichte/bau-magnetbombe.md</c>. Selbst nachgelesen in C UND F (alle acht
/// Routinen Befehl fuer Befehl gleich, nur die Adressen verschoben, mb/cmpfn.py):
///
/// <code>
/// Takt     C 0x4072E1 / F 0x40720A  +0x0F == 0xAD und UKOL != 4  -> Seeker 0x4119D0 / F 0x4117A0
///          C 0x4072FB / F 0x407224  +0x0E == 0x4C                -> Antimagnet 0x411C80 / F 0x411A50
///          (0x40730B/0x40731B Mechaniker und Reparateur = UnitRepairTick)
///          C 0x40733E / F 0x407267  +0x0D == 0x13                -> Naehezuender 0x4123D0 / F 0x4121A0
///          C 0x40736A / F 0x407293  +0x0E == 0xC5                -> Membrandroid 0x4125F0 / F 0x4123C0,
///                                                                   true -> Ende des Einheitentakts
/// Seeker   UKOL 0 oder 2, sonst raus. Ziel +0x40:
///            0xFFFF: nur bei (Uhr+Platz)%100 == 11 Spirale 0x79A008, 3000 Schritte:
///                    imap &lt; 8000, Gattung +0x0A == 0, T[Spieler, Ziel/1000] == 0 -> +0x40 := Ziel
///            sonst:  Ziel UKOL &gt; 45 oder eigenes +0x09 == 0xFF -> +0x40 := 0xFFFF, raus (kein Klang, kein Wurf)
///                    UKOL 0 -> fahre(Zelle des Ziels); UKOL 2 -> nur bei (Uhr+Platz)%100 == 44
///                    (Uhr+Platz)%40 != 0 -> Klang 37, Modus 1 (0x411AB0)
///                    Spielwuerfel 0x4013ED % 400 == 111 -> +0x40 := 0xFFFF (JEDEN Takt)
/// Antimag. (Uhr+Platz)%20 == 9: Spirale, 760 Schritte (0x2F8): imap &lt; 8000, +0x0F == 0xAD, UKOL == 2,
///          feindlich -> UKOL := 0, +0x40 := 0xFFFF; danach EINMAL Klang 47, Modus 6 (0x411DC1)
/// Zuender  Nachbartafel 0x4F6310 (−1,0)(1,0)(0,1)(0,−1)(−1,−1)(−1,1)(1,−1)(1,1), je Zelle 0x4121E0:
///          imap &lt; 8000 und T[...] == 0 -> Sprengung 0x412280, Schluss
/// Sprengung C 0x412280 / F 0x412050: je Nachbarzelle imap 60000..60299 -> Zasah(40020),
///          imap &lt; 14000 und Gattung != 3 -> Zasah(40250); sonst nichts (kein Wald, keine Bruecke).
///          Dann Klang 36 Modus 1 (0x41234F) und Zasah(40250, imap der EIGENEN Zelle).
///          Keine Buendnispruefung: eigene Nachbarn leiden mit. Kein eigenes Bild.
/// Tod      C 0x40B7DA / F 0x40B6CA: ZBRAN == 19 -> 0x412280; +0x0E == 0xC5 -> 0x412480 (0x40B82E)
/// Befehl 28 C 0x4C366E / F 0x4C31E5 (Tafel 0x4C4D54 Eintrag 27) -> 0x412280, keine weitere Pruefung.
///          Einziger Absender: Menuezeile 0x23 »-Jump« (Aufsatz 0x27) @0x448BC3 / F 0x447BB7.
/// Membran  0x4125F0: eigene Zelle Infanteriezelle 10000..13999, die 9 Plaetze 0x7847EC: erster
///          FEINDLICHE Mann -> 0x412480, dann +0x09 := 0xFF (der Satz ist frei), true.
///          0x412480: je Nachbarzelle Gebaeude Zasah(40010), imap &lt; 8000 Zasah(40030),
///          &lt; 14000 Zasah(40100); Klang 36 Modus 1; Bild 0x435950(..., 500); Zasah(40100, eigene Zelle)
/// Laden    C 0x41D210 liest die Einheitentafel ROH (0x41D391, 624000 B); der Nachlauf 0x41F318
///          faesst +0x40 nicht an. 0xAD vergleichen in C und F je GENAU 5 Stellen: der Rufer und
///          der Antimagnetiker sowie die drei Erzeuger 0x4B1840/0x4B34E0/0x4C0EC0 (+0x40 := 0xFFFF).
///          ⇒ +0x40 = 0 aus der Karte bleibt stehen: der Stahlsucher jagt Einheit 0.
/// </code>
///
/// <para>⚠ <b>UNSERE SETZUNGEN</b> (alle hier):</para>
/// <list type="bullet">
/// <item>UKOL: »0« = <c>Ukol == 0</c>, kein Weg, kein Angriffsziel; »2« = <c>Ukol == 0</c> mit Weg und
///   ohne Angriffsziel; »4« = <c>Target &gt;= 0</c> (wie im Gruppenangriff, KiGruppenangriff.cs). Ein
///   Ziel »UKOL &gt; 45« ist bei uns auch ein TOTES Ziel (das Original setzt beim Tod UKOL &gt;= 100).</item>
/// <item><c>fahre</c> ist <see cref="AiWalkTo"/>; die Klemme 0x40B0D4 steht woertlich da (auch ihr
///   Fehler: eine zu grosse Zeile setzt die SPALTE auf Hoehe−2).</item>
/// <item>Feindlich ist <see cref="MineFeindlich"/> (die Tafel 0x87B155 plus unser Standby).</item>
/// <item>Eine untergestellte Einheit (Garage) zuendet, sucht und haelt nicht an — wo ihr Satz im
///   Original steht, ist ungelesen (VERMUTET: nicht auf der Karte).</item>
/// <item>Klang 47 »Modus 6« faellt im Original in den Vorgabezweig 0x4048EC mit uninitialisierter
///   Lage — bei uns ohne Ort.</item>
/// <item>Der Selbsttreffer der Sprengung entfaellt beim TOD (die eigene Zelle traegt dann keine
///   Einheit mehr); was das Original dort im imap findet (Wrack?), ist ungelesen.</item>
/// <item>Gattung der Infanteriezelle: 0x412321 liest fuer imap 10000..13999 Speicher HINTER der
///   Einheitentafel — VERMUTET ≠ 3, also getroffen.</item>
/// <item>Membrandroid: die 9 Plaetze sind bei uns »Fussvolk in derselben Zelle«, in Listenfolge;
///   Bild 500 ist nicht ausgegeben -> »explosion« (Folge 48); +0x09 := 0xFF = tot OHNE Leiche,
///   ohne Todesklang (<see cref="Entity.OhneLeiche"/>).</item>
/// </list>
/// <para>Gegenschalter <c>--magnetbombe-alt</c> (alles aus), <c>--seeker-alt</c>, <c>--membrandroid-alt</c>,
/// <c>--antimagnet-alt</c>; <c>--seeker-ziel0-leer</c> liest +0x40 = 0 aus der Karte als »kein Ziel«.
/// Pruefstand <c>--magnetbombe-check</c> (MagnetbombeCheck.cs).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--magnetbombe-alt</c> — das ganze Paket aus, der Stand bis 30.10.2026.</summary>
    public static bool MagnetbombeAlt;
    /// <summary><c>--seeker-alt</c> — nur der Seeker 0x4119D0 aus (bug-710).</summary>
    public static bool SeekerAlt;
    /// <summary><c>--membrandroid-alt</c> (bug-712).</summary>
    public static bool MembrandroidAlt;
    /// <summary><c>--antimagnet-alt</c> (bug-713).</summary>
    public static bool AntimagnetAlt;
    /// <summary><c>--seeker-ziel0-leer</c> — UNSERE Lesart: +0x40 = 0 aus der Karte heisst »kein Ziel«.</summary>
    public static bool SeekerZiel0Leer;

    /// <summary>Fahrwerk 173 »Stahlsucher« (+0x0F, <c>cmp al,0xad</c> @0x4072E4).</summary>
    public const int StahlsucherFahrwerk = 0xAD;
    /// <summary>Waffe 19 »M-Bombe« (ZBRAN +0x0D, <c>cmp al,0x13</c> @0x407341).</summary>
    public const int MagnetbombeWaffe = 0x13;
    /// <summary>Ausruestung 76 »Antimagnetiker« (+0x0E, <c>cmp al,0x4c</c> @0x4072FB).</summary>
    public const int AntimagnetTeil = 0x4C;
    /// <summary>Waffe 197 »Membranbombe« des Membrandroiden (+0x0E, <c>cmp cl,0xc5</c> @0x40736D).</summary>
    public const int MembranTeil = 0xC5;
    /// <summary>Klang 36 der Sprengung (<c>push 0x24</c> @0x41234D, @0x412544).</summary>
    public const int SprengKlang = 36;
    /// <summary>Klang 47 des Antimagnetikers (<c>push 0x2f</c> @0x411DBF).</summary>
    public const int AntimagnetKlang = 47;

    /// <summary>Die Nachbartafel <c>0x4F6310</c> (Spalte, Zeile) — gelesen, NICHT die
    /// Sterbetafel 0x4F5AF0.</summary>
    private static readonly (int Dx, int Dy)[] MagnetNachbarn =
        { (-1, 0), (1, 0), (0, 1), (0, -1), (-1, -1), (-1, 1), (1, -1), (1, 1) };

    // ---- Zaehler fuer Zeile und Pruefstand ------------------------------------
    public int MbSuchen, MbGefunden, MbFahrten, MbKlang37, MbVergessenWurf, MbVergessenTot;
    public int MbZuendungen, MbSprengungen, MbBefehl28, MbTodSprengungen;
    public int MbAntimagnetHalte, MbAntimagnetKlaenge, MbMembranZuendungen, MbMembranTod;

    private bool MbAn => !MagnetbombeAlt;

    /// <summary>Die Einheit mit dem Satzplatz <paramref name="platz"/> (Gebaeude und Requisiten
    /// ausgenommen), die lebende zuerst.</summary>
    private int MbPlatzIndex(int platz)
    {
        int tot = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var t = _entities[i];
            if (t.IsBuilding || t.IsProp || t.Slot != platz) continue;
            if (!t.Dead) return i;
            if (tot < 0) tot = i;
        }
        return tot;
    }

    /// <summary>imap &lt; 8000 an (c, r): das Fahrzeug der Belegung, sonst −1.</summary>
    private int MbFahrzeugAuf(int c, int r)
    {
        if (_nav == null || !_nav.InBounds(c, r)) return -1;
        int o = _nav.OccupantAt(c, r);
        if (o < 0 || o >= _entities.Count) return -1;
        var t = _entities[o];
        if (t.Dead || t.IsBuilding || t.IsProp || t.Infantry >= 0 || Untergestellt(t)) return -1;
        return o;
    }

    private bool MbFussvolkAuf(int c, int r)
    {
        foreach (var t in _entities)
            if (!t.Dead && !t.IsProp && !t.IsBuilding && t.Infantry >= 0 && t.Col == c && t.Row == r
                && !Untergestellt(t)) return true;
        return false;
    }

    // ==== der Einheitentakt (aus SimTick) ========================================

    /// <summary>Vor <c>UnitRepairTick</c>: @0x4072E1 und @0x4072FB.</summary>
    private void MagnetVorTakt(int i, Entity e)
    {
        if (!MbAn || Untergestellt(e)) return;
        if (!SeekerAlt && e.Comp0F == StahlsucherFahrwerk && e.Target < 0) StahlsucherTakt(i, e);   // UKOL != 4
        if (!AntimagnetAlt && e.Part == AntimagnetTeil && e.Infantry < 0) AntimagnetTakt(i, e);
    }

    /// <summary>Nach <c>UnitRepairTick</c>: @0x40733E und @0x40736A. true = die Einheit ist
    /// fort (0x40737D springt ans Ende des Takts).</summary>
    private bool MagnetNachTakt(int i, Entity e)
    {
        if (!MbAn || Untergestellt(e) || e.Dead) return e.Dead;
        if (e.Comp0D == MagnetbombeWaffe && e.Infantry < 0) Naehezuender(i, e);
        if (e.Dead) return true;
        if (!MembrandroidAlt && e.Part == MembranTeil && e.Infantry >= 0) return MembranTakt(i, e);
        return false;
    }

    // ---- bug-710: der Seeker 0x4119D0 ------------------------------------------

    private void StahlsucherTakt(int i, Entity e)
    {
        if (e.Ukol != 0) return;                                   // UKOL 0 oder 2 (0x411A05)
        bool ukol2 = e.Path != null;
        int uhr = _taktNr + e.Slot;
        if (e.SuchZiel == 0xFFFF)
        {
            if (uhr % 100 != 11) return;                           // @0x411AFE
            MbSuchen++;
            for (int k = 0; k < 3000; k++)                         // @0x411BC9 cmp si,0xbb8
            {
                var (dx, dy) = Simulation.NavGrid.RingEintrag(k);
                int ti = MbFahrzeugAuf(e.Col + dx, e.Row + dy);    // 0x4018B1, imap < 8000
                if (ti < 0) continue;
                var t = _entities[ti];
                if (t.GameUnitType != 0) continue;                 // @0x411B96 Gattung 0
                if (!MineFeindlich(e.Owner, t.Owner)) continue;    // @0x411BBC 0x87B155
                e.SuchZiel = t.Slot;                               // @0x411BD9
                MbGefunden++;
                if (_mbProbe == i) _mbProbeFund.Add((_taktNr, t.Slot));
                return;
            }
            return;
        }
        int zi = MbPlatzIndex(e.SuchZiel);
        var z = zi >= 0 ? _entities[zi] : null;
        if (z == null || z.Dead || z.Ukol > 45)                    // @0x411A3E / 0x411A4D
        {
            e.SuchZiel = 0xFFFF;                                   // @0x411ACD, dann raus
            MbVergessenTot++;
            return;
        }
        if (!ukol2 || uhr % 100 == 44) StahlsucherFahre(i, z.Col, z.Row);    // @0x411A52..0x411A8C
        if (uhr % 40 != 0)                                         // @0x411AA5
        {
            Audio.GameSounds.PlayAt(Audio.GameSounds.TargetFound, e.Col, e.Row);   // 37, Modus 1
            MbKlang37++;
        }
        if (Simulation.Determinism.Roll(400) == 111)               // @0x411AB8..0x411AC7
        {
            e.SuchZiel = 0xFFFF;
            MbVergessenWurf++;
        }
    }

    /// <summary><c>fahre</c> 0x4021A8 → 0x40B070 mit der Klemme 0x40B0BB..0x40B0F7, woertlich.</summary>
    private void StahlsucherFahre(int i, int x, int y)
    {
        if (_nav == null) return;
        if (x < 1) x = 1;
        if (y < 1) y = 1;
        if (_nav.Width - 2 < x) x = _nav.Width - 2;
        if (_nav.Height - 2 < y) x = _nav.Height - 2;              // ⚠ so im Original (@0x40B0F7: ebx = Spalte)
        if (AiWalkTo(i, new Vector2I(x, y))) MbFahrten++;
    }

    // ---- bug-713: der Antimagnetiker 0x411C80 -----------------------------------

    private void AntimagnetTakt(int i, Entity e)
    {
        if ((_taktNr + e.Slot) % 20 != 9) return;                  // @0x411CA0
        bool gehalten = false;
        for (int k = 0; k < 0x2F8; k++)                            // @0x411DA3
        {
            var (dx, dy) = Simulation.NavGrid.RingEintrag(k);
            int ti = MbFahrzeugAuf(e.Col + dx, e.Row + dy);
            if (ti < 0) continue;
            var t = _entities[ti];
            if (t.Comp0F != StahlsucherFahrwerk) continue;         // @0x411D59
            if (!(t.Ukol == 0 && t.Path != null && t.Target < 0)) continue;   // UKOL == 2 @0x411D64
            if (!MineFeindlich(e.Owner, t.Owner)) continue;        // @0x411D81
            AiAnhalten(ti, t);                                     // UKOL := 0 @0x411D8C
            t.SuchZiel = 0xFFFF;                                   // @0x411D98
            gehalten = true;
            MbAntimagnetHalte++;
        }
        if (gehalten)
        {
            Audio.GameSounds.Play(AntimagnetKlang);                // 47, Modus 6 — ohne Ort (Kopf)
            MbAntimagnetKlaenge++;
        }
    }

    // ---- bug-711: Naehezuender, Sprengung, Tod, Befehl 28 -----------------------

    private void Naehezuender(int i, Entity e)
    {
        foreach (var (dx, dy) in MagnetNachbarn)
        {
            int ti = MbFahrzeugAuf(e.Col + dx, e.Row + dy);        // 0x4121E0: imap < 8000
            if (ti < 0 || !MineFeindlich(e.Owner, _entities[ti].Owner)) continue;
            MbZuendungen++;
            MagnetSprengung(i, e, $"MAGNETBOMBE {LabelOf(e)} (Platz {e.Slot}) — Naehezuender 0x4123D0");
            return;
        }
    }

    /// <summary><b>0x412280</b> — siehe Kopf.</summary>
    private void MagnetSprengung(int i, Entity e, string grund)
    {
        MbSprengungen++;
        int c0 = e.Col, r0 = e.Row;
        foreach (var (dx, dy) in MagnetNachbarn)
        {
            int c = c0 + dx, r = r0 + dy;
            if (_nav == null || !_nav.InBounds(c, r)) continue;
            int fz = MbFahrzeugAuf(c, r);
            if (fz < 0 && !MbFussvolkAuf(c, r) && GebaeudeAufZelle(c, r) >= 0)
            { _mbLog?.Add((c, r, 20, grund)); SkripttrefferZelle(c, r, 20, grund); }   // 60000..60299 -> 40020
            else if (fz >= 0 ? _entities[fz].GameUnitType != 3 : MbFussvolkAuf(c, r))
            { _mbLog?.Add((c, r, 250, grund)); SkripttrefferZelle(c, r, 250, grund); } // < 14000, Gattung != 3 -> 40250
        }
        Audio.GameSounds.PlayAt(SprengKlang, c0, r0);              // 36, Modus 1 @0x41234F
        if (!e.Dead) { _mbLog?.Add((c0, r0, 250, grund + " (Selbsttreffer)")); SkripttrefferZelle(c0, r0, 250, grund + " (Selbsttreffer)"); }   // @0x412376
    }

    /// <summary>Aus <c>Kill</c>, nach Druckwelle und Gaswolke: @0x40B7DA.</summary>
    private void TodDerMagnetbombe(int vi, Entity v)
    {
        if (!MbAn || v.Comp0D != MagnetbombeWaffe || v.Infantry >= 0) return;
        MbTodSprengungen++;
        MagnetSprengung(vi, v, $"MAGNETBOMBE {LabelOf(v)} (Platz {v.Slot}) — Tod 0x40B7DA");
    }

    /// <summary>Menuezeile 0x23 »-Jump« (@0x448BC3): Befehl 28 mit der gewaehlten Einheit.</summary>
    public string PostMagnetSprengung()
    {
        int idx = MenueEinheit();
        if (idx < 0) return "keine eigene Einheit gewaehlt";
        Emit(CommandRecord.Make(CommandOp.MagnetSprengung, (byte)ViewPlayer, (short)idx));
        return $"{LabelOf(_entities[idx])} zuendet";
    }

    /// <summary>Befehl 28, C 0x4C366E / F 0x4C31E5: 0x412280 ohne weitere Pruefung.</summary>
    private bool ApplyMagnetSprengung(in CommandRecord c)
    {
        if (MagnetbombeAlt) return false;
        var e = _entities[c.P1];
        if (e.Dead || e.IsBuilding || e.IsProp) return false;
        MbBefehl28++;
        MagnetSprengung(c.P1, e, $"MAGNETBOMBE {LabelOf(e)} (Platz {e.Slot}) — Befehl 28");
        return true;
    }

    // ---- bug-712: der Membrandroid 0x4125F0 / 0x412480 -------------------------

    private bool MembranTakt(int i, Entity e)
    {
        for (int k = 0; k < _entities.Count; k++)
        {
            var t = _entities[k];
            if (k == i || t.Dead || t.IsProp || t.IsBuilding || t.Infantry < 0) continue;
            if (t.Col != e.Col || t.Row != e.Row || Untergestellt(t)) continue;
            if (!MineFeindlich(e.Owner, t.Owner)) continue;         // @0x412687
            MbMembranZuendungen++;
            MembranSprengung(i, e, $"MEMBRANDROID {LabelOf(e)} (Platz {e.Slot}) — 0x4125F0");
            MembranEntfernen(i, e);                                 // @0x4126B3 +0x09 := 0xFF, auch nach dem eigenen Tod
            return true;
        }
        return false;
    }

    private void MembranSprengung(int i, Entity e, string grund)
    {
        int c0 = e.Col, r0 = e.Row;
        foreach (var (dx, dy) in MagnetNachbarn)
        {
            int c = c0 + dx, r = r0 + dy;
            if (_nav == null || !_nav.InBounds(c, r)) continue;
            int fz = MbFahrzeugAuf(c, r);
            bool fuss = MbFussvolkAuf(c, r);
            int a = fz < 0 && !fuss && GebaeudeAufZelle(c, r) >= 0 ? 10      // 40010
                  : fz >= 0 ? 30                                              // 40030
                  : fuss ? 100 : 0;                                           // 40100
            if (a == 0) continue;
            _mbLog?.Add((c, r, a, grund));
            SkripttrefferZelle(c, r, a, grund);
        }
        Audio.GameSounds.PlayAt(SprengKlang, c0, r0);               // @0x412546
        _effects.Add(new Effect { Pos = e.Pos, Kind = "explosion", FrameTime = 0.05f });   // Bild 500 (Kopf)
        _mbLog?.Add((c0, r0, 100, grund + " (eigene Zelle)"));
        SkripttrefferZelle(c0, r0, 100, grund + " (eigene Zelle)"); // @0x412593
    }

    /// <summary>Aus <c>Kill</c>, Fussvolkzweig: @0x40B7F0 → 0x412480.</summary>
    private void TodDesMembrandroiden(int vi, Entity v)
    {
        if (!MbAn || MembrandroidAlt || v.Part != MembranTeil || v.Infantry < 0) return;
        MbMembranTod++;
        MembranSprengung(vi, v, $"MEMBRANDROID {LabelOf(v)} (Platz {v.Slot}) — Tod 0x40B7F0");
    }

    /// <summary>+0x09 := 0xFF — der Satz ist frei: kein Todesweg, kein Klang, keine Leiche.</summary>
    private void MembranEntfernen(int i, Entity e)
    {
        e.OhneLeiche = true;
        if (e.Dead) return;                                     // schon ueber den Todesweg (Zasah 40100 auf sich)
        _sel.Remove(i);
        if (_selected == i) _selected = -1;
        _nav?.ClearOccupant(e.Col, e.Row, i);
        e.Hp = 0; e.Dead = true; e.OhneLeiche = true; e.DeadTime = 0;
        e.Path = null; e.Target = -1; e.Orders.Clear(); e.Reserved = null;
        for (int k = 0; k < _entities.Count; k++)
        {
            if (_entities[k].Target == i) _entities[k].Target = -1;
            if (_entities[k].Schussziel == i) _entities[k].Schussziel = -1;
        }
    }

    // ---- Spielstand ------------------------------------------------------------

    private static void MagnetEinheitSchreiben(Core.SaveGame.Writer w, Entity e)
    {
        if (MagnetbombeAlt) return;
        if (e.Comp0F == StahlsucherFahrwerk && e.SuchZiel != 0xFFFF) w.Raw("suchziel", e.SuchZiel.ToString());
        if (e.OhneLeiche) w.Bool("ohneleiche", true);
    }

    private static void MagnetEinheitLesen(GDict d, Entity e)
    {
        if (MagnetbombeAlt) return;
        e.SuchZiel = d.TryGetValue("suchziel", out var s) && s.VariantType != Variant.Type.Nil ? s.AsInt32() : 0xFFFF;
        e.OhneLeiche = d.TryGetValue("ohneleiche", out var o) && o.VariantType != Variant.Type.Nil && o.AsBool();
    }

    /// <summary>Ein Teil fuer <c>SpielstandFingerabdruck</c>.</summary>
    public string MagnetFingerabdruck()
    {
        int n = 0; long p = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead || e.Comp0F != StahlsucherFahrwerk) continue;
            n++; p += (e.Slot + 1) * 7L + (e.SuchZiel + 1) * 13L;
        }
        return $"Stahlsucher {n} (Suchziele, Pruefzahl {p})";
    }

    /// <summary>Fuer <c>SpielstandProbeVorbereiten</c>: einem Stahlsucher ein Suchziel geben,
    /// das kein Kartenwert ist. ⚠ EINGRIFF.</summary>
    public string SpielstandProbeMagnet()
    {
        foreach (var e in _entities)
            if (!e.Dead && !e.IsProp && e.Comp0F == StahlsucherFahrwerk)
            { e.SuchZiel = 4242; return $"Stahlsucher Platz {e.Slot}: Suchziel 4242"; }
        return "Stahlsucher: keiner auf der Karte";
    }

    /// <summary>Die Zeile fuer den Lauf: was das Paket in diesem Lauf getan hat.</summary>
    public string MagnetZeile()
        => $"magnetbombe: Suchen {MbSuchen}, gefunden {MbGefunden}, Fahrten {MbFahrten}, Klang37 {MbKlang37}, "
         + $"vergessen Wurf {MbVergessenWurf} / Ziel weg {MbVergessenTot}, Zuendungen {MbZuendungen}, Sprengungen {MbSprengungen} "
         + $"(Tod {MbTodSprengungen}, Befehl28 {MbBefehl28}), Antimagnet {MbAntimagnetHalte}/{MbAntimagnetKlaenge}, "
         + $"Membran {MbMembranZuendungen}/{MbMembranTod}"
         + (MagnetbombeAlt ? " — --magnetbombe-alt" : "");

    // Pruefstand: Fundliste der Probe (MagnetbombeCheck.cs)
    private int _mbProbe = -1;
    private List<(int C, int R, int Angriff, string Grund)>? _mbLog;
    private readonly List<(int Takt, int Ziel)> _mbProbeFund = new();
}
