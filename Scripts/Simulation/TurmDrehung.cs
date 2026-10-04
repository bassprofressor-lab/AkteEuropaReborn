namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐ <b>TURM UND KÖRPER DREHEN GETRENNT</b> (03.10.2026, bug-406/407/408,
/// Meldungen von <b>KayelGee</b>; Lesung <c>berichte/turm-koerper-drehen-fable.md</c>).
///
/// <para><b>Der Turm</b> ist ein eigener Zustand: <see cref="Entity.AimFacing"/> ist der
/// tatsächliche Turmblick (Satz +0x03 <c>OT_HLAV</c>), <see cref="Entity.TurmWunsch"/>
/// der Wunsch +0x17 <c>OTOC_HLAVEN</c>, <see cref="Entity.TurmAussetzen"/> dessen
/// Merkbit 8. Gelesen (O, EXE C):</para>
/// <code>
/// 0x409F26  Wunsch frei (0xFF)
/// 0x409F2E    und UKOL == 2 (fährt)
/// 0x409F38    und VRSEK nicht 41..54 (Ausrüstung)
/// 0x409F51    und STRILI_NA == 0xFFFF (kein Schussziel)
/// 0x409F59  → Wunsch := Richtung(Ankerzelle → CX/CY), 40:20 (0x434300)
/// 0x409F74  Wunsch ≥ 8 → −8, diesen Takt aussetzen
/// 0x409F7C  0x405100: eine Stufe, kürzester Weg (cmp eax,4)
/// 0x409F8A  fertig → 0xFF, sonst Wunsch + 8
/// </code>
/// <para>→ eine Turmstufe je ZWEI Takte: 180° = 6 Takte, 90° = 2, 45° = 0. Der Rumpf
/// braucht für 180° neun (bug-407, Drehstelle im Fahrtakt) — der Turm ist zuerst
/// fertig, genau wie KayelGee es in K1 sieht.</para>
/// <para><b>Bei Beschuss</b> setzt die Schiessuhr nur den Wunsch zum Ziel
/// (0x40E7DA–0x40E7F0); der Schuss wartet nicht auf den Turm. Die Uhr läuft nur
/// bei NABYTO == 0 (Tor 0x40DE8A): während des Nachladens wird der Wunsch nicht
/// nachgeführt und das Ziel beim Verlassen der Reichweite erst NACH dem Nachladen
/// losgelassen (0x40E7CB) — dann zeigt der Turm wieder aufs Endziel.</para>
/// <para>Gegenschalter <c>--turm-alt</c> (Turm springt aufs Ziel, ohne Ziel klebt er
/// am Rumpf), <c>--koerperdrehung-alt</c> (Rumpf eine Stufe je Takt, Bremse nur für
/// Schiffe), <c>--gegenbefehl-alt</c> (Schrittabbruch aus der Zwischenlage).
/// Protokoll: <c>--dreh-protokoll</c> → Zeilen <c>dreh:</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--turm-alt</c> — der Stand bis 03.10.2026: Turm springt sofort aufs
    /// Ziel, ohne Ziel folgt er dem Rumpf (AimFacing = −1).</summary>
    public static bool TurmAlt;

    /// <summary><c>--koerperdrehung-alt</c> — Rumpf eine Stufe je Takt (Bodenklassen),
    /// letzte Stufe kostet einen Takt, Bremse nur bei Schiffen.</summary>
    public static bool KoerperdrehungAlt;

    /// <summary><c>--gegenbefehl-alt</c> — ein neuer Fahrbefehl verwirft den laufenden
    /// Schritt wieder (Reserved = null) und der Fahrtakt setzt aus der Zwischenlage an.</summary>
    public static bool GegenbefehlAlt;

    /// <summary><c>--dreh-protokoll</c> — je Rumpf- und Turmstufe eine Zeile <c>dreh:</c>.</summary>
    public static bool DrehProtokoll;

    /// <summary>Wie oft ein Fahrbefehl einen laufenden Schritt stehen liess (bug-408).
    /// ⚠ Ohne die Zahl ist »wirkt« nicht von »Zweig nie erreicht« zu unterscheiden.</summary>
    public int GegenbefehlOhneAbbruch;

    /// <summary>Wie viele Turmstufen gedreht wurden (alle Einheiten).</summary>
    public int TurmStufen;

    /// <summary>
    /// Hat diese Einheit einen EIGENEN Turm im Sinne von 0x405100? Waffe (VRSEK
    /// +0x0C) 1…40; Ausrüstung 41…54 folgt dem Rumpf (0x40503E), Fussvolk hat keinen.
    /// <para>⚠ UNSERE Setzung (Umfang): Schiffe und Gattung 3 (16 Turmstufen,
    /// 0x405140) bleiben beim alten Verhalten — nicht gelesen, nicht gemeldet.</para>
    /// </summary>
    private static bool TurmEigen(Entity e)
        => !TurmAlt && !e.IsBuilding && !e.IsProp && e.Infantry < 0
           && e.Move != Simulation.NavGrid.MoveClass.Ship && e.GameUnitType != 3
           && e.Weapon is >= 1 and <= 40;

    /// <summary>
    /// Eine Stufe kürzester Weg, wie 0x404FE3 (Rumpf) und 0x405205 (Turm):
    /// Schritt = −1, wenn ist &gt; soll, sonst +1; ist |ist − soll| größer als der
    /// halbe Ring, die andere Seite. ⚠ Bei genau 180° entscheidet damit »ist &gt;
    /// soll« die Drehseite — nicht wie bisher immer +1.
    /// </summary>
    internal static int StufeOriginal(int ist, int soll, int ring)
    {
        int schritt = ist > soll ? -1 : 1;
        if (Mathf.Abs(ist - soll) > ring / 2) schritt = -schritt;
        return ((ist + schritt) % ring + ring) % ring;
    }

    /// <summary>
    /// Der Turmtakt, je Einheit und Spieltakt VOR der Schiessuhr — 0x409F26…0x409F9C.
    /// </summary>
    private void TurmTakt(int i, Entity e)
    {
        if (!TurmEigen(e)) { e.TurmWunsch = -1; e.TurmAussetzen = false; e.TurmHalteZiel = -1; return; }
        // Wer noch keinen eigenen Turmblick hat (Erzeuger, Bodenangriff setzt −1),
        // fängt dort an, wo der Turm bisher gezeichnet wurde: auf dem Rumpf.
        if (e.AimFacing < 0) e.AimFacing = TurmBlick(e, e.Facing);
        // ⚠ (V) das gehaltene Ziel endet mit dem Nachladen, oder wenn ein neues da ist
        if (e.TurmHalteZiel >= 0 && (e.Cooldown <= 0 || e.Target >= 0)) e.TurmHalteZiel = -1;

        bool faehrt = e.Path != null && e.PathIdx < e.Path.Count;          // UKOL == 2
        bool schussziel = e.Target >= 0 || e.AngriffsZelle != null || e.TurmHalteZiel >= 0;
        if (e.TurmWunsch < 0 && faehrt && !schussziel)
        {
            // 0x434300 aus der ANKERZELLE, 40:20; dx = dy = 0 → der alte Blick (0x43434A)
            var d = new Vector2((e.Goal.X - e.Col) * TileW, (e.Goal.Y - e.Row) * TileH);
            e.TurmWunsch = d.LengthSquared() < 0.0001f ? e.AimFacing : DirToFacing(d);
        }
        if (e.TurmWunsch < 0) return;
        if (e.TurmAussetzen) { e.TurmAussetzen = false; return; }          // 0x409F74
        if (e.AimFacing != e.TurmWunsch)                                    // 0x405100
        {
            int vor = e.AimFacing;
            e.AimFacing = StufeOriginal(e.AimFacing, e.TurmWunsch, 8);
            TurmStufen++;
            if (DrehProtokoll) DrehZeile(e, "Turm", vor, e.AimFacing);
        }
        if (e.AimFacing == e.TurmWunsch) e.TurmWunsch = -1;                // 0x409F8A
        else e.TurmAussetzen = true;
    }

    /// <summary>
    /// Die Schiessuhr setzt den Wunsch zum Ziel (0x40E7DA–0x40E7F0), aber nur, wenn
    /// sie läuft — bei NABYTO == 0 (0x40DE8A).
    /// <para>⚠ (V) Das Original vergleicht <c>(+0x17 &amp; 7)</c> auch, wenn +0x17 = 0xFF
    /// ist (dann 7) — eine Zielrichtung 7 würde dort ohne laufenden Wunsch nie
    /// gesetzt. Nicht nachgebaut: »kein Wunsch« zählt hier als ungleich.</para>
    /// <para>⚠ UNSERE Setzung: die Richtung kommt wie bisher aus dem
    /// Bildpunktvektor zum Ziel; woraus das Original <c>[esp+0x24]</c> rechnet, ist
    /// nicht gelesen.</para>
    /// </summary>
    private static void TurmZielWunsch(Entity e, int richtung)
    {
        if (e.Cooldown > 0) return;
        if (e.AimFacing != richtung && e.TurmWunsch != richtung)
        {
            e.TurmWunsch = richtung;
            e.TurmAussetzen = false;               // `+0x17 := bl` löscht das Merkbit mit
        }
    }

    /// <summary>
    /// Ein Ziel verlässt die Reichweite: das Original lässt es erst los, wenn die
    /// Uhr wieder läuft (NABYTO == 0, 0x40DE8A → Probeschuss → 0x40E7CB). Unsere
    /// Schiessuhr lässt es sofort los; der TURM hält es bis zum Ende des Nachladens.
    /// ⚠ UNSERE Setzung (V): nur der Turm hält, die Zielwahl bleibt wie sie war.
    /// </summary>
    private static void TurmZielHalten(Entity e)
    {
        if (TurmEigen(e) && e.Cooldown > 0) e.TurmHalteZiel = e.Target;
    }

    /// <summary>Bauliste D — eine Zeile je Drehstufe (<c>--dreh-protokoll</c>).</summary>
    private void DrehZeile(Entity e, string was, int alt, int neu)
        => GD.Print($"dreh: Takt {_taktNr} Platz {e.Slot} {was} {alt}->{neu} "
                  + $"(Rumpf {e.Facing}, Turm {e.AimFacing}, Wunsch {e.TurmWunsch}, "
                  + $"Zelle ({e.Col},{e.Row}), Ziel ({e.Goal.X},{e.Goal.Y}))");
}
