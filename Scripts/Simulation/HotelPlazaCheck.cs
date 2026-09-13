using System.Text;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>--hotelplaza-check</b> (13.09.2026) — die beiden Untermissionen der
/// Mission 11 »Trading Center« von vorn bis hinten, synchron im Skripttakt.
/// Lesung: <c>berichte/m11-hotelplaza-fable.md</c>.
///
/// <para>Gemessen wird die KETTE, nicht jede Regel für sich: Transporter tot →
/// 430 $ → Agent gesetzt → Laufregel auf der Uhr → Übernahme (echter
/// <see cref="TakeoverTick"/>) → Text 213 mit dem NEUEN Platz → untergestellt →
/// Text 214 mit v[9] = Energie des Hotels → Hotel unter die Hälfte → zwei VIPs
/// von Spieler 4 → beide tot → v[102] = 10 und 800 $.</para>
///
/// <para>⚠ EINGRIFFE des Prüfstands, jeweils benannt: der Transporter wird
/// nicht bekämpft, sondern v[2] direkt auf 3 gesetzt (er ist dann nicht da);
/// die Uhrvariablen v[3]/v[6] werden zurückgestellt statt zehn Spielminuten
/// zu warten; eine eigene Einheit wird neben den Agenten VERSETZT; das
/// Unterstellen ist <c>Ukol = 50</c> von Hand; der Beschuss des Hotels ist
/// <c>Hp</c> von Hand; die VIPs sterben über <see cref="Kill"/>.</para>
///
/// <para>Nullmodell <c>--uebernahme-alt</c>: der Satz behält Platz 7000, Regel
/// 13 feuert nie, v[7] bleibt 0 und v[102] bleibt 1.</para>
/// </summary>
public partial class MapEntityLayer
{
    public string HotelPlazaCheck()
    {
        var sb = new StringBuilder();
        if (_mscript == null) MissionScriptTick(0.001f);
        var s = _mscript;
        if (s == null) return "hotelplaza-check: kein Missionsskript";
        int fehler = 0;
        void Pruefe(bool ok, string was)
        {
            sb.Append(ok ? "  ok     " : "  FEHLER ").Append(was).Append('\n');
            if (!ok) fehler++;
        }
        void Block() => s.Advance(Campaign.MissionScript.BlockPeriod);
        Entity? SatzAuf(int platz)
        {
            foreach (var q in _entities)
                if (!q.IsBuilding && !q.Dead && q.Slot == platz) return q;
            return null;
        }
        int texte213 = 0, texte214 = 0, texte100 = 0, texte450 = 0;
        var vorherText = s.ShowText;
        s.ShowText = (id, art, x, y) =>
        {
            if (id == 213) texte213++;
            if (id == 214) texte214++;
            if (id == 100) texte100++;
            if (id == 450) texte450++;
            vorherText?.Invoke(id, art, x, y);
        };
        int spieler = ViewPlayer >= 0 ? ViewPlayer : 0;
        sb.Append($"hotelplaza-check: Mission {s.Mission}, Spieler {spieler}" +
                  (UebernahmeAltText()) + "\n");

        // ---- 1. Transporter (0x49BEA9) ------------------------------------
        int geld0 = Money(spieler);
        s.SetVarFuerProbe(2, 3);                         // EINGRIFF: Satz 5000 fehlt
        Block();
        Pruefe(s.VarAt(2) == 10 && s.VarAt(101) == 10,
               $"Transporter nicht da -> v[2]={s.VarAt(2)} (10), v[101]={s.VarAt(101)} (10)");
        Pruefe(Money(spieler) - geld0 == 430, $"Zahlung {Money(spieler) - geld0} $ (430)");

        // ---- 2. Agent (0x49C288) und Laufregel (0x49C2BE) -------------------
        s.SetVarFuerProbe(3, s.Minutes - 11);           // EINGRIFF: zehn Minuten
        Block();
        var agent = SatzAuf(7000);
        Pruefe(agent != null && agent.Mark == 194 && s.VarAt(5) == 1,
               $"Agent auf Platz 7000 (Marke {agent?.Mark}), v[5]={s.VarAt(5)} (1)");
        s.SetVarFuerProbe(6, s.Minutes - 11);
        Block();
        Pruefe(s.VarAt(5) == 2, $"Laufregel auf der Uhr: v[5]={s.VarAt(5)} (2)");
        if (agent == null) return sb.Append("hotelplaza-check: FEHLER (kein Agent)").ToString();

        // ---- 3. Übernahme (0x411000) und Regel 13 (0x49C3F0) ---------------
        Entity? eigene = null;
        foreach (var q in _entities)
            if (!q.Dead && !q.IsBuilding && !q.IsProp && q.Mobile && q.Owner == spieler)
            { eigene = q; break; }
        bool versetzt = false;
        if (eigene != null && _nav != null)
        {
            int ei = _entities.IndexOf(eigene);
            foreach (var z in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1),
                                      new Vector2I(0, -1), new Vector2I(1, 1), new Vector2I(-1, -1) })
            {
                int c = agent.Col + z.X, r = agent.Row + z.Y;
                if (!MechFrei(c, r)) continue;
                _nav.ClearOccupant(eigene.Col, eigene.Row, ei);
                eigene.Col = c; eigene.Row = r; eigene.Path = null;
                _nav.SetOccupant(c, r, ei);                  // EINGRIFF: versetzt
                versetzt = true;
                break;
            }
        }
        Pruefe(versetzt, "eigene Einheit neben den Agenten gestellt");
        TakeoverTick();
        Pruefe(agent.Owner == spieler, $"Agent uebergelaufen (Besitzer {agent.Owner})");
        Pruefe(UebernahmeAlt ? agent.Slot == 7000 : agent.Slot < 1000,
               $"Platz nach der Uebernahme {agent.Slot}" +
               (UebernahmeAlt ? " (Nullmodell: 7000)" : " (Block 0..999)"));
        Block();
        Pruefe(s.VarAt(7) == 1 && texte213 == 1 && s.VarAt(8) == agent.Slot,
               $"Regel 13: v[7]={s.VarAt(7)} (1), Text 213 x{texte213}, v[8]={s.VarAt(8)} ({agent.Slot})");
        Block();
        Pruefe(s.VarAt(7) == 1, $"Regel 14 bleibt still: v[7]={s.VarAt(7)} (1)");

        // ---- 4. Untergestellt (0x49C4B3..0x49C4FE) -------------------------
        Entity? hotel = null;
        foreach (var q in _entities) if (q.IsBuilding && q.Slot == 12) { hotel = q; break; }
        int hotelHp = hotel?.Hp ?? -1;
        agent.Ukol = 50;                                  // EINGRIFF: in der Basis
        Block();
        Pruefe(s.VarAt(7) == 3 && s.VarAt(102) == 2 && texte214 == 1 && s.VarAt(9) == hotelHp,
               $"Text 214 x{texte214}, v[7]={s.VarAt(7)} (3), v[102]={s.VarAt(102)} (2), " +
               $"v[9]={s.VarAt(9)} ({hotelHp})");

        // ---- 5. VIPs (0x49C53A) --------------------------------------------
        if (hotel != null) hotel.Hp = s.VarAt(9) / 2;   // genau die Haelfte: noch NICHT
        Block();
        Pruefe(s.VarAt(7) == 3, $"Hotel auf genau der Haelfte ({hotel?.Hp}): v[7]={s.VarAt(7)} (3)");
        if (hotel != null) hotel.Hp = s.VarAt(9) / 2 - 1;  // EINGRIFF: Beschuss
        Block();
        var vip1 = SatzAuf(s.VarAt(17));
        var vip2 = SatzAuf(s.VarAt(18));
        Pruefe(s.VarAt(7) == 4 && vip1 != null && vip2 != null &&
               vip1.Owner == 4 && vip2.Owner == 4 && vip1.Mark == 194 && vip2.Mark == 194,
               $"VIPs: v[7]={s.VarAt(7)} (4), v[17]={s.VarAt(17)} bei ({vip1?.Col},{vip1?.Row}), " +
               $"v[18]={s.VarAt(18)} bei ({vip2?.Col},{vip2?.Row}), Spieler {vip1?.Owner}/{vip2?.Owner}");
        Block();
        Pruefe(s.VarAt(16) == 0 && s.VarAt(15) == 0,
               $"lebende VIPs gelten nicht als tot: v[16]={s.VarAt(16)}, v[15]={s.VarAt(15)}");

        // ---- 6. Beide tot (0x49C5AC..0x49C618) -----------------------------
        int geld1 = Money(spieler);
        if (vip1 != null) Kill(_entities.IndexOf(vip1), vip1, grund: "hotelplaza-check");
        if (vip2 != null) Kill(_entities.IndexOf(vip2), vip2, grund: "hotelplaza-check");
        Block();
        Block();
        Pruefe(s.VarAt(102) == 10 && texte100 == 1 && Money(spieler) - geld1 == 800,
               $"erfuellt: v[102]={s.VarAt(102)} (10), Text 100 x{texte100}, " +
               $"Zahlung {Money(spieler) - geld1} $ (800)");

        s.ShowText = vorherText;
        sb.Append($"  (Text 450 x{texte450} — steht in keiner HELPG.TXT, das Original zeigt dort nichts)\n");
        sb.Append(fehler == 0 ? "hotelplaza-check: IN ORDNUNG"
                              : $"hotelplaza-check: {fehler} FEHLER");
        return sb.ToString();
    }

    private static string UebernahmeAltText()
        => UebernahmeAlt ? "  ⚠ NULLMODELL --uebernahme-alt: Regel 13 MUSS ausbleiben" : "";
}
