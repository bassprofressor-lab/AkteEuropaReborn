namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// ⭐⭐ <b>DIE TAB-BALKEN</b> (03.10.2026, bug-412, Meldung B von KayelGee,
/// <c>berichte/maus-tab-handsteuerung-fable.md</c> §2).
///
/// <para><b>Original (O):</b> <c>VK_TAB</c> → <c>0x412FF5</c>:
/// <c>byte[0xA31A88] += 1; == 4 → 0</c> — der Ring <b>0 → 1 → 2 → 3 → 0</b>.
/// Der Einheitenzeichner fragt ihn bei <c>0x42AAA3</c>: <b>0 = gar kein
/// Balken</b> (der Verteiler <c>0x4B71F0</c> wird nicht einmal gerufen), und
/// das UNABHÄNGIG von Auswahl und Schaden. Sonst verteilt <c>0x4B71F0</c>
/// über die Gattung (<c>+0x0A</c>, Tafel <c>0x4B78BC</c>):</para>
/// <list type="bullet">
/// <item><b>1 Leben</b> — für ALLE gezeichneten Einheiten aller Spieler:
/// Breite <c>(HpMax&gt;&gt;2)+2</c>, Füllung <c>Hp&gt;&gt;2</c>, Farben 5/13/9.</item>
/// <item><b>2 Sprit</b> — nur EIGENE (<c>einheit/1000 == byte[0x4FA284]</c>),
/// Fahrzeug <c>0x4B72A7</c>: Breite <c>word[+0x30]/20+2</c>, Füllung
/// <c>word[+0x2E]/20</c>, Platz 3 <c>#2B53CB</c>.</item>
/// <item><b>3 Munition</b> — nur eigene, <c>0x4B72E9</c>: Breite
/// <c>(byte[+0x3A]&gt;&gt;2)+2</c>, Füllung <c>byte[+0x39]&gt;&gt;2</c>, Platz 0x54
/// <c>#E3C793</c>.</item>
/// <item>Gattung 1 Fußvolk (<c>0x4B732D</c>): nur Art 1, Höhe <b>4</b>.
/// Gattung 2: nichts (<c>0x4B78B5</c>). Gattung 3/4/5: Höhe <b>6</b>.</item>
/// <item>Ohne Sprit/Munition (Max 0): Breite 2, Füllung 0 → der Zeichner
/// <c>0x4B6F60</c> malt einen <b>2×5-Rahmenstummel</b> — so gebaut.</item>
/// </list>
///
/// <para>Rahmen und Füllung zeichnet <see cref="TabBalkenMalen"/> mit demselben
/// Muster wie der Lebensbalken aus bug-410 (Umriss in Spielerfarbe
/// <see cref="BalkenRahmen"/>, Füllung innen ab x+1/y+1, <c>fuellung−1</c>
/// Spalten × <c>hoehe−2</c> Zeilen).</para>
///
/// <para>⚠ <b>UNSERE Setzungen:</b> (a) der STARTWERT 0 — wo das Original
/// <c>0xA31A88</c> beim Missionsstart setzt, ist nicht gelesen (der Schreiber
/// <c>0x413009</c> »auf 0« ist nicht verfolgt). (b) GEBÄUDE: der Zeichner
/// <c>0x42AAA3</c> sitzt im EINHEITENzeichner; ob und wie ein Gebäude einen
/// Balken trägt, ist nicht gelesen (V) — bei uns folgen Gebäude dem Ring nur
/// in Art 1 (Leben), wie eine Einheit der Gattung 0. (c) Die Lage der Balken
/// der Gattungen 3/4/5 (<c>x+35</c>, <c>y+5/15/92</c>) ist NICHT nachgebaut —
/// der Balken hängt am gewohnten <see cref="BalkenHub"/>; nur die Höhe 6 ist
/// übernommen.</para>
///
/// <para>Gegenschalter: <c>--balkenmodus-alt</c> (Balken wie vorher bei Anwahl
/// oder Schaden, Fußvolk Höhe 5) und <c>--tab-alt</c> (Tab = Ereignissprung;
/// schliesst die alten Balken mit ein, sonst bliebe der Ring auf 0 und es gäbe
/// gar keine).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>byte[0xA31A88]</c> — die Balkenbetriebsart 0…3.
    /// ⚠ UNSERE Setzung: Startwert 0 (siehe Kopf).</summary>
    public int Balkenmodus;

    /// <summary><c>--balkenmodus-alt</c>: Lebensbalken wieder bei Anwahl oder
    /// Schaden, kein Sprit/Munition, Fußvolk Höhe 5 (Stand vor bug-412).</summary>
    public static bool BalkenmodusAlt;

    /// <summary><c>--tab-alt</c>: Tab springt wieder zum letzten Ereignis
    /// (<c>JumpToEvent</c>, unsere Zutat), und die Balken sind die alten.</summary>
    public static bool TabAlt;

    /// <summary>Gelten die Tab-Balken des Originals?</summary>
    public static bool TabBalkenNeu => !BalkenmodusAlt && !TabAlt;

    /// <summary>Füllfarbe Art 2 Sprit: Palettenplatz 3 aus <c>DATA/01.PAL</c>.</summary>
    private static readonly Color BalkenSprit = new("2B53CB");
    /// <summary>Füllfarbe Art 3 Munition: Palettenplatz 0x54 aus <c>DATA/01.PAL</c>.</summary>
    private static readonly Color BalkenMunition = new("E3C793");

    /// <summary>Tab: <c>0x412FF5</c>, Ring 0→1→2→3→0. Gibt die neue Stellung zurück.</summary>
    public int BalkenmodusWeiter()
    {
        Balkenmodus++;
        if (Balkenmodus == 4) Balkenmodus = 0;
        QueueRedraw();
        return Balkenmodus;
    }

    /// <summary>Ein Balken, wie ihn <c>0x4B71F0</c> an <c>0x4B6F60</c> übergibt.</summary>
    public readonly record struct TabBalken(int Breite, int Hoehe, int Fuellung, Color Farbe);

    /// <summary>
    /// Der Verteiler <c>0x4B71F0</c> für eine Stellung: welcher Balken (oder
    /// keiner) über dieser Einheit steht. Rein — keine Zeichnung, kein Zustand;
    /// <see cref="TabBalkenMalen"/> und der Prüfstand nehmen beide diese Zeile.
    /// </summary>
    public TabBalken? TabBalkenFuer(Entity e, int art)
    {
        if (art == 0) return null;                           // 0x42AAA3: AUS
        if (e.IsProp || e.Dead || e.HpMax <= 0) return null; // UKOL 100 sterbend → keiner
        bool fuss = e.Infantry >= 0 || e.GameUnitType == 1;
        if (!e.IsBuilding && !fuss && e.GameUnitType == 2) return null;   // 0x4B78B5
        int hoehe = fuss ? 4 : e.GameUnitType is 3 or 4 or 5 ? 6 : 5;
        if (art == 1)
        {
            int breite = (e.HpMax >> 2) + 2;                 // 0x4B724C
            int fuell = System.Math.Max(0, e.Hp) >> 2;
            int innen = breite - 2;
            // 0x4B70B5: 2·f ≥ innen → 5 grün, 4·f < innen → 9 rot, sonst 13 gelb
            var fb = 2 * fuell >= innen ? BarGreen : 4 * fuell < innen ? BarRed : BarYellow;
            return new TabBalken(breite, hoehe, fuell, fb);
        }
        // Art 2/3: nur EIGENE, nicht Fußvolk (0x4B732D), ⚠ (V) nicht Gebäude.
        if (fuss || e.IsBuilding || e.Owner != ViewPlayer) return null;
        if (art == 2)
            return new TabBalken(e.FuelMax / 20 + 2, hoehe,        // 0x4B72A7, idiv 20
                                 System.Math.Max(0, e.Fuel) / 20, BalkenSprit);
        return new TabBalken((e.AmmoMax >> 2) + 2, hoehe,           // 0x4B72E9
                             System.Math.Max(0, e.Ammo) >> 2, BalkenMunition);
    }

    /// <summary>Zeichnet einen Tab-Balken mit dem Muster von <c>0x4B6F60</c>
    /// (wie bug-410): zentriert, Umriss 1 px in Spielerfarbe, Füllung innen.</summary>
    private void TabBalkenMalen(Entity e, TabBalken b)
    {
        float bw = b.Breite, h = b.Hoehe;
        var hb = e.Pos + new Vector2(-bw / 2f, -BalkenHub(e));
        hb = new Vector2(Mathf.Floor(hb.X), Mathf.Floor(hb.Y));
        var rc = e.Owner >= 0 && e.Owner < BalkenRahmen.Length ? BalkenRahmen[e.Owner] : PropColor;
        DrawRect(new Rect2(hb, new Vector2(bw, 1)), rc);
        DrawRect(new Rect2(hb + new Vector2(0, h - 1), new Vector2(bw, 1)), rc);
        if (h > 2)
        {
            DrawRect(new Rect2(hb + new Vector2(0, 1), new Vector2(1, h - 2)), rc);
            DrawRect(new Rect2(hb + new Vector2(bw - 1, 1), new Vector2(1, h - 2)), rc);
        }
        if (b.Fuellung > 1 && h > 2)
            DrawRect(new Rect2(hb + Vector2.One, new Vector2(b.Fuellung - 1, h - 2)), b.Farbe);
    }

    /// <summary>Der alte Ausstieg (vor bug-412): Balken nur bei Anwahl oder Schaden.</summary>
    private bool AlterBalkenGezeigt(int i, Entity e) => _sel.Contains(i) || e.Hp < e.HpMax;

    // ---- --tabbalken-check -------------------------------------------------

    public static bool TabbalkenCheckAn;

    /// <summary>Eine Stellung auszählen: dieselben Ausstiege wie die
    /// Zeichenschleife (Kulisse, tot, HpMax 0, Nebel), dann je Einheit
    /// <see cref="TabBalkenFuer"/> — bzw. im Nullmodell die alte Regel. Die
    /// Breite wird gegen die Formel des Berichts GEGENGERECHNET (eigene
    /// Zeilen, nicht aus <see cref="TabBalkenFuer"/>).</summary>
    public (int eigene, int fremde, int eigeneSprit, int fussMitSprit, int formelFehler, int stummel)
        TabBalkenZaehlen()
    {
        int eig = 0, fre = 0, sprit = 0, fussSprit = 0, fehler = 0, stummel = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.IsProp || e.Dead || e.HpMax <= 0) continue;
            if (ImNebelVerborgen(e)) continue;
            bool eigen = e.Owner == ViewPlayer;
            if (!TabBalkenNeu)
            {
                if (!AlterBalkenGezeigt(i, e)) continue;
                if (eigen) eig++; else fre++;
                continue;
            }
            var b = TabBalkenFuer(e, Balkenmodus);
            if (b == null) continue;
            if (eigen) eig++; else fre++;
            bool fuss = e.Infantry >= 0 || e.GameUnitType == 1;
            if (Balkenmodus >= 2 && fuss) fussSprit++;
            if (Balkenmodus == 2 && eigen) sprit++;
            int soll = Balkenmodus switch
            {
                1 => e.HpMax / 4 + 2,
                2 => e.FuelMax / 20 + 2,
                _ => e.AmmoMax / 4 + 2,
            };
            int sollFuell = Balkenmodus switch
            {
                1 => e.Hp / 4,
                2 => e.Fuel / 20,
                _ => e.Ammo / 4,
            };
            int sollH = fuss ? 4 : e.GameUnitType is 3 or 4 or 5 ? 6 : 5;
            if (b.Value.Breite != soll || b.Value.Fuellung != sollFuell || b.Value.Hoehe != sollH) fehler++;
            if (b.Value.Breite == 2) stummel++;
        }
        return (eig, fre, sprit, fussSprit, fehler, stummel);
    }

    /// <summary>Für den Prüfstand: eine eigene Einheit anwählen (damit das
    /// Nullmodell in Stellung 0 einen Balken zeigt).</summary>
    public int TabBalkenProbeWaehle()
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsProp || e.IsBuilding || e.Owner != ViewPlayer || e.HpMax <= 0) continue;
            _sel.Clear(); _sel.Add(i); SetPrimary();
            return i;
        }
        return -1;
    }
}
