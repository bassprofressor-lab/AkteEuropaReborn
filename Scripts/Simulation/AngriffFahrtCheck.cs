namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>--angriff-fahrt-check</b> (29.10.2026, P4-Zusatz, Spielerentscheidung »wie im Original«) —
/// siehe <see cref="AngriffFaehrtWeiter"/>. Ein eigenes Fahrzeug A mit Turm (Reichweite 3..10)
/// und ein feindliches Fahrzeug X (festgeheftet, Nachladen gesperrt, 30000 TP) auf einem freien
/// Streifen, X liegt Reichweite + 4 Zellen entfernt. A bekommt den Angriffsbefehl wie ein Klick
/// (Target, Ordered, Weg leer), 400 Takte. Soll: A steht am Ende hoechstens 1,5 Zellen von der
/// Zielzelle (die Zielzelle selbst ist belegt, also die naechste freie daneben) und hat
/// IM FAHREN auf X geschossen (Schuesse mit Weg &gt; 0). Nullmodell <c>--angriff-halt-alt</c>:
/// A haelt bei Reichweite (Abstand ≈ Reichweite, 0 Schuesse im Fahren) → DURCHGEFALLEN.
/// </summary>
public partial class MapEntityLayer
{
    private int _afA = -1, _afX = -1, _afSchuesse, _afImFahren;

    private void AfProbeZaehlen(int si, int vi)
    {
        if (si != _afA || vi != _afX) return;
        _afSchuesse++;
        if (_entities[si].Path != null) _afImFahren++;
    }

    public string AngriffFahrtCheck()
    {
        var sb = new System.Text.StringBuilder("angriff-fahrt-check"
            + (AngriffHaltAlt ? " — NULLMODELL --angriff-halt-alt" : "") + "\n");
        if (_nav == null) return sb.Append("  KEIN URTEIL: keine Karte").ToString();
        int ai = -1;
        for (int i = 0; i < _entities.Count && ai < 0; i++)
        {
            var e = _entities[i];
            if (e.IsBuilding || e.IsProp || e.Dead || !e.Mobile || Untergestellt(e) || !CanFight(e) || e.Owner != ViewPlayer) continue;
            float r = RangeOf(e);
            if (e.Infantry < 0 && e.Weapon != 0 && e.GameUnitType == 0 && r >= 3 && r <= 10 && RangeMinOf(e) <= 1
                && WeaponRowOf(e.Weapon) != 8 && !GaswerferSchweigt(e)) ai = i;
        }
        if (ai < 0) return sb.Append("  KEIN URTEIL: kein eigenes Fahrzeug mit Turm, Reichweite 3..10").ToString();
        var A = _entities[ai];
        int xi = -1;
        for (int i = 0; i < _entities.Count && xi < 0; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding && !e.IsProp && !e.Dead && e.Mobile && !Untergestellt(e) && e.Infantry < 0 && IsHostile(A, e)) xi = i;
        }
        if (xi < 0) return sb.Append("  KEIN URTEIL: kein feindliches Fahrzeug").ToString();
        var X = _entities[xi];
        int rng = Mathf.FloorToInt(RangeOf(A)), weit = rng + 4;
        int sc = -1, sr = -1;
        for (int r = 4; r < _nav.Height - 4 && sc < 0; r++)
            for (int c = 4; c < _nav.Width - weit - 4 && sc < 0; c++)
            {
                bool frei = true;
                for (int dy = -2; dy <= 2 && frei; dy++)
                    for (int dx = -2; dx <= weit + 2 && frei; dx++)
                        frei = _nav.CanEnter(c + dx, r + dy, A.Move) && _nav.OccupantAt(c + dx, r + dy) < 0;
                if (frei) { sc = c; sr = r; }
            }
        if (sc < 0) return sb.Append($"  KEIN URTEIL: kein freier Streifen {weit + 5}x5").ToString();
        var xz = new Vector2I(sc + weit, sr);
        foreach (int k in new[] { ai, xi })
        {
            var e = _entities[k];
            e.HpMax = e.Hp = 30000; e.Path = null; e.Orders.Clear(); e.Target = -1; e.Ordered = false;
            e.RestZiel = null; e.Schussziel = -1;
        }
        ProbeVersetzen(ai, sc, sr);
        ProbeVersetzen(xi, xz.X, xz.Y);
        A.Target = xi; A.Ordered = true; A.Cooldown = 0; AuftragLoeschen(A);
        _afA = ai; _afX = xi; _afSchuesse = _afImFahren = 0;
        int neu0 = AngriffNeuGerufen;
        float naechst = float.MaxValue;
        int erstSchussTakt = -1;
        for (int t = 0; t < 400; t++)
        {
            if (X.Col != xz.X || X.Row != xz.Y) ProbeVersetzen(xi, xz.X, xz.Y);
            X.Path = null; X.Cooldown = 99f;
            SimTickFuerProbe();
            if (erstSchussTakt < 0 && _afSchuesse > 0) erstSchussTakt = t;
            naechst = Mathf.Min(naechst, new Vector2(A.Col - xz.X, A.Row - xz.Y).Length());
        }
        _afA = _afX = -1;
        float ende = new Vector2(A.Col - xz.X, A.Row - xz.Y).Length();
        sb.AppendLine($"  A Platz {A.Slot} ({LabelOf(A)}, Reichweite {RangeOf(A):0.0}) von ({sc},{sr}) auf X Platz {X.Slot} ({LabelOf(X)}) "
                    + $"an ({xz.X},{xz.Y}), Abstand {weit}");
        sb.AppendLine($"  nach 400 Takten: A auf ({A.Col},{A.Row}), Abstand zur Zielzelle {ende:0.0} (naechster {naechst:0.0}), "
                    + $"Schuesse auf X {_afSchuesse}, davon im Fahren {_afImFahren}, erster Schuss Takt {erstSchussTakt}, "
                    + $"Neurufe 0x40FC90 {AngriffNeuGerufen - neu0}, Auftrag noch X {A.Target == xi}");
        bool ok = ende <= 1.5f && _afImFahren > 0 && A.Target == xi;
        sb.AppendLine($"  Soll: Abstand <= 1,5 und Schuesse im Fahren > 0 (Nullmodell: Abstand ~ Reichweite {rng}, 0 im Fahren)");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
