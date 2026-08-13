namespace AkteEuropaReborn.Rendering;

using System;
using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// DER BEFEHLSPRÜFSTAND: <c>--befehl-check[=&lt;takte&gt;]</c>
///
/// <para><b>Welche Fehlerklasse dieser Prüfstand sehen kann</b> (Regel 7 — die
/// Frage steht VOR dem grünen Ergebnis):</para>
/// <list type="number">
///   <item><b>Eine falsche Verschriftlichung des Satzes.</b> Er schreibt Sätze
///   in die 236 Byte des Originals und liest sie zurück. Gegenprobe: er kippt
///   in einem davon ein einziges Bit und MUSS das merken — tut er das nicht, ist
///   die Rückleseprobe blind und wird als wertlos gemeldet.</item>
///   <item><b>Ein Befehl, der durch den Puffer etwas ANDERES bewirkt als der
///   heutige Direktzugriff.</b> Er fährt dieselbe Karte dreimal im selben
///   Prozess: <b>A</b> gibt den Befehl als Satz durch den Puffer, <b>B</b> ruft
///   den heutigen Weg (<c>IssueMove</c>) am selben Taktanfang, <b>C</b> gibt
///   keinen Befehl. Verglichen wird nach JEDEM Takt der ganzzahlige Zustand.
///   <c>A == B</c> ist die Aussage; <c>A != C</c> ist die Bedingung dafür, dass
///   sie etwas bedeutet.</item>
///   <item><b>Ein Puffer, der gar nicht angeschlossen ist.</b> Wäre
///   <c>CommandTick</c> nicht gerufen oder der Satz nie fällig, wäre A gleich C
///   — und genau das wird geprüft und als FEHLER gemeldet, nicht als Erfolg.
///   Das ist die Lehre aus <c>--script-check</c>, der ein Lager um 1000 anhob
///   und »erzwungen« meldete.</item>
///   <item><b>Ein Befehl, der die Simulation auseinanderlaufen lässt.</b>
///   <c>--befehl-poison=&lt;takt&gt;</c> gibt A einen um eine Zelle
///   verschobenen Befehl. Der Prüfstand MUSS anschlagen und den Takt nennen.</item>
/// </list>
///
/// <para><b>Was er NICHT sehen kann</b>, und das gehört dazu: er fährt drei
/// Simulationen im SELBEN Prozess mit demselben Programm. Ein Unterschied
/// zwischen zwei Rechnern (andere Fliesskommaeinheit, andere .NET-Fassung) liegt
/// aussen. Und er prüft nur die Befehle, die er selbst absetzt — nicht, ob die
/// Oberfläche sie richtig absetzt, denn die hängt heute noch am Direktweg.</para>
///
/// <para><b>Aufruf</b> (⚠ <c>DOTNET_gcConcurrent=0</c> ist Pflicht — die
/// Begründung steht in <c>DeterminismTwinRunner.WarnAboutGc</c>):</para>
/// <code>
///   DOTNET_gcConcurrent=0 Godot_console.exe --headless --path . -- \
///       --skirmish=map_NET07,3,hard --no-briefing --befehl-check=600
/// </code>
/// <list type="bullet">
///   <item><c>--befehl-check=&lt;takte&gt;</c> — so viele Takte, Vorgabe 600.</item>
///   <item><c>--befehl-at=&lt;takt&gt;</c> — in diesem Takt wird der Befehl
///     fällig, Vorgabe 30. Abgesetzt wird er einen Takt vorher.</item>
///   <item><c>--befehl-einheiten=&lt;zahl&gt;</c> — so viele Einheiten werden
///     ausgewählt, Vorgabe 4.</item>
///   <item><c>--befehl-weg=&lt;zellen&gt;</c> — so weit vom Start weg wird
///     geklickt, Vorgabe 8.</item>
///   <item><c>--befehl-poison=&lt;takt&gt;</c> — GEGENPROBE: A bekommt einen
///     verschobenen Befehl.</item>
///   <item><c>--befehl-out=&lt;pfad&gt;</c> — Protokoll auch in eine Datei.</item>
/// </list>
/// </summary>
public sealed partial class CommandCheckRunner : Node
{
    private MapEntityLayer _a = null!, _b = null!, _c = null!;
    private int _ticks, _limit = 600, _at = 30, _units = 4, _reach = 8, _poison = -1;
    private double _dt = 1.0 / 60.0;
    private bool _done, _split;
    private Godot.FileAccess? _log;
    private ulong _gdSeed;
    private Vector2I _click;
    private readonly List<int> _chosen = new();

    /// <summary>Nur EINER je Prozess — dieselbe Sperre und derselbe Grund wie
    /// bei <see cref="DeterminismTwinRunner.Active"/>: die Simulationen, die
    /// dieser Prüfstand aufsetzt, sind selbst <c>MapEntityLayer</c> und würden
    /// sonst jede einen weiteren Prüfstand starten.</summary>
    internal static bool Active { get; private set; }

    internal static bool TryStart(MapEntityLayer host)
    {
        if (Active) return true;
        bool on = false;
        var r = new CommandCheckRunner();
        string outPath = "";
        foreach (string a in Core.CommandLine.Args)
        {
            if (a == "--befehl-check") on = true;
            else if (a.StartsWith("--befehl-check="))
            { on = true; int.TryParse(a["--befehl-check=".Length..], out int t); if (t > 0) r._limit = t; }
            else if (a.StartsWith("--befehl-at=")) int.TryParse(a["--befehl-at=".Length..], out r._at);
            else if (a.StartsWith("--befehl-einheiten=")) int.TryParse(a["--befehl-einheiten=".Length..], out r._units);
            else if (a.StartsWith("--befehl-weg=")) int.TryParse(a["--befehl-weg=".Length..], out r._reach);
            else if (a.StartsWith("--befehl-poison=")) int.TryParse(a["--befehl-poison=".Length..], out r._poison);
            else if (a.StartsWith("--befehl-out=")) outPath = a["--befehl-out=".Length..];
        }
        if (!on) return false;
        // ⚠ mindestens 2: abgesetzt wird einen Takt VOR der Fälligkeit, und
        // Takt 1 ist der erste, den der Prüfstand fährt.
        if (r._at < 2) r._at = 2;
        if (outPath.Length > 0) r._log = Godot.FileAccess.Open(outPath, Godot.FileAccess.ModeFlags.Write);
        Active = true;
        host.GetTree().Root.AddChild(r);
        r.CallDeferred(nameof(Build), host);
        return true;
    }

    private void Say(string s)
    {
        GD.Print(s);
        if (_log == null) return;
        _log.StoreLine(s);
        _log.Flush();
    }

    // ================= 1. Die Rückleseprobe ==================================

    /// <summary>
    /// Satz -&gt; 236 Byte -&gt; Satz, und danach die Gegenprobe.
    ///
    /// <para>Ein Prüfstand, der unsere Ableitung mit sich selbst vergleicht,
    /// prüft nichts. Deshalb steht hinter der Rückleseprobe die
    /// <b>Bitkippprobe</b>: ein einziges Bit in den 236 Byte wird umgelegt, und
    /// wenn der Vergleich das nicht merkt, wird die ganze Rückleseprobe als
    /// wertlos gemeldet.</para>
    /// </summary>
    private bool RoundTrip()
    {
        var rnd = new Core.Math.DeterministicRng(0xA17E5EED);
        int n = 0, bad = 0;
        Span<byte> buf = stackalloc byte[CommandRecord.Stride];
        for (int k = 0; k < 2000; k++)
        {
            var c = new CommandRecord
            {
                Op = (short)rnd.NextInt(0, 1300),
                Flag = (byte)rnd.NextInt(0, 2),
                Player = (byte)rnd.NextInt(0, 8),
                Due = rnd.NextInt(-1, 100000),
                Unknown22 = (short)rnd.NextInt(short.MinValue, short.MaxValue),
                P15 = (short)rnd.NextInt(short.MinValue, short.MaxValue),
            };
            for (int p = 1; p <= CommandRecord.Params; p++)
                c[p] = (short)rnd.NextInt(short.MinValue, short.MaxValue);

            c.WriteTo(buf);
            var back = CommandRecord.ReadFrom(buf);
            n++;
            if (!Same(c, back)) bad++;
        }
        Say($"1) Rückleseprobe (Satz -> {CommandRecord.Stride} Byte -> Satz): {n - bad} von {n} gleich");
        if (bad > 0) { Say($"   ⚠ {bad} Sätze kamen VERÄNDERT zurück — die Verschriftlichung ist falsch."); return false; }

        // Gegenprobe: kann der Vergleich einen Unterschied überhaupt sehen?
        var probe = CommandRecord.Make(CommandOp.Move, 1, 5, 40, 41, 0);
        probe.WriteTo(buf);
        int seen = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            buf[0x0A] ^= (byte)(1 << bit);              // ein Bit in P2
            if (!Same(probe, CommandRecord.ReadFrom(buf))) seen++;
            buf[0x0A] ^= (byte)(1 << bit);
        }
        Say($"   Gegenprobe Bitkipp in P2: {seen} von 8 gekippten Bits erkannt");
        if (seen != 8) { Say("   ⚠ die Rückleseprobe ist BLIND — ihr grünes Ergebnis oben belegt nichts."); return false; }

        // Und die Versätze selbst: liegt der Opcode wirklich auf +0x00, die
        // Fälligkeit auf +0x04, P1 auf +0x08? Das prüft die FORM des Originals,
        // nicht nur die Umkehrbarkeit unserer eigenen Rechnung.
        var f = new CommandRecord { Op = 0x0102, Flag = 0x03, Player = 0x04, Due = 0x08070605, P15 = 0x1112 };
        f[1] = 0x0A09; f[2] = 0x0C0B; f[11] = 0x1E1D; f.Unknown22 = 0x2021;
        f.WriteTo(buf);
        bool form = buf[0x00] == 0x02 && buf[0x01] == 0x01 && buf[0x02] == 0x03 && buf[0x03] == 0x04
                 && buf[0x04] == 0x05 && buf[0x07] == 0x08
                 && buf[0x08] == 0x09 && buf[0x09] == 0x0A
                 && buf[0x0A] == 0x0B && buf[0x0B] == 0x0C
                 && buf[0x1C] == 0x1D && buf[0x1D] == 0x1E
                 && buf[0x22] == 0x21 && buf[0x23] == 0x20
                 && buf[0x24] == 0x12 && buf[0x25] == 0x11;
        Say($"   Feldversätze wie im Original (+0x00 Op, +0x02 Merker, +0x04 fällig, " +
            $"+0x08 P1, +0x1C P11, +0x22, +0x24 P15): {(form ? "ja" : "NEIN")}");
        if (!form) return false;

        // Alles hinter +0x25 muss 0 bleiben: das Original berührt es nicht, und
        // wenn wir dort etwas ablegen, wäre unser Satz nicht mehr der seine.
        int dirty = 0;
        for (int i = 0x26; i < CommandRecord.Stride; i++) if (buf[i] != 0) dirty++;
        Say($"   unberührter Rest von +0x26 bis +0x{CommandRecord.Stride - 1:X2}: " +
            $"{CommandRecord.Stride - 0x26 - dirty} von {CommandRecord.Stride - 0x26} Byte auf 0");
        return dirty == 0;
    }

    private static bool Same(in CommandRecord a, in CommandRecord b)
    {
        if (a.Op != b.Op || a.Flag != b.Flag || a.Player != b.Player || a.Due != b.Due
            || a.Unknown22 != b.Unknown22 || a.P15 != b.P15) return false;
        for (int p = 1; p <= CommandRecord.Params; p++) if (a[p] != b[p]) return false;
        return true;
    }

    // ================= 2. Der Ringlauf =======================================

    /// <summary>Der Puffer allein, ohne Simulation: Fälligkeit, Reihenfolge,
    /// Überlauf. Drei Zahlen, die nichts mit der Spielwelt zu tun haben und
    /// deshalb auch nicht von ihr verdeckt werden können.</summary>
    private bool RingBehaviour()
    {
        var ring = CommandManager.NewRing();
        var seen = new List<(int tick, short p1)>();

        // drei Befehle in Takt 0 abgesetzt, einer davon mit späterer Fälligkeit
        ring.Post(CommandRecord.Make(CommandOp.Move, 0, 1, 10, 10, 0));
        ring.Post(CommandRecord.Make(CommandOp.Move, 0, 2, 11, 11, 0));
        var late = CommandRecord.Make(CommandOp.Move, 0, 3, 12, 12, 0);
        late.Due = 5;
        ring.Post(late);
        ring.Post(CommandRecord.Make(CommandOp.Move, 0, 4, 13, 13, 0));

        for (int t = 0; t <= 8; t++)
            ring.ApplyDue(t, c => { seen.Add((t, c.P1)); return true; });

        string got = string.Join(" ", seen.ConvertAll(s => $"{s.tick}:{s.p1}"));
        Say($"2) Ringlauf, Reihenfolge Takt:P1 -> {got}");

        // Erwartet: 1 und 2 werden in Takt 1 fällig (Absetzen in Takt 0 => Takt 1),
        // 3 erst in Takt 5, und 4 steht HINTER 3 — die Reihenfolge im Ring ist die
        // Reihenfolge der Ausführung, es wird nicht übersprungen.
        bool ok = seen.Count == 4
               && seen[0] == (1, 1) && seen[1] == (1, 2)
               && seen[2] == (5, 3) && seen[3] == (5, 4);
        Say($"   fällig am Taktanfang, nichts übersprungen, nichts vorgezogen: {(ok ? "ja" : "NEIN")}");
        if (!ok) return false;

        // Überlauf: das Original kennt den Zustand und meldet ihn
        // (»Message buffer full is«, 0x5383EC). Wir zählen ihn.
        var full = CommandManager.NewRing();
        int posted = 0;
        for (int k = 0; k < CommandRing.Slots + 10; k++)
            if (full.Post(CommandRecord.Make(CommandOp.Move, 0, (short)k))) posted++;
        Say($"   Ring nimmt {posted} Sätze auf ({CommandRing.Slots} Plätze, einer bleibt " +
            $"Trennplatz), abgewiesen {full.Overflowed}");
        return posted == CommandRing.Slots - 1 && full.Overflowed == 11;
    }

    // ================= 3. Die drei Läufe =====================================

    private void Build(MapEntityLayer host)
    {
        var viewer = host.GetParent() as MapViewer;
        if (viewer == null) { Say("FEHLER: der Prüfstand hängt nicht unter einem MapViewer"); Quit(2); return; }
        host.SetProcess(false);
        host.SetPhysicsProcess(false);

        Say($"# Befehlsprüfstand: {_limit} Takte, Befehl fällig in Takt {_at}, " +
            $"{_units} Einheit(en), Klick {_reach} Zellen weg, " +
            $"Gift {(_poison < 0 ? "—" : "Takt " + _poison)}");

        bool r1 = RoundTrip();
        bool r2 = RingBehaviour();
        if (!r1 || !r2) { Say("ABBRUCH: die Grundprobe des Satzes/Puffers ist nicht grün."); Quit(3); return; }

        string name = viewer.TwinMapName;
        if (name.Length == 0) { Say("FEHLER: kein Kartenname"); Quit(2); return; }

        _a = viewer.BuildTwin(name, out string ea);
        if (ea.Length > 0) { Say("FEHLER A: " + ea); Quit(2); return; }
        _b = viewer.BuildTwin(name, out string eb);
        if (eb.Length > 0) { Say("FEHLER B: " + eb); Quit(2); return; }
        _c = viewer.BuildTwin(name, out string ec);
        if (ec.Length > 0) { Say("FEHLER C: " + ec); Quit(2); return; }

        // ⚠ Nach ALLEN Ladevorgängen: jeder ruft Determinism.NewMap und stellt
        // die Ströme zurück — der dritte macht die Würfe der ersten zwei
        // rückgängig. Erst hier stehen alle drei auf demselben Anfang.
        Determinism.ResetStreams();
        _gdSeed = Determinism.Seed;

        var sa = new List<long>(); var sb = new List<long>(); var sc = new List<long>();
        _a.DeterminismSnapshot(sa); _b.DeterminismSnapshot(sb); _c.DeterminismSnapshot(sc);
        if (DeterminismTwinRunner.FirstDiff(sa, sb) >= 0 || DeterminismTwinRunner.FirstDiff(sa, sc) >= 0)
        {
            Say("⚠ SCHON DER ANFANGSZUSTAND ist verschieden — jede weitere Zahl wäre wertlos");
            Quit(3);
            return;
        }
        Say($"3) Anfangszustand aller drei Läufe gleich: {sa.Count} Zahlen, " +
            $"Prüfsumme {_a.DeterminismChecksum():X16}");

        if (!ChooseOrder()) { Quit(3); return; }
    }

    /// <summary>
    /// Dieselbe Auswahl und derselbe Klick in allen drei Läufen — sonst
    /// vergleicht der Prüfstand zwei verschiedene Befehle und misst am falschen
    /// Gegenstand.
    /// </summary>
    private bool ChooseOrder()
    {
        _chosen.Clear();
        foreach (int i in _a.CommandCheckMobileUnits(_units)) _chosen.Add(i);
        if (_chosen.Count == 0)
        {
            Say("⚠ ABBRUCH: auf dieser Karte hat der Spieler keine fahrbereite Einheit. " +
                "Ein Prüfstand ohne Gegenstand meldet keinen Erfolg.");
            return false;
        }
        _click = _a.CommandCheckClickCell(_chosen[0], _reach);
        if (_click.X < 0)
        {
            Say("⚠ ABBRUCH: keine erreichbare Zielzelle gefunden.");
            return false;
        }
        _a.CommandCheckSelect(_chosen);
        _b.CommandCheckSelect(_chosen);
        _c.CommandCheckSelect(_chosen);
        Say($"   Auswahl {_chosen.Count} Einheit(en) {string.Join(",", _chosen)}, " +
            $"Klick auf Zelle ({_click.X},{_click.Y})");
        // ⚠ Was der Prüfstand mit EINER Einheit NICHT prüft: die Gruppenlogik
        // (verschiedene Zielzellen je Einheit, die `taken`-Menge). Das ist kein
        // Mangel des Prüfstands, sondern der Karte — und es muss dastehen, sonst
        // liest jemand das grüne Ergebnis als »Gruppenbefehle stimmen«.
        if (_chosen.Count < 2)
            Say("   ⚠ nur EINE Einheit: die Gruppenlogik (je Einheit eine eigene Zielzelle) " +
                "ist mit diesem Lauf NICHT geprüft. Eine Karte mit mehreren eigenen " +
                "fahrbereiten Einheiten nehmen.");
        return true;
    }

    public override void _Process(double delta)
    {
        if (_done || _a == null) return;
        _ticks++;

        // --- A: der Befehl geht als SATZ durch den Puffer --------------------
        //
        // ⚠ HIER STAND `_a.CommandTick();` — der Prüfstand rief den Taktanfang
        // selbst, weil er älter ist als die Zeile in `SimTick`. Seit Commit
        // 185e98f ruft `SimTick` ihn als erste Zeile, und damit lief er ZWEIMAL
        // je Takt: die Befehlsuhr dieser Simulation zählte 2 statt 1, und die
        // Fälligkeit wurde gegen eine doppelt so schnelle Uhr geprüft. Das war
        // unschädlich (die Fälligkeit wird bei Post aus derselben Uhr gebildet),
        // aber es ist genau die Sorte stillgelegter Schalter, die Regel 11
        // meint — und ein Prüfstand mit einer eigenen Uhr neben der der
        // Simulation prüft nicht mehr denselben Gegenstand.
        Sync();
        Determinism.UseStream(0);
        _a._Process(_dt);
        if (_ticks == _at - 1)
        {
            int n = _a.CommandCheckPostMove(_click, _poison == _at);
            Say($"   Takt {_ticks}: {n} Satz/Sätze abgesetzt (A){(_poison == _at ? " ⚠ GIFT: um eine Zelle verschoben" : "")}");
            foreach (string s in CommandManager.Dump(_a.Commands)) Say("      " + s);
        }

        // --- B: derselbe Befehl auf dem HEUTIGEN Direktweg ------------------
        Sync();
        Determinism.UseStream(1);
        if (_ticks == _at) _b.CommandCheckIssueDirect(_click);
        _b._Process(_dt);

        // --- C: kein Befehl -------------------------------------------------
        Sync();
        Determinism.UseStream(2);
        _c._Process(_dt);

        var sa = new List<long>(); var sb = new List<long>(); var sc = new List<long>();
        _a.DeterminismSnapshot(sa); _b.DeterminismSnapshot(sb); _c.DeterminismSnapshot(sc);

        int d = DeterminismTwinRunner.FirstDiff(sa, sb);
        if (d >= 0)
        {
            Say($"AUSEINANDER (A Puffer vs. B Direktweg) bei Takt {_ticks} (t={_ticks * _dt:0.0000} s)");
            Say($"   {DeterminismTwinRunner.Describe(d, sa, sb)}");
            Say($"   A: {_a.CommandReport()}");
            Say($"   Der Befehl, der es war — zuletzt ausgeführt: {_a.CommandReport().Last.Describe()}");
            if (_poison >= 0)
            {
                Say("   ⚠ DAS WAR DIE GEGENPROBE, und sie hat angeschlagen: der verschobene "
                  + "Befehl wurde gefunden, mit Takt, Einheit und Feld. Der Prüfstand ist "
                  + "damit nicht blind.");
                Finish(true);
            }
            else Finish(false);
            return;
        }
        if (!_split && DeterminismTwinRunner.FirstDiff(sa, sc) >= 0)
        {
            _split = true;
            Say($"   Takt {_ticks}: A und C (ohne Befehl) sind ab hier verschieden — " +
                "der Befehl hat gewirkt.");
        }

        if (_ticks % 120 == 0)
            Say($"   Takt {_ticks}: gleich, Prüfsumme {_a.DeterminismChecksum():X16}, {_a.CommandReport()}");

        if (_ticks >= _limit)
        {
            Say($"DURCH: {_limit} Takte, A und B Zahl für Zahl gleich.");
            Say($"   {_a.CommandReport()}");
            if (!_split)
            {
                Say("⚠ ABER: A und C sind NIE auseinandergegangen. Der Befehl hat nichts " +
                    "bewirkt — entweder war er nie fällig, oder der Behandler hat ihn " +
                    "verworfen, oder der Puffer ist nicht angeschlossen. Das grüne " +
                    "Ergebnis oben belegt in diesem Fall NICHTS.");
                Finish(false);
                return;
            }
            if (_poison >= 0)
            {
                Say("⚠ ABER: mit --befehl-poison MUSSTE A von B abweichen, und das tat es " +
                    "nicht. Der Prüfstand sieht einen falschen Befehl nicht.");
                Finish(false);
                return;
            }
            Finish(true);
        }
    }

    /// <summary>Denselben Keim vor jeden der drei Läufe — dieselbe Krücke und
    /// dieselbe Begründung wie in <see cref="DeterminismTwinRunner"/>: der
    /// globale Godot-Würfel ist EINER je Prozess.</summary>
    private void Sync() => GD.Seed(_gdSeed ^ (ulong)_ticks * 0x9E3779B97F4A7C15UL);

    private void Finish(bool ok)
    {
        _done = true;
        Say(ok ? "ERGEBNIS: grün." : "ERGEBNIS: ROT.");
        _log?.Flush();
        Quit(ok ? 0 : 1);
    }

    private void Quit(int code)
    {
        _done = true;
        _log?.Flush();
        GetTree().Quit(code);
    }
}

/// <summary>
/// Die Handgriffe, die der Befehlsprüfstand an einer Simulation braucht.
/// Sie liegen bei <see cref="MapEntityLayer"/>, weil sie an die Auswahl und die
/// Einheitenliste heranmüssen — und in dieser Datei, weil sie nur der Prüfstand
/// ruft.
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>Die ersten <paramref name="want"/> fahrbereiten Einheiten des
    /// Spielers, in Reihenfolge des Feldes — NICHT aus <c>_sel</c>, damit alle
    /// drei Läufe dieselbe Liste bekommen.</summary>
    internal List<int> CommandCheckMobileUnits(int want)
    {
        var outp = new List<int>();
        for (int i = 0; i < _entities.Count && outp.Count < want; i++)
        {
            var e = _entities[i];
            if (!Commandable(i) || !e.Mobile || e.IsBuilding || e.DugIn) continue;
            if (e.FuelMax > 0 && e.Fuel <= 0) continue;
            outp.Add(i);
        }
        return outp;
    }

    /// <summary>Eine erreichbare Zelle etwa <paramref name="reach"/> Zellen von
    /// der Einheit weg. Sie wird EINMAL gesucht, in Lauf A, und danach in allen
    /// drei Läufen benutzt.</summary>
    internal Vector2I CommandCheckClickCell(int unit, int reach)
    {
        if (_nav == null || unit < 0 || unit >= _entities.Count) return new Vector2I(-1, -1);
        var e = _entities[unit];
        for (int r = reach; r >= 1; r--)
            foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(0, 1), new Vector2I(-1, 0),
                                      new Vector2I(0, -1), new Vector2I(1, 1), new Vector2I(-1, -1) })
            {
                var c = new Vector2I(e.Col + d.X * r, e.Row + d.Y * r);
                if (c.X < 1 || c.Y < 1 || c.X >= _nav.Width - 1 || c.Y >= _nav.Height - 1) continue;
                if (!_nav.IsFree(c.X, c.Y, e.Move, unit)) continue;
                var p = _nav.FindPath(new Vector2I(e.Col, e.Row), c, e.Move, unit);
                if (p != null && p.Count > 0) return c;
            }
        return new Vector2I(-1, -1);
    }

    internal void CommandCheckSelect(List<int> units)
    {
        _sel.Clear();
        foreach (int i in units) _sel.Add(i);
        SetPrimary();
    }

    /// <summary>Lauf A: den Befehl als Satz absetzen. <paramref name="poison"/>
    /// verschiebt die Zielzelle um eine — die Gegenprobe.</summary>
    internal int CommandCheckPostMove(Vector2I cell, bool poison)
        => PostMove(CellCenter(cell.X + (poison ? 1 : 0), cell.Y), queue: false);

    /// <summary>Lauf B: derselbe Klick auf dem HEUTIGEN Weg — der Direktzugriff,
    /// gegen den gemessen wird.</summary>
    internal void CommandCheckIssueDirect(Vector2I cell) => IssueMove(CellCenter(cell.X, cell.Y));
}
