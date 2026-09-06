using System.Collections.Generic;
using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <c>--einfahrt-fremd-probe</c> — <b>faehrt eine Einheit in ein Gebaeude, das
/// noch dem GEGNER gehoert?</b>
///
/// <para>Angelegt am 06.09.2026 auf seine Meldung: »solange ein Gebaeude noch im
/// fremden Besitz ist, kann man noch nicht in die Tuer einfahren. Aktuell
/// koennen wir das. Im Original wird das Gebaeude sozusagen genau davor
/// eingenommen, bei uns faehrt eine Einheit schon rein, wobei es noch dem
/// Gegner gehoert.«</para>
///
/// <para>⚠ <b>Der Lauf beweist nichts ueber das ORIGINAL</b> — er misst nur, was
/// UNSER Bau tut. Genau das ist der Punkt: im Kopf von
/// <c>Simulation/Einfahrt.cs</c> steht seit dem Bau, dass der Besitzerabgleich
/// UNSERER ist (der Arm der Basis <c>0x43D5D9..0x43D65B</c> hat keinen, der
/// zweite Zweig <c>0x43D6C0..0x43D71F</c> prueft dagegen
/// <c>byte[+0x05] == id/1000</c>). Bevor daraus Code wird, muss erst dastehen,
/// ob unser Tor ueberhaupt GREIFT — seine Meldung sagt nein, der Code sagt ja,
/// und eine von beiden Aussagen ist falsch.</para>
///
/// <para>Gemessen werden beide Richtungen: die FREMDE Basis darf die Einheit
/// nicht schlucken, die EIGENE muss es tun. Ohne die zweite Haelfte waere
/// »faehrt nicht ein« auch dann gruen, wenn die Einfahrt ueberhaupt nicht
/// laeuft.</para>
///
/// <para>⚠⚠ <b>STAND 06.09.2026: DIESER LAUF IST NOCH KEINE MESSUNG.</b> Die
/// Gegenprobe faellt durch — auch in das EIGENE Gebaeude faehrt hier niemand
/// ein. Damit sagt die erste Zeile (»fremd bleibt draussen«) genau nichts: sie
/// waere auch dann gruen, wenn die Einfahrt gar nicht laeuft, und das tut sie
/// offenbar. Der Aufbau ist zu duenn — die echte Einfahrt braucht mehr als
/// »Einheit auf die Tuerzelle setzen« (der Tuerzustand laeuft ueber eine eigene
/// Schleife, und <c>--einfahrt-check</c> raeumt dafuer zusaetzlich die
/// Belegung in <c>_nav</c> ab). <b>Bis das steht, wird an der Einfahrt nichts
/// geaendert.</b> Der Lauf bleibt hier, weil ein durchgefallener Pruefstand
/// mehr wert ist als eine Behauptung ohne Zahl.</para>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    /// <summary>Ergebniszeile des Laufs; <see cref="EinfahrtFremdProbe"/> baut
    /// sie, <c>MapViewer</c> druckt sie.</summary>
    public string EinfahrtFremdProbe()
    {
        var sb = new System.Text.StringBuilder("einfahrt-fremd-probe\n");

        // Ein Gebaeude mit Tuer, in das ueberhaupt eingefahren werden kann.
        int bi = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var b = _entities[i];
            if (!b.IsBuilding || b.IsProp || b.Dead) continue;
            if (!GarageTyp(b.BType) || b.Built == 0 || b.DoorCells.Count == 0) continue;
            bi = i; break;
        }
        if (bi < 0) return sb.Append("  kein Gebaeude mit Tuer auf dieser Karte").ToString();

        int ui = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var u = _entities[i];
            if (u.IsBuilding || u.IsProp || u.Dead || !u.Mobile) continue;
            ui = i; break;
        }
        if (ui < 0) return sb.Append("  keine fahrende Einheit auf dieser Karte").ToString();

        var geb = _entities[bi];
        var eh = _entities[ui];

        // Die Einheit gehoert dem Betrachter und steht ohne Auftrag in der Tuer.
        void AnDieTuer()
        {
            eh.Owner = eh.Team = ViewPlayer;
            eh.Ukol = UkolFrei;
            eh.Path = null; eh.Orders.Clear(); eh.Target = -1;
            eh.Col = geb.Col + geb.DoorCol;
            eh.Row = geb.Row + geb.DoorRow;
        }

        // Sechs Takte reichen: angemeldet wird im ersten, geschluckt im
        // naechsten (die Tuerschleife braucht einen Takt je Zustand).
        bool Untergestellt6()
        {
            for (int t = 0; t < 6; t++) PollEinfahrt();
            return Untergestellt(eh) || geb.Garage.Contains(eh) || geb.Depot.Contains(ui);
        }

        // ---- 1. das Gebaeude gehoert dem GEGNER ----------------------------
        int fremd = ViewPlayer == 0 ? 1 : 0;
        geb.Owner = geb.Team = fremd;
        geb.CaptureProgress = 0; geb.Intruder = -1; geb.ShownOwner = fremd;
        AnDieTuer();
        bool drinFremd = Untergestellt6();
        sb.AppendLine($"  Gebaeude P{fremd} (fremd), eigene Einheit in der Tuer: "
                    + $"UKOL {eh.Ukol}, untergestellt {(drinFremd ? "JA" : "nein")}: "
                    + $"{(drinFremd ? "FAEHRT EIN — seine Meldung stimmt" : "bleibt draussen")}");

        // ---- 2. Gegenprobe: dasselbe Gebaeude, jetzt EIGEN ------------------
        // Ohne sie hiesse »bleibt draussen« vielleicht nur, dass die Einfahrt
        // gar nicht laeuft.
        geb.Garage.Clear(); geb.Depot.Clear();
        geb.Owner = geb.Team = geb.ShownOwner = ViewPlayer;
        AnDieTuer();
        bool drinEigen = Untergestellt6();
        sb.AppendLine($"  Gegenprobe, dasselbe Gebaeude jetzt EIGEN: "
                    + $"UKOL {eh.Ukol}, untergestellt {(drinEigen ? "ja" : "NEIN")}: "
                    + $"{(drinEigen ? "faehrt ein, die Mechanik laeuft" : "FAEHRT NICHT EIN — der Lauf sagt nichts")}");

        sb.Append(!drinFremd && drinEigen
                  ? "  BESTANDEN (fremd bleibt draussen, eigen faehrt ein)"
                  : "  DURCHGEFALLEN");
        return sb.ToString();
    }
}
