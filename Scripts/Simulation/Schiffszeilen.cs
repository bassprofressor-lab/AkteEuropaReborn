namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>DIE SCHIFFSZEILEN WERDEN GERECHNET, NICHT ABGESCHRIEBEN</b> — das
/// Gegenstück zu <see cref="Simulation.DesignMath"/> (Land), für sec119.
/// (19.09.2026, <c>berichte/schiffsentwurf-fable.md</c> §7.)
///
/// <para><b>⚠⚠ Warum das nötig war.</b> Die 23 <c>.CWM</c>-Kampagnenkarten hören
/// nach Abschnitt 38 auf und bringen <b>keine eigene sec119</b> mit. Bei uns
/// fielen sie damit auf die Tafel aus der <c>.data</c> der EXE zurück — und die
/// ist der tote Anfangszustand, den das Original beim Missionsstart sofort
/// überschreibt. Die Folgen im Spiel waren greifbar:</para>
/// <list type="bullet">
///   <item><b>Entwurf 7 (Flak-Barkasse) trug Energie 0</b> statt 110, und
///   <c>LaunchShip</c> setzt <c>Hp = HpMax = d.Energie</c> ohne Absicherung —
///   ein gebautes Schiff hätte 0 Trefferpunkte gehabt. Der Fahrplan gibt den
///   Entwurf ab <b>Mission 25</b> frei.</item>
///   <item>Entwurf 9 (Kreuzer) trug Angriff <b>218</b> statt 20.</item>
///   <item>Jedes in einer Kampagne gebaute Schiff bekam falsche Trefferpunkte —
///   gemessen an <c>--schiffsentwurf-check</c>: K1 Frachter 240 statt 120,
///   K10 Patrouillenboot 100 statt 90, K17 100 statt 125.</item>
/// </list>
///
/// <para><b>Was das Original tut.</b> <c>0x4B23C0</c> am Missionsstart: erst die
/// Bauteilblöcke kopieren (<c>0x4B2290</c>), dann die Schiffszeilen anlegen, dann
/// jede Zeile mit <c>0x4B25E0(spieler, entwurf)</c> aus ihren <b>zwei</b>
/// Bauteilen rechnen — Rumpf <c>+0x17</c> und Waffe <c>+0x16</c>. Ein
/// Spielstand überspringt das (<c>0x41EE04</c>, F <c>0x41DFC3</c>) und trägt die
/// schon gerechneten Zeilen; darum rührt <see cref="SchiffszeilenRechnen"/> eine
/// Karte mit <b>eigener</b> sec119 nicht an.</para>
///
/// <para><b>Die Tafel, Feld für Feld</b> (Bauteilzeile <c>0x5045A0 + 58·(Teil +
/// 200·Spieler)</c>; Byte-Summen mit 8-Bit-Überlauf, Wort-Summen 16 Bit; kein
/// Faktor, keine Klemmung, keine Konstante):</para>
/// <code>
///   +0x19 Preis Waffenteile  = NUR Waffe +0x20                 0x4B2637/0x4B2645
///   +0x1A Preis Rumpf        = NUR Rumpf +0x21                 0x4B265B/0x4B266F
///   +0x1B Preis Spezial      = Rumpf +0x22 + Waffe +0x22       0x4B2669…0x4B2683
///   +0x1C Tempo              = Rumpf +0x10 + Waffe +0x10       0x4B267D…0x4B2697
///   +0x1D Energie            = Rumpf +0x0E(W) + Waffe +0x0E    0x4B2691…0x4B26AC
///   +0x1E Angriff            = Rumpf +0x12 + Waffe +0x12       0x4B26A6…0x4B26C0
///                              := 0 BEI WAFFE 0                0x4B26D4…0x4B26DC
///   +0x1F Verteidigung       = Rumpf +0x13 + Waffe +0x13       0x4B26BA…0x4B26CE
///   +0x20 Reichweite  (Wort) = Rumpf +0x16 + Waffe +0x16       0x4B26E3…0x4B26F7
///   +0x22 Reichweite2 (Wort) = Rumpf +0x14 + Waffe +0x14       0x4B26FE…0x4B270C
///   +0x24 Sicht              = Rumpf +0x11 + Waffe +0x11       0x4B2713…0x4B2721
///   +0x25 Munition           = Rumpf +0x18(W) + Waffe +0x18    0x4B273C…0x4B2746
///   +0x26 Treibstoff  (Wort) = Rumpf +0x1A + Waffe +0x1A       0x4B2727…0x4B2735
///   +0x28 Nachladen          = Rumpf +0x1E(W) + Waffe +0x1E    0x4B274C…0x4B275B
/// </code>
///
/// <para>⭐⭐ <b>Das Nullmodell.</b> Die Tafel ist gegen <b>1040</b>
/// Entwurfszeilen aus 13 Spielständen geprüft — <b>13 520 von 13 520</b>
/// Feldwerten, <b>0</b> Abweichungen. Die bisherige Quelle, die EXE-<c>.data</c>,
/// trifft je Feld zwischen <b>0</b> und 936 von 1040; bei vier Feldern
/// (+0x19, +0x1A, +0x1F, +0x24, +0x26) trifft sie <b>kein einziges Mal</b>.
/// ⚠ Die 416 Treffer bei Angriff und Tempo sind kein Beleg, sondern die vier
/// waffenlosen Entwürfe × 8 Spieler × 13 Stände — also Nullen.</para>
///
/// <para>⭐ <b>Zwei unabhängige Lesungen für die Reichweiten:</b> die Zeilen
/// <c>+0x20</c> und <c>+0x22</c> standen seit dem 17.08.2026 schon bei
/// <c>ShipDesign.Range1/Range2</c> — mit denselben Bauteilfeldern, die diese
/// Tafel nennt. Sie sind hier nicht neu, sondern bestätigt.</para>
///
/// <para>⚠ <b>Waffe 0</b> (Frachter, U-Boot, Treibstoff- und Munitionstender):
/// ein einziger Sonderzweig, der Angriff wird 0. Alle anderen Felder lesen für
/// die Waffe die Bauteilzeile 0, und die ist in <c>PARTS.CWD</c>, in der
/// EXE-<c>.data</c> und in allen 13 Spielständen null — die Summe ist also der
/// Rumpfwert. Der Zweig ist damit heute wirkungslos, gehört aber zur Abschrift.
/// 416 der 1040 Zeilen tragen Waffe 0, und 416/416 treffen in allen 13 Feldern.</para>
///
/// <para>Gegenschalter <c>--schiffszeilen-alt</c>, Prüfstand
/// <c>--schiffsentwurf-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--schiffszeilen-alt</c> — der Stand vor dem 19.09.2026: die
    /// Schiffszeilen kommen roh aus der EXE-Vorgabe, ungerechnet. Das Nullmodell
    /// zu <c>--schiffsentwurf-check</c>: damit muss der Prüfstand DURCHFALLEN.
    /// </summary>
    public static bool SchiffszeilenAlt;

    private static int SzB(byte[]? r, int off)
        => r != null && off >= 0 && off < r.Length ? r[off] : 0;

    private static int SzW(byte[]? r, int off)
        => r != null && off >= 0 && off + 1 < r.Length ? r[off] | (r[off + 1] << 8) : 0;

    /// <summary>
    /// <c>0x4B25E0</c> für jede Zeile der geladenen Schiffstafel.
    /// </summary>
    /// <param name="eigeneTafel">Die Karte bringt ihre sec119 selbst mit (ein
    /// Spielstand). Dann bleibt sie unangetastet, wie im Original.</param>
    private void SchiffszeilenRechnen(bool eigeneTafel)
    {
        if (_shipDesigns == null) return;
        if (SchiffszeilenAlt)
        {
            GD.Print("schiffszeilen: --schiffszeilen-alt — die EXE-Vorgabe bleibt roh stehen");
            return;
        }
        if (eigeneTafel)
        {
            // ⚠ Kein stilles Überspringen: ohne diese Zeile ist »nicht
            // gerechnet« von »gerechnet, nichts geändert« nicht zu unterscheiden.
            GD.Print("schiffszeilen: die Karte bringt sec119 selbst mit — nicht gerechnet "
                   + "(0x41EE04: ein Spielstand ueberspringt 0x4B23C0)");
            return;
        }

        int gerechnet = 0, geaendert = 0, ohneRumpf = 0;
        foreach (var d in _shipDesigns)
        {
            // Die GELTENDE Bauteilzeile des Spielers, nicht die Grundzeile —
            // 0x4B25E0 liest den Block des Spielers. Für Schiffsteile sind alle
            // acht Blöcke gleich (sie werden nie aufgewertet, siehe Kopf von
            // Nachziehen.cs), aber die Abschrift nimmt trotzdem den richtigen.
            var rumpf = BauteilFuer(d.Player, d.Chassis)
                        ?? Simulation.DesignMath.GrundZeile(d.Chassis);
            if (rumpf == null) { ohneRumpf++; continue; }
            var waffe = d.Weapon > 0
                ? BauteilFuer(d.Player, d.Weapon) ?? Simulation.DesignMath.GrundZeile(d.Weapon)
                : null;

            int costW = SzB(waffe, 0x20);
            int costF = SzB(rumpf, 0x21);
            int costS = (SzB(rumpf, 0x22) + SzB(waffe, 0x22)) & 0xFF;
            int speed = (SzB(rumpf, 0x10) + SzB(waffe, 0x10)) & 0xFF;
            int energie = (SzW(rumpf, 0x0E) + SzB(waffe, 0x0E)) & 0xFF;
            int attack = d.Weapon == 0 ? 0 : (SzB(rumpf, 0x12) + SzB(waffe, 0x12)) & 0xFF;
            int defence = (SzB(rumpf, 0x13) + SzB(waffe, 0x13)) & 0xFF;
            int range1 = (SzW(rumpf, 0x16) + SzW(waffe, 0x16)) & 0xFFFF;
            int range2 = (SzW(rumpf, 0x14) + SzW(waffe, 0x14)) & 0xFFFF;
            int sight = (SzB(rumpf, 0x11) + SzB(waffe, 0x11)) & 0xFF;
            int ammo = (SzW(rumpf, 0x18) + SzB(waffe, 0x18)) & 0xFF;
            int fuel = (SzW(rumpf, 0x1A) + SzW(waffe, 0x1A)) & 0xFFFF;
            int reload = (SzW(rumpf, 0x1E) + SzB(waffe, 0x1E)) & 0xFF;

            if (costW != d.CostW || costF != d.CostF || costS != d.CostS
                || speed != d.Speed || energie != d.Energie || attack != d.Attack
                || defence != d.Defence || range1 != d.Range1 || range2 != d.Range2
                || sight != d.Sight || ammo != d.Ammo || fuel != d.Fuel
                || reload != d.Reload) geaendert++;

            // ⚠ Name, Freigabe, Rumpf, Waffe und Variante werden NICHT angefasst
            // — 0x4B25E0 schreibt nur +0x19…+0x28.
            d.CostW = costW; d.CostF = costF; d.CostS = costS;
            d.Speed = speed; d.Energie = energie; d.Attack = attack; d.Defence = defence;
            d.Range1 = range1; d.Range2 = range2;
            d.Sight = sight; d.Ammo = ammo; d.Fuel = fuel; d.Reload = reload;
            gerechnet++;
        }
        GD.Print($"schiffszeilen: {gerechnet} Zeilen aus Rumpf+Waffe gerechnet (0x4B25E0), "
               + $"{geaendert} wichen von der EXE-Vorgabe ab"
               + (ohneRumpf > 0 ? $"; {ohneRumpf} ohne Rumpfzeile" : ""));
    }
}
