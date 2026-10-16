namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

/// <summary>
/// <c>--inhaltsstand-check</c> — rechnet den Pruefer des Inhaltsstands
/// (<see cref="InhaltsStand"/>) nach, kopflos und selbstbeendend.
///
/// <para>⚠⚠ Er arbeitet AUSSCHLIESSLICH in einem eigenen Ordner unter dem
/// Temp-Verzeichnis und bricht ab, bevor er irgendetwas schreibt, wenn dieser
/// Ordner mit <c>user://data</c> zusammenfaellt oder darin liegt. Die
/// eingelesenen Daten des Spielers fasst er nicht an — auch nicht lesend.</para>
///
/// <para>Die Faelle (Zahl je Fall = wie viele Teile veraltet sein MUESSEN):</para>
/// <list type="bullet">
/// <item>(a) kein Stempel, alle Merkdateien alt (01.01.2026) -> alle Teile mit
///   Stichtag (10 von 11; die Kachelsaetze haben keinen, siehe dort);</item>
/// <item>(a2) kein Stempel, Merkdateien jung -> nur die Teile mit noetigem Stand
///   ueber dem von 0.6.9 (heute: keiner) — die Rueckdatierung;</item>
/// <item>(a3) dasselbe streng (<c>--inhaltsstand-streng</c>) -> alle;</item>
/// <item>(a4) kein Stempel, nur die Briefing-Merkdatei alt -> genau »briefings«
///   (so steht die echte Installation des Entwicklers: Vollimport 12.10.,
///   Briefing-Knoepfe am 13.10. dazugekommen);</item>
/// <item>(b) Stempel aktuell -> keiner;</item>
/// <item>(c) ein Teil in der Tafel erhoeht (Karten +1) -> genau »karten«;</item>
/// <item>(c2) kaputte Stempeldatei -> gilt als fehlend, nicht als aktuell;</item>
/// <item>(d) zwei Teile zurueckgesetzt (hilfe, briefings), dann ueber DIESELBEN
///   Wege wie der Startschirm (<see cref="InhaltsStand.Teil.Weg"/>) aus einer
///   gefundenen Quelle neu eingelesen -> keiner. Ohne Quelle nur die
///   Stempellogik.</item>
/// </list>
/// <para>Nullmodell: <c>--inhaltsstand-aus</c> — dann erkennt der Pruefer
/// nichts, und (a), (a3), (a4), (c), (c2) und der erste Teil von (d) scheitern.</para>
/// </summary>
public static class InhaltsStandCheck
{
    public static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), $"aer_inhaltsstand_check_{System.Environment.ProcessId}")
                          .Replace('\\', '/');
        string echt = Path.GetFullPath(ProjectSettings.GlobalizePath(Core.Content.UserRoot)).TrimEnd('/', '\\');
        string voll = Path.GetFullPath(root).TrimEnd('/', '\\');
        if (voll.StartsWith(echt, StringComparison.OrdinalIgnoreCase)
            || echt.StartsWith(voll, StringComparison.OrdinalIgnoreCase))
        {
            GD.PrintErr($"inhaltsstand-check: ABBRUCH — der Pruefordner {voll} beruehrt user://data ({echt})");
            return 3;
        }
        InhaltsStand.VersionMerken();
        GD.Print($"inhaltsstand-check: Pruefordner {root} (user://data bleibt unberuehrt), " +
                 $"Pruefer {(InhaltsStand.Aus ? "AUS (Nullmodell)" : "an")}");
        int fehler = 0, faelle = 0;
        void Fall(string name, List<InhaltsStand.Teil> ist, IEnumerable<string> soll)
        {
            faelle++;
            var a = ist.Select(t => t.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var b = soll.OrderBy(x => x, StringComparer.Ordinal).ToList();
            bool ok = a.SequenceEqual(b);
            if (!ok) fehler++;
            GD.Print($"inhaltsstand-check: ({name}) veraltet {a.Count} [{string.Join(", ", a)}], " +
                     $"Soll {b.Count} [{string.Join(", ", b)}] -> {(ok ? "OK" : "FALSCH")}");
        }

        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            var dateien = MerkdateienAnlegen(root);
            var alle = InhaltsStand.Teile.Select(t => t.Name).ToList();
            var ueber069 = InhaltsStand.Teile
                .Where(t => InhaltsStand.Noetig.TryGetValue(t.Name, out int n) && n > InhaltsStand.StandVon069)
                .Select(t => t.Name).ToList();
            var alt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var jung = DateTime.UtcNow;

            // (a) kein Stempel, alles alt
            foreach (string f in dateien) File.SetLastWriteTimeUtc(f, alt);
            // ⚠ ohne die Teile ohne Stichtag (Kachelsaetze: eine Kopie traegt die
            // Dateizeit von der CD, 1997) — die gelten als da, sobald sie da sind
            var mitStichtag = InhaltsStand.Teile.Where(t => t.Stichtag != null).Select(t => t.Name).ToList();
            Fall("a ohne Stempel, alte Dateien", InhaltsStand.Veraltet(root, streng: false), mitStichtag);

            // (a2) kein Stempel, alles jung -> Rueckdatierung
            foreach (string f in dateien) File.SetLastWriteTimeUtc(f, jung);
            Fall("a2 ohne Stempel, junge Dateien, Rueckdatierung", InhaltsStand.Veraltet(root, streng: false), ueber069);

            // (a3) dasselbe streng
            Fall("a3 ohne Stempel, streng", InhaltsStand.Veraltet(root, streng: true), alle);

            // (a4) nur die Briefings alt
            var br = InhaltsStand.TeilNach("briefings")!;
            foreach (string m in br.Merk) File.SetLastWriteTimeUtc(Path.Combine(root, m), alt);
            Fall("a4 ohne Stempel, nur Briefings alt", InhaltsStand.Veraltet(root, streng: false),
                 ueber069.Union(new[] { "briefings" }));
            foreach (string m in br.Merk) File.SetLastWriteTimeUtc(Path.Combine(root, m), jung);

            // (b) Stempel aktuell
            InhaltsStand.Erreicht(root, alle, null);
            Fall("b Stempel aktuell", InhaltsStand.Veraltet(root), Array.Empty<string>());

            // (c) ein Teil in der Tafel erhoeht
            var hoeher = new Dictionary<string, int>(InhaltsStand.Noetig);
            hoeher["karten"]++;
            Fall("c Karten in der Tafel +1", InhaltsStand.Veraltet(root, hoeher), new[] { "karten" });

            // (c2) kaputte Datei — mit ALTEN Merkdateien, damit auch die
            // Rueckdatierung nichts gutschreibt
            string stempel = InhaltsStand.Pfad(root);
            string heil = File.ReadAllText(stempel);
            File.WriteAllText(stempel, "{ kaputt");
            foreach (string f in dateien) File.SetLastWriteTimeUtc(f, alt);
            Fall("c2 kaputte Stempeldatei", InhaltsStand.Veraltet(root, streng: false), mitStichtag);
            File.WriteAllText(stempel, heil);
            foreach (string f in dateien) File.SetLastWriteTimeUtc(f, jung);

            // (d) zwei Teile zurueck, dann ueber die Wege des Startschirms neu
            var n = JsonNode.Parse(File.ReadAllText(stempel))!.AsObject();
            n["teile"]!["hilfe"] = 0;
            n["teile"]!["briefings"] = 0;
            File.WriteAllText(stempel, n.ToJsonString());
            var veraltet = InhaltsStand.Veraltet(root);
            Fall("d1 hilfe und briefings zurueckgesetzt", veraltet, new[] { "hilfe", "briefings" });

            var src = InhaltsStand.QuelleFinden(root);
            if (src == null || !src.Complete)
            {
                GD.Print("inhaltsstand-check: (d2) keine Quelle mit GAME.EXE gefunden — nur die Stempellogik");
                InhaltsStand.Erreicht(root, veraltet.Select(t => t.Name), null);
            }
            else
            {
                GD.Print($"inhaltsstand-check: (d2) Quelle {src.Label} (exe {src.Exe ?? "-"}, kabinett {src.Cabinet ?? "-"})");
                long vorher = new FileInfo(Path.Combine(root, "UI/help.json")).Length;
                foreach (var t in veraltet)
                {
                    bool ok = t.Weg(new ContentBuilder(src, root), null);
                    GD.Print($"inhaltsstand-check: (d2) Teil {t.Name} neu eingelesen -> {(ok ? "gelungen" : "FEHLGESCHLAGEN")}");
                }
                long nachher = new FileInfo(Path.Combine(root, "UI/help.json")).Length;
                long brief = File.Exists(Path.Combine(root, "Maps/briefings.json"))
                    ? new FileInfo(Path.Combine(root, "Maps/briefings.json")).Length : 0;
                GD.Print($"inhaltsstand-check: (d2) UI/help.json {vorher} -> {nachher} Byte, Maps/briefings.json {brief} Byte");
                faelle++;
                if (nachher <= vorher || brief <= 2) { fehler++; GD.Print("inhaltsstand-check: (d2) die Dateien wurden NICHT geschrieben -> FALSCH"); }
            }
            Fall("d3 nach dem Neu-Einlesen", InhaltsStand.Veraltet(root), Array.Empty<string>());
            GD.Print("inhaltsstand-check: Stempel danach: " + File.ReadAllText(stempel).Replace("\r", "").Replace("\n", " "));
        }
        catch (Exception e)
        {
            fehler++;
            GD.PrintErr("inhaltsstand-check: Ausnahme " + e);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception) { }
        }
        GD.Print($"inhaltsstand-check: {(fehler == 0 ? "DURCH" : "NICHT DURCH")} — {faelle - fehler}/{faelle} Faelle richtig" +
                 (InhaltsStand.Aus ? " (Nullmodell --inhaltsstand-aus: muss NICHT DURCH sein)" : ""));
        return fehler == 0 ? 0 : 1;
    }

    /// <summary>Je Teil seine Merkdateien (ein <c>*</c> wird zu <c>01</c>) mit
    /// einem kleinen Inhalt, dazu der Kanarienvogel.</summary>
    private static List<string> MerkdateienAnlegen(string root)
    {
        var list = new List<string>();
        foreach (var t in InhaltsStand.Teile)
            foreach (string m in t.Merk)
            {
                string p = Path.Combine(root, m.Replace("*", "01"));
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                if (!File.Exists(p)) File.WriteAllText(p, "{}");
                list.Add(p);
            }
        return list;
    }
}
