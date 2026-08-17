namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--m21-check</c> — <b>gewinnt Mission 21 wirklich, wenn der Spieler die
/// neun Bahnverbindungen hält?</b>
///
/// <para>Der Skriptprüfstand sagt »9 von 9 Endbedingungen erzwingbar« — das ist
/// eine <b>Analyse</b> und kein Beweis. Sie sagt, dass die Bedingungen
/// herstellbar wären, nicht dass die Regel dann feuert. Genau dazwischen liegt
/// der Fehler, den man sonst erst im Spiel bemerkt.</para>
///
/// <para>Dieser Lauf stellt den Fall <b>her</b>: er übergibt dem Spieler die
/// Endgebäude der neun Verbindungen und sieht nach, ob die Mission endet. Er
/// misst dabei <b>in beide Richtungen</b> — vorher darf keine einzige
/// Verbindung ihm gehören, nachher müssen es alle neun sein.</para>
///
/// <para>⚠ <b>Er greift ein, und das steht in seiner Ausgabe.</b> Im Spiel
/// nimmt der Spieler die Gebäude ein; hier werden sie ihm gegeben. Was
/// gemessen wird, ist die REGEL, nicht der Weg dorthin.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die neun Verbindungen aus dem Missionsblock @0x4A01F5 — sie
    /// stehen dort im Klartext und in beiden GAME.EXE gleich.</summary>
    private static readonly int[] M21Links = { 4, 7, 9, 12, 14, 17, 15, 16, 18 };

    /// <summary>Die Linie mit dieser Nummer, oder null.</summary>
    private RailLine? RailLineOf(int slot)
    {
        foreach (var l in _railLines) if (l.Slot == slot) return l;
        return null;
    }

    private int _m21Check = -1;
    private long _m21Sim;
    private System.Text.StringBuilder? _m21Log;

    /// <summary><c>--m21-check</c> anwerfen.</summary>
    public void M21CheckStart() => _m21Check = 0;

    private void PollM21Check()
    {
        if (_m21Check != 0) return;
        // ⚠ Nicht im ersten Takt: die Linien entstehen beim Kartenaufbau, und
        // wer davor misst, misst eine leere Liste und nennt es einen Befund.
        if (DebugTicks < 60) return;
        _m21Check = -1;
        var sb = new System.Text.StringBuilder("m21-check\n");
        int me = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;

        int vorher = 0, fehlend = 0;
        foreach (int n in M21Links)
        {
            if (RailLinkHeld(n, me)) vorher++;
            if (RailLineOf(n) == null) fehlend++;
        }
        sb.AppendLine($"  {M21Links.Length} Verbindungen aus dem Block; auf dieser Karte " +
                      $"fehlen {fehlend}");
        if (fehlend > 0)
        { sb.Append("  KEIN URTEIL: das ist nicht map_21"); GD.Print(sb); return; }
        sb.AppendLine($"  vorher gehalten: {vorher} von {M21Links.Length} " +
                      (vorher == 0 ? "— richtig, es ist nichts geschenkt"
                                   : "⚠ der Fall ist unsauber, es gehört schon etwas"));

        // ---- EINGRIFF: die Endgebäude übergeben ----
        var namen = new System.Collections.Generic.List<string>();
        foreach (int n in M21Links)
        {
            var l = RailLineOf(n);
            if (l == null) continue;
            foreach (int slot in new[] { l.Bud1, l.Bud2 })
                foreach (var e in _entities)
                    if (e.IsBuilding && !e.IsProp && e.Slot == slot && e.Owner != me)
                    {
                        namen.Add($"{slot}({BuildingTypeName(e.BType)} P{e.Owner}→P{me})");
                        e.Owner = e.Team = e.ShownOwner = me;
                    }
        }
        sb.AppendLine($"  ⚠ EINGRIFF: {namen.Count} Endgebäude übergeben — " +
                      string.Join(" ", namen));

        int nachher = 0;
        foreach (int n in M21Links) if (RailLinkHeld(n, me)) nachher++;
        sb.AppendLine($"  jetzt gehalten: {nachher} von {M21Links.Length} " +
                      (nachher == M21Links.Length ? "— alle" : "— FALSCH"));

        _m21Log = sb;
        _m21Sim = DebugTicks;
        _m21Check = 1;
    }

    /// <summary>Stufe 2: ein paar Takte später — ist die Mission entschieden?
    ///
    /// <para>⚠ Der Endregel-Teil eines Missionsblocks läuft im LANGSAMEN Takt
    /// (jeder hundertste, siehe <c>Script.Gate</c>). Wer im nächsten SimTick
    /// nachsieht, meldet »feuert nicht« über eine Regel, die schlicht noch
    /// nicht dran war.</para></summary>
    private void PollM21Check2()
    {
        if (_m21Check != 1 || _m21Log == null) return;
        // ⚠ `Verdict()` ist die EINE Stelle, die urteilt; sie gibt "" zurueck,
        // solange nichts entschieden ist. Ein eigener Merker daneben waere
        // eine zweite Wahrheit.
        string urteil = Verdict();
        bool fertig = urteil.Length > 0;
        if (!fertig && DebugTicks - _m21Sim < 1200) return;
        _m21Check = -1;
        var sb = _m21Log;
        sb.AppendLine($"  nach {DebugTicks - _m21Sim} Takten: " +
                      (fertig ? "die Mission ist ENTSCHIEDEN — die Regel feuert"
                              : "⚠ FALSCH: die Mission laeuft weiter"));
        if (fertig) sb.AppendLine($"    Urteil: {urteil}");
        GD.Print(sb.ToString());
    }
}
