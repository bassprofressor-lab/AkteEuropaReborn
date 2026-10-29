namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐ 29.10.2026, bug-652 (KayelGee R8 K10, Bild <c>Bug Bilder/29102026bug10.png</c>):
/// »ueber Transportern fehlt die Leiste fuer den Fuellstand«. Gemeint ist der
/// <b>Materialbalken</b> des Wagens der Materialroute (Oberteil 46 → Ausruestung
/// 0x47), gelesen in <c>berichte/anzeige-r8-opus.md</c> §1.2:
/// <code>
///   Fahrzeugzeichner C 0x42A755 (F 0x429943…):
///     byte[+0x0C] == 0x2E (46)  und  Besitzer == byte[0x4FA284] (eigener Spieler)
///     push y+0x0A ; push x+0x12 ; push id ; call 0x40115E -> 0x4B93F0
///   0x4B93F0:  byte[0xA31A88] := 4 (nur fuer diesen Ruf)
///              satz = 18·byte[+0x40] (sec48)
///              0x4B6F60(x, y, breite = Fassung(+0x0A)+1, hoehe 5, fuellung = Ladung(+0x09), id)
/// </code>
/// Breite 31, Hoehe 5, Fuellung 0..30, Farbe Platz <b>0x56 = #BBAB87</b> (beige,
/// 01.PAL), Rahmen 1 px in Spielerfarbe. Gegen den Lebensbalken (Anker +0x14,+0x14)
/// sitzt er <b>10 px hoeher und 2 px weiter links</b> (Anker +0x12,+0x0A). Er hat
/// <b>keine Mengenpruefung</b> und haengt <b>nicht</b> am Tab-Ring (das Tor 0x42A707
/// ueberspringt nur den Lebensbalken) — ein eigener Wagen traegt ihn also immer,
/// leer oder voll; fremde nie. Das Bild des Testers passt pixelgenau (§1.3).
///
/// <para>⚠ UNSERE SETZUNG: ein Wagen ohne Umschlagsatz (RouteVon == null) zeigt
/// Breite 31 und Fuellung 0 — das Original laese dort Satz 0 (eines anderen Wagens);
/// ob das je vorkommt, ist ungelesen. ⚠ Der zweite Lebensbalkenruf 0x42A74D
/// (<c>[0x504038] ≠ 0</c>) wiederholt den Materialbalken nicht; ungelesen, ob das
/// sichtbar wird. Gegenschalter <c>--materialbalken-alt</c> (kein Balken, 0.6.11).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--materialbalken-alt</c> — bug-652: kein Materialbalken (Stand 0.6.11).</summary>
    public static bool MaterialbalkenAlt;

    /// <summary>Palettenplatz 0x56 aus <c>DATA/01.PAL</c> = (187,171,135).</summary>
    private static readonly Color MaterialFarbe = Color.Color8(187, 171, 135);

    /// <summary>Anker des Materialbalkens gegen den des Lebensbalkens:
    /// (0x12 − 0x14, 0x0A − 0x14) = (−2, −10).</summary>
    private const float MaterialLinks = 2f, MaterialHoeher = 10f;

    /// <summary>Wie viele Materialbalken zuletzt gezeichnet wurden (Pruefstand).</summary>
    public int MaterialbalkenGezeichnet;

    /// <summary>Das Tor von 0x42A755: Oberteil 46 (bei uns: Aufsatz 46 oder der
    /// Umschlagsatz-Traeger 0x47) und EIGENER Wagen.</summary>
    private bool HatMaterialbalken(Entity e)
        => !MaterialbalkenAlt && !e.IsBuilding && !e.IsProp && !e.Dead
           && e.Owner == ViewPlayer
           && (e.Weapon == MaterialOberteil || IstTransporter(e));

    /// <summary>Oberteil 46 (<c>cmp 0x2E</c> @0x42A755).</summary>
    private const int MaterialOberteil = 46;

    /// <summary>Lage und Masse des Materialbalkens: linke obere Ecke, Breite,
    /// Fuellung — dieselbe Rechnung fuer Zeichner und Pruefstand.</summary>
    private (Vector2 Ecke, int Breite, int Fuellung) MaterialbalkenMass(Entity e)
    {
        var r = RouteVon(e);
        int fassung = r?.Fassung ?? RouteFassung;
        int breite = fassung + 1;                                   // 0x77AC5A + 1
        int fuell = System.Math.Clamp(r?.Gesamt ?? 0, 0, fassung);  // 0x77AC59
        // Mitte = Lebensbalkenmitte − 2, x −= breite/2 ganzzahlig (0x4B6F6D)
        float mitte = Mathf.Floor(e.Pos.X - MaterialLinks);
        float oben = Mathf.Floor(e.Pos.Y - BalkenHub(e)) - MaterialHoeher;
        return (new Vector2(mitte - breite / 2, oben), breite, fuell);
    }

    private void MaterialbalkenZeichnen(Entity e)
    {
        if (!HatMaterialbalken(e)) return;
        var (ecke, breite, fuell) = MaterialbalkenMass(e);
        BalkenMitRahmen(ecke, breite, 5, fuell, MaterialFarbe,
                        e.Owner >= 0 && e.Owner < BalkenRahmen.Length ? BalkenRahmen[e.Owner] : PropColor);
        MaterialbalkenGezeichnet++;
    }

    // ---- --materialbalken-check ---------------------------------------------

    public static bool MaterialbalkenCheckAn;

    /// <summary>
    /// Je eigenem und fremdem Wagen nachrechnen, was der Zeichner tut — in allen
    /// vier Tab-Stellungen. Soll: je eigenem Wagen ein Balken, Breite 31 == Fassung+1,
    /// Fuellung == Gesamt, Mitte == Lebensbalkenmitte − 2, Oberkante ==
    /// Lebensbalken-Oberkante − 10 (eigene, unabhaengige Zeilen), fremde 0.
    /// ⚠ Messfalle: erst die Zahl der eigenen Wagen — 0 Wagen ist keine Messung.
    /// Eingriff: hat die Karte keinen eigenen Wagen, wird ein eigenes Fahrzeug zum
    /// Transporter (Part 0x47) gemacht, und einem die Ladung 17 gegeben.
    /// Nullmodell <c>--materialbalken-alt</c>: 0 Balken → DURCHGEFALLEN.
    /// </summary>
    public string MaterialbalkenCheck()
    {
        var sb = new System.Text.StringBuilder("materialbalken-check"
            + (MaterialbalkenAlt ? " — NULLMODELL --materialbalken-alt" : "") + "\n");
        int eigene = 0, fremde = 0;
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.IsProp && !e.Dead && IstTransporter(e))
            { if (e.Owner == ViewPlayer) eigene++; else fremde++; }
        if (eigene == 0)
        {
            foreach (var e in _entities)
            {
                if (e.IsBuilding || e.IsProp || e.Dead || e.Owner != ViewPlayer || e.Infantry >= 0
                    || !e.Mobile || e.GameUnitType != 0 || Untergestellt(e)) continue;
                e.Part = TransporterTeil; e.Weapon = 0;
                sb.AppendLine($"  EINGRIFF: Platz {e.Slot} wird Transporter (Part 0x47)");
                eigene = 1;
                break;
            }
        }
        // einem Wagen eine Ladung geben, damit die Fuellung etwas misst
        foreach (var e in _entities)
            if (!e.IsBuilding && !e.Dead && e.Owner == ViewPlayer && IstTransporter(e))
            {
                var r = RouteVon(e) ?? RouteAnlegen(e);
                if (r != null) { r.Gesamt = 17; sb.AppendLine($"  EINGRIFF: Platz {e.Slot} Ladung 17 von {r.Fassung}"); }
                break;
            }
        sb.AppendLine($"  eigene Wagen {eigene}, fremde Wagen {fremde} (Sichtspieler {ViewPlayer})");
        if (eigene == 0) return sb.Append("  KEIN URTEIL: kein eigener Wagen").ToString();

        int fehler = 0;
        int merk = Balkenmodus;
        int[] jeStellung = new int[4];
        for (int st = 0; st < 4; st++)
        {
            Balkenmodus = st;
            foreach (var e in _entities)
            {
                if (e.IsBuilding || e.IsProp || e.Dead || Untergestellt(e)) continue;
                bool wagen = IstTransporter(e) || e.Weapon == MaterialOberteil;
                if (!wagen) continue;
                bool hat = HatMaterialbalken(e);
                if (e.Owner != ViewPlayer)
                {
                    if (hat) { fehler++; sb.AppendLine($"  FEHLER fremder Wagen Platz {e.Slot} hat einen Balken"); }
                    continue;
                }
                if (!hat) continue;
                jeStellung[st]++;
                var (ecke, breite, fuell) = MaterialbalkenMass(e);
                var r = RouteVon(e);
                int sollBreite = (r?.Fassung ?? 30) + 1;
                int sollFuell = r?.Gesamt ?? 0;
                // die Lebensbalkenlage aus SEINER Rechnung (TabBalkenPunkt), unabhaengig
                float lbw = (e.HpMax >> 2) + 2;
                var leben = TabBalkenPunkt(e, lbw);
                float lebenMitte = Mathf.Floor(e.Pos.X);
                float mitte = ecke.X + breite / 2;
                bool ok = breite == sollBreite && breite == 31 && fuell == sollFuell
                          && mitte == lebenMitte - 2 && ecke.Y == leben.Y - 10;
                if (!ok) fehler++;
                if (st == 1 || !ok)
                    sb.AppendLine($"  Stellung {st} Platz {e.Slot}: Breite {breite} (Soll {sollBreite}), Fuellung {fuell} (Soll {sollFuell}), "
                                + $"Mitte {mitte} gegen Leben {lebenMitte} ({mitte - lebenMitte:+0;-0;0}, Soll −2), "
                                + $"Oberkante {ecke.Y} gegen Leben {leben.Y} ({ecke.Y - leben.Y:+0;-0;0}, Soll −10) -> {(ok ? "ok" : "FEHLER")}");
            }
        }
        Balkenmodus = merk;
        bool gleich = jeStellung[0] == eigene && jeStellung[1] == eigene && jeStellung[2] == eigene && jeStellung[3] == eigene;
        sb.AppendLine($"  Balken je Tab-Stellung 0/1/2/3: {jeStellung[0]}/{jeStellung[1]}/{jeStellung[2]}/{jeStellung[3]} (Soll je {eigene})");
        bool bestanden = fehler == 0 && gleich;
        sb.Append(bestanden ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
