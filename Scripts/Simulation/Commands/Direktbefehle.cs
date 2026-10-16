namespace AkteEuropaReborn.Rendering;

using System.Collections.Generic;
using Godot;
using AkteEuropaReborn.Simulation.Commands;

/// <summary>
/// ⭐⭐ <b>DIE SIEBEN BEFEHLE, DIE AM RING VORBEI GINGEN</b> (16.10.2026,
/// bug-530, berichte/desync-opus.md).
///
/// <para>Anhalten, Eingraben, Bau abbrechen, Flugzeugstart, Forschung, Ausbau
/// über die Tasten V/C und die Spielgeschwindigkeit schrieben bis heute
/// UNMITTELBAR in den Zustand. Im Netzspiel sah das nur die klickende Maschine:
/// sie lief mit anderem Kontostand, anderer Forschung, anderen Flugzeugen
/// weiter, und der Lockstep brach im nächsten Abdruck. Jetzt geht jeder davon
/// als Satz in den Ring (über <see cref="Emit"/>, also im Netz erst über die
/// Leitung) und wirkt am Taktanfang — im Einzelspieler einen Takt später, genau
/// wie <see cref="PostMove"/>.</para>
///
/// <para><b>Die Nummern des Originals</b> (gelesen 16.10.2026, Befehlskladde
/// <c>word[0xB8A3D8]</c>, Verteiler 0x4C2280):</para>
/// <code>
///   Anhalten       12   P1 = Einheit        Absender 0x433768 / Gruppe 0x4353B1,
///                                           Behandler 0x4C2E87 (F 0x4C29FB)
///   Eingraben      26   P1 = Einheit, P2 = 1 ein / 0 aus
///                                           Absender 0x448887 / 0x4488BF,
///                                           Behandler 0x4C35E1 (F 0x4C315B)
///   Geschwindigkeit 989 P1 = neuer Wert (1..3)  Absender 0x413A3E (+) /
///                                           0x413A81 (−), Behandler 0x4C4448
///   Ausbau     509/510/515/516/536          schon gebaut (PostBuildingJob)
///   Bau abbrechen   —   das Original hat keinen (Einheiten ohne Bauzeit) —
///                       UNSERE Nummer 2004
///   Forschung       —   das Original kauft ÖRTLICH (0x44A87D -> 0x4AABD0) und
///                       schickt nur den Preis (528) — UNSERE Nummer 2005
///   Alle starten    —   das Original startet je Flugzeug mit 502 (P2 Modus
///                       6/7/1); unser Knopf »alle zum nächsten Feind« hat
///                       keinen Gegenpart — UNSERE Nummer 2006
/// </code>
///
/// <para>⚠ <b>Die Auswahl ist ein HashSet.</b> Die Sätze gehen darum in
/// fester Reihenfolge hinaus, nach Platz (<see cref="AuswahlNachPlatz"/>). Im
/// Netz reist die Reihenfolge ohnehin im Paket mit; sortiert ist sie trotzdem,
/// damit zwei Läufe derselben Eingabe denselben Satzstrom geben.</para>
///
/// <para>Gegenschalter <c>--direktbefehle-alt</c>: alle sieben wieder
/// unmittelbar. Das Nullmodell — im Zwei-Prozess-Lauf laufen die Prüfsummen
/// damit auseinander, und genau daran sieht man, dass der Prüfstand es
/// sieht.</para>
/// </summary>
public partial class MapEntityLayer
{
    /// <summary><c>--direktbefehle-alt</c> — siehe Kopf.</summary>
    public static bool DirektbefehleAlt;

    /// <summary>Die Auswahl in fester Reihenfolge — aufsteigend nach Platz in
    /// der Einheitenliste.</summary>
    private List<int> AuswahlNachPlatz()
    {
        var l = new List<int>(_sel);
        l.Sort();
        return l;
    }

    /// <summary>Wie viele Sätze je Befehlsnummer der Behandler angenommen hat
    /// — für den Netzprüfstand (»hat der Satz überhaupt gewirkt?«).</summary>
    public readonly SortedDictionary<int, int> AngenommenJeBefehl = new();

    // ================= DIE ABSENDER ==========================================

    /// <summary>Anhalten (Taste X, Befehlsleiste, Einheitenmenü) — Befehl 12
    /// je gewählter Einheit.</summary>
    public int PostAnhalten()
    {
        if (DirektbefehleAlt) { StopSelected(); return 0; }
        int n = PostStop();
        _order = "stop";
        QueueRedraw();
        return n;
    }

    /// <summary>Eingraben/Ausgraben (Taste E, Befehlsleiste, Einheitenmenü) —
    /// Befehl 26. ⚠ Der Absender entscheidet, OB ein- oder ausgegraben wird
    /// (P2), aus dem Stand, den der Spieler sieht — genau wie das Original zwei
    /// Menüzeilen mit P2 = 1 und P2 = 0 hat. Ein Umschalter im Behandler würde
    /// zwei rasch folgende Klicks im Netz verschieden zählen.</summary>
    public int PostEingraben()
    {
        if (DirektbefehleAlt) { ToggleDigIn(); return 0; }
        int n = 0;
        foreach (int i in AuswahlNachPlatz())
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (!e.Mobile || e.Dead) continue;
            if (Emit(CommandRecord.Make(CommandOp.Eingraben, (byte)ViewPlayer,
                                        (short)i, (short)(e.DugIn ? 0 : 1)))) n++;
        }
        _order = n > 0 ? $"{OrderName(OrdDigIn)} / {OrderName(OrdDigOut)}: {n}" : "";
        return n;
    }

    /// <summary>Bau abbrechen (Umschalt+B, Basisfenster) — unsere 2004 je
    /// gewählter Fabrik.</summary>
    public int PostBauAbbrechen()
    {
        if (DirektbefehleAlt) { CancelBuild(); return 0; }
        int n = 0;
        foreach (int i in AuswahlNachPlatz())
        {
            if (i < 0 || i >= _entities.Count) continue;
            var e = _entities[i];
            if (!IsUnitPlant(e) || e.Dead) continue;
            if (Emit(CommandRecord.Make(CommandOp.OursCancelBuild, (byte)ViewPlayer, (short)i))) n++;
        }
        if (n == 0) _order = "nichts abzubrechen";
        return n;
    }

    /// <summary>»Alle starten« (Taste Y, Flughafen- und Basisfenster) — unsere
    /// 2006, P1 = der Spieler.</summary>
    public int PostAlleStarten(int owner)
    {
        if (DirektbefehleAlt) return LaunchAircraft(owner);
        return Emit(CommandRecord.Make(CommandOp.OursLaunchAll, (byte)ViewPlayer, (short)owner)) ? 1 : 0;
    }

    /// <summary>Forschung kaufen (Taste O, Basisfenster) — unsere 2005,
    /// P1 = Basis, P2 = Zeile des Angebots, P3 = der Preis, den der Spieler
    /// gesehen hat. ⚠ Der Preis ist die Wächterzahl: steht im Behandler an der
    /// Zeile ein anderes Angebot (die Liste hat sich zwischen Klick und Takt
    /// verschoben), wird der Satz verworfen statt etwas anderes zu kaufen.</summary>
    public int PostForschung(int zeile)
    {
        if (DirektbefehleAlt || ForschungAlt) { ForschungAusPanel(zeile); return 0; }
        AimAtPanelBuilding();
        foreach (int i in AuswahlNachPlatz())
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.BType != 1 || e.Dead) continue;
            var angebot = ForschungAngebote(Mathf.Clamp(e.Owner, 0, 7));
            if (zeile < 0 || zeile >= angebot.Count)
            { _order = "Forschung — zuerst eine Zeile waehlen."; return 0; }
            int preis = angebot[zeile].Preis;
            return Emit(CommandRecord.Make(CommandOp.OursResearch, (byte)ViewPlayer,
                                           (short)i, (short)zeile,
                                           (short)Mathf.Clamp(preis, short.MinValue, short.MaxValue))) ? 1 : 0;
        }
        _order = "Forschung — keine Basis gewählt.";
        return 0;
    }

    /// <summary>Lagerausbau/Produktionserweiterung über die Tasten V/C — das
    /// Fenster nimmt schon <see cref="PostUpgradeFromPanel"/>; die Tasten
    /// gingen bis heute an <see cref="StartUpgrade"/> vorbei.</summary>
    public int PostAusbauTaste(bool storage)
    {
        if (DirektbefehleAlt) { StartUpgrade(storage); return 0; }
        return PostBuildingJob(storage ? BuildingJob.ExpandStore : BuildingJob.ExpandProd);
    }

    /// <summary>Die Spielgeschwindigkeit (Ziffernblock +/−) — Befehl 989,
    /// P1 = der NEUE Wert, gedeckelt wie im Original (+ nicht über 3 @0x413A4C,
    /// − nicht unter 1 @0x413A8F). Der Absender rechnet den Wert aus dem, was
    /// er sieht; der Behandler setzt ihn nur.</summary>
    public int PostGeschwindigkeit(int delta)
    {
        int neu = Mathf.Clamp(GameSpeed + delta, 1, 3);
        if (DirektbefehleAlt)
        {
            GameSpeed = neu;
            GD.Print($"Geschwindigkeit {GameSpeed} ({50 * GameSpeed} Takte/s)");
            return 0;
        }
        return Emit(CommandRecord.Make(CommandOp.Geschwindigkeit, (byte)ViewPlayer, (short)neu)) ? 1 : 0;
    }

    /// <summary>Für den Netzprüfstand: das erste eigene Gebäude der Art
    /// (1 = Basis, 2 = Fabrik 2/3/4) anwählen, wie ein Klick es täte.</summary>
    internal bool NetzProbeWaehleGebaeude(int owner, int art)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (!e.IsBuilding || e.Dead || e.Owner != owner) continue;
            if (art == 1 ? e.BType != 1 : e.BType is not (2 or 3 or 4)) continue;
            _sel.Clear(); _sel.Add(i); _selected = i;
            return true;
        }
        return false;
    }

    // ================= DIE BEHANDLER =========================================

    /// <summary>26, Eingraben. P1 = Einheit, P2 = 1 ein / 0 aus. ⚠ Unser
    /// Eingraben ist weiter der Sofortumschalter (halber Schaden, unsere
    /// Regel); die Bauphase des Originals (UKOL 22/23, Warteschlange 0x834B70)
    /// ist NICHT gebaut — der Satz ist es.</summary>
    private bool ApplyEingraben(in CommandRecord c)
    {
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (!e.Mobile || e.Dead) return false;
        bool ein = c.P2 != 0;
        if (e.DugIn == ein) return false;          // schon so — nichts zu tun
        DigInSetzen(i, ein);
        if (_selected == i || _sel.Contains(i)) UpdatePanel();
        QueueRedraw();
        return true;
    }

    /// <summary>2004, Bau abbrechen. P1 = die Fabrik.</summary>
    private bool ApplyBauAbbrechen(in CommandRecord c)
    {
        if (_designs == null) return false;
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        bool ok = CancelBuildAt(i);
        if (ok) { UpdatePanel(); QueueRedraw(); }
        return ok;
    }

    /// <summary>2005, Forschung. P1 = Basis, P2 = Zeile, P3 = Preis beim
    /// Klick.</summary>
    private bool ApplyForschung(in CommandRecord c)
    {
        if (ForschungAlt) return false;
        int i = c.P1;
        if (i < 0 || i >= _entities.Count) return false;
        var e = _entities[i];
        if (!e.IsBuilding || e.BType != 1 || e.Dead) return false;
        var angebot = ForschungAngebote(Mathf.Clamp(e.Owner, 0, 7));
        if (c.P2 < 0 || c.P2 >= angebot.Count) return false;
        if (Mathf.Clamp(angebot[c.P2].Preis, short.MinValue, short.MaxValue) != c.P3) return false;
        return ForschungKaufen(e, c.P2);
    }

    /// <summary>2006, alle Flugzeuge des Spielers P1 starten. ⚠ P1 ist KEINE
    /// Einheit, darum steht der Satz vor der Eigentümerschranke und prüft hier
    /// selbst: nur der Spieler darf seine eigene Flotte starten.</summary>
    private bool ApplyAlleStarten(in CommandRecord c)
    {
        if (c.P1 != c.Player) return false;
        return LaunchAircraft(c.P1) > 0;
    }

    /// <summary>989, Geschwindigkeit setzen — @0x4C4448: <c>byte[0x4FA23C] :=
    /// P1</c>. Wir klemmen auf 1..3 (der Absender des Originals tut es, der
    /// Behandler nicht; ein Satz aus dem Netz darf aber keine 0 = Pause und
    /// keine 200 setzen).</summary>
    private bool ApplyGeschwindigkeit(in CommandRecord c)
    {
        int neu = Mathf.Clamp(c.P1, 1, 3);
        GameSpeed = neu;
        GD.Print($"Geschwindigkeit {GameSpeed} ({50 * GameSpeed} Takte/s)");
        return true;
    }
}
