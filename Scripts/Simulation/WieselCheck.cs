namespace AkteEuropaReborn.Rendering;

using Godot;

/// <summary>
/// <b>--wiesel-check</b> (29.10.2026, bug-659, KayelGee R8 K17) — die sieben Bauteile,
/// die in der EXE-Grundtafel Besitz 0 tragen und die das Missionsskript erst spaet
/// gibt (<c>berichte/forschung-kampagne-r8-opus.md</c> Bauvorschlag B):
/// 169 → M9, 170 → M13, 68/69 → M15, 171 → M19, 172 → M20, 65 → M29
/// (Fahrplan <c>campaign_schedule.json</c>, an M2/M3 gegen die EXE bestaetigt).
///
/// <para>Je Zeile in ihrer ersten Mission: steht sie in der Erstellungsliste
/// (<see cref="LoadDesignParts"/> — Fahrwerke bzw. Aufbauten) UND liefert
/// <c>ForschungBesitzt(0, b)</c> wahr? <b>Soll 7/7.</b> Gegenprobe: in der Mission
/// davor <b>0/7</b>. Nullmodell <c>--besitz-grundtafel-alt --fahrwerkliste-katalog-alt</c>:
/// 0/7 (KayelGees Bild). Mit nur <c>--fahrwerkliste-katalog-alt</c> fehlen 169 und
/// 172 (Sperre 2 traegt allein); mit nur <c>--besitz-grundtafel-alt</c> fehlen alle
/// sieben (Sperre 1 traegt allein).</para>
///
/// <para>⚠ EINGRIFF: <c>CampaignMission</c> wird fuer die Zaehlung umgestellt und
/// danach zurueckgesetzt; die geladene Karte bleibt dieselbe. ⚠ Vorbehalt, mit
/// ausgegeben: fuer Fahrwerk 169 (und 172) gibt es KEIN Rumpfbild unter
/// <c>Assets/Legacy/Units/hull/</c> — der Bildausfuhr kennt nur Fahrwerke der
/// EXE-Einheitentafel. Eine gebaute Wiesel-Einheit waere also baubar, aber ob sie
/// gezeichnet wird, misst dieser Pruefstand nicht.</para>
/// </summary>
public partial class MapEntityLayer
{
    public static bool WieselCheckAn;

    public string WieselCheck()
    {
        var sb = new System.Text.StringBuilder("wiesel-check"
            + (BesitzGrundtafelAlt ? " --besitz-grundtafel-alt" : "")
            + (FahrwerklisteKatalogAlt ? " --fahrwerkliste-katalog-alt" : "") + "\n");
        int merk = UI.SkirmishSetup.CampaignMission;
        var faelle = new (int Teil, int Mission, string Liste)[]
        {
            (169, 9, "Fahrwerk"), (170, 13, "Fahrwerk"), (68, 15, "Aufbau"), (69, 15, "Aufbau"),
            (171, 19, "Fahrwerk"), (172, 20, "Fahrwerk"), (65, 29, "Aufbau"),
        };
        int jetzt = 0, davor = 0;
        foreach (var (teil, mission, liste) in faelle)
        {
            (bool inListe, bool besitz) Zaehle(int m)
            {
                UI.SkirmishSetup.CampaignMission = m;
                LoadDesignParts();
                var l = liste == "Fahrwerk" ? Designer.Propulsions : Designer.Weapons;
                return (l.Exists(p => p.Id == teil), ForschungBesitzt(0, teil));
            }
            var (l1, b1) = Zaehle(mission);
            var (l0, b0) = Zaehle(mission - 1);
            bool ok1 = l1 && b1, ok0 = l0 || b0;
            if (ok1) jetzt++;
            if (ok0) davor++;
            string name = UI.UnitStatBook.ComponentLongName(teil);
            sb.AppendLine($"  {teil,3} {name,-20} M{mission}: Liste {(l1 ? "ja" : "nein")}, Besitz {(b1 ? "ja" : "nein")} -> "
                        + $"{(ok1 ? "da" : "FEHLT")}   | M{mission - 1}: Liste {(l0 ? "ja" : "nein")}, Besitz {(b0 ? "ja" : "nein")}"
                        + (liste == "Fahrwerk" ? $"   | Rumpfbild hull/{teil}: {(FileAccess.FileExists($"res://Assets/Legacy/Units/hull/{teil}/f0.png") ? "da" : "⚠ KEINES")}" : ""));
        }
        UI.SkirmishSetup.CampaignMission = merk;
        LoadDesignParts();
        sb.AppendLine($"  in ihrer ersten Mission: {jetzt}/7 (Soll 7), in der Mission davor: {davor}/7 (Soll 0)");
        bool nullmodell = BesitzGrundtafelAlt || FahrwerklisteKatalogAlt;
        sb.Append(jetzt == 7 && davor == 0 ? "  BESTANDEN" : "  DURCHGEFALLEN");
        if (nullmodell) sb.Append(" (Nullmodell: DURCHGEFALLEN ist das erwartete Ergebnis)");
        return sb.ToString();
    }
}
