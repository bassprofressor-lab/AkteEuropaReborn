namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <c>--leichenanker-check</c> — <b>liegt die Leiche dort, wo der Soldat
/// stand?</b> (11.09.2026, bug-175)
///
/// <para>Fuer jeden lebenden Fusssoldaten der Karte: die UNTERKANTE der
/// sichtbaren Flaeche, einmal im Stehbild am Zeichenort des Lebenden, einmal im
/// Leichenbild am Zeichenort des Toten (der Soldat wird dafuer kurz auf
/// <c>Dead</c> gestellt und sofort zurueck). Der Unterschied ist, um wie viele
/// Punkte die Leiche ueber den Fuessen liegt.</para>
///
/// <para>⚠ Gefragt wird <see cref="FussvolkOrt"/> — die Funktion, die BEIDE
/// Zeichenzweige in <c>DrawUnitBody</c> aufrufen. Das ist der Unterschied zu
/// <c>--anker-probe</c>, die nur <c>FussVersatzFuer</c> befragte und die
/// Leichenzeile, die ihn nicht rief, nie zu sehen bekam.</para>
///
/// <para><b>Soll:</b> hoechstens 8 px (das liegende Bild reicht unten etwas
/// weiter als das stehende — die Silhouette, nicht der Anker). ⚠ Die 8 ist
/// UNSERE Schwelle. Nullmodell <c>--leichenanker-alt</c>: der Bericht misst
/// 29…36 px.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string LeichenankerCheck()
    {
        var sb = new System.Text.StringBuilder("leichenanker-check\n");
        var werte = new List<int>();
        int ohneBild = 0;
        string beispiel = "";
        foreach (var e in _entities)
        {
            if (e.Dead || e.IsBuilding || e.IsProp || e.Infantry < 0) continue;
            var steh = GetInfantryTexture(e.Infantry, e.Facing, InfIdleBlock);
            var ortLebend = FussvolkOrt(e);

            float deadTime = e.DeadTime;
            e.Dead = true;
            e.DeadTime = 99f;                   // das letzte Sterbebild: die liegende Leiche
            var leiche = GetInfantryTexture(e.Infantry, InfDrawFacing(e), InfBlock(e));
            var ortTot = FussvolkOrt(e);
            e.Dead = false;
            e.DeadTime = deadTime;

            int uL = Unterkante(steh), uT = Unterkante(leiche);
            if (uL < 0 || uT < 0) { ohneBild++; continue; }
            int hoeher = Mathf.RoundToInt(ortLebend.Y + uL - (ortTot.Y + uT));
            werte.Add(hoeher);
            if (beispiel.Length == 0 || Mathf.Abs(hoeher) > 8 && !beispiel.Contains("⚠"))
                beispiel = $"  z. B. Satz {e.Infantry} Richtung {e.Facing} bei ({e.Col},{e.Row}): "
                         + $"Fuesse y {ortLebend.Y + uL:0}, Leiche unten y {ortTot.Y + uT:0} "
                         + $"-> {hoeher} px hoeher{(Mathf.Abs(hoeher) > 8 ? " ⚠" : "")}\n";
        }

        if (werte.Count == 0)
            return sb.Append("  kein Fusssoldat mit Bildern auf der Karte — der Lauf sagt NICHTS").ToString();
        werte.Sort();
        int min = werte[0], max = werte[^1], median = werte[werte.Count / 2];
        int ueber = werte.FindAll(v => Mathf.Abs(v) > 8).Count;
        sb.Append($"  {werte.Count} Fusssoldaten ({ohneBild} ohne Bild): die Leiche liegt {min}…{max} px "
                + $"ueber den Fuessen, Median {median}; ueber 8 px: {ueber}\n");
        sb.Append(beispiel);
        sb.Append($"  Gegenschalter --leichenanker-alt: {LeichenankerAlt}\n");
        sb.Append(ueber == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Die unterste Zeile mit sichtbaren Punkten (Alpha &gt; 0,3), oder
    /// −1 — dieselbe Schwelle wie <c>FussVersatz</c>.</summary>
    private static int Unterkante(Texture2D? tex)
    {
        var img = tex?.GetImage();
        if (img == null) return -1;
        for (int y = img.GetHeight() - 1; y >= 0; y--)
            for (int x = 0; x < img.GetWidth(); x++)
                if (img.GetPixel(x, y).A > 0.3f) return y;
        return -1;
    }
}
