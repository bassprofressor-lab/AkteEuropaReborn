namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// ⭐⭐ <b>DER X-ANKER DER EINHEITENBILDER</b> (15.09.2026, seine Meldung aus K15/K16:
/// »egal welche Grafik von den Gebäuden, die Einheiten fahren nicht ganz mittig in die
/// Tür, sondern immer mit leichtem Linksversatz«). Lesung
/// berichte/tueren-zeichnen-einfahrt-opus.md §B.
///
/// <para><b>Original:</b> ein stehendes Fahrzeug wird auf die LINKE ZELLKANTE geblittet
/// (<c>x = Spalte·40 + fx − 20</c>, stehend fx = 20; C 0x430207 / F 0x42F36F) — dieselbe
/// Spalte wie Kachel und Tor. Fußvolk 5 Punkte weiter rechts (C 0x43032D / F 0x42F48B).</para>
///
/// <para><b>Bei uns</b> hing das Bild an <c>Pos.x − 30 + CanvasXPad 4</c> = Zellkante − 6:
/// die (30,55) aus <see cref="ComposedAnchor"/> sind für das GLEIS richtig (<c>sub ax,6</c>
/// @0x42DFEC) und waren unbesehen auf die Einheiten übergegangen. Gemessen über alle
/// Bilder: Fahrzeug −6,0, Fußvolk −11,0 Punkte. Mit dem Original: 0,0 / 0,0.</para>
///
/// <para>⚠ UNSERE Setzung bzw. ungeprüft: Schiffe (Original +4, also Anker 20) bleiben
/// beim alten Anker, bis sie nachgemessen sind; Flugzeuge haben einen eigenen Einreiher
/// (nicht gelesen). Gegenschalter <c>--einheitenanker-alt</c>.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--einheitenanker-alt</c> — der Stand bis 15.09.2026: Einheitenbilder am
    /// Gleisanker (30, 55).</summary>
    public static bool EinheitenankerAlt;

    /// <summary>Der Zeichenanker eines Einheitenbildes: y wie bisher, x je Klasse —
    /// Fahrzeug 24 (C 0x430207), Fußvolk 19 (C 0x43032D).</summary>
    private Vector2 EinheitenAnker(Entity e)
    {
        var a = ComposedAnchor;
        if (EinheitenankerAlt) return a;
        if (e.Infantry >= 0) return new Vector2(19, a.Y);
        if (e.Move == Simulation.NavGrid.MoveClass.Ship) return a;       // (V) nicht nachgemessen
        return new Vector2(24, a.Y);
    }

    /// <summary>
    /// <c>--einheitenanker-check</c>: für jede stehende Einheit der Karte die sichtbare
    /// Körpermitte (ohne Schatten) minus ihren Standpunkt in x, getrennt nach Fahrzeug
    /// und Fußvolk, als Median. Soll |Median| ≤ 1. Nullmodell <c>--einheitenanker-alt</c>:
    /// −6 / −11.
    /// </summary>
    public string EinheitenankerCheck()
    {
        var sb = new System.Text.StringBuilder("einheitenanker-check\n");
        if (EinheitenankerAlt) sb.AppendLine("  ⚠ NULLMODELL --einheitenanker-alt: hier MUSS es durchfallen");
        var fahr = new List<float>();
        var fuss = new List<float>();
        foreach (var e in _entities)
        {
            if (e.Dead || e.IsProp || e.IsBuilding || Untergestellt(e) || e.Path != null) continue;
            if (e.Move == Simulation.NavGrid.MoveClass.Ship) continue;
            var picC = PictureAnchor(e);
            if (e.Infantry >= 0)
            {
                var tex = GetInfantryTexture(e.Infantry, e.Facing, InfBlock(e));
                if (tex == null) continue;
                fuss.Add(FussvolkOrt(e).X + KoerperMitte(tex).X - picC.X);
            }
            else
            {
                var tex = GetHullTexture(e.UnitType, e.Facing, PoseOf(e), SlopeClassOf(e.Col, e.Row))
                          ?? GetComposedTexture(e.Combo, e.Facing);
                if (tex == null) continue;
                fahr.Add(picC.X - EinheitenAnker(e).X + KoerperMitte(tex).X - picC.X);
            }
        }
        float Median(List<float> l) { if (l.Count == 0) return float.NaN; l.Sort(); return l[l.Count / 2]; }
        float mf = Median(fahr), mi = Median(fuss);
        bool okF = fahr.Count > 0 && Mathf.Abs(mf) <= 1f;
        bool okI = fuss.Count == 0 || Mathf.Abs(mi) <= 1f;
        sb.AppendLine($"  {(okF ? "ok  " : "⚠ FALSCH")} Fahrzeuge: {fahr.Count} Bilder, Median Koerpermitte − Standpunkt x = {mf:+0.0;-0.0;0.0} px (Soll ±1)");
        sb.AppendLine($"  {(okI ? "ok  " : "⚠ FALSCH")} Fussvolk: {fuss.Count} Bilder, Median = {mi:+0.0;-0.0;0.0} px (Soll ±1)"
                    + (fuss.Count == 0 ? " — keine Infanterie, sagt nichts" : ""));
        sb.AppendLine($"  --einheitenanker-alt {EinheitenankerAlt}");
        return sb.Append(okF && okI ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
