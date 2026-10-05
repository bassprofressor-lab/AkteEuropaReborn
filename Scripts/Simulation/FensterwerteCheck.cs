namespace AkteEuropaReborn.Rendering;

using System.Text;

/// <summary>
/// <b><c>--fensterwerte-check</c></b> — bug-441/442/443 (KayelGee R4, Meldungen
/// 31, 35, 32). Tafelprobe ueber die Stellen, die die Fenster selbst rufen:
/// <list type="bullet">
/// <item>Nachladen: <c>AnzeigeNachladen(20) == 7</c>, <c>(120) == 4</c>, und fuer
/// jeden Wert 0..200 die Formel (16 − ⌊√x⌋)/2 + 1 (@0x46944D);</item>
/// <item>Entwurfsname: (1, —, 161) → »Kanone-0-161«, (4, —, 161) →
/// »M-Gewehr-0-161«, (2, 81, 165) → »S.Kanone-81-165«;</item>
/// <item>Basistitel: das erste Gebaeude der Art 1 traegt »Basis &lt;Platz+1&gt;«.</item>
/// </list>
/// Nullmodelle: <c>--nachladen-roh</c>, <c>--entwurfsname-alt</c> (der alte Name
/// laeuft nicht ueber NameWieOriginal — die Zeile prueft darum den Weg, den
/// ProposedName nimmt), <c>--gebaeudename-karte</c>.
/// </summary>
public partial class MapEntityLayer
{
    public string FensterwerteCheckLine()
    {
        var sb = new StringBuilder("fensterwerte-check\n");
        bool ok = true;

        int a20 = UI.UnitStatBook.AnzeigeNachladen(20), a120 = UI.UnitStatBook.AnzeigeNachladen(120);
        int falsch = 0;
        for (int x = 0; x <= 200; x++)
            if (UI.UnitStatBook.AnzeigeNachladen(x) != (16 - (int)System.Math.Sqrt(x)) / 2 + 1) falsch++;
        bool nOk = a20 == 7 && a120 == 4 && falsch == 0;
        ok &= nOk;
        sb.Append($"  Nachladen: 20 -> {a20} (Soll 7), 120 -> {a120} (Soll 4), Formel {201 - falsch}/201{(nOk ? "" : "   ⚠")}\n");

        var faelle = new (int W, int? V, int P, string Soll)[]
        {
            (1, null, 161, "Kanone-0-161"), (4, null, 161, "M-Gewehr-0-161"), (2, 81, 165, "S.Kanone-81-165"),
        };
        foreach (var f in faelle)
        {
            string ist = DesignScreen.EntwurfsnameAlt ? "(alter Weg)" : DesignScreen.NameWieOriginal(f.W, "", f.V, f.P);
            bool g = ist == f.Soll;
            ok &= g;
            sb.Append($"  Entwurfsname ({f.W},{(f.V?.ToString() ?? "—")},{f.P}): {ist}, Soll {f.Soll}{(g ? "" : "   ⚠")}\n");
        }

        foreach (var e in _entities)
        {
            if (!e.IsBuilding || e.Dead || e.BType != 1 || e.Owner != ViewPlayer) continue;   // das Baumenue gibt es nur fuer eigene
            int vorher = _selected;
            _selected = _entities.IndexOf(e);
            string titel = BuildPanelHead()?.Name ?? "(keiner)";
            _selected = vorher;
            string soll = $"{BuildingTypeName(1)} {e.Slot + 1}";
            bool g = titel == soll;
            ok &= g;
            sb.Append($"  Basistitel Platz {e.Slot} (Karte »{e.Name}«): {titel}, Soll {soll}{(g ? "" : "   ⚠")}\n");
            break;
        }
        // bug-460 (Meldung 19): was das Erstellen-Fenster in der Kampagne anbietet —
        // nur Bauteile, die ForschungBesitzt bejaht (Fahrplan des Missionsskripts).
        LoadDesignParts();
        ErfundeneWaffenNachtragen();
        string Ids(System.Collections.Generic.List<DesignScreen.Part> l)
            => string.Join(",", l.ConvertAll(p => p.Id.ToString()));
        int fremd = 0;
        if (UI.SkirmishSetup.CampaignMission > 0)
            foreach (var l in new[] { Designer.Propulsions, Designer.Weapons, Designer.Equipment })
                foreach (var p in l) if (!ForschungBesitzt(ViewPlayer, p.Id)) fremd++;
        bool eOk = fremd == 0;
        ok &= eOk;
        sb.Append($"  Erstellen-Fenster (Mission {UI.SkirmishSetup.CampaignMission}): Fahrwerke [{Ids(Designer.Propulsions)}], "
                + $"Waffen [{Ids(Designer.Weapons)}], Ausruestung [{Ids(Designer.Equipment)}], nicht besessen {fremd}"
                + (EntwurfAlleTeile ? " (Nullmodell --entwurf-alle-teile)" : "") + (eOk ? "\n" : "   ⚠\n"));
        sb.Append(ok ? "fensterwerte-check: BESTANDEN" : "fensterwerte-check: DURCHGEFALLEN");
        return sb.ToString();
    }
}
