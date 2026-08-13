namespace AkteEuropaReborn.Simulation.Commands;

using System;
using System.Collections.Generic;

/// <summary>
/// DER BEFEHLSPUFFER — 1000 Plätze von 236 Byte, drei Zeiger, ein Takt.
///
/// <para><b>Die Form ist die des Originals.</b> Es hält die Sätze in EINEM
/// zusammenhängenden Feld und drei Wortzeigern darauf; nichts wird verkettet,
/// nichts belegt Speicher nach. Darum liegt hier ein <c>byte[]</c> und keine
/// Liste von Objekten: der Puffer IST das Byteabbild, und die Prüfsumme über
/// ihn ist damit die Prüfsumme über die Sätze, wie sie über die Leitung gehen
/// würden — nicht über eine C#-Zwischenform.</para>
/// <code>
///   Lesezeiger    0x53828C   der Verteiler führt ihn aus
///   Schreibzeiger 0x538290   hier trägt post() ein, hierhin empfängt das Netz
///   Sendezeiger   0x538294   bis hierher ist weitergeleitet
///   weiterschalten @0x4C1B30:  i = (i + 1) % 1000
/// </code>
///
/// <para><b>Warum überhaupt ein Puffer, wenn es doch auch direkt geht.</b> Weil
/// das Original es NICHT direkt tut, und zwar nicht aus Bequemlichkeit. In
/// <c>post()</c> @0x4C1C50 steht die Weiche:</para>
/// <code>
///   if ([0x538298] == 2) return;              ; wir spielen eine Wiederholung: nichts posten
///   if ([0x538270] == 0) {                    ; normale Partie
///       Kladde[+0x02] = 0;  Kladde[+0x04] = -1;
///       Kladde[+0x22] = [0x4F9248];
///       IDirectPlay::Send(Kladde, 236);       ; ← auch der EIGENE Befehl geht über die Leitung
///       return;
///   }
///   Ring[Schreibzeiger] = Kladde;  weiterschalten(&Schreibzeiger);
/// </code>
/// <para>Der eigene Befehl wird <b>gesendet</b> und kommt über
/// <c>IDirectPlay::Receive</c> (@0x404460, in den Ringplatz des
/// Schreibzeigers) genauso zurück wie der eines Mitspielers. Erst dann wird er
/// ausgeführt. Das ist der Kern von Lockstep: <b>keine Eingabe wirkt je direkt
/// auf den Zustand</b>, alle nehmen denselben Weg und damit dieselbe
/// Reihenfolge. Der zweite Zweig ([0x538270] != 0, gesetzt @0x419492 beim
/// Abbrechen der Sitzung) ist der Kurzschluss ohne Netz — und selbst der geht
/// noch durch den Ring.</para>
///
/// <para><b>Die Fälligkeit ist echt.</b> Der Absender @0x4C5B50 trägt
/// <c>[0x538264]</c> (die Uhr) in +0x04 ein, und die Pumpe
/// @0x4C1DF9..0x4C1E0D lässt einen Satz erst durch, wenn <c>Uhr &gt;=
/// Fälligkeit</c> — mit <c>-1</c> als »sofort«. Genau dieses Feld ist später
/// der Verzögerungsausgleich; heute steht es auf dem nächsten Takt.</para>
///
/// <para>⚠ <b>UNSERE SETZUNG:</b> die Fälligkeit zählt bei uns in
/// <b>Simulationstakten</b> (SimHz = 60), nicht in Millisekunden. Das
/// Original zählt in seiner eigenen Uhr, deren Einheit nicht gelesen ist. Takte
/// sind die einzige Einheit, die auf zwei Maschinen dieselbe ist — auf ihnen
/// beruht die Stufe 1 dieses Umbaus, und alles andere wäre ein Rückschritt.</para>
/// </summary>
public sealed class CommandRing
{
    /// <summary>Die Plätze. 1000, wie im Original — siehe
    /// <see cref="CommandOp.RingSlots"/> und die dort notierte
    /// Ungereimtheit.</summary>
    public const int Slots = CommandOp.RingSlots;

    private readonly byte[] _buf = new byte[Slots * CommandRecord.Stride];

    private int _read, _write, _relay;

    /// <summary>Der Takt, in dem die Simulation gerade steht. Wird von
    /// <see cref="ApplyDue"/> gesetzt und ist die Uhr, gegen die die
    /// Fälligkeit geprüft wird.</summary>
    public int Tick { get; private set; }

    /// <summary>⚠ UNSERE SETZUNG: um so viele Takte liegt die Fälligkeit eines
    /// neu abgesetzten Befehls in der Zukunft. <b>0 bedeutet: nächster
    /// Takt</b> — ein Befehl, der mitten im Bild abgesetzt wird, wirkt nie
    /// mitten im Takt, sondern immer an einem Taktanfang. Im Netzspiel wird
    /// hier der Vorlauf eingetragen (die Zahl, um die alle Maschinen dem
    /// Eingang voraus rechnen).</summary>
    public int Lead { get; set; }

    // ---- Zahlen für den Prüfstand ------------------------------------------

    public long Posted { get; private set; }
    public long Applied { get; private set; }
    /// <summary>Wie oft ein Befehl abgewiesen wurde, weil der Ring voll war.
    /// Das Original schreibt hier »Message buffer full is« (0x5383EC,
    /// @0x4C1E8B) — ein Zustand, den es kennt und meldet.</summary>
    public long Overflowed { get; private set; }

    public int Pending => _write >= _read ? _write - _read : Slots - _read + _write;

    /// <summary>Der Weiterschalter des Originals, Zeichen für Zeichen:
    /// <c>inc; cmp 1000; jne; mov 0</c> (@0x4C1B30).</summary>
    private static int Next(int i) => i + 1 == Slots ? 0 : i + 1;

    // ---- Eintragen ----------------------------------------------------------

    /// <summary>
    /// Einen Befehl absetzen. <b>Er wirkt hier nicht</b> — er wird nur
    /// hingelegt. Die Fälligkeit bekommt er jetzt: <c>Takt + 1 + Lead</c>.
    ///
    /// <para>Gibt false zurück, wenn der Ring voll ist. Ein Aufrufer, der das
    /// nicht prüft, verliert einen Befehl still — deshalb wird es gezählt.</para>
    /// </summary>
    public bool Post(CommandRecord cmd)
    {
        int next = Next(_write);
        if (next == _read) { Overflowed++; return false; }
        if (cmd.Due == -1) cmd.Due = Tick + 1 + Lead;
        cmd.WriteTo(_buf.AsSpan(_write * CommandRecord.Stride, CommandRecord.Stride));
        _write = next;
        Posted++;
        return true;
    }

    /// <summary>Den Satz an einem Platz ansehen, ohne ihn zu verbrauchen.</summary>
    public CommandRecord At(int slot) => CommandRecord.ReadFrom(_buf.AsSpan(slot * CommandRecord.Stride, CommandRecord.Stride));

    /// <summary>Alle noch nicht ausgeführten Sätze, in der Reihenfolge, in der
    /// sie ausgeführt werden. Nur für Prüfstand und Protokoll.</summary>
    public IEnumerable<CommandRecord> Waiting()
    {
        for (int i = _read; i != _write; i = Next(i)) yield return At(i);
    }

    // ---- Ausführen ----------------------------------------------------------

    /// <summary>
    /// <b>DER TAKTANFANG.</b> Alles, was für diesen Takt fällig ist, wird
    /// ausgeführt — und nichts sonst. Gibt zurück, wie viele Sätze das waren.
    ///
    /// <para>Die Schleife ist die des Originals: ein Kopfzeiger, ein Blick auf
    /// die Fälligkeit, ausführen, weiterschalten, bis der Lesezeiger den
    /// Schreibzeiger erreicht (@0x4C4847: <c>weiterschalten(&amp;Lesezeiger);
    /// if (Schreibzeiger != Lesezeiger) noch einmal</c>). Es wird
    /// <b>nicht</b> gesucht und <b>nicht</b> sortiert: die Reihenfolge im Ring
    /// IST die Reihenfolge der Ausführung, und das ist der Punkt — zwei
    /// Maschinen mit demselben Ring rechnen dasselbe.</para>
    ///
    /// <para>⚠ Bricht beim ersten noch nicht fälligen Satz ab und geht NICHT
    /// weiter hinten weiter. Ein Satz mit späterer Fälligkeit blockiert also
    /// die dahinter — genauso wie im Original, wo die Pumpe an derselben Stelle
    /// stehen bleibt. Das ist gewollt: würde übersprungen, hinge die
    /// Reihenfolge an der Ankunftszeit, und die ist auf zwei Maschinen
    /// verschieden.</para>
    /// </summary>
    public int ApplyDue(int tick, Func<CommandRecord, bool> handler)
    {
        Tick = tick;
        int n = 0;
        while (_read != _write)
        {
            var c = At(_read);
            if (c.Due != -1 && c.Due > tick) break;
            Absorb(c);
            handler(c);
            _read = Next(_read);
            Applied++;
            n++;
        }
        return n;
    }

    /// <summary>Nur die Uhr stellen, ohne auszuführen — für den Aufbau, bevor
    /// der erste Takt läuft.</summary>
    public void SetTick(int tick) => Tick = tick;

    // ---- Weiterleiten (noch ohne Transport) ---------------------------------

    /// <summary>
    /// Was ab dem Sendezeiger weiterzuleiten wäre, mit der Schranke des
    /// Originals (Opcode &lt;= 1000, @0x4C1FE2), und den Merker +0x02 auf 1
    /// gesetzt (@0x4C1FFB) — genau die Buchführung, die der Transport später
    /// braucht.
    ///
    /// <para>⚠ Es gibt heute keinen Transport. Diese Methode leitet nichts
    /// weiter; sie liefert die Sätze, die weiterzuleiten WÄREN, und schaltet den
    /// Sendezeiger nach. Damit ist die Stelle gebaut und benannt, an der
    /// <c>ENetMultiplayerPeer</c> später ansetzt, ohne dass irgendetwas
    /// vorgetäuscht wird (Regel 3).</para>
    /// </summary>
    public List<CommandRecord> DrainForRelay()
    {
        var outp = new List<CommandRecord>();
        while (_relay != _write)
        {
            var c = At(_relay);
            if (CommandOp.IsRelayed(c.Op))
            {
                c.Flag = 1;
                c.WriteTo(_buf.AsSpan(_relay * CommandRecord.Stride, CommandRecord.Stride));
                outp.Add(c);
            }
            _relay = Next(_relay);
        }
        return outp;
    }

    // ---- Prüfsumme ----------------------------------------------------------

    /// <summary>
    /// Prüfsumme über die Sätze, die dieser Ring schon AUSGEFÜHRT hat — nicht
    /// über den Ringinhalt.
    ///
    /// <para>Der Unterschied ist wichtig: der Ringinhalt hängt an den Zeigern
    /// und an dem, was noch aussteht, also an der Ankunftszeit. Was ausgeführt
    /// wurde, hängt nur an der Simulation. Zwei Maschinen können also nur diese
    /// Zahl sinnvoll vergleichen, und es ist dieselbe Sorte Prüfsumme wie in
    /// <see cref="Determinism.Hasher"/>.</para>
    /// </summary>
    public ulong Digest { get; private set; } = 0xCBF29CE484222325UL;

    internal void Absorb(in CommandRecord c)
    {
        ulong h = Digest;
        Span<byte> b = stackalloc byte[CommandRecord.Stride];
        c.WriteTo(b);
        foreach (byte x in b) { h ^= x; h *= 0x100000001B3UL; }
        Digest = h;
    }
}
