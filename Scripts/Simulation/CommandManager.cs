namespace AkteEuropaReborn.Simulation;

using System.Collections.Generic;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// DIE BEFEHLSSCHICHT, von aussen gesehen — Stufe 2 des Determinismus.
///
/// <para><b>Was sich ändert.</b> Bis heute wirkt ein Mausklick unmittelbar auf
/// den Zustand: <c>MapViewer._UnhandledInput</c> ruft
/// <c>MapEntityLayer.IssueMove</c>, und das schreibt Pfade, Ziele und Zellen in
/// die Einheiten — mitten im Bildlauf, an einer beliebigen Stelle zwischen zwei
/// Simulationstakten. Für ein Netzspiel ist das nicht reparierbar: der zweite
/// Rechner hat den Klick nicht, und wann er ihn bekäme, hängt an der
/// Leitung.</para>
///
/// <para><b>Der Weg des Originals</b> ist der hier gebaute (Belege in
/// <see cref="CommandRecord"/> und <see cref="CommandRing"/>): eine Eingabe wird ein
/// <b>Satz von 236 Byte</b>, der Satz geht in einen <b>Ring</b>, und die
/// Simulation führt ihn <b>am Taktanfang</b> aus. Das Original schickt dabei
/// selbst den eigenen Befehl über die Leitung und wartet, bis er zurückkommt —
/// es kennt gar keinen kurzen Weg.</para>
///
/// <para><b>Zwei Rollen, die nicht zu verwechseln sind.</b></para>
/// <list type="number">
///   <item><b>Der Absender</b> (örtlich, darf den Zustand LESEN): entscheidet,
///   welche Einheiten gemeint sind und welche konkreten Zahlen der Satz trägt.
///   Er läuft nur auf der Maschine, die den Klick hat. Alles, was er ausrechnet,
///   steckt danach IM SATZ und ist damit auf allen Maschinen dieselbe Zahl.
///   Deshalb darf er die Auswahl (<c>_sel</c>, eine <c>HashSet&lt;int&gt;</c>)
///   und die Karte befragen, ohne den Determinismus zu berühren.</item>
///   <item><b>Der Behandler</b> (auf JEDER Maschine, am Taktanfang): darf nur
///   noch den Satz und den Zustand sehen. Er prüft und klemmt selbst — das
///   Original tut genau das (Opcode 3 klemmt P2/P3 im Behandler auf die
///   Karte, @0x4C2324), und es ist auch der einzige Aufbau, der einen
///   böswilligen oder veralteten Satz übersteht.</item>
/// </list>
///
/// <para><b>Wie ein Fremdcode das benutzt</b> — das ist die ganze Schnittstelle,
/// die der Eigentümer von <c>MapViewer.cs</c> / <c>MapEntityLayer.cs</c>
/// braucht:</para>
/// <code>
///   // statt  _entities.IssueMove(pos, shift):
///   _entities.PostMove(pos, shift);
///   // statt  _entities.IssueAttack(pos, shift):
///   _entities.PostAttack(pos, shift);       // false = kein Ziel getroffen
///
///   // in MapEntityLayer.SimTick(dt), als ERSTE Zeile:
///   CommandTick();
/// </code>
/// <para>Beides liegt in <c>Scripts/Simulation/Commands/CommandBridge.cs</c>
/// und gehört zu <c>MapEntityLayer</c> (eigene Teildatei), damit an den
/// fremden Dateien nur diese drei Zeilen zu ändern sind.</para>
///
/// <para>⚠ <b>Was hier NICHT drin ist.</b> Kein Transport, keine Lobby, keine
/// Keimverteilung, keine Prüfsummenrunde zwischen zwei Rechnern. Der Ring hat
/// die Stellen dafür (<see cref="CommandRing.Lead"/>,
/// <see cref="CommandRing.DrainForRelay"/>, <see cref="CommandRing.Digest"/>),
/// aber sie tun heute nichts über die Buchführung hinaus. Was nicht gebaut ist,
/// steht nicht da, als wäre es gebaut.</para>
///
/// <para>⚠ <b>EINE ENTSCHEIDUNG ÜBER DIE BAUFORM: diese Klasse wird GEFRAGT,
/// nicht GETICKT — und ist deshalb <c>static</c> und KEIN Autoload.</b></para>
///
/// <para>Sie war es zuerst versehentlich: die Datei stand als Platzhalter vom
/// 19.07. mit <c>: Node</c> da und ist in <c>project.godot</c> als Autoload
/// eingetragen; der erste Prüflauf brach mit »Failed to instantiate an autoload,
/// script … does not inherit from 'Node'« ab. Der Fehler ist behoben, indem die
/// <b>Autoload-Zeile entfällt</b>, nicht indem die Klasse ein Knoten wird — und
/// zwar aus einem inhaltlichen Grund:</para>
///
/// <para><b>Der Puffer darf nicht EINER für den Prozess sein.</b> Ein
/// Autoload-Knoten ist genau das: eine Instanz je Programm. Der Zwillings-
/// Prüfstand fährt aber ZWEI vollständige Simulationen im selben Prozess und der
/// Befehlsprüfstand DREI. Läge der Ring am Autoload, teilten sie ihn sich — und
/// das ist buchstäblich die Fehlerklasse, die Stufe 1 an vier Zufallsquellen
/// gefunden hat (ein <c>static</c>, das zwei Läufe teilen). Der Ring hängt
/// deshalb an der Simulation (<c>MapEntityLayer.Commands</c>), und getickt wird
/// er von <c>MapEntityLayer.CommandTick()</c> aus <c>SimTick</c> — von dort, wo
/// der Takt ist, nicht von einem zweiten Ort daneben. Was hier bleibt, sind
/// Werkzeuge ohne Zustand: <see cref="NewRing"/>, <see cref="Report"/>,
/// <see cref="Dump"/>. Zustandslose Werkzeuge brauchen keinen Knoten.</para>
///
/// <para>Das Projekt hat dafür seinen eigenen Präzedenzfall, zwei Zeilen über der
/// gestrichenen: »<c>CampaignManager is a static class, not a Node — it holds
/// the mission list and the progress number and is asked, not ticked</c>«. Genau
/// so ist es hier nachgezogen. <c>ReplayManager</c> und <c>NetworkManager</c>
/// bleiben unangetastete Platzhalter mit <c>: Node</c> und bleiben Autoloads —
/// für eine Änderung an ihnen gibt es heute keine Messung.</para>
/// </summary>
public static class CommandManager
{
    /// <summary>
    /// Der Ring dieser Simulation. ⚠ <b>Nicht statisch</b>, und das ist
    /// Absicht: der Zwillings-Prüfstand fährt ZWEI Simulationen im selben
    /// Prozess, und jede braucht ihren eigenen Ring. Genau an dieser Sorte
    /// Versehen — ein <c>static</c>, das zwei Läufe teilen — sind bei Stufe 1
    /// schon vier Zufallsquellen aufgeflogen.
    /// </summary>
    public static CommandRing NewRing() => new();

    /// <summary>Die Zahlen, die ein Prüfstand über eine Befehlsschicht braucht.
    /// Eine Summe allein sagt nie, welches Glied falsch war (Regel 10) —
    /// deshalb steht hier auch der letzte ausgeführte Satz drin.</summary>
    public readonly struct Report
    {
        public Report(CommandRing r, CommandRecord last, int lastTick)
        {
            Posted = r.Posted; Applied = r.Applied; Overflowed = r.Overflowed;
            Pending = r.Pending; Digest = r.Digest; Last = last; LastTick = lastTick;
        }

        public long Posted { get; }
        public long Applied { get; }
        public long Overflowed { get; }
        public int Pending { get; }
        public ulong Digest { get; }
        public CommandRecord Last { get; }
        public int LastTick { get; }

        public override string ToString()
            => $"abgesetzt {Posted}, ausgeführt {Applied}, wartend {Pending}, " +
               $"verloren {Overflowed}, Befehlsprüfsumme {Digest:X16}" +
               (Applied > 0 ? $", zuletzt in Takt {LastTick}: {Last.Describe()}" : "");
    }

    /// <summary>Die Sätze, die ein Ring noch vor sich hat, als Text — für den
    /// Fall, dass zwei Maschinen auseinanderlaufen und die Frage lautet, WELCHER
    /// Befehl es war.</summary>
    public static IEnumerable<string> Dump(CommandRing r)
    {
        foreach (var c in r.Waiting()) yield return c.Describe();
    }
}
