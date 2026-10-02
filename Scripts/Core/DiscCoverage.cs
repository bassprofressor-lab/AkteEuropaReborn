namespace AkteEuropaReborn.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Welche Kampagnenmissionen eine Quelle traegt, welche nach dem Einlesen
/// fehlen und auf welcher CD sie liegen — die Rechnung hinter »Bitte CD 2
/// einlegen« (bug-379).
///
/// <para>⚠ <b>Der Anlass, gemeldet von einem Spieler:</b> »Wenn man von CD 1
/// installiert, fragt der Installer nie nach CD 2, also fehlen die Hälfte der
/// Missionen.« Das stimmte: <see cref="ContentSources.Discs"/> nimmt, was
/// GERADE in den Laufwerken steckt, und bei einem einzigen Laufwerk ist das
/// CD 1. Der Import las die Missionen 1–15, schrieb eine campaign.json mit
/// fuenfzehn Eintraegen, und <c>CampaignManager.Next</c> uebersprang den Rest
/// wortlos (»someone with only disc 1 gets 1 to 15«). Gemerkt hat es niemand.</para>
///
/// <para><b>Was auf welcher CD liegt — nachgesehen am 01.10.2026 an den
/// echten Scheiben</b> (D: und E: dieses Rechners):</para>
/// <list type="bullet">
/// <item>CD 1 (<c>CW.ID</c> = 0x00): <c>LEVELS\01..15.CWM</c>, die dreizehn
/// <c>.DM</c>, <c>NET01..08</c>; <c>DATA1.CAB</c> mit GAME.EXE, ROBO.CWR und
/// SOUNDS.CWN; die Kachelsaetze 01–15, 21, 25, 26, 40–47 und — nur hier —
/// <c>DATA\01.PAL</c>, die Palette der Einheitengrafiken; Filme 1–15, 34, INTRO.</item>
/// <item>CD 2 (<c>CW.ID</c> = 0x01): <c>LEVELS\16..33.CWM</c>, dieselben
/// <c>.DM</c> und NET-Karten, die Kachelsaetze 16–33 (und einige von CD 1
/// noch einmal), KEIN Kabinett, KEINE 01.PAL; Filme 16–34.</item>
/// </list>
///
/// <para>⚠ Daraus folgt der Bauplan: CD 2 ALLEIN kann nicht nachimportiert
/// werden. Ohne Kabinett und ohne 01.PAL entstehen keine Einheitengrafiken fuer
/// die Truppen der Karten 16–33, und campaign.json, Gebaeude- und
/// Einheitenkatalog werden aus den Karten DIESES Laufs gezaehlt — ein Lauf nur
/// ueber CD 2 wuerde die fuenfzehn ersten Missionen aus der Liste werfen.
/// Deshalb wird CD 1 vor dem Wechsel <b>zwischengelagert</b>
/// (<see cref="Stage"/>, ~160 MB ohne Filme), und der zweite Lauf ist derselbe
/// volle Import wie mit beiden CDs in zwei Laufwerken.</para>
///
/// <para>⚠ UNSERE SETZUNG: das Original fragt beim Spielen nach der CD, nicht
/// beim Installieren — es liest die Karten zur Laufzeit von der Scheibe. Wir
/// lesen sie einmal ein, also muss die Frage hierher.</para>
/// </summary>
public static class DiscCoverage
{
    /// <summary>Die Kampagne hat 33 Missionen (<c>01.CWM</c> .. <c>33.CWM</c>).</summary>
    public const int CampaignCount = 33;

    /// <summary>Die erste Mission, die nur auf CD 2 liegt — gezaehlt an den
    /// LEVELS beider Scheiben, siehe oben.</summary>
    public const int FirstOnDisc2 = 16;

    /// <summary>Auf welcher CD eine Mission liegt.</summary>
    public static int DiscOf(int mission) => mission >= FirstOnDisc2 ? 2 : 1;

    /// <summary>Die Missionsnummern, die eine Quelle als <c>NN.CWM</c> traegt —
    /// dieselbe Regel wie <c>ContentBuilder.BakeOne</c>: numerischer Stamm, nur
    /// <c>.CWM</c> (ein <c>1.DM</c> ist ein Spielstand, keine Mission 1).</summary>
    public static SortedSet<int> MissionsIn(IEnumerable<string> roots)
    {
        var set = new SortedSet<int>();
        foreach (string r in roots)
        {
            string dir = r.TrimEnd('/', '\\') + "/LEVELS";
            if (!Directory.Exists(dir)) continue;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.CWM"); }
            catch (Exception) { continue; }
            foreach (string p in files)
                if (int.TryParse(Path.GetFileNameWithoutExtension(p), out int n)
                    && n >= 1 && n <= CampaignCount)
                    set.Add(n);
        }
        return set;
    }

    public static SortedSet<int> MissionsIn(ContentSources.Source src) => MissionsIn(src.Roots);

    /// <summary>1..33 ohne die vorhandenen.</summary>
    public static List<int> Missing(IEnumerable<int> have)
    {
        var h = new HashSet<int>(have);
        var list = new List<int>();
        for (int i = 1; i <= CampaignCount; i++)
            if (!h.Contains(i)) list.Add(i);
        return list;
    }

    /// <summary>Was in der eingelesenen campaign.json fehlt — die Rechnung fuer
    /// das Kampagnenmenue, NACHDEM jemand »Ohne CD 2 fortfahren« gewaehlt hat.
    /// Leer, wenn noch gar nichts eingelesen ist: dann zeigt das Menue ohnehin
    /// »keine Missionen«, und eine Liste von 33 Fehlenden waere nur Laerm.</summary>
    public static List<int> MissingImported()
    {
        var ms = Campaign.CampaignManager.Missions;
        if (ms.Count == 0) return new List<int>();
        return Missing(ms.Select(m => m.Index));
    }

    /// <summary>Die CDs, deren Missionen fehlen, z. B. {2}.</summary>
    public static SortedSet<int> DiscsFor(IEnumerable<int> missing)
        => new(missing.Select(DiscOf));

    /// <summary>»16–33« oder »3, 7–9« — fuer Menschen, nicht fuer Rechner.</summary>
    public static string Ranges(IEnumerable<int> nums)
    {
        var l = nums.OrderBy(x => x).ToList();
        var parts = new List<string>();
        for (int i = 0; i < l.Count;)
        {
            int j = i;
            while (j + 1 < l.Count && l[j + 1] == l[j] + 1) j++;
            parts.Add(j == i ? $"{l[i]}" : j == i + 1 ? $"{l[i]}, {l[j]}" : $"{l[i]}–{l[j]}");
            i = j + 1;
        }
        return string.Join(", ", parts);
    }

    /// <summary>Ein Satz fuer Import- und Kampagnenschirm: »Es fehlen die
    /// Missionen 16–33 (CD 2).«</summary>
    public static string Explain(IReadOnlyCollection<int> missing)
    {
        if (missing.Count == 0) return "Alle 33 Missionen sind eingelesen.";
        string cds = string.Join(" und ", DiscsFor(missing).Select(d => $"CD {d}"));
        return missing.Count == 1
            ? $"Es fehlt Mission {Ranges(missing)} — sie liegt auf {cds}."
            : $"Es fehlen die Missionen {Ranges(missing)} — sie liegen auf {cds}.";
    }

    /// <summary>Welche CD eine Wurzel ist, nach <c>CW.ID</c> (ein Byte: 0 = CD 1,
    /// 1 = CD 2, gelesen an beiden Scheiben). -1, wenn die Datei fehlt — ein
    /// kopierter Ordner muss sie nicht mitgenommen haben, dann entscheiden die
    /// LEVELS.</summary>
    public static int DiscId(string root)
    {
        try
        {
            string p = root.TrimEnd('/', '\\') + "/CW.ID";
            if (!File.Exists(p)) return -1;
            using var f = File.OpenRead(p);
            int b = f.ReadByte();
            return b is 0 or 1 ? b + 1 : -1;
        }
        catch (Exception) { return -1; }
    }

    /// <summary>Liegt diese Wurzel auf einem Wechseldatentraeger? Nur solche
    /// werden zwischengelagert — ein Ordner auf der Platte (kopierte CD,
    /// Installation) ist beim zweiten Lauf ja noch da.</summary>
    public static bool IsRemovable(string root)
    {
        try
        {
            string? r = Path.GetPathRoot(Path.GetFullPath(root));
            if (string.IsNullOrEmpty(r)) return false;
            var d = new DriveInfo(r);
            return d.DriveType is DriveType.CDRom or DriveType.Removable;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Die Wechseldatentraeger einer Quelle auf die Platte kopieren, damit sie
    /// nach dem CD-Wechsel noch lesbar sind. Mitgenommen werden <c>DATA\</c>,
    /// <c>LEVELS\</c> und die losen Dateien der Wurzel (darunter DATA1.CAB und
    /// CW.ID); <c>MOVIES\</c> NICHT — die Filme spielt <c>MoviePlayer</c> zur
    /// Laufzeit von der Scheibe, und sie sind 400 MB.
    ///
    /// <para>Rueckgabe: dieselbe Quelle, die Wechselwurzeln ersetzt durch ihre
    /// Kopie unter <paramref name="stagingDir"/>. Ordner auf der Platte bleiben,
    /// wie sie sind.</para>
    /// </summary>
    public static ContentSources.Source Stage(ContentSources.Source src, string stagingDir,
                                              Action<string, long, long>? progress = null)
    {
        var res = new ContentSources.Source
        {
            Kind = src.Kind, Label = src.Label, Exe = src.Exe, Cabinet = src.Cabinet,
        };
        // erst die Groesse, damit der Balken ein Ende kennt
        var jobs = new List<(string From, string To)>();
        int k = 0;
        foreach (string root in src.Roots)
        {
            if (!IsRemovable(root)) { res.Roots.Add(root); continue; }
            string dst = $"{stagingDir.TrimEnd('/', '\\')}/cd{++k}";
            string r = root.TrimEnd('/', '\\');
            foreach (string f in SafeFiles(r)) jobs.Add((f, dst + "/" + Path.GetFileName(f)));
            foreach (string sub in new[] { "DATA", "LEVELS" })
                foreach (string f in SafeFiles(r + "/" + sub))
                    jobs.Add((f, $"{dst}/{sub}/{Path.GetFileName(f)}"));
            res.Roots.Add(dst);
            // das Kabinett und die EXE zeigen jetzt auf die Kopie
            if (src.Cabinet != null && SameDir(Path.GetDirectoryName(src.Cabinet), r))
                res.Cabinet = dst + "/" + Path.GetFileName(src.Cabinet);
            if (src.Exe != null && SameDir(Path.GetDirectoryName(src.Exe), r))
                res.Exe = dst + "/" + Path.GetFileName(src.Exe);
        }
        long total = 0, done = 0;
        foreach (var j in jobs) total += new FileInfo(j.From).Length;
        foreach (var (from, to) in jobs)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            // schon da und gleich lang: nicht noch einmal von der CD ziehen
            var fi = new FileInfo(from);
            if (!(File.Exists(to) && new FileInfo(to).Length == fi.Length))
                File.Copy(from, to, true);
            done += fi.Length;
            progress?.Invoke(Path.GetFileName(from), done, total);
        }
        return res;
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir) : Array.Empty<string>(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    private static bool SameDir(string? a, string b)
        => a != null && string.Equals(a.TrimEnd('/', '\\').Replace('\\', '/'),
                                      b.TrimEnd('/', '\\').Replace('\\', '/'),
                                      StringComparison.OrdinalIgnoreCase);

    /// <summary>Zwei Quellen zu EINER: erst die alte (CD 1 bzw. ihre Kopie),
    /// dann die neue — die Suchreihenfolge von <c>ContentBuilder.Find</c>, »CD1
    /// answers for everything it has and CD2 fills in the rest«. Doppelte
    /// Wurzeln fallen heraus.</summary>
    public static ContentSources.Source Merge(ContentSources.Source a, ContentSources.Source b)
    {
        var res = new ContentSources.Source
        {
            Kind = a.Kind == ContentSources.Kind.Disc || b.Kind == ContentSources.Kind.Disc
                ? ContentSources.Kind.Disc : ContentSources.Kind.Installation,
            Label = a.Label + " + " + b.Label,
            Exe = a.Exe ?? b.Exe,
            Cabinet = a.Cabinet ?? b.Cabinet,
        };
        foreach (string r in a.Roots.Concat(b.Roots))
            if (!res.Roots.Any(x => SameDir(x, r))) res.Roots.Add(r);
        return res;
    }

    /// <summary>Die Wurzeln einer frisch gefundenen Quelle, die etwas von dem
    /// FEHLENDEN tragen. Leer heisst: das ist noch dieselbe CD (oder gar keine
    /// Spiel-CD) — dann darf »Weiter« nicht noch einmal alles einlesen.</summary>
    public static ContentSources.Source? Useful(ContentSources.Source? found,
                                               IReadOnlyCollection<int> missing)
    {
        if (found == null) return null;
        var keep = new ContentSources.Source
        {
            Kind = found.Kind, Label = found.Label, Exe = found.Exe, Cabinet = found.Cabinet,
        };
        foreach (string r in found.Roots)
            if (MissionsIn(new[] { r }).Overlaps(missing)) keep.Roots.Add(r);
        return keep.Roots.Count > 0 ? keep : null;
    }
}
