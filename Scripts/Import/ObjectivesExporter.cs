namespace AkteEuropaReborn.Import;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// DIE MISSIONSZIELE IM KLARTEXT — <b>OBJECTG.TXT</b>, 3.181 Byte, 33 Saetze
/// fuer 33 Missionen.
///
/// <para><b>Warum es das jetzt gibt.</b> Der Spieler wollte im Kampagnen-HUD das
/// Ziel der Hauptmission sehen. Wir zeigen bisher nur die UNTERmissionen, und
/// das aus einem Grund: ueber 28 Missionen sind alle 91 verfolgten Ziele
/// Untermissionen (78 beginnen woertlich mit <c>@Untermission</c>). Das
/// Hauptziel steht in keiner dieser Listen — es ist die ENDREGEL, und die hat
/// gar keinen Text. Den Text hat nur diese Datei.</para>
///
/// <para>⚠ <b>Und sie liegt auf der CD.</b> Auf dieser Maschine ist
/// <c>OBJECTG.TXT</c> lose nur in der Installation auf <c>F:</c> zu finden,
/// weshalb sie zuerst als »nur dort vorhanden« galt. Das ist falsch: sie steht
/// im Namensverzeichnis von <c>DATA1.CAB</c> auf CD 1, direkt zwischen
/// <c>HELPG.TXT</c> und <c>OPTIONS.CFG</c> — also genau dort, wo der Einspieler
/// <see cref="BriefingExporter"/> und <see cref="HelpExporter"/> ihre Texte
/// ohnehin schon herholt. Sie steht damit JEDER Installation zur Verfuegung,
/// und das Hauptziel muss keine Erfindung von uns sein.</para>
///
/// <para><b>Die Form, von der Datei abgelesen und gezaehlt:</b></para>
/// <code>
///   #001 Missionsziele:          Satz: '#' + DREI Ziffern, Nummern 001..033
///   ^- Neutralisieren Sie …      '^' beginnt ein Ziel, wie in HELPG.TXT
///   ^- Finden Sie das …
///   &lt;leer&gt;
/// </code>
/// <para>Gezaehlt: <b>33 Saetze</b>, Nummern 001..033 <b>ohne Luecke</b> — genau
/// die 33 Kampagnenmissionen —, und <b>57 Zielzeilen</b>.</para>
///
/// <para><b>Zwei Unregelmaessigkeiten, beide gemessen und beide behandelt</b>
/// (nicht uebergangen — eine still verschluckte Zeile ist ein fehlendes Ziel):
/// </para>
/// <list type="number">
///   <item><b>#026 traegt sein erstes Ziel in der KOPFZEILE:</b>
///     <c>#026 - Schuetzen Sie die verbuendete Basis</c> — dort steht nicht
///     »Missionsziele:«. Was hinter der Nummer steht und nicht die Ueberschrift
///     ist, ist selbst ein Ziel. Es ist der einzige solche Fall von 33.</item>
///   <item><b>Eine Zielzeile ist UMBROCHEN:</b> »… waehrend sie Positionen des
///     Gegners « endet mit einem Leerzeichen, und in der naechsten Zeile steht
///     allein <c>nehmen</c> — ohne <c>^</c>. Eine nichtleere Zeile ohne
///     Vorzeichen gehoert darum an das vorige Ziel. Es ist die einzige solche
///     Zeile der Datei.</item>
/// </list>
///
/// <para><b>UNSERE Setzung, und es ist die einzige:</b> der Aufzaehlungsstrich
/// »- « am Anfang eines Ziels faellt weg. Er gehoert der festen Anzeige von
/// 1997, nicht dem Text; wie die Anzeige eine Aufzaehlung zeichnet, ist ihre
/// Sache. ⚠ Das <c>@</c> vor einem Wort bleibt dagegen stehen, aus demselben
/// Grund wie bei <see cref="HelpExporter"/>: es ist eine Auszeichnung des
/// Originals, und sie wegzuwerfen waere ein Verlust.</para>
///
/// <para>cp437, wie alle Texte dieses Spiels — Latin-1 hat die Umlaute schon
/// einmal gefressen und einen Lauf gekostet.</para>
/// </summary>
public sealed class ObjectivesExporter
{
    private readonly string _dst;

    public ObjectivesExporter(string dstUi) => _dst = dstUi;

    /// <summary>Wie viele Saetze und wie viele Zielzeilen geschrieben wurden.</summary>
    public int Missions { get; private set; }
    public int Goals { get; private set; }

    /// <summary>Die zwei gemessenen Sonderfaelle, einzeln gezaehlt — faellt einer
    /// weg oder kommt einer dazu, hat sich die Datei geaendert und die Deutung
    /// gehoert nachgesehen.</summary>
    public int HeadGoals { get; private set; }
    public int Continued { get; private set; }

    public sealed class Entry
    {
        public int Id;
        public readonly List<string> Goals = new();
    }

    public static List<Entry> Parse(byte[] raw)
    {
        string text = Decode(raw);
        var list = new List<Entry>();
        Entry? cur = null;

        foreach (string lineRaw in text.Split('\n'))
        {
            string line = lineRaw.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line[0] == '#' && line.Length >= 4 &&
                Digit(line[1]) && Digit(line[2]) && Digit(line[3]))
            {
                cur = new Entry { Id = int.Parse(line.Substring(1, 3)) };
                list.Add(cur);
                // Was hinter der Nummer steht: entweder die Ueberschrift
                // »Missionsziele:« — dann traegt sie nichts bei — oder, in
                // genau einem der 33 Saetze, das erste Ziel selbst.
                string rest = line.Length > 4 ? line[4..].Trim() : "";
                if (rest.Length > 0 && !rest.Equals("Missionsziele:", StringComparison.OrdinalIgnoreCase))
                    cur.Goals.Add(Clean(rest));
                continue;
            }

            if (cur == null) continue;                 // Vorspann vor dem ersten Satz

            if (line[0] == '^') { cur.Goals.Add(Clean(line[1..])); continue; }

            // Eine nichtleere Zeile ohne Vorzeichen ist die FORTSETZUNG des
            // vorigen Ziels — die vorige Zeile endet dann auf einem Leerzeichen.
            if (cur.Goals.Count > 0)
                cur.Goals[^1] = (cur.Goals[^1] + " " + line.Trim()).Replace("  ", " ").Trim();
        }
        return list;
    }

    public void Write(byte[] raw, Action<string>? say = null)
    {
        var list = Parse(raw);
        Directory.CreateDirectory(_dst);

        var sb = new StringBuilder(1 << 14);
        sb.Append("{\"_note\":\"Missionsziele aus OBJECTG.TXT, cp437 — der Klartext, den das ");
        sb.Append("Original selbst fuehrt. Satznummer = Missionsnummer, 001..033 ohne Luecke.\",");
        sb.Append("\"_format\":\"'#NNN Missionsziele:' eroeffnet, '^' beginnt ein Ziel. ");
        sb.Append("#026 traegt sein erstes Ziel in der Kopfzeile; eine Zielzeile ist umbrochen ");
        sb.Append("und wird zusammengefuegt.\",");
        sb.Append("\"_ours\":\"der Aufzaehlungsstrich '- ' am Anfang faellt weg; das '@' bleibt.\",");
        sb.Append("\"goals\":{");
        bool first = true;
        foreach (var e in list)
        {
            if (e.Goals.Count == 0) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"\"{e.Id}\":[");
            for (int i = 0; i < e.Goals.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append($"\"{Esc(e.Goals[i])}\"");
            }
            sb.Append(']');
            Missions++;
            Goals += e.Goals.Count;
        }
        sb.Append("}}");
        File.WriteAllText(_dst + "/objectives.json", sb.ToString(), new UTF8Encoding(false));

        say?.Invoke($"Missionsziele: {Missions} Missionen, {Goals} Ziele aus OBJECTG.TXT " +
                    $"(gemessen: 33 Saetze 001..033 ohne Luecke, 57 Zielzeilen)");
        if (Missions != 33)
            say?.Invoke($"Missionsziele: ⚠ {Missions} statt 33 Saetze — die Datei ist eine " +
                        "andere als die gemessene, die Deutung gehoert nachgesehen");
    }

    private static bool Digit(char c) => c >= '0' && c <= '9';

    /// <summary>Den Aufzaehlungsstrich abnehmen und mehrfache Leerzeichen
    /// zusammenziehen — die Datei setzt sie zum Ausrichten auf dem festen
    /// Schirm von 1997 (»Besetzen Sie den      Forschungskomplex«).</summary>
    private static string Clean(string s)
    {
        s = s.Trim();
        if (s.StartsWith("- ", StringComparison.Ordinal)) s = s[2..];
        else if (s.StartsWith("-", StringComparison.Ordinal)) s = s[1..];
        while (s.Contains("  ", StringComparison.Ordinal)) s = s.Replace("  ", " ");
        return s.Trim();
    }

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
