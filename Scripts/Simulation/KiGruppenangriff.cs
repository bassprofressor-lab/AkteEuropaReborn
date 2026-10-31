namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

/// <summary>
/// ⭐⭐ <b>DER ANGRIFFSARM DES GRUPPENLAUFS — C <c>0x4BD1E6…0x4BD5F7</c>, F
/// <c>0x4BCCA6…0x4BD0B7</c></b> (30.10.2026, bug-690; Lesung
/// <c>berichte/pruefung-rest-c-opus.md</c> §1.2, Bau <c>berichte/bau-ki-gruppenangriff.md</c>).
///
/// <para>Hinter dem Wegpunkt-Tor (<c>0x4BD18B</c>) und Klang 122 nimmt der Gruppenlauf
/// <c>0x4BCF30</c> die Art des Auftrags (<c>byte[0xBC5A78+…]</c>, 1..4) und springt über die
/// Tafel <c>0x4BD7CC</c> (F <c>0x4BD28C</c>) = { <c>0x4BD206</c>, <c>0x4BD397</c>,
/// <c>0x4BD474</c>, <c>0x4BD534</c> } (F { <c>0x4BCCC6</c>, <c>0x4BCE57</c>, <c>0x4BCF34</c>,
/// <c>0x4BCFF4</c> }). Jeder Arm geht <b>in jedem Lauf</b> alle Mitglieder
/// (<c>word[0xB36BE2 + 2·(101·gruppe + k)]</c>, k aufwärts) durch; es gibt keinen Merker
/// »schon befohlen«. C und F Befehl für Befehl gleich, nur die Adressen verschoben
/// (Würfel C <c>0x4013ED → 0x4C5B30</c> / F <c>0x4013E8 → 0x4C56E0</c>, <c>angreife</c>
/// C <c>0x4020DB → 0x410220</c> / F <c>0x4020D1 → 0x410050</c>, <c>fahre</c> C
/// <c>0x4021A8 → 0x40B070</c> / F <c>0x40219E → 0x40AF60</c>):</para>
/// <code>
///   Satz der Einheit 0x6E26C8 + 78·u: +0x00 x, +0x01 y, +0x04 (0xFF = steht),
///   +0x14 UKOL, +0x16 OTACIM (Drehbremse), +0x34 STRILI_NA
///   »zweit« := +0x04 == 0xFF und +0x16 == 0
///
///   Art 1, c != 0 (Gebaeude, 0x4BD23B):  UKOL != 4 oder zweit
///       ry := W % 10 ; rx := W &amp; 7                      ; ERST y, DANN x
///       angreife(u, bx + rx − 2, by + ry − 2, 60000+Platz)   ; Kasten x −2..+5, y −2..+7
///   Art 1, c == 0 (Tuergriff, 0x4BD2ED): UKOL == 0 oder zweit
///       fahre(u, bx + [+0x35], by + [+0x36] + 1)             ; die Einnahmezelle
///   Art 2 (Einheit, 0x4BD397):           UKOL != 4 oder zweit
///       ry := W % 10 ; rx := W % 10
///       angreife(u, ux + rx − 4, uy + ry − 4, Platz)         ; Kasten −4..+5 / −4..+5
///   Art 3 (sec16-Objekt 0xBFEA80, 0x4BD474): UKOL != 4 oder (zweit und +0x34 != Ziel)
///       angreife(u, ox + 1, oy + 1, 40100+Objekt)            ; kein Wurf
///   Art 4 (Ort, 0x4BD534):               UKOL == 0 oder zweit
///       ry := W % 3 ; rx := W % 3
///       fahre(u, x + rx − 1, y + ry − 1)                     ; Kasten −1..+1 / −1..+1
/// </code>
/// <para>Alle Zellen sind <b>Bytes</b> (<c>add al / sub al</c>). Der Würfel ist der
/// Spielwürfel <c>0x4C5B30</c> (Netzspiel: <c>word[0x539240]</c> über <c>fsin</c>, sonst
/// <c>rand</c> 0x4D6C70) — jeder Wurf ist Spielzustand, bei uns
/// <see cref="Simulation.Determinism.Roll"/> in der Reihenfolge des Originals.</para>
///
/// <para>⚠ <b>UNSERE SETZUNGEN</b> (alle hier, nirgends sonst):</para>
/// <list type="bullet">
///   <item>»UKOL == 0« ist <see cref="AiSteht"/> (wie Türgriff und Wegpunktfahrt);
///   »UKOL != 4« ist »frei und kein Angriffsziel« (<c>Ukol == 0</c>, <c>Target &lt; 0</c>) — bei
///   uns ist der Angriff kein UKOL-Wert, sondern <c>Target</c>.</item>
///   <item>»zweit« ist <c>Path == null</c> (+0x04 == 0xFF, »steht«) und
///   <c>DrehWarten == 0</c> (+0x16 OTACIM).</item>
///   <item>Eine Einheit mit einem Sonderzustand (<c>Ukol != 0</c>: Tür, Garage, Zug,
///   Ausweichen, Umschlag) fasst der Arm NICHT an. Das Original befähle ihr bei UKOL != 4;
///   unsere Zustandsmaschinen dieser Werte vertragen einen Fremdbefehl nicht (V).</item>
///   <item><c>angreife</c> ist <see cref="AiSend"/> mit der gewürfelten Zelle als Fahrziel
///   (UKOL 4 + CX/CY + UTOK_NA); steht das Ziel schon in Waffenreichweite, bleibt die
///   Einheit stehen und schiesst (wie bisher bei AiSend). <c>fahre</c> ist
///   <see cref="AiWalkTo"/>.</item>
///   <item>Die Bytezelle wird auf die Karte geklemmt (das Original reicht sie ungeklemmt an
///   <c>order()</c>; was dessen Wegsuche mit Zelle 254 tut, ist ungelesen).</item>
///   <item>Türgriff: wer schon AUF der Einnahmezelle steht, bekommt keinen Befehl (wie bisher,
///   bug-384; im Original ein wirkungsloser <c>fahre</c> auf die eigene Zelle).</item>
///   <item>Art 3 erreicht den Arm bei uns nie: <see cref="ResolveTarget"/> kann sec16/17 nicht
///   auflösen, die Gruppe entsteht nicht. Der Zweig steht trotzdem da (mit der Zelle des
///   aufgelösten Ziels).</item>
///   <item>Der Gruppenlauf steht bei uns VOR der Zielwahl (im Original danach, 0x4BE30E);
///   darum läuft der Arm für eine eben gebildete Gruppe ohne Marsch direkt in
///   <see cref="AiMissionAttack"/>.</item>
/// </list>
/// <para><b>Kampagne und Gefecht.</b> ⭐ bug-714 (30.10.2026, Spielerentscheidung »ja soll sie
/// auch bekommen«): seit heute läuft der Arm auch in der Gefechts-KI (W3,
/// Simulation/GefechtKiMarsch.cs, nur mit Gruppen/Marsch, nicht unter
/// <c>--gefecht-ki-marsch-aus</c>). NET07 ändert sich. Was dort UNSERE SETZUNG bleibt, steht in
/// GefechtKiMarsch.cs (Umziel nach dem Marsch, Auffüllen ohne eigenen Befehl, Nachsetzen
/// entfällt). Gegenschalter <c>--gefecht-gruppenangriff-alt</c> = im Gefecht der Stand davor
/// (<c>Losgeschickt</c>, <c>AiGruppeLosschicken</c>, Nachsetzen). Gegenschalter
/// <c>--ki-gruppenangriff-einmal</c> = überall der Stand vor bug-690 (Angriff einmal, nur der
/// Türgriff je Lauf). Prüfstand <c>--ki-gruppenangriff-check[=takte]</c> unten, auch im
/// Gefecht (<c>--skirmish=map_NET07</c>).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--ki-gruppenangriff-einmal</c> (bug-690): der Stand vor dem 30.10.2026 —
    /// die Gruppe bekommt ihren Angriff EINMAL (<c>Losgeschickt</c>), je Lauf neu nur der
    /// Türgriff.</summary>
    public static bool KiGruppenangriffEinmal;

    /// <summary><c>--gefecht-gruppenangriff-alt</c> (bug-714): im Gefecht der Stand vor dem
    /// 30.10.2026 abends — <c>Losgeschickt</c> (einmal losschicken), Nachsetzen der stehenden
    /// Mitglieder mit <c>AiSend</c> ohne Würfelzelle. Die Kampagne berührt er nicht.</summary>
    public static bool GefechtGruppenangriffAlt;

    /// <summary>Läuft der Arm des Originals? Kampagne, und (bug-714) das Gefecht mit der
    /// Gruppen-KI W3 — siehe oben.</summary>
    private bool KiGruppenangriffJeLauf => !KiGruppenangriffEinmal &&
        (InCampaign || (GefechtKiMarschAn && !GefechtGruppenangriffAlt));

    /// <summary>bug-714: läuft der Arm gerade im Gefecht?</summary>
    private bool GefechtArmAn => !InCampaign && KiGruppenangriffJeLauf;

    /// <summary>Zähler: Befehle des Arms je Art (0 = Türgriff, 1..4).</summary>
    public static readonly int[] KiArmBefehle = new int[5];

    /// <summary>»zweit« des Originals: <c>+0x04 == 0xFF</c> und <c>+0x16 == 0</c> — UNSERE
    /// Lesart siehe oben.</summary>
    private static bool KiArmZweit(Entity e) => e.Path == null && e.DrehWarten == 0;

    /// <summary>»UKOL != 4« — UNSERE Lesart: kein Angriffsziel.</summary>
    private static bool KiArmGreiftNicht(Entity e) => e.Target < 0;

    /// <summary>Die Buchführung, die bis dahin <see cref="AiGruppeLosschicken"/> beim ersten
    /// Befehl mit erledigte (Welle, Ziel, Wellenzahl, Protokollzeile) — OHNE Befehl.</summary>
    private void KiGruppeLosBuchen(int p, int g, int ziel, string grund)
    {
        var gruppe = _aiGruppen[p][g];
        gruppe.Losgeschickt = true;
        if (ziel < 0 || ziel >= _entities.Count) return;
        AiPlayer? a = null;
        foreach (var x in _ai) if (x.Player == p) { a = x; break; }
        if (a != null)
        {
            a.Wave.Clear();
            foreach (int i in gruppe.Einheiten) a.Wave.Add(i);
            a.TargetIdx = ziel;
            a.Waves++;
        }
        bool tuer = IstTuergriff(gruppe.Auftrag);
        var front = tuer ? CaptureCells(_entities[ziel]).Front : default;
        GD.Print($"KI P{p}: Gruppe {g} mit {gruppe.Einheiten.Count} Einheiten " +
                 (tuer ? $"zum EINNEHMEN auf Zelle ({front.X},{front.Y}) vor " : $"(Art {gruppe.Auftrag?.Kind}) auf ") +
                 $"{_entities[ziel].Name} bei ({_entities[ziel].Col},{_entities[ziel].Row}) — {grund} [Angriffsarm je Lauf, bug-690]");
    }

    /// <summary>
    /// Der Arm, für eine Gruppe in einem Gruppenlauf (aus <see cref="AiGruppenPflegen"/> nach
    /// Wegpunkt-Tor und Klang 122, und einmal direkt nach dem Bilden). Siehe Klassenkopf.
    /// </summary>
    private void KiGruppenAngriffsarm(int p, int g, AiGruppe gruppe, int zi)
    {
        var t = gruppe.Auftrag;
        if (t == null || zi < 0 || zi >= _entities.Count) return;
        var ziel = _entities[zi];
        bool tuer = IstTuergriff(t);
        // Gueltigkeit Art 1 c == 0 (@0x4BD095): eingenommen -> Gruppe aufloesen (wie bisher).
        if (tuer && ziel.Owner == p)
        {
            TuergriffErledigt++;
            GD.Print($"KI P{p}: Gruppe {g} — {ziel.Name} eingenommen, Gruppe aufgeloest (@0x4BD095)");
            AiGruppeAufloesen(p, g);
            return;
        }
        if (!gruppe.Losgeschickt) KiGruppeLosBuchen(p, g, zi, "nach dem Marsch");

        int art = tuer ? 0 : t.Kind;
        var front = tuer ? CaptureCells(ziel).Front : default;
        int bx = ziel.Col & 0xFF, by = ziel.Row & 0xFF;   // Satz +0x00 / +0x01 (+0x02 beim Gebaeude), Bytes
        foreach (int i in gruppe.Einheiten)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (e.Ukol != UkolFrei) continue;               // UNSERE SETZUNG: Sonderzustaende bleiben
            switch (art)
            {
                case 0:                                     // Tuergriff @0x4BD2ED…0x4BD392
                {
                    if (!(AiSteht(e) || KiArmZweit(e))) break;
                    if ((e.Col, e.Row) == (front.X, front.Y)) break;
                    KiArmBefehle[0]++;
                    if (KiGaPruefAn) KiGaPruefBefehl(i, 0, 0, 0, false);
                    if (KiPruefAn) KiPruefAngriff(p, g, i);
                    if (AiWalkTo(i, front)) TuergriffFahrten++;
                    break;
                }
                case 1:                                     // Gebaeude @0x4BD23B…0x4BD2E8
                {
                    if (!(KiArmGreiftNicht(e) || KiArmZweit(e))) break;
                    int ry = Simulation.Determinism.Roll(10);    // ERST: div cx(10) @0x4BD2AE -> y
                    int rx = Simulation.Determinism.Roll(8);     // DANN: and al,7 @0x4BD2BE -> x
                    KiArmAngreifen(p, g, i, zi, 1, bx, by, rx - 2, ry - 2);
                    break;
                }
                case 2:                                     // Einheit @0x4BD397…0x4BD46F
                {
                    if (!(KiArmGreiftNicht(e) || KiArmZweit(e))) break;
                    int ry = Simulation.Determinism.Roll(10);    // @0x4BD42E -> y
                    int rx = Simulation.Determinism.Roll(10);    // @0x4BD445 -> x
                    KiArmAngreifen(p, g, i, zi, 2, bx, by, rx - 4, ry - 4);
                    break;
                }
                case 3:                                     // sec16-Objekt @0x4BD474…0x4BD52F
                {
                    // +0x34 STRILI_NA != Ziel — UNSERE Lesart: Target != Ziel
                    if (!(KiArmGreiftNicht(e) || (KiArmZweit(e) && e.Target != zi))) break;
                    KiArmAngreifen(p, g, i, zi, 3, bx, by, 1, 1);
                    break;
                }
                case 4:                                     // Ort @0x4BD534…0x4BD5F7
                {
                    if (!(AiSteht(e) || KiArmZweit(e))) break;
                    int ry = Simulation.Determinism.Roll(3);     // @0x4BD5AA -> y
                    int rx = Simulation.Determinism.Roll(3);     // @0x4BD5C8 -> x
                    int x = t.Word & 0xFF, y = t.Second & 0xFF;  // bl = Wort, byte +4 = c
                    var zelle = KiArmZelle((x + rx - 1) & 0xFF, (y + ry - 1) & 0xFF);
                    KiArmBefehle[4]++;
                    if (KiGaPruefAn) KiGaPruefBefehl(i, 4, rx - 1, ry - 1, true);
                    if (KiPruefAn) KiPruefAngriff(p, g, i);
                    AiWalkTo(i, zelle);
                    break;
                }
            }
        }
    }

    /// <summary><c>angreife(u, zx, zy, Ziel)</c> = <c>order()</c> 0x410220 — bei uns
    /// <see cref="AiSend"/> mit der gewürfelten Zelle.</summary>
    private void KiArmAngreifen(int p, int g, int i, int zi, int art, int bx, int by, int dx, int dy)
    {
        var zelle = KiArmZelle((bx + dx) & 0xFF, (by + dy) & 0xFF);   // add al / sub al: Bytes
        KiArmBefehle[art]++;
        if (KiGaPruefAn) KiGaPruefBefehl(i, art, dx, dy, art != 3);
        if (KiPruefAn) KiPruefAngriff(p, g, i);
        AiSend(i, zi, zelle);
    }

    /// <summary>UNSERE SETZUNG: die Bytezelle auf die Karte klemmen.</summary>
    private Vector2I KiArmZelle(int x, int y)
    {
        int w = _nav?.Width ?? 256, h = _nav?.Height ?? 256;
        return new Vector2I(Math.Clamp(x, 0, Math.Max(0, w - 1)), Math.Clamp(y, 0, Math.Max(0, h - 1)));
    }

    // ---- Prüfstand --ki-gruppenangriff-check --------------------------------------------

    /// <summary>An, solange <c>--ki-gruppenangriff-check</c> läuft.</summary>
    public static bool KiGaPruefAn;

    /// <summary><c>--ki-gruppenangriff-natur</c>: der Prüfstand legt KEINE Probegruppen und
    /// greift nicht ein — nur die Aktivitätsmessung (vorher/nachher).</summary>
    public static bool KiGaNatur;

    private static readonly string[] KiArtName = { "Tuergriff", "Art 1 Gebaeude", "Art 2 Einheit", "Art 3 Objekt", "Art 4 Ort" };

    private sealed class KiGaLauf
    {
        public int P, G, Art;
        public readonly HashSet<int> Stehend = new();
        public readonly HashSet<int> Befohlen = new();
    }

    private KiGaLauf? _kiGaLauf;
    private readonly int[] _kiGaLaeufe = new int[5], _kiGaStehend = new int[5], _kiGaStehendBefohlen = new int[5],
                           _kiGaNachherStehend = new int[5], _kiGaBefehle = new int[5], _kiGaAusserKasten = new int[5];
    private readonly int[,] _kiGaWurfMin = new int[5, 2], _kiGaWurfMax = new int[5, 2];
    private readonly int[] _kiGaWuerfe = new int[5];
    /// <summary>bug-714: welche Arten die Würfelprobe pruefen konnte (im Gefecht fehlt oft ein
    /// feindliches Gebaeude, dann gibt es weder Art 1 noch Art 4).</summary>
    private readonly HashSet<int> _kiGaProbeArten = new();
    private readonly HashSet<(int p, int g, MissionTarget t)> _kiGaGesehen = new();
    private readonly int[] _kiGaGruppenJeArt = new int[5];

    private sealed class KiGaEingriff
    {
        public int P, G, Art, Idx = -1, Takt = -1, LaeufeSeitBildung, Urteil;   // Urteil 0 offen, 1 befohlen, -1 nicht
        public string Text = "";
    }
    private readonly List<KiGaEingriff> _kiGaEingriffe = new();

    private static readonly (int lo, int hi)[,] KiGaKasten =
    {
        { (0, 0), (0, 0) },        // Tuergriff: kein Wurf
        { (-2, 5), (-2, 7) },      // Art 1: x &7 −2, y %10 −2
        { (-4, 5), (-4, 5) },      // Art 2: %10 −4
        { (1, 1), (1, 1) },        // Art 3: +1, kein Wurf
        { (-1, 1), (-1, 1) },      // Art 4: %3 −1
    };

    /// <summary>Aus <see cref="AiGruppenPflegen"/>, BEVOR der Arm (oder der alte Stand) für
    /// die Gruppe läuft: wer steht jetzt (AiSteht, beim Türgriff nicht auf der Einnahmezelle)?</summary>
    private void KiGaPruefVorher(int p, int g, AiGruppe gruppe, int zi)
    {
        KiGaPruefAbschluss();
        if (gruppe.Auftrag == null || zi < 0) return;
        int art = IstTuergriff(gruppe.Auftrag) ? 0 : gruppe.Auftrag.Kind;
        if (art is < 0 or > 4) return;
        var front = art == 0 ? CaptureCells(_entities[zi]).Front : default;
        var lauf = new KiGaLauf { P = p, G = g, Art = art };
        foreach (int i in gruppe.Einheiten)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (!AiSteht(e)) continue;
            if (art == 0 && (e.Col, e.Row) == (front.X, front.Y)) continue;
            lauf.Stehend.Add(i);
        }
        _kiGaLauf = lauf;
        _kiGaLaeufe[art]++;
        foreach (var ein in _kiGaEingriffe)
            if (ein.P == p && ein.G == g) ein.LaeufeSeitBildung++;
    }

    /// <summary>Aus <see cref="AiSend"/>/<see cref="AiWalkTo"/>: ein Befehl, gleich von wem
    /// (Arm, alter Türgriff, AiGruppeLosschicken) — solange ein Gruppenlauf offen ist.</summary>
    private void KiGaPruefSendung(int idx)
    {
        _kiGaLauf?.Befohlen.Add(idx);
    }

    /// <summary>Aus dem Arm: Befehl und Wurf (für G3).</summary>
    private void KiGaPruefBefehl(int idx, int art, int dx, int dy, bool wurf)
    {
        _kiGaBefehle[art]++;
        _kiGaLauf?.Befohlen.Add(idx);
        if (art == 0) return;
        var k = KiGaKasten;
        if (dx < k[art, 0].lo || dx > k[art, 0].hi || dy < k[art, 1].lo || dy > k[art, 1].hi) _kiGaAusserKasten[art]++;
        if (_kiGaWuerfe[art] == 0) { _kiGaWurfMin[art, 0] = _kiGaWurfMax[art, 0] = dx; _kiGaWurfMin[art, 1] = _kiGaWurfMax[art, 1] = dy; }
        _kiGaWurfMin[art, 0] = Math.Min(_kiGaWurfMin[art, 0], dx); _kiGaWurfMax[art, 0] = Math.Max(_kiGaWurfMax[art, 0], dx);
        _kiGaWurfMin[art, 1] = Math.Min(_kiGaWurfMin[art, 1], dy); _kiGaWurfMax[art, 1] = Math.Max(_kiGaWurfMax[art, 1], dy);
        if (wurf || art == 3) _kiGaWuerfe[art]++;
    }

    /// <summary>Den offenen Gruppenlauf auswerten (am Ende von <see cref="AiGruppenPflegen"/>
    /// und vor dem nächsten).</summary>
    private void KiGaPruefAbschluss()
    {
        var l = _kiGaLauf;
        if (l == null) return;
        _kiGaLauf = null;
        foreach (int i in l.Stehend)
        {
            _kiGaStehend[l.Art]++;
            if (l.Befohlen.Contains(i)) _kiGaStehendBefohlen[l.Art]++;
            if (i < _entities.Count && !_entities[i].Dead && AiSteht(_entities[i])) _kiGaNachherStehend[l.Art]++;
        }
        foreach (var ein in _kiGaEingriffe)
        {
            if (ein.Urteil != 0 || ein.Takt < 0 || ein.P != l.P || ein.G != l.G) continue;
            if (!l.Stehend.Contains(ein.Idx)) continue;     // stand nicht mehr (z. B. AiSweep) — naechster Lauf
            ein.Urteil = l.Befohlen.Contains(ein.Idx) ? 1 : -1;
            ein.Text += $", naechster Lauf bei Takt {_taktNr}: {(ein.Urteil > 0 ? "NEU BEFOHLEN" : "NICHT befohlen")}";
        }
    }

    /// <summary>Probegruppen legen (ohne Marsch, Ende 0): Art 1 c = 1 auf das nächste
    /// feindliche Gebäude, Art 2 auf die nächste feindliche Einheit, Art 4 auf die Zelle
    /// eines feindlichen Gebäudes — je zwei bewaffnete, freie Einheiten eines Rechnerspielers.</summary>
    private string KiGaGruppenLegen()
    {
        var teile = new List<string>();
        foreach (var ap in _ai)
        {
            int q = ap.Player;
            if (AiGesperrt(q)) continue;
            var frei = new List<int>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != q || !e.Mobile || e.DugIn) continue;
                if (!CanFight(e) || AiUnarmed(e) || e.AiCpu0 == 10 || e.Ukol != UkolFrei) continue;
                frei.Add(i);
            }
            if (frei.Count < 6 || _nav == null) continue;
            // die groesste Gruppe EINER Fahrklasse, und nur Ziele, zu denen ihr erstes
            // Mitglied einen Weg hat (sonst bleibt jeder Befehl ohne Wirkung)
            var klasse = frei.GroupBy(i => _entities[i].Move).OrderByDescending(x => x.Count()).First();
            frei = klasse.ToList();
            if (frei.Count < 6) continue;
            var a0 = _entities[frei[0]];
            var kand = new List<(int i, float d)>();
            for (int i = 0; i < _entities.Count; i++)
            {
                var v = _entities[i];
                if (v.IsProp || v.Dead || v.Owner is < 0 or > 7 || !AiHostile(q, v.Owner) || v.Slot < 0) continue;
                if (v.IsBuilding ? v.NoStructure : !v.Mobile) continue;
                if (!InCampaign && !KiKennt(q, v)) continue;   // bug-714: im Gefecht nur Gesehenes (sonst Umziel)
                kand.Add((i, new Vector2(v.Col - a0.Col, v.Row - a0.Row).Length()));
            }
            int bestB = -1, bestU = -1;
            foreach (var (i, _) in kand.OrderBy(x => x.d))
            {
                var v = _entities[i];
                if (v.IsBuilding ? bestB >= 0 : bestU >= 0) continue;
                var ziel = _nav.NearestFree(new Vector2I(v.Col, v.Row), a0.Move, frei[0]);
                if (ziel == null) continue;
                var weg = _nav.FindPath(new Vector2I(a0.Col, a0.Row), ziel.Value, a0.Move, frei[0]);
                if (weg == null || weg.Count == 0) continue;
                if (v.IsBuilding) bestB = i; else bestU = i;
                if (bestB >= 0 && bestU >= 0) break;
            }
            var plan = new List<(int art, int word, int second, int ziel)>();
            if (bestB >= 0) plan.Add((1, _entities[bestB].Slot, 1, bestB));
            if (bestU >= 0) plan.Add((2, _entities[bestU].Slot, 0, bestU));
            if (bestB >= 0) plan.Add((4, _entities[bestB].Col, _entities[bestB].Row, bestB));
            int k = 0;
            foreach (var (art, word, second, zielIdx) in plan)
            {
                int g = -1;
                for (int s = 0; s < 4; s++) if (_aiGruppen[q][s].Einheiten.Count == 0) { g = s; break; }
                if (g < 0 || k + 2 > frei.Count) break;
                AddMissionTarget(q, art, 9, word, second);
                var gr = _aiGruppen[q][g];
                gr.Auftrag = _missionTargets[q][^1];
                if (ResolveTarget(q, gr.Auftrag) < 0) { gr.Auftrag = null; continue; }
                for (int m = 0; m < 2; m++)
                {
                    int i = frei[k++];
                    gr.Einheiten.Add(i); _entities[i].AiCpu0 = 10; _entities[i].AiCpu1 = g;
                }
                _kiSec108[62 * (4 * q + g) + 1] = 0;        // kein Marsch: sofort Angriffsphase
                gr.Losgeschickt = false;
                _kiGaEingriffe.Add(new KiGaEingriff { P = q, G = g, Art = art });
                teile.Add($"P{q} g{g} {KiArtName[art]} -> {_entities[zielIdx].Name} ({_entities[zielIdx].Col},{_entities[zielIdx].Row})");
            }
            if (teile.Count > 0) break;
        }
        return teile.Count == 0 ? "Probegruppen: keine moeglich (kein Rechnerspieler mit 6 freien Bewaffneten und Ziel)"
                                : "Probegruppen gelegt: " + string.Join("; ", teile);
    }

    /// <summary>Der Eingriff (G4): je Probegruppe, sobald sie zwei Läufe hinter sich hat, ein
    /// Mitglied anhalten, das gerade fährt oder angreift (<see cref="AiAnhalten"/>).</summary>
    private void KiGaEingreifen()
    {
        foreach (var ein in _kiGaEingriffe)
        {
            if (ein.Takt >= 0 || ein.LaeufeSeitBildung < 2) continue;
            var gr = _aiGruppen[ein.P][ein.G];
            if (gr.Einheiten.Count == 0) { ein.Takt = -2; ein.Text = $"{KiArtName[ein.Art]} P{ein.P} g{ein.G}: Gruppe vorher aufgeloest"; continue; }
            foreach (int i in gr.Einheiten)
            {
                if (i < 0 || i >= _entities.Count) continue;
                var e = _entities[i];
                if (e.Dead || e.Ukol != UkolFrei || AiSteht(e) || !e.Mobile) continue;
                AiAnhalten(i, e);
                e.DrehWarten = 0;
                ein.Idx = i; ein.Takt = _taktNr;
                ein.Text = $"{KiArtName[ein.Art]} P{ein.P} g{ein.G}: Einheit {i} ({e.Name}) bei Takt {_taktNr} angehalten";
                break;
            }
        }
    }

    /// <summary>G3, die Würfelprobe am Ende des Laufs: je Art 1/2/4 eine Probegruppe (zwei
    /// bewaffnete Einheiten eines Rechnerspielers, NICHT in <c>_aiGruppen</c> eingetragen), deren
    /// Mitglieder vor jedem Aufruf angehalten werden — <paramref name="laeufe"/>-mal der Arm
    /// selbst. Danach endet der Lauf; die verbrauchten Würfe wirken nicht mehr.
    /// Mit <c>--ki-gruppenangriff-einmal</c> gibt es den Arm nicht: keine Probe.</summary>
    private string KiGaWuerfelprobe(int laeufe)
    {
        if (!KiGruppenangriffJeLauf) return "Wuerfelprobe: keine (kein Arm, --ki-gruppenangriff-einmal)";
        var teile = new List<string>();
        foreach (var ap in _ai)
        {
            int q = ap.Player;
            var eig = new List<int>();
            for (int i = 0; i < _entities.Count && eig.Count < 2; i++)
            {
                var e = _entities[i];
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != q || !e.Mobile || e.DugIn) continue;
                if (!CanFight(e) || AiUnarmed(e) || e.Ukol != UkolFrei) continue;
                eig.Add(i);
            }
            if (eig.Count < 2) continue;
            int bau = -1, einheit = -1;
            for (int i = 0; i < _entities.Count; i++)
            {
                var v = _entities[i];
                if (v.IsProp || v.Dead || v.Slot < 0 || v.Owner is < 0 or > 7 || !AiHostile(q, v.Owner)) continue;
                if (v.IsBuilding && !v.NoStructure && bau < 0) bau = i;
                if (!v.IsBuilding && v.Mobile && einheit < 0) einheit = i;
            }
            var arten = new List<(int art, MissionTarget t)>();
            if (bau >= 0) arten.Add((1, new MissionTarget { Kind = 1, Priority = 9, Word = _entities[bau].Slot, Second = 1 }));
            if (einheit >= 0) arten.Add((2, new MissionTarget { Kind = 2, Priority = 9, Word = _entities[einheit].Slot, Second = 0 }));
            if (bau >= 0) arten.Add((4, new MissionTarget { Kind = 4, Priority = 9, Word = _entities[bau].Col, Second = _entities[bau].Row }));
            foreach (var (art, t) in arten)
            {
                int zi = ResolveTarget(q, t);
                if (zi < 0) { teile.Add($"{KiArtName[art]}: Ziel nicht aufloesbar"); continue; }
                var gr = new AiGruppe { Auftrag = t, Losgeschickt = true };
                gr.Einheiten.AddRange(eig);
                int vorher = _kiGaWuerfe[art];
                _kiGaProbeArten.Add(art);
                for (int k = 0; k < laeufe; k++)
                {
                    foreach (int i in eig) { AiAnhalten(i, _entities[i]); _entities[i].DrehWarten = 0; }
                    KiGruppenAngriffsarm(q, 0, gr, zi);
                }
                teile.Add($"{KiArtName[art]} {_kiGaWuerfe[art] - vorher}");
            }
            foreach (int i in eig) AiAnhalten(i, _entities[i]);
            break;
        }
        return teile.Count == 0 ? "Wuerfelprobe: kein Rechnerspieler mit zwei Bewaffneten und Ziel"
                                : $"Wuerfelprobe ({laeufe} Laeufe, Mitglieder je Lauf angehalten): " + string.Join(", ", teile);
    }

    /// <summary>Der Prüfstand. Synchron wie <c>--ki-wegpunkte-check</c>.</summary>
    public string KiGruppenangriffCheck(int takte)
    {
        int m = UI.SkirmishSetup.CampaignMission;
        string ort = InCampaign ? $"K{m}" : $"Gefecht {UI.SkirmishSetup.Map}";   // bug-714
        var sb = new StringBuilder($"ki-gruppenangriff-check {ort}, {takte} Takte ({takte / SimHz} s)\n");
        sb.AppendLine($"  Schalter: --ki-gruppenangriff-einmal {KiGruppenangriffEinmal}, --gefecht-gruppenangriff-alt {GefechtGruppenangriffAlt}, " +
                      $"--ki-gruppenangriff-natur {KiGaNatur}, " +
                      $"--ki-wegpunkte-aus {KiWegpunkteAus}, --ki-produktionsfreigabe-aus {KiProduktionsfreigabeAus}; " +
                      $"KI {string.Join(",", _ai.Select(a => $"P{a.Player}"))}, Mensch P{ViewPlayer}");
        // bug-714: im Gefecht mit der Gruppen-KI W3 (sonst gibt es keine Gruppen)
        if (!InCampaign && !GefechtKiMarschAn)
            return sb.Append("ki-gruppenangriff-check: KEIN URTEIL (Gefecht ohne Gruppen-KI, --gefecht-ki-marsch-aus)").ToString();

        Array.Clear(_kiGaLaeufe); Array.Clear(_kiGaStehend); Array.Clear(_kiGaStehendBefohlen); Array.Clear(_kiGaNachherStehend);
        Array.Clear(_kiGaBefehle); Array.Clear(_kiGaAusserKasten); Array.Clear(_kiGaWuerfe); Array.Clear(_kiGaGruppenJeArt);
        Array.Clear(KiArmBefehle);
        _kiGaGesehen.Clear(); _kiGaEingriffe.Clear(); GruppenProtokoll.Clear(); _kiGaProbeArten.Clear();
        _gkSchuesse = _gkSchuesseMensch = _gkSchuesseGebaeude = _gkGruppenSchuesse = _gkGruppenSchuesseGebaeude = _gkGruppenSchuesseEinheit = 0;
        _gkErsterSchuss = _gkErsterSchussMensch = _gkErsterGruppenSchuss = -1;

        int mensch = ViewPlayer;
        int menschEinheiten0 = _entities.Count(e => !e.IsProp && !e.Dead && !e.IsBuilding && e.Owner == mensch);
        int menschGebaeude0 = _entities.Count(e => e.IsBuilding && !e.Dead && !e.NoStructure && e.Owner == mensch);
        long menschHp0 = _entities.Where(e => !e.IsProp && !e.Dead && e.Owner == mensch).Sum(e => (long)e.Hp);
        int wellen0 = _ai.Sum(a => a.Waves);
        long wuerfe0 = Simulation.Determinism.Draws;

        MissionScriptTick(0.001f);
        KiGaPruefAn = true;
        GefechtKiCheckAn = true;
        string legen = KiGaNatur ? "Probegruppen: keine (--ki-gruppenangriff-natur)" : KiGaGruppenLegen();
        int t0 = _taktNr, ersteGruppe = -1;
        for (int t = 0; t < takte; t++)
        {
            // bug-714: im Gefecht kennt die KI zu Beginn oft noch kein Ziel (Nebel) — alle
            // 5 s neu versuchen, bis die Probegruppen liegen (in der ersten Haelfte des Laufs).
            if (!KiGaNatur && !InCampaign && _kiGaEingriffe.Count == 0 && t > 0 && t % (5 * SimHz) == 0 && t < takte / 2)
                legen = KiGaGruppenLegen() + $" (bei {t / SimHz} s)";
            SimTickFuerProbe();
            if (!KiGaNatur) KiGaEingreifen();
            for (int p = 0; p < 8; p++)
                for (int g = 0; g < 4; g++)
                {
                    var gr = _aiGruppen[p][g];
                    if (gr.Einheiten.Count == 0 || gr.Auftrag == null) continue;
                    if (!_kiGaGesehen.Add((p, g, gr.Auftrag))) continue;
                    int art = IstTuergriff(gr.Auftrag) ? 0 : gr.Auftrag.Kind;
                    if (art is >= 0 and <= 4) _kiGaGruppenJeArt[art]++;
                    if (ersteGruppe < 0 && !_kiGaEingriffe.Any(x => x.P == p && x.G == g)) ersteGruppe = _taktNr;
                }
        }
        KiGaPruefAbschluss();
        GefechtKiCheckAn = false;
        int[] wuerfeLauf = (int[])_kiGaWuerfe.Clone();
        string probe = KiGaWuerfelprobe(200);
        KiGaPruefAn = false;

        int menschEinheiten1 = _entities.Count(e => !e.IsProp && !e.Dead && !e.IsBuilding && e.Owner == mensch);
        int menschGebaeude1 = _entities.Count(e => e.IsBuilding && !e.Dead && !e.NoStructure && e.Owner == mensch);
        long menschHp1 = _entities.Where(e => !e.IsProp && !e.Dead && e.Owner == mensch).Sum(e => (long)e.Hp);

        sb.AppendLine($"  {legen}");
        sb.AppendLine($"  Wuerfe im Lauf Art 1/2/4: {wuerfeLauf[1]}/{wuerfeLauf[2]}/{wuerfeLauf[4]}; {probe}");
        for (int a = 0; a <= 4; a++)
        {
            if (_kiGaLaeufe[a] == 0 && _kiGaGruppenJeArt[a] == 0) { sb.AppendLine($"  {KiArtName[a]}: keine Gruppe"); continue; }
            string wurf = a == 0 ? "kein Wurf"
                : _kiGaWuerfe[a] == 0 ? "keine Befehle mit Zelle"
                : $"Zellen {_kiGaWuerfe[a]}: x {_kiGaWurfMin[a, 0]}..{_kiGaWurfMax[a, 0]} (Soll {KiGaKasten[a, 0].lo}..{KiGaKasten[a, 0].hi}), " +
                  $"y {_kiGaWurfMin[a, 1]}..{_kiGaWurfMax[a, 1]} (Soll {KiGaKasten[a, 1].lo}..{KiGaKasten[a, 1].hi}), ausserhalb {_kiGaAusserKasten[a]}";
            sb.AppendLine($"  {KiArtName[a]}: Gruppen {_kiGaGruppenJeArt[a]}, Gruppenlaeufe {_kiGaLaeufe[a]}, stehende Mitglieder {_kiGaStehend[a]}, " +
                          $"davon neu befohlen {_kiGaStehendBefohlen[a]}, nach dem Lauf noch stehend {_kiGaNachherStehend[a]}, " +
                          $"Befehle des Arms {_kiGaBefehle[a]}; {wurf}");
        }
        sb.AppendLine("  Art 3 (sec16/17-Objekt): ResolveTarget fuehrt sec17 nicht — eine Art-3-Gruppe entsteht bei uns nie (M10: 2 Setup-Ziele)");
        foreach (var ein in _kiGaEingriffe)
            sb.AppendLine($"  Eingriff: {(ein.Text.Length > 0 ? ein.Text : $"{KiArtName[ein.Art]} P{ein.P} g{ein.G}: kein Anlass (keine zwei Laeufe mit fahrendem Mitglied)")}");
        sb.AppendLine($"  Aktivitaet: Gruppen gebildet (Takt 8) {GruppenProtokoll.Count}, erste natuerliche Gruppe {Sek(ersteGruppe, t0)}, " +
                      $"Wellen {_ai.Sum(a => a.Waves) - wellen0}; KI-Schuesse auf Feinde {_gkSchuesse}, auf den Menschen {_gkSchuesseMensch} " +
                      $"(erster {Sek(_gkErsterSchussMensch, t0)}), Gruppenschuesse {_gkGruppenSchuesse}");
        sb.AppendLine($"  Mensch P{mensch}: Einheiten {menschEinheiten0} -> {menschEinheiten1}, Gebaeude {menschGebaeude0} -> {menschGebaeude1}, " +
                      $"Trefferpunkte {menschHp0} -> {menschHp1}; Wuerfe gesamt {Simulation.Determinism.Draws - wuerfe0}");
        sb.AppendLine($"  Auftragswahl: {AmGruendeZeile()}");
        if (!InCampaign) sb.AppendLine($"  {GefechtKiZeile()}");   // bug-714
        if (GruppenProtokoll.Count > 0) sb.AppendLine($"  Gruppen gebildet: {string.Join("; ", GruppenProtokoll.Take(10))}");

        // Urteil
        int stehAngriff = _kiGaStehend[1] + _kiGaStehend[2] + _kiGaStehend[4];
        bool g1 = Enumerable.Range(0, 5).All(a => _kiGaStehendBefohlen[a] == _kiGaStehend[a]);
        bool g3 = Enumerable.Range(1, 4).All(a => _kiGaAusserKasten[a] == 0)
                  && Enumerable.Range(1, 4).All(a => _kiGaGruppenJeArt[a] == 0 || a == 3 || _kiGaBefehle[a] > 0)
                  && _kiGaProbeArten.Count > 0                                  // die Wuerfelprobe lief (bug-714:
                  && _kiGaProbeArten.All(a => _kiGaWuerfe[a] >= 60)            //  je Art, die sie pruefen konnte)
                  && (!InCampaign || new[] { 1, 2, 4 }.All(a => _kiGaWuerfe[a] >= 60))
                  && Enumerable.Range(1, 4).All(a => _kiGaWuerfe[a] < 60 || a == 3 ||
                         (_kiGaWurfMin[a, 0] == KiGaKasten[a, 0].lo && _kiGaWurfMax[a, 0] == KiGaKasten[a, 0].hi &&
                          _kiGaWurfMin[a, 1] == KiGaKasten[a, 1].lo && _kiGaWurfMax[a, 1] == KiGaKasten[a, 1].hi));
        var beurteilt = _kiGaEingriffe.Where(x => x.Urteil != 0).ToList();
        bool g4 = beurteilt.All(x => x.Urteil > 0);
        string urteil;
        if (stehAngriff == 0 && beurteilt.Count == 0)
            urteil = "KEIN URTEIL (keine Angriffsgruppe Art 1/2/4 mit stehendem Mitglied)";
        else if (!(g1 && g3 && g4))
            urteil = $"DURCHGEFALLEN ({(g1 ? "" : "G1 ")}{(g3 ? "" : "G3 ")}{(g4 ? "" : "G4")})".Replace(" )", ")");
        else urteil = KiGaNatur || beurteilt.Count > 0 ? "BESTANDEN" : "KEIN URTEIL (Eingriff kam nicht zustande)";
        sb.Append($"ki-gruppenangriff-check: {ort} Laeufe {_kiGaLaeufe.Sum()}, stehend {_kiGaStehend.Sum()} -> befohlen {_kiGaStehendBefohlen.Sum()} " +
                  $"(Angriffsarten {stehAngriff} -> {_kiGaStehendBefohlen[1] + _kiGaStehendBefohlen[2] + _kiGaStehendBefohlen[4]}), " +
                  $"Wuerfe Art1/2/4 {_kiGaWuerfe[1]}/{_kiGaWuerfe[2]}/{_kiGaWuerfe[4]} ausserhalb {_kiGaAusserKasten.Sum()}, " +
                  $"Eingriffe {beurteilt.Count(x => x.Urteil > 0)}/{beurteilt.Count}, Gruppen {GruppenProtokoll.Count}, " +
                  $"Schuesse auf den Menschen {_gkSchuesseMensch} — {urteil}");
        return sb.ToString();
    }
}
