namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Die Hilfe- und Untermissionstexte — <b>HELPG.TXT</b>, 101.223 Byte, und das
/// Stück, das der Kampagne bisher ganz gefehlt hat.
///
/// <para><b>Warum es gebraucht wird</b> (11.08.2026): der Missionsblock von
/// Mission 1 @0x49844D ruft <c>show_text</c> @0x401A69 und <c>show_text2</c>
/// @0x401D66 siebzehnmal, mit den Nummern 1..13, 18, 20, 39, 40 und 110 — und
/// genau das ist der tutorialartige Ablauf, den das Original dort hat:
/// »Willkommen bei Akte Europa« (#001), »Sie können eine Einheit @ANWÄHLEN«
/// (#002), »Zum @BEWEGEN der angewählten Einheit…« (#004), »Für den @ANGRIFF…«
/// (#006), »Zum Übernehmen @neutraler @Einheiten führen Sie eine ihrer eigenen
/// in deren unmittelbare Nähe« (#013) — und die Nebenmission mit den Schiffen:
/// »@Untermission … Versenken Sie sie. @Bezahlung — 50$ für jeden versenkten…«
/// (#110), deren Auszahlung derselbe Block als dreimal
/// <c>bus_cmd(528, 50, 0)</c> führt.</para>
///
/// <para><b>Die Form, von der Datei abgelesen, nicht geraten:</b></para>
/// <code>
///   #0Help Texts&lt;CR&gt;&lt;LF&gt;      Kopfzeile — KEIN Leerzeichen nach der Null
///   #001 Willkommen bei …        Satz: '#' + DREI Ziffern + Leerzeichen oder Umbruch
///   … das @Spielfeld und&lt;CR&gt;&lt;LF&gt; CR LF ist nur der Umbruch des festen Schirms
///   ^ @Ziel der Untermission     '^' beginnt einen Absatz (wie in BRIEFG.TXT)
///   … #0 general message; …      '#0 ' leitet einen Entwicklerkommentar ein
/// </code>
///
/// <para>Gezählt: <b>274 Sätze</b> am Zeilenanfang, Nummern 1..533 mit Lücken,
/// davon <b>269 mit Text und 5 leer</b> (die leeren sind Platzhalter, etwa
/// #030/#031 direkt vor #032). Zwei Nummern kommen doppelt vor; der erste
/// Satz gewinnt, und die Zahl steht in der Datei, damit es auffällt.</para>
///
/// <para><b>Zwei Entscheidungen sind UNSERE und sind hier benannt:</b>
/// (1) CR und CR LF im Rumpf werden zu einem Leerzeichen und der Text wird vom
/// Schirm neu umbrochen — dieselbe Begründung wie bei
/// <see cref="BriefingExporter"/>: die Umbrüche gehören der festen Anzeige von
/// 1997, nicht dieser. Nur '^' bleibt ein Absatz. (2) Das <c>@</c> vor einem
/// Wort bleibt im Text stehen. Das Original hebt das folgende Wort damit hervor;
/// es hier zu entfernen würde die Auszeichnung wegwerfen, und sie zu deuten ist
/// Sache der Anzeige, nicht des Imports.</para>
///
/// <para>HELPG.DAT (4.000 Byte) und HELPG.PIC (129.600 = 360x360) gehören dazu
/// und sind <b>nicht</b> gelesen.</para>
/// </summary>
public sealed class HelpExporter
{
    private readonly string _dst;

    /// <summary>Wie viele Sätze mit Text geschrieben wurden.</summary>
    public int Texts;

    /// <summary>Wie viele leere Platzhalter übergangen wurden.</summary>
    public int Empty;

    /// <summary>Nummern, die zweimal vorkamen — der erste Satz gewinnt.</summary>
    public int Duplicates;

    public HelpExporter(string uiDir) => _dst = uiDir.TrimEnd('/', '\\');

    public sealed class Entry
    {
        public int Id;
        public readonly List<string> Paragraphs = new();
    }

    /// <summary>Liest HELPG.TXT. Öffentlich, damit der Selbsttest dieselbe
    /// Zerlegung prüfen kann, die der Export benutzt, statt eine Kopie.</summary>
    public static List<Entry> Parse(byte[] raw)
    {
        string text = Decode(raw);
        var list = new List<Entry>();
        var seen = new HashSet<int>();
        Doubles.Clear();

        // Satzanfänge: '#' + drei Ziffern am Zeilenanfang, gefolgt von einem
        // Leerzeichen oder einem Umbruch. Das '#0 ' des Entwicklerkommentars
        // fällt damit heraus (eine Ziffer), und die Kopfzeile '#0Help Texts'
        // ebenso.
        var starts = new List<(int Pos, int Id)>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '#') continue;
            if (i > 0 && text[i - 1] != '\n') continue;          // nur am Zeilenanfang
            if (i + 4 > text.Length) break;
            if (!Digit(text[i + 1]) || !Digit(text[i + 2]) || !Digit(text[i + 3])) continue;
            if (i + 4 < text.Length && text[i + 4] != ' ' && text[i + 4] != '\r'
                && text[i + 4] != '\n') continue;
            starts.Add((i, (text[i + 1] - '0') * 100 + (text[i + 2] - '0') * 10 + (text[i + 3] - '0')));
        }

        for (int k = 0; k < starts.Count; k++)
        {
            int from = starts[k].Pos + 4;
            int to = k + 1 < starts.Count ? starts[k + 1].Pos : text.Length;
            string body = text[from..to];

            // der Entwicklerkommentar am Satzende
            int cut = body.IndexOf("#0 ", StringComparison.Ordinal);
            if (cut >= 0) body = body[..cut];

            var e = new Entry { Id = starts[k].Id };
            foreach (string part in body.Split('^'))
            {
                // CR und LF sind der Umbruch des alten Schirms — UNSERE Setzung,
                // siehe Kopf
                string p = part.Replace('\r', ' ').Replace('\n', ' ');
                while (p.Contains("  ", StringComparison.Ordinal))
                    p = p.Replace("  ", " ", StringComparison.Ordinal);
                p = p.Trim();
                if (p.Length > 0) e.Paragraphs.Add(p);
            }
            // ⚠ Doppelte Nummern gibt es wirklich: #058 und #340 stehen jeweils
            // ZWEIMAL HINTEREINANDER in der Datei — zwei Übersetzungsversuche
            // desselben Satzes (»Wenn ein @Minenentferner eine Mine passiert…«
            // und »Wenn ein @Fallenentferner eine Fußangel passiert…«). Der
            // ERSTE gewinnt. Welchen von beiden das Original nimmt, ist
            // ungelesen — es zählt bei jedem Aufruf neu durch die Datei, und
            // wo es dabei stehenbleibt, steht hier nicht fest.
            if (!seen.Add(e.Id)) { Doubles.Add(e.Id); continue; }
            list.Add(e);
        }
        return list;
    }

    /// <summary>Nummern, die in der Datei mehr als einmal vorkommen.</summary>
    public static readonly List<int> Doubles = new();

    public void Write(byte[] raw, Action<string>? say = null)
    {
        var list = Parse(raw);
        Directory.CreateDirectory(_dst);

        int ids = 0;
        var sb = new StringBuilder(1 << 17);
        sb.Append("{\"_note\":\"Hilfe- und Untermissionstexte aus HELPG.TXT, cp437\",");
        sb.Append("\"_format\":\"Saetze '#NNN text' am Zeilenanfang; '^' ist ein Absatz, ");
        sb.Append("'#0 ' leitet einen Entwicklerkommentar ein\",");
        sb.Append("\"_wrap\":\"CR und CR LF werden zu einem Leerzeichen und der Text vom Schirm ");
        sb.Append("neu umbrochen - UNSERE Setzung, wie bei den Briefings\",");
        sb.Append("\"_at\":\"das '@' vor einem Wort bleibt stehen: das Original hebt das folgende ");
        sb.Append("Wort damit hervor\",\"texts\":{");
        bool first = true;
        foreach (var e in list)
        {
            ids++;
            if (e.Paragraphs.Count == 0) { Empty++; continue; }
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{e.Id}\":[");
            for (int i = 0; i < e.Paragraphs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"\"{Esc(e.Paragraphs[i])}\"");
            }
            sb.Append(']');
            Texts++;
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/help.json", sb.ToString(), new UTF8Encoding(false));
        Duplicates = Doubles.Count;
        say?.Invoke($"Hilfetexte: {Texts} Saetze, {Empty} leere Platzhalter uebergangen " +
                    $"(von {ids} Nummern), {Duplicates} doppelte Nummern " +
                    $"[{string.Join(",", Doubles)}] — die erste Fassung gilt");
    }

    private static bool Digit(char c) => c >= '0' && c <= '9';

    /// <summary>cp437, wie der Rest der Texte des Spiels. Latin-1 hat die
    /// Umlaute schon einmal gefressen (0x81/0x84/0x94/0xE1) und einen Lauf
    /// gekostet — siehe <see cref="BriefingExporter"/>.</summary>
    private static string Decode(byte[] raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (byte b in raw) sb.Append(Cp437.Char(b));
        return sb.ToString();
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c < 0x20 ? "" : c.ToString(),
            });
        return sb.ToString();
    }
}
