namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <c>--zasah-check</c> (11.09.2026) — <b>greifen die drei Sonderfaelle im
/// Vorspann von Zasah?</b> Siehe Simulation/ZasahSonderfaelle.cs.
///
/// <para>Gemessen wird ueber <c>ApplyHit</c>, den Weg, den jeder Treffer im
/// Spiel nimmt — nicht ueber den Vorspann allein.</para>
/// <list type="number">
/// <item>Plasma (Waffe 34 = ZBRAN 14) fuenfmal auf ein Fahrzeug: das Tempo
///   halbiert sich bis zur Untergrenze 2, die Trefferpunkte bleiben.</item>
/// <item>Schallkanone (Waffe 23 = ZBRAN 3): das Fahrzeug ist tot, seine Zelle
///   frei, ein Effekt 82 mehr, kein Wrack und keine Sprengung.</item>
/// <item>Mission 17: ein Treffer von Spieler 0 auf Spieler 3 setzt v[0] von 0
///   auf 1; die Gegenprobe auf Spieler 2 laesst v[0] stehen.</item>
/// </list>
/// <para>⚠ EINGRIFFE, genannt: Waffe, Besitzer und Trefferpunkte werden fuer
/// die Messung gesetzt und danach zurueckgestellt (das entfernte Fahrzeug
/// bleibt weg); fuer Teil 3 wird die Missionsnummer voruebergehend auf 17
/// gestellt — es gibt keine Leveldatei 17. Nullmodell
/// <c>--zasah-sonderfaelle-aus</c>.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    public string ZasahCheck()
    {
        var sb = new System.Text.StringBuilder("zasah-check\n");
        int si = -1, v1 = -1, v2 = -1, v3 = -1;
        for (int k = 0; k < _entities.Count; k++)
        {
            var e = _entities[k];
            if (e.Dead || e.IsProp || e.IsBuilding || e.Infantry >= 0 || e.GameUnitType != 0) continue;
            if (_nav?.OccupantAt(e.Col, e.Row) != k) continue;
            if (si < 0) si = k; else if (v1 < 0) v1 = k; else if (v2 < 0) v2 = k; else if (v3 < 0) v3 = k;
        }
        if (v3 < 0) return sb.Append("  weniger als vier Fahrzeuge auf der Karte — sagt NICHTS").ToString();
        var s = _entities[si];
        int waffe0 = s.Weapon, besitzer0 = s.Owner;
        bool ok = true;

        // ---- 1 Plasma ----
        {
            var v = _entities[v1];
            int tempo0 = v.Speed, hp0 = v.Hp;
            v.Hp = 10000;
            s.Weapon = 34;
            var folge = new System.Collections.Generic.List<int> { v.Speed };
            for (int k = 0; k < 5; k++) { ApplyHit(si, v1, v, 0); folge.Add(v.Speed); }
            bool halbiert = true;
            for (int k = 1; k < folge.Count; k++)
                halbiert &= folge[k] == Mathf.Max(2, folge[k - 1] / 2);
            bool unbeschaedigt = v.Hp == 10000;
            bool t1 = halbiert && unbeschaedigt && folge[1] != folge[0];
            ok &= t1;
            sb.Append($"  1 Plasma (Waffe 34) 5x auf {LabelOf(v)}: Tempo {string.Join(" -> ", folge)}, "
                    + $"TP {(unbeschaedigt ? "unveraendert" : $"10000 -> {v.Hp}")}  {(t1 ? "ja" : "NEIN")}\n");
            v.Speed = tempo0; v.Hp = hp0;
        }

        // ---- 2 Schallkanone ----
        {
            var v = _entities[v2];
            int feuer0 = _effects.FindAll(x => x.Kind == "fire").Count;
            int wrack0 = _effects.FindAll(x => x.Kind == "wreck" || x.Kind.StartsWith("sprengung")).Count;
            int hp0 = v.Hp;
            v.Hp = 10000;
            s.Weapon = 23;
            int zc = v.Col, zr = v.Row;
            ApplyHit(si, v2, v, 0);
            int feuer = _effects.FindAll(x => x.Kind == "fire").Count - feuer0;
            int wrack = _effects.FindAll(x => x.Kind == "wreck" || x.Kind.StartsWith("sprengung")).Count - wrack0;
            bool frei = _nav!.OccupantAt(zc, zr) != v2;
            bool t2 = v.Dead && frei && feuer == 1 && wrack == 0;
            ok &= t2;
            sb.Append($"  2 Schallkanone (Waffe 23) auf {LabelOf(v)}: {(v.Dead ? "entfernt" : $"lebt (TP {v.Hp})")}, "
                    + $"Zelle ({zc},{zr}) {(frei ? "frei" : "BELEGT")}, Effekt 82 +{feuer}, Wrack/Sprengung +{wrack}  "
                    + $"{(t2 ? "ja" : "NEIN")}\n");
            if (!v.Dead) v.Hp = hp0;
        }
        s.Weapon = waffe0;

        // ---- 3 Mission 17 ----
        if (_mscript == null)
        {
            ok = false;
            sb.Append("  3 Mission 17: ⚠ kein Missionsskript — sagt NICHTS\n");
        }
        else
        {
            var v = _entities[v3];
            int mission0 = UI.SkirmishSetup.CampaignMission, var0 = _mscript.Var(0), besitzerV = v.Owner, hp0 = v.Hp;
            UI.SkirmishSetup.CampaignMission = 17;
            s.Owner = 0;
            v.Hp = 10000;

            _mscript.SetVarFuerProbe(0, 0);
            v.Owner = 3;
            ApplyHit(si, v3, v, 0);
            int nachSpieler3 = _mscript.Var(0);

            _mscript.SetVarFuerProbe(0, 0);
            v.Owner = 2;
            ApplyHit(si, v3, v, 0);
            int nachSpieler2 = _mscript.Var(0);

            UI.SkirmishSetup.CampaignMission = mission0;
            _mscript.SetVarFuerProbe(0, var0);
            v.Owner = besitzerV; v.Hp = hp0; s.Owner = besitzer0;
            bool t3 = nachSpieler3 == 1 && nachSpieler2 == 0;
            ok &= t3;
            sb.Append($"  3 Mission 17 (⚠ EINGRIFF Missionsnummer 17): Treffer auf Spieler 3 -> v[0] = {nachSpieler3}, "
                    + $"auf Spieler 2 -> v[0] = {nachSpieler2}  {(t3 ? "ja" : "NEIN")}\n");
        }

        sb.Append($"  Gegenschalter --zasah-sonderfaelle-aus: {ZasahSonderfaelleAus}\n");
        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
