namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE DICKE BERTHA — Gattung 3, Rumpf 138</b> (12.10.2026, bug-485/486;
/// Lesung <c>berichte/dicke-bertha-fable.md</c>, Spielermeldung aus K25: »sie buggt
/// im Boden, ist nur halb sichtbar«).
///
/// <para><b>Das Bild (bug-485, O, EXE C).</b> Der Zeichner sortiert Klasse 3 mit
/// <b>Eintragsart 3</b> ein (<c>0x43059B</c>), Verteiler <c>0x429900</c> → Tafel
/// <c>0x42BC10</c> (3 → Fall 2) → eigener Arm <c>0x42AAD9</c>:</para>
/// <code>
/// 0x42AB42/0x42AB75  Blit [0x7A4FF0 + 4·(OT_PODV + word[0x77C962])]   Koerper = Teil 60
/// 0x42ABB5/0x42ABDA  Blit [0x7A4FF0 + 4·(OT_HLAV + word[0x77C966])]   Turm    = Teil 61
///                    SELBES x, SELBES y — keine Montagetafel, keine Hangpose
/// 0x430598/0x430590  Punkt = Fahrzeugpunkt + (4, 4)
/// </code>
/// <para><c>0x77C962 = 0x77C872 + 4·60</c>, <c>0x77C966 = … + 4·61</c>: die Teile sind
/// FEST einkodiert. ROBO.CWR Teil 60 = 16 Bilder ab 3888 (71×49…55 px), Teil 61 =
/// 16 Bilder ab 3904 — also <b>16 Blicke</b>, OT_PODV/OT_HLAV in Sechzehnteln. Bei uns
/// war <c>hull/138</c> LEER (Rumpftyp 138 hat kein Fahrwerksbauteil, Teil 60 wurde
/// nie ausgegeben) und <c>composed/138_61</c> nur der Turm, um <c>[0,−12]</c>
/// gerueckt — der Spieler sah einen schwebenden Raketenblock ohne Rumpf.
/// Gegenschalter <c>--bertha-bild-alt</c>.</para>
///
/// <para><b>Der Rumpf (bug-486)</b> steht in <see cref="Simulation.NavGrid.HullSide"/>:
/// 2x2 wie im Can_go-Arm 0x406419. Gegenschalter <c>--bertha-rumpf-alt</c>.</para>
///
/// <para>Pruefstand <c>--bertha-check[=Sekunden]</c> (K25, kopflos), siehe
/// <see cref="BerthaCheckBericht"/>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Der einzige Rumpftyp der Gattung 3 auf allen 44 Karten (K25, K32,
    /// K33, DM 3/5/6/7/10 — zehn Saetze, alle 138/60/61/139).</summary>
    public const int BerthaRumpf = 138;

    /// <summary>Die zwei ROBO-Teile, die der MM-Arm fest benutzt (0x77C962/0x77C966).</summary>
    public const int BerthaTeilKoerper = 60, BerthaTeilTurm = 61;

    /// <summary>Entwurfstafel sec47 Zeile 99, Name bei +0x08: VA <c>0x51DFF2</c>
    /// (= 0x51CE20 + 99·46 + 8); Enzyklopaedie Seite 95 traegt denselben Titel.</summary>
    public const string BerthaName = "Dicke Bertha";

    /// <summary>⚠ UNSERE Setzung: Aufsatz 61 hat keine Waffenzeile, und welchen
    /// Namen das Original im Infofenster zeigt, ist nicht gelesen. Der Text folgt
    /// der Enzyklopaedie Seite 95 (»Raketenwerfer … Hochgeschwindigkeits-
    /// maschinengewehre«). Statt »BAUTEIL 61«.</summary>
    public const string BerthaWaffe = "Raketenwerfer + MG";

    /// <summary>Der Zuschlag des Klasse-3-Eintrags gegen den Fahrzeugpunkt,
    /// <c>lea ebx,[ecx+4]</c> @0x430598 und <c>lea edi,[esi+4]</c> @0x430590.</summary>
    public static readonly Vector2 BerthaVersatz = new(4, 4);

    /// <summary><c>--bertha-bild-alt</c> — Stand vor bug-485: 8 Blicke, kein Koerper,
    /// Turm allein aus composed/138_61; Satzblick <c>&amp; 7</c>; Name aus der Stufe.</summary>
    public static bool BerthaBildAlt;

    /// <summary><c>--bertha-check[=s]</c> — der Pruefstand; s = Sekunden bis zum
    /// Bericht (Vorgabe 115, also mit <c>--quit-after=125</c> starten).</summary>
    public static bool BerthaCheck;
    public static int BerthaCheckEnde = 115;

    /// <summary>Wie oft die Bertha ueber ihren eigenen Arm gezeichnet wurde.</summary>
    public static int BerthaGezeichnet;

    private static bool IstBertha(Entity e) => e.GameUnitType == 3 && !BerthaBildAlt;

    /// <summary>Wo das Bild liegt: erst der eigene Satz (<c>hull/138</c> bzw.
    /// <c>turret/61</c>, f0…f15 nach dem Neuexport), sonst die Teilebank
    /// <c>part/60</c> / <c>part/60/b1</c> — dieselben Bilder (Basis + Blick), die
    /// es seit dem 15.08. ohnehin gibt. Gibt (Satz, Schluessel, Nummer) zurueck.</summary>
    private static (string Set, string Key, int F)[] BerthaQuellen(bool koerper, int f16)
    {
        f16 = ((f16 % 16) + 16) % 16;
        int teil = koerper ? BerthaTeilKoerper : BerthaTeilTurm;
        return new[]
        {
            koerper ? ("hull", BerthaRumpf.ToString(), f16) : ("turret", BerthaTeilTurm.ToString(), f16),
            ("part", f16 < 8 ? teil.ToString() : $"{teil}/b1", f16 & 7),
        };
    }

    /// <summary>⚠⚠ Gilt eine Quelle? Der eigene Satz nur aus dem EINGESPIELTEN
    /// Inhalt (<c>user://</c>): unter <c>res://Assets/Legacy/Units/hull/138/</c>
    /// liegen acht LEERE 94-Byte-Bilder (Export vom 06.08.), und
    /// <see cref="Core.Content.Path"/> faellt dorthin durch, wenn user:// nichts hat —
    /// dieselbe Falle wie bug-399. Der erste Pruefstandlauf hat genau sie gezeigt
    /// (»8 aus hull/138«, Koerper unsichtbar).</summary>
    private static bool BerthaQuelleGilt(string set, string key, int f)
    {
        string p = Core.Content.Path($"Units/{set}/{key}/f{f}.png");
        if (set == "part") return FileAccess.FileExists(p) || ResourceLoader.Exists(p);
        return p.StartsWith(Core.Content.UserRoot) && FileAccess.FileExists(p);
    }

    private Texture2D? BerthaBild(bool koerper, int f16)
    {
        foreach (var (set, key, f) in BerthaQuellen(koerper, f16))
        {
            if (!BerthaQuelleGilt(set, key, f)) continue;
            var t = LoadUnitPart(set, key, f);
            if (t != null) return t;
        }
        return null;
    }

    /// <summary>Welcher der 16 Turmblicke gezeichnet wird: der Kartenwert, solange
    /// der Turm noch nicht gedreht hat, sonst <c>AimFacing·2</c> (0x405148 vergleicht
    /// OT_HLAV gegen <c>wunsch·2</c>), ohne Turmziel der Rumpfblick.</summary>
    private static int BerthaTurmBild(Entity e)
    {
        if (e.Turm16 >= 0 && e.AimFacing == e.Turm16 / 2) return e.Turm16;
        e.Turm16 = -1;
        return e.AimFacing >= 0 ? ((e.AimFacing % 8) + 8) % 8 * 2 : ((e.Facing % 16) + 16) % 16;
    }

    /// <summary>Der MM-Arm 0x42AAD9: Koerper, dann Turm, beide am selben Punkt.
    /// Liefert false, wenn kein Koerperbild da ist — dann zeichnet der alte Weg.</summary>
    private bool BerthaZeichnen(Entity e, Vector2 picC)
    {
        var koerper = BerthaBild(true, e.Facing);
        if (koerper == null) return false;
        var at = picC - EinheitenAnker(e) + BerthaVersatz;
        DrawTexture(Parteifarbe(koerper, e.Owner), at);
        var turm = BerthaBild(false, BerthaTurmBild(e));
        if (turm != null) DrawTexture(Parteifarbe(turm, e.Owner), at);
        BerthaGezeichnet++;
        return true;
    }

    // ======================= PRUEFSTAND --bertha-check ========================

    private int _berthaIdx = -2, _berthaLetzteSek = -1;
    private int _berthaStartBlick = -1, _berthaStartTurm = -1;
    private Vector2I _berthaStart;
    private readonly List<(int S, int C, int R)> _berthaSpur = new();
    private bool _berthaFertig;

    /// <summary>Einmal je Sekunde Spieltakt: die Zelle der Bertha merken; bei 60 s
    /// eine Zwischenzeile, bei <see cref="BerthaCheckEnde"/> der Bericht.</summary>
    private void BerthaCheckTakt()
    {
        if (!BerthaCheck || _berthaFertig || _nav == null) return;
        int s = _taktNr / SimHz;
        if (s == _berthaLetzteSek) return;
        _berthaLetzteSek = s;
        if (_berthaIdx == -2)
        {
            _berthaIdx = -1;
            for (int i = 0; i < _entities.Count; i++)
                if (!_entities[i].IsBuilding && !_entities[i].Dead && _entities[i].GameUnitType == 3)
                { _berthaIdx = i; break; }
            if (_berthaIdx < 0)
            {
                GD.Print("bertha-check: ⚠ keine Einheit der Gattung 3 auf dieser Karte (K25 nehmen)");
                _berthaFertig = true;
                return;
            }
            var b0 = _entities[_berthaIdx];
            _berthaStart = new Vector2I(b0.Col, b0.Row);
            // Beim Laden, nicht beim ersten Pruefschritt: bis dahin hat Takt 7 sie
            // schon losgeschickt, und der Turm dreht in Fahrtrichtung (0x409F59).
            _berthaStartBlick = b0.SatzBlick;
            _berthaStartTurm = b0.SatzTurm;
        }
        var b = _entities[_berthaIdx];
        if (_berthaSpur.Count == 0 || _berthaSpur[^1].C != b.Col || _berthaSpur[^1].R != b.Row)
            _berthaSpur.Add((s, b.Col, b.Row));
        if (s == 60) GD.Print($"bertha-check: 60 s — Platz {b.Slot} auf ({b.Col},{b.Row}), " +
                              $"Sektor ({b.Col / 24},{b.Row / 24}), Abstand zum Start " +
                              $"{Mathf.Max(Mathf.Abs(b.Col - _berthaStart.X), Mathf.Abs(b.Row - _berthaStart.Y))} Zellen");
        if (s >= BerthaCheckEnde)
        {
            GD.Print(BerthaCheckBericht());
            _berthaFertig = true;
        }
    }

    /// <summary>Traegt der Boden einen Rumpf der Kante <paramref name="seite"/> mit
    /// Anker (c, r)? Nur GELAENDE (CanEnter fuer ein gewoehnliches Fahrwerk), keine
    /// Belegung — dieselbe Frage wie die Flut im Bericht §2.4.</summary>
    private bool BerthaAnkerTraegt(int c, int r, int seite)
    {
        for (int dy = 0; dy < seite; dy++)
            for (int dx = 0; dx < seite; dx++)
                if (!_nav!.CanEnter(c + dx, r + dy, Simulation.NavGrid.MoveClass.Vehicle)) return false;
        return true;
    }

    private int BerthaFlut(Vector2I von, int seite, bool flach = false)
    {
        var gesehen = new HashSet<Vector2I> { von };
        var q = new Queue<Vector2I>();
        q.Enqueue(von);
        // 4er-Flut wie im Bericht; die flache Flut mit acht Nachbarn ohne
        // Eckenregel, so wie 0x4D2A10 sucht (der Start selbst zaehlt immer).
        var dirs = flach
            ? new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1),
                      new Vector2I(1, 1), new Vector2I(-1, 1), new Vector2I(1, -1), new Vector2I(-1, -1) }
            : new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) };
        if (!flach && !BerthaAnkerTraegt(von.X, von.Y, seite)) return 0;
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var d in dirs)
            {
                var n = p + d;
                if (gesehen.Contains(n) || !BerthaAnkerTraegt(n.X, n.Y, seite)) continue;
                if (flach && !_nav!.BerthaRumpfFlach(n.X, n.Y)) continue;
                gesehen.Add(n);
                q.Enqueue(n);
            }
        }
        return gesehen.Count;
    }

    /// <summary>Sichtbarer Kasten eines Bildes, aus der DATEI gelesen (kopflos hat
    /// eine Textur kein Bild). (x0,y0,x1,y1,w,h) oder null.</summary>
    private static (int X0, int Y0, int X1, int Y1, int W, int H)? BerthaKasten(string set, string key, int f)
    {
        string path = Core.Content.Path($"Units/{set}/{key}/f{f}.png");
        if (!FileAccess.FileExists(path)) return null;
        var img = Image.LoadFromFile(path);
        if (img == null) return null;
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
                if (img.GetPixel(x, y).A > 0.3f)
                { x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x); y1 = Mathf.Max(y1, y); }
        return x1 < 0 ? null : (x0, y0, x1, y1, img.GetWidth(), img.GetHeight());
    }

    private static (string Set, string Key, int F)? BerthaDatei(bool koerper, int f16)
    {
        foreach (var q in BerthaQuellen(koerper, f16))
            if (BerthaQuelleGilt(q.Set, q.Key, q.F)) return q;
        return null;
    }

    /// <summary>
    /// <b>PRUEFSTAND bug-485/486</b> (<c>--bertha-check</c>, K25 kopflos).
    /// <list type="bullet">
    /// <item><b>Bild:</b> 16 Koerper- und 16 Turmbilder vorhanden (Quelle wird
    /// genannt), Koerperbild ≥ 60 px breit, Startblick = Satzwert 2 (Sechzehntel),
    /// Turmbild = Satzwert 5; Unterkante des Koerpers gegen die Ankerlinie.
    /// Nullmodell <c>--bertha-bild-alt</c>: kein eigener Arm, 0 Koerperbilder im
    /// Zeichenweg, Kasten des composed-Bildes endet 9 px ueber der Ankerlinie.</item>
    /// <item><b>Rumpf:</b> HullOf(Bertha) = 2; Gelaendeflut (4er) von der Startzelle
    /// mit 2x2 gegen 1x1 (Bericht: 5 892 gegen 15 170 bzw. 1x1 mit 8er-Flut); KEIN
    /// Anker der Fahrspur, auf dem der Boden kein 2x2 traegt; (93,44) nie Endlage.
    /// Nullmodell <c>--bertha-rumpf-alt</c>: Rumpf 1.</item>
    /// </list>
    /// Dazu, weil der Spieler es aus dem Let's Play anders kennt: Lage nach 60 s und
    /// am Ende, Sektor, Abstand zum Start.
    /// </summary>
    public string BerthaCheckBericht()
    {
        var sb = new System.Text.StringBuilder("bertha-check (bug-485/486):\n");
        if (_berthaIdx < 0 || _nav == null) return sb.Append("   ⚠ keine Bertha / kein Gitter\n").ToString();
        var b = _entities[_berthaIdx];
        bool arm = IstBertha(b);

        // ---- A: das Bild --------------------------------------------------
        int kn = 0, kEigen = 0, tn = 0, tEigen = 0;
        for (int f = 0; f < 16; f++)
        {
            if (BerthaDatei(true, f) is { } k) { kn++; if (k.Set == "hull") kEigen++; }
            if (BerthaDatei(false, f) is { } t) { tn++; if (t.Set == "turret") tEigen++; }
        }
        sb.Append($"   Bild: eigener Arm (Gattung 3 -> Teil 60+61) {(arm ? "JA" : "nein (--bertha-bild-alt)")}; " +
                  $"Koerper {kn}/16 Blicke ({kEigen} aus hull/138, {kn - kEigen} aus part/60), " +
                  $"Turm {tn}/16 ({tEigen} aus turret/61)\n");
        sb.Append($"   Satz: Startblick {_berthaStartBlick} (Satz OT_PODV 2 = Sechzehntel), " +
                  $"Startturm {_berthaStartTurm} (Satz OT_HLAV 5), FacingsOf(138) = {FacingsOf(BerthaRumpf)}\n");
        float anker = EinheitenAnker(b).Y;
        int breite = 0; float unterkante = float.NaN;
        if (arm && BerthaDatei(true, 2) is { } kd && BerthaKasten(kd.Set, kd.Key, kd.F) is { } kk)
        {
            breite = kk.X1 - kk.X0 + 1;
            unterkante = kk.Y1 + 1 + BerthaVersatz.Y - anker;
            sb.Append($"   Koerper Blick 2 ({kd.Set}/{kd.Key}/f{kd.F}): sichtbar {breite}x{kk.Y1 - kk.Y0 + 1} px, " +
                      $"Kasten ({kk.X0},{kk.Y0})-({kk.X1},{kk.Y1}) auf {kk.W}x{kk.H}, +4/+4; " +
                      $"Unterkante {unterkante:+0;-0;0} px gegen die Ankerlinie {anker:0}\n");
        }
        else if (BerthaKasten("turret", b.Weapon.ToString(), 2) is { } ck)
        {
            var mo = TurretOffset(b.UnitType, b.Col, b.Row, 2, b);
            unterkante = ck.Y1 + 1 + mo.Y - anker;
            sb.Append($"   ALTER Weg: unsichtbarer Rumpf + turret/{b.Weapon}/f2 am Montagepunkt ({mo.X:0},{mo.Y:0}): " +
                      $"sichtbar {ck.X1 - ck.X0 + 1}x{ck.Y1 - ck.Y0 + 1} px; Unterkante {unterkante:+0;-0;0} px " +
                      $"gegen die Ankerlinie {anker:0} — KEIN Koerper\n");
        }
        bool bildOk = arm && kn == 16 && tn == 16 && breite >= 60
                      && _berthaStartBlick == 2 && _berthaStartTurm == 5;
        sb.Append($"   → Bild {(bildOk ? "BESTANDEN" : "⚠ DURCHGEFALLEN")}" +
                  $"{(BerthaBildAlt ? " (unter --bertha-bild-alt: muss durchfallen)" : "")}\n");

        // ---- B: der Rumpf -------------------------------------------------
        int seite = _nav.HullOf(_berthaIdx);
        int flut2 = BerthaFlut(_berthaStart, 2), flut1 = BerthaFlut(_berthaStart, 1);
        int flutFlach = BerthaFlut(_berthaStart, 2, flach: true);
        int schlecht = 0, hang = 0; var schlechte = new List<string>();
        foreach (var (_, c, r) in _berthaSpur)
        {
            if (!BerthaAnkerTraegt(c, r, 2)) { schlecht++; if (schlechte.Count < 6) schlechte.Add($"({c},{r})"); }
            if (!_nav.BerthaRumpfFlach(c, r)) hang++;
        }
        bool hangRegel = !Simulation.NavGrid.BerthaHangAlt && seite == 2;
        bool ende9344 = b.Col == 93 && b.Row == 44;
        sb.Append($"   Rumpf: HullOf = {seite}x{seite} (Gattung {b.GameUnitType}), FootW/H {b.FootW}x{b.FootH}; " +
                  $"Gelaendeflut (4er) von ({_berthaStart.X},{_berthaStart.Y}): 2x2 {flut2} Anker, 1x1 {flut1} Zellen; " +
                  $"2x2 FLACH (Flag 0, 8er wie 0x4D2A10): {flutFlach} Anker\n");
        sb.Append($"   Spur: {_berthaSpur.Count} Zellen, davon {schlecht} OHNE 2x2-Boden" +
                  (schlechte.Count > 0 ? " " + string.Join(" ", schlechte) : "") +
                  $", {hang} auf Hangflag ≠ 0 (Planung {(hangRegel ? "flach wie 0x4D1620" : "ohne Hangsperre")})" +
                  $"; Endlage (93,44)? {(ende9344 ? "JA" : "nein")}\n");
        var spur = new System.Text.StringBuilder();
        int letzte = -100;
        foreach (var (s, c, r) in _berthaSpur)
            if (s - letzte >= 5) { spur.Append($" ({c},{r})@{s}s"); letzte = s; }
        if (_berthaSpur.Count > 0) spur.Append($" … ({_berthaSpur[^1].C},{_berthaSpur[^1].R})@{_berthaSpur[^1].S}s");
        sb.Append($"   Zellspur:{spur}\n");
        int abst = Mathf.Max(Mathf.Abs(b.Col - _berthaStart.X), Mathf.Abs(b.Row - _berthaStart.Y));
        sb.Append($"   Lage am Ende ({BerthaCheckEnde} s): ({b.Col},{b.Row}), Sektor ({b.Col / 24},{b.Row / 24}) " +
                  $"(Start ({_berthaStart.X / 24},{_berthaStart.Y / 24})), Abstand {abst} Zellen, " +
                  $"faze {b.Faze}, Blick {b.Facing}/16, TP {b.Hp}\n");
        bool rumpfOk = seite == 2 && b.FootW == 2 && schlecht == 0 && !ende9344 && flut2 > 0 && flut2 < flut1;
        sb.Append($"   → Rumpf {(rumpfOk ? "BESTANDEN" : "⚠ DURCHGEFALLEN")}" +
                  $"{(Simulation.NavGrid.BerthaRumpfAlt ? " (unter --bertha-rumpf-alt: muss durchfallen)" : "")}\n");
        bool hangOk = hangRegel && hang == 0;
        sb.Append($"   → Hangsperre {(hangOk ? "BESTANDEN" : "⚠ DURCHGEFALLEN")}" +
                  $"{(Simulation.NavGrid.BerthaHangAlt ? " (unter --bertha-hang-alt: muss durchfallen)" : "")}\n");
        rumpfOk &= hangOk;
        sb.Append($"   → GESAMT {(bildOk && rumpfOk ? "BESTANDEN" : "⚠ DURCHGEFALLEN")}; select-Name \"{(arm ? BerthaName : LabelOf(b.UnitType))}\", " +
                  $"Waffe \"{MountName(b)}\"");
        return sb.ToString();
    }
}
