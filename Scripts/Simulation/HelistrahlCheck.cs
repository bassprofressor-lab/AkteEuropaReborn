namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>--helistrahl-check</b> (30.10.2026, bug-686, pruefung-rest-b-opus.md §2) — siehe
/// <see cref="BodenstrahlTakt"/>, Hubschrauberzweig.
///
/// <para>Ein Kampfhubschrauber (Art 10…12) wird waagerecht d = 0…4 Zellen neben den
/// Zielpunkt eines feindlichen Ziels gestellt, und es wird EINE Salve ueber den echten
/// Weg (<see cref="LuftSalve(Special, Entity?, Vector2)"/> → <see cref="StrahlAnlegen"/>)
/// abgegeben. Danach laeuft nur der Strahltakt (<see cref="StrahlTakt"/>), bis beide
/// Strahlen angekommen sind. Gemessen je Strahl: Laenge L am Boden, Punkte, Takte bis zum
/// Treffer. Soll (0x454CF0, Hubschrauberzweig 0x454DD1): Punkte = ⌊L⌋ − 6, Takte =
/// ⌈(L − 6)/40⌉; dazu der Strich: der erste Punkt steht auf Bildhoehe des Hubschraubers,
/// der letzte am Boden. Nullmodell <c>--heli-strahl-alt</c>: Punkte = Takte − 1, Takte =
/// ⌈L/40⌉ → DURCHGEFALLEN.</para>
/// <para>⚠ EINGRIFFE: der Hubschrauber wird versetzt (Kartenlage), das Ziel bekommt TP
/// 30000; gibt es keinen fliegenden Kampfhubschrauber, gibt es KEIN URTEIL. Bei d = 0 ist
/// L nur der Seitenversatz ±20 (der echte Schuss faellt bei uns erst unter 1,5 Zellen).</para>
/// </summary>
public partial class MapEntityLayer
{
    public string HelistrahlCheck()
    {
        var sb = new System.Text.StringBuilder("helistrahl-check"
            + (HeliStrahlAlt ? " — NULLMODELL --heli-strahl-alt" : "") + "\n");
        Special? a = null;
        foreach (var s in _special)
            if (s.Kind is >= 10 and <= 12 && !s.Dead && !s.Stored && s.Alt > 0) { a = s; break; }
        string eingriff = "";
        if (a == null)
            foreach (var s in _special)
                if (s.Kind is >= 10 and <= 12 && !s.Dead)
                {
                    // ⚠ EINGRIFF: beim Missionsstart steht jeder im Hangar oder am Boden.
                    a = s; eingriff = $"  ⚠ EINGRIFF: Platz {s.Slot} war {(s.Stored ? "eingelagert" : "am Boden")} (Hoehe {s.Alt}) — "
                                     + "ausgelagert, Hoehe Gelaende·15 + 60\n";
                    s.Stored = false; s.Alt = ElevOf(s.Col, s.Row) * 15 + 60;
                    break;
                }
        if (a == null)
        {
            // ⚠ EINGRIFF wie --absturz-check: ein Probehubschrauber Art 10 des Spielers.
            int slot = 900;
            foreach (var x in _special) slot = Mathf.Max(slot, x.Slot + 1);
            a = new Special
            {
                Slot = slot, Kind = 10, Name = "Probeheli", TypeName = "Probeheli",
                Owner = ViewPlayer, Stored = false, HomeSlot = -1,
                Col = 10, Row = 10, Pos = CellCenter(10, 10),
                Hp = 100, HpMax = 100, Attack = 20, Ammo = 99, AmmoMax = 99,
                Fuel = 999, FuelMax = 999, Speed = 5, Alt = ElevOf(10, 10) * 15 + 60,
            };
            _special.Add(a);
            eingriff = $"  ⚠ EINGRIFF: kein Kampfhubschrauber auf der Karte — Probeheli Platz {slot} (Art 10, Hoehe {a.Alt}) angelegt\n";
        }
        sb.Append(eingriff);
        if (HeliStrahlAus) return sb.Append("  KEIN URTEIL — --heli-strahl-aus legt keinen Strahl an").ToString();
        int zi = -1;
        for (int i = 0; i < _entities.Count && zi < 0; i++)
        {
            var e = _entities[i];
            if (!e.Dead && !e.IsProp && !e.IsBuilding && e.Mobile && e.Owner >= 0 && e.Owner != a.Owner
                && !Untergestellt(e)) zi = i;
        }
        if (zi < 0) return sb.Append("  KEIN URTEIL — kein Ziel").ToString();
        var Z = _entities[zi];
        sb.AppendLine($"  Hubschrauber Platz {a.Slot} ({a.Name}, Art {a.Kind}, Hoehe {a.Alt}), Ziel Platz {Z.Slot} ({LabelOf(Z)}) auf ({Z.Col},{Z.Row})");
        bool ok = true; int gemessen = 0;
        foreach (int d in new[] { 0, 1, 2, 3, 4 })
        {
            Z.HpMax = Z.Hp = 30000;
            var aim = AirAimPoint(Z);
            a.Pos = aim + new Vector2(d * TileW, 0f);
            HsMessung.Clear();
            int vorher = _strahlen.Count;
            LuftSalve(a, Z, aim);
            var meine = _strahlen.GetRange(vorher, _strahlen.Count - vorher);
            if (meine.Count == 0) { sb.AppendLine($"  d {d}: kein Strahl angelegt"); ok = false; continue; }
            // Der Strich: erster und letzter Punkt des ersten Strahls gegen Bild und Boden.
            float h0 = HeliBildHoehe(a);
            for (int t = 0; t < 40 && meine.Exists(s => _strahlen.Contains(s)); t++)
            {
                Z.HpMax = Z.Hp = 30000;
                StrahlTakt();
            }
            if (meine.Exists(s => _strahlen.Contains(s))) { sb.AppendLine($"  d {d}: nicht angekommen (40 Takte)"); ok = false; continue; }
            string strich = "";
            if (!HeliStrahlAlt && meine[0].Punkte > 1)
            {
                var s0 = meine[0];
                float L0 = s0.Laenge;
                // Abstand ueber dem Bodenstrich am ersten (t = 6) und letzten Punkt (t = ⌊L⌋ − 1)
                float erst = (s0.Start + (s0.Ziel - s0.Start) * (HeliZuschlag / L0)).Y - s0.ErsterPunkt.Y;
                float rest = (s0.Start + (s0.Ziel - s0.Start) * (((int)L0 - 1) / L0)).Y - s0.LetzterPunkt.Y;
                float sollErst = h0 * (1f - HeliZuschlag / L0), sollRest = h0 * (1f - ((int)L0 - 1) / L0);
                bool schraeg = Mathf.Abs(erst - sollErst) < 0.5f && Mathf.Abs(rest - sollRest) < 0.5f;
                ok &= schraeg;
                strich = $"; Strich von {erst:0.0} px bis {rest:0.0} px ueber dem Boden (Bildhoehe {h0:0.0}; Soll {sollErst:0.0} / {sollRest:0.0}) -> {(schraeg ? "schraeg" : "NICHT SCHRAEG")}";
            }
            foreach (var (L, p, tk) in HsMessung)
            {
                gemessen++;
                int sollP = Mathf.Max(0, (int)L - HeliZuschlag), sollT = Mathf.Max(1, Mathf.CeilToInt((L - HeliZuschlag) / HeliSchritt));
                int altT = Mathf.Max(1, Mathf.CeilToInt(L / 40f)), altP = altT - 1;
                bool tr = p == sollP && tk == sollT;
                ok &= tr;
                sb.AppendLine($"  d {d}: L {L:0.0} px — Punkte {p}, Takte bis Treffer {tk}; Soll {sollP} / {sollT} (0x454DD1), "
                            + $"Nullmodell {altP} / {altT} -> {(tr ? "ja" : "NEIN")}");
            }
            if (strich.Length > 0) sb.AppendLine("       " + strich.TrimStart(';', ' '));
        }
        sb.Append(gemessen == 0 ? "  KEIN URTEIL" : ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
