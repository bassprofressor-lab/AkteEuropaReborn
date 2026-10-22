namespace AkteEuropaReborn.Rendering;

using System.Text;

/// <summary>
/// <c>--bauteilstufe-check</c> (bug-589, Reaper R1: »im Erstellungsmenü bleibt die Zahl in
/// den eckigen Klammern auf 0 stehen«). Ohne Basis, auf jeder Kampagnenkarte lauffähig:
/// <list type="number">
/// <item>vorher: Stufe des Bauteils (Vorgabe 163, 6x6 Reifen) im Block des Menschen und in
/// der Klammer des Erstellungsfensters (<c>DesignWindow.Name</c>, derselbe Text, den die
/// Fahrwerksliste zeigt) — beide 0;</item>
/// <item><c>Aufwerten(Mensch, Bauteil)</c> — der direkte Weg ohne Laufsatz, bis die
/// Entwurfsrechnung eines Entwurfs mit diesem Bauteil sich sichtbar ändert (der Zuwachs
/// ist gewürfelt, höchstens 8 Stufen);</item>
/// <item>nachher: Block = Klammer im Erstellungsfenster = Klammer in der Werteliste =
/// Infofenster-Stufe = k; derselbe Bauteil für den ANDEREN Spieler bleibt »(0)« (die
/// Blöcke sind getrennt);</item>
/// <item>Werteliste (<c>BaseWindow.StatsFor</c>): Zahlen = <c>Compute(…, Mensch)</c>,
/// Klammer ohne Leerzeichen (0x469337 → 0x5017E4 "("); Erstellung mit (0x46CC6D → " (").</item>
/// </list>
/// Nullmodelle: <c>--bauteilstufe-global-alt</c> (Teil 3a fällt: 1 gegen 0, Reapers Bild),
/// <c>--werteliste-global-alt</c> (Teil 4a fällt), <c>--klammerschreibweise-alt</c> (Teil 4b).
/// </summary>
public partial class MapEntityLayer
{
    public string BauteilstufeCheck(int bauteil)
    {
        var sb = new StringBuilder("bauteilstufe-check (bug-589)");
        if (UI.UnitStatBook.BauteilStufeGlobalAlt) sb.Append(" (⚠ Nullmodell --bauteilstufe-global-alt: Teil 3a MUSS fallen)");
        if (UI.BaseWindow.WertelisteGlobalAlt) sb.Append(" (⚠ Nullmodell --werteliste-global-alt: Teil 4a MUSS fallen)");
        if (UI.UnitStatBook.KlammerschreibweiseAlt) sb.Append(" (⚠ Nullmodell --klammerschreibweise-alt: Teil 4b MUSS fallen)");
        UI.UnitStatBook.Load();
        BauteileVorbereiten();
        int h = UI.SkirmishSetup.Human is >= 0 and < 8 ? UI.SkirmishSetup.Human : 0;
        int anderer = h == 0 ? 1 : 0;
        int vp = ViewPlayer;

        // ein Entwurf des Wertebuchs mit diesem Bauteil (Fahrwerk, Aufbau oder Verbesserung)
        UI.UnitStatBook.Entry? ent = null;
        foreach (var e in UI.UnitStatBook.All())
            if (e.Propulsion == bauteil || e.Weapon == bauteil || e.Equip == bauteil) { ent = e; break; }
        sb.Append($"\n   Mensch {h}, Betrachter {vp}, anderer {anderer}; Bauteil {bauteil} »{UI.UnitStatBook.ComponentLongName(bauteil)}«"
                  + $"; Entwurf {(ent is { } x ? $"»{x.Name}« (W{x.Weapon}/F{x.Propulsion}/V{x.Equip})" : "KEINER")}");
        if (ent is not { } en || BauteilFuer(h, bauteil) is not { Length: >= 58 })
            return sb.Append("\n   keine Probe moeglich — sagt NICHTS\n   Ergebnis: DURCHGEFALLEN").ToString();

        string Fenster() => UI.DesignWindow.Name(new DesignScreen.Part { Id = bauteil, Name = "?" });
        int vorBlock = BauteilFuer(h, bauteil)![0x01];
        string vorFenster = Fenster();
        bool ok0 = vorBlock == 0 && vorFenster.EndsWith(" (0)");
        sb.Append($"\n   {(ok0 ? "ok  " : "FEHL")} Teil 1: vorher Block {vorBlock}, Erstellung »{vorFenster}«");

        string Werte(Simulation.DesignMath.Derived d) => $"A/V {d.Attack}/{d.Defence} Sicht {d.Sight} Reichw. {d.Range} Sprit {d.Fuel} Munition {d.Ammo} Energie {d.Hp} Geschw. {d.Speed} Nachladen {UI.UnitStatBook.AnzeigeNachladen(d.Reload)}";
        string grund = Werte(Simulation.DesignMath.Compute(en.Weapon, en.Propulsion, en.Equip));
        int k = 0;
        for (; k < 8; )
        {
            if (Aufwerten(h, bauteil) < 0) break;
            k++;
            if (Werte(Simulation.DesignMath.Compute(en.Weapon, en.Propulsion, en.Equip, h)) != grund) break;
        }
        var dh = Simulation.DesignMath.Compute(en.Weapon, en.Propulsion, en.Equip, h);
        bool geaendert = Werte(dh) != grund;
        sb.Append($"\n   {(k > 0 ? "ok  " : "FEHL")} Teil 2: Aufwerten({h}, {bauteil}) {k}-mal, Werte des Entwurfs "
                  + (geaendert ? "veraendert" : "UNVERAENDERT (Teil 4a ohne Urteil)"));
        if (k == 0) return sb.Append("\n   Ergebnis: DURCHGEFALLEN").ToString();

        int nachBlock = BauteilFuer(h, bauteil)![0x01];
        string nachFenster = Fenster();
        string anderFenster = UI.UnitStatBook.ComponentLongLabel(bauteil, anderer);
        int info = UI.UnitStatBook.ComponentStufe(bauteil, vp);
        bool ok3a = nachBlock == k && nachFenster.EndsWith($" ({k})") && (vp != h || info == k);
        bool ok3b = anderFenster.EndsWith(" (0)");
        sb.Append($"\n   {(ok3a ? "ok  " : "FEHL")} Teil 3a: nachher Block {nachBlock} | Erstellung »{nachFenster}« | Infofenster-Stufe {info} (Soll alle {k})");
        sb.Append($"\n   {(ok3b ? "ok  " : "FEHL")} Teil 3b: Spieler {anderer} »{anderFenster}« (Soll (0), Bloecke getrennt)");

        string liste = UI.BaseWindow.StatsFor(en.Name);
        string flach = liste.Replace('\n', '|');
        // Teil 4a: die Zahlen der Werteliste = Rechnung aus dem Block des Menschen
        bool ok4a = !geaendert ||
                    (liste.Contains($"Sprit {dh.Fuel}\n") && liste.Contains($"A/V {dh.Attack}/{dh.Defence}\n")
                     && liste.Contains($"Sicht {dh.Sight}\n") && liste.Contains($"Energie : {dh.Hp}\n")
                     && liste.Contains($"Geschw. {dh.Speed}\n") && liste.EndsWith($"Munition {dh.Ammo}")
                     && liste.Contains($"Nachladen {UI.UnitStatBook.AnzeigeNachladen(dh.Reload)}\n")
                     && liste.Contains($"Reichw. {dh.Range}/"));
        // Teil 4b: Klammer der Werteliste ohne Leerzeichen, mit der Stufe k
        string kurz = UI.UnitStatBook.ComponentPlain(bauteil);
        bool inListe = bauteil == en.Weapon || bauteil == en.Propulsion;   // Verbesserung steht nicht in der Werteliste
        bool ok4b = !inListe || (liste.Contains($"{kurz}({k})\n") && !liste.Contains($"{kurz} ("));
        sb.Append($"\n   {(ok4a ? "ok  " : "FEHL")} Teil 4a: Werteliste-Zahlen = Compute(…, {h}) [{Werte(dh)}]; Grundtafel [{grund}]");
        sb.Append($"\n   {(ok4b ? "ok  " : "FEHL")} Teil 4b: Werteliste »{kurz}({k})« ohne Leerzeichen" + (inListe ? "" : " — Bauteil nicht in der Liste, ohne Urteil"));
        sb.Append($"\n   Werteliste: {flach}");
        bool alles = ok0 && ok3a && ok3b && ok4a && ok4b;
        return sb.Append($"\n   Ergebnis: {(alles ? "BESTANDEN" : "DURCHGEFALLEN")}").ToString();
    }
}
