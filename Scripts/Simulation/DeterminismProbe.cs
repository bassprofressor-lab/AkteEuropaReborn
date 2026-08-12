namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation;

/// <summary>
/// DIE PRÜFSUMME ÜBER DEN GANZZAHLIGEN ZUSTAND.
///
/// <para>⚠ <b>Warum das hier steht und nicht in MapEntityLayer.cs.</b>
/// Dieselbe Begründung wie bei <c>Core/MainMenuCommandLine.cs</c>: an
/// <c>MapEntityLayer.cs</c> arbeitet gerade jemand anderes, und ein
/// Zusammenstoss in einer 12 800-Zeilen-Datei wäre teurer als diese Datei.
/// <c>MapEntityLayer</c> ist eine <c>partial class</c> — ihre Simulationsstücke
/// liegen ohnehin schon in <c>Scripts/Simulation/</c> (<c>Capture.cs</c>,
/// <c>RailFreight.cs</c>, <c>SkirmishAi.cs</c>). Dieses Stück kommt an dieselben
/// privaten Felder <c>_entities</c>, <c>_special</c> und <c>_money</c>.</para>
///
/// <para><b>Was drin ist und was NICHT.</b> Drin ist ausschliesslich, was das
/// Spiel als GANZE ZAHL führt. Draussen bleiben — mit Absicht — alle sieben
/// Uhren (<c>Cooldown</c>, <c>EconTimer</c>, <c>BuildTime</c>, <c>WaitTime</c>,
/// <c>DeadTime</c>, <c>FireUntil</c>, <c>FuelFrac</c>) und die
/// Zwischenposition <c>Pos</c> zwischen zwei Zellen. Nähme die Prüfsumme die
/// auf, misse sie das Rauschen der Bildzeit statt des Zustands — und wäre für
/// den Beweis wertlos, den sie führen soll. Die Zelle (<c>Col</c>/<c>Row</c>)
/// ist der Zustand, <c>Pos</c> ist nur, wie weit die Zeichnung dazwischen
/// steht.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary>
    /// Die Prüfsumme über den ganzen ganzzahligen Weltzustand.
    ///
    /// <para>Reihenfolge geht ein: <c>_entities</c> wird in Listenreihenfolge
    /// gelesen, und die Listenreihenfolge IST Teil des Zustands (die Indizes
    /// sind die Zielverweise <c>Target</c>, <c>Customer</c>, <c>_occupant</c>).
    /// Vertauschte Listen heissen auseinandergelaufen.</para>
    /// </summary>
    public ulong DeterminismChecksum()
    {
        var h = Determinism.Hasher.Start();

        foreach (var e in _entities)
        {
            if (e.IsProp) continue;              // Requisiten haben keinen Zustand
            h.Add(e.Slot);
            h.Add(e.Col);
            h.Add(e.Row);
            h.Add(e.Owner);
            h.Add(e.Hp);
            h.Add(e.Ammo);
            h.Add(e.Fuel);
            h.Add(e.StockW);
            h.Add(e.StockF);
            h.Add(e.StockS);
            h.Add(e.StockT);
            h.Add(e.State);
            h.Add(e.UpgradeStep);
            h.Add(e.ProdAccum);
            h.Add(e.CaptureProgress);
            // Diese vier stehen nicht in der Aufgabenliste, gehören aber
            // zweifelsfrei zum diskreten Zustand und sind das, was ein
            // Auseinanderlaufen zuerst sichtbar macht.
            h.Add(e.Dead);
            h.Add(e.Deposit);
            h.Add(e.Target);
            h.Add(e.Facing);
        }

        // Die Flugzeuge stehen in einer EIGENEN Liste und tragen denselben
        // diskreten Zustand. Sie MÜSSEN mit hinein: an ihnen hängt der schon
        // heute vorhandene Bildratenfehler (MapEntityLayer 12178/12179,
        // `CeilToInt(AmmoMax * dt / AirReloadSec)` — ein schnellerer Rechner
        // lädt schneller nach). Ohne sie könnte der Prüfstand genau den Fehler
        // nicht sehen, für den es ihn gibt.
        foreach (var a in _special)
        {
            h.Add(a.Slot);
            h.Add(a.Col);
            h.Add(a.Row);
            h.Add(a.Owner);
            h.Add(a.Hp);
            h.Add(a.Ammo);
            h.Add(a.Fuel);
            h.Add(a.Cargo);
            h.Add(a.Stored);
            h.Add(a.Dead);
            h.Add(a.Target);
            h.Add(a.Customer);
        }

        for (int p = 0; p < _money.Length; p++) h.Add(_money[p]);

        return h.Value;
    }

    /// <summary>Nur die Flugzeuge — damit der Bericht sagen kann, WO die Läufe
    /// auseinandergehen, nicht nur DASS sie es tun.</summary>
    public ulong DeterminismAircraftChecksum()
    {
        var h = Determinism.Hasher.Start();
        foreach (var a in _special)
        { h.Add(a.Slot); h.Add(a.Ammo); h.Add(a.Fuel); h.Add(a.Cargo); h.Add(a.Stored); h.Add(a.Dead); }
        return h.Value;
    }

    /// <summary>Nur der Boden — Einheiten, Gebäude, Geld.</summary>
    public ulong DeterminismGroundChecksum()
    {
        var h = Determinism.Hasher.Start();
        foreach (var e in _entities)
        {
            if (e.IsProp) continue;
            h.Add(e.Slot); h.Add(e.Col); h.Add(e.Row); h.Add(e.Owner); h.Add(e.Hp);
            h.Add(e.Ammo); h.Add(e.Fuel); h.Add(e.StockW); h.Add(e.StockF); h.Add(e.StockS);
            h.Add(e.StockT); h.Add(e.State); h.Add(e.UpgradeStep); h.Add(e.ProdAccum);
            h.Add(e.CaptureProgress); h.Add(e.Dead); h.Add(e.Deposit); h.Add(e.Target);
        }
        for (int p = 0; p < _money.Length; p++) h.Add(_money[p]);
        return h.Value;
    }

    /// <summary>
    /// DER VOLLE ZUSTAND ALS ZAHLENREIHE — für den Zwillings-Prüfstand.
    ///
    /// <para>Eine Prüfsumme sagt DASS zwei Läufe auseinandergehen. Sie sagt nie,
    /// WO. Dieser Auszug legt denselben Zustand, den die Prüfsumme frisst, als
    /// flache Reihe ab; der Prüfstand vergleicht sie Zahl für Zahl und kann
    /// darum »Einheit #212, Feld Hp: A=340 B=339« melden statt »Prüfsumme
    /// verschieden«. Der Unterschied zwischen den beiden Sätzen ist der
    /// Unterschied zwischen einem Befund und einer Beobachtung.</para>
    ///
    /// <para><b>Der Aufbau, und er muss zu
    /// <c>DeterminismTwinRunner.EntityFields</c> passen</b> — beide Listen
    /// stehen nebeneinander, weil eine Zahlenreihe ohne Feldnamen nicht lesbar
    /// ist und Feldnamen ohne Zahlenreihe nichts messen:</para>
    /// <list type="number">
    ///   <item>Kopf: Anzahl Einheiten, Anzahl Flugzeuge, Anzahl Spieler. Damit
    ///     fällt ein Unterschied in der ANZAHL sofort auf — und das ist der
    ///     schlimmste Fall, weil danach alle folgenden Zahlen gegeneinander
    ///     verschoben stünden.</item>
    ///   <item>Je Einheit die 23 Felder aus <c>EntityFields</c>.</item>
    ///   <item>Je Flugzeug die 12 Felder aus <c>AircraftFields</c>.</item>
    ///   <item>Die Kontostände.</item>
    /// </list>
    ///
    /// <para>⚠ <b>Drei Felder mehr als die Prüfsumme</b>: <c>PathIdx</c>,
    /// <c>PathLen</c> und das Wegziel. Sie stehen nicht in
    /// <see cref="DeterminismChecksum"/> — dort sind sie auch nicht nötig, denn
    /// ein anderer Weg zeigt sich spätestens eine Zelle später in Col/Row.
    /// Hier sind sie es sehr wohl: sie sagen, ob zwei Simulationen dieselbe
    /// ROUTE gewählt haben, und die Wegsuche war bis zum 12.08.2026 die
    /// grösste Fliesskomma-Baustelle im Zustand (siehe NavGrid.FindPath).
    /// Ein Auseinanderlaufen, das in PathIdx anfängt, ist ein anderer Befund
    /// als eines, das in Hp anfängt.</para>
    ///
    /// <para><c>Pos</c> und die sieben Uhren bleiben AUCH hier draussen, aus
    /// demselben Grund wie bei der Prüfsumme: sie sind Fliesskomma und messen
    /// das Rauschen der Bildzeit statt des Zustands.</para>
    /// </summary>
    public void DeterminismSnapshot(List<long> into)
    {
        into.Clear();

        int nE = 0;
        foreach (var e in _entities) if (!e.IsProp) nE++;
        into.Add(nE);
        into.Add(_special.Count);
        into.Add(_money.Length);

        foreach (var e in _entities)
        {
            if (e.IsProp) continue;
            into.Add(e.Slot); into.Add(e.Col); into.Add(e.Row); into.Add(e.Owner);
            into.Add(e.Hp); into.Add(e.Ammo); into.Add(e.Fuel);
            into.Add(e.StockW); into.Add(e.StockF); into.Add(e.StockS); into.Add(e.StockT);
            into.Add(e.State); into.Add(e.UpgradeStep); into.Add(e.ProdAccum);
            into.Add(e.CaptureProgress); into.Add(e.Dead ? 1 : 0); into.Add(e.Deposit);
            into.Add(e.Target); into.Add(e.Facing);
            into.Add(e.PathIdx);
            into.Add(e.Path?.Count ?? -1);
            into.Add(e.Goal.X); into.Add(e.Goal.Y);
        }

        foreach (var a in _special)
        {
            into.Add(a.Slot); into.Add(a.Col); into.Add(a.Row); into.Add(a.Owner);
            into.Add(a.Hp); into.Add(a.Ammo); into.Add(a.Fuel); into.Add(a.Cargo);
            into.Add(a.Stored ? 1 : 0); into.Add(a.Dead ? 1 : 0);
            into.Add(a.Target); into.Add(a.Customer);
        }

        for (int p = 0; p < _money.Length; p++) into.Add(_money[p]);
    }

    /// <summary>
    /// DIE GEGENPROBE: einer lebenden Einheit genau EINEN Trefferpunkt nehmen.
    ///
    /// <para>Kleiner lässt sich der ganzzahlige Zustand nicht verändern. Wenn
    /// der Prüfstand diese eine Zahl im Takt ihres Entstehens findet, findet er
    /// jede echte Abweichung — und erst dann ist ein »kein Unterschied über 600
    /// Takte« eine Aussage und nicht bloss ein Schweigen.</para>
    ///
    /// <para>Gibt den Slot der getroffenen Einheit zurück, oder −1, wenn keine
    /// lebende Einheit da war. Die −1 muss der Prüfstand melden: eine
    /// Gegenprobe, die ins Leere ging, belegt nichts.</para>
    /// </summary>
    public int DeterminismPoisonOneHp()
    {
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead || e.Hp <= 1) continue;
            e.Hp--;
            return e.Slot;
        }
        return -1;
    }

    /// <summary>Zwei Summenwerte, die eine Abweichung LESBAR machen: wie viele
    /// Trefferpunkte und wie viele Teile insgesamt auf der Karte stehen.</summary>
    public (int Hp, int Stock, int Ammo, int Alive) DeterminismTotals()
    {
        int hp = 0, st = 0, am = 0, al = 0;
        foreach (var e in _entities)
        {
            if (e.IsProp || e.Dead) continue;
            al++; hp += e.Hp; am += e.Ammo;
            st += e.StockW + e.StockF + e.StockS + e.StockT;
        }
        foreach (var a in _special) { if (!a.Dead) { al++; hp += a.Hp; am += a.Ammo; } }
        return (hp, st, am, al);
    }

    /// <summary>
    /// Den Zufall dieser Partie keimen. Wird vom Prüfstand beim ersten Takt
    /// gerufen, sobald die Karte steht.
    ///
    /// <para><c>_rng</c> ist <c>readonly</c> — das verbietet, das FELD neu zu
    /// setzen, nicht, dem Objekt seinen Keim zu geben. Damit wird
    /// <c>MapEntityLayer.cs:7804</c> (Produktionschance) reproduzierbar, ohne
    /// dass eine fremde Datei angefasst wird. Dasselbe für <c>GD.Randi()</c>
    /// über <c>GD.Seed</c> in <see cref="Determinism.NewMap"/>.</para>
    ///
    /// <para>⚠ Das ist eine KRÜCKE, kein Umbau: beide Quellen werden auch
    /// ausserhalb der Spielwelt angefasst (Klang, Anzeige), und die läuft auf
    /// Bildzeit. Für das Netzspiel müssen die vier Fundstellen auf
    /// <see cref="Determinism.Roll"/> umgestellt werden — die genauen
    /// Änderungen stehen im Bericht.</para>
    /// </summary>
    public void DeterminismSeed(string mapName)
    {
        if (Determinism.Poisoned)
        {
            // Gegenprobe: KEIN Keim. Genau der Zustand von vor dieser Sitzung.
            GD.Randomize();
            GD.Print($"determinism: VERGIFTET — ungekeimter Zufall (Gegenprobe), Karte {mapName}");
            return;
        }
        // ⚠ Hier wird NICHT noch einmal gekeimt. Der Keim steht seit
        // NavGrid.Build (Determinism.NewMap), und das MUSS die einzige Stelle
        // bleiben: ein zweiter Aufruf mit einer anderen Schreibweise des
        // Kartennamens gäbe einen anderen Keim. Genau das ist am 12.08.2026
        // passiert — Build keimte aus meta["mission"], diese Zeile noch einmal
        // aus dem GROSSGESCHRIEBENEN _mission, und die beiden Keime waren
        // 2819280086 und 2524234454.
        // ⚠ 15.08.2026 — HIER STAND `_rng.Seed = Determinism.Seed;`, die Krücke
        // für den zufällig gekeimten Godot-Würfel der Produktionschance. Sie ist
        // WEG, weil das Feld weg ist: die vier Fundstellen sind auf
        // Determinism.Roll umgestellt (Schadensrechnung, Produktionschance,
        // Trümmerbild). Ein Keim für GD.Randi bleibt in Determinism.NewMap, denn
        // der Motor würfelt auch ausserhalb der Spielwelt.
        GD.Print($"determinism: Keim {Determinism.Seed} (Karte \"{mapName}\") " +
                 "— GD.Seed + DeterministicRng; die Spielwelt wuerfelt nur noch " +
                 "ueber Determinism.Roll");
    }
}
