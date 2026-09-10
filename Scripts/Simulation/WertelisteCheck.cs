namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--werteliste-check</c> — <b>hat die Werteliste des Basisfensters zu jedem
/// Entwurf ihre Zahlen?</b> (10.09.2026, bug-161.)
///
/// <para>⚠⚠ Der Befund: <c>UnitStatBook.ReadSec47</c> uebersprang jeden Entwurf
/// ohne rohen Satz, und seit bug-057 schreibt der Ausfuhrweg gar keinen mehr —
/// also alle Saetze aus der EXE. Im Basisfenster stand bei einem
/// Original-Entwurf weder Energie noch A/V noch Reichweite. Der Fehler war
/// unsichtbar, weil die SELBST entworfenen Einheiten ihre Zahlen weiter zeigten
/// (die rechnet <c>ReadOwnDesigns</c> mit <c>DesignMath.Compute</c>) — und weil
/// ein kopfloser Lauf das Fenster gar nicht oeffnet.</para>
///
/// <para>Gemessen wird die ENERGIE: ein Entwurf ohne Schwanz traegt dort 0.
/// Nullmodell: <c>--werteliste-nur-roh</c> muss auf 0 fallen.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string WertelisteCheck()
    {
        var sb = new System.Text.StringBuilder("werteliste-check\n");
        int n = 0, mit = 0;
        foreach (var e in UI.UnitStatBook.All())
        {
            n++;
            if (e.Derived.Hp > 0) mit++;
        }
        sb.Append($"  {n} Entwuerfe in der Werteliste, {mit} davon mit Energie\n");
        sb.Append($"  Gegenschalter --werteliste-nur-roh: {UI.UnitStatBook.NurRoheWerteliste}\n");
        // ⚠ DIE ZAHL, AUF DIE ES ANKOMMT, IST N — nicht der Anteil mit Energie.
        // Vor der Behebung standen hier nur die SELBST entworfenen Einheiten
        // (drei), weil jeder EXE-Satz uebersprungen wurde; danach sind es die
        // EXE-Entwuerfe dazu. Ein einzelner Lauf kann das nicht beurteilen —
        // erst der Vergleich mit --werteliste-nur-roh sagt es. Darum hier kein
        // BESTANDEN/DURCHGEFALLEN, sondern die Zahl und die Ansage dazu.
        sb.Append(n == 0
                  ? "  ⚠ kein Entwurf geladen — der Lauf sagt NICHTS"
                  : UI.UnitStatBook.NurRoheWerteliste
                    ? "  (alt: nur Entwuerfe mit rohem Satz, also nur die selbst entworfenen)"
                    : $"  MESSWERT — mit dem Gegenschalter gegenzupruefen, dort muessen es"
                      + " deutlich weniger sein");
        return sb.ToString();
    }
}
