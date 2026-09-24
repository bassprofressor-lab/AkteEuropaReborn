namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 23.09.2026 — <b>der Plasmawerfer wie im Original</b> (K21-Frage »Funktioniert
/// der PlasmaWerfer korrekt?«, <c>berichte/k21-plasmawerfer.md</c>). Die Wirkung
/// (Tempo halbieren, Untergrenze 2, kein Schaden, <c>0x40CAA0</c>) stimmte schon
/// (<see cref="ZasahVorspann"/>); hier stehen die drei Abweichungen daneben.
///
/// <code>
/// C 0x452376  mov cx,6 / mov ax,[0x4FA248] / cwd / idiv cx / mov [esi+0x88474E],dl
///             -> Flugbild-Block := Bildzaehler % 6 (Tafel 0x453140/0x45316C)   A
/// C 0x40DFED  mov al,[esp+0x12] / cmp al,0xE / jne weiter        ; ZBRAN 14
/// C 0x40DFFD  cmp word[+0x20 der Zieleinheit],2 / jle naechstes   ; Tempo &lt;= 2  C
/// C 0x40E069  mov al,[esp+0x12] / cmp al,0xE / je naechstes       ; Griff &gt;= 8000 B
/// </code>
/// (selbst nachgelesen). Beide Tore stehen in der SCHIESSUHR <c>0x40DDB0</c>. ⚠ Auch
/// das BEFOHLENE Ziel laeuft durch sie: @0x40DFC0 laesst eine belegte Zelle nur zu,
/// wenn UKOL == 4 und das Ziel <c>+0x6E26FE</c> genau diese ist, und danach kommt
/// dasselbe ZBRAN-14-Tor. Ein Angriffsbefehl hebt die Tore also NICHT auf.
///
/// <para>⚠ UNSERE Setzung: die Schiessuhr sucht im Original jeden Takt neu, bei uns
/// haelt eine Einheit ihr Ziel. Darum in UpdateCombat: ein SELBSTgewaehltes Ziel,
/// das durch ein Tor faellt, wird losgelassen; ein BEFOHLENES bleibt stehen, wird
/// aber nicht beschossen. Der Weg Befehl 9 → Verteiler <c>0x40C8C0</c> (fire_at)
/// ist dafuer nicht gelesen.</para>
///
/// Gegenschalter <c>--plasma-drehen-aus</c>, <c>--plasma-zielwahl-alt</c>
/// (Felder in K21Schalter.cs). Pruefstand <c>--plasma-check</c>.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die Geschossart des Plasmawerfers (Tafel <c>0x4F98E8 + 22·13</c>).</summary>
    private const int ArtPlasma = 13;

    /// <summary>Die Bauteilzeile »Plasma« (ZBRAN 14).</summary>
    private const int ZbranPlasma = 14;

    /// <summary>A: der Block des Flugbilds, oder -1, wenn es kein Plasma ist.</summary>
    private static int PlasmaBlock(Projectile p)
        => p.Art == ArtPlasma && !PlasmaDrehenAus ? Bildzaehler % 6 : -1;

    /// <summary>B + C: darf ein Plasmawerfer dieses Ziel von SELBST nehmen?</summary>
    private bool PlasmaSelbstzielVerboten(Entity e, Entity t)
    {
        if (PlasmaZielwahlAlt || WeaponRowOf(e.Weapon) != ZbranPlasma) return false;
        if (t.Infantry >= 0 || t.IsBuilding || t.IsProp) { PlasmaZielVerworfenB++; return true; }   // 0x40E069
        if (t.Speed <= 2) { PlasmaZielVerworfenC++; return true; }                                 // 0x40DFFD
        return false;
    }

    public int PlasmaZielVerworfenB, PlasmaZielVerworfenC, PlasmaZielFallen;

    /// <summary>D: eine Zeile je Treffer — damit ein gespielter Lauf zeigt, ob die
    /// langsamen Fahrzeuge vom Plasma kommen.</summary>
    private void PlasmaTrefferZeile(Entity shooter, Entity victim, int vorher)
        => GD.Print($"plasma: {LabelOf(shooter)} (Platz {shooter.Slot}, Spieler {shooter.Owner}) trifft "
                  + $"{LabelOf(victim)} (Platz {victim.Slot}, Spieler {victim.Owner}), "
                  + $"Tempo {vorher} -> {victim.Speed} (Original 0x40CAA0)");

    /// <summary><c>--plasma-check</c>: die Zaehler, am Ende gedruckt. Hier selbst
    /// aus der Befehlszeile gelesen, damit MapViewer nur die Druckzeile traegt.</summary>
    public static readonly bool PlasmaCheckAn =
        System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--plasma-check") >= 0;

    public string PlasmaCheckLine()
        => $"plasma-check: Treffer {PlasmaTreffer}, von selbst verworfen: Fussvolk/Gebaeude "
         + $"{PlasmaZielVerworfenB}, Tempo<=2 {PlasmaZielVerworfenC}, gelaehmtes Ziel losgelassen "
         + $"{PlasmaZielFallen} (Zielwahl {(PlasmaZielwahlAlt ? "ALT" : "wie Original")})";
}
