using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DIE CODES DES EINHEITENMENÜS</b> — <c>0x441810(einheit, &amp;c1..&amp;c8)</c>,
/// die Zusammenstellung, aus der <c>0x4573C0</c> das Fenster der Art 1 baut.
///
/// <para>Gebaut am 08.09.2026 auf seine Meldung »ebenso dieses … auf die
/// Einheit, das will ich alles haben«, und noch am selben Tag erweitert auf
/// »bau alles nach original mit dem Doppelklick, anstatt unsere Anzeige die
/// unten mittig ist«. Die Befehlsleiste (<c>UI/UnitOrderBar.cs</c>) ist damit
/// abgelöst; sie kommt nur noch mit <c>--befehlsleiste-alt</c> zurück.</para>
///
/// <para><b>Die Tafel c1 ist die des Fable-Berichts</b>
/// (<c>berichte/transporterroute-fable.md</c>, 2.2), nach dem Turmaufsatz
/// <c>+0x0C</c>, und die Codes sind <b>Zeilen der Befehlsliste
/// <c>0x4FD660</c></b> — dieselbe Liste, aus der auch unsere Wörter kommen
/// (<see cref="OrderWord"/>):</para>
/// <code>
///   0x1C -> 0x30       0x23 -> 0x0A       0x24 -> 0x0B
///   0x27 -> 0x23       0x2C -> 0x26       0x2E -> 0x10  TRANSPORTZYKLUS
///   0x2F -> 0x11 (+ c2 = 0x12, c3 = 1)    0x31 -> 0x13   0x32 -> 0x14
///   0x35 -> 0x15       sonst 0 (Angreifen), wenn +0x0D != 0
/// </code>
///
/// <para>⭐⭐ <b>UND SIE PASST AUF UNSERE BAUTEILE, ohne dass jemand sie
/// angepasst hätte</b> — das ist der Quercheck, der aus einer Tafel eine Lesung
/// macht:</para>
/// <list type="bullet">
///   <item>Turm <b>0x2F</b> (47) ist bei uns der <b>Gebäude-Techniker</b>, und
///   er bietet <see cref="OrderDepot"/> + <see cref="OrderFieldMine"/> an —
///   die Tafel gibt ihm <c>0x11</c> und <c>0x12</c>, also die Listenzeilen
///   <b>17 und 18</b>, mit denen <see cref="BuildOrderWord"/> seit jeher
///   dieselben zwei Aufträge benennt.</item>
///   <item>Turm <b>0x31</b> (49) ist der <b>Generatorenbauer</b>; die Tafel
///   gibt ihm <c>0x13</c> = Zeile <b>19</b> — unser
///   <see cref="OrderGenerator"/>.</item>
///   <item>Turm <b>0x32</b> (50) ist der <b>Radarstab-Ausleger</b>; die Tafel
///   gibt ihm <c>0x14</c> = Zeile <b>20</b> — »Radar setzen«.</item>
/// </list>
/// <para>Drei Treffer auf drei Türme, aus zwei unabhängig gelesenen Quellen.</para>
///
/// <para>⚠ <b>Was NICHT wirkt</b>, und es sagt das beim Druck: Angreifen,
/// Bewegen, Beschützen, Selbstzerstörung und Handsteuerung sind im Original
/// <b>Zeigermerker</b> — der Klick schaltet einen Modus (<c>byte[0xA31A9C]</c>,
/// <c>byte[0xA1833B]</c>, <c>byte[0xA18330]</c>), und erst der nächste Klick
/// auf die Karte führt ihn aus. Den Modus haben wir nicht.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    // ---- die Codes, als Zeilen der Befehlsliste 0x4FD660 --------------------
    public const int CodeAngreifen = 0, CodeBewegen = 1, CodeBeschuetzen = 2,
                     CodeSelbstzerstoerung = 3, CodeVerkaufen = 4,
                     CodeHandsteuerung = 5, CodeInfo = 6,
                     CodeEingraben = 7, CodeAusgraben = 8,
                     CodeTransportzyklus = 0x10, CodeDepot = 0x11,
                     CodeFeldmine = 0x12, CodeGenerator = 0x13,
                     CodeRadar = 0x14, CodeAnhalten = 0x1A;

    /// <summary>Die erste eigene Einheit der Auswahl — im Original
    /// <c>[0x4FA0C8]</c>, die »gewaehlte Einheit«, auf die das Menue sich
    /// bezieht.</summary>
    public int MenueEinheit()
    {
        foreach (int i in _sel)
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp) continue;
            if (e.Owner != ViewPlayer) continue;
            return i;
        }
        return -1;
    }

    /// <summary>Die acht Codes für diese Einheit, −1 = leerer Platz.</summary>
    public int[] MenueCodes(int idx)
    {
        var c = new[] { -1, -1, -1, -1, -1, -1, -1, -1 };
        if (idx < 0 || idx >= _entities.Count) return c;
        var e = _entities[idx];

        // c1 — die Tafel roh, ohne Deutung.
        c[0] = e.Weapon switch
        {
            0x1C => 0x30, 0x1D => -1, 0x23 => 0x0A, 0x24 => 0x0B, 0x27 => 0x23,
            0x2C => 0x26, 0x2E => CodeTransportzyklus, 0x2F => CodeDepot,
            0x31 => CodeGenerator, 0x32 => CodeRadar, 0x35 => 0x15,
            _ => e.Comp0D != 0 ? CodeAngreifen : -1,
        };
        // ⭐ Der Gebaeude-Techniker ist der einzige mit ZWEI Auftraegen: die
        // Tafel setzt fuer 0x2F ausdruecklich c2 = 0x12 und c3 = 1.
        if (e.Weapon == 0x2F) { c[1] = CodeFeldmine; c[2] = CodeBewegen; }
        else
        {
            c[1] = CodeBewegen;
            // c3: Anhalten statt Beschuetzen, wenn die Einheit unbewaffnet ist;
            // Ein-/Ausgraben, wenn +0x0F == 0xAB (das Fussvolk).
            c[2] = e.Comp0F == 0xAB ? (e.DugIn ? CodeAusgraben : CodeEingraben)
                 : e.Comp0D == 0 ? CodeAnhalten : CodeBeschuetzen;
        }
        // ⚠ UNSER Zusatz an c4: das Original hat dort nur die zwei Faelle
        // +0x10 ∈ {0x53, 0x54}, die es bei uns nicht gibt — und ohne ihn waere
        // »Anhalten« fuer ein bewaffnetes Fahrzeug ueberhaupt nicht mehr
        // erreichbar, seit die Befehlsleiste weg ist. Der Platz bleibt sonst
        // leer, also kostet es nichts.
        if (c[2] != CodeAnhalten && e.Mobile) c[3] = CodeAnhalten;
        // c5: Selbstzerstoerung nur oberhalb von 15 % Huelle.
        if (e.HpMax > 0 && e.Hp * 100 / e.HpMax > 15) c[4] = CodeSelbstzerstoerung;
        c[5] = CodeInfo;                                // Einheiteninformation
        if (e.GameUnitType != 1) c[6] = CodeHandsteuerung;
        // c8: Verkaufen — im Original hinter byte[0x504598]; bei uns hinter der
        // Frage, ob es hier ueberhaupt etwas zu verkaufen GIBT.
        if (SellChoiceOfSelection() != null) c[7] = CodeVerkaufen;
        return c;
    }

    /// <summary>Das Wort des SPIELS zu einem Menuecode — dieselbe Liste
    /// <c>0x4FD660</c>, aus der auch die Bauauftraege ihre Namen holen.</summary>
    public static string MenueWort(int code)
    {
        string w = OrderWord(code);
        return w is "?" or "" ? $"Befehl {code}" : w;
    }

    /// <summary>Was der Menüdruck bewirkt. Der Rückgabewert ist die Zeile für
    /// den Spieler — leer heisst »hat gewirkt, es gibt nichts zu sagen«.</summary>
    public string MenueAktion(int code)
    {
        switch (code)
        {
            case CodeTransportzyklus:
                return RouteFensterOeffnen() ? ""
                     : RouteNote.Length > 0 ? RouteNote : "kein Transporter gewaehlt";
            case CodeAnhalten:
                StopSelected();
                return "";
            case CodeVerkaufen:
                SellFromPanel();
                return SellNote;
            case CodeEingraben:
            case CodeAusgraben:
                ToggleDigIn();
                return "";
            case CodeDepot:
                BeginPlacementFromPanel(OrderDepot);
                return BuildOrderNote;
            case CodeFeldmine:
                BeginPlacementFromPanel(OrderFieldMine);
                return BuildOrderNote;
            case CodeGenerator:
                BeginPlacementFromPanel(OrderGenerator);
                return BuildOrderNote;
            case CodeRadar:
                PlaceRadarFromPanel();
                return BuildOrderNote.Length > 0 ? BuildOrderNote : "";
            case CodeInfo:
                return "Die Werte stehen im Bedienblock links unten.";
            default:
                // ⚠ Ehrlich statt still: das sind die Zeigermerker des
                // Originals, und den Modus gibt es bei uns nicht.
                return $"»{MenueWort(code)}« ist noch nicht gebaut "
                     + "(im Original ein Zeigermodus).";
        }
    }
}
