namespace AkteEuropaReborn.Rendering;

using System.Text;
using Godot;

/// <summary>
/// <c>--auswahlwinkel-check</c> (bug-590, KayelGee R7: »bei einer Einheit in einer
/// ausgewählten Gruppe ist der Auswahlrahmen dicker«). Weg des Spielers: das
/// Auswahlrechteck (<see cref="BoxSelect"/>) über die ganze Karte, danach je markierter
/// Einheit der Stil, den der Zeichner (<c>_Draw</c>, Aufrufer mit
/// <c>primary = i == _selected</c>) ihr gibt — als reine Funktion
/// <see cref="AuswahlwinkelStil"/>, weil kopflos kein <c>_Draw</c> verlässlich läuft.
/// Soll: N von N gleich (das Original fragt im Zeichner nur <c>+0x1B ≠ 0</c>).
/// Nullmodell <c>--auswahlwinkel-haupt-alt</c>: N−1 von N weichen ab. Ohne Gruppe
/// (N &lt; 3) fällt der Prüfstand durch — nicht gemessen ist nicht grün.
/// </summary>
public partial class MapEntityLayer
{
    public string AuswahlwinkelCheck()
    {
        var sb = new StringBuilder("auswahlwinkel-check (bug-590)"
            + (AuswahlwinkelHauptAlt ? " (⚠ Nullmodell --auswahlwinkel-haupt-alt: MUSS fallen)" : ""));
        BoxSelect(new Rect2(-100000f, -100000f, 200000f, 200000f), false);
        int n = _sel.Count;
        if (n < 3 || _selected < 0)
            return sb.Append($"\n   Gruppe zu klein (N = {n}, Haupt {_selected}) — sagt NICHTS\n   Ergebnis: DURCHGEFALLEN").ToString();
        var haupt = AuswahlwinkelStil(true);
        int gleich = 0;
        string abw = "";
        foreach (int i in _sel)
        {
            var s = AuswahlwinkelStil(i == _selected);
            if (s.Farbe == haupt.Farbe && s.Staerke == haupt.Staerke) gleich++;
            else if (abw.Length == 0) abw = $"; z. B. Platz {_entities[i].Slot}: α {s.Farbe.A:0.00} Staerke {s.Staerke} gegen α {haupt.Farbe.A:0.00}/{haupt.Staerke}";
        }
        bool ok = gleich == n;
        sb.Append($"\n   Gruppe aus dem Auswahlrechteck: N = {n}, Hauptauswahl Platz {_entities[_selected].Slot}");
        sb.Append($"\n   {(ok ? "ok  " : "FEHL")} auswahlwinkel {gleich}/{n} gleich, {n - gleich} von {n} abweichend{abw}");
        _sel.Clear(); SetPrimary();
        return sb.Append($"\n   Ergebnis: {(ok ? "BESTANDEN" : "DURCHGEFALLEN")}").ToString();
    }
}
