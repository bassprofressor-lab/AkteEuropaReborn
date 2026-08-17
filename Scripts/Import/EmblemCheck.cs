namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// <b>PRÜFSTAND <c>--emblem-check</c></b> — misst das Emblem an seinen zwei
/// Behauptungen, nicht an seinem Vorhandensein.
///
/// <para>Lauf (der Prüfstand steigt im echten Briefingschirm ein, siehe
/// <c>UI/BriefingEmblem.cs</c>; er baut sich also keinen eigenen):</para>
/// <code>
///   Godot_..._console.exe --path . --headless -- --campaign=1 --emblem-check
/// </code>
///
/// <para><b>Was er misst und woran er scheitern kann:</b></para>
/// <list type="number">
///   <item><b>Die Bank rechnet auf.</b> Die 29 Grössen aus dem Lader müssen
///   sich auf 43.302 summieren — den Schwanz von BRIEFG.DAT. Eine falsch
///   abgeschriebene Zeile der Tabelle fällt hier sofort auf, weil die Summe
///   dann nicht mehr aufgeht.</item>
///
///   <item><b>Der Schimmer LÄUFT, und er läuft nach rechts.</b> Über die neun
///   Nischenbilder muss der x-Schwerpunkt der hellen Punkte <b>streng monoton
///   wachsen</b> und dabei mindestens 25 Punkte zurücklegen. Gemessen sind
///   9,4 → 40,0 bei 57 Punkten Breite. Ein Prüfstand, der nur zählt, ob neun
///   Bilder da sind, würde neunmal dasselbe Bild durchgehen lassen.</item>
///
///   <item><b>Die zwei Nischen sind SPIEGELBILDER.</b> An jedem der 16
///   Zählerstände muss <c>links + rechts == 8</c> sein, die Bildnummer darf nie
///   um mehr als eins springen, und links müssen alle neun Bilder vorkommen.
///   Das prüft die Phasenrechnung des Originals (<c>|c&amp;15-8|</c> gegen
///   <c>|((c+8)&amp;15)-8|</c>); ein Vorzeichenfehler darin bricht die Summe
///   sofort.
///
///   <para>⚠ Hier stand zuerst »links und rechts sind nie gleich«, und dieser
///   Prüfstand hat das beim ersten Lauf widerlegt: an den Ständen 4 und 12
///   steht in beiden Nischen Bild 4. Die zwei Bahnen kreuzen sich in der Mitte
///   — die Spiegelsumme ist die Aussage, die wirklich gilt.</para></item>
///
///   <item><b>Das Wasserzeichen DREHT SICH.</b> Von den neun Bildern muss das
///   fünfte (Index 4) das schmalste sein — die Kante — und mindestens fünfmal
///   schmaler als das erste und das letzte. Gemessen: 13 Spalten gegen 232 und
///   230. Neun Kopien desselben Bildes kämen hier nicht durch.</item>
///
///   <item><b>Das Fenster ist ein Fenster.</b> <c>MarkFrame</c> muss für genau
///   neun Zählerstände ein Bild liefern und sonst −1 — sonst dreht sich das
///   Wasserzeichen endlos, statt einmal.</item>
/// </list>
///
/// <para>⚠ Er misst an den <b>eingespielten PNG</b> und ruft für die Phase die
/// Rechnung auf, die auch der Zeichner benutzt
/// (<see cref="BriefingExporter.NicheFrame"/>). Er bildet nichts davon nach:
/// eine zweite Abschrift der Formel würde sich mit der ersten irren.</para>
/// </summary>
public static class EmblemCheck
{
    /// <summary>Rückgabe 0 heisst bestanden, sonst die Zahl der Beanstandungen
    /// (gedeckelt, damit der Rückgabewert in ein Byte passt).</summary>
    public static int Run(Action<string> say)
    {
        var bad = new List<string>();
        say("emblem-check: das Emblem von Akte Europa");

        // ---- 1. die Bank rechnet auf ----------------------------------------
        int summe = BriefingExporter.BankBytes();
        const int schwanz = 350502 - BriefingExporter.BackdropW * BriefingExporter.BackdropH;
        say($"  Bank: {BriefingExporter.Bank.Length} Eintraege, Flaechensumme {summe}, " +
            $"Schwanz von BRIEFG.DAT {schwanz}");
        if (BriefingExporter.Bank.Length != 29)
            bad.Add($"Bank hat {BriefingExporter.Bank.Length} Eintraege statt 29");
        if (summe != schwanz)
            bad.Add($"Bank summiert {summe}, der Schwanz ist {schwanz} — die Tabelle stimmt nicht");
        int embAt = BriefingExporter.BankOffset(BriefingExporter.EmblemFirst);
        say($"  Emblem beginnt bei Versatz {embAt} (erwartet 329982)");
        if (embAt != 329982) bad.Add($"Emblem-Versatz {embAt} statt 329982");

        // ---- 2. der Schimmer laeuft nach rechts ------------------------------
        var niche = Laden("UI/emblem/niche", BriefingExporter.EmblemFrames,
                          BriefingExporter.EmblemW, BriefingExporter.EmblemH, bad, "Nische");
        if (niche != null)
        {
            double vor = double.NegativeInfinity, erst = 0, letzt = 0;
            int stufen = 0;
            var bahn = new List<string>();
            for (int f = 0; f < niche.Count; f++)
            {
                double c = HellSchwerpunkt(niche[f], out int hell);
                bahn.Add($"f{f}={c:0.0}({hell})");
                if (double.IsNaN(c)) { bad.Add($"Nischenbild {f} hat keinen hellen Punkt"); continue; }
                if (f == 0) erst = c; else if (c > vor) stufen++;
                letzt = c;
                vor = c;
            }
            say("  Schimmer, x-Schwerpunkt der hellen Punkte (Anzahl): " + string.Join(" ", bahn));
            if (stufen != niche.Count - 1)
                bad.Add($"der Schimmer waechst nur an {stufen} von {niche.Count - 1} Stellen — " +
                        "er laeuft nicht durch");
            if (letzt - erst < 25)
                bad.Add($"der Schimmer legt nur {letzt - erst:0.0} Punkte zurueck, " +
                        "erwartet mindestens 25");
        }

        // ---- 3. die zwei Nischen sind Spiegelbilder --------------------------
        //
        // ⚠⚠ HIER STAND EINE FALSCHE BEHAUPTUNG VON MIR, und dieser Prüfstand
        // hat sie beim ersten Lauf gefangen: »die zwei Nischen zeigen nie
        // dasselbe Bild«. Sie tun es doch, an zwei von sechzehn Ständen (4 und
        // 12) — dort steht in beiden Bild 4, die Mitte. Das ist keine Panne der
        // Rechnung, das ist unvermeidlich: die zwei Bahnen kreuzen sich.
        //
        // Die richtige, härtere Aussage steht in der gemessenen Bahn selbst:
        //   links   8 7 6 5 4 3 2 1 0 1 2 3 4 5 6 7
        //   rechts  0 1 2 3 4 5 6 7 8 7 6 5 4 3 2 1
        // Die SUMME ist an jedem Stand **8**. Rechts ist also nicht »um 8
        // versetzt«, sondern das exakte Spiegelbild von links — und genau das
        // ist, was man sieht: der Schimmer läuft in den zwei Nischen
        // gegeneinander und trifft sich in der Mitte.
        var gesehen = new HashSet<int>();
        int nichtGespiegelt = 0, gesprungen = 0;
        var phase = new List<string>();
        int vorL = BriefingExporter.NicheFrame(15, right: false);
        for (int c = 0; c < 16; c++)
        {
            int l = BriefingExporter.NicheFrame(c, right: false);
            int r = BriefingExporter.NicheFrame(c, right: true);
            gesehen.Add(l);
            if (l + r != BriefingExporter.EmblemFrames - 1) nichtGespiegelt++;
            if (Math.Abs(l - vorL) != 1) gesprungen++;
            vorL = l;
            phase.Add($"{l}/{r}");
        }
        say("  Phase links/rechts ueber 16 Zaehlerstaende: " + string.Join(" ", phase));
        if (gesehen.Count != BriefingExporter.EmblemFrames)
            bad.Add($"links kommen nur {gesehen.Count} der {BriefingExporter.EmblemFrames} " +
                    "Bilder vor");
        if (nichtGespiegelt != 0)
            bad.Add($"an {nichtGespiegelt} Zaehlerstaenden ist links+rechts nicht " +
                    $"{BriefingExporter.EmblemFrames - 1} — die zwei Nischen laufen nicht gegeneinander");
        if (gesprungen != 0)
            bad.Add($"die Bildnummer springt an {gesprungen} Stellen um mehr als eins — " +
                    "das ist kein Hin und Her, das ist ein Sprung");

        // ---- 4. das Wasserzeichen dreht sich ---------------------------------
        var mark = Laden("UI/emblem/mark", BriefingExporter.MarkFrames,
                         BriefingExporter.MarkW, BriefingExporter.MarkH, bad, "Wasserzeichen");
        if (mark != null)
        {
            var breit = new int[mark.Count];
            for (int f = 0; f < mark.Count; f++) breit[f] = BelegteBreite(mark[f]);
            say("  Wasserzeichen, belegte Breite je Bild: " + string.Join(" ", breit));
            int schmal = 0;
            for (int f = 1; f < breit.Length; f++) if (breit[f] < breit[schmal]) schmal = f;
            if (schmal != 4)
                bad.Add($"das schmalste Bild ist {schmal}, erwartet 4 (die Kante der Drehung)");
            else if (breit[4] * 5 > breit[0] || breit[4] * 5 > breit[^1])
                bad.Add($"die Kante ist {breit[4]} breit gegen {breit[0]} und {breit[^1]} — " +
                        "das dreht sich nicht, das steht");
        }

        // ---- 5. das Fenster ist ein Fenster ----------------------------------
        int im = 0;
        for (int c = 0; c < 64; c++) if (BriefingExporter.MarkFrame(c) >= 0) im++;
        say($"  Wasserzeichen-Fenster: {im} von 64 Zaehlerstaenden zeichnen (erwartet 9)");
        if (im != BriefingExporter.MarkFrames)
            bad.Add($"das Fenster deckt {im} Staende statt {BriefingExporter.MarkFrames}");

        foreach (string b in bad) say("  FEHLER: " + b);
        say(bad.Count == 0
            ? "emblem-check: bestanden"
            : $"emblem-check: {bad.Count} Beanstandung(en)");
        return Math.Min(bad.Count, 120);
    }

    /// <summary>Der x-Schwerpunkt der hellen Punkte eines Bildes. »Hell« ist
    /// Mittelwert der drei Kanäle über 140 von 255 — die Schwelle trennt den
    /// Schimmer (weiss bis hellblau) vom Emblem selbst (Index 129..131, also
    /// höchstens 72).</summary>
    private static double HellSchwerpunkt(Image img, out int hell)
    {
        double sum = 0;
        hell = 0;
        for (int y = 0; y < img.GetHeight(); y++)
            for (int x = 0; x < img.GetWidth(); x++)
            {
                var c = img.GetPixel(x, y);
                if ((c.R + c.G + c.B) / 3f * 255f <= 140f) continue;
                sum += x;
                hell++;
            }
        return hell == 0 ? double.NaN : sum / hell;
    }

    /// <summary>Wie viele Spalten das Bild belegt: von der ersten bis zur
    /// letzten Spalte, die etwas anderes als den Grund enthält. Der Grund ist
    /// die Farbe der linken oberen Ecke — bei allen neun Bildern
    /// Palettenindex 47.</summary>
    private static int BelegteBreite(Image img)
    {
        var grund = img.GetPixel(0, 0);
        int lo = -1, hi = -1;
        for (int x = 0; x < img.GetWidth(); x++)
            for (int y = 0; y < img.GetHeight(); y++)
                if (img.GetPixel(x, y) != grund)
                {
                    if (lo < 0) lo = x;
                    hi = x;
                    break;
                }
        return lo < 0 ? 0 : hi - lo + 1;
    }

    /// <summary>Einen Bildersatz laden und gleich auf seine Masse prüfen. Null,
    /// wenn er fehlt oder unvollständig ist — dann steht der Grund schon in
    /// <paramref name="bad"/>.</summary>
    private static List<Image>? Laden(string dir, int count, int w, int h,
                                      List<string> bad, string was)
    {
        var list = new List<Image>();
        for (int f = 0; f < count; f++)
        {
            string p = Core.Content.Path($"{dir}/f{f}.png");
            if (!FileAccess.FileExists(p))
            {
                bad.Add($"{was}: {p} fehlt — ist der Inhalt neu eingespielt?");
                return null;
            }
            var im = Image.LoadFromFile(ProjectSettings.GlobalizePath(p));
            if (im == null) { bad.Add($"{was}: {p} nicht lesbar"); return null; }
            if (im.GetWidth() != w || im.GetHeight() != h)
            {
                bad.Add($"{was} Bild {f}: {im.GetWidth()}x{im.GetHeight()} statt {w}x{h}");
                return null;
            }
            list.Add(im);
        }
        return list;
    }
}
