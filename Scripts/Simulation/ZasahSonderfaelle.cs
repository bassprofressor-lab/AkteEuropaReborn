namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>DER VORSPANN VON ZASAH — drei Sonderfaelle, bevor Schaden faellt</b>
/// (gebaut 11.09.2026, seine Ansage »bau die offenen punkte auch wie im
/// original«; gelesen in <c>berichte/fireat-buendnis.md</c> 1.2 und heute
/// selbst zerlegt).
///
/// <para>Zasah <c>0x40C9A0(angreifer, ziel)</c> geht fuer Angreifer &lt; 8000
/// und Ziel &lt; 8000 (Einheit gegen Einheit, kein Fussvolk, kein Gebaeude)
/// durch diesen Vorspann:</para>
/// <code>
///   0x40CA1F  Mission == 17 und Angreifer &lt; 1000 (Spieler 0) und Ziel/1000 == 3
///             und v[0] == 0  ->  v[0] := 1                     ; Ausloeser, Schaden laeuft weiter
///   0x40CA58  Ziel untaetig, zielfrei, bewaffnet -> 0x411770   ; Gegenschuss nach 20 Takten
///   0x40CAA0  ZBRAN(Angreifer) == 14 und Klasse(Ziel) == 0
///             -> Tempo(+0x20) := Tempo/2, mindestens 2; ENDE ohne Schaden   ; »Plasma«
///   0x40CAF2  ZBRAN(Angreifer) == 3  und Klasse(Ziel) == 0
///             -> Effekt 82 an der Zielstelle, 0x4120C0: Zelle frei, Einheit
///                entfernen (0x4011B3); ENDE ohne Schaden        ; »SchallKmp.«
/// </code>
/// <para>ZBRAN ist die Bauteilzeile der Waffe (<c>Weapon − 20</c>, siehe
/// <c>WeaponRowOf</c>); die Namen stehen in <c>component_stats.json</c>:
/// Zeile 3 »SchallKmp.«, Zeile 14 »Plasma«.</para>
///
/// <para>⚠ Der Gegenschuss nach 20 Takten (0x411770) ist hier NICHT gebaut —
/// er wird gerade gelesen (<c>berichte/selbstverteidiger.md</c>).</para>
///
/// <para>Gegenschalter <c>--zasah-sonderfaelle-aus</c>. Pruefstand
/// <c>--zasah-check</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--zasah-sonderfaelle-aus</c> — der Stand vor dem 11.09.2026.</summary>
    public static bool ZasahSonderfaelleAus;

    public int PlasmaTreffer, SchallTreffer, Mission17Ausloeser;

    /// <summary>Gibt <c>true</c> zurueck, wenn der Treffer im Vorspann endet
    /// (ohne Schaden).</summary>
    private bool ZasahVorspann(Entity? shooter, int vi, Entity victim)
    {
        if (ZasahSonderfaelleAus || shooter == null || shooter.IsBuilding || shooter.IsProp) return false;
        // Ziel < 8000: eine Einheit, kein Fussvolk (10000+), kein Gebaeude (60000+)
        if (victim.IsBuilding || victim.IsProp || victim.Infantry >= 0) return false;

        // @0x40CA1F — der Ausloeser der Mission 17
        if (UI.SkirmishSetup.CampaignMission == 17 && shooter.Owner == 0 && victim.Owner == 3
            && _mscript != null && _mscript.Var(0) == 0)
        {
            _mscript.SetVar(0, 1);
            Mission17Ausloeser++;
            GD.Print("Zasah: Mission 17 — Spieler 0 trifft Spieler 3, v[0] := 1 (@0x40CA4F)");
        }

        int zbran = WeaponRowOf(shooter.Weapon);
        if (victim.GameUnitType != 0) return false;               // nur Klasse 0

        if (zbran == 14)                                           // @0x40CAA0 »Plasma«
        {
            victim.Speed = Mathf.Max(2, victim.Speed / 2);         // sar 1, Untergrenze 2
            PlasmaTreffer++;
            return true;
        }
        if (zbran == 3)                                            // @0x40CAF2 »SchallKmp.«
        {
            _effects.Add(new Effect { Pos = victim.Pos, Kind = "fire", FrameTime = 0.08f });   // push 0x52
            EinheitEntfernen(vi, victim, "SCHALLKANONE von " + LabelOf(shooter));
            SchallTreffer++;
            return true;
        }
        return false;
    }

    /// <summary>
    /// <c>0x4120C0</c> → <c>0x4011B3</c>: eine Einheit vom Feld nehmen, OHNE
    /// Tod — kein Wrack, keine Sprengung, keine Truemmer, keine Statistik. Steht
    /// sie mitten im Schritt, raeumt das Original auch die vorgemerkte Zelle
    /// (@0x4120F2..0x412123); bei uns beide Belegungen.
    /// </summary>
    private void EinheitEntfernen(int vi, Entity e, string grund)
    {
        if (TodesLog)
            GD.Print($"tod: {LabelOf(e)} (Platz {e.Slot}, Spieler {e.Owner}) auf ({e.Col},{e.Row}) "
                   + $"ENTFERNT, Grund: {grund}");
        _nav?.ClearOccupant(e.Col, e.Row, vi);
        if (e.Reserved is { } rc) _nav?.ClearOccupant(rc.X, rc.Y, vi);
        _sel.Remove(vi);
        foreach (var other in _entities)
            if (other.Target == vi) other.Target = -1;
        if (_selected == vi) SetPrimary();
        e.Hp = 0;
        e.Dead = true;
        e.DeadTime = 0;
        e.Path = null;
        e.Target = -1;
        e.Reserved = null;
    }
}
