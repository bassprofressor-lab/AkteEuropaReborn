namespace AkteEuropaReborn.UI;

using Godot;

/// <summary>
/// <c>--rollbalken-check</c> — <b>der Rollbalken des Originals, gemessen statt
/// angesehen</b> (09.09.2026, gelesen aus <c>0x456FF0</c> und <c>0x457140</c>;
/// siehe <see cref="WindowChrome.PaintScrollbar"/> und
/// <see cref="WindowChrome.ScrollHit"/>).
///
/// <para>⚠ Gemessen wird <b>ohne Godot-Knoten</b>. Der Balken ist Rechnerei:
/// vier Kachelplätze und drei Trefferfelder. Ein Lauf, der erst ein Fenster
/// malen müsste, hinge an der Oberfläche statt an der Regel — genauso hält es
/// <see cref="WindowManagerCheck"/>.</para>
///
/// <para><b>Was hier geprüft wird, und warum gerade das:</b></para>
/// <list type="number">
///   <item>⭐ <b>Die drei Trefferfelder stossen lückenlos aneinander und decken
///   genau die Griffbahn.</b> Das ist der Satz, an dem die ganze Lesung hängt:
///   die Kappen (Kachel 0x3C und 0x3F) werden GEMALT, sind aber NICHT klickbar
///   — klickbar ist genau der Streifen, den der Griff durchlaufen kann. Wer vom
///   Bild auf den Klick schliesst, legt die Pfeile auf die Kappen und bekommt
///   eine Lücke. Genau das ist das Nullmodell.</item>
///   <item><b>Der Griff schliesst unten bündig ab.</b> Bei Stand = Anzahl−1
///   muss seine Unterkante auf der Oberkante der unteren Kappe liegen — sonst
///   stimmt die Bahnlänge <c>(Kacheln−3)·20</c> nicht.</item>
///   <item><b>Die Enden der Bahnrechnung.</b> Der oberste Punkt der Bahn gibt
///   0, der unterste Anzahl−1 — kein Ausreisser über den Rand.</item>
///   <item><b>Beide Masse des Originals</b>, 11 Kacheln (Art 22 und 27) und
///   9 Kacheln (Art 29).</item>
/// </list>
///
/// <para>Das Nullmodell ist <c>--rollbalken-alt</c>: damit MUSS Punkt 1
/// durchfallen und die Zeile die Lücke nennen.</para>
/// </summary>
public static class WindowChromeCheck
{
    public static string RollbalkenLauf()
    {
        var sb = new System.Text.StringBuilder("rollbalken-check\n");
        bool alt = WindowChrome.RollbalkenAlt;
        sb.Append($"  Nullmodell --rollbalken-alt: {alt}\n");

        bool ok = true;
        // Die zwei Masse, die im Original wirklich vorkommen: Art 22 (x=600,
        // y=40, 11 Kacheln @0x4770F3) und Art 27 (x=580, y=40, 11) teilen sich
        // die 11; Art 29 hat x=180, y=20, 9 Kacheln (@0x47CBFD).
        ok &= Fall(sb, "Art 22 (Einheitenliste)", 600, 40, 11, 25);
        ok &= Fall(sb, "Art 27 (Gebaeudeliste)", 580, 40, 11, 2);
        ok &= Fall(sb, "Art 29 (Forschungsergebnisse)", 180, 20, 9, 7);

        sb.Append(ok ? "  BESTANDEN" : "  DURCHGEFALLEN");
        return sb.ToString();
    }

    private static bool Fall(System.Text.StringBuilder sb, string name,
                             int x, int y, int tiles, int anzahl)
    {
        int cell = WindowChrome.Cell;
        int spanne = WindowChrome.ScrollSpan(tiles);
        int bahnOben = y + cell;                       // Oberkante der Griffbahn
        int bahnUnten = y + cell * (tiles - 1);        // Oberkante der unteren Kappe

        sb.Append($"\n  {name}: x={x} y={y} Kacheln={tiles} Anzahl={anzahl}\n");
        sb.Append($"    Kappen (gemalt, nicht klickbar): oben {y}..{y + cell}, "
                + $"unten {bahnUnten}..{bahnUnten + cell}\n");
        sb.Append($"    Griffbahn {bahnOben}..{bahnUnten} = {spanne} Punkte "
                + $"(erwartet (Kacheln-3)*20 = {(tiles - 3) * cell})\n");

        // ---- 1. Lueckenlos, und genau die Griffbahn -------------------------
        //
        // ⚠ Punkt fuer Punkt abgefragt, nicht ueber die Formel nachgerechnet.
        // Wer die Rechnung mit sich selbst vergleicht, misst nichts.
        int ersterTreffer = int.MaxValue, letzterTreffer = int.MinValue, luecken = 0;
        bool innenAllesGetroffen = true, aussenAllesFrei = true;
        for (int yy = y - 5; yy < y + cell * tiles + 5; yy++)
        {
            int r = WindowChrome.ScrollHit(x, y, tiles, anzahl,
                                           new Vector2(x + 10, yy));
            bool drin = yy >= bahnOben && yy < bahnUnten;
            if (r >= 0)
            {
                if (yy < ersterTreffer) ersterTreffer = yy;
                if (yy > letzterTreffer) letzterTreffer = yy;
                if (!drin) aussenAllesFrei = false;
            }
            else
            {
                if (drin) { innenAllesGetroffen = false; luecken++; }
            }
        }
        bool deckung = innenAllesGetroffen && aussenAllesFrei
                       && ersterTreffer == bahnOben && letzterTreffer == bahnUnten - 1;
        sb.Append($"    Trefferstreifen {(ersterTreffer == int.MaxValue ? -1 : ersterTreffer)}"
                + $"..{(letzterTreffer == int.MinValue ? -1 : letzterTreffer) + 1}, "
                + $"Luecken darin: {luecken}, ausserhalb sauber: {aussenAllesFrei}\n");
        sb.Append($"    deckungsgleich mit der Griffbahn: {deckung}\n");

        // ---- 2. Der Griff schliesst unten buendig ab -----------------------
        int gOben = WindowChrome.ScrollGripY(y, tiles, 0, anzahl);
        int gUnten = WindowChrome.ScrollGripY(y, tiles, anzahl - 1, anzahl);
        // ⚠ Die UNTERKANTE des Griffs muss auf der OBERKANTE der unteren Kappe
        // liegen — nicht auf deren Unterkante. Hier stand beim ersten Anlauf
        // `bahnUnten + cell`, und der Lauf meldete »buendig: False« bei einem
        // Balken, der tadellos schloss. Ein Pruefstand, der die falsche Kante
        // vergleicht, meldet einen Fehler, den es nicht gibt.
        bool buendig = gOben == bahnOben && gUnten + cell == bahnUnten
                       && gUnten == bahnOben + spanne;
        sb.Append($"    Griff: Stand 0 -> y={gOben}, Stand {anzahl - 1} -> y={gUnten} "
                + $"(Unterkante {gUnten + cell}, untere Kappe ab {bahnUnten}) "
                + $"buendig: {buendig}\n");

        // ---- 3. Die Enden der Bahnrechnung ---------------------------------
        int obenR = WindowChrome.ScrollHit(x, y, tiles, anzahl,
                                           new Vector2(x + 10, y + 30));
        int untenR = WindowChrome.ScrollHit(x, y, tiles, anzahl,
                                            new Vector2(x + 10, y + 30 + spanne - 1));
        bool enden = obenR == 0 && untenR == anzahl - 1;
        sb.Append($"    Bahnenden: y={y + 30} -> {obenR}, "
                + $"y={y + 30 + spanne - 1} -> {untenR} (erwartet 0 und {anzahl - 1}) "
                + $"{enden}\n");

        // ---- 4. Die Kachelnummern ------------------------------------------
        bool kacheln = WindowChrome.ScrollCap == 0x3C && WindowChrome.ScrollTrack == 0x3D
                       && WindowChrome.ScrollBottom == 0x3F && WindowChrome.ScrollGrip == 0x40;
        sb.Append($"    Kacheln 0x3C/0x3D+0x3E/0x3F/0x40: {kacheln}\n");

        bool ok = deckung && buendig && enden && kacheln;
        if (WindowChrome.RollbalkenAlt)
        {
            // ⚠ Im Nullmodell MUSS Punkt 1 fallen. Ein Nullmodell, das
            // durchgeht, ist keines.
            sb.Append("    ⚠ NULLMODELL: hier MUSS »deckungsgleich« FALSE sein\n");
            ok = !deckung && buendig && enden && kacheln;
        }
        sb.Append(ok ? "    -> in Ordnung\n" : "    -> FALSCH\n");
        return ok;
    }
}
