using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b>»NAH GENUG« — das Aufgeben, das dritte Gegengewicht aus BZ.4.</b>
///
/// <para>Gelesen am 31.08.2026 aus <c>GAME.EXE</c> (OFFENE_FRAGEN CG). Es ist
/// der Zweig, den das Original selbst <c>"stay move I"</c> nennt — die
/// Debug-Zeichenkette steht bei <c>0x4F6934</c> und wird unmittelbar davor
/// ausgegeben.</para>
///
/// <para><b>Der Block, Byte für Byte</b> (<c>edi</c> = Einheitensatz):</para>
/// <code>
///   0x408D05  xor eax,eax
///   0x408D07  mov al, [edi+0x18]              ; CX = Ziel-Spalte
///   0x408D0C  shl eax, 8
///   0x408D0F  mov cl, [edi+0x19]              ; CY = Ziel-Zeile
///   0x408D12  add eax, ecx                    ; idx = CX*256 + CY
///   0x408D14  mov ecx, 0x1F40                 ; 8000
///   0x408D19  cmp word [eax*2+0xBDEA80], cx   ; imap[Zielzelle]
///   0x408D21  jae  0x408D41                   ; >= 8000 -> NICHT aufgeben
///   0x408D23  cmp word [edi+0x36], cx         ; Zieleinheit
///   0x408D27  jbe  0x408D41                   ; <= 8000 -> NICHT aufgeben
///   0x408D29  push ebx ; call 0x410D70        ; Luftlinie Position -> Zielfeld
///   0x408D32  cmp ax, 3
///   0x408D36  jge  0x408D41                   ; >= 3    -> NICHT aufgeben
///   0x408D38  mov byte [edi+0x14], 0          ; UKOL := 0   AUFGEBEN
///   0x408D3C  jmp  0x409EEE                   ; raus
/// </code>
///
/// <para>⚠⚠ <b>CD.3 hatte die erste Bedingung VERKEHRT HERUM</b> (bug-004).
/// Dort stand »wenn dort <i>keine</i> Einheit steht (<c>&gt;= 0x1F40</c>)«.
/// <c>jae</c> springt bei <c>&gt;= 0x1F40</c> aber ÜBER das Aufgeben hinweg.
/// Aufgegeben wird bei <c>imap &lt; 8000</c>, und das ist nach CE.3 genau
/// <b>ein Fahrzeug auf der Zielzelle</b> (10000…13999 wären Infanterie,
/// darüber Gebäude — beides zählt hier NICHT). Gebaut hätte die verkehrte
/// Fassung in jedem Lauf das Gegenteil getan: aufgeben, sobald das Ziel
/// <i>frei</i> ist. BZ.4 Nr. 3 hatte es von Anfang an richtig.</para>
///
/// <para>⭐ <b>Die dritte Bedingung fehlte in beiden Notizen ganz.</b>
/// <c>+0x36</c> ist die ZIELEINHEIT — BX.1 hat sie schon gelesen: der
/// Fahrbefehl setzt <c>word[+0x36] := 0xFFFF</c> (»Zieleinheit löschen«).
/// Werte unter 8000 sind ein Einheitenindex (8000 Sätze zu 78 ab
/// <c>0x6E26C8</c>), alles darüber heißt <i>keine</i>. <b>Aufgegeben wird also
/// nur bei einem reinen Fahrbefehl</b> — wer einer Einheit folgt oder sie
/// angreift, gibt nie auf, egal wie nah er ist.</para>
///
/// <para>⚠ <b>Und die Stelle im Ablauf ist enger als »bei einer Blockade«.</b>
/// Der Block hängt allein im Absage-Zweig (<c>Can_go = 0</c>) und dort erst
/// HINTER dem Geduldszähler:</para>
/// <code>
///   Can_go = 0 -> Geduld +0x1C--  ; != 0 -> raus (warten)
///                 Geduld := 40 + rand()%20
///                 Gattung 0x47? -> Streuung (H), raus     [haben wir nicht]
///                 stay move I: AUFGEBEN?  ja -> UKOL := 0, raus
///                                         nein -> neu planen (Tafel 0x40A208)
/// </code>
/// <b>Der Zugesagt-Zweig (<c>Can_go = 1</c>) hat das Aufgeben NICHT.</b> Wer
/// eine Zusage bekommt, gibt nie auf. Geprüft wird also im Mittel alle 40–59
/// Takte, nicht jeden Takt — wer es in den Takt hängt, baut etwas anderes.
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Die Schwelle aus <c>cmp ax, 3</c> @0x408D32. Die Luftlinie
    /// kommt aus <c>0x410D70</c>: <c>sqrt(dx² + dy²)</c> über
    /// <c>(+0x00,+0x01)</c> gegen <c>(+0x18,+0x19)</c>, gerundet nach
    /// <c>ax</c>. Also EUKLIDISCH — nicht die Chebyshev-Entfernung, mit der
    /// <c>--stuck-check</c> rechnet.</summary>
    private const int AufgebenNah = 3;

    /// <summary><c>--kein-aufgeben</c> — die Gegenprobe.</summary>
    public static bool AufgebenAn = true;

    /// <summary>Wie oft die Prüfung überhaupt lief (Geduld abgelaufen), wie oft
    /// sie zuschlug, und an welcher der drei Bedingungen sie sonst scheiterte.
    /// ⚠ Ohne die Aufschlüsselung ist »das Aufgeben tut nichts« nicht von »es
    /// wurde nie geprüft« zu unterscheiden — dieselbe Lehre wie bei
    /// <c>AusweichFeind</c> (CF.3).</summary>
    public int AufgebenGeprueft, Aufgegeben,
               AufgebenZielFrei, AufgebenHatZieleinheit, AufgebenZuWeit;

    /// <summary>Wie oft alle drei Bedingungen zutrafen, das Aufgeben aber
    /// wegen <c>--kein-aufgeben</c> unterblieb. Das ist die Zahl, die die
    /// Gegenprobe erst aussagekräftig macht.</summary>
    public int AufgebenUnterdrueckt;

    /// <summary>
    /// <b>Die Prüfung <c>@0x408D05</c>.</b> Wird im Absage-Zweig gerufen,
    /// nachdem die Geduld abgelaufen und neu gesetzt ist, und <b>vor</b> dem
    /// Neuplanen.
    /// </summary>
    /// <returns><c>true</c>, wenn aufgegeben wurde — dann darf der Rufer NICHT
    /// mehr neu planen.</returns>
    private bool AufgebenWeilNahGenug(int i, Entity e)
    {
        if (_nav == null) return false;
        // ⚠ Die Pruefung laeuft AUCH unter --kein-aufgeben, nur ihre WIRKUNG
        // nicht. Sonst meldet die Gegenprobe gar keine Zahl, und dann laesst
        // sich »das Aufgeben aendert nichts« nicht von »der Fall kam nie vor«
        // unterscheiden — genau der Fehler, den CF.3 beschreibt.
        AufgebenGeprueft++;

        // 1. imap[Zielzelle] < 8000 — ein FAHRZEUG steht auf dem Zielfeld.
        //    ⚠ Das Original vergleicht hier NICHT mit sich selbst. Steht die
        //    Einheit auf ihrem eigenen Ziel, ist imap[Ziel] ihr eigener Index,
        //    also < 8000, und sie gibt auf. Das ist gewollt und bleibt so.
        int wer = _nav.OccupantAt(e.Goal.X, e.Goal.Y);
        if (wer < 0 || wer >= _entities.Count) { AufgebenZielFrei++; return false; }
        var z = _entities[wer];
        // Infanterie steht in der imap bei 10000…13999, Gebäude darüber —
        // beide fallen NICHT unter 8000 und lösen das Aufgeben nicht aus.
        if (z.Dead || z.IsBuilding || z.IsProp || z.Infantry >= 0)
        { AufgebenZielFrei++; return false; }

        // 2. Zieleinheit +0x36 >= 8000 — ein REINER Fahrbefehl, kein Verfolgen.
        if (e.Target >= 0) { AufgebenHatZieleinheit++; return false; }

        // 3. Luftlinie < 3 (0x410D70, euklidisch).
        float dx = e.Col - e.Goal.X, dy = e.Row - e.Goal.Y;
        if (Mathf.Sqrt(dx * dx + dy * dy) >= AufgebenNah)
        { AufgebenZuWeit++; return false; }

        if (!AufgebenAn) { AufgebenUnterdrueckt++; return false; }

        // UKOL := 0.
        //
        // ⚠⚠ HIER WEICHT DER NACHBAU AB, UND ZWAR NOTWENDIG. Im Original ist
        // UKOL das Auftragsfeld, und der Fahrarm laeuft nur bei UKOL == 2; mit
        // UKOL := 0 kommt er nie wieder dran, und der Wegpuffer +0x1A darf
        // darum unberuehrt stehenbleiben (anders als in Zweig C @0x408B4F, der
        // ihn ausdruecklich auf 0xFF setzt). UNSER Fahrer hat dieses Tor nicht
        // — bei uns IST `e.Path != null` das Tor (siehe die Schleife um
        // BlockedStep). Wer hier nur `e.Ukol = UkolFrei` setzt, aendert nichts:
        // die Einheit faehrt im naechsten Takt weiter.
        //
        // Die WIRKUNG von UKOL := 0 ist »der Fahrauftrag ist zu Ende«, und das
        // ist bei uns wortgleich die Ankunftsbehandlung am Wegende.
        SchiffAufgegeben++;
        e.Ukol = UkolFrei;
        e.Path = null;
        e.StepCost = 0; e.Progress = 0;
        Aufgegeben++;
        NextQueued(i, e);          // ein Wegpunkt ist erledigt: der naechste
        return true;
    }

    /// <summary>Die Meldezeile. Leer, solange nie geprüft wurde.
    ///
    /// <para>⚠ <b>Was <c>--stuck-check</c> daraus macht, muss man wissen:</b>
    /// wer bei Entfernung 2 aufgibt, zählt dort NICHT als »auf dem eigenen
    /// Ziel« (das verlangt <c>d &lt;= 1</c>) und hat keinen Weg mehr — er
    /// landet in <c>STEHT OHNE WEG / unterwegs liegengeblieben</c>. Das
    /// Aufgeben treibt diesen Zähler also nach oben, ohne dass etwas kaputt
    /// ist. Zu vergleichen sind <b>Fortschritt</b> und <b>gefahren</b>, und
    /// diese Zeile sagt, wie viel davon aufs Aufgeben geht.</para></summary>
    public string AufgebenLine()
        => AufgebenGeprueft == 0 ? ""
         : $"aufgeben: {AufgebenGeprueft}x geprueft, {Aufgegeben}x aufgegeben "
         + (AufgebenAn ? "" : $"(GEGENPROBE: {AufgebenUnterdrueckt}x haette es) ")
         + $"(nah genug am belegten Ziel); abgelehnt weil "
         + $"{AufgebenZielFrei}x kein Fahrzeug auf dem Ziel, "
         + $"{AufgebenHatZieleinheit}x Zieleinheit gesetzt, "
         + $"{AufgebenZuWeit}x weiter als {AufgebenNah} Zellen";
}
