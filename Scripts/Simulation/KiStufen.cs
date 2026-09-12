namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>DIE DREI STUFEN DER GEFECHTS-KI — leicht, normal, schwer bedeuten jetzt
/// etwas</b> (gebaut 12.09.2026, bug-205).
///
/// <para><b>Gemeldet:</b> »die KI sollte nicht von anfang an solche einheiten
/// haben« (die Mittelstreckenraketen aus bug-202). Und dazu seine Vorgabe:
/// »Die KI im Gefecht ist eben eine andere als die in der Kampagne. Wir haben
/// ja 3 Modis für eine AI, leicht, normal, schwer. Darin sollte sich
/// unterscheiden wie stark diese Einheiten baut und wie aggressiv sie erkundet
/// und angreift.«</para>
///
/// <para><b>Was gelesen ist — und es beantwortet die Frage von selbst.</b>
/// <c>component_stats.json</c> (die Bauteiltafel des Originals) trägt bei
/// <b>+0x24 die Techstufe</b> jedes Bauteils, und das Freigabetor @0x419E90 /
/// @0x419F30 hält sie gegen den Techstandard:
/// <c>stats[Teil].+0x24 &lt;= Techstandard</c>. Ausgelesen:</para>
/// <code>
///   Stufe 1  Kanone, M-Gewehr · Reifen, Schneegl., W-Wiesel · Radar, Transport,
///            G-/B-Techniker, Generator, TerrFind
///   Stufe 2  S.Kanone, 2-M-Gewehr · 6x6 Reifen, Ketten, S.W-Wiesel · Radarstab
///   Stufe 3  L-Raketen, Flammen, Minenleger, Fallenleger · Panz.Reif. · Minen-
///            und Fallenräumer, Antiradar
///   Stufe 4  Gaswerfer, Flak · Spinne, A-W-Stell., Läufer · Luftsauger
///   Stufe 5  H-Raketen, Laser, Plasma · S.Ketten, S.Blocker, Schweber · Mechaniker
///   Stufe 6  2-Laser, Mörser · Kugeln
///   Stufe 7  MS-RAKETE, M-Bombe · Luftkissen, Stahlsucher · Antimagnet
///   Stufe 8  SchallKmp., Blitz · Teleporter, Zielfokus
///   Stufe 0  beide Infanterie-Fahrwerke (148/149) und ALLE Handwaffen (185..199)
///            — Fussvolk steht nie unter dem Tor.
/// </code>
///
/// <para><b>Die Mittelstreckenrakete ist Techstufe 7 von 8</b> — im Original das
/// vorletzte, was jemand bekommt. Bei uns prüfte dieses Tor nur der Markt
/// (<see cref="Simulation.MarketTrade"/>) und der Flughafen; die
/// FAHRZEUGFERTIGUNG prüfte es gar nicht, und der Gefechts-Techstandard steht in
/// der Vorgabe auf 8 (unsere Wettkampfentscheidung, siehe
/// <see cref="UI.SkirmishSetup.Techstandard"/>). Darum stand die stärkste Rakete
/// des Spiels ab Sekunde eins im Baumenü der KI.</para>
///
/// <para>⚠ <b>Die Reichweite 255 bleibt</b>, und sie ist nicht unsere: dieselbe
/// Tafel trägt für die MS-Rakete bei +0x14 den Wert 255, und auf den
/// Originalkarten (map_04, map_DM_4) stehen sechs Fahrzeuge des Typs 163 mit
/// genau dieser Reichweite im eigenen Satz (+0x2b). Der Rest der Tafel läuft von
/// 2 bis 10 — die MS-Rakete ist der einzige Ausreisser im ganzen Spiel. Seine
/// Ansage dazu: »Wenn die Reichweite dieser einzelnen Langstrecken Rakete 255
/// ist laut Original, dann behält sie diesen Wert.«</para>
///
/// <para><b>Was jetzt gilt — NUR IM GEFECHT und NUR FÜR DIE KI</b> (seine
/// Entscheidung auf die Frage, wer unter dem Tor steht: »Nur die KI«). Jede
/// Computerpartei führt einen EIGENEN, mitwachsenden Techstandard. Er fängt
/// niedrig an und klettert mit der Spielzeit; die Stufe bestimmt allein das
/// Tempo und den Deckel. Der Spieler baut wie bisher.</para>
///
/// <code>
///                        leicht    normal    schwer
///   Starttechstufe          1         1         2
///   eine Stufe mehr alle   5:00      2:30      1:30
///   Deckel        Techstandard-2  Techstandard  Techstandard
///   MS-Rakete (7) ab        nie      15:00      7:30
///   Pause zwischen Wellen  40 s      20 s      10 s
///   Pause zwischen Spähf.  20 s       8 s       4 s
///   Spähtrupp             1 Einheit  2 Einh.  alles Freie
/// </code>
///
/// <para>⚠ <b>UNSERE SETZUNGEN, benannt:</b> gelesen ist das TOR und die
/// TECHSTUFE jedes Bauteils. Dass der Standard einer KI mit der Spielzeit
/// klettert, und mit welchem Tempo, ist Wettkampf und steht nirgends im
/// Original — das Original setzt den Techstandard einmal beim Aufbau der Partie
/// und lässt ihn stehen. Ebenso unser: die drei Zahlenspalten oben, und dass die
/// Wellen- und Spähpausen überhaupt an der Stufe hängen.</para>
///
/// <para>Gegenschalter <c>--ki-stufen-aus</c> — der Stand vor dem 12.09.: kein
/// Tor, alle Pausen wie bisher (20 s / 8 s), Spähtrupp alles Freie. Prüfstand
/// <c>--ki-stufen-check</c>: schreibt je Computerspieler, in welcher Minute er
/// zum ersten Mal ein Bauteil welcher Techstufe gebaut hat, und zählt jeden Bau
/// ÜBER seiner Leiter. ⚠ Die Leiter wird auch unter dem Gegenschalter gerechnet
/// und dagegen gezählt — sonst hätte das Nullmodell nichts, wogegen es
/// durchfallen könnte (die Lehre vom 11.09.).</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary><c>--ki-stufen-aus</c> — siehe Kopf.</summary>
    public static bool KiStufenAus;

    /// <summary>Sekunden seit dem ersten KI-Takt dieser Partie. Läuft mit
    /// demselben <c>dt</c> wie die KI selbst, damit Leiter und Entscheidung aus
    /// derselben Uhr kommen.</summary>
    private float _kiSpielzeit;

    /// <summary>Gilt die Regel gerade? Gefecht, eine KI, kein Gegenschalter.</summary>
    private bool KiStufenAn => !KiStufenAus && !InCampaign && _aiOn;

    /// <summary>Das Zahlenwerk einer Stufe. ⚠ UNSERE Setzung, siehe Kopf.</summary>
    private readonly struct KiProfil
    {
        public KiProfil(int start, float schritt, int deckel, float welle, float spaeh, int trupp)
        {
            TechStart = start; TechSchritt = schritt; TechDeckel = deckel;
            WellenPause = welle; SpaehPause = spaeh; SpaehTrupp = trupp;
        }

        /// <summary>Techstufe in der ersten Sekunde.</summary>
        public int TechStart { get; }
        /// <summary>Sekunden je Stufe.</summary>
        public float TechSchritt { get; }
        /// <summary>Versatz auf den Techstandard der Partie (leicht: −2).</summary>
        public int TechDeckel { get; }
        /// <summary>Sekunden zwischen zwei Angriffswellen.</summary>
        public float WellenPause { get; }
        /// <summary>Sekunden zwischen zwei Spähfahrten.</summary>
        public float SpaehPause { get; }
        /// <summary>Wie viele Einheiten höchstens spähen fahren.</summary>
        public int SpaehTrupp { get; }
    }

    private static KiProfil ProfilOf(AiLevel l) => l switch
    {
        AiLevel.Easy => new KiProfil(1, 300f, -2, 40f, 20f, 1),
        AiLevel.Hard => new KiProfil(2,  90f,  0, 10f,  4f, 999),
        _            => new KiProfil(1, 150f,  0, 20f,  8f, 2),
    };

    // ---- die Leiter ---------------------------------------------------------

    /// <summary>Der Techstandard, den diese Computerpartei gerade hat — die
    /// LEITER. Sie wird immer gerechnet, auch unter dem Gegenschalter; ob sie
    /// auch GILT, entscheidet <see cref="KiStufenAn"/>.</summary>
    private int KiTechLeiter(AiLevel stufe)
    {
        var p = ProfilOf(stufe);
        int deckel = Mathf.Clamp(UI.SkirmishSetup.Techstandard + p.TechDeckel, 1, 8);
        int wert = p.TechStart + (int)(_kiSpielzeit / Mathf.Max(1f, p.TechSchritt));
        return Mathf.Clamp(wert, 1, deckel);
    }

    /// <summary>Die Techstufe eines ganzen Entwurfs: die höchste seiner drei
    /// Bauteile. ⚠ Eine unbekannte Zeile (<c>TechLevel</c> gibt −1) zählt als
    /// frei — dieselbe Regel, die der Laden schon anwendet
    /// (<c>ShopDesignLocked</c>): eine Lücke darf nicht wie eine Sperre
    /// aussehen.</summary>
    private static int KiEntwurfsstufe(Design d)
    {
        int hoch = 0;
        foreach (int teil in new[] { d.Weapon, d.Propulsion, d.Equip })
        {
            if (teil <= 0) continue;
            int lvl = Simulation.DesignMath.TechLevel(teil);
            if (lvl > hoch) hoch = lvl;
        }
        return hoch;
    }

    /// <summary>Das Freigabetor für einen Entwurf, wörtlich wie @0x419F30:
    /// <c>stats[Teil].+0x24 &lt;= Techstandard</c>, nur eben gegen den
    /// mitwachsenden Standard dieser Computerpartei.</summary>
    private bool KiDarfBauen(AiLevel stufe, Design d) =>
        KiEntwurfsstufe(d) <= KiTechLeiter(stufe);

    // ---- die Zähler des Prüfstands; sie laufen auch unter dem Gegenschalter --

    private sealed class KiStufenBuch
    {
        public AiLevel Stufe;
        public int Gebaut, Gesperrt, Rueckfall, Verstoesse;
        /// <summary>⚠ Die zweite Haelfte seiner Ansage — »wie aggressiv sie
        /// erkundet und angreift«. Ohne diese zwei Zahlen misst der Pruefstand
        /// nur das Bauen und behauptet den Rest (die Lehre vom 11.09.).</summary>
        public int Spaehfahrten, Wellen;
        /// <summary>Sekunde des ersten Baus je Techstufe 0..8, −1 = nie.</summary>
        public readonly float[] Erstbau = { -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f, -1f };
        public readonly List<string> Notizen = new();
        public int HoechsteLeiter;
    }

    private readonly Dictionary<int, KiStufenBuch> _kiStufenBuch = new();

    private KiStufenBuch KiBuch(AiPlayer a)
    {
        if (!_kiStufenBuch.TryGetValue(a.Player, out var b))
            _kiStufenBuch[a.Player] = b = new KiStufenBuch { Stufe = a.Level };
        b.Stufe = a.Level;
        int leiter = KiTechLeiter(a.Level);
        if (leiter > b.HoechsteLeiter) b.HoechsteLeiter = leiter;
        return b;
    }

    /// <summary>Aus dem Baumenü einer Basis wird die Liste, die diese
    /// Computerpartei auf ihrer Leiter bauen DARF.
    ///
    /// <para>⚠ Bleibt davon nichts übrig, gilt wieder die ganze Liste, und der
    /// Rückfall wird GEZÄHLT (<c>Rueckfall</c>). Eine KI, die gar nicht mehr
    /// baut, wäre der schlimmere Fehler — und ein stiller Rückfall, den niemand
    /// sieht, der zweitschlimmere.</para></summary>
    private List<int> KiBaumenue(AiPlayer a, List<int> menue)
    {
        var buch = KiBuch(a);
        if (!KiStufenAn || _designs == null) return menue;

        var frei = new List<int>();
        foreach (int i in menue)
            if (KiDarfBauen(a.Level, _designs[i])) frei.Add(i);

        buch.Gesperrt += menue.Count - frei.Count;
        if (frei.Count > 0) return frei;
        buch.Rueckfall++;
        return menue;
    }

    /// <summary>Nach jedem wirklich begonnenen Bau — die einzige Stelle, an der
    /// der Prüfstand seine Zahlen bekommt.</summary>
    private void KiStufenNotiz(AiPlayer a, Design d)
    {
        var buch = KiBuch(a);
        buch.Gebaut++;
        int stufe = Mathf.Clamp(KiEntwurfsstufe(d), 0, 8);
        int leiter = KiTechLeiter(a.Level);
        if (buch.Erstbau[stufe] < 0f)
        {
            buch.Erstbau[stufe] = _kiSpielzeit;
            if (buch.Notizen.Count < 24)
                buch.Notizen.Add($"Stufe {stufe} erstmals bei {Zeitwort(_kiSpielzeit)} — "
                               + $"{d.Name} (Waffe {d.Weapon}, Fahrwerk {d.Propulsion}, "
                               + $"Reichweite {d.Range}), Leiter stand auf {leiter}");
        }
        if (stufe > leiter)
        {
            buch.Verstoesse++;
            if (buch.Notizen.Count < 24)
                buch.Notizen.Add($"⚠ ÜBER DER LEITER bei {Zeitwort(_kiSpielzeit)}: {d.Name} "
                               + $"ist Stufe {stufe}, die Leiter stand auf {leiter}");
        }
    }

    private static string Zeitwort(float s) => $"{(int)s / 60:00}:{(int)s % 60:00}";

    /// <summary><c>--ki-stufen-check</c> — die Zeile am Laufende.</summary>
    public string KiStufenCheckLine()
    {
        var sb = new System.Text.StringBuilder("ki-stufen-check\n");
        sb.Append($"  Gegenschalter --ki-stufen-aus: {KiStufenAus}, Kampagne: {InCampaign}, "
                + $"KI: {_ai.Count}, Techstandard der Partie: {UI.SkirmishSetup.Techstandard}, "
                + $"Spielzeit {Zeitwort(_kiSpielzeit)}\n");

        int verstoesseGesamt = 0, gebautGesamt = 0;
        foreach (var a in _ai)
        {
            if (!_kiStufenBuch.TryGetValue(a.Player, out var b))
            {
                sb.Append($"  P{a.Player} ({a.Level}): hat nie eine Bauentscheidung getroffen\n");
                continue;
            }
            var p = ProfilOf(b.Stufe);
            verstoesseGesamt += b.Verstoesse;
            gebautGesamt += b.Gebaut;
            sb.Append($"  P{a.Player} ({b.Stufe}): Leiter {p.TechStart} -> {KiTechLeiter(b.Stufe)} "
                    + $"(eine Stufe je {p.TechSchritt:0} s, Deckel "
                    + $"{Mathf.Clamp(UI.SkirmishSetup.Techstandard + p.TechDeckel, 1, 8)}), "
                    + $"gebaut {b.Gebaut}, Entwuerfe gesperrt {b.Gesperrt}, "
                    + $"Rueckfall auf die ganze Liste {b.Rueckfall}, ueber der Leiter {b.Verstoesse}\n");
            var ms = b.Erstbau[7] >= 0f ? Zeitwort(b.Erstbau[7]) : "NIE";
            sb.Append($"     Techstufe 7 (MS-Rakete, Luftkissen, M-Bombe) zuerst gebaut: {ms}\n");
            sb.Append($"     Angriffswellen {b.Wellen} (Pause {p.WellenPause:0} s), "
                    + $"Spaehfahrten {b.Spaehfahrten} (Pause {p.SpaehPause:0} s, "
                    + $"Trupp {(p.SpaehTrupp > 100 ? "alles Freie" : p.SpaehTrupp.ToString())})\n");
            foreach (var n in b.Notizen) sb.Append($"     {n}\n");
        }

        if (InCampaign || _ai.Count == 0 || gebautGesamt == 0)
            sb.Append("  KEIN URTEIL — kein Gefecht mit KI, oder es wurde nichts gebaut");
        else if (KiStufenAus)
            sb.Append(verstoesseGesamt > 0
                ? $"  NULLMODELL WIE ERWARTET: ohne Tor {verstoesseGesamt} Bauten ueber der Leiter"
                : "  ⚠ NULLMODELL OHNE BEFUND — ohne Tor haette etwas ueber der Leiter gebaut "
                + "werden muessen; laeuft der Lauf lang genug, und baut die KI ueberhaupt frei?");
        else
            sb.Append(verstoesseGesamt == 0
                ? $"  BESTANDEN: {gebautGesamt} Bauten, keiner ueber der eigenen Leiter"
                : $"  DURCHGEFALLEN: {verstoesseGesamt} von {gebautGesamt} Bauten ueber der Leiter");
        return sb.ToString();
    }
}
