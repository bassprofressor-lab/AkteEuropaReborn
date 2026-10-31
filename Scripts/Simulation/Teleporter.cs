namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using AkteEuropaReborn.Simulation.Commands;
using GDict = Godot.Collections.Dictionary<string, Godot.Variant>;
using Godot;

/// <summary>
/// ⭐⭐ <b>DER TELEPORTER</b> (30.10.2026, bug-719). Lesung
/// <c>berichte/fehlende-funktionen-opus.md</c> §5, Bau <c>berichte/bau-teleporter.md</c>.
/// Selbst nachgelesen in C UND F (Befehl fuer Befehl gleich, nur die Adressen verschoben;
/// cmpfn.py: 0x43A420/0x439590 250 Befehle 0 Abweichungen, 0x43A2F0/0x439460 70/0,
/// 0x4319DA/0x430B18 75/0, 0x4377F7/0x436957 10/0, 0x40B7DA/0x40B6CA 30/0; der Aufsteller
/// 0x4B1DA7/0x4B16E3 30 Befehle, 7 Abweichungen = nur Registerwahl si/di, edi/esi):
///
/// <code>
/// Entwurf  65 »Teleporter«: Waffe 65 (+0x17 == 0x41), Fahrwerk 165 S.Ketten. Teil 65 -> +0x0E,
///          Aufsatzbild part[65]+0x0D = 0x2C -> +0x0C.
/// Bau      C 0x4B18CA / F 0x4B11FC: Entwurfswaffe == 0x41 -> Zwillingsmarke; der Aufsteller
///          0x4B1840 prueft dann ZWEI Ausgangsplaetze (0x878E66). C 0x4B1DA7 / F 0x4B16E3:
///          Ablage 0x50459C (F 0x5035DC) == 0xFFFF -> Ablage := ich, 0x4B1840(dieselben drei
///          Argumente) baut den Zwilling, ich.+0x40 := Zwilling, Ablage := 0xFFFF;
///          sonst (ich BIN der Zwilling) ich.+0x40 := Ablage. Bezahlt wird EINMAL (Befehl 503,
///          das Basisfenster 0x44A692 verlangt zwei freie Depotplaetze, »Es gibt keinen Platz
///          mehr im Depot.«). Nur 0x4B1840 kennt die Marke: der Laden VERWIRFT Entwurfswaffe
///          0x41 (C 0x4C0F35 / F 0x4C09F5, mit 0x47 und 0x4E), die anderen Erzeuger pruefen nicht.
/// Menue    Aufsatz 0x2C -> Zeile 0x26 (»-Dig up Bunker«). Knopf C 0x448BF2 / F 0x447BE6:
///          dword[0x502ACC] := 9, Menue zu.
/// Zeiger   C 0x4319DA / F 0x430B18, E = gewaehlte Einheit [0x4FA0C8], (x,y) = Mauszelle:
///            E.+0x32 != 0                    -> 0x19 (laedt nach)
///            dx²+dy² > 26                    -> 0x13 (zu weit)
///            imap &lt; 8000: +0x0A == 0, != E, != E.+0x40, +0x09 == 0 -> 0x11, sonst 0x12
///            imap 8000..13999                -> 0x11
///            sonst                           -> 0x12
///          KEINE Besitzerpruefung — ein fremdes Fahrzeug, ein fremder Trupp ist ein Ziel.
/// Klick    Zeigerart 0x11, C 0x4377F7 / F 0x436957: Befehl 23 (E, x, y). Der Modus BLEIBT.
/// Bef. 23  C 0x4C342A / F 0x4C2FA4 -> 0x43A420 / F 0x439590 (T, x, y), ohne Pruefung:
///            Bild 0x60 an (x,y); Klang 56 Modus 1 an T; Klang 57 Modus 2 an (x,y);
///            T.+0x32 := T.+0x3D (nachladen)
///            imap(x,y) &lt; 8000 (Fahrzeug V):
///              0x43A2F0(Zwilling, 0): Spirale 0x79A008 ab der Zwillingszelle, bis 20000
///              Schritte, erste Zelle mit imap == 0xFFFE; keine -> Schluss
///              faehrt V (+0x04 != 0xFF und KOLIK &gt; 0): alte Zelle frei, Lage := Zielzelle
///              des Schritts; die Zelle frei; Zielzelle := V; KOLIK := 0, +0x04 := 0xFF,
///              Geduld +0x1C := 0 (UKOL bleibt!); Bild 0x60 und Klang 58 Modus 2 dort
///            sonst (Trupp): freie Zelle wie oben (keine -> Schluss), Bild 0x60 dort, dann
///              je Mann der 9 Plaetze 0x7847EC: vom Platz nehmen (0x40175D), an die Zelle
///              (Trupp dort -> anschliessen 0x40107D, sonst neuer Trupp 0x401267), KOLIK 0,
///              +0x04 0xFF, +0x1C 0; Trupp dort nicht mehr aufnahmefaehig (0x402509: neun
///              Mann) -> neue freie Zelle, Bild 0x60; findet sie keine: »Error while
///              teleporting«. KEIN Klang 58 fuer den Trupp.
/// Tod      C 0x40B7FF / F 0x40B6EF: +0x0E == 0x41 -> eigene +0x08 := 0; lebt der Zwilling
///          (+0x08 != 0), stirbt er durch denselben Todesweg (jmp 0x40B3CC).
/// </code>
///
/// <para>⚠ <b>UNSERE SETZUNGEN</b> (alle hier):</para>
/// <list type="bullet">
/// <item>Der Zwilling ist ein eigenes Feld <see cref="Entity.Zwilling"/> (Satzplatz), nicht das
///   geteilte Satzwort +0x40.</item>
/// <item>Zwilling an <see cref="NeubauInListe"/> — dem Aufsteller 0x4B1840 bei uns (Fabrik,
///   KI-Fertigung, Depotzeile). <c>SendOutOfDepot</c> (nur noch hinter alten Schaltern) baut
///   KEINEN Zwilling. Die Zwei-Plaetze-Probe des Basisfensters ist nicht nachgebaut.</item>
/// <item>imap &lt; 8000 ist bei uns ein lebendes, nicht untergestelltes Fahrzeug auf der Zelle;
///   »Trupp« ist das Fussvolk in derselben Zelle (wie beim Membrandroiden). imap 8000..9999 (im
///   Original ohne Belegung) zaehlt nicht. Steht auf der Zelle weder Fahrzeug noch Trupp,
///   tut der Behandler nach Bild, Klaengen und Nachladen nichts (das Original laese Unsinn).</item>
/// <item>Frei heisst <c>imap == 0xFFFE</c>: <see cref="ImapWortFrei"/>, ohne Grundriss
///   (bug-706). Ausserhalb der Karte wird nicht gelesen (das Original prueft die Grenze nicht).</item>
/// <item>Hat ein Teleporter keinen Zwilling (keiner liegt auf den 54 Karten), tut er nichts
///   (das Original naehme Einheit 0).</item>
/// <item>Faehrt das Fahrzeug, plant es ab der neuen Zelle EINEN neuen Weg zu seinem Ziel (der
///   Auftrag bleibt im Original stehen; wie es von dort weiterfaehrt, ist nicht gelesen).</item>
/// <item>Der Mitsterbende geht durch unseren ganzen <c>Kill</c>.</item>
/// <item>Bild 0x60 = ANIM-Folge 96 »lichtblitz« (wie der Raumfrachter, 0x4C158D).</item>
/// </list>
/// <para>Gegenschalter <c>--teleporter-aus</c> (kein Zwilling, Menue »noch nicht gebaut«, Befehl 23
/// verworfen, kein Mitsterben). Pruefstand <c>--teleport-check</c> (K29).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--teleporter-aus</c> — der Stand bis 30.10.2026.</summary>
    public static bool TeleporterAus;

    /// <summary>Teil 65 »Teleporter« (+0x0E, <c>cmp eax,0x41</c> @0x40B7EB) = Entwurfswaffe 0x41.</summary>
    public const int TeleporterTeil = 0x41;
    /// <summary>Menuezeile 0x26 (Aufsatz 0x2C, Tafel c1).</summary>
    public const int CodeTeleport = 0x26;
    /// <summary>Unser Setzmodus fuer <c>dword[0x502ACC] = 9</c>.</summary>
    public const int OrderTeleport = 12;
    /// <summary>Die Zeigerzustaende von 0x4319DA.</summary>
    public const int TeleZustandGut = 0x11, TeleZustandSchlecht = 0x12, TeleZustandWeit = 0x13, TeleZustandLaedt = 0x19;
    /// <summary>Klang 56 am Teleporter, 57 am Ziel, 58 an der Ankunft (<c>push 0x38/0x39/0x3a</c>).</summary>
    public const int TeleKlangStart = 56, TeleKlangZiel = 57, TeleKlangAnkunft = 58;
    /// <summary>Bild 0x60 = ANIM-Folge 96.</summary>
    private const string TeleBlitz = "lichtblitz";

    public int TeleZwillinge, TeleBefehle, TeleFahrzeuge, TeleTrupps, TeleMaenner, TeleKeinPlatz, TeleOhneZiel, TeleMitgestorben, TeleFehler;

    /// <summary>Ist das ein Teleporter (+0x0E == 0x41)?</summary>
    private static bool IstTeleporter(Entity e) => e.Part == TeleporterTeil && e.Infantry < 0;

    /// <summary>Der lebende Satz mit diesem Platz (die lebende zuerst), −1 = keiner.</summary>
    private int TelePlatzIndex(int platz) => platz < 0 ? -1 : MbPlatzIndex(platz);

    // ==== der Bau: 0x4B1840 @0x4B1DA7 ===========================================

    private bool _zwillingImBau;   // die Ablage 0x50459C (F 0x5035DC) != 0xFFFF

    /// <summary>Aus <see cref="NeubauInListe"/>: ist die Entwurfswaffe 0x41, baut derselbe
    /// Aufsteller sofort den Zwilling, und beide merken sich einander.</summary>
    private void TeleporterZwillingBauen(Entity werk, int designIdx, Entity u, Design d)
    {
        if (TeleporterAus || d.Weapon != TeleporterTeil || _zwillingImBau) return;
        _zwillingImBau = true;                                     // Ablage := ich
        Entity? z;
        try { z = NeubauInListe(werk, designIdx, merker: false); } // 0x4B1840(dieselben Argumente)
        finally { _zwillingImBau = false; }                        // Ablage := 0xFFFF
        if (z == null) return;
        u.Zwilling = z.Slot;                                       // ich.+0x40 := Zwilling
        z.Zwilling = u.Slot;                                       // Zwilling.+0x40 := Ablage
        TeleZwillinge++;
        GD.Print($"teleporter: Zwilling gebaut — Platz {u.Slot} <-> {z.Slot} (Spieler {u.Owner}, 0x4B1DA7), Takt {_taktNr} = {_taktNr / (float)SimHz:0.0} s");
    }

    /// <summary><c>--teleporter-depot-alt</c> — bug-724 aus: der Teleporter braucht wie jeder
    /// Entwurf nur EINEN freien Depotplatz.</summary>
    public static bool TeleporterDepotAlt;
    /// <summary><c>--zwilling-aussenden-alt</c> — bug-725 aus: <c>SendOutOfDepot</c> baut keinen Zwilling.</summary>
    public static bool ZwillingAussendenAlt;
    public int TeleDepotAbsagen, TeleZwillingFehlt;

    /// <summary>⭐ bug-724 — das Basisfenster C 0x44A692 / F 0x449685: Entwurfswaffe 0x41 ->
    /// <c>word[0x878E66 + 2·(8·Platz − 1)] == 0xFFFF</c>, also auch der VORLETZTE Depotplatz
    /// muss frei sein — zwei Plaetze statt einem, sonst »Es gibt keinen Platz mehr im Depot.«
    /// (0x44A6CD). Wie viele Plaetze der Entwurf braucht.</summary>
    private int TeleporterDepotBedarf(Design d)
    {
        if (TeleporterAus || TeleporterDepotAlt || d.Weapon != TeleporterTeil) return 1;
        return 2;
    }

    /// <summary>⭐ bug-725 — <c>SendOutOfDepot</c> ist bei uns die zweite Haelfte des Aufstellers
    /// 0x4B1840 (Fertigstellung -> Depotzeile -> hier der Satz; die KI nimmt diesen Weg, wenn ihr
    /// Gebaeude keine Tuer hat oder <c>--bauzeit-alt</c> steht). Das Original baut den Zwilling
    /// IM Aufsteller, also auch hier: eine zweite Zeile desselben Entwurfs gleich hinterher.
    /// ⚠ UNSERE SETZUNG: geht sie nicht hinaus (Tuer belegt), legt <see cref="NeubauInListe"/>
    /// den Zwilling in die Garage, die ihn spaeter auslaesst; ohne Tuer entsteht keiner (gezaehlt).
    /// Die uebrigen Erzeuger bauen im Original KEINEN: der Zwillingsteil steht nur in 0x4B1840
    /// (Ablage 0x50459C hat 3 Leser, alle dort; F 0x5035DC ebenso), und 0x4B1840 rufen nur
    /// sich selbst, Befehl 503 (Tafel 0x4C4DCC Eintrag 2, @0x4C3798) und die KI-Fertigung
    /// <c>build_in_base</c> 0x4BB1E0 (@0x4BB31B). <c>space_in</c> geht ueber 0x4B34E0, der Laden
    /// ueber 0x4C0EC0 (und verwirft 0x41), die Mitnahme ueber <c>place_carry</c> 0x43B190.</summary>
    private void TeleporterZwillingAussenden(Entity werk, int designIdx, Entity u, Design d, bool nurTuer)
    {
        if (TeleporterAus || ZwillingAussendenAlt || d.Weapon != TeleporterTeil || _zwillingImBau) return;
        _zwillingImBau = true;
        Entity? z = null;
        try
        {
            int n = _entities.Count;
            werk.Depot.Insert(0, designIdx);
            if (SendOutOfDepot(werk, 0, nurTuer) && _entities.Count > n) z = _entities[^1];
            else
            {
                if (werk.Depot.Count > 0 && werk.Depot[0] == designIdx) werk.Depot.RemoveAt(0);
                if (werk.DoorCells.Count > 0) z = NeubauInListe(werk, designIdx, merker: false);
            }
        }
        finally { _zwillingImBau = false; }
        if (z == null) { TeleZwillingFehlt++; GD.Print($"teleporter: KEIN Zwilling fuer Platz {u.Slot} — kein Ausgang"); return; }
        u.Zwilling = z.Slot;
        z.Zwilling = u.Slot;
        TeleZwillinge++;
        GD.Print($"teleporter: Zwilling ausgesandt — Platz {u.Slot} <-> {z.Slot} (Spieler {u.Owner}, SendOutOfDepot), Takt {_taktNr} = {_taktNr / (float)SimHz:0.0} s");
    }

    // ==== der Tod: 0x40B7FF ==========================================================

    private void TodDesTeleporters(int vi, Entity v)
    {
        if (TeleporterAus || !IstTeleporter(v) || v.Zwilling < 0) return;
        int zi = TelePlatzIndex(v.Zwilling);
        if (zi < 0 || zi == vi) return;
        var z = _entities[zi];
        if (z.Dead) return;                                        // +0x08 == 0
        TeleMitgestorben++;
        z.Hp = 0;
        Kill(zi, z, -1, $"TELEPORTER {LabelOf(z)} (Platz {z.Slot}) — der Zwilling ist tot (0x40B7FF)");
    }

    // ==== das Menue und der Zeiger ===================================================

    /// <summary>Menuezeile 0x26 — @0x448BF2: Befehlsart 9.</summary>
    public string TeleporterBeginnen()
    {
        int idx = MenueEinheit();
        if (idx < 0) return "keine Einheit gewaehlt.";
        PlacementMode = OrderTeleport;
        PlacementUnit = idx;
        BuildOrderNote = "Teleportieren: Ziel anklicken (Esc bricht ab)";
        return "";
    }

    /// <summary>Was auf der Zelle zum Teleportieren steht: ein Fahrzeug (Index) oder ein
    /// Trupp (−2), sonst −1.</summary>
    private int TeleZielAuf(int c, int r)
    {
        int v = MbFahrzeugAuf(c, r);
        if (v >= 0) return v;
        return MbFussvolkAuf(c, r) ? -2 : -1;
    }

    /// <summary>Der Zeigerzustand 0x4319DA fuer den Teleporter <paramref name="t"/> an (c, r).</summary>
    public int TeleZeigerZustand(int t, int c, int r)
    {
        if (t < 0 || t >= _entities.Count) return TeleZustandSchlecht;
        var e = _entities[t];
        if (e.Cooldown > 0) return TeleZustandLaedt;                              // @0x431A1F
        int dx = e.Col - c, dy = e.Row - r;
        if (dx * dx + dy * dy > 26) return TeleZustandWeit;                       // @0x431A57
        int z = TeleZielAuf(c, r);
        if (z == -2) return TeleZustandGut;                                       // @0x431AE4
        if (z < 0) return TeleZustandSchlecht;
        var v = _entities[z];
        if (v.GameUnitType != 0 || z == t || (e.Zwilling >= 0 && v.Slot == e.Zwilling) || v.Faze != 0)
            return TeleZustandSchlecht;                                           // @0x431AAD..0x431ACB
        return TeleZustandGut;
    }

    /// <summary>Zustand -> Bild (Tafel 0x4A9BEC): 17→21, 18→22, 19→23, 25→24.</summary>
    public static int TeleZeigerbild(int zustand) => zustand switch
    {
        TeleZustandGut => 21, TeleZustandSchlecht => 22, TeleZustandWeit => 23, TeleZustandLaedt => 24, _ => -1,
    };

    /// <summary>Der Klick — 0x4377F7 nur bei Zeigerart 0x11, sonst nichts; der Modus bleibt.</summary>
    public bool TeleportKlick(int idx, int col, int row)
    {
        PlacementMode = OrderTeleport; PlacementUnit = idx;          // 0x4377F7 nullt 0x502ACC nicht
        if (idx < 0 || idx >= _entities.Count) { CancelPlacement(); BuildOrderNote = "die Einheit ist weg"; return false; }
        var e = _entities[idx];
        if (e.Dead || e.Owner != ViewPlayer) { CancelPlacement(); BuildOrderNote = "die Einheit ist weg"; return false; }
        int zustand = TeleZeigerZustand(idx, col, row);
        if (zustand != TeleZustandGut)
        {
            BuildOrderNote = zustand == TeleZustandLaedt ? "der Teleporter laedt nach"
                           : zustand == TeleZustandWeit ? "zu weit weg" : "dort ist nichts zu teleportieren";
            return false;
        }
        if (!Emit(CommandRecord.Make(CommandOp.Teleportieren, (byte)ViewPlayer, (short)idx, (short)col, (short)row)))
        { BuildOrderNote = "der Befehl liess sich nicht absetzen"; return false; }
        BuildOrderNote = $"Teleportieren: ({col},{row})";
        return true;
    }

    // ==== der Befehl 23: 0x43A420 ====================================================

    private void TeleBlitzAn(int c, int r)
        => _effects.Add(new Effect { Pos = CellCenter(c, r), Kind = TeleBlitz, FrameTime = 1f / SimHz });

    /// <summary><c>0x43A2F0(Zwilling, 0)</c>: die Spirale 0x79A008 ab (c0, r0), 20000 Schritte,
    /// die erste Zelle mit imap == 0xFFFE.</summary>
    private Vector2I? TeleFreieZelle(int c0, int r0)
    {
        for (int k = 0; k < 20000; k++)                                          // @0x43A3B5 cmp si,0x4e20
        {
            var (dx, dy) = Simulation.NavGrid.RingEintrag(k);
            int c = c0 + dx, r = r0 + dy;
            if (ImapWortFrei(c, r) && !GrundrissSperrt(c, r) && !MbFussvolkAuf(c, r)) return new Vector2I(c, r);
        }
        return null;
    }

    private bool ApplyTeleportieren(in CommandRecord c)
    {
        if (TeleporterAus || _nav == null) return false;
        int ti = c.P1, x = c.P2, y = c.P3;
        var t = _entities[ti];
        if (t.Dead || t.IsBuilding || t.IsProp) return false;
        TeleBefehle++;
        TeleBlitzAn(x, y);                                                       // 0x401172(x, y, 0, 0, 0x60)
        Audio.GameSounds.PlayAt(TeleKlangStart, t.Col, t.Row);                   // 56, Modus 1
        Audio.GameSounds.PlayAt(TeleKlangZiel, x, y);                            // 57, Modus 2
        t.Cooldown = t.Reload * ReloadTick;                                      // +0x32 := +0x3D
        int zi = TelePlatzIndex(t.Zwilling);
        if (zi < 0) { TeleOhneZiel++; return true; }                             // UNSERE SETZUNG (Kopf)
        var zw = _entities[zi];
        int v = _nav.InBounds(x, y) ? TeleZielAuf(x, y) : -1;
        if (v == -1) { TeleOhneZiel++; return true; }                            // UNSERE SETZUNG (Kopf)
        var frei = TeleFreieZelle(zw.Col, zw.Row);
        if (frei == null) { TeleKeinPlatz++; return true; }                      // je 0x43a791
        if (v >= 0) TeleFahrzeugUmsetzen(v, frei.Value);
        else TeleTruppUmsetzen(x, y, frei.Value, zw);
        return true;
    }

    /// <summary>Der Fahrzeugzweig 0x43A4EB..0x43A621.</summary>
    private void TeleFahrzeugUmsetzen(int vi, Vector2I z)
    {
        var v = _entities[vi];
        bool fuhr = v.Path != null;
        var ziel = v.Goal;
        _nav!.ClearOccupant(v.Col, v.Row, vi);                                   // alte Zelle frei
        if (v.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, vi);            // die Zelle des Schritts frei
        v.Reserved = null;
        v.Col = z.X; v.Row = z.Y;                                                // +0x00/+0x01
        v.Elev = ElevOf(z.X, z.Y);
        v.Pos = BodyCenterAt(v, z.X, z.Y);
        v.Footprint = CellRect(_ox, _oy, z.X, z.Y, v.Elev);
        _nav.SetOccupant(z.X, z.Y, vi);                                          // imap := V
        v.Path = null; v.PathIdx = 0; v.WaitTime = 0;                            // KOLIK 0, +0x04 0xFF, +0x1C 0
        if (fuhr)                                                                // UNSERE SETZUNG (Kopf)
        {
            var p = _nav.FindPath(z, ziel, v.Move, vi);
            if (p != null && p.Count > 0) { v.Path = p; v.PathIdx = 0; v.Goal = ziel; }
        }
        TeleFahrzeuge++;
        TeleBlitzAn(z.X, z.Y);                                                   // 0x401172(..., 0x60)
        Audio.GameSounds.PlayAt(TeleKlangAnkunft, z.X, z.Y);                     // 58, Modus 2
        GD.Print($"teleporter: Fahrzeug {vi} »{LabelOf(v)}« (Spieler {v.Owner}) -> ({z.X},{z.Y})");
    }

    /// <summary>Der Truppzweig 0x43A631..0x43A78B.</summary>
    private void TeleTruppUmsetzen(int x, int y, Vector2I z, Entity zw)
    {
        var maenner = new List<int>();
        for (int i = 0; i < _entities.Count; i++)
        {
            var s = _entities[i];
            if (s.Dead || s.IsProp || s.IsBuilding || s.Infantry < 0 || Untergestellt(s)) continue;
            if (s.Col != x || s.Row != y) continue;
            maenner.Add(i);
            if (maenner.Count == 9) break;                                       // 9 Plaetze 0x7847EC
        }
        TeleBlitzAn(z.X, z.Y);
        TeleTrupps++;
        foreach (int si in maenner)
        {
            var s = _entities[si];
            _nav!.ClearOccupant(s.Col, s.Row, si);                               // 0x40175D
            if (s.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, si);
            s.Reserved = null;
            s.Col = z.X; s.Row = z.Y;
            s.Elev = ElevOf(z.X, z.Y);
            s.Pos = BodyCenterAt(s, z.X, z.Y);
            s.Footprint = CellRect(_ox, _oy, z.X, z.Y, s.Elev);
            _nav.SetOccupant(z.X, z.Y, si, true);                                // 0x40107D / 0x401267
            s.Path = null; s.PathIdx = 0; s.WaitTime = 0;                        // KOLIK 0, +0x04 0xFF, +0x1C 0
            TeleMaenner++;
            int dort = 0;
            foreach (var o in _entities)
                if (!o.Dead && !o.IsProp && !o.IsBuilding && o.Infantry >= 0 && o.Col == z.X && o.Row == z.Y && !Untergestellt(o)) dort++;
            if (dort < 9) continue;                                              // 0x402509: noch Platz
            var neu = TeleFreieZelle(zw.Col, zw.Row);
            if (neu == null) { TeleFehler++; GD.Print("teleporter: Error while teleporting (0x43A765)"); }
            else z = neu.Value;
            TeleBlitzAn(z.X, z.Y);
        }
        GD.Print($"teleporter: Trupp ({x},{y}) {maenner.Count} Mann -> ({z.X},{z.Y})");
    }

    // ==== Spielstand ==================================================================

    private static void TeleEinheitSchreiben(Core.SaveGame.Writer w, Entity e)
    {
        if (!TeleporterAus && e.Zwilling >= 0) w.Raw("zwilling", e.Zwilling.ToString());
        if (e.SpiegelAn != 0 || e.IllusionAn != 0 || e.Bild4A != 0 || e.Bild4B != 0)
            w.Raw("tarnung", $"[{e.SpiegelAn},{e.IllusionAn},{e.Bild4A},{e.Bild4B}]");
    }

    private static void TeleEinheitLesen(GDict d, Entity e)
    {
        e.Zwilling = !TeleporterAus && d.TryGetValue("zwilling", out var z) && z.VariantType != Variant.Type.Nil ? z.AsInt32() : -1;
        e.SpiegelAn = e.IllusionAn = e.Bild4A = e.Bild4B = 0;
        if (d.TryGetValue("tarnung", out var t) && t.VariantType == Variant.Type.Array)
        {
            var a = t.AsGodotArray();
            if (a.Count >= 4) { e.SpiegelAn = a[0].AsInt32(); e.IllusionAn = a[1].AsInt32(); e.Bild4A = a[2].AsInt32(); e.Bild4B = a[3].AsInt32(); }
        }
    }

    /// <summary>Ein Teil fuer <c>SpielstandFingerabdruck</c>.</summary>
    public string TeleFingerabdruck()
    {
        int n = 0, tn = 0; long p = 0, tp = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead) continue;
            if (e.Zwilling >= 0) { n++; p += (e.Slot + 1) * 7L + (e.Zwilling + 1) * 13L; }
            if (e.SpiegelAn != 0 || e.IllusionAn != 0 || e.Bild4A != 0 || e.Bild4B != 0)
            { tn++; tp += (e.Slot + 1) * 3L + e.SpiegelAn * 5L + e.IllusionAn * 11L + e.Bild4A * 17L + e.Bild4B * 19L; }
        }
        return $"Zwillinge {n} (Pruefzahl {p}), Tarnung {tn} (Pruefzahl {tp})";
    }

    /// <summary>Fuer <c>SpielstandProbeVorbereiten</c>: zwei eigene Fahrzeuge zu Zwillingen
    /// machen und einem dritten Spiegel- und Illusionswerte geben. ⚠ EINGRIFF.</summary>
    public string SpielstandProbeTele()
    {
        var eigene = new List<Entity>();
        foreach (var e in _entities)
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Infantry < 0 && e.Owner == ViewPlayer && !Untergestellt(e)) eigene.Add(e);
        if (eigene.Count < 3) return "Teleporter: keine drei eigenen Fahrzeuge";
        eigene[0].Zwilling = eigene[1].Slot; eigene[1].Zwilling = eigene[0].Slot;
        eigene[2].SpiegelAn = 1; eigene[2].IllusionAn = 1; eigene[2].Bild4A = 7; eigene[2].Bild4B = 44;
        return $"Zwillinge Platz {eigene[0].Slot}<->{eigene[1].Slot}, Tarnung an Platz {eigene[2].Slot} [1,1,7,44]";
    }

    // ==== --teleport-check =============================================================

    /// <summary>
    /// <c>--teleport-check</c>, Kampagne 29 (P0 hat eine Basis). Der SPIELERWEG, mit benannten
    /// Eingriffen:
    /// <list type="number">
    /// <item>Bauliste: steht Entwurf 65 in der Bauliste der Basis von P0? (nur Auskunft)</item>
    /// <item>Bau: Befehl 503 mit Entwurf 65 an der Basis (⚠ EINGRIFF: Geld und Lager, ggf. Liste)
    ///   -> ZWEI Teleporter, gegenseitig Zwilling.</item>
    /// <item>Fahrzeug: ein eigenes Fahrzeug 3 Zellen neben Teleporter A (⚠ EINGRIFF: versetzt),
    ///   Menue 0x26, Zeiger 21, Klick -> Befehl 23 ueber den Ring -> es steht an der ersten freien
    ///   Spiralzelle des Zwillings; Klaenge 56/57/58; danach Zeiger 24 (laedt nach).</item>
    /// <item>Trupp: der naechste Trupp der Karte (fremd erlaubt — keine Besitzerpruefung),
    ///   Teleporter A daneben (⚠ EINGRIFF), Nachladen auf 0 (⚠ EINGRIFF) -> alle Mann in EINER
    ///   Zelle neben B, kein Klang 58.</item>
    /// <item>Tod: A toeten -> B stirbt mit.</item>
    /// </list>
    /// Nullmodell <c>--teleporter-aus</c>: ein Teleporter, kein Befehl — der Lauf MUSS durchfallen.
    /// </summary>
    public string TeleportCheck()
    {
        var sb = new StringBuilder("teleport-check (bug-719)\n");
        EnsureMissionScript();
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (_nav == null || _designs == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (TeleporterAus) sb.AppendLine("  ⚠ NULLMODELL --teleporter-aus: hier MUSS der Lauf durchfallen");
        var gehoert = new List<int>();
        var vorherHorcher = Audio.SoundBankPlayer.Horcher;
        Audio.SoundBankPlayer.Horcher = (s, c, r) => { gehoert.Add(s); vorherHorcher?.Invoke(s, c, r); };
        try
        {
            // 1. die Basis und die Bauliste
            int bi = -1;
            for (int i = 0; i < _entities.Count && bi < 0; i++)
                if (IsUnitPlant(_entities[i]) && !_entities[i].Dead && _entities[i].Owner == ViewPlayer) bi = i;
            if (bi < 0) return sb.Append("  keine eigene Basis\n  DURCHGEFALLEN").ToString();
            var basis = _entities[bi];
            var liste = BuildableFor(basis.BType, basis.Owner);
            int di = liste.Find(k => _designs[k].Weapon == TeleporterTeil && !_designs[k].Entfernt);
            bool inListe = liste.Exists(k => _designs[k].Weapon == TeleporterTeil);
            sb.AppendLine($"  Basis Platz {basis.Slot} ({basis.Col},{basis.Row}); Bauliste {liste.Count} Entwuerfe, Teleporter darin: {(inListe ? "JA" : "nein")}"
                        + $" (Teil 65 erforscht: {(Researched(ViewPlayer).Contains(TeleporterTeil) ? "ja" : "nein")})");
            bool listenEingriff = false;
            if (!inListe)
            {
                di = _designs.FindIndex(d => d.Weapon == TeleporterTeil && !d.Entfernt);
                if (di < 0) return sb.Append("  kein Entwurf mit Waffe 65\n  DURCHGEFALLEN").ToString();
                listenEingriff = true;
                sb.AppendLine($"  ⚠ EINGRIFF: Entwurf {di} »{_designs[di].Name}« steht nicht in der Bauliste — er wird direkt ueber 0x4B1840 (NeubauInListe) gebaut");
            }
            // 2. bauen
            _money[ViewPlayer] += 100000;
            basis.StockW += 500; basis.StockF += 500; basis.StockS += 500;
            sb.AppendLine("  ⚠ EINGRIFF: +100000 Geld, +500 je Lager");
            // 2a. bug-724: nur EIN Depotplatz frei -> der Teleporter wird abgesagt (0x44A692)
            if (!listenEingriff)
            {
                var platzhalter = new List<int>();
                while (GarageBelegt(basis) < DepotSlots - 1) { basis.Depot.Add(di); platzhalter.Add(di); }
                int nA = _entities.Count, mA = BasisMeldungen, aA = TeleDepotAbsagen;
                _sel.Clear(); _sel.Add(bi); _selected = bi;
                Emit(CommandRecord.Make(CommandOp.EinheitBauen, (byte)ViewPlayer, (short)di, (short)ViewPlayer, (short)bi));
                for (int k = 0; k < 30 && BasisMeldungen == mA && _entities.Count == nA; k++) SimTickFuerProbe();
                int gebautA = 0;
                for (int i = nA; i < _entities.Count; i++) if (IstTeleporter(_entities[i])) gebautA++;
                sb.AppendLine($"  ⚠ EINGRIFF: {platzhalter.Count} Platzhalter ins Depot, belegt {GarageBelegt(basis)}/{DepotSlots}");
                Soll(gebautA == 0 && LetzteBasisMeldung == DepotFullWord,
                     $"bug-724: ein Platz frei -> {gebautA} Teleporter, Meldung »{LetzteBasisMeldung}« (Soll: keiner, »{DepotFullWord}«)");
                // die Platzhalter wieder heraus (und was eine Gegenprobe gebaut hat)
                foreach (var _ in platzhalter) basis.Depot.Remove(di);
                for (int i = nA; i < _entities.Count; i++)
                    if (IstTeleporter(_entities[i]) && !_entities[i].Dead) { _entities[i].Zwilling = -1; basis.Garage.Remove(_entities[i]); _entities[i].Hp = 0; _entities[i].Dead = true; }
                _ = aA;
            }
            int n0 = _entities.Count, zw0 = TeleZwillinge;
            if (listenEingriff) NeubauInListe(basis, di);
            else
            {
                _sel.Clear(); _sel.Add(bi); _selected = bi;
                Emit(CommandRecord.Make(CommandOp.EinheitBauen, (byte)ViewPlayer, (short)di, (short)ViewPlayer, (short)bi));
            }
            var neu = new List<int>();
            for (int k = 0; k < 600 && !TeleNeuDraussen(n0, neu); k++) SimTickFuerProbe();
            sb.AppendLine($"  gebaut: {neu.Count} Teleporter ({string.Join(", ", neu.ConvertAll(i => $"Platz {_entities[i].Slot} ({_entities[i].Col},{_entities[i].Row}) Zwilling {_entities[i].Zwilling}{(Untergestellt(_entities[i]) ? " IM DEPOT" : "")}"))})");
            bool paar = neu.Count == 2 && _entities[neu[0]].Zwilling == _entities[neu[1]].Slot
                        && _entities[neu[1]].Zwilling == _entities[neu[0]].Slot && TeleZwillinge == zw0 + 1;
            Soll(paar, "Bau: zwei Teleporter, gegenseitig Zwilling (0x4B1DA7)");
            if (neu.Count < 2) return sb.Append("  DURCHGEFALLEN").ToString();
            int ai = neu[0], bzi = neu[1];
            var A = _entities[ai]; var B = _entities[bzi];
            if (Untergestellt(A) || Untergestellt(B)) return sb.Append("  ⚠ ein Teleporter steht noch im Depot\n  DURCHGEFALLEN").ToString();
            // Teleporter A und B auseinander: A an einen freien Platz weit weg von B (⚠ EINGRIFF)
            var weit = TeleProbePlatz(B.Col, B.Row, 12, 20);
            if (weit != null) { TeleProbeVersetzen(ai, weit.Value.X, weit.Value.Y); sb.AppendLine($"  ⚠ EINGRIFF: Teleporter A nach ({A.Col},{A.Row}), B steht ({B.Col},{B.Row})"); }

            // 3. ein Fahrzeug
            int vi = -1;
            for (int i = 0; i < _entities.Count && vi < 0; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry >= 0 || e.GameUnitType != 0 || e.Owner != ViewPlayer
                    || i == ai || i == bzi || Untergestellt(e) || e.Faze != 0) continue;
                vi = i;
            }
            if (vi < 0) { Soll(false, "kein eigenes Fahrzeug fuer den Fahrzeugfall"); }
            else
            {
                var V = _entities[vi];
                var nah = TeleProbePlatz(A.Col, A.Row, 3, 3);
                if (nah != null) TeleProbeVersetzen(vi, nah.Value.X, nah.Value.Y);
                sb.AppendLine($"  ⚠ EINGRIFF: Fahrzeug {vi} »{LabelOf(V)}« nach ({V.Col},{V.Row}), {System.Math.Sqrt((V.Col - A.Col) * (V.Col - A.Col) + (V.Row - A.Row) * (V.Row - A.Row)):0.0} Zellen von A");
                var soll = TeleFreieZelle(B.Col, B.Row);
                int h0 = gehoert.Count, c0 = TeleFahrzeuge;
                _sel.Clear(); _sel.Add(ai); _selected = ai;
                string menue = MenueAktion(CodeTeleport);
                int modus = PlacementMode;
                int zeiger = BefehlsartZeigerbild(V.Col, V.Row);
                int zeigerWeit = BefehlsartZeigerbild(A.Col + 6, A.Row);
                int zeigerSelbst = BefehlsartZeigerbild(A.Col, A.Row);
                bool klick = PlacementClick(V.Col, V.Row);
                for (int k = 0; k < 30 && TeleFahrzeuge == c0; k++) SimTickFuerProbe();
                var neuK = gehoert.GetRange(h0, gehoert.Count - h0);
                int zeigerDanach = BefehlsartZeigerbild(V.Col, V.Row);
                bool modusBleibt = PlacementMode == OrderTeleport;
                bool steht = soll != null && V.Col == soll.Value.X && V.Row == soll.Value.Y;
                int abstand = System.Math.Max(System.Math.Abs(V.Col - B.Col), System.Math.Abs(V.Row - B.Row));
                sb.AppendLine($"  Menue »{(menue.Length == 0 ? "ok" : menue)}«, Modus {modus}, Zeiger am Fahrzeug {zeiger} (21), 6 Zellen weg {zeigerWeit} (23), auf sich selbst {zeigerSelbst} (22)");
                Soll(modus == OrderTeleport && zeiger == 21 && zeigerWeit == 23 && zeigerSelbst == 22, "Befehlsart 9 und die Zeigerzustaende 0x11/0x13/0x12");
                Soll(klick && TeleFahrzeuge == c0 + 1 && steht && abstand <= 2,
                     $"Fahrzeug: Befehl 23 -> steht ({V.Col},{V.Row}), Soll {soll?.ToString() ?? "keine"}, {abstand} Zellen vom Zwilling ({B.Col},{B.Row})");
                Soll(neuK.Contains(TeleKlangStart) && neuK.Contains(TeleKlangZiel) && neuK.Contains(TeleKlangAnkunft),
                     $"Klaenge 56/57/58: {string.Join(",", neuK)}");
                Soll(zeigerDanach == 24 && modusBleibt && A.Cooldown > 0, $"danach: Zeiger {zeigerDanach} (24, laedt nach), Modus bleibt {modusBleibt}, Nachladen {A.Cooldown:0.00}");
                CancelPlacement();
            }

            // 4. ein Trupp (der naechste der Karte). K29 hat keinen: ⚠ EINGRIFF, drei Mann aus der Basis.
            if (!_entities.Exists(e => !e.Dead && !e.IsProp && !e.IsBuilding && e.Infantry >= 0 && !Untergestellt(e)))
            {
                int fd = _designs.FindIndex(d => d.Propulsion is 148 or 149 && !d.Entfernt && InfantryFor(d.Weapon, out _, out _));
                var mann = new List<Entity>();
                for (int k = 0; k < 3 && fd >= 0; k++) if (NeubauInListe(basis, fd, merker: false) is { } m) mann.Add(m);
                for (int k = 0; k < 900 && mann.Exists(m => Untergestellt(m)); k++) SimTickFuerProbe();
                var platz = mann.Count > 0 && !Untergestellt(mann[0]) ? TeleProbePlatz(A.Col, A.Row, 3, 4) : null;
                if (platz != null)
                    foreach (var m in mann) if (!Untergestellt(m) && !m.Dead) TeleProbeVersetzen(_entities.IndexOf(m), platz.Value.X, platz.Value.Y);
                sb.AppendLine($"  ⚠ EINGRIFF: kein Fussvolk auf der Karte — {mann.Count} Mann »{(fd >= 0 ? _designs[fd].Name : "?")}« gebaut und auf {platz?.ToString() ?? "-"} gestellt");
            }
            int ti = -1; int best = int.MaxValue;
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry < 0 || Untergestellt(e)) continue;
                int d2 = (e.Col - A.Col) * (e.Col - A.Col) + (e.Row - A.Row) * (e.Row - A.Row);
                if (d2 < best) { best = d2; ti = i; }
            }
            if (ti < 0) Soll(false, "kein Fussvolk auf der Karte");
            else
            {
                var T = _entities[ti];
                int tc = T.Col, tr = T.Row, mann = 0, besitzer = T.Owner;
                foreach (var e in _entities) if (!e.Dead && !e.IsProp && e.Infantry >= 0 && e.Col == tc && e.Row == tr && !Untergestellt(e)) mann++;
                var nah = TeleProbePlatz(tc, tr, 2, 4);
                if (nah != null) TeleProbeVersetzen(ai, nah.Value.X, nah.Value.Y);
                A.Cooldown = 0;
                sb.AppendLine($"  ⚠ EINGRIFF: Teleporter A nach ({A.Col},{A.Row}) neben den Trupp ({tc},{tr}) von Spieler {besitzer} ({mann} Mann), Nachladen := 0");
                var soll = TeleFreieZelle(B.Col, B.Row);
                int h0 = gehoert.Count, m0 = TeleMaenner;
                _sel.Clear(); _sel.Add(ai); _selected = ai;
                MenueAktion(CodeTeleport);
                int zeiger = BefehlsartZeigerbild(tc, tr);
                bool klick = PlacementClick(tc, tr);
                for (int k = 0; k < 30 && TeleMaenner == m0; k++) SimTickFuerProbe();
                CancelPlacement();
                var neuK = gehoert.GetRange(h0, gehoert.Count - h0);
                int amZiel = 0, amStart = 0;
                foreach (var e in _entities)
                {
                    if (e.Dead || e.IsProp || e.Infantry < 0 || Untergestellt(e)) continue;
                    if (soll != null && e.Col == soll.Value.X && e.Row == soll.Value.Y) amZiel++;
                    if (e.Col == tc && e.Row == tr) amStart++;
                }
                Soll(zeiger == 21 && klick && TeleMaenner == m0 + mann && amZiel == mann && amStart == 0,
                     $"Trupp: Zeiger {zeiger}, {TeleMaenner - m0} Mann umgesetzt, am Ziel {soll?.ToString() ?? "-"} {amZiel}/{mann}, am Start {amStart}");
                Soll(neuK.Contains(TeleKlangStart) && neuK.Contains(TeleKlangZiel) && !neuK.Contains(TeleKlangAnkunft),
                     $"Trupp-Klaenge 56/57 ohne 58: {string.Join(",", neuK)}");
            }

            // 5. Mitsterben
            int tot0 = TeleMitgestorben;
            A.Hp = 0;
            Kill(ai, A, -1, "Pruefstand teleport-check");
            for (int k = 0; k < 3; k++) SimTickFuerProbe();
            Soll(A.Dead && B.Dead && TeleMitgestorben == tot0 + 1, $"Tod: A tot {A.Dead}, Zwilling B tot {B.Dead} (0x40B7FF)");

            // 6. bug-725: der alte Aussendeweg (KI ohne Tuer, --bauzeit-alt) — eine Depotzeile
            if (!listenEingriff || di >= 0)
            {
                while (basis.Depot.Count > 0 && GarageBelegt(basis) >= DepotSlots - 1) basis.Depot.RemoveAt(basis.Depot.Count - 1);
                int nB = _entities.Count;
                basis.Depot.Add(di);
                bool raus = SendOutOfDepot(basis, basis.Depot.Count - 1);
                for (int k = 0; k < 600 && _entities.GetRange(nB, _entities.Count - nB).Exists(x => Untergestellt(x)); k++) SimTickFuerProbe();
                var neuB = new List<Entity>();
                for (int i = nB; i < _entities.Count; i++) if (IstTeleporter(_entities[i]) && !_entities[i].Dead) neuB.Add(_entities[i]);
                bool paarB = neuB.Count == 2 && neuB[0].Zwilling == neuB[1].Slot && neuB[1].Zwilling == neuB[0].Slot;
                sb.AppendLine($"  ⚠ EINGRIFF: Entwurf {di} als Depotzeile, SendOutOfDepot -> {raus}");
                Soll(paarB, $"bug-725: Aussenden aus der Depotzeile -> {neuB.Count} Teleporter ({string.Join(", ", neuB.ConvertAll(x => $"Platz {x.Slot} Zwilling {x.Zwilling}"))})");
            }
        }
        finally { Audio.SoundBankPlayer.Horcher = vorherHorcher; _sel.Clear(); _selected = -1; }
        sb.AppendLine($"  Zaehler: Zwillinge {TeleZwillinge}, Befehle {TeleBefehle}, Fahrzeuge {TeleFahrzeuge}, Trupps {TeleTrupps} ({TeleMaenner} Mann), "
                    + $"kein Platz {TeleKeinPlatz}, ohne Ziel {TeleOhneZiel}, Fehler {TeleFehler}, mitgestorben {TeleMitgestorben}; --teleporter-aus {TeleporterAus}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }

    /// <summary>Die Teleporter, die seit <paramref name="n0"/> entstanden sind; true, wenn alle
    /// (mindestens einer) draussen stehen und kein weiterer mehr kommt.</summary>
    private bool TeleNeuDraussen(int n0, List<int> neu)
    {
        neu.Clear();
        for (int i = n0; i < _entities.Count; i++)
            if (!_entities[i].Dead && IstTeleporter(_entities[i])) neu.Add(i);
        if (neu.Count == 0) return false;
        foreach (int i in neu) if (Untergestellt(_entities[i])) return false;
        return true;
    }

    /// <summary>Pruefstand: eine freie, erreichbare Zelle im Abstand min..max (Schachbrett) um (c, r).</summary>
    private Vector2I? TeleProbePlatz(int c0, int r0, int min, int max)
    {
        for (int d = min; d <= max; d++)
            for (int dy = -d; dy <= d; dy++)
                for (int dx = -d; dx <= d; dx++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != d) continue;
                    if (dx * dx + dy * dy > 26 && max <= 4) continue;
                    int c = c0 + dx, r = r0 + dy;
                    if (ImapWortFrei(c, r) && !GrundrissSperrt(c, r) && GebaeudeAufZelle(c, r) < 0 && !MbFussvolkAuf(c, r))
                        return new Vector2I(c, r);
                }
        return null;
    }

    /// <summary>⚠ Pruefstand-Eingriff: eine Einheit auf eine Zelle stellen, mit Belegung.</summary>
    private void TeleProbeVersetzen(int idx, int c, int r)
    {
        var e = _entities[idx];
        _nav!.ClearOccupant(e.Col, e.Row, idx);
        if (e.Reserved is { } rc) _nav.ClearOccupant(rc.X, rc.Y, idx);
        e.Reserved = null; e.Path = null;
        e.Col = c; e.Row = r;
        e.Elev = ElevOf(c, r);
        e.Pos = BodyCenterAt(e, c, r);
        e.Footprint = CellRect(_ox, _oy, c, r, e.Elev);
        _nav.SetOccupant(c, r, idx, e.Infantry >= 0);
    }
}
