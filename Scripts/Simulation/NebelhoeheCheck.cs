namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--nebelhoehe-check</c> — <b>liegt der Nebel auf dem Gelaende, und gibt es
/// den Saum?</b> (11.09.2026)
///
/// <para>⚠⚠ Seine Meldung: »es fuehlt sich an als wuerden wir kaestchenweise
/// aufdecken, manchmal ein Rechteck Baeume«. Zwei Ursachen, beide gelesen
/// (Kopf von <c>BuildFogTexture</c> und von <c>FogGrid.Saum</c>):</para>
/// <list type="number">
/// <item>die Nebeltextur lag FLACH, der Boden ist um Hoehe·15 px angehoben
///   (@0x4B4785…0x4B479F);</item>
/// <item>es gab keinen Saum-Zustand (@0x420C5C → 0x41FF50): Einheiten im Ring
///   um den Stempel waren unsichtbar, und der Wald dort blieb synthetischer
///   Boden, bis die Zelle klar wurde.</item>
/// </list>
///
/// <para><b>Teil 1</b> misst den Saum an einem freistehenden Stempel gegen die
/// Zahlen, die der Bericht vom 10.09. aus der Sehnentafel der EXE gezaehlt hat
/// (r 3/5/10: Stempel 41/109/373, Ring 32/54/94) — unabhaengig von jeder
/// Karte.</para>
///
/// <para><b>Teil 2</b> misst die WIRKUNG am Bild, nicht die Formel: fuer jede
/// Zelle wird die Nebeltextur an der Stelle gelesen, an der ihr BODEN
/// gezeichnet ist (<c>MapBaker</c>: <c>zeile·20 − hoehe·15</c>), und mit der
/// Deckung verglichen, die die Zelle selbst haben soll. Zellen, deren Boden im
/// Bild von einer vorderen Hochflaeche verdeckt ist, zaehlen nicht — dort zeigt
/// auch der Boden etwas anderes. Eine Abweichung kann nur an einer Nebelkante
/// entstehen; ohne Kanten sagt der Lauf nichts, und das steht dann da.</para>
///
/// <para>Nullmodelle: <c>--nebel-flach</c> (Teil 2 muss Abweichungen auf Hoehe
/// ≥ 1 zeigen, auf Hoehe 0 keine) und <c>--kein-saum</c> (Teil 1 muss den Ring
/// verlieren).</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string NebelhoeheCheck()
    {
        var sb = new System.Text.StringBuilder("nebelhoehe-check\n");
        if (_fog == null) return sb.Append("  kein Nebel geladen — der Lauf sagt NICHTS").ToString();
        bool ok = true;

        // ---- Teil 1: der Saum an einem freistehenden Stempel ----
        sb.Append("  Teil 1 — Saum um einen freistehenden Stempel (Sehnentafel der EXE)\n");
        Simulation.FogGrid.Load();
        foreach (var (radius, sollStempel, sollSaum) in new[] { (3, 41, 32), (5, 109, 54), (10, 373, 94) })
        {
            var g = new Simulation.FogGrid(60, 60);
            // radius = sicht + hoehe − 1 (@0x4207CF), also sicht = radius + 1 bei hoehe 0
            g.Update(new (int, int, int, int)[] { (30, 30, radius + 1, 0) });
            var (_, _, stempel, saum) = g.Counts();
            bool treffer = stempel == sollStempel && saum == sollSaum;
            ok &= treffer;
            sb.Append($"    r {radius,2}: Stempel {stempel,3} (soll {sollStempel,3}), "
                    + $"Saum {saum,3} (soll {sollSaum,3})  {(treffer ? "ja" : "NEIN")}\n");
        }

        // ---- Teil 2: liegt der Nebel auf dem Gelaende? ----
        bool warErzwungen = ForceFog;
        ForceFog = true;                     // sonst deckt UpdateFog alles auf und es gibt keine Kante
        sb.Append("  Teil 2 — liegt der Nebel auf dem Gelaende?\n");
        sb.Append($"    Karte {_fog.Width}x{_fog.Height}, Hoehe bis {_fogElevMax}, {NebelHub} Texel je Stufe\n");

        // (a) der Stand beim Missionsstart
        UpdateFog();
        ok &= GelaendeMessen(sb, "Missionsstart");
        var (_, _, st, sm) = _fog.Counts();

        // (b) ⚠ ein RASTER aus Stempeln ueber die ganze Karte. Beim Start gibt
        // es nur die Kanten um die eigenen Einheiten (auf K7 rund 70) — zu
        // wenige und zu einseitig verteilt, um die Hoehen zu treffen. Das Raster
        // legt Kanten auf jede Stufe. Eingriff nur ins Gitter, danach wieder
        // der echte Stand.
        var raster = new System.Collections.Generic.List<(int, int, int, int)>();
        for (int r = 4; r < _fog.Height; r += 9)
            for (int c = 4 + (r / 9) % 2 * 5; c < _fog.Width; c += 11)
                raster.Add((c, r, 4, 0));
        _fog.Update(raster);
        ok &= GelaendeMessen(sb, $"Raster ({raster.Count} Stempel r 3)");
        UpdateFog();
        ForceFog = warErzwungen;

        // ---- Teil 3: der Saum auf dieser Karte ----
        int nurSaum = 0;
        foreach (var e in _entities)
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Owner != ViewPlayer
                && _fog.IsWatched(e.Col, e.Row) && !_fog.IsStamped(e.Col, e.Row))
                nurSaum++;
        sb.Append($"  Teil 3 — auf dieser Karte: Stempel {st}, Saum {sm} Zellen, "
                + $"{nurSaum} fremde Einheiten nur durch den Saum sichtbar\n");

        sb.Append($"  Gegenschalter --nebel-flach: {NebelFlach}, --kein-saum: {Simulation.FogGrid.KeinSaum}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    /// <summary>Baut die Nebeltextur aus dem Gitter, wie es gerade steht, und
    /// liest sie an jedem sichtbaren Bodenort. Siehe den Kopf der Datei.</summary>
    private bool GelaendeMessen(System.Text.StringBuilder sb, string was)
    {
        BuildFogTexture();
        int w = _fog!.Width, h = _fog.Height, n = FogSub;
        const int sx = 2, sy = 2;            // die Zellmitte in Texeln
        int geprueft = 0, verdeckt = 0, kanten = 0, falsch0 = 0, falschHoch = 0, ausserhalb = 0;
        int kVoraus = _fogElevMax * Import.MapBaker.ElevStep / TileH + 1;
        string beispiel = "";
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int el = _fogElev[r * w + c];
                // wo der BODEN der Zelle im Bild liegt — die Formel des Backofens
                float bodenY = r * TileH - el * Import.MapBaker.ElevStep + (sy + 0.5f) * TileH / n;

                // von einer vorderen Zeile verdeckt? (wahre Geometrie, auch bei --nebel-flach)
                bool zu = false;
                for (int k = 1; k <= kVoraus && r + k < h && !zu; k++)
                    if ((r + k) * TileH - _fogElev[(r + k) * w + c] * Import.MapBaker.ElevStep <= bodenY)
                        zu = true;
                if (zu) { verdeckt++; continue; }

                float soll = Deckung(_fog.At(c, r), _fog.CornerAt(c, r), sx, sy);
                if (r + 1 < h && Mathf.Abs(soll - Deckung(_fog.At(c, r + 1), _fog.CornerAt(c, r + 1), sx, sy)) > 0.01f)
                    kanten++;
                geprueft++;

                float gx = _ox + c * TileW + (sx + 0.5f) * TileW / n;
                float gy = _oy + bodenY;
                int tx = Mathf.FloorToInt((gx - _fogRect.Position.X) * _fogTexW / _fogRect.Size.X);
                int ty = Mathf.FloorToInt((gy - _fogRect.Position.Y) * _fogTexH / _fogRect.Size.Y);
                // ⚠ ausserhalb der Textur getrennt zaehlen: das ist der Kartenrand
                // oben, nicht eine Nebelkante — sonst traegt er das Nullmodell
                if (tx < 0 || ty < 0 || tx >= _fogTexW || ty >= _fogTexH || _fogPixels == null)
                { ausserhalb++; continue; }
                int ist = _fogPixels[(ty * _fogTexW + tx) * 4 + 3];
                if (Mathf.Abs(ist - (int)(soll * 255f)) <= 1) continue;
                if (el == 0) falsch0++; else falschHoch++;
                if (beispiel.Length == 0)
                    beispiel = $"      z. B. Zelle ({c},{r}) Hoehe {el}: soll {(int)(soll * 255f)}, am Bodenort {ist}\n";
            }

        sb.Append($"    {was}: {geprueft} Zellen ({verdeckt} im Bild verdeckt), {kanten} Nebelkanten\n");
        sb.Append($"      falscher Nebelwert am Bodenort: {falsch0 + falschHoch} "
                + $"(Hoehe 0: {falsch0}, Hoehe >= 1: {falschHoch}), ausserhalb der Textur: {ausserhalb}\n");
        sb.Append(beispiel);
        if (kanten == 0)
        {
            sb.Append("      ⚠ keine Nebelkante — dieser Teil sagt NICHTS\n");
            return false;
        }
        return falsch0 + falschHoch + ausserhalb == 0;
    }
}
