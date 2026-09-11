namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>UEBERFAHREN, TREFFER OHNE SCHUETZEN, SPRENGUNG</b> — K2, K3 und K5 aus
/// <c>berichte/infanterie-tod-versetzt.md</c>, gebaut am 11.09.2026 auf seine
/// Ansage »K2, K3 und K5 dann mach es wie im original«.
///
/// <para><b>K2 — wer ueberfaehrt.</b> <c>Can_go</c> <c>0x4055D0</c> verzweigt
/// erst nach der Klasse, dann nach dem Fahrwerk. Nur der Arm fuer Raeder und
/// Ketten fragt <c>prejet</c>:</para>
/// <code>
///   gerader Schritt @0x405CD8:  imap == 0xFFFE          -> 2 frei
///             @0x405CFD:  prejet(imap, einheit) == 1    -> 0x412A50 ueberfahren, 2
///             @0x405D42:  sonst Freund/Feind 0x401BC2
///                           Freund -> @0x405D63 0x40109B »geh zur Seite«
///                           Feind  -> @0x405D80 0 (blockiert)
///   Hover (Fahrwerk 7) / Walker (0x11): kein prejet — Freund/Feind wie oben
///   Infanteriearm @0x406178: 0x433DF0 »Zelle hat Platz« (&lt; 9 Mann, egal wessen)
///                           -> 2 frei, er geht MIT in die Zelle
/// </code>
/// <para>⚠ UNSERE NAEHERUNG: die Neunerzelle haben wir nicht, eine Zelle traegt
/// bei uns einen Satz. Fussvolk geht darum durch fremdes Fussvolk hindurch, wie
/// es bei uns schon immer durch eigenes ging — keiner stirbt.</para>
///
/// <para><b>K3 — wie.</b> 0x412A50 ruft die Schadensroutine mit Angreifer
/// <c>40200</c> (Rang 0, Angriff 200) auf die Zelle; der Infanteriezellen-Arm
/// <c>0x40D00C</c> rechnet, selbst zerlegt:</para>
/// <code>
///   @0x40D12D  ebp = Hoehe + Verteidigung(+0x27)          ; Hoehe EINFACH
///   @0x40D186  abwehr = (30 + Rang(+0x28)/2) · ebp / 50
///   @0x40D1A6  s = (RangA + 30) · Angriff / 40 − abwehr − rand%5 + rand%5
///   @0x40D1DC  s &lt; 1 -> s = (rand%10) / 7
///   @0x40D1FE  s &lt; Energie -> abziehen, sonst Tod (0x40B3C0)
/// </code>
/// <para>⚠ Der Bericht schrieb »2·Hoehe« — die Zerlegung sagt <c>lea
/// ebp,[eax+ecx]</c>. Mit der Flagge haelt der Tote 8 + rand&amp;3 Takte in Block
/// 12 (@0x40B73A) und klingt erst danach (77 + rand%3, @0x406E4D).</para>
///
/// <para><b>K5 — die Sprengung.</b> Stirbt eine Einheit der Klasse 0, trifft
/// der Sterbe-Anleger die acht Nachbarzellen in der Reihenfolge der Tafel
/// <c>0x4F5AF0</c> — (0,1) (−1,1) (−1,0) (−1,−1) (0,−1) (1,−1) (1,0) (1,1) —
/// je mit Angreifer <c>40015</c> (@0x40B70F). Getroffen wird, was das Zellwort
/// nennt: eine Einheit (Arm <c>0x40CCBD</c>), Fussvolk (<c>0x40D00C</c>), ein
/// Gebaeude (<c>0x40D269</c>), ein Objekt (<c>0x40D3CB</c>) oder Wald
/// (<c>0x40D61D</c>). Keine Buendnispruefung fuer Angreifer &gt; 8000
/// (@0x40D04F) — eigene Nachbarn leiden mit.</para>
///
/// <para>Gegenschalter: <c>--ueberfahren-alle</c> (K2), <c>--ueberfahren-loeschen</c>
/// (K3), <c>--sprengung-ohne-nachbarn</c> (K5).</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--ueberfahren-alle</c> — der Stand vor dem 11.09.2026: jeder
    /// Mover faehrt durch Fussvolk, und jeder ueberfaehrt Feinde.</summary>
    public static bool UeberfahrenAlle;

    /// <summary><c>--ueberfahren-loeschen</c> — der Stand vor dem 11.09.2026:
    /// der Ueberfahrene wird geloescht statt getroffen, ohne Halten.</summary>
    public static bool UeberfahrenLoeschen;

    /// <summary><c>--sprengung-ohne-nachbarn</c> — der Stand vor dem 11.09.2026.</summary>
    public static bool SprengungOhneNachbarn;

    /// <summary>Die Nachbartafel <c>0x4F5AF0</c> (dx = Spalte, dy = Zeile), in
    /// der Reihenfolge des Originals.</summary>
    private static readonly (int Dx, int Dy)[] SprengNachbarn =
        { (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1) };

    // ---- K2 -------------------------------------------------------------------

    /// <summary>Was ein Mover tut, dessen naechste Zelle ein Fusssoldat haelt —
    /// die Weiche von <c>Can_go</c>, siehe Dateikopf.</summary>
    private Simulation.NavGrid.Step FussvolkImWeg(int i, Entity fahrer, Entity fuss)
    {
        Simulation.NavGrid.Step urteil;
        if (fahrer.Infantry >= 0)
            urteil = Simulation.NavGrid.Step.Free;                       // 0x433DF0: geht mit
        else if (!IsHostile(fahrer, fuss))
            urteil = Simulation.NavGrid.Step.GiveWay;                    // @0x405D63: bitten
        else if (fahrer.Move == Simulation.NavGrid.MoveClass.Vehicle)
            urteil = Simulation.NavGrid.Step.Free;                       // @0x405D1E: ueberfahren
        else
            urteil = Simulation.NavGrid.Step.Blocked;                    // Hover/Walker/Schiff: Feind sperrt

        if (_uCheckAn)
        {
            if (!_uZaehler.TryGetValue(i, out var z)) z = new int[3];
            z[urteil == Simulation.NavGrid.Step.GiveWay ? 0
              : urteil == Simulation.NavGrid.Step.Blocked ? 1 : 2]++;
            _uZaehler[i] = z;
        }
        return urteil;
    }

    // ---- K3 -------------------------------------------------------------------

    /// <summary>
    /// Der Infanteriezellen-Arm <c>0x40D00C</c> fuer EINEN Mann: Treffer mit
    /// Rang <paramref name="rangA"/> und Angriff <paramref name="angriff"/>
    /// eines Angreifers ohne Satz (Band 40000..41000). Gibt den Schaden zurueck.
    /// </summary>
    private int InfanterieZellenTreffer(int vi, Entity v, int rangA, int angriff, string grund,
                                        bool ueberfahren = false)
    {
        if (v.Dead) return 0;                                            // @0x40D0BE: UKOL > 99
        int s = InfanterieKern(rangA, angriff, v, ElevOf(v.Col, v.Row))
                - Simulation.Determinism.Roll(5) + Simulation.Determinism.Roll(5);
        if (s < 1) s = Simulation.Determinism.Roll(10) / 7;
        SpeakHit(v);                                                     // @0x40D14F, Klangsperre
        if (CheatGodMode && Cheated(v)) s = 0;
        TrefferNotieren(v, s, angriff);
        if (s < v.Hp) { v.Hp -= s; NoteEvent(v, "unter Beschuss"); return s; }
        Kill(vi, v, -1, grund, ueberfahren);
        return s;
    }

    /// <summary>Das Ende des Haltens: Klang 77 + rand%3 (@0x406E4D), dann faellt
    /// er (DeadTime laeuft ab jetzt).</summary>
    private void UeberfahrenHaltEnde(Entity e)
    {
        Audio.GameSounds.PlayAt(Audio.GameSounds.InfantryDiesPick(), e.Col, e.Row);
        _haltGemessen[e] = _taktNr - e.HaltenTakt;
    }

    private readonly Dictionary<Entity, int> _haltGemessen = new();

    // ---- K5 -------------------------------------------------------------------

    private void SprengungTrifftNachbarn(Entity v)
    {
        string grund = "SPRENGUNG von " + LabelOf(v);
        foreach (var (dx, dy) in SprengNachbarn)
            SkripttrefferZelle(v.Col + dx, v.Row + dy, 15, grund);       // @0x40B70F push 0x9C4F
    }

    /// <summary>
    /// Ein Treffer ohne Schuetzen auf EINE Zelle, wie die Schadensroutine ihn
    /// nach dem Zellwort verteilt. Angreiferrang 0, Angriff
    /// <paramref name="angriff"/> (Band 40000..41000, @0x40CC9D).
    /// </summary>
    private void SkripttrefferZelle(int c, int r, int angriff, string grund)
    {
        if (_nav == null || c < 0 || r < 0 || c >= _nav.Width || r >= _nav.Height) return;
        _sprengLog?.Add((c, r, "Zelle", -1, false));
        SkripttrefferEinheiten(c, r, angriff, grund);

        // ein Gebaeude — derselbe Weg wie ApplyMissionHits
        if (GebaeudeAufZelle(c, r) is var bi and >= 0)
        {
            var b = _entities[bi];
            int s = SkripttrefferSchaden(angriff, b.Armor);
            if (s >= b.Hp) Kill(bi, b, -1, grund);
            else { b.Hp -= s; GebaeudeStufeNachziehen(b); }
            _sprengLog?.Add((c, r, "Gebaeude " + LabelOf(b), s, b.Dead));
        }

        WaldTreffer(c, r, angriff);
        ObjektTreffer(c, r, angriff);
    }

    /// <summary>Jede EINHEIT auf der Zelle: alle Fusssoldaten (bei uns kann mehr
    /// als einer dort stehen) und das Fahrzeug der Belegung (auch der Rumpf
    /// eines Schiffs). Gibt die Zahl der Getroffenen zurueck.</summary>
    private int EinheitenAufZelle(int c, int r, System.Action<int, Entity> treffen)
    {
        int n = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry < 0 || e.Col != c || e.Row != r) continue;
            treffen(i, e); n++;
        }
        int o = _nav?.OccupantAt(c, r) ?? -1;
        if (o >= 0 && o < _entities.Count)
        {
            var e = _entities[o];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Infantry < 0) { treffen(o, e); n++; }
        }
        return n;
    }

    /// <summary>Ein Treffer ohne Schuetzen (Band 40000..41000) auf jede Einheit
    /// der Zelle — Fussvolk im Infanteriezellen-Arm, Fahrzeuge im Einheitenarm.
    /// Derselbe Weg fuer die Sprengung und die Treffer des Missionsskripts.</summary>
    private int SkripttrefferEinheiten(int c, int r, int angriff, string grund)
        => EinheitenAufZelle(c, r, (i, e) =>
        {
            int s = e.Infantry >= 0 ? InfanterieZellenTreffer(i, e, 0, angriff, grund)
                                    : EinheitSkripttreffer(i, e, angriff, grund);
            _sprengLog?.Add((c, r, (e.Infantry >= 0 ? "Fussvolk " : "Einheit ") + LabelOf(e), s, e.Dead));
        });

    /// <summary>
    /// Der Einheitenarm <c>0x40CCBD</c> mit einem Angreifer ohne Satz:
    /// dieselbe Rechnung wie <see cref="ShotCore"/>, nur mit Rang 0 und ohne
    /// Hoehe des Schuetzen (@0x40CDE0..0x40CEE3, @0x40CF8D).
    /// </summary>
    private int EinheitSkripttreffer(int vi, Entity v, int angriff, string grund)
    {
        if (v.Dead) return 0;                                            // @0x40CD7B
        int elev = ElevOf(v.Col, v.Row);
        int core = 30 * angriff / 40 - (30 + v.Rating28 / 5) * (v.Defence + 2 * elev) / 50;
        int s = core - Simulation.Determinism.Roll(5) + Simulation.Determinism.Roll(5);
        if (s < 1) s = s <= -2 ? 0 : Simulation.Determinism.Roll(10) / 3;
        SpeakHit(v);
        if (CheatGodMode && Cheated(v)) s = 0;
        TrefferNotieren(v, s, angriff);
        if (s < v.Hp) { v.Hp -= s; NoteEvent(v, "unter Beschuss"); return s; }
        Kill(vi, v, -1, grund);
        return s;
    }

    /// <summary>Fuer die Pruefstaende: jeder Treffer ohne Schuetzen, mit den
    /// Werten, die ihn bestimmt haben.</summary>
    private void TrefferNotieren(Entity v, int s, int angriff)
    {
        if (!_uCheckAn && _sprengLog == null) return;
        _trefferLog.Add((v, s, v.Hp, angriff));
    }

    private readonly List<(Entity Opfer, int Schaden, int HpVorher, int Angriff)> _trefferLog = new();
    private List<(int C, int R, string Was, int Schaden, bool Tot)>? _sprengLog;
}
