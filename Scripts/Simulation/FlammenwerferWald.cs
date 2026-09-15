namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--flammenwerfer-wald-check</c> (15.09.2026, seine Meldung aus K16: »den Jungle setzt
/// der Flammenwerfer nicht in Brand«). Lesung berichte/flammenwerfer-wald-opus.md.
/// <list type="number">
/// <item><b>Fall 1, echter Weg:</b> ein Flammenwerfer (ZBRAN 12; fehlt er auf der Karte,
/// wird ein Fahrzeug dazu gemacht — EINGRIFF) greift per Bodenangriff eine stehende
/// Waldzelle in Reichweite an. Soll: der erste Einschlag zündet (Brandwert 60).
/// Nullmodelle <c>--flamme-ohne-sonderfall</c> und <c>--wald-waffenschaden</c>: 0 Feuer.</item>
/// <item><b>Fall 2, die Bänder:</b> ein S-Raketenwerfer (ZBRAN 7, Angriff 28, Rang 0) trifft
/// 400 wechselnde stehende Waldzellen über <see cref="ZellWirkung"/>. Soll: Feuerquote
/// 0,25 ± 0,05, »zrus« (Wald weg ohne Feuer) 0. Nullmodell <c>--wald-waffenschaden</c>
/// (Tafelschaden 120): »zrus« 1,00.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer
{
    public string FlammenwerferWaldCheck()
    {
        var sb = new System.Text.StringBuilder("flammenwerfer-wald-check\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (_nav == null) return sb.Append("  keine Karte\n  DURCHGEFALLEN").ToString();
        if (FlammeOhneSonderfall) sb.AppendLine("  ⚠ NULLMODELL --flamme-ohne-sonderfall: Fall 1 MUSS scheitern");
        if (WaldWaffenschaden) sb.AppendLine("  ⚠ NULLMODELL --wald-waffenschaden: beide Faelle MUESSEN scheitern");
        if (BodenangriffTafelreichweite) sb.AppendLine("  (--bodenangriff-tafelreichweite an)");

        // alle Flammenwerfer der Karte mit ihren Schusswerten (seine Meldung: der der KI
        // schiesst viel schneller)
        foreach (var x in _entities)
            if (!x.Dead && !x.IsProp && !x.IsBuilding && (x.Comp0D == FlammenwerferZbran || (x.Name ?? "").Contains("Flamm")))
                sb.AppendLine($"    Flammenwerfer Platz {x.Slot} Sp{x.Owner} »{x.Name}« Fuss {x.Infantry >= 0} ZBRAN {x.Comp0D} Waffe {x.Weapon} "
                            + $"Reload {x.Reload} -> {ReloadOf(x):0.00}s, Angriff {x.Attack}, Rang {x.Rating28}, Reichweite {x.Range}, Munition {x.Ammo}/{x.AmmoMax}");

        // stehende Waldzellen
        var wald = new System.Collections.Generic.List<Vector2I>();
        foreach (var o in _objDraw)
            if (o.IstWald && !o.Abgebrannt && o.BrandVon < 0f) wald.Add(new Vector2I(o.Col, o.Row));
        sb.AppendLine($"  stehende Waldzellen: {wald.Count}");
        if (wald.Count < 20) return sb.Append("  zu wenig Wald — ungeprueft\n  DURCHGEFALLEN").ToString();

        // ---- Fall 1 -------------------------------------------------------------
        int fi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Mobile && e.Owner == ViewPlayer && e.Comp0D == FlammenwerferZbran
                && !Untergestellt(e)) { fi = i; break; }
        }
        bool eingriff = false;
        if (fi < 0)
            for (int i = 0; i < _entities.Count; i++)
            {
                var e = _entities[i];
                if (e.Dead || e.IsProp || e.IsBuilding || !e.Mobile || e.Owner != ViewPlayer || Untergestellt(e)) continue;
                if (e.Infantry >= 0 || e.Move == Simulation.NavGrid.MoveClass.Ship || !CanFight(e)) continue;
                fi = i; eingriff = true; break;
            }
        if (fi < 0) Soll(false, "Fall 1: kein Fahrzeug fuer den Flammenwerfer");
        else
        {
            var f = _entities[fi];
            if (eingriff)
            {
                f.Comp0D = FlammenwerferZbran; f.Attack = 4; f.Rating28 = 0;
                sb.AppendLine($"  ⚠ EINGRIFF: Platz {f.Slot} wird Flammenwerfer (ZBRAN 12, Angriff 4, Rang 0)");
            }
            // eine Waldzelle mit freiem Platz 2 Zellen daneben
            Vector2I? ziel = null, platz = null;
            foreach (var w in wald)
            {
                foreach (var (dx, dy) in new[] { (-2, 0), (2, 0), (0, -2), (0, 2), (-3, 0), (3, 0) })
                {
                    int c = w.X + dx, r = w.Y + dy;
                    if (!_nav.InBounds(c, r) || !_nav.IsFree(c, r, f.Move, fi)) continue;
                    ziel = w; platz = new Vector2I(c, r); break;
                }
                if (ziel != null) break;
            }
            if (ziel == null) Soll(false, "Fall 1: keine Waldzelle mit freiem Platz daneben");
            else
            {
                ProbeVersetzen(fi, platz!.Value.X, platz.Value.Y);
                f.Path = null; f.Orders.Clear(); f.Target = -1; f.Cooldown = 0;
                if (f.AmmoMax > 0) f.Ammo = f.AmmoMax;
                int feuer0 = BodenWaldFeuer, weg0 = BodenWaldWeg, sonder0 = FlammenBrandwert;
                _sel.Clear(); _sel.Add(fi); _selected = fi;
                bool posted = PostAttackGround(ZellMitte(ziel.Value.X, ziel.Value.Y));
                int t = 0;
                while (t < 1500 && BodenWaldFeuer == feuer0 && BodenWaldWeg == weg0) { SimTickFuerProbe(); t++; }
                _sel.Clear(); _selected = -1;
                bool feuer = BodenWaldFeuer > feuer0;
                Soll(posted && feuer && BrandwertLetzter == 60,
                     $"Fall 1: Flammenwerfer Platz {f.Slot} auf ({f.Col},{f.Row}) -> Wald ({ziel.Value.X},{ziel.Value.Y}): "
                     + $"nach {t} Takten Feuer {BodenWaldFeuer - feuer0}, weg {BodenWaldWeg - weg0}, Brandwert {BrandwertLetzter}, "
                     + $"Weiche {FlammenBrandwert - sonder0}x, Schuesse {BodenSchuesse}");
            }
        }

        // ---- Fall 2 -------------------------------------------------------------
        int ri = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Owner == ViewPlayer && i != fi) { ri = i; break; }
        }
        if (ri < 0) Soll(false, "Fall 2: kein Schuetze");
        else
        {
            var s = _entities[ri];
            int z0 = s.Comp0D, a0 = s.Attack, r0 = s.Rating28;
            s.Comp0D = 7; s.Attack = 28; s.Rating28 = 0;
            int elev = ElevOf(s.Col, s.Row);
            int feuer0 = BodenWaldFeuer, weg0 = BodenWaldWeg, n = 0, wMin = int.MaxValue, wMax = int.MinValue;
            foreach (var w in wald)
            {
                if (n >= 400) break;
                // nur stehende Zellen (Fall 1 kann eine angezuendet haben)
                bool steht = false;
                foreach (var o in _objDraw)
                    if (o.Col == w.X && o.Row == w.Y && o.IstWald && !o.Abgebrannt && o.BrandVon < 0f) { steht = true; break; }
                if (!steht) continue;
                ZellWirkung(w.X, w.Y, 120, ri);
                n++;
                wMin = System.Math.Min(wMin, BrandwertLetzter); wMax = System.Math.Max(wMax, BrandwertLetzter);
            }
            s.Comp0D = z0; s.Attack = a0; s.Rating28 = r0;
            double quote = n > 0 ? (double)(BodenWaldFeuer - feuer0) / n : 0;
            double zrus = n > 0 ? (double)(BodenWaldWeg - weg0) / n : 0;
            Soll(n >= 100 && quote is >= 0.20 and <= 0.30 && BodenWaldWeg == weg0,
                 $"Fall 2: S-Raketenwerfer (Angriff 28, Rang 0, Hoehe {elev}) auf {n} Waldzellen: Feuerquote {quote:0.000} (Soll 0,25 ± 0,05), "
                 + $"zrus {zrus:0.000} (Soll 0), Brandwerte {wMin}..{wMax} (Soll um {(128 * (28 + 2 * elev)) >> 7} ± 4)");
        }

        sb.AppendLine($"  --flamme-ohne-sonderfall {FlammeOhneSonderfall}, --wald-waffenschaden {WaldWaffenschaden}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
