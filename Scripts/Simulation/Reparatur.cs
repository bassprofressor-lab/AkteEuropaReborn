namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// ⭐⭐ <b>DIE GEBÄUDEREPARATUR — Klicksperre und Schadensstufe</b> (15.09.2026, seine
/// Meldung aus K15/K16: »die Reparatur läuft nicht immer sauber, das Drücken von Reparatur
/// zieht am Anfang kurz Lebenspunkte ab«). Lesung berichte/gebaeudereparatur-opus.md.
///
/// <list type="bullet">
/// <item><b>Der Abzug ist original:</b> alle vier Behandler (519 Fabrik C 0x43FBA0, 520
/// Flughafen, 521 Basis, 522 Mine) setzen bei hp &gt; 50 <c>hp := 29·hp/30</c>.</item>
/// <item><b>Die Klicksperre fehlte:</b> alle vier Klickarme des Originals tun nichts, wenn
/// die Reparatur schon läuft (C 0x44AF3F / 0x449FA2 / 0x44A134 / 0x44BD4C, F 0x449F32 …).
/// Bei uns hatte nur das Fabrikfenster sie; Basisfenster, Minen-/Flughafenfenster und
/// Taste K zogen bei jedem weiteren Klick wieder 1/30 ab (gemessen 739 → 714 → 690). Die
/// Sperre gehört an die ABSENDER, nicht in den Behandler. <c>--reparaturklick-alt</c>.</item>
/// <item><b>Die Schadensstufe blieb stehen:</b> der Behandler ruft <c>0x4CBBF0(Platz, 0)</c>
/// (Flag 0 = nur schreiben, nicht zünden), der Reparaturtakt ebenso. Bei uns zog niemand
/// sie nach, und der nächste Treffer hielt die gesunkene Stufe für einen Wechsel und
/// zündete Brand und Explosion. <c>--reparaturstufe-alt</c>.</item>
/// </list>
/// ⚠ NICHT gebaut (seine Entscheidung offen): der Reparaturtakt läuft über
/// <c>TickScale = 16</c> 3,1× langsamer als das Original.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--reparaturklick-alt</c> — der Stand bis 15.09.2026: nur das
    /// Fabrikfenster sperrt einen Reparaturklick auf laufende Reparatur.</summary>
    public static bool ReparaturklickAlt;

    /// <summary><c>--reparaturstufe-alt</c> — der Stand bis 15.09.2026: die gespeicherte
    /// Schadensstufe wird beim Reparieren nicht nachgezogen.</summary>
    public static bool ReparaturstufeAlt;

    /// <summary>Wie oft ein Reparaturklick still verworfen wurde.</summary>
    public int ReparaturKlickStill;

    /// <summary>Die Sperre der Klickarme: läuft die Reparatur dieses Gebäudes schon, geschieht
    /// nichts — kein Befehl, kein Klang.</summary>
    public bool ReparaturKlickGesperrt(Entity e)
    {
        if (ReparaturklickAlt || !e.IsBuilding) return false;
        if (e.State != JobState(e, BuildingJob.Repair)) return false;
        ReparaturKlickStill++;
        return true;
    }

    /// <summary>0x4CBBF0(Platz, 0) — die Stufe schreiben, ohne zu zünden.</summary>
    private void ReparaturStufe(Entity e)
    {
        if (ReparaturstufeAlt) return;
        GebaeudeStufeNachziehen(e, zuenden: false);
    }

    /// <summary>
    /// <c>--reparaturklick-check</c>:
    /// <list type="number">
    /// <item>Klicksperre über Taste K (<see cref="StartRepair"/>) und den Fensterweg
    /// (<see cref="BuildingWindowRepair"/> bzw. Basisfenster <c>PostRepairFromPanel</c>):
    /// erster Klick hp−1/30, zwei weitere Klicks auf laufende Reparatur ändern nichts.
    /// Nullmodell <c>--reparaturklick-alt</c>: jeder Klick zieht wieder ab.</item>
    /// <item>Schadensstufe: nach dem Reparieren über eine Stufengrenze ist die gespeicherte
    /// Stufe die gezeichnete, und ein Treffer von 1 TP ohne Stufenwechsel zündet nichts.
    /// Nullmodell <c>--reparaturstufe-alt</c>: derselbe Treffer zündet.</item>
    /// </list>
    /// ⚠ EINGRIFFE: Trefferpunkte und Zustand eines eigenen Gebäudes werden gesetzt.
    /// </summary>
    public string ReparaturklickCheck()
    {
        var sb = new System.Text.StringBuilder("reparaturklick-check\n");
        bool ok = true;
        void Soll(bool b, string was) { sb.Append($"  {(b ? "ok  " : "⚠ FALSCH")} {was}\n"); ok &= b; }
        if (ReparaturklickAlt) sb.AppendLine("  ⚠ NULLMODELL --reparaturklick-alt: Teil 1 MUSS scheitern");
        if (ReparaturstufeAlt) sb.AppendLine("  ⚠ NULLMODELL --reparaturstufe-alt: Teil 2 MUSS scheitern");

        Entity? Eigenes(System.Func<Entity, bool> art)
        {
            foreach (var x in _entities)
                if (x.IsBuilding && !x.Dead && !x.IsProp && x.Owner == ViewPlayer && x.HpMax > 200 && x.Built != 0 && art(x)) return x;
            return null;
        }

        // ---- 1. Klicksperre, Taste K und Fensterweg --------------------------------
        int gemessen = 0;
        foreach (var (name, art) in new (string, System.Func<Entity, bool>)[]
                 { ("Basis", x => x.BType == 1), ("Fabrik", x => x.BType is 2 or 3 or 4), ("Mine", x => x.BType is 10 or 15) })
        {
            var b = Eigenes(art);
            if (b == null) { sb.AppendLine($"  ({name}: keine eigene auf der Karte)"); continue; }
            gemessen++;
            int idx = _entities.IndexOf(b);
            b.State = StAktiv; b.Hp = b.HpMax - 60;
            int h0 = b.Hp, soll1 = 29 * h0 / 30, still0 = ReparaturKlickStill;
            _sel.Clear(); _sel.Add(idx); _selected = idx;
            StartRepair();                                        // Taste K, erster Klick
            int h1 = b.Hp;
            StartRepair();                                        // Taste K, zweiter Klick
            BuildingWindowRepair();                               // Fensterweg (Fenstergebaeude)
            int saetze = PostRepairFromPanel();                   // Basisfensterweg durch den Ring
            for (int k = 0; k < 5; k++) SimTickFuerProbe();       // Ring leerlaufen (Ankunft mitmessen)
            int h2 = b.Hp;
            // Der Reparaturtakt gibt in 5 Takten hoechstens 1..2 TP zurueck — gemessen wird
            // darum »kein zweiter Abzug«: h2 >= h1.
            h2 = h2 >= h1 && h2 <= h1 + 5 ? h1 : h2;
            _sel.Clear(); _selected = -1;
            Soll(h1 == soll1 && h2 == soll1,
                 $"{name} Platz {b.Slot}: TP {h0} -> erster Klick {h1} (Soll {soll1}) -> zwei weitere Klicks {h2} (Soll {soll1}), "
                 + $"still verworfen {ReparaturKlickStill - still0}x, Basisfenster-Saetze {saetze}, Zustand {b.State}");
            b.Hp = b.HpMax; b.State = StAktiv; ReparaturStufe(b);
        }

        if (gemessen == 0) Soll(false, "Klicksperre: kein eigenes Gebaeude — sagt NICHTS");

        // ---- 2. Schadensstufe ------------------------------------------------------
        var g = Eigenes(x => Patterns != null && Patterns.GetBuildingType(x.BType).PatternCount >= 5);
        if (g == null || Patterns == null) Soll(false, "Stufe: kein eigenes Gebaeude mit mindestens 5 Schadensbildern");
        else
        {
            int count = Patterns.GetBuildingType(g.BType).PatternCount;
            int step = g.HpMax / count;
            // Stufe k = 3 (so dass k-1 = 2 noch zuendet), Grenze zu k-1 bei hp = HpMax - 3·step
            int grenze = g.HpMax - 3 * step;
            g.State = StAktiv;
            g.Hp = grenze - 1;
            int still = ReparaturKlickStill;
            GebaeudeStufeNachziehen(g, zuenden: false);           // Stand wie nach einem Treffer
            int stufeVor = g.Schadensstufe;
            _sel.Clear(); _sel.Add(_entities.IndexOf(g)); _selected = _entities.IndexOf(g);
            StartRepair();
            _sel.Clear(); _selected = -1;
            int t = 0;
            while (t < 20000 && (DamageFrame(g) >= stufeVor || g.Hp < grenze + 8) && g.Hp < g.HpMax) { SimTickFuerProbe(); t++; }
            int gezeichnet = DamageFrame(g), gespeichert = g.Schadensstufe;
            int brand0 = BrandZuendungen;
            g.Hp -= 1;                                           // ein Treffer von 1 TP, ohne Stufenwechsel
            bool wechsel = DamageFrame(g) != gezeichnet;
            GebaeudeStufeNachziehen(g);                          // was ApplyHit danach tut
            Soll(stufeVor == 3 && gezeichnet == 2 && gespeichert == gezeichnet,
                 $"Stufe: {BuildingName(g)} Platz {g.Slot} ({count} Bilder) von Stufe {stufeVor}, nach {t} Takten TP {g.Hp + 1}: gezeichnet {gezeichnet}, gespeichert {gespeichert}");
            Soll(!wechsel && BrandZuendungen == brand0,
                 $"Stufe: ein Treffer von 1 TP ohne Stufenwechsel ({(wechsel ? "WECHSEL" : "kein Wechsel")}) zuendet {BrandZuendungen - brand0}x (Soll 0)");
            g.Hp = g.HpMax; g.State = StAktiv; ReparaturStufe(g);
            _ = still;
        }

        sb.AppendLine($"  --reparaturklick-alt {ReparaturklickAlt}, --reparaturstufe-alt {ReparaturstufeAlt}");
        return sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN").ToString();
    }
}
