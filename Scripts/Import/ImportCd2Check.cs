namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using AkteEuropaReborn.Core;

/// <summary>
/// Pruefstand <c>--import-cd2-check</c> (bug-379): rechnet die CD-2-Erkennung
/// an den CDs nach, die GERADE in den Laufwerken stecken, und an
/// Teilmengen davon — »nur CD 1«, »nur CD 2«, »beide« —, ohne etwas zu
/// schreiben. Dazu, nur LESEND, was in user://data eingelesen ist.
///
/// <para><c>--import-cd2-check=voll</c> laesst zusaetzlich den ECHTEN Weg
/// laufen, wie ein Spieler mit einem Laufwerk ihn geht: Import nur aus CD 1,
/// Zwischenlager, zweiter Import aus Lager + CD 2 — alles in einen Ordner unter
/// dem Temp-Verzeichnis. ⚠ user://data wird dabei NICHT angefasst
/// (<see cref="ContentBuilder(ContentSources.Source, string)"/>). Dauert
/// Minuten und braucht ~1 GB Platz, der danach wieder freigegeben wird.</para>
///
/// <para>Ergebniszeile: <c>import-cd2-check: BESTANDEN|DURCHGEFALLEN (n/m)</c>.
/// ⚠ Der Rueckgabewert eines kopflosen Laufs ist wertlos — die Zeile lesen.</para>
/// </summary>
public static class ImportCd2Check
{
    private static int _ok, _all;

    private static void Check(bool cond, string what)
    {
        _all++;
        if (cond) _ok++;
        GD.Print($"import-cd2-check: [{(cond ? "ok" : "FEHLER")}] {what}");
    }

    private static string Set(IEnumerable<int> s) => s.Any() ? DiscCoverage.Ranges(s) : "—";

    private static ContentSources.Source Of(IEnumerable<string> roots, string label)
    {
        var s = new ContentSources.Source { Kind = ContentSources.Kind.Disc, Label = label };
        foreach (string r in roots)
        {
            s.Roots.Add(r);
            s.Cabinet ??= ContentSources.CabinetIn(r);
            s.Exe ??= ContentSources.ExeIn(r);
        }
        return s;
    }

    public static int Run(bool voll)
    {
        _ok = _all = 0;
        var expected1 = Enumerable.Range(1, 15).ToList();
        var expected2 = Enumerable.Range(16, 18).ToList();

        // ---- die Rechenregeln selbst ------------------------------------------
        Check(DiscCoverage.Ranges(expected2) == "16–33", $"Ranges(16..33) = »{DiscCoverage.Ranges(expected2)}«");
        Check(DiscCoverage.Ranges(new[] { 3, 7, 8, 9, 12, 13 }) == "3, 7–9, 12, 13",
              $"Ranges(3,7,8,9,12,13) = »{DiscCoverage.Ranges(new[] { 3, 7, 8, 9, 12, 13 })}«");
        Check(DiscCoverage.DiscOf(15) == 1 && DiscCoverage.DiscOf(16) == 2 && DiscCoverage.DiscOf(33) == 2,
              "DiscOf: 15 -> CD 1, 16 -> CD 2, 33 -> CD 2");
        Check(DiscCoverage.Missing(Enumerable.Range(1, 33)).Count == 0, "Missing(1..33) ist leer");

        // ---- die Laufwerke ----------------------------------------------------
        var discs = ContentSources.Discs();
        if (discs == null)
        {
            GD.Print("import-cd2-check: keine Spiel-CD im Laufwerk — nur Rechenregeln und user://data geprueft");
        }
        else
        {
            foreach (string r in discs.Roots)
                GD.Print($"import-cd2-check: Laufwerk {r}: CW.ID -> CD {DiscCoverage.DiscId(r)}, " +
                         $"Missionen {Set(DiscCoverage.MissionsIn(new[] { r }))}, " +
                         $"Kabinett {(ContentSources.CabinetIn(r) != null ? "ja" : "nein")}, " +
                         $"Wechseldatentraeger {(DiscCoverage.IsRemovable(r) ? "ja" : "nein")}");
            var cd1 = discs.Roots.Where(r => DiscCoverage.MissionsIn(new[] { r }).Contains(1)).ToList();
            var cd2 = discs.Roots.Where(r => DiscCoverage.MissionsIn(new[] { r }).Contains(16)).ToList();

            // beide zugleich: KEINE Rueckfrage
            var both = DiscCoverage.MissionsIn(discs);
            GD.Print($"import-cd2-check: alle Laufwerke zusammen: {both.Count} Missionen, " +
                     $"fehlend {Set(DiscCoverage.Missing(both))}");
            if (cd1.Count > 0 && cd2.Count > 0)
                Check(DiscCoverage.Missing(both).Count == 0,
                      "beide CDs in zwei Laufwerken -> nichts fehlt -> keine Frage nach CD 2");

            if (cd1.Count > 0)
            {
                // nur CD 1 — der gemeldete Fall
                var src1 = Of(cd1, "nur CD 1");
                var miss1 = DiscCoverage.Missing(DiscCoverage.MissionsIn(src1));
                GD.Print($"import-cd2-check: nur CD 1: {DiscCoverage.Explain(miss1)}");
                Check(miss1.SequenceEqual(expected2), $"nur CD 1 -> fehlend {Set(miss1)} (erwartet 16–33)");
                Check(DiscCoverage.DiscsFor(miss1).SequenceEqual(new[] { 2 }), "nur CD 1 -> gefragt wird nach CD 2");
                Check(src1.Complete, "nur CD 1 traegt das Kabinett (GAME.EXE, ROBO.CWR)");
                Check(src1.Roots.All(DiscCoverage.IsRemovable), "CD 1 ist ein Wechseldatentraeger -> wird zwischengelagert");
                // »Weiter« mit noch derselben CD im Laufwerk darf NICHT neu einlesen
                Check(DiscCoverage.Useful(src1, miss1) == null,
                      "»Weiter« mit CD 1 noch im Laufwerk -> nichts Brauchbares, kein zweiter Lauf");
                if (cd2.Count > 0)
                {
                    var u = DiscCoverage.Useful(Of(cd2, "CD 2"), miss1);
                    Check(u != null && u.Roots.SequenceEqual(cd2), "»Weiter« mit CD 2 -> nimmt genau CD 2");
                    var merged = DiscCoverage.Merge(src1, u ?? Of(cd2, "CD 2"));
                    Check(DiscCoverage.MissionsIn(merged).Count == 33 && merged.Cabinet != null
                          && merged.Roots[0] == cd1[0],
                          $"Lager(CD 1) + CD 2 -> {DiscCoverage.MissionsIn(merged).Count} Missionen, " +
                          "Kabinett von CD 1, CD 1 zuerst durchsucht");
                }
            }
            if (cd2.Count > 0)
            {
                var src2 = Of(cd2, "nur CD 2");
                var miss2 = DiscCoverage.Missing(DiscCoverage.MissionsIn(src2));
                GD.Print($"import-cd2-check: nur CD 2: {DiscCoverage.Explain(miss2)}");
                Check(miss2.SequenceEqual(expected1), $"nur CD 2 -> fehlend {Set(miss2)} (erwartet 1–15)");
                Check(DiscCoverage.DiscsFor(miss2).SequenceEqual(new[] { 1 }), "nur CD 2 -> gefragt wird nach CD 1");
                Check(!src2.Complete, "CD 2 hat kein Kabinett (sie allein ergibt keine Einheitengrafiken)");
            }
        }

        // ---- der Ordnerweg ----------------------------------------------------
        foreach (string dir in new[] { @"F:\Akte Europa" })
        {
            var f = ContentSources.FromFolder(dir);
            if (f == null) { GD.Print($"import-cd2-check: Ordner {dir}: nicht vorhanden/keine Spieldaten"); continue; }
            var m = DiscCoverage.MissionsIn(f);
            GD.Print($"import-cd2-check: Ordner {dir}: {f.Kind}, Missionen {Set(m)}, " +
                     $"GAME.EXE {(f.Exe != null ? "ja" : "nein")}, DATA+LEVELS {(ContentSources.HasGameData(dir) ? "ja" : "nein")}, " +
                     $"Wechseldatentraeger {(DiscCoverage.IsRemovable(dir) ? "ja" : "nein")}");
        }

        // ---- was eingelesen ist (NUR LESEN) -----------------------------------
        Campaign.CampaignManager.Forget();
        var ms = Campaign.CampaignManager.Missions;
        var missImp = DiscCoverage.MissingImported();
        GD.Print($"import-cd2-check: eingelesen ({Core.Content.Path("Maps/campaign.json")}): " +
                 $"{ms.Count} Missionen, {DiscCoverage.Explain(missImp)}");
        int karten = ms.Count(m => Godot.FileAccess.FileExists(Core.Content.Path($"Maps/{m.Map}.entities.json")));
        Check(karten == ms.Count, $"jede Mission der campaign.json hat ihre Karte ({karten}/{ms.Count})");
        Check(ms.Count == 0 || missImp.Count == 0 || missImp.Count == 33 - ms.Count,
              "MissingImported passt zur Zahl der eingelesenen Missionen");

        if (voll) Voll(discs);

        GD.Print($"import-cd2-check: {(_ok == _all ? "BESTANDEN" : "DURCHGEFALLEN")} ({_ok}/{_all})");
        return _ok == _all ? 0 : 1;
    }

    /// <summary>Der echte Weg mit einem Laufwerk, nachgestellt: CD 1 einlesen,
    /// zwischenlagern, CD 2 dazu einlesen — in einen Temp-Ordner.</summary>
    private static void Voll(ContentSources.Source? discs)
    {
        var cd1 = discs?.Roots.Where(r => DiscCoverage.MissionsIn(new[] { r }).Contains(1)).ToList() ?? new();
        var cd2 = discs?.Roots.Where(r => DiscCoverage.MissionsIn(new[] { r }).Contains(16)).ToList() ?? new();
        if (cd1.Count == 0 || cd2.Count == 0)
        {
            Check(false, "voll: braucht CD 1 UND CD 2 in den Laufwerken");
            return;
        }
        string tmp = Path.Combine(Path.GetTempPath(), "aer_import_cd2_check");
        string ziel = Path.Combine(tmp, "data"), lager = Path.Combine(tmp, "lager");
        try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (Exception) { }
        Directory.CreateDirectory(ziel);
        GD.Print($"import-cd2-check: voll: Ziel {ziel} (user://data bleibt unberuehrt)");
        var t0 = DateTime.Now;
        try
        {
            var src1 = Of(cd1, "nur CD 1");
            var b1 = new ContentBuilder(src1, ziel);
            int lastPct = -1;
            bool ok1 = b1.Run(_ =>
            {
                int pct = b1.Schritte > 0 ? 100 * b1.Schritt / b1.Schritte : 0;
                if (pct / 10 != lastPct / 10) GD.Print($"import-cd2-check: voll: Lauf 1 Schritt {b1.Schritt}/{b1.Schritte}");
                lastPct = pct;
            });
            var miss1 = DiscCoverage.Missing(b1.MissionNumbers);
            Check(ok1 && b1.MissionNumbers.SequenceEqual(Enumerable.Range(1, 15)),
                  $"voll: Lauf 1 (nur CD 1) baeckt Missionen {Set(b1.MissionNumbers)}, Schritt {b1.Schritt}/{b1.Schritte}");
            Check(CampaignCount(ziel) == 15, $"voll: campaign.json nach Lauf 1 hat {CampaignCount(ziel)} Eintraege (erwartet 15)");
            Check(miss1.SequenceEqual(Enumerable.Range(16, 18)), $"voll: danach fehlen {Set(miss1)} -> Frage nach CD 2");

            long mb = 0;
            var staged = DiscCoverage.Stage(src1, lager, (_, d, _) => mb = d >> 20);
            Check(staged.Roots.All(r => r.StartsWith(lager)) && staged.Cabinet != null && File.Exists(staged.Cabinet),
                  $"voll: Zwischenlager {mb} MB, Kabinett {staged.Cabinet}");

            var u = DiscCoverage.Useful(Of(cd2, "CD 2"), miss1)!;
            var b2 = new ContentBuilder(DiscCoverage.Merge(staged, u), ziel);
            bool ok2 = b2.Run();
            Check(ok2 && b2.MissionNumbers.Count == 33, $"voll: Lauf 2 (Lager + CD 2) baeckt {b2.MissionNumbers.Count} Missionen");
            Check(CampaignCount(ziel) == 33, $"voll: campaign.json nach Lauf 2 hat {CampaignCount(ziel)} Eintraege (erwartet 33)");
            int maps = Enumerable.Range(1, 33).Count(i => File.Exists($"{ziel}/Maps/map_{i:00}.entities.json"));
            Check(maps == 33, $"voll: {maps}/33 Karten map_NN.entities.json im Ziel");
            Check(b2.SpriteFrames >= b1.SpriteFrames && b2.SpriteFrames > 0,
                  $"voll: Einheitenbilder Lauf 1 {b1.SpriteFrames}, Lauf 2 {b2.SpriteFrames} (Lauf 2 kennt die Truppen von 16–33)");
            GD.Print($"import-cd2-check: voll: Dauer {(DateTime.Now - t0).TotalSeconds:0} s");
        }
        catch (Exception e) { Check(false, "voll: " + e.Message); }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (Exception) { GD.Print($"import-cd2-check: {tmp} bitte von Hand loeschen"); }
        }
    }

    private static int CampaignCount(string ziel)
    {
        string p = ziel + "/Maps/campaign.json";
        if (!File.Exists(p)) return 0;
        var json = new Json();
        if (json.Parse(File.ReadAllText(p)) != Error.Ok) return -1;
        var d = json.Data.AsGodotDictionary<string, Variant>();
        return d.TryGetValue("missions", out var mv) ? mv.AsGodotArray().Count : 0;
    }
}
