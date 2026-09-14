using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>DER BAUZUSTAND</b> eines im Feld gebauten Gebäudes (Depot, Generator,
/// Feld-Rohstoffmine) — 13.09.2026, Kampagne 13. Lesung:
/// <c>berichte/generator-fable.md</c> §5.3 und §6.
///
/// <list type="bullet">
/// <item><c>add_building</c> <c>0x4C91B0</c> setzt <c>+0x0A := 100</c>.</item>
/// <item>Gebäudetakt <c>0x43CA9F…0x43CB24</c> (F <c>0x43BB3F</c>): nur jeden
/// ZWEITEN Takt <c>+0x0A += 1</c>; bei 250 → 1 (fertig). Der übrige Takt
/// entfällt. 150 Erhöhungen × 2 = <b>300 Takte = 6 s</b>.</item>
/// <item>Solange ≥ 100: <b>unverwundbar</b> (<c>0x40D295</c>), <b>nicht
/// anklickbar</b> (Zeigerwahl <c>0x4324ED</c>), gezeichnet als <b>Gerüst</b>
/// <c>(b − 100)/50</c> ∈ {0, 1, 2} aus Musterzeile Typtafel+0x08 − 2 + Bild
/// (Stempler <c>0x4C95E0</c>). Strom liefert ein Generator schon im Bau
/// (<c>0x440340</c> fragt nichts).</item>
/// </list>
///
/// <para>⚠ UNSERE Setzungen: der Takt hängt am Originaltakt (50 Hz) mit
/// <c>_origTicks</c> gerade; das Freigeben der Türzelle bei 250 (@0x43CAD9) ist
/// nicht nachgebaut — unsere Tür wird beim Setzen gestempelt; ein Skripttreffer
/// (<c>hit_cell</c>) auf ein Gebäude im Bau ist nicht gesperrt.</para>
///
/// <para>Gegenschalter <c>--bauzustand-aus</c>, <c>--bauauftrag-alt</c>
/// (Auftrag verfällt, kein Klang 42), <c>--generatorfenster-alt</c>,
/// <c>--panzerung-neubau-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool BauzustandAus, BauauftragAlt, GeneratorfensterAlt, PanzerungNeubauAlt;

    /// <summary><c>--geruest-aus</c>: kein Geruest, ein Bau zeigt das fertige Gebaeude
    /// (der Stand bis 14.09.2026). Das Geruest selbst ist seit dem 14.09. gegen die
    /// Daten geprueft (berichte/bauanimation-fable.md).</summary>
    public static bool GeruestAus;

    public const int BauzustandStart = 100, BauzustandEnde = 250;

    public int BauzustandFertig;

    /// <summary>Im Originaltakt — @0x43CA9F.</summary>
    private void BauzustandTakt()
    {
        if ((_origTicks & 1) != 0) return;                  // @0x43CAAE: nur jeden zweiten
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.Dead || b.Bauzustand < BauzustandStart) continue;
            b.Bauzustand++;                                  // @0x43CAC5
            if (b.Bauzustand >= BauzustandEnde)
            {
                b.Bauzustand = 0;                            // @0x43CACD: := 1 = heil
                BauzustandFertig++;
            }
            QueueRedraw();
        }
    }

    /// <summary>Die Musterzeile des Gerüsts (Stempler 0x4C95E0 @0x4C9619).</summary>
    private static int GeruestMuster(Import.CwpFile.BuildingType bt, int bauzustand)
        => bt.TilePattern - 2 + Mathf.Clamp((bauzustand - BauzustandStart) / 50, 0, 2);

    /// <summary>Panzerung +0x08 aus der EXE-Typtafel <c>0x539DBA + 10·typ</c>
    /// (generator-fable.md §7): Basis 10, Fabriken 8, Depot/Bahnhof/Generator/Mine 7,
    /// Flughafen/Hafen 10, Feldmine 4.</summary>
    public static int NeubauPanzerung(int typ) => typ switch
    {
        1 => 10,
        2 or 3 or 4 => 8,
        5 or 6 or 7 or 10 => 7,
        9 or 11 => 10,
        15 => 4,
        _ => 0,
    };

    // ---- das Generatorfenster (Fensterart 20) --------------------------------

    /// <summary>»Stromerzeugung : n« = sec26-Byte +0x02: der Kartenlader stempelt 50
    /// (0x41F3C6), der Platzvergeber beim Neubau NICHT — ein selbst gebauter
    /// Generator zeigt im Original 0. Wörtlich übernommen.</summary>
    private static int GeneratorAnzeige(Entity e) => e.State;   // Karte: state 50, Neubau: 0

    /// <summary>Der Bauzustands-Prüfstand: eine Generator-Einheit baut, wir zählen
    /// mit. Siehe <c>--generator-check</c> in Rendering/GeneratorLauf.cs.</summary>
    public (int Bau, bool Fenster, int Hp) BauzustandProbe(Entity b)
        => (b.Bauzustand, FensterArtVon(b) != null, b.Hp);
}
