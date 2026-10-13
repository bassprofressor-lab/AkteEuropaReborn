namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>Was ein Schuss ohne Flugbild zeigt, und welche Art die Dicke Bertha
/// schiesst</b> (KayelGee R5#23 und #24, berichte/kayelgee-runde5-waffen.md).
///
/// <para><b>#23 »MG und leichte Infanterie schiessen Laser«:</b> jede Waffe ohne
/// Flugbild zog bei uns eine gelbe Leuchtspur. Im Original hat die Leuchtspur
/// (Tafel 0x88C450, Anmelder 0x4551A0) genau EINEN Rufer, 0x451CCD im Zweig
/// <c>cmp al,0xC</c> — nur Art 12, die Blitzschleuder. Der Fussvolkschuss
/// 0x40EC70 fragt bei 0x40ED66 die Waffe: 191 (Laser) und 194 (LaserXXL) gehen
/// ueber 0x40EE83 in den Strahlanleger 0x455320, alle anderen bei 0x40ED7A
/// sofort ohne Bild in die Wirkung. Das Fahrzeug-MG (Art 3/4, Flugfolge 30000)
/// hat kein Flugbild und keine Linie.</para>
///
/// <para><b>#24 »die Bertha schiesst keine Raketen«:</b> ihre Waffe +0x0C ist 61,
/// der Turmteil — <c>WeaponRowOf(61)</c> = −1, also Art −1, Leuchtspur und
/// Sofort-Treffer. Der Schuss 0x40BB00 legt die Art fuer Klasse 3 FEST
/// (Sprungtafel 0x40C5C8, Eintrag 3 = 0x40BF56): <c>cmp cx,0xA0 / setle al /
/// add al,0x14</c> — bis 160 Feineinheiten (4 Zellen) Art 21, darueber Art 20
/// (Doppelrakete, Flugfolge 65). Derselbe Zweig im Schusstest 0x453D5F.</para>
///
/// <para>Gegenschalter <c>--leuchtspur-alt</c> (jede Waffe ohne Flugbild zieht
/// wieder die Linie) und <c>--bertha-waffe-alt</c> (Art aus der Statstafel, −1).</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--leuchtspur-alt</c> — Stand 0.6.7. bug-514.</summary>
    public static bool LeuchtspurAlt;

    /// <summary><c>--bertha-waffe-alt</c> — Stand 0.6.7. bug-515.</summary>
    public static bool BerthaWaffeAlt;

    /// <summary>Fuer die Pruefzeile: Schuesse je Bild.</summary>
    public static int SchussLeuchtspuren, SchussOhneBild, SchussBodenstrahlen, BerthaArt20, BerthaArt21;

    /// <summary>Die Geschossart eines Schusses auf die Zelle (zc, zr).</summary>
    private int SchussArt(Entity s, int zc, int zr)
    {
        if (!BerthaWaffeAlt && s.GameUnitType == 3 && !s.IsBuilding)
        {
            // cx = Entfernung in Feineinheiten, beide Achsen ·40 (0x40BEAB/0x40BEE0).
            // ⚠ UNSERE LESART: euklidisch — welche Norm 0x40BEAB..0x40BF56 bildet,
            // steht im Bericht nicht ausgezaehlt.
            float cx = new Vector2((zc - s.Col) * 40, (zr - s.Row) * 40).Length();
            return cx <= 160f ? 21 : 20;
        }
        return Simulation.DesignMath.SoundClass(WeaponRowOf(s.Weapon));
    }

    /// <summary>Strahlwaffe des Fussvolks (0x40ED66: 191 Laser, 194 LaserXXL)?</summary>
    private static bool FussStrahl(Entity s)
        => s.Infantry >= 0 && (s.Weapon - InfCompBase) is 191 or 194;

    /// <summary>
    /// Der Schuss ohne Flugbild. true = erledigt (der Aufrufer trifft nicht mehr selbst).
    /// </summary>
    private bool SchussOhneFlugbild(int si, Entity shooter, int vi, Entity victim, int art, Vector2 dir, int schaden)
    {
        var start = shooter.Pos + TurretOffset(shooter.UnitType, shooter.Col, shooter.Row, shooter.Facing, shooter)
                    + dir * MuzzleReach;
        if (LeuchtspurAlt)
        {
            _tracers.Add((start, victim.Pos - new Vector2(0, 6), 0.10f));
            SchussLeuchtspuren++;
            return false;
        }
        if (FussStrahl(shooter))
        {
            // der Strahlanleger 0x455320 — derselbe wie beim Kampfhubschrauber;
            // der Treffer sitzt bei Ankunft auf der ZIELZELLE (StrahlTakt).
            // ⚠ UNSERE SETZUNG: EIN Strahl (der Hubschrauber hat zwei Muendungen,
            // die Handwaffe eine) und der gruene Punkt 74 (»der gruene der
            // Boden-Laserhandwaffen«, Luftschuss.StrahlTakt).
            _strahlen.Add(new Strahl
            {
                Pos = start, Ziel = victim.Pos, Zielgriff = vi, Schaden = schaden,
                Owner = shooter.Owner, Name = LabelOf(shooter), Schuetze = si, Punkt = "strahlpunkt_gruen",
            });
            SchussBodenstrahlen++;
            return true;
        }
        if (art == 12)
        {
            _tracers.Add((start, victim.Pos - new Vector2(0, 6), 0.10f));
            SchussLeuchtspuren++;
        }
        else SchussOhneBild++;
        return false;
    }

    /// <summary><c>--mzf-anker-alt</c> — Stand 0.6.7: das Muendungsfeuer haengt an
    /// der Leinwandmitte. bug-517.</summary>
    public static bool MzfAnkerAlt;
    private static readonly System.Collections.Generic.Dictionary<string, (Vector2 Alt, Vector2 Neu)> _mzfAnker = new();

    /// <summary>
    /// bug-517 (KayelGee R5#1, »das Muendungsfeuer sitzt immer noch weit weg vom
    /// Rohr«): die Folgen mzf45..48 legt der Ausgeber ab x=0/y=YOffset auf eine
    /// gemeinsame Leinwand, der sichtbare Inhalt sitzt UNTEN RECHTS. Mit dem
    /// Anker Leinwandmitte lag das Feuer (+7,+9) Spielpunkte neben dem Punkt,
    /// den Fire meint. bug-454 hatte nur die Richtung berichtigt.
    /// ⚠ UNSERE SETZUNG (Zwischenloesung): Anker = Mitte des sichtbaren Kastens
    /// ueber alle Bilder der Folge. Das Original setzt das Bild an einen
    /// SHOOT.CWT-Punkt des Turmbildes (0x42A15F..0x42A1EA) und laesst es am Turm
    /// mitfahren — SHOOT.CWT ist nicht ausgegeben.
    /// </summary>
    private static Vector2? MzfAnker(string kind, int bilder)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        Vector2 groesse = Vector2.Zero;
        for (int i = 0; i < bilder; i++)
        {
            string path = Core.Content.Path($"Effects/{kind}/f{i:00}.png");
            if (!FileAccess.FileExists(path)) continue;
            var img = Image.LoadFromFile(path);
            if (img == null) continue;
            groesse = new Vector2(img.GetWidth(), img.GetHeight());
            for (int y = 0; y < img.GetHeight(); y++)
                for (int x = 0; x < img.GetWidth(); x++)
                    if (img.GetPixel(x, y).A > 0.1f)
                    {
                        if (x < x0) x0 = x; if (y < y0) y0 = y;
                        if (x > x1) x1 = x; if (y > y1) y1 = y;
                    }
        }
        if (x1 < 0) return null;
        var neu = new Vector2((x0 + x1 + 1) / 2f, (y0 + y1 + 1) / 2f);
        _mzfAnker[kind] = (groesse / 2f, neu);
        return neu;
    }

    public string SchussbildLine()
        => $"schussbild: Leuchtspuren {SchussLeuchtspuren}, ohne Bild {SchussOhneBild}, Bodenstrahlen {SchussBodenstrahlen}; " +
           $"Bertha Art 20 {BerthaArt20}x, Art 21 {BerthaArt21}x; Rumpfziel 16 Stufen: auf richtung*2 gesetzt {Blick16Gerade}x, Bertha UNGERADE {Blick16Ungerade}x" +
           (_mzfAnker.Count == 0 ? "; Muendungsfeuer: nicht geladen" : "; Muendungsfeuer-Anker " +
               string.Join(", ", System.Linq.Enumerable.Select(_mzfAnker, k => $"{k.Key} ({k.Value.Neu.X:0.#},{k.Value.Neu.Y:0.#}) statt ({k.Value.Alt.X:0.#},{k.Value.Alt.Y:0.#})"))) +
           (MzfAnkerAlt ? "   ⚠ Nullmodell --mzf-anker-alt" : "") +
           (LeuchtspurAlt ? "   ⚠ Nullmodell --leuchtspur-alt" : "") + (BerthaWaffeAlt ? "   ⚠ Nullmodell --bertha-waffe-alt" : "");
}
