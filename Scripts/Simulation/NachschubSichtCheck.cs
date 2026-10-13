namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--nachschub-sicht-check</c> (bug-500, bug-505, KayelGee R5#3/#5): kauft am
/// ersten Nachschubposten der Karte zwei Helis fuer den Spieler — der Weg ueber
/// <see cref="BuyAircraft"/>, den auch der Knopf nimmt. Auszuwerten sind danach
/// die Zeilen <c>nachschub-sicht:</c> (Soll: keiner deckt auf) und
/// <c>flughuepfen:</c> (Soll: 0 Spruenge). Gedacht fuer K2 (Huegel, der Posten
/// aus der Meldung): <c>--campaign=2 --no-briefing</c>.
/// </summary>
public partial class MapEntityLayer
{
    public string NachschubSichtProbe()
    {
        foreach (var e in _entities)
        {
            if (e.Dead || !IsSupplyDepot(e)) continue;
            int who = ViewPlayer is >= 0 and <= 7 ? ViewPlayer : 0;
            _money[who] += 2 * HeliPrice;
            int n = 0;
            for (int k = 0; k < 2; k++)
            {
                e.MenuIndex = k;
                if (BuyAircraft(e)) n++;
            }
            return $"nachschub-sicht-check: Posten ({e.Col},{e.Row}) — {n} Helis gekauft ({_order})";
        }
        return "nachschub-sicht-check: kein Nachschubposten auf dieser Karte — dieser Lauf sagt NICHTS";
    }
}
