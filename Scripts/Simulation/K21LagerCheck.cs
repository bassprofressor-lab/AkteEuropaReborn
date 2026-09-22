namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// <c>--k21-lager-check[=schalter]</c> — PRUEFSTAND fuer die Bahnwirtschaft
/// von Kampagne 21 (22.09.2026, berichte/bahnhof-transport-fable.md §7.1/§7.3).
///
/// <para>Gemeldet: »ich kann nach wie vor keine einheiten bauen trotz das die
/// züge schon mehrmals gefahren sind«. Der Pruefstand gibt dem Spieler die
/// neutralen Karvina-Gebaeude (Basis 0, Fabriken 1..4, Bahnhof 55,
/// Feldbahnhoefe 56/57) — den Zustand nach ihrer Einnahme — und schreibt alle
/// 20 s die Lager der Basis und der drei Bahnhoefe.</para>
///
/// <para>Ohne <c>=schalter</c> ist das die GRUNDLINIE: nach der Matrix kommen
/// nur Waffen zur Basis, Fahrwerk bleibt in 55, Spezial in 56. Mit
/// <c>=schalter</c> setzt er nach 5 s per Befehl 500 wie ein Spieler:
/// Linie 2 (55 -> 56) Waffen und Fahrwerk nach Knoten 2, Linie 5 (56 -> 57)
/// Waffen, Fahrwerk, Spezial nach Knoten 2. Dann MUESSEN Fahrwerk und Spezial
/// in der Basis steigen. Nullmodell: <c>--bahnschalter-matrix-alt</c> —
/// die Matrix ueberschreibt die Schalter, der Pruefstand faellt durch.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool K21LagerCheckAn, K21LagerSchalter;
    private float _k21Zeit, _k21Naechste;
    private bool _k21Gesetzt, _k21Geschaltet;
    private int _k21BasisF0 = -1, _k21BasisS0 = -1;
    private readonly List<string> _k21Zeilen = new();

    /// <summary>Wie oft die LEISTE (W, F oder S) von einem Takt zum naechsten
    /// FIEL. In diesem Lauf baut niemand etwas: Fabriken erzeugen, der Zug
    /// verschiebt nur. Soll 0; unter <c>--leiste-alt</c> faellt sie, sobald ein
    /// Zug laedt (berichte/bahnhof-transport-fable.md §1.4).</summary>
    private int _k21LeisteFiel, _k21LeisteFielUm;
    private (int W, int F, int S)? _k21LeisteVor;

    private static readonly int[] K21Plaetze = { 0, 1, 2, 3, 4, 55, 56, 57 };

    /// <summary>Je Bild aus <see cref="UpdateFreight"/>.</summary>
    private void K21LagerTakt(float dt)
    {
        if (!K21LagerCheckAn) return;
        _k21Zeit += dt;
        if (!_k21Gesetzt)
        {
            _k21Gesetzt = true;
            int n = 0;
            foreach (int p in K21Plaetze)
                if (RailBuilding(p) is { Dead: false } e) { e.Owner = ViewPlayer; n++; }
            var b0 = RailBuilding(0);
            _k21BasisF0 = b0?.StockF ?? -1;
            _k21BasisS0 = b0?.StockS ?? -1;
            GD.Print($"k21-lager-check: {n} von {K21Plaetze.Length} Gebaeuden an Spieler {ViewPlayer} gegeben");
        }
        if (K21LagerSchalter && !_k21Geschaltet && _k21Zeit >= 5f)
        {
            _k21Geschaltet = true;
            bool ok = RailSetFlag(2, GoodW, 1) & RailSetFlag(2, GoodF, 1)
                    & RailSetFlag(5, GoodW, 1) & RailSetFlag(5, GoodF, 1) & RailSetFlag(5, GoodS, 1);
            GD.Print($"k21-lager-check: Befehl 500 x5 (Linie 2: W,F -> Kn2; Linie 5: W,F,S -> Kn2) {(ok ? "angenommen" : "ABGELEHNT")}");
        }
        var h = PlayerStocks(ViewPlayer);
        if (_k21LeisteVor is { } v && (h.W < v.W || h.F < v.F || h.S < v.S))
        {
            _k21LeisteFiel++;
            _k21LeisteFielUm = Mathf.Max(_k21LeisteFielUm,
                Mathf.Max(v.W - h.W, Mathf.Max(v.F - h.F, v.S - h.S)));
        }
        _k21LeisteVor = (h.W, h.F, h.S);
        if (_k21Zeit >= _k21Naechste)
        {
            _k21Naechste += 20f;
            _k21Zeilen.Add(K21LagerZeile());
            GD.Print(_k21Zeilen[^1]);
        }
    }

    private string K21LagerZeile()
    {
        string L(int slot)
        {
            var e = RailBuilding(slot);
            return e == null ? $"{slot}:—" : $"{slot}:W{e.StockW}/F{e.StockF}/S{e.StockS}/T{e.StockT}";
        }
        var hud = PlayerStocks(ViewPlayer);
        string m2 = RailModeOf(2) is { } a ? string.Join("", a) : "-";
        string m5 = RailModeOf(5) is { } b ? string.Join("", b) : "-";
        return $"k21-lager {_k21Zeit,5:0}s  Basis {L(0)}  Bhf {L(55)}  Fbhf {L(56)}  {L(57)}  "
             + $"Leiste W{hud.W}/F{hud.F}/S{hud.S}/T{hud.T}  Schalter L2 {m2} L5 {m5}";
    }

    /// <summary>Für die Prüfstände: die Karvina-Plätze dem Betrachter geben.</summary>
    public int K21PlaetzeGeben()
    {
        if (_bldBySlot.Count == 0) RebuildRailIndex();
        int n = 0;
        foreach (int p in K21Plaetze)
            if (RailBuilding(p) is { Dead: false } e) { e.Owner = ViewPlayer; n++; }
        return n;
    }

    /// <summary>Entitätsindex eines Gebäudeplatzes, oder -1.</summary>
    public int GebaeudeIndexVonPlatz(int slot) => EntityOfSlot(slot);

    /// <summary>Einheiten in der Garage eines Gebäudes.</summary>
    public int GarageZahl(int idx) => idx >= 0 && idx < _entities.Count ? _entities[idx].Garage.Count : -1;

    /// <summary>Eine Zelle im Grundriss eines Gebäudes (für den Kartenklick).</summary>
    public Vector2I GebaeudeZelle(int idx)
    {
        var e = _entities[idx];
        return new Vector2I(e.Col + Mathf.Max(1, e.FootW) / 2, e.Row + Mathf.Max(1, e.FootH) / 2);
    }

    /// <summary>Welche Linien einen NEUTRALEN Knoten 1 haben und welche Farbe sie
    /// bekommen — Soll 10 (@0x42B6A0).</summary>
    public string BahnNeutraleLinienZeile()
    {
        var sb = new StringBuilder("Zugfarbe: ");
        int n = 0;
        foreach (var l in _railLines)
        {
            var a = RailBuilding(l.Bud1);
            if (a == null || a.Owner != NeutralOwner) continue;
            sb.Append($"L{l.Slot}->{ZugBesitzerFarbe(l.Slot)} ");
            n++;
        }
        return n == 0 ? "Zugfarbe: keine Linie mit neutralem Knoten 1" : sb.Append("(Soll je 10)").ToString();
    }

    public string K21LagerCheckLine()
    {
        if (!K21LagerCheckAn) return "";
        var sb = new StringBuilder("--k21-lager-check" + (K21LagerSchalter ? "=schalter" : "") + "\n");
        sb.AppendLine("  " + K21LagerZeile());
        var b0 = RailBuilding(0);
        if (b0 == null) return sb.Append("  KEIN URTEIL — keine Basis auf Platz 0 (ist das K21?)").ToString();
        bool fStieg = b0.StockF > _k21BasisF0, sStieg = b0.StockS > _k21BasisS0;
        sb.AppendLine($"  Basis Fahrwerk {_k21BasisF0} -> {b0.StockF}, Spezial {_k21BasisS0} -> {b0.StockS}");
        sb.AppendLine($"  Leiste fiel {_k21LeisteFiel}x (hoechstens um {_k21LeisteFielUm}) — Soll 0"
                      + (LeisteAlt ? "   ⚠ NULLMODELL --leiste-alt: MUSS hier > 0 sein" : ""));
        if (!K21LagerSchalter)
            return sb.Append("  GRUNDLINIE (ohne Befehl 500) — Soll nach der Matrix: F und S bleiben "
                             + (fStieg || sStieg ? "— ⚠ SIE STIEGEN" : "stehen")).ToString();
        if (BahnschalterMatrixAlt)
            sb.AppendLine("  ⚠ NULLMODELL --bahnschalter-matrix-alt: MUSS hier durchfallen");
        return sb.Append(fStieg && sStieg ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
