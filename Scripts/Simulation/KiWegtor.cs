using Godot;
namespace AkteEuropaReborn.Rendering;

/// <summary>
/// ⭐⭐ <b>DAS WEGTOR DES SEKTORANGRIFFS</b> — C <c>0x4BC74E…0x4BC76B</c> /
/// F <c>0x4BC20E…0x4BC22B</c> (KayelGee 14.10.2026, Punkt 14; bug-576).
///
/// <code>
///   0x4BC6FD  call 0x401163            ; get_target_in_sector (0x4BC3D0)
///   0x4BC70A  cmp ax, 0xFFFF / je      ; kein Ziel -> naechster Nachbar
///   0x4BC74E  call 0x401960            ; = jmp 0x4D2580, 1x1-Breitensuche
///                                      ;   (erste abrufbereite Einheit -> Ziel)
///   0x4BC75C  cmp word[0xBCA0E0], 0x32 ; die Wellenzahl = Weglaenge
///   0x4BC76B  jae 0x4BC804             ; >= 50: NAECHSTER NACHBAR, niemand faehrt
///   ...       Klang 122 (FeindNaht.cs), dann order() fuer alle des Sektors
///   0x4D2869  mov word[0xBCA0E0], ax   ; einziger Schreiber, in 0x4D2580 nach
///                                      ; erreichtem Ziel (Marke 5); ohne Weg
///                                      ; kehrt 0x4D2580 VORHER mit 0 zurueck
/// </code>
///
/// <para>Bei uns fehlte das Tor: der Sektorangriff schickte über die reine
/// Sektornachbarschaft (bis ~68 Zellen Luftlinie), auch wenn der Weg über eine
/// ferne Brücke führt. In M3 sieht Weiß (Spieler 5, Lhota) die Mittelbasis Dolni
/// im Diagonalnachbarsektor; der Weg dorthin führt über die letzte Brücke
/// (81,73) und ist länger als 50 Schritte.</para>
///
/// <para>⚠ <b>SETZUNGEN, benannt:</b> (a) die Weglänge rechnet unsere
/// <see cref="Simulation.NavGrid.FindPathUr"/> — dieselbe Breitensuche, aber
/// unsere Karte und die Zielwahl von <c>Search:</c>; (b) ohne Weg entscheidet das
/// Original mit der Länge der VORIGEN Suche irgendeiner Einheit (der Wert in
/// <c>0xBCA0E0</c> bleibt stehen) — wir werten »kein Weg« als »Tor zu«.</para>
///
/// <para>Gegenschalter <c>--ki-wegtor-alt</c>. Prüfstand <c>--feind-naht-check</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die Grenze des Wegtors: <c>cmp word[0xBCA0E0], 0x32; jae</c>.</summary>
    public const int WegtorGrenze = 50;

    /// <summary><c>--ki-wegtor-alt</c>: der Sektorangriff ohne das Wegtor
    /// <c>0x4BC74E…0x4BC76B</c> (Stand bis 0.6.9).</summary>
    public static bool KiWegtorAlt;

    /// <summary>Wegtor-Zähler: offen, zu (Weg &gt;= 50), zu (kein Weg).</summary>
    public int WegtorOffen, WegtorZuLang, WegtorKeinWeg;
    public int WegtorLaengste = -1;
    public readonly System.Collections.Generic.List<string> WegtorProtokoll = new();

    /// <summary>
    /// Das Wegtor von Stelle 1: ist der Weg der ERSTEN abrufbereiten Einheit
    /// zum Ziel kürzer als 50 Schritte? (<c>0x4BC74E call 0x4D2580</c>,
    /// <c>0x4BC75C cmp word[0xBCA0E0], 0x32 / jae 0x4BC804</c>.)
    /// </summary>
    private bool AiWegtorOffen(int p, int einheit, int ziel)
    {
        if (KiWegtorAlt || _nav == null) return true;
        if (einheit < 0 || einheit >= _entities.Count || ziel < 0 || ziel >= _entities.Count) return true;
        var e = _entities[einheit];
        var z = _entities[ziel];
        var weg = _nav.FindPathUr(new Vector2I(e.Col, e.Row), new Vector2I(z.Col, z.Row), e.Move, einheit);
        int laenge = weg == null ? -1 : Simulation.NavGrid.UrWellen;
        bool offen = laenge >= 0 && laenge < WegtorGrenze;
        if (offen) WegtorOffen++;
        else if (laenge < 0) WegtorKeinWeg++;
        else WegtorZuLang++;
        if (laenge > WegtorLaengste) WegtorLaengste = laenge;
        if (WegtorProtokoll.Count < 400)
            WegtorProtokoll.Add($"P{p} Platz {e.Slot} ({e.Col},{e.Row}) -> {z.Name} P{z.Owner} ({z.Col},{z.Row}): " +
                                $"Weg {(laenge < 0 ? "keiner" : laenge.ToString())} -> {(offen ? "OFFEN" : "ZU")} (Takt {_taktNr})");
        return offen;
    }
}
