using Godot;

namespace AkteEuropaReborn.Rendering;

/// <summary>
/// <b><c>--routenfenster-probe</c> — kann der SPIELER eine Route setzen?</b>
///
/// <para>Zu Stufe 2 der Materialroute (Simulation/TransportrouteFenster.cs).
/// Die Probe geht genau den Weg, den seine Hand ginge: Transporter anwaehlen →
/// Fenster auf → Gebaeude anklicken (erst Ziel, dann Quelle) → Start.</para>
///
/// <para><b>Die Messlatte, Schritt fuer Schritt</b> — jeder Schritt kann fuer
/// sich scheitern, und eine einzelne Zahl am Ende koennte nicht sagen, welcher:</para>
/// <list type="number">
///   <item>Das Fenster geht auf, und die Route steht dabei STILL (das Original
///   schickt beim Oeffnen Befehl 514).</item>
///   <item>Nach dem ersten Gebaeudeklick ist das ZIEL gesetzt und der Modus auf
///   »Von« umgesprungen.</item>
///   <item>Nach dem zweiten ist eine QUELLE gesetzt.</item>
///   <item>»Start« nimmt es an, die Route laeuft, das Fenster ist zu.</item>
///   <item>⭐ Und die Gegenprobe: »Start« OHNE Ziel muss abgelehnt werden —
///   sonst prueft die Messlatte nichts.</item>
/// </list>
/// </summary>
public partial class MapEntityLayer : Node2D
{
    private int _rfpStufe = -1;
    private float _rfpUhr;

    public void RoutenfensterProbeStart() { _rfpStufe = 0; _rfpUhr = 5f; }

    private void PollRoutenfensterProbe(float dt)
    {
        if (_rfpStufe < 0 || _nav == null) return;
        if (_rfpStufe == 0)
        {
            _rfpUhr -= dt;
            if (_rfpUhr > 0f) return;
            _rfpStufe = 1;
        }
        if (_rfpStufe != 1) return;
        _rfpStufe = -1;                       // die ganze Probe laeuft in EINEM Takt

        // --- Aufbau: ein eigener Transporter, ein Ziel, eine Quelle ---------
        int wagen = -1;
        for (int i = 0; i < _entities.Count; i++)
        {
            var e = _entities[i];
            if (e.Dead || e.IsBuilding || e.IsProp) continue;
            if (e.Owner != ViewPlayer || !IstTransporter(e)) continue;
            wagen = i; break;
        }
        Entity? ziel = null, quelle = null;
        foreach (var b in _entities)
        {
            if (!b.IsBuilding || b.Dead || b.Owner != ViewPlayer) continue;
            if (ziel == null && b.BType is 1 or 9) ziel = b;
            else if (quelle == null && b.BType is 2 or 3 or 4 or 10 or 15) quelle = b;
        }
        if (wagen < 0 || ziel == null || quelle == null)
        {
            GD.Print($"routenfenster-probe: nicht stellbar — Wagen {(wagen >= 0 ? "ja" : "NEIN")}, "
                   + $"Ziel {(ziel != null ? "ja" : "NEIN")}, Quelle {(quelle != null ? "ja" : "NEIN")}");
            return;
        }

        var u = _entities[wagen];
        var r0 = RouteVon(u) ?? RouteAnlegen(u);
        if (r0 != null) RouteStarten(u, r0);          // damit Schritt 1 etwas zu STOPPEN hat
        _sel.Clear(); _sel.Add(wagen);

        var sb = new System.Text.StringBuilder("routenfenster-probe\n");
        sb.Append($"  Wagen Platz {u.Slot} \"{LabelOf(u)}\"; Ziel Platz {ziel.Slot} "
                + $"({BuildingTypeName(ziel.BType)}), Quelle Platz {quelle.Slot} "
                + $"({BuildingTypeName(quelle.BType)})\n");

        // --- 1. Fenster auf -------------------------------------------------
        bool auf = RouteFensterOeffnen();
        var r = RouteFensterSatz();
        bool steht = r != null && !r.Gestartet;
        sb.Append($"  1. Fenster auf: {(auf ? "ja" : "NEIN")}, Route haelt an: "
                + $"{(steht ? "ja" : "NEIN")}, Modus {RouteWahlModus} (2 = Nach)\n");

        // --- 5. Gegenprobe zuerst: Start ohne Ziel muss scheitern ------------
        bool frueh = RouteStartKnopf();
        sb.Append($"  2. Start OHNE Ziel: {(frueh ? "ANGENOMMEN (falsch)" : "abgelehnt")} "
                + $"— »{RouteNote}«\n");

        // --- 2. Klick auf das Ziel ------------------------------------------
        PickOhneNebel = true;
        bool k1 = RouteKlickAufGebaeude(BodyRect(ziel).GetCenter());
        bool zielGesetzt = r != null && r.Ziel == ziel.Slot;
        sb.Append($"  3. Klick auf das Ziel: {(k1 ? "genommen" : "ABGEWIESEN")}, Ziel gesetzt "
                + $"{(zielGesetzt ? "ja" : "NEIN")}, Modus jetzt {RouteWahlModus} (1 = Von)\n");

        // --- 3. Klick auf die Quelle ----------------------------------------
        bool k2 = RouteKlickAufGebaeude(BodyRect(quelle).GetCenter());
        bool quelleGesetzt = false;
        if (r != null) foreach (int q in r.Quelle) if (q == quelle.Slot) quelleGesetzt = true;
        sb.Append($"  4. Klick auf die Quelle: {(k2 ? "genommen" : "ABGEWIESEN")}, Quelle gesetzt "
                + $"{(quelleGesetzt ? "ja" : "NEIN")}\n");

        // --- 4. Start --------------------------------------------------------
        bool start = RouteStartKnopf();
        bool laeuft = r != null && r.Gestartet;
        bool zu = RouteFensterWagen < 0;
        PickOhneNebel = false;
        sb.Append($"  5. Start: {(start ? "angenommen" : "ABGELEHNT")}, Route laeuft "
                + $"{(laeuft ? "ja" : "NEIN")}, Fenster zu {(zu ? "ja" : "NEIN")}\n");

        bool ok = auf && steht && !frueh && zielGesetzt && quelleGesetzt && start && laeuft && zu;
        sb.Append(ok ? "  WIE ERWARTET — der Spieler kann eine Route setzen"
                     : "  NICHT wie erwartet (siehe die Zeile, die NEIN sagt)");
        GD.Print(sb.ToString());
    }
}
