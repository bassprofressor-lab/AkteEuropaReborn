namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// <b>DIE ENZYKLOPÄDIE DES ORIGINALS</b> — <c>ENCYCLOG.TXT</c>, 32.977 Byte,
/// 106 Seiten, und bis zum 17.08.2026 hat niemand hineingesehen.
///
/// <para><b>Wie sie gefunden wurde</b>, und die Lehre steckt darin: der Spieler
/// hatte gebeten, den Menüpunkt »Enzyklopaedie« auf unser Wiki zu verlinken.
/// Beim Nachsehen, was das Original hinter der Zeile hat, lagen neben GAME.EXE
/// drei Dateien: <c>ENCYCLOG.TXT</c>, <c>ENCYCLOG.DAT</c> (12.000 B) und
/// <c>ENCYCLOG.PIC</c> (345.600 B). Der Text ist vollständig da. ⚠ <b>Bevor
/// etwas ERSETZT wird, nachsehen, ob das Original es schon hat</b> — dieselbe
/// Lehre wie bei der Bahnstrecke (Arbeitsweise 16), nur an einer Menüzeile.</para>
///
/// <para><b>Die Form, von der Datei abgelesen:</b></para>
/// <code>
///   #p20,1&lt;CR&gt;&lt;LF&gt;    Seite 20, Bildnummer 1  (die Zahl kann fehlen: "#p1,")
///   #c1Spinne          Farbe 1 = Überschrift, bis Zeilenende
///   #c0                Farbe 0 = Fliesstext
///   Die Spinne ist …   Rumpf, mit den festen Umbrüchen des Schirms von 1997
///   #r20Spinne         VERWEIS auf Seite 20, Verweistext bis Zeilenende
///   #r95Dicke #r95Bertha   zwei Bruchstücke, DERSELBE Verweis — ein Name mit
///                          Leerzeichen wird stückweise ausgezeichnet
/// </code>
///
/// <para><b>Gezählt:</b> 106 Seiten (Nummern 1..162 mit Lücken), 96 davon mit
/// einer Bildnummer im Bereich 1..97, 149 Verweise, 316 Farbwechsel.</para>
///
/// <para>⚠⚠ <b>DIE KODIERUNG IST EINE FALLE, UND ZWAR DIE UMGEKEHRTE ZUR
/// BEKANNTEN.</b> <c>HELPG.TXT</c> und <c>BRIEFG.TXT</c> sind <b>cp437</b>
/// (»können« = <c>6B 94 6E</c>), <c>ENCYCLOG.TXT</c> im selben Ordner ist
/// <b>Latin-1</b> (»Räder« = <c>52 E4 64</c>). Mit dem cp437-Leser wird daraus
/// »RΣder«, mit dem Latin-1-Leser wird aus HELPG »knnen«. Beide Leser sind
/// richtig — für ihre Datei. Im Kopf der Arbeitsweise steht die Lehre bisher
/// nur in einer Richtung (»Latin-1 statt cp437 fraß die Umlaute«); sie gilt in
/// beide, und die Datei entscheidet, nicht der Ordner.</para>
///
/// <para><b>Was NICHT gelesen ist und hier auch nicht behauptet wird:</b>
/// <c>ENCYCLOG.PIC</c>. Die Bildnummern laufen bis 97, die Datei fasst bei
/// 120×120 aber nur 24 Bilder (345.600 = 24 · 14.400) — die Nummer zeigt also
/// nicht ohne Weiteres dorthin. Die Nummer wird trotzdem mitgeschrieben, damit
/// sie da ist, wenn jemand die Zuordnung findet; angezeigt wird sie nicht.
/// <c>ENCYCLOG.DAT</c> ist ebenfalls ungelesen.</para>
///
/// <para><b>Unsere Entscheidungen, ausdrücklich:</b> (1) Die festen Umbrüche des
/// Rumpfes werden zu Leerzeichen und der Text wird vom Schirm neu umbrochen —
/// dieselbe Begründung wie bei <see cref="HelpExporter"/> und
/// <see cref="BriefingExporter"/>: die Umbrüche gehören der Anzeige von 1997.
/// Eine LEERZEILE bleibt ein Absatz. (2) Zwei aufeinanderfolgende Verweise auf
/// dieselbe Seite werden zu EINEM zusammengezogen, sonst stünde »Dicke« und
/// »Bertha« als zwei Knöpfe da.</para>
/// </summary>
public sealed class EncyclopediaExporter
{
    private readonly string _dst;

    public int Pages, Links, WithPicture;

    public EncyclopediaExporter(string dst) { _dst = dst; }

    private sealed class Page
    {
        public int Number, Picture = -1;
        public string Title = "";
        public readonly StringBuilder Body = new();
        public readonly List<(int To, string Text)> Links = new();
    }

    public void Write(byte[] raw, Action<string>? say = null)
    {
        // ⚠ Latin-1, NICHT cp437 — siehe Klassenkopf. Das ist die einzige Zeile,
        // an der diese Datei sich von HELPG.TXT unterscheidet, und sie kostet
        // jede Umlautstelle, wenn sie falsch steht.
        string text = Encoding.Latin1.GetString(raw);
        var pages = new List<Page>();
        Page? cur = null;
        bool titleNext = false;

        foreach (string rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine;

            if (line.StartsWith("#p", StringComparison.Ordinal))
            {
                // "#p20,1" — Seite und (kann fehlen) Bildnummer
                string rest = line[2..];
                int comma = rest.IndexOf(',');
                string numTxt = comma >= 0 ? rest[..comma] : rest;
                string picTxt = comma >= 0 ? rest[(comma + 1)..].Trim() : "";
                if (!int.TryParse(numTxt, out int num)) continue;
                cur = new Page { Number = num };
                if (int.TryParse(picTxt, out int pic)) { cur.Picture = pic; WithPicture++; }
                pages.Add(cur);
                titleNext = false;
                continue;
            }
            if (cur == null) continue;

            if (line.StartsWith("#c", StringComparison.Ordinal))
            {
                // Farbe 1 leitet die Überschrift ein; sie steht in DERSELBEN
                // Zeile dahinter, nicht in der nächsten.
                bool head = line.Length > 2 && line[2] == '1';
                string tail = line.Length > 3 ? line[3..] : "";
                if (head)
                {
                    if (tail.Length > 0) cur.Title = tail.Trim();
                    else titleNext = true;         // kommt in der nächsten Zeile
                }
                else if (tail.Length > 0) AppendBody(cur, tail);
                continue;
            }

            if (line.Contains("#r", StringComparison.Ordinal))
            {
                foreach (var (to, txt) in ParseLinks(line))
                {
                    // Zwei Bruchstücke auf DIESELBE Seite hintereinander sind
                    // EIN Verweis — "#r95Dicke #r95Bertha".
                    if (cur.Links.Count > 0 && cur.Links[^1].To == to)
                        cur.Links[^1] = (to, cur.Links[^1].Text + " " + txt);
                    else cur.Links.Add((to, txt));
                    Links++;
                }
                continue;
            }

            if (titleNext && line.Trim().Length > 0)
            { cur.Title = line.Trim(); titleNext = false; continue; }

            AppendBody(cur, line);
        }

        Pages = pages.Count;
        Directory.CreateDirectory(_dst);
        File.WriteAllText(_dst + "/encyclopedia.json", ToJson(pages), new UTF8Encoding(false));
        say?.Invoke($"Enzyklopaedie: {Pages} Seiten, {Links} Verweise, " +
                    $"{WithPicture} mit Bildnummer (Bild ungelesen)");
    }

    /// <summary>Eine Zeile Rumpf anhängen: der feste Umbruch von 1997 wird ein
    /// Leerzeichen, eine LEERZEILE bleibt ein Absatz.</summary>
    private static void AppendBody(Page p, string line)
    {
        if (line.Trim().Length == 0)
        {
            if (p.Body.Length > 0 && !p.Body.ToString().EndsWith("\n\n", StringComparison.Ordinal))
                p.Body.Append("\n\n");
            return;
        }
        if (p.Body.Length > 0 && !p.Body.ToString().EndsWith("\n\n", StringComparison.Ordinal))
            p.Body.Append(' ');
        p.Body.Append(line.Trim());
    }

    /// <summary>Alle <c>#rN&lt;text&gt;</c>-Bruchstücke einer Zeile.</summary>
    private static IEnumerable<(int To, string Text)> ParseLinks(string line)
    {
        int i = 0;
        while (true)
        {
            int at = line.IndexOf("#r", i, StringComparison.Ordinal);
            if (at < 0) yield break;
            int k = at + 2;
            int num = 0; bool any = false;
            while (k < line.Length && char.IsDigit(line[k])) { num = num * 10 + (line[k] - '0'); k++; any = true; }
            if (!any) { i = at + 2; continue; }
            int end = line.IndexOf("#r", k, StringComparison.Ordinal);
            string txt = (end < 0 ? line[k..] : line[k..end]).Trim();
            i = end < 0 ? line.Length : end;
            if (txt.Length > 0) yield return (num, txt);
            if (end < 0) yield break;
        }
    }

    private static string ToJson(List<Page> pages)
    {
        var sb = new StringBuilder();
        sb.Append("{\"_source\":\"ENCYCLOG.TXT (Latin-1!), Enzyklopaedie des Originals\",");
        sb.Append("\"_note\":\"#pN,Bild | #c1 Ueberschrift | #c0 Text | #rN Verweis. ");
        sb.Append("Die Bildnummer ist mitgeschrieben, aber UNGELESEN: ENCYCLOG.PIC ");
        sb.Append("fasst bei 120x120 nur 24 Bilder, die Nummern laufen bis 97.\",");
        sb.Append("\"pages\":{");
        bool first = true;
        foreach (var p in pages)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{p.Number}\":{{\"picture\":{p.Picture},");
            sb.Append($"\"title\":\"{Esc(p.Title)}\",");
            sb.Append($"\"body\":\"{Esc(p.Body.ToString().Trim())}\",\"links\":[");
            for (int i = 0; i < p.Links.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"{{\"to\":{p.Links[i].To},\"text\":\"{Esc(p.Links[i].Text)}\"}}");
            }
            sb.Append("]}");
        }
        sb.Append("}}");
        return sb.ToString();
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
            sb.Append(c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "", '\t' => " ",
                _ => c.ToString(),
            });
        return sb.ToString();
    }
}
