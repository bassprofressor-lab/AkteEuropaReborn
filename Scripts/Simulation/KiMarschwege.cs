namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE MARSCHWEGE DER KI-ANGRIFFSGRUPPEN — sec108</b> (22.10.2026, Paket W der
/// Testerrunde 0.6.11, bug-621..; Lesung berichte/gruppen-wegpunkte-sec108-opus.md,
/// Bau-Abschnitt dort).
///
/// <para>Vier Teile, alle in C <i>und</i> F nachgelesen:</para>
/// <list type="number">
///   <item><b>W1 Sektoranker und Verbindungen</b> — C <c>0x41BD60</c> / F <c>0x41AF20</c>
///   (Anker, Tafel <c>0x541F60</c> / F <c>0x540FC0</c>) und C <c>0x41C6C0</c> / F
///   <c>0x41B880</c> (Verbindungen <c>0x542148</c> / F <c>0x5411A8</c>).
///   <see cref="KiSektorTafelnBauen"/>.</item>
///   <item><b>W2 Sektor-Wegsuche</b> — C <c>0x4BEA30</c> / F <c>0x4BE4E0</c>, Gefahr
///   C <c>0x4BE9A0</c> / F <c>0x4BE450</c>. <see cref="KiSektorWegsuche"/>.</item>
///   <item><b>W3 Weg schreiben</b> — C <c>0x4BCCAA…0x4BCD5A</c> / F <c>0x4BC76A</c>,
///   Zielsektor aus <c>0x4BECF0</c> (letzter gültiger Kandidat).
///   <see cref="KiWegSchreiben"/>.</item>
///   <item><b>W4 Wegpunktfahrt</b> — C <c>0x4BD5FC…0x4BD780</c> / F <c>0x4BD0BC…0x4BD240</c>,
///   Tor C <c>0x4BD18B</c> / F <c>0x4BCC4B</c>. <see cref="KiWegpunktfahrt"/>.</item>
/// </list>
///
/// <para>Gegenschalter: <c>--ki-sektoranker-alt</c>, <c>--ki-sektorweg-alt</c>,
/// <c>--ki-wegpunkte-aus</c> (das alte AiSend sofort, vollständig),
/// <c>--ki-wegziel-bester</c> (UNSERE Berichtigung), <c>--ki-spielstand-alt</c>,
/// <c>--ki-wegkasten-berichtigt</c> und <c>--ki-wegende-berichtigt</c> (beide UNSERE
/// Berichtigungen von Originalfehlern; Standard = wie das Original).</para>
/// </summary>
public partial class MapEntityLayer
{
    // ---- Gegenschalter -------------------------------------------------------

    /// <summary><c>--ki-sektoranker-alt</c>: Anker = Sektormitte, jede Nachbarschaft
    /// verbunden — eine ERKLÄRTE Setzung für den Fall, dass die Lesung von
    /// <c>0x41BD60</c> hakt.</summary>
    public static bool KiSektorankerAlt;
    /// <summary><c>--ki-sektorweg-alt</c>: die alte Zielwahl samt 8-Nachbar-Breitensuche
    /// ohne Verbindungen und Gefahr (Stand bis 0.6.10); der Marschweg folgt dann deren
    /// Vorgängern (auch schräg) — das Nullmodell für die Form.</summary>
    public static bool KiSektorwegAlt;
    /// <summary><c>--ki-wegpunkte-aus</c>: kein Marsch, keine Sammelphase — die
    /// Gruppe wird beim Bilden sofort mit AiSend/AiWalkTo losgeschickt (Stand bis 0.6.10).</summary>
    public static bool KiWegpunkteAus;
    /// <summary><c>--ki-wegziel-bester</c>: UNSERE Berichtigung — der Marschweg führt in
    /// den Sektor des GEWÄHLTEN Auftrags statt in den des letzten gültigen Kandidaten.</summary>
    public static bool KiWegzielBester;
    /// <summary><c>--ki-spielstand-alt</c>: kein KI-Zustand im Spielstand (Format 2).</summary>
    public static bool KiSpielstandAlt;
    /// <summary><c>--ki-wegkasten-berichtigt</c>: UNSERE Berichtigung des Sammelkastens
    /// (<c>loX = 24·sx − 6</c> statt der Konstante 6, kein Byteüberlauf bei Sektor 10).</summary>
    public static bool KiWegkastenBerichtigt;
    /// <summary><c>--ki-wegende-berichtigt</c>: UNSERE Berichtigung des Wegendes
    /// (Start == Ziel → keine Wegpunkte; Weiterschalten über das Ende hinaus beendet).</summary>
    public static bool KiWegendeBerichtigt;

    // ---- W1: Sektoranker und Verbindungen -----------------------------------

    /// <summary><c>0x541F60</c>: zwei Tafeln zu 121 × (x, y), Index
    /// <c>121·Tafel + 11·sx + sy</c>; <c>0xFF</c> = kein Anker. Nur Tafel 0 wird
    /// gerechnet (<c>0x41C850</c> ruft <c>0x41C6C0(0)</c>, C @0x41C874).</summary>
    private readonly byte[] _kiAnker = new byte[0x1E4];

    /// <summary><c>0x542148</c>: 121 × 2 Byte (+0 nach x+1, +1 nach y+1), dahinter die
    /// Sicherung <c>0x54223A</c> (C @0x41C87A <c>rep movsd</c> 0xF2 Byte).</summary>
    private readonly byte[] _kiVerb = new byte[0x1E4];

    private bool _kiTafelnDa;

    /// <summary>Für Prüfstand und Protokoll: Dauer und Zahl der 1×1-Suchen.</summary>
    public long KiTafelMs;
    public int KiTafelSuchen;

    /// <summary>Die neun Probepunkte, Tafel C <c>0x4F8118</c> / F <c>0x4F70F8</c>
    /// (9 × (x, y) als Wörter, aus beiden EXE gelesen).</summary>
    private static readonly (int X, int Y)[] KiAnkerProben =
    {
        (4, 4), (12, 4), (20, 4), (4, 12), (12, 12), (20, 12), (4, 20), (12, 20), (20, 20),
    };

    /// <summary>
    /// <b>Die Sektortafeln bauen</b> — der Nachbau von <c>0x41C850</c> (C) beim
    /// Kartenladen, wenn keine <c>AIxx.CWI</c> vorliegt.
    ///
    /// <para>⚠ UNSERE SETZUNG, benannt: Für die Karten mit <c>AIxx.CWI</c> (Missionen
    /// 1..15, 21, 25, 26 und NET01..08) liest das Original die Tafeln aus der Datei
    /// (<c>0x41CB00</c>, C @0x41CCDC…0x41CD06). Wir rechnen sie für JEDE Karte zur
    /// Laufzeit, wie das Original es für die übrigen tut — <c>--ki-wegpunkte-check</c>
    /// vergleicht das Ergebnis mit der Datei. Die Brückennachführung
    /// (<c>0x41CAD0</c> → <c>0x41CA40</c>, Brückensätze <c>0x542330</c>) entfällt
    /// damit: zur Laufzeit gerechnete Tafeln haben im Original auch keine Brückensätze
    /// (<c>0x41C850</c> nullt <c>0x542330</c> vorher).</para>
    ///
    /// <para>⚠ UNSERE SETZUNG: Die Suche ist die von Satz 999 (<c>push 0x3E7</c>),
    /// dessen Felder im Spiel leer sind — Klasse 0, Fahrwerk 0, also ein gewöhnliches
    /// Fahrzeug (<see cref="Simulation.NavGrid.MoveClass.Vehicle"/>). »Frei« beim Verfeinern
    /// (<c>imap == 0xFFFE</c>, C @0x41BCD2) ist bei uns »befahrbar und unbesetzt«
    /// (<see cref="Simulation.NavGrid.ImapGanzFrei"/>).</para>
    /// </summary>
    public void KiSektorTafelnBauen()
    {
        _kiTafelnDa = true;
        Array.Clear(_kiAnker);
        Array.Clear(_kiVerb);
        KiTafelSuchen = 0;
        if (_nav == null) return;
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        int w = _nav.Width, h = _nav.Height;

        if (KiSektorankerAlt)
        {
            for (int x = 0; x < SektorKante; x++)
                for (int y = 0; y < SektorKante; y++)
                {
                    int i = 2 * (11 * x + y);
                    int ax = 24 * x + 12, ay = 24 * y + 12;
                    bool da = ax < w && ay < h;
                    _kiAnker[i] = da ? (byte)ax : (byte)0xFF;
                    _kiAnker[i + 1] = da ? (byte)ay : (byte)0xFF;
                }
            for (int x = 0; x < SektorKante; x++)
                for (int y = 0; y < SektorKante; y++)
                {
                    int i = 2 * (11 * x + y);
                    if (_kiAnker[i] == 0xFF) continue;
                    if (x + 1 < SektorKante && _kiAnker[i + 22] != 0xFF) _kiVerb[i] = 1;
                    if (y + 1 < SektorKante && _kiAnker[i + 2] != 0xFF) _kiVerb[i + 1] = 1;
                }
            Array.Copy(_kiVerb, 0, _kiVerb, 0xF2, 0xF2);
            KiTafelMs = uhr.ElapsedMilliseconds;
            return;
        }

        var basis = _nav.UrBasisKarte(Simulation.NavGrid.MoveClass.Vehicle);
        int Wellen(int x0, int y0, int x1, int y1, int grenze)
        {
            KiTafelSuchen++;
            return _nav!.UrWellenBis(new Vector2I(x0, y0), new Vector2I(x1, y1), grenze, basis);
        }

        // 0x41BBD0 (F 0x41ADB0): Zelle im Sektor und mehr als 2 vom Rand.
        bool Pruefen(int x, int y, int sx, int sy)
            => x > 2 && y > 2 && x < w - 2 && y < h - 2 && x / 24 == sx && y / 24 == sy;

        // 0x41BC60: die Ringtafel 0x79A008 bis word[0x834A94] = T[10] (Radius < 10),
        // erste Zelle, die Pruefen besteht UND imap == 0xFFFE ist; sonst (0xFF, 0xFF).
        int ringBis = Simulation.NavGrid.RingTafelBis(10);
        (int x, int y, bool ok) Verfeinern(int x, int y, int sx, int sy)
        {
            for (int k = 0; k < ringBis; k++)
            {
                var (dx, dy) = Simulation.NavGrid.RingEintrag(k);
                int cx = x + dx, cy = y + dy;
                if (!Pruefen(cx, cy, sx, sy)) continue;
                if (_nav!.ImapGanzFrei(cx, cy, Simulation.NavGrid.MoveClass.Vehicle)) return (cx, cy, true);
            }
            return (0xFF, 0xFF, false);
        }

        // ---- 0x41BD60: die Anker. Aussen X (word[esp+0x1A]), innen Y (word[esp+0x18]).
        var zaehl = new int[9];
        for (int X = 0; X < SektorKante; X++)
            for (int Y = 0; Y < SektorKante; Y++)
            {
                int bx = 24 * X, by = 24 * Y;
                Array.Clear(zaehl);                                   // 0x542B00, 9 Byte
                for (int i = 0; i < 9; i++)
                {
                    var p = Verfeinern(KiAnkerProben[i].X + bx, KiAnkerProben[i].Y + by, X, Y);
                    if (!Pruefen(p.x, p.y, X, Y)) continue;              // @0x41BE5C
                    for (int j = i + 1; j < 9; j++)
                    {
                        var q = Verfeinern(KiAnkerProben[j].X + bx, KiAnkerProben[j].Y + by, X, Y);
                        if (!q.ok) continue;                             // @0x41BEAE
                        if (q.x == p.x && q.y == p.y) { zaehl[i]++; zaehl[j]++; continue; }   // @0x41BEDB
                        if (!Pruefen(q.x, q.y, X, Y)) continue;          // @0x41BEFD
                        int wl = Wellen(p.x, p.y, q.x, q.y, 50);         // Satz 999 von p nach q
                        if (wl >= 0 && wl < 50) { zaehl[i]++; zaehl[j]++; }   // @0x41BF22 cmp ax,0x32
                    }
                }
                // Wahl @0x41BF4C…0x41BFAE: 0..3 aufwaerts, dann 8..4 abwaerts, jeweils
                // `>=` — bei Gleichstand gewinnt der SPAETER gepruefte. ⚠ Der Startwert
                // 0 mit `>=` macht den Zweig »kein Anker« (si == 0xFF, @0x41BFB0) tot;
                // 0xFF kommt allein aus dem Verfeinern.
                int si = 0xFF, best = 0;
                for (int b = 0; b < 4; b++) if (zaehl[b] >= best) { si = b; best = zaehl[b]; }
                for (int b = 8; b > 3; b--) if (zaehl[b] >= best) { si = b; best = zaehl[b]; }
                int idx = 2 * (11 * X + Y);
                if (si == 0xFF) { _kiAnker[idx] = 0xFF; continue; }
                var a = Verfeinern(KiAnkerProben[si].X + bx, KiAnkerProben[si].Y + by, X, Y);
                _kiAnker[idx] = (byte)a.x;
                _kiAnker[idx + 1] = (byte)a.y;
            }

        // ---- 0x41C6C0: die Verbindungen, nur X, Y < 10 (cmp si,0xA / cmp …,0xA).
        for (int X = 0; X < 10; X++)
            for (int Y = 0; Y < 10; Y++)
            {
                int i = 2 * (11 * X + Y);
                if (_kiAnker[i] == 0xFF) continue;                         // @0x41C741
                int ax = _kiAnker[i], ay = _kiAnker[i + 1];
                if (_kiAnker[i + 22] != 0xFF)                              // (X+1, Y) @0x41C760
                {
                    int wl = Wellen(ax, ay, _kiAnker[i + 22], _kiAnker[i + 23], 80);
                    _kiVerb[i] = (byte)(wl >= 0 && wl < 80 ? 1 : 0);       // @0x41C77F cmp ax,0x50
                }
                if (_kiAnker[i + 2] != 0xFF)                               // (X, Y+1) @0x41C7A1
                {
                    int wl = Wellen(ax, ay, _kiAnker[i + 2], _kiAnker[i + 3], 80);
                    _kiVerb[i + 1] = (byte)(wl >= 0 && wl < 80 ? 1 : 0);
                }
            }
        Array.Copy(_kiVerb, 0, _kiVerb, 0xF2, 0xF2);                       // Sicherung 0x54223A
        KiTafelMs = uhr.ElapsedMilliseconds;
    }

    private void KiTafelnSicher()
    {
        if (!_kiTafelnDa) KiSektorTafelnBauen();
    }

    /// <summary>
    /// Ein Byte aus dem Speicherbereich ab <c>0x541F60</c>, wie ihn die Wegpunktfahrt
    /// liest (<c>[ebp + 0x541F60]</c>, ebp = 2·(11·sx + sy)). Mit Sektoren über 10 —
    /// sie entstehen nur beim Lesen über Platz 29 hinaus — greift das Original über die
    /// Ankertafel hinaus. Nachgebaut: Anker (0..0x1E3), Verbindungen ab 0x1E8, die
    /// Brückensätze ab 0x3D0 (bei uns leer, wie bei einer zur Laufzeit gerechneten
    /// Karte). ⚠ UNSERE SETZUNG: alles dahinter liest 0.
    /// </summary>
    private int KiAnkerSpeicher(int off)
    {
        if (off >= 0 && off < 0x1E4) return _kiAnker[off];
        if (off >= 0x1E8 && off < 0x1E8 + 0x1E4) return _kiVerb[off - 0x1E8];
        return 0;
    }

    /// <summary>Für Prüfstände: Anker (x, y) eines Sektors, 0xFF = keiner.</summary>
    public (int x, int y) KiAnker(int sx, int sy)
    {
        KiTafelnSicher();
        int i = 2 * (11 * Math.Clamp(sx, 0, 10) + Math.Clamp(sy, 0, 10));
        return (_kiAnker[i], _kiAnker[i + 1]);
    }

    /// <summary>Für Prüfstände: ist der Schritt a → b (4er-Nachbarn) offen?</summary>
    public bool KiSchrittOffen((int x, int y) a, (int x, int y) b)
    {
        KiTafelnSicher();
        int Byte(int x, int y, int k)
            => x is >= 0 and <= 10 && y is >= 0 and <= 10 ? _kiVerb[2 * (11 * x + y) + k] : 0;
        if (b.x == a.x + 1 && b.y == a.y) return Byte(a.x, a.y, 0) != 0;
        if (b.x == a.x - 1 && b.y == a.y) return Byte(b.x, b.y, 0) != 0;
        if (b.y == a.y + 1 && b.x == a.x) return Byte(a.x, a.y, 1) != 0;
        if (b.y == a.y - 1 && b.x == a.x) return Byte(b.x, b.y, 1) != 0;
        return false;
    }

    // ---- W2: die Sektor-Wegsuche 0x4BEA30 -------------------------------------

    /// <summary><c>0xB45FB0</c>: Kosten je Sektor (121 × 4 B).</summary>
    private readonly int[] _kiKosten = new int[SektorZahl];

    /// <summary><c>0xB36AA0</c>: der Code, mit dem ein Sektor zuletzt VERBESSERT wurde
    /// (0 von y−1, 1 von x−1, 2 von x+1, 3 von y+1, 4 Start). ⚠ Das Original leert die
    /// Tafel NICHT je Suche — ein unerreichter Sektor behält den Code einer früheren
    /// Suche (auch eines anderen Spielers). Genau so hier.</summary>
    private readonly byte[] _kiWegCode = new byte[SektorZahl];

    /// <summary>Nur unter <c>--ki-sektorweg-alt</c>: der Vorgänger aus der alten
    /// 8-Nachbar-Breitensuche (−1 = Start/keiner), für den Marschweg des Nullmodells.</summary>
    private readonly int[] _kiAltVorg = new int[SektorZahl];

    private readonly List<(int x, int y, int k, int code)> _kiSchlange = new();

    /// <summary>
    /// <c>0x4BE9A0</c> (F <c>0x4BE450</c>): <c>max(0, feind − (eigen + verbündet/2)/2) / 50</c>
    /// aus dem 12-Byte-Satz <c>0xB3D390 + 12·(121p + 11x + y)</c> — das ist unser
    /// <see cref="AiSektor"/> (+0 eigen, +2 verbündet, +4 feindlich; derselbe Satz wie
    /// bei »Not free attacker«, berichte/k26-freie-angreifer-fable.md). Wörter, also
    /// 16 Bit; <c>w2 &gt;&gt; 1</c> vorzeichenlos.
    /// </summary>
    private int KiGefahr(int p, int x, int y)
    {
        if (p is < 0 or > 7 || x is < 0 or > 10 || y is < 0 or > 10) return 0;
        var s = _aiRaster[p][SektorIndex(x, y)];
        int feind = s.Feind & 0xFFFF, eigen = s.Eigen & 0xFFFF, verb = (s.Verbuendet & 0xFFFF) >> 1;
        int v = feind - (eigen + verb) / 2;
        if (v < 0) v = 0;
        return v / 50;
    }

    /// <summary>
    /// <b>Die Sektor-Wegsuche</b> — C <c>0x4BEA30</c> / F <c>0x4BE4E0</c>, Befehl für
    /// Befehl:
    /// <code>
    ///   Kosten[121] := 999 999                         ; 0xF423F
    ///   Schlange := [(sx, sy, 0, 4)], Kopf 0           ; 16-B-Saetze 0xB38D50
    ///   solange Schwanz &gt; Kopf:
    ///     (x, y, k, c) := Schlange[Kopf++]
    ///     wenn Kosten[11x+y] &lt;= k: weiter           ; @0x4BEAEC jle
    ///     Kosten := k ; Code[11x+y] := c ; k += Gefahr(p, x, y)
    ///     Verb.+0 (x,y)   -&gt; (x+1, y, k, 1)          ; 0x542148
    ///     Verb.+1 (x,y)   -&gt; (x, y+1, k, 0)          ; 0x542149
    ///     y &gt; 0 und Verb.+1 (x,y−1) -&gt; (x, y−1, k, 3) ; 0x542147
    ///     x &gt; 0 und Verb.+0 (x−1,y) -&gt; (x−1, y, k, 2) ; 0x542132
    ///   je Sektor Kosten += Gefahr(p, x, y)            ; @0x4BEC10
    /// </code>
    /// <para><b>Kein Schrittpreis</b>: ein sicherer Weg kostet 0, gleich wie lang. Die
    /// Schlange ist eine reine FIFO ohne Besuchsmerker (Label-Correcting).</para>
    /// <para>⚠ UNSERE SETZUNG: die Schlange wächst bei uns unbegrenzt; im Original
    /// liegt dahinter fremder Speicher (der Schwanz ist ein Wort).</para>
    /// </summary>
    private void KiSektorWegsuche(int p, int sx, int sy)
    {
        KiTafelnSicher();
        for (int s = 0; s < SektorZahl; s++) _kiKosten[s] = 999999;
        var q = _kiSchlange;
        q.Clear();
        q.Add((sx, sy, 0, 4));
        int kopf = 0;
        while (q.Count > kopf)
        {
            var (x, y, k, code) = q[kopf++];
            int i = 11 * x + y;
            if (i < 0 || i >= SektorZahl) continue;      // UNSERE Setzung: ausserhalb der Tafel
            if (_kiKosten[i] <= k) continue;
            _kiKosten[i] = k;
            _kiWegCode[i] = (byte)code;
            k += KiGefahr(p, x, y);
            if (_kiVerb[2 * i] != 0) q.Add((x + 1, y, k, 1));
            if (_kiVerb[2 * i + 1] != 0) q.Add((x, y + 1, k, 0));
            if (y > 0 && _kiVerb[2 * i - 1] != 0) q.Add((x, y - 1, k, 3));
            if (x > 0 && _kiVerb[2 * i - 22] != 0) q.Add((x - 1, y, k, 2));
        }
        for (int x = 0; x < SektorKante; x++)
            for (int y = 0; y < SektorKante; y++)
                _kiKosten[11 * x + y] += KiGefahr(p, x, y);
        KiWegsuchen++;
    }

    /// <summary>Zähler für Prüfstände.</summary>
    public static int KiWegsuchen;

    /// <summary>
    /// <b><c>target:</c> wie im Original</b> — C <c>0x4BECF0</c> / F <c>0x4BE7A0</c>,
    /// selbst nachgelesen (22.10.2026). Drei Unterschiede zur alten Fassung
    /// (<see cref="AiZielwahlAlt"/>, unter <c>--ki-sektorweg-alt</c>):
    /// <list type="bullet">
    ///   <item>die Plätze laufen <b>aufwärts</b> 0..99 (<c>inc al / cmp al,0x64</c>
    ///   @0x4BEFBD), bei Gleichstand gewinnt der ERSTE (<c>cmp esi,ebp / jge</c>
    ///   @0x4BEFAB) — mit Gefahrkosten ist <c>po</c> oft 0, der Gleichstand also die
    ///   Regel;</item>
    ///   <item><c>min</c> beginnt bei <b>30 000</b> (<c>mov ebp,0x7530</c> @0x4BED32);
    ///   ein unerreichbarer Sektor (999 999 + Gefahr) ist damit nicht ausgeschlossen,
    ///   er braucht nur ein grosses <c>imp</c>;</item>
    ///   <item>⚠⚠ <b>(cx, cy) wird in JEDER gültigen Runde überschrieben</b>
    ///   (C <c>0x4BEED7</c>/<c>0x4BEEEA</c>), gemerkt wird beim neuen Minimum nur
    ///   <c>min</c> und der Platz — der Marschweg führt in den Sektor des LETZTEN
    ///   gültigen Kandidaten. Fehler des Originals, in C und F gleich.</item>
    /// </list>
    /// </summary>
    private (int platz, int po) AiZielwahl(int p, List<MissionTarget> liste, int startX, int startY)
    {
        _kiZielLetzt = (-1, -1);
        _kiZielBest = (-1, -1);
        if (KiSektorwegAlt) return AiZielwahlAlt(p, liste, startX, startY);

        KiSektorWegsuche(p, startX, startY);
        int best = -1, min = 30000;
        int k = 0;
        while (k < liste.Count)
        {
            // die Marken @0x4BED60…0x4BEDA5 (bug-405, A3) — vor der Gueltigkeit
            if (!ZielgruppeAlt && _aiSec110[p] != 0 && AiAuftragHatGruppe(p, liste[k]))
            {
                ZielMitGruppeUebersprungen++;
                k++;
                continue;
            }
            int idx = ResolveTarget(p, liste[k]);
            if (idx < 0) { liste.RemoveAt(k); continue; }     // Platz geleert (@0x4BEE15 …)
            var e = _entities[idx];
            if (!TuergriffAus && liste[k].Kind == 1 && Allied(p, e.Owner))
            {
                ZielwahlVerbuendetUebersprungen++;           // @0x4BEE39 jne -> naechster
                k++;
                continue;
            }
            // ⚠ AiSektorVon klemmt auf 0..10; das Original teilt nur (idiv 0x18) —
            // auf Karten bis 255 Zellen dasselbe.
            var (tx, ty) = AiSektorVon(e.Col, e.Row);
            _kiZielLetzt = (tx, ty);                          // @0x4BEED7 / @0x4BEEEA
            int imp = liste[k].Priority;
            // »IMP is 0!!!« (@0x4BEF6A) ist im Original ein Fenster und danach eine
            // Division durch Null. UNSERE Setzung: uebergehen (wie bisher).
            if (imp <= 0) { k++; continue; }
            int po = _kiKosten[11 * tx + ty] / imp;           // cdq / idiv ecx
            if (po < min) { min = po; best = k; _kiZielBest = (tx, ty); }
            k++;
        }
        return best < 0 ? (-1, 0) : (best, min);
    }

    /// <summary>Sektor des letzten gültigen Kandidaten bzw. des gewählten (für
    /// <c>--ki-wegziel-bester</c>), aus der letzten <see cref="AiZielwahl"/>.</summary>
    private (int x, int y) _kiZielLetzt = (-1, -1), _kiZielBest = (-1, -1);

    /// <summary>Die Zielwahl bis 0.6.10 — unter <c>--ki-sektorweg-alt</c>.</summary>
    private (int platz, int po) AiZielwahlAlt(int p, List<MissionTarget> liste, int startX, int startY)
    {
        AiWegekarte(startX, startY);

        int best = -1, min = int.MaxValue;
        for (int k = liste.Count - 1; k >= 0; k--)
        {
            if (!ZielgruppeAlt && _aiSec110[p] != 0 && AiAuftragHatGruppe(p, liste[k]))
            {
                ZielMitGruppeUebersprungen++;
                continue;
            }
            int idx = ResolveTarget(p, liste[k]);
            if (idx < 0) { liste.RemoveAt(k); continue; }
            int imp = liste[k].Priority;
            if (imp <= 0) continue;
            var e = _entities[idx];
            if (!TuergriffAus && liste[k].Kind == 1 && Allied(p, e.Owner))
            {
                ZielwahlVerbuendetUebersprungen++;
                continue;
            }
            var (tx, ty) = AiSektorVon(e.Col, e.Row);
            int pway = AiWegewert(tx, ty);
            if (pway == int.MaxValue) continue;
            _kiZielLetzt = (tx, ty);
            int po = pway / imp;
            if (po >= min) continue;
            min = po;
            best = k;
            _kiZielBest = (tx, ty);
        }
        return best < 0 ? (-1, 0) : (best, min);
    }

    // ---- sec108: die Wegpunkte ------------------------------------------------

    /// <summary>
    /// <b>sec108</b> — C <c>0xB3CBD0</c> / F <c>0xB3BC30</c>, 32 Sätze × 62 Byte
    /// (Satz <c>4p + g</c>): +0 Index, +1 Ende (29 oder 0), +2+2k / +3+2k Sektor x/y
    /// von Platz k = 0..29. ⭐ Als FLACHES Feld gehalten, denn das Original liest beim
    /// Weiterschalten über Platz 29 hinaus in den NÄCHSTEN Satz (siehe
    /// <see cref="KiWegpunktfahrt"/>).
    /// </summary>
    private readonly byte[] _kiSec108 = new byte[Sec108Laenge];
    public const int Sec108Laenge = 32 * 62;

    /// <summary>Ein Byte von sec108 lesen. ⚠ Hinter sec108 liegt im Original
    /// <c>0xB3D390</c> (die Sektortafel sec56); UNSERE SETZUNG: 0.</summary>
    private int KiSec108(int off) => off >= 0 && off < Sec108Laenge ? _kiSec108[off] : 0;

    public int KiIndex(int p, int g) => _kiSec108[62 * (4 * p + g)];
    public int KiEnde(int p, int g) => _kiSec108[62 * (4 * p + g) + 1];
    public (int x, int y) KiWegpunkt(int p, int g, int k)
    {
        int b = 62 * (4 * p + g) + 2 + 2 * k;
        return (KiSec108(b), KiSec108(b + 1));
    }

    /// <summary>
    /// <b>Den Weg schreiben</b> — C <c>0x4BCCAA…0x4BCD5A</c> (F <c>0x4BC76A</c>):
    /// <code>
    ///   k := 29 ; (x, y) := Zielsektor
    ///   schleife:
    ///     (x, y) == Start -&gt; Index := k+1 ; Ende := 29 ; fertig     ; @0x4BCD44
    ///     c := Code[11x+y] ; Wp[k] := (x, y) ; k--                  ; @0x4BCD14
    ///     k == −1 -&gt; fertig (Index/Ende bleiben ALT)               ; @0x4BCD0E
    ///     c: 0 y−1, 1 x−1, 2 x+1, 3 y+1, sonst bleibt (x, y)        ; Tafel 0x4BCD6C
    /// </code>
    /// <para>⚠⚠ <b>Fehler des Originals, nachgebaut:</b> Liegt das Ziel neben dem Start,
    /// ist Index 29 == Ende; bei Start == Ziel ist Index 30. In beiden Fällen schaltet
    /// die Fahrt über Platz 29 HINAUS und liest Index/Ende und Wegpunkte des NÄCHSTEN
    /// Satzes als Sektor (C @0x4BD603, @0x4BD778 <c>cmp Ende, Index / jne</c> — 30 ist
    /// nie 29). <c>--ki-wegende-berichtigt</c> = UNSERE Berichtigung: Start == Ziel ohne
    /// Wegpunkte (die Setzung des Berichts W3), und die Fahrt endet bei Index ≥ Ende.</para>
    /// </summary>
    private void KiWegSchreiben(int p, int g, int sx, int sy, int zx, int zy)
    {
        int b = 62 * (4 * p + g);
        var weg = new List<(int, int)>();
        if (KiSektorwegAlt)
        {
            // Nullmodell: der Weg der alten 8-Nachbar-Suche, rueckwaerts ueber die
            // Vorgaenger — auch schraeg.
            int k2 = 29, s = 11 * zx + zy;
            while (true)
            {
                if (s < 0 || s >= SektorZahl) return;
                int x = s / 11, y = s % 11;
                if (x == sx && y == sy) { _kiSec108[b] = (byte)(k2 + 1); _kiSec108[b + 1] = 29; break; }
                _kiSec108[b + 2 + 2 * k2] = (byte)x; _kiSec108[b + 3 + 2 * k2] = (byte)y;
                weg.Add((x, y));
                k2--;
                if (k2 == -1) break;
                s = _kiAltVorg[s];
            }
            KiWegProtokollieren(p, g, sx, sy, zx, zy, weg);
            return;
        }

        int kk = 29, cx = zx, cy = zy;
        while (true)
        {
            if (cx == sx && cy == sy)
            {
                if (KiWegendeBerichtigt && kk == 29)
                {
                    // UNSERE Berichtigung (Bericht W3): Start == Ziel -> keine Wegpunkte.
                    _kiSec108[b] = 0; _kiSec108[b + 1] = 0;
                }
                else
                {
                    _kiSec108[b] = (byte)(kk + 1);
                    _kiSec108[b + 1] = 0x1D;
                }
                break;
            }
            int i = 11 * cx + cy;
            // ⚠ UNSERE Setzung: ausserhalb der Tafel liest das Original fremden
            // Speicher; wir nehmen »bleibt stehen« (wie jeder Code > 3).
            int code = i >= 0 && i < SektorZahl ? _kiWegCode[i] : 0xFF;
            int platz = kk;
            kk--;
            _kiSec108[b + 2 + 2 * platz] = (byte)cx;
            _kiSec108[b + 3 + 2 * platz] = (byte)cy;
            weg.Add((cx & 0xFF, cy & 0xFF));
            if (kk == -1) break;                         // > 30 Schritte: Index/Ende bleiben alt
            switch (code)
            {
                case 0: cy--; break;
                case 1: cx--; break;
                case 2: cx++; break;
                case 3: cy++; break;
            }
        }
        KiWegProtokollieren(p, g, sx, sy, zx, zy, weg);
    }

    // ---- W4: die Wegpunktfahrt 0x4BD5FC ----------------------------------------

    /// <summary>
    /// <b>Die Wegpunktfahrt</b> — C <c>0x4BD5FC…0x4BD780</c> / F <c>0x4BD0BC…0x4BD240</c>,
    /// solange <c>Ende != 0</c> (Tor C <c>0x4BD18B</c>); in diesem Lauf entfallen
    /// Klang 122 und der Angriff.
    /// <code>
    ///   (sx, sy) := Wp[Index]
    ///   ax := byte(24·sx) ; loX := ax &lt;= 5 ? ax : 6      ; ⚠ Konstante 6 (@0x4BD637)
    ///   hiX := byte(ax + 30)                             ; ⚠ Byte: Sektor 10 -&gt; 14
    ///   ay := byte(24·sy) ; loY := ay &lt;= 5 ? ay : ay − 6 ; hiY := byte(ay + 30)
    ///   je Mitglied: loX &lt; x &lt; hiX und loY &lt; y &lt; hiY -&gt; drin++
    ///     sonst UKOL == 0 (oder +0x04 == 0xFF und +0x16 == 0):
    ///        ry := W&#252;rfel &amp; 7 ; rx := W&#252;rfel &amp; 7       ; 0x4013ED -&gt; 0x4C5B30, ERST y
    ///        Befehl 3: fahre(u, Anker.x + rx − 4, Anker.y + ry − 4)   ; 0x402162 -&gt; 0x4D0860
    ///   3·drin/2 &gt; Gr&#246;sse -&gt; Index++ ; Index == Ende -&gt; Ende := 0
    /// </code>
    /// <para>⚠ UNSERE SETZUNGEN: »UKOL == 0« ist <see cref="AiSteht"/>; die zweite
    /// Bedingung (<c>+0x04 == 0xFF</c> und <c>+0x16 == 0</c>) ist ungelesen und darin
    /// aufgegangen. Der Fahrbefehl geht über den Befehlsring (Befehl 3 =
    /// <see cref="Simulation.Commands.CommandOp.Move"/>), wie der des Missionsskripts.</para>
    /// </summary>
    private void KiWegpunktfahrt(int p, int g, AiGruppe gruppe)
    {
        KiTafelnSicher();
        int b = 62 * (4 * p + g);
        int index = _kiSec108[b];
        int wx = KiSec108(b + 2 + 2 * index), wy = KiSec108(b + 3 + 2 * index);
        int loX, hiX, loY, hiY;
        int ax = (24 * wx) & 0xFF, ay = (24 * wy) & 0xFF;     // imul cl, nur al
        if (KiWegkastenBerichtigt)
        {
            int rx = 24 * wx, ry = 24 * wy;                    // UNSERE Berichtigung
            loX = rx <= 5 ? rx : rx - 6; hiX = rx + 30;
            loY = ry <= 5 ? ry : ry - 6; hiY = ry + 30;
        }
        else
        {
            loX = ax <= 5 ? ax : 6;                            // ⚠ Fehler des Originals
            hiX = (ax + 30) & 0xFF;                            // ⚠ Byteueberlauf
            loY = ay <= 5 ? ay : (ay - 6) & 0xFF;
            hiY = (ay + 30) & 0xFF;
        }

        int drin = 0;
        foreach (int i in gruppe.Einheiten)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var u = _entities[i];
            int ux = u.Col & 0xFF, uy = u.Row & 0xFF;          // Satz +0x00 / +0x01 (Bytes)
            if (loX < ux && ux < hiX && loY < uy && uy < hiY) { drin++; continue; }
            if (!AiSteht(u)) continue;
            int wy8 = Simulation.Determinism.Roll(8);          // ERST y (@0x4BD717)
            int wx8 = Simulation.Determinism.Roll(8);          // DANN x (@0x4BD729)
            int ai = 2 * (11 * wx + wy);
            int zx = (KiAnkerSpeicher(ai) + wx8 - 4) & 0xFF;
            int zy = (KiAnkerSpeicher(ai + 1) + wy8 - 4) & 0xFF;
            bool ok = PostRaw(Simulation.Commands.CommandRecord.Make(
                Simulation.Commands.CommandOp.Move, (byte)Math.Clamp(u.Owner, 0, 7),
                (short)i, (short)zx, (short)zy, (short)0));
            KiWegBefehle++;
            if (KiPruefAn) KiPruefBefehl(p, g, i, wx, wy, zx, zy, ok);
        }

        int groesse = gruppe.Einheiten.Count;
        bool weiter = 3 * drin / 2 > groesse;                  // lea eax,[eax+eax*2] / sar 1
        if (KiPruefAn) KiPruefLauf(p, g, index, drin, groesse, weiter);
        if (!weiter) return;
        int neu = (index + 1) & 0xFF;
        _kiSec108[b] = (byte)neu;
        KiWegWeiter++;
        if (_kiSec108[b + 1] == neu || (KiWegendeBerichtigt && neu >= _kiSec108[b + 1]))
        {
            _kiSec108[b + 1] = 0;                              // @0x4BD780
            KiWegEnde++;
            if (KiPruefAn) KiPruefEnde(p, g);
            GD.Print($"KI P{p}: Gruppe {g} hat ihren Marschweg beendet (Index {neu}) — Angriff ab dem naechsten Lauf");
        }
    }

    /// <summary>Zähler: Fahrbefehle, Weiterschaltungen, beendete Marschwege.</summary>
    public static int KiWegBefehle, KiWegWeiter, KiWegEnde;

    /// <summary>
    /// Die Gruppe losschicken — das, was bis 0.6.10 sofort beim Bilden geschah
    /// (AiSend auf das Ziel, beim Türgriff AiWalkTo auf die Einnahmezelle). Mit
    /// Wegpunkten erst im Lauf NACH dem Ende des Marsches (das Original greift ab
    /// dem nächsten Gruppenlauf an, C <c>0x4BD193 → 0x4BD1E6</c> Sprungtafel).
    /// </summary>
    private void AiGruppeLosschicken(int p, int g, int ziel, string grund)
    {
        var gruppe = _aiGruppen[p][g];
        gruppe.Losgeschickt = true;
        if (ziel < 0 || ziel >= _entities.Count) return;
        AiPlayer? a = null;
        foreach (var x in _ai) if (x.Player == p) { a = x; break; }
        bool tuer = IstTuergriff(gruppe.Auftrag);
        var front = tuer ? CaptureCells(_entities[ziel]).Front : default;
        a?.Wave.Clear();
        foreach (int i in gruppe.Einheiten)
        {
            a?.Wave.Add(i);
            if (KiPruefAn) KiPruefAngriff(p, g, i);
            if (!tuer) AiSend(i, ziel);
            else if (AiWalkTo(i, front)) TuergriffFahrten++;
        }
        if (a != null) { a.TargetIdx = ziel; a.Waves++; }
        GD.Print($"KI P{p}: Gruppe {g} mit {gruppe.Einheiten.Count} Einheiten " +
                 (tuer ? $"zum EINNEHMEN auf Zelle ({front.X},{front.Y}) vor " : "auf ") +
                 $"{_entities[ziel].Name} bei ({_entities[ziel].Col},{_entities[ziel].Row}) — {grund}");
    }

    /// <summary>
    /// Der Wegpunkt-Teil des Gruppenlaufs, aus <see cref="AiGruppenPflegen"/> nach der
    /// Gültigkeitsprüfung. <c>true</c> = dieser Lauf ist für die Gruppe erledigt
    /// (Marsch, oder aufgelöst).
    /// </summary>
    private bool KiGruppenlaufWegpunkte(int p, int g, AiGruppe gruppe, int zi)
    {
        if (KiWegpunkteAus) return false;
        // Die Gueltigkeit Art 1 c == 0 (Tuergriff erledigt, @0x4BD095) steht im Original
        // VOR dem Wegpunkt-Tor — eine eingenommene Tuer loest die Gruppe auch mitten im
        // Marsch auf.
        if (IstTuergriff(gruppe.Auftrag) && _entities[zi].Owner == p)
        {
            TuergriffErledigt++;
            GD.Print($"KI P{p}: Gruppe {g} — {_entities[zi].Name} eingenommen, Gruppe aufgeloest (@0x4BD095)");
            AiGruppeAufloesen(p, g);
            return true;
        }
        if (_kiSec108[62 * (4 * p + g) + 1] != 0)             // @0x4BD18B
        {
            KiWegpunktfahrt(p, g, gruppe);
            return true;
        }
        return false;
    }

    /// <summary>Nach Klang 122: eine Gruppe, die ihren Marsch beendet hat, greift an.</summary>
    private void KiGruppeNachMarsch(int p, int g, AiGruppe gruppe, int zi)
    {
        if (KiWegpunkteAus || gruppe.Losgeschickt) return;
        AiGruppeLosschicken(p, g, zi, "nach dem Marsch");
    }

    /// <summary>
    /// Aus <see cref="AiMissionAttack"/>, direkt nach <c>0x4BC920</c>: den Weg schreiben
    /// und entscheiden, ob die Gruppe sofort angreift (kein Weg, oder Gegenschalter) oder
    /// marschiert. Zielsektor wie das Original: der des LETZTEN gültigen Kandidaten.
    /// </summary>
    private bool KiNachGruppeBilden(int p, int g, int sx, int sy)
    {
        if (KiWegpunkteAus)
        {
            if (KiPruefAn) KiWegProtokollieren(p, g, sx, sy, -1, -1, new List<(int, int)>());
            return false;
        }
        var z = KiWegzielBester ? _kiZielBest : _kiZielLetzt;
        if (z.x < 0) z = _kiZielBest;
        if (z.x < 0) return false;
        KiWegSchreiben(p, g, sx, sy, z.x, z.y);
        bool marsch = _kiSec108[62 * (4 * p + g) + 1] != 0;
        if (g >= 0 && g < 4) _aiGruppen[p][g].Losgeschickt = !marsch;
        return marsch;
    }

    /// <summary>
    /// <c>--ki-wegpunkte-check</c> braucht die Bildung auch, wenn die Gruppe leer blieb:
    /// das Original schreibt den Weg in jedem Fall, sobald ein Platz frei war
    /// (<c>0x4BCCAA</c> liegt hinter der Aufnahmeschleife, nicht hinter einer Prüfung).
    /// </summary>
    private int _kiLetzterPlatz = -1;

    // ---- Spielstand (W5) ----------------------------------------------------

    /// <summary>Der KI-Zustand als JSON-Teil: sec61, sec106, die Zieltafel sec69, die
    /// Gruppen sec68 (Mitglieder über die Stelle im Spielstand), sec108 und die
    /// Codetafel 0xB36AA0. ⚠ Die Anker (W1) stehen NICHT darin — sie werden beim Laden
    /// neu gerechnet, wie im Original (eigene Datei, nicht im Spielstand).</summary>
    private void KiZustandSchreiben(Core.SaveGame.Writer w, Func<int, int> stelle)
    {
        w.Raw("ki_sec108", "[" + string.Join(",", _kiSec108) + "]");
        w.Raw("ki_wegcode", "[" + string.Join(",", _kiWegCode) + "]");
        w.Raw("ki_betriebsart", "[" + string.Join(",", _aiSec61) + "]");
        w.Raw("ki_sperre", "[" + string.Join(",", _aiSec106) + "]");
        w.ArrayStart("ki_ziele");
        for (int p = 0; p < 8; p++)
            foreach (var t in _missionTargets[p])
            {
                w.ItemStart();
                w.Num("p", p).Num("kind", t.Kind).Num("prio", t.Priority).Num("word", t.Word).Num("second", t.Second);
                w.ItemEnd();
            }
        w.ArrayEnd();
        w.ArrayStart("ki_gruppen");
        for (int p = 0; p < 8; p++)
            for (int g = 0; g < 4; g++)
            {
                var gr = _aiGruppen[p][g];
                if (gr.Einheiten.Count == 0) continue;
                int auftrag = gr.Auftrag == null ? -1 : _missionTargets[p].IndexOf(gr.Auftrag);
                w.ItemStart();
                w.Num("p", p).Num("g", g).Num("auftrag", auftrag).Bool("los", gr.Losgeschickt);
                if (auftrag < 0 && gr.Auftrag != null)
                    w.Num("kind", gr.Auftrag.Kind).Num("prio", gr.Auftrag.Priority)
                     .Num("word", gr.Auftrag.Word).Num("second", gr.Auftrag.Second);
                w.Raw("members", "[" + string.Join(",", gr.Einheiten.Select(stelle).Where(s => s >= 0)) + "]");
                w.ItemEnd();
            }
        w.ArrayEnd();
    }

    /// <summary>Gegenstück zu <see cref="KiZustandSchreiben"/>. Ein Stand ohne KI-Teil
    /// (Format 1..2, oder <c>--ki-spielstand-alt</c>) lässt die KI leer wie bisher.</summary>
    private void KiZustandLesen(Godot.Collections.Dictionary<string, Variant> root, List<int> listenstelle)
    {
        Array.Clear(_kiSec108);
        for (int p = 0; p < 8; p++)
        {
            _missionTargets[p].Clear();
            for (int g = 0; g < 4; g++) { _aiGruppen[p][g].Einheiten.Clear(); _aiGruppen[p][g].Auftrag = null; _aiGruppen[p][g].Losgeschickt = true; }
        }
        if (KiSpielstandAlt || !root.ContainsKey("ki_sec108")) { KiZustandGeladen = false; return; }
        KiZustandGeladen = true;

        void Bytes(string key, byte[] ziel)
        {
            if (!root.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Array) return;
            var a = v.AsGodotArray();
            for (int i = 0; i < ziel.Length && i < a.Count; i++) ziel[i] = (byte)a[i].AsInt32();
        }
        void Ints(string key, int[] ziel)
        {
            if (!root.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Array) return;
            var a = v.AsGodotArray();
            for (int i = 0; i < ziel.Length && i < a.Count; i++) ziel[i] = a[i].AsInt32();
        }
        Bytes("ki_sec108", _kiSec108);
        Bytes("ki_wegcode", _kiWegCode);
        Ints("ki_betriebsart", _aiSec61);
        Ints("ki_sperre", _aiSec106);

        int I(Godot.Collections.Dictionary<string, Variant> d, string k, int dflt = 0)
            => d.TryGetValue(k, out var x) ? x.AsInt32() : dflt;
        if (root.TryGetValue("ki_ziele", out var zv) && zv.VariantType == Variant.Type.Array)
            foreach (var item in zv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                int p = I(d, "p", -1);
                if (p is < 0 or > 7) continue;
                _missionTargets[p].Add(new MissionTarget
                { Kind = I(d, "kind"), Priority = I(d, "prio"), Word = I(d, "word"), Second = I(d, "second") });
            }
        if (root.TryGetValue("ki_gruppen", out var gv) && gv.VariantType == Variant.Type.Array)
            foreach (var item in gv.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var d = item.AsGodotDictionary<string, Variant>();
                int p = I(d, "p", -1), g = I(d, "g", -1);
                if (p is < 0 or > 7 || g is < 0 or > 3) continue;
                var gr = _aiGruppen[p][g];
                int auftrag = I(d, "auftrag", -1);
                gr.Auftrag = auftrag >= 0 && auftrag < _missionTargets[p].Count
                    ? _missionTargets[p][auftrag]
                    : d.ContainsKey("kind")
                        ? new MissionTarget { Kind = I(d, "kind"), Priority = I(d, "prio"), Word = I(d, "word"), Second = I(d, "second") }
                        : null;
                gr.Losgeschickt = !d.TryGetValue("los", out var lv) || lv.AsBool();
                if (d.TryGetValue("members", out var mv) && mv.VariantType == Variant.Type.Array)
                    foreach (var q in mv.AsGodotArray())
                    {
                        int st = q.AsInt32();
                        if (st >= 0 && st < listenstelle.Count) gr.Einheiten.Add(listenstelle[st]);
                    }
            }
    }

    /// <summary>Hat das letzte Laden einen KI-Teil gelesen?</summary>
    public static bool KiZustandGeladen;

    /// <summary>Für <c>--spielstand-check</c>: der KI-Zustand als Prüfzahlen.</summary>
    public string KiZustandFingerabdruck()
    {
        long s108 = 0;
        for (int i = 0; i < Sec108Laenge; i++) s108 += (i % 251 + 1) * _kiSec108[i];
        int gruppen = 0, mitglieder = 0; long gzellen = 0, auftraege = 0;
        for (int p = 0; p < 8; p++)
            for (int g = 0; g < 4; g++)
            {
                var gr = _aiGruppen[p][g];
                if (gr.Einheiten.Count == 0) continue;
                gruppen++;
                if (gr.Auftrag != null)
                    auftraege += (gr.Auftrag.Kind * 7 + gr.Auftrag.Priority * 13 + gr.Auftrag.Word * 3 + gr.Auftrag.Second) * (4 * p + g + 1);
                if (!gr.Losgeschickt) auftraege += 1000003 * (4 * p + g + 1);
                foreach (int i in gr.Einheiten)
                {
                    if (i < 0 || i >= _entities.Count) continue;
                    mitglieder++;
                    gzellen += (_entities[i].Col * 7 + _entities[i].Row * 13 + _entities[i].Slot) * (4 * p + g + 1);
                }
            }
        int ziele = 0; long zpruef = 0;
        for (int p = 0; p < 8; p++)
            foreach (var t in _missionTargets[p]) { ziele++; zpruef += (p + 1) * (t.Kind * 7 + t.Priority * 13 + t.Word * 3 + t.Second); }
        long cpu = 0;
        foreach (var e in _entities) if (!e.IsProp && !e.Dead) cpu += e.AiCpu0 * 31 + e.AiCpu1;
        long ba = 0;
        for (int p = 0; p < 8; p++) ba += (p + 1) * (_aiSec61[p] * 3 + _aiSec106[p]);
        return $"KI: {gruppen} Gruppen, {mitglieder} Mitglieder (Pruefzahl {gzellen}, Auftraege {auftraege}), " +
               $"{ziele} Ziele ({zpruef}), sec108 {s108}, CPU {cpu}, Betriebsart {ba}";
    }
}
