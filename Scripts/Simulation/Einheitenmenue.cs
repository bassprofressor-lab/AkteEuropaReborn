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

    // ---- die Zeilen des FUSSVOLKS (12.09.2026, bug-219) ---------------------
    //
    // ⭐ Gelesen in beiden EXE (berichte/pionier-menue-fable.md): die
    // Befehlsliste 0x4FD660 hat 48 Zeilen à 30 Byte, und das Fussvolk benutzt
    // eigene — nicht die des Fahrzeugs.
    public const int CodeBruecke = 0x0D,      // »Bruecke bauen«
                     CodeMole = 0x0E,         // »Mole bauen«  = die Landungsbruecke
                     CodeAusbessern = 0x0C,   // »Bruecke/Mole reparieren«
                     CodeFussAngriff = 0x1F,  // Angreifen, Fussvolkzeile
                     CodeFussLaufen = 0x20,   // Laufen
                     CodeFussSchutz = 0x21;   // Beschuetzen

    /// <summary><c>--fussvolkmenue-alt</c> — der Stand vor dem 12.09.2026: das
    /// Fussvolk bekommt dieselbe Menuetafel wie ein Fahrzeug. Das Nullmodell zu
    /// bug-219; unter ihm hat der Pionier wieder leere Haende.</summary>
    public static bool FussvolkmenueAlt;

    /// <summary><b>Der Waffenuntertyp +0x0B</b>, an dem die Fussvolkweiche
    /// haengt. Bevorzugt das Rohbyte; fehlt es (erzeugte Einheiten tragen
    /// keinen Rohsatz), gilt der gelesene Zusammenhang
    /// <c>+0x0B == 2·(Waffenzeile − 190)</c> als Rueckfall.
    ///
    /// <para>⚠ Unsere Fusssoldaten fuehren die Waffenzeile als
    /// <c>Weapon = InfCompBase + Zeile</c> (390…399), das Original als
    /// <c>+0x0D</c> = 190…201. Beides derselbe Wert, zwei Zaehlweisen.</para>
    /// <returns>0…22, oder −1 wenn die Einheit kein Fussvolk ist.</returns>
    public static int WaffenUntertyp(Entity e)
    {
        if (e.GameUnitType != 1) return -1;
        if (e.Comp0B >= 0) return e.Comp0B;
        int zeile = e.Weapon >= InfCompBase ? e.Weapon - InfCompBase
                  : e.Comp0D is >= 190 and <= 201 ? e.Comp0D : -1;
        return zeile < 0 ? -1 : Mathf.Clamp(2 * (zeile - 190), 0, 22);
    }

    /// <summary>
    /// <b>DIE VIER ERSTEN PLAETZE EINES FUSSSOLDATEN</b> — die Sprungtafel
    /// @0x441A60 mit ihren 13 Faellen, in beiden EXE byteweise gleich
    /// (berichte/pionier-menue-fable.md, Abschnitt 1.2).
    ///
    /// <code>
    ///   Indextafel @0x441A94: nur GERADE +0x0B tragen einen Fall,
    ///   ungerade landen auf Fall 12 = nichts.
    ///
    ///   Fall 0,1,2,4,7,9   (Waffe 190,191,192,194,197,199)  bewaffnet
    ///        -> 0x1F Angreifen · 0x20 Laufen · 0x21 Beschuetzen
    ///   Fall 3,5,6,10,11   (Waffe 193,195,196,200,201)      unbewaffnet
    ///        ->      —     · 0x20 Laufen · 0x21 Beschuetzen
    ///   Fall 8             (Waffe 198 = PIONIER)
    ///        -> 0x0C Ausbessern · 0x20 Laufen · 0x0D Bruecke · 0x0E Mole
    /// </code>
    ///
    /// <para>⚠⚠ <b>Alle Fussvolkfaelle springen nach 0x4419F8</b> und
    /// ueberspringen damit den Block, der beim Fahrzeug c2 = Bewegen, c3 =
    /// Beschuetzen/Anhalten und das Ein-/Ausgraben setzt. Fussvolk bekommt
    /// darum <b>nie</b> Handsteuerung (@0x441A28 <c>cmp al,1; je</c>),
    /// <b>nie</b> Verkaufen (@0x441A3B verlangt +0x0A == 0) und <b>nie</b>
    /// Ein-/Ausgraben. Bei uns stand genau das drin — <c>Comp0F == 0xAB</c>
    /// gab jedem Fusssoldaten den Spaten.</para>
    /// </summary>
    private static int[] FussvolkCodes(Entity e)
    {
        var c = new[] { -1, -1, -1, -1, -1, -1, -1, -1 };
        int sub = WaffenUntertyp(e);
        if (sub < 0 || sub > 22 || (sub & 1) != 0) return c;   // Fall 12: nichts
        int fall = sub / 2;
        switch (fall)
        {
            case 8:                                            // der PIONIER
                c[0] = CodeAusbessern; c[1] = CodeFussLaufen;
                c[2] = CodeBruecke;    c[3] = CodeMole;
                break;
            case 3: case 5: case 6: case 10: case 11:          // unbewaffnet
                c[1] = CodeFussLaufen; c[2] = CodeFussSchutz;
                break;
            default:                                           // bewaffnet
                c[0] = CodeFussAngriff; c[1] = CodeFussLaufen; c[2] = CodeFussSchutz;
                break;
        }
        return c;
    }

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

    /// <summary>Eine Einheit anwaehlen — fuer die Pruefstaende, die keinen
    /// Mausklick haben.</summary>
    public bool WaehleFuerProbe(int idx)
    {
        if (idx < 0 || idx >= _entities.Count) return false;
        _sel.Clear(); _sel.Add(idx); SetPrimary();
        return true;
    }

    /// <summary>Die erste eigene, bewegliche Einheit — fuer die
    /// Pruefstaende.</summary>
    public int ErsteEigeneEinheit()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp) continue;
            if (e.Owner != ViewPlayer || !e.Mobile) continue;
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

        // ⭐⭐ 12.09.2026 — DIE GATTUNGSWEICHE (bug-219). @0x441877 steht
        // `cmp byte[+0x0A], 1` VOR allem anderen: Fussvolk geht einen eigenen
        // Weg, und der endet direkt beim gemeinsamen Teil c5/c6. Siehe
        // FussvolkCodes. Gegenschalter --fussvolkmenue-alt.
        bool fussvolk = !FussvolkmenueAlt && e.GameUnitType == 1;
        if (fussvolk)
        {
            c = FussvolkCodes(e);
            if (e.HpMax > 0 && e.Hp * 100 / e.HpMax > 15) c[4] = CodeSelbstzerstoerung;
            c[5] = CodeInfo;
            return c;                       // kein c7, kein c8 — siehe Kopf
        }

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
        // ⚠⚠ 08.09.2026 — HIER STAND EIN SYMBOL ZUVIEL, UND ES WAR MEINES.
        // Seine Meldung: »das menu zeigt mir je nach einheit ein oder zwei
        // Icons zuviel an in der Box? Ich weiss nicht was da richtig ist«.
        // An Platz 4 hatte ich »Anhalten« nachgetragen, weil es nach dem
        // Abschalten der Befehlsleiste sonst nicht mehr erreichbar gewesen
        // waere. Das Original hat dort NUR die zwei Faelle +0x10 in {0x53,
        // 0x54}, die es bei uns nicht gibt — der Platz bleibt also LEER.
        // ⭐ Ein bewaffnetes Fahrzeug hat im Original kein »Anhalten« im
        // Menue; bei uns steht es weiter auf der Taste X.
        // c5: Selbstzerstoerung nur oberhalb von 15 % Huelle.
        if (e.HpMax > 0 && e.Hp * 100 / e.HpMax > 15) c[4] = CodeSelbstzerstoerung;
        c[5] = CodeInfo;                                // Einheiteninformation
        if (e.GameUnitType != 1) c[6] = CodeHandsteuerung;
        // c8: Verkaufen — die Tafel sagt »nur wenn byte[0x504598] != 0 UND
        // +0x0A == 0«. Die Globale kennen wir nicht, die Gattung schon: nur
        // ein FAHRZEUG (Gattung 0) bekommt den Eintrag. Dazu unsere Frage, ob
        // es hier ueberhaupt etwas zu verkaufen gibt.
        if (e.GameUnitType == 0 && SellChoiceOfSelection() != null)
            c[7] = CodeVerkaufen;
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

            // ---- die ZEIGERMERKER, siehe Simulation/Zeigermerker.cs ------
            // ⭐ 12.09.2026: das Fussvolk hat eigene Zeilen fuer dieselben drei
            // Merker (0x1F/0x20/0x21 statt 0/1/2) — die Wirkung ist dieselbe,
            // nur die Zeile der Befehlsliste ist eine andere.
            case CodeFussAngriff:
                return MerkerSetzen(CodeAngreifen);
            case CodeFussLaufen:
                return MerkerSetzen(CodeBewegen);
            case CodeFussSchutz:
                goto case CodeBeschuetzen;
            case CodeAngreifen:
            case CodeBewegen:
                return MerkerSetzen(code);

            // ---- was der PIONIER kann (bug-219) --------------------------
            case CodeMole:
                return MolenbauBeginnen();
            case CodeAusbessern:
                return AusbessernBeginnen();
            case CodeBruecke:
                // ⭐ 13.09.2026 — GEBAUT (bug-245). Hier stand bis dahin die
                // Auskunft, dass die Geometrie ungelesen sei; sie ist es seit
                // berichte/pionier-bruecke-fable.md nicht mehr.
                return BrueckenbauBeginnen();
            case CodeSelbstzerstoerung:
                return SelbstzerstoerungAusfuehren();
            case CodeHandsteuerung:
                return HandsteuerungUmschalten();
            case CodeBeschuetzen:
                // ⚠ Merker A loest im naechsten Takt BEFEHL 12 auf der
                // gewaehlten Einheit aus (revier3 §3.2). Was 12 TUT, ist
                // nicht gelesen — und eine erfundene Wirkung waere schlimmer
                // als keine. Der Merker ist gebaut, die Wirkung fehlt.
                return "»Beschuetzen« ist Befehl 12, und dessen Behandler ist "
                     + "nicht gelesen — hier passiert darum (noch) nichts.";

            default:
                return $"»{MenueWort(code)}« ist noch nicht gebaut.";
        }
    }
}
