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
/// danach zurueckgesetzt; die geladene Karte bleibt dieselbe.</para>
///
/// <para>⭐ 29.10.2026 abends — <b>die Rumpfbildprobe war falsch, nicht der Bildausfuhr.</b>
/// Hier stand »fuer 169/172 gibt es KEIN Rumpfbild, der Bildausfuhr kennt nur Fahrwerke
/// der EXE-Einheitentafel«. Die Probe schaute aber fest in <c>res://Assets/Legacy</c>,
/// den Entwicklungsbaum vom 06.08. (nicht im Git, nie ausgeliefert). Der Zeichner laedt
/// ueber <see cref="Core.Content.Path"/> zuerst <c>user://data</c>, und dort liegen
/// <c>hull/169</c> (= ROBO-Teil 10) und <c>hull/172</c> (= Teil 15) seit dem Einlesen —
/// die EXE-Einheitentafel FUEHRT beide Zeilen (Name + Bauteil), und die zweite Schleife
/// in <c>UnitsExporter.Run</c> laeuft seit dem 15.08. ueber die ganze Tafel. Die Probe
/// misst jetzt den Ladeweg des Zeichners (<see cref="RumpfbildZeile"/>); Gegenschalter
/// <c>--wiesel-rumpfbild-res-alt</c>. Bericht <c>berichte/wiesel-rumpfbild-opus.md</c>.</para>
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
        _wieselBilderDa = 0; _wieselBilderSoll = 0;
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
                        + (liste == "Fahrwerk" ? $"   | Rumpfbild hull/{teil}: {RumpfbildZeile(teil)}" : ""));
        }
        UI.SkirmishSetup.CampaignMission = merk;
        LoadDesignParts();
        sb.AppendLine($"  in ihrer ersten Mission: {jetzt}/7 (Soll 7), in der Mission davor: {davor}/7 (Soll 0)");
        bool nullmodell = BesitzGrundtafelAlt || FahrwerklisteKatalogAlt;
        sb.AppendLine($"  Rumpfbilder der Fahrwerke (Ladeweg wie der Zeichner, 8 Blicke mit Bildpunkten): {_wieselBilderDa}/{_wieselBilderSoll}"
                    + (WieselRumpfbildResAlt ? "  [--wiesel-rumpfbild-res-alt: nur res://Assets/Legacy geprueft]" : ""));
        sb.Append(jetzt == 7 && davor == 0 && _wieselBilderDa == _wieselBilderSoll ? "  BESTANDEN" : "  DURCHGEFALLEN");
        if (nullmodell) sb.Append(" (Nullmodell: DURCHGEFALLEN ist das erwartete Ergebnis)");
        return sb.ToString();
    }

    private int _wieselBilderDa, _wieselBilderSoll;

    /// <summary><c>--wiesel-rumpfbild-res-alt</c> — der Stand vor dem 29.10.2026 abends: die
    /// Rumpfbildprobe schaut nur in den Entwicklungsbaum <c>res://Assets/Legacy/Units</c>
    /// (dort fehlen 169/172, der Baum ist vom 06.08. und nicht im Git) und meldet darum
    /// »KEINES«, obwohl der Zeichner das Bild aus <c>user://data</c> laedt.</summary>
    public static bool WieselRumpfbildResAlt;

    /// <summary>⭐ 29.10.2026 (Nachtrag bug-659) — die Rumpfbildprobe misst jetzt, was der
    /// ZEICHNER bekommt: <see cref="GetHullTexture"/> (Ladeweg <see cref="LoadUnitPart"/> →
    /// <see cref="Core.Content.Path"/>, <c>user://data</c> vor <c>res://Assets/Legacy</c>), alle
    /// acht Blicke der flachen Lage, je mit mindestens einem deckenden Bildpunkt. Vorher
    /// stand hier eine feste <c>res://</c>-Probe — und die meldete »KEINES« fuer Bilder, die
    /// der Bildausfuhr seit dem 15.08. schreibt (zweite Schleife ueber die ganze
    /// Einheitentafel, <c>UnitsExporter.Run</c>).</summary>
    private string RumpfbildZeile(int teil)
    {
        _wieselBilderSoll++;
        if (WieselRumpfbildResAlt)
        {
            bool alt = FileAccess.FileExists($"res://Assets/Legacy/Units/hull/{teil}/f0.png");
            if (alt) _wieselBilderDa++;
            return alt ? "da (res://)" : "⚠ KEINES (res://)";
        }
        int blicke = 0;
        for (int f = 0; f < 8; f++)
        {
            var t = GetHullTexture(teil, f, 0, 0);
            var img = t?.GetImage();
            if (img == null) continue;
            bool deckt = false;
            for (int y = 0; y < img.GetHeight() && !deckt; y++)
                for (int x = 0; x < img.GetWidth() && !deckt; x++)
                    if (img.GetPixel(x, y).A > 0.5f) deckt = true;
            if (deckt) blicke++;
        }
        string quelle = Core.Content.Path($"Units/hull/{teil}/f0.png");
        quelle = quelle.StartsWith(Core.Content.UserRoot) ? "user://data" : "res://Assets/Legacy";
        if (blicke == 8) _wieselBilderDa++;
        return blicke == 8 ? $"da, 8/8 Blicke aus {quelle}" : $"⚠ {blicke}/8 Blicke ({quelle})";
    }

    /// <summary><c>--wiesel-bild</c> (29.10.2026) — fuer den Bildlauf: das erste eigene
    /// Landfahrzeug wird zum Wuestenwiesel (Rumpf 169), das naechste zum Schweren Blocker
    /// (172); Waffe, Blick und Ort bleiben. ⚠ EINGRIFF nur fuer ein Bild (kein Entwurf ueber
    /// die Fabrik, keine Werte umgestellt) — gemessen wird allein, ob und wie der Zeichner
    /// den Rumpf zeichnet. Rueckgabe: die Zelle der ersten umgebauten Einheit.</summary>
    public (int Col, int Row)? WieselUmbau(System.Text.StringBuilder log)
    {
        int[] ziel = { 169, 172 };
        int n = 0;
        (int, int)? erste = null;
        foreach (var e in _entities)
        {
            if (n >= ziel.Length) break;
            if (e.Dead || e.Owner != 0 || e.Infantry >= 0 || e.GameUnitType == 3) continue;
            if (e.UnitType < 160 || e.UnitType > 168) continue;
            log.Append($"wiesel-bild: ({e.Col},{e.Row}) Rumpf {e.UnitType} -> {ziel[n]}, Waffe {e.Weapon}, Blick {e.Facing}\n");
            e.UnitType = ziel[n];
            erste ??= (e.Col, e.Row);
            n++;
        }
        QueueRedraw();
        return erste;
    }
}
