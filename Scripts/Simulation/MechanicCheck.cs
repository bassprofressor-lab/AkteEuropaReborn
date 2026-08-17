namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--mechaniker-check</c> — <b>repariert der Mechaniker, was neben ihm steht?</b>
///
/// <para>Anlass: der Spieler meldete »die Reparatureinheit … repariert keine
/// Fahrzeuge«, und ich hatte geantwortet, das sei originalgetreu. Es war es
/// nicht — siehe <see cref="MapEntityLayer.MechanicTick"/>. Ein Prüfstand, den
/// es damals gegeben hätte, hätte die falsche Antwort sofort widerlegt.</para>
///
/// <para><b>Der Aufbau stellt seinen Fall selbst her</b>, statt auf eine Karte
/// zu hoffen, die zufällig einen beschädigten Panzer neben einem Mechaniker
/// hat: ein eigener Mechaniker wird gesetzt, daneben ein beschädigtes Fahrzeug,
/// und — das ist der Kern — <b>ein zweites, gleich beschädigtes Fahrzeug ausser
/// Reichweite</b>. Ohne diese Gegenprobe würde jede andere Reparatur im Spiel
/// (Depot, Basis, Flughafen) als Erfolg des Mechanikers durchgehen.</para>
///
/// <para><b>Gemessen wird gegen die gelesenen Zahlen:</b> vier orthogonale
/// Nachbarn, ein Punkt je 30 Originaltakte für eine Einheit, fünf für ein
/// Gebäude. Auch die DIAGONALE wird geprüft: sie darf NICHT heilen — das
/// Original ruft <c>repair_at</c> genau viermal.</para>
/// </summary>
public partial class MapEntityLayer
{
    private int _mechCheck = -1;
    private int _mechNah = -1, _mechFern = -1, _mechDiag = -1, _mechIdx = -1;
    private int _mechHp0, _mechFernHp0, _mechDiagHp0, _mechRep0;
    private long _mechSim;
    private System.Text.StringBuilder? _mechLog;

    /// <summary><c>--mechaniker-check</c> anwerfen.</summary>
    public void MechanikerCheckStart() => _mechCheck = 0;

    private void PollMechanikerCheck()
    {
        if (_mechCheck < 0) return;
        if (_mechCheck == 0) { MechStufe1(); return; }
        if (DebugTicks - _mechSim < MechWartetakte) return;
        MechStufe2();
    }

    /// <summary>So viele SIMULATIONSSCHRITTE laufen zwischen Aufbau und Urteil.
    ///
    /// <para>⚠ Hier stand erst eine 16, und der Prüfstand meldete FEHLER, obwohl
    /// die Sache in Ordnung war: <c>DebugTicks</c> zählt <b>Bilder</b>, nicht
    /// Wirtschaftstakte. 16 Bilder sind ein Viertel einer Sekunde — ein
    /// Reparaturpunkt braucht 30 Originaltakte, also knapp zwei. Gemessen wurde
    /// damit, bevor der Gegenstand überhaupt handeln konnte.</para>
    ///
    /// <para>600 Schritte sind bei 60 Bildern je Sekunde zehn Sekunden, also
    /// rund zehn Wirtschaftstakte à <see cref="TickScale"/> = 160 Originaltakte
    /// und damit fünf Punkte — deutlich über Null und weit unter dem Deckel.</para></summary>
    private const int MechWartetakte = 600;

    private void MechStufe1()
    {
        _mechCheck = -1;
        var sb = new System.Text.StringBuilder("mechaniker-check\n");

        // ---- eine freie Stelle suchen, an der ein Kreuz aus fünf Zellen passt
        int cx = -1, cy = -1;
        for (int y = 3; y < 60 && cx < 0; y++)
            for (int x = 3; x < 60; x++)
            {
                if (!MechFrei(x, y) || !MechFrei(x + 1, y) || !MechFrei(x - 1, y) ||
                    !MechFrei(x, y + 1) || !MechFrei(x, y - 1) ||
                    !MechFrei(x + 1, y + 1) || !MechFrei(x + 6, y)) continue;
                cx = x; cy = y; break;
            }
        if (cx < 0)
        { sb.Append("  KEIN URTEIL: keine freie Stelle fuer den Aufbau"); GD.Print(sb); return; }

        int spieler = ViewPlayer;
        _mechIdx  = MechSetze(cx, cy, spieler, mechaniker: true,  hp: 40);
        _mechNah  = MechSetze(cx + 1, cy, spieler, mechaniker: false, hp: 20);
        _mechDiag = MechSetze(cx + 1, cy + 1, spieler, mechaniker: false, hp: 20);
        _mechFern = MechSetze(cx + 6, cy, spieler, mechaniker: false, hp: 20);
        if (_mechIdx < 0 || _mechNah < 0 || _mechDiag < 0 || _mechFern < 0)
        { sb.Append("  KEIN URTEIL: der Aufbau liess sich nicht setzen"); GD.Print(sb); return; }

        var m = _entities[_mechIdx];
        sb.AppendLine($"  Mechaniker auf ({m.Col},{m.Row}): Bauteilzeile {m.Part} " +
                      $"({(m.Part == 70 ? "Mechaniker" : "FALSCH — erwartet 70")}), " +
                      $"Aufsatz {m.Weapon}, Name \"{MountName(m)}\"");
        _mechHp0     = _entities[_mechNah].Hp;
        _mechDiagHp0 = _entities[_mechDiag].Hp;
        _mechFernHp0 = _entities[_mechFern].Hp;
        _mechRep0    = MechanicRepairs;
        sb.Append($"  Aufbau: nebenan ({_entities[_mechNah].Col},{_entities[_mechNah].Row}) " +
                  $"{_mechHp0} TP, diagonal {_mechDiagHp0} TP, " +
                  $"sechs Felder weiter {_mechFernHp0} TP");
        _mechLog = sb;
        _mechSim = DebugTicks;
        _mechCheck = 1;
    }

    private void MechStufe2()
    {
        _mechCheck = -1;
        var sb = _mechLog ?? new System.Text.StringBuilder();
        sb.AppendLine();

        int nah = _entities[_mechNah].Hp - _mechHp0;
        int dia = _entities[_mechDiag].Hp - _mechDiagHp0;
        int fern = _entities[_mechFern].Hp - _mechFernHp0;
        sb.AppendLine($"  nach {MechWartetakte} Simulationsschritten:");
        sb.AppendLine($"    nebenan:  +{nah} TP   {(nah > 0 ? "REPARIERT" : "⚠ NICHTS")}");
        sb.AppendLine($"    diagonal: +{dia} TP   " +
                      $"{(dia == 0 ? "richtig — die Diagonale zaehlt nicht" : "⚠ FALSCH")}");
        sb.AppendLine($"    entfernt: +{fern} TP  " +
                      $"{(fern == 0 ? "richtig — ausser Reichweite" : "⚠ FALSCH")}");
        sb.AppendLine($"    Zaehler MechanicRepairs: {MechanicRepairs - _mechRep0}");
        var mm = _entities[_mechIdx];
        sb.AppendLine($"    Mechaniker: tot={mm.Dead} prop={mm.IsProp} geb={mm.IsBuilding} " +
                      $"Ziel={mm.Target} Weg={(mm.Goal == null ? "keiner" : "gesetzt")} " +
                      $"Zeile={mm.Part} Uhr={mm.RepairTimer:0.00} Rest={mm.MechanicRest}");

        bool ok = nah > 0 && dia == 0 && fern == 0;
        // ⚠ Die Rate wird GENANNT, aber nicht zur Bedingung gemacht: sie haengt
        // daran, wie viele Wirtschaftstakte in der Wartezeit wirklich gelaufen
        // sind, und das ist bei Bildrate-getriebenen Laeufen nicht auf den Punkt
        // festzunageln. Was hier scheitern KANN, ist die Sache selbst.
        sb.Append(ok ? "  mechaniker-check: IN ORDNUNG"
                     : "  mechaniker-check: FEHLER");
        GD.Print(sb);
    }

    private bool MechFrei(int x, int y)
    {
        if (_nav == null || x < 0 || y < 0 || x >= _nav.Width || y >= _nav.Height) return false;
        if (_nav.GroundAt(x, y) != Simulation.NavGrid.Ground.Free) return false;
        foreach (var e in _entities)
            if (!e.Dead && e.Col == x && e.Row == y) return false;
        return true;
    }

    /// <summary>Setzt eine Einheit für den Prüfstand. Der Mechaniker bekommt
    /// Bauteilzeile 70 und Aufsatz 43 — die gemessene Paarung, siehe
    /// <see cref="Entity.Part"/>.</summary>
    private int MechSetze(int x, int y, int owner, bool mechaniker, int hp)
    {
        var u = new Entity
        {
            Slot = FreeRecord(owner), Col = x, Row = y, Owner = owner, Team = owner,
            UnitType = 164, Mobile = true, Elev = ElevOf(x, y),
            Hp = hp, HpMax = 100, Facing = DefaultFacing,
            Name = mechaniker ? "Mechaniker (Pruefstand)" : "Ziel (Pruefstand)",
            Weapon = mechaniker ? 43 : 0,
            Part = mechaniker ? 70 : 0,
        };
        _entities.Add(u);
        _nav?.SetOccupant(x, y, _entities.Count - 1);
        return _entities.Count - 1;
    }
}
